using System;
using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;
using Shared.Tweening;

namespace Case2
{
    /// <summary>
    /// Pick up / drag / drop for one block. Two drivers share the same code path: a real pointer
    /// (mouse or touch, new Input System) and the director, which drags programmatically so the
    /// sequence can run head-less in batchmode. While a block is held it gains the three layers the
    /// reference shows: a white silhouette outline, a white grab dot, and an offset ground shadow.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class BlockDragController : MonoBehaviour
    {
        [Header("Scene wiring (filled in by Case2SceneSetup)")]
        public Transform block;
        public Camera targetCamera;
        public HoleGlowHighlight[] holes = new HoleGlowHighlight[0];
        public BlockShapeId shapeId = BlockShapeId.Unknown;

        [Header("Materials (filled in by Case2SceneSetup)")]
        public Material outlineMaterial;
        public Material shadowMaterial;
        public Material grabDotMaterial;

        [Header("Feel")]
        [Tooltip("How far the block rises off the board while held.")]
        public float liftHeight = 0.42f;

        [Tooltip("How far below its resting height the piece settles when it lands in a hole. The "
            + "reference's piece drops INTO the opening and reads as its top face alone, flush with "
            + "the board: at 1.30s its purple spans exactly its footprint, j5.10-j8.00, with plain "
            + "tile still visible at j5.05. Ours stood on the board instead - measured at t=0 the "
            + "silhouette is the footprint translated 0.19 cells up-screen, i.e. a top face about "
            + "1.08 world units high, which is 21% more purple area than the reference. Sinking by "
            + "this much drops the top face to roughly y=0.18, still above the pit plate at 0.034 so "
            + "it draws over the cavity, while the side walls fall below the plate and are hidden.")]
        public float dropDepth = 0f;

        [Tooltip("Uniform scale the piece settles to once it is in the hole, so the opening's own rim "
            + "stays visible around it. Measured at 1.30s the reference's piece is inset about 0.10 "
            + "cells inside the opening - its purple starts at j5.10 against a footprint edge of j5.00, "
            + "and its horizontal arm runs x0.13..3.05 - while ours filled the opening edge to edge and "
            + "covered the rim completely. The rim cannot be recovered by sinking further, because the "
            + "pit plate is a single flat quad: anything below it is hidden outright. Only a smaller "
            + "piece leaves the rim showing.")]
        public float landScale = 1f;

        [Tooltip("White outline thickness in world units.")]
        public float outlineWidth = 0.022f;

        [Tooltip("Ground shadow offset while the block is held; this is what sells the lift.")]
        public Vector3 shadowOffset = new Vector3(0.16f, 0f, -0.16f);

        [Tooltip("Height the ground shadow is drawn at, just above the floor tiles.")]
        public float shadowGroundY = 0.014f;

        [Tooltip("How close (XZ) the block centre must be to a hole to count as hovering it.")]
        public float hoverRadius = 0.70f;

        [Tooltip("Let a real pointer drive the block when the director is idle.")]
        public bool allowUserInput = true;

        [Tooltip("Pre-fractured version of THIS block. Falls back to the sink's default when empty.")]
        public GameObject fracturedPrefab;

        [Header("Drag tilt (measured off the reference, not guessed)")]
        // All three numbers come from tracking the reference's red L drag, frames 340-413 of
        // Block Hole.mp4 at 30 fps, by segmenting the white held-block outline and regressing its
        // screen-space bounding box against drag velocity:
        //
        //   silhouette HEIGHT  H = -0.352*vy + 263.9 px   r = -0.82 at a 2-frame lag
        //   silhouette WIDTH   W =  0.028*vy + 374.6 px   r =  0.23   (and 0.29 against vx)
        //
        // Height responds to vertical drag velocity and width responds to nothing, so the block
        // pitches about WORLD X alone - no yaw, no roll. The 263.9 px zero-velocity intercept
        // matches the 262.7 px a geometric projection predicts for an untilted 2-cell-deep,
        // 1-unit-tall block under this camera to within 1.2 px, which is what says the
        // segmentation and the model agree.
        [Tooltip("Degrees of pitch per world unit/second of z (up-screen) drag velocity. Measured: "
            + "-0.352 px of silhouette height per px/frame, and dH/dtheta = 122.55*(cos10 - 2*sin10) "
            + "= 1.364 px/degree for a 2-cell-deep block, giving 0.258 deg per px/frame; one px/frame "
            + "at 30 fps is 30/(122.55*sin80) = 0.2486 world units/s, so 1.04 deg per unit/s.")]
        // OWNER'S CALL, recorded against the measurement it departs from. The reference's peak
        // drag speed is 43 px/frame at 30 fps = 10.7 world units/s, and at the MEASURED rate of
        // 1.04 deg per unit/s that produces 11.1 degrees - which is what the reference actually
        // shows. The owner asked for a visible lean clamped at 30. Raising the clamp alone would
        // leave the same 11 degrees on screen and read as no change, so the rate is scaled to
        // 2.80 = 30/10.7, which puts that same reference-speed gesture at the requested 30.
        // MEASURED VALUE: 1.04. SHIPPED VALUE: 2.80. The difference is deliberate, not a re-fit.
        public float dragTiltDegPerUnitPerSecond = 2.80f;

        [Tooltip("Degrees of roll per world unit/second of x (sideways) drag velocity. NOT MEASURED - "
            + "the reference shows no lateral lean at all: its silhouette width regresses on x "
            + "velocity at r = 0.29 and on y at r = 0.23, i.e. nothing, which is how the pitch axis "
            + "was identified in the first place. This axis is the owner's stylistic addition.")]
        public float dragRollDegPerUnitPerSecond = 2.80f;

        [Tooltip("Clamp. The reference's silhouette swings 249-280 px about its 263.9 px rest value, "
            + "i.e. +16.1/-14.9 px, i.e. +11.8/-10.9 degrees at 1.364 px/degree. MEASURED CLAMP "
            + "would be 12; 30 is the owner's call.")]
        public float dragTiltMaxDegrees = 30f;

        [Tooltip("Seconds of exponential smoothing. The reference's tilt trails its velocity: the "
            + "height/velocity correlation peaks at a 2-frame lag (-0.823) rather than at zero "
            + "lag (-0.732), and 2 frames at 30 fps is 67 ms.")]
        public float dragTiltSmoothing = 0.067f;

        /// <summary>Raised when a drop (real pointer or simulated) lands on a hole that matches this block.</summary>
        public event Action<BlockDragController, HoleGlowHighlight> OnUserDrop;

        /// <summary>Raised when a drop misses: empty board, or a hole this block does not fit.</summary>
        public event Action<BlockDragController, HoleGlowHighlight> OnUserMiss;

        /// <summary>Only one block can be held at a time, whichever driver is holding it.</summary>
        static BlockDragController _activeDrag;

        /// <summary>
        /// Clears the held-block latch. Without the domain reload, quitting Play mid-drag would leave this
        /// pointing at the previous session's destroyed controller, and the guard in OnMouseDrag would
        /// refuse every new drag until something else happened to clear it - input dead on the second Play.
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            _activeDrag = null;
            _live.Clear();
            LegacyBoundingBoxPickForControl = false;
        }

