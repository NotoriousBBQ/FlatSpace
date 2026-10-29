# Manual worker assignment (human UI, planet panel)

Date 2026-10-28. Requested by the owner as an addition to the planet panel before the rest of stage 3 of the human UI.
Related: `FUTURE_FEATURES.md` entry "Planet strategy picker" (added with this design), "Human player mode, project 2" (stage 3).

## Intent

A human player who develops a planet by hand (`Planet.ManualDevelopment`) can already queue production, but cannot decide what the planet's
people work on. Today `Planet.AssignWorkForStrategy` splits the population into Food, Grotsits, Industry and Research workers every turn from
the planet's built-in `CurrentStrategy`. The human wants to override that split: move people from one category to another, directly on the
planet panel. New population keeps being placed by the existing algorithm, so the player only steers and never has to maintain the split.

## What the player sees and does

- The planet panel gets a **Workers** section for a planet the human owns. It shows one icon per person, grouped by category in the order
  Food, Grotsits, Industry, Research. A category with more icons than fit on a line wraps onto further lines. Every category has a drop box
  even when it holds no one, so an empty category can receive people.
- **Pick up.** Pressing (mouse down) on an icon picks up that icon and every icon after it in the same category, including its wrapped lines
  (an icon is "after" another by its position in the category's reading order, left to right then top to bottom). Example: with 5 icons in Food,
  pressing the second from the right picks up 2 icons. The picked-up icons are attached to the cursor (a ghost showing the icons or a count).
- **Drop.** Releasing over another category's box moves the picked-up people to that category; the counts and icon rows redraw at once.
  Releasing anywhere else, including the same category, puts them back where they were (nothing changes, no command is sent).
- **Automatic planets** show the icons but cannot be dragged (the owner did not object to this; treated as agreed). A planet the human cannot
  see in full (the limited panel for planets out of sight) shows no Workers section.
- **Warning.** When the resulting rates would starve the planet, a red line in the Workers section says what is short and how long the stock
  lasts, for example "Food: -3 per turn, stock lasts 4 turns" (Food and Grotsits are the two resources that can run out). The drop still
  goes through; the line clears when the assignment no longer starves the planet. No confirmation box.
- The planet panel's per-turn rates and stock text update after a drop, as they already do after any command.

## Rules

1. **The override is stored per planet** as four whole numbers (food, grotsits, industry, research) that add up exactly to the population. It
   is saved with the game and holds from turn to turn. With no override the planet behaves exactly as today.
2. **The first drop creates the override** from the allocation currently shown (see "Shown allocation"), then applies the move.
3. **New arrivals** (population grows by a colonist, growth or conquest): the new person goes where the built-in strategy would put one more
   person. The player's other assignments do not move.
4. **Losses** (population falls): the person comes out of the category where the built-in strategy would take one fewer; the player's other
   assignments do not move.
5. **The override ends** (workers go back to the algorithm) when the planet is switched to automatic development, when the planet changes
   hands, and when its population reaches 0.
6. AI players never have an override, and the algorithm's output for planets without an override is unchanged, so every existing economy
   number and tuning log stays comparable.

## Goals, non-goals, anti-goals

- Goal: the human can steer a manual planet's workers with one drag, and always sees what the assignment does to food and grotsits.
- Non-goal: a strategy picker (recorded in `FUTURE_FEATURES.md`; new arrivals use the planet's existing built-in strategy).
- Non-goal: tracking individual people (an icon is only a unit of one worker; there is no identity, name or age).
- Non-goal: AI use of overrides, automatic rebalancing, a "reset to automatic split" button (an easy later addition; not asked for).
- Non-goal: changing `AssignWorkForStrategy` for planets without an override.
- Anti-goal: anything that changes the AI's economy or the existing tuning-log numbers.

## Decisions and calls

Owner's decisions: the drag interaction as described above; new arrivals use the built-in strategy; the strategy picker is deferred; the
red warning line (not a confirmation box); the pick-up rule (icon plus everything after it in the same category).

My calls (the owner can overrule; the builder should treat them as settled unless told otherwise):

- **Shown allocation.** The built-in algorithm can assign more workers than there are people (Food and FocusedFood over-assign food on
  purpose, see "Planet strategy" in `CLAUDE.md`). The panel and the override always use an allocation that adds up to the population: the
  algorithm's counts, with any excess taken off Food first, then the other categories largest first; any shortfall added to the category the
  algorithm would give leftovers to. The simulation for a planet without an override still uses the raw algorithm counts.
- **Placing an arrival or removing a loss without stored algorithm state:** run the algorithm for the new population (normalized as above).
  An arrival goes to the category where the algorithm's count exceeds the player's count by the most; a loss comes out of the category where
  the player's count exceeds the algorithm's by the most. Ties break by the strategy's own priority order (arrival: the strategy's
  leftover category first; loss: the largest category). This is stateless, so nothing about the algorithm needs saving.
- **When a person is placed or removed.** Not at the moment the population changes, but at the next `AssignWorkForStrategy` call (the first
  step of `UpdatePlanet`, and of `ProjectProduction`): it compares the override's total with the current population and places or removes
  the difference one person at a time using the rule above, running the algorithm on the planet's state at that call. This handles several
  changes in one turn, and `WorkersPlaced` is logged there (one line per person).
- **The panel between turns.** For display and for building the first override, the normalized allocation is recomputed on demand from the
  planet's current population (not read from the counts left by the last `UpdatePlanet`, which can be stale after an arrival). The
  `assignWorkers` sum check therefore always runs against a fresh allocation.
- **Override stored in four ints plus a flag**, `PlanetSave` fields where false/0 means "no override" (the project's rule for saved flags).
- **One command, `assignWorkers`**, with `origin` (the planet) and four counts; flat `HumanCommand` fields cannot hold four named counts
  today, so add four ints (names are the builder's choice, e.g. `foodWorkers` ...). It is refused with a reason from the closed set when:
  the planet is not the human's, not in manual development, the counts are negative, or do not add up to the population. A successful command
  replaces the override and takes effect at once (like the other UI commands, executed between turns through `Gameboard.SubmitHumanCommands`).
  `HumanCommandProcessor.Check` supports it as a dry run, used while dragging to show whether a drop would be accepted.
- **Warning threshold.** The red line appears when the projected net rate of Food or Grotsits is negative and the stock covers fewer than
  10 turns of it; the threshold is a constant in the pure view builder. Rates come from `Planet.ProjectProduction` (workers assigned as a turn
  would, no stock change), which must honour the override.
- **Pure view builder** `HumanWorkerView` in `Assets/Flatspace/Player/Human/` (rows, counts, which icons a press picks up, the drop result,
  the warning text), like the other `Human*` list builders, so the UI code is thin and the logic is self-checked.
- **UI pattern.** UI Toolkit, built in code in `Assets/Flatspace/UI/HumanUI/`, hosted by `HumanPlanetOrdersView` inside the existing planet
  panel, dark theme via `HumanUiKit`. The drag follows the ghost-label pattern already used by `FleetUIController` for dragging ship rows
  between fleet boxes (reuse it, including cleaning up the ghost if a turn interrupts a drag).
- **Warning line is shown on any manual planet** the human can see in full, whether or not an override exists (the cause is irrelevant to
  the player; the information is useful either way).

- **Owner ruling (2026-10-28): the first drag on a Food or FocusedFood planet may lower its food output**, because the shown allocation is
  normalized to the population (over-assigned food workers are dropped). Accepted as is.

## Constraints

- `Planet` is a simulation `MonoBehaviour`; the override lives in `Planet` and is applied inside the worker assignment, so `UpdatePlanet`,
  `ProjectProduction` and the projected-worker code all see it. `ProjectedFoodWorkers`/`ProjectedGrotsitsWorkers` read `FoodWorkers` etc., so
  they pick the override up without separate handling; verify this.
- Follow the project conventions: the command processor never touches `Gameboard.Instance`; a self-check never depends on
  `Gameboard.Instance` (several `Planet` methods, `GetPopulationFraction` among them, touch it transitively); a fixture planet type has no
  per-worker food rate, so do not call `UpdatePlanet` on it without setting the rates (it divides by zero).
- Saves: `GameSave.PlanetSave` gains the override fields; restored in `GameAIMap.SetPlanetSimulationStats`; an older save has no
  override.
- Files with a CRLF/LF mix (see the last handoff): detect each file's line endings before patching; `GameButtonHandler.cs` and
  `MainScreenUIController.cs` are LF, most others CRLF. Stage files by explicit path, include any new `.meta`.
- Unity cannot run from the command line: verification is the owner running the self-check menu item and a Play session; read
  `Editor.log` for `error CS` after edits.

## Verification

- Extend `HumanModeSelfCheck` (planet and command rules) and `HumanUiSelfCheck` (the pure `HumanWorkerView`), both already in
  `Run All AI Self-Checks`. Cases: the override is respected by `AssignWorkForStrategy`/`ProjectProduction`; counts always add up to the
  population including the Food over-assign strategies; arrival placement; loss removal; each refusal reason; the override clears on
  automatic, on a change of owner and at population 0; the pick-up set (icon plus those after it, wrapped lines included, last icon
  alone, first icon picks the whole category); the warning appears and clears; an AI planet's allocation is identical with and without the
  feature (guards the anti-goal).
- Save round trip of the override if the existing save self-checks can do it without `Gameboard.Instance`; otherwise a Play-mode check by
  the owner (save, load, same icons).
- Play check by the owner: drag from the middle of a wrapped category, drop on an empty category, drop outside, a starving assignment
  (red line), an arrival on a manual planet, switching to automatic, window sizes other than the owner's.

## Tuning log output

The feature changes nothing for AI players, so it adds no tuning lines for them. An applied `assignWorkers` command is already logged by the existing `HumanCommand` line (its detail field carries the four counts, so no separate line is added: reuse). For the human's play it adds two one-off lines (each is logged at the moment of the event, so no tracker class is needed, the same as the `Human*` lines):

- `T<turn>|P<playerId>|WorkersPlaced|<planet>|<Arrival or Loss>|<category>` - when population changes on a planet with an override and a
  person is placed or removed by the algorithm rule. Answers: whether the placement rule behaves sensibly (check against the category the
  player would have chosen).
- `T<turn>|P<playerId>|WorkersCleared|<planet>|<Automatic, OwnerChanged or NoPopulation>` - when an override ends. Answers: how long
  overrides last and why they end.

Added in the same tasks as the code: the `AITuningLogger.Log*` methods and call sites, the lines in `.claude/skills/tuning-log/SKILL.md`
(with the rule that they appear only in human matches), and one sentence in the "Human player mode" part of `CLAUDE.md`. Self-check:
the logger writes to the real filesystem and has no self-check by design; verify the three lines in a real Play run with `_logAIEvents` on.

## Left to the builder

Icon size, colours per category, ghost appearance, the exact placement in the panel (below the rates is suggested), the field names, the
exact wording of the warning line, and how the new `Planet` members are named.
