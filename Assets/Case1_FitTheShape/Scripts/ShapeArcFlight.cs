using System;
using System.Collections;
using UnityEngine;
using Shared.Juice;
using Shared.Tweening;

namespace Case1
{
    /// <summary>
    /// Moves ONE deck shape - the one the player tapped - from its slot into the drum cell that matches
    /// it: the deck dip, the curved flight (which overshoots the hole and settles back), the short align
    /// above the mouth, and the sink through the hole.
    ///
    /// Every deck shape is registered as an <see cref="Entry"/> with its own matched target cell, so the
    /// selection is real: tap the star and the star flies to the star hole. Nothing here decides *what*
    /// is tapped; <see cref="ShapeTapInput"/> does that and calls <see cref="Select"/>.
    ///
    /// All motion runs on scaled gameplay time, the same clock as <see cref="Case1Director"/>. The capture
    /// harness fixes that clock at 180 Hz; normal play still respects pause and time-scale changes.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ShapeArcFlight : MonoBehaviour
    {
        /// <summary>One tappable deck shape and the drum cell it belongs in.</summary>
        [Serializable]
        public sealed class Entry
        {
            /// <summary>The deck shape transform.</summary>
            public Transform shape;
            /// <summary>Renderer of the shape body, used for its colour and its on-screen size.</summary>
            public Renderer shapeRenderer;
            /// <summary>Index into <see cref="DrumSlotReaction.cells"/> this shape matches. -1 = unmatched, not tappable.</summary>
            public int targetCell = -1;
            /// <summary>Why the match was made; written into the scene dump and the selection gate log.</summary>
            public string matchNote = "";

            [NonSerialized] public Transform RestParent;
            [NonSerialized] public Vector3 RestLocalPosition;
            [NonSerialized] public Quaternion RestLocalRotation;
            [NonSerialized] public Vector3 RestLocalScale;
            [NonSerialized] public Renderer[] Renderers;
            [NonSerialized] public Color Colour;
            [NonSerialized] public float GrowFactor;
            [NonSerialized] public bool Consumed;
            [NonSerialized] public bool Captured;
        }

        [Header("Scene wiring (filled in by Case1SceneSetup)")]
        public Entry[] entries = new Entry[0];
        public DrumSlotReaction drum;
        public Camera viewCamera;
        public Material trailMaterial;

        [Header("Arc")]
        [Tooltip("Screen-space bulge of the arc, in world units along the camera's up axis.")]
        public float arcHeight = 0.42f;
        [Tooltip("OutBack overshoot on the flight path; the shape passes the hole and settles back.")]
        public float pathOvershoot = 0.0f;
        [Tooltip("How far above the cell face the shape waits before it drops in, as a fraction of the cell size.")]
        public float mouthOffsetFactor = 0.20f;
        [Tooltip("Target width of the shape at the hole, as a fraction of the cell size.")]
        public float arrivalWidthFactor = 0.82f;

        [Header("Entry")]
        public float sinkDepthFactor = 0.52f;
        // The reference never shows the piece hanging over the cell and bobbing; travel and entry
        // are one continuous movement. Kept as a field so it can be A/B'd, but off by default.
        public float hoverBobFactor = 0f;

        [Header("Trail")]
        // The reference hero leaves no trail behind it. Readability comes from silhouette, scale
        // and its shadow, not from a streak.
        public float trailTime = 0f;
        public float trailWidth = 0f;

        TrailRenderer _trail;
        int _current = -1;

        const float ClockEpsilon = 0.0001f;

        /// <summary>Number of registered deck shapes.</summary>
        public int Count { get { return entries != null ? entries.Length : 0; } }

        /// <summary>Index of the shape currently selected, or -1.</summary>
        public int CurrentIndex { get { return _current; } }

        /// <summary>The selected entry, or null.</summary>
        public Entry Current { get { return (_current >= 0 && _current < Count) ? entries[_current] : null; } }

        /// <summary>Transform of the shape currently selected, or null. This is the object that flies.</summary>
        public Transform CurrentShape { get { Entry e = Current; return e != null ? e.shape : null; } }

        /// <summary>Cell index the selected shape is flying to, or -1.</summary>
        public int TargetCell { get { Entry e = Current; return e != null ? e.targetCell : -1; } }

        /// <summary>Colour of the selected shape, used to tint the cell flash and the trail.</summary>
        public Color ShapeColor { get { Entry e = Current; return e != null ? e.Colour : Color.white; } }

        /// <summary>Straight-line world distance the shape covers, for the report detail strings.</summary>
        public float FlightDistance { get; private set; }

        void Awake()
        {
            CaptureRestState();
            BuildTrail();
        }

        /// <summary>Records every shape's untouched pose so a reset can restore it exactly.</summary>
        public void CaptureRestState()
        {
            for (int i = 0; i < Count; i++)
            {
                Entry e = entries[i];
                if (e == null || e.shape == null || e.Captured) continue;

                e.RestParent = e.shape.parent;
                e.RestLocalPosition = e.shape.localPosition;
                e.RestLocalRotation = e.shape.localRotation;
                e.RestLocalScale = e.shape.localScale;
                e.Renderers = e.shape.GetComponentsInChildren<Renderer>(true);
                if (e.shapeRenderer == null) e.shapeRenderer = e.shape.GetComponent<Renderer>();
                e.Colour = ReadColour(e.shapeRenderer);
                e.GrowFactor = 1f;
                e.Captured = true;
            }
        }

        // ------------------------------------------------------------------ selection

        /// <summary>Index of the entry holding <paramref name="shape"/>, or -1.</summary>
        public int IndexOf(Transform shape)
        {
            for (int i = 0; i < Count; i++)
            {
                if (entries[i] != null && entries[i].shape == shape) return i;
            }
            return -1;
        }

        /// <summary>True when the shape can still be tapped: it exists, has a matched cell and has not flown yet.</summary>
        public bool Playable(int index)
        {
            if (index < 0 || index >= Count) return false;
            Entry e = entries[index];
            // A cell that is already filled makes every remaining piece aimed at it unplayable. Two
            // copies of a shape both point at the one recess that fits them, and the second must not
            // fly into a hole that has just closed.
            if (e == null || e.shape == null || e.Consumed || e.targetCell < 0) return false;
            return drum == null || !drum.IsFilled(e.targetCell);
        }

        /// <summary>
        /// Makes <paramref name="index"/> the shape that flies on the next run. Returns false (and changes
        /// nothing) when that shape has already been used or has no matching cell - a shape without a
        /// target is deliberately left untappable rather than sent to some arbitrary hole.
        /// </summary>
        public bool Select(int index)
        {
            if (!Playable(index))
            {
                Shared.Sequencing.SeqLog.Info("[Case1Flight] SELECT_REFUSED index=" + index +
                          " reason=" + (index >= 0 && index < Count && entries[index] != null
                                        ? (entries[index].Consumed ? "already-used" : "no-matching-cell")
                                        : "out-of-range"));
                return false;
            }

            _current = index;
            Entry e = entries[index];
            ComputeGrowFactor(e);
            AttachTrail(e.shape);

            Shared.Sequencing.SeqLog.Info(string.Format("[Case1Flight] SELECTED shape={0} -> cell={1} ({2}) colour={3}",
                e.shape.name, e.targetCell,
                drum != null && e.targetCell >= 0 && e.targetCell < drum.CellCount ? drum.CellName(e.targetCell) : "?",
                e.matchNote));
            return true;
        }

        /// <summary>
        /// Selects a shape transform dynamically with a specific target cell.
        /// </summary>
        public bool SelectDynamic(Transform shape, int targetCell)
        {
            if (shape == null || targetCell < 0) return false;
            int idx = IndexOf(shape);
            if (idx < 0)
            {
                var list = new System.Collections.Generic.List<Entry>(entries != null ? entries : new Entry[0]);
                Entry newEntry = new Entry
                {
                    shape = shape,
                    shapeRenderer = shape.GetComponent<Renderer>(),
                    targetCell = targetCell,
                    matchNote = "dynamic tap",
                    RestParent = shape.parent,
                    RestLocalPosition = shape.localPosition,
                    RestLocalRotation = shape.localRotation,
                    RestLocalScale = shape.localScale,
                    Renderers = shape.GetComponentsInChildren<Renderer>(true),
                    Colour = ReadColour(shape.GetComponent<Renderer>()),
                    GrowFactor = 1f,
                    Captured = true
                };
                list.Add(newEntry);
                entries = list.ToArray();
                idx = entries.Length - 1;
            }
            else
            {
                entries[idx].targetCell = targetCell;
                entries[idx].Consumed = false;
            }

            _current = idx;
            Entry e = entries[idx];
            ComputeGrowFactor(e);
            AttachTrail(e.shape);

            Shared.Sequencing.SeqLog.Info(string.Format("[Case1Flight] SELECTED_DYNAMIC shape={0} -> cell={1} ({2})",
                e.shape.name, e.targetCell,
                drum != null && e.targetCell >= 0 && e.targetCell < drum.CellCount ? drum.CellName(e.targetCell) : "?"));
            return true;
        }

        /// <summary>Picks the first still-playable shape when nothing has been selected (capture harness path).</summary>
        public bool EnsureSelection()
        {
            if (Playable(_current)) return true;
            for (int i = 0; i < Count; i++)
            {
                if (Select(i)) return true;
            }
            return false;
        }

        /// <summary>Marks the selected shape as spent, so it can never be tapped or reflowed again.</summary>
        public void MarkConsumed()
        {
            Entry e = Current;
            if (e != null) e.Consumed = true;
        }

        // ------------------------------------------------------------------ phases

        /// <summary>Deck dip: the shape compresses against its slot and springs back before it leaves.</summary>
        public IEnumerator Anticipate(float duration)
        {
            Entry e = Current;
            if (e == null) yield break;
            Squash.SquashStretch(e.shape, SquashAxis.Y, -0.10f, duration * 1.25f, EaseType.OutQuad);
            yield return Wait(duration);
        }

        /// <summary>
        /// Curved flight from the deck to just above the matched hole. The path parameter is eased with
        /// OutBack so the shape genuinely passes the hole and settles back onto it inside the same window.
        /// </summary>
        public IEnumerator FlyArc(float duration)
        {
            Entry e = Current;
            if (e == null) yield break;

            Transform shape = e.shape;
            Squash.Cancel(shape);

            Vector3 p0 = shape.position;
            Vector3 p2 = MouthPosition();
            FlightDistance = Vector3.Distance(p0, p2);

            Vector3 up = viewCamera != null ? viewCamera.transform.up : Vector3.up;
            Vector3 control = (p0 + p2) * 0.5f + up * arcHeight;

            Quaternion r0 = shape.rotation;
            Quaternion r1 = drum.FaceRotation(e.targetCell);
            Vector3 s0 = e.RestLocalScale;
            Vector3 s1 = s0 * e.GrowFactor;

            if (_trail != null) { _trail.Clear(); _trail.emitting = true; }

            // Absolute scaled clock: summing per-frame deltas drifts badly at batchmode frame rates.
            float start = Time.time;
            float span = Mathf.Max(0.0001f, duration);
            while (Time.time - start + ClockEpsilon < span)
            {
                float u = Ease.Evaluate(EaseType.OutCubic, Mathf.Clamp01((Time.time - start) / span));
                shape.position = Bezier(p0, control, p2, u);
                shape.rotation = Quaternion.SlerpUnclamped(r0, r1, Mathf.Clamp01(u * 1.25f));
                shape.localScale = Vector3.LerpUnclamped(s0, s1, Mathf.Clamp01(u));
                yield return null;
            }

            shape.position = p2;
            shape.rotation = r1;
            shape.localScale = s1;
        }

        /// <summary>Short hold above the mouth while the deck reflows behind it; the trail bleeds out here.</summary>
        public IEnumerator Hover(float duration)
        {
            Entry e = Current;
            if (e == null) yield break;
            if (_trail != null) _trail.emitting = false;

            Transform shape = e.shape;
            Vector3 basePosition = shape.position;
            Vector3 normal = drum.FaceNormal(e.targetCell);
            float bob = drum.CellSize(e.targetCell) * hoverBobFactor;

            float start = Time.time;
            float span = Mathf.Max(0.0001f, duration);
            while (Time.time - start + ClockEpsilon < span)
            {
                float k = Mathf.Sin(Mathf.Clamp01((Time.time - start) / span) * Mathf.PI) * bob;
                shape.position = basePosition + normal * k;
                yield return null;
            }
            shape.position = basePosition;
        }

        /// <summary>Drops through the hole: squeezes on the way in and is hidden once it is fully inside.</summary>
        public IEnumerator Sink(float duration)
        {
            Entry e = Current;
            if (e == null) yield break;

            Transform shape = e.shape;
            Vector3 p0 = shape.position;
            Vector3 p1 = drum.FacePoint(e.targetCell, -sinkDepthFactor);   // well below the cell's front surface

            Vector3 s0 = shape.localScale;
            Vector3 s1 = new Vector3(s0.x * 0.78f, s0.y * 0.58f, s0.z * 0.78f);   // quick compression as the shape clears the lip

            float start = Time.time;
            float span = Mathf.Max(0.0001f, duration);
            while (Time.time - start + ClockEpsilon < span)
            {
                float u = Ease.Evaluate(EaseType.InQuad, Mathf.Clamp01((Time.time - start) / span));
                shape.position = Vector3.LerpUnclamped(p0, p1, u);
                shape.localScale = Vector3.LerpUnclamped(s0, s1, u);
                yield return null;
            }

            SetVisible(e, false);
        }

        // ------------------------------------------------------------------ reset

        /// <summary>Puts every shape back on its deck slot exactly as it started and clears the used marks.</summary>
        public void ResetInstant()
        {
            CaptureRestState();
            if (_trail != null) { _trail.emitting = false; _trail.Clear(); }

            for (int i = 0; i < Count; i++)
            {
                Entry e = entries[i];
                if (e == null || e.shape == null) continue;

                Squash.Cancel(e.shape);
                e.shape.SetParent(e.RestParent, false);
                e.shape.localPosition = e.RestLocalPosition;
                e.shape.localRotation = e.RestLocalRotation;
                e.shape.localScale = e.RestLocalScale;
                e.Consumed = false;
                SetVisible(e, true);
            }

            _current = -1;
        }

        static void SetVisible(Entry e, bool visible)
        {
            if (e == null || e.Renderers == null) return;
            for (int i = 0; i < e.Renderers.Length; i++)
            {
                if (e.Renderers[i] != null) e.Renderers[i].enabled = visible;
            }
        }

        // ------------------------------------------------------------------ helpers

        /// <summary>World point the selected shape aims at: the hole centre, lifted clear of the drum face.</summary>
        public Vector3 MouthPosition()
        {
            Entry e = Current;
            if (e == null || drum == null) return transform.position;
            return drum.FacePoint(e.targetCell, mouthOffsetFactor);
        }

        void ComputeGrowFactor(Entry e)
        {
            e.GrowFactor = 1f;
            if (e.shapeRenderer == null || drum == null || drum.CellCount == 0 || e.targetCell < 0) return;

            Vector3 s = e.shapeRenderer.bounds.size;
            float width = Mathf.Max(0.01f, Mathf.Max(s.x, Mathf.Max(s.y, s.z)));
            float target = drum.CellSize(e.targetCell) * arrivalWidthFactor;
            e.GrowFactor = Mathf.Clamp(target / width, 0.82f, 1.35f);
        }

        void BuildTrail()
        {
            if (_trail != null) return;

            GameObject go = new GameObject("ShapeTrail");
            go.transform.SetParent(transform, false);

            _trail = go.AddComponent<TrailRenderer>();
            _trail.sharedMaterial = trailMaterial;
            _trail.time = trailTime;
            _trail.startWidth = trailWidth;
            _trail.endWidth = 0f;
            _trail.minVertexDistance = 0.02f;
            _trail.numCapVertices = 4;
            _trail.alignment = LineAlignment.View;
            _trail.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _trail.receiveShadows = false;
            _trail.emitting = false;
        }

        /// <summary>Moves the single trail onto the shape that is about to fly and recolours it to match.</summary>
        void AttachTrail(Transform shape)
        {
            BuildTrail();
            if (_trail == null || shape == null) return;

            _trail.emitting = false;
            _trail.Clear();
            _trail.transform.SetParent(shape, false);
            _trail.transform.localPosition = Vector3.zero;

            Color c = ShapeColor;
            Gradient g = new Gradient();
            g.SetKeys(
                new[] { new GradientColorKey(Color.Lerp(c, Color.white, 0.55f), 0f), new GradientColorKey(c, 1f) },
                new[] { new GradientAlphaKey(0.95f, 0f), new GradientAlphaKey(0f, 1f) });
            _trail.colorGradient = g;
        }

        static Vector3 Bezier(Vector3 a, Vector3 b, Vector3 c, float t)
        {
            float inv = 1f - t;
            return inv * inv * a + 2f * inv * t * b + t * t * c;
        }

        static Color ReadColour(Renderer r)
        {
            if (r == null || r.sharedMaterial == null) return Color.white;
            Material m = r.sharedMaterial;
            if (m.HasProperty("_BaseColor")) return m.GetColor("_BaseColor");
            if (m.HasProperty("_Color")) return m.GetColor("_Color");
            return Color.white;
        }

        static IEnumerator Wait(float seconds)
        {
            float end = Time.time + seconds;
            while (Time.time + ClockEpsilon < end) yield return null;
        }
    }
}
