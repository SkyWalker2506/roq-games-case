using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>
/// P20 colour gate. Reads the capture strip each case has already written to
/// <c>.plan-build/verify/&lt;scene&gt;/frame_NN.png</c> and, for a fixed list of viewport rectangles,
/// compares the colour we render against the colour the REFERENCE VIDEO shows at the same place.
///
/// Where the target colours come from (repeatable, no taste involved):
///   ffmpeg -ss &lt;t&gt; -i "_refs/Developer Case Referans/&lt;case&gt;.mp4" -vframes 1 -vf scale=540:864 ref.png
/// then, for each named element, the MEDIAN of a rectangle that sits wholly inside a flat area of that
/// element - never on an edge, a highlight or a drop shadow. The median (not the mean) so a stray
/// specular pixel cannot drag the number. Our own frame is sampled with the same rectangle, at the same
/// 540x864 capture size, and because <see cref="RefPositionGate"/> already proves the layouts line up,
/// the same viewport rectangle lands on the same object in both.
///
/// The distance is CIE76 dE in L*a*b* (D65). Rule of thumb: dE 2-3 is invisible side by side, dE 10 is
/// an obvious difference, dE 20 is "a different colour". The gate fails a case whose mean dE is above
/// <see cref="MaxMeanDeltaE"/>.
///
/// PROOF THE GATE IS NOT EMPTY: the "before" column below is what these very same rectangles measured
/// on the P19 capture, before any of P20's material work. Four of the four cases were above the bar or
/// close to it, and Case 2 was at dE 36 - a magenta board where the reference is red and navy. If this
/// gate is ever green by accident, those numbers say what it was catching.
///
/// Zero-argument on purpose: Unity's -executeMethod refuses anything else (lesson #1).
/// </summary>
public static class CaseColorGate
{
    /// <summary>A case fails above this mean CIE76 dE.</summary>
    public const float MaxMeanDeltaE = 12f;

    const int W = 540;
    const int H = 864;

    struct Sample
    {
        public string name;
        public int frame;                 // index into the case's capture strip
        public float x0, x1, y0, y1;      // viewport rectangle, y measured from the TOP of the frame
        public string target;             // reference colour, "#RRGGBB"
        public float before;              // what this rectangle measured before P20 (see class docs)
    }

    struct Case
    {
        public string name;
        public string folder;             // .plan-build/verify/<folder>
        public Sample[] samples;
    }

