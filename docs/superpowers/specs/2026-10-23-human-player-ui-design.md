# Human player mode, project 2: the UI

Status: draft for approval, 2026-10-23. Source: brainstorming session with the owner, after project 1 was built and committed
(`3f5d083`; design `2026-10-23-human-player-mode-design.md`, plan `2026-10-23-human-player-mode.md`). Roadmap entry: "Human player
mode, project 2: UI rework" in `FUTURE_FEATURES.md`.

Project 1 built the rules and a command layer driven by a temporary JSON file. The owner's verdict after playing it: the JSON approach
is untenable for a game of any length. This project gives the human a real interface to the same command layer.

## Intent and why

Playing a long game means a loop the owner described exactly: **the timer stops on something, the human clicks the notification to see
where it is, the planet UI opens, and they give orders there in response.** Everything in stage 1 serves that loop. Stages 2 and 3 add
fleet control and then rework the rest of the screen (top bar, menus, summary screens) around a board of clickable icons that open
support screens.

## Goals

1. A human can play a whole match without touching a file: every command type project 1 supports is reachable from the UI.
2. The pause-notification-planet-orders loop above is quick: one click from a notification to the right place, one more click or
   two to give the order.
3. The UI only ever calls the existing command layer (`HumanCommandProcessor` and the orders it emits). No new order types, no rules in
   the UI. Where a list or button needs to know whether a command would be accepted, it asks the processor (see "Calls I made", 1)
   instead of copying the rules.
4. Zero-human matches look and behave as they do now (the new screens and the notification list exist only while a human plays).

## Non-goals and anti-goals

- No scoring, no victory screen, no second human, no keyboard shortcut design, no redirect or recall of anything in flight.
- No change to the rules of project 1 except those named under "Changes to project 1" below.
- Anti-goal: a UI that lets the human do something the command layer would refuse, or that shows a figure the AI does not use for the
  same decision (the shipping surplus is the AI's own figure, see Stage 1).

## Existing pieces to build on (UI Toolkit)

`MainScreenUIController` (top bar and notification host), `NotificationListController`, `PlanetDetailUIController` (the planet panel),
`FleetUIController` (the fleet panel, `Gameboard.ShowFleetUI`), `GameButtonHandler` (the buttons and the debug fog dropdown) and
`FleetSummary` (per-planet fleet grouping with icons). `Gameboard.ViewPlanet(name)` centres the board on a planet. Panels are `.uxml` plus
a controller. Prefer extending these over new frameworks.

## Stage 1: notifications, planet orders, research, colonize

### Notification list (human player only)

- One entry per **(planet, kind)**, for example "Food shortage: Farm 5". An entry that persists shows how many turns it has lasted and
  updates its count; it clears itself when its cause ends, and it clears when the human **clicks** it.
- **Clicking an entry** centres the board on its planet (`ViewPlanet`) and opens the planet UI for it. Entries with no planet (first
  contact, a war declared, research complete) are shown and dismissed by click but have no location.
- The entries are the event kinds project 1 produces (`HumanEventKind`), shown to the human as listed in project 1's "Messages" section:
  the pause events, population gained or lost, fleet and ship arrival, planet colonized, production complete, production queue empty,
  food and grotsits shortfalls, transport orders only while shipments are automated, shipments lost to a blockade, a planet blockaded,
  conversion begun and ended, an AI declaring war. Rival events (`HumanEvent.Rival`) are shown for visible planets. New research and new
  production starts are not shown.

### Planet UI as the place to give orders (layout A, chosen by the owner)

One scrolling panel with collapsible sections and an **action strip at the top** that is always visible:

- Action strip: **Ship resources**, **Colonize**, **Fleet** (opens the existing fleet panel; its orders arrive in stage 2) and the
  **development switch** (Manual or Automatic, `setDevelopment`).
- Stock and per-turn rate of food, grotsits, industry and research, so the cause of a shortage is visible.
- **Production**: the item in progress with its progress, the queue with Up, Down and Remove per entry, and **+ Add item**
  (`queueProduction`, `removeProduction`, `moveProduction`).
- **Improvements built**: collapsed when the panel opens, expandable, lists `Planet.CompletedImprovements`.
- **Ships docked**: collapsed, grouped by kind with damaged ships flagged (`HumanFleetView.Docked`).

### + Add item

A list of what can be built **now**: unlocked, not already built, not superseded, under the bunker cap (the project 1 queue-validity
rules, `Planet.IsProductionItemValid` plus the bunker cap). Grouped by type (improvements, ships, defenses). Each row shows its cost and
an estimate of turns at the planet's current industry; a click adds it to the queue. Locked items are not shown.

### Ship resources dialog (the owner's flow)

Opened from the planet UI of the **destination** planet:
1. Choose the resource: food or grotsits.
2. A list of the human's planets within the resource-distribution node limit (`maxPathNodesForResourceDistribution`) that have a surplus.
   Each row shows the planet's **AI surplus figure** (what the planet reports as spare after its own needs, the figure
   `ProcessResourceShipments` ships from: the `...Surplus` update result scaled by `GetPopulationFraction`), its **raw stock** beside
   it, and the distance in hops.
3. The human ticks one or more planets and sets an amount for each. The default is the surplus. They may type up to the raw stock; an
   amount above the surplus shows a warning.
4. A total is shown against the destination's shortage.
5. **Commit** sends one `shipResource` command per ticked source in a single batch (so the processor's stock accounting stops two
   shipments overdrawing one planet). Cancel closes the dialog.

### Colonize

The action-strip button lists the known planets in range that are valid colonization targets for this planet (the `colonize` command's
rules), with turns to arrive; the human picks one and commits. The food rider is not mentioned: it always goes. The button is enabled
only when a colony ship is docked here and the planet has at least two inhabitants.

### Research screen (reachable from an icon on the main screen, and from the top bar's research readout)

- The **whole research catalog as a tree**, drawn by prerequisite (`requiredTech`): researched items **checked off**, items whose
  prerequisite is not yet researched **greyed out**, queued items showing their queue position.
- Click an available item: add it to the queue (`queueResearch`).
- Click a greyed item: after a short confirmation ("Queue 4 items?"), queue the whole chain of missing prerequisites ahead of it, in
  order.
- Click a queued item: remove it (`removeResearch`).
- The current research and the queue are shown with turns to completion.

### Changes to project 1 (needed by stage 1)

- `ShortageFood` and `ShortageGrotsits` pause events fire when a shortage **starts** (a planet newly short), not on every turn it
  continues. The notification list shows the ongoing shortage as one entry with a turn count. `HumanTurnEvents` keeps the state
  (`PlanetAttacked` stays per fight turn unless the builder finds it equally noisy; the owner has not decided that).
- The notification list needs "ongoing" state, so `HumanTurnEvents` (or a new list builder over it) exposes, per turn, which
  (planet, kind) pairs are still true.

## Stage 2: fleet orders and fleet summary

- Click a **fleet icon**, on the map or in the planet UI, to open the fleet panel with **Move, Merge and Split**. Split shows each
  ship's condition and flags damaged ships (`HumanFleetView.Docked`). Merge needs no command: ships arriving at one planet join.
- **Drag** from a fleet icon to a planet to send that whole fleet there. Valid targets are known planets within the ship-transport
  node limit; anything else is refused with a short message.
- Orders are committed once sent. Clicking a fleet or a shipment **in flight** opens a read-only summary: contents, origin,
  destination, turns to arrival (`HumanFleetView.InFlight`).
- **Fleet summary screen**: each fleet, ship count, location or destination, turns until arrival.
- When this stage lands the game no longer reads `HumanCommands/commands.json`; `HumanCommandProcessor.ApplyJson` stays for the
  self-check. *(Designer's call; the owner said only that the JSON approach is untenable.)*

## Stage 3: global rework, summaries, diplomacy

- **Top bar**: smaller; the grotsits field removed; **research per turn as a decimal** (not rounded to an integer); the current
  research and turns to completion.
- **Game buttons** hidden except **Next Turn**, which is smaller and alone in the lower right. The rest appear when Esc is pressed or
  from a **system icon**; the menu has **Exit Game** and **Exit to Main Menu**.
- **Clickable icons on the board** that open the support screens: research, diplomacy, planet summary, fleet summary, system.
- **Planet summary**: planet, planet type, current and maximum population, net gain or loss of each resource, current production item
  and turns to completion; a switch between **owned** planets and **known** planets.
- **Diplomacy screen**: the stance controls (declare war, make peace, surrender; `declareWar`, `makePeace`, `surrender`) and each
  opponent's status, including **each player's diplomatic state with each other player**, shown as **"unknown"** for any pair that has
  had no contact.
- **Planet search popup**: find a planet by name and go to it (`ViewPlanet`).

## Calls I made (the owner can overrule any of these)

These came from a reviewer's questions; each is settled here so the plan does not guess.

1. **Asking whether a command would be accepted.** The processor today only applies a batch. Stage 1 adds a small extension to the
   command layer, `HumanCommandProcessor.Check(command)`, returning the refusal reason or none, by running the same handler against a
   scratch order list and without keeping its claims. The dialogs and lists pre-filter with it (owned, in range, in stock, valid
   target, valid item), and whatever the processor still refuses at commit is shown to the human from the result. No rule is duplicated
   in the UI, and `Check` adds no rule to project 1.
2. **The shipping surplus.** The dialog reads the **last turn's update results** (kept on `GameAI`, as `LastTurnResults` already is) for
   the `FoodSurplus` / `GrotsitsSurplus` entries of the human's planets, scaled by the planet's population fraction for the human. A
   planet with no such result this turn is not listed. Because `Planet.GetPopulationFraction` touches `Gameboard.Instance`, the list is
   built by a pure class that takes the results and a population-fraction function as inputs. `ProcessResourceShipments` is not
   changed.
3. **Notification state.** State-based kinds are shortage (food, grotsits), planet blockaded, conversion (begun until ended), and
   production queue empty on a planet; each is one entry per (planet, kind) that lives while true. Everything else is one-shot and
   stays until clicked, with the oldest dropped past a cap (builder's number). A click dismisses an entry for that **episode**: it
   returns only if the cause ends and later happens again. The list is **not saved**: a load starts with it empty and the first turn
   after refills the state-based entries.
4. **Controls during stage 1.** The existing Next Turn and run/stop buttons stay as they are. The pause settings use project 1's
   defaults and are changed with `setPause` through the JSON file until stage 3 gives them a place in the Esc menu. The JSON channel
   stays active until stage 2 lands.
5. **Panels and dialogs.** The Ship resources dialog, the Colonize list, Add item and the research confirmation count as part of the
   planet panel for `Gameboard.Update()`'s click-outside rule (`ContainsScreenPoint` covers every open element), and the planet panel
   stays open while one of them is open. Clicking a notification while the panel is open switches it to the new planet.
6. **The research catalog.** Every research item has at most one `requiredTech`, so a greyed item's missing chain is the list found by
   following `requiredTech` until an item that is researched or queued; the confirmation shows its length. `queueResearch` already
   accepts an item whose prerequisite is queued earlier in the same batch.
7. **Turn estimates.** The in-progress item's estimate uses the current rate; each queued item's estimate is cumulative after those
   before it. A rate of zero shows no estimate ("no progress"). The research queue is estimated the same way.
8. **"Visible" for rival events.** Visible means **known** (`PlayerKnowledge`), exactly as project 1's `HumanTurnEvents` decides, so a
   self-check needs no fog system. With the debug fog view locked to the human, the board and the notifications agree.

## Constraints

- UI Toolkit for new screens, matching the existing panels; the setup popups from project 1 stay uGUI.
- Follow `CLAUDE.md` conventions. UI logic that can be pure (the notification list's (planet, kind) state, the surplus list for the
  shipping dialog, the research tree layout, the add-item list) lives in plain classes with a self-check under `Assets/Editor/`, never
  using `Gameboard.Instance`. What cannot be self-checked (layout, clicks, camera centring) is verified in a Play run and listed as
  such in the plan.
- Every stage adds its tuning-log lines (for example `HumanUiOrder|<screen>|<commands sent>`) or states why none are needed, and
  updates the `tuning-log` skill and `CLAUDE.md`.
- The surplus and distance figures shown must come from the same data the AI uses, so the UI and the AI cannot disagree.

## Left to the builder

- The visual design, the exact UXML layout and styling, how the tree is drawn (indented list or a drawn graph), how the dialog and
  the list popups are built, where the pause-event settings (`setPause`) and the stepping or timer controls live (the owner has not
  said; a section in the Esc menu is the designer's guess), and the order of work inside a stage.
