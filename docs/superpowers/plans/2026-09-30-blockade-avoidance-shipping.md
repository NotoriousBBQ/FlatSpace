# Blockade Avoidance for Resource Shipping Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Food and grotsits shipments plan around blockades the player can see, ship through unavoidable blockades only when something would still arrive, and are cancelled otherwise.

**Architecture:** `RoutePlanner` gains `PlanShipmentRoute` (clean route via the existing `PlanRoute`, else a least-loss route from a generalised shared Dijkstra core). `PlayerAI.BuildResourceMatrix` plans every (source, shortage) pair through it, drops pairs whose loss is at least the amount, and carries the route on the shipment order. `BlockadeSystem` also applies a shipment's origin blockade once on its first processed turn.

**Tech Stack:** Unity 6000.4.1f1, C#, Editor self-checks (`Assets/Editor/BlockadeAvoidanceSelfCheck.cs`), no automated test framework.

**Spec:** `docs/superpowers/specs/2026-09-30-blockade-avoidance-shipping-design.md` (builds on `2026-09-29-blockade-avoidance-colonization-design.md`).

## Global Constraints

- There is no command-line build or test runner. "Run the self-check" means: ask the user to focus the Unity Editor (so it recompiles), then run `FlatSpace → AI → Run Blockade Avoidance Self-Check` and read the Console. A compile error in the Editor counts as the "expected FAIL" for a test written before its implementation.
- Code that runs at initialization is not re-run by a recompile; the self-checks here build their own maps and need no Play-mode restart.
- Pure classes (`RoutePlanner`, `BlockadeView`, `BlockadeSystem`) never touch `Gameboard.Instance`; self-checks never depend on it either.
- A method a self-check calls directly is `public`, never `internal` (`Assets/Editor/` is a separate assembly).
- Self-check planets need distinct positions (`PathingSystem.FindPath` tie-break fragility).
- `OrderType` serializes as an int: add no new order types (none are needed).
- Range rule unchanged from colonization: the target must be in range by its **shortest** path (`2 <= NumNodes <= maxPathNodesForResourceDistribution`); only the route actually taken may exceed the limit.
- A shipment is cancelled when total loss `>=` the amount it would carry (`min(remainingSurplus, remainingShortage)`).
- Log lines: `T<turn>|P<playerId>|<Code>|<fields>`; new codes `ShipmentLossy|<origin>-><target>|<amount>|<loss>` and `ShipmentCancelled|<target>|Blockade`; `RouteDetour` is reused.
- Namespaces: match each file (`FlatSpace.AI` for the planner/system, `Flatspace.Objects.Resource` for `ResourceMatrix.cs`).
- Commit messages end with `Co-Authored-By: Claude Sonnet 5.5 <noreply@anthropic.com>`.

## Review Focus

Failure modes the spec implies that no single feature test would naturally cover; each has a pinned test in the task that owns the code:

1. A zero-delay `Delayed` shipment (`TotalDelay 0`, the known double-execution quirk) must not have its origin cut applied every turn — Task 2.
2. Unblockaded maps and a null view (before the first turn, and every older self-check) must behave exactly as before, with no throw — Tasks 1 and 3 (`PlayerAIResourceSelfCheck` re-run).
3. A shipment exactly at `loss == amount` is cancelled, one just above ships — Task 3.
4. A shipment loaded mid-flight (first processed turn already past) or with no carried route (older save) must not double-count the origin or throw — Task 2.
5. A planet that is both a shortage row and a surplus source (a DC) must still never ship to itself — Task 3 (the `s.Name != shortage.Name` guard is kept; `PlayerAIResourceSelfCheck` covers it).

---

### Task 1: Planner — loss, shared search core, `PlanShipmentRoute`

**Files:**
- Modify: `Assets/Flatspace/GameAI/RoutePlanner.cs`
- Modify: `Assets/Flatspace/GameAI/BlockadeView.cs` (add `WithValues`, a test factory like `Of`)
- Modify: `Assets/Editor/BlockadeAvoidanceSelfCheck.cs`

**Interfaces:**
- Produces: `RoutePlanner.PlannedRoute.Loss` (float); `RoutePlanner.PlanShipmentRoute(GameAIMap map, string origin, string target, BlockadeView view, int maxNodes)` returning `PlannedRoute` or null; `BlockadeView.WithValues(params (string name, float value)[] entries)`.
- Consumes: existing `RoutePlanner.PlanRoute`, `BlockadeView.Value/IsBlockaded`, `PathingSystem.Instance.PathNodes`, `GameAIMap.GetPath`.

- [ ] **Step 1: Write the failing test**

In `Assets/Editor/BlockadeAvoidanceSelfCheck.cs`, add `ok &= RunShipmentPlannerCheck();` after `ok &= RunShortestPathAvoidingCheck();` in `RunChecks`, and add this method after `RunShortestPathAvoidingCheck`:

