# Handoff: Human player UI, stage 2 (fleets, hover popups, fleet summary)

Saved from a Claude Code session with no name, so named for its content, on 2026-10-23. It followed the "human_player" session
(`HANDOFF-human-player-2026-10-23.md`), which built project 1 and UI stage 1.

Read `CLAUDE.md` first (the "Human player mode" section and its two "The human UI" subsections, stage 1 and stage 2), then `FUTURE_FEATURES.md`. Design
`docs/superpowers/specs/2026-10-23-human-player-ui-stage2-design.md` (including its "Calls made after the builder check"), plan
`docs/superpowers/plans/2026-10-23-human-player-ui-stage2.md`.

## Where things stand

**Asked:** stage 2 of the human UI: split a planet's ships into several fleets at one planet, drag one fleet to another planet to send it, combine two
fleets and drag that, hover popups on fleet icons and on order lines in flight (ships or resources, destination, turns left, rivals' lines too where both ends are
visible), a fleet summary screen, and the temporary JSON command file removed.

**Done, merged to local `main` (not pushed):** merge commit `f2a13f2`, which brings in `3153348` (the feature) and `7f4cbbf` (the review fixes); the design is
`ea68a32`. What it does:

- **Fleets are a label on ships:** `Ship.FleetId` (saved as `ShipSave.fleetId`, 0 = as before). Only the human's ships are grouped by it
  (`FleetSummary.ForPlanet(planet, humanId)`); the AI never reads it. A new fleet takes `Planet.NextFleetId(owner)`; ships arriving by an order dock as one new fleet
  (the orders of one move share `GameAIOrder.ArrivalGroup`, so a mixed fleet lands as one); built ships join `LowestFleetId`.
