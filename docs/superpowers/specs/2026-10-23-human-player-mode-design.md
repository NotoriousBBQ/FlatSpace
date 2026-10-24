# Human player mode: design

Status: draft for approval, 2026-10-23. Source: brainstorming session with the owner. Roadmap entry: "Human player mode" in
`FUTURE_FEATURES.md`; background in `player_control.md` (exploratory notes on externally controlled players, which this
design partly adopts: one command layer, a file-based interface first).

This is **project 1 of 2**. Project 2, the UI rework (global screen, new support screens), is listed at the end and gets
its own design after this one is built. Project 1 delivers the control rules and a temporary JSON command interface so it
can be tested before any of the new UI exists.

## Intent and why

Today every player is AI-driven and the game only runs the simulation forward. The owner wants to play one player
themselves against AI rivals: see their own planets, direct production and research, and move, merge and split fleets,
while being able to hand routine work (shipments, planet development) back to the AI. The same command layer is intended
to serve a later UI and, if ever wanted, an externally driven player (see `player_control.md`).

## Goals

1. A match can have **zero or one human player**, chosen at match start. Zero is today's watch-only behaviour and must stay
   unchanged (including reproducible tuning-log runs).
2. The human player gives orders through one **command layer** that reuses the existing order machinery
   (`GameAI.GameAIOrder` trios, `PlayerAI` emission helpers), so no one derives delays or deduction/in-progress order pairs by hand.
3. Partial automation the human can turn on and off: all shipments (one player-wide switch), and planetary development
   (a switch per planet).
4. A turn flow that can step one turn at a time or run on the timer, with the timer **pausing itself on chosen events**.
5. The human's view uses the existing **fog of war** and `PlayerKnowledge`.
6. Defeat detection and a match turn limit.
7. A **temporary JSON command interface** so everything above can be driven and tested with no new UI.

## Non-goals and anti-goals

- No UI rework here (project 2). Existing HUD, planet detail panel and fleet panel stay as they are, except where a
  setup popup, a picker or a pause setting needs a minimal control.
- No more than one human; no hot-seat, no per-player hidden views for several humans.
- No victory condition and no scoring. Reaching the turn limit ends the match with no winner. Scoring is a separate,
  lower-priority roadmap item the owner does not want started yet.
- No redirect or recall of anything in flight. Orders are committed once sent.
- No research automation switch. No colonization automation.
- No change to AI behaviour for AI players. **Anti-goal:** any change that alters a zero-human match's simulation, since
  tuning-log comparisons depend on it.

## Rules the owner decided

### Setup

- A **first popup**, shown before the board-selection popup, asks: Human player (yes or no) and a **turn limit** from the
  presets 100, 200, 400 and Unlimited (default 400). The limit applies to watch-only matches too.
- After the board loads, if a human was chosen, a **picker lists that board's players with their home planets** and the
  human picks one. (The picker comes after the board because the player count is only known then.) The human cannot be
  changed mid-match.
- The choice must reach `Gameboard.Awake()` the way other pre-scene values do (see the `DontDestroyOnLoad` note in
  `CLAUDE.md`); the human player id and turn limit are saved and restored with the game.

### Visibility

- The human's view applies **fog of war** and uses `PlayerKnowledge` for known planets. Unexplored areas are dark; rival
  planets show what the human has learned. The existing fog debug dropdown is replaced by the human's view while a human
  is playing (it remains for zero-human matches).

### Fleets

- Ships are directed as fleets (the groups of docked ships at a planet, and ships in flight).
- A fleet is all of the human's docked ships at a planet (shown grouped by kind). It can be **moved, merged and split**.
  Merging happens by docking at the same planet; splitting is sending fewer than all ships. **The split view and command show
  each ship's condition and flag damaged ships** (owner-decided), so the human can see which ships they are sending or keeping;
  ships are therefore individually identifiable in the command schema. *(Designer's call: the human picks specific ships, with
  a count per kind as a shortcut that takes the healthiest first.)* A fleet order is the existing ship-transport trio (`OrderTypeShipTransport`, `OrderTypeShipDeparture`,
  `OrderTypeShipTransferInProgress`) with the existing clamp of at least one turn of delay.
- **Valid targets:** any **known** planet reachable within the existing ship-transport node limit
  (`maxPathNodesForShipTransport`). Others are refused with a short message.
