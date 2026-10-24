# Human Player UI, Stage 2 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** The human splits a planet's ships into fleets by dragging in the fleet panel, drags a fleet icon to a planet to send it, hovers icons and order lines for a popup, opens a fleet summary, and no longer needs the JSON command file.

**Architecture:** A fleet is a label on ships (`Ship.FleetId`), grouped per planet and owner. Two new instant commands (`setFleet`, `mergeFleets`) reorganise it; a fleet drag is the existing `move` command with the dock indices of that fleet's ships, and `HumanCommandProcessor.Check` is extended to `move` so a drag can be validated before the drop. Popup text, hover picking, line visibility and the summary list are pure classes in `FlatSpace.Human` (self-checked); the uGUI icons, the UI Toolkit panel, popup and summary screen are thin views over them. Everything goes through `Gameboard.SubmitHumanCommands`.

**Tech Stack:** Unity 6000.4.1f1, C#, uGUI (board icons), UI Toolkit (HUD, code-built), Editor self-checks.

**Spec:** `docs/superpowers/specs/2026-10-23-human-player-ui-stage2-design.md` (including "Calls made after the builder check"). Builds on stage 1 (`2026-10-23-human-player-ui-design.md`, plan `2026-10-23-human-player-ui-stage1.md`). Read `CLAUDE.md` ("Human player mode" and "The human UI"), `Assets/Flatspace/Player/Human/HumanFleetView.cs`, `Assets/Flatspace/UI/PlanetUIObject.cs`, `Assets/Flatspace/UI/MainGameScreenUI/FleetUIController.cs`, `Assets/Flatspace/Objects/Board/GameBoard.cs` (`Update`, `_orderLines`, `SubmitHumanCommands`, `HumanSeesPlanet`).

## Global Constraints

- Zero-human matches and every AI player behave exactly as before: grouping by fleet id applies only to the human's ships, the AI never reads `FleetId`, and every new view exists only while `Gameboard.HumanPlayerId >= 0`.
- No new `OrderType`. Any new serialized value is defined so that 0/absent means "as before" (`ShipSave.fleetId` default 0). New `HumanCommand` types are handled in `Dispatch`; refusals come from a closed set and nothing throws.
- A fleet drag is all or nothing; a refused drop leaves the fleet where it is and shows the reason.
- Never give a Delayed order `TimingDelay <= 0` (`EmitShipOrders` clamps; the drag reuses it through `move`).
- Pure classes live in `Assets/Flatspace/Player/Human/` (namespace `FlatSpace.Human`), never touch `Gameboard.Instance`, and are covered by `HumanModeSelfCheck.cs` / `HumanUiSelfCheck.cs` (both already in `Run All AI Self-Checks`). Methods a self-check calls are `public`. A self-check docks ships with `DockShipFromSave`, never `DockNewShip`.
- Views live in `Assets/Flatspace/UI/HumanUI/` (UI Toolkit, code-built, `HumanUiKit` dark theme).
- Spec values: hover pixel tolerance 10; drag threshold 6 pixels; a new fleet id is the highest id at that planet for that owner plus one; arrivals by order get a fresh id; newly built ships join the owner's lowest-numbered fleet at the planet (0 if none); refusal reasons added: `UnknownFleet`, `SameFleet`.
- New scripts are committed with their `.meta`; stage by explicit path; commit and push only when the user asks. After any edit read `Editor.log` for `error CS` before asking the user to run a check. Unity cannot run from the command line: "run" means the user runs the named `[MenuItem]` or a Play run. Checks no self-check can cover are marked **Play check** and collected in Task 8.
- `PlayerAI.cs`, `GameAIConstants.cs` and some self-check files are CRLF; patch with scripts that preserve line endings (write the script to a file and run with `python3 -I`; no large heredocs).

## Review Focus

