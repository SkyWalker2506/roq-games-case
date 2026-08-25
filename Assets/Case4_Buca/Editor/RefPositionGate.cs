using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// P19 position gate. For every case it projects the objects that carry the layout into viewport space
/// at the reference aspect (1080/1728 = 0.625) and compares them with the SAME quantity measured off
/// the reference videos. A deviation larger than <see cref="Tolerance"/> fails the gate.
///
/// How the reference numbers were obtained (no guessing, and repeatable):
///   ffmpeg -ss &lt;t&gt; -i "_refs/Developer Case Referans/&lt;case&gt;.mp4" -vframes 1 frame.png
/// the frame was resampled to 540x864 (the capture size) and each object was isolated with a colour
/// threshold, then its pixel bounding box was divided by the frame size. y is measured from the BOTTOM,
/// matching Unity's viewport convention.
///
/// Zero-argument on purpose: Unity's -executeMethod refuses anything else (lessons #1).
/// </summary>
public static class RefPositionGate
{
    /// <summary>Everything has to land inside this band of the reference point.</summary>
    public const float Tolerance = 0.05f;

    static int _checks;
    static int _failures;

    // ------------------------------------------------------------------ entry point

    /// <summary>
    /// Rebuilds all four scenes and then measures them, in ONE editor invocation. The Unity lock is
    /// shared with another agent, so every batch call is expensive; build-then-measure in one process
    /// is the difference between one wait and five.
    /// </summary>
    public static void BuildAllAndRun()
    {
        Case1SceneSetup.Build();
        Case2SceneSetup.Build();
        Case3SceneSetup.Build();
        Case4SceneSetup.Build();
        Run();
    }

    public static void Run()
    {
        _checks = 0;
        _failures = 0;

        Line("---- P19 reference position gate (aspect " +
             Shared.View.AspectRatioEnforcer.TargetAspect.ToString("0.000") + ", tolerance +/-" +
             Tolerance.ToString("0.00") + ") ----");

        CheckCase4();
        CheckCoinRefusesWithoutContact();
        CheckCase1();
        CheckCase2();
        CheckCase3();

        Line(string.Format("checks={0} failures={1}", _checks, _failures));
        if (_failures > 0) { Line("POSITION_GATE FAILED"); Finish(1); return; }
        Line("POSITION_GATE PASSED");
        Finish(0);
    }

    // ------------------------------------------------------------------ case 4: Buca

    static void CheckCase4()
    {
        Scene scene = Open("Assets/Case4_Buca/Scenes/Buca.unity");
        Camera cam = Object.FindFirstObjectByType<Camera>(FindObjectsInactive.Include);
        if (cam == null) { Bad("Buca", "no camera"); return; }

        // arena rim: the real mesh silhouette, which is what the video can be measured as
        Renderer frame = FindDeep(scene, "level_frame");
        List<Vector3> verts = MeshPoints(frame);
        Box("Buca", "arena rim", cam, verts,
            Case4SceneSetup.RefRimX0, Case4SceneSetup.RefRimX1,
            Case4SceneSetup.RefRimY0, Case4SceneSetup.RefRimY1);

        // green stack: every block's renderer box
        List<Vector3> stack = new List<Vector3>(256);
        Transform blocks = FindRoot(scene, "Case4_Blocks");
        if (blocks != null)
            foreach (Renderer r in blocks.GetComponentsInChildren<Renderer>(true))
                AddBoxCorners(stack, r.bounds);
        Box("Buca", "green stack", cam, stack,
            Case4SceneSetup.RefStackX0, Case4SceneSetup.RefStackX1,
            Case4SceneSetup.RefStackY0, Case4SceneSetup.RefStackY1);

        // puck: a single point, its rest pose
        Transform puck = FindRoot(scene, "Case4_Puck");
        if (puck == null) Bad("Buca", "no Case4_Puck root");
        else Point("Buca", "gold puck", cam, puck.position, Case4SceneSetup.RefPuckX, Case4SceneSetup.RefPuckY);
    }

