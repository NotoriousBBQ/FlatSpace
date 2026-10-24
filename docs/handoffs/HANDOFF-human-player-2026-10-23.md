# Handoff: Human player mode (project 1) and the human UI (project 2, stage 1)

Saved from the Claude Code session named **"human_player"**, on 2026-10-23.

Read `CLAUDE.md` first (the "Human player mode" section and its subsection "The human UI (project 2, stage 1)"), then `FUTURE_FEATURES.md`. Specs
`docs/superpowers/specs/2026-10-23-human-player-mode-design.md` and `docs/superpowers/specs/2026-10-23-human-player-ui-design.md`, plans
`docs/superpowers/plans/2026-10-23-human-player-mode.md` and `docs/superpowers/plans/2026-10-23-human-player-ui-stage1.md`.

## Where things stand

**Asked:** a way for a human to play one player against AI rivals: see their planets, set production and research, move, merge and split fleets, with
optional automation of shipments (all) and development (per planet). Then, after trying it, a real UI in place of the temporary JSON command file.

**Done, merged to `main` and pushed:**

- **Project 1 (the rules and a command layer):** one human or none, chosen at match start (a Match Setup panel with a turn limit of 100/200/400/Unlimited, then a
  player picker after the board loads); `PlayerAI.HumanControlled` (from `Player.isHuman`) runs a reduced routine; a pure `HumanCommandProcessor` turns commands
  into the AI's own orders (move, colonize with the food rider, shipResource, production and research queues, development and shipment switches, diplomacy,
  setPause); events and a pause-settings class; defeat; the turn limit; the human's fog view; saves; tuning-log lines; `HumanModeSelfCheck`. A temporary
  `HumanCommands/commands.json` channel (project root, gitignored) drives it until stage 2.
- **Project 2, stage 1 (the UI):** a per-turn notification list (one entry per planet and kind, shortages counted, rivals named "Player N", rival news only
  for planets currently in sight); a red pause banner saying why the timer stopped (clickable to go there); the planet panel as the place to give orders
  (action strip, rates, production queue with Up/Down/Remove and an Add item list, development switch, Ship resources dialog with the AI's surplus figure,
  Colonize, collapsed Improvements built and Ships docked); the research tree screen (HUD button) with chain queueing; every button goes through
  `Gameboard.SubmitHumanCommands`. Pure list builders are in `Assets/Flatspace/Player/Human/` with `HumanUiSelfCheck`.

**Verified, and how:** you ran `HumanModeSelfCheck` (196 assertions), `HumanUiSelfCheck` (140) and `Run All AI Self-Checks` (all 19 suites) after the last change,
and I read `Editor.log` (no compile errors). A whole-branch review by a fresh opus subagent was done for project 1 (1 Critical, 5 Important, all fixed) and for
UI stage 1 (1 Critical, 6 Important, all fixed). You played project 1 with the JSON file on `test2.json` and stage 1 over three Play runs; your last word was
"that's all working as intended".

**Not verified:** the watch-only repeat-run harness after the turn-limit change (it should still play 6 runs per press; nothing ran it); the defeat popup and
the turn-limit popup; a save and load of a human match (human id, queues, pause settings, switches); a human match on a board other than `test2.json` / `4p.json`;
the exact cause of the "Add item closes the planet panel" bug was never found (the fix is a behavioural guard, see Gotchas); the Add item dialog at other
window sizes.

## Files and commits

- `3f5d083` feat: human player mode, project 1. `b27ee9e` feat: human player UI, stage 1. `f219ba1` merge of branch `human-player-ui` into `main`. This handoff is
  committed after it. `origin/main`, `human-player-mode` and `human-player-ui` are pushed (the two branches are kept on purpose and can be deleted).
- Project 1: `Assets/Flatspace/Player/{MatchSettings,Player}.cs`, `Assets/Flatspace/Player/Human/*.cs` (command, processor, fleet view, turn events, pause
  settings, status), `Assets/Flatspace/MainMenu/{HumanPlayerPicker,MatchEndPopup,RandomBoardPopup}.cs`, edits to `PlayerAI.cs` (reduced routine,
  `EmitColonistOrders`, research queue), `GameAI.cs`, `GameBoard.cs`, `Planet.cs` (`ManualDevelopment`, queue validity), `SaveLoadSystem.cs`, `AITuningLogger.cs`,
  `Assets/Editor/HumanModeSelfCheck.cs`.
- UI stage 1: `Assets/Flatspace/Player/Human/{HumanNotificationList,HumanShipmentList,HumanBuildList,HumanPlanetView,HumanResearchTree}.cs`,
  `Assets/Flatspace/UI/HumanUI/*.cs`, edits to `PlanetDetailUI.uxml`, `PlanetDetailUIController.cs`, `MainScreenUI.uxml`, `MainScreenUIController.cs`,
  `NotificationListController.cs`, `GameButtonHandler.cs`, `Assets/Editor/HumanUiSelfCheck.cs`. Docs: `CLAUDE.md`, the `tuning-log` skill, both specs and both plans.
- Not in git: `HumanCommands/` (commands.json and example files for `4p.json` and `test2.json`) and `.superpowers/` (the executor ledgers with every ruling).

## Decisions made and why

- **Two projects, three UI stages (yours):** project 1 first (rules and a JSON command layer so it could be tested), then the UI in stages: (1) notifications, planet
  orders, research tree, colonize; (2) fleet orders and fleet summary; (3) top bar, Esc menu, summaries, diplomacy, planet search, board icons.
