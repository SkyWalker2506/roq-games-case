using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Shared.Audio;
using Shared.Juice;
using Shared.Tweening;

namespace Case4
{
    /// <summary>
    /// The puck. It is a real Rigidbody: the release sets a velocity, the arena rails and the divider
    /// are real colliders, and every ricochet is a physics contact rather than a point on a
    /// precomputed polyline. The previous version interpolated between waypoints, which is why the
    /// shot read as an animation - it was one.
    ///
    /// Rail and stack contacts come from a dedicated OnCollision relay on the puck itself. This keeps
    /// impact timing tied to the physics solver rather than inferred from velocity turns or from how
    /// far the stack happened to move one frame later.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PuckLauncher : MonoBehaviour
    {
        // Every squash and stretch below is applied to the puck's VISUAL child, never to the body.
        // The collider is a sphere on the body, so scaling the body scales the collider: the shot's
        // effective radius wobbled by several percent while the deform tween ran, the ricochet points
        // moved with it, and two runs of the identical launch arrived at the stack in different
        // places. Deforming the child keeps the physics repeatable.

        [Header("Wiring (filled in by Case4SceneSetup)")]
        public Transform puck;
        public GameObject starTrailPrefab;
        [Tooltip("Soft-circle material used by the continuous white core under the discrete gold stars.")]
        public Material trailGlowMaterial;

        [Tooltip("The gold payout. It is armed here, on the solver's own contact frame, and nowhere else.")]
        public CoinArcStream payout;

        [Tooltip("Assigned at runtime by Case4Director; the rail is a plain object, not a component.")]
        [System.NonSerialized] public NeonWallFlash wall;

        [Header("Reference shot (computed by Case4SceneSetup from the measured arena)")]
        [Tooltip("Unit XZ direction of the shot that reproduces the reference: up the right lane, over the divider, off the arch, down onto the stack.")]
        public Vector3 referenceAimDir = Vector3.forward;
        [Tooltip("Launch speed in world units per second for a full-power release.")]
        public float launchSpeed = 18f;
        [Tooltip("Predicted reference path, logged for the layout gate. Not used to move the puck.")]

        [Header("Feel")]
        public float flightHeight = 0.35f;

        [Tooltip("Vertical rise applied to the puck during the idle pull-back. ZERO, and it must stay " +
                 "zero: the reference puck does not lift before the release at all. Per-frame it is " +
                 "bit-static for 32 frames - centroid (865,1118), gold count 1511, both unchanged - " +
                 "and then steps -78 px at n=33.\n\n" +
                 "This field used to be 3.35, justified by 'the reference puck travels 127 px up-screen " +
                 "between t=0.00 and t=0.65 while the rim is still idle white'. That reading sampled " +
                 "two endpoints and attributed the difference to a 0.65 s anticipation. At 51 fps " +
                 "t=0.65 s IS n=33 - the launch frame. The 127 px is the launch itself, and there are " +
                 "no intervening frames in which any lift occurs. An interval inferred from its " +
                 "endpoints.\n\n" +
                 "What it actually produced: the puck rose 143 px over 53 frames, grew 30% in gold " +
                 "area (3520 -> 4574) as it neared the camera, visibly detached from its own contact " +
                 "shadow, and then snapped back down 164 px on the release frame - the owner's " +
                 "'havaya kalkiyor sonra gidiyor'.\n\n" +
                 "Changing it is shot-neutral by construction, not by measurement: Launch() builds " +
                 "launchPos as (puck.position.x, _restPos.y, puck.position.z), discarding the puck's " +
                 "Y outright, and the body carries RigidbodyConstraints.FreezePositionY thereafter. " +
                 "Nothing downstream reads the term this feeds.\n\n" +
                 "The horizontal pull-back that used to stand beside it has since been zeroed too, for " +
                 "the same reason and on the same evidence: over those 32 static reference frames the " +
                 "puck's centroid moves 1.3 px in x as well as 1.2 px in y. It is now unread by " +
                 "anything - see PuckLauncher.HoldForScriptedShot.")]
        public float idleLift = 0f;
        public float trailEmissionRate = 30f;
        [Tooltip("LEGACY. It used to scale the trail objects' transforms and, through them, the " +
                 "particle sizes. It no longer feeds any size: the trail is measured in puck " +
                 "diameters now, which is the quantity this field was standing in for. Left in " +
                 "place because the scene serialises it; nothing reads it.")]
        public float trailScale = 0.65f;
        public float trailLifetime = 0.27f;

        // ---------------------------------------------------------------- trail, measured
        // Owner: "arkadan gelen izde daha farkli. soldakine daha benzer yap." What ours drew was a
        // white bloom smear plus a tidy column of identical four-pointed stars at even spacing - an
        // authored pattern, not speed. What the reference draws, measured off Buca.mp4 frame n=30
        // (1080x1728, puck screen diameter d = 82 px, .plan-build/cli/case4-puck/trail.py):
        //
        //   streak   ONE component attached to the puck, 88 x 177 px. Strongly warm (R-B > 20) for
        //            0.80 d behind the puck and faintly warm to about 2.1 d; 56 px wide at its base
        //            (0.68 d) and tapering to nothing. Peak colour walks
        //            (253,250,173) -> (250,198,162) -> (243,180,153) -> (188,150,145) -> floor:
        //            pale and hot where it leaves the puck, saturated orange through the body,
        //            desaturating to warm grey at the tail.
        //   droplets 9 to 13 SEPARATE round components, aspect ~1.0, diameters 3..14 px
        //            (0.037 d .. 0.171 d - a 4.7x spread, not one repeated sprite), spread from
        //            2.0 d to 4.45 d behind the puck, scattered off the flight line by up to
        //            +120 px (1.46 d) on both sides, dimming toward the floor colour with distance.
        //
        // Ours, same script, same frame scale, on .plan-build/verify/Buca/frame_100.png: 5 star
        // components at 1.41 / 2.01 / 2.61 / 3.25 / 3.88 d - even 0.6 d steps - all within a few px
        // of the flight line, one size, and NO warm streak at all.
        //
        // The fields below are what those readings turn into. Lengths are expressed as a multiple
        // of the puck's world diameter and converted with the live launch speed, because the trail
        // in world simulation space is (speed x lifetime) long: a length in puck-diameters is the
        // thing the reference actually holds constant, seconds are not.
        // ------------------------------------------------------------------------------------
        // THE TRAIL'S SHAPE IS CODE-OWNED, NOT SERIALIZED. [System.NonSerialized] is load-bearing.
        //
        // Buca.unity is hand-authored and must not be written by this tree, and it carries its own
        // copies of these fields from the last time a builder wrote it (streakLength 2.1,
        // streakWidth 0.629, screenToWorld 2.7). A serialized field is read from the SCENE, not
        // from the initialiser here, so every previous round's numbers were overwritten by the
        // scene the instant it loaded and the owner saw no change at all from them. Marking them
        // NonSerialized makes Unity ignore the scene's stale keys - which stay in the YAML,
        // harmlessly, without the file being touched - and makes the values below the ones that
        // actually run. Do not put [SerializeField] or plain `public` back on them.
        //
        // MEASURED 2026-08-26 off ref_flight.png and after_flight.png at 1080x1728, by taking
        // perpendicular profiles at fixed multiples of the puck's on-screen diameter behind the
        // puck (reference d = 53 px, ours d = 44 px) and scoring WARMTH, R-B over the floor's own
        // R-B, rather than luminance. Warmth is the right axis twice over: it is the thing the
        // owner is complaining about, and it excludes the cyan rail and the bright floor spot that
        // sit beside the trail in these two frames. BOTH earlier rounds measured luminance and both
        // got the width backwards because of it - af2ae78 read the reference as 1.07 d, d2aaacc
        // read ours as 1.69 d and halved the quad to 0.629, and the run I inherited read ours as a
        // 2.10 d "bright slab" and was about to cut it again to 0.300. Reproduced here: the same
        // profile scored on luminance returns 2.0-2.3 d for the reference because the blue rail
        // clears floor+40 L.
        //
        // Warm width (R-B > 30), in puck diameters, at 0.25 / 0.50 / 0.75 / 1.00 / 1.25 / 1.50 / 1.75 d behind:
        //   reference  1.50  1.44  1.36  1.22  1.06  0.54  0.14   <- widest at the source, then tapers away
        //   ours       1.00  0.98  0.88  0.78  0.74  0.66  0.70   <- narrower than the reference AND parallel-sided
        //
        // So the owner is right and both re-measurements were wrong: ours is a THIN column, 33%
        // narrower at the source than the reference, and it does not taper - it is still 0.52 d
        // wide at 2.5 d behind, where the reference has been gone since 1.75. The reference plume
        // is 1.5x the puck wide where it leaves the puck; that excess over the puck's own width is
        // what reads as "flares slightly".
        [Header("Trail shaped against the reference (measured; see the block above this in source)")]

        /// <summary>Puck diameters behind the puck that the warm streak covers, ON SCREEN.
        /// MEASURED: the reference's warmth is 140 at 0.25 d, 78 at 1.25 d, 43 at 1.50 d, 17 at
        /// 1.75 d and 2 at 2.00 d, so the streak is over by 1.75 d. Ours held a flat 90-92 all the
        /// way out to 2.50 d.</summary>
        [System.NonSerialized] public float streakLengthInDiameters = 1.75f;

        /// <summary>Authored quad width of one streak puff, as a fraction of the puck diameter.
        /// Solved from a measured point rather than a model: 0.629 authored renders 1.00 d of warm
        /// width at the source, so the authored-to-rendered factor is 1.59, and the reference's
        /// 1.50 d needs 1.50 / 1.59 = 0.94. That lands within 6% of af2ae78's original 0.994, which
        /// d2aaacc then cut by a third in the wrong direction.</summary>
        [System.NonSerialized] public float streakWidthInDiameters = 0.94f;

        /// <summary>Nearest and furthest droplet, in puck diameters behind the puck, ON SCREEN.
        /// MEASURED on the reference: the countable warm droplets run 2.14 d to 5.58 d.</summary>
        [System.NonSerialized] public Vector2 dropletSpanInDiameters = new Vector2(2.1f, 5.6f);

        [Tooltip("FALLBACK ONLY. Screen puck-diameters -> world puck-diameters along the shot. " +
                 "Every reference trail length was read off a FRAME, so it is a screen length, and a " +
                 "world length along the shot is foreshortened before it reaches the frame. This used " +
                 "to be a shipped constant of 2.7, solved once on the reference bank's opening leg, " +
                 "which runs away from the camera.\n" +
                 "THAT IS WHY THE TRAIL RAN LONG. The puck ricochets; most legs of a real shot run " +
                 "ACROSS the screen, where there is no foreshortening at all, and the same world-space " +
                 "lifetime then draws 2.7x further than it was solved to. Measured on after_flight.png, " +
                 "a leg running across frame: our streak was still 0.52 d wide and warm at 2.5 puck " +
                 "diameters behind, where the reference is gone by 1.75.\n" +
                 "ResolveForeshortening now measures it from the camera and the puck's ACTUAL heading " +
                 "every frame, so the trail is the same length on screen whichever way the shot is " +
                 "going. This field is only used when there is no camera to measure with.")]
        [System.NonSerialized] public float screenToWorldAlongFlight = 2.7f;

        /// <summary>Authored quad diameters of a droplet, as a fraction of the puck diameter.
        /// MEASURED rendered sizes: reference 0.075..0.491 d over seven countable droplets, ours
        /// 0.091..0.341 from an authored 0.020..0.094. The low end is right; the top end is short,
        /// and it is the RANGE that stops the field reading as one repeated mark. Scaling the top
        /// end by the same authored-to-rendered factor the low end shows gives 0.135.</summary>
        [System.NonSerialized] public Vector2 dropletSizeInDiameters = new Vector2(0.018f, 0.135f);

        /// <summary>Droplets alive at once. MEASURED 13 warm components in the reference frame, of
        /// which 7 clear R-B 25. Ours returned 4 real ones. Set above the count that must be
        /// countable, because the faintest of ours fall under the threshold that counted them.</summary>
        [System.NonSerialized] public int dropletCount = 18;

        /// <summary>Sideways wander of a droplet off the flight line, in puck diameters.
        /// MEASURED: the reference's droplets sit a mean 0.56 d off the flight axis and its widest
        /// straggler 1.64 d. Ours sat a mean 0.13 d off it - four times too tidy, and that
        /// on-axis regularity is most of why the owner reads them as an authored pattern rather
        /// than as debris. This is a cap on a radial speed, so only the widest droplet approaches
        /// it; raised to 2.4 to move the MEAN to the reference's 0.56.</summary>
        [System.NonSerialized] public float dropletScatterInDiameters = 2.4f;

        /// <summary>How many streak puffs are emitted per puff WIDTH of travel. The seams between
        /// consecutive puffs are what the owner calls hard horizontal bands, and they are invisible
        /// only while the gap is small against the puff. Stated per puff width rather than as a rate
        /// so it survives any change to <see cref="streakWidthInDiameters"/>.</summary>
        public const float SamplesPerPuffWidth = 16f;

        [Tooltip("World diameter of the drawn puck. Used only to convert the trail's " +
                 "puck-diameter units into world units; read off the renderer at Awake when it can " +
                 "be, this is the fallback.")]
        public float puckDrawnDiameter = 1.306f;
        public float stretchAmount = 0.10f;
        public float bounceSquash = -0.10f;

        [Header("Damping applied once the shot is over")]
        public float restingDamping = 6.5f;

        Rigidbody _rb;
        Transform _visual;
        PhysicsMaterial _physicsMaterial;
        PhysicsMaterial _railPhysicsMaterial;
        ParticleSystem _trail;
        ParticleSystem _glowTrail;
        // The rest pose is captured once, on the very first Awake, and must never be re-read from a
        // puck that has already been pulled back, launched or squashed. [SerializeField] keeps these
        // through a mid-playmode domain reload, which is the only way EnsureInitialised can ever run
        // against an already-moved puck.
        [SerializeField, HideInInspector] bool _restCaptured;
        [SerializeField, HideInInspector] Vector3 _restPos;
        [SerializeField, HideInInspector] Quaternion _restRot;
        [SerializeField, HideInInspector] Vector3 _restScale;
        [SerializeField, HideInInspector] Vector3 _bodyScale = Vector3.one;
        [SerializeField, HideInInspector] Vector3 _visualRestLocalPos;

        // Local-space Y that seats the drawn coin on its own contact patch. MEASURED off Unity's
        // bounds on the first Awake, not authored: the puck prefab draws its Body with the coin's
        // underside at the puck origin, which is itself 0.128u off the floor, so at zero lift the coin
        // still hangs 0.106u over the dark ellipse that grounds it. A second hand-tuned constant would
        // have been a third layer of compensation on top of the first two; this is read from the two
        // renderers it has to reconcile, so it stays correct if either is resized.
        [SerializeField, HideInInspector] float _visualSeatLift;

        // Where the NEXT shot begins. For the harness's scripted bank, and for the first pull of a
        // session, that is the rest disc. For every pull after that it is wherever the previous shot
        // actually left the puck. The two used to be the same value - _restPos - which is why the
        // owner's second pull always started at the far right.
        [SerializeField, HideInInspector] Vector3 _shotOrigin;
        [SerializeField, HideInInspector] bool _shotOriginSet;

        [Header("Launch pad")]
        [Tooltip("Local-space Y lift applied to the puck's VISUAL child. NOW ZERO, and it has to stay " +
                 "zero unless the start disc is being drawn again.\n\n" +
                 "It was 0.396, and it was right when it was written: the imported start disc was " +
                 "VISIBLE, its top plane sat at world y=0.561, the coin spanned y 0.185..0.299, and it " +
                 "was therefore buried inside the disc - frame_00 held zero gold pixels in the whole " +
                 "1080x1728 image. Lifting the render child put the coin on the rim.\n\n" +
                 "266a162 then hid the disc for the entire run (HidePad, called from EnsureInitialised " +
                 "onwards) because the disc itself was the owner's first 'extra round object'. The lift " +
                 "was not removed with it, so the coin went on floating over a rim that no longer " +
                 "renders. Measured by the gate: coin drawn at y[0.505,0.733], contact patch top at " +
                 "y=0.022 - a 0.483u gap, twice the coin's own 0.228u thickness - and the coin's centre " +
                 "0.305u above the collider's. On screen that is a dark ellipse lying on the floor with " +
                 "a gold disc hanging over it: the owner's 'baslangicta baska pak var, giden pak " +
                 "farkli'.\n\n" +
                 "Physics never read this: the collider is on the body and only the render child moves.")]
        public float visualPadLift = 0f;

        Renderer _padRenderer;
        Vector3 _lastDir;
        int _bounces;
        float _distance;
        float _flightDistance;
        Vector3 _lastSample;
        bool _flying;
        float _nextBounceAt;
        bool _stackHit;
        Collider _impactCollider;
        Vector3 _impactPoint;
        Vector3 _impactDirection;
        float _impactNormalSpeed;
        Coroutine _postImpactGlide;
        float _launchTime;
        float _stackHitTime = -1f;
        bool _bleeding;

        /// <summary>The puck's body. Null only if the scene was not built.</summary>
        public Rigidbody Body { get { return _rb; } }

        /// <summary>Physics shape used for the canonical first stack contact.</summary>
        public Collider PuckCollider { get { return puck != null ? puck.GetComponent<Collider>() : null; } }

        /// <summary>Rail contacts counted since the last launch.</summary>
        public int BounceCount { get { return _bounces; } }

        /// <summary>
        /// Path length actually travelled since the last launch, measured, not planned. This is the
        /// WHOLE path: it keeps accumulating through PostImpactGlide, which drives puck.position for
        /// another 0.74 s and roughly 5 units after the shot is over. Read
        /// <see cref="FlightDistance"/> for the distance the puck covered under physics.
        /// </summary>
        public float TravelledDistance { get { return _distance; } }

        /// <summary>
        /// Distance travelled between launch and the solver frame of the first stack contact - the
        /// flight, and nothing else. Frozen at that contact, so the scripted glide cannot inflate it.
        /// Equal to <see cref="TravelledDistance"/> on a shot that never reaches the stack.
        /// </summary>
        public float FlightDistance { get { return _stackHit ? _flightDistance : _distance; } }

        /// <summary>Current speed of the body.</summary>
        public float Speed { get { return _rb == null ? 0f : _rb.linearVelocity.magnitude; } }

        /// <summary>True from the exact solver frame in which the puck first contacts a marked stack cube.</summary>
        public bool StackHit { get { return _stackHit; } }

        /// <summary>
        /// Seconds from the launch to the solver frame of the first stack contact; -1 if the puck has
        /// not reached the stack. Recorded because "how long did this shot take to arrive" was the one
        /// number that separated the shots that paid out from the ones that did not, and nothing in
        /// the log carried it: FLIGHT prints how long the DIRECTOR waited, which on a shot that
        /// arrived late is a different number entirely.
        /// </summary>
        public float TimeToStack { get { return _stackHitTime; } }

        /// <summary>True while the flight step is deliberately bleeding the puck's energy off.</summary>
        public bool Bleeding { get { return _bleeding; } }

        /// <summary>Puck radius used for the gate's pass-through test.</summary>
        public float PuckRadius
        {
            get
            {
                Collider c = PuckCollider;
                if (c == null) return 0.2f;
                Bounds b = c.bounds;
                return Mathf.Max(b.extents.x, b.extents.z);
            }
        }

        /// <summary>The stack collider the puck actually made its first contact with. Null until then.
        /// The gate compares this against GreenBlockShatter.PrimaryImpactCollider: if the puck's real
        /// solver contact is not the block the approach filter selected, the filter picked the wrong
        /// block and the shot is passing through the stack.</summary>
        public Collider ImpactCollider { get { return _impactCollider; } }

        /// <summary>Normal component of relative velocity on the first stack contact.</summary>
        public float ImpactNormalSpeed { get { return _impactNormalSpeed; } }

        /// <summary>Direction the puck was moving in on the frame the stack was hit.</summary>
        public Vector3 ImpactDirection
        {
            get
            {
                Vector3 d = _stackHit ? _impactDirection : _lastDir;
                d.y = 0f;
                return d.sqrMagnitude < 0.0001f ? Vector3.left : d.normalized;
            }
        }

        /// <summary>Exact first stack contact point, falling back to the current puck position before impact.</summary>
        public Vector3 ImpactPoint { get { return _stackHit ? _impactPoint : (puck != null ? puck.position : transform.position); } }

        /// <summary>
        /// The render child the coin is actually drawn as - the same transform <see cref="visualPadLift"/>
        /// moves. Exposed so the gate can measure where the COIN is rather than where the body is: the
        /// two are different objects and the lift is what separates them.
        /// </summary>
        public Transform Visual { get { return _visual; } }

        /// <summary>
        /// The puck's contact patch - the small dark ellipse parented under it that grounds it. The gate
        /// measures the coin against this rather than against the floor plane, because this is the thing
        /// a floating coin visibly detaches from.
        /// </summary>
        public Transform ContactPatch
        {
            get
            {
                if (puck == null) return null;
                return puck.Find("ReferenceContactShadow");
            }
        }

        /// <summary>
        /// The imported start disc, resolved by material. Null means the lookup found nothing, which is
        /// itself worth reporting: <see cref="HidePad"/> is a silent no-op in that case.
        /// </summary>
        public Renderer PadRenderer { get { return _padRenderer != null ? _padRenderer : FindPadRenderer(); } }

        void Awake()
        {
            // Awake only runs on a real scene load, so this is the one moment the authored transform
            // is definitely the rest pose. A flag left true in the scene asset by an earlier session
            // must not survive into a session where Case4SceneSetup has moved the puck.
            _restCaptured = false;
            EnsureInitialised();
            Hold();
        }

        /// <summary>
        /// Establishes everything Awake normally establishes, and is safe to call again at any time.
        /// It exists because Awake is not guaranteed to be the last word: Unity recompiles scripts and
        /// reloads the domain <i>while the capture harness is in play mode</i>, which destroys every
        /// object built at runtime (both PhysicsMaterials here) and blanks references the serializer
        /// does not carry. Awake never runs again, so without this the second and every later run of a
        /// capture is a different simulation from the first. Returns true if anything had to be healed.
        /// </summary>
        public bool EnsureInitialised()
        {
            bool healed = false;
            if (puck == null) return false;

            if (_rb == null)
            {
                _rb = puck.GetComponent<Rigidbody>();
                healed = true;
            }
            if (_rb != null && !_rb.detectCollisions) { _rb.detectCollisions = true; healed = true; }

            if (!_restCaptured)
            {
                _restPos = puck.position;
                _restRot = puck.rotation;
                _bodyScale = puck.localScale;
                _visual = ResolveVisual();                                  // cached before the trail is parented in
                _restScale = _visual != null ? _visual.localScale : puck.localScale;
                _visualRestLocalPos = _visual != null ? _visual.localPosition : Vector3.zero;
                _visualSeatLift = MeasureSeatLift();
                _restCaptured = true;
                _shotOrigin = _restPos;
                _shotOriginSet = true;
            }
            else if (_visual == null)
            {
                // The rest scale is already known; re-reading it here could bake in a live squash.
                _visual = ResolveVisual();
                healed = true;
            }

            // Both materials are created with `new PhysicsMaterial(...)`. A domain reload destroys
            // them and leaves the puck and every rail collider pointing at a dead object, so the
            // near-elastic rail response the reference shot depends on quietly disappears.
            if (_physicsMaterial == null) { ApplyBouncePhysics(); healed = true; }
            if (_railPhysicsMaterial == null) { ApplyRailBouncePhysics(); healed = true; }

            if (_visual != null)
            {
                Vector3 want = _visualRestLocalPos + Vector3.up * (visualPadLift + _visualSeatLift);
                if ((_visual.localPosition - want).sqrMagnitude > 1e-8f)
                {
                    _visual.localPosition = want;
                    healed = true;
                    Debug.Log(string.Format(
                        "[Case4] COIN_SEAT visual local y {0:0.000} -> {1:0.000} (padLift={2:0.000} + " +
                        "measured seat={3:0.000}); coin now sits at world y={4:0.000}",
                        _visualRestLocalPos.y, want.y, visualPadLift, _visualSeatLift, _visual.position.y));
                }
            }

            if (_padRenderer == null)
            {
                _padRenderer = FindPadRenderer();
                if (_padRenderer != null) healed = true;
            }
            // Re-asserted here rather than only at build time: a domain reload mid-capture restores the
            // renderer's serialised enabled flag, and the disc would come back for the rest of the run.
            HidePad();

            PuckCollisionRelay relay = puck.GetComponent<PuckCollisionRelay>();
            if (relay == null)
            {
                relay = puck.gameObject.AddComponent<PuckCollisionRelay>();
                healed = true;
            }
            if (relay.owner != this) { relay.owner = this; healed = true; }

            if (_trail == null || _glowTrail == null) { BuildTrail(); healed = true; }

            // The rail/divider cache is built once and was never invalidated. A mid-playmode domain
            // reload destroys the colliders it holds; PushOutOfWalls skips every null entry, so
            // de-penetration silently stopped working while every log line still read normal. This is
            // the same class of decay the two PhysicsMaterials above are healed for, so it is healed
            // in the same place.
            if (RefreshWallCacheIfStale()) healed = true;

            return healed;
        }

        /// <summary>
        /// Drops the wall cache if any entry has been destroyed, so the next PushOutOfWalls rebuilds
        /// it against live colliders. Returns true if the cache was dropped.
        /// </summary>
        bool RefreshWallCacheIfStale()
        {
            if (_wallColliders == null) return false;
            for (int i = 0; i < _wallColliders.Length; i++)
            {
                if (_wallColliders[i] != null) continue;
                Debug.LogWarning("[Case4] WALL_CACHE_STALE entry " + i + " of " + _wallColliders.Length +
                                 " was destroyed; rebuilding on next use");
                _wallColliders = null;
                return true;
            }
            return false;
        }

        /// <summary>
        /// How far the render child has to move, in its own local Y, for the drawn coin's underside to
        /// land on the top of the contact patch it is supposed to be resting on. Read off both
        /// Renderers' world bounds, so it shares no arithmetic with whatever placed either of them.
        /// Zero if either renderer is missing - a missing patch is not a reason to move the coin.
        /// </summary>
        float MeasureSeatLift()
        {
            if (_visual == null || puck == null) return 0f;
            Renderer coin = _visual.GetComponent<Renderer>();
            Transform patch = ContactPatch;
            Renderer patchRenderer = patch != null ? patch.GetComponent<Renderer>() : null;
            if (coin == null || patchRenderer == null) return 0f;

            float deltaWorld = patchRenderer.bounds.max.y - coin.bounds.min.y;
            float scale = Mathf.Abs(puck.lossyScale.y);
            if (scale < 0.0001f) return 0f;
            return deltaWorld / scale;
        }

        /// <summary>
        /// The start pad, found by the material Case4SceneSetup puts on it. It lives inside the
        /// imported art rather than as a scene object of ours, so there is nothing to serialise a
        /// reference to; the same name-and-material lookup the setup uses works at runtime.
        /// </summary>
        Renderer FindPadRenderer()
        {
            Renderer[] all = FindObjectsByType<Renderer>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            for (int i = 0; i < all.Length; i++)
            {
                Material m = all[i].sharedMaterial;
                if (m == null || !m.name.StartsWith("Case4_DiscPad")) continue;

                // Never the puck's own renderer. The lookup is by MATERIAL, so the moment the puck
                // was given the disc's material - which is what the owner asked for - this found the
                // puck and HidePad switched it off on entering play: "play baslamayinca puck var
                // baslayinca kayboluyor". A material is not an identity; the puck's hierarchy is.
                if (puck != null && all[i].transform.IsChildOf(puck)) continue;

                return all[i];
            }
            return null;
        }

        /// <summary>
        /// Keeps the imported start disc from ever rendering. The earlier reading - that the reference
        /// lights a pad at rest - was wrong: the 303 dark pixels the reference holds in the rest box
        /// (y 1040..1180, x 790..960) at n=1 belong to the PUCK, which is itself a dark torus with a
        /// gold centre, and they leave with it (0 dark, 0 gold in that box from n=37 on). There is no
        /// pad under it at any point. Ours had a real second object: with the pad shown until Launch,
        /// the same box measured 1222 dark pixels at frame 30 and 1187 at frame 50, once the idle lift
        /// had carried the puck clear of the disc and exposed it. That is the owner's "extra round
        /// object". Hiding it on launch was a half fix - it was visible for the whole pull-back.
        /// </summary>
        void HidePad()
        {
            if (_padRenderer == null) _padRenderer = FindPadRenderer();
            if (_padRenderer != null) _padRenderer.enabled = false;
        }

        /// <summary>The render child, skipping the trail objects this component parents onto the puck.</summary>
        Transform ResolveVisual()
        {
            if (puck == null) return null;
            for (int i = 0; i < puck.childCount; i++)
            {
                Transform child = puck.GetChild(i);
                if (child == null) continue;
                if (child.name == "PuckStarTrail" || child.name == "PuckGlowTrail") continue;
                return child;
            }
            return null;
        }

        /// <summary>
        /// The puck itself is deliberately LOW-bounce.  Rail elasticity lives on the rail colliders,
        /// not on the puck.  This matters because PhysicsMaterial combine modes are pair-wise: a
        /// Maximum/0.98 puck would also make puck-vs-stack contacts nearly elastic and the green pile
        /// would jump like rubber.  With this split, rail pairs choose the rail's Maximum bounce while
        /// stack pairs choose the block's Minimum bounce.
        /// </summary>
        void ApplyBouncePhysics()
        {
            Collider col = puck.GetComponent<Collider>();
            if (col == null) return;

            PhysicsMaterial mat = new PhysicsMaterial("Case4_PuckContact");
            mat.bounciness = 0.025f;
            mat.dynamicFriction = 0f;
            mat.staticFriction = 0f;
            mat.bounceCombine = PhysicsMaterialCombine.Average;
            mat.frictionCombine = PhysicsMaterialCombine.Minimum;
            col.sharedMaterial = mat;
            _physicsMaterial = mat;
        }

        /// <summary>
        /// Installs a separate near-elastic surface on the invisible rail/divider colliders built by
        /// Case4SceneSetup.  Runtime assignment avoids serialising a generated PhysicsMaterial asset
        /// while still giving rail contacts a very different response from stack contacts.
        /// </summary>
        void ApplyRailBouncePhysics()
        {
            GameObject root = GameObject.Find("Case4_Colliders");
            if (root == null) return;

            _railPhysicsMaterial = new PhysicsMaterial("Case4_RailBounce");
            _railPhysicsMaterial.bounciness = 0.965f;
            _railPhysicsMaterial.dynamicFriction = 0f;
            _railPhysicsMaterial.staticFriction = 0f;
            _railPhysicsMaterial.bounceCombine = PhysicsMaterialCombine.Maximum;
            _railPhysicsMaterial.frictionCombine = PhysicsMaterialCombine.Minimum;

            Collider[] colliders = root.GetComponentsInChildren<Collider>(true);
            for (int i = 0; i < colliders.Length; i++)
            {
                Collider c = colliders[i];
                if (c == null) continue;
                string n = c.gameObject.name;
                // Floor never needs to redirect the puck (Y is constrained); everything else is a rail.
                if (n == "Floor") continue;
                c.sharedMaterial = _railPhysicsMaterial;
            }
        }

        void BuildTrail()
        {
            // Re-adopt trails that already exist before building any: EnsureInitialised can run after a
            // domain reload that blanked the references but left the child objects on the puck, and a
            // second Instantiate would double the emission rate of an effect the reference match tunes.
            if (_trail == null && puck != null)
            {
                Transform existing = puck.Find("PuckStarTrail");
                if (existing != null) _trail = existing.GetComponent<ParticleSystem>();
            }
            if (_glowTrail == null && puck != null)
            {
                Transform existingGlow = puck.Find("PuckGlowTrail");
                if (existingGlow != null) _glowTrail = existingGlow.GetComponent<ParticleSystem>();
            }

            if (_trail != null || starTrailPrefab == null || puck == null) return;

            float d = ResolveDrawnDiameter();
            float speed = Mathf.Max(1f, launchSpeed);

            // ---------------------------------------------------------------- droplets
            // Round, soft, and of MANY sizes. The four-pointed star sprite is what made ours read
            // as an authored marker rather than as thrown-off material, so this layer takes the
            // soft-circle material too; the two layers now differ by size, lifetime and colour
            // ramp, which is what separates them in the reference as well.
            GameObject go = Instantiate(starTrailPrefab, puck);
            go.name = "PuckStarTrail";
            go.transform.localPosition = Vector3.zero;
            go.transform.localRotation = Quaternion.identity;
            go.transform.localScale = Vector3.one;   // sizes below are world units, not scaled
            _trail = go.GetComponent<ParticleSystem>();

            ParticleSystemRenderer dropRenderer = go.GetComponent<ParticleSystemRenderer>();
            if (dropRenderer != null)
            {
                Material round = ResolveRoundTrailMaterial();
                if (round != null) dropRenderer.sharedMaterial = round;
                Debug.Log("[Case4] TRAIL_DROPLET_MATERIAL " +
                          (round != null ? round.name : "<none - the prefab's own sprite is being drawn>") +
                          " (trailGlowMaterial wired=" + (trailGlowMaterial != null) + ")");
            }

            if (_trail != null)
            {
                // World space or the droplets travel with the puck and there is no trail at all.
                ParticleSystem.MainModule main = _trail.main;
                main.simulationSpace = ParticleSystemSimulationSpace.World;
                main.playOnAwake = false;

                // A droplet's age IS its distance behind the puck: the system is in world space and
                // the puck is the emitter, so a particle of age t sits speed*t behind. The furthest
                // droplet the reference draws is 4.45 puck diameters back, so that is the longest
                // life; the shortest is set so droplets keep arriving over the whole span rather
                // than all expiring together.
                float k = ResolveForeshortening();
                float farLife = dropletSpanInDiameters.y * k * d / speed;
                float nearLife = Mathf.Max(0.02f, dropletSpanInDiameters.x * k * d / speed);
                main.startLifetime = new ParticleSystem.MinMaxCurve(nearLife, farLife);

                // The 4.7x size spread is the point. One size is what a repeated sprite looks like.
                // NOT multiplied by trailScale. Every size here is already stated as a fraction of
                // the puck's own diameter, which is what trailScale used to stand in for; leaving
                // both in place meant the measured 0.037..0.171 d rendered at 0.75 of itself and
                // the numbers in the comment stopped describing the pixels.
                main.startSize = new ParticleSystem.MinMaxCurve(
                    dropletSizeInDiameters.x * d,
                    dropletSizeInDiameters.y * d);
                main.startColor = new Color(1f, 0.90f, 0.55f, 1f);
                // Spacing, not just spread. A droplet's distance behind the puck is its age times
                // the puck's speed, so a CONSTANT emission rate at a constant speed puts them at
                // perfectly even intervals - which is the tidy vertical comb of identical marks the
                // owner keeps pointing at. The sphere shape already randomises the DIRECTION; what
                // was missing was enough magnitude for the along-track component to scatter them
                // out of that comb. The range now spans an order of magnitude, so neighbours differ
                // in both their offset along the line and their offset across it.
                float scatterSpeed = dropletScatterInDiameters * d / Mathf.Max(0.01f, farLife);
                main.startSpeed = new ParticleSystem.MinMaxCurve(0.4f * scatterSpeed, 3.2f * scatterSpeed);
                main.gravityModifier = 0f;

                // Both verification passes must emit the same droplets in the same places. A local
                // fixed seed keeps this effect deterministic without touching UnityEngine.Random.
                _trail.useAutoRandomSeed = false;
                _trail.randomSeed = 0xC4A11u;

                // Held count, not a rate: the reference frame has 9..13 droplets in the air, and
                // that is the number the eye reads. Rate = count / mean life.
                ParticleSystem.EmissionModule em = _trail.emission;
                em.rateOverTime = dropletCount / Mathf.Max(0.01f, 0.5f * (nearLife + farLife));

                // Scatter off the flight line. A sphere, not the prefab's narrow cone: the
                // reference's droplets sit on BOTH sides of the line and at every distance from it,
                // which a forward cone cannot produce.
                ParticleSystem.ShapeModule shape = _trail.shape;
                shape.enabled = true;
                shape.shapeType = ParticleSystemShapeType.Sphere;
                shape.radius = Mathf.Max(0.01f, 0.16f * d);
                shape.radiusThickness = 1f;

                // Brightness falls with distance: the far droplets in the reference sample within a
                // few units of the floor colour, the near ones are near-white gold.
                ParticleSystem.ColorOverLifetimeModule col = _trail.colorOverLifetime;
                col.enabled = true;
                col.color = new ParticleSystem.MinMaxGradient(WarmDropletGradient());

                ParticleSystem.SizeOverLifetimeModule sz = _trail.sizeOverLifetime;
                sz.enabled = true;
                sz.size = new ParticleSystem.MinMaxCurve(1f, DropletSizeCurve());
            }

            // ---------------------------------------------------------------- streak
            // The reference's streak is a short, WIDE, warm flare welded to the puck, not a long
            // thin line and not the white bloom ours drew. It is built from the same soft circle,
            // emitted fast enough that consecutive particles overlap into something continuous, and
            // ramped down in size so the shape tapers to a point behind the puck.
            if (ResolveRoundTrailMaterial() != null)
            {
                GameObject glowGo = Instantiate(starTrailPrefab, puck);
                glowGo.name = "PuckGlowTrail";
                glowGo.transform.localPosition = Vector3.zero;
                glowGo.transform.localRotation = Quaternion.identity;
                glowGo.transform.localScale = Vector3.one;
                _glowTrail = glowGo.GetComponent<ParticleSystem>();
                ParticleSystemRenderer glowRenderer = glowGo.GetComponent<ParticleSystemRenderer>();
                if (glowRenderer != null) glowRenderer.sharedMaterial = ResolveRoundTrailMaterial();

                if (_glowTrail != null)
                {
                    // Authored quad width. It is NOT the width that comes back off a frame, and
                    // the correction goes the opposite way to the obvious one: fx_softcircle's own
                    // alpha*luminance is above a tenth of its peak across only 0.684 of its 256 px,
                    // which argued for a 1.46x WIDER quad - but the material is additive and the
                    // pipeline blooms it, so a quad authored at 0.994 d measured back at 1.69 d
                    // against the reference streak's 1.07 d. The field below is therefore calibrated
                    // against the CAPTURE, not against the texture, and no divisor is applied here.
                    float streakWidth = Mathf.Max(0.02f, streakWidthInDiameters * d);
                    float streakLife = Mathf.Max(0.02f,
                        streakLengthInDiameters * ResolveForeshortening() * d / speed);

                    ParticleSystem.MainModule glowMain = _glowTrail.main;
                    glowMain.simulationSpace = ParticleSystemSimulationSpace.World;
                    glowMain.playOnAwake = false;
                    glowMain.startLifetime = streakLife;
                    // startColor MULTIPLIES the colour-over-lifetime ramp, so anything but white
                    // here quietly re-tints every key that was just fitted to the reference. It used
                    // to be (1, 0.93, 0.72), which pulled 28% of the blue out of the birth key and
                    // then kept pulling it out of the brown tail as well. The ramp owns the colour.
                    glowMain.startColor = Color.white;
                    glowMain.startSize = new ParticleSystem.MinMaxCurve(streakWidth * 0.90f, streakWidth);
                    glowMain.startSpeed = 0f;                                 // the streak is the puck's own path
                    glowMain.gravityModifier = 0f;

                    _glowTrail.useAutoRandomSeed = false;
                    _glowTrail.randomSeed = 0xC4A12u;

                    // Continuity condition, not a tuned number: consecutive puffs are speed/rate
                    // apart, and they read as one streak only while that gap is small against the
                    // puff. Six samples per puff width is the smallest that left no beading in the
                    // capture.
                    // THE BANDING. Consecutive puffs are speed/rate apart and each is streakWidth
                    // across, so the seams are invisible only while that ratio is small. At the
                    // shipped 6 the gap was a sixth of a puff - 0.137 world units against a 0.82
                    // unit puff at the launch speed - and the capture shows exactly that: a chain of
                    // discrete blobs, which is the "hard horizontal steps" in the owner's crop. The
                    // condition is samples per PUFF WIDTH, so it holds at any width; 16 puts the
                    // seam spacing at about 2.6 capture pixels at the launch speed, where spacing is
                    // worst, which is below what a soft edge can show.
                    ParticleSystem.EmissionModule glowEmission = _glowTrail.emission;
                    glowEmission.rateOverTime = SamplesPerPuffWidth * speed / streakWidth;

                    ParticleSystem.ShapeModule glowShape = _glowTrail.shape;
                    glowShape.enabled = true;
                    glowShape.shapeType = ParticleSystemShapeType.Sphere;
                    glowShape.radius = streakWidth * 0.12f;
                    glowShape.radiusThickness = 1f;

                    ParticleSystem.ColorOverLifetimeModule glowCol = _glowTrail.colorOverLifetime;
                    glowCol.enabled = true;
                    glowCol.color = new ParticleSystem.MinMaxGradient(WarmStreakGradient());

                    ParticleSystem.SizeOverLifetimeModule glowSize = _glowTrail.sizeOverLifetime;
                    glowSize.enabled = true;
                    glowSize.size = new ParticleSystem.MinMaxCurve(1f, StreakTaperCurve());
                }
            }
            SetTrail(false);
        }

        Material _fallbackRoundTrail;

        /// <summary>
        /// The soft ROUND sprite both trail layers draw with.
        ///
        /// <para>CHECKED, because the run I inherited had concluded the opposite and built this
        /// fallback on it: in Buca.unity the field IS wired, to PFX_BucaSoft (guid
        /// c4b10000f100f000000000000000c00a), whose _BaseMap is fx_softcircle.png - and that texture
        /// is a true radial falloff, alpha 251 at the centre falling smoothly to 0, isotropic to
        /// within 2/255 all the way round at r = 0.45. The four-pointed star, fx_star4.png, is
        /// alpha 255 on the axes and 0 on the diagonals, and it reaches the puck only through
        /// StarTrail.prefab's own renderer slot, which is overwritten below. So the stars in the
        /// owner's crop are from the tree BEFORE af2ae78; our own capture at 07:23 already draws
        /// round droplets.</para>
        ///
        /// <para>The fallback is kept anyway, and only for this: assigning the prefab's slot is the
        /// single thing standing between a wired circle and the star, and if that field is ever
        /// cleared the star comes back with no error and no log. It is insurance against a silent
        /// regression of a defect the owner has already reported, not a diagnosis of the current
        /// one.</para>
        /// </summary>
        Material ResolveRoundTrailMaterial()
        {
            if (trailGlowMaterial != null) return trailGlowMaterial;
            if (_fallbackRoundTrail != null) return _fallbackRoundTrail;

            Shader sh = Shader.Find("Universal Render Pipeline/Particles/Unlit");
            if (sh == null) sh = Shader.Find("Universal Render Pipeline/Unlit");
            if (sh == null) return null;

            // A radial falloff generated here rather than looked up, so the fallback cannot itself
            // depend on an asset reference that might also be missing.
            const int N = 64;
            Texture2D tex = new Texture2D(N, N, TextureFormat.RGBA32, false);
            tex.name = "Case4_FallbackSoftCircle";
            tex.wrapMode = TextureWrapMode.Clamp;
            Color[] px = new Color[N * N];
            for (int y = 0; y < N; y++)
            {
                for (int x = 0; x < N; x++)
                {
                    float dx = (x + 0.5f) / N * 2f - 1f;
                    float dy = (y + 0.5f) / N * 2f - 1f;
                    float r = Mathf.Sqrt(dx * dx + dy * dy);
                    float a = Mathf.Clamp01(1f - r);
                    a = a * a * (3f - 2f * a);
                    px[y * N + x] = new Color(1f, 1f, 1f, a);
                }
            }
            tex.SetPixels(px);
            tex.Apply();

            _fallbackRoundTrail = new Material(sh);
            _fallbackRoundTrail.name = "Case4_FallbackRoundTrail";
            if (_fallbackRoundTrail.HasProperty("_BaseMap")) _fallbackRoundTrail.SetTexture("_BaseMap", tex);
            if (_fallbackRoundTrail.HasProperty("_MainTex")) _fallbackRoundTrail.SetTexture("_MainTex", tex);
            _fallbackRoundTrail.SetFloat("_Surface", 1f);
            _fallbackRoundTrail.SetFloat("_Blend", 2f);
            _fallbackRoundTrail.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            _fallbackRoundTrail.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.One);
            _fallbackRoundTrail.SetFloat("_ZWrite", 0f);
            _fallbackRoundTrail.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            _fallbackRoundTrail.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;

            Debug.LogWarning("[Case4] TRAIL_MATERIAL_UNWIRED trailGlowMaterial is null. In the authored " +
                             "scene it is wired to PFX_BucaSoft, so this is a REGRESSION, not the " +
                             "normal path: a round soft-circle material was generated at runtime to " +
                             "keep the prefab's four-pointed star off the screen. RE-WIRE THE FIELD.");
            return _fallbackRoundTrail;
        }

