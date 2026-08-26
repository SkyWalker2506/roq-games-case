using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Shared.Juice;

namespace Case2
{
    /// <summary>
    /// Turns the landed block into readable colour-matched chunks and drops them down the hole. The chunks are the
    /// project's pre-fractured mesh pieces; they are driven by hand rather than by rigidbodies so the
    /// fall is repeatable frame for frame and always ends inside the hole instead of scattering on
    /// the floor. Colour comes from the block and is pushed per renderer with a MaterialPropertyBlock.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class BlockShatterSink : MonoBehaviour
    {
        [Header("Assets (filled in by Case2SceneSetup)")]
        [Tooltip("Pre-fractured version of the block; every child piece becomes one shard.")]
        public GameObject fracturedPrefab;
        [Tooltip("Unit cell fracture prefab used to composite multi-cell shapes like Cross and L.")]
        public GameObject unitFracturePrefab;

        [Tooltip("Opaque chunk material shared by every broken piece.")]
        public Material shardMaterial;

        public GameObject debrisBurstPrefab;
        public GameObject impactRingPrefab;
        public GameObject dustPuffPrefab;

        /// <summary>
        /// Whether the break has an impact layer that can actually render. Every call site in
        /// <see cref="PlayVfx"/> is behind a null check, so with all three unassigned the break is
        /// shards and nothing else - and the run report used to certify a chip burst anyway.
        /// Anything that claims the effect fired has to ask this first.
        /// </summary>
        public bool HasImpactVfx
        {
            get { return debrisBurstPrefab != null || impactRingPrefab != null || dustPuffPrefab != null; }
        }

        [Header("Feel")]
        [Tooltip("Upward pop given to a shard the instant the block breaks.")]
        public Vector2 riseSpeed = new Vector2(0.18f, 0.62f);

        [Tooltip("Sideways spread of a shard, scaled by how far it sits from the block centre.")]
        public float outwardSpeed = 1.45f;

        [Tooltip("Gravity applied to the shards while they fall.")]
        public float gravity = 6.4f;

        [Tooltip("How hard the hole pulls the shards back over its mouth as they drop.")]
        public float funnelRate = 4.2f;

        /// <summary>
        /// Widest XZ distance any shard reached from the hole centre during the last burst, world
        /// units. Read by the gate: an outward impulse does not imply a wide burst if the funnel
        /// pulls the cloud back before it travels, which is exactly what the authored scene did.
        /// </summary>
        float _peakSpread;
        public float PeakShardSpread { get { return _peakSpread; } }
        public void ResetPeakShardSpread() { _peakSpread = 0f; }

        [Tooltip("Seconds the shards are allowed to spray outward before the hole starts gathering "
            + "them back. The reference's burst is still wide and clear of the board at 1.60s and "
            + "only settles over the opening by 1.95s, so the cloud needs a real spray window.")]
        public float sprayWindow = 0.14f;

        [Tooltip("Alpha of a broken chunk; keep at 1 for readable colour identity.")]
        public float shardAlpha = 1.0f;

        [Tooltip("Every shard is blown up by this much. A 1-unit block split 57 ways gives crumbs the " +
                 "size of a few screen pixels; the reference break throws chunks that read as glass. " +
                 "Volume conservation is not the goal, legibility is.")]
        public float shardScale = 0.88f;

        [Tooltip("How far towards white a shard is pushed off the block colour, so it reads as glass " +
                 "rather than as a chip of painted plastic.")]
        [Range(0f, 1f)] public float shardWhitening = 0.01f;

        [Tooltip("Descent speed once a shard is below the mouth of the hole. Slower than free fall so " +
                 "the sink phase keeps showing motion instead of emptying in three frames.")]
        public float sinkSpeed = 1.05f;

        [Tooltip("How deep below the mouth a shard has to get before it has shrunk away completely.")]
        public float swallowDepth = 0.92f;

        [Header("Readability")]
        [Tooltip("How many fracture pieces the SPRAY runs - the pieces outside the core, which is " +
                 "kept whole on top of this. Raised together with a drop in shardScale: coverage " +
                 "goes as roughly N*s^2, and the two settings it replaced (28 at 1.55, and the " +
                 "48-at-1.18 experiment before that) were both 67, i.e. the same material.")]
        // 61 at scale 1.05 is N*s^2 = 67 for the spray alone - the arms keep exactly the material
        // they had - and the core is added on top of it, which is where the new coverage comes
        // from. At 1.60 the reference's burst is many small fragments densely packed with visible
        // layering where they overlap; ours was about fourteen large flat leaves in a ring.
        [Range(8, 160)] public int maxReadableChunks = 61;

        [Tooltip("Fraction of the burst's horizontal half-extent that counts as its core. Every " +
                 "fracture piece starting inside this radius of the hole centre is kept whole and " +
                 "barely thrown, so the middle of the burst is a merged body rather than a ring " +
                 "around a void. 0.34 of a cross's 1.5-cell half-extent is about half a cell.")]
        [Range(0f, 0.8f)] public float coreRadiusFraction = 0.34f;

        [Tooltip("How much of its outward impulse a core piece keeps. Near zero: the core holds.")]
        [Range(0f, 1f)] public float coreSpread = 0.12f;

        [Tooltip("How much of the upward pop a core piece keeps. The core rises a little so the " +
                 "break still reads as an eruption, but not far enough to clear its own cell.")]
        [Range(0f, 1f)] public float coreRise = 0.35f;

        [Header("Survivors")]
        [Tooltip("How many shards are thrown clear of the opening and left resting on the board " +
                 "after it seals, instead of every shard being force-deactivated when the fall ends.")]
        // MEASURED on docs/verify/case2/ref/ref_2.40s.png, bright-restricted (purple and L>110)
        // inside the board interior: the reference still holds 509 px in three shard blobs of
        // 263, 88 and 24 px after the hole has closed, at screen cells (2.29, j7.19), (0.86,
        // j7.70) and (0.94, j7.86). Ours held 27 px, and all of it was board-frame edge rather
        // than shard - Fall() force-deactivated every shard the moment its duration ran out.
        [Range(0, 3)] public int survivingShards = 3;

        [Tooltip("Bounding diagonal of the largest surviving shard, in cells. An ABSOLUTE size: " +
                 "each survivor is rescaled to hit it, so how big it renders does not depend on " +
                 "which fracture piece happened to be picked.")]
        // The scene's grid is exactly one world unit per cell (holes sit at 1.5/5.5/6.5/5.0), so
        // the reference's blob positions convert straight to world offsets from the cross hole at
        // (1.5, 1.5): (+0.79, -0.69), (-0.64, -1.20), (-0.56, -1.36), remembering that screen row
        // j runs opposite to world z (rowJ = 8 - z).
        //
        // Size was first written as a MULTIPLE of a spray chunk, and that failed: the three picked
        // pieces have wildly different natural extents, so the 1.00/0.58/0.30 ladder was swamped
        // by the source piece and the survivors rendered 57/133/56 px - the ladder inverted, the
        // largest slot producing the smallest blob. Sizing them absolutely removes the source
        // piece from the answer entirely. 0.230 is solved from the reference's own 263 px blob:
        // sqrt(263)/122.6 = 0.133 cells across, and a roughly cubic chunk of side a has a
        // bounding diagonal of a*sqrt(3) = 0.230.
        [Range(0.02f, 1f)] public float survivorScale = 0.230f;

        [Tooltip("How high above the hole's own plane a resting shard sits, so it reads as lying " +
                 "on the board rather than sunk into it.")]
        public float survivorRestLift = 0.04f;

        [Tooltip("Seconds into the fall before a survivor stops tumbling and eases onto its " +
                 "resting place, and how long that easing takes.")]
        public float survivorSettleStart = 0.45f;
        public float survivorSettleTime = 0.30f;

        /// <summary>Relative linear size of each survivor: 263 px, 88 px and 24 px in the reference.</summary>
        static readonly float[] SurvivorRelativeScale = { 1.00f, 0.58f, 0.30f };

        /// <summary>Where each survivor comes to rest, in cells from the hole centre, x right and z away.</summary>
        static readonly Vector2[] SurvivorRestCells =
        {
            new Vector2(0.79f, -0.69f),
            new Vector2(-0.64f, -1.20f),
            new Vector2(-0.56f, -1.36f)
        };

        struct Shard
        {
            public Transform Tr;
            public Vector3 Velocity;
            public Vector3 Spin;
            public Vector3 BaseScale;
            public float GravityScale;
            /// <summary>-1 for a shard the hole swallows; 0..n for one that survives the seal.</summary>
            public int Survivor;
            public Vector3 RestPos;
        }

        readonly List<Shard> _shards = new List<Shard>(64);
        readonly List<int> _sprayShards = new List<int>(64);
        GameObject _root;
        Coroutine _fall;
        MaterialPropertyBlock _mpb;

        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        /// <summary>Number of shards created by the last break.</summary>
        public int ShardCount { get { return _shards.Count; } }

        /// <summary>
        /// Breaks <paramref name="blockTransform"/> into shards at its current pose and starts the fall
        /// into <paramref name="holeCenter"/>. The block renderer is hidden; the caller restores it.
        /// </summary>
        public int Shatter(Transform blockTransform, Renderer blockRenderer, Color color,
                           Vector3 holeCenter, float fallDuration, GameObject prefabOverride = null,
                           Bounds? blockBounds = null, BlockShapeId shapeId = BlockShapeId.Unknown)
        {
            Clear();
            GameObject source = prefabOverride != null ? prefabOverride : fracturedPrefab;
            if (blockTransform == null) return 0;

            Bounds art = blockBounds.HasValue
                ? blockBounds.Value
                : (blockRenderer != null ? blockRenderer.bounds : new Bounds(blockTransform.position, Vector3.one * 0.5f));

            _root = source != null
                ? Instantiate(source, blockTransform.position, blockTransform.rotation)
                : BuildProceduralFragments(blockTransform, art, shapeId);
            _root.name = "Case2_Shards";

            // MEASURED, in the owner's own open Editor with the game NOT running: 7 orphan
            // `Case2_Shards` roots holding 1,438 shard objects, every one of them reporting
            // `scene = <NO SCENE>`.
            //
            // `<NO SCENE>` is the signature of an object that outlived the teardown of the scene
            // it was born in: the Scene view still draws it, because rendering does not ask which
            // scene an object belongs to, while the Hierarchy cannot list it, because it
            // enumerates the roots of LOADED scenes and this object belongs to none. That is
            // exactly the pair of symptoms reported - shards on screen with the game closed, and
            // nothing in the Hierarchy to select or delete.
            //
            // WHAT IS MEASURED AND WHAT IS NOT, kept apart on purpose:
            //
            //   MEASURED - the leak itself: 7 roots / 1,438 objects / all `<NO SCENE>`.
            //   MEASURED - `HideFlags.DontSave` does NOT carry NotEditable on this Unity
            //     (6000.3.11f1); a control read the flag back and got NotEditable=False. So the
            //     leak was never un-deletable in principle, only unreachable in practice.
            //   MEASURED, and it REFUTED the first hypothesis: closing a real scene with
            //     `CloseScene(removeScene: true)` destroys a DontSave root, a
            //     DontSaveInEditor|DontSaveInBuild root and a parented DontSave child alike. An
            //     edit-mode scene unload is therefore NOT the path these shards survived.
            //   NOT MEASURED HERE - the remaining path, which is the PLAY-MODE EXIT teardown. It
            //     could not be exercised because the owner was working in the Editor window at
            //     the time. So the survival mechanism is narrowed to play-mode exit by
            //     elimination, not demonstrated.
            //
            // The fix is therefore written so that it does not DEPEND on which teardown it was:
            //
            // 1. The flags keep "never serialized" and nothing else, so a shard that does somehow
            //    outlive a run can still never be written into the owner's scene file.
            // 2. The root is parented under the sink, a real scene object, so its lifetime is a
            //    subtree relationship rather than a flag Unity is free to interpret. Control C
            //    above confirms a parented child dies with the scene. `Case2_Sequence` sits at
            //    the scene root with an identity transform, and SetParent(worldPositionStays:
            //    true) preserves the world pose regardless, so no shard moves by a float.
            // 3. OnDisable calls Clear (below), which covers the play-mode-exit teardown
            //    explicitly instead of trusting it.
            _root.hideFlags = HideFlags.DontSaveInEditor | HideFlags.DontSaveInBuild;
            _root.transform.SetParent(transform, true);
            if (source != null) _root.transform.localScale = blockTransform.lossyScale;
            _root.SetActive(true);   // the fracture asset ships disabled

            MeshRenderer[] pieces = _root.GetComponentsInChildren<MeshRenderer>(true);
            if (pieces.Length == 0)
            {
                Destroy(_root);
                _root = null;
                return 0;
            }

            // Which pieces survive is chosen further down, once the cloud has been moved onto the
            // opening, so "how far did this piece start from the hole" is a real distance.

            // The fracture asset does not have to share the block's pivot; line the two up by bounds
            // centre so the shards start exactly where the block art was.
            Bounds shardBounds = pieces[0].bounds;
            for (int i = 1; i < pieces.Length; i++) shardBounds.Encapsulate(pieces[i].bounds);

            // The block art is not always the pivot: a block whose mesh hangs off a child would spawn
            // its shards next to itself. Whoever knows the real bounds passes them in.
            _root.transform.position += art.center - shardBounds.center;

            // Centre the cloud on the opening, which is where the piece actually broke. (The note
            // that used to sit here claimed the reference's chunks "never leave the hole's own
            // footprint"; its 1.60s frame shows them well outside it. Centring the spawn is still
            // right - it is the containment that was wrong, and that lives in the funnel timing.)
            Vector3 spawnFix = new Vector3(holeCenter.x - art.center.x, 0f, holeCenter.z - art.center.z);
            _root.transform.position += spawnFix;

            // The piece now settles INTO the opening, so by the time it breaks its art centre can
            // be below the mouth - and Fall() shrinks away anything under holeCenter.y, which would
            // swallow the whole burst on the frame it spawned. Lift the cloud to the mouth so the
            // shards still erupt out of the hole rather than starting inside it.
            float mouthY = holeCenter.y + 0.06f;
            float spawnLift = Mathf.Max(0f, mouthY - art.center.y);
            if (spawnLift > 0f) _root.transform.position += Vector3.up * spawnLift;

            Vector3 center = new Vector3(holeCenter.x, Mathf.Max(art.center.y, mouthY), holeCenter.z);

            // ---------------------------------------------------------------- which pieces survive
            //
            // MEASURED, .plan-build/verify/BlockHole/frame_151.png -> frame_152.png: the centre
            // cell of the cross hole goes 100.0% -> 55.7% purple across the break. Those two
            // capture samples are 9.5 ms apart, so 44.3 of the centre's 50.7 missing points are
            // cut by the fracture itself; the whole flight from there to frame_168.png (t=1.60)
            // costs only the remaining 14.6. That is why three rounds of impulse work - a
            // distance-scaled radial push, a distance-scaled lateral jitter, and a translucency
            // pass - all measured FLAT rather than wrong: none of them can touch a hole that
            // already exists on the spawn frame.
            //
            // The arm cells hide the same porosity instead of showing it. With the hole EMPTY and
            // no block anywhere near it (frame_20.png, t=0.19) they already read 85-87% purple,
            // because that is lit pit wall behind the gaps. Only the centre cell of a cross has
            // hole on all four sides and no wall behind it, so it is the one cell where the gaps
            // between chunks are visible at all. Baseline-subtracted, our centre held 48% shard
            // coverage at the spawn frame against the reference's 82%.
            //
            // Two things follow, and they are the two halves of the fix.
            //
            // THE CORE. The reference's burst is one merged body over the mouth with fliers around
            // it; ours was a ring of leaves around a void. Every piece that starts over the mouth
            // is now kept WHOLE - no decimation inside coreRadius - so the middle of the burst is
            // solid material rather than a sampled scatter of it.
            //
            // THE BUDGET. 28 pieces at scale 1.55 and the earlier experiment's 48 at 1.18 are both
            // N*s^2 = 67: the same quantity of material. Swapping between them moved the rendered
            // union by nothing because it never left that iso-line. Coverage only rises if the
            // product rises, so the spray runs more pieces at a smaller scale - which also brings
            // the grain closer to the reference's packing instead of our half-cell leaves.
            Bounds cloud = pieces[0].bounds;
            for (int i = 1; i < pieces.Length; i++) cloud.Encapsulate(pieces[i].bounds);
            float halfExtent = Mathf.Max(cloud.extents.x, cloud.extents.z);
            float coreRadius = Mathf.Max(0.01f, coreRadiusFraction * halfExtent);

            bool[] core = new bool[pieces.Length];
            bool[] keep = new bool[pieces.Length];
            List<int> outer = new List<int>(pieces.Length);
            for (int i = 0; i < pieces.Length; i++)
            {
                Vector3 d = pieces[i].bounds.center - center;
                d.y = 0f;
                if (d.sqrMagnitude <= coreRadius * coreRadius)
                {
                    core[i] = true;
                    keep[i] = true;
                }
                else
                {
                    outer.Add(i);
                }
            }

            // The spray is spread evenly across the pieces OUTSIDE the core. Keeping the first N
            // renderers in hierarchy order is what made the burst a tight clump once before: the
            // procedural fracture lays one unit prefab over each footprint cell, so the first N of
            // ~285 pieces all come from the first cell or two.
            int outerBudget = Mathf.Min(Mathf.Max(8, maxReadableChunks), outer.Count);
            for (int k = 0; k < outerBudget; k++)
            {
                keep[outer[(int)((long)k * outer.Count / outerBudget)]] = true;
            }
            // Glass, not painted chips: pushed hard towards a cold white and lifted past 1 so the
            // shards catch the light instead of sitting flat at the block's own paint colour.
            // Cold pale blue rather than white: fifty overlapping translucent shards add up, and a
            // white base clips to a solid cotton puff the moment two of them cross.
            Color icy = Color.Lerp(color, Color.white, shardWhitening);
            Color shardColor = new Color(icy.r, icy.g, icy.b, shardAlpha);
            if (_mpb == null) _mpb = new MaterialPropertyBlock();

            Random.State randomState = Random.state;
            Random.InitState(220819);
            for (int i = 0; i < pieces.Length; i++)
            {
                MeshRenderer mr = pieces[i];
                GameObject go = mr.gameObject;
                if (!keep[i])
                {
                    go.SetActive(false);
                    continue;
                }
                go.SetActive(true);

                Rigidbody rb = go.GetComponent<Rigidbody>();
                if (rb != null) Destroy(rb);
                Collider col = go.GetComponent<Collider>();
                if (col != null) Destroy(col);

                if (shardMaterial != null) mr.sharedMaterial = shardMaterial;
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                mr.receiveShadows = false;
                mr.GetPropertyBlock(_mpb);
                _mpb.SetColor(BaseColorId, shardColor);
                mr.SetPropertyBlock(_mpb);

                Vector3 out3 = mr.bounds.center - center;
                out3.y = 0f;
                if (out3.sqrMagnitude < 0.0001f) out3 = new Vector3(Random.value - 0.5f, 0f, Random.value - 0.5f);
                // Radial speed scaled by how far the chunk started from the centre, instead of
                // every chunk leaving at the SAME speed regardless of where it began. A uniform
                // outward impulse evacuates the middle: chunks that spawned at the centre fly out
                // just as fast as the ones on the rim, which is exactly why our burst read as a
                // ring around an empty hole while the reference's is dense all the way through.
                // Clamped at 1.2 cells so the outermost chunks keep the same speed they have now
                // and the containment settled at 1.95 is not disturbed.
                // ...and the lateral jitter is scaled the same way. Scaling only the radial term
                // left the jitter at full strength, and for a chunk spawning at the centre the
                // jitter is the LARGER of the two - so the middle still emptied itself.
                //
                // Measured shard-only coverage of the centre cell (bright, minus the hole-alone
                // baseline): 47.6% at the spawn frame, 28.6% by 1.60, while every arm GAINS
                // between +8 and +14 points over the same window. The chunks are not missing at
                // spawn, they migrate outward. The reference holds 64.6% there.
                float spawnRadius = out3.magnitude;
                float distFactor = Mathf.Clamp01(spawnRadius / 1.2f);
                float radial = outwardSpeed * distFactor * Random.Range(0.75f, 1.35f);
                float jitterScale = 0.3f + 0.7f * distFactor;
                out3 = out3.normalized * radial
                     + new Vector3(Random.Range(-0.6f, 0.6f), 0f, Random.Range(-0.6f, 0.6f)) * jitterScale;

                // A core piece barely moves sideways at all. Scaling by distance was not enough on
                // its own - a piece that spawns a tenth of a cell off centre still keeps a tenth of
                // the full impulse, and a dozen of those drifting apart is what opened the void.
                // The core holds together and sinks as one body, which is the structure the
                // reference's burst has.
                if (core[i]) out3 *= coreSpread;

                // Chunky, and every shard on its own clock: the spread of gravity scales is what turns a
                // single puff into a stream that keeps arriving through the whole sink phase.
                go.transform.localScale *= shardScale;

                // The chunks are THROWN, not dropped. At 1.60s the reference's burst stands clear
                // of the board and spans about three cells; only by 1.95s is it falling back into
                // the opening. The previous line hardcoded a downward launch of -1.6..-0.6 on the
                // stated grounds that "in the reference the chunks go DOWN from frame one", which
                // the frames contradict - and it meant riseSpeed was serialized, written by
                // Case2SceneSetup and logged, but never read by anything, so tuning it did nothing.
                if (!core[i]) _sprayShards.Add(_shards.Count);
                _shards.Add(new Shard
                {
                    Tr = go.transform,
                    // The core is thrown gently and given a narrow spread of gravity scales, so it
                    // stays over the mouth through the peak instead of being lofted off its own
                    // cell - the camera is tilted, so height reads as travel up the screen.
                    Velocity = out3 + Vector3.up * (Random.Range(riseSpeed.x, riseSpeed.y) * (core[i] ? coreRise : 1f)),
                    Spin = new Vector3(Random.Range(-550f, 550f), Random.Range(-550f, 550f), Random.Range(-550f, 550f)),
                    BaseScale = go.transform.localScale,
                    GravityScale = core[i] ? Random.Range(0.9f, 1.4f) : Random.Range(0.8f, 2.4f),
                    Survivor = -1
                });
            }

            Random.state = randomState;

            // ------------------------------------------------------- shards that survive the seal
            //
            // The reference's board is not empty when the hole closes: at 2.40 it still carries
            // three shard blobs of 263, 88 and 24 px resting on the tiles below the opening. Ours
            // carried none, because Fall() force-deactivated every shard the instant its duration
            // ran out - the shards did not fail to land, they were switched off.
            BlockShapeId footprintId = shapeId != BlockShapeId.Unknown
                ? shapeId
                : BlockShapeIds.Parse(blockTransform.name);
            float cellWorld = Mathf.Max(0.08f,
                Mathf.Min(art.size.x, art.size.z) / FootprintDivisor(footprintId));
            int survivorCount = Mathf.Clamp(survivingShards, 0,
                Mathf.Min(SurvivorRestCells.Length, _sprayShards.Count));
            for (int k = 0; k < survivorCount; k++)
            {
                // Picked from the SPRAY only, and spread across it. Picking from _shards directly
                // would have taken its early entries, and those are the core: the pieces are added
                // in hierarchy order, the centre footprint cell is laid down first, so an evenly
                // spread pick lands its first survivor inside the core and shrinks it to 0.61.
                // That would quietly spend part of the 1.60 centre coverage this round is meant
                // to leave untouched.
                int slot = (int)((long)(k + 1) * _sprayShards.Count / (survivorCount + 1));
                int idx = _sprayShards[Mathf.Clamp(slot, 0, _sprayShards.Count - 1)];
                Shard s = _shards[idx];
                if (s.Tr == null) continue;

                // Rescale to an absolute world size rather than a multiple of the source piece.
                // bounds is world space and already carries the current localScale, so the ratio
                // below lands every survivor on the same target regardless of which piece it was.
                Renderer sr = s.Tr.GetComponent<Renderer>();
                float diagonal = sr != null ? sr.bounds.size.magnitude : 0f;
                if (diagonal > 0.0001f)
                {
                    float targetDiagonal = survivorScale * SurvivorRelativeScale[k] * cellWorld;
                    s.Tr.localScale = s.Tr.localScale * (targetDiagonal / diagonal);
                }
                s.BaseScale = s.Tr.localScale;
                s.Survivor = k;
                s.RestPos = new Vector3(
                    holeCenter.x + SurvivorRestCells[k].x * cellWorld,
                    holeCenter.y + survivorRestLift,
                    holeCenter.z + SurvivorRestCells[k].y * cellWorld);
                _shards[idx] = s;
            }

            if (blockRenderer != null) blockRenderer.enabled = false;

            PlayVfx(center, holeCenter);

            _fall = StartCoroutine(Fall(holeCenter, fallDuration));
            return _shards.Count;
        }

        /// <summary>
        /// Cells across the shape's own footprint. Shared by the fracture layout and by the
        /// survivor placement, which needs a world cell size to convert the reference's measured
        /// screen positions into offsets from the hole.
        /// </summary>
        static float FootprintDivisor(BlockShapeId id)
        {
            string[] mask = BlockShapeIds.Mask(id);
            if (mask == null || mask.Length == 0) return 1f;
            // The SHORT side of the mask, because the caller divides the SHORT side of the art
            // bounds by it. Hand-written, this returned 2 for an L - whose mask is 3 wide by 2
            // tall, so 2 happened to be right - and 1 for a Two, whose mask is 1 by 3, where 1 is
            // also right. It was wrong for neither, but it was a third copy of the same facts.
            return Mathf.Max(1, Mathf.Min(mask[0].Length, mask.Length));
        }

        GameObject _spanPrefab;
        float _spanCached = -1f;

        /// <summary>
        /// Widest XZ extent of one unit fracture at its authored scale, in world units. Measured
        /// once and cached: it is a property of the asset, and a 90 or 180 degree yaw cannot
        /// change max(x, z). The gauge instance is deactivated before it is handed to Destroy, so
        /// it can never draw a frame at the origin while Destroy waits for the end of the frame.
        /// </summary>
        float MeasureUnitSpan(GameObject unitPrefab)
        {
            if (_spanCached > 0f && _spanPrefab == unitPrefab) return _spanCached;
            float span = 1f;
            GameObject gauge = Instantiate(unitPrefab, Vector3.zero, Quaternion.identity);
            MeshRenderer[] rs = gauge.GetComponentsInChildren<MeshRenderer>(true);
            if (rs.Length > 0)
            {
                Bounds gb = rs[0].bounds;
                for (int i = 1; i < rs.Length; i++) gb.Encapsulate(rs[i].bounds);
                span = Mathf.Max(0.0001f, Mathf.Max(gb.size.x, gb.size.z));
            }
            gauge.SetActive(false);
            if (Application.isPlaying) Destroy(gauge); else DestroyImmediate(gauge);
            _spanPrefab = unitPrefab;
            _spanCached = span;
            return span;
        }

        GameObject BuildProceduralFragments(Transform source, Bounds art, BlockShapeId shapeId)
        {
            GameObject root = new GameObject("Case2_ProceduralFracture");
            root.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);

            BlockShapeId id = shapeId != BlockShapeId.Unknown ? shapeId : BlockShapeIds.Parse(source.name);

            // The footprint comes from the game's own shape mask, in WORLD axes, and nowhere else.
            //
            // It used to come from a hand-written table sitting right here, and that table
            // DISAGREED with the mask. Measured against each block's own drawn footprint, as the
            // fraction of it that received any fracture material at all:
            //
            //     Cross   100.0%   5 cells, agreed
            //     Square  100.0%   4 cells, agreed
            //     L        37.5%   the table laid 3 cells in a 2x2 box; an L is 4 cells in a 3x2
            //     Two      33.3%   the table laid 2 cells; a Two is 3
            //
            // and for the L a further 37.5% of a shape-area's worth of material landed OUTSIDE the
            // shape, because three cells of the wrong size were centred on the bounds centre of a
            // 3x2 box and straddled its cell boundaries. That is the reported symptom exactly: the
            // hole reads flat black while the shards sit scattered on the board around it. The
            // effect was never per-shape by intent; it was per-shape by a stale duplicate table.
            //
            // Placement is in world axes because that is the frame the mask is written in. The old
            // code offset by source.right / source.forward, the block's LOCAL axes, which is a
            // second shape-dependence hiding inside the first: Block-L carries a 180 degree yaw
            // and Block-2 a 90 degree one, so their local frames are not the frame the mask means.
            // Each fracture instance still inherits source.rotation, so the chunks are oriented
            // with the block as before - only where they are PLACED changes.
            string[] mask = BlockShapeIds.Mask(id);
            if (mask == null || mask.Length == 0) mask = new[] { "#" };
            int maskRows = mask.Length, maskCols = mask[0].Length;
            float cellW = art.size.x / maskCols;      // world +x per mask column
            float cellH = art.size.z / maskRows;      // world +z per mask row
            float cell = Mathf.Max(0.08f, Mathf.Min(cellW, cellH));

            GameObject unitPrefab = unitFracturePrefab != null ? unitFracturePrefab : fracturedPrefab;
#if UNITY_EDITOR
            if (unitPrefab == null)
                unitPrefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Case2_BlockHole/Prefabs/Fractured/FractureMeshes-Game/Block-Single.prefab");
#endif
            if (unitPrefab != null)
            {
                // How big is one unit fracture, as instantiated and turned to face the way this
                // block faces? Measured rather than assumed, so the piece lands on exactly one
                // cell whatever the block's own scale or yaw is. The old expression divided by
                // `art.size.x / divisor`, which is the cell size along ONE world axis - correct
                // only for a shape whose bounding box is square, and Block-L's is 3x2 and
                // Block-2's is 1x3. It also multiplied by source.lossyScale, and Block-2 carries a
                // 1.5 on one axis, so its pieces would have come out half again too big.
                float pieceScale = cell / MeasureUnitSpan(unitPrefab);

                for (int r0 = 0; r0 < maskRows; r0++)
                for (int c0 = 0; c0 < maskCols && c0 < mask[r0].Length; c0++)
                {
                    if (mask[r0][c0] != '#') continue;
                    // Row 0 is the +z row, matching BuildArtPickRegion's own convention.
                    Vector3 cellPos = new Vector3(
                        art.min.x + (c0 + 0.5f) * cellW,
                        art.center.y,
                        art.max.z - (r0 + 0.5f) * cellH);

                    GameObject cellInstance = Instantiate(unitPrefab, cellPos, source.rotation);
                    cellInstance.transform.localScale *= pieceScale;

                    MeshRenderer[] renderers = cellInstance.GetComponentsInChildren<MeshRenderer>(true);
                    for (int r = 0; r < renderers.Length; r++)
                    {
                        renderers[r].transform.SetParent(root.transform, true);
                    }
                    if (Application.isPlaying) Destroy(cellInstance); else DestroyImmediate(cellInstance);
                }
                return root;
            }

            // Same mask, same world axes - the crude cube fallback must not disagree with the real
            // one about which cells the shape covers.
            int index = 0;
            for (int r0 = 0; r0 < maskRows; r0++)
            for (int c0 = 0; c0 < maskCols && c0 < mask[r0].Length; c0++)
            {
                if (mask[r0][c0] != '#') continue;
                float cx = art.min.x + (c0 + 0.5f) * cellW;
                float cz = art.max.z - (r0 + 0.5f) * cellH;
                for (int sx = 0; sx < 2; sx++)
                for (int sz = 0; sz < 2; sz++)
                {
                    GameObject chunk = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    chunk.name = "Chunk_" + index++;
                    chunk.transform.SetParent(root.transform, true);
                    chunk.transform.position = new Vector3(
                        cx + (sx == 0 ? -0.26f : 0.26f) * cell,
                        art.center.y,
                        cz + (sz == 0 ? -0.26f : 0.26f) * cell);
                    chunk.transform.rotation = source.rotation;
                    chunk.transform.localScale = new Vector3(cell * 0.32f, Mathf.Max(0.06f, art.size.y * 0.55f), cell * 0.32f);
                    Collider collider = chunk.GetComponent<Collider>();
                    if (collider != null) Destroy(collider);
                }
            }
            return root;
        }

