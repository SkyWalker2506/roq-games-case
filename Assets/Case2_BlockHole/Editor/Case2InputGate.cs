using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Case2;

/// <summary>
/// Zero-argument batchmode gate for the Case 2 input path. Opens the scene, enters play mode, attaches
/// <see cref="Case2InputProbe"/> and exits non-zero the moment an assertion fails.
/// Usage: tools/unity-run.sh -batchmode -executeMethod Case2InputGate.Run -logFile ...
/// (no -quit: the gate exits by itself.)
/// </summary>
[InitializeOnLoad]
public static class Case2InputGate
{
    const string ScenePath = "Assets/Case2_BlockHole/Scenes/BlockHole.unity";
    const string KeyActive = "Case2InputGate.Active";
    const double Timeout = 240.0;

    static bool _hooked;
    static double _start;
    static bool _attached;

    static Case2InputGate()
    {
        if (SessionState.GetInt(KeyActive, 0) == 1) Hook();
    }

    /// <summary>Entry point.</summary>
    public static void Run()
    {
        // This gate writes the only pixel evidence Case 2 has for the player path, so it must not
        // run without a graphics device. Under `-nographics` every Camera.Render() produced a flat
        // grey frame: nine bit-identical blanks were filed as proof and the gate still reported
        // INPUT_GATE GREEN failures=0, because rc=0 and a clean transcript cannot tell a
        // screenshot from an empty camera. Refuse the run instead of certifying blanks.
        if (SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null)
        {
            Log("CASE2_INPUT_GATE_FAILED: -nographics. This gate captures screenshots; a null " +
                "graphics device renders flat grey and every visual assertion becomes vacuous. " +
                "Re-run as: tools/unity-run.sh -batchmode -executeMethod Case2InputGate.Run -logFile ...");
            if (Application.isBatchMode) EditorApplication.Exit(2);
            return;
        }

        SessionState.SetInt(KeyActive, 1);
        // Test the authored, committed scene exactly as a player receives it. Wiring is built and
        // saved explicitly before this gate; a test must not repair or rearrange its own subject.
        EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        Log("authored scene opened without mutation " + ScenePath);
        Hook();
        _start = EditorApplication.timeSinceStartup;
        EditorApplication.EnterPlaymode();
    }

    static void Hook()
    {
        if (_hooked) return;
        _hooked = true;
        _start = EditorApplication.timeSinceStartup;
        EditorApplication.update += Drive;
    }

    static void Drive()
    {
        if (SessionState.GetInt(KeyActive, 0) != 1) return;
        if (!EditorApplication.isPlaying) return;

        if (EditorApplication.timeSinceStartup - _start > Timeout)
        {
            Finish(false, "TIMEOUT after " + Timeout + "s");
            return;
        }

        if (!_attached)
        {
            Case2Director director = Object.FindFirstObjectByType<Case2Director>(FindObjectsInactive.Include);
            if (director == null) return;
            director.gameObject.AddComponent<Case2InputProbe>();
            _attached = true;
            Log("probe attached");
            return;
        }

        if (!Case2InputProbe.Finished) return;
        Finish(Case2InputProbe.Passed, Case2InputProbe.Transcript);
    }

    static void Finish(bool passed, string transcript)
    {
        SessionState.SetInt(KeyActive, 0);
        EditorApplication.update -= Drive;
        _hooked = false;

        Log("---- transcript ----\n" + transcript);
        Log(passed ? "CASE2_INPUT_GATE_OK" : "CASE2_INPUT_GATE_FAILED");

        if (Application.isBatchMode) EditorApplication.Exit(passed ? 0 : 1);
        else EditorApplication.isPlaying = false;
    }

    static void Log(string s)
    {
        Debug.Log("[Case2InputGate] " + s);
        System.Console.WriteLine("[Case2InputGate] " + s);
    }
}