```csharp
    // PlanShipmentRoute: a clean route when one exists (even beyond the node range), else the least-loss route; the
    // origin's own blockade counts as loss; an out-of-range source is never reachable.
    public static bool RunShipmentPlannerCheck()
    {
        var ok = true;
        var go = new GameObject("BASelfCheckMap_ShipPlanner");
        var constants = ScriptableObject.CreateInstance<GameAIConstants>();
        try
        {
            var map = BuildDiamond(go, constants);   // A to D: about 224 via B, about 361 via C

            var none = RoutePlanner.PlanShipmentRoute(map, "A", "D", null, 6);
            ok &= Check(Join(none) == "A>B>D" && !none.IsDetour && Near(none.Loss, 0f),
                "a null view: the ordinary shortest path with no loss");
            var clean = RoutePlanner.PlanShipmentRoute(map, "A", "D", BlockadeView.Of("B"), 6);
            ok &= Check(Join(clean) == "A>C>D" && clean.IsDetour && Near(clean.Loss, 0f),
                "B blockaded: the clean detour A>C>D, no loss");

            var tie = RoutePlanner.PlanShipmentRoute(map, "A", "D", BlockadeView.Of("B", "C"), 6);
            ok &= Check(Join(tie) == "A>B>D" && !tie.IsDetour && Near(tie.Loss, 1f),
                "both ways blockaded (1 each): equal loss, so the cheaper path A>B>D, loss 1, not a detour");
            var lighter = RoutePlanner.PlanShipmentRoute(map, "A", "D",
                BlockadeView.WithValues(("B", 2f), ("C", 1f)), 6);
            ok &= Check(Join(lighter) == "A>C>D" && lighter.IsDetour && Near(lighter.Loss, 1f),
                "B worth 2, C worth 1: the least-loss route is the longer A>C>D, loss 1");

            var target = RoutePlanner.PlanShipmentRoute(map, "A", "D", BlockadeView.Of("D"), 6);
            ok &= Check(Join(target) == "A>B>D" && Near(target.Loss, 1f),
                "a blockaded target is not refused: its value counts (loss 1)");
            var origin = RoutePlanner.PlanShipmentRoute(map, "A", "D", BlockadeView.Of("A"), 6);
            ok &= Check(Join(origin) == "A>B>D" && !origin.IsDetour && Near(origin.Loss, 1f),
                "a blockaded origin is not refused: its value counts (loss 1)");
            var all = RoutePlanner.PlanShipmentRoute(map, "A", "D",
                BlockadeView.WithValues(("A", 3f), ("B", 5f), ("C", 1f), ("D", 2f)), 6);
            ok &= Check(Join(all) == "A>C>D" && Near(all.Loss, 6f),
                "origin 3 + C 1 + target 2 = 6, cheaper than via B (3 + 5 + 2)");

            ok &= Check(RoutePlanner.PlanShipmentRoute(map, "A", "D", null, 2) == null,
                "a target beyond the node range (3 nodes, max 2) is unreachable");
            ok &= Check(RoutePlanner.PlanShipmentRoute(map, "A", "D", BlockadeView.Of("B", "C"), 2) == null,
                "a blockade does not bring an out-of-range target into range");
            ok &= Check(RoutePlanner.PlanShipmentRoute(map, "A", "Z", null, 6) == null
                        && RoutePlanner.PlanShipmentRoute(map, "A", "A", null, 6) == null
                        && RoutePlanner.PlanShipmentRoute(map, "A", "Nowhere", null, 6) == null,
                "the no-route stub, origin == target and unknown planets have no route");
        }
        finally
        {
            Object.DestroyImmediate(go);
            Object.DestroyImmediate(constants);
        }

        // A detour may exceed the node range: A - B - D is 3 nodes, the way round is 5.
        var go2 = new GameObject("BASelfCheckMap_ShipPlannerLong");
        var constants2 = ScriptableObject.CreateInstance<GameAIConstants>();
        try
        {
            var map = Build(go2, constants2,
                Spawn("A", 0f, 0f, new[] { "B", "C1" }),
                Spawn("B", 100f, 0f, new[] { "A", "D" }),
                Spawn("D", 200f, 0f, new[] { "B", "C3" }),
                Spawn("C1", 0f, 200f, new[] { "A", "C2" }),
                Spawn("C2", 100f, 300f, new[] { "C1", "C3" }),
                Spawn("C3", 200f, 200f, new[] { "C2", "D" }));
            var longWay = RoutePlanner.PlanShipmentRoute(map, "A", "D", BlockadeView.Of("B"), 3);
            ok &= Check(Join(longWay) == "A>C1>C2>C3>D" && longWay.NumNodes == 5 && Near(longWay.Loss, 0f),
                "with B blockaded the clean detour takes 5 nodes, beyond the maximum of 3, with no loss");
            var cheap = RoutePlanner.PlanShipmentRoute(map, "A", "D",
                BlockadeView.WithValues(("B", 1f), ("C2", 5f)), 3);
            ok &= Check(Join(cheap) == "A>B>D" && Near(cheap.Loss, 1f),
                "no clean route (B and C2 both blockaded): the least-loss route is through B, loss 1");
        }
        finally
        {
            Object.DestroyImmediate(go2);
            Object.DestroyImmediate(constants2);
        }
        return ok;
    }
```

- [ ] **Step 2: Run the self-check to verify it fails**

Ask the user to focus the Unity Editor. Expected: compile errors for `RoutePlanner.PlanShipmentRoute`, `BlockadeView.WithValues`, `PlannedRoute.Loss` (not defined yet).

- [ ] **Step 3: Implement**

In `Assets/Flatspace/GameAI/BlockadeView.cs`, add after `Of`:

```csharp
        /// <summary>A view with these blockade values (no blocker). For planning tests.</summary>
        public static BlockadeView WithValues(params (string name, float value)[] entries)
        {
            var view = new BlockadeView();
            foreach (var (name, value) in entries) view._blockaded[name] = (value, Planet.NoOwner);
            return view;
        }
```

Replace the whole of `Assets/Flatspace/GameAI/RoutePlanner.cs` with:

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using FlatSpace.Pathing;

namespace FlatSpace.AI
{
    /// <summary>
    /// Plans a route that avoids planets the player can SEE are blockaded against it. Pure (no Gameboard.Instance). The
    /// normal shortest path is used whenever it is clean, so behavior on unblockaded routes is exactly as before;
    /// otherwise a fresh Dijkstra runs (FindPath's A* tie-breaking is fragile, see CLAUDE.md, so it is left alone).
    /// Colonization uses PlanRoute (avoid or cancel); resource shipping uses PlanShipmentRoute (avoid, else the least-loss
    /// route).
    /// </summary>
    public static class RoutePlanner
    {
        public class PlannedRoute
        {
            public List<string> Nodes = new List<string>();   // origin to target inclusive
            public float Cost;
            public int NumNodes => Nodes.Count;
            public bool IsDetour;
            /// <summary>Total blockade value the route accrues (shipments only; always 0 for PlanRoute).</summary>
            public float Loss;
        }

        // Both planets known and different, and the shortest path is within range: 2 nodes or more (so FindPath's 1-node
        // no-route stub is never read as a free trip) and at most maxNodes.
        private static bool InRange(GameAIMap map, string origin, string target, int maxNodes)
        {
            if (origin == target) return false;
            var originPlanet = map.GetPlanet(origin);
            if (originPlanet == null || map.GetPlanet(target) == null) return false;
            return originPlanet.DistanceMapToPathingList.TryGetValue(target, out var entry)
                   && entry.NumNodes >= 2 && entry.NumNodes <= maxNodes;
        }

        /// <summary>
        /// The route from origin to target, or null when colonization must be cancelled: the target (or origin) is
        /// unknown or the same, the target is visibly blockaded, the target is out of range by node count or has only
        /// FindPath's 1-node no-route stub, or every way round the blockaded planets is closed. A target in range whose
        /// shortest path is blocked gets a detour, which may exceed maxNodes.
        /// </summary>
        public static PlannedRoute PlanRoute(GameAIMap map, string origin, string target, BlockadeView view, int maxNodes)
        {
            if (origin == target) return null;
            if (map.GetPlanet(origin) == null || map.GetPlanet(target) == null) return null;
            if (view != null && view.IsBlockaded(target)) return null;
            if (!InRange(map, origin, target, maxNodes)) return null;

            var shortest = map.GetPath(origin, target);
            var names = shortest.PathNodes.Select(n => n.Name).ToList();
            if (view == null || !names.Skip(1).Any(view.IsBlockaded))
                return new PlannedRoute { Nodes = names, Cost = shortest.Cost, IsDetour = false };

            return ShortestPathAvoiding(PathingSystem.Instance.PathNodes, origin, target,
                new HashSet<string>(view.BlockadedNames));
        }

