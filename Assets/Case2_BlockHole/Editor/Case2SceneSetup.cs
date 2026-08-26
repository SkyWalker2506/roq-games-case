using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using Case2;
using Shared.Sequencing;

/// <summary>
/// Wires runtime Case 2 components onto the authored scene without moving its camera or presentation.
/// Idempotent: running it twice leaves the same scene. It adds the director, the drag controllers and
/// the shatter sink, puts a <see cref="HoleGlowHighlight"/> on every hole, creates the handful of
/// materials the effects need, and writes a discovery dump to the log.
/// </summary>
public static class Case2SceneSetup
{
    const string ScenePath = "Assets/Case2_BlockHole/Scenes/BlockHole.unity";
    const string MaterialDir = "Assets/Case2_BlockHole/Materials";
    const string FracturedDir = "Assets/Case2_BlockHole/Prefabs/Fractured/FractureMeshes-Game";
    const string FracturedPath = FracturedDir + "/Block-Single.prefab";
    const string DebrisPath = "Assets/Case2_BlockHole/VFX/DebrisBurst.prefab";
    const string RingPath = "Assets/Case2_BlockHole/VFX/ImpactRing.prefab";
    const string DustPath = "Assets/Case2_BlockHole/VFX/DustPuff.prefab";
    const string RootName = "Case2_Sequence";
    const BlockShapeId HeroShape = BlockShapeId.Cross;
    // static readonly, not const. Identical behaviour, but a const lets the compiler PROVE every
    // `if (!SceneIsAuthored)` body is dead, and it emitted 16 "unreachable code" warnings saying so.
    // Those bodies are deliberately parked - the scene owns that placement now - and burying 16
    // warnings in the console to say it is worse than the one comment that already explains it.
    static readonly bool SceneIsAuthored = true;

    /// <summary>Menu entry point.</summary>
    public static void BuildMenu()
    {
        Build();
    }

