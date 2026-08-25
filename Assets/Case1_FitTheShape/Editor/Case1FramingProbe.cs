using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Case1;

/// <summary>
/// Measures the two structural invariants Case 1's framing has to satisfy, from the SCENE GRAPH rather
/// than from a screenshot. A silhouette cannot fake either of them:
///
///   I-A  PLAYABILITY. Every tappable tray piece must be drawn IN FRONT of the drum at its own screen
///        position. Measured as camera-forward depth: for each tray piece, every drum renderer whose
///        screen-space rect contains the piece's screen centre is compared against the piece's own
///        depth. A drum renderer that is both over the piece on screen AND nearer the lens hides it.
///        Reported per piece, plus the global separation min(drum depth) - max(tray depth).
///
///   I-B  FRAMING. The live row's leftmost and rightmost DRAWN pixels - arrow caps included - must fall
///        inside the 1080 px frame, and the row's width must land near the reference's share of it.
///        Measured by projecting every renderer of the slot band and of the live-row cells and taking
///        the union of their screen rects.
///
/// Read-only. It opens the scene, prints, and exits; it never writes.
/// </summary>
public static class Case1FramingProbe
{
    const string ScenePath = "Assets/Case1_FitTheShape/Scenes/FitTheShape.unity";
    const float FrameW = 1080f;
    const float FrameH = 1728f;

    /// <summary>MEASURED off docs/reference/case1/CASE1_TEPSI.png, scanline y = 435 (the live row):
    /// the drawn band runs x 149..962, i.e. 0.753 of the 1080 px frame. See the report for the run
    /// that produced it.</summary>
    const float RefRowWidthShare = 0.753f;

    public static void Probe()
    {
        Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        Camera cam = Object.FindFirstObjectByType<Camera>(FindObjectsInactive.Include);
        if (cam == null) { Debug.LogError("[C1Probe] no camera"); Done(2); return; }
        float prevAspect = cam.aspect;
        cam.aspect = Shared.View.AspectRatioEnforcer.TargetAspect;

        Transform drumRoot = Find(scene, "Drum");
        if (drumRoot == null) { Debug.LogError("[C1Probe] no Drum root"); Done(2); return; }

        Debug.Log(string.Format("[C1Probe] CAMERA pos=({0:0.0000},{1:0.0000},{2:0.0000}) euler=({3:0.00},{4:0.00},{5:0.00}) fov={6:0.000} aspect={7:0.0000}",
            cam.transform.position.x, cam.transform.position.y, cam.transform.position.z,
            cam.transform.eulerAngles.x, cam.transform.eulerAngles.y, cam.transform.eulerAngles.z,
            cam.fieldOfView, cam.aspect));
        Debug.Log(string.Format("[C1Probe] DRUM_ROOT pos=({0:0.0000},{1:0.0000},{2:0.0000})",
            drumRoot.position.x, drumRoot.position.y, drumRoot.position.z));

        // ---------------------------------------------------------------- drum renderers
        List<Renderer> drum = new List<Renderer>();
        foreach (Renderer r in drumRoot.GetComponentsInChildren<Renderer>(false))
            if (r.enabled && r.gameObject.activeInHierarchy) drum.Add(r);

        float drumMinDepth = float.MaxValue, drumMaxDepth = float.MinValue;
        Rect drumRect = new Rect();
        bool haveDrumRect = false;
        float drumLowestPixelY = float.MinValue;   // largest py = lowest on screen
        for (int i = 0; i < drum.Count; i++)
        {
            Rect rc; float dMin, dMax;
            if (!ScreenRect(cam, drum[i].bounds, out rc, out dMin, out dMax)) continue;
            if (dMin < drumMinDepth) drumMinDepth = dMin;
            if (dMax > drumMaxDepth) drumMaxDepth = dMax;
            drumRect = haveDrumRect ? Union(drumRect, rc) : rc;
            haveDrumRect = true;
            if (rc.yMax > drumLowestPixelY) drumLowestPixelY = rc.yMax;
        }
        Debug.Log(string.Format("[C1Probe] DRUM renderers={0} depth=[{1:0.0000}..{2:0.0000}] screenRect x=[{3:0.0}..{4:0.0}] y=[{5:0.0}..{6:0.0}] lowestPy={7:0.0}",
            drum.Count, drumMinDepth, drumMaxDepth, drumRect.xMin, drumRect.xMax, drumRect.yMin, drumRect.yMax, drumLowestPixelY));

        // ---------------------------------------------------------------- I-B: live row span
        // The live row is the band the player aims at: the slot band (frame + arrow caps) plus the
        // row-0 cells it frames.
        Rect rowRect = new Rect(); bool haveRow = false;
        Transform band = Find(scene, "Case1_SlotBand");
        if (band != null)
            foreach (Renderer r in band.GetComponentsInChildren<Renderer>(false))
            {
                Rect rc; float a, b;
                if (!r.enabled || !r.gameObject.activeInHierarchy) continue;
                if (!ScreenRect(cam, r.bounds, out rc, out a, out b)) continue;
                rowRect = haveRow ? Union(rowRect, rc) : rc; haveRow = true;
            }
        DrumSlotReaction dsr = Object.FindFirstObjectByType<DrumSlotReaction>(FindObjectsInactive.Include);
        int liveCells = 0;
        if (dsr != null && dsr.cells != null)
            for (int i = 0; i < dsr.cells.Length; i++)
            {
                DrumSlotReaction.Cell c = dsr.cells[i];
                if (c == null || c.root == null || c.row != 0) continue;
                foreach (Renderer r in c.root.GetComponentsInChildren<Renderer>(false))
                {
                    Rect rc; float a, b;
                    if (!r.enabled || !r.gameObject.activeInHierarchy) continue;
                    if (!ScreenRect(cam, r.bounds, out rc, out a, out b)) continue;
                    rowRect = haveRow ? Union(rowRect, rc) : rc; haveRow = true;
                }
                liveCells++;
            }

        Transform hl = Find(scene, "HolderLeft_Outer");
        Transform hr = Find(scene, "HolderRight_Outer");
        for (int k = 0; k < 2; k++)
        {
            Transform t = k == 0 ? hl : hr;
            if (t == null) { Debug.Log("[C1Probe] HOLDER " + (k == 0 ? "Left" : "Right") + "_Outer NOT FOUND"); continue; }
            Vector2 sp; float sd;
            Project(cam, t.position, out sp, out sd);
            Debug.Log(string.Format("[C1Probe] HOLDER {0}_Outer world=({1:0.0000},{2:0.0000},{3:0.0000}) px=({4:0.0},{5:0.0}) depth={6:0.0000} inFrame={7}",
                k == 0 ? "Left" : "Right", t.position.x, t.position.y, t.position.z,
                sp.x, sp.y, sd, sp.x >= 0f && sp.x <= FrameW));
        }

        float rowShare = haveRow ? rowRect.width / FrameW : -1f;
        bool rowInFrame = haveRow && rowRect.xMin >= 0f && rowRect.xMax <= FrameW;
        Debug.Log(string.Format("[C1Probe] I-B LIVE_ROW cells={0} x=[{1:0.0}..{2:0.0}] width={3:0.0}px share={4:0.0000} refShare={5:0.0000} inFrame={6}",
            liveCells, rowRect.xMin, rowRect.xMax, rowRect.width, rowShare, RefRowWidthShare, rowInFrame));

        // ---------------------------------------------------------------- I-A: playability
        DeckReflow deck = Object.FindFirstObjectByType<DeckReflow>(FindObjectsInactive.Include);
        Case1Director dir = Object.FindFirstObjectByType<Case1Director>(FindObjectsInactive.Include);
        ShapeArcFlight flight = dir != null ? dir.flight : null;

        List<Transform> pieces = new List<Transform>();
        List<int> slots = new List<int>();
        if (deck != null && deck.entries != null)
            for (int i = 0; i < deck.entries.Length; i++)
            {
                DeckReflow.Entry e = deck.entries[i];
                if (e == null || e.shape == null) continue;
                pieces.Add(e.shape); slots.Add(e.slot);
            }

        int columns = deck != null ? deck.columns : 3;
        float trayMaxDepth = float.MinValue;
        int occluded = 0, occludedFront = 0;
        StringBuilder sb = new StringBuilder();
        for (int i = 0; i < pieces.Count; i++)
        {
            Transform p = pieces[i];
            Bounds pb;
            if (!WorldBounds(p, out pb)) continue;
            Rect prc; float pMin, pMax;
            if (!ScreenRect(cam, pb, out prc, out pMin, out pMax)) continue;
            if (pMax > trayMaxDepth) trayMaxDepth = pMax;
            Vector2 centre = prc.center;

            // Depth of the drum AT THIS PIECE'S OWN SCREEN POSITION.
            float drumDepthHere = float.MaxValue;
            string blocker = "-";
            for (int d = 0; d < drum.Count; d++)
            {
                Rect rc; float dMin, dMax;
                if (!ScreenRect(cam, drum[d].bounds, out rc, out dMin, out dMax)) continue;
                if (!rc.Contains(centre)) continue;
                if (dMin < drumDepthHere) { drumDepthHere = dMin; blocker = drum[d].transform.parent != null ? drum[d].transform.parent.name : drum[d].name; }
            }
            bool hidden = drumDepthHere < pMin;
            bool front = columns > 0 && slots[i] < columns;
            if (hidden) { occluded++; if (front) occludedFront++; }
            int flightIdx = flight != null ? flight.IndexOf(p) : -1;
            sb.AppendLine(string.Format(
                "[C1Probe] PIECE slot={0} {1,-26} world=({2:0.0000},{3:0.0000},{4:0.0000}) px=({5:0.0},{6:0.0}) depth={7:0.0000} drumDepthHere={8} blocker={9} HIDDEN={10} front={11} flightIdx={12}",
                slots[i], p.name, p.position.x, p.position.y, p.position.z,
                centre.x, centre.y, pMin,
                drumDepthHere == float.MaxValue ? "none" : drumDepthHere.ToString("0.0000"), blocker,
                hidden, front, flightIdx));
        }
        Debug.Log(sb.ToString());
        Debug.Log(string.Format("[C1Probe] I-A PLAYABILITY pieces={0} occluded={1} occludedFrontRow={2} columns={3} separation(min drum depth - max tray depth)={4:0.0000}",
            pieces.Count, occluded, occludedFront, columns, drumMinDepth - trayMaxDepth));

        Debug.Log(string.Format("[C1Probe] VERDICT I-A={0} I-B={1}",
            occluded == 0 ? "PASS" : "FAIL", (rowInFrame && Mathf.Abs(rowShare - RefRowWidthShare) <= 0.04f) ? "PASS" : "FAIL"));

        cam.aspect = prevAspect;
        Done(0);
    }

