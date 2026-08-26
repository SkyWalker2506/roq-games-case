using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Shared.Tweening;

namespace Case4
{
    /// <summary>
    /// The green block stack. In the reference the stack does not explode: whole blocks topple and fan
    /// across the left lane. The puck reaches it through real rigidbody/rail physics; that solver contact
    /// starts a deterministic whole-block cascade. Kinematic trajectories are deliberate here: a 21-body
    /// PhysX pile chose different simultaneous-contact branches on capture replay, while the authored
    /// cascade preserves the reference silhouette, timing and exact frame repeatability.
    ///
    /// The name is kept from the previous, scripted-shatter version so no scene reference is orphaned.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class GreenBlockShatter : MonoBehaviour
    {
        sealed class Block
        {
            public Transform Tr;
            public Rigidbody Rb;
            public Collider Col;
            public Renderer Rend;
            public Vector3 RestPos;
            public Quaternion RestRot;
            public Vector3 RestScale;
        }

        /// <summary>
        /// One block's authored settle pose. Public, and carrying the block's own rest data rather
        /// than a reference to the private <see cref="Block"/> record, so that an editor probe can
        /// measure the SAME poses the cascade will play rather than a second copy of the formula.
        /// Two copies of a layout formula is the trap this case has already paid for twice.
        /// </summary>
        public sealed class CascadePose
        {
            public Transform Tr;
            public Vector3 RestPos;
            public Quaternion RestRot;
            public Vector3 HalfExtents;   // world half-size of the block at REST (rotation identity)
            public float Delay;
            public float Duration;
            public float Arc;
            public Vector3 EndPos;
            public Quaternion EndRot;
        }

        [Header("Wiring (filled in by Case4SceneSetup)")]
        public Material greenNeonMaterial;
        public Transform blockRoot;

        [Tooltip("The green blocks, in scene order. Filled in by Case4SceneSetup.")]
        public Transform[] blocks = new Transform[0];

        [Header("Green")]
        public Color blockBaseColor = new Color(0.180f, 0.820f, 0.240f, 1f);
        public Color blockRestEmission = new Color(0.010f, 0.055f, 0.018f, 1f);

        [Header("Scale (world units, derived from the arena by Case4SceneSetup)")]
        public float blockSize = 0.70f;
        [Tooltip("Neighbour-to-neighbour distance. Every size below is derived from this, never from a single renderer bound.")]
        public float blockPitch = 0.78f;

        [Header("Bodies")]
        public float blockMass = 0.52f;   // light enough for a chain reaction, heavy enough to read as solid pieces
        public float linearDamp = 0.035f;
        public float angularDamp = 0.11f;
        [Tooltip("Moderate contact friction lets neighbouring cubes drag and topple one another instead of skating like ice.")]
        public float dynamicFriction = 0.42f;
        public float staticFriction = 0.55f;
        [Tooltip("A little bounce keeps the collapse alive, but it must never look like exploding rubber dice.")]
        public float blockBounciness = 0.045f;

        // _blocks is a readonly List of a plain (non-[Serializable]) class, so Unity's domain-reload
        // backup drops it: after a mid-playmode script recompile the registry comes back EMPTY while
        // Awake never runs again. Every method below then iterates nothing - ResetInstant leaves the
        // block colliders exactly as it last left them (disabled) and ArmPhysics never re-enables
        // them, so the puck flies straight through a stack that is physically not there. The rest
        // poses are mirrored into serialized arrays so EnsureBlocks can rebuild the registry from the
        // authored pose rather than from wherever the blocks happen to be lying.
        readonly List<Block> _blocks = new List<Block>(24);
        [SerializeField, HideInInspector] Vector3[] _restPosBackup = new Vector3[0];
        [SerializeField, HideInInspector] Quaternion[] _restRotBackup = new Quaternion[0];
        [SerializeField, HideInInspector] Vector3[] _restScaleBackup = new Vector3[0];
        // Cleared by Awake, which only ever runs on a real scene load. A backup left in the scene
        // asset by an earlier session must never win over the authored transforms - Case4SceneSetup
        // is free to move the stack between sessions - but one that was written this session must
        // survive a domain reload, which is exactly the window Awake does not cover.
        [SerializeField, HideInInspector] bool _restBackupValid;
        Material _runtimeMat;
        PhysicsMaterial _blockPhysicsMaterial;
        Color _restEmission = new Color(0.01f, 0.055f, 0.018f, 1f);
        bool _armed;
        Coroutine _colorEvolution;
        Coroutine _cascade;
        bool _cascadeStarted;
        Collider _filteredPuckCollider;
        Collider _primaryImpactCollider;

        static readonly Color Mustard = new Color(0.643f, 0.706f, 0.016f, 1f); // #A4B404
        static readonly Color Red = new Color(0.988f, 0.235f, 0.008f, 1f);     // #FC3C02
        static readonly Color Magenta = new Color(0.996f, 0f, 0.992f, 1f);     // #FE00FD

        static readonly int EmissionId = Shader.PropertyToID("_EmissionColor");
        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        static readonly int SmoothnessId = Shader.PropertyToID("_Smoothness");

        // Case4/SoftBlock draws a dark line where a block's own edge falls. GreenNeon.mat carries NO
        // entry for either of these, so until now they ran at the shader's Properties defaults
        // (0.16 / 0.045) - live, but far too weak to read. Measured on the reference at rest: its
        // interior carries 6 dark runs across its 6 columns, dipping from a 252 median to 159-186,
        // and its mean interior edge energy is 47.3 against our 4.55. A tenfold gap, and H.264
        // compression suppresses high-frequency edges, so the true gap is wider than that.
        //
        // Depth: the reference's seam floor of 169 sRGB against a 252 median is a linear ratio of
        // 0.408, and the shader's seamDarken bottoms out at (1 - _SeamDepth), so 1 - 0.408 = 0.59.
        // Width: the geometry wants ~0.17; the declared Range caps the honest value at 0.15, and the
        // Range has been widened to 0.30 so this value is not sitting on its own ceiling.
        //
        // These are written HERE rather than into GreenNeon.mat or the shader's Properties defaults.
        // The .mat has no entry for them at all, so adding one would create a fresh two-copy trap of
        // exactly the kind this case has been paying for all night; and Properties defaults can be
        // baked into a material on reimport. Capture()'s _runtimeMat writes are a proven-live path.
        static readonly int SeamDepthId = Shader.PropertyToID("_SeamDepth");
        static readonly int SeamWidthId = Shader.PropertyToID("_SeamWidth");

        // Case4/SoftBlock draws every visible face from these three, not from _BaseColor. See ApplyColor.
        static readonly int TopColorId = Shader.PropertyToID("_TopColor");
        static readonly int FrontColorId = Shader.PropertyToID("_FrontColor");
        static readonly int SideColorId = Shader.PropertyToID("_SideColor");

        // NO brightness ladder. The reference's blocks render every visible face at the SAME value:
        // its top median is (1,246,2) and its cube fronts read (10,246,10) - g identical, a flat
        // unlit read. Ours had a ladder and measured tops 254 / fronts 174 on frame_20, a front-face
        // ratio of 0.43 in linear. These two constants were one of the two multiplicative dimmers
        // producing it (the other is lightFactor in Case4/SoftBlock, flattened in the same pass).
        // Setting both to 1 makes faceCol == blockBaseColor on every face, which is the reference's
        // structural invariant exactly, with no fitted constant.
        const float FrontFaceScale = 1.0f;
        const float SideFaceScale = 1.0f;

        void Awake()
        {
            _restBackupValid = false;   // real scene load: the authored transforms are the truth
            Capture();
        }

        // ------------------------------------------------------------------ capture / rest pose

        /// <summary>
        /// Remembers the rest pose of every block and gives them one shared runtime material instance.
        /// A real instance rather than a MaterialPropertyBlock: with the SRP Batcher on, per-material
        /// CBUFFER writes made through a property block are dropped silently.
        /// </summary>
        public void Capture()
        {
            _blocks.Clear();
            _settleAreaState = 0;   // re-resolve the arena; it may have moved since the last capture

            // First capture reads the authored pose off the transforms and mirrors it into the
            // serialized backup; any later capture (a rebuild after a domain reload) must read the
            // backup instead, because by then the blocks may be lying wherever the last run left them.
            bool useBackup = _restBackupValid &&
                             _restPosBackup != null && _restPosBackup.Length == blocks.Length &&
                             _restRotBackup != null && _restRotBackup.Length == blocks.Length &&
                             _restScaleBackup != null && _restScaleBackup.Length == blocks.Length &&
                             blocks.Length > 0;
            if (!useBackup)
            {
                _restPosBackup = new Vector3[blocks.Length];
                _restRotBackup = new Quaternion[blocks.Length];
                _restScaleBackup = new Vector3[blocks.Length];
                _restBackupValid = true;
            }

            if (_runtimeMat == null && greenNeonMaterial != null)
            {
                _runtimeMat = new Material(greenNeonMaterial);
                _runtimeMat.name = "Case4_GreenBlock (runtime)";
                _runtimeMat.EnableKeyword("_EMISSION");
                _runtimeMat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
                _restEmission = blockRestEmission;
                ApplyColor(blockBaseColor, _restEmission);
                if (_runtimeMat.HasProperty(SmoothnessId)) _runtimeMat.SetFloat(SmoothnessId, 0.06f);
                if (_runtimeMat.HasProperty(SeamDepthId)) _runtimeMat.SetFloat(SeamDepthId, 0.59f);
                if (_runtimeMat.HasProperty(SeamWidthId)) _runtimeMat.SetFloat(SeamWidthId, 0.15f);
            }

            for (int i = 0; i < blocks.Length; i++)
            {
                Transform t = blocks[i];
                if (t == null) continue;

                Rigidbody rb = t.GetComponent<Rigidbody>();
                if (rb == null) rb = t.gameObject.AddComponent<Rigidbody>();
                rb.mass = blockMass;
                rb.useGravity = true;
                rb.isKinematic = false;
                rb.linearDamping = linearDamp;
                rb.angularDamping = angularDamp;
                rb.interpolation = RigidbodyInterpolation.Interpolate;
                // A fast puck can tunnel through thin contacts at a 10 ms fixed step. Speculative CCD
                // on the stack is cheap at this body count and preserves whole-cube contacts.
                rb.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
                rb.sleepThreshold = 0.010f;
                rb.solverIterations = 10;
                rb.solverVelocityIterations = 4;
                rb.maxAngularVelocity = 30f;

                Collider col = t.GetComponent<Collider>();
                if (col == null) col = t.gameObject.AddComponent<BoxCollider>();
                col.enabled = true;
                col.sharedMaterial = EnsureBlockPhysicsMaterial();

                BucaStackBlock marker = t.GetComponent<BucaStackBlock>();
                if (marker == null) marker = t.gameObject.AddComponent<BucaStackBlock>();
                marker.owner = this;
                marker.index = i;

                Renderer r = t.GetComponent<Renderer>();
                if (r != null && _runtimeMat != null) r.sharedMaterial = _runtimeMat;

                if (!useBackup)
                {
                    _restPosBackup[i] = t.position;
                    _restRotBackup[i] = t.rotation;
                    _restScaleBackup[i] = t.localScale;
                }

                _blocks.Add(new Block
                {
                    Tr = t,
                    Rb = rb,
                    Col = col,
                    Rend = r,
                    RestPos = _restPosBackup[i],
                    RestRot = _restRotBackup[i],
                    RestScale = _restScaleBackup[i]
                });
            }

            ResetInstantInternal();
        }

        /// <summary>
        /// Rebuilds the runtime block registry if it has been lost, and reports whether it had to.
        /// See the comment on <see cref="_blocks"/>: a mid-playmode domain reload empties it silently,
        /// and every replay after that point runs against a stack with no colliders.
        /// </summary>
        public bool EnsureBlocks()
        {
            int wanted = 0;
            for (int i = 0; i < blocks.Length; i++) if (blocks[i] != null) wanted++;
            if (_blocks.Count == wanted) return false;

            Debug.LogWarning(string.Format(
                "[Case4] STACK_REGISTRY_REBUILD had={0} expected={1}; rebuilding from the serialized rest pose",
                _blocks.Count, wanted));
            Capture();
            return true;
        }

        PhysicsMaterial EnsureBlockPhysicsMaterial()
        {
            if (_blockPhysicsMaterial != null) return _blockPhysicsMaterial;
            _blockPhysicsMaterial = new PhysicsMaterial("Case4_StackBlocks");
            _blockPhysicsMaterial.dynamicFriction = dynamicFriction;
            _blockPhysicsMaterial.staticFriction = staticFriction;
            _blockPhysicsMaterial.bounciness = blockBounciness;
            _blockPhysicsMaterial.frictionCombine = PhysicsMaterialCombine.Average;
            _blockPhysicsMaterial.bounceCombine = PhysicsMaterialCombine.Minimum;
            return _blockPhysicsMaterial;
        }

        /// <summary>Zeroes every body and puts it to sleep, so an untouched stack never drifts on its own.</summary>
        public void Settle()
        {
            for (int i = 0; i < _blocks.Count; i++)
            {
                Rigidbody rb = _blocks[i].Rb;
                if (rb == null || rb.isKinematic) continue;
                rb.linearVelocity = Vector3.zero;
                rb.angularVelocity = Vector3.zero;
                rb.Sleep();
            }
            _armed = true;
        }

        /// <summary>
        /// Puts the stack back exactly where it was built and leaves it kinematic until the release.
        /// Holding the authored rest pose this way clears PhysX's contact history between replays; the
        /// bodies become live together in <see cref="ArmPhysics"/> on the actual launch frame.
        /// </summary>
        public void ResetInstant()
        {
            EnsureBlocks();
            ResetInstantInternal();
        }

        /// <summary>The reset itself, with no registry check, so <see cref="Capture"/> can end on it.</summary>
        void ResetInstantInternal()
        {
            ReleasePuckCollisionFilter();
            if (_cascade != null) StopCoroutine(_cascade);
            _cascade = null;
            _cascadeStarted = false;
            if (_colorEvolution != null) StopCoroutine(_colorEvolution);
            _colorEvolution = null;

            for (int i = 0; i < _blocks.Count; i++)
            {
                Block b = _blocks[i];
                if (b.Tr == null) continue;
                if (b.Rb != null)
                {
                    if (!b.Rb.isKinematic)
                    {
                        b.Rb.linearVelocity = Vector3.zero;
                        b.Rb.angularVelocity = Vector3.zero;
                    }
                    b.Rb.isKinematic = true;
                    b.Rb.detectCollisions = false;
                }
                b.Tr.SetPositionAndRotation(b.RestPos, b.RestRot);
                b.Tr.localScale = b.RestScale;
                if (b.Rend != null) b.Rend.enabled = true;
                if (b.Col != null) b.Col.enabled = false;
            }
            // Update the broadphase while every body is kinematic. Without this sync, the first fixed
            // step after Replay can still solve against a body's previous end-of-run pose.
            Physics.SyncTransforms();
            _armed = false;
            ApplyColor(blockBaseColor, _restEmission);
        }

        /// <summary>
        /// Makes every whole block a live sleeping rigidbody on one shared frame. A puck contact wakes
        /// the pile normally, but both capture passes now enter the solver with identical contact state.
        /// </summary>
        public void ArmPhysics(Collider puckCollider)
        {
            EnsureBlocks();
            for (int i = 0; i < _blocks.Count; i++)
            {
                Block block = _blocks[i];
                if (block.Col != null) block.Col.enabled = true;
                if (block.Rb != null) block.Rb.detectCollisions = true;
            }
            Physics.SyncTransforms();

            for (int i = 0; i < _blocks.Count; i++)
            {
                Rigidbody rb = _blocks[i].Rb;
                if (rb == null) continue;
                rb.isKinematic = false;
                rb.linearVelocity = Vector3.zero;
                rb.angularVelocity = Vector3.zero;
                rb.Sleep();
            }
            Physics.SyncTransforms();
            _armed = true;
            _chosenImpactCollider = null;
            FilterPuckToOneImpactBlock(puckCollider);
        }

        /// <summary>
        /// The authored puck arrives between adjacent cubes and PhysX may report either simultaneous
        /// contact first, which gives the same shot two different collapse branches. So the puck is
        /// allowed to touch exactly ONE cube; that cube transfers momentum through ordinary
        /// block-to-block contacts to the real pile.
        ///
        /// WHICH cube used to be "the one with the smallest x". That was the near side while the stack
        /// was a flat wall. The taper put the DEEPEST column at min-x, so the filter selected
        /// Cube (1) at x -36.209, z -15.750: seven columns behind the near face and 3.3u deeper than
        /// the face the puck actually meets. Every other cube was handed to Physics.IgnoreCollision,
        /// so the puck flew through 35 of 36 blocks - measured in the capture as its yellow pixel
        /// count going 3346 -> 2399 -> 3 -> 0 -> 0, gone for five frames, reappearing INSIDE the pile.
        /// The tie-break was degenerate on top of that: all eight cubes in the min-x column share x
        /// exactly, so `secondFromLeft` picked another cube in the same column by array order.
        ///
        /// The replacement asks the geometry instead of assuming an axis: cast the puck's own velocity
        /// forward and take the first cube the ray enters. That is the cube the puck is about to hit
        /// by definition, whatever the layout is, and it re-derives itself after every ricochet - the
        /// shot reaches the stack off the arch, so its approach direction is not knowable at the
        /// launch frame at all, which is when the old filter was committing to one.
        ///
        /// Deliberately reads NO stale constant. The old version gated candidates to the bottom layer
        /// with `blockSize * 0.30f`, and blockSize is 0.4667 against an actual block of
        /// 0.4354 x 1.240 x 0.4456. The cast is done in XZ only, which is the honest plane: the puck
        /// body carries RigidbodyConstraints.FreezePositionY.
        /// </summary>
        void FilterPuckToOneImpactBlock(Collider puckCollider)
        {
            ReleasePuckCollisionFilter();
            _filterPuck = puckCollider;
            _filterLocked = false;
            // No primary yet, on purpose. At the launch frame the puck's velocity is still zero, so no
            // approach axis exists to read; committing here is what forced the old code to guess one.
            // The puck is the length of the arena away from the stack at this point, so there is no
            // ambiguous contact to protect against yet. UpdateApproachFilter picks the block as soon
            // as the puck is actually travelling.
        }

        Collider _filterPuck;
        bool _filterLocked;

        void FixedUpdate()
        {
            if (!_armed || _cascadeStarted || _filterLocked || _filterPuck == null) return;
            UpdateApproachFilter();
        }

        /// <summary>
        /// Re-derives the impact block from the puck's actual heading and re-points the collision
        /// filter at it. Locks once contact is imminent, so the choice cannot flip on the contact
        /// frame itself.
        /// </summary>
        void UpdateApproachFilter()
        {
            Rigidbody prb = _filterPuck.attachedRigidbody;
            if (prb == null) return;
            Vector3 v = prb.linearVelocity; v.y = 0f;
            if (v.sqrMagnitude < 0.25f) return;          // not travelling: no approach axis exists yet
            Vector3 dir = v.normalized;
            Bounds pb = _filterPuck.bounds;
            Vector3 origin = pb.center; origin.y = 0f;
            float radius = Mathf.Max(pb.extents.x, pb.extents.z);

            Block hit = null; float hitT = float.MaxValue;
            Block nearest = null; float nearestPerp = float.MaxValue;
            for (int i = 0; i < _blocks.Count; i++)
            {
                Block b = _blocks[i];
                if (b.Tr == null || b.Col == null) continue;
                Vector3 c = b.Tr.position; c.y = 0f;
                Vector3 h = RotatedHalfExtents(b.Tr.rotation, RestHalfExtents(b));
                float t;
                if (RayBoxXZ(origin, dir, c, h.x + radius, h.z + radius, out t) && t < hitT)
                { hitT = t; hit = b; }

                Vector3 d = c - origin;
                float along = Vector3.Dot(d, dir);
                if (along > 0f)
                {
                    float perp = (d - dir * along).magnitude;
                    if (perp < nearestPerp) { nearestPerp = perp; nearest = b; }
                }
            }

            // Nothing on the ray means the puck is not pointed at the stack this instant - mid-ricochet,
            // for one thing. Fall back to the block closest to the heading rather than dropping the
            // filter, so there is always exactly one live contact candidate.
            // Commit on a REAL ray hit. The nearest-block fallback exists only so there is always
            // exactly one live contact candidate; re-running it every fixed step while the puck is
            // still on its outbound leg reshuffled 36 IgnoreCollision pairs nine times for nothing,
            // and the block it names during that leg is meaningless anyway - the puck is pointing
            // away from the stack. Once a primary exists, only a ray hit may replace it.
            Block primary = hit;
            if (primary == null)
            {
                if (_primaryImpactCollider != null) return;
                primary = nearest;
            }
            if (primary == null) return;

            if (primary.Col != _primaryImpactCollider)
            {
                ApplyPuckCollisionFilter(primary);
                Shared.Sequencing.SeqLog.Info(string.Format(
                    "[Case4] IMPACT_FILTER primary={0} pos=({1:0.000},{2:0.000}) rayT={3} " +
                    "puck=({4:0.000},{5:0.000}) heading=({6:0.00},{7:0.00})",
                    primary.Tr.name, primary.Tr.position.x, primary.Tr.position.z,
                    hit != null ? hitT.ToString("0.000") : "none(nearest)",
                    origin.x, origin.z, dir.x, dir.z));
            }
            if (hit != null && hitT <= radius * 2f + 0.05f)
            {
                _filterLocked = true;
                // Logged separately from the selection above, which only prints on a CHANGE. Without
                // this line a run in which the ray never fired and the fallback happened to name the
                // right block is indistinguishable from one in which the ray did the work.
                Shared.Sequencing.SeqLog.Info(string.Format("[Case4] IMPACT_FILTER_LOCK primary={0} rayT={1:0.000} at puck=({2:0.000},{3:0.000})",
                    primary.Tr.name, hitT, origin.x, origin.z));
            }
        }

        /// <summary>Ray against an axis-aligned box in the XZ plane. Returns the entry distance.</summary>
        static bool RayBoxXZ(Vector3 o, Vector3 d, Vector3 c, float hx, float hz, out float t)
        {
            t = 0f;
            float tmin = 0f, tmax = float.MaxValue;
            if (!Slab(o.x, d.x, c.x, hx, ref tmin, ref tmax)) return false;
            if (!Slab(o.z, d.z, c.z, hz, ref tmin, ref tmax)) return false;
            t = tmin;
            return true;
        }

        static bool Slab(float o, float d, float c, float h, ref float tmin, ref float tmax)
        {
            if (Mathf.Abs(d) < 1e-6f) return Mathf.Abs(o - c) <= h;
            float inv = 1f / d;
            float t1 = (c - h - o) * inv;
            float t2 = (c + h - o) * inv;
            if (t1 > t2) { float tmp = t1; t1 = t2; t2 = tmp; }
            tmin = Mathf.Max(tmin, t1);
            tmax = Mathf.Min(tmax, t2);
            return tmin <= tmax;
        }

        void ApplyPuckCollisionFilter(Block primary)
        {
            if (_filterPuck == null) return;
            if (_filteredPuckCollider != null)
                for (int i = 0; i < _blocks.Count; i++)
                    if (_blocks[i].Col != null) Physics.IgnoreCollision(_filteredPuckCollider, _blocks[i].Col, false);

            _filteredPuckCollider = _filterPuck;
            _primaryImpactCollider = primary.Col;
            _chosenImpactCollider = primary.Col;
            for (int i = 0; i < _blocks.Count; i++)
            {
                Collider col = _blocks[i].Col;
                if (col != null && col != _primaryImpactCollider)
                    Physics.IgnoreCollision(_filteredPuckCollider, col, true);
            }
        }

        Collider _chosenImpactCollider;

        /// <summary>The cube the puck is currently allowed to hit, or null before the approach begins.</summary>
        public Collider PrimaryImpactCollider { get { return _primaryImpactCollider; } }

        /// <summary>
        /// The cube the approach filter selected, kept past the end of the run.
        /// <see cref="BeginDeterministicCascade"/> releases the live filter on the contact frame, which
        /// nulls <see cref="PrimaryImpactCollider"/> long before any gate can compare it with the
        /// collider the solver actually reported. Cleared by <see cref="ArmPhysics"/>, i.e. once per run.
        /// </summary>
        public Collider ChosenImpactCollider { get { return _chosenImpactCollider; } }

        /// <summary>Restores ordinary puck collision with every cube when the run resets.</summary>
        public void ReleasePuckCollisionFilter()
        {
            if (_filteredPuckCollider != null)
            {
                for (int i = 0; i < _blocks.Count; i++)
                {
                    Collider col = _blocks[i].Col;
                    if (col != null) Physics.IgnoreCollision(_filteredPuckCollider, col, false);
                }
            }
            _filteredPuckCollider = null;
            _primaryImpactCollider = null;
            _filterPuck = null;
            _filterLocked = false;
        }

        /// <summary>
        /// Starts the measured 1.4 second fan-out after the puck's real solver contact. Every endpoint,
        /// delay and rotation comes from a local integer hash, never UnityEngine.Random.
        /// </summary>
        public void BeginDeterministicCascade()
        {
            if (_cascadeStarted) return;
            _cascadeStarted = true;
            ReleasePuckCollisionFilter();
            _cascade = StartCoroutine(DeterministicCascade());
        }

        IEnumerator DeterministicCascade()
        {
            for (int i = 0; i < _blocks.Count; i++)
            {
                Block b = _blocks[i];
                if (b.Rb != null)
                {
                    if (!b.Rb.isKinematic)
                    {
                        b.Rb.linearVelocity = Vector3.zero;
                        b.Rb.angularVelocity = Vector3.zero;
                    }
                    b.Rb.isKinematic = true;
                    b.Rb.detectCollisions = false;
                }
                if (b.Col != null) b.Col.enabled = false;
                b.Tr.SetPositionAndRotation(b.RestPos, b.RestRot);
            }
            Physics.SyncTransforms();

            List<CascadePose> poses = PlanCascade();

            float started = Time.time;
            float total = 0f;
            for (int i = 0; i < poses.Count; i++)
                total = Mathf.Max(total, poses[i].Delay + poses[i].Duration);

            while (Time.time - started < total)
            {
                float elapsed = Time.time - started;
                for (int i = 0; i < poses.Count; i++)
                {
                    CascadePose pose = poses[i];
                    float raw = (elapsed - pose.Delay) / pose.Duration;
                    if (raw <= 0f) continue;
                    float t = Mathf.Clamp01(raw);
                    float move = t < 0.20f
                        ? 0.80f * Mathf.Pow(t / 0.20f, 0.80f)
                        : 0.80f + 0.20f * Mathf.SmoothStep(0f, 1f, (t - 0.20f) / 0.80f);
                    float arcH = t < 0.20f
                        ? Mathf.Sin(t / 0.20f * Mathf.PI) * pose.Arc
                        : Mathf.Sin((t - 0.20f) / 0.80f * Mathf.PI) * (pose.Arc * 0.15f);
                    Vector3 p = Vector3.Lerp(pose.RestPos, pose.EndPos, move);
                    p.y += arcH;
                    pose.Tr.SetPositionAndRotation(
                        p, Quaternion.Slerp(pose.RestRot, pose.EndRot, move));
                }
                yield return null;
            }

            for (int i = 0; i < poses.Count; i++)
                poses[i].Tr.SetPositionAndRotation(poses[i].EndPos, poses[i].EndRot);
            Physics.SyncTransforms();
            _cascade = null;
        }

        /// <summary>
        /// The authored settle poses, computed with no side effects. <see cref="DeterministicCascade"/>
        /// plays exactly this list, and Case4SettleProbe measures exactly this list, so a settled pose
        /// can never be measured as one thing and rendered as another.
        /// </summary>
        /// <summary>
        /// World half-size of a block in its REST orientation, read from the renderer's own mesh
        /// bounds rather than assumed to be a unit cube. blockSize/blockPitch are NOT used here: they
        /// are stale against every real dimension in this scene (0.4666 declared against an actual
        /// 0.4354 x 1.240 x 0.4456), and a settle height derived from them would be wrong by 0.15u.
        /// </summary>
        Vector3 RestHalfExtents(Block b)
        {
            Vector3 localExtents = new Vector3(0.5f, 0.5f, 0.5f);
            if (b.Rend != null) localExtents = b.Rend.localBounds.extents;
            Vector3 sc = b.RestScale;
            return new Vector3(Mathf.Abs(localExtents.x * sc.x),
                               Mathf.Abs(localExtents.y * sc.y),
                               Mathf.Abs(localExtents.z * sc.z));
        }

        /// <summary>
        /// Half-height of an oriented box along world +Y: the exact y half-extent of its world AABB.
        /// </summary>
        public static float RotatedHalfHeight(Quaternion rot, Vector3 half)
        {
            return Mathf.Abs((rot * Vector3.right).y)   * half.x
                 + Mathf.Abs((rot * Vector3.up).y)      * half.y
                 + Mathf.Abs((rot * Vector3.forward).y) * half.z;
        }

        /// <summary>World AABB half-size of an oriented box.</summary>
        public static Vector3 RotatedHalfExtents(Quaternion rot, Vector3 half)
        {
            Vector3 rx = rot * Vector3.right, ry = rot * Vector3.up, rz = rot * Vector3.forward;
            return new Vector3(
                Mathf.Abs(rx.x) * half.x + Mathf.Abs(ry.x) * half.y + Mathf.Abs(rz.x) * half.z,
                Mathf.Abs(rx.y) * half.x + Mathf.Abs(ry.y) * half.y + Mathf.Abs(rz.y) * half.z,
                Mathf.Abs(rx.z) * half.x + Mathf.Abs(ry.z) * half.y + Mathf.Abs(rz.z) * half.z);
        }

        public List<CascadePose> PlanCascade()
        {
            float floorTopY = float.MaxValue;
            float minX = float.MaxValue;
            float maxX = float.MinValue;
            for (int i = 0; i < _blocks.Count; i++)
            {
                Block b = _blocks[i];
                if (b.Tr == null) continue;
                // The surface the stack STANDS on, not the height its centres sit at. The blocks are
                // upright at rest, so this is the rest centre minus the block's own half-height, and
                // it comes out at exactly y=0.000 - the Floor collider's top face, independently
                // measured. Deriving it from the stack rather than from a named scene object keeps it
                // correct if the stack is ever moved or resized.
                floorTopY = Mathf.Min(floorTopY, b.RestPos.y - RotatedHalfHeight(b.RestRot, RestHalfExtents(b)));
                minX = Mathf.Min(minX, b.RestPos.x);
                maxX = Mathf.Max(maxX, b.RestPos.x);
            }

            List<CascadePose> poses = new List<CascadePose>(_blocks.Count);
            float width = Mathf.Max(blockPitch, maxX - minX);

            // Depth strata. The fitted distribution below is only meaningful if the 33 blocks
            // actually SAMPLE it, and 33 independent hash draws do not: the realised sample came out
            // at IQR/range 0.46 against the fitted 0.29 and put 48.5% of blocks past the rail where
            // the fit wanted ~32%. That was sampling luck, not a wrong model - the render matched the
            // hash's own realised draws to 0.4 points. Stratifying gives each block a different
            // quantile, deterministically shuffled so depth does not correlate with stack order, and
            // jittered inside the stratum rather than taken at its centre.
            int blockCount = _blocks.Count;
            int[] depthOrder = new int[blockCount];
            for (int k = 0; k < blockCount; k++) depthOrder[k] = k;
            System.Array.Sort(depthOrder, (p, q) => Sample01(p, 9).CompareTo(Sample01(q, 9)));
            int[] depthStratum = new int[blockCount];
            for (int k = 0; k < blockCount; k++) depthStratum[depthOrder[k]] = k;

            for (int i = 0; i < _blocks.Count; i++)
            {
                Block b = _blocks[i];
                if (b.Tr == null) continue;
                Vector3 half = RestHalfExtents(b);
                float column01 = Mathf.Clamp01((b.RestPos.x - minX) / width);
                float side = SampleSigned(i, 0);

                Vector3 end = b.RestPos;
                // LATERAL spread. Left as it is, deliberately, and this is the measurement that
                // says so - a fresh audit reported the opposite and it was reading contaminated
                // pixels.
                //
                // The reported claim was that the reference floods both lanes while ours stays in
                // its corner: "reference t=4.20 s, 25,029 of 78,715 debris px right of the divider
                // = 31.8%, x-sigma 258; ours frame_330, 4,427 of 50,735 = 8.7%, x-sigma 181".
                // Both halves of that are artefacts.
                //
                // 1. The mask. It was chroma > 60 minus cyan and gold. Of the 25,029 pixels it puts
                //    right of the divider in Buca.mp4 at t=4.20 s, only 3,222 are debris. 11,306 sit
                //    above y=400 (the HUD row and the "LEVEL 6 COMPLETE" caption) and 11,298 below
                //    y=1000 (the foreground boulder and the level-complete tint); their modal
                //    colours are blue-purple, (24,72,120) and (72,0,168), not block magenta.
                // 2. The sample point. t=4.20 s is inside the reference's level-complete removal
                //    animation, not its settle. The reference's magenta debris count runs 79k-84k
                //    over t=2.94..3.96 s and then collapses: 87,091 at t=4.04, 77,836 at 4.12,
                //    51,597 at 4.20, 17,453 at 4.28, 9,412 at 4.43.
                //
                // Measured on the settle plateau instead, with a mask that selects only the blocks'
                // own magenta (r,b > 90 and both at least 60 above g - no other object in either
                // frame wears it), reference Buca.mp4 f182 (t=3.57 s) against ours frame_330:
                //
                //     right of the divider   ref  3.1%   ours  8.2%
                //     x-sigma                ref  131 px ours  158 px
                //     debris clusters right of the divider   ref 0   ours 2
                //
                // So ours already crosses lanes MORE than the reference and is laterally MORE
                // spread, not less. Tightening it is not free either: the same frames give a
                // reference debris footprint of 79,898 px against our 48,672, and a median cluster
                // of 4,536 px against our 2,356 - our blocks cover about half the area the
                // reference's do. Fitting x-sigma now would be fitting on top of a known geometry
                // error, which is exactly why depthSpread was left alone when the deepest-debris
                // residual was found. The stack-geometry patch is still the prerequisite.
                // The settle rotation is needed BEFORE the height, because a tumbled block's world
                // half-height depends on it. Hoisted out of the object initialiser below; the three
                // expressions are unchanged.
                Quaternion endRot = Quaternion.Euler(
                    Mathf.Lerp(35f, 175f, Sample01(i, 5)),
                    Mathf.Lerp(-150f, 150f, Sample01(i, 6)),
                    Mathf.Lerp(-145f, 145f, Sample01(i, 7)));

                end.x += Mathf.Lerp(-0.75f, 2.65f, column01) + side * 1.10f;
                // REST ON THE FLOOR, do not sit the centre at the rest-centre height. `end.y = floorY`
                // was right while the blocks were 0.4667 cubes, where half-height equalled the rest
                // centre height by coincidence. They are 0.4354 x 1.240 x 0.4456 now, so a tumbled
                // block's world half-height runs 0.218..0.620 and the coincidence is gone: measured,
                // 31 of 36 blocks ended hovering, mean |gap| 0.176u, max 0.360u, and 3 clipped through
                // the floor. Debris stopped in mid-air with its contact shadow detached underneath it.
                end.y = floorTopY + RotatedHalfHeight(endRot, half);
                // Depth spread of the settled debris, fitted to the reference.
                //
                // The camera sits at z=-41.5 looking toward +Z, so -Z is TOWARD the viewer. The
                // original term was `spill + Lerp(0.20f, 4.80f, forward)`: strictly POSITIVE, so a
                // variable named `forward` threw every block AWAY from the camera. Measured mean
                // z-displacement was +1.87u and not one block pixel ever reached the rail (deepest
                // debris y=1122 against our own innermost rail line at y=1123).
                //
                // Merely negating it was also wrong - that put 97.6% of the debris mass forward of
                // the rest plane and emptied the arena. Measuring the reference's settled debris
                // (Buca.mp4 frame 183, unprojected onto the floor plane, expressed in block-widths
                // from the stack's own rest plane) gives a distribution that straddles zero:
                //     min -9.29  p25 -2.47  median -0.35  p75 +3.11  max +9.18  (46.2% forward)
                // IQR/range = 0.30, which is a symmetric triangular fit (0.293), not uniform (0.5)
                // and not a skew. Hence: triangular on +/-9.2 block-widths about the rest plane.
                // 9.2 * blockSize(0.46666) = 4.29 world units.
                // Triangular inverse CDF over the block's own jittered stratum.
                const float depthSpread = 4.29f;
                float u = (depthStratum[i] + Sample01(i, 10)) / blockCount;
                float tri = u < 0.5f
                    ? -1f + Mathf.Sqrt(2f * u)
                    :  1f - Mathf.Sqrt(2f * (1f - u));
                end.z += depthSpread * tri;

                poses.Add(new CascadePose
                {
                    Tr = b.Tr,
                    RestPos = b.RestPos,
                    RestRot = b.RestRot,
                    HalfExtents = half,
                    // Left-to-right cascade only. There used to be a `+ row * 0.012f` term here,
                    // recovering a row index as round((RestPos.y - floorY) / blockPitch). The stack
                    // is ONE layer now - all 36 blocks rest at world y = 0.620 - so floorY equalled
                    // every block's own y, the quotient was 0 for all of them, and the term added
                    // nothing. It went with the `floorY` accumulator that existed only to feed it.
                    Delay = column01 * 0.04f,
                    Duration = Mathf.Lerp(1.60f, 2.20f, Sample01(i, 3)),
                    Arc = Mathf.Lerp(1.20f, 2.80f, Sample01(i, 4)),
                    EndPos = end,
                    EndRot = endRot
                });
            }

            FitSettleArea(poses, floorTopY);
            return poses;
        }

        // ------------------------------------------------------------------ settle area

        Bounds _settleArea;
        int _settleAreaState;      // 0 unresolved, 1 resolved, -1 failed

        /// <summary>
        /// The box a settled block is allowed to occupy, read from the arena's own colliders.
        ///
        /// It is the LEFT LANE, not the whole arena: the Divider is a solid wall spanning
        /// z -17.636..-9.863, which covers the entire depth band the debris occupies, so no block can
        /// reach the right lane by going around its far end. Bounding by the right rail instead would
        /// permit exactly the five poses the audit found standing inside the divider.
        ///
        /// Resolved by EXACT object name, not by substring: a substring test is how Case 1 got a flag
        /// that matched its own project folder and silently killed seven shading branches. If any part
        /// is missing the area is marked failed and the fit is SKIPPED with an error rather than
        /// clamping every block into a garbage box.
        /// </summary>
        public bool TryGetSettleArea(out Bounds area)
        {
            if (_settleAreaState == 0)
            {
                Bounds l, r, b, a, d;
                bool ok = ExactBounds("Rail_Left", out l) & ExactBounds("Rail_Bottom", out b)
                        & ExactBounds("Rail_Arch", out a) & ExactBounds("Rail_Right", out r);
                bool hasDivider = ExactBounds("Divider", out d);
                if (!ok)
                {
                    _settleAreaState = -1;
                    Debug.LogError("[Case4] SETTLE_AREA_UNRESOLVED: the arena rails were not all found by name; " +
                                   "settled poses will NOT be fitted to the arena this run");
                }
                else
                {
                    float x0 = l.max.x;
                    float x1 = hasDivider && d.min.x > x0 && d.min.x < r.min.x ? d.min.x : r.min.x;
                    float z0 = b.max.z;
                    float z1 = a.min.z;
                    float y0 = 0f;
                    _settleArea = new Bounds(
                        new Vector3((x0 + x1) * 0.5f, y0, (z0 + z1) * 0.5f),
                        new Vector3(x1 - x0, 0f, z1 - z0));
                    _settleAreaState = 1;
                    Shared.Sequencing.SeqLog.Info(string.Format("[Case4] SETTLE_AREA x {0:0.000}..{1:0.000}  z {2:0.000}..{3:0.000} " +
                                            "(right bound = {4})", x0, x1, z0, z1,
                                            hasDivider && x1 < r.min.x ? "Divider" : "Rail_Right"));
                }
            }
            area = _settleArea;
            return _settleAreaState == 1;
        }

        static bool ExactBounds(string name, out Bounds b)
        {
            b = default(Bounds);
            GameObject go = GameObject.Find(name);
            if (go == null) return false;
            Collider c = go.GetComponent<Collider>();
            if (c != null) { b = c.bounds; return true; }
            Renderer r = go.GetComponent<Renderer>();
            if (r != null) { b = r.bounds; return true; }
            return false;
        }

        /// <summary>
        /// Fits the planned settle offsets into the arena.
        ///
        /// The lateral and depth magnitudes above - Lerp(-0.75, 2.65), side*1.10, depthSpread 4.29 -
        /// were all fitted when the stack was a flat wall 3.9u wide and ZERO units deep, and 4.29 in
        /// particular is 9.2 x blockSize(0.46666), a product of two numbers that are both stale. On a
        /// 3.5 x 3.6u formation standing 0.24u off the front rail they throw 19 of 36 blocks out of
        /// the arena: through Rail_Left past its OUTER face, and 2.6u past Rail_Bottom toward the
        /// camera, beyond the floor's own edge. They are treated here as the DESIRED spread, an upper
        /// bound, not as truth.
        ///
        /// Fitting is done in two stages, and the order matters:
        ///
        ///  1. Four one-sided scale factors (x-, x+, z-, z+), each the largest value in [0,1] that
        ///     keeps every block on that side inside the arena. Scaling is a similarity transform, so
        ///     the distribution's SHAPE - which is the part fitted to the reference - is preserved.
        ///     Clamping alone would not: it piles every over-travelling block flat against the wall
        ///     in a line, which is a worse artefact than the one being fixed.
        ///  2. A residual per-block clamp, which guarantees the invariant outright. It only ever
        ///     catches blocks whose settle ROTATION makes them too wide to fit at their own rest
        ///     column - the leftmost column stands 0.42u from the rail and a block rotated onto its
        ///     long diagonal has an x half-extent up to 0.69u. Stage 1 cannot fix those by scaling
        ///     because their offset is not what puts them out.
        ///
        /// Both stages are logged, so a future layout change that compresses the fan to nothing shows
        /// up as a number rather than as a silently flatter collapse.
        ///
        /// blockSize and blockPitch are deliberately NOT retuned here. They are stale (0.4667/0.4676
        /// against an actual 0.4354 x 1.240 x 0.4456 and pitches 0.4398/0.4500), but they also feed
        /// the impact filter's y-tolerance and the gate's own MovedCount threshold, so correcting them
        /// is a separate change with its own blast radius. Nothing in this method reads either of them.
        /// </summary>
        void FitSettleArea(List<CascadePose> poses, float floorTopY)
        {
            Bounds area;
            if (!TryGetSettleArea(out area)) return;
            float ax0 = area.min.x, ax1 = area.max.x, az0 = area.min.z, az1 = area.max.z;

            float sxNeg = 1f, sxPos = 1f, szNeg = 1f, szPos = 1f;
            for (int i = 0; i < poses.Count; i++)
            {
                CascadePose q = poses[i];
                Vector3 h = RotatedHalfExtents(q.EndRot, q.HalfExtents);
                Fit(q.RestPos.x, q.EndPos.x - q.RestPos.x, ax0 + h.x, ax1 - h.x, ref sxNeg, ref sxPos);
                Fit(q.RestPos.z, q.EndPos.z - q.RestPos.z, az0 + h.z, az1 - h.z, ref szNeg, ref szPos);
            }

            int clamped = 0; float worstClamp = 0f;
            for (int i = 0; i < poses.Count; i++)
            {
                CascadePose q = poses[i];
                Vector3 h = RotatedHalfExtents(q.EndRot, q.HalfExtents);
                float offX = (q.EndPos.x - q.RestPos.x);
                float offZ = (q.EndPos.z - q.RestPos.z);
                Vector3 end = q.EndPos;
                end.x = q.RestPos.x + offX * (offX >= 0f ? sxPos : sxNeg);
                end.z = q.RestPos.z + offZ * (offZ >= 0f ? szPos : szNeg);

                float bx = Clamp(end.x, ax0 + h.x, ax1 - h.x);
                float bz = Clamp(end.z, az0 + h.z, az1 - h.z);
                float moved = Mathf.Abs(bx - end.x) + Mathf.Abs(bz - end.z);
                if (moved > 1e-4f) { clamped++; worstClamp = Mathf.Max(worstClamp, moved); }
                end.x = bx; end.z = bz;
                end.y = floorTopY + RotatedHalfHeight(q.EndRot, q.HalfExtents);
                q.EndPos = end;
            }

            Shared.Sequencing.SeqLog.Info(string.Format(
                "[Case4] SETTLE_FIT scales x-={0:0.000} x+={1:0.000} z-={2:0.000} z+={3:0.000}; " +
                "residual clamp on {4}/{5} blocks, worst {6:0.000}u",
                sxNeg, sxPos, szNeg, szPos, clamped, poses.Count, worstClamp));
        }

        /// <summary>
        /// Largest one-sided scale in [0,1] that keeps rest+scale*offset inside [lo,hi]. A block whose
        /// REST position is already outside the band, or whose rotated width exceeds the band
        /// entirely, cannot be fixed by scaling its offset and is left to the residual clamp - folding
        /// it into the scale would drag the factor to zero and flatten the whole fan for one block.
        /// </summary>
        static void Fit(float rest, float off, float lo, float hi, ref float negScale, ref float posScale)
        {
            if (lo > hi) return;
            if (rest < lo || rest > hi) return;
            if (off > 1e-4f && rest + off > hi) posScale = Mathf.Min(posScale, Mathf.Max(0f, (hi - rest) / off));
            else if (off < -1e-4f && rest + off < lo) negScale = Mathf.Min(negScale, Mathf.Max(0f, (lo - rest) / off));
        }

        static float Clamp(float v, float lo, float hi)
        {
            if (lo > hi) return (lo + hi) * 0.5f;
            return Mathf.Clamp(v, lo, hi);
        }


        static float Sample01(int index, int salt)
        {
            uint x = 0xB0CA4u + (uint)(index + 1) * 0x9E3779B9u + (uint)(salt + 1) * 0x85EBCA6Bu;
            x ^= x >> 16;
            x *= 0x7FEB352Du;
            x ^= x >> 15;
            x *= 0x846CA68Bu;
            x ^= x >> 16;
            return (x & 0x00FFFFFFu) / 16777215f;
        }

        static float SampleSigned(int index, int salt)
        {
            return Sample01(index, salt) * 2f - 1f;
        }

        /// <summary>Compatibility alias for the old scripted-shatter API; a reset is all a replay needs now.</summary>
        public void Clear() { ResetInstant(); }

        // ------------------------------------------------------------------ reporting

        /// <summary>How many blocks the stack holds.</summary>
        public int BlockCount { get { return _blocks.Count; } }

        /// <summary>Kept for the report: the stack is never fractured, so every moving body is a whole block.</summary>
        public int PieceCount { get { return _blocks.Count; } }

        /// <summary>Fragments produced this run. Zero by design: the reference topples, it does not shatter.</summary>
        public int FragmentCount { get { return 0; } }

        /// <summary>Blocks that still exist as one whole cube (all of them, since nothing is fractured).</summary>
        public int WholeFormCount
        {
            get
            {
                int n = 0;
                for (int i = 0; i < _blocks.Count; i++) if (_blocks[i].Tr != null && _blocks[i].Rend != null && _blocks[i].Rend.enabled) n++;
                return n;
            }
        }

        /// <summary>Blocks that travelled further than <paramref name="minDistance"/> from their rest pose.</summary>
        public int MovedCount(float minDistance)
        {
            int n = 0;
            for (int i = 0; i < _blocks.Count; i++)
            {
                Block b = _blocks[i];
                if (b.Tr == null) continue;
                if (Vector3.Distance(b.Tr.position, b.RestPos) >= minDistance) n++;
            }
            return n;
        }

        /// <summary>Blocks whose orientation changed by more than <paramref name="minDegrees"/>: the toppling proof.</summary>
        public int RotatedCount(float minDegrees)
        {
            int n = 0;
            for (int i = 0; i < _blocks.Count; i++)
            {
                Block b = _blocks[i];
                if (b.Tr == null) continue;
                if (Quaternion.Angle(b.Tr.rotation, b.RestRot) >= minDegrees) n++;
            }
            return n;
        }

        /// <summary>True as soon as anything in the stack has been knocked out of place.</summary>
        public bool AnyDisturbed(float minDistance)
        {
            for (int i = 0; i < _blocks.Count; i++)
            {
                Block b = _blocks[i];
                if (b.Tr == null) continue;
                if (Vector3.Distance(b.Tr.position, b.RestPos) >= minDistance) return true;
                if (b.Rb != null && !b.Rb.isKinematic && b.Rb.linearVelocity.sqrMagnitude > 0.35f) return true;
            }
            return false;
        }

        /// <summary>Blocks that have come to rest again; used to end the collapse phase when it is really over.</summary>
        public int AsleepCount
        {
            get
            {
                int n = 0;
                for (int i = 0; i < _blocks.Count; i++)
                {
                    Rigidbody rb = _blocks[i].Rb;
                    if (rb == null) continue;
                    if (rb.isKinematic || rb.IsSleeping() || rb.linearVelocity.sqrMagnitude < 0.06f) n++;
                }
                return n;
            }
        }

        /// <summary>Slowest-moving proof line for the log: how far the furthest block ended up.</summary>
        /// <summary>
        /// Blocks still sitting in their rest pose - neither displaced nor rotated. This is the
        /// honest form of "the cascade actually ran": a block counts as disturbed if it moved OR
        /// turned, so a block whose depth stratum happens to land near zero still counts as long as
        /// it tumbled. Replaces the old per-block displacement census, which demanded that all 33
        /// blocks exceed a distance threshold the REFERENCE itself does not meet (its own median
        /// forward travel is -0.35 block-widths, about 0.16u, under the 0.233u bar).
        /// </summary>
        public int UndisturbedCount(float minDistance, float minDegrees)
        {
            int n = 0;
            for (int i = 0; i < _blocks.Count; i++)
            {
                Block b = _blocks[i];
                if (b.Tr == null) continue;
                bool moved = Vector3.Distance(b.Tr.position, b.RestPos) >= minDistance;
                bool turned = Quaternion.Angle(b.Tr.rotation, b.RestRot) >= minDegrees;
                if (!moved && !turned) n++;
            }
            return n;
        }

        /// <summary>
        /// How far the formation has opened out: settled XZ footprint area over rest XZ footprint
        /// area. This is the property the old "every block moved" clause was standing in for - that
        /// the stack no longer holds its shape - and unlike a per-block threshold it survives a
        /// reference-matched depth spread in which some blocks barely travel.
        /// </summary>
        public float FormationSpread()
        {
            if (_blocks.Count == 0) return 0f;
            float rx0 = float.MaxValue, rx1 = float.MinValue, rz0 = float.MaxValue, rz1 = float.MinValue;
            float sx0 = float.MaxValue, sx1 = float.MinValue, sz0 = float.MaxValue, sz1 = float.MinValue;
            for (int i = 0; i < _blocks.Count; i++)
            {
                Block b = _blocks[i];
                if (b.Tr == null) continue;
                rx0 = Mathf.Min(rx0, b.RestPos.x); rx1 = Mathf.Max(rx1, b.RestPos.x);
                rz0 = Mathf.Min(rz0, b.RestPos.z); rz1 = Mathf.Max(rz1, b.RestPos.z);
                Vector3 p = b.Tr.position;
                sx0 = Mathf.Min(sx0, p.x); sx1 = Mathf.Max(sx1, p.x);
                sz0 = Mathf.Min(sz0, p.z); sz1 = Mathf.Max(sz1, p.z);
            }
            // The rest formation is one block deep, so pad both extents by a block to keep the
            // ratio finite and meaningful rather than dividing by a near-zero rest depth.
            float restArea    = (rx1 - rx0 + blockSize) * (rz1 - rz0 + blockSize);
            float settledArea = (sx1 - sx0 + blockSize) * (sz1 - sz0 + blockSize);
            return restArea <= 0.0001f ? 0f : settledArea / restArea;
        }

        // ------------------------------------------------------------------ census for the gate
        //
        // Every one of these reads Unity's own Renderer.bounds off the live transform, NOT the
        // placement formula in PlanCascade. A gate that measured a settled block with the same
        // function that placed it would report zero error whether or not the block was where it
        // claimed - a tautology, not an assertion. That is the whole reason findings 1-4 survived:
        // nothing the gate read could be falsified by any of them.

        /// <summary>
        /// Blocks the puck passed THROUGH to reach its contact point.
        ///
        /// This exists because the obvious assertion - "the block the filter chose is the block the
        /// solver reported" - is a tautology and was measured to be one. The filter hands every other
        /// cube to Physics.IgnoreCollision, so the puck always ends up contacting the one cube it is
        /// permitted to contact, however deep inside the pile that cube is. Restoring the old
        /// smallest-x rule as a negative control left the gate GREEN on that line: chose 'Cube',
        /// contacted 'Cube', and the puck had flown through seven columns to get there.
        ///
        /// So this measures the thing that is actually wrong instead: walk back along the puck's own
        /// contact heading and count the cubes whose rest volume lies between the outside world and
        /// the contact point. A puck that hit the near face passes through none. A puck routed to a
        /// cube behind the stack passes through everything in front of it - which is what the capture
        /// saw as the puck's yellow pixel count dropping to 0 for five frames and reappearing inside
        /// the pile.
        ///
        /// Filter-independent by construction: it never asks which cube was selected or why.
        /// </summary>
        public int BlocksPassedThrough(Vector3 impactPoint, Vector3 heading, float puckRadius,
                                       Collider contacted, out string firstBlockedBy)
        {
            firstBlockedBy = "-";
            Vector3 dir = new Vector3(heading.x, 0f, heading.z);
            if (dir.sqrMagnitude < 1e-6f) return -1;
            dir.Normalize();

            Vector3 origin = new Vector3(impactPoint.x, 0f, impactPoint.z) - dir * 24f;
            float tContact = Vector3.Dot(new Vector3(impactPoint.x, 0f, impactPoint.z) - origin, dir);

            int n = 0; float firstT = float.MaxValue;
            for (int i = 0; i < _blocks.Count; i++)
            {
                Block b = _blocks[i];
                if (b.Tr == null || b.Col == null || b.Col == contacted) continue;
                Vector3 c = new Vector3(b.RestPos.x, 0f, b.RestPos.z);
                Vector3 h = RotatedHalfExtents(b.RestRot, RestHalfExtents(b));
                float t;
                if (!RayBoxXZ(origin, dir, c, h.x + puckRadius, h.z + puckRadius, out t)) continue;
                if (t < tContact - 0.02f)
                {
                    n++;
                    if (t < firstT) { firstT = t; firstBlockedBy = b.Tr.name; }
                }
            }
            return n;
        }

        /// <summary>The surface the stack stands on: rest centre minus the block's own half-height.</summary>
        public float FloorTopY()
        {
            float y = float.MaxValue;
            for (int i = 0; i < _blocks.Count; i++)
            {
                Block b = _blocks[i];
                if (b.Tr == null) continue;
                y = Mathf.Min(y, b.RestPos.y - RotatedHalfHeight(b.RestRot, RestHalfExtents(b)));
            }
            return y == float.MaxValue ? 0f : y;
        }

        /// <summary>Pairs of blocks whose REST poses interpenetrate. Must be zero: ArmPhysics makes
        /// all 36 live on one frame, and an overlapping pile asks PhysX to resolve that on the launch
        /// frame.</summary>
        public int RestOverlapPairs(out float worst)
        {
            worst = 0f;
            int n = 0;
            for (int i = 0; i < _blocks.Count; i++)
            {
                if (_blocks[i].Tr == null) continue;
                Vector3 hi = RotatedHalfExtents(_blocks[i].RestRot, RestHalfExtents(_blocks[i]));
                for (int j = i + 1; j < _blocks.Count; j++)
                {
                    if (_blocks[j].Tr == null) continue;
                    Vector3 hj = RotatedHalfExtents(_blocks[j].RestRot, RestHalfExtents(_blocks[j]));
                    Vector3 d = _blocks[i].RestPos - _blocks[j].RestPos;
                    float pen = Mathf.Min(hi.x + hj.x - Mathf.Abs(d.x),
                                Mathf.Min(hi.y + hj.y - Mathf.Abs(d.y),
                                          hi.z + hj.z - Mathf.Abs(d.z)));
                    if (pen > 1e-4f) { n++; worst = Mathf.Max(worst, pen); }
                }
            }
            return n;
        }

        /// <summary>Blocks whose CURRENT rendered bounds stick out of the arena. Must be zero.</summary>
        public int OutsideArenaCount(out float worst)
        {
            worst = 0f;
            Bounds area;
            if (!TryGetSettleArea(out area)) return -1;   // unresolved: say so, do not report a false zero
            int n = 0;
            for (int i = 0; i < _blocks.Count; i++)
            {
                Renderer r = _blocks[i].Rend;
                if (r == null) continue;
                Bounds b = r.bounds;
                float over = Mathf.Max(Mathf.Max(area.min.x - b.min.x, b.max.x - area.max.x),
                                       Mathf.Max(area.min.z - b.min.z, b.max.z - area.max.z));
                if (over > 1e-3f) { n++; worst = Mathf.Max(worst, over); }
            }
            return n;
        }

        /// <summary>Blocks not resting on the floor: their rendered bottom is off it by more than
        /// <paramref name="tolerance"/>, above or below.</summary>
        public int OffFloorCount(float tolerance, out float worst)
        {
            worst = 0f;
            float top = FloorTopY();
            int n = 0;
            for (int i = 0; i < _blocks.Count; i++)
            {
                Renderer r = _blocks[i].Rend;
                if (r == null) continue;
                float gap = r.bounds.min.y - top;
                if (Mathf.Abs(gap) > tolerance) { n++; if (Mathf.Abs(gap) > Mathf.Abs(worst)) worst = gap; }
            }
            return n;
        }

        public float MaxDisplacement()
        {
            float best = 0f;
            for (int i = 0; i < _blocks.Count; i++)
            {
                Block b = _blocks[i];
                if (b.Tr == null) continue;
                best = Mathf.Max(best, Vector3.Distance(b.Tr.position, b.RestPos));
            }
            return best;
        }

        /// <summary>True while the bodies are live physics (never kinematic during a run).</summary>
        public bool Armed { get { return _armed; } }

        // ------------------------------------------------------------------ geometry

        /// <summary>Centre of the stack's rest pose.</summary>
        public Vector3 StackCenter()
        {
            if (_blocks.Count == 0) return transform.position;
            Vector3 sum = Vector3.zero;
            for (int i = 0; i < _blocks.Count; i++) sum += _blocks[i].RestPos;
            return sum / _blocks.Count;
        }

        /// <summary>Compatibility alias used by the director's prewarm.</summary>
        public Vector3 WallCenter() { return StackCenter(); }

        /// <summary>Highest block of the rest pose.</summary>
        public Vector3 StackTop()
        {
            if (_blocks.Count == 0) return transform.position;
            Vector3 best = _blocks[0].RestPos;
            for (int i = 1; i < _blocks.Count; i++) if (_blocks[i].RestPos.y > best.y) best = _blocks[i].RestPos;
            return best;
        }

        // ------------------------------------------------------------------ look

        /// <summary>
        /// Evolves the debris color: green -> mustard -> red -> magenta, matching the reference.
        /// </summary>
        /// <remarks>
        /// This routine was deleted in c26eef9 on the strength of the claim that "the reference stack
        /// retains its bright neon green base throughout the entire sequence". That claim is false.
        /// Colour-mask pixel counts over the gameplay band (y288-1152) of the reference frames in
        /// docs/verify/case4/ref, green / red / magenta:
        ///
        ///     1.80s    41778     163        0
        ///     2.10s    58737   17437       35
        ///     2.60s        0   54765     9038
        ///     3.00s        0    1838    51594
        ///     3.40s        0    1644    48019
        ///
        /// The green mask is exactly 0 from 2.60s onward, while the combined coloured debris holds at
        /// 50-64k the whole way. The same fragments are changing hue; no new ones arrive. Our own
        /// debris was measured at 50520 px over x2-597 / y551-833 at 2.60s against the reference's
        /// 54765 px over x20-579 / y480-863 -- same mass, same footprint, wrong colour. Deleting this
        /// again means arguing with those numbers rather than with a recollection of the clip.
        ///
        /// Stage lengths are set from the reference, not inherited. Taking its impact as ~1.90s (its
        /// stack is intact at 1.80s and shattered at 2.10s), it is fully red by impact+0.70 and fully
        /// magenta by impact+1.10. The schedule deleted in c26eef9 reached only mustard by +0.70 and
        /// red by +1.10, a 0.45s lag, so the stages below are compressed to hit the reference's marks.
        ///
        /// Anchored to impact, deliberately, not to absolute sequence time. Our impact currently fires
        /// about 0.45s early against the reference, so these colours lead the reference's at any fixed
        /// timestamp. Retiming this effect to cancel that would hide the timeline fault inside a second
        /// compensating fault, and the hidden one would surface as silently broken colour the moment
        /// the timeline is corrected. One visible bug is better than two entangled ones.
        /// </remarks>
        public void StartColorEvolution()
        {
            if (_runtimeMat == null) return;
            if (_colorEvolution != null) StopCoroutine(_colorEvolution);
            _colorEvolution = StartCoroutine(EvolveColorsRoutine());
        }

        IEnumerator EvolveColorsRoutine()
        {
            yield return WaitScaled(0.15f);
            yield return TransitionColor(blockBaseColor, Mustard, 0.25f);   // fully mustard at +0.40
            yield return TransitionColor(Mustard, Red, 0.30f);              // fully red     at +0.70
            yield return TransitionColor(Red, Magenta, 0.40f);              // fully magenta at +1.10
            _colorEvolution = null;
        }

        IEnumerator TransitionColor(Color from, Color to, float duration)
        {
            float started = Time.time;
            while (Time.time - started < duration)
            {
                float t = Mathf.Clamp01((Time.time - started) / duration);
                t = Mathf.SmoothStep(0f, 1f, t);
                Color c = Color.Lerp(from, to, t);
                ApplyColor(c, Color.Lerp(EmissionFor(from), EmissionFor(to), t));
                yield return null;
            }
            ApplyColor(to, EmissionFor(to));
        }

        static IEnumerator WaitScaled(float seconds)
        {
            float until = Time.time + seconds;
            while (Time.time < until) yield return null;
        }

        static Color EmissionFor(Color c)
        {
            return new Color(c.r * 0.18f, c.g * 0.18f, c.b * 0.18f, 1f);
        }

        /// <summary>
        /// Recolours the blocks. Writing _BaseColor alone does nothing visible: the blocks render with
        /// Case4/SoftBlock, whose fragment builds every face from _TopColor / _FrontColor / _SideColor
        /// and only falls back to _BaseColor when isTop + isFront + isSide &lt; 0.1, which axis-aligned
        /// cubes essentially never hit. That shader also declares no _EmissionColor at all, so the
        /// emission write below is a silent no-op for it (kept for materials that do have one).
        ///
        /// This is why the restored colour story first appeared to "start and then revert": it never
        /// rendered at all. Probing the debris region, our maximum red-minus-green stayed at 47 across
        /// the whole sequence where the reference reaches 127 at 2.10s and 255 at 2.60s and 3.00s. The
        /// apparent mustard was the handful of degenerate-normal faces plus blocks brightening as they
        /// tumbled, not the material changing hue.
        /// </summary>
        void ApplyColor(Color baseColor, Color emission)
        {
            if (_runtimeMat == null) return;
            _runtimeMat.SetColor(BaseColorId, baseColor);
            _runtimeMat.SetColor(EmissionId, emission);

            if (_runtimeMat.HasProperty(TopColorId)) _runtimeMat.SetColor(TopColorId, baseColor);
            if (_runtimeMat.HasProperty(FrontColorId)) _runtimeMat.SetColor(FrontColorId, Scale(baseColor, FrontFaceScale));
            if (_runtimeMat.HasProperty(SideColorId)) _runtimeMat.SetColor(SideColorId, Scale(baseColor, SideFaceScale));
        }

        static Color Scale(Color c, float k)
        {
            return new Color(c.r * k, c.g * k, c.b * k, c.a);
        }

        /// <summary>Emission kick on the blocks right as the puck lands.</summary>
        public void EmissionPulse(float duration)
        {
            if (_runtimeMat == null) return;
            Color hot = new Color(0.40f, 1.70f, 0.70f, 1f);
            Color rest = _restEmission;
            Tweener.Color(hot, rest, duration, c =>
            {
                if (_runtimeMat != null) _runtimeMat.SetColor(EmissionId, c);
            }).SetEase(EaseType.OutQuad);
        }

        void OnDestroy()
        {
            ReleasePuckCollisionFilter();
            if (_cascade != null) StopCoroutine(_cascade);
            if (_colorEvolution != null) StopCoroutine(_colorEvolution);
            if (_runtimeMat != null) Destroy(_runtimeMat);
            if (_blockPhysicsMaterial != null) Destroy(_blockPhysicsMaterial);
        }

    }
}