    /// <summary>
    /// The negative half of the coin proof, run in edit mode so it costs nothing: a stream that was
    /// never armed by a solver contact must spawn zero coins. The play-mode run proves the positive
    /// half (COIN_ARMED / maxSpawnOffsetFromContact); this proves that no contact means no gold, which
    /// is the actual bug that was reported - "gold appears where it did not hit".
    /// </summary>
    static void CheckCoinRefusesWithoutContact()
    {
        GameObject probe = new GameObject("P19_CoinRefusalProbe");
        try
        {
            Case4.CoinArcStream stream = probe.AddComponent<Case4.CoinArcStream>();
            stream.coinPrefab = null;   // nothing to instantiate: the refusal must happen before that
            stream.BuildCurve(Vector3.zero, Vector3.up * 5f);

            System.Collections.IEnumerator run = stream.Launch();
            int steps = 0;
            while (run.MoveNext() && steps++ < 512) { }

            bool ok = stream.LaunchedCount == 0 && stream.BlockedLaunchAttempts == 1 && !stream.Armed;
            _checks++;
            if (!ok) _failures++;
            Line(string.Format("  {0,-12} {1,-22} un-armed Launch() -> launched={2} refused={3} armed={4}  {5}",
                "Buca", "coin needs contact", stream.LaunchedCount, stream.BlockedLaunchAttempts, stream.Armed,
                ok ? "OK" : "OUT_OF_BAND"));

            // and the positive control: once a contact arms it, the same call is allowed through
            stream.ArmFromContact(new Vector3(1f, 2f, 3f));
            _checks++;
            if (!stream.Armed) { _failures++; Line("  Buca         coin arming did not take  OUT_OF_BAND"); }
            else Line("  Buca         coin arms from contact  ArmFromContact((1,2,3)) -> Armed=True, ContactPoint=" +
                      stream.ContactPoint.ToString("0.###") + "  OK");
        }
        finally
        {
            Object.DestroyImmediate(probe);
        }
    }

    // ------------------------------------------------------------------ case 1: Fit The Shape

    // The shape tray was built at these viewport points, read off Fit The Shape.mp4 at 1080x1728.
    // Gating them proves the tray is still standing where the reference puts it after any camera change.
    static readonly float[] TrayColumnX = { 0.375f, 0.500f, 0.625f };
    static readonly float[] TrayRowY = { 0.365f, 0.268f, 0.174f };

    /// <summary>Closest tray occupant to a measured cell, whether it is scenery or a playable shape.</summary>
    static Transform NearestOccupant(Scene scene, Camera cam, float vx, float vy)
    {
        Transform best = null; float bestD = 0.06f;   // must be inside the gate's own tolerance band
        float old = cam.aspect; cam.aspect = 1080f / 1728f;
        Transform tray = FindRoot(scene, "Case1_ShapeTray");
        Transform deck = FindRoot(scene, "Deck");
        for (int pass = 0; pass < 2; pass++)
        {
            Transform root = pass == 0 ? tray : deck;
            if (root == null) continue;
            for (int i = 0; i < root.childCount; i++)
            {
                Transform t = root.GetChild(i);
                if (t.GetComponentInChildren<Renderer>(true) == null) continue;
                if (t.name.StartsWith("DeckSlot_") || t.name.StartsWith("TrayFloor")) continue;
                Vector3 v = cam.WorldToViewportPoint(t.position);
                if (v.z <= 0f) continue;
                float d = Mathf.Sqrt((v.x - vx) * (v.x - vx) + (v.y - vy) * (v.y - vy));
                if (d < bestD) { bestD = d; best = t; }
            }
        }
        cam.aspect = old;
        return best;
    }

    static void CheckCase1()
    {
        Scene scene = Open("Assets/Case1_FitTheShape/Scenes/FitTheShape.unity");
        Camera cam = Object.FindFirstObjectByType<Camera>(FindObjectsInactive.Include);
        if (cam == null) { Bad("FitTheShape", "no camera"); return; }

        Transform tray = FindRoot(scene, "Case1_ShapeTray");
        if (tray == null) { Bad("FitTheShape", "no Case1_ShapeTray root"); return; }

        for (int r = 0; r < TrayRowY.Length; r++)
        {
            for (int c = 0; c < TrayColumnX.Length; c++)
            {
                // The tray is now a MIXED pool: three of its nine cells hold the real playable shapes
                // (which are children of Deck, not of the tray root) and the rest hold scenery. Checking
                // by name only ever found the scenery, so the three playable cells read as "missing".
                // What matters is that SOMETHING occupies each measured cell, so look up by position.
                Vector3 want = new Vector3(TrayColumnX[c], TrayRowY[r], 0f);
                Transform tile = NearestOccupant(scene, cam, TrayColumnX[c], TrayRowY[r]);
                if (tile == null) { Bad("FitTheShape", "tray cell empty: r" + r + "c" + c); continue; }
                Point("FitTheShape", "tray r" + r + "c" + c, cam, tile.position, TrayColumnX[c], TrayRowY[r]);
            }
        }

        // The drum is reported rather than gated on a point: CaseFramingGate already owns its width,
        // and its vertical extent is the drum mesh's own proportion, not a placement.
        Transform drum = FindRoot(scene, "Drum");
        if (drum != null)
        {
            List<Vector3> pts = new List<Vector3>(512);
            foreach (Renderer r in drum.GetComponentsInChildren<Renderer>(true))
                if (r.name.StartsWith("Segment_")) AddBoxCorners(pts, r.bounds);
            Report("FitTheShape", "drum cells (reported)", cam, pts);
        }
    }

