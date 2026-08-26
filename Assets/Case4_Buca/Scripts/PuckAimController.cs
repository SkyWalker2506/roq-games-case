using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;
using Shared.Audio;

namespace Case4
{
    /// <summary>
    /// The player's half of Case 4. Press on the puck, pull back, let go: the puck is fired along the
    /// opposite of the pull with a speed set by how far it was pulled. There is no trajectory preview
    /// any more - the shot is real physics, so a predicted polyline would be a promise the engine does
    /// not have to keep, and the reference shows only a short direction cone at the puck anyway.
    ///
    /// Two drivers share one code path: a real pointer, and <see cref="SimulateDragRelease"/> for
    /// batchmode, where there is no mouse at all (lesson #7).
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PuckAimController : MonoBehaviour
    {
        [Header("Wiring (filled in by Case4SceneSetup)")]
        public Case4Director director;
        public PuckLauncher launcher;
        public Camera targetCamera;
        public Material aimLineMaterial;

        [Header("Feel")]
        [Tooltip("How close to the puck the press has to land to start an aim.")]
        public float grabRadius = 3.2f;

        [Tooltip("Pull distance in world units that counts as full power.")]
        public float maxPull = 7.0f;

        [Tooltip("Shortest pull that fires at all; anything less is treated as a cancelled aim.")]
        public float minPull = 0.9f;

        public float flightHeight = 0.35f;

        [Header("Aim indicator")]
        [Tooltip("Length of the direction cone at full power, in world units. Kept short on purpose: the reference has no trajectory predictor.")]
        public float indicatorLength = 2.4f;
        public float indicatorWidth = 0.55f;

        [Header("Stack, for the gate's fallback aim (world XZ)")]
        public Vector3 stackAimPoint;

        [Header("Aim cone core (the reference's brightening tip)")]
        [Tooltip("Fraction along the cone at which the bright core starts. Measured on the reference " +
                 "at n=1, luminance over the local floor median across x[600,1000): +17.8 L at y=1000, " +
                 "+38.6 at 950, +55.7 at 900, +76.0 at 850, +97.6 at 800, apex ~y=790. Its wedge is " +
                 "dim and wide at the puck and brightens as it narrows. Ours is a flat +20.5..+21.3 " +
                 "for its whole visible run. MEASURED after the first pass at coreStart 0.30: the core " +
                 "is live (+40.9 at y=950 against the reference's +38.6) but its near edge landed at " +
                 "screen y=1009, so it also lifted y=1000 to +40.9 where the reference wants +17.8. " +
                 "0.40 puts the near edge at y=973 - clear of row 1000 by more than the rounded start " +
                 "cap can bleed (numCapVertices=2 extends it about one core half-width, ~9 px here) - " +
                 "while y=950 stays well inside the span. The far rows cannot be fixed from here: at " +
                 "y=900 the axis is at x~943 and the rail edge is at x=945, so the narrow core spine " +
                 "is behind the rail while the halo's left flank still shows. That is the ceiling " +
                 "until the cone stops being aimed into the rail.")]
        public float coreStart = 0.40f;

        [Tooltip("Core width as a fraction of indicatorWidth, so the bright part is a narrow spine " +
                 "inside the dim halo rather than a second full-width cone.")]
        public float coreWidthScale = 0.30f;

        [Tooltip("_BaseColor alpha for the core's own material instance. The halo keeps the material " +
                 "asset's 0.08, which measured +21 L and matches the reference's near-puck end. " +
                 "Additive (SrcAlpha/One) makes brightness linear in alpha: 0.08 -> +21 means about " +
                 "+262 L per unit alpha, so 0.10 adds about +26 where the core covers, taking the " +
                 "overlap to about +47. That is a two-step approximation of the reference's +18->+98 " +
                 "ramp, deliberately conservative: one flat core cannot BE a ramp, and overshooting " +
                 "the reference at y=950/900 would be worse than undershooting it.")]
        public float coreAlpha = 0.10f;

        // Two renderers, not one gradient. Universal Render Pipeline/Unlit declares only POSITION and
        // TEXCOORD0 (UnlitForwardPass.hlsl, struct Attributes) and computes
        // `alpha = texColor.a * _BaseColor.a` - there is no COLOR semantic anywhere in the pass. A
        // LineRenderer.colorGradient on this material is therefore never read: it would have looked
        // like a fix in source review and drawn nothing. _BaseColor IS consumed, and
        // LineRenderer.material instantiates per renderer, so a second renderer with its own instance
        // is the one lever that actually reaches the pixels.
        LineRenderer _aimLine;
        LineRenderer _aimCore;
        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        bool _aiming;
        Vector3 _pressPoint;

        /// <summary>True while the player is holding the sling back.</summary>
        public bool IsAiming { get { return _aiming; } }

        /// <summary>Direction of the most recent release.</summary>
        public Vector3 LastDirection { get; private set; }

        /// <summary>Power of the most recent release, 0..1.</summary>
        public float LastPower { get; private set; }

        /// <summary>True once a release has been accepted by the director.</summary>
        public bool LastLaunchAccepted { get; private set; }

        // ---------------------------------------------------------------- indicator, for the gate
        // Read-only windows onto what the cone is actually doing. The gate cannot assert "the
        // indicator is hidden at rest" or "the indicator is on the player's heading" from outside
        // without them, and those are exactly the two states no existing assertion could falsify.

        /// <summary>True while the aim cone is being drawn.</summary>
        public bool IndicatorVisible { get { return _aimLine != null && _aimLine.enabled; } }

        /// <summary>Unit XZ heading the aim cone is currently drawn along; zero when it is hidden.</summary>
        public Vector3 IndicatorDirection
        {
            get
            {
                if (!IndicatorVisible || _aimLine.positionCount < 2) return Vector3.zero;
                Vector3 d = _aimLine.GetPosition(1) - _aimLine.GetPosition(0);
                d.y = 0f;
                return d.sqrMagnitude < 0.000001f ? Vector3.zero : d.normalized;
            }
        }

        /// <summary>The measured reference shot direction, computed by Case4SceneSetup.</summary>
        public Vector3 ReferenceAimDirection
        {
            get { return launcher != null ? launcher.referenceAimDir : Vector3.forward; }
        }

        void Awake()
        {
            BuildAimLine();
        }

        void BuildAimLine()
        {
            if (_aimLine != null || aimLineMaterial == null) return;

            // Wide, dim halo: the part that already measured correctly against the reference's
            // near-puck end (+21 L vs its +18..+39). Unchanged.
            _aimLine = CreateConeRenderer("PuckAimIndicator", indicatorWidth);

            // Narrow, bright core over the far half only. Keeping it off the near half is what stops
            // this from being "the whole cone got brighter": the halo alone still owns the base.
            _aimCore = CreateConeRenderer("PuckAimIndicatorCore", indicatorWidth * coreWidthScale);
            if (_aimCore != null && _aimCore.material != null)
            {
                Color c = _aimCore.material.GetColor(BaseColorId);
                c.a = coreAlpha;
                _aimCore.material.SetColor(BaseColorId, c);
            }
        }

        /// <summary>
        /// One tapered additive strip. Separate GameObjects rather than one renderer with more points,
        /// because the brightness difference has to live in a per-renderer material instance.
        /// </summary>
        LineRenderer CreateConeRenderer(string goName, float widthMultiplier)
        {
            GameObject go = new GameObject(goName);
            go.transform.SetParent(transform, false);
            go.hideFlags = HideFlags.DontSave;
            LineRenderer lr = go.AddComponent<LineRenderer>();
            lr.material = aimLineMaterial;          // instantiates; each renderer gets its own _BaseColor
            lr.useWorldSpace = true;
            lr.positionCount = 2;
            lr.numCapVertices = 2;
            // Tapered: wide at the puck, a point at the far end. That is the reference's little cone,
            // not a line drawn across the whole arena.
            lr.widthCurve = AnimationCurve.Linear(0f, 1f, 1f, 0.05f);
            lr.widthMultiplier = widthMultiplier;
            lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            lr.receiveShadows = false;
            lr.enabled = false;
            return lr;
        }

        /// <summary>
        /// Places halo and core along one axis. Every draw path goes through here.
        ///
        /// Note on length, because the obvious fix here is wrong: the cone is NOT too short in
        /// geometry. Projecting indicatorLength=7.5 through the scene camera (pos -31.07/27.07/-41.53,
        /// pitch 41.456, yaw 0, vertical FOV 33.81, 1728 px tall) puts the tip 351 px up-screen from
        /// the puck, against the reference's 328 px - already slightly longer. What actually happens
        /// is that our axis runs +17.4 deg clockwise of screen-vertical, straight into the RIGHT RAIL,
        /// and the cone is occluded from about y=900 down to nothing by y=850 - roughly 200 px of the
        /// 351. The reference's axis sits near -3.5 deg and runs up the open lane for its full length.
        /// Raising indicatorLength only pushes more of the cone behind the rail. The axis comes from
        /// launcher.referenceAimDir, which also determines every ricochet, the impact point and the
        /// coin arming, so it is not a lever that can be moved for the indicator's sake alone.
        /// </summary>
        void PlaceCone(Vector3 from, Vector3 dir, float length, float widthScale)
        {
            if (_aimLine == null) return;
            Vector3 tip = from + dir * length;

            _aimLine.enabled = true;
            _aimLine.positionCount = 2;
            _aimLine.SetPosition(0, from);
            _aimLine.SetPosition(1, tip);
            _aimLine.widthMultiplier = indicatorWidth * widthScale;

            if (_aimCore == null) return;
            _aimCore.enabled = true;
            _aimCore.positionCount = 2;
            _aimCore.SetPosition(0, from + dir * (length * Mathf.Clamp01(coreStart)));
            _aimCore.SetPosition(1, tip);
            _aimCore.widthMultiplier = indicatorWidth * coreWidthScale * widthScale;
        }

        // ------------------------------------------------------------------ pointer

        void Update()
        {
            if (director == null || launcher == null || launcher.puck == null) return;
            if (director.IsPlaying || !director.Ready) { HideAim(); return; }

            // Untouched puck: NO cone. It used to be drawn here every frame, which is why the owner
            // saw a grey wedge sitting behind the puck before he had touched anything - measured at
            // 78209 of 78220 idle frames. The reference does show a cone before its shot, but that is
            // during its pull-back, not at rest, and the capture still gets it: the director's idle
            // step calls ShowIdleAim itself while it winds the sling up. This path is the resting
            // scene, and the resting scene has no cone.
            if (!_aiming) HideAim();

            Pointer pointer = Pointer.current;
            if (pointer == null) return;

            Camera cam = ResolveCamera();
            if (cam == null) return;

            Vector3 point;
            if (!GroundPoint(cam, pointer.position.ReadValue(), out point)) return;

            if (!_aiming && pointer.press.wasPressedThisFrame)
            {
                // Accept a press near the puck OR near the disc it belongs on. After a shot the puck
                // is lying somewhere out in the arena, and requiring the player to find it there is
                // how "press to take the next shot" stopped working at all.
                Vector3 d = point - launcher.puck.position;
                d.y = 0f;
                Vector3 dRest = point - launcher.RestPosition;
                dRest.y = 0f;
                if (d.magnitude <= grabRadius || (director.ShotSpent && dRest.magnitude <= grabRadius))
                {
                    BeginAim(point);
                    AudioService.Play(SfxId.TapPop, 0.35f, 1.15f);
                }
                return;
            }

            if (!_aiming) return;

            if (pointer.press.isPressed)
            {
                ShowAim(point);
                return;
            }

            if (pointer.press.wasReleasedThisFrame)
            {
                _aiming = false;
                HideAim();
                Release(point, false);
            }
        }

        // ------------------------------------------------------------------ simulated pointer

        /// <summary>
        /// Drives an aim and a release with no real pointer behind it, through the same
        /// <see cref="Release"/> the pointer path uses. Batchmode has no mouse, so this is how the
        /// input gate proves the shot is player-started rather than scripted.
        /// </summary>
        public IEnumerator SimulateDragRelease(Vector3 pressWorld, Vector3 releaseWorld, float holdSeconds)
        {
            BeginAim(pressWorld);

            float end = Time.time + Mathf.Max(0.05f, holdSeconds);
            while (Time.time < end)
            {
                ShowAim(releaseWorld);
                yield return null;
            }

            _aiming = false;
            HideAim();
            Release(releaseWorld, true);
        }

        /// <summary>Convenience for the gate: pull straight back along <paramref name="aimDirection"/>.</summary>
        public IEnumerator SimulateAimAt(Vector3 aimDirection, float power, float holdSeconds)
        {
            Vector3 dir = new Vector3(aimDirection.x, 0f, aimDirection.z).normalized;
            Vector3 press = launcher.puck.position;
            Vector3 release = press - dir * (maxPull * Mathf.Clamp01(power));
            yield return SimulateDragRelease(press, release, holdSeconds);
        }

        /// <summary>Fires the measured reference shot; used by the layout and input gates.</summary>
        public IEnumerator SimulateReferenceShot(float holdSeconds)
        {
            yield return SimulateAimAt(ReferenceAimDirection, 1f, holdSeconds);
        }

        // ------------------------------------------------------------------ shared

        void Release(Vector3 releasePoint, bool simulated)
        {
            Vector3 pull = _pressPoint - releasePoint;
            pull.y = 0f;
            float dist = pull.magnitude;

            if (dist < minPull)
            {
                Shared.Sequencing.SeqLog.Info(string.Format("[Case4] AIM_CANCELLED pull={0:0.00} < {1:0.00}; puck stays on the disc",
                    dist, minPull));
                return;
            }

            Vector3 dir = pull / dist;
            float power = Mathf.Clamp01(dist / maxPull);
            LastDirection = dir;
            LastPower = power;

            Shared.Sequencing.SeqLog.Info(string.Format("[Case4] RELEASE {0} dir={1} pull={2:0.00} power={3:0.00}",
                simulated ? "(simulated)" : "(pointer)", dir.ToString("0.00"), dist, power));

            // A simulated release has no real input frame behind it, so the director's own
            // "nothing runs itself" check has to be told this one is deliberate.
            if (simulated) director.AllowPlayWithoutInput();

            LastLaunchAccepted = director.LaunchFromPlayer(dir, power);
            Shared.Sequencing.SeqLog.Info("[Case4] LAUNCH_ACCEPTED=" + LastLaunchAccepted);
        }

        /// <summary>Middle of the stack, the fallback aim point for the gate log.</summary>
        public Vector3 WallAimPoint() { return stackAimPoint; }

        /// <summary>
        /// Starts an aim. Both drivers go through here so neither can skip the re-arm: the previous
        /// shot left the puck out in the arena with its collider switched off by the post-impact
        /// glide, and firing from that state is what sent the puck straight through the rails.
        /// Arming on the PRESS means the board is made ready before the player has pulled anything,
        /// so nothing moves under his hand once the drag has started.
        /// </summary>
        void BeginAim(Vector3 pressPoint)
        {
            if (director != null && director.ShotSpent) director.ArmNextShot();
            _aiming = true;
            _pressPoint = pressPoint;
        }

        public void ShowAim(Vector3 point)
        {
            if (_aimLine == null || launcher == null || launcher.puck == null) return;

            Vector3 pull = _pressPoint - point;
            pull.y = 0f;
            float dist = pull.magnitude;

            if (dist < minPull)
            {
                launcher.SetPullbackAlong(Vector3.zero, 0f);
                HideAim();
                return;
            }

            float power = Mathf.Clamp01(dist / maxPull);
            Vector3 dir = pull / dist;

            // The puck follows the hand. It used to stand still through the whole drag and only move
            // after the release, when the director wound it up along its own scripted axis - so the
            // pull the player made and the pull he saw were two different things.
            launcher.SetPullbackAlong(dir, power);

            Vector3 from = launcher.puck.position + Vector3.up * 0.04f;
            // Length tracks the pull instead of starting at 55% of full. The old floor meant a
            // barely-there drag already drew more than half the line, which is the owner's
            // "full uzun olmasin ufak cekince". power is the SAME value the launch uses
            // (dist / maxPull), so what he sees and what fires cannot drift apart. At minPull -
            // the shortest drag that fires at all - power is 0.129, so the line is visibly a stub
            // there and only reaches full length at a full pull. The 0.06 floor keeps it from
            // vanishing entirely inside the dead zone, where it correctly reads as "this will not fire".
            PlaceCone(from, dir, indicatorLength * Mathf.Max(0.06f, power), 1f);
        }

        public void HideAim()
        {
            if (_aimLine != null) _aimLine.enabled = false;
            if (_aimCore != null) _aimCore.enabled = false;
        }

        /// <summary>
        /// The resting direction cone the reference shows while the puck is still on the disc, before
        /// anyone has touched it. The drag indicator only exists while a pointer is held, and batchmode
        /// has no pointer at all, so without this the idle frames render no cone where the reference
        /// clearly has one.
        /// </summary>
        public void ShowIdleAim() { ShowIdleAim(1f); }

        /// <param name="strength">
        /// 1 draws the cone at full size, 0 hides it. Measured from the reference: at t=0.00 s the cone
        /// runs 255 px from the puck and is ~70 px wide at its base, and by t=0.65 s, with the puck
        /// pulled back and about to fire, nothing is left above y=940 - the cone has shrunk away rather
        /// than grown. So the idle step fades it out as the pullback builds.
        /// </param>
        public void ShowIdleAim(float strength) { ShowIdleAim(strength, ReferenceAimDirection); }

        /// <param name="aimDirection">
        /// The heading the cone is drawn along. This used to be hard-wired to
        /// <see cref="ReferenceAimDirection"/> - the scripted 4-rail bank - so the wind-up cone pointed
        /// down the capture's heading no matter which way the player had pulled. Measured at 40.0 deg
        /// of error against a 40-deg-off pull. The capture path is unaffected: the director passes its
        /// own launch direction, which for a harness-driven Play() still IS referenceAimDir.
        /// </param>
        public void ShowIdleAim(float strength, Vector3 aimDirection)
        {
            if (_aimLine == null || launcher == null || launcher.puck == null) return;

            strength = Mathf.Clamp01(strength);
            if (strength < 0.02f) { HideAim(); return; }

            Vector3 dir = aimDirection;
            dir.y = 0f;
            if (dir.sqrMagnitude < 0.0001f) { HideAim(); return; }
            dir.Normalize();

            Vector3 from = launcher.puck.position + Vector3.up * 0.04f;
            PlaceCone(from, dir, indicatorLength * strength, strength);
        }

        Camera ResolveCamera()
        {
            if (targetCamera != null) return targetCamera;
            targetCamera = Camera.main;
            return targetCamera;
        }

        bool GroundPoint(Camera cam, Vector2 screen, out Vector3 world)
        {
            Plane plane = new Plane(Vector3.up, new Vector3(0f, flightHeight, 0f));
            Ray ray = cam.ScreenPointToRay(screen);
            float enter;
            if (!plane.Raycast(ray, out enter)) { world = Vector3.zero; return false; }
            world = ray.GetPoint(enter);
            return true;
        }
    }
}
