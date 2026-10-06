using System;
using System.Collections.Generic;
using System.Linq;

namespace FlatSpace.AI
{
    /// <summary>
    /// Decides which of a player's groups leave a fight and where they go. For each of my FightProjection results: a
    /// deterministic gate (a war rival survives and I would lose at least retreatCheckFraction; at a planet I populate, only a
    /// wipe-out), the best tiered destination, then RetreatMatrix weighs Stay against Retreat. Tiers (first non-empty wins,
    /// cheapest path inside a tier): 1 a planet I populate; 2 a known empty planet, or one populated only by players I am not at
    /// war with; 3 a known planet of a war rival whose REMEMBERED blockade value is under my group's offense / (1 + margin)
    /// (it may be stale by arrival). Tiers 1 and 2 must hold no war-rival warship and not be blockaded by a war rival; the route
    /// avoids planets blockaded by a war rival (RoutePlanner). Pure (no Gameboard.Instance).
    /// </summary>
    public class RetreatPlanner
    {
        public const string HoldNoDestination = "NoDestination";
        public const string HoldOwnPlanetNotWiped = "OwnPlanetNotWiped";

        public class Retreat
        {
            public string Planet;
            public string Destination;
            public int Tier;
            public int Ships;
            public float LossFraction;        // the perceived loss the decision used (the mean over the imperfect-intel samples)
            public float ExactLossFraction;   // the exact projection's loss, for the log
            public float RivalSurvivorsFraction;
            public float RouteCost;
            public float PRetreat;
            public float RememberedBlockade;   // tier 3 only
            public float MyOffense;            // tier 3 only
        }

        public class Stay
        {
            public string Planet;
            public float LossFraction;
            public float PRetreat;
        }

        public class Hold
        {
            public string Planet;
            public string Reason;
            public float LossFraction;
        }

        public class Plan
        {
            public List<ShipAction> Actions = new List<ShipAction>();
            public List<Retreat> Retreats = new List<Retreat>();
            public List<Stay> Stays = new List<Stay>();
            public List<Hold> Holds = new List<Hold>();
            public HashSet<string> Retreating = new HashSet<string>();
        }

        private class Destination
        {
            public string Planet;
            public int Tier;
            public float Cost;
        }

        private readonly GameAIMap _map;
        private readonly int _me;
        private readonly ISet<int> _warRivals;
        private readonly BlockadeView _view;       // what I remember and see (any blocker)
        private readonly BlockadeView _warView;    // only blockades by my war rivals
        private readonly WarshipStats _stats;
        private readonly GameAIConstants _constants;

        public RetreatPlanner(GameAIMap map, int playerId, ISet<int> warRivals, BlockadeView view, WarshipStats stats)
        {
            _map = map;
            _me = playerId;
            _warRivals = warRivals ?? new SortedSet<int>();
            _view = view ?? new BlockadeView();
            _warView = _view.OnlyFrom(_warRivals);
            _stats = stats;
            _constants = map.GameAIConstants;
        }

