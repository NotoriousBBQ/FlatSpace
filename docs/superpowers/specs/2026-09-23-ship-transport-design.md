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

- **Source:** docked warships above the round garrison (see Garrison rounds). `spare = docked - roundGarrison`, excluding ships already
  ordered out this turn.
- **Target:** `roundGarrison - (docked + incoming) > 0`. Category 5 planets are only targets when the player's
  total warship count is at least `category5UnlockShipsPerColonizedPlanet` x the number of colonized
  planets (owned by the player with population above 0), so the unlock scales with empire size.
- Trip length is bounded by a new, separate `maxPathNodesForShipTransport` (not the resource-distribution value).

## Garrison rounds

Ships built beyond the sum of all base garrisons start further rounds of filling.

- For each eligible planet, `filled = floor((docked + incoming) / garrison)`.
- The **current round** is `r = 1 + min(filled)` over all eligible planets.
- `roundGarrison = r x garrison` for every planet (its own garrison, the max over its categories).
- Eligible planets are the player's colonized planets with garrison above 0, excluding category 5
  planets while locked, and excluding planets that no other owned planet can reach within
  `maxPathNodesForShipTransport` (otherwise one unreachable planet would pin `r` at 1 forever).
- When all garrisons are full at level 1, `r` becomes 2 and every planet targets `2 x base`, and so on.
- A new planet (or a newly unlocked category 5 planet) holds 0 ships, so `min(filled)` is 0 and `r` is 1:
  planets holding more than `1 x base` become sources for it, so new planets fill first. Once it is
  filled, `r` returns to its higher value and normal category-priority filling resumes.
- No cap on rounds (a cap could be added later as a tunable).

## Tunables

New fields on `GameAIConstants` (edited through the existing asset
`Assets/GameAIConstants4ProductionTypes.asset`): five garrison sizes (one per category),
`maxPathNodesForShipTransport` (int, max path node count for a ship trip) and
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
claimed once per turn; garrison round computation (advances when all are full, drops back to 1 when a new planet or unlocked category 5 planet appears, unreachable planets excluded); category-5 unlock ratio (excluded below `ratio x colonized planets`, included at or above it, and it scales as planets are added); targets beyond `maxPathNodesForShipTransport` are excluded; count = `min(spare, deficit)`; order trio types,
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
- A drop back to round 1 (a new planet) drains ships from planets that were partway through a higher round; this is intended.
- Peek/undock "first N" coupling must be preserved (covered by the self-check).

## Plan-time refinements

Decisions made while writing the implementation plan (`docs/superpowers/plans/2026-09-23-ship-transport.md`).
Where these differ from the body above, these win.

- **Choice equality is on the target planet only** (not source + target). `ScoreMatrix` removes a chosen
  choice from other rows via `Equals`; the same target appears in every row with different cost/spare
  values, so any other equality would let two sources claim one target.
- **Row priorities are unique.** Each source row's `Priority` is its rank (largest spare first). The
  existing `ScoreMatrixDecisionComparer` can report distinct rows as equal when their positive
  priorities tie, which would make `SortedDictionary` throw. `ScoreMatrix.cs` is not modified.
- **High traffic** counts `GameAIMap.GetNeighbours(name).Count` (the symmetrized connection view shared
  with fog of war and player knowledge) against a tunable `highTrafficConnectionCount` (default 4).
- **Locked category-5-only planets** (only category 5 applies and the unlock ratio is not met) are neither
  source nor target while locked. A planet that also has another category is never locked.
- **Fleet save shape** reuses `GameSave.ShipSave` (kind, owner, researchSnapshot) as
  `OrderSave.fleetShips`; no new wrapper class. Empty/missing means no fleet.
- **Travel delay** is clamped to at least 1 turn: a `Delayed` order with `TimingDelay <= 0` is both queued
  and executed at once by `ProcessNewOrders`, which would dock the fleet twice.
- **`ProcessShipActions(orders)`** takes only the order list (it does not read the turn's result list).
- **Defaults** for all new tunables live as field initializers on `GameAIConstants`, so the existing
  asset works without editing (garrisons 4/6/4/2/1, `maxPathNodesForShipTransport` 10,
  `category5UnlockShipsPerColonizedPlanet` 3, `highTrafficConnectionCount` 4).
- **Edit-mode safety:** `Planet` destroys ship components with `DestroyImmediate` outside Play mode, so the
  self-check can exercise undocking.