    /// <summary>Batchmode entry point: wires the scene and saves it.</summary>
    public static void Build()
    {
        Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

        Transform board = FindRoot(scene, "Board");
        if (board == null) { Fail("Board root not found in " + ScenePath); return; }

        Transform blocks = board.Find("Blocks");
        Transform holes = board.Find("Holes");
        if (blocks == null || holes == null) { Fail("Board/Blocks or Board/Holes not found"); return; }

        string identityProblem;
        if (!ValidateReferenceSet(blocks, "block", out identityProblem) ||
            !ValidateReferenceSet(holes, "hole", out identityProblem))
        {
            Fail(identityProblem);
            return;
        }

        // ---------------------------------------------------------------- materials
        Material outlineMat = EnsureMaterial(MaterialDir + "/Case2_BlockOutline.mat", "Case2/BlockOutline", m =>
        {
            m.SetColor("_OutlineColor", Color.white);
            m.SetFloat("_OutlineWidth", 0.022f);
        });

        // The neon layer is an additive plate in the block silhouette rather than an outline hull:
        // a hull around a hole mesh is a vertical wall, which is invisible from a near top-down camera.
        // Alpha blended, not additive. Additive over this board's light purple floor always resolves to
        // pale pink-white at the peak of the pulse whatever colour goes in, which is exactly the
        // "beyaz-sicak" reading the deviation list flags; blending paints the rim in the hole's own colour.
        Material neonMat = EnsureMaterial(MaterialDir + "/Case2_HoleNeon.mat", "Universal Render Pipeline/Unlit", m =>
        {
            MakeTransparent(m);
            m.SetColor("_BaseColor", Color.white);
        });

        Material rimMat = EnsureMaterial(MaterialDir + "/Case2_HoleLip.mat", "Universal Render Pipeline/Unlit", m =>
        {
            m.SetColor("_BaseColor", Color.clear);
        });

        Material pitMat = EnsureMaterial(MaterialDir + "/Case2_HoleDepth.mat", "Case2/HoleDepthGradient", m =>
        {
            m.SetColor("_LipColor", Color.white);
            m.SetFloat("_LipWidth", 0.22f);
            m.SetFloat("_BevelIntensity", 0.45f);
            m.SetFloat("_AOStrength", 0.35f);
        });

        // Broken pieces must keep the source block's colour and silhouette readability. The previous
        // transparent/frosted-glass treatment piled highlights on top of each other and turned the break
        // into a pale smoke/cloud mass, obscuring the actual fall into the hole.
        Material shardMat = EnsureMaterial(MaterialDir + "/Case2_CrystalShard.mat", "Case2/ToyChunk", m =>
        {
            m.SetColor("_BaseColor", Color.white);
            if (m.HasProperty("_EdgeLift")) m.SetFloat("_EdgeLift", 0.10f);
            if (m.HasProperty("_FaceContrast")) m.SetFloat("_FaceContrast", 0.14f);
        });

        Material dotMat = EnsureMaterial(MaterialDir + "/Case2_GrabDot.mat", "Universal Render Pipeline/Unlit", m =>
        {
            m.SetColor("_BaseColor", Color.white);
        });

        // The reference lives on a dark navy playfield. Retone only the board shell/tiles; blocks and
        // holes keep their own materials and remain the saturated focal elements.
        // Placement, camera and board presentation belong to the authored scene. The one-time
        // Case2SceneAuthoring pass owns those values; repeated wiring must never move or repaint them.

        // ---------------------------------------------------------------- holes & blocks grid model
        if (!SceneIsAuthored)
        {
            for (int i = 0; i < blocks.childCount; i++)
            {
                Transform b = blocks.GetChild(i);
                BlockShapeId id = BlockShapeIds.Parse(b.name);
                if (BlockGridPlacements.TryGetValue(id, out ShapeCellDefinition def))
                {
                    Vector3 center = def.ComputeCenter(0.03f);
                    b.localPosition = center;
                    if (id == BlockShapeId.Two)
                    {
                        b.localRotation = Quaternion.Euler(0f, 90f, 0f);
                        b.localScale = new Vector3(1.5f, 1.0f, 1.0f);
                    }
                    else
                    {
                        b.localRotation = Quaternion.identity;
                        b.localScale = Vector3.one;
                    }
                    EditorUtility.SetDirty(b);
                    Debug.Log(string.Format("[Case2Grid] Block {0} placed at cell-derived ({1:0.0}, {2:0.0})",
                        id, center.x, center.z));
                }
                UpgradeBlockSurface(b, id);
            }
        }

        List<HoleGlowHighlight> holeList = new List<HoleGlowHighlight>();
        for (int i = 0; i < holes.childCount; i++)
        {
            Transform h = holes.GetChild(i);
            BlockShapeId shapeId = BlockShapeIds.Parse(h.name);
            if (!SceneIsAuthored && HoleGridPlacements.TryGetValue(shapeId, out ShapeCellDefinition def))
            {
                Vector3 center = def.ComputeCenter(0.03f);
                h.localPosition = center;
                h.localRotation = Quaternion.identity;
                h.localScale = Vector3.one;
                EditorUtility.SetDirty(h);
                Debug.Log(string.Format("[Case2Grid] Hole {0} placed at cell-derived ({1:0.0}, {2:0.0})",
                    shapeId, center.x, center.z));
            }

            if (!SceneIsAuthored)
            {
                // Disable original deep 3D extruded FBX renderers and child objects that hang below the board
                foreach (Renderer hr in h.GetComponentsInChildren<Renderer>(true))
                {
                    if (hr.name == "SDFHolePit" || hr.name == "NeonGlow") continue;
                    hr.enabled = false;
                }
                foreach (Transform child in h.GetComponentsInChildren<Transform>(true))
                {
                    if (child.name == "ActiveFX" || child.name == "IsActive" || child.name == "HoleCenter" ||
                        child.name.Contains("shadow") || child.name.Contains("decal") || child.name.Contains("Shadow"))
                    {
                        child.gameObject.SetActive(false);
                    }
                }
            }

            HoleGlowHighlight glow = Ensure<HoleGlowHighlight>(h.gameObject);

            glow.shapeId = shapeId;
            glow.shapeKey = BlockShapeIds.Key(glow.shapeId);
            glow.neonMaterial = neonMat;
            glow.rimMaterial = rimMat;
            glow.pitMaterial = pitMat;
            glow.neonColor = ColourFor(glow.shapeId, Snap(ReadBaseColor(h, Color.white)));
            glow.silhouetteMesh = FindSilhouette(blocks, glow.shapeKey);
            glow.pitScale = 0.74f;
            EditorUtility.SetDirty(glow);
            holeList.Add(glow);
        }

        // Clean up stray shadow decals under Frame and set exact 35px bevelled frame
        Material frameMat = EnsureMaterial(MaterialDir + "/Case2_BoardFrame.mat", "Case2/BoardFrame", m =>
        {
            m.SetColor("_BaseColor", new Color(0.48f, 0.54f, 0.82f, 1f));
            m.SetColor("_HighlightColor", new Color(0.78f, 0.85f, 0.99f, 1f));
            m.SetColor("_ShadowColor", new Color(0.18f, 0.22f, 0.44f, 1f));
        });

        Transform frameRoot = board.Find("Frame");
        if (frameRoot != null && !SceneIsAuthored)
        {
            // Deactivate all legacy messy prefabs under Frame that crossed/overlapped the board
            for (int i = 0; i < frameRoot.childCount; i++)
            {
                Transform child = frameRoot.GetChild(i);
                if (child.name.StartsWith("CleanFrame_")) continue;
                child.gameObject.SetActive(false);
            }

            // Outer border frame strictly OUTSIDE the 7x8 playfield
            // CleanFrame_Left x = -0.26, CleanFrame_Right x = 7.26, CleanFrame_Bottom z = -0.26, CleanFrame_Top z = 8.26
            float w = 0.52f; // border thickness (~35px on screen spanning x60-90)
            float h = 0.08f; // height of frame above board surface

            EnsureFrameRail(frameRoot, "CleanFrame_Left", new Vector3(-w * 0.5f, 0.02f, 4.0f), new Vector3(w, h, 8.0f + w * 2f), frameMat);
            EnsureFrameRail(frameRoot, "CleanFrame_Right", new Vector3(7.0f + w * 0.5f, 0.02f, 4.0f), new Vector3(w, h, 8.0f + w * 2f), frameMat);
            EnsureFrameRail(frameRoot, "CleanFrame_Top", new Vector3(3.5f, 0.02f, 8.0f + w * 0.5f), new Vector3(7.0f, h, w), frameMat);
            EnsureFrameRail(frameRoot, "CleanFrame_Bottom", new Vector3(3.5f, 0.02f, -w * 0.5f), new Vector3(7.0f, h, w), frameMat);
        }

        // Apply alternating checkerboard palette across all floor tiles
        if (!SceneIsAuthored) ApplyReferenceBoardPalette(board, blocks, holes);

        float pitch = GridCellSize;
        Debug.Log("[Case2Setup] board pitch (grid cell size) = " + pitch.ToString("0.000"));

        // ---------------------------------------------------------------- the block that plays
        Transform block = FindBlock(blocks, HeroShape);
        if (block == null) { Fail("Board/Blocks has no " + HeroShape + " block"); return; }

        HoleGlowHighlight target = null;
        for (int i = 0; i < holeList.Count; i++)
        {
            if (holeList[i].Matches(HeroShape)) { target = holeList[i]; break; }
        }
        if (target == null) { Fail("No hole matches the " + HeroShape + " block"); return; }

        // Decoy: the non-matching hole closest to the straight line the block travels, so the drag
        // genuinely passes over a hole it does not fit before reaching the one it does.
        HoleGlowHighlight decoy = null;
        float best = float.MaxValue;
        for (int i = 0; i < holeList.Count; i++)
        {
            HoleGlowHighlight h = holeList[i];
            if (h == target) continue;
            float d = DistanceToSegment(h.SnapPoint, block.position, target.SnapPoint);
            if (d < best) { best = d; decoy = h; }
        }

        // ---------------------------------------------------------------- director object
        Transform rootTf = FindRoot(scene, RootName);
        GameObject root = rootTf != null ? rootTf.gameObject : new GameObject(RootName);
        if (rootTf == null) SceneManager.MoveGameObjectToScene(root, scene);

        Case2Director director = Ensure<Case2Director>(root);
        BlockShatterSink sinkCtl = Ensure<BlockShatterSink>(root);
        sinkCtl.outwardSpeed = 1.45f;
        sinkCtl.funnelRate = 4.2f;
        sinkCtl.shardScale = 1.05f;
        sinkCtl.maxReadableChunks = 61;
        sinkCtl.shardMaterial = shardMat;
        sinkCtl.unitFracturePrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Case2_BlockHole/Prefabs/Fractured/FractureMeshes/Block-Single.prefab");
        Ensure<ReplayButton>(root);
        if (!SceneIsAuthored)
        {
            foreach (BlockDragController stale in root.GetComponents<BlockDragController>())
                Object.DestroyImmediate(stale);
        }

        // One stable holder per block. Reusing these objects preserves component file IDs, making a
        // wiring pass genuinely idempotent instead of rewriting half the scene on every gate run.
        Dictionary<string, GameObject> existingDragHolders = new Dictionary<string, GameObject>();
        for (int i = root.transform.childCount - 1; i >= 0; i--)
        {
            Transform c = root.transform.GetChild(i);
            if (!c.name.StartsWith("Drag_")) continue;
            if (existingDragHolders.ContainsKey(c.name))
            {
                if (!SceneIsAuthored) Object.DestroyImmediate(c.gameObject);
            }
            else existingDragHolders.Add(c.name, c.gameObject);
        }
        HashSet<string> usedDragHolders = new HashSet<string>();

        Camera sceneCam = Object.FindFirstObjectByType<Camera>(FindObjectsInactive.Include);
        List<BlockDragController> dragList = new List<BlockDragController>();
        BlockDragController dragCtl = null;

        for (int i = 0; i < blocks.childCount; i++)
        {
            Transform b = blocks.GetChild(i);
            if (b.GetComponentInChildren<MeshFilter>(true) == null) continue;
            if (!SceneIsAuthored && b.localScale.x < 0.60f) b.localScale = new Vector3(0.695333f, 0.695333f, 0.695333f);

            BlockShapeId shapeId = BlockShapeIds.Parse(b.name);
            string shape = BlockShapeIds.Key(shapeId);
            if (!SceneIsAuthored) UpgradeBlockSurface(b, shapeId);
            string holderName = "Drag_" + shape;
            GameObject holder;
            if (!existingDragHolders.TryGetValue(holderName, out holder))
            {
                holder = new GameObject(holderName);
                holder.transform.SetParent(root.transform, false);
            }
            usedDragHolders.Add(holderName);

            BlockDragController ctl = Ensure<BlockDragController>(holder);
            ctl.block = b;
            ctl.shapeId = shapeId;
            ctl.targetCamera = sceneCam;
            ctl.holes = holeList.ToArray();
            ctl.outlineMaterial = outlineMat;
            ctl.grabDotMaterial = dotMat;
            ctl.hoverRadius = Mathf.Max(0.70f, pitch * 0.42f);
            ctl.fracturedPrefab = LoadFractured(shape);

            dragList.Add(ctl);
            if (shapeId == HeroShape) dragCtl = ctl;

            Debug.Log("[Case2Setup] draggable block " + b.name + " shape=" + shape +
                      " fractured=" + (ctl.fracturedPrefab != null ? ctl.fracturedPrefab.name : "<fallback>") +
                      " hoverRadius=" + ctl.hoverRadius.ToString("0.00"));
        }

        if (!SceneIsAuthored)
        {
            foreach (KeyValuePair<string, GameObject> pair in existingDragHolders)
                if (!usedDragHolders.Contains(pair.Key) && pair.Value != null) Object.DestroyImmediate(pair.Value);
        }

        if (dragCtl == null && dragList.Count > 0) dragCtl = dragList[0];
        if (dragCtl == null) { Fail("no draggable block built"); return; }
        if (dragList.Count != 4 || holeList.Count != 4)
        {
            Fail("reference layout requires exactly 4 blocks and 4 holes; found " +
                 dragList.Count + " blocks / " + holeList.Count + " holes");
            return;
        }

        // A shared Single fracture silently changes the footprint of shapes without an authored
        // fracture (notably the reference hero Cross). Each drag controller supplies an exact
        // prefab when one exists; otherwise BlockShatterSink builds a deterministic composite footprint.
        sinkCtl.fracturedPrefab = null;
        sinkCtl.unitFracturePrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Case2_BlockHole/Prefabs/Fractured/FractureMeshes-Game/Block-Single.prefab");
        sinkCtl.shardMaterial = shardMat;
        sinkCtl.debrisBurstPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(DebrisPath);
        sinkCtl.impactRingPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(RingPath);
        sinkCtl.dustPuffPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(DustPath);
        // Shard tuning for distinct readable crystal fragments (at least 8 distinct chunks)
        // Spray budget and grain. These are the authored scene's live values; a re-run of this
        // setup used to reset them to a count-and-size pair that sits on the same N*s^2 iso-line
        // as the one before it, i.e. the same amount of material and the same hollow centre.
        sinkCtl.shardScale = 1.05f;
        sinkCtl.shardAlpha = 0.62f;
        sinkCtl.maxReadableChunks = 61;
        sinkCtl.coreRadiusFraction = 0.34f;
        sinkCtl.coreSpread = 0.12f;
        sinkCtl.coreRise = 0.35f;
        sinkCtl.shardWhitening = 0.01f;
        sinkCtl.outwardSpeed = pitch * 1.8f;
        sinkCtl.riseSpeed = new Vector2(pitch * 0.4f, pitch * 1.2f);
        sinkCtl.gravity = pitch * 5.5f;
        sinkCtl.funnelRate = 6.5f;
        sinkCtl.sinkSpeed = 1.25f;
        sinkCtl.swallowDepth = pitch * 0.65f;
        Debug.Log(string.Format("[Case2Setup] shard scatter from pitch {0:0.00}: outward={1:0.00} rise={2}-{3} gravity={4:0.00} scale={5:0.00}",
            pitch, sinkCtl.outwardSpeed, sinkCtl.riseSpeed.x.ToString("0.00"), sinkCtl.riseSpeed.y.ToString("0.00"),
            sinkCtl.gravity, sinkCtl.shardScale));

        // The staged scene ships without a listener, so none of the procedural audio would be heard.
        Camera mainCam = Object.FindFirstObjectByType<Camera>(FindObjectsInactive.Include);
        if (mainCam != null && mainCam.GetComponent<AudioListener>() == null)
        {
            mainCam.gameObject.AddComponent<AudioListener>();
            EditorUtility.SetDirty(mainCam.gameObject);
            Debug.Log("[Case2Setup] added AudioListener to " + mainCam.name);
        }

        director.drag = dragCtl;
        director.drags = dragList.ToArray();
        director.sink = sinkCtl;
        director.targetHole = target;
        director.decoyHole = decoy;
        // The reference break is local to the hole. Global wall-clock hitstop/camera shake both
        // weaken that read and make deterministic frame capture dependent on PNG/render stalls.
        director.hitstopSeconds = 0f;
        director.shakeAmplitude = 0f;
        director.punchAmplitude = 0f;

        EditorUtility.SetDirty(director);
        for (int i = 0; i < dragList.Count; i++) EditorUtility.SetDirty(dragList[i]);
        EditorUtility.SetDirty(sinkCtl);
        EditorUtility.SetDirty(root);

        AssetDatabase.SaveAssets();
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);

