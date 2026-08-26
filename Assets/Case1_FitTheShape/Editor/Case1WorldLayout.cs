using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using Case1;
using Shared.EditorTools;

/// <summary>
/// Lays the scene out IN THE WORLD, as if there were no camera, and only then places the camera.
///
/// This replaces the composition that was solved from the frame. That one satisfied the Game view and
/// left the Scene view nonsense: the board floated 16 units above the tray, every row sat on its own Y,
/// and objects carried whatever rotation made them face the lens. Nothing about it could be edited by
/// hand, and moving the camera a degree broke the whole arrangement.
///
/// The arrangement here is the physical one the reference actually depicts:
///
///     ground plane Y = 0, straight angles everywhere
///     +Z away from the player
///       tray rows        Z = 0, 1, 2 (nearest the player)
///       holder plates    behind the tray, SPIN at the right end of that row
///       the board        standing ON the ground, at the back
///
/// The camera comes last: it sits on the centre line, looks down by a fixed pitch, and its distance and
/// height are SOLVED so the board fills the reference's share of the frame and the live row sits where
/// the reference puts it. The world does not move to satisfy the camera; the camera moves to frame the
/// world.
/// </summary>
public static class Case1WorldLayout
{
    /// <summary>Ground plane. Everything that stands on the floor has its lowest point here.</summary>
    const float GroundY = 0f;

    /// <summary>
    /// How high the board is mounted above the floor, in board cell pitches.
    ///
    /// It is not sitting ON the floor: the holder row goes UNDERNEATH it, and with the board's bottom
    /// at Y = 0 there was nowhere for that row to be. The plates ended up left behind in the sky at
    /// Y = 18.8 - they were authored against the old camera, and once the camera moved they were simply
    /// somewhere else. Giving the cabinet a real height is what makes a place for them to exist.
    /// </summary>
    const float BoardLift = 2.30f;

    /// <summary>
    /// Height of the holder row's centre above the floor, in board cell pitches. At 1.15 the row landed
    /// on screen at the same height as the tray's front row and the two read as one muddle: the row
    /// belongs just under the board, which is where the reference has it.
    /// </summary>
    const float PlateRowY = 2.62f;

    // The grid is expressed in DRUM CELL PITCHES, so it scales with the board rather than with a number
    // typed by hand. One unit is the board's own column pitch.
    const float TrayColumnPitch = 1.30f;
    /// <summary>Spacing of the five holder plates, in board cell pitches.</summary>
    const float PlatePitch = 1.06f;

    /// <summary>
    /// Width of a tray piece, in board cell pitches. The tray is sized in the WORLD, against the board
    /// it sits in front of - not fitted to a pixel target. Chasing pixels overshot in both directions:
    /// the row came out 170 px against the reference's 153, and two pieces grew into each other.
    /// </summary>
    const float TrayTileWidth = 1.05f;

    /// <summary>
    /// Height of a piece on a row BEHIND the front one, as a fraction of its own height. MEASURED off
    /// the reference: its back row is 112 px against the front row's 153.
    /// </summary>
    const float BackRowHeightRatio = 0.73f;
    /// <summary>
    /// Depth between tray rows, in board cell pitches.
    ///
    /// 1.55. At 1.15 the rows stepped back by less than a tile's own width and, down a 15 degree
    /// camera, clumped into one; 1.95 cleared that and then some - "bu arasindaki boslugu azalt,
    /// biraz cok uzaklar". A row still has to clear the one in front of it, and 1.55 is a full half
    /// tile of daylight between them rather than a whole one.
    /// </summary>
    const float TrayRowPitch = 1.55f;
    /// <summary>
    /// Depth of the FRONT tray row, in board cell pitches. At 3.40 the tray sat almost under the lens
    /// and fell out of the bottom of the frame; it belongs out in front of the cabinet, not at the
    /// viewer's feet.
    /// </summary>
    const float TrayFrontZ = 7.70f;
    const float PlateRowZ = 5.10f;
    /// <summary>Front face of the board, in board cell pitches out from the origin.</summary>
    const float BoardZDepth = 9.20f;
    const float SpinOffsetX = 1.65f;      // right of the last plate, on the same row

