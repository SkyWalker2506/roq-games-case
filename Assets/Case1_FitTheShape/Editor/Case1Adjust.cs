using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// One-off scene adjustments, run by hand from the menu or by -executeMethod.
///
/// Deliberately NOT part of Case1SceneSetup.Build. The scene is hand-authored now and the builder no
/// longer places anything; these are edits a person asked for, applied once and then living in the
/// scene like every other authored value. Running one twice is safe - each method sets an absolute
/// result rather than nudging by a delta.
/// </summary>
public static class Case1Adjust
{
    const string ScenePath = "Assets/Case1_FitTheShape/Scenes/FitTheShape.unity";

    /// <summary>
    /// Pulls the holder plates into one even row and parks SPIN at its right end, the way the
    /// reference has them: five plates on a constant pitch with the button immediately after, all at
    /// the same height and depth.
    /// </summary>
    public static void HolderRowNextToSpin()
    {
        Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

        List<Transform> plates = new List<Transform>(8);
        foreach (GameObject go in scene.GetRootGameObjects()) Collect(go.transform, "DeckSlot_", plates);
        if (plates.Count == 0) { Debug.LogError("[Case1Adjust] no DeckSlot_* found"); return; }
        plates.Sort((a, b) => a.position.x.CompareTo(b.position.x));

        Transform spin = Find(scene, "Case1_ReferenceChrome");
        if (spin == null) { Debug.LogError("[Case1Adjust] no Case1_ReferenceChrome found"); return; }

        // The row's own left edge, height and depth are kept - the author placed those. Only the
        // spacing is regularised, and SPIN is brought up against the end of the row instead of sitting
        // off on its own.
        Bounds first = Bounds(plates[0]);
        float pitch = PlatePitchFrom(plates);
        float y = first.center.y, z = first.center.z, x0 = first.center.x;

        for (int i = 0; i < plates.Count; i++)
        {
            Bounds b = Bounds(plates[i]);
            plates[i].position += new Vector3(x0 + i * pitch, y, z) - b.center;
            EditorUtility.SetDirty(plates[i]);
        }

        Bounds last = Bounds(plates[plates.Count - 1]);
        Bounds sb = Bounds(spin);
        // A gap of a third of a plate: touching reads as one object, and the reference leaves daylight.
        float gap = pitch * 0.33f;
        float spinX = last.max.x + gap + sb.extents.x;
        spin.position += new Vector3(spinX, y, z) - sb.center;
        EditorUtility.SetDirty(spin);

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Debug.Log(string.Format("[Case1Adjust] HOLDER_ROW {0} plates on pitch {1:0.000} at Y {2:0.00} Z {3:0.00} " +
                                "| SPIN centre X {4:0.00}", plates.Count, pitch, y, z, spinX));
    }

