using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Shared.Sequencing;

/// <summary>
/// Plays a case scene's <see cref="SequenceDirector"/> and writes 16 evenly spaced PNG frames plus the
/// sequence report to .plan-build/verify/&lt;scene&gt;/. The sequence is run twice: once to measure its real
/// duration, once to sample it at 16 equal intervals.
/// Usage: tools/unity-run.sh -batchmode -executeMethod FrameStripCapture.CaptureAll -logFile ...
/// (deliberately no -quit: the harness exits by itself once every scene is captured).
/// </summary>
[InitializeOnLoad]
public static class FrameStripCapture
{
    /// <summary>
    /// Number of frames written per sequence. 16 is the verification strip - enough to check poses and
    /// colour, and small enough to read at a glance. Raise it through <see cref="SetFrameCount"/> to
    /// film the same sequence densely enough to encode as video; the sampling is identical either way,
    /// so the video shows exactly what the strip verifies rather than a separate render path.
    /// </summary>
    public static int FrameCount { get; private set; }

    /// <summary>Sets the sample count for the next capture. Values below 2 are ignored.</summary>
    public static void SetFrameCount(int frames)
    {
        if (frames < 2) return;
        FrameCount = frames;
        SessionState.SetInt("FrameStripCapture.Frames", frames);
    }

    /// <summary>Restores the verification strip's 16 samples.</summary>
    public static void ResetFrameCount() { FrameCount = 16; SessionState.SetInt("FrameStripCapture.Frames", 16); }

    // The reference clips are 1080x1728. Capturing at half that made every thin thing - glyph edges,
    // sparkle stars, the rail, bevels - read worse than the reference purely because of resolution, and
    // a side-by-side at two different scales is not a fair comparison of anything.
    const int FrameWidth = 1080;
    const int FrameHeight = 1728;
    const double SceneTimeout = 90.0;
    const float MinDuration = 0.2f;
    const float ReportEpsilon = 0.0001f;

    const string KeyQueue = "FrameStripCapture.Queue";
    const string KeyCurrent = "FrameStripCapture.Current";
    const string KeyStrict = "FrameStripCapture.Strict";
    const string KeyCaptured = "FrameStripCapture.Captured";

    // Per-playmode-session state. Recreated on every domain reload, which is fine: a capture never
    // spans a reload, only the queue does (and that lives in SessionState).
    static int _stage;
    static int _frame;
    static float _total;
    static double _stageStart;
    static bool _sessionInit;
    static bool _hooked;
    static int _stableTicks;
    static double _lastTick;
    static string _measuredCaseName = "";
    static float _measuredReportDuration;
    static SequenceStep[] _measuredSteps = new SequenceStep[0];
    static SequenceJuiceEventRecord[] _measuredEvents = new SequenceJuiceEventRecord[0];
    static bool _captureClockConfigured;
    static bool _captureClockActive;
    static int _captureFramerate;
    static int _previousCaptureFramerate;

    const string KeyFrames = "FrameStripCapture.Frames";

    static FrameStripCapture()
    {
        // The count has to survive the domain reload a play-mode entry causes, so it lives in
        // SessionState like the rest of the capture's state rather than in a plain static.
        FrameCount = Mathf.Max(2, SessionState.GetInt(KeyFrames, 16));
        if (string.IsNullOrEmpty(SessionState.GetString(KeyCurrent, ""))) return;
        Hook();
    }

    // ------------------------------------------------------------------ entry points

    /// <summary>Captures every scene in the project that contains a <see cref="SequenceDirector"/>; scenes without one are skipped.</summary>
    public static void CaptureAll()
    {
        List<string> scenes = new List<string>();
        foreach (string guid in AssetDatabase.FindAssets("t:Scene", new[] { "Assets" }))
        {
            scenes.Add(AssetDatabase.GUIDToAssetPath(guid));
        }
        scenes.Sort();
        Begin(scenes, false);
    }

    /// <summary>Captures 180 frames (60fps video quality) for all scenes.</summary>
    public static void CaptureAllVideos()
    {
        SetFrameCount(180);
        CaptureAll();
    }

    /// <summary>Captures a single scene by name or asset path. Fails loudly if that scene has no <see cref="SequenceDirector"/>.</summary>
    public static void Capture(string sceneName)
    {
        string path = ResolveScenePath(sceneName);
        if (string.IsNullOrEmpty(path))
        {
            Fail("SCENE_NOT_FOUND: " + sceneName, 2);
            return;
        }
        Begin(new List<string> { path }, true);
    }

