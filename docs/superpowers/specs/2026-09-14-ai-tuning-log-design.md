# AI Tuning Log — Design

Status: draft, awaiting user review before implementation planning.

## Problem

There is no way to review what the AI actually did across a match after the fact. The in-game
notification panel (`Gameboard._playerNotifications`) shows some events as they happen, but it is
transient (cleared every turn), UI-only, and filtered to one player whenever the fog debug view is
locked to `Player n`. Tuning AI behavior (shipment timing, colonization pacing, research/production
choices) requires a durable, complete record of what happened, turn by turn, across every player,
that both a human and Claude can read back afterward.

## Load-bearing facts about the current code

- **The turn loop already funnels almost every AI action through two choke points.**
  `GameAI.GameAIUpdate()` calls `Gameboard.Instance.CreateNotificationsForNewOrders(gameAIOrders)`
  (orders freshly created this turn) and, from `ProcessCurrentOrders()`,
  `Gameboard.Instance.CreateNotificationsForExecutingOrders(executableOrders)` (orders arriving/
  completing this turn). Both already contain a `switch (order.Type)` covering
  `OrderTypeFoodTransport`, `OrderTypeGrotsitsTransport`, `OrderTypePopulationTransport`, and (new-orders
  only) `OrderTypeIndustrySetProduction` — everything else falls through a `default: break;` and
  produces no notification today.
- **Research start/completion are notified from a different call site.** `PlayerAI.cs` calls
  `Gameboard.Instance.CreateNotificationsForNewResearch(...)` and
  `...CreateNotificationsForCompletedResearch(...)` directly (not order-based), each just before/after
  a research item changes.
- **Two events are produced but never notified at all today:**
  `Planet.PlanetUpdateResult.PlanetUpdateResultType.PlanetUpdateResultTypeIndustryProductionComplete`
  and `...PlanetUpdateResultTypeColonizerReady`. Both are `PlanetUpdateResult`s appended to the
  `planetUpdateResults` list `GameAI.GameAIUpdate()` builds via `UpdateAllPlanets(planetUpdateResults)`
  (`GameAI.cs`, right before `GameAIMap.Knowledge.Update(...)`), then consumed only internally by
  `PlayerAI.BuildIndustryMatrix` / `ProcessColonizers` — nothing surfaces them to a human or a log.
- **`Gameboard.Instance.TurnNumber` is incremented in `SingleUpdate()`** (`GameAI.GameAIUpdate();
  TurnNumber++;`), *after* `GameAIUpdate()` — and therefore after every notification call site above —
  returns. So every existing notification call site (and therefore every log call site this design
  adds) reads `TurnNumber` at the same relative point: the value *before* this turn's increment.
- **`SaveLoadSystem` already has an established, duplicated-per-call-site path convention** for a
  project-specific folder: `Application.dataPath + "/Flatspace/<FolderName>"` in the editor,
  `Application.persistentDataPath + "/<FolderName>"` in a build, picked via `#if UNITY_EDITOR`. No
  shared path-helper method exists; each call site repeats the two lines. This design follows the same
  pattern for its own folder rather than introducing a new abstraction.
- **The fog-of-war debug view established the "opt-in, off by default" precedent** for a
  developer/tuning-only feature that must not change default play: `FogViewMode` defaults to `NoFog`.

## Goals

- A durable, plain-text log of outcome-level AI events, covering every player regardless of fog view
  state, that survives past the turn it happened on.
- Coverage: everything the notification system already surfaces (food/grotsits shipment sent +
  arrived, colonization started + arrived, production set, research started + completed) plus the two
  currently-silent events (production completed, colonizer-ready).
- One timestamped file per match, so multiple tuning runs can be compared afterward.
- Opt-in via a single toggle, off by default — zero overhead and zero file writes for a normal match.
- A format compact enough to read at a glance and trivial to parse reliably (by a human or by Claude)
  at the scale of a full match's worth of lines.

## Non-goals

- **No decision-level tracing in this pass.** This only logs outcomes (what happened), not the
  `ScoreMatrix`'s rejected alternatives or scoring weights behind a choice. Recorded as a follow-up.
- **No UI.** This is a file on disk, not an in-game panel. The existing notification panel is
  untouched.
- **No log rotation, size cap, or compression.** A match's line count is bounded by turns × players ×
  a handful of events per turn; not a concern at this project's scale.
- **No structured query tooling.** Reading/answering questions about the log is a plain-text read (by
  a human, or by Claude via the `Read` tool) — no database, no custom viewer.
- **No retroactive backfill.** Only events from a match with logging enabled from the start are
  captured; there is no way to reconstruct a log for a match that already happened.

## Decisions taken during brainstorming

- **Pipe-delimited structured lines**, not prose mirroring the notification text — compact and
  reliably parseable, at some cost to how naturally it reads without knowing the event codes (documented
  below).
- **Opt-in toggle, off by default**, matching the fog debug view's precedent.
- **One timestamped file per match**, kept indefinitely (the user deletes old ones manually when
  wanted) rather than a single file overwritten each match.
- **A new, standalone static class** (`AITuningLogger`), not new logic folded into `Gameboard`'s
  existing notification methods — keeps the tuning log fully decoupled from the player-facing
  notification/UI system, which has different lifetime and filtering rules (per-turn-cleared,
  fog-view-filtered) that must not leak into the log.
- **File writes are unconditionally safe to call** — `AITuningLogger`'s logging methods no-op when no
  match is active (path unset), so call sites in `GameAI.cs`/`PlayerAI.cs` never need to check the
  toggle themselves; only `Gameboard.InitGame` needs to know about it, when deciding whether to open a
  match.

## Architecture

```
Gameboard
 ├─ [SerializeField] private bool _logAIEvents = false;   ◄── new, opt-in toggle
 ├─ InitGame(...)
 │    AITuningLogger.BeginMatch(_logAIEvents);             ◄── new call
 │    ... existing init ...
 └─ (no other Gameboard changes — notification methods are untouched)

GameAI.GameAIUpdate()
    ProcessCurrentOrders()
        var executableOrders = ...
        Gameboard.Instance.CreateNotificationsForExecutingOrders(executableOrders);
        AITuningLogger.LogExecutingOrders(Gameboard.Instance.TurnNumber, executableOrders);  ◄── new
        ...
    UpdateAllPlanets(planetUpdateResults);
    AITuningLogger.LogPlanetEvents(Gameboard.Instance.TurnNumber, planetUpdateResults);      ◄── new
    GameAIMap.Knowledge.Update(...);
    ProcessResults(planetUpdateResults, gameAIOrders);
    Gameboard.Instance.CreateNotificationsForNewOrders(gameAIOrders);
    AITuningLogger.LogNewOrders(Gameboard.Instance.TurnNumber, gameAIOrders);                ◄── new
    ProcessNewOrders(gameAIOrders);

PlayerAI.cs
    ChooseNewResearch(...)
        ...
        Gameboard.Instance.CreateNotificationsForNewResearch(currentResearch?.itemName, Player.playerID);
        AITuningLogger.LogResearchStart(Gameboard.Instance.TurnNumber,                        ◄── new
            Player.playerID, currentResearch?.itemName);
    CompleteResearch(...)
        ...
        Gameboard.Instance.CreateNotificationsForCompletedResearch(completedResearchName, Player.playerID);
        AITuningLogger.LogResearchComplete(Gameboard.Instance.TurnNumber,                     ◄── new
            Player.playerID, completedResearchName);

AITuningLogger (new, static, Assets/Flatspace/Diagnostics/AITuningLogger.cs)
    private static string _currentLogPath;   // null when no match is logging

    BeginMatch(bool enabled)         — sets _currentLogPath to a fresh timestamped path, or null
    LogNewOrders(turn, orders)       — FoodShip / GrotsitsShip / ColonizeStart / ProductionSet
    LogExecutingOrders(turn, orders)— FoodArrive / GrotsitsArrive / ColonizeArrive
    LogPlanetEvents(turn, results)   — ProductionComplete / ColonizerReady
    LogResearchStart(turn, playerId, itemName)
    LogResearchComplete(turn, playerId, itemName)
    (private) AppendLine(string line) — no-ops if _currentLogPath == null, else File.AppendAllText
```

### Component boundaries

- **`AITuningLogger`** — knows nothing about `Gameboard`, the notification system, or UI. Given a
  turn number and the same data structures `GameAI`/`PlayerAI` already have in hand (orders, planet
  update results, a player id + item name), it formats lines and appends them to the current match's
  file. It owns the file-path convention and the line format, and nothing else.
- **`Gameboard`** — owns the toggle and the one `BeginMatch` call at match start. Otherwise unchanged;
  its notification methods are not modified.
- **`GameAI` / `PlayerAI`** — each gains one extra, unconditional call right next to an existing
  notification call. No control flow changes, no new fields.

## Data model / line format

One line per event: `T<turn>|P<playerId>|<EventCode>|<fields...>`, fields joined by `|`, no fields
ever contain a literal `|` (planet names, catalog item names, and numeric data in this codebase never
do). Turn and player are always the first two fields so every line sorts and scans the same way
regardless of event code.

| EventCode | Fields (in order) | Source |
|---|---|---|
| `FoodShip` | `<origin>-><target>`, `<amount>` | `OrderTypeFoodTransport`, new orders |
| `FoodArrive` | `<origin>-><target>`, `<amount>` | `OrderTypeFoodTransport`, executing orders |
| `GrotsitsShip` | `<origin>-><target>`, `<amount>` | `OrderTypeGrotsitsTransport`, new orders |
| `GrotsitsArrive` | `<origin>-><target>`, `<amount>` | `OrderTypeGrotsitsTransport`, executing orders |
| `ColonizeStart` | `<origin>-><target>` | `OrderTypePopulationTransport`, new orders |
| `ColonizeArrive` | `<origin>-><target>` | `OrderTypePopulationTransport`, executing orders |
| `ProductionSet` | `<planet>`, `<itemName>` | `OrderTypeIndustrySetProduction`, new orders |
| `ProductionComplete` | `<planet>`, `<itemName>` | `PlanetUpdateResultTypeIndustryProductionComplete` |
| `ColonizerReady` | `<planet>` | `PlanetUpdateResultTypeColonizerReady` |
| `ResearchStart` | `<itemName>` | `PlayerAI.ChooseNewResearch` |
| `ResearchComplete` | `<itemName>` | `PlayerAI.CompleteResearch` |

Example lines from a real turn:
```
T12|P0|FoodShip|P0Surplus->P0Shortage|15
T14|P0|FoodArrive|P0Surplus->P0Shortage|15
T20|P1|ColonizeStart|Home->Frontier
T23|P1|ColonizeArrive|Home->Frontier
T20|P0|ProductionSet|Capital|Food1Production
T31|P0|ProductionComplete|Capital|Food1Production
T18|P0|ColonizerReady|Capital
T9|P0|ResearchStart|Food Improvement 1
T22|P0|ResearchComplete|Food Improvement 1
```

`amount` is rendered the same way the existing notification text already does — direct string
concatenation of the boxed `order.Data` object (`"Shipping " + order.Data + " Food..."`), i.e. plain
`.ToString()`, not a fresh `Convert.ToSingle(...)` step — so the log's numbers always match what a
human already sees in the in-game notification for the same event.

## File lifecycle

- **Path**: `Application.dataPath + "/Flatspace/AITuningLogs"` in the editor,
  `Application.persistentDataPath + "/AITuningLogs"` in a build — same `#if UNITY_EDITOR` pattern
  `SaveLoadSystem` already repeats at each of its call sites, not a shared helper.
- **Filename**: `aiTuningLog_yyyy-MM-dd_HH-mm-ss.txt`, timestamped at `BeginMatch`.
- **Directory creation**: `Directory.CreateDirectory(...)` (no-op if it already exists) before the
  first write, same as `SaveLoadSystem` does for its own folders.
- **Opened**: once, in `AITuningLogger.BeginMatch(bool enabled)`, called from `Gameboard.InitGame`. If
  `enabled` is `false`, `_currentLogPath` is set to `null` and every subsequent `Log*` call for this
  match is a no-op — this also correctly resets state if the previous match had logging on and this
  one doesn't (or vice versa).
- **Written**: each `Log*` call appends its lines immediately via `File.AppendAllText` (or
  `File.AppendAllLines`) — no buffering, no persistent open `FileStream`, so there's no handle to leak
  or flush on teardown. The cost of an open-append-close per call is negligible at turn-based cadence.
