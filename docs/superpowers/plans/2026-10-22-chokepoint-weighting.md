# Chokepoint Weighting Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Tilt assault/conversion target choice and bunker building toward chokepoint planets, and log the side effects.

**Architecture:** `AssaultPlanner.ChooseEnemyTarget` divides the compared path cost by `1 + assaultChokepointWeight x chokepoint percentile` (the pattern of `PlayerAI.ChokepointDivisor`), keeping the sticky "most own ships" rule first. `PlayerAI.BunkerWeight` gets a separate `bunkerChokepointFactor`. Two log changes (`ChokepointAssault` line, a `chokepoint` field on `BunkerBuilt`) and the tuning-log skill/docs follow.

**Tech Stack:** Unity 6000.4.1f1, C#, `[MenuItem]` self-checks under `Assets/Editor/` (no test framework).

**Spec:** `docs/superpowers/specs/2026-10-22-chokepoint-weighting-design.md`

## Global Constraints

- Work on a branch `chokepoint-weighting` off `main` (use superpowers:using-git-worktrees if isolating). Commit and push only when the owner asks; the "Commit" step in each task means stage the listed files and propose the commit, not run it unprompted.
- New tunables live on `GameAIConstants` with in-code defaults: `assaultChokepointWeight = 0.5f` (<= 0 off), `bunkerChokepointFactor = 3f`; `bunkerOuterFactor` stays `2f`.
- The sticky rule in `ChooseEnemyTarget` stays first; a target with ships already committed (`own > 0`) is never tilted.
- `ChooseBlockadeTarget`, `ChooseTarget`, `ScoreMatrix` and the strategy tables are not touched. Bunker weight stays inside the situational multiplier.
- Real path cost, never the tilted cost, is what is logged and what flight timing uses.
- A self-check must never use `Gameboard.Instance`; a method a self-check calls is `public`, not `internal`. Edit CRLF files in place without changing line endings.
- Older fixtures keep their meaning: `WarshipSelfCheck.MakeConstants` sets `assaultChokepointWeight = 0f` next to `colonizationChokepointWeight = 0f`.
- Unity cannot run from the command line. Verification = the owner focuses the Editor (compile, read Console) and runs the named `[MenuItem]` self-checks; I never claim a pass I did not see.
- New log lines must be added to `.claude/skills/tuning-log/SKILL.md` and the "AI Tuning Log" section of `CLAUDE.md` (Task 5).

## Review Focus

1. Weight 0 (or negative) must reproduce today's `ChooseEnemyTarget` exactly. Pinned in Task 2.
2. A fleet already committed to a target is never pulled off it by a chokepoint. Pinned in Task 2.
3. A chokepoint with no usable path (or in `ExcludedTargets`, a retreat cooldown) is never chosen. Pinned in Task 2.
4. `chokepointPercentile` above 1 switches the assault tilt off, like the other chokepoint consumers. Pinned in Task 2.
5. A planet that is both outer and a chokepoint takes the larger bunker factor, not the product; a chokepoint with no rival near and no known enemy planet still gets weight 0. Pinned in Task 1.

## File Structure

- `Assets/Flatspace/GameAI/GameAIConstants.cs`: two tunables (Tasks 1, 2).
- `Assets/Flatspace/GameAI/PlayerAI.cs`: `BunkerWeight` split, `LogBunkerBuilt` field, `ChokepointAssault` wiring.
- `Assets/Flatspace/GameAI/AssaultPlanner.cs`: tilt in `ChooseEnemyTarget`, `LastChokepointTilt`.
- `Assets/Flatspace/Diagnostics/AITuningLogger.cs`: `LogBunkerBuilt` field, `LogChokepointAssault` + formatter.
- `Assets/Editor/{PlanetDefenseSelfCheck,BlockadeBreakSelfCheck,ChokepointSelfCheck,WarshipSelfCheck}.cs`: checks and fixture default.
- Docs: `.claude/skills/tuning-log/SKILL.md`, `CLAUDE.md`, `Assets/Flatspace/GameAI/CLAUDE.md`, `FUTURE_FEATURES.md`.

