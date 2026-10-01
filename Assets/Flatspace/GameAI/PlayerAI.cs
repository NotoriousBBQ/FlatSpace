// PlayerAI.cs
using System;
using System.Collections.Generic;
using System.Linq;
using FlatSpace.Game;
using Flatspace.Objects.Production;
using Flatspace.Objects.Resource;
using Unity.VisualScripting;
using UnityEngine;

namespace FlatSpace
{
    namespace AI
    {
        public class PlayerAI : MonoBehaviour
        {
            // ── Public state ─────────────────────────────────────────────────

            public enum AIStrategy
            {
                AIStrategyNone = 0,
                AIStrategyExpand,
                AIStrategyConsolidate,
                AIStrategyAmass
            }

            public AIStrategy Strategy { get; set; } = AIStrategy.AIStrategyExpand;
            public Player      Player  { get; set; }
            public GameAIMap   AIMap   { get; set; }

            public Catalog ProductionCatalog { get; set; }
            public Catalog ResearchCatalog   { get; set; }

            // ── Blockade awareness ───────────────────────────────────────────

            private BlockadeView _blockadeView;

            /// <summary>This player's view of visible blockades, rebuilt at the start of every ProcessResults; null before the first.</summary>
            public BlockadeView CurrentBlockadeView => _blockadeView;

            /// <summary>Rebuilds the view from current docked ships. Public for the self-check.</summary>
            public void RefreshBlockadeView(int turn = -1)
            {
                if (turn < 0) turn = Gameboard.Instance != null ? Gameboard.Instance.TurnNumber : 0;
                var items = ResearchCatalog != null ? ResearchCatalog.catalogItems : null;
                _blockadeView = BlockadeView.Build(AIMap, Player.playerID, new BlockadeSystem(AIMap, items),
                    _blockadeMemory, turn, AIMap.GameAIConstants.blockadeMemoryTurns);
            }

            // Planets where one of this player's own orders was cut by a blockade, remembered for a while so colonization
            // routes around them even when the player cannot currently see them (see BlockadeMemory).
            private readonly BlockadeMemory _blockadeMemory = new BlockadeMemory();

            /// <summary>Remembers a planet where one of this player's orders was cut. True (and logged) only when newly remembered.</summary>
            public bool LearnBlockade(string planetName, float value, int turn)
            {
                var news = _blockadeMemory.Learn(planetName, value, turn, AIMap.GameAIConstants.blockadeMemoryTurns);
                if (news) AITuningLogger.LogBlockadeLearned(turn, Player.playerID, planetName, value);
                return news;
            }

            public bool IsBlockadeRemembered(string planetName, int turn)
                => _blockadeMemory.IsActive(planetName, turn, AIMap.GameAIConstants.blockadeMemoryTurns);

            /// <summary>The active remembered blockades, for saving.</summary>
            public List<BlockadeMemory.Entry> RememberedBlockades(int turn)
                => _blockadeMemory.Snapshot(turn, AIMap.GameAIConstants.blockadeMemoryTurns);

            public void RestoreRememberedBlockades(IEnumerable<BlockadeMemory.Entry> entries)
                => _blockadeMemory.Restore(entries);

            // A null view (self-checks, before the first turn) means nothing is known to be blockaded.
            private bool IsBlockaded(string planetName)
                => _blockadeView != null && _blockadeView.IsBlockaded(planetName);

            // Why each ready colonizer was last held back (planet name -> reason), so ColonizeCancelled is logged when that
            // changes and not on every turn the condition holds. Log-only state: not saved (a load re-logs it once).
            private readonly Dictionary<string, string> _colonizeHeldBack = new Dictionary<string, string>();

            /// <summary>The reason this planet's colonizer was last held back, or null when it is not. Public for the self-check.</summary>
            public string ColonizeHeldBackReason(string planetName)
                => _colonizeHeldBack.TryGetValue(planetName, out var reason) ? reason : null;

            /// <summary>
            /// Records that a ready colonizer was held back. True when this is news (its first turn held back, or a different
            /// reason than last time), i.e. the caller should log it. Public for the self-check.
            /// </summary>
            public bool NoteColonizeHeldBack(string planetName, string reason)
            {
                if (_colonizeHeldBack.TryGetValue(planetName, out var last) && last == reason) return false;
                _colonizeHeldBack[planetName] = reason;
                return true;
            }

            // Why each shortage last went unsupplied ("<transportType>:<target>" -> reason), so ShipmentCancelled is logged on
            // a change and not every turn the blockade holds. Log-only state: not saved.
            private readonly Dictionary<string, string> _shipmentHeldBack = new Dictionary<string, string>();

            /// <summary>
            /// Why a shortage went unsupplied: the dropped (source, shortage) pair closest to shipping, for the
            /// ShipmentCancelled log line (log-only, not saved). BlockedNodes lists the route's blockaded planets with their
            /// values ("Normal 23=22,Farm 4=10", "-" when none).
            /// </summary>
            public class ShipmentHold
            {
                public string Reason;       // "Blockade" (loss >= amount) or "LowYield" (below the minimum delivered fraction)
                public string Source;
                public float Loss;
                public float Amount;
                public string BlockedNodes;
            }

            private readonly Dictionary<string, ShipmentHold> _shipmentHeldBackDetail = new Dictionary<string, ShipmentHold>();

            /// <summary>The record behind the last ShipmentCancelled for this shortage, or null. Public for the self-check.</summary>
            public ShipmentHold ShipmentHeldBackDetail(GameAI.GameAIOrder.OrderType transportType, string target)
                => _shipmentHeldBackDetail.TryGetValue(ShipmentKey(transportType, target), out var detail) ? detail : null;

            /// <summary>The route's planets the player sees as blockaded, with their values, for the ShipmentCancelled line.</summary>
            public string BlockedNodeSummary(IEnumerable<string> routeNodes)
            {
                if (_blockadeView == null || routeNodes == null) return "-";
                var parts = routeNodes.Where(n => _blockadeView.Value(n) > 0f)
                    .Select(n => n + "=" + _blockadeView.Value(n).ToString("0.#", System.Globalization.CultureInfo.InvariantCulture))
                    .ToList();
                return parts.Count == 0 ? "-" : string.Join(",", parts);
            }

            private void ForgetShipmentHold(string key)
            {
                _shipmentHeldBack.Remove(key);
                _shipmentHeldBackDetail.Remove(key);
            }

            private static string ShipmentKey(GameAI.GameAIOrder.OrderType transportType, string target)
                => $"{transportType}:{target}";

            /// <summary>The reason this shortage was last left unsupplied by blockades, or null. Public for the self-check.</summary>
            public string ShipmentHeldBackReason(GameAI.GameAIOrder.OrderType transportType, string target)
                => _shipmentHeldBack.TryGetValue(ShipmentKey(transportType, target), out var reason) ? reason : null;

            /// <summary>True when this is news (first turn held back, or a different reason), i.e. the caller should log it. Public for the self-check.</summary>
            public bool NoteShipmentHeldBack(GameAI.GameAIOrder.OrderType transportType, string target, string reason)
            {
                var key = ShipmentKey(transportType, target);
                if (_shipmentHeldBack.TryGetValue(key, out var last) && last == reason) return false;
                _shipmentHeldBack[key] = reason;
                return true;
            }

            // ── Entry point ──────────────────────────────────────────────────

            public void ProcessResults(
                List<Planet.PlanetUpdateResult> results,
                List<GameAI.GameAIOrder>        orders)
            {
                // Self-checks (e.g. PlayerAIResourceSelfCheck) call this with no Gameboard in the scene.
                TryEnterConsolidate(Gameboard.Instance != null ? Gameboard.Instance.TurnNumber : 0);
                RefreshBlockadeView();

                switch (Strategy)
                {
                    case AIStrategy.AIStrategyExpand:
                    case AIStrategy.AIStrategyConsolidate:
                        // Consolidate has no behavior of its own yet; it plays like Expand.
                        ProcessResultsStrategyExpand(results, Player, ref orders);
                        break;
                    case AIStrategy.AIStrategyAmass:
                        break;
                }
            }

            /// <summary>
            /// One-way switch from Expand to Consolidate on first contact with another player.
            /// Public (and free of Gameboard.Instance) so the self-check can drive it directly.
            /// Returns true only on the turn the switch happens.
            /// </summary>
            public bool TryEnterConsolidate(int turnNumber)
            {
                if (Strategy != AIStrategy.AIStrategyExpand) return false;
                if (!AIMap.Knowledge.HasContact(AIMap, Player.playerID)) return false;

                Strategy = AIStrategy.AIStrategyConsolidate;
                AITuningLogger.LogStrategyChange(turnNumber, Player.playerID,
                    AIStrategy.AIStrategyExpand.ToString(), AIStrategy.AIStrategyConsolidate.ToString());
                return true;
            }

            // ── Strategy: Expand ─────────────────────────────────────────────

            private void ProcessResultsStrategyExpand(
                List<Planet.PlanetUpdateResult> results,
                Player                          player,
                ref List<GameAI.GameAIOrder>    orders)
            {
                ProcessColonizers(results, orders);
                UpdateDistributionCenters(results, Gameboard.Instance != null ? Gameboard.Instance.TurnNumber : 0);
                ProcessFoodShortage(results, orders);
                ProcessGrotsitsShortage(results, orders);
                ProcessResearch(results, orders);
                ProcessIndustry(results, orders);
                ProcessShipActions(orders);
            }

            // ── Colonization ─────────────────────────────────────────────────
            private bool PlanetHasColonyShip(string planetName)
            {
                var planet = AIMap.GetPlanet(planetName);
                if (planet != null)
                {
                    return planet.HasDockedShip(Ship.ShipKind.ColonyShip);
                }
                return false;
            }

