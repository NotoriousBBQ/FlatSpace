# Handoff: AI-initiated blockades

Saved from the Claude Code session named **"choke_point_changes"** (the session also built the AI-initiated blockades, so the file is named for them; the
chokepoint work has its own file, `HANDOFF-choke-point-changes-2026-10-22.md`), on 2026-10-22.

Read `CLAUDE.md` first, then `Assets/Flatspace/GameAI/CLAUDE.md` ("Initiated blockades"), then `FUTURE_FEATURES.md`. Spec
`docs/superpowers/specs/2026-10-22-ai-initiated-blockades-design.md`, plan `docs/superpowers/plans/2026-10-22-ai-initiated-blockades.md`.

## Where things stand

**Asked:** after the chokepoint weighting, the AI-initiated blockades: aggressive attempts to starve rivals (1 enemy Distribution Centers, 2 planets high
volumes of enemy shipments pass through, 3 uncolonized high value planets until a colony ship can get there), using only leftover warships, valid against
rivals at war or contact players over a tunable hostility, with the fleet cap raised per active blockade and producer value by planet type built here.
A late-game slowdown profile was also done and shelved (see Open items).

**Done and merged to `main` locally (not pushed):** merge commit `cde929b`, which brings in `d496488` (the feature) and `dfae925` (siege ship icon asset, wider
fleet panel with wrapping rows). The branch `ai-initiated-blockades` still exists locally.