    // ------------------------------------------------------------------ queue driving

    static void Begin(List<string> scenePaths, bool strict)
    {
        RestoreCaptureClock();
        if (scenePaths.Count == 0)
        {
            Fail("NO_SCENES", 2);
            return;
        }

        // Cleared HERE, at queue setup, deliberately NOT next to EditorApplication.EnterPlaymode:
        // the harness's first pass measures the sequence's real duration against wall-clock, so
        // hundreds of synchronous File.Delete calls immediately before play-mode entry can shift that
        // measurement by a frame. At Begin the work is far from any timed path.
        for (int i = 0; i < scenePaths.Count; i++) ClearStaleFrames(scenePaths[i]);

        SessionState.SetString(KeyQueue, string.Join(";", scenePaths));
        SessionState.SetString(KeyCurrent, "");
        SessionState.SetInt(KeyStrict, strict ? 1 : 0);
        SessionState.SetInt(KeyCaptured, 0);
        Hook();
        StartNext();
    }

    static void StartNext()
    {
        RestoreCaptureClock();
        string queue = SessionState.GetString(KeyQueue, "");
        if (string.IsNullOrEmpty(queue))
        {
            int captured = SessionState.GetInt(KeyCaptured, 0);
            if (captured == 0)
            {
                Fail("NO_DIRECTOR_IN_ANY_SCENE: nothing was captured. Add a SequenceDirector to a scene first.", 3);
                return;
            }
            Log("CAPTURE_DONE scenes=" + captured);
            Finish(0);
            return;
        }

        string[] parts = queue.Split(';');
        string path = parts[0];
        SessionState.SetString(KeyQueue, parts.Length > 1 ? string.Join(";", parts, 1, parts.Length - 1) : "");

        EditorSceneManager.OpenScene(path, OpenSceneMode.Single);

        SequenceDirector director = Object.FindFirstObjectByType<SequenceDirector>(FindObjectsInactive.Include);
        if (director == null)
        {
            if (SessionState.GetInt(KeyStrict, 0) == 1)
            {
                Fail("NO_DIRECTOR: " + path + " has no SequenceDirector component; nothing to capture.", 2);
                return;
            }
            Log("SKIP_NO_DIRECTOR: " + path);
            StartNext();
            return;
        }

        SessionState.SetString(KeyCurrent, path);
        _sessionInit = false;   // the play-mode domain reload wipes the fields below; Drive re-seeds them
        Log("CAPTURE_SCENE: " + path);
        EditorApplication.EnterPlaymode();
    }

    static void Hook()
    {
        if (_hooked) return;
        _hooked = true;
        EditorApplication.update += Drive;
        EditorApplication.playModeStateChanged += OnPlayModeChanged;
        AssemblyReloadEvents.beforeAssemblyReload += RestoreCaptureClock;
        EditorApplication.quitting += RestoreCaptureClock;
    }

    static void OnPlayModeChanged(PlayModeStateChange change)
    {
        if (change == PlayModeStateChange.ExitingPlayMode)
        {
            RestoreCaptureClock();
            return;
        }
        if (change != PlayModeStateChange.EnteredEditMode) return;
        RestoreCaptureClock();
        _sessionInit = false;
        if (string.IsNullOrEmpty(SessionState.GetString(KeyCurrent, ""))) return;

        SessionState.SetString(KeyCurrent, "");
        StartNext();
    }

    // ------------------------------------------------------------------ playmode state machine