            private bool PlanetCanColonize(string planetName)
                => IsValidColonizer(planetName) && PlanetHasColonizationTarget(planetName);

            /// <summary>
            /// A known, valid colonization target is within reach of this planet, whether or not the planet is
            /// ready to colonize yet. Drives whether a planet should be building colony ships at all.
            /// </summary>
            private bool PlanetHasColonizationTarget(string planetName)
            {
                var origin = AIMap.GetPlanet(planetName);
                var maxNodes = AIMap.GameAIConstants.maxPathNodesForColonization;
                return origin.DistanceMapToPathingList.Any(t =>
                    t.Value.NumNodes <= maxNodes
                    && IsValidColonizationTarget(AIMap.GetPlanet(t.Key))
                    && CanSupportColony(origin, AIMap.GetPlanet(t.Key))
                    // a route must exist that avoids the planets I can see are blockaded (the planner also rejects the
                    // 1-node no-route stub)
                    && RoutePlanner.PlanRoute(AIMap, planetName, t.Key, _blockadeView, maxNodes) != null);
            }

            /// <summary>
            /// A target that cannot feed a colonist by itself (Planet.NeedsColonyFoodRider) is only viable from an
            /// origin that can pay the colony ship's food rider; any other target needs nothing. Public for the
            /// self-check.
            /// </summary>
            public bool CanSupportColony(Planet origin, Planet target)
                => !target.NeedsColonyFoodRider || origin.Food >= AIMap.GameAIConstants.colonyFoodRider;

            // ── Distribution Centers ────────────────────────────────────────

            public List<string> FoodDistributionCenters { get; private set; } = new List<string>();
            public List<string> GrotsitsDistributionCenters { get; private set; } = new List<string>();

            /// <summary>The player's own surplus-reporting planets as of the last UpdateDistributionCenters
            /// call — one turn stale by the time an order executes, since order execution runs before this
            /// turn's PlanetUpdateResults exist. Used by IsCoverageGap (Task 7).</summary>
            public List<string> LastFoodSurplusPlanets { get; private set; } = new List<string>();
            public List<string> LastGrotsitsSurplusPlanets { get; private set; } = new List<string>();

            /// <summary>Restores a saved DC list; a missing/older field passes an empty list here, i.e.
            /// "no DC yet, select fresh." Public for SaveLoadSystem/GameBoard restore.</summary>
            public void SetDistributionCenters(string resource, List<string> names)
            {
                if (resource == "Food") FoodDistributionCenters = names ?? new List<string>();
                else if (resource == "Grotsits") GrotsitsDistributionCenters = names ?? new List<string>();
            }

            /// <summary>
            /// Sticky, per-resource DC selection and pruning, once per turn. Public and free of
            /// Gameboard.Instance so the self-check can drive it directly.
            /// </summary>
            public void UpdateDistributionCenters(List<Planet.PlanetUpdateResult> results, int turnNumber)
            {
                LastFoodSurplusPlanets = results
                    .Where(x => x.PlayerID == Player.playerID
                             && x.Result == Planet.PlanetUpdateResult.PlanetUpdateResultType.PlanetUpdateResultTypeFoodSurplus)
                    .Select(x => x.Name).ToList();
                LastGrotsitsSurplusPlanets = results
                    .Where(x => x.PlayerID == Player.playerID
                             && x.Result == Planet.PlanetUpdateResult.PlanetUpdateResultType.PlanetUpdateResultTypeGrotsitsSurplus)
                    .Select(x => x.Name).ToList();

                UpdateDistributionCentersForResource(turnNumber, "Food", LastFoodSurplusPlanets, FoodDistributionCenters);
                UpdateDistributionCentersForResource(turnNumber, "Grotsits", LastGrotsitsSurplusPlanets, GrotsitsDistributionCenters);
            }

            private void UpdateDistributionCentersForResource(
                int turnNumber, string resource, List<string> producers, List<string> current)
            {
                for (var i = current.Count - 1; i >= 0; i--)
                {
                    var planet = AIMap.GetPlanet(current[i]);
                    if (planet != null && planet.Owner == Player.playerID && planet.Population.Count > 0) continue;
                    AITuningLogger.LogDCLost(turnNumber, Player.playerID, current[i], resource);
                    current.RemoveAt(i);
                }

                var allowedSlots = AllowedDistributionCenterSlots();
                if (current.Count >= allowedSlots) return;

                var colonized = AIMap.PlanetList.Where(p => p.Owner == Player.playerID && p.Population.Count > 0).ToList();
                var candidates = colonized.Where(p => !current.Contains(p.PlanetName)).ToList();

                // Treat already-selected DCs as producers for scoring purposes (empty on the first slot, so
                // this only affects a second+ slot): SelectDistributionCenter excludes anything a producer
                // already reaches from a candidate's coverage score, so without this a second DC could score
                // just as well sitting redundantly next to the first as it would reaching genuinely new
                // territory -- this makes it correctly score near zero instead, pushing selection outward.
                var scoringProducers = producers.Union(current).ToList();
                var chosen = SelectDistributionCenter(candidates, colonized, scoringProducers, current);
                if (chosen == null) return;

                current.Add(chosen);
                AITuningLogger.LogDCSelected(turnNumber, Player.playerID, chosen, resource);
            }

            /// <summary>
            /// 0 below minPlanetsForDistributionCenters, 1 below minPlanetsForSecondDistributionCenter, else
            /// 2. Counts colonized planets on the WHOLE BOARD (every player), matching the topology probe
            /// this design is based on — not this player's own colonized count.
            /// </summary>
            private int AllowedDistributionCenterSlots()
            {
                var totalColonized = AIMap.TotalColonizedPlanetCount();
                if (totalColonized < AIMap.GameAIConstants.minPlanetsForDistributionCenters) return 0;
                return totalColonized < AIMap.GameAIConstants.minPlanetsForSecondDistributionCenter ? 1 : 2;
            }

            /// <summary>
            /// Coverage-maximizing DC candidate selection. A candidate must be reachable
            /// (NumNodes &lt;= maxPathNodesForResourceDistribution) from at least one producer to be
            /// eligible at all. Scored by how many OTHER colonized, non-producer planets it would newly
            /// reach that no producer already reaches; tied scores are broken by being farthest (in
            /// NumNodes) from the nearest already-selected DC for this resource (so a second+ slot that
            /// ties on coverage prefers spreading out over clustering near an existing DC), then by being
            /// furthest from whichever producer supplies the candidate, then by the cheapest path cost to
            /// that producer. Returns null if no candidate is reachable from any producer.
            /// </summary>
            private string SelectDistributionCenter(
                List<Planet> candidates, List<Planet> allColonized, List<string> producers, List<string> existingDCs)
            {
                string best = null;
                var bestScore = -1;
                var bestDcDistance = -1;
                var bestSupplyDistance = -1;
                var bestSupplyCost = float.MaxValue;
                var maxNodes = AIMap.GameAIConstants.maxPathNodesForResourceDistribution;

                // A path's NumNodes lower bound is 2 (a real 2-planet-minimum route), matching the
                // reachability convention already established by ShipTransportPlanner.IsUsablePath and
                // AssaultPlanner.IsUsablePath: PathingSystem.FindPath does not throw when no route
                // exists, it returns a 1-node, zero-cost stub (ConstructPath's while loop never runs
                // because the destination's ParentName was never set), which must not be read as a free
                // adjacent trip. GameAIMap precomputes DistanceMapToPathingList for every pair
                // unconditionally, including disconnected ones, so this check is required here too.
                bool IsUsablePath(GameAIMap.DestinationToPathingListEntry entry)
                    => entry.NumNodes >= 2 && entry.NumNodes <= maxNodes;

                foreach (var candidate in candidates)
                {
                    var pathMap = candidate.DistanceMapToPathingList;

                    var supplyingProducers = producers
                        .Where(p => pathMap.ContainsKey(p) && IsUsablePath(pathMap[p]))
                        .ToList();
                    if (supplyingProducers.Count == 0) continue;

                    var supplyDistance = supplyingProducers.Max(p => pathMap[p].NumNodes);
                    var supplyCost = supplyingProducers.Min(p => pathMap[p].Cost);

                    // Hops to the nearest already-selected DC for this resource. int.MaxValue (and so,
                    // via the "farther is better" comparison below, treated as the best possible value)
                    // when there are no existing DCs yet, or none are within range -- this tie-break is
                    // inert for the first slot and never penalizes a candidate outside every existing DC's
                    // reach, since that candidate is unambiguously independent of them.
                    var dcDistance = existingDCs
                        .Where(dc => pathMap.ContainsKey(dc) && IsUsablePath(pathMap[dc]))
                        .Select(dc => pathMap[dc].NumNodes)
                        .DefaultIfEmpty(int.MaxValue)
                        .Min();

                    var score = 0;
                    foreach (var other in allColonized)
                    {
                        if (other.PlanetName == candidate.PlanetName) continue;
                        if (producers.Contains(other.PlanetName)) continue; // a producer trivially reaches itself
                        if (!pathMap.ContainsKey(other.PlanetName) || !IsUsablePath(pathMap[other.PlanetName])) continue;

                        var otherPathMap = other.DistanceMapToPathingList;
                        var reachedByProducer = producers.Any(p =>
                            otherPathMap.ContainsKey(p) && IsUsablePath(otherPathMap[p]));
                        if (reachedByProducer) continue;

                        score++;
                    }

                    var better = score > bestScore
                        || (score == bestScore && dcDistance > bestDcDistance)
                        || (score == bestScore && dcDistance == bestDcDistance && supplyDistance > bestSupplyDistance)
                        || (score == bestScore && dcDistance == bestDcDistance && supplyDistance == bestSupplyDistance
                            && supplyCost < bestSupplyCost);
                    if (!better) continue;

                    best = candidate.PlanetName;
                    bestScore = score;
                    bestDcDistance = dcDistance;
                    bestSupplyDistance = supplyDistance;
                    bestSupplyCost = supplyCost;
                }

                return best;
            }

