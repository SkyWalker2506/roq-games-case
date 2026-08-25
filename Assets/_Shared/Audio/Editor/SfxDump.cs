using System;
using System.Globalization;
using System.IO;
using System.Text;
using Shared.Audio;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Measurement gate for the procedural SFX bank. Renders every recipe to a 16 bit PCM WAV in
/// <c>.plan-build/audio_out/</c> and logs duration, peak and RMS for each, so the bank can be
/// verified without listening to it. Global namespace on purpose: the batch entry point is
/// invoked as <c>-executeMethod SfxDump.DumpAll</c>.
/// </summary>
public static class SfxDump
{
    const string OutFolder = ".plan-build/audio_out";

    /// <summary>Renders every clip in <see cref="SfxLibrary"/> to WAV and logs its measurements.</summary>
    public static void DumpAll()
    {
        try
        {
            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            string outDir = Path.Combine(projectRoot, OutFolder);
            if (Directory.Exists(outDir))
            {
                foreach (var old in Directory.GetFiles(outDir, "*.wav")) File.Delete(old);
            }
            Directory.CreateDirectory(outDir);

            SfxLibrary.ClearCache();
            int count = 0;
            var sb = new StringBuilder();
            sb.AppendLine("SFX_DUMP name,duration_s,peak,rms,samples,loop");

            foreach (var id in SfxLibrary.All)
            {
                float[] samples = SfxLibrary.GetSamples(id);
                if (samples == null || samples.Length == 0)
                    throw new InvalidOperationException("Recipe produced no samples: " + id);

                float duration = samples.Length / (float)SfxSynth.SampleRate;
                float peak = SfxSynth.Peak(samples);
                float rms = SfxSynth.Rms(samples);

                string path = Path.Combine(outDir, id + ".wav");
                WriteWav16(path, samples, SfxSynth.SampleRate);
                count++;

                Debug.Log(string.Format(CultureInfo.InvariantCulture,
                    "SFX_CLIP name={0} dur={1:F3}s peak={2:F4} rms={3:F4} samples={4} loop={5}",
                    id, duration, peak, rms, samples.Length, SfxLibrary.IsLoop(id)));

                sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                    "SFX_ROW {0},{1:F3},{2:F4},{3:F4},{4},{5}",
                    id, duration, peak, rms, samples.Length, SfxLibrary.IsLoop(id)));

                if (peak >= 0.999f) throw new InvalidOperationException("Clip is clipped at full scale: " + id);
                if (rms < 1e-4f) throw new InvalidOperationException("Clip is silent: " + id);
            }

            Debug.Log(sb.ToString());
            Debug.Log("SFX_DUMP wrote " + count + " wav files to " + outDir);
            Debug.Log("SFX_DUMP_OK");
        }
        catch (Exception e)
        {
            Debug.LogError("SFX_DUMP_FAIL " + e);
            if (Application.isBatchMode) EditorApplication.Exit(1);
        }
    }

    /// <summary>Writes a mono 16 bit PCM WAV file.</summary>
    static void WriteWav16(string path, float[] samples, int sampleRate)
    {
        const int channels = 1;
        const int bitsPerSample = 16;
        int dataBytes = samples.Length * channels * (bitsPerSample / 8);

        using (var fs = new FileStream(path, FileMode.Create, FileAccess.Write))
        using (var w = new BinaryWriter(fs))
        {
            w.Write(Encoding.ASCII.GetBytes("RIFF"));
            w.Write(36 + dataBytes);
            w.Write(Encoding.ASCII.GetBytes("WAVE"));
            w.Write(Encoding.ASCII.GetBytes("fmt "));
            w.Write(16);                                   // PCM chunk size
            w.Write((short)1);                             // PCM format
            w.Write((short)channels);
            w.Write(sampleRate);
            w.Write(sampleRate * channels * bitsPerSample / 8); // byte rate
            w.Write((short)(channels * bitsPerSample / 8));     // block align
            w.Write((short)bitsPerSample);
            w.Write(Encoding.ASCII.GetBytes("data"));
            w.Write(dataBytes);

            for (int i = 0; i < samples.Length; i++)
            {
                float v = Mathf.Clamp(samples[i], -1f, 1f);
                w.Write((short)Mathf.RoundToInt(v * 32767f));
            }
        }
    }
}
