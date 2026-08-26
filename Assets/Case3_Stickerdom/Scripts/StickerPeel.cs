using UnityEngine;

namespace Case3
{
    /// <summary>Where <see cref="StickerPeel"/> gets the direction its fold line travels in.</summary>
    public enum PeelDirectionSource
    {
        /// <summary>From the peel origin the caller supplies - the tap point. What the reference does.</summary>
        TapPoint = 0,
        /// <summary>Always <see cref="StickerPeel.curlDirection"/>. The old fixed behaviour.</summary>
        Authored = 1,
    }

    /// <summary>
    /// Drives the page-curl peel of one sticker.
    ///
    /// A sticker in the scene is a <see cref="SpriteRenderer"/>, i.e. four vertices, and a vertex-bend
    /// curl on four vertices is either a hard crease or nothing at all. So when the peel starts the
    /// sticker is swapped onto a tessellated grid mesh built by <see cref="StickerMeshBuilder"/> with the
    /// same texture, the same world size and the same sorting, the sprite renderer is switched off, and
    /// <c>Case3/StickerCurl</c> bends that mesh. <see cref="ResetInstant"/> puts the sprite back, so a
    /// replay starts from exactly the state the scene shipped in.
    ///
    /// All shader values go through a <see cref="MaterialPropertyBlock"/>; the material asset is shared
    /// and never cloned.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class StickerPeel : MonoBehaviour
    {
        [Header("Target")]
        [Tooltip("The sticker that peels. Filled in by Case3SceneSetup.")]
        public SpriteRenderer sticker;

        /// <summary>
        /// Renderers that draw PART OF THE SAME PIECE OF PAPER as <see cref="sticker"/> and must
        /// disappear with it the moment the curl mesh takes over.
        ///
        /// The page items are drawn as three stacked sprites - the art, a white die-cut rim under it,
        /// and a soft drop shadow under that. Only the art becomes the curl mesh. Left alone, the rim
        /// stays lying on the page as a white silhouette of a sticker that has already flown away.
        /// </summary>
        public SpriteRenderer[] companions;

        [Tooltip("Shared Case3/StickerCurl material. Per-sticker values are pushed with a property block.")]
        public Material curlMaterial;

        [Header("Mesh")]
        [Tooltip("Grid resolution of the runtime mesh. 16 is the minimum for a curl that reads as round.")]
        public int segments = 30;

        [Tooltip("Sorting order added on top of the sticker's own, so the peeled sheet clears the page.")]
        public int sortingOrderBoost = 10;

        [Header("Curl direction")]
        [Tooltip("Where the curl direction comes from.\n\n" +
                 "TapPoint (default, and what the reference does): the sheet lifts at the finger and the " +
                 "fold travels away from it, so the direction is a continuous angle set by the player.\n\n" +
                 "Authored: always use curlDirection, whatever the tap was. Reproduces the old fixed peel.")]
        public PeelDirectionSource directionSource = PeelDirectionSource.TapPoint;

        [Tooltip("Direction the fold line travels in; the sticker lifts from the edge it points at. " +
                 "Used verbatim when directionSource is Authored, and as the starting value otherwise. " +
                 "\n\n" +
                 "There is nothing special about an axis here. At maxAngle = pi the peeled flap is a MIRROR " +
                 "of the sheet about the fold line, so an off-axis fold turns the flying silhouette; the " +
                 "reference does exactly that (its cat peels at +20 deg and flies visibly turned). What " +
                 "actually bit the old (1, 0.45) fold was the wrap angle, not the tilt: at 0.92*pi the " +
                 "'mirror' is a shear and the flap slides outside the sprite. Measured on all three " +
                 "stickers, a full 360 deg direction sweep at maxAngle = pi keeps the curl AABB inside " +
                 "Case3SilhouetteGate.MaxRatio (1.35) on both axes - see the sweep logged in " +
                 ".plan-build/cli/dir-sweep.txt.")]
        public Vector2 curlDirection = new Vector2(0.07f, 1f);

        [Tooltip("How far off the sheet centre the peel origin has to be before it is allowed to set the " +
                 "direction, as a fraction of the sheet's half-diagonal. A finger landing on the middle of " +
                 "the sticker names no direction at all: the measurement on the reference's own cat peel, " +
                 "whose tap sits 30 px from the centre, is 14 deg off its fitted fold angle, against 8-9 " +
                 "deg for the two taps that land well off centre. Below this the fallback angle is used.")]
        [Range(0f, 0.9f)] public float minOriginOffset = 0.18f;

        [Tooltip("Direction used when nothing has tapped this sticker - a prewarm pass, or a scripted demo " +
                 "peel. DEVIATION from the reference, which always has a finger: derived from the " +
                 "sticker's name so each sheet keeps one fixed character and a page full of stickers peels " +
                 "in visibly different directions instead of all sliding the same way.")]
        public bool fallbackFromName = true;

        [Tooltip("Cylinder radius as a fraction of the sticker's longest side. Larger = looser roll.")]
        // MEASURED against the reference clip, not chosen. Frame by frame over the peel, the white
        // area on the page roughly DOUBLES - 47,300 px before the lift, 93,100 px at the peak - because
        // the sheet folds over and shows its full-size white back. Ours went 36,400 -> 37,000: no
        // increase at all, because at radiusFactor 0.105 the flap is rolled into a tube about a tenth
        // of the sheet's width and there is nothing of it to see.
        //
        // maxAngle was never the problem - it is already PI, so the flap does turn a full 180 and IS a
        // mirror of the art. The radius is what decides whether that turn reads as a page folding over
        // or as paper rolled round a pencil.
        //
        // NonSerialized: Stickerdom.unity carries 0.105 on most stickers (and 0.34 on two, which are
        // the ones that already looked closest), and a serialized field is read from the SCENE. Fifth
        // time today; the scene is the owner's and not ours to write.
        [System.NonSerialized] public float radiusFactor = 0.105f;

        [Tooltip("Wrap is clamped here. EXACTLY pi and nothing else: at pi cos(theta) = -1, so the flap " +
                 "past the roll is a rigid 1:1 mirror of the sheet and the peeled paper keeps the " +
                 "sticker's own silhouette. At 0.92*pi the mirror is a shear (cos = -0.97) and the flap " +
                 "slides several units outside the sprite footprint - that was the giant white cloth.")]
        public float maxAngle = Mathf.PI;

        [Tooltip("How far the fold line ripples off straight, as a fraction of the curl radius.")]
        public float waveFactor = 0.055f;

        [Tooltip("Ripples along the fold line across the width of the sticker.")]
        public float waveCycles = 1.0f;

        [Tooltip("How far the ripple travels over a full peel, in radians.")]
        public float waveTravel = 4.2f;

        [Header("Curl stability")]
        [Tooltip("0 keeps the sticker anchor fixed; 1 fully recentres the curl. Partial compensation preserves the sticker silhouette without making the entire graphic slide like a loose cloth.")]
        [Range(0f, 1f)] public float centroidCompensation = 0.34f;

        [Header("Shading")]
        [Tooltip("Grey shadow the curl casts on the flat sheet, as a fraction of the curl radius.")]
        public float shadowWidthFactor = 0.55f;

        [Range(0f, 1f)] public float shadowStrength = 0.42f;

        [Tooltip("Darkest the rounded part of the roll gets; 1 would be flat, unshaded paper.")]
        [Range(0f, 1f)] public float shadeFloor = 0.74f;

        [Tooltip("Extra darkening on the inside of the roll where the paper tucks under itself.")]
        [Range(0f, 1f)] public float backAO = 0.30f;

        [Tooltip("Colour of the sticker's blank back face.")]
        // MEASURED, not guessed. This colour is pushed straight into _BackColor through the
        // MaterialPropertyBlock, and on the flat part of the peeled flap the shader multiplies it by
        // shade = ao = 1 - so it reaches the framebuffer unchanged and forms a single flat plateau.
        // The old (0.86, 0.84, 0.80) is perceptual L* 85.84, and the captured flight frames show the
        // paper back as a plateau at L* 85.8 across every frame: an exact match, which is the proof
        // that this field - not the shader's shading terms and not the scene grade - is what makes the
        // paper read grey. It also sits just UNDER the L* 88 line the reference clears, which is why
        // only 3-5% of our neutral pixels were above 88 against the reference's 58-76%.
        // (0.918, 0.898, 0.855) is L* 91.05: same warm paper hue, lifted clear of the 88 line while
        // staying below plain white (the material asset's own 0.97/0.96/0.93 would be L* 96.5, above
        // the reference's 79.9-88.5 band).
        public Color backColor = new Color(0.918f, 0.898f, 0.855f, 1f);

        // ------------------------------------------------------------------ runtime state

        static readonly int IdMainTex = Shader.PropertyToID("_MainTex");
        static readonly int IdColor = Shader.PropertyToID("_Color");
        static readonly int IdBackColor = Shader.PropertyToID("_BackColor");
        static readonly int IdCurlDir = Shader.PropertyToID("_CurlDir");
        static readonly int IdFoldPos = Shader.PropertyToID("_FoldPos");
        static readonly int IdCurlRadius = Shader.PropertyToID("_CurlRadius");
        static readonly int IdMaxAngle = Shader.PropertyToID("_MaxAngle");
        static readonly int IdWaveAmp = Shader.PropertyToID("_WaveAmp");
        static readonly int IdWaveFreq = Shader.PropertyToID("_WaveFreq");
        static readonly int IdWavePhase = Shader.PropertyToID("_WavePhase");
        static readonly int IdShadowWidth = Shader.PropertyToID("_ShadowWidth");
        static readonly int IdShadowStrength = Shader.PropertyToID("_ShadowStrength");
        static readonly int IdShadeFloor = Shader.PropertyToID("_ShadeFloor");
        static readonly int IdBackAO = Shader.PropertyToID("_BackAO");
        static readonly int IdAlpha = Shader.PropertyToID("_Alpha");

        GameObject _meshGo;
        Transform _meshTf;
        MeshFilter _filter;
        MeshRenderer _renderer;
        Mesh _mesh;
        MaterialPropertyBlock _mpb;

        Vector2 _localMin, _localMax;
        float _projMin, _projMax, _acrossSpan;
        float _radius, _shadowWidth, _waveAmp, _waveFreq, _lead, _trail;
        Vector2 _dir = Vector2.up;

        Vector2 _originLocal;
        bool _hasOrigin;

        float _progress;
        float _alpha = 1f;
        bool _built;
        bool _meshMode;

        SpriteRenderer _paperShadow;
        bool _shadowResolved;
        bool _placed;
        Color _shadowHomeColor = Color.white;

        /// <summary>Current peel amount, 0 = flat sticker, 1 = fully lifted and turned over.</summary>
        public float Progress { get { return _progress; } }

        /// <summary>True while the sticker is drawn as a curl mesh instead of a sprite.</summary>
        public bool MeshMode { get { return _meshMode; } }

        /// <summary>Cylinder radius in the sticker's local units; useful for sizing the dust puff.</summary>
        public float CurlRadius { get { return _radius; } }

        /// <summary>
        /// The unit direction the fold line is actually travelling in, after the peel origin and the
        /// fallback have had their say. This, not <see cref="curlDirection"/>, is what reaches
        /// <c>_CurlDir</c> in the shader.
        /// </summary>
        public Vector2 EffectiveCurlDirection { get { return _dir; } }

        /// <summary>True while a peel origin (a tap) has been supplied and not yet cleared.</summary>
        public bool HasPeelOrigin { get { return _hasOrigin; } }

        // ------------------------------------------------------------------ direction

        /// <summary>
        /// Tells the sheet where the finger landed, in world space. The sticker lifts AT that point and
        /// the fold travels away from it, which is the rule the reference footage follows: in all three
        /// of its peels the tap indicator sits at the far +dir end of the sweep (at the 93rd, 106th and
        /// 138th percentile of the flap's own along-fold extent, where 100% is the edge the peel starts
        /// from), and the fitted fold angles - +20, -28 and -86 deg on screen - agree with
        /// normalize(tap - centre) to within 14, 9 and 8 deg.
        ///
        /// Safe before or after <see cref="Prepare"/>; the direction-dependent constants are recomputed
        /// either way. Cleared by <see cref="ResetInstant"/> so a replay re-derives from the next tap.
        /// </summary>
        public void SetPeelOrigin(Vector3 worldPoint)
        {
            Transform t = sticker != null ? sticker.transform : transform;
            Vector3 local = t.InverseTransformPoint(worldPoint);
            _originLocal = new Vector2(local.x, local.y);
            _hasOrigin = true;
            if (_built)
            {
                ApplyDirection(ResolveDirection());
                SetProgress(_progress);
            }
        }

        /// <summary>
        /// Points the curl mesh at whatever sorting the sticker is wearing right now, plus its boost.
        /// Cheap enough to call on every state change; the alternative is a value that is correct only
        /// for the frame it was baked on.
        /// </summary>
        public void SyncMeshSorting()
        {
            if (sticker == null) return;
            if (_renderer != null)
            {
                _renderer.sortingLayerID = sticker.sortingLayerID;
                _renderer.sortingOrder = sticker.sortingOrder + sortingOrderBoost;
            }

            // The companions have to move with the sheet too. The die-cut rim is authored just under
            // the sticker in the PAGE band (around 140); when the director lifts the sheet over the
            // album at 600+, a rim left at its page number is drawn UNDER the card and the landed
            // sticker loses its white border - which is exactly what the capture showed. Same class of
            // bug as the curl mesh keeping the order it was built with: a value that was only correct
            // for the moment it was written.
            //
            // Rim sits one below the sheet so it reads as a border rather than covering the art;
            // everything else (the paper contact shadow) goes one below that.
            if (companions == null) return;
            for (int i = 0; i < companions.Length; i++)
            {
                SpriteRenderer c = companions[i];
                if (c == null) continue;
                c.sortingLayerID = sticker.sortingLayerID;
                c.sortingOrder = sticker.sortingOrder - (c.name == RimChildName ? 1 : 2);
            }
        }

        /// <summary>Forgets the tap, so the next peel falls back to the per-sticker angle.</summary>
        public void ClearPeelOrigin()
        {
            if (!_hasOrigin) return;
            _hasOrigin = false;
            if (_built)
            {
                ApplyDirection(ResolveDirection());
                SetProgress(_progress);
            }
        }

        /// <summary>
        /// Picks the fold direction. A continuous angle in every branch - there are no four cases here,
        /// so diagonals are ordinary values and not a special path.
        /// </summary>
        /// <summary>
        /// Which of the sheet's four corners the finger is closest to, in local space.
        ///
        /// The hinge of a peel is a corner, not a point on a line through the middle. Taking the
        /// nearest one means the pivot is stable under a shaky tap - move the finger a few pixels and
        /// the corner does not change - and it can never degenerate to the centre.
        /// </summary>
        Vector2 NearestCornerLocal(Vector2 p)
        {
            Vector2 best = _localMin;
            float bestD = float.MaxValue;
            for (int i = 0; i < 4; i++)
            {
                Vector2 c = new Vector2((i & 1) == 0 ? _localMin.x : _localMax.x,
                                        (i & 2) == 0 ? _localMin.y : _localMax.y);
                float d = (p - c).sqrMagnitude;
                if (d < bestD) { bestD = d; best = c; }
            }
            return best;
        }

        [Header("Manual preview")]
        [Tooltip("Drag this in the Inspector to scrub the peel by hand. 0 = flat, front face showing. " +
                 "1 = fully curled from the pivot corner. Editor-only; the sequence overwrites it at runtime.")]
        [Range(0f, 1f)] public float manualPeel;

        /// <summary>
        /// Applies <see cref="manualPeel"/> the moment it is dragged in the Inspector, so the curl can
        /// be dialled in by eye without entering Play mode. Does nothing while the game is running -
        /// the sequence owns the progress then.
        /// </summary>
        void OnValidate()
        {
            if (Application.isPlaying) return;
            if (sticker == null || sticker.sprite == null) return;
            Prepare();
            if (!_built) return;
            SetMeshMode(manualPeel > 0.0001f);
            SetProgress(manualPeel);
        }

        Vector2 ResolveDirection()
        {
            Vector2 authored = curlDirection.sqrMagnitude < 0.0001f ? Vector2.up : curlDirection.normalized;
            if (directionSource == PeelDirectionSource.Authored) return authored;

            if (_hasOrigin)
            {
                // THE PIVOT IS A CORNER. Always one of the four, never the middle and never a point
                // that depends on how far off centre the finger landed.
                //
                // The old rule took the direction straight from (tap - centre), so a tap near the
                // middle produced a near-zero vector whose angle was decided by a few pixels of noise,
                // and `minOriginOffset` existed only to suppress that. Snapping to the nearest corner
                // removes the failure instead of thresholding it: every tap names a corner, the corner
                // is the hinge, and the fold runs from it across the sheet to the far corner.
                Vector2 corner = NearestCornerLocal(_originLocal);
                Vector2 away = ((_localMin + _localMax) * 0.5f) - corner;   // corner -> centre
                if (away.sqrMagnitude > 1e-8f) return away.normalized;
            }

            return fallbackFromName ? FallbackDirection() : authored;
        }

        /// <summary>
        /// The angle a sticker peels at when nothing tapped it. DEVIATION from the reference, which
        /// always has a finger; recorded rather than fitted. Derived from the sticker's name so it is
        /// stable across runs and across replays, and so a page of stickers peels every which way
        /// instead of all sliding the same direction.
        /// </summary>
        Vector2 FallbackDirection()
        {
            string key = sticker != null ? sticker.name : name;
            unchecked
            {
                uint h = 2166136261u;
                for (int i = 0; i < key.Length; i++) { h ^= key[i]; h *= 16777619u; }
                h ^= h >> 15; h *= 0x2545F491u; h ^= h >> 13;
                float angle = (h / 4294967296f) * 2f * Mathf.PI;
                return new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
            }
        }

        /// <summary>
        /// Recomputes every constant that depends on which way the fold travels. Called from
        /// <see cref="Prepare"/> and again whenever the peel origin changes, so a tap that arrives after
        /// the mesh was built still steers the curl.
        /// </summary>
        /// <summary>The hinge corner, in the sheet's local space. Fixed for the whole peel.</summary>
        Vector2 _pivotLocal;

        /// <summary>
        /// Where the hinge corner is actually DRAWN, in world space.
        ///
        /// Not the same as the corner's transform position: StickerPeel offsets the curl mesh by its
        /// centroid compensation as the roll grows, so a corner that is mathematically fixed still
        /// slides across the screen. This is the point the director pins to keep the pivot still -
        /// the owner: "o pin noktasi konum degismemeli, sadece ust kisim kivrilmali".
        /// </summary>
        public Vector3 PivotWorld()
        {
            Transform basis = _meshTf != null ? _meshTf : (sticker != null ? sticker.transform : transform);
            return basis.TransformPoint(new Vector3(_pivotLocal.x, _pivotLocal.y, 0f));
        }

        void ApplyDirection(Vector2 dir)
        {
            _dir = dir;
            // The hinge is the corner the fold runs AWAY from, i.e. the one at the minimum projection.
            _pivotLocal = new Vector2(
                Vector2.Dot(new Vector2(_localMin.x, _localMin.y), dir) <= Vector2.Dot(new Vector2(_localMax.x, _localMin.y), dir) ? _localMin.x : _localMax.x,
                Vector2.Dot(new Vector2(_localMin.x, _localMin.y), dir) <= Vector2.Dot(new Vector2(_localMin.x, _localMax.y), dir) ? _localMin.y : _localMax.y);
            StickerMeshBuilder.ProjectionRange(_localMin, _localMax, _dir, out _projMin, out _projMax);

            Vector2 perp = new Vector2(-_dir.y, _dir.x);
            float acrossMin, acrossMax;
            StickerMeshBuilder.ProjectionRange(_localMin, _localMax, perp, out acrossMin, out acrossMax);
            _acrossSpan = Mathf.Max(0.01f, acrossMax - acrossMin);
            _waveFreq = waveCycles * 2f * Mathf.PI / _acrossSpan;

            // Margins so that at progress 0 nothing (not even the cast shadow) has entered the sheet,
            // and at progress 1 every vertex has cleared the arc and lies flat again, mirrored.
            _lead = _shadowWidth + _waveAmp + _radius * 0.35f;
            _trail = Mathf.PI * _radius + _waveAmp + (_projMax - _projMin) * 0.03f;
        }

        // ------------------------------------------------------------------ setup

        /// <summary>
        /// Builds the grid mesh and works out the curl constants for the bound sticker. Safe to call
        /// repeatedly; it only does the work once per sprite.
        /// </summary>
        public void Prepare()
        {
            // Same stale-flag guard as SetMeshMode: if the mesh this was built around is gone, the
            // build has to happen again, whatever the flag says.
            if (_built && (_renderer == null || _meshGo == null || _mesh == null)) _built = false;
            if (_built || sticker == null || sticker.sprite == null) return;

            _mesh = StickerMeshBuilder.Build(sticker.sprite, segments, out _localMin, out _localMax);
            if (_mesh == null) return;

            Vector2 size = _localMax - _localMin;
            _radius = Mathf.Max(0.02f, radiusFactor * Mathf.Max(size.x, size.y));
            _shadowWidth = shadowWidthFactor * _radius;
            _waveAmp = waveFactor * _radius;

            // Everything that depends on WHICH WAY the fold travels lives in one place, because it is no
            // longer decided once at build time - a tap can change it after the mesh exists.
            ApplyDirection(ResolveDirection());

            _meshGo = new GameObject("CurlMesh");
            _meshGo.hideFlags = HideFlags.DontSave;
            _meshTf = _meshGo.transform;
            _meshTf.SetParent(sticker.transform, false);
            _meshTf.localPosition = Vector3.zero;
            _meshTf.localRotation = Quaternion.identity;
            _meshTf.localScale = Vector3.one;

            _filter = _meshGo.AddComponent<MeshFilter>();
            _filter.sharedMesh = _mesh;

            _renderer = _meshGo.AddComponent<MeshRenderer>();
            _renderer.sharedMaterial = curlMaterial;
            _renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _renderer.receiveShadows = false;
            _renderer.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
            _renderer.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
            _renderer.sortingLayerID = sticker.sortingLayerID;
            _renderer.sortingOrder = sticker.sortingOrder + sortingOrderBoost;
            _renderer.enabled = false;

            _mpb = new MaterialPropertyBlock();
            _built = true;

            Debug.Log(string.Format(
                "[Case3Peel] mesh built for {0}: {1}x{1} grid ({2} verts), local size {3:0.00}x{4:0.00}, " +
                "curl radius {5:0.000} (dir {6}), fold sweep {7:0.00} -> {8:0.00}",
                sticker.name, segments, (segments + 1) * (segments + 1), size.x, size.y,
                _radius, _dir, _projMax + _lead, _projMin - _trail));

            SetProgress(0f);
        }

        // ------------------------------------------------------------------ driving

        /// <summary>Swaps between the sprite renderer and the curl mesh.</summary>
        public void SetMeshMode(bool on)
        {
            if (sticker == null) return;
            // A Unity object that has been destroyed compares equal to null while the C# reference is
            // still non-null, so `_built` can outlive the renderer it describes - a domain reload, a
            // play-mode entry, or an editor-time Prepare() whose GameObject was cleaned up afterwards
            // all produce that state. Writing to it throws MissingReferenceException. Treat it as
            // not-built and rebuild, rather than trusting a flag over the object it refers to.
            if (_built && (_renderer == null || _meshGo == null)) _built = false;
            if (on) Prepare();
            if (!_built || _renderer == null) return;

            _meshMode = on;
            _renderer.enabled = on;
            // Re-read the sticker's order EVERY time the mesh is shown, not once in Prepare(). Prepare
            // runs at prewarm, when the sheet still carries its page order (140 / 502 / 505); the
            // director then lifts the sticker above the album for the flight, but the curl mesh - which
            // is the thing actually on screen while it flies - kept the low number it was built with and
            // slid UNDER the objects it passed over.
            SyncMeshSorting();
            sticker.enabled = !on;
            if (companions != null)
                for (int i = 0; i < companions.Length; i++)
                    if (companions[i] != null) companions[i].enabled = !on;
            ApplyPaperShadow();
        }

        /// <summary>
        /// Sets the peel amount. 0 leaves the sheet flat and pixel-identical to the sprite; 1 has the
        /// fold line past the far edge, so the whole sticker lies turned over with its white back out.
        /// </summary>
        public void SetProgress(float progress01)
        {
            if (_built && (_renderer == null || _meshGo == null)) _built = false;
            if (!_built) return;

            _progress = Mathf.Clamp01(progress01);

            // The ripple travels with the crease rather than with wall-clock time, so a replay is
            // frame-for-frame identical to the first run.
            StickerMeshBuilder.CurlParams cp = CurrentCurl();

            _mpb.SetTexture(IdMainTex, sticker.sprite.texture);
            _mpb.SetColor(IdColor, sticker.color);
            _mpb.SetColor(IdBackColor, backColor);
            _mpb.SetVector(IdCurlDir, new Vector4(cp.dir.x, cp.dir.y, 0f, 0f));
            _mpb.SetFloat(IdFoldPos, cp.fold);
            _mpb.SetFloat(IdCurlRadius, cp.radius);
            _mpb.SetFloat(IdMaxAngle, cp.maxAngle);
            _mpb.SetFloat(IdWaveAmp, cp.waveAmp);
            _mpb.SetFloat(IdWaveFreq, cp.waveFreq);
            _mpb.SetFloat(IdWavePhase, cp.wavePhase);
            _mpb.SetFloat(IdShadowWidth, _shadowWidth);
            _mpb.SetFloat(IdShadowStrength, shadowStrength * Mathf.Clamp01(_progress * 8f));
            _mpb.SetFloat(IdShadeFloor, shadeFloor);
            _mpb.SetFloat(IdBackAO, backAO);
            _mpb.SetFloat(IdAlpha, _alpha);
            _renderer.SetPropertyBlock(_mpb);

            // The peeled flap swings away from where it started, so re-centre the mesh on its own
            // centroid. Only x/y: the z lift towards the camera is part of the effect and stays.
            Vector3 centroid = StickerMeshBuilder.Centroid(_localMin, _localMax, cp);
            _meshTf.localPosition = new Vector3(-centroid.x * centroidCompensation, -centroid.y * centroidCompensation, 0f);

            ApplyPaperShadow();
        }

        /// <summary>Fades the whole sheet, front and back alike.</summary>
        public void SetAlpha(float alpha)
        {
            _alpha = Mathf.Clamp01(alpha);
            if (_built) SetProgress(_progress);
        }

        /// <summary>
        /// Shows or hides the rim/shadow sprites that belong to the same piece of paper. The peel
        /// itself drives these, but the attach step needs them off for good once the card art has
        /// taken over, and the reset needs them back on.
        /// </summary>
        /// <summary>
        /// Name of the child that carries the sticker's white die-cut border. It is a companion like
        /// the contact shadow, but it belongs to the STICKER, not to the page: it must survive the
        /// landing, while the shadow must not.
        /// </summary>
        public const string RimChildName = "Rim";

        /// <summary>
        /// Drops the dressing that only makes sense while the sheet is lying on the page - the paper
        /// contact shadow - and KEEPS the die cut, which is part of the sticker wherever it ends up.
        ///
        /// `SetCompanionsEnabled(false)` used to be called at attach, back when the landed sheet was
        /// hidden and replaced by baked card art. Now the sheet IS the card's subject, and switching
        /// every companion off took its white border with it: the sticker sat on the card with no die
        /// cut while the reference's card subject clearly has one.
        /// </summary>
        public void SetPageDressingEnabled(bool on)
        {
            // The contact shadow FIRST, and directly. It is not in `companions` - only the rim is -
            // it is found by prefix through PaperShadow(). So the loop below skipped the rim by
            // design and then had nothing left to touch, which meant this method did nothing at all
            // and every consumed sticker left its grey shadow lying on the page.
            SpriteRenderer shadow = PaperShadow();
            if (shadow != null)
            {
                _placed = !on;                  // placed == the paper has left the page for good
                _shadowSuppressed = !on;
                if (!on) { shadow.enabled = false; }
                else     { _shadowResolved = false; _paperShadow = null; ApplyPaperShadow(); }
            }

            if (companions == null) return;
            for (int i = 0; i < companions.Length; i++)
            {
                SpriteRenderer c = companions[i];
                if (c == null) continue;
                if (c.name == RimChildName) continue;   // the die cut travels with the sticker
                c.enabled = on;
            }
        }

        public void SetCompanionsEnabled(bool on)
        {
            if (companions == null) return;
            for (int i = 0; i < companions.Length; i++)
                if (companions[i] != null) companions[i].enabled = on;
        }

        /// <summary>Puts the sticker back to a flat sprite; the mesh object is kept for the next run.</summary>
        public void ResetInstant()
        {
            _shadowSuppressed = false;
            _alpha = 1f;
            _placed = false;
            _hasOrigin = false;
            if (_built) ApplyDirection(ResolveDirection());
            if (_built)
            {
                SetProgress(0f);
                _meshTf.localPosition = Vector3.zero;
            }
            SetMeshMode(false);
            ApplyPaperShadow();
        }

        /// <summary>
        /// The soft brown drop shadow lives as a child sprite so the sticker reads as paper lying on the
        /// page. It is a CONTACT shadow: it is only true while the sheet lies flat where it started.
        /// The moment the crease starts travelling the paper is off the page and the flat shadow becomes
        /// a lie, so it fades out over the first part of the curl; and once the sheet has been placed in
        /// its card slot it never comes back, because a placed sticker is printed onto the page.
        ///
        /// Driven from <see cref="SetProgress"/> rather than from mesh mode: mesh mode is switched on a
        /// whole idle phase before the corner lifts, and switching the shadow off there would strip the
        /// resting stickers of their shadow for the entire wait.
        /// </summary>
        /// <summary>
        /// Set once the sheet has been picked, cleared only by <see cref="ResetInstant"/>.
        ///
        /// Killing the shadow at one call site was not enough - five separate paths call
        /// ApplyPaperShadow, and any one of them running after the sheet had left the page brought
        /// the shadow back. The owner saw it return twice. A latch is the honest fix: once the paper
        /// is off the page there is no state in which a contact shadow is correct, so no caller
        /// should be able to argue otherwise.
        /// </summary>
        bool _shadowSuppressed;

        void ApplyPaperShadow()
        {
            SpriteRenderer sr = PaperShadow();
            if (sr == null) return;

            // The contact shadow is a separate SPRITE laid under the sheet, and the owner does not want
            // one: "golge sanirim bu karanlik olsun diye katman koyuyorsun o - oyle yapma, direk shader
            // ile karart". A quad cannot follow a curling sheet, so it reads as a grey blob sitting
            // beside the paper rather than as shading on it. The curl shader already darkens the fold
            // (_ShadowStrength, _ShadeFloor, _BackAO) and now does it much harder.
            //
            // Kept as a component rather than deleted so the scene's wiring stays valid; it simply
            // never draws.
            sr.enabled = false;
            return;
#pragma warning disable 0162
            if (_shadowSuppressed) { sr.enabled = false; return; }

            float k = _placed ? 1f : Mathf.Clamp01(_progress / ShadowFadeProgress);
            float a = _shadowHomeColor.a * (1f - k);

            sr.enabled = a > 0.001f;
            Color c = _shadowHomeColor;
            c.a = a;
            sr.color = c;
#pragma warning restore 0162
        }

        /// <summary>
        /// Where the sheet actually READS on screen, in world x/y.
        ///
        /// This is not <c>sticker.transform.position</c> and it must not be measured as if it were.
        /// Between the transform and the pixels sit TWO more position channels, and both of them move
        /// on their own clock:
        ///   1. <c>_meshTf.localPosition</c>, written by <see cref="SetProgress"/> from
        ///      <c>centroidCompensation</c>. It slides back to zero as the curl unwinds, which is a
        ///      real lateral move of the drawn sheet AFTER the flight has already ended.
        ///   2. the curl geometry itself, which rolls the paper to one side of its own anchor.
        /// A trace taken off the transform alone therefore reads clean over a landing the player sees
        /// drift. The AABB centre of the mesh, sampled through the same <see cref="StickerMeshBuilder.Curl"/>
        /// the shader runs, contains all three channels, so this is the only honest place to measure
        /// "did the sticker stop moving".
        ///
        /// Never builds: an unprepared sheet answers with its flat sprite bounds rather than
        /// allocating a mesh inside a measurement call.
        /// </summary>
        public Vector3 VisualWorldCentre(int samples = 9)
        {
            if (_meshMode && _built && _renderer != null && _meshTf != null)
            {
                Bounds cb = CurlWorldBounds(samples);
                return new Vector3(cb.center.x, cb.center.y, 0f);
            }
            if (sticker != null)
            {
                Bounds sb = sticker.bounds;
                return new Vector3(sb.center.x, sb.center.y, 0f);
            }
            Vector3 p = transform.position;
            return new Vector3(p.x, p.y, 0f);
        }

        /// <summary>
        /// Marks the sheet as placed: it has arrived at its slot and is about to unwind flat there. The
        /// contact shadow must not reappear as the curl flattens, so this is called once, on entering the
        /// flip, and is cleared only by <see cref="ResetInstant"/>.
        /// </summary>
        public void MarkPlaced()
        {
            _placed = true;
            _shadowSuppressed = true;
            ApplyPaperShadow();
        }

        /// <summary>
        /// The drop-shadow child, resolved once and cached. <see cref="Case3SceneSetup"/> names it
        /// "Shadow_" + the sticker's key ("Shadow_Cat"), so it is found by prefix, not by a fixed name:
        /// a fixed name is exactly what silently broke this - Find("PaperShadow") returned null for
        /// every sticker in the scene and the shadow was never hidden at all. If no such child exists the
        /// failure is now LOUD, once, instead of a silent early return.
        /// </summary>
        SpriteRenderer PaperShadow()
        {
            if (_shadowResolved) return _paperShadow;
            _shadowResolved = true;
            if (sticker == null) return null;

            Transform st = sticker.transform;
            for (int i = 0; i < st.childCount; i++)
            {
                Transform c = st.GetChild(i);
                if (!c.name.StartsWith(PaperShadowPrefix, System.StringComparison.Ordinal)) continue;
                SpriteRenderer sr = c.GetComponent<SpriteRenderer>();
                if (sr == null) continue;
                _paperShadow = sr;
                _shadowHomeColor = sr.color;
                break;
            }

            if (_paperShadow == null)
                Debug.LogWarning("[Case3Peel] " + (sticker != null ? sticker.name : name) + " has no '" +
                                 PaperShadowPrefix + "*' child SpriteRenderer: the paper contact shadow " +
                                 "cannot be hidden during the peel and will follow the sheet off the page.");
            return _paperShadow;
        }

        /// <summary>Name prefix of the drop-shadow child Case3SceneSetup creates under every sticker.</summary>
        public const string PaperShadowPrefix = "Shadow_";

        /// <summary>
        /// Peel amount by which the contact shadow has fully faded out.
        ///
        /// 0.06, not the 0.30 it used to be. The shadow is a child of the sticker's TRANSFORM, but
        /// what moves on screen during a peel is the curl MESH - the transform stays where the sheet
        /// was. So for the whole of the old 30% ramp the shadow sat still at the sheet's old place
        /// while the paper visibly lifted away from it, which is exactly what the owner reported:
        /// "hareket etse bile o iz ayni yerde kaliyor, golge gibi ama olmamasi lazim".
        ///
        /// Fixing the parenting instead would be wrong: a contact shadow belongs to the CONTACT, so
        /// it should die the moment the paper stops touching the page rather than travel with it.
        /// The shadow now goes in the first breath of the peel, before the gap is visible.
        /// </summary>
        const float ShadowFadeProgress = 0.06f;

        /// <summary>Alpha of the shadow while the sticker rests on the page, read from the scene once.</summary>
        public float PaperShadowAlpha { get { SpriteRenderer sr = PaperShadow(); return sr != null ? sr.color.a : 0f; } }

        // ------------------------------------------------------------------ measurement (silhouette gate)

        /// <summary>
        /// World-space AABB of the sticker while it is FLAT: the sprite rect through the sticker's own
        /// transform. This is the silhouette the curl is not allowed to grow far beyond.
        /// </summary>
        /// <summary>
        /// World point of the edge the curl is rolling AWAY from - the one that stays stuck.
        ///
        /// A sticker does not unroll by spinning about its own centre; one edge holds and the sheet
        /// opens out from it. Pinning the CENTRE during the unroll is exactly what made ours turn in
        /// place - the owner: "olacagi yerde ters donuyor gibi olmayacak, bir ucu sabit kalacak".
        /// Pin this instead and the fold behaves like paper.
        /// </summary>
        public Vector3 AnchoredEdgeWorld(int samples = 33)
        {
            Bounds b = CurlWorldBounds(samples);
            Vector2 d = ResolveDirection();
            Vector3 dir = new Vector3(d.x, d.y, 0f);
            if (dir.sqrMagnitude < 1e-6f) dir = Vector3.right; else dir.Normalize();
            // The fold travels along +dir, so the edge that stays put is the one at -dir.
            float half = Mathf.Abs(dir.x) * b.extents.x + Mathf.Abs(dir.y) * b.extents.y;
            return b.center - dir * half;
        }

        public Bounds FlatWorldBounds()
        {
            if (!_built) Prepare();
            Transform t = sticker != null ? sticker.transform : transform;

            Bounds b = new Bounds(t.TransformPoint(new Vector3(_localMin.x, _localMin.y, 0f)), Vector3.zero);
            b.Encapsulate(t.TransformPoint(new Vector3(_localMax.x, _localMin.y, 0f)));
            b.Encapsulate(t.TransformPoint(new Vector3(_localMin.x, _localMax.y, 0f)));
            b.Encapsulate(t.TransformPoint(new Vector3(_localMax.x, _localMax.y, 0f)));
            return b;
        }

        /// <summary>
        /// World-space AABB the curl mesh actually occupies at the current progress, sampled on a grid
        /// through the same maths the shader runs. Only x/y count: the z lift is towards an orthographic
        /// camera and does not change what the player sees.
        /// </summary>
        public Bounds CurlWorldBounds(int samples = 33)
        {
            if (!_built) Prepare();
            if (!_built) return FlatWorldBounds();

            StickerMeshBuilder.CurlParams cp = CurrentCurl();
            Transform t = _meshTf != null ? _meshTf : transform;

            samples = Mathf.Max(3, samples);
            bool first = true;
            Bounds b = new Bounds(Vector3.zero, Vector3.zero);

            for (int y = 0; y < samples; y++)
            {
                float fy = (float)y / (samples - 1);
                for (int x = 0; x < samples; x++)
                {
                    float fx = (float)x / (samples - 1);
                    Vector2 p = new Vector2(Mathf.Lerp(_localMin.x, _localMax.x, fx),
                                            Mathf.Lerp(_localMin.y, _localMax.y, fy));
                    Vector3 world = t.TransformPoint(StickerMeshBuilder.Curl(p, cp));
                    world.z = 0f;
                    if (first) { b = new Bounds(world, Vector3.zero); first = false; }
                    else b.Encapsulate(world);
                }
            }
            return b;
        }

        /// <summary>The curl parameters for the current progress; the single source both the shader and the gate read.</summary>
        StickerMeshBuilder.CurlParams CurrentCurl()
        {
            return new StickerMeshBuilder.CurlParams
            {
                dir = _dir,
                fold = Mathf.Lerp(_projMax + _lead, _projMin - _trail, _progress),
                radius = _radius,
                maxAngle = Mathf.Min(maxAngle, Mathf.PI),
                waveAmp = _waveAmp,
                waveFreq = _waveFreq,
                wavePhase = -_progress * waveTravel
            };
        }

        void OnDestroy()
        {
            if (_mesh != null) Destroy(_mesh);
            if (_meshGo != null) Destroy(_meshGo);
        }
    }
}
