# AI Usage Report

## Confirmed decisions

- M0 source approval is recorded in `Docs/Decisions/M0_SOURCE_APPROVAL.json`.
- Pixel-art calibration is confirmed in `Docs/Decisions/STYLE_DECISION.json`.
- M1 audio is confirmed as repository-local procedural synthesis only in `Docs/Decisions/OPEN_A3_AUDIO.json`.
- External recordings, externally generated audio, and AI audio assets are prohibited.
- Six deterministic procedural WAVs were generated under `Assets/_Project/Audio/M1Functional/`. Each event record under `Docs/AI_Usage/generations/` binds the generator source SHA, Unity version, UTC, seed, parameters, WAV path/SHA, ordered modifications, and `pending-user-gate` reviewer state. Generation is complete; human audio approval is not.

## Functional-event provenance contract

Each generated WAV for the following six functional events requires a separate record. Every record must contain actual values, not placeholders, for: generator name; tool name and version; generator source SHA-256; generation UTC; seed; parameters or instruction; original WAV path and SHA-256; ordered modifications; final WAV path and SHA-256; and reviewer.

| Event requiring its own record |
| --- |
| `DasherReady` |
| `ArcherReady` |
| `AttackLocked` |
| `PlayerHit` |
| `SoulCollected` |
| `ExitOpened` |

## Recording rules

- Preserve both the original and final WAV files and calculate SHA-256 from their actual bytes.
- Record modifications as an ordered list, including the operation and its sequence.
- Use `asset_manifest.csv` for the checklist-defined asset fields. Put the procedural generator/tool/version, seed, parameters or instruction, original and final WAV provenance, ordered modifications, and reviewer in the associated event record; reference that record from the manifest notes.
- The local M1 validation build may exercise a provenance-complete WAV while its reviewer state is `pending-user-gate`, but the audio evidence lane and final candidate gate cannot pass until the required human review is recorded.

## M1 visual reference production

- Ten review-only pixel-art reference PNGs were generated with `god-tibo-imagen 0.3.1`, `gpt-5.4`, and the `private-codex` provider under `Docs/Style/References/`.
- The set contains a master style board, player/Dasher/Archer/Minion sheets, a relative scale sheet, Haste/Giant rules, Dasher/Archer telegraph rules, M1 dungeon environment rules, and M1 UI rules.
- Exact submitted prompts are stored under `Docs/AI_Usage/prompts/`. Per-image response IDs, requested and actual dimensions, input references, paths, review state, and SHA-256 values are stored under `Docs/AI_Usage/generations/` and `asset_manifest.csv`.
- The visual and technical review is recorded in `Docs/AI_Usage/edits/visual_reference_review_v001.json`.
- Every generated image remains `review-required`. These opaque reference boards are not transparent Unity-ready sprites and do not replace `Assets/_Project/Art/M1Representative/`.
- M1 scope was enforced: no Echo sheet, Golem, hazard, destructible pillar, final-room, or marketing asset was generated.

## M1 Unity production sprites

- Nine isolated chroma-key source PNGs were generated under `Docs/AI_Usage/sources/m1_unity_v001/` and transformed deterministically by `Tools/process_m1_art.py`.
- Final 128×128 RGBA assets are under `Assets/_Project/Art/M1Production/`: four idle character sprites, a soul pickup, a closed exit, Haste and Giant icons, and a seamless floor tile.
- Character sprites use bottom-center pivots; all other sprites use centered pivots. Unity imports them at 128 pixels per unit with point filtering, no mipmaps, uncompressed texture data, and custom pivots.
- `M1ContentBootstrap` imports the required production sprites and fails clearly when one is missing; it never overwrites the generated PNGs.
- Exact source and final hashes, response IDs, prompt keys, bounding boxes, and ordered post-processing steps are recorded in `Docs/AI_Usage/generations/m1_unity_sprites_v001.json`.
- All nine assets remain `review-required` until human review of the actual 1280×720 gameplay scene.
- Machine, Unity, WebGL, and browser verification passed; evidence and the remaining human visual checks are recorded in `Docs/AI_Usage/edits/m1_unity_sprite_review_v001.json`.

## M1 runtime UI concretization

- The approved Haste, Giant, soul and player sprites are reused in a deterministic Unity UI layout; no additional AI-generated image was introduced.
- The M1 HUD now presents live player life, dash cooldown, room/soul/exit state, blessing availability and selection guidance. Five world-space enemy health bars expose friendly-fire outcomes.
- The 1920×1080 reference canvas scales to the verified 1280×720 minimum. Initial and Haste-selected browser captures, test hashes and the WebGL file-set hash are recorded in `Docs/AI_Usage/edits/m1_ui_implementation_review_v001.json`.
- M1 scope remains enforced: no Echo slot, minimap, inventory, meta-progression, mobile controls or M2 room UI was added.

## M1 directional character animation

- Eight AI-generated north/east key poses extend the four approved M1 character designs. South reuses the approved production sprite; west mirrors east, while diagonal poses are deterministic integer-shift opaque-pixel composites with mirrored left-facing counterparts.
- `Tools/process_m1_animation.py` deterministically produced four 6144-pixel-wide multi-sprite atlases containing 944 active 128×128 frames across eight directions and the full PRD M1 state set.
- Unity imports the atlases as named multi-sprites at 128 PPU with point filtering, no mipmaps, uncompressed data and bottom-center pivots. `DirectionalSpriteAnimator` selects one of eight sectors using deterministic 22.5°/67.5° boundaries.
- Prompts, response IDs, superseded attempts, source hashes and atlas hashes are recorded in `Docs/AI_Usage/generations/m1_directional_animations_v001.json`; exact frame rectangles and per-frame hashes are in `Docs/AI_Usage/generations/m1_directional_animation_index_v001.json`.
- Test, WebGL and 1280×720 browser evidence is recorded in `Docs/AI_Usage/edits/m1_directional_animation_review_v001.json`. The generated poses remain `review-required` pending final human consistency review.
## M2 offline image-resource preproduction

- The user explicitly approved offline M2 image production in `Docs/Decisions/M2_ASSET_PRODUCTION_APPROVAL.json`; no M2 scene, prefab, gameplay data or runtime binding was created.
- Eight gpt-5.4 source generations cover five Golem directions plus Echo VFX, environment mechanics and final-room presentation sheets.
- `Tools/process_m2_art.py` produced one eight-direction Golem animation atlas with 264 named frames and 28 standalone 128×128 sprites for Echo, resonance, cliffs, destructibles, traps, final-room portals and UI crests.
- `M2ImageResourceBootstrap` imports the package at 128 PPU with point filtering, binary alpha, no mipmaps and uncompressed texture data.
- Prompts, response IDs, source/output hashes, deterministic edits and remaining visual review items are recorded in `Docs/AI_Usage/generations/m2_image_resources_v001.json`; per-cell and per-frame hashes are recorded in `Docs/AI_Usage/generations/m2_image_resource_index_v001.json`.