            /// <summary>
            /// True when the given planet is unreachable, for one resource, from EVERY planet in
            /// lastKnownSurplusPlanets AND every planet in distributionCenters — i.e. sticky DC selection
            /// isn't giving this planet any path to get resupplied. Uses the CACHED last-known surplus set
            /// (LastFoodSurplusPlanets/LastGrotsitsSurplusPlanets), not this turn's live results, because
            /// order execution (where this is called from) runs before this turn's PlanetUpdateResults
            /// exist — one turn stale, acceptable for a diagnostic. Public and free of Gameboard.Instance
            /// so the self-check can drive it directly.
            /// </summary>
            public bool IsCoverageGap(Planet planet, List<string> lastKnownSurplusPlanets, List<string> distributionCenters)
            {
                var maxNodes = AIMap.GameAIConstants.maxPathNodesForResourceDistribution;
                var pathMap = planet.DistanceMapToPathingList;
                // NumNodes >= 2: PathingSystem.FindPath returns a 1-node, zero-cost stub for an
                // unreachable destination rather than throwing (see SelectDistributionCenter's
                // IsUsablePath for the full explanation) — a bare "<= maxNodes" would misread that stub
                // as reachable and never report a real gap.
                return !lastKnownSurplusPlanets.Concat(distributionCenters)
                    .Any(s => pathMap.ContainsKey(s) && pathMap[s].NumNodes >= 2 && pathMap[s].NumNodes <= maxNodes);
            }

            private bool IsValidColonizer(string planetName)
            {
                var planet = AIMap.GetPlanet(planetName);
                if (planet.Owner == Player.playerID &&
                    planet.Population.Count >= planet.MaxPopulation
                    * AIMap.GameAIConstants.expandPopulationTrigger)
                    return true;
                
                return false;
            }
            // Public for the FlatSpace/AI self-check (Assets/Editor is a separate assembly).
            public bool IsValidColonizationTarget(Planet planet)
            {
                if (!AIMap.Knowledge.IsKnown(Player.playerID, planet.PlanetName)) return false;
                if (planet.IsPopulationTransferInProgress(Player.playerID))       return false;
                if (planet.Population.Count == 0)                                return true;
                if (planet.Population.Count >= planet.MaxPopulation)             return false;
                return planet.PlayerWithMostPopulation() != Player.playerID;
            }
            // Public for the FlatSpace/AI self-check (Assets/Editor is a separate assembly).
            public void ProcessColonizers(
                List<Planet.PlanetUpdateResult> results,
                List<GameAI.GameAIOrder>        orders)
            {
                var colonizers = results.FindAll(
                    x => x.PlayerID == Player.playerID 
                         && x.Result == Planet.PlanetUpdateResult.PlanetUpdateResultType.PlanetUpdateResultTypeColonizerReady);
                if (colonizers.Count == 0)
                {
                    _colonizeHeldBack.Clear();   // nobody is ready, so nobody is being held back
                    return;
                }

                var targets = AIMap.PlanetList.FindAll(IsValidColonizationTarget);
                if (targets.Count == 0)
                {
                    _colonizeHeldBack.Clear();   // nothing to colonize: not held back by a blockade
                    return;
                }

                // A planet that is no longer a ready colonizer is no longer held back.
                foreach (var heldBackPlanet in _colonizeHeldBack.Keys.ToList())
                    if (!colonizers.Exists(c => c.Name == heldBackPlanet))
                        _colonizeHeldBack.Remove(heldBackPlanet);

                var turn = Gameboard.Instance != null ? Gameboard.Instance.TurnNumber : 0;
                var maxNodes = AIMap.GameAIConstants.maxPathNodesForColonization;
                // The route planned for each (origin, target) choice; the chosen one rides on the colonist order below.
                var plannedRoutes = new Dictionary<(string origin, string target), RoutePlanner.PlannedRoute>();

                var matrix = new ScoreMatrix<ScoreMatrixDecisionElement, ScoreMatrixChoiceElement, ScoreMatrixAction>
                    (new ScoreMatrixDecisionComparer());
                int colonizerIndex = 0;
                foreach (var colonizer in colonizers)
                {
                    // A blockaded planet keeps its colony ship until the blockade lifts.
                    if (IsBlockaded(colonizer.Name))
                    {
                        if (NoteColonizeHeldBack(colonizer.Name, "BlockadedOrigin"))
                            AITuningLogger.LogColonizeCancelled(turn, Player.playerID, colonizer.Name, "BlockadedOrigin");
                        continue;
                    }

                    var colonizerPlanet = AIMap.GetPlanet(colonizer.Name);
                    var pathMap = colonizerPlanet.DistanceMapToPathingList;
                    var entries = new List<ScoreMatrixChoiceElement>();
                    var hadCandidate = false;
                    foreach (var t in targets)
                    {
                        if (!pathMap.ContainsKey(t.PlanetName)
                            || pathMap[t.PlanetName].NumNodes > maxNodes
                            || pathMap[t.PlanetName].NumNodes < 2   // FindPath's 1-node no-route stub is not a candidate
                            || !CanSupportColony(colonizerPlanet, t))
                            continue;
                        hadCandidate = true;

                        // Avoid planets I can see are blockaded; a target with no such route is dropped.
                        var route = RoutePlanner.PlanRoute(AIMap, colonizer.Name, t.PlanetName, _blockadeView, maxNodes);
                        if (route == null) continue;
                        plannedRoutes[(colonizer.Name, t.PlanetName)] = route;
                        entries.Add(new ScoreMatrixChoiceElement
                        {
                            Surplus  = 1.0f,
                            Target   = t.PlanetName,
                            Cost     = route.Cost,
                            Shortage = 1.0f,
                        });
                    }

                    if (entries.Count == 0 && hadCandidate)
                    {
                        if (NoteColonizeHeldBack(colonizer.Name, "NoRoute"))
                            AITuningLogger.LogColonizeCancelled(turn, Player.playerID, colonizer.Name, "NoRoute");
                    }
                    else
                    {
                        _colonizeHeldBack.Remove(colonizer.Name);   // it launches (or has nothing to colonize): no longer held back
                    }

                    if (entries.Count > 0)
                    {
                        
                            matrix.MatrixElements.Add(
                                new ScoreMatrixDecisionElement
                                {
                                    Target = colonizer.Name,
                                    Priority = 0f
                                }, entries);
                        colonizerIndex++;
                    }
                }

                foreach (var action in matrix.GenerateActionList(
                             actionFactory: (origin, element) => new ScoreMatrixAction
                             {
                                 Origin = origin.Target,
                                 Target = element.Target,
                                 Cost = element.Cost
                             }, null))
                {
                    var amount = Convert.ToInt32(
                        colonizers.Find(x => x.Name == action.Origin).Data);
                    var delay  = Convert.ToInt32(
                        action.Cost / AIMap.GameAIConstants.defaultTravelSpeed);
                    var route  = plannedRoutes[(action.Origin, action.Target)];

                    var colonist = MakeOrder(GameAI.GameAIOrder.OrderType.OrderTypePopulationTransport,
                        GameAI.GameAIOrder.OrderTimingType.OrderTimingTypeDelayed,
                        delay, delay, amount, action.Origin, action.Target);
                    colonist.Route = new List<string>(route.Nodes);
                    orders.Add(colonist);
                    if (route.IsDetour)
                        AITuningLogger.LogRouteDetour(turn, Player.playerID, action.Origin, action.Target,
                            route.Nodes, route.Cost);

                    orders.Add(MakeOrder(GameAI.GameAIOrder.OrderType.OrderTypePopulationChange,
                        GameAI.GameAIOrder.OrderTimingType.OrderTimingTypeImmediate,
                        0, 0, amount * -1.0f, action.Origin, action.Origin));

                    orders.Add(MakeOrder(GameAI.GameAIOrder.OrderType.OrderTypePopulationTransferInProgress,
                        GameAI.GameAIOrder.OrderTimingType.OrderTimingTypeImmediate,
                        0, 0, amount, action.Origin, action.Target));
                                    
                    orders.Add(MakeOrder(GameAI.GameAIOrder.OrderType.OrderTypeRemoveShip,
                        GameAI.GameAIOrder.OrderTimingType.OrderTimingTypeImmediate,
                        0, 0, 1, action.Origin, action.Origin));

                    // A target that cannot feed a colonist gets a food rider as its own order: paid by the origin
                    // now (an existing food change), delivered with the colonist (same delay), and unrelated to the
                    // food shipping system. It bridges the gap until a shortage can be answered; it does not
                    // guarantee survival.
                    if (AIMap.GetPlanet(action.Target).NeedsColonyFoodRider)
                    {
                        var rider = AIMap.GameAIConstants.colonyFoodRider;
                        orders.Add(MakeOrder(GameAI.GameAIOrder.OrderType.OrderTypeColonyFoodRider,
                            GameAI.GameAIOrder.OrderTimingType.OrderTimingTypeDelayed,
                            delay, delay, rider, action.Origin, action.Target));
                        orders.Add(MakeOrder(GameAI.GameAIOrder.OrderType.OrderTypeFoodChange,
                            GameAI.GameAIOrder.OrderTimingType.OrderTimingTypeImmediate,
                            0, 0, rider * -1.0f, action.Origin, action.Origin));
                    }
                }
            }