        // MEASURED, and the reason all three prefabs are deliberately left unassigned in the scene.
        //
        // Wiring DebrisBurst / ImpactRing / DustPuff up and capturing proved the pool and the
        // systems work: raised to holeCenter.y + 0.90 at 3x scale they changed 195,590 px at the
        // break, so nothing here is broken. The problem is what they ARE. At a size you can see,
        // this layer is a large opaque white smoke cloud plus tumbling BROWN rock debris - over a
        // purple break. The reference at 1.60s is purple fragments and a soft magenta haze: no
        // smoke, no grey, no brown, which is exactly what the director's own event text already
        // demanded ("no opaque smoke cloud"). At their authored position (+0.05) and 1x they are
        // instead invisible - buried under the opaque shard cloud - which is why they read as
        // harmless: 12,734 changed px at the break, all of it shard timing, none of it effect.
        //
        // So there is no scale at which this particular art helps: too small to see, or wrong when
        // seen. Assigning them is a fidelity regression, not a fix. The gap the reference actually
        // has - a colour-matched magenta haze at the lip - needs new art, not this art.
        // Case2Director gates its ImpactVFX report line on HasImpactVfx, so with these null the run
        // report no longer claims a burst that never rendered.
        void PlayVfx(Vector3 center, Vector3 holeCenter)
        {
            Vector3 fxPos = new Vector3(holeCenter.x, holeCenter.y + 0.05f, holeCenter.z);
            // The fourth argument of VFXPool.Play is a uniform SCALE, not a duration - PlayInternal
            // does `t.localScale = prefab.transform.localScale * scale` and works the lifetime out
            // by itself from each particle system's duration + startLifetime. These three were
            // passing 0.45, 0.45 and 0.35, which read as the seconds each effect should last, so
            // the burst has been rendering at 45%, 45% and 35% of its authored size - roughly 20%,
            // 20% and 12% of its authored AREA. Case 1 and Case 3 pass variables actually named
            // `scale` and `sparkleScale`; Case 2 was the only site reading the parameter as time.
            //
            // Measured, this is exactly the shortfall at the 1.60 peak: against the reference we
            // are short 9,688 px in the 140-200 brightness band and 5,364 in 200-240 - the mid-tone
            // magenta haze between the chunks - while already EXCEEDING it at peak brightness
            // (45,672 vs 43,117) and on bright chunk area (43,479 vs 40,654).
            if (impactRingPrefab != null)
                VFXPool.Play(impactRingPrefab, fxPos, Quaternion.identity, 1f);
            if (debrisBurstPrefab != null)
                VFXPool.Play(debrisBurstPrefab, fxPos + Vector3.up * 0.03f, Quaternion.identity, 1f);
            if (dustPuffPrefab != null)
                VFXPool.Play(dustPuffPrefab, fxPos, Quaternion.identity, 1f);
        }

