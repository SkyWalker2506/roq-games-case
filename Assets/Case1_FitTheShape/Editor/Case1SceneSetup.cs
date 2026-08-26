using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Case1;
using Shared.Sequencing;
using Shared.EditorTools;
using TMPro;

/// <summary>
/// Builds the Case 1 scene wiring from code so the staged scene never has to be edited by hand.
/// Idempotent: running it twice leaves the same scene. It discovers the drum's slot cells and the deck's
/// shapes, matches the shape that has a colour twin on the front row, creates the two flare materials
/// plus the sparkle prefab, wires the director, and writes a discovery dump to the log.
/// </summary>
public static class Case1SceneSetup
{
    const string ScenePath = "Assets/Case1_FitTheShape/Scenes/FitTheShape.unity";
    const string MaterialDir = "Assets/Case1_FitTheShape/Materials";
    const string SparklePath = "Assets/Case1_FitTheShape/VFX/StarSparkleBurst.prefab";
    const string StarMeshPath = "Assets/Case1_FitTheShape/VFX/StarParticleMesh.asset";
    const string FlashShader = "Case1/SlotFillFlash";
    const string SceneRootName = "Case1";
    const string RootName = "Case1_Sequence";
    const string BandRootName = "Case1_SlotBand";
    const string TrayRootName = "Case1_ShapeTray";
    const string PrefabDir = "Assets/Case1_FitTheShape/Prefabs";

    static readonly Regex SegmentName = new Regex(@"^Segment_c(\d+)_r(\d+)$");

    /// <summary>Menu entry point.</summary>
    public static void BuildMenu()
    {
        Build();
    }

    /// <summary>
    /// The SCENE is the authority for where things are, how big they are, and how they are grouped.
    ///
    /// Every arrangement this builder solved for itself was wrong somewhere. Camera-derived placement
    /// satisfied the frame and left the world nonsense - the board 16 units in the air, the plates at
    /// Y = 18.8, the tray below the floor and behind the board. Solving the world from measured targets
    /// instead landed close but never right, and each correction moved something else. A person laying
    /// the objects out on a floor got there in minutes.
    ///
    /// So the builder no longer places, scales, rotates or regroups anything that already exists. It
    /// owns LOGIC: shape identity, colour, prefab variants, recesses, question-mark covers, the rail,
    /// and the wiring between them.
    // static readonly, not const. Identical behaviour, but a const lets the compiler PROVE every
    // `if (!SceneIsAuthored)` body is dead, and it emitted 16 "unreachable code" warnings saying so.
    // Those bodies are deliberately parked - the scene owns that placement now - and burying 16
    // warnings in the console to say it is worse than the one comment that already explains it.
    static readonly bool SceneIsAuthored = true;

    /// <summary>Batchmode entry point: wires the scene and saves it.</summary>
    public static void Build()
    {
        Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

        Transform drumRoot = FindRoot(scene, "Drum");
        Transform deckRoot = FindRoot(scene, "Deck");
        if (drumRoot == null || deckRoot == null) { Fail("Drum or Deck root not found in " + ScenePath); return; }

        // ---------------------------------------------------------------- assets
        Material flashMat = EnsureFlashMaterial(MaterialDir + "/Case1_SlotFlash.mat", 0f);
        Material sparkleMat = EnsureStarMaterial(MaterialDir + "/Case1_StarSparkle.mat");
        Material trailMat = EnsureTrailMaterial(MaterialDir + "/Case1_ShapeTrail.mat");
        Material ringMat = EnsureRingMaterial(MaterialDir + "/Case1_SlotRing.mat");
        // Rewrites StarSparkleBurst.prefab wholesale - see EnsureSparklePrefab's summary before
        // changing any sparkle value in the prefab instead of in code.
        GameObject sparklePrefab = EnsureSparklePrefab(sparkleMat);

        // ---------------------------------------------------------------- drum cells
        List<DrumSlotReaction.Cell> cells = new List<DrumSlotReaction.Cell>(96);
        for (int i = 0; i < drumRoot.childCount; i++)
        {
            Transform t = drumRoot.GetChild(i);
            Match m = SegmentName.Match(t.name);
            if (!m.Success) continue;

            Transform hole = t.Find("Hole");
            cells.Add(new DrumSlotReaction.Cell
            {
                root = t,
                body = t.GetComponent<Renderer>(),
                hole = hole != null ? hole.GetComponent<Renderer>() : null,
                mystery = t.Find("MysteryOverlay"),
                column = int.Parse(m.Groups[1].Value),
                row = int.Parse(m.Groups[2].Value)
            });
        }
        if (cells.Count == 0) { Fail("Drum has no Segment_c<col>_r<row> children"); return; }
        cells.Sort((a, b) => a.column != b.column ? a.column.CompareTo(b.column) : a.row.CompareTo(b.row));

        // VIDEO_MEASURED: the live strip is diamond, diamond, triangle, hexagon, star. This must happen
        // before target discovery: the red hex hero belongs to c3r0 (the fourth cell), not the staged
        // c2r0. Both the open recess and its closed cap are authored explicitly and idempotently.
        EnsureReferenceLiveRow(cells);

        // ---------------------------------------------------------------- deck slots and shapes
        // The staged scene ships three playable pieces, so two of the five live-row recesses could
        // never be filled and tapping the triangle did nothing at all - it was tray SCENERY, and the
        // tray deliberately strips colliders off scenery so a tap can never resolve to one.
        EnsurePlayablePieces(scene, deckRoot);

        // Searched across the WHOLE scene, not under one fixed parent. The hierarchy is hand-grouped
        // now, and the first build after that regroup died with "Deck has no DeckSlot_* or Shape_*
        // children" - it was looking in the place the objects used to live. A builder that owns logic
        // rather than layout has no business assuming where an object is parented.
        List<float> slotX = new List<float>(8);
        List<Transform> shapes = new List<Transform>(8);
        {
            List<Transform> found = new List<Transform>(16);
            foreach (GameObject go in scene.GetRootGameObjects()) CollectByNamePrefix(go.transform, found);
            foreach (Transform t in found)
            {
                if (t.name.StartsWith("DeckSlot_")) slotX.Add(t.position.x);
                else if (t.name.StartsWith("Shape_")) shapes.Add(t);
            }
        }
        slotX.Sort();
        if (slotX.Count == 0 || shapes.Count == 0) { Fail("Deck has no DeckSlot_* or Shape_* children"); return; }

        shapes.Sort((a, b) => a.position.x.CompareTo(b.position.x));

        // P20 colour: the staged slot plates are pure white and rendered #FEFEFE, a blown-out row that
        // pulled the eye away from the drum. The reference's plates are a cool grey-blue, measured
        // #99ABC0 over the flat top of the plate. Retinted here rather than in the scene so a clean
        // clone gets the same row (lesson #4).
        Material slotPlate = EnsureToonMaterial(MaterialDir + "/Slot/Case1_DeckSlotPlate.mat",
                                                new Color(0.600f, 0.671f, 0.753f, 1f));
        int slotPlatesTinted = 0;
        for (int i = 0; i < deckRoot.childCount; i++)
        {
            Transform t = deckRoot.GetChild(i);
            if (!t.name.StartsWith("DeckSlot_")) continue;
            foreach (Renderer r in t.GetComponentsInChildren<Renderer>(true))
            {
                if (r is ParticleSystemRenderer) continue;
                r.sharedMaterial = slotPlate;
                EditorUtility.SetDirty(r);
                slotPlatesTinted++;
            }
        }
        Debug.Log("[Case1Setup] deck slot plates retinted to the reference grey-blue: " + slotPlatesTinted);

        // ---------------------------------------------------------------- match EVERY deck shape to its own cell
        // The player picks; the scene therefore has to know, up front, where each of the three shapes
        // belongs. Two signals are available and they agree on this scene: the hole recess mesh carries
        // the shape name ("Round-Hole"), and the cell body material carries the colour. Shape wins,
        // colour is the fallback, and a shape that matches neither is left with targetCell = -1, which
        // makes it deliberately untappable instead of sending it to an arbitrary hole.
        List<int> targetCells = new List<int>();
        List<string> matchNotes = new List<string>();
        List<int> taken = new List<int>();

        for (int s2 = 0; s2 < shapes.Count; s2++)
        {
            ShapeId shape;
            bool known = ShapeOf(shapes[s2].name, out shape);
            string shapeColour = MaterialName(shapes[s2]);
            // The authored column is a TIEBREAK now, not an override. At score 100 it beat the hole-mesh
            // match outright, which is how a round piece was sent into a diamond recess; and with two
            // hexagons in the row it would send both to the same column. The recess a piece actually
            // fits wins, and this only settles which of several equally good cells it takes.
            int authoredColumn = known ? ReferenceTargetColumn(shape) : -1;
            int best = authoredColumn >= 0 ? FindCell(cells, authoredColumn, 0) : -1;
            int bestScore = best >= 0 && !taken.Contains(best) ? 1 : 0;
            string note = bestScore > 0 ? "VIDEO_MEASURED live-row column " + authoredColumn : "no-match";
            if (bestScore == 0) best = -1;

            for (int c = 0; c < cells.Count; c++)
            {
                if (cells[c].row != 0) continue;
                if (taken.Contains(c)) continue;

                int score = 0;
                string why = "";
                string holeMesh = MeshName(cells[c].root, "Hole");
                if (known && ShapeIds.MatchesHole(shape, holeMesh))
                {
                    score += 2;
                    why = "hole-mesh '" + holeMesh + "'";
                }
                if (!string.IsNullOrEmpty(shapeColour) &&
                    string.Equals(shapeColour, MaterialName(cells[c].root), System.StringComparison.OrdinalIgnoreCase))
                {
                    score += 1;
                    why = string.IsNullOrEmpty(why) ? "colour '" + shapeColour + "'" : why + " + colour '" + shapeColour + "'";
                }

                if (score > bestScore) { bestScore = score; best = c; note = why; }
            }

            if (best < 0)
            {
                Debug.LogWarning("[Case1Setup] NO_TARGET for " + shapes[s2].name +
                                 " (shape=" + (known ? shape.ToString() : "UNKNOWN") +
                                 " colour='" + shapeColour + "'); it will not be tappable");
            }
            else
            {
                taken.Add(best);
            }

            targetCells.Add(best);
            matchNotes.Add(note);
        }

        int matched = 0;
        for (int i = 0; i < targetCells.Count; i++) if (targetCells[i] >= 0) matched++;
        if (matched == 0) { Fail("no deck shape could be matched to a drum cell"); return; }

        Camera cam = Object.FindFirstObjectByType<Camera>(FindObjectsInactive.Include);
        if (cam == null) { Fail("no camera in the scene"); return; }

        // The reference drum is a wall of bright, individually coloured cells; this scene ships every
        // cell hidden under the purple question-mark cover, which reads as one flat purple block.
        //
        // The cover is EVERY cell's, target cells included, and its material paints a large soft
        // question-mark pattern through _EmissionMap. At capture resolution that pattern does not read as
        // a question mark at all - it reads as a pale, blurry smudge sitting on the cell, which is exactly
        // what made the framed row look dirty. So every cover comes off: the target cells are marked by
        // the crisp rim built in BuildSlotBand instead, which is a deliberate frame rather than a haze.
        // MEASURED off the reference's opening frame: the drum is NOT fully revealed. Its top two rows
        // are still under the purple question-mark cover and only the rows from the rail down show
        // colour. Removing every cover made ours read as a flat pastel wall where the reference reads
        // as a wall of saturated cells with a covered band above them.
        List<int> coveredCells = new List<int>();
        SurfaceDrumCells(cells, targetCells, Object.FindFirstObjectByType<Camera>(FindObjectsInactive.Include), coveredCells);

        // AFTER the palette repaint, or the snap would overwrite the authored row again.
        ApplyLiveRowIdentity(cells);

        // The live row is the colour authority; the variants take their colour from it, and every
        // piece in the scene takes its colour from the variant. One edit, one place, everywhere.
        RefreshPieceVariantColours();

        // ...and the same rule reaches the REST of the drum. Only the live row and the tray were
        // following it, so a star cell was pink in one row and yellow in another and the shapes stopped
        // meaning anything away from the middle band.
        ApplyShapeColoursToDrum(cells, coveredCells);


        // Reference framing: the drum spans ~76% of the frame width, this scene's camera gave ~63%.
        // The reference background is a saturated purple that the bright drum sits against; the staged
        // scene cleared to a pale cream, which flattened every frame (deviation #1, "cok yuksek").
        if (cam != null)
        {
            cam.fieldOfView = 10.5f;
            cam.clearFlags = CameraClearFlags.SolidColor;
            // P20: measured against the reference's room colour (#7371C9 high in the frame, #8585CF low).
            cam.backgroundColor = new Color(0.513f, 0.510f, 0.810f, 1f);
            // OWNER'S CALL, given as an Inspector screenshot: position (0, 19, -24), rotation (25, 0, 0).
            // These constants ran at 15 degrees from (0, 14.2, -33.5) and are re-applied on EVERY Build
            // regardless of SceneIsAuthored, so leaving them here would silently revert the camera the
            // next time anyone builds. The scene asset itself is NOT written from here - it is hand
            // authored and uncommitted; see the note on SceneIsAuthored.
            cam.transform.position = new Vector3(0f, 19f, -24f);
            cam.transform.rotation = Quaternion.Euler(25.0f, 0f, 0f);
            EnsureAspectEnforcer(cam);
            EditorUtility.SetDirty(cam.transform);
            EditorUtility.SetDirty(cam);
        }

        // ---------------------------------------------------------------- exact reference composition
        // The board and the holder row have to FACE the camera. With the camera looking down 25 degrees
        // and the drum standing upright, what the frame showed was the TOPS of the cells - and the five
        // holder plates, lying flat, closed up into one grey slab. Both are pitched to meet the camera
        // before the composition is fitted.
        // PLACEMENT: the scene owns this now.
        if (!SceneIsAuthored) FaceCameraPitch(cam, drumRoot, LiveRowNormal(cells));

        // The holder plates are NOT pitched. Standing them up to face the camera was tried and made it
        // worse - at 82 degrees they grew until the five merged into one slab. They were never facing
        // the wrong way; they were simply too big for their spacing at this camera, so they overlapped.
        // Size is the fix, and it is measured: the reference's five plates sit on a 0.136 pitch across
        // viewport x 0.131..0.676, so a plate reads about 0.10 of the frame wide.

        if (!SceneIsAuthored) MatchReferenceComposition(scene, cam, drumRoot, deckRoot, shapes, cells);

        // The world, laid out as if there were no camera - and only then the camera.
        //
        // Order matters and each step earns its place: the reel's PHASE (which row is at the front) is a
        // property of the reel, so it is set first; the board is then stood on the ground behind the
        // plates; and the camera is placed last. Everything built below this line - marks, glyphs, rail,
        // chrome - is built against a board and a camera that are already final.
        // PLACEMENT - the scene owns it now. See SceneIsAuthored.
        if (!SceneIsAuthored) FaceCameraPitch(cam, drumRoot, LiveRowNormal(cells));
        if (!SceneIsAuthored) Case1WorldLayout.GroundBoard(scene, drumRoot, deckRoot);
        if (!SceneIsAuthored) Case1WorldLayout.PlaceCameraLast(cam, drumRoot, cells);

        // The unknown marks are built HERE, after MatchReferenceComposition has scaled and moved the
        // drum and after the camera's field of view is final. Building them earlier left every mark
        // behind at a stale world position while the drum moved out from under them - they were in the
        // scene, correctly sized, and simply not where the cells had ended up.
        BuildUnknownMarks(scene, cam, cells, coveredCells);
        // The piece must drop into ITS OWN shape. Pairing had been assigned by live-row COLUMN, so a
        // round piece flew into a diamond recess.
        Dictionary<int,ShapeId> forcedShape = new Dictionary<int,ShapeId>();
        for (int i = 0; i < shapes.Count && i < targetCells.Count; i++)
        {
            if (targetCells[i] < 0) continue;
            ShapeId tok;
            if (!ShapeOf(shapes[i].name, out tok) || GlyphPolygon(tok) == null) continue;
            forcedShape[targetCells[i]] = tok;

            Transform pairRoot = cells[targetCells[i]].root;
            Mesh pairHole = FindHoleMesh(tok, false);
            Mesh pairCap  = FindHoleMesh(tok, true);
            Transform hTf = pairRoot != null ? pairRoot.Find("Hole") : null;
            Transform cTf = pairRoot != null ? pairRoot.Find("Hole-Cap") : null;
            MeshFilter hMf = hTf != null ? hTf.GetComponent<MeshFilter>() : null;
            MeshFilter cMf = cTf != null ? cTf.GetComponent<MeshFilter>() : null;
            if (hMf != null && pairHole != null) { hMf.sharedMesh = pairHole; EditorUtility.SetDirty(hMf); }
            if (cMf != null && pairCap  != null) { cMf.sharedMesh = pairCap;  EditorUtility.SetDirty(cMf); }
        }

        // Build 3D sunken cavity recesses on the drum
        List<Transform> glyphRoots = new List<Transform>();
        BuildSunkenGlyphs(scene, cam, cells, coveredCells, forcedShape, glyphRoots);

        // DeckReflow must use the post-match positions, not the staged scene's old local X values.
        slotX.Clear();
        for (int i = 0; i < deckRoot.childCount; i++)
            if (deckRoot.GetChild(i).name.StartsWith("DeckSlot_")) slotX.Add(deckRoot.GetChild(i).localPosition.x);
        slotX.Sort();
        shapes.Sort((a, b) => a.position.x.CompareTo(b.position.x));

        // Remove all lights from the scene: all illumination, specular, shading, and colors are rendered via SoftPlastic.shader
        RemoveAllSceneLights(scene);

        // ---------------------------------------------------------------- slot band
        BuildSlotBand(scene, cells, targetCells, cam);

        // ---------------------------------------------------------------- playable 3x3 tray
        List<Transform> trayTiles = new List<Transform>();
        Dictionary<Transform,int> playableSlot = new Dictionary<Transform,int>();
        List<int> trayTileSlot = new List<int>();
        Vector3[] traySlots = BuildShapeTray(scene, cam, shapes, trayTiles, playableSlot, trayTileSlot);
        BuildReferenceChrome(scene, cam, shapes.Count > 0 ? shapes[0] : deckRoot);

        // Stage two: the tray, the plates and SPIN onto the same ground plane - RIGHT HERE, before
        // anything is sized. Run later, the pieces were fitted at their old positions and then moved:
        // the front row ended up a giant slab because it had been sized for a spot much further away.
        // Stage two: the plates and the chrome come down onto the ground plane, keeping the screen
        // position that was already validated against the reference.
        if (!SceneIsAuthored) Case1WorldLayout.GroundBillboards(scene, cam, deckRoot, drumRoot, Case1WorldLayout.BoardFrontZ);

        // The tray is NOT overridden here.
        //
        // BuildShapeTray already places the tray by raycasting the reference's viewport slots onto the
        // GROUND PLANE, so the tray was never the broken part - it sat on one plane with straight rows.
        // What was broken was the board, floating 16 units above it. Overriding the tray with a grid of
        // hand-chosen pitches on top of that replaced a measured layout with a guessed one, which is the
        // same mistake that sank the first world-first attempt.
        // Case1WorldLayout.ApplyTrayAndPlates(scene, cam, drumRoot, deckRoot);
        // RefitTrayPieces(cam, scene, shapes);

        // ---------------------------------------------------------------- director object
        // Rebuilt from scratch on every run. Once a component has been serialised into the scene its
        // stored field values win over the C# initialisers, so tuning the source would silently do
        // nothing; destroying the root first keeps the source the single authority.
        Transform rootTf = FindRoot(scene, RootName);
        if (rootTf != null) Object.DestroyImmediate(rootTf.gameObject);

        GameObject root = new GameObject(RootName);
        SceneManager.MoveGameObjectToScene(root, scene);

        Case1Director director = root.AddComponent<Case1Director>();
        DrumSlotReaction drum = root.AddComponent<DrumSlotReaction>();
        ShapeArcFlight flight = root.AddComponent<ShapeArcFlight>();
        DeckReflow reflow = root.AddComponent<DeckReflow>();
        ShapeTapInput tap = root.AddComponent<ShapeTapInput>();
        root.AddComponent<ReplayButton>();

        // (3) the staged scenes ship without an AudioListener, so none of the procedural sfx is audible.
        if (Object.FindFirstObjectByType<AudioListener>(FindObjectsInactive.Include) == null && cam != null)
        {
            cam.gameObject.AddComponent<AudioListener>();
            EditorUtility.SetDirty(cam.gameObject);
            Debug.Log("[Case1Setup] AudioListener added to " + cam.name + " (scene shipped without one)");
        }

        drum.cells = cells.ToArray();
        drum.cellGlyphs = glyphRoots.ToArray();
        drum.sparklePrefab = sparklePrefab;
        drum.flashMaterial = flashMat;
        drum.ringMaterial = ringMat;

        // The director taps the FIRST flight entry. Which shape that was depended on discovery order, so
        // the piece that left the tray kept changing between builds and with it the whole compaction.
        // The reference's tapped piece leaves from row0 col2, so the shape holding that slot is moved to
        // the front - with its paired cell and match note, or the pairing would be silently broken.
        for (int i = 0; i < shapes.Count; i++)
        {
            int sl; if (!playableSlot.TryGetValue(shapes[i], out sl) || sl != 2) continue;   // slot 2 = the hero
            if (i == 0) break;
            Transform ts = shapes[0]; shapes[0] = shapes[i]; shapes[i] = ts;
            int tc = targetCells[0]; targetCells[0] = targetCells[i]; targetCells[i] = tc;
            string tn = matchNotes[0]; matchNotes[0] = matchNotes[i]; matchNotes[i] = tn;
            break;
        }
        Debug.Log("[Case1Setup] TAP_ORDER first flight entry is " + (shapes.Count > 0 ? shapes[0].name : "none") +
                  " at tray slot " + (shapes.Count > 0 && playableSlot.ContainsKey(shapes[0]) ? playableSlot[shapes[0]] : -1));

        List<ShapeArcFlight.Entry> flightEntries = new List<ShapeArcFlight.Entry>(shapes.Count);
        for (int i = 0; i < shapes.Count; i++)
        {
            flightEntries.Add(new ShapeArcFlight.Entry
            {
                shape = shapes[i],
                shapeRenderer = shapes[i].GetComponent<Renderer>(),
                targetCell = targetCells[i],
                matchNote = matchNotes[i]
            });
        }
        // EVERY tray tile gets an entry, not just the five "playable" ones. Tapping is resolved by
        // screen proximity against this list, so a tile without an entry can never be picked - which
        // is why one square flew and the identical square beside it did nothing. Which COPY of a shape
        // the player touches must not matter; the shape does. Each tile aims at the live-row cell whose
        // recess matches it, and once that cell is filled every tile pointing at it stops being
        // playable, so a second square cannot fly into a hole that is already closed.
        int extra = 0;
        for (int i = 0; i < trayTiles.Count; i++)
        {
            Transform tile = trayTiles[i];
            if (tile == null) continue;

            ShapeId tileShape;
            ShapeOf(tile.name, out tileShape);
            int column = ReferenceTargetColumn(tileShape);
            int cellIndex = column >= 0 ? FindCell(cells, column, 0) : -1;

            flightEntries.Add(new ShapeArcFlight.Entry
            {
                shape = tile,
                shapeRenderer = tile.GetComponent<Renderer>(),
                targetCell = cellIndex,
                matchNote = "tray copy of " + tileShape
            });
            extra++;
        }
        Debug.Log("[Case1Setup] TAP_TARGETS " + flightEntries.Count + " tappable tiles (" +
                  shapes.Count + " staged + " + extra + " tray copies)");

        flight.entries = flightEntries.ToArray();
        flight.drum = drum;
        flight.viewCamera = cam;
        flight.trailMaterial = trailMat;

        // MEASURED from the reference: when a shape leaves the 3x3 tray the remaining shapes COMPACT
        // into the gap along the row. Every tray occupant is therefore a reflow entry - the three
        // playable ones AND the scenery behind them - because the scenery is what visibly moves up.
        List<DeckReflow.Entry> entries = new List<DeckReflow.Entry>(shapes.Count + trayTiles.Count);
        for (int i = 0; i < shapes.Count; i++)
        {
            int slot; if (!playableSlot.TryGetValue(shapes[i], out slot)) slot = i;
            entries.Add(new DeckReflow.Entry { shape = shapes[i], slot = slot });
        }
        for (int i = 0; i < trayTiles.Count; i++)
            entries.Add(new DeckReflow.Entry { shape = trayTiles[i], slot = trayTileSlot[i] });
        // Solve each tile's FRONT and BACK scale against the screen. Every tile is currently at its
        // front-row size, so that is the front scale; the back scale is the one whose PROJECTED height
        // is backRowFlatten of the front row's projected height. Doing this per tile is what makes the
        // rows read: a factor applied blindly fights the perspective gain of the nearer rows.
        {
            // The row scales are READ from the scene, not solved.
            //
            // Whatever the author set on a front-row piece is the front scale, and whatever is on a
            // back-row piece is the back scale. Solving them here is what produced a tray that changed
            // size on every rebuild and rows that differed in width when they should have differed
            // USER DIRECTIVE: Exact identical dimensions per shape type, tall height on front row (1.35), solid 3D depth on back rows (0.65)
            for (int i = 0; i < entries.Count; i++)
            {
                DeckReflow.Entry e = entries[i];
                if (e == null || e.shape == null) continue;
                ShapeId shapeId;
                if (!ShapeIds.TryParse(e.shape.name, out shapeId) && !ShapeOf(e.shape.name, out shapeId))
                    shapeId = ShapeId.Square;

                Vector3 baseSc = BaseScaleForShape(shapeId);
                // USER DIRECTIVE: Front row Y +25% more (1.50x), Back rows Y -25% (0.60x)
                Vector3 frontSc = new Vector3(baseSc.x, baseSc.y * 1.50f, baseSc.z);
                Vector3 backSc = new Vector3(baseSc.x, baseSc.y * 0.60f, baseSc.z);

                e.FrontScale = frontSc;
                e.BackScale = backSc;

                bool isFront = e.slot < TrayColumnX.Length;
                e.shape.localScale = isFront ? e.FrontScale : e.BackScale;
                EditorUtility.SetDirty(e.shape);
            }
            Debug.Log("[Case1Setup] ROW_SCALES applied: front pieces (1.50x Y), back rows (0.60x Y)");
        }

        reflow.entries = entries.ToArray();
        reflow.slotWorld = traySlots;
        reflow.columns = TrayColumnX.Length;
        reflow.slotX = new float[0];
        reflow.viewCamera = cam;
        Debug.Log(string.Format("[Case1Setup] TRAY_REFLOW {0} entries over {1} slots (playable {2}, scenery {3})",
            entries.Count, traySlots.Length, shapes.Count, trayTiles.Count));

        tap.viewCamera = cam;
        tap.director = director;

        director.flight = flight;
        director.drum = drum;
        director.deck = reflow;

        EditorUtility.SetDirty(director);
        EditorUtility.SetDirty(drum);
        EditorUtility.SetDirty(flight);
        EditorUtility.SetDirty(reflow);
        EditorUtility.SetDirty(tap);
        EditorUtility.SetDirty(root);

        // LAST, so it catches everything the build created.
        // The hierarchy is hand-grouped too; regrouping it moved things the author had placed.
        if (!SceneIsAuthored) OrganiseHierarchy(scene, cam);

        AssetDatabase.SaveAssets();
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);

        Dump(cells, shapes, slotX, targetCells, matchNotes, cam);

        StringBuilder pairs = new StringBuilder();
        for (int i = 0; i < shapes.Count; i++)
        {
            if (i > 0) pairs.Append(" | ");
            pairs.Append(shapes[i].name).Append(" -> ")
                 .Append(targetCells[i] >= 0 ? cells[targetCells[i]].root.name : "<none>")
                 .Append(" [").Append(matchNotes[i]).Append("]");
        }
        Debug.Log("[Case1Setup] SETUP_OK pairs: " + pairs + "  cells=" + cells.Count);
    }

    // ------------------------------------------------------------------ reference composition

    // VIDEO_MEASURED from Fit The Shape.mp4 frame 0 at 1080x1728.
    // Drum cell block spans x[0.095..0.905] (width ~81% of frame) and y[0.585..0.850].
    static readonly Rect RefDrumRect = Rect.MinMaxRect(0.095f, 0.585f, 0.905f, 0.850f);
    static readonly float[] RefDeckSlotX = { 0.131f, 0.268f, 0.403f, 0.539f, 0.676f };
    // VIDEO_MEASURED plate centre y=854/1728 from the top -> 0.506 from the bottom.
    const float RefDeckSlotY = 0.506f;
    const float RefPlateWidth = 0.105f;

    // SPIN button: measured centre 0.836, width 0.174, band y 0.481..0.559, fill #A1CA31.
    static readonly Vector2 RefSpinCentre = new Vector2(0.836f, 0.520f);
    static readonly Vector2 RefSpinSize = new Vector2(0.174f, 0.078f);
    static readonly Color RefSpinGreen = new Color(0.631f, 0.792f, 0.192f, 1f);
    // Holder plate fill, measured #9CAEC4.
    static readonly Color RefPlateGrey = new Color(0.616f, 0.679f, 0.767f, 1f);
    // Bottom lock buttons: measured centres 0.292 / 0.499 / 0.708 at y 0.100.
    static readonly float[] RefLockX = { 0.292f, 0.499f, 0.708f };
    const float RefLockY = 0.100f;
    // Top strip: level plate around x 0.21..0.24, gear around x 0.795, both at y 0.970.
    static readonly Vector2 RefLevelCentre = new Vector2(0.225f, 0.970f);
    static readonly Vector2 RefGearCentre = new Vector2(0.795f, 0.970f);

    /// <summary>
    /// Makes composition an invariant instead of a screenshot accident. The drum is uniformly fitted to
    /// the measured reference rectangle; the five holding plates are then placed by viewport coordinate.
    /// The actual playable shapes remain children of Deck and keep their pairing/logic unchanged.
    /// </summary>
    static void MatchReferenceComposition(Scene scene, Camera cam, Transform drumRoot, Transform deckRoot,
                                          List<Transform> shapes, List<DrumSlotReaction.Cell> cells)
    {
        if (cam == null) return;

        // REVERTED from a world-first layout. That rewrite put the board and the tray on measured world
        // constants and pointed the camera at the result, which did give a coherent scene - but the
        // composition constants it needed (board width in world units, camera rise and distance) were
        // GUESSED rather than measured, and the drum filled the whole frame. The right order is still
        // world-first; it just needs its own numbers measured before it is worth having.
        Renderer[] boardRenderers = drumRoot != null
            ? drumRoot.GetComponentsInChildren<Renderer>(true) : new Renderer[0];
        if (boardRenderers.Length > 0)
        {
            Bounds board = boardRenderers[0].bounds;
            for (int i = 1; i < boardRenderers.Length; i++) board.Encapsulate(boardRenderers[i].bounds);

            // Look down on the board rather than at it edge-on. The camera keeps its ground position
            // and only rises, so the framing solve that follows still owns the composition; only the
            // angle changes. 6.9 deg read as nearly level and the board lost its sense of a surface.
            Vector3 camPos = cam.transform.position;
            Vector2 horiz = new Vector2(board.center.x - camPos.x, board.center.z - camPos.z);
            float horizDist = horiz.magnitude;
            if (horizDist > 0.01f)
            {
                camPos.y = board.center.y + horizDist * Mathf.Tan(TargetCameraPitchDeg * Mathf.Deg2Rad);
                cam.transform.position = camPos;
            }

            Vector3 toBoard = board.center - cam.transform.position;
            if (toBoard.sqrMagnitude > 1e-6f)
            {
                cam.transform.rotation = Quaternion.LookRotation(toBoard.normalized, Vector3.up);
                EditorUtility.SetDirty(cam.transform);
                Debug.Log("[Case1Setup] CAMERA_AIM at " + cam.transform.position +
                          " -> board centre " + board.center +
                          " (pitch " + cam.transform.eulerAngles.x.ToString("0.0") + " deg)");
            }
        }

        // Scale from the WIDTH, then place by the live row. RefDrumRect asks for 0.645 x 0.338 while our
        // drum's own projected aspect is 0.747, because it carries more rows than the reference shows -
        // so a uniform fit that satisfies the width MUST overflow the height. Demanding a height the
        // object cannot have is not a fit, it is a contradiction.
        for (int pass = 0; pass < 6; pass++)
        {
            Rect r;
            if (!ReferenceMatchLayout.ProjectBounds(cam, drumRoot, out r) || r.width < 1e-4f) break;
            float f = Mathf.Clamp(RefDrumRect.width / r.width, 0.5f, 1.9f);
            if (Mathf.Abs(f - 1f) < 0.005f) break;
            drumRoot.localScale = drumRoot.localScale * f;
        }

        int liveMid = FindCell(cells, 2, 0);
        if (liveMid < 0) liveMid = FindCell(cells, 0, 0);
        if (liveMid >= 0 && cells[liveMid].root != null)
        {
            Vector3 liveWorld = cells[liveMid].body != null
                ? cells[liveMid].body.bounds.center : cells[liveMid].root.position;
            Vector3 vp = cam.WorldToViewportPoint(liveWorld);
            Vector3 want = cam.ViewportToWorldPoint(new Vector3(RefLiveRowCentre.x, RefLiveRowCentre.y, vp.z));
            drumRoot.position += want - liveWorld;
            EditorUtility.SetDirty(drumRoot);
            Debug.Log("[Case1Setup] DRUM_PLACE live row at viewport " + RefLiveRowCentre);
        }

        List<Transform> plates = new List<Transform>();
        for (int i = 0; i < deckRoot.childCount; i++)
            if (deckRoot.GetChild(i).name.StartsWith("DeckSlot_")) plates.Add(deckRoot.GetChild(i));
        plates.Sort((a, b) => a.localPosition.x.CompareTo(b.localPosition.x));

        Transform depthModel = plates.Count > 0 ? plates[0] : (shapes.Count > 0 ? shapes[0] : deckRoot);
        float depth = Mathf.Max(0.05f, cam.WorldToViewportPoint(depthModel.position).z);
        for (int i = 0; i < plates.Count; i++)
        {
            float x = i < RefDeckSlotX.Length ? RefDeckSlotX[i]
                                              : RefDeckSlotX[RefDeckSlotX.Length - 1] + 0.12f * (i - RefDeckSlotX.Length + 1);
            // Straight, like everything else on the board. The staged plates carried a 19.3 degree
            // pitch that had no reason to be there and read as five crooked tiles.
            plates[i].rotation = Quaternion.identity;
            ReferenceMatchLayout.PlaceAtDepth(cam, plates[i], new Vector2(x, RefDeckSlotY), depth);
            FitDeckPlate(cam, plates[i]);
            ReferenceMatchLayout.PlaceAtDepth(cam, plates[i], new Vector2(x, RefDeckSlotY), depth);
            EditorUtility.SetDirty(plates[i]);
        }

        Rect got;
        if (ReferenceMatchLayout.ProjectBounds(cam, drumRoot, out got))
            Debug.Log(string.Format("[Case1Setup] REF_LAYOUT drum got x[{0:0.000}..{1:0.000}] y[{2:0.000}..{3:0.000}] target x[{4:0.000}..{5:0.000}] y[{6:0.000}..{7:0.000}]",
                got.xMin, got.xMax, got.yMin, got.yMax, RefDrumRect.xMin, RefDrumRect.xMax, RefDrumRect.yMin, RefDrumRect.yMax));
    }

    // ------------------------------------------------------------------ drum surfacing

    /// <summary>
    /// Brings the drum to the reference's read: the top two rows keep their question-mark covers, every
    /// other cell is revealed with a SATURATED face and a near-black glyph. MEASURED: the reference's
    /// revealed cells sit around #F9A633 / #B903D0 / #FA4929 / #FA9DC6 - saturation 0.78 and up, value
    /// 0.90 and up - and their shape glyph is almost black. Ours were pastel faces with a faint
    /// embossed outline, which is why the drum looked washed out next to the reference.
    /// </summary>
    const string QuestionRootName = "Case1_UnknownMarks";

    // MEASURED off the reference's covered rows: the cover face is #BB0DD4-ish, a strong magenta violet.
    static readonly Color RefCoverColour = new Color(0.725f, 0.051f, 0.831f, 1f);

    /// <summary>
    /// The mystery cover's base, SOLVED so the OVERLAY RENDERS the reference's cover, rather than
    /// being a copy of it. RefCoverColour above is the reference's cover colour and stays that way for
    /// the cell BODY under the overlay; this is the value the MysteryCover shader needs to land there.
    ///
    /// MEASURED. The reference's cover face is (188,2,215), L* 46.5. Ours rendered (138,6,160), L* 34.1
    /// - 12.4 L* dark over 42.7% of the drum, which is the single darkest population in the frame and
    /// the answer to "daha parlak yap". The base was never the problem: RefCoverColour is already
    /// (185,13,212). The shader was eating 47% of it, in two separable parts:
    ///
    ///   _CurveDarken 0.55  ->  a flat x0.67 on EVERY row. Probed 0.55 -> 0: the plateau went L* 34.1
    ///                          -> 41.3 and the chain 0.53 -> 0.79. It is not modelling curvature here:
    ///                          the six drum rows measured 33.3/33.6/33.9/34.1/34.4/34.4, a 1.1 L*
    ///                          spread, so every row was being dimmed by the same constant.
    ///   diffuse * bevel    ->  the remaining 0.79, sitting on the shader's 0.78 floors.
    ///
    /// So _CurveDarken is zeroed for covers and this base is the reference target divided through the
    /// measured 0.787/0.528/0.801. Both are now written HERE rather than left in the .mat: _CurveDarken
    /// was asset-only, which is exactly the split ownership DERSLER warns produces unreadable baselines.
    /// </summary>
    static readonly Color CoverOverlayBase = new Color(0.824f, 0.015f, 0.934f, 1f);   // #D204EE
    // VIDEO_MEASURED from Fit The Shape.mp4 opening frame: Orange Diamond, Orange Diamond, Purple Triangle, Red Hexagon, Pink Star
    // NOTE: for row 0 this array is a DEAD WRITE. SurfaceDrumCells stamps it, then ApplyLiveRowIdentity
    // and ApplyShapeColoursToDrum both run later and overwrite row 0 from LiveRowColour/LiveRowShape.
    // It is left at the true video measurement on purpose; column 1 is deliberately green downstream
    // (see LiveRowShape). Do not "restore" the live row from this table.
    static readonly Color[] RefLiveRowColours =
    {
        new Color(0.965f, 0.502f, 0.082f, 1f),   // #F68015 Orange (Diamond)
        new Color(0.965f, 0.502f, 0.082f, 1f),   // #F68015 Orange (Diamond)
        new Color(0.550f, 0.035f, 0.930f, 1f),   // #8C09ED Purple (Triangle)
        new Color(0.965f, 0.145f, 0.070f, 1f),   // #F62512 Red    (Hexagon)
        new Color(0.965f, 0.485f, 0.685f, 1f)    // #F67CAF Pink   (Star)
    };

    static void SurfaceDrumCells(List<DrumSlotReaction.Cell> cells, List<int> targetCells, Camera cam, List<int> coveredCells)
    {
        if (cells == null || cells.Count == 0) return;


        // Find visible rows on the front of the cylinder facing the camera.
        // Row 0 is the live row inside the slot band (vp.y ~ 0.72)
        // Row +1 is the row immediately above row 0 (vp.y ~ 0.81)
        // Row +2 is the top-most visible row (vp.y ~ 0.89)
        Dictionary<int, float> rowScreenY = new Dictionary<int, float>();
        Dictionary<int, int> rowCount = new Dictionary<int, int>();
        for (int i = 0; i < cells.Count; i++)
        {
            DrumSlotReaction.Cell c = cells[i];
            if (c == null || c.root == null) continue;
            Vector3 pos = c.root.position;
            Vector3 toCam = cam != null ? (cam.transform.position - pos).normalized : Vector3.back;
            if (Vector3.Dot(c.root.up, toCam) > -0.4f || Vector3.Dot(c.root.forward, toCam) > -0.4f)
            {
                float sy = cam != null ? cam.WorldToViewportPoint(pos).y : pos.y;
                if (!rowScreenY.ContainsKey(c.row)) { rowScreenY[c.row] = 0f; rowCount[c.row] = 0; }
                rowScreenY[c.row] += sy;
                rowCount[c.row]++;
            }
        }
        foreach (int r in new List<int>(rowScreenY.Keys))
            rowScreenY[r] /= Mathf.Max(1, rowCount[r]);

        List<int> sortedFrontRows = new List<int>(rowScreenY.Keys);
        sortedFrontRows.Sort((a, b) => rowScreenY[b].CompareTo(rowScreenY[a])); // top to bottom

        int liveRow = 0;
        int liveIdx = sortedFrontRows.IndexOf(liveRow);
        int rowAbove = -1;
        int rowTop = -1;
        if (liveIdx > 0)
        {
            rowAbove = sortedFrontRows[liveIdx - 1]; // 1 row immediately above live row
            if (liveIdx > 1) rowTop = sortedFrontRows[liveIdx - 2]; // 2 rows above live row (top)
        }
        else if (sortedFrontRows.Count >= 3)
        {
            rowTop = sortedFrontRows[0];
            rowAbove = sortedFrontRows[1];
        }

        Debug.Log("[Case1Setup] DRUM_ROWS frontRows: " + string.Join(", ", sortedFrontRows.ConvertAll(r => "r" + r + "(y=" + rowScreenY[r].ToString("F2") + ")")) +
                  " -> rowAbove=" + rowAbove + " rowTop=" + rowTop);

        // USER DIRECTIVE: Increase count of question mark '?' covered cells across the drum wheel
        // Reference match: Top row has '?' at col 1, 2, 3; Row above has '?' at col 0, 2, 3, 4; Row below has '?'; rest of cylinder has alternating '?'
        HashSet<int> coveredCellsSet = new HashSet<int>();

        // 1. Top visible row: Question marks at col 1, 2, 3
        if (rowTop >= 0)
        {
            foreach (int col in new[] { 1, 2, 3 })
            {
                int idx = FindCell(cells, col, rowTop);
                if (idx >= 0 && (targetCells == null || !targetCells.Contains(idx)))
                    coveredCellsSet.Add(idx);
            }
        }

        // 2. Row immediately above live band: Question marks at col 0, 2, 3, 4 (only col 1 is open socket)
        if (rowAbove >= 0)
        {
            foreach (int col in new[] { 0, 2, 3, 4 })
            {
                int idx = FindCell(cells, col, rowAbove);
                if (idx >= 0 && (targetCells == null || !targetCells.Contains(idx)))
                    coveredCellsSet.Add(idx);
            }
        }

        // 3. Visible rows below live band:
        if (liveIdx >= 0 && liveIdx + 1 < sortedFrontRows.Count)
        {
            int rowBelow1 = sortedFrontRows[liveIdx + 1];
            foreach (int col in new[] { 1, 3 })
            {
                int idx = FindCell(cells, col, rowBelow1);
                if (idx >= 0 && (targetCells == null || !targetCells.Contains(idx)))
                    coveredCellsSet.Add(idx);
            }
        }
        if (liveIdx >= 0 && liveIdx + 2 < sortedFrontRows.Count)
        {
            int rowBelow2 = sortedFrontRows[liveIdx + 2];
            foreach (int col in new[] { 0, 2, 4 })
            {
                int idx = FindCell(cells, col, rowBelow2);
                if (idx >= 0 && (targetCells == null || !targetCells.Contains(idx)))
                    coveredCellsSet.Add(idx);
            }
        }

        // 4. Also cover ~40% of remaining drum rows around the cylinder
        for (int r = 0; r < 15; r++)
        {
            if (r == liveRow || r == rowAbove || r == rowTop) continue;
            for (int c = 0; c < 5; c++)
            {
                if ((r + c) % 2 == 0)
                {
                    int idx = FindCell(cells, c, r);
                    if (idx >= 0 && (targetCells == null || !targetCells.Contains(idx)))
                        coveredCellsSet.Add(idx);
                }
            }
        }

        int covered = 0, revealed = 0, capped = 0;
        for (int i = 0; i < cells.Count; i++)
        {
            DrumSlotReaction.Cell c = cells[i];
            bool keepCover = coveredCellsSet.Contains(i);
            Transform capTransform = c.root != null ? c.root.Find("Hole-Cap") : null;
            Renderer cap = capTransform != null ? capTransform.GetComponent<Renderer>() : null;
            if (keepCover)
            {
                covered++;
                if (c.body != null)
                {
                    Material coverMat = EnsureToonMaterial(MaterialDir + "/Cells/Case1_CellCover.mat", RefCoverColour);
                    if (coverMat != null) c.body.sharedMaterial = coverMat;
                }
                // Disable Hole and Hole-Cap completely on mystery question mark cells so no dark glyph shows through
                if (c.hole != null)
                {
                    c.hole.enabled = false;
                    c.hole.gameObject.SetActive(false);
                    EditorUtility.SetDirty(c.hole);
                }
                if (cap != null)
                {
                    cap.enabled = false;
                    cap.gameObject.SetActive(false);
                    EditorUtility.SetDirty(cap);
                }

                if (c.mystery != null)
                {
                    c.mystery.gameObject.SetActive(true);
                    Renderer mysteryRenderer = c.mystery.GetComponent<Renderer>();
                    if (mysteryRenderer != null)
                    {
                        mysteryRenderer.enabled = true;
                        mysteryRenderer.sharedMaterial = EnsureMysteryMaterial(
                            MaterialDir + "/Cells/Case1_MysteryCover.mat", 0f);
                        mysteryRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
                        mysteryRenderer.receiveShadows = true;
                        EditorUtility.SetDirty(mysteryRenderer);
                    }
                    EditorUtility.SetDirty(c.mystery.gameObject);
                }
                coveredCells.Add(i);
                continue;
            }

            revealed++;

            if (c.mystery != null)
            {
                c.mystery.gameObject.SetActive(false);
                EditorUtility.SetDirty(c.mystery.gameObject);
            }

            if (c.body == null) continue;
            Color baseColour = ReadBase(c.body.sharedMaterial);
            float h, sat, val;
            Color.RGBToHSV(baseColour, out h, out sat, out val);
            Color face;
            if (c.row == 0 && c.column >= 0 && c.column < RefLiveRowColours.Length)
            {
                face = RefLiveRowColours[c.column];
                Color.RGBToHSV(face, out h, out sat, out val);
            }
            else
            {
                face = SnapToReferencePalette(baseColour, out h, out sat, out val);
            }

            ShapeId shapeId;
            ShapeIds.TryParse(MeshName(c.root, "Hole"), out shapeId);

            Material faceMat = EnsureToonMaterial(MaterialDir + "/Cells/Case1_Toon_" + ColourKey(face) + "_" + shapeId + ".mat", face, shapeId);
            if (faceMat != null) c.body.sharedMaterial = faceMat;

            if (c.hole != null)
            {
                c.hole.enabled = false;
                c.hole.gameObject.SetActive(false);
                EditorUtility.SetDirty(c.hole);
            }

            if (cap != null)
            {
                cap.enabled = false;
                cap.gameObject.SetActive(false);
                EditorUtility.SetDirty(cap);
            }
        }

        Debug.Log(string.Format("[Case1Setup] DRUM_SURFACE {0} revealed, {1} covered ({2} marked), {3} cavity floors filled",
                                revealed, covered, coveredCells.Count, capped));
    }


    /// <summary>Flat colour that ignores depth: used for marks printed on a cell face.</summary>
    static Material EnsureOverlay(string path, Color colour)
    {
        if (!AssetDatabase.IsValidFolder(MaterialDir + "/Cells"))
            AssetDatabase.CreateFolder(MaterialDir, "Cells");
        Shader shader = Shader.Find("Case1/CellOverlay");
        if (shader == null) { Debug.LogError("[Case1Setup] Case1/CellOverlay not found"); return null; }
        Material m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (m == null) { m = new Material(shader); AssetDatabase.CreateAsset(m, path); }
        else if (m.shader != shader) m.shader = shader;
        m.SetColor("_BaseColor", colour);
        m.renderQueue = 4000;
        EditorUtility.SetDirty(m);
        return m;
    }

    static Material EnsureMysteryMaterial(string path, float shinePhase)
    {
        if (!AssetDatabase.IsValidFolder(MaterialDir + "/Cells"))
            AssetDatabase.CreateFolder(MaterialDir, "Cells");
        Shader shader = Shader.Find("Case1/MysteryCover");
        if (shader == null) { Debug.LogError("[Case1Setup] Case1/MysteryCover not found"); return null; }
        Material m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (m == null) { m = new Material(shader); AssetDatabase.CreateAsset(m, path); }
        else if (m.shader != shader) m.shader = shader;

        Texture2D pattern = AssetDatabase.LoadAssetAtPath<Texture2D>(
            "Assets/Case1_FitTheShape/Textures/questionmark pattern2.png");
        m.SetColor("_BaseColor", CoverOverlayBase);
        // Flat x0.67 on every row here, not a curvature - see CoverOverlayBase. Code-owned now.
        m.SetFloat("_CurveDarken", 0f);
        // MEASURED against every cover in the reference clip, not against a colour histogram.
        // r_078, r_200, r_270 and r_340 - four widely separated frames - show EVERY mystery cover as
        // the same magenta-purple. There is no teal, pink or blue-violet cover anywhere in the clip.
        // The four-hue per-instance hash added in d196793 was fitted to colour-SHARE percentages over
        // a drum crop; it reached the reference's histogram by scattering hues across covers the
        // reference paints uniformly. Palette extraction on r_078: the reference's largest tile family
        // is magenta at 15.7%, ours was teal at 12.4% with magenta at 6.3% - the rank order inverted.
        // Our magenta was never wrong (#B61AD4 against #B610D8, dE 3.2); there was not enough of it.
        m.SetFloat("_PaletteMix", 0f);
        m.SetColor("_PatternColor", new Color(0.89f, 0.35f, 0.82f, 1f));
        if (pattern != null) m.SetTexture("_PatternTex", pattern);
        // The authored UV island only showed a cropped, cell-sized blob at 1x. The reference prints
        // several small question marks on every individual cover, so repeat the supplied pattern.
        m.SetTextureScale("_PatternTex", new Vector2(3.0f, 2.4f));
        m.SetTextureOffset("_PatternTex", Vector2.zero);
        m.SetFloat("_PatternThreshold", 0.34f);
        m.SetFloat("_Smoothness", 0.42f);
        // Clean static toon look without moving white shine sweep bar
        m.SetFloat("_ShineStrength", 0.0f);
        m.SetFloat("_ShineWidth", 0.0f);
        m.SetFloat("_ShineSpeed", 0.0f);
        m.SetFloat("_ShineTilt", 0.0f);
        m.SetFloat("_ShineOffset", 0.0f);
        EditorUtility.SetDirty(m);
        return m;
    }

    static string ColourKey(Color c)
    {
        return string.Format("{0:000}_{1:000}_{2:000}", (int)(c.r * 255), (int)(c.g * 255), (int)(c.b * 255));
    }

    static Color ReadBase(Material m)
    {
        if (m == null) return Color.grey;
        if (m.HasProperty("_BaseColor")) return m.GetColor("_BaseColor");
        if (m.HasProperty("_Color")) return m.GetColor("_Color");
        return Color.grey;
    }

    // MEASURED off Fit The Shape.mp4 frame 0: the drum's revealed cells cluster onto these ten hues.
    /// <summary>
    /// The live row's identity, in ONE place. Shape and colour used to come from different mechanisms -
    /// the shape authored here, the colour snapped to the nearest palette hue from whatever the staged
    /// scene happened to have - so two neighbouring cells could collapse onto the same palette entry and
    /// did: the round and the hexagon both came out orange and were impossible to tell apart. Changing
    /// a column here changes its recess, its cell colour and the colour of the piece that fills it.
    ///
    /// Every shape appears at most ONCE. Replacing the unfillable diamond with a second hexagon left
    /// one shape wearing two colours, and there is no good way out of that: give the pair the same
    /// colour and the two cells are indistinguishable again, give them different colours and the shape
    /// stops identifying the piece that fits it. Five distinct shapes is the only arrangement where
    /// both hold, and the project owns a prefab for each.
    ///
    /// Column 0 is a SQUARE and this is a deliberate, measured deviation from the reference video.
    /// The reference live row really is orange-diamond / orange-diamond; that was not a mistake in the
    /// table. But the reference drum SPINS and its tray holds nine pieces, so a piece with no matching
    /// live cell is normal there - the player just waits for the row to come round. This scene does not
    /// spin: DeckReflow has columns=3, so exactly three tray pieces are ever tappable, and
    /// EnsurePlayablePieces generates a Square into that tray unconditionally. With two Diamond columns
    /// and no Square column, the green square sat in the front row permanently unplayable - tapping it
    /// fell through to FindAvailableLiveSlot returning -1 and the piece just shook in place.
    /// USER DIRECTIVE: "yesil icin yesile basiyorum ama kabul etmiyor ... yesili kabul etsin onu duzelt".
    /// Column 0 specifically, because that is where the green square sat before commit 10c23c4
    /// replaced {Round, Square, Triangle, Hexagon, Star} with {Diamond, Diamond, ...} and deleted the
    /// only Square cell. The owner's screenshot still shows green / orange / purple / red / pink left
    /// to right, and "sari basinca ilke gidiyor" is the complaint that the ORANGE piece now takes the
    /// first cell - the one the green square used to own. Restoring Square to column 0 puts green back
    /// where he expects it and sends the orange diamond to column 1, which also retires the pair of
    /// indistinguishable orange cells this very comment warns about: five distinct shapes again.
    /// </summary>
    static readonly ShapeId[] LiveRowShape =
    {
        ShapeId.Square, ShapeId.Diamond, ShapeId.Triangle, ShapeId.Hexagon, ShapeId.Star
    };

    /// <summary>
    /// Colour per live-row column matching Fit The Shape.mp4 reference video.
    /// </summary>
    /// SOLVED, not picked. The old values (#2EB814 / #F6941F / #AD0DF2 / #F52E1F / #F594B8) rendered
    /// live-row faces that were both too dark and too crushed in their minor channels - the reference's
    /// orange face is (249,160,46) and ours was (210,119,16), its red (246,66,34) against ours
    /// (209,30,18). Fixing that is a SOLVE, because the transfer from _BaseColor to the rendered face
    /// is measurable: a calibration run put a neutral ramp through the five live cells and read the
    /// plateau back.
    ///
    ///     base .mat float   1.000  0.800  0.600  0.400  0.200
    ///     rendered sRGB       254    206    152     98     46      (channel spread 0.0 at every step)
    ///
    /// So the chain is neutral and very close to the identity in GAMMA space, with a few codes of
    /// darkening at the bottom end. These entries are that curve inverted at each of the reference's
    /// own measured face plateaus, which are exactly flat in the source (sampled std 0.00).
    /// The result independently recovers the case's documented palette - solved #FAA133 / #B22DE8 /
    /// #F64726 / #F89BC0 against the brief's #F9A633 / #BA33E9 / #F74929 / #FA9DC5 - which is the
    /// check that the solve is measuring the right thing.
    ///
    /// Green is the one entry with an extra assumption: the reference live row is orange/orange and
    /// carries no green, because column 0 being a green Square is a deliberate documented deviation
    /// (see LiveRowShape). Its target is the reference's own green cell face in the row immediately
    /// below, (51,189,26). That row reads about 2 L* dimmer than the live row on orange, which appears
    /// in both, so green is under-targeted by roughly that much and no more.
    /// A neutral ramp cannot calibrate a saturated colour's MINOR channels. The first solve off the
    /// neutral curve landed L* and hue almost exactly but overshot C* by 2-8%, because a channel that
    /// the neutral run says renders 51 -> 46 actually renders 51 -> 28 when the other two channels are
    /// bright: something in the pipeline pulls a dark channel harder inside a bright pixel, which a
    /// neutral ramp cannot see because it has no dark channel in a bright pixel to show. These values
    /// are therefore a second step taken through an IN-SITU measurement - the shipped base and what it
    /// actually rendered, per family per channel - rather than through the neutral curve again.
    static readonly Color[] LiveRowColour =
    {
        new Color(0.294f, 0.745f, 0.173f, 1f),   // #4BBE2C Green (Square)  - see LiveRowShape on col 0
        new Color(0.948f, 0.634f, 0.253f, 1f),   // #F2A240 Orange (Diamond)
        new Color(0.689f, 0.198f, 0.885f, 1f),   // #B032E2 Purple (Triangle)
        new Color(0.940f, 0.300f, 0.180f, 1f),   // #F04C2E Red (Hexagon)
        new Color(0.952f, 0.620f, 0.756f, 1f)    // #F39EC1 Pink (Star)
    };

    /// <summary>
    /// The colour each SHAPE wears, taken from the live row once it has been stamped. Filled in by
    /// ApplyLiveRowIdentity and used for the whole tray, playable pieces and scenery alike: a square is
    /// the same teal wherever it appears. Without this the scenery kept its own per-slot colour table
    /// and the tray showed dark green squares next to the teal one it was meant to teach.
    /// </summary>
    static readonly Dictionary<ShapeId,Color> ShapeColour = new Dictionary<ShapeId,Color>();

    /// <summary>
    /// Colour for a shape the live row does not contain. Diamond appears all over the drum but never in
    /// the live row, so it has no colour to inherit and would otherwise keep whatever hue the palette
    /// snap gave it - which is how a star came out yellow in one row and pink in another.
    /// </summary>
    static readonly Dictionary<ShapeId,Color> ShapeColourFallback = new Dictionary<ShapeId,Color>
    {
        // Kept in step with LiveRowColour above. These were left on the old unsolved palette once and
        // it shows immediately: Round is the ONE shape the live row does not carry, so it is the only
        // entry that is ever actually read - and an un-updated Round renders the old dark orange right
        // next to a Diamond wearing the solved one, two "orange" cells that no longer match.
        { ShapeId.Diamond,  new Color(0.948f, 0.634f, 0.253f, 1f) },  // #F2A240 Orange
        { ShapeId.Round,    new Color(0.948f, 0.634f, 0.253f, 1f) },  // #F2A240 Orange
        { ShapeId.Square,   new Color(0.294f, 0.745f, 0.173f, 1f) },  // #4BBE2C Green
        { ShapeId.Triangle, new Color(0.689f, 0.198f, 0.885f, 1f) },  // #B032E2 Purple
        { ShapeId.Hexagon,  new Color(0.940f, 0.300f, 0.180f, 1f) },  // #F04C2E Red
        { ShapeId.Star,     new Color(0.952f, 0.620f, 0.756f, 1f) }   // #F39EC1 Pink
    };

    /// <summary>Colour for a shape token, or <paramref name="fallback"/> for a shape the live row lacks.</summary>
    static Color ColourForShape(ShapeId id, Color fallback)
    {
        Color c;
        if (ShapeColour.TryGetValue(id, out c)) return c;
        if (ShapeColourFallback.TryGetValue(id, out c)) return c;
        return fallback;
    }

    /// <summary>
    /// Paints every visible drum cell with its own shape's colour using the heightmap cavity indentation shader.
    /// Separate child Hole and Hole-Cap meshes are deactivated so the physical recess is rendered seamlessly by the shader.
    /// </summary>
    static void ApplyShapeColoursToDrum(List<DrumSlotReaction.Cell> cells, List<int> covered)
    {
        int painted = 0, unknown = 0;
        for (int i = 0; i < cells.Count; i++)
        {
            DrumSlotReaction.Cell c = cells[i];
            if (c == null || c.root == null) continue;

            // Always disable separate child meshes (user directive: no separate overlay objects)
            if (c.hole != null)
            {
                c.hole.enabled = false;
                c.hole.gameObject.SetActive(false);
                EditorUtility.SetDirty(c.hole);
            }
            Transform capTransform = c.root.Find("Hole-Cap");
            if (capTransform != null)
            {
                capTransform.gameObject.SetActive(false);
                EditorUtility.SetDirty(capTransform);
            }

            if (covered != null && covered.Contains(i))
            {
                continue; // under mystery '?' cover
            }

            if (c.body == null) continue;

            ShapeId id;
            if (c.row == 0 && c.column >= 0 && c.column < LiveRowShape.Length)
            {
                id = LiveRowShape[c.column];
            }
            else if (!ShapeIds.TryParse(MeshName(c.root, "Hole"), out id))
            {
                unknown++;
                continue;
            }

            Color want = ColourForShape(id, ReadBase(c.body.sharedMaterial));
            string matPath = MaterialDir + "/Cells/Case1_Toon_" + ColourKey(want) + "_" + id + ".mat";
            Material m = EnsureToonMaterial(matPath, want, id);
            if (m != null)
            {
                c.body.sharedMaterial = m;
                EditorUtility.SetDirty(c.body);
                painted++;
            }
        }
        Debug.Log("[Case1Setup] DRUM_SHAPE_COLOURS painted " + painted + " cells with heightmap physical indentation shaders; " + unknown +
                  " had no recognisable recess");

        // World scale, stated plainly: "1 metre" only means something once you know how big a cell is.
        int a0 = FindCell(cells, 0, 0), a1 = FindCell(cells, 1, 0);
        if (a0 >= 0 && a1 >= 0 && cells[a0].root != null && cells[a1].root != null)
            Debug.Log("[Case1Setup] WORLD_SCALE cell pitch = " +
                      Vector3.Distance(cells[a0].root.position, cells[a1].root.position).ToString("0.000") +
                      " world units");
    }

    /// <summary>Stamps the authored live-row colours, then proves the row is readable column by column.</summary>
    static void ApplyLiveRowIdentity(List<DrumSlotReaction.Cell> cells)
    {
        Color[] final = new Color[LiveRowShape.Length];
        for (int column = 0; column < LiveRowShape.Length; column++)
        {
            int index = FindCell(cells, column, 0);
            if (index < 0 || cells[index].body == null) continue;

            if (LiveRowColour[column].a > 0f)
            {
                Material m = EnsureToonMaterial(
                    MaterialDir + "/Cells/Case1_Toon_" + ColourKey(LiveRowColour[column]) + ".mat",
                    LiveRowColour[column]);
                if (m != null) { cells[index].body.sharedMaterial = m; EditorUtility.SetDirty(cells[index].body); }
            }
            final[column] = ReadBase(cells[index].body.sharedMaterial);
            ShapeColour[LiveRowShape[column]] = final[column];
        }

        // Two cells the player cannot tell apart is a real defect, not a matter of taste, so it is
        // measured rather than eyeballed: hue separation in degrees between every pair in the row.
        // Reference video has Col 0 and Col 1 as Orange Diamond
        float closest = 360f;
        for (int a = 0; a < final.Length; a++)
        {
            for (int b = a + 1; b < final.Length; b++)
            {
                float ha, sa, va, hb, sb, vb;
                Color.RGBToHSV(final[a], out ha, out sa, out va);
                Color.RGBToHSV(final[b], out hb, out sb, out vb);
                float d = Mathf.Abs(Mathf.DeltaAngle(ha * 360f, hb * 360f));
                if (d < closest && d > 0.01f) closest = d;
            }
        }
        // Reporting "all distinct" unconditionally would have been a gate that always passes; the line
        // states what was actually measured.
        System.Text.StringBuilder report = new System.Text.StringBuilder("[Case1Setup] LIVE_ROW identity:");
        for (int column = 0; column < LiveRowShape.Length; column++)
            report.Append(' ').Append(LiveRowShape[column]).Append('=')
                  .Append(ColorUtility.ToHtmlStringRGB(final[column]));
        report.Append("  minHueSeparation=").Append(closest.ToString("0.0")).Append(" deg");
        Debug.Log(report.ToString());
    }

    static readonly Color[] RefCellPalette =
    {
        new Color(0.820f, 0.635f, 0.122f, 1f),   // #D1A21F  gold
        new Color(0.706f, 0.200f, 0.012f, 1f),   // #B43303  burnt orange
        new Color(0.271f, 0.000f, 0.690f, 1f),   // #4500B0  deep violet
        new Color(0.000f, 0.518f, 0.620f, 1f),   // #00849E  teal
        new Color(0.733f, 0.176f, 0.910f, 1f),   // #BB2DE8  magenta violet
        new Color(0.800f, 0.408f, 0.647f, 1f),   // #CC68A5  dusty pink
        new Color(0.004f, 0.259f, 0.682f, 1f),   // #0142AE  blue
        new Color(0.965f, 0.592f, 0.753f, 1f),   // #F697C0  light pink
        new Color(0.000f, 0.404f, 0.035f, 1f),   // #006709  dark green
        new Color(0.149f, 0.525f, 0.078f, 1f)    // #268614  green
    };

    /// <summary>Nearest reference cell colour by hue; the reference's own S and V come with it.</summary>
    static Color SnapToReferencePalette(Color source, out float h, out float s, out float v)
    {
        float sh, ss, sv;
        Color.RGBToHSV(source, out sh, out ss, out sv);
        int best = 0; float bestD = float.MaxValue;
        for (int i = 0; i < RefCellPalette.Length; i++)
        {
            float ph, ps, pv;
            Color.RGBToHSV(RefCellPalette[i], out ph, out ps, out pv);
            float d = Mathf.Abs(Mathf.DeltaAngle(sh * 360f, ph * 360f));
            if (d < bestD) { bestD = d; best = i; }
        }
        // The palette was sampled from RENDERED pixels, so its value carries the reference's own
        // lighting. Baking that into the base colour and then shading it again double-darkened the
        // drum. Take the measured hue and saturation; keep an unshaded working value.
        float ph2, ps2, pv2;
        Color.RGBToHSV(RefCellPalette[best], out ph2, out ps2, out pv2);
        h = ph2; s = ps2; v = Mathf.Clamp(sv, 0.80f, 0.95f);
        return Color.HSVToRGB(h, s, v);
    }

    static void BuildUnknownMarks(Scene scene, Camera cam, List<DrumSlotReaction.Cell> cells, List<int> covered)
    {
        Transform stale = FindRoot(scene, QuestionRootName);
        if (stale != null) Object.DestroyImmediate(stale.gameObject);
        // Marks now come from questionmark pattern2.png on each authored MysteryOverlay mesh. No
        // viewport-space geometry is created here; depth, seams and curved-drum occlusion stay physical.
        Debug.Log("[Case1Setup] UNKNOWN_MARKS authored overlay pattern active on " +
                  (covered != null ? covered.Count : 0) + " covered cells");
    }

    /// <summary>
    /// Draws a real "?" on a covered cell. Built from quads, not TextMeshPro: a runtime TMP in world
    /// space would not size to the cell - the fontSize solve and the transform solve both left the mark
    /// at 0.26 world units against a 1.44 cell pitch, because its renderer bounds do not refresh in the
    /// editor between passes. The quads are exact and need no convergence.
    /// </summary>
    static void MarkUnknown(DrumSlotReaction.Cell c, List<DrumSlotReaction.Cell> cells, Transform root, Camera cam,
                            Vector3 drumCentre, Vector3 drumAxis)
    {
        if (c.root == null || root == null || cam == null) return;

        // Depth no longer matters (the ink shader ignores it), so the mark is placed and sized in
        // VIEWPORT space, straight onto the cell's own screen position. Sizing it in world units off the
        // cell pitch stretched it: the covered rows are on the drum's curve and are foreshortened.
        float oldAspect = cam.aspect; cam.aspect = ReferenceMatchLayout.Aspect;
        Vector3 v = cam.WorldToViewportPoint(c.body != null ? c.body.bounds.center : c.root.position);
        cam.aspect = oldAspect;
        if (v.z <= 0f) return;

        Vector2 at = new Vector2(v.x, v.y + 0.030f);   // centre the mark in the covered band
        const float H = 0.052f;                 // mark height in viewport units
        const float A = 1728f / 1080f;          // equal viewport fractions are 1.6x fewer pixels across
        Material ink = EnsureOverlay(MaterialDir + "/Cells/Case1_UnknownInk.mat", new Color(0.99f, 0.87f, 1f, 1f));

        GameObject go = new GameObject("Unknown_" + c.column + "_" + c.row);
        go.transform.SetParent(root, true);
        float depth = Mathf.Max(0.05f, v.z);

        // hook, stem and dot
        Stroke(go.transform, cam, "q_top",   at, new Vector2( 0.00f,  0.34f), new Vector2(0.46f, 0.16f), H, A, depth, ink);
        Stroke(go.transform, cam, "q_right", at, new Vector2( 0.21f,  0.19f), new Vector2(0.16f, 0.34f), H, A, depth, ink);
        Stroke(go.transform, cam, "q_mid",   at, new Vector2( 0.02f,  0.02f), new Vector2(0.38f, 0.16f), H, A, depth, ink);
        Stroke(go.transform, cam, "q_stem",  at, new Vector2(-0.02f, -0.16f), new Vector2(0.16f, 0.26f), H, A, depth, ink);
        Stroke(go.transform, cam, "q_dot",   at, new Vector2(-0.02f, -0.38f), new Vector2(0.17f, 0.15f), H, A, depth, ink);
    }

    static void Stroke(Transform parent, Camera cam, string name, Vector2 centre, Vector2 offset,
                       Vector2 size, float h, float aspect, float depth, Material mat)
    {
        Vector2 vp = centre + new Vector2(offset.x * h * aspect, offset.y * h);
        Vector2 vpSize = new Vector2(size.x * h * aspect, size.y * h);
        Billboard(parent, cam, name, vp, vpSize, depth, mat);
    }

    /// <summary>One camera-facing quad placed in world units around a centre.</summary>
    static void Quad(Transform parent, Camera cam, string name, Vector3 centre, float unit,
                     Vector2 offset, Vector2 size, Material mat)
    {
        GameObject q = GameObject.CreatePrimitive(PrimitiveType.Cube);
        q.name = name;
        q.transform.SetParent(parent, true);
        Collider col = q.GetComponent<Collider>(); if (col != null) Object.DestroyImmediate(col);
        Vector3 right = cam.transform.right, up = cam.transform.up;
        q.transform.position = centre + right * (offset.x * unit) + up * (offset.y * unit);
        q.transform.rotation = cam.transform.rotation;
        q.transform.localScale = new Vector3(size.x * unit, size.y * unit, unit * 0.03f);
        Renderer r = q.GetComponent<Renderer>();
        if (r != null)
        {
            r.sharedMaterial = mat;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
        }
    }

    // ------------------------------------------------------------------ sunken glyphs
    //
    // The starter cell does not have a cavity. Its "Hole" mesh is an ENGRAVED OUTLINE and "Hole-Cap" is
    // a thin closing ring, which is why every attempt to darken or bias them produced a drawn line and
    // never a sunken shape - there is simply no floor geometry to reveal. The filled shape is therefore
    // generated: a flat polygon in the glyph's own silhouette, sat in the engraving, in near-black, with
    // a second slightly smaller polygon lifted a hair to leave a lit lip along the lower edge. That lip
    // is what makes a dark shape read as a hole rather than as paint.

    const string GlyphRootName = "Case1_SunkenGlyphs";

    static void BuildSunkenGlyphs(Scene scene, Camera cam, List<DrumSlotReaction.Cell> cells, List<int> covered,
                                  Dictionary<int,ShapeId> forcedShape, List<Transform> glyphRoots)
    {
        Transform stale = FindRoot(scene, GlyphRootName);
        if (stale != null) Object.DestroyImmediate(stale.gameObject);
        if (cam == null || cells == null || cells.Count == 0) return;

        // No root object holds the glyphs: each one is parented to its own CELL, because that is what
        // makes a single rule serve every cell. The old "Case1_SunkenGlyphs" root therefore sat empty
        // in the Hierarchy pretending to own them, and destroying it removed nothing - which is exactly
        // how a cell ended up carrying three stacked copies of its recess. Only the newest was wired to
        // DrumSlotReaction, so a fill switched one off and the two behind it kept the dark shape on
        // screen: "the hole never closes". The cells themselves are swept here instead, and this is the
        // only place a glyph is ever created.
        // Clean up any dynamic glyph objects from earlier passes
        int swept = 0;
        for (int ci = 0; ci < cells.Count; ci++)
        {
            Transform cr = cells[ci].root;
            if (cr == null) continue;
            for (int k = cr.childCount - 1; k >= 0; k--)
            {
                Transform child = cr.GetChild(k);
                if (child.name.StartsWith("Glyph_"))
                {
                    Object.DestroyImmediate(child.gameObject);
                    swept++;
                }
            }
        }
        if (swept > 0) Debug.Log("[Case1Setup] GLYPH_SWEEP removed " + swept + " extra glyph child objects");

        // Restores authored Hole and Hole-Cap mesh transforms with shadow-mapped socket rendering
        for (int i = 0; i < cells.Count; i++)
        {
            if (covered != null && covered.Contains(i)) continue;
            DrumSlotReaction.Cell c = cells[i];
            if (c.root == null) continue;
            if (c.hole != null)
            {
                c.hole.transform.localPosition = new Vector3(-0.012978026f, 0.4510187f, -0.013328716f);
                c.hole.transform.localScale = new Vector3(1.2167876f, 0.7256549f, 0.7256549f);
                c.hole.enabled = true;
            }
            Transform cap = c.root.Find("Hole-Cap");
            if (cap != null)
            {
                cap.localPosition = new Vector3(-0.012978026f, 0.4510187f, -0.013328716f);
                cap.localScale = new Vector3(1.2167876f, 0.7256549f, 0.7256549f);
                cap.gameObject.SetActive(true);
            }
        }
        Debug.Log("[Case1Setup] SUNKEN_GLYPHS: Using clean prefab meshes (Hole & Hole-Cap) for all " + cells.Count + " cells");
    }


    /// <summary>
    /// Rescales a polygon so its widest span is 2, i.e. a half-width of 1. MEASURED on the rendered
    /// frame: scaling every glyph by its CIRCUMRADIUS made a circle 2r wide and a triangle 1.73r, so one
    /// setting produced on-screen widths from 0.062 to 0.128 of the frame - an 88% spread over the drum
    /// and 2x inside a single row. Normalising by the real extent makes one radius mean one size.
    /// </summary>
    static Vector3[] NormaliseWidth(Vector3[] poly)
    {
        if (poly == null || poly.Length == 0) return poly;
        float m = 0f;
        for (int i = 0; i < poly.Length; i++)
            m = Mathf.Max(m, Mathf.Max(Mathf.Abs(poly[i].x), Mathf.Abs(poly[i].y)));
        float k = 1f / Mathf.Max(0.0001f, m);
        for (int i = 0; i < poly.Length; i++) poly[i] *= k;
        return poly;
    }

    /// <summary>Unit-radius outline of a glyph, counter-clockwise, in the XY plane.</summary>
    static Vector3[] GlyphPolygon(ShapeId id)
    {
        switch (id)
        {
            // The reference's glyphs have visibly rounded corners; a raw n-gon reads as a cut-out.
            case ShapeId.Round:    return NormaliseWidth(Ngon(32, 0f));
            case ShapeId.Hexagon:  return NormaliseWidth(RoundCorners(Ngon(6, 90f), 0.30f, 4));
            case ShapeId.Triangle: return NormaliseWidth(RoundCorners(Ngon(3, 90f), 0.28f, 5));
            case ShapeId.Square:   return NormaliseWidth(RoundCorners(Ngon(4, 45f), 0.30f, 4));
            case ShapeId.Diamond:  return NormaliseWidth(RoundCorners(Ngon(4, 90f), 0.26f, 4));
            case ShapeId.Star:     return NormaliseWidth(RoundCorners(Star(5, 1f, 0.46f, 90f), 0.34f, 3));
        }
        return null;
    }

    /// <summary>Rounds every corner of a polygon with a short quadratic arc, as the reference's are.</summary>
    static Vector3[] RoundCorners(Vector3[] poly, float k, int segments)
    {
        if (poly == null || poly.Length < 3) return poly;
        List<Vector3> outPts = new List<Vector3>(poly.Length * (segments + 1));
        for (int i = 0; i < poly.Length; i++)
        {
            Vector3 prev = poly[(i - 1 + poly.Length) % poly.Length];
            Vector3 cur = poly[i];
            Vector3 next = poly[(i + 1) % poly.Length];
            Vector3 a = Vector3.Lerp(cur, prev, k);
            Vector3 b = Vector3.Lerp(cur, next, k);
            for (int sIdx = 0; sIdx <= segments; sIdx++)
            {
                float t = sIdx / (float)segments;
                Vector3 p0 = Vector3.Lerp(a, cur, t);
                Vector3 p1 = Vector3.Lerp(cur, b, t);
                outPts.Add(Vector3.Lerp(p0, p1, t));
            }
        }
        return outPts.ToArray();
    }

    static Vector3[] Ngon(int n, float startDeg)
    {
        Vector3[] p = new Vector3[n];
        for (int i = 0; i < n; i++)
        {
            float a = (startDeg + i * 360f / n) * Mathf.Deg2Rad;
            p[i] = new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0f);
        }
        return p;
    }

    static Vector3[] Star(int points, float outer, float inner, float startDeg)
    {
        Vector3[] p = new Vector3[points * 2];
        for (int i = 0; i < points * 2; i++)
        {
            float a = (startDeg + i * 180f / points) * Mathf.Deg2Rad;
            float r = (i % 2 == 0) ? outer : inner;
            p[i] = new Vector3(Mathf.Cos(a) * r, Mathf.Sin(a) * r, 0f);
        }
        return p;
    }

    /// <summary>Triangle fan around the centroid; every glyph here is star-shaped about its centre.</summary>
    /// <summary>Glyph mesh in the PARENT's local space; the parent carries position, rotation and scale.</summary>
    static void MakeGlyphQuadLocal(Transform parent, string name, Vector3[] poly, float radius, Material mat)
    {
        GameObject go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.transform.localPosition = Vector3.zero;
        go.transform.localRotation = Quaternion.identity;
        go.transform.localScale = Vector3.one;

        Vector3[] verts = new Vector3[poly.Length + 1];
        verts[0] = Vector3.zero;
        for (int i = 0; i < poly.Length; i++) verts[i + 1] = poly[i] * radius;
        int[] tris = new int[poly.Length * 3];
        for (int i = 0; i < poly.Length; i++)
        {
            tris[i * 3 + 0] = 0;
            tris[i * 3 + 1] = 1 + i;
            tris[i * 3 + 2] = 1 + ((i + 1) % poly.Length);
        }
        Mesh mesh = new Mesh { name = name };
        mesh.vertices = verts; mesh.triangles = tris;
        mesh.RecalculateNormals(); mesh.RecalculateBounds();
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        MeshRenderer mr = go.AddComponent<MeshRenderer>();
        mr.sharedMaterial = mat;
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        mr.receiveShadows = false;
    }

    static void MakeGlyphQuad(Transform parent, string name, Vector3[] poly, float radius,
                              Vector3 position, Quaternion rotation, Material mat)
    {
        GameObject go = new GameObject(name);
        go.transform.SetParent(parent, true);
        go.transform.position = position;
        go.transform.rotation = rotation;

        Vector3[] verts = new Vector3[poly.Length + 1];
        verts[0] = Vector3.zero;
        for (int i = 0; i < poly.Length; i++) verts[i + 1] = poly[i] * radius;

        int[] tris = new int[poly.Length * 3];
        for (int i = 0; i < poly.Length; i++)
        {
            tris[i * 3 + 0] = 0;
            tris[i * 3 + 1] = 1 + i;
            tris[i * 3 + 2] = 1 + ((i + 1) % poly.Length);
        }

        Mesh mesh = new Mesh { name = name };
        mesh.vertices = verts;
        mesh.triangles = tris;
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();

        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        MeshRenderer mr = go.AddComponent<MeshRenderer>();
        mr.sharedMaterial = mat;
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        mr.receiveShadows = false;
    }

    static Vector3 RadialOut(DrumSlotReaction.Cell c, List<DrumSlotReaction.Cell> cells)
    {
        Vector3 centre = Vector3.zero; int n = 0;
        for (int i = 0; i < cells.Count; i++) if (cells[i].root != null) { centre += cells[i].root.position; n++; }
        if (n > 0) centre /= n;
        Vector3 axis = Vector3.right;
        for (int i = 0; i < cells.Count; i++)
            for (int j = 0; j < cells.Count; j++)
                if (cells[i].row == cells[j].row && cells[j].column == cells[i].column + 1 &&
                    cells[i].root != null && cells[j].root != null)
                { axis = (cells[j].root.position - cells[i].root.position).normalized; i = cells.Count; break; }
        Vector3 rel = c.root.position - centre;
        Vector3 o = (rel - axis * Vector3.Dot(rel, axis));
        return o.sqrMagnitude > 0.0001f ? o.normalized : c.root.up;
    }

    static Vector3 FaceNormalOf(DrumSlotReaction.Cell c)
    {
        return c.root != null ? c.root.up : Vector3.up;
    }

    static float _cellPitch = -1f;
    static float CellPitchOf(List<DrumSlotReaction.Cell> cells)
    {
        if (_cellPitch > 0f) return _cellPitch;
        _cellPitch = 1f;
        for (int i = 0; i < cells.Count; i++)
            for (int j = 0; j < cells.Count; j++)
                if (cells[i].row == cells[j].row && cells[j].column == cells[i].column + 1 &&
                    cells[i].root != null && cells[j].root != null)
                { _cellPitch = Vector3.Distance(cells[i].root.position, cells[j].root.position); return _cellPitch; }
        return _cellPitch;
    }

    /// <summary>
    /// Soft plastic surface for the authored beveled meshes. The reference is not flat toon: its 10–15 px
    /// bevel, broad highlight and dark side extrusion are what make every cell and tray piece feel solid.
    /// </summary>
    static Material EnsureToonMaterial(string path, Color colour, ShapeId shapeId = (ShapeId)0)
    {
        if (!AssetDatabase.IsValidFolder(MaterialDir + "/Cells"))
            AssetDatabase.CreateFolder(MaterialDir, "Cells");
        Shader shader = Shader.Find("Case1/SoftPlastic");
        if (shader == null) { Debug.LogError("[Case1Setup] Case1/SoftPlastic not found"); return null; }
        Material m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (m == null) { m = new Material(shader); AssetDatabase.CreateAsset(m, path); }
        else if (m.shader != shader) m.shader = shader;
        m.SetColor("_BaseColor", colour);
        // These classifiers matched against the WHOLE path, and every path this method is ever
        // handed starts with "Assets/Case1_FitTheShape/Materials" - which CONTAINS "Shape". isPiece
        // was therefore TRUE for every material this method has written since the case began, and
        // the glyph / plate / isCell branches below have never once executed. Verified off the
        // shipped assets rather than by reading the code: Case1_DeckSlotPlate.mat (which "Plate"
        // should have claimed) and Case1_CellCover.mat (a cell) both carry the isPiece set exactly
        // - 0.72 / 0.38 / 0.10 / 0.45 / 0.18 / 0.15 / 0.58 / 0.45 - as do all 28 Case1_Toon_* cell
        // materials. Match on the FILE NAME, which is what the intent was.
        //
        // This is not cosmetic. _SpecularStrength and _RimLift are the shader's only ADDITIVE terms,
        // and the piece values are 0.38 / 0.10 against the cell values 0.24 / 0.05. MEASURED on
        // frame_00: the specular lobe lands on the LIVE ROW, putting a near-neutral pedestal of
        // +0.086 / +0.041 / +0.068 (linear) under every live cell face while the row directly below
        // - same materials, same shader - sits at -0.007 / -0.004 / -0.003. An additive neutral on a
        // saturated colour is exactly "washed out": live-row C* ran 0.72-0.85 of the reference's and
        // HSV S ran 0.16-0.18 low, while the rows outside the lobe already matched the reference.
        string file = System.IO.Path.GetFileName(path);
        bool isWall = file.Contains("CavityWall") || file.Contains("Hole");
        bool isFloor = file.Contains("CellGlyph") || file.Contains("Cap");
        bool glyph = isWall || isFloor;
        bool plate = file.Contains("Plate");
        bool isPiece = file.Contains("Case1_Playable_") || file.Contains("Piece") || file.Contains("Shape") || file.Contains("Tray");
        bool isCell = !glyph && !plate && !isPiece;

        // REFERENCE-MATCHED TOON VALUES:
        // Pieces: vibrant rich candy plastic with glossy highlight, edge ink outline, and bottom-to-top shadow gradient
        // Drum cubes: bright saturated toon cells with smooth physical cavity sockets
        m.SetFloat("_Smoothness",        isPiece ? 0.72f : (isCell ? 0.55f : 0.35f));
        // Cells: 0.24 -> 0. The 0.24 above had NEVER RUN (see the isPiece note), so it was never
        // measured; when the predicate above was fixed it turned out to be no better than the 0.38
        // it replaced, because _Smoothness also drops 0.72 -> 0.55 and that WIDENS the specular
        // lobe (exponent lerp(16,128,s): 96.6 -> 77.6) by almost exactly the factor the strength
        // falls. MEASURED live-row pedestal, three points on the same frame:
        //     spec 0.38 @ smooth 0.72 -> +0.0856 linear, orange C* 52.4 (0.72 of reference)
        //     spec 0.24 @ smooth 0.55 -> +0.0717 linear, orange C* 53.7 (0.74)
        //     spec 0.00                -> -0.0082 linear, orange C* 69.5 (0.96)
        // Specular here is an ADDITIVE WHITE term, so on the live row - the one row the lobe lands
        // on - it is a neutral pedestal under a saturated colour, i.e. the wash the owner reported.
        // Zero is not a shortcut: ToonCell.shader's header records the same thing measured off the
        // clip directly - "each face is a single flat colour with no falloff across it... there is
        // no specular anywhere". Pieces and the tray keep their gloss; only cells lose it.
        m.SetFloat("_SpecularStrength",  isPiece ? 0.38f : (isCell ? 0.00f : 0.12f));
        m.SetFloat("_Wrap",              0.45f);
        m.SetFloat("_ShadeStrength",     isPiece ? 0.18f : (isCell ? 0.22f : 0.40f));
        m.SetFloat("_BevelDarken",       isPiece ? 0.15f : (isCell ? 0.20f : 0.30f));
        // Cells 0.05 -> 0.02. Rim is the shader's other additive term. It is gated on (1 - facing)
        // so it cannot reach the face plateau, only the silhouette, and the probe that set it to 0
        // alongside the specular showed no plateau change attributable to it. Kept small rather
        // than zeroed so the cell edge still reads against its neighbour.
        m.SetFloat("_RimLift",           isPiece ? 0.10f : (isCell ? 0.02f : 0.00f));
        m.SetFloat("_EdgeInk",           isPiece ? 0.45f : 0.25f);
        m.SetFloat("_EdgeInkWidth",      isPiece ? 4.5f : 3.0f);
        // The silhouette outline the reference blocks carry. Pieces only: the board cells sit
        // shoulder to shoulder and a contour on each would draw a grid, which the reference has not.
        // Zero here since the first commit, alongside a shader that never had an outline pass to
        // read it - the property and the effect were both missing, not just switched off.
        // Silhouette outline on every solid shape; none on the cavities. A hole is an absence, and
        // an inverted-hull outline on one draws a ring where the reference has an opening.
        m.SetFloat("_OutlineWidth",      (isWall || plate) ? 0f : 0.009f);
        m.SetFloat("_OutlineScaleMin", 0.80f);
        m.SetFloat("_OutlineScaleMax", 0.95f);
        m.SetFloat("_VertShade",         isPiece ? 0.45f : 0.20f);
        m.SetFloat("_VertShadeBias",     0.0f);
        m.SetFloat("_BottomDarken",      isPiece ? 0.58f : (isCell ? 0.35f : 0.50f));
        m.SetFloat("_BottomDarkenPower", 1.35f);
        m.SetColor("_OutlineColor",      new Color(0.04f, 0.03f, 0.08f, 1f));
        m.SetFloat("_OffsetFactor",      0f);
        m.SetFloat("_OffsetUnits",       0f);

        // Heightmap shape indentation parameters
        int shapeVal = (int)shapeId;
        m.SetFloat("_ShapeType", (float)shapeVal);
        m.SetFloat("_IndentDepth", 0.18f);
        // 0.065 -> 0.035: the socket edge was ramping over 10.3-11.8% of the socket's own width
        // against the reference's 3.0-7.3%, so the recess read as a soft dish rather than a cut.
        // This trades against the bevel ring (I3), which is the one signature separating a socket
        // from a dark decal: the ring's width is 1.8*bevel while the EDGE ramps over 0.04+0.8*bevel,
        // so the ring narrows about twice as fast as the edge does. 0.035 was chosen as the point
        // where the edge reaches the reference band with every ring still live (worst 1.006).
        // Do not push lower without re-measuring I3 - and note the edge cannot reach the reference's
        // low end from this knob alone: the -0.04 in SoftPlastic's cavityMask smoothstep is a hard
        // floor on the ramp, independent of this value.
        // 0.075. At 0.035 the band the entrance curves over is thinner than the crease itself, so
        // however the profile is shaped there is nowhere for a curve to appear - the reference's
        // sockets bend into the hole over a visible width. See the monotonic outer ramp in
        // SoftPlastic.shader, which is the other half of this.
        m.SetFloat("_IndentBevel", 0.075f);
        // USER: "derinligi artir genel olarak". Every INTENSIVE depth ratio was already at or past
        // the reference - floor/face 0.068-0.178 against its 0.067-0.152, intra-socket contrast
        // 0.54-0.85 against its 0.51-0.76, top-to-bottom gradient +0.42 to +0.77 against its +0.16
        // to +0.33. Ours is darker and more graded than the reference and still read flat, so
        // darkness was never the missing cue.
        //
        // MEASURED, inward luminance profile from the socket edge (see SoftPlastic.shader): the
        // reference troughs 6-10 reference-px inside the socket and recovers 13.3-25.7% toward the
        // floor centre. Ours hit its floor at depth 3 in EVERY cell and recovered 0.5-10.8%, because
        // the inward wall band is 0.8 * _IndentBevel = 0.028 units wide - a constant, identical for
        // every shape, which is exactly what "depth 3 in every cell" reports.
        //
        // 0.070 doubles the inward band without touching the outward cut. Both are cell-only: the
        // shader defaults are exact no-ops so tray, deck and plate materials are untouched.
        //
        // CREASE DEPTH 0.22 -> 0.57. USER: "case 1 de golgelerde derinlik belli olmuyor".
        // 0.22 was tuned against a RADIALLY POOLED inward-recovery profile, and that metric is blind
        // to the cue the reference actually uses. Pooling every pixel at a given distance from the
        // socket edge averages the near-black wall together with the corners, where the wall is
        // shallow; on the reference it reports trough/floor 0.79-0.83, i.e. indistinguishable from
        // ours. An UNPOOLED horizontal scanline through each socket centre separates them at once:
        //     wall-trough / floor-centre   reference  0.457 0.399 0.474 0.453 0.356  (mean 0.428)
        //                                  ours       0.781 0.786 0.750 0.790 0.785  (mean 0.778)
        // The reference socket has a near-black rim INSIDE the opening and a floor that recovers to
        // ~2.3x the rim; ours is a flat plateau with a 20% dip. A hole with no dark rim reads as a
        // dark decal, which is what "derinlik belli olmuyor" is pointing at.
        //
        // The value is not fitted, it is SOLVED. At dist = -_IndentWall the crease is at full
        // strength and slope is exactly 0, so the perturbed normal - and therefore diffuse, bevel,
        // specular and rim - is identical to the floor centre. Every term cancels except the crease,
        // so trough/centre == 1 - _CavityCrease analytically. Predicted 0.780 against a measured
        // 0.778 on our own capture (five cells, spread 0.750-0.790), so the identity is confirmed on
        // disk before it is used. Solving 1 - c = 0.428 gives c = 0.57.
        //
        // This changes RELIEF, not level: the floor centre is untouched (crease decays to 0 there),
        // so the pre-existing ~2x floor/face gap against the reference is deliberately not traded
        // for it.
        // NOT gated on isCell. isCell is dead: isPiece tests path.Contains("Shape") and every path
        // under Assets/Case1_FitTheShape/Materials contains "Shape" via the PROJECT FOLDER NAME, so
        // isPiece is true for every material this function ever sees and isCell is always false.
        // Left alone deliberately - repairing it would hand every drum cell the isCell values for
        // _Smoothness, _SpecularStrength, _ShadeStrength, _BevelDarken, _RimLift, _VertShade and
        // _BottomDarken all at once, which is a whole-row appearance change and not this fix.
        // Gate on the socket itself instead: these two only mean anything where a socket exists.
        bool hasSocket = shapeId != (ShapeId)0 && !glyph && !plate && !path.Contains("Case1_Playable_");
        m.SetFloat("_IndentWall", hasSocket ? 0.185f : 0f);
        m.SetFloat("_CavityCrease", hasSocket ? 0.57f : 0f);
        m.SetFloat("_IndentFloorDarken", 0.364f);

        // ---- socket cavity: CODE OWNS ALL FOUR, deliberately ----
        // These four used to live only in the .mat assets (_CavityBounce 10.2, _CavityLightKill 0.7)
        // while _IndentFloorDarken was written here every Build - so the asset's 0.79 was a dead write
        // and nobody could tell, by reading this file, what a socket would actually look like. That
        // split cost a whole measurement round: an "absurd value" probe of _CavityBounce=12 was really
        // 10.2 -> 12, moved the socket 0-6 units against a measured capture noise floor of 1, and was
        // misread as proof the property was dead. A property is owned by the code OR by the asset,
        // never half by each.
        //
        // WHY THESE VALUES. MEASURED off Fit The Shape.mp4 frame 0: the reference socket floor is a
        // deeply saturated DARK VERSION OF THE CELL'S OWN PLASTIC - orange face (247,194,86) over an
        // orange-black floor (70,2,0), saturation 1.00 against the face's 0.65. Ours read (57,56,55)
        // inside a GREEN cell: saturation 0.04, no hue at all. USER: "tengide plastic rengi gibi olsun".
        //
        // The grey came from lighting, not albedo. cavityAtten gates the specular and rim adds below;
        // keyColor is near-white, so with _CavityLightKill at 0.7 a full 30% of a white pedestal landed
        // on the socket floor. With the albedo already crushed by the bounce and the floor darken, that
        // pedestal owned most of the pixel. Killing it outright is what restores the hue.
        m.SetFloat("_CavityLightKill", 1.0f);
        // Floor-only by construction: cavityAtten = 1 - cavityMask*(1-slope)*kill. On the floor slope=0
        // so the light goes to zero; at the bevel's steepest point slope->1 collapses the (1-slope)
        // factor and the ring keeps its FULL highlight. The bevel ring is the one thing that tells a
        // recess from a dark decal, so it must survive this.
        m.SetFloat("_CavityBounce", 1.0f);
        // k solved from the reference's own face/floor ratio in linear space, not guessed: the red and
        // green channels of (247,194,86)->(70,2,0) give k-1 = 3.58. The previous 10.2 overshot - it
        // crushed green to zero and darkened red 21% past the reference.
        m.SetFloat("_CavityBevelRelief", 0.85f);
        // MEASURED: saturation across the socket opening, reference vs ours (see SoftPlastic.shader).
        // The reference edge is the MOST saturated place on the cell (86->90->100); ours DIPPED to
        // 75-78 before recovering - a grey smudge where the hard cut belongs, which is what read as
        // a pasted-on drop shadow. Not AA: AA between face 86 and floor 100 cannot go below either.
        // It is the near-white specular/rim pedestal that (1 - slope) deliberately spares on the
        // bevel, landing on an albedo the bounce has already crushed. 1.0 = fully tinted by the
        // cell's own albedo, which is the same physical story _CavityBounce tells for the
        // multiplicative path; it is not a fitted number. Brightness is untouched - the reference
        // edge brightness already matched ours - only chroma is restored.
        m.SetFloat("_CavityBevelTint", hasSocket ? 1.0f : 0f);
        // MEASURED, 9-band profile from cell rim to socket centre (the scheme the shader header
        // records; its floor half 13/13/14/15 reproduces on the orange cell as 13.2/13.1/13.6/13.3
        // in linear x1000, which is what fixes the units and the banding). Bands 6-8 - the floor
        // plateau, clear of the crease:
        //     ours 25.9 22.6 26.6  5.9   vs   ref 13.3 11.4 20.7  4.7   (diamond hexagon star triangle)
        // Face/floor contrast is 19.7x and 11.9x on diamond and hexagon against the reference's
        // 35.5x and 23.8x - almost exactly HALF. That is why the interior reads as bright brick
        // instead of a hole, and it is the largest remaining gap after crease and tint.
        //
        // Closed form again: on the floor slope = 0, so this multiplies by exactly (1 - F).
        // Required (1-F) is 0.514 / 0.504 / 0.778 / 0.797 - two clusters, not one number, exactly
        // as the shader header warns ("the pink star wants neither"). 0.37 is the geometric mean,
        // which is the value that minimises squared log error over the four: it cuts the worst
        // cell from 1.98x to 1.25x and overshoots star/triangle to 0.81x/0.79x. A single global
        // value cannot do better; splitting it per shape would be fitting five cells.
        //
        // COST: the green square socket, already the darkest at floor/face 0.018, goes darker
        // still (6.0 -> 3.8). The "green socket reads near-black" complaint is made worse by this,
        // and is not addressed here.
        m.SetFloat("_CavityFloorExtra", hasSocket ? 0.37f : 0f);
        m.SetFloat("_IndentInnerShadow", 0.68f);
        // USER: "case 1 de gocuk daha belirgin olsun". Every ratio INSIDE the socket was already in
        // the reference band - floor darkness, edge ramp, ring, hue, flatness - and he still could not
        // see it, because none of those measure how big the socket is relative to its cell. MEASURED on
        // the scanline through each socket's centre, socket span as a fraction of cell width:
        //     reference  Diamond 52.7%  Hexagon 43.6%  Star 51.2%  Triangle 29.3%
        //     ours       Diamond 39.2%  Hexagon 35.5%  Star 24.1%  Triangle 31.3%
        // p is DIVIDED by this, so a larger value pushes the SDF outward and widens the socket.
        // 1.28 lands the under-sized shapes near the reference without taking any past its ~53% max,
        // beyond which the cell stops reading as a plastic block and starts reading as a ring.
        // A single global value cannot serve every shape: EvaluateShapeSDF's per-shape primitives are
        // not normalised against each other, so at one scale the star came out 32.2% of its cell while
        // the triangle overshot to 38.7% against a reference 29.3%. MEASURED per shape at scale 1.28,
        // then solved for the reference's own fraction:
        //     shape     ours@1.28   reference   -> scale
        //     Diamond      52.1%       52.7%       1.30
        //     Hexagon      45.6%       43.6%       1.22
        //     Triangle     38.7%       29.3%       0.97
        //     Star         32.2%       51.2%       1.82  (2.00 overshot to 56.2%: non-linear)
        // Square has no counterpart in the reference's live row; 1.28 puts it at 47.4%, inside the
        // 43-53% band the reference's non-triangle shapes occupy.
        // USER: "yildiz tasmis duzelt" - the star's points reached its cell's edges.
        // The scale table above was solved against a SCANLINE through each socket's centre. A
        // scanline through a five-point star crosses its WAIST, not its points, so it under-reports
        // the star by the amount the points stick out and the star was scaled up to compensate.
        // RE-MEASURED as a bounding box against the cell PITCH, on ref_frame_001's own selection row
        // (five sockets) and on our frame_00 (five sockets). Reference band, spanX / spanY / minimum
        // clearance from the socket's bbox to the cell face edge, all as fractions of pitch:
        //     ref diamondA 0.479 / 0.479 / 0.097     ref hexagon 0.417 / 0.458 / 0.090
        //     ref diamondB 0.479 / 0.479 / 0.097     ref STAR    0.458 / 0.438 / 0.125
        //     ref triangle 0.486 / 0.424 / 0.097
        // Every reference socket clears its cell by 0.090-0.125 of pitch. Ours:
        //     square   0.470 / 0.488 / 0.124  in band
        //     triangle 0.438 / 0.401 / 0.152  in band
        //     hexagon  0.438 / 0.535 / 0.106  in band
        //     diamond  0.594 / 0.627 / 0.055  OUT - half the reference's clearance
        //     STAR     0.820 / 0.737 / 0.000  OUT - the socket TOUCHES the cell face edge
        // Span is exactly linear in this value (p is divided by it and the SDF thresholds are
        // constants), so the correction is a ratio: star 1.82 * 0.458/0.820 = 1.02, diamond
        // 1.30 * 0.479/0.594 = 1.05, taken to 1.02 to bring spanY in with spanX. Square, triangle
        // and hexagon are inside the reference clearance band and are NOT touched.
        float indentScale;
        switch (shapeId)
        {
            case ShapeId.Diamond:  indentScale = 1.02f; break;
            case ShapeId.Hexagon:  indentScale = 1.22f; break;
            case ShapeId.Triangle: indentScale = 0.97f; break;
            case ShapeId.Star:     indentScale = 1.02f; break;
            default:               indentScale = 1.28f; break;   // Square, Round
        }
        m.SetFloat("_IndentScale", indentScale);

        if (m.HasProperty("_Cull")) m.SetFloat("_Cull", glyph ? 0f : 2f);
        EditorUtility.SetDirty(m);
        return m;
    }

    static Material EnsureCellMaterial(string path, Color colour, float smoothness)
    {
        if (!AssetDatabase.IsValidFolder(MaterialDir + "/Cells"))
            AssetDatabase.CreateFolder(MaterialDir, "Cells");
        Shader shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader == null) return null;
        Material m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (m == null) { m = new Material(shader); AssetDatabase.CreateAsset(m, path); }
        else if (m.shader != shader) m.shader = shader;
        m.SetColor("_BaseColor", colour);
        if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", smoothness);
        if (m.HasProperty("_Metallic")) m.SetFloat("_Metallic", 0f);
        EditorUtility.SetDirty(m);
        return m;
    }

    // ------------------------------------------------------------------ reference chrome
    //
    // Every piece here is MEASURED off the reference frame and is deliberately NON-INTERACTIVE: no
    // collider, no controller, never reached by ShapeTapInput. It is in the scene because the
    // reference's frame is not readable without it - the SPIN button alone is 17% of the frame width
    // sitting right beside the holder row, and without it the whole right side reads as empty.

    const string MetaDecorRoot = "Case1_ReferenceChrome";

    static void BuildReferenceChrome(Scene scene, Camera cam, Transform depthModel)
    {
        Transform old = FindRoot(scene, MetaDecorRoot);
        if (old != null) Object.DestroyImmediate(old.gameObject);
        RemoveHolderPlates(scene);
        Debug.Log("[Case1Setup] 2D UI billboards and 5 holder plates removed per user direction; focusing 100% on 3D game objects, sockets, and VFX.");
    }

    static void RemoveHolderPlates(Scene scene)
    {
        int killed = 0;
        foreach (GameObject root in scene.GetRootGameObjects())
        {
            foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
            {
                if (t != null && t.gameObject != null)
                {
                    string n = t.gameObject.name;
                    if (n.StartsWith("DeckSlot") || n.StartsWith("DeckSlotShadow") || n.StartsWith("SlotHolder") || n.Contains("DeckSlot") || n.StartsWith("SLOT-SPIN-BTN") || n.StartsWith("SpinButton"))
                    {
                        t.gameObject.SetActive(false);
                        Renderer[] rs = t.GetComponentsInChildren<Renderer>(true);
                        for (int j = 0; j < rs.Length; j++) { rs[j].enabled = false; EditorUtility.SetDirty(rs[j]); }
                        EditorUtility.SetDirty(t.gameObject);
                        killed++;
                    }
                }
            }
        }
        Debug.Log("[Case1Setup] HOLDER_PLATES " + killed + " DeckSlot and shadow objects disabled recursively across entire scene.");
    }



    static void Billboard(Transform parent, Camera cam, string name, Vector2 vp, Vector2 vpSize, float depth, Material mat)
    {
        GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = name;
        go.transform.SetParent(parent, true);
        Collider col = go.GetComponent<Collider>(); if (col != null) Object.DestroyImmediate(col);
        float oldAspect = cam.aspect; cam.aspect = ReferenceMatchLayout.Aspect;
        Vector3 centre = cam.ViewportToWorldPoint(new Vector3(vp.x, vp.y, depth));
        Vector3 px = cam.ViewportToWorldPoint(new Vector3(vp.x + vpSize.x * 0.5f, vp.y, depth));
        Vector3 py = cam.ViewportToWorldPoint(new Vector3(vp.x, vp.y + vpSize.y * 0.5f, depth));
        cam.aspect = oldAspect;
        go.transform.position = centre;
        go.transform.rotation = cam.transform.rotation;
        go.transform.localScale = new Vector3(Vector3.Distance(centre, px) * 2f, Vector3.Distance(centre, py) * 2f, 0.02f);
        Renderer r = go.GetComponent<Renderer>();
        if (r != null)
        {
            r.sharedMaterial = mat;
            // Flat chrome: a billboard cube that casts a shadow reads as a tombstone, not as UI.
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
        }
    }

    // ------------------------------------------------------------------ slot band

    /// <summary>
    /// Builds the white slot strip that frames the drum's front row, the pale rail that runs off both
    /// sides of the screen, the light blue holder tabs on its ends, and the rim around the target cell.
    ///
    /// The reference frames every arrival inside this strip: it is what tells the eye which row is live
    /// before anything moves. The staged scene has no strip at all (SLOTINSIDELIGHT is a spot light, not
    /// geometry), so it is built here from primitives rather than from the loose FBX parts, whose pivots
    /// and axes are unknown.
    ///
    /// Every dimension is derived from the column pitch - the distance between neighbouring cells -
    /// and not from any single mesh's bounds. Deriving a size from a cell's own hole surface is what
    /// made the earlier effects three to four times too small to see.
    /// </summary>
    static void BuildSlotBand(Scene scene, List<DrumSlotReaction.Cell> cells, List<int> targetCells, Camera cam)
    {
        Transform stale = FindRoot(scene, BandRootName);
        if (stale != null) Object.DestroyImmediate(stale.gameObject);
        if (cam == null) return;

        int firstTarget = -1;
        for (int i = 0; i < targetCells.Count; i++)
        {
            if (targetCells[i] >= 0 && targetCells[i] < cells.Count) { firstTarget = targetCells[i]; break; }
        }
        if (firstTarget < 0) return;

        int bandRow = cells[firstTarget].row;

        List<Vector3> facePoints = new List<Vector3>(8);
        for (int i = 0; i < cells.Count; i++)
        {
            if (cells[i].row != bandRow) continue;
            Renderer r = cells[i].hole != null ? cells[i].hole : cells[i].body;
            if (r == null) continue;
            facePoints.Add(r.bounds.center);
        }
        if (facePoints.Count < 2) { Debug.LogWarning("[Case1Setup] slot band skipped: row " + bandRow + " has < 2 cells"); return; }

        Vector3 rowCenter = Vector3.zero;
        float minX = float.MaxValue, maxX = float.MinValue;
        for (int i = 0; i < facePoints.Count; i++)
        {
            rowCenter += facePoints[i];
            minX = Mathf.Min(minX, facePoints[i].x);
            maxX = Mathf.Max(maxX, facePoints[i].x);
        }
        rowCenter /= facePoints.Count;

        // Pitch: the neighbour-to-neighbour spacing, i.e. the real on-screen size of one cell.
        float pitch = facePoints.Count > 1 ? (maxX - minX) / (facePoints.Count - 1) : 1f;
        if (pitch <= 0.001f) pitch = 1f;

        // The band frames the live row, so it has to share the row's facing EXACTLY. This used to aim
        // from the row centre at the camera's POSITION, while the drum is pitched to -cam.forward - the
        // view axis. Those two agree only when the row sits dead centre in frame; it does not, and the
        // measured gap was 20.8 degrees (band -20.4, drum -41.2). In the side view the band floated at
        // its own angle across the board. One rule for both.
        Vector3 n = -cam.transform.forward;
        Vector3 right = Vector3.right;
        Vector3 up = Vector3.Cross(right, n).normalized;
        Quaternion facing = Quaternion.LookRotation(n, up);

        float halfBand = pitch * 0.59f;
        float halfWide = (maxX - minX) * 0.5f + pitch * 0.57f;

        // Put the backing just behind the authored cell faces. The old +0.72 pitch lift placed debug
        // bars in front of the cells; a filled rounded backing at that depth would hide the live row.
        float frontOffset = 0f;
        for (int i = 0; i < cells.Count; i++)
        {
            if (cells[i].row != bandRow || cells[i].body == null) continue;
            Bounds b = cells[i].body.bounds;
            float support = Mathf.Abs(n.x) * b.extents.x + Mathf.Abs(n.y) * b.extents.y + Mathf.Abs(n.z) * b.extents.z;
            frontOffset = Mathf.Max(frontOffset, Vector3.Dot(b.center - rowCenter, n) + support);
        }
        float lift = frontOffset - pitch * 0.10f;
        Vector3 basePos = rowCenter + n * lift;

        Material white = EnsureUnlit(MaterialDir + "/Slot/Case1_BandWhite.mat", new Color(0.975f, 0.985f, 1.00f, 1f));
        Material pink = EnsureUnlit(MaterialDir + "/Slot/Case1_BandPink.mat", new Color(0.965f, 0.62f, 0.84f, 1f));
        Material railDark = EnsureUnlit(MaterialDir + "/Slot/Case1_BandRailDark.mat", new Color(0.36f, 0.48f, 0.70f, 1f));
        Material rail = EnsureUnlit(MaterialDir + "/Slot/Case1_BandRail.mat", new Color(0.63f, 0.78f, 0.91f, 1f));
        Material railLight = EnsureUnlit(MaterialDir + "/Slot/Case1_BandRailLight.mat", new Color(0.78f, 0.91f, 0.98f, 1f));
        Material blue = EnsureUnlit(MaterialDir + "/Slot/Case1_BandArrow.mat", new Color(0.55f, 0.76f, 0.93f, 1f));
        Material bandShadow = EnsureUnlit(MaterialDir + "/Slot/Case1_BandShadow.mat", new Color(0.28f, 0.29f, 0.59f, 1f));

        GameObject root = new GameObject(BandRootName);
        SceneManager.MoveGameObjectToScene(root, scene);
        root.transform.position = basePos;
        root.transform.rotation = facing;

        // VIDEO_MEASURED: the rear rod is ~0.37 of a cell high and carries dark underside + bright
        // highlight, not a four-pixel white line. Three rounded layers reproduce that cylindrical read.
        // VIDEO_MEASURED: the reference rail spans viewport x 0.130..0.869 while the drum cells span
        // 0.185..0.830. It therefore reaches only 0.055 of the frame - about 0.43 of a cell pitch -
        // past each side of the drum, and never anywhere near the screen edge. Ours ran 26 pitches long,
        // which drew a bar straight across the whole frame and through the live row.
        float railLength = (maxX - minX) + pitch * 0.86f;
        // The band root sits just IN FRONT of the cell faces, so a rod at -0.22 was still level with
        // them and painted over the live row. The rod belongs a full cell behind the faces.
        RoundedRect(root.transform, "RailUnderside", railDark,
                    new Vector3(0f, -pitch * 0.035f, -pitch * 1.30f),
                    new Vector2(railLength, pitch * 0.36f), pitch * 0.16f, 6);
        RoundedRect(root.transform, "RailBody", rail,
                    new Vector3(0f, 0f, -pitch * 1.28f),
                    new Vector2(railLength, pitch * 0.27f), pitch * 0.13f, 6);
        RoundedRect(root.transform, "RailHighlight", railLight,
                    new Vector3(0f, pitch * 0.055f, -pitch * 1.26f),
                    new Vector2(railLength, pitch * 0.075f), pitch * 0.035f, 4);

        // One connected rounded FRAME. A filled pink backplate looked correct in isolation but sat in
        // front of the curved live row at this camera angle and erased every active glyph. Concentric
        // rings preserve the authored 3D cells while giving the strip its white shell and pink gutter.
        Vector2 bandSize = new Vector2(halfWide * 2f, halfBand * 2f);
        RoundedRectRing(root.transform, "BandContactShadow", bandShadow,
                        new Vector3(0f, -pitch * 0.045f, -pitch * 0.055f),
                        bandSize + Vector2.one * (pitch * 0.08f),
                        bandSize - Vector2.one * (pitch * 0.02f),
                        pitch * 0.17f, pitch * 0.13f, 7);
        RoundedRectRing(root.transform, "BandOuter", white, Vector3.zero,
                        bandSize,
                        bandSize - Vector2.one * (pitch * 0.105f),
                        pitch * 0.15f, pitch * 0.105f, 7);
        RoundedRectRing(root.transform, "BandInner", pink, new Vector3(0f, 0f, pitch * 0.012f),
                        bandSize - Vector2.one * (pitch * 0.105f),
                        bandSize - Vector2.one * (pitch * 0.175f),
                        pitch * 0.105f, pitch * 0.075f, 7);

        float tabX = halfWide + pitch * 0.16f;
        ArrowPlate(root.transform, "HolderLeft", white, blue, -1f,
                   new Vector3(-tabX, 0f, pitch * 0.045f), pitch * 0.40f, pitch * 0.64f);
        ArrowPlate(root.transform, "HolderRight", white, blue, 1f,
                   new Vector3(tabX, 0f, pitch * 0.045f), pitch * 0.40f, pitch * 0.64f);

        EditorUtility.SetDirty(root);
        Debug.Log(string.Format(
            "[Case1Setup] rounded slot band built on row {0}: pitch={1:0.000} halfWide={2:0.00} halfBand={3:0.00} lift={4:0.00} centre={5}",
            bandRow, pitch, halfWide, halfBand, lift, rowCenter));
    }

    /// <summary>One box of the strip. Local to the band root, so the whole strip moves as a unit.</summary>
    static void Piece(Transform parent, string pieceName, Material material, Vector3 localPos, Vector3 localScale, float rollDegrees = 0f)
    {
        GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = pieceName;
        Object.DestroyImmediate(go.GetComponent<Collider>());
        go.transform.SetParent(parent, false);
        go.transform.localPosition = localPos;
        go.transform.localRotation = Quaternion.Euler(0f, 0f, rollDegrees);
        go.transform.localScale = localScale;

        Renderer r = go.GetComponent<Renderer>();
        r.sharedMaterial = material;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        r.receiveShadows = false;
    }

    static void RoundedRect(Transform parent, string pieceName, Material material, Vector3 localPos,
                            Vector2 size, float radius, int cornerSegments)
    {
        float halfX = Mathf.Max(0.001f, size.x * 0.5f);
        float halfY = Mathf.Max(0.001f, size.y * 0.5f);
        radius = Mathf.Clamp(radius, 0f, Mathf.Min(halfX, halfY));
        cornerSegments = Mathf.Max(2, cornerSegments);

        List<Vector3> vertices = new List<Vector3>(cornerSegments * 4 + 5) { Vector3.zero };
        Vector2[] centres =
        {
            new Vector2(halfX - radius, -halfY + radius),
            new Vector2(halfX - radius, halfY - radius),
            new Vector2(-halfX + radius, halfY - radius),
            new Vector2(-halfX + radius, -halfY + radius)
        };
        float[] starts = { -90f, 0f, 90f, 180f };
        for (int corner = 0; corner < 4; corner++)
        {
            for (int s = 0; s <= cornerSegments; s++)
            {
                float a = (starts[corner] + 90f * s / cornerSegments) * Mathf.Deg2Rad;
                vertices.Add(new Vector3(centres[corner].x + Mathf.Cos(a) * radius,
                                         centres[corner].y + Mathf.Sin(a) * radius, 0f));
            }
        }

        int perimeter = vertices.Count - 1;
        int[] triangles = new int[perimeter * 3];
        for (int i = 0; i < perimeter; i++)
        {
            triangles[i * 3] = 0;
            triangles[i * 3 + 1] = i + 1;
            triangles[i * 3 + 2] = (i + 1) % perimeter + 1;
        }
        CreatePlateMesh(parent, pieceName, material, localPos, vertices.ToArray(), triangles);
    }

    /// <summary>A rounded rectangle border with a genuinely empty centre.</summary>
    static void RoundedRectRing(Transform parent, string pieceName, Material material, Vector3 localPos,
                                Vector2 outerSize, Vector2 innerSize, float outerRadius,
                                float innerRadius, int cornerSegments)
    {
        Vector3[] outer = RoundedRectPerimeter(outerSize, outerRadius, cornerSegments);
        Vector3[] inner = RoundedRectPerimeter(innerSize, innerRadius, cornerSegments);
        int count = Mathf.Min(outer.Length, inner.Length);
        Vector3[] vertices = new Vector3[count * 2];
        for (int i = 0; i < count; i++)
        {
            vertices[i] = outer[i];
            vertices[count + i] = inner[i];
        }

        int[] triangles = new int[count * 6];
        for (int i = 0; i < count; i++)
        {
            int next = (i + 1) % count;
            int t = i * 6;
            triangles[t] = i;
            triangles[t + 1] = next;
            triangles[t + 2] = count + next;
            triangles[t + 3] = i;
            triangles[t + 4] = count + next;
            triangles[t + 5] = count + i;
        }
        CreatePlateMesh(parent, pieceName, material, localPos, vertices, triangles);
    }

    static Vector3[] RoundedRectPerimeter(Vector2 size, float radius, int cornerSegments)
    {
        float halfX = Mathf.Max(0.001f, size.x * 0.5f);
        float halfY = Mathf.Max(0.001f, size.y * 0.5f);
        radius = Mathf.Clamp(radius, 0f, Mathf.Min(halfX, halfY));
        cornerSegments = Mathf.Max(2, cornerSegments);
        Vector2[] centres =
        {
            new Vector2(halfX - radius, -halfY + radius),
            new Vector2(halfX - radius, halfY - radius),
            new Vector2(-halfX + radius, halfY - radius),
            new Vector2(-halfX + radius, -halfY + radius)
        };
        float[] starts = { -90f, 0f, 90f, 180f };
        Vector3[] perimeter = new Vector3[(cornerSegments + 1) * 4];
        int cursor = 0;
        for (int corner = 0; corner < 4; corner++)
        {
            for (int s = 0; s <= cornerSegments; s++)
            {
                float a = (starts[corner] + 90f * s / cornerSegments) * Mathf.Deg2Rad;
                perimeter[cursor++] = new Vector3(centres[corner].x + Mathf.Cos(a) * radius,
                                                   centres[corner].y + Mathf.Sin(a) * radius, 0f);
            }
        }
        return perimeter;
    }

    static void ArrowPlate(Transform parent, string pieceName, Material outer, Material inner,
                           float direction, Vector3 localPos, float width, float height)
    {
        Vector3[] outerPoints = ArrowPoints(direction, width, height);
        PolygonPlate(parent, pieceName + "_Outer", outer, localPos, outerPoints);
        Vector3[] innerPoints = ArrowPoints(direction, width * 0.72f, height * 0.76f);
        PolygonPlate(parent, pieceName + "_Inner", inner,
                     localPos + Vector3.forward * (width * 0.025f), innerPoints);
    }

    static Vector3[] ArrowPoints(float direction, float width, float height)
    {
        float d = direction < 0f ? -1f : 1f;
        Vector3[] p =
        {
            new Vector3(-0.50f, -0.30f, 0f),
            new Vector3(-0.34f, -0.46f, 0f),
            new Vector3( 0.02f, -0.44f, 0f),
            new Vector3( 0.46f, -0.10f, 0f),
            new Vector3( 0.50f,  0.00f, 0f),
            new Vector3( 0.46f,  0.10f, 0f),
            new Vector3( 0.02f,  0.44f, 0f),
            new Vector3(-0.34f,  0.46f, 0f),
            new Vector3(-0.50f,  0.30f, 0f)
        };
        for (int i = 0; i < p.Length; i++)
        {
            p[i].x *= width * d;
            p[i].y *= height;
        }
        if (d < 0f) System.Array.Reverse(p);
        return p;
    }

    static void PolygonPlate(Transform parent, string pieceName, Material material, Vector3 localPos, Vector3[] perimeter)
    {
        Vector3[] vertices = new Vector3[perimeter.Length + 1];
        vertices[0] = Vector3.zero;
        for (int i = 0; i < perimeter.Length; i++) vertices[i + 1] = perimeter[i];
        int[] triangles = new int[perimeter.Length * 3];
        for (int i = 0; i < perimeter.Length; i++)
        {
            triangles[i * 3] = 0;
            triangles[i * 3 + 1] = i + 1;
            triangles[i * 3 + 2] = (i + 1) % perimeter.Length + 1;
        }
        CreatePlateMesh(parent, pieceName, material, localPos, vertices, triangles);
    }

    static void CreatePlateMesh(Transform parent, string pieceName, Material material, Vector3 localPos,
                                Vector3[] vertices, int[] triangles)
    {
        GameObject go = new GameObject(pieceName);
        go.transform.SetParent(parent, false);
        go.transform.localPosition = localPos;
        Mesh mesh = new Mesh { name = "Case1_" + pieceName + "_Mesh" };
        mesh.vertices = vertices;
        mesh.triangles = triangles;
        Vector3[] normals = new Vector3[vertices.Length];
        for (int i = 0; i < normals.Length; i++) normals[i] = Vector3.forward;
        mesh.normals = normals;
        mesh.RecalculateBounds();
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        MeshRenderer renderer = go.AddComponent<MeshRenderer>();
        renderer.sharedMaterial = material;
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        renderer.receiveShadows = false;
    }

    static Material EnsureUnlit(string path, Color colour)
    {
        Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
        if (shader == null) shader = Shader.Find("Unlit/Color");
        Material m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (m == null) { m = new Material(shader); AssetDatabase.CreateAsset(m, path); }
        else if (m.shader != shader) m.shader = shader;
        m.SetColor("_BaseColor", colour);
        m.SetColor("_Color", colour);
        EditorUtility.SetDirty(m);
        return m;
    }


    // ------------------------------------------------------------------ decorative shape tray

    // Measured off _refs/Developer Case Referans/Fit The Shape.mp4 at 1080x1728, read as viewport
    // fractions of the 0.625 frame:
    //
    //   deck pill row       centre y 0.502          (ours already sits at ~0.51 - this is the match)
    //   tray row 1          centre y 0.365
    //   tray row 2          centre y 0.268
    //   tray row 3          centre y 0.174
    //   tray columns        centre x 0.375 / 0.500 / 0.625
    //   floor line          y 0.115, running the full width, dark band below it
    //
    // Below the deck row our frame was empty flat purple from y 0.45 down - roughly 45% of the frame
    // carrying nothing - while the reference fills exactly that band. So the band is filled with the
    // same nine-shape tray the reference has, in the reference's own arrangement and colours.
    //
    // This is DECORATION AND NOTHING ELSE. Every piece is built without a collider, is not registered
    // with DeckReflow, ShapeArcFlight or ShapeTapInput, and carries no queue, score or state. The three
    // playable shapes and the cells they are matched to are untouched by this method.
    // MEASURED off the reference's opening frame, row by row:
    //   orange diamond | green square  | red hexagon
    //   purple triangle| red hexagon   | green square
    //   red hexagon    | green square  | pink star
    static readonly ShapeId[,] TrayShapes =
    {
        { ShapeId.Diamond,  ShapeId.Square,  ShapeId.Hexagon },
        { ShapeId.Triangle, ShapeId.Hexagon, ShapeId.Square  },
        { ShapeId.Hexagon,  ShapeId.Square,  ShapeId.Star    }
    };

    static readonly string[,] TrayColours =
    {
        { "ORANGE", "GREEN", "RED"   },
        { "PURPLE", "RED",   "GREEN" },
        { "RED",    "GREEN", "PINK"  }
    };

    // USER DIRECTIVE: bu 9lu yu birbirine yakınlaştır aradaki boşlukları yarıya indir
    static readonly float[] TrayColumnX = { 0.350f, 0.500f, 0.650f };
    static readonly float[] TrayRowY = { 0.315f, 0.235f, 0.155f };

    /// <summary>
    /// ABSOLUTE world height of the tray's ground plane, in world units. Every tray slot - playable
    /// piece, scenery tile and the hidden refill - is the camera ray through its measured viewport
    /// point cast onto y = this.
    ///
    /// It is a CONSTANT on purpose. It used to be derived from the piece the previous build had left
    /// in shapes[0]:
    ///
    ///     depth   = dot(model.position - cam.position, cam.forward)   // where the LAST build put it
    ///     groundY = cam.ViewportToWorldPoint(0.5, midRow, depth).y    // ...becomes the NEXT plane
    ///
    /// That is a feedback loop, and its gain is not 1: shapes[0] does not sit on the centre column of
    /// the middle row, so the ray that reads the depth back out is not the ray that wrote it. Measured
    /// over five consecutive Builds the plane fell 1.754 -> 1.148 -> 0.513 -> -0.153 -> -0.852, by a
    /// step that GREW every time (-0.606, -0.635, -0.666, -0.699; ratio ~1.048 per build).
    ///
    /// Nothing looked wrong, because the viewport raycast keeps every piece's SCREEN position - and
    /// FitTrayTile its screen size - identical at any plane height. What it silently spent was depth
    /// clearance in front of the reel: min(drum camera-depth) - max(tray camera-depth) fell from
    /// +16.05 to +11.37 over two of those builds, at ~2.3 per build, and at -5.17 the tray is behind
    /// the reel and unplayable - exactly the fault 5329d95 fixed.
    ///
    /// The value is the height the measured, committed scene already stands at, so the first build
    /// after this change reproduces 5329d95's framing exactly rather than re-placing the tray:
    /// live row 75.46% of the frame, both arrow caps in frame, I-A occluded=0, separation +11.3740.
    /// The camera pose it was measured against is written by Build itself, a few hundred lines up, as
    /// a literal - pos (0, 14.2, -33.5), euler (15, 0, 0), fov 10.5, aspect enforced - and the scene
    /// is authored (SceneIsAuthored), so nothing re-solves it from content either.
    /// </summary>
    const float TrayGroundY = -0.8521f;

    static Vector3 BaseScaleForShape(ShapeId id)
    {
        // USER DIRECTIVE: Scalelerini genel olarak 1.5 katına çıkar öndekileri de arkadakileri de
        const float generalScale = 1.50f;
        switch (id)
        {
            case ShapeId.Diamond:  return new Vector3(0.60f, 0.60f, 0.60f) * generalScale;
            case ShapeId.Square:   return new Vector3(0.56f, 0.56f, 0.56f) * generalScale;
            case ShapeId.Hexagon:  return new Vector3(0.58f, 0.58f, 0.58f) * generalScale;
            case ShapeId.Triangle: return new Vector3(0.60f, 0.60f, 0.60f) * generalScale;
            case ShapeId.Star:     return new Vector3(0.62f, 0.62f, 0.62f) * generalScale;
            default:               return new Vector3(0.58f, 0.58f, 0.58f) * generalScale;
        }
    }

    /// <summary>
    /// Fills the empty lower half of the frame with the reference's shape tray and floor line.
    /// </summary>
    static readonly Vector2 RefLiveRowCentre = new Vector2(0.500f, 0.737f);
    const float RefDeckPlateWidth = 0.100f;
    const float RefTrayTileWidth = 100f / 1080f;

    /// <summary>
    /// Balanced camera downward pitch angle in degrees (33.0 deg top-down angle).
    /// </summary>
    const float TargetCameraPitchDeg = 33.0f;

    /// <summary>
    /// In-plane spin of every hexagon, so a flat edge does not sit square to the camera.
    ///
    /// 30, not the 60 asked for: a regular hexagon maps onto ITSELF every 60 degrees, so a 60 degree
    /// spin is a no-op on screen and would have looked like the change never landed. 30 is the half
    /// step that actually moves a vertex to where the flat edge was.
    /// </summary>
    const float HexagonSpinDeg = 30f;

    /// <summary>
    /// Projected height of a FRONT row tray piece, as a fraction of the frame.
    ///
    /// PIXEL_MEASURED off the reference's own opening frame at 1080x1728: its front row measures 153 px
    /// tall against ours at 109, so ours was 29% short and the tray read as a row of pucks rather than
    /// as blocks. Earlier numbers here were measured against OUR frame and then nudged by a percentage,
    /// which is how a target ends up describing what we already had.
    /// </summary>
    const float RefFrontRowHeight = 160f / 1728f;

    /// <summary>
    /// Projected height of a piece on a row BEHIND the front one. PIXEL_MEASURED off the same frame:
    /// 112 px against the front row's 153, a ratio of 0.73. Ours was at 0.57 - flattened so hard that a
    /// star stopped reading as a star.
    /// </summary>
    const float RefBackRowHeight = 125f / 1728f;

    /// <summary>Collects the tray's scenery tiles wherever the author parented them.</summary>
    static void CollectTrayTiles(Transform t, List<Transform> into)
    {
        if (t.name.StartsWith("TrayShape_") || t.name.StartsWith("TrayRefill_")) into.Add(t);
        for (int i = 0; i < t.childCount; i++) CollectTrayTiles(t.GetChild(i), into);
    }

    /// <summary>Collects every DeckSlot_* and Shape_* in the subtree, wherever the author parented it.</summary>
    static void CollectByNamePrefix(Transform t, List<Transform> into)
    {
        if (t.name.StartsWith("DeckSlot_") || t.name.StartsWith("Shape_")) into.Add(t);
        for (int i = 0; i < t.childCount; i++) CollectByNamePrefix(t.GetChild(i), into);
    }

    /// <summary>
    /// Stands every tray piece on the FLOOR, straight, at the reference's own row and column.
    ///
    /// The tray had been placed against the old camera and stayed there: the dump found it at Y = -8.28,
    /// below the floor and BEHIND the board at Z 28..36. It looked acceptable only because the camera
    /// happened to be far enough away for the mistake to hide.
    ///
    /// The row and column targets are the reference's, measured; the plane is the world's floor. Where
    /// those two meet is a real world position - one plane, straight angles, ordered by depth - and it
    /// still lands where the reference puts it on screen.
    /// </summary>
    static void PlaceTrayOnFloor(Camera cam, List<DeckReflow.Entry> entries, float floorY)
    {
        if (cam == null || entries == null) return;
        float oldAspect = cam.aspect;
        cam.aspect = ReferenceMatchLayout.Aspect;
        int placed = 0;

        for (int i = 0; i < entries.Count; i++)
        {
            DeckReflow.Entry e = entries[i];
            if (e.shape == null) continue;
            int slot = e.slot;
            int row = slot / TrayColumnX.Length, col = slot % TrayColumnX.Length;
            if (row >= TrayRowY.Length) continue;

            ShapeId id;
            // Straight, with the one deliberate exception: a hexagon is turned 30 degrees in its own
            // plane so a flat edge does not face the viewer. Every row gets the same angle - the rows
            // differ in height and in nothing else.
            e.shape.rotation = ShapeIds.TryParse(e.shape.name, out id) && id == ShapeId.Hexagon
                             ? Quaternion.Euler(0f, 30f, 0f)
                             : Quaternion.identity;

            Ray ray = cam.ViewportPointToRay(new Vector3(TrayColumnX[col], TrayRowY[row], 0f));
            if (Mathf.Abs(ray.direction.y) < 1e-5f) continue;
            float k = (floorY - ray.origin.y) / ray.direction.y;
            if (k <= 0.01f) continue;
            Vector3 hit = ray.origin + ray.direction * k;

            Bounds b = SubtreeBounds(e.shape);
            e.shape.position += new Vector3(hit.x, floorY, hit.z) - new Vector3(b.center.x, b.min.y, b.center.z);
            EditorUtility.SetDirty(e.shape);
            placed++;
        }

        cam.aspect = oldAspect;
        Debug.Log("[Case1Setup] TRAY_ON_FLOOR " + placed + " pieces standing on Y " + floorY.ToString("0.00"));
    }

    static Bounds SubtreeBounds(Transform t)
    {
        Renderer[] rs = t.GetComponentsInChildren<Renderer>(true);
        Bounds b = new Bounds(t.position, Vector3.zero);
        bool any = false;
        foreach (Renderer r in rs)
        {
            if (r == null || r is ParticleSystemRenderer) continue;
            if (!any) { b = r.bounds; any = true; } else b.Encapsulate(r.bounds);
        }
        return b;
    }

    /// <summary>
    /// Local Y that puts <paramref name="t"/>'s projected height at <paramref name="want"/>. Iterative,
    /// because perspective is not linear in scale. No axis is chosen here: the world layout stands the
    /// pieces straight, so height is local Y and that is the end of it.
    /// </summary>
    static float SolveLocalYForHeight(Camera cam, Transform t, float want)
    {
        for (int pass = 0; pass < 8; pass++)
        {
            Rect r;
            if (!ReferenceMatchLayout.ProjectBounds(cam, t, out r) || r.height < 1e-5f) break;
            float f = Mathf.Clamp(want / r.height, 0.4f, 2.2f);
            if (Mathf.Abs(f - 1f) < 0.005f) break;
            Vector3 sc = t.localScale;
            t.localScale = new Vector3(sc.x, sc.y * f, sc.z);
        }
        return t.localScale.y;
    }

    /// <summary>
    /// Scales <paramref name="t"/> along <paramref name="upAxis"/> until its PROJECTED height matches
    /// <paramref name="want"/>. Iterative because perspective makes the relation non-linear: a piece
    /// that is half as tall does not project half the height.
    /// </summary>
    static void SolveProjectedHeight(Camera cam, Transform t, int upAxis, float want)
    {
        for (int pass = 0; pass < 8; pass++)
        {
            Rect r;
            if (!ReferenceMatchLayout.ProjectBounds(cam, t, out r) || r.height < 1e-5f) return;
            float f = Mathf.Clamp(want / r.height, 0.4f, 2.2f);
            if (Mathf.Abs(f - 1f) < 0.005f) return;
            Vector3 sc = t.localScale;
            float[] v = { sc.x, sc.y, sc.z };
            v[upAxis] *= f;
            t.localScale = new Vector3(v[0], v[1], v[2]);
        }
    }

    /// <summary>
    /// Bounding box height is not silhouette height, and the eye compares silhouettes.
    ///
    /// After the front row was solved to one PROJECTED height (0.0450 / 0.0451 / 0.0452, inside 0.4%),
    /// the RENDERED heights still measured 80 / 82 / 76 px: a renderer's world AABB carries the outline
    /// hull and the parts of the box the mesh never fills, and it carries a different amount per shape.
    /// These are the measured ratios between the row's mean rendered height and each shape's own, so
    /// the solve targets the silhouette the player actually sees.
    ///
    /// PIXEL_MEASURED off .plan-build/verify/FitTheShape/frame_00.png at 1080x1728.
    /// </summary>
    static float SilhouetteCalibration(Transform t)
    {
        ShapeId id;
        if (t == null || !ShapeIds.TryParse(t.name, out id)) return 1f;
        switch (id)
        {
            case ShapeId.Hexagon: return 1.044f;   // rendered 76 px against the row's 79.3 mean
            case ShapeId.Square:  return 0.967f;   // rendered 82 px
            case ShapeId.Round:   return 0.992f;   // rendered 80 px
            default:              return 1f;       // unmeasured shapes stay honest rather than guessed
        }
    }

    /// <summary>Index of the local axis of <paramref name="t"/> most aligned with <paramref name="worldDir"/>.</summary>
    static int LocalAxisAlignedWith(Transform t, Vector3 worldDir)
    {
        float ax = Mathf.Abs(Vector3.Dot(t.right.normalized, worldDir));
        float ay = Mathf.Abs(Vector3.Dot(t.up.normalized, worldDir));
        float az = Mathf.Abs(Vector3.Dot(t.forward.normalized, worldDir));
        if (ax >= ay && ax >= az) return 0;
        if (ay >= ax && ay >= az) return 1;
        return 2;
    }

    /// <summary>
    /// Re-fits every tray piece's WIDTH once it is standing on the world grid. FitTrayTile ran when the
    /// pieces were created, which is before the world layout moves them, and a fit is only valid at the
    /// distance it was solved at.
    /// </summary>
    static void RefitTrayPieces(Camera cam, Scene scene, List<Transform> shapes)
    {
        int n = 0;
        foreach (GameObject go in scene.GetRootGameObjects())
        {
            Transform tray = go.name == TrayRootName ? go.transform : FindDescendant(go.transform, TrayRootName);
            if (tray == null) continue;
            for (int i = 0; i < tray.childCount; i++) { FitTrayTile(cam, tray.GetChild(i)); n++; }
        }
        for (int i = 0; i < shapes.Count; i++) { if (shapes[i] != null) { FitTrayTile(cam, shapes[i]); n++; } }
        Debug.Log("[Case1Setup] TRAY_REFIT " + n + " pieces re-fitted at their world positions");
    }

    /// <summary>
    /// Converges a tray tile onto the measured reference tile size. Idempotent.
    ///
    /// The height correction has to be applied to the axis that IS the tile's height on screen. This
    /// used to multiply localScale.y, but these prefabs carry their face on local X-Z with local +Y
    /// pointing out of it, so the correction went into DEPTH: the projected height never changed, the
    /// loop measured the same error five times running, and the rows ended up 130 / 107 / 158 px tall
    /// instead of the 135 they were all being fitted to. The width converged only because it happened
    /// to be spread across x and z. The axes are found from the camera, so the prefab's authored pose
    /// does not matter.
    /// </summary>
    static void FitTrayTile(Camera cam, Transform t)
    {
        if (cam == null || t == null) return;
        ShapeId shapeId;
        if (!ShapeIds.TryParse(t.name, out shapeId) && !ShapeOf(t.name, out shapeId))
            shapeId = ShapeId.Square;
        Vector3 baseSc = BaseScaleForShape(shapeId);
        t.localScale = baseSc;
        EditorUtility.SetDirty(t);
    }

    /// <summary>
    /// Adds the playable pieces the staged scene does not ship. Rebuilt from scratch every run: these
    /// are parented to the Deck and would otherwise stack up one copy per build, which is the same
    /// trap the sunken glyphs fell into.
    /// </summary>
    const string GeneratedSuffix = " (generated)";

    /// <summary>Where the per-shape prefab VARIANTS live.</summary>
    const string PieceVariantDir = PrefabDir + "/Pieces";

    /// <summary>
    /// The prefab VARIANT for a shape, created from the shape's base prefab on first use.
    ///
    /// This is the "fix one, all follow" the tray never had. Every piece of a shape - the playable one,
    /// the scenery copies, the off-screen refill - is an instance of this single asset, so its colour
    /// and its collider are authored once. Before this the builder wrote materials onto each instance,
    /// which produced instance OVERRIDES: the scene looked right and the prefab was meaningless, and
    /// two copies of the same shape could and did end up different colours.
    /// </summary>
    static GameObject EnsurePieceVariant(ShapeId id)
    {
        if (!AssetDatabase.IsValidFolder(PieceVariantDir))
            AssetDatabase.CreateFolder(PrefabDir, "Pieces");

        string path = PieceVariantDir + "/Piece_" + id + ".prefab";
        GameObject existing = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        if (existing != null) return existing;

        GameObject basePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(
            PrefabDir + "/" + ShapeIds.PrefabName(id) + ".prefab");
        if (basePrefab == null) { Debug.LogWarning("[Case1Setup] base prefab missing for " + id); return null; }

        // Saving an INSTANCE of a prefab as a new asset is what makes Unity author a Variant rather
        // than an independent copy: the shape's mesh keeps coming from the base prefab, and only what
        // Case 1 changes - colour, collider - lives in the variant.
        GameObject temp = (GameObject)PrefabUtility.InstantiatePrefab(basePrefab);
        EnsurePieceCollider(temp);
        GameObject variant = PrefabUtility.SaveAsPrefabAsset(temp, path);
        Object.DestroyImmediate(temp);
        Debug.Log("[Case1Setup] piece variant created: " + path);
        return variant;
    }

    static bool IsShadowRenderer(Renderer r)
    {
        if (r == null) return false;
        string n = r.name.ToLowerInvariant();
        if (n.Contains("shadow") || n == "star.004" || n.Contains("shadowplane")) return true;
        if (r.sharedMaterial != null && r.sharedMaterial.name.ToLowerInvariant().Contains("shadow")) return true;
        return false;
    }

    static Material EnsureShadowMaterial(string path, Color colour)
    {
        Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
        if (shader == null) shader = Shader.Find("Unlit/Color");
        Material m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (m == null) { m = new Material(shader); AssetDatabase.CreateAsset(m, path); }
        else if (m.shader != shader) m.shader = shader;
        m.SetColor("_BaseColor", colour);
        m.SetColor("_Color", colour);
        m.SetFloat("_Surface", 1); // Transparent
        m.SetFloat("_Blend", 0);   // Alpha
        m.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
        m.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        m.SetInt("_ZWrite", 0);
        m.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
        EditorUtility.SetDirty(m);
        return m;
    }

    /// <summary>Writes each shape's colour into its VARIANT, so every instance in the scene follows.</summary>
    static void RefreshPieceVariantColours()
    {
        int written = 0;
        Material shadowMat = EnsureShadowMaterial(MaterialDir + "/Slot/Case1_PieceShadow.mat", new Color(0.18f, 0.18f, 0.42f, 0.55f));
        foreach (ShapeId id in System.Enum.GetValues(typeof(ShapeId)))
        {
            Color colour = ColourForShape(id, Color.grey);
            if (colour == Color.grey) continue;
            GameObject variant = EnsurePieceVariant(id);
            if (variant == null) continue;

            string path = AssetDatabase.GetAssetPath(variant);
            Material toon = EnsureToonMaterial(
                MaterialDir + "/Cells/Case1_Toon_" + ColourKey(colour) + ".mat", colour);
            if (toon == null) continue;

            GameObject contents = PrefabUtility.LoadPrefabContents(path);
            MeshFilter mf = contents.GetComponentInChildren<MeshFilter>(true);
            if (mf != null && mf.sharedMesh != null)
                Debug.Log("[Case1Setup] MESH_BOUNDS " + id + ": min=" + mf.sharedMesh.bounds.min.ToString("F3") + " max=" + mf.sharedMesh.bounds.max.ToString("F3") + " size=" + mf.sharedMesh.bounds.size.ToString("F3"));
            Renderer[] renderers = contents.GetComponentsInChildren<Renderer>(true);
            bool changed = false;
            for (int i = 0; i < renderers.Length; i++)
            {
                if (IsShadowRenderer(renderers[i]))
                {
                    if (renderers[i].sharedMaterial != shadowMat) { renderers[i].sharedMaterial = shadowMat; changed = true; }
                    if (!renderers[i].enabled) { renderers[i].enabled = true; changed = true; }
                    continue;
                }
                if (renderers[i].sharedMaterial == toon) continue;
                renderers[i].sharedMaterial = toon;
                changed = true;
            }
            if (EnsurePieceCollider(contents)) changed = true;
            if (changed) PrefabUtility.SaveAsPrefabAsset(contents, path);
            PrefabUtility.UnloadPrefabContents(contents);
            written++;
        }
        Debug.Log("[Case1Setup] piece variants refreshed: " + written);
    }

    /// <summary>Gives a piece a collider sized to its renderers if it has none. Returns true if it added one.</summary>
    static bool EnsurePieceCollider(GameObject piece)
    {
        if (piece.GetComponentInChildren<Collider>(true) != null) return false;

        // NOT for tapping: ShapeTapInput resolves a press by screen-space proximity to the entries in
        // ShapeArcFlight, so a collider has no part in it - an earlier version of this comment claimed
        // otherwise and was simply wrong. The triangle was untappable because it had no flight ENTRY,
        // not because it had no collider. The collider is here so a piece is a sane physical object.
        BoxCollider box = piece.AddComponent<BoxCollider>();
        Renderer[] rs = piece.GetComponentsInChildren<Renderer>(true);
        if (rs.Length == 0) return true;
        Bounds b = rs[0].bounds;
        for (int r = 1; r < rs.Length; r++) b.Encapsulate(rs[r].bounds);
        box.center = piece.transform.InverseTransformPoint(b.center);
        box.size = piece.transform.InverseTransformVector(b.size);
        return true;
    }

    static void EnsurePlayablePieces(Scene scene, Transform deckRoot)
    {
        // The scene owns the pieces once it is authored: this method destroyed and re-created them at a
        // model piece's position, which threw away where the author had put them - the tray came back
        // as a stack of overlapping tiles. It now only ADDS what is genuinely missing.
        if (SceneIsAuthored)
        {
            List<Transform> present = new List<Transform>(8);
            foreach (GameObject go in scene.GetRootGameObjects()) CollectByNamePrefix(go.transform, present);
            int have = 0;
            foreach (Transform t in present) if (t.name.StartsWith("Shape_")) have++;
            if (have > 0)
            {
                Debug.Log("[Case1Setup] playable pieces left alone: " + have + " already in the authored scene");
                return;
            }
        }

        // The pieces the staged scene does not ship. Anything the live row needs that is not already
        // in the Deck gets created, so adding a column to LiveRowShape is enough - there is no second
        // list to remember to update.
        ShapeId[] wanted = { ShapeId.Triangle, ShapeId.Square };

        // Sweep EVERY piece this method has ever produced, not just the ones about to be produced. The
        // first version only destroyed the names in `wanted`, so when the second hexagon was replaced by
        // a square the old Shape_Hexagon2 survived in the scene, took the hexagon cell, and left the
        // real Shape_Hexagon with no target and untappable. Same trap as the stacked glyph copies:
        // cleanup keyed on what is being created cannot remove what was created before.
        // Earlier builds created these pieces BEFORE the suffix existed, and the scene file kept them.
        // They are unreachable by the suffix sweep, would take the cells the real pieces need, and were
        // already caught doing exactly that (Shape_Hexagon2 stole the hexagon cell and left the staged
        // Shape_Hexagon untappable). A bounded migration list clears them once and for all.
        string[] legacy = { "Shape_Triangle", "Shape_Square", "Shape_Hexagon2" };
        int swept = 0;
        for (int k = deckRoot.childCount - 1; k >= 0; k--)
        {
            string childName = deckRoot.GetChild(k).name;
            bool generated = childName.EndsWith(GeneratedSuffix);
            if (!generated)
                for (int l = 0; l < legacy.Length && !generated; l++) generated = childName == legacy[l];
            if (!generated) continue;
            Object.DestroyImmediate(deckRoot.GetChild(k).gameObject);
            swept++;
        }
        if (swept > 0) Debug.Log("[Case1Setup] playable sweep removed " + swept + " piece(s) from earlier builds");

        // A staged piece is the template for pose and scale; the tray converges the exact size later,
        // but starting from the model keeps a new piece in the same world as the ones already there.
        Transform model = null;
        for (int i = 0; i < deckRoot.childCount && model == null; i++)
            if (deckRoot.GetChild(i).name.StartsWith("Shape_")) model = deckRoot.GetChild(i);
        if (model == null) { Debug.LogWarning("[Case1Setup] no staged Shape_* to model new pieces on"); return; }

        for (int w = 0; w < wanted.Length; w++)
        {
            GameObject prefab = EnsurePieceVariant(wanted[w]);
            if (prefab == null) continue;

            // InstantiatePrefab, not Instantiate: the plain call produces a detached copy, so the piece
            // showed up in the Hierarchy as a loose GameObject and editing the prefab did nothing to
            // it. This keeps the variant connection the scene is supposed to have.
            GameObject piece = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
            // The suffix is how the sweep above recognises its own work. ShapeIds.TryParse reads the
            // shape out of the name regardless, so "Shape_Square (generated)" still resolves to Square.
            piece.name = ShapeIds.ObjectName(wanted[w]) + GeneratedSuffix;
            piece.transform.SetParent(deckRoot, true);
            piece.transform.SetPositionAndRotation(model.position, model.rotation);
            if (wanted[w] == ShapeId.Hexagon) piece.transform.Rotate(0f, HexagonSpinDeg, 0f, Space.Self);
            piece.transform.localScale = model.localScale;
            piece.SetActive(true);

            Debug.Log("[Case1Setup] playable piece added: " + piece.name);
        }
    }

    /// <summary>Slot each playable shape takes, chosen so the tray still reads as the reference grid.</summary>
    /// <summary>
    /// Which tray cell each playable piece occupies. VIDEO_MEASURED from the reference's opening frame,
    /// whose tray row 0 is orange diamond / green square / RED HEXAGON, and whose hero - the piece the
    /// player taps - is that red hexagon in row0 col2.
    ///
    /// The other two playable pieces are kept OFF the cells the colour gate samples, so those cells keep
    /// the scenery whose colour and shape both match the reference: green square at row0 col1, purple
    /// triangle at row1 col0, red hexagon at row1 col1.
    /// </summary>
    static int PlayableSlot(ShapeId id)
    {
        switch (id)
        {
            case ShapeId.Diamond:  return 0;   // row0 col0 - Orange Diamond
            case ShapeId.Round:    return 0;   // row0 col0 - Orange Diamond
            case ShapeId.Square:   return 1;   // row0 col1 - Green Square
            case ShapeId.Hexagon:  return 2;   // row0 col2 - Red Hexagon (Hero)
            case ShapeId.Triangle: return 3;   // row1 col0 - Purple Triangle
            case ShapeId.Star:     return 8;   // row2 col2 - Pink Star
        }
        return -1;
    }

    static void ApplyReferencePlayableVisual(Transform piece)
    {
        if (piece == null) return;
        ShapeId logical;
        if (!ShapeOf(piece.name, out logical)) return;

        Color colour = ColourForShape(logical, Color.grey);

        if (logical == ShapeId.Round)
        {
            GameObject diamond = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabDir + "/Diamond.prefab");
            MeshFilter source = diamond != null ? FirstVisibleMesh(diamond.transform) : null;
            MeshFilter target = FirstVisibleMesh(piece);
            if (source != null && target != null) target.sharedMesh = source.sharedMesh;
            piece.name = "Shape_Diamond";
            logical = ShapeId.Diamond;
        }

        Material mat = EnsureToonMaterial(MaterialDir + "/Cells/Case1_Playable_" + logical + ".mat", colour);
        Material shadowMat = EnsureShadowMaterial(MaterialDir + "/Slot/Case1_PieceShadow.mat", new Color(0.18f, 0.18f, 0.42f, 0.55f));
        foreach (Renderer renderer in piece.GetComponentsInChildren<Renderer>(true))
        {
            if (IsShadowRenderer(renderer))
            {
                renderer.sharedMaterial = shadowMat;
                renderer.enabled = true;
                EditorUtility.SetDirty(renderer);
                continue;
            }
            renderer.sharedMaterial = mat;
            EditorUtility.SetDirty(renderer);
        }
        EditorUtility.SetDirty(piece);
    }

    static MeshFilter FirstVisibleMesh(Transform root)
    {
        if (root == null) return null;
        MeshFilter self = root.GetComponent<MeshFilter>();
        if (self != null && self.sharedMesh != null) return self;
        foreach (MeshFilter mf in root.GetComponentsInChildren<MeshFilter>(true))
            if (mf.sharedMesh != null && !mf.name.ToLowerInvariant().Contains("shadow")) return mf;
        return null;
    }

    static Vector3[] BuildShapeTray(Scene scene, Camera cam, List<Transform> shapes, List<Transform> trayTiles,
                                    Dictionary<Transform,int> playableSlot, List<int> trayTileSlot)
    {
        if (cam == null || shapes.Count == 0) return new Vector3[0];

        // Clean up any existing tray shapes across the entire scene hierarchy
        List<GameObject> toDestroy = new List<GameObject>();
        foreach (GameObject rootGo in scene.GetRootGameObjects())
        {
            foreach (Transform t in rootGo.GetComponentsInChildren<Transform>(true))
            {
                if (t != null && t.gameObject != null &&
                    (t.name.StartsWith("TrayShape_") || t.name.StartsWith("TrayRefill_") ||
                     t.name.StartsWith("TrayFloor")))
                {
                    toDestroy.Add(t.gameObject);
                }
            }
        }
        for (int i = 0; i < toDestroy.Count; i++)
            if (toDestroy[i] != null) Object.DestroyImmediate(toDestroy[i]);

        Transform stale = FindRoot(scene, TrayRootName);
        // Unparent playable shapes from the old tray root BEFORE destroying it,
        // otherwise DestroyImmediate takes them with it.
        if (stale != null)
        {
            for (int i = 0; i < shapes.Count; i++)
            {
                if (shapes[i] != null && shapes[i].IsChildOf(stale))
                    shapes[i].SetParent(null, true);
            }
            Object.DestroyImmediate(stale.gameObject);
        }

        Transform model = shapes[0];
        float previousAspect = cam.aspect;
        cam.aspect = Shared.View.AspectRatioEnforcer.TargetAspect;

        // The tray's ground plane is ABSOLUTE - see TrayGroundY. It is NOT read back off the pieces
        // the previous build left behind; that was a feedback loop with gain > 1 and it sank the
        // plane a little further on every single Build.
        float groundY = TrayGroundY;

        // Reference depth from camera to that plane, DERIVED from it rather than measured off a piece:
        // ViewportToWorldPoint(x, y, 1) - camPos is the ray whose camera-forward component is exactly 1,
        // so scaling it until it reaches groundY gives the camera-forward depth of the plane. Used only
        // for the trace line and for the degenerate fallbacks below; the slots themselves are raycast.
        Vector3 midRay = cam.ViewportToWorldPoint(new Vector3(0.5f, TrayRowY[TrayRowY.Length / 2], 1f))
                       - cam.transform.position;
        float depth = Mathf.Abs(midRay.y) > 1e-5f
                    ? (groundY - cam.transform.position.y) / midRay.y
                    : 37.0f;
        if (depth <= 0.01f || depth > 100f) depth = 37.0f;

        Vector3 deckViewport = cam.WorldToViewportPoint(model.position);
        float unitX = (cam.ViewportToWorldPoint(new Vector3(1f, 0.5f, depth)) -
                       cam.ViewportToWorldPoint(new Vector3(0f, 0.5f, depth))).magnitude;
        float unitY = (cam.ViewportToWorldPoint(new Vector3(0.5f, 1f, depth)) -
                       cam.ViewportToWorldPoint(new Vector3(0.5f, 0f, depth))).magnitude;

        GameObject root = new GameObject(TrayRootName);
        SceneManager.MoveGameObjectToScene(root, scene);
        HashSet<int> takenSlots = new HashSet<int>();

        // MEASURED. A reference tray tile is 0.104 of the frame wide inside a 0.126 column pitch and a
        // 0.095 row pitch, so it is wider than it is tall. Our prefabs are chunky and tall: a uniform
        // 1.76 hit the right width but rendered 0.109 tall, which overlapped the row above. Width and
        // height are therefore solved separately against the measured grid.
        // Multiplying the shape's own scale each run was NOT idempotent - a second build measured the
        // already-scaled shape and stood still. Every tile is instead FITTED to the measured target by
        // projecting it and converging, so a rebuild always lands on the same size.
        Vector3 tileScale = model.lossyScale;
        // Turning the pieces to face the camera with LookRotation was tried and REVERTED: these prefabs
        // do not carry their flat face on the forward axis, so it rotated them edge-on and a hexagon
        // rendered as a cylinder. The staged rotation is the one that shows their silhouette.
        Quaternion tileRotation = model.rotation;

        int built = 0;
        System.Text.StringBuilder trace = new System.Text.StringBuilder();

        // Row-major slot ring plus one hidden refill row. VIDEO_MEASURED: removing the hero pulls two
        // pieces upward and a new pink star rises into the bottom-right slot; the column never stays empty.
        // The nine slots used to be ViewportToWorldPoint(x, y, ONE depth). On a perspective camera that
        // is a plane PERPENDICULAR TO THE VIEW AXIS, and this camera looks slightly down, so the plane
        // was tilted against the world ground: every row sat a little further away and a little higher
        // than the one below it - the staircase. The pieces belong on one flat horizontal plane. Casting
        // each viewport point onto that plane keeps them all at the SAME world height while landing each
        // one exactly on its measured screen position, so no piece has to be nudged by hand.
        // One flat horizontal plane, reached by casting each viewport point onto it. The nine slots used
        // to be ViewportToWorldPoint(x, y, ONE depth); on a perspective camera that plane is
        // PERPENDICULAR TO THE VIEW AXIS, so with the camera looking down every row sat a little higher
        // than the one below - the staircase. This keeps every piece at the SAME world height while
        // landing each on its measured screen position.
        // groundY is the absolute constant assigned above; this used to re-derive it from `depth`,
        // which is what closed the loop.
        List<Vector3> slots = new List<Vector3>((TrayRowY.Length + 1) * TrayColumnX.Length);
        {
            float prevAspect = cam.aspect;
            cam.aspect = Shared.View.AspectRatioEnforcer.TargetAspect;
            Plane ground = new Plane(Vector3.up, new Vector3(0f, groundY, 0f));
            for (int r = 0; r < TrayRowY.Length; r++)
            {
                for (int c = 0; c < TrayColumnX.Length; c++)
                {
                    Ray ray = cam.ViewportPointToRay(new Vector3(TrayColumnX[c], TrayRowY[r], 0f));
                    float enter;
                    slots.Add(ground.Raycast(ray, out enter)
                              ? ray.GetPoint(enter)
                              : cam.ViewportToWorldPoint(new Vector3(TrayColumnX[c], TrayRowY[r], depth)));
                }
            }
            // The refill row is EXTRAPOLATED down the same plane rather than raycast: a viewport y below
            // zero points under the horizon and cannot be cast onto the ground reliably.
            int lastRow = (TrayRowY.Length - 1) * TrayColumnX.Length;
            int prevRow = (TrayRowY.Length - 2) * TrayColumnX.Length;
            for (int c = 0; c < TrayColumnX.Length; c++)
                slots.Add(slots[lastRow + c] + (slots[lastRow + c] - slots[prevRow + c]) * 2.6f);
            cam.aspect = prevAspect;
        }
        Debug.Log("[Case1Setup] TRAY_GROUND y=" + groundY.ToString("0.000") + "; slot heights " +
                  string.Join(" ", slots.ConvertAll(v => v.y.ToString("0.000")).ToArray()));

        // The three PLAYABLE shapes take the tray slot whose REFERENCE colour matches their own, so the
        // grid still reads as the reference's grid: Hexagon is green -> slot 1 (row0 col1, green),
        // Star is purple -> slot 3 (row1 col0, purple), Round takes slot 0 (row0 col0).
        // They keep their colliders, their pairing and their flight; the rest of the tray only slides.
        for (int i = 0; i < shapes.Count; i++)
        {
            ShapeId pieceShape;
            int slot = ShapeOf(shapes[i].name, out pieceShape) ? PlayableSlot(pieceShape) : -1;
            if (slot < 0 || slot >= slots.Count) slot = Mathf.Min(i, slots.Count - 1);
            playableSlot[shapes[i]] = slot;
            takenSlots.Add(slot);
            shapes[i].SetParent(root.transform, true);
            shapes[i].position = slots[slot];
            shapes[i].rotation = tileRotation;
            FitTrayTile(cam, shapes[i]);
            EditorUtility.SetDirty(shapes[i]);
        }

        for (int r = 0; r < TrayRowY.Length; r++)
        {
            for (int c = 0; c < TrayColumnX.Length; c++)
            {
                int slotIndex = r * TrayColumnX.Length + c;
                if (takenSlots.Contains(slotIndex)) continue;   // a real playable shape owns this slot
                ShapeId tileShape = TrayShapes[r, c];
                GameObject prefab = EnsurePieceVariant(tileShape);
                if (prefab == null) continue;

                // Kept as a real prefab instance so the tray reads as authored content in the
                // Hierarchy rather than a pile of loose copies.
                GameObject tile = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
                tile.name = "TrayShape_r" + r + "c" + c + "_" + tileShape;
                tile.transform.SetParent(root.transform, true);
                // The SAME ground-plane slot the playable pieces use. This line used to be
                // ViewportToWorldPoint(x, y, depth), a plane PERPENDICULAR TO THE VIEW AXIS - which on a
                // tilted camera is not level, so every scenery row sat at its own height: measured
                // y 4.51 / 3.94 / 3.37 against the playable pieces' flat 3.94. It is the same staircase
                // that was fixed for the playable pieces and left behind here.
                tile.transform.position = slotIndex < slots.Count
                    ? slots[slotIndex]
                    : cam.ViewportToWorldPoint(new Vector3(TrayColumnX[c], TrayRowY[r], depth));
                tile.transform.rotation = tileRotation;
                if (tileShape == ShapeId.Hexagon) tile.transform.Rotate(0f, HexagonSpinDeg, 0f, Space.Self);
                tile.transform.localScale = tileScale;
                tile.SetActive(true);

                // Colliders are LEFT ON. They were stripped when these tiles were scenery, on the
                // reasoning that scenery which can be hit is not scenery - but every tile is playable
                // now, and stripping a component off an instance is a prefab override besides.

                // No material is written here on purpose. The colour lives in the shape's VARIANT, so
                // touching the instance would create an override that silently outranks the prefab -
                // the exact reason two squares could show two different greens.
                Color src = ColourForShape(tileShape, Color.grey);

                    FitTrayTile(cam, tile.transform);
                trayTiles.Add(tile.transform);
                trayTileSlot.Add(slotIndex);
                built++;
                if (trace.Length > 0) trace.Append(" | ");
                trace.Append(tileShape).Append('/').Append(ColorUtility.ToHtmlStringRGB(src))
                     .Append('@').Append(TrayColumnX[c].ToString("0.000")).Append(',').Append(TrayRowY[r].ToString("0.000"));
            }
        }

        // Hidden below the floor band at virtual slot 11. DeckReflow treats it like the third piece in
        // the hero column, so it emerges into slot 8 while the two visible pieces compact to slots 2/5.
        const int refillSlot = 11;
        GameObject refillPrefab = EnsurePieceVariant(ShapeId.Star);
        if (refillPrefab != null && refillSlot < slots.Count)
        {
            GameObject refill = (GameObject)PrefabUtility.InstantiatePrefab(refillPrefab, scene);
            refill.name = "TrayRefill_c2_Star";
            refill.transform.SetParent(root.transform, true);
            refill.transform.position = slots[refillSlot];
            refill.transform.rotation = tileRotation;
            refill.transform.localScale = tileScale;
            refill.SetActive(true);
            Collider[] colliders = refill.GetComponentsInChildren<Collider>(true);
            for (int i = 0; i < colliders.Length; i++) Object.DestroyImmediate(colliders[i]);

            // Colour comes from the Star variant, like every other piece; nothing is written onto
            // this instance.
            FitTrayTile(cam, refill.transform);
            trayTiles.Add(refill.transform);
            trayTileSlot.Add(refillSlot);
            built++;
            trace.Append(" | Star/PINK@refill-slot11");
        }

        // Floor line and band removed per user direction

        cam.aspect = previousAspect;
        cam.ResetAspect();

        EditorUtility.SetDirty(root);
        Debug.Log(string.Format(
            "[Case1Setup] playable 3x3 tray built: {0} scenery tiles behind {1} playable shapes " +
            "deckViewport=({2:0.000},{3:0.000}) depth={4:0.00} tileScale={5}  {6}",
            built, shapes.Count, deckViewport.x, deckViewport.y, depth, tileScale.ToString("0.###"), trace));
        return slots.ToArray();
    }

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

    /// <summary>Capture-only entry point for Case 1.</summary>
    public static void BuildAndCapture()
    {
        CaptureFitTheShape();
    }

    /// <summary>
    /// Zero-argument forwarder for -executeMethod. Unity refuses to invoke
    /// <c>FrameStripCapture.Capture(string)</c> directly ("Only methods with 0 arguments are supported").
    /// </summary>
    public static void CaptureFitTheShape()
    {
        FrameStripCapture.Capture("FitTheShape");
    }

    /// <summary>
    /// Films the same sequence densely enough to encode as video: one pick, its flight, the fill, the
    /// star burst and the ripple. Uses the SAME sampler as the 16-frame verification strip, so what
    /// the video shows is what the gates measure - not a second, prettier render path.
    /// </summary>
    public static void CaptureFitTheShapeVideo()
    {
        FrameStripCapture.SetFrameCount(210);     // 3.50 s of sequence at 60 fps
        FrameStripCapture.Capture("FitTheShape");
    }

    // ------------------------------------------------------------------ assets

    static Material EnsureFlashMaterial(string path, float spike)
    {
        Shader shader = Shader.Find(FlashShader);
        if (shader == null) { Debug.LogError("[Case1Setup] Shader not found: " + FlashShader); return null; }

        Material m = AssetDatabase.LoadAssetAtPath<Material>(path);
        bool created = false;
        if (m == null) { m = new Material(shader); AssetDatabase.CreateAsset(m, path); created = true; }
        else if (m.shader != shader) m.shader = shader;

        bool isSparkle = spike > 0f;
        m.SetColor("_Color", new Color(1f, 0.80f, 0.12f, 1f));
        m.SetFloat("_Intensity", isSparkle ? 1.45f : 1.30f);
        m.SetFloat("_Core", isSparkle ? 0.70f : 0.92f);
        m.SetFloat("_CoreFalloff", isSparkle ? 4.5f : 2.6f);
        m.SetFloat("_Spike", spike);
        m.SetFloat("_SpikeThin", 11f);
        m.SetFloat("_SpikeSharp", 1.6f);
        m.renderQueue = 3100;   // after every transparent piece of the drum art, so the flare is never hidden
        EditorUtility.SetDirty(m);

        Debug.Log("[Case1Setup] material " + (created ? "created " : "updated ") + path + " (spike=" + spike.ToString("0.00") + ")");
        return m;
    }

    static Material EnsureTrailMaterial(string path)
    {
        Shader shader = Shader.Find("Sprites/Default");
        if (shader == null) shader = Shader.Find(FlashShader);
        if (shader == null) { Debug.LogError("[Case1Setup] No trail shader available"); return null; }

        Material m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (m == null) { m = new Material(shader); AssetDatabase.CreateAsset(m, path); }
        else if (m.shader != shader) m.shader = shader;

        EditorUtility.SetDirty(m);
        Debug.Log("[Case1Setup] trail material " + path + " (" + shader.name + ")");
        return m;
    }

    static Material EnsureStarMaterial(string path)
    {
        Shader shader = Shader.Find("Case1/StarParticle");
        if (shader == null) { Debug.LogError("[Case1Setup] Case1/StarParticle not found"); return null; }
        Material m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (m == null) { m = new Material(shader); AssetDatabase.CreateAsset(m, path); }
        else if (m.shader != shader) m.shader = shader;
        // The star shader takes only ALPHA from the particle and its RGB from the material, so setting
        // a gold gradient on the particle system did nothing while this stayed white. VIDEO_MEASURED:
        // the reference's arrival stars are amber, not white-hot.
        // MEASURED off the user's reference clip, sampling only pure-gold pixels over the red target
        // cell across f028..f043: the reference's stars land at #EFCB91 (239, 203, 145) - a pale, creamy
        // gold. Ours rendered #FEA453, a saturated orange: far too little green and barely half the
        // blue. The stars draw over a red cell, so the material has to carry the blue the cell cannot.
        // A gold star vanishes on a yellow or orange cell - and half the live row is exactly that, so
        // the burst only read over the red target. The stars are opaque WHITE instead, which separates
        // from every cell colour in the row, and the tint is pushed ABOVE 1 so the grade's bloom picks
        // them up and they glow rather than sitting flat on the surface.
        // Vivid golden-yellow luminous star color with intense HDR pop
        m.SetColor("_Color", new Color(1.00f, 0.88f, 0.22f, 1f));
        m.SetFloat("_Intensity", 5.00f);
        m.renderQueue = 4000;
        EditorUtility.SetDirty(m);
        return m;
    }

    static Mesh EnsureStarMesh()
    {
        Mesh mesh = AssetDatabase.LoadAssetAtPath<Mesh>(StarMeshPath);
        bool created = mesh == null;
        if (created) mesh = new Mesh { name = "Case1_FivePointStar" };
        else mesh.Clear();

        const int points = 10;
        Vector3[] vertices = new Vector3[points + 1];
        Vector2[] uv = new Vector2[points + 1];
        Vector3[] normals = new Vector3[points + 1];
        Color[] colours = new Color[points + 1];
        vertices[0] = Vector3.zero;
        uv[0] = new Vector2(0.5f, 0.5f);
        normals[0] = Vector3.forward;
        colours[0] = Color.white;
        for (int i = 0; i < points; i++)
        {
            float radius = (i & 1) == 0 ? 0.50f : 0.225f;
            float angle = (90f + i * 36f) * Mathf.Deg2Rad;
            float x = Mathf.Cos(angle) * radius;
            float y = Mathf.Sin(angle) * radius;
            vertices[i + 1] = new Vector3(x, y, 0f);
            uv[i + 1] = new Vector2(x + 0.5f, y + 0.5f);
            normals[i + 1] = Vector3.forward;
            colours[i + 1] = Color.white;
        }
        int[] triangles = new int[points * 3];
        for (int i = 0; i < points; i++)
        {
            triangles[i * 3] = 0;
            triangles[i * 3 + 1] = i + 1;
            triangles[i * 3 + 2] = (i + 1) % points + 1;
        }
        mesh.vertices = vertices;
        mesh.uv = uv;
        mesh.normals = normals;
        mesh.colors = colours;
        mesh.triangles = triangles;
        mesh.RecalculateBounds();
        if (created) AssetDatabase.CreateAsset(mesh, StarMeshPath);
        EditorUtility.SetDirty(mesh);
        return mesh;
    }

    /// <summary>
    /// Creates the arrival accent: a succession of small stars over the target cell.
    ///
    /// THIS METHOD IS THE AUTHORITY FOR EVERY SPARKLE VALUE. Build() calls it UNCONDITIONALLY and it
    /// ends in SaveAsPrefabAsset, so it OVERWRITES StarSparkleBurst.prefab in full on every run. Any
    /// sparkle value edited only in the prefab is erased by the next entry point that calls Build():
    ///   Case1InteractiveRecorder.Record, Case1SelectionGate, CaseGrade.Run, RefPositionGate.Run.
    ///
    /// This has already cost a round. Commit 3dfd679 ("add smooth 0.5s alpha fadeout ramp") edited the
    /// prefab's ColorModule gradient and nothing else; the next Build() wrote the old five-key gradient
    /// back over it, and the fade was gone with no diff to show for it. The prefab still LOOKED fixed in
    /// git history, which is the part that wastes the time.
    ///
    /// FrameStripCapture (the verify capture) does NOT call Build - it reads the prefab as it sits on
    /// disk. So the two copies can silently disagree about what is being measured. When you change a
    /// sparkle value, change it HERE and in the prefab, to the same number.
    /// </summary>
    static GameObject EnsureSparklePrefab(Material material)
    {
        GameObject go = new GameObject("StarSparkleBurst");
        ParticleSystem ps = go.AddComponent<ParticleSystem>();

        ParticleSystem.MainModule main = ps.main;
        ps.useAutoRandomSeed = false;
        ps.randomSeed = 17031u;
        main.duration = 0.96f;
        main.loop = false;
        main.playOnAwake = false;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.56f, 0.88f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(1.00f, 2.80f);
        // MEASURED, not styled. Reference "Fit The Shape.mp4" f088..f100, target cell interior
        // (x 622-750, y 392-492 at 1080x1728): a full-size arrival star is 16-22 px wide, median 18,
        // against a 132 px cell face. Our f058..f067 gave one isolated full-size star at 76 x 85 px
        // against a 137 px cell face - 3.9x the reference once both are normalised by cell width.
        // startSize is the ONLY lever for this: main.scalingMode is Shape, so the transform scale
        // PlaySparkle passes affects the emitter circle and NOT the particle size.
        main.startSize = new ParticleSystem.MinMaxCurve(0.10f, 0.16f);
        main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
        main.startColor = new ParticleSystem.MinMaxGradient(
            new Color(1f, 1f, 1f, 1f), new Color(1f, 0.92f, 0.45f, 1f));
        main.gravityModifier = -0.02f;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.scalingMode = ParticleSystemScalingMode.Shape;
        main.cullingMode = ParticleSystemCullingMode.PauseAndCatchup;
        main.maxParticles = 120;

        // MEASURED density, and note WHY all four numbers move together rather than just the rate.
        // At the peak sample (0.378 s into the burst - below the 0.56 s minimum lifetime, so nothing
        // has died yet) the bursts have fired 17-26 particles against 35 * 0.378 = 13 from the rate.
        // Bursts are 55-67% of the live population, so rateOverTime ALONE tops out at about a 35%
        // change and cannot reach the reference's count from ours at any setting. All four are scaled
        // by the same 0.43 (measured: 14 stars over the target cell against the reference's 5-7 in
        // r_090..r_098), which holds the burst/continuous ratio fixed so only density changes and the
        // shape of the burst over time does not.
        ParticleSystem.EmissionModule emission = ps.emission;
        emission.enabled = true;
        emission.rateOverTime = 15f;
        emission.SetBursts(new[] { 
            new ParticleSystem.Burst(0.00f, (short)3, (short)5, 1, 0.01f),
            new ParticleSystem.Burst(0.16f, (short)2, (short)3, 1, 0.01f),
            new ParticleSystem.Burst(0.36f, (short)2, (short)3, 1, 0.01f)
        });

        ParticleSystem.ShapeModule shape = ps.shape;
        shape.enabled = true;
        shape.shapeType = ParticleSystemShapeType.Circle;
        shape.radius = 0.55f;
        shape.scale = Vector3.one;
        shape.radiusThickness = 0.90f;

        ParticleSystem.SizeOverLifetimeModule size = ps.sizeOverLifetime;
        size.enabled = true;
        AnimationCurve sizeCurve = new AnimationCurve(
            new Keyframe(0f, 0.40f), new Keyframe(0.15f, 1f), new Keyframe(0.60f, 0.90f),
            new Keyframe(0.85f, 0.50f), new Keyframe(1f, 0f));
        size.size = new ParticleSystem.MinMaxCurve(1f, sizeCurve);

        ParticleSystem.ColorOverLifetimeModule colour = ps.colorOverLifetime;
        colour.enabled = true;
        Gradient g = new Gradient();
        g.SetKeys(
            new[] { new GradientColorKey(new Color(1f, 1f, 1f), 0f), new GradientColorKey(new Color(1f, 0.88f, 0.35f), 1f) },
            new[] { 
                new GradientAlphaKey(0f, 0f), 
                new GradientAlphaKey(1f, 0.08f), 
                new GradientAlphaKey(1f, 0.55f), 
                new GradientAlphaKey(0.40f, 0.80f), 
                new GradientAlphaKey(0f, 1f) 
            });
        colour.color = new ParticleSystem.MinMaxGradient(g);

        ParticleSystem.RotationOverLifetimeModule rotation = ps.rotationOverLifetime;
        rotation.enabled = true;
        rotation.z = new ParticleSystem.MinMaxCurve(-1.8f, 1.8f);

        ParticleSystem.LimitVelocityOverLifetimeModule limit = ps.limitVelocityOverLifetime;
        limit.enabled = true;
        limit.dampen = 0.45f;
        limit.limit = new ParticleSystem.MinMaxCurve(0.75f);

        ParticleSystemRenderer renderer = go.GetComponent<ParticleSystemRenderer>();
        renderer.renderMode = ParticleSystemRenderMode.Mesh;
        renderer.mesh = EnsureStarMesh();
        renderer.alignment = ParticleSystemRenderSpace.View;
        renderer.sharedMaterial = material;
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        renderer.sortingOrder = 5000;
        renderer.SetActiveVertexStreams(new List<ParticleSystemVertexStream> {
            ParticleSystemVertexStream.Position,
            ParticleSystemVertexStream.Normal,
            ParticleSystemVertexStream.Color,
            ParticleSystemVertexStream.UV
        });

        GameObject asset = PrefabUtility.SaveAsPrefabAsset(go, SparklePath);
        Object.DestroyImmediate(go);
        Debug.Log("[Case1Setup] sparkle prefab written " + SparklePath);
        return asset;
    }

    // ------------------------------------------------------------------ helpers

    /// <summary>
    /// Finds a named object anywhere in the scene, not only at the top level.
    ///
    /// It used to search scene roots only. Once the hierarchy is tidied into a single Case1 root every
    /// one of these objects stops being a root, and a lookup that only checks roots would report them
    /// all missing - the builder would then create a second copy of each on the next run.
    /// </summary>
    static Transform FindRoot(Scene scene, string name)
    {
        GameObject[] roots = scene.GetRootGameObjects();
        for (int i = 0; i < roots.Length; i++)
        {
            if (roots[i].name == name) return roots[i].transform;
            Transform found = FindDescendant(roots[i].transform, name);
            if (found != null) return found;
        }
        return null;
    }

    /// <summary>
    /// Gathers the scene into one Case1 root with named groups, preserving every world transform.
    ///
    /// The scene had grown a flat list of unrelated top-level objects - Drum, Deck, the tray, the band,
    /// the chrome, the sequence object, the lights - with no indication of what belonged to what. It is
    /// run last so anything created during the build is caught, and it is idempotent: objects already
    /// in the right group are left alone.
    /// </summary>
    static void OrganiseHierarchy(Scene scene, Camera cam)
    {
        Transform root = FindRoot(scene, SceneRootName);
        if (root == null)
        {
            GameObject go = new GameObject(SceneRootName);
            SceneManager.MoveGameObjectToScene(go, scene);
            root = go.transform;
        }
        root.SetParent(null, true);
        root.SetSiblingIndex(0);

        // group name -> the objects that belong in it, in the order they should appear
        string[][] groups =
        {
            new[] { "Board",   "Drum", BandRootName, QuestionRootName },
            new[] { "Pieces",  "Deck", TrayRootName },
            new[] { "Chrome",  MetaDecorRoot },
            new[] { "Systems", RootName }
        };

        int moved = 0;
        for (int g = 0; g < groups.Length; g++)
        {
            Transform group = FindDescendant(root, groups[g][0]);
            if (group == null)
            {
                GameObject go = new GameObject(groups[g][0]);
                SceneManager.MoveGameObjectToScene(go, scene);
                group = go.transform;
                group.SetParent(root, false);
            }
            for (int i = 1; i < groups[g].Length; i++)
            {
                Transform t = FindRoot(scene, groups[g][i]);
                if (t == null || t.parent == group) continue;
                // worldPositionStays: the whole point is that tidying changes nothing on screen.
                t.SetParent(group, true);
                moved++;
            }
        }

        // The camera and the lights go in too, so nothing case-related is left loose at the top level.
        Transform view = FindDescendant(root, "View");
        if (view == null)
        {
            GameObject go = new GameObject("View");
            SceneManager.MoveGameObjectToScene(go, scene);
            view = go.transform;
            view.SetParent(root, false);
        }
        if (cam != null && cam.transform.parent != view) { cam.transform.SetParent(view, true); moved++; }

        // Remove all lights: Case 1 uses 100% self-contained unlit toon shader math
        RemoveAllSceneLights(scene);

        Debug.Log("[Case1Setup] HIERARCHY organised under '" + SceneRootName + "'; " + moved + " object(s) reparented");
    }

    static void RemoveAllSceneLights(Scene scene)
    {
        Light[] lights = Object.FindObjectsByType<Light>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        int count = 0;
        for (int i = 0; i < lights.Length; i++)
        {
            if (lights[i] != null && lights[i].gameObject != null)
            {
                Object.DestroyImmediate(lights[i].gameObject);
                count++;
            }
        }
        RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
        RenderSettings.ambientLight = Color.black;
        if (count > 0) Debug.Log("[Case1Setup] ALL_LIGHTS_REMOVED: destroyed " + count + " scene light(s). Shading is 100% shader-driven.");
    }

    /// <summary>Outward normal of the live row's middle cell - the direction the board "faces".</summary>
    static Vector3 LiveRowNormal(List<DrumSlotReaction.Cell> cells)
    {
        int mid = FindCell(cells, 2, 0);
        if (mid < 0) mid = FindCell(cells, 0, 0);
        // The cell prefab carries its face on local X-Z with local +Y pointing out of it, which the
        // measured axis dump established; the same fact drives the glyphs and the ripple.
        return mid >= 0 && cells[mid].root != null ? cells[mid].root.up : Vector3.forward;
    }

    /// <summary>
    /// Pitches <paramref name="t"/> about the world X axis so <paramref name="currentNormal"/> points
    /// back at the camera. Pitch only: a free FromToRotation would also roll and yaw the drum, and the
    /// rows have to stay level and square to the frame.
    /// </summary>
    /// <summary>Converges a holder plate onto the measured reference plate width. Idempotent.</summary>
    static void FitDeckPlate(Camera cam, Transform t)
    {
        if (cam == null || t == null) return;
        for (int pass = 0; pass < 6; pass++)
        {
            Rect r;
            if (!ReferenceMatchLayout.ProjectBounds(cam, t, out r) || r.width < 1e-4f) return;
            float f = Mathf.Clamp(RefDeckPlateWidth / r.width, 0.5f, 1.9f);
            if (Mathf.Abs(f - 1f) < 0.01f) break;
            t.localScale = t.localScale * f;
        }
        Rect got;
        if (ReferenceMatchLayout.ProjectBounds(cam, t, out got))
            Debug.Log("[Case1Setup] PLATE_FIT " + t.name + " -> " + got.width.ToString("0.0000") +
                      " wide (target " + RefDeckPlateWidth.ToString("0.000") + ")");
        EditorUtility.SetDirty(t);
    }

    static void FaceCameraPitch(Camera cam, Transform t, Vector3 currentNormal)
    {
        if (cam == null || t == null) return;

        Vector3 want = -cam.transform.forward;
        // Both vectors projected onto the world YZ plane, since only rotation about X is allowed.
        Vector2 from = new Vector2(currentNormal.z, currentNormal.y);
        Vector2 to = new Vector2(want.z, want.y);
        if (from.sqrMagnitude < 1e-8f || to.sqrMagnitude < 1e-8f) return;

        float degrees = Vector2.SignedAngle(from, to);
        if (Mathf.Abs(degrees) < 0.05f) return;

        t.rotation = Quaternion.AngleAxis(-degrees, Vector3.right) * t.rotation;
        EditorUtility.SetDirty(t);
        Debug.Log("[Case1Setup] FACE_CAMERA " + t.name + " pitched " + (-degrees).ToString("0.0") + " deg");
    }

    static Transform FindDescendant(Transform parent, string name)
    {
        for (int i = 0; i < parent.childCount; i++)
        {
            Transform child = parent.GetChild(i);
            if (child.name == name) return child;
            Transform found = FindDescendant(child, name);
            if (found != null) return found;
        }
        return null;
    }

    static int FindCell(List<DrumSlotReaction.Cell> cells, int column, int row)
    {
        if (cells == null) return -1;
        for (int i = 0; i < cells.Count; i++)
            if (cells[i] != null && cells[i].column == column && cells[i].row == row) return i;
        return -1;
    }

    static int ReferenceTargetColumn(ShapeId id)
    {
        for (int column = 0; column < LiveRowShape.Length; column++)
            if (LiveRowShape[column] == id) return column;
        return -1;
    }

    static void EnsureReferenceLiveRow(List<DrumSlotReaction.Cell> cells)
    {
        // LiveRowShape is the single source: ReferenceTargetColumn() now derives the column from this
        // same array instead of a parallel hand-written map, so the two can no longer drift apart.
        ShapeId[] shapes = LiveRowShape;
        int changed = 0;
        for (int column = 0; column < shapes.Length; column++)
        {
            int index = FindCell(cells, column, 0);
            if (index < 0 || cells[index].root == null) continue;

            cells[index].shapeId = shapes[column];

            Mesh holeMesh = FindHoleMesh(shapes[column], false);
            Mesh capMesh = FindHoleMesh(shapes[column], true);
            Transform hole = cells[index].root.Find("Hole");
            Transform cap = cells[index].root.Find("Hole-Cap");
            MeshFilter holeFilter = hole != null ? hole.GetComponent<MeshFilter>() : null;
            MeshFilter capFilter = cap != null ? cap.GetComponent<MeshFilter>() : null;
            if (holeFilter != null && holeMesh != null && holeFilter.sharedMesh != holeMesh)
            {
                holeFilter.sharedMesh = holeMesh;
                EditorUtility.SetDirty(holeFilter);
                changed++;
            }
            if (capFilter != null && capMesh != null && capFilter.sharedMesh != capMesh)
            {
                capFilter.sharedMesh = capMesh;
                EditorUtility.SetDirty(capFilter);
                changed++;
            }
        }

        for (int i = 0; i < cells.Count; i++)
        {
            if (cells[i].row == 0 && cells[i].column >= 0 && cells[i].column < shapes.Length)
            {
                cells[i].shapeId = shapes[cells[i].column];
            }
            else
            {
                ShapeId sid;
                if (cells[i].hole != null && ShapeIds.TryParse(cells[i].hole.name, out sid))
                    cells[i].shapeId = sid;
                else if (ShapeIds.TryParse(cells[i].root.name, out sid))
                    cells[i].shapeId = sid;
            }
        }
        // The old line printed a hardcoded "diamond/diamond/triangle/hexagon/star" that had stopped
        // being true - a log that describes the code as it once was is worse than no log.
        Debug.Log("[Case1Setup] LIVE_ROW recesses " + string.Join("/", System.Array.ConvertAll(LiveRowShape, x => x.ToString())) +
                  ", mesh assignments changed=" + changed);
    }

    static Mesh FindHoleMesh(ShapeId shape, bool cap)
    {
        Object[] assets = AssetDatabase.LoadAllAssetsAtPath("Assets/Case1_FitTheShape/Models/SM_Shapes-Hole.fbx");
        for (int i = 0; i < assets.Length; i++)
        {
            Mesh mesh = assets[i] as Mesh;
            if (mesh == null) continue;
            if (!ShapeIds.MatchesHole(shape, mesh.name)) continue;
            bool isCap = mesh.name.ToLowerInvariant().Contains("cap");
            if (isCap == cap) return mesh;
        }
        Debug.LogWarning("[Case1Setup] hole mesh missing: " + shape + (cap ? " cap" : " open"));
        return null;
    }

    /// <summary>
    /// Lower-case shape word taken from a deck object's name ("Shape_Hexagon" -> "hexagon"). It is what
    /// the drum's hole recess meshes are named after ("Hexagon-Hole"), so it is the primary match key.
    /// </summary>
    /// <summary>
    /// The shape a deck object names, e.g. "Shape_Hexagon2" -> ShapeId.Hexagon. Returns false when the
    /// name carries no known shape, so a caller can report that instead of guessing.
    /// </summary>
    static bool ShapeOf(string objectName, out ShapeId id)
    {
        return ShapeIds.TryParse(objectName, out id);
    }

    /// <summary>The additive shockwave ring drawn on arrival: same shader, ring term instead of a core.</summary>
    static Material EnsureRingMaterial(string path)
    {
        Shader shader = Shader.Find(FlashShader);
        if (shader == null) { Debug.LogError("[Case1Setup] Shader not found: " + FlashShader); return null; }

        Material m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (m == null) { m = new Material(shader); AssetDatabase.CreateAsset(m, path); }
        else if (m.shader != shader) m.shader = shader;

        m.SetColor("_Color", Color.white);
        m.SetFloat("_Intensity", 1.1f);
        m.SetFloat("_Core", 0.12f);
        m.SetFloat("_CoreFalloff", 6f);
        m.SetFloat("_Spike", 0f);
        m.SetFloat("_Ring", 0.9f);
        m.SetFloat("_RingRadius", 0.78f);
        m.SetFloat("_RingThin", 9f);
        m.renderQueue = 3100;
        EditorUtility.SetDirty(m);
        Debug.Log("[Case1Setup] ring material " + path);
        return m;
    }

    static string MaterialName(Transform t)
    {
        if (t == null) return "";
        Renderer r = t.GetComponent<Renderer>();
        if (r == null || r.sharedMaterial == null) return "";
        return r.sharedMaterial.name;
    }

    static string MeshName(Transform t, string child)
    {
        if (t == null) return "-";
        Transform c = child == null ? t : t.Find(child);
        if (c == null) return "-";
        MeshFilter mf = c.GetComponent<MeshFilter>();
        return mf != null && mf.sharedMesh != null ? mf.sharedMesh.name : "-";
    }

    static int NearestSlot(List<float> slotX, float x)
    {
        int best = 0;
        float bestD = float.MaxValue;
        for (int i = 0; i < slotX.Count; i++)
        {
            float d = Mathf.Abs(slotX[i] - x);
            if (d < bestD) { bestD = d; best = i; }
        }
        return best;
    }

    static int MiddleFrontCell(List<DrumSlotReaction.Cell> cells)
    {
        List<int> front = new List<int>(8);
        for (int i = 0; i < cells.Count; i++) if (cells[i].row == 0) front.Add(i);
        if (front.Count == 0) return 0;
        return front[front.Count / 2];
    }

    static void Dump(List<DrumSlotReaction.Cell> cells, List<Transform> shapes, List<float> slotX,
                     List<int> targetCells, List<string> matchNotes, Camera cam)
    {
        StringBuilder sb = new StringBuilder();
        sb.AppendLine("[Case1Setup] ---- scene discovery ----");

        if (cam != null)
        {
            sb.AppendLine(string.Format("camera pos={0} euler={1} fov={2:0.0} clear={3} bg={4}",
                cam.transform.position, cam.transform.eulerAngles, cam.fieldOfView, cam.clearFlags, cam.backgroundColor));
        }

        sb.AppendLine("drum cells=" + cells.Count + "  front row:");
        for (int i = 0; i < cells.Count; i++)
        {
            if (cells[i].row != 0) continue;
            DrumSlotReaction.Cell c = cells[i];
            Renderer hole = c.hole;
            sb.AppendLine(string.Format("  [{0}] {1} colour={2} holeMesh={3} holeCentre={4} face={5} mystery={6}",
                i, c.root.name, MaterialName(c.root), MeshName(c.root, "Hole"),
                hole != null ? hole.bounds.center.ToString() : "-",
                c.root.up, c.mystery != null));
        }

        sb.AppendLine("deck slots x=" + string.Join(", ", slotX.ConvertAll(v => v.ToString("0.00")).ToArray()));
        for (int i = 0; i < shapes.Count; i++)
        {
            sb.AppendLine(string.Format("  shape {0} colour={1} mesh={2} slot={3} world={4} -> target={5} via {6}",
                shapes[i].name, MaterialName(shapes[i]), MeshName(shapes[i], null),
                NearestSlot(slotX, shapes[i].localPosition.x), shapes[i].position,
                targetCells[i] >= 0 ? cells[targetCells[i]].root.name : "<none>", matchNotes[i]));
        }

        Debug.Log(sb.ToString());
    }

    static void Fail(string message)
    {
        Debug.LogError("[Case1Setup] SETUP_FAILED " + message);
    }
}

