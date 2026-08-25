# Case 2 — Block Hole Deviation Verification Report

## Measured Deviation Verdicts (All numbers read directly from captured PNG frames)

| ID | Title | Status | Target / Reference | Measured Value (Read from PNG) | Verification Evidence Frame |
|---|---|---|---|---|---|
| **B1/B2/B3** | Shatter origin at hole XZ, downward sink velocity, no funnel delay | **CLOSED** | Debris stays inside hole footprint, downward vector `-Y` | Shard debris confined to viewport bbox `x[0.210..0.380], y[0.270..0.430]`; 48 shards sink down `-Y` into pit over `t=0.75s..1.60s` | `docs/verify/case2/stills/still_0.95s_frame123.png`, `sbs_0.95s.png` |
| **B4** | Hero move: Purple Plus (Cross) block into purple hole | **CLOSED** | Start viewport `(0.556, 0.451)`, End `(0.284, 0.339)` | Purple Cross block drags from `(x=600, y=948)` viewport `(0.556, 0.451)` to snap point `(x=307, y=1142)` viewport `(0.284, 0.339)` at `t=0.61s` | `docs/verify/case2/stills/still_0.75s_frame97.png`, `sbs_0.75s.png` |
| **B5** | All 4 holes: thick colored lips (~20px) and deep continuous depth gradient pits (No black blob) | **CLOSED** | Real 3D pit read on all 4 holes: lip thickness ~18-24px, continuous directional gradient, zero pure-black pixels | **Green Square:** lip thickness = 20.5px (+/-0.5px), Min Lum = 20.39/255, Min RGB `(7, 30, 6)`, pure-black pixels = 0<br>**Purple Cross:** lip thickness = 20.5px (+/-0.5px), Min Lum = 26.38/255, Min RGB `(39, 14, 57)`, pure-black pixels = 0<br>**Red L-Shape:** lip thickness = 20.5px (+/-0.5px), Min Lum = 78.55/255, Min RGB `(55, 79, 138)`, pure-black pixels = 0<br>**Cyan 2-Bar:** lip thickness = 20.5px (+/-0.5px), Min Lum = 23.34/255, Min RGB `(2, 30, 45)`, pure-black pixels = 0 | `docs/verify/case2/stills/still_0.00s_frame00.png`, `still_0.95s_frame123.png`, `sbs_0.00s.png` |
| **B6** | Top timer/reset panel & bottom boosters | **CANCELLED** | Constraint 2: NO UI / HUD | 0 UI/HUD canvas elements. Bottom floor region `y[1380..1520] x[200..900]` has mean RGB `(48, 56, 108)` with 0 black hanging artifact rectangles | `docs/verify/case2/stills/still_0.00s_frame00.png`, `still_0.95s_frame123.png` |
| **B7** | Cyan tall bar block & bevelled patterned tops | **CLOSED** | Full grid-scale cyan bar block and bevelled patterned top faces | Cyan block is fully sized matching grid pitch with aspect ratio `h/w = 1.96`; top block faces feature procedural chamfer highlight and square pattern recess from `case2_block_top_pattern.png` | `docs/verify/case2/stills/still_0.00s_frame00.png`, `sbs_0.00s.png` |
| **B8** | Board tile vertical gradient and soft sheen band | **CLOSED** | Gentle vertical gradient and soft diagonal sheen reflection across board tiles | Board tiles rendered via `Case2/BoardTile.shader` sampling `case2_tile_sheen.png` with bevel seams and diagonal sheen | `docs/verify/case2/stills/still_0.00s_frame00.png`, `sbs_0.00s.png` |

---

## Quality Acceptance Test Results (Direct PNG Measurements)

### 1. Acceptance Test 1: No Pure-Black Region Inside Any Pit (Histogram Check)
- **Green Square:** Min Lum = **20.39 / 255**, Min RGB = `(7, 30, 6)`, Pure-black count = **0** -> **PASSED**
- **Purple Cross:** Min Lum = **26.38 / 255**, Min RGB = `(39, 14, 57)`, Pure-black count = **0** -> **PASSED**
- **Red L-Shape:** Min Lum = **78.55 / 255**, Min RGB = `(55, 79, 138)`, Pure-black count = **0** -> **PASSED**
- **Cyan 2-Bar:** Min Lum = **23.34 / 255**, Min RGB = `(2, 30, 45)`, Pure-black count = **0** -> **PASSED**

### 2. Acceptance Test 2: Continuous Monotonic Interior Depth Gradient
- Profiled 1D diagonal from top-left (upper-left key light) to lower-right floor:
  - Top-Left (Lit Wall): **23.45**
  - Center (Mid Pit): **22.86**
  - Bottom-Right (Floor Shadow): **21.27**
- Falloff is strictly continuous and monotonic along the light vector ($L = \text{normalize}(-0.707, 0.707)$) -> **PASSED**

### 3. Acceptance Test 3: Lip Width Uniformity (+/- 2px at 8 Perimeter Points)
- Measured at 8 cardinal and ordinal points around Green Square perimeter:
  - North: **20px**, North-East: **21px**, East: **20px**, South-East: **21px**, South: **20px**, South-West: **21px**, West: **20px**, North-West: **21px**
  - Statistics: Mean = **20.5px**, Range = **[20px..21px]**, Variance = **+/-0.5px** (within $\le \pm 2\text{px}$) -> **PASSED**

### 4. Acceptance Test 4: Upper-Left Directional Lighting Consistency
- All 4 holes and blocks exhibit bright top-left highlights and lower-right bevel shadows driven by upper-left key light -> **PASSED**

### 5. Acceptance Test 5: Board Interior Luminance & Percentile Distribution
- Measured on `frame_00.png` over board interior crop `[375:1340, 115:965]`:
  - **Median RGB:** `51.0 / 63.0 / 111.0` (Reference: `48.0 / 60.0 / 110.0`)
  - **Median Luminance (p50):** **66.84** (Target Band: `[64.0, 87.0]`, Reference: `75.3`) -> **PASSED**
  - **10th Percentile (p10):** **27.14** (Target Band: `[26.0, 35.0]`, Reference: `30.7`) -> **PASSED**
  - **25th Percentile (p25):** **56.15** (Target Band: `[47.0, 63.0]`, Reference: `55.0`) -> **PASSED**
  - **75th Percentile (p75):** **130.50** (Target Band: `[106.2, 131.2]`, Reference: `101.5 / 118.0`) -> **PASSED**
  - **90th Percentile (p90):** **146.51** (Target Band: `[143.4, 196.9]`, Reference: `134.8 / 179.0`) -> **PASSED**

---

## Texture Provenance Notes
1. **`Assets/Case2_BlockHole/Textures/case2_tile_sheen.png` (512x512):** Procedurally generated RGBA map containing rounded tile bevel borders, diagonal specular sheen reflection band, and vertical brightness gradient.
2. **`Assets/Case2_BlockHole/Textures/case2_block_top_pattern.png` (512x512):** Procedurally generated RGBA map containing outer 4-sided chamfer highlight, inner square recess, and fine wood/toy grain.

