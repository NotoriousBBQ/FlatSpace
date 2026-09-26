// AssaultPlanner.cs
using System;
using System.Collections.Generic;
using System.Linq;

namespace FlatSpace
{
    namespace AI
    {
        /// <summary>
        /// Consolidate only. Picks a known enemy-occupied planet, sizes the force to the enemy's known
        /// docked fleet, and sends the ships home defence left spare. Pure with respect to the
        /// simulation and free of Gameboard.Instance so the Editor self-check can drive it directly.
        /// </summary>
        public class AssaultPlanner
        {
            private readonly GameAIMap _map;
            private readonly int _playerId;
            private readonly GameAIConstants _constants;

            public AssaultPlanner(GameAIMap map, int playerId)
            {
                _map = map;
                _playerId = playerId;
                _constants = map.GameAIConstants;
            }

            private int CountWarships(Planet planet)
                => planet.DockedShips.Count(s => s.Kind == Ship.ShipKind.WarShip && s.Owner == _playerId);

            /// <summary>Another player's population is present (same test as PlayerKnowledge.HasContact).</summary>
            public bool IsEnemyOccupied(Planet planet)
                => planet.Population.Exists(p => p.Player != _playerId);

            /// <summary>
            /// Enemy warships docked on planets THIS player knows. Ownerless ships (Owner &lt; 0) are not
            /// an enemy fleet, and unknown planets are outside the AI's view.
            /// </summary>
            public int EnemyWarshipTotal()
            {
                var total = 0;
                foreach (var name in _map.Knowledge.KnownPlanets(_playerId))
                {
                    var planet = _map.GetPlanet(name);
                    if (planet == null) continue;
                    total += planet.DockedShips.Count(s => s.Kind == Ship.ShipKind.WarShip
                                                           && s.Owner >= 0 && s.Owner != _playerId);
                }
                return total;
            }

            public int RequiredForce()
                => Math.Max(_constants.assaultMinimumShips,
                    (int)Math.Ceiling(EnemyWarshipTotal() * _constants.assaultRatio));

            /// <summary>Ships still needed at the target: required force minus my docked + incoming there.</summary>
            public int Deficit(Planet target)
                => Math.Max(0, RequiredForce()
                               - (CountWarships(target) + target.GetIncomingShips(Ship.ShipKind.WarShip)));

            // Mirrors ShipTransportPlanner.IsUsablePath: FindPath returns a 1-node zero-cost "path"
            // (it does not throw) when no route exists, so NumNodes < 2 means unreachable.
            private bool IsUsablePath(GameAIMap.DestinationToPathingListEntry entry)
                => entry.NumNodes >= 2 && entry.NumNodes <= _constants.maxPathNodesForShipTransport;

            private float? CheapestPathCost(List<Planet> holders, Planet target)
            {
                float? best = null;
                foreach (var holder in holders)
                {
                    if (holder == target) continue;
                    if (holder.DistanceMapToPathingList.TryGetValue(target.PlanetName, out var entry)
                        && IsUsablePath(entry)
                        && (best == null || entry.Cost < best.Value))
                        best = entry.Cost;
                }
                return best;
            }

            /// <summary>
            /// The known, reachable, enemy-occupied planet where I already have the most warships
            /// docked + incoming (sticky); otherwise the cheapest path from a planet holding warships;
            /// ties by name. Null when there is no such planet.
            /// </summary>
            public Planet ChooseTarget()
            {
                var holders = _map.PlanetList.Where(p => CountWarships(p) > 0).ToList();
                if (holders.Count == 0) return null;

                Planet best = null;
                var bestOwn = -1;
                var bestCost = float.MaxValue;
                foreach (var name in _map.Knowledge.KnownPlanets(_playerId).OrderBy(n => n, StringComparer.Ordinal))
                {
                    var planet = _map.GetPlanet(name);
                    if (planet == null || !IsEnemyOccupied(planet)) continue;

                    var own = CountWarships(planet) + planet.GetIncomingShips(Ship.ShipKind.WarShip);
                    // Ships already committed make a target valid without needing another holder to path from.
                    var cost = own > 0 ? 0f : CheapestPathCost(holders, planet);
                    if (cost == null) continue;

                    if (own > bestOwn || (own == bestOwn && cost.Value < bestCost))
                    {
                        best = planet;
                        bestOwn = own;
                        bestCost = cost.Value;
                    }
                }
                return best;
            }

            private struct Source
            {
                public string Name;
                public int    Remaining;
                public float  Cost;
            }

            /// <summary>
            /// Sends spare ships to the target, cheapest path first, until the deficit is met. A source's
            /// spare is its state's Spare minus what the home actions already send from it.
            /// </summary>
            public List<ShipAction> Plan(Planet target, List<ShipTransportPlanner.PlanetState> states,
                List<ShipAction> homeActions)
            {
                var actions = new List<ShipAction>();
                if (target == null) return actions;
                var deficit = Deficit(target);
                if (deficit <= 0) return actions;

                var sentByOrigin = homeActions
                    .GroupBy(a => a.Origin)
                    .ToDictionary(g => g.Key, g => g.Sum(a => a.Count));

                var sources = new List<Source>();
                foreach (var state in states)
                {
                    if (state.Planet == target) continue;
                    sentByOrigin.TryGetValue(state.Planet.PlanetName, out var sent);
                    var remaining = state.Spare - sent;
                    if (remaining <= 0) continue;
                    if (!state.Planet.DistanceMapToPathingList.TryGetValue(target.PlanetName, out var entry)
                        || !IsUsablePath(entry)) continue;
                    sources.Add(new Source { Name = state.Planet.PlanetName, Remaining = remaining, Cost = entry.Cost });
                }

                foreach (var source in sources
                             .OrderBy(s => s.Cost)
                             .ThenBy(s => s.Name, StringComparer.Ordinal))
                {
                    var count = Math.Min(source.Remaining, deficit);
                    actions.Add(new ShipAction
                    {
                        Origin = source.Name,
                        Target = target.PlanetName,
                        Cost   = source.Cost,
                        Count  = count,
                        Kind   = Ship.ShipKind.WarShip,
                    });
                    deficit -= count;
                    if (deficit <= 0) break;
                }
                return actions;
            }
        }
    }
}
