# Blockade Avoidance for Resource Shipping — Design (sub-project 2 of the "AI avoids blockaded routes" unit)

Date: 2026-09-30

## Background

Sub-project 1 (`2026-09-29-blockade-avoidance-colonization-design.md`) made colonization blockade-aware and left the
view, the route planner and route-carrying orders in place for this sub-project to reuse. Food and grotsits shipments are
still planned blindly: `PlayerAI.BuildResourceMatrix` offers any surplus planet whose precomputed shortest path is in
range, `EmitResourceOrders` sets the delay from that path's cost and attaches no route, and `BlockadeSystem` then cuts the
shipment wherever it crosses a blockaded planet (losing the blockade value at each one; removed when nothing is left).

## Goal

Resource shipping plans around blockades the player can see. A shipment takes a clean route when one exists (even one
longer than `maxPathNodesForResourceDistribution`); when none exists it is sent only if it would still arrive after the
unavoidable losses; otherwise it is cancelled. Success is measured on a tuning log: far fewer food/grotsits shipments
reduced or removed by blockades (`OrderBlocked` lines), without a fall in shipments that reach their targets.

## Rules (from the roadmap item "AI avoids blockaded routes and uses blockades", sub-project 2)

- Routes avoid visible blockaded planets, even beyond the maximum range: the detour's final path may be longer than
  `maxPathNodesForResourceDistribution`.
- With no clean route, ship only if the amount exceeds the total blockade values along the best route; otherwise cancel.
- A blockaded source or target is **not** dropped outright. It is an ordinary blockaded node on the route: its value is
  part of the loss, and the shipment is cancelled only when the total loss reaches the shipment amount.

## Decisions agreed in brainstorming

- **Reuse from sub-project 1, unchanged:** `BlockadeView` and `PlayerAI._blockadeView` (null means nothing is blockaded;
  remembered blockades are already in the view), `RoutePlanner.PlanRoute` (clean shortest path or clean detour; range
  rule), `GameAIOrder.Route`, `BlockadeSystem.RouteFor`/`PassedNodes`, `OrderSave.route` and its restore in
  `GameAI.SetSimulationStats`, `RouteDetour` logging, and the "log on state change" pattern (`NoteColonizeHeldBack`).
- **Range rule is the same as colonization:** the target must be in range by its *shortest* path
  (`2 <= NumNodes <= maxPathNodesForResourceDistribution`, which also rejects `FindPath`'s 1-node no-route stub). Only the
  route actually taken may run past the limit. A source out of range by shortest path is not made reachable by this.
- **Best route with no clean route** is the one with the least total blockade value; ties break by path cost, then node
  name.
- **Cancel when `loss >= amount`** (a shipment that would arrive with 0 is cancelled; the system removes orders at
  `remaining <= 0`). This differs from the literal "exceeds" only on exact equality.
- **Source blockades are really applied.** `BlockadeSystem` currently never checks a shipment's origin ("the origin is never
  checked"). To keep the simulation and the AI consistent, food/grotsits shipments now lose the origin's blockade value on
  departure, like any other node (see section 4). Colonists are unchanged: a blockaded colonizer is already held back.
- **Shipment with partial loss** sends and deducts the full amount; the loss happens en route as it does today.
  `remainingShortage` is reduced by the amount sent, not the expected arrival, so a shortfall re-reports after a lossy
  shipment lands.

## 1. `RoutePlanner`

- `PlannedRoute` gets `Loss` (float, default 0): the total blockade value the route accrues, origin included. Colonization
  ignores it (always 0).
- **Shared search core.** Extract the Dijkstra in `ShortestPathAvoiding` into a private core that takes an optional
  blocked set (hard-skipped nodes, as today) and an optional per-node loss function, and minimises the pair
  `(total loss, total cost)` with ties broken by the smaller node name. Both keys are additive and non-negative, so the
  lexicographic order keeps Dijkstra correct. `ShortestPathAvoiding` becomes a wrapper with the same signature and
  behaviour (no loss function, so the loss key is always 0 and ordering is exactly cost then name, as today).
- Extract the range rule from `PlanRoute` (target and origin known, not the same, `2 <= NumNodes <= maxNodes`) into a
  private helper shared with the new method; `PlanRoute`'s behaviour does not change.
- **`PlanShipmentRoute(map, origin, target, view, maxNodes)`:**
  1. Range check via the shared helper; failure returns null (cancel: out of range, unknown planet or no-route stub).
  2. The origin's own value `o = view.Value(origin)` (0 with a null view).
  3. Target not blockaded: `PlanRoute(...)`. A non-null result is the clean route (shortest path, or a detour that may
     exceed `maxNodes`); return it with `Loss = o`.
  4. Otherwise (the target is blockaded, or `PlanRoute` found no clean route): the core with no blocked set and the
     view's values as the node loss. Result: the least-loss route, `Loss = o + route loss`, `IsDetour` true when it is not
     the ordinary shortest path.
  5. No path at all returns null.
- Nothing else in `PlanRoute` changes; colonization is untouched.

## 2. Matrix (`PlayerAI.BuildResourceMatrix`)

- Sources are no longer dropped for being blockaded, and the `pathMap`/`NumNodes` filter is replaced by
  `PlanShipmentRoute` (the same range rule lives inside it). For each (shortage, surplus) pair the amount that pair could
  carry is `min(remainingSurplus, remainingShortage)`; the pair is offered only if `loss < amount` (rule above).
- `ResourceChoiceElement` gains `Route` (`List<string>`) and `Loss`; its `Cost` is now the planned route's cost (so the
  delay is right for detours).