        /// <summary>
        /// The route for a resource shipment, or null when it cannot be planned at all (out of range by its shortest
        /// path, the no-route stub, unknown planets). Unlike colonization a blockaded planet is not refused: a clean route
        /// (PlanRoute, which may detour beyond maxNodes) is used when one exists; otherwise the least-loss route, then the
        /// cheapest, is returned with `Loss` set to the total blockade value along it. The origin's own value counts as
        /// loss too. The caller decides whether the shipment still pays (loss must stay below the amount).
        /// </summary>
        public static PlannedRoute PlanShipmentRoute(GameAIMap map, string origin, string target, BlockadeView view,
            int maxNodes)
        {
            if (!InRange(map, origin, target, maxNodes)) return null;
            var originLoss = view != null ? view.Value(origin) : 0f;

            if (view == null || !view.IsBlockaded(target))
            {
                var clean = PlanRoute(map, origin, target, view, maxNodes);
                if (clean != null)
                {
                    clean.Loss = originLoss;
                    return clean;
                }
            }

            var best = Search(PathingSystem.Instance.PathNodes, origin, target, null,
                view != null ? (Func<string, float>)view.Value : null);
            if (best == null) return null;
            best.Loss += originLoss;
            best.IsDetour = !map.GetPath(origin, target).PathNodes.Select(n => n.Name).SequenceEqual(best.Nodes);
            return best;
        }

        /// <summary>
        /// Dijkstra over the explicit graph skipping every node in `blocked` (the origin is never expanded into, so it is
        /// never skipped). Ties in distance expand the lexicographically smaller node name first. Null when there is no
        /// path. The result is marked IsDetour.
        /// </summary>
        public static PlannedRoute ShortestPathAvoiding(IReadOnlyDictionary<string, PathNode> graph, string origin,
            string target, ISet<string> blocked)
            => Search(graph, origin, target, blocked, null);

        /// <summary>
        /// The shared search: Dijkstra minimising the pair (total node loss, total cost), ties broken by the smaller node
        /// name. `blocked` nodes are hard-skipped (never the origin); `nodeLoss` (optional) gives each node's blockade
        /// value, added for every node after the origin. Both keys are additive and non-negative, so the lexicographic
        /// order keeps Dijkstra correct. With no nodeLoss the loss key is always 0 and this is plain shortest-cost search.
        /// </summary>
        private static PlannedRoute Search(IReadOnlyDictionary<string, PathNode> graph, string origin, string target,
            ISet<string> blocked, Func<string, float> nodeLoss)
        {
            if (!graph.ContainsKey(origin) || !graph.ContainsKey(target)) return null;

            var loss = new Dictionary<string, float> { [origin] = 0f };
            var dist = new Dictionary<string, float> { [origin] = 0f };
            var previous = new Dictionary<string, string>();
            var done = new HashSet<string>();
            while (true)
            {
                string current = null;
                var bestLoss = float.MaxValue;
                var bestDist = float.MaxValue;
                foreach (var pair in dist)
                {
                    if (done.Contains(pair.Key)) continue;
                    var l = loss[pair.Key];
                    if (current == null || l < bestLoss
                        || (l == bestLoss && pair.Value < bestDist)
                        || (l == bestLoss && pair.Value == bestDist && string.CompareOrdinal(pair.Key, current) < 0))
                    {
                        current = pair.Key;
                        bestLoss = l;
                        bestDist = pair.Value;
                    }
                }
                if (current == null) return null;   // the reachable frontier is exhausted: no path
                if (current == target) break;

                done.Add(current);
                foreach (var edge in graph[current].Connections)
                {
                    var next = edge.NodeName;
                    if (done.Contains(next) || !graph.ContainsKey(next)) continue;
                    if (blocked != null && blocked.Contains(next)) continue;
                    var candidateLoss = loss[current] + (nodeLoss != null ? nodeLoss(next) : 0f);
                    var candidateDist = dist[current] + edge.Cost;
                    if (!dist.TryGetValue(next, out var existingDist)
                        || candidateLoss < loss[next]
                        || (candidateLoss == loss[next] && candidateDist < existingDist))
                    {
                        loss[next] = candidateLoss;
                        dist[next] = candidateDist;
                        previous[next] = current;
                    }
                }
            }

            var nodes = new List<string>();
            var at = target;
            while (at != null)
            {
                nodes.Add(at);
                at = previous.TryGetValue(at, out var parent) ? parent : null;
            }
            nodes.Reverse();
            return new PlannedRoute { Nodes = nodes, Cost = dist[target], Loss = loss[target], IsDetour = true };
        }
    }
}
```

- [ ] **Step 4: Run the self-check to verify it passes**

Ask the user to focus the Editor and run `FlatSpace → AI → Run Blockade Avoidance Self-Check`. Expected: `[BlockadeAvoidanceSelfCheck] ALL PASSED`. The existing `RunRoutePlannerCheck` and `RunShortestPathAvoidingCheck` (colonization regression for the refactored core) must still pass; a harmless red "planet 'Z' has no connections" error is expected (see the comment above `BuildDiamond`).

- [ ] **Step 5: Commit**

```bash
git add Assets/Flatspace/GameAI/RoutePlanner.cs Assets/Flatspace/GameAI/BlockadeView.cs Assets/Editor/BlockadeAvoidanceSelfCheck.cs
git commit -m "feat(blockade): shipment route planner with least-loss fallback

