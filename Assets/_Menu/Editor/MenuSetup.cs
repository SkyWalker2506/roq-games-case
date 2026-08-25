using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Shared.Sequencing;

/// <summary>
/// Builds the case-picker menu scene, drops the HOME button into every case scene and rewrites Build
/// Settings so <c>MainMenu</c> is build index 0. Idempotent: running it twice leaves the project identical.
///
/// The case scenes are only ever touched by adding (or replacing) one extra root object named
/// <see cref="MenuNavigation.RootName"/> — nothing that was already staged is read, moved or removed.
/// </summary>
[InitializeOnLoad]
public static class MenuSetup
{
    const string MenuSceneDir = "Assets/_Menu/Scenes";
    const string MenuScenePath = MenuSceneDir + "/MainMenu.unity";

    static readonly string[] CaseScenePaths =
    {
        "Assets/Case1_FitTheShape/Scenes/FitTheShape.unity",
        "Assets/Case2_BlockHole/Scenes/BlockHole.unity",
        "Assets/Case3_Stickerdom/Scenes/Stickerdom.unity",
        "Assets/Case4_Buca/Scenes/Buca.unity",
    };

    // ---- navigation test state (survives the play-mode domain reload) ----
    const string KeyNavStage = "MenuSetup.NavStage";
    const string KeyNavCase = "MenuSetup.NavCase";
    const string KeyNavActive = "MenuSetup.NavActive";
    const string KeyNavExit = "MenuSetup.NavExit";
    const double NavStepTimeout = 120.0;

    // ---- interaction gate state (survives the play-mode domain reload) ----
    const string KeyIxActive = "MenuSetup.IxActive";
    const string KeyIxScene = "MenuSetup.IxScene";
    const string KeyIxStage = "MenuSetup.IxStage";
    const string KeyIxDirId = "MenuSetup.IxDirId";

    // ---- menu screenshot state ----
    const string KeyShotActive = "MenuSetup.ShotActive";
    const int ShotWidth = 720;
    const int ShotHeight = 1152;          // 10:16, the framing the CanvasScaler is authored against
    const string ShotDir = ".plan-build/verify/Menu";

    static bool _navHooked;
    static double _navStepStart;
    static bool _ixHooked;
    static double _ixStepStart;

    static MenuSetup()
    {
        // Re-arms after the domain reload that entering / leaving play mode triggers.
        if (SessionState.GetInt(KeyNavActive, 0) == 1) HookNav();
        else if (SessionState.GetInt(KeyIxActive, 0) == 1) HookIx();
        else if (SessionState.GetInt(KeyShotActive, 0) == 1) HookShot();
        else if (SessionState.GetInt(KeyNavExit, -1) >= 0) EditorApplication.update += DriveExit;
    }

    // ================================================================== setup

    public static void RunMenu()
    {
        Run();
    }

    /// <summary>Batchmode entry point. Builds everything and dumps the resulting build settings.</summary>
    public static void Run()
    {
        BuildMenuScene();

        for (int i = 0; i < CaseScenePaths.Length; i++)
        {
            AddHomeButton(CaseScenePaths[i]);
        }

        WriteBuildSettings();
        DumpBuildSettings();

        Debug.Log("[MenuSetup] MENU_SETUP_OK");
    }

    static void BuildMenuScene()
    {
        Directory.CreateDirectory(MenuSceneDir);

        Scene scene;
        if (File.Exists(MenuScenePath))
        {
            scene = EditorSceneManager.OpenScene(MenuScenePath, OpenSceneMode.Single);
        }
        else
        {
            scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
        }

        // Camera: the menu is a screen-space overlay canvas, but a scene with no camera logs warnings and
        // renders nothing behind the UI, so make sure one exists with a flat background.
        Camera cam = Object.FindFirstObjectByType<Camera>(FindObjectsInactive.Include);
        if (cam == null)
        {
            GameObject camGo = new GameObject("Main Camera", typeof(Camera));
            SceneManager.MoveGameObjectToScene(camGo, scene);
            cam = camGo.GetComponent<Camera>();
        }
        cam.gameObject.tag = "MainCamera";
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0.07f, 0.08f, 0.11f, 1f);
        if (cam.GetComponent<AudioListener>() == null) cam.gameObject.AddComponent<AudioListener>();
        EditorUtility.SetDirty(cam.gameObject);