            // ── Food ─────────────────────────────────────────────────────────

            // public for the self-check
            public void ProcessFoodShortage(
                List<Planet.PlanetUpdateResult> results,
                List<GameAI.GameAIOrder>        orders)
            {
                ProcessResourceShipments(results,
                    Planet.PlanetUpdateResult.PlanetUpdateResultType.PlanetUpdateResultTypeFoodShortage,
                    Planet.PlanetUpdateResult.PlanetUpdateResultType.PlanetUpdateResultTypeFoodSurplus,
                    incomingCheck: name => AIMap.GetPlanet(name).FoodShipmentIncoming,
                    FoodDistributionCenters,
                    AIMap.GameAIConstants.distributionCenterFoodTargetStock,
                    currentStockSelector: p => p.Food,
                    GameAI.GameAIOrder.OrderType.OrderTypeFoodTransport,
                    GameAI.GameAIOrder.OrderType.OrderTypeFoodChange,
                    GameAI.GameAIOrder.OrderType.OrderTypeFoodTransportInProgress,
                    orders);
            }

            // ── Grotsits ─────────────────────────────────────────────────────

            // public for the self-check
            public void ProcessGrotsitsShortage(
                List<Planet.PlanetUpdateResult> results,
                List<GameAI.GameAIOrder>        orders)
            {
                ProcessResourceShipments(results,
                    Planet.PlanetUpdateResult.PlanetUpdateResultType.PlanetUpdateResultTypeGrotsitsShortage,
                    Planet.PlanetUpdateResult.PlanetUpdateResultType.PlanetUpdateResultTypeGrotsitsSurplus,
                    incomingCheck: name => AIMap.GetPlanet(name).GrotsitsShipmentIncoming,
                    GrotsitsDistributionCenters,
                    AIMap.GameAIConstants.distributionCenterGrotsitsTargetStock,
                    currentStockSelector: p => p.Grotsits,
                    GameAI.GameAIOrder.OrderType.OrderTypeGrotsitsTransport,
                    GameAI.GameAIOrder.OrderType.OrderTypeGrotsitsChange,
                    GameAI.GameAIOrder.OrderType.OrderTypeGrotsitsTransportInProgress,
                    orders);
            }

            // Below any real shortage's priority: real shortages' Priority is -Data (a positive magnitude,
            // worse shortage sorts first), so an ordinary fixed negative constant is enough to always lose
            // to every real shortage regardless of magnitude.
            private const float DistributionCenterPrioritySentinel = float.MinValue / 2f;

            /// <summary>
            /// The least share of a shipment that must still arrive, through unavoidable blockades, for this target to be
            /// supplied (GameAIConstants.shipmentMinDeliveredFraction, clamped to 0..1). One method so a later change can
            /// ask for less on a high value target (a Distribution Center, a specialist producer, a chokepoint) and more on
            /// an ordinary one; today every target shares the constant. Public for the self-check.
            /// </summary>
            public float MinDeliveredFractionFor(string targetPlanetName)
                => Mathf.Clamp01(AIMap.GameAIConstants.shipmentMinDeliveredFraction);

            /// <summary>
            /// Choice order within a shortage row: clean routes before lossy ones (a nearer source that loses part of the
            /// shipment never beats a clean one), then the ScoreMatrix default, cost minus surplus. Public for the self-check.
            /// </summary>
            public static int CompareResourceChoices(ResourceChoiceElement x, ResourceChoiceElement y)
            {
                var lossy = (x.Loss > 0f).CompareTo(y.Loss > 0f);
                return lossy != 0 ? lossy : (x.Cost - x.Surplus).CompareTo(y.Cost - y.Surplus);
            }

            /// <summary>
            /// Shared shipment logic for any surplus -> shortage resource. A shipment is capped at
            /// min(source's remaining surplus, target's remaining shortfall) rather than always draining
            /// the source's entire surplus, so an overshoot no longer silently turns the target into an
            /// accidental new source next turn. Because a source can therefore have surplus left over
            /// after serving one shortage, the matrix is rebuilt and re-run in rounds against the
            /// remaining balances, letting a single source serve multiple shortages within the same turn
            /// (each round strictly zeroes out at least one side, so this always terminates within
            /// shortages.Count + surpluses.Count rounds).
            ///
            /// A designated Distribution Center below its target stock, with no REAL shortage already
            /// reported for it this turn, gets a synthetic shortage-shaped entry added to the same list —
            /// same rounds-based capping, same shipment orders — at a fixed low-priority sentinel so real
            /// shortages always claim surplus first.
            /// </summary>
            private void ProcessResourceShipments(
                List<Planet.PlanetUpdateResult>                  results,
                Planet.PlanetUpdateResult.PlanetUpdateResultType shortageType,
                Planet.PlanetUpdateResult.PlanetUpdateResultType surplusType,
                Func<string, bool>                               incomingCheck,
                List<string>                                     distributionCenters,
                float                                             distributionCenterTargetStock,
                Func<Planet, float>                               currentStockSelector,
                GameAI.GameAIOrder.OrderType                     transportType,
                GameAI.GameAIOrder.OrderType                     changeType,
                GameAI.GameAIOrder.OrderType                     inProgressType,
                List<GameAI.GameAIOrder>                         orders)
            {
                var surplusResults = results.FindAll(x => x.PlayerID == Player.playerID && x.Result == surplusType);
                if (surplusResults.Count == 0)
                {
                    PruneShipmentHeldBack(transportType, null);   // nothing to ship: nothing is held back by a blockade
                    return;
                }

                var shortages = results.FindAll(x =>
                    x.Result == shortageType && x.PlayerID == Player.playerID && !incomingCheck(x.Name));

                // Tracks ONLY the entries this call synthesizes below — deliberately not the same list as
                // distributionCenters, because a DC can also have a genuine real shortage the same turn
                // (see the guard just below), and that real shortage must keep its normal, gap-derived
                // priority rather than being demoted just because the planet happens to hold a DC role.
                var syntheticShortageNames = new List<string>();
                foreach (var dcName in distributionCenters)
                {
                    if (shortages.Exists(s => s.Name == dcName)) continue; // a real shortage already covers it
                    if (incomingCheck(dcName)) continue; // a shipment (real or synthetic) is already en route
                    var dcPlanet = AIMap.GetPlanet(dcName);
                    if (dcPlanet == null) continue;
                    var gap = distributionCenterTargetStock - currentStockSelector(dcPlanet);
                    if (gap <= 0f) continue;
                    shortages.Add(new Planet.PlanetUpdateResult(dcName, shortageType, -gap, Player.playerID));
                    syntheticShortageNames.Add(dcName);
                }

                if (shortages.Count == 0)
                {
                    PruneShipmentHeldBack(transportType, null);   // no shortage left to be held back
                    return;
                }

                // Real PlanetUpdatePlanet shortages carry a negative Data; the magnitude is what matters here.
                var remainingShortage = shortages.ToDictionary(s => s.Name, s => Mathf.Abs(Convert.ToSingle(s.Data)));
                var remainingSurplus = surplusResults.ToDictionary(s => s.Name,
                    s => Convert.ToSingle(s.Data) * AIMap.GetPlanet(s.Name).GetPopulationFraction(Player.playerID));

                var maxRounds = shortages.Count + surplusResults.Count;
                // Rows where blockades removed every source (in any round): "Blockade" (loss >= amount) or "LowYield"
                // (only the minimum delivered fraction refused them).
                var blockedRows = new Dictionary<string, ShipmentHold>();
                var servedRows = new HashSet<string>();
                for (var round = 0; round < maxRounds; round++)
                {
                    var matrix = BuildResourceMatrix(shortages, surplusResults, remainingShortage, remainingSurplus,
                        syntheticShortageNames, blockedRows);
                    if (matrix == null) break;

                    var actions = matrix.GenerateActionList(
                        actionFactory: (origin, element) => new ResourceAction { ChosenChoiceElement = element },
                        ChoiceCompare: CompareResourceChoices);
                    if (actions.Count == 0) break;

                    foreach (var action in actions)
                    {
                        var amount = Mathf.Min(remainingSurplus[action.Origin], remainingShortage[action.Target]);
                        if (amount <= 0f) continue;

                        EmitResourceOrders(action, amount, transportType, changeType, inProgressType, orders);
                        remainingSurplus[action.Origin]  -= amount;
                        remainingShortage[action.Target] -= amount;
                        servedRows.Add(action.Target);
                    }
                }

                // ShipmentCancelled is logged when a shortage's blocked state changes, not on every turn it holds.
                var turn = Gameboard.Instance != null ? Gameboard.Instance.TurnNumber : 0;
                foreach (var shortage in shortages)
                {
                    if (!servedRows.Contains(shortage.Name) && blockedRows.TryGetValue(shortage.Name, out var hold))
                    {
                        _shipmentHeldBackDetail[ShipmentKey(transportType, shortage.Name)] = hold;
                        if (NoteShipmentHeldBack(transportType, shortage.Name, hold.Reason))
                            AITuningLogger.LogShipmentCancelled(turn, Player.playerID, shortage.Name, hold.Reason,
                                hold.Source, hold.Loss, hold.Amount, hold.BlockedNodes);
                    }
                    else
                    {
                        ForgetShipmentHold(ShipmentKey(transportType, shortage.Name));
                    }
                }
                PruneShipmentHeldBack(transportType, shortages.Select(s => s.Name));
            }

