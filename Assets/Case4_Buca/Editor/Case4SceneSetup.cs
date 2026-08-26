using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Case4;
using Shared.Sequencing;
using Shared.EditorTools;

/// <summary>
/// Wiring and verification utilities for the hand-authored Case 4 scene. <see cref="Build"/> is
/// intentionally wiring-only: it never replaces the rig, moves the camera, regenerates the stack or
/// touches authored transforms. The former procedural constructor remains private below only so its
/// geometry-analysis helpers stay available to the reference gates; production tools never call it.
/// </summary>
public static class Case4SceneSetup
{
    const string ScenePath = "Assets/Case4_Buca/Scenes/Buca.unity";
    const string MaterialDir = "Assets/Case4_Buca/Materials";
    const string PuckPath = "Assets/Case4_Buca/Prefabs/Puck.prefab";
    const string GreenNeonPath = "Assets/Case4_Buca/Materials/GreenNeon.mat";
    const string TrailVfxPath = "Assets/Case4_Buca/VFX/StarTrail.prefab";
    const string TrailGlowMaterialPath = "Assets/Case4_Buca/VFX/Materials/PFX_BucaSoft.mat";

    const string RigName = "Case4_Rig";
    const string BlocksName = "Case4_Blocks";
    const string SequenceName = "Case4_Sequence";
    const string PuckName = "Case4_Puck";
    /// <summary>
    /// DO NOT FLIP THIS. It is not a switch between two working builders: the procedural path below
    /// no longer describes the scene that ships, and running it would replace the authored arena with
    /// a different one while every gate still reported green. Four measured contradictions, as of
    /// this cleanup:
    ///
    ///   1. ColumnHeights = {8,7,6,5,4,3} - six columns, tallest 8. The authored stack is EIGHT
    ///      columns of 36 blocks tapering to 1, all resting at world y = 0.620 (ONE layer).
    ///   2. SolveStackBlockSize bisects a GAPLESS six-column footprint. The authored columns are
    ///      pitched 0.4398 (x) / 0.4500 (z) around blocks 0.4354 wide, i.e. with a real inter-column
    ///      gap, so the solved size would be wrong even if the column count matched.
    ///   3. DepthRatio = 1.7 describes a brick 1.7 deep per unit wide. The authored block is
    ///      0.4354 x 1.240 x 0.4456 - taller than it is deep, ratio 1.02, not 1.7.
    ///   4. blockPitch = blockSize (line ~288). The values the scene actually carries are
    ///      0.4666573 and 0.4675906 - not equal, and both stale against the geometry above.
    ///
    /// Build() is still called, by RefPositionGate and Case1's CaseGrade, precisely BECAUSE it is a
    /// no-op there; and the constants and ViewportBox above it are live. So this file stays. What is
    /// unreachable is BuildLegacyProcedural and the private helpers only it calls - kept as the
    /// measured derivation of why the authored arena has the proportions it has, which is the
    /// evidence trail for this case, not as something anyone should run.
    /// </summary>
    // static readonly, not const. Identical behaviour, but a const lets the compiler PROVE every
    // `if (!SceneIsAuthored)` body is dead, and it emitted 16 "unreachable code" warnings saying so.
    // Those bodies are deliberately parked - the scene owns that placement now - and burying 16
    // warnings in the console to say it is worse than the one comment that already explains it.
    static readonly bool SceneIsAuthored = true;

    /// <summary>Stack silhouette: tallest column against the left rail, stepping down to the right.</summary>
    // The reference staircase, counted off _refs/Developer Case Referans/Buca.mp4 at t=0.14 s
    // (1080x1728): six columns with heights 7, 7, 6, 5, 4, 3 (height 217px matching ref bbox y 0.336..0.464).
    // {8,7,6,5,4,3}: the reference resolves SIX descending plateaus where {7,7,...} resolves five,
    // because a duplicated tallest height reads as one wide plateau. Confirmed by capture - the top
    // plateau collapsed from 7 slices to 3. NOTE: this builder is inert (SceneIsAuthored) and its
    // SolveStackBlockSize still bisects a GAPLESS six-column footprint, so it would need re-deriving
    // against the 15.6%-of-a-block inter-column gap before it could reproduce the authored scene.
    static readonly int[] ColumnHeights = { 8, 7, 6, 5, 4, 3 };

    /// <summary>
    /// How many block-widths deep each brick is along Z (toward the camera). The reference's blocks
    /// are 2:1 bricks with the long axis pointing at the viewer, not cubes - the owner's own note,
    /// "ust uste degil one dogruydu". Three independent measurements agree on 2.0: PCA aspect of
    /// isolated scattered blocks is 2.01/1.84 in the reference against 1.42/1.53 for our cubes; the
    /// reference's scattered green coverage is ~1.6x ours, and the mean projected area of a randomly
    /// tumbling box is surfaceArea/4, so cube 6a^2 -> brick 10a^2 predicts exactly 1.67; and the
    /// owner said it in words. MEASURED DOWN TO 1.7 after the first capture: at 2.0 the tower
    /// projected 240 px tall against the reference's 231, and its deep top faces filled in the
    /// staircase notches the reference keeps open (fill ratio 0.749 against the reference's 0.657).
    /// Pre-change we were 212 px, nineteen SHORT, so the depth increase is real - solving for the
    /// height match gives 1.68. That is a third independent arrival at ~1.7 alongside the scattered
    /// coverage ratio; only the PCA reading wants 1.9-2.0, and three routes beat one. The apparent
    /// 25% resting-mass overshoot at 2.0 was mostly not depth at all: the reference's interior is
    /// 12.4% dark seam, so its 42.3k of green is measured on a tower with holes in it.
    /// 1.0 restores the old cubes and every formula below reduces to the
    /// pre-change scene at that value - that identity is the check on this arithmetic.
    /// </summary>
    const float DepthRatio = 1.7f;

    /// <summary>
    /// Z of a block's CENTRE, placed so its FRONT face keeps the clearance the cube stack had.
    /// BottomInnerZ is the near rail (Rail_Bottom sits at BottomInnerZ - t*0.5, and the shader's
    /// isFront = saturate(-N.z) puts the camera at lower Z), so Z grows away from the viewer and the
    /// front face sits at centre - halfDepth. The cube stack had halfDepth 0.5 and a centre at
    /// 0.95, i.e. a front face at BottomInnerZ + 0.45*size.
    ///
    /// Anchoring the CENTRE and doubling the depth would have put the front face at
    /// BottomInnerZ - 0.05*size - INSIDE the bottom rail collider, which spans back to
    /// BottomInnerZ - 1.2*size. The front row would have started the run interpenetrating the rail
    /// and been ejected by the solver on frame one: a visible twitch in the stack before the puck is
    /// anywhere near it. It would not have tripped anything - 0.05 is under the blockSize*0.25 the
    /// coin gate tests - so every boolean would have passed with the frame still wrong.
    ///
    /// At DepthRatio = 1 this returns bottomInnerZ + size * 0.95, the pre-change value exactly.
    /// At 2 it puts the front face back at BottomInnerZ + 0.45*size, bit-identical to where it is
    /// now - which is both the face the camera sees and the face the puck strikes.
    /// </summary>
    static float StackZFor(float bottomInnerZ, float size)
    {
        return bottomInnerZ + size * (0.95f + 0.5f * (DepthRatio - 1f));
    }

    // ------------------------------------------------------------------ reference viewport targets
    //
    // Measured off _refs/Developer Case Referans/Buca.mp4, frame at t=0.14 s, resampled to 540x864 and
    // thresholded per object (white rim: value>0.82 & saturation<0.14; green stack: g-r>0.18 & g-b>0.18;
    // gold puck: r-b>0.22 & r-g>0.03). Numbers are viewport fractions with y measured from the BOTTOM,
    // at the reference aspect 1080/1728 = 0.625.
    //
    //   arena rim silhouette   x 0.028 .. 0.969   y 0.311 .. 0.777
    //   green stack            x 0.087 .. 0.344   y 0.336 .. 0.464
    //   gold puck centroid     x 0.800            y 0.361
    //   centre divider         x 0.474 .. 0.513   (staged mesh; not placed by this script)
    //
    // These drive the camera and the two things this script actually places. Everything is solved
    // against the camera with WorldToViewportPoint rather than guessed in world units.
    public const float RefRimX0 = 0.028f;
    public const float RefRimX1 = 0.969f;
    public const float RefRimY0 = 0.311f;
    public const float RefRimY1 = 0.777f;
    public const float RefStackX0 = 0.087f;
    public const float RefStackX1 = 0.344f;
    public const float RefStackY0 = 0.336f;
    public const float RefStackY1 = 0.464f;
    public const float RefPuckX = 0.800f;
    public const float RefPuckY = 0.361f;

    /// <summary>Everything the arena mesh tells us about where things can go.</summary>
    public struct Arena
    {
        public Bounds Bounds;
        public float LeftInnerX;      // inner face of the left rail
        public float RightInnerX;     // inner face of the right rail
        public float DividerMinX;
        public float DividerMaxX;
        public float DividerMinZ;
        public float DividerMaxZ;
        public float ArchInnerZ;      // inner face of the arch, i.e. the top rail
        public float BottomInnerZ;    // inner face of the bottom rail
        public float RimTopY;
        public bool HasDivider;

        public float DividerCenterX { get { return (DividerMinX + DividerMaxX) * 0.5f; } }
        public float LeftLaneWidth { get { return DividerMinX - LeftInnerX; } }
        public float RightLaneWidth { get { return RightInnerX - DividerMaxX; } }
        public float RightLaneCenterX { get { return (DividerMaxX + RightInnerX) * 0.5f; } }
    }

    // ------------------------------------------------------------------ entry points

    /// <summary>Menu entry point.</summary>
    public static void BuildMenu() { Build(); }

    /// <summary>
    /// Zero-argument entry point for the capture gate. Unity's -executeMethod only accepts methods
    /// without arguments, so FrameStripCapture.Capture("Buca") cannot be invoked from the command
    /// line directly; this forwards to it.
    /// </summary>
    public static void CaptureBuca() { FrameStripCapture.Capture("Buca"); }

    /// <summary>Capture-only entry point for Case 4.</summary>
    public static void BuildAndCapture()
    {
        CaptureBuca();
    }

    /// <summary>
    /// Zero-argument layout gate. Opens the authored scene and runs it in play mode with
    /// <see cref="Case4InputProbe"/>, which asserts the direction facts
    /// (stack left of puck, no hole, puck is a live rigidbody) and the collapse quality.
    /// </summary>
    public static void LayoutGate() { Case4LayoutGateDriver.Run(); }

    // ------------------------------------------------------------------ build

