# Colony ship surge for dominated planets (design)

Date: 2026-10-18. Source: `FUTURE_FEATURES.md`, "Still open from (3)", item (1); brainstormed with the owner this session.
Builds on `docs/superpowers/specs/2026-10-06-invasion-conversion-design.md`.

## Intent

A conversion goes faster the larger my share of the planet (`progress += 1 / max(1, conversionTurnsBase x (1 - p))`), and a colonist
raises that share. Today a dominated planet (`PlayerAI.IsConversionTarget`) only gets a *cost tilt* on where colonists go
(`ConversionColonizeDivisor`); nothing makes more colony ships get built or sent, so the colony-ship speed-up fired 0 to 3 times a run.
The owner wants a **surge**: planets next to a dominated planet build and send colony ships to it quickly, so the conversion finishes ASAP,
without ever sending more colonists than the planet can hold.

## Goals

1. **Wanted ships scale with how little of the planet is mine.** For a dominated planet D:
   `ratio = total population / max(1, my population)`; `wanted(D) = ceil(ratio x conversionSurgeShipsPerRatio)`.
2. **Hard cap by room (the owner's "would wince" rule).** No colony ship is started for D when
   `population(D) + my colonists en route to D + my colony ships being built or docked for D >= MaxPopulation(D)`.
   Equivalently `pipeline(D) < room(D)` where `room = MaxPopulation - population - colonists en route`. `wanted` is the total number of ships
   committed to D (en route plus pipeline), so the effective wanted count is `EW = max(0, min(wanted - enRoute, room))` (approved
   2026-10-18; read as plain `min(wanted, room)` it would send `wanted` more ships every turn). Production starts a ship while
   `pipeline < EW`; the launch budget in one colonization pass is `EW`. A planet at max population has room 0 and gets no surge.
3. **Production surge.** Planets adjacent to D (`GameAIMap.GetNeighbours`) get their ColonyShip weight multiplied by
   `conversionSurgeBoost` (default 3, on top of today's x2) while D's pipeline is below its effective wanted count. It is a situational
   weight: it goes in `GetIndustrySituationalWeightMultiplier`, computed at most once per production turn.
4. **Docked ships go to D first.** An adjacent planet that is a ready colonizer (docked colony ship, population at or above
   `expandPopulationTrigger` x max) sends that ship to D while D has unmet wanted count and room, instead of picking by cost. It builds no
   new ship while it holds one (the existing weight 0 for "already docked" stays).

## Non-goals and anti-goals

- No forced production: a surging neighbour can still build anything else (a multiplier, not an exclusive choice).
- No drain of unready planets: the readiness rule (population trigger) stays; an unready neighbour keeps its docked ship.
- No change to which planets count as dominated, to conversion pace, hold/release, hostility or saves.
- Never more colonists in flight and building than D's room.
- The existing rules still win: a blockaded planet builds and launches nothing; nothing builds when no valid target is in reach;
  a launch needs a route (`RoutePlanner.PlanRoute`) and `CanSupportColony`. If D has no usable route from a neighbour, that neighbour
  launches by the ordinary rule.

## Constraints found in the code (the builder must handle them)

- **One colonist per target at a time today.** `IsValidColonizationTarget` returns false when `IsPopulationTransferInProgress` for me, and
  `ScoreMatrix.GenerateActionList` removes a claimed target from every other row. A surge needs several colonists to D at once. The
  smallest change: for a surge target, accept it while en-route count + claims this pass < room, and allow up to that many claims on it
  in the launch pass. `ColonistRedirect.IsDiversionTarget` and the arrival rule (`ApplyColonistArrival`: at max, an own-planet colonist docks a
  colony ship) must keep working; the room cap exists to make that rare. The `PopulationTransferInProgress` flag is per player and per
  planet, so a second colonist must not clear it early (see `ColonistRedirect.Apply`, which already handles twin colonists).
- Colonists en route are counted from the in-flight `OrderTypePopulationTransport` orders of my player with `Target == D`.
- Pipeline counts, for D: my colony ships in `CurrentProduction` and the production queue's in-progress item on D's neighbours, plus
  colony ships docked on D's neighbours.
- Diplomacy off (legacy mode): `IsConversionTarget` is already false, so the feature is inert.

## Decisions

Owner's: the wanted count scales with `total / mine`; the cap by room including ships being built; neighbours surge; a multiplier at x3;
a docked ship goes to D before a new one is built; the ready requirement stays.

My calls (to check):
- **"Adjacent" means a direct neighbour** (`GetNeighbours`), not path range.
- **Docked ships count as pipeline**, and ships in production count against the nearest-cap rule, because neither has a target yet.
- **A neighbour of several dominated planets** serves the one with the largest unmet count (ties to the lower name); each ship is counted
  once, for that planet.
- **`mine` is floored at 1** in the ratio, so a planet with none of mine reads as `total`.
- **The surge multiplies whatever ColonyShip weight the planet already has** (it is skipped when that weight is 0). In Expand a
  not-yet-ready planet has weight 1 and becomes 3.
- **`conversionSurgeShipsPerRatio` = 1** as the starting scale; 0 or below switches the whole surge off, as does `conversionSurgeBoost` of 1 for production only.

## Tunables (`GameAIConstants`, in-code defaults, also written into the constants asset)

| Tunable | Default | Meaning |
|---|---|---|
| `conversionSurgeBoost` | 3 | multiplier on a neighbour's ColonyShip weight while D needs ships (1 or below: off) |
| `conversionSurgeShipsPerRatio` | 1 | wanted ships = ceil(total / mine x this); 0 or below: no surge at all |

## Tuning log output (proposed)

- `ConversionSurge|<planet>|<wanted>|<room>|<enRoute>|<pipeline>|<myPop>|<totalPop>` on a change of (wanted, room, pipeline) for a
  dominated planet, through a new log-only `ConversionSurgeTracker` (like `ConversionHoldTracker`); a load logs each current one again.
  Answers: is the wanted count what we think, does the cap bind, how long does a surge last.
- `SurgeBuild|<neighbour>|<planet>|<multiplier>` when a ColonyShip is started on a planet whose weight was surged (once per start, next to
  the existing `ProductionSet`). Answers: how often the boost actually changes a build.
- `SurgeLaunch|<origin>-><planet>|<myPop>|<totalPop>|<enRoute>|<routeCost>|<nearestOrdinaryTarget>` per colonist sent by the docked-ship
  rule (extends the idea of `ConversionColonize`, which stays). Answers: did the forced target override a nearer one, and how much.
- Update the `tuning-log` skill (pace of surges, launches per conversion, arrivals at a full planet via `ColonistDocked` as the overshoot
  signal, colony failures on converted planets) and the "AI Tuning Log" section of `CLAUDE.md`.

## Tests

A new suite (`Assets/Editor/ConversionSurgeSelfCheck.cs`, added to `AllAISelfChecks`; no `Gameboard.Instance`): wanted count and ratio
(including mine = 0), room and the pipeline cap (the owner's rule at, below and above the boundary), the weight multiplier (applied,
skipped at weight 0, off at 1), the neighbour test, one ship counted once for a neighbour of two dominated planets, multiple
simultaneous colonists to one target and the flag staying until the last lands, the docked ship's forced target (and the fallback when D
has no route or the planet is unready). Do not put a check exactly on a float boundary. A case in `SimultaneitySelfCheck` is not needed
(every input is the shared state).

## Left to the builder

Where the per-turn surge snapshot lives (it must be computed once per production turn and once per colonization pass), the exact claim
mechanism for several colonists on one target, and the names of the helper methods.
