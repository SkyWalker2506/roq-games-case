using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

namespace Case2
{
    /// <summary>
    /// Batchmode proof that the board only ever moves because something dropped a block on it.
    /// For every block on the board it does the same two things: drop it on a hole it does not fit
    /// (or on bare board), which must send the block home and start nothing, and then drop it on the
    /// hole it does fit, which must run the shatter tail. Batchmode has no mouse, so the drops are
    /// driven through <see cref="BlockDragController.SimulateDrop"/>, which is the same release
    /// decision a real pointer release goes through.
    /// </summary>
    public sealed class Case2InputProbe : MonoBehaviour
    {
        /// <summary>Set once the probe has finished, pass or fail.</summary>
        public static bool Finished;

        /// <summary>Whether every assertion held.</summary>
        public static bool Passed;

        /// <summary>Human readable transcript, written to the gate log.</summary>
        public static string Transcript = "";

        readonly StringBuilder _log = new StringBuilder();
        Case2Director _director;
        int _failures;

        const int ShotWidth = 1080;
        const int ShotHeight = 1728;

        /// <summary>Content fingerprint of every shot taken this run, keyed by label.</summary>
        readonly Dictionary<string, string> _shotHashes = new Dictionary<string, string>();

        /// <summary>
        /// Renders the main camera to an offscreen target and reads it back.
        /// <paramref name="cullingMask"/> overrides the camera's own mask for the duration of the
        /// render; pass -1 to leave it alone. A mask of 0 renders nothing and is used as this
        /// probe's built-in positive control for the blank-frame detector.
        /// </summary>
        Texture2D Render(Camera cam, int cullingMask)
        {
            RenderTexture rt = RenderTexture.GetTemporary(ShotWidth, ShotHeight, 24, RenderTextureFormat.ARGB32);
            RenderTexture previousTarget = cam.targetTexture;
            int previousMask = cam.cullingMask;
            if (cullingMask != -1) cam.cullingMask = cullingMask;
            cam.targetTexture = rt;
            cam.Render();
            cam.targetTexture = previousTarget;
            cam.cullingMask = previousMask;

            RenderTexture previousActive = RenderTexture.active;
            RenderTexture.active = rt;
            Texture2D tex = new Texture2D(ShotWidth, ShotHeight, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0f, 0f, ShotWidth, ShotHeight), 0, 0);
            tex.Apply(false);
            RenderTexture.active = previousActive;
            RenderTexture.ReleaseTemporary(rt);
            return tex;
        }

        /// <summary>
        /// Whether a readback holds a rendered board rather than an empty camera.
        /// A `-nographics` run produces a single flat colour across the whole frame; so does a
        /// camera with nothing in its culling mask. Both are rejected here on the same two numbers:
        /// how many distinct colours the frame contains, and what share of it the most common
        /// colour occupies. `hash` is a cheap content fingerprint so two shots that are supposed to
        /// show different board states can be compared.
        /// </summary>
        static bool HasContent(Texture2D tex, out string stats, out string hash)
        {
            Color32[] px = tex.GetPixels32();
            Dictionary<int, int> counts = new Dictionary<int, int>();
            int step = 37;                                  // coprime with the row stride, so the walk covers the frame
            int sampled = 0;
            ulong acc = 1469598103934665603UL;              // FNV-1a over the sampled values
            for (int i = 0; i < px.Length; i += step)
            {
                int key = (px[i].r << 16) | (px[i].g << 8) | px[i].b;
                int n; counts.TryGetValue(key, out n); counts[key] = n + 1;
                sampled++;
                acc = (acc ^ (ulong)key) * 1099511628211UL;
            }
            int modal = 0;
            foreach (var kv in counts) if (kv.Value > modal) modal = kv.Value;
            float modalShare = sampled > 0 ? (float)modal / sampled : 1f;
            hash = acc.ToString("x16");
            stats = string.Format("distinctColours={0} modalShare={1:0.000} sampled={2} hash={3}",
                                  counts.Count, modalShare, sampled, hash);
            // Two arms, because there are two ways to get an empty frame.
            //
            // modalShare catches a genuine `-nographics` readback: one flat colour over the whole
            // frame, distinctColours=1, modalShare=1.000.
            //
            // distinctColours catches a camera that rendered nothing but still cleared. Measured on
            // this scene: an empty culling mask scores 57 (the clear gradient plus dithering, so
            // modalShare is only 0.140 and that arm does NOT catch it), while the thinnest real
            // board frame - board_99_all_fed, every hole sealed - scores 1161 and a full board
            // scores ~4900. The threshold sits at 400: 7x above the empty render, 3x below the
            // thinnest real one. It was 64, which cleared the empty render by 7 counts; do not
            // lower it back toward the noise without re-measuring both ends.
            return counts.Count >= 400 && modalShare < 0.90f;
        }

        /// <summary>
        /// Writes one full-resolution board frame next to the frame strips, under
        /// .plan-build/verify/BlockHole_UserPath/. The scripted capture strip never exercises this
        /// path - it runs RunSequence, which feeds exactly one hole and never reaches the delivery
        /// bookkeeping - so a frame taken here is the only pixel evidence of what the board looks
        /// like after a PLAYER has fed a hole.
        ///
        /// Every shot is asserted to contain a rendered board, and to differ from every shot
        /// already taken this run. Nine bit-identical blank frames once passed this gate green:
        /// `rc=0` and a clean transcript cannot tell a screenshot from an empty camera, so the
        /// screenshots now have to answer for themselves.
        /// </summary>
        void Shot(string label)
        {
            Camera cam = Camera.main;
            if (cam == null) { Check(false, "SHOT " + label + ": a main camera exists to render from"); return; }

            Texture2D tex = Render(cam, -1);
            string stats, hash;
            bool ok = HasContent(tex, out stats, out hash);

            string root = Directory.GetParent(Application.dataPath).FullName;
            string dir = Path.Combine(Path.Combine(Path.Combine(root, ".plan-build"), "verify"), "BlockHole_UserPath");
            Directory.CreateDirectory(dir);
            string path = Path.Combine(dir, label + ".png");
            File.WriteAllBytes(path, tex.EncodeToPNG());
            Destroy(tex);

            Line("SHOT " + label + " -> " + path + "  " + stats);
            Check(ok, "SHOT " + label + " is a rendered board, not an empty camera (" + stats + ")");

            foreach (var kv in _shotHashes)
            {
                if (kv.Value != hash) continue;
                Check(false, "SHOT " + label + " is bit-identical to '" + kv.Key + "' - two board states cannot render the same");
                break;
            }
            _shotHashes[label] = hash;
        }

        /// <summary>
        /// Positive control for the blank-frame detector, run in-band before any real shot.
        /// Renders the main camera with an empty culling mask - which is as close as this process
        /// can get to what `-nographics` produced - and requires the detector to reject it. A
        /// detector that has never produced a red is not evidence that the frames are good.
        /// </summary>
        void BlankDetectorControl()
        {
            Camera cam = Camera.main;
            if (cam == null) { Check(false, "CONTROL: a main camera exists to render from"); return; }

            Texture2D tex = Render(cam, 0);
            string stats, hash;
            bool ok = HasContent(tex, out stats, out hash);
            Destroy(tex);
            Line("CONTROL empty-culling-mask render  " + stats);
            Check(!ok, "CONTROL: the blank-frame detector rejects a deliberately empty render (" + stats + ")");
        }

        void Line(string s)
        {
            _log.AppendLine(s);
            Shared.Sequencing.SeqLog.Info("[Case2Gate] " + s);
        }

        void Check(bool ok, string what)
        {
            if (!ok) _failures++;
            Line((ok ? "PASS " : "FAIL ") + what);
        }

        IEnumerator Start()
        {
            Finished = false;
            Passed = false;

            _director = Object.FindFirstObjectByType<Case2Director>(FindObjectsInactive.Include);
            if (_director == null)
            {
                Line("FAIL no Case2Director in the scene");
                Done();
                yield break;
            }

            // Settle: the first playmode frames stall on shader compilation (lesson #10).
            float until = Time.realtimeSinceStartup + 2.0f;
            while (Time.realtimeSinceStartup < until) yield return null;

            Check(!_director.IsPlaying && !_director.UserTailRunning && !_director.Report.completed,
                  "scene idle on load: nothing plays by itself");

            // The screenshots are this gate's only pixel evidence, so prove the check that guards
            // them can fail before trusting the frames it passes.
            Check(SystemInfo.graphicsDeviceType != UnityEngine.Rendering.GraphicsDeviceType.Null,
                  "a real graphics device is present (graphicsDeviceType=" + SystemInfo.graphicsDeviceType + ")");
            BlankDetectorControl();

            // Baseline: every hole still open, nothing delivered.
            Shot("board_00_before");

            BlockDragController[] drags = _director.AllDrags();
            Line("blocks on the board = " + drags.Length);
            Check(drags.Length == 4, "all four reference blocks are draggable (found " + drags.Length + ")");

            HashSet<BlockShapeId> blockIds = new HashSet<BlockShapeId>();
            HashSet<HoleGlowHighlight> holes = new HashSet<HoleGlowHighlight>();
            for (int i = 0; i < drags.Length; i++)
            {
                blockIds.Add(drags[i].ShapeId);
                if (drags[i].holes == null) continue;
                for (int h = 0; h < drags[i].holes.Length; h++)
                    if (drags[i].holes[h] != null) holes.Add(drags[i].holes[h]);
            }
            Check(blockIds.SetEquals(new[] { BlockShapeId.L, BlockShapeId.Square, BlockShapeId.Two, BlockShapeId.Cross }),
                  "block IDs are exactly L, Square, Two, Cross");
            Check(holes.Count == 4, "exactly four hole identities are wired (found " + holes.Count + ")");

            yield return PickerHitsTheBlockThatIsDrawnThere(drags);

            yield return GrainCarvesTheBlockFaces(drags);

            BlockDragController tiltSubject = null;
            for (int i = 0; i < drags.Length; i++)
                if (drags[i].ShapeId == BlockShapeId.L) tiltSubject = drags[i];
            if (tiltSubject != null)
            {
                yield return DragTiltMatchesReference(tiltSubject, false);
                yield return DragTiltMatchesReference(tiltSubject, true);
            }
            else Check(false, "TILT: Block-L is on the board to run the tilt test on");

            HoleGlowHighlight[] holeList = new HoleGlowHighlight[holes.Count];
            holes.CopyTo(holeList);
            yield return OpenPitPixelTest(holeList);

            yield return TargetHoleLightsOnPickup(drags, holeList);

            yield return TileRiseControl(holeList[0]);

            yield return ReplayResetControl(holeList[0]);

            for (int i = 0; i < drags.Length; i++)
            {
                yield return ProbeBlock(drags[i], drags.Length - i - 1);
            }

            // Settle, then the frame the owner's complaint is actually about: all four holes fed,
            // board expected to read as plain checkerboard everywhere.
            float settle = Time.realtimeSinceStartup + 0.5f;
            while (Time.realtimeSinceStartup < settle) yield return null;
            Shot("board_99_all_fed");
            yield return FedHolesReadAsPlainBoard(holeList);

            Done();
        }

