using System;
using UnityEngine;

namespace Shared.Audio
{
    /// <summary>
    /// Sample level procedural synthesis helpers. Everything works on plain mono
    /// <c>float[]</c> buffers at <see cref="SampleRate"/> Hz, so recipes stay testable
    /// outside play mode (the editor dump tool renders them straight to WAV).
    /// No external audio assets are involved anywhere in this pipeline.
    /// </summary>
    public static class SfxSynth
    {
        /// <summary>Sample rate every buffer in this library is rendered at.</summary>
        public const int SampleRate = 44100;

        const float TwoPi = 6.28318530718f;

        // ------------------------------------------------------------------ rng

        /// <summary>Deterministic xorshift RNG so a recipe renders identically every run.</summary>
        public sealed class Rng
        {
            uint _s;

            /// <summary>Creates a generator seeded from <paramref name="seed"/>; the same seed always yields the same noise.</summary>
            public Rng(int seed)
            {
                unchecked { _s = (uint)(seed * 2654435761u) ^ 0x9E3779B9u; }
                if (_s == 0u) _s = 1u;
            }

            /// <summary>Next uniform value in [0,1).</summary>
            public float Next01()
            {
                _s ^= _s << 13;
                _s ^= _s >> 17;
                _s ^= _s << 5;
                return (_s & 0xFFFFFFu) / 16777216f;
            }

            /// <summary>Next uniform value in [min,max).</summary>
            public float Range(float min, float max) { return min + (max - min) * Next01(); }

            /// <summary>Next uniform value in [-1,1).</summary>
            public float Bipolar() { return Next01() * 2f - 1f; }
        }

        // ----------------------------------------------------------- allocation

        /// <summary>Sample count for a duration in seconds (at least one sample).</summary>
        public static int Samples(float seconds) { return Mathf.Max(1, Mathf.RoundToInt(seconds * SampleRate)); }

        /// <summary>Duration in seconds of a buffer.</summary>
        public static float Duration(float[] buf) { return buf.Length / (float)SampleRate; }

        /// <summary>Silent buffer of the given duration.</summary>
        public static float[] Alloc(float seconds) { return new float[Samples(seconds)]; }

        // ---------------------------------------------------------- oscillators

        /// <summary>Constant pitch sine.</summary>
        public static float[] Sine(float seconds, float freq, float amp = 1f)
        {
            return SineCurve(seconds, delegate { return freq; }, amp);
        }

        /// <summary>Constant pitch triangle wave (softer harmonics than a square, good for glass-like tones).</summary>
        public static float[] Triangle(float seconds, float freq, float amp = 1f)
        {
            var buf = Alloc(seconds);
            float phase = 0f;
            float step = freq / SampleRate;
            for (int i = 0; i < buf.Length; i++)
            {
                phase += step; if (phase >= 1f) phase -= 1f;
                float t = phase < 0.5f ? phase * 4f - 1f : 3f - phase * 4f;
                buf[i] = t * amp;
            }
            return buf;
        }

        /// <summary>Constant pitch square wave.</summary>
        public static float[] Square(float seconds, float freq, float amp = 1f)
        {
            var buf = Alloc(seconds);
            float phase = 0f;
            float step = freq / SampleRate;
            for (int i = 0; i < buf.Length; i++)
            {
                phase += step; if (phase >= 1f) phase -= 1f;
                buf[i] = (phase < 0.5f ? amp : -amp);
            }
            return buf;
        }

        /// <summary>Flat spectrum noise.</summary>
        public static float[] WhiteNoise(float seconds, float amp, Rng rng)
        {
            var buf = Alloc(seconds);
            for (int i = 0; i < buf.Length; i++) buf[i] = rng.Bipolar() * amp;
            return buf;
        }

        /// <summary>1/f noise (Paul Kellet filter). Warmer than white; the base for crowd rumble.</summary>
        public static float[] PinkNoise(float seconds, float amp, Rng rng)
        {
            var buf = Alloc(seconds);
            float b0 = 0f, b1 = 0f, b2 = 0f, b3 = 0f, b4 = 0f, b5 = 0f, b6 = 0f;
            for (int i = 0; i < buf.Length; i++)
            {
                float w = rng.Bipolar();
                b0 = 0.99886f * b0 + w * 0.0555179f;
                b1 = 0.99332f * b1 + w * 0.0750759f;
                b2 = 0.96900f * b2 + w * 0.1538520f;
                b3 = 0.86650f * b3 + w * 0.3104856f;
                b4 = 0.55000f * b4 + w * 0.5329522f;
                b5 = -0.7616f * b5 - w * 0.0168980f;
                float pink = b0 + b1 + b2 + b3 + b4 + b5 + b6 + w * 0.5362f;
                b6 = w * 0.115926f;
                buf[i] = pink * 0.11f * amp;
            }
            return buf;
        }

