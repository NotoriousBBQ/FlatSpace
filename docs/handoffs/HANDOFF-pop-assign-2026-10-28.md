# Handoff: Population assignment (manual worker assignment)

Saved from the Claude Code session named **"pop_assign"** (session ID `4ae60c2b-8317-4ea2-ac08-fa4e05e9261a`), 2026-10-28.
Resume it with `/resume` (pick it) or `claude --resume "pop_assign"`. This file is the written version so nothing depends on that history.

Read `CLAUDE.md` first, especially the "Human player mode" section: it now has a "Manual worker assignment" bullet. The design is
`docs/superpowers/specs/2026-10-28-manual-worker-assignment-design.md` and the plan is `docs/superpowers/plans/2026-10-28-manual-worker-assignment.md`.

## Where things stand

The session opened with "where are we?" (stage 3 of the human UI: only the Planets screen was built). The owner then asked for one addition before
the next piece: on a planet in manual mode the player can assign population to each production category, while new population is still placed by
the automatic algorithm (the player only overrides it). That feature is **built, reviewed, committed and merged to `main`**.

What the player sees: the Workers section of the planet panel shows one square per person in four boxes (Food, Grotsits, Industry, Research), in
rows that wrap. Pressing a square picks up that square and every square after it in the same category (wrapped lines included). Dropping on another
box assigns those people there; dropping anywhere else puts them back. A red line warns when Food or Grotsits would run out within 10 turns (the
drop still goes through, no confirmation).

| Area | What it does | Key code |
|---|---|---|
| Override | four counts summing to the population, plus a flag; `AssignWorkForStrategy` runs `AssignAutomaticWorkers` (the old switch) and then `ApplyWorkerOverride` | `Planet` region "Manual worker assignment (human UI)" |
| Arrival and loss | one person at a time at the next assignment, by the strategy rule (`PlaceOne`, `RemoveOne`, `LeftoverCategory`) | `Planet.cs` |
| Ends | switching to Automatic, an owner change, population 0 | `ClearWorkerOverride`, `SetPlanetOwnership`, `ChangePopulation`, `HandleSetDevelopment` |
| Command | `assignWorkers` (origin, `workers` = 4 counts); refusals `NotManual`, `BadWorkerCounts`, `WorkersDoNotMatchPopulation`; `Check` supports it | `HumanCommandProcessor.HandleAssignWorkers` |
| Saves | `PlanetSave.hasWorkerOverride` / `workerOverride`; an older save, a wrong length, a negative count or a sum that is not the population loads as no override | `SaveLoadSystem`, `GameAIMap.WorkerOverrideFromSave` |
| View logic | pick-up count, drop result, drop command, warnings (pure) | `HumanWorkerView` |
| UI | the Workers section, drag with a ghost label | `HumanWorkersView`, hosted by `HumanPlanetOrdersView` |
| Tuning log | `WorkersPlaced\|<planet>\|<Arrival or Loss>\|<category>` and `WorkersCleared\|<planet>\|<Automatic, OwnerChanged or NoPopulation>` | `AITuningLogger`, `Gameboard.SetHumanPlayer` (sets `Planet.WorkerEventSink`) |

Two changes beyond the original ask, both owner-driven or found while testing:
- **Planet panel layout (owner request):** the Food, Grotsits and Industry/Research rows at the top of the panel are hidden while the order section shows
  (they stay for planets the human cannot order on, and when no human plays). The order section now shows the rates in two columns: Food and Grotsits
  (stock and rate) on the left, Industry and Research (production) on the right.
- **Load bug fix (existing bug, found by this work):** `GameAIMap.SetPlanetSimulationStats` now calls `planet.Population.Clear()` before restoring the
  saved people. `InitGame` had already given each planet its type's starting population, so a loaded planet carried both (a Prime PopTest planet saved at 15 loaded
  with 30; the owner saw "17/15"). That also made the override's sum check fail, which is why the split was lost on load. Population-driven numbers on a loaded
  save may look different from before because of this.

### Verified

- The Unity Editor self-checks, run by the owner after each task: `Run Human Mode Self-Check` (`RunWorkerOverrideChecks`, `RunWorkerCommandChecks`,
  `RunWorkerSaveChecks`), `Run Human UI Self-Check` (`RunWorkerViewChecks`) and `Run All AI Self-Checks`, all passed.
- Owner's Play checks: drag and drop, dropping outside, the warning line, Automatic clearing the split, layout (good), a short run with the tuning log on,
  and save/load (after the fix above).
- A fresh Opus reviewer read the whole diff: no Critical or Important findings, "ready to merge".

### Not verified