        Dump(board, block, target, decoy, sinkCtl);
        Debug.Log("[Case2Setup] SETUP_OK draggableBlocks=" + dragList.Count + " block=" + block.name +
                  " target=" + target.name + " decoy=" + (decoy != null ? decoy.name : "<none>"));
    }

    /// <summary>
    /// Zero-argument entry point for the capture gate. Unity's -executeMethod only accepts methods
    /// without arguments, so FrameStripCapture.Capture("BlockHole") cannot be invoked from the command
    /// line directly; this forwards to it.
    /// </summary>
    public static void CaptureBlockHole()
    {
        FrameStripCapture.Capture("BlockHole");
    }


    /// <summary>
    /// Pins the camera to the reference 0.625 frame whatever the screen is. Rebuilt rather than reused
    /// so the source stays the single authority over the serialised scene value (lesson #4).
    /// </summary>
    static void EnsureAspectEnforcer(Camera cam)
    {
        if (SceneIsAuthored)
        {
            if (cam.GetComponent<Shared.View.AspectRatioEnforcer>() != null) return;
        }
        Shared.View.AspectRatioEnforcer[] existing = cam.GetComponents<Shared.View.AspectRatioEnforcer>();
        for (int i = 0; i < existing.Length; i++) Object.DestroyImmediate(existing[i]);
        cam.gameObject.AddComponent<Shared.View.AspectRatioEnforcer>();
        EditorUtility.SetDirty(cam.gameObject);
        Debug.Log("[AspectEnforcer] added to " + cam.name + " (target aspect " +
                  Shared.View.AspectRatioEnforcer.TargetAspect.ToString("0.000") + ")");
    }

    /// <summary>Capture-only entry point for Case 2.</summary>
    public static void BuildAndCapture()
    {
        CaptureBlockHoleVideo();
    }

    public static void CaptureBlockHoleVideo()
    {
        FrameStripCapture.SetFrameCount(254); // 1.95 s at the reference's measured 130 fps.
        FrameStripCapture.Capture("BlockHole");
    }

    // ------------------------------------------------------------------ helpers

    /// <summary>
    /// Returns the single existing component or adds it once. Explicit wiring assignments above own
    /// tuned values; preserving the component keeps scene file IDs stable between repeated runs.
    /// </summary>
    static T Ensure<T>(GameObject go) where T : Component
    {
        T[] existing = go.GetComponents<T>();
        if (existing.Length == 0) return go.AddComponent<T>();
        if (!SceneIsAuthored)
        {
            for (int i = existing.Length - 1; i >= 1; i--) Object.DestroyImmediate(existing[i]);
        }
        return existing[0];
    }

    static Transform FindRoot(Scene scene, string name)
    {
        GameObject[] roots = scene.GetRootGameObjects();
        for (int i = 0; i < roots.Length; i++)
        {
            if (roots[i].name == name) return roots[i].transform;
        }
        return null;
    }

    /// <summary>Pre-fractured prefab for one shape; missing shapes use deterministic procedural footprints.</summary>
    static GameObject LoadFractured(string shapeKey)
    {
        GameObject g = AssetDatabase.LoadAssetAtPath<GameObject>(FracturedDir + "/Block-" + shapeKey + ".prefab");
        return g;
    }

    // ---------------------------------------------------------------- grid model
    public const int GridCols = 7;
    public const int GridRows = 8;
    public const float GridCellSize = 1.0f;

    public struct CellCoord
    {
        public int X;
        public int Z;
        public CellCoord(int x, int z) { X = x; Z = z; }
    }

    public class ShapeCellDefinition
    {
        public BlockShapeId ShapeId;
        public CellCoord[] Cells;

        public ShapeCellDefinition(BlockShapeId id, params CellCoord[] cells)
        {
            ShapeId = id;
            Cells = cells;
        }

        public Vector3 ComputeCenter(float y = 0.03f)
        {
            int minX = int.MaxValue, maxX = int.MinValue;
            int minZ = int.MaxValue, maxZ = int.MinValue;
            for (int i = 0; i < Cells.Length; i++)
            {
                minX = Mathf.Min(minX, Cells[i].X);
                maxX = Mathf.Max(maxX, Cells[i].X);
                minZ = Mathf.Min(minZ, Cells[i].Z);
                maxZ = Mathf.Max(maxZ, Cells[i].Z);
            }
            float cx = (minX + maxX + 1) * 0.5f;
            float cz = (minZ + maxZ + 1) * 0.5f;
            return new Vector3(cx, y, cz);
        }
    }

    static readonly Dictionary<BlockShapeId, ShapeCellDefinition> BlockGridPlacements = new Dictionary<BlockShapeId, ShapeCellDefinition>
    {
        {
            BlockShapeId.Square,
            new ShapeCellDefinition(BlockShapeId.Square,
                new CellCoord(0, 4), new CellCoord(1, 4),
                new CellCoord(0, 5), new CellCoord(1, 5))
        },
        {
            BlockShapeId.Cross,
            new ShapeCellDefinition(BlockShapeId.Cross,
                new CellCoord(4, 4),
                new CellCoord(3, 3), new CellCoord(4, 3), new CellCoord(5, 3),
                new CellCoord(4, 2))
        },
        {
            BlockShapeId.Two,
            new ShapeCellDefinition(BlockShapeId.Two,
                new CellCoord(6, 0), new CellCoord(6, 1), new CellCoord(6, 2))
        },
        {
            BlockShapeId.L,
            new ShapeCellDefinition(BlockShapeId.L,
                new CellCoord(0, 7), new CellCoord(0, 6), new CellCoord(1, 6), new CellCoord(2, 6))
        }
    };

    static readonly Dictionary<BlockShapeId, ShapeCellDefinition> HoleGridPlacements = new Dictionary<BlockShapeId, ShapeCellDefinition>
    {
        {
            BlockShapeId.Square,
            new ShapeCellDefinition(BlockShapeId.Square,
                new CellCoord(5, 6), new CellCoord(6, 6),
                new CellCoord(5, 7), new CellCoord(6, 7))
        },
        {
            BlockShapeId.Cross,
            new ShapeCellDefinition(BlockShapeId.Cross,
                new CellCoord(1, 2),
                new CellCoord(0, 1), new CellCoord(1, 1), new CellCoord(2, 1),
                new CellCoord(1, 0))
        },
        {
            BlockShapeId.Two,
            new ShapeCellDefinition(BlockShapeId.Two,
                new CellCoord(6, 3), new CellCoord(6, 4), new CellCoord(6, 5))
        },
        {
            BlockShapeId.L,
            // Four cells: the bottom row (4,0)(5,0)(6,0) and (4,1) above its left end. The bottom
            // right cell is the one the cyan bar stands on. ComputeCenter gives (5.5, 1.0), which
            // is what the authored scene now carries; the old entry here was (4.5, 1.0) and the
            // scene said 5.0, so this table was already disagreeing with the scene it describes.
            new ShapeCellDefinition(BlockShapeId.L,
                new CellCoord(4, 1), new CellCoord(4, 0), new CellCoord(5, 0), new CellCoord(6, 0))
        }
    };

    static Transform FindBlock(Transform blocks, BlockShapeId shapeId)
    {
        for (int i = 0; i < blocks.childCount; i++)
        {
            Transform b = blocks.GetChild(i);
            if (BlockShapeIds.Parse(b.name) == shapeId) return b;
        }
        return null;
    }

    static Mesh FindSilhouette(Transform blocks, string shapeKey)
    {
        Transform b = FindBlock(blocks, BlockShapeIds.Parse(shapeKey));
        if (b == null) return null;
        MeshFilter mf = b.GetComponentInChildren<MeshFilter>(true);
        return mf != null ? mf.sharedMesh : null;
    }

    /// <summary>
    /// The reference board's four block colours, sampled off "Block Hole.mp4" frame by frame with a
    /// median over the flat face of each block (not its lit edge): red #EC2C3F, green #229A02,
    /// cyan #0496E0, purple #AD36FC. The staged level's own palette is wider than this (pink, teal,
    /// orange, yellow, two blues) and none of those hues appear in the reference, which is why the
    /// board used to read magenta/teal where the reference reads red/cyan.
    /// </summary>
    static readonly Color[] ReferencePalette =
    {
        new Color(0.925f, 0.173f, 0.247f, 1f),   // red
        new Color(0.133f, 0.604f, 0.008f, 1f),   // green
        new Color(0.086f, 0.820f, 0.984f, 1f),   // cyan
        new Color(0.678f, 0.212f, 0.988f, 1f),   // purple
    };

    /// <summary>
    /// Snaps a staged colour onto the nearest reference-palette hue. Hue distance is measured on the
    /// circle, so orange lands on red and teal lands on cyan rather than on whatever is nearest in RGB.
    /// </summary>
    static Color Snap(Color c)
    {
        float h, sat, v;
        Color.RGBToHSV(c, out h, out sat, out v);
        int best = 0;
        float bestDistance = float.MaxValue;
        for (int i = 0; i < ReferencePalette.Length; i++)
        {
            float ph, ps, pv;
            Color.RGBToHSV(ReferencePalette[i], out ph, out ps, out pv);
            float d = Mathf.Abs(h - ph);
            if (d > 0.5f) d = 1f - d;                       // hue is a circle
            if (d < bestDistance) { bestDistance = d; best = i; }
        }
        Color outc = ReferencePalette[best];
        outc.a = c.a;
        return outc;
    }

    static bool ValidateReferenceSet(Transform root, string role, out string problem)
    {
        HashSet<BlockShapeId> found = new HashSet<BlockShapeId>();
        int visualChildren = 0;
        for (int i = 0; i < root.childCount; i++)
        {
            Transform child = root.GetChild(i);
            if (child.GetComponentInChildren<MeshFilter>(true) == null) continue;
            visualChildren++;
            BlockShapeId id = BlockShapeIds.Parse(child.name);
            if (id == BlockShapeId.Unknown)
            {
                problem = "unknown " + role + " identity on " + child.name;
                return false;
            }
            if (!found.Add(id))
            {
                problem = "duplicate " + role + " identity " + id + " on " + child.name;
                return false;
            }
        }

        HashSet<BlockShapeId> expected = new HashSet<BlockShapeId>
        {
            BlockShapeId.L, BlockShapeId.Square, BlockShapeId.Two, BlockShapeId.Cross
        };
        if (visualChildren != 4 || !found.SetEquals(expected))
        {
            problem = "reference layout requires " + role + " IDs L, Square, Two, Cross; found " +
                      visualChildren + " visual children / " + string.Join(",", found);
            return false;
        }

        problem = null;
        return true;
    }

    static Color ColourFor(BlockShapeId id, Color fallback)
    {
        switch (id)
        {
            case BlockShapeId.L: return ReferencePalette[0];
            case BlockShapeId.Square: return ReferencePalette[1];
            case BlockShapeId.Two: return ReferencePalette[2];
            case BlockShapeId.Cross: return ReferencePalette[3];
            default: return fallback;
        }
    }

    static Color ReadBaseColor(Transform t, Color fallback)
    {
        Renderer r = t.GetComponentInChildren<Renderer>(true);
        if (r == null || r.sharedMaterial == null) return fallback;
        return r.sharedMaterial.HasProperty("_BaseColor") ? r.sharedMaterial.GetColor("_BaseColor") : fallback;
    }

    static float DistanceToSegment(Vector3 p, Vector3 a, Vector3 b)
    {
        p.y = 0f; a.y = 0f; b.y = 0f;
        Vector3 ab = b - a;
        float len2 = ab.sqrMagnitude;
        if (len2 < 0.0001f) return Vector3.Distance(p, a);
        float t = Mathf.Clamp01(Vector3.Dot(p - a, ab) / len2);
        return Vector3.Distance(p, a + ab * t);
    }

    static void UpgradeBlockSurface(Transform block, BlockShapeId shapeId)
    {
        Renderer[] renderers = block.GetComponentsInChildren<Renderer>(true);
        if (renderers.Length == 0) return;
        Color sourceColor = Color.white;
        Texture sourceMap = null;
        Material source = renderers[0].sharedMaterial;
        if (source != null)
        {
            if (source.HasProperty("_BaseColor")) sourceColor = source.GetColor("_BaseColor");
            else if (source.HasProperty("_Color")) sourceColor = source.GetColor("_Color");
            if (source.HasProperty("_BaseMap")) sourceMap = source.GetTexture("_BaseMap");
            else if (source.HasProperty("_MainTex")) sourceMap = source.GetTexture("_MainTex");
        }
        Color tuned = ColourFor(shapeId, Snap(sourceColor));
        tuned.a = 1f;
        Texture2D topPattern = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Case2_BlockHole/Textures/case2_block_top_pattern.png");
        Material m = EnsureMaterial(MaterialDir + "/Case2_BlockSurface_" + shapeId + ".mat", "Case2/ToyBlock", mat =>
        {
            mat.SetColor("_BaseColor", tuned);
            if (topPattern != null) mat.SetTexture("_PatternMap", topPattern);
            mat.SetFloat("_PatternInfluence", 0.55f);
            mat.SetFloat("_EdgeLift", 0.06f);
            mat.SetFloat("_FaceContrast", 0.08f);
            mat.SetFloat("_Smoothness", 0.25f);
        });
        if (m == null) return;
        for (int r = 0; r < renderers.Length; r++)
        {
            Material[] mats = new Material[Mathf.Max(1, renderers[r].sharedMaterials.Length)];
            for (int j = 0; j < mats.Length; j++) mats[j] = m;
            renderers[r].sharedMaterials = mats;
            EditorUtility.SetDirty(renderers[r]);
        }
    }

    internal static void ApplyReferenceBoardPalette(Transform board, Transform blocks, Transform holes)
    {
        Texture2D tileSheen = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Case2_BlockHole/Textures/case2_tile_sheen.png");
        Material navyA = EnsureMaterial(MaterialDir + "/Case2_BoardNavy_A.mat", "Case2/BoardTile", m =>
        {
            // RE-SOLVED against the reference (0e1c47a). The old (0.175, 0.225, 0.420) was both too
            // dark and over-saturated - measured 0.76 against the reference's 0.49 - and the chamfer
            // was invisible: _BevelLift defaulted to 0.03, which produced an overshoot of MINUS ONE
            // code value, so the board read as a painted texture rather than as blocks.
            m.SetColor("_BaseColor", new Color(0.2432f, 0.2797f, 0.4053f, 1f));
            if (tileSheen != null) m.SetTexture("_SheenMap", tileSheen);
            m.SetFloat("_SheenStrength", 0.12f);
            m.SetFloat("_BevelContrast", 0.20f);
            m.SetFloat("_VerticalGrad", 0.05f);
            m.SetFloat("_BevelLift", 0.42f);
            m.SetFloat("_BevelLiftZ", 0.16f);
            m.SetFloat("_ShadeWidth", 0.085f);
        });
        Material navyB = EnsureMaterial(MaterialDir + "/Case2_BoardNavy_B.mat", "Case2/BoardTile", m =>
        {
            m.SetColor("_BaseColor", new Color(0.2792f, 0.3151f, 0.4249f, 1f));
            if (tileSheen != null) m.SetTexture("_SheenMap", tileSheen);
            m.SetFloat("_SheenStrength", 0.12f);
            m.SetFloat("_BevelContrast", 0.20f);
            m.SetFloat("_VerticalGrad", 0.05f);
            m.SetFloat("_BevelLift", 0.42f);
            m.SetFloat("_BevelLiftZ", 0.16f);
            m.SetFloat("_ShadeWidth", 0.085f);
        });
        if (navyA == null || navyB == null) return;

        Renderer[] all = board.GetComponentsInChildren<Renderer>(true);
        for (int i = 0; i < all.Length; i++)
        {
            Renderer r = all[i];
            if (r == null) continue;
            string n = r.name.ToLowerInvariant();
            if (n.StartsWith("tile_"))
            {
                int gridX = Mathf.RoundToInt(r.transform.position.x - 0.5f);
                int gridZ = Mathf.RoundToInt(r.transform.position.z - 0.5f);
                bool isOdd = ((gridX + gridZ) & 1) != 0;
                r.sharedMaterial = isOdd ? navyB : navyA;
                EditorUtility.SetDirty(r);
            }
        }
    }

    static Material EnsureMaterial(string path, string shaderName, System.Action<Material> configure)
    {
        Shader shader = Shader.Find(shaderName);
        if (shader == null)
        {
            Debug.LogError("[Case2Setup] Shader not found: " + shaderName);
            return null;
        }

        Material m = AssetDatabase.LoadAssetAtPath<Material>(path);
        bool created = false;
        if (m == null)
        {
            m = new Material(shader);
            AssetDatabase.CreateAsset(m, path);
            created = true;
        }
        else if (m.shader != shader)
        {
            m.shader = shader;
        }

        configure(m);
        EditorUtility.SetDirty(m);
        Debug.Log("[Case2Setup] material " + (created ? "created " : "updated ") + path + " (" + shaderName + ")");
        return m;
    }

    static void MakeAdditive(Material m)
    {
        MakeTransparent(m);
        m.SetFloat("_Blend", 2f);
        m.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
        m.SetFloat("_DstBlend", (float)BlendMode.One);
    }

    static void MakeOpaque(Material m)
    {
        m.SetFloat("_Surface", 0f);
        m.SetFloat("_Blend", 0f);
        m.SetFloat("_SrcBlend", (float)BlendMode.One);
        m.SetFloat("_DstBlend", (float)BlendMode.Zero);
        m.SetFloat("_ZWrite", 1f);
        m.SetFloat("_AlphaClip", 0f);
        m.DisableKeyword("_SURFACE_TYPE_TRANSPARENT");
        m.DisableKeyword("_ALPHATEST_ON");
        m.renderQueue = (int)RenderQueue.Geometry;
    }

    static void MakeTransparent(Material m)
    {
        m.SetFloat("_Surface", 1f);
        m.SetFloat("_Blend", 0f);
        m.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
        m.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
        m.SetFloat("_ZWrite", 0f);
        m.SetFloat("_AlphaClip", 0f);
        m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        m.DisableKeyword("_ALPHATEST_ON");
        m.renderQueue = (int)RenderQueue.Transparent;
    }

    static void Dump(Transform board, Transform block, HoleGlowHighlight target, HoleGlowHighlight decoy,
                     BlockShatterSink sink)
    {
        StringBuilder sb = new StringBuilder();
        sb.AppendLine("[Case2Setup] ---- scene discovery ----");

        Camera cam = Object.FindFirstObjectByType<Camera>(FindObjectsInactive.Include);
        if (cam != null)
        {
            sb.AppendLine(string.Format("camera pos={0} euler={1} ortho={2} size={3:0.000}",
                cam.transform.position, cam.transform.eulerAngles, cam.orthographic, cam.orthographicSize));
        }

        DumpObject(sb, "block", block);
        DumpObject(sb, "target hole", target != null ? target.transform : null);
        DumpObject(sb, "decoy hole", decoy != null ? decoy.transform : null);

        if (sink.fracturedPrefab != null)
        {
            MeshRenderer[] pieces = sink.fracturedPrefab.GetComponentsInChildren<MeshRenderer>(true);
            sb.AppendLine("fractured prefab pieces=" + pieces.Length + " path=" + FracturedPath);
        }
        else
        {
            sb.AppendLine("fractured prefab MISSING at " + FracturedPath);
        }

        sb.AppendLine("vfx debris=" + (sink.debrisBurstPrefab != null) +
                      " ring=" + (sink.impactRingPrefab != null) +
                      " dust=" + (sink.dustPuffPrefab != null));

        Debug.Log(sb.ToString());
    }

    static void DumpObject(StringBuilder sb, string label, Transform t)
    {
        if (t == null) { sb.AppendLine(label + ": <null>"); return; }
        Renderer r = t.GetComponent<Renderer>();
        MeshFilter mf = t.GetComponent<MeshFilter>();
        sb.AppendLine(string.Format("{0}: {1} pos={2} bounds c={3} e={4} mesh={5} mat={6} shader={7}",
            label, t.name, t.position,
            r != null ? r.bounds.center.ToString() : "-",
            r != null ? r.bounds.extents.ToString() : "-",
            mf != null && mf.sharedMesh != null ? mf.sharedMesh.name : "-",
            r != null && r.sharedMaterial != null ? r.sharedMaterial.name : "-",
            r != null && r.sharedMaterial != null ? r.sharedMaterial.shader.name : "-"));
    }

    static void EnsureFrameRail(Transform parent, string name, Vector3 pos, Vector3 size, Material mat)
    {
        Transform t = parent.Find(name);
        GameObject go;
        if (t == null)
        {
            go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.SetParent(parent, false);
            Collider col = go.GetComponent<Collider>();
            if (col != null) Object.DestroyImmediate(col);
        }
        else
        {
            go = t.gameObject;
        }
        go.SetActive(true);
        go.transform.localPosition = pos;
        go.transform.localRotation = Quaternion.identity;
        go.transform.localScale = size;
        Renderer r = go.GetComponent<Renderer>();
        if (r != null && mat != null) r.sharedMaterial = mat;
    }

    static void Fail(string message)
    {
        Debug.LogError("[Case2Setup] SETUP_FAILED " + message);
    }
}
