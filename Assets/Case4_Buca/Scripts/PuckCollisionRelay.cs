using UnityEngine;

namespace Case4
{
    /// <summary>
    /// Thin physics contact relay attached to the puck body. The launcher owns all feel logic, while
    /// this component exists only so rail/stack events come from Unity's actual collision solver.
    /// No waypoint, velocity-angle heuristic or trigger proxy is involved.
    ///
    /// <para><b>owner must survive a domain reload.</b> It used to be <c>[System.NonSerialized]</c>,
    /// which meant a mid-playmode assembly reload (Unity recompiles scripts while the capture harness
    /// is in play mode) silently blanked it. The relay kept receiving OnCollisionEnter, the puck kept
    /// ricocheting for real, and every one of those contacts was dropped on the floor: the launcher
    /// counted 0 rail bounces, never saw the stack, never armed the payout, and the flight ran to its
    /// 2.40 s timeout instead of ending at 1.13 s. Nothing in the log said so, because a null owner is
    /// not an error. The field is now serialized, and <see cref="Owner"/> re-resolves it if it is ever
    /// lost anyway.</para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PuckCollisionRelay : MonoBehaviour
    {
        [Tooltip("The launcher these contacts are reported to. Wired by PuckLauncher; serialized so it survives a domain reload.")]
        public PuckLauncher owner;

        /// <summary>The launcher, re-resolved from the scene if the reference was lost.</summary>
        public PuckLauncher Owner
        {
            get
            {
                if (owner != null) return owner;
                owner = GetComponentInParent<PuckLauncher>();
                if (owner == null) owner = FindFirstObjectByType<PuckLauncher>(FindObjectsInactive.Include);
                if (owner != null)
                    Debug.Log("[Case4] RELAY_REBOUND owner recovered on " + name + "; contacts were being dropped");
                return owner;
            }
        }

        void OnCollisionEnter(Collision collision)
        {
            PuckLauncher o = Owner;
            if (o != null) o.NotifyCollision(collision, true);
        }

        void OnCollisionStay(Collision collision)
        {
            PuckLauncher o = Owner;
            if (o != null) o.NotifyCollision(collision, false);
        }
    }
}