- **Commands:** `setFleet`, `mergeFleets` (instant, no orders), `moveResearch`; `HumanCommandProcessor.Check`/`CheckBatch` now cover `move` (a dry run that leaves the dock order alone).
- **Fleet panel** (`FleetUIController`): the human's ships as fleet boxes plus "+ New fleet"; drag a ship row to a box (`setFleet`), a box title onto a box (`mergeFleets`).
- **Board:** one icon per human fleet; hover and drag go through `PlanetUIObject` as well as `FleetIconHandle` (the planet's collider can win the raycast over an icon); a drag draws a
  green/red line (`Gameboard.CheckFleetDrag`, checked when the planet under the pointer changes) and a release sends the whole fleet (`Gameboard.TryDragFleet`); a refusal shows a toast.
- **Hover popups:** `HumanHoverPopup` fed by the pure `HumanHoverText` (`Order`, `Ships`, `CanSee`, `Pick` with a 10 px screen-space test); `Gameboard.UpdateLineHover`.
- **Fleets summary:** `FleetSummaryScreen` from a HUD Fleets button; a row click centres the board on the planet and closes the screen.
- **Removed:** `HumanCommandFile`, `Gameboard.RunHumanCommands`, `GameAI.PendingHumanOrders`. `HumanCommandProcessor.ApplyJson` stays for the self-checks.
- **Added at your request during the Play check:** moving the first queued production item up replaces the current production (the displaced item keeps its progress; `Planet`
  now resumes with `Progress += Industry`); the research queue can be reordered (Up/Down) but never so an item precedes its queued prerequisite (`PrerequisiteOrder`); the planet panel's
  "per turn" rates are projected (`Planet.ProjectProduction`) so a new or loaded game no longer shows every planet losing its population; the stock text is red only when it cannot cover the
  loss, the "SHORT" label is gone; the research screen is wider.

**Verified, and how:** you ran `HumanModeSelfCheck`, `HumanUiSelfCheck`, `FleetSummarySelfCheck` and `Run All AI Self-Checks`; all passed after the last change (both before and
after the review fixes). You played the UI over several Play runs and reported "everything checks out" (fleet split, drag, merge, hover, summary, queue changes, rate colours). A fresh opus
review of the whole branch found 0 Critical and 5 Important, all fixed in `7f4cbbf` (see Decisions). I never ran Unity; compile state was read from `Editor.log`.

**Not verified:** the review fixes that depend on Unity event timing, checked by self-check only where a pure part existed: a pan that starts on a planet no longer opens its panel (you were asked
to Play-check it and said the self-checks passed; nothing said whether the pan was tried), a drag line left behind when the dragged icon vanishes, a ghost label left in the fleet panel when a turn
interrupts a drag. A save and load of a match with several fleets was in the Play check list; you did not say it was done. The fleet 0 hover popup relies on the input module sending pointer-move
events to the planet (it worked in your Play check). Window sizes other than yours were not tried.

## Files and commits

- `ea68a32` design doc; `3153348` the feature (code, self-checks, `CLAUDE.md`, the `tuning-log` skill, `FUTURE_FEATURES.md`, the plan); `7f4cbbf` review fixes; `f2a13f2` merge into `main`.
- New: `Assets/Flatspace/Player/Human/{HumanHoverText,HumanFleetList}.cs`, `Assets/Flatspace/UI/FleetIconHandle.cs`, `Assets/Flatspace/UI/HumanUI/{HumanHoverPopup,FleetSummaryScreen}.cs` (all with `.meta`).
- Changed (main ones): `Ship.cs`, `Planet.cs`, `GameAI.cs`, `GameAIMap.cs`, `SaveLoadSystem.cs`, `GameBoard.cs`, `HumanCommandProcessor.cs`, `HumanFleetView.cs`, `HumanPlanetView.cs`, `HumanResearchTree.cs`,
  `FleetSummary.cs`, `PlanetUIObject.cs`, `FleetUIController.cs`, `MainScreenUIController.cs`, `HumanUiKit.cs`, `HumanPlanetOrdersView.cs`, `ResearchScreen.cs`, the three self-check files.
- **Uncommitted:** `FUTURE_FEATURES.md` and `completed_features.md` (the close-out: UI stages 1 and 2 moved to a "Partially done" entry) and this handoff. `main` is **not pushed**.
- Not in git: `.superpowers/sdd/2026-10-23-human-player-ui-stage2/` (the ledger with every ruling and the review package).

## Decisions made and why

- **Option C for splitting (yours):** drag ships between fleet boxes in the panel; **option A for the board (yours):** one icon per fleet beside the planet, drag it. Hover on an in-flight line shows the
  same ship text plus destination and turns; resource lines show type, amount, destination, turns. Rivals' lines get the same full detail where both ends are visible. A refused drop leaves the fleet and
  shows the reason. The fleet summary lists docked and in-flight fleets, and a row click centres the board and closes the screen (your correction after my first reading opened the fleet panel).
- **My calls in the spec (yours to overrule):** fleet ids are per owner per planet; arrivals form a fresh fleet; built ships join the lowest fleet; a drag is all or nothing; the drag starts on the uGUI icon and
  the drop planet is found with the `RaycastAll` test; a rival's line needs both ends visible to the human.
- **Rulings during the build:** the failing run for Tasks 1 to 3 was the compile failure of the new tests (no separate RED run for you); icon drags use Unity's drag distance, not a custom 6 px; drop feedback is the line
  colour only (no planet halo); `HumanCommandFile` and `PendingHumanOrders` were deleted outright; a saved production value for the rate bug was tried and replaced by projecting production on demand (it also fixes older saves).
- **Review fixes (Important, all fixed):** a pan that started on a planet opened its panel on release (the planet became both the pressed and dragged object); a mixed-kind fleet arrived as one fleet per kind;
  a displaced queued item lost its progress on load (the loader hard-coded 0); a stuck drag line when the icon vanished mid-drag; a stuck ghost label when a turn interrupted a panel drag.
- **Deferred by you, not worked:** the base output of a planet and each improvement's effect (added to stage 3); the colony ship manual-move versus Colonize inconsistency (outside the UI; recorded in `FUTURE_FEATURES.md`).
- **Judgment calls in the close-out:** only the entry's two labelled sub features (stages 1 and 2) were moved. The project 1 entry ("Human player mode", "Designed 2026-10-23 (approved)") has no built label, so it stayed in
  `FUTURE_FEATURES.md` even though it is built and merged (`3f5d083`, `f219ba1`); label it, then move it.

## Open items and next steps

1. **Stage 3** (recommended next), per the entry in `FUTURE_FEATURES.md`: the smaller top bar (research per turn as a decimal, no grotsits field), the Esc / system-icon menu with a small Next Turn, planet summary (owned or known),
   diplomacy screen (each pair's state, "unknown" without contact), planet search popup, clickable board icons, a place for the pause settings (`setPause` has no UI), and the new item: **show a planet's base output and each improvement's effect**.
2. **Tuning issues you noticed** (to be handled after stage 3; you did not say which, ask).
3. **Open bug (outside the UI):** a colony ship moved by hand cannot colonize at the destination, and the source can no longer colonize either. Not investigated; where to look is in the `FUTURE_FEATURES.md` entry.
4. **Deferred minors of the final review** (none fixed): line popups show through HUD screens (summary, research, dialogs, notification list); a stale line popup after the board pans under a still pointer; the static
   `FleetSummaryScreen` survives a scene reload if it was open (same pattern as `ResearchScreen`); the drag colour is stale across a turn mid-drag; a drop re-resolves the fleet by id (a reused id or dead ships could send newcomers or survivors);
   dropping back on the origin planet toasts "already there"; rival-line visibility reads the displayed fog view, not the human's own grid (equal while the view is locked); the "Check leaves the dock alone" test compares fleet ids, not ship order.
   Older deferred minors from stage 1 still stand (see `HANDOFF-human-player-2026-10-23.md`, open item 4).
5. **Housekeeping:** `main` is not pushed. The local branches `human-player-mode`, `human-player-ui` and `human-player-ui-stage2` can be deleted. `player_control.md` and the project 1 entry still describe the early exploratory idea.
   A save from before this work shows every planet's "per turn" figure right now (it is projected), but pre-stage-2 saves load all ships in fleet 0, which is one fleet per planet.

## Gotchas

- Unity cannot run from the command line: every check is you running a `[MenuItem]` self-check or a Play run. After any edit read `Editor.log` for `error CS`. Unity recompiles only when its window is focused.
- A Python patch script that **fails during the write truncates the file**: a first attempt on `FleetUIController.cs` raised a `UnicodeEncodeError` on a non-ASCII comment (box-drawing characters) after opening the file for writing,
  and left it empty (restored with `git checkout`). Write the patch script to a file with the Write tool, keep comments ASCII, read and write with `encoding='utf-8'`, and compute everything before opening the file for writing.
  Large bash heredocs containing Python also failed ("unexpected EOF"). Most source files are CRLF; the edit scripts convert to LF and back.
- A self-check fixture's planet type has no per-worker food rate: `Planet.UpdatePlanet` on it divides by zero and throws `OverflowException` (in `SetProjectedWorkers`). Do not call `UpdatePlanet` in a fixture without setting the rates.
  `ProjectProduction` is safe there because the worker requirements skip a zero rate.
- A real `move` reorders the dock list (`Planet.PrioritizeShips`), so dock indices go stale after one; a test that sends a fleet and then names ships by index needs its own fixture. `Check` is a dry run for that reason.
- The planet's collider can win the raycast over a fleet icon (the click code already worked around it with a screen-rect test); anything new that needs pointer events on an icon must go through `PlanetUIObject.IconAt`.
- Unity sends the click before the end of a drag on release, and cancels the click only when the pressed and dragged objects differ; `PlanetUIObject._dragStarted` exists because the planet is now both.
- A queued production item now keeps its `Progress` through a displace-and-resume; anything that adds queue entries with progress other than 0 relies on `Progress += Industry` in `Planet.UpdateProductionQueue`.
- Standing rules: a recommendation with every choice; a yes before code; commit and push only when asked; use they/them when pronouns are unstated; stage files by explicit path (never `git add -A Assets`).
