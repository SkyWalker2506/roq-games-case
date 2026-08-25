# Case 3 — Stickerdom Deviation Verification Report

## Measured Deviation Verdicts (All numbers read directly from captured PNG frames)

| ID | Title | Status | Target / Reference | Measured Value (Read from PNG) | Verification Evidence Frame |
|---|---|---|---|---|---|
| **S1** | Real page curl on peel with white back | **CLOSED** | White back exposed during peel, roll radius matching reference | At `t=1.05s`, curled flap extends to `(x=620, y=860)` exposing blank white paper back (`RGB > 240, 240, 240`) before flight launch | `docs/verify/case3/stills/still_1.05s_frame143.png`, `sbs_1.05s.png` |
| **S2** | Sparkle star flight trail | **CLOSED** | Yellow-lime star particles trailing white curled back | Yellow-lime star particle cluster at viewport `(0.433, 0.639)` `(x=468, y=624)` with sampled particle RGB `(255, 238, 90)` trailing flight path | `docs/verify/case3/stills/still_1.25s_frame170.png`, `sbs_1.25s.png` |
| **S3** | Target card UI (Cat label & 1/5 counter) removal | **REOPENED (name), CANCELLED (counter)** | Owner, on the reward-card row: "isim yazmadi" - the name was not written | The name is not HUD: the reference draws it INTO the card, on a paperclip-pinned tab across the card's top edge, and it fades in with the card. It is back as card art - measured name ink in the tab strip 10.3-17.4% against the reference crops' 15.6-26.4% (`tools/case3_gate.py cards`). The separate `1/5` counter stays cancelled under Constraint 2; the card's empty bottom band is ours, not the reference's, and removing it is the owner's call. | `Assets/Case3_Stickerdom/Textures/Reference/card_filled_*.png` |
| **S4** | Bottom theme shelf & top level/hearts HUD removal | **CANCELLED** | Constraint 2: NO UI / HUD | Top band `y[0..140]` mean RGB `(201, 173, 132)` and bottom band `y[1540..1728]` mean RGB `(216, 182, 143)` contain 0 baked HUD badges, hearts, or tool shelf icons | `docs/verify/case3/stills/still_0.00s_frame00.png`, `sbs_0.00s.png` |
| **S5** | Beat alignment: idle delay & flight timing | **CLOSED** | At `t=0.75s` Cat on page; flight `t=1.05..1.40s`; pop `t=1.55s` | **t=0.00s:** Cat at home strip pos `(x=672, y=936)` viewport `(0.622, 0.458)`<br>**t=0.75s:** Cat still on page at `(x=672, y=936)`, tap anticipation begins<br>**t=1.05s:** Peel completes, launch begins<br>**t=1.25s:** Mid-flight at `(x=468, y=624)` viewport `(0.433, 0.639)`<br>**t=1.40s:** Reaches card slot `(x=240, y=362)` viewport `(0.222, 0.790)`<br>**t=1.55s:** Card flip & pop overshoot active<br>**t=1.75s:** Card settled rest | `docs/verify/case3/stills/still_0.75s_frame102.png`, `still_1.25s_frame170.png`, `still_1.55s_frame211.png` |

## Summary of Fixes Applied in Round 2
1. **Sequence Beat Alignment (S5):** Added `idleDelay = 0.75s` to `Case3Director.cs`, shifting peel, flight, flip, and settle to match the reference video timestamps.
2. **Card Texture UI Removal (S3):** Cleaned `card_empty_*.png` and `card_filled_*.png`, removing the "Cat" text label and "1/5" counter.
3. **Baked HUD Removal (S4):** Cleaned `stickerdom_base_final.png` by painting over the top level/hearts band (`y=0..140`) and bottom tool shelf (`y=1540..1728`) with seamless table/wall background.