- **Scoring:** `GenerateActionList` currently takes `ChoiceCompare: null` (default order by `Cost - Surplus`). Pass a
  comparer that puts clean routes (`Loss == 0`) before lossy ones, then uses the existing order, so a nearer lossy source
  never beats a clean one. Confirm the comparer signature in the implementation.
- **Held-back state:** when `PlanShipmentRoute`/the amount rule removed every source for a shortage row that had at least
  one in-range candidate, log `ShipmentCancelled` once per state change (new `_shipmentHeldBack` dictionary beside
  `_colonizeHeldBack`, same `Note...` helper shape, log-only and not saved). It is cleared when the row gets a choice or
  the shortage ends.

## 3. Orders (`EmitResourceOrders`)

- `ResourceAction` exposes `Route` and `Loss` from its chosen element. The transport order gets
  `Route = new List<string>(route.Nodes)`; the deduction and in-progress orders do not need one. Delay is
  `Convert.ToInt32(action.Cost / defaultTravelSpeed)`, unchanged in form.
- A detour (`route.IsDetour`) logs `RouteDetour` (as colonists do; a shipment detour is distinguished by its neighbouring
  `FoodShip`/`GrotsitsShip` lines). A shipment with `Loss > 0` logs `ShipmentLossy|<origin>-><target>|<amount>|<loss>`.

## 4. `BlockadeSystem`: the origin of a shipment

- `ApplyToShipment` also applies the origin's blockade value, once, on the first turn the order is processed (the turn its
  previous progress is 0: `TimingDelay + 1 >= TotalDelay`). `RouteFor`/`PassedNodes` are not changed, because they are shared
  with colonists, whose origin stays unchecked.
- The origin cut uses the same code path as any other node: `LogBlockade`, a `BlockadeCut` (so the owner's
  `BlockadeMemory` learns the origin), `LogOrderBlocked`, reduce `Data`, remove and clear the incoming flag at `<= 0`.
- The value is read when the order is first processed (one turn after planning), so a blockade that appears or lifts in
  between is what counts. Accepted; same staleness as any node.
- `CLAUDE.md` ("The origin is never checked") is updated to say shipments check it.

## 5. Saves and compatibility

Nothing new: `OrderSave.route` already persists the route for any order. An older in-flight shipment with no route falls
back to the shortest path. Its origin cut happens only if the order has not yet had its first processed turn, so a loaded
older shipment is normally unaffected. No new tunables.

## 6. Logging

Reused: `RouteDetour`. New (`AITuningLogger`, no toggle checks at call sites):
`ShipmentLossy|<origin>-><target>|<amount>|<loss>` and `ShipmentCancelled|<target>|Blockade`. The `tuning-log` skill
gets both lines, plus the expectation that food/grotsits `OrderBlocked` counts fall.

## 7. Self-checks

Added to `BlockadeAvoidanceSelfCheck` (already in Run All AI Self-Checks; never `Gameboard.Instance`; distinct positions):

- **Planner core:** `ShortestPathAvoiding` behaves exactly as before on the existing diamond-graph checks (regression).
  `PlanShipmentRoute`: clean shortest path unchanged with `Loss` 0; clean detour (including beyond `maxNodes`) with `Loss`
  0; out-of-range shortest path is rejected even when a detour would exist; no-route stub rejected; with every way
  blockaded, the least-loss route is chosen, ties broken by cost then name; a blockaded target counts toward the loss; a
  blockaded origin adds its value to the loss and is not dropped.
- **Matrix:** a blockaded source is offered with its loss; a pair with `loss >= amount` is not offered and one with
  `loss < amount` is; a clean source beats a nearer lossy one; a shortage whose every source is removed by blockade sets
  the held-back state once (and again only when it changes).
- **Orders:** the transport order carries `Route`; its delay uses the route cost (a detour delay exceeds the shortest
  path's); `ShipmentLossy` is logged for a lossy choice.
- **BlockadeSystem:** a shipment from a blockaded origin loses the origin's value on its first turn and only once; a
  colonist from a blockaded origin is unchanged; a shipment removed by the origin cut clears the incoming flag; a blockade
  on a planet only on the shortest path does not affect a shipment carrying a detour route.

## 8. Docs

`CLAUDE.md` (Warships and Blockade: shipment planning, the shared search core, the origin rule, the two log lines, the
`RouteDetour` ambiguity), `FUTURE_FEATURES.md` (mark sub-project 2 done; sub-projects 3 to 5 remain), the `tuning-log`
skill. New scripts' `.meta` files are committed with them (this design adds no new scripts).

## Out of scope

Distribution Center selection (it keeps its own reachability logic); assault and warship movement (sub-project 3);
connectivity weighting (4); rerouting shipments already in flight (5); acting on blockades the player cannot see; the
pre-existing zero-delay double execution of a `Delayed` order.

## Risks

- **Origin cut changes economy numbers:** blockaded producers will now lose part of every shipment, so runs are not
  directly comparable with earlier ones on boards with lasting blockades. The tuning-log comparison should note this.
- **Sort key:** the default choice order uses `Surplus` from the raw result, not the remaining balance; the new comparer
  keeps that behaviour and only adds the clean-first rule.
- **Planner cost:** the lossy search runs only when no clean route exists, per (shortage, source) pair. Matrix rounds
  re-plan each round; if this shows in profiling, cache routes per turn (keyed by origin and target), as colonization does
  with `plannedRoutes`.
- **Stale loss:** the view is built once per `ProcessResults`; a shipment's losses are decided when it passes each planet,
  so the planned `Loss` is an estimate.
