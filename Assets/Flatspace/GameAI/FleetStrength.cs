using System.Collections.Generic;
using System.Linq;

namespace FlatSpace
{
    namespace AI
    {
        /// <summary>
        /// How strong a set of docked warships is: the sum of Offense x (Health + Defense) over each ship (WarshipStats, so
        /// research counts). One pure place for the formula, so ship combat can replace it later without touching the callers.
        /// </summary>
        public static class FleetStrength
        {
            public static float Of(IEnumerable<Ship> ships, WarshipStats stats)
            {
                var sum = 0f;
                foreach (var ship in ships)
                {
                    if (ship.Kind != Ship.ShipKind.WarShip) continue;
                    // A damaged ship counts for less: effective offense x (current health + Defense).
                    var offense = stats.EffectiveOffense(ship);
                    var durability = stats.CurrentHealth(ship)
                                     + stats.Defense(ship.Template, ship.ResearchSnapshot);
                    sum += offense * durability;
                }
                return sum;
            }

            /// <summary>My docked warships anywhere (in-flight ships are not counted).</summary>
            public static float Mine(GameAIMap map, int player, WarshipStats stats)
                => map.PlanetList.Sum(p => Of(p.DockedShips.Where(s => s.Owner == player), stats));

            /// <summary>The rival's docked warships on planets the viewer knows (the same limit AssaultPlanner uses).</summary>
            public static float VisibleOf(GameAIMap map, int viewer, int rival, WarshipStats stats)
            {
                var sum = 0f;
                foreach (var name in map.Knowledge.KnownPlanets(viewer))
                {
                    var planet = map.GetPlanet(name);
                    if (planet == null) continue;
                    sum += Of(planet.DockedShips.Where(s => s.Owner == rival), stats);
                }
                return sum;
            }
        }
    }
}
