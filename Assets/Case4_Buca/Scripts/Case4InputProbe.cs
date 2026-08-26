using System.Collections;
using System.Text;
using UnityEngine;

namespace Case4
{
    /// <summary>
    /// Batchmode proof for Case 4. It asserts two separate things at once, because both are cheap once
    /// play mode is up:
    ///
    /// 1. LAYOUT - the scene is laid out the way the reference clip is: green stack on the left, puck
    ///    on the right, no hole anywhere, and a puck that is a real non-kinematic rigidbody with a live
    ///    collider.
    /// 2. INPUT - the puck only ever leaves the disc because someone aimed and released it, and the
    ///    resulting physical contact really triggers the repeatable stack cascade, with every block
    ///    keeping its whole form.
    /// </summary>
    public sealed class Case4InputProbe : MonoBehaviour
    {
        /// <summary>Set once the probe has finished, pass or fail.</summary>
        public static bool Finished;

        /// <summary>Whether every assertion held.</summary>
        public static bool Passed;

        /// <summary>Human readable transcript, written to the gate log.</summary>
        public static string Transcript = "";

        readonly StringBuilder _log = new StringBuilder();
        int _failures;

        void Line(string s)
        {
            _log.AppendLine(s);
            Shared.Sequencing.SeqLog.Info("[Case4Gate] " + s);
        }

        void Check(bool ok, string what)
        {
            if (!ok) _failures++;
            Line((ok ? "PASS " : "FAIL ") + what);
        }