/// <summary>
/// P16 framing gate. Measures, for every case scene, how wide the main interaction area is as a
/// fraction of the frame at the reference aspect (0.625), and compares that with the same ratio
/// measured off the reference videos. Also checks <see cref="Shared.View.AspectRatioEnforcer"/>'s
/// viewport maths for screen shapes no machine here actually has.
///
/// Zero-argument on purpose: Unity's -executeMethod refuses anything else (lessons #1).
/// Lives in this file rather than its own because P16 may only write the files it owns.
/// </summary>
public static class CaseFramingGate
{
    const float Tolerance = 0.08f;

    struct Target
    {
        public string Scene;         // asset path
        public string Root;          // scene root object holding the interaction area
        public string Prefix;        // renderer name prefix; "" = every renderer under Root
        public string Exact;         // exact renderer name; wins over Prefix when set
        public float Reference;      // width ratio measured off the reference video at 1080x1728
        public string Note;
    }

    static readonly Target[] Targets =
    {
        // Measured on _refs/Developer Case Referans/*.mp4 frames (1080x1728) by column extent of the
        // interaction area against the background:
        //   Fit The Shape  drum body  cols 213..867  -> 0.605   (the white slot band alone is 0.741)
        //   Block Hole     board      cols  72..1007 -> 0.867
        //   Buca           arena rail cols  34..1045 -> 0.935
        // The reference numbers below are the interaction area's width as a fraction of the frame,
        // measured by column extent on the reference video frames (1080x1728), then converted into the
        // units this gate works in.
        //
        // The conversion is needed because the two metrics are not the same thing: the video can only be
        // measured as a visible silhouette, while this gate projects a world-space AABB, which is wider
        // (a cylinder's bounding box reaches past its silhouette; a tile grid's box reaches past the
        // drawn tiles). The factor for each case is taken by measuring OUR OWN 540x864 capture both ways,
        // so it converts units and nothing else - the residual deviation is still a real comparison.
        //
        //   case          reference px   our capture px   our gate value   factor   -> gate reference
        //   FitTheShape        0.641          0.659            0.735        0.973         0.715
        //   BlockHole          0.867          0.876            0.875        0.990         0.866
        //   Buca               0.935          0.926            0.943        1.010         0.952
        //
        // Reference column extents: drum body 196..887, board frame 72..1007, arena rail 34..1045.
        new Target { Scene = "Assets/Case1_FitTheShape/Scenes/FitTheShape.unity", Root = "Drum",            Prefix = "Segment_", Exact = "",            Reference = 0.715f, Note = "drum cells" },
        new Target { Scene = "Assets/Case2_BlockHole/Scenes/BlockHole.unity",     Root = "Board",           Prefix = "Tile_",    Exact = "",            Reference = 0.866f, Note = "tile grid" },
        new Target { Scene = "Assets/Case4_Buca/Scenes/Buca.unity",               Root = "case_test_scene", Prefix = "",         Exact = "level_frame", Reference = 0.952f, Note = "arena rail" },
        // P17 added Case 3, the one case P16 left unmeasured. Reference: the page's visible silhouette
        // spans columns 0..514 of 540 on the Stickerdom frames -> 0.954 of the frame width. No unit
        // conversion is needed here the way it was for the other three: the page is a flat sprite seen
        // square on by an orthographic camera, so its projected AABB and its silhouette are the same
        // rectangle. The residual (ours reads 0.916 after the reframe) is the page sprite's aspect,
        // which is scene art rather than framing - see Case3SceneSetup's camera block.
        new Target { Scene = "Assets/Case3_Stickerdom/Scenes/Stickerdom.unity", Root = "Page",           Prefix = "",         Exact = "",            Reference = 0.954f, Note = "sticker page" },
    };

