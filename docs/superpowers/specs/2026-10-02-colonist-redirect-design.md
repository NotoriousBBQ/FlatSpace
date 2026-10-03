# Colonist redirect (blockade sub-project 5)

Date: 2026-10-02. Working from `FUTURE_FEATURES.md` entry (5), "Colonization avoidance for orders already in flight".
Sub-project 5 is the last item of the blockade unit.

## Problem

Blockade memory and `RoutePlanner` only protect colonists launched after the owner learns of a blockade. A colonist
already in flight keeps its planned route and is removed (with its food rider) the turn it passes a blockaded node. In the
`4p.json` tuning runs P2 lost three colonists at `Farm 1` at T379, T380 and T382, all launched before the planet was
learned at T362. The colonist and rider are already paid for, so they should be saved when a way exists.

## Decisions (all the owner's picks, each with a recommendation first)

- **When no clean route exists, divert to another target** (not hold, not return).
- **Diversion only gets the wider candidate set.** Ordinary launches keep today's `IsValidColonizationTarget`.
- **Detour first, divert second.** Keep the original target whenever a clean route to it exists, however long.
- **Position model:** the colonist counts as standing on the last route node it passed; a redirect restarts its clock
  (progress on the edge it was crossing is discarded).
- **Knowledge:** redirect decisions use only the owner's `BlockadeView` (visible planets plus blockade memory).

## Behavior

### Trigger
Once per turn in `GameAI.ProcessCurrentOrders`, after delays are decremented and after `ApplyBlockades` (a colonist that was
just cut is already gone), before orders execute. For every in-flight `OrderTypePopulationTransport` with
`TimingDelay > 0`, take the order's current node (last route node passed, from `PassedNodes`-style progress against
`RouteFor` fractions; the origin when none has been passed). If any node ahead of it, up to and including the target, is
blockaded in the owner's view, the order is redirected. Otherwise nothing changes.

### Resolution, in order
1. **Detour, same target.** `RoutePlanner.PlanRoute(map, currentNode, target, view, maxPathNodesForColonization)`. A clean
   route replaces the order's `Route`; the delay becomes `max(1, round(route.Cost / defaultTravelSpeed))` for both
   `TotalDelay` and `TimingDelay`.
2. **Divert.** Otherwise choose among diversion candidates (below) that have a clean `PlanRoute` from the current node.
   Choice cost is `route.Cost / ColonizationCostDivisor(target)`; ties break by planet name. The order's target, route and
   delays are replaced as in step 1.
3. **Neither possible:** the order is left alone and is cut as today.

