# Colony Surge Reach Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Let a dominated planet be served by my planets two hops away, stop several servers overshooting its room in one production pass, and log why a ready server did not send.

**Architecture:** Three small changes to the merged surge (`ConversionSurge`, `PlayerAI`, `AITuningLogger`): the planner finds servers from the stored all-pairs paths instead of `GetNeighbours`; production picks that were surged reserve a slot in the plan as they are emitted; the surge launch pass reports the reason a server was skipped. No order type, save or `ScoreMatrix` change.

**Tech Stack:** Unity 6 C#, Editor self-check suites (no test framework; `Debug.LogError` on failure).

**Spec:** `docs/superpowers/specs/2026-10-18-conversion-colony-surge-reach-design.md` (amends `2026-10-18-conversion-colony-surge-design.md`). Read both and `CLAUDE.md` ("Invasion (conversion)" surge bullet, "AI Tuning Log") first.

## Global Constraints

- New tunable on `GameAIConstants` with an in-code default, also written into `Assets/GameAIConstantsProductionTypes.asset`: `conversionSurgeReachNodes` 3 (2 = the merged direct-neighbour behaviour).
- A server is my planet `S != D` with `2 <= Planet.DistanceMapToPathingList[S].NumNodes <= conversionSurgeReachNodes` read from D's table (the stored shortest paths; symmetric). Never use `GetNeighbours` for serving any more.
- The owner's cap: `population + my colonists en route + my colony ships docked or in production for D < MaxPopulation(D)`, also within one production pass.
- Docked ships on an unready server keep counting in the pipeline.
- Held-launch reasons are exactly `BlockadedOrigin`, `NoRoute`, `OutOfRange`; log lines use `T<turn>|P<id>|<Code>|...`, never repeat every turn, and are log-only state.
- No `Gameboard.Instance` in the planner or its self-check; a method a self-check calls is `public`. New scripts get their `.meta` committed (none are planned here).
- Never put a self-check exactly on a float boundary.

## Review Focus

- A server in range of two targets is claimed once (larger wanted first, then name).
- A target whose servers are 3 nodes away at reach 2: no servers, no crash, no surge.
- Two servers of one target with EW 1 both pick ColonyShip in one pass: one start emitted, not two; a non-surged ColonyShip pick on a server of a full target is NOT dropped.
- A ready server skipped for a reason is logged once, not every turn, and clears when it launches.
- Legacy mode and `conversionSurgeShipsPerRatio` 0 still give an empty plan.
- `SurgeBuild` still logs for the pick that took the last slot.

## File Structure

- Modify `Assets/Flatspace/GameAI/GameAIConstants.cs`, `Assets/GameAIConstantsProductionTypes.asset`, `Assets/Flatspace/GameAI/ConversionSurge.cs`, `Assets/Flatspace/GameAI/ConversionSurgeTracker.cs`, `Assets/Flatspace/GameAI/PlayerAI.cs`, `Assets/Flatspace/Diagnostics/AITuningLogger.cs`, `Assets/Editor/ConversionSurgeSelfCheck.cs`, `.claude/skills/tuning-log/SKILL.md`, `CLAUDE.md`, `FUTURE_FEATURES.md`.

---

### Task 1: Reach, and the servers field on the plan line

**Files:** `GameAIConstants.cs` (+ asset), `ConversionSurge.cs`, `ConversionSurgeTracker.cs`, `PlayerAI.cs` (`RefreshSurgePlan`), `AITuningLogger.cs` (`LogConversionSurge`), `ConversionSurgeSelfCheck.cs`

**Interfaces:**
- Produces: `GameAIConstants.conversionSurgeReachNodes` (int, 3); `ConversionSurgeTracker.Entry.Servers` (int; a change re-reports); `AITuningLogger.LogConversionSurge(int turn, int playerId, string planet, int wanted, int room, int enRoute, int pipeline, int myPop, int totalPop, int servers)` writing `...|<totalPop>|<servers>`. `ConversionSurge.Build` keeps its signature and sets `SurgeTarget.Servers` from the path tables.

