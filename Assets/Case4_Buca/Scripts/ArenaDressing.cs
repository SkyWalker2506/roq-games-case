using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Case4
{
    /// <summary>
    /// The dark props that ring the arena, and their long shadows.
    /// <para>The reference frames the arena inside a crowd of near-black tapered figures standing
    /// behind it, a heavy angular boulder in the foreground and a figure clipped by the left edge -
    /// all throwing long shadows toward the camera. That surround is what gives the reference its
    /// sense of scale, depth and light direction. Ours had none of it: measured on
    /// .plan-build/verify/Buca/frame_00.png, the whole 1080x1728 frame held 2542 pixels below L=70
    /// and every one of them was the puck's own dark ring.</para>
    /// <para>Every prop is built here rather than authored into the scene so the layout is one
    /// readable table, and so it can be re-established after a mid-playmode domain reload the same
    /// way the rest of Case 4 now heals itself. Nothing here has a collider: the props cannot touch
    /// the puck even in principle, and they are all placed clear of the arena keep-out box besides.</para>
    /// </summary>
    public static class ArenaDressing
    {
        public const string RootName = "Case4_ArenaDressing";

        // The arena plus a margin. Every prop below is asserted to lie outside this box; the assert
        // is cheap and it is the only thing standing between a dressing tweak and a physics change.
        static readonly Vector2 KeepX = new Vector2(-38.5f, -23.7f);
        static readonly Vector2 KeepZ = new Vector2(-18.6f, 2.4f);

        // Tones are sRGB and land where the reference's do: its figure bodies measure RGB(28,40,61),
        // L=38.9, and its shadow cores sit at L 65-75. The prop shader is lit, so the three colours
        // below bracket its whole output range (L 31 to L 56) - whichever face catches the light, a
        // prop pixel is below the L=70 the band measurements count. The grade's +6 contrast only
        // pushes them further down.
        static readonly Color32 PropShadowFace = new Color32(24, 32, 46, 255);   // L 31.2
        static readonly Color32 PropBase       = new Color32(30, 42, 63, 255);   // L 40.9
        static readonly Color32 PropTop        = new Color32(44, 57, 80, 255);   // L 55.8
        static readonly Color32 PaleBand       = new Color32(222, 230, 240, 255);
        static readonly Color32 ShadowTone     = new Color32(52, 62, 78, 255);   // L 60.8

        const float ShadowY = 0.03f;   // clear of the floor plane; no other geometry is out here

        // Shadows run toward the camera and slightly right, the way the reference's do. The scene's
        // own sun throws shadows up-screen instead, so the props are marked ShadowCastingMode.Off:
        // one light direction on screen, not two contradicting each other.
        static readonly Vector3 ShadowDir  = new Vector3(0.321903f, 0f, -0.946773f);
        static readonly Vector3 ShadowPerp = new Vector3(-0.946773f, 0f, -0.321903f);

        // ---- layout ---------------------------------------------------------------------------
        // x, z, height, width, yaw. Placed so each prop is inside the camera's visible x window at
        // its own depth, and spread so the dark pixels are distributed across the frame rather than
        // stacked in one column.
        static readonly float[,] BackFigures =
        {
            { -38.70f,   6.55f, 4.1325f, 0.9520f,  -3.10f },
            { -33.60f,   6.10f, 4.6075f, 0.9520f, -16.80f },
            { -28.50f,   6.10f, 4.2275f, 0.9520f,   9.50f },
            { -23.40f,   6.55f, 4.4650f, 0.9520f,  -4.20f },
            { -37.30f,  11.40f, 3.7525f, 0.8925f,   2.10f },
            { -31.00f,  11.10f, 3.5625f, 0.8925f, -13.00f },
            { -24.70f,  11.40f, 3.8475f, 0.8925f,  11.90f },
        };
        const int FrontRowCount = 4;   // the first four stand nearer; their shadows reach further down-screen

        static readonly float[] ForeFigure = { -37.00f, -21.60f, 3.7000f, 1.1440f, -12.00f };
        // x, z, size, yaw, tallness
        static readonly float[] ForeBoulder = { -32.20f, -22.60f, 1.7640f, 52.60f, 1.15f };

        static Material _body, _pale, _shade;
        static Mesh _cube;
        static readonly List<Mesh> _owned = new List<Mesh>();

        /// <summary>
        /// Releases the generated materials and meshes and forgets them. <see cref="Ensure"/> already
        /// rebuilds on fake-null, so nothing here is load-bearing while the domain reload runs; with the
        /// reload disabled it is what stops each Play from stranding the previous Play's meshes in
        /// <see cref="_owned"/> and leaking them.
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            for (int i = 0; i < _owned.Count; i++) if (_owned[i] != null) Object.Destroy(_owned[i]);
            _owned.Clear();
            if (_body != null) Object.Destroy(_body);
            if (_pale != null) Object.Destroy(_pale);
            if (_shade != null) Object.Destroy(_shade);
            _body = _pale = _shade = null;
            _cube = null;
        }

        /// <summary>
        /// Builds the dressing if it is missing or has been hollowed out by a domain reload (which
        /// destroys the runtime materials and meshes while leaving the GameObjects behind).
        /// Returns true if it had to build.
        /// </summary>
        public static bool Ensure()
        {
            GameObject root = GameObject.Find(RootName);
            bool intact = root != null && root.transform.childCount > 0 &&
                          _body != null && _pale != null && _shade != null && _cube != null;
            if (intact) return false;

            if (root != null) Object.DestroyImmediate(root);
            Build();
            return true;
        }

        static void Build()
        {
            _owned.Clear();
            _cube = BuildCube();
            _body  = MakeMaterial("Case4/DarkGeometricProp", "Case4_PropDark", m =>
            {
                m.SetColor("_BaseColor", PropBase);
                m.SetColor("_TopHighlight", PropTop);
                m.SetColor("_ShadowColor", PropShadowFace);
                m.SetFloat("_Smoothness", 0.05f);
                m.SetFloat("_BevelLift", 0f);   // a lifted rim would erode the silhouette the bands count
            });
            _pale  = MakeUnlit("Case4_PropBand", PaleBand);
            _shade = MakeUnlit("Case4_PropShadow", ShadowTone);

            GameObject root = new GameObject(RootName);
            root.transform.position = Vector3.zero;
            Transform t = root.transform;

            int props = 0, shadows = 0;
            for (int i = 0; i < BackFigures.GetLength(0); i++)
            {
                float x = BackFigures[i, 0], z = BackFigures[i, 1];
                float h = BackFigures[i, 2], w = BackFigures[i, 3], yaw = BackFigures[i, 4];
                props += Figure(t, "Figure" + i, x, z, h, w, yaw);
                float len = i < FrontRowCount ? 3.05f : 2.684f;
                Shadow(t, "FigureShadow" + i, x, z, len, w * 1.05f, w * 1.05f * 0.45f); shadows++;
            }

            props += Figure(t, "ForeFigure", ForeFigure[0], ForeFigure[1], ForeFigure[2], ForeFigure[3], ForeFigure[4]);
            Shadow(t, "ForeFigureShadow", ForeFigure[0], ForeFigure[1], ForeFigure[2] * 0.9f,
                   ForeFigure[3] * 1.15f, ForeFigure[3] * 0.5f); shadows++;

            float bx = ForeBoulder[0], bz = ForeBoulder[1], bs = ForeBoulder[2];
            float byaw = ForeBoulder[3], btall = ForeBoulder[4];
            props += Boulder(t, "ForeBoulder", bx, bz, bs, byaw, btall);
            Shadow(t, "ForeBoulderShadow", bx, bz, bs * 1.5f, bs * 1.0f, bs * 0.55f); shadows++;

            Debug.Log(string.Format("[Case4] DRESSING built {0} prop pieces + {1} shadows under '{2}'; " +
                                    "colliders=0, shadowCasting=Off", props, shadows, RootName));
        }

        // ---- prop assembly --------------------------------------------------------------------

        static int Figure(Transform parent, string name, float x, float z, float h, float w, float yaw)
        {
            AssertClear(name, x, z, Mathf.Max(w * 1.28f, 1.0f) * 0.5f);
            Box(parent, name + "_foot", new Vector3(x, 0f,          z), new Vector3(w * 1.28f, h * 0.150f, w * 1.18f), yaw, _body);
            Box(parent, name + "_body", new Vector3(x, h * 0.150f,  z), new Vector3(w * 0.76f, h * 0.620f, w * 0.70f), yaw, _body);
            Box(parent, name + "_band", new Vector3(x, h * 0.710f,  z), new Vector3(w * 0.86f, h * 0.085f, w * 0.80f), yaw, _pale);
            Box(parent, name + "_head", new Vector3(x, h * 0.780f,  z), new Vector3(w * 1.00f, h * 0.220f, w * 0.94f), yaw, _body);
            return 4;
        }

        static int Boulder(Transform parent, string name, float x, float z, float s, float yaw, float tall)
        {
            AssertClear(name, x, z, s * 0.8f);
            Box(parent, name + "_lower", new Vector3(x, 0f, z),
                new Vector3(s, s * 0.58f * tall, s * 0.86f), yaw, _body);
            Box(parent, name + "_upper", new Vector3(x + s * 0.20f, s * 0.52f * tall, z - s * 0.08f),
                new Vector3(s * 0.64f, s * 0.44f * tall, s * 0.56f), yaw + 24f, _body);
            return 2;
        }

        /// <summary>Places a box whose BASE sits at <paramref name="basePoint"/>, matching the layout table.</summary>
        static void Box(Transform parent, string name, Vector3 basePoint, Vector3 size, float yaw, Material mat)
        {
            GameObject go = NewPiece(parent, name, mat);
            go.transform.SetPositionAndRotation(basePoint + Vector3.up * (size.y * 0.5f),
                                                Quaternion.Euler(0f, -yaw, 0f));
            go.transform.localScale = size;
            go.GetComponent<MeshFilter>().sharedMesh = _cube;
        }

        /// <summary>A ground quad that tapers away from the prop, the way a real cast shadow does.</summary>
        static void Shadow(Transform parent, string name, float x, float z, float length, float w0, float w1)
        {
            GameObject go = NewPiece(parent, name, _shade);
            go.transform.SetPositionAndRotation(new Vector3(x, ShadowY, z), Quaternion.identity);
            go.transform.localScale = Vector3.one;

            Vector3 tip = ShadowDir * length;
            Vector3[] v =
            {
                -ShadowPerp * (w0 * 0.5f),
                 ShadowPerp * (w0 * 0.5f),
                 tip + ShadowPerp * (w1 * 0.5f),
                 tip - ShadowPerp * (w1 * 0.5f),
            };
            Mesh m = new Mesh { name = "Case4_Shadow_" + name };
            m.vertices = v;
            m.normals = new[] { Vector3.up, Vector3.up, Vector3.up, Vector3.up };
            // Both windings: the quad is drawn from above, and a flipped normal here would make the
            // shadow silently invisible in a capture nobody can re-run cheaply.
            m.triangles = new[] { 0, 1, 2, 0, 2, 3, 0, 2, 1, 0, 3, 2 };
            m.RecalculateBounds();
            _owned.Add(m);
            go.GetComponent<MeshFilter>().sharedMesh = m;
        }

        static GameObject NewPiece(Transform parent, string name, Material mat)
        {
            GameObject go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>();
            MeshRenderer r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = ShadowCastingMode.Off;   // the fake shadows are the only ones on screen
            r.receiveShadows = false;
            r.lightProbeUsage = LightProbeUsage.Off;
            r.reflectionProbeUsage = ReflectionProbeUsage.Off;
            r.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
            return go;
        }

        static void AssertClear(string name, float x, float z, float radius)
        {
            bool clear = (x + radius < KeepX.x) || (x - radius > KeepX.y) ||
                         (z - radius > KeepZ.y) || (z + radius < KeepZ.x);
            if (!clear)
                Debug.LogError(string.Format(
                    "[Case4] DRESSING_IN_PLAY_AREA {0} at ({1:0.00},{2:0.00}) r={3:0.00} overlaps the arena keep-out box",
                    name, x, z, radius));
        }

        // ---- resources ------------------------------------------------------------------------

        static Material MakeUnlit(string name, Color32 colour)
        {
            return MakeMaterial("Universal Render Pipeline/Unlit", name, m =>
            {
                m.SetColor("_BaseColor", colour);
                if (m.HasProperty("_Color")) m.SetColor("_Color", colour);
                if (m.HasProperty("_Cull")) m.SetFloat("_Cull", 0f);   // ground quads are read from one side only
            });
        }

        static Material MakeMaterial(string shaderName, string name, System.Action<Material> setup)
        {
            Shader s = Shader.Find(shaderName);
            if (s == null) s = Shader.Find("Universal Render Pipeline/Unlit");
            if (s == null) s = Shader.Find("Unlit/Color");
            Material m = new Material(s) { name = name };
            if (setup != null) setup(m);
            return m;
        }

        static Mesh BuildCube()
        {
            Mesh m = new Mesh { name = "Case4_PropCube" };
            Vector3[] v = new Vector3[24];
            Vector3[] n = new Vector3[24];
            Vector3[] dir = { Vector3.up, Vector3.down, Vector3.forward, Vector3.back, Vector3.right, Vector3.left };
            int[] tri = new int[36];
            for (int f = 0; f < 6; f++)
            {
                Vector3 d = dir[f];
                Vector3 a = new Vector3(d.y, d.z, d.x);       // any vector not parallel to d
                Vector3 u = Vector3.Normalize(Vector3.Cross(d, a));
                Vector3 w = Vector3.Cross(d, u);
                int b = f * 4;
                v[b + 0] = (d - u - w) * 0.5f; v[b + 1] = (d + u - w) * 0.5f;
                v[b + 2] = (d + u + w) * 0.5f; v[b + 3] = (d - u + w) * 0.5f;
                for (int k = 0; k < 4; k++) n[b + k] = d;
                int t = f * 6;
                tri[t + 0] = b; tri[t + 1] = b + 1; tri[t + 2] = b + 2;
                tri[t + 3] = b; tri[t + 4] = b + 2; tri[t + 5] = b + 3;
            }
            m.vertices = v; m.normals = n; m.triangles = tri;
            m.RecalculateBounds();
            _owned.Add(m);
            return m;
        }
    }
}