        /// <summary>
        /// How many world units along the puck's CURRENT heading it takes to cover one puck diameter
        /// on screen, divided by how many it takes across the view. 1 means the heading runs square
        /// across the frame and a world length draws at face value; larger means the heading runs
        /// away from the camera and a world length is foreshortened before it reaches the frame.
        ///
        /// <para>Every trail length in this file was READ OFF A FRAME, so each is a screen length,
        /// and turning one into a particle lifetime needs this factor. It used to be the shipped
        /// constant <see cref="screenToWorldAlongFlight"/> = 2.7, solved once against the reference
        /// bank's opening leg, which runs away from the camera. The puck ricochets: most legs of a
        /// real shot run across the frame, where the true factor is close to 1, and the trail was
        /// therefore drawn 2.7x longer than it was solved to be on every one of them. Measured on
        /// after_flight.png - a leg running across frame - our streak was still 0.9 puck diameters
        /// wide and 108 L above the floor at 5.0 diameters behind the puck, where the reference has
        /// nothing at all past 1.25.</para>
        /// </summary>
        public float ResolveForeshortening()
        {
            Camera cam = Camera.main;
            if (cam == null) cam = FindFirstObjectByType<Camera>(FindObjectsInactive.Include);
            if (cam == null || puck == null) return Mathf.Max(0.1f, screenToWorldAlongFlight);

            Vector3 heading = _lastDir;
            heading.y = 0f;
            if (heading.sqrMagnitude < 0.0001f) heading = referenceAimDir;
            heading.y = 0f;
            if (heading.sqrMagnitude < 0.0001f) return Mathf.Max(0.1f, screenToWorldAlongFlight);
            heading.Normalize();

            const float Probe = 1f;
            Vector3 origin = puck.position;
            Vector3 s0 = cam.WorldToScreenPoint(origin);
            if (s0.z <= 0.01f) return Mathf.Max(0.1f, screenToWorldAlongFlight);

            Vector3 sAlong = cam.WorldToScreenPoint(origin + heading * Probe);
            Vector3 across = cam.transform.right;
            across.y = 0f;
            if (across.sqrMagnitude < 0.0001f) across = Vector3.right;
            across.Normalize();
            Vector3 sAcross = cam.WorldToScreenPoint(origin + across * Probe);
            if (sAlong.z <= 0.01f || sAcross.z <= 0.01f) return Mathf.Max(0.1f, screenToWorldAlongFlight);

            float pxAlong = new Vector2(sAlong.x - s0.x, sAlong.y - s0.y).magnitude;
            float pxAcross = new Vector2(sAcross.x - s0.x, sAcross.y - s0.y).magnitude;
            if (pxAlong < 0.5f || pxAcross < 0.5f) return Mathf.Max(0.1f, screenToWorldAlongFlight);

            // Clamped, because a heading pointing almost straight at the camera sends this to
            // infinity and a single frame of that would draw a trail across the whole arena.
            return Mathf.Clamp(pxAcross / pxAlong, 0.35f, 4.0f);
        }

