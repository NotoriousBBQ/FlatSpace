# Manual Worker Assignment Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** On a planet the human develops by hand, show one icon per person grouped by Food / Grotsits / Industry / Research, and let the player drag people between categories, with a red warning when the result starves the planet.

**Architecture:** `Planet` gains an optional four-number worker override applied right after the unchanged `AssignWorkForStrategy` switch (renamed `AssignAutomaticWorkers`); population changes are absorbed by pure placement helpers that compare the player's counts with the algorithm's. A new `assignWorkers` `HumanCommand` (with a dry-run `Check`) is the only writer; a pure `HumanWorkerView` holds the icon/pick-up/drop/warning logic; a thin UI Toolkit `HumanWorkersView` draws it inside the existing `HumanPlanetOrdersView`.

**Tech Stack:** Unity 6000.4.1f1 C#, UI Toolkit built in code, `JsonUtility` saves, Editor self-checks (`[MenuItem]` suites; no test framework).

**Spec:** `docs/superpowers/specs/2026-10-28-manual-worker-assignment-design.md` (read it first; the owner's decisions and my calls are listed there). Read `CLAUDE.md` ("Human player mode", "Planet strategy") and `FUTURE_FEATURES.md` ("Planet strategy picker" is the deferred sibling) before starting.

## Global Constraints

- Category order everywhere is **Food, Grotsits, Industry, Research** (`Planet.WorkerCategory` values 0..3); override counts always add up to `Planet.Population.Count`.
- Planets without an override must behave **exactly** as today (AI economy and tuning-log numbers unchanged). AI players never get an override.
- Refusals come from a closed set; nothing throws. New reasons: `NotManual`, `BadWorkerCounts`, `WorkersDoNotMatchPopulation` (plus the existing `UnknownPlanet`, `NotYourPlanet`).
- Warning rule: shown when Food or Grotsits net per turn is negative **and** the stock lasts fewer than **10** turns (constant `HumanWorkerView.WarnTurns = 10`); the drop is never blocked.
- Placement/removal of people happens at the next `AssignWorkForStrategy` call (first step of `UpdatePlanet` and of `ProjectProduction`), one person at a time; the panel recomputes the normalized allocation on demand.
- Override ends on: switch to automatic (`setDevelopment` flag true), owner change, population 0.
- Saved flag rule: absent/false/0 = "as before" (no override).
- A self-check never touches `Gameboard.Instance`; methods a self-check calls are `public`; do not call `UpdatePlanet` on a fixture planet (zero per-worker rates divide by zero), use `ProjectProduction`. Fixture planets have zero worker rates, so set `CurrentStrategy = PlanetStrategyIndustry` to make the automatic allocation deterministic (`[0,0,pop,0]`).
- Line endings: detect per file (`GameButtonHandler.cs` and `MainScreenUIController.cs` are LF, most others CRLF); `git diff --stat` after each edit must show only intended lines. New scripts: stage the `.meta` too. Stage by explicit path, never `git add -A Assets`.
- Commit/push **only when the owner asks**: commit steps below mean "stage these paths and commit if the owner has said to commit; otherwise leave staged and report".
- Unity cannot run from the command line: after each task read `%LOCALAPPDATA%\Unity\Editor\Editor.log` for `error CS`, then ask the owner to focus Unity and run the named self-check menu item; report its result honestly.

## Review Focus

- Several population changes in one turn (+2 arrivals, a conquest dropping 5 to 1): counts stay non-negative and add up (Task 1 test `WorkerPlacementMultiStep`).
- A loss from a category the player emptied (count 0): the person comes from a category with people (Task 1 test `RemoveOneSkipsEmptyCategory`).
- Mixed planet: `Population.Count` includes a rival's people; the override sums to the whole count, and losing the majority clears it via the owner change (Task 1 test `OverrideClearsOnOwnerChange`).
- A drop arriving after a turn changed the population (stale icons): refused with `WorkersDoNotMatchPopulation`, nothing applied, panel refreshes (Task 2 test `StaleDropRefused`, Task 5 ghost cleanup).
- Old save without the fields, and a saved override whose sum no longer matches the population: loads as no override (Task 3 test).
- Switching to automatic while an override exists clears it and the icons become display-only (Task 2 test `AutomaticClearsOverride`, Task 5 Play check).

---

## File Structure

- Modify `Assets/Flatspace/Objects/Planets/Planet.cs`: override state, pure helpers, split of `AssignWorkForStrategy`, clearing, event sink.
- Modify `Assets/Flatspace/Player/Human/HumanCommand.cs` (new `workers` field) and `HumanCommandProcessor.cs` (`assignWorkers`, `Check`, `setDevelopment` clears).
- Modify `Assets/Flatspace/SaveSystem/SaveLoadSystem.cs` (`PlanetSave` fields, write) and `Assets/Flatspace/GameAI/GameAIMap.cs` (`SetPlanetSimulationStats` restore).
- Create `Assets/Flatspace/Player/Human/HumanWorkerView.cs` (pure view logic).
- Create `Assets/Flatspace/UI/HumanUI/HumanWorkersView.cs`; modify `HumanPlanetOrdersView.cs` to host it.
- Modify `Assets/Flatspace/Diagnostics/AITuningLogger.cs`, `Assets/Flatspace/Objects/Board/GameBoard.cs` (sets the sink), `.claude/skills/tuning-log/SKILL.md`, `CLAUDE.md`.
- Modify `Assets/Editor/HumanModeSelfCheck.cs`, `Assets/Editor/HumanUiSelfCheck.cs` (register new suites in each `RunChecks`).

---

### Task 1: Planet override, helpers and placement

**Files:**
- Modify: `Assets/Flatspace/Objects/Planets/Planet.cs` (worker fields ~108-116, `AssignWorkForStrategy` line 423, `ProjectProduction` 593, `SetPlanetOwnership` 867, `ChangePopulation` end ~857)
- Test: `Assets/Editor/HumanModeSelfCheck.cs` (new `RunWorkerOverrideChecks`, added to `RunChecks`)

**Interfaces:**
- Produces (all on `Planet`):
  - `public enum WorkerCategory { Food = 0, Grotsits = 1, Industry = 2, Research = 3 }`
  - `public static WorkerCategory LeftoverCategory(PlanetStrategy strategy)` - Food for Balanced/Growth/Food/FocusedFood, Grotsits for Grotsits/FocusedGrotsits, Research for Research/FocusedResearch, Industry for Industry/FocusedIndustry (the category the switch gives leftover workers).
  - `public static int[] NormalizeWorkers(int[] raw, int population, PlanetStrategy strategy)` - negatives to 0; while sum > population remove from Food first, then the largest category (ties lowest index); while sum < population add to `LeftoverCategory`.
  - `public static WorkerCategory PlaceOne(int[] current, int[] automatic, PlanetStrategy strategy)` - category with the largest `automatic - current`; ties go to `LeftoverCategory`, then lowest index.
  - `public static WorkerCategory RemoveOne(int[] current, int[] automatic)` - among categories with `current > 0`, the largest `current - automatic`; ties go to the larger `current`, then lowest index.
  - `public bool HasWorkerOverride { get; private set; }`
  - `public int[] CurrentWorkerCounts()` - the shown allocation (override if present, else normalized automatic), a fresh 4-array summing to `Population.Count`; recomputed on demand.
  - `public void SetWorkerOverride(int[] counts)` - caller has checked the sum; stores a copy, sets the flag, and applies it to `FoodWorkers`/`GrotsitsWorkers`/`IndustryWorkers`/`ResearchWorkers`.
  - `public void ClearWorkerOverride(string reason)` - no-op without an override; reasons used: `"Automatic"`, `"OwnerChanged"`, `"NoPopulation"`; calls the sink.
  - `public int[] WorkerOverrideCounts()` (copy, or null) and `public void RestoreWorkerOverride(int[] counts)` (save load; no event).
  - `public static System.Action<Planet, string, string> WorkerEventSink` - called `(planet, "Placed", "Arrival|Loss <Category>")` and `(planet, "Cleared", reason)`; null in self-checks.

- [ ] **Step 1: Write the failing tests** `RunWorkerOverrideChecks` in `HumanModeSelfCheck.cs` (style of `RunHumanFlagChecks`; `Fixture(1)` with `f.Own(0, 5)` and `f.P(0).CurrentStrategy = Planet.PlanetStrategy.PlanetStrategyIndustry`). Assertions, each with a label:
  - `NormalizeWorkers` cases: `{8,0,0,0}` pop 5 (any strategy) is `{5,0,0,0}`; `{2,2,2,2}` pop 9 with a Research strategy is `{2,2,2,3}` (shortfall to the leftover category); `{3,3,3,3}` pop 8 is `{0,2,3,3}` (Food emptied first, then the largest, ties to the lowest index); a negative entry reads as 0; the result always sums to the population.
  - `PlaceOneByDeficit`: `PlaceOne({2,0,2,0}, {2,0,2,1}, Industry)` is Research; a tie case goes to `LeftoverCategory`.
  - `RemoveOneSkipsEmptyCategory`: `RemoveOne({0,3,0,1}, {1,2,0,1})` is Grotsits; never returns a category with count 0.
  - `OverrideRespectedByProjectProduction`: `SetWorkerOverride({1,1,2,1})` then `ProjectProduction()`: `FoodWorkers==1, GrotsitsWorkers==1, IndustryWorkers==2, ResearchWorkers==1`; `CurrentWorkerCounts()` equals it; `HasWorkerOverride`.
  - `NoOverrideSameAsBefore`: with no override `CurrentWorkerCounts()` is `{0,0,5,0}` and the four worker fields after `ProjectProduction()` equal those from a second identical planet (guards the anti-goal).
  - `WorkerPlacementMultiStep`: override `{1,1,2,1}`, then `ChangePopulation(2, player)` (pop 7), `ProjectProduction()`: counts add to 7, none negative, the two new people landed where `PlaceOne` says (automatic is `{0,0,7,0}`, so both go to Industry: `{1,1,4,1}`); then `ChangePopulation(-5, player)` (pop 2): counts add to 2, none negative.
  - `OverrideClearsOnOwnerChange`: with an override on P0, `ChangePopulation` so another player has the majority: `HasWorkerOverride` false; also `ChangePopulation(-all)` clears with reason `NoPopulation`. Capture reasons via `Planet.WorkerEventSink` set in the test and reset to null in `finally`.
  - A sink assertion: one `"Placed"` event per placed person with detail `"Arrival Industry"`.
- [ ] **Step 2: Run to verify failure** - the suite does not compile until the members exist; expected result is the compile error naming `Planet.WorkerCategory` (record that as the red run). Do not ask the owner to run Unity yet.
- [ ] **Step 3: Implement** in `Planet.cs`:
  - Rename the current `AssignWorkForStrategy` to private `AssignAutomaticWorkers` (body untouched). New private `AssignWorkForStrategy()` calls it, then `if (HasWorkerOverride) ApplyWorkerOverride();`.
  - `ApplyWorkerOverride()`: take `automatic = NormalizeWorkers({FoodWorkers,GrotsitsWorkers,IndustryWorkers,ResearchWorkers}, Population.Count, CurrentStrategy)`; while override sum < `Population.Count` add `PlaceOne(override, automatic, CurrentStrategy)` (sink `"Placed"`, `"Arrival <cat>"`); while greater, subtract `RemoveOne(override, automatic)` (`"Loss <cat>"`); then write the override to the four worker fields.
  - `CurrentWorkerCounts()`: runs `AssignAutomaticWorkers()` (so it is fresh), normalizes, and when an override exists returns the override after the same sum adjustment (without sink events or mutation of the stored override; the stored one is updated at the next real `AssignWorkForStrategy`).
  - Call `ClearWorkerOverride("OwnerChanged")` inside `SetPlanetOwnership` when `Owner != before`, and `ClearWorkerOverride("NoPopulation")` where `Population.Count == 0` is handled at the end of `ChangePopulation` (next to `ClearBunkers()`).
- [ ] **Step 4: Run to verify pass** - read `Editor.log` for `error CS`; then the owner runs `FlatSpace -> Human -> Run Human Mode Self-Check`. Expected: `ALL PASSED`, assertion count higher than before.
- [ ] **Step 5: Commit** `Planet.cs`, `HumanModeSelfCheck.cs` (message `feat: planet worker override and placement helpers`).

---

### Task 2: The `assignWorkers` command

**Files:**
- Modify: `Assets/Flatspace/Player/Human/HumanCommand.cs` (add `public List<int> workers;` beside `ships`, comment: four counts in category order)
- Modify: `Assets/Flatspace/Player/Human/HumanCommandProcessor.cs` (`Dispatch` case at ~175, `Check` ~46, `HandleSetDevelopment` ~513, class summary at top)
- Test: `Assets/Editor/HumanModeSelfCheck.cs` (new `RunWorkerCommandChecks`)

**Interfaces:**
- Consumes: Task 1 (`SetWorkerOverride`, `ClearWorkerOverride`, `CurrentWorkerCounts`).
- Produces: command `{ type = "assignWorkers", origin = planetName, workers = [food, grotsits, industry, research] }`. `HumanCommandProcessor.Check(command)` returns null when it would be accepted (dry run: nothing stored), else the reason.

- [ ] **Step 1: Write failing tests** `RunWorkerCommandChecks` (uses `Refusal(processor, command)` helper; planet `P0` owned with 5 people, `ManualDevelopment = true`, Industry strategy):
  - `AssignAppliesAndDetail`: valid `{1,1,2,1}` is accepted (`Refusal == ""`), `HasWorkerOverride`, the planet's `CurrentWorkerCounts()` equals it, and the result `detail` contains `"1/1/2/1"`.
  - Refusals: unknown planet `UnknownPlanet`; planet owned by player 1 `NotYourPlanet`; `ManualDevelopment=false` `NotManual`; `workers == null`, length 3, or a negative entry `BadWorkerCounts`; `{1,1,1,1}` (sum 4 of 5) `WorkersDoNotMatchPopulation`.
  - `StaleDropRefused`: build the counts for pop 5, add one person (`ChangePopulation(1, human)`), send the same command: `WorkersDoNotMatchPopulation`, and `HasWorkerOverride` unchanged.
  - `CheckIsDryRun`: `Check` of a valid command returns null and leaves `HasWorkerOverride` false; `Check` of an invalid one returns its reason.
  - `AutomaticClearsOverride`: with an override, `setDevelopment` with `flag = true` (automatic) clears it (`HasWorkerOverride` false); `flag = false` (manual) keeps nothing to clear.
- [ ] **Step 2: Run to verify failure** - compile error on the missing `workers` field (record as red).
- [ ] **Step 3: Implement** `private string HandleAssignWorkers(HumanCommand command, out string detail)` using `OwnedPlanet(command.origin, out var refusal)`, then `ManualDevelopment`, then shape/negatives, then `sum == planet.Population.Count`; when `_dryRun` return null before mutating; otherwise `planet.SetWorkerOverride(counts)` and `detail = $"{planet.PlanetName} {f}/{g}/{i}/{r}"`. Add `"assignWorkers"` to the dry-run case list in `Check` (not to `CheckBatch`). In `HandleSetDevelopment`, when `command.flag` is true call `planet.ClearWorkerOverride("Automatic")` (skip when `_dryRun`). Update the class summary's command list.
- [ ] **Step 4: Run to verify pass** - `Editor.log` clean, owner runs the Human Mode Self-Check: `ALL PASSED`.
- [ ] **Step 5: Commit** `HumanCommand.cs`, `HumanCommandProcessor.cs`, `HumanModeSelfCheck.cs` (`feat: assignWorkers command`).

---

### Task 3: Saves

**Files:**
- Modify: `Assets/Flatspace/SaveSystem/SaveLoadSystem.cs` (`PlanetSave` near line 90, writer near 298), `Assets/Flatspace/GameAI/GameAIMap.cs` (`SetPlanetSimulationStats`, near the `manualDevelopment` line 432)
- Test: `Assets/Editor/HumanModeSelfCheck.cs` (new `RunWorkerSaveChecks`)

**Interfaces:**
- Consumes: `Planet.WorkerOverrideCounts()` / `RestoreWorkerOverride(int[])`.
- Produces: `PlanetSave.hasWorkerOverride` (bool) and `PlanetSave.workerOverride` (`List<int>` of 4); an older save has neither and loads with no override.

- [ ] **Step 1: Write failing test** `RunWorkerSaveChecks`: build a `PlanetSave` the way `SetPlanetSimulationStats` reads it (check first whether that method can be called from a fixture without `Gameboard.Instance`; if it can, drive it, else test the restore through a small `public static` helper you extract, e.g. `GameAIMap.ApplyWorkerOverrideFromSave(Planet, PlanetSave)`). Assertions: round trip of `{1,1,2,1}` on a 5-person planet; `hasWorkerOverride=false` gives no override; a saved override whose sum is not the planet's population is discarded; a list whose length is not 4 is discarded.
- [ ] **Step 2: Run to verify failure** (compile error or failing assertion; record it).
- [ ] **Step 3: Implement** the two `PlanetSave` fields, write them from `planet.WorkerOverrideCounts()`, restore with validation (length 4, sum equals `Population.Count`, else leave none).
- [ ] **Step 4: Run to verify pass** - owner runs the Human Mode Self-Check. Also ask the owner for one Play check: save a game with an override, load it, same icons (done again in Task 5's Play check).
- [ ] **Step 5: Commit** the three files (`feat: save the worker override`).

---

### Task 4: `HumanWorkerView` (pure view logic)

**Files:**
- Create: `Assets/Flatspace/Player/Human/HumanWorkerView.cs` (namespace `FlatSpace.Human`, like `HumanPlanetView`; stage its `.meta`)
- Test: `Assets/Editor/HumanUiSelfCheck.cs` (new `RunWorkerViewChecks`, added to `RunChecks`)

**Interfaces:**
- Consumes: Task 1 `CurrentWorkerCounts`, Task 2 command shape, `HumanPlanetView.OwnsPlanet`, `HumanPlanetView.NetPerTurn`.
- Produces (static class `HumanWorkerView`):
  - `public const int WarnTurns = 10;`
  - `public static bool CanAssign(Planet planet, int humanId)` - `OwnsPlanet(planet, humanId) && planet.ManualDevelopment`.
  - `public static int PickedCount(int categoryCount, int iconIndex)` - `categoryCount - iconIndex` (icon index 0-based in reading order; the pressed icon and every icon after it, wrapped lines included); 0 when the index is out of range.
  - `public static int[] AfterDrop(int[] counts, Planet.WorkerCategory from, Planet.WorkerCategory to, int picked)` - a new array; `from == to` or `picked <= 0` returns an unchanged copy.
  - `public static HumanCommand DropCommand(Planet planet, int[] counts, Planet.WorkerCategory from, Planet.WorkerCategory to, int picked)` - an `assignWorkers` command for `AfterDrop`, or null when nothing would change.
  - `public static List<string> Warnings(Planet planet)` - lines like `"Food: -3 per turn, stock lasts 4 turns"` / `"Grotsits: -2 per turn, stock is empty"`, only when net < 0 and turns of stock < `WarnTurns`; stock turns = `floor(stock / -net)`; empty list otherwise.

- [ ] **Step 1: Write failing tests** `RunWorkerViewChecks`:
  - `PickedCount(5, 3) == 2` (second from the right of five), `PickedCount(5, 0) == 5`, `PickedCount(5, 4) == 1`, `PickedCount(5, 5) == 0`.
  - `AfterDrop({3,1,2,0}, Food, Research, 2)` is `{1,1,2,2}`; same category and `picked 0` leave it unchanged; the result always sums to the input sum.
  - `DropCommand` for a real move has `type == "assignWorkers"`, the planet name and `workers` equal to `AfterDrop`; null for a same-category drop.
  - `CanAssign`: true for an owned manual planet, false for automatic and for a rival's.
  - `Warnings`: on a manual fixture planet (pop 5, override `{0,1,4,0}`, zero worker rates, base food 1 so net Food is negative) with `Food = 1f` the list has a line starting `"Food:"` containing `"stock lasts"`; with `Food = 100f` it has no Food line (the stock lasts well over 10 turns); with `Food = 0f` the Food line reads `"stock is empty"`. Assert presence/absence and those phrases, not exact numbers (morale scales the rate).
- [ ] **Step 2: Run to verify failure** (compile error on `HumanWorkerView`; record it).
- [ ] **Step 3: Implement** the class with the signatures above; `Warnings` calls `HumanPlanetView.NetPerTurn` and reads `planet.Food`/`planet.Grotsits`.
- [ ] **Step 4: Run to verify pass** - `Editor.log` clean, owner runs `FlatSpace -> Human -> Run Human UI Self-Check`: `ALL PASSED`.
- [ ] **Step 5: Commit** `HumanWorkerView.cs`, its `.meta`, `HumanUiSelfCheck.cs` (`feat: worker view logic`).

---

### Task 5: The Workers section (UI)

**Files:**
- Create: `Assets/Flatspace/UI/HumanUI/HumanWorkersView.cs` (namespace `Game.UI.MainGameScreenUI`; stage its `.meta`)
- Modify: `Assets/Flatspace/UI/HumanUI/HumanPlanetOrdersView.cs` (add the section to the scroll body after the rate lines, ~line 87)

**Interfaces:**
- Consumes: Task 4 (`HumanWorkerView`), Task 2 command via the existing `Send("Workers", command)` of `HumanPlanetOrdersView`.
- Produces: `public sealed class HumanWorkersView` with `public VisualElement Build(Planet planet, int humanId, System.Action<HumanCommand> send)` returning the section (title, four category boxes with icon rows that wrap, warning lines in `HumanUiKit.Bad`), and `public void CancelDrag()` (removes a ghost).

No self-check covers pixels (as with `FleetSummarySelfCheck`); the pure logic is covered in Task 4. This task's verification is a Play check.

- [ ] **Step 1: Implement `Build`.** Counts come from `planet.CurrentWorkerCounts()`. Each category is a labelled box (fixed minimum height so an empty one can receive a drop) holding that many small coloured square icons in a wrapping row (`flexWrap = Wrap`); colours per category are the builder's choice. Icons are pressable only when `HumanWorkerView.CanAssign(planet, humanId)`; otherwise the section is display-only. Add one `HumanUiKit.Text(line, HumanUiKit.Bad)` per `HumanWorkerView.Warnings(planet)` entry (warnings show on any manual planet the human sees in full).
- [ ] **Step 2: Drag.** Follow `FleetUIController.MakeDraggable` (`PointerDownEvent` captures the pointer, a ghost `Label` with the picked count follows `PointerMoveEvent`, `PointerUpEvent` releases): on press compute `HumanWorkerView.PickedCount(count, iconIndex)`, show the ghost at once, dim the picked icons; on release find the box whose `worldBound` contains `evt.position`; if it is another category call `send(HumanWorkerView.DropCommand(...))`, otherwise do nothing (the rebuilt/unchanged rows show the icons back in place). The ghost is attached to the panel root and removed on release and by `CancelDrag()`, which `HumanPlanetOrdersView.Bind` calls before it clears the body (a turn can interrupt a drag, as `FleetUIController` does).
- [ ] **Step 3: Host it.** In `HumanPlanetOrdersView.Bind`, after the three rate lines, `scroll.Add(_workers.Build(planet, humanId, c => Send("Workers", c)))` for a planet the human owns (`owned`); a refusal already shows through `_status`. Create `_workers` in the constructor.
- [ ] **Step 4: Verify.** `Editor.log` has no `error CS`; owner re-enters Play mode (wiring runs in `Awake`/`Start`) and checks: icons per category match the rates; drag from the middle of a wrapped category takes that icon and the rest; drop on an empty category; drop outside returns them; a starving assignment shows the red line and clears when fixed; Development switched to Automatic clears the assignment and the icons stop dragging; save, load, same icons; a ninth person arriving is placed by the strategy (use a colony ship); window sizes other than the owner's; the limited panel for a planet out of sight shows no section. Report what was and was not tried.
- [ ] **Step 5: Commit** `HumanWorkersView.cs`, its `.meta`, `HumanPlanetOrdersView.cs` (`feat: the Workers section of the planet panel`).

---

### Task 6: Tuning log, docs

**Files:**
- Modify: `Assets/Flatspace/Diagnostics/AITuningLogger.cs` (after `LogHumanDefeat`, ~line 797), `Assets/Flatspace/Objects/Board/GameBoard.cs` (where `Planet.HumanOwnerTest` is set), `.claude/skills/tuning-log/SKILL.md`, `CLAUDE.md`, `FUTURE_FEATURES.md`

**Interfaces:**
- Consumes: `Planet.WorkerEventSink` (Task 1).
- Produces: two log lines (human matches only):
  - `T<turn>|P<owner>|WorkersPlaced|<planet>|<Arrival or Loss>|<category>` - `AITuningLogger.LogWorkersPlaced(int turnNumber, int playerId, string planet, string arrivalOrLoss, string category)`.
  - `T<turn>|P<owner>|WorkersCleared|<planet>|<Automatic, OwnerChanged or NoPopulation>` - `AITuningLogger.LogWorkersCleared(int turnNumber, int playerId, string planet, string reason)`.
  Both fire once per event, so no tracker class; both no-op when no match is logging (`if (_currentLogPath == null) return;`). The applied command is already logged by the existing `HumanCommand` line (its detail carries the counts). Questions they answer: how often and why overrides end (`Cleared`), and whether the arrival/loss rule puts people where the player would (`Placed`, compared with the following `assignWorkers` commands on the same planet).

- [ ] **Step 1:** Add the two logger methods in the style of `LogHumanPause`.
- [ ] **Step 2:** In `Gameboard`, set `Planet.WorkerEventSink` while a human plays (next to `HumanOwnerTest`; clear it where that is cleared): the sink maps `("Placed", "Arrival Industry")` and `("Cleared", reason)` to the two logger calls with `TurnNumber` and `planet.Owner`. Skip the call when the planet's owner is not the human.
- [ ] **Step 3:** Document: the two lines in `.claude/skills/tuning-log/SKILL.md` (human matches only; one-off lines), a paragraph in `CLAUDE.md` under "Human player mode" (the override, `Planet.AssignAutomaticWorkers`/`ApplyWorkerOverride`, `assignWorkers`, `HumanWorkerView`, `HumanWorkersView`, the normalization rule and that the first drag on a Food/FocusedFood planet lowers food output - the owner accepted that), and update the `FUTURE_FEATURES.md` "Planet strategy picker" entry only if the build changed what it says.
- [ ] **Step 4: Verify.** `Editor.log` clean; owner runs `Run All AI Self-Checks` (expected all suites pass, assertion counts up) and a short Play run with `_logAIEvents` on, checking `WorkersPlaced` after an arrival and `WorkersCleared` after switching to Automatic appear in the newest `AITuningLogs` file (the logger has no self-check by design).
- [ ] **Step 5: Commit** the five files (`docs+feat: worker override log lines and notes`).

---

## Tuning log output (session rule 4)

Covered in Task 6: two new human-match lines, `WorkersPlaced` and `WorkersCleared`, no tracker (one event, one line), `HumanCommand` already carries the counts. Matching updates to `.claude/skills/tuning-log/SKILL.md` and the "AI Tuning Log"/"Human player mode" notes in `CLAUDE.md` are in Task 6 Step 3. AI players get no lines because nothing changes for them.

## Self-review

- **Spec coverage:** Workers section and drag (Tasks 4, 5); pick-up rule (Task 4 `PickedCount`, Task 5); drop outside returns (Task 5); display-only on automatic and no section on limited panel (Task 5); red warning with the 10-turn threshold (Task 4 `Warnings`); saved override and old saves (Task 3); arrivals/losses by the strategy rule at the next assignment (Task 1); override ends on automatic / owner change / population 0 (Tasks 1, 2); `assignWorkers` with `Check` and the closed refusals (Task 2); normalization incl. Food over-assign and the accepted food drop (Task 1 `NormalizeWorkers`, Task 6 docs); tuning lines (Task 6); AI economy unchanged (Task 1 `NoOverrideSameAsBefore`); strategy picker deferred (already in `FUTURE_FEATURES.md`).
- **Type consistency:** `WorkerCategory`, `CurrentWorkerCounts`, `SetWorkerOverride`, `ClearWorkerOverride`, `WorkerEventSink`, `HumanWorkerView.DropCommand`, `assignWorkers`/`workers` are named identically across tasks.
- **Proportion:** signatures, rules and test assertions only; bodies are left to the implementer except where a rule is not determined by the signature (normalize, place, remove).
