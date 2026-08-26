using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Case4;

/// <summary>
/// Zero-argument batchmode gate for the owner's report "bazen vursak bile altin toplama efekti
/// calismiyor". Opens the authored scene, enters play mode, attaches <see cref="Case4PayoutProbe"/>
/// and exits non-zero if the pre-registered payout invariant broke on any shot that reached the
/// stack.
///
/// Usage:
///   tools/unity-run.sh -batchmode -executeMethod Case4PayoutGate.Run       -logFile ...
///   tools/unity-run.sh -batchmode -executeMethod Case4PayoutGate.RunMutated -logFile ...
///
/// RunMutated restores the flight loop this work replaced (Case4Director.legacyFixedFlightBudget)
/// and REQUIRES the invariant to break, so the assertion is shown red against the unfixed behaviour
/// in a run where COIN_EXIT and COIN_GAP are still green. An invariant that has never been red is a
/// sentence, not a measurement.
///
/// No -quit: the gate exits by itself. A batch entry point that finishes its work and then sits
/// there holds tools/unity-run.sh's lock and every later run queues behind it in silence.
/// </summary>
[InitializeOnLoad]
public static class Case4PayoutGate
{
    const string ScenePath = "Assets/Case4_Buca/Scenes/Buca.unity";
    const string KeyActive = "Case4PayoutGate.Active";
    const string KeyMutate = "Case4PayoutGate.Mutate";
    const string KeyShots = "Case4PayoutGate.Shots";
    const double Timeout = 900.0;
    const int DefaultShots = 24;

    static bool _hooked;
    static double _start;
    static bool _attached;

    static Case4PayoutGate()
    {
        if (SessionState.GetInt(KeyActive, 0) == 1) Hook();
    }

    /// <summary>Entry point: the current tree. The invariant must hold on every covered shot.</summary>
    public static void Run() { Begin(false, DefaultShots); }

    /// <summary>Entry point: the pre-fix flight loop. The invariant must BREAK.</summary>
    public static void RunMutated() { Begin(true, DefaultShots); }

    static void Begin(bool mutate, int shots)
    {
        SessionState.SetInt(KeyActive, 1);
        SessionState.SetInt(KeyMutate, mutate ? 1 : 0);
        SessionState.SetInt(KeyShots, shots);
        EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        Log("authored scene opened " + ScenePath + "; mutate=" + mutate + " shots=" + shots);
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
            Case4PayoutProbe.Shots = SessionState.GetInt(KeyShots, DefaultShots);
            Case4PayoutProbe.Mutate = SessionState.GetInt(KeyMutate, 0) == 1;
            director.gameObject.AddComponent<Case4PayoutProbe>();
            _attached = true;
            Log("probe attached (shots=" + Case4PayoutProbe.Shots + ", mutate=" + Case4PayoutProbe.Mutate + ")");
            return;
        }

        if (!Case4PayoutProbe.Finished) return;
        Finish(Case4PayoutProbe.Passed, Case4PayoutProbe.Transcript);
    }

    static void Finish(bool passed, string transcript)
    {
        SessionState.SetInt(KeyActive, 0);
        EditorApplication.update -= Drive;
        _hooked = false;
        _attached = false;

        Log("---- transcript ----\n" + transcript);
        Log(passed ? "CASE4_PAYOUT_GATE_OK" : "CASE4_PAYOUT_GATE_FAILED");

        if (Application.isBatchMode) EditorApplication.Exit(passed ? 0 : 1);
        else EditorApplication.isPlaying = false;
    }

    static void Log(string s)
    {
        Debug.Log("[Case4PayoutGate] " + s);
        System.Console.WriteLine("[Case4PayoutGate] " + s);
    }
}
