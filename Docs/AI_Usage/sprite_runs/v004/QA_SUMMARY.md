# Monster Motion v004 QA Summary

## Scope

The `Minion`, `Dasher`, and `Archer` enemy sets now use separate, transparent
frame PNGs for each action and cardinal direction. The final runtime files are
under `Assets/_Project/Art/M1Production/Characters/Animation/MotionsV004`.

## Source and automated checks

- 12 direction runs: 3 roles x South/North/East/West.
- Each run includes `Idle` (4), `Run` (8), `Attack` (5), `Hurt` (3), and
  `Death` (5) frames: 25 frames per direction, 300 exported frames total.
- Every run passed `frames/frames-manifest.json.ok` and
  `sprite-sheet-alpha.report.json.ok` after chroma removal, component
  extraction, atlas composition, and preview generation.
- All final PNGs are normalized to one fixed cell size per role: Minion
  160x208, Dasher and Archer 192x224. The feet use a bottom pivot in Unity.
- Visible spot checks included Archer East attack, Dasher West run, and Minion
  North death contact sheets. No cropping or chroma background was retained.

## Runtime contract

`M1DirectionalAnimationBootstrap` imports each final file as a point-filtered,
uncompressed Sprite and builds the animation sets directly from those files.
No monster now depends on an animation atlas texture or a quad/cube renderer.
The existing 8-direction animation API remains intact: diagonal movement maps
to the closest front/back v004 cardinal source until diagonal art is authored.

## Motion caveat and final gate

Run locomotion is generated as an experimental directional cycle. Automated
extraction and frame continuity are clean; the remaining acceptance gate is
Unity PlayMode verification at real enemy movement speeds, including a full
attack/death sequence in the room scenes.