    /// <summary>
    /// Batchmode entry point: validates the authored scene and refreshes component references only.
    /// This is safe to run after manual scene edits because no object or transform is created, deleted
    /// or moved (apart from removing an already-missing legacy MonoBehaviour on Case4_Sequence).
    /// </summary>
    public static void Build()
    {
        if (SceneIsAuthored)
        {
            Debug.Log("[Case4Setup] Authored scene preserved; no reconstruction performed. " +
                      "BuildLegacyProcedural is UNREACHABLE and contradicts this scene in four places - " +
                      "see the SceneIsAuthored doc comment before considering running it.");
            return;
        }
        BuildLegacyProcedural();
    }

    /// <summary>Retired procedural scene constructor. Kept private; do not call for authored scenes.</summary>
    static void BuildLegacyProcedural()
    {
        Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        StringBuilder log = new StringBuilder();
        log.AppendLine("[Case4Setup] ---- build ----");

        // ------------------------------------------------------------- staged art
        Transform art = FindRoot(scene, "case_test_scene");
        if (art == null) { Fail("case_test_scene root not found"); return; }

        Renderer frameRenderer = FindChildRenderer(art, "level_frame");
        Renderer obstacleRenderer = FindChildRenderer(art, "obstacle");
        Transform startDisc = FindChildTransform(art, "disc");
        if (frameRenderer == null) { Fail("case_test_scene/level_frame not found"); return; }
        if (startDisc == null) { Fail("case_test_scene/disc not found"); return; }

        foreach (Renderer sr in art.GetComponentsInChildren<Renderer>(true))
            log.AppendLine("staged part '" + sr.name + "' bounds c=" + sr.bounds.center.ToString("0.##") +
                           " e=" + sr.bounds.extents.ToString("0.##") + " enabled=" + sr.enabled);

        Arena arena = MeasureArena(frameRenderer, log);

        // ------------------------------------------------------------- camera FIRST
        // Everything below is placed by where it lands ON SCREEN, so the camera has to exist before
        // anything is positioned. It used to be framed at the very end, which meant the stack and the
        // puck were placed in world units and whatever they projected to was what you got.
        Camera cam = Object.FindFirstObjectByType<Camera>(FindObjectsInactive.Include);
        if (cam == null) { Fail("no camera in the scene"); return; }
        FrameCamera(cam, arena.Bounds, frameRenderer, log);

        // ------------------------------------------------------------- materials
        Texture2D railGlow = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Case4_Buca/Textures/case4_rail_glow.png");
        Material neonWall = EnsureMaterial(MaterialDir + "/Case4_NeonWall.mat", "Case4/NeonRail", m =>
        {
            m.SetColor("_BaseColor", new Color(0.97f, 0.98f, 1f, 1f));
            if (railGlow != null) m.SetTexture("_GlowMap", railGlow);
            m.SetFloat("_Smoothness", 0.65f);
            m.SetFloat("_GlowIntensity", 1.25f);
            m.EnableKeyword("_EMISSION");
            m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            m.SetColor("_EmissionColor", Color.black);
        });

        Material coinGold = EnsureMaterial(MaterialDir + "/Case4_CoinGold.mat", "Universal Render Pipeline/Lit", m =>
        {
            m.SetColor("_BaseColor", new Color(1f, 0.72f, 0.10f, 1f));
            m.SetFloat("_Smoothness", 0.72f);
            m.SetFloat("_Metallic", 0.40f);
            m.EnableKeyword("_EMISSION");
            m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            m.SetColor("_EmissionColor", new Color(0.30f, 0.16f, 0.02f, 1f));
        });

        // The start pad is a pad, not a second coin: the reference disc is a dim ring under the puck,
        // and a bright gold disc the same size as the puck reads as a duplicate puck in the frame.
        Material discPad = EnsureMaterial(MaterialDir + "/Case4_DiscPad.mat", "Universal Render Pipeline/Lit", m =>
        {
            // P20: the launch pad rendered #A48A44 against the reference's #796F47 - too bright and too orange.
            m.SetColor("_BaseColor", new Color(0.458f, 0.402f, 0.251f, 1f));
            m.SetFloat("_Smoothness", 0.30f);
            m.SetFloat("_Metallic", 0.10f);
            m.SetColor("_EmissionColor", Color.black);
        });

        Material puckGold = EnsureMaterial(MaterialDir + "/Case4_PuckGold.mat", "Case4/GoldPuck", m =>
        {
            m.SetColor("_BaseColor", new Color(1f, 0.84f, 0.34f, 1f));
            m.SetFloat("_Smoothness", 0.86f);
            m.SetFloat("_Metallic", 0.45f);
            m.EnableKeyword("_EMISSION");
            m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            m.SetColor("_EmissionColor", new Color(0.22f, 0.14f, 0.02f, 1f));
        });

        Material aimLineMat = EnsureMaterial(MaterialDir + "/Case4_AimLine.mat", "Universal Render Pipeline/Unlit", m =>
        {
            m.SetFloat("_Surface", 1f);
            m.SetFloat("_Blend", 2f);
            m.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            m.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.One);
            m.SetFloat("_ZWrite", 0f);
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            // Additive blend, so screen brightness scales with alpha. Measured against the reference
            // in the lane interior at t=0: its beam lifts the floor by +18 L at y=1020 and +30 at
            // y=980, ours lifted it by +34 and +111. 0.14 -> 0.08 halves that.
            m.SetColor("_BaseColor", new Color(1f, 0.94f, 0.72f, 0.08f));
        });

        // ------------------------------------------------------------- rig root, rebuilt every run
        Transform oldRig = FindRoot(scene, RigName);
        if (oldRig != null) Object.DestroyImmediate(oldRig.gameObject);
        GameObject rig = new GameObject(RigName);
        SceneManager.MoveGameObjectToScene(rig, scene);

        // Any hole left over from an older build of this scene goes with it. The reference clip has no
        // hole in it at all; the brief's "sent into the hole" wording was taken literally once and put
        // a black half-disc in the middle of the arena that is in no frame of the reference.
        int holesRemoved = RemoveHoles(scene, log);

        // ------------------------------------------------------------- sizes derived from the arena
        // Six columns have to fit inside the left lane with a little air, so the block size is a
        // fraction of the measured lane, never a hard-coded number or a single renderer bound.
        // `unit` is the gameplay scale everything except the stack has always been built from: puck
        // collider, rail thickness, aim radii, coin scale. It is left exactly as it was so this package
        // cannot move the feel while it is moving the positions.
        float unit = Mathf.Max(0.12f, arena.LeftLaneWidth / (ColumnHeights.Length + 0.95f));

        // The stack's own block size is SOLVED, not derived: the size at which six columns project to
        // the width the reference's six columns project to. Ours read 0.328 of the frame against the
        // reference's 0.257 - the single biggest "wrong place" in the frame.
        float stackZSeed = StackZFor(arena.BottomInnerZ, unit);
        float blockSize = SolveStackBlockSize(cam, arena, stackZSeed, unit, log);
        float blockPitch = blockSize;
        float puckRadius = unit * 0.50f;
        log.AppendLine(string.Format("derived: unit={0:0.###} solved blockSize={1:0.###} pitch={2:0.###} puckRadius={3:0.###} (left lane {4:0.###} wide)",
            unit, blockSize, blockPitch, puckRadius, arena.LeftLaneWidth));

        // ------------------------------------------------------------- block stack (LEFT lane)
        List<Transform> cubes = CollectStagedCubes(scene);
        if (cubes.Count == 0) { Fail("no staged Cube objects found"); return; }
        log.AppendLine("staged cubes found=" + cubes.Count);

        Transform blocksRoot = FindRoot(scene, BlocksName);
        if (blocksRoot == null)
        {
            GameObject bg = new GameObject(BlocksName);
            SceneManager.MoveGameObjectToScene(bg, scene);
            blocksRoot = bg.transform;
        }
        blocksRoot.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
        blocksRoot.localScale = Vector3.one;

        Material greenNeon = EnsureMaterial(GreenNeonPath, "Case4/SoftBlock", m =>
        {
            m.SetColor("_BaseColor", new Color(0.12f, 0.78f, 0.16f, 1f));
            m.SetColor("_EmissionColor", new Color(0.002f, 0.050f, 0.004f, 1f));
            if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", 0.20f);
            if (m.HasProperty("_EdgeLift")) m.SetFloat("_EdgeLift", 0.10f);
            if (m.HasProperty("_TopLift")) m.SetFloat("_TopLift", 0.08f);
        });
        float stackZ = StackZFor(arena.BottomInnerZ, blockSize);
        float stackX0 = SolveStackX0(cam, arena, blockSize, stackZ, log);   // centre of the tall column
        List<Transform> stackBlocks = LayOutStack(cubes, blocksRoot, greenNeon, stackX0, blockSize, stackZ, log);

        // Log-only (the printed bounds, and a center.x comparison further down that a Z-only change
        // cannot move) - but it must not print a Z extent that lies, because this is one of the lines
        // read to judge the capture. Bounds' second argument is SIZE, not extents.
        Vector3 brickSize = new Vector3(blockSize, blockSize, blockSize * DepthRatio);
        Bounds stackBounds = new Bounds(stackBlocks[0].position, brickSize);
        for (int i = 1; i < stackBlocks.Count; i++)
            stackBounds.Encapsulate(new Bounds(stackBlocks[i].position, brickSize));
        log.AppendLine("stack bounds c=" + stackBounds.center.ToString("0.###") + " e=" + stackBounds.extents.ToString("0.###"));

        // ------------------------------------------------------------- puck (RIGHT lane)
        Transform oldPuck = FindRoot(scene, PuckName);
        if (oldPuck != null) Object.DestroyImmediate(oldPuck.gameObject);

        GameObject puckPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(PuckPath);
        if (puckPrefab == null) { Fail("Puck prefab missing at " + PuckPath); return; }

        // A plain instantiate, not a prefab link: the puck needs a rigidbody and a sized collider of
        // its own, and instance overrides on a shared prefab are a worse place to keep those.
        GameObject puck = Object.Instantiate(puckPrefab);
        puck.name = PuckName;
        SceneManager.MoveGameObjectToScene(puck, scene);
        puck.transform.SetParent(rig.transform, true);
        puck.transform.localScale = Vector3.one * 0.95f;

        // The reference does NOT park the puck in the middle of the right lane: it sits at viewport
        // 0.800, about 71% of the way across the lane, and ours sat at the lane centre, 0.709. So the
        // rest pose is solved from the reference viewport point instead of assumed, then clamped so it
        // still has clean air on both sides of the lane.
        float puckY = unit * 0.18f;
        float puckX, puckZ;
        SolvePuckRest(cam, arena, puckY, puckRadius, out puckX, out puckZ, log);
        puck.transform.SetPositionAndRotation(new Vector3(puckX, puckY, puckZ), Quaternion.identity);

