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

            // ── Round and roles ──────────────────────────────────────────────

            public class PlanetState
            {
                public Planet Planet;
                public int Category;
                public int Garrison;        // base garrison
                public int Docked;
                public int Incoming;
                public int RoundGarrison;   // Garrison x current round
                public int Spare   => Math.Max(0, Docked - RoundGarrison);
                public int Deficit => Math.Max(0, RoundGarrison - (Docked + Incoming));
            }

            /// <summary>Current garrison round set by the last BuildStates call.</summary>
            public int LastRound { get; private set; } = 1;

            public int CountWarships(Planet planet)
                => planet.DockedShips.Count(s => s.Kind == Ship.ShipKind.WarShip && s.Owner == _playerId);

            /// <summary>
            /// Participating planets with this turn's round garrison filled in. Locked
            /// category-5-only planets are left out entirely (neither source nor target).
            /// </summary>
            public List<PlanetState> BuildStates()
            {
                var colonized = _map.PlanetList.Where(IsColonized).ToList();
                // Planets the player holds ships on but no longer has colonized (ownership can flip
                // while a fleet is in flight): they are source-only so those ships are not stranded.
                var stranded = _map.PlanetList.Where(p => !IsColonized(p) && CountWarships(p) > 0).ToList();
                // Ships in flight still belong to the player, so they count toward the unlock total.
                var totalWarships = colonized.Concat(stranded)
                    .Sum(p => CountWarships(p) + p.GetIncomingShips(Ship.ShipKind.WarShip));
                var category5Unlocked = totalWarships
                    >= _constants.category5UnlockShipsPerColonizedPlanet * colonized.Count;

                var states = new List<PlanetState>();
                foreach (var planet in stranded)
                {
                    states.Add(new PlanetState
                    {
                        Planet   = planet,
                        Category = NoCategory,
                        Garrison = 0,
                        Docked   = CountWarships(planet),
                        Incoming = planet.GetIncomingShips(Ship.ShipKind.WarShip),
                    });
                }
                foreach (var planet in colonized)
                {
                    var categories = ApplicableCategories(planet);
                    if (!category5Unlocked && categories.Count == 1 && categories[0] == 5) continue;
                    states.Add(new PlanetState
                    {
                        Planet   = planet,
                        Category = categories.Count == 0 ? NoCategory : categories.Min(),
                        Garrison = GarrisonOf(categories),
                        Docked   = CountWarships(planet),
                        Incoming = planet.GetIncomingShips(Ship.ShipKind.WarShip),
                    });
                }

                LastRound = ComputeRound(states);
                foreach (var state in states) state.RoundGarrison = state.Garrison * LastRound;
                return states;
            }

            // r = 1 + min(floor((docked + incoming) / garrison)) over planets that have a garrison
            // and can be reached from another participant. Unreachable planets are excluded so one
            // stranded planet cannot pin the round at 1 forever.
            private int ComputeRound(List<PlanetState> states)
            {
                var pool = states.Where(s => s.Garrison > 0 && HasReachablePeer(s, states)).ToList();
                if (pool.Count == 0) return 1;
                return 1 + pool.Min(s => (s.Docked + s.Incoming) / s.Garrison);
            }

            // A path between two distinct planets always has at least 2 nodes. PathingSystem.FindPath
            // returns a 1-node, zero-cost "path" (it does not throw) when no route exists, so
            // NumNodes < 2 means unreachable and must not be mistaken for a free, adjacent trip.
            private bool IsUsablePath(GameAIMap.DestinationToPathingListEntry entry)
                => entry.NumNodes >= 2 && entry.NumNodes <= _constants.maxPathNodesForShipTransport;

            private bool HasReachablePeer(PlanetState state, List<PlanetState> states)
            {
                var paths = state.Planet.DistanceMapToPathingList;
                return states.Any(other => other != state
                    && paths.TryGetValue(other.Planet.PlanetName, out var entry)
                    && IsUsablePath(entry));
            }

            // ── Matrix / plan ────────────────────────────────────────────────

            /// <summary>
            /// One row per source planet (largest spare first); choices are reachable target planets
            /// ordered by category then path cost. ScoreMatrix removes a chosen target from every
            /// other row, so each target is claimed once per turn. Count = min(spare, deficit).
            /// </summary>
            public List<ShipAction> Plan()
            {
                var actions = new List<ShipAction>();
                var states = BuildStates();
                if (states.Count == 0) return actions;

                var sources = states.Where(s => s.Spare > 0)
                    .OrderByDescending(s => s.Spare)
                    .ThenBy(s => s.Planet.PlanetName, StringComparer.Ordinal)
                    .ToList();
                var targets = states.Where(s => s.Deficit > 0).ToList();
                if (sources.Count == 0 || targets.Count == 0) return actions;

                var matrix = new ScoreMatrix<ScoreMatrixDecisionElement, ShipChoiceElement, ShipAction>(
                    new ScoreMatrixDecisionComparer());

                for (var i = 0; i < sources.Count; i++)
                {
                    var source = sources[i];
                    var paths = source.Planet.DistanceMapToPathingList;
                    var entries = targets
                        .Where(t => t != source
                                    && paths.TryGetValue(t.Planet.PlanetName, out var e)
                                    && IsUsablePath(e))
                        .Select(t => new ShipChoiceElement
                        {
                            TargetPlanet = t.Planet.PlanetName,
                            Category     = t.Category,
                            PathCost     = paths[t.Planet.PlanetName].Cost,
                            SpareShips   = source.Spare,
                            Deficit      = t.Deficit,
                        })
                        .ToList();
                    if (entries.Count == 0) continue;

                    // Priority must be UNIQUE per row: ScoreMatrixDecisionComparer can report two
                    // distinct rows as equal when their positive priorities tie (SortedDictionary
                    // would then throw). Rank order (largest spare first) is the priority.
                    matrix.MatrixElements.Add(
                        new ScoreMatrixDecisionElement
                        {
                            Target   = source.Planet.PlanetName,
                            Priority = sources.Count - i,
                        },
                        entries);
                }

                foreach (var action in matrix.GenerateActionList(
                             (origin, element) => new ShipAction
                             {
                                 Origin = origin.Target,
                                 Target = element.Target,
                                 Cost   = element.PathCost,
                                 Count  = (int)Math.Min(element.SpareShips, element.Deficit),
                                 Kind   = Ship.ShipKind.WarShip,
                             },
                             CompareChoices))
                {
                    if (action.Count > 0) actions.Add(action);
                }
                return actions;
            }

            private static int CompareChoices(ShipChoiceElement a, ShipChoiceElement b)
            {
                var byCategory = a.Category.CompareTo(b.Category);
                if (byCategory != 0) return byCategory;
                var byCost = a.PathCost.CompareTo(b.PathCost);
                return byCost != 0 ? byCost : string.CompareOrdinal(a.TargetPlanet, b.TargetPlanet);
            }
        }
    }
}
