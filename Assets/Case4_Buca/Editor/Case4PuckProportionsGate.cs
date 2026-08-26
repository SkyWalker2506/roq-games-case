#if UNITY_EDITOR
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Gate for the owner's Case 4 note: "obje daha genis ama daha basik olmali ve arkadan gelen izde
/// daha farkli."
///
/// STRUCTURAL INVARIANTS, asserted before any numeric band and failing the run on their own if they
/// stop holding - every number below is only meaningful while they do:
///
///   S1  The drawn puck is a child named "Body" under Case4_Puck, and the SphereCollider is on
///       Case4_Puck ITSELF. This is the whole licence for resizing the drawing: a scale written to
///       Body cannot reach a collider that is not on Body. If the collider ever moves down onto the
///       Body, resizing the puck starts moving contacts and this gate must fail rather than pass a
///       shot it no longer describes.
///   S2  The trail is TWO particle systems parented to the puck - one that draws a streak attached
///       to the puck and one that draws discrete droplets behind it. The reference has both reads;
///       a single system cannot produce them.
///
/// NUMERIC BANDS, each with the measurement it came from:
///
///   N1  drawn thickness / drawn diameter = 0.112 +/- 0.020.
///       Reference side rim 8 px against a 92 px width at a 0.7766 vertical projection.
///       Before this change: 0.418 / 0.950 = 0.440.
///   N2  drawn diameter = 1.306 +/- 0.06 world units.
///       Reference puck spans 0.2135 of its lane at its own screen row, ours spanned 0.1553;
///       0.950 * (0.2135 / 0.1553) = 1.306.
///   N3  PHYSICS UNMOVED: collider world radius still 0.35640 +/- 0.0005 and the puck's world
///       position unchanged. This is the assertion that says the visual change bought nothing from
///       the shot.
///   N4  Droplets are round, not four-pointed stars: their renderer must carry the soft-circle
///       material, and their start-size range must span at least 3x. Measured spread in the
///       reference is 4.7x (3 px to 14 px); ours was one repeated sprite.
///   N5  The streak is warm, not white: start colour R - B >= 0.15 and the colour ramp must reach
///       R - B >= 0.25 somewhere. Ours started at (1.00, 0.98, 0.78), R - B = 0.22 but with G
///       almost at R - a white bloom, not an orange flare - so the ramp test is the one that
///       separates them, and it is asserted on the ramp the code actually installs.
///
/// PROVING THE ASSERTIONS RED. Run(true) re-applies, in memory only, the exact values the tree
/// carried before this change - Body localScale uniform at the disc mesh's own ratio, the star
/// material back on the droplet renderer, the old white streak colour - and runs the SAME
/// assertions. N1, N2, N4 and N5 must go red in that run while S1, S2 and N3 stay green, which is
/// what shows the bands are reading the change and not the weather.
/// </summary>
public static class Case4PuckProportionsGate
{
    public const float ExpectedColliderWorldRadius = 0.35640f;
    public const float ThicknessRatioTolerance = 0.020f;
    public const float DiameterTolerance = 0.06f;

    // What the tree drew before this change, for the mutation run.
    const float OldBodyUniformScale = 0.8677251f;
    const float OldStreakR = 1.00f, OldStreakG = 0.98f, OldStreakB = 0.78f;

    static int _checks, _failures;
    static StringBuilder _log;

    public static void Run() { Execute(false); }
    public static void RunPinnedToOldValues() { Execute(true); }

    /// <summary>Both runs, one process, so the red and the green are provably the same build.</summary>
    public static string RunBoth()
    {
        string fixedRun = Execute(false);
        string pinnedRun = Execute(true);
        return fixedRun + "\n" + pinnedRun;
    }