        foreach (Renderer r in puck.GetComponentsInChildren<Renderer>(true)) r.sharedMaterial = puckGold;
        foreach (Collider c in puck.GetComponentsInChildren<Collider>(true))
        {
            if (c.transform != puck.transform) Object.DestroyImmediate(c);
        }

        // The collider is lifted off the floor with its centre offset so the puck's body sweeps the
        // bottom row of the stack instead of grinding along the ground plane.
        SphereCollider puckCol = puck.GetComponent<SphereCollider>();
        if (puckCol == null) puckCol = puck.AddComponent<SphereCollider>();
        float localScale = 0.95f;
        puckCol.radius = puckRadius / localScale;
        puckCol.center = new Vector3(0f, (unit * 0.44f - puckY) / localScale, 0f);   // low enough to topple the bottom row

        Rigidbody puckRb = puck.GetComponent<Rigidbody>();
        if (puckRb == null) puckRb = puck.AddComponent<Rigidbody>();
        puckRb.mass = 9.0f;   // the stack has to be ploughed through, not tapped
        puckRb.useGravity = false;
        puckRb.isKinematic = false;
        puckRb.linearDamping = 0f;
        puckRb.angularDamping = 0.05f;
        puckRb.interpolation = RigidbodyInterpolation.Interpolate;
        puckRb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        puckRb.constraints = RigidbodyConstraints.FreezePositionY | RigidbodyConstraints.FreezeRotation;

        log.AppendLine(string.Format("puck at {0} radius={1:0.###} rb(kinematic={2}, gravity={3}) collider enabled={4}",
            puck.transform.position.ToString("0.##"), puckRadius, puckRb.isKinematic, puckRb.useGravity, puckCol.enabled));

        // The staged start disc lives on the left; the reference disc is on the right, under the puck.
        startDisc.position = new Vector3(puckX, startDisc.position.y, puckZ);
        Renderer discRenderer = FindChildRenderer(art, "disc");
        if (discRenderer != null)
        {
            Material[] discMats = new Material[Mathf.Max(1, discRenderer.sharedMaterials.Length)];
            for (int i = 0; i < discMats.Length; i++) discMats[i] = discPad;
            discRenderer.sharedMaterials = discMats;
            log.AppendLine("start disc moved to " + startDisc.position.ToString("0.##") + " and recoloured gold");
        }

        // ------------------------------------------------------------- real colliders
        BuildColliders(rig.transform, arena, unit, log);

        // ------------------------------------------------------------- staged clutter
        Animator[] anims = art.GetComponentsInChildren<Animator>(true);
        for (int i = 0; i < anims.Length; i++) anims[i].enabled = false;
        if (obstacleRenderer != null) obstacleRenderer.enabled = false;
        Renderer obstaclePlate = FindChildRenderer(art, "hole_obstacle");
        if (obstaclePlate != null) obstaclePlate.enabled = false;
        log.AppendLine("held " + anims.Length + " staged animator(s) still, obstacle + hole plate hidden");

        Transform planeTf = FindRoot(scene, "Plane");
        if (planeTf != null)
        {
            Material floorMat = EnsureMaterial(MaterialDir + "/Case4_Floor.mat", "Case4/Floor", m =>
            {
                m.SetColor("_LowColor", new Color(0.392f, 0.451f, 0.506f, 1f));
                m.SetColor("_HighColor", new Color(0.314f, 0.365f, 0.439f, 1f));
                m.SetColor("_ShadowColor", new Color(0.051f, 0.086f, 0.180f, 1f));
            });
            Renderer pr = planeTf.GetComponent<Renderer>();
            if (pr != null && floorMat != null) pr.sharedMaterial = floorMat;
        }

        // ------------------------------------------------------------- reference outside-arena environment
        // v4 also proposed cube "rocks", capsule "spectators" and a score/coin HUD strip here.
        // Dropped: untextured primitives outside the arena and fake score chrome. The contact shadow
        // below is kept - it grounds the puck, which is part of the interaction, not decoration.
        BuildPuckContactShadow(puck, puckY, puckRadius);

        // ------------------------------------------------------------- camera grade
        // (the camera itself was framed at the top of this method, before anything was placed)
        // The flattest frame of the four: local contrast 0.084 against the reference's 0.157, and a 5th
        // luminance percentile of 0.475, meaning the frame contains no dark values at all. The grade
        // that closes that gap is mostly contrast, with a light vignette and a thresholded bloom so the
        // cyan reads as neon without the arena washing out to white.
        ReferenceLighting.Configure(scene, new Color(0.94f,0.97f,1.00f,1f), 1.16f, new Vector3(50f,-38f,0f),
            new Color(0.34f,0.38f,0.42f,1f), 0.46f, 0.28f);
        CaseGrade.Apply(scene, cam, "Case4_Buca", CaseGrade.Buca);

        if (cam.GetComponent<AudioListener>() == null)
        {
            cam.gameObject.AddComponent<AudioListener>();
            log.AppendLine("added AudioListener to " + cam.name + " (staged scene ships without one)");
        }

        // ------------------------------------------------------------- reference shot
        Vector3 puckStart = new Vector3(puckX, 0f, puckZ);
        // Aimed at the base of the TALL end of the staircase, not at its middle. The shot comes down
        // almost along the stack's depth, so it only ever engages the column it arrives at: aimed at
        // the middle it knocks out two one-high steps and the five-high column never hears about it.
        // Taking the tall column's base out drops five blocks onto their neighbours, and that chain is
        // what actually brings the stack down. The z offset puts the crossing inside the row rather
        // than on its near face.
        Vector3 stackAim = new Vector3(stackX0 + blockSize * 0.55f, 0f, stackZ - blockSize * 0.55f);
        Vector3 aimDir;
        List<Vector3> predicted;
        string solveNote = SolveReferenceShot(arena, puckStart, stackAim, puckRadius, out aimDir, out predicted);
        float pathLength = 0f;
        for (int i = 1; i < predicted.Count; i++) pathLength += Vector3.Distance(predicted[i - 1], predicted[i]);
        // The reference's flight is ~1.16s, not 0.32s. The 0.32 came from reading the puck's arrival
        // at the top of the arena (t=1.00s) as the impact. It is not the impact: the reference's stack
        // is still whole at t=1.80s -- its green mask reads 41778, exactly its resting value -- and is
        // shattered by t=2.10s, so the impact lands near t=1.85s. Against a release at t=0.69s that is
        // 1.16s of flight, and it is why our collapse, coins and colour story all ran ~0.45s early.
        //
        // The divisor is not literally the flight time: the puck's bounced path runs roughly 2.3x
        // pathLength, so it is calibrated on the output. Measured, 49.4 produced a 0.74s flight, and
        // flight scales inversely with launch speed because the puck flies with gravity off and zero
        // linear damping (only restitution changes its speed), so 49.4 * 0.74/1.16 = 31.5.
        // pathLength / 0.32 is only the geometric baseline: here it evaluates to 49.4, the speed that
        // was measured producing the 0.74s flight above. The retime factor below is what turns it into
        // the reference's 1.16s. Leaving it off is how a Build re-run silently reverts the retime.
        const float ReferenceFlightRetime = 0.74f / 1.16f;   // measured flight -> reference flight
        float launchSpeed = Mathf.Max(6f, pathLength / 0.32f * ReferenceFlightRetime);

        log.AppendLine("reference shot: " + solveNote);
        log.AppendLine("reference aim dir=" + aimDir.ToString("0.0000") +
                       " predictedLength=" + pathLength.ToString("0.##") +
                       " launchSpeed=" + launchSpeed.ToString("0.##"));
        StringBuilder pathLog = new StringBuilder();
        for (int i = 0; i < predicted.Count; i++) pathLog.Append(predicted[i].ToString("0.0")).Append(i < predicted.Count - 1 ? " -> " : "");
        log.AppendLine("predicted path: " + pathLog);

        // ------------------------------------------------------------- sequence object
        Transform seqTf = FindRoot(scene, SequenceName);
        GameObject seq = seqTf != null ? seqTf.gameObject : new GameObject(SequenceName);
        if (seqTf == null) SceneManager.MoveGameObjectToScene(seq, scene);
        seq.transform.position = Vector3.zero;

        Case4Director director = Ensure<Case4Director>(seq);
        PuckLauncher launcher = Ensure<PuckLauncher>(seq);
        GreenBlockShatter stack = Ensure<GreenBlockShatter>(seq);
        CoinArcStream coinStream = Ensure<CoinArcStream>(seq);
        Ensure<ReplayButton>(seq);

        director.wall.wallRenderers = new[] { frameRenderer };
        director.wall.neonMaterial = neonWall;

        launcher.puck = puck.transform;
        launcher.payout = coinStream;   // the ONLY thing that may arm the payout is a real contact
        launcher.starTrailPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(TrailVfxPath);
        launcher.referenceAimDir = aimDir;
        launcher.launchSpeed = launchSpeed;
        launcher.flightHeight = puckY;
        launcher.trailScale = unit * 0.70f;
        launcher.trailEmissionRate = 85f;
        launcher.trailLifetime = 0.30f;
        launcher.stretchAmount = 0.055f;
        launcher.bounceSquash = -0.055f;

        stack.greenNeonMaterial = greenNeon;
        stack.blockRoot = blocksRoot;
        stack.blocks = stackBlocks.ToArray();
        stack.blockSize = blockSize;
        stack.blockPitch = blockPitch;
        // P20 (lesson #4 again, one level deeper): GreenBlockShatter builds a runtime material instance
        // and writes blockBaseColor/blockRestEmission into it every frame, so editing GreenNeon.mat moved
        // nothing at all - round B measured the stack byte-for-byte unchanged at #17C728. The colour has
        // to be stated HERE. Reference measures #00FC00 over the flat face of the stack.
        stack.blockBaseColor = new Color(0.02f, 1f, 0.02f, 1f);
        stack.blockRestEmission = new Color(0.002f, 0.050f, 0.004f, 1f);

