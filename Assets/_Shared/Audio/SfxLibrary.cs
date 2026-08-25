using System;
using System.Collections.Generic;
using UnityEngine;

namespace Shared.Audio
{
    /// <summary>Every sound effect the four case sequences can ask for.</summary>
    public enum SfxId
    {
        /// <summary>Very short tonal tap, the moment the player selects a shape.</summary>
        TapPop = 0,
        /// <summary>Broadband arrival hit with a low thump and a sparkle top; the loudest event of a sequence.</summary>
        ArrivalImpact = 1,
        /// <summary>Single cell of the spreading drum ripple. Play several through <see cref="AudioService.PlayRepeat"/>.</summary>
        RippleTick = 2,
        /// <summary>Sticker peel: rising filtered hiss.</summary>
        PeelShhh = 3,
        /// <summary>Sticker landing in its slot, plus a sparkle accent.</summary>
        AttachPop = 4,
        /// <summary>Block breaking apart: deep thump plus glass debris.</summary>
        Shatter = 5,
        /// <summary>Fragments falling into the hole after a shatter.</summary>
        DebrisFall = 6,
        /// <summary>Puck striking the block wall: very short, very steep.</summary>
        PuckImpact = 7,
        /// <summary>Continuous low crowd rumble; loops under a whole sequence.</summary>
        CrowdAmbience = 8,
        /// <summary>Air movement over a short flight arc; swells from silence.</summary>
        WhooshArc = 9
    }

    /// <summary>
    /// Procedural SFX bank. Each recipe implements one row of the measured audio design
    /// (attack, band, layer count, duration) with no sampled material of any kind.
    /// Buffers and clips are synthesised on first request and cached from then on.
    /// </summary>
    public static class SfxLibrary
    {
        /// <summary>All ids in declaration order.</summary>
        public static readonly SfxId[] All = (SfxId[])Enum.GetValues(typeof(SfxId));

        static readonly Dictionary<SfxId, float[]> Buffers = new Dictionary<SfxId, float[]>();
        static readonly Dictionary<SfxId, AudioClip> Clips = new Dictionary<SfxId, AudioClip>();

        /// <summary>True for clips meant to be played as a continuous bed rather than one shot.</summary>
        public static bool IsLoop(SfxId id) { return id == SfxId.CrowdAmbience; }

        /// <summary>Raw mono samples for an effect, synthesised once and cached.</summary>
        public static float[] GetSamples(SfxId id)
        {
            float[] buf;
            if (Buffers.TryGetValue(id, out buf)) return buf;
            buf = Build(id);
            Buffers[id] = buf;
            return buf;
        }

        /// <summary>Playable <see cref="AudioClip"/> for an effect, created once and cached.</summary>
        public static AudioClip GetClip(SfxId id)
        {
            AudioClip clip;
            if (Clips.TryGetValue(id, out clip) && clip != null) return clip;
            clip = SfxSynth.ToAudioClip(GetSamples(id), "SFX_" + id);
            Clips[id] = clip;
            return clip;
        }

        /// <summary>Synthesises every effect up front so the first play of a sequence has no hitch.</summary>
        public static void Prewarm()
        {
            for (int i = 0; i < All.Length; i++) GetClip(All[i]);
        }

        /// <summary>
        /// Own reset hook. <see cref="AudioService"/> already calls <see cref="ClearCache"/> from its own
        /// SubsystemRegistration hook, but with the domain reload disabled that would be the single point
        /// keeping this cache honest: a scene without an AudioService would hand out AudioClips destroyed
        /// by the previous Play. Owning the hook here removes that dependency.
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { ClearCache(); }

        /// <summary>Drops cached buffers and clips (editor domain reloads, or after tweaking a recipe).</summary>
        public static void ClearCache()
        {
            Buffers.Clear();
            Clips.Clear();
        }

        // ------------------------------------------------------------- recipes

        static float[] Build(SfxId id)
        {
            switch (id)
            {
                case SfxId.TapPop: return BuildTapPop();
                case SfxId.ArrivalImpact: return BuildArrivalImpact();
                case SfxId.RippleTick: return BuildRippleTick();
                case SfxId.PeelShhh: return BuildPeelShhh();
                case SfxId.AttachPop: return BuildAttachPop();
                case SfxId.Shatter: return BuildShatter();
                case SfxId.DebrisFall: return BuildDebrisFall();
                case SfxId.PuckImpact: return BuildPuckImpact();
                case SfxId.CrowdAmbience: return BuildCrowdAmbience();
                case SfxId.WhooshArc: return BuildWhooshArc();
                default: throw new ArgumentOutOfRangeException("id", id, "No recipe for this SfxId.");
            }
        }