## Tuning log output (proposed additions)

| Line | When logged | Fields | Question it answers | Repeat guard | Task / test |
|---|---|---|---|---|---|
| `T<t>\|P<id>\|ChokepointAssault\|target\|pathCost\|percentile\|nearestTarget\|nearestCost` | the turn the assault target changed AND the tilt changed it versus the weight-0 pick | real cost of the chosen target; real cost of the untilted pick | Is the tilt moving fleets, and how much farther? (side effect: neglected cheap targets) | `_lastLoggedChokepointAssault` key `target\|nearestTarget` in `PlayerAI`, reset to null when the tilt is inactive | Task 3; formatter check in `ChokepointSelfCheck` |
| `BunkerBuilt` trailing field `chokepoint` (0/1) | existing line, on completion | chokepoint status at completion | Are bunkers landing on chokepoints? | already once per completion | Task 1; formatter check in `PlanetDefenseSelfCheck` |

Side effects read from existing lines (no new code): over-concentration from `ChokepointGarrison` (`shipsOnThem/allShips`) and churn from `OwnerChanged` per chokepoint, with the chokepoint names from `T0|P-1|Chokepoints|`. Task 5 adds these checks to the `tuning-log` skill.

---

### Task 1: Bunker chokepoint factor and `BunkerBuilt` chokepoint field

**Files:**
- Modify: `Assets/Flatspace/GameAI/GameAIConstants.cs` (Bunker header, after `bunkerOuterFactor`, ~line 92)
- Modify: `Assets/Flatspace/GameAI/PlayerAI.cs:2071-2085` (`BunkerWeight`), `:2091-2099` (`LogBunkerBuilt`)
- Modify: `Assets/Flatspace/Diagnostics/AITuningLogger.cs:337` (`LogBunkerBuilt`)
- Test: `Assets/Editor/PlanetDefenseSelfCheck.cs` (`RunBunkerThreatWeightCheck` at ~409, plus a new `RunBunkerChokepointCheck`, registered in `RunChecks` near line 28)

**Interfaces:**
- Produces: `GameAIConstants.bunkerChokepointFactor` (float, 3); `AITuningLogger.LogBunkerBuilt(int turnNumber, int playerId, string planetName, int count, int cap, float weight, bool chokepoint)`; `public static string AITuningLogger.FormatBunkerBuilt(int turnNumber, int playerId, string planetName, int count, int cap, float weight, bool chokepoint)` returning the exact line (used by `LogBunkerBuilt` and the check).

- [ ] **Step 1: Write the failing checks** in `PlanetDefenseSelfCheck`.
  - In `RunBunkerThreatWeightCheck`'s first block, extend the defaults assertion to also require `Near(f.Constants.bunkerChokepointFactor, 3f)` ("the bunker tunables default to 1, 2, 3 and 2 (chokepoint factor 3)").
  - New `public static bool RunBunkerChokepointCheck()` with `Fixture.Line()` (A-B-C-D, player 0 colonizes A, B, C, rival warships docked on B and C, `f.War(0, 1)`), setting `f.Constants.chokepointPercentile` to a value that makes exactly B a chokepoint on this line (read `f.Map.Chokepoint("A"/"B"/"C")` first and assert the precondition `IsChokepoint("B") && !IsChokepoint("A")`). Assertions (Consolidate `ThreatAi`):
    - interior chokepoint B holding a rival ship: weight `1 x 3 x 2 = 6` (chokepoint factor 3 replaces the 1 an interior planet has, rival-adjacent boost 2);
    - with `bunkerChokepointFactor = 2` B is `4`; with 1 it is `2` (the factor is flat, not scaled by percentile);
    - a planet that is both outer and a chokepoint (make C the chokepoint, set `bunkerOuterFactor = 5`, `bunkerChokepointFactor = 3`): factor is `max = 5`, not 15;
    - a chokepoint with no rival ship nearby and no known enemy planet (no `Ships`, `War` set, `Knowledge.Update`): weight 0.
  - Add `FormatBunkerBuilt` assertion: `AITuningLogger.FormatBunkerBuilt(12, 1, "Normal 2", 3, 7, 6f, true)` equals `"T12|P1|BunkerBuilt|Normal 2|3|7|6|1"` and with `false` ends `|0`.