        IEnumerator Start()
        {
            // The gate's own logging was distorting what it measures: a Debug.Log per assertion with a
            // full managed stack trace costs milliseconds each in batchmode, the frames stretch, and
            // Time.maximumDeltaTime then clamps how much physics the collapse window actually gets.
            // Same shot, twelve blocks down in the capture and six under the gate. Stack traces off.
            Application.SetStackTraceLogType(LogType.Log, StackTraceLogType.None);
            Application.SetStackTraceLogType(LogType.Warning, StackTraceLogType.None);

            Finished = false;
            Passed = false;

            Case4Director director = Object.FindFirstObjectByType<Case4Director>(FindObjectsInactive.Include);
            PuckAimController aim = Object.FindFirstObjectByType<PuckAimController>(FindObjectsInactive.Include);
            if (director == null || aim == null)
            {
                Line("FAIL scene is missing Case4Director or PuckAimController");
                Done();
                yield break;
            }

            // ---------------------------------------------------------- layout gate
            Line("---- LAYOUT_GATE ----");

            Vector3 restPos = director.launcher.puck.position;
            Vector3 stackCenter = director.shatter.StackCenter();
            Line(string.Format("stack centre x={0:0.000}  puck start x={1:0.000}  (stack must be LEFT of the puck)",
                stackCenter.x, restPos.x));
            Check(stackCenter.x < restPos.x,
                  string.Format("green stack is left of the puck (dx={0:0.00})", restPos.x - stackCenter.x));

            string holes = FindHoleObjects();
            Line("visible objects whose name mentions a hole: " + (holes.Length == 0 ? "<none>" : holes));
            Check(holes.Length == 0, "no hole object is visible in the scene");

            Rigidbody rb = director.launcher.Body;
            Check(rb != null, "puck has a Rigidbody");
            Check(rb != null && !rb.isKinematic, "puck Rigidbody is NOT kinematic (isKinematic=" +
                  (rb != null ? rb.isKinematic.ToString() : "n/a") + ")");
            int enabledCols = EnabledColliders(director.launcher.puck);
            Line("puck colliders: " + director.launcher.ColliderSummary());
            Check(enabledCols > 0, "puck has at least one enabled collider");
            Line("rim state at rest: cyanActive=" + director.wall.IsActive + " (must be false before the release)");
            Check(!director.wall.IsActive, "arena rim is idle white before the release, not cyan");

            // ---------------------------------------------------------- rest geometry (what is DRAWN)
            // ADDED for the owner's "baslangicta baska pak var, giden pak farkli". Every assertion the
            // probe already made was about the puck's TRANSFORM and its RIGIDBODY. Not one of them read
            // a Renderer, so nothing here could see that the coin the player looks at and the body the
            // physics moves had come apart: PuckLauncher.visualPadLift offsets the render child only.
            //
            // Measured off Unity's own Renderer.bounds, never off the field that produces the offset.
            Line("---- REST_RENDER ----");

            PuckLauncher launcher = director.launcher;
            Transform visual = launcher.Visual;
            Transform patch = launcher.ContactPatch;
            Renderer visualRenderer = visual != null ? visual.GetComponent<Renderer>() : null;
            Renderer patchRenderer = patch != null ? patch.GetComponent<Renderer>() : null;

            Check(visualRenderer != null, "the puck has a render child to measure");
            Check(patchRenderer != null, "the puck has a contact patch to measure against");

            if (visualRenderer != null && patchRenderer != null)
            {
                Bounds coin = visualRenderer.bounds;
                Bounds patchBounds = patchRenderer.bounds;
                float gap = coin.min.y - patchBounds.max.y;
                Line(string.Format(
                    "coin drawn at y[{0:0.000},{1:0.000}] (d={2:0.000}u across, {3:0.000}u thick); " +
                    "contact patch top y={4:0.000}; gap={5:0.000}u; visualPadLift={6:0.000}",
                    coin.min.y, coin.max.y, coin.size.x, coin.size.y,
                    patchBounds.max.y, gap, launcher.visualPadLift));
                // The bar is a third of the coin's own thickness. Anything above that and the coin is
                // visibly hovering over a dark ellipse that stays on the floor - two round objects.
                float bar = Mathf.Max(0.06f, coin.size.y * 0.34f);
                Check(gap <= bar,
                      string.Format("the coin rests on its own contact patch rather than floating over it " +
                                    "(gap {0:0.000}u, bar {1:0.000}u)", gap, bar));

                // The same separation stated against the FLOOR, so it is not the contact patch measured
                // twice: if the patch were ever moved the line above would follow it and stop meaning
                // "grounded". The puck's collider centre is deliberately lifted (Case4SceneSetup puts
                // it at unit*0.44 so the body sweeps the bottom row of the stack rather than grinding
                // along the ground) - so the collider is NOT the thing to measure the drawing against.
                // The floor is.
                float floorTop = director.shatter.FloorTopY();
                float ride = coin.min.y - floorTop;
                Line(string.Format("coin underside rides {0:0.000}u above the floor (floor top y={1:0.000}); " +
                     "collider spans y[{2:0.000},{3:0.000}] and is lifted on purpose",
                     ride, floorTop,
                     launcher.PuckCollider != null ? launcher.PuckCollider.bounds.min.y : 0f,
                     launcher.PuckCollider != null ? launcher.PuckCollider.bounds.max.y : 0f));
                Check(ride <= coin.size.y,
                      string.Format("the coin is drawn on the floor, not hovering over it (rides " +
                                    "{0:0.000}u, its own thickness is {1:0.000}u)", ride, coin.size.y));
            }

            // Anything ELSE drawing inside the puck's column at rest is a second object in the frame.
            // The imported start disc is the known candidate: PuckLauncher hides it by a material-name
            // lookup, and a lookup that resolves to null hides nothing and says nothing.
            Renderer pad = launcher.PadRenderer;
            Line("launch pad renderer: " + (pad == null ? "<not resolved - HidePad is a no-op>"
                                                        : pad.name + " enabled=" + pad.enabled));
            Check(pad == null || !pad.enabled,
                  "the imported start disc is not being drawn" +
                  (pad == null ? " (WARNING: it could not be resolved either, so this line is weak)" : ""));

            string extras = RenderersInPuckColumn(launcher);
            Line("other enabled renderers standing in the puck's column at rest: " +
                 (extras.Length == 0 ? "<none>" : extras));
            Check(extras.Length == 0,
                  "only the puck itself is drawn at the start position (" + extras + ")");

            // ---------------------------------------------------------- input gate
            Line("---- INPUT_GATE ----");

            float watch = Time.realtimeSinceStartup + 3.0f;
            bool playedByItself = false;
            // COMPLAINT 1. Nobody has touched the puck yet. Whatever the cone does in this window is
            // what the owner sees on the title frame. Sampled every frame rather than once, so a cone
            // that is drawn on only some frames still trips it.
            int idleFrames = 0, idleFramesWithCone = 0;
            Vector3 idleConeDir = Vector3.zero;
            while (Time.realtimeSinceStartup < watch)
            {
                if (director.IsPlaying) { playedByItself = true; break; }
                idleFrames++;
                if (aim.IndicatorVisible)
                {
                    idleFramesWithCone++;
                    idleConeDir = aim.IndicatorDirection;
                }
                yield return null;
            }
            Check(!playedByItself, "scene idle on load: the puck does not fire by itself");

            Line(string.Format("idle indicator: cone drawn on {0} of {1} untouched frames, heading {2}",
                idleFramesWithCone, idleFrames, idleConeDir.ToString("0.00")));
            Check(idleFramesWithCone == 0,
                  "no aim cone is drawn while nobody is pulling the puck (" + idleFramesWithCone +
                  " of " + idleFrames + " idle frames had one)");
            Check(director.Ready, "director finished its prewarm and is waiting for input");
            Check(director.shatter.MaxDisplacement() < director.shatter.blockSize * 0.5f,
                  string.Format("stack stood still while idle (max drift {0:0.000}u)", director.shatter.MaxDisplacement()));

            // ---------------------------------------------------------- rest geometry
            // ADDED after four scene-graph regressions all survived a green gate. None of them could
            // be falsified by anything this probe read: it asserted the stack was left of the puck,
            // that no hole object existed, that the puck was a live rigidbody, that >=8 blocks moved
            // and >=4 rotated, that nothing fragmented, and that the report completed. Every one of
            // those held while the puck flew through 35 of 36 blocks, 19 blocks settled outside the
            // arena, 28 hovered in mid-air and the pile rested 10% inside itself.
            //
            // The four assertions below are the ones those regressions would have failed, and they
            // are measured off Unity's own Renderer.bounds rather than off the code that places the
            // blocks - a check that shares its arithmetic with the thing it checks cannot fail.
            float worstOverlap;
            int overlapPairs = director.shatter.RestOverlapPairs(out worstOverlap);
            Line(string.Format("rest interpenetration: {0} overlapping pairs, worst {1:0.0000}u",
                overlapPairs, worstOverlap));
            Check(overlapPairs == 0,
                  "the stack does not rest inside itself (" + overlapPairs + " overlapping pairs)");

            Line("reference aim dir = " + aim.ReferenceAimDirection.ToString("0.000"));
            yield return aim.SimulateReferenceShot(0.25f);

            Check(aim.LastLaunchAccepted, "release STARTED the sequence");
            Check(director.IsPlaying, "director is running after the release");

            float moved = 0f;
            float until = Time.realtimeSinceStartup + 4.0f;
            while (Time.realtimeSinceStartup < until && director.IsPlaying)
            {
                moved = Mathf.Max(moved, Vector3.Distance(director.launcher.puck.position, restPos));
                yield return null;
            }
            Check(moved > 1.0f, "puck travelled off the disc (max offset " + moved.ToString("0.00") + ")");

            float deadline = Time.realtimeSinceStartup + 25.0f;
            while (director.IsPlaying && Time.realtimeSinceStartup < deadline) yield return null;
            Check(!director.IsPlaying, "sequence ran to completion");

            // ---------------------------------------------------------- collapse quality
            Line("---- COLLAPSE ----");
            int blocks = director.shatter.BlockCount;
            int movedBlocks = director.shatter.MovedCount(director.shatter.blockSize * 0.5f);
            int rotated = director.shatter.RotatedCount(12f);
            int fragments = director.shatter.FragmentCount;
            int whole = director.shatter.WholeFormCount;
            float ratio = movedBlocks > 0 ? (float)fragments / movedBlocks : 0f;

            Line(string.Format("blocks={0} moved={1} rotated>12deg={2} fragments={3} wholeForm={4} fragments/moved={5:0.000} maxDisplacement={6:0.00}",
                blocks, movedBlocks, rotated, fragments, whole, ratio, director.shatter.MaxDisplacement()));
            Line(string.Format("puck after the shot: rail contacts={0} flight={1:0.00}u (+{2:0.00}u scripted glide = {3:0.00}u total) kinematic={4}",
                director.launcher.BounceCount, director.launcher.FlightDistance,
                director.launcher.TravelledDistance - director.launcher.FlightDistance,
                director.launcher.TravelledDistance,
                director.launcher.Body != null && director.launcher.Body.isKinematic));

            Check(movedBlocks >= 8, "at least 8 blocks were knocked out of place (" + movedBlocks + ")");
            Check(rotated >= 4, "blocks rotated rather than only sliding (" + rotated + " turned more than 12 deg)");
            Check(ratio <= 0.25f, "the collapse keeps whole cubes: fragments/moved = " + ratio.ToString("0.000") + " <= 0.25");
            Check(whole >= blocks - 2, "at least " + (blocks - 2) + " blocks still exist as whole cubes (" + whole + ")");
            Check(director.Report.completed, "sequence report completed");

            // ---------------------------------------------------------- settled geometry
            float worstOut, worstGap;
            int outside = director.shatter.OutsideArenaCount(out worstOut);
            int offFloor = director.shatter.OffFloorCount(0.02f, out worstGap);
            Line(string.Format("settled: outsideArena={0} (worst {1:0.000}u)  offFloor={2} (worst {3:0.000}u)  floorTop={4:0.000}",
                outside, worstOut, offFloor, worstGap, director.shatter.FloorTopY()));
            Check(outside == 0,
                  "every settled block is inside the arena (" + outside + " outside" +
                  (outside < 0 ? "; SETTLE AREA UNRESOLVED" : "") + ")");
            Check(offFloor == 0,
                  "every settled block rests on the floor within 0.02u (" + offFloor + " off it)");

            // The puck must actually hit the block its approach selected. This is the assertion the
            // min-x filter would have failed outright: it handed the puck a block seven columns
            // behind the near face and ignored the other 35, and the shot passed through the stack
            // for five frames. StackHit alone does NOT catch that - the puck did eventually touch
            // the one block it was allowed to touch, deep inside the pile.
            Collider chosen = director.shatter.ChosenImpactCollider;
            Collider actual = director.launcher.ImpactCollider;
            Line("impact: filter chose " + (chosen != null ? chosen.name : "<none>") +
                 ", puck actually contacted " + (actual != null ? actual.name : "<none>"));
            Check(actual != null, "the puck made a real solver contact with the stack");
            // Kept, but demoted, and the comment says why: this line is nearly a tautology. The filter
            // ignores every other cube, so the puck contacts whichever cube it is allowed to contact.
            // Measured: restoring the old smallest-x rule as a negative control left this line GREEN.
            Check(chosen != null && actual == chosen,
                  "the filter's chosen block is the one the solver reported (weak: near-tautological)");

            // THE assertion for finding 1. Filter-independent: it walks back along the puck's own
            // contact heading and counts the cubes standing between the outside world and the contact
            // point. Zero means the puck struck the face it was approaching. Anything above zero means
            // it travelled through the stack to reach a cube behind it - the regression itself.
            string blockedBy;
            int passedThrough = director.shatter.BlocksPassedThrough(
                director.launcher.ImpactPoint, director.launcher.ImpactDirection,
                director.launcher.PuckRadius, actual, out blockedBy);
            Line(string.Format("puck passed through {0} block(s) to reach its contact point (first: {1}); " +
                 "impact={2} heading={3}", passedThrough, blockedBy,
                 director.launcher.ImpactPoint.ToString("0.000"),
                 director.launcher.ImpactDirection.ToString("0.00")));
            Check(passedThrough == 0,
                  "the puck reached the stack's near face without passing through any block (" +
                  passedThrough + " passed through)");

            // formationSpread: LOGGED, NOT GATED, and the reason is a measurement.
            // It is settled XZ footprint over rest XZ footprint. Its denominator changed by 8x when
            // the stack stopped being one row deep and became a 3.5 x 3.6u formation, so the same
            // expression read x39.2 on the flat wall, x5.0 on the deep stack and x1.93 once the
            // debris was fitted to the arena - three different numbers with no code change, all
            // reported against a fixed ">= 3.0" bar. In this layout that bar is unreachable by
            // construction: the whole left lane is 4.95 x 17.1u, so even debris thrown to the back
            // wall tops out near x5, and any plausible debris field sits under x2. A threshold the
            // correct outcome cannot satisfy is measuring the wrong property, which is exactly what
            // the old "every block moved" clause was retired for. The property it stood for - the
            // formation is gone - is now covered honestly by UndisturbedCount plus the two
            // geometric assertions above.
            Line(string.Format("formationSpread x{0:0.00} (informational; the old >=3.0 bar is not " +
                 "meaningful in this layout - see the comment in Case4InputProbe)",
                 director.shatter.FormationSpread()));

            // ---------------------------------------------------------- second pull
            // The reference shot above was pull #1. Everything from here is pull #2, and it is a
            // separate section because NOTHING above can fail on account of it: the layout, cascade
            // and settle assertions are all read after a single shot, and the owner's complaints are
            // about what the SECOND pull does. The heading is deliberately rotated off the reference
            // bank so that "the cone is drawn on the reference heading" is distinguishable from
            // "the cone is drawn on the player's heading" - on the reference heading itself the two
            // are the same line and the assertion could not fail.
            yield return SecondPull(director, aim);

            // Pull #3, aimed so that it cannot reach the stack. Everything the impact beat does is
            // supposed to be a consequence of a real solver contact; this shot makes no contact, so
            // every one of those consequences has to be absent.
            yield return MissShot(director, aim);

            // LAST, because unlike everything above it this one runs a real harness Replay(): it is the
            // only section that exercises Case4Director's scripted idle, and it must not disturb the
            // player-path measurements that precede it.
            yield return ScriptedWindUp(director);

            Done();
        }

