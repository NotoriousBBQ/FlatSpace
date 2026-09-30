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
