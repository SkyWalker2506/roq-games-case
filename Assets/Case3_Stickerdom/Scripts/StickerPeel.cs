using UnityEngine;

namespace Case3
{
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

        [Tooltip("Shared Case3/StickerCurl material. Per-sticker values are pushed with a property block.")]
        public Material curlMaterial;

        [Header("Mesh")]
        [Tooltip("Grid resolution of the runtime mesh. 16 is the minimum for a curl that reads as round.")]
        public int segments = 30;

        [Tooltip("Sorting order added on top of the sticker's own, so the peeled sheet clears the page.")]
        public int sortingOrderBoost = 10;

        [Header("Curl shape")]
        [Tooltip("Direction the fold line travels in; the sticker lifts from the edge it points at. " +
                 "Kept within a few degrees of an axis on purpose: at maxAngle = pi the peeled flap is a " +
                 "MIRROR of the sheet about the fold line, and a mirror about a diagonal line rotates the " +
                 "silhouette by twice that angle. The old (1, 0.45) fold turned the flying sticker 48 deg " +
                 "off its own shape and blew its screen footprint up by ~1.6x. A 4 deg tilt keeps the " +
                 "peel from looking mechanical while the silhouette stays the sticker's own.")]
        public Vector2 curlDirection = new Vector2(0.07f, 1f);

        [Tooltip("Cylinder radius as a fraction of the sticker's longest side. Larger = looser roll.")]
        public float radiusFactor = 0.105f;

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

        // ------------------------------------------------------------------ setup

        /// <summary>
        /// Builds the grid mesh and works out the curl constants for the bound sticker. Safe to call
        /// repeatedly; it only does the work once per sprite.
        /// </summary>
        public void Prepare()
        {
            if (_built || sticker == null || sticker.sprite == null) return;

            _mesh = StickerMeshBuilder.Build(sticker.sprite, segments, out _localMin, out _localMax);
            if (_mesh == null) return;

            _dir = curlDirection.sqrMagnitude < 0.0001f ? Vector2.up : curlDirection.normalized;
            StickerMeshBuilder.ProjectionRange(_localMin, _localMax, _dir, out _projMin, out _projMax);

            Vector2 perp = new Vector2(-_dir.y, _dir.x);
            float acrossMin, acrossMax;
            StickerMeshBuilder.ProjectionRange(_localMin, _localMax, perp, out acrossMin, out acrossMax);
            _acrossSpan = Mathf.Max(0.01f, acrossMax - acrossMin);

            Vector2 size = _localMax - _localMin;
            _radius = Mathf.Max(0.02f, radiusFactor * Mathf.Max(size.x, size.y));
            _shadowWidth = shadowWidthFactor * _radius;
            _waveAmp = waveFactor * _radius;
            _waveFreq = waveCycles * 2f * Mathf.PI / _acrossSpan;

            // Margins so that at progress 0 nothing (not even the cast shadow) has entered the sheet,
            // and at progress 1 every vertex has cleared the arc and lies flat again, mirrored.
            _lead = _shadowWidth + _waveAmp + _radius * 0.35f;
            _trail = Mathf.PI * _radius + _waveAmp + (_projMax - _projMin) * 0.03f;

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
            if (on) Prepare();
            if (!_built) return;

            _meshMode = on;
            _renderer.enabled = on;
            sticker.enabled = !on;
            ApplyPaperShadow();
        }

        /// <summary>
        /// Sets the peel amount. 0 leaves the sheet flat and pixel-identical to the sprite; 1 has the
        /// fold line past the far edge, so the whole sticker lies turned over with its white back out.
        /// </summary>
        public void SetProgress(float progress01)
        {
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

        /// <summary>Puts the sticker back to a flat sprite; the mesh object is kept for the next run.</summary>
        public void ResetInstant()
        {
            _alpha = 1f;
            _placed = false;
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
        void ApplyPaperShadow()
        {
            SpriteRenderer sr = PaperShadow();
            if (sr == null) return;

            float k = _placed ? 1f : Mathf.Clamp01(_progress / ShadowFadeProgress);
            float a = _shadowHomeColor.a * (1f - k);

            sr.enabled = a > 0.001f;
            Color c = _shadowHomeColor;
            c.a = a;
            sr.color = c;
        }

        /// <summary>
        /// Marks the sheet as placed: it has arrived at its slot and is about to unwind flat there. The
        /// contact shadow must not reappear as the curl flattens, so this is called once, on entering the
        /// flip, and is cleared only by <see cref="ResetInstant"/>.
        /// </summary>
        public void MarkPlaced()
        {
            _placed = true;
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

        /// <summary>Peel amount by which the contact shadow has fully faded out.</summary>
        const float ShadowFadeProgress = 0.30f;

        /// <summary>Alpha of the shadow while the sticker rests on the page, read from the scene once.</summary>
        public float PaperShadowAlpha { get { SpriteRenderer sr = PaperShadow(); return sr != null ? sr.color.a : 0f; } }

        // ------------------------------------------------------------------ measurement (silhouette gate)

        /// <summary>
        /// World-space AABB of the sticker while it is FLAT: the sprite rect through the sticker's own
        /// transform. This is the silhouette the curl is not allowed to grow far beyond.
        /// </summary>
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