        /// <summary>
        /// THE OWNER'S "basta atarken geri cekilme yada kuculme gibi bir sey yapiyor" - measured on the
        /// path he is actually watching.
        ///
        /// He could not say which of the two it was, and that ambiguity is the finding rather than a
        /// gap in his report: on the harness path the puck has exactly ONE wind-up motion, and that one
        /// motion produces both readings at once. It drags the puck 0.85u back along the aim - straight
        /// into Rail_Bottom, whose inner face is only 0.52u behind the disc - so the coin retreats AND
        /// is progressively swallowed by the rail until none of it is drawn at all.
        ///
        /// Reference, measured off Buca.mp4 frame by frame with the same gold mask: over its first 32
        /// frames (0.000-0.608 s at 51 fps) the reference puck's gold centroid moves 1.3 px in x and
        /// 1.2 px in y, and its bounding box is a constant 88x58 px with bit-identical ymin/ymax. It
        /// steps -79 px in a single frame at n=32 and that is the launch. The reference has no wind-up
        /// in either direction, so the fix is to remove ours rather than to shrink it.
        ///
        /// Nothing the probe read before this could see any of it. Every earlier assertion is taken
        /// either at rest, before the wind-up has moved anything, or after a PLAYER release - and a
        /// player release sets Case4Director._playerDriven, which zeroes scriptedIdle and skips the
        /// wind-up entirely. The scripted wind-up runs on the capture path ONLY, so the gate had never
        /// once executed the frames the owner is complaining about.
        ///
        /// This drives the real thing: Replay() on the director, sampled every frame across the whole
        /// idle window. It does not call the launcher's hold method itself, so it cannot be satisfied by
        /// that method being renamed or emptied while Case4Director keeps moving the puck some other
        /// way. Everything it reads comes from the puck's transform, its rigidbody, Unity's
        /// Renderer.bounds and Physics.ComputePenetration.
        /// </summary>
        IEnumerator ScriptedWindUp(Case4Director director)
        {
            Line("---- SCRIPTED_WIND_UP ----");

            PuckLauncher launcher = director.launcher;
            Transform puck = launcher.puck;
            Transform visual = launcher.Visual;
            Renderer coinRenderer = visual != null ? visual.GetComponent<Renderer>() : null;
            Collider puckCol = launcher.PuckCollider;
            Rigidbody body = launcher.Body;
            Camera cam = Camera.main;

            Check(coinRenderer != null, "there is a drawn coin to measure across the wind-up");
            Check(puckCol != null && body != null, "there is a puck body and collider to measure");
            if (coinRenderer == null || puckCol == null || body == null) yield break;

            // The arena walls. They are COLLIDER-ONLY boxes - Case4SceneSetup.AddBox adds a BoxCollider
            // and no Renderer - and the white rim the player sees is the imported arena mesh drawn over
            // the same volume. So the box is what the coin visually disappears behind, and its bounds
            // are the honest stand-in for the drawn rail. A first cut of this section looked for
            // Renderers on these objects, found zero, and reported 0.000 hidden on every frame of a
            // wind-up that ends with the puck completely invisible: an assertion that could not observe
            // the thing it named.
            var walls = new System.Collections.Generic.List<Collider>();
            Collider[] allCols = Object.FindObjectsByType<Collider>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            for (int i = 0; i < allCols.Length; i++)
            {
                Collider c = allCols[i];
                if (c == null || c.isTrigger) continue;
                string n = c.gameObject.name;
                if (n.StartsWith("Rail_") || n == "Divider") walls.Add(c);
            }
            Line("arena walls resolved: " + walls.Count + " collider box(es)");
            Check(walls.Count > 0, "the arena walls were resolvable, so the assertions below can see something");

            // Let anything the previous shots left in flight settle, then run the harness shot exactly
            // as BatchCaptureRunner does: Replay() is Stop + ResetState + Play, and a Play() that is not
            // flagged player-driven is the one that takes the scripted idle.
            float settle = Time.realtimeSinceStartup + 3f;
            while (director.IsPlaying && Time.realtimeSinceStartup < settle) yield return null;
            director.AllowPlayWithoutInput();
            director.Replay();
            yield return null;

            Vector3 firstPose = puck.position;
            Vector3 launchFrom = firstPose;
            float worstRetreat = 0f, worstPen = 0f, worstSwallowed = 0f, restSwallowed = 0f;
            float startArea = ScreenArea(cam, coinRenderer.bounds), minArea = float.MaxValue;
            string worstWall = "<none>";
            int idleFrames = 0;
            bool sawLaunch = false;

            // The body is held kinematic for the whole scripted idle and goes dynamic in Launch(), so
            // "still kinematic" IS the idle window, read off the rigidbody rather than off a duration
            // this probe would otherwise have to keep in step with Case4Director.idleDelay.
            float cap = Time.realtimeSinceStartup + 8f;
            while (Time.realtimeSinceStartup < cap)
            {
                if (!body.isKinematic) { launchFrom = puck.position; sawLaunch = true; break; }

                idleFrames++;
                Vector3 pos = puck.position;
                Bounds coin = coinRenderer.bounds;

                float retreat = XZDist(pos, firstPose);
                if (retreat > worstRetreat) worstRetreat = retreat;

                float area = ScreenArea(cam, coin);
                if (area < minArea) minArea = area;

                float swallowed = 0f, pen = 0f;
                string penWall = "<none>";
                for (int w = 0; w < walls.Count; w++)
                {
                    swallowed = Mathf.Max(swallowed, OverlapFraction(coin, walls[w].bounds));
                    Vector3 dir; float dist;
                    if (Physics.ComputePenetration(puckCol, pos, puck.rotation,
                                                   walls[w], walls[w].transform.position,
                                                   walls[w].transform.rotation, out dir, out dist) && dist > pen)
                    { pen = dist; penWall = walls[w].gameObject.name; }
                }
                if (idleFrames == 1) restSwallowed = swallowed;
                if (swallowed > worstSwallowed) worstSwallowed = swallowed;
                if (pen > worstPen) { worstPen = pen; worstWall = penWall; }

                if (idleFrames <= 3 || idleFrames % 12 == 0)
                    Line(string.Format("  idle f{0,-3} puck={1} retreat={2:0.000}u hiddenByWall={3:0.000} " +
                         "wallPenetration={4:0.000}u ({5}) screenArea={6:0} px2",
                         idleFrames, pos.ToString("0.000"), retreat, swallowed, pen, penWall, area));

                yield return null;
            }

            Line(string.Format("scripted idle lasted {0} frames; puck stood at {1} and the shot left from {2}",
                idleFrames, firstPose.ToString("0.000"), launchFrom.ToString("0.000")));
            Check(idleFrames > 10 && sawLaunch,
                  "the scripted wind-up really ran and really ended in a launch (" + idleFrames +
                  " idle frames, sawLaunch=" + sawLaunch + ") - without this the three assertions below prove nothing");
            Line(string.Format("screen-projected coin area across the idle: {0:0} -> {1:0} px2 ({2:0.0}%). " +
                 "INFORMATIONAL ONLY: this projects the drawn bounds and is blind to occlusion, which is " +
                 "why it barely moves while the coin is going out of sight behind a rail. The " +
                 "hidden-by-wall figure is the gated one.",
                 startArea, minArea == float.MaxValue ? startArea : minArea,
                 startArea > 0f && minArea != float.MaxValue ? 100f * minArea / startArea : 100f));

            // ---- "geri cekilme"
            Line(string.Format("the scripted wind-up moved the puck {0:0.000}u backwards before firing", worstRetreat));
            Check(worstRetreat <= 0.05f,
                  "the puck does not pull back before the scripted shot (it retreated " +
                  worstRetreat.ToString("0.000") + "u; the reference puck's centroid moves 1.3 px over " +
                  "the whole 0.61 s it stands there)");

            // ---- "kuculme". Absolute, and deliberately NOT measured as a change from the rest pose: a
            // relative bar would be satisfied by parking the puck inside the rail for the entire idle
            // instead of sliding it in. The puck would be just as invisible and the line would read green.
            Line(string.Format("most of the drawn coin inside an arena wall at any point in the wind-up: " +
                 "{0:0.000}; on the first idle frame it is {1:0.000}", worstSwallowed, restSwallowed));
            Check(worstSwallowed <= 0.10f,
                  "the coin is never swallowed by a rail during the wind-up (worst " +
                  worstSwallowed.ToString("0.000") + " of it was inside one)");

            Line(string.Format("deepest the wind-up drove the puck's collider into a wall: {0:0.000}u into {1}",
                 worstPen, worstWall));
            Check(worstPen <= 0.05f,
                  "the wind-up never parks the puck inside an arena wall (" +
                  worstPen.ToString("0.000") + "u into " + worstWall + ")");

            // ---- and the consequence: the shot has to leave from where the puck was standing.
            float originDrift = XZDist(launchFrom, firstPose);
            Line(string.Format("launch origin is {0:0.000}u from the pose the puck held all through the idle",
                 originDrift));
            Check(sawLaunch && originDrift <= 0.15f,
                  "the scripted shot leaves from where the puck was standing (" +
                  originDrift.ToString("0.000") + "u away)");

            float done = Time.realtimeSinceStartup + 25f;
            while (director.IsPlaying && Time.realtimeSinceStartup < done) yield return null;
            Line("harness shot finished; puck at " + puck.position.ToString("0.000"));
        }