What it does: `PlayerAI.PlanInitiatedBlockades` runs last in `PlanShipActions`. `BlockadeInitiator` (pure) picks Dc, Lane and Colony targets in strict priority,
held ones first; `InitiatedBlockadeTracker` (pure) holds the state (active holds, lane hysteresis, give-up counters, skips, cooldown, last turn's wanted ships);
`AssaultPlanner.HoldNeededOffense`/`PlanHoldForce` size and pick the force; each group passes the arrival projection (now per ship kind). Log lines
`BlockadeInitiateStart/End/Skipped`. Constants on `GameAIConstants` under "Initiated blockades".

### What was verified, and how

- **Self-checks:** you ran `Run Blockade Initiation Self-Check` and `Run All AI Self-Checks` and reported all passing after the last fix pass (several
  fixture failures along the way were fixture errors, not rule errors).
- **Play runs, read with `/tuning-log`** against the 2026-10-22 chokepoint baselines (`4p.json` 15:09 to 15:39, `test2.json` 15:46 to 16:02), three batches:

| Batch | Holds starting with 0 ships (4p Dc) | Holds with a `Blockade` at the held planet | Restarts of a given-up planet | 4p `Blockade` per run |
|---|---|---|---|---|
| 1 (`17:56`-`18:58`, before fixes) | 80% | 35 of 630 Dc (6%) | 30 to 113 a run | ~110 |
| 2 (`19:15`-`20:10`, cooldown, no slot without a ship) | 13% | 12% of holds | 0 | 83 |
| 3 (`20:59`-`21:23`, after the review fix pass) | 13% (20% over all kinds) | 26% (Dc 29%) | 0 | 133 |

  Baseline 4p `Blockade` 53, grotsits `OrderBlocked` 35. Batch 3: grotsits `OrderBlocked` 79, `OwnerChanged` 8 (baseline 9), `PlanetDead` 23 (28),
  `ColonizeArrive` 139 (157), `PopulationLoss` 290 (257). test2 within the earlier ranges. The 4p blockade and cut increase is the intended effect.
- **Final branch review** by a fresh opus subagent: 1 Critical, 6 Important, 10 Minor. All Critical and Important were fixed in one pass (see Decisions).

### What was not verified

- **The fleet panel layout and the siege ship icon in mixed fleets as drawn:** you confirmed the icon shows; the wider panel and wrapping rows were changed
  after the review and not seen in a Play run by the session. Check with a damaged siege ship.
- **Fix I5 (the assault's ships to the same planet count against a hold's need)** has no self-check of its own; it showed only indirectly (the old fleet-term
  expectation of exactly 5 became a 2 to 5 range).
- My fleet-cap invariant script (multiplier 0 against warship starts) reports "violations" in the baseline too, so it is a script artifact, not a signal.
  The `WarshipBoost` fields are `wanted|have|multiplier`.
- Holds starting without ships (about 20% of Starts) and the Colony kind (it almost never logs a `Blockade`) were noticed, not worked.

## Files and commits

- `d496488 feat: AI-initiated blockades (Dc, lane and colony holds)`: new `Assets/Flatspace/GameAI/{BlockadeInitiator,InitiatedBlockadeTracker}.cs`,
  `Assets/Editor/BlockadeInitiateSelfCheck.cs` (all with `.meta`); changed `PlayerAI.cs`, `AssaultPlanner.cs`, `GameAIConstants.cs`, `GameAIMap.cs`,
  `Planet.cs`, `PlanetTypeData.cs`, three planet-type `.asset` files (producer value), `AITuningLogger.cs`, `AllAISelfChecks.cs`, `WarshipSelfCheck.cs`,
  `GameAI/CLAUDE.md`, `CLAUDE.md`, `FUTURE_FEATURES.md`, the `tuning-log` skill, the spec and the plan.
- `dfae925`: `Siegeship.asset` (new icon), `FleetUI.uxml` (560px wide), `FleetUIController.cs` (rows wrap, right padding).
- Merge `cde929b` on `main`. Earlier today: chokepoint weighting `b23f378` / merge `7a3b506`, spec `ba769c3`.
- **Uncommitted (from the close-out):** `FUTURE_FEATURES.md` and `completed_features.md` (see below), and this handoff. `ProfilerCaptures/` is untracked and was
  never staged. Nothing is pushed.

## Decisions made and why

- Priority Dc, then Lane, then Colony; valid rivals are at war or contact players over `blockadeInitiateHostility` 60 (yours); leftovers only; the cap
  is raised per active blockade (yours); producer value built here (yours).
- **Fixes after the first Play batch** (yours, "do all three"): a 20-turn cooldown after a give-up, no slot for a target that cannot be sent a ship, and a trace
  of `WarEnded` (the label was right: wars on `4p.json` last about 10 turns).
- **Fix pass after the review** (yours, "do the fix pass"): the arrival gate now projects the kind of ship sent (before it saw siegeships, so a warship group
  was effectively never refused); ships docked at another hold are not spare for a hold; assault ships to the same planet count as sent; a hold short of its
  need for the give-up turns is released as `Outmatched`; a held lane drops with its rival (`WarEnded`); a Colony hold needs a colonizable planet
  (`NoLongerTarget`); rival DC lists are read as of the start of the turn (`DistributionCentersAtTurnStart`).
- Deferred on purpose: all 10 review Minors (listed in `FUTURE_FEATURES.md` under ship_combat_refinements), tuning of the lane/hostility/active constants.
- **Close-out calls (yours):** the handoff is named `HANDOFF-ai-initiated-blockades-...` because the chokepoint handoff holds the other name; ship_combat is
  marked done and moved to completed_features, with its still-open items moved to a new feature, **ship_combat_refinements**.
- Shelved: the late-game slowdown (markers on branch `profiling`; captures in the untracked `ProfilerCaptures/`). The presentation refresh stayed flat, so
  turning graphics off would not help; the cost is in the AI and diplomacy loops. Detail is in `FUTURE_FEATURES.md`.

## Open items and next steps

1. **Human player mode** is the next roadmap item.
2. Commit the close-out files (`FUTURE_FEATURES.md`, `completed_features.md`, this handoff), on request. Push `main` when you ask (it has not been pushed).
3. Look at the fleet panel in a Play run (damaged siege ship).
4. **ship_combat_refinements** in `FUTURE_FEATURES.md` holds everything left over, including the blockade items (0-ship Starts, Colony holds that rarely
   blockade, tuning) and the deferred minors.
5. Local branches `profiling`, `chokepoint-weighting`, `planet-defense`, `ai-initiated-blockades` still exist (not deleted).

## Gotchas

- Stage files by explicit path; `git add -A Assets` once nearly staged the Unity-generated `Assets/Tiny Ships/Art/tinyShip17_9.asset` (it is committed now,
  in `ffe49cf`). Never stage `ProfilerCaptures/`.
- `PlayerAI.cs`, `GameAIConstants.cs` and the self-check files are CRLF in places; patch with a script that preserves the file's line endings
  (large bash heredocs containing Python failed once: write the script to a file and run it with `python3 -I`).
- A self-check must never use `Gameboard.Instance`, and methods it calls must be `public`. `WarshipSelfCheck.MakeConstants` sets
  `blockadeInitiateMaxActive = 0`, so older fixtures run with the feature off only if they use it.
- Plan-level fixtures are easy to get wrong: the ordinary assault, retreat and keep logic also move ships and can take a hold's spare. Print what the plan did
  (the `Dump` helper in the self-check) rather than guessing; Unity's console text is also in `%LOCALAPPDATA%\Unity\Editor\Editor.log`, which can be read
  without asking you to paste it.
- Two inits within the same second collide on one log file name (`aiTuningLog_..._20-59-26.txt` is a 2-line stub); ignore such files.
- Pronouns: use they/them when unstated. Standing rules: a recommendation with every choice, a yes before code, commit/push only when asked.
