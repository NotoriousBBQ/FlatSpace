// AssaultPlanner.cs
using System;
using System.Collections.Generic;
using System.Linq;

namespace FlatSpace
{
    namespace AI
    {
        /// <summary>
        /// Consolidate and Amass only. Picks a known enemy-occupied planet, sizes the force to the enemy's known
        /// docked fleet, and sends the ships home defence left spare. Pure with respect to the
        /// simulation and free of Gameboard.Instance so the Editor self-check can drive it directly.
        /// </summary>
        public class AssaultPlanner
        {
            public const string ReasonCommitted = "Committed";
            public const string ReasonRecentCut = "RecentCut";
            public const string ReasonChokepoint = "Chokepoint";
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
            private readonly ISet<int> _warRivals;

            /// <summary>
            /// `view`, `stats` and `memory` are optional: without a view nothing is a blockade target and the planner behaves
            /// exactly as it did before blockade breaking existed. `turn` is only used to test BlockadeMemory for recent cuts.
            /// `warRivals` is the set of players I am at war with: only their planets are enemy-occupied and only their docked
            /// warships size the force. Null (the default, and diplomacy off) means every other player is an enemy.
            /// </summary>
            public AssaultPlanner(GameAIMap map, int playerId, BlockadeView view = null, WarshipStats stats = null,
                BlockadeMemory memory = null, int turn = 0, ISet<int> warRivals = null)
            {
                _map = map;
                _playerId = playerId;
                _constants = map.GameAIConstants;
                _view = view;
                _stats = stats;
                _memory = memory;
                _turn = turn;
                _warRivals = warRivals;
            }

            /// <summary>
            /// Planets that may not be chosen as a target this turn: a planet I just retreated from (the cooldown) or am retreating
            /// from now, so the assault does not send the same ships straight back. Null = none.
            /// </summary>
            public ISet<string> ExcludedTargets { get; set; }

            private bool IsExcluded(string planetName) => ExcludedTargets != null && ExcludedTargets.Contains(planetName);

            private bool IsRival(int player) => player != _playerId && (_warRivals == null || _warRivals.Contains(player));

            private int CountWarships(Planet planet)
                => planet.DockedShips.Count(s => s.Kind == Ship.ShipKind.WarShip && s.Owner == _playerId);

            /// <summary>A player I am at war with has population here (without a war set: any other player; same test as PlayerKnowledge.HasContact).</summary>
            public bool IsEnemyOccupied(Planet planet)
                => planet.Population.Exists(p => IsRival(p.Player));

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
                                                           && s.Owner >= 0 && IsRival(s.Owner));
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
                        sum += _stats.EffectiveOffense(ship);
                return sum;
            }

            /// <summary>
            /// Planets where my warships and another player's are both docked: a standoff I am holding. A blockade I have
            /// just broken is one: its value is 0 or less, so it has left my BlockadeView, yet if my ships left the blockade
            /// would re-form at once. The home plan keeps these ships where they are for as long as the rival stays. Empty
            /// without warship stats. With a war set, a planet a player outside it populates (and I do not) is not held unless
            /// it is blockaded against me: after peace the force goes home instead of sitting beside the rival's planet, where
            /// it would feed the rival's hostility and re-start the war. Breaking a blockade needs no war.
            /// </summary>
            public List<string> ContestedHolds()
            {
                var holds = new List<string>();
                if (_stats == null) return holds;
                foreach (var planet in _map.PlanetList)
                {
                    if (DockedOffense(planet) <= 0f) continue;
                    if (_warRivals != null && !IsBlockadeTarget(planet)
                        && !planet.Population.Exists(p => p.Player == _playerId)
                        && planet.Population.Exists(p => p.Player != _playerId && !_warRivals.Contains(p.Player)))
                        continue;
                    var rivalHere = planet.DockedShips.Any(s => s.Kind == Ship.ShipKind.WarShip
                        && s.Owner != _playerId && s.Owner != Planet.NoOwner
                        && _stats.EffectiveOffense(s) > 0f);
                    if (rivalHere) holds.Add(planet.PlanetName);
                }
                return holds;
            }

            /// <summary>My real docked warship offense at the planet. Public for the conversion-hold audit.</summary>
            public float HeldOffense(Planet planet) => DockedOffense(planet);

            /// <summary>
            /// Planets I dominate (my warships or siegeships docked with bombard, none of a player at war with me) and convert, or could: an
            /// at-war holder still has inhabitants there, or my own conversion session is running with inhabitants of others left (the
            /// non-war tail has no assault target, so only this hold keeps the fleet). Stateless; empty without warship stats and in
            /// legacy mode (nothing converts there). The home plan never strips these planets, so the dominance, and with it the
            /// session's progress, is not lost the turn after a fight is won. Retreat still runs first and wins.
            /// </summary>
            public List<string> ConversionHolds()
            {
                var holds = new List<string>();
                if (_stats == null || !_map.Diplomacy.Enabled) return holds;
                foreach (var planet in _map.PlanetList)
                {
                    if (ConversionSystem.DockedBombard(planet, _playerId, _stats) <= 0f) continue;   // dominance is by bombard
                    if (ConversionSystem.Dominator(planet, _map, _stats) != _playerId) continue;
                    if (ConversionSystem.HasStuckBunker(planet, _playerId, _map.Diplomacy)) continue;   // conversion can never run here
                    var session = planet.ConversionBy == _playerId && planet.Population.Exists(p => p.Player != _playerId);
                    var atWarHolder = planet.Population.Exists(p => p.Player != _playerId && _map.Diplomacy.IsAtWar(_playerId, p.Player, true));
                    if (session || atWarHolder) holds.Add(planet.PlanetName);
                }
                return holds;
            }

            /// <summary>
            /// For each conversion-held planet (retreating planets left out), the ships that must stay: max(1, ceil(conversionHoldKeepFraction
            /// x nearby rival offense / my offense per ship)), at most the ships docked there; every ship when the fraction is negative.
            /// The rest are free for garrison and blockade calls (see ShipTransportPlanner.ConversionKeep). Empty in legacy mode.
            /// </summary>
            public Dictionary<string, int> ConversionKeep(ICollection<string> retreating = null)
            {
                var keep = new Dictionary<string, int>();
                var fraction = _constants.conversionHoldKeepFraction;
                foreach (var name in ConversionHolds())
                {
                    if (retreating != null && retreating.Contains(name)) continue;
                    var planet = _map.GetPlanet(name);
                    var ships = CountWarships(planet);
                    if (ships <= 0) continue;
                    if (fraction < 0f) { keep[name] = ships; continue; }
                    var rival = NearbyRivalOffense(planet);
                    var perShip = DockedOffense(planet) / ships;
                    var needed = rival <= 0f || perShip <= 0f ? 1 : (int)Math.Ceiling(fraction * rival / perShip);
                    keep[name] = Math.Max(1, Math.Min(ships, needed));
                }
                return keep;
            }

            /// <summary>
            /// The largest single at-war rival's docked offense on the planet and its known neighbours: what could come back to
            /// break a hold. Reporting only (the ConversionHoldSpare audit).
            /// </summary>
            public float NearbyRivalOffense(Planet planet)
            {
                if (_stats == null) return 0f;
                var names = new List<string> { planet.PlanetName };
                names.AddRange(_map.GetNeighbours(planet.PlanetName));
                var perPlayer = new Dictionary<int, float>();
                foreach (var name in names.Distinct())
                {
                    if (!_map.Knowledge.IsKnown(_playerId, name)) continue;
                    var near = _map.GetPlanet(name);
                    if (near == null) continue;
                    foreach (var ship in near.DockedShips)
                    {
                        if (ship.Kind != Ship.ShipKind.WarShip || ship.Owner == Planet.NoOwner || ship.Owner == _playerId) continue;
                        if (!_map.Diplomacy.IsAtWar(_playerId, ship.Owner, true)) continue;
                        perPlayer[ship.Owner] = (perPlayer.TryGetValue(ship.Owner, out var sum) ? sum : 0f) + _stats.EffectiveOffense(ship);
                    }
                }
                return perPlayer.Count == 0 ? 0f : perPlayer.Values.Max();
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
                public float  Chokepoint;
                public float  Needed;
                public float  Cost;
            }

            /// <summary>
            /// The blockaded, reachable planet to break. Ranked: most of my offense committed there, then a recent cut of one
            /// of my orders, then the more central planet (a chokepoint, higher percentile first), then the smallest offense
            /// still needed, then the cheapest path from a holder of my warships, then name. `reason` says which step decided it (null with no target). A planet is reachable when I have ships
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
                    if (planet == null || IsExcluded(name)) continue;

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
                        // Only real chokepoints (at or above chokepointPercentile) outrank others; everything else ties at 0,
                        // so a percentile above 1 switches the step off and the smaller need decides again.
                        Chokepoint = _map.IsChokepoint(name) ? _map.Chokepoint(name) : 0f,
                        Needed    = NeededOffense(planet),
                        Cost      = cost.Value,
                    });
                }
                if (candidates.Count == 0) return null;

                var ranked = candidates
                    .OrderByDescending(c => c.Committed)
                    .ThenByDescending(c => c.Recent)
                    .ThenByDescending(c => c.Chokepoint)
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
                else if (best.Chokepoint != ranked[1].Chokepoint) reason = ReasonChokepoint;
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
                    if (planet == null || IsExcluded(name) || !IsEnemyOccupied(planet) || IsHeldByMe(planet)) continue;

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

            // ── Siegeships (assault composition) ─────────────────────────────

            /// <summary>
            /// Task 14 seam: may `count` siegeships leave `origin` for `target` now? Null (the default) allows every send. PlayerAI sets it
            /// to PlayerAI.CanSendSiegeship (the arrival-safety gate). A refused source is skipped; the next source is tried.
            /// </summary>
            public Func<string, string, int, bool> SiegeshipGate { get; set; }

            /// <summary>A war rival's (any other player's without a war set) bunker is docked here.</summary>
            private bool HoldsRivalBunker(Planet planet)
                => planet.DockedShips.Exists(s => s.Kind == Ship.ShipKind.Bunker && s.Owner >= 0 && IsRival(s.Owner));

            /// <summary>
            /// The summed Defense stat (WarshipStats, the bunker's own research line) of the war rivals' bunkers on the target, as far as I
            /// can see it: the target must be a planet I know, the way the force sizing sees docked warships. 0 for an unknown planet, with
            /// no bunkers, or without warship stats.
            /// </summary>
            public float KnownBunkerDefense(Planet target)
            {
                if (_stats == null || target == null || !_map.Knowledge.IsKnown(_playerId, target.PlanetName)) return 0f;
                var sum = 0f;
                foreach (var ship in target.DockedShips)
                    if (ship.Kind == Ship.ShipKind.Bunker && ship.Owner >= 0 && IsRival(ship.Owner))
                        sum += _stats.Defense(ship);
                return sum;
            }

            /// <summary>Bombard the assault wants at the target: KnownBunkerDefense x siegeBombardMargin.</summary>
            public float RequiredBombard(Planet target) => KnownBunkerDefense(target) * _constants.siegeBombardMargin;

            private int CountSiegeships(Planet planet)
                => planet.DockedShips.Count(s => s.Kind == Ship.ShipKind.SiegeShip && s.Owner == _playerId);

            /// <summary>My siegeships docked at the target plus those in flight there.</summary>
            public int CommittedSiegeships(Planet target)
                => CountSiegeships(target) + target.GetIncomingShips(Ship.ShipKind.SiegeShip, _playerId);

            /// <summary>My siegeships' effective bombard at the target: docked (health-scaled) plus in flight (GetIncomingBombard). 0 without stats.</summary>
            public float CommittedSiegeBombard(Planet target)
            {
                if (_stats == null) return 0f;
                var sum = target.GetIncomingBombard(Ship.ShipKind.SiegeShip, _playerId);
                foreach (var ship in target.DockedShips)
                    if (ship.Kind == Ship.ShipKind.SiegeShip && ship.Owner == _playerId)
                        sum += _stats.EffectiveBombard(ship);
                return sum;
            }

            /// <summary>
            /// The most siegeships the assault fleet at the target may hold, so they are at most siegeshipMaxFleetShare of warships +
            /// siegeships: floor(share x W / (1 - share)), W = max(RequiredForce, my warships docked + in flight there). A share of 0 or
            /// less gives 0, 1 or more no cap (int.MaxValue). The small epsilon keeps a product that is whole in decimal from flooring
            /// one short.
            /// </summary>
            public int SiegeshipCap(Planet target)
            {
                var share = _constants.siegeshipMaxFleetShare;
                if (share <= 0f) return 0;
                if (share >= 1f) return int.MaxValue;
                var warships = Math.Max(RequiredForce(),
                    CountWarships(target) + target.GetIncomingShips(Ship.ShipKind.WarShip, _playerId));
                return (int)Math.Floor(share * warships / (1f - share) + 1e-4f);
            }

            /// <summary>
            /// Siegeships still to send to the target: ceil((RequiredBombard - CommittedSiegeBombard) / bombardPerSiegeship), at most the
            /// room left under SiegeshipCap (cap - CommittedSiegeships), never below 0. 0 when nothing is needed or a siegeship adds no
            /// bombard. Production wants CommittedSiegeships + this (PlayerAI.WantedSiegeships).
            /// </summary>
            public int WantedSiegeships(Planet target, float bombardPerSiegeship)
            {
                if (target == null || bombardPerSiegeship <= 0f) return 0;
                var remaining = RequiredBombard(target) - CommittedSiegeBombard(target);
                if (remaining <= 0f) return 0;
                var need = (int)Math.Ceiling(remaining / bombardPerSiegeship - 1e-4f);
                var room = Math.Max(0, SiegeshipCap(target) - CommittedSiegeships(target));
                return Math.Min(need, room);
            }

            /// <summary>
            /// The siegeship part of the assault: siegeships docked on my other planets (none from a planet where a war rival's bunker
            /// stands: they are bombarding it; none from a conversion hold: their bombard sets its conversion pace), cheapest usable path first, each ship counted at its own effective bombard, until the
            /// bombard still needed is covered or the cap's room is used. Each source is one action of Kind SiegeShip; SiegeshipGate
            /// may refuse a source. Never waits for anything: with no siegeship available it returns nothing.
            /// </summary>
            private List<ShipAction> PlanSiegeships(Planet target)
            {
                var actions = new List<ShipAction>();
                if (_stats == null) return actions;
                var remaining = RequiredBombard(target) - CommittedSiegeBombard(target);
                var room = Math.Max(0, SiegeshipCap(target) - CommittedSiegeships(target));
                if (remaining <= 0f || room <= 0) return actions;

                var template = _constants.siegeShipData;
                var subtype = WarshipStats.SubtypeOf(Ship.ShipKind.SiegeShip);
                // Siegeships at a conversion hold drive its conversion pace (bombard-scaled): they stay. Computed once per call.
                var conversionHolds = new HashSet<string>(ConversionHolds());
                var sources = new List<Source>();
                foreach (var planet in _map.PlanetList)
                {
                    if (planet == target) continue;
                    var docked = CountSiegeships(planet);
                    if (docked <= 0 || HoldsRivalBunker(planet) || conversionHolds.Contains(planet.PlanetName)) continue;
                    if (!planet.DistanceMapToPathingList.TryGetValue(target.PlanetName, out var entry) || !IsUsablePath(entry)) continue;
                    sources.Add(new Source { Name = planet.PlanetName, Remaining = docked, Cost = entry.Cost });
                }

                foreach (var source in sources.OrderBy(s => s.Cost).ThenBy(s => s.Name, StringComparer.Ordinal))
                {
                    if (remaining <= 0f || room <= 0) break;
                    var sourcePlanet = _map.GetPlanet(source.Name);
                    var available = Math.Min(source.Remaining, room);
                    var snapshots = sourcePlanet.PeekShipSnapshots(Ship.ShipKind.SiegeShip, _playerId, available);
                    var damages = sourcePlanet.PeekShipDamage(Ship.ShipKind.SiegeShip, _playerId, available);
                    var taken = 0;
                    var bombard = 0f;
                    for (var i = 0; i < snapshots.Count && remaining - bombard > 0f; i++)
                    {
                        bombard += _stats.EffectiveBombard(template, snapshots[i], damages[i], subtype);
                        taken++;
                    }
                    if (taken == 0) continue;
                    if (SiegeshipGate != null && !SiegeshipGate(source.Name, target.PlanetName, taken)) continue;

                    actions.Add(new ShipAction
                    {
                        Origin = source.Name,
                        Target = target.PlanetName,
                        Cost   = source.Cost,
                        Count  = taken,
                        Kind   = Ship.ShipKind.SiegeShip,
                    });
                    remaining -= bombard;
                    room -= taken;
                }
                return actions;
            }

            /// <summary>
            /// Sends spare ships to the target, cheapest path first. An ordinary target is sized by ship count until the
            /// deficit is met; a blockaded target is sized by real offense until NeededOffense is covered (or every spare
            /// ship is sent). A source's spare is its state's Spare minus what the home actions already send from it.
            /// Then the siegeship part (PlanSiegeships) is appended as separate actions of Kind SiegeShip: the warships leave
            /// with what exists whether or not any siegeship is available (no stall), and siegeships that exist go the same turn.
            /// </summary>
            public List<ShipAction> Plan(Planet target, List<ShipTransportPlanner.PlanetState> states,
                List<ShipAction> homeActions)
            {
                LastBlockadeForce = default;
                if (target == null) return new List<ShipAction>();
                var actions = PlanWarships(target, states, homeActions);
                actions.AddRange(PlanSiegeships(target));
                return actions;
            }

            private List<ShipAction> PlanWarships(Planet target, List<ShipTransportPlanner.PlanetState> states,
                List<ShipAction> homeActions)
            {
                var actions = new List<ShipAction>();
                // A source's warship spare is reduced by the warships already leaving it (siegeship actions do not claim warships).
                var sentByOrigin = homeActions
                    .Where(a => a.Kind != Ship.ShipKind.SiegeShip)
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
                    var sourcePlanet = _map.GetPlanet(source.Name);
                    var snapshots = sourcePlanet.PeekShipSnapshots(Ship.ShipKind.WarShip, _playerId, source.Remaining, skip);
                    var damages = sourcePlanet.PeekShipDamage(Ship.ShipKind.WarShip, _playerId, source.Remaining, skip);

                    var taken = 0;
                    for (var i = 0; i < snapshots.Count; i++)
                    {
                        if (needed <= 0f) break;
                        // A wounded ship sends less offense: the force is sized by what each ship deals now.
                        var shipOffense = _stats != null ? _stats.EffectiveOffense(template, snapshots[i], damages[i]) : 0f;
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
