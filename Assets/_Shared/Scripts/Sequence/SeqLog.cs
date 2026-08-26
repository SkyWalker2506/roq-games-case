using UnityEngine;

namespace Shared.Sequencing
{
    /// <summary>
    /// Informational logging for the four case scenes, behind one switch.
    ///
    /// The scenes narrate themselves - what was tapped, what landed, how long a run took, which
    /// sorting order a sheet claimed. That narration is how most of the bugs in these cases were
    /// found, so none of it is deleted. But a reviewer opening a scene and pressing Play should see
    /// an empty console, not two hundred lines, and a genuine LogError should not have to compete
    /// with them for attention.
    ///
    /// So: Debug.LogError and Debug.LogWarning are left alone - a real failure still speaks. Only
    /// the running commentary comes through here, and it is off.
    ///
    /// Turn it on from anywhere with Shared.Sequencing.SeqLog.Enabled = true, or flip the default.
    /// </summary>
    public static class SeqLog
    {
        /// <summary>Whether the running commentary reaches the console. Off for play, on to diagnose.</summary>
        public static bool Enabled = false;

        /// <summary>One line of commentary. Ignored unless <see cref="Enabled"/>.</summary>
        public static void Info(object message)
        {
            if (Enabled) Debug.Log(message);
        }

        /// <summary>Commentary tied to an object, so clicking the line selects it.</summary>
        public static void Info(object message, Object context)
        {
            if (Enabled) Debug.Log(message, context);
        }
    }
}
