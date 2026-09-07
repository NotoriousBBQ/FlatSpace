using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Random = System.Random;

namespace FlatSpace
{
    namespace Tools
    {
        public struct GeneratedPlanet
        {
            public string Name;
            public Planet.PlanetType Type;
            public Planet.PlanetStrategy Strategy;
            public Vector2 Position;
            public List<string> Connections;
        }

        public class GenerationResult
        {
            public bool Success;
            public string Error;
            public int EffectiveSeed;
            public List<GeneratedPlanet> Planets = new List<GeneratedPlanet>();

            public static GenerationResult Fail(string error, int seed) =>
                new GenerationResult { Success = false, Error = error, EffectiveSeed = seed };
        }

        public static class MapGenerator
        {
            private const float ExtraEdgeFraction = 0.25f;

            public static GenerationResult Generate(MapGenSettings settings)
            {
                var settingsError = ValidateSettings(settings);
                if (settingsError != null)
                    return GenerationResult.Fail(settingsError, 0);

                var baseSeed = settings.seed != 0 ? settings.seed : new Random().Next(1, int.MaxValue);

                GenerationResult lastFailure = null;
                for (var attempt = 0; attempt < Mathf.Max(1, settings.seedRetryBudget); attempt++)
                {
                    var seed = baseSeed + attempt;
                    Debug.Log($"[MapGenerator] attempt {attempt + 1}, seed {seed}");
                    var result = TryGenerate(settings, seed);
                    if (result.Success)
                        return result;
                    if (result.Error != null && result.Error.StartsWith("could not place planets"))
                        return result; // deterministic settings failure; retrying other seeds won't help
                    lastFailure = result;
                }
                return lastFailure ?? GenerationResult.Fail("generation failed with no diagnostic", baseSeed);
            }

            private static string ValidateSettings(MapGenSettings s)
            {
                if (s == null) return "MapGenSettings is null";
                if (s.playerCount < 1) return "playerCount must be >= 1";
                if (s.totalPlanetCount <= s.playerCount)
                    return $"totalPlanetCount ({s.totalPlanetCount}) must be > playerCount ({s.playerCount})";
                var eligible = EligibleWeights(s);
                if (eligible.Count == 0)
                    return "no typeWeights entry with weight > 0 and a non-Prime type";
                if (s.minPlanetSeparation <= 0f) return "minPlanetSeparation must be > 0";
                if (s.connectionRadius <= 0f) return "connectionRadius must be > 0";
                if (s.nominalSpacing <= 0f) return "nominalSpacing must be > 0";
                return null;
            }

            private static List<TypeWeight> EligibleWeights(MapGenSettings s) =>
                s.typeWeights
                    .Where(w => w.weight > 0f && w.type != Planet.PlanetType.PlanetTypePrime)
                    .ToList();

            // Distribute nonPrimeCount planets across the eligible types by their
            // normalized weights, using largest-remainder rounding so the parts
            // sum to exactly nonPrimeCount.
            private static List<Planet.PlanetType> ResolveTypeMultiset(MapGenSettings s, int nonPrimeCount)
            {
                var weights = EligibleWeights(s);
                var total = weights.Sum(w => w.weight);
                var exact = weights.Select(w => (type: w.type, value: w.weight / total * nonPrimeCount)).ToList();

                var counts = exact.Select(e => (e.type, count: Mathf.FloorToInt((float)e.value))).ToList();
                var assigned = counts.Sum(c => c.count);
                var remainders = exact
                    .Select((e, i) => (i, frac: e.value - Mathf.FloorToInt((float)e.value)))
                    .OrderByDescending(x => x.frac)
                    .ToList();

                var idx = 0;
                while (assigned < nonPrimeCount)
                {
                    var slot = remainders[idx % remainders.Count].i;
                    counts[slot] = (counts[slot].type, counts[slot].count + 1);
                    assigned++;
                    idx++;
                }

                var multiset = new List<Planet.PlanetType>();
                foreach (var c in counts)
                    for (var k = 0; k < c.count; k++)
                        multiset.Add(c.type);
                return multiset;
            }

