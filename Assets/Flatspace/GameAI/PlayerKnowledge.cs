using System;
using System.Collections.Generic;

namespace FlatSpace
{
    namespace AI
    {
        /// <summary>
        /// Per-player, sticky "has this player ever discovered planet X" tracker. A planet becomes
        /// known when it is a vision source for that player (population or a docked ship) or when
        /// it is a direct neighbour (GameAIMap.GetNeighbours, symmetrized Planet.Connections) of a
        /// source planet. Knowledge is never removed once granted.
        /// </summary>
        public class PlayerKnowledge
        {
            private readonly Dictionary<int, HashSet<string>> _known = new Dictionary<int, HashSet<string>>();

            public bool IsKnown(int playerId, string planetName) =>
                _known.TryGetValue(playerId, out var set) && set.Contains(planetName);

            public IReadOnlyCollection<string> KnownPlanets(int playerId) =>
                _known.TryGetValue(playerId, out var set) ? set : Array.Empty<string>();

            /// <summary>
            /// True when any planet this player knows holds another player's population or docked
            /// ship. Reads Planet.Population / DockedShips directly (not Planet.Owner, which is
            /// NoOwner on a population tie) and needs no player count or Gameboard.Instance.
            /// </summary>
            public bool HasContact(GameAIMap map, int playerId)
            {
                foreach (var name in KnownPlanets(playerId))
                {
                    var planet = map.GetPlanet(name);
                    if (planet == null) continue;
                    if (planet.Population.Exists(p => p.Player != playerId)) return true;
                    if (planet.DockedShips.Exists(s => s.Owner != playerId)) return true;
                }
                return false;
            }

            /// <summary>
            /// Grants knowledge out to maxPathNodesForKnowledge path nodes from each vision source: a BFS
            /// over GetNeighbours' graph adjacency, converting NumNodes to a hop count (NumNodes - 1). The
            /// default of 2 reproduces this method's original behavior (source + direct neighbours only),
            /// so every existing call site that only passes (map, numPlayers) is unaffected.
            /// </summary>
            public void Update(GameAIMap map, int numPlayers, int maxPathNodesForKnowledge = 2)
            {
                var hops = Math.Max(0, maxPathNodesForKnowledge - 1);
                for (var p = 0; p < numPlayers; p++)
                {
                    if (!_known.TryGetValue(p, out var set))
                        _known[p] = set = new HashSet<string>();

                    foreach (var source in map.GetVisionSourcePlanets(p))
                    {
                        set.Add(source.Planet.PlanetName);
                        // Tracks this SOURCE's own traversal, separate from the permanent known set, so
                        // the BFS can pass through a planet already known (from an earlier turn or a
                        // different source) to reach genuinely new territory beyond it — a version gated
                        // on the permanent set alone stops dead the instant it touches known territory.
                        var visited = new HashSet<string> { source.Planet.PlanetName };
                        var frontier = new List<string> { source.Planet.PlanetName };
                        for (var hop = 0; hop < hops && frontier.Count > 0; hop++)
                        {
                            var next = new List<string>();
                            foreach (var name in frontier)
                                foreach (var neighbourName in map.GetNeighbours(name))
                                    if (visited.Add(neighbourName))
                                    {
                                        set.Add(neighbourName);
                                        next.Add(neighbourName);
                                    }
                            frontier = next;
                        }
                    }
                }
            }

            public void SetKnownPlanets(int playerId, IEnumerable<string> names)
            {
                _known[playerId] = new HashSet<string>(names);
            }
        }
    }
}