    /// <summary>
    /// Raises the camera so it looks further down on the board, keeping what it is aimed AT. The
    /// camera slides up its own arc around the aim point, so the subject stays centred and only the
    /// angle changes - moving it any other way re-frames the shot as a side effect.
    /// </summary>
    public static void CameraFromHigherUp()
    {
        Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        Camera cam = Object.FindFirstObjectByType<Camera>(FindObjectsInactive.Include);
        if (cam == null) { Debug.LogError("[Case1Adjust] no camera"); return; }

        // Aimed at ALL the content, not just the board. Orbiting the board alone raised the angle and
        // dropped the tray straight out of the bottom of the frame - the subject is the whole table.
        Bounds content = ContentBounds(scene, cam);
        if (content.size.sqrMagnitude < 1e-6f) { Debug.LogError("[Case1Adjust] nothing to frame"); return; }

        float before = cam.transform.eulerAngles.x;

        // TargetPitch is absolute, so running this twice does not tip the camera over. 26 was tried
        // first and LOWERED it (the camera already sat at 30), and 38 threw the tray off screen; 34
        // reads as looking down on a table while still holding the whole layout.
        const float TargetPitch = 34f;
        float pitch = TargetPitch * Mathf.Deg2Rad;

        // Distance solved against what the frame ACTUALLY shows, iteratively. The first attempt used
        // the bounding sphere's radius, which over-pads a scene that is long and shallow: everything
        // fitted and the whole layout sat small in the middle of the frame.
        const float FillHeight = 0.88f;      // share of the frame height the content should occupy
        Vector3 aim = content.center;
        float halfFov = cam.fieldOfView * 0.5f * Mathf.Deg2Rad;
        float distance = (content.extents.magnitude / Mathf.Tan(halfFov)) * 1.06f;

        cam.transform.rotation = Quaternion.Euler(TargetPitch, 0f, 0f);
        for (int pass = 0; pass < 12; pass++)
        {
            cam.transform.position = aim + new Vector3(0f,
                                                       distance * Mathf.Sin(pitch),
                                                       -distance * Mathf.Cos(pitch));
            Rect r;
            if (!Shared.EditorTools.ReferenceMatchLayout.ProjectBounds(cam, RenderersIn(scene), out r)) break;
            if (r.height < 1e-5f) break;
            float f = r.height / FillHeight;
            if (Mathf.Abs(f - 1f) < 0.005f) break;
            distance *= Mathf.Clamp(f, 0.5f, 2f);
        }
        EditorUtility.SetDirty(cam.transform);

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Debug.Log(string.Format("[Case1Adjust] CAMERA pitch {0:0.0} -> {1:0.0} | aim {2} | distance {3:0.0} | " +
                                "content {4}", before, TargetPitch, aim, distance, content.size));
    }

    /// <summary>Every renderer the shot should hold, for the framing solve.</summary>
    static List<Renderer> RenderersIn(Scene scene)
    {
        List<Renderer> all = new List<Renderer>(256);
        foreach (GameObject go in scene.GetRootGameObjects())
        foreach (Renderer r in go.GetComponentsInChildren<Renderer>(true))
        {
            if (r == null || r is ParticleSystemRenderer) continue;
            all.Add(r);
        }
        return all;
    }

    /// <summary>Bounds of everything the shot is meant to hold: board, tray, holder row and chrome.</summary>
    static Bounds ContentBounds(Scene scene, Camera cam)
    {
        Bounds b = new Bounds();
        bool any = false;
        foreach (GameObject go in scene.GetRootGameObjects())
        foreach (Renderer r in go.GetComponentsInChildren<Renderer>(true))
        {
            if (r == null || r is ParticleSystemRenderer) continue;
            if (r.GetComponentInParent<Camera>() != null) continue;
            if (!any) { b = r.bounds; any = true; } else b.Encapsulate(r.bounds);
        }
        return b;
    }

    /// <summary>
    /// Brings the reel DOWN and TOWARDS the viewer, so it sits closer to the holder row instead of
    /// hanging back and high.
    ///
    /// Both targets are absolute and derived from the holder row itself - the reel's bottom clears the
    /// plates by a set share of its own height, and its front face stands a set share of its own depth
    /// behind them. Running this twice therefore lands in the same place; nudging by a delta would walk
    /// the reel across the table one run at a time.
    /// </summary>
    public static void ReelLowerAndCloser()
    {
        Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        Transform drum = Find(scene, "Drum");
        if (drum == null) { Debug.LogError("[Case1Adjust] no Drum"); return; }

        List<Transform> plates = new List<Transform>(8);
        foreach (GameObject go in scene.GetRootGameObjects()) Collect(go.transform, "DeckSlot_", plates);
        if (plates.Count == 0) { Debug.LogError("[Case1Adjust] no DeckSlot_* to measure against"); return; }

        Bounds row = Bounds(plates[0]);
        for (int i = 1; i < plates.Count; i++) row.Encapsulate(Bounds(plates[i]));

        Bounds d = Bounds(drum);
        Vector3 before = drum.position;

        const float ClearAbovePlates = 0.10f;   // of the reel's own height
        // 0.35 of the reel's depth put its front face 1.27 BEHIND the plates - further from the viewer,
        // the opposite of what was asked. It stands just clear of the row now.
        const float StandBackFromRow = 0.04f;   // of the reel's own depth
        float wantBottomY = row.max.y + d.size.y * ClearAbovePlates;
        float wantFrontZ = row.max.z + d.size.z * StandBackFromRow;

        drum.position += new Vector3(0f, wantBottomY - d.min.y, wantFrontZ - d.min.z);
        EditorUtility.SetDirty(drum);

        // The rail and the arrows were placed against the reel's live row, so they ride with it.
        Vector3 delta = drum.position - before;
        foreach (string name in new[] { "Case1_SlotBand", "Case1_UnknownMarks" })
        {
            Transform t = Find(scene, name);
            if (t == null) continue;
            t.position += delta;
            EditorUtility.SetDirty(t);
        }

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Debug.Log(string.Format("[Case1Adjust] REEL moved {0} -> bottom Y {1:0.00}, front Z {2:0.00} " +
                                "(plates top {3:0.00}, back {4:0.00})",
                                delta, wantBottomY, wantFrontZ, row.max.y, row.max.z));
    }

