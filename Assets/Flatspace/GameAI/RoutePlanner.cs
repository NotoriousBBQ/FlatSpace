using System.Collections.Generic;
using System.Linq;
using FlatSpace.Pathing;

namespace FlatSpace.AI
{
    /// <summary>
    /// Plans a colonization route that avoids planets the player can SEE are blockaded against it. Pure (no
    /// Gameboard.Instance). The normal shortest path is used whenever it is clean, so behavior on unblockaded routes is
    /// exactly as before; otherwise a fresh Dijkstra skips the blockaded planets (FindPath's A* tie-breaking is fragile,
    /// see CLAUDE.md, so it is left alone).
    /// </summary>
    public static class RoutePlanner
    {
        public class PlannedRoute
        {
            public List<string> Nodes = new List<string>();   // origin to target inclusive
            public float Cost;
            public int NumNodes => Nodes.Count;
            public bool IsDetour;
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
            var originPlanet = map.GetPlanet(origin);
            if (originPlanet == null || map.GetPlanet(target) == null) return null;
            if (view != null && view.IsBlockaded(target)) return null;
            if (!originPlanet.DistanceMapToPathingList.TryGetValue(target, out var entry)) return null;
            // 1 node is the no-route stub (unreachable); more than maxNodes is beyond the normal range.
            if (entry.NumNodes < 2 || entry.NumNodes > maxNodes) return null;

            var shortest = map.GetPath(origin, target);
            var names = shortest.PathNodes.Select(n => n.Name).ToList();
            if (view == null || !names.Skip(1).Any(view.IsBlockaded))
                return new PlannedRoute { Nodes = names, Cost = shortest.Cost, IsDetour = false };

            return ShortestPathAvoiding(PathingSystem.Instance.PathNodes, origin, target,
                new HashSet<string>(view.BlockadedNames));
        }

        /// <summary>
        /// Dijkstra over the explicit graph skipping every node in `blocked` (the origin is never expanded into, so it is
        /// never skipped). Ties in distance expand the lexicographically smaller node name first. Null when there is no
        /// path. The result is marked IsDetour.
        /// </summary>
        public static PlannedRoute ShortestPathAvoiding(IReadOnlyDictionary<string, PathNode> graph, string origin,
            string target, ISet<string> blocked)
        {
            if (!graph.ContainsKey(origin) || !graph.ContainsKey(target)) return null;

            var dist = new Dictionary<string, float> { [origin] = 0f };
            var previous = new Dictionary<string, string>();
            var done = new HashSet<string>();
            while (true)
            {
                string current = null;
                var best = float.MaxValue;
                foreach (var pair in dist)
                {
                    if (done.Contains(pair.Key)) continue;
                    if (current == null || pair.Value < best
                        || (pair.Value == best && string.CompareOrdinal(pair.Key, current) < 0))
                    {
                        current = pair.Key;
                        best = pair.Value;
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
                    var candidate = dist[current] + edge.Cost;
                    if (!dist.TryGetValue(next, out var existing) || candidate < existing)
                    {
                        dist[next] = candidate;
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
            return new PlannedRoute { Nodes = nodes, Cost = dist[target], IsDetour = true };
        }
    }
}
