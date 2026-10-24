# Human Player Mode (project 1) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** One human can control one player (fleets, colonization, production and research queues, diplomacy, optional automation of shipments and per-planet development) through a command layer driven by a temporary JSON file, with fog view, pause events, defeat and a turn limit.

**Architecture:** A pure command layer (`FlatSpace.Human.HumanCommandProcessor`) validates a command against a `GameAIMap` and the human's `PlayerAI`, and emits the same `GameAI.GameAIOrder`s the AI emits (reusing `EmitShipOrders`, an extracted colonist emitter, `OrderTypeIndustrySetProduction`, the stance orders). The human's `PlayerAI` keeps its catalogs and research accounting but a `HumanControlled` flag replaces its decision routine with a reduced one. Everything is gated on that flag, so a zero-human match runs the unchanged code path.

**Tech Stack:** Unity 6000.4.1f1, C#, `JsonUtility`, UI via code-built uGUI popups (as `RandomBoardPopup`), Editor self-checks (`[MenuItem]`, no test framework).

**Spec:** `docs/superpowers/specs/2026-10-23-human-player-mode-design.md` (read it and `CLAUDE.md` + `Assets/Flatspace/GameAI/CLAUDE.md` first). Roadmap entry: `FUTURE_FEATURES.md`, "Human player mode". Rule: use or modify existing components where feasible.

## Global Constraints

- Zero-human matches must behave exactly as today: every new branch is gated on `PlayerAI.HumanControlled`/`Player.isHuman`; `Run All AI Self-Checks` must still pass unchanged.
- Self-checks never use `Gameboard.Instance`; methods a self-check calls are `public` (not `internal`); new scripts are committed with their `.meta`.
- `OrderType` and `UpdateResultType` values are appended last (serialized as ints). No `Nullable<T>` in saved structs (`JsonUtility` skips them).
- **A missing key reads as 0/false in an older save**, so every new saved flag is defined so that 0/false means "as before" (e.g. `manualDevelopment`, not `autoDevelopment`; `hasHuman` plus `humanPlayerId`).
- Subtype strings are case-exact: `"Warship"`, `"Siegeship"`, `"Bunker"`, `"ColonyShip"`.
- Valid fleet/colonize targets: known (`GameAIMap.Knowledge.IsKnown`) and `2 <= NumNodes <= maxPathNodesForShipTransport` (ships) / `maxPathNodesForColonization` (colonists); `PathingSystem.FindPath` returns a 1-node stub when there is no route, never a free adjacent trip.
- A `Delayed` ship order needs `TimingDelay >= 1` (use `EmitShipOrders`, which clamps).
- Turn-limit presets 100, 200, 400, Unlimited (0); default 400. Defeat: no populated planets, no docked colony ship, no colonist in flight.
- Namespace: new human files use `FlatSpace.Human`; edited files keep their own casing. Stage by explicit path (never `git add -A Assets`); commit/push only when the user asks.
- Unity cannot run from the command line: "run" steps mean the user runs the named `[MenuItem]` and reads the Console (`Editor.log` is readable).

## Review Focus

Input classes the spec implies but no obvious test exercises; each is pinned by a test in the named task.

1. Older save (no human keys) loads as a zero-human, auto-development, 400-limit match with AI planets still developing (Task 2, 6).
2. Command naming a planet the human does not own, or ships that are not theirs, or a planet that no longer exists after a conquest (Task 3, 4).
3. Two commands in one batch touching the same ships (move 3 of 4 warships, then move 3 again): the second must be refused for lack of ships, not throw (Task 4).
4. Colonize with no colony ship docked, a populated-to-max target, or an origin that cannot pay the food rider (Task 5).
5. A queued item that stops being valid before it is built (superseded tier, bunker over the population cap) and an empty queue on a manual planet (Task 6).
6. A malformed or empty commands file, or one that names an unknown command type: reported, never thrown (Task 3).

## Tuning log additions (rule 4)

All lines use `T<turn>|P<playerId>|<Code>|...`; they fire per event (a command is a one-off), so no tracker is needed except where noted. Added in Task 13 together with the `tuning-log` skill and `CLAUDE.md` "AI Tuning Log" updates.
- `HumanCommand|<type>|<origin>|<target>|<detail>` per applied command. Answers: what did the human do, and in what order.
- `HumanCommandRefused|<type>|<reason>` per refused command. Answers: which rule blocks the human most.
- `HumanPause|<event>|<planet>` when the timer pauses itself. Answers: whether the default event set is too noisy.
- `T0|P-1|MatchSetup|human=<id|none>|turnLimit=<n>` once per init (the LAST one is real, like `BoardConfig`).
- `HumanDefeat|<populated>|<colonyShips>|<colonists>` once; `TurnLimitReached|<limit>` once.
Log-only de-duplication (once-only flags) lives on `Gameboard`/`HumanTurnEvents` and is not saved, so a load may log them again.

