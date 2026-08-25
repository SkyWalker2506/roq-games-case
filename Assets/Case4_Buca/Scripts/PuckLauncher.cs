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
        public float trailScale = 0.65f;
        public float trailLifetime = 0.27f;
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
            GameObject go = Instantiate(starTrailPrefab, puck);
            go.name = "PuckStarTrail";
            go.transform.localPosition = Vector3.zero;
            go.transform.localRotation = Quaternion.identity;
            go.transform.localScale = Vector3.one * trailScale;
            _trail = go.GetComponent<ParticleSystem>();

            if (_trail != null)
            {
                // World space or the stars travel with the puck and there is no streak at all. Short
                // lifetime: the reference trail is a couple of sparks, not a chain of dots that reads
                // as "animated markers following the puck".
                ParticleSystem.MainModule main = _trail.main;
                main.simulationSpace = ParticleSystemSimulationSpace.World;
                main.playOnAwake = false;
                main.startLifetime = trailLifetime;
                main.startColor = new Color(1f, 0.86f, 0.18f, 1f);
                main.startSize = new ParticleSystem.MinMaxCurve(0.20f, 0.45f);

                // Both verification passes must emit the same stars in the same places. A local fixed
                // seed keeps this effect deterministic without touching UnityEngine.Random globally.
                _trail.useAutoRandomSeed = false;
                _trail.randomSeed = 0xC4A11u;

                ParticleSystem.EmissionModule em = _trail.emission;
                em.rateOverTime = trailEmissionRate;
            }

            // The reference has two simultaneous reads: separate warm stars and an almost continuous
            // white bloom core. Reusing the same deterministic emitter keeps their motion coherent;
            // the soft-circle material and denser, larger particles turn the second copy into a plume.
            if (trailGlowMaterial != null)
            {
                GameObject glowGo = Instantiate(starTrailPrefab, puck);
                glowGo.name = "PuckGlowTrail";
                glowGo.transform.localPosition = Vector3.zero;
                glowGo.transform.localRotation = Quaternion.identity;
                glowGo.transform.localScale = Vector3.one * trailScale;
                _glowTrail = glowGo.GetComponent<ParticleSystem>();
                ParticleSystemRenderer glowRenderer = glowGo.GetComponent<ParticleSystemRenderer>();
                if (glowRenderer != null) glowRenderer.sharedMaterial = trailGlowMaterial;

                if (_glowTrail != null)
                {
                    ParticleSystem.MainModule glowMain = _glowTrail.main;
                    glowMain.simulationSpace = ParticleSystemSimulationSpace.World;
                    glowMain.playOnAwake = false;
                    glowMain.startLifetime = Mathf.Min(0.09f, trailLifetime);
                    glowMain.startColor = new Color(1f, 0.98f, 0.78f, 1f);
                    glowMain.startSize = new ParticleSystem.MinMaxCurve(0.85f, 1.25f);

                    _glowTrail.useAutoRandomSeed = false;
                    _glowTrail.randomSeed = 0xC4A12u;
                    ParticleSystem.EmissionModule glowEmission = _glowTrail.emission;
                    glowEmission.rateOverTime = trailEmissionRate * 3.2f;
                }
            }
            SetTrail(false);
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
        /// Bleeds the puck's energy off once the shot has done its job, so the shot does not loop.
        ///
        /// ONLY REACHES A SHOT THAT MISSED. On any shot that connects, BeginPostImpactGlide has
        /// already zeroed the velocity and set isKinematic on the solver frame of the stack contact,
        /// and linearDamping on a kinematic body does nothing - so restingDamping, the whole `calmed`
        /// branch in the director's collapse loop, and puckCalmDelay's 1.25 s all apply to the
        /// timed-out path and nothing else. SetTrail(false) is likewise already done, by the same
        /// contact. Kept because the miss path is reachable: the flight loop can hit flightTimeout
        /// without a stack contact, and there the body is still dynamic and this is what stops it.
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
