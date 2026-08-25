using System;
using UnityEngine;
using Shared.Juice;
using Shared.Tweening;

namespace Case1
{
    /// <summary>
    /// Slides the shapes still on the deck into the slot that was just emptied. In the reference it starts
    /// as the hero leaves (VIDEO_MEASURED f052) and overlaps the flight, which is why it is fired as a tween
    /// batch rather than awaited.
    ///
    /// Slots are tracked at runtime, not just at setup: the player can send a second and a third shape
    /// away, and each of those has to reflow from wherever the previous one left the deck.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class DeckReflow : MonoBehaviour
    {
        /// <summary>One shape sitting on the deck.</summary>
        [Serializable]
        public sealed class Entry
        {
            /// <summary>The shape transform.</summary>
            public Transform shape;
            /// <summary>Deck slot index it currently occupies.</summary>
            public int slot;

            /// <summary>Local scale for the FRONT row - the piece at its full height.</summary>
            public Vector3 FrontScale = Vector3.one;
            /// <summary>Local scale for a row BEHIND the front one, solved so its SCREEN height is right.</summary>
            public Vector3 BackScale = Vector3.one;
            [NonSerialized] public Vector3 RestLocalPosition;
            [NonSerialized] public Vector3 RestWorldPosition;
            [NonSerialized] public int RestSlot;
            [NonSerialized] public bool Gone;
        }

        [Header("Scene wiring (filled in by Case1SceneSetup)")]
        public Entry[] entries = new Entry[0];

        [Tooltip("Used to work out which of a piece's LOCAL axes reads as height on screen.")]
        public Camera viewCamera;

        [Tooltip("Local X of every deck slot, left to right. Legacy 1-D path; ignored when slotWorld is filled.")]
        public float[] slotX = new float[0];

        [Tooltip("World position of every tray slot, row-major from the top-left. MEASURED from the " +
                 "reference: Fit The Shape's playable pool is the 3x3 tray, not the holder row, and it " +
                 "COMPACTS when a shape leaves - the shapes behind move up into the gap.")]
        public Vector3[] slotWorld = new Vector3[0];

        [Tooltip("Tray columns. MEASURED from the reference: the compaction runs DOWN THE COLUMN, not " +
                 "along the row. Removing (0,2) pulls (1,2) up to (0,2) and (2,2) up to (1,2); the other " +
                 "two columns do not move at all.")]
        public int columns = 3;

        [Header("Feel")]
        // VIDEO_MEASURED f051..f062: three column movers (including the hidden refill) complete in
        // about 0.244 s. 0.16 + two 0.04 staggers lands exactly on that beat at 45 fps tolerance.
        public float slideDuration = 0.16f;
        public float stagger = 0.040f;
        public float bump = 0.09f;

        [Tooltip("Screen height of a tray piece that is NOT on the front row, as a fraction of the front " +
                 "row's. The front three stand at full height and the rows behind are lower, so the " +
                 "queue reads as depth; a piece stands back up as it advances.")]
        // VIDEO_MEASURED from the reference's own tray: front row 155/148/149 px, back rows 104/113/110.
        // That is 0.72, not the 0.25 first asked for - at 0.25 the pieces collapse into slivers and a
        // star stops being recognisable as a star, which defeats the point of showing what is queued.
        public float backRowFlatten = 0.72f;

        [Tooltip("How long a piece takes to stand up when it reaches the front row.")]
        public float standUpDuration = 0.16f;

        void Awake()
        {
            CaptureRestState();
            ApplyRowScalesInstant();
        }

        /// <summary>Records where each shape rests so a replay can restore it.</summary>
        public void CaptureRestState()
        {
            int maxSlot = 0;
            for (int i = 0; i < entries.Length; i++)
            {
                if (entries[i] != null && entries[i].shape != null)
                {
                    entries[i].RestLocalPosition = entries[i].shape.localPosition;
                    entries[i].RestWorldPosition = entries[i].shape.position;
                    entries[i].RestSlot = entries[i].slot;
                    if (entries[i].slot > maxSlot) maxSlot = entries[i].slot;
                }
            }

            // Keep slotWorld dynamically synced to the authored rest positions of the shapes
            if (maxSlot >= 0 && entries.Length > 0)
            {
                if (slotWorld == null || slotWorld.Length <= maxSlot)
                {
                    slotWorld = new Vector3[maxSlot + 1];
                }
                for (int i = 0; i < entries.Length; i++)
                {
                    if (entries[i] != null && entries[i].shape != null && entries[i].slot < slotWorld.Length)
                    {
                        slotWorld[entries[i].slot] = entries[i].RestWorldPosition;
                    }
                }
            }
        }

        /// <summary>
        /// Lays a piece flat while it waits behind the front row, and stands it back up when it reaches
        /// the front. Only the vertical axis changes: the footprint stays put, so the tray grid does not
        /// shift as pieces advance.
        /// </summary>
        /// <returns>True when this piece changes row height and the scale has been taken over here.</returns>
        bool ApplyRowScale(Entry e, float delay)
        {
            if (e == null || e.shape == null) return false;

            Transform t = e.shape;
            Vector3 from = t.localScale;
            bool front = columns > 0 && e.slot < columns;
            Vector3 to = ScaleForRow(e, front);
            if ((from - to).sqrMagnitude < 1e-8f) return false;

            // ONE tween owns localScale here, and it carries the squash itself.
            //
            // Previously the stand-up ran as its own tween while Squash.SquashStretch ran alongside it
            // on the same transform. Squash captures the CURRENT scale as the resting scale - the back
            // row's - writes it every frame, and stamps it back exactly in OnComplete. Both were fired
            // at the same delay with the same duration, so the squash always had the last word and the
            // piece arrived at the front row still flat: the growth was computed and then overwritten.
            Squash.Cancel(t);
            float amount = bump;
            Tweener.Float(0f, 1f, standUpDuration, p =>
            {
                if (t == null) return;
                Vector3 s = Vector3.LerpUnclamped(from, to, p);          // unclamped: OutBack overshoots, and
                                                                          // the overshoot is the pop we want
                t.localScale = Squash.Deform(s, SquashAxis.X, amount * (1f - Mathf.Clamp01(p)));
            }).SetEase(EaseType.OutBack, 1.1f)
              .SetDelay(delay)
              .OnComplete(() => { if (t != null) t.localScale = to; });   // exact landing, no residual drift
            return true;
        }

        /// <summary>Sets every piece's height for the row it is on, with no tween. Used at build and reset.</summary>
        public void ApplyRowScalesInstant()
        {
            int front = 0, back = 0;
            for (int i = 0; i < entries.Length; i++)
            {
                Entry e = entries[i];
                if (e == null || e.shape == null) continue;
                bool isFront = columns > 0 && e.slot < columns;
                e.shape.localScale = ScaleForRow(e, isFront);
                if (isFront) front++; else back++;
            }
            Debug.Log("[Case1Reflow] ROW_SCALES entries=" + entries.Length + " front=" + front +
                      " back=" + back + " flatten=" + backRowFlatten.ToString("0.00") +
                      " columns=" + columns);
        }

        /// <summary>
        /// Full-height scale for a front-row piece, or the flattened one for a piece waiting behind.
        ///
        /// The flatten is applied to the local axis that actually reads as HEIGHT ON SCREEN, which is
        /// not necessarily local Y. These prefabs carry their face on the local X-Z plane with local +Y
        /// pointing out of it, so scaling Y squeezed them along the camera's view direction and did
        /// nothing visible at all - the log said the flatten had been applied and the tray looked
        /// untouched. The axis is chosen by projecting each of the piece's world axes onto the camera's
        /// up vector, so the same code works whatever pose the prefab is authored in.
        /// </summary>
        /// <summary>
        /// The row's scale, SOLVED AT BUILD TIME against the screen rather than derived from a factor.
        ///
        /// Multiplying the back rows by 0.72 did not read: the tray's back rows sit NEARER the camera,
        /// so perspective enlarges them by about as much as the factor shrinks them, and the measured
        /// heights came out 109/128/110 for the front row against 115/111/90 for the back - no
        /// distinction at all. The builder measures each row's projected height instead and solves the
        /// scale that puts the back rows at backRowFlatten of the FRONT row ON SCREEN, which is what
        /// the eye actually compares.
        /// </summary>
        Vector3 ScaleForRow(Entry e, bool front)
        {
            return front ? e.FrontScale : e.BackScale;
        }

        /// <summary>Deck slot the given shape currently sits on, or -1 if it is not a deck shape.</summary>
        public int SlotOf(Transform shape)
        {
            for (int i = 0; i < entries.Length; i++)
            {
                if (entries[i] != null && !entries[i].Gone && entries[i].shape == shape) return entries[i].slot;
            }
            return -1;
        }

        /// <summary>True when the shape is alive and sits on the front row (slot < columns).</summary>
        public bool IsInFrontRow(Transform shape)
        {
            int sl = SlotOf(shape);
            return sl >= 0 && sl < columns;
        }

        /// <summary>Restores the exact front/back scale for the shape based on its current row.</summary>
        public void RestoreRestScale(Transform shape)
        {
            for (int i = 0; i < entries.Length; i++)
            {
                if (entries[i] != null && entries[i].shape == shape)
                {
                    bool isFront = columns > 0 && entries[i].slot < columns;
                    shape.localScale = ScaleForRow(entries[i], isFront);
                    return;
                }
            }
        }

        /// <summary>Takes <paramref name="shape"/> off the deck; it will never slide or be counted again.</summary>
        public void MarkGone(Transform shape)
        {
            for (int i = 0; i < entries.Length; i++)
            {
                if (entries[i] != null && entries[i].shape == shape) entries[i].Gone = true;
            }
        }

        /// <summary>Number of shapes that would slide if the shape in <paramref name="emptiedSlot"/> left.</summary>
        public int CountMoving(int emptiedSlot)
        {
            int n = 0;
            bool worldPath = slotWorld.Length > 0;
            for (int i = 0; i < entries.Length; i++)
            {
                Entry e = entries[i];
                if (e == null || e.shape == null || e.Gone || e.slot <= emptiedSlot) continue;
                if (worldPath && columns > 1 && e.slot % columns != emptiedSlot % columns) continue;
                n++;
            }
            return n;
        }

        /// <summary>
        /// Slides every eligible shape after <paramref name="emptiedSlot"/> into the preceding row/slot and
        /// records the new slot so the next reflow starts from the truth.
        /// </summary>
        public void Reflow(int emptiedSlot)
        {
            int order = 0;
            bool worldPath = slotWorld.Length > 0;
            if (!worldPath && slotX.Length == 0) return;
            int slotCount = worldPath ? slotWorld.Length : slotX.Length;

            for (int i = 0; i < entries.Length; i++)
            {
                Entry e = entries[i];
                if (e == null || e.shape == null || e.Gone || e.slot <= emptiedSlot) continue;

                // Column compaction: only the pieces UNDER the gap, in the same column, move up.
                if (worldPath && columns > 1)
                {
                    if (e.slot % columns != emptiedSlot % columns) continue;
                }
                int destination = worldPath && columns > 1
                    ? Mathf.Clamp(e.slot - columns, 0, slotCount - 1)
                    : Mathf.Clamp(e.slot - 1, 0, slotCount - 1);

                Transform t = e.shape;
                float delay = stagger * order;

                if (worldPath)
                {
                    // Column compaction: the two pieces below the gap move up one tray row.
                    Vector3 fromW = t.position;
                    Vector3 toW = slotWorld[destination];
                    Tweener.Vector3(fromW, toW, slideDuration, v => { if (t != null) t.position = v; })
                           .SetEase(EaseType.OutBack, 1.1f)
                           .SetDelay(delay);
                    e.slot = destination;
                    // Only squash separately when the row height does NOT change; otherwise the stand-up
                    // tween carries the squash, because two tweens on one localScale means the loser is
                    // silently erased.
                    if (!ApplyRowScale(e, delay))
                    {
                        Tweener.Delay(delay, () => Squash.SquashStretch(t, SquashAxis.X, bump, slideDuration, EaseType.OutElastic));
                    }
                    order++;
                    continue;
                }

                Vector3 from = t.localPosition;
                Vector3 to = new Vector3(slotX[destination], from.y, from.z);

                Tweener.Vector3(from, to, slideDuration, v => { if (t != null) t.localPosition = v; })
                       .SetEase(EaseType.OutBack, 1.1f)
                       .SetDelay(delay);

                Tweener.Delay(delay, () => Squash.SquashStretch(t, SquashAxis.X, bump, slideDuration, EaseType.OutElastic));

                e.slot = destination;
                order++;
            }
        }

        /// <summary>Puts every deck shape back on its original slot and undoes every "gone" mark.</summary>
        public void ResetInstant()
        {
            for (int i = 0; i < entries.Length; i++)
            {
                Entry e = entries[i];
                if (e == null || e.shape == null) continue;
                Squash.Cancel(e.shape);
                if (slotWorld.Length > 0) e.shape.position = e.RestWorldPosition;
                else e.shape.localPosition = e.RestLocalPosition;
                e.slot = e.RestSlot;
                e.Gone = false;
            }
            // Heights go back with the positions, or a replay would start with the back rows already
            // standing up from the previous run.
            ApplyRowScalesInstant();
        }
    }
}
