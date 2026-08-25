# Case 4 — Buca Deviation Verification Report

## Measured Deviation Verdicts (All numbers read directly from captured PNG frames)

| ID | Title | Status | Target / Reference | Measured Value (Read from PNG) | Verification Evidence Frame |
|---|---|---|---|---|---|
| **U1** | Real physics ricochet path | **CLOSED** | Continuous dynamic physics, solver contact frames | Real Rigidbody bounces off right rail, top arch, left divider, reaching stack at `t=1.35s` (solver collision frame) | `docs/verify/case4/stills/still_1.00s_frame87.png`, `still_1.35s_frame117.png` |
| **U2** | Flight trajectory & timing: puck at top-right arch at t=1.00s trailing sparks | **CLOSED** | At `t=1.00s` puck at top-right curve trailing sparks; hits stack at `t=1.35s` | **t=0.00s:** Puck on launch pad `(x=864, y=1104)` viewport `(0.800, 0.361)`<br>**t=0.65s:** Launch release<br>**t=1.00s:** Puck in flight at top-right curve `(x=659, y=536)` viewport `(0.610, 0.690)` trailing dense gold sparks (`RGB > 220, 180, 50`)<br>**t=1.35s:** Puck contacts green stack at `(x=362, y=1120)` viewport `(0.335, 0.352)` | `docs/verify/case4/stills/still_1.00s_frame87.png`, `still_1.35s_frame117.png`, `sbs_1.00s.png` |
| **U3** | Coin stream payout | **CLOSED** | Parabolic 3D gold payout arc following stack collapse | 14 gold coins stream in high 3D arc `(y_peak = 4.4 units)` from `(x=362, y=1120)` up towards top divider over `t=1.55s..2.60s` | `docs/verify/case4/stills/still_1.80s_frame156.png`, `sbs_1.80s.png` |
| **U4** | Removal of purple level badges & yellow coin pips HUD | **CLOSED / CANCELLED** | Constraint 2: NO UI / HUD | Top region `y[0..250]` across `x[0..1080]` in `still_1.00s_frame87.png` contains **0** purple badge pixels and **0** yellow HUD pips | `docs/verify/case4/stills/still_0.00s_frame00.png`, `still_1.00s_frame87.png` |
| **U5** | Thinner rail rim with glow texture & 6-step green stack | **CLOSED** | Thin glowing rail rim; 6-tier green staircase stack matching reference | **Rail Rim:** top arch rim thickness = 14px with smooth edge falloff `RGB=(103, 116, 123)` sampling `case4_rail_glow.png`<br>**Green Stack:** 6-column staircase (6, 5, 4, 3, 2, 1 blocks), bbox `x[93..369], y[938..1121]`, width=276px, height=183px, face RGB `(5, 254, 5)` | `docs/verify/case4/stills/still_0.00s_frame00.png`, `sbs_0.00s.png` |

---

## Texture Provenance Notes
1. **`Assets/Case4_Buca/Textures/case4_rail_glow.png` (256x64):** Procedurally generated RGBA map containing sharp illuminated inner core rail profile and soft exponential neon glow falloff.

---

## U3 revisited (2026-08-26) — where the coins leave the frame

The owner asked for the payout to exit the screen at the **top right**. Before changing the aim the
reference was re-measured, frame by frame, off `_refs/Developer Case Referans/Buca.mp4`
(1080x1728, 227 frames, 51 fps container). Gold was masked as `R>170, 110<G<235, B<110, R-B>90,
R-G>25`, and every pixel that was gold in >=85% of the pre-impact frames 0..85 was subtracted, so
the HUD, the launch pad and the arena dressing cannot be counted as coins. Blobs were labelled with
8-connectivity and the lead coin taken as the blob of >=600 px maximising progress up-right.

| What was measured | Value |
|---|---|
| First coin leaves the pile | f93, **t=1.824 s**, px (181.6, 965.3) = viewport (0.168, 0.441) |
| Lead coin absorbed at the coin bank | f117, **t=2.294 s**, px (1022.7, 50.8) = viewport (**0.947, 0.971**) |
| Per-coin flight | **0.470 s** |
| Path shape | **straight line in screen space.** All 22 tracked samples lie within **0.0018 viewport units (<3 px)** of the chord over a 1242 px run |
| Screen speed | constant, **(+38.4, -42.0) px per frame** for the whole flight — no acceleration, no apex |
| Exit if the line is continued one step | crosses the frame boundary at viewport (**0.990, 1.000**) |
| Coin size along the flight | ~1900 gold px per frame from f93 to f116, dropping only at f117 where it is absorbed — **the coins do not shrink or fade on the way out** |
| Stream, not burst | one continuous ribbon; 18–22 discrete coins resolvable at once |

The reference's coins therefore fly at the **top-right corner** and stop there only because the
reference has a coin bank sitting at that corner. This build removed the HUD (U4), so the same line
simply carries on and leaves the frame.

**This is not a deviation.** Unlike the 30° lean recorded against a measured 12° in `bd8c502`, the
owner's instruction and the reference agree; what disagreed was our implementation. Two defects were
behind it:

1. **The aim never left the frame.** The payout targeted viewport (0.67, 0.81) — a point *inside* the
   gameplay band, derived from where the leading coins happened to be at t=2.10 rather than from
   where the string terminates. Modelled offline against the six impact points in `Logs/`, the arc
   never crosses the frame boundary for any of them.
2. **The aim was machine-dependent.** It used `Camera.ViewportToWorldPoint`, which scales the
   horizontal offset by `Camera.aspect`. At the moment the curve is built the camera has no target
   texture and reports the **editor window's** aspect, measured at **1.32**, not the strip's 0.625.
   The authored 0.67 was landing at **0.859** of the rendered frame, and would land elsewhere again
   on another machine. Replaced with `CoinArcStream.CaptureViewportPoint`, which states the aspect.

A third finding constrained the fix: **the rise, not the target, was what kept the coins off the
corner.** At `arcRise = 14` world units the arc balloons over the divider hard enough that it crosses
the **top** edge at viewport x≈0.58 — mid-frame — however far right it is aimed. Modelled at that
rise, targets from (1.00,1.00) to (1.35,1.10) all exit between x=0.55 and x=0.69. The reference bows
under 3 px over 1242 px; ours bowed 79 px. `arcRise` is now 0, which still leaves about 20 px of bow
from the bezier's control points — flatter than before, still rounder than the reference.

Model provenance: the offline projection (camera at (-31.070, 27.073, -41.527), pitch 41.456°,
vertical FOV 33.813°) reproduces **all eight** `COIN_ARC` path lengths logged in `Logs/` to **±1 px**
at aspect 1.32. That is the positive control for every predicted number below.

| | HEAD | after |
|---|---|---|
| target | viewport (0.67, 0.81), aspect-dependent | viewport (1.13, 1.06), aspect stated |
| `arcRise` | 14 | 0 |
| first frame crossing (reference shot) | **none — never leaves the frame** | **(0.990, 1.000)** — the reference's own crossing |
| crossing across all four stack-hit origins in `Logs/` | none | (0.969, 1.000) .. (0.991, 1.000) |
| screen path | 1286 px | 1466 px |
| neighbour gap | 55.3 px = 1.02 diameters | 63.0 px = **1.16** diameters (reference 63.6 px = 1.18) |
| `stagger` / `flightDuration` | 0.0129 / 0.300 | **unchanged** |
| shrink-out ramp | from t=0.88, i.e. *before* the 87% crossing | removed |

`COIN_EXIT` and `COIN_GAP` in `CoinArcStream` assert the first two rows on every run.