            // Rejection-sampled blue-noise scatter. Returns null if it cannot
            // place `count` points respecting `minSeparation` even after growing
            // the extent several times.
            private static List<Vector2> PlacePlanets(Random rng, int count, float minSeparation, float nominalSpacing)
            {
                var side = Mathf.Sqrt(count) * nominalSpacing;
                var half = side / 2f;
                var minSqr = minSeparation * minSeparation;
                var points = new List<Vector2>();

                var stall = 0;
                var grows = 0;
                while (points.Count < count)
                {
                    var candidate = new Vector2(
                        (float)(rng.NextDouble() * 2.0 - 1.0) * half,
                        (float)(rng.NextDouble() * 2.0 - 1.0) * half);

                    var ok = true;
                    foreach (var p in points)
                    {
                        if ((p - candidate).sqrMagnitude < minSqr) { ok = false; break; }
                    }

                    if (ok)
                    {
                        points.Add(candidate);
                        stall = 0;
                    }
                    else if (++stall > 40)
                    {
                        if (++grows > 6)
                            return null;
                        half *= 1.1f;
                        stall = 0;
                    }
                }
                return points;
            }

            // Farthest-point sampling: pick `primeCount` indices out of `positions`
            // that are spread as far apart as possible.
            private static HashSet<int> PickPrimeIndices(Random rng, List<Vector2> positions, int primeCount)
            {
                var chosen = new HashSet<int> { rng.Next(positions.Count) };
                while (chosen.Count < primeCount)
                {
                    var bestIdx = -1;
                    var bestDist = -1f;
                    for (var i = 0; i < positions.Count; i++)
                    {
                        if (chosen.Contains(i)) continue;
                        var nearest = float.MaxValue;
                        foreach (var c in chosen)
                            nearest = Mathf.Min(nearest, (positions[i] - positions[c]).sqrMagnitude);
                        if (nearest > bestDist) { bestDist = nearest; bestIdx = i; }
                    }
                    chosen.Add(bestIdx);
                }
                return chosen;
            }

            private readonly struct Edge
            {
                public readonly int A;
                public readonly int B;
                public readonly float Cost;
                public Edge(int a, int b, float cost) { A = a; B = b; Cost = cost; }
            }

            // Union-find for Kruskal / component tracking.
            private class DisjointSet
            {
                private readonly int[] _parent;
                public DisjointSet(int n)
                {
                    _parent = new int[n];
                    for (var i = 0; i < n; i++) _parent[i] = i;
                }
                public int Find(int x) => _parent[x] == x ? x : (_parent[x] = Find(_parent[x]));
                public bool Union(int a, int b)
                {
                    int ra = Find(a), rb = Find(b);
                    if (ra == rb) return false;
                    _parent[ra] = rb;
                    return true;
                }
            }

            private static bool IsPrimePair(HashSet<int> primes, int a, int b) =>
                primes.Contains(a) && primes.Contains(b);

            // Returns the undirected edge set (pairs with A < B), or null if the
            // graph cannot be made connected without a Prime-Prime edge.
            private static HashSet<(int, int)> BuildGraph(
                Random rng, List<Vector2> positions, HashSet<int> primes, float connectionRadius)
            {
                var n = positions.Count;

                var candidates = new List<Edge>();
                for (var i = 0; i < n; i++)
                for (var j = i + 1; j < n; j++)
                {
                    if (IsPrimePair(primes, i, j)) continue;
                    var d = Vector2.Distance(positions[i], positions[j]);
                    if (d <= connectionRadius)
                        candidates.Add(new Edge(i, j, d));
                }

                var edges = new HashSet<(int, int)>();
                var ds = new DisjointSet(n);

                // Kruskal MST over in-radius candidates.
                foreach (var e in candidates.OrderBy(e => e.Cost))
                {
                    if (ds.Union(e.A, e.B))
                        edges.Add((e.A, e.B));
                }

                // Bridge any remaining components with the shortest non-Prime-Prime
                // inter-component edge, ignoring the radius limit.
                while (true)
                {
                    var roots = Enumerable.Range(0, n).Select(ds.Find).Distinct().ToList();
                    if (roots.Count <= 1) break;

                    Edge? best = null;
                    for (var i = 0; i < n; i++)
                    for (var j = i + 1; j < n; j++)
                    {
                        if (ds.Find(i) == ds.Find(j)) continue;
                        if (IsPrimePair(primes, i, j)) continue;
                        var d = Vector2.Distance(positions[i], positions[j]);
                        if (best == null || d < best.Value.Cost)
                            best = new Edge(i, j, d);
                    }

                    if (best == null) return null; // only Prime-Prime bridges remain
                    ds.Union(best.Value.A, best.Value.B);
                    edges.Add((best.Value.A, best.Value.B));
                }

                // Add back a fraction of the unused in-radius candidates for loops.
                var unused = candidates
                    .Where(e => !edges.Contains((e.A, e.B)))
                    .ToList();
                var extra = Mathf.RoundToInt(ExtraEdgeFraction * unused.Count);
                for (var k = 0; k < extra && unused.Count > 0; k++)
                {
                    var pick = rng.Next(unused.Count);
                    edges.Add((unused[pick].A, unused[pick].B));
                    unused.RemoveAt(pick);
                }

                if (candidates.Count > 0 && unused.Count == 0)
                    Debug.LogWarning($"[MapGenerator] every in-range pair is connected ({edges.Count} edges); consider more planets or a smaller connectionRadius for a sparser map");

                return edges;
            }

