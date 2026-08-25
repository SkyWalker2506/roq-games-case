using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

/// <summary>
/// P17 colour grading. Every case scene gets its own global <see cref="Volume"/> whose profile is
/// generated here, from numbers that were MEASURED off the reference videos rather than guessed.
///
/// How the numbers were found: 16 frames were pulled from each reference mp4 and 16 from our own
/// capture strip, and six statistics were computed on both sets - mean luminance, mean HSV saturation,
/// per-frame luminance standard deviation (local contrast), the 5th and 95th luminance percentiles
/// (how deep the blacks and how hot the whites are), and the mean R/B ratio (white balance). A
/// simulation of URP's own grading maths (linear exposure, LMS white balance, LogC-space contrast,
/// luminance-preserving saturation) was then fitted to close the gap. So each grade below is an
/// answer to a measurement, not a taste call, and the "before" numbers are recorded next to it.
///
/// Two deliberate restraints:
///   * Tonemapping is left at <see cref="TonemappingMode.None"/>. The references are flat, stylised
///     mobile captures; a filmic curve would roll the highlights the reference does not roll and would
///     make the fitted exposure/contrast pair unpredictable.
///   * Bloom is thresholded, not global. The measured share of pixels above 0.85 luminance is the
///     budget: Stickerdom's paper gets none at all, because bloom on matte paper reads as plastic.
///
/// Lives in Case 1's Editor folder for the same reason CaseFramingGate does: the case Editor folders
/// share one assembly (Assembly-CSharp-Editor), and this package may only write the files it owns.
/// </summary>
public static class CaseGrade
{
    const string SettingsFolder = "Assets/Settings";
    const string GradeFolderName = "CaseGrades";
    const string GradeFolder = SettingsFolder + "/" + GradeFolderName;

    /// <summary>Name of the scene root that carries the global volume. Rebuilt on every setup run.</summary>
    public const string VolumeRootName = "Case_PostProcess";

    /// <summary>One case's grade. Every field is in URP's own units.</summary>
    public struct Grade
    {
        public float exposure;        // ColorAdjustments.postExposure, EV
        public float contrast;        // ColorAdjustments.contrast, -100..100
        public float saturation;      // ColorAdjustments.saturation, -100..100
        public float temperature;     // WhiteBalance.temperature, -100..100 (negative = cooler)
        public float tint;            // WhiteBalance.tint
        public float bloomIntensity;  // 0 = no Bloom override at all
        public float bloomThreshold;
        public float bloomScatter;
        public float vignette;        // 0 = no Vignette override at all
        public float vignetteSmoothness;
        public string note;
    }

    // ------------------------------------------------------------------ measured grades
    //
    //  case          statistic     reference   ours (ungraded)
    //  ------------  ------------  ----------  ---------------
    //  FitTheShape   luminance        0.468        0.545        too bright
    //                saturation       0.523        0.405        too pale
    //                local contrast   0.173        0.135        too flat
    //                p5 luminance     0.111        0.349        no blacks at all
    //                R/B              0.737        0.778        too warm for a purple room
    //  BlockHole     luminance        0.333        0.423        much too bright
    //                saturation       0.559        0.326        far too pale - this is the big one
    //                local contrast   0.164        0.106
    //                R/B              0.602        0.787        the navy board is missing its blue
    //  Stickerdom    luminance        0.622        0.615        already right
    //                saturation       0.381        0.395        already right
    //                local contrast   0.201        0.256        OURS IS TOO CONTRASTY
    //                p5 luminance     0.307        0.135        our shadows are far too deep for paper
    //                R/B              1.533        1.356        not warm enough
    //  Buca          luminance        0.424        0.542
    //                saturation       0.347        0.224
    //                local contrast   0.157        0.084        nearly twice as flat as the reference
    //                p5 luminance     0.136        0.475        no dark values anywhere in the frame

    public static readonly Grade FitTheShape = new Grade
    {
        exposure = 0.12f, contrast = 8f, saturation = 16f, temperature = 0f, tint = 0f,
        bloomIntensity = 1.35f, bloomThreshold = 0.85f, bloomScatter = 0.72f,
        vignette = 0f,
        note = "vibrant saturated toy palette with radiant star bloom"
    };

