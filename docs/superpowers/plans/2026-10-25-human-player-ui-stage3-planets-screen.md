# Planets Screen (Human UI Stage 3) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** One HUD screen (button and P key) that lists every known planet in a searchable, filterable, sortable table, with the last-known owner for planets out of sight.

**Architecture:** A pure `PlanetSightMemory` (owner and turn last seen, human only, saved) and a pure `HumanPlanetTable` (rows, four filters, search, click-to-sort) in `Assets/Flatspace/Player/Human/`; a layout-only `PlanetsScreen` in `Assets/Flatspace/UI/HumanUI/` built like `FleetSummaryScreen`; wiring in `Gameboard`, `MainScreenUIController`, `GameButtonHandler` and `SaveLoadSystem`.

**Tech Stack:** Unity 6 (6000.4.1f1), C#, UI Toolkit built in code, `JsonUtility` saves, Editor self-checks (no test framework).

**Spec:** `docs/superpowers/specs/2026-10-25-human-player-ui-stage3-planets-screen-design.md`. Read `CLAUDE.md` ("Human player mode", both UI subsections) first.

## Global Constraints

- Namespace `FlatSpace.Human` for the pure classes (match `HumanFleetList`); `Game.UI.MainGameScreenUI` for the screen (match `FleetSummaryScreen`).
- Pure classes never touch `Gameboard.Instance`; a self-check never uses it either, and methods it calls are `public` (not `internal`: `Assets/Editor/` is another assembly).
- Columns, in order: Planet, Owner, Type, Pop, Food, Grotsits, Industry, Research, Building, Ships, Alerts. Filters (radio, one active): All known, Colonization targets, Mine, Hostile activity. Default filter Mine.
- Sort: first click on a heading is descending, the second ascending, then alternating; blanks always last; Building sorts by item name (ordinal, case-insensitive); ties by planet name.
- Owner text for a planet out of sight: last-known owner plus `(N turns ago)` (`(1 turn ago)` for 1); in sight: no suffix; never seen live: blank. Rival text is `Player N`.
- New saved fields default so that missing/empty means "as before" (older saves load with no sightings).
- Each new script's `.meta` is staged with it. Stage by explicit path, never `git add -A Assets`. Commit only when the owner asks; each task ends at a checkpoint, not a commit.
- Unity cannot run from the command line: the owner runs the self-check; after every edit read `%LOCALAPPDATA%\Unity\Editor\Editor.log` for `error CS`. Python patch scripts: write to a file, ASCII comments, compute before opening the file for writing.

## Review Focus

- A known planet never seen live: Owner blank, rates/building/ships/alerts blank, no exception (Task 2 test `NeverSeenPlanetIsBlank`).
- A planet that changes hands while out of sight keeps the old owner and its "turns ago" grows (Task 1 test `StaleOwnerKeptWhileUnseen`).
- All-blank or tied columns sort deterministically (by planet name) in both directions (Task 2 test `BlanksAndTiesSortStable`).
- A search that matches nothing leaves an empty table with a "No planets match" line and the filter counts unchanged (Task 3 Play check).
- A save with no `humanSightings`, and a match with no human, load/run with the button hidden and P inert (Task 1 test `EmptyRestore`; Task 3 Play check).

## Tuning log

No new `AITuningLogger` lines: the screen only reads state and sends no commands, so a Play run has nothing to measure. The `tuning-log` skill needs no change. `CLAUDE.md` gets the stage 3 subsection (Task 4).

## File structure

- Create `Assets/Flatspace/Player/Human/PlanetSightMemory.cs`, `HumanPlanetTable.cs`; `Assets/Flatspace/UI/HumanUI/PlanetsScreen.cs` (+ `.meta` each).
- Modify `GameBoard.cs` (own/refresh/reset the memory, P key, click-outside rule), `SaveLoadSystem.cs` + `GameAIMap`/`Gameboard` load path (sightings), `MainScreenUIController.cs` (button, refresh), `GameButtonHandler.cs` (Esc), `Assets/Editor/HumanUiSelfCheck.cs`, `CLAUDE.md`.

---

### Task 1: PlanetSightMemory and its save