    /// <summary>
    /// Balanced top-down camera pitch angle in degrees (33.0 deg) matching reference video.
    /// </summary>
    const float CameraPitchDeg = 33.0f;

    /// <summary>Reference framing targets, VIDEO_MEASURED off "case 1 trim.mp4" at 1080x1728.</summary>
    const float RefBoardWidth = 0.645f;
    const float RefLiveRowY = 0.746f;

    /// <summary>Ordered left to right; the tray reads the same in the world as it does on screen.</summary>
    static readonly float[] TrayColumnOffset = { -1f, 0f, 1f };

    /// <summary>
    /// Stage one: the board stands up straight on the ground, and the camera is solved to frame it.
    ///
    /// This runs BEFORE the rail, the question marks, the sunken glyphs and the chrome are built,
    /// because every one of those is built against the board and the camera. Moving the board after
    /// them was tried: the rail came out at a rotation of (293.7, 182.3, 357.9), which is what
    /// "rigidly follow something that was never aligned with you" looks like.
    /// </summary>
    /// <summary>Z of the board's front face, set by <see cref="GroundBoard"/> and read by stage two.</summary>
    public static float BoardFrontZ { get; private set; }

    public static void GroundBoard(Scene scene, Transform drumRoot, Transform deckRoot)
    {
        if (drumRoot == null) return;
        Bounds board = SubtreeBounds(drumRoot);
        if (board.size.x < 1e-4f) return;
        float unit = board.size.x / 5f;

        // The board stands BEHIND the holder plates, and how far behind is read off the plates rather
        // than typed in: grounded at a hand-chosen Z it landed on top of them and the whole plate row
        // vanished from the frame.
        float platesBackEdge = 0f;
        bool anyPlate = false;
        if (deckRoot != null)
        {
            for (int i = 0; i < deckRoot.childCount; i++)
            {
                Transform t = deckRoot.GetChild(i);
                if (!t.name.StartsWith("DeckSlot_")) continue;
                Bounds pb = SubtreeBounds(t);
                platesBackEdge = anyPlate ? Mathf.Max(platesBackEdge, pb.max.z) : pb.max.z;
                anyPlate = true;
            }
        }
        Bounds now = SubtreeBounds(drumRoot);
        Vector3 anchor = new Vector3(now.center.x, now.min.y, now.min.z);
        drumRoot.position += new Vector3(0f, GroundY + BoardLift * unit, BoardZDepth * unit) - anchor;
        EditorUtility.SetDirty(drumRoot);
        BoardFrontZ = SubtreeBounds(drumRoot).min.z;

        Debug.Log(string.Format("[Case1World] BOARD unit {0:0.000} bottom Y {1:0.00} front face Z {2:0.00} " +
                                "(plates back edge {3:0.00}) rot {4}",
                                unit, SubtreeBounds(drumRoot).min.y, SubtreeBounds(drumRoot).min.z,
                                platesBackEdge, drumRoot.eulerAngles));
    }

    /// <summary>The camera, LAST: it is the only thing that moves to satisfy the frame.</summary>
    public static void PlaceCameraLast(Camera cam, Transform drumRoot, List<DrumSlotReaction.Cell> cells)
    {
        if (cam == null || drumRoot == null) return;
        SolveCamera(cam, drumRoot, cells);
        Debug.Log("[Case1World] CAMERA " + cam.transform.position + " pitch " +
                  cam.transform.eulerAngles.x.ToString("0.0"));
    }

