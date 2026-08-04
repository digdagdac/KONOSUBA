# Fresh Playtest Pack (no coaching)

Purpose: raise the current three-room submission run to a quality where a stranger can finish one clean run without being taught mid-session.

## Rules
1. Tester has never seen Overbless, or at least has not been coached on this run.
2. No mid-run advice of any kind. Camera language, button reminders on-screen, and the build UI are allowed; spoken tips are not.
3. Record raw notes immediately after the session. Do not rewrite failures into successes.
4. Tuning numbers stay `[PLACEHOLDER]` until three independent sessions complete.

## Session card
| Field | Value |
|---|---|
| Date (local) | |
| Tester ID | T1 / T2 / T3 |
| Build path or URL | Builds/Overbless_Web or published gh-pages |
| Device / browser | |
| Resolution | 16:9, preferably 1920x1080 |
| Session length | target 8–15 minutes |

## Script for facilitator (read once, then silence)
1. "This is a browser game. Click to begin when it asks."
2. "Please think aloud if you want, but I cannot answer strategy questions."
3. Start the build. Do not explain blessings, Echo, or the pillar.
4. After quit/finish, fill the form below with the tester.

## Observation checklist
Mark Yes / Partial / No. Add one line of evidence.

### First 60 seconds
- [ ] Finds the start click without help
- [ ] Understands movement (WASD) within 30s
- [ ] Notices they cannot directly attack
- [ ] Opens a blessing (1/2 or Echo) without frustration quitting

### ROOM 01 — induction
- [ ] Applies Haste or Giant at least once on purpose
- [ ] Causes at least one friendly-fire kill
- [ ] Collects a soul and understands souls come from enemy kills between enemies
- [ ] Opens the exit and leaves without being told R/restart rules after first death

### ROOM 02 — Echo
- [ ] Notices Echo is a new blessing without being told
- [ ] Successfully causes one replay-related kill or near-kill intent
- [ ] Does not report the room as "same as room 1"

### ROOM 03 — pillar routing
- [ ] Recognizes the pillar as a path splitter / line-of-fire block
- [ ] Survives long enough to try a second strategy after first failure
- [ ] Reaches exit or can explain a plausible clear plan

### Defeat / recovery
- [ ] After first death, uses R without being told (or finds the on-screen prompt)
- [ ] Defeat panel is not confusing
- [ ] Restart does not soft-lock controls, audio, or start gate

### End-of-run impression
- [ ] Result screen is understandable
- [ ] Would restart for "one more try" without external push
- [ ] Fantasy understood: strengthen enemies → redirect their force

## Scores (1–5)
1 = broken / confusing, 5 = clean and readable
| Metric | Score | Note |
|---|---|---|
| Core fantasy clarity |  | |
| Control readability |  | |
| Telegraph readability |  | |
| Room-to-room learning curve |  | |
| Juice / feedback satisfaction |  | |
| Desire for a longer run later |  |  (B input) |

## Free notes
What confused you first?
What moment felt best?
What should the next permanent feature add (B backlog seed)?

## Pass bar for submission polish
A run is "demo-ready" only if:
1. 2 of 3 testers clear Room 01 without coaching.
2. At least 2 of 3 correctly describe the fantasy in one sentence after Room 01.
3. No session hard-locks, soft-locks, or silent death without restart affordance.
4. Room_02 Echo is recognized as a new rule by at least 2 of 3.