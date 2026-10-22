# Handoff: Choke point changes (chokepoint weighting)

Saved from the Claude Code session named **"choke_point_changes"**, on 2026-10-22. The session started from the planet-defense handoffs and ran the whole
brainstorm, spec, plan, implementation, Play measurement and merge for chokepoint weighting. This file is the written version so nothing depends on that history.

Read `CLAUDE.md` first, then `Assets/Flatspace/GameAI/CLAUDE.md` ("Connectivity (chokepoints)"), then `FUTURE_FEATURES.md`. Spec
`docs/superpowers/specs/2026-10-22-chokepoint-weighting-design.md`, plan `docs/superpowers/plans/2026-10-22-chokepoint-weighting.md`.

## Where things stand

**Asked:** more activity at chokepoints: a higher rate of assault targeting, conversion targeting and bunker creation, attacking enemy-held and defending owned
chokepoints equally, with the side effects identified and minimized. AI-initiated blockades are the next item, not part of this. The owner approved the design
choices: the cost-divisor approach (not a hard chokepoint ranking step), the sticky "most own ships" rule kept first (reading (a)), `bunkerChokepointFactor` starting at 3,
and a note to possibly move assault targeting to `ScoreMatrix` later.

**Done and merged to local `main` (not pushed):**

| Change | What it does | Key code |
|---|---|---|
| Assault cost divisor | a target with no ships of mine committed is compared at `pathCost / (1 + assaultChokepointWeight (0.5) x chokepoint percentile)`; only real chokepoints tilt; the real cost is what is logged and flown | `AssaultPlanner.ChooseEnemyTarget`, `LastChokepointTilt`, `GameAIConstants.assaultChokepointWeight` |
| Bunker chokepoint factor | `BunkerWeight` uses `max(outer ? bunkerOuterFactor (2) : 1, chokepoint ? bunkerChokepointFactor (3) : 1)`; the rival-nearby gate is unchanged | `PlayerAI.BunkerWeight`, `GameAIConstants.bunkerChokepointFactor` |
| Logging | new `ChokepointAssault|target|pathCost|percentile|nearestTarget|nearestCost`; `BunkerBuilt` ends with `|<chokepoint 0/1>` | `AITuningLogger`, `PlayerAI.PlanShipActions`, `tuning-log` skill |
| Self-checks | `RunAssaultChokepointCheck` (BlockadeBreak), `RunBunkerChokepointCheck` (PlanetDefense), a formatter check (Chokepoint); `WarshipSelfCheck.MakeConstants` sets `assaultChokepointWeight = 0` | `Assets/Editor/*` |

### What was verified, and how
- **Self-checks:** you ran Planet Defense, Blockade Breaking, Chokepoint and `Run All AI Self-Checks`; all passed. I never ran Unity.
- **Final review:** one opus subagent over the staged diff: no Critical or Important findings. Four minor comment/doc inaccuracies were fixed; one minor deferred (below).
- **Play runs, 6 per board, read with `/tuning-log`, compared with the 10-22 planet-defense batches on the same boards:**

| Per run (mean) | 4p base | 4p new | test2 base | test2 new |
|---|---|---|---|---|
| `BunkerBuilt` total | 101 | 73 | 69 | 62 |
| `BunkerBuilt` on chokepoints | 19.3 (19%) | 18.5 (25%) | 12.3 (18%) | 15.5 (25%) |
| `AssaultTarget` (share on chokepoints) | 20.0 (17%) | 23.5 (16%) | 8.3 (36%) | 9.0 (52%) |
| `ChokepointAssault` lines | 0 | 2.3 | 0 | 1.2 |
| `OwnerChanged` | 9.5 | 8.7 | 6.3 (4-8) | 12.3 (5-21) |

  Fleet-cap violations were 0 in all 24 runs. `ChokepointAssault` costs the pick 42 to 349 extra cost units when it fires. `ChokepointGarrison` share at T375 rose a
  little on 4p (about 0.10 to 0.14). The most owner changes on one chokepoint were 4 on test2 (baseline max 2): watch item. `4p.json` `PlanetDead` rose from 15 to 28.5
  per run and `PopulationLoss` from 201 to 257, but the ranges overlap and it cannot be attributed. A baseline on `Random_s72648369_2p_20.json` (2 players, 20 planets, 6 runs,
  14:36 to 14:43) exists from before the change: assault 25% and bunkers 12% of events on 2 chokepoints (10% of planets).

### What was not verified
- Whether the assault effect is real: with the sticky rule first the tilt fires 1 to 2 times a run, and the test2 conquest rise (6.3 to 12.3) is suggestive, not proven.
- No random-board run after the change; the Chokepoints log line lists only the top 10 planets, so the 4p chokepoint count of 10 may undercount.
- Conversion at chokepoints did not rise as a share (`ConversionStart` share 45% to 36% on 4p, 33% to 30% on test2).

## Files and commits
- `b23f378` the feature (code, self-checks, docs, skill, spec edits, plan), merge commit `7a3b506` on local `main`. Spec committed earlier as `d55c12a`. Branch `chokepoint-weighting` still exists
  locally (not pushed, not deleted). **Not pushed: `origin/main` is behind.**
- **Uncommitted:** `FUTURE_FEATURES.md` and `completed_features.md` (the close-out: consumer (1) moved as a "Partially done" entry, next-up block updated) and this handoff.
- Untracked and not ours: `Assets/Tiny Ships/Art/tinyShip17_9.asset` and its `.meta` (Unity-generated).

## Decisions made and why
- **Cost divisor over a ranking step** (owner accepted my recommendation): a ranking step makes any chokepoint beat any non-chokepoint at any distance; the divisor is one tunable and cannot beat the sticky rule.
- **Reading (a): sticky rule first** (owner): so the tilt only decides among targets with none committed, a modest effect by design. Letting a chokepoint outrank a target with some committed ships (reading (b)) is **not built**; revisit if the logs show too little effect, and tell the owner.
- **`bunkerChokepointFactor` starts at 3** (owner), not 2: so the first Play batch tests the goal; the outer factor stays 2.
- **Assault targeting stays a sorted pick, not `ScoreMatrix`** (owner agreed): recorded in `FUTURE_FEATURES.md` as "Possibly change assault targeting to use ScoreMatrix".
- **`ChokepointAssault` baseline is "the same loop with weight 0"**, tracked in one pass; repeat guard key is target|nearestTarget.
- **Close-out judgment call:** "Weight planet value by connectivity" has no labelled sub features, so I treated consumer (1) plus chokepoint bunkers as done and moved only that; the rest of the entry stays.
- **Process:** native execution (no per-task subagents); one final opus reviewer. Tests and code for Tasks 1 to 3 were written together and verified in one batched owner run instead of a RED run first.

## Open items and next steps
1. **Profile the late-game turn slowdown** (the owner's next topic): the presentation refresh runs over the whole board every turn (`GameBoard.SingleUpdate`: every planet's `UIUpdate`, every order's line, `FogUIUpdate`'s `GetComponentsInChildren<LineDrawObject>()` plus three samples per connection, fog `Recompute`), with no viewport culling. Whether that or the simulation causes the growth is unmeasured: a deep-profile capture of an early and a late turn would settle it (Rider has `unity_profiler_*` tools). Cheap fixes if it is the UI: skip the order-line and fog refresh when the view is off, cache the connection-line list.
2. **AI-initiated blockades** (next in the "Next up" block, then Human player mode).
3. Deferred minor from the final review: `Math.Max` in `BunkerWeight` puts a floor of 1 under both bunker factors, so a factor tuned below 1 does nothing; the value that restores the old chokepoint weight is `bunkerChokepointFactor = 2`.
4. If the effect should be stronger: raise `bunkerChokepointFactor` or `assaultChokepointWeight`, or build reading (b).
5. Still open in the entry: producer value by planet type, DC selection, the shipment threshold, garrison and own-hub priority beyond category 4.
6. Housekeeping: push `main` when you want; branch `chokepoint-weighting` can be deleted; the `planet-defense` branch is also still around.

## Gotchas
- Unity cannot run from the command line: every check is you running a `[MenuItem]` self-check or a Play run; a self-check must never use `Gameboard.Instance`.
- `Fixture.Line` (A-B-C-D): B and C tie at percentile 0.67, so a chokepoint test on it needs `chokepointPercentile` 0.6 and makes both chokepoints; C is also outer.
- A tilt test needs `maxPathNodesForShipTransport` handling: setting it to 1 makes every route unusable (used for the no-path case).
- `git add -A Assets` also stages Unity-generated assets (it nearly staged `tinyShip17_9.asset`); stage by path.
- The owner wants a recommendation with every choice, a yes before code, and commits and pushes only when asked. Subagents are acceptable (one fresh final review per branch).
