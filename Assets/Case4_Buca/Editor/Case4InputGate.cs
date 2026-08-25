using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Case4;

/// <summary>
/// Zero-argument batchmode gate for the Case 4 input path. Opens the scene, enters play mode, attaches
/// <see cref="Case4InputProbe"/> and exits non-zero the moment an assertion fails.
/// Usage: tools/unity-run.sh -batchmode -executeMethod Case4InputGate.Run -logFile ...
/// (no -quit: the gate exits by itself.)
/// </summary>
[InitializeOnLoad]
public static class Case4InputGate
{
    const string ScenePath = "Assets/Case4_Buca/Scenes/Buca.unity";
    const string KeyActive = "Case4InputGate.Active";
    const double Timeout = 240.0;

    static bool _hooked;
    static double _start;
    static bool _attached;

    static Case4InputGate()
    {
        if (SessionState.GetInt(KeyActive, 0) == 1) Hook();
    }

    /// <summary>Entry point.</summary>
    public static void Run()
    {
        SessionState.SetInt(KeyActive, 1);
        EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        Log("authored scene opened " + ScenePath);
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
            Case4Director director = Object.FindFirstObjectByType<Case4Director>(FindObjectsInactive.Include);
            if (director == null) return;
            director.gameObject.AddComponent<Case4InputProbe>();
            _attached = true;
            Log("probe attached");
            return;
        }

        if (!Case4InputProbe.Finished) return;
        Finish(Case4InputProbe.Passed, Case4InputProbe.Transcript);
    }

    static void Finish(bool passed, string transcript)
    {
        SessionState.SetInt(KeyActive, 0);
        EditorApplication.update -= Drive;
        _hooked = false;

        Log("---- transcript ----\n" + transcript);
        Log(passed ? "CASE4_INPUT_GATE_OK" : "CASE4_INPUT_GATE_FAILED");

        if (Application.isBatchMode) EditorApplication.Exit(passed ? 0 : 1);
        else EditorApplication.isPlaying = false;
    }

    static void Log(string s)
    {
        Debug.Log("[Case4InputGate] " + s);
        System.Console.WriteLine("[Case4InputGate] " + s);
    }
}