        /// <summary>
        /// Settles the claim that <see cref="HoleGlowHighlight.OpenPit"/> moves no pixels, by
        /// measurement rather than by reading the arithmetic. OpenPit raises _pitOpen from
        /// restingPitOpen (0.96) to 1.0, but the shader is handed
        /// _Open = Clamp01(_pitOpen / restingPitOpen), which is 1.0 at both - so the "the pit opens
        /// as the shards start falling" frame should be identical to the resting frame.
        /// <para>
        /// Run here, on a completely static board before any drop, precisely so the comparison is
        /// clean: at the real call site OpenPit fires on the same frame as the hitstop, the camera
        /// shake and the shatter, and any of those would mask the result.
        /// </para>
        /// </summary>
        IEnumerator OpenPitPixelTest(HoleGlowHighlight[] holeList)
        {
            yield return null;
            Shot("openpit_00_rest");

            StringBuilder before = new StringBuilder();
            for (int i = 0; i < holeList.Length; i++)
                before.Append(holeList[i].name).Append("=").Append(holeList[i].PitOpen.ToString("0.0000")).Append(" ");
            Line("OPENPIT rest   " + before.ToString().Trim());

            for (int i = 0; i < holeList.Length; i++) holeList[i].OpenPit(0.001f);

            for (int guard = 0; guard < 60; guard++)
            {
                bool allOpen = true;
                for (int i = 0; i < holeList.Length; i++) if (holeList[i].PitOpen < 0.9999f) allOpen = false;
                if (allOpen) break;
                yield return null;
            }
            yield return null;

            StringBuilder after = new StringBuilder();
            for (int i = 0; i < holeList.Length; i++)
                after.Append(holeList[i].name).Append("=").Append(holeList[i].PitOpen.ToString("0.0000")).Append(" ");
            Line("OPENPIT opened " + after.ToString().Trim());
            Shot("openpit_01_open");

            for (int i = 0; i < holeList.Length; i++) holeList[i].ResetInstant();
            yield return null;
            yield return null;
            Shot("openpit_02_restored");
            Line("OPENPIT restored to resting; board handed back to the drop probe unchanged");
        }

        /// <summary>
        /// The assertion the owner's complaint needed and this gate did not have: press where a block
        /// is DRAWN and you must get that block.
        /// <para>
        /// Where the block is drawn is established from PIXELS, not from geometry, so the test cannot
        /// agree with the picker by construction. For each block the camera is rendered twice - once
        /// with every block visible, once with that block hidden - and the pixels that changed are
        /// exactly the pixels that block owns on screen. Each sampled pixel is then unprojected onto
        /// the block's own top-face plane and handed to <see cref="BlockDragController.ResolvePick"/>,
        /// which must name that block and no other.
        /// </para>
        /// <para>
        /// Nothing here can be satisfied by moving a root onto a snap point. The existing drop
        /// assertions could: <c>SimulateDrop</c> moves the block to <c>SnapPoint</c> and the check
        /// then measures the distance from the block to <c>SnapPoint</c>, which is zero by
        /// construction whatever the art does.
        /// </para>
        /// <para>
        /// The same sweep is re-run through the bounding-box predicate this replaced, as an in-band
        /// negative control. That arm MUST fail, or this assertion is not observing what it names.
        /// </para>
        /// </summary>
        /// <summary>
        /// Ray through a pixel of a <see cref="Render"/> readback.
        /// NOT <c>Camera.ScreenPointToRay</c>: that unprojects against the camera's own pixel rect,
        /// which under batchmode is the game view's size and has nothing to do with the 1080x1728
        /// offscreen target these shots are taken on. Using it made every sample land off the board
        /// and the whole sweep read 1690/1690 wrong - a failure of the instrument, not of the picker.
        /// </summary>
        static Ray ShotPixelToRay(Camera cam, float x, float y)
        {
            float u = (x + 0.5f) / ShotWidth - 0.5f;      // -0.5 .. +0.5, +x right
            float v = (y + 0.5f) / ShotHeight - 0.5f;     // -0.5 .. +0.5, +y up (ReadPixels origin)
            float aspect = (float)ShotWidth / ShotHeight;
            if (!cam.orthographic) return cam.ScreenPointToRay(new Vector3(x, y, 0f));
            Vector3 origin = cam.transform.position
                           + cam.transform.right * (u * 2f * cam.orthographicSize * aspect)
                           + cam.transform.up * (v * 2f * cam.orthographicSize);
            return new Ray(origin, cam.transform.forward);
        }

        IEnumerator PickerHitsTheBlockThatIsDrawnThere(BlockDragController[] drags)
        {
            Camera cam = Camera.main;
            if (cam == null) { Check(false, "PICK: a main camera exists to render from"); yield break; }

            // The mask the pick region is built from has to still match the art it was measured off.
            for (int i = 0; i < drags.Length; i++)
            {
                int want = drags[i].ShapeId == BlockShapeId.Two ? 3
                         : drags[i].ShapeId == BlockShapeId.Cross ? 5 : 4;
                Check(drags[i].IsPickable,
                      "PICK " + drags[i].Block.name + " can answer a press at all (allowUserInput=" +
                      drags[i].allowUserInput + ")");
                Bounds ab = drags[i].ArtBounds;
                Line(string.Format("PICK bounds {0} x[{1:0.###},{2:0.###}] z[{3:0.###},{4:0.###}] topY={5:0.###}",
                     drags[i].Block.name, ab.min.x, ab.max.x, ab.min.z, ab.max.z, ab.max.y));
                Check(drags[i].ArtCellCount == want,
                      "PICK mask " + drags[i].Block.name + " covers " + drags[i].ArtCellCount +
                      " cells (measured art has " + want + ")");
            }

            Texture2D full = Render(cam, -1);
            Color32[] basePx = full.GetPixels32();
            Destroy(full);
            yield return null;

            int liveFailures = 0, liveSamples = 0, wallSamples = 0;
            int legacyFailures = 0, legacySamples = 0;

            for (int i = 0; i < drags.Length; i++)
            {
                BlockDragController d = drags[i];
                d.SetVisible(false);
                yield return null;
                Texture2D without = Render(cam, -1);
                Color32[] hidPx = without.GetPixels32();
                Destroy(without);
                d.SetVisible(true);
                yield return null;

                // Every pixel this block owns on screen, thinned to a workable sample.
                var pts = new List<Vector2>();
                for (int y = 0; y < ShotHeight; y += 12)
                    for (int x = 0; x < ShotWidth; x += 12)
                    {
                        int k = y * ShotWidth + x;
                        Color32 a = basePx[k], b = hidPx[k];
                        int diff = Mathf.Abs(a.r - b.r) + Mathf.Abs(a.g - b.g) + Mathf.Abs(a.b - b.b);
                        if (diff > 40) pts.Add(new Vector2(x, y));
                    }

                Line("PICK " + d.Block.name + " owns " + pts.Count + " sampled screen points");
                Check(pts.Count > 40, "PICK " + d.Block.name + " is actually drawn on screen (" + pts.Count + " sample points)");

                float planeY = Mathf.Max(d.HomePosition.y, d.ArtBounds.max.y);
                Plane top = new Plane(Vector3.up, new Vector3(0f, planeY, 0f));

                int firstBadX = -1, firstBadY = -1;
                string firstBadWho = "";
                for (int p = 0; p < pts.Count; p++)
                {
                    Ray ray = ShotPixelToRay(cam, pts[p].x, pts[p].y);
                    float enter;
                    if (!top.Raycast(ray, out enter)) continue;
                    Vector3 world = ray.GetPoint(enter);

                    // A block's owned pixels are its TOP FACE plus the side walls the 80-degree
                    // camera leaves visible. A side-wall pixel is not above the block's own cells, so
                    // unprojecting it onto the top-face plane lands past the footprint by design, not
                    // by fault - and resolving those would need a 3D silhouette test rather than a
                    // board-plane point. They are counted and reported, not asserted on.
                    if (!d.ArtContainsXZ(world)) { wallSamples++; continue; }

                    BlockDragController.LegacyBoundingBoxPickForControl = false;
                    BlockDragController got = BlockDragController.ResolvePick(world);
                    liveSamples++;
                    if (got != d)
                    {
                        liveFailures++;
                        if (firstBadX < 0)
                        {
                            firstBadX = (int)pts[p].x; firstBadY = (int)pts[p].y;
                            firstBadWho = got == null ? "<nothing>" : got.Block.name;
                        }
                    }

                    BlockDragController.LegacyBoundingBoxPickForControl = true;
                    BlockDragController old = BlockDragController.ResolvePick(world);
                    legacySamples++;
                    if (old != d) legacyFailures++;
                    BlockDragController.LegacyBoundingBoxPickForControl = false;
                }

                if (firstBadX >= 0)
                {
                    Line("PICK FIRST MISS on " + d.Block.name + " at screen (" + firstBadX + "," +
                         firstBadY + ") went to " + firstBadWho);
                }
            }

            Line("PICK side-wall samples skipped (visible walls project past their own cells): " + wallSamples);
            Check(liveSamples > 200, "PICK: enough top-face samples to mean anything (" + liveSamples + ")");
            Check(liveFailures == 0,
                  "PICK: every press on a block's drawn TOP FACE picks THAT block (" +
                  liveFailures + " of " + liveSamples + " samples went to the wrong block)");

            // The control. A predicate that has never produced a red is not evidence.
            Line("PICK CONTROL legacy bounding-box picker: " + legacyFailures + " of " + legacySamples + " samples wrong");
            Check(legacyFailures > 0,
                  "PICK CONTROL: the bounding-box picker this replaced FAILS the same sweep (" +
                  legacyFailures + " wrong) - the assertion above can go red");
        }


