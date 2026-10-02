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
            public const string ReasonCommitted = "Committed";
            public const string ReasonRecentCut = "RecentCut";
            public const string ReasonCheapest  = "Cheapest";
            public const string SkipNoPath      = "NoPath";
            public const string SkipOutranked   = "Outranked";

            private readonly GameAIMap _map;
            private readonly int _playerId;
            private readonly GameAIConstants _constants;
            private readonly BlockadeView _view;
            private readonly WarshipStats _stats;
            private readonly BlockadeMemory _memory;
            private readonly int _turn;

            /// <summary>
            /// `view`, `stats` and `memory` are optional: without a view nothing is a blockade target and the planner behaves
            /// exactly as it did before blockade breaking existed. `turn` is only used to test BlockadeMemory for recent cuts.
            /// </summary>
            public AssaultPlanner(GameAIMap map, int playerId, BlockadeView view = null, WarshipStats stats = null,
                BlockadeMemory memory = null, int turn = 0)
            {
                _map = map;
                _playerId = playerId;
                _constants = map.GameAIConstants;
                _view = view;
                _stats = stats;
                _memory = memory;
                _turn = turn;
            }

            private int CountWarships(Planet planet)
                => planet.DockedShips.Count(s => s.Kind == Ship.ShipKind.WarShip && s.Owner == _playerId);

            /// <summary>Another player's population is present (same test as PlayerKnowledge.HasContact).</summary>
            public bool IsEnemyOccupied(Planet planet)
                => planet.Population.Exists(p => p.Player != _playerId);

            /// <summary>
            /// A planet I colonize (same test as ShipTransportPlanner.IsColonized). It may still hold a few
            /// enemy colonists, but it is home ground, not an assault target: my own garrison there would
            /// otherwise make it the sticky target forever.
            /// </summary>
            public bool IsHeldByMe(Planet planet)
                => planet.Owner == _playerId && planet.Population.Count > 0;

            /// <summary>
            /// A known planet with enemy population that I do not colonize exists. Unlike ChooseTarget
            /// this needs no warships and no path, so production can ask "is there anyone to attack?"
            /// even while the fleet is empty.
            /// </summary>
            public bool HasKnownEnemyPlanet()
            {
                foreach (var name in _map.Knowledge.KnownPlanets(_playerId))
                {
                    var planet = _map.GetPlanet(name);
                    if (planet != null && IsEnemyOccupied(planet) && !IsHeldByMe(planet)) return true;
                }
                return false;
            }

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
                               - (CountWarships(target) + target.GetIncomingShips(Ship.ShipKind.WarShip, _playerId)));

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

            // ── Blockade breaking ────────────────────────────────────────────

            /// <summary>The planet is blockaded against me in my view.</summary>
            public bool IsBlockadeTarget(Planet planet)
                => _view != null && planet != null && _view.IsBlockaded(planet.PlanetName);

            /// <summary>My real warship offense docked at the planet (0 without WarshipStats).</summary>
            private float DockedOffense(Planet planet)
            {
                if (_stats == null) return 0f;
                var sum = 0f;
                foreach (var ship in planet.DockedShips)
                    if (ship.Kind == Ship.ShipKind.WarShip && ship.Owner == _playerId)
                        sum += _stats.Offense(ship.Template, ship.ResearchSnapshot);
                return sum;
            }

            /// <summary>
            /// Planets where my warships and another player's are both docked: a standoff I am holding. A blockade I have
            /// just broken is one: its value is 0 or less, so it has left my BlockadeView, yet if my ships left the blockade
            /// would re-form at once. The home plan keeps these ships where they are for as long as the rival stays. Empty
            /// without warship stats.
            /// </summary>
            public List<string> ContestedHolds()
            {
                var holds = new List<string>();
                if (_stats == null) return holds;
                foreach (var planet in _map.PlanetList)
                {
                    if (DockedOffense(planet) <= 0f) continue;
                    var rivalHere = planet.DockedShips.Any(s => s.Kind == Ship.ShipKind.WarShip
                        && s.Owner != _playerId && s.Owner != Planet.NoOwner
                        && _stats.Offense(s.Template, s.ResearchSnapshot) > 0f);
                    if (rivalHere) holds.Add(planet.PlanetName);
                }
                return holds;
            }

            /// <summary>My offense committed at the planet: docked plus in flight.</summary>
            public float CommittedOffense(Planet planet)
                => DockedOffense(planet) + planet.GetIncomingOffense(Ship.ShipKind.WarShip, _playerId);

            /// <summary>
            /// Offense still to send to break the blockade: value x (1 + margin) minus offense already in flight. The view's
            /// value already subtracts my docked offense. 0 when the planet is not blockaded; at or below 0 when covered.
            /// </summary>
            public float NeededOffense(Planet planet)
            {
                if (!IsBlockadeTarget(planet)) return 0f;
                return _view.Value(planet.PlanetName) * (1f + _constants.blockadeBreakMargin)
                       - planet.GetIncomingOffense(Ship.ShipKind.WarShip, _playerId);
            }

            /// <summary>
            /// The blockaded planets the last ChooseBlockadeTarget call passed over and why (no usable path, or outranked by
            /// the winner, with the winner's committed offense). Reporting only: it never changes the choice. Replaced on
            /// every call; empty before the first and without a view.
            /// </summary>
            public List<BlockadeSkipTracker.Skip> LastSkipped { get; private set; } = new List<BlockadeSkipTracker.Skip>();

            private struct BlockadeCandidate
            {
                public Planet Planet;
                public float  Committed;
                public bool   Recent;
                public float  Needed;
                public float  Cost;
            }

            /// <summary>
            /// The blockaded, reachable planet to break. Ranked: most of my offense committed there, then a recent cut of one
            /// of my orders, then the smallest offense still needed, then the cheapest path from a holder of my warships,
            /// then name. `reason` says which step decided it (null with no target). A planet is reachable when I have ships
            /// committed there or a usable path from a planet holding my warships; remembered, unseen planets count.
            /// </summary>
            public Planet ChooseBlockadeTarget(out string reason)
            {
                reason = null;
                var skipped = new List<BlockadeSkipTracker.Skip>();
                LastSkipped = skipped;
                if (_view == null) return null;

                var holders = _map.PlanetList.Where(p => CountWarships(p) > 0).ToList();
                var candidates = new List<BlockadeCandidate>();
                foreach (var name in _view.BlockadedNames.OrderBy(n => n, StringComparer.Ordinal))
                {
                    var planet = _map.GetPlanet(name);
                    if (planet == null) continue;

                    var own = CountWarships(planet) + planet.GetIncomingShips(Ship.ShipKind.WarShip, _playerId);
                    var cost = own > 0 ? 0f : CheapestPathCost(holders, planet);
                    if (cost == null)
                    {
                        skipped.Add(new BlockadeSkipTracker.Skip
                        {
                            Planet = name, Reason = SkipNoPath, Winner = "-", Value = _view.Value(name), WinnerCommitted = 0f,
                        });
                        continue;
                    }

                    candidates.Add(new BlockadeCandidate
                    {
                        Planet    = planet,
                        Committed = CommittedOffense(planet),
                        Recent    = _memory != null
                                    && _memory.IsActive(name, _turn, _constants.blockadeTargetRecentTurns),
                        Needed    = NeededOffense(planet),
                        Cost      = cost.Value,
                    });
                }
                if (candidates.Count == 0) return null;

                var ranked = candidates
                    .OrderByDescending(c => c.Committed)
                    .ThenByDescending(c => c.Recent)
                    .ThenBy(c => c.Needed)
                    .ThenBy(c => c.Cost)
                    .ThenBy(c => c.Planet.PlanetName, StringComparer.Ordinal)
                    .ToList();
                var best = ranked[0];
                foreach (var other in ranked.Skip(1))
                    skipped.Add(new BlockadeSkipTracker.Skip
                    {
                        Planet          = other.Planet.PlanetName,
                        Reason          = SkipOutranked,
                        Winner          = best.Planet.PlanetName,
                        Value           = _view.Value(other.Planet.PlanetName),
                        WinnerCommitted = best.Committed,
                    });
                if (ranked.Count == 1)
                    reason = best.Committed > 0f ? ReasonCommitted : best.Recent ? ReasonRecentCut : ReasonCheapest;
                else if (best.Committed != ranked[1].Committed) reason = ReasonCommitted;
                else if (best.Recent != ranked[1].Recent) reason = ReasonRecentCut;
                else reason = ReasonCheapest;
                return best.Planet;
            }

            // ── Targets ──────────────────────────────────────────────────────

            /// <summary>A blockaded planet first (see ChooseBlockadeTarget), else the enemy-occupied rule.</summary>
            public Planet ChooseTarget() => ChooseBlockadeTarget(out _) ?? ChooseEnemyTarget();

            /// <summary>
            /// The known, reachable, enemy-occupied planet where I already have the most warships
            /// docked + incoming (sticky); otherwise the cheapest path from a planet holding warships;
            /// ties by name. Null when there is no such planet.
            /// </summary>
            public Planet ChooseEnemyTarget()
            {
                var holders = _map.PlanetList.Where(p => CountWarships(p) > 0).ToList();
                if (holders.Count == 0) return null;

                Planet best = null;
                var bestOwn = -1;
                var bestCost = float.MaxValue;
                foreach (var name in _map.Knowledge.KnownPlanets(_playerId).OrderBy(n => n, StringComparer.Ordinal))
                {
                    var planet = _map.GetPlanet(name);
                    if (planet == null || !IsEnemyOccupied(planet) || IsHeldByMe(planet)) continue;

                    var own = CountWarships(planet) + planet.GetIncomingShips(Ship.ShipKind.WarShip, _playerId);
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

            /// <summary>What the last Plan call sent at a blockade target; all zero for any other target.</summary>
            public struct BlockadeForce
            {
                public int   Ships;
                public float Offense;
                public float StillNeeded;
            }

            public BlockadeForce LastBlockadeForce { get; private set; }

            private struct Source
            {
                public string Name;
                public int    Remaining;
                public float  Cost;
            }

            /// <summary>The ships a planet can spare for `target`, cheapest path first, ties by name.</summary>
            private List<Source> SpareSources(Planet target, List<ShipTransportPlanner.PlanetState> states,
                Dictionary<string, int> sentByOrigin)
            {
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
                return sources.OrderBy(s => s.Cost).ThenBy(s => s.Name, StringComparer.Ordinal).ToList();
            }

            /// <summary>
            /// Sends spare ships to the target, cheapest path first. An ordinary target is sized by ship count until the
            /// deficit is met; a blockaded target is sized by real offense until NeededOffense is covered (or every spare
            /// ship is sent). A source's spare is its state's Spare minus what the home actions already send from it.
            /// </summary>
            public List<ShipAction> Plan(Planet target, List<ShipTransportPlanner.PlanetState> states,
                List<ShipAction> homeActions)
            {
                LastBlockadeForce = default;
                var actions = new List<ShipAction>();
                if (target == null) return actions;

                var sentByOrigin = homeActions
                    .GroupBy(a => a.Origin)
                    .ToDictionary(g => g.Key, g => g.Sum(a => a.Count));

                if (IsBlockadeTarget(target))
                    return PlanBlockadeForce(target, states, sentByOrigin);

                var deficit = Deficit(target);
                if (deficit <= 0) return actions;

                foreach (var source in SpareSources(target, states, sentByOrigin))
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

            // Walks the sources and, for each, the exact ships that would leave (the first docked ones after those home
            // defence claimed), subtracting each ship's real offense from what is still needed. Without stats a ship
            // counts 0, so every spare ship is sent.
            private List<ShipAction> PlanBlockadeForce(Planet target, List<ShipTransportPlanner.PlanetState> states,
                Dictionary<string, int> sentByOrigin)
            {
                var actions = new List<ShipAction>();
                var needed = NeededOffense(target);
                if (needed <= 0f) return actions;

                var template = _constants.warShipData;
                var ships = 0;
                var offense = 0f;
                foreach (var source in SpareSources(target, states, sentByOrigin))
                {
                    if (needed <= 0f) break;
                    sentByOrigin.TryGetValue(source.Name, out var skip);
                    var snapshots = _map.GetPlanet(source.Name)
                        .PeekShipSnapshots(Ship.ShipKind.WarShip, _playerId, source.Remaining, skip);

                    var taken = 0;
                    foreach (var snapshot in snapshots)
                    {
                        if (needed <= 0f) break;
                        var shipOffense = _stats != null ? _stats.Offense(template, snapshot) : 0f;
                        needed -= shipOffense;
                        offense += shipOffense;
                        taken++;
                    }
                    if (taken == 0) continue;

                    actions.Add(new ShipAction
                    {
                        Origin = source.Name,
                        Target = target.PlanetName,
                        Cost   = source.Cost,
                        Count  = taken,
                        Kind   = Ship.ShipKind.WarShip,
                    });
                    ships += taken;
                }
                LastBlockadeForce = new BlockadeForce { Ships = ships, Offense = offense, StillNeeded = Math.Max(0f, needed) };
                return actions;
            }
        }
    }
}