1. A click on a fleet icon that wobbles a few pixels must open the panel, not start a drag; a drag must never pan the board (Task 5).
2. A fleet drop on its own planet, on an unknown or out-of-range planet, or after its ships changed (a ship was sold/moved by another command): refused with a reason, nothing partially sent (Tasks 2, 5).
3. Hover over many overlapping lines, over a line that finishes this turn, over a line whose order was removed between frames: one popup, no exception, never a stale entry (Tasks 3, 4).
4. A rival's line with one end in fog, and a rival's line when the human is dead or the match has no human: no popup (Tasks 3, 4).
5. Splitting the last ship out of a fleet, merging a fleet into itself, dragging a ship to the box it is already in: no empty fleet left behind, a refusal or a no-op, never an exception (Tasks 2, 6).
6. A save and load of a match with three fleets at a planet, and a pre-stage-2 save: fleets survive, the old save shows one fleet (Task 1).

## Tuning log additions (rule 4)

- No new line codes. The new commands appear through the existing `HumanCommand` / `HumanCommandRefused` lines (`SubmitHumanCommands` already logs every applied or refused result with `result.type` and `result.detail`) and `HumanUiOrder|<screen>|<commands>|<refused>`, which gets two new screen labels, `Fleets` (panel split/merge) and `FleetDrag` (icon drag). They answer how often fleets are reorganised and how often a drop is refused. One-off per click, so no tracker. Task 8 adds the two labels to the `tuning-log` skill and to the "AI Tuning Log" and "Human player mode" sections of `CLAUDE.md`.

---

## File structure

| File | Responsibility |
|---|---|
| `Assets/Flatspace/Objects/Ships/Ship.cs` (modify) | `FleetId` field |
| `Assets/Flatspace/Objects/Planets/Planet.cs` (modify) | fleet id allocation helpers; docking sets the id |
| `Assets/Flatspace/GameAI/GameAI.cs` (modify) | `ApplyShipArrival` gives arrivals a fresh fleet id |
| `Assets/Flatspace/SaveSystem/SaveLoadSystem.cs`, `GameAIMap.cs` (modify) | `ShipSave.fleetId` save and restore |
| `Assets/Flatspace/UI/FleetSummary.cs` (modify) | per-fleet groups for the human |
| `Assets/Flatspace/Player/Human/HumanFleetView.cs` (modify) | fleet queries: fleets at a planet, a fleet's dock indices |
| `Assets/Flatspace/Player/Human/HumanCommandProcessor.cs` (modify) | `setFleet`, `mergeFleets`, `Check`/`CheckBatch` for `move` |
| `Assets/Flatspace/Player/Human/HumanHoverText.cs` (create) | popup text, line visibility, hover pick, refusal sentences |
| `Assets/Flatspace/Player/Human/HumanFleetList.cs` (create) | the fleet summary rows |
| `Assets/Flatspace/UI/HumanUI/HumanHoverPopup.cs` (create) | one popup element on the HUD |
| `Assets/Flatspace/UI/HumanUI/FleetSummaryScreen.cs` (create) | the summary screen |
| `Assets/Flatspace/UI/PlanetUIObject.cs` (modify) | one icon per fleet; hover; drag |
| `Assets/Flatspace/UI/MainGameScreenUI/FleetUIController.cs` (modify) | fleet boxes and ship-row dragging |
| `Assets/Flatspace/Objects/Board/GameBoard.cs` (modify) | line hover, drag entry points, remove the JSON read |
| `Assets/Flatspace/UI/MainGameScreenUI/MainScreenUIController.cs` (modify) | Fleets button |
| `Assets/Editor/HumanModeSelfCheck.cs`, `HumanUiSelfCheck.cs`, `FleetSummarySelfCheck.cs` (modify) | checks |

---

### Task 1: Fleet ids on ships, saved, and grouped

**Files:** modify `Ship.cs`, `Planet.cs` (docking at ~1127-1148), `GameAI.cs:664`, `SaveLoadSystem.cs` (`ShipSave` ~49, writer ~285), `GameAIMap.cs:446`, `FleetSummary.cs`, `FleetSummarySelfCheck.cs`, `HumanModeSelfCheck.cs`.

