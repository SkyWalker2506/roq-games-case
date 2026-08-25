# Case 2 Contract - Block Hole

## Interaction

`DRAG CROSS -> MATCHING CROSS HOLE -> SNAP -> BREAK IN PLACE -> FALL DOWN -> HOLE CLOSES`

## Hero and identity

- Hero: purple `Cross` block.
- Start role: lifted draggable block with a contact shadow.
- Target role: purple Cross-shaped hole in the lower-left playfield.
- Required set: red `L`, green `Square`, cyan `Two`, purple `Cross`; each has exactly one block and one matching hole.
- Identity owner: `BlockShapeId`, assigned by Case 2 wiring. Object names are discovery/migration data, not the runtime match authority.

## Authority

- Placement and camera: `Assets/Case2_BlockHole/Scenes/BlockHole.unity`.
- One-time reference layout: `Case2SceneAuthoring.ApplyReferenceLayout`; it is not called by gameplay gates or capture.
- Materials and runtime wiring: `Case2SceneSetup.Build`.
- Runtime position while held: `BlockDragController` only.
- Runtime break/fall: `BlockShatterSink` only.

## Reference manifest

- Video: `/Users/musabkara/Downloads/Block Hole.mp4`
- SHA-256: record in final reference manifest generated with the delivery media.
- Resolution/duration: 1080x1728, 14.447 s.
- Measured hero chain:
  - `0.00-0.70`: Cross drag.
  - `~0.75`: Cross seated in its hole.
  - `0.80-1.10`: break remains inside the Cross footprint.
  - `1.10-1.90`: fragments travel downward and darken into the shaft.
  - `~1.95`: hole closes.

## Required proof

- Structural: exactly four stable block IDs, four matching hole IDs, one controller per block, no stale root controller.
- Functional: wrong hole does not glow or consume; matching Cross drop completes.
- Visual: thick coloured hole lip, dark shaft, purple Cross mass retained at fracture, no white smoke cloud.
- Temporal: scripted sequence approximately 1.95 s; snap near 0.75 s, break ending near 1.10 s, fall ending near 1.90 s.
- Capture: 1080x1728 and visually inspected beside event-aligned reference frames.
- Idempotence: wiring run twice produces the same Case 2 files and does not move camera/blocks/holes.

## Out of scope

- Level progression and puzzle generation.
- Functional booster, timer, currency, settings or other metagame systems.
- Case 1 files and scenes.
