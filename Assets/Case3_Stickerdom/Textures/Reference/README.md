# Case 3 Stickerdom reference extraction manifest

These assets are pixel-faithful crops from `_refs/Developer Case Referans/Stickerdom.mp4` (SHA-256 `db01a86d884fbdb30b5f35987a87125d6e37fc7c409673264f4ce55cec9ae7e7`). No generated artwork is mixed into them.

## Coordinate convention

- Reference canvas: `1080 x 1728` pixels.
- Origin: top-left; +X points right and +Y points down.
- ROIs use half-open notation: `(x0,y0)-(x1,y1)`.
- To reproduce the reference registration, place each crop's top-left at its ROI `(x0,y0)` with no scaling.

## Asset/source table

| Asset | Size | Source time | Source ROI / placement | Notes |
| --- | ---: | ---: | --- | --- |
| `stickerdom_base_final.png` | 1080 x 1728 RGB | 8.400 s | `(0,0)-(1080,1728)` | Single untouched final-state video frame; it is **not** a multi-time composite. 8.900-9.000 s was rejected because the recording shows a blue touch marker over the gear. Includes the reference's black pencil overlay at upper-right. |
| `card_empty_01.png` | 244 x 292 RGBA | 0.000 s | `(24,145)-(268,437)` | Empty Cat card. Rounded card alpha plus a very small top-left restoration mask that hides the filled-card paperclip when layered over the final base. Coordinate-locked. |
| `card_empty_02.png` | 244 x 292 RGBA | 0.000 s | `(286,145)-(530,437)` | Empty Noodle card; same coordinate-locked restoration treatment. |
| `card_empty_03.png` | 244 x 292 RGBA | 0.000 s | `(548,145)-(792,437)` | Empty Sweets card; same coordinate-locked restoration treatment. |
| `card_filled_cat.png` | 244 x 292 RGBA | 3.000 s | `(24,145)-(268,437)` | Cat card after the peel/reward settles. |
| `card_filled_noodle.png` | 244 x 292 RGBA | 6.000 s | `(286,145)-(530,437)` | Noodle card after the peel/reward settles. |
| `card_filled_sweets.png` | 244 x 292 RGBA | 8.400 s | `(548,145)-(792,437)` | Sweets card after the peel/reward settles. |
| `sticker_cat.png` | 308 x 311 RGBA | 0.000 s | `(622,777)-(930,1088)` | Playable Cat layer. |
| `sticker_noodle.png` | 366 x 283 RGBA | 3.200 s | `(292,997)-(658,1280)` | Playable Noodle layer, sampled after Cat settles and before Noodle peel begins. |
| `sticker_sweets.png` | 239 x 277 RGBA | 6.000 s | `(421,852)-(660,1129)` | Playable Sweets layer, sampled after Noodle settles and before Sweets peel begins. |
| `preview_initial_reconstruction.png` | 1080 x 1728 RGB | composite QA | full canvas | Verification-only image: base at 8.400 s + empty cards at 0.000 s + Cat at 0.000 s + Noodle at 3.200 s + Sweets at 6.000 s. Do not use this flattened preview as the interactive scene. |

## Extraction and alpha notes

- Cards were cropped at their stable full-card bounds. Their outer rounded rectangles are antialiased; the three empty cards retain a tiny coordinate-specific background repair at the top-left so the final base's colored paperclip tips do not leak through.
- Playable stickers were isolated from their original frames using the closed bright sticker outline as a barrier, selecting the enclosed component, removing thin neighbouring-outline branches, then applying a sub-pixel antialias blur. Alpha corners are zero and the opaque art remains source-resolution.
- Transparent pixels intentionally retain source RGB. Unity's `Alpha Is Transparency` import setting prevents dark fringes during filtering.
- The base already contains the final HUD, cards, page, tool band and reference color grade. Avoid applying a second strong post grade to these baked reference pixels.

## Unity import contract

Runtime RGBA crops (`card_*` and `sticker_*`) are imported as `Sprite (2D and UI)`, single sprite, sRGB, bilinear, clamp, mipmaps off, `Alpha Is Transparency` on, max size 2048 and uncompressed on the default platform. `stickerdom_base_final.png` uses the same sprite settings but has no alpha channel. Generated `.meta` files are kept beside the PNGs so GUIDs stay stable.

At native 1080 x 1728 registration, use a common pixels-per-unit or UI scale for the base and every crop. Do not independently fit the sticker bounds; use the table's exact top-left coordinates. `preview_initial_reconstruction.png` is QA-only and does not need runtime loading.