- [ ] **Step 2: Ask the owner to focus the Editor and run `FlatSpace -> AI -> Run Planet Defense Self-Check`.** Expected: compile errors for the missing members (that is the failing state).
- [ ] **Step 3: Implement.**
  - Add `public float bunkerChokepointFactor = 3f;` to `GameAIConstants` with a comment (flat factor for a colonized chokepoint; a planet that is also outer takes the larger of the two factors).
  - In `PlayerAI.BunkerWeight`: keep `special` and the gate unchanged; replace `if (special) weight *= constants.bunkerOuterFactor;` with a factor `Mathf.Max(outer ? bunkerOuterFactor : 1f, chokepoint ? bunkerChokepointFactor : 1f)` where `outer = ctx.Planner.IsOuter(planet)` and `chokepoint = ctx.Planner.IsChokepoint(planet)`.
  - `AITuningLogger.LogBunkerBuilt` gains the trailing `bool chokepoint` (written `1`/`0`) via the new `FormatBunkerBuilt`; `PlayerAI.LogBunkerBuilt` passes `AIMap.IsChokepoint(planetName)`.
- [ ] **Step 4: Owner re-runs the Planet Defense self-check and `Run All AI Self-Checks`.** Expected: PASS, no new Console errors.
- [ ] **Step 5: Commit** `PlanetDefenseSelfCheck.cs`, `GameAIConstants.cs`, `PlayerAI.cs`, `AITuningLogger.cs` ("feat: bunker chokepoint factor, chokepoint field on BunkerBuilt").

### Task 2: Assault cost divisor in `ChooseEnemyTarget`

**Files:**
- Modify: `Assets/Flatspace/GameAI/GameAIConstants.cs` ("Connectivity (chokepoints)" header, after `colonizationChokepointWeight`)
- Modify: `Assets/Flatspace/GameAI/AssaultPlanner.cs:384-410` (`ChooseEnemyTarget`), new `LastChokepointTilt`
- Modify: `Assets/Editor/WarshipSelfCheck.cs:232` (`MakeConstants`)
- Test: `Assets/Editor/BlockadeBreakSelfCheck.cs` (new `public static bool RunAssaultChokepointCheck()`, registered in `RunChecks`)

**Interfaces:**
- Produces: `GameAIConstants.assaultChokepointWeight` (float, 0.5); `AssaultPlanner.ChokepointTilt` struct `{ bool Active; string Target; float PathCost; float Percentile; string NearestTarget; float NearestCost; }`; `AssaultPlanner.LastChokepointTilt` (`ChokepointTilt`, replaced on every `ChooseEnemyTarget` call, `Active == false` when the tilt did not change the pick).