        /// <summary>
        /// Screen-space bounding box of the pixels a block owns, from a render-with / render-without
        /// diff. Same instrument the pick sweep uses; here it answers a different question - what
        /// SHAPE the block projects to - so the tilt can be checked in pixels and not only in the
        /// number the controller reports about itself.
        /// </summary>
        IEnumerator OwnedBox(Camera cam, BlockDragController d, Color32[] basePx, System.Action<int,int,int> done)
        {
            d.SetVisible(false);
            yield return null;
            Texture2D without = Render(cam, -1);
            Color32[] hid = without.GetPixels32();
            Destroy(without);
            d.SetVisible(true);

            int minX = int.MaxValue, maxX = int.MinValue, minY = int.MaxValue, maxY = int.MinValue, n = 0;
            for (int y = 0; y < ShotHeight; y += 2)
                for (int x = 0; x < ShotWidth; x += 2)
                {
                    int k = y * ShotWidth + x;
                    Color32 a = basePx[k], b = hid[k];
                    if (Mathf.Abs(a.r - b.r) + Mathf.Abs(a.g - b.g) + Mathf.Abs(a.b - b.b) <= 40) continue;
                    n++;
                    if (x < minX) minX = x; if (x > maxX) maxX = x;
                    if (y < minY) minY = y; if (y > maxY) maxY = y;
                }
            done(n, n > 0 ? maxX - minX : 0, n > 0 ? maxY - minY : 0);
        }

        /// <summary>
        /// The block faces have to carry the reference's painted-wood grain, and each block has to
        /// carry its OWN amount of it.
        ///
        /// <para>Instrument: the pixels a block owns come from the same render-with / render-without
        /// diff the pick sweep uses, so the mask cannot agree with the shader by construction. The
        /// mask is then eroded by 6 px, which drops the bevel, the white held-block outline and the
        /// grab dot without having to name any of them, and the metric is the standard deviation of
        /// the 4-neighbour Laplacian of the block's DOMINANT channel inside what is left. A
        /// Laplacian is blind to the flat colour and to any smooth shading ramp; only surface
        /// texture survives it.</para>
        ///
        /// <para>MEASURED on ref_0.00s.png (Block Hole.mp4 frame 0), same 1080x1728 frame size and
        /// the same board scale, with the same instrument:</para>
        /// <code>
        ///                 reference   ours before this change
        ///   red             6.76            2.82
        ///   green           2.90            2.78
        ///   cyan            4.45            2.78
        ///   purple          5.49            2.51
        ///   board tile      1.95            3.11
        /// </code>
        /// <para>Our four blocks sat BELOW our own board tiles: they carried no texture at all above
        /// the render's own noise. The thresholds below are not our output rounded down. Our render
        /// is noisier than the reference's (tile floor 3.11 against 1.95), so an absolute match on
        /// sigma would demand a GREEN block quieter than our own noise floor - which is unreachable
        /// and would mean no grain at all on green. What is matched instead is the grain's own
        /// contrast above each image's own board floor, in quadrature:
        /// g = sqrt(sigma_block^2 - sigma_tile^2), which for the reference is
        /// red 6.47, green 2.14, cyan 4.00, purple 5.13. Re-seated on our block floors that lands at
        /// red 7.06, green 3.51, cyan 4.87, purple 5.71, and the thresholds take 15% off those.</para>
        /// </summary>
        IEnumerator GrainCarvesTheBlockFaces(BlockDragController[] drags)
        {
            Camera cam = Camera.main;
            if (cam == null) { Check(false, "GRAIN: a main camera exists to render from"); yield break; }

            Texture2D full = Render(cam, -1);
            Color32[] basePx = full.GetPixels32();
            Destroy(full);
            yield return null;

            bool[] anyBlock = new bool[ShotWidth * ShotHeight];

            for (int i = 0; i < drags.Length; i++)
            {
                BlockDragController d = drags[i];
                d.SetVisible(false);
                yield return null;
                Texture2D without = Render(cam, -1);
                Color32[] hid = without.GetPixels32();
                Destroy(without);
                d.SetVisible(true);
                yield return null;

                bool[] mask = new bool[ShotWidth * ShotHeight];
                for (int k = 0; k < mask.Length; k++)
                {
                    Color32 a = basePx[k], b = hid[k];
                    bool owned = Mathf.Abs(a.r - b.r) + Mathf.Abs(a.g - b.g) + Mathf.Abs(a.b - b.b) > 40;
                    mask[k] = owned;
                    if (owned) anyBlock[k] = true;
                }
                Erode(mask, 6);

                int dom, n;
                float sigma = LaplacianSigmaDominant(basePx, mask, out dom, out n);
                float want = GrainFloorFor(d.ShapeId);
                Line(string.Format("GRAIN {0} sigma={1:0.00} on channel {2} over {3} px (floor {4:0.00})",
                     d.Block.name, sigma, "RGB"[dom], n, want));
                Check(n > 5000, "GRAIN " + d.Block.name + ": enough eroded face pixels to mean anything (" + n + ")");
                Check(sigma >= want,
                      string.Format("GRAIN {0} face carries the reference's grain contrast (sigma {1:0.00} >= {2:0.00})",
                                    d.Block.name, sigma, want));
            }

            // Reported, never asserted, and deliberately NOT called a floor: this is every pixel
            // no block owns, which is the tiles AND the board frame AND the page AND every hole
            // edge. It comes out around 9.5 because it is full of edges. It is here as a sanity
            // line - if it ever collapses toward zero the render is empty - not as the number the
            // block sigmas are compared against. That comparison lives in GrainFloorFor, whose
            // values come from the reference frame.
            for (int k = 0; k < anyBlock.Length; k++) anyBlock[k] = !anyBlock[k];
            Erode(anyBlock, 6);
            int fdom, fn;
            float floorSigma = LaplacianSigmaDominant(basePx, anyBlock, out fdom, out fn);
            Line(string.Format("GRAIN non-block sigma={0:0.00} on channel {1} over {2} px (edges included; a sanity line, not a floor)",
                 floorSigma, "RGB"[fdom], fn));
        }