        Renderer _blockRenderer;
        Renderer[] _blockRenderers = new Renderer[0];
        MeshFilter _blockFilter;
        Vector3 _meshLocalOffset;
        Bounds _combinedBounds;

        /// <summary>
        /// The cells this block's art actually covers, as world-axis AABBs offset from the transform.
        /// This is the pick region. It has to be the drawn cells and not the bounding box, because
        /// two of the four blocks are concave: Block-L draws 4 cells inside a 3x2 box, so a third of
        /// its old bounding-box pick region was bare board, and the box was then inflated a further
        /// 0.18 cells on every side. Where two of those inflated boxes met, which block got the press
        /// was decided by Update order - which is what "I press green and it grabs the red one" is.
        /// </summary>
        Rect[] _artCellsLocal = new Rect[0];

        /// <summary>
        /// Where the art centre sits relative to the transform, in BLOCK-LOCAL space. Not zero on
        /// every block: Block-L carries its mesh on a child at local (0.5, 0, 0.5) under a 180-degree
        /// root, so its art centre measures at world x 1.5 against a root at x 1.0. Everything that
        /// reasons about where the block IS - hover, snap - has to use the art, or it is half a cell
        /// out on that one block only.
        /// </summary>
        Vector3 _artOffsetWorld;

        /// <summary>Bounds of the ART ONLY, with the fracture shards and VFX pieces left out.</summary>
        Bounds _artBounds;

        /// <summary>Bounds of the art alone. The pick region and the press plane are both anchored to this.</summary>
        public Bounds ArtBounds { get { return _artBounds; } }

        /// <summary>Root-minus-pointer offset captured on press, so the art does not jump under the finger.</summary>
        Vector3 _grabOffset;

        /// <summary>Current pitch about world X, degrees. Positive tips the top face toward +z.</summary>
        float _tiltDeg;

        /// <summary>Current roll about world Z, degrees. Negative tips the top face toward +x.</summary>
        float _rollDeg;
        Vector3 _homePos;
        Quaternion _homeRot;
        Vector3 _homeScale;
        Color _blockColor = Color.white;
        string _shapeKey = "";
        BlockShapeId _shapeId = BlockShapeId.Unknown;

        Transform _outline;
        Transform _grabDot;
        Transform _shadow;
        Renderer _shadowRenderer;
        Renderer _outlineRenderer;
        MaterialPropertyBlock _mpb;

        Vector3 _dragXZ;
        float _lift;
        bool _held;
        /// <summary>The hole this block belongs in, lit for as long as the block is held.</summary>
        HoleGlowHighlight _litHole;
        bool _programmatic;
        bool _userDragging;
        bool _consumed;
        HoleGlowHighlight _hover;
        HoleGlowHighlight _lastDropHole;
        bool _lastDropMatched;

        static readonly int OutlineColorId = Shader.PropertyToID("_OutlineColor");
        static readonly int OutlineWidthId = Shader.PropertyToID("_OutlineWidth");
        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        /// <summary>Shape identifier parsed from the block name, e.g. "Single" for Block_Block-Single.</summary>
        public string ShapeKey { get { return _shapeKey; } }

        /// <summary>Stable matching identity; names are used only to migrate old authored scenes.</summary>
        public BlockShapeId ShapeId { get { return _shapeId; } }

        /// <summary>Base colour of the block art; the hole rim and the shards reuse it.</summary>
        public Color BlockColor { get { return _blockColor; } }

        /// <summary>Board position the block started at.</summary>
        public Vector3 HomePosition { get { return _homePos; } }

        /// <summary>True while the block is off the board, held by either driver.</summary>
        public bool IsHeld { get { return _held; } }

        /// <summary>True once this block has been delivered into its hole; it stops responding to input.</summary>
        public bool Consumed { get { return _consumed; } }

        /// <summary>Result of the most recent release: the hole it landed in, or null for a miss.</summary>
        public HoleGlowHighlight LastDropHole { get { return _lastDropHole; } }

        /// <summary>True when the most recent release matched the hole under the block.</summary>
        public bool LastDropMatched { get { return _lastDropMatched; } }

        /// <summary>Takes this block out of play after it has been delivered.</summary>
        public void Consume()
        {
            _consumed = true;
            _held = false;
            _programmatic = false;
            _userDragging = false;
            if (_activeDrag == this) _activeDrag = null;
            ClearHover();
            HideHeldLayers();
            HideShadow();
        }