        /// <summary>
        /// Re-solves the trail's lifetimes against the heading and the speed the puck has RIGHT NOW.
        /// Particle lifetime is fixed at birth, so writing it every frame is what makes the trail the
        /// same length on screen after a ricochet as it was before one - and what keeps it from
        /// stretching out behind a puck that has slowed down.
        /// </summary>
        void UpdateTrailForHeading()
        {
            if (_trail == null && _glowTrail == null) return;
            float d = ResolveDrawnDiameter();
            float speed = Mathf.Max(1f, Speed);
            float k = ResolveForeshortening();

            if (_glowTrail != null)
            {
                ParticleSystem.MainModule glowMain = _glowTrail.main;
                glowMain.startLifetime = Mathf.Max(0.02f, streakLengthInDiameters * k * d / speed);
            }
            if (_trail != null)
            {
                float farLife = Mathf.Max(0.03f, dropletSpanInDiameters.y * k * d / speed);
                float nearLife = Mathf.Max(0.02f, dropletSpanInDiameters.x * k * d / speed);
                ParticleSystem.MainModule main = _trail.main;
                main.startLifetime = new ParticleSystem.MinMaxCurve(nearLife, farLife);
                ParticleSystem.EmissionModule em = _trail.emission;
                em.rateOverTime = dropletCount / Mathf.Max(0.01f, 0.5f * (nearLife + farLife));
            }
        }

