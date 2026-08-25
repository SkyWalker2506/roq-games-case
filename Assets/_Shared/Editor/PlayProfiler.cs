#if UNITY_EDITOR
using System;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using UnityEditor;
using UnityEngine;
using Debug = UnityEngine.Debug;

/// <summary>
/// TEMPORARY measurement harness. Times (a) every [InitializeOnLoad] static constructor in the project
/// and (b) the phases of a play-mode entry, and prints both to the log. Lives in Shared.Editor because
/// that assembly is processed before Assembly-CSharp-Editor, so it can force-run - and therefore truly
/// time - the gate constructors that live there before Unity gets to them.
/// </summary>
[InitializeOnLoad]
public static class PlayProfiler
{
    const string KeyActive = "PlayProfiler.Active";
    const string KeyMarks = "PlayProfiler.Marks";

    static readonly string[] Targets =
    {
        "Case3SilhouetteGate", "Case3SelectionGate", "Case1InteractiveRecorder", "Case1SelectionGate",
        "MenuSetup", "Case2InputGate", "Case4InputGate", "Case4LayoutGateDriver",
        "Shared.EditorTools.BatchAudioMute", "FrameStripCapture",
    };

    static bool _hooked;
    static bool _reported;
    static int _lastFrameMarked;
    static int _round;

    static PlayProfiler()
    {
        // Inert unless a run explicitly asks for it: this tool must not add cost to the thing it measures.
        if (SessionState.GetInt(KeyActive, 0) != 1 &&
            string.IsNullOrEmpty(Environment.GetEnvironmentVariable("PROFILE_CTORS"))) return;

        double t0 = EditorApplication.timeSinceStartup;
        StringBuilder sb = new StringBuilder();
        for (int i = 0; i < Targets.Length; i++)
        {
            Type t = FindType(Targets[i]);
            if (t == null) { sb.Append(Targets[i]).Append("=NOTFOUND "); continue; }
            Stopwatch sw = Stopwatch.StartNew();
            try { RuntimeHelpers.RunClassConstructor(t.TypeHandle); }
            catch (Exception) { sb.Append(Targets[i]).Append("=ERR "); continue; }
            sw.Stop();
            sb.Append(Targets[i]).Append('=').Append(sw.Elapsed.TotalMilliseconds.ToString("F4")).Append("ms ");
        }
        string line = "[PlayProfiler] CTOR_COSTS " + sb + "| forcedLoopTotal=" +
                      ((EditorApplication.timeSinceStartup - t0) * 1000.0).ToString("F3") + "ms";
        Debug.Log(line);
        Console.WriteLine(line);

        Mark("domainReloaded");
        if (SessionState.GetInt(KeyActive, 0) == 1) Hook();
    }

    static Type FindType(string name)
    {
        Assembly[] asms = AppDomain.CurrentDomain.GetAssemblies();
        for (int i = 0; i < asms.Length; i++)
        {
            Type t = asms[i].GetType(name, false);
            if (t != null) return t;
        }
        return null;
    }

    static void Mark(string name)
    {
        string marks = SessionState.GetString(KeyMarks, "");
        SessionState.SetString(KeyMarks, marks + name + "=" + EditorApplication.timeSinceStartup.ToString("F4") + ";");
    }

    /// <summary>Entry point: opens a scene, waits for an idle Editor, enters play mode, times, exits.</summary>
    public static void Run()
    {
        SessionState.SetString(KeyMarks, "");
        SessionState.SetInt(KeyActive, 1);
        // timeSinceStartup at the moment -executeMethod fires IS the editor startup cost: assembly
        // loading, the initial asset refresh and every package's InitializeOnLoad, all before this line.
        Console.WriteLine("[PlayProfiler] EDITOR_STARTUP_MS " +
                          (EditorApplication.timeSinceStartup * 1000.0).ToString("F0") +
                          " loadedAssemblies=" + AppDomain.CurrentDomain.GetAssemblies().Length);
        Mark("executeMethod");

        string scene = Environment.GetEnvironmentVariable("PROFILE_SCENE");
        if (!string.IsNullOrEmpty(scene))
        {
            UnityEditor.SceneManagement.EditorSceneManager.OpenScene(scene, UnityEditor.SceneManagement.OpenSceneMode.Single);
        }
        Mark("sceneOpened");
        Hook();

        double warm = 0;
        double.TryParse(Environment.GetEnvironmentVariable("PROFILE_WARM_WAIT") ?? "0",
                        System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out warm);
        if (warm > 0)
        {
            // Wait for a settled Editor: the owner presses Play in a warm session, not 10s after launch.
            EditorApplication.update += WaitThenPlay;
            _warmUntil = EditorApplication.timeSinceStartup + warm;
            return;
        }
        StartPlay();
    }

