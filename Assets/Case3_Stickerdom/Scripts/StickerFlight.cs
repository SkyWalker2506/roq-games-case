using UnityEngine;
using Shared.Juice;

namespace Case3
{
    /// <summary>
    /// The path a peeled sticker travels on and the sparkle trail it leaves behind.
    ///
    /// The route is a quadratic Bezier bulged sideways, so the sticker swings out and comes back in
    /// rather than sliding along a ruler line; the reference flight is short (0.35 s) and arcs. Trail
    /// emission is rate limited on the caller's clock, so the dotted look does not change with frame
    /// rate.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class StickerFlight : MonoBehaviour
    {
        [Header("Path")]
        [Tooltip("How far the arc bulges sideways off the straight line, in world units.")]
        public float arcBulge = 0.58f;

        [Tooltip("Extra lift added at the top of the arc, in world units.")]
        public float arcLift = 0.26f;

        [Header("Sparkle trail")]
        [Tooltip("VFX/SparklePop prefab; played repeatedly along the path.")]
        public GameObject sparklePrefab;

        [Tooltip("Yellow dot trail, as measured off the reference frames.")]
        public Color sparkleColor = new Color(0.93f, 1f, 0.16f, 1f);

        [Tooltip("Seconds between sparkle emissions.")]
        public float sparkleInterval = 0.045f;

        [Tooltip("Scale applied to each pooled sparkle burst.")]
        public float sparkleScale = 0.42f;

        [Tooltip("Random offset around the emission point, in world units.")]
        public float sparkleScatter = 0.08f;

        /// <summary>
        /// Sorting order every Case 3 particle renderer is forced to.
        ///
        /// It has to sit ABOVE the whole sprite band of Stickerdom.unity, which after
        /// commit c8b92c3 ("give every SpriteRenderer an explicit unique sorting order")
        /// runs 0..602: PageObj 100-121, Empty 200s, Ghost 300s, Shadow 400s, Sticker
        /// 500s, Reward 600s. The previous value here was 100, written by commit 6994fd3
        /// when the top sprite in the scene was still at 50 - correct on the day, and
        /// silently pushed to the BOTTOM of the band by the rebanding a day later. The
        /// trail and the landing burst kept firing (report.json still logs Trail and
        /// ImpactVFX) while every particle drew behind the page art.
        /// If the sprite band is ever re-banded again, this number moves with it.
        /// </summary>
        public const int VfxSortingOrder = 1000;

        float _nextSparkle;
        int _emitted;

        /// <summary>How many sparkle bursts the current run has emitted.</summary>
        public int EmittedCount { get { return _emitted; } }

        /// <summary>Control point of the arc between <paramref name="from"/> and <paramref name="to"/>.</summary>
        public Vector3 ControlPoint(Vector3 from, Vector3 to)
        {
            Vector3 delta = to - from;
            Vector2 dir = new Vector2(delta.x, delta.y);
            if (dir.sqrMagnitude < 0.0001f) dir = Vector2.up;
            dir.Normalize();
            Vector2 perp = new Vector2(-dir.y, dir.x);
            Vector3 mid = (from + to) * 0.5f;
            return mid + new Vector3(perp.x, perp.y, 0f) * arcBulge + Vector3.up * arcLift;
        }

        /// <summary>Position along the arc at <paramref name="t"/> in 0..1.</summary>
        public Vector3 Evaluate(Vector3 from, Vector3 to, float t)
        {
            Vector3 c = ControlPoint(from, to);
            float u = 1f - t;
            return u * u * from + 2f * u * t * c + t * t * to;
        }

        /// <summary>
        /// Emits one sparkle if enough time has passed since the last one. <paramref name="clock"/> is
        /// the caller's absolute sequence time, so the trail keeps the same spacing on any frame rate.
        /// </summary>
        public bool TryEmit(Vector3 worldPosition, float clock)
        {
            if (sparklePrefab == null || clock < _nextSparkle) return false;
            _nextSparkle = clock + Mathf.Max(0.01f, sparkleInterval);

            // Index-derived jitter makes replay/capture stable without changing Unity's global random
            // stream (which other particle systems and presentation code may use).
            float jx = SignedHash(_emitted * 2 + 17) * sparkleScatter;
            float jy = SignedHash(_emitted * 2 + 43) * sparkleScatter;
            Vector3 jitter = new Vector3(jx, jy, -0.45f);
            GameObject go = VFXPool.Play(sparklePrefab, worldPosition + jitter, Quaternion.identity, sparkleScale);
            Tint(go);
            _emitted++;
            return true;
        }

        static float SignedHash(int value)
        {
            unchecked
            {
                uint x = (uint)value;
                x ^= x >> 16;
                x *= 0x7feb352du;
                x ^= x >> 15;
                x *= 0x846ca68bu;
                x ^= x >> 16;
                return (x / 4294967295f) * 2f - 1f;
            }
        }

        /// <summary>Clears the emission clock so the next run starts its trail immediately.</summary>
        public void ResetTrail(float clock)
        {
            _nextSparkle = clock;
            _emitted = 0;
        }

        /// <summary>Recolours a pooled burst to the reference's yellow-green. Runtime instance only, never the asset.</summary>
        void Tint(GameObject go)
        {
            if (go == null) return;
            ParticleSystem[] systems = go.GetComponentsInChildren<ParticleSystem>(true);
            for (int i = 0; i < systems.Length; i++)
            {
                ParticleSystem ps = systems[i];
                ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                ps.useAutoRandomSeed = false;
                ps.randomSeed = (uint)(0xC330u + i * 97u + _emitted * 17u);
                ParticleSystem.MainModule main = systems[i].main;
                main.startColor = sparkleColor;
                ParticleSystemRenderer psr = systems[i].GetComponent<ParticleSystemRenderer>();
                if (psr != null)
                {
                    psr.sortingOrder = VfxSortingOrder;
                }
                ps.Play(true);
            }
        }
    }
}