        /// <summary>
        /// World diameter of the DRAWN puck, read off the renderer rather than trusted from a
        /// field: every trail length below is a multiple of it, and a puck that is later resized
        /// must drag its trail along or the two stop matching.
        /// </summary>
        float ResolveDrawnDiameter()
        {
            Transform visual = _visual != null ? _visual : ResolveVisual();
            if (visual != null)
            {
                Renderer r = visual.GetComponent<Renderer>();
                if (r != null)
                {
                    Vector3 sz = r.bounds.size;
                    float measured = Mathf.Max(sz.x, sz.z);
                    if (measured > 0.05f) return measured;
                }
            }
            return Mathf.Max(0.05f, puckDrawnDiameter);
        }

        /// <summary>
        /// Streak colour walk, read off Buca.mp4 n=30 along the streak's centreline:
        /// (253,250,173) at the puck, (250,198,162) and (243,180,153) through the body,
        /// (188,150,145) at the tail, floor beyond. Pale and hot into saturated orange into warm
        /// grey - our old streak started at (255,250,199) and simply faded, which is why it read
        /// white.
        /// </summary>
        /// <summary>
        /// The streak ramp, for the gate. The gate has to read the ramp the game actually installs
        /// rather than a copy of it: a copy would keep passing after the real one was changed, which
        /// is the failure mode the whole exercise is trying to avoid.
        /// </summary>
        public static Gradient StreakGradientForGate() { return WarmStreakGradient(); }

