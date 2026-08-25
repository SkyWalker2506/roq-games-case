using UnityEditor;
using UnityEngine;

/// <summary>Batchmode entry points for the case project's build gates.</summary>
public static class CaseBuild
{
    /// <summary>
    /// Compile gate. Unity only reaches an -executeMethod target when the editor assemblies compiled, so
    /// arriving here already proves most of it; this additionally checks the player script compilation
    /// state and exits non-zero if anything failed.
    /// Usage: tools/unity-run.sh -batchmode -quit -executeMethod CaseBuild.CompileCheck -logFile ...
    /// </summary>
    public static void CompileCheck()
    {
        bool failed = EditorUtility.scriptCompilationFailed;

        if (failed)
        {
            Debug.LogError("COMPILE_FAILED: script compilation reported errors.");
            EditorApplication.Exit(1);
            return;
        }

        Debug.Log("COMPILE_OK");
        EditorApplication.Exit(0);
    }
}