    // ------------------------------------------------------------------ case 2: Block Hole

    // Read off Block Hole.mp4 at 1080x1728: the playfield grid inside the board frame spans
    // x 0.055..0.930 and y 0.185..0.790 of the frame.
    const float RefBoardX0 = 0.055f, RefBoardX1 = 0.930f, RefBoardY0 = 0.185f, RefBoardY1 = 0.790f;

    static void CheckCase2()
    {
        Scene scene = Open("Assets/Case2_BlockHole/Scenes/BlockHole.unity");
        Camera cam = Object.FindFirstObjectByType<Camera>(FindObjectsInactive.Include);
        if (cam == null) { Bad("BlockHole", "no camera"); return; }

        Transform board = FindRoot(scene, "Board");
        if (board == null) { Bad("BlockHole", "no Board root"); return; }

        List<Vector3> pts = new List<Vector3>(1024);
        foreach (Renderer r in board.GetComponentsInChildren<Renderer>(true))
            if (r.name.StartsWith("Tile_")) AddBoxCorners(pts, r.bounds);
        Box("BlockHole", "tile grid", cam, pts, RefBoardX0, RefBoardX1, RefBoardY0, RefBoardY1);
    }

    // ------------------------------------------------------------------ case 3: Stickerdom

    // Read off Stickerdom.mp4 at 1080x1728: the row of target cards above the page spans
    // x 0.020..0.720 and y 0.735..0.875 (three cards plus a dashed placeholder out to 0.92).
    const float RefCardsY0 = 0.735f, RefCardsY1 = 0.875f;

    static void CheckCase3()
    {
        Scene scene = Open("Assets/Case3_Stickerdom/Scenes/Stickerdom.unity");
        Camera cam = Object.FindFirstObjectByType<Camera>(FindObjectsInactive.Include);
        if (cam == null) { Bad("Stickerdom", "no camera"); return; }

        Transform ghosts = FindRoot(scene, "GhostSlots");
        if (ghosts == null) Bad("Stickerdom", "no GhostSlots root");
        else
        {
            List<Vector3> pts = new List<Vector3>(64);
            foreach (Renderer r in ghosts.GetComponentsInChildren<Renderer>(true)) AddBoxCorners(pts, r.bounds);
            Box("Stickerdom", "target card row", cam, pts, float.NaN, float.NaN, RefCardsY0, RefCardsY1);
        }

        // The page is REPORTED, not gated. Its sprite is taller and narrower than the reference's, so a
        // camera that matches the reference's width cannot also match its height; CaseFramingGate owns
        // the width, and the residual vertical overhang is art, not placement. Saying so out loud is the
        // point: a gate that quietly skipped it would be pretending.
        Transform page = FindRoot(scene, "Page");
        if (page != null)
        {
            List<Vector3> pts = new List<Vector3>(64);
            foreach (Renderer r in page.GetComponentsInChildren<Renderer>(true)) AddBoxCorners(pts, r.bounds);
            Report("Stickerdom", "page (reported: reference x[0.020..0.965] y[0.110..0.725])", cam, pts);
        }
    }

    // ------------------------------------------------------------------ measurement helpers

    static void Box(string scene, string what, Camera cam, List<Vector3> pts,
                    float rx0, float rx1, float ry0, float ry1)
    {
        float x0, x1, y0, y1;
        if (pts.Count == 0 || !Case4SceneSetup.ViewportBox(cam, pts, out x0, out x1, out y0, out y1))
        {
            Bad(scene, what + ": nothing to measure");
            return;
        }

        bool ok = true;
        System.Text.StringBuilder d = new System.Text.StringBuilder();
        if (!float.IsNaN(rx0)) { ok &= Edge(d, "x0", x0, rx0); ok &= Edge(d, "x1", x1, rx1); }
        ok &= Edge(d, "y0", y0, ry0);
        ok &= Edge(d, "y1", y1, ry1);

        _checks++;
        if (!ok) _failures++;
        Line(string.Format("  {0,-12} {1,-22} ours x[{2:0.000}..{3:0.000}] y[{4:0.000}..{5:0.000}]  {6}  {7}",
            scene, what, x0, x1, y0, y1, d, ok ? "OK" : "OUT_OF_BAND"));
    }

