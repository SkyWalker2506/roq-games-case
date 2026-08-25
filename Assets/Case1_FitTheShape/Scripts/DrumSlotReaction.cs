using System;
using System.Collections.Generic;
using UnityEngine;
using Shared.Juice;
using Shared.Tweening;

namespace Case1
{
    /// <summary>
    /// Everything the drum does when a shape drops into one of its slot cells. Four of the five visual
    /// layers measured off the reference frame live here:
    ///   1. white-hot bloom in the cell centre that settles into the shape's colour in ~0.08 s,
    ///   2. yellow four-armed sparkle burst out of that cell,
    ///   3. the same sparkle spilling onto the neighbouring cells a few frames later,
    ///   4. a scale/lift pulse that travels across the whole drum, delayed by each cell's distance from
    ///      the impact - the detail that makes the reaction read as a wave instead of a single pop.
    /// Colour changes go through a MaterialPropertyBlock; no material is ever cloned.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class DrumSlotReaction : MonoBehaviour
    {
        /// <summary>One drum slot cell, discovered and wired by Case1SceneSetup.</summary>
        [Serializable]
        public sealed class Cell
        {
            /// <summary>Segment root (Segment_c&lt;col&gt;_r&lt;row&gt;).</summary>
            public Transform root;
            /// <summary>Renderer of the coloured cell body.</summary>
            public Renderer body;
            /// <summary>Renderer of the recessed hole inside the cell.</summary>
            public Renderer hole;
            /// <summary>The question-mark cover that hides an unfilled cell.</summary>
            public Transform mystery;
            /// <summary>Shape identity of this cell's socket recess.</summary>
            public ShapeId shapeId;
            /// <summary>Column index across the drum (0 = leftmost).</summary>
            public int column;
            /// <summary>Row index around the drum (0 = the row facing the camera).</summary>
            public int row;

            [NonSerialized] public Vector3 BaseScale;
            [NonSerialized] public Vector3 BasePosition;
            [NonSerialized] public Vector3 MysteryBaseScale;
            [NonSerialized] public bool MysteryStartedOn;
            [NonSerialized] public Color BaseBodyColor;
            [NonSerialized] public Color BaseHoleColor;
            [NonSerialized] public float RippleDelay;
            [NonSerialized] public float RippleAmount;
            [NonSerialized] public float RippleRandomJitter;
            [NonSerialized] public float RippleRandomFreq;
            /// <summary>Direction this cell travels while the wave passes, in its parent's local space.</summary>
            [NonSerialized] public Vector3 RippleDirection;
        }

        [Header("Scene wiring (filled in by Case1SceneSetup)")]
        public Cell[] cells = new Cell[0];
        [Tooltip("Generated recess per cell, filled in by Case1SceneSetup. When a piece lands the recess " +
                 "is switched off and the cell reads as FILLED - in the reference the arriving piece seats " +
                 "flush and the hole is gone.")]
        public Transform[] cellGlyphs = new Transform[0];

        public GameObject sparklePrefab;
        public Material flashMaterial;
        [Tooltip("Additive ring material (Case1/SlotFillFlash with _Ring > 0) for the arrival shockwave.")]
        public Material ringMaterial;

        [Header("Cell-contained arrival bloom (VIDEO_MEASURED: one 45 fps frame)")]
        public float flashDuration = 1f / 45f;
        public float flashRise = 0.24f;
        public float flashColorSettle = 1f / 45f;
        public float flashSizeFactor = 0.82f;
        public float cellWhiteDuration = 0.085f;
        public float mysteryPopDuration = 0.085f;

        [Header("Arrival shockwave ring")]
        public bool enableShockwaveRing = true;
        [Tooltip("Ring diameter at the end of the expansion, in cell pitches.")]
        public float ringEndFactor = 1.65f;
        [Tooltip("Ring diameter when it is born, in cell pitches.")]
        public float ringStartFactor = 0.70f;
        public float ringDuration = 0.18f;

        [Header("Sparkle burst and neighbour spill")]
        public float neighbourDelayStep = 0.032f;
        public float neighbourScale = 0.22f;
        [Tooltip("Lead-in from the impact flash to the star burst.")]
        public float sparkleDelay = 0.035f;

        [Header("Arrival glow under the star burst")]
        public float glowDuration = 0.230f;
        public float glowSizeFactor = 1.05f;
        [Range(0f, 1f)] public float glowStrength = 0.62f;

        // MEASURED against CASE1_SEKANS.mp4: the reference's landing response is a localized
        // flash with faint neighbour outlines - its peak frame-to-frame motion is ~1/3 of what the
        // old values produced (amplitude 0.48 x lift 1.45 displaced every cell up to 0.87 world
        // units, and falloff 0.16 left cells 5 steps away at 61% amplitude, so the whole drum
        // heaved). Owner asked for simplification to match. Displacement is now ~1/5 (0.18 x 0.9)
        // and falloff 0.55 keeps the wave genuinely local (5 steps away: 31%).
        [Header("Drum ripple (radial distance-staggered wave)")]
        public float rippleDelayStart = 0.020f;
        public float rippleSecondsPerUnit = 0.080f;
        public float rippleMaxDelay = 0.650f;
        public float ripplePulse = 0.650f;
        public float rippleAmplitude = 0.18f;
        public float rippleFalloff = 0.55f;
        public float rippleLift = 0.9f;

        readonly List<int> _spillTargets = new List<int>(6);
        MaterialPropertyBlock _fillBlock;
        GameObject _flashObject;
        Renderer _flashRenderer;
        GameObject _ringObject;
        Renderer _ringRenderer;
        MaterialPropertyBlock _block;
        Mesh _quad;

        float _cellPitch = -1f;
        float _rippleStart;
        float _rippleTime;
        float _rippleEnd;
        bool _rippleActive;
        int _impactIndex = -1;
        /// <summary>Recess renderers switched off by a fill, so ResetAll puts exactly those back.</summary>
        readonly List<Renderer> _hiddenRecess = new List<Renderer>();

        /// <summary>Cells that already hold a piece. A filled cell takes no more.</summary>
        readonly HashSet<int> _filled = new HashSet<int>();
        TweenHandle _impactPulse = TweenHandle.None;

        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        static readonly int ColorId = Shader.PropertyToID("_Color");

        /// <summary>Number of wired cells.</summary>
        public int CellCount { get { return cells != null ? cells.Length : 0; } }

        void Awake()
        {
            _block = new MaterialPropertyBlock();
            CaptureRestState();
        }

        /// <summary>Records the untouched pose and colour of every cell so a replay can restore them exactly.</summary>
        public void CaptureRestState()
        {
            if (cells == null) return;
            for (int i = 0; i < cells.Length; i++)
            {
                Cell c = cells[i];
                if (c == null || c.root == null) continue;
                c.BaseScale = c.root.localScale;
                c.BasePosition = c.root.localPosition;
                if (c.mystery != null)
                {
                    c.MysteryBaseScale = c.mystery.localScale;
                    c.MysteryStartedOn = c.mystery.gameObject.activeSelf;
                }
                c.BaseBodyColor = ReadColor(c.body, Color.white);
                c.BaseHoleColor = ReadColor(c.hole, Color.grey);
            }
        }

        /// <summary>True once a piece has landed in this cell; it cannot take another.</summary>
        public bool IsFilled(int index) { return _filled.Contains(index); }

        /// <summary>
        /// Finds the first matching unfilled and uncovered cell on the active live row (row 0), or -1.
        /// </summary>
        public int FindAvailableLiveSlot(ShapeId id)
        {
            if (cells == null) return -1;
            for (int col = 0; col < 5; col++)
            {
                int index = IndexOf(col, 0);
                if (index < 0 || index >= cells.Length) continue;
                Cell c = cells[index];
                if (c == null) continue;
                if (IsFilled(index)) continue;
                if (c.mystery != null && c.mystery.gameObject.activeSelf) continue;
                if (c.shapeId == id) return index;
            }
            return -1;
        }

        /// <summary>Scene name of a cell, for logs and the selection gate.</summary>
        public string CellName(int index)
        {
            if (cells == null || index < 0 || index >= cells.Length || cells[index] == null || cells[index].root == null) return "<none>";
            return cells[index].root.name;
        }

        /// <summary>Mesh name of a cell's hole recess (e.g. "Round-Hole"), for shape matching and logs.</summary>
        public string HoleMeshName(int index)
        {
            if (cells == null || index < 0 || index >= cells.Length || cells[index] == null) return "-";
            MeshFilter mf = cells[index].hole != null ? cells[index].hole.GetComponent<MeshFilter>() : null;
            return mf != null && mf.sharedMesh != null ? mf.sharedMesh.name : "-";
        }

        /// <summary>World centre of a cell's hole - where a shape has to arrive.</summary>
        public Vector3 HoleCenter(int index)
        {
            Cell c = cells[index];
            if (c.body != null && c.body.enabled && c.body.gameObject.activeInHierarchy) return c.body.bounds.center;
            if (c.root != null) return c.root.position;
            if (c.hole != null) return c.hole.bounds.center;
            return Vector3.zero;
        }

        /// <summary>Outward normal of a cell's face; a shape enters along the opposite direction.</summary>
        public Vector3 FaceNormal(int index) { return cells[index].root != null ? cells[index].root.up : Vector3.up; }

        /// <summary>Rotation a shape must take to sit flush in the cell.</summary>
        public Quaternion FaceRotation(int index) { return cells[index].root != null ? cells[index].root.rotation : Quaternion.identity; }

        /// <summary>
        /// Distance from the hole centre out to the cell's front surface. The hole mesh is a recess, so
        /// its bounds centre sits *inside* the cell body: anything spawned there is depth-rejected by the
        /// cell itself. Everything additive is pushed past this offset.
        /// </summary>
        public float FaceOffset(int index)
        {
            Cell c = cells[index];
            Renderer r = (c.body != null && c.body.enabled && c.body.gameObject.activeInHierarchy) ? c.body : c.hole;
            if (r == null || c.root == null) return 0.1f;

            Vector3 n = FaceNormal(index);
            Bounds b = r.bounds;
            float support = Mathf.Abs(n.x) * b.extents.x + Mathf.Abs(n.y) * b.extents.y + Mathf.Abs(n.z) * b.extents.z;
            return Vector3.Dot(b.center + n * support - HoleCenter(index), n);
        }

        /// <summary>Point on the cell's outward axis, <paramref name="sizeFactor"/> cell-widths clear of its front surface.</summary>
        public Vector3 FacePoint(int index, float sizeFactor)
        {
            return HoleCenter(index) + FaceNormal(index) * (FaceOffset(index) + CellSize(index) * sizeFactor);
        }

        /// <summary>Widest world-space dimension of a cell's hole, used to size the flash and the sparkles.</summary>
        public float CellSize(int index)
        {
            if (_cellPitch > 0f) return _cellPitch;

            // Column pitch, i.e. the real on-screen width of a cell. The hole mesh is a shallow recess and
            // its bounds are a fraction of that; sizing the flare and the sparkles off it made both far
            // too small to read at capture resolution.
            int a = IndexOf(0, 0);
            int b = IndexOf(1, 0);
            if (a >= 0 && b >= 0)
            {
                _cellPitch = Vector3.Distance(cells[a].root.position, cells[b].root.position);
            }
            if (_cellPitch <= 0.05f)
            {
                Renderer r = cells[index].hole != null ? cells[index].hole : cells[index].body;
                Vector3 s = r != null ? r.bounds.size : Vector3.one;
                _cellPitch = Mathf.Max(0.2f, Mathf.Max(s.x, Mathf.Max(s.y, s.z)));
            }
            return _cellPitch;
        }

        /// <summary>Index of the cell at (column, row), or -1.</summary>
        public int IndexOf(int column, int row)
        {
            for (int i = 0; i < cells.Length; i++)
            {
                if (cells[i] != null && cells[i].column == column && cells[i].row == row) return i;
            }
            return -1;
        }

        /// <summary>
        /// Renders one invisible pass of both flare materials and fills the sparkle pool, so the shader
        /// compile and the first Instantiate happen before the timed sequence rather than inside it.
        /// </summary>
        public void Warmup()
        {
            if (cells == null || cells.Length == 0) return;

            EnsureFlash();
            _flashObject.SetActive(true);
            _flashObject.transform.SetPositionAndRotation(FacePoint(0, 0.03f), FaceRotation(0));
            _flashObject.transform.localScale = Vector3.one * CellSize(0);
            SetFlashColor(new Color(1f, 1f, 1f, 0f));   // renders, compiles, contributes nothing (additive)

            EnsureRing();
            if (enableShockwaveRing && _ringObject != null)
            {
                _ringObject.SetActive(true);
                _ringObject.transform.SetPositionAndRotation(FacePoint(0, 0.05f), FaceRotation(0));
                _ringObject.transform.localScale = Vector3.one * CellSize(0);
                SetRingColor(new Color(1f, 1f, 1f, 0f));
            }

            if (sparklePrefab != null)
            {
                VFXPool.Prewarm(sparklePrefab, 8);
                VFXPool.Play(sparklePrefab, FacePoint(0, 0.12f), Quaternion.identity, 0.001f);
            }

            // The warm-up quads have to be *rendered* once for the shader to compile, so they cannot be
            // switched off in the same call. Three frames later they are done and go away completely:
            // idle must show no additive quad at all, not an additive quad that happens to sit at alpha 0.
            StartCoroutine(EndWarmup());
        }

        System.Collections.IEnumerator EndWarmup()
        {
            yield return null;
            yield return null;
            yield return null;
            if (_flashObject != null) { SetFlashColor(new Color(0f, 0f, 0f, 0f)); _flashObject.SetActive(false); }
            if (_ringObject != null) { SetRingColor(new Color(0f, 0f, 0f, 0f)); _ringObject.SetActive(false); }
        }

        // ------------------------------------------------------------------ the reaction

        /// <summary>
        /// Fires the whole arrival reaction on <paramref name="index"/>: bloom, reveal, sparkle burst,
        /// neighbour spill and the travelling ripple. <paramref name="tint"/> is the arriving shape's colour.
        /// </summary>
        public void Impact(int index, Color tint)
        {
            if (index < 0 || index >= cells.Length) return;
            Cell c = cells[index];
            _impactIndex = index;
            _filled.Add(index);

            // Close the hole. VIDEO_MEASURED f061-f067: the piece seats into the recess and the cell
            // reads as a solid filled cell from then on, with a warm bloom over it. Leaving the recess
            // visible under a landed piece is the "it does not close" the reference never shows.
            if (cellGlyphs != null && index < cellGlyphs.Length && cellGlyphs[index] != null)
                cellGlyphs[index].gameObject.SetActive(false);
            // Switching the generated ink off is not enough on its own. The recess POCKET is the
            // prefab's own mesh and it carries the dark interior, and there are TWO of them: "Hole" and
            // the "Hole-Cap" that draws the pocket floor. With either left on the cell still shows a
            // dark shape under a landed piece. In the reference a filled cell is flat and solid, so
            // every recess renderer under the cell goes with the ink.
            if (c.root != null)
            {
                Renderer[] rs = c.root.GetComponentsInChildren<Renderer>(true);
                for (int r = 0; r < rs.Length; r++)
                {
                    if (rs[r] == null || rs[r] == c.body || !rs[r].enabled) continue;
                    if (rs[r].name.ToLowerInvariant().IndexOf("hole", System.StringComparison.Ordinal) < 0) continue;
                    rs[r].enabled = false;
                    _hiddenRecess.Add(rs[r]);
                }
            }
            // A cell that still draws anything but its own body after a fill is the duplicate-glyph
            // regression coming back, so it is reported rather than left to be spotted by eye.
            if (c.root != null)
            {
                Renderer[] all = c.root.GetComponentsInChildren<Renderer>(true);
                for (int r = 0; r < all.Length; r++)
                    if (all[r] != null && all[r] != c.body && all[r].enabled && all[r].gameObject.activeInHierarchy)
                        Debug.LogWarning("[Case1] FILL_LEFTOVER cell=" + index + " still drawing '" + all[r].name + "'");
            }
            if (c.body != null)
            {
                if (_fillBlock == null) _fillBlock = new MaterialPropertyBlock();
                c.body.GetPropertyBlock(_fillBlock);
                _fillBlock.SetColor(BaseColorId, tint);
                _fillBlock.SetFloat(Shader.PropertyToID("_ShapeType"), 0f);
                c.body.SetPropertyBlock(_fillBlock);
            }

            float size = CellSize(index);

            // ---- layer 1: white-hot bloom that settles into the shape colour
            EnsureFlash();
            _flashObject.SetActive(true);
            _flashObject.transform.SetPositionAndRotation(FacePoint(index, 0.03f), FaceRotation(index));
            _flashObject.transform.localScale = Vector3.one * (size * flashSizeFactor);
            ApplyFlash(0f, tint);
            float flashDur = Mathf.Max(0.01f, flashDuration);
            Tweener.Float(0f, 1f, flashDur, t => ApplyFlash(t, tint))
                   .OnComplete(() =>
                   {
                       // Zero the colour BEFORE deactivating: a tween that stops one sample short of 1
                       // would otherwise leave a non-zero alpha parked on the renderer, which is exactly
                       // the sort of thing that comes back as a smudge the next time the object is shown.
                       SetFlashColor(new Color(0f, 0f, 0f, 0f));
                       if (_flashObject != null) _flashObject.SetActive(false);
                   });

            // ---- layer 1b: an expanding shockwave ring, sized off the cell PITCH (the real on-screen
            // width of a cell), not off the shallow hole recess. Derived from the neighbour spacing the
            // ring ends up four cells wide, which is what makes the arrival read at capture resolution.
            EnsureRing();
            if (enableShockwaveRing && _ringObject != null)
            {
                Quaternion ringRotation = FaceRotation(index);
                Vector3 ringPosition = FacePoint(index, 0.06f);
                Color ringTint = Color.Lerp(tint, Color.white, 0.55f);
                _ringObject.SetActive(true);
                _ringObject.transform.SetPositionAndRotation(ringPosition, ringRotation);
                _ringObject.transform.localScale = Vector3.one * (size * ringStartFactor);
                SetRingColor(new Color(ringTint.r, ringTint.g, ringTint.b, 0f));

                Tweener.Float(0f, 1f, Mathf.Max(0.08f, ringDuration), t =>
                {
                    if (_ringObject == null) return;
                    float e = 1f - Mathf.Pow(1f - t, 3f);                       // OutCubic expansion
                    float d = Mathf.Lerp(size * ringStartFactor, size * ringEndFactor, e);
                    _ringObject.transform.localScale = new Vector3(d, d, d);
                    Color c = ringTint;
                    c.a = Mathf.Clamp01(t < 0.12f ? t / 0.12f : Mathf.Pow(1f - (t - 0.12f) / 0.88f, 1.4f));
                    SetRingColor(c);
                }).SetUnscaled(true).OnComplete(() => { if (_ringObject != null) _ringObject.SetActive(false); });
            }

            // ---- the question-mark cover pops away, revealing the filled cell
            if (c.mystery != null && c.mystery.gameObject.activeSelf)
            {
                Transform m = c.mystery;
                Vector3 from = c.MysteryBaseScale;
                Tweener.Float(1f, 0f, mysteryPopDuration, k =>
                {
                    if (m != null) m.localScale = from * k;
                }).SetEase(EaseType.InBack).OnComplete(() =>
                {
                    if (m != null) m.gameObject.SetActive(false);
                });
            }

            // VIDEO_MEASURED from f073..f083: screen-facing cell footprint follows
            // 1.00 -> 1.41 -> 0.86 -> 1.00. Scaling local X/Z enlarges the visible face; the previous
            // SquashAxis.Y stretched only the outward normal and made the face smaller on screen.
            PulseImpactCell(c, size);

            // ---- MEASURED against Fit The Shape.mp4 t=1.33: the reference's arrival sparkle is about a
            // cell wide and unmistakable at 1080x1728. Ours was sized at 0.22 of a cell, which at capture
            // resolution is a few pixels - present in the report, invisible on screen. That is exactly the
            // "gate green, nothing on screen" failure, so the accent is now sized off the cell itself.
            // Instant arrival sparkles matching reference reward timing
            PlaySparkle(index, 0.35f);
            PlayArrivalGlow(index, size);

            // Only the two horizontal neighbours are considered "spill" for reporting. They do not get
            // their own particle bursts; the visible secondary motion comes from the wheel ripple below.
            _spillTargets.Clear();
            AddNeighbour(c.column - 1, c.row);
            AddNeighbour(c.column + 1, c.row);

            // ---- restrained ripple across the wheel. Small amplitude, short delay, quick settle.
            StartRipple(index);
        }

        /// <summary>World size the last arrival sparkle was played at; the capture report prints it.</summary>
        public float LastSparkleSize { get; private set; }

        /// <summary>Number of immediate neighbour cells included in the last local reaction report.</summary>
        public int SpillCount { get { return _spillTargets.Count; } }

        /// <summary>Longest ripple delay in the current wave, in seconds; proof the pulse is distance-staggered.</summary>
        public float RippleSpan { get; private set; }

        /// <summary>
        /// DEAD CONTROL WARNING - <paramref name="scale"/> DOES NOT SIZE THE STARS.
        ///
        /// It reaches VFXPool.Play, which writes it to transform.localScale. The sparkle prefab has
        /// main.scalingMode = ParticleSystemScalingMode.Shape, and under Shape the transform scale
        /// drives the EMITTER SHAPE ONLY and never particle size. So this argument moves the emission
        /// circle (0.55 world units at scale 1) and nothing else. Star size lives in main.startSize,
        /// authored in Case1SceneSetup.EnsureSparklePrefab.
        ///
        /// Commit ecbabcd changed this call from 0.65f to 0.35f to "reduce sparkle burst star size".
        /// Measured before/after: the stars did not shrink at all - they PACKED TIGHTER, because the
        /// only thing that changed was the emitter radius.
        ///
        /// The reason this cost a round rather than being caught immediately is the line below:
        /// LastSparkleSize reports CellSize(index) * scale into the capture report. A control whose
        /// only downstream consumer is a log line is worse than a control that does nothing, because
        /// it manufactures its own confirmation - the report would have said the stars got smaller
        /// while the pixels said they had not. If you change this value, verify it in pixels.
        /// </summary>
        void PlaySparkle(int index, float scale)
        {
            if (sparklePrefab == null || index < 0 || index >= cells.Length) return;
            Camera cam = Camera.main;
            Vector3 pos = FacePoint(index, 0.50f);
            if (cam != null)
            {
                Vector3 toCam = (cam.transform.position - pos).normalized;
                pos += toCam * 0.90f;
            }
            Quaternion rot = (cam != null) ? Quaternion.LookRotation(-cam.transform.forward, cam.transform.up) : Quaternion.LookRotation(FaceNormal(index));
            GameObject burst = VFXPool.Play(sparklePrefab, pos, rot, scale);
            LastSparkleSize = CellSize(index) * scale;
        }

        void CollectNeighbours(int index)
        {
            _spillTargets.Clear();
            Cell c = cells[index];
            AddNeighbour(c.column - 1, c.row);
            AddNeighbour(c.column + 1, c.row);
            AddNeighbour(c.column, c.row + 1);
            AddNeighbour(c.column, c.row - 1);
            AddNeighbour(c.column - 2, c.row);
            AddNeighbour(c.column + 2, c.row);
        }

        void AddNeighbour(int column, int row)
        {
            int i = IndexOf(column, row);
            if (i >= 0) _spillTargets.Add(i);
        }

        void PulseImpactCell(Cell c, float cellSize)
        {
            if (c == null || c.root == null) return;
            _impactPulse.Cancel();
            Transform target = c.root;
            Vector3 baseScale = c.BaseScale;
            Vector3 basePosition = c.BasePosition;
            Vector3 liftAxis = RadialLiftLocal(c);
            const float duration = 0.22f;

            _impactPulse = Tweener.Float(0f, 1f, duration, u =>
            {
                if (target == null) return;
                float faceScale;
                float lift;
                if (u < 0.25f)
                {
                    float p = Mathf.Clamp01(u / 0.25f);
                    p = 1f - Mathf.Pow(1f - p, 3f);
                    faceScale = Mathf.LerpUnclamped(1f, 1.41f, p);
                    lift = Mathf.LerpUnclamped(0f, 0.075f, p);
                }
                else if (u < 0.55f)
                {
                    float p = Mathf.Clamp01((u - 0.25f) / 0.30f);
                    p = p * p * (3f - 2f * p);
                    faceScale = Mathf.LerpUnclamped(1.41f, 0.86f, p);
                    lift = Mathf.LerpUnclamped(0.075f, -0.018f, p);
                }
                else
                {
                    float p = Mathf.Clamp01((u - 0.55f) / 0.45f);
                    p = 1f - Mathf.Pow(1f - p, 3f);
                    faceScale = Mathf.LerpUnclamped(0.86f, 1f, p);
                    lift = Mathf.LerpUnclamped(-0.018f, 0f, p);
                }

                float normalScale = faceScale >= 1f
                    ? Mathf.Lerp(1f, 0.92f, (faceScale - 1f) / 0.41f)
                    : Mathf.Lerp(1f, 1.04f, (1f - faceScale) / 0.14f);
                target.localScale = new Vector3(baseScale.x * faceScale,
                                                baseScale.y * normalScale,
                                                baseScale.z * faceScale);
                target.localPosition = basePosition + liftAxis * (lift * cellSize);
            }).OnComplete(() =>
            {
                if (target == null) return;
                target.localScale = baseScale;
                target.localPosition = basePosition;
            });
        }

        static Vector3 RadialLiftLocal(Cell c)
        {
            if (c == null || c.root == null) return Vector3.up;
            Transform parent = c.root.parent;
            Vector3 worldOut = c.root.up;
            return parent != null ? parent.InverseTransformDirection(worldOut).normalized : worldOut.normalized;
        }

        // ------------------------------------------------------------------ ripple

        void StartRipple(int index)
        {
            Cell impact = cells[index];
            float longest = 0f;

            for (int i = 0; i < cells.Length; i++)
            {
                Cell c = cells[i];
                if (c == null || c.root == null) continue;

                if (i == index) { c.RippleAmount = 0f; c.RippleDelay = 0f; continue; }

                float pitch = CellSize(index);
                float steps = Vector3.Distance(c.root.position, impact.root.position) / Mathf.Max(0.0001f, pitch);
                steps = Mathf.Max(1f, steps);

                // Deterministic pseudo-randomness per cell so it's consistent across replays/captures
                float hash = Mathf.Sin(i * 12.9898f + 78.233f) * 43758.5453f;
                float rnd01 = hash - Mathf.Floor(hash);
                float rndSigned = rnd01 * 2f - 1f;

                c.RippleRandomJitter = rndSigned;
                c.RippleRandomFreq = 1.35f + rnd01 * 0.85f;
                c.RippleDelay = rippleDelayStart + Mathf.Min(rippleMaxDelay, steps * rippleSecondsPerUnit) + rndSigned * 0.040f;
                c.RippleAmount = (rippleAmplitude / (1f + (steps - 1f) * rippleFalloff)) * (0.75f + 0.50f * rnd01);

                c.RippleDirection = RadialLiftLocal(c);
                if (c.RippleDelay > longest) longest = c.RippleDelay;
            }

            RippleSpan = longest;
            _rippleStart = Time.time;
            _rippleTime = 0f;
            _rippleEnd = longest + ripplePulse * 1.35f;
            _rippleActive = true;
        }

        void Update()
        {
            if (!_rippleActive) return;

            _rippleTime = Time.time - _rippleStart;
            for (int i = 0; i < cells.Length; i++)
            {
                Cell c = cells[i];
                if (c == null || c.root == null || c.RippleAmount <= 0f) continue;

                float u = (_rippleTime - c.RippleDelay) / ripplePulse;
                if (u <= 0f || u >= 1f) continue;

                // Multi-phase spring wave with natural inip-çıkma oscillation and per-cell randomness:
                // Primary outward pop (k > 0) -> rebound overshoot dip (k < 0) -> secondary settle
                float envelope = 1f - u;
                float wave = Mathf.Sin(u * Mathf.PI * c.RippleRandomFreq) * Mathf.Exp(-u * 1.8f);
                float subWave = Mathf.Sin(u * Mathf.PI * (c.RippleRandomFreq * 1.65f)) * envelope * 0.50f * c.RippleRandomJitter;
                float k = (wave + subWave) * c.RippleAmount;

                c.root.localScale = new Vector3(c.BaseScale.x * (1f + k),
                                                c.BaseScale.y * (1f - k * 0.15f),
                                                c.BaseScale.z * (1f + k));
                c.root.localPosition = c.BasePosition + c.RippleDirection * (k * rippleLift);
            }

            if (_rippleTime >= _rippleEnd)
            {
                _rippleActive = false;
                RestorePoses();
            }
        }

        // ------------------------------------------------------------------ reset

        /// <summary>Puts every cell back to its untouched pose, colour and cover so a replay looks identical.</summary>
        public void ResetAll()
        {
            _rippleActive = false;
            _rippleTime = 0f;
            _impactIndex = -1;
            _spillTargets.Clear();
            _filled.Clear();
            _impactPulse.Cancel();
            _impactPulse = TweenHandle.None;
            for (int h = 0; h < _hiddenRecess.Count; h++) if (_hiddenRecess[h] != null) _hiddenRecess[h].enabled = true;
            _hiddenRecess.Clear();

            for (int i = 0; i < cells.Length; i++)
            {
                Cell c = cells[i];
                if (c == null || c.root == null) continue;

                Squash.Cancel(c.root);
                c.root.localScale = c.BaseScale;
                c.root.localPosition = c.BasePosition;

                if (c.body != null) c.body.SetPropertyBlock(null);
                if (c.hole != null) c.hole.SetPropertyBlock(null);
                if (cellGlyphs != null && i < cellGlyphs.Length && cellGlyphs[i] != null)
                    cellGlyphs[i].gameObject.SetActive(true);

                if (c.mystery != null)
                {
                    c.mystery.localScale = c.MysteryBaseScale;
                    c.mystery.gameObject.SetActive(c.MysteryStartedOn);
                }
            }

            if (_flashObject != null) { SetFlashColor(new Color(0f, 0f, 0f, 0f)); _flashObject.SetActive(false); }
            if (_ringObject != null) { SetRingColor(new Color(0f, 0f, 0f, 0f)); _ringObject.SetActive(false); }
            VFXPool.ReclaimAll();
        }

        void RestorePoses()
        {
            for (int i = 0; i < cells.Length; i++)
            {
                Cell c = cells[i];
                if (c == null || c.root == null || i == _impactIndex) continue;
                c.root.localScale = c.BaseScale;
                c.root.localPosition = c.BasePosition;
            }
        }

        // ------------------------------------------------------------------ helpers

        void ApplyFlash(float t, Color tint)
        {
            if (_flashRenderer == null) return;

            float alpha;
            if (t < flashRise) alpha = t / Mathf.Max(0.0001f, flashRise);
            else alpha = Mathf.Pow(1f - (t - flashRise) / Mathf.Max(0.0001f, 1f - flashRise), 1.6f);

            float settle = Mathf.Clamp01(t * flashDuration / Mathf.Max(0.0001f, flashColorSettle));
            // The single-frame bloom is gold-white in the source regardless of the arriving cell hue.
            Color c = Color.Lerp(Color.white, new Color(1f, 0.68f, 0.12f, 1f), settle);
            c.a = Mathf.Clamp01(alpha);

            SetFlashColor(c);
        }

        /// <summary>
        /// The soft warm core the reference shows UNDER its star burst. MEASURED: the hard flash is a
        /// single frame at f022, but at f031 - with the stars - the cell centre carries a wide, gentle
        /// glow that is gone again by f039. Without it our burst was stars over a flat cell while the
        /// reference's stars sit in light, which is most of why the two read differently.
        /// </summary>
        void PlayArrivalGlow(int index, float size)
        {
            if (index < 0 || index >= cells.Length) return;
            EnsureFlash();
            if (_flashObject == null) return;

            _flashObject.SetActive(true);
            _flashObject.transform.SetPositionAndRotation(FacePoint(index, 0.03f), FaceRotation(index));
            _flashObject.transform.localScale = Vector3.one * (size * glowSizeFactor);

            Color warm = new Color(0.937f, 0.910f, 0.839f, 1f);   // MEASURED core #EFE8D6
            Color gold = new Color(1f, 0.80f, 0.42f, 1f);
            Tweener.Float(0f, 1f, Mathf.Max(0.0001f, glowDuration), t =>
            {
                // Quick lift, long tail - the reference's glow is already up when the stars appear and
                // then thins out under them rather than snapping off.
                float a = t < 0.18f ? t / 0.18f : Mathf.Pow(1f - (t - 0.18f) / 0.82f, 1.7f);
                Color c = Color.Lerp(warm, gold, t);
                c.a = Mathf.Clamp01(a) * glowStrength;
                SetFlashColor(c);
            }).SetUnscaled(true).OnComplete(() =>
            {
                SetFlashColor(new Color(0f, 0f, 0f, 0f));
                if (_flashObject != null) _flashObject.SetActive(false);
            });
        }

        void SetFlashColor(Color c)
        {
            if (_flashRenderer == null) return;
            _block.Clear();
            _block.SetColor(ColorId, c);
            _flashRenderer.SetPropertyBlock(_block);
        }

        void SetRingColor(Color c)
        {
            if (_ringRenderer == null) return;
            _block.Clear();
            _block.SetColor(ColorId, c);
            _ringRenderer.SetPropertyBlock(_block);
        }

        void SetCellWhiteness(Cell c, float w)
        {
            Paint(c.body, Color.Lerp(c.BaseBodyColor, Color.white, w));
            Paint(c.hole, Color.Lerp(c.BaseHoleColor, Color.white, w * 0.7f));
        }

        void Paint(Renderer r, Color color)
        {
            if (r == null) return;
            _block.Clear();
            _block.SetColor(BaseColorId, color);
            _block.SetColor(ColorId, color);
            r.SetPropertyBlock(_block);
        }

        static Color ReadColor(Renderer r, Color fallback)
        {
            if (r == null || r.sharedMaterial == null) return fallback;
            Material m = r.sharedMaterial;
            if (m.HasProperty(BaseColorId)) return m.GetColor(BaseColorId);
            if (m.HasProperty(ColorId)) return m.GetColor(ColorId);
            return fallback;
        }

        void EnsureFlash()
        {
            if (_flashObject != null) return;

            _quad = new Mesh { name = "Case1_FlashQuad" };
            _quad.vertices = new[]
            {
                new Vector3(-0.5f, 0f, -0.5f), new Vector3(0.5f, 0f, -0.5f),
                new Vector3(-0.5f, 0f,  0.5f), new Vector3(0.5f, 0f,  0.5f)
            };
            _quad.uv = new[] { new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0f, 1f), new Vector2(1f, 1f) };
            _quad.colors = new[] { Color.white, Color.white, Color.white, Color.white };
            _quad.triangles = new[] { 0, 2, 1, 2, 3, 1 };
            _quad.RecalculateNormals();
            _quad.RecalculateBounds();

            _flashObject = new GameObject("SlotFillFlash");
            _flashObject.transform.SetParent(transform, false);
            _flashObject.AddComponent<MeshFilter>().sharedMesh = _quad;

            _flashRenderer = _flashObject.AddComponent<MeshRenderer>();
            _flashRenderer.sharedMaterial = flashMaterial;
            _flashRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _flashRenderer.receiveShadows = false;
            _flashRenderer.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
            _flashObject.SetActive(false);
        }

        void EnsureRing()
        {
            if (_ringObject != null || ringMaterial == null) return;

            EnsureFlash();   // builds the shared quad mesh
            _ringObject = new GameObject("SlotShockRing");
            _ringObject.transform.SetParent(transform, false);
            _ringObject.AddComponent<MeshFilter>().sharedMesh = _quad;

            _ringRenderer = _ringObject.AddComponent<MeshRenderer>();
            _ringRenderer.sharedMaterial = ringMaterial;
            _ringRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _ringRenderer.receiveShadows = false;
            _ringRenderer.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
            _ringObject.SetActive(false);
        }

        void OnDestroy()
        {
            if (_quad != null) Destroy(_quad);
        }
    }
}