        /// <summary>
        /// Picking a block up must light THAT block's hole, and only that one.
        ///
        /// <para>MEASURED off Block Hole.mp4 (65 fps). Rest frame differenced against a held frame,
        /// so the halo localises itself instead of being looked for at guessed coordinates:</para>
        /// <list type="bullet">
        /// <item>It is a step, not a ramp. The green hole reads +0.00, -0.09 at f194-f198 and
        /// +20.10 at f199 - one frame, 15 ms. The red hole does the same at f736.</item>
        /// <item>It does not pulse. f199-f223 hold +20.10 ... +19.81, a drift of 1.4% across
        /// 0.38 s. The scene's authored pulseHz 2.1 has a 0.48 s period and cannot be flat
        /// across that.</item>
        /// <item>It keys off PICKUP, not proximity: at f199 the green block has not moved from
        /// its own cell and is four cells from its hole.</item>
        /// <item>It carries information. Across the whole of the green block's hold, the RED
        /// hole's band reads +0.0, +0.0, -0.0 - the non-matching holes stay dark. That is the
        /// point of the effect; four holes glowing would be decoration with the cue removed.</item>
        /// <item>It goes out once the hole is fed and does not come back (red hole: +101 at the
        /// shatter, +5 by f907, flat after).</item>
        /// </list>
        ///
        /// <para>Instrument: each hole's own pixels come from hiding its cavity plate and
        /// differencing, NOT from projecting the geometry the shader uses - a screen rectangle
        /// derived from the same SDF would agree with the shader by construction. The band is
        /// then that mask dilated to 49 px (0.40 cells at 122 px per cell) minus the mask dilated
        /// by 2, with every block's own pixels dilated by 60 px removed, so a block lifting off
        /// the board cannot be mistaken for its hole lighting up.</para>
        /// </summary>
        IEnumerator TargetHoleLightsOnPickup(BlockDragController[] drags, HoleGlowHighlight[] holeList)
        {
            Camera cam = Camera.main;
            if (cam == null) { Check(false, "GLOW: a main camera exists to render from"); yield break; }

            Texture2D rest = Render(cam, -1);
            Color32[] restPx = rest.GetPixels32();
            Destroy(rest);
            yield return null;

            // Every block's own pixels, generously dilated: lifting a block moves its art and adds
            // an outline, a grab dot and a drop shadow, none of which is a hole lighting up.
            bool[] blockPixels = new bool[ShotWidth * ShotHeight];
            for (int i = 0; i < drags.Length; i++)
            {
                drags[i].SetVisible(false);
                yield return null;
                Texture2D without = Render(cam, -1);
                Color32[] hid = without.GetPixels32();
                Destroy(without);
                drags[i].SetVisible(true);
                yield return null;
                for (int k = 0; k < blockPixels.Length; k++)
                {
                    Color32 a = restPx[k], b = hid[k];
                    if (Mathf.Abs(a.r - b.r) + Mathf.Abs(a.g - b.g) + Mathf.Abs(a.b - b.b) > 40) blockPixels[k] = true;
                }
            }
            Dilate(blockPixels, 60);

            // Each hole's own pixels, and the band just outside them.
            bool[][] mouths = new bool[holeList.Length][];
            Vector3[] mouthColour = new Vector3[holeList.Length];
            for (int h = 0; h < holeList.Length; h++)
            {
                Renderer pit = holeList[h].PitRendererForProbe;
                if (pit == null) { Check(false, "GLOW " + holeList[h].name + ": has a cavity plate to locate it by"); yield break; }
                bool wasOn = pit.enabled;
                pit.enabled = false;
                yield return null;
                Texture2D without = Render(cam, -1);
                Color32[] hid = without.GetPixels32();
                Destroy(without);
                pit.enabled = wasOn;
                yield return null;

                bool[] mouth = new bool[ShotWidth * ShotHeight];
                double mr = 0, mg = 0, mb = 0; long mn = 0;
                for (int k = 0; k < mouth.Length; k++)
                {
                    Color32 a = restPx[k], b = hid[k];
                    if (Mathf.Abs(a.r - b.r) + Mathf.Abs(a.g - b.g) + Mathf.Abs(a.b - b.b) <= 40) continue;
                    mouth[k] = true;
                    mr += a.r; mg += a.g; mb += a.b; mn++;
                }
                mouths[h] = mouth;
                // The direction the halo pushes the board in is the hole's own NEON colour, not
                // its dark interior: the green opening's floor is (1,14,0) and its rim (24,151,2),
                // and the reference's halo is plainly the second. Measured the first way, the green
                // hole scored +11.3 while its green channel had moved +31.1.
                mouthColour[h] = new Vector3(holeList[h].neonColor.r * 255f,
                                             holeList[h].neonColor.g * 255f,
                                             holeList[h].neonColor.b * 255f);
                Line(string.Format("GLOW mouth {0}: {1} px, mean {2:0},{3:0},{4:0}, neon {5:0},{6:0},{7:0}",
                     holeList[h].name, mn, mn > 0 ? mr / mn : 0, mn > 0 ? mg / mn : 0, mn > 0 ? mb / mn : 0,
                     mouthColour[h].x, mouthColour[h].y, mouthColour[h].z));
            }

            HoleInteriorsMatchTheReference(restPx, holeList, mouths);

            bool[][] bands = new bool[holeList.Length][];
            for (int h = 0; h < holeList.Length; h++)
            {
                bool[] inner = (bool[])mouths[h].Clone();
                Dilate(inner, 2);
                bool[] outer = (bool[])mouths[h].Clone();
                Dilate(outer, 49);           // 0.40 cells at the measured 122 px per cell

                // Two holes can be edge-to-edge on this board - the green P's cell (6,j1) sits
                // directly on top of the cyan bar's (6,j2) - so a lit hole's halo lands inside its
                // NEIGHBOUR's band. Measured: holding the green block moved the cyan hole's band
                // by -8.6,-1.9,-11.5 before this exclusion, which reads as "the cyan hole lit up"
                // and is nothing of the kind. Every other hole's reach is cut out of this one.
                bool[] neighbours = new bool[ShotWidth * ShotHeight];
                for (int j = 0; j < holeList.Length; j++)
                {
                    if (j == h) continue;
                    bool[] other = (bool[])mouths[j].Clone();
                    Dilate(other, 55);
                    for (int k = 0; k < neighbours.Length; k++) if (other[k]) neighbours[k] = true;
                }

                int n = 0;
                for (int k = 0; k < outer.Length; k++)
                {
                    outer[k] = outer[k] && !inner[k] && !blockPixels[k] && !neighbours[k];
                    if (outer[k]) n++;
                }

                // Keep only the plain-board population. Three of the four openings touch the
                // board's lavender frame, and a halo painted over the frame swamps a mean taken
                // over both surfaces - the green hole read -21.9,-0.6,-49.2 that way, i.e. the
                // right effect with the wrong sign. The kept colour is found from the band's OWN
                // mode rather than named, so nothing here has to know what a board tile looks like.
                Color32 modal = ModalColour(restPx, outer);
                int kept = 0;
                for (int k = 0; k < outer.Length; k++)
                {
                    if (!outer[k]) continue;
                    Color32 c = restPx[k];
                    if (Mathf.Abs(c.r - modal.r) > 30 || Mathf.Abs(c.g - modal.g) > 30 || Mathf.Abs(c.b - modal.b) > 30)
                        outer[k] = false;
                    else kept++;
                }

                bands[h] = outer;
                Line(string.Format("GLOW band {0}: mouth {1} px, band {2} px, on plain board {3} px, mode {4},{5},{6}",
                     holeList[h].name, Count(mouths[h]), n, kept, modal.r, modal.g, modal.b));
                Check(kept > 3000, "GLOW " + holeList[h].name + ": its band survives every exclusion (" + kept + " px)");
            }

            BoardTileDidNotMove(restPx, bands);

            // ---- REACH. The band above asks "does the halo appear where it should". This asks the
            // question the owner's complaint is: does it appear where it should NOT.
            //
            // A halo whose reach is _GlowReach = 0.32 cells cannot touch a pixel further than that
            // from the opening. The opening here is the pixel set found above by hiding the cavity
            // plate at REST, so it is the shape actually drawn, not the shape the shader's arithmetic
            // believes in - the two disagreeing was the bug.
            //
            // 56 px, not 39. Dilate() grows by 4-neighbours, i.e. it is a MANHATTAN radius, and
            // 0.32 cells Euclidean at 122 px per cell is 0.32*sqrt(2) = 0.45 cells = 55 px of
            // Manhattan radius in the worst (45 degree) direction. Rounded up to 56 so a CORRECT
            // halo cannot reach past this mask even off a convex corner, which keeps every pixel
            // this assertion looks at genuinely out of reach.
            //
            // Bounded to 160 px (1.3 cells) around the mouth. Unbounded, the statistic is a mean
            // over the whole screen and any real leak is divided into nothing.
            bool[][] outOfReach = new bool[holeList.Length][];
            for (int h = 0; h < holeList.Length; h++)
            {
                bool[] reach = (bool[])mouths[h].Clone();
                Dilate(reach, 56);
                bool[] near = (bool[])mouths[h].Clone();
                Dilate(near, 160);

                bool[] neighbours = new bool[ShotWidth * ShotHeight];
                for (int j = 0; j < holeList.Length; j++)
                {
                    if (j == h) continue;
                    bool[] other = (bool[])mouths[j].Clone();
                    Dilate(other, 160);
                    for (int k = 0; k < neighbours.Length; k++) if (other[k]) neighbours[k] = true;
                }

                for (int k = 0; k < near.Length; k++)
                    near[k] = near[k] && !reach[k] && !blockPixels[k] && !neighbours[k];

                // Same plain-board filter the band uses, and for the same reason: three of the four
                // openings touch the lavender frame.
                Color32 modal = ModalColour(restPx, near);
                int kept = 0;
                for (int k = 0; k < near.Length; k++)
                {
                    if (!near[k]) continue;
                    Color32 c = restPx[k];
                    if (Mathf.Abs(c.r - modal.r) > 30 || Mathf.Abs(c.g - modal.g) > 30 || Mathf.Abs(c.b - modal.b) > 30)
                        near[k] = false;
                    else kept++;
                }
                outOfReach[h] = near;
                // The sample size is LOGGED and not compared against a floor, because there is no
                // measured floor to compare it against: this ring has never been counted on this
                // scene, and the band's own 3000 is a different set of pixels taken at a different
                // radius. Read the four numbers this line prints on the first run and turn them
                // into a floor then. The only thing asserted here is the one thing that needs no
                // measurement - a mean over an empty set is not a measurement.
                Line(string.Format("REACH ring {0}: {1} px of plain board beyond {2:0.00} cells of the opening, mode {3},{4},{5} (SAMPLE SIZE - no floor derived yet)",
                     holeList[h].name, kept, 56f / 122f, modal.r, modal.g, modal.b));
                Check(kept > 0, "REACH " + holeList[h].name + ": it has an out-of-reach ring at all (" + kept + " px)");
            }

            for (int i = 0; i < drags.Length; i++)
            {
                BlockDragController d = drags[i];
                yield return d.Pickup(0.02f);
                yield return null;
                yield return null;
                Texture2D litTex = Render(cam, -1);
                Color32[] litPx = litTex.GetPixels32();
                Destroy(litTex);

                for (int h = 0; h < holeList.Length; h++)
                {
                    bool matches = holeList[h].Matches(d.ShapeId);
                    float dr, dg, db;
                    BandDelta(restPx, litPx, bands[h], out dr, out dg, out db);
                    float toward = TowardHole(restPx, bands[h], mouthColour[h], dr, dg, db);
                    Line(string.Format("GLOW hold {0} -> hole {1} match={2} delta {3:+0.0;-0.0},{4:+0.0;-0.0},{5:+0.0;-0.0} toward-hole {6:+0.0;-0.0}",
                         d.Block.name, holeList[h].name, matches, dr, dg, db, toward));
                    if (matches)
                    {
                        Check(toward >= 12.0f,
                              string.Format("GLOW {0} lights its own hole {1} ({2:0.0} toward the hole's own colour, reference +20.1 to +22.3)",
                                            d.Block.name, holeList[h].name, toward));

                        // The reach assertion, on the same hold and the same instrument. Pixels
                        // further from the opening than the halo can reach must not move toward the
                        // hole's colour.
                        //
                        // The bound is 2.5, and it is not new here: it is the same "leaves it dark"
                        // tolerance this gate already applies per channel to a NON-matching hole's
                        // band, eleven lines below. Both ask the same question - did a set of board
                        // pixels that should be untouched stay untouched - so they are held to the
                        // same number rather than to a second one invented for this arm.
                        //
                        // PRE-REGISTERED CONTROL, aimed at this conclusion: "The cyan bar hole is a
                        // plain sdBox with no cuts, so its outer distance is already exact and its
                        // halo is already correct. If the bar hole's out-of-reach number is not
                        // already near zero on the unfixed build, then this statistic is not
                        // measuring what I claim and the cross's number proves nothing."
                        float orr, org, orb;
                        BandDelta(restPx, litPx, outOfReach[h], out orr, out org, out orb);
                        float outToward = TowardHole(restPx, outOfReach[h], mouthColour[h], orr, org, orb);
                        Line(string.Format("REACH hold {0} -> hole {1} out-of-reach delta {2:+0.0;-0.0},{3:+0.0;-0.0},{4:+0.0;-0.0} toward-hole {5:+0.0;-0.0}",
                             d.Block.name, holeList[h].name, orr, org, orb, outToward));
                        Check(Mathf.Abs(outToward) <= 2.5f,
                              string.Format("REACH {0}: the halo stays inside its own reach - board more than {1:0.00} cells from the opening does not move toward the hole ({2:0.0}, limit 2.5)",
                                            holeList[h].name, 56f / 122f, outToward));
                    }
                    else
                    {
                        Check(Mathf.Abs(dr) <= 2.5f && Mathf.Abs(dg) <= 2.5f && Mathf.Abs(db) <= 2.5f,
                              string.Format("GLOW {0} leaves the non-matching hole {1} dark ({2:0.0},{3:0.0},{4:0.0}) - the cue is which hole, not that a hole exists",
                                            d.Block.name, holeList[h].name, dr, dg, db));
                    }
                }

                d.ResetInstant();
                float settle = Time.realtimeSinceStartup + 0.45f;   // glowFadeOutSeconds is 0.15
                while (Time.realtimeSinceStartup < settle) yield return null;
                Texture2D backTex = Render(cam, -1);
                Color32[] backPx = backTex.GetPixels32();
                Destroy(backTex);

                for (int h = 0; h < holeList.Length; h++)
                {
                    if (!holeList[h].Matches(d.ShapeId)) continue;
                    float dr, dg, db;
                    BandDelta(restPx, backPx, bands[h], out dr, out dg, out db);
                    Check(Mathf.Abs(dr) <= 2.5f && Mathf.Abs(dg) <= 2.5f && Mathf.Abs(db) <= 2.5f,
                          string.Format("GLOW {0} released: hole {1} goes back dark ({2:0.0},{3:0.0},{4:0.0})",
                                        d.Block.name, holeList[h].name, dr, dg, db));
                }
                yield return null;
            }
        }

