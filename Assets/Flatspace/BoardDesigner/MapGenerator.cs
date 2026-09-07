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

            // Filled in over Tasks 5-7. For now: resolve counts and report them,
            // then fail so the dry-run has something to print.
            private static GenerationResult TryGenerate(MapGenSettings settings, int seed)
            {
                var rng = new Random(seed);
                var primeCount = settings.playerCount;
                var nonPrimeCount = settings.totalPlanetCount - primeCount;
                var typeMultiset = ResolveTypeMultiset(settings, nonPrimeCount);

                var summary = string.Join(", ", typeMultiset
                    .GroupBy(t => t)
                    .Select(g => $"{PlanetTypeDefaults.ShortName(g.Key)}:{g.Count()}"));
                Debug.Log($"[MapGenerator] seed {seed}: {primeCount} Prime, non-Prime [{summary}]");

                return GenerationResult.Fail("placement not implemented yet (Task 5)", seed);
            }
        }
    }
}