        public Plan Decide(IEnumerable<FightProjection> projections)
        {
            var plan = new Plan();
            var rows = new List<RetreatMatrix.Row>();
            var destinations = new Dictionary<string, List<Destination>>();
            var byPlanet = new Dictionary<string, FightProjection>();
            foreach (var projection in projections.Where(p => p != null && p.Player == _me).OrderBy(p => p.Planet, StringComparer.Ordinal))
            {
                var planet = _map.GetPlanet(projection.Planet);
                if (planet == null) continue;
                var group = MyWarships(planet);
                if (group.Count == 0) continue;

                // The decision reads the perceived values: the mean loss over the imperfect-intel samples, the rival standing in at
                // least half of them, my group wiped in at least half of them (all three fall back to the exact projection when
                // it carries no samples).
                var own = planet.Owner == _me && planet.Population.Count > 0;
                var loss = projection.PerceivedLossFraction;
                var rivalSurvives = projection.PerceivedRivalSurvivesShare >= 0.5f;
                var wiped = projection.PerceivedWipedShare >= 0.5f;
                var gated = rivalSurvives && (own ? wiped : loss >= _constants.retreatCheckFraction);
                if (!gated)
                {
                    if (own && rivalSurvives && loss >= _constants.retreatCheckFraction)
                        plan.Holds.Add(new Hold { Planet = projection.Planet, Reason = HoldOwnPlanetNotWiped, LossFraction = loss });
                    continue;
                }

                var offense = group.Sum(s => _stats.EffectiveOffense(s));
                var candidates = Destinations(planet, offense);
                if (candidates.Count == 0)
                {
                    plan.Holds.Add(new Hold { Planet = projection.Planet, Reason = HoldNoDestination, LossFraction = loss });
                    continue;
                }
                destinations[projection.Planet] = candidates;
                byPlanet[projection.Planet] = projection;
                rows.Add(new RetreatMatrix.Row
                {
                    Planet = projection.Planet, Ships = group.Count, LossFraction = loss,
                    Candidates = candidates.Select(c => new RetreatMatrix.Candidate { Planet = c.Planet, Tier = c.Tier, PathCost = c.Cost }).ToList(),
                });
            }

            foreach (var decision in RetreatMatrix.Decide(rows, _constants))
            {
                var projection = byPlanet[decision.Planet];
                if (!decision.Retreat)
                {
                    plan.Stays.Add(new Stay { Planet = decision.Planet, LossFraction = projection.PerceivedLossFraction, PRetreat = decision.PRetreat });
                    continue;
                }
                var destination = destinations[decision.Planet].First(c => c.Planet == decision.Action.Target);
                plan.Actions.Add(decision.Action);
                plan.Retreating.Add(decision.Planet);
                var retreat = new Retreat
                {
                    Planet = decision.Planet, Destination = destination.Planet, Tier = destination.Tier, Ships = decision.Action.Count,
                    LossFraction = projection.PerceivedLossFraction, ExactLossFraction = projection.ProjectedLossFraction,
                    RivalSurvivorsFraction = projection.RivalSurvivorsFraction, RouteCost = destination.Cost, PRetreat = decision.PRetreat,
                };
                if (destination.Tier == 3)
                {
                    retreat.RememberedBlockade = _view.Value(destination.Planet);
                    retreat.MyOffense = MyWarships(_map.GetPlanet(decision.Planet)).Sum(s => _stats.EffectiveOffense(s));
                }
                plan.Retreats.Add(retreat);
            }
            return plan;
        }

        private List<Ship> MyWarships(Planet planet)
            => planet.DockedShips.Where(s => s.Kind == Ship.ShipKind.WarShip && s.Owner == _me).ToList();

        /// <summary>Every valid destination in the best (first non-empty) tier, ordered by cost then name; empty when none qualifies.</summary>
        private List<Destination> Destinations(Planet from, float groupOffense)
        {
            var known = _map.Knowledge.KnownPlanets(_me).OrderBy(n => n, StringComparer.Ordinal).ToList();
            var maxNodes = _constants.maxPathNodesForShipTransport;
            for (var tier = 1; tier <= 3; tier++)
            {
                var found = new List<Destination>();
                foreach (var name in known)
                {
                    if (name == from.PlanetName) continue;
                    var planet = _map.GetPlanet(name);
                    if (planet == null || TierOf(planet, groupOffense) != tier) continue;
                    var route = RoutePlanner.PlanRoute(_map, from.PlanetName, name, tier == 3 ? _warView.Without(name) : _warView, maxNodes);
                    if (route == null) continue;
                    found.Add(new Destination { Planet = name, Tier = tier, Cost = route.Cost });
                }
                if (found.Count > 0)
                    return found.OrderBy(d => d.Cost).ThenBy(d => d.Planet, StringComparer.Ordinal).ToList();
            }
            return new List<Destination>();
        }

        // 0 = not a destination.
        private int TierOf(Planet planet, float groupOffense)
        {
            var warRivalShips = planet.DockedShips.Any(s => s.Kind == Ship.ShipKind.WarShip && s.Owner != Planet.NoOwner && _warRivals.Contains(s.Owner));
            var blockadedByWar = _warView.IsBlockaded(planet.PlanetName);
            if (planet.Owner == _me && planet.Population.Count > 0) return !warRivalShips && !blockadedByWar ? 1 : 0;
            if (!planet.Population.Exists(p => _warRivals.Contains(p.Player))) return !warRivalShips && !blockadedByWar ? 2 : 0;
            return groupOffense > _view.Value(planet.PlanetName) * (1f + _constants.blockadeBreakMargin) ? 3 : 0;
        }
    }
}
