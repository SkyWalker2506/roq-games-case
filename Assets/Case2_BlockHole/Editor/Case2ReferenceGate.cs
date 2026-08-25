using System;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Case2;

/// <summary>Read-only composition gate for the authored 1080x1728 Case 2 scene.</summary>
public static class Case2ReferenceGate
{
    const string ScenePath = "Assets/Case2_BlockHole/Scenes/BlockHole.unity";

    struct Target
    {
        public string Key;
        public Vector2 BlockCentre;
        public float BlockWidth;
        public Vector2 HoleCentre;
        public float HoleWidth;

        public Target(string key, Vector2 blockCentre, float blockWidth, Vector2 holeCentre, float holeWidth)
        {
            Key = key;
            BlockCentre = blockCentre;
            BlockWidth = blockWidth;
            HoleCentre = holeCentre;
            HoleWidth = holeWidth;
        }
    }

    static readonly Target[] Targets =
    {
        // Hole L centre moved .667 -> .721 with the opening. The pivot went from x = 5.0 to 5.5
        // when the opening picked up its fourth cell, and at the authored orthographicSize 7.45
        // with aspect 0.625 the viewport is 9.3125 world units wide, so half a cell is exactly
        // .0537 of it. The width stays .224 on purpose: useMouthPivot projects the silhouette
        // MESH, which is the BLOCK's footprint and is unchanged - it is not the opening, and it is
        // not the opening for the green P either (.222 here against a 3-cell mouth).
        new Target("L",      new Vector2(.280f, .713f), .331f, new Vector2(.721f, .297f), .224f),
        new Target("Square", new Vector2(.222f, .579f), .222f, new Vector2(.719f, .702f), .222f),
        new Target("2",      new Vector2(.831f, .345f), .115f, new Vector2(.831f, .515f), .115f),
        new Target("Cross",  new Vector2(.556f, .451f), .339f, new Vector2(.284f, .339f), .369f),
    };

    public static void Run()
    {
        Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        Transform board = FindRoot(scene, "Board");
        Transform blocks = board != null ? board.Find("Blocks") : null;
        Transform holes = board != null ? board.Find("Holes") : null;
        Camera cam = UnityEngine.Object.FindFirstObjectByType<Camera>(FindObjectsInactive.Include);
        if (blocks == null || holes == null || cam == null) throw new Exception("CASE2_REFERENCE_GATE missing board/camera");

        int failures = 0;
        float previousAspect = cam.aspect;
        cam.aspect = 0.625f;
        try
        {
            Check(cam.orthographic && Mathf.Abs(cam.orthographicSize - 7.45f) < .001f,
                  "camera orthographic size is authored 7.45", ref failures);
            Check(blocks.childCount == 4 && holes.childCount == 4,
                  "scene contains exactly four block/hole roots", ref failures);

            for (int i = 0; i < Targets.Length; i++)
            {
                Target target = Targets[i];
                Transform block = blocks.Find("Block_Block-" + target.Key);
                Transform hole = holes.Find("Hole_Hole-Block-" + target.Key);
                CheckVisual(cam, block, "block " + target.Key, target.BlockCentre, target.BlockWidth, false, ref failures);
                CheckVisual(cam, hole, "hole " + target.Key, target.HoleCentre, target.HoleWidth, true, ref failures);
            }
        }
        finally
        {
            cam.aspect = previousAspect;
        }

        if (failures > 0) throw new Exception("CASE2_REFERENCE_GATE_FAILED failures=" + failures);
        Debug.Log("[Case2ReferenceGate] CASE2_REFERENCE_GATE_OK pairs=4 aspect=0.625");
    }

    static void CheckVisual(Camera cam, Transform root, string label, Vector2 expectedCentre,
                            float expectedWidth, bool useMouthPivot, ref int failures)
    {
        if (root == null)
        {
            Check(false, label + " exists", ref failures);
            return;
        }

        Rect rect = useMouthPivot ? ProjectRuntimeMouth(cam, root) : Project(cam, VisibleBounds(root));
        Vector2 centre = rect.center;
        bool centreOk = Vector2.Distance(centre, expectedCentre) <= .018f;
        bool widthOk = Mathf.Abs(rect.width - expectedWidth) <= .025f;
        Check(centreOk && widthOk,
              string.Format("{0} centre=({1:0.000},{2:0.000}) width={3:0.000}",
                  label, centre.x, centre.y, rect.width), ref failures);
    }

    static Rect ProjectRuntimeMouth(Camera cam, Transform root)
    {
        HoleGlowHighlight glow = root.GetComponent<HoleGlowHighlight>();
        if (glow == null || glow.silhouetteMesh == null) return Project(cam, VisibleBounds(root));

        Matrix4x4 plate = root.localToWorldMatrix * Matrix4x4.TRS(
            new Vector3(0f, .014f, 0f), Quaternion.identity, new Vector3(1.015f, .006f, 1.015f));
        return Project(cam, glow.silhouetteMesh.bounds, plate);
    }

    static Bounds VisibleBounds(Transform root)
    {
        Renderer[] renderers = root.GetComponentsInChildren<Renderer>(false);
        Bounds result = new Bounds(root.position, Vector3.zero);
        bool any = false;
        for (int i = 0; i < renderers.Length; i++)
        {
            Renderer renderer = renderers[i];
            if (renderer == null || !renderer.enabled || renderer is ParticleSystemRenderer) continue;
            string n = renderer.name.ToLowerInvariant();
            if (n.Contains("activefx") || n.Contains("vacum") || n.Contains("particle")) continue;
            if (!any) { result = renderer.bounds; any = true; }
            else result.Encapsulate(renderer.bounds);
        }
        return result;
    }

    static Rect Project(Camera cam, Bounds bounds)
    {
        return Project(cam, bounds, Matrix4x4.identity);
    }

    static Rect Project(Camera cam, Bounds bounds, Matrix4x4 transform)
    {
        Vector3 min = bounds.min, max = bounds.max;
        Vector3[] corners =
        {
            new Vector3(min.x,min.y,min.z), new Vector3(max.x,min.y,min.z),
            new Vector3(min.x,max.y,min.z), new Vector3(max.x,max.y,min.z),
            new Vector3(min.x,min.y,max.z), new Vector3(max.x,min.y,max.z),
            new Vector3(min.x,max.y,max.z), new Vector3(max.x,max.y,max.z)
        };
        float x0 = float.MaxValue, y0 = float.MaxValue, x1 = float.MinValue, y1 = float.MinValue;
        for (int i = 0; i < corners.Length; i++)
        {
            Vector3 p = cam.WorldToViewportPoint(transform.MultiplyPoint3x4(corners[i]));
            x0 = Mathf.Min(x0, p.x); y0 = Mathf.Min(y0, p.y);
            x1 = Mathf.Max(x1, p.x); y1 = Mathf.Max(y1, p.y);
        }
        return Rect.MinMaxRect(x0, y0, x1, y1);
    }

    static Transform FindRoot(Scene scene, string name)
    {
        GameObject[] roots = scene.GetRootGameObjects();
        for (int i = 0; i < roots.Length; i++) if (roots[i].name == name) return roots[i].transform;
        return null;
    }

    static void Check(bool ok, string detail, ref int failures)
    {
        Debug.Log("[Case2ReferenceGate] " + (ok ? "PASS " : "FAIL ") + detail);
        if (!ok) failures++;
    }
}