---

## File structure

| File | Responsibility |
|---|---|
| `Assets/Flatspace/Player/MatchSettings.cs` (new) | Static match setup carried across the scene load: turn limit, human requested, human id. |
| `Assets/Flatspace/MainMenu/MatchSetupPopup.cs` (new) | First popup (Human yes/no, turn limit dropdown); chains to `RandomBoardPopup`. |
| `Assets/Flatspace/MainMenu/HumanPlayerPicker.cs` (new) | Picker shown after the board loads. |
| `Assets/Flatspace/Player/Human/HumanCommand.cs` (new) | Flat serializable command + result types, file read/write. |
| `Assets/Flatspace/Player/Human/HumanCommandProcessor.cs` (new) | Pure validation and order emission for every command. |
| `Assets/Flatspace/Player/Human/HumanTurnEvents.cs` (new) | Pure event collection and pause decision. |
| `Assets/Flatspace/Player/Human/HumanStatus.cs` (new) | Pure defeat test. |
| `Assets/Editor/HumanModeSelfCheck.cs` (new) | This feature's self-check (`FlatSpace -> Human -> Run Human Mode Self-Check`). |
| Modified: `Player.cs`, `PlayerAI.cs`, `GameAI.cs`, `GameBoard.cs`, `Planet.cs`, `SaveLoadSystem.cs`, `GameAIMap.cs` (restore), `MainMenu.cs`, `AITuningLogger.cs`, `AllAISelfChecks.cs`, docs, `tuning-log` skill. |

---

### Task 1: Match settings and the first popup

**Files:**
- Create: `Assets/Flatspace/Player/MatchSettings.cs`, `Assets/Flatspace/MainMenu/MatchSetupPopup.cs`, `Assets/Editor/HumanModeSelfCheck.cs`
- Modify: `Assets/Flatspace/MainMenu/MainMenu.cs:19` (`StartGame` shows `MatchSetupPopup` first), `Assets/Flatspace/MainMenu/RandomBoardPopup.cs` (unchanged flow; the setup popup calls `RandomBoardPopup.Show()` on OK), `Assets/Flatspace/Objects/Board/GameBoard.cs:816` (`TimedUpdate`), `AllAISelfChecks.cs`

**Interfaces:**
- Produces: `public static class MatchSettings { public static readonly int[] TurnLimitPresets = {100,200,400,0}; public const int DefaultTurnLimit = 400; public static int TurnLimit; public static bool HumanRequested; public static int HumanPlayerId = -1; public static int ClampTurnLimit(int v); public static void Reset(); }` (0 = Unlimited).
- Produces: `public static bool HumanModeSelfCheck.RunChecks()`.

- [ ] **Step 1: Write failing checks** in `HumanModeSelfCheck.RunChecks`: `ClampTurnLimit(250)==400`? No: `ClampTurnLimit` maps any value not in the presets to `DefaultTurnLimit` (assert `ClampTurnLimit(250)==400`, `ClampTurnLimit(0)==0`, `ClampTurnLimit(100)==100`); `Reset()` gives `TurnLimit==400`, `HumanRequested==false`, `HumanPlayerId==-1`.
- [ ] **Step 2: Run** `FlatSpace -> Human -> Run Human Mode Self-Check`; expect a compile error / missing type.
- [ ] **Step 3: Implement `MatchSettings`** as above (plain static class, no MonoBehaviour; `AITuningLogger.EnabledViaMainMenu` is the precedent).
- [ ] **Step 4: Implement `MatchSetupPopup`** (code-built under the menu `Canvas`, same helper style as `RandomBoardPopup`: dim panel + box): a Human toggle, a `TMP_Dropdown` with 100/200/400/Unlimited defaulting to 400, OK writes `MatchSettings.HumanRequested`/`TurnLimit` then calls `RandomBoardPopup.Show()`; Cancel hides. `MainMenu.StartGame` creates and shows it before the existing popup. Reuse `RandomBoardPopup`'s private UI helpers by making them `internal static` on a small shared `PopupUi` class only if copying them would exceed ~30 lines; otherwise duplicate.
- [ ] **Step 5: Turn limit in `Gameboard.TimedUpdate`:** replace the literal `400` with `MatchSettings.TurnLimit` (0 = never); keep the repeat-run behaviour for the test harness. Add `public bool TurnLimitReached { get; private set; }` set when `TurnNumber >= limit`; `SingleUpdate` returns early (no turn) once reached.
- [ ] **Step 6: Register** the self-check in `AllAISelfChecks.cs`; run it and `Run All AI Self-Checks`. Expected: all pass.
- [ ] **Step 7: Commit** (only when the user asks): `MatchSettings.cs`+`.meta`, `MatchSetupPopup.cs`+`.meta`, edited files.