        /// <summary>Axis-aligned screen-space area of a world bounds, in square pixels. 0 if there is no camera.</summary>
        static float ScreenArea(Camera cam, Bounds b)
        {
            if (cam == null) return 0f;
            float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
            for (int i = 0; i < 8; i++)
            {
                Vector3 corner = new Vector3(
                    (i & 1) == 0 ? b.min.x : b.max.x,
                    (i & 2) == 0 ? b.min.y : b.max.y,
                    (i & 4) == 0 ? b.min.z : b.max.z);
                Vector3 sp = cam.WorldToScreenPoint(corner);
                if (sp.z <= 0f) return 0f;
                minX = Mathf.Min(minX, sp.x); maxX = Mathf.Max(maxX, sp.x);
                minY = Mathf.Min(minY, sp.y); maxY = Mathf.Max(maxY, sp.y);
            }
            return (maxX - minX) * (maxY - minY);
        }

        /// <summary>How much of <paramref name="a"/>'s volume lies inside <paramref name="b"/>, 0..1.</summary>
        static float OverlapFraction(Bounds a, Bounds b)
        {
            float dx = Mathf.Min(a.max.x, b.max.x) - Mathf.Max(a.min.x, b.min.x);
            float dy = Mathf.Min(a.max.y, b.max.y) - Mathf.Max(a.min.y, b.min.y);
            float dz = Mathf.Min(a.max.z, b.max.z) - Mathf.Max(a.min.z, b.min.z);
            if (dx <= 0f || dy <= 0f || dz <= 0f) return 0f;
            float vol = a.size.x * a.size.y * a.size.z;
            return vol <= 0f ? 0f : Mathf.Clamp01((dx * dy * dz) / vol);
        }

