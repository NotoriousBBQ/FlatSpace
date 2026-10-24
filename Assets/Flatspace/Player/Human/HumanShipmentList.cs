using System;
using System.Collections.Generic;
using System.Linq;
using FlatSpace.AI;

namespace FlatSpace
{
    namespace Human
    {
        /// <summary>One planet the Ship resources dialog offers as a source.</summary>
        public struct ShipmentSource
        {
            public string Planet;
            public float Surplus;   // what the planet reports as spare: the figure the AI ships from
            public float Stock;     // the raw amount held
            public int Hops;
        }

        /// <summary>
        /// The sources for shipping food or grotsits to a planet, from the human's own planets. The surplus is the figure
        /// ProcessResourceShipments uses (last turn's surplus result scaled by the planet's population fraction), so the dialog and the AI
        /// cannot disagree. Pure: the population fraction is passed in because Planet.GetPopulationFraction reaches Gameboard.Instance.
        /// </summary>
        public static class HumanShipmentList
        {
            public static List<ShipmentSource> Build(GameAIMap map, int humanId, bool food, string destination,
                IEnumerable<Planet.UpdateResult> lastResults, Func<Planet, float> populationFraction,
                IDictionary<string, float> alreadySent = null)
            {
                var sources = new List<ShipmentSource>();
                if (string.IsNullOrEmpty(destination) || map.GetPlanet(destination) == null) return sources;
                var resultType = food ? Planet.UpdateResult.UpdateResultType.UpdateResultTypeFoodSurplus
                                      : Planet.UpdateResult.UpdateResultType.UpdateResultTypeGrotsitsSurplus;
                var maxNodes = map.GameAIConstants.maxPathNodesForResourceDistribution;

                foreach (var result in lastResults)
                {
                    if (result.Result != resultType || result.PlayerID != humanId || result.Name == destination) continue;
                    if (sources.Exists(s => s.Planet == result.Name)) continue;
                    var planet = map.GetPlanet(result.Name);
                    if (planet == null || !planet.Population.Exists(p => p.Player == humanId)) continue;
                    var surplus = Convert.ToSingle(result.Data) * populationFraction(planet);
                    // The surplus is last turn's report; what the human has shipped since has left the planet already.
                    if (alreadySent != null && alreadySent.TryGetValue(result.Name, out var sent)) surplus -= sent;
                    if (surplus <= 0f) continue;
                    // A real route needs at least two nodes (FindPath's 1-node no-route stub is not a trip).
                    if (!planet.DistanceMapToPathingList.TryGetValue(destination, out var path)
                        || path.NumNodes < 2 || path.NumNodes > maxNodes) continue;
                    sources.Add(new ShipmentSource
                    {
                        Planet = result.Name, Surplus = surplus, Stock = food ? planet.Food : planet.Grotsits, Hops = path.NumNodes - 1,
                    });
                }
                return sources.OrderBy(s => s.Hops).ThenBy(s => s.Planet, StringComparer.Ordinal).ToList();
            }
        }
    }
}