### Task 2: Human flag, picker, and saves

**Files:**
- Create: `Assets/Flatspace/MainMenu/HumanPlayerPicker.cs`
- Modify: `Player.cs`, `PlayerAI.cs:161` (`ProcessResults`), `GameBoard.cs` (init paths ~171-200, Awake), `SaveLoadSystem.cs` (`GameSave`, `PlayerSave`, save/restore), `AITuningLogger.cs`

**Interfaces:**
- Produces: `Player.isHuman` (bool), `PlayerAI.HumanControlled { get; set; }`, `Gameboard.SetHumanPlayer(int id)`, `Gameboard.HumanPlayerId` (int, -1 none), `GameSave.hasHuman`, `GameSave.humanPlayerId`, `GameSave.turnLimit`, `GameSave.hasMatchSettings`.
- `HumanPlayerPicker.Show(Canvas canvas, IReadOnlyList<(int id, string homePlanet)> players, Action<int> onPicked)`; players come from `GameAI.GetPlayerCapitol(playerID)`.

- [ ] **Step 1: Failing checks:** (a) a `GameSave` built with `hasHuman=false` leaves `MatchSettings` at its defaults after the restore helper `ApplyMatchSettings(GameSave)`; (b) with `hasHuman=true, humanPlayerId=1, turnLimit=200, hasMatchSettings=true` it sets `HumanRequested`, `HumanPlayerId==1`, `TurnLimit==200`; (c) an old JSON string with none of the keys parses (`JsonUtility.FromJson<GameSave>`) to `hasHuman==false`.
- [ ] **Step 2: Run** the self-check; expect failure.
- [ ] **Step 3: Implement** the `GameSave`/`PlayerSave` fields and `SaveLoadSystem.ApplyMatchSettings(GameSave)` (public static). `SaveGame` writes the current `MatchSettings` and the human id.
- [ ] **Step 4: `PlayerAI.ProcessResults` branch:** `if (HumanControlled) { ProcessResultsHuman(results, orders); return; }` placed first. `ProcessResultsHuman` is implemented in Task 8; stub it to call `RecordLosses` and `RefreshBlockadeView` only.
- [ ] **Step 5: `Gameboard.SetHumanPlayer(int id)`** sets `players[id].isHuman`, its `PlayerAI.HumanControlled`, `MatchSettings.HumanPlayerId`, `owningPlayerId=id`. After `InitGame*` completes for a new game with `HumanRequested && HumanPlayerId<0`, create the picker; picking calls `SetHumanPlayer`. Loading a save calls `SetHumanPlayer(savedId)` instead (no picker).
- [ ] **Step 6: Log** `T0|P-1|MatchSetup|human=..|turnLimit=..` via a new `AITuningLogger.LogMatchSetup(int humanId, int turnLimit)` called after the human is settled.
- [ ] **Step 7: Run** the self-check and `Run All AI Self-Checks`; then a Play run with "No human": expect the same behaviour as before (no picker). Commit when asked.

### Task 3: Command types, JSON file, runner

**Files:**
- Create: `Assets/Flatspace/Player/Human/HumanCommand.cs`, `HumanCommandProcessor.cs`
- Modify: `GameAI.cs:154` (`GameAIUpdate`), `GameBoard.cs:715` (`SingleUpdate`), `AITuningLogger.cs`