            private static bool IsConnected(int n, HashSet<(int, int)> edges)
            {
                if (n == 0) return true;
                var adj = new Dictionary<int, List<int>>();
                for (var i = 0; i < n; i++) adj[i] = new List<int>();
                foreach (var (a, b) in edges) { adj[a].Add(b); adj[b].Add(a); }

                var seen = new HashSet<int> { 0 };
                var stack = new Stack<int>();
                stack.Push(0);
                while (stack.Count > 0)
                {
                    foreach (var next in adj[stack.Pop()])
                        if (seen.Add(next)) stack.Push(next);
                }
                return seen.Count == n;
            }

            // Assign `multiset` types to the non-prime node indices so that no
            // edge joins two nodes of the same type unless that type is Normal.
            // Randomized greedy with bounded backtracking. Returns a
            // node-index -> type map, or null on failure.
            private static Dictionary<int, Planet.PlanetType> AssignTypes(
                Random rng, int n, HashSet<int> primes,
                HashSet<(int, int)> edges, List<Planet.PlanetType> multiset)
            {
                var adj = new Dictionary<int, List<int>>();
                for (var i = 0; i < n; i++) adj[i] = new List<int>();
                foreach (var (a, b) in edges) { adj[a].Add(b); adj[b].Add(a); }

                var nodes = Enumerable.Range(0, n)
                    .Where(i => !primes.Contains(i))
                    .OrderByDescending(i => adj[i].Count)
                    .ToList();

                var assignment = new Dictionary<int, Planet.PlanetType>();
                foreach (var p in primes) assignment[p] = Planet.PlanetType.PlanetTypePrime;

                // Remaining count of each type available to hand out.
                var pool = multiset.GroupBy(t => t).ToDictionary(g => g.Key, g => g.Count());
                var steps = 0;
                const int maxSteps = 20000;

                bool Conflicts(int node, Planet.PlanetType type)
                {
                    if (type == Planet.PlanetType.PlanetTypeNormal) return false;
                    foreach (var nb in adj[node])
                        if (assignment.TryGetValue(nb, out var t) && t == type)
                            return true;
                    return false;
                }

                bool Recurse(int idx)
                {
                    if (++steps > maxSteps) return false;
                    if (idx == nodes.Count) return true;
                    var node = nodes[idx];

                    var types = pool.Where(kv => kv.Value > 0).Select(kv => kv.Key).ToList();
                    for (var i = types.Count - 1; i > 0; i--)
                    {
                        var j = rng.Next(i + 1);
                        (types[i], types[j]) = (types[j], types[i]);
                    }

                    foreach (var type in types)
                    {
                        if (Conflicts(node, type)) continue;
                        assignment[node] = type;
                        pool[type]--;
                        if (Recurse(idx + 1)) return true;
                        pool[type]++;
                        assignment.Remove(node);
                    }
                    return false;
                }

                return Recurse(0) ? assignment : null;
            }

            private static string Summarize(IEnumerable<Planet.PlanetType> types) =>
                string.Join(", ", types.GroupBy(t => t)
                    .Select(g => $"{PlanetTypeDefaults.ShortName(g.Key)}:{g.Count()}"));

