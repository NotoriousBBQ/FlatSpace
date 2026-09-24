# Ship Transport AI — Design

## Goal

Give the AI the ability to move docked ships between its own planets. Today `OrderTypeShipTransport`
is mechanism-only: `GameAI.ExecuteOrder` handles it, but nothing in `PlayerAI` creates one, and the stub
creates brand-new ships on arrival (wrong owner, research snapshot silently rebuilt). This work adds:

- a new `ProcessShipActions` step in `PlayerAI`, following the `Process*` pattern;
- a `ScoreMatrix`-driven decision of which planets send how many ships where;
- a proper order flow that preserves each ship's owner and `ResearchSnapshot`;
- notifications and tuning-log entries for every step.

Success: under `AIStrategyExpand`, surplus warships flow from planets that hold more than their garrison
to planets that hold fewer, in a tunable priority order, without spreading ships evenly across the map.

## Decisions already made (with the user)

- **Garrison model.** Each planet category has a desired garrison size. Ships move from planets above
  their garrison to planets below it. Priority decides who is filled first when supply is short.
- **Matrix shape (Option B).** Rows are source planets; choices are target planets. The ship count is
  fixed when the choice is built as `min(spare, deficit)`. A target picked by one row is removed from all
  others (the existing `ScoreMatrix` behaviour), which implements "a targeted planet is removed from
  further targeting this turn". A large deficit is filled over several turns.
- **Overlapping categories.** Priority = best (lowest-numbered) applicable category. Garrison = the
  largest garrison among all applicable categories.
- **Snapshots preserved.** Fleet orders carry each ship's `ResearchSnapshot` from departure to arrival.
- **Line drawing.** Already exists (`GameBoard`: magenta, 30-unit offset, fog dimming). No change.
- **Deferred.** A third matrix dimension (quantity or ship kind) is explicitly out of scope; revisit later.

## Categories, garrisons, roles

Categories, per planet owned by the player (priority order, lower = better):

| # | Category | Condition |
|---|----------|-----------|
| 1 | Specialized | Desert, Industrial, Farm, Ocean |
| 2 | Outer | owned by the player with at least one neighbour (`GameAIMap.GetNeighbours`) whose population is 0 |
| 3 | Prime | `PlanetTypePrime` |
| 4 | High traffic | 4 or more `Connections` |
| 5 | Highly specialized | Verdant, Desolate |

A planet may match several. Its target priority is the lowest matching number; its garrison is the max of
the matching categories' garrison values. A planet in no category has garrison 0.

Garrison size ordering (all tunable): category 2 highest; categories 1 and 3 medium; category 4 low;
category 5 very low. Initial values are a starting guess to be tuned from the AI tuning log.

Roles each turn (one role per planet; target wins a tie):

- **Source:** docked warships above garrison. `spare = docked - garrison`, excluding ships already
  ordered out this turn.
- **Target:** `garrison - (docked + incoming) > 0`. Category 5 planets are only targets when the player's
  total warship count is at least `category5UnlockShipsPerColonizedPlanet` x the number of colonized
  planets (owned by the player with population above 0), so the unlock scales with empire size.
- Trip length is bounded by the existing `maxPathNodesForResourceDistribution`.

## Tunables

New fields on `GameAIConstants` (edited through the existing asset
`Assets/GameAIConstants4ProductionTypes.asset`): five garrison sizes (one per category) and
`category5UnlockShipsPerColonizedPlanet` (float ratio of total warships to colonized planets). No other tunable mechanism is introduced.

## Matrix

`Assets/Flatspace/GameAI/ShipMatrix.cs` (beside `IndustryMatrix.cs` / `ResourceMatrix.cs`):

- `ShipChoiceElement : IScoreMatrixChoiceElement` — target planet, category priority, path cost,
  `Surplus` (spare ships at the source), `Shortage` (target deficit). Equality is on source + target.
- `ShipAction : IScoreMatrixAction` — origin, target, cost, count, ship kind.
- `ScoreMatrix<ScoreMatrixDecisionElement, ShipChoiceElement, ShipAction>`: one row per source with
  `Priority = spare` (largest surplus first). Choices are ordered by `ChoiceCompare`: category priority,
  then path cost. Action factory builds `ShipAction` with `count = min(spare, deficit)`.