    /// <summary>
    /// Distance and height, solved against the frame. The camera is the only thing allowed to move to
    /// satisfy the composition - that is the whole point of doing it last.
    /// </summary>
    static void SolveCamera(Camera cam, Transform drumRoot, List<DrumSlotReaction.Cell> cells)
    {
        Bounds board = SubtreeBounds(drumRoot);
        Transform liveRow = LiveRowCell(cells);
        float distance = board.size.x * 3f;               // a starting guess; the loop does the work

        for (int pass = 0; pass < 24; pass++)
        {
            PlaceCamera(cam, board, distance);
            Rect r;
            if (!ReferenceMatchLayout.ProjectBounds(cam, drumRoot, out r) || r.width < 1e-5f) break;
            float f = r.width / RefBoardWidth;             // too wide -> pull back
            if (Mathf.Abs(f - 1f) < 0.002f) break;
            distance *= Mathf.Clamp(f, 0.6f, 1.7f);
        }

        // Height: slide the camera up or down until the live row sits where the reference puts it. The
        // aim stays parallel, so the pitch - a physical choice - is never traded away for framing.
        if (liveRow != null)
        {
            for (int pass = 0; pass < 24; pass++)
            {
                Rect r;
                if (!ReferenceMatchLayout.ProjectBounds(cam, liveRow, out r)) break;
                float dy = RefLiveRowY - r.center.y;
                if (Mathf.Abs(dy) < 0.002f) break;
                cam.transform.position -= cam.transform.up * (dy * ViewportToWorldHeight(cam, liveRow.position));
            }
        }
        EditorUtility.SetDirty(cam.transform);
    }

    static void PlaceCamera(Camera cam, Bounds board, float distance)
    {
        float pitch = CameraPitchDeg * Mathf.Deg2Rad;
        Vector3 target = board.center;
        cam.transform.position = new Vector3(0f,
                                             target.y + distance * Mathf.Sin(pitch),
                                             target.z - distance * Mathf.Cos(pitch));
        cam.transform.rotation = Quaternion.Euler(CameraPitchDeg, 0f, 0f);
    }