        /// <summary>
        /// Drives one more player-style pull after a shot has already been taken, and measures the four
        /// things the owner reported. Every number here comes from the puck's own transform, its
        /// rigidbody and Unity's collider bounds - never from the code that moves it.
        /// </summary>
        IEnumerator SecondPull(Case4Director director, PuckAimController aim)
        {
            Line("---- SECOND_PULL ----");

            Bounds arena;
            if (!ArenaFootprint(out arena))
            {
                Line("FAIL could not resolve the arena footprint from the Floor collider");
                _failures++;
                yield break;
            }
            Line(string.Format("arena floor footprint x[{0:0.000},{1:0.000}] z[{2:0.000},{3:0.000}]",
                arena.min.x, arena.max.x, arena.min.z, arena.max.z));

            Transform puck = director.launcher.puck;
            Collider puckCol = director.launcher.PuckCollider;

            // Wait for the first shot to be completely finished, so this really is a fresh pull and
            // not a launch refused because the director was still busy.
            float settleWait = Time.realtimeSinceStartup + 5f;
            while (director.IsPlaying && Time.realtimeSinceStartup < settleWait) yield return null;

            Line(string.Format("state left by pull #1: puck at {0}, kinematic={1}, colliderEnabled={2}",
                puck.position.ToString("0.00"),
                director.launcher.Body != null && director.launcher.Body.isKinematic,
                puckCol != null && puckCol.enabled));

            // COMPLAINT 1, the measurement the old section could not make. Everything below is read
            // against WHERE PULL #1 STOPPED, not against the rest disc. The existing teleport check
            // only samples frames after the release, and the snap-back happens on the PRESS, inside
            // ArmNextShot - so it sat green through the whole bug.
            Vector3 settledAt = puck.position;
            Vector3 disc = director.launcher.RestPosition;
            float settleToDisc = XZDist(settledAt, disc);
            Line(string.Format("pull #1 came to rest {0:0.000}u from the rest disc {1}; a pull that " +
                 "starts on the disc is therefore distinguishable from one that starts here",
                 settleToDisc, disc.ToString("0.00")));
            Check(settleToDisc > 3f,
                  "the first shot left the puck far enough from the disc for the next two assertions " +
                  "to mean anything (" + settleToDisc.ToString("0.000") + "u)");

            // A heading 40 deg off the reference bank, so complaint 2 is falsifiable.
            Vector3 refDir = aim.ReferenceAimDirection;
            refDir.y = 0f; refDir.Normalize();
            Vector3 pullDir = Quaternion.AngleAxis(40f, Vector3.up) * refDir;
            Line("pull #2 heading " + pullDir.ToString("0.000") + " (reference " + refDir.ToString("0.000") + ", 40 deg apart)");

            _sampling = true;
            _worstConeError = -1f;
            _coneFrames = 0;
            _worstTeleport = 0f;
            _teleportAt = Vector3.zero;
            _worstPullJump = 0f;
            _pullJumpAt = Vector3.zero;
            _launchPos = Vector3.zero;
            _launchColliderEnabled = true;
            _sawLaunch = false;
            _escaped = false;
            _worstEscape = 0f;
            _playerAim = pullDir;
            _arena = arena;
            _director = director;
            _aim = aim;
            StartCoroutine(SampleShot());

            Vector3 press = puck.position;
            Vector3 release = press - pullDir * (aim.maxPull * 0.9f);
            yield return aim.SimulateDragRelease(press, release, 0.35f);

            Check(aim.LastLaunchAccepted, "the SECOND pull was accepted as a launch");

            float deadline = Time.realtimeSinceStartup + 25f;
            while (director.IsPlaying && Time.realtimeSinceStartup < deadline) yield return null;
            // Let any post-sequence glide or roll finish before the escape verdict is read.
            float tail = Time.realtimeSinceStartup + 1.5f;
            while (Time.realtimeSinceStartup < tail) yield return null;
            _sampling = false;

            // ---- COMPLAINT 2: the cone must sit on the player's heading, not the scripted bank.
            Line(string.Format("cone during pull #2: visible on {0} frames, worst heading error {1:0.0} deg " +
                 "from the player's aim (reference bank is 40.0 deg away)",
                 _coneFrames, _worstConeError));
            Check(_coneFrames == 0 || _worstConeError <= 12f,
                  "every frame the cone was drawn, it lay along the player's own pull (worst " +
                  (_worstConeError < 0f ? "n/a" : _worstConeError.ToString("0.0")) + " deg)");

            // ---- COMPLAINT 3: the release must not be followed by the puck jumping back to the disc.
            Line(string.Format("largest single-frame puck jump after the release, while it was still " +
                 "kinematic: {0:0.000}u at {1}; rest disc is at {2}",
                 _worstTeleport, _teleportAt.ToString("0.00"),
                 director.launcher.RestPosition.ToString("0.00")));
            Check(_worstTeleport <= 0.5f,
                  "the puck did not snap back to the start after the release (worst jump " +
                  _worstTeleport.ToString("0.000") + "u)");

            // ---- COMPLAINT 1, the part the line above cannot see: the snap happens on the PRESS.
            Line(string.Format("largest single-frame puck jump DURING the pull, before the release: " +
                 "{0:0.000}u at {1}", _worstPullJump, _pullJumpAt.ToString("0.00")));
            Check(_worstPullJump <= 1.5f,
                  "the puck did not jump anywhere when the player put his finger down (worst " +
                  _worstPullJump.ToString("0.000") + "u; the whole pull-back offset is 0.85u)");

            // ---- COMPLAINT 1, the verdict: where did the second shot actually leave from?
            float fromSettled = XZDist(_launchPos, settledAt);
            float fromDisc = XZDist(_launchPos, disc);
            Line(string.Format("pull #2 fired from {0}: {1:0.000}u from where pull #1 stopped, " +
                 "{2:0.000}u from the rest disc", _launchPos.ToString("0.00"), fromSettled, fromDisc));
            Check(_sawLaunch && fromSettled <= 1.5f,
                  "the second shot started from where the first one stopped (" +
                  fromSettled.ToString("0.000") + "u away; the pull-back offset alone is 0.85u)");
            Check(_sawLaunch && fromSettled < fromDisc,
                  "the second shot started nearer its own resting place than the rest disc (" +
                  fromSettled.ToString("0.000") + "u vs " + fromDisc.ToString("0.000") + "u)");

            // ---- COMPLAINT 4, part one: a puck with no collider cannot be stopped by any rail.
            Line("collider enabled on the frame the puck was fired: " + _launchColliderEnabled +
                 " (sawLaunch=" + _sawLaunch + ")");
            Check(_sawLaunch && _launchColliderEnabled,
                  "the puck had a live collider on the frame it was fired");

            // ---- COMPLAINT 4, part two: measured against the floor's own bounds, not against the
            // rail-placing code. The floor footprint is the generous bound - the rails stand on its
            // edge - so anything failing this is unambiguously outside the arena.
            Line(string.Format("puck escape: {0}, worst distance outside the floor footprint {1:0.000}u",
                _escaped ? "YES" : "no", _worstEscape));
            Check(!_escaped,
                  "the puck stayed inside the arena for the whole of the second shot (worst " +
                  _worstEscape.ToString("0.000") + "u outside)");
        }