        /// <summary>
        /// Phase continuous sine following an arbitrary frequency curve. <paramref name="freqAt"/>
        /// receives normalised position 0..1 and returns Hz. Phase is integrated rather than
        /// recomputed, so pitch can move without producing a click.
        /// </summary>
        public static float[] SineCurve(float seconds, Func<float, float> freqAt, float amp = 1f)
        {
            var buf = Alloc(seconds);
            float phase = 0f;
            float inv = 1f / Mathf.Max(1, buf.Length - 1);
            for (int i = 0; i < buf.Length; i++)
            {
                float f = freqAt(i * inv);
                phase += TwoPi * f / SampleRate;
                if (phase > TwoPi) phase -= TwoPi;
                buf[i] = Mathf.Sin(phase) * amp;
            }
            return buf;
        }

        /// <summary>
        /// Phase continuous pitch drop/rise from <paramref name="f0"/> to <paramref name="f1"/>.
        /// <paramref name="curve"/> &gt; 1 front loads the movement (fast drop then settle).
        /// </summary>
        public static float[] SineSweep(float seconds, float f0, float f1, float amp = 1f, float curve = 1f)
        {
            return SineCurve(seconds, t => Mathf.Lerp(f0, f1, Mathf.Pow(Mathf.Clamp01(t), curve)), amp);
        }

        // ------------------------------------------------------------ envelopes

        /// <summary>
        /// Classic ADSR shape as a multiplier curve of <paramref name="length"/> samples.
        /// Segment times are scaled down proportionally if they exceed the buffer.
        /// </summary>
        public static float[] Envelope(int length, float attack, float decay, float sustain, float release)
        {
            var env = new float[Mathf.Max(1, length)];
            float total = env.Length / (float)SampleRate;
            float sum = attack + decay + release;
            if (sum > total && sum > 0f)
            {
                float k = total / sum;
                attack *= k; decay *= k; release *= k;
            }
            int a = Mathf.RoundToInt(attack * SampleRate);
            int d = Mathf.RoundToInt(decay * SampleRate);
            int r = Mathf.RoundToInt(release * SampleRate);
            int relStart = Mathf.Max(0, env.Length - r);
            for (int i = 0; i < env.Length; i++)
            {
                float v;
                if (i < a) v = a > 0 ? i / (float)a : 1f;
                else if (i < a + d) v = d > 0 ? Mathf.Lerp(1f, sustain, (i - a) / (float)d) : sustain;
                else v = sustain;
                if (i >= relStart && r > 0) v *= 1f - (i - relStart) / (float)r;
                env[i] = v;
            }
            return env;
        }

        /// <summary>Applies an ADSR envelope in place.</summary>
        public static void ApplyEnvelope(float[] buf, float attack, float decay, float sustain, float release)
        {
            var env = Envelope(buf.Length, attack, decay, sustain, release);
            for (int i = 0; i < buf.Length; i++) buf[i] *= env[i];
        }

        /// <summary>Exponential decay curve with time constant <paramref name="tau"/> seconds.</summary>
        public static float[] ExpDecay(int length, float tau)
        {
            var env = new float[Mathf.Max(1, length)];
            float k = -1f / Mathf.Max(1e-5f, tau * SampleRate);
            for (int i = 0; i < env.Length; i++) env[i] = Mathf.Exp(i * k);
            return env;
        }

        /// <summary>Applies an exponential decay in place (percussive shape).</summary>
        public static void ApplyExpDecay(float[] buf, float tau, float attackSeconds = 0.001f)
        {
            int a = Mathf.Max(1, Mathf.RoundToInt(attackSeconds * SampleRate));
            float k = -1f / Mathf.Max(1e-5f, tau * SampleRate);
            for (int i = 0; i < buf.Length; i++)
            {
                float atk = i < a ? i / (float)a : 1f;
                buf[i] *= atk * Mathf.Exp(i * k);
            }
        }

        // -------------------------------------------------------------- filters
        //
        // These are one pole sections (6 dB/oct). A single pole is far too gentle to hold a
        // stated band: noise low passed at 6 kHz still carries most of 6-20 kHz. Passing the
        // buffer through the same section several times (poles: 2..4) steepens the slope, and
        // the pole frequency is pre-compensated so the -3 dB point stays where the recipe asked.

        /// <summary>Low pass with a fixed cutoff; <paramref name="poles"/> cascades the section for a steeper slope.</summary>
        public static void LowPass(float[] buf, float cutoffHz, int poles = 1)
        {
            LowPass(buf, delegate { return cutoffHz; }, poles);
        }