`PlayerAI.ProcessShipActions(results, orders)` is called from `ProcessResultsStrategyExpand` after
`ProcessIndustry`, so ships produced this turn are visible. Mixed fleets are not created under Expand;
`ShipAction` and the payload carry a kind so a later strategy can add them without changing the shape.

## Order flow

`OrderType` values are appended last (it serializes as an int):

1. `OrderTypeShipTransport` (existing) — delayed arrival at the target; `Data` = ship count. The old
   signed-delta stub is replaced. Nothing created these orders before, so no saved order changes meaning.
2. `OrderTypeShipDeparture` (new) — immediate; undocks N ships of a kind at the origin.
3. `OrderTypeShipTransferInProgress` (new) — immediate; adds N to the target's incoming counter.

Delay = `path.Cost / defaultTravelSpeed`, as for other transports.

### Fleet payload

`GameAIOrder` gets an optional `ShipFleetPayload` (ship kind + one snapshot list per ship).

- `PlayerAI` fills it at order creation by reading the snapshots of the first N docked ships of the kind.
- Departure undocks the first N of that kind, so peek and undock select the same ships (unit-checked).
- Arrival calls the existing `Planet.DockShipFromSave(kind, owner, snapshot)` with the order's `PlayerId`
  as owner, fixing the wrong-owner and silent-upgrade problems.
- A mixed fleet later is one order per kind on the same route.

### Incoming counter

`Planet` gets an incoming-ships count per kind. In-progress adds N; arrival subtracts N. It is not saved
separately: on load it is recomputed from saved in-flight orders so it cannot drift.

### Saves

The order save entry gets an optional `fleet` field: kind plus a list of snapshot wrappers (a wrapper
class is required because `JsonUtility` cannot serialize `List<List<string>>`). Missing field (older saves,
non-ship orders) loads as null. If an arriving fleet has fewer snapshots than ships, the extras fall back
to a rebuilt snapshot.

## Display and logging

- `GameBoard.CreateNotificationsForNewOrders`: "Fleet of N warships departing X for Y" (view target: origin).
- `GameBoard.CreateNotificationsForExecutingOrders`: "Fleet of N warships arrived at Y from X"
  (view target: target).
- Departure and in-progress orders get no notification, matching the resource deduct/flag orders.
- `AITuningLogger.LogNewOrders`: code `ShipMove`, fields `origin->target`, count.
- `AITuningLogger.LogExecutingOrders`: code `ShipArrive`, same fields.
- Call sites are unconditional; the logger already no-ops when logging is off.

## Testing

New `Assets/Editor/ShipTransportSelfCheck.cs` at `FlatSpace → AI → Run Ship Transport Self-Check`,
plain assertions, never touching `Gameboard.Instance`; it builds a minimal `GameAIMap`/`Planet`/`PlayerAI`
set with distinct planet positions (avoids the `FindPath` tie issue). Covers: category detection and
overlap resolution; outer-planet definition; spare/deficit arithmetic including in-flight ships; a target
claimed once per turn; category-5 unlock ratio (excluded below `ratio x colonized planets`, included at or above it, and it scales as planets are added); count = `min(spare, deficit)`; order trio types,
timing and payload; payload snapshots match departing ships; save round-trip and older-save load; kind
field accepted while Expand never produces mixed fleets. Methods the check calls are `public`.
Line drawing, notification text and log output are verified in a real Play-mode run.

## Files

- Modified: `PlayerAI.cs`, `GameAI.cs`, `GameAIConstants.cs`, `Planet.cs`, `GameBoard.cs`,
  `AITuningLogger.cs`, `SaveLoadSystem.cs`, `CLAUDE.md` (new Ship Transport section; correct the
  "mechanism-only" note; add the self-check to Tests).
- New: `ShipMatrix.cs`, `ShipTransportSelfCheck.cs`, each with its `.meta` committed.
- Asset: `GameAIConstants4ProductionTypes.asset` needs the new garrison values (user enters them, or asks
  Claude to edit).

## Out of scope

Combat or anything reading snapshots; mixed-fleet creation; a 3D matrix; Consolidate and Amass strategies.

## Risks

- Initial tuning values (garrisons and the category-5 ratio) are guesses; the tuning log will show whether they behave as intended.
- A large deficit takes several turns to fill under Option B.
- Peek/undock "first N" coupling must be preserved (covered by the self-check).