        /// <summary>Puts the block back in play (used by a replay reset).</summary>
        public void Revive()
        {
            _consumed = false;
        }

        /// <summary>Hole currently under the block, matching or not.</summary>
        public HoleGlowHighlight HoveredHole { get { return _hover; } }

        /// <summary>Renderer of the block art, so the director can hide it when it shatters.</summary>
        public Renderer BlockRenderer { get { return _blockRenderer; } }

        /// <summary>Transform of the block being dragged.</summary>
        public Transform Block { get { return block; } }

        void Awake()
        {
            CacheBlock();
        }

        void CacheBlock()
        {
            if (block == null) return;
            // Not GetComponent: Block-L keeps its mesh on a child, and looking only at the root is how
            // that whole block went missing from the board (and its hole ended up with no pit).
            _blockRenderers = block.GetComponentsInChildren<Renderer>(true);
            _blockRenderer = _blockRenderers.Length > 0 ? _blockRenderers[0] : null;
            for (int i = 0; i < _blockRenderers.Length; i++)
            {
                if (_blockRenderers[i].GetComponent<MeshFilter>() != null) { _blockRenderer = _blockRenderers[i]; break; }
            }
            _blockFilter = block.GetComponentInChildren<MeshFilter>(true);
            _meshLocalOffset = _blockFilter != null
                ? block.InverseTransformPoint(_blockFilter.transform.position)
                : Vector3.zero;
            RecomputeBounds();
            BuildArtPickRegion();
            _homePos = block.position;
            _homeRot = block.rotation;
            _homeScale = block.localScale;
            _dragXZ = new Vector3(_homePos.x, 0f, _homePos.z);
            _shapeId = shapeId != BlockShapeId.Unknown ? shapeId : BlockShapeIds.Parse(block.name);
            shapeId = _shapeId;
            _shapeKey = BlockShapeIds.Key(_shapeId);
            if (_blockRenderer != null && _blockRenderer.sharedMaterial != null &&
                _blockRenderer.sharedMaterial.HasProperty(BaseColorId))
            {
                _blockColor = _blockRenderer.sharedMaterial.GetColor(BaseColorId);
            }
        }

        void RecomputeBounds()
        {
            if (_blockRenderers.Length == 0) { _combinedBounds = new Bounds(block.position, Vector3.one * 0.5f); return; }
            _combinedBounds = _blockRenderers[0].bounds;
            for (int i = 1; i < _blockRenderers.Length; i++) _combinedBounds.Encapsulate(_blockRenderers[i].bounds);
        }

        /// <summary>World bounds of the whole block, however many renderers it is made of.</summary>
        public Bounds CombinedBounds { get { RecomputeBounds(); return _combinedBounds; } }

        // ------------------------------------------------------------------ pick region

        /// <summary>
        /// World-axis occupancy of each block shape, top row = +z (up-screen), left = -x. These are
        /// not invented: they are the grids Case2ShapeProbe rasterised out of the authored scene's own
        /// art meshes, and they agree cell for cell with the SDFs in HoleDepthGradient.shader.
        /// <para>
        /// The mask cannot be rasterised at runtime instead: every block FBX under
        /// Assets/Case2_BlockHole/Models carries <c>isReadable: 0</c>, so <c>Mesh.vertices</c> is
        /// unavailable in play mode and a mesh-based pick region would silently collapse back to the
        /// bounding box this replaces - passing, and testing nothing.
        /// </para>
        /// </summary>
        /// <remarks>
        /// Forwards to <see cref="BlockShapeIds.Mask"/>, which is now the single copy. The table
        /// used to live here, and BlockShatterSink kept a second, DIFFERENT one - see the note on
        /// BlockShapeIds.Mask for what that cost.
        /// </remarks>
        static string[] ShapeMask(BlockShapeId id)
        {
            return BlockShapeIds.Mask(id);
        }

