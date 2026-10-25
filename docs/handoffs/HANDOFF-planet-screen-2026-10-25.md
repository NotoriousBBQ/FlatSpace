# Handoff: Planets screen and what the human sees (human UI stage 3, first piece)

Saved from the Claude Code session named **"planet_screen"**, on 2026-10-25. It followed `HANDOFF-human-player-ui-stage2-2026-10-23.md`.

Read `CLAUDE.md` first (the "Human player mode" section and its three UI subsections: stage 1, stage 2, stage 3), then `FUTURE_FEATURES.md`. Spec
`docs/superpowers/specs/2026-10-25-human-player-ui-stage3-planets-screen-design.md`, plan `docs/superpowers/plans/2026-10-25-human-player-ui-stage3-planets-screen.md`.

## Where things stand

**Asked:** the housekeeping from the last handoff, then the first piece of stage 3: "planet search, combined with summary as a single screen". During the owner's Play checks
that grew into a set of rules for what the human may see of planets and fleets, plus a fix for an over-eager message.

**Done, all merged to `main` and pushed (`origin/main` = `c28cf10`):**

1. **Housekeeping:** "Human player mode" (project 1) moved to `completed_features.md` (`e7c25c9`). `main` was already pushed; the three stage branches were already gone.
2. **The Planets screen** (design `0238710`/`3811957`, feature `ade4397`, merge `1ff1946`). HUD **Planets** button or the **P** key (P only opens it; Esc or the button closes it). A search
   box (cursor in it on open), four radio filters (All known, Colonization targets, Mine, Hostile activity; default Mine) and one table row per planet the human has seen (the fog view)
   plus every planet it owns. Columns: Planet, Owner, Type, Pop, Food, Grots, Ind, Res, Building (item and turns left), Ships (count and owners, e.g. `3 (You 1, P1 2)`), Alerts.
   Every column sorts (first click descending, then ascending, blanks last, Building by item name). A row click opens that planet's panel and closes the screen.
   - `HumanPlanetTable` (pure: rows, filters, search, sort) and `PlanetSightMemory` (pure: last-known owner and turn, saved as `GameSave.humanSightings`, fresh per match) in
     `Assets/Flatspace/Player/Human/`; `PlanetsScreen` in `Assets/Flatspace/UI/HumanUI/`; wiring in `Gameboard`, `MainScreenUIController`, `GameButtonHandler`, `SaveLoadSystem`.
   - Owner column: current owner for a planet in sight or next to a planet the human occupies or has a ship at (`Gameboard.HumanOwnerVisible`); otherwise the last owner seen with its age
     (`Player 2 (7 turns ago)`, `Unowned (7 turns ago)`); blank for a planet never seen live.
   - Colonization targets use the AI's own rule (`PlayerAI.IsValidColonizationTarget(planet, requireKnown)`, a new overload), need no colony ship, and exclude planets a rival holds.
   - Hostile activity: the human's blockaded, converted or enemy-ship planets; planets where the human's ships cut another player's orders in the last 5 turns
     (`Gameboard._humanBlockadingSince`, from `LastBlockadeCuts`, any stance, not saved); rival planets the human converts. Docked ships that cut nothing do not count.
   - The Colonize dialog now lists only planets the human has seen (`ColonizeOptions(..., hasSeen)`).
3. **Blockade messages** (`f18479f`, merge `3db89d9`): `Blockaded: <planet>` (and its pause) now covers only the human's own planets and planets where one of its orders was cut
   (`HumanTurnEvents.RelevantBlockades`). `BlockadeView` is unchanged (the AI needs it for routing).