        // 0.09 s -- sine 600 -> 400 Hz drop, short exponential body, light second harmonic.
        static float[] BuildTapPop()
        {
            const float dur = 0.09f;
            var body = SfxSynth.SineSweep(dur, 600f, 400f, 1f, 2f);
            SfxSynth.ApplyExpDecay(body, 0.022f, 0.0015f);
            var harm = SfxSynth.SineSweep(dur, 1200f, 800f, 0.28f, 2f);
            SfxSynth.ApplyExpDecay(harm, 0.010f, 0.0010f);
            var mix = SfxSynth.Mix(body, harm);
            SfxSynth.Normalize(mix, 0.45f);
            SfxSynth.DeClick(mix, 2f);
            return mix;
        }

        // 0.25 s hit + 0.9 s tail -- low thump 90 -> 55 Hz, band passed noise burst, 8-14 kHz sparkle.
        static float[] BuildArrivalImpact()
        {
            const float dur = 1.15f;
            var rng = new SfxSynth.Rng(1101);
            var outBuf = SfxSynth.Alloc(dur);

            // VIDEO_MEASURED from the reference clip's own audio: the seat is a 30 ms attack whose
            // dominant partial sits near 590 Hz with a 770 Hz spectral centroid - a low-mid THUNK, not
            // the sub-bass 90 -> 55 Hz sweep this used to be, which at that pitch is felt rather than
            // heard and left the arrival sounding like it had no impact at all.
            var thump = SfxSynth.SineSweep(1.05f, 700f, 520f, 1f, 2f);
            SfxSynth.ApplyExpDecay(thump, 0.17f, 0.0015f);
            SfxSynth.MixAt(outBuf, thump, 0f, 1.0f);

            var burst = SfxSynth.WhiteNoise(0.30f, 1f, rng);
            SfxSynth.BandPass(burst, 200f, 260f, 5200f, 1200f, lowPoles: 1, highPoles: 3);
            SfxSynth.ApplyExpDecay(burst, 0.075f, 0.0010f);
            SfxSynth.MixAt(outBuf, burst, 0f, 0.75f);

            var sparkle = SfxSynth.WhiteNoise(0.35f, 1f, rng);
            SfxSynth.BandPass(sparkle, 8000f, 14000f, lowPoles: 2, highPoles: 3);
            SfxSynth.ApplyExpDecay(sparkle, 0.11f, 0.004f);
            SfxSynth.MixAt(outBuf, sparkle, 0.015f, 0.45f);

            // Body of the tail: filtered rumble so the 0.9 s decay is not a bare sine.
            var tail = SfxSynth.WhiteNoise(0.95f, 1f, rng);
            SfxSynth.LowPass(tail, 900f, 220f, poles: 2);
            SfxSynth.ApplyExpDecay(tail, 0.30f, 0.02f);
            SfxSynth.MixAt(outBuf, tail, 0.05f, 0.26f);

            SfxSynth.FadeOut(outBuf, 0.12f);
            SfxSynth.Normalize(outBuf, 0.92f);
            SfxSynth.DeClick(outBuf, 2f);
            return outBuf;
        }

        // 0.05 s -- filtered noise tick plus a tonal click. Pitch/level ladder is applied at play time.
        //
        // VIDEO_MEASURED: in the reference the ticks are far BRIGHTER than the seat that starts them.
        // The attack's centroid is 770 Hz, but the tail from 0.55 s on measures a 5389 Hz centroid with
        // a strong partial around 5570 Hz. Ours sat at 1.5-4.5 kHz with a 900 Hz click, which read as
        // more of the same thud instead of a wave running away across the drum.
        static float[] BuildRippleTick()
        {
            const float dur = 0.05f;
            var rng = new SfxSynth.Rng(2202);
            var noise = SfxSynth.WhiteNoise(dur, 1f, rng);
            SfxSynth.BandPass(noise, 3500f, 8000f, lowPoles: 1, highPoles: 3);
            SfxSynth.ApplyExpDecay(noise, 0.011f, 0.0006f);

            var click = SfxSynth.SineSweep(dur, 5570f, 4600f, 0.6f, 2f);
            SfxSynth.ApplyExpDecay(click, 0.009f, 0.0006f);

            var mix = SfxSynth.Mix(noise, click);
            SfxSynth.Normalize(mix, 0.30f);
            SfxSynth.DeClick(mix, 2f);
            return mix;
        }

