using System.Collections.Generic;
using System.Linq;
using FlatSpace.AI;

namespace FlatSpace
{
    namespace Human
    {
        /// <summary>One docked ship as the human sees it: where it sits in the planet's dock list (the index the move command names) and its condition.</summary>
        public struct HumanShipInfo
        {
            public int DockIndex;
            public Ship.ShipKind Kind;
            public float Damage;
            public bool Damaged;
        }

        /// <summary>A fleet of the human's in flight: what it carries, where it goes and how many turns are left.</summary>
        public struct HumanFlightInfo
        {
            public Ship.ShipKind Kind;
            public int Count;
            public string Origin;
            public string Target;
            public int TurnsLeft;
        }

        /// <summary>
        /// Read-only queries the fleet panel and the in-flight summary are built on. Pure: they read a planet and an order list, so
        /// a self-check drives them without a Gameboard.
        /// </summary>
        public static class HumanFleetView
        {
            /// <summary>The owner's docked ships at a planet (bunkers included, since they are docked there), each with its dock index and whether it is damaged.</summary>
            public static List<HumanShipInfo> Docked(Planet planet, int owner)
            {
                var ships = new List<HumanShipInfo>();
                for (var i = 0; i < planet.DockedShips.Count; i++)
                {
                    var ship = planet.DockedShips[i];
                    if (ship.Owner != owner) continue;
                    ships.Add(new HumanShipInfo { DockIndex = i, Kind = ship.Kind, Damage = ship.Damage, Damaged = ship.Damage > 0f });
                }
                return ships;
            }

            /// <summary>The owner's ship fleets still travelling (ship transport orders in the list).</summary>
            public static List<HumanFlightInfo> InFlight(IEnumerable<GameAI.GameAIOrder> orders, int owner)
            {
                return orders
                    .Where(o => o.Type == GameAI.GameAIOrder.OrderType.OrderTypeShipTransport && o.PlayerId == owner && o.Fleet != null)
                    .Select(o => new HumanFlightInfo
                    {
                        Kind = o.Fleet.Kind,
                        Count = o.Fleet.Snapshots != null ? o.Fleet.Snapshots.Count : System.Convert.ToInt32(o.Data),
                        Origin = o.Origin,
                        Target = o.Target,
                        TurnsLeft = o.TimingDelay,
                    })
                    .ToList();
            }
        }
    }
}