- **Yours:** manual colonization as its own Colonize command (the food rider always goes); human planets default to manual development; AI players can declare war
  on the human; fog applied to the human's view; defeat means no populated planet, no colony ship and no colonist in flight; scoring is a separate, later feature;
  the planet panel is layout A (one scrolling panel with an action strip); the shipping dialog shows the AI's surplus figure with the raw stock beside it; Add
  item lists only what can be built now; locked research items are greyed and a click queues the whole prerequisite chain; the notification list is rebuilt each turn
  with no click-to-dismiss; rival notifications name the player ("Player 1: ...") and only cover planets in sight; the timer stops when a colony ship is built, a
  planet's queue is empty or research has nothing queued.
- **Rejected by you:** the JSON file as the way to play ("untenable for a game of any length"); click-to-dismiss on notifications; ever-known planets for rival
  events (you wanted what is visible); tabs for the planet panel.
- **My calls, which you can overrule** (all in the ledgers under `.superpowers/sdd/`): a shortage stops the timer only when one of your planets could ship to it
  (it is listed either way); `CanOrder` needs people or ownership on a planet, but production and the development switch need ownership; UI commands execute
  immediately between turns (`GameAI.ApplyOrdersNow`) while the JSON path applies after combat; `OriginTooSmall` refuses a colonist from a planet with fewer than
  2 of your people; queue validity has no "affordable in industry" gate (the spec text was corrected).
- **Process:** native execution (no per-task subagents) with one final opus review per piece; tests written first, run by you, results read from `Editor.log`.

## Open items and next steps

1. **Stage 2 of the UI** (recommended next): fleet orders (Move, Merge, Split from the fleet panel, clicking a fleet icon, dragging a fleet to a planet), the in-flight
   summary, the fleet summary screen; then remove the JSON reader from `Gameboard` (keep `ApplyJson` for the self-check) and let the JSON path and the UI path share
   one entry point. `HumanCommandProcessor.Check` only covers queueProduction, queueResearch, colonize and shipResource; stage 2 must extend it to `move`.
2. **Stage 3:** the smaller top bar (research per turn as a decimal, no grotsits field), the Esc / system-icon menu with a small Next Turn in the lower right, planet
   summary (owned or known), diplomacy screen (each pair's state, "unknown" without contact), planet search popup, clickable board icons, and a place for the pause
   settings (`setPause` is JSON-only until then).
3. **Tuning issues you noticed, to be handled after stage 3** (you did not say which; ask).
4. **Deferred minors** from the reviews: blockade entries outlive the blockade by the blockade-memory period; after a load blockade and conversion entries are not
   refilled; the research screen is outside the planet panel's click-outside rule; static `ResearchScreen` state across a scene reload; the mouse wheel over scroll
   views also zooms the board; the top bar research readout does not open the research screen; the destination shortage is not shown beside the shipping total;
   the planet panel shrinks fixed-height rows on short screens; a production queue is not cleared when ownership changes through starvation or a landing colonist
   (only conversion clears it); an item dropped inside `ScheduleProductionItem` is not reported; a command with a missing `rival` or `index` reads 0; `RemoveShip`
   can remove another player's colony ship (predates this work); running `HumanModeSelfCheck` in Play mode resets the live match's settings.
5. Housekeeping: delete the local and remote branches `human-player-mode` and `human-player-ui` when you like; `player_control.md` and the `FUTURE_FEATURES.md`
   entry "Human player mode" still describe the early exploratory idea (the entry now points at the specs).

## Gotchas

- Unity cannot run from the command line: every check is you running a `[MenuItem]` self-check or a Play run. **After any edit, read `Editor.log` for `error CS` before
  asking for a run.** One fix pass never compiled (`Planet.GetMaintenanceCost()` was private) and you played the old build without knowing it; Rider's
  inspections cannot see new files (the solution is stale), so they are no help.
- A large bash heredoc of Python failed twice ("unexpected EOF"). Write the patch script to a file with the Write tool and run it with `python3 -I`. `PlayerAI.cs` is CRLF; the
  patch scripts convert to LF and back.
- `HumanModeSelfCheck`'s `Fixture` and helpers are `internal` so `HumanUiSelfCheck` reuses them; a helper named `Ship` shadows the `Ship` type (it was renamed `Shipment`).
- A self-check must never use `Gameboard.Instance`, and methods it calls must be `public` or `internal` within the Editor assembly.
- **The "Add item closes the planet panel" bug:** the cause was never found. `Gameboard.Update` now defers the outside-click close to `LateUpdate` and cancels it when any
  control of the planet panel's document received a pointer press that frame (`PlanetDetailUIController.LastPointerDownFrame`). If a similar click problem returns,
  look there first.
- Do not give a Delayed order a `TimingDelay <= 0` (it is queued and executed at once); `EmitShipOrders`, the colonize and shipResource handlers all clamp.
- `HumanEventKind` values are saved as ints in the pause settings: new kinds are appended last (`ColonyShipBuilt` is last).
- The `HumanCommands/` folder is read only while a human match runs: the first turn step consumes `commands.json`. Keep the 4p and test2 example files named
  `4p-*.json` / `test2-*.json` so they are not read by accident.
- Standing rules: a recommendation with every choice; a yes before code; commit and push only when asked; use they/them when pronouns are unstated; avoid the word
  "slice" for a stage and say "split" only for the fleet command (you asked what both meant).