            private static string Validate(
                int n, int primeCount, HashSet<int> primes,
                HashSet<(int, int)> edges, Dictionary<int, Planet.PlanetType> assignment)
            {
                if (!IsConnected(n, edges))
                    return "internal error: graph not connected after assignment";

                if (assignment.Count(kv => kv.Value == Planet.PlanetType.PlanetTypePrime) != primeCount)
                    return "internal error: Prime count mismatch after assignment";

                var degree = new int[n];
                foreach (var (a, b) in edges)
                {
                    degree[a]++; degree[b]++;
                    if (IsPrimePair(primes, a, b))
                        return "internal error: an edge joins two Prime planets";
                    var ta = assignment[a];
                    var tb = assignment[b];
                    if (ta == tb && ta != Planet.PlanetType.PlanetTypeNormal)
                        return $"internal error: edge joins two {PlanetTypeDefaults.ShortName(ta)} planets";
                }
                for (var i = 0; i < n; i++)
                    if (degree[i] == 0)
                        return "internal error: a planet has no connections";

                return null;
            }

            private static List<GeneratedPlanet> Emit(
                List<Vector2> positions, HashSet<(int, int)> edges,
                Dictionary<int, Planet.PlanetType> assignment)
            {
                var adj = new Dictionary<int, List<int>>();
                for (var i = 0; i < positions.Count; i++) adj[i] = new List<int>();
                foreach (var (a, b) in edges) { adj[a].Add(b); adj[b].Add(a); }

                // Per-type running index for "{ShortName} {n}" names.
                var typeIndex = new Dictionary<Planet.PlanetType, int>();
                var names = new string[positions.Count];
                for (var i = 0; i < positions.Count; i++)
                {
                    var type = assignment[i];
                    typeIndex.TryGetValue(type, out var next);
                    names[i] = $"{PlanetTypeDefaults.ShortName(type)} {next}";
                    typeIndex[type] = next + 1;
                }

                var planets = new List<GeneratedPlanet>();
                for (var i = 0; i < positions.Count; i++)
                {
                    var type = assignment[i];
                    planets.Add(new GeneratedPlanet
                    {
                        Name = names[i],
                        Type = type,
                        Strategy = PlanetTypeDefaults.StrategyFor(type),
                        Position = positions[i],
                        Connections = adj[i].Select(j => names[j]).OrderBy(s => s).ToList(),
                    });
                }
                return planets;
            }

            private static GenerationResult TryGenerate(MapGenSettings settings, int seed)
            {
                var rng = new Random(seed);
                var primeCount = settings.playerCount;
                var nonPrimeCount = settings.totalPlanetCount - primeCount;
                var typeMultiset = ResolveTypeMultiset(settings, nonPrimeCount);

                var positions = PlacePlanets(rng, settings.totalPlanetCount,
                    settings.minPlanetSeparation, settings.nominalSpacing);
                if (positions == null)
                    return GenerationResult.Fail(
                        "could not place planets: minPlanetSeparation too large for the planet count", seed);

                var primeIndices = PickPrimeIndices(rng, positions, primeCount);

                var edges = BuildGraph(rng, positions, primeIndices, settings.connectionRadius);
                if (edges == null)
                    return GenerationResult.Fail(
                        "could not connect the map without joining two Prime planets; increase totalPlanetCount or connectionRadius", seed);

                var assignment = AssignTypes(rng, positions.Count, primeIndices, edges, typeMultiset);
                if (assignment == null)
                    return GenerationResult.Fail(
                        "could not assign types without same-type neighbors; loosen the frequency weights " +
                        $"(non-Prime multiset was [{Summarize(typeMultiset)}])", seed);

                var validationError = Validate(positions.Count, primeCount, primeIndices, edges, assignment);
                if (validationError != null)
                    return GenerationResult.Fail(validationError, seed);

                var planets = Emit(positions, edges, assignment);
                Debug.Log($"[MapGenerator] seed {seed} OK: {planets.Count} planets, {edges.Count} edges, " +
                          $"non-Prime [{Summarize(typeMultiset)}], connectivity OK");
                return new GenerationResult
                {
                    Success = true,
                    EffectiveSeed = seed,
                    Planets = planets,
                };
            }
        }
    }
}
