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

                Debug.Log($"[MapGenerator] seed {seed}: placed {positions.Count} planets, " +
                          $"prime indices [{string.Join(",", primeIndices.OrderBy(i => i))}]");

                return GenerationResult.Fail("connection graph not implemented yet (Task 6)", seed);
            }
        }
    }
}