    static readonly Case[] Cases =
    {
        new Case
        {
            name = "Case4_Buca", folder = "Buca",
            samples = new[]
            {
                //                                                                                          target      before
                new Sample { name = "ground_in",   frame = 0, x0 = 0.58f,  x1 = 0.72f,  y0 = 0.42f, y1 = 0.50f, target = "#596878", before = 11.6f },
                new Sample { name = "ground_out",  frame = 0, x0 = 0.80f,  x1 = 0.92f,  y0 = 0.78f, y1 = 0.86f, target = "#5B6B7A", before =  9.7f },
                new Sample { name = "green_stack", frame = 0, x0 = 0.13f,  x1 = 0.20f,  y0 = 0.56f, y1 = 0.62f, target = "#00FC00", before = 22.1f },
                new Sample { name = "rim_idle",    frame = 0, x0 = 0.09f,  x1 = 0.115f, y0 = 0.50f, y1 = 0.58f, target = "#F0FCFB", before =  4.4f },
                new Sample { name = "divider",     frame = 0, x0 = 0.46f,  x1 = 0.50f,  y0 = 0.50f, y1 = 0.62f, target = "#FDFDFD", before =  0.3f },
                new Sample { name = "puck",        frame = 0, x0 = 0.78f,  x1 = 0.82f,  y0 = 0.638f, y1 = 0.652f, target = "#796F47", before = 32.1f },
                new Sample { name = "rim_active",  frame = 3, x0 = 0.09f,  x1 = 0.115f, y0 = 0.50f, y1 = 0.58f, target = "#2EF6FA", before =  4.8f },
            }
        },
        new Case
        {
            name = "Case2_BlockHole", folder = "BlockHole",
            samples = new[]
            {
                new Sample { name = "outer_bg",    frame = 15, x0 = 0.05f, x1 = 0.25f, y0 = 0.88f,  y1 = 0.93f,  target = "#3C457C", before =  5.7f },
                new Sample { name = "board",       frame = 15, x0 = 0.08f, x1 = 0.90f, y0 = 0.17f,  y1 = 0.25f,  target = "#314171", before = 21.5f },
                new Sample { name = "hole_glow",   frame = 15, x0 = 0.50f, x1 = 0.60f, y0 = 0.57f,  y1 = 0.62f,  target = "#AD36FC", before = 79.4f },
                new Sample { name = "block_cyan",  frame = 15, x0 = 0.24f, x1 = 0.40f, y0 = 0.645f, y1 = 0.685f, target = "#0496E0", before = 67.0f },
            }
        },
        new Case
        {
            name = "Case1_FitTheShape", folder = "FitTheShape",
            samples = new[]
            {
                new Sample { name = "bg_upper",     frame = 5, x0 = 0.03f, x1 = 0.12f, y0 = 0.06f,  y1 = 0.14f,  target = "#7371C9", before =  8.6f },
                new Sample { name = "bg_lower",     frame = 5, x0 = 0.05f, x1 = 0.15f, y0 = 0.55f,  y1 = 0.62f,  target = "#8585CF", before = 20.3f },
                new Sample { name = "deck_slot",    frame = 5, x0 = 0.22f, x1 = 0.28f, y0 = 0.475f, y1 = 0.495f, target = "#99ABC0", before = 33.0f },
                new Sample { name = "bottom_bar",   frame = 5, x0 = 0.82f, x1 = 0.95f, y0 = 0.91f,  y1 = 0.95f,  target = "#3D26AF", before =  3.6f },
                new Sample { name = "shape_green",  frame = 5, x0 = 0.45f, x1 = 0.55f, y0 = 0.60f,  y1 = 0.63f,  target = "#25A111", before = 48.2f },
                new Sample { name = "shape_red",    frame = 5, x0 = 0.46f, x1 = 0.52f, y0 = 0.71f,  y1 = 0.735f, target = "#E24A30", before = 25.0f },
                new Sample { name = "shape_purple", frame = 5, x0 = 0.35f, x1 = 0.40f, y0 = 0.715f, y1 = 0.735f, target = "#C100E1", before = 17.6f },
            }
        },
        new Case
        {
            name = "Case3_Stickerdom", folder = "Stickerdom",
            samples = new[]
            {
                new Sample { name = "top_wood",      frame = 0, x0 = 0.30f, x1 = 0.45f, y0 = 0.02f, y1 = 0.06f, target = "#D99D62", before = 10.1f },
                new Sample { name = "slot_lavender", frame = 0, x0 = 0.05f, x1 = 0.20f, y0 = 0.15f, y1 = 0.24f, target = "#D09BC2", before = 12.2f },
                new Sample { name = "page_cream",    frame = 0, x0 = 0.24f, x1 = 0.30f, y0 = 0.30f, y1 = 0.34f, target = "#F9CD90", before =  9.4f },
            }
        },
    };

    // ------------------------------------------------------------------ entry point

    public static void Run()
    {
        string root = Path.GetDirectoryName(Application.dataPath);
        int failures = 0;
        int points = 0;

        Line("---- P20 colour gate (CIE76 dE, per-case mean must be <= " + MaxMeanDeltaE.ToString("0") + ") ----");

        for (int c = 0; c < Cases.Length; c++)
        {
            Case cs = Cases[c];
            float sum = 0f;
            float sumBefore = 0f;
            int n = 0;
            Dictionary<int, Texture2D> frames = new Dictionary<int, Texture2D>();

            Line(cs.name + ":");
            for (int s = 0; s < cs.samples.Length; s++)
            {
                Sample sm = cs.samples[s];
                Texture2D tex;
                if (!frames.TryGetValue(sm.frame, out tex))
                {
                    string path = Path.Combine(root, ".plan-build/verify/" + cs.folder + "/frame_" + sm.frame.ToString("00") + ".png");
                    tex = Load(path);
                    if (tex == null)
                    {
                        Line("  MISSING CAPTURE: " + path + " - run the case's BuildAndCapture first");
                        failures++;
                        n = 0;
                        break;
                    }
                    frames[sm.frame] = tex;
                }

                Color ours = Median(tex, sm);
                Color want = Parse(sm.target);
                float d = DeltaE(ours, want);
                sum += d;
                sumBefore += sm.before;
                n++;
                points++;
                Line(string.Format("  {0,-14} ref {1}  ours {2}   dE={3,5:0.0}   (before P20: {4:0.0})",
                                   sm.name, sm.target, Hex(ours), d, sm.before));
            }

            if (n == 0) continue;
            float mean = sum / n;
            float meanBefore = sumBefore / n;
            bool ok = mean <= MaxMeanDeltaE;
            if (!ok) failures++;
            Line(string.Format("  MEAN dE = {0:0.0}   (before P20: {1:0.0})   {2}",
                               mean, meanBefore, ok ? "PASS" : "FAIL"));
        }

        Line(string.Format("points={0} failedCases={1}", points, failures));
        if (failures > 0) { Line("COLOUR_GATE FAILED"); Finish(1); return; }
        Line("COLOUR_GATE PASSED");
        Finish(0);
    }