            // Forgets this resource's held-back records for shortages that are no longer reported, so a later blockade
            // episode for the same planet is logged again (activeTargets null = no shortage is active).
            private void PruneShipmentHeldBack(GameAI.GameAIOrder.OrderType transportType, IEnumerable<string> activeTargets)
            {
                var prefix = transportType + ":";
                var active = new HashSet<string>(activeTargets ?? Enumerable.Empty<string>());
                foreach (var key in _shipmentHeldBack.Keys.ToList())
                    if (key.StartsWith(prefix, StringComparison.Ordinal) && !active.Contains(key.Substring(prefix.Length)))
                        ForgetShipmentHold(key);
            }

            /// <summary>
            /// Builds one decision row per shortage still owed resource, offering every reachable
            /// surplus planet that still has some left. A row whose name is in syntheticShortageNames
            /// (this call's own synthetic Distribution Center entries — NOT every DC-owned planet; a DC's
            /// genuine real shortage keeps its normal priority) uses a fixed low-priority sentinel instead
            /// of its gap size, so it never outranks a real shortage. Returns null if nothing remains to
            /// match.
            /// </summary>
            private ScoreMatrix<ScoreMatrixDecisionElement, ResourceChoiceElement, ResourceAction> BuildResourceMatrix(
                List<Planet.PlanetUpdateResult> shortages,
                List<Planet.PlanetUpdateResult> surplusResults,
                Dictionary<string, float>       remainingShortage,
                Dictionary<string, float>       remainingSurplus,
                List<string>                    syntheticShortageNames,
                Dictionary<string, ShipmentHold> blockedRows)
            {
                var matrix = new ScoreMatrix<ScoreMatrixDecisionElement, ResourceChoiceElement, ResourceAction  >
                    (new ScoreMatrixDecisionComparer());

                foreach (var shortage in shortages)
                {
                    if (remainingShortage[shortage.Name] <= 0f) continue;

                    var maxNodes = AIMap.GameAIConstants.maxPathNodesForResourceDistribution;
                    // Every (source, shortage) pair is planned through RoutePlanner.PlanShipmentRoute (range rule included:
                    // the shortest path must be 2..maxNodes nodes, which also rejects FindPath's no-route stub; the route
                    // actually taken may be longer). s.Name != shortage.Name stays: a DC can be BOTH this shortage row
                    // (synthetic demand) AND a real surplus source the same turn, and must never ship to itself.
                    // A blockaded source or target is not dropped: its value is part of the route's Loss, and the pair is
                    // offered only while Loss < the amount it would carry (a shipment that would arrive with 0 is cancelled)
                    // and it delivers at least MinDeliveredFractionFor(shortage) of that amount (LowYield otherwise).
                    var entries = new List<ResourceChoiceElement>();
                    ShipmentHold dropped = null;   // the dropped pair closest to shipping (Blockade outranks LowYield)
                    var minFraction = MinDeliveredFractionFor(shortage.Name);
                    foreach (var s in surplusResults)
                    {
                        if (s.Name == shortage.Name || remainingSurplus[s.Name] <= 0f) continue;
                        var route = RoutePlanner.PlanShipmentRoute(AIMap, s.Name, shortage.Name, _blockadeView, maxNodes);
                        if (route == null) continue;
                        var amount = Mathf.Min(remainingSurplus[s.Name], remainingShortage[shortage.Name]);
                        var dropReason = route.Loss >= amount ? "Blockade"
                            : amount - route.Loss < minFraction * amount ? "LowYield" : null;
                        if (dropReason != null)
                        {
                            if (dropped == null || (dropReason == "Blockade" && dropped.Reason == "LowYield")
                                || (dropReason == dropped.Reason && route.Loss < dropped.Loss))
                                dropped = new ShipmentHold
                                {
                                    Reason = dropReason, Source = s.Name, Loss = route.Loss, Amount = amount,
                                    BlockedNodes = BlockedNodeSummary(route.Nodes),
                                };
                            continue;
                        }
                        entries.Add(new ResourceChoiceElement
                        {
                            SurplusResult = s,
                            ShortageResult = shortage,
                            Cost = route.Cost,
                            Route = route.Nodes,
                            Loss = route.Loss,
                            IsDetour = route.IsDetour,
                        });
                    }
                    if (entries.Count == 0 && dropped != null
                        && (!blockedRows.TryGetValue(shortage.Name, out var already)
                            || (dropped.Reason == "Blockade" && already.Reason == "LowYield")
                            || (dropped.Reason == already.Reason && dropped.Loss < already.Loss)))
                        blockedRows[shortage.Name] = dropped;

                    if (entries.Count > 0)
                    {
                        var decision = new ScoreMatrixDecisionElement
                        {
                            Target = shortage.Name,
                            // shortage.Data is the already-negative deficit (e.g. -20). Negate it so a worse
                            // shortage (more negative Data, larger magnitude) sorts to a HIGHER priority --
                            // ScoreMatrixDecisionComparer processes descending, and rows are processed in
                            // order with each claiming its best choice before the next row runs, so priority
                            // order is who gets first pick of scarce surplus. Un-negated, a mild -5 shortage
                            // outranked a severe -20 one and got served first; this was backwards.
                            Priority = syntheticShortageNames.Contains(shortage.Name)
                                ? DistributionCenterPrioritySentinel
                                : -Convert.ToSingle(shortage.Data),
                        };

                        if (matrix.MatrixElements.ContainsKey(decision))
                        {
                            Debug.LogError("Duplicate Key in Build Resource Matrix");
                        }
                        else
                        {
                            matrix.MatrixElements.Add(decision, entries);
                        }
                    }
                }

                return matrix.MatrixElements.Count == 0 ? null : matrix;
            }

            /// <summary>
            /// Emits the three standard orders (transport, deduct, in-progress) for a resource shipment
            /// action, for a caller-computed amount (already capped at the target's remaining need).
            /// </summary>
            private void EmitResourceOrders(
                ResourceAction               action,
                float                        amount,
                GameAI.GameAIOrder.OrderType transportType,
                GameAI.GameAIOrder.OrderType changeType,
                GameAI.GameAIOrder.OrderType inProgressType,
                List<GameAI.GameAIOrder>     orders)
            {
                var delay = Convert.ToInt32(action.Cost / AIMap.GameAIConstants.defaultTravelSpeed);

                var transport = MakeOrder(transportType,
                    GameAI.GameAIOrder.OrderTimingType.OrderTimingTypeDelayed,
                    delay, delay, amount, action.Origin, action.Target);
                if (action.Route != null) transport.Route = new List<string>(action.Route);
                orders.Add(transport);

                var turn = Gameboard.Instance != null ? Gameboard.Instance.TurnNumber : 0;
                if (action.IsDetour)
                    AITuningLogger.LogRouteDetour(turn, Player.playerID, action.Origin, action.Target, action.Route, action.Cost);
                if (action.Loss > 0f)
                    AITuningLogger.LogShipmentLossy(turn, Player.playerID, action.Origin, action.Target, amount, action.Loss);

                orders.Add(MakeOrder(changeType,
                    GameAI.GameAIOrder.OrderTimingType.OrderTimingTypeImmediate,
                    0, 0, amount * -1.0f, action.Origin, action.Origin));

                orders.Add(MakeOrder(inProgressType,
                    GameAI.GameAIOrder.OrderTimingType.OrderTimingTypeImmediate,
                    0, 0, amount, action.Origin, action.Target));
            }

            // ── Research ─────────────────────────────────────────────────────

            public float       researchTotal   = 0.0f;
            public CatalogItem currentResearch = null;

            // Weight for a choice whose subType is not in the strategy table.
            private const float DefaultChoiceWeight = 1f;
            // Weight multiplier applied to a ColonyShip on a planet that is ready to colonize.
            private const float ColonyShipUrgentBoost = 2f;

            // Roulette-wheel weight per item subType. Higher = more likely to be picked.
            // 1.0f = neutral. Add an entry for AIStrategyAmass when needed.
            private static readonly Dictionary<string, float> ExpandResearchWeights =
                new Dictionary<string, float>
                {
                    { "Food",          3.0f },  // food upgrades biggest boost
                    { "Industry",      2.0f },  // useful but secondary
                    { "Grotsits",      1.0f },  // least useful while expanding
                    { "Research",      1.0f },  // least useful while expanding
                    { "ColonyShip",    2.5f },  // ships useful but secondary
                    { "Warship",       1.5f },  // Updated priority
                };
            private static readonly Dictionary<string, float> ConsolidateResearchWeights =
                new Dictionary<string, float>
                {
                    { "Food",          1.0f },  // food upgrades biggest boost
                    { "Industry",      3.0f },  // useful but secondary
                    { "Grotsits",      2.0f },  // least useful while expanding
                    { "Research",      1.0f },  // least useful while expanding
                    { "ColonyShip",    0.5f },  // ships useful but secondary
                    { "Warship",       2.5f },  // Updated priority
                };
            private static readonly Dictionary<AIStrategy, Dictionary<string, float>> ResearchWeightTable =
                new Dictionary<AIStrategy, Dictionary<string, float>>
                {
                    { AIStrategy.AIStrategyExpand,      ExpandResearchWeights },
                    { AIStrategy.AIStrategyConsolidate, ConsolidateResearchWeights },
                    // AIStrategyAmass — add when needed
                };

            private void ProcessResearch(
                List<Planet.PlanetUpdateResult> results,
                List<GameAI.GameAIOrder>        orders)
            {
                var researchResults = results
                    .Where(p => p.PlayerID == Player.playerID 
                                && p.Result == Planet.PlanetUpdateResult.PlanetUpdateResultType
                        .PlanetUpdateResultTypeResearchProduced)
                    .ToList();

                if (researchResults.Count == 0) return;

                var researchThisTurn = researchResults.Sum(p => (float)p.Data);
                if (researchThisTurn > 0.0f) UpdateResearch(researchThisTurn, orders);

                foreach (var r in researchResults)
                    orders.Add(MakeOrder(
                        GameAI.GameAIOrder.OrderType.OrderTypeResearchChange,
                        GameAI.GameAIOrder.OrderTimingType.OrderTimingTypeImmediate,
                        0, 0, (float)r.Data * -1.0f, r.Name, r.Name));
            }

