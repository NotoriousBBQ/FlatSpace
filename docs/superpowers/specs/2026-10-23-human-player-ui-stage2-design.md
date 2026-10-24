# Human player UI, stage 2: fleets, hover popups, fleet summary

Date 2026-10-23. Follows `2026-10-23-human-player-ui-design.md` (stage 1) and `2026-10-23-human-player-mode-design.md` (project 1). Roadmap entry:
"Human player mode, project 2" in `FUTURE_FEATURES.md` (stage 2: fleet orders and fleet summary, then the JSON file goes away).

## Intent

The human can organise their ships at a planet into several fleets and send any one of them somewhere by dragging it on the board, the way a
strategy game works, and can see what everything in transit is carrying and when it arrives. This replaces the temporary JSON command file as
the way to move ships.

The owner's picture of the flow: split a planet's ships into three fleets at the same planet; drag one fleet to another planet and release,
and the ship-transport orders are created as usual; combine the other two into one fleet; drag that to a different planet.

## Goals

1. **Several fleets per owner at one planet.** Fleets are created, changed and merged in the fleet panel by dragging.
2. **Drag a fleet icon to a planet** to send it. Release creates the usual ship-transport order trio.
3. **Hover popups** on fleet icons (ships by kind and count) and on order lines in flight (ships or resources, destination, turns left).
4. **Fleet summary screen**: one list of the human's fleets, docked and in flight.
5. **The JSON reader is removed** from `Gameboard`; the UI and (for self-checks) `ApplyJson` share one entry point, `SubmitHumanCommands`.

## Non-goals

- Anything in stage 3 (top bar, Esc menu, diplomacy and planet summary screens, planet search, board icons for support screens, the pause-settings screen).
- Fleet names, fleet orders other than "go to a planet" (no patrol, escort or standing orders), auto-merge of arriving fleets, undo.
- Any change to what the AI does. AI players never read a fleet id.
- Tuning issues the owner noticed (after stage 3).

## Anti-goals

- A drag that moves the board, or a click on an icon that starts a tiny accidental drag.
- A refused drop that silently does nothing, or that sends part of a fleet.
- A hover that needs pixel-exact aim on a thin line.

## Decisions (the owner's)

- **Splitting is done by dragging in the fleet panel (option C).** The panel shows the owner's fleets at the planet as boxes side by side plus an
  empty "+ New fleet" box. Drag a ship row to another box to move it; drag a ship row to the "+ New fleet" box to make a fleet; drag a whole box
  onto another box to merge them.
- **Board: one icon per fleet beside the planet (option A)**, each with its ship count; press and drag one icon to a planet. The other icons stay.
- **Hover popup on a fleet icon** lists the number and type of ships. **Hover on an order line in flight** shows the same for a ship order plus
  destination and turns until it arrives; a resource transport line shows resource type, amount, destination and turns to arrival.
- **Rivals' lines get the popup too, with the same full detail**, where both end planets are currently visible to the human.
- **Mouse accuracy** (agreed approach): hover is a screen-space distance test, about 10 px, the nearest line brightens, and when lines overlap the
  popup lists every line within tolerance.
- **An invalid drop** (out of range, no path, not connected, and so on): the fleet stays, nothing is sent, and a short message gives the reason from
  `HumanCommandProcessor`'s existing closed set of refusals.
- **Fleet summary screen**: all the human's fleets, docked and in flight, each with location or destination, turns to arrival and ship mix; a row
  click centres the board on it.

## The design

### Fleets are a label on ships

There is no fleet entity today: `FleetSummary.ForPlanet` groups a planet's docked ships by owner (bunkers excluded), and `Ship` has no fleet field.

- Add `Ship.FleetId` (int, default 0). A fleet at a planet is the owner's non-bunker ships sharing a `FleetId`. Numbering shown to the player is by
  order of `FleetId` at that planet ("Fleet 1", "Fleet 2"); no stored names.
- `FleetSummary.ForPlanet` groups by (owner, `FleetId`). Rivals and the AI never set a fleet id, so their single group per planet is unchanged.
  `Group` gains `FleetId`; the icon rules stay.
- Saves: `GameSave.PlanetSave.dockedShips` entries gain `fleetId`; an older save has none and reads 0, so false/0 means "as before" and the old
  grouping results.
- **Arrival (my call):** ships arriving by an order dock with a fresh fleet id unused at that planet for their owner, so a fleet that arrives stays
  one fleet and does not silently merge with ships already waiting. The AI's owners are unaffected (they have one group either way, because the
  group key ignores the id for non-human owners, or because the builder gives AI arrivals id 0, whichever is simpler and keeps their behaviour
  identical; builder's choice).

### Commands (flat `HumanCommand`, handled by `HumanCommandProcessor`)

- **`move`** already takes `origin`, `target` and `ships` (dock indices). A fleet drag builds a `move` from the dock indices of that fleet's ships.
  `Check` is extended to `move` (and `CheckBatch`), so the drag can show valid and invalid targets while hovering, with the same reasons as Apply.
- **New `setFleet`** (instant, creates no orders): `origin`, `ships` (dock indices), `index` = the fleet id to put them in, or a negative value for
  "a new fleet". Refuses ships that are not the human's, are bunkers, or are claimed by an earlier command in the batch.
- **New `mergeFleets`** (instant): `origin`, `index` = fleet id to merge from, `toIndex` = fleet id to merge into.
- Refusals use the existing closed-set style (new reasons for an unknown fleet and a bad ship index); nothing throws.
- Splitting and merging go through `Gameboard.SubmitHumanCommands` like every other panel action, so the board and panels refresh. They do not
  consume or advance a turn.
- New command types are appended; `OrderType` is not touched (no new order is needed).

### Fleet panel (inside the existing `FleetUIController` / planet panel)

`FleetUIController.RefreshShipList` is replaced for the human's own planets by fleet boxes as described in the decisions. For a rival's ships or a
non-human match it keeps the current read-only list (`FormatShipRow`). Ship rows keep their effective stats text. Dragging uses UI Toolkit pointer
events and respects `PlanetDetailUIController`'s click-inside rule (`ShowDialog` / `ContainsScreenPoint`) so a drag ending in the panel is not read
as an outside click.

### Fleet icons and the drag

`PlanetUIObject` builds one `FleetIconView` per group today and opens the fleet panel on a click. Changes:

- One icon per (owner, fleet id); icons keep the owner colour, sprite rule and count plate.
- For the human's own icons: a press followed by movement beyond a small threshold (a few pixels) becomes a drag; below it, a click opens the panel
  as before. The drag draws a line from the icon to the pointer, highlights a planet under the pointer that `Check` accepts and marks one it
  refuses, and on release sends the whole fleet (all its ships) or shows the refusal message. The board must not pan or select during the drag
  (the `Gameboard` is a `RectTransform` inside a `ScrollRect`; the builder resolves this).
- Rival icons have no drag; they still have the hover popup.

### Hover popups

Neither popup exists yet (a check of the code found no hover handling for icons or lines), so both are built here, sharing one popup element on the
HUD document and one set of **pure text builders** (self-checkable without a UI):

- Fleet icon: ships grouped by kind with counts (for example "3 Warship, 1 Siegeship"); damaged ships noted as in the panel (builder decides how).
- Ship order line: the same ship text, plus destination planet and turns left (`GameAIOrder.TimingDelay`), from the `GameAIOrder.Fleet` payload.
- Resource line: resource type, amount (`Data`), destination, turns left. Population and food-rider orders that draw lines are covered by whichever
  of these fits; the builder lists the order types that have a line and gives each a sentence.
- Several lines in tolerance: one popup, one block per line.
- Lines are `LineRenderer`s with no colliders. Hover is a distance test of the pointer (screen pixels) against each line's segments, done once per
  frame only while the pointer moved, over the order lines the presentation layer already holds.

**Visibility:** the human sees their own lines always. A rival's line shows its popup only when both end planets are currently live-visible in the
human's fog (the human's fog view; with fog at `NoFog` everything counts as visible).

### Fleet summary screen

A screen opened from the HUD (the stage-1 `HumanUiKit` dark theme, like `ResearchScreen`), built from a pure list builder in
`Assets/Flatspace/Player/Human/` (extending `HumanFleetView.InFlight`, which today gives kind, count, origin, target and turns left): one row per
docked fleet (planet, ships by kind) and per fleet in flight (origin to destination, turns left, ships by kind). A click centres the board on the
planet (docked) or on the destination (in flight) via the existing `GoToNotification`-style helper. The existing `FleetSummary` (per-planet
grouping) is not this screen.

### JSON path

Remove the `commands.json` read from `Gameboard` (`RunHumanCommands`' file step). `HumanCommandFile` may stay or go (builder's call), but
`HumanCommandProcessor.ApplyJson` stays, because the self-checks use it. Keep the `HumanCommands/` example files out of the repo as now.
`CLAUDE.md` "Human player mode" is updated to say the file channel is gone.

## Calls made after the builder check (mine, for the owner to overrule)

- **Fleet ids:** per owner per planet; a new fleet gets the highest id at that planet plus one; id 0 is an ordinary fleet (all ships start there, and old
  saves load there); an emptied fleet's id vanishes with it and may be reused. Ids mean nothing across planets: ships that land by an order get a
  fresh id at the destination (above), so a fleet's id is never carried along.
- **Newly built ships** (and ships already docked on a planet that changes hands) join the owner's lowest-numbered fleet at that planet, or fleet 0 if
  there is none. Only ships arriving by an order form a new fleet.
- **Grouping key:** `FleetSummary.ForPlanet` groups by fleet id only for the human's ships; every other owner stays one group per planet.
- **A fleet drag is all or nothing:** if any ship of the fleet cannot go (the target is out of range or unreachable), the whole drop is refused.
- **Drag and drop layers:** the drag starts from the uGUI icon (`PlanetUIObject`); the drop planet is found with the same `EventSystem.RaycastAll`
  check `Gameboard.Update()` uses. Dropping an icon onto the fleet panel is out of scope. The panel's own drags (ship to box, box to box) are UI
  Toolkit and stay inside the panel.
- **`Check` during a drag:** only the planet under the pointer is tested, and only when the pointer moves onto a different planet, never every frame;
  `Check` already runs on a scratch processor and order list.
- **Fog test:** a planet is live-visible when the human's own `VisibilityGrid` strength at its cell is above zero, read from the human's grid whatever
  the displayed view is (the human match locks the view to them anyway).
- **Bad ids:** `mergeFleets` into itself or from an empty or unknown fleet is refused; `setFleet` with a non-negative id that does not exist at the
  planet is refused (only a negative index makes a new fleet).
- **`results.json`:** the file step goes with `commands.json`; the result list stays in memory as the return value of `Apply`/`ApplyJson`, and a UI
  refusal is shown as a message and logged as `HumanCommandRefused` as now.
- **Order-to-line link:** the builder checks whether `LineDrawObject` keeps its `GameAIOrder`; if not, it adds that reference (a presentation change only).

## Constraints and gotchas (from earlier work)

- Do not give a Delayed order `TimingDelay <= 0`; `EmitShipOrders` clamps.
- `Planet.PrioritizeShips` reorders the real dock list, so a command names ships by the dock list as it stood when the batch began.
- New `HumanEventKind` and `OrderType` values are appended last. This stage needs no new order type.
- The "Add item closes the planet panel" behavioural guard (`LastPointerDownFrame`, deferred outside-click close in `LateUpdate`) must keep working
  with the new dialogs and drag handling.
- A self-check never uses `Gameboard.Instance`; helpers it calls are `public` or `internal` within the Editor assembly.
- Stage new scripts with their `.meta` files; stage by path.

## Tuning log

No new tuning-log lines are proposed, because this stage changes how a human gives orders, not what the AI decides. The existing
`HumanCommand` / `HumanCommandRefused` lines already record every applied or refused command; the builder confirms they log the two new command
types with their fields and, if not, adds the fields and the matching line in the `tuning-log` skill and `CLAUDE.md`'s "AI Tuning Log" section.

## Verification

- Extend `HumanModeSelfCheck` (commands: `setFleet`, `mergeFleets`, `move` through `Check`, a fleet-id save round trip, arrival fleet ids, older
  save reads 0) and `HumanUiSelfCheck` (fleet grouping, popup text builders, summary list, rival-line visibility rule, hover distance test). Both stay
  in `Run All AI Self-Checks`.
- Play-mode look, owner to confirm: split into three, drag one, merge two, drag; refused drop; hover popups on icons and on own and rival lines; the
  fleet summary; a save and load of a match with several fleets; the board does not pan during a drag.

## Left to the builder

Popup visuals and placement, drag threshold and exact pixel tolerance, message styling for refusals, how damaged ships read in the popup, which
order types draw lines, how arrivals for non-human owners keep id 0, and whether `HumanCommandFile` stays.