**Interfaces:**
- Produces (flat, because `JsonUtility` has no polymorphism):
```csharp
[Serializable] public class HumanCommand {
  public string type;      // "move","colonize","queueProduction","removeProduction","moveProduction","queueResearch","removeResearch","setShipments","setDevelopment","declareWar","makePeace","surrender","setPause"
  public string origin, target, item, kind, eventName;
  public int count, index, rival, toIndex;
  public List<int> ships;  // dock indices at origin
  public bool flag;
}
[Serializable] public class HumanCommandBatch { public List<HumanCommand> commands = new List<HumanCommand>(); }
public class HumanCommandResult { public string type; public bool applied; public string reason; }
public class HumanCommandProcessor {
  public HumanCommandProcessor(GameAIMap map, PlayerAI human, int turn);
  public List<HumanCommandResult> Apply(IEnumerable<HumanCommand> commands, List<GameAI.GameAIOrder> ordersOut);
  public static List<HumanCommand> Parse(string json, out string error);   // never throws
}
```
- `GameAI.PendingHumanOrders` (`List<GameAIOrder>`), merged into `gameAIOrders` in `GameAIUpdate` after `ProcessResults(...)` and before `ProcessNewOrders` (the AI never sees them).
- `HumanCommandFile.Read(path)`/`WriteResults(path, results)`; file `HumanCommands/commands.json` and `results.json` under the project root in the editor (outside `Assets/`, gitignored) / `Application.persistentDataPath/HumanCommands` in a build.

- [ ] **Step 1: Failing checks:** `Parse("", ...)` returns an empty list with an error string and does not throw; `Parse("{bad", ...)` same; a batch with one unknown `type` yields one result `applied==false, reason=="UnknownCommand"`; an empty batch yields no orders.
- [ ] **Step 2: Run** the self-check; expect failure.
- [ ] **Step 3: Implement** the types, `Parse`, `Apply` (a switch that dispatches by `type` to per-command methods added in Tasks 4-9; unknown types refuse) and `HumanCommandFile`.
- [ ] **Step 4: `Gameboard.SingleUpdate`:** before `GameAI.GameAIUpdate()`, when a human is set: read `commands.json` (if present and newer than the last processed write time), run `Apply` into `GameAI.PendingHumanOrders`, write `results.json`, and rename the input to `commands.done.json`. `GameAIUpdate` appends `PendingHumanOrders` then clears it.
- [ ] **Step 5: Logging:** `AITuningLogger.LogHumanCommand(turn, player, type, origin, target, detail)` and `LogHumanCommandRefused(turn, player, type, reason)`, called from the runner for each result.
- [ ] **Step 6: Run** the self-check; Play run with a hand-written `commands.json` containing an unknown command; expect `HumanCommandRefused|...|UnknownCommand` in the log. Commit when asked.

### Task 4: Fleet commands (move, merge, split)

**Files:**
- Modify: `HumanCommandProcessor.cs`, `Planet.cs:1119` (add the ship-selection helper beside `PeekShipSnapshots`), `Assets/Flatspace/UI/FleetSummary.cs` (query helper)