    public static readonly Grade BlockHole = new Grade
    {
        // Round 2 measured the WHOLE frame and was misled by it: our frame surrounds the board with a
        // wide clear-colour margin the reference does not have (the reference fills it with HUD), so a
        // whole-frame average said "on target" while the board itself was 47% too bright. Measured on
        // the board region alone (x 0.10..0.90, y 0.20..0.75) round 2 read luminance 0.418 against the
        // reference's 0.284, local contrast 0.160 against 0.125, p5 0.046 against 0.129 and R/B 0.572
        // against 0.632: too bright, too contrasty, blacks crushed, and too blue. Round 3 backs all
        // four off. The clear colour is set in Case2SceneSetup to the value that lands on the
        // reference's own navy through this grade.
        // P20 round A: contrast 26 / saturation 32 was doing the whole job and was crushing the navy
        // board to near black while turning the pink block into raw magenta. Stood down to identity-ish.
        exposure = 0f, contrast = 5f, saturation = 5f, temperature = 0f, tint = 0f,
        bloomIntensity = 0.08f, bloomThreshold = 1.25f, bloomScatter = 0.44f,
        vignette = 0f,
        note = "navy board and toy blocks carried by materials, not by a contrast push"
    };

    public static readonly Grade Stickerdom = new Grade
    {
        // round 1: local contrast 0.200 against the reference's 0.200 and R/B 1.541 against 1.537 -
        // both exact. The paper is still a shade dark in the shadows (p5 0.223 against 0.306) and short
        // of bright paper (0.082 of the frame above 0.85 luminance against 0.209), so round 2 lifts.
        // P20 round A: Stickerdom was the closest case already; the +30 white balance is kept small
        // because the paper's warmth belongs to the page texture, not to a global tint.
        // P20 round B: Stickerdom's colour lives in page/wood/slot TEXTURES, so there is no material to
        // correct - the honest lever here is the grade. Measured after round A: wood #B3815C against the
        // reference's #D99D62, page #E0B481 against #F9CD90, slots #B79FC3 against #D09BC2. All three ask
        // for the same thing, about +15% brightness with a small warm push, so that is what is applied.
        // P21: exposure 0.32 over-shot. The colour gate measured the page at #FEFED5 against the
        // reference's #F9CD90 - it had been brightened past the warm tan into near-white. v4 proposed
        // a lower exposure with a small contrast pull; tested here because the gate was failing.
        exposure = 0.14f, contrast = -4f, saturation = 3f, temperature = 8f, tint = 1f,
        bloomIntensity = 0f,                       // matte paper: any bloom turns it into plastic
        vignette = 0f,
        note = "warm matte paper, near-identity grade, no bloom"
    };

    public static readonly Grade Buca = new Grade
    {
        // round 1: luminance 0.429 against 0.434, saturation 0.358 against 0.336, local contrast 0.187
        // against 0.168 - the flat look is gone. Two overshoots to pay back: 0.111 of the frame above
        // 0.85 luminance against the reference's 0.036 (the bloom is blowing the cyan out) and R/B
        // 0.635 against 0.753 (the contrast push turned the arena colder than the reference).
        // P20 round A: contrast 28 was the arena's whole look; it also drove the floor blue and the
        // green stack dark. Stood down; the floor and stack colours are set on their own materials.
        exposure = 0f, contrast = 6f, saturation = 4f, temperature = 0f, tint = 0f,
        bloomIntensity = 0.08f, bloomThreshold = 1.30f, bloomScatter = 0.46f,
        vignette = 0f,
        note = "neon rim bloom over a high threshold; floor and stack colours come from materials"
    };

    // ------------------------------------------------------------------ application

