using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Case3;

/// <summary>
/// Proves the peel still looks like the sticker.
///
/// The failure this gate exists for: with the wrap angle clamped just short of pi (0.92*pi) the flap
/// past the roll is not a mirror but a shear - cos(theta) = -0.97 instead of -1 - so the peeled paper
/// slid several world units outside the sprite's own footprint and rendered as a big shapeless white
/// sheet that had nothing to do with the sticker underneath it. Nothing in the compile, capture or
/// selection gates could see that, because every one of them was green while it happened.
///
/// So this gate walks the peel across its whole range and measures, at every step, the world-space AABB
/// the curl mesh actually occupies against the AABB of the flat sticker. If the curl ever grows past
/// <see cref="MaxRatio"/> on either axis the gate is red. It also logs how many stickers are on the
/// page, decorative ones included, so a thin scene cannot pass unnoticed.
/// </summary>
[InitializeOnLoad]
public static class Case3SilhouetteGate
{
    const string KeyActive = "Case3SilhouetteGate.Active";

    /// <summary>How much bigger than the flat sticker the curled sheet is allowed to get, on either axis.</summary>
    public const float MaxRatio = 1.35f;

    const double ReadyTimeout = 30.0;

    static bool _hooked;
    static bool _sessionInit;
    static double _start;

    static Case3SilhouetteGate()
    {
        if (SessionState.GetInt(KeyActive, 0) == 1) Hook();
    }

    /// <summary>Zero-argument entry point for -executeMethod.</summary>
    public static void SilhouetteGate()
    {
        EditorSceneManager.OpenScene("Assets/Case3_Stickerdom/Scenes/Stickerdom.unity", OpenSceneMode.Single);
        SessionState.SetInt(KeyActive, 1);
        _sessionInit = false;
        Hook();
        Debug.Log("[Case3Silhouette] GATE_START entering play mode");
        EditorApplication.EnterPlaymode();
    }

    static void Hook()
    {
        if (_hooked) return;
        _hooked = true;
        EditorApplication.update += Drive;
    }

    static void Drive()
    {
        if (SessionState.GetInt(KeyActive, 0) != 1) return;
        if (!EditorApplication.isPlaying) return;

        Case3Director director = Object.FindFirstObjectByType<Case3Director>(FindObjectsInactive.Include);
        if (director == null || director.Count == 0)
        {
            Finish("no Case3Director (or no wired stickers) in the play-mode scene", 2, 0f, 0);
            return;
        }

        if (!_sessionInit)
        {
            _sessionInit = true;
            _start = EditorApplication.timeSinceStartup;
        }

        if (!director.Ready && EditorApplication.timeSinceStartup - _start < ReadyTimeout) return;

        Measure(director);
    }

    static void Measure(Case3Director director)
    {
        StringBuilder sb = new StringBuilder();
        sb.AppendLine("[Case3Silhouette] ---- curl vs sprite silhouette ----");

        float worst = 0f;
        string worstWhere = "-";
        int failures = 0;

        for (int i = 0; i < director.Count; i++)
        {
            Case3Director.Entry e = director.entries[i];
            if (e == null || e.peel == null || e.peel.sticker == null) continue;

            StickerPeel peel = e.peel;
            peel.Prepare();
            peel.SetMeshMode(true);

            Bounds flat = peel.FlatWorldBounds();
            float flatX = Mathf.Max(0.0001f, flat.size.x);
            float flatY = Mathf.Max(0.0001f, flat.size.y);

            float localWorst = 0f;
            float worstProgress = 0f;
            Bounds worstBounds = flat;

            // 0.05 .. 1.00: the whole sweep, not just the frames the strip happens to sample.
            for (int step = 1; step <= 20; step++)
            {
                float p = step * 0.05f;
                peel.SetProgress(p);
                Bounds curl = peel.CurlWorldBounds();

                float ratio = Mathf.Max(curl.size.x / flatX, curl.size.y / flatY);
                if (ratio > localWorst)
                {
                    localWorst = ratio;
                    worstProgress = p;
                    worstBounds = curl;
                }
            }

            peel.ResetInstant();

            bool ok = localWorst <= MaxRatio;
            if (!ok) failures++;
            if (localWorst > worst) { worst = localWorst; worstWhere = peel.sticker.name; }

            sb.AppendLine(string.Format(
                "{0} {1}: flat {2:0.00}x{3:0.00} u -> worst curl {4:0.00}x{5:0.00} u at progress {6:0.00} = {7:0.000}x (limit {8:0.00})",
                ok ? "PASS" : "FAIL", peel.sticker.name, flatX, flatY,
                worstBounds.size.x, worstBounds.size.y, worstProgress, localWorst, MaxRatio));
        }

        int stickerCount = CountStickers();
        sb.AppendLine("[Case3Silhouette] stickers on the page = " + stickerCount + " (decorative included)");
        Debug.Log(sb.ToString());

        Finish(failures == 0 ? null : failures + " sticker(s) curl outside their own silhouette",
               failures == 0 ? 0 : 1, worst, stickerCount, worstWhere);
    }

    /// <summary>
    /// Every sticker sprite drawn on the page: the playable ones plus the decorative collage. Drop
    /// shadows, ghosts, residues and page furniture are not counted.
    /// </summary>
    static int CountStickers()
    {
        SpriteRenderer[] all = Object.FindObjectsByType<SpriteRenderer>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        List<string> names = new List<string>();
        for (int i = 0; i < all.Length; i++)
        {
            string n = all[i].name;
            if (n.StartsWith("Sticker_") || n.StartsWith("Deco_")) names.Add(n);
        }
        return names.Count;
    }

    static void Finish(string fatal, int exitCode, float worst, int stickerCount, string worstWhere = "-")
    {
        StringBuilder sb = new StringBuilder();
        if (!string.IsNullOrEmpty(fatal)) sb.AppendLine("[Case3Silhouette] FATAL " + fatal);
        sb.AppendLine(string.Format(
            "[Case3Silhouette] SILHOUETTE_GATE {0} worstRatio={1:0.000} (limit {2:0.00}) at {3}; stickers={4}",
            exitCode == 0 ? "GREEN" : "RED", worst, MaxRatio, worstWhere, stickerCount));

        Debug.Log(sb.ToString());
        System.Console.WriteLine(sb.ToString());

        SessionState.SetInt(KeyActive, 0);
        EditorApplication.update -= Drive;
        _hooked = false;

        if (Application.isBatchMode) EditorApplication.Exit(exitCode);
        else EditorApplication.isPlaying = false;
    }
}
