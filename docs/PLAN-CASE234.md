# Cases 2, 3, 4 — implementation plan (round 2)

> For the agent that will execute it. **Verification first.** Every fidelity claim from the previous
> round is currently UNPROVEN: that round produced no captures of our own scenes, and its "0 errors,
> verified" was read off the GUI editor's log rather than a headless build — the project did not in
> fact compile (12 errors, two orphaned `_hud.ResetInstant()` calls). Do not repeat that.

**Repo:** `/Users/musabkara/Projects/roq-games-case` — Unity 6000.3.11f1, URP, **Gamma** colour space.
**Push remote:** `upstream_case` only. Never push to `origin`.
**Reference videos:** `_refs/Developer Case Referans/{Block Hole,Stickerdom,Buca}.mp4` (1080x1728).
**Measured facts:** `docs/REFERANS-ANALIZ.md` (deviation IDs B1-B6, S1-S4, U1-U4) and `docs/DERSLER.md`
(mistakes already paid for, with evidence). Read both before touching anything.

## Standing constraints — these override everything below

1. `Assets/Case1_FitTheShape/**` is FINISHED. Do not open, build, edit or capture it. Never run
   `Case1SceneSetup.*`. (`FrameStripCapture.CaptureAll` opens every scene including Case 1 — it only
   reads, never saves, which is acceptable.)
2. **NO UI anywhere in Cases 2-4.** Deviations **B6, S3 and S4 are UI and are CANCELLED** — not
   pending. Do not "helpfully" re-add HUDs, counters, labels, badges, banners or fake chrome. If you
   find any remaining UI element, remove it.
3. Prefer removing excess over adding features. The owner wants a cleaner, smaller, more faithful
   result.
4. Every fidelity claim must be MEASURED against the reference video, never eyeballed.

## Tooling — get these wrong and you lose hours

- Runner: `./tools/unity-run.sh -batchmode [-quit] -executeMethod <Class.Method> -logFile Logs/<name>.log`
  (the script adds `-projectPath` and serialises runs via `.plan-build/unity.lock.d`).
- **The project takes an exclusive lock.** Before any batch run: `pgrep -f "MacOS/Unity"`. If a GUI
  editor holds the project, a batch run kills it.
- **Anything that enters play mode (every `Capture*`) must run WITHOUT `-quit`** — `FrameStripCapture`
  exits by itself. Only `CaseBuild.CompileCheck` and plain `Build` runs take `-quit`.
- **"0 compile errors" is not success.** After every run also
  `grep -E "SETUP_FAILED|Exception|error CS|FAILED" Logs/<name>.log`.
- **The builders are not idempotent.** Before every clean build: `git checkout -- <scene>`. After every
  build run `git status` and confirm INPUT assets (base prefabs, materials) did not change — only the
  scene output may.
- Unity CLI 1.0.0-beta.5 (`unity`) can drive a RUNNING editor via `unity command` / `unity eval`
  without the batch lock, if `unity pipeline install` has been run in the project.
- Capture output: `.plan-build/verify/<SceneName>/frame_00..NN.png` + `report.json`, 1080x1728 — the
  same size as the reference, so comparisons are fair.

**Entry points (verified in code; top-level classes, no namespace prefix):**

| Purpose | `-executeMethod` | `-quit`? |
|---|---|---|
| Compile check | `CaseBuild.CompileCheck` | yes |
| Case 2 build | `Case2SceneSetup.Build` | yes |
| Case 2 build + 16-frame strip | `Case2SceneSetup.BuildAndCapture` | **no** |
| Case 2 dense video (254 frames) | `Case2SceneSetup.CaptureBlockHoleVideo` | **no** |
| Case 3 validate/wire + strip | `Case3SceneSetup.BuildAndCapture` | **no** |
| Case 4 build + strip | `Case4SceneSetup.BuildAndCapture` | **no** |
| Case 4 layout gate | `Case4SceneSetup.LayoutGate` | **no** |

Case 3's builder contract: the scene is AUTHORED; `Case3SceneSetup.Build` only validates and wires.
Never make it produce layout, sprites or camera. Two consecutive runs must leave the scene hash equal.

---

## Phase 1 — Prove the current state (no code changes)

1. **Reset + compile.** `git checkout -- Assets/Case2_BlockHole/Scenes/BlockHole.unity` (inspect the
   diff first — it is currently dirty), then `CaseBuild.CompileCheck`, then grep the log.
2. **Capture our own strips**, one run per case, scene reset before each:
   `Case2SceneSetup.BuildAndCapture`, `Case3SceneSetup.BuildAndCapture`, `Case4SceneSetup.BuildAndCapture`.
   Grep each log for `SETUP_FAILED|Exception|FAILED|TIMEOUT|NONDETERMINISTIC`. Confirm 16 PNGs +
   `report.json` under `.plan-build/verify/{BlockHole,Stickerdom,Buca}/`.