    /// <summary>
    /// Rebuilds <paramref name="profileName"/>'s volume profile from <paramref name="g"/>, hangs a
    /// global Volume on the scene that uses it, and switches post-processing on for the camera.
    ///
    /// Idempotent by demolition: both the profile asset and the scene object are destroyed and made
    /// again, so a value that is not restated in this file cannot survive in the scene (lesson #4).
    /// </summary>
    public static void Apply(Scene scene, Camera cam, string profileName, Grade g)
    {
        VolumeProfile profile = BuildProfile(profileName, g);
        if (profile == null) return;

        Transform stale = FindRoot(scene, VolumeRootName);
        if (stale != null) Object.DestroyImmediate(stale.gameObject);

        GameObject go = new GameObject(VolumeRootName);
        SceneManager.MoveGameObjectToScene(go, scene);
        go.layer = 0;                       // the Default layer, which every camera's volume mask includes

        Volume volume = go.AddComponent<Volume>();
        volume.isGlobal = true;
        volume.priority = 10f;              // above URP's project-wide default profile
        volume.weight = 1f;
        volume.sharedProfile = profile;
        EditorUtility.SetDirty(volume);
        EditorUtility.SetDirty(go);

        if (cam != null)
        {
            UniversalAdditionalCameraData data = cam.GetUniversalAdditionalCameraData();
            if (data != null)
            {
                data.renderPostProcessing = true;
                data.volumeLayerMask = ~0;  // the volume object sits on Default, but be explicit
                // Clean mobile geometry benefits from a restrained post AA pass.  Keep this on the
                // camera instead of trying to blur jaggies away with bloom or soft materials.
                data.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
                data.antialiasingQuality = AntialiasingQuality.High;
                data.dithering = true;
                EditorUtility.SetDirty(data);
            }
            cam.allowHDR = true;            // bloom over a threshold of 1.0 needs values above 1.0
            EditorUtility.SetDirty(cam);
        }

        Debug.Log(string.Format(
            "[CaseGrade] {0}: exposure={1:+0.00;-0.00} contrast={2:+0;-0} saturation={3:+0;-0} " +
            "whiteBalance=({4:+0;-0},{5:+0;-0}) bloom={6} vignette={7} postProcessing={8}  // {9}",
            profileName, g.exposure, g.contrast, g.saturation, g.temperature, g.tint,
            g.bloomIntensity > 0f ? g.bloomIntensity.ToString("0.00") + "@" + g.bloomThreshold.ToString("0.00") : "off",
            g.vignette > 0f ? g.vignette.ToString("0.00") : "off",
            cam != null ? "on" : "NO_CAMERA", g.note));
    }

    static VolumeProfile BuildProfile(string profileName, Grade g)
    {
        if (!AssetDatabase.IsValidFolder(GradeFolder))
            AssetDatabase.CreateFolder(SettingsFolder, GradeFolderName);

        string path = GradeFolder + "/" + profileName + ".asset";
        if (AssetDatabase.LoadAssetAtPath<VolumeProfile>(path) != null) AssetDatabase.DeleteAsset(path);

        VolumeProfile profile = ScriptableObject.CreateInstance<VolumeProfile>();
        AssetDatabase.CreateAsset(profile, path);

        // Stated even though it is the "off" value: URP's project-wide default profile is layered under
        // this one, and a curve nobody restates is a curve nobody controls.
        Tonemapping tone = AddOverride<Tonemapping>(profile);
        tone.mode.Override(TonemappingMode.None);

        ColorAdjustments colour = AddOverride<ColorAdjustments>(profile);
        colour.postExposure.Override(g.exposure);
        colour.contrast.Override(g.contrast);
        colour.saturation.Override(g.saturation);

        if (!Mathf.Approximately(g.temperature, 0f) || !Mathf.Approximately(g.tint, 0f))
        {
            WhiteBalance wb = AddOverride<WhiteBalance>(profile);
            wb.temperature.Override(g.temperature);
            wb.tint.Override(g.tint);
        }

        if (g.bloomIntensity > 0f)
        {
            Bloom bloom = AddOverride<Bloom>(profile);
            bloom.intensity.Override(g.bloomIntensity);
            bloom.threshold.Override(g.bloomThreshold);
            bloom.scatter.Override(g.bloomScatter);
            bloom.highQualityFiltering.Override(false);
        }

        if (g.vignette > 0f)
        {
            Vignette vignette = AddOverride<Vignette>(profile);
            vignette.intensity.Override(g.vignette);
            vignette.smoothness.Override(g.vignetteSmoothness);
            vignette.rounded.Override(false);
        }

        EditorUtility.SetDirty(profile);
        AssetDatabase.SaveAssets();
        AssetDatabase.ImportAsset(path);
        return AssetDatabase.LoadAssetAtPath<VolumeProfile>(path);
    }

    static T AddOverride<T>(VolumeProfile profile) where T : VolumeComponent
    {
        T component = profile.Add<T>(true);
        component.hideFlags = HideFlags.HideInHierarchy;
        AssetDatabase.AddObjectToAsset(component, profile);
        return component;
    }

    static Transform FindRoot(Scene scene, string name)
    {
        GameObject[] roots = scene.GetRootGameObjects();
        for (int i = 0; i < roots.Length; i++) if (roots[i].name == name) return roots[i].transform;
        return null;
    }

    // ------------------------------------------------------------------ batch entry points