        /// <summary>
        /// Caches the drawn cells as transform-relative world-axis rects, and measures how far the art
        /// centre sits from the transform.
        /// <para>
        /// Captured in WORLD axes at Awake, when the block carries its authored rotation. Two blocks
        /// are turned (Block-L by 180 degrees, Block-2 by 90) and nothing yaws them at runtime, so a
        /// world-axis mask stays correct. If a block is ever given a runtime yaw this has to be
        /// re-derived, and <see cref="Case2InputProbe"/> asserts the mask still matches the measured
        /// art bounds so a drift shows up rather than silently mis-picking.
        /// </para>
        /// </summary>
        void BuildArtPickRegion()
        {
            _artOffsetWorld = Vector3.zero;
            _artCellsLocal = new Rect[0];
            if (block == null || _blockRenderers.Length == 0) return;

            // NOT CombinedBounds. That is the union of every renderer under the block including the
            // inactive fracture shards and VFX pieces, which spread well past the art: anchoring the
            // cell mask to it stretched every cell and made neighbouring blocks' regions overlap.
            // This is the same filter Case2ShapeProbe measured the authored footprints with - the art
            // mesh on the block or a direct child, no ActiveFX, no Chain - so the mask is anchored to
            // the same bounds the mask was read off.
            Bounds b = new Bounds(block.position, Vector3.zero);
            bool any = false;
            MeshFilter[] filters = block.GetComponentsInChildren<MeshFilter>(true);
            for (int i = 0; i < filters.Length; i++)
            {
                MeshFilter mf = filters[i];
                if (mf.sharedMesh == null) continue;
                MeshRenderer mr = mf.GetComponent<MeshRenderer>();
                if (mr == null || !mr.enabled) continue;
                if (mf.transform != block && mf.transform.parent != block) continue;
                string n = mf.name;
                if (n.Contains("ActiveFX") || n.Contains("Chain") || n.Contains("Outline")
                    || n.Contains("Shadow") || n.Contains("GrabDot")) continue;
                if (!any) { b = mr.bounds; any = true; } else b.Encapsulate(mr.bounds);
            }
            if (!any) b = _combinedBounds;
            _artBounds = b;

            Vector3 worldOffset = b.center - block.position;
            worldOffset.y = 0f;
            // Stored in WORLD axes and never re-derived from block.rotation. The drag lean now
            // reaches 30 degrees, and rotating this offset by the live rotation would swing the
            // point hover and snap reason about by up to 0.13 u (16 px) while the block is merely
            // leaning. The lean is a transient look; it does not move the block's footprint.
            _artOffsetWorld = worldOffset;

            BlockShapeId id = shapeId != BlockShapeId.Unknown ? shapeId : BlockShapeIds.Parse(block.name);
            string[] mask = ShapeMask(id);
            if (mask == null || mask.Length == 0)
            {
                _artCellsLocal = new[] { new Rect(b.min.x - block.position.x, b.min.z - block.position.z,
                                                  b.size.x, b.size.z) };
                return;
            }

            int rows = mask.Length, cols = mask[0].Length;
            float cw = b.size.x / cols, ch = b.size.z / rows;
            var cells = new System.Collections.Generic.List<Rect>();
            for (int r = 0; r < rows; r++)
                for (int c = 0; c < cols && c < mask[r].Length; c++)
                {
                    if (mask[r][c] != '#') continue;
                    float x0 = b.min.x + c * cw;
                    float z0 = b.max.z - (r + 1) * ch;      // row 0 is the +z row
                    cells.Add(new Rect(x0 - block.position.x, z0 - block.position.z, cw, ch));
                }
            _artCellsLocal = cells.ToArray();
        }

        /// <summary>How many cells this block's pick region covers. Read by the gate.</summary>
        public int ArtCellCount { get { return _artCellsLocal.Length; } }

        /// <summary>Art centre in world space. This, not <c>block.position</c>, is where the block reads as being.</summary>
        public Vector3 ArtCentre
        {
            get
            {
                if (block == null) return Vector3.zero;
                return block.position + _artOffsetWorld;
            }
        }

        /// <summary>World XZ offset from the transform to the art centre.</summary>
        public Vector3 ArtOffset { get { return ArtCentre - block.position; } }

        /// <summary>True when the block's art is actually drawn over <paramref name="worldPoint"/> in XZ.</summary>
        public bool ArtContainsXZ(Vector3 worldPoint)
        {
            if (block == null || _artCellsLocal.Length == 0) return false;
            float x = worldPoint.x - block.position.x;
            float z = worldPoint.z - block.position.z;
            for (int i = 0; i < _artCellsLocal.Length; i++)
            {
                Rect r = _artCellsLocal[i];
                if (x >= r.xMin && x <= r.xMax && z >= r.yMin && z <= r.yMax) return true;
            }
            return false;
        }

        /// <summary>Distance in XZ from <paramref name="worldPoint"/> to the nearest drawn cell; 0 when inside.</summary>
        public float DistanceToArtXZ(Vector3 worldPoint)
        {
            if (block == null || _artCellsLocal.Length == 0) return float.MaxValue;
            float x = worldPoint.x - block.position.x;
            float z = worldPoint.z - block.position.z;
            float best = float.MaxValue;
            for (int i = 0; i < _artCellsLocal.Length; i++)
            {
                Rect r = _artCellsLocal[i];
                float dx = Mathf.Max(r.xMin - x, Mathf.Max(0f, x - r.xMax));
                float dz = Mathf.Max(r.yMin - z, Mathf.Max(0f, z - r.yMax));
                float d = Mathf.Sqrt(dx * dx + dz * dz);
                if (d < best) best = d;
            }
            return best;
        }

        /// <summary>Every controller that could answer a press, in a deterministic order.</summary>
        static readonly System.Collections.Generic.List<BlockDragController> _live =
            new System.Collections.Generic.List<BlockDragController>();

        /// <summary>
        /// Set true only by the input gate's negative control, which re-runs the pick sweep through
        /// the bounding-box predicate this fix replaced so the new assertion can be shown going RED.
        /// </summary>
        public static bool LegacyBoundingBoxPickForControl;

        /// <summary>Slop used ONLY when a press lands on no block's art at all.</summary>
        public const float PickForgiveness = 0.18f;

        void OnEnable() { if (!_live.Contains(this)) _live.Add(this); }
        void OnDisable() { _live.Remove(this); }

        /// <summary>
        /// The single arbiter for "which block did that press hit". Previously there was none: every
        /// controller tested its own inflated bounding box in its own Update and the first one to run
        /// claimed the drag, so overlapping regions resolved by script execution order.
        /// <para>
        /// A press inside a block's drawn art always wins over one that is merely near it, and ties
        /// inside overlapping art are broken by distance to the art centre - never by Update order.
        /// </para>
        /// </summary>
        public static BlockDragController ResolvePick(Vector3 worldPoint)
        {
            BlockDragController best = null;
            float bestScore = float.MaxValue;

            if (LegacyBoundingBoxPickForControl)
            {
                // The replaced predicate, kept ONLY so the gate can prove its new assertion can fail.
                for (int i = 0; i < _live.Count; i++)
                {
                    BlockDragController c = _live[i];
                    if (!c.IsPickable) continue;
                    Bounds hit = c.CombinedBounds;
                    hit.Expand(new Vector3(PickForgiveness, 2f, PickForgiveness));
                    Vector3 p = new Vector3(worldPoint.x, hit.center.y, worldPoint.z);
                    if (hit.Contains(p)) return c;      // first-come-first-served, exactly as before
                }
                return null;
            }

            for (int i = 0; i < _live.Count; i++)
            {
                BlockDragController c = _live[i];
                if (!c.IsPickable) continue;
                if (!c.ArtContainsXZ(worldPoint)) continue;
                Vector3 d = c.ArtCentre - worldPoint; d.y = 0f;
                float score = d.sqrMagnitude;
                if (score < bestScore) { bestScore = score; best = c; }
            }
            if (best != null) return best;

            // Nothing was drawn under the press. Fall back to the nearest block within the same
            // forgiveness the old box carried - but measured from the DRAWN CELLS, and arbitrated by
            // distance rather than by Update order, so a press in the gap between two blocks goes to
            // the nearer one every time instead of to whichever component happened to tick first.
            float bestDist = PickForgiveness;
            for (int i = 0; i < _live.Count; i++)
            {
                BlockDragController c = _live[i];
                if (!c.IsPickable) continue;
                float dist = c.DistanceToArtXZ(worldPoint);
                if (dist < bestDist) { bestDist = dist; best = c; }
            }
            return best;
        }