        // Sampler state. Written only by SampleShot, read only after _sampling goes false.
        bool _sampling;
        Vector3 _launchPos;
        float _worstPullJump;
        Vector3 _pullJumpAt;
        float _worstConeError;
        int _coneFrames;
        float _worstTeleport;
        Vector3 _teleportAt;
        bool _launchColliderEnabled;
        bool _sawLaunch;
        bool _escaped;
        float _worstEscape;
        Vector3 _playerAim;
        Bounds _arena;
        Case4Director _director;
        PuckAimController _aim;

        /// <summary>
        /// One sample per frame of everything the four complaints are about. Deliberately passive: it
        /// reads transforms, a rigidbody and a collider, and changes nothing.
        /// </summary>
        IEnumerator SampleShot()
        {
            Transform puck = _director.launcher.puck;
            Rigidbody rb = _director.launcher.Body;
            Collider col = _director.launcher.PuckCollider;
            Vector3 prev = puck.position;
            bool released = false;
            float radius = _director.launcher.PuckRadius;
            int traceLines = 0;

            while (_sampling)
            {
                Vector3 pos = puck.position;
                Vector3 vel = rb != null ? rb.linearVelocity : Vector3.zero;
                bool kinematic = rb == null || rb.isKinematic;

                if (_aim.IndicatorVisible)
                {
                    _coneFrames++;
                    float err = Vector3.Angle(_aim.IndicatorDirection, _playerAim);
                    if (err > _worstConeError) _worstConeError = err;
                }

                // "Released" starts the moment the director accepts the shot; everything before that
                // is the drag itself, where the puck is allowed to follow the pointer.
                if (!released && _director.IsPlaying) released = true;

                if (!released)
                {
                    // The drag itself. The puck is allowed to FOLLOW the hand here, but it is not
                    // allowed to jump somewhere else the instant the hand goes down: the pull-back
                    // offset is 0.85u in total, so nothing legitimate moves it further than that in
                    // one frame.
                    float pullJump = Vector3.Distance(pos, prev);
                    if (kinematic && pullJump > _worstPullJump) { _worstPullJump = pullJump; _pullJumpAt = pos; }
                }

                if (released)
                {
                    float jump = Vector3.Distance(pos, prev);
                    // Only kinematic frames count: a fast physical puck legitimately covers ground
                    // between frames, but a teleport happens while the body is being placed by hand.
                    if (kinematic && jump > _worstTeleport) { _worstTeleport = jump; _teleportAt = pos; }

                    if (!_sawLaunch && !kinematic && vel.magnitude > 1f)
                    {
                        _sawLaunch = true;
                        _launchPos = pos;
                        _launchColliderEnabled = col != null && col.enabled;
                        Shared.Sequencing.SeqLog.Info(string.Format(
                            "[Case4Gate] TRACE launch at t={0:0.000} pos={1} vel={2} speed={3:0.00} colliderEnabled={4}",
                            Time.time, pos.ToString("0.000"), vel.ToString("0.00"), vel.magnitude,
                            _launchColliderEnabled));
                    }

                    float outX = Mathf.Max(_arena.min.x - pos.x, pos.x - _arena.max.x);
                    float outZ = Mathf.Max(_arena.min.z - pos.z, pos.z - _arena.max.z);
                    float outside = Mathf.Max(outX, outZ);
                    if (outside > _worstEscape) _worstEscape = outside;
                    if (outside > radius && !_escaped)
                    {
                        _escaped = true;
                        Shared.Sequencing.SeqLog.Info(string.Format(
                            "[Case4Gate] TRACE ESCAPE at t={0:0.000} pos={1} vel={2} speed={3:0.00} " +
                            "outsideBy={4:0.000}u colliderEnabled={5}",
                            Time.time, pos.ToString("0.000"), vel.ToString("0.00"), vel.magnitude,
                            outside, col != null && col.enabled));
                    }

                    if (_sawLaunch && traceLines < 40 && Time.frameCount % 6 == 0)
                    {
                        traceLines++;
                        Shared.Sequencing.SeqLog.Info(string.Format("[Case4Gate] TRACE t={0:0.000} pos={1} vel={2} speed={3:0.00} kin={4} col={5}",
                            Time.time, pos.ToString("0.00"), vel.ToString("0.0"), vel.magnitude,
                            kinematic, col != null && col.enabled));
                    }
                }

                prev = pos;
                yield return null;
            }
        }

