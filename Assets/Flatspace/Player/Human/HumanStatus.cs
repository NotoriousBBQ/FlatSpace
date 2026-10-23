using System.Collections.Generic;
using System.Linq;
using FlatSpace.AI;

namespace FlatSpace
{
    namespace Human
    {
        /// <summary>The human's standing in the match. Pure (a GameAIMap and an order list), so a self-check drives it.</summary>
        public static class HumanStatus
        {
            /// <summary>
            /// Defeated: no populated planet, no docked colony ship and no colonist in flight. A colony ship or a colonist is a way back
            /// into the match, so either keeps the player in. Warships do not (they cannot found a colony).
            /// </summary>
            public static bool IsDefeated(GameAIMap map, IEnumerable<GameAI.GameAIOrder> inFlight, int playerId,
                out int populatedPlanets, out int dockedColonyShips, out int colonistsInFlight)
            {
                populatedPlanets = map.PlanetList.Count(p => p.Population.Exists(i => i.Player == playerId));
                dockedColonyShips = map.PlanetList.Sum(p => p.DockedShips.Count(s => s.Kind == Ship.ShipKind.ColonyShip && s.Owner == playerId));
                colonistsInFlight = inFlight.Count(o => o.Type == GameAI.GameAIOrder.OrderType.OrderTypePopulationTransport && o.PlayerId == playerId);
                return populatedPlanets == 0 && dockedColonyShips == 0 && colonistsInFlight == 0;
            }
        }
    }
}