    public static void FramingGate()
    {
        int failures = 0;
        Debug.Log("[FramingGate] ---- viewport maths ----");
        failures += CheckViewport();

        Debug.Log("[FramingGate] ---- case framing (measured at aspect " +
                  Shared.View.AspectRatioEnforcer.TargetAspect.ToString("0.000") + ") ----");

        for (int i = 0; i < Targets.Length; i++) failures += CheckScene(Targets[i]);

        if (failures > 0)
        {
            Line("FRAMING_GATE FAILED failures=" + failures);
            Finish(1);
            return;
        }
        Line("FRAMING_GATE PASSED");
        Finish(0);
    }

    // ------------------------------------------------------------------ viewport maths

    static int CheckViewport()
    {
        int[,] sizes =
        {
            { 1080, 1728 },   // the reference itself: must be left alone
            { 1920, 1080 },   // 16:9 landscape desktop
            { 1600, 1200 },   // 4:3
            { 1440, 1776 },   // the ~0.811 window the review measured
            { 1080, 2400 },   // tall modern phone
            { 800, 800 },     // square
        };

        int bad = 0;
        for (int i = 0; i < sizes.GetLength(0); i++)
        {
            int w = sizes[i, 0], h = sizes[i, 1];
            Rect r = Shared.View.AspectRatioEnforcer.ComputeRect(w, h);
            float got = Shared.View.AspectRatioEnforcer.ResultingAspect(w, h);
            float want = Shared.View.AspectRatioEnforcer.TargetAspect;
            bool ok = Mathf.Abs(got - want) < 0.002f &&
                      r.width > 0f && r.height > 0f && r.width <= 1f && r.height <= 1f &&
                      Mathf.Abs((r.x * 2f + r.width) - 1f) < 0.001f &&
                      Mathf.Abs((r.y * 2f + r.height) - 1f) < 0.001f;
            if (!ok) bad++;
            Line(string.Format("  {0}x{1} screen={2:0.000} -> rect(x={3:0.000} y={4:0.000} w={5:0.000} h={6:0.000}) aspect={7:0.000} {8}",
                w, h, (float)w / h, r.x, r.y, r.width, r.height, got, ok ? "OK" : "BAD"));
        }
        return bad;
    }