3. **Extract reference frames at the interaction beats** (timestamps from `docs/REFERANS-ANALIZ.md`):
   - Block Hole: t = 0.00, 0.40, 0.75, 0.95, 1.30, 1.60, 1.95, 2.40 s
   - Stickerdom: t = 0.75, 0.90, 1.05, 1.15, 1.25, 1.40, 1.55, 1.75 s
   - Buca: t = 0.00, 0.65, 1.00, 1.35, 1.55, 1.80, 2.10, 2.60, 3.00, 3.40 s
   `ffmpeg -ss <t> -i "<video>" -frames:v 1 docs/verify/case<N>/ref/ref_<t>.png`
4. **Side-by-side contact sheets at identical size.** `report.json`'s `totalDuration` maps frame index
   to seconds: `t = i * total/(N-1)`. Pair each reference beat with the nearest strip frame and hstack
   them into `docs/verify/case<N>/side_by_side/`.
5. **Measure, do not eyeball.** Reuse the method used for the reference analysis (connected-component
   bbox on hue/sat/value masks; see `.plan-build/refanalysis/measure_*.json`). Per deviation produce a
   number: viewport bbox, CIE76 dE over the relevant region, particle presence/count in a crop.
   Compare the same object with the same metric — never an aggregate across different framings.
6. **Write the verdict** into `docs/verify/case2_deviation.md`, `case3_deviation.md`,
   `case4_deviation.md`: per ID, measured status (closed/open), the number, and the pair image proving
   it.

**Acceptance:** three clean strips, a measured table per case, contact sheets committed. No source
edits in this phase.

### Deviation status going in (claimed, unproven — confirm each)

| ID | Claim | What to verify |
|---|---|---|
| B1/B2/B3 | fixed (shatter origin locked to hole XZ, initial velocity down, funnel window removed) | across ref t≈0.80-1.90 the debris bbox stays inside the hole footprint (ref hole x 0.100-0.469, y 0.228-0.450) and sinks |
| B4 | Cross is the hero move (`Case2Director.cs`) | our first interaction is purple plus → purple plus hole (ref centres (0.556, 0.451) → (0.284, 0.339)) |
| B5 | thick lip + depth gradient edited into the shader | crop a hole, ours vs ref: lip thickness in px, lip colour dE. "thin line + black interior" = still open |
| B6 | UI — **CANCELLED** | confirm no HUD objects remain |
| S1 | **OPEN** | page must be the drawn scene: placed colour stickers + dark silhouettes carrying `?` for unfilled slots. Manifest: `Assets/Case3_Stickerdom/Textures/Reference/README.md` |
| S2 | sparkle trail implemented | frames over ref t=1.05-1.25 must show yellow-green sparks along the path, on top of the page |
| S3/S4 | UI — **CANCELLED** | — |
| U1 | coin payout retargeted off-screen top-right | frames over ref t=1.55-2.10: the arc leaves the contact point and exits top-right; no coins dying at a phantom target |
| U2 | puck trail enabled | frames over ref t=0.65-1.35 show a spark trail behind the puck |
| U3 | debris colour evolution | dominant debris hue at t≈2.4/2.8/3.1/3.4 walks green → mustard → red → magenta |
| U4 | puck y should be 0.361 (ours 0.352) | measure the puck centre in our t=0 frame |

Scene-serialized values override C# defaults: a code edit is not live until the builder rewrites the
scene, so only a `BuildAndCapture` is a trustworthy test.

---

## Phase 2 — Case 2 (Block Hole), priority order

Only what Phase 1 measured as open. Per fix: reset scene → edit builder/runtime code → `BuildAndCapture`
→ re-measure → side-by-side.

1. **B4 (P0):** make the purple plus the played piece, (0.556, 0.451) → (0.284, 0.339). Acceptance:
   the first-move frames show the plus dragged and seated at ~ref 0.75 s, and the debris bbox area is
   within ±30% of the reference at the matched beat.
2. **B5 (P2):** hole lip and pit gradient. MEASURE the reference lip thickness and colour off a ref
   t=0.00 crop first — do not invent px values. URP SRP Batcher needs identical `UnityPerMaterial`
   across passes or you get silent magenta. Gamma space: material values are raw sRGB, no conversion.
3. **B1/B2/B3 regression check** after any change: debris never rises and never spawns outside the
   hole footprint.
4. **B6: do nothing.**

## Phase 3 — Case 3 (Stickerdom)