        EnsureNavigationRoot(scene);

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene, MenuScenePath);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log("[MenuSetup] menu scene written " + MenuScenePath);
    }

    static void AddHomeButton(string scenePath)
    {
        if (!File.Exists(scenePath))
        {
            Debug.LogError("[MenuSetup] SETUP_FAILED case scene missing: " + scenePath);
            return;
        }

        Scene scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
        int rootsBefore = scene.GetRootGameObjects().Length;

        EnsureNavigationRoot(scene);

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);

        int rootsAfter = scene.GetRootGameObjects().Length;
        Debug.Log("[MenuSetup] home button in " + scenePath + " roots " + rootsBefore + " -> " + rootsAfter);
    }

    /// <summary>
    /// Destroys any previous navigation root and recreates it, so the component is never carried over with
    /// stale serialised data (lesson #4). The object is the only thing this script adds to a scene.
    /// </summary>
    static void EnsureNavigationRoot(Scene scene)
    {
        GameObject[] roots = scene.GetRootGameObjects();
        for (int i = 0; i < roots.Length; i++)
        {
            if (roots[i].name == MenuNavigation.RootName) Object.DestroyImmediate(roots[i]);
        }

        GameObject go = new GameObject(MenuNavigation.RootName);
        SceneManager.MoveGameObjectToScene(go, scene);
        go.AddComponent<MenuNavigation>();
        EditorUtility.SetDirty(go);
    }

    static void WriteBuildSettings()
    {
        List<EditorBuildSettingsScene> scenes = new List<EditorBuildSettingsScene>(5);
        scenes.Add(new EditorBuildSettingsScene(MenuScenePath, true));   // index 0
        for (int i = 0; i < CaseScenePaths.Length; i++)
        {
            scenes.Add(new EditorBuildSettingsScene(CaseScenePaths[i], true));
        }

        EditorBuildSettings.scenes = scenes.ToArray();
        AssetDatabase.SaveAssets();
    }

    /// <summary>Prints the build-scene list so the gate can read it out of the log.</summary>
    public static void DumpBuildSettings()
    {
        EditorBuildSettingsScene[] scenes = EditorBuildSettings.scenes;
        StringBuilder sb = new StringBuilder();
        sb.AppendLine("[MenuSetup] BUILD_SETTINGS count=" + scenes.Length);
        for (int i = 0; i < scenes.Length; i++)
        {
            sb.AppendLine(string.Format("[MenuSetup] BUILD_SCENE {0} enabled={1} exists={2} path={3}",
                i, scenes[i].enabled ? 1 : 0, File.Exists(scenes[i].path) ? 1 : 0, scenes[i].path));
        }

        bool ok = scenes.Length == 5 &&
                  scenes[0].path == MenuScenePath &&
                  scenes[0].enabled;
        for (int i = 0; i < CaseScenePaths.Length && ok; i++)
        {
            ok = scenes[i + 1].path == CaseScenePaths[i] && scenes[i + 1].enabled;
        }
        for (int i = 0; i < scenes.Length && ok; i++)
        {
            ok = File.Exists(scenes[i].path);
        }

        sb.Append(ok ? "[MenuSetup] BUILD_SETTINGS_OK" : "[MenuSetup] BUILD_SETTINGS_BAD");
        Debug.Log(sb.ToString());
        System.Console.WriteLine(sb.ToString());
    }

    /// <summary>Zero-argument gate helper: dumps build settings and exits with the verdict.</summary>
    public static void VerifyBuildSettings()
    {
        DumpBuildSettings();
        EditorBuildSettingsScene[] scenes = EditorBuildSettings.scenes;
        EditorApplication.Exit(scenes.Length == 5 && scenes[0].path == MenuScenePath ? 0 : 1);
    }


    // ================================================================== interaction gate (P10)

    /// <summary>
    /// Proves the two behavioural claims of P10 in a real play session, one case scene at a time:
    ///   1. nothing auto-plays  — after 3 s of play mode the director is idle and its report has no steps;
    ///   2. REPLAY resets       — clicking the generated REPLAY button reloads the scene (fresh director
    ///                            instance) and the reloaded scene is idle again.
    /// Exits 0 when all four scenes pass, non-zero on the first failure.
    /// </summary>
    public static void InteractionGate()
    {
        SessionState.SetInt(KeyIxActive, 1);
        SessionState.SetInt(KeyIxScene, 0);
        SessionState.SetInt(KeyIxStage, 0);
        SessionState.SetInt(KeyIxDirId, 0);
        SessionState.SetInt(KeyNavExit, -1);
        IxLog("IX_GATE_START scenes=" + CaseScenePaths.Length);
        OpenIxScene();
    }

    static void OpenIxScene()
    {
        int index = SessionState.GetInt(KeyIxScene, 0);
        if (index >= CaseScenePaths.Length)
        {
            IxLog("IX_GATE_OK all " + CaseScenePaths.Length + " case scenes idle on load and reset by REPLAY");
            IxFinish(0);
            return;
        }

        EditorSceneManager.OpenScene(CaseScenePaths[index], OpenSceneMode.Single);
        SessionState.SetInt(KeyIxStage, 0);
        SessionState.SetInt(KeyIxDirId, 0);
        HookIx();
        IxLog("IX_SCENE " + CaseScenePaths[index]);
        EditorApplication.EnterPlaymode();
    }

    static void HookIx()
    {
        if (_ixHooked) return;
        _ixHooked = true;
        _ixStepStart = EditorApplication.timeSinceStartup;
        EditorApplication.update += DriveIx;
    }

    static void DriveIx()
    {
        if (SessionState.GetInt(KeyIxActive, 0) != 1) return;

        int stage = SessionState.GetInt(KeyIxStage, 0);

        if (!EditorApplication.isPlaying)
        {
            // Stage 9 means the scene passed and we asked play mode to end; once it actually has (and the
            // domain reload that comes with it is over), move on to the next scene. Polling the state here
            // instead of listening to playModeStateChanged avoids depending on whether this static class was
            // re-hooked before the event was dispatched.
            if (stage != 9 || EditorApplication.isPlayingOrWillChangePlaymode) return;
            SessionState.SetInt(KeyIxScene, SessionState.GetInt(KeyIxScene, 0) + 1);
            OpenIxScene();
            return;
        }

        if (stage == 9) return;

        int index = SessionState.GetInt(KeyIxScene, 0);
        string scenePath = CaseScenePaths[index];

        if (EditorApplication.timeSinceStartup - _ixStepStart > NavStepTimeout)
        {
            IxFail("IX_TIMEOUT stage=" + stage + " scene=" + scenePath, 4);
            return;
        }

        SequenceDirector director = Object.FindFirstObjectByType<SequenceDirector>(FindObjectsInactive.Include);

        switch (stage)
        {
            case 0: // let the scene run untouched, then prove it never started itself
            {
                if (EditorApplication.timeSinceStartup - _ixStepStart < 3.0) return;
                if (director == null)
                {
                    IxFail("IX_NO_DIRECTOR " + scenePath, 2);
                    return;
                }

                if (director.IsPlaying || director.Report.steps.Count > 0 || director.Report.completed)
                {
                    IxFail("IX_AUTOPLAY_DETECTED scene=" + scenePath +
                           " isPlaying=" + director.IsPlaying +
                           " steps=" + director.Report.steps.Count +
                           " completed=" + director.Report.completed, 5);
                    return;
                }

                IxLog("IX_NOAUTOPLAY_OK scene=" + Path.GetFileNameWithoutExtension(scenePath) +
                      " isPlaying=" + director.IsPlaying +
                      " steps=" + director.Report.steps.Count +
                      " unlocked=" + director.PlayUnlocked +
                      " afterSeconds=3.0");

                ReplayButton replay = Object.FindFirstObjectByType<ReplayButton>(FindObjectsInactive.Include);
                if (replay == null || replay.Button == null)
                {
                    IxFail("IX_NO_REPLAY_BUTTON " + scenePath, 6);
                    return;
                }

                SessionState.SetInt(KeyIxDirId, director.GetInstanceID());
                IxLog("IX_REPLAY_CLICK scene=" + Path.GetFileNameWithoutExtension(scenePath) +
                      " directorId=" + director.GetInstanceID());
                replay.Button.onClick.Invoke();
                SetIxStage(1);
                break;
            }

            case 1: // the click must have reloaded the scene: a brand new director, still idle
            {
                if (director == null) return;
                if (director.GetInstanceID() == SessionState.GetInt(KeyIxDirId, 0)) return;

                if (director.IsPlaying || director.Report.steps.Count > 0)
                {
                    IxFail("IX_REPLAY_AUTOPLAY scene=" + scenePath +
                           " isPlaying=" + director.IsPlaying +
                           " steps=" + director.Report.steps.Count, 7);
                    return;
                }

                IxLog("IX_REPLAY_RELOAD_OK scene=" + Path.GetFileNameWithoutExtension(scenePath) +
                      " newDirectorId=" + director.GetInstanceID() +
                      " isPlaying=" + director.IsPlaying +
                      " steps=" + director.Report.steps.Count);

                SessionState.SetInt(KeyIxStage, 9);
                EditorApplication.isPlaying = false;   // DriveIx picks up the next scene once play mode ended
                break;
            }
        }
    }

    static void SetIxStage(int stage)
    {
        SessionState.SetInt(KeyIxStage, stage);
        _ixStepStart = EditorApplication.timeSinceStartup;
    }

    static void IxLog(string message)
    {
        Debug.Log("[MenuSetup] " + message);
        System.Console.WriteLine("[MenuSetup] " + message);
    }

    static void IxFail(string message, int exitCode)
    {
        Debug.LogError("[MenuSetup] " + message);
        System.Console.WriteLine("[MenuSetup] FAILED " + message);
        IxFinish(exitCode);
    }

    static void IxFinish(int exitCode)
    {
        SessionState.SetInt(KeyIxActive, 0);
        SessionState.SetInt(KeyNavExit, exitCode);
        EditorApplication.update -= DriveIx;
        _ixHooked = false;

        EditorApplication.update += DriveExit;
        if (EditorApplication.isPlaying) EditorApplication.isPlaying = false;
    }

    // ================================================================== navigation test

    /// <summary>
    /// Enters play mode on the menu scene and walks menu -> case -> home for all four cases, clicking the
    /// real generated buttons. Exits 0 on a full round trip, non-zero on the first failure.
    /// </summary>
    public static void NavigationTest()
    {
        EditorSceneManager.OpenScene(MenuScenePath, OpenSceneMode.Single);
        SessionState.SetInt(KeyNavActive, 1);
        SessionState.SetInt(KeyNavStage, 0);
        SessionState.SetInt(KeyNavCase, 0);
        SessionState.SetInt(KeyNavExit, -1);
        HookNav();
        NavLog("NAV_TEST_START");
        EditorApplication.EnterPlaymode();
    }

    static void HookNav()
    {
        if (_navHooked) return;
        _navHooked = true;
        _navStepStart = EditorApplication.timeSinceStartup;
        EditorApplication.update += DriveNav;
    }

    static void DriveNav()
    {
        if (SessionState.GetInt(KeyNavActive, 0) != 1) return;
        if (!EditorApplication.isPlaying) return;

        if (EditorApplication.timeSinceStartup - _navStepStart > NavStepTimeout)
        {
            NavFail("NAV_TIMEOUT stage=" + SessionState.GetInt(KeyNavStage, -1) +
                    " case=" + SessionState.GetInt(KeyNavCase, -1) +
                    " scene=" + SceneManager.GetActiveScene().name, 4);
            return;
        }

        int stage = SessionState.GetInt(KeyNavStage, 0);
        int caseIndex = SessionState.GetInt(KeyNavCase, 0);
        string activeScene = SceneManager.GetActiveScene().name;
        MenuNavigation nav = Object.FindFirstObjectByType<MenuNavigation>(FindObjectsInactive.Include);

        switch (stage)
        {
            case 0: // in the menu, waiting for the picker to be built
                if (activeScene != MenuNavigation.MenuSceneName) return;
                if (nav == null || nav.CaseButtons.Count != MenuNavigation.CaseScenes.Length) return;

                NavLog("NAV_MENU_READY buttons=" + nav.CaseButtons.Count + " clicking case " + caseIndex);
                nav.CaseButtons[caseIndex].onClick.Invoke();
                SetStage(1);
                break;

            case 1: // waiting for the case scene, then clicking HOME
            {
                string expected = MenuNavigation.CaseScenes[caseIndex];
                if (activeScene != expected) return;
                if (nav == null || nav.HomeButton == null) return;

                NavLog("NAV_IN_CASE " + expected + " home button present, clicking HOME");
                nav.HomeButton.onClick.Invoke();
                SetStage(2);
                break;
            }

            case 2: // waiting to be back in the menu
                if (activeScene != MenuNavigation.MenuSceneName) return;

                NavLog("NAV_ROUND_TRIP_OK case=" + caseIndex + " scene=" + MenuNavigation.CaseScenes[caseIndex]);
                caseIndex++;
                if (caseIndex >= MenuNavigation.CaseScenes.Length)
                {
                    NavLog("NAV_OK all " + MenuNavigation.CaseScenes.Length + " cases reachable and returnable");
                    NavFinish(0);
                    return;
                }
                SessionState.SetInt(KeyNavCase, caseIndex);
                SetStage(0);
                break;
        }
    }

    static void SetStage(int stage)
    {
        SessionState.SetInt(KeyNavStage, stage);
        _navStepStart = EditorApplication.timeSinceStartup;
    }

    static void NavLog(string message)
    {
        Debug.Log("[MenuSetup] " + message);
        System.Console.WriteLine("[MenuSetup] " + message);
    }

    static void NavFail(string message, int exitCode)
    {
        Debug.LogError("[MenuSetup] " + message);
        System.Console.WriteLine("[MenuSetup] FAILED " + message);
        NavFinish(exitCode);
    }

    /// <summary>
    /// Leaves play mode first and only then quits. Calling <c>EditorApplication.Exit</c> from inside a
    /// running play session is what makes batchmode hang, so the exit code is parked in SessionState (the
    /// only thing that survives the domain reload on play-mode exit) and spent once the editor is idle.
    /// </summary>
    static void NavFinish(int exitCode)
    {
        SessionState.SetInt(KeyNavActive, 0);
        SessionState.SetInt(KeyNavExit, exitCode);
        EditorApplication.update -= DriveNav;
        _navHooked = false;

        EditorApplication.update += DriveExit;
        if (EditorApplication.isPlaying) EditorApplication.isPlaying = false;
    }

    // ================================================================== menu screenshot

    static bool _shotHooked;
    static double _shotStart;
    static int _shotStage;
    static RenderTexture _shotRt;

    /// <summary>
    /// Zero-argument entry point that writes one PNG of the live menu to
    /// <c>.plan-build/verify/Menu/main_menu.png</c>, so the picker can actually be looked at instead of
    /// being described.
    ///
    /// A ScreenSpaceOverlay canvas never renders into a camera's target texture, so the capture flips
    /// every canvas to ScreenSpaceCamera for the shot. It also pins each CanvasScaler to the exact scale
    /// factor ScaleWithScreenSize would produce at <see cref="ShotHeight"/> (matchWidthOrHeight is 1, i.e.
    /// height-matched), because in batchmode Screen.height is whatever the offscreen game view happens to
    /// be - which would otherwise silently scale the layout and make the shot a lie.
    /// </summary>
    public static void CaptureMenuShot()
    {
        SessionState.SetInt(KeyShotActive, 1);
        SessionState.SetInt(KeyNavExit, -1);
        _shotStage = 0;
        EditorSceneManager.OpenScene(MenuScenePath, OpenSceneMode.Single);
        HookShot();
        Debug.Log("[MenuSetup] SHOT_START " + MenuScenePath);
        EditorApplication.EnterPlaymode();
    }

    static void HookShot()
    {
        if (_shotHooked) return;
        _shotHooked = true;
        _shotStart = EditorApplication.timeSinceStartup;
        EditorApplication.update += DriveShot;
    }

    static void DriveShot()
    {
        if (SessionState.GetInt(KeyShotActive, 0) != 1) return;
        if (!EditorApplication.isPlaying) return;

        if (EditorApplication.timeSinceStartup - _shotStart > 120.0)
        {
            Debug.LogError("[MenuSetup] SHOT_TIMEOUT stage=" + _shotStage);
            ShotFinish(4);
            return;
        }

        Camera cam = Object.FindFirstObjectByType<Camera>(FindObjectsInactive.Include);
        MenuNavigation nav = Object.FindFirstObjectByType<MenuNavigation>(FindObjectsInactive.Include);

        switch (_shotStage)
        {
            case 0:
            {
                if (EditorApplication.timeSinceStartup - _shotStart < 3.0) return;
                if (cam == null || nav == null || nav.CaseButtons.Count != MenuNavigation.CaseScenes.Length) return;

                _shotRt = new RenderTexture(ShotWidth, ShotHeight, 24, RenderTextureFormat.ARGB32);
                _shotRt.Create();
                cam.targetTexture = _shotRt;

                Canvas[] canvases = Object.FindObjectsByType<Canvas>(FindObjectsInactive.Include, FindObjectsSortMode.None);
                for (int i = 0; i < canvases.Length; i++)
                {
                    canvases[i].renderMode = RenderMode.ScreenSpaceCamera;
                    canvases[i].worldCamera = cam;
                    canvases[i].planeDistance = 1f;

                    CanvasScaler scaler = canvases[i].GetComponent<CanvasScaler>();
                    if (scaler == null) continue;
                    scaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;
                    scaler.scaleFactor = ShotHeight / 1728f;
                }
                cam.nearClipPlane = 0.05f;

                _shotStage = 1;
                _shotStart = EditorApplication.timeSinceStartup;
                return;
            }

            case 1:
            {
                // Give the canvases a couple of frames to rebuild at the new size before rendering.
                if (EditorApplication.timeSinceStartup - _shotStart < 0.6) return;
                if (cam == null || _shotRt == null) { ShotFinish(5); return; }

                cam.Render();

                RenderTexture previous = RenderTexture.active;
                RenderTexture.active = _shotRt;
                Texture2D tex = new Texture2D(ShotWidth, ShotHeight, TextureFormat.RGB24, false);
                tex.ReadPixels(new Rect(0f, 0f, ShotWidth, ShotHeight), 0, 0);
                tex.Apply();
                RenderTexture.active = previous;

                string dir = Path.Combine(Directory.GetCurrentDirectory(), ShotDir);
                Directory.CreateDirectory(dir);
                string file = Path.Combine(dir, "main_menu.png");
                File.WriteAllBytes(file, tex.EncodeToPNG());
                Object.DestroyImmediate(tex);

                cam.targetTexture = null;
                _shotRt.Release();
                Object.DestroyImmediate(_shotRt);
                _shotRt = null;

                Debug.Log("[MenuSetup] SHOT_OK " + file + " " + ShotWidth + "x" + ShotHeight);
                System.Console.WriteLine("[MenuSetup] SHOT_OK " + file);
                _shotStage = 2;
                ShotFinish(0);
                return;
            }
        }
    }

    static void ShotFinish(int exitCode)
    {
        SessionState.SetInt(KeyShotActive, 0);
        SessionState.SetInt(KeyNavExit, exitCode);
        EditorApplication.update -= DriveShot;
        _shotHooked = false;

        EditorApplication.update += DriveExit;
        if (EditorApplication.isPlaying) EditorApplication.isPlaying = false;
    }

    static void DriveExit()
    {
        if (EditorApplication.isPlaying || EditorApplication.isPlayingOrWillChangePlaymode) return;

        int code = SessionState.GetInt(KeyNavExit, 0);
        SessionState.SetInt(KeyNavExit, -1);
        EditorApplication.update -= DriveExit;

        System.Console.WriteLine("[MenuSetup] NAV_TEST_EXIT " + code);
        if (Application.isBatchMode) EditorApplication.Exit(code);
        else if (code != 0) Debug.LogError("[MenuSetup] nav test exit code " + code);
    }
}