        /// <summary>Whether this block can currently answer a press.</summary>
        public bool IsPickable
        {
            get
            {
                return block != null && allowUserInput && !_consumed && !_programmatic
                       && isActiveAndEnabled && (_activeDrag == null || _activeDrag == this);
            }
        }

        /// <summary>Shows or hides every piece of the block art at once.</summary>
        public void SetVisible(bool visible)
        {
            for (int i = 0; i < _blockRenderers.Length; i++)
            {
                if (_blockRenderers[i] != null) _blockRenderers[i].enabled = visible;
            }
        }

        /// <summary>"Block_Block-Single" and "Hole_Hole-Block-Single" both resolve to "Single".</summary>
        public static string ParseShapeKey(string objectName)
        {
            return BlockShapeIds.Key(BlockShapeIds.Parse(objectName));
        }

        // ------------------------------------------------------------------ programmatic driving

        /// <summary>Lifts the block off the board and shows the held-state layers.</summary>
        public IEnumerator Pickup(float duration)
        {
            _programmatic = true;
            BeginHold();

            float start = Time.time;
            float t = 0f;
            while (t < duration)
            {
                t = Time.time - start;
                _lift = Ease.Evaluate(EaseType.OutQuad, Mathf.Clamp01(t / duration));
                ApplyTransform();
                yield return null;
            }
            _lift = 1f;
            ApplyTransform();
        }

        /// <summary>Slides the held block over the board to a world point, evaluating hole hover on the way.</summary>
        public IEnumerator MoveTo(Vector3 worldPoint, float duration, EaseType ease)
        {
            Vector3 from = _dragXZ;
            Vector3 to = new Vector3(worldPoint.x, 0f, worldPoint.z);

            float start = Time.time;
            float t = 0f;
            while (t < duration)
            {
                t = Time.time - start;
                float k = Ease.Evaluate(ease, Mathf.Clamp01(t / duration));
                _dragXZ = Vector3.Lerp(from, to, k);
                ApplyTransform();
                EvaluateHover();
                yield return null;
            }
            _dragXZ = to;
            ApplyTransform();
            EvaluateHover();
        }

        /// <summary>Holds position for a beat while still updating hover, so a hover can be observed.</summary>
        public IEnumerator Hover(float duration)
        {
            float start = Time.time;
            float t = 0f;
            while (t < duration)
            {
                t = Time.time - start;
                ApplyTransform();
                EvaluateHover();
                yield return null;
            }
        }

        /// <summary>Drops the block into a hole: it falls the lift height back down and lands with an overshoot.</summary>
        public IEnumerator SnapInto(HoleGlowHighlight hole, float duration)
        {
            Vector3 from = block.position;
            // Land the ART on the snap point, not the transform. They are the same point on three of
            // the four blocks and half a cell apart on Block-L, which is why that one piece settled
            // 0.5 cells to the right of the opening it had just been accepted into. The gate could
            // not see it: SimulateDrop moved the ROOT onto SnapPoint and then asserted the distance
            // between them was zero, which is true by construction whatever the art does.
            Vector3 artOffset = ArtOffset;
            Vector3 to = new Vector3(hole.SnapPoint.x - artOffset.x, _homePos.y - dropDepth,
                                     hole.SnapPoint.z - artOffset.z);
            Vector3 fromScale = block.localScale;
            Quaternion fromRot = block.rotation;
            // Inset HORIZONTALLY ONLY. A uniform scale is applied about the pivot, which sits at
            // the block's base, so shrinking also lowers the top face - and after dropDepth the top
            // face clears the pit plate by only 0.049 world units (0.113 against the plate at
            // 0.064). Uniform landScale 0.93 put it at 0.044, i.e. UNDER the plate, and the plate
            // is opaque with ZWrite: at 1.30 the hole rendered completely empty, no piece in it at
            // all. Anything below s=0.950 hides the block outright. Leaving Y alone keeps the top
            // face exactly where dropDepth put it while the footprint still shrinks.
            Vector3 toScale = new Vector3(_homeScale.x * landScale, _homeScale.y, _homeScale.z * landScale);

            HideHeldLayers();

            float start = Time.time;
            float t = 0f;
            while (t < duration)
            {
                t = Time.time - start;
                float k = Ease.Evaluate(EaseType.OutBack, Mathf.Clamp01(t / duration), 2.0f);
                block.position = Vector3.LerpUnclamped(from, to, k);
                // Unwind the drag lean over the same beat. A piece that lands still tilted reads as
                // wedged in the opening rather than seated in it.
                block.rotation = Quaternion.Slerp(fromRot, _homeRot, Mathf.Clamp01(k));
                block.localScale = Vector3.LerpUnclamped(fromScale, toScale, k);
                UpdateShadow();
                yield return null;
            }

            block.position = to;
            block.rotation = _homeRot;
            _tiltDeg = 0f;
            _rollDeg = 0f;
            // Settled scale is set exactly, and set BEFORE the anticipation squash starts at the end
            // of the snap: Squash captures whatever base scale it finds and restores to that, so it
            // will carry this value rather than fight it.
            block.localScale = toScale;
            _dragXZ = new Vector3(to.x, 0f, to.z);
            _lift = 0f;
            _held = false;
            _programmatic = false;
            HideShadow();
        }

