using System.Collections.Generic;
using System.Linq;
using FlatSpace.AI;

namespace FlatSpace
{
    namespace Human
    {
        /// <summary>One line of the fleet summary: a fleet docked at a planet or one in flight.</summary>
        public struct HumanFleetRow
        {
            public string Where;          // the planet, or "Origin -> Destination" in flight
            public string Detail;         // the ships by kind
            public string PlanetToShow;   // where a click takes the board: the planet, or the destination in flight
            public bool InFlight;
            public int TurnsLeft;         // in flight only
        }

        /// <summary>The rows of the fleet summary screen. Pure: reads planets and an order list, so a self-check drives it without a Gameboard.</summary>
        public static class HumanFleetList
        {
            /// <summary>Docked fleets (by planet name, then fleet id) followed by the human's ship orders in flight.</summary>
            public static List<HumanFleetRow> Build(IEnumerable<Planet> planets, IEnumerable<GameAI.GameAIOrder> orders, int humanId)
            {
                var rows = new List<HumanFleetRow>();
                foreach (var planet in planets.OrderBy(p => p.PlanetName, System.StringComparer.Ordinal))
                {
                    foreach (var fleet in HumanFleetView.Fleets(planet, humanId))
                        rows.Add(new HumanFleetRow
                        {
                            Where = planet.PlanetName,
                            Detail = HumanHoverText.Ships(fleet.Ships.Select(s => s.Kind)),
                            PlanetToShow = planet.PlanetName,
                        });
                }
                foreach (var order in orders)
                {
                    if (order.Type != GameAI.GameAIOrder.OrderType.OrderTypeShipTransport || order.PlayerId != humanId || order.Fleet == null) continue;
                    var count = order.Fleet.Snapshots != null && order.Fleet.Snapshots.Count > 0
                        ? order.Fleet.Snapshots.Count
                        : System.Convert.ToInt32(order.Data);
                    rows.Add(new HumanFleetRow
                    {
                        Where = $"{order.Origin} -> {order.Target}",
                        Detail = HumanHoverText.Ships(Enumerable.Repeat(order.Fleet.Kind, count)),
                        PlanetToShow = order.Target,
                        InFlight = true,
                        TurnsLeft = order.TimingDelay,
                    });
                }
                return rows;
            }
        }
    }
}
