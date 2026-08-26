#if UNITY_EDITOR
/// <summary>
/// Owner, with the reference on the left and ours on the right: "case 4 de obje daha genis ama daha
/// basik olmali" - the puck must be WIDER and FLATTER.
///
/// STRUCTURAL INVARIANT this rests on: the puck's drawn body is a child ("Body") of Case4_Puck, and
/// the SphereCollider is on Case4_Puck ITSELF, not on Body. Scaling Body therefore cannot move a
/// contact point, a bounce, or the solved shot - which is why the whole change lives here and not in
/// the scene setup that owns the physics. Case4PuckProportionsGate asserts the collider is on the
/// parent and refuses to run if it ever moves onto Body.
///
/// WHAT WAS MEASURED (1080x1728, both sides, .plan-build/cli/case4-puck/measure2.py, whose positive
/// control recovers known ratios to within 1.4% for a gold disc and for a black one):
///
///   reference, Buca.mp4 at rest      92 x 68 px   W:H = 1.353
///   ours, verify/Buca/frame_00.png   66 x 62 px   W:H = 1.065
///
/// Scale-invariantly, against the arena the layout gate already matches: at the puck's own screen
/// row the right lane is 431 px wide in the reference and 425 px in ours, so the puck spans 0.2135
/// of its lane there and 0.1553 of its lane here. The reference puck is 1.375x wider - that number
/// is the same 1.394 the raw pixel widths give, so the width gap is real and not a camera artefact.
///
/// WHY THE TARGET IS NOT SIMPLY "W:H = 1.353". The two cameras do not sit at the same pitch. Read
/// off the images, the reference's top-face ellipse is 58 px tall over 92 wide (foreshortening
/// 0.630); ours projects at 0.718, confirmed analytically from Buca.unity's own camera by
/// .plan-build/cli/case4-puck/project.py, whose positive control lands the puck's centre within
/// 0.7 px of where it was measured. Under OUR camera a disc 92 px wide is already 66 px tall before
/// it has any thickness at all, so forcing W:H = 1.353 on screen leaves 2 px of rim and turns the
/// puck into a decal - thinner than the object the reference is actually showing.
///
/// So the target is the reference OBJECT's proportion, not its screen number. Its side rim measures
/// 8 px against a 92 px width at a 0.7766 vertical projection, giving thickness:diameter = 0.112.
/// Ours is 0.418 / 0.950 = 0.440. Rendered by our camera that predicts 92 x 73 px, W:H = 1.26 -
/// which does not reach 1.353 and is not meant to; the residual is the camera pitch difference and
/// is recorded here rather than fitted away.
/// </summary>
public static class Case4PuckProportions
{
    public const string ScenePath = "Assets/Case4_Buca/Scenes/Buca.unity";

    /// <summary>Drawn world diameter. 0.950 today; 0.950 * 1.375 from the lane-relative reading.</summary>
    public const float TargetDrawnDiameter = 1.306f;

    /// <summary>Thickness:diameter of the reference disc, from its 8 px rim over a 92 px width.</summary>
    public const float TargetThicknessRatio = 0.112f;

    public static float TargetDrawnThickness { get { return TargetDrawnDiameter * TargetThicknessRatio; } }