**Files:**
- Create: `Assets/Flatspace/Player/Human/PlanetSightMemory.cs`
- Modify: `Assets/Flatspace/SaveSystem/SaveLoadSystem.cs` (the `GameSave` human fields near `hasHuman`/`humanPlayerId`, and the save/load blocks that fill them), `Assets/Flatspace/Objects/Board/GameBoard.cs`
- Test: `Assets/Editor/HumanUiSelfCheck.cs` (add `RunSightMemoryChecks()` and call it in `RunChecks`)

**Interfaces:**
- Produces:
  - `public sealed class PlanetSightMemory`
  - `public struct Sighting { public int Owner; public int Turn; }` (`Owner` is `Planet.Owner`, `Planet.NoOwner` for none)
  - `public void Update(IEnumerable<Planet> planets, Func<string, bool> inSight, int turn)` records owner and turn for every planet in sight
  - `public bool TryGet(string planetName, out Sighting sighting)`
  - `public IEnumerable<(string planet, int owner, int turn)> Entries()` and `public void Restore(IEnumerable<(string planet, int owner, int turn)> entries)` (replaces all)
  - `[Serializable] public class SightingSave { public string planet; public int owner; public int turn; }` in `SaveLoadSystem`, and `GameSave.humanSightings: List<SightingSave>`
  - `Gameboard.PlanetSights` (a `PlanetSightMemory`, never null)

