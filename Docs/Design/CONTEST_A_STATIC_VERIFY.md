# Contest A Static Verification

Date: 2026-08-04
Branch: feature/contest-run-shell
Commit: b76bb2d (local clone)

## What was verified without Unity batchmode

Unity batchmode EditMode returned `198` / "No valid Unity Editor license found" under the sandbox identity, so runtime NUnit could not be the gate. Static chain checks were used instead.

### Scene chain (nextScene string bindings)

| Scene | nextScene | StartPrompt | Defeat panel |
|---|---|---|---|
| Title | M1_GuidedValidation | n/a (TrustedInputScreen) | n/a |
| M1_GuidedValidation | Room_02 | yes | yes |
| Room_02 | Room_03 | yes | yes |
| Room_03 | Result | yes | yes |
| Result | Title | n/a | n/a |

### EditorBuildSettings play order

1. Assets/_Project/Scenes/Title.unity
2. Assets/_Project/Scenes/M1_GuidedValidation.unity
3. Assets/_Project/Scenes/Room_02.unity
4. Assets/_Project/Scenes/Room_03.unity
5. Assets/_Project/Scenes/Result.unity

### Room objective HUD

- R01: MAKE THEIR ATTACKS HIT EACH OTHER / HASTE OR GIANT · COLLECT 3 SOULS · REACH THE EXIT
- R02: ECHO REPLAYS THE LOCKED ATTACK / BLESS WITH ECHO · USE THE REPLAY · 3 SOULS THEN EXIT
- R03: THE PILLAR SPLITS THE PATH / ROUTE AROUND THE PILLAR · ECHO + HASTE/GIANT · 3 SOULS

### Contested artifacts present

- ContestWebGLBuilder, TrustedInputScreen, StartGatePrompt, RunOutcomePresenter
- ContestSubmissionTests
- CONTEST_SUBMISSION_APPROVAL.json
- PLAYTEST_PACK_CONTEST_KO.md
- Tools/verify_submission_run.py, Tools/publish_gh_pages.py

## Blocked machineside work

- Unity batchmode EditMode: license 198
- GitHub 443 / `gh` token invalid at verification time
- Main repo `.git` unpack rejected local push (write ACL under sandbox identity)

## Playtest remainder (human only)

Fill Docs/Design/PLAYTEST_PACK_CONTEST_KO.md for three no-coaching sessions. Agent cannot pass that gate.

## Next

Kick off `feature/room-pack-data` for pure data room layouts reusing RoomSequenceController + objective HUD API.