- **Colonization is manual, through a separate Colonize command** (owner-decided). It names a planet that holds a colony ship and
  a known target, and emits the existing four colonization orders (`OrderTypePopulationTransport`, `OrderTypePopulationChange`,
  `OrderTypePopulationTransferInProgress`, `OrderTypeRemoveShip`), reusing `PlayerAI.ProcessColonizers`' emission code. The
  **food rider is applied on every human use**: when `Planet.NeedsColonyFoodRider` is true for the target, the command emits the
  rider pair (`OrderTypeColonyFoodRider` plus the `OrderTypeFoodChange` deduction) automatically, and the command is refused
  unless `PlayerAI.CanSupportColony(origin, target)` holds, exactly as for the AI. Colony ships are otherwise moved like any
  other ship; there is no colonization automation. The "colony ship ready" pause event means a colony ship is docked at a
  planet and a viable target exists.
- Orders are **committed once sent**; nothing in flight can be redirected or cancelled.
- Interaction (to be built in project 2's UI, but defined here so the command layer matches): clicking a fleet icon, on the
  map or in the planet details UI, opens the fleet panel with Move, Merge and Split; dragging from a fleet icon to a
  planet sends that fleet entire. *(Owner-confirmed rule; the drag source being the fleet icon is the designer's reading.)*
  Clicking a fleet or a shipment **in flight** opens a read-only summary (contents, origin, destination, turns to arrival).
  Until project 2 exists, the JSON interface supplies these commands.

### Production and research

- **Queue validity (designer's call):** the human gets the same hard rules as the AI: the item must be unlocked, not
  superseded, and affordable in industry. They are checked when the item is queued and again when it is built; an item that is
  no longer valid at build time is dropped from the queue with a message. (Correction while building: there is no "affordable in industry" gate,
  since industry is spent as build progress; the validity rules are unlocked, not already built, not superseded, and the bunker cap.) The AI's grotsits-affordability rule
  (`Planet.CanAffordImprovement`) is a preference, not a hard rule, and does not bind the human. A planet that changes owner
  has its queue cleared.
- **Production is a per-planet queue.** When a planet's development automation is off, the human fills its queue. When the
  queue empties the planet **waits** (industry kept) and may raise a "planet idle" pause event.
- **Research is a single player-wide queue**, manual only, with no automation switch. An empty queue stalls research and may
  raise a "research idle" pause event.
- The planet UI gains a section **"Improvements built"**, collapsed by default and expandable, listing the planet's
  completed improvements (`Planet.CompletedImprovements`).

### Automation switches

- **Shipments:** one player-wide switch. On means `PlayerAI`'s resource-shipment logic, including distribution-center
  selection, runs for the human's player as it does for an AI player, with the same inputs the AI uses (designer's call); off
  means the human orders shipments.
- **Planetary development:** a switch per planet. On means the AI chooses what that planet builds (the existing industry
  choice for that planet only); off means the human's queue is used.
- Everything else for the human's player (fleet moves, colonization, research, diplomacy) is manual and never run by `PlayerAI`.

### Diplomacy

- The human **declares war, makes peace and surrenders** through the existing immediate stance orders
  (`OrderTypeDeclareWar`, `OrderTypeMakePeace`, `OrderTypeSurrender`), with the same effects they have for AI players. The
  human's own decisions are manual: `PlayerAI` never issues these for the human. **AI players can still declare war on the
  human under the same rules they use against each other** (owner-accepted recommendation), so the human is not immune, and
  "war declared" raises the pause event when an AI declares it on the human.

### Turn flow and pausing

- The human can **switch between stepping one turn at a time** (the existing `Gameboard.SingleUpdate()` path) **and the timer**
  (the existing `TimedUpdate` coroutine) at any time.
- In timer mode the game **pauses itself on selected events**. Default **on:** planet attacked or blockaded, planet lost,
  first contact, war declared or peace made, fleet arrived, shortage (food or grotsits) on a human planet. Available,
  default **off:** production complete, research complete, colony ship ready, planet idle, research idle.
  Each event is individually toggleable; the settings are saved.

### Messages (for the human; defined here, displayed in project 2)

The event set must be producible as discrete events by the simulation or the command layer (project 1 supplies the events;
project 2 lays out the message queue). The set is: the pause-event list above; population added or lost; fleet or ship arrival;
planet colonized; production complete; production queue empty; food or grotsits shortfall; transport events (only while
shipments are automated and on); and, added by the owner, **an AI player declares war** (on anyone the human can see, or on the
human), **a food or grotsits shipment lost to a blockade**, **a planet blockaded by an enemy**, **planetary conversion begins**
and **planetary conversion ends**, each for any planet visible to the human. Rival events of the same kinds appear only for
visible planets. New research and new production are not messages.

### Match end

- **Defeat:** the human has no populated planets, no colony ship docked anywhere, and no colonist in flight. A popup says so; the human may keep watching
  the AIs finish.
- **Turn limit:** at the limit the match stops with a "turn limit reached" screen and no winner.

## Temporary JSON command interface

The command layer takes **commands** (move fleet, split, merge, queue production on a planet, remove or reorder queue
entries, queue research, set the shipments switch, set a planet's development switch, declare war, make peace, surrender,
set pause-event toggles). A **commands file** in the project's data folder (same convention as `AITuningLogs/`, outside
`Assets/`) is read **each time a turn steps**; each command is applied through the command layer and reported (applied or
refused with a reason) to a result file or the log. It is a test and development tool: the later UI calls the same command
layer instead. The exact schema is the builder's to define. A command refused by the rules (unknown planet, beyond the
node limit, not the human's ships, empty queue entry unaffordable ...) must refuse cleanly and never throw.

## Constraints

- Follow `CLAUDE.md` conventions: self-checks under `Assets/Editor/` (never using `Gameboard.Instance`; methods the checks
  call must be `public`), new scripts committed with their `.meta`, `OrderType` values appended last, namespace casing of
  the folder you edit, saves via `JsonUtility` with older saves still loading.
- **Saved with the game (designer's call):** human player id, turn limit, the shipments switch, every planet's development
  switch, production queues, the research queue and the pause-event settings. Older saves load as zero-human matches.
- Fog of war is presentation-only today; the human's view must not make the simulation depend on it. (Whether commands
  themselves are validated against `PlayerKnowledge` is required only for "known planet" targets above.)
- Every new feature adds its tuning-log lines (human commands applied/refused, pause events fired, defeat, turn limit) and
  updates the `tuning-log` skill and the "AI Tuning Log" section of `CLAUDE.md`, or states why none are needed.
- **Plan phasing** (one design, built in phases): (1) setup popups, human id and turn limit, saves; (2) command layer and JSON
  interface for fleets; (3) production and research queues plus the improvements list data; (4) automation switches;
  (5) diplomacy commands; (6) fog view for the human and defeat; (7) pause events and timer/step switching.

## Left to the builder

- The command schema and file location; how a human player is excluded from `PlayerAI`'s per-turn dispatch while the
  shipment and planet-development paths still run on request; how the production queue maps onto the existing
  one-item-per-turn build and affordability rules; how pause events are emitted from the existing `UpdateResult` and order
  flow; the minimal controls for the setup popup, picker and pause settings.

## Project 2 (separate design, after this one)

The UI rework: a board of clickable icons that open support screens. Top bar smaller, grotsits field removed, showing
**per-turn research as a decimal** and the current research with turns to completion. Game buttons hidden unless Esc or a
system icon is pressed (Exit Game, Exit to Main Menu); Next Turn stays visible, smaller, alone in the lower right. Message
queue for the human: shown are the pause-event messages, population added or lost, fleet or ship arrival, planet colonized,
production complete, production queue empty, food or grotsits shortfalls, and transport only when shipments are automated and
on; hidden are new research and new production; rival events appear only on visible planets. New screens: research (from an
icon), **diplomacy** (stance controls plus each player's diplomatic state with each other player, "unknown" when they have had
no contact), **planet summary** (planet, type, current and max population, net resource gain or loss, current production
item and turns to completion, with a switch between owned and known planets), **fleet summary** (each fleet, ship count,
location or destination, turns to arrival), a **planet search popup**, and the fleet-click/drag interaction and in-flight
summary defined above.

## Roadmap note

Scoring (victory by score at the turn limit) is recorded in `FUTURE_FEATURES.md` as a lower-priority item, not started.
