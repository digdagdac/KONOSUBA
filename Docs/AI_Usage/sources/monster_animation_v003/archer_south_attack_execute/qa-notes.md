# Archer South AttackExecute v003 QA

- State: `attack-execute-south`, 6 frames, 14 fps, non-looping.
- Verdict: `best-effort` accepted for the reported crop defect.
- Identity anchor: `Assets/_Project/Art/M1Production/Characters/chr_archer_idle_south_a_v001.png`.
- Reference stack: accepted south Idle anchor plus the six-slot layout guide; the base character sheet was not used as an action-row input.
- Alpha: the green chroma-key row was component-extracted, then normalized to binary alpha at threshold 128 by the deterministic atlas processor.
- Bounds: each final 128px frame has at least 10px left/right safe padding, 2px top padding, and a bottom-aligned foot baseline.
- Motion: ready -> draw -> full draw -> release follow-through -> recoil -> ready. The bow remains attached and visible in every frame; no detached arrow, projectile, glow, trail, particles, or stray pixels are present.
- Limitation: the bow release is communicated through arm/string pose and recoil rather than a detached projectile effect.
