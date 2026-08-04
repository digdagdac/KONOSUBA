# Longevity / Product Quality Backlog (B)

Contest distribution stays inside approved three-room scope. This document captures product longevity work that should not dilute the submission run until A is stable.

## Guardrails
- No Room_Final, Golem activation, cliffs, traps, residue until after submission demo is stable.
- M2EntryGate remains user-owned; do not invent PASS.
- Prefer systems that reuse Haste / Giant / Echo / pillar / friendly-fire fantasy.

## Candidate pillars (priority after playtest)
1. **Run meta, not combat buttons**
   - Between-run trinket table that only modifies existing blessings / spawn mixes
   - No direct player attacks
2. **Room template authoring**
   - Data-driven room packs from the same Exit/soul/blessing contracts
   - Seeded enemy spawns with identity cards already approved
3. **Readable mastery layers**
   - Optional "echo chain" challenge rooms
   - Daily one-room challenge with fixed seed + ghost clear time
4. **Collection without power creep**
   - Character appeal dossier unlocks (already partially present) with non-stat flavor rewards
5. **Live-ops free loop**
   - Weekly roster remix of Room_02/03 layouts
   - No pay systems

## Kickoff criteria
Start B implementation only when A has:
1. Title → R01 → R02 → R03 → Result playable from ContestWebGLBuilder
2. Playtest pack filled for 3 no-coaching sessions
3. No open hard block on restart, start gate, or room sequencing

## First B slice (when unlocked)
`feature/room-pack-data` — extract Room_02/03 spawn layout further into pure data so new rooms can ship without scene rewrites, reusing RoomSequenceController + HUD objective API.