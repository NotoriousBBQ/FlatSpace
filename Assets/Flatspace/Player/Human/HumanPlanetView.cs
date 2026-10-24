using System;
using System.Collections.Generic;
using System.Linq;
using FlatSpace.AI;

namespace FlatSpace
{
    namespace Human
    {
        /// <summary>A planet the Colonize dialog offers as a target.</summary>
        public struct ColonizeOption
        {
            public Planet Target;
            public int Turns;
        }

        /// <summary>Small questions the planet panel asks about a planet. Pure (no Gameboard.Instance), so a self-check drives them.</summary>
        public static class HumanPlanetView
        {
            /// <summary>
            /// How much the planet's food and grotsits stocks change in a turn at its present production: food produced minus one per
            /// inhabitant, grotsits produced minus the planet's maintenance cost (people plus improvement upkeep). The size of the stock does
            /// not matter, so a full store does not read as a loss.
            /// </summary>
            public static (float food, float grotsits) NetPerTurn(Planet planet)
            {
                planet.ProjectProduction();   // production is otherwise only known once a turn has run (a new or loaded game starts at 0)
                return (planet.FoodProduced - planet.Population.Count, planet.GrotsitsProduced - planet.GetMaintenanceCost());
            }

            /// <summary>
            /// Whether a stockpile can carry a net loss for a turn: always when the rate is not negative, else when the stock holds at least the
            /// loss. A planet whose store covers it is not in trouble even though the rate is negative (a shipment just landed, say).
            /// </summary>
            public static bool StockCovers(float stock, float perTurn) => perTurn >= 0f || stock >= -perTurn;

            /// <summary>The planet is the human's: production, its queue and the development switch are theirs to change only here.</summary>
            public static bool OwnsPlanet(Planet planet, int humanId) => planet != null && planet.Owner == humanId;

            /// <summary>The human may ship from and colonize from a planet they own or have people on (production and development need OwnsPlanet).</summary>
            public static bool CanOrder(Planet planet, int humanId)
                => planet != null && (planet.Owner == humanId || planet.Population.Exists(p => p.Player == humanId));

            /// <summary>A colony ship is docked here and at least two of the human's people live here (the colonist is taken from them).</summary>
            public static bool CanColonizeFrom(Planet origin, int humanId)
                => origin != null
                   && origin.DockedShips.Exists(s => s.Kind == Ship.ShipKind.ColonyShip && s.Owner == humanId)
                   && origin.Population.Count(p => p.Player == humanId) >= 2;

            /// <summary>
            /// The known planets the human could colonize from here: each is one the colonize command would accept (HumanCommandProcessor.Check),
            /// with the turns the colonist takes. Nearest first. Empty when nothing can be colonized from this planet at all.
            /// </summary>
            public static List<ColonizeOption> ColonizeOptions(GameAIMap map, PlayerAI human, Planet origin, int turn)
            {
                var options = new List<ColonizeOption>();
                var humanId = human.Player.playerID;
                if (!CanColonizeFrom(origin, humanId)) return options;

                var checker = new HumanCommandProcessor(map, human, turn);
                var speed = Math.Max(0.0001f, map.GameAIConstants.defaultTravelSpeed);
                foreach (var entry in origin.DistanceMapToPathingList)
                {
                    var target = map.GetPlanet(entry.Key);
                    if (target == null) continue;
                    if (checker.Check(new HumanCommand { type = "colonize", origin = origin.PlanetName, target = target.PlanetName }) != null) continue;
                    // The same rounding the colonize command uses for the colonist's delay, so the list and the order agree.
                    options.Add(new ColonizeOption { Target = target, Turns = Math.Max(1, Convert.ToInt32(entry.Value.Cost / speed)) });
                }
                return options.OrderBy(o => o.Turns).ThenBy(o => o.Target.PlanetName, StringComparer.Ordinal).ToList();
            }
        }
    }
}