        /// <summary>
        /// How far the band moved TOWARD this hole's own colour, in code values.
        /// <para>Not "the delta on the hole's brightest channel": the cyan hole's brightest channel
        /// is blue, and the board it sits on is already blue, so a cyan halo over navy moves blue
        /// by +2.4 while moving red by -31.5. Measured that way the cyan hole looked dark while it
        /// was plainly lit. The discriminating direction is the one from the board the band
        /// actually sits on to the hole's own neon colour.</para>
        /// <para>The 12.0 floor is roughly 60% of the reference's own per-channel peak (+20.1 on
        /// the green hole, +22.3 on the red). It is a FLOOR, not the fidelity target: how closely
        /// the halo matches is settled on the capture strip with the same instrument that was run
        /// on the reference, because this band and that one are not the same set of pixels.</para>
        /// </summary>
        static float TowardHole(Color32[] rest, bool[] band, Vector3 holeNeon, float dr, float dg, float db)
        {
            double sr = 0, sg = 0, sb = 0; long n = 0;
            for (int k = 0; k < band.Length; k++)
            {
                if (!band[k]) continue;
                sr += rest[k].r; sg += rest[k].g; sb += rest[k].b; n++;
            }
            if (n == 0) return 0f;
            Vector3 axis = holeNeon - new Vector3((float)(sr / n), (float)(sg / n), (float)(sb / n));
            float len = axis.magnitude;
            if (len < 1e-3f) return 0f;
            return Vector3.Dot(new Vector3(dr, dg, db), axis / len);
        }

        static int Count(bool[] m) { int n = 0; for (int k = 0; k < m.Length; k++) if (m[k]) n++; return n; }

        /// <summary>Most common colour in a masked region, on a 16-per-channel grid.</summary>
        static Color32 ModalColour(Color32[] px, bool[] mask)
        {
            int[] bins = new int[16 * 16 * 16];
            for (int k = 0; k < mask.Length; k++)
            {
                if (!mask[k]) continue;
                bins[(px[k].r >> 4) * 256 + (px[k].g >> 4) * 16 + (px[k].b >> 4)]++;
            }
            int best = 0, bestN = -1;
            for (int i = 0; i < bins.Length; i++) if (bins[i] > bestN) { bestN = bins[i]; best = i; }
            double sr = 0, sg = 0, sb = 0; long n = 0;
            for (int k = 0; k < mask.Length; k++)
            {
                if (!mask[k]) continue;
                if ((px[k].r >> 4) * 256 + (px[k].g >> 4) * 16 + (px[k].b >> 4) != best) continue;
                sr += px[k].r; sg += px[k].g; sb += px[k].b; n++;
            }
            if (n == 0) return new Color32(0, 0, 0, 255);
            return new Color32((byte)(sr / n), (byte)(sg / n), (byte)(sb / n), 255);
        }

        /// <summary>
        /// The colour inside each opening, against the reference's own.
        ///
        /// <para>MEASURED on ref_0.00s.png. The floor is the MODAL colour of the opening eroded by
        /// 14 px - a mode, so it cannot land on a bevel or on the bright rim the way a mean can.
        /// The four values it returns on the reference are 45/0/0, 1/13/0, 0/41/67 and 24/10/58,
        /// which are the same four the HoleDepthGradient header records from a hand transect taken
        /// independently. That agreement is this metric's positive control.</para>
        ///
        /// <para>"Make the holes more vivid" was the wrong reading of the deviation. In CIE C*
        /// the four floors were red -17%, green +130%, cyan -41%, purple -20%: green was already
        /// too chromatic and a global lift would have pushed it further wrong. The floors are now
        /// tinted per hole and per channel - see HoleGlowHighlight.FloorTintFor.</para>
        ///
        /// <para>The opening is located by hiding its cavity plate and differencing, so the mask
        /// does not depend on the colour being measured. That matters: the first attempt used a
        /// hue classifier, and changing the green hole's tint moved its own mask from 44,866 px
        /// to 22,723 - the before and after were taken over different pixels.</para>
        /// </summary>
        void HoleInteriorsMatchTheReference(Color32[] restPx, HoleGlowHighlight[] holeList, bool[][] mouths)
        {
            for (int h = 0; h < holeList.Length; h++)
            {
                bool[] inside = (bool[])mouths[h].Clone();
                Erode(inside, 14);
                int n = Count(inside);
                Color32 want = ReferenceFloorFor(holeList[h].ResolvedShape);
                Color32 got = ModalColour(restPx, inside);
                int dr = got.r - want.r, dg = got.g - want.g, db = got.b - want.b;
                Line(string.Format("INTERIOR {0} shape={1} floor {2},{3},{4} reference {5},{6},{7} delta {8},{9},{10} over {11} px",
                     holeList[h].name, holeList[h].ResolvedShape, got.r, got.g, got.b, want.r, want.g, want.b, dr, dg, db, n));
                Check(n > 5000, "INTERIOR " + holeList[h].name + ": enough eroded interior to take a mode over (" + n + ")");
                Check(Mathf.Abs(dr) <= 8 && Mathf.Abs(dg) <= 8 && Mathf.Abs(db) <= 8,
                      string.Format("INTERIOR {0} floor matches the reference within 8 per channel ({1},{2},{3})",
                                    holeList[h].name, dr, dg, db));
            }

        }

        /// <summary>
        /// CONTROL for the cavity-tint change, registered on the unfixed build before the tints
        /// were touched, and aimed straight at this pass's own conclusion: the board tile must not
        /// move. Nothing here goes near BoardTile.shader or the two navy materials, and this is
        /// what says so in pixels rather than in prose.
        /// <para>Averaged, not moded. The board alternates two navy shades, so a mode flips
        /// between them depending on which tiles happen to be in the sample - the cyan hole's band
        /// modes to 53,62,120 while the cross's modes to 39,51,103, and neither is "the tile".</para>
        /// </summary>
        void BoardTileDidNotMove(Color32[] restPx, bool[][] bands)
        {
            bool[] plain = new bool[ShotWidth * ShotHeight];
            for (int h = 0; h < bands.Length; h++)
                for (int k = 0; k < plain.Length; k++) if (bands[h][k]) plain[k] = true;

            double sr = 0, sg = 0, sb = 0; long n = 0;
            for (int k = 0; k < plain.Length; k++)
            {
                if (!plain[k]) continue;
                sr += restPx[k].r; sg += restPx[k].g; sb += restPx[k].b; n++;
            }
            if (n == 0) { Check(false, "INTERIOR CONTROL: found board to measure"); return; }
            float r = (float)(sr / n), g = (float)(sg / n), b = (float)(sb / n);
            float dr = r - RegisteredTileR, dg = g - RegisteredTileG, db = b - RegisteredTileB;
            Line(string.Format("INTERIOR CONTROL board tile mean {0:0.00},{1:0.00},{2:0.00} over {3} px, registered {4:0.00},{5:0.00},{6:0.00}, delta {7:+0.00;-0.00},{8:+0.00;-0.00},{9:+0.00;-0.00}",
                 r, g, b, n, RegisteredTileR, RegisteredTileG, RegisteredTileB, dr, dg, db));
            Check(Mathf.Abs(dr) <= 1f && Mathf.Abs(dg) <= 1f && Mathf.Abs(db) <= 1f,
                  string.Format("INTERIOR CONTROL: the board tile did not move ({0:+0.00;-0.00},{1:+0.00;-0.00},{2:+0.00;-0.00}, limit 1.00)", dr, dg, db));
        }

        // Registered on the unfixed build, before any cavity tint changed. Do not re-derive these
        // from a run that already carries the change - that is what makes it a control.
        const float RegisteredTileR = 43.68f;
        const float RegisteredTileG = 55.06f;
        const float RegisteredTileB = 109.06f;

        /// <summary>The reference's own floor colour for each opening. See <see cref="HoleInteriorsMatchTheReference"/>.</summary>
        static Color32 ReferenceFloorFor(BlockShapeId id)
        {
            switch (id)
            {
                case BlockShapeId.L:      return new Color32(45, 0, 0, 255);
                case BlockShapeId.Square: return new Color32(1, 13, 0, 255);
                case BlockShapeId.Two:    return new Color32(0, 41, 67, 255);
                case BlockShapeId.Cross:  return new Color32(24, 10, 58, 255);
                default:                  return new Color32(0, 0, 0, 255);
            }
        }

        static void BandDelta(Color32[] a, Color32[] b, bool[] band, out float dr, out float dg, out float db)
        {
            double sr = 0, sg = 0, sb = 0; long n = 0;
            for (int k = 0; k < band.Length; k++)
            {
                if (!band[k]) continue;
                sr += b[k].r - a[k].r; sg += b[k].g - a[k].g; sb += b[k].b - a[k].b; n++;
            }
            if (n == 0) { dr = dg = db = 0f; return; }
            dr = (float)(sr / n); dg = (float)(sg / n); db = (float)(sb / n);
        }

        /// <summary>
        /// Per-block grain threshold. See <see cref="GrainCarvesTheBlockFaces"/> for the derivation;
        /// these are 85% of the reference's own grain contrast re-seated on our noise floor, NOT our
        /// own measurement rounded down.
        /// </summary>
        static float GrainFloorFor(BlockShapeId id)
        {
            switch (id)
            {
                case BlockShapeId.L:      return 6.00f;   // red,    target 7.06, reference 6.76
                case BlockShapeId.Square: return 2.98f;   // green,  target 3.51, reference 2.90
                case BlockShapeId.Two:    return 4.14f;   // cyan,   target 4.87, reference 4.45
                case BlockShapeId.Cross:  return 4.85f;   // purple, target 5.71, reference 5.49
                default:                  return 0f;
            }
        }

        /// <summary>Shrinks a screen mask by <paramref name="r"/> 4-neighbour passes.</summary>
        static void Erode(bool[] mask, int r)
        {
            bool[] src = new bool[mask.Length];
            for (int pass = 0; pass < r; pass++)
            {
                System.Array.Copy(mask, src, mask.Length);
                for (int y = 0; y < ShotHeight; y++)
                {
                    int row = y * ShotWidth;
                    for (int x = 0; x < ShotWidth; x++)
                    {
                        int k = row + x;
                        if (!src[k]) continue;
                        if (x == 0 || x == ShotWidth - 1 || y == 0 || y == ShotHeight - 1) { mask[k] = false; continue; }
                        if (!src[k - 1] || !src[k + 1] || !src[k - ShotWidth] || !src[k + ShotWidth]) mask[k] = false;
                    }
                }
            }
        }

        /// <summary>Grows a screen mask by <paramref name="r"/> 4-neighbour passes.</summary>
        static void Dilate(bool[] mask, int r)
        {
            bool[] src = new bool[mask.Length];
            for (int pass = 0; pass < r; pass++)
            {
                System.Array.Copy(mask, src, mask.Length);
                for (int y = 1; y < ShotHeight - 1; y++)
                {
                    int row = y * ShotWidth;
                    for (int x = 1; x < ShotWidth - 1; x++)
                    {
                        int k = row + x;
                        if (src[k]) continue;
                        if (src[k - 1] || src[k + 1] || src[k - ShotWidth] || src[k + ShotWidth]) mask[k] = true;
                    }
                }
            }
        }