Co-Authored-By: Claude Sonnet 5.5 <noreply@anthropic.com>"
```

---

### Task 2: `BlockadeSystem` — a shipment's origin is cut once

**Files:**
- Modify: `Assets/Flatspace/GameAI/BlockadeSystem.cs:207-235` (`ApplyToShipment`)
- Modify: `Assets/Editor/BlockadeAvoidanceSelfCheck.cs`

**Interfaces:**
- Consumes: `BlockadeSystem.PassedNodes`, `RouteNode` (`Name`, `Fraction`), `Value`, `BlockadeCut`.
- Produces: no new API; `Apply` behaviour change for food/grotsits orders only.

- [ ] **Step 1: Write the failing test**

In `BlockadeAvoidanceSelfCheck.RunChecks` add `ok &= RunShipmentOriginCheck();` after `ok &= RunCarriedRouteCheck();`. Add after `RunCarriedRouteCheck`:

```csharp
    private static GameAI.GameAIOrder ShipmentOrder(string origin, string target, int timingDelay, int totalDelay,
        float amount, List<string> route)
        => new GameAI.GameAIOrder
        {
            Type = GameAI.GameAIOrder.OrderType.OrderTypeFoodTransport,
            TimingType = GameAI.GameAIOrder.OrderTimingType.OrderTimingTypeDelayed,
            TimingDelay = timingDelay, TotalDelay = totalDelay,
            Data = amount, Origin = origin, Target = target, PlayerId = 0,
            Route = route,
        };

    // The origin of a food/grotsits shipment is blockaded like any other node, once, on the first turn the order is
    // processed (TimingDelay + 1 >= TotalDelay, TotalDelay > 0). Colonists are unchanged.
    public static bool RunShipmentOriginCheck()
    {
        var ok = true;
        var go = new GameObject("BASelfCheckMap_ShipOrigin");
        var template = WarshipSelfCheck.MakeTemplate();
        var constants = WarshipSelfCheck.MakeConstants(template);
        var research = WarshipSelfCheck.MakeResearch();
        try
        {
            var map = BuildDiamond(go, constants);
            var blockade = new BlockadeSystem(map, research);
            var route = new List<string> { "A", "B", "D" };
            WarshipSelfCheck.DockWarships(map.GetPlanet("A"), 1, 1);   // 10 against player 0 at the origin A

            var order = ShipmentOrder("A", "D", 3, 4, 50f, route);
            var orders = new List<GameAI.GameAIOrder> { order };
            var cuts = blockade.Apply(orders, 1);
            ok &= Check(Near(System.Convert.ToSingle(order.Data), 40f) && cuts.Count == 1 && cuts[0].Planet == "A"
                        && Near(cuts[0].Value, 10f),
                "a shipment loses the origin's blockade value on its first processed turn (50 to 40) and reports the cut");
            order.TimingDelay = 2;
            blockade.Apply(orders, 2);
            ok &= Check(Near(System.Convert.ToSingle(order.Data), 40f), "the origin is cut once, not again on later turns");

            var small = ShipmentOrder("A", "D", 3, 4, 8f, route);
            map.GetPlanet("D").FoodShipmentIncoming = true;
            orders = new List<GameAI.GameAIOrder> { small };
            blockade.Apply(orders, 1);
            ok &= Check(orders.Count == 0 && !map.GetPlanet("D").FoodShipmentIncoming,
                "a shipment smaller than the origin's blockade is removed and the target's incoming flag cleared");

            orders = new List<GameAI.GameAIOrder> { ColonistOrder("A", "D", 3, 4, route) };
            blockade.Apply(orders, 1);
            ok &= Check(orders.Count == 1, "a colonist's origin is still never checked");

            var zero = ShipmentOrder("A", "D", 0, 0, 50f, route);
            orders = new List<GameAI.GameAIOrder> { zero };
            blockade.Apply(orders, 1);
            ok &= Check(Near(System.Convert.ToSingle(zero.Data), 50f),
                "a zero-delay order (the known double-execution quirk) never has its origin cut");

            var mid = ShipmentOrder("A", "D", 1, 4, 50f, route);
            orders = new List<GameAI.GameAIOrder> { mid };
            blockade.Apply(orders, 1);
            ok &= Check(Near(System.Convert.ToSingle(mid.Data), 50f),
                "an order already past its first turn (e.g. loaded mid-flight) is not cut at the origin");

            var older = ShipmentOrder("A", "D", 3, 4, 50f, null);
            orders = new List<GameAI.GameAIOrder> { older };
            blockade.Apply(orders, 1);
            ok &= Check(Near(System.Convert.ToSingle(older.Data), 40f),
                "an older shipment with no carried route still has its origin cut on its first turn");
        }
        finally
        {
            WarshipSelfCheck.DestroyAll(research);
            Object.DestroyImmediate(go);
            Object.DestroyImmediate(constants);
            Object.DestroyImmediate(template);
        }
        return ok;
    }
```

- [ ] **Step 2: Run the self-check to verify it fails**

Ask the user to focus the Editor and run the self-check. Expected: `FAIL: a shipment loses the origin's blockade value ...` (data stays 50) and the removal and "older shipment" checks fail too.

- [ ] **Step 3: Implement**

In `Assets/Flatspace/GameAI/BlockadeSystem.cs`, change the start of `ApplyToShipment` from

```csharp
            foreach (var node in PassedNodes(order))
            {
                var value = Value(_map.GetPlanet(node.Name), order.PlayerId, out var blocker);
                if (value <= 0f) continue;

                AITuningLogger.LogBlockade(turnNumber, order.PlayerId, node.Name, blocker, value);
                cuts.Add(new BlockadeCut { PlayerId = order.PlayerId, Planet = node.Name, Value = value });
                var remaining = System.Convert.ToSingle(order.Data) - value;
```

to

```csharp
            // A shipment's own origin is blockaded like any other node, once: on the first turn the order is processed
            // (its previous progress is 0). Colonists never check their origin (a blockaded colonizer is held back), and
            // RouteFor/PassedNodes stay shared with them. A zero-delay order (the known double-execution quirk) is skipped
            // so its stale copy cannot be cut at the origin every turn.
            var nodes = new List<RouteNode>();
            if (order.TotalDelay > 0 && order.TimingDelay + 1 >= order.TotalDelay)
                nodes.Add(new RouteNode { Name = order.Origin, Fraction = 0f });
            nodes.AddRange(PassedNodes(order));

            foreach (var node in nodes)
            {
                var value = Value(_map.GetPlanet(node.Name), order.PlayerId, out var blocker);
                if (value <= 0f) continue;

                AITuningLogger.LogBlockade(turnNumber, order.PlayerId, node.Name, blocker, value);
                cuts.Add(new BlockadeCut { PlayerId = order.PlayerId, Planet = node.Name, Value = value });
                var remaining = System.Convert.ToSingle(order.Data) - value;
```

(The rest of the method is unchanged. If `RouteNode` is a struct or class with other required members, construct it the same way `Route()` does: `new RouteNode { Name = ..., Fraction = ... }`.)

- [ ] **Step 4: Run the self-check to verify it passes**