- [ ] **Step 1: Write the failing checks.** In `ConversionSurgeSelfCheck`: `RunReachCheck` on `CombatSelfCheck.Fixture.Line()` (A-B-C-D, B dominated, `f.Colonize` A, C and D for player 0, D populated so it is mine): at reach 3 B's servers are A, C and D (D is 3 nodes away), at reach 2 only A and C; a rival-owned or empty planet in range is not a server; with B at the far end (use D dominated, reach 3) A is 4 nodes away and not a server; a server in range of two dominated planets (B and D dominated, C and A mine) appears only in the larger-wanted target's `Servers`; reach 1 or below gives no servers and no exception. Extend `RunTunableDefaultsCheck` (reach 3) and `RunSurgeTrackerCheck` (a changed `Servers` is reported). Update `RunSurgeWeightCheck`: its "a planet that serves no target keeps the plain weight" assertion now needs `f.Constants.conversionSurgeReachNodes = 2` for D (D is 3 nodes from B).
- [ ] **Step 2: Run** `FlatSpace -> AI -> Run Conversion Surge Self-Check` (owner). Expected: compile errors (`conversionSurgeReachNodes`, `Entry.Servers` missing).
- [ ] **Step 3: Implement.** Add the tunable (comment in the style of its neighbours, naming 2 as the old behaviour) and the asset entry. In `ConversionSurge.Build` replace the `GetNeighbours` loop with: for each planet S in `map.PlanetList` owned by `playerId`, `S != D`, with `D.DistanceMapToPathingList.TryGetValue(S.PlanetName, out var e) && e.NumNodes >= 2 && e.NumNodes <= reach`, ordered by `e.Cost` then name, claim it (same `claimed` set, same pipeline arithmetic). Pass the server count through `RefreshSurgePlan` into the tracker entry and `LogConversionSurge`.
- [ ] **Step 4: Run** the suite and `Run All AI Self-Checks`. Expected: pass.
- [ ] **Step 5: Commit** `feat: surge reach by path nodes, servers on the plan line`.

### Task 2: The cap holds within one production pass

**Files:** `PlayerAI.cs` (`BuildIndustryMatrix`, `UpdatePlanetaryProduction`, `EmitProductionOrders`, `RefreshSurgePlan`), `ConversionSurgeSelfCheck.cs`

**Interfaces:**
- Consumes: `SurgePlan.For`, `SurgeTarget.Pipeline`, `EffectiveWanted`.
- Produces in `PlayerAI`: `private HashSet<string> _surgedServers` (planets whose ColonyShip weight was surged when the matrix was built, i.e. `SurgeMultiplier(planet) > 1`), filled at the start of `BuildIndustryMatrix` and cleared by `RefreshSurgePlan`; `public bool TryReserveSurgeSlot(string planetName, CatalogItem item)`: true for anything that is not a surged ColonyShip pick (item not `ColonyShip`, or the planet not in `_surgedServers`); for a surged pick, false when its target's `Pipeline >= EffectiveWanted`, else raises that `Pipeline` by 1 and returns true; `public void SnapshotSurgedServers()` (the fill, public for the self-check).

- [ ] **Step 1: Write the failing checks.** `RunSameTurnCapCheck` using `LaunchScene`-style setup (B dominated 3 of 5, EW 2, servers A and C ready to build, a ColonyShip `CatalogItem`): after `SnapshotSurgedServers()`, `TryReserveSurgeSlot("A", colony)` and `("C", colony)` are both true and the target's `Pipeline` is 2; a third call for either is false; with EW 1 the second call is false; a non-ColonyShip item always returns true and changes nothing; a planet that serves no target returns true and changes nothing; a server whose weight was NOT surged at snapshot time (add a queued ship so `Pipeline >= EW` before the snapshot) returns true for a ColonyShip pick (ordinary expansion is not dropped); `RefreshSurgePlan()` clears the snapshot.
- [ ] **Step 2: Run** the suite. Expected: compile errors.
- [ ] **Step 3: Implement.** In `UpdatePlanetaryProduction`, before `EmitProductionOrders(action, orders)`, skip the action when `!TryReserveSurgeSlot(action.PlanetName, action.ChosenItem)` (the planet starts nothing this turn and re-checks next turn). Change the `SurgeBuild` condition in `EmitProductionOrders` to `_surgedServers.Contains(action.Origin)` (not `SurgeMultiplier`, which drops to 1 once the slot is taken) and log the boost value `AIMap.GameAIConstants.conversionSurgeBoost`.
- [ ] **Step 4: Run** the suite and `Run All AI Self-Checks`. Expected: pass.
- [ ] **Step 5: Commit** `fix: surged colony ship picks reserve their slot within a production pass`.