4. **Rival fleets are a count** (`a9f6428`, merge `cff7a93`): while a human plays, a rival's fleet shows only a ship count in the icon popup, the order-line popup and the fleet panel
   (`HumanHoverText.ShowFullFleetDetail(owner, humanId)` is the one rule: full detail for the human's own fleets and for every fleet when no human plays). A rival's fleet panel opens
   only for a planet in sight now, never a dimmed one (`Gameboard.ShowFleetUI`). **Debug mode** was added to `FUTURE_FEATURES.md`.
5. **Dimmed planets next to the human's** (`bd8ce8c`, merge `c28cf10`): on the board such a planet keeps its name, owner colour and fleet icons and hides population, food, grotsits and
   morale (`PlanetUIObject.SetFogState(..., nextToHuman)`). Clicking any planet not in sight opens a **limited planet panel** (`Gameboard.PlanetDetailIsLimited`): name, an `Owner:` line
   (`Gameboard.OwnerLabel`), population only when next to the human's (`?/max` otherwise); the stock, production and order sections are hidden. The `Owner:` line also shows on full panels.
   The Planets screen's Pop column shows the population of such planets too.

**Verified, and how:** the owner ran `FlatSpace -> Human -> Run Human UI Self-Check` after every change; it passed each time (it now includes `RunSightMemoryChecks`,
`RunPlanetTableChecks`, `RunBlockadeRelevanceChecks` and the hover-text checks). The owner played each change and reported the fixes as good ("Looks good", "Good, commit and push").
A fresh opus review of the Planets screen branch found no Critical issue and one Important (Hostile counted docked warships at peace), fixed.

**Not verified:**
- The blockade message for a rival blockade of the human's **own** planet, and for one of the human's shipments being **cut** (the owner could not test: shipment automation has no UI yet).
- `HumanModeSelfCheck`, `Run All AI Self-Checks` and the fleet/hover Play checks were not rerun by the owner as separate steps after the last change; only the Human UI self-check was reported.
- The planet panel at window sizes other than the owner's; the Planets screen on a very small window beyond "fits the window".

## Files and commits

- On `main`, pushed: `e7c25c9` (feature list), `3811957` + `0238710` (design), `ade4397` (Planets screen) / `1ff1946` (merge), `f18479f` / `3db89d9` (blockade messages), `a9f6428` / `cff7a93`
  (fleet detail), `bd8ce8c` / `c28cf10` (dimmed planets). This handoff is committed after them.
- New: `Assets/Flatspace/Player/Human/{PlanetSightMemory,HumanPlanetTable}.cs`, `Assets/Flatspace/UI/HumanUI/PlanetsScreen.cs` (all with `.meta`), the design and the plan.
- Changed (main ones): `GameBoard.cs`, `SaveLoadSystem.cs`, `PlayerAI.cs` (the `IsValidColonizationTarget(planet, requireKnown)` overload), `HumanPlanetView.cs`, `HumanTurnEvents.cs`, `HumanHoverText.cs`,
  `ColonizeDialog.cs`, `FleetIconHandle.cs`, `FleetUIController.cs`, `PlanetUIObject.cs`, `PlanetDetailUIController.cs`, `MainScreenUIController.cs`, `GameButtonHandler.cs`, `HumanUiSelfCheck.cs`, `CLAUDE.md`,
  `FUTURE_FEATURES.md`, `completed_features.md`.
- Branches: the four feature branches (`human-player-ui-stage3`, `blockade-notification-relevance`, `fleet-detail-visibility`, `adjacent-planet-ui`) were deleted, local and remote. Still there on purpose or
  untouched: local `profiling` and `conversion-surge-reach`; remote `conversion-surge-reach`, `human-player-mode`, `human-player-ui` (the last two can go; the owner only asked for the four).
- Not in git: `.superpowers/` (gitignored; the visual companion's mockups, server stopped).

## Decisions made and why

- **One screen, a table (option B), not a list with a side pane (yours):** you scan columns; all columns sortable, first click descending; filters are radio buttons; Building shows turns left.
- **Blank cells where you cannot see the figure, plus an owner column (yours):** and the owner shown is the last-known one with its age (yours), "turns since" instead of "old".
- **Hostile activity (yours):** includes rival planets you blockade or convert; **a docked ship alone is not hostile**; a blockade is real cuts of shipments, **not** stance (you were blockading at peace).
- **"Known" means seen (yours):** the AI's knowledge reaches 6 path nodes; the screen and the Colonize dialog use the fog view (visible or explored) plus your own planets.
- **Colonization targets need no colony ship (yours)** and exclude planets a rival already holds.
- **Rival fleets are a count while a human plays, full detail for AI-only matches and (later) debug mode (yours);** no opening a rival's fleet panel on a dimmed planet (yours).
- **Dimmed planets next to yours show name, owner colour, population (yours);** the limited panel applies to every dimmed planet (my call); `Owner:` line on full panels too (my call).
- **My calls the owner did not overrule:** opens on Mine; sort and filter reset per match; blanks sort last; a row click closes the screen; the 5-turn memory of "I am blockading"; "You" for the human in
  the Owner column; Industry and Research columns show last turn's produced value (0 before the first turn); the sight memory is saved as a `GameSave` field, not in `PlayerSave`.
- **Rejected or changed after Play:** the P key toggling (it typed a "p" in the search box, so P only opens); the first filter group (`RadioButtonGroup` could not be laid out in a row; now a `GroupBox` of `RadioButton`s);
  tooltips (not shown on runtime panels; owners are inline in the Ships cell); a dark search field (the caret cannot be recoloured in code: the field is light grey with black text).

## Open items and next steps

1. **Re-verify the blockade message** once shipment automation has a UI (own planet blockaded by a rival; own shipment cut). A memory note keeps this reminder.
2. **Rest of stage 3** (`FUTURE_FEATURES.md`): smaller top bar (research per turn as a decimal, no grotsits field), Esc / system menu with a small Next Turn, diplomacy screen (each pair's state, "unknown"
   without contact), clickable board icons, a place for the pause settings (`setPause` has no UI), and **show a planet's base output and each improvement's effect**.
3. **Debug mode** (new `FUTURE_FEATURES.md` entry): one switch, off by default in a human match, always on with no human; it would make `HumanHoverText.ShowFullFleetDetail` return true, show full figures in the
   Planets screen and re-enable the debug fog views. Design it before adding more per-display exceptions.
4. **Open bug outside the UI** (still not investigated): a colony ship moved by hand cannot colonize at the destination and the source can no longer colonize (see the `FUTURE_FEATURES.md` entry).
5. **Tuning issues you noticed** (you never said which; ask).
6. **Deferred minors** from the Planets screen review: the Owner column sorts as text ("Player 10" before "Player 2", the age takes part); a populated planet with a population tie shows a blank owner
   (`Planet.Owner` is NoOwner on a tie); Industry/Research use last turn's output while Food/Grotsits are projected; no self-check for a rival siegeship counting as "Enemy ships" or for the at-war cases;
   `PlanetUIObject.SetFogState` hides the stats panel's children by position in the prefab (`NamePanel` and `StatsPanel`), so a prefab restructure would need a look; the sort arrow is ASCII (`v`/`^`).
   Older deferred minors from stages 1 and 2 still stand (their handoffs).
7. **Housekeeping:** delete `origin/human-player-mode` and `origin/human-player-ui` when you like; `player_control.md` still describes the early idea.

## Gotchas

- Unity cannot run from the command line: every check is the owner running a `[MenuItem]` self-check or a Play run. Unity recompiles only when its window is focused; read
  `%LOCALAPPDATA%\Unity\Editor\Editor.log` for `error CS`, and ask the owner for warnings and errors from the Console (they pasted two obsolete-API warnings and two compile errors this session).
- **Line endings:** most files are CRLF but `GameButtonHandler.cs` and `MainScreenUIController.cs` are LF. A blanket `sed 's/$/\r/'` converted two of them and made the whole file show as changed; use a patch
  script that detects the file's endings (read `\r\n` -> `\n`, write back in the same style) and check `git diff --stat` afterwards.
- **Patch scripts:** write them to a file with the Write tool (scratchpad) and run them with `python3 -I`; large bash heredocs with Python failed ("unexpected EOF") once. Compute everything before opening a file
  for writing. A script that asserts on a multi-line anchor can fail on the line breaks; anchor on a single line.
- **UI Toolkit in this Unity:** runtime tooltips do not show; `ITextSelection.cursorColor/selectionColor` are obsolete (the USS replacement cannot be set inline); `FindObjectsByType` takes no sort-mode argument;
  `RadioButtonGroup` children are vertical (use a `GroupBox` row of `RadioButton`s); a `TextField`'s default input draws dark text on a dark panel.
- **The AI's "known" is not what the human has seen:** `PlayerKnowledge` reaches `maxPathNodesForKnowledge` (6) path nodes and is only updated inside a turn, so it is empty on turn 1. Use the fog view
  (`Gameboard.HumanHasSeenPlanet`) for anything the human is shown.
- **`BlockadeView` lists every visible planet where a rival's docked offense beats the human's** (any owner), for routing; do not show it as news. The human's own blockades come from `LastBlockadeCuts`.
- A rival's order line and icon popups, the fleet panel and the Planets screen all go through the same "may the human see this" decisions; add a new display to them rather than a new rule.
- Standing rules (unchanged): a recommendation with every choice; a yes before code; commit and push only when asked; use they/them when pronouns are unstated; stage files by explicit path
  (never `git add -A Assets`); the owner's "unit" means a milestone.