- **Never read by the game itself** — this is a write-only, developer-facing artifact. Claude reads it
  with the `Read` tool when asked a tuning question.

## Lifecycle & integration points

| Site | Change |
|---|---|
| `Gameboard` | Add `[SerializeField] private bool _logAIEvents = false;`. In `InitGame`, call `AITuningLogger.BeginMatch(_logAIEvents);` (placement: alongside the other one-time-per-match init calls, before the turn loop can run). |
| `GameAI.ProcessCurrentOrders` | After `Gameboard.Instance.CreateNotificationsForExecutingOrders(executableOrders);`, add `AITuningLogger.LogExecutingOrders(Gameboard.Instance.TurnNumber, executableOrders);`. |
| `GameAI.GameAIUpdate` | After `UpdateAllPlanets(planetUpdateResults);` (before `GameAIMap.Knowledge.Update(...)`), add `AITuningLogger.LogPlanetEvents(Gameboard.Instance.TurnNumber, planetUpdateResults);`. After `Gameboard.Instance.CreateNotificationsForNewOrders(gameAIOrders);`, add `AITuningLogger.LogNewOrders(Gameboard.Instance.TurnNumber, gameAIOrders);`. |
| `PlayerAI.ChooseNewResearch` | After the existing `CreateNotificationsForNewResearch` call, add `AITuningLogger.LogResearchStart(Gameboard.Instance.TurnNumber, Player.playerID, currentResearch?.itemName);`. |
| `PlayerAI.CompleteResearch` | After the existing `CreateNotificationsForCompletedResearch` call, add `AITuningLogger.LogResearchComplete(Gameboard.Instance.TurnNumber, Player.playerID, completedResearchName);`. |
| new `Assets/Flatspace/Diagnostics/AITuningLogger.cs` | The class. |

No change to `Gameboard`'s notification methods, `PlanetUpdateResult`, `GameAIOrder`, save/load, or
any UI.

## Namespace / naming

`AITuningLogger` lives in `Assets/Flatspace/Diagnostics/` (a new folder — this is the first
diagnostics-only, non-gameplay file in the project) in the global namespace, matching
`SaveLoadSystem`'s own global-namespace static-class style. Event codes (`FoodShip`, `ColonizeArrive`,
etc.) are plain string literals embedded directly in `AITuningLogger`'s formatting code — not an enum,
since they only ever appear as log text and are never round-tripped back into code.

## Manual verification

No automated test infrastructure exists in the project, and file I/O against the real filesystem isn't
a good fit for this project's self-check pattern (which favors isolated, in-memory Editor checks).
Verification is Play-mode:

1. With `_logAIEvents` left `false` (default), play a full match and confirm no
   `Assets/Flatspace/AITuningLogs/` folder is created.
2. Enable `_logAIEvents`, start a new match, play several turns covering a shipment, a colonization,
   a production cycle, and a research completion. Confirm a timestamped file appears under
   `Assets/Flatspace/AITuningLogs/` (editor) and that its lines match what actually happened, in the
   format above.
3. Start a second match with logging still enabled; confirm a second, distinctly-timestamped file is
   created rather than overwriting the first.
4. Ask Claude a tuning question referencing the log file's turn/player/event lines, to confirm it can
   read and reason over the format directly.

## Follow-ups (out of scope, recorded for later)

- **Decision-level tracing.** Log the `ScoreMatrix`'s candidate rows and their weights at the moment a
  choice is made (colonization target selection, resource shortage/surplus pairing, production/research
  item choice), not just the winning outcome — needed to answer "why didn't it pick X instead."
- **Ship transport / industry transport events.** `OrderTypeShipTransport`, `OrderTypeRemoveShip`, and
  industry-surplus shipping aren't produced by any AI logic yet (per `CLAUDE.md`); add log coverage
  alongside whichever future task adds that AI behavior.
- **Per-planet periodic snapshots.** A turn-based dump of morale/food/grotsits per planet (rather than
  only threshold-crossing events) if outcome-level event logging alone doesn't answer enough tuning
  questions about gradual economic drift.