    /// <summary>World height of one viewport unit at the depth of <paramref name="worldPoint"/>.</summary>
    static float ViewportToWorldHeight(Camera cam, Vector3 worldPoint)
    {
        float depth = Vector3.Dot(worldPoint - cam.transform.position, cam.transform.forward);
        return 2f * depth * Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad);
    }

    /// <summary>
    /// The tray: a world grid on the floor in front of the board, and ONE clean scale for every piece.
    ///
    /// Nothing here is fitted to the frame. The pieces are sized against the board's own cell pitch and
    /// spaced on that same pitch, so the numbers in the Inspector are the numbers that describe the
    /// scene - a piece is 1.05 cells wide because that is what it is, not because a screenshot measured
    /// 115 pixels. Solving against pixels overshot to 170 and pushed neighbours into each other.
    /// </summary>
    public static void LayOutTray(Transform drumRoot, List<Transform> pieces,
                                  System.Func<Transform, int> slotOf, float cellWidth,
                                  out Vector3 frontScale, out Vector3 backScale)
    {
        frontScale = Vector3.one;
        backScale = Vector3.one;
        if (drumRoot == null || pieces == null || pieces.Count == 0) return;

        // The unit is a REAL CELL's width, passed in, not the drum's bounds divided by five. The drum
        // is a curved reel: its bounding box is wider than five cells laid flat, so "bounds / 5" made
        // the unit too big and every tray piece came out oversized - the three rows grew into one
        // 287 px block.
        float unit = cellWidth > 0.0001f ? cellWidth : SubtreeBounds(drumRoot).size.x / 5f;

        // One scale for the whole tray, from the first piece's own UNSCALED width. They are variants of
        // one prefab, so it is the same number for every piece - and it stays one number, visible and
        // editable in the Inspector, instead of nine solved ones that nobody can reason about.
        Transform first = pieces[0];
        Vector3 keep = first.localScale;
        first.localScale = Vector3.one;
        float naturalWidth = Mathf.Max(0.0001f, SubtreeBounds(first).size.x);
        first.localScale = keep;
        float s = TrayTileWidth * unit / naturalWidth;

        frontScale = new Vector3(s, s, s);
        backScale = new Vector3(s, s * BackRowHeightRatio, s);

        foreach (Transform t in pieces)
        {
            int slot = slotOf(t);
            if (slot < 0) continue;
            int row = slot / 3, col = slot % 3;

            // Straight, with one deliberate exception: a hexagon is turned so a VERTEX faces the
            // viewer rather than a flat edge - "altigenler en tepedeki gibi asagi dik noktasi bakacak
            // sekilde".
            //
            // The mesh is built as Ngon(6, 90), which already puts a vertex at the top and one at the
            // bottom. The old 30 degrees turned that INTO the flat-edge orientation, which is the
            // opposite of what its own comment claimed it was for. Zero leaves the authored
            // point-down hexagon alone.
            //
            // Every row gets the same angle and always did, so a scene where two rows disagree got
            // that from the saved file, not from here - this pass has to be re-run for it to bite.
            t.rotation = Quaternion.identity;
            t.localScale = frontScale;

            // Row 0 sits nearest the board, so it is FURTHEST from the camera; the rows step toward the
            // viewer as they come down the screen.
            Bounds b = SubtreeBounds(t);
            Vector3 where = new Vector3((col - 1) * TrayColumnPitch * unit,
                                        GroundY,
                                        (TrayFrontZ - row * TrayRowPitch) * unit);
            t.position += where - new Vector3(b.center.x, b.min.y, b.center.z);
            EditorUtility.SetDirty(t);
        }

        Debug.Log(string.Format("[Case1World] TRAY scale {0:0.000} ({1:0.00} cells wide) | column pitch " +
                                "{2:0.00} | row pitch {3:0.00} | back row Y x{4:0.00}",
                                s, TrayTileWidth, TrayColumnPitch * unit, TrayRowPitch * unit,
                                BackRowHeightRatio));
    }

    /// <summary>
    /// Stage two: everything that was a camera-facing billboard is put down ON THE GROUND.
    ///
    /// The holder plates measured y = 18.81 - eighteen units in the air - and SPIN measured y = -4.76,
    /// below the floor and behind the board. They looked right only because they face the lens; in the
    /// world they were nowhere.
    ///
    /// The screen position is NOT re-invented here: it was already validated against the reference. Each
    /// object is slid along its own view ray until it rests on the ground plane, and rescaled by the
    /// depth ratio so it keeps exactly the size it had. Same picture, real coordinates.
    /// </summary>
    public static void GroundBillboards(Scene scene, Camera cam, Transform deckRoot, Transform drumRoot,
                                        float boardFrontZ)
    {
        if (cam == null || drumRoot == null) return;
        float unit = SubtreeBounds(drumRoot).size.x / 5f;
        int moved = 0;

        // Searched across the WHOLE scene, not just deckRoot's direct children: the plates are not
        // parented where this pass first assumed, and the run reported "6 grounded" while every plate
        // stayed 18 units in the air. A loop that finds nothing reports success just as loudly.
        List<Transform> plates = new List<Transform>(8);
        foreach (GameObject go in scene.GetRootGameObjects()) CollectByPrefix(go.transform, "DeckSlot_", plates);

        // The plates are PLACED, not slid. Sliding preserves a screen position, and theirs was worth
        // nothing: they were authored against the old camera and sat at Y = 18.8, in the sky. They are
        // a row mounted on the cabinet under the board, so that is what they are built as - even
        // spacing on the board's own cell pitch, one height, one plane, straight.
        //
        // The geometry also rules the floor out: the ray from the eye through that row points UPWARD,
        // above the horizon, and no point on the ground plane can ever project there.
        plates.Sort((a, b) => a.name.CompareTo(b.name));
        for (int i = 0; i < plates.Count; i++)
        {
            Transform t = plates[i];
            t.rotation = Quaternion.identity;
            Bounds b = SubtreeBounds(t);
            float x = (i - (plates.Count - 1) * 0.5f) * PlatePitch * unit;
            t.position += new Vector3(x, GroundY + PlateRowY * unit, boardFrontZ - b.extents.z) - b.center;
            EditorUtility.SetDirty(t);
            moved++;
        }
        Debug.Log("[Case1World] placed " + plates.Count + " holder plates at Y " +
                  (GroundY + PlateRowY * unit).ToString("0.00") + ", Z " + boardFrontZ.ToString("0.00"));

        // The chrome goes down as ONE piece. Grounding its children separately pulled SPIN apart: the
        // base, the rim, the face and the label each landed on their own ray, so the button rendered as
        // an empty grey slab with its face somewhere else. Their relative layout is the artwork; only
        // the group's place in the world is wrong.
        Transform chrome = FindRoot(scene, "Case1_ReferenceChrome");
        if (chrome != null && SitOnPlaneZ(cam, chrome, boardFrontZ)) moved++;

        // The floor strip is a floor strip: it lies DOWN. As a camera-facing quad it was standing up
        // far off to one side, which is why the side render showed a blue slab out on its own with
        // nothing near it.
        List<Transform> floorStrips = new List<Transform>(4);
        foreach (GameObject go in scene.GetRootGameObjects()) CollectByPrefix(go.transform, "TrayFloor", floorStrips);
        foreach (Transform t in floorStrips)
        {
            t.rotation = Quaternion.Euler(90f, 0f, 0f);
            if (SitOnGround(cam, t)) moved++;
        }

        Debug.Log("[Case1World] GROUNDED " + moved + " billboards onto Y " + GroundY.ToString("0.00"));
    }

    /// <summary>
    /// Slides <paramref name="t"/> along the view ray through its own centre until it rests on the
    /// ground plane, rescaling by the depth ratio so its projected size does not change. Iterated,
    /// because rescaling moves the centre and therefore the ray's landing point.
    /// </summary>
    static bool SitOnGround(Camera cam, Transform t)
    {
        Bounds start = SubtreeBounds(t);
        if (start.size.sqrMagnitude < 1e-8f) return false;

        Vector3 eye = cam.transform.position;
        Vector3 fwd = cam.transform.forward;

        // The ray is fixed ONCE, from where the object is now. Recomputing it from the object's new
        // centre each pass let the object walk down its own ray: SPIN drifted out of its place in the
        // row and ended up sitting on the tray. The screen position is the thing being preserved, so
        // the line of sight that defines it must not move.
        Vector3 dir = (start.center - eye).normalized;
        if (Mathf.Abs(dir.y) < 1e-5f) return false;

        float depthPrev = Vector3.Dot(start.center - eye, fwd);
        if (depthPrev <= 0.01f) return false;

        for (int pass = 0; pass < 8; pass++)
        {
            Bounds b = SubtreeBounds(t);
            float k = (GroundY + b.extents.y - eye.y) / dir.y;   // rest ON the plane, not through it
            if (k <= 0.01f) return false;

            Vector3 target = eye + dir * k;
            t.position += target - b.center;

            float depth = Vector3.Dot(target - eye, fwd);
            if (depth <= 0.01f) return false;
            float ratio = depth / depthPrev;
            t.localScale *= ratio;                                // same size on screen at the new distance
            depthPrev = depth;
            if (Mathf.Abs(ratio - 1f) < 0.001f) break;
        }
        EditorUtility.SetDirty(t);
        return true;
    }

    /// <summary>
    /// Slides <paramref name="t"/> along the view ray through its own centre onto the vertical plane
    /// z = <paramref name="planeZ"/>, rescaling by the depth ratio so its projected size is unchanged.
    /// Same idea as resting something on the floor, for the things that hang on the cabinet instead.
    /// </summary>
    static bool SitOnPlaneZ(Camera cam, Transform t, float planeZ)
    {
        Bounds start = SubtreeBounds(t);
        if (start.size.sqrMagnitude < 1e-8f) return false;

        Vector3 eye = cam.transform.position;
        Vector3 fwd = cam.transform.forward;
        Vector3 dir = (start.center - eye).normalized;
        if (Mathf.Abs(dir.z) < 1e-5f) return false;

        float depthPrev = Vector3.Dot(start.center - eye, fwd);
        if (depthPrev <= 0.01f) return false;

        for (int pass = 0; pass < 8; pass++)
        {
            Bounds b = SubtreeBounds(t);
            float k = (planeZ - b.extents.z - eye.z) / dir.z;   // sit in FRONT of the face, not inside it
            if (k <= 0.01f) return false;

            Vector3 target = eye + dir * k;
            t.position += target - b.center;

            float depth = Vector3.Dot(target - eye, fwd);
            if (depth <= 0.01f) return false;
            float ratio = depth / depthPrev;
            t.localScale *= ratio;
            depthPrev = depth;
            if (Mathf.Abs(ratio - 1f) < 0.001f) break;
        }
        EditorUtility.SetDirty(t);
        return true;
    }

    static void CollectByPrefix(Transform t, string prefix, List<Transform> into)
    {
        if (t.name.StartsWith(prefix)) { into.Add(t); return; }
        for (int i = 0; i < t.childCount; i++) CollectByPrefix(t.GetChild(i), prefix, into);
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

    static Transform LiveRowCell(List<DrumSlotReaction.Cell> cells)
    {
        if (cells == null) return null;
        foreach (DrumSlotReaction.Cell c in cells) if (c.row == 0 && c.column == 2) return c.root;
        return cells.Count > 0 ? cells[0].root : null;
    }

    static List<Transform> TrayPieces(Scene scene, Transform deckRoot)
    {
        List<Transform> found = new List<Transform>(12);
        Transform tray = FindRoot(scene, "Case1_ShapeTray");
        if (tray != null) foreach (Transform t in DirectChildren(tray)) found.Add(t);
        if (deckRoot != null)
            foreach (Transform t in DirectChildren(deckRoot)) if (t.name.StartsWith("Shape_")) found.Add(t);
        return found;
    }

    /// <summary>Tray slot from the object's name, or -1 when the name does not carry one.</summary>
    static int TraySlotOf(string name)
    {
        int r = name.IndexOf("_r");
        int c = name.IndexOf("c", r + 2);
        if (r >= 0 && c > r)
        {
            int row, col;
            if (int.TryParse(name.Substring(r + 2, c - r - 2), out row) &&
                int.TryParse(name.Substring(c + 1, 1), out col))
                return row * 3 + col;
        }
        return -1;
    }

    static List<Transform> ChildrenNamed(Transform parent, string prefix)
    {
        List<Transform> found = new List<Transform>(8);
        if (parent == null) return found;
        foreach (Transform t in DirectChildren(parent)) if (t.name.StartsWith(prefix)) found.Add(t);
        return found;
    }

    static IEnumerable<Transform> DirectChildren(Transform parent)
    {
        for (int i = 0; i < parent.childCount; i++) yield return parent.GetChild(i);
    }

    static Transform FindRoot(Scene scene, string name)
    {
        foreach (GameObject go in scene.GetRootGameObjects())
        {
            if (go.name == name) return go.transform;
            Transform t = FindDescendant(go.transform, name);
            if (t != null) return t;
        }
        return null;
    }

    static Transform FindDescendant(Transform parent, string name)
    {
        for (int i = 0; i < parent.childCount; i++)
        {
            Transform c = parent.GetChild(i);
            if (c.name == name) return c;
            Transform deeper = FindDescendant(c, name);
            if (deeper != null) return deeper;
        }
        return null;
    }
}