        /// <summary>Low pass whose cutoff glides (logarithmically) from start to end across the buffer.</summary>
        public static void LowPass(float[] buf, float cutoffStart, float cutoffEnd, int poles = 1)
        {
            LowPass(buf, t => LogLerp(cutoffStart, cutoffEnd, t), poles);
        }

        /// <summary>Low pass with an arbitrary cutoff curve; <paramref name="cutoffAt"/> takes 0..1 and returns Hz.</summary>
        public static void LowPass(float[] buf, Func<float, float> cutoffAt, int poles = 1)
        {
            int n = Mathf.Max(1, poles);
            float comp = LowPoleComp(n);
            float dt = 1f / SampleRate;
            float inv = 1f / Mathf.Max(1, buf.Length - 1);
            for (int p = 0; p < n; p++)
            {
                float y = 0f;
                for (int i = 0; i < buf.Length; i++)
                {
                    float fc = Mathf.Clamp(cutoffAt(i * inv) * comp, 10f, SampleRate * 0.45f);
                    float rc = 1f / (TwoPi * fc);
                    float a = dt / (rc + dt);
                    y += a * (buf[i] - y);
                    buf[i] = y;
                }
            }
        }

        /// <summary>High pass with a fixed cutoff; <paramref name="poles"/> cascades the section for a steeper slope.</summary>
        public static void HighPass(float[] buf, float cutoffHz, int poles = 1)
        {
            HighPass(buf, delegate { return cutoffHz; }, poles);
        }

        /// <summary>High pass whose cutoff glides (logarithmically) from start to end across the buffer.</summary>
        public static void HighPass(float[] buf, float cutoffStart, float cutoffEnd, int poles = 1)
        {
            HighPass(buf, t => LogLerp(cutoffStart, cutoffEnd, t), poles);
        }

        /// <summary>High pass with an arbitrary cutoff curve; <paramref name="cutoffAt"/> takes 0..1 and returns Hz.</summary>
        public static void HighPass(float[] buf, Func<float, float> cutoffAt, int poles = 1)
        {
            int n = Mathf.Max(1, poles);
            float comp = HighPoleComp(n);
            float dt = 1f / SampleRate;
            float inv = 1f / Mathf.Max(1, buf.Length - 1);
            for (int p = 0; p < n; p++)
            {
                float y = 0f, xPrev = 0f;
                for (int i = 0; i < buf.Length; i++)
                {
                    float fc = Mathf.Clamp(cutoffAt(i * inv) * comp, 5f, SampleRate * 0.45f);
                    float rc = 1f / (TwoPi * fc);
                    float a = rc / (rc + dt);
                    float x = buf[i];
                    y = a * (y + x - xPrev);
                    xPrev = x;
                    buf[i] = y;
                }
            }
        }

        /// <summary>Band pass: high pass at <paramref name="lowHz"/> then low pass at <paramref name="highHz"/>.</summary>
        public static void BandPass(float[] buf, float lowHz, float highHz, int lowPoles = 1, int highPoles = 1)
        {
            HighPass(buf, lowHz, lowPoles);
            LowPass(buf, highHz, highPoles);
        }

        /// <summary>Band pass whose two edges each glide across the buffer (peel and whoosh sweeps).</summary>
        public static void BandPass(float[] buf, float lowStart, float lowEnd, float highStart, float highEnd, int lowPoles = 1, int highPoles = 1)
        {
            HighPass(buf, lowStart, lowEnd, lowPoles);
            LowPass(buf, highStart, highEnd, highPoles);
        }

        /// <summary>Band pass with arbitrary curves for both edges (0..1 in, Hz out).</summary>
        public static void BandPass(float[] buf, Func<float, float> lowAt, Func<float, float> highAt, int lowPoles = 1, int highPoles = 1)
        {
            HighPass(buf, lowAt, lowPoles);
            LowPass(buf, highAt, highPoles);
        }

        // Pole frequency multiplier that keeps the -3 dB corner at the requested cutoff
        // after n identical sections are cascaded.
        static float LowPoleComp(int n)
        {
            if (n <= 1) return 1f;
            return 1f / Mathf.Sqrt(Mathf.Pow(2f, 1f / n) - 1f);
        }

        static float HighPoleComp(int n)
        {
            if (n <= 1) return 1f;
            float k = Mathf.Pow(2f, -1f / n);
            return Mathf.Sqrt((1f - k) / k);
        }

        static float LogLerp(float a, float b, float t)
        {
            a = Mathf.Max(1f, a); b = Mathf.Max(1f, b);
            return Mathf.Exp(Mathf.Lerp(Mathf.Log(a), Mathf.Log(b), Mathf.Clamp01(t)));
        }

        // ------------------------------------------------------- mix and shape

