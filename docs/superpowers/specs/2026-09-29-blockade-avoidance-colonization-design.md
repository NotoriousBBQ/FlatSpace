# Blockade Avoidance for Colonization — Design (sub-project 1 of 3)

Date: 2026-09-29

## Background

Blockade (see `2026-09-28-warship-stats-and-blockade-design.md`) removes colony orders and shipments that pass a
planet where another player's docked offense exceeds the order owner's. It shipped without any AI awareness of it. A
tuning-log review of `test2.json` (three players, 400 turns) found that every colony ship that failed to arrive was
blockaded: 40 to 55% of launches were lost in each of five runs (e.g. P0 35 launched, 14 arrived), against about 98%
arriving before blockade existed, and the AI kept building colony ships for routes it could not use.

## Goal

Make colonization blockade-aware: a player routes colony ships around planets it can see are blockaded against it,
cancels colonization when no such route exists, does not colonize from or build colony ships at a blockaded planet, and
builds warships at a blockaded planet to break the blockade. Success is measured on a `test2.json` tuning log: the
colony-ship arrival rate returns to near its pre-blockade level, with far fewer colony ships lost or wasted.

## The user's rules, and how they are split

The user gave eleven rules. They are delivered as three sub-projects, each with its own spec, plan and Editor
verification, in this order. This spec is sub-project 1 only.

| # | Rule | Sub-project |
|---|---|---|
| 1 | Blockaded planets start no new colony ship production | 1 |
| 2 | Blockaded planets are not eligible to ship resources | 2 |
| 3 | Blockaded planets may hold colony ships but do not colonize until the blockade lifts | 1 |
| 4 | Blockaded planets get a tunable boost to warship production and upgrade | 1 |
| 5 | Colonization routes avoid visible blockaded planets, even beyond the maximum range | 1 |
| 6 | No viable colonization route: colonization is cancelled | 1 |
| 7 | Resource shipments avoid visible blockaded planets, even beyond the maximum range | 2 |
| 8 | No clean route: ship only if the amount exceeds the total blockade values on the route | 2 |
| 9 | Otherwise resource shipment is cancelled | 2 |
| 10 | Assault behavior is modified to break blockades on visibly blockaded planets | 3 |
| 11 | Optional: prioritize planets that cut a colonization or shipment in the last N turns (tunable, 5) | 3 |
| 12 | (added later) Breaking blockades also includes research priority changes for warships, so a blockaded player researches warship improvements sooner | 3 |

Assumptions recorded for the later sub-projects: rule 8's "total blockade values" is the sum along the best route (a
blockaded target counts), not across the whole map; "breaking" a blockade means docking enough offense that the value
is 0 or less (there is no combat), which needs offense-based force sizing because `AssaultPlanner` sizes by ship count
today. Sub-project 2 reuses the view, the planner and route-carrying orders built here.

## Decisions agreed in brainstorming

- **Visibility** is current vision: a player sees the docked ships, and so the blockade value, of every planet it has
  presence at (population or a docked ship, via `GameAIMap.GetVisionSourcePlanets`) and of their direct neighbours
  (`GetNeighbours`). Recomputed each turn, not sticky. Not the sticky `PlayerKnowledge` set, and never the fog-of-war
  grid (fog is presentation-only). A planet the player cannot see is treated as unblockaded, so the AI will sometimes
  route into an unseen blockade and lose the ship.
- **Fleet cap:** a blockaded planet is exempt from the Consolidate fleet-cap cutoff for warship production (the boost
  applies to a multiplier floored at 1), because new ships dock at the producing planet and directly raise its own docked
  offense there.
- **Detours** are computed only when the target is in range by node count but its shortest path is visibly blocked; the
  detour may then exceed the normal node range. A target out of range by node count is not made reachable by this.
- **Approach:** orders carry the route they were planned on (rather than re-deriving a route each turn), with hard
  avoidance and cancellation (rather than soft cost penalties).

## 1. `BlockadeView`

- Pure class in `FlatSpace.AI`, no `Gameboard.Instance`. `BlockadeView.Build(GameAIMap map, int playerId,
  BlockadeSystem blockade)` collects the visible set (every `GetVisionSourcePlanets(playerId)` planet plus each one's
  `GetNeighbours`) and keeps the planets where `blockade.Value(planet, playerId, out blocker) > 0`.