            private void UpdateResearch(float researchThisTurn, List<GameAI.GameAIOrder> orders)
            {
                researchTotal += researchThisTurn;
                if (currentResearch == null)
                {
                    ChooseNewResearch(orders);
                }
                else if (researchTotal >= currentResearch.cost)
                {
                    researchTotal -= currentResearch?.cost ?? 0.0f;
                    CompleteResearch(orders);
                    ChooseNewResearch(orders);
                }
            }

            private void CompleteResearch(List<GameAI.GameAIOrder> orders)
            {
                if (currentResearch == null)
                    return;
                var completedResearchName = currentResearch.name;
                currentResearch.researched = true;
                foreach( var dependentItem in ProductionCatalog.catalogItems.FindAll(x => x.requiredTech == currentResearch.itemName))
                {
                    dependentItem.researched  = true;
                }
                Gameboard.Instance.CreateNotificationsForCompletedResearch(completedResearchName, Player.playerID);
                AITuningLogger.LogResearchComplete(Gameboard.Instance.TurnNumber, Player.playerID, completedResearchName);

            }
            private void ChooseNewResearch(List<GameAI.GameAIOrder> orders)
            {
                var researchChoices = ResearchCatalog.catalogItems.FindAll(x => !x.researched).ToList();
                researchChoices = researchChoices.FindAll(x =>
                    (string.IsNullOrEmpty(x.requiredTech) || ResearchCatalog.GetItem(x.requiredTech).researched)).ToList();   
  
                var matrix = BuildChoiceMatrix(researchChoices, Strategy);
                if (matrix == null) return;

                var actions = matrix.GenerateActionList(
                    actionFactory: (origin, element) => new ResearchAction { ChosenItem = element.Item },
                    weightSelector:      element => element.Weight);

                if (actions.Count == 0) return;

                currentResearch = actions[0].ChosenItem;
                var offenseBoost = GetResearchSituationalMultiplier(currentResearch);
                if (offenseBoost > 1f)
                    AITuningLogger.LogOffenseResearchBoost(Gameboard.Instance.TurnNumber, Player.playerID,
                        currentResearch.itemName, offenseBoost);
                if (Player.playerID == 0)
                {
                    Debug.Log("Turn: " + Gameboard.Instance.TurnNumber + " New Research: " + currentResearch.name);
                }

                orders.Add(MakeOrder(
                    GameAI.GameAIOrder.OrderType.OrderTypeResearchSet,
                    GameAI.GameAIOrder.OrderTimingType.OrderTimingTypeImmediate,
                    0, 0, 0f, currentResearch.itemName, currentResearch.itemName));
                Gameboard.Instance.CreateNotificationsForNewResearch(currentResearch?.itemName, Player.playerID);
                AITuningLogger.LogResearchStart(Gameboard.Instance.TurnNumber, Player.playerID, currentResearch?.itemName);
            }

            /// <summary>
            /// Builds a one-row choice matrix: a single "AI" origin mapped to all
            /// available research choices, weighted by strategy preference.
            /// </summary>
            private ScoreMatrix<ScoreMatrixDecisionElement, ResearchChoiceElement, ResearchAction> BuildChoiceMatrix(
                List<CatalogItem> choices,
                AIStrategy        strategy)
            {
                if (choices.Count == 0) return null;

                var matrix  = new ScoreMatrix<ScoreMatrixDecisionElement, ResearchChoiceElement, ResearchAction>
                    (new ScoreMatrixDecisionComparer());
                var entries = ApplyResearchSituationalWeights(NormalizeResearchWeightsBySubtype(
                    choices.Select(item => new ResearchChoiceElement
                    {
                        Item   = item,
                        Weight = GetResearchWeight(item, strategy),
                    }).ToList()));

                matrix.MatrixElements.Add(new ScoreMatrixDecisionElement
                {
                    Target = "Research",
                    Priority = 0f
                }, entries);
                return matrix;
            }

            /// <summary>
            /// Divides each research choice's weight by the number of choices sharing its subtype, so a subtype's
            /// total weight stays its table weight however many of its items are eligible at once (Warship has three
            /// parallel research lines: Weapons, Armor, Shields). A subtype with one choice is unchanged.
            /// </summary>
            public static List<ResearchChoiceElement> NormalizeResearchWeightsBySubtype(List<ResearchChoiceElement> choices)
            {
                var counts = choices
                    .GroupBy(c => c.Item.subType ?? string.Empty)
                    .ToDictionary(g => g.Key, g => g.Count());
                return choices.Select(c =>
                {
                    var normalized = c;
                    normalized.Weight = c.Weight / counts[c.Item.subType ?? string.Empty];
                    return normalized;
                }).ToList();
            }

            /// <summary>
            /// Roulette-wheel weight for a research item given the current strategy.
            /// Higher = more likely to be picked. Falls back to a neutral weight if the
            /// strategy or subType has no table entry.
            /// </summary>
            public static float GetResearchWeight(CatalogItem item, AIStrategy strategy)
            {
                if (ResearchWeightTable.TryGetValue(strategy, out var typeWeights) &&
                    typeWeights.TryGetValue(item.subType, out var weight))
                {
                    return weight;
                }

                return DefaultChoiceWeight;
            }

            /// <summary>
            /// The situational factor on a research item's weight: blockadedOffenseResearchBoost for a Warship Offense item
            /// while any blockade against me is visible (blockade value is offense against offense), otherwise 1. Kept out
            /// of the static strategy table, as production's situational weights are.
            /// </summary>
            public float GetResearchSituationalMultiplier(CatalogItem item)
            {
                if (item == null || !WarshipStats.IsWarshipImprovement(item) || item.effect != WarshipStats.OffenseKey)
                    return 1f;
                if (_blockadeView == null || !_blockadeView.BlockadedNames.Any()) return 1f;
                return AIMap.GameAIConstants.blockadedOffenseResearchBoost;
            }

            /// <summary>
            /// Applies the situational multiplier to already-normalized research weights (so Armor and Shields do not dilute
            /// the boost). Returns a new list; the input is not mutated.
            /// </summary>
            public List<ResearchChoiceElement> ApplyResearchSituationalWeights(List<ResearchChoiceElement> choices)
                => choices.Select(c =>
                {
                    var adjusted = c;
                    adjusted.Weight = c.Weight * GetResearchSituationalMultiplier(c.Item);
                    return adjusted;
                }).ToList();

            // ── Industry ─────────────────────────────────────────────────────
            // Roulette-wheel weight per item subType. Higher = more likely to be picked.
            // 1.0f = neutral. Add an entry for AIStrategyAmass when needed.
            private static readonly Dictionary<string, float> ExpandIndustryWeights =
                new Dictionary<string, float>
                {
                    { "Food",          2.5f },  // food needed for pop growth
                    { "Industry",      1.5f },  // slightly useful while expanding
                    { "Grotsits",      1.0f },  // build the base
                    { "Research",      1.0f },  // build the base
                    { "ColonyShip",    2.5f },  // colony ships needed
                    { "Warship",       1.5f },  // Updated priority
                    { "WarshipUpdate", 1.5f },  // same as Warship
                };

            private static readonly Dictionary<string, float> ConsolidateIndustryWeights =
                new Dictionary<string, float>
                {
                    { "Food",          1.0f },  // not as growth focused
                    { "Industry",      2.0f },  // building focused
                    { "Grotsits",      1.5f },  // support
                    { "Research",      1.0f },  // research can slack off a bit
                    { "ColonyShip",    1.0f },  // colony ships needed as much
                    { "Warship",       2.5f },  // highest priority
                    { "WarshipUpdate", 2.5f },  // same as Warship
                };
            private static readonly Dictionary<AIStrategy, Dictionary<string, float>> IndustryWeightTable =
                new Dictionary<AIStrategy, Dictionary<string, float>>
                {
                    { AIStrategy.AIStrategyExpand,      ExpandIndustryWeights },
                    { AIStrategy.AIStrategyConsolidate, ConsolidateIndustryWeights },
                    // AIStrategyAmass — add when needed
                };

            public static float GetIndustryStrategyWeight(CatalogItem item, AIStrategy strategy)
            {
                if (IndustryWeightTable.TryGetValue(strategy, out var typeWeights) &&
                    typeWeights.TryGetValue(item.subType, out var strategyWeight))
                {
                    return strategyWeight;
                }

                return DefaultChoiceWeight;
            }

            private float GetIndustryWeight(CatalogItem item, AIStrategy strategy, string planetName)
            {
                return GetIndustryStrategyWeight(item, strategy) * GetIndustrySituationalWeightMultiplier(item, planetName);
            }

            /// <summary>
            /// Below the wanted fleet: 1 + boost x (shortfall / wanted). From the wanted fleet up to
            /// wanted x cap: tapers linearly from 1 to 0. At or beyond wanted x cap: 0. 1 when nothing
            /// is wanted (no basis to shut warships off). A cap at or below 1 cuts off as soon as the
            /// wanted fleet is met (capFleet then never exceeds wanted, so there is no divide by zero).
            /// </summary>
            public static float WarshipShortfallMultiplier(int wanted, int have, float boost, float cap)
            {
                if (wanted <= 0) return 1f;
                if (have < wanted) return 1f + boost * (wanted - have) / wanted;

                var capFleet = wanted * cap;
                if (have >= capFleet) return 0f;
                return 1f - (have - wanted) / (capFleet - wanted);
            }