        /// <summary>
        /// A third pull, constructed so it CANNOT reach the stack, which is what makes the owner's
        /// "birde vurmadan ses geliyor" falsifiable at all. It starts the puck from the rest disc on
        /// purpose - that is test setup, not the thing under test - and fires it straight down the
        /// right lane, parallel to the divider, so no reflection off an axis-aligned rail can ever
        /// carry it across into the stack's lane.
        ///
        /// If the shot nevertheless makes contact, the section FAILS rather than passing: a miss test
        /// that did not miss proves nothing, and saying so out loud is the point.
        /// </summary>
        IEnumerator MissShot(Case4Director director, PuckAimController aim)
        {
            Line("---- MISS_SHOT ----");

            float settleWait = Time.realtimeSinceStartup + 8f;
            while (director.IsPlaying && Time.realtimeSinceStartup < settleWait) yield return null;

            Transform puck = director.launcher.puck;

            // Arm the board first, then put the puck on the disc, so the heading below is measured
            // from a pose this test controls rather than from wherever pull #2 happened to stop.
            if (director.ShotSpent) director.ArmNextShot();
            director.launcher.ResumeFrom(director.launcher.RestPosition);
            yield return null;

            Vector3 stack = director.shatter.StackCenter();
            Vector3 heading = Vector3.back;     // straight at Rail_Bottom, no X component at all
            Line(string.Format("puck placed at {0}, stack centre {1}, firing {2} - pure -Z, so the " +
                 "puck reflects up and down its own lane and never crosses the divider",
                 puck.position.ToString("0.00"), stack.ToString("0.00"), heading.ToString("0.00")));

            // Counted where the sound is actually made. The line this replaces read
            // director.ContactlessImpactSfx, a counter whose only increment sat behind
            // `play && !earned` with `play = earned` - a contradiction - so it was pinned at zero and
            // the assertion below could not fail for any reason at all.
            int sfxBefore = director.DebrisSfxPlayed;

            Vector3 press = puck.position;
            Vector3 release = press - heading * (aim.maxPull * 0.9f);
            yield return aim.SimulateDragRelease(press, release, 0.35f);
            Check(aim.LastLaunchAccepted, "the MISS pull was accepted as a launch");

            float deadline = Time.realtimeSinceStartup + 40f;
            while (director.IsPlaying && Time.realtimeSinceStartup < deadline) yield return null;

            Line("stack contact on the miss shot: " + director.launcher.StackHit +
                 " (rail contacts " + director.launcher.BounceCount +
                 ", travelled " + director.launcher.TravelledDistance.ToString("0.00") + "u)");
            Check(!director.launcher.StackHit,
                  "the miss shot really missed - without this the two assertions below prove nothing");

            int contactless = director.DebrisSfxPlayed - sfxBefore;
            Line(string.Format("debris-layer sounds SOUNDED on a shot that touched nothing: {0} " +
                 "(withheld beats so far: {1})", contactless, director.DebrisSfxRefused));
            Check(contactless == 0,
                  "no impact or debris sound played on a shot that touched nothing (" + contactless + " did)");

            Line("coins launched on the miss shot: " + director.coins.LaunchedCount);
            Check(director.coins.LaunchedCount == 0,
                  "no coin left the pile on a shot that touched nothing (" +
                  director.coins.LaunchedCount + " did)");
        }

