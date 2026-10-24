# Human Player UI, Stage 1 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** A human plays the notification loop without the JSON file: click a notification, land on the planet, and give orders from the planet UI (ship resources, production queue, colonize) and from a research tree.

**Architecture:** Every list a screen shows is built by a small pure class in `FlatSpace.Human` (no `Gameboard.Instance`, self-checked); the UI Toolkit screens are thin code-built views over those classes. Every button builds `HumanCommand`s and sends them through one new entry point, `Gameboard.SubmitHumanCommands`, which uses the existing `HumanCommandProcessor` and executes the resulting orders at once. Where a screen needs to know whether a command would be accepted it asks `HumanCommandProcessor.Check`.

**Tech Stack:** Unity 6000.4.1f1, C#, UI Toolkit (UIDocument, code-built `VisualElement`s), Editor self-checks.

**Spec:** `docs/superpowers/specs/2026-10-23-human-player-ui-design.md` (stage 1 and "Calls I made"). Builds on `docs/superpowers/specs/2026-10-23-human-player-mode-design.md` and the committed project 1 (`3f5d083`). Read `CLAUDE.md`, its "Human player mode" section, and `Assets/Flatspace/UI/MainGameScreenUI/` (`PlanetDetailUIController`, `NotificationListController`, `MainScreenUIController`, `GameButtonHandler`).

## Global Constraints

- Zero-human matches keep their current screens and behaviour: every new view, list and notification entry exists only while `Gameboard.HumanPlayerId >= 0`.
- No new order types and no rules in the UI. A screen that needs a yes/no on a command calls `HumanCommandProcessor.Check`; the processor's refusal at commit is shown to the human.
- New pure classes live in `Assets/Flatspace/Player/Human/` (namespace `FlatSpace.Human`) and never touch `Gameboard.Instance`; they are covered by `Assets/Editor/HumanUiSelfCheck.cs` (menu `FlatSpace -> Human -> Run Human UI Self-Check`, also added to `AllAISelfChecks`). Methods a self-check calls are `public`.
- UI Toolkit views live in `Assets/Flatspace/UI/HumanUI/` (namespace `Game.UI.MainGameScreenUI`), are built in code (no new UXML except the one edit in Task 6) and use the existing dark theme (black background, white text, red 2px border on panels).
- New scripts are committed with their `.meta`; stage by explicit path; commit/push only when the user asks.
- Spec values: notification entries are one per (planet, kind); state kinds are `ShortageFood`, `ShortageGrotsits`, `PlanetBlockaded`, `ConversionBegan`, `ProductionQueueEmpty`; a click dismisses an entry for the episode; the list is not saved; surplus = last turn's `...Surplus` update result x `Planet.GetPopulationFraction`; the shipping dialog lists only the human's planets within `maxPathNodesForResourceDistribution`; the food rider is never mentioned in the colonize list.
- Unity cannot run from the command line: "run" means the user runs the named `[MenuItem]` or a Play run, and `Editor.log` is read for the result. What no self-check can cover (layout, clicks, camera centring) is marked **Play check** and listed in Task 9.

## Review Focus