            /// <summary>
            /// The fleet Consolidate wants: round-1 garrisons for every outer planet (from the Consolidate
            /// transport planner), plus the assault's required force whenever a known enemy planet exists
            /// (not ChooseTarget, which is null while I hold no warships, exactly when I most need to build).
            /// </summary>
            public int WantedWarships()
            {
                var transport = new ShipTransportPlanner(AIMap, Player.playerID, AIStrategy.AIStrategyConsolidate);
                var garrisons = transport.BuildStates().Sum(s => s.RoundGarrison);
                var assault = new AssaultPlanner(AIMap, Player.playerID);
                var unbounded = garrisons + (assault.HasKnownEnemyPlanet() ? assault.RequiredForce() : 0);

                // Bounded by my economy: the assault force follows the enemies' fleets, which follow mine.
                var ceiling = (int)(AIMap.GameAIConstants.warshipsPerColonizedPlanet * ColonizedPlanetCount());
                return Math.Min(unbounded, ceiling);
            }

            /// <summary>
            /// Drops every production choice whose weight is not a positive finite number (the values
            /// ScoreMatrix.WeightedPick would treat as 0), keeping the rest in order. Applies to every
            /// strategy: 0 always meant "exclude" (see the ColonyShip situational multiplier).
            /// </summary>
            public static List<IndustryChoiceElement> OfferedChoices(List<IndustryChoiceElement> choices)
                => choices.FindAll(c => c.Weight > 0f && !float.IsNaN(c.Weight) && !float.IsInfinity(c.Weight));

            /// <summary>
            /// Keeps only the highest tier per subtype among the remaining choices. Once several tiers of the
            /// same resource are unlocked and affordable, building anything but the best one is wasted industry
            /// (yield and upkeep only ever count a planet's best tier per resource, regardless of build order),
            /// and a backlog of same-resource tiers would otherwise inflate that resource's share of the roulette
            /// wheel relative to Warship/ColonyShip, which only ever have one catalog entry each.
            /// </summary>
            public static List<IndustryChoiceElement> BestTierPerSubtype(List<IndustryChoiceElement> choices)
                => choices
                    .GroupBy(c => c.Item.subType)
                    .Select(g => g.OrderByDescending(c => c.Item.tier).First())
                    .ToList();

            /// <summary>Planets I own with population (the same test as ShipTransportPlanner.IsColonized).</summary>
            public int ColonizedPlanetCount()
                => AIMap.PlanetList.Count(p => p.Owner == Player.playerID && p.Population.Count > 0);

            /// <summary>Every warship I own: docked anywhere plus my own in-flight ships. Ships still in production are not counted.</summary>
            public int OwnedWarships()
                => AIMap.PlanetList.Sum(p =>
                    p.DockedShips.Count(s => s.Kind == Ship.ShipKind.WarShip && s.Owner == Player.playerID)
                    + p.GetIncomingShips(Ship.ShipKind.WarShip, Player.playerID));

            /// <summary>The Warship production multiplier for a strategy; 1 for anything but Consolidate.</summary>
            public float ComputeWarshipMultiplier(AIStrategy strategy)
                => ComputeWarshipMultiplier(strategy, out _, out _);

            private float ComputeWarshipMultiplier(AIStrategy strategy, out int wanted, out int have)
            {
                wanted = 0;
                have = 0;
                if (strategy != AIStrategy.AIStrategyConsolidate) return 1f;
                wanted = WantedWarships();
                have = OwnedWarships();
                return WarshipShortfallMultiplier(wanted, have,
                    AIMap.GameAIConstants.warshipShortfallBoost, AIMap.GameAIConstants.warshipFleetCap);
            }

            // The Warship multiplier reads every planet and is logged, so it is computed at most once per
            // production turn: the first Warship choice evaluated fills it, BuildIndustryMatrix resets it.
            private float? _warshipMultiplierThisTurn;

            // Public for the FlatSpace/AI self-check (Assets/Editor is a separate assembly).
            public float GetIndustrySituationalWeightMultiplier(CatalogItem item, string planetName)
            {
                if (item.subType == "ColonyShip")
                {
                    if (IsBlockaded(planetName))
                        return 0f;                       // a blockaded planet builds no colony ships (it launches none either)
                    if (PlanetHasColonyShip(planetName))
                        return 0f;                       // already have one — exclude
                    if (!PlanetHasColonizationTarget(planetName))
                        return 0f;                       // nothing left to colonize — exclude (no useless colony ships)
                    if (IsValidColonizer(planetName))
                        return ColonyShipUrgentBoost;    // ready to colonize — strongly favour
                    if (Strategy == AIStrategy.AIStrategyConsolidate)
                        return ColonyShipUrgentBoost;    // targets remain: keep expanding even before the planet is ready
                }
                if (item.type == "Improvement" && !AIMap.GetPlanet(planetName).CanAffordImprovement(item))
                    return 0f;                           // its upkeep would sink the planet's grotsits — do not offer it
                var blockaded = IsBlockaded(planetName);
                var blockadeBoost = blockaded ? AIMap.GameAIConstants.blockadedWarshipBoost : 1f;
                if (item.subType == "WarshipUpdate")
                {
                    if (AIMap.GetPlanet(planetName).FindWarshipUpdateTarget(
                            Player.playerID, WarshipStats.ResearchedNames(ResearchCatalog.catalogItems)) == null)
                        return 0f;                       // nothing docked here is missing an improvement — do not offer it
                    return blockadeBoost;
                }
                if (item.subType == "Warship")
                {
                    var shortfall = 1f;                  // Expand has no fleet cap
                    if (Strategy == AIStrategy.AIStrategyConsolidate)
                    {
                        if (_warshipMultiplierThisTurn == null)
                        {
                            _warshipMultiplierThisTurn = ComputeWarshipMultiplier(Strategy, out var wanted, out var have);
                            AITuningLogger.LogWarshipBoost(
                                Gameboard.Instance != null ? Gameboard.Instance.TurnNumber : 0,
                                Player.playerID, wanted, have, _warshipMultiplierThisTurn.Value);
                        }
                        shortfall = _warshipMultiplierThisTurn.Value;   // shortfall boost / surplus taper, 0 at the cap
                    }
                    // A blockaded planet is exempt from the fleet-cap cutoff: floor at 1, then boost.
                    return (blockaded ? Math.Max(shortfall, 1f) : shortfall) * blockadeBoost;
                }
                return 1f;
            }

            private void ProcessIndustry(
                List<Planet.PlanetUpdateResult> results,
                List<GameAI.GameAIOrder>        orders)
            {
                UpdatePlanetaryProduction(results, orders);
                
                // TODO: PlanetUpdateResultTypeIndustrySurplus — ship industry
            }

            /// <summary>
            /// Chooses new production item if production is complete, 
            /// or if the production queue is empty
            /// </summary>
            private void UpdatePlanetaryProduction(
                List<Planet.PlanetUpdateResult> results,
                List<GameAI.GameAIOrder>        orders)
            {
                var matrix = BuildIndustryMatrix(results, Strategy);
                if (matrix == null) return;

                foreach (var action in matrix.GenerateActionList(
                             actionFactory: (origin, element) =>
                                 new IndustryAction {ChosenItem = element.Item, PlanetName = origin.Target},
                             weightSelector:      element => element.Weight)
                        )
                    EmitProductionOrders(action, orders);
            }
            /// <summary>
            /// Emits the three standard orders (transport, deduct, in-progress)
            /// for a resource shipment action.
            /// </summary>
            private void EmitProductionOrders(
                IndustryAction              action,
                List<GameAI.GameAIOrder>        orders)
            {
                var originPlanet = AIMap.GetPlanet(action.Origin);
                var productionName = action.Target;

                // Mark a warship started under the blockade fleet-cap exemption, so a log can tell it from a cap bug.
                var producedItem = ProductionCatalog.catalogItems.Find(x => x.name == productionName);
                if (producedItem != null && (producedItem.subType == "Warship" || producedItem.subType == "WarshipUpdate")
                    && IsBlockaded(action.Origin))
                    AITuningLogger.LogBlockadedProduction(Gameboard.Instance != null ? Gameboard.Instance.TurnNumber : 0,
                        Player.playerID, action.Origin, productionName, _blockadeView.Value(action.Origin));

                orders.Add(MakeOrder(GameAI.GameAIOrder.OrderType.OrderTypeIndustrySetProduction,
                    GameAI.GameAIOrder.OrderTimingType.OrderTimingTypeImmediate,
                    0, 0, action.Target, action.Origin, action.Origin));
            }

