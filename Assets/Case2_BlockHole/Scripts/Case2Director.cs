using System.Collections;
using UnityEngine;
using Shared.Audio;
using Shared.Juice;
using Shared.Sequencing;
using Shared.Tweening;

namespace Case2
{
    /// <summary>
    /// Case 2 interaction: pick the block up, drag it to the matching hole, release it, and let it
    /// break into colour-matched chunks that fall through the opening.
    /// The timing is intentionally brisk: the reference sells the break at the lip and the immediate
    /// downward pull rather than a long explosion/settle tail.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class Case2Director : SequenceDirector
    {
        [Header("Scene wiring (filled in by Case2SceneSetup)")]
        public BlockDragController drag;
        [Tooltip("Every draggable block on the board. All four are playable; the scripted capture run uses `drag`.")]
        public BlockDragController[] drags = new BlockDragController[0];
        public BlockShatterSink sink;
        public HoleGlowHighlight targetHole;
        public HoleGlowHighlight decoyHole;

        [Header("Timeline")]
        public float pickupDuration = 0.14f;
        public float dragToTarget = 0.38f;
        public float preDropHold = 0.03f;
        public float snapDuration = 0.14f;

        /// <summary>
        /// How long the piece takes to fall into its hole. A constant, not the serialised
        /// <see cref="snapDuration"/>, because that field lives on the hand-authored BlockHole
        /// scene at 0.06 s and this branch must not re-serialise it.
        /// <para>
        /// "zaten delige oturma var da cok hizli. onu .5 saniye yapalim hareketi" - the drop
        /// already exists, it is just far too fast. MEASURED on the reference's red L, tracking the
        /// top edge of its silhouette frame by frame at 65 fps: the piece descends continuously
        /// from f880 to f913 and is then motionless from f913 to f924, breaking at f925. That is a
        /// 0.508 s fall followed by a 0.169 s dwell. The owner's "0.5 saniye" is the fall, and the
        /// footage agrees with it to within a frame.
        /// </para>
        /// <para>
        /// The other two numbers he did NOT ask to change turn out to be right already, so they
        /// are left alone: the scene's anticipationDuration is 0.15 s against the reference's
        /// 0.169 s dwell, and dropDepth is 0.9 units against his estimate of "about 1 metre".
        /// </para>
        /// </summary>
        public const float SnapSecondsFixed = 0.50f;

        /// <summary>
        /// Dwell between the piece settling and the break firing. OWNER-DIRECTED, and a deliberate
        /// deviation from the measurement rather than a re-fit of it: the reference's dwell is
        /// 0.169 s and the scene's anticipationDuration of 0.15 s already matched it. The context
        /// changed underneath that number - with the drop now taking 0.50 s instead of 0.06 s, the
        /// total wait before anything breaks is much longer and the pause at the bottom reads as a
        /// stall. "yerlestikten sonra shatter olmasi daha hizli olsun, cok bekliyorsun."
        /// </summary>
        public const float DwellSecondsFixed = 0.035f;
        public float anticipationDuration = 0.95f;
        public float shatterDuration = 0.35f;
        public float sinkDuration = 0.35f;
        public float closeDuration = 0.25f;
        [Tooltip("Seconds the fed hole takes to fade from lit to spent, starting at the top of the " +
                 "sink. Sized so the cavity is dark by 1.95s the way the reference's is.")]
        public float spentFadeDuration = 0.20f;

        [Header("Impact")]
        public float hitstopSeconds = 0.018f;
        public float shakeAmplitude = 0.022f;
        public float shakeDuration = 0.13f;
        public float shakeFrequency = 31f;
        public float punchAmplitude = 0.040f;

        float _t0;              // scaled sequence clock; deterministic under Time.captureFramerate
        float _dropTime;
        int _startFrame;
        bool _userTailRunning;
        int _delivered;

        /// <summary>Name written into the report.</summary>
        public override string SequenceName { get { return "Case2_BlockHole"; } }
        protected override float SequenceClock { get { return Time.time; } }
        public override int DeterministicCaptureFramerate { get { return 130; } }

        void Awake()
        {
            BlockDragController[] all = AllDrags();
            for (int i = 0; i < all.Length; i++)
            {
                all[i].OnUserDrop += HandleUserDrop;
                all[i].OnUserMiss += HandleUserMiss;
            }
        }

        void OnDestroy()
        {
            BlockDragController[] all = AllDrags();
            for (int i = 0; i < all.Length; i++)
            {
                all[i].OnUserDrop -= HandleUserDrop;
                all[i].OnUserMiss -= HandleUserMiss;
            }
        }

        /// <summary>Every wired drag controller, with `drag` folded in when the array does not hold it.</summary>
        public BlockDragController[] AllDrags()
        {
            System.Collections.Generic.List<BlockDragController> list =
                new System.Collections.Generic.List<BlockDragController>();
            if (drags != null)
            {
                for (int i = 0; i < drags.Length; i++)
                    if (drags[i] != null && !list.Contains(drags[i])) list.Add(drags[i]);
            }
            if (drag != null && !list.Contains(drag)) list.Add(drag);
            return list.ToArray();
        }

        /// <summary>True while a player-started drop tail is running.</summary>
        public bool UserTailRunning { get { return _userTailRunning; } }

        /// <summary>How many player drops have been delivered since the scene loaded.</summary>
        public int DeliveredCount { get { return _delivered; } }

        /// <summary>
        /// Pays every first-use cost before the sequence clock starts: the procedural audio bank, the
        /// VFX pools, and one throwaway render of every object this case creates at runtime. Without it
        /// the first play-mode frames stall on shader compilation and the run the capture harness
        /// measures is far longer than the run it samples.
        /// </summary>
        protected override IEnumerator Start()
        {
            yield return Prewarm();
            yield return base.Start();
        }

        IEnumerator Prewarm()
        {
            AudioService.Prewarm();

            if (sink != null)
            {
                if (sink.debrisBurstPrefab != null) VFXPool.Prewarm(sink.debrisBurstPrefab, 2);
                if (sink.impactRingPrefab != null) VFXPool.Prewarm(sink.impactRingPrefab, 2);
                if (sink.dustPuffPrefab != null) VFXPool.Prewarm(sink.dustPuffPrefab, 2);
            }

            BlockDragController[] warm = AllDrags();
            for (int i = 0; i < warm.Length; i++) warm[i].WarmLayers();

            if (drag != null && sink != null && targetHole != null)
            {
                drag.WarmLayers();
                targetHole.SetLit(true);
                targetHole.OpenPit(0.001f);
                sink.Shatter(drag.Block, drag.BlockRenderer, drag.BlockColor, targetHole.SnapPoint, 0.001f,
                             null, drag.ArtBounds, drag.ShapeId);

                yield return null;
                yield return null;

                // Deliberately not ResetState(): that stops every coroutine on this object, including
                // the Start chain this prewarm is running inside.
                sink.Clear();
                for (int i = 0; i < warm.Length; i++)
                {
                    Squash.Cancel(warm[i].Block);
                    warm[i].ResetInstant();
                }
                targetHole.ResetInstant();
                if (decoyHole != null) decoyHole.ResetInstant();
                VFXPool.ReclaimAll();
            }
            yield return null;
        }

        // ------------------------------------------------------------------ scripted sequence

        protected override IEnumerator RunSequence()
        {
            if (drag == null || sink == null || targetHole == null)
            {
                Debug.LogError("[Case2] Director is not wired; run Case2SceneSetup.Build.");
                yield break;
            }

            // One absolute timeline for the whole run. Every phase ends at a fixed offset from the
            // start rather than lasting a fixed duration, so a slow frame is absorbed by the phase it
            // lands in instead of pushing the rest of the sequence back. That keeps the run the frame
            // capture measures the same length as the run it samples.
            _t0 = Time.time;
            _startFrame = Time.frameCount;

            float tPickup = pickupDuration;
            // The reference interaction is about the successful drag/drop. A scripted detour over a
            // wrong hole made the sequence feel like a tutorial and diluted the actual drop beat.
            float tTarget = tPickup + dragToTarget;
            float tDrop = tTarget + preDropHold;

            // ---------------------------------------------------------- 1. pickup
            BeginStep("pickup");
            Fire(JuiceEvent.Anticipation, "block compresses on the board before it is lifted");
            Squash.SquashStretch(drag.Block, SquashAxis.Y, -0.08f, pickupDuration + 0.03f, EaseType.OutQuad);
            AudioService.Play(SfxId.TapPop, 0.5f);

            yield return drag.Pickup(Remaining(tPickup));

            Fire(JuiceEvent.SquashStretch, "lift stretch +0.14 on Y over " + (pickupDuration + 0.12f).ToString("0.00") + " s");
            Squash.SquashStretch(drag.Block, SquashAxis.Y, 0.06f, pickupDuration + 0.08f, EaseType.OutQuad);
            EndStep();

            // ---------------------------------------------------------- 2. drag directly to the valid hole
            BeginStep("drag");
            // Linear, not OutCubic. OutCubic put 99% of the travel in the first 60% of the drag and
            // left the block pixel-identical for the last 0.39 s - 16% of a 2.41 s clip with nothing
            // moving in it. Fitted against the reference's own cross: its centroid runs
            // (543.4,982.9) -> (453.7,1030.5) -> (359.5,1107.3) -> (307.4,1141.7) px at
            // t = 0.00 / 0.40 / 0.75 / 0.95, i.e. 35.7% and 78.4% of the travel done at u = 0.42
            // and 0.79. Linear predicts 42.1% / 78.9%; OutCubic predicts 80.6% / 99.1%. The
            // reference barely decelerates - the landing is sold by the snap's OutBack overshoot
            // that follows, not by the drag petering out.
            //
            // dragToTarget stays at the SCENE's 1.1 (the 0.38 initializer is dead): the drop lands
            // at t=1.254 and every downstream beat is timed off the reference's absolute clock
            // (lit at 1.60, dark at 1.95, sealed by 2.40). Shortening the drag would slide all of
            // them 0.3 s early. The dead air was the curve, not the duration.
            yield return drag.MoveTo(targetHole.SnapPoint, Remaining(tTarget), EaseType.Linear);
            yield return drag.Hover(Remaining(tDrop));

            Shared.Sequencing.SeqLog.Info(string.Format(
                "[Case2] PROOF matching hole '{0}' (shape={1}) lit for block shape={2}: glow {3:0.000} -> neon ON",
                targetHole.name, targetHole.shapeKey, drag.ShapeKey, targetHole.GlowIntensity));
            EndStep();

            // ---------------------------------------------------------- 3-5. drop, shatter, sink
            yield return DropTail(drag, targetHole, true, tDrop);

            Shared.Sequencing.SeqLog.Info(string.Format("[Case2] TIMING run={0:0.000} s over {1} frames ({2:0.0} fps)",
                SequenceTime, Time.frameCount - _startFrame,
                (Time.frameCount - _startFrame) / Mathf.Max(0.001f, SequenceTime)));
        }

        /// <summary>
        /// Everything after the block is released: snap into the hole, break, sink, close.
        /// Shared by the scripted run and by a real user drop; only the scripted run records evidence.
        /// <paramref name="baseOffset"/> keeps the tail on the same absolute timeline as the drag.
        /// </summary>
        IEnumerator DropTail(BlockDragController d, HoleGlowHighlight hole, bool record, float baseOffset)
        {
            float tSnap = baseOffset + SnapSecondsFixed;

            // ---------------------------------------------------------- snap
            if (record)
            {
                BeginStep("snap");
                _dropTime = SequenceTime;
                Fire(JuiceEvent.Overshoot, "OutBack drop into the hole over " + SnapSecondsFixed.ToString("0.00") + " s");
            }
            // The drop is given its FULL length rather than whatever is left on the absolute
            // clock. Remaining() returns zero once the moment has passed, so a sequence that had
            // drifted even slightly late played the fall in a single frame - and at the authored
            // 0.06 s it was about four frames even when perfectly on time, which is why it read as
            // no drop at all followed by a break.
            yield return d.SnapInto(hole, Mathf.Max(SnapSecondsFixed, Remaining(tSnap)));
            if (record) EndStep();

            // EVENT-BASED FROM HERE, not scheduled. Every beat after the drop is measured from the
            // moment the piece ACTUALLY SETTLED, so lengthening the fall cannot make the break
            // fire while the block is still in the air. The old code derived tAntic from an
            // absolute tSnap computed before the drop began; with a 0.06 s fall the difference was
            // invisible, with a 0.5 s one it would break in mid-flight.
            float tAntic = SequenceTime + DwellSecondsFixed;
            float tShatter = tAntic + shatterDuration;
            float tSink = tShatter + sinkDuration;
            float tClose = tSink + closeDuration;

            // ---------------------------------------------------------- anticipation + shatter
            if (record)
            {
                BeginStep("shatter");
                Fire(JuiceEvent.Anticipation, DwellSecondsFixed.ToString("0.00") + " s hold and compress before the break");
            }
            Squash.SquashStretch(d.Block, SquashAxis.Y, -0.055f, DwellSecondsFixed, EaseType.InQuad);
            yield return WaitUntil(tAntic);

            // The neon does NOT hand over to the pit here. It used to - SetLit(false) fired on this
            // frame, which is the real reason the outline vanished the instant the block broke, half
            // a second before any tile rose. Moving Spend twice did not touch it, because Spend only
            // tints the lip and cavity; the halo is _glow, and this was the line that killed it.
            hole.OpenPit(0.16f);

            if (hitstopSeconds > 0f) HitstopService.Stop(hitstopSeconds);
            if (shakeAmplitude > 0f) CameraShakeService.Shake(shakeAmplitude, shakeDuration, shakeFrequency);
            if (punchAmplitude > 0f) CameraShakeService.Punch(Vector3.down, punchAmplitude, 0.10f);
            AudioService.PlayLayered(SfxId.Shatter, SfxId.ArrivalImpact, 0.035f);

            // The shards now have to stay alive right through the close, not vanish a third of the way
            // into the sink: the previous budget emptied the screen with a second of sequence still to run.
            // EVERY shape composites its own footprint out of unit fractures. There is no per-shape
            // branch left here, and that is the point: `ShapeId == Cross ? null : fracturedPrefab`
            // meant the Cross alone ran the tuned composite path while every other shape ran
            // whatever fracture asset its drag happened to carry. Only Drag_2 carries one, so the
            // cyan bar was instantiating a 24-piece authored fracture built for a different mesh -
            // no footprint composition at all - which is why it read as a soft banded pattern
            // instead of chunks. Square and L carried none, fell through to the composite path,
            // and were then laid out by a stale footprint table. Passing null unconditionally
            // sends all four down the one path the reference's break actually looks like.
            //
            // ArtBounds, not CombinedBounds. CombinedBounds is the union of EVERY renderer under
            // the block, inactive fracture shards and VFX pieces included, and it overshoots the
            // drawn art by a different amount per shape: measured, Square 2.190 against an art
            // 2.000, L 3.261 x 2.234 against 3.000 x 2.000, Two 1.153 x 3.372 against 1.000 x
            // 3.000, Cross 3.000 exactly. Sizing the fracture cells off it is one more way for the
            // shape to change the effect. Cross is unaffected, so its tuning is untouched.
            int shards = sink.Shatter(d.Block, d.BlockRenderer, d.BlockColor, hole.SnapPoint,
                                      shatterDuration + sinkDuration + closeDuration - 0.06f,
                                      null, d.ArtBounds, d.ShapeId);
            d.SetVisible(false);

            if (record)
            {
                if (hitstopSeconds > 0f)
                    Fire(JuiceEvent.Hitstop, hitstopSeconds.ToString("0.00") + " s freeze on the break");
                if (shakeAmplitude > 0f)
                    Fire(JuiceEvent.CameraShake, string.Format("{0:0.00} amp / {1:0.00} s / {2:0} Hz", shakeAmplitude, shakeDuration, shakeFrequency));
                if (punchAmplitude > 0f)
                    Fire(JuiceEvent.CameraPunch, "down " + punchAmplitude.ToString("0.00"));
                // Gated like every other impact line above it. This one fired unconditionally, so
                // report.json asserted "a small colour-matched chip burst at the hole lip" at the
                // break while all three of PlayVfx's prefabs were null and nothing could render. A
                // report that certifies an absent effect is worse than no report.
                if (sink != null && sink.HasImpactVfx)
                    Fire(JuiceEvent.ImpactVFX, "small colour-matched chip burst at the hole lip; no opaque smoke cloud");
                Fire(JuiceEvent.SquashStretch, "board reaction: the hole rim pops as the block gives way");
                Fire(JuiceEvent.Deform, shards + " colour-matched chunks replace the block mesh");
            }
            Squash.Bump(hole.transform, 0.07f, 0.14f);

            yield return WaitUntil(tShatter);
            if (record) EndStep();

            // ---------------------------------------------------------- sink + close
            if (record) BeginStep("sink-close");
            // The opening keeps its shape for the WHOLE fall. The owner: "delik kapanip kareler
            // gelmeden once delik sekli kalsin, delik dolduktan sonra kaybolsun."
            //
            // DEVIATION from the reference, recorded rather than fitted. The cross hole there is still
            // fully lit at 1.60 (arm cell mean L=141) and already dark at 1.95 (L=79) while the pit
            // does not begin to close until 2.20, so the extinguish is its own beat early in the sink.
            // Spending it there left a stretch where the hole had stopped reading as a hole and the
            // tiles had not yet arrived - nothing was announcing what the shards were falling into.
            // Spend now runs with the seal, so the opening is a hole until it is filled and stops
            // being one at the moment it stops being one.
            AudioService.Play(SfxId.DebrisFall);
            yield return WaitUntil(tSink);

            if (record)
            {
                float observedFall = SequenceTime - _dropTime;
                float authoredFall = SnapSecondsFixed + DwellSecondsFixed + shatterDuration + sinkDuration;
                Fire(JuiceEvent.Deform, string.Format("shards fully inside the hole; drop -> end of fall = {0:0.00} s", authoredFall));
                Shared.Sequencing.SeqLog.Info(string.Format("[Case2] PROOF drop -> end of readable fall = {0:0.000} s (authored {1:0.000} s)",
                    observedFall, authoredFall));
            }

            // The floor coming back is the last beat of the sequence, so it gets its own motion
            // instead of the pit quietly shrinking: the opening erodes shut and the hole transform
            // pops. Those two lines are what renders. FlashSeal does NOT - it drives _glow, and
            // ApplyGlow disables the glow plate every frame - so do not read the line below as
            // part of the seal's look.
            hole.ClosePit(closeDuration);
            // The board coming back is a MOTION in the reference, not a fade: each cell's tile pops
            // up out of the sealing cavity and settles flush, staggered across the opening. Measured
            // at 2.1 world units peak - see the tile-rise block in HoleGlowHighlight.
            hole.RiseTiles();
            hole.FlashSeal(closeDuration * 0.75f);
            Squash.Bump(hole.transform, 0.05f, closeDuration);
            // No second smoke burst on close. The reference keeps the eye on the coloured fragments
            // falling through the opening; a late white puff masks that motion and reads as a reset VFX.
            AudioService.Play(SfxId.TapPop, 0.22f);
            yield return WaitUntil(tClose);
            // LAST, not first. "hala ilk basta kayboluyor sonra kareler yukseliyor - karelerin hepsi
            // bitmeden kaybolmasin". Both of the things that make the opening visible are released
            // here, together, after every tile has risen and settled: the halo (SetLit) and the
            // lip/cavity tint (Spend). Either one on its own leaves the other still announcing a
            // hole that is no longer there, or - as it did - kills the outline while the board is
            // still on its way back.
            hole.SetLit(false);
            hole.Spend(spentFadeDuration);
            if (record) EndStep();
        }

        // ------------------------------------------------------------------ user drop

        void HandleUserDrop(BlockDragController d, HoleGlowHighlight hole)
        {
            if (IsPlaying || _userTailRunning || hole == null || d == null) return;
            StartCoroutine(UserTail(d, hole));
        }

        void HandleUserMiss(BlockDragController d, HoleGlowHighlight hole)
        {
            Shared.Sequencing.SeqLog.Info(string.Format("[Case2] MISS block={0} over={1} -> sequence NOT started (playing={2}, tail={3})",
                d != null && d.Block != null ? d.Block.name : "<null>",
                hole != null ? hole.name : "<empty board>", IsPlaying, _userTailRunning));
        }

        IEnumerator UserTail(BlockDragController d, HoleGlowHighlight hole)
        {
            _userTailRunning = true;
            _t0 = Time.time;
            yield return DropTail(d, hole, false, 0f);
            yield return Wait(0.20f);

            // The delivered block stays delivered: it is gone, its hole is sealed, and every block still
            // on the board keeps responding to the pointer.
            d.Consume();
            _delivered++;
            Squash.Cancel(d.Block);
            // SealShut, NOT ResetInstant. ResetInstant restores _pitOpen to restingPitOpen (0.96),
            // which re-opened the cavity 0.20 s after ClosePit had just sealed it - so a hole the
            // player had actually fed went back to being a permanent void and the board never read
            // as whole again. The reference does the opposite: a fed opening is gone for the rest
            // of the clip and its cells are ordinary checkerboard tiles (cross fed at 1.5 s, plain
            // board from 2.5 s on; green 4.5 s -> 5.5 s; cyan 10.0 s -> 11.0 s).
            hole.SealShut();
            VFXPool.ReclaimAll();

            BlockDragController[] all = AllDrags();
            int left = 0;
            for (int i = 0; i < all.Length; i++) if (!all[i].Consumed) left++;
            Shared.Sequencing.SeqLog.Info(string.Format("[Case2] DELIVERED block={0} into {1}; blocks still draggable = {2}",
                d.Block != null ? d.Block.name : "<null>", hole.name, left));

            _userTailRunning = false;
        }

        // ------------------------------------------------------------------ reset

        protected override void ResetState()
        {
            StopAllCoroutines();
            _userTailRunning = false;

            if (sink != null) sink.Clear();
            BlockDragController[] all = AllDrags();
            for (int i = 0; i < all.Length; i++)
            {
                Squash.Cancel(all[i].Block);
                all[i].Revive();
                all[i].ResetInstant();
            }
            _delivered = 0;

            if (drag != null)
            {
                for (int i = 0; i < drag.holes.Length; i++)
                {
                    HoleGlowHighlight h = drag.holes[i];
                    if (h == null) continue;
                    Squash.Cancel(h.transform);
                    h.ResetInstant();
                }
            }
            if (targetHole != null) { Squash.Cancel(targetHole.transform); targetHole.ResetInstant(); }
            if (decoyHole != null) { Squash.Cancel(decoyHole.transform); decoyHole.ResetInstant(); }

            VFXPool.ReclaimAll();
            HitstopService.Resume();
            CameraShakeService.StopAll();
        }

        /// <summary>Holds until <paramref name="offset"/> seconds into the run.</summary>
        IEnumerator WaitUntil(float offset)
        {
            while (Time.time < _t0 + offset) yield return null;
        }

        /// <summary>Seconds left until <paramref name="offset"/> into the run; zero if that moment already passed.</summary>
        float Remaining(float offset)
        {
            return Mathf.Max(0f, _t0 + offset - Time.time);
        }

        static IEnumerator Wait(float seconds)
        {
            float end = Time.time + seconds;
            while (Time.time < end) yield return null;
        }
    }
}