        /// <summary>Returns the block to the board where it started, used when a user drop misses.</summary>
        public IEnumerator ReturnHome(float duration)
        {
            Vector3 from = block.position;
            Quaternion fromRot = block.rotation;
            _tiltDeg = 0f;
            _rollDeg = 0f;
            float start = Time.time;
            float t = 0f;
            while (t < duration)
            {
                t = Time.time - start;
                float k = Ease.Evaluate(EaseType.OutCubic, Mathf.Clamp01(t / duration));
                block.position = Vector3.Lerp(from, _homePos, k);
                block.rotation = Quaternion.Slerp(fromRot, _homeRot, k);
                UpdateShadow();
                yield return null;
            }
            ResetInstant();
        }

        /// <summary>Puts block and every held-state layer back exactly where the scene started.</summary>
        public void ResetInstant()
        {
            StopAllCoroutines();
            _programmatic = false;
            _userDragging = false;
            _held = false;
            _lift = 0f;
            if (block != null)
            {
                block.SetPositionAndRotation(_homePos, _homeRot);
                block.localScale = _homeScale;
            }
            SetVisible(true);
            _dragXZ = new Vector3(_homePos.x, 0f, _homePos.z);
            ClearHover();
            HideHeldLayers();
            HideShadow();
        }

        /// <summary>
        /// Builds the held-state layers and shows them for a frame. Called once before the sequence so
        /// URP compiles the outline/shadow/dot shader variants off the clock instead of stalling the
        /// first real frames of the run.
        /// </summary>
        public void WarmLayers()
        {
            EnsureLayers();
            BeginHold(false);
            _lift = 1f;
            ApplyTransform();
        }

        // ------------------------------------------------------------------ user pointer driving

        void Update()
        {
            if (!allowUserInput || _programmatic || _consumed || block == null) return;
            if (_activeDrag != null && _activeDrag != this) return;

            Pointer pointer = Pointer.current;
            if (pointer == null) return;

            Camera cam = ResolveCamera();
            if (cam == null) return;

            Vector2 screen = pointer.position.ReadValue();
            // A press has to be resolved on the plane the player is looking at, which is the block's
            // TOP FACE, not the board under it. The camera is orthographic and pitched 80 degrees, so
            // a face h above the board is drawn 0.176*h world units up-screen of its own cells: a
            // press unprojected onto the board plane lands that much toward +z of where the finger
            // actually was. Block-Square sits directly below Block-L, so that entire band along the
            // top of the drawn green face mapped into the red block's cells.
            // CombinedBounds already carries the lift while the block is held, so the same expression
            // keeps the drag on the same plane the press was resolved on - no jump on the first
            // drag frame from swapping planes underneath the pointer.
            float pressPlaneY = Mathf.Max(_homePos.y, _artBounds.max.y + (block.position.y - _homePos.y));
            Plane plane = new Plane(Vector3.up, new Vector3(0f, pressPlaneY, 0f));
            Ray ray = cam.ScreenPointToRay(screen);
            float enter;
            if (!plane.Raycast(ray, out enter)) return;
            Vector3 point = ray.GetPoint(enter);

            if (!_userDragging && pointer.press.wasPressedThisFrame)
            {
                // One arbiter for the whole board, not one bounding box per component. Every
                // controller reaches the same verdict for the same press, so exactly one claims it.
                if (ResolvePick(point) == this)
                {
                    _userDragging = true;
                    _activeDrag = this;
                    // Hold the block where it was grabbed. Without this the root teleports onto the
                    // pointer on the first drag frame - and on Block-L, whose art sits half a cell
                    // from its root, the piece jumps 0.5 world units (61 screen px) sideways the
                    // instant it is touched.
                    _grabOffset = new Vector3(block.position.x - point.x, 0f, block.position.z - point.z);
                    BeginHold();
                    _lift = 1f;
                    ApplyTransform();
                }
                return;
            }

            if (!_userDragging) return;

            if (pointer.press.isPressed)
            {
                _dragXZ = new Vector3(point.x + _grabOffset.x, 0f, point.z + _grabOffset.z);
                ApplyTransform();
                EvaluateHover();
                return;
            }

            if (pointer.press.wasReleasedThisFrame)
            {
                _userDragging = false;
                if (_activeDrag == this) _activeDrag = null;
                ResolveRelease();
            }
        }

        /// <summary>
        /// The single decision point every release goes through, whichever driver made it. A drop on a
        /// hole this block fits hands over to the director; anything else - empty board or the wrong
        /// hole - sends the block home and starts nothing at all.
        /// </summary>
        void ResolveRelease()
        {
            HoleGlowHighlight hole = _hover;
            bool match = hole != null && hole.Matches(_shapeId);
            _lastDropHole = hole;
            _lastDropMatched = match;
            ClearHover();

            Debug.Log(string.Format("[Case2] DROP block={0} shape={1} over={2} match={3} -> {4}",
                block != null ? block.name : "<null>", _shapeKey,
                hole != null ? hole.name : "<empty board>", match,
                match ? "SEQUENCE" : "RETURN_HOME"));

            if (match)
            {
                Action<BlockDragController, HoleGlowHighlight> handler = OnUserDrop;
                if (handler != null) { handler(this, hole); return; }
            }

            Action<BlockDragController, HoleGlowHighlight> missed = OnUserMiss;
            if (missed != null) missed(this, hole);
            StartCoroutine(ReturnHome(0.18f));
        }

