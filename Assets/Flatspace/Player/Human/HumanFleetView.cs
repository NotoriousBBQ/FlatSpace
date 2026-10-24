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

        /// <summary>One of the owner's fleets at a planet: its id and the ships in it (each with its dock index).</summary>
        public struct HumanFleetInfo
        {
            public int FleetId;
            public List<HumanShipInfo> Ships;
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

            /// <summary>The owner's fleets docked at a planet, ordered by fleet id (bunkers are not fleet members).</summary>
            public static List<HumanFleetInfo> Fleets(Planet planet, int owner)
            {
                var fleets = new List<HumanFleetInfo>();
                for (var i = 0; i < planet.DockedShips.Count; i++)
                {
                    var ship = planet.DockedShips[i];
                    if (ship.Owner != owner || !Ship.CountsAsShip(ship.Kind)) continue;
                    var index = fleets.FindIndex(f => f.FleetId == ship.FleetId);
                    if (index < 0)
                    {
                        fleets.Add(new HumanFleetInfo { FleetId = ship.FleetId, Ships = new List<HumanShipInfo>() });
                        index = fleets.Count - 1;
                    }
                    fleets[index].Ships.Add(new HumanShipInfo { DockIndex = i, Kind = ship.Kind, Damage = ship.Damage, Damaged = ship.Damage > 0f });
                }
                return fleets.OrderBy(f => f.FleetId).ToList();
            }

            /// <summary>The dock indices of one fleet's ships: what a move command names to send exactly that fleet.</summary>
            public static List<int> ShipIndices(Planet planet, int owner, int fleetId)
            {
                var fleet = Fleets(planet, owner).Find(f => f.FleetId == fleetId);
                return fleet.Ships == null ? new List<int>() : fleet.Ships.Select(s => s.DockIndex).ToList();
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
