# Human player UI, stage 3: the Planets screen

Date 2026-10-25. Follows `2026-10-23-human-player-ui-stage2-design.md`. Read `CLAUDE.md` ("Human player mode" and its two UI subsections) first.
Work from the "Human player mode, project 2: UI rework" entry in `FUTURE_FEATURES.md`.

## Intent and scope

When playing the human, the owner wants to (1) find any known planet quickly, then (2) scan everything they own. Today the only ways to reach a planet are
clicking it on the board or a notification. This stage adds **one screen, the Planets screen**, that combines the entry's "planet search popup" and
"planet summary" items.

**This screen is all of stage 3 for now (owner).** The rest of the stage 3 entry (smaller top bar, Esc/system menu with a small Next Turn, diplomacy
screen, clickable board icons, a home for the pause settings, base output and improvement effects on the planet panel) is NOT part of this design and
stays in `FUTURE_FEATURES.md`.

Goals: fast keyboard-first search; an at-a-glance table of every planet the human knows; filters for the four questions the owner asks.
Non-goals: no new orders from this screen (it only navigates); no changes to the simulation, saves or the AI; no tuning-log lines (it is a read-only view,
nothing for a Play run to measure). Anti-goal: a screen that shows a rival's figures the human cannot see.

## The screen

A new UI Toolkit screen built in code like `FleetSummaryScreen` and `ResearchScreen` (`HumanUiKit` styling, shown through the same host, static
`Toggle`/`Close`/`Refresh`/`ContainsScreenPoint`). Only available while a human plays.

**Opening and closing**
- A **Planets** HUD button (built in code in `MainScreenUIController`, under the Fleets button, hidden unless a human plays) and the **P** key toggle it. The
  Esc path (`GameButtonHandler.EscapeButtonPressed`) closes it first when it is open. P is free in the project's input map (`MapInputActions` has only zoom
  and `OpenMainMenu`/Escape); add it as a new action in the map and regenerate the wrapper from `Assets/InputSystem_Actions.inputactions`, or read the key
  directly, whichever matches how `OpenMainMenu` is wired (builder's call).
- **The search box has focus on open**, so typing starts a search at once (owner). While the search box has focus, the P key must type a "p", not toggle
  the screen (a text field must swallow it); the HUD button and Esc still close it.
- Opens on the **Mine** filter (my guess, not yet confirmed by the owner). Sort and filter are remembered for the rest of the session (static state, like `ResearchScreen`; my guess).

**Search and filters**
- A search box: typing narrows the rows to planets whose name contains the text (case-insensitive), within the active filter. Searching "known planets" means
  the filter All known; with another filter active the search narrows that filter (my guess; the builder should state it in the UI as a plain behaviour, not special-case it).
- Four filters as **radio buttons** (exactly one active), with a count each:
  - **All known**: every planet in `Knowledge.KnownPlanets(humanId)` (`PlayerKnowledge`).
  - **Colonization targets**: the targets the existing Colonize dialog offers (owner: reuse). Use `HumanPlanetView.ColonizeOptions` (per origin) unioned over the human's
    planets that can colonize, de-duplicated by target; do not write a second target rule.
  - **Mine**: planets owned by the human (`Planet.Owner == humanId`).
  - **Hostile activity** (owner): the human's planets that are blockaded, being converted (`Planet.ConversionBy != NoOwner`) or hold an enemy warship docked, **plus rival
    planets the human is blockading or converting**. The own-planet conditions already exist in `HumanOngoing.IsStillTrue` / `HumanTurnEvents` (blockade list, `ConversionBy`);
    the rival-planet side (the human's blockades and conversions of others) must be derived from `GameAI`/`PlayerAI` blockade and conversion state (builder to find it).

**The table** (owner chose a table, option B). One row per planet; the same columns for every row, including a rival's or an unowned planet:

| Column | Content |
|---|---|
| Planet | name |
| Owner | the owner's name, "Player N" for a rival (the notification list's wording), blank if unowned |
| Type | planet type |
| Pop | population / max population |
| Food, Grotsits, Industry, Research | per-turn net rate, as the planet panel shows them (`HumanPlanetView.NetPerTurn`, `Planet.ProjectProduction`) |
| Building | the item being built **and the turns until it completes**, e.g. "Warship, 5 turns" |
| Ships | docked ship count |
| Alerts | Blockaded, Converting, and the like |

- **Figures the human cannot see are blank** (owner). The rule for "can see" is the one `Gameboard.HumanSeesPlanet` already applies (the human's fog view, falling back to
  known planets when there is no fog system); a planet that is not live-visible shows only what the human knows from before. Which cells are blank for a planet that is
  known but not currently visible (for example owner, type and max population are static facts, rates and building are not) is the builder's call, stated in the plan. The owner's own planets are always fully shown.
- **Sorting** (owner): every column sorts, alphabetically for text and numerically for numbers. The **first click on a heading sorts descending, the second ascending**
  (then it alternates). Blank cells **always sort to the bottom**, in either direction (my guess, awaiting confirmation). A sorted-by marker on the heading.
- **Row click** opens that planet's panel and centres the board on it: `Gameboard.GoToPlanet` (the same entry a notification click uses). The screen stays open or closes as the Fleets
  summary does (closes: the fleet summary's rule, "choosing a row is the whole job"; my guess).

## Architecture (reuse)

- A pure builder in `Assets/Flatspace/Player/Human/`, `HumanPlanetTable`, next to `HumanFleetList` and `HumanNotificationList`: rows from a `GameAIMap`, the human id, an "is visible" predicate
  (the same shape as `HumanHoverText.CanSee`'s) and the turn; filter, search and sort as pure functions over the rows. No `Gameboard.Instance`.
- The view `Assets/Flatspace/UI/HumanUI/PlanetsScreen.cs` is layout only; it is refreshed with the other human panels after each turn and after a command (find where `FleetSummaryScreen.Refresh` is called and add it beside).
- Turns until the building item completes: reuse `HumanBuildList`'s cumulative queue estimates (it already estimates turns for queue items); do not add a second estimator.
- New files need their `.meta` committed.

## Testing

A self-check, `Assets/Editor/HumanUiSelfCheck.cs` extended (or a section in it; it is already in `Run All AI Self-Checks`): filters (each of the four, including both sides of Hostile), search, sort
(first click descending, second ascending, blanks last, text vs numeric), blank rules for a non-visible rival planet, and the owner column. Fixture rules from `CLAUDE.md` apply (never `Gameboard.Instance`,
distinct planet positions, no `UpdatePlanet` on a fixture without worker rates). The screen itself is checked by a Play run (button, P key, cursor in the search box, WASD-free typing, row click, Esc).

## Tuning log

No new log lines: a read-only view of existing state. `CLAUDE.md` gets a short subsection under "The human UI" (stage 3: the Planets screen).

## Open calls (mine, for the owner to overrule)

Opens on Mine; sort and filter remembered for the session; blanks sort last; the search narrows the active filter; a row click closes the screen; the P key is ignored while the search box has focus.