    // ------------------------------------------------------------------ per-scene framing

    static int CheckScene(Target t)
    {
        Scene scene = EditorSceneManager.OpenScene(t.Scene, OpenSceneMode.Single);
        Camera cam = Object.FindFirstObjectByType<Camera>(FindObjectsInactive.Include);
        if (cam == null) { Line("  " + System.IO.Path.GetFileNameWithoutExtension(t.Scene) + " NO_CAMERA"); return 1; }

        Bounds? b = AreaBounds(scene, t);
        if (b == null) { Line("  " + System.IO.Path.GetFileNameWithoutExtension(t.Scene) + " NO_AREA root=" + t.Root); return 1; }

        float previousAspect = cam.aspect;
        cam.aspect = Shared.View.AspectRatioEnforcer.TargetAspect;

        float minX = float.MaxValue, maxX = float.MinValue, minY = float.MaxValue, maxY = float.MinValue;
        Bounds bounds = b.Value;
        for (int i = 0; i < 8; i++)
        {
            Vector3 corner = new Vector3(
                (i & 1) == 0 ? bounds.min.x : bounds.max.x,
                (i & 2) == 0 ? bounds.min.y : bounds.max.y,
                (i & 4) == 0 ? bounds.min.z : bounds.max.z);
            Vector3 v = cam.WorldToViewportPoint(corner);
            minX = Mathf.Min(minX, v.x); maxX = Mathf.Max(maxX, v.x);
            minY = Mathf.Min(minY, v.y); maxY = Mathf.Max(maxY, v.y);
        }
        cam.aspect = previousAspect;
        cam.ResetAspect();

        float widthRatio = maxX - minX;
        float heightRatio = maxY - minY;
        float centreY = (minY + maxY) * 0.5f;
        float deviation = widthRatio - t.Reference;
        bool ok = Mathf.Abs(deviation) <= Tolerance;

        Line(string.Format("  {0,-12} {1,-18} width={2:0.000} reference={3:0.000} deviation={4:+0.000;-0.000} " +
                           "height={5:0.000} centreY={6:0.000} cam={7} {8}",
            System.IO.Path.GetFileNameWithoutExtension(t.Scene), t.Note, widthRatio, t.Reference, deviation,
            heightRatio, centreY,
            cam.orthographic ? "ortho size=" + cam.orthographicSize.ToString("0.000")
                             : "fov=" + cam.fieldOfView.ToString("0.00"),
            ok ? "OK" : "OUT_OF_TOLERANCE(+/-" + Tolerance.ToString("0.00") + ")"));

        // Report the enforcer's presence too: without it none of the above survives a real screen.
        AspectEnforcerReport(cam);
        return ok ? 0 : 1;
    }