        /// <summary>
        /// Standard deviation of the 4-neighbour Laplacian of the masked region's dominant channel.
        /// The dominant channel is chosen from the region's own mean, so a red block is read in red
        /// and a cyan one in blue; reading every block in one fixed channel would score a block by
        /// how much of that channel it happens to contain rather than by how textured it is.
        /// </summary>
        static float LaplacianSigmaDominant(Color32[] px, bool[] mask, out int dom, out int n)
        {
            double sr = 0, sg = 0, sb = 0; long count = 0;
            for (int k = 0; k < mask.Length; k++)
            {
                if (!mask[k]) continue;
                sr += px[k].r; sg += px[k].g; sb += px[k].b; count++;
            }
            dom = 0; n = (int)count;
            if (count == 0) return 0f;
            if (sg >= sr && sg >= sb) dom = 1;
            else if (sb >= sr && sb >= sg) dom = 2;

            double sum = 0, sum2 = 0; long m = 0;
            for (int y = 1; y < ShotHeight - 1; y++)
            {
                int row = y * ShotWidth;
                for (int x = 1; x < ShotWidth - 1; x++)
                {
                    int k = row + x;
                    if (!mask[k]) continue;
                    double v = 4.0 * Chan(px[k], dom)
                             - Chan(px[k - 1], dom) - Chan(px[k + 1], dom)
                             - Chan(px[k - ShotWidth], dom) - Chan(px[k + ShotWidth], dom);
                    sum += v; sum2 += v * v; m++;
                }
            }
            if (m < 2) return 0f;
            double mean = sum / m;
            return (float)System.Math.Sqrt(System.Math.Max(0.0, sum2 / m - mean * mean));
        }

        static int Chan(Color32 c, int i) { return i == 0 ? c.r : i == 1 ? c.g : c.b; }