        // 0.50 s -- high passed noise sweeping 1.5 -> 6 kHz, slow attack and fast decay.
        static float[] BuildPeelShhh()
        {
            const float dur = 0.50f;
            var rng = new SfxSynth.Rng(3303);
            var buf = SfxSynth.WhiteNoise(dur, 1f, rng);
            SfxSynth.BandPass(buf, t => Mathf.Lerp(1500f, 6000f, t), t => Mathf.Lerp(11000f, 13000f, t), lowPoles: 2, highPoles: 3);
            SfxSynth.ApplyEnvelope(buf, 0.26f, 0.14f, 0.30f, 0.10f);

            // Friction grain: slow amplitude wobble that speeds up as the corner lifts.
            for (int i = 0; i < buf.Length; i++)
            {
                float t = i / (float)buf.Length;
                float rate = Mathf.Lerp(14f, 34f, t);
                buf[i] *= 1f + 0.16f * Mathf.Sin(6.28318530718f * rate * t * dur);
            }

            SfxSynth.Normalize(buf, 0.55f);
            SfxSynth.DeClick(buf, 4f);
            return buf;
        }

        // 0.30 s -- sine pop plus a sparkle layer 0.12 s later (the measured two layer rule, baked in).
        static float[] BuildAttachPop()
        {
            const float dur = 0.30f;
            var rng = new SfxSynth.Rng(4404);
            var outBuf = SfxSynth.Alloc(dur);

            var pop = SfxSynth.SineSweep(0.12f, 520f, 300f, 1f, 2f);
            SfxSynth.ApplyExpDecay(pop, 0.030f, 0.0012f);
            SfxSynth.MixAt(outBuf, pop, 0f, 1f);

            var thud = SfxSynth.SineSweep(0.14f, 150f, 95f, 1f, 2f);
            SfxSynth.ApplyExpDecay(thud, 0.045f, 0.0015f);
            SfxSynth.MixAt(outBuf, thud, 0f, 0.5f);

            for (int i = 0; i < 5; i++)
            {
                float f = rng.Range(2600f, 7000f);
                float len = rng.Range(0.06f, 0.13f);
                var tone = SfxSynth.Triangle(len, f, 1f);
                SfxSynth.ApplyExpDecay(tone, rng.Range(0.020f, 0.045f), 0.0015f);
                SfxSynth.MixAt(outBuf, tone, 0.12f + rng.Range(0f, 0.05f), rng.Range(0.10f, 0.20f));
            }

            SfxSynth.FadeOut(outBuf, 0.05f);
            SfxSynth.Normalize(outBuf, 0.60f);
            SfxSynth.DeClick(outBuf, 2f);
            return outBuf;
        }

        // 0.4 s break + ~1.0 s tail -- 30-70 Hz thump, broadband crack, 7 short glass tones.
        static float[] BuildShatter()
        {
            const float dur = 1.40f;
            var rng = new SfxSynth.Rng(5505);
            var outBuf = SfxSynth.Alloc(dur);

            var thump = SfxSynth.SineSweep(1.10f, 70f, 30f, 1f, 2f);
            SfxSynth.ApplyExpDecay(thump, 0.19f, 0.0012f);
            SfxSynth.MixAt(outBuf, thump, 0f, 1.0f);

            var crack = SfxSynth.WhiteNoise(0.45f, 1f, rng);
            SfxSynth.BandPass(crack, 120f, 180f, 5000f, 900f, lowPoles: 1, highPoles: 3);
            SfxSynth.ApplyExpDecay(crack, 0.095f, 0.0008f);
            SfxSynth.MixAt(outBuf, crack, 0f, 0.50f);

            for (int i = 0; i < 7; i++)
            {
                float f = rng.Range(1800f, 5200f);
                float len = rng.Range(0.10f, 0.24f);
                var tone = SfxSynth.Triangle(len, f, 1f);
                SfxSynth.ApplyExpDecay(tone, rng.Range(0.025f, 0.055f), 0.0012f);
                SfxSynth.MixAt(outBuf, tone, rng.Range(0.005f, 0.30f), rng.Range(0.06f, 0.14f));
            }

            var rumble = SfxSynth.WhiteNoise(1.30f, 1f, rng);
            SfxSynth.LowPass(rumble, 700f, 160f, poles: 3);
            SfxSynth.ApplyExpDecay(rumble, 0.38f, 0.02f);
            SfxSynth.MixAt(outBuf, rumble, 0.03f, 0.14f);

            SfxSynth.FadeOut(outBuf, 0.15f);
            SfxSynth.Normalize(outBuf, 0.95f);
            SfxSynth.DeClick(outBuf, 2f);
            return outBuf;
        }