        static Gradient WarmStreakGradient()
        {
            // FITTED TO THE REFERENCE'S OWN SPINE, not chosen. The material is additive over the
            // arena floor, whose measured colour is (98,113,127), so what lands on the frame is
            // floor + contribution and the authored colour is only half of the answer. Reading the
            // reference's brightest warm pixel across the plume at each distance behind the puck,
            // and subtracting that floor, gives the contribution the sprite has to make:
            //
            //   behind   reference sRGB      contribution      reads as
            //   0.25 d   (253, 252, 113)     (155, 139,   0)   white-YELLOW, G almost equal to R
            //   0.50 d   (252, 186, 107)     (154,  73,   0)   saturated orange, G half of R
            //   0.75 d   (208, 138,  95)     (110,  25,   0)   deep orange
            //   1.25 d   (229, 172, 151)     (131,  59,  24)   burnt
            //   1.50 d   (186, 147, 143)     ( 88,  34,  16)   brown, going out
            //
            // The blue contribution is ZERO the whole way down: the reference's B never rises above
            // the floor's own 127 until the very end. Ours measured (254, 200, 163) - a +36 blue
            // contribution - held FLAT from 1.0 d to 2.5 d. That flat pale-peach body, not the
            // birth colour, is the "cold" the owner keeps pointing at, and it is why warming only
            // the first key in the previous rounds changed nothing he could see.
            //
            // So: B is pinned near zero, G falls from 0.90 of R to 0.20 of R across the first half
            // of the life, and the last key is the reference's brown. Alpha reaches zero at 0.88
            // rather than 1.00 so the streak actually ENDS.
            Gradient g = new Gradient();
            g.SetKeys(
                new GradientColorKey[]
                {
                    new GradientColorKey(new Color(1.000f, 0.900f, 0.030f), 0.00f),   // white-yellow, at the puck
                    new GradientColorKey(new Color(1.000f, 0.560f, 0.010f), 0.20f),   // gold
                    new GradientColorKey(new Color(1.000f, 0.280f, 0.000f), 0.45f),   // saturated orange body
                    new GradientColorKey(new Color(0.780f, 0.240f, 0.060f), 0.72f),   // burnt
                    new GradientColorKey(new Color(0.430f, 0.180f, 0.070f), 1.00f),   // brown tail
                },
                new GradientAlphaKey[]
                {
                    new GradientAlphaKey(1.00f, 0.00f),
                    new GradientAlphaKey(0.92f, 0.30f),
                    new GradientAlphaKey(0.68f, 0.62f),
                    new GradientAlphaKey(0.30f, 0.82f),
                    new GradientAlphaKey(0.06f, 0.94f),
                    new GradientAlphaKey(0.00f, 1.00f),
                });
            return g;
        }

        /// <summary>
        /// Droplet colour walk. Sampled at the reference's droplet centroids: (252,175,95) and
        /// (192,185,145) near the puck, (165,155,141) and (139,145,146) far out - warm gold
        /// desaturating toward the floor rather than a constant gold that just switches off.
        /// </summary>
        static Gradient WarmDropletGradient()
        {
            Gradient g = new Gradient();
            g.SetKeys(
                new GradientColorKey[]
                {
                    // MEASURED, and this one was already close: the reference's seven countable
                    // droplets peak at R-B 28..87 (mean 56) and ours at 84..95, so ours are if
                    // anything MORE saturated than the reference's, which read (238,217,151) and
                    // (236,218,161) - a pale gold with G high, not a deep orange. The correction
                    // that was needed is at the END: the old 0.70 and 1.00 keys had R, G and B
                    // within 0.06 of each other, which over the floor is a grey speck rather than a
                    // cooling ember. G is lifted toward the reference's pale gold at birth and blue
                    // held down through the tail.
                    new GradientColorKey(new Color(1.000f, 0.860f, 0.330f), 0.00f),
                    new GradientColorKey(new Color(1.000f, 0.720f, 0.230f), 0.35f),
                    new GradientColorKey(new Color(0.880f, 0.520f, 0.150f), 0.70f),
                    new GradientColorKey(new Color(0.600f, 0.320f, 0.100f), 1.00f),
                },
                new GradientAlphaKey[]
                {
                    new GradientAlphaKey(0.00f, 0.00f),   // born inside the streak, invisible there
                    new GradientAlphaKey(1.00f, 0.15f),
                    new GradientAlphaKey(0.95f, 0.55f),
                    new GradientAlphaKey(0.55f, 0.80f),
                    new GradientAlphaKey(0.00f, 1.00f),
                });
            return g;
        }

        /// <summary>Droplets shrink as they fall behind; the far ones in the reference are a fifth
        /// the diameter of the near ones on top of their own spread.</summary>
        static AnimationCurve DropletSizeCurve()
        {
            AnimationCurve c = new AnimationCurve();
            c.AddKey(new Keyframe(0.00f, 0.55f));
            c.AddKey(new Keyframe(0.20f, 1.00f));
            c.AddKey(new Keyframe(1.00f, 0.30f));
            return c;
        }