### Task 3: Held launches are logged; docs

**Files:** `PlayerAI.cs` (`ProcessColonizers` surge pass), `AITuningLogger.cs`, `ConversionSurgeSelfCheck.cs`, `.claude/skills/tuning-log/SKILL.md`, `CLAUDE.md`, `FUTURE_FEATURES.md`

**Interfaces:**
- Produces: `AITuningLogger.LogSurgeHeld(int turn, int playerId, string origin, string planet, string reason)` writing `SurgeHeld|<origin>|<planet>|<reason>`; `PlayerAI` `private readonly Dictionary<string, string> _surgeHeldBack` and `public bool NoteSurgeHeld(string origin, string reason)` (true when the reason is new for that origin; the same pattern as `NoteColonizeHeldBack`, but a separate dictionary because the ordinary loop removes `_colonizeHeldBack` entries and the two would flap).

- [ ] **Step 1: Write the failing checks.** `RunSurgeHeldCheck` with `LaunchScene` and `ProcessColonizers` (a `ColonizerReady` result for the server): a server whose dominated planet is blockaded in its view (dock rival warships at B and `RefreshBlockadeView`) is logged `NoRoute` once through `NoteSurgeHeld` and returns false on the second turn; a blockaded server (rival ships at it) logs `BlockadedOrigin`; set `maxPathNodesForColonization` to 1 so the server is out of range: `OutOfRange`; a server that launches is not noted and its state is cleared (a later hold is noted again); a pass with no surge targets clears the dictionary. Assert through `NoteSurgeHeld`'s return values and a public read of the state (`SurgeHeldReason(string origin) : string`, mirroring the existing `ColonizeHeldBackReason`).
- [ ] **Step 2: Run** the suite. Expected: compile errors.
- [ ] **Step 3: Implement.** In the surge pass: for each ready colonizer that serves a surge target, a blockaded origin notes `BlockadedOrigin`, `CandidateRoute` with `inRange == false` notes `OutOfRange`, a null route with `inRange == true` notes `NoRoute`; call `LogSurgeHeld` only when `NoteSurgeHeld` returns true. After the pass remove `_surgeHeldBack` entries for servers that were not held this pass; clear it when `surgeLeft` is empty. Comment that `OutOfRange` also covers a target the origin cannot support (`CanSupportColony`).
- [ ] **Step 4: Docs.** Update the `tuning-log` skill's surge bullet (servers per plan and the share of plans with 0 servers against the 70% `test2.json` and 40% `4p.json` baselines, `SurgeHeld` counts by reason, `SurgeBuild` and `SurgeLaunch` per run against 0.2 / 0.7 on `test2.json` and 0.5 / 0 on `4p.json`; the `ConversionSurge` line's last field `<servers>`, older logs lack it), the "AI Tuning Log" and surge bullets of `CLAUDE.md` (reach tunable, the same-turn reservation, `SurgeHeld`), and the surge note in `FUTURE_FEATURES.md`.
- [ ] **Step 5: Run** the suite and `Run All AI Self-Checks`. Expected: pass. Commit `feat: log held surge launches; surge docs`.
- [ ] **Step 6: Owner Play check** on `test2.json` and `4p.json` (a few runs each), read with `/tuning-log`: plans with 0 servers fall well under 70% / 40%; `SurgeBuild` and `SurgeLaunch` rise from 0.2 to 3 a run; `SurgeHeld` reasons explain the ready servers that did not send; `pipeline + enRoute + population` never reaches max in a plan line; real conquests finishing against the 16 to 20% baseline; colony failures and `Blockade` events inside their usual drift; fleet-cap violations 0.

## Self-review

- **Spec coverage:** reach (Task 1), the servers field (Task 1), same-turn cap (Task 2), `SurgeHeld` (Task 3), docs and the skill (Task 3), tests for every spec test bullet (each task), non-goals untouched (readiness, multiplier, unready-server pipeline all unchanged).
- **Names:** `conversionSurgeReachNodes`, `Entry.Servers`, `LogConversionSurge(... servers)`, `_surgedServers`, `SnapshotSurgedServers`, `TryReserveSurgeSlot`, `NoteSurgeHeld`, `SurgeHeldReason`, `LogSurgeHeld` are defined once above and used with those names.
