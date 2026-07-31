# AI Usage Report

## Confirmed decisions

- M0 source approval is recorded in `Docs/Decisions/M0_SOURCE_APPROVAL.json`.
- Pixel-art calibration is confirmed in `Docs/Decisions/STYLE_DECISION.json`.
- M1 audio is confirmed as repository-local procedural synthesis only in `Docs/Decisions/OPEN_A3_AUDIO.json`.
- External recordings, externally generated audio, and AI audio assets are prohibited.
- Ten deterministic procedural WAVs were generated under `Assets/_Project/Audio/M1Functional/`. Each event record under `Docs/AI_Usage/generations/` binds the generator source SHA, Unity version, UTC, seed, parameters, WAV path/SHA, ordered modifications, and `pending-user-gate` reviewer state. Generation is complete; human audio approval is not.

## Functional-event provenance contract

Each generated WAV for the following ten functional events requires a separate record. Every record must contain actual values, not placeholders, for: generator name; tool name and version; generator source SHA-256; generation UTC; seed; parameters or instruction; original WAV path and SHA-256; ordered modifications; final WAV path and SHA-256; and reviewer.

| Event requiring its own record |
| --- |
| `DasherReady` |
| `ArcherReady` |
| `AttackLocked` |
| `PlayerHit` |
| `SoulCollected` |
| `ExitOpened` |
| `BlessingApplied` |
| `BlessingRejected` |
| `EnemyDefeated` |
| `FriendlyFireKill` |

The last four are core-loop cues added after the original six. Applying a blessing and the
resulting enemy-on-enemy kill are the only actions that express player agency, and both were
previously silent. The six original clips keep byte-identical WAV hashes across regeneration;
only their generation timestamp and generator source hash moved when the generator grew.

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

- At v001 generation time, the user-approved scope in `Docs/Decisions/M2_ASSET_PRODUCTION_APPROVAL.json` was offline image production only; that v001 generation created no M2 scene, prefab, gameplay data, or runtime binding.
- Eight gpt-5.4 source generations cover five Golem directions plus Echo VFX, environment mechanics and final-room presentation sheets.
- `Tools/process_m2_art.py` produced one eight-direction Golem animation atlas with 264 named frames and 28 standalone 128×128 sprites for Echo, resonance, cliffs, destructibles, traps, final-room portals and UI crests.
- `M2ImageResourceBootstrap` imports the package at 128 PPU with point filtering, binary alpha, no mipmaps and uncompressed texture data.
- Prompts, response IDs, source/output hashes, deterministic edits and remaining visual review items are recorded in `Docs/AI_Usage/generations/m2_image_resources_v001.json`; per-cell and per-frame hashes are recorded in `Docs/AI_Usage/generations/m2_image_resource_index_v001.json`.

## M2 local runtime visual baseline v002

- The later standing approval in `Docs/Decisions/M2_IMPLEMENTATION_APPROVAL.json` authorizes only the selected Echo and fixed non-damaging pillar scope for local unsealed technical QA. It does not create or imply `M2EntryGate PASS`.
- `Tools/process_m2_runtime_visuals.py` promotes exactly four fixed Echo cells from the immutable v001 source into `M2Production` and produces one original permanent-cover pillar. All Golem, destructible, trap, cliff, resonance, and final-room preproduction assets remain unbound.
- The pillar source was constructed from repository-local Pillow geometric pixel primitives after three image-service attempts produced no bytes. No model output or response ID is claimed. The generation record preserves the failed-attempt facts, local tool version, source/final hashes, and repository-generated-original license.
- `M2RuntimeVisualBootstrap` imports exactly five 128×128 sprites at 128 PPU with point filtering, binary alpha, no mipmaps, uncompressed data, and explicit center or bottom-center pivots.
- M1 and M2 view topology is separated: M1 physically contains only Haste/Giant UI and no Echo presenter or M2-production dependency; M2 uses distinct prefabs, one Echo card, and the v002 resources.
- Prompt, source, output, processing, index, manifest, and review records are `Docs/AI_Usage/prompts/m2_runtime_visual_prompts_v002.json`, `Docs/AI_Usage/generations/m2_runtime_visuals_v002.json`, `Docs/AI_Usage/generations/m2_runtime_visual_index_v002.json`, `Docs/AI_Usage/asset_manifest.csv`, and `Docs/AI_Usage/edits/m2_runtime_visual_review_v002.json`. Human gameplay-scale visual review remains required.

## Monster directional animation overhaul v002