    static double _warmUntil;

    static void WaitThenPlay()
    {
        if (EditorApplication.timeSinceStartup < _warmUntil) return;
        if (EditorApplication.isCompiling || EditorApplication.isUpdating) return;
        EditorApplication.update -= WaitThenPlay;
        Mark("editorIdle");
        StartPlay();
    }

    static void StartPlay()
    {
        Mark("enterPlaymodeCall");
        EditorApplication.EnterPlaymode();
    }

    static void Hook()
    {
        if (_hooked) return;
        _hooked = true;
        EditorApplication.update += Drive;
        EditorApplication.playModeStateChanged += OnPlayModeChanged;
        AssemblyReloadEvents.beforeAssemblyReload += OnBeforeReload;
        AssemblyReloadEvents.afterAssemblyReload += OnAfterReload;
    }

    static void OnBeforeReload() { Mark("beforeAssemblyReload"); }
    static void OnAfterReload() { Mark("afterAssemblyReload"); }
    static void OnPlayModeChanged(PlayModeStateChange c) { Mark("pms_" + c); }

    static void Drive()
    {
        if (SessionState.GetInt(KeyActive, 0) != 1) return;
        if (_reported) return;
        if (!EditorApplication.isPlaying) return;

        int f = Time.frameCount;
        if (f > _lastFrameMarked && f <= 6) { _lastFrameMarked = f; Mark("frame" + f); }
        if (f < 6) return;

        int rounds = 1;
        int.TryParse(Environment.GetEnvironmentVariable("PROFILE_ROUNDS") ?? "1", out rounds);
        _round++;
        Report("round " + _round);
        if (_round < rounds)
        {
            // Leave and re-enter: with the domain reload disabled this is the run that would expose a
            // static that survives a Play, so the second round is the one worth reading.
            SessionState.SetString(KeyMarks, "");
            _lastFrameMarked = 0;
            EditorApplication.isPlaying = false;
            EditorApplication.delayCall += () =>
            {
                Mark("enterPlaymodeCall");
                EditorApplication.EnterPlaymode();
            };
            return;
        }

        _reported = true;
        SessionState.SetInt(KeyActive, 0);
        EditorApplication.Exit(0);
    }

    static void Report(string label)
    {
        string marks = SessionState.GetString(KeyMarks, "");
        string[] parts = marks.Split(';');
        double first = -1, prev = -1;
        StringBuilder sb = new StringBuilder();
        sb.Append("\n[PlayProfiler] ===== PLAY MODE ENTRY BREAKDOWN (" + label + ") =====\n");
        sb.Append(string.Format("{0,-28} {1,10} {2,10}\n", "mark", "delta_ms", "cum_ms"));
        for (int i = 0; i < parts.Length; i++)
        {
            if (string.IsNullOrEmpty(parts[i])) continue;
            int eq = parts[i].LastIndexOf('=');
            string name = parts[i].Substring(0, eq);
            double t = double.Parse(parts[i].Substring(eq + 1), System.Globalization.CultureInfo.InvariantCulture);
            if (first < 0) { first = t; prev = t; }
            sb.Append(string.Format("{0,-28} {1,10:F1} {2,10:F1}\n", name, (t - prev) * 1000.0, (t - first) * 1000.0));
            prev = t;
        }
        sb.Append("[PlayProfiler] TOTAL_PLAY_ENTRY_MS " + ((prev - first) * 1000.0).ToString("F1") + "\n");
        sb.Append("[PlayProfiler] batchMode=" + Application.isBatchMode + "\n");
        Console.WriteLine(sb.ToString());
        Debug.Log(sb.ToString());
    }
}
#endif
