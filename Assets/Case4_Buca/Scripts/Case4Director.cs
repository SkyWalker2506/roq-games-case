using System.Collections;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Shared.Audio;
using Shared.Juice;
using Shared.Sequencing;
using Shared.Tweening;

namespace Case4
{
    /// <summary>
    /// Case 4 interaction. The player pulls the puck back and lets go; the puck is a real rigidbody
    /// that runs up the right lane, ricochets off the arch and reaches the green stack through real
    /// solver contacts. The contact starts a deterministic whole-block cascade so measurement and
    /// capture replay cannot choose different simultaneous-contact branches in the 21-body pile.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class Case4Director : SequenceDirector
    {
        [Header("Scene wiring (filled in by Case4SceneSetup)")]
        public PuckLauncher launcher;
        public GreenBlockShatter shatter;
        public CoinArcStream coins;

        [Header("Arena rim (plain object, wired by Case4SceneSetup)")]
        public NeonWallFlash wall = new NeonWallFlash();

        [Header("Timing, seconds")]
        [Tooltip("Pre-launch idle duration matching reference video (t=0.00 to 0.65s).")]
        public float idleDelay = 0.68f;
        public float anticipationDuration = 0.0f;
        [Tooltip("How long the shot is given before its energy is bled off. NOT a verdict on whether " +
                 "it hit - see the flight step. The reference bank arrives at 1.07-1.13 s, well inside it.")]
        public float flightTimeout = 2.40f;
        [Tooltip("Speed at or below which the puck counts as stopped, in world units per second.")]
        public float flightRestSpeed = 0.35f;
        [Tooltip("How long the puck has to stay under flightRestSpeed before the shot is called a miss.")]
        public float flightRestHold = 0.14f;
        [Tooltip("Absolute ceiling on the flight step, so a puck that somehow never rests cannot hang " +
                 "the sequence. A real shot reaches this only if the physics is broken.")]
        public float flightCeiling = 9.0f;
        [Tooltip("Restores the pre-fix behaviour: the flight ends on flightTimeout whether or not the " +
                 "shot has resolved. Exists ONLY so Case4PayoutGate can prove its invariant goes red " +
                 "against the tree this replaced. Never ship it true.")]
        [HideInInspector] public bool legacyFixedFlightBudget;
        public float impactDuration = 0.07f;
        [Tooltip("Hard cap on the collapse. It really ends when the stack has come to rest.")]
        public float collapseTimeout = 1.85f;
        public float settleDuration = 0.65f;
        [Tooltip("Seconds after the first contact before the puck's energy is bled off. It has to keep moving long enough to go through the stack.")]
        // Miss path only. On a shot that connects, the stack contact has already made the body
        // kinematic (PuckLauncher.BeginPostImpactGlide), so this delay expires onto a Calm() that
        // cannot do anything. See PuckLauncher.Calm.
        public float puckCalmDelay = 1.25f;

        [Header("Impact")]
        public float hitstopSeconds = 0.018f;

        [Header("Juice - Camera")]
        [SerializeField] float shakeAmplitude = 0.018f;
        [SerializeField] float shakeDuration = 0.45f;
        [SerializeField] float shakeFrequency = 24f;
        [SerializeField] float punchAmplitude = 0.088f;

        [Header("Coin stream fallback target (world)")]
        [Tooltip("Used only when the reference HUD cannot be created. Normally the fifth HUD pip is resolved through the camera at impact time.")]
        public Vector3 coinTarget = Vector3.zero;

        [Header("Ambience")]
        public float crowdVolume = 0.18f;

        [Header("Post")]
        public bool addBloom = true;
        public float bloomIntensity = 0.35f;
        public float bloomThreshold = 0.95f;

        [Header("Physics")]
        [Tooltip("The shot is fast and the colliders are thin; a shorter step is what keeps the puck inside the arena.")]
        public float fixedTimeStep = 0.01f;

        float _t0;
        float _impactTime;
        int _startFrame;
        int _stallFrames;
        float _worstFrame;
        GameObject _postFx;
        Vector3 _launchDir;
        float _launchPower = 1f;
        float _defaultFixedStep;
        Camera _targetCamera;
        Coroutine _cameraKick;
        Vector3 _cameraKickBase;
        bool _cameraKickActive;
        bool _manualHitstop;
        float _manualRestoreScale = 1f;
        float _manualRestoreFixed = 0.01f;

        /// <summary>Name written into the report.</summary>
        public override string SequenceName { get { return "Case4_Buca"; } }

        /// <summary>One 10 ms physics step per captured frame keeps the rigidbody result repeatable.</summary>
        public override int DeterministicCaptureFramerate { get { return 100; } }

        /// <summary>Guarantees total captured strip duration is exactly 4.05 seconds over 340 frames (dt = 0.01191s).</summary>
        public override float CaptureTailDuration { get { return Mathf.Max(0f, 4.05f - Report.totalDuration); } }

        /// <summary>The physics and all visible reward motion use the capture-controlled scaled clock.</summary>
        protected override float SequenceClock { get { return Time.time; } }

        /// <summary>True once the prewarm is done and the puck will accept an aim.</summary>
        /// <remarks>
        /// Backed by a serialized field rather than an auto-property: Unity's domain-reload backup
        /// does not carry compiler-generated backing fields, so a mid-playmode recompile would leave
        /// Ready false forever - Start never runs again - and the puck would refuse every launch.
        /// </remarks>
        public bool Ready { get { return _ready; } private set { _ready = value; } }
        [SerializeField, HideInInspector] bool _ready;

        bool _playerDriven;
        [SerializeField, HideInInspector] bool _shotSpent;

        /// <summary>
        /// True once a shot has run, until the arena is armed again. While it is true the puck is
        /// wherever the last shot left it, its collider is off after the post-impact glide, and the
        /// stack is down - so the scene is not in a state any new shot can be taken from.
        /// </summary>
        public bool ShotSpent { get { return _shotSpent; } }

        /// <summary>
        /// Puts the arena back to its authored pose for the next shot: puck on the disc with a live
        /// collider, stack rebuilt, rim idle, coins cleared.
        ///
        /// This is called when the player PRESSES, not when he releases and not when the previous shot
        /// ends. Both of those were tried by the code this replaces and both are wrong for the same
        /// reason: they move the world while the player is looking at it. Pressing is the one moment
        /// he expects the board to be made ready, and the capture harness never presses at all, so its
        /// cosmetic tail keeps the puck exactly where the shot left it.
        /// </summary>
        public void ArmNextShot()
        {
            if (!_shotSpent) return;
            _shotSpent = false;

            // Read BEFORE the reset: ResetState puts the puck back on the disc, and that call has to
            // keep doing exactly that, because the capture's Replay() goes through it and the
            // reference bank shot starts from the authored pose.
            bool keepInPlace = launcher != null && launcher.puck != null;
            Vector3 restingAt = keepInPlace ? launcher.puck.position : Vector3.zero;

            ResetState();

            // ...and then the puck is put back where the last shot left it. Arming the next shot and
            // sending the puck to the far right used to be the same call.
            if (keepInPlace) launcher.ResumeFrom(restingAt);

            Debug.Log(string.Format(
                "[Case4] ARMED for the next shot: stack rebuilt, puck stays where it stopped ({0})",
                keepInPlace ? launcher.puck.position.ToString("0.00") : "n/a"));
        }

        // ------------------------------------------------------------------ boot

        void Awake()
        {
            // Awake runs on a real scene load and never on a domain reload, so this is the one place
            // that may clear the serialized ready flag: the prewarm in Start has to earn it again.
            _ready = false;
            _defaultFixedStep = Time.fixedDeltaTime;
            if (fixedTimeStep > 0.001f) Time.fixedDeltaTime = fixedTimeStep;

            if (wall != null) wall.Init();
            if (launcher != null)
            {
                launcher.wall = wall;
                _launchDir = launcher.referenceAimDir;
            }
            // The surround has to exist before the first frame is rendered, not just before the
            // sequence runs: the harness renders warmup and settle frames of its own.
            ArenaDressing.Ensure();
        }

        void OnDestroy()
        {
            RestoreCameraKick();
            RestoreManualHitstop();
            if (_defaultFixedStep > 0.0001f) Time.fixedDeltaTime = _defaultFixedStep;
        }

        /// <summary>
        /// Warms up and then hands over to the base start hook, which does NOT play: the scene comes up
        /// idle and the puck only leaves the disc when the player pulls it back and lets go.
        /// </summary>
        protected override IEnumerator Start()
        {
            _targetCamera = Camera.main;
            if (_targetCamera == null)
                _targetCamera = FindFirstObjectByType<Camera>(FindObjectsInactive.Include);
            if (_targetCamera != null)
                _cameraKickBase = _targetCamera.transform.localPosition;
            EnsureBloom();
            yield return Prewarm();
            for (int i = 0; i < 8; i++) yield return null;
            if (_targetCamera != null)
                _targetCamera.transform.localPosition = _cameraKickBase;
            Ready = true;
            Debug.Log("[Case4] READY: waiting for the player to aim and release the puck");
            yield return base.Start();
        }

        void EnsureBloom()
        {
            if (!addBloom || _postFx != null) return;

            _postFx = new GameObject("Case4_PostFX");
            Volume v = _postFx.AddComponent<Volume>();
            v.isGlobal = true;
            v.priority = 100f;
            v.weight = 1f;

            VolumeProfile profile = ScriptableObject.CreateInstance<VolumeProfile>();
            profile.name = "Case4_Bloom";
            Bloom bloom = profile.Add<Bloom>(true);
            bloom.active = true;
            bloom.intensity.Override(bloomIntensity);
            bloom.threshold.Override(bloomThreshold);
            bloom.scatter.Override(0.82f);
            v.sharedProfile = profile;
        }

        /// <summary>
        /// Pays every first-use cost before the sequence clock starts: audio bank, coin instances and
        /// one throwaway render of the trail. It completes after three rendered frames, before the
        /// capture harness's five-stable-frame gate can start the run. Nothing here touches the stack.
        /// </summary>
        IEnumerator Prewarm()
        {
            float started = Time.realtimeSinceStartup;
            AudioService.Prewarm();
            if (coins != null) coins.Prewarm();
            if (wall != null) wall.Init();

            if (launcher != null)
            {
                if (coins != null) coins.ShowAll(true);
                launcher.SetTrail(true);
                if (wall != null) wall.Flash(launcher.ImpactPoint, 0.05f);
                HitstopService.Stop(0.001f);
                AudioService.PlayLoop(SfxId.CrowdAmbience, 0f);
                AudioService.PlayLayered(SfxId.PuckImpact, SfxId.Shatter, 0.01f, 0f, 0f);
                AudioService.Play(SfxId.WhooshArc, 0f);
                AudioService.Play(SfxId.DebrisFall, 0f);
                AudioService.Play(SfxId.RippleTick, 0f);
                AudioService.Play(SfxId.TapPop, 0f);
                AudioService.Play(SfxId.ArrivalImpact, 0f);

                yield return null;
                yield return null;
                yield return null;

                AudioService.StopLoop(SfxId.CrowdAmbience);
                HitstopService.Resume();
                CameraShakeService.StopAll();
                if (coins != null) { coins.ShowAll(false); coins.Clear(); }
                launcher.ResetInstant();
                launcher.Hold();
                if (wall != null) wall.ResetInstant();
                VFXPool.ReclaimAll();
            }

            // Both the measurement pass and capture replay must begin from the exact authored pose.
            // Merely sleeping the bodies preserves any tiny prewarm drift in the first pass only.
            if (shatter != null) shatter.ResetInstant();
            Physics.SyncTransforms();
            Debug.Log(string.Format("[Case4] WARMUP done waited={0:0.000}s stackMoved={1:0.000}",
                Time.realtimeSinceStartup - started, shatter != null ? shatter.MaxDisplacement() : 0f));
        }

        // ------------------------------------------------------------------ player launch

        /// <summary>
        /// Fires the puck along the direction the player aimed. <paramref name="power"/> is 0..1 from
        /// how far the sling was pulled back and scales the launch speed. Called from the frame the
        /// pointer was released, so the director's own input check lets it through.
        /// </summary>
        public bool LaunchFromPlayer(Vector3 direction, float power)
        {
            if (!Ready || IsPlaying || launcher == null) return false;

            // Read and cleared by RunSequence. Replay() never comes through here, so the capture's
            // filmed pass keeps the scripted wind-up.
            _playerDriven = true;

            Vector3 d = new Vector3(direction.x, 0f, direction.z);
            if (d.sqrMagnitude < 0.0001f) d = launcher.referenceAimDir;
            _launchDir = d.normalized;
            _launchPower = Mathf.Clamp01(power);

            Debug.Log(string.Format("[Case4] PLAYER_LAUNCH dir={0} power={1:0.00}",
                _launchDir.ToString("0.000"), _launchPower));

            Play();
            return IsPlaying;
        }

        // ------------------------------------------------------------------ sequence

        protected override IEnumerator RunSequence()
        {
            if (launcher == null || shatter == null || coins == null)
            {
                Debug.LogError("[Case4] Director is not wired; run Case4SceneSetup.Build first.");
                yield break;
            }

            EnsureRuntimeState("run");

            _t0 = Time.time - Mathf.Min(SequenceTime, 0.30f);
            _startFrame = Time.frameCount;
            _stallFrames = 0;
            _worstFrame = 0f;

            if (_launchDir.sqrMagnitude < 0.0001f) _launchDir = launcher.referenceAimDir;

            AudioService.PlayLoop(SfxId.CrowdAmbience, crowdVolume);
            StartCoroutine(StallWatch(8f));

            // ---------------------------------------------------------- 0. idle (0.00s .. 0.65s)
            // The scripted wind-up belongs to the HARNESS shot only. When the player has just let go
            // of the sling he has already done the pulling, and replaying it dragged the puck back to
            // the disc and wound it up a second time - measured: the puck finished pull #1 at
            // (-34.37,-8.97) and was then fired from (-27.10,-16.97), the rest disc. That is the
            // owner's "tekrar cektiginden basa donuyor". A harness-driven Play() is untouched.
            bool playerDriven = _playerDriven;
            _playerDriven = false;
            float scriptedIdle = playerDriven ? 0f : idleDelay;

            if (scriptedIdle > 0.001f)
            {
                BeginStep("idle");
                float idleStart = SequenceTime;
                PuckAimController aimCtrl = FindFirstObjectByType<PuckAimController>();
                while (SequenceTime - idleStart < scriptedIdle)
                {
                    float elapsed = SequenceTime - idleStart;
                    float t = Mathf.Clamp01(elapsed / scriptedIdle);
                    float pull = Mathf.SmoothStep(0f, 1f, t);
                    // Holds the puck on its disc. It used to be dragged 0.85u back along the aim over
                    // this same ramp; see PuckLauncher.HoldForScriptedShot for what that looked like and
                    // what removing it does to the launch origin. `pull` still drives the aim cone's
                    // fade below, which is the only other thing this ramp was ever used for.
                    launcher.HoldForScriptedShot();
                    if (_targetCamera != null)
                    {
                        _targetCamera.transform.localPosition = _cameraKickBase;
                    }
                    if (aimCtrl != null)
                    {
                        // ShowAim measures its pull from _pressPoint, which no pointer has ever set on
                        // this path, so it was aiming the cone at the world origin. The reference cone
                        // points along the shot and fades out as the pullback builds.
                        // Along the direction THIS shot will take, not along the reference bank.
                        // For a harness-driven Play() the two are the same vector, so the reference
                        // strip is unchanged; for a player release they differ by however far off the
                        // bank he aimed, and the owner was watching the bank.
                        aimCtrl.ShowIdleAim(1f - pull, _launchDir);
                    }
                    yield return null;
                }
                if (aimCtrl != null) aimCtrl.HideAim();
                if (_targetCamera != null) _targetCamera.transform.localPosition = _cameraKickBase;
                EndStep();
            }

            // ---------------------------------------------------------- 1. launch
            BeginStep("launch");
            // anticipationDuration is 0 in the authored scene and Squash.SquashStretch returns at
            // duration <= 0, so Anticipate() draws nothing. The Fire() below used to run regardless
            // and reported "puck compresses into the disc for 0.00 s" - a beat in the juice log that
            // no frame contains. Report it only when it exists; the call itself stays, so raising the
            // field is all it takes to get the beat (and its report) back.
            if (anticipationDuration > 0.001f)
                Fire(JuiceEvent.Anticipation, "puck compresses into the disc for " + anticipationDuration.ToString("0.00") + " s before firing");
            else
                Debug.Log("[Case4] ANTICIPATION_SKIPPED anticipationDuration=0; no compression is drawn and none is reported");
            launcher.Anticipate(anticipationDuration);
            AudioService.Play(SfxId.TapPop, 0.55f, 0.9f);
            yield return WaitUntil(scriptedIdle + anticipationDuration);

            // Ensure the rim is active
            if (wall != null && !wall.IsActive) wall.Activate(0.24f);
            // Activate() and SetEmission() have had nothing to write to since 3d4984e stopped the
            // material swap, so the cyan release flash is GONE, not hidden. The call is kept - it is
            // the hook for rebuilding the beat against the authored materials - but claiming the rim
            // "switches to cyan" while no pixel changes is worse than saying nothing.
            if (wall != null && wall.IsWired)
                Fire(JuiceEvent.ImpactVFX, "arena rim switches from idle white to cyan on release");
            else
                Debug.Log("[Case4] RIM_FLASH_UNWIRED release flash requested but the rim keeps its authored materials; nothing is drawn");

            launcher.SetTrail(true);
            Fire(JuiceEvent.Trail, "short spark trail, " + launcher.trailLifetime.ToString("0.00") + " s lifetime; speed cue only");
            Fire(JuiceEvent.SquashStretch, "puck stretched " + launcher.stretchAmount.ToString("0.00") + " along travel while airborne");
            AudioService.Play(SfxId.WhooshArc, 0.6f);

            // The pile was held kinematic at its exact authored rest pose while idle. Arm all bodies
            // together on the release frame so first play and replay enter PhysX with the same state.
            shatter.ArmPhysics(launcher.PuckCollider);
            Physics.SyncTransforms();
            StartLaunchCameraKick(_launchDir);
            launcher.Launch(_launchDir, _launchPower);
            EndStep();

            // ---------------------------------------------------------- 2. flight
            // Real ricochets. End on the solver frame of the first stack collision, not one or more
            // frames later when a displacement threshold finally notices the pile moved.
            BeginStep("flight");
            // THE FLIGHT ENDS WHEN THE SHOT RESOLVES, NOT WHEN A STOPWATCH SAYS SO.
            //
            // It used to be `while (elapsed < flightTimeout && !launcher.StackHit)`, and the sequence
            // then treated the timeout as proof of a miss. It is not proof of anything except that the
            // director stopped waiting. The owner's own console has the failure verbatim
            // (2026-08-26 06:14:44, seq 281-304): a hand-aimed shot banked ten times, was still doing
            // 25.45 u/s when the 2.40 s budget expired, and the sequence ran its whole impact and
            // collapse beat with reached=false - COIN_BLOCKED, "coins launched = 0". One second later
            // the puck arrived for real, COIN_ARMED fired from a genuine solver contact, the cascade
            // ran and the pile came down in front of him. The end-of-run PROOF line for that same shot
            // reads `stackHit=True` and `coin stream: 0 coins launched`. That IS the bug: the stack
            // was hit, the payout was never launched, and every gate in the tree passed, because every
            // one of them measures the reference bank, which arrives at 1.07-1.13 s and never spends
            // its budget.
            //
            // The distinguishing quantity is time-to-stack, so the loop is written against the two
            // things that actually end a shot:
            //   * the puck reaches the stack, or
            //   * the puck stops moving, which is the only honest definition of a miss.
            // flightTimeout keeps its pacing job - after it the puck's energy is bled off so a miss
            // still resolves in about the same beat it used to - but it is no longer a verdict, and
            // the bleed-off deliberately keeps the contact path live (PuckLauncher.BleedOff, not
            // Calm) so a puck that arrives during the bleed still registers a real hit.
            float flightStart = Time.time;
            float stillSince = -1f;
            string flightEnd = "ceiling";
            while (!launcher.StackHit)
            {
                float flightElapsed = Time.time - flightStart;
                if (flightElapsed >= 0.30f && _targetCamera != null)
                {
                    float t = Mathf.Clamp01((flightElapsed - 0.30f) / 0.40f);
                    float curve = Mathf.Sin(t * Mathf.PI * 0.5f);
                    Vector3 flightDrift = new Vector3(-0.047f * curve, 0f, -0.023f * curve);
                    _targetCamera.transform.localPosition = _cameraKickBase + flightDrift;
                }

                if (legacyFixedFlightBudget)
                {
                    if (flightElapsed >= flightTimeout) { flightEnd = "legacy-timeout"; break; }
                    yield return null;
                    continue;
                }

                if (flightElapsed >= flightTimeout) launcher.BleedOff();

                if (launcher.Speed <= flightRestSpeed)
                {
                    if (stillSince < 0f) stillSince = Time.time;
                    if (Time.time - stillSince >= flightRestHold) { flightEnd = "rest"; break; }
                }
                else stillSince = -1f;

                if (flightElapsed >= flightCeiling) { flightEnd = "ceiling"; break; }
                yield return null;
            }
            if (launcher.StackHit) flightEnd = "stack";
            float flightTime = Time.time - flightStart;
            bool reached = launcher.StackHit;
            Debug.Log(string.Format("[Case4] FLIGHT {0:0.000}s rails hit={1} travelled={2:0.00} speed={3:0.00} reachedStack={4}",
                flightTime, launcher.BounceCount, launcher.FlightDistance, launcher.Speed, reached));
            // Printed separately from FLIGHT because they are different quantities and the old log
            // conflated them: FLIGHT is how long the DIRECTOR waited, TimeToStack is when the puck
            // actually arrived. On the failing shot those were 2.403 s and about 3.5 s, and no line
            // in the log said so.
            Debug.Log(string.Format(
                "[Case4] FLIGHT_END reason={0} waited={1:0.000}s timeToStack={2} bledOff={3} " +
                "(budget {4:0.00}s, rest<= {5:0.00}u/s for {6:0.00}s, ceiling {7:0.00}s)",
                flightEnd, flightTime,
                launcher.TimeToStack >= 0f ? launcher.TimeToStack.ToString("0.000") + "s" : "never",
                launcher.Bleeding, flightTimeout, flightRestSpeed, flightRestHold, flightCeiling));
            EndStep();

            // ---------------------------------------------------------- 3. impact
            BeginStep("impact");
            _impactTime = SequenceTime;

            if (reached)
            {
                StartCameraKick(launcher.ImpactDirection);
            }
            // The collision relay already plays the hard puck contact exactly on the solver frame.
            // This second layer is the softer block-collapse body, not a fake glass shatter.
            PlayDebrisLayer("impact", reached, 0.46f, 0.96f);
            if (wall != null) wall.Flash(launcher.ImpactPoint, 0.42f);
            if (reached) shatter.StartColorEvolution();
            shatter.EmissionPulse(0.42f);
            launcher.SetTrail(false);

            Fire(JuiceEvent.Hitstop, reached ? hitstopSeconds.ToString("0.000") + " s micro-freeze on real stack contact" : "no hitstop: flight timed out without stack contact");
            Fire(JuiceEvent.CameraShake, string.Format("{0:0.00} amp / {1:0.00} s / {2:0} Hz", shakeAmplitude, shakeDuration, shakeFrequency));
            Fire(JuiceEvent.CameraPunch, "along the puck's travel, " + punchAmplitude.ToString("0.00"));
            Fire(JuiceEvent.ImpactVFX, "whole blocks topple and evolve green -> mustard -> red -> magenta");

            if (reached) yield return DeterministicHitstop(hitstopSeconds);

            // The reference emits its FIRST coin on the impact beat (t=1.82), not on the collapse beat
            // (t=1.90). Launching at collapse meant only ~16 of the 22 coins had left by t=2.10 and
            // three of those were still inside the pop-in scale ramp, so the arc read as 13 coins
            // against the reference's 18. The stream is still armed only by the solver's own contact:
            // `reached` is a real stack contact and coins.Armed was set from the contact point itself.
            // The toppled-blocks proof CANNOT exist on this frame - the blocks have not had time to
            // move yet - so it is still measured in the collapse step below, where a failure disarms
            // the stream and aborts it mid-flight.
            Vector3 coinOrigin = launcher.ImpactPoint + Vector3.up * (shatter.blockPitch * 0.55f);
            if (!(reached && coins.Armed)) coins.Disarm();
            // U1: In reference coins fly to top-right. Without HUD, send to point just outside top-right viewport.
            Vector3 resolvedCoinTarget = ResolvedCoinTarget(coinOrigin);
            coins.BuildCurve(coinOrigin, resolvedCoinTarget);
            StartCoroutine(DelayedCoinLaunch(coins, 0.0f));

            yield return WaitScaled(impactDuration);
            EndStep();

            // ---------------------------------------------------------- 4. collapse
            BeginStep("collapse");
            Fire(JuiceEvent.Deform, shatter.BlockCount + " whole blocks fan out in a deterministic contact-triggered cascade");
            // The coin stream is locked behind two physical gates:
            //   1. the puck actually reached the stack (not stopped short by an obstacle), AND
            //   2. blocks really came off their rest pose because of it.
            // Gate 1 is checked on the impact beat, where the stream now starts. Gate 2 can only be
            // measured here, once the cascade has had a beat to move: failing it disarms the stream,
            // which Launch() re-checks every frame, so an unearned stream stops mid-string instead of
            // paying out in full.
            int toppled = shatter.MovedCount(shatter.blockSize * 0.25f);
            bool payoutEarned = reached && coins.Armed && toppled > 0;
            if (!payoutEarned) coins.Disarm();

            Debug.Log(string.Format(
                "[Case4] COIN_GATE stackHit={0} armed={1} toppledBlocks={2} -> payout={3}; contact={4} origin={5} originToContact={6:0.000}u",
                reached, coins.Armed, toppled, payoutEarned,
                launcher.ImpactPoint.ToString("0.###"), coinOrigin.ToString("0.###"),
                Vector3.Distance(coinOrigin, launcher.ImpactPoint)));

            PlayDebrisLayer("collapse", reached && toppled > 0, 0.9f, 1f);

            // Measured on the SCALED clock on purpose. A slow frame - and the gate run is slow, it
            // logs every assertion - is clamped by maximumDeltaTime, so physics advances less than
            // wall clock does. Timing the collapse on wall clock therefore cut the collapse short
            // exactly in the run that was being measured: the same shot toppled twelve blocks in the
            // capture and six under the gate. Time.time advances with the simulation, so the collapse
            // now gets the same amount of physics however slowly the frames come.
            float collapseStart = Time.time;
            bool calmed = false;
            int deterministicFrames = Time.captureFramerate > 0
                ? Mathf.Max(1, Mathf.RoundToInt(collapseTimeout * Time.captureFramerate))
                : 0;
            int collapseFrame = 0;
            // Keep this beat at the measured 1.50 seconds even if a batchmode solver happens to put the
            // pile to sleep early. The colour story reaches magenta at 0.98 seconds after impact, so an
            // asleep-count shortcut would cut off both the visual reference beat and determinism.
            while (deterministicFrames > 0
                ? collapseFrame < deterministicFrames
                : Time.time - collapseStart < collapseTimeout)
            {
                // The puck is left at full speed long enough to plough all the way through the stack -
                // damping it on first contact stops it dead against the first block it grazes and the
                // rest of the stack never hears about the hit - and is then bled off so the climax is
                // the collapse rather than a puck still touring the arena behind it.
                if (!calmed && Time.time - collapseStart > puckCalmDelay)
                {
                    calmed = true;
                    launcher.Calm();
                }
                collapseFrame++;
                yield return null;
            }
            if (!calmed) launcher.Calm();
            EndStep();

            // ---------------------------------------------------------- 5. settle
            // The climax is over: the puck is calmed so the shot does not go around the arena again.
            BeginStep("settle");
            launcher.ClearTrail();
            float completionDelay = Mathf.Min(0.25f, settleDuration);
            yield return WaitScaled(completionDelay);
            yield return WaitScaled(Mathf.Max(0f, settleDuration - completionDelay));
            launcher.Park();
            EndStep();

            AudioService.StopLoop(SfxId.CrowdAmbience);

            // ---------------------------------------------------------- proof
            // PROOF criterion AMENDED (collapse-direction work). Old form:
            //     "{0} blocks, {1} moved >{2:0.00}u, {3} rotated >12deg, fragments, wholeForm"
            //     and it required moved == BlockCount.
            // New form:
            //     "{0} blocks, {1} undisturbed (must be 0), formationSpread x{2} (must be >= 3.0),
            //      {3} rotated >12deg, fragments, wholeForm"
            // Why: "every block moved further than half a block" was a proxy for "the cascade ran
            // at all", written when the cascade threw every block a long way in one direction. Once
            // the depth spread was fitted to the reference that proxy stopped being valid - and the
            // REFERENCE ITSELF FAILS IT. Its own median forward travel is -0.35 block-widths (about
            // 0.16u), under the 0.233u bar, so a stack matching the reference necessarily leaves a
            // block or two barely displaced. A criterion the thing we are copying cannot satisfy is
            // measuring the wrong property. The replacement measures what the proxy stood for: no
            // block is still in its rest pose (moved OR turned), and the formation itself is gone
            // (footprint opened out at least 3x). `moved` is still logged, informational, so the
            // number stays comparable with every earlier run in Logs/.
            int moved = shatter.MovedCount(shatter.blockSize * 0.5f);
            int undisturbed = shatter.UndisturbedCount(shatter.blockSize * 0.5f, 12f);
            float formationSpread = shatter.FormationSpread();
            int rotated = shatter.RotatedCount(12f);
            Fire(JuiceEvent.Deform, string.Format(
                "outcome moved={0} rotated={1} max={2:0.000} bounces={3}",
                moved, rotated, shatter.MaxDisplacement(), launcher.BounceCount));
            Debug.Log(string.Format("[Case4] PROOF impact -> end of settle = {0:0.00} s", SequenceTime - _impactTime));
            Debug.Log(string.Format(
                "[Case4] PROOF stack: {0} blocks, {1} undisturbed (must be 0), formationSpread x{2:0.0} (informational - the old >=3.0 bar assumed a one-row-deep rest footprint and is unreachable in this layout), {3} rotated >12deg, fragments={4}, wholeForm={5}, maxDisplacement={6:0.00}, moved>{7:0.00}u={8} (informational)",
                shatter.BlockCount, undisturbed, formationSpread, rotated,
                shatter.FragmentCount, shatter.WholeFormCount, shatter.MaxDisplacement(),
                shatter.blockSize * 0.5f, moved));
            Debug.Log(string.Format(
                // Was one `travelled` number, read as flight distance, and it was not: _distance keeps
                // accumulating through PostImpactGlide, which drives puck.position for a further
                // 0.74 s and about 5 units after the shot is over. FLIGHT above and PROOF here
                // therefore disagreed by the glide, with nothing saying so. Both are printed now.
                "[Case4] PROOF puck: rigidbody kinematic={0}, {1}, rail contacts={2}, flight={3:0.00}u + scripted glide {4:0.00}u = {5:0.00}u total, stackHit={6}, contactNormalSpeed={7:0.00}",
                launcher.Body != null && launcher.Body.isKinematic, launcher.ColliderSummary(),
                launcher.BounceCount, launcher.FlightDistance,
                launcher.TravelledDistance - launcher.FlightDistance, launcher.TravelledDistance,
                launcher.StackHit, launcher.ImpactNormalSpeed));
            Debug.Log(string.Format(
                "[Case4] PROOF coin stream: {0} coins launched on a {1:0.000} s stagger along one arc",
                coins.LaunchedCount, coins.stagger));
            Debug.Log(string.Format(
                "[Case4] PROOF coin exit: leavesFrame={0} at viewport ({1:0.000},{2:0.000}) " +
                "[top-right quadrant = x>=0.5 and y>=0.5 -> {3}]; {4:0} px of the {5:0} px arc is on screen, " +
                "neighbour gap {6:0.0} px (reference crossing (0.990,1.000), reference gap 63.6 px)",
                coins.ExitsFrame, coins.ExitViewport.x, coins.ExitViewport.y,
                coins.ExitsFrame && coins.ExitViewport.x >= 0.5f && coins.ExitViewport.y >= 0.5f ? "PASS" : "FAIL",
                coins.OnScreenPathPx, coins.ScreenPathPx, coins.NeighbourGapPx));
            Debug.Log(string.Format(
                "[Case4] PROOF coin origin: armedFromContact={0} contact={1} maxSpawnOffsetFromContact={2:0.000}u " +
                "(spawn jitter radius {3:0.000}u), launchesRefusedWithoutContact={4}",
                coins.Armed, coins.ContactPoint.ToString("0.###"), coins.WorstSpawnOffset,
                shatter.blockPitch * 0.55f + 0.16f, coins.BlockedLaunchAttempts));
            Debug.Log(string.Format(
                "[Case4] PROOF contactless payout: stackHit={0}, coins launched while un-armed = {1}",
                launcher.StackHit, coins.Armed ? 0 : coins.LaunchedCount));
            string payoutWhy;
            bool payoutHeld = PayoutInvariantHolds(out payoutWhy);
            Debug.Log("[Case4] PAYOUT_INVARIANT " + (payoutHeld ? "PASS " : "FAIL ") + payoutWhy);
            Debug.Log(string.Format(
                "[Case4] PROOF arena rim: {0} flash CALLS, cyan active={1}, wired={2}" +
                (wall != null && wall.IsWired ? "" : " -- NOTHING WAS DRAWN: the rim keeps its authored materials, so these are requests, not pixels"),
                wall != null ? wall.FlashCount : 0, wall != null && wall.IsActive,
                wall != null && wall.IsWired));
            Debug.Log(string.Format(
                "[Case4] TIMING run={0:0.000} s over {1} frames ({2:0.0} fps), stalls>0.12s={3}, worst frame={4:0.000}s",
                SequenceTime, Time.frameCount - _startFrame,
                (Time.frameCount - _startFrame) / Mathf.Max(0.001f, SequenceTime),
                _stallFrames, _worstFrame));

            // The arena is now spent: puck parked wherever the shot left it, stack down. It is armed
            // again on the next press, not here - see ArmNextShot.
            _shotSpent = true;
        }

        /// <summary>
        /// THE PRE-REGISTERED INVARIANT, written before the fix that made it hold:
        ///
        ///   Every shot that registers a real solver contact with the stack emits at least
        ///   <see cref="PayoutCoinFloor"/> coins, and the first coin's first drawn frame is inside
        ///   the viewport.
        ///
        /// Both halves are needed. LaunchedCount alone cannot tell "no payout" from "a payout the
        /// player could not see", and those are the same complaint from the owner's chair. A shot
        /// that never touched the stack is not covered: it is supposed to pay nothing, and the
        /// contactless-payout proof above is what holds that end.
        /// </summary>
        public bool PayoutInvariantHolds(out string detail)
        {
            int floor = PayoutCoinFloor;
            bool hit = launcher != null && launcher.StackHit;
            int launched = coins != null ? coins.LaunchedCount : 0;
            Vector3 vp = coins != null ? coins.FirstCoinViewport : Vector3.zero;
            bool onScreen = coins != null && coins.FirstCoinOnScreen;

            detail = string.Format(
                "stackHit={0} timeToStack={1} coinsLaunched={2} (floor {3} of {4}) " +
                "firstCoinViewport=({5:0.000},{6:0.000},{7:0.00}) firstCoinOnScreen={8}",
                hit,
                launcher != null && launcher.TimeToStack >= 0f ? launcher.TimeToStack.ToString("0.000") + "s" : "never",
                launched, floor, coins != null ? coins.coinCount : 0,
                vp.x, vp.y, vp.z, onScreen);

            if (!hit) { detail += " -> not covered (no stack contact; the payout is supposed to be nothing)"; return true; }
            if (launched < floor) { detail += " -> BROKEN: the stack was hit and the payout did not play"; return false; }
            if (!onScreen) { detail += " -> BROKEN: coins were emitted where the player cannot see them"; return false; }
            detail += " -> held";
            return true;
        }

        /// <summary>Fewest coins a shot that hit the stack is allowed to emit: 90% of the authored stream.</summary>
        public int PayoutCoinFloor
        {
            get { return coins == null ? 0 : Mathf.Max(1, Mathf.FloorToInt(coins.coinCount * 0.9f)); }
        }

        int _debrisSfxPlayed;
        int _debrisSfxRefused;

        /// <summary>
        /// How many times the impact/collapse debris layer has actually been SOUNDED. Counted where the
        /// AudioService call is made, so it rises whenever that sound is audible and for no other reason.
        ///
        /// It replaces a counter that could not move. The old one read
        ///     <c>bool play = earned; if (play &amp;&amp; !earned) _contactlessImpactSfx++;</c>
        /// - a contradiction, since <c>play</c> IS <c>earned</c> - so it was pinned at 0 by construction,
        /// and the gate line built on it ("no impact or debris sound played on a shot that touched
        /// nothing") was a tautology that no regression could ever have failed. Counting the plays and
        /// letting the MISS shot assert the delta is 0 is the same claim, measured somewhere it can move:
        /// un-gate the call and the counter reads 1.
        /// </summary>
        public int DebrisSfxPlayed { get { return _debrisSfxPlayed; } }

        /// <summary>Debris-layer beats that were withheld because nothing was hit. Informational.</summary>
        public int DebrisSfxRefused { get { return _debrisSfxRefused; } }

        /// <summary>
        /// The falling-debris layer of the impact and collapse beats.
        ///
        /// It used to be two bare AudioService.Play calls straight off the sequence timeline, outside
        /// every `reached` guard around them, so a shot that hit nothing still produced them.
        /// SfxLibrary.BuildDebrisFall is forty band-passed noise ticks with sine pings sweeping upward
        /// from 900 Hz - a cascade of bright metallic tings. Played over a shot that touched nothing,
        /// that is exactly a coin-collect sound arriving out of nowhere.
        ///
        /// The coin STREAM was already gated on the solver's own contact (CoinArcStream.Launch refuses
        /// to run un-armed). Its soundtrack was not, which is the whole of this bug.
        /// </summary>
        void PlayDebrisLayer(string beat, bool earned, float volume, float pitch)
        {
            if (earned)
            {
                _debrisSfxPlayed++;
                AudioService.Play(SfxId.DebrisFall, volume, pitch);
            }
            else
            {
                _debrisSfxRefused++;
            }
            Debug.Log(string.Format("[Case4] SFX_DEBRIS beat={0} earned={1} played={2} (played so far={3}, refused={4})",
                beat, earned, earned, _debrisSfxPlayed, _debrisSfxRefused));
        }

        IEnumerator StallWatch(float seconds)
        {
            float until = Time.unscaledTime + seconds;
            while (Time.unscaledTime < until && IsPlaying)
            {
                float dt = Time.unscaledDeltaTime;
                if (dt > _worstFrame) _worstFrame = dt;
                if (dt > 0.12f)
                {
                    _stallFrames++;
                    Debug.LogWarning(string.Format("[Case4] STALL {0:0.000} s frame at seqTime {1:0.000} s",
                        dt, SequenceTime));
                }
                yield return null;
            }
        }

        // ------------------------------------------------------------------ reset

        /// <summary>
        /// Re-establishes everything Awake and Start establish, and logs whatever it had to repair.
        /// <para>Why this exists: the capture harness measures the sequence once and then films a
        /// <c>Replay()</c>, so the filmed run is never the first run. That alone is fine - but Unity
        /// can recompile scripts and reload the domain <i>while play mode is running</i> (it did:
        /// Logs/g_CaptureDenseCase4.log line 1227, "Reloading assemblies after finishing script
        /// compilation", sits between the measuring run and the filmed one). Awake and Start do not
        /// run again afterwards, every object built at runtime is destroyed, and every reference the
        /// serializer does not carry comes back null. In that log the measuring run ended at 1.130 s
        /// with 3 rail contacts and 34.91 units travelled; the two runs after the reload both ran to
        /// the 2.40 s flight timeout with 0 rail contacts and 71.55 units - and the capture sampled
        /// one of those. Replay() cannot restore what Awake owned, so the pieces heal themselves.</para>
        /// </summary>
        void EnsureRuntimeState(string reason)
        {
            string repaired = "";

            if (fixedTimeStep > 0.001f && Mathf.Abs(Time.fixedDeltaTime - fixedTimeStep) > 0.0001f)
            {
                repaired += " fixedDeltaTime(" + Time.fixedDeltaTime.ToString("0.0000") + "->" +
                            fixedTimeStep.ToString("0.0000") + ")";
                Time.fixedDeltaTime = fixedTimeStep;
            }

            if (_targetCamera == null)
            {
                _targetCamera = Camera.main;
                if (_targetCamera == null)
                    _targetCamera = FindFirstObjectByType<Camera>(FindObjectsInactive.Include);
                if (_targetCamera != null)
                {
                    _cameraKickBase = _targetCamera.transform.localPosition;
                    repaired += " camera";
                }
            }

            if (wall != null) wall.Init();

            if (launcher != null)
            {
                if (launcher.EnsureInitialised()) repaired += " launcher";
                if (launcher.wall != wall) { launcher.wall = wall; repaired += " launcher.wall"; }
            }

            if (shatter != null && shatter.EnsureBlocks()) repaired += " stack";

            if (ArenaDressing.Ensure()) repaired += " dressing";

            if (coins != null) coins.Prewarm();

            if (repaired.Length > 0)
                Debug.LogWarning("[Case4] RUNTIME_STATE_REPAIRED at=" + reason + " ->" + repaired);
            else
                Debug.Log("[Case4] RUNTIME_STATE_OK at=" + reason + " blocks=" +
                          (shatter != null ? shatter.BlockCount : 0) +
                          " fixedDeltaTime=" + Time.fixedDeltaTime.ToString("0.0000"));
        }

        protected override void ResetState()
        {
            EnsureRuntimeState("reset");
            RestoreCameraKick();
            RestoreManualHitstop();
            StopAllCoroutines();
            Tweener.CancelAll();

            if (coins != null) coins.Clear();
            if (launcher != null) { launcher.ResetInstant(); launcher.Hold(); }
            if (shatter != null) shatter.ResetInstant();
            if (wall != null) wall.ResetInstant();
            Physics.SyncTransforms();

            VFXPool.ReclaimAll();
            HitstopService.Resume();
            CameraShakeService.StopAll();
            AudioService.StopLoop(SfxId.CrowdAmbience);
        }

        void StartLaunchCameraKick(Vector3 dir)
        {
            if (_targetCamera == null) _targetCamera = Camera.main;
            if (_targetCamera == null) return;
            RestoreCameraKick();
            _cameraKickActive = true;
            _cameraKick = StartCoroutine(LaunchCameraKickRoutine(dir));
        }

        IEnumerator LaunchCameraKickRoutine(Vector3 dir)
        {
            Vector3 kickDir = dir.sqrMagnitude > 0.0001f ? dir.normalized : Vector3.forward;
            float duration = 0.30f;
            _targetCamera.transform.localPosition = _cameraKickBase;
            yield return null;

            float started = Time.time;
            while (Time.time - started < duration)
            {
                float elapsed = Time.time - started;
                float t = Mathf.Clamp01(elapsed / duration);
                float kickMult = 5.75f;
                float kick = Mathf.Sin(t * Mathf.PI) * punchAmplitude * kickMult;
                _targetCamera.transform.localPosition = _cameraKickBase + kickDir * kick;
                yield return null;
            }
            RestoreCameraKick(false);
        }

        void StartCameraKick(Vector3 impactDirection)
        {
            if (_targetCamera == null) return;
            RestoreCameraKick();
            _cameraKickActive = true;
            _cameraKick = StartCoroutine(CameraKick(impactDirection));
        }

        IEnumerator CameraKick(Vector3 impactDirection)
        {
            Vector3 punchDirection = impactDirection.sqrMagnitude > 0.0001f
                ? impactDirection.normalized
                : Vector3.left;
            float duration = 3.45f;
            float started = Time.time;
            Vector3 driftOffset = new Vector3(-0.047f, 0f, -0.023f);

            while (Time.time - started < duration)
            {
                float elapsed = Time.time - started;
                float k = Mathf.Clamp01(elapsed / duration);
                float fastFalloff = Mathf.Exp(-elapsed * 12.0f);
                float phase = elapsed * shakeFrequency * Mathf.PI * 2f;
                Vector3 shake = new Vector3(
                    Mathf.Sin(phase + 0.31f),
                    Mathf.Sin(phase * 1.37f + 1.73f),
                    Mathf.Sin(phase * 0.71f + 3.11f) * 0.5f) * (shakeAmplitude * fastFalloff);
                float punch = elapsed <= 0.14f
                    ? Mathf.Sin(elapsed / 0.14f * Mathf.PI) * punchAmplitude * 0.12f
                    : 0f;
                float holdDrift = elapsed <= 0.50f
                    ? 1f
                    : Mathf.Clamp01(1f - (elapsed - 0.50f) / 0.35f);
                float quietValley = elapsed > 0.14f && elapsed < 0.52f
                    ? Mathf.Sin((elapsed - 0.14f) / 0.38f * Mathf.PI) * punchAmplitude * 0.48f
                    : 0f;
                float victoryKick = elapsed >= 0.52f && elapsed <= 0.78f
                    ? Mathf.Sin((elapsed - 0.52f) / 0.26f * Mathf.PI) * punchAmplitude * 3.65f
                    : 0f;
                _targetCamera.transform.localPosition = _cameraKickBase + driftOffset * holdDrift + shake + punchDirection * (punch + quietValley + victoryKick);
                yield return null;
            }

            RestoreCameraKick(false);
        }

        void RestoreCameraKick(bool stopCoroutine = true)
        {
            if (stopCoroutine && _cameraKick != null) StopCoroutine(_cameraKick);
            _cameraKick = null;
            if (!_cameraKickActive || _targetCamera == null) return;
            _targetCamera.transform.localPosition = _cameraKickBase;
            _cameraKickActive = false;
        }

        IEnumerator DelayedCoinLaunch(CoinArcStream stream, float delay)
        {
            if (delay > 0f) yield return new WaitForSeconds(delay);
            if (stream != null) yield return stream.Launch();
        }

        IEnumerator DeterministicHitstop(float seconds)
        {
            if (seconds <= 0f) yield break;

            if (Time.captureFramerate <= 0)
            {
                HitstopService.Stop(seconds);
                while (HitstopService.IsActive) yield return null;
                yield break;
            }

            _manualRestoreScale = Time.timeScale > 0f ? Time.timeScale : 1f;
            _manualRestoreFixed = Time.fixedDeltaTime;
            _manualHitstop = true;
            Time.timeScale = 0f;

            int frames = Mathf.Max(1, Mathf.CeilToInt(seconds * Time.captureFramerate));
            for (int i = 0; i < frames; i++) yield return null;
            RestoreManualHitstop();
        }

        void RestoreManualHitstop()
        {
            if (!_manualHitstop) return;
            _manualHitstop = false;
            Time.timeScale = _manualRestoreScale;
            Time.fixedDeltaTime = _manualRestoreFixed;
        }

        public Vector3 ResolvedCoinTarget(Vector3 origin)
        {
            if (_targetCamera != null && coins != null)
            {
                // The payout is aimed OUT of the frame through the top-right corner.
                //
                // Measured in the reference, not chosen: the lead coin in Buca.mp4 was tracked from
                // f93 (t=1.824) to f117 (t=2.294) and runs dead straight in screen space from
                // viewport (0.168, 0.441) to (0.947, 0.971) - 22 samples, none more than 0.0018
                // viewport units off that line, at a constant (+38.4, -42.0) px per frame. It stops
                // there because that is where the reference's coin bank is; this build has no HUD
                // (U4), so the same line simply carries on and leaves the frame. Continuing it one
                // step past the last visible sample crosses the boundary at viewport (0.990, 1.000).
                //
                // Aiming at (1.13, 1.06) from the reference shot's impact point puts the crossing at
                // (0.990, 1.000) - the reference's own crossing - and holds it between (0.969, 1.000)
                // and (0.991, 1.000) across all four stack-hit impact points recorded in Logs/.
                //
                // The earlier (1.08, 1.05) attempt is not this one and was abandoned for a real
                // reason: it kept arcRise at 14 world units, which balloons the arc over the divider
                // so hard that it crosses the TOP edge at viewport x=0.58 - the middle of the frame,
                // not the corner - and dumps most of the string off-screen on the way. The rise is
                // what had to go, and the reference agrees: its path bows less than 3 px over a
                // 1242 px run, where ours at rise 14 bows 79 px.
                //
                // NOT ViewportToWorldPoint. That scales the horizontal offset by Camera.aspect,
                // which is the editor window's 1.32 at this moment and not the strip's 0.625, so the
                // authored 0.67 was landing at 0.859 of the rendered frame and would land somewhere
                // else again on another machine. CaptureViewportPoint states the aspect.
                return coins.CaptureViewportPoint(_targetCamera, 1.13f, 1.06f, origin);
            }
            return coinTarget;
        }

        /// <summary>Holds until <paramref name="offset"/> seconds into the run, on the capture-controlled clock.</summary>
        IEnumerator WaitUntil(float offset)
        {
            if (Time.captureFramerate > 0)
            {
                float remaining = Mathf.Max(0f, _t0 + offset - Time.time);
                int frames = Mathf.Max(0, Mathf.RoundToInt(remaining * Time.captureFramerate));
                for (int i = 0; i < frames; i++) yield return null;
                yield break;
            }
            while (Time.time < _t0 + offset) yield return null;
        }

        /// <summary>Holds for <paramref name="seconds"/> of simulated time, so slow frames cannot skip physics.</summary>
        IEnumerator WaitScaled(float seconds)
        {
            if (Time.captureFramerate > 0)
            {
                int frames = Mathf.Max(0, Mathf.RoundToInt(seconds * Time.captureFramerate));
                for (int i = 0; i < frames; i++) yield return null;
                yield break;
            }
            float until = Time.time + seconds;
            while (Time.time < until) yield return null;
        }
    }
}