        // ------------------------------------------------------------------ simulated pointer

        /// <summary>
        /// Drives a full pick-up / drag / release with no real pointer behind it, through exactly the
        /// same release decision a real drop goes through. This is what lets the input gate be run in
        /// batchmode, where there is no mouse at all (lesson #7).
        /// </summary>
        public IEnumerator SimulateDrop(Vector3 worldPoint, float pickupTime, float moveTime)
        {
            if (_consumed || block == null) yield break;

            _activeDrag = this;
            _grabOffset = Vector3.zero;
            yield return Pickup(pickupTime);
            // MoveTo drives the transform, so aim the transform at the point that puts the ART there.
            Vector3 artOffset = ArtOffset;
            yield return MoveTo(new Vector3(worldPoint.x - artOffset.x, worldPoint.y, worldPoint.z - artOffset.z),
                                moveTime, EaseType.InOutSine);
            yield return Hover(0.05f);

            _programmatic = false;
            _activeDrag = null;
            ResolveRelease();
        }

        // ------------------------------------------------------------------ shared internals

        /// <summary>
        /// Lifts the block into the held state. <paramref name="lightMatchingHole"/> is false only
        /// for <see cref="WarmLayers"/>, which runs this off the clock during prewarm to compile
        /// shader variants and must not light anything.
        /// </summary>
        void BeginHold(bool lightMatchingHole = true)
        {
            EnsureLayers();
            _held = true;

            // The target hole lights on PICKUP, not on proximity. Measured on the reference: the
            // green hole's halo steps on at f199 with the green block still at its own starting
            // cell, four cells away and not moving toward it yet; the red hole's does the same at
            // f736. So the cue answers "which hole does this piece belong to", which is the thing
            // the player needs before they start dragging - not "are you nearly there".
            //
            // Deliberately NOT routed through _hover. _hover is the DROP decision and has to stay
            // proximity-based; if the lit hole were _hover, then dragging the block away from its
            // own hole would put the cue out exactly when it is most needed.
            if (lightMatchingHole)
            {
                _litHole = null;
                for (int i = 0; i < holes.Length; i++)
                {
                    if (holes[i] == null || !holes[i].Matches(_shapeId)) continue;
                    _litHole = holes[i];
                    _litHole.SetLit(true);
                    Debug.Log(string.Format("[Case2] PICKUP block={0} shape={1} lights hole={2} (shape={3})",
                        block != null ? block.name : "<null>", _shapeKey, _litHole.name, _litHole.shapeKey));
                    break;
                }
            }
            _tiltDeg = 0f;
            _dragXZ = new Vector3(block.position.x, 0f, block.position.z);
            if (_outline != null) _outline.gameObject.SetActive(true);
            if (_grabDot != null) _grabDot.gameObject.SetActive(true);
            if (_shadow != null) _shadow.gameObject.SetActive(true);
            PushOutlineColor();
        }

        void ApplyTransform()
        {
            if (block == null) return;
            Vector3 next = new Vector3(_dragXZ.x, _homePos.y + liftHeight * _lift, _dragXZ.z);
            UpdateDragTilt(next);
            block.position = next;
            UpdateShadow();
        }

        /// <summary>
        /// Leans the block into the drag. Measured off the reference rather than dialled in by eye -
        /// see the Drag tilt header for the regression. Pitch about WORLD X, not the block's own x:
        /// two of the four blocks carry an authored yaw (Block-L 180 degrees, Block-2 90), so
        /// <c>_homeRot * Euler(t,0,0)</c> would tilt them about their local axis and Block-L would
        /// lean the wrong way.
        /// </summary>
        void UpdateDragTilt(Vector3 next)
        {
            float dt = Time.deltaTime;
            if (dt <= 0f) return;
            float vz = (next.z - block.position.z) / dt;
            float vx = (next.x - block.position.x) / dt;
            float pitchTarget = Mathf.Clamp(vz * dragTiltDegPerUnitPerSecond, -dragTiltMaxDegrees, dragTiltMaxDegrees);
            // Negative about +Z tips the top toward +x, so the block leans INTO a sideways drag the
            // same way it leans into a vertical one.
            float rollTarget = Mathf.Clamp(-vx * dragRollDegPerUnitPerSecond, -dragTiltMaxDegrees, dragTiltMaxDegrees);
            float k = dragTiltSmoothing > 0.0001f ? 1f - Mathf.Exp(-dt / dragTiltSmoothing) : 1f;
            _tiltDeg = Mathf.Lerp(_tiltDeg, pitchTarget, k);
            _rollDeg = Mathf.Lerp(_rollDeg, rollTarget, k);
            // Both in WORLD axes, applied before _homeRot. Block-L carries a 180-degree authored yaw
            // and Block-2 a 90; composing either of these after _homeRot leans them about their own
            // axes, which sends Block-L the wrong way and turns Block-2's pitch into a roll.
            block.rotation = Quaternion.AngleAxis(_tiltDeg, Vector3.right)
                           * Quaternion.AngleAxis(_rollDeg, Vector3.forward)
                           * _homeRot;
        }

        /// <summary>Current drag lean in degrees about world X. Read by the gate.</summary>
        public float DragTiltDegrees { get { return _tiltDeg; } }

        /// <summary>Current drag roll in degrees about world Z. Read by the gate.</summary>
        public float DragRollDegrees { get { return _rollDeg; } }