### Diversion candidates
`PlayerAI.IsDiversionTarget(planet)` (public, for the self-check): the planet is known to the owner, is not the order's
current target, and is one of: empty; has this player's colonist already inbound (`IsPopulationTransferInProgress`);
colonized with `Population.Count < MaxPopulation` (the owner's own planets included). `CanSupportColony` is not applied:
the origin already paid for colonist and rider. No claim tracking: several colonists may divert to one planet, and the
arrival rule absorbs overflow.

### Rider and flags
A diverted colonist's food rider (the `OrderTypeColonyFoodRider` order matching the player, origin and old target) takes the
new target and the same new delays. The old target's population-transfer-in-progress flag for this player is cleared,
unless another in-flight colonist of this player still targets it, and the new target's is set. A detour keeps target and
flags and only updates route and delays (and the rider's delays).

### Arrival (every colonist)
New `GameAI.ApplyColonistArrival(planet, order)`, used by the `OrderTypePopulationTransport` case: a planet with
`Population.Count < MaxPopulation` adds the colonist as today; a planet at or above max docks one colony ship for the order's
player (`DockShipRebuiltSnapshot`, since a colonist order carries no research snapshot, the ship having been undocked at
launch). That ship is eligible for the next colonization pass. The transfer flag is cleared as today and the food rider still
lands either way.

### Out of scope
Warships, resource shipments, assault routes, ordinary launch rules, per-target thresholds.

## Components

| Piece | Change |
|---|---|
| `ColonistRedirect` (new, pure, `Assets/Flatspace/GameAI/`) | Given an order, the owner's view and the map: current node, trigger test, detour then divert; returns a result (kind, route, target, cost, blocked planets). No `Gameboard.Instance`. |
| `BlockadeSystem` | Public current-node helper built on `RouteFor` and `Progress`. |
| `PlayerAI` | `IsDiversionTarget`; public getter for the `BlockadeView`; cost-divisor access for the divert choice. |
| `GameAI` | `RedirectColonists()` after `ApplyBlockades`; order, rider and flag updates; `ApplyColonistArrival`; log-only set of orders already logged as failed. |
| `AITuningLogger` | The three lines below. |

Reused unchanged: `RoutePlanner`, `BlockadeView`, `RouteFor`/`PassedNodes`, `ColonizationCostDivisor`,
`ApplyColonyFoodRider`, `GameAIOrder.Route` and `OrderSave.route`.

Because a redirect restarts the clock and starts a new route at fraction 0, `PassedNodes` and `BlockadeSystem` apply to the new
route with no change. The order keeps its real `Origin` (used to match the rider and for logs); its `Route` starts at the
redirect node, which `RouteFor` handles (a carried route of 2 or more nodes is used as planned).

Saves: none. Route, target and both delays are already in `OrderSave`; transfer flags are derived from orders on load.
Tunables: none.

## Tuning log

| Line | When | Question it answers |
|---|---|---|
| `T<turn>\|P<id>\|ColonistRedirect\|<origin>-><oldTarget>\|<Detour or Divert>\|<newTarget>\|<nodes>\|<cost>\|<blocked planets>` | once per redirect (a redirected order no longer triggers) | how many in-flight colonists are saved, and by detour or diversion |
| `T<turn>\|P<id>\|ColonistRedirectFailed\|<origin>-><target>\|<blocked planets>` | once per order, on its first failed redirect; log-only set of orders in `GameAI`, pruned as orders leave | how many in-flight colonists are still lost |
| `T<turn>\|P<id>\|ColonistDocked\|<planet>\|<amount>` | each arrival at a full planet (no tracker) | how often the arrival rule fires |

`<blocked planets>` is `name` joined by `,`, `-` when none. The `tuning-log` skill gets a "Colonist redirect" section
(redirects, failures, docked count, colonist `OrderBlocked` losses against earlier runs) and the "AI Tuning Log" section of
`CLAUDE.md` gets the lines. `AITuningLogger` has no self-check by design; verify the lines from a Play-mode log.

## Tests

`FlatSpace -> AI -> Run Colonist Redirect Self-Check` (`Assets/Editor/ColonistRedirectSelfCheck.cs`), registered in
`AllAISelfChecks` (10 suites). Built from a minimal `GameAIMap`/`Planet`/`PlayerAI` set, distinct positions, no assertion on a
float boundary. Covers: a clean remaining route is untouched; a detour keeps the target; a blocked target diverts; candidate
rules (empty, inbound, below max, own planet below max, excluded when full, unknown, current target); no candidate leaves the
order unchanged; rider and flags follow a divert (and the old flag stays when another colonist still targets it); the current
node at several progress values; arrival below max adds, at max docks a colony ship; a redirect never yields a zero-turn
delay; ordinary `IsValidColonizationTarget` is unchanged.

## Files

New: `ColonistRedirect.cs`, `ColonistRedirectSelfCheck.cs` (each with its `.meta`). Changed: `BlockadeSystem.cs`, `PlayerAI.cs`,
`GameAI.cs`, `AITuningLogger.cs`, `AllAISelfChecks.cs`, `CLAUDE.md`, `FUTURE_FEATURES.md`,
`.claude/skills/tuning-log/SKILL.md`.

## Accepted limits

- Edge progress is discarded on a redirect, so it can be a little slower than strictly necessary.
- A diverted colonist may land on a planet that became full in transit: it docks, by design.
- The view can be stale (a blockade lifted since it was seen); the colonist then detours or diverts needlessly.
- The existing zero-delay `Delayed` double-execution quirk is unchanged; redirected delays are clamped to at least 1.