**Interfaces:**
- Produces: `Ship.FleetId` (`int`, default 0); `Planet.NextFleetId(int owner) -> int` (highest `FleetId` of that owner's non-bunker ships here + 1, 0 when none); `Planet.LowestFleetId(int owner) -> int` (0 when none); `Planet.DockShipFromSave(kind, owner, snapshot, float damage = 0f, int fleetId = 0)`; `ShipSave.fleetId` (int, 0 = "as before"); `FleetSummary.ForPlanet(Planet planet, int humanId = -1) -> List<Group>` where `Group` gains `int FleetId` and, for `Owner == humanId`, groups are per (owner, `FleetId`) ordered by `FleetId`, otherwise one per owner as today.
- `FleetSummary.Group.FleetId` is 0 for every non-human group.

- [ ] **Step 1: Write failing checks** in `FleetSummarySelfCheck` (`RunFleetIdGroupingCheck`): a planet with human (owner 0) ships at fleet ids 0,0,1 and a rival (owner 1) ships at ids 0,5 gives `ForPlanet(planet, 0)` = two groups for owner 0 (counts 2 and 1, ids 0 and 1) and one group for owner 1 (count 2); `ForPlanet(planet)` (no human) gives one group per owner; bunkers never appear. In `HumanModeSelfCheck` (`RunFleetIdSaveCheck`): `NextFleetId` is 0/1/3 for empty / ids {0} / ids {0,2}; `LowestFleetId` returns the smallest present id; a `ShipSave` round trip through `JsonUtility` keeps `fleetId`; an old JSON ship entry with no `fleetId` loads as 0; restoring through `DockShipFromSave(..., fleetId)` reproduces the ids; `GameAI.ApplyShipArrival` on a planet already holding owner 0 ships at id 0 docks the arrivals at id 1, all arrivals of one order sharing it.
- [ ] **Step 2: Ask the user to run** `FlatSpace -> Human -> Run Human Mode Self-Check` and the Fleet Summary check; expect the new checks FAIL (compile errors count).
- [ ] **Step 3: Implement** the interfaces above. `DockShipRebuiltSnapshot` sets `ship.FleetId = LowestFleetId(owner)` (built ships join the lowest fleet); `ApplyShipArrival` computes `var fleetId = target.NextFleetId(order.PlayerId)` once before the loop and passes it to every `DockShipFromSave` and sets it on rebuilt ones; the writer and `GameAIMap` restore pass `fleetId` through. Callers of `FleetSummary.ForPlanet` that serve the human pass `Gameboard.Instance.HumanPlayerId` (`PlanetUIObject.UIUpdate`, `PlanetDetailUIController`).
- [ ] **Step 4: Read `Editor.log`** for `error CS`, then ask the user to rerun both checks and `Run All AI Self-Checks`; expect PASS with no regression.
- [ ] **Step 5: Commit** (when asked): `feat: fleet ids on ships (saved, grouped for the human)`.

### Task 2: Fleet commands and `Check` for move

**Files:** modify `HumanFleetView.cs`, `HumanCommandProcessor.cs`, `HumanModeSelfCheck.cs`.

**Interfaces:**
- Consumes: Task 1 (`Ship.FleetId`, `Planet.NextFleetId`).
- Produces: `HumanFleetInfo { int FleetId; List<HumanShipInfo> Ships; }` and `HumanFleetView.Fleets(Planet planet, int owner) -> List<HumanFleetInfo>` (non-bunker ships, ordered by `FleetId`, each ship carrying its `DockIndex`); `HumanFleetView.ShipIndices(Planet planet, int owner, int fleetId) -> List<int>`; commands `setFleet` (`origin`, `ships` dock indices, `index` = target fleet id, negative = a new fleet) and `mergeFleets` (`origin`, `index` = from fleet id, `toIndex` = into fleet id), both instant, no orders; `Check(move)` and `CheckBatch` accepting `move`; refusal reasons `UnknownFleet`, `SameFleet`.

- [ ] **Step 1: Write failing checks** (`RunFleetCommandsCheck`, using the existing `Fixture`): `setFleet` with ships [0,1] and index -1 on a planet whose five human ships are all in fleet 0 puts them in fleet 1 (`NextFleetId`), leaves the other three in 0, applied, no orders produced; moving all remaining fleet-0 ships to fleet 1 leaves one fleet (no empty fleet exists because fleets are derived); `setFleet` index 7 (does not exist) is refused `UnknownFleet`; a bunker index is refused `UnknownKind`; a rival's ship index `NotYourShips`; index out of range `BadShipIndex`; a ship claimed by a `move` earlier in the same batch is refused `NotEnoughShips`; `mergeFleets` 1 into 0 leaves every ship at 0; `mergeFleets` 1 into 1 is `SameFleet`; from an unknown or empty fleet `UnknownFleet`; a planet the human does not occupy `NotYourPlanet`. `Check` of a valid `move` returns null and leaves the dock list, claims and order list untouched; `Check` of a move to an unknown, out-of-range or same planet returns the same reasons `Apply` gives (`TargetUnknown`, `OutOfRange`, `SameDestination`). A move built from `ShipIndices(planet, 0, 1)` sends exactly that fleet's ships (assert the emitted `ShipTransport` order count and that the other fleet's ships are still docked after execution).
- [ ] **Step 2: Ask the user to run** the Human Mode check; expect FAIL.
- [ ] **Step 3: Implement** `HandleSetFleet` / `HandleMergeFleets` in the Fleets region of `HumanCommandProcessor`, resolving ship indices against `DockAtBatchStart`, reusing `IsHumanPlanet`, `IsMovable`, `IsClaimed`; add the two cases to `Dispatch`, `move`/`setFleet`/`mergeFleets` to `Check` (the scratch-processor path used for `colonize`) and to `CheckBatch`'s supported list. Fleet ids are read live from `Ship.FleetId`; a non-negative target id must be one that exists at the planet for the human.
- [ ] **Step 4: Read `Editor.log`**, ask the user to rerun the check and `Run All AI Self-Checks`; expect PASS.
- [ ] **Step 5: Commit** (when asked): `feat: setFleet and mergeFleets commands, Check for move`.

### Task 3: Pure hover text, visibility, pick, and fleet list

**Files:** create `Assets/Flatspace/Player/Human/HumanHoverText.cs`, `HumanFleetList.cs` (with `.meta`); modify `HumanUiSelfCheck.cs`.

**Interfaces:**
- Consumes: `GameAI.GameAIOrder` (`Type`, `Fleet`, `Data`, `Origin`, `Target`, `TimingDelay`, `PlayerId`), `Ship.ShipKind`.
- Produces (all `public static`, namespace `FlatSpace.Human`):
  - `HumanHoverText.Ships(IEnumerable<Ship.ShipKind> kinds) -> string` ("3 Warship, 1 Siegeship"; kinds in enum order, singular kind names, empty -> "no ships").
  - `HumanHoverText.Order(GameAI.GameAIOrder order) -> string` or null when the order type draws no line: ship transport ("<ships> to <Target>, arrives in N turns"), food/grotsits transport ("<amount> Food to <Target>, arrives in N turns"), population transport ("Colonist to <Target>, ..."), colony food rider ("<amount> Food (colony rider) to <Target>, ..."); N is `TimingDelay` (singular "1 turn").
  - `HumanHoverText.CanSee(GameAI.GameAIOrder order, int humanId, Func<string, bool> planetVisible) -> bool` (own order: true; another player's: both `Origin` and `Target` visible).
  - `HumanHoverText.Pick(IReadOnlyList<(T item, IReadOnlyList<Vector2> screenPoints)> lines, Vector2 pointer, float tolerance) -> List<T>` (generic over `T`; every item with a segment within `tolerance` of the pointer, nearest first).
  - `HumanHoverText.RefusalSentence(string reason) -> string` (a sentence for each reason in the closed set, including `OutOfRange`, `TargetUnknown`, `SameDestination`, `NotYourPlanet`, `NotEnoughShips`, `UnknownFleet`, `SameFleet`; unknown reason -> the reason text itself).
  - `HumanFleetRow { string Where; string Detail; string PlanetToShow; bool InFlight; int TurnsLeft; }` and `HumanFleetList.Build(IEnumerable<Planet> planets, IEnumerable<GameAI.GameAIOrder> orders, int humanId) -> List<HumanFleetRow>`: one row per docked fleet (non-empty `HumanFleetView.Fleets`, `PlanetToShow` = the planet) then one per ship transport order of the human (`PlanetToShow` = `Target`, `TurnsLeft` = `TimingDelay`), docked rows ordered by planet name.

- [ ] **Step 1: Write failing checks** in `HumanUiSelfCheck` (`RunHoverTextCheck`, `RunFleetListCheck`): exact strings for `Ships` (one kind, two kinds, empty), `Order` for each of the four order types with `TimingDelay` 1 and 3, `Order` null for `OrderTypeShipDeparture`; `CanSee` for own, rival with both ends visible, rival with one end hidden, rival with both hidden; `Pick`: a pointer 4 px from one segment and 30 px from another returns only the first; two lines both within tolerance return both, nearest first; a degenerate zero-length segment and an empty point list do not throw; `Build` with two docked fleets at one planet, one at another, and one fleet in flight yields four rows in the documented order with the right `PlanetToShow`; a rival's ships and orders are excluded.
- [ ] **Step 2: Ask the user to run** `Run Human UI Self-Check`; expect FAIL.
- [ ] **Step 3: Implement** the two files. `Pick` uses point-to-segment distance in screen pixels; segments are consecutive points.
- [ ] **Step 4: Read `Editor.log`**, rerun the checks; expect PASS.
- [ ] **Step 5: Commit** (when asked): `feat: pure hover text, line pick, visibility and fleet list builders`.

### Task 4: Hover popup on order lines

**Files:** create `Assets/Flatspace/UI/HumanUI/HumanHoverPopup.cs`; modify `GameBoard.cs`, `LineDrawObject.cs` only if highlighting needs a hook.

**Interfaces:**
- Consumes: Task 3 builders; `Gameboard._orderLines` (`Dictionary<GameAIOrder, LineDrawObject>`, the order-to-line link), `Gameboard.HumanSeesPlanet(string)`.
- Produces: `HumanHoverPopup.Show(VisualElement root, Vector2 screenPosition, IReadOnlyList<string> blocks)` / `Hide()` (a single dark-theme element with one label block per entry, positioned beside the pointer and kept on screen; `pickingMode` Ignore so it never blocks the board); `Gameboard.UpdateLineHover()` called from `Update()` only while a human plays and only when the pointer moved.

- [ ] **Step 1: Write the view and wiring.** In `UpdateLineHover`: build `(order, screen points)` for every entry of `_orderLines` whose line is enabled, whose `HumanHoverText.CanSee(order, HumanPlayerId, HumanSeesPlanet)` is true and whose `HumanHoverText.Order(order)` is non-null (world positions from the `LineRenderer` via `TransformPoint`, then `Camera.WorldToScreenPoint`); call `HumanHoverText.Pick` with tolerance 10; show the popup with one block per picked order and brighten the nearest line (restore on leave); hide it when the pointer is over the planet panel (`ContainsScreenPoint`), over a dialog, or nothing is picked. Skip entries whose `LineDrawObject` is null or destroyed (pooled).
- [ ] **Step 2: Read `Editor.log`** for `error CS`; run `Run All AI Self-Checks` (user) to confirm no regression.
- [ ] **Step 3: Play check (user):** hover your own in-flight fleet line (ship text, destination, turns), a food shipment line, a colonist line, a rival line between two planets you can see (popup shows) and one with an end in fog (no popup); overlapping lines list each; the popup follows the pointer, vanishes over the planet panel, and never blocks a click on a planet.
- [ ] **Step 4: Commit** (when asked): `feat: hover popups on order lines`.

### Task 5: One icon per fleet, hover on icons, drag to send

**Files:** modify `PlanetUIObject.cs`, `GameBoard.cs`; extend `HumanUiKit.cs` with a toast.

**Interfaces:**
- Consumes: `FleetSummary.ForPlanet(planet, humanId)` (Task 1), `HumanFleetView.ShipIndices` and `Check(move)` (Task 2), `HumanHoverText.Ships`/`RefusalSentence` (Task 3), `HumanHoverPopup` (Task 4).
- Produces: `FleetIconView` gains `FleetId`; the icon component implements `IPointerEnterHandler`, `IPointerExitHandler`, `IBeginDragHandler`, `IDragHandler`, `IEndDragHandler`; `Gameboard.TryDragFleet(string origin, int fleetId, string targetPlanet) -> string` (null when applied, else the refusal reason: it builds `move` with `ShipIndices` and calls `SubmitHumanCommands(..., "FleetDrag")`); `Gameboard.CheckFleetDrag(string origin, int fleetId, string targetPlanet) -> string` (the `Check` result); `HumanUiKit.Toast(VisualElement root, string text)` (a label that fades after about 3 seconds).

- [ ] **Step 1: Implement** one icon per group (count plate as now; the human's icons at their fleet ids, rivals as before). Hover: show `HumanHoverText.Ships` of that fleet's ships (rivals' icons included). Drag, human's own icons only: a press that moves under 6 px is a click and opens the fleet panel as today; beyond 6 px it becomes a drag that draws a line from the icon to the pointer (a `LineRenderer` on a small child object, removed on release); the planet under the pointer is found with the same `EventSystem.RaycastAll` + `GetComponentInParent<PlanetUIObject>` test as `IsPointerOverPlanet`, tinted when `CheckFleetDrag` returns null and red when not (tested only when the planet under the pointer changes); release calls `TryDragFleet`; a non-null result shows `HumanUiKit.Toast(HumanHoverText.RefusalSentence(reason))`. Because the icon is the nearest drag handler, the board `ScrollRect` receives no drag events; set `_panelClickFrame` the way a fleet icon click does so the release is not read as an outside click.
- [ ] **Step 2: Read `Editor.log`**; `Run All AI Self-Checks` (user).
- [ ] **Step 3: Play check (user):** split a planet's ships into fleets (Task 6 first, or via an existing mixed group), two icons show with counts; click opens the panel; click with a 3 px wobble still opens it; drag one onto a connected planet and release: the usual ship-transport lines appear and the other icon stays; drop on an out-of-range planet, on the same planet and on empty space: the fleet stays, a message states why for the first two; the board does not scroll during a drag; a rival's icon cannot be dragged but hovers.
- [ ] **Step 4: Commit** (when asked): `feat: per-fleet icons, icon hover, drag to send`.

### Task 6: Fleet panel boxes (split and merge by dragging)

**Files:** modify `FleetUIController.cs` (and `FleetUI.uxml` only if a container is missing); extend `HumanModeSelfCheck` only if a pure helper is added.

**Interfaces:**
- Consumes: `HumanFleetView.Fleets` (Task 2), `setFleet` / `mergeFleets` (Task 2), `Gameboard.SubmitHumanCommands`.
- Produces: for the human's own ships `FleetUIController.RefreshShipList` renders one box per fleet side by side (title "Fleet N (count)", ship rows with the existing `FormatShipRow` text) plus a "+ New fleet" drop box; dragging a ship row onto another box sends `setFleet` (`index` = that box's fleet id; the new box sends -1); dragging a box title onto another box sends `mergeFleets`; each call goes through `SubmitHumanCommands(..., "Fleets")` and the panel refreshes. A rival's ships and non-human matches keep the current read-only list.

- [ ] **Step 1: Implement** with UI Toolkit pointer events (pointer down on a row/title starts a drag after 6 px, a ghost label follows the pointer, pointer up over a box commits; releasing elsewhere cancels). A drop on the box a ship is already in does nothing. Refusals use `HumanUiKit.Toast`. Pointer presses inside the panel must keep `PlanetDetailUIController.LastPointerDownFrame` current so they are not read as outside clicks.
- [ ] **Step 2: Read `Editor.log`**; `Run All AI Self-Checks` (user).
- [ ] **Step 3: Play check (user):** your flow end to end: nine ships in one box; drag ships into "+ New fleet" twice (three fleets); drag one fleet's icon on the board to a planet; drag one box's title onto the other box (merge); drag that fleet to a different planet; the emptied box disappears; Add item and the other dialogs still open without closing the panel.
- [ ] **Step 4: Commit** (when asked): `feat: fleet panel boxes with drag split and merge`.

### Task 7: Fleet summary screen

**Files:** create `Assets/Flatspace/UI/HumanUI/FleetSummaryScreen.cs`; modify `MainScreenUIController.cs`.

**Interfaces:**
- Consumes: `HumanFleetList.Build` (Task 3), `Gameboard.GoToPlanet(string)`.
- Produces: `FleetSummaryScreen.Toggle(VisualElement root)`, `Close()`, `Refresh()` (same shape as `ResearchScreen`); a "Fleets" button beside the Research button, shown only for a human match; each row shows where (or origin to destination), turns left for flights and the ship mix; a row click calls `GoToPlanet(row.PlanetToShow)`. The screen refreshes with the HUD each turn and after any command.

- [ ] **Step 1: Implement** following `ResearchScreen`'s structure (static state reset on scene reload, closed when no human).
- [ ] **Step 2: Read `Editor.log`**; `Run All AI Self-Checks` (user).
- [ ] **Step 3: Play check (user):** rows for docked fleets and one in flight; a row click centres the board and opens that planet; the list updates after a drag and after a turn; hidden in a watch-only match.
- [ ] **Step 4: Commit** (when asked): `feat: fleet summary screen`.

### Task 8: Remove the JSON reader; docs; final checks

**Files:** modify `GameBoard.cs` (`RunHumanCommands`), `GameAI.cs:195`, `HumanCommand.cs` (`HumanCommandFile`), `CLAUDE.md`, `FUTURE_FEATURES.md`, `.claude/skills/tuning-log/SKILL.md`, `HumanModeSelfCheck.cs`.

- [ ] **Step 1:** Delete `Gameboard.RunHumanCommands` and its call in `GameAI.GameAIUpdate` (`gameAIOrders.AddRange(PendingHumanOrders)` and `GameAI.PendingHumanOrders` stay; check what else queues into it before removing anything). Remove `HumanCommandFile` and the `HumanCommands/` mentions only if nothing else uses them; keep `HumanCommandProcessor.ApplyJson`, `Parse` and the result types (the self-checks use them). Any self-check that read the file is repointed at `ApplyJson`.
- [ ] **Step 2:** Update docs: `CLAUDE.md` "Human player mode" (the file channel is gone; commands list gains `setFleet`/`mergeFleets`; fleet ids; the fleet UI and hover popups; `HumanUiOrder` screens `Fleets` and `FleetDrag`), the `tuning-log` skill (the two screen labels), and `FUTURE_FEATURES.md` (stage 2 built, stage 3 next; the HANDOFF note that `player_control.md` is stale stays open).
- [ ] **Step 3: Read `Editor.log`**; user runs `Run Human Mode Self-Check`, `Run Human UI Self-Check`, `Fleet Summary` and `Run All AI Self-Checks`; expect all PASS.
- [ ] **Step 4: Play check (user, the full list):** the Review Focus items 1 to 6 (including save and load of a match with three fleets and an old save); a rival's hover between visible planets; defeat popup and a turn-limit run still work with the file reader gone; a zero-human match shows no new UI.
- [ ] **Step 5: Final review:** one fresh opus subagent over the whole branch diff against the spec; fix Critical and Important findings. Commit (when asked): `feat: human player UI stage 2 (fleets, hover popups, fleet summary); JSON channel removed`.