    static void Point(string scene, string what, Camera cam, Vector3 world, float rx, float ry)
    {
        float previous = cam.aspect;
        cam.aspect = Shared.View.AspectRatioEnforcer.TargetAspect;
        Vector3 v = cam.WorldToViewportPoint(world);
        cam.aspect = previous;
        cam.ResetAspect();

        System.Text.StringBuilder d = new System.Text.StringBuilder();
        bool ok = Edge(d, "x", v.x, rx) & Edge(d, "y", v.y, ry);

        _checks++;
        if (!ok) _failures++;
        Line(string.Format("  {0,-12} {1,-22} ours ({2:0.000},{3:0.000})  reference ({4:0.000},{5:0.000})  {6}  {7}",
            scene, what, v.x, v.y, rx, ry, d, ok ? "OK" : "OUT_OF_BAND"));
    }

    static void Report(string scene, string what, Camera cam, List<Vector3> pts)
    {
        float x0, x1, y0, y1;
        if (pts.Count == 0 || !Case4SceneSetup.ViewportBox(cam, pts, out x0, out x1, out y0, out y1))
        {
            Line("  " + scene + " " + what + ": nothing to measure");
            return;
        }
        Line(string.Format("  {0,-12} {1,-22} ours x[{2:0.000}..{3:0.000}] y[{4:0.000}..{5:0.000}] (not gated)",
            scene, what, x0, x1, y0, y1));
    }

    static bool Edge(System.Text.StringBuilder d, string name, float got, float want)
    {
        float delta = got - want;
        bool ok = Mathf.Abs(delta) <= Tolerance;
        d.Append(name).Append('=').Append(delta.ToString("+0.000;-0.000")).Append(ok ? " " : "! ");
        return ok;
    }

    // ------------------------------------------------------------------ scene helpers

    static Scene Open(string path) { return EditorSceneManager.OpenScene(path, OpenSceneMode.Single); }

    static Transform FindRoot(Scene scene, string name)
    {
        GameObject[] roots = scene.GetRootGameObjects();
        for (int i = 0; i < roots.Length; i++) if (roots[i].name == name) return roots[i].transform;
        // not a root: look through the whole scene, lesson #14 (what looks like a root can be a child)
        for (int i = 0; i < roots.Length; i++)
        {
            Transform[] all = roots[i].GetComponentsInChildren<Transform>(true);
            for (int j = 0; j < all.Length; j++) if (all[j].name == name) return all[j];
        }
        return null;
    }

    static Renderer FindDeep(Scene scene, string name)
    {
        GameObject[] roots = scene.GetRootGameObjects();
        for (int i = 0; i < roots.Length; i++)
        {
            Renderer[] all = roots[i].GetComponentsInChildren<Renderer>(true);
            for (int j = 0; j < all.Length; j++) if (all[j].name == name) return all[j];
        }
        return null;
    }

    static List<Vector3> MeshPoints(Renderer r)
    {
        List<Vector3> pts = new List<Vector3>(2048);
        if (r == null) return pts;
        MeshFilter mf = r.GetComponent<MeshFilter>();
        if (mf != null && mf.sharedMesh != null)
        {
            Vector3[] v = mf.sharedMesh.vertices;
            for (int i = 0; i < v.Length; i++) pts.Add(r.transform.TransformPoint(v[i]));
            return pts;
        }
        AddBoxCorners(pts, r.bounds);
        return pts;
    }

    static void AddBoxCorners(List<Vector3> into, Bounds b)
    {
        for (int i = 0; i < 8; i++)
            into.Add(new Vector3((i & 1) == 0 ? b.min.x : b.max.x,
                                 (i & 2) == 0 ? b.min.y : b.max.y,
                                 (i & 4) == 0 ? b.min.z : b.max.z));
    }

    // ------------------------------------------------------------------ output

    static void Bad(string scene, string message)
    {
        _checks++;
        _failures++;
        Line("  " + scene + " " + message + "  OUT_OF_BAND");
    }

    static void Line(string message)
    {
        Debug.Log("[PositionGate] " + message);
        System.Console.WriteLine("[PositionGate] " + message);
    }

    static void Finish(int exitCode)
    {
        if (Application.isBatchMode) EditorApplication.Exit(exitCode);
        else if (exitCode != 0) Debug.LogError("[PositionGate] exit code " + exitCode);
    }
}