              /// <summary>
            /// Matrix-building logic for any production decisions.
            /// Returns null if there is nothing to do.
            /// </summary>
            private ScoreMatrix<ScoreMatrixMultipleDecisionElement, IndustryChoiceElement, IndustryAction> BuildIndustryMatrix(
                List<Planet.PlanetUpdateResult>                  results,
                AIStrategy        strategy)
            {

                var productionCompleteResults = results.FindAll(x => x.PlayerID == Player.playerID
                    && (x.Result is Planet.PlanetUpdateResult.PlanetUpdateResultType.PlanetUpdateResultTypeIndustryProductionComplete 
                        or Planet.PlanetUpdateResult.PlanetUpdateResultType.PlanetUpdateResultTypeIndustryProductionQueueEmpty))
                    .OrderBy(x => x.Name).ThenBy(x => x.GetType()).ToList();
                var surplusResults = productionCompleteResults.FindAll(x => x.PlayerID == Player.playerID
                    && x.Result is Planet.PlanetUpdateResult.PlanetUpdateResultType.PlanetUpdateResultTypeIndustrySurplus);
                
                if (productionCompleteResults.Count == 0)
                    return null;

                var matrix = new ScoreMatrix<ScoreMatrixMultipleDecisionElement, IndustryChoiceElement, IndustryAction  >
                    (new ScoreMatrixMultipleDecisionComparer());

                _warshipMultiplierThisTurn = null;   // recomputed lazily by GetIndustrySituationalWeightMultiplier

                var decisionIndex = 0;
                foreach (var planetName in productionCompleteResults.Select(x => x.Name).Distinct())
                {
                    var planetResults = productionCompleteResults.FindAll(x => x.Name == planetName);
                    var planet = AIMap.GetPlanet(planetName);
                    var potentialProduction = ProductionCatalog.catalogItems.FindAll(x => x.researched == true
                        && !(planet.CompletedImprovements.Select(y => y.Item1).ToList().Contains(x.name))
                        && !planet.IsImprovementSuperseded(x));   // a lower tier than the best already built adds nothing

                    var planetSurplus = surplusResults.FindIndex(x => x.Name == planetName) == -1
                        ? 0f
                        : Convert.ToSingle(surplusResults.Find(x => x.Name == planetName).Data); 
                    var entries = potentialProduction.Select(item => new IndustryChoiceElement
                    {
                        Item     = item,
                        Weight = GetIndustryWeight(item, strategy, planetName),
                        Surplus = planetSurplus,
                        PlanetName = planetName,
                    }).ToList();

                    // A weight of 0 means "do not offer" (already holds a colony ship; Warship at its fleet
                    // cap), but ScoreMatrix treats weights as relative and picks uniformly when every
                    // weight is 0, so such choices must be dropped, not just weighted down.
                    entries = OfferedChoices(entries);
                    entries = BestTierPerSubtype(entries);
                    if(entries.Count == 0) continue;

                    // A planet has one production slot, so it gets exactly one new
                    // item per turn — even when it emitted several production signals
                    // this turn (e.g. ProductionComplete + ProductionQueueEmpty both
                    // fire when the last queued item finishes).
                    matrix.MatrixElements.Add(
                        new ScoreMatrixMultipleDecisionElement
                        {
                            Target =  planetName,
                            Priority = decisionIndex++,
                            NumChoices = planetResults.Count,
                        },
                        entries);

                }
                return matrix;
            }

              // ── Order factory ────────────────────────────────────────────────

            private GameAI.GameAIOrder MakeOrder(
                GameAI.GameAIOrder.OrderType       type,
                GameAI.GameAIOrder.OrderTimingType timing,
                int                                timingDelay,
                int                                totalDelay,
                object                              data,
                string                             origin,
                string                             target)
            => new GameAI.GameAIOrder
            {
                Type        = type,
                TimingType  = timing,
                TimingDelay = timingDelay,
                TotalDelay  = totalDelay,
                Data        = data,
                Origin      = origin,
                Target      = target,
                PlayerId    = Player.playerID,
            };

            // ── Ship transport ───────────────────────────────────────────────

            /// <summary>
            /// Moves warships. The decisions live in ShipTransportPlanner (home garrisons) and, under
            /// Consolidate, AssaultPlanner; this turns each resulting ShipAction into orders.
            /// </summary>
            public void ProcessShipActions(List<GameAI.GameAIOrder> orders)
            {
                // Self-checks call this with no Gameboard in the scene.
                var turn = Gameboard.Instance != null ? Gameboard.Instance.TurnNumber : 0;

                // Nothing is undocked until the orders execute, so a second fleet leaving the same
                // origin this turn (home defence + assault) must skip the ships the first one takes.
                var claimedByOrigin = new Dictionary<string, int>();
                foreach (var action in PlanShipActions(turn))
                {
                    claimedByOrigin.TryGetValue(action.Origin, out var claimed);
                    EmitShipOrders(action, orders, claimed);
                    claimedByOrigin[action.Origin] = claimed + action.Count;
                }
            }

            private string _lastLoggedAssaultTarget;

            // Log-only: which blockade-breaking target the assault has, so start and end are logged on change only.
            private readonly BlockadeTargetTracker _blockadeTargets = new BlockadeTargetTracker();

            /// <summary>
            /// Expand: the home garrison plan, unchanged. Consolidate: choose the assault target (a planet blockaded against
            /// me first, else the enemy-occupied rule), plan home defence with those ships held out, then send whatever is
            /// still spare to the target. Public (and free of Gameboard.Instance) so the self-check can drive it directly.
            /// </summary>
            public List<ShipAction> PlanShipActions(int turnNumber)
            {
                if (Strategy != AIStrategy.AIStrategyConsolidate)
                    return new ShipTransportPlanner(AIMap, Player.playerID).Plan();

                var stats = new WarshipStats(ResearchCatalog != null ? ResearchCatalog.catalogItems : null);
                var assault = new AssaultPlanner(AIMap, Player.playerID, _blockadeView, stats, _blockadeMemory, turnNumber);
                var blockadeTarget = assault.ChooseBlockadeTarget(out var blockadeReason);
                var target = blockadeTarget ?? assault.ChooseEnemyTarget();

                var targetName = target?.PlanetName;
                LogBlockadeTargetChanges(turnNumber, assault, blockadeTarget, blockadeReason);
                if (blockadeTarget == null && targetName != null && targetName != _lastLoggedAssaultTarget)
                    AITuningLogger.LogAssaultTarget(turnNumber, Player.playerID, targetName, assault.RequiredForce());
                _lastLoggedAssaultTarget = blockadeTarget == null ? targetName : null;

                var transport = new ShipTransportPlanner(AIMap, Player.playerID, Strategy)
                {
                    HeldPlanet = targetName,
                };
                var actions = transport.Plan();
                actions.AddRange(assault.Plan(target, transport.LastStates, actions));

                var force = assault.LastBlockadeForce;
                if (blockadeTarget != null && force.Ships > 0)
                    AITuningLogger.LogBlockadeForce(turnNumber, Player.playerID, blockadeTarget.PlanetName,
                        force.Ships, force.Offense, force.StillNeeded);
                return actions;
            }

            private void LogBlockadeTargetChanges(int turnNumber, AssaultPlanner assault, Planet blockadeTarget, string reason)
            {
                BlockadeTargetTracker.Target? current = null;
                if (blockadeTarget != null)
                {
                    var name = blockadeTarget.PlanetName;
                    current = new BlockadeTargetTracker.Target
                    {
                        Planet  = name,
                        Blocker = _blockadeView.Blocker(name),
                        Value   = _blockadeView.Value(name),
                        Needed  = assault.NeededOffense(blockadeTarget),
                        Reason  = reason,
                    };
                }
                foreach (var change in _blockadeTargets.Update(turnNumber, current,
                             name => _blockadeView != null && _blockadeView.IsBlockaded(name)))
                {
                    if (change.Started)
                        AITuningLogger.LogBlockadeTarget(turnNumber, Player.playerID, change.Planet, change.Target.Blocker,
                            change.Target.Value, change.Target.Needed, change.Target.Reason);
                    else
                        AITuningLogger.LogBlockadeTargetEnd(turnNumber, Player.playerID, change.Planet, change.EndReason,
                            change.TurnsHeld);
                }
            }

            /// <summary>
            /// Emits the standard order trio for a fleet: delayed arrival (carrying each ship's
            /// snapshot), immediate departure from the origin, immediate "incoming" flag at the target.
            /// </summary>
            public void EmitShipOrders(ShipAction action, List<GameAI.GameAIOrder> orders, int skipShips = 0)
            {
                var origin = AIMap.GetPlanet(action.Origin);
                var snapshots = origin.PeekShipSnapshots(action.Kind, Player.playerID, action.Count, skipShips);
                if (snapshots.Count < action.Count)
                {
                    Debug.LogWarning($"Ship transport {action.Origin}->{action.Target} skipped: " +
                                     $"wanted {action.Count} ships, only {snapshots.Count} docked.");
                    return;
                }

                var fleet = new GameAI.GameAIOrder.ShipFleetPayload { Kind = action.Kind, Snapshots = snapshots };
                // At least 1: a Delayed order with TimingDelay <= 0 is both queued AND executed
                // immediately by ProcessNewOrders, which would dock the fleet twice.
                var delay = Math.Max(1, Convert.ToInt32(action.Cost / AIMap.GameAIConstants.defaultTravelSpeed));

                var transport = MakeOrder(GameAI.GameAIOrder.OrderType.OrderTypeShipTransport,
                    GameAI.GameAIOrder.OrderTimingType.OrderTimingTypeDelayed,
                    delay, delay, action.Count, action.Origin, action.Target);
                transport.Fleet = fleet;
                orders.Add(transport);

                var departure = MakeOrder(GameAI.GameAIOrder.OrderType.OrderTypeShipDeparture,
                    GameAI.GameAIOrder.OrderTimingType.OrderTimingTypeImmediate,
                    0, 0, action.Count, action.Origin, action.Origin);
                departure.Fleet = fleet;
                orders.Add(departure);

                var inProgress = MakeOrder(GameAI.GameAIOrder.OrderType.OrderTypeShipTransferInProgress,
                    GameAI.GameAIOrder.OrderTimingType.OrderTimingTypeImmediate,
                    0, 0, action.Count, action.Origin, action.Target);
                inProgress.Fleet = fleet;
                orders.Add(inProgress);
            }

            // ── MonoBehaviour ────────────────────────────────────────────────

            void Awake()
            {
                ProductionCatalog = this.AddComponent<Catalog>();
                ProductionCatalog.CatalogName = "Production Catalog";
                ResearchCatalog = this.AddComponent<Catalog>();
                ResearchCatalog.CatalogName = "Research Catalog";
            }

            void Update() { }
        }
    }
}
