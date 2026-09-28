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

            // ── Entry point ──────────────────────────────────────────────────

            public void ProcessResults(
                List<Planet.PlanetUpdateResult> results,
                List<GameAI.GameAIOrder>        orders)
            {
                // Self-checks (e.g. PlayerAIResourceSelfCheck) call this with no Gameboard in the scene.
                TryEnterConsolidate(Gameboard.Instance != null ? Gameboard.Instance.TurnNumber : 0);

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
                return origin.DistanceMapToPathingList.Any(t =>
                    t.Value.NumNodes <= AIMap.GameAIConstants.maxPathNodesForColonization
                    && IsValidColonizationTarget(AIMap.GetPlanet(t.Key))
                    && CanSupportColony(origin, AIMap.GetPlanet(t.Key)));
            }

            /// <summary>
            /// A target that cannot feed a colonist by itself (Planet.NeedsColonyFoodRider) is only viable from an
            /// origin that can pay the colony ship's food rider; any other target needs nothing. Public for the
            /// self-check.
            /// </summary>
            public bool CanSupportColony(Planet origin, Planet target)
                => !target.NeedsColonyFoodRider || origin.Food >= AIMap.GameAIConstants.colonyFoodRider;

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
                if (colonizers.Count == 0) return;

                var targets = AIMap.PlanetList.FindAll(IsValidColonizationTarget);
                if (targets.Count == 0) return;

                var matrix = new ScoreMatrix<ScoreMatrixDecisionElement, ScoreMatrixChoiceElement, ScoreMatrixAction>
                    (new ScoreMatrixDecisionComparer());
                int colonizerIndex = 0;
                foreach (var colonizer in colonizers)
                {
                    var colonizerPlanet = AIMap.GetPlanet(colonizer.Name);
                    var pathMap = colonizerPlanet.DistanceMapToPathingList;
                    var entries = targets
                        .Where(t => pathMap.ContainsKey(t.PlanetName)
                                 && pathMap[t.PlanetName].NumNodes
                                        <= AIMap.GameAIConstants.maxPathNodesForColonization
                                 && CanSupportColony(colonizerPlanet, t))
                        .Select(t => new ScoreMatrixChoiceElement
                        {
                            Surplus  = 1.0f,
                            Target   = t.PlanetName,
                            Cost     = pathMap[t.PlanetName].Cost,
                            Shortage = 1.0f,
                        })
                        .ToList();

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

                    orders.Add(MakeOrder(GameAI.GameAIOrder.OrderType.OrderTypePopulationTransport,
                        GameAI.GameAIOrder.OrderTimingType.OrderTimingTypeDelayed,
                        delay, delay, amount, action.Origin, action.Target));

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

            private void ProcessFoodShortage(
                List<Planet.PlanetUpdateResult> results,
                List<GameAI.GameAIOrder>        orders)
            {
                ProcessResourceShipments(results,
                    Planet.PlanetUpdateResult.PlanetUpdateResultType.PlanetUpdateResultTypeFoodShortage,
                    Planet.PlanetUpdateResult.PlanetUpdateResultType.PlanetUpdateResultTypeFoodSurplus,
                    incomingCheck: name => AIMap.GetPlanet(name).FoodShipmentIncoming,
                    GameAI.GameAIOrder.OrderType.OrderTypeFoodTransport,
                    GameAI.GameAIOrder.OrderType.OrderTypeFoodChange,
                    GameAI.GameAIOrder.OrderType.OrderTypeFoodTransportInProgress,
                    orders);
            }

            // ── Grotsits ─────────────────────────────────────────────────────

            private void ProcessGrotsitsShortage(
                List<Planet.PlanetUpdateResult> results,
                List<GameAI.GameAIOrder>        orders)
            {
                ProcessResourceShipments(results,
                    Planet.PlanetUpdateResult.PlanetUpdateResultType.PlanetUpdateResultTypeGrotsitsShortage,
                    Planet.PlanetUpdateResult.PlanetUpdateResultType.PlanetUpdateResultTypeGrotsitsSurplus,
                    incomingCheck: name => AIMap.GetPlanet(name).GrotsitsShipmentIncoming,
                    GameAI.GameAIOrder.OrderType.OrderTypeGrotsitsTransport,
                    GameAI.GameAIOrder.OrderType.OrderTypeGrotsitsChange,
                    GameAI.GameAIOrder.OrderType.OrderTypeGrotsitsTransportInProgress,
                    orders);
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
            /// </summary>
            private void ProcessResourceShipments(
                List<Planet.PlanetUpdateResult>                  results,
                Planet.PlanetUpdateResult.PlanetUpdateResultType shortageType,
                Planet.PlanetUpdateResult.PlanetUpdateResultType surplusType,
                Func<string, bool>                               incomingCheck,
                GameAI.GameAIOrder.OrderType                     transportType,
                GameAI.GameAIOrder.OrderType                     changeType,
                GameAI.GameAIOrder.OrderType                     inProgressType,
                List<GameAI.GameAIOrder>                         orders)
            {
                var shortages = results.FindAll(x =>
                    x.Result == shortageType && x.PlayerID == Player.playerID && !incomingCheck(x.Name));
                if (shortages.Count == 0) return;

                var surplusResults = results.FindAll(x => x.PlayerID == Player.playerID && x.Result == surplusType);
                if (surplusResults.Count == 0) return;

                // Real PlanetUpdatePlanet shortages carry a negative Data; the magnitude is what matters here.
                var remainingShortage = shortages.ToDictionary(s => s.Name, s => Mathf.Abs(Convert.ToSingle(s.Data)));
                var remainingSurplus = surplusResults.ToDictionary(s => s.Name,
                    s => Convert.ToSingle(s.Data) * AIMap.GetPlanet(s.Name).GetPopulationFraction(Player.playerID));

                var maxRounds = shortages.Count + surplusResults.Count;
                for (var round = 0; round < maxRounds; round++)
                {
                    var matrix = BuildResourceMatrix(shortages, surplusResults, remainingShortage, remainingSurplus);
                    if (matrix == null) break;

                    var actions = matrix.GenerateActionList(
                        actionFactory: (origin, element) => new ResourceAction { ChosenChoiceElement = element },
                        ChoiceCompare: null);
                    if (actions.Count == 0) break;

                    foreach (var action in actions)
                    {
                        var amount = Mathf.Min(remainingSurplus[action.Origin], remainingShortage[action.Target]);
                        if (amount <= 0f) continue;

                        EmitResourceOrders(action, amount, transportType, changeType, inProgressType, orders);
                        remainingSurplus[action.Origin]  -= amount;
                        remainingShortage[action.Target] -= amount;
                    }
                }
            }

            /// <summary>
            /// Builds one decision row per shortage still owed resource, offering every reachable
            /// surplus planet that still has some left. Returns null if nothing remains to match.
            /// </summary>
            private ScoreMatrix<ScoreMatrixDecisionElement, ResourceChoiceElement, ResourceAction> BuildResourceMatrix(
                List<Planet.PlanetUpdateResult> shortages,
                List<Planet.PlanetUpdateResult> surplusResults,
                Dictionary<string, float>       remainingShortage,
                Dictionary<string, float>       remainingSurplus)
            {
                var matrix = new ScoreMatrix<ScoreMatrixDecisionElement, ResourceChoiceElement, ResourceAction  >
                    (new ScoreMatrixDecisionComparer());

                foreach (var shortage in shortages)
                {
                    if (remainingShortage[shortage.Name] <= 0f) continue;

                    var pathMap = AIMap.GetPlanet(shortage.Name).DistanceMapToPathingList;
                    var entries = surplusResults
                        .Where(s => remainingSurplus[s.Name] > 0f
                                    && pathMap[s.Name].NumNodes
                                    <= AIMap.GameAIConstants.maxPathNodesForResourceDistribution)
                        .Select(s => new ResourceChoiceElement
                        {
                            SurplusResult = s,
                            ShortageResult = shortage,
                            Cost =  pathMap[s.Name].Cost
                        })
                        .ToList();

                    if (entries.Count > 0)
                    {
                        var decision = new ScoreMatrixDecisionElement
                        {
                            Target = shortage.Name,
                            Priority = Convert.ToSingle(shortage.Data),
                        };

                        if (matrix.MatrixElements.ContainsKey(decision))
                        {
                            Debug.LogError("Duplicate Key in Build Resource Matrix");
                        }
                        else
                        {
                            matrix.MatrixElements.Add(
                                decision,
                                entries);

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

                orders.Add(MakeOrder(transportType,
                    GameAI.GameAIOrder.OrderTimingType.OrderTimingTypeDelayed,
                    delay, delay, amount, action.Origin, action.Target));

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
                var entries = choices.Select(item => new ResearchChoiceElement
                {
                    Item   = item,
                    Weight = GetResearchWeight(item, strategy),
                }).ToList();

                matrix.MatrixElements.Add(new ScoreMatrixDecisionElement
                {
                    Target = "Research",
                    Priority = 0f
                }, entries);
                return matrix;
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
                if (item.subType == "Warship" && Strategy == AIStrategy.AIStrategyConsolidate)
                {
                    if (_warshipMultiplierThisTurn == null)
                    {
                        _warshipMultiplierThisTurn = ComputeWarshipMultiplier(Strategy, out var wanted, out var have);
                        AITuningLogger.LogWarshipBoost(
                            Gameboard.Instance != null ? Gameboard.Instance.TurnNumber : 0,
                            Player.playerID, wanted, have, _warshipMultiplierThisTurn.Value);
                    }
                    return _warshipMultiplierThisTurn.Value;   // shortfall boost / surplus taper, 0 at the cap
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

            /// <summary>
            /// Expand: the home garrison plan, unchanged. Consolidate: choose the assault target, plan
            /// home defence with those ships held out, then send whatever is still spare to the target.
            /// Public (and free of Gameboard.Instance) so the self-check can drive it directly.
            /// </summary>
            public List<ShipAction> PlanShipActions(int turnNumber)
            {
                if (Strategy != AIStrategy.AIStrategyConsolidate)
                    return new ShipTransportPlanner(AIMap, Player.playerID).Plan();

                var assault = new AssaultPlanner(AIMap, Player.playerID);
                var target = assault.ChooseTarget();

                var targetName = target?.PlanetName;
                if (targetName != null && targetName != _lastLoggedAssaultTarget)
                    AITuningLogger.LogAssaultTarget(turnNumber, Player.playerID, targetName, assault.RequiredForce());
                _lastLoggedAssaultTarget = targetName;

                var transport = new ShipTransportPlanner(AIMap, Player.playerID, Strategy)
                {
                    HeldPlanet = targetName,
                };
                var actions = transport.Plan();
                actions.AddRange(assault.Plan(target, transport.LastStates, actions));
                return actions;
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
