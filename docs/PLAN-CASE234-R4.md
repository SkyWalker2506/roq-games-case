# Cases 2, 3, 4 — round 4. Fix the regressions, close the untouched items, prove everything on our own frames.

**Repo:** `/Users/musabkara/Projects/roq-games-case` — Unity 6000.3.11f1, URP, Gamma.
**Push remote:** `upstream_case` only. Never `origin`.
**Read first:** `docs/REFERANS-ANALIZ.md`, `docs/DERSLER.md`, and this file. They override your instincts.

## THE EVIDENCE CONTRACT — read this before anything else

A status line is acceptable ONLY in this form:

    <ID> <CLOSED|OPEN>: <metric> = <number> measured in <our frame file>, ref = <number> measured in <ref frame file>

Valid: "B5 CLOSED: lip thickness 11 px, lip RGB (196,64,58) sampled in
`.plan-build/verify/BlockHole/frame_00.png`; ref 12 px, (201,58,52) in
`docs/verify/case2/ref/ref_0.00s.png`".

NOT evidence, ever: code constants (`scale = 1.06`), serialized field values, gate colours, log lines,
duration reports, "verified", "looks correct". You reported all of these as measurements in earlier
rounds and every one was wrong on screen. If you did not read the number out of a PNG that this
round's run produced, do not write the line.

Reference frames already exist: `docs/verify/case2/ref/ref_<t>s.png`, `case3/ref/`, `case4/ref/`.
Our strips land in `.plan-build/verify/{BlockHole,Stickerdom,Buca}/frame_NN.png` (16 frames +
`report.json`; frame time = `i * totalDuration/(N-1)`). Beat-match: pick the strip frame nearest the
ref timestamp and SAY WHICH — in round 2 you compared Case 3 against a later state than the reference
beat.

## REGRESSION GUARD — re-verify ALL of these at the end of EVERY change batch, not just what you touched

Round 3 fixed the pit blob and simultaneously destroyed the Case 2 hole lips, flooded Case 3 with
label UI, and duplicated the Case 4 puck. Only the changed thing had been checked. Therefore, before
you may report anything closed, re-measure this whole list on fresh captures:

| Guard | Test (on our frames) |
|---|---|
| G1 Compile clean | `CaseBuild.CompileCheck` with `-quit`, then grep ITS log (`Logs/compile.log`) for `error CS`. Never read `~/Library/Logs/Unity/Editor.log` — that is the GUI editor's log and proved nothing in round 1 (12 real errors behind a "0 errors" claim). |
| G2 Case 1 untouched | `git diff --stat HEAD -- Assets/Case1_FitTheShape` is empty. |
| G3 No UI anywhere (B6/S3/S4 are CANCELLED, not pending) | In frame_00 of each case: crop top band (y 0.85–1.0) and bottom band (y 0.0–0.15), count text glyphs / badges / pips / counters = 0. Zero-count, not "I deleted the code" — the scene serializes stale copies. |
| G4 Pit interior has no black blob | Case 2 frame at the shatter beat: near-black (V<0.12) fraction inside each hole footprint ≤ 5.7% (the reference's own value). Round 3 achieved 0.0% — keep it. |
| G5 Exactly one puck | Every Case 4 frame: count of gold-hue connected components of puck size = 1. Round 3 showed two (one flying, one parked on the pad). |
| G6 Debris origin/direction (B1/B2/B3) | Case 2 shatter frames: debris bbox stays inside the hole footprint and moves downward. |
| G7 No input-asset drift | `git status` after each build: base prefabs/materials unchanged; only scene output may change. |
| G8 Builder idempotent where contracted | Two consecutive `Case3SceneSetup.Build` runs → identical scene file hash. |

A round that closes one item and silently breaks a guard is a net-negative round and will be sent back.

## TOOLING — getting these wrong cost you a full round each time

- Runner: `./tools/unity-run.sh -batchmode [-quit] -executeMethod <Class.Method> -logFile Logs/<name>.log`.
- Exclusive project lock: `pgrep -f "MacOS/Unity"` before ANY batch run.
- Every `Capture*` / `BuildAndCapture` runs WITHOUT `-quit` (they exit themselves). Only `CompileCheck`
  and plain `Build` take `-quit`.
- After every run grep ITS log for `SETUP_FAILED|Exception|error CS|FAILED|TIMEOUT|NONDETERMINISTIC`.
- Builders are not idempotent: `git checkout -- <scene>` before every clean build.
- Scene-serialized values override C# defaults: a code edit is not live until the builder rewrites the
  scene. Only a fresh `BuildAndCapture` is a trustworthy test.

## PRIORITY ORDER

### P0 — Case 3 regression: kill the label grid and the white blob

1. **(S3/S4 enforcement — regression)** The frame is now covered top and bottom with a repeating
   "Cat / Noodle / Sweets" label grid. This is the UI you were told to REMOVE in round 2; instead it
   multiplied. Remove every instance — from the scene, from any generated layer, from any texture you
   composited it into. Acceptance: in `.plan-build/verify/Stickerdom/frame_00.png`, crops y 0.80–1.00
   and y 0.00–0.20 contain ZERO text-label components (connected-component count of label-coloured
   pill shapes = 0). State the count.
2. **(S1 partial regression)** A large unshaded pure-white blob sits where the peeled sticker was. The
   slot must read as the reference does: either the placed sticker art or a dark silhouette with `?`.
   Acceptance: in frame_00, the sticker ROI (manifest:
   `Assets/Case3_Stickerdom/Textures/Reference/README.md`) contains no connected component that is
   >90% pure white (R,G,B all >245) and larger than 2% of the viewport. State the largest white
   component's area fraction.
3. **(Beat alignment)** When comparing against `ref_0.75s.png` etc., use the strip frame whose time is
   NEAREST that beat and name it. Round 2 compared a later state (sticker already collected) and called
   it matched.

### P1 — Case 2: restore the lips, remove the junk, add the missing bar

4. **(B5 — regressed in round 3)** The reference gives every hole a vivid lip in its own colour; ours
   are now dark rectangles with no lip at all. Restore the lip AND the depth gradient without
   reintroducing the black blob (guard G4). First measure the reference: crop the red-L hole
   (x 0.556–0.780, y 0.229–0.365 viewport) out of `docs/verify/case2/ref/ref_0.00s.png`, read lip
   thickness in px and lip RGB. Then match. Acceptance on `frame_00.png`: per hole, lip thickness
   within ±30% of the ref measurement; lip colour dE(CIE76) to the ref lip < 25; pit interior shows a
   gradient (interior mean V strictly between lip V and the 0.12 floor); G4 still passing. Gamma colour
   space: material values are raw sRGB, no conversion; identical `UnityPerMaterial` blocks across
   shader passes or URP drops to magenta silently.
5. **(B7, new)** Two black rectangles hang below the board — untouched for three rounds. Find them in
   the scene (they are objects, not shadows), delete them, and make sure the builder does not
   regenerate them. Acceptance: in frame_00, crop the band below the board (measure the board's lower
   edge, don't guess): near-black connected components of area > 0.1% viewport = 0.
6. **(B8, new)** The tall cyan bar (block + hole) is missing. Reference bbox: x 0.774–0.889,
   y 0.234–0.634 (`REFERANS-ANALIZ.md` §2.1). Add it at that measured position. Acceptance: in
   frame_00, a cyan-hue component whose bbox matches the ref bbox within 0.03 viewport on each edge.
7. **(B9, new)** The board bezel is a thin flat line; the reference has a thick rounded frame. Measure
   the ref bezel thickness in px off `ref_0.00s.png` (sample the frame band on all four sides), then
   build it. Acceptance: bezel thickness in frame_00 within ±25% of ref, corners visibly rounded
   (outer corner pixel is background, not bezel colour).
8. **(B4 quality — "mushy purple shatter")** The plus-block shatter reads as a soft mush; the reference
   shows discrete crystal shards. Acceptance: in the strip frame nearest ref t=0.95 s, a crop of the
   hole footprint contains ≥ 8 distinct debris components (connected components of debris colour, min
   area 0.01% viewport), not one merged mass. Count them and state the number. Guards G4/G6 must still
   pass.

### P2 — Case 4: one puck on the arc, correct rim and stack

9. **(Regression — duplicate puck)** Exactly one puck at all times. Likely cause: the builder spawns a
   new puck without destroying/repositioning the authored one, or the capture starts mid-flight with
   the rest pose still visible. Fix the cause, not the frame. Acceptance: guard G5 (component count = 1)
   on every one of the 16 frames of `.plan-build/verify/Buca/`.
10. **(Flight path)** At the beat where the reference has the puck hugging the rim (ref t≈1.00,
    `ref_1.00s.png`), ours sat off the arc. Acceptance: in our nearest strip frame, distance from puck
    centre to the rim arc centreline ≤ 1.5 puck radii (measure both in px on the frame). If the arc
    geometry constant is wrong, fix the WORLD constant in `Case4SceneSetup.cs`; never nudge the camera.
11. **(Rim thickness — untouched)** Ours is chunky; the reference rim is thinner. Measure ref rim
    stroke thickness in px off `ref_0.00s.png` (perpendicular to the arc at 3 points), then match.
    Acceptance: our thickness within ±25% of ref at the same 3 sample points in frame_00.
12. **(Green stack too short — untouched)** Reference stack bbox: x 0.087–0.346, y 0.336–0.463
    (`REFERANS-ANALIZ.md` §4.1). Acceptance: our green-hue stack bbox in frame_00 matches within 0.02
    viewport per edge — especially the top edge y≈0.463.
13. **(U3)** Debris hue walk green → mustard → red → magenta across ref t 2.30–3.40. Acceptance:
    dominant debris hue sampled in our frames nearest ref t=2.4/2.8/3.1/3.4 moves monotonically through
    the hue bands (state the four hue values in degrees).
14. **(U4)** Puck rest y = viewport 0.361 (last measured ours 0.352). Fix the world constant;
    acceptance: measured puck centre y in frame_00 = 0.361 ± 0.004.
15. **(U1/U2 re-verify under the guard)** Coin stream exits top-right off-screen from the contact
    point; puck trail present over the flight frames. These passed in round 3 — they are now guard
    items: re-measure, don't assume.

## PER-FIX LOOP (mandatory)

reset scene (`git checkout -- <scene>`) → edit code → `BuildAndCapture` (no `-quit`) → grep the run's
own log → measure the acceptance number on the new frames → run the FULL regression guard → write the
status line in evidence-contract form with frame filenames.

## DEFINITION OF DONE

- **Case 2:** B5 lips restored with measured thickness+dE; B7 black rectangles zero-count; B8 cyan bar
  bbox matched; B9 bezel matched; B4 shard count ≥ 8; guards G4/G6 green. Numbers + frame files for
  every line.
- **Case 3:** zero label components in top/bottom crops; no >2% pure-white blob in the sticker ROI; S1
  composition matches ref t=0.75 (silhouette bboxes within 0.02 viewport, `?` visible); S2 sparks
  visible in the flight frames (non-zero spark-coloured pixel count along the path, state it); G8 scene
  hash stable.
- **Case 4:** exactly one puck in all 16 frames; puck on-arc at the matched beat; rim and stack matched
  to ref measurements; U1–U4 all re-measured with numbers.
- **Global:** all 8 guards green, measured this round, on this round's artefacts; deviation docs
  (`docs/verify/case{2,3,4}_deviation.md`) updated with evidence-contract lines; side-by-sides
  regenerated; committed and pushed to `upstream_case`.

## DO NOT

- Do not touch `Assets/Case1_FitTheShape/**` in any way.
- Do not add ANY UI: no labels, counters, badges, pips, banners, "LEVEL N COMPLETE". B6/S3/S4 are
  cancelled. If told to remove something, the proof is a zero-count in a crop of our frame — not a
  diff, not a claim.
- Do not report a code constant, serialized value, gate colour or log line as a measurement.
- Do not read `~/Library/Logs/Unity/Editor.log` for anything.
- Do not check only the thing you changed — the guard list runs every round.
- Do not run a capture with `-quit`; do not run batchmode while a GUI editor holds the project.
- Do not build without resetting the scene; do not let builder output become builder input.
- Do not lower a threshold or edit an expected value to green a gate.
- Do not invent numbers. Where this plan says "measure it": crop the named ref frame, run the
  connected-component / band-sampling measurement, state the value, then match it.