        /// <summary>
        /// Holds a block and drives it at a steady z velocity for <paramref name="frames"/> frames,
        /// through the same ApplyTransform a real drag frame runs.
        /// </summary>
        IEnumerator DriveSideways(BlockDragController d, float vx, float seconds)
        {
            Vector3 p = d.Block.position;
            float x = p.x;
            float t0 = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup - t0 < seconds)
            {
                x += vx * Time.deltaTime;
                d.DriveHeldTo(x, p.z);
                yield return null;
            }
        }

        IEnumerator DriveAt(BlockDragController d, float vz, float seconds)
        {
            // Driven for a DURATION, not a frame count. The lean is an exponential with a 67 ms time
            // constant, so a fixed number of frames reaches a different fraction of it at every frame
            // rate: at the editor's batchmode rate 14 frames got 47% of the way there and the
            // assertion read as an under-implemented tilt rather than as an under-run test.
            Vector3 p = d.Block.position;
            float z = p.z;
            float t0 = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup - t0 < seconds)
            {
                z += vz * Time.deltaTime;
                d.DriveHeldTo(p.x, z);
                yield return null;
            }
        }

        /// <summary>
        /// The reference leans a held block into the drag. Measured off Block Hole.mp4 frames
        /// 340-413 at 30 fps by segmenting the white held-block outline: silhouette HEIGHT regresses
        /// on vertical drag velocity at r = -0.82 (2-frame lag) while WIDTH regresses on nothing
        /// (r = 0.23 vertical, 0.29 horizontal). So: pitch about world X, no yaw, no roll.
        /// <para>
        /// Three claims are checked, and the negative control turns the lean off and requires all
        /// three to fail. A tilt assertion that cannot tell a tilted block from a flat one is the
        /// same tautology as measuring the distance from a root to the snap point it was just moved to.
        /// </para>
        /// </summary>
        IEnumerator DragTiltMatchesReference(BlockDragController d, bool control)
        {
            string tag = control ? "TILT CONTROL " : "TILT ";
            Camera cam = Camera.main;
            if (cam == null) { Check(false, tag + "a main camera exists"); yield break; }

            float savedMax = d.dragTiltMaxDegrees;
            float savedRoll = d.dragRollDegPerUnitPerSecond;
            if (control) d.dragTiltMaxDegrees = 0f;      // the "we never shipped the lean" build

            Vector3 home = d.HomePosition;
            yield return d.Pickup(0.05f);

            // 5 u/s for 0.30 s is 4.5 time constants (99% of the steady lean) over 1.5 world units
            // of travel, which keeps Block-L inside the board from a start half that distance below
            // home - a silhouette clipped by the board frame would not be measurable.
            const float Speed = 5f;                      // world units/s
            const float Leg = 0.30f;                     // seconds per leg
            float expect = Mathf.Clamp(Speed * d.dragTiltDegPerUnitPerSecond, -savedMax, savedMax);

            // Start below home so both legs stay on the board.
            d.DriveHeldTo(home.x, home.z - Speed * Leg * 0.5f);
            yield return null;

            Texture2D full = Render(cam, -1);
            Color32[] basePx = full.GetPixels32();
            Destroy(full);

            // --- lean while travelling toward +z (up-screen)
            yield return DriveAt(d, +Speed, Leg);
            float tiltUp = d.DragTiltDegrees;
            int nU = 0, wU = 0, hU = 0;
            full = Render(cam, -1); basePx = full.GetPixels32(); Destroy(full);
            yield return OwnedBox(cam, d, basePx, (n, w, h) => { nU = n; wU = w; hU = h; });
            yield return null;

            // --- and toward -z
            yield return DriveAt(d, -Speed, Leg);
            float tiltDown = d.DragTiltDegrees;
            int nD = 0, wD = 0, hD = 0;
            full = Render(cam, -1); basePx = full.GetPixels32(); Destroy(full);
            yield return OwnedBox(cam, d, basePx, (n, w, h) => { nD = n; wD = w; hD = h; });

            Line(string.Format("{0}{1} at +{2:0.#}u/s tilt={3:0.00} deg box {4}x{5} ({6} px) | at -{2:0.#}u/s tilt={7:0.00} deg box {8}x{9} ({10} px) | expected +/-{11:0.00}",
                 tag, d.Block.name, Speed, tiltUp, wU, hU, nU, tiltDown, wD, hD, nD, expect));

            // 1. magnitude and sign, against the measured law
            bool magOk = Mathf.Abs(tiltUp - expect * 0.9f) < expect * 0.35f
                      && Mathf.Abs(tiltDown + expect * 0.9f) < expect * 0.35f;
            Check(control != magOk, tag + "lean matches the measured law: +" + expect.ToString("0.00") +
                  " deg per " + Speed + " u/s, got " + tiltUp.ToString("0.00") + " / " + tiltDown.ToString("0.00"));

            // 2. it is a PITCH: the silhouette gets taller travelling +z and its width does not move
            bool tallerOk = hU - hD >= 8;
            Check(control != tallerOk, tag + "silhouette is taller leaning +z than -z (" + hU + " vs " + hD + " px)");

            bool widthOk = Mathf.Abs(wU - wD) <= 6;
            Check(widthOk, tag + "silhouette WIDTH does not move - the axis is world X, no yaw, no roll (" +
                  wU + " vs " + wD + " px)");

            // 3. the owner's second axis. NOT measured - the reference shows no lateral lean, its
            // width regressing on x velocity at r = 0.29, i.e. nothing. This arm holds the shipped
            // behaviour to the owner's request rather than to the reference. Note the sign of the
            // claim flips here: a roll about world Z DOES move the silhouette width, which is
            // exactly what the vertical arm above requires to stay still.
            yield return DriveSideways(d, +Speed, Leg);
            float rollRight = d.DragRollDegrees;
            int nR = 0, wR = 0, hR = 0;
            full = Render(cam, -1); basePx = full.GetPixels32(); Destroy(full);
            yield return OwnedBox(cam, d, basePx, (n, w, h) => { nR = n; wR = w; hR = h; });
            yield return DriveSideways(d, -Speed, Leg);
            float rollLeft = d.DragRollDegrees;
            int nL = 0, wL = 0, hL = 0;
            full = Render(cam, -1); basePx = full.GetPixels32(); Destroy(full);
            yield return OwnedBox(cam, d, basePx, (n, w, h) => { nL = n; wL = w; hL = h; });

            float expectRoll = Mathf.Clamp(Speed * d.dragRollDegPerUnitPerSecond, -savedMax, savedMax);
            Line(string.Format("{0}{1} LATERAL at +{2:0.#}u/s roll={3:0.00} box {4}x{5} | at -{2:0.#}u/s roll={6:0.00} box {7}x{8} | expected -/+{9:0.00}",
                 tag, d.Block.name, Speed, rollRight, wR, hR, rollLeft, wL, hL, expectRoll));

            bool rollOk = Mathf.Abs(rollRight + expectRoll * 0.9f) < expectRoll * 0.35f
                       && Mathf.Abs(rollLeft - expectRoll * 0.9f) < expectRoll * 0.35f;
            Check(control != rollOk, tag + "leans sideways too: -/+" + expectRoll.ToString("0.00") +
                  " deg per " + Speed + " u/s, got " + rollRight.ToString("0.00") + " / " + rollLeft.ToString("0.00"));

            // WIDENS, not narrows. I predicted narrower and the gate caught me: rolling a box of
            // width w and height h by r projects to w*cos(r) + h*sin(r), so a 3-cell-wide, 1-unit-tall
            // block at 13.9 degrees goes to 3*0.971 + 1*0.240 = 3.15 cells - the height it gains
            // outweighs the width it loses. Predicted 386 px, measured 378.
            bool wider = wR > 366 + 6 && wL > 366 + 6;
            Check(control != wider, tag + "a sideways lean WIDENS the silhouette (" + wR + " / " + wL +
                  " px against 366 upright) - the axis the vertical arm requires to stay still");

            // 3. recovery: the reference's lean trails velocity by 67 ms, so it must unwind, not stick
            float peak = Mathf.Max(Mathf.Abs(d.DragTiltDegrees), Mathf.Abs(d.DragRollDegrees));
            float until = Time.realtimeSinceStartup + 0.25f;      // ~3.7 tau
            while (Time.realtimeSinceStartup < until) { d.DriveHeldTo(d.Block.position.x, d.Block.position.z); yield return null; }
            float settled = Mathf.Max(Mathf.Abs(d.DragTiltDegrees), Mathf.Abs(d.DragRollDegrees));
            bool recovered = peak > 0.5f && settled < peak * 0.1f;
            Check(control != recovered, tag + "lean unwinds once the drag stops: peak " +
                  peak.ToString("0.00") + " -> " + settled.ToString("0.000") + " deg after 250 ms (tau 67 ms)");

            d.dragTiltMaxDegrees = savedMax;
            d.dragRollDegPerUnitPerSecond = savedRoll;
            yield return d.ReturnHome(0.12f);
            float back = Vector3.Distance(d.Block.position, home);
            Check(back < 0.05f, tag + "block put back where the tilt test found it (offset " + back.ToString("0.000") + ")");
            yield return null;
        }


        /// <summary>Screen pixel a world point lands on in a <see cref="Render"/> readback.</summary>
        static Vector2 WorldToShotPixel(Camera cam, Vector3 p)
        {
            float aspect = (float)ShotWidth / ShotHeight;
            Vector3 rel = p - cam.transform.position;
            float u = Vector3.Dot(rel, cam.transform.right) / (2f * cam.orthographicSize * aspect) + 0.5f;
            float v = Vector3.Dot(rel, cam.transform.up) / (2f * cam.orthographicSize) + 0.5f;
            return new Vector2(u * ShotWidth - 0.5f, v * ShotHeight - 0.5f);
        }

        /// <summary>Mean RGB of a patch centred on a world point, on the board plane.</summary>
        static Vector3 PatchMean(Camera cam, Color32[] px, Vector3 world, int half)
        {
            Vector2 c = WorldToShotPixel(cam, world);
            int cx = Mathf.RoundToInt(c.x), cy = Mathf.RoundToInt(c.y);
            double r = 0, g = 0, b = 0; int n = 0;
            for (int y = cy - half; y <= cy + half; y++)
            {
                if (y < 0 || y >= ShotHeight) continue;
                for (int x = cx - half; x <= cx + half; x++)
                {
                    if (x < 0 || x >= ShotWidth) continue;
                    Color32 q = px[y * ShotWidth + x];
                    r += q.r; g += q.g; b += q.b; n++;
                }
            }
            return n == 0 ? Vector3.zero : new Vector3((float)(r / n), (float)(g / n), (float)(b / n));
        }

        /// <summary>
        /// The closure invariant, in pixels: once every hole has been fed the board has to read as
        /// plain board where the openings were. Each fed cell is compared against a REFERENCE cell of
        /// the same checkerboard parity that was never a hole and never had a block on it - (3,1) for
        /// even parity and (3,2) for odd, both confirmed free by the occupancy audit of this scene.
        /// <para>
        /// This is the check the tile rise had to not break. A rise that left a tile proud, or that
        /// finished on a recomputed rather than a cached position, would show here as a seam even
        /// though PitOpen is honestly 0.
        /// </para>
        /// </summary>
        IEnumerator FedHolesReadAsPlainBoard(HoleGlowHighlight[] holes)
        {
            Camera cam = Camera.main;
            if (cam == null) { Check(false, "CLOSURE: a main camera exists"); yield break; }
            yield return null;

            Texture2D tex = Render(cam, -1);
            Color32[] px = tex.GetPixels32();
            Destroy(tex);

            // Board grid: x cell boundaries on integers, z on half-integers, so a cell centre is
            // (col + 0.5, row). Free cells taken from the measured occupancy table.
            float boardY = holes.Length > 0 ? holes[0].transform.position.y : 0.03f;
            Vector3 evenRef = new Vector3(3f + 0.5f, boardY, 1f);   // (3,1), parity 0
            Vector3 oddRef = new Vector3(3f + 0.5f, boardY, 2f);    // (3,2), parity 1
            Vector3 evenC = PatchMean(cam, px, evenRef, 18);
            Vector3 oddC = PatchMean(cam, px, oddRef, 18);
            Line(string.Format("CLOSURE plain refs: (3,1)={0} (3,2)={1}", evenC, oddC));

            float worst = 0f; string worstWhere = "";
            for (int h = 0; h < holes.Length; h++)
            {
                int n = holes[h].CellTileCount;
                for (int c = 0; c < n; c++)
                {
                    Vector3 home = holes[h].CellTileHome(c);
                    int col = Mathf.FloorToInt(home.x);
                    int row = Mathf.RoundToInt(home.z);
                    Vector3 want = ((col + row) & 1) == 0 ? evenC : oddC;
                    Vector3 got = PatchMean(cam, px, new Vector3(col + 0.5f, boardY, row), 18);
                    float d = Mathf.Abs(got.x - want.x) + Mathf.Abs(got.y - want.y) + Mathf.Abs(got.z - want.z);
                    if (d > worst) { worst = d; worstWhere = holes[h].name + " cell(" + col + "," + row + ") " + got + " vs " + want; }
                }
            }
            Line("CLOSURE worst fed cell: " + worstWhere);
            Check(worst <= 11f,
                  "CLOSURE: every fed cell reads as plain board (worst dRGB " + worst.ToString("0.0") +
                  ", limit 11)");
        }


        /// <summary>
        /// In-band control for the tile-rise assertions. Runs the SAME RiseTiles path twice on an
        /// untouched hole, changing exactly one number: first at the measured height, which must
        /// rise, then at zero - the build we shipped before this - which must not. An assertion that
        /// has only ever been seen passing is not evidence that it observes anything.
        /// <para>
        /// Both arms end by requiring every tile back on its exact cached position, so the control
        /// cannot leave the board disturbed for the drop probes that follow.
        /// </para>
        /// </summary>
        IEnumerator TileRiseControl(HoleGlowHighlight h)
        {
            float saved = h.tileRiseHeight;
            float savedDepth = h.tileRiseDepth;
            int n = h.CellTileCount;
            Check(n > 0, "RISE CONTROL " + h.name + " has board tiles to pop (" + n + ")");
            if (n == 0) yield break;

            for (int arm = 0; arm < 2; arm++)
            {
                h.tileRiseHeight = arm == 0 ? saved : 0f;
                h.tileRiseDepth = arm == 0 ? savedDepth : 0f;
                float peak = 0f, low = 0f;
                h.RiseTiles();
                float until = Time.realtimeSinceStartup + (h.tileRiseStagger + h.tileRiseDuration + 0.35f);
                while (Time.realtimeSinceStartup < until)
                {
                    peak = Mathf.Max(peak, h.TileRisePeak);
                    low = Mathf.Max(low, h.TileRiseDeepest);
                    yield return null;
                }
                bool rose = peak > saved * 0.6f && low > savedDepth * 0.8f;
                if (arm == 0)
                    Check(rose, "RISE CONTROL live arm: tiles climb from " + low.ToString("0.00") +
                          " u below to " + peak.ToString("0.00") + " u above");
                else
                    Check(!rose, "RISE CONTROL zero arm: with the arc flattened the same check FAILS " +
                          "(deep " + low.ToString("0.00") + ", peak " + peak.ToString("0.00") +
                          ") - the rise assertion can go red");

                float worst = 0f;
                for (int c = 0; c < n; c++) worst = Mathf.Max(worst, h.CellTileOffset(c));
                Check(worst < 0.001f, "RISE CONTROL arm " + arm + " left the board undisturbed (worst " +
                      worst.ToString("0.0000") + " u)");
            }
            h.tileRiseHeight = saved;
            h.tileRiseDepth = savedDepth;
        }

        /// <summary>
        /// The replay invariant, with its own in-band control.
        ///
        /// <para>
        /// FrameStripCapture restarts the sequence on the FIRST update where the measure pass
        /// reports itself finished, and that instant falls inside the tile rise's own window: the
        /// rise is started at the sink-close beat and runs for up to tileRiseStagger +
        /// tileRiseDuration = 0.27 + 0.21 s after it. The rise runs as coroutines on the HOLE, so
        /// Case2Director.ResetState - whose StopAllCoroutines() is called on the DIRECTOR - cannot
        /// reach them, and ResetInstant() did not move the tiles. They kept climbing into the next
        /// run.
        /// </para>
        /// <para>
        /// A climbing tile is a board tile standing up to tileRiseHeight = 2.1 world units above a
        /// pit plate that sits at pitHeight = 0.034, so it draws OVER the cavity. Measured on the
        /// unfixed build at the harness's own replay instant, purple cross cell offsets in world
        /// units: (1.5,3)=0.324  (0.5,2)=2.099  (1.5,2)=0.744  (2.5,2)=5.100  (1.5,1)=5.100 - the
        /// 5.100 pair is tileRiseDepth, i.e. still waiting BELOW the plate and correctly invisible.
        /// In the dense strip that showed up as frame_00 reading 48/60/116 on the cross's top arm
        /// and 49/56/114 on its left arm: plain board, inside a hole.
        /// </para>
        /// <para>
        /// Invariant asserted here, verbatim: <c>after the replay reset path runs, every cell tile
        /// of the hole is exactly on its cached home on that same frame</c>.
        /// </para>
        /// <para>
        /// Control, verbatim: <c>the same measurement is taken twice on the same hole in the same
        /// rise, changing exactly one thing - whether the reset is called. The arm that calls it
        /// must read home; the arm that does not must read a displacement. If the second arm does
        /// not go red the instrument cannot see a displaced tile and the first arm proves
        /// nothing.</c>
        /// </para>
        /// </summary>
        IEnumerator ReplayResetControl(HoleGlowHighlight h)
        {
            int n = h.CellTileCount;
            Check(n > 0, "REPLAY RESET " + h.name + " has board tiles to pop (" + n + ")");
            if (n == 0) yield break;

            for (int arm = 0; arm < 2; arm++)
            {
                h.RiseTiles();
                yield return null;
                yield return null;

                if (arm == 0) h.ResetInstant();   // the replay path, exactly as ResetState calls it
                yield return null;

                float worst = 0f; string where = "";
                for (int c = 0; c < n; c++)
                {
                    float off = h.CellTileOffset(c);
                    if (off > worst)
                    {
                        worst = off;
                        Vector3 home = h.CellTileHome(c);
                        where = "cell(" + home.x.ToString("0.#") + "," + home.z.ToString("0.#") + ")";
                    }
                }

                if (arm == 0)
                    Check(worst < 0.001f,
                          "REPLAY RESET live arm: ResetInstant() during a rise leaves every cell tile " +
                          "home (worst " + worst.ToString("0.000") + " u at " + where + ", limit 0.001)");
                else
                    Check(worst > 0.05f,
                          "REPLAY RESET null arm: with the reset skipped the SAME measurement sees the " +
                          "displacement (worst " + worst.ToString("0.000") + " u at " + where +
                          ") - the live assertion can go red");

                // Never hand a disturbed board to the probes that follow: wait the whole arc out
                // and require the tiles back on their exact cached positions either way.
                float until = Time.realtimeSinceStartup + (h.tileRiseStagger + h.tileRiseDuration + 0.35f);
                while (Time.realtimeSinceStartup < until) yield return null;
                float rest = 0f;
                for (int c = 0; c < n; c++) rest = Mathf.Max(rest, h.CellTileOffset(c));
                Check(rest < 0.001f, "REPLAY RESET arm " + arm + " left the board undisturbed (worst " +
                      rest.ToString("0.0000") + " u)");
            }
        }

        IEnumerator ProbeBlock(BlockDragController d, int expectedRemaining, bool burstControl = false)
        {
            string blockName = d.Block != null ? d.Block.name : "<null>";
            Line("---- block " + blockName + " shape=" + d.ShapeKey);

            HoleGlowHighlight right = null;
            HoleGlowHighlight wrong = null;
            for (int i = 0; i < d.holes.Length; i++)
            {
                HoleGlowHighlight h = d.holes[i];
                if (h == null) continue;
                if (h.Matches(d.ShapeId)) { if (right == null) right = h; }
                else if (wrong == null) wrong = h;
            }

            if (right == null)
            {
                Check(false, blockName + " has a matching hole");
                yield break;
            }

            // ---------------------------------------------------------- wrong drop
            Vector3 home = d.HomePosition;
            if (wrong != null)
            {
                yield return d.SimulateDrop(wrong.SnapPoint, 0.06f, 0.16f);
                Check(!d.LastDropMatched, blockName + " released over " + wrong.name + " is refused");
            Check(!_director.IsPlaying && !_director.UserTailRunning,
                      blockName + " wrong drop started NO sequence");

                float until = Time.realtimeSinceStartup + 0.6f;
                while (Time.realtimeSinceStartup < until) yield return null;

                float back = Vector3.Distance(d.Block.position, home);
                Check(back < 0.05f, blockName + " returned home after the wrong drop (offset " + back.ToString("0.000") + ")");
                Check(!d.Consumed, blockName + " still on the board after the wrong drop");
                Check(!_director.Report.completed, "no report written by a wrong drop");
            }

            // ---------------------------------------------------------- right drop
            // The burst has to leave the opening. Measured off the reference's purple cross shatter,
            // frames 44-58 at 30 fps: the fragment cloud reaches 74 px beyond the opening edge, and
            // the cross opening's half-extent is 1.5 cells, so shards travel 1.5 + 74/122.55 = 2.10
            // world units from the hole centre. Ours reached 0.02 cells past the opening and froze.
            BlockShatterSink sink = Object.FindFirstObjectByType<BlockShatterSink>(FindObjectsInactive.Include);
            if (sink != null) sink.ResetPeakShardSpread();
            float sinkFunnelSaved = sink != null ? sink.funnelRate : 0f;

            // Named here on purpose. One run (frame-count tilt drive, which walked Block-L 2.8 world
            // units off the board) produced 16 downstream failures in which the director refused
            // every tail - "correct drop STARTED the shatter sequence" red for all four blocks, with
            // nothing saying why. HandleUserDrop returns silently when the director is already busy,
            // so a stuck latch reads as four broken blocks. It has not reproduced in three runs since
            // and I have not isolated it, so the gate now says which of the two it was.
            Check(!_director.IsPlaying && !_director.UserTailRunning,
                  blockName + ": director idle before this drop (isPlaying=" + _director.IsPlaying +
                  " tail=" + _director.UserTailRunning + ")");
            yield return d.SimulateDrop(right.SnapPoint, 0.06f, 0.16f);
            Check(d.LastDropMatched, blockName + " released over " + right.name + " is accepted");

            float wait = Time.realtimeSinceStartup + 0.5f;
            bool started = false;
            while (Time.realtimeSinceStartup < wait)
            {
                if (_director.UserTailRunning) { started = true; break; }
                yield return null;
            }
            Check(started, blockName + " correct drop STARTED the shatter sequence");

            // One frame of the block RESTING in the opening, before the break replaces it with
            // shards. Every other still in this run is an after-the-fact board state, so nothing
            // here used to show how a landed block actually sits over the hole it was dropped into
            // - the only question a shape or alignment claim can be settled by. The snap runs 0.06 s
            // and the anticipation hold 0.15 s, so a shot taken ~0.10 s in lands inside the hold.
            if (started)
            {
                float restAt = Time.realtimeSinceStartup + 0.10f;
                while (Time.realtimeSinceStartup < restAt && _director.UserTailRunning) yield return null;
                if (_director.UserTailRunning && !d.Consumed)
                {
                    Vector3 p = d.Block.position;
                    Line(string.Format("REST {0} block.position=({1:0.###},{2:0.###}) snapPoint=({3:0.###},{4:0.###})",
                         blockName, p.x, p.z, right.SnapPoint.x, right.SnapPoint.z));
                    Shot("rest_" + blockName.Replace("Block_Block-", ""));
                }
            }

            // The board coming back is a MOTION in the reference: each fed cell's tile pops up out
            // of the sealing cavity and settles flush. Measured off the purple cross's hole,
            // frames 62-88 at 30 fps - peaks 1.79/2.11/2.30/2.30 world units, mean 2.13.
            int cells = right.CellTileCount;
            float peakRise = 0f, deepest = 0f;
            float deadline = Time.realtimeSinceStartup + 8.0f;
            while (_director.UserTailRunning && Time.realtimeSinceStartup < deadline)
            {
                peakRise = Mathf.Max(peakRise, right.TileRisePeak);
                deepest = Mathf.Max(deepest, right.TileRiseDeepest);
                yield return null;
            }

            // L is 4, not 3. The red opening's fourth cell is the one the cyan bar stands on; it
            // was missed by an occupancy audit that counted visible red pixels on frame 0 and
            // re-measured at f520 of Block Hole.mp4, with the bar dragged away.
            int wantCells = d.ShapeId == BlockShapeId.Two ? 3
                          : d.ShapeId == BlockShapeId.Cross ? 5
                          : d.ShapeId == BlockShapeId.Square ? 5 : 4;
            Check(cells == wantCells,
                  "RISE " + right.name + " pops one tile per cell of its opening (" + cells +
                  ", opening has " + wantCells + ")");

            // The tile comes up FROM somewhere. Measured -5.08 u on cell (2,2), held for three
            // frames before it moves, and -5.17 u on cell (1,1). The first version of this beat
            // started flush and only had the overshoot, which is the half the owner noticed missing.
            bool deep = deepest > right.tileRiseDepth * 0.8f;
            Check(right.tileRiseDepth <= 0f ? !deep : deep,
                  "RISE " + right.name + " tiles start BELOW the board: deepest " +
                  deepest.ToString("0.00") + " u against " + right.tileRiseDepth.ToString("0.00") + " u");

            bool rose = peakRise > right.tileRiseHeight * 0.6f;
            Check(right.tileRiseHeight <= 0f ? !rose : rose,
                  "RISE " + right.name + " tiles reach the measured height: peak " +
                  peakRise.ToString("0.00") + " u against " + right.tileRiseHeight.ToString("0.00") +
                  " u (2.1 u = 45 screen px at 122.55*cos(80) = 21.28 px per world unit)");

            // The closure invariant. A tile left a hair proud reads as a seam in the board and no
            // pit-open check can see it, because the pit is genuinely shut.
            //
            // Waited for, not sampled at the moment the tail happens to end. The tiles are staggered
            // across stagger + duration = 0.40 s and the director's close beat is shorter than that,
            // so the last tile is still in the air when UserTailRunning goes false: sampling there
            // measured 0.0044 u and read as a settle bug rather than as a test that looked too early.
            // The bound is the assertion - a rise that never finished would sit here until it fired.
            float settleBy = Time.realtimeSinceStartup + right.tileRiseStagger + right.tileRiseDuration + 0.5f;
            while (right.TileRisePeak > 0.0001f && Time.realtimeSinceStartup < settleBy) yield return null;
            Check(Time.realtimeSinceStartup < settleBy,
                  "RISE " + right.name + " finished inside stagger + duration + 0.5 s");

            if (sink != null)
            {
                float spread = sink.PeakShardSpread;
                sink.funnelRate = sinkFunnelSaved;
                float half = right.OpeningHalfExtent;
                float past = spread - half;
                Line("BURST " + right.name +
                     " peak shard spread " + spread.ToString("0.00") + " u, opening half-extent " +
                     half.ToString("0.00") + " u, so " + past.ToString("0.00") + " u past the edge " +
                     "(reference: 0.60 u)");
                // Asserted on the CROSS only. That is the one shape whose burst was measured in the
                // reference, and OpeningHalfExtent takes the widest side, which is meaningless for
                // the 1x3 bar - its shards clear a 0.5-cell half-width, not 1.5. Every hole's number
                // is logged; only the one with a reference behind it is a gate.
                if (d.ShapeId == BlockShapeId.Cross)
                    Check(past >= 0.40f,
                          "BURST " + right.name + " clears the opening (" + past.ToString("0.00") +
                          " u past the edge, reference 0.60, floor 0.40)");
            }

            float worstOffset = 0f;
            for (int c = 0; c < cells; c++) worstOffset = Mathf.Max(worstOffset, right.CellTileOffset(c));
            Check(worstOffset < 0.001f,
                  "RISE " + right.name + " every tile settled EXACTLY back (worst offset " +
                  worstOffset.ToString("0.0000") + " u)");
            Check(!_director.UserTailRunning, blockName + " sequence ran to completion");
            Check(d.Consumed, blockName + " was delivered and left the board");

            BlockDragController[] all = _director.AllDrags();
            int left = 0;
            for (int i = 0; i < all.Length; i++) if (!all[i].Consumed) left++;
            Check(left == expectedRemaining,
                  "after " + blockName + ", " + left + " block(s) still draggable (expected " + expectedRemaining + ")");

            // The complaint this gate now covers: "after it swallows a piece, squares should appear
            // where the gaps are". A fed hole has to STAY shut. This used to read 0.96 - the tail
            // sealed the cavity and then reopened it - and nothing in the suite noticed, because
            // every other assertion here is about blocks, not about the board.
            Check(right.PitOpen < 0.001f,
                  right.name + " stays sealed after being fed (PitOpen " + right.PitOpen.ToString("0.000") + ")");
            Shot("board_" + (4 - expectedRemaining).ToString("00") + "_after_" + d.ShapeKey);
        }

        void Done()
        {
            Passed = _failures == 0;
            _log.AppendLine("INPUT_GATE " + (Passed ? "GREEN" : "RED") + " failures=" + _failures);
            Transcript = _log.ToString();
            Shared.Sequencing.SeqLog.Info("[Case2Gate] INPUT_GATE " + (Passed ? "GREEN" : "RED") + " failures=" + _failures);
            Finished = true;
        }
    }
}