        /// <summary>Streak taper: full width where it leaves the puck, gone by the tail. This is
        /// what turns a line of equal puffs into the reference's flare.</summary>
        static AnimationCurve StreakTaperCurve()
        {
            // MEASURED off the reference, and it is MONOTONIC - it does not pinch and re-open.
            // Warm width (R-B > 30) at 0.25 / 0.50 / 0.75 / 1.00 / 1.25 / 1.50 / 1.75 d behind is
            // 1.50 / 1.44 / 1.36 / 1.22 / 1.06 / 0.54 / 0.14 puck diameters. Divided by the width
            // at the source, and with the distance expressed as a fraction of the 1.75 d life:
            //
            //   life  0.14  0.29  0.43  0.57  0.71  0.86  1.00
            //   width 1.00  0.96  0.91  0.81  0.71  0.36  0.09
            //
            // A slow taper that holds most of its width for two thirds of the life, then collapses.
            // The run I inherited put a bump at 0.55 to make the plume "flare", from widths read on
            // LUMINANCE, where the cyan rail beside the reference plume clears the threshold and
            // fakes a second opening. There is no bump in the warmth profile. The flare the owner
            // describes is not a bulge in the middle - it is that the plume leaves the puck 1.5x
            // the puck's own width, which streakWidthInDiameters is what delivers.
            AnimationCurve c = new AnimationCurve();
            c.AddKey(new Keyframe(0.00f, 1.00f));
            c.AddKey(new Keyframe(0.29f, 0.96f));
            c.AddKey(new Keyframe(0.57f, 0.81f));
            c.AddKey(new Keyframe(0.71f, 0.71f));
            c.AddKey(new Keyframe(0.86f, 0.36f));
            c.AddKey(new Keyframe(1.00f, 0.09f));
            return c;
        }

        // ------------------------------------------------------------------ physics state

        /// <summary>Parks the puck on its disc with no velocity; it stays there until it is launched.</summary>
        public void Hold()
        {
            if (_rb == null) return;
            if (!_rb.isKinematic)
            {
                _rb.linearVelocity = Vector3.zero;
                _rb.angularVelocity = Vector3.zero;
            }
            _rb.linearDamping = 0f;
            _rb.Sleep();
            _flying = false;
            HidePad();
        }

        /// <summary>Puts the puck back on its start disc with a clean transform and no motion.</summary>
        public void ResetInstant()
        {
            if (puck == null) return;
            if (_postImpactGlide != null) StopCoroutine(_postImpactGlide);
            _postImpactGlide = null;
            Squash.Cancel(Deformable);
            Deformable.localScale = _restScale;
            if (_rb != null)
            {
                if (!_rb.isKinematic)
                {
                    _rb.linearVelocity = Vector3.zero;
                    _rb.angularVelocity = Vector3.zero;
                }
                _rb.isKinematic = true;
                _rb.linearDamping = 0f;
                _rb.position = _restPos;
                _rb.rotation = _restRot;
            }
            puck.SetPositionAndRotation(_restPos, _restRot);
            puck.localScale = _bodyScale;   // the collider is on the body; its size must never drift
            Collider puckCollider = PuckCollider;
            if (puckCollider != null) puckCollider.enabled = true;
            Physics.SyncTransforms();
            if (_rb != null) _rb.isKinematic = false;
            _bounces = 0;
            _distance = 0f;
            _flying = false;
            _stackHit = false;
            _impactCollider = null;
            _impactPoint = _restPos;
            _impactDirection = Vector3.left;
            _impactNormalSpeed = 0f;
            // Putting the puck on the disc is also declaring the disc to be the next shot's origin.
            // Saying so here keeps the harness's Replay() path anchored exactly where it always was.
            _shotOrigin = _restPos;
            _shotOriginSet = true;
            ClearTrail();
            HidePad();
        }

        /// <summary>
        /// Holds the puck STILL on its disc for the scripted idle window, and holds it there with a
        /// kinematic body so nothing nudges it while the sequence waits.
        ///
        /// This was <c>SetPullback(float t)</c>, and it dragged the puck <c>0.85 * t</c> units back
        /// along <c>-referenceAimDir</c> before the release. That wind-up is the owner's "basta atarken
        /// geri cekilme yada kuculme gibi bir sey yapiyor", and the reason he could not decide which of
        /// the two it was is that it is one motion producing both readings:
        ///
        ///   - GERI CEKILME: 0.850u of retreat, measured by the gate off the puck's own transform.
        ///   - KUCULME: Rail_Bottom's inner face is only 0.523u behind the disc, so the retreat drives
        ///     the puck 0.640u INTO the rail. Measured off the capture: the coin's gold pixel count
        ///     falls 1604 -> 0 over 29 frames and the puck is then completely invisible for 27 more
        ///     before the launch pops it back out. It does not shrink, it is swallowed - which is also
        ///     why the screen-projected size of its bounds barely moves while this is happening.
        ///
        /// The reference has no wind-up to match. Measured frame by frame off Buca.mp4 with a gold
        /// mask: across its first 32 frames (0.000-0.608 s at 51 fps) the puck's centroid moves 1.3 px
        /// in x and 1.2 px in y and its bounding box is a constant 88x58 px with bit-identical
        /// ymin/ymax; at n=32 it steps -79 px in one frame and that is the launch. So the fix is to
        /// remove the wind-up, not to shorten it.
        ///
        /// WHAT THIS DOES TO THE LAUNCH ORIGIN, stated plainly because the offset used to set it:
        /// Launch() fires from wherever the puck stands, so the scripted shot now leaves from the rest
        /// disc (-26.836, 0.128, -16.165) instead of from (-27.104, 0.128, -16.972). That is +0.850u
        /// along referenceAimDir - the SAME ray, entered 0.850u further along it, at the same speed and
        /// heading. The bank geometry is therefore unchanged: the rails are struck at the same points,
        /// the contact count is the same, and the shot is shorter and earlier by exactly that 0.850u of
        /// pre-travel. The old origin was inside a wall, so keeping it byte-identical and keeping the
        /// puck visible were not both available.
        ///
        /// The rail-contact count in that signature goes 3 -> 2, and a negative control says why rather
        /// than leaving it to be assumed. Restoring the 0.85u offset and nothing else reproduces the old
        /// signature exactly (FLIGHT 1.060 s, 3 contacts, 32.80u, 4.320 s / 434 frames) and names the
        /// contacts: #1 Rail_Bottom at (-27.10, 0.31, -16.69), #2 Rail_Right, #3 Rail_Arch. That first
        /// one is Rail_Bottom's inner face at the launch origin's own x - the puck striking the wall it
        /// had been standing inside, 0.12 s before the bank's real first leg. With the offset gone the
        /// contacts are #1 Rail_Right and #2 Rail_Arch, at the same points as before (-13.38 -> -13.18
        /// and -30.23 -> -30.16). The bank is intact; what was lost was the self-extraction.
        /// </summary>
        public void HoldForScriptedShot()
        {
            if (puck == null) return;
            if (_rb != null)
            {
                if (!_rb.isKinematic)
                {
                    _rb.linearVelocity = Vector3.zero;
                    _rb.angularVelocity = Vector3.zero;
                }
                _rb.isKinematic = true;
            }
            puck.position = _restPos;
            if (_rb != null) _rb.position = _restPos;
        }

        /// <summary>
        /// The PLAYER's pull-back: along the axis he is dragging, not along the scripted bank, and
        /// kept out of the walls.
        ///
        /// Deliberately a separate method from the harness's own idle hold, rather than an extra
        /// argument on it. That one used to carry a 0.85-unit offset along -referenceAimDir which set
        /// the launch origin, and it was kept byte-identical for a long time on the grounds that the
        /// launch origin re-rolls every ricochet, the impact point and the coin arming. It has since
        /// been removed outright, because that offset put the puck 0.640u inside Rail_Bottom and the
        /// owner was watching the coin disappear into the wall - see
        /// <see cref="HoldForScriptedShot"/>. The harness hold now leaves the puck on its disc.
        ///
        /// A player pull still needs its own method, and needs the de-penetration this one does: he can
        /// drag in any direction, including into a wall, and a puck that starts a shot embedded in a
        /// rail is launched by the solver's de-penetration rather than by his aim. So this one resolves
        /// the overlap before it hands the pose over.
        /// </summary>
        public void SetPullbackAlong(Vector3 pullDir, float t)
        {
            if (puck == null) return;
            if (_rb != null)
            {
                if (!_rb.isKinematic)
                {
                    _rb.linearVelocity = Vector3.zero;
                    _rb.angularVelocity = Vector3.zero;
                }
                _rb.isKinematic = true;
            }

            // Anchored on ShotOrigin, NOT on _restPos. Anchoring the player's pull on the rest disc
            // meant that the moment he started dragging, the puck jumped from wherever the last shot
            // had left it back to the far right and pulled back from there.
            Vector3 d = new Vector3(pullDir.x, 0f, pullDir.z);
            Vector3 origin = ShotOrigin;
            Vector3 pos = origin;
            if (d.sqrMagnitude > 0.0001f)
                pos = origin - d.normalized * (0.85f * Mathf.Clamp01(t));

            pos = PushOutOfWalls(pos);
            puck.position = pos;
            if (_rb != null) _rb.position = pos;
        }

        /// <summary>
        /// Where the next shot starts from. The rest disc until something says otherwise.
        /// </summary>
        public Vector3 ShotOrigin { get { return _shotOriginSet ? _shotOrigin : _restPos; } }

        /// <summary>
        /// Makes the puck shootable again WITHOUT sending it back to the disc: it keeps the XZ the
        /// last shot left it at, and only snaps Y back onto the authored plane the body is frozen on
        /// anyway.
        ///
        /// This is the half of <see cref="ResetInstant"/> that the between-shots path actually wants.
        /// The two were one call, and "arm the next shot" therefore meant "put the puck on the disc":
        /// measured on the unfixed code, pull #1 came to rest at (-34.29, -9.17) and pull #2 was fired
        /// from (-27.49, -16.33), 9.87 units away, on the disc. That is the owner's
        /// "en saga donup oradan baslamasin".
        ///
        /// ResetInstant itself is untouched, because the capture's Replay() goes through it and the
        /// reference bank shot has to keep starting from the authored pose.
        /// </summary>
        public void ResumeFrom(Vector3 world)
        {
            if (puck == null) return;

            if (_postImpactGlide != null) StopCoroutine(_postImpactGlide);
            _postImpactGlide = null;
            Squash.Cancel(Deformable);
            Deformable.localScale = _restScale;

            Vector3 want = new Vector3(world.x, _restPos.y, world.z);

            if (_rb != null)
            {
                if (!_rb.isKinematic)
                {
                    _rb.linearVelocity = Vector3.zero;
                    _rb.angularVelocity = Vector3.zero;
                }
                _rb.isKinematic = true;
                _rb.linearDamping = 0f;
            }
            puck.localScale = _bodyScale;      // the collider is on the body; its size must never drift

            // The collider goes on BEFORE the overlap is resolved. The post-impact glide slides the
            // puck through the divider with its collider off, so the place a shot ends is not
            // guaranteed to be a place a shot can legally start.
            Collider puckCollider = PuckCollider;
            if (puckCollider != null) puckCollider.enabled = true;
            puck.SetPositionAndRotation(want, _restRot);
            if (_rb != null) _rb.position = want;
            Physics.SyncTransforms();

            Vector3 resolved = PushOutOfWalls(want);
            puck.SetPositionAndRotation(resolved, _restRot);
            if (_rb != null) _rb.position = resolved;
            Physics.SyncTransforms();

            if (_rb != null)
            {
                _rb.isKinematic = false;
                _rb.linearDamping = 0f;
                _rb.Sleep();
            }

            _shotOrigin = resolved;
            _shotOriginSet = true;
            _bounces = 0;
            _distance = 0f;
            _lastSample = resolved;
            _flying = false;
            _stackHit = false;
            _impactCollider = null;
            _impactPoint = resolved;
            _impactDirection = Vector3.left;
            _impactNormalSpeed = 0f;
            ClearTrail();
            HidePad();

            Debug.Log(string.Format(
                "[Case4] RESUME_IN_PLACE next shot starts at {0} ({1:0.000}u from the rest disc {2}; " +
                "de-penetration moved it {3:0.000}u)",
                resolved.ToString("0.000"), Vector3.Distance(resolved, _restPos),
                _restPos.ToString("0.000"), Vector3.Distance(resolved, want)));
        }

