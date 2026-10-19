# Handoff: Colony Ship Surge (built, measured, removed from main)

Saved from an unnamed Claude Code session, called **"colony ship surge"** here, on 2026-10-19. The work ran 2026-10-18 to
2026-10-19. This file is the written version so nothing depends on that history.

Read `CLAUDE.md` first (architecture, conventions, the self-check pattern). The Editor cannot be run from the command
line: the user focuses it and runs the self-checks, and any Unity compile error is only visible to them.

## Where things stand

**Outcome: the surge was built, measured in Play, found to do almost nothing, and removed from `main`. The full work is
kept on the pushed branch `conversion-surge-reach`.**

What the surge was: a colony ship production boost and priority launch for planets next to a dominated planet, so a
conquered planet fills with the conqueror's colonists faster (spec
`docs/superpowers/specs/2026-10-18-conversion-colony-surge-design.md`). A reach amendment then widened "servers" from
direct neighbours to planets within `conversionSurgeReachNodes` (3) path nodes, capped surged picks within one production
pass, and added a `SurgeHeld` log line (amendment spec `2026-10-18-conversion-colony-surge-reach-design.md`, plan
`docs/superpowers/plans/2026-10-18-conversion-colony-surge-reach.md`).

What was verified, and how:

- Editor self-checks all passed on the branch (the user ran `Run Conversion Surge Self-Check` and `Run All AI Self-Checks`).
- A fresh whole-branch review by a subagent found no Critical issues and one Important design gap (see Open items).
- Play runs, six per board, branch against the pre-amendment surge, read with `/tuning-log`:

| Per run (average) | 4p before | 4p reach | test2 before | test2 reach |
|---|---|---|---|---|
| Plans with 0 servers | 40% | 16% | 70% | 16% |
| `SurgeBuild` | 0.5 | 1.5 | 0.2 | 0.8 |
| `SurgeLaunch` | 0 | 0.8 | 0.7 | 0.8 |
| `OwnerChanged` | 23.0 | 19.7 | 18.2 | 23.8 |
| `PlanetDead` | 20.8 | 16.8 | 30.2 | 24.7 |
| `Blockade` events | 54.5 | 68.8 | 47.3 | 39.0 |

  Reach fixed the zero-server plans, but conquests, Clean share, colony failures and blockades all stayed within noise.
  Fleet-cap violations were 0 in all six `4p.json` runs. `SurgeHeld` appeared once on `4p.json` (a `NoRoute`) and four
  times on `test2.json`.
- Why it does little (a reading of the logs, not proven): the surge only acts when a dominated planet has real room,
  which is rare (room is mostly 1 to 3), and ordinary production already fills small room. The pipeline was already at
  or above the target's effective wanted in about 55% of `test2.json` plan lines. A rough reconstruction of docked versus
  in-production colony ships from the log found ships launching within about 3 to 6 turns, so there was no sign of a
  launch bug. The log cannot resolve this per server.
- After the removal, the user ran all self-checks again and they passed.

What was not done: no code was written to make servers route-aware (see Open items), and `/update_feature_list` moved
nothing, because the surge is not a done feature.

## Files and commits

On `main` (pushed to `origin/main`, now at `7bac80f`):

- `d1ec4eb` merge of the first surge, later reverted.
- `da91bd9` amendment spec, `fb36733` amendment plan (both kept).
- `7bac80f` **revert of the merge** plus the `FUTURE_FEATURES.md` note. Removed the planner, tracker, tunables, incoming
  colonist counter, log lines, surge self-check, and the surge text in `CLAUDE.md` and the tuning-log skill. The spec and
  plan documents were deliberately kept.

On branch `conversion-surge-reach` (pushed, tracking `origin/conversion-surge-reach`, based on `fb36733`):

- `d49e14e` reach by path nodes, servers on the plan line.
- `b213d0f` same-turn cap on surged colony ship picks.
- `349a2ce` `SurgeHeld` log and the docs.

Uncommitted: nothing from this work. `FUTURE_FEATURES.md` and `completed_features.md` were not changed by the close-out
(nothing moved). Untracked and unrelated: `.idea/.idea.FlatSpace/.idea/indexLayout.xml`.

## Decisions and why

- **Reach 3 path nodes** (2 reproduces direct neighbours only): the first Play batches showed 40 to 70% of plans with no
  direct-neighbour server.
- **Did not fix the reviewer's Important finding** (servers have no route or blockade check, so a server behind a
  blockaded planet gets the boosted weight and its ships count in the pipeline). Reason: dropping unreachable servers
  would also hide the `NoRoute` and `OutOfRange` cases `SurgeHeld` reports. The Play runs showed it is rare (5 `SurgeHeld`
  lines in 12 runs).
- **Removed the surge from `main` instead of merging the amendment or just switching it off** (the user's call after I
  recommended it): limited utility did not justify the code complexity. Switching off via `conversionSurgeShipsPerRatio` 0
  was rejected because the code and logs would remain. Merging the amendment was ruled out as the most code for the least
  evidence.
- **Revert by `git revert -m 1`, not a history rewrite**, and the branch kept and pushed at the user's instruction.

## Open items and next steps

1. Next carried-over item (unchanged): **offense-scaled conversion speed**, the lever most likely to raise the real
   conquest rate (16 to 20% baseline). Start with brainstorming.
2. `FUTURE_FEATURES.md` item (1) under "Still open from (3)" now reads "TRIED AND REMOVED 2026-10-19" with these numbers
   and a note to **revisit if the AI is ever allowed to reorder or interrupt its build queue**. It stays in
   `FUTURE_FEATURES.md` on purpose.
3. If the surge is ever revived from the branch: the reviewer's route-aware server gap, the stale wording ("serving
   neighbour" in the old `CLAUDE.md` bullet, `SurgePlan.For(string neighbour)`, a comment near `PlayerAI.cs:1778`), and
   that the same-turn cap check calls `TryReserveSurgeSlot` directly rather than driving the real production pass. A
   dominated planet I own can also serve another dominated target (the user was asked to confirm; no answer recorded).
4. Older carried-over items: the `test2.json` colony-failure watch, and the other open invasion items listed in
   `FUTURE_FEATURES.md`.

## Gotchas

- Do not push or merge `conversion-surge-reach` into `main` without asking; it still contains the full surge and
  conflicts in intent with `main`.
- The six "before" logs for the comparison are from earlier in the same day (`4p.json` 19-52 to 20-06, `test2.json`
  19-35 to 19-43) and ran the first surge version, not a no-surge build. There is no no-surge baseline from this session.
  Conclusions about conquests come from six runs a side and are within noise either way.
- Do not run the tuning-log `ConversionSurge` analysis on new logs: those lines no longer exist on `main`.
- Per-session working rules: the user wants a recommendation with every choice, a yes before code, and commits and
  pushes only when asked. Subagents are acceptable (one fresh final review per branch).
