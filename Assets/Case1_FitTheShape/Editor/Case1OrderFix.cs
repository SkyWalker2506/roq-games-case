using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Puts Case 1's world back in the right order without touching a single material, shader or scale.
///
/// Measured before this ran, with the camera at z = -33.5 looking down +Z, so nearer the viewer means
/// smaller z:
///
///     reel   z = -12.87, y = +10.41   -> 21 units from the lens: the NEAREST thing, and floating
///     tray   z =  29..36, y =  -6.89  -> 62-69 units away: the FURTHEST thing, and under the floor
///
/// That is exactly inverted: the pieces the player taps belong closest, the reel belongs at the back.
/// It read as acceptable only because the camera had been placed to make it read as acceptable.
///
/// Each group is moved as a RIGID BODY - one translation for the whole group - so the arrangement the
/// art depends on (cell grid, tray spacing, the rail's fit against the live row) is preserved exactly.
/// Nothing is rescaled and nothing is rotated. The camera is solved last.
/// </summary>
public static class Case1OrderFix
{
    const string ScenePath = "Assets/Case1_FitTheShape/Scenes/FitTheShape.unity";

    /// <summary>Floor. The tray stands on it.</summary>
    const float GroundY = 0f;

    /// <summary>Depth of the reel's front face, and the gap the tray keeps in front of it.</summary>
    const float ReelFrontZ = 5.20f;
    const float TrayGap = 0.80f;

    /// <summary>How far the rail stands off the reel's front face.</summary>
    const float RailStandoff = 0.15f;

    /// <summary>How high the reel is mounted above the floor, as a share of its own height.</summary>
    const float ReelLiftShare = 0.28f;

    /// <summary>Camera pitch. Physical choice: the player looks down at a table.</summary>
    const float CameraPitchDeg = 34f;

    /// <summary>Share of the frame height the whole scene should fill.</summary>
    const float FillHeight = 0.88f;

    public static void Run()
    {
        Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

        List<Transform> tray = new List<Transform>(16);
        foreach (GameObject go in scene.GetRootGameObjects()) CollectTray(go.transform, tray);
        Transform reel = Find(scene, "Reel") ?? Find(scene, "Drum");
        Transform rail = Find(scene, "Case1_SlotBand");
        Camera cam = Object.FindFirstObjectByType<Camera>(FindObjectsInactive.Include);
        if (tray.Count == 0 || reel == null || cam == null)
        {
            Debug.LogError("[Case1Order] missing pieces: tray=" + tray.Count + " reel=" + (reel != null) +
                           " camera=" + (cam != null));
            return;
        }

        // ---- reel (and its rail) FIRST: it defines where "behind" is ---------------------------
        Bounds reelBounds = SubtreeBounds(reel);
        float wantBottomY = GroundY + reelBounds.size.y * ReelLiftShare;
        Vector3 reelDelta = new Vector3(-reelBounds.center.x,
                                        wantBottomY - reelBounds.min.y,
                                        ReelFrontZ - reelBounds.min.z);
        reel.position += reelDelta;
        EditorUtility.SetDirty(reel);
        // The rail is placed AGAINST the reel's front face, not shifted by the reel's delta. Shifting
        // by the delta preserves whatever gap the two started with - and in this scene they started
        // 21 units apart, so the rail ended up floating alone above an empty patch of floor. Its
        // rotation is left exactly as authored; only its position moves.
        if (rail != null && !rail.IsChildOf(reel))
        {
            Bounds reelNow = SubtreeBounds(reel);
            Bounds railNow = SubtreeBounds(rail);
            Vector3 target = new Vector3(reelNow.center.x, reelNow.center.y, reelNow.min.z - RailStandoff);
            rail.position += target - railNow.center;
            EditorUtility.SetDirty(rail);
        }

        // ---- tray: onto the floor, and wholly IN FRONT of the reel -----------------------------
        //
        // Centring the tray on a chosen z was not enough: its own rows span 15.7 units in this scene,
        // so a centred group still reached z 9.07 and pushed through the reel at 5.20..8.82. The
        // constraint that matters is not where the tray's middle sits, it is that its BACK edge clears
        // the reel's front face.
        // The refill piece is parked off-frame on purpose, but at z = -10.89 it sat eight units ahead
        // of a tray whose rows are 3.3 apart, and it dragged the scene bounds so far that the camera
        // had to retreat to 100 units to hold everything. It is placed one row ahead of the front row
        // instead, and kept OUT of both the tray bounds and the framing bounds - it is not part of the
        // composition, it is a piece waiting its turn.
        List<Transform> grid = tray.FindAll(t => !t.name.StartsWith("TrayRefill_"));
        Bounds trayBounds = GroupBounds(grid.Count > 0 ? grid : tray);
        float trayBackWanted = SubtreeBounds(reel).min.z - TrayGap;
        Vector3 trayDelta = new Vector3(-trayBounds.center.x,
                                        GroundY - trayBounds.min.y,
                                        trayBackWanted - trayBounds.max.z);
        foreach (Transform t in tray) { t.position += trayDelta; EditorUtility.SetDirty(t); }

        // Row pitch read from the grid itself, so the refill sits exactly one row further out.
        float rowPitch = RowPitchOf(grid);
        foreach (Transform t in tray)
        {
            if (!t.name.StartsWith("TrayRefill_")) continue;
            Bounds rb = SubtreeBounds(t);
            Bounds gb = GroupBounds(grid);
            t.position += new Vector3(0f, 0f, (gb.min.z - rowPitch) - rb.center.z);
            EditorUtility.SetDirty(t);
        }

        // ---- camera last -----------------------------------------------------------------------
        Bounds all = GroupBounds(grid);
        all.Encapsulate(SubtreeBounds(reel));
        if (rail != null) all.Encapsulate(SubtreeBounds(rail));
        float pitch = CameraPitchDeg * Mathf.Deg2Rad;
        float halfFov = cam.fieldOfView * 0.5f * Mathf.Deg2Rad;
        float distance = (all.extents.magnitude / Mathf.Tan(halfFov)) * 1.06f;
        cam.transform.rotation = Quaternion.Euler(CameraPitchDeg, 0f, 0f);
        for (int pass = 0; pass < 12; pass++)
        {
            cam.transform.position = all.center + new Vector3(0f,
                                                              distance * Mathf.Sin(pitch),
                                                              -distance * Mathf.Cos(pitch));
            Rect r;
            if (!Shared.EditorTools.ReferenceMatchLayout.ProjectBounds(cam, FramedRenderers(grid, reel, rail), out r)) break;
            if (r.height < 1e-5f) break;
            float f = r.height / FillHeight;
            if (Mathf.Abs(f - 1f) < 0.005f) break;
            distance *= Mathf.Clamp(f, 0.5f, 2f);
        }
        EditorUtility.SetDirty(cam.transform);

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);

        Bounds t2 = GroupBounds(tray), r2 = SubtreeBounds(reel);
        Debug.Log(string.Format("[Case1Order] tray z {0:0.00}..{1:0.00} y {2:0.00} | reel z {3:0.00}..{4:0.00} " +
                                "y {5:0.00} | camera {6} pitch {7:0.0}",
                                t2.min.z, t2.max.z, t2.min.y, r2.min.z, r2.max.z, r2.min.y,
                                cam.transform.position, cam.transform.eulerAngles.x));
    }

    /// <summary>Median gap between tray rows, so one stray piece cannot define the pitch.</summary>
    static float RowPitchOf(List<Transform> grid)
    {
        List<float> zs = new List<float>();
        foreach (Transform t in grid)
        {
            float z = SubtreeBounds(t).center.z;
            bool seen = false;
            foreach (float k in zs) if (Mathf.Abs(k - z) < 0.4f) { seen = true; break; }
            if (!seen) zs.Add(z);
        }
        zs.Sort();
        if (zs.Count < 2) return 3f;
        List<float> gaps = new List<float>();
        for (int i = 1; i < zs.Count; i++) gaps.Add(zs[i] - zs[i - 1]);
        gaps.Sort();
        return gaps[gaps.Count / 2];
    }

    /// <summary>Only what the shot is meant to hold: the tray grid, the reel and its rail.</summary>
    static List<Renderer> FramedRenderers(List<Transform> grid, Transform reel, Transform rail)
    {
        List<Renderer> all = new List<Renderer>(128);
        foreach (Transform t in grid) all.AddRange(t.GetComponentsInChildren<Renderer>(true));
        if (reel != null) all.AddRange(reel.GetComponentsInChildren<Renderer>(true));
        if (rail != null) all.AddRange(rail.GetComponentsInChildren<Renderer>(true));
        all.RemoveAll(r => r == null || r is ParticleSystemRenderer);
        return all;
    }

    static void CollectTray(Transform t, List<Transform> into)
    {
        if (t.name.StartsWith("Shape_") || t.name.StartsWith("TrayShape_") || t.name.StartsWith("TrayRefill_"))
        { into.Add(t); return; }
        for (int i = 0; i < t.childCount; i++) CollectTray(t.GetChild(i), into);
    }

    static Bounds GroupBounds(List<Transform> group)
    {
        Bounds b = new Bounds(); bool any = false;
        foreach (Transform t in group)
        {
            Bounds sb = SubtreeBounds(t);
            if (!any) { b = sb; any = true; } else b.Encapsulate(sb);
        }
        return b;
    }

    static Bounds SubtreeBounds(Transform t)
    {
        Bounds b = new Bounds(t.position, Vector3.zero); bool any = false;
        foreach (Renderer r in t.GetComponentsInChildren<Renderer>(true))
        {
            if (r == null || r is ParticleSystemRenderer) continue;
            if (!any) { b = r.bounds; any = true; } else b.Encapsulate(r.bounds);
        }
        return b;
    }

    static Bounds SceneBounds(Scene scene)
    {
        Bounds b = new Bounds(); bool any = false;
        foreach (Renderer r in Renderers(scene))
        {
            if (!any) { b = r.bounds; any = true; } else b.Encapsulate(r.bounds);
        }
        return b;
    }

    static List<Renderer> Renderers(Scene scene)
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