    /// <summary>
    /// Stands the reel BACK, behind the whole tray, at the reference's size in the frame.
    ///
    /// Two measured faults share this one root cause, so they share this one fix:
    ///
    ///   * The tray was UNPLAYABLE. Its three rows sit on the ground plane at world z 9.3305 / 7.1797 /
    ///     5.2196 while the reel's cells sat at z 6.75..7.35, so the back two rows - six of the nine
    ///     pieces, INCLUDING all three tappable front-row slots - were farther from the lens than the
    ///     reel and were painted over by it. The only piece a player could reach was reachable because
    ///     ShapeTapInput picks by screen proximity and does not care what is drawn on top.
    ///   * The reel OVERFLOWED the frame. Cap to cap it measured 1293 px against a 1080 px frame, and
    ///     both light-blue arrow caps - clearly present in the reference - projected outside it
    ///     (HolderLeft_Outer at px 1138.6, its mirror at px -62.1).
    ///
    /// Standing the reel further back shrinks it in the frame AND puts it behind every tray row, so one
    /// rigid translation answers both. It cannot be a translation in Z alone: at this pitch a receding
    /// object also RISES in the frame, and pure +Z carried the live row to py -74, off the top. The Y
    /// component is what holds the row at the reference's height while the Z component sets its size.
    ///
    /// Both targets are ABSOLUTE and re-derived from the scene on every run, so this converges rather
    /// than nudges: run it twice and the second run moves the reel by ~0.
    ///
    /// MEASURED off docs/reference/case1/CASE1_TEPSI.png (1080x1728): the reel's bbox spans x 149..963,
    /// i.e. 815 px cap to cap, and the live row's white frame rails sit at y 358..364 and 515..521, so
    /// the row's centre is py 439.5. (Case1SceneSetup's own RefLiveRowCentre, 0.737 of the viewport =
    /// py 454.5, was measured from a different frame and agrees to within 15 px.)
    /// </summary>
    public static void ReelBackToReferenceFrame()
    {
        Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        Camera cam = Object.FindFirstObjectByType<Camera>(FindObjectsInactive.Include);
        Transform drum = Find(scene, "Drum");
        if (cam == null || drum == null) { Debug.LogError("[Case1Adjust] no camera or no Drum"); return; }

        // The builder's own camera values, so this solve does not depend on when it is run relative to
        // Case1SceneSetup.Build. They are re-applied there anyway.
        cam.fieldOfView = 10.5f;
        cam.transform.position = new Vector3(0f, 19f, -24f);
        cam.transform.rotation = Quaternion.Euler(25f, 0f, 0f);
        float prevAspect = cam.aspect;
        cam.aspect = Shared.View.AspectRatioEnforcer.TargetAspect;

        const float RefRowWidthPx = 815f;
        const float RefRowCentrePy = 439.5f;

        // The rail and the unknown marks were built against the live row, so they RIDE WITH IT - and
        // they must ride inside the loop, not after it. The row's widest pixels are the rail's arrow
        // caps, so leaving the rail behind made every pass re-measure the ORIGINAL width and apply the
        // same step three times over: the reel ended up 70 units back at 20% of the frame.
        List<Transform> riders = new List<Transform>(2);
        foreach (string name in new[] { "Case1_SlotBand", "Case1_UnknownMarks" })
        {
            Transform t = Find(scene, name);
            if (t != null) riders.Add(t);
        }

        Vector3 before = drum.position;
        Vector3 total = Vector3.zero;
        Vector3 fwd = cam.transform.forward, up = cam.transform.up;
        float tanHalf = Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad);