    // ------------------------------------------------------------------ measurement

    static Texture2D Load(string path)
    {
        if (!File.Exists(path)) return null;
        Texture2D tex = new Texture2D(2, 2, TextureFormat.RGBA32, false, true);
        if (!tex.LoadImage(File.ReadAllBytes(path))) return null;
        return tex;
    }

    /// <summary>Median colour of the sample rectangle. Channels are taken independently, which is what
    /// the reference numbers were measured with.</summary>
    static Color Median(Texture2D tex, Sample s)
    {
        int px0 = Mathf.Clamp(Mathf.RoundToInt(s.x0 * tex.width), 0, tex.width - 1);
        int px1 = Mathf.Clamp(Mathf.RoundToInt(s.x1 * tex.width), px0 + 1, tex.width);
        // the sample rectangle is stated with y from the TOP; Unity's GetPixels is bottom-up
        int py0 = Mathf.Clamp(Mathf.RoundToInt((1f - s.y1) * tex.height), 0, tex.height - 1);
        int py1 = Mathf.Clamp(Mathf.RoundToInt((1f - s.y0) * tex.height), py0 + 1, tex.height);

        Color[] block = tex.GetPixels(px0, py0, px1 - px0, py1 - py0);
        List<float> r = new List<float>(block.Length);
        List<float> g = new List<float>(block.Length);
        List<float> b = new List<float>(block.Length);
        for (int i = 0; i < block.Length; i++) { r.Add(block[i].r); g.Add(block[i].g); b.Add(block[i].b); }
        r.Sort(); g.Sort(); b.Sort();
        int m = block.Length / 2;
        return new Color(r[m], g[m], b[m], 1f);
    }

    static Color Parse(string hex)
    {
        Color c;
        ColorUtility.TryParseHtmlString(hex, out c);
        return c;
    }

    static string Hex(Color c)
    {
        return "#" + ColorUtility.ToHtmlStringRGB(c);
    }

    // ------------------------------------------------------------------ colour maths

    /// <summary>sRGB (0..1, as stored in the PNG) to CIE L*a*b* under D65.</summary>
    static Vector3 Lab(Color c)
    {
        float r = ToLinear(c.r), g = ToLinear(c.g), b = ToLinear(c.b);
        float x = 0.4124f * r + 0.3576f * g + 0.1805f * b;
        float y = 0.2126f * r + 0.7152f * g + 0.0722f * b;
        float z = 0.0193f * r + 0.1192f * g + 0.9505f * b;
        float fx = F(x / 0.95047f), fy = F(y), fz = F(z / 1.08883f);
        return new Vector3(116f * fy - 16f, 500f * (fx - fy), 200f * (fy - fz));
    }

    static float ToLinear(float v)
    {
        return v <= 0.04045f ? v / 12.92f : Mathf.Pow((v + 0.055f) / 1.055f, 2.4f);
    }

    static float F(float t)
    {
        return t > 0.008856f ? Mathf.Pow(t, 1f / 3f) : 7.787f * t + 16f / 116f;
    }

    static float DeltaE(Color a, Color b)
    {
        return Vector3.Distance(Lab(a), Lab(b));
    }

    // ------------------------------------------------------------------ plumbing

    static void Line(string message)
    {
        Debug.Log("[CaseColour] " + message);
        System.Console.WriteLine("[CaseColour] " + message);
    }

    static void Finish(int exitCode)
    {
        System.Console.Out.Flush();
        if (Application.isBatchMode) EditorApplication.Exit(exitCode);
    }
}