        coinStream.coinPrefab = puckPrefab;
        coinStream.coinMaterial = coinGold;
        // CORRECTION, and do not re-derive from the old number: the reference's full-size coin at
        // t=2.10 is 54.1 px across, NOT the 48.5 px this comment used to claim. Both 48.5 and the
        // even older 36.5 were medians taken over a population that includes the deliberately shrunk
        // pop-in and shrink-out coins at the two ends of the string, so both under-report the coins
        // that actually read as full size. The reference string is 21 coins over 1273 px of arc, a
        // mean gap of 63.6 px = 1.18 diameters at the corrected 54.1 px.
        //
        // Diameter tracks coinScale linearly. unit*0.715 (coinScale 0.51) captured at 43-46 px for
        // the full-size cluster, confirming the 44.6 px this line predicted, so the reference size is
        // 0.715 * 54.1/44.6 = 0.867.
        coinStream.coinScale = unit * 0.867f;         // 54.1 px on screen, matched to the reference
        coinStream.coinCount = 22;
        // Now that BuildCurve spaces the string by SCREEN distance, the gap is the same everywhere
        // along the arc and is simply (stagger / flightDuration) * arcPath - 1254 px at this rise.
        // Two bounds fix the rest: flightDuration 0.30 keeps the head of the string at screen 0.93 of
        // the arc at t=2.10, and 21 coins must be airborne by then, so the effective stagger is at
        // most 0.28/21. The runtime overshoots the authored stagger by about 3.5% (COIN_PACING
        // reports both), so 0.0129 authored lands 21 coins 55.8 px apart. Against the corrected
        // 54.1 px coin that is 1.03 diameters, not the 1.25 quoted here when the coin was 44.6 px,
        // and the reference reads 1.18. The gap is (stagger / flightDuration) * arcPath, so it is
        // flightDuration - not stagger - that is the free lever left for it.
        coinStream.stagger = 0.0129f;
        coinStream.flightDuration = 0.30f;
        // 0, not unit*19.6. The rise is what stopped the string from leaving at the top-right: at
        // 14 world units the arc balloons over the divider and crosses the TOP edge at viewport
        // x=0.58, mid-frame, however far right it is aimed. The reference's own path bows less than
        // 3 px over its 1242 px run; even at rise 0 the bezier's control points still bow ours about
        // 20 px, so this is not straighter than the reference, it is still slightly rounder.
        // (This builder is inert - SceneIsAuthored - but it must not contradict Buca.unity.)
        coinStream.arcRise = 0f;                      // ~1466 px of arc: gap 63.0 px = 1.16 diameters at 54.1 px

        PuckAimController aim = Ensure<PuckAimController>(seq);
        aim.director = director;
        aim.launcher = launcher;
        aim.targetCamera = cam;
        aim.aimLineMaterial = aimLineMat;
        aim.flightHeight = puckY;
        aim.grabRadius = unit * 5.0f;
        aim.maxPull = unit * 8.0f;
        aim.minPull = unit * 1.0f;
        // Both were hand-tuned in the scene well past what these lines produced; these now match it.
        // Width measured against the reference at t=0, across the lane interior: reference 49/54/64 px
        // at y=940/980/1020, ours 66/90/117. The 0.63 correction is carried here.
        aim.indicatorLength = unit * 10.51f;
        aim.indicatorWidth = unit * 1.822f;
        aim.stackAimPoint = stackAim;

        director.launcher = launcher;
        director.shatter = stack;
        director.coins = coinStream;
        // The coin stream leaves the collapse and runs up beside the divider, the way the reference
        // sends it toward the counter at the top of the screen.
        director.coinTarget = new Vector3(
            arena.DividerCenterX + unit * 1.4f,
            unit * 5.5f,
            arena.ArchInnerZ + unit * 1.2f);

        log.AppendLine("coin target " + director.coinTarget.ToString("0.##"));

        EditorUtility.SetDirty(director);
        EditorUtility.SetDirty(launcher);
        EditorUtility.SetDirty(stack);
        EditorUtility.SetDirty(coinStream);
        EditorUtility.SetDirty(aim);
        EditorUtility.SetDirty(seq);