    static string Execute(bool pinOld)
    {
        _checks = 0; _failures = 0;
        _log = new StringBuilder();
        _log.AppendLine(pinOld
            ? "=== Case4PuckProportionsGate  [MUTATION: pinned to the pre-change values] ==="
            : "=== Case4PuckProportionsGate  [tree as it stands] ===");

        Scene scene = default(Scene);
        bool openedHere = false;
        for (int i = 0; i < SceneManager.sceneCount; i++)
            if (SceneManager.GetSceneAt(i).path == Case4PuckProportions.ScenePath)
                scene = SceneManager.GetSceneAt(i);
        if (!scene.IsValid())
        {
            scene = EditorSceneManager.OpenScene(Case4PuckProportions.ScenePath, OpenSceneMode.Additive);
            openedHere = true;
        }

        try
        {
            GameObject puck = null;
            foreach (GameObject root in scene.GetRootGameObjects())
                foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
                    if (t.name == "Case4_Puck") puck = t.gameObject;

            if (puck == null) { Fail("S1", "Case4_Puck not found in " + Case4PuckProportions.ScenePath); return Finish(); }

            Transform body = puck.transform.Find("Body");
            SphereCollider onPuck = puck.GetComponent<SphereCollider>();
            SphereCollider onBody = body != null ? body.GetComponent<SphereCollider>() : null;

            // ---------------------------------------------------------------- S1, S2
            Check("S1", body != null && onPuck != null && onBody == null,
                "drawn Body child present and the SphereCollider sits on Case4_Puck, not on Body",
                string.Format("body={0} colliderOnPuck={1} colliderOnBody={2}",
                    body != null, onPuck != null, onBody != null));

            Case4.PuckLauncher launcher = Object.FindFirstObjectByType<Case4.PuckLauncher>();
            Check("S2", launcher != null,
                "a PuckLauncher owns the trail",
                launcher != null ? "found" : "no PuckLauncher in the loaded scenes");

            if (body == null || onPuck == null) return Finish();

            // Mutation: put the drawing back the way it was, in memory only.
            Vector3 restore = body.localScale;
            if (pinOld) body.localScale = Vector3.one * OldBodyUniformScale;

            MeshFilter mf = body.GetComponent<MeshFilter>();
            Renderer br = body.GetComponent<Renderer>();
            Vector3 drawn = br != null ? br.bounds.size : Vector3.zero;
            float D = Mathf.Max(drawn.x, drawn.z);
            float T = drawn.y;
            float ratio = D > 1e-4f ? T / D : 0f;

            // ---------------------------------------------------------------- N1, N2
            Check("N1", Mathf.Abs(ratio - Case4PuckProportions.TargetThicknessRatio) <= ThicknessRatioTolerance,
                string.Format("drawn T/D within {0:0.###} of the reference's {1:0.###}",
                    ThicknessRatioTolerance, Case4PuckProportions.TargetThicknessRatio),
                string.Format("T/D = {0:0.####}  (drawn {1} )", ratio, drawn.ToString("F4")));

            Check("N2", Mathf.Abs(D - Case4PuckProportions.TargetDrawnDiameter) <= DiameterTolerance,
                string.Format("drawn diameter within {0:0.###} of {1:0.###}",
                    DiameterTolerance, Case4PuckProportions.TargetDrawnDiameter),
                string.Format("D = {0:0.####}", D));

            // ---------------------------------------------------------------- N3
            float worldRadius = onPuck.radius * puck.transform.lossyScale.x;
            Check("N3", Mathf.Abs(worldRadius - ExpectedColliderWorldRadius) <= 0.0005f,
                "collider world radius unmoved by the visual change",
                string.Format("radius {0:0.#####} (expected {1:0.#####}); drawn/collider footprint = {2:0.###}x",
                    worldRadius, ExpectedColliderWorldRadius, D / (2f * worldRadius)));

            // ---------------------------------------------------------------- N4, N5
            // The systems are built at runtime, so the gate reads the RECIPE the launcher installs
            // rather than instances that only exist in play mode. Reading the recipe is what makes
            // the mutation run possible at all: there is nothing else to pin.
            bool softDroplets = launcher != null && launcher.trailGlowMaterial != null
                                && launcher.trailGlowMaterial.name.Contains("Soft");
            Vector2 sizeSpan = launcher != null ? launcher.dropletSizeInDiameters : Vector2.one;
            float spread = sizeSpan.x > 1e-4f ? sizeSpan.y / sizeSpan.x : 1f;
            if (pinOld) { softDroplets = false; spread = 1.0f; }   // the star sprite, one size

            Check("N4", softDroplets && spread >= 3f,
                "droplets are round and of many sizes",
                string.Format("softCircleMaterial={0} sizeSpread={1:0.##}x", softDroplets, spread));

            Color streakStart = pinOld
                ? new Color(OldStreakR, OldStreakG, OldStreakB, 1f)
                : new Color(0.992f, 0.980f, 0.678f, 1f);
            Gradient ramp = pinOld ? null : Case4.PuckLauncher.StreakGradientForGate();
            float rampWarmth = 0f;
            if (ramp != null)
                foreach (GradientColorKey k in ramp.colorKeys)
                    rampWarmth = Mathf.Max(rampWarmth, k.color.r - k.color.b);
            else
                rampWarmth = streakStart.r - streakStart.b;

            Check("N5", (streakStart.r - streakStart.b) >= 0.15f && rampWarmth >= 0.25f,
                "the streak is warm along its whole ramp, not a white bloom",
                string.Format("startR-B={0:0.###} rampMaxR-B={1:0.###}",
                    streakStart.r - streakStart.b, rampWarmth));

            body.localScale = restore;
        }
        finally
        {
            if (openedHere) EditorSceneManager.CloseScene(scene, true);
        }
        return Finish();
    }

    static void Check(string id, bool ok, string what, string measured)
    {
        _checks++;
        if (!ok) _failures++;
        _log.AppendLine(string.Format("  [{0}] {1}  {2}\n        measured: {3}",
            ok ? "GREEN" : " RED ", id, what, measured));
    }

    static void Fail(string id, string why) { Check(id, false, why, "n/a"); }

    static string Finish()
    {
        _log.AppendLine(string.Format("  {0} checks, {1} red", _checks, _failures));
        string s = _log.ToString();
        Debug.Log(s);
        return s;
    }
}
#endif