        // Three passes. The linear solve is exact only for points at ONE depth, and the row's extreme
        // pixels come from the arrow caps while its centre comes from their origins; re-measuring after
        // each step converges the two onto their targets instead of trusting one linearisation.
        for (int pass = 0; pass < 8; pass++)
        {
            float w0, y0, d0;
            if (!MeasureLiveRow(scene, cam, out w0, out y0, out d0))
            { Debug.LogError("[Case1Adjust] could not measure the live row"); break; }

            float d1 = d0 * (w0 / RefRowWidthPx);                      // width scales as 1/depth
            float ndc0 = 1f - 2f * y0 / 1728f;
            float ndc1 = 1f - 2f * RefRowCentrePy / 1728f;
            float dForward = d1 - d0;
            float dUp = ndc1 * tanHalf * d1 - ndc0 * tanHalf * d0;

            Vector3 step = up * dUp + fwd * dForward;                  // camera basis -> world
            step.x = 0f;                                                // the row is centred; keep it there
            drum.position += step;
            for (int i = 0; i < riders.Count; i++) riders[i].position += step;
            total += step;
            Debug.Log(string.Format("[Case1Adjust] REEL_FRAME pass {0}: width {1:0.0} px (want {2:0.0}), " +
                                    "centre py {3:0.0} (want {4:0.0}), depth {5:0.000} -> step {6}",
                                    pass, w0, RefRowWidthPx, y0, RefRowCentrePy, d0, step));
            if (step.magnitude < 0.01f) break;
        }

        EditorUtility.SetDirty(drum);
        for (int i = 0; i < riders.Count; i++) EditorUtility.SetDirty(riders[i]);

        // MEASURED BEFORE the aspect is restored. Reported after, it came out 382 px instead of 815 -
        // batchmode's screen is 640x480, so cam.aspect goes back to 1.333 and the same geometry
        // projects at less than half the width. A log line is a measurement like any other.
        float wEnd, yEnd, dEnd; MeasureLiveRow(scene, cam, out wEnd, out yEnd, out dEnd);

