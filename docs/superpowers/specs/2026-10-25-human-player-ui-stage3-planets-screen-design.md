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
  the screen (a text field must swallow it), so **P only opens it** and the HUD button or Esc closes it (my call). Nothing else on the keyboard reaches the board while the box has focus (the map input has no WASD pan today, so this is only the P key).
- Opens on the **Mine** filter (my call). Sort and filter are remembered while a match runs (static state, like `ResearchScreen`) and **reset at match start and on load** (my call).

**Search and filters**
- A search box: typing narrows the rows to planets whose name contains the text (case-insensitive), within the active filter. Searching "known planets" means
  the filter All known; with another filter active the search narrows that filter (my guess; the builder should state it in the UI as a plain behaviour, not special-case it).
- Four filters as **radio buttons** (exactly one active), with a count each:
  - **All known**: every planet in `Knowledge.KnownPlanets(humanId)` (`PlayerKnowledge`).
  - **Colonization targets**: the targets the existing Colonize dialog offers (owner: reuse). Use `HumanPlanetView.ColonizeOptions` (per origin) unioned over the human's
    planets that can colonize, de-duplicated by target; do not write a second target rule.
  - **Mine**: planets owned by the human (`Planet.Owner == humanId`).
  - **Hostile activity** (owner): the human's planets that are blockaded, being converted (`Planet.ConversionBy != NoOwner`) or hold an enemy warship docked, **plus rival
    planets the human is blockading or converting**. A rival planet where the human merely has ships docked does **not** count (owner). The human's own blockades and conversions of a rival planet always count, even when that planet is not in sight (my call). The own-planet conditions already exist in `HumanOngoing.IsStillTrue` / `HumanTurnEvents` (blockade list, `ConversionBy`);
    the rival-planet side is `Planet.ConversionBy == humanId` for conversion; for blockades the builder finds the human's blockade data in `PlayerAI`/`BlockadeView` and passes it into `HumanPlanetTable` (my call: the pure builder takes it as a parameter).

**The table** (owner chose a table, option B). One row per planet; the same columns for every row, including a rival's or an unowned planet:

| Column | Content |
|---|---|
| Planet | name |
| Owner | the owner's name, "Player N" for a rival (the notification list's wording), blank if unowned. For a planet not in sight now it is the **last-known owner followed by the turns since the human last saw it**, e.g. "Player 2 (7 turns ago)" (owner's call; see "Last-known memory"). Blank with no suffix if never seen live |
| Type | planet type |
| Pop | population / max population |
| Food, Grotsits, Industry, Research | per-turn net rate, as the planet panel shows them (`HumanPlanetView.NetPerTurn`, `Planet.ProjectProduction`) |
| Building | the item being built **and the turns until it completes**, e.g. "Warship, 5 turns". Blank for an empty queue, no estimate (`HumanBuildList.QueueEstimates` returns null) or a planet the human cannot see. **Sorts alphabetically by the item's name** (owner) |
| Ships | docked ship count |
| Alerts | Blockaded, Converting, and the like |

- **Figures the human cannot see are blank** (owner), except the owner column, which shows the last-known owner (owner's call, see above). The rule for "can see" is the one `Gameboard.HumanSeesPlanet` already applies (the human's fog view, falling back to
  known planets when there is no fog system); a planet that is not live-visible shows only what the human knows from before. Rates, building, ships and alerts are blank for a planet not currently visible; type and max population are static and always shown. Which exact cells go blank beyond that is the builder's call, stated in the plan. The owner's own planets are always fully shown.
- **Sorting** (owner): every column sorts, alphabetically for text and numerically for numbers. The **first click on a heading sorts descending, the second ascending**
  (then it alternates). Blank cells **always sort to the bottom**, in either direction (my guess, awaiting confirmation). A sorted-by marker on the heading.
- **Row click** opens that planet's panel and centres the board on it: `Gameboard.GoToPlanet` (the same entry a notification click uses). The screen stays open or closes as the Fleets
  summary does (closes: the fleet summary's rule, "choosing a row is the whole job"; my guess).

## Last-known memory (new state, owner's request)

Nothing in the code remembers a planet's owner or when the human last saw it (`PlayerKnowledge` only records that a planet is known). So the Owner column needs a small new
record, **presentation-only like the fog layer and kept for the human only**: per planet, the owner and the turn at which the human last saw it live. It is refreshed at the end of each
turn for every planet `HumanSeesPlanet` reports in sight, never read by the simulation, and saved with the human's player data in `GameSave.PlayerSave` as plain lists
(a missing field loads as "nothing remembered yet", per the standing rule that false/0/null means "as before"). A planet never seen live shows no owner. A pure class
(`PlanetSightMemory`) holds it so the self-check can cover it. The suffix wording is "N turns ago"; a planet in sight shows no suffix.

## Architecture (reuse)

- A pure builder in `Assets/Flatspace/Player/Human/`, `HumanPlanetTable`, next to `HumanFleetList` and `HumanNotificationList`: rows from a `GameAIMap`, the human id, an "is visible" predicate
  (the same shape as `HumanHoverText.CanSee`'s) and the turn; filter, search and sort as pure functions over the rows. No `Gameboard.Instance`.
- The view `Assets/Flatspace/UI/HumanUI/PlanetsScreen.cs` is layout only; it is refreshed with the other human panels after each turn and after a command (find where `FleetSummaryScreen.Refresh` is called and add it beside).
- Turns until the building item completes: reuse `HumanBuildList`'s cumulative queue estimates (it already estimates turns for queue items); do not add a second estimator.
- New files need their `.meta` committed.

## Testing

A self-check, `Assets/Editor/HumanUiSelfCheck.cs` extended (or a section in it; it is already in `Run All AI Self-Checks`): filters (each of the four, including both sides of Hostile and the docked-only case staying out), search, sort
(first click descending, second ascending, blanks last, text vs numeric), blank rules for a non-visible rival planet, and the owner column. Fixture rules from `CLAUDE.md` apply (never `Gameboard.Instance`,
distinct planet positions, no `UpdatePlanet` on a fixture without worker rates). The screen itself is checked by a Play run (button, P key, cursor in the search box, WASD-free typing, row click, Esc).

## Tuning log

No new log lines: a read-only view of existing state. `CLAUDE.md` gets a short subsection under "The human UI" (stage 3: the Planets screen).

## Open calls (mine, for the owner to overrule)

Opens on Mine; sort and filter reset at match start and on load; blanks sort last; the search narrows the active filter; a row click closes the screen; P only opens the screen; the human's
own blockades and conversions always count for Hostile; the table refreshes once a turn and after a command; an empty Colonization targets filter just shows 0; the last-known memory is
saved with the human's player data. Pop sorts by current population, Ships by count, Alerts as text (empty last).