- The red steps were never observed: each task's tests were written before the code, so the red state was a compile failure nobody saw in the Editor log.
- No self-check covers the load path (it needs the full `Gameboard` load); the load fix is checked only by the owner's Play re-test.
- Pixels (the drag section's layout at other window sizes) were checked by the owner's Play look only.
- `AITuningLogger` has no self-check by design; the two new lines were checked in a Play run's log.

## Files

Committed on `main` (nothing pushed):
- `c3fb9ba` feat: manual worker assignment on the planet panel, merged as `168a1ed` (branch `manual-worker-assignment`, now deleted).
  - Changed: `.claude/skills/tuning-log/SKILL.md`, `Assets/Editor/HumanModeSelfCheck.cs`, `Assets/Editor/HumanUiSelfCheck.cs`,
    `Assets/Flatspace/Diagnostics/AITuningLogger.cs`, `Assets/Flatspace/GameAI/GameAIMap.cs`, `Assets/Flatspace/Objects/Board/GameBoard.cs`,
    `Assets/Flatspace/Objects/Planets/Planet.cs`, `Assets/Flatspace/Player/Human/HumanCommand.cs`, `HumanCommandProcessor.cs`,
    `Assets/Flatspace/SaveSystem/SaveLoadSystem.cs`, `Assets/Flatspace/UI/HumanUI/HumanPlanetOrdersView.cs`,
    `Assets/Flatspace/UI/MainGameScreenUI/PlanetDetailUIController.cs`, `CLAUDE.md`, `FUTURE_FEATURES.md` (the strategy picker entry).
  - New: `Assets/Flatspace/Player/Human/HumanWorkerView.cs`, `Assets/Flatspace/UI/HumanUI/HumanWorkersView.cs` (each with its `.meta`),
    the spec and the plan.

Uncommitted (written at the close-out):
- `FUTURE_FEATURES.md`: a "Stage 3, manual worker assignment moved to completed_features on 2026-10-28" line under the human UI entry.
- `completed_features.md`: the new sub feature under "Human player mode, project 2: UI rework".
- `docs/handoffs/HANDOFF-pop-assign-2026-10-28.md` (this file).

## Decisions and why

- **Icons grouped by category, one per person; a press takes that icon and every later one in the same category** (the owner changed this from "the current row and subsequent rows" to "same category").
- **New arrivals use the existing built-in strategy**, not a picker. The picker is recorded in `FUTURE_FEATURES.md` ("Planet strategy picker").
- **Warn, don't block:** a starving assignment shows a red line (10-turn threshold) and the drop goes through with no confirmation.
- **First drag on a Food or FocusedFood planet may lower food output**, because those strategies over-assign food workers on purpose and the shown split is normalised to the population. The owner accepted this ("yes, accept it").
- **Rulings made by the executor** (full list in the final message of the session; the ledger was deleted at merge):
  - the branch lived in the shared working tree, not a worktree, because Unity reads this directory;
  - task completion was taken from the owner's self-check runs, since Unity tests cannot run from the CLI;
  - the save writes the adjusted `CurrentWorkerCounts()` (a guess at the load problem that turned out not to be the cause; harmless, still in);
  - the load fix is `Population.Clear()`;
  - the top rows stay for planets the human cannot order on and when no human plays;
  - the worker log lines use the human's id with no owner filter.
- **Subagents:** the owner had already allowed subagents; one fresh reviewer was used for the final review.

## Open items and next steps

1. **Next piece of stage 3: the diplomacy screen** (recommended). The rest of stage 3 is still open: top bar, Esc menu, clickable board icons, pause settings, base-output display (the "Remaining of stage 3" entry in `FUTURE_FEATURES.md`), and the debug mode.
2. **Remind the owner when the Planets screen work is finished** (memory note): planet ownership indicators; the fleet hover popup showing too much on dimmed planets; re-verify the Blockaded-notification fix once shipment automation has a UI.
3. **Deferred minors from the final review** (none are bugs that block play; address if they show up):
   - where an arrival or loss lands depends on whether a panel or the Planets screen is open (`ProjectProduction` vs the next `UpdatePlanet`), and the `WorkersPlaced` turn stamp can shift by one;
   - the save bakes in a pending arrival, so no `WorkersPlaced` line is logged after the load for that person;
   - the drag has no pointer-capture-lost handler (Alt-Tab mid-drag leaves dimmed icons and a ghost until the next refresh), and `PointerUp` ignores which button;
   - the ghost is parented to the panel root, so pressing Esc mid-drag can leave capture and a release can still send `assignWorkers`;
   - the drop hit-test ignores the scroll view's clipping;
   - `HumanCommandProcessor.Check(assignWorkers)` is never called during the drag, so there is no accept or refuse feedback until after the drop.
4. **Strategy picker** (`FUTURE_FEATURES.md`): let the player choose a planet's `PlanetStrategy`; it pairs with this feature.
5. **Optional, offered and not started:** a script for the filtering and metrics of the tuning-log analysis plus a short prompt, so a smaller local model can narrate a single player's log (the owner asked about this in this session; the answer was that a skill is a Claude Code construct and would have to be adapted as a prompt).

## Gotchas

- **Never use `sed -i` on the repo's files:** most are CRLF and it strips them to LF. Use a CRLF-preserving patch (a Python exact-anchor replacement). `GameButtonHandler.cs` and `MainScreenUIController.cs` are LF.
- **Stage files by explicit path** and include the `.meta` files; never `git add -A Assets`.
- **`PlanetSave` is nested:** write `SaveLoadSystem.GameSave.PlanetSave`, not `SaveLoadSystem.PlanetSave` (a compile error cost a round trip).
- **Fixture planets have zero worker rates:** do not call `UpdatePlanet` on them in a self-check; use `ProjectProduction`. `CurrentStrategy = PlanetStrategyIndustry` makes the automatic split `[0,0,pop,0]`, which makes expectations simple.
- **A self-check never uses `Gameboard.Instance`;** and wiring that runs in `Awake`/`Start` needs the owner to exit and re-enter Play mode after a recompile.
- **Planet init adds a starting population per type** (`_initialPopulation`, Prime PopTest 15). Anything that restores state onto an initialised planet must account for it.
- **The owner's standing rules:** commit and push only when asked; give a recommendation with every choice; use they/them when pronouns are unstated.