        AssetDatabase.SaveAssets();
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);

        Debug.Log(log.ToString());
        Debug.Log(string.Format(
            "[Case4Setup] LAYOUT stackCenterX={0:0.###} puckStartX={1:0.###} stackIsLeft={2} holeObjects={3} blocks={4}",
            stackBounds.center.x, puckX, stackBounds.center.x < puckX, holesRemoved == 0 ? "none" : holesRemoved + " removed",
            stackBlocks.Count));
        Debug.Log("[Case4Setup] SETUP_OK blocks=" + stackBlocks.Count + " aimDir=" + aimDir.ToString("0.000"));
    }

    // ------------------------------------------------------------------ arena measurement

    /// <summary>
    /// Reads the arena out of the level_frame mesh instead of assuming it.
    ///
    /// The first attempt at this sampled vertices, and it measured nothing: the arena is low-poly, so
    /// a long straight rail carries vertices only at its two ends and a band taken through the middle
    /// of the arena came back empty. What is sampled here instead is the mesh's FOOTPRINT: every
    /// triangle is rasterised onto an XZ grid, which fills the solid parts whatever the tessellation
    /// is. A column that is solid through most of the straight lower half of the arena is a rail or
    /// the centre divider; the gaps between them are the two lanes.
    /// </summary>
    public static Arena MeasureArena(Renderer frameRenderer, StringBuilder log)
    {
        Arena a = new Arena();
        a.Bounds = frameRenderer.bounds;
        a.RimTopY = a.Bounds.center.y + a.Bounds.extents.y;

        MeshFilter mf = frameRenderer.GetComponent<MeshFilter>();
        log.AppendLine("arena bounds c=" + a.Bounds.center.ToString("0.###") + " e=" + a.Bounds.extents.ToString("0.###"));

        const int NX = 240;
        const int NZ = 300;
        float minX = a.Bounds.center.x - a.Bounds.extents.x;
        float maxX = a.Bounds.center.x + a.Bounds.extents.x;
        float minZ = a.Bounds.center.z - a.Bounds.extents.z;
        float maxZ = a.Bounds.center.z + a.Bounds.extents.z;

        bool[,] solid = Rasterize(mf, minX, maxX, minZ, maxZ, NX, NZ, log);

        // --- which columns are solid through the straight lower half of the arena?
        int rowLo = Mathf.RoundToInt(NZ * 0.15f);
        int rowHi = Mathf.RoundToInt(NZ * 0.55f);
        int rows = rowHi - rowLo;
        List<Vector2> xRuns = new List<Vector2>();
        int runStart = -1;
        for (int ix = 0; ix < NX; ix++)
        {
            int hit = 0;
            for (int iz = rowLo; iz < rowHi; iz++) if (solid[ix, iz]) hit++;
            bool isSolid = hit >= rows * 0.5f;
            if (isSolid && runStart < 0) runStart = ix;
            else if (!isSolid && runStart >= 0)
            {
                xRuns.Add(new Vector2(CellX(minX, maxX, NX, runStart), CellX(minX, maxX, NX, ix)));
                runStart = -1;
            }
        }
        if (runStart >= 0) xRuns.Add(new Vector2(CellX(minX, maxX, NX, runStart), maxX));
        log.AppendLine("solid x columns (through the straight lower half): " + RunsToString(xRuns));

        if (xRuns.Count >= 3)
        {
            a.LeftInnerX = xRuns[0].y;
            a.RightInnerX = xRuns[xRuns.Count - 1].x;
            int best = 1;
            float bestD = float.MaxValue;
            for (int i = 1; i < xRuns.Count - 1; i++)
            {
                float d = Mathf.Abs((xRuns[i].x + xRuns[i].y) * 0.5f - a.Bounds.center.x);
                if (d < bestD) { bestD = d; best = i; }
            }
            a.DividerMinX = xRuns[best].x;
            a.DividerMaxX = xRuns[best].y;
            a.HasDivider = true;
        }
        else if (xRuns.Count == 2)
        {
            a.LeftInnerX = xRuns[0].y;
            a.RightInnerX = xRuns[1].x;
            a.DividerMinX = a.DividerMaxX = a.Bounds.center.x;
            a.HasDivider = false;
            log.AppendLine("WARNING no centre divider found; the arena is treated as one open lane");
        }
        else
        {
            a.LeftInnerX = minX + a.Bounds.extents.x * 0.1f;
            a.RightInnerX = maxX - a.Bounds.extents.x * 0.1f;
            a.DividerMinX = a.DividerMaxX = a.Bounds.center.x;
            a.HasDivider = false;
            log.AppendLine("WARNING footprint produced " + xRuns.Count + " solid column run(s); falling back to bounds");
        }

        // --- bottom rail and arch, read out of the lane columns
        float bottomInner = minZ;
        float archApex = maxZ;
        bool measuredLane = false;
        for (int ix = 0; ix < NX; ix++)
        {
            float x = CellX(minX, maxX, NX, ix);
            bool inLane = (x > a.LeftInnerX && x < a.DividerMinX) || (x > a.DividerMaxX && x < a.RightInnerX);
            if (!inLane) continue;

            int firstTop = -1;   // top of the bottom rail in this column
            int lastBottom = -1; // bottom of the arch in this column
            bool seenGap = false;
            for (int iz = 0; iz < NZ; iz++)
            {
                if (solid[ix, iz]) { if (!seenGap) firstTop = iz; }
                else if (firstTop >= 0) seenGap = true;
            }
            for (int iz = NZ - 1; iz >= 0; iz--)
            {
                if (solid[ix, iz]) lastBottom = iz;
                else if (lastBottom >= 0) break;
            }
            if (firstTop < 0 || lastBottom < 0 || lastBottom <= firstTop) continue;

            bottomInner = Mathf.Max(bottomInner, CellZ(minZ, maxZ, NZ, firstTop + 1));
            archApex = Mathf.Min(archApex, CellZ(minZ, maxZ, NZ, lastBottom));
            if (!measuredLane) { bottomInner = CellZ(minZ, maxZ, NZ, firstTop + 1); archApex = CellZ(minZ, maxZ, NZ, lastBottom); }
            measuredLane = true;
        }
        // The arch is a curve, so its inner face sits furthest back at the apex. That apex is the plane
        // the shot bounces off, so take the deepest reading, not the shallowest.
        float apex = minZ;
        for (int ix = 0; ix < NX; ix++)
        {
            float x = CellX(minX, maxX, NX, ix);
            if (x <= a.LeftInnerX || x >= a.RightInnerX) continue;
            int lastBottom = -1;
            for (int iz = NZ - 1; iz >= 0; iz--)
            {
                if (solid[ix, iz]) lastBottom = iz;
                else if (lastBottom >= 0) break;
            }
            if (lastBottom > 0) apex = Mathf.Max(apex, CellZ(minZ, maxZ, NZ, lastBottom));
        }
        a.BottomInnerZ = measuredLane ? bottomInner : minZ + (maxZ - minZ) * 0.06f;
        a.ArchInnerZ = apex > a.BottomInnerZ ? apex : maxZ - (maxZ - minZ) * 0.06f;

        // --- divider height. Measured only after the arch is known: the divider's own columns also
        // pass through the arch at the top of the frame, so the naive "highest solid row in these
        // columns" reading returns the arch and reports a divider that touches the ceiling. What is
        // wanted is the highest solid row that is NOT part of the arch.
        if (a.HasDivider)
        {
            int cLo = Mathf.Clamp(CellIndex(minX, maxX, NX, a.DividerMinX) + 1, 0, NX - 1);
            int cHi = Mathf.Clamp(CellIndex(minX, maxX, NX, a.DividerMaxX) - 1, 0, NX - 1);
            if (cHi < cLo) cHi = cLo;

            float archFloor = a.ArchInnerZ - (maxZ - minZ) * 0.02f;
            int topRow = -1, botRow = NZ;
            for (int iz = 0; iz < NZ; iz++)
            {
                float z = CellZ(minZ, maxZ, NZ, iz);
                if (z >= archFloor) break;              // from here up it is the arch, not the divider
                bool any = false;
                for (int ix = cLo; ix <= cHi && !any; ix++) any = solid[ix, iz];
                if (!any) continue;
                if (iz > topRow) topRow = iz;
                if (iz < botRow) botRow = iz;
            }
            a.DividerMinZ = CellZ(minZ, maxZ, NZ, Mathf.Max(0, botRow));
            a.DividerMaxZ = CellZ(minZ, maxZ, NZ, Mathf.Max(0, topRow) + 1);
        }

        log.AppendLine(string.Format(
            "MEASURED leftInnerX={0:0.###} dividerX=[{1:0.###},{2:0.###}] rightInnerX={3:0.###} dividerZ=[{4:0.###},{5:0.###}] bottomInnerZ={6:0.###} archInnerZ={7:0.###} rimTopY={8:0.###}",
            a.LeftInnerX, a.DividerMinX, a.DividerMaxX, a.RightInnerX,
            a.DividerMinZ, a.DividerMaxZ, a.BottomInnerZ, a.ArchInnerZ, a.RimTopY));
        log.AppendLine(string.Format("lanes: left={0:0.###} right={1:0.###} gap above the divider={2:0.###}",
            a.LeftLaneWidth, a.RightLaneWidth, a.ArchInnerZ - a.DividerMaxZ));

        return a;
    }

    /// <summary>Marks every grid cell whose centre falls inside a mesh triangle projected onto XZ.</summary>
    static bool[,] Rasterize(MeshFilter mf, float minX, float maxX, float minZ, float maxZ,
                             int nx, int nz, StringBuilder log)
    {
        bool[,] solid = new bool[nx, nz];
        if (mf == null || mf.sharedMesh == null) return solid;

        Mesh mesh = mf.sharedMesh;
        Vector3[] verts = mesh.vertices;
        int[] tris = mesh.triangles;
        Transform t = mf.transform;

        Vector2[] flat = new Vector2[verts.Length];
        for (int i = 0; i < verts.Length; i++)
        {
            Vector3 w = t.TransformPoint(verts[i]);
            flat[i] = new Vector2(w.x, w.z);
        }

        float cw = (maxX - minX) / nx;
        float ch = (maxZ - minZ) / nz;
        int marked = 0;

        for (int i = 0; i + 2 < tris.Length; i += 3)
        {
            Vector2 p0 = flat[tris[i]], p1 = flat[tris[i + 1]], p2 = flat[tris[i + 2]];
            float area = (p1.x - p0.x) * (p2.y - p0.y) - (p2.x - p0.x) * (p1.y - p0.y);
            if (Mathf.Abs(area) < 1e-6f) continue;   // a vertical wall projects to a line

            int ix0 = Mathf.Clamp(Mathf.FloorToInt((Mathf.Min(p0.x, Mathf.Min(p1.x, p2.x)) - minX) / cw), 0, nx - 1);
            int ix1 = Mathf.Clamp(Mathf.CeilToInt((Mathf.Max(p0.x, Mathf.Max(p1.x, p2.x)) - minX) / cw), 0, nx - 1);
            int iz0 = Mathf.Clamp(Mathf.FloorToInt((Mathf.Min(p0.y, Mathf.Min(p1.y, p2.y)) - minZ) / ch), 0, nz - 1);
            int iz1 = Mathf.Clamp(Mathf.CeilToInt((Mathf.Max(p0.y, Mathf.Max(p1.y, p2.y)) - minZ) / ch), 0, nz - 1);

            for (int ix = ix0; ix <= ix1; ix++)
            {
                float px = minX + (ix + 0.5f) * cw;
                for (int iz = iz0; iz <= iz1; iz++)
                {
                    if (solid[ix, iz]) continue;
                    float pz = minZ + (iz + 0.5f) * ch;
                    if (!InTriangle(new Vector2(px, pz), p0, p1, p2, area)) continue;
                    solid[ix, iz] = true;
                    marked++;
                }
            }
        }

        log.AppendLine(string.Format("footprint rasterised: {0} tris, {1}/{2} cells solid ({3:0.0}%)",
            tris.Length / 3, marked, nx * nz, 100f * marked / (nx * nz)));
        return solid;
    }

    static bool InTriangle(Vector2 p, Vector2 a, Vector2 b, Vector2 c, float area)
    {
        float s = ((b.x - a.x) * (p.y - a.y) - (b.y - a.y) * (p.x - a.x)) / area;
        float tt = ((c.x - b.x) * (p.y - b.y) - (c.y - b.y) * (p.x - b.x)) / area;
        float u = ((a.x - c.x) * (p.y - c.y) - (a.y - c.y) * (p.x - c.x)) / area;
        return s >= 0f && tt >= 0f && u >= 0f;
    }

    static float CellX(float lo, float hi, int n, int i) { return lo + (hi - lo) * i / n; }
    static float CellZ(float lo, float hi, int n, int i) { return lo + (hi - lo) * i / n; }
    static int CellIndex(float lo, float hi, int n, float v)
    {
        return Mathf.Clamp(Mathf.FloorToInt((v - lo) / (hi - lo) * n), 0, n - 1);
    }

    static string RunsToString(List<Vector2> runs)
    {
        StringBuilder sb = new StringBuilder();
        for (int i = 0; i < runs.Count; i++)
            sb.Append('[').Append(runs[i].x.ToString("0.###")).Append(',').Append(runs[i].y.ToString("0.###")).Append("] ");
        return sb.Length == 0 ? "<none>" : sb.ToString();
    }

    // ------------------------------------------------------------------ stack

    static List<Transform> CollectStagedCubes(Scene scene)
    {
        List<Transform> found = new List<Transform>(24);

        Transform existing = FindRoot(scene, BlocksName);
        if (existing != null)
        {
            for (int i = 0; i < existing.childCount; i++) found.Add(existing.GetChild(i));
            if (found.Count > 0) return found;
        }

        GameObject[] roots = scene.GetRootGameObjects();
        for (int i = 0; i < roots.Length; i++)
        {
            if (!roots[i].name.StartsWith("Cube")) continue;
            if (roots[i].GetComponent<MeshFilter>() == null) continue;
            found.Add(roots[i].transform);
        }
        found.Sort((a, b) => string.CompareOrdinal(a.name, b.name));
        return found;
    }

    /// <summary>
    /// Lays the stack out in the LEFT lane, tallest column against the left rail, stepping down to the
    /// right - the reference silhouette. Positions carry a small deterministic jitter so the stack
    /// reads as hand-piled rather than snapped to a level editor grid; the offsets are well inside the
    /// support polygon, so the stack still stands still until it is hit.
    /// </summary>
    static List<Transform> LayOutStack(List<Transform> cubes, Transform root, Material greenNeon,
                                       float x0, float blockSize, float stackZ, StringBuilder log)
    {
        // The reference staircase needs 21 cubes; the staged scene ships 16. The shortfall is cloned
        // from a staged cube rather than faked with primitives, so every block in the pile is the same
        // mesh, material and collider the scene came with.
        int needed = 0;
        for (int i = 0; i < ColumnHeights.Length; i++) needed += ColumnHeights[i];
        int cloned = 0;
        while (cubes.Count < needed && cubes.Count > 0)
        {
            Transform src = cubes[0];
            GameObject copy = Object.Instantiate(src.gameObject);
            copy.name = "Cube_clone_" + cloned;
            SceneManager.MoveGameObjectToScene(copy, src.gameObject.scene);
            cubes.Add(copy.transform);
            cloned++;
        }
        if (cloned > 0) log.AppendLine("cloned " + cloned + " staged cube(s) to reach the reference's " + needed + "-block staircase");

        List<Transform> placed = new List<Transform>(cubes.Count);
        int index = 0;
        Random.State state = Random.state;
        Random.InitState(20260819);   // deterministic: the same stack every build, so a replay matches

        for (int col = 0; col < ColumnHeights.Length && index < cubes.Count; col++)
        {
            float x = x0 + col * blockSize;
            for (int row = 0; row < ColumnHeights[col] && index < cubes.Count; row++)
            {
                Transform b = cubes[index++];
                b.SetParent(root, true);
                // 2:1 bricks, long axis on Z = toward the camera. X and Y stay at blockSize so the
                // solved staircase width and the column heights are untouched by this change.
                b.localScale = new Vector3(blockSize, blockSize, blockSize * DepthRatio);
                float jx = Random.Range(-0.020f, 0.020f) * blockSize;
                // jz rides the LONG axis. A jitter tuned to the short axis is under-scaled on an axis
                // twice as long, and that reads as "the bricks do not settle right" without anyone
                // being able to name why.
                float jz = Random.Range(-0.020f, 0.020f) * blockSize * DepthRatio;
                float jyaw = Random.Range(-2.5f, 2.5f);

                b.rotation = Quaternion.Euler(0f, jyaw, 0f);
                b.position = new Vector3(x + jx, blockSize * (0.5f + row * 1.002f), stackZ + jz);

                Renderer r = b.GetComponent<Renderer>();
                if (r != null && greenNeon != null) r.sharedMaterial = greenNeon;

                // The collider is left at whatever Unity fitted to the mesh; forcing a unit box here
                // would be a guess about the staged mesh, and the staged mesh is the authority.
                BoxCollider bc = b.GetComponent<BoxCollider>();
                if (bc == null) bc = b.gameObject.AddComponent<BoxCollider>();
                bc.enabled = true;

                placed.Add(b);
            }
        }

        // Anything left over parks on the floor at the right-hand end of the stack rather than being
        // deleted, so nothing the scene shipped with silently disappears.
        while (index < cubes.Count)
        {
            Transform b = cubes[index++];
            b.SetParent(root, true);
            b.localScale = new Vector3(blockSize, blockSize, blockSize * DepthRatio);
            b.rotation = Quaternion.identity;
            b.position = new Vector3(x0 + ColumnHeights.Length * blockSize, blockSize * 0.5f, stackZ);
            Renderer r = b.GetComponent<Renderer>();
            if (r != null && greenNeon != null) r.sharedMaterial = greenNeon;
            BoxCollider bc = b.GetComponent<BoxCollider>();
            if (bc == null) bc = b.gameObject.AddComponent<BoxCollider>();
            bc.enabled = true;
            placed.Add(b);
        }

        Random.state = state;
        log.AppendLine(string.Format("stack: {0} columns, {1} blocks, size={2:0.###}, x0={3:0.###}, z={4:0.###}",
            ColumnHeights.Length, placed.Count, blockSize, x0, stackZ));
        return placed;
    }

    // ------------------------------------------------------------------ colliders

    /// <summary>
    /// The arena mesh ships with no collider at all, which is why nothing could ever bounce off it.
    /// These are the real rails: floor, four sides at the measured inner faces, and the centre divider
    /// with the gap at the top the shot has to thread.
    /// </summary>
    static void BuildColliders(Transform rig, Arena arena, float blockSize, StringBuilder log)
    {
        GameObject box = new GameObject("Case4_Colliders");
        box.transform.SetParent(rig, false);

        float h = blockSize * 8f;
        float t = blockSize * 1.2f;
        float cxMid = (arena.LeftInnerX + arena.RightInnerX) * 0.5f;
        float czMid = (arena.BottomInnerZ + arena.ArchInnerZ) * 0.5f;
        float w = arena.RightInnerX - arena.LeftInnerX;
        float d = arena.ArchInnerZ - arena.BottomInnerZ;

        AddBox(box.transform, "Floor", new Vector3(cxMid, -blockSize * 0.5f, czMid),
               new Vector3(w + t * 4f, blockSize, d + t * 4f));
        AddBox(box.transform, "Rail_Left", new Vector3(arena.LeftInnerX - t * 0.5f, h * 0.5f, czMid),
               new Vector3(t, h, d + t * 2f));
        AddBox(box.transform, "Rail_Right", new Vector3(arena.RightInnerX + t * 0.5f, h * 0.5f, czMid),
               new Vector3(t, h, d + t * 2f));
        AddBox(box.transform, "Rail_Bottom", new Vector3(cxMid, h * 0.5f, arena.BottomInnerZ - t * 0.5f),
               new Vector3(w + t * 2f, h, t));
        AddBox(box.transform, "Rail_Arch", new Vector3(cxMid, h * 0.5f, arena.ArchInnerZ + t * 0.5f),
               new Vector3(w + t * 2f, h, t));

        if (arena.HasDivider)
        {
            // The collider stops short of the divider's drawn top so the shot has a gap it can
            // actually fit through. Purely a collision detail; the mesh is untouched.
            float gap = arena.ArchInnerZ - arena.DividerMaxZ;
            float need = blockSize * 2.2f;
            float top = arena.DividerMaxZ;
            if (gap < need) top = arena.ArchInnerZ - need;
            float dz = Mathf.Max(blockSize * 0.5f, top - arena.DividerMinZ);

            AddBox(box.transform, "Divider",
                   new Vector3(arena.DividerCenterX, h * 0.5f, arena.DividerMinZ + dz * 0.5f),
                   new Vector3(arena.DividerMaxX - arena.DividerMinX, h, dz));

            log.AppendLine(string.Format("divider collider: z=[{0:0.###},{1:0.###}] drawn top={2:0.###} gap {3:0.###} -> {4:0.###}",
                arena.DividerMinZ, arena.DividerMinZ + dz, arena.DividerMaxZ, gap, arena.ArchInnerZ - (arena.DividerMinZ + dz)));
        }

        log.AppendLine(string.Format("colliders: arena x[{0:0.##},{1:0.##}] z[{2:0.##},{3:0.##}] wallHeight={4:0.##}",
            arena.LeftInnerX, arena.RightInnerX, arena.BottomInnerZ, arena.ArchInnerZ, h));
    }

    static void AddBox(Transform parent, string name, Vector3 center, Vector3 size)
    {
        GameObject go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.transform.position = center;
        BoxCollider c = go.AddComponent<BoxCollider>();
        c.size = size;
    }

    static int RemoveHoles(Scene scene, StringBuilder log)
    {
        int removed = 0;
        GameObject[] roots = scene.GetRootGameObjects();
        for (int i = 0; i < roots.Length; i++)
        {
            if (roots[i] == null) continue;
            string n = roots[i].name.ToLowerInvariant();
            if (n.Contains("hole") && !n.Contains("obstacle"))
            {
                log.AppendLine("removed leftover hole object: " + roots[i].name);
                Object.DestroyImmediate(roots[i]);
                removed++;
            }
        }
        return removed;
    }

    // ------------------------------------------------------------------ reference shot

    /// <summary>
    /// Solves the launch direction that reproduces the reference flow: up the right lane, over the
    /// divider through the gap at the arch, off the arch, down the left lane into the stack. Uses the
    /// mirror method - the target is reflected back through each rail in reverse order - and then
    /// checks the resulting straight line really clears the divider before it is accepted.
    /// </summary>
    static string SolveReferenceShot(Arena arena, Vector3 start, Vector3 target, float puckRadius,
                                     out Vector3 dir, out List<Vector3> path)
    {
        float rx = arena.RightInnerX - puckRadius;
        float az = arena.ArchInnerZ - puckRadius;

        // two bounces: right rail, then arch
        Vector3 t1 = new Vector3(target.x, 0f, 2f * az - target.z);
        Vector3 t2 = new Vector3(2f * rx - t1.x, 0f, t1.z);
        Vector3 d2 = (t2 - start); d2.y = 0f;
        if (d2.sqrMagnitude > 0.0001f)
        {
            d2.Normalize();
            if (TryPath(arena, start, d2, puckRadius, target, 2, out path)) { dir = d2; return "two-bounce (right rail -> arch)"; }
        }

        // one bounce: arch only
        Vector3 d1 = (t1 - start); d1.y = 0f;
        if (d1.sqrMagnitude > 0.0001f)
        {
            d1.Normalize();
            if (TryPath(arena, start, d1, puckRadius, target, 1, out path)) { dir = d1; return "one-bounce (arch only)"; }
        }

        // Last resort: aim through the gap above the divider and let the arch send it left.
        Vector3 gate = new Vector3(arena.DividerCenterX - puckRadius,
                                   0f,
                                   Mathf.Lerp(arena.DividerMaxZ, arena.ArchInnerZ, 0.55f));
        Vector3 d0 = gate - start; d0.y = 0f;
        dir = d0.sqrMagnitude > 0.0001f ? d0.normalized : Vector3.forward;
        TryPath(arena, start, dir, puckRadius, target, 1, out path);
        return "FALLBACK aimed straight at the divider gap; the mirror solutions did not clear the divider";
    }

    /// <summary>
    /// Walks a straight shot through the arena reflecting off the rails, and reports whether it
    /// behaved: it must clear the divider on the way over and end up in the left lane.
    /// </summary>
    static bool TryPath(Arena arena, Vector3 start, Vector3 dir, float puckRadius, Vector3 target,
                        int expectedBounces, out List<Vector3> path)
    {
        path = new List<Vector3>(6);
        path.Add(start);

        float minX = arena.LeftInnerX + puckRadius;
        float maxX = arena.RightInnerX - puckRadius;
        float minZ = arena.BottomInnerZ + puckRadius;
        float maxZ = arena.ArchInnerZ - puckRadius;

        Vector3 p = start;
        Vector3 d = dir;
        bool clearedDivider = !arena.HasDivider;
        float dividerLo = arena.DividerMinX - puckRadius;
        float dividerHi = arena.DividerMaxX + puckRadius;
        float dividerTop = arena.DividerMaxZ;

        for (int b = 0; b <= expectedBounces + 1; b++)
        {
            // nearest rail
            float best = float.MaxValue;
            Vector3 n = Vector3.zero;
            if (d.x > 1e-4f) { float t = (maxX - p.x) / d.x; if (t > 1e-3f && t < best) { best = t; n = Vector3.left; } }
            if (d.x < -1e-4f) { float t = (minX - p.x) / d.x; if (t > 1e-3f && t < best) { best = t; n = Vector3.right; } }
            if (d.z > 1e-4f) { float t = (maxZ - p.z) / d.z; if (t > 1e-3f && t < best) { best = t; n = Vector3.back; } }
            if (d.z < -1e-4f) { float t = (minZ - p.z) / d.z; if (t > 1e-3f && t < best) { best = t; n = Vector3.forward; } }
            if (best == float.MaxValue) return false;

            Vector3 next = p + d * best;

            // Does this leg cross the divider band, and if so, does it pass above the drawn top?
            if (arena.HasDivider && Mathf.Abs(d.x) > 1e-4f)
            {
                float[] gates = { dividerLo, dividerHi };
                for (int g = 0; g < 2; g++)
                {
                    float t = (gates[g] - p.x) / d.x;
                    if (t <= 1e-3f || t >= best) continue;
                    float z = p.z + d.z * t;
                    if (z <= dividerTop) return false;      // it would smack into the divider
                    clearedDivider = true;
                }
            }

            path.Add(next);

            // Have we reached the stack's lane heading down? Then this is the last leg.
            if (clearedDivider && next.x < dividerLo && d.z < 0f)
            {
                path[path.Count - 1] = new Vector3(target.x, 0f, target.z);
                return true;
            }
            if (clearedDivider && Vector3.Distance(new Vector3(next.x, 0f, next.z), new Vector3(target.x, 0f, target.z)) < puckRadius * 4f)
                return true;

            p = next;
            d = Vector3.Reflect(d, n);
        }
        return false;
    }

    // ------------------------------------------------------------------ environment / grounding

    static void BuildPuckContactShadow(GameObject puck, float puckY, float puckRadius)
    {
        if(puck==null)return;
        Transform old=puck.transform.Find("ReferenceContactShadow"); if(old!=null)Object.DestroyImmediate(old.gameObject);
        GameObject sh=GameObject.CreatePrimitive(PrimitiveType.Cylinder); sh.name="ReferenceContactShadow"; sh.transform.SetParent(puck.transform,false);
        sh.transform.localPosition=new Vector3(0f,-puckY/Mathf.Max(.001f,puck.transform.localScale.y)+.015f,0f);
        sh.transform.localScale=new Vector3(puckRadius*1.35f,.008f,puckRadius*.82f);
        Collider c=sh.GetComponent<Collider>(); if(c!=null)Object.DestroyImmediate(c);
        Renderer r=sh.GetComponent<Renderer>(); if(r!=null)
        {
            Material m=EnsureMaterial(MaterialDir+"/Case4_PuckShadow.mat","Universal Render Pipeline/Unlit",mat=>
            {
                // A tiny opaque contact patch is safer than a half-configured transparent URP material
                // (surface flag alone does not configure blend factors). At this size it reads as soft grounding.
                if(mat.HasProperty("_BaseColor"))mat.SetColor("_BaseColor",new Color(.055f,.065f,.075f,1f));
            });
            r.sharedMaterial=m;
        }
    }

    // ------------------------------------------------------------------ camera

    /// <summary>
    /// Pins the camera to the reference 0.625 frame whatever the screen is. Rebuilt rather than reused
    /// so the source stays the single authority over the serialised scene value (lesson #4).
    /// </summary>
    static void EnsureAspectEnforcer(Camera cam)
    {
        Shared.View.AspectRatioEnforcer[] existing = cam.GetComponents<Shared.View.AspectRatioEnforcer>();
        for (int i = 0; i < existing.Length; i++) Object.DestroyImmediate(existing[i]);
        cam.gameObject.AddComponent<Shared.View.AspectRatioEnforcer>();
        EditorUtility.SetDirty(cam.gameObject);
        Debug.Log("[AspectEnforcer] added to " + cam.name + " (target aspect " +
                  Shared.View.AspectRatioEnforcer.TargetAspect.ToString("0.000") + ")");
    }

    /// <summary>
    /// Projected viewport bounding box of a mesh's real vertices, measured at the reference aspect.
    /// This is the object's silhouette box, which is what the reference video can be measured as -
    /// not a world AABB, which reaches past the silhouette on a curved arch.
    /// </summary>
    public static bool ViewportBox(Camera cam, IList<Vector3> worldPoints,
                                   out float x0, out float x1, out float y0, out float y1)
    {
        x0 = y0 = float.MaxValue; x1 = y1 = float.MinValue;
        if (cam == null || worldPoints == null || worldPoints.Count == 0) return false;
        float previous = cam.aspect;
        cam.aspect = Shared.View.AspectRatioEnforcer.TargetAspect;
        for (int i = 0; i < worldPoints.Count; i++)
        {
            Vector3 v = cam.WorldToViewportPoint(worldPoints[i]);
            if (v.z <= 0f) continue;
            x0 = Mathf.Min(x0, v.x); x1 = Mathf.Max(x1, v.x);
            y0 = Mathf.Min(y0, v.y); y1 = Mathf.Max(y1, v.y);
        }
        cam.aspect = previous;
        cam.ResetAspect();
        return x1 > x0;
    }

    /// <summary>The eight corners of the box the stack would occupy at a given block size.</summary>
    static List<Vector3> StackCorners(float x0, float blockSize, float stackZ)
    {
        int cols = ColumnHeights.Length;
        int tallest = 0;
        for (int i = 0; i < cols; i++) tallest = Mathf.Max(tallest, ColumnHeights[i]);

        float minX = x0 - blockSize * 0.5f;
        float maxX = x0 + (cols - 1) * blockSize + blockSize * 0.5f;
        float minY = 0f;
        float maxY = blockSize * tallest;
        // Derived from DepthRatio rather than passed in: it is a compile-time const, so threading it
        // through five call sites would add five typo sites and no information. Computing it here
        // means every caller - including any added later - solves against the real footprint. This
        // matters because SolveStackBlockSize bisects blockSize until THIS box projects to the
        // reference's 0.257 frame width; left at the cube's half-depth it would have been solving
        // honestly for a shape the scene no longer has, and the far corners project differently
        // toward the vanishing point.
        float halfDepth = blockSize * DepthRatio * 0.5f;
        float minZ = stackZ - halfDepth;
        float maxZ = stackZ + halfDepth;

        List<Vector3> c = new List<Vector3>(8);
        for (int i = 0; i < 8; i++)
            c.Add(new Vector3((i & 1) == 0 ? minX : maxX, (i & 2) == 0 ? minY : maxY, (i & 4) == 0 ? minZ : maxZ));
        return c;
    }

    /// <summary>
    /// Finds the block size whose six-column staircase projects to the width the reference's six-column
    /// staircase projects to (0.257 of the frame). Bisection on the projected box, not a world guess:
    /// the arena is seen at 41 degrees, so a world width and a screen width are not proportional and a
    /// hand-tuned constant would only be right for one camera.
    /// </summary>
    static float SolveStackBlockSize(Camera cam, Arena arena, float stackZ, float unit, StringBuilder log)
    {
        const float target = RefStackX1 - RefStackX0;

        float lo = unit * 0.35f, hi = unit * 1.60f;
        float best = unit, bestErr = float.MaxValue;
        for (int i = 0; i < 48; i++)
        {
            float mid = (lo + hi) * 0.5f;
            float x0, x1, y0, y1;
            if (!ViewportBox(cam, StackCorners(arena.LeftInnerX + mid * 0.55f, mid, StackZFor(arena.BottomInnerZ, mid)),
                             out x0, out x1, out y0, out y1))
                break;
            float w = x1 - x0;
            float err = Mathf.Abs(w - target);
            if (err < bestErr) { bestErr = err; best = mid; }
            if (w > target) hi = mid; else lo = mid;
        }

        // The stack still has to fit its lane with air on the right, whatever the screen wants.
        float cap = arena.LeftLaneWidth / (ColumnHeights.Length + 0.95f) * 1.35f;
        float chosen = Mathf.Clamp(best, unit * 0.35f, cap);

        float fx0, fx1, fy0, fy1;
        ViewportBox(cam, StackCorners(arena.LeftInnerX + chosen * 0.55f, chosen, StackZFor(arena.BottomInnerZ, chosen)),
                    out fx0, out fx1, out fy0, out fy1);
        log.AppendLine(string.Format(
            "stack size solved: blockSize={0:0.####} (unit {1:0.####}, x{2:0.000}) -> viewport x[{3:0.000}..{4:0.000}] y[{5:0.000}..{6:0.000}]  " +
            "reference x[{7:0.000}..{8:0.000}] y[{9:0.000}..{10:0.000}]  dWidth={11:+0.000;-0.000}",
            chosen, unit, chosen / unit, fx0, fx1, fy0, fy1,
            RefStackX0, RefStackX1, RefStackY0, RefStackY1, (fx1 - fx0) - target));
        return chosen;
    }

    /// <summary>
    /// Slides the whole staircase along x until its projected LEFT edge is the reference's 0.087.
    /// Measured and corrected rather than derived from the rail, because the rail is not vertical: the
    /// arena's left wall leans, so "one block off the inner face" lands somewhere different on screen
    /// depending on how big the block is.
    /// </summary>
    static float SolveStackX0(Camera cam, Arena arena, float blockSize, float stackZ, StringBuilder log)
    {
        float x0 = arena.LeftInnerX + blockSize * 0.55f;
        float lx = 0f, rx = 0f, by = 0f, ty = 0f;

        for (int i = 0; i < 24; i++)
        {
            if (!ViewportBox(cam, StackCorners(x0, blockSize, stackZ), out lx, out rx, out by, out ty)) break;
            float err = lx - RefStackX0;
            if (Mathf.Abs(err) < 0.0008f) break;
            // one probe to get the local world-per-viewport scale, then step
            float probe = blockSize * 0.5f;
            float lx2, rx2, by2, ty2;
            if (!ViewportBox(cam, StackCorners(x0 + probe, blockSize, stackZ), out lx2, out rx2, out by2, out ty2)) break;
            float slope = (lx2 - lx) / probe;
            if (Mathf.Abs(slope) < 1e-6f) break;
            x0 -= err / slope;
        }

        // Never let the correction push the pile through the left rail or across the divider.
        float lo = arena.LeftInnerX + blockSize * 0.5f;
        float hi = arena.DividerMinX - (ColumnHeights.Length - 0.5f) * blockSize;
        float clamped = Mathf.Clamp(x0, lo, Mathf.Max(lo, hi));

        ViewportBox(cam, StackCorners(clamped, blockSize, stackZ), out lx, out rx, out by, out ty);
        log.AppendLine(string.Format(
            "stack x solved: x0={0:0.###} (staged formula gave {1:0.###}) -> viewport x[{2:0.000}..{3:0.000}] y[{4:0.000}..{5:0.000}]  " +
            "reference x[{6:0.000}..{7:0.000}] y[{8:0.000}..{9:0.000}]  d=({10:+0.000;-0.000},{11:+0.000;-0.000},{12:+0.000;-0.000},{13:+0.000;-0.000})",
            clamped, arena.LeftInnerX + blockSize * 0.55f, lx, rx, by, ty,
            RefStackX0, RefStackX1, RefStackY0, RefStackY1,
            lx - RefStackX0, rx - RefStackX1, by - RefStackY0, ty - RefStackY1));
        return clamped;
    }

    /// <summary>
    /// Puts the puck where the reference puts it on screen: cast the reference viewport point through
    /// the camera onto the puck's own flight plane, then clamp the result into the right-hand lane.
    /// </summary>
    static void SolvePuckRest(Camera cam, Arena arena, float puckY, float puckRadius,
                              out float puckX, out float puckZ, StringBuilder log)
    {
        float previous = cam.aspect;
        cam.aspect = Shared.View.AspectRatioEnforcer.TargetAspect;
        Ray ray = cam.ViewportPointToRay(new Vector3(RefPuckX, RefPuckY, 0f));
        cam.aspect = previous;
        cam.ResetAspect();

        float wantX, wantZ;
        if (Mathf.Abs(ray.direction.y) < 1e-4f)
        {
            wantX = arena.RightLaneCenterX;
            wantZ = arena.BottomInnerZ + puckRadius * 3.8f;
            log.AppendLine("WARNING puck ray is parallel to the floor; fell back to the lane centre");
        }
        else
        {
            float t = (puckY - ray.origin.y) / ray.direction.y;
            Vector3 hit = ray.GetPoint(t);
            wantX = hit.x;
            wantZ = hit.z;
        }

        float margin = puckRadius * 1.35f;
        puckX = Mathf.Clamp(wantX, arena.DividerMaxX + margin, arena.RightInnerX - margin);
        puckZ = Mathf.Clamp(wantZ - 0.40f, arena.BottomInnerZ + margin, arena.ArchInnerZ - margin);

        float previous2 = cam.aspect;
        cam.aspect = Shared.View.AspectRatioEnforcer.TargetAspect;
        Vector3 got = cam.WorldToViewportPoint(new Vector3(puckX, puckY, puckZ));
        cam.aspect = previous2;
        cam.ResetAspect();

        log.AppendLine(string.Format(
            "puck rest solved: world ({0:0.###},{1:0.###}) -> viewport ({2:0.000},{3:0.000})  reference ({4:0.000},{5:0.000})  " +
            "d=({6:+0.000;-0.000},{7:+0.000;-0.000})  lane [{8:0.##}..{9:0.##}] clamped={10}",
            puckX, puckZ, got.x, got.y, RefPuckX, RefPuckY, got.x - RefPuckX, got.y - RefPuckY,
            arena.DividerMaxX + margin, arena.RightInnerX - margin,
            !Mathf.Approximately(puckX, wantX) || !Mathf.Approximately(puckZ, wantZ)));
    }

    static void FrameCamera(Camera cam, Bounds arena, Renderer frameRenderer, StringBuilder log)
    {
        // The pitch and the perspective convergence are kept from the measurement the earlier package
        // made off the reference (the rails are not parallel, so an orthographic camera can never
        // produce this silhouette). What is NEW here is that the framing is no longer computed from a
        // world AABB and then hoped for: the arena's real mesh is projected, its silhouette box is
        // compared with the reference's, and the camera is moved until the two agree. That closes the
        // gap the AABB left - the arena's projected centre sat 0.046 of the frame below the
        // reference's, which is exactly the "objects are in the wrong place" the review reported.
        const float splay = 1.40f;          // near rim width / far rim width across the full arena depth
        const float pitchDegrees = 41.456f; // the staged pitch, which measurement confirmed rather than moved
        const float aspect = 1080f / 1728f;

        cam.transform.rotation = Quaternion.Euler(pitchDegrees, 0f, 0f);

        float pitch = pitchDegrees * Mathf.Deg2Rad;
        if (Mathf.Abs(Mathf.Sin(pitch)) < 0.05f) { log.AppendLine("camera pitch too shallow, framing skipped"); return; }

        bool beforeOrtho = cam.orthographic;
        Vector3 beforePos = cam.transform.position;

        Vector3 forward = new Vector3(0f, -Mathf.Sin(pitch), Mathf.Cos(pitch));
        Vector3 up = new Vector3(0f, Mathf.Cos(pitch), Mathf.Sin(pitch));
        Vector3 right = Vector3.right;

        float h = arena.extents.z * Mathf.Cos(pitch);
        float distance = h * (splay + 1f) / (splay - 1f);
        float nearDistance = distance - h;

        // Opening pose: the reference fill at the near rim, as before. The loop below then corrects it
        // against the real silhouette.
        float tanHalfV = (arena.extents.x * 2f) / (0.935f * 2f * nearDistance * aspect);

        cam.orthographic = false;
        cam.fieldOfView = 2f * Mathf.Atan(tanHalfV) * Mathf.Rad2Deg;
        cam.transform.position = arena.center - forward * distance;
        cam.nearClipPlane = Mathf.Max(0.1f, nearDistance * 0.25f);
        cam.farClipPlane = Mathf.Max(cam.farClipPlane, distance * 4f);

        List<Vector3> silhouette = WorldVertices(frameRenderer != null ? frameRenderer.GetComponent<MeshFilter>() : null);
        if (silhouette.Count == 0)
        {
            log.AppendLine("WARNING level_frame has no readable mesh; camera left at the opening pose");
        }
        else
        {
            const float targetW = RefRimX1 - RefRimX0;
            const float targetCX = (RefRimX0 + RefRimX1) * 0.5f;
            const float targetCY = (RefRimY0 + RefRimY1) * 0.5f;

            float x0 = 0f, x1 = 0f, y0 = 0f, y1 = 0f;
            for (int iter = 0; iter < 40; iter++)
            {
                if (!ViewportBox(cam, silhouette, out x0, out x1, out y0, out y1)) break;

                float w = x1 - x0;
                float cx = (x0 + x1) * 0.5f;
                float cy = (y0 + y1) * 0.5f;

                // Widen or narrow the frustum until the silhouette is the reference's width.
                tanHalfV *= w / targetW;
                cam.fieldOfView = Mathf.Clamp(2f * Mathf.Atan(tanHalfV) * Mathf.Rad2Deg, 1f, 120f);
                tanHalfV = Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad);

                // Then slide the camera in its own screen plane until the silhouette is centred where
                // the reference centres it. Moving the camera +right moves the image -x, hence the sign.
                float frameH = 2f * distance * tanHalfV;
                float frameW = frameH * aspect;
                cam.transform.position -= right * ((targetCX - cx) * frameW) + up * ((targetCY - cy) * frameH);

                if (Mathf.Abs(w - targetW) < 0.0015f &&
                    Mathf.Abs(cx - targetCX) < 0.0015f &&
                    Mathf.Abs(cy - targetCY) < 0.0015f) break;
            }

            ViewportBox(cam, silhouette, out x0, out x1, out y0, out y1);
            log.AppendLine(string.Format(
                "rim silhouette solved: x[{0:0.000}..{1:0.000}] y[{2:0.000}..{3:0.000}]  " +
                "reference x[{4:0.000}..{5:0.000}] y[{6:0.000}..{7:0.000}]  dx0={8:+0.000;-0.000} dx1={9:+0.000;-0.000} dy0={10:+0.000;-0.000} dy1={11:+0.000;-0.000}",
                x0, x1, y0, y1, RefRimX0, RefRimX1, RefRimY0, RefRimY1,
                x0 - RefRimX0, x1 - RefRimX1, y0 - RefRimY0, y1 - RefRimY1));
        }

        // The wider perspective frustum sees past the edge of the ground plane in the top corner, where
        // the default clear showed a warm grey wedge. Clearing to the lit ground's own colour makes that
        // edge invisible instead of moving the plane, which is scene layout and not this package's.
        cam.clearFlags = CameraClearFlags.SolidColor;
        // P20: kept in step with the floor above, same measured ratio, so the horizon does not part company
        // with the ground it continues.
        cam.backgroundColor = new Color(0.343f, 0.397f, 0.441f, 1f);

        EnsureAspectEnforcer(cam);

        log.AppendLine(string.Format(
            "camera reframed: perspective fov={0:0.00} distance={1:0.##} splay={2:0.00} pitch={3:0.###} deg, " +
            "was ortho={4}, pos {5} -> {6}",
            cam.fieldOfView, distance, splay, pitchDegrees, beforeOrtho,
            beforePos.ToString("0.##"), cam.transform.position.ToString("0.##")));
    }

    // ------------------------------------------------------------------ helpers

    static List<Vector3> WorldVertices(MeshFilter mf)
    {
        List<Vector3> outv = new List<Vector3>(2048);
        if (mf == null || mf.sharedMesh == null) return outv;
        Vector3[] verts = mf.sharedMesh.vertices;
        Transform t = mf.transform;
        for (int i = 0; i < verts.Length; i++) outv.Add(t.TransformPoint(verts[i]));
        return outv;
    }

    /// <summary>
    /// Replaces the component with a fresh one. Values already serialised into the scene survive a
    /// change to the field initialiser in code, so the scene and the source would quietly disagree
    /// about the tuning. Rebuilding makes the source the single authority.
    /// </summary>
    static T Ensure<T>(GameObject go) where T : Component
    {
        T[] existing = go.GetComponents<T>();
        for (int i = 0; i < existing.Length; i++) Object.DestroyImmediate(existing[i]);
        return go.AddComponent<T>();
    }

    static Transform FindRoot(Scene scene, string name)
    {
        GameObject[] roots = scene.GetRootGameObjects();
        for (int i = 0; i < roots.Length; i++) if (roots[i].name == name) return roots[i].transform;
        return null;
    }

    static Renderer FindChildRenderer(Transform root, string name)
    {
        Renderer[] all = root.GetComponentsInChildren<Renderer>(true);
        for (int i = 0; i < all.Length; i++) if (all[i].name == name) return all[i];
        return null;
    }

    static Transform FindChildTransform(Transform root, string name)
    {
        Transform[] all = root.GetComponentsInChildren<Transform>(true);
        for (int i = 0; i < all.Length; i++) if (all[i].name == name) return all[i];
        return null;
    }

    static Material EnsureMaterial(string path, string shaderName, System.Action<Material> configure)
    {
        Shader shader = Shader.Find(shaderName);
        if (shader == null)
        {
            Debug.LogError("[Case4Setup] Shader not found: " + shaderName);
            return null;
        }

        Material m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (m == null)
        {
            m = new Material(shader);
            AssetDatabase.CreateAsset(m, path);
        }
        else if (m.shader != shader)
        {
            m.shader = shader;
        }

        configure(m);
        EditorUtility.SetDirty(m);
        return m;
    }

    static void Fail(string message)
    {
        Debug.LogError("[Case4Setup] SETUP_FAILED " + message);
        if (Application.isBatchMode) throw new System.InvalidOperationException(message);
    }
}