- Fifteen accepted AI-generated source sheets cover Dasher, Archer and Minion from five directly authored directions. Each sheet is an 8×5 grid for Walk, Run, AttackCharge, AttackExecute and Recover; eight topology-invalid attempts are retained under `Docs/AI_Usage/sources/monster_animation_v002/rejected/`.
- `Tools/process_monster_animation_v002.py` permits only magenta chroma removal, nearest-neighbor crop/resize, bottom-center alignment, deterministic packing and exact horizontal mirroring. It does not synthesize motion from idle translation, rotation, scale or tint, and it does not composite directly authored diagonals.
- South, North, East, SouthEast and NorthEast account for 450 directly authored target-state frames. West, SouthWest and NorthWest account for 270 exact mirror-derived frames. The immutable v001 Idle, Hit and Death lineage contributes 312 inherited frames inside the three versioned v002 atlases.
- Each role atlas is 8192×1024 with 128×128 cells. The deterministic index records every source/output hash, crop, opaque bound, classification and sprite name in `Docs/AI_Usage/generations/monster_directional_animation_index_v002.json`.
- Full generation provenance is recorded in `Docs/AI_Usage/generations/monster_directional_animations_v002.json`; machine checks and the live-review matrix are tracked in `Docs/AI_Usage/edits/monster_directional_animation_review_v002.json`. Final Unity tests, both WebGL builds and two-resolution gameplay visual review remain explicitly pending until their evidence is attached.

### Live-review machine evidence

- `Docs/AI_Usage/edits/monster_directional_animation_live_review_v002.json` attaches that evidence for candidate `4ef7d30`. It extends the earlier review record instead of editing it, so the earlier record keeps its own bytes and status, and the new record is bound to them by SHA-256.
- Suites: EditMode 36/36 and PlayMode 22/22 passed, with the result XML hashes recorded. PlayMode grew from 19 to 22 cases because `Assets/Tests/PlayMode/MonsterLocomotionTimingTests.cs` captures the locomotion timing contract as a repeatable test.
- Builds: both Development WebGL players were rebuilt from the reviewed tree. The M1 served-file manifest hash is recorded, and the unsealed M2 technical-QA build is recorded with a deterministic file-set hash.
- Timing: seven metrics were measured on the pre-v002 baseline `dd7ad95` and on the candidate with an identical harness pinned to the 0.02 second fixed step. Chase, retreat and preparation timings are unchanged; the four cadence metrics move by 0.04 seconds, which is 0.78 to 1.18 percent, so every delta stays inside the ten percent band.
- Visuals: `Tools/capture_webgl_visuals.py` served each build and drove headless Chrome at 1280x720 and 1920x1080, sending a trusted pointer gesture plus a movement burst. Ten gameplay frames per surface are recorded with per-frame hashes and change ratios; two frames per surface are attached under `Docs/AI_Usage/reviews/monster_animation_v002/`.
- Human gameplay-scale approval is still outstanding. The record leaves `reviewer` null, states what a reviewer must still judge, and does not touch the user-owned `M2EntryGate`.

## M2 character identity layer

- The approved character direction in `Docs/Decisions/M2_IMPLEMENTATION_APPROVAL.json` now has a runtime form. `Assets/_Project/Data/Characters/CharacterIdentityCatalog.asset` binds Rivella to the player and Vera, Lume and Moko to the Dasher, Archer and Minion data assets, together with an age line, a concept line, a motif colour and one habit line that names behaviour a tester can watch happen.
- `Assets/Tests/EditMode/M2CharacterIdentityTests.cs` reads the approval file and fails if a name, an age or an archetype mapping drifts, so the shipped cast cannot diverge from the approved direction without a test failure.
- Atra is deliberately absent. The guardian is described in the approval, but golem runtime activation stays out of scope, so shipping an identity for it would activate an excluded actor.
- `CharacterAppealPresenter` opens a card at the four approved moments only: first encounter, blessing choice, victory and defeat. Each cast member introduces itself once per attempt even though two archers share one rival, a restart earns the introductions back, and no card opens before a trusted gesture starts the run.
- Portrait art is not produced yet. Every identity declares `portraitSource: RepresentativeCombatSprite` and stands in with its authoritative pixel combat sprite while the four expressions differ by card framing. No record claims cel art that does not exist.
- The acceptance seam for real art already exists. `Docs/AI_Usage/prompts/m2_character_appeal_prompts_v002.json` holds the exact prompts and the expected output paths under `Docs/AI_Usage/sources/m2_character_appeal_v002/`. Once those sheets are delivered, the panels are extracted from their magenta gutters the same way the other sheets were, the catalog switches each identity to `CelPortraitSheet` with four panels, and the EditMode test already requires that switch as soon as the source directory exists.
- This is local unsealed implementation of approved M2 scope. It creates no `M2EntryGate` decision, and the M1 guided scene physically excludes the card, which `M1IntegrationTests` asserts the same way it asserts the Echo exclusion.