1. Clicking a notification whose planet changed hands or no longer exists: the planet UI opens read-only (order controls disabled), never throws (Task 3, 6).
2. A dialog committed after the state moved on (the source's stock changed, the colony ship left): the processor's refusal is shown and nothing partial is applied (Task 4, 7).
3. A shortage that lasts 30 turns: one entry, a count of 30, one pause at the start, none after; a dismissed entry stays gone until the shortage ends and returns (Task 3).
4. A research item with an unknown or cyclic `requiredTech`: the tree and the chain builder terminate and grey it out (Task 8).
5. Clicking inside a dialog or a list must not close the planet panel; clicking the board outside all of them does (Task 6).
6. Opening the screens in a watch-only match or before a player is picked: no exceptions, nothing shown (Task 6, 8).

## Tuning log additions (rule 4)

- `T<turn>|P<humanId>|HumanUiOrder|<screen>|<commands>|<refused>` once per `Gameboard.SubmitHumanCommands` call, where `<screen>` is `ShipResources`, `Production`, `Colonize` or `Research` (the caller's label), `<commands>` the number sent and `<refused>` how many were refused. It answers: which screens the human actually uses, and how often they hit a refusal. One-off per click, so no tracker. Added in Task 2; documented in Task 9 together with the `tuning-log` skill and the "AI Tuning Log" and "Human player mode" sections of `CLAUDE.md`.

---

## File structure

| File | Responsibility |
|---|---|
| `Player/Human/HumanCommandProcessor.cs` (modify) | add `Check(command)` and split `queueResearch` into validate and apply |
| `Player/Human/HumanNotificationList.cs` (new) | the per-(planet, kind) entry list and its fresh-event output; `HumanOngoing` |
| `Player/Human/HumanShipmentList.cs` (new) | sources for the shipping dialog |
| `Player/Human/HumanBuildList.cs` (new) | what Add item offers, costs and turn estimates |
| `Player/Human/HumanPlanetView.cs` (new) | net per-turn rates and the colonize target list |
| `Player/Human/HumanResearchTree.cs` (new) | the research tree, chains and estimates |
| `UI/HumanUI/*.cs` (new) | `HumanPlanetOrdersView`, `ShipResourcesDialog`, `AddItemDialog`, `ColonizeDialog`, `ResearchScreen`, `HumanStatusLine` |
| `Objects/Board/GameBoard.cs`, `GameAI/GameAI.cs` (modify) | `SubmitHumanCommands`, `ApplyOrdersNow`, notification wiring, `GoToNotification` |
| `UI/MainGameScreenUI/PlanetDetailUI.uxml`, `PlanetDetailUIController.cs`, `NotificationListController.cs`, `MainScreenUIController.cs` (modify) | scrolling panel, hosting the orders view, human entries, the Research icon |
| `Editor/HumanUiSelfCheck.cs` (new) | this stage's self-check |

---

### Task 1: `Check` and the command layer pre-validation

**Files:** Modify `Assets/Flatspace/Player/Human/HumanCommandProcessor.cs`; create `Assets/Editor/HumanUiSelfCheck.cs` (the suite shell with `RunChecks`, `Check`, a `Fixture` copied in shape from `HumanModeSelfCheck`).

**Interfaces:**
- Produces: `public string HumanCommandProcessor.Check(HumanCommand command)`: null when a fresh processor would apply the command, else its refusal reason. Supports `queueProduction`, `queueResearch`, `colonize`, `shipResource`; any other type returns `"CheckNotSupported"`. It never changes game state (no queue, no order list, no claims survive).
- Produces: `private string ValidateQueueResearch(HumanCommand c, out CatalogItem item)` used by both `HandleQueueResearch` and `Check`.

- [ ] **Step 1: Failing checks** in `HumanUiSelfCheck.RunCheckChecks` (fixture: a 4-planet chain, human on P0 with 2 inhabitants and a docked colony ship, food 50, one researched and one unresearched production item, research items RA and RB with RB requiring RA): `Check(colonize P0->P2)==null`; `Check(colonize P0->P0)=="SameDestination"`; `Check(shipResource P0->P2 food 20)==null`, `Check(... food 500)=="NotEnoughStock"`; `Check(queueProduction P0 locked item)=="NotUnlocked"`; `Check(queueResearch RB)=="PrerequisiteMissing"` and `Check(queueResearch RA)==null` **and afterwards `human.ResearchQueue.Count==0`** (Check queued nothing); `Check(move ...)=="CheckNotSupported"`. Calling `Check` ten times in a row for the same colonize still returns null (no claims leak).
- [ ] **Step 2: Run** `FlatSpace -> Human -> Run Human UI Self-Check`; expected: compile error (no `Check`).
- [ ] **Step 3: Implement** `Check` by creating a fresh `HumanCommandProcessor(_map, _human, _turn)` with the same `PauseSettings` and running the handler on a scratch order list; for `queueResearch` call `ValidateQueueResearch` only. Extract that validation from `HandleQueueResearch` without changing its refusals.
- [ ] **Step 4: Run** the new suite and `FlatSpace -> Human -> Run Human Mode Self-Check`; expected: both pass.
- [ ] **Step 5: Commit** (when the user asks): the two source files and `.meta`.

### Task 2: Submitting commands from the UI

**Files:** Modify `GameAI/GameAI.cs`, `Objects/Board/GameBoard.cs`, `Diagnostics/AITuningLogger.cs`.

**Interfaces:**
- Produces: `public void GameAI.ApplyOrdersNow(List<GameAIOrder> orders)` = the existing `ProcessNewOrders` (delayed orders queue, immediate ones execute), public.
- Produces: `public List<HumanCommandResult> Gameboard.SubmitHumanCommands(IEnumerable<HumanCommand> commands, string screen)`: returns an empty list when no human plays; otherwise builds a `HumanCommandProcessor` (with `PauseSettings = HumanPause`), applies the batch, calls `GameAI.ApplyOrdersNow` with the orders, refreshes the planet panel and board (`PlanetaryUIUpdate`, `BoardUIUpdate`), logs each result as `HumanCommand`/`HumanCommandRefused` plus one `HumanUiOrder`, and returns the results.
- Produces: `AITuningLogger.LogHumanUiOrder(int turn, int player, string screen, int commands, int refused)`.

Orders executed between turns are equivalent to the AI's: a `Delayed` order is queued and its delay is first decremented by the next turn's `ProcessCurrentOrders`, exactly as for orders the AI creates during a turn.

- [ ] **Step 1: Write the Play check first** (nothing here is self-checkable: it needs `Gameboard.Instance`): in Task 9 acceptance list "queue a production item from the UI; the queue shows it at once".
- [ ] **Step 2: Implement** the three items above.
- [ ] **Step 3: Verify** by compiling (the user focuses the Editor) and running `Run All AI Self-Checks`; expected: all suites pass, no `error CS` in `Editor.log`.
- [ ] **Step 4: Commit** when asked.

### Task 3: The notification list

**Files:** Create `Player/Human/HumanNotificationList.cs`, `UI/HumanUI/HumanNotificationsView.cs`; modify `GameBoard.cs` (`CollectHumanEvents`), `NotificationListController.cs`, `MainScreenUIController.cs`, `PlanetUIObject.cs` only if the click path needs it.

**Interfaces:**
- Produces: `HumanNotificationEntry { HumanEventKind Kind; string Planet; int Player; bool Rival; int FirstTurn; int Turns; string Text; }`.
- Produces: `HumanNotificationList(int cap = 60)`, `IReadOnlyList<HumanNotificationEntry> Entries` (newest first), `List<HumanEvent> Update(int turn, IEnumerable<HumanEvent> events, Func<HumanEventKind, string, bool> stillTrue)` which returns the **fresh** events (an episode starting, or a one-shot kind), `bool Dismiss(HumanNotificationEntry e)`, `static bool IsStateKind(HumanEventKind k)`, `static string TextFor(HumanEvent e, int turns)` (for example `"Food shortage: Farm 5 (12 turns)"`).
- Produces: `static bool HumanOngoing.IsStillTrue(GameAIMap map, int humanId, IEnumerable<string> blockaded, IEnumerable<HumanEvent> thisTurn, HumanEventKind kind, string planet)`: a shortage is still true when `thisTurn` holds it; `PlanetBlockaded` when `blockaded` holds the planet; `ConversionBegan` while the planet's `ConversionBy != NoOwner`; `ProductionQueueEmpty` while the human's planet has no current production and an empty queue.
- Produces: `Gameboard.HumanNotifications` (the list) and `Gameboard.GoToNotification(HumanNotificationEntry e)`: dismisses it, and when it has a planet calls `ViewPlanet(name)` then `ShowPlanetDetail(name)`.
- Consumes: `HumanEvent`, `HumanEventKind` (project 1), `HumanPauseSettings.ShouldPause`.

- [ ] **Step 1: Failing checks** (`RunNotificationChecks`): a shortage event on P0 for 5 consecutive turns with `stillTrue` true gives one entry with `Turns==5` and `Update` returns it as fresh only on turn 1; a one-shot `FleetArrived` returns fresh every time it is passed and stays an entry until dismissed; when `stillTrue` turns false the state entry disappears; `Dismiss` removes the entry and a shortage that continues is **not** re-added, but after one turn with `stillTrue` false and a new shortage it is added and fresh again; the cap of 60 drops the oldest; `TextFor` of a `ShortageFood` event on `P0` after 12 turns is exactly `"Food shortage: P0 (12 turns)"`; a `Rival` event is prefixed `"Rival: "`; two events of different kinds on one planet make two entries.
- [ ] **Step 2: Run** the suite; expected: compile error / failures.
- [ ] **Step 3: Implement** the list and `HumanOngoing`. In `Gameboard.CollectHumanEvents` replace `HumanPause.ShouldPause(LastHumanEvents, ...)` with `HumanPause.ShouldPause(HumanNotifications.Update(...), ...)` so shortages and other state kinds pause only when they start. Reset the list in `InitGame`.
- [ ] **Step 4: Implement the view:** while a human plays, `NotificationListController` shows `HumanNotifications.Entries` (text from `TextFor`) instead of the per-turn `PlayerNotification`s, refreshed after each turn; selecting an entry calls `Gameboard.GoToNotification`. A watch-only match keeps the old behaviour untouched.
- [ ] **Step 5: Run** the suite, `Run Human Mode Self-Check` (update its pause expectations if a test relied on a repeated shortage pausing), and `Run All AI Self-Checks`; expected: pass. **Play check:** a shortage pauses the timer once; clicking its entry centres the board and opens the planet panel and removes the entry.
- [ ] **Step 6: Commit** when asked.

### Task 4: Ship resources

**Files:** Create `Player/Human/HumanShipmentList.cs`, `UI/HumanUI/ShipResourcesDialog.cs`; modify `GameBoard.cs`.

**Interfaces:**
- Produces: `struct ShipmentSource { string Planet; float Surplus; float Stock; int Hops; }`.
- Produces: `static List<ShipmentSource> HumanShipmentList.Build(GameAIMap map, int humanId, bool food, string destination, IEnumerable<Planet.UpdateResult> lastResults, Func<Planet, float> populationFraction)`: one entry per human-populated planet other than the destination with a `FoodSurplus` (or `GrotsitsSurplus`) result for the human, surplus = `Data x populationFraction(planet)` > 0, `2 <= NumNodes <= maxPathNodesForResourceDistribution` to the destination, `Stock` = the planet's food or grotsits, `Hops = NumNodes - 1`; sorted by hops, then name.
- Produces: `Gameboard.GetShipmentSources(bool food, string destination)` calling `Build` with `GameAI.LastTurnResults` and `planet.GetPopulationFraction(HumanPlayerId)`.
- Produces: `ShipResourcesDialog.Show(VisualElement host, string destination)`.

- [ ] **Step 1: Failing checks** (`RunShipmentListChecks`, 4-planet chain, human on P0 P1 P3, rival on P2, results with surplus 34 at P0 and 11 at P1 and 5 at P2 (rival) and a grotsits surplus 9 at P3): destination P2 food gives P1 (hops 1, surplus 11) before P0 (hops 2, surplus 34) and not P2 itself nor the rival's; populationFraction 0.5 halves the surplus; a surplus of 0.4 scaled to 0 is dropped; `maxPathNodesForResourceDistribution = 2` drops P0 (3 nodes); `food:false` returns only P3; a planet with no result is absent.
- [ ] **Step 2: Run;** expected: failures.
- [ ] **Step 3: Implement** `Build` and `GetShipmentSources`.
- [ ] **Step 4: Implement the dialog** (code-built overlay on the planet panel's host): resource buttons Food/Grotsits (default Food); one row per source: tick box, planet, `surplus`, `stock`, `N hops`, an integer field defaulting to `floor(surplus)`; an amount above surplus shows an amber warning and above stock turns the row red and disables Commit; a total line with the destination's shortage; **Commit** builds one `shipResource` command per ticked row with amount > 0, calls `Gameboard.SubmitHumanCommands(commands, "ShipResources")`, shows the refusal reason for any refused row in the status line and closes only when all were applied; **Cancel** closes.
- [ ] **Step 5: Run** suites; **Play check:** open the dialog on a short planet, tick two sources, commit; both stocks drop at once and two shipments appear as order lines.
- [ ] **Step 6: Commit** when asked.

### Task 5: Production queue and Add item

**Files:** Create `Player/Human/HumanBuildList.cs`, `UI/HumanUI/AddItemDialog.cs`; the queue section is built in Task 6's view.

**Interfaces:**
- Produces: `struct BuildOption { CatalogItem Item; string Group; float Cost; int? Turns; }` with `Group` one of `"Improvements"`, `"Ships"`, `"Defenses"`.
- Produces: `static List<BuildOption> HumanBuildList.Options(Planet planet, PlayerAI human)`: every production catalog item with `Planet.IsProductionItemValid(item)`; `Cost` = `WarshipCosts.ProductionCost(...)` for ship subtypes and `item.cost` otherwise (the same arguments `GameAI.ExecuteOrder`'s `OrderTypeIndustrySetProduction` case uses); `Turns` = `ceil(Cost / planet.IndustryProduced)`, null when `IndustryProduced <= 0`; sorted by group, then tier, then name.
- Produces: `static List<int?> HumanBuildList.QueueEstimates(Planet planet)`: cumulative turns for the item in production (remaining cost) followed by each queued item; null for every entry once the industry rate is 0.

- [ ] **Step 1: Failing checks** (`RunBuildListChecks`): an unresearched item, an already-built improvement and a superseded tier are absent; a researched tier-2 food improvement is present in `"Improvements"` with `Turns == ceil(cost / industry)`; industry 0 gives `Turns == null`; a bunker over the cap is absent; `QueueEstimates` for a planet building a 10-cost item at progress 4 with industry 3 and a queued 9-cost item gives `{2, 5}`; a planet with an empty queue and nothing in production gives an empty list.
- [ ] **Step 2: Run;** expected: failures.
- [ ] **Step 3: Implement** both functions (reuse `WarshipCosts.ProductionCost`; do not copy its formula).
- [ ] **Step 4: Implement `AddItemDialog.Show(host, planetName)`:** grouped list, each row `name, cost, turns`; a click sends one `queueProduction` via `SubmitHumanCommands(..., "Production")` and keeps the dialog open so several items can be added; refusals show in its status line.
- [ ] **Step 5: Run** suites; **Play check** is part of Task 6.
- [ ] **Step 6: Commit** when asked.

### Task 6: The planet UI as the place for orders

**Files:** Modify `UI/MainGameScreenUI/PlanetDetailUI.uxml`, `PlanetDetailUIController.cs`, `GameBoard.cs` (the click-outside rule); create `Player/Human/HumanPlanetView.cs`, `UI/HumanUI/HumanPlanetOrdersView.cs`, `UI/HumanUI/HumanStatusLine.cs`.

**Interfaces:**
- Produces: `static (float food, float grotsits) HumanPlanetView.NetPerTurn(Planet p)` = `(ProjectedFood - Food, ProjectedGrotsits - Grotsits)`.
- Produces: `static bool HumanPlanetView.CanOrder(Planet p, int humanId)` = the human populates `p` (owner or any inhabitant).
- Produces: `HumanPlanetOrdersView.Bind(Planet planet)` (rebuilds the sections), `Refresh()`; `PlanetDetailUIController.ContainsScreenPoint` also returns true for any open human dialog's bounds (it keeps a list of open overlay elements, `RegisterOverlay(VisualElement)`).
- Layout (the owner chose A): the panel becomes `max-height: 70%` with a `ScrollView` named `OrdersScroll` below the existing rows; an **action strip** row (always visible, above the scroll) holds **Ship resources**, **Colonize**, **Fleet** (opens `Gameboard.ShowFleetUI` for the human's group, existing behaviour) and the **Development** toggle (`setDevelopment`; label `Manual`/`Automatic`); in the scroll: stock and net rate lines (a negative food rate in red), **Production** (item in progress with progress, the queue as rows with Up, Down, Remove buttons sending `moveProduction`/`removeProduction`, and **+ Add item**), **Improvements built** (collapsed foldout listing `CompletedImprovements`), **Ships docked** (collapsed foldout, `HumanFleetView.Docked` grouped by kind, damaged flagged).
- When `CanOrder` is false (a rival's or an unowned planet, or no human) the strip and the order controls are hidden or disabled and only the existing read-only rows show.

- [ ] **Step 1: Failing checks** (`RunPlanetViewChecks`): `NetPerTurn` with `Food=3, ProjectedFood=-3` is `-6`; `CanOrder` true for the human's planet, false for a rival's and for an unpopulated planet.
- [ ] **Step 2: Run;** expected: failures.
- [ ] **Step 3: Implement** `HumanPlanetView`.
- [ ] **Step 4: Edit the UXML and controller** as described; keep every element name the controller already queries; the old fixed `height: 240px` is replaced by `max-height`.
- [ ] **Step 5: Implement `HumanPlanetOrdersView`** and wire it from `PlanetDetailUIController.SetPlanet`/`UpdatePlanetDetail` only while a human plays. Update the click-outside rule so a click inside any registered dialog counts as inside the panel and the panel stays open while a dialog is open.
- [ ] **Step 6: Run** suites; **Play check:** open a planet of yours: strip, rates and sections show; the Improvements foldout starts closed; Add item → click an item → it appears in the queue at once; Up/Down/Remove reorder and remove; the Development toggle flips and the planet's queue-empty entry stops (manual) or the AI fills it (automatic) next turn; open a rival's planet: no order controls.
- [ ] **Step 7: Commit** when asked.

### Task 7: Colonize

**Files:** modify `Player/Human/HumanPlanetView.cs`; create `UI/HumanUI/ColonizeDialog.cs`.

**Interfaces:**
- Produces: `struct ColonizeOption { Planet Target; int Turns; }`.
- Produces: `static List<ColonizeOption> HumanPlanetView.ColonizeOptions(GameAIMap map, PlayerAI human, Planet origin, int turn)`: every known planet within `maxPathNodesForColonization` of `origin` for which `new HumanCommandProcessor(map, human, turn).Check(colonize origin->planet) == null`; `Turns = max(1, ceil(pathCost / defaultTravelSpeed))`; sorted by turns then name. Empty when the origin holds no colony ship or fewer than 2 human inhabitants.
- Produces: `static bool HumanPlanetView.CanColonizeFrom(Planet origin, int humanId)` (a docked human colony ship and at least 2 human inhabitants).

- [ ] **Step 1: Failing checks** (`RunColonizeOptionsChecks`, chain of 5, human P0 with 3 inhabitants and a colony ship, `maxPathNodesForColonization = 3`): options are P1 and P2 (P3 and P4 out of range), P1 first; a rival-held full planet and an unknown planet are absent; with one inhabitant, or no colony ship, the list is empty and `CanColonizeFrom` false; `Turns` for a path cost of 250 at speed 100 is 3.
- [ ] **Step 2: Run;** expected: failures.
- [ ] **Step 3: Implement** both functions.
- [ ] **Step 4: Implement `ColonizeDialog.Show(host, originName)`:** one row per option (planet, type, `N turns`), a click selects, **Commit** sends one `colonize` via `SubmitHumanCommands(..., "Colonize")` and closes on success, shows the refusal otherwise. The strip button is disabled when `CanColonizeFrom` is false.
- [ ] **Step 5: Run** suites; **Play check:** with a colony ship docked, pick a target; the colony ship is consumed, a colonist order line appears, and the target gets a flag for the incoming colonist.
- [ ] **Step 6: Commit** when asked.

### Task 8: The research tree

**Files:** Create `Player/Human/HumanResearchTree.cs`, `UI/HumanUI/ResearchScreen.cs`; modify `MainScreenUIController.cs` (a **Research** button in the HUD, shown only while a human plays).

**Interfaces:**
- Produces: `enum ResearchNodeState { Researched, Available, Locked, Queued, Current }`; `class ResearchNode { CatalogItem Item; ResearchNode Parent; List<ResearchNode> Children; ResearchNodeState State; int QueuePosition; int Depth; }`.
- Produces: `static List<ResearchNode> HumanResearchTree.Build(IEnumerable<CatalogItem> catalog, IEnumerable<string> queue, CatalogItem current)`: the roots (items with no `requiredTech`, or whose `requiredTech` is not in the catalog), children ordered by name. State: `Current` for the item in progress; `Researched`; `Queued` with 1-based `QueuePosition`; `Available` when its prerequisite is researched or it has none; otherwise `Locked`. A cyclic or self-referencing `requiredTech` is cut (the item becomes a root marked `Locked`); the build always terminates.
- Produces: `static List<string> HumanResearchTree.ChainFor(IEnumerable<CatalogItem> catalog, IEnumerable<string> queue, CatalogItem current, string itemName)`: the item names to queue to reach `itemName`, prerequisites first and `itemName` last, skipping any already researched, queued or current; empty for an unknown item or a researched one.
- Produces: `static List<int?> HumanResearchTree.Estimates(CatalogItem current, float researchTotal, IList<CatalogItem> queue, float perTurn)`: cumulative turns for the current item (`(cost - researchTotal) / perTurn`, rounded up) then each queued item; all null when `perTurn <= 0`.
- Produces: `static float HumanEconomy.ResearchPerTurn(GameAIMap map, int humanId)` = sum of `ResearchProduced` over planets the human populates (in `HumanPlanetView.cs` or its own small class).

- [ ] **Step 1: Failing checks** (`RunResearchTreeChecks`; catalog A, B requires A, C requires B, D none, E requires a missing item "Z", F requires itself, G requires H and H requires G): roots are A, D, E, F, G, H; B is A's child; states: A researched → B Available, C Locked; queue [B] → B Queued position 1; current D → Current; `ChainFor(C)` with nothing researched is `{A, B, C}`; with A researched `{B, C}`; with B queued `{C}`; `ChainFor("Nope")` and `ChainFor(A researched)` are empty; the cyclic F, G, H build without hanging and are Locked; `Estimates` for current cost 30 at total 10 and rate 5 and a queued cost-20 item gives `{4, 8}`; rate 0 gives `{null, null}`.
- [ ] **Step 2: Run;** expected: failures.
- [ ] **Step 3: Implement** the tree, chain and estimates.
- [ ] **Step 4: Implement `ResearchScreen`** (a full-height overlay): an indented tree (children under their parent, depth-indented) with ✓ before researched items, greyed text for `Locked`, a queue position badge for `Queued`, the current research highlighted with turns; the queue panel on the right with each queued item and its cumulative turns; click rules: `Available` → `queueResearch`; `Locked` → confirmation line "Queue N items?" (N = `ChainFor` length) then one `queueResearch` per chain item in order, in a single `SubmitHumanCommands` batch; `Queued` → `removeResearch` at its index. Close button.
- [ ] **Step 5: Run** suites; **Play check:** open from the HUD button; queue an available item; click a deep greyed item and accept: the chain is queued in order; click a queued item to remove it; start the timer: research proceeds and completing an item checks it off.
- [ ] **Step 6: Commit** when asked.

### Task 9: Logging, docs and acceptance

**Files:** `Diagnostics/AITuningLogger.cs` (Task 2 added the method), `.claude/skills/tuning-log/SKILL.md`, `CLAUDE.md`, `FUTURE_FEATURES.md`, `Assets/Editor/AllAISelfChecks.cs`.

- [ ] **Step 1:** Register `HumanUiSelfCheck.RunChecks` in `AllAISelfChecks`.
- [ ] **Step 2:** Document `HumanUiOrder` in the `tuning-log` skill (fields, the screens, the invariant that every `HumanUiOrder` is followed by that many `HumanCommand`/`HumanCommandRefused` lines of the same turn) and the stage 1 screens in the "Human player mode" section of `CLAUDE.md`; mark stage 1 done in `FUTURE_FEATURES.md` only when the user says so.
- [ ] **Step 3: Run** `Run Human UI Self-Check`, `Run Human Mode Self-Check`, `Run All AI Self-Checks`; expected: all pass.
- [ ] **Step 4: Play-mode acceptance** (the user runs it, I read `AITuningLogs`): start a human match on `test2.json` as the `Prime 0` player without a commands file. (a) Run the timer until a shortage pauses it; click the entry; the board centres and the panel opens. (b) Ship food from two planets with the dialog. (c) Queue three items and reorder them. (d) Switch a planet to Automatic and back. (e) Colonize with a colony ship. (f) Open the research tree, queue a chain, remove one item. (g) Click a rival planet: no order controls. (h) Start a watch-only match: no new UI appears. Fix anything that fails with a test first where a pure class is involved.
- [ ] **Step 5: Commit** when the user asks; stage by explicit path with every `.meta`.

---

## Self-review notes

- **Spec coverage:** notification list and click-through (Task 3), planet UI layout A with the strip, rates, production, improvements and ships sections (Task 6), Add item (Task 5), Ship resources dialog with AI surplus and raw stock (Task 4), Colonize without the rider (Task 7), research tree and queue (Task 8), `Check` (Task 1), the change that shortage pauses fire on start (Task 3), no saved list (Task 3, by omission). Not in this stage by design: fleet orders, summaries, top bar, menus, diplomacy, planet search (stages 2 and 3), the pause-settings UI (JSON `setPause` until stage 3).
- **Open assumption:** UI commands are applied immediately between turns through `ApplyOrdersNow`; the JSON path still applies after combat. If a later stage wants one path, make the JSON reader submit through the same entry point.