        /// <summary>
        /// Moves a candidate puck pose out of any arena wall it is inside, using Unity's own
        /// ComputePenetration against the real colliders rather than any arithmetic this file shares
        /// with the code that placed them.
        /// </summary>
        Vector3 PushOutOfWalls(Vector3 pos)
        {
            Collider self = PuckCollider;
            if (self == null) return pos;

            Collider[] walls = WallColliders();
            for (int pass = 0; pass < 4; pass++)
            {
                bool moved = false;
                for (int i = 0; i < walls.Length; i++)
                {
                    Collider w = walls[i];
                    if (w == null || !w.enabled) continue;
                    Vector3 dir;
                    float dist;
                    if (!Physics.ComputePenetration(self, pos, puck.rotation,
                                                    w, w.transform.position, w.transform.rotation,
                                                    out dir, out dist)) continue;
                    Vector3 push = dir * dist;
                    push.y = 0f;                      // the body is Y-frozen; never lift it out
                    pos += push;
                    moved = true;
                }
                if (!moved) break;
            }
            return pos;
        }

        Collider[] _wallColliders;

        Collider[] WallColliders()
        {
            if (_wallColliders != null) return _wallColliders;
            System.Collections.Generic.List<Collider> found = new System.Collections.Generic.List<Collider>();
            Collider[] all = FindObjectsByType<Collider>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            for (int i = 0; i < all.Length; i++)
            {
                Collider c = all[i];
                if (c == null || c.isTrigger) continue;
                string n = c.gameObject.name;
                if (n.StartsWith("Rail_") || n == "Divider") found.Add(c);
            }
            _wallColliders = found.ToArray();
            return _wallColliders;
        }

        /// <summary>Compresses the puck into the disc before it is fired.</summary>
        public void Anticipate(float duration)
        {
            if (puck == null) return;
            Squash.SquashStretch(Deformable, SquashAxis.Y, -0.12f, duration, EaseType.OutQuad);
        }

        /// <summary>
        /// Fires the puck. This is the whole launch: one impulse on a real body. Everything after it -
        /// the ricochets, the angle it arrives at the stack with, the momentum it hands over - is the
        /// physics engine, not a curve.
        /// </summary>
        public void Launch(Vector3 direction, float power)
        {
            if (_rb == null || puck == null) return;

            Vector3 d = new Vector3(direction.x, 0f, direction.z);
            if (d.sqrMagnitude < 0.0001f) d = referenceAimDir;
            d.Normalize();

            float speed = launchSpeed * Mathf.Lerp(0.72f, 1f, Mathf.Clamp01(power));

            Vector3 launchPos = new Vector3(puck.position.x, _restPos.y, puck.position.z);
            puck.position = launchPos;

            // Last line of defence, and it is a real one. Measured on the unfixed code: pull #2 was
            // fired at 30.62 u/s with colliderEnabled=False and finished 97.50u outside the floor
            // footprint, because a puck with no collider cannot be turned by any rail. Whatever
            // disabled it, a shot never leaves this method intangible.
            Collider launchCollider = PuckCollider;
            if (launchCollider != null && !launchCollider.enabled)
            {
                launchCollider.enabled = true;
                Debug.LogWarning("[Case4] LAUNCH_RE_ENABLED the puck collider; it was disabled at fire time");
            }

            _rb.isKinematic = false;
            _rb.position = launchPos;
            _rb.linearDamping = 0f;
            _rb.WakeUp();
            _rb.linearVelocity = d * speed;
            _rb.angularVelocity = Vector3.zero;

            _lastDir = d;
            _lastSample = puck.position;
            _bounces = 0;
            _distance = 0f;
            _flightDistance = 0f;
            _flying = true;
            _nextBounceAt = 0f;
            _stackHit = false;
            _launchTime = Time.time;
            _stackHitTime = -1f;
            _bleeding = false;
            _impactCollider = null;
            _impactPoint = puck.position;
            _impactDirection = d;
            _impactNormalSpeed = 0f;

            Squash.Cancel(Deformable);
            Deformable.localScale = Squash.Deform(_restScale, SquashAxis.Z, stretchAmount);
            SetTrail(true);
            HidePad();   // idempotent; the disc is already off and stays off for the whole run

            Debug.Log(string.Format("[Case4] PHYSICS_LAUNCH dir={0} speed={1:0.00} from={2} rbKinematic={3} collider={4}",
                d.ToString("0.000"), speed, puck.position.ToString("0.00"),
                _rb.isKinematic, ColliderSummary()));
        }

        /// <summary>
        /// Slows a puck that is STILL IN ITS SHOT, so a bank that has outrun the shot's pacing budget
        /// comes to a stop instead of ricocheting on the arena's 0.965-bounciness rails for another
        /// several seconds. Deliberately NOT <see cref="Calm"/>: Calm clears <c>_flying</c>, which
        /// makes <see cref="NotifyCollision"/> return immediately, and a puck that is blind to
        /// contacts can roll into the stack and shove it about with raw physics while the launcher
        /// reports it never touched anything. This keeps the contact path live all the way down to
        /// rest, so a late arrival is still a real, armed, counted stack hit.
        /// </summary>
        public void BleedOff()
        {
            if (_rb == null || _bleeding) return;
            _bleeding = true;
            _rb.linearDamping = restingDamping;
            Debug.Log(string.Format(
                "[Case4] PUCK_BLEEDOFF damping {0:0.0} applied at speed {1:0.00} after {2:0.000}s of flight; " +
                "contacts stay live",
                restingDamping, Speed, Time.time - _launchTime));
        }

        /// <summary>
        /// Bleeds the puck's energy off once the shot has done its job, so the shot does not loop.
        ///
        /// ONLY REACHES A SHOT THAT MISSED. On any shot that connects, BeginPostImpactGlide has
        /// already zeroed the velocity and set isKinematic on the solver frame of the stack contact,
        /// and linearDamping on a kinematic body does nothing - so restingDamping, the whole `calmed`
        /// branch in the director's collapse loop, and puckCalmDelay's 1.25 s all apply to the
        /// missed path and nothing else. SetTrail(false) is likewise already done, by the same
        /// contact.
        ///
        /// <para>It used to say "the flight loop can hit flightTimeout without a stack contact, and
        /// there the body is still dynamic and this is what stops it" - i.e. it treated the timeout as
        /// PROOF of a miss. It was not: the timeout only said the director had stopped waiting. A shot
        /// still travelling at 25 u/s when the 2.40 s budget ran out reached the stack a second later,
        /// and by then this method had already been called and had blinded the contact path. The
        /// flight step now resolves the shot before anything calls Calm, so by the time it runs the
        /// puck has either hit the stack or genuinely stopped.</para>
        /// </summary>
        public void Calm()
        {
            if (_rb == null) return;
            _rb.linearDamping = restingDamping;
            _flying = false;
            SetTrail(false);
        }

        /// <summary>
        /// Stops the puck dead; called at the very end so the last frames are still.
        ///
        /// It did not actually do that. PostImpactGlide drives puck.position from its own coroutine,
        /// and Park left the coroutine running: for as long as it had left to run the puck kept sliding
        /// after the beat that is supposed to have stopped it. It also left the collider in whatever
        /// state the glide had it in. Neither is reachable on the reference shot, where the glide's
        /// 0.74 s is long over by the settle beat - but "stops the puck dead" should not be true only
        /// because of a timing coincidence somewhere else in the file.
        /// </summary>
        public void Park()
        {
            if (_rb == null) return;
            if (_postImpactGlide != null) { StopCoroutine(_postImpactGlide); _postImpactGlide = null; }
            if (!_rb.isKinematic)
            {
                _rb.linearVelocity = Vector3.zero;
                _rb.angularVelocity = Vector3.zero;
            }
            _rb.Sleep();
            _flying = false;
            ClearTrail();
            // The glide switches this off. If Park interrupted it, nothing else would ever switch it
            // back on, and that is exactly the shape of the bug 9416ac6 had to add a last-ditch guard
            // in Launch() for.
            Collider parkedCollider = PuckCollider;
            if (parkedCollider != null && !parkedCollider.enabled) parkedCollider.enabled = true;
            if (puck != null)
            {
                Squash.Cancel(Deformable);
                Deformable.localScale = _restScale;
                if (_rb != null) _rb.position = puck.position;
                Physics.SyncTransforms();
            }
        }

        /// <summary>The transform the juice deforms: the visual child, or the body if there is no child.</summary>
        Transform Deformable { get { return _visual != null ? _visual : puck; } }

        /// <summary>Human readable proof that the puck's collider is on, for the gate log.</summary>
        public string ColliderSummary()
        {
            if (puck == null) return "<no puck>";
            Collider[] cols = puck.GetComponentsInChildren<Collider>(true);
            int enabled = 0;
            for (int i = 0; i < cols.Length; i++) if (cols[i].enabled) enabled++;
            return cols.Length + " collider(s), " + enabled + " enabled";
        }

        // ------------------------------------------------------------------ per-frame

        // ------------------------------------------------------------------ per-frame / contacts

        /// <summary>
        /// Per-frame work is deliberately visual only: measure travelled distance and orient the render
        /// child. Collision semantics are handled by NotifyCollision on the physics callback frame.
        /// </summary>
        void Update()
        {
            if (_rb == null || puck == null) return;

            _distance += Vector3.Distance(puck.position, _lastSample);
            _lastSample = puck.position;

            Vector3 v = _rb.linearVelocity;
            v.y = 0f;
            if (v.sqrMagnitude < 0.20f) return;
            Vector3 dir = v.normalized;
            _lastDir = dir;

            if (_visual != null) _visual.rotation = Quaternion.LookRotation(dir, Vector3.up);

            // The heading has just been updated, so the trail's screen length is re-solved against
            // it. A ricochet changes the foreshortening by up to 2.7x and nothing used to notice.
            if (_flying) UpdateTrailForHeading();
        }

