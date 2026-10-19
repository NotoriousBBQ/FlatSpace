# Colony Ship Surge Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Planets next to a dominated planet build and send colony ships to it in proportion to how little of it is mine, never beyond its room.

**Architecture:** One pure planner (`ConversionSurge`) turns the map state into a per-turn `SurgePlan` (wanted, room, en route, pipeline per dominated planet, and which neighbour serves which). Both consumers read that one plan: the ColonyShip situational weight (`GetIndustrySituationalWeightMultiplier`) and `ProcessColonizers` (docked ships launch to the dominated planet first). Everything else is reused: the derived-incoming-counter pattern, `GetNeighbours`, `IsConversionTarget`, `RoutePlanner.PlanRoute`, `CanSupportColony`, the existing colonist order emission, and the log-tracker pattern.

**Tech Stack:** Unity 6 C#, Editor self-check suites (no test framework; `Debug.LogError` on failure).

**Spec:** `docs/superpowers/specs/2026-10-18-conversion-colony-surge-design.md`. Read it and `CLAUDE.md` ("Invasion (conversion)", "Ship Transport", "AI Tuning Log", Conventions) first.

## Global Constraints

- Tunables on `GameAIConstants` with in-code defaults, also written into `Assets/GameAIConstantsProductionTypes.asset`: `conversionSurgeBoost` 3 (1 or below: no production surge), `conversionSurgeShipsPerRatio` 1 (0 or below: no surge at all).
- Wanted ships for a dominated planet D: `ceil(total / max(1, mine) x conversionSurgeShipsPerRatio)`.
- Hard cap (owner's rule): no colony ship is started or launched for D when `population + my colonists en route + my colony ships being built or docked for D >= MaxPopulation`.
- No `Gameboard.Instance` in the planner or its self-check. A method the self-check calls is `public`, not `internal`.
- A situational weight lives in `GetIndustrySituationalWeightMultiplier`; nothing state-dependent goes in the strategy tables or `ScoreMatrix`. It is computed at most once per production turn.
- Weight 0 still means "do not offer". A blockaded planet builds and launches nothing; the readiness rule (`expandPopulationTrigger`) stays for launches.
- New log lines use the `T<turn>|P<id>|<Code>|...` format, never repeat every turn, and are never saved state. No `OrderType` or save change (a new `OrderType` would have to be appended last; none is needed).
- Never put a self-check exactly on a float boundary (`CLAUDE.md` Conventions). New scripts are committed with their `.meta`.

## Review Focus

- A dominated planet at max population, or with room 0 once colonists en route are counted: no ship built, none launched.
- `mine` = 0 (ratio uses 1) and `total` = 0: no divide by zero, no surge.
- A neighbour next to two dominated planets: its ship is counted for one planet only.
- Two ready neighbours and a room of 1: only one launches.
- A blockaded neighbour, an unready neighbour (population under the trigger), and a neighbour with no route to D: none of them is forced; they fall back to the ordinary rule.
- A surge target with a colonist already en route (the one-at-a-time rule) still accepts the second colonist, and the first arrival must not break the second.
- Legacy mode (`Diplomacy.Enabled` false): nothing changes.

## File Structure

- Create `Assets/Flatspace/GameAI/ConversionSurge.cs` (+ `.meta`): pure planner and `SurgePlan`.
- Create `Assets/Flatspace/GameAI/ConversionSurgeTracker.cs` (+ `.meta`): log-only change tracker, modelled on `ConversionHoldTracker`.
- Create `Assets/Editor/ConversionSurgeSelfCheck.cs` (+ `.meta`); register it in `Assets/Editor/AllAISelfChecks.cs` and add its menu item like the others.
- Modify `GameAIConstants.cs`, the constants asset, `Planet.cs`, `GameAIMap.cs`, `GameAI.cs`, `PlayerAI.cs`, `AITuningLogger.cs`, `.claude/skills/tuning-log/SKILL.md`, `CLAUDE.md`, `FUTURE_FEATURES.md`.

## Call that refines the spec (check it)

The spec says effective wanted = `min(wanted, room)`. Read literally it would send `wanted` more ships every turn. This plan measures wanted as the total committed: **`EW = max(0, min(wanted - enRoute, room))`**, with `room = MaxPopulation - population - enRoute`. Production starts a ship while `pipeline < EW`; the launch budget this pass is `EW`. The owner's cap holds either way (room already includes en route). Update the spec's Goal 2 sentence when this is approved.

---

### Task 1: Tunables and the incoming-colonist counter

**Files:**
- Modify: `Assets/Flatspace/GameAI/GameAIConstants.cs` (the "Invasion (conversion)" header block), `Assets/GameAIConstantsProductionTypes.asset`, `Assets/Flatspace/Objects/Planets/Planet.cs` (next to `GetIncomingOffense`), `Assets/Flatspace/GameAI/GameAIMap.cs` (next to `RecomputeIncomingOffense`), `Assets/Flatspace/GameAI/GameAI.cs` (the `GameAIUpdate` call site at the `RecomputeIncomingOffense` line, and the load path next to `RecomputeIncomingShips`)
- Create: `Assets/Editor/ConversionSurgeSelfCheck.cs`; modify `Assets/Editor/AllAISelfChecks.cs`

**Interfaces:**
- Produces: `GameAIConstants.conversionSurgeBoost` (float, 3), `conversionSurgeShipsPerRatio` (float, 1); `Planet.GetIncomingColonists(int owner) : int`, `Planet.AddIncomingColonists(int owner, int delta)`, `Planet.ClearIncomingColonists()`; `GameAIMap.RecomputeIncomingColonists(List<GameAI.GameAIOrder> orders)`; `public static bool ConversionSurgeSelfCheck.RunChecks()` (+ `[MenuItem("FlatSpace/AI/Run Conversion Surge Self-Check")] Run()`).

- [ ] **Step 1: Write the failing checks** in `ConversionSurgeSelfCheck`: `RunTunableDefaultsCheck` (new `GameAIConstants` instance: boost 3, ratio scale 1; follow `ConversionSelfCheck.RunTunableDefaultsCheck`) and `RunIncomingColonistsCheck` (a `CombatSelfCheck.Fixture.Line()` map; two `OrderTypePopulationTransport` orders for player 0 targeting "A" with `Data` 1, one for player 1, one with `TimingDelay` 0 is still counted; `RecomputeIncomingColonists` gives `A.GetIncomingColonists(0) == 2`, `(1) == 1`; a second call with an empty list gives 0; an order of another type is ignored). Register the suite in `AllAISelfChecks` with the name "Conversion Surge".
- [ ] **Step 2: Run** `FlatSpace -> AI -> Run Conversion Surge Self-Check` (owner focuses the Editor). Expected: compile errors for the missing members.
- [ ] **Step 3: Implement** the two constants (with the comment style of the neighbouring ones, naming the 0/1 "off" values), the asset entries, and the counter by mirroring `_incomingOffense` / `RecomputeIncomingOffense` (count each colonist order's `Data` as an int, keyed by `order.PlayerId`). Call `RecomputeIncomingColonists(CurrentAIOrders)` in `GameAIUpdate` right beside `RecomputeIncomingOffense` and in the load path beside `RecomputeIncomingShips`. Derived state, never saved.
- [ ] **Step 4: Run** the suite. Expected: both checks pass. Then run `Run All AI Self-Checks`: still passing.
- [ ] **Step 5: Commit** `feat: surge tunables and the incoming-colonist counter` (include the new `.meta`).

### Task 2: The pure planner

**Files:**
- Create: `Assets/Flatspace/GameAI/ConversionSurge.cs` (+ `.meta`)
- Modify: `Assets/Flatspace/Objects/Planets/Planet.cs` (a small production counter), `Assets/Editor/ConversionSurgeSelfCheck.cs`

**Interfaces:**
- Consumes: Task 1 members; `GameAIMap.GetNeighbours(string)`, `GameAIMap.PlanetList`, `Planet.MaxPopulation`, `Planet.Population`, `Planet.DockedShips`, `Planet.Owner`.
- Produces in `FlatSpace.AI`: 
  - `Planet.CountColonyShipsInProduction() : int` (`CurrentProduction` plus `ProductionQueue` items whose `Item.subType == "ColonyShip"`).
  - `ConversionSurge.Wanted(int total, int mine, float shipsPerRatio) : int` (0 when `total <= 0` or the scale is <= 0).
  - `ConversionSurge.EffectiveWanted(int wanted, int enRoute, int room) : int` = `max(0, min(wanted - enRoute, room))`.
  - `class SurgeTarget { string Planet; int Wanted, Room, EnRoute, Pipeline, EffectiveWanted, MyPop, TotalPop; List<string> Servers; }` and `class SurgePlan { List<SurgeTarget> Targets; SurgeTarget For(string neighbour); }` (`For` returns the target a server neighbour serves, else null).
  - `static SurgePlan ConversionSurge.Build(GameAIMap map, int playerId, Func<Planet, bool> isDominated)`: for every planet D with `isDominated(D)` and `EffectiveWanted > 0` after the cap, in order of descending `Wanted` then name, claim D's neighbours that I own (`Owner == playerId`) and no earlier D has claimed; `Pipeline` is the claimed servers' docked colony ships plus `CountColonyShipsInProduction()`; `EnRoute` is `D.GetIncomingColonists(playerId)`; `Room = MaxPopulation - Population.Count - EnRoute`. A target with `Pipeline >= Room` keeps `EffectiveWanted` but production is not wanted (the consumer compares `Pipeline < EffectiveWanted`). Returns an empty plan when `conversionSurgeShipsPerRatio <= 0` or `Diplomacy.Enabled` is false.

- [ ] **Step 1: Write the failing checks.** `RunWantedCheck`: `Wanted(7, 1, 1)` = 7, `Wanted(7, 2, 1)` = 4 (3.5 rounds up), `Wanted(7, 0, 1)` = 7 (mine floored at 1), `Wanted(0, 0, 1)` = 0, `Wanted(7, 1, 0)` = 0. `RunEffectiveWantedCheck`: `(7, 0, 3)` = 3 (room binds), `(2, 1, 5)` = 1, `(2, 2, 5)` = 0, `(2, 3, 5)` = 0 (never negative). `RunBuildCheck` on a `Fixture` with a dominated planet "B" (use `ConversionSelfCheck.Populate` and `f.Ships`/`f.War` as `RunConversionColonizeCheck` does) and neighbours "A" and "C": servers are A and C, `For("A")` is B's target; pipeline counts a docked ship on A and a colony ship in A's `ProductionQueue`; a colonist order en route lowers `Room`; B at max population gives no target; a neighbour owned by someone else is not a server; a neighbour of two dominated planets (build a 4-planet fixture or two lines) is served to the one with the larger wanted and appears in only one `Servers` list; the plan is empty with the scale at 0 and in legacy mode.
- [ ] **Step 2: Run** the suite. Expected: fails to compile (types missing).
- [ ] **Step 3: Implement** `ConversionSurge` (pure; no `Gameboard.Instance`) and `Planet.CountColonyShipsInProduction`. Reuse `GetNeighbours`; do not re-derive adjacency.
- [ ] **Step 4: Run** the suite and `Run All AI Self-Checks`. Expected: pass.
- [ ] **Step 5: Commit** `feat: ConversionSurge planner` (with `.meta`).

### Task 3: The production surge

**Files:**
- Modify: `Assets/Flatspace/GameAI/PlayerAI.cs` (`ProcessResults` after `RefreshBlockadeView`, `GetIndustrySituationalWeightMultiplier` ColonyShip branch, the production-start site in `ProcessIndustry`), `Assets/Flatspace/Diagnostics/AITuningLogger.cs`, `Assets/Editor/ConversionSurgeSelfCheck.cs`

**Interfaces:**
- Consumes: `ConversionSurge.Build`, `SurgePlan.For`, `SurgeTarget`.
- Produces: `PlayerAI` field `_surgePlan` (rebuilt once per `ProcessResults`, with `isDominated: IsConversionTarget`; null-safe when diplomacy is off), `public SurgePlan SurgePlanForTest()` is not needed: the self-check reads `GetIndustrySituationalWeightMultiplier` and sets the plan through `ProcessResults`-free means by calling a new `public void RefreshSurgePlan()` (the same method `ProcessResults` calls); `private float SurgeMultiplier(string planetName)` = `conversionSurgeBoost` when the planet is a server of a target with `Pipeline < EffectiveWanted` and the boost is above 1, else 1; `AITuningLogger.LogSurgeBuild(int turn, int playerId, string neighbour, string planet, float multiplier)`.

- [ ] **Step 1: Write the failing checks.** `RunSurgeWeightCheck` (use `ConversionSelfCheck`'s `MakeAI` pattern and a ColonyShip `CatalogItem`): a ready neighbour with room and an unmet count gets `ColonyShipUrgentBoost x 3` = 6; the same planet not ready under Consolidate also 6; under Expand not ready, weight 1 becomes 3; a neighbour already holding a docked colony ship stays 0; a blockaded neighbour stays 0; a planet that serves no target is unchanged; once `Pipeline >= EffectiveWanted` (add a queued colony ship) the multiplier is back to the old value; `conversionSurgeBoost` 1 leaves every value unchanged; the owner's cap: with `population + enRoute + pipeline >= MaxPopulation` (set by populating D to max, or by en-route orders through `RecomputeIncomingColonists`) the multiplier is the old value. Also assert `GetIndustryWeight` still equals strategy weight x situational weight.
- [ ] **Step 2: Run** the suite. Expected: the surge assertions fail (weight 2, not 6).
- [ ] **Step 3: Implement.** `RefreshSurgePlan()` is called in `ProcessResults` right after `RefreshBlockadeView()` and builds the plan (stale after launches within the pass is safe: a launch lowers pipeline and room together). In the ColonyShip branch keep every existing early return; replace the three tail returns with `base x SurgeMultiplier(planetName)` where `base` is `ColonyShipUrgentBoost`, or 1 for the Expand-not-ready fall-through (restructure only as far as needed, keeping the other items' code path unchanged). Call `LogSurgeBuild` where `ProcessIndustry` emits a production start for a ColonyShip on a planet whose `SurgeMultiplier > 1`.
- [ ] **Step 4: Run** the suite and `Run All AI Self-Checks`. Expected: pass.
- [ ] **Step 5: Commit** `feat: colony ship surge on neighbours of dominated planets`.

### Task 4: Docked ships go to the dominated planet first

**Files:**
- Modify: `Assets/Flatspace/GameAI/PlayerAI.cs` (`ProcessColonizers`, `IsValidColonizationTarget`), `Assets/Flatspace/Diagnostics/AITuningLogger.cs`, `Assets/Editor/ConversionSurgeSelfCheck.cs`

**Interfaces:**
- Consumes: `_surgePlan`, `SurgeTarget.Servers`, `SurgeTarget.EffectiveWanted`, `GameAI.ApplyColonistArrival` (unchanged).
- Produces: `AITuningLogger.LogSurgeLaunch(int turn, int playerId, string origin, string target, int myPop, int totalPop, int enRoute, float routeCost, string nearestTarget)`.

Reuse, do not duplicate: pull the per-(colonizer, target) candidate test inside `ProcessColonizers` (path-map presence, node limits, `CanSupportColony`, `RoutePlanner.PlanRoute`, recording in `plannedRoutes` and `nearest`) into one local function that the ordinary loop and the surge pass both call, then feed the surge launches into the existing emission loop (colonist, population change, transfer flag, remove ship, rider).

- [ ] **Step 1: Write the failing checks.** `RunSurgeLaunchCheck` (drive `ProcessColonizers` with a hand-built `ColonizerReady` result list, as `PlayerAIResourceSelfCheck` drives its processors): (a) a ready neighbour with a docked ship sends it to D even when a nearer ordinary target exists; (b) two ready neighbours and `EffectiveWanted` 1: one colonist order only; (c) `EffectiveWanted` 2: both launch, and a D with a colonist already en route (set through `RecomputeIncomingColonists` and the flag) still accepts them; (d) an unready neighbour has no `ColonizerReady` result and launches nothing; (e) a blockaded neighbour is skipped (`ColonizeCancelled` path unchanged); (f) a neighbour with no route to D (block it in the `BlockadeView`) falls back to the ordinary rule; (g) after surge launches use up the room, the ordinary matrix does not also claim D (the room left filter); (h) a non-surge planet is unchanged (compare with the old output); (i) `IsValidColonizationTarget` rejects a conversion target when `population + GetIncomingColonists >= MaxPopulation`. Add `RunSurgeArrivalCheck`: two colonists to D, the first lands through `GameAI.ApplyColonistArrival` (flag cleared), the second still lands normally, and with D full the second docks a colony ship (the existing arrival rule).
- [ ] **Step 2: Run** the suite. Expected: launch assertions fail.
- [ ] **Step 3: Implement** the shared candidate function; the surge pass (for each ready, non-blockaded colonizer that is a server of a target with budget left, plan the route to D with the shared function; assign cheapest route first until each target's `EffectiveWanted` is used); remove assigned colonizers from the ordinary matrix; build the ordinary `targets` with a `roomLeft` dictionary so a surge-served D is excluded once its room is used; add the `population + GetIncomingColonists < MaxPopulation` condition to the conversion branch of `IsValidColonizationTarget` only. Emit `LogSurgeLaunch` with the nearest ordinary target from the existing `nearest` map. The flag cleared by the first arrival is accepted (the derived counter, not the flag, guards the room); note it in the code comment.
- [ ] **Step 4: Run** the suite, `Run Colonist Redirect Self-Check` and `Run All AI Self-Checks`. Expected: pass.
- [ ] **Step 5: Commit** `feat: docked colony ships launch at the dominated planet first`.

### Task 5: The surge log, docs and the Play check

**Files:**
- Create: `Assets/Flatspace/GameAI/ConversionSurgeTracker.cs` (+ `.meta`)
- Modify: `PlayerAI.cs`, `AITuningLogger.cs`, `ConversionSurgeSelfCheck.cs`, `.claude/skills/tuning-log/SKILL.md`, `CLAUDE.md`, `FUTURE_FEATURES.md`

**Interfaces:**
- Produces: `ConversionSurgeTracker.Entry { string Planet; int Wanted, Room, EnRoute, Pipeline, MyPop, TotalPop; }`, `List<Entry> Update(IEnumerable<Entry> current)` (changed entries, ordered by name; state drops planets that left the plan; a changed `Wanted`, `Room`, `EnRoute` or `Pipeline` re-reports), `Clear()`; `AITuningLogger.LogConversionSurge(...)` writing `ConversionSurge|<planet>|<wanted>|<room>|<enRoute>|<pipeline>|<myPop>|<totalPop>`.

- [ ] **Step 1: Write the failing check** `RunSurgeTrackerCheck` (same shape as `RunConversionHoldTrackerCheck`): first update reports all, an identical second reports none, a changed pipeline reports one, a planet that leaves is forgotten and reports again on return, `Clear()` reports all.
- [ ] **Step 2: Run** the suite. Expected: fails to compile.
- [ ] **Step 3: Implement** the tracker and feed it from `RefreshSurgePlan` (each plan target becomes an entry; log the changed ones). Update the `tuning-log` skill (surge lines: how long surges last, launches per conversion, `SurgeBuild` and `SurgeLaunch` counts, `ColonistDocked` as the overshoot signal, colony failures on converted planets), the "AI Tuning Log" and "Invasion (conversion)" sections of `CLAUDE.md` (the new tunables, the rule, the three log lines), and mark item (1) of `FUTURE_FEATURES.md` as built (leave the rest of that entry). Run `Run All AI Self-Checks`.
- [ ] **Step 4: Owner Play check** on `test2.json` and `4p.json`, a few runs each, read with `/tuning-log`: `ConversionSurge` appears during sieges; `SurgeBuild` and `SurgeLaunch` fire (the old speed-up fired 0 to 3 times a run); `ColonistDocked` at dominated planets stays near zero; real conquests (under half the planet mine at the start) finish more often than the 16 to 20% baseline; colony failures and blockade events do not rise outside their 20 to 30% batch drift.
- [ ] **Step 5: Commit** `feat: surge log lines and docs`.

## Self-review

- **Spec coverage:** wanted ratio (Task 2), room cap (Tasks 2, 3, 4), production x3 (Task 3), docked ship first with readiness kept (Task 4), neighbours only (Task 2), one ship counted once (Task 2), tunables (Task 1), three log lines and the tracker (Tasks 3, 4, 5), docs and skill (Task 5), self-check suite (every task), legacy mode inert (Tasks 2, 3).
- **Multiple colonists on one target:** handled without touching `ScoreMatrix` (the surge pass assigns launches directly, the matrix never sees D once its room is used) and without a flag change (the derived counter guards the room).
- **Names:** `GetIncomingColonists`, `RecomputeIncomingColonists`, `ConversionSurge.Build`, `SurgePlan.For`, `RefreshSurgePlan`, `SurgeMultiplier`, `LogSurgeBuild`, `LogSurgeLaunch`, `LogConversionSurge` are defined once above and used with those names.