        /// <summary>
        /// Every enabled Renderer whose bounds stand inside a vertical column around the puck's rest
        /// position, excluding the puck's own hierarchy and the arena shell it sits on. Deliberately
        /// name-agnostic: the point is to catch an object nobody remembered was there.
        /// </summary>
        static string RenderersInPuckColumn(PuckLauncher launcher)
        {
            if (launcher == null || launcher.puck == null) return "";
            Vector3 rest = launcher.puck.position;
            float radius = Mathf.Max(0.05f, launcher.PuckRadius * 1.30f);

            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            Renderer[] all = Object.FindObjectsByType<Renderer>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            for (int i = 0; i < all.Length; i++)
            {
                Renderer r = all[i];
                if (r == null || !r.enabled) continue;
                if (r.transform.IsChildOf(launcher.puck)) continue;
                if (r is ParticleSystemRenderer || r is TrailRenderer || r is LineRenderer) continue;

                Bounds b = r.bounds;
                // The floor, the rails and the divider are long shells the puck legitimately stands on
                // or beside; a second PUCK-SIZED object is not. Filtering by footprint rather than by
                // name keeps this from going blind to a differently named disc.
                if (b.size.x > radius * 6f || b.size.z > radius * 6f) continue;
                if (Mathf.Abs(b.center.x - rest.x) > radius) continue;
                if (Mathf.Abs(b.center.z - rest.z) > radius) continue;

                if (sb.Length > 0) sb.Append(", ");
                sb.Append(r.name).Append(" @").Append(b.center.ToString("0.00")).Append(" size").Append(b.size.ToString("0.00"));
            }
            return sb.ToString();
        }

        /// <summary>Planar distance. Y is frozen on the body and irrelevant to every question here.</summary>
        static float XZDist(Vector3 a, Vector3 b)
        {
            return new Vector2(a.x - b.x, a.z - b.z).magnitude;
        }

        /// <summary>
        /// The arena's XZ footprint, taken from the Floor collider's own bounds. Using the floor rather
        /// than the rails keeps this a bound the code under test does not compute: the rails stand on
        /// the floor's edge, so being outside the floor is outside the arena by any reading.
        /// </summary>
        static bool ArenaFootprint(out Bounds bounds)
        {
            bounds = new Bounds();
            Collider[] all = Object.FindObjectsByType<Collider>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            for (int i = 0; i < all.Length; i++)
            {
                if (all[i] != null && all[i].gameObject.name == "Floor")
                {
                    bounds = all[i].bounds;
                    return true;
                }
            }
            return false;
        }

        static int EnabledColliders(Transform root)
        {
            if (root == null) return 0;
            Collider[] cols = root.GetComponentsInChildren<Collider>(true);
            int n = 0;
            for (int i = 0; i < cols.Length; i++) if (cols[i].enabled) n++;
            return n;
        }

        /// <summary>Every renderer still drawing something that calls itself a hole. Must come back empty.</summary>
        static string FindHoleObjects()
        {
            StringBuilder sb = new StringBuilder();
            Renderer[] all = Object.FindObjectsByType<Renderer>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            for (int i = 0; i < all.Length; i++)
            {
                Renderer r = all[i];
                if (r == null || !r.enabled || !r.gameObject.activeInHierarchy) continue;
                string n = r.gameObject.name.ToLowerInvariant();
                if (n.Contains("hole"))
                {
                    if (sb.Length > 0) sb.Append(", ");
                    sb.Append(r.gameObject.name);
                }
            }
            return sb.ToString();
        }

        void Done()
        {
            Passed = _failures == 0;
            _log.AppendLine("CASE4_GATE " + (Passed ? "GREEN" : "RED") + " failures=" + _failures);
            Transcript = _log.ToString();
            Shared.Sequencing.SeqLog.Info("[Case4Gate] CASE4_GATE " + (Passed ? "GREEN" : "RED") + " failures=" + _failures);
            Finished = true;
        }
    }
}