        /// <summary>
        /// Called by PuckCollisionRelay on Unity's real solver contact. Stack hits and rail bounces are
        /// therefore timed on contact, not guessed from a change in velocity direction.
        /// </summary>
        internal void NotifyCollision(Collision collision, bool entered)
        {
            if (!_flying || collision == null || collision.collider == null) return;

            ContactPoint cp = collision.contactCount > 0 ? collision.GetContact(0) : default;
            Vector3 rel = collision.relativeVelocity;
            rel.y = 0f;
            float normalSpeed = collision.contactCount > 0 ? Mathf.Abs(Vector3.Dot(rel, cp.normal)) : rel.magnitude;

            BucaStackBlock stack = collision.collider.GetComponentInParent<BucaStackBlock>();
            if (stack == null && collision.gameObject != null) stack = collision.gameObject.GetComponentInParent<BucaStackBlock>();

            if (stack != null)
            {
                if (!_stackHit && entered)
                {
                    _stackHit = true;
                    _stackHitTime = Time.time - _launchTime;
                    _flightDistance = _distance;
                    _impactCollider = collision.collider;
                    _impactPoint = collision.contactCount > 0 ? cp.point : puck.position;
                    Vector3 travel = _rb.linearVelocity; travel.y = 0f;
                    _impactDirection = travel.sqrMagnitude > 0.001f ? travel.normalized : _lastDir;
                    _impactNormalSpeed = travel.magnitude;
                    if (stack.owner != null) stack.owner.BeginDeterministicCascade();
                    AudioService.Play(SfxId.PuckImpact, 0.72f, 0.98f);
                    SetTrail(false);

                    // The payout is armed from THIS contact point, on this solver frame. Nothing else
                    // in the sequence can arm it, so a shot that never touches the pile pays nothing.
                    if (payout != null)
                    {
                        payout.ArmFromContact(_impactPoint);
                        Debug.Log(string.Format(
                            "[Case4] COIN_ARMED from solver contact at {0} (block '{1}', normalSpeed={2:0.00})",
                            _impactPoint.ToString("0.###"), collision.collider.name, normalSpeed));
                    }

                    BeginPostImpactGlide();
                }
                return;
            }

            if (!entered || Time.time < _nextBounceAt || normalSpeed < 1.2f) return;
            _nextBounceAt = Time.time + 0.055f;
            _bounces++;
            OnRailContact(collision.contactCount > 0 ? cp.point : puck.position, collision.collider);
        }

        void OnDestroy()
        {
            if (_postImpactGlide != null) StopCoroutine(_postImpactGlide);
            if (_physicsMaterial != null) Destroy(_physicsMaterial);
            if (_railPhysicsMaterial != null) Destroy(_railPhysicsMaterial);
        }

        void OnRailContact(Vector3 at, Collider what)
        {
            // Named, because "rails hit=N" on its own cannot say WHICH rails, and the count is part of
            // the reference path's signature. It went 3 -> 2 when the scripted wind-up was removed and
            // the only way to know that the lost contact was the puck extracting itself from the rail
            // it used to be launched from inside of - rather than a leg of the bank going missing - is
            // to read the names back.
            Debug.Log(string.Format("[Case4] RAIL_CONTACT #{0} with {1} at {2} (t+{3:0.000}s)",
                _bounces, what != null ? what.name : "<unknown>", at.ToString("0.00"), Time.time));
            if (puck != null)
            {
                Squash.Cancel(Deformable);
                Deformable.localScale = _restScale;
                Squash.SquashStretch(Deformable, SquashAxis.Z, bounceSquash, 0.09f, EaseType.OutQuad);
            }
            if (wall != null) wall.Flash(at, 0.11f);
            AudioService.Play(SfxId.ArrivalImpact, 0.30f, 1.18f);
        }

        void BeginPostImpactGlide()
        {
            if (_rb == null || puck == null || _postImpactGlide != null) return;
            _flying = false;
            _rb.linearVelocity = Vector3.zero;
            _rb.angularVelocity = Vector3.zero;
            _rb.isKinematic = true;
            Collider puckCollider = PuckCollider;
            if (puckCollider != null) puckCollider.enabled = false;
            _postImpactGlide = StartCoroutine(PostImpactGlide());
        }

        IEnumerator PostImpactGlide()
        {
            Vector3 from = puck.position;
            Vector3 to = from + new Vector3(0.55f, 0f, 5.10f);
            float started = Time.time;
            const float duration = 0.74f;
            while (Time.time - started < duration)
            {
                float t = Mathf.Clamp01((Time.time - started) / duration);
                float move = 1f - Mathf.Pow(1f - t, 2.2f);
                Vector3 p = Vector3.Lerp(from, to, move);
                p.y += Mathf.Sin(t * Mathf.PI) * 0.22f;
                puck.position = p;
                yield return null;
            }
            puck.position = to;
            _postImpactGlide = null;

            // The glide switched the collider off so the puck would not shove the debris around while
            // it slid clear. Switching it back on is not cosmetic: nothing else on the between-shots
            // path did it, so the puck sat there solid-looking but intangible until the next
            // ResetInstant - and the next shot was fired with no collider at all.
            Collider puckCollider = PuckCollider;
            if (puckCollider != null) puckCollider.enabled = true;

            // The glide moved the TRANSFORM only. The body it belongs to is kinematic and was left at
            // the impact point, up to 5.13u behind, and the collider above is re-registered at that
            // stale pose until something happens to sync it. ResumeFrom happens to overwrite
            // _rb.position on the next press, which is why this never showed - but between the two the
            // scene's own physics view of where the puck is was simply wrong.
            if (_rb != null) _rb.position = to;
            Physics.SyncTransforms();
        }

        // ------------------------------------------------------------------ trail

        /// <summary>Turns the spark trail emission on or off; the already-emitted sparks keep living.</summary>
        public void SetTrail(bool on)
        {
            SetEmitter(_trail, on);
            SetEmitter(_glowTrail, on);
        }

        static void SetEmitter(ParticleSystem particles, bool on)
        {
            if (particles == null) return;
            ParticleSystem.EmissionModule emission = particles.emission;
            emission.enabled = on;
            if (on && !particles.isPlaying) particles.Play(true);
        }

        /// <summary>Puck's authored starting position.</summary>
        public Vector3 RestPosition { get { return _restPos; } }

        /// <summary>Clears every spark currently in the air.</summary>
        public void ClearTrail()
        {
            SetTrail(false);
            if (_trail != null) _trail.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            if (_glowTrail != null) _glowTrail.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        }
    }

    /// <summary>
    /// The arena's rim. Idle it is plain white, exactly as the reference is before the shot; the cyan
    /// only arrives when the puck is released, and a contact blows it to white for a moment.
    /// Deliberately a plain serialisable object rather than a MonoBehaviour: Unity only creates a
    /// MonoScript for a class whose name matches its file, so a second MonoBehaviour declared here
    /// would serialise into the scene as a missing script.
    /// </summary>
    [System.Serializable]
    public sealed class NeonWallFlash
    {
        [Header("Wiring (filled in by Case4SceneSetup)")]
        public Renderer[] wallRenderers = new Renderer[0];
        public Material neonMaterial;

        [Header("Colours")]
        public Color baseColor = new Color(0.97f, 0.98f, 1.00f, 1f);
        [Tooltip("Base colour once the rim is live. The reference rim is unmistakably cyan, not a white rail with a blue glow on it.")]
        public Color activeBaseColor = new Color(0.36f, 0.88f, 1.00f, 1f);
        [Tooltip("Before the release the rim is neutral white, like the reference. No emission at all.")]
        public Color idleEmission = new Color(0f, 0f, 0f, 1f);
        [Tooltip("The activation colour is cyan, not blue: it is the loudest colour change in the reference.")]
        public Color neonEmission = new Color(0.052f, 0.58f, 0.78f, 1f);
        public Color flashEmission = new Color(0.165f, 0.58f, 0.76f, 1f);

        [System.NonSerialized] Material _runtime;
        [System.NonSerialized] TweenHandle _flash;
        [System.NonSerialized] int _flashCount;
        [System.NonSerialized] bool _active;

        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        static readonly int EmissionId = Shader.PropertyToID("_EmissionColor");

        /// <summary>How many times the rim flashed during the current run (report proof).</summary>
        public int FlashCount { get { return _flashCount; } }

        /// <summary>True once the rim has been switched to its cyan state.</summary>
        public bool IsActive { get { return _active; } }

        /// <summary>
        /// True only if there is a material for Activate/Flash/SetEmission to write to. It is FALSE
        /// in the shipped scene: ApplyNeonMaterial deliberately no longer swaps a material in
        /// (3d4984e), so the rim keeps its authored look and every emission write below returns at
        /// its first line. IsActive and FlashCount count CALLS, not pixels - anything reporting the
        /// rim beat has to say this too, or it reports a flash that nothing drew.
        /// </summary>
        public bool IsWired { get { return _runtime != null; } }

        /// <summary>Called once from the director; swaps the material in and sets the resting colour.</summary>
        public void Init()
        {
            ApplyNeonMaterial();
            _active = false;
            SetEmission(idleEmission);
        }

        /// <summary>Turns the rim cyan when the puck is released, matching the reference beat.</summary>
        public void Activate(float duration = 0.24f)
        {
            _active = true;
            if (duration > 0.001f)
            {
                _flash = Tweener.Color(idleEmission, neonEmission, duration, c =>
                {
                    SetEmission(c);
                }).SetEase(EaseType.OutQuad);
            }
            else
            {
                SetEmission(neonEmission);
            }
        }

        /// <summary>
        /// Swaps the staged cream frame material for a single instance shared by every rim renderer.
        /// A real material instance rather than a MaterialPropertyBlock: with the SRP Batcher on,
        /// per-material CBUFFER properties pushed through a property block are dropped silently.
        /// </summary>
        /// <summary>The frame's authored material arrays, kept so nothing is lost by the swap.</summary>
        Material[][] _authored;
        Material[] _instances;
        Color[] _seedColors;
        bool _adoptedBase;
        static readonly int LegacyColorId = Shader.PropertyToID("_Color");

        public void ApplyNeonMaterial()
        {
            // DELIBERATELY DOES NOT SWAP. The owner wants play to look like the authored scene:
            // "playe basinca ortadaki renk degisiyor ... sagdaki gibi baslasin ve bitsin."
            //
            // level_frame carries two submeshes - level_frame and levelframe1 - and the arena's
            // whole look lives in them. Every attempt to paint the neon instance over one of those
            // slots recoloured something the owner did not want recoloured: first the arch rim went
            // white, and after seeding the instance from the material it replaced, the divider cap
            // did. One runtime material cannot represent an arena authored as two, and chasing it
            // slot by slot was recolouring by trial.
            //
            // So the frame keeps exactly the materials the artist gave it, start to finish.
            // The cost is the cyan release flash, which was driven through this instance: Activate
            // and SetEmission now have nothing to write to and are inert. That beat is a real
            // reference behaviour and it is GONE, not hidden - it needs to be rebuilt against the
            // authored materials, per slot, if it is wanted back.
        }


        /// <summary>Pushes an emission colour onto the shared rim material instance.</summary>
        public void SetEmission(Color c)
        {
            if (_runtime == null) return;
            _runtime.SetColor(EmissionId, c);
            _runtime.SetColor(BaseColorId, _active ? activeBaseColor : baseColor);

            // Each wall keeps its own authored colour at rest and only shares the cyan flash.
            if (_instances == null) return;
            for (int i = 0; i < _instances.Length; i++)
            {
                if (_instances[i] == null) continue;
                _instances[i].SetColor(EmissionId, c);
                _instances[i].SetColor(BaseColorId, _active ? activeBaseColor : _seedColors[i]);
            }
        }

        /// <summary>Blows the emission to white and lets it fall back to the cyan.</summary>
        public void Flash(Vector3 at, float duration)
        {
            _flashCount++;
            _flash.Cancel();
            SetEmission(flashEmission);
            Color back = _active ? neonEmission : idleEmission;
            _flash = Tweener.Color(flashEmission, back, duration, SetEmission)
                .SetEase(EaseType.OutQuad);
        }

        /// <summary>Back to the plain white idle rim, cancelling any flash in flight.</summary>
        public void ResetInstant()
        {
            _flash.Cancel();
            _flashCount = 0;
            _active = false;
            SetEmission(idleEmission);
        }
    }
}