        IEnumerator Fall(Vector3 holeCenter, float duration)
        {
            float t = 0f;
            while (t < duration)
            {
                float dt = Time.deltaTime;      // scaled, so hitstop freezes the shards mid-air
                t += dt;

                // First they spray, then the hole gathers them. The reference's burst at 1.60s
                // covers roughly three cells by three and stands clear of the board, and only by
                // 1.95s are the shards back over the opening - so there IS a spray window, and a
                // wide one. The previous code ramped the funnel in over 0.08s on the stated
                // grounds that "the reference never shows a chunk travelling back towards the
                // hole from outside it", which is the opposite of what the frames show; and it
                // then never applied `pull` to anything, so the value was computed and thrown
                // away every frame and the funnel did not exist at all.
                float funnel = Mathf.Clamp01((t - sprayWindow) / 0.45f);
                float pull = 1f - Mathf.Exp(-funnelRate * funnel * dt);

                for (int i = 0; i < _shards.Count; i++)
                {
                    Shard s = _shards[i];
                    if (s.Tr == null) continue;

                    s.Velocity += Vector3.down * (gravity * s.GravityScale * dt);

                    // A survivor is never funnelled and never swallowed: it tumbles clear of the
                    // opening, then eases onto the tile it comes to rest on and holds that pose
                    // through the seal and past the end of the fall.
                    if (s.Survivor >= 0)
                    {
                        float u = Mathf.Clamp01((t - survivorSettleStart)
                                                / Mathf.Max(0.01f, survivorSettleTime));
                        Vector3 flown = s.Tr.position + s.Velocity * dt;
                        // It lands ON the board. Without this it free-falls past the tile for the
                        // whole settle window and then rises back out of the floor.
                        if (flown.y < s.RestPos.y)
                        {
                            flown.y = s.RestPos.y;
                            if (s.Velocity.y < 0f) s.Velocity.y = 0f;
                        }
                        s.Tr.position = Vector3.Lerp(flown, s.RestPos, u);
                        s.Tr.Rotate(s.Spin * ((1f - u) * dt), Space.World);
                        s.Tr.localScale = s.BaseScale;
                        _shards[i] = s;
                        continue;
                    }

                    // Below the mouth the shard stops free-falling and sinks at a fixed, readable rate.
                    if (s.Tr.position.y < holeCenter.y && s.Velocity.y < -sinkSpeed)
                    {
                        s.Velocity.y = -sinkSpeed;
                    }

                    Vector3 p = s.Tr.position + s.Velocity * dt;

                    // Horizontal funnel: once the spray window has passed, draw the cloud back
                    // over the mouth so the shards are swallowed rather than stranded on tiles.
                    p.x = Mathf.Lerp(p.x, holeCenter.x, pull);
                    p.z = Mathf.Lerp(p.z, holeCenter.z, pull);

                    s.Tr.position = p;
                    // Measured AFTER the funnel, because the funnel is the thing that decides how
                    // wide the burst actually reads. Sampling before it made the metric blind to
                    // funnelRate entirely - the control changed the value and the number did not
                    // move, which is a test that cannot observe what it names.
                    _peakSpread = Mathf.Max(_peakSpread,
                        Mathf.Max(Mathf.Abs(p.x - holeCenter.x), Mathf.Abs(p.z - holeCenter.z)));
                    s.Tr.Rotate(s.Spin * dt, Space.World);

                    // Depth-driven scale, evaluated every frame and in BOTH directions.
                    //
                    // The piece now breaks while it is sitting IN the opening, so roughly half the
                    // cloud spawns below the mouth and erupts upward through it. Writing the scale
                    // only inside `if (depth > 0)` meant those chunks were shrunk on their very
                    // first frame - the lowest to about 52% - and then kept that scale forever once
                    // they rose clear, because the branch simply stopped running and nothing ever
                    // restored them. That is where the peak coverage went when the piece started
                    // landing flush: the burst was erupting at half size.
                    //
                    // A shard descending into the hole still shrinks exactly as before, so the
                    // swallow and the 1.95/2.40 decay are untouched.
                    float depth = holeCenter.y - p.y;
                    float k = depth > 0f
                        ? Mathf.Clamp01(1f - depth / Mathf.Max(0.05f, swallowDepth))
                        : 1f;
                    s.Tr.localScale = s.BaseScale * Mathf.Max(0.05f, k);
                    if (k <= 0.06f && s.Tr.gameObject.activeSelf) s.Tr.gameObject.SetActive(false);

                    _shards[i] = s;
                }
                yield return null;
            }

            for (int i = 0; i < _shards.Count; i++)
            {
                // Survivors are deliberately left on the board. This blanket deactivation is why
                // our 2.40 frame held 27 px of bright purple against the reference's 509.
                if (_shards[i].Survivor >= 0) continue;
                // Destroyed, not merely switched off. A swallowed shard has finished its job and
                // has nothing left to show, so the effect gives its objects back instead of
                // stockpiling several hundred deactivated transforms per drop for the rest of the
                // run. Visually identical - a deactivated shard rendered nothing either.
                if (_shards[i].Tr != null) Destroy(_shards[i].Tr.gameObject);
            }
            _fall = null;
        }

        /// <summary>
        /// The effect tears itself down when it stops running, which is what "calistiktan sonra
        /// zaten silinmesi lazim" asks for. This fires on the way out of Play mode and on a scene
        /// unload, so the shards are released by the component that created them rather than swept
        /// up afterwards by an editor-side tidy that would only hide the leak.
        /// </summary>
        void OnDisable()
        {
            // DestroyImmediate is illegal from OnDisable, so the teardown path never reaches for
            // it. In Play mode Destroy does the work; on an edit-mode unload the root is a child
            // of this transform and Unity destroys it with the scene, which is the whole point of
            // parenting it there.
            _tearingDown = true;
            Clear();
            _tearingDown = false;
        }

        bool _tearingDown;

        /// <summary>Removes every shard and stops the fall; a replay starts from a clean board.</summary>
        public void Clear()
        {
            if (_fall != null)
            {
                StopCoroutine(_fall);
                _fall = null;
            }
            _shards.Clear();
            _sprayShards.Clear();
            if (_root != null)
            {
                if (Application.isPlaying) Destroy(_root);
                else if (!_tearingDown) DestroyImmediate(_root);
                _root = null;
            }
        }
    }
}
