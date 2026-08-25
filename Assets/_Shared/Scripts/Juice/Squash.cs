using System.Collections.Generic;
using UnityEngine;
using Shared.Tweening;

namespace Shared.Juice
{
    /// <summary>Axis a squash or stretch is applied along.</summary>
    public enum SquashAxis
    {
        /// <summary>Squash along local X.</summary>
        X,
        /// <summary>Squash along local Y (the usual one for jumps and landings).</summary>
        Y,
        /// <summary>Squash along local Z.</summary>
        Z
    }

    /// <summary>
    /// Volume-preserving squash and stretch. One axis is scaled by (1 + amount) while the other two are
    /// scaled by 1/sqrt(1 + amount), so the shape deforms without visibly gaining or losing mass.
    /// </summary>
    public static class Squash
    {
        struct Entry
        {
            public Vector3 BaseScale;
            public TweenHandle Handle;
        }

        static readonly Dictionary<Transform, Entry> Active = new Dictionary<Transform, Entry>(16);

        /// <summary>
        /// Deforms <paramref name="target"/> along <paramref name="axis"/> by <paramref name="amount"/>
        /// (positive stretches, negative squashes) and springs back to its resting scale over
        /// <paramref name="duration"/> seconds.
        /// </summary>
        public static TweenHandle SquashStretch(Transform target, SquashAxis axis, float amount, float duration,
            EaseType ease = EaseType.OutElastic)
        {
            if (target == null || duration <= 0f) return TweenHandle.None;

            Vector3 baseScale = ResolveBaseScale(target);
            TweenHandle handle = Tweener.Float(amount, 0f, duration, a =>
            {
                if (target == null) return;
                target.localScale = Deform(baseScale, axis, a);
            }).SetEase(ease).OnComplete(() =>
            {
                if (target != null) target.localScale = baseScale; // exact restore, no residual drift
                Active.Remove(target);
            });

            Active[target] = new Entry { BaseScale = baseScale, Handle = handle };
            return handle;
        }

        /// <summary>Convenience squash along Y, the axis most impacts want.</summary>
        public static TweenHandle Bump(Transform target, float amount = 0.25f, float duration = 0.35f)
        {
            return SquashStretch(target, SquashAxis.Y, amount, duration);
        }

        /// <summary>Returns the volume-preserving scale for a resting scale and a deform amount.</summary>
        public static Vector3 Deform(Vector3 baseScale, SquashAxis axis, float amount)
        {
            float along = 1f + amount;
            if (along < 0.01f) along = 0.01f;
            float across = 1f / Mathf.Sqrt(along); // keeps along * across^2 == 1

            switch (axis)
            {
                case SquashAxis.X: return new Vector3(baseScale.x * along, baseScale.y * across, baseScale.z * across);
                case SquashAxis.Z: return new Vector3(baseScale.x * across, baseScale.y * across, baseScale.z * along);
                default: return new Vector3(baseScale.x * across, baseScale.y * along, baseScale.z * across);
            }
        }

        /// <summary>Stops any running deform on <paramref name="target"/> and restores its resting scale.</summary>
        public static void Cancel(Transform target)
        {
            if (target == null) return;
            Entry entry;
            if (!Active.TryGetValue(target, out entry)) return;
            entry.Handle.Cancel();
            target.localScale = entry.BaseScale;
            Active.Remove(target);
        }

        static Vector3 ResolveBaseScale(Transform target)
        {
            // Re-triggering mid-deform must not compound: reuse the resting scale, not the deformed one.
            Entry entry;
            if (Active.TryGetValue(target, out entry))
            {
                entry.Handle.Cancel();
                return entry.BaseScale;
            }
            return target.localScale;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            Active.Clear();
        }
    }
}