    static void Drive()
    {
        string current = SessionState.GetString(KeyCurrent, "");
        if (string.IsNullOrEmpty(current)) return;
        if (!EditorApplication.isPlaying) return;

        if (!_sessionInit)
        {
            _sessionInit = true;
            _stage = 0;
            _frame = 0;
            _total = 0f;
            _stageStart = EditorApplication.timeSinceStartup;
            _stableTicks = 0;
            _lastTick = _stageStart;
            _measuredCaseName = "";
            _measuredReportDuration = 0f;
            _measuredSteps = new SequenceStep[0];
            _measuredEvents = new SequenceJuiceEventRecord[0];
            _captureClockConfigured = false;
            _captureFramerate = 0;
            // Pay the first-render / first-encode cost now. Doing it later would stall the player loop
            // right where the capture clock starts and shift the whole sampling window.
            WarmupCapturePath();
        }

        if (EditorApplication.timeSinceStartup - _stageStart > SceneTimeout)
        {
            Fail("TIMEOUT stage=" + _stage + " scene=" + current, 4);
            return;
        }

        SequenceDirector director = Object.FindFirstObjectByType<SequenceDirector>(FindObjectsInactive.Include);
        if (director == null)
        {
            Fail("NO_DIRECTOR_IN_PLAYMODE: " + current, 2);
            return;
        }

        if (!_captureClockConfigured) ConfigureCaptureClock(director);

        switch (_stage)
        {
            case 0: // nothing auto-plays any more: the harness starts the sequence itself, once the
                    // scene has settled (lesson #10: the first playmode frames stall for seconds and would
                    // otherwise eat the first phase of the sequence).
            {
                if (director.IsPlaying)
                {
                    _stage = 1;
                    _stageStart = EditorApplication.timeSinceStartup;
                    break;
                }

                double now = EditorApplication.timeSinceStartup;
                double tick = now - _lastTick;
                _lastTick = now;
                _stableTicks = tick <= 0.030 ? _stableTicks + 1 : 0;

                bool settled = now - _stageStart >= 1.0 && _stableTicks >= 5;
                if (settled || now - _stageStart > 6.0)
                {
                    Log("HARNESS_PLAY settled=" + (settled ? 1 : 0) +
                        " after=" + (now - _stageStart).ToString("0.00") + "s");
                    director.AllowPlayWithoutInput();   // batchmode has no real input behind the call
                    director.Play();
                }
                break;
            }

            case 1: // measuring pass
                if (!director.IsPlaying && director.Report.completed)
                {
                    _measuredCaseName = director.Report.caseName;
                    _measuredReportDuration = director.Report.totalDuration;
                    _measuredSteps = director.Report.steps.ToArray();
                    _measuredEvents = director.Report.events.ToArray();
                    float tail = Mathf.Max(0f, director.CaptureTailDuration);
                    _total = Mathf.Max(MinDuration, _measuredReportDuration + tail);
                    Log("MEASURED duration=" + _measuredReportDuration.ToString("0.000") +
                        "s tail=" + tail.ToString("0.000") + "s strip=" + _total.ToString("0.000") +
                        "s scene=" + current);
                    director.Replay();
                    _frame = 0;
                    _stage = 2;
                    _stageStart = EditorApplication.timeSinceStartup;
                }
                break;

            case 2: // capture pass, 16 samples across the measured interaction plus any cosmetic tail
            {
                // SequenceTime deliberately keeps advancing after Report.completed so a cosmetic particle
                // tail can be filmed without keeping the director (and therefore player input) busy.
                float elapsed = director.SequenceTime;
                float target = _frame < FrameCount ? _frame * _total / (FrameCount - 1) : _total;
                if (_frame < FrameCount && elapsed >= target)
                {
                    float lateness = elapsed - target;
                    bool strict = SessionState.GetInt(KeyStrict, 0) == 1;
                    if (strict && _captureFramerate > 0 && lateness > 1f / _captureFramerate + ReportEpsilon)
                    {
                        Fail(string.Format("NONDETERMINISTIC_SAMPLE frame={0:00} target={1:0.000000} " +
                                           "actual={2:0.000000} late={3:0.000000} fps={4} scene={5}",
                            _frame, target, elapsed, lateness, _captureFramerate, current), 6);
                        return;
                    }

                    CaptureFrame(current, _frame);
                    Log(string.Format("FRAME {0:00} target={1:0.000} seqTime={2:0.000} late={3:0.0000}",
                        _frame, target, elapsed, lateness));
                    _frame++;
                }
                if (_frame >= FrameCount)
                {
                    _stage = 3;
                    _stageStart = EditorApplication.timeSinceStartup;
                }
                break;
            }

            case 3: // let the sequence finish, then dump the report
                if (!director.IsPlaying || EditorApplication.timeSinceStartup - _stageStart > 5.0)
                {
                    string mismatch;
                    if (!CaptureReportMatches(director, out mismatch))
                    {
                        Fail("NONDETERMINISTIC_REPORT scene=" + current + " " + mismatch, 6);
                        return;
                    }

                    WriteReport(current, director);
                    SessionState.SetInt(KeyCaptured, SessionState.GetInt(KeyCaptured, 0) + 1);
                    _stage = 4;
                    RestoreCaptureClock();
                    EditorApplication.isPlaying = false;
                }
                break;
        }
    }

