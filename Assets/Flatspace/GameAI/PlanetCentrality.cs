// PlanetCentrality.cs
using System;
using System.Collections.Generic;
using System.Linq;

namespace FlatSpace
{
    namespace AI
    {
        /// <summary>
        /// Shortest-path betweenness of every planet: how many planet pairs have a stored shortest path that passes THROUGH
        /// it (endpoints excluded), and that count as a percentile of the board. A high percentile marks a chokepoint such as
        /// a crossroads, which a plain neighbour count misses. Pure: it reads only the planet names and the paths it is
        /// given (the all-pairs paths GameAIMap stores, the routes ships actually fly), so a self-check drives it directly
        /// and the result does not depend on the order the paths arrive in. One path per pair counts, so equal-cost ties go
        /// to whichever path was stored (tools/planet-centrality.ps1 has the same property).
        /// </summary>
        public class PlanetCentrality
        {
            private readonly Dictionary<string, int> _betweenness = new Dictionary<string, int>();
            private readonly Dictionary<string, float> _percentile = new Dictionary<string, float>();

            /// <summary>
            /// Each path is a list of planet names from one planet to another. A path with fewer than 3 nodes (including
            /// FindPath's 1-node no-route stub) and any node that is not in planetNames add nothing.
            /// </summary>
            public static PlanetCentrality Compute(IEnumerable<string> planetNames, IEnumerable<IReadOnlyList<string>> paths)
            {
                var result = new PlanetCentrality();
                var names = planetNames.Distinct().ToList();
                foreach (var name in names) result._betweenness[name] = 0;

                foreach (var path in paths)
                    for (var i = 1; i < path.Count - 1; i++)
                        if (result._betweenness.ContainsKey(path[i]))
                            result._betweenness[path[i]]++;

                // Percentile: the fraction of the OTHER planets with strictly lower betweenness, so ties share a score,
                // leaves score 0 and the top planet scores 1. A board of 0 or 1 planets scores 0.
                var values = names.Select(n => result._betweenness[n]).ToList();
                foreach (var name in names)
                {
                    var own = result._betweenness[name];
                    result._percentile[name] = names.Count <= 1
                        ? 0f
                        : values.Count(v => v < own) / (float)(names.Count - 1);
                }
                return result;
            }

            /// <summary>Paths through the planet (endpoints excluded); 0 for an unknown name.</summary>
            public int Betweenness(string name)
                => name != null && _betweenness.TryGetValue(name, out var value) ? value : 0;

            /// <summary>0..1 share of the other planets with lower betweenness; 0 for an unknown name.</summary>
            public float Percentile(string name)
                => name != null && _percentile.TryGetValue(name, out var value) ? value : 0f;

            /// <summary>The count planets with the highest betweenness, ties by name.</summary>
            public IEnumerable<(string name, int betweenness, float percentile)> Top(int count)
                => _betweenness
                    .OrderByDescending(kv => kv.Value)
                    .ThenBy(kv => kv.Key, StringComparer.Ordinal)
                    .Take(Math.Max(0, count))
                    .Select(kv => (kv.Key, kv.Value, _percentile[kv.Key]));
        }
    }
}
