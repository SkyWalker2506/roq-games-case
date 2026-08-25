using System.Collections;
using UnityEngine;
using Shared.Audio;
using Shared.Juice;
using Shared.Sequencing;
using Shared.Tweening;

namespace Case1
{
    /// <summary>
    /// Case 1 interaction: tap a deck shape, watch THAT shape arc into the drum cell that matches it,
    /// drop in, and see the drum react. The interaction itself is the VIDEO_MEASURED eighteen-frame
    /// window from tap to settled (f049..f067 at 45 fps); the sparkle is allowed to decay after input is
    /// released instead of extending that interaction window.
    ///
    /// The sequence never starts by itself. It starts when the player picks a shape, and when it ends the
    /// scene is immediately ready for the next pick: the shapes that have not flown yet are still live and
    /// the cells that were filled stay filled.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class Case1Director : SequenceDirector
    {
        [Header("Scene wiring (filled in by Case1SceneSetup)")]
        public ShapeArcFlight flight;
        public DrumSlotReaction drum;
        public DeckReflow deck;

        [Header("Timing, seconds")]
        // A coroutine completes on the first 180 Hz simulation tick after its authored interval. Each
        // phase therefore stores one capture tick less than its visible 45 fps span; the measured report
        // still lands on exactly 12 + 36 + 24 ticks = 18 / 45 = 0.400 s.
        public float anticipationDuration = 0.10f;
        public float arcDuration = 0.44f;
        public float approachDuration = 0f;
        public float sinkDuration = 0.16f;
        // VIDEO_MEASURED, Fit The Shape.mp4 at 45 fps, first interaction:
        //   f049 1.09 s  tap indicator on the red hexagon
        //   f052 1.16 s  hexagon has left the tray; column 2 is ALREADY compacting
        //   f058 1.29 s  hexagon reaches the active band
        //   f061 1.36 s  it is over the target cell
        //   f067 1.49 s  the cell is filled and settled
        //   sparkle tail measured on the second interaction: f083 -> f095, about 0.30 s
        // Tap to settled is exactly 18 / 45 = 0.40 s. These legacy fields remain serialisable so older
        // staged scenes load safely, but neither blocks the interaction; CaptureTailDuration films VFX.
        public float sparkleWindow = 0f;
        public float settleWindow = 0f;

        // Legacy serialised/API fields. The reference has neither effect, so these values are intentionally
        // never read; keeping the fields lets already-staged scenes and external tooling load without churn.
        [HideInInspector] public float hitstopSeconds = 0f;
        [HideInInspector] public float punchAmplitude = 0f;
        [HideInInspector] public float punchDuration = 0f;

        // VIDEO_MEASURED from the reference clip's audio. Its arrival is one 1.1 s event, not a single
        // hit: a 30 ms thunk, a dip, and then a run of small bright ticks whose envelope peaks sit at
        //   30 | 240 260 350 430 480 540 560 590 620 650 760 800 ms
        // - about eleven of them from 0.24 s out to 0.80 s, decaying. That run IS the ripple; it is the
        // audio of the same wave the drum shows, and it starts on the same beat (the wave's own onset
        // measures +0.13 s and its far edge peaks at +0.38 s). Ours fired ONE tick 65 ms after the hit,
        // so the drum wobbled in silence.
        [Header("Ripple audio (VIDEO_MEASURED: a run of ~11 bright ticks, 0.24 s -> 0.80 s)")]
        public int rippleTicks = 11;
        public float rippleTickSpacing = 0.052f;
        [Tooltip("Silence between the seat and the first tick. MEASURED 30 ms thunk, first tick at 240 ms.")]
        public float rippleTickDelay = 0.210f;

        [Header("Warm-up (the timed sequence must not pay first-frame costs)")]
        [Tooltip("Consecutive frames under warmFrameBudget needed before the scene is declared ready.")]
        public int warmFrames = 5;
        public float warmFrameBudget = 0.03f;
        [Tooltip("Hard cap; FrameStripCapture nudges Play() itself after ~1 s, so this has to finish first.")]
        public float warmTimeout = 1.7f;
        [Tooltip("Log any frame longer than this while the sequence runs, so a stall is never silent.")]
        public float stallReportThreshold = 0.12f;

        float _flightStart;
        float _flightSpan;
        float _entrySpan;
        float _cursor;
        float _lastFrameStamp;
        int _emptiedDeckSlot;

        const float ClockEpsilon = 0.0001f;

        /// <summary>Deck slot the shape currently in flight left behind.</summary>
        public int EmptiedDeckSlot { get { return _emptiedDeckSlot; } }

        /// <summary>True once the warm-up gate has passed and the scene is ready to accept a tap.</summary>
        public bool Ready { get; private set; }

        /// <summary>Name written into the report.</summary>
        public override string SequenceName { get { return "Case1_FitTheShape"; } }

        /// <summary>Case 1 motion is authored on scaled gameplay time, so pausing pauses the interaction.</summary>
        protected override float SequenceClock { get { return Time.time; } }

        /// <summary>Four deterministic simulation ticks per frame of the 45 fps reference.</summary>
        public override int DeterministicCaptureFramerate { get { return 180; } }

        /// <summary>VIDEO_MEASURED sparkle tail (approximately f083..f095 on the second interaction).</summary>
        // The reaction now runs 0.13 s of lead-in + 0.26 s of wave delay + a 0.26 s pulse after the
        // impact, so a 0.30 s tail filmed less than half of it and the capture strip stopped mid-wave.
        // VIDEO_MEASURED: the reference is fully settled 0.67 s after the hit.
        // The ripple is authored to run 3.00 s after the hit, so anything shorter films only part of
        // it - the capture would stop mid-wave and the strip would "prove" an effect that is still
        // moving when the last frame is taken.
        public override float CaptureTailDuration { get { return 3.10f; } }

        // ------------------------------------------------------------------ warm-up

        /// <summary>
        /// Holds the scene back until the player loop is genuinely warm. Measured on this project: the
        /// first frames of a batchmode play session stall for seconds (asset pipeline, shader compiles,
        /// editor indexing). A stall inside the timed sequence smears the captured frame strip, so the
        /// cost is paid here instead.
        ///
        /// It deliberately does NOT call Play(): the scene comes up idle and waits for a tap.
        /// </summary>
        protected override IEnumerator Start()
        {
            AudioService.Prewarm();
            if (drum != null) drum.Warmup();

            yield return null;
            yield return null;

            int stable = 0;
            float deadline = Time.realtimeSinceStartup + warmTimeout;
            while (stable < warmFrames && Time.realtimeSinceStartup < deadline)
            {
                float frameStart = Time.realtimeSinceStartup;
                yield return null;
                stable = (Time.realtimeSinceStartup - frameStart) <= warmFrameBudget ? stable + 1 : 0;
            }

            if (drum != null) drum.ResetAll();
            VFXPool.ReclaimAll();
            Ready = true;
            Debug.Log(string.Format("[Case1] warm-up finished after {0:0.00} s of play, {1} stable frames; " +
                                    "scene is idle and waiting for a tap ({2} playable shapes)",
                Time.realtimeSinceStartup - (deadline - warmTimeout), stable, PlayableCount()));
        }

        int PlayableCount()
        {
            if (flight == null) return 0;
            int n = 0;
            for (int i = 0; i < flight.Count; i++) if (flight.Playable(i)) n++;
            return n;
        }

        void Update()
        {
            if (!IsPlaying) { _lastFrameStamp = Time.realtimeSinceStartup; return; }

            float now = Time.realtimeSinceStartup;
            float frame = now - _lastFrameStamp;
            _lastFrameStamp = now;
            if (frame > stallReportThreshold)
            {
                Debug.LogWarning(string.Format("[Case1] STALL {0:0.000} s frame at sequenceTime {1:0.000} s", frame, SequenceTime));
            }
        }

        // ------------------------------------------------------------------ selection

        /// <summary>
        /// Runs the sequence for the deck shape at <paramref name="shapeIndex"/>. Returns false when that
        /// shape cannot be played (already used, or it has no matching drum cell), in which case nothing
        /// happens at all. The scene is NOT reset first: earlier arrivals stay where the player put them.
        /// </summary>
        public bool PlaySelected(int shapeIndex)
        {
            if (IsPlaying || flight == null) return false;
            if (!flight.Select(shapeIndex)) return false;

            _emptiedDeckSlot = deck != null ? Mathf.Max(0, deck.SlotOf(flight.CurrentShape)) : 0;
            Play();

            if (!IsPlaying)
            {
                Debug.LogWarning("[Case1] PlaySelected: Play() was refused (no input behind the call)");
                return false;
            }
            return true;
        }

        /// <summary>
        /// Handles a direct player tap on a tray piece. If the piece is in the front row and there is an
        /// available matching slot on the live drum row (row 0), it launches into that slot. Otherwise,
        /// it plays an in-place wobble rejection animation without moving.
        /// </summary>
        public bool HandlePieceTap(Transform shape)
        {
            if (shape == null || flight == null || drum == null || deck == null) return false;
            if (IsPlaying) return false;

            // Must be alive on the deck and in the front row
            if (!deck.IsInFrontRow(shape))
            {
                Debug.Log("[Case1Tap] Tap on " + shape.name + " ignored (not in front row)");
                PlayRejectAnimation(shape);
                return false;
            }

            // Identify the shape
            ShapeId shapeId;
            if (!ShapeIds.TryParse(shape.name, out shapeId))
            {
                Debug.LogWarning("[Case1Tap] Could not identify shape of " + shape.name);
                PlayRejectAnimation(shape);
                return false;
            }

            // Find matching available live slot on the active row
            int targetCell = drum.FindAvailableLiveSlot(shapeId);
            if (targetCell < 0)
            {
                Debug.Log("[Case1Tap] No matching available live slot for " + shapeId + "; rejecting with in-place shake");
                PlayRejectAnimation(shape);
                return false;
            }

            // Match found! Setup dynamic flight
            if (!flight.SelectDynamic(shape, targetCell))
            {
                PlayRejectAnimation(shape);
                return false;
            }

            _emptiedDeckSlot = Mathf.Max(0, deck.SlotOf(shape));
            Play();
            return true;
        }

        /// <summary>
        /// USER DIRECTIVE: yoksa üstüne bazsak yerinde bir hareket yapsın ufak ama gitmesin
        /// Plays a small juicy in-place wobble/rejection animation on the tapped piece without moving it from its tray slot.
        /// </summary>
        public void PlayRejectAnimation(Transform shape)
        {
            if (shape == null) return;
            Squash.Cancel(shape);
            Vector3 originPos = shape.position;
            AudioService.Play(SfxId.TapPop, 0.7f);

            Tweener.Float(0f, 1f, 0.22f, p =>
            {
                if (shape == null) return;
                // Subtle horizontal wiggle
                float wiggle = Mathf.Sin(p * Mathf.PI * 4f) * (1f - p) * 0.05f;
                shape.position = originPos + new Vector3(wiggle, 0f, 0f);
            }).SetEase(EaseType.OutQuad)
              .OnComplete(() =>
              {
                  if (shape != null)
                  {
                      shape.position = originPos;
                      if (deck != null) deck.RestoreRestScale(shape);
                  }
              });

            Squash.SquashStretch(shape, SquashAxis.Y, -0.08f, 0.20f, EaseType.OutQuad);
        }

        // ------------------------------------------------------------------ sequence

        protected override IEnumerator RunSequence()
        {
            if (flight == null || drum == null || deck == null)
            {
                Debug.LogError("[Case1] Director is not wired; run Case1SceneSetup.Build.");
                yield break;
            }

            // The capture harness calls Play() straight, with no pick behind it; give it the first
            // still-playable shape so the strip always has something to film.
            if (!flight.Playable(flight.CurrentIndex))
            {
                if (!flight.EnsureSelection())
                {
                    Debug.LogError("[Case1] no playable shape left; nothing to run");
                    yield break;
                }
                _emptiedDeckSlot = Mathf.Max(0, deck.SlotOf(flight.CurrentShape));
            }

            Transform flying = flight.CurrentShape;
            int cell = flight.TargetCell;
            Debug.Log(string.Format("[Case1] RUN_BEGIN shape={0} targetCell={1} ({2}) emptiedSlot={3}",
                flying != null ? flying.name : "<null>", cell, drum.CellName(cell), _emptiedDeckSlot));

            AudioService.Prewarm();

            // One absolute scaled schedule, advanced by whole phase lengths rather than by summing
            // per-frame deltas. During capture Unity advances this clock at an exact 180 Hz.
            _cursor = SequenceClock;

            // ---------------------------------------------------------- 1. anticipation (VIDEO_MEASURED f049..f052)
            BeginStep("anticipation");
            AudioService.Play(SfxId.TapPop, 0.9f);
            Fire(JuiceEvent.Anticipation, "tapped shape " + (flying != null ? flying.name : "?") +
                 " compresses against its slot for " + anticipationDuration.ToString("0.00") + " s before it leaves");
            Fire(JuiceEvent.SquashStretch, "small volume-preserving compression on the tapped shape");
            yield return flight.Anticipate(anticipationDuration);
            EndStep();

            // ---------------------------------------------------------- 2. flight (VIDEO_MEASURED f052..f061)
            BeginStep("flight");
            _flightStart = SequenceTime;
            AudioService.Play(SfxId.WhooshArc, 0.5f);

            // VIDEO_MEASURED: at f052 (1.16 s) the hexagon has left the tray and the square behind it is
            // ALREADY on its way up, while the hexagon does not reach the band until f058 (1.29 s). The
            // compaction and the flight overlap. Ours ran the reflow only after the flight had finished,
            // which reads as "first A, then B" instead of one connected movement.
            deck.MarkGone(flying);
            int moving = deck.CountMoving(_emptiedDeckSlot);
            deck.Reflow(_emptiedDeckSlot);
            Debug.Log("[Case1] PROOF deck reflow: " + moving + " shape(s) compact into slot " + _emptiedDeckSlot +
                      " while the hero is still in flight");

            yield return flight.FlyArc(arcDuration);
            Fire(JuiceEvent.Overshoot, string.Format("direct OutCubic transfer over {0:0.00} s across {1:0.00} world units; no visible path overshoot",
                arcDuration, flight.FlightDistance));
            _flightSpan = SequenceTime - _flightStart;
            EndStep();

            // ---------------------------------------------------------- 3. entry + sparkle (VIDEO_MEASURED f061..f067)
            BeginStep("entry-sparkle");
            float entryStart = SequenceTime;

            yield return flight.Sink(sinkDuration);
            flight.MarkConsumed();

            // VIDEO_MEASURED: the target is filled and settled at f067. Bloom and wheel response start
            // there, then decay through CaptureTailDuration without extending the interaction/input lock.
            AudioService.PlayLayered(SfxId.ArrivalImpact, SfxId.AttachPop, 0.055f);
            drum.Impact(cell, flight.ShapeColor);
            StartCoroutine(RippleTicks());

            Fire(JuiceEvent.ImpactVFX, "target-cell bloom + restrained horizontal wheel ripple");
            Fire(JuiceEvent.SquashStretch, "target cell gives one compact compression/rebound");

            Debug.Log(string.Format("[Case1] PROOF reaction kept local: {0} immediate neighbours, ripple span {1:0.000} s across {2} cells",
                drum.SpillCount, drum.RippleSpan, drum.CellCount));

            _entrySpan = SequenceTime - entryStart;
            EndStep();

            Debug.Log(string.Format("[Case1] PROOF flight {0:0.00} s + entry {1:0.00} s = {2:0.00} s; total {3:0.00} s",
                _flightSpan, _entrySpan, _flightSpan + _entrySpan, SequenceTime));
            Debug.Log(string.Format("[Case1] RUN_END shape={0} landed in {1}; {2} shape(s) still tappable",
                flying != null ? flying.name : "?", drum.CellName(cell), PlayableCount()));
        }

        /// <summary>
        /// A couple of quiet staggered ticks support the wheel reaction without turning it into a global
        /// explosion; the service shifts pitch and gain per index on its own.
        /// </summary>
        IEnumerator RippleTicks()
        {
            float origin = SequenceClock;
            for (int i = 0; i < rippleTicks; i++)
            {
                float due = origin + rippleTickDelay + rippleTickSpacing * i;
                while (SequenceClock + ClockEpsilon < due) yield return null;
                AudioService.PlayRepeat(SfxId.RippleTick, i, 0.8f);
            }
        }

        // ------------------------------------------------------------------ reset

        /// <summary>
        /// Full reset back to the untouched scene. Only Replay() (i.e. the capture harness) uses it; a
        /// player tap never resets, because the point of the scene is that each pick sticks.
        /// </summary>
        protected override void ResetState()
        {
            StopAllCoroutines();

            if (flight != null) flight.ResetInstant();
            if (deck != null) deck.ResetInstant();
            if (drum != null) drum.ResetAll();

            VFXPool.ReclaimAll();
        }

        /// <summary>
        /// Pins the end of a phase to the sequence schedule. The cursor moves by the nominal phase length,
        /// never backwards past the present, so a phase that overran shifts what follows rather than
        /// collapsing every later wait into a no-op.
        /// </summary>
        IEnumerator Hold(float phaseLength)
        {
            _cursor += phaseLength;
            if (_cursor < SequenceClock - ClockEpsilon) _cursor = SequenceClock;
            while (SequenceClock + ClockEpsilon < _cursor) yield return null;
        }
    }
}