    /// <summary>
    /// Rebuilds all four case scenes and then captures every sequence in the project, in one editor
    /// session. Tuning the grade means comparing sixteen frames per case against sixteen reference
    /// frames, and doing that one -executeMethod at a time costs four editor start-ups per iteration.
    /// </summary>
    public static void BuildAllAndCaptureAll()
    {
        Case1SceneSetup.Build();
        Case2SceneSetup.Build();
        Case3SceneSetup.Build();
        Case3FramingProbe.Report();
        Case4SceneSetup.Build();
        FrameStripCapture.CaptureAll();
    }
}

/// <summary>
/// Reports how wide and how tall Case 3's page sits in the reference 0.625 frame. Case 3 was the one
/// case P16 never measured, so there is no reference number to gate against until this has run once
/// against the real scene; it prints, it does not judge.
/// </summary>
public static class Case3FramingProbe
{
    const string ScenePath = "Assets/Case3_Stickerdom/Scenes/Stickerdom.unity";

    /// <summary>Zero-argument entry point for -executeMethod.</summary>
    public static void Run()
    {
        EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        Report();
    }

    /// <summary>Measures the currently open Case 3 scene. Assumes it is already open.</summary>
    public static void Report()
    {
        Scene scene = EditorSceneManager.GetActiveScene();
        Camera cam = Object.FindFirstObjectByType<Camera>(FindObjectsInactive.Include);
        if (cam == null) { Log("NO_CAMERA"); return; }

        Log("camera ortho=" + cam.orthographic + " size=" + cam.orthographicSize.ToString("0.000") +
            " pos=" + cam.transform.position.ToString("0.###") +
            " aspectEnforcer=" + (cam.GetComponent<Shared.View.AspectRatioEnforcer>() != null ? "yes" : "NO"));

        Measure(scene, cam, "Page", "PageFrame");
        Measure(scene, cam, "Page", "PageSheet");
        Measure(scene, cam, "Page", "");
    }

    static void Measure(Scene scene, Camera cam, string rootName, string exact)
    {
        Transform root = null;
        GameObject[] roots = scene.GetRootGameObjects();
        for (int i = 0; i < roots.Length; i++) if (roots[i].name == rootName) { root = roots[i].transform; break; }
        if (root == null) { Log("no root named '" + rootName + "'; roots: " + Names(roots)); return; }

        Renderer[] all = root.GetComponentsInChildren<Renderer>(true);
        List<string> seen = new List<string>();
        bool any = false;
        Bounds b = new Bounds();
        for (int i = 0; i < all.Length; i++)
        {
            if (all[i] is ParticleSystemRenderer) continue;
            if (seen.Count < 24) seen.Add(all[i].name);
            if (!string.IsNullOrEmpty(exact) && all[i].name != exact) continue;
            if (!any) { b = all[i].bounds; any = true; } else b.Encapsulate(all[i].bounds);
        }
        if (!any) { Log("no renderer '" + exact + "' under '" + rootName + "'; present: " + string.Join(" ", seen.ToArray())); return; }

        float previous = cam.aspect;
        cam.aspect = Shared.View.AspectRatioEnforcer.TargetAspect;
        float minX = float.MaxValue, maxX = float.MinValue, minY = float.MaxValue, maxY = float.MinValue;
        for (int i = 0; i < 8; i++)
        {
            Vector3 corner = new Vector3(
                (i & 1) == 0 ? b.min.x : b.max.x,
                (i & 2) == 0 ? b.min.y : b.max.y,
                (i & 4) == 0 ? b.min.z : b.max.z);
            Vector3 v = cam.WorldToViewportPoint(corner);
            minX = Mathf.Min(minX, v.x); maxX = Mathf.Max(maxX, v.x);
            minY = Mathf.Min(minY, v.y); maxY = Mathf.Max(maxY, v.y);
        }
        cam.aspect = previous;
        cam.ResetAspect();

        Log(string.Format("  {0}/{1,-12} width={2:0.000} height={3:0.000} centreY={4:0.000} bounds={5}",
            rootName, string.IsNullOrEmpty(exact) ? "<all>" : exact,
            maxX - minX, maxY - minY, (minY + maxY) * 0.5f, b.size.ToString("0.###")));
    }

    static string Names(GameObject[] roots)
    {
        System.Text.StringBuilder sb = new System.Text.StringBuilder();
        for (int i = 0; i < roots.Length; i++) sb.Append(roots[i].name).Append(' ');
        return sb.ToString();
    }

    static void Log(string message)
    {
        Debug.Log("[Case3Framing] " + message);
        System.Console.WriteLine("[Case3Framing] " + message);
    }
}