Expected: `ALL PASSED`, including the pre-existing `RunCarriedRouteCheck`, `RunBlockadeCutsCheck` and the `WarshipSelfCheck` blockade checks (run `FlatSpace → AI → Run All AI Self-Checks` to be sure no existing shipment-blockade check assumed an unchecked origin; if one fails because its shipment starts at a blockaded origin, update that check's expectation and say so in the commit).

- [ ] **Step 5: Commit**

```bash
git add Assets/Flatspace/GameAI/BlockadeSystem.cs Assets/Editor/BlockadeAvoidanceSelfCheck.cs
git commit -m "feat(blockade): shipments are cut at a blockaded origin on their first turn

Co-Authored-By: Claude Sonnet 5.5 <noreply@anthropic.com>"
```

---

### Task 3: Shipment planning in `PlayerAI`

**Files:**
- Modify: `Assets/Flatspace/GameAI/ResourceMatrix.cs` (route/loss on the choice and action)
- Modify: `Assets/Flatspace/GameAI/PlayerAI.cs` (`BuildResourceMatrix`, `ProcessResourceShipments`, `EmitResourceOrders`, `ProcessFoodShortage`/`ProcessGrotsitsShortage` visibility, held-back state, comparer)
- Modify: `Assets/Flatspace/Diagnostics/AITuningLogger.cs` (two new log methods)
- Modify: `Assets/Editor/BlockadeAvoidanceSelfCheck.cs`

**Interfaces:**
- Consumes: `RoutePlanner.PlanShipmentRoute` and `PlannedRoute.Nodes/Cost/Loss/IsDetour` (Task 1); the origin cut (Task 2) makes the planned loss real.
- Produces: `ResourceChoiceElement.Route/Loss/IsDetour`; `ResourceAction.Route/Loss/IsDetour`; `PlayerAI.CompareResourceChoices(ResourceChoiceElement, ResourceChoiceElement)` (public static); `PlayerAI.ProcessFoodShortage` and `ProcessGrotsitsShortage` now `public`; `PlayerAI.ShipmentHeldBackReason(GameAI.GameAIOrder.OrderType transportType, string target)` and `NoteShipmentHeldBack(GameAI.GameAIOrder.OrderType transportType, string target, string reason)`; `AITuningLogger.LogShipmentLossy`, `LogShipmentCancelled`.

- [ ] **Step 1: Write the failing test**

In `BlockadeAvoidanceSelfCheck`:

1. Add `using Flatspace.Objects.Resource;` at the top, and `ok &= RunShipmentPlanningCheck();` after `ok &= RunColonizationRoutingCheck();` in `RunChecks`.
2. In `Scenario.Diamond()` add `s.Constants.maxPathNodesForResourceDistribution = 6;` next to the other constants lines.
3. Add to `Scenario` (beside `Colonize`):

```csharp
        public static Scenario Long()
        {
            var s = new Scenario();
            s.Template = WarshipSelfCheck.MakeTemplate();
            s.Constants = WarshipSelfCheck.MakeConstants(s.Template);
            s.Constants.defaultTravelSpeed = 1f;
            s.Constants.maxPathNodesForResourceDistribution = 3;   // the direct route A>B>D is 3 nodes; the way round is 5
            s.Constants.maxPathNodesForKnowledge = 6;
            s.Research = WarshipSelfCheck.MakeResearch();
            s.MapGo = new GameObject("BASelfCheckMap_ScenarioLong");
            s.PlayerGo = new GameObject("BASelfCheckPlayer_ScenarioLong");
            s.Map = Build(s.MapGo, s.Constants,
                Spawn("A", 0f, 0f, new[] { "B", "C1" }),
                Spawn("B", 100f, 0f, new[] { "A", "D" }),
                Spawn("D", 200f, 0f, new[] { "B", "C3" }),
                Spawn("C1", 0f, 200f, new[] { "A", "C2" }),
                Spawn("C2", 100f, 300f, new[] { "C1", "C3" }),
                Spawn("C3", 200f, 200f, new[] { "C2", "D" }));

            var a = s.Map.GetPlanet("A");
            a.Owner = 0;
            for (var i = 0; i < 5; i++) a.Population.Add(new Planet.Inhabitant { Player = 0 });

            var player = s.PlayerGo.AddComponent<Player>();
            s.AI = s.PlayerGo.AddComponent<PlayerAI>();
            s.AI.Player = player;
            s.AI.AIMap = s.Map;
            player.playerID = 0;
            s.AI.ResearchCatalog = s.PlayerGo.AddComponent<Catalog>();
            s.AI.ResearchCatalog.catalogItems = s.Research;
            s.Map.Knowledge.Update(s.Map, 1, 6);
            return s;
        }

        // A populated A (surplus `surplus` food) ships to D (shortage `shortage` food): the food shipment orders.
        public List<GameAI.GameAIOrder> ShipFood(float shortage, float surplus = 100f)
        {
            var results = new List<Planet.PlanetUpdateResult>
            {
                new Planet.PlanetUpdateResult("D",
                    Planet.PlanetUpdateResult.PlanetUpdateResultType.PlanetUpdateResultTypeFoodShortage, -shortage, 0),
                new Planet.PlanetUpdateResult("A",
                    Planet.PlanetUpdateResult.PlanetUpdateResultType.PlanetUpdateResultTypeFoodSurplus, surplus, 0),
            };
            var orders = new List<GameAI.GameAIOrder>();
            AI.ProcessFoodShortage(results, orders);
            return orders;
        }
```

4. Add these methods:

```csharp
    private static GameAI.GameAIOrder FoodShipment(List<GameAI.GameAIOrder> orders)
        => orders.Find(o => o.Type == GameAI.GameAIOrder.OrderType.OrderTypeFoodTransport);

    private static float FoodDeducted(List<GameAI.GameAIOrder> orders)
    {
        var change = orders.Find(o => o.Type == GameAI.GameAIOrder.OrderType.OrderTypeFoodChange);
        return change == null ? 0f : System.Convert.ToSingle(change.Data);
    }

    public static bool RunShipmentPlanningCheck()
    {
        var ok = true;
        const GameAI.GameAIOrder.OrderType food = GameAI.GameAIOrder.OrderType.OrderTypeFoodTransport;
        using (var s = Scenario.Diamond())
        {
            // No view yet (null): nothing is blockaded, shipping is exactly as before.
            s.Blockade("B");
            var before = FoodShipment(s.ShipFood(50f));
            ok &= Check(before != null && string.Join(">", before.Route) == "A>B>D",
                "with no view yet, a blockade at B is unknown: the shipment takes the shortest route A>B>D");
            s.Unblockade("B");

            // Clean: the shortest route rides on the order; the delay comes from its cost.
            s.AI.RefreshBlockadeView();
            var cleanOrders = s.ShipFood(50f);
            var clean = FoodShipment(cleanOrders);
            var cleanRoute = RoutePlanner.PlanShipmentRoute(s.Map, "A", "D", s.AI.CurrentBlockadeView, 6);
            ok &= Check(clean != null && string.Join(">", clean.Route) == "A>B>D"
                        && clean.TotalDelay == System.Convert.ToInt32(cleanRoute.Cost) && Near(FoodDeducted(cleanOrders), -50f),
                "unblockaded: the shipment carries A>B>D, its delay comes from the route cost, the origin is charged 50");

            // B blockaded (visible): a clean detour through C, longer delay, nothing lost.
            s.Blockade("B");
            s.AI.RefreshBlockadeView();
            var detoured = FoodShipment(s.ShipFood(50f));
            ok &= Check(detoured != null && string.Join(">", detoured.Route) == "A>C>D" && detoured.TotalDelay > clean.TotalDelay,
                "B blockaded: the shipment detours A>C>D and the longer trip has a longer delay");

            // Both ways blockaded: least loss wins even though it is the longer way (B worth 20, C worth 10).
            s.Blockade("B");                 // B now holds 2 warships
            s.Blockade("C");
            s.AI.RefreshBlockadeView();
            var lossyOrders = s.ShipFood(50f);
            var lossy = FoodShipment(lossyOrders);
            ok &= Check(lossy != null && string.Join(">", lossy.Route) == "A>C>D" && Near(FoodDeducted(lossyOrders), -50f),
                "no clean route: the least-loss route A>C>D (10 against B's 20) is taken and the full 50 is sent");

            // loss >= amount cancels; the state is noted once; a shipment just above the loss ships.
            s.Unblockade("B");
            s.Blockade("B");                 // B and C now 10 each: every route loses 10
            s.AI.RefreshBlockadeView();
            ok &= Check(s.ShipFood(8f).Count == 0, "a shipment smaller than the unavoidable loss (8 against 10) is cancelled");
            ok &= Check(s.AI.ShipmentHeldBackReason(food, "D") == "Blockade", "the cancelled shortage is remembered as held back");
            ok &= Check(!s.AI.NoteShipmentHeldBack(food, "D", "Blockade") && s.AI.NoteShipmentHeldBack(food, "D", "Other"),
                "the held-back note is news only when the reason changes (so the log line is not repeated every turn)");
            ok &= Check(s.ShipFood(10f).Count == 0, "a shipment exactly equal to the loss (10 against 10) is cancelled: nothing would arrive");
            ok &= Check(FoodShipment(s.ShipFood(11f)) != null, "a shipment just above the loss (11 against 10) still ships");
            ok &= Check(s.AI.ShipmentHeldBackReason(food, "D") == null, "a shipment that goes out clears the held-back state");

            // A way opens: it ships again.
            s.Unblockade("C");
            s.AI.RefreshBlockadeView();
            ok &= Check(string.Join(">", FoodShipment(s.ShipFood(8f)).Route) == "A>C>D",
                "when a clean way opens, even a small shipment goes");
            s.Unblockade("B");

            // A blockaded SOURCE is not dropped: its value is loss. 8 is lost entirely, 50 ships.
            s.Blockade("A");
            s.AI.RefreshBlockadeView();
            ok &= Check(s.ShipFood(8f).Count == 0, "a blockaded source (10) cancels a shipment of 8");
            var fromBlockaded = FoodShipment(s.ShipFood(50f));
            ok &= Check(fromBlockaded != null && string.Join(">", fromBlockaded.Route) == "A>B>D",
                "a blockaded source still ships a shipment larger than its blockade, by the ordinary route");
            s.Unblockade("A");

            // A blockaded TARGET is not dropped either: give player 0 presence at C so D is visible.
            s.Map.GetPlanet("C").DockShipFromSave(Ship.ShipKind.ColonyShip, 0, new List<string>());
            s.Blockade("D");
            s.AI.RefreshBlockadeView();
            ok &= Check(s.AI.CurrentBlockadeView.IsBlockaded("D"), "D is visible and blockaded");
            ok &= Check(s.ShipFood(8f).Count == 0, "a blockaded target (10) cancels a shipment of 8");
            ok &= Check(FoodShipment(s.ShipFood(50f)) != null, "a blockaded target still receives a shipment larger than its blockade");
            s.Unblockade("D");

            // Range: D is 3 nodes from A; with a maximum of 2 nothing is reachable, detour or not.
            s.Constants.maxPathNodesForResourceDistribution = 2;
            s.AI.RefreshBlockadeView();
            ok &= Check(s.ShipFood(50f).Count == 0, "a target beyond the node range is not shipped to");
            s.Blockade("B");
            s.AI.RefreshBlockadeView();
            ok &= Check(s.ShipFood(50f).Count == 0, "a blockade does not bring an out-of-range target into range");
        }

        // The detour may exceed the node range: direct A>B>D is 3 nodes (the maximum), the clean way round is 5.
        using (var s = Scenario.Long())
        {
            s.Blockade("B");
            s.AI.RefreshBlockadeView();
            var around = FoodShipment(s.ShipFood(50f));
            ok &= Check(around != null && string.Join(">", around.Route) == "A>C1>C2>C3>D",
                "a clean detour of 5 nodes is taken although the maximum is 3");
        }

        // Clean routes sort before lossy ones; otherwise by cost minus surplus as before.
        var surplusPlanet = new Planet.PlanetUpdateResult("X",
            Planet.PlanetUpdateResult.PlanetUpdateResultType.PlanetUpdateResultTypeFoodSurplus, 10f, 0);
        var cleanFar = new ResourceChoiceElement { SurplusResult = surplusPlanet, Cost = 500f, Loss = 0f };
        var lossyNear = new ResourceChoiceElement { SurplusResult = surplusPlanet, Cost = 100f, Loss = 5f };
        var cleanNear = new ResourceChoiceElement { SurplusResult = surplusPlanet, Cost = 100f, Loss = 0f };
        ok &= Check(PlayerAI.CompareResourceChoices(cleanFar, lossyNear) < 0
                    && PlayerAI.CompareResourceChoices(lossyNear, cleanFar) > 0,
            "a clean far source sorts before a lossy near one");
        ok &= Check(PlayerAI.CompareResourceChoices(cleanNear, cleanFar) < 0,
            "between clean routes the cheaper sorts first (the existing order)");
        return ok;
    }
```

- [ ] **Step 2: Run the self-check to verify it fails**

Expected: compile errors (`ProcessFoodShortage` inaccessible, `ShipmentHeldBackReason`, `NoteShipmentHeldBack`, `ResourceChoiceElement.Loss/Cost` init, `CompareResourceChoices`).

- [ ] **Step 3: Implement**

**3a. `ResourceMatrix.cs`** — add `using System.Collections.Generic;` and, in `ResourceChoiceElement` after `Cost`:

```csharp
        /// <summary>The planned route, source to target inclusive (null in tests that do not plan one).</summary>
        public List<string> Route { get; set; }
        /// <summary>Total blockade value along the route, origin included; 0 for a clean route.</summary>
        public float Loss { get; set; }
        /// <summary>True when the route is not the ordinary shortest path (for the RouteDetour log line).</summary>
        public bool IsDetour { get; set; }
```

and in `ResourceAction` after `Cost`:

```csharp
        public List<string> Route => ChosenChoiceElement.Route;
        public float Loss => ChosenChoiceElement.Loss;
        public bool IsDetour => ChosenChoiceElement.IsDetour;
```

**3b. `AITuningLogger.cs`** — after `LogRouteDetour`:

```csharp
    /// <summary>A shipment was sent through unavoidable blockades: T&lt;turn&gt;|P&lt;id&gt;|ShipmentLossy|origin-&gt;target|amount|loss.</summary>
    public static void LogShipmentLossy(int turnNumber, int playerId, string origin, string target, float amount, float loss)
    {
        if (_currentLogPath == null) return;
        AppendLines(new List<string> { FormatLine(turnNumber, playerId, "ShipmentLossy", $"{origin}->{target}",
            amount.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture),
            loss.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture)) });
    }

    /// <summary>A shortage could not be supplied because blockades removed every source: T&lt;turn&gt;|P&lt;id&gt;|ShipmentCancelled|target|Blockade. Logged on state change only.</summary>
    public static void LogShipmentCancelled(int turnNumber, int playerId, string target, string reason)
    {
        if (_currentLogPath == null) return;
        AppendLines(new List<string> { FormatLine(turnNumber, playerId, "ShipmentCancelled", target, reason) });
    }
```

**3c. `PlayerAI.cs`:**

1. Make `ProcessFoodShortage` and `ProcessGrotsitsShortage` `public` (add the comment `// public for the self-check` above each signature).

2. Beside `_colonizeHeldBack` add:

```csharp
            // Why each shortage last went unsupplied ("<transportType>:<target>" -> reason), so ShipmentCancelled is logged on
            // a change and not every turn the blockade holds. Log-only state: not saved.
            private readonly Dictionary<string, string> _shipmentHeldBack = new Dictionary<string, string>();

            private static string ShipmentKey(GameAI.GameAIOrder.OrderType transportType, string target)
                => $"{transportType}:{target}";

            /// <summary>The reason this shortage was last left unsupplied by blockades, or null. Public for the self-check.</summary>
            public string ShipmentHeldBackReason(GameAI.GameAIOrder.OrderType transportType, string target)
                => _shipmentHeldBack.TryGetValue(ShipmentKey(transportType, target), out var reason) ? reason : null;

            /// <summary>True when this is news (first turn held back, or a different reason), i.e. the caller should log it. Public for the self-check.</summary>
            public bool NoteShipmentHeldBack(GameAI.GameAIOrder.OrderType transportType, string target, string reason)
            {
                var key = ShipmentKey(transportType, target);
                if (_shipmentHeldBack.TryGetValue(key, out var last) && last == reason) return false;
                _shipmentHeldBack[key] = reason;
                return true;
            }
```

3. Add the comparer next to `DistributionCenterPrioritySentinel`:

```csharp
            /// <summary>
            /// Choice order within a shortage row: clean routes before lossy ones (a nearer source that loses part of the
            /// shipment never beats a clean one), then the ScoreMatrix default, cost minus surplus. Public for the self-check.
            /// </summary>
            public static int CompareResourceChoices(ResourceChoiceElement x, ResourceChoiceElement y)
            {
                var lossy = (x.Loss > 0f).CompareTo(y.Loss > 0f);
                return lossy != 0 ? lossy : (x.Cost - x.Surplus).CompareTo(y.Cost - y.Surplus);
            }
```

4. In `ProcessResourceShipments`: replace the rounds loop (from `var maxRounds` through the closing brace of the `for`) with:

```csharp
                var maxRounds = shortages.Count + surplusResults.Count;
                var blockedRows = new HashSet<string>();   // rows a blockade removed every source from (in any round)
                var servedRows = new HashSet<string>();
                for (var round = 0; round < maxRounds; round++)
                {
                    var matrix = BuildResourceMatrix(shortages, surplusResults, remainingShortage, remainingSurplus,
                        syntheticShortageNames, blockedRows);
                    if (matrix == null) break;

                    var actions = matrix.GenerateActionList(
                        actionFactory: (origin, element) => new ResourceAction { ChosenChoiceElement = element },
                        ChoiceCompare: CompareResourceChoices);
                    if (actions.Count == 0) break;

                    foreach (var action in actions)
                    {
                        var amount = Mathf.Min(remainingSurplus[action.Origin], remainingShortage[action.Target]);
                        if (amount <= 0f) continue;

                        EmitResourceOrders(action, amount, transportType, changeType, inProgressType, orders);
                        remainingSurplus[action.Origin]  -= amount;
                        remainingShortage[action.Target] -= amount;
                        servedRows.Add(action.Target);
                    }
                }

                // ShipmentCancelled is logged when a shortage's blocked state changes, not on every turn it holds.
                var turn = Gameboard.Instance != null ? Gameboard.Instance.TurnNumber : 0;
                foreach (var shortage in shortages)
                {
                    if (!servedRows.Contains(shortage.Name) && blockedRows.Contains(shortage.Name))
                    {
                        if (NoteShipmentHeldBack(transportType, shortage.Name, "Blockade"))
                            AITuningLogger.LogShipmentCancelled(turn, Player.playerID, shortage.Name, "Blockade");
                    }
                    else
                    {
                        _shipmentHeldBack.Remove(ShipmentKey(transportType, shortage.Name));
                    }
                }
```

5. In `BuildResourceMatrix`: add the parameter `HashSet<string> blockedRows` after `syntheticShortageNames`, and replace the body from `var pathMap = ...` through `.ToList();` (the `entries` computation, including its long comment) with:

```csharp
                    var maxNodes = AIMap.GameAIConstants.maxPathNodesForResourceDistribution;
                    // Every (source, shortage) pair is planned through RoutePlanner.PlanShipmentRoute (range rule included:
                    // the shortest path must be 2..maxNodes nodes, which also rejects FindPath's no-route stub; the route
                    // actually taken may be longer). s.Name != shortage.Name stays: a DC can be BOTH this shortage row
                    // (synthetic demand) AND a real surplus source the same turn, and must never ship to itself.
                    // A blockaded source or target is not dropped: its value is part of the route's Loss, and the pair is
                    // offered only while Loss < the amount it would carry (a shipment that would arrive with 0 is cancelled).
                    var entries = new List<ResourceChoiceElement>();
                    var droppedByBlockade = false;
                    foreach (var s in surplusResults)
                    {
                        if (s.Name == shortage.Name || remainingSurplus[s.Name] <= 0f) continue;
                        var route = RoutePlanner.PlanShipmentRoute(AIMap, s.Name, shortage.Name, _blockadeView, maxNodes);
                        if (route == null) continue;
                        var amount = Mathf.Min(remainingSurplus[s.Name], remainingShortage[shortage.Name]);
                        if (route.Loss >= amount)
                        {
                            droppedByBlockade = true;
                            continue;
                        }
                        entries.Add(new ResourceChoiceElement
                        {
                            SurplusResult = s,
                            ShortageResult = shortage,
                            Cost = route.Cost,
                            Route = route.Nodes,
                            Loss = route.Loss,
                            IsDetour = route.IsDetour,
                        });
                    }
                    if (entries.Count == 0 && droppedByBlockade) blockedRows.Add(shortage.Name);
```

   and change the `matrix` declaration's unaffected; leave the `if (entries.Count > 0) { var decision = ... }` block as is.

6. In `EmitResourceOrders`, replace the first `orders.Add(MakeOrder(transportType, ...))` statement with:

```csharp
                var transport = MakeOrder(transportType,
                    GameAI.GameAIOrder.OrderTimingType.OrderTimingTypeDelayed,
                    delay, delay, amount, action.Origin, action.Target);
                if (action.Route != null) transport.Route = new List<string>(action.Route);
                orders.Add(transport);

                var turn = Gameboard.Instance != null ? Gameboard.Instance.TurnNumber : 0;
                if (action.IsDetour)
                    AITuningLogger.LogRouteDetour(turn, Player.playerID, action.Origin, action.Target, action.Route, action.Cost);
                if (action.Loss > 0f)
                    AITuningLogger.LogShipmentLossy(turn, Player.playerID, action.Origin, action.Target, amount, action.Loss);
```

- [ ] **Step 4: Run the self-checks to verify they pass**

Ask the user to focus the Editor and run `FlatSpace → AI → Run All AI Self-Checks`. Expected: every suite `ALL PASSED`. Pay particular attention to `PlayerAIResourceSelfCheck` (shipping behaviour on unblockaded maps, including the DC-also-surplus regression, is the main regression here) and `DistributionCenterSelfCheck`. If `PlayerAIResourceSelfCheck` fails on a path-length or ordering expectation, compare against `git stash` of this task: the only intended differences are the source-to-target route direction (path costs are symmetric) and clean-before-lossy ordering.

- [ ] **Step 5: Commit**

```bash
git add Assets/Flatspace/GameAI/ResourceMatrix.cs Assets/Flatspace/GameAI/PlayerAI.cs Assets/Flatspace/Diagnostics/AITuningLogger.cs Assets/Editor/BlockadeAvoidanceSelfCheck.cs
git commit -m "feat(blockade): resource shipments route around blockades or ship only what survives

Co-Authored-By: Claude Sonnet 5.5 <noreply@anthropic.com>"
```

---

### Task 4: Docs, tuning-log skill, and Play-mode verification

**Files:**
- Modify: `CLAUDE.md` (Warships and Blockade section; AI Tuning Log section)
- Modify: `FUTURE_FEATURES.md:9` (sub-project 2 done)
- Modify: `.claude/skills/tuning-log/SKILL.md`
- Modify: `docs/superpowers/specs/2026-09-30-blockade-avoidance-shipping-design.md` (origin rule refinement)

- [ ] **Step 1: `CLAUDE.md`**

In "Warships and Blockade", change "The origin is never checked." to: "The origin is never checked for colonist orders; a food/grotsits shipment's origin is checked once, on the first turn the order is processed (`TotalDelay > 0` and `TimingDelay + 1 >= TotalDelay`; a zero-delay or mid-flight order is skipped)." Then add after the "Blockade avoidance (colonization)" bullet a bullet **Blockade avoidance (resource shipping)** covering: `RoutePlanner.PlanShipmentRoute` (clean route via `PlanRoute` else least-loss route from the shared `Search` core, origin and target blockades count, range rule on the shortest path only, detours may exceed `maxPathNodesForResourceDistribution`); that `BuildResourceMatrix` plans every (source, shortage) pair and drops it when `Loss >= amount` (a blockaded source or target is ordinary, not dropped outright); clean routes sort before lossy ones (`PlayerAI.CompareResourceChoices`); shipment transport orders carry `Route` and the delay uses the route cost; the `ShipmentLossy` and `ShipmentCancelled` log lines (cancelled on state change only, via `NoteShipmentHeldBack`); `RouteDetour` is shared with colonists; and that the origin cut means economy numbers are not comparable with earlier runs on boards with lasting blockades. In "AI Tuning Log", add the two new lines to the event list. In Tests, mention `BlockadeAvoidanceSelfCheck` now also covers shipment planning. Remove "Resource shipping and assault are not blockade-aware yet." and replace with "Assault is not blockade-aware yet."

- [ ] **Step 2: `FUTURE_FEATURES.md`**

Change item (2) to start with `*Done: ...*` in the style of item (1), e.g. "(2) **Resource shipping** — *done: shipments route around visible blockaded planets (detours may exceed the maximum range), ship through unavoidable blockades only when the amount exceeds the total loss (the source's and target's blockades count like any other planet), otherwise cancel; see CLAUDE.md "Warships and Blockade".*" Update the milestone line to say sub-projects (3) to (5) remain.

- [ ] **Step 3: `tuning-log` skill**

Read `.claude/skills/tuning-log/SKILL.md`, then add to its event-code list `ShipmentLossy` and `ShipmentCancelled`, and to its blockade metrics: count `OrderBlocked` for `OrderTypeFoodTransport`/`OrderTypeGrotsitsTransport` (expected to fall relative to a pre-change run), `ShipmentLossy` lines, `ShipmentCancelled` lines, and note that `RouteDetour` lines now come from both colonists and shipments and that a run on a board with lasting blockades is not directly comparable with earlier runs because shipments are now cut at a blockaded origin.

- [ ] **Step 4: Spec refinement**

In the spec's section 4, change "on the first turn the order is processed (the turn its previous progress is 0: `TimingDelay + 1 >= TotalDelay`)" to add "and `TotalDelay > 0`, so a zero-delay order (the known double-execution quirk) is never cut at its origin".

- [ ] **Step 5: Play-mode verification (user-run)**

Ask the user to enable `_logAIEvents` on the `MainMenu` component, play a `test2.json` (three players, about 400 turns) and/or `4p.json` match, then run `/tuning-log`. Expected: `OrderBlocked` lines for food/grotsits fall versus the last comparable log; `ShipmentLossy` and `ShipmentCancelled` appear only where a blockade was visible; shipments still arrive at their usual rate. Report any shipment reduced at a node the player could see.

- [ ] **Step 6: Commit**

```bash
git add CLAUDE.md FUTURE_FEATURES.md .claude/skills/tuning-log/SKILL.md docs/superpowers/specs/2026-09-30-blockade-avoidance-shipping-design.md
git commit -m "docs: document blockade avoidance for resource shipping

Co-Authored-By: Claude Sonnet 5.5 <noreply@anthropic.com>"
```
