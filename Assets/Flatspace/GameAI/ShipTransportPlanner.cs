// ShipTransportPlanner.cs
using System;
using System.Collections.Generic;
using System.Linq;

namespace FlatSpace
{
    namespace AI
    {
        /// <summary>
        /// Decides which of a player's planets send spare warships to which planets short of them.
        /// Pure with respect to the simulation: it reads planet/map state and returns ShipActions.
        /// Must not touch Gameboard.Instance so the Editor self-check can drive it directly.
        /// </summary>
        public class ShipTransportPlanner
        {
            public const int NoCategory = int.MaxValue;

            private readonly GameAIMap _map;
            private readonly int _playerId;
            private readonly GameAIConstants _constants;

            public ShipTransportPlanner(GameAIMap map, int playerId)
            {
                _map = map;
                _playerId = playerId;
                _constants = map.GameAIConstants;
            }

            // ── Categories and garrisons ─────────────────────────────────────

            public bool IsColonized(Planet planet)
                => planet.Owner == _playerId && planet.Population.Count > 0;

            // Colonized by this player with at least one uncolonized (no population) neighbour.
            public bool IsOuter(Planet planet)
            {
                if (!IsColonized(planet)) return false;
                foreach (var name in _map.GetNeighbours(planet.PlanetName))
                {
                    var neighbour = _map.GetPlanet(name);
                    if (neighbour != null && neighbour.Population.Count == 0) return true;
                }
                return false;
            }

            /// <summary>Every category (1..5) that applies to the planet.</summary>
            public List<int> ApplicableCategories(Planet planet)
            {
                var result = new List<int>();
                switch (planet.Type)
                {
                    case Planet.PlanetType.PlanetTypeDesert:
                    case Planet.PlanetType.PlanetTypeIndustrial:
                    case Planet.PlanetType.PlanetTypeFarm:
                    case Planet.PlanetType.PlanetTypeOcean:
                        result.Add(1);
                        break;
                }
                if (IsOuter(planet)) result.Add(2);
                if (planet.Type == Planet.PlanetType.PlanetTypePrime) result.Add(3);
                if (_map.GetNeighbours(planet.PlanetName).Count >= _constants.highTrafficConnectionCount)
                    result.Add(4);
                if (planet.Type == Planet.PlanetType.PlanetTypeVerdant
                    || planet.Type == Planet.PlanetType.PlanetTypeDesolate)
                    result.Add(5);
                return result;
            }

            /// <summary>Target priority: the best (lowest) applicable category, or NoCategory.</summary>
            public int Category(Planet planet)
            {
                var categories = ApplicableCategories(planet);
                return categories.Count == 0 ? NoCategory : categories.Min();
            }

            /// <summary>Base garrison: the largest garrison among applicable categories; 0 if none.</summary>
            public int Garrison(Planet planet) => GarrisonOf(ApplicableCategories(planet));

            private int GarrisonOf(List<int> categories)
                => categories.Count == 0 ? 0 : categories.Max(GarrisonForCategory);

            private int GarrisonForCategory(int category)
            {
                switch (category)
                {
                    case 1: return _constants.garrisonSpecialized;
                    case 2: return _constants.garrisonOuter;
                    case 3: return _constants.garrisonPrime;
                    case 4: return _constants.garrisonHighTraffic;
                    case 5: return _constants.garrisonHighlySpecialized;
                    default: return 0;
                }
            }
        }
    }
}