- [ ] **Step 1: Write the failing tests** in `RunSightMemoryChecks()`: `StaleOwnerKeptWhileUnseen` (Update at turn 3 with a planet in sight and owner 2; change the planet's owner to 1; Update at turn 9 with it out of sight; `TryGet` still gives owner 2, turn 3; Update at turn 10 in sight gives owner 1, turn 10), `NeverSeenHasNoEntry` (`TryGet` false), `RestoreRoundTrip` (`Restore(Entries())` into a fresh instance gives identical `TryGet`), `EmptyRestore` (`Restore(null-safe empty)` leaves `TryGet` false).
- [ ] **Step 2: Run** `FlatSpace -> Human -> Run Human UI Self-Check`. Expected: compile failure (`PlanetSightMemory` not defined).
- [ ] **Step 3: Implement `PlanetSightMemory`** with a `Dictionary<string, Sighting>`; `Update` overwrites only for planets where `inSight(name)` is true.
- [ ] **Step 4: Wire into `Gameboard`:** create a fresh `PlanetSights` in `InitGame` (so it resets per match); call `PlanetSights.Update(GameAI.GameAIMap.PlanetList, HumanSeesPlanet, TurnNumber)` once per turn at the end of `SingleUpdate` before the presentation refresh (only while `HumanPlayerId >= 0`), and once after a game starts or loads so turn 0 is seen.
- [ ] **Step 5: Save it:** in `SaveLoadSystem` write `humanSightings` from `Entries()` when a human plays; on load call `Restore` (a null list restores nothing). Write it where `humanPlayerId` is saved (my call: a `GameSave` field, not per `PlayerSave`, since it is human only).
- [ ] **Step 6: Run the self-check.** Expected: ALL PASSED; no `error CS` in `Editor.log`. Checkpoint: stage the new file, its `.meta`, `SaveLoadSystem.cs`, `GameBoard.cs`, `HumanUiSelfCheck.cs` (commit when the owner asks).

### Task 2: HumanPlanetTable (rows, filters, search, sort)

**Files:**
- Create: `Assets/Flatspace/Player/Human/HumanPlanetTable.cs`
- Test: `Assets/Editor/HumanUiSelfCheck.cs` (add `RunPlanetTableChecks()`, call it in `RunChecks`)

**Interfaces:**
- Consumes: `PlanetSightMemory.TryGet`; `PlayerKnowledge.KnownPlanets(humanId)` (via `map.Knowledge`); `HumanPlanetView.ColonizeOptions(GameAIMap, PlayerAI, Planet origin, int turn)` and `CanColonizeFrom`; `HumanBuildList.QueueEstimates(Planet)`; `HumanPlanetView.NetPerTurn`/`Planet.ProjectProduction` for rates; `BlockadeView.IsBlockaded`; `Planet.ConversionBy`
- Produces (all `public static` on `HumanPlanetTable`, namespace `FlatSpace.Human`):
  - `enum PlanetFilter { AllKnown, ColonizationTargets, Mine, Hostile }`
  - `enum PlanetColumn { Planet, Owner, Type, Pop, Food, Grotsits, Industry, Research, Building, Ships, Alerts }`
  - `struct PlanetRow` with `string Planet, OwnerText, Type, BuildItem, Alerts; int? Pop; int MaxPop; float? Food, Grotsits, Industry, Research; int? BuildTurns, Ships; bool InSight, IsMine, IsTarget, IsHostile`
  - `struct SortState { public PlanetColumn Column; public bool Descending; public bool Active; }`
  - `List<PlanetRow> Build(GameAIMap map, PlayerAI human, int humanId, int turn, Func<string,bool> inSight, PlanetSightMemory sights, BlockadeView blockadedMine, Func<Planet,bool> humanBlockades)`
  - `List<PlanetRow> Filter(List<PlanetRow> rows, PlanetFilter filter)`; `int Count(List<PlanetRow> rows, PlanetFilter filter)`
  - `List<PlanetRow> Search(List<PlanetRow> rows, string text)` (case-insensitive contains on `Planet`; empty text returns all)
  - `SortState Click(SortState current, PlanetColumn column)` (inactive or different column: descending; same column: flip)
  - `List<PlanetRow> Sort(List<PlanetRow> rows, SortState state)`
  - `string OwnerText(int owner, bool inSight, bool seenBefore, int turnsAgo)` (the Global Constraints rule)

- [ ] **Step 1: Write the failing tests** in `RunPlanetTableChecks()` using the same `HumanModeSelfCheck.Fixture` helpers the neighbouring checks use (distinct planet positions): `FilterMine`, `FilterAllKnownOnlyKnown`, `FilterTargetsMatchColonizeOptions` (same set as `ColonizeOptions` unioned over the human's planets), `FilterHostileOwnPlanet` (blockaded, converting, enemy warship docked each qualify), `FilterHostileRivalPlanet` (`humanBlockades` true, or `ConversionBy == humanId`, qualifies even out of sight), `DockedOnlyStaysOut` (human ships docked at a rival planet, no blockade/conversion: not Hostile), `NeverSeenPlanetIsBlank` (known, never in sight: `OwnerText` empty, rates/`BuildItem`/`Ships`/`Alerts` blank, `Type` and `MaxPop` still set), `StaleOwnerText` ("Player 2 (7 turns ago)", "Player 2 (1 turn ago)", no suffix when in sight), `SearchNarrowsActiveFilter`, `ClickCycle` (first click descending, second ascending, third descending; a different column restarts at descending), `SortNumericAndText`, `BuildingSortsByItemName`, `BlanksLastBothDirections`, `BlanksAndTiesSortStable` (ties and blanks ordered by planet name in both directions), `BuildingBlankWhenNoEstimateOrEmptyQueue`.
- [ ] **Step 2: Run** the self-check. Expected: compile failure (`HumanPlanetTable` not defined).
- [ ] **Step 3: Implement `Build`:** one row per name in `map.Knowledge.KnownPlanets(humanId)`; `InSight = inSight(name) || owner is human`; rates, `Pop`, `BuildItem`/`BuildTurns` (first entry of `QueueEstimates`, item name from `CurrentProduction` else the first queued item), `Ships` (docked non-bunker count) and own `Alerts` only when `InSight`; `IsTarget` from the union of `ColonizeOptions` over the human's planets that `CanColonizeFrom`; `IsHostile` per the spec's Hostile rule (own: `blockadedMine.IsBlockaded`, `ConversionBy != NoOwner`, a docked warship of a player the human is at war with, using the diplomacy stance query the combat code uses; rival: `humanBlockades(planet)` or `ConversionBy == humanId`, regardless of `InSight`); `Alerts` text from those flags ("Blockaded", "Converting", "Enemy ships", "Blockading", "Converting (you)").
- [ ] **Step 4: Implement `Filter`, `Count`, `Search`, `Click`, `Sort`, `OwnerText`** per the signatures; `Sort` is a stable ordering with blanks (null/empty) after non-blanks and ties by ordinal planet name, ascending or descending only on the non-blank comparison.
- [ ] **Step 5: Run the self-check.** Expected: ALL PASSED; no `error CS`. Checkpoint: stage `HumanPlanetTable.cs`, `.meta`, `HumanUiSelfCheck.cs`.

### Task 3: PlanetsScreen, HUD button, P key, Esc, refresh

**Files:**
- Create: `Assets/Flatspace/UI/HumanUI/PlanetsScreen.cs`
- Modify: `Assets/Flatspace/UI/MainGameScreenUI/MainScreenUIController.cs` (button under Fleets at `top = 112`, hidden unless a human plays; `Close`/`Refresh` beside the `FleetSummaryScreen` lines near 118), `Assets/Flatspace/UI/MainGameScreenUI/GameButtonHandler.cs` (`EscapeButtonPressed`: close the screen first), `Assets/Flatspace/Objects/Board/GameBoard.cs` (P key; `PlanetsScreen.ContainsScreenPoint` in the click-outside rule near line 112; a `BuildPlanetRows()` helper that supplies `inSight = HumanSeesPlanet`, `PlanetSights`, the human's `CurrentBlockadeView`, and `humanBlockades = p => p.Owner != humanId && p.Owner >= 0 && new BlockadeSystem(map, research).Value(p, p.Owner, out var b) > 0f && b == humanId`)

**Interfaces:**
- Consumes: everything from Tasks 1 and 2; `Gameboard.GoToPlanet(string)`; `HumanUiKit` (`Panel`, `Header`, `Btn`, `Row`, `Cell`, `Text`, `Muted`, `Warn`).
- Produces: `public static class PlanetsScreen` with `IsOpen`, `Toggle(VisualElement host)`, `Close()`, `Refresh()`, `ContainsScreenPoint(Vector2)`, `SearchHasFocus` (bool), `ResetState()` (default Mine filter, no sort, empty search; call from `InitGame` and after a load).

- [ ] **Step 1: Implement `PlanetsScreen`** like `FleetSummaryScreen`: a `HumanUiKit.Panel` with a search `TextField` (focused on open, scheduled after layout), four radio buttons with counts (`RadioButtonGroup`), a header row of clickable column labels with a sort arrow, and a `ScrollView` of rows (cells coloured `Warn` for alerts, `Muted` for blanks); a row click calls `Gameboard.Instance.GoToPlanet(row.Planet)` then `Close()`. Empty result shows "No planets match". Sort, filter and search live in static fields reset by `ResetState()`.
- [ ] **Step 2: Wire the button and refresh** in `MainScreenUIController` (display only while a human plays; `Close()` when not), and `Refresh()` after each turn and each submitted command (the same call sites that refresh `FleetSummaryScreen`).
- [ ] **Step 3: P key and Esc:** in `Gameboard.Update`, `Keyboard.current.pKey.wasPressedThisFrame` opens the screen only when it is closed, a human plays, and `!PlanetsScreen.SearchHasFocus`; `EscapeButtonPressed` closes the screen before the fleet UI and planet panel.
- [ ] **Step 4: Read `Editor.log`** for `error CS`; ask the owner to focus the Editor. Expected: none.
- [ ] **Step 5: Play check (owner):** button and P open it with the cursor in the search box; typing "p" in the box does not toggle it; Esc and the button close it; each filter and its count; search; each column sorts descending then ascending; blanks last; a rival planet shows its last-known owner with turns ago; a row click opens the planet panel and closes the screen; no human (zero-human match): button hidden, P does nothing; save, load, reopen: owners and ages persist. Checkpoint: stage the new file, `.meta`, and the modified files by path.

### Task 4: Documentation

**Files:**
- Modify: `CLAUDE.md` (a "The human UI (project 2, stage 3: the Planets screen)" subsection after stage 2), `FUTURE_FEATURES.md` (the project 2 entry: stage 3's Planets screen done, remainder listed)

- [ ] **Step 1:** Add the subsection: the three new classes, the last-known memory and its save field, the Hostile rule, and that P only opens the screen. Keep to the length of the stage 2 subsection.
- [ ] **Step 2:** In `FUTURE_FEATURES.md` mark the planet search and planet summary items of stage 3 built and leave the rest (top bar, Esc menu, diplomacy, board icons, pause settings, base output display) as remaining.
- [ ] **Step 3:** Run the self-check once more and `Run All AI Self-Checks`. Expected: all passed. Checkpoint: stage both docs; commit when the owner asks.
