# AI-Initiated Blockades Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Let a Consolidate-like AI start blockades on purpose (rival Distribution Centers, heavy rival shipment lanes, valuable uncolonized planets), using only spare warships, with producer value built as authored data.

**Architecture:** A pure `BlockadeInitiator` ranks targets of three kinds from plain inputs; an `InitiatedBlockadeTracker` keeps hold state (sticky, hysteresis, release reasons, log transitions); `PlayerAI.PlanShipActions` runs the initiate pass last and sends ships through a generalised `AssaultPlanner.PlanHoldForce` (the existing blockade-force sizing); `WantedWarships` gains a per-blockade term from last turn's active set.

**Tech Stack:** Unity 6000.4.1f1, C#, `[MenuItem]` self-checks under `Assets/Editor/` (no test framework).

**Spec:** `docs/superpowers/specs/2026-10-22-ai-initiated-blockades-design.md` (read its "Calls made by the designer" section: it is binding).

## Global Constraints

- Work on a branch `ai-initiated-blockades` off `main`. Commit and push only when the owner asks; each "Commit" step stages the listed files and proposes the commit.
- New tunables live on `GameAIConstants` with in-code defaults (Task 1). `blockadeInitiateMaxActive = 0` switches the whole feature off; a fixture that needs the old behavior sets it to 0.
- Initiated blockades use only ships left spare after garrison, conversion holds, blockade-breaking, assault and siegeship actions, and escorts. They never change those plans.
- Spec fixes carried into code: End reasons are `Colonized`, `LostDc`, `LaneCooled`, `WarEnded`, `Outmatched`, `Unreachable`, `NoSpare` (these replace the spec's `ReasonEnded`/`NeededElsewhere`; `NoSpare` = no spare ship reached the target for `blockadeInitiateGiveUpTurns` turns); Skip reasons are `NoSpare`, `NoPath`, `Outmatched`, `OverCap`.
- Pure logic classes live in `Assets/Flatspace/GameAI/`, namespace `FlatSpace.AI`, no `Gameboard.Instance`; every new script's `.meta` is committed with it. Edit CRLF files in place without changing line endings; a method a self-check calls is `public`.
- Diplomacy off (`!AIMap.Diplomacy.Enabled`, the legacy everyone-is-an-enemy mode): every other player is a valid rival.
- Unity cannot run from the command line: verification = the owner focuses the Editor (compile, Console) and runs the named `[MenuItem]` self-checks; never claim a pass I did not see. Stage by explicit path (`git add -A Assets` also stages Unity-generated files).
- Every new log line is added to `.claude/skills/tuning-log/SKILL.md` and the "AI Tuning Log" section of `CLAUDE.md` (Task 7).

## Review Focus

1. A hold ends for its reason and not later: colony target colonized, rival DC lost or moved, war ended, lane cooled. Pinned in Task 3.
2. Initiated ships come only from spare: with `blockadeInitiateMaxActive = 0` the ship actions are identical to today's, and with it on the garrison, assault and escort actions are unchanged. Pinned in Task 6.
3. Lane hysteresis: a count oscillating between the start and end values does not flap. Pinned in Task 3.
4. A rival at peace below `blockadeInitiateHostility` is never targeted, at or above it is; Expand and None never initiate. Pinned in Task 4.
5. The wanted-fleet term is 0 with no active blockades, bounded by `blockadeInitiateMaxActive x blockadeInitiateMaxShips`, and read from last turn's set (no same-turn feedback). Pinned in Task 6.

## File Structure

- Create `Assets/Flatspace/GameAI/InitiatedBlockadeTracker.cs` (hold state and transitions), `Assets/Flatspace/GameAI/BlockadeInitiator.cs` (candidates, lane tally, ranking), `Assets/Editor/BlockadeInitiateSelfCheck.cs`.
- Modify `GameAIConstants.cs`, `PlanetTypeData.cs` (+3 assets), `Planet.cs`, `GameAIMap.cs`, `AssaultPlanner.cs`, `PlayerAI.cs`, `AITuningLogger.cs`, `AllAISelfChecks.cs`, docs/skill.

## Tuning log output (proposed additions)

| Line | When | Fields | Question | Repeat guard | Task / test |
|---|---|---|---|---|---|
| `T<t>\|P<id>\|BlockadeInitiateStart\|planet\|kind\|rival\|stance\|score\|neededOffense\|ships` | a target becomes active | kind Dc/Lane/Colony; stance War/Peace; score; needed offense; ships sent this turn | what is held and why; was it a peace-time blockade? | `InitiatedBlockadeTracker` (state change only) | Task 5; formatter + tracker checks |
| `...\|BlockadeInitiateEnd\|planet\|kind\|reason\|turnsHeld` | a target stops being active | reasons above | how long do holds last and why do they end? | same tracker | Task 5 |
| `...\|BlockadeInitiateSkipped\|planet\|kind\|reason` | a candidate is passed over, on change only | NoSpare, NoPath, Outmatched, OverCap | are targets starved of ships? | same tracker | Task 5 |
| `WarshipBoost` (existing) | each production turn | wanted now includes the initiated term | did the cap rise? | existing | Task 6 |

---

### Task 1: Constants and producer value

**Files:**
- Modify: `Assets/Flatspace/GameAI/GameAIConstants.cs` (new header "Initiated blockades")
- Modify: `Assets/Flatspace/Objects/Planets/ScritpableObjects/ResourceData/PlanetTypeData.cs` (`PlanetResourceData`)
- Modify: `Assets/Flatspace/Objects/Planets/Planet.cs` (near `MaxPopulation`, line ~167), `Assets/Flatspace/GameAI/GameAIMap.cs` (near `Chokepoint`, line ~261)
- Modify: `.../ResourceData/NoPopResourceData/{VerdantPlanetTypeDataNoPop,DesolatePlanetTypeDataNoPop,FarmPlanetTypeDataNoPop}.asset` (add the `_producerValue` YAML key)
- Test: `Assets/Editor/BlockadeInitiateSelfCheck.cs` (create; `RunChecks`, `[MenuItem("FlatSpace/AI/Run Blockade Initiation Self-Check")]`), registered in `AllAISelfChecks`

**Interfaces:**
- Produces: `GameAIConstants` fields (all public floats/ints): `blockadeInitiateMaxActive` (int, 2), `blockadeInitiateHostility` (60), `blockadeLaneStartCount` (int, 4), `blockadeLaneEndCount` (int, 2), `blockadeLaneEndTurns` (int, 5), `blockadeColonyMinValue` (1), `blockadeColonyChokepointWeight` (1), `blockadeColonyProducerWeight` (1), `blockadeInitiateGiveUpTurns` (int, 10), `blockadeInitiateMaxShips` (int, 4); `PlanetResourceData._producerValue` (float, 0); `Planet.ProducerValue` (float); `GameAIMap.ProducerValue(string planetName)` (float, 0 for an unknown name).

- [ ] **Step 1: Write the failing check** `RunProducerValueCheck` (in the new file; follow `RunChecks` + `Check(bool, string)` as `BlockadeBreakSelfCheck`): the ten defaults above; `AssetDatabase.LoadAssetAtPath<PlanetResourceData>` of the Verdant and Desolate assets has `_producerValue == 1f`, Farm `0.5f`, and Normal, Industrial, Ocean, Desert (the other `NoPopResourceData` assets) `0f`; `GameAIMap.ProducerValue("nope") == 0f`; a map built (as `BlockadeBreakSelfCheck.Scenario` does) from spawn data whose `_resourceData` is the Verdant asset returns 1 for that planet.
- [ ] **Step 2: Owner runs `FlatSpace -> AI -> Run Blockade Initiation Self-Check`.** Expected: compile errors / FAIL.
- [ ] **Step 3: Implement** the fields (a comment each, as the neighbouring tunables do), the accessors, and the asset YAML values (Verdant 1, Desolate 1, Farm 0.5; Desert, Industrial and Ocean stay 0 because their output is local-only today).
- [ ] **Step 4: Owner re-runs it and `Run All AI Self-Checks`.** Expected: PASS.
- [ ] **Step 5: Commit** the new script and its `.meta`, `AllAISelfChecks.cs`, the constants, data class, `Planet.cs`, `GameAIMap.cs`, three assets ("feat: producer value by planet type, blockade initiation tunables").

### Task 2: Reusable hold-force sizing in `AssaultPlanner`

**Files:**
- Modify: `Assets/Flatspace/GameAI/AssaultPlanner.cs` (`PlanBlockadeForce` ~line 718, the private `DockedOffense`, new public methods)
- Test: `Assets/Editor/BlockadeInitiateSelfCheck.cs` (`RunHoldForceCheck`), plus the existing `BlockadeBreakSelfCheck` must still pass

**Interfaces:**
- Produces: `public List<ShipAction> AssaultPlanner.PlanHoldForce(Planet target, float neededOffense, List<ShipTransportPlanner.PlanetState> states, Dictionary<string,int> sentByOrigin)` (same sizing as `PlanBlockadeForce`, sets `LastBlockadeForce`); `public float AssaultPlanner.DockedOffenseOf(Planet planet, int owner)` (effective docked warship offense of `owner`, health-scaled as the private helper does); `public float AssaultPlanner.HoldNeededOffense(Planet target, int rival)` = `max(DockedOffenseOf(target, rival) x (1 + constants.blockadeBreakMargin) - my docked offense - GetIncomingOffense(WarShip, me), 0)`, but `0.001f` when that is 0 and I have no warship docked or incoming there (so one ship is sent); `PlanBlockadeForce` becomes `PlanHoldForce(target, NeededOffense(target), ...)` with unchanged behavior.

- [ ] **Step 1: Write the failing check** `RunHoldForceCheck` with `BlockadeBreakSelfCheck`-style scenario code (copy its small `Scenario` use or build via `WarshipSelfCheck` helpers; the check must not use `Gameboard.Instance`): (a) `PlanHoldForce` with `neededOffense` equal to two ships' offense returns two ships from the cheapest source and `LastBlockadeForce.StillNeeded == 0`; (b) with spare for one ship it sends one and `StillNeeded > 0`; (c) `neededOffense <= 0` returns no actions; (d) `HoldNeededOffense` on a planet holding 3 rival ships and none of mine equals `3 ships' offense x (1 + margin)`; with two of mine docked it falls by two ships' offense; on an empty planet it is `0.001f`; on an empty planet where I already dock a ship it is `0`; (e) `DockedOffenseOf` of a wounded ship is lower than a healthy one.
- [ ] **Step 2: Owner runs the new self-check.** Expected: compile errors / FAIL.
- [ ] **Step 3: Implement** the three public members; extract the body of `PlanBlockadeForce` into `PlanHoldForce` and have `PlanBlockadeForce` call it; generalise the private docked-offense helper to take an owner.
- [ ] **Step 4: Owner runs the new check, `Run Blockade Breaking Self-Check` and `Run All AI Self-Checks`.** Expected: PASS (the blockade-force checks prove behavior is unchanged).
- [ ] **Step 5: Commit** `AssaultPlanner.cs`, `BlockadeInitiateSelfCheck.cs` ("refactor: reusable hold-force sizing for initiated blockades").

### Task 3: `InitiatedBlockadeTracker` (hold state and release rules)

**Files:**
- Create: `Assets/Flatspace/GameAI/InitiatedBlockadeTracker.cs` (+ `.meta`)
- Test: `Assets/Editor/BlockadeInitiateSelfCheck.cs` (`RunTrackerCheck`)

**Interfaces:**
- Produces (namespace `FlatSpace.AI`, pure, log-only/state-only, never saved): `enum BlockadeKind { Dc, Lane, Colony }`; `struct InitiatedBlockadeTracker.Held { string Planet; BlockadeKind Kind; int Rival; string Stance; float Score; float Needed; int Ships; }`; `struct Change { bool Started; string Planet; BlockadeKind Kind; string EndReason; int TurnsHeld; Held Held; }`; `struct Skip { string Planet; BlockadeKind Kind; string Reason; }`;
  - `bool IsActive(string planet)`, `IReadOnlyList<string> ActivePlanets`, `BlockadeKind? KindOf(string planet)`;
  - `List<Change> Update(int turn, List<Held> active, Func<Held,string> endReason)` where `active` is this turn's selected set and `endReason(heldLastTurn)` returns the end reason for a previously active planet no longer in `active` (null means it was replaced while still valid; use "Outmatched"), returning an end for each dropped planet (with `TurnsHeld = turn - startTurn`) before the starts of new ones;
  - `List<Skip> UpdateSkips(List<Skip> skips)` returning only skips that are new or changed since the last call (on-change logging), dropping entries that disappeared;
  - lane cooling: `bool LaneStillHeld(string planet, int count, int endCount, int endTurns)` increments a per-planet counter of consecutive turns with `count < endCount` and returns false once it reaches `endTurns`; any turn at or above `endCount` resets it;
  - give-up counters: `int BumpGiveUp(string planet, string cause)` / `void ClearGiveUp(string planet)` counting consecutive turns of the same cause (Outmatched, Unreachable, NoSpare) and resetting when the cause changes or clears;
  - `int WantedShipsLastTurn { get; }` and `void RecordWantedShips(int ships)` (the fleet term source, Task 6).

- [ ] **Step 1: Write the failing check** `RunTrackerCheck`: a Start change on first sight carries the `Held`; no change while unchanged; dropping a planet yields an End with the reason from the callback and `TurnsHeld`; `UpdateSkips` returns a repeated identical skip once only and a changed reason again; `LaneStillHeld` with end count 2, end turns 3: counts 1,1 stay held, a count 2 resets, then 1,1,1 releases on the third (Review Focus 3: a count oscillating 3,1,3,1 never releases); `BumpGiveUp` reaches 10 after ten consecutive calls with the same cause and restarts when the cause changes; `RecordWantedShips(5)` is read back and survives `Update`.
- [ ] **Step 2: Owner runs the self-check.** Expected: compile errors / FAIL.
- [ ] **Step 3: Implement** the class (state in dictionaries keyed by planet name).
- [ ] **Step 4: Owner runs it again.** Expected: PASS.
- [ ] **Step 5: Commit** the new script and `.meta`, `BlockadeInitiateSelfCheck.cs` ("feat: InitiatedBlockadeTracker").

### Task 4: `BlockadeInitiator` (valid rivals, kinds, ranking)

**Files:**
- Create: `Assets/Flatspace/GameAI/BlockadeInitiator.cs` (+ `.meta`)
- Test: `Assets/Editor/BlockadeInitiateSelfCheck.cs` (`RunInitiatorCheck`)

**Interfaces:**
- Consumes: `GameAIMap.Chokepoint(name)`, `IsChokepoint(name)`, `ProducerValue(name)` (Task 1), `InitiatedBlockadeTracker` / `BlockadeKind` (Task 3).
- Produces: `public static ISet<int> BlockadeInitiator.ValidRivals(int me, DiplomacyState diplomacy, IEnumerable<int> contact, IEnumerable<int> allOthers, GameAIConstants constants)` (diplomacy disabled: `allOthers`; else players at war plus contact players whose `diplomacy.Hostility(me, r) >= constants.blockadeInitiateHostility`; nobody if the value is above 100 and none are at war); `public static Dictionary<(string planet, int rival), int> BlockadeInitiator.LaneCounts(GameAIMap map, IEnumerable<GameAI.GameAIOrder> orders, ISet<int> rivals)` (for each rival-owned Food or Grotsits transport order, count 1 on every node of `map.GetPath(order.Origin, order.Target).PathNodes`, endpoints included); `struct BlockadeInitiator.Candidate { string Planet; BlockadeKind Kind; int Rival; float Score; }`; `struct BlockadeInitiator.Inputs { ISet<int> Rivals; Dictionary<string,int> DcOwner /*planet -> rival that has it as a DC*/; Dictionary<(string,int),int> Lanes; Func<Planet,bool> ColonyStartAllowed; InitiatedBlockadeTracker Tracker; }`; `public static BlockadeInitiator.Selection BlockadeInitiator.Select(GameAIMap map, int me, Inputs inputs)` where `Selection { List<Candidate> Active; List<InitiatedBlockadeTracker.Skip> Skipped; }`.

- [ ] **Step 1: Write the failing check** `RunInitiatorCheck` (pure fixtures, `Scenario`-style map like `BlockadeBreakSelfCheck.Scenario.Hub`; no `Gameboard.Instance`; `constants.blockadeInitiateMaxActive` set per case):
  - `ValidRivals`: at war yes; peace at hostility 59 with value 60 no, at 60 yes; value 101 and no war: empty; diplomacy disabled: all others (Review Focus 4).
  - DC candidate: a known planet in `DcOwner` of a valid rival is a `Dc` candidate; an unknown (not in `Knowledge`) planet or one of an invalid rival is not.
  - Lane candidate: count 4 (start 4) qualifies, 3 does not; an already-active lane at count 2 (end value) stays, at 1 does not until the tracker's `LaneStillHeld` says so; `LaneCounts` over two in-flight grotsits orders A->H->X1 and A->H->X2 gives H a count of 2 and A, X1, X2 a count of 1 each, and ignores my own and a non-rival's orders and non-transport orders.
  - Colony candidate: an unpopulated, unowned, known planet with `ColonyStartAllowed` true and value `Chokepoint x blockadeColonyChokepointWeight + ProducerValue x blockadeColonyProducerWeight >= blockadeColonyMinValue`; value below the minimum, populated planets, or `ColonyStartAllowed` false (for a not-yet-active target) are excluded; an already-active Colony target stays while it is unpopulated even if `ColonyStartAllowed` is false; a Colony target needs at least one valid rival.
  - Ranking and caps: all `Dc` before `Lane` before `Colony`; within a kind by `Score` descending, ties by name ordinal; already-active targets first within the cap; `Active.Count <= blockadeInitiateMaxActive` and the rest appear in `Skipped` with `OverCap`; `blockadeInitiateMaxActive = 0` gives no candidates.
- [ ] **Step 2: Owner runs the self-check.** Expected: compile errors / FAIL.
- [ ] **Step 3: Implement.** DC score = the planet's chokepoint percentile + `ProducerValue`; lane score = the count, ties by percentile; colony score = the value formula. A Dc target is a planet in `DcOwner` (the rival's own DC); a Lane target is any known planet on a rival transport path, whoever owns it (the force sizing and the safety gate are Task 6).
- [ ] **Step 4: Owner runs it again, then `Run All AI Self-Checks`.** Expected: PASS.
- [ ] **Step 5: Commit** the new script and `.meta`, `BlockadeInitiateSelfCheck.cs` ("feat: BlockadeInitiator target selection").

### Task 5: Log lines

**Files:**
- Modify: `Assets/Flatspace/Diagnostics/AITuningLogger.cs` (after `LogBlockadeTargetEnd`)
- Test: `Assets/Editor/BlockadeInitiateSelfCheck.cs` (`RunFormatCheck`)

**Interfaces:**
- Produces: `public static string AITuningLogger.FormatBlockadeInitiateStart(int turn, int playerId, string planet, string kind, int rival, string stance, float score, float needed, int ships)`, `...FormatBlockadeInitiateEnd(int turn, int playerId, string planet, string kind, string reason, int turnsHeld)`, `...FormatBlockadeInitiateSkipped(int turn, int playerId, string planet, string kind, string reason)` each with a `Log...` twin that no-ops without an open log (the pattern of `FormatBunkerBuilt`/`LogBunkerBuilt`).

- [ ] **Step 1: Write the failing check**: `FormatBlockadeInitiateStart(40, 1, "Ocean 2", "Dc", 0, "War", 1.5f, 33f, 3) == "T40|P1|BlockadeInitiateStart|Ocean 2|Dc|0|War|1.5|33|3"`; End `"T52|P1|BlockadeInitiateEnd|Ocean 2|Dc|LostDc|12"`; Skipped `"T40|P1|BlockadeInitiateSkipped|Farm 1|Lane|NoSpare"` (floats `0.##` invariant).
- [ ] **Step 2: Owner runs the self-check.** Expected: compile errors / FAIL.
- [ ] **Step 3: Implement** the six methods.
- [ ] **Step 4: Owner runs it again.** Expected: PASS.
- [ ] **Step 5: Commit** `AITuningLogger.cs`, `BlockadeInitiateSelfCheck.cs` ("feat: BlockadeInitiate tuning log lines").

### Task 6: `PlayerAI` integration and the fleet term

**Files:**
- Modify: `Assets/Flatspace/GameAI/PlayerAI.cs` (`PlanShipActions` ~line 2511, `WantedWarships` ~line 1747, new members)
- Test: `Assets/Editor/BlockadeInitiateSelfCheck.cs` (`RunPlanIntegrationCheck`, `RunFleetTermCheck`)

**Interfaces:**
- Consumes: Tasks 1 to 5.
- Produces: `PlayerAI._initiated` (`InitiatedBlockadeTracker`); `public Func<int, IEnumerable<string>> PlayerAI.RivalDistributionCenters { get; set; }` (default: the rival's `playerAI.FoodDistributionCenters` plus `GrotsitsDistributionCenters` via `Gameboard.Instance.players`, empty when no Gameboard; self-checks set it); `public bool PlayerAI.ColonyShipCanReach(Planet target)` (a docked colony ship of mine on a planet with a usable path of at most `maxPathNodesForColonization` nodes, or a colonist order of mine already in flight to the target); `public List<ShipAction> PlayerAI.PlanInitiatedBlockades(int turnNumber, AssaultPlanner assault, List<ShipTransportPlanner.PlanetState> states, List<ShipAction> alreadyPlanned)` (the initiate pass, returns the extra warship actions); `PlayerAI.WantedWarships()` adds `_initiated.WantedShipsLastTurn` after the existing `Math.Min(unbounded, ceiling)`.

- [ ] **Step 1: Write the failing checks** with `BlockadeBreakSelfCheck`-style `Scenario` fixtures that call `ai.PlanShipActions(turn)` (see `RunPlanShipActionsCheck` there for the setup):
  - `RunPlanIntegrationCheck`: (a) `blockadeInitiateMaxActive = 0`: the action list equals the list produced with the feature absent (compare Origin/Target/Count/Kind per action against a baseline built in the same check before enabling); (b) enabled, a rival DC planet I know and spare ships at a reachable origin: a warship action to it is added, the garrison, assault and escort actions in the list are unchanged and no origin sends more than its spare (`Spare - sent`); (c) with the arrival projection refusing (a stronger rival fleet docked there), no action is added and the skip is `Outmatched`; (d) no spare anywhere: no action, skip `NoSpare`; (e) the hold persists next turn (tracker `IsActive`), is dropped with `LostDc` when the planet stops being a DC, with `WarEnded` when the rival is no longer valid, with `Colonized` for a Colony target once populated, and with `Outmatched` after `blockadeInitiateGiveUpTurns` consecutive refused turns (Review Focus 1); (f) Expand never initiates (Review Focus 4).
  - `RunFleetTermCheck`: `WantedWarships()` is unchanged with no active blockades; after a turn with two active blockades needing 6 and 1 ships and `blockadeInitiateMaxShips = 4` it rises by 4 + 1 = 5 on the **next** call only (same-turn calls see last turn's value); never above `blockadeInitiateMaxActive x blockadeInitiateMaxShips` (Review Focus 5).
- [ ] **Step 2: Owner runs the self-check.** Expected: compile errors / FAIL.
- [ ] **Step 3: Implement.**
  - `PlanShipActions`: run `PlanInitiatedBlockades` last, after the conversion-hold audit and before `return actions` (so it only sees what is left and the audit is unaffected); skip it for non-Consolidate-like strategies (the early return already does).
  - `PlanInitiatedBlockades`: build `Inputs` (`ValidRivals` from `AIMap.Diplomacy`, `AIMap.Knowledge.ContactPlayers(AIMap, me)`; `DcOwner` from `RivalDistributionCenters` for each valid rival; `Lanes` from `BlockadeInitiator.LaneCounts(AIMap, InFlightOrders, rivals)`; `ColonyStartAllowed = p => !ColonyShipCanReach(p)`); `Select`; for each active target compute needed = `assault.HoldNeededOffense(planet, rival)` (the targeted rival; for Colony the largest valid-rival docked offense), call `assault.PlanHoldForce(planet, needed, states, sentByOrigin)` where `sentByOrigin` sums the warship `alreadyPlanned` actions per origin, keep each returned action only if `ArrivalNotOutmatched(origin, planet, count, plannedSoFar)` holds (else the target is `Outmatched` this turn), advance the tracker's give-up counters (`Outmatched`, `Unreachable` when no path, `NoSpare` when nothing was sent and none docked there), feed `Tracker.Update` with the end-reason callback (`Colonized`, `LostDc`, `LaneCooled`, `WarEnded`, else the give-up cause) and log Start/End/Skipped through Task 5's methods, and call `_initiated.RecordWantedShips(sum over active of min(blockadeInitiateMaxShips, max(1, ceil(needed / offensePerWarship))))` using the same offense-per-ship the Warship sizing uses.
  - The initiated actions use the ordinary ship-order emission (`EmitShipOrders`), no new order types.
- [ ] **Step 4: Owner runs the new checks, `Run Blockade Breaking Self-Check`, `Run Planet Defense Self-Check` and `Run All AI Self-Checks`.** Expected: PASS. A fixture that now sees initiated actions sets `blockadeInitiateMaxActive = 0` in its constants (`WarshipSelfCheck.MakeConstants` already carries the other feature switches; add it there so older checks keep their meaning).
- [ ] **Step 5: Commit** `PlayerAI.cs`, `WarshipSelfCheck.cs`, `BlockadeInitiateSelfCheck.cs` ("feat: AI-initiated blockades in PlanShipActions, fleet term in WantedWarships").

### Task 7: Docs and skill

**Files:**
- Modify: `.claude/skills/tuning-log/SKILL.md` (new "Initiated blockades" bullet near the blockade-breaking bullets), `CLAUDE.md` (AI Tuning Log line list), `Assets/Flatspace/GameAI/CLAUDE.md` (new "Initiated blockades" subsection under "Warships and Blockade"; producer value paragraph in "Connectivity (chokepoints)" changed from "agreed but deliberately not built" to built), `FUTURE_FEATURES.md` (note built items), `docs/superpowers/specs/2026-10-22-ai-initiated-blockades-design.md` (the End reason names)

- [ ] **Step 1:** document the three log lines, the three kinds and their eligibility, the valid-rival gate, the spare-only and give-up rules, the fleet term and constants, producer value, and the analysis the skill should run: per run, starts and ends by kind and reason (turnsHeld distribution, repeat Start on one planet = flapping), peace-time starts and the `Stance ... War` that followed within 20 turns, rival `OrderBlocked`/`ShipmentCancelled`/shortage counts at held planets before and during a hold, defense and conquest figures (`PlanetDead`, `OwnerChanged`, `ChokepointGarrison`) against the 10-22 chokepoint batches, `WarshipBoost` wanted/have, and the fleet-cap invariant.
- [ ] **Step 2:** update the spec's End reason list to the names in Global Constraints.
- [ ] **Step 3: Commit** the docs ("docs: AI-initiated blockades, producer value").

### Task 8: Play verification

**Files:** none.

- [ ] **Step 1: Owner runs 6 Play runs each on `4p.json` and `test2.json` with `_logAIEvents` on** (restart Play mode after the recompile).
- [ ] **Step 2: Read them with `/tuning-log`** against the 2026-10-22 chokepoint batches (4p 15:09 to 15:39, test2 15:46 to 16:02): the spec's success measure and the side-effect table. Report numbers and symptoms before proposing retuning.