    // ------------------------------------------------------------------ output

    /// <summary>Runs one throwaway render and PNG encode so the first real sample is not delayed by first-use cost.</summary>
    static void WarmupCapturePath()
    {
        Camera cam = Camera.main;
        if (cam == null) return;

        for (int i = 0; i < 2; i++)
        {
            Texture2D tex = RenderToTexture(cam);
            if (tex == null) return;
            tex.EncodeToPNG();
            Object.DestroyImmediate(tex);
        }
    }

    static Texture2D RenderToTexture(Camera cam)
    {
        RenderTexture rt = RenderTexture.GetTemporary(FrameWidth, FrameHeight, 24, RenderTextureFormat.ARGB32);
        RenderTexture previousTarget = cam.targetTexture;
        cam.targetTexture = rt;
        cam.Render();
        cam.targetTexture = previousTarget;

        RenderTexture previousActive = RenderTexture.active;
        RenderTexture.active = rt;
        Texture2D tex = new Texture2D(FrameWidth, FrameHeight, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0f, 0f, FrameWidth, FrameHeight), 0, 0);
        tex.Apply(false);
        RenderTexture.active = previousActive;
        RenderTexture.ReleaseTemporary(rt);
        return tex;
    }

    /// <summary>
    /// Deletes any frame_*.png left by a PREVIOUS capture of this scene, before the new strip is written.
    ///
    /// Without this, a short strip run into a directory holding a long one leaves the tail behind and the
    /// run still reports CAPTURE_DONE with exit code 0. Measured on Case 3: FrameCount 16 wrote frames
    /// 00-15 and left 16-239 from an earlier 240-frame run, so the directory held two different timebases
    /// (0.141 s spacing for the first 16, 0.009 s for the rest) and any reader would have measured a strip
    /// that never existed. The exit code cannot distinguish that case, which made it a trap for every
    /// downstream consumer; clearing first removes the possibility instead of relying on the reader to
    /// check file mtimes.
    ///
    /// Scoped to each captured scene's own OutputDirectory, so concurrent captures of other scenes are
    /// untouched. Called from Begin (queue setup), never from the play-mode entry path.
    /// </summary>
    static void ClearStaleFrames(string scenePath)
    {
        string dir = OutputDirectory(scenePath);
        string[] existing = Directory.GetFiles(dir, "frame_*.png");
        for (int i = 0; i < existing.Length; i++) File.Delete(existing[i]);
        if (existing.Length > 0) Log("CLEARED_STALE_FRAMES count=" + existing.Length + " dir=" + dir);
    }

    static void CaptureFrame(string scenePath, int index)
    {
        Camera cam = Camera.main;
        if (cam == null)
        {
            Fail("NO_MAIN_CAMERA: " + scenePath, 5);
            return;
        }

        Texture2D tex = RenderToTexture(cam);

        string dir = OutputDirectory(scenePath);
        File.WriteAllBytes(Path.Combine(dir, string.Format("frame_{0:00}.png", index)), tex.EncodeToPNG());
        Object.DestroyImmediate(tex);
    }

    static void WriteReport(string scenePath, SequenceDirector director)
    {
        string dir = OutputDirectory(scenePath);
        string json = director.Report.ToJson(true);
        File.WriteAllText(Path.Combine(dir, "report.json"), json);
        Log("REPORT_WRITTEN: " + Path.Combine(dir, "report.json"));
    }

    static string OutputDirectory(string scenePath)
    {
        string root = Directory.GetParent(Application.dataPath).FullName;
        string dir = Path.Combine(root, ".plan-build", "verify", Path.GetFileNameWithoutExtension(scenePath));
        Directory.CreateDirectory(dir);
        return dir;
    }

    // ------------------------------------------------------------------ helpers

    static void ConfigureCaptureClock(SequenceDirector director)
    {
        _captureClockConfigured = true;
        _captureFramerate = Mathf.Max(0, director.DeterministicCaptureFramerate);
        if (_captureFramerate == 0) return;

        _previousCaptureFramerate = Time.captureFramerate;
        Time.captureFramerate = _captureFramerate;
        _captureClockActive = true;
        Log("FIXED_CLOCK fps=" + _captureFramerate + " previous=" + _previousCaptureFramerate +
            " sequence=" + director.SequenceName);
    }

    static void RestoreCaptureClock()
    {
        if (!_captureClockActive) return;
        Time.captureFramerate = _previousCaptureFramerate;
        _captureClockActive = false;
    }

    static bool CaptureReportMatches(SequenceDirector director, out string mismatch)
    {
        mismatch = null;
        if (_captureFramerate <= 0) return true;

        SequenceReport captured = director.Report;
        float tolerance = 1f / _captureFramerate + ReportEpsilon;
        if (!captured.completed)
        {
            mismatch = "capture report did not complete";
            return false;
        }
        if (captured.caseName != _measuredCaseName)
        {
            mismatch = "case measured=" + _measuredCaseName + " captured=" + captured.caseName;
            return false;
        }
        if (Mathf.Abs(captured.totalDuration - _measuredReportDuration) > tolerance)
        {
            mismatch = string.Format("total measured={0:0.000000} captured={1:0.000000} tolerance={2:0.000000}",
                _measuredReportDuration, captured.totalDuration, tolerance);
            return false;
        }
        if (captured.steps.Count != _measuredSteps.Length)
        {
            mismatch = "step-count measured=" + _measuredSteps.Length + " captured=" + captured.steps.Count;
            return false;
        }

        for (int i = 0; i < _measuredSteps.Length; i++)
        {
            SequenceStep measured = _measuredSteps[i];
            SequenceStep actual = captured.steps[i];
            if (measured.name != actual.name ||
                Mathf.Abs(measured.startTime - actual.startTime) > tolerance ||
                Mathf.Abs(measured.duration - actual.duration) > tolerance)
            {
                mismatch = string.Format("step[{0}] measured={1}@{2:0.000000}+{3:0.000000} " +
                                         "captured={4}@{5:0.000000}+{6:0.000000} tolerance={7:0.000000}",
                    i, measured.name, measured.startTime, measured.duration,
                    actual.name, actual.startTime, actual.duration, tolerance);
                return false;
            }
        }

        if (captured.events.Count != _measuredEvents.Length)
        {
            mismatch = "event-count measured=" + _measuredEvents.Length + " captured=" + captured.events.Count;
            return false;
        }
        for (int i = 0; i < _measuredEvents.Length; i++)
        {
            SequenceJuiceEventRecord measured = _measuredEvents[i];
            SequenceJuiceEventRecord actual = captured.events[i];
            if (measured.type != actual.type || measured.detail != actual.detail ||
                Mathf.Abs(measured.time - actual.time) > tolerance)
            {
                mismatch = string.Format("event[{0}] measured={1}@{2:0.000000} captured={3}@{4:0.000000} " +
                                         "tolerance={5:0.000000}",
                    i, measured.type, measured.time, actual.type, actual.time, tolerance);
                return false;
            }
        }

        Log(string.Format("REPORT_MATCH duration={0:0.000000}s steps={1} events={2} tolerance={3:0.000000}",
            captured.totalDuration, captured.steps.Count, captured.events.Count, tolerance));
        return true;
    }

    static string ResolveScenePath(string sceneName)
    {
        if (sceneName.EndsWith(".unity") && File.Exists(sceneName)) return sceneName;
        foreach (string guid in AssetDatabase.FindAssets("t:Scene", new[] { "Assets" }))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            if (Path.GetFileNameWithoutExtension(path) == sceneName || path == sceneName) return path;
        }
        return null;
    }

    static void Log(string message)
    {
        Debug.Log("[FrameStripCapture] " + message);
        System.Console.WriteLine("[FrameStripCapture] " + message);
    }

    static void Fail(string message, int exitCode)
    {
        Debug.LogError("[FrameStripCapture] " + message);
        System.Console.WriteLine("[FrameStripCapture] FAILED " + message);
        Finish(exitCode);
    }

    static void Finish(int exitCode)
    {
        RestoreCaptureClock();
        SessionState.SetString(KeyQueue, "");
        SessionState.SetString(KeyCurrent, "");
        EditorApplication.update -= Drive;
        EditorApplication.playModeStateChanged -= OnPlayModeChanged;
        AssemblyReloadEvents.beforeAssemblyReload -= RestoreCaptureClock;
        EditorApplication.quitting -= RestoreCaptureClock;
        _hooked = false;

        if (Application.isBatchMode) EditorApplication.Exit(exitCode);
        else if (exitCode != 0) Debug.LogError("[FrameStripCapture] exit code " + exitCode);
    }
}