        /// <summary>Sums any number of buffers; the result is as long as the longest input.</summary>
        public static float[] Mix(params float[][] tracks)
        {
            int len = 0;
            for (int i = 0; i < tracks.Length; i++) if (tracks[i] != null && tracks[i].Length > len) len = tracks[i].Length;
            var outBuf = new float[Mathf.Max(1, len)];
            for (int i = 0; i < tracks.Length; i++)
            {
                var t = tracks[i];
                if (t == null) continue;
                for (int j = 0; j < t.Length; j++) outBuf[j] += t[j];
            }
            return outBuf;
        }

        /// <summary>Adds <paramref name="src"/> into <paramref name="dst"/> at a sample offset, clipped to the destination.</summary>
        public static void MixInto(float[] dst, float[] src, int offsetSamples, float gain = 1f)
        {
            if (src == null) return;
            int start = Mathf.Max(0, offsetSamples);
            for (int i = start; i < dst.Length; i++)
            {
                int s = i - offsetSamples;
                if (s < 0 || s >= src.Length) continue;
                dst[i] += src[s] * gain;
            }
        }

        /// <summary>Adds <paramref name="src"/> into <paramref name="dst"/> at a time offset in seconds.</summary>
        public static void MixAt(float[] dst, float[] src, float offsetSeconds, float gain = 1f)
        {
            MixInto(dst, src, Mathf.RoundToInt(offsetSeconds * SampleRate), gain);
        }

        /// <summary>Scales a buffer in place.</summary>
        public static void Gain(float[] buf, float g)
        {
            for (int i = 0; i < buf.Length; i++) buf[i] *= g;
        }

        /// <summary>Largest absolute sample value.</summary>
        public static float Peak(float[] buf)
        {
            float p = 0f;
            for (int i = 0; i < buf.Length; i++) { float a = buf[i] < 0f ? -buf[i] : buf[i]; if (a > p) p = a; }
            return p;
        }

        /// <summary>Root mean square level of a buffer.</summary>
        public static float Rms(float[] buf)
        {
            double acc = 0.0;
            for (int i = 0; i < buf.Length; i++) acc += (double)buf[i] * buf[i];
            return (float)Math.Sqrt(acc / Mathf.Max(1, buf.Length));
        }

        /// <summary>Rescales so the loudest sample sits at <paramref name="targetPeak"/>; never reaches 1.0, so nothing clips.</summary>
        public static void Normalize(float[] buf, float targetPeak = 0.9f)
        {
            float p = Peak(buf);
            if (p < 1e-6f) return;
            Gain(buf, Mathf.Clamp(targetPeak, 0f, 0.98f) / p);
        }

        /// <summary>Linear fade in over the first <paramref name="seconds"/>.</summary>
        public static void FadeIn(float[] buf, float seconds)
        {
            int n = Mathf.Min(buf.Length, Mathf.Max(1, Mathf.RoundToInt(seconds * SampleRate)));
            for (int i = 0; i < n; i++) buf[i] *= i / (float)n;
        }

        /// <summary>Linear fade out over the last <paramref name="seconds"/>.</summary>
        public static void FadeOut(float[] buf, float seconds)
        {
            int n = Mathf.Min(buf.Length, Mathf.Max(1, Mathf.RoundToInt(seconds * SampleRate)));
            int start = buf.Length - n;
            for (int i = 0; i < n; i++) buf[start + i] *= 1f - i / (float)n;
        }

        /// <summary>
        /// Closes both buffer edges with a very short fade. Without this a non zero first or last
        /// sample is a step discontinuity and the clip starts or ends with an audible tick.
        /// </summary>
        public static void DeClick(float[] buf, float milliseconds = 3f)
        {
            float s = milliseconds * 0.001f;
            FadeIn(buf, s);
            FadeOut(buf, s);
        }

        /// <summary>
        /// Makes a buffer loop seamlessly: the tail is cross faded into the head so sample 0
        /// continues from the last sample. Used instead of edge fades for looping clips.
        /// </summary>
        public static float[] CrossfadeWrap(float[] buf, float seconds)
        {
            int n = Mathf.Clamp(Mathf.RoundToInt(seconds * SampleRate), 1, buf.Length / 3);
            var outBuf = new float[buf.Length - n];
            Array.Copy(buf, outBuf, outBuf.Length);
            for (int i = 0; i < n; i++)
            {
                float w = i / (float)n;
                outBuf[i] = outBuf[i] * w + buf[outBuf.Length + i] * (1f - w);
            }
            return outBuf;
        }

        /// <summary>Wraps a rendered buffer in a runtime <see cref="AudioClip"/> (mono, <see cref="SampleRate"/> Hz).</summary>
        public static AudioClip ToAudioClip(float[] samples, string name)
        {
            var clip = AudioClip.Create(name, samples.Length, 1, SampleRate, false);
            clip.SetData(samples, 0);
            return clip;
        }
    }
}
