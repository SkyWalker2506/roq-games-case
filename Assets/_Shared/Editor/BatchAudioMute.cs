#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace Shared.EditorTools
{
    /// <summary>
    /// Silences everything that runs headless. The capture harness and the input gates all enter Play
    /// Mode, and the procedural SFX played out of the machine's speakers every time a gate ran in the
    /// background. Only batchmode is muted: an Editor session the user opened themselves keeps its audio.
    /// </summary>
    [InitializeOnLoad]
    public static class BatchAudioMute
    {
        static BatchAudioMute()
        {
            if (!Application.isBatchMode) return;
            AudioListener.volume = 0f;
            AudioListener.pause = true;
            EditorApplication.playModeStateChanged += OnPlayModeChanged;
        }

        static void OnPlayModeChanged(PlayModeStateChange change)
        {
            // Entering Play Mode rebuilds the listener, so re-assert on every transition.
            AudioListener.volume = 0f;
            AudioListener.pause = true;
        }
    }
}
#endif