**Interfaces:**
- Consumes: `PlayerAI.EmitShipOrders(ShipAction, List<GameAIOrder>, int skipShips=0)`, `Planet.PeekShipSnapshots/UndockShips` (take the first `count` ships of a kind and owner).
- Produces: `public void Planet.PrioritizeShips(Ship.ShipKind kind, int owner, IList<int> dockIndices)` (stable reorder so the chosen ships lead that kind/owner's docked ships; the departure trio then takes them), `public static List<HumanShipInfo> HumanFleetView.Docked(Planet p, int owner)` where `HumanShipInfo { int DockIndex; Ship.ShipKind Kind; float Damage; bool Damaged; }`, `public static List<HumanFlightInfo> HumanFleetView.InFlight(IEnumerable<GameAI.GameAIOrder> orders, int owner)` (`Kind`, `Count`, `Origin`, `Target`, `TurnsLeft`).

- [ ] **Step 1: Failing checks** (build a 3-planet chain `GameAIMap` with distinct positions, a human `PlayerAI`, a planet with 4 docked warships of which dock indices 1 and 3 are damaged):
  - `move` `ships:[1,3]` produces one `OrderTypeShipTransport` whose `Fleet.Damage` has two nonzero entries, plus the departure and in-progress orders, and `TimingDelay >= 1`.
  - `move` with `kind:"Warship", count:2` and no `ships` takes the two **least damaged**.
  - a second `move` of 3 in the same batch after 3 of 4 were sent is refused `NotEnoughShips`.
  - refused with reasons `NotYourPlanet`, `UnknownPlanet`, `TargetUnknown` (not in the human's knowledge), `OutOfRange` (beyond `maxPathNodesForShipTransport`), `SameDestination`.
  - `HumanFleetView.Docked` flags `Damaged` where `Damage>0`; `InFlight` lists the order just made.
- [ ] **Step 2: Run** the self-check; expect failure.
- [ ] **Step 3: Implement** `PrioritizeShips`, the two view helpers, and the `move` handler: resolve ships (explicit indices, else kind+count healthiest first), count claims per `(origin,kind)` within the batch so later commands see earlier ones (the `claimedByOrigin` pattern in `ProcessShipActions`), build a `ShipAction{Origin,Target,Cost=path cost,Count,Kind}` and call `EmitShipOrders` with the skip count. Merge is simply a move to a planet where the human already has ships; there is no separate command.
- [ ] **Step 4: Run** the self-check; expect pass. Commit when asked.

### Task 5: Colonize command

**Files:**
- Modify: `PlayerAI.cs:874-1100` (extract the colonist emission), `HumanCommandProcessor.cs`

**Interfaces:**
- Produces: `public void PlayerAI.EmitColonistOrders(string origin, string target, RoutePlanner.PlannedRoute route, List<GameAI.GameAIOrder> orders)` which emits exactly what the loop at `PlayerAI.cs:1065-1100` emits for a launch with no escort: the delayed `OrderTypePopulationTransport` (route attached, delay `Convert.ToInt32(route.Cost / defaultTravelSpeed)`, at least 1), `OrderTypePopulationChange` (-1 at the origin), `OrderTypePopulationTransferInProgress`, `OrderTypeRemoveShip`, and, when `Planet.NeedsColonyFoodRider` is true for the target, the `OrderTypeColonyFoodRider` plus `OrderTypeFoodChange` (-`colonyFoodRider`) pair. `ProcessColonizers` is changed to call it for its non-escort part; behaviour for the AI must be identical.

- [ ] **Step 1: Failing checks:** `colonize` origin with a docked colony ship, a known empty target within range, produces the four orders; a Desolate-type target (`NeedsColonyFoodRider`) adds the rider pair and the food deduction; refused `NoColonyShip`, `TargetNotValid` (`!IsValidColonizationTarget`), `CannotPayRider` (`!CanSupportColony`), `NoRoute` (`RoutePlanner.PlanRoute` null), `OutOfRange`.
- [ ] **Step 2: Run**; expect failure. Run `Run All AI Self-Checks` first as the regression baseline.
- [ ] **Step 3: Extract** `EmitColonistOrders` and call it from `ProcessColonizers`; implement the `colonize` handler (route via `RoutePlanner.PlanRoute(AIMap, origin, target, blockadeView, maxNodes)`).
- [ ] **Step 4: Run** the new checks and `Run All AI Self-Checks` (`PlayerAIResourceSelfCheck`, `BlockadeAvoidanceSelfCheck`, `ColonistRedirectSelfCheck` must still pass). Commit when asked.

### Task 6: Production queue and per-planet development switch

**Files:**
- Modify: `Planet.cs` (flag, validity, drop result, `UpdateResultType` appended), `PlayerAI.cs:2451` (`BuildIndustryMatrix` filter), `SaveLoadSystem.cs` (`PlanetSave.manualDevelopment`), `GameAIMap.cs` (restore in `SetPlanetSimulationStats`), `HumanCommandProcessor.cs`

**Interfaces:**
- Produces: `public bool Planet.ManualDevelopment` (default false = the AI chooses, as today); `public bool Planet.IsProductionItemValid(CatalogItem item)` = `item.researched`, not already in `CompletedImprovements`, not `IsImprovementSuperseded(item)`, and for subtype `"Bunker"` `CanBuildBunker`; new `UpdateResultTypeProductionDropped` (appended last; `Data` = item name, `PlayerID` = owner), appended when `UpdateProductionQueue` discards an invalid head of a `ManualDevelopment` planet's queue.
- Commands: `queueProduction{origin=planet,item}` (emits `OrderTypeIndustrySetProduction`, which calls `ScheduleProductionItem` with the fixed cost exactly as for the AI), `removeProduction{origin,index}`, `moveProduction{origin,index,toIndex}` (both mutate `Planet.ProductionQueue` directly), `setDevelopment{origin,flag}` (flag true = automatic).
- Defaults: a planet whose owner becomes a human player gets `ManualDevelopment=true`; any other planet keeps false. `Planet` ownership change already clears `CurrentProduction` and `ProductionQueue` (`Planet.cs:163`).

- [ ] **Step 1: Failing checks:** `queueProduction` of an unresearched item refused `NotUnlocked`; of an already-built improvement `AlreadyBuilt`; of a superseded tier `Superseded`; a valid one appends to the queue after the executed orders; `removeProduction`/`moveProduction` reorder and refuse a bad index; with `ManualDevelopment=true` a planet with a queue-empty result gets **no** `IndustrySetProduction` from `BuildIndustryMatrix`, with false it does (the AI path); an old-JSON `PlanetSave` restores `ManualDevelopment==false`; a queue head that became invalid is dropped and a `ProductionDropped` result is appended.
- [ ] **Step 2: Run**; expect failure.
- [ ] **Step 3: Implement** the flag, validity method, the filter in `BuildIndustryMatrix` (skip `productionCompleteResults` whose planet has `ManualDevelopment`; the AI player's planets never do), the dropped-head handling, save/restore, and the handlers. Add `Planet.cs:36`-style priority for the new result type (Medium).
- [ ] **Step 4: Run** the self-check and `Run All AI Self-Checks`. Commit when asked.

### Task 7: Research queue

**Files:**
- Modify: `PlayerAI.cs:1555` (`ChooseNewResearch`), `SaveLoadSystem.cs` (`PlayerSave.researchQueue: List<string>`), `HumanCommandProcessor.cs`

**Interfaces:**
- Produces: `public List<string> PlayerAI.ResearchQueue`; for a `HumanControlled` player `ChooseNewResearch` sets `currentResearch` to the first queue entry that is still unresearched with its `requiredTech` researched (removing used or invalid entries) and emits `OrderTypeResearchSet`; if none, `currentResearch=null` (research accumulates in `researchTotal`, i.e. stalls) and `PlayerAI.ResearchIdle` is true. Commands `queueResearch{item}` (refused `UnknownItem`, `AlreadyResearched`, `AlreadyQueued`) and `removeResearch{index}`.
- AI players keep the existing roulette unchanged.

- [ ] **Step 1: Failing checks:** a human `PlayerAI` with `ResearchQueue=["A","B"]` and `UpdateResearch` called with enough points completes A then starts B; an empty queue leaves `currentResearch==null` and `ResearchIdle==true` while `researchTotal` still rises; an AI `PlayerAI` still rolls (existing `PlayerAIResourceSelfCheck`).
- [ ] **Step 2: Run**; expect failure.
- [ ] **Step 3: Implement** the branch in `ChooseNewResearch`, the queue, the commands, and save/restore (older save: null list restores empty).
- [ ] **Step 4: Run** the self-check and `Run All AI Self-Checks`. Commit when asked.

### Task 8: Reduced decision routine and shipments switch

**Files:**
- Modify: `PlayerAI.cs` (`ProcessResultsHuman`, `ShipmentsAutomated`), `SaveLoadSystem.cs` (`PlayerSave.shipmentsAutomated`), `HumanCommandProcessor.cs`

**Interfaces:**
- Produces: `public bool PlayerAI.ShipmentsAutomated` (a human default false; saved as `PlayerSave.shipmentsAutomated`, an older save reads false, which only matters for a human); `private void ProcessResultsHuman(results, orders)`: `TryEnterConsolidate`, `ApplyWarState`, `RecordLosses`, `RefreshBlockadeView`, then if `ShipmentsAutomated`: `UpdateDistributionCenters`, `ProcessFoodShortage`, `ProcessGrotsitsShortage`; always `ProcessResearch` and `ProcessIndustry` (industry self-limits to planets with `ManualDevelopment==false` by Task 6). Never: `ProcessColonizers`, `ProcessShipActions`, `UpdateDiplomacy`. Command `setShipments{flag}`.

- [ ] **Step 1: Failing checks:** a human player with a food-shortage and a food-surplus result emits **no** shipment orders when `ShipmentsAutomated=false` and a transport trio when true; with a `ColonizerReady` result it emits no colonist orders either way; it never emits a stance order; an AI player's output for the same results equals the pre-change output (compare against the current `ProcessResults` result in the same fixture).
- [ ] **Step 2: Run**; expect failure.
- [ ] **Step 3: Implement** `ProcessResultsHuman`, wire the Task 2 stub to it, add the command and the save field.
- [ ] **Step 4: Run** the self-check and `Run All AI Self-Checks`. Commit when asked.

### Task 9: Diplomacy commands

**Files:**
- Modify: `HumanCommandProcessor.cs`

**Interfaces:**
- Consumes: `OrderTypeDeclareWar`, `OrderTypeMakePeace`, `OrderTypeSurrender` (immediate, `Data` = rival id as `int`, empty origin/target, never in `CurrentAIOrders`), `GameAI.ApplyStanceOrder`, `GameAI.ApplySurrender`, `DiplomacyState.IsAtWar`.
- Commands `declareWar{rival}`, `makePeace{rival}`, `surrender{rival}`; refused `UnknownRival`, `SelfRival`, `NoContact` (rival not in `Knowledge.ContactPlayers` for declare), `NotAtWar` (surrender), `Truce` (declare during `TruceUntil`).

- [ ] **Step 1: Failing checks** for each emission and each refusal using a two-player `GameAIMap` with diplomacy enabled and contact established.
- [ ] **Step 2: Run**; expect failure.
- [ ] **Step 3: Implement** the three handlers, mirroring the order construction in `PlayerAI.UpdateDiplomacy`.
- [ ] **Step 4: Run.** Also confirm by a Play run that an AI player can still declare war on the human (`Stance` log line with the human as the rival). Commit when asked.

### Task 10: Turn events

**Files:**
- Create: `Assets/Flatspace/Player/Human/HumanTurnEvents.cs`
- Modify: `GameAI.cs` (expose `LastTurnResults`, `LastExecutedOrders`, `LastBlockadeCuts`, `LastConversionReports`), `GameBoard.cs`

**Interfaces:**
- Produces:
```csharp
public enum HumanEventKind { PlanetAttacked, PlanetBlockaded, PlanetLost, FirstContact, WarDeclared, PeaceMade, FleetArrived, ShortageFood, ShortageGrotsits,
  ProductionComplete, ResearchComplete, ColonyShipReady, PlanetIdle, ResearchIdle, PopulationGained, PopulationLost, PlanetColonized, ProductionQueueEmpty,
  ShipmentLost, ConversionBegan, ConversionEnded, TransportOrdered }
public struct HumanEvent { public HumanEventKind Kind; public string Planet; public int Player; public bool Rival; }
public class HumanTurnEvents {
  public List<HumanEvent> Collect(GameAIMap map, int humanId, bool shipmentsAutomated, IList<Planet.UpdateResult> results,
      IList<GameAI.GameAIOrder> executed, IList<BlockadeSystem.BlockadeCut> cuts, IList<ConversionReport> conversions);   // first-contact state kept inside
}
```
  Rival events (the same kinds caused by another player) are included only for planets the human can see (`Knowledge.IsKnown` and the fog `Classify == Visible` equivalent: use `Knowledge.IsKnown` in the pure layer). New research and new production starts are never events. `TransportOrdered` only when `shipmentsAutomated`.

- [ ] **Step 1: Failing checks** (fixtures of hand-built results and orders): a `FoodShortage` result for a human planet yields `ShortageFood`; for a rival planet the human does not know yields nothing; an executed `OrderTypeShipTransport` arriving at a human planet yields `FleetArrived`; a `Conversion` result begin/end; a rival `DeclareWar` against any visible player yields `WarDeclared`; `FirstContact` fires once on the turn `ContactPlayers` gains a member and not again; `TransportOrdered` absent when `shipmentsAutomated=false`.
- [ ] **Step 2: Run**; expect failure.
- [ ] **Step 3: Implement** `Collect`; make `GameAI` keep the four `Last*` lists at the end of `GameAIUpdate` (read-only getters; clear at the start of each turn).
- [ ] **Step 4: Run.** Commit when asked.

### Task 11: Pause settings and timer integration

**Files:**
- Modify: `HumanTurnEvents.cs`, `GameBoard.cs:816` (`TimedUpdate`), `SaveLoadSystem.cs` (`PlayerSave.pauseEvents: List<int>` plus `hasPauseEvents`), `HumanCommandProcessor.cs`, `AITuningLogger.cs`

**Interfaces:**
- Produces: `public class HumanPauseSettings { public bool IsEnabled(HumanEventKind k); public void Set(HumanEventKind k, bool on); public static HumanPauseSettings Defaults(); public bool ShouldPause(IEnumerable<HumanEvent> events, out HumanEvent first); }` with defaults on: `PlanetAttacked, PlanetBlockaded, PlanetLost, FirstContact, WarDeclared, PeaceMade, FleetArrived, ShortageFood, ShortageGrotsits`; off: `ProductionComplete, ResearchComplete, ColonyShipReady, PlanetIdle, ResearchIdle` (and the pure-message kinds are never pause kinds). Command `setPause{eventName,flag}` (refused `UnknownEvent`). `Gameboard.TimedUpdate` stops after a `SingleUpdate` when `ShouldPause` is true and logs `HumanPause`.

- [ ] **Step 1: Failing checks:** `Defaults()` enables exactly the nine kinds above; `Set` toggles; `ShouldPause` is false for a message-only event and true for a defaults-on one; saved pause list round-trips; an older save without the key restores defaults (`hasPauseEvents` false).
- [ ] **Step 2: Run**; expect failure.
- [ ] **Step 3: Implement** the class, the timer check (stepping mode is unaffected), the command, the save fields, and `AITuningLogger.LogHumanPause`.
- [ ] **Step 4: Run** the self-check; Play run: timer mode stops on a food shortage for the human; stepping mode still advances one turn. Commit when asked.

### Task 12: Fog view, defeat, turn-limit screens

**Files:**
- Create: `Assets/Flatspace/Player/Human/HumanStatus.cs`
- Modify: `GameBoard.cs` (`SetHumanPlayer`, `SingleUpdate`), `GameButtonHandler` (fog dropdown), `AITuningLogger.cs`

**Interfaces:**
- Produces: `public static class HumanStatus { public static bool IsDefeated(GameAIMap map, IEnumerable<GameAI.GameAIOrder> inFlight, int playerId, out int populated, out int dockedColonyShips, out int colonistsInFlight); }` (a populated planet = any `Population` entry of the player; a colonist in flight = an `OrderTypePopulationTransport` with that `PlayerId`). `Gameboard` shows a code-built defeat popup once (`HumanDefeat` log) and a turn-limit popup (`TurnLimitReached` log), stops the timer, and keeps the board viewable.
- `SetHumanPlayer` calls `SetFogViewMode(FogViewMode.Player, id)` and locks the fog dropdown (hidden or disabled) while a human plays.

- [ ] **Step 1: Failing checks:** a player with one populated planet is not defeated; with none, a docked colony ship **or** a colonist order in flight is not defeated; with none of the three it is, and the out counts are right.
- [ ] **Step 2: Run**; expect failure.
- [ ] **Step 3: Implement** `HumanStatus`, the popups (same code-built style), the fog lock, the end-of-turn defeat and limit checks in `SingleUpdate`.
- [ ] **Step 4: Run** the self-check; Play run: the human view is fogged, the dropdown is locked, and the limit popup appears at the chosen turn. Commit when asked.

### Task 13: Logging, docs, and the full regression

**Files:**
- Modify: `AITuningLogger.cs` (any line not yet added), `.claude/skills/tuning-log/SKILL.md`, `CLAUDE.md` (a "Human player" section and the "AI Tuning Log" list), `Assets/Flatspace/GameAI/CLAUDE.md` (pointer), `FUTURE_FEATURES.md` / `completed_features.md` (mark project 1 done only when the user says so), `.gitignore` (`HumanCommands/`)

- [ ] **Step 1:** Add every log line in the "Tuning log additions" section and describe each in the skill file with its invariants (for example: every `HumanCommandRefused` has a reason from the command's closed set).
- [ ] **Step 2:** Write the `CLAUDE.md` section: the `HumanControlled` gate, the command file location and schema (list of command types and fields), the `manualDevelopment` inversion rationale, and that scoring and the UI rework are separate.
- [ ] **Step 3: Run** `Run Human Mode Self-Check` and `Run All AI Self-Checks`; both must pass.
- [ ] **Step 4: Play-mode acceptance** (user runs it): a human game on `4p.json` driven only by `commands.json`: move a split fleet, colonize, queue production and research, toggle shipments, declare war on an AI, step and run the timer, reach the turn limit. A zero-human run on the same board must match an earlier tuning log's shape (`/tuning-log`).
- [ ] **Step 5: Commit** when the user asks; stage by explicit path, including every `.meta`.

---

## Self-review notes

- **Spec coverage:** setup popups and limit (T1), picker and saves (T2), command layer and JSON (T3), fleets incl. damaged flag and in-flight summary data (T4), Colonize with rider (T5), queues, improvements data and per-planet switch (T6, T7), shipments switch and reduced routine (T8), diplomacy (T9), events and messages (T10), pause and timer/step (T11), fog, defeat and limit end (T12), logging and docs (T13). The "Improvements built" collapsed UI section and the click/drag interaction belong to project 2's UI; project 1 supplies the data (`Planet.CompletedImprovements`, `HumanFleetView`).
- **Spec correction:** the spec says queue items must be "affordable in industry"; industry is spent as progress, so there is no affordability gate and the validity rule is unlocked, not built, not superseded, and bunker cap (Task 6). Update the spec when this plan is approved.
- **Open assumption:** a planet newly owned by the human starts with development switched off (manual). The spec does not say; if the owner prefers automatic, change the default in Task 6 and nothing else.