        /// <summary>
        /// Drives one frame of held motion through the real transform path, so the gate can hold a
        /// block at a controlled velocity without a pointer. Same code a real drag frame runs.
        /// </summary>
        public void DriveHeldTo(float x, float z)
        {
            _dragXZ = new Vector3(x, 0f, z);
            ApplyTransform();
        }

        void UpdateShadow()
        {
            if (_shadow == null || !_shadow.gameObject.activeSelf) return;

            float lift = Mathf.Clamp01((block.position.y - _homePos.y) / Mathf.Max(0.001f, liftHeight));
            // Follow the mesh, not the pivot: on a block whose art hangs off a child the two are not
            // the same point and the shadow would sit beside the block instead of under it.
            Vector3 meshWorld = block.TransformPoint(_meshLocalOffset);
            _shadow.rotation = _blockFilter != null ? _blockFilter.transform.rotation : block.rotation;
            Vector3 p = meshWorld + shadowOffset * lift;
            _shadow.position = new Vector3(p.x, shadowGroundY, p.z);
            float s = Mathf.Lerp(1f, 1.06f, lift);
            _shadow.localScale = new Vector3(s, 0.02f, s);

            if (_mpb == null) _mpb = new MaterialPropertyBlock();
            _shadowRenderer.GetPropertyBlock(_mpb);
            _mpb.SetColor(BaseColorId, new Color(0.05f, 0.05f, 0.08f, Mathf.Lerp(0f, 0.30f, lift)));
            _shadowRenderer.SetPropertyBlock(_mpb);
        }

        void HideShadow()
        {
            if (_shadow != null) _shadow.gameObject.SetActive(false);
        }

        void HideHeldLayers()
        {
            if (_outline != null) _outline.gameObject.SetActive(false);
            if (_grabDot != null) _grabDot.gameObject.SetActive(false);
        }

        void PushOutlineColor()
        {
            if (_outlineRenderer == null) return;
            if (_mpb == null) _mpb = new MaterialPropertyBlock();
            _outlineRenderer.GetPropertyBlock(_mpb);
            _mpb.SetColor(OutlineColorId, Color.white);
            _mpb.SetFloat(OutlineWidthId, outlineWidth);
            _outlineRenderer.SetPropertyBlock(_mpb);
        }

        void EvaluateHover()
        {
            HoleGlowHighlight best = null;
            float bestDist = hoverRadius;
            // The ART centre, not the transform. On Block-L those are half a cell apart, so hovering
            // used to be judged from a point that is not where the player sees the piece: the block
            // lit a hole while its art was still 0.5 cells (61 screen px) short of it, and stopped
            // lighting it while the art was over it.
            Vector3 p = ArtCentre;

            for (int i = 0; i < holes.Length; i++)
            {
                HoleGlowHighlight h = holes[i];
                if (h == null) continue;
                Vector3 d = h.SnapPoint - p;
                d.y = 0f;
                float dist = d.magnitude;
                if (dist < bestDist) { bestDist = dist; best = h; }
            }

            if (best != _hover)
            {
                // Never darken the hole this block is being carried to: that light belongs to the
                // hold, not to the hover, and ClearHover here would switch it off the moment the
                // block drifted out of hoverRadius.
                if (_hover != null && _hover != _litHole) _hover.ClearHover();
                _hover = best;
            }
            if (_hover != null && _hover != _litHole) _hover.RequestGlow(_shapeId);
        }

        void ClearHover()
        {
            for (int i = 0; i < holes.Length; i++)
            {
                if (holes[i] != null) holes[i].ClearHover();
            }
            _hover = null;
            _litHole = null;
        }

        Camera ResolveCamera()
        {
            if (targetCamera != null) return targetCamera;
            targetCamera = Camera.main;
            return targetCamera;
        }

        void EnsureLayers()
        {
            if (block == null || _blockFilter == null || _blockFilter.sharedMesh == null) return;

            if (_outline == null && outlineMaterial != null)
            {
                GameObject go = new GameObject("BlockOutline");
                go.transform.SetParent(_blockFilter.transform, false);
                go.hideFlags = HideFlags.DontSave;
                MeshFilter mf = go.AddComponent<MeshFilter>();
                mf.sharedMesh = _blockFilter.sharedMesh;
                MeshRenderer mr = go.AddComponent<MeshRenderer>();
                mr.sharedMaterial = outlineMaterial;
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                mr.receiveShadows = false;
                _outline = go.transform;
                _outlineRenderer = mr;
                go.SetActive(false);
            }

            if (_grabDot == null && grabDotMaterial != null)
            {
                GameObject go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                go.name = "GrabDot";
                Collider col = go.GetComponent<Collider>();
                if (col != null) Destroy(col);
                go.transform.SetParent(_blockFilter.transform, false);
                go.hideFlags = HideFlags.DontSave;
                Bounds b = _blockFilter.sharedMesh.bounds;
                go.transform.localPosition = new Vector3(b.center.x, b.max.y + 0.02f, b.center.z);
                go.transform.localScale = new Vector3(0.20f, 0.06f, 0.20f);
                MeshRenderer mr = go.GetComponent<MeshRenderer>();
                mr.sharedMaterial = grabDotMaterial;
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                mr.receiveShadows = false;
                _grabDot = go.transform;
                go.SetActive(false);
            }

            if (_shadow == null && shadowMaterial != null)
            {
                GameObject go = new GameObject("DragShadow");
                go.transform.SetParent(block.parent, false);
                go.hideFlags = HideFlags.DontSave;
                MeshFilter mf = go.AddComponent<MeshFilter>();
                mf.sharedMesh = _blockFilter.sharedMesh;
                MeshRenderer mr = go.AddComponent<MeshRenderer>();
                mr.sharedMaterial = shadowMaterial;
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                mr.receiveShadows = false;
                _shadow = go.transform;
                _shadowRenderer = mr;
                go.SetActive(false);
            }
        }
    }
}