1. **S1 (P1, the big one):** the page must read as the reference's drawn scene — placed colour stickers
   plus dark silhouettes with `?` for unfilled slots, not a random collage. Use the established method:
   one untouched reference base frame as background + measured alpha layers for the interacting
   elements only, source time and ROI recorded in the manifest. Acceptance: our t≈0 frame vs ref t=0.75
   — silhouette bboxes within 0.02 viewport, `?` marks visible; the peel → flight → stick chain still
   plays; `Case3SilhouetteGate.SilhouetteGate` passes; two consecutive `Case3SceneSetup.Build` runs
   leave the scene file hash identical.
2. **S2 (P2):** confirm the sparkle trail actually reads in the flight frames. If it does not, check the
   four causes separately — size, depth/sorting, contrast against the page, lifetime — rather than
   guessing one. Rebuild, do not merely recapture.
3. **S3/S4: do nothing.** The card filling with the cat image stays; the label and `1/5` counter do not.

## Phase 4 — Case 4 (Buca)

1. **U1 (P1):** the coin stream must visibly fly from the contact point and exit top-right, not
   evaporate at a deleted HUD's phantom position. Verify the bezier endpoint really is off-screen
   (viewport x>1.0, y>1.0) by measuring the arc's exit edge in our frames. Keep the contact-armed
   payout contract intact (the `COIN_GATE`/`PROOF` log lines).
2. **U2 (P2):** puck spark trail over t 0.65-1.35, both layers (soft plume + gold stars, shared seed).
3. **U3 (P2):** debris hue walk across t 2.30-3.40. Acceptance: the dominant debris hue sampled at four
   beats moves monotonically through those bands.
4. **U4 (P2):** puck rest y → viewport 0.361 (now 0.352). Fix the WORLD constant in
   `Case4SceneSetup.cs`, never nudge the camera; the screen measurement is verification only.
   Acceptance: measured puck centre y = 0.361 ± 0.004, with `LayoutGate` and `RefPositionGate` still
   green.
5. **No "LEVEL 6 COMPLETE" banner** — UI.

## Phase 5 — Deliverables (video + stills)

Create and commit:

```
docs/verify/case2/{final.mp4, stills/, side_by_side/, ref/}
docs/verify/case3/{...}
docs/verify/case4/{...}
```

1. Dense frames: Case 2 via `CaptureBlockHoleVideo` (254 frames ≈ ref 130 fps over 1.95 s). For Cases
   3 and 4 add two small wrapper methods mirroring that pattern (`SetFrameCount(round(ref_fps *
   duration))`; Stickerdom 120 fps, Buca 51 fps), or run `FrameStripCapture.CaptureAllVideos`
   (180 frames per scene). All without `-quit`.
2. Assemble: effective fps = `(N-1)/totalDuration` from `report.json`;
   `ffmpeg -framerate <fps> -i .plan-build/verify/<Scene>/frame_%02d.png -pix_fmt yuv420p -crf 18
   docs/verify/case<N>/final.mp4` (match the real filename padding).
3. Stills: copy the beat-matched frames used for the side-by-sides into `stills/`.
4. Commit the deliverables and the updated deviation docs; push to `upstream_case`.

## Definition of done

- **Case 2:** B1-B5 measured closed with numbers and pair images; no UI; strips deterministic;
  `final.mp4` + stills committed.
- **Case 3:** S1 closed (silhouette + `?` page, composition measured against ref t=0.75); S2 visible in
  the flight frames; builder idempotent (scene hash stable across two runs); no UI; deliverables in.
- **Case 4:** U1-U4 measured closed; contact-armed payout proof intact; layout gates green; no UI;
  deliverables in.
- **Global:** `CaseBuild.CompileCheck` clean; every log grepped for `SETUP_FAILED|Exception`;
  `git status` shows no unintended input-asset changes; Case 1 untouched — prove it with
  `git diff --stat <base>..HEAD -- Assets/Case1_FitTheShape` returning empty.

## What NOT to do

- Do not touch `Assets/Case1_FitTheShape/**` in any way.
- Do not add B6, S3, S4 or any other UI — they are cancelled, not pending.
- Do not push to `origin`.
- Do not trust "0 compile errors", a green gate, or a duration report as proof. Only measured
  side-by-side frames count.
- Do not derive object placement from the camera, and do not chase pixel targets with a solver: fix the
  world constant, then verify on screen.
- Do not run a capture with `-quit`, and do not run batchmode while a GUI editor holds the project.
- Do not build without resetting the scene first; do not let a builder's output become its own input.
- Do not lower a threshold or edit an expected value to turn a red gate green.
- Do not invent numbers. Where this plan says "measure it", extract the reference frame and measure.