    /// <summary>
    /// Resizes the puck's drawn body to the measured proportions. Per-axis, deliberately: the
    /// uniform scale Case4PuckFromDisc applied is exactly what carried the disc mesh's own
    /// 0.507 / 1.152 = 0.440 thickness ratio onto the puck and made it read as a short cylinder.
    /// Returns a one-line log of what it did.
    /// </summary>
    public static string ApplyTo(UnityEngine.GameObject puck)
    {
        if (puck == null) return "[PuckProportions] no puck";
        UnityEngine.Transform body = puck.transform.Find("Body");
        if (body == null) return "[PuckProportions] Case4_Puck/Body not found";
        UnityEngine.Renderer r = body.GetComponent<UnityEngine.Renderer>();
        UnityEngine.MeshFilter mf = body.GetComponent<UnityEngine.MeshFilter>();
        if (r == null || mf == null || mf.sharedMesh == null)
            return "[PuckProportions] Body has no mesh/renderer";

        // Work from the MESH's own local bounds and the parent's lossy scale, not from the current
        // renderer bounds: renderer bounds are the axis-aligned world box AFTER whatever scale is
        // already on Body, so reading them and then writing a scale computes a correction on top of
        // a correction. The mesh box is fixed.
        UnityEngine.Vector3 meshSize = mf.sharedMesh.bounds.size;
        UnityEngine.Vector3 parentLossy = puck.transform.lossyScale;
        if (meshSize.x < 1e-4f || meshSize.y < 1e-4f || parentLossy.x < 1e-4f || parentLossy.y < 1e-4f)
            return "[PuckProportions] degenerate mesh or parent scale";

        UnityEngine.Vector3 before = r.bounds.size;

        float sxz = TargetDrawnDiameter / (meshSize.x * parentLossy.x);
        float sy = TargetDrawnThickness / (meshSize.y * parentLossy.y);
        body.localScale = new UnityEngine.Vector3(sxz, sy, sxz);

        UnityEngine.Vector3 after = r.bounds.size;
        return string.Format(
            "[PuckProportions] drawn {0} -> {1} (target D={2:0.###} T={3:0.####}, T/D={4:0.###}); " +
            "Body localScale = {5}; collider untouched on the PARENT",
            before.ToString("F3"), after.ToString("F3"),
            TargetDrawnDiameter, TargetDrawnThickness, TargetThicknessRatio,
            body.localScale.ToString("F5"));
    }

    /// <summary>
    /// Applies the change to the saved scene. Loads Buca ADDITIVELY and closes it again if it was
    /// not already open, so this never evicts whatever scene the Editor is showing. It does NOT
    /// call EditorApplication.Exit - this runs against a live Editor.
    /// </summary>
    public static string ApplyToScene()
    {
        UnityEngine.SceneManagement.Scene scene = default(UnityEngine.SceneManagement.Scene);
        bool opened = false;
        for (int i = 0; i < UnityEngine.SceneManagement.SceneManager.sceneCount; i++)
        {
            UnityEngine.SceneManagement.Scene s = UnityEngine.SceneManagement.SceneManager.GetSceneAt(i);
            if (s.path == ScenePath) { scene = s; break; }
        }
        if (!scene.IsValid())
        {
            scene = UnityEditor.SceneManagement.EditorSceneManager.OpenScene(
                ScenePath, UnityEditor.SceneManagement.OpenSceneMode.Additive);
            opened = true;
        }

        string log;
        try
        {
            UnityEngine.GameObject puck = null;
            foreach (UnityEngine.GameObject root in scene.GetRootGameObjects())
                foreach (UnityEngine.Transform t in root.GetComponentsInChildren<UnityEngine.Transform>(true))
                    if (t.name == "Case4_Puck") puck = t.gameObject;

            log = ApplyTo(puck);

            // Saving a scene from an Editor whose Game view is showing a DIFFERENT scene's aspect
            // writes that aspect's letterbox into this scene's camera: the first run of this
            // function put x=0.198 / width=0.603 on Main Camera's normalizedViewPortRect, which
            // would have shipped a cropped Case 4. Snapshot the rect and put it back, so what gets
            // written is the puck change and nothing else.
            UnityEngine.Rect[] rects = null;
            UnityEngine.Camera[] cams = null;
            {
                var list = new System.Collections.Generic.List<UnityEngine.Camera>();
                foreach (UnityEngine.GameObject root in scene.GetRootGameObjects())
                    list.AddRange(root.GetComponentsInChildren<UnityEngine.Camera>(true));
                cams = list.ToArray();
                rects = new UnityEngine.Rect[cams.Length];
                for (int i = 0; i < cams.Length; i++) rects[i] = cams[i].rect;
            }

            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(scene);
            UnityEditor.SceneManagement.EditorSceneManager.SaveScene(scene);

            bool rectDrifted = false;
            for (int i = 0; i < cams.Length; i++)
                if (cams[i] != null && cams[i].rect != rects[i]) { cams[i].rect = rects[i]; rectDrifted = true; }
            if (rectDrifted)
            {
                UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(scene);
                UnityEditor.SceneManagement.EditorSceneManager.SaveScene(scene);
                log += "  [camera viewport rect restored after the save drifted it]";
            }
        }
        finally
        {
            if (opened) UnityEditor.SceneManagement.EditorSceneManager.CloseScene(scene, true);
        }
        return log;
    }
}
#endif