/// <summary>
/// Play-mode driver behind <see cref="Case4SceneSetup.LayoutGate"/>. Same shape as the input gate:
/// open the authored scene, enter play mode, attach the probe, exit non-zero on an assertion failure.
/// </summary>
[InitializeOnLoad]
public static class Case4LayoutGateDriver
{
    const string ScenePath = "Assets/Case4_Buca/Scenes/Buca.unity";
    const string KeyActive = "Case4LayoutGate.Active";
    const double Timeout = 240.0;

    static bool _hooked;
    static double _start;
    static bool _attached;

    static Case4LayoutGateDriver()
    {
        if (SessionState.GetInt(KeyActive, 0) == 1) Hook();
    }

    /// <summary>Entry point.</summary>
    public static void Run()
    {
        SessionState.SetInt(KeyActive, 1);
        EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        Log("authored scene opened " + ScenePath);
        Hook();
        _start = EditorApplication.timeSinceStartup;
        EditorApplication.EnterPlaymode();
    }

    static void Hook()
    {
        if (_hooked) return;
        _hooked = true;
        _start = EditorApplication.timeSinceStartup;
        EditorApplication.update += Drive;
    }

    static void Drive()
    {
        if (SessionState.GetInt(KeyActive, 0) != 1) return;
        if (!EditorApplication.isPlaying) return;

        if (EditorApplication.timeSinceStartup - _start > Timeout)
        {
            Finish(false, "TIMEOUT after " + Timeout + "s");
            return;
        }

        if (!_attached)
        {
            Case4Director director = Object.FindFirstObjectByType<Case4Director>(FindObjectsInactive.Include);
            if (director == null) return;
            director.gameObject.AddComponent<Case4InputProbe>();
            _attached = true;
            Log("probe attached");
            return;
        }

        if (!Case4InputProbe.Finished) return;
        Finish(Case4InputProbe.Passed, Case4InputProbe.Transcript);
    }

    static void Finish(bool passed, string transcript)
    {
        SessionState.SetInt(KeyActive, 0);
        EditorApplication.update -= Drive;
        _hooked = false;

        Log("---- transcript ----\n" + transcript);
        Log(passed ? "CASE4_LAYOUT_GATE_OK" : "CASE4_LAYOUT_GATE_FAILED");

        if (Application.isBatchMode) EditorApplication.Exit(passed ? 0 : 1);
        else EditorApplication.isPlaying = false;
    }

    static void Log(string s)
    {
        Debug.Log("[Case4LayoutGate] " + s);
        System.Console.WriteLine("[Case4LayoutGate] " + s);
    }
}