- API: `IsBlockaded(string name)`, `Value(string name)`, `Blocker(string name)`, `BlockadedNames`.
- The player's own planets are always visible, so a blockaded origin is always known.
- Built once per turn at the start of that player's `PlayerAI.ProcessResults`, from the player's own research catalog
  items (an absent catalog gives an empty view). Derived state: not saved. A null view (self-checks, before the first
  turn) means "nothing is blockaded".

## 2. `RoutePlanner`

- Pure, `FlatSpace.AI`. `RoutePlanner.PlanRoute(GameAIMap map, string origin, string target, BlockadeView view,
  int maxNodes)` returns a `PlannedRoute { List<string> Nodes; float Cost; int NumNodes; bool IsDetour }` (origin to
  target inclusive) or null, which means "cancel".
- Order of checks:
  1. The target is visibly blockaded: null (a colonist could never dock).
  2. The shortest path from `GameAIMap.GetPath` is within range (`2 <= NumNodes <= maxNodes`, so the 1-node no-route stub
     is never read as a free trip) and no node after the origin is visibly blockaded: use it. This is exactly today's
     behavior for unblockaded routes.
  3. The target is in range by node count but that shortest path passes a visibly blockaded planet: run a fresh
     Dijkstra over the explicit graph (`PathingSystem.Instance.PathNodes`, each `PathNode.Connections` giving edge
     costs) that skips visibly blockaded planets (never the origin), with no node limit. Ties break by node name so the
     result is deterministic. No path: null. Otherwise `IsDetour = true`.
  4. The target is out of range by node count: null (unchanged from today's filter).
- `FindPath` is left untouched: its tie-breaking is fragile (see `CLAUDE.md`, Pathing), so the detour uses its own
  small, separately tested Dijkstra.
- The trip delay is computed from the chosen route's cost exactly as today (`Convert.ToInt32(cost /
  defaultTravelSpeed)`); the existing behavior for a zero delay is unchanged.

## 3. Orders carry their route

- `GameAI.GameAIOrder` gets `Route` (`List<string>`, origin to target inclusive), `[NonSerialized]` like `Fleet`. It is
  set on the colonist order (`OrderTypePopulationTransport`) only; the food-rider twin does not need one because it is
  removed together with the colonist. Sub-project 2 reuses the field for shipments.
- `BlockadeSystem` gets `RouteFor(GameAI.GameAIOrder order)`: the order's `Route` when it has 2 or more nodes (per-node
  fractions from edge costs), otherwise the existing shortest-path `Route(origin, target)`. `PassedNodes` uses it.
  `GameAIMap.EdgeCost(a, b)` (new) reads the connection cost between two adjacent nodes. Nothing else about how
  blockades act on passed nodes changes.
- Saves: `OrderSave.route` (`List<string>`), written by `SaveLoadSystem` and restored in `GameAI.SetSimulationStats`. An
  older save, or an order without a route, loads as null and falls back to the shortest path.

## 4. AI wiring

- `PlayerAI.ProcessColonizers`:
  - skips a colonizer whose planet is blockaded for that player (rule 3);
  - builds each colonizer's choice list from `RoutePlanner` (rules 5 and 6): a target with no route is dropped, the
    choice `Cost` is the planned route's cost, and the chosen route is attached to the transport order as `Route`. When
    no choice survives the colony ship stays docked and retries next turn.
  - `CanSupportColony` and `IsValidColonizationTarget` are unchanged.
- `PlanetHasColonizationTarget` (drives colony ship production) uses the planner too, so a planet does not build a colony
  ship for targets it cannot currently reach.
- ColonyShip situational weight is 0 on a blockaded planet (rule 1), in `GetIndustrySituationalWeightMultiplier`.
- Warship boost (rule 4), also in `GetIndustrySituationalWeightMultiplier` per the convention that situational factors
  live there: on a blockaded planet, Warship gets `max(current multiplier, 1) x blockadedWarshipBoost` (so the
  Consolidate cap cutoff no longer applies there, and under Expand, which has no cap, it simply multiplies); Update
  Warship gets the same boost, but its existing 0 (nothing to upgrade) stays 0. New tunable
  `GameAIConstants.blockadedWarshipBoost`, in-code default 3. The Warship multiplier cache
  (`_warshipMultiplierThisTurn`) is unaffected: the planet-specific part is applied after it.
- Logging (`AITuningLogger`, no toggle checks at call sites): `RouteDetour|<origin>-><target>|<nodes joined by '>'>|<cost>`
  when a detour is taken, and `ColonizeCancelled|<origin>|<reason>` with reason `BlockadedOrigin` or `NoRoute`. Like
  `ColonizerReady`, `ColonizeCancelled` repeats every turn the condition holds, so its count overcounts; note that in
  `CLAUDE.md`.

## 5. Saves and compatibility

Only `OrderSave.route` is new; everything else is derived. Older saves load unchanged. `blockadedWarshipBoost` has an
in-code default, so the constants asset needs no edit until tuned.

## 6. Self-checks

New checks (a new `BlockadeAvoidanceSelfCheck.cs`, `FlatSpace → AI → Run Blockade Avoidance Self-Check`, added to
`Run All AI Self-Checks`; never using `Gameboard.Instance`, planets at distinct positions):

- `BlockadeView`: a blockaded own planet and a blockaded neighbour are seen; a blockaded planet two hops from any
  presence is not; the player's own docked offense cancels a blockade; a null view means nothing is blockaded.
- `RoutePlanner` on a synthetic diamond graph (A to D via B or C, plus a long way round): clean shortest path unchanged;
  shortest path blocked at B gives the detour via C with `IsDetour`; both blocked with a long clean way round gives
  that route even beyond `maxNodes`; no way round gives null; blockaded target gives null; out-of-range target gives
  null; the 1-node no-route stub is never accepted; deterministic tie-breaking; an unseen blockade is not avoided.
- Orders: the planned route is attached to the colonist order and its delay matches the route cost; `BlockadeSystem`
  applies a blockade along the carried route (a planet on the detour blockades it, a planet only on the shortest path
  does not), and falls back to the shortest path when no route is carried.
- Saves: an order route survives a `JsonUtility` round trip through `OrderSave`; an older order with no route loads as
  null.
- Production: ColonyShip weight is 0 on a blockaded planet; the Warship multiplier on a blockaded planet is
  `max(m, 1) x boost` under Consolidate at and beyond the cap (where it is otherwise 0) and `boost` under Expand;
  Update Warship keeps 0 when nothing is upgradable.
- Colonization: a blockaded colonizer emits no colonist order; a colonizer whose every route is blocked emits none and
  stays ready.

The fleet-cap check in the tuning-log review ("multiplier 0 means no warship start") is updated in `CLAUDE.md` and the
`tuning-log` skill to exclude blockaded planets.

## 7. Docs

`CLAUDE.md` (Warships and Blockade section: the view, planner, route-carrying orders, the fleet-cap exemption and the
new tunable; the two log lines and their overcount caveat; the self-check menu entry and its inclusion in Run All),
`FUTURE_FEATURES.md` (mark rules 1, 3, 4, 5, 6 done; list sub-projects 2 and 3 with the rules above). New scripts'
`.meta` files are committed with them.

## Out of scope

Resource shipping (sub-project 2); assault and blockade breaking (sub-project 3); rerouting an order already in flight
when a blockade appears; acting on blockades the player cannot see; soft or cost-based avoidance.

## Risks

- **Planner cost per turn:** the detour Dijkstra runs only for targets whose shortest path is blocked and only for
  colonizers with a ready colony ship, so its cost is small; it is not run for every pair.
- **Stale view within a turn:** the view is built once at the start of `ProcessResults`. Orders emitted earlier the same
  turn by other players can change docked ships only at execution time, so within a turn the view is consistent; ships
  arriving at execution can still surprise an order in flight, as designed (unseen or new blockades are not avoided).
- **Detour length:** a detour can be long and slow, and the colonist may still be blockaded en route by a planet the
  player could not see when it launched. Accepted; the tuning log will show how often.
- **Fleet-cap exemption:** deliberately weakens the fleet-cap invariant on blockaded planets (see Decisions).