        // 1.10 s -- sparse randomly spaced ticks; level falls and pitch rises as fragments get smaller.
        static float[] BuildDebrisFall()
        {
            const float dur = 1.10f;
            var rng = new SfxSynth.Rng(6606);
            var outBuf = SfxSynth.Alloc(dur);

            float t = 0.01f;
            int index = 0;
            while (t < 0.95f && index < 40)
            {
                float amp = Mathf.Exp(-t / 0.42f) * rng.Range(0.55f, 1f);
                float centre = 900f * Mathf.Pow(1.04f, index);
                var tick = SfxSynth.WhiteNoise(rng.Range(0.020f, 0.048f), 1f, rng);
                SfxSynth.BandPass(tick, centre * 0.6f, Mathf.Min(centre * 3.2f, 12000f), lowPoles: 1, highPoles: 3);
                SfxSynth.ApplyExpDecay(tick, rng.Range(0.006f, 0.014f), 0.0006f);
                SfxSynth.MixAt(outBuf, tick, t, amp);

                var ping = SfxSynth.Sine(0.03f, centre * rng.Range(1.4f, 2.6f), 1f);
                SfxSynth.ApplyExpDecay(ping, 0.008f, 0.0006f);
                SfxSynth.MixAt(outBuf, ping, t, amp * 0.35f);

                t += rng.Range(0.020f, 0.075f) * (1f + t * 1.6f);
                index++;
            }

            SfxSynth.FadeOut(outBuf, 0.12f);
            SfxSynth.Normalize(outBuf, 0.40f);
            SfxSynth.DeClick(outBuf, 2f);
            return outBuf;
        }

        // 0.14 s -- broadband transient plus a 60 Hz thump, one millisecond attack.
        static float[] BuildPuckImpact()
        {
            const float dur = 0.14f;
            var rng = new SfxSynth.Rng(7707);
            var outBuf = SfxSynth.Alloc(dur);

            var transient = SfxSynth.WhiteNoise(0.09f, 1f, rng);
            SfxSynth.BandPass(transient, 150f, 9000f, lowPoles: 1, highPoles: 2);
            SfxSynth.ApplyExpDecay(transient, 0.020f, 0.0005f);
            SfxSynth.MixAt(outBuf, transient, 0f, 0.85f);

            var thump = SfxSynth.SineSweep(dur, 75f, 45f, 1f, 2f);
            SfxSynth.ApplyExpDecay(thump, 0.048f, 0.0008f);
            SfxSynth.MixAt(outBuf, thump, 0f, 1f);

            SfxSynth.FadeOut(outBuf, 0.02f);
            SfxSynth.Normalize(outBuf, 0.90f);
            SfxSynth.DeClick(outBuf, 2f);
            return outBuf;
        }

        // 4.0 s seamless loop -- low passed pink noise with slow amplitude modulation.
        static float[] BuildCrowdAmbience()
        {
            const float raw = 4.35f;
            var rng = new SfxSynth.Rng(8808);
            var buf = SfxSynth.PinkNoise(raw, 1f, rng);
            SfxSynth.LowPass(buf, 600f, poles: 3); // measured crowd bed sits in 20-600 Hz
            SfxSynth.HighPass(buf, 25f);

            // Modulation periods divide the 4.0 s loop length so the wobble wraps with the noise.
            for (int i = 0; i < buf.Length; i++)
            {
                float t = i / (float)SfxSynth.SampleRate;
                float m = 1f
                    + 0.22f * Mathf.Sin(6.28318530718f * 0.25f * t)
                    + 0.11f * Mathf.Sin(6.28318530718f * 0.75f * t + 1.1f);
                buf[i] *= m;
            }

            var looped = SfxSynth.CrossfadeWrap(buf, 0.35f);
            SfxSynth.Normalize(looped, 0.25f);
            return looped; // no edge fades: the wrap cross fade already makes head and tail continuous
        }

        // 0.35 s -- band passed noise whose band follows flight speed; opens from silence.
        static float[] BuildWhooshArc()
        {
            const float dur = 0.35f;
            var rng = new SfxSynth.Rng(9909);
            var buf = SfxSynth.WhiteNoise(dur, 1f, rng);

            // Speed curve of an arc: accelerate out, decelerate in. Band opens and closes with it.
            Func<float, float> speed = t => Mathf.Sin(Mathf.Clamp01(t) * 3.14159265f);
            SfxSynth.BandPass(buf,
                t => Mathf.Lerp(180f, 700f, speed(t)),
                t => Mathf.Lerp(900f, 3200f, speed(t)),
                lowPoles: 2, highPoles: 3);
            SfxSynth.ApplyEnvelope(buf, 0.16f, 0.09f, 0.55f, 0.10f);

            SfxSynth.Normalize(buf, 0.35f);
            SfxSynth.DeClick(buf, 4f);
            return buf;
        }
    }
}