- [ ] **Step 1: Write the failing check** `RunAssaultChokepointCheck` with `Scenario.Hub()` (A(0,0)-H(100,0), H-X1/X2/X3, A-Y(0,80); player 0 holds A; H is chokepoint percentile 1, Y a leaf). Put `s.Ships("A", 0, 2)`, colonize H and Y for player 1 (`s.Colonize("H", 1)`, `s.Colonize("Y", 1)`), `s.Map.Knowledge.Update(s.Map, 2, 6)`, `s.Constants.chokepointPercentile = 0.9f`. Untilted costs: H 100, Y 80. Use `new AssaultPlanner(s.Map, 0)` and `ChooseEnemyTarget()`:
  - weight 0.5: picks H (100/1.5 = 66.7 < 80) and `LastChokepointTilt` is `Active` with `Target == "H"`, `NearestTarget == "Y"`, `PathCost == 100`, `NearestCost == 80`, `Percentile == 1`;
  - weight 0: picks Y, tilt not `Active` (Review Focus 1); weight -1: picks Y;
  - weight 0.2 (100/1.2 = 83.3 > 80): picks Y, tilt not `Active`;
  - `chokepointPercentile = 2f` with weight 0.5: picks Y (Review Focus 4);
  - sticky: `s.Ships("Y", 0, 1)` (my warship docked at Y) with weight 0.5 picks Y, tilt not `Active` (Review Focus 2);
  - `ExcludedTargets = { "H" }` with weight 0.5 picks Y (Review Focus 3); an enemy chokepoint with no usable path is not chosen (use a second scenario with an unconnected enemy planet `Z` beyond `maxPathNodesForShipTransport`, or the `Line()` scenario's unconnected `Z`, asserting the pick is not `Z`).
- [ ] **Step 2: Owner runs `FlatSpace -> AI -> Run Blockade Breaking Self-Check`.** Expected: compile error / FAIL.
- [ ] **Step 3: Implement.** Add the tunable with a comment ("Assault target choice: the cost compared for a target with no ships of mine committed is its cheapest path cost / (1 + this x its chokepoint percentile). 0 or below is off; only real chokepoints (`IsChokepoint`) tilt."). In `ChooseEnemyTarget` keep the loop and the `own > bestOwn || (own == bestOwn && ...)` rule; for `own == 0` compare `cost / (1 + w x p)` where `w = _constants.assaultChokepointWeight`, `p = _map.IsChokepoint(name) ? _map.Chokepoint(name) : 0f` and `w <= 0 -> divisor 1`. In the same pass track the best untilted candidate (same sticky rule, real cost) and its real cost; after the loop set `LastChokepointTilt` to `Active` with the fields above only when the tilted pick differs from the untilted one (`Active == false` otherwise, including no target). Targets with `own > 0` use cost 0 and never tilt. Set `constants.assaultChokepointWeight = 0f;` in `WarshipSelfCheck.MakeConstants` beside line 232.
- [ ] **Step 4: Owner runs the Blockade Breaking self-check, then `Run All AI Self-Checks`.** Expected: PASS. If an older fixture fails (for example in `ShipTransportSelfCheck`, which builds its own `NewConstants()`), set `assaultChokepointWeight = 0f` in that fixture's constants, not in the production default.
- [ ] **Step 5: Commit** `AssaultPlanner.cs`, `GameAIConstants.cs`, `WarshipSelfCheck.cs`, `BlockadeBreakSelfCheck.cs` (+ any fixture touched) ("feat: chokepoint cost divisor for assault target choice").

### Task 3: `ChokepointAssault` log line

**Files:**
- Modify: `Assets/Flatspace/Diagnostics/AITuningLogger.cs` (after `LogChokepointColonize`, ~line 503)
- Modify: `Assets/Flatspace/GameAI/PlayerAI.cs` (the `PlanShipActions` block at ~2531-2540, next to `_lastLoggedAssaultTarget`)
- Test: `Assets/Editor/ChokepointSelfCheck.cs` (`RunSummaryAndFormatCheck`, ~233)

**Interfaces:**
- Consumes: `AssaultPlanner.LastChokepointTilt` (Task 2).
- Produces: `public static string AITuningLogger.FormatChokepointAssault(int turnNumber, int playerId, string target, float pathCost, float percentile, string nearestTarget, float nearestCost)` and `AITuningLogger.LogChokepointAssault(...)` with the same arguments.

- [ ] **Step 1: Write the failing check** in `RunSummaryAndFormatCheck`: `FormatChokepointAssault(40, 0, "Ocean 2", 100f, 1f, "Normal 4", 80f)` equals `"T40|P0|ChokepointAssault|Ocean 2|100|1.00|Normal 4|80"` (costs `0.#`, percentile `0.00`, invariant culture, as `LogChokepointColonize` does).
- [ ] **Step 2: Owner runs `FlatSpace -> AI -> Run All AI Self-Checks`.** Expected: compile error / FAIL.
- [ ] **Step 3: Implement** the formatter and `LogChokepointAssault` (no-op when no log is open, like its neighbours). In `PlayerAI` add `private string _lastLoggedChokepointAssault;`. Right after the existing `AssaultTarget` log block: when `blockadeTarget == null && assault.LastChokepointTilt.Active` and `key = target + "|" + NearestTarget` differs from `_lastLoggedChokepointAssault`, log and store the key; otherwise (tilt not active, or a blockade target in force) set the field to null.
- [ ] **Step 4: Owner runs the self-checks again.** Expected: PASS.
- [ ] **Step 5: Commit** `AITuningLogger.cs`, `PlayerAI.cs`, `ChokepointSelfCheck.cs` ("feat: ChokepointAssault tuning log line").

### Task 4: Play verification

**Files:** none.

- [ ] **Step 1: Owner runs 6 Play runs each on `4p.json` and `test2.json` (and optionally the random board) with `_logAIEvents` on.** Needs a restart of Play mode, not just a recompile.
- [ ] **Step 2: Read the logs with `/tuning-log`** (Task 5's new checks) against the 10-22 baselines: assault/`BunkerBuilt`/`OwnerChanged` on chokepoints (random board: assault 25%, bunkers 12% of events at 10% of planets), `ChokepointAssault` count and distance penalty, `ChokepointGarrison` share, repeat `OwnerChanged` per chokepoint, fleet-cap discipline. Report numbers and any side-effect symptom before proposing retuning.

### Task 5: Docs and tuning-log skill

**Files:**
- Modify: `.claude/skills/tuning-log/SKILL.md` (Chokepoints bullet ~line 297, Planet defense `BunkerBuilt` text ~line 338)
- Modify: `CLAUDE.md` ("AI Tuning Log" bullet list), `Assets/Flatspace/GameAI/CLAUDE.md` ("Connectivity (chokepoints)", ~line 381-398, and the Planet defense bunker-weight text)
- Modify: `FUTURE_FEATURES.md` ("Weight planet value by connectivity": note consumer (1) assault target choice and bunker weighting built, with the date)

- [ ] **Step 1: Skill:** document `ChokepointAssault|<target>|<pathCost>|<percentile>|<nearestTarget>|<nearestCost>` (on tilt-changed picks only; how to report frequency and extra distance), the trailing `chokepoint` field on `BunkerBuilt`, and a "chokepoint activity" check: share of `AssaultTarget`, `BlockadeTarget`, `ConversionStart`, `Convert`, `BunkerBuilt` and `OwnerChanged` on chokepoints (names from `T0|P-1|Chokepoints|`, percentile >= `chokepointPercentile`) against the planets' share, plus the three side-effect watches (neglect via `ChokepointAssault` distances, concentration via `ChokepointGarrison`, churn via repeat `OwnerChanged`).
- [ ] **Step 2: `GameAI/CLAUDE.md`:** add the assault divisor (`assaultChokepointWeight`, sticky rule first, only among targets with equal committed ships), the separate `bunkerChokepointFactor` (3, larger of outer/chokepoint factor, gate unchanged) to the Connectivity section; `CLAUDE.md` AI Tuning Log: mention the new line/field in one line.
- [ ] **Step 3: `FUTURE_FEATURES.md`:** record what was built; leave AI-initiated blockades and producer value listed.
- [ ] **Step 4: Commit** the four files ("docs: chokepoint weighting, tuning-log skill").