        cam.aspect = prevAspect;
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);

        Debug.Log(string.Format("[Case1Adjust] REEL_FRAME reel {0} -> {1} (moved {2}) | live row {3:0.0} px " +
                                "({4:0.0000} of frame) centred at py {5:0.0}",
                                before, drum.position, total, wEnd, wEnd / 1080f, yEnd));
    }

    /// <summary>
    /// The live row as the frame sees it: width in CAPTURE pixels of the union of the slot band and the
    /// row-0 cells (the arrow caps are the widest part, exactly as in the reference), the py of the
    /// caps' own centre, and the camera-forward depth of the widest points.
    ///
    /// Projected with the camera's matrices, NOT WorldToScreenPoint: in batchmode Unity's screen is
    /// 640x1024 and WorldToScreenPoint answers in THAT frame, which reported the holder at px 674
    /// instead of 1138 the first time this was measured.
    /// </summary>
    static bool MeasureLiveRow(Scene scene, Camera cam, out float widthPx, out float centrePy, out float depth)
    {
        widthPx = 0f; centrePy = 0f; depth = 0f;
        Matrix4x4 vp = cam.projectionMatrix * cam.worldToCameraMatrix;

        List<Renderer> rs = new List<Renderer>(64);
        Transform band = Find(scene, "Case1_SlotBand");
        if (band != null)
            foreach (Renderer r in band.GetComponentsInChildren<Renderer>(false))
                if (r.enabled && r.gameObject.activeInHierarchy && !(r is ParticleSystemRenderer)) rs.Add(r);

        Case1.DrumSlotReaction dsr = Object.FindFirstObjectByType<Case1.DrumSlotReaction>(FindObjectsInactive.Include);
        if (dsr != null && dsr.cells != null)
            for (int i = 0; i < dsr.cells.Length; i++)
            {
                Case1.DrumSlotReaction.Cell c = dsr.cells[i];
                if (c == null || c.root == null || c.row != 0) continue;
                foreach (Renderer r in c.root.GetComponentsInChildren<Renderer>(false))
                    if (r.enabled && r.gameObject.activeInHierarchy && !(r is ParticleSystemRenderer)) rs.Add(r);
            }
        if (rs.Count == 0) return false;

        float xMin = float.MaxValue, xMax = float.MinValue, dAtMin = 0f, dAtMax = 0f;
        for (int i = 0; i < rs.Count; i++)
        {
            Bounds b = rs[i].bounds;
            Vector3 c = b.center, e = b.extents;
            for (int k = 0; k < 8; k++)
            {
                Vector3 p = c + new Vector3((k & 1) == 0 ? -e.x : e.x, (k & 2) == 0 ? -e.y : e.y,
                                            (k & 4) == 0 ? -e.z : e.z);
                Vector4 clip = vp * new Vector4(p.x, p.y, p.z, 1f);
                if (clip.w <= 0.0001f) continue;
                float px = (clip.x / clip.w * 0.5f + 0.5f) * 1080f;
                if (px < xMin) { xMin = px; dAtMin = clip.w; }
                if (px > xMax) { xMax = px; dAtMax = clip.w; }
            }
        }
        if (xMax <= xMin) return false;
        widthPx = xMax - xMin;
        depth = (dAtMin + dAtMax) * 0.5f;

        // The band's own vertical centre, taken from the two arrow caps. The union rect's y-centre would
        // include the cells, which stand well above and below the rail the reference measurement used.
        int n = 0; float sum = 0f;
        foreach (string name in new[] { "HolderLeft_Outer", "HolderRight_Outer" })
        {
            Transform t = Find(scene, name);
            if (t == null) continue;
            Vector4 clip = vp * new Vector4(t.position.x, t.position.y, t.position.z, 1f);
            if (clip.w <= 0.0001f) continue;
            sum += (1f - (clip.y / clip.w * 0.5f + 0.5f)) * 1728f; n++;
        }
        if (n == 0) return false;
        centrePy = sum / n;
        return true;
    }

    /// <summary>Both adjustments, for a single -executeMethod run.</summary>
    public static void All()
    {
        HolderRowNextToSpin();
        ReelLowerAndCloser();
        CameraFromHigherUp();   // last: the framing follows whatever the world ended up being
    }

    /// <summary>Median gap between neighbouring plates, so one stray plate cannot set the pitch.</summary>
    static float PlatePitchFrom(List<Transform> plates)
    {
        if (plates.Count < 2) return 1f;
        List<float> gaps = new List<float>(plates.Count - 1);
        for (int i = 1; i < plates.Count; i++) gaps.Add(Bounds(plates[i]).center.x - Bounds(plates[i - 1]).center.x);
        gaps.Sort();
        return gaps[gaps.Count / 2];
    }

    static Bounds Bounds(Transform t)
    {
        Renderer[] rs = t.GetComponentsInChildren<Renderer>(true);
        Bounds b = new Bounds(t.position, Vector3.zero);
        bool any = false;
        foreach (Renderer r in rs)
        {
            if (r == null || r is ParticleSystemRenderer) continue;
            if (!any) { b = r.bounds; any = true; } else b.Encapsulate(r.bounds);
        }
        return b;
    }

    static void Collect(Transform t, string prefix, List<Transform> into)
    {
        if (t.name.StartsWith(prefix)) { into.Add(t); return; }
        for (int i = 0; i < t.childCount; i++) Collect(t.GetChild(i), prefix, into);
    }

    static Transform Find(Scene scene, string name)
    {
        foreach (GameObject go in scene.GetRootGameObjects())
        {
            if (go.name == name) return go.transform;
            Transform t = FindIn(go.transform, name);
            if (t != null) return t;
        }
        return null;
    }

    static Transform FindIn(Transform parent, string name)
    {
        for (int i = 0; i < parent.childCount; i++)
        {
            Transform c = parent.GetChild(i);
            if (c.name == name) return c;
            Transform deeper = FindIn(c, name);
            if (deeper != null) return deeper;
        }
        return null;
    }
}