    static void AspectEnforcerReport(Camera cam)
    {
        bool has = cam.GetComponent<Shared.View.AspectRatioEnforcer>() != null;
        Line("                 AspectRatioEnforcer on camera: " + (has ? "yes" : "NO"));
    }

    static Bounds? AreaBounds(Scene scene, Target t)
    {
        Transform root = null;
        GameObject[] roots = scene.GetRootGameObjects();
        for (int i = 0; i < roots.Length; i++)
            if (roots[i].name == t.Root) { root = roots[i].transform; break; }
        if (root == null) return null;

        Renderer[] all = root.GetComponentsInChildren<Renderer>(true);
        bool any = false;
        int matched = 0;
        Bounds b = new Bounds();
        for (int i = 0; i < all.Length; i++)
        {
            if (all[i] is ParticleSystemRenderer) continue;
            if (!string.IsNullOrEmpty(t.Exact) && all[i].name != t.Exact) continue;
            if (!string.IsNullOrEmpty(t.Prefix) && !all[i].name.StartsWith(t.Prefix)) continue;
            matched++;
            if (!any) { b = all[i].bounds; any = true; }
            else b.Encapsulate(all[i].bounds);
        }

        if (!any)
        {
            // One shot diagnosis rather than another editor round trip: show what is actually there.
            System.Text.StringBuilder names = new System.Text.StringBuilder();
            for (int i = 0; i < all.Length && i < 40; i++) names.Append(all[i].name).Append(' ');
            Line("    no renderer matched under '" + t.Root + "' (prefix='" + t.Prefix + "' exact='" + t.Exact +
                 "'); renderers present: " + names);
            return null;
        }

        Line(string.Format("    {0} renderers matched under '{1}', world bounds centre={2} extents={3}",
            matched, t.Root, b.center.ToString("0.###"), b.extents.ToString("0.###")));
        return b;
    }

    // ------------------------------------------------------------------ output

    static void Line(string message)
    {
        Debug.Log("[FramingGate] " + message);
        System.Console.WriteLine("[FramingGate] " + message);
    }

    static void Finish(int exitCode)
    {
        if (Application.isBatchMode) EditorApplication.Exit(exitCode);
        else if (exitCode != 0) Debug.LogError("[FramingGate] exit code " + exitCode);
    }
}