    static void Done(int code)
    {
        Debug.Log("[C1Probe] PROBE_DONE");
    }

    static bool WorldBounds(Transform t, out Bounds b)
    {
        b = new Bounds();
        Renderer[] rs = t.GetComponentsInChildren<Renderer>(false);
        bool any = false;
        for (int i = 0; i < rs.Length; i++)
        {
            if (!rs[i].enabled || !rs[i].gameObject.activeInHierarchy) continue;
            if (!any) { b = rs[i].bounds; any = true; } else b.Encapsulate(rs[i].bounds);
        }
        return any;
    }

    /// <summary>
    /// Projects a world point into the CAPTURE frame - 1080 x 1728, y measured from the TOP - using the
    /// camera's own matrices rather than <c>WorldToScreenPoint</c>. In batchmode Unity's screen is
    /// 640 x 1024, so WorldToScreenPoint returns pixels in a frame that is not the one being captured;
    /// the first run of this probe reported the holder at px 674 instead of 1138 for exactly that reason.
    /// Returns false when the point is behind the lens. <paramref name="depth"/> is camera-forward
    /// distance, the quantity that decides who is drawn in front.
    /// </summary>
    static bool Project(Camera cam, Vector3 world, out Vector2 px, out float depth)
    {
        Matrix4x4 vp = cam.projectionMatrix * cam.worldToCameraMatrix;
        Vector4 clip = vp * new Vector4(world.x, world.y, world.z, 1f);
        px = Vector2.zero; depth = 0f;
        if (clip.w <= 0.0001f) return false;
        depth = clip.w;
        px = new Vector2((clip.x / clip.w * 0.5f + 0.5f) * FrameW,
                         (1f - (clip.y / clip.w * 0.5f + 0.5f)) * FrameH);
        return true;
    }

    /// <summary>Capture-frame rect (x, y-from-top) and camera-forward depth range of a world AABB.</summary>
    static bool ScreenRect(Camera cam, Bounds b, out Rect rect, out float depthMin, out float depthMax)
    {
        rect = new Rect(); depthMin = float.MaxValue; depthMax = float.MinValue;
        Vector3 c = b.center, e = b.extents;
        float xMin = float.MaxValue, xMax = float.MinValue, yMin = float.MaxValue, yMax = float.MinValue;
        int ok = 0;
        for (int i = 0; i < 8; i++)
        {
            Vector3 p = c + new Vector3((i & 1) == 0 ? -e.x : e.x, (i & 2) == 0 ? -e.y : e.y, (i & 4) == 0 ? -e.z : e.z);
            Vector2 sp; float d;
            if (!Project(cam, p, out sp, out d)) continue;
            ok++;
            if (sp.x < xMin) xMin = sp.x; if (sp.x > xMax) xMax = sp.x;
            if (sp.y < yMin) yMin = sp.y; if (sp.y > yMax) yMax = sp.y;
            if (d < depthMin) depthMin = d;
            if (d > depthMax) depthMax = d;
        }
        if (ok == 0) return false;
        rect = Rect.MinMaxRect(xMin, yMin, xMax, yMax);
        return true;
    }

    static Rect Union(Rect a, Rect b)
    {
        return Rect.MinMaxRect(Mathf.Min(a.xMin, b.xMin), Mathf.Min(a.yMin, b.yMin),
                               Mathf.Max(a.xMax, b.xMax), Mathf.Max(a.yMax, b.yMax));
    }

    static Transform Find(Scene scene, string name)
    {
        foreach (GameObject go in scene.GetRootGameObjects())
        {
            if (go.name == name) return go.transform;
            Transform f = Descend(go.transform, name);
            if (f != null) return f;
        }
        return null;
    }

    static Transform Descend(Transform t, string name)
    {
        for (int i = 0; i < t.childCount; i++)
        {
            if (t.GetChild(i).name == name) return t.GetChild(i);
            Transform f = Descend(t.GetChild(i), name);
            if (f != null) return f;
        }
        return null;
    }
}
