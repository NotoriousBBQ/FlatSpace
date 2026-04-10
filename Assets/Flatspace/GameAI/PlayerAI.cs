// PlayerAI.cs
using System;
using System.Collections.Generic;
using System.Linq;
using FlatSpace.Game;
using Flatspace.Objects.Production;
using Flatspace.Objects.Resource;
using Unity.VisualScripting;
using UnityEngine;
using Random = UnityEngine.Random;

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
                switch (Strategy)
                {
                    case AIStrategy.AIStrategyExpand:
                        ProcessResultsStrategyExpand(results, Player, ref orders);
                        break;
                    case AIStrategy.AIStrategyConsolidate:
                        break;
                    case AIStrategy.AIStrategyAmass:
                        break;
                }
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
            }

            // ── Colonization ─────────────────────────────────────────────────
            private bool PlanetHasColonyShip(string planetName)
            {
                var planet = AIMap.GetPlanet(planetName);
                if (planet != null)
                {
                    return planet.HasColonyShip;
                }
                return false;
            }

            private bool PlanetCanColonize(string planetName)
            {
                if (IsValidColonizer(planetName))
                { 
                    var pathMap = AIMap.GetPlanet(planetName).DistanceMapToPathingList;
                    var validColonizationTargets = pathMap
                        .Where(t => (
                            t.Value.NumNodes <= AIMap.GameAIConstants.maxPathNodesForResourceDistribution
                            && IsValidColonizationTarget(AIMap.GetPlanet(t.Key)))).ToList();
                    return validColonizationTargets.Any();

                }
                return false;
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
            private bool IsValidColonizationTarget(Planet planet)
            {
                if (planet.IsPopulationTransferInProgress(Player.playerID)) return false;
                if (planet.Population.Count == 0)                           return true;
                if (planet.Population.Count >= planet.MaxPopulation)        return false;
                return planet.PlayerWithMostPopulation() != Player.playerID;
            }
            private void ProcessColonizers(
                List<Planet.PlanetUpdateResult> results,
                List<GameAI.GameAIOrder>        orders)
            {
                var colonizers = results.FindAll(
                    x => x.Result == Planet.PlanetUpdateResult.PlanetUpdateResultType.PlanetUpdateResultTypeColonizerReady);
                if (colonizers.Count == 0) return;

                var targets = AIMap.PlanetList.FindAll(IsValidColonizationTarget);
                if (targets.Count == 0) return;

                var matrix = new ScoreMatrix<ScoreMatrixDecisionElement, ScoreMatrixChoiceElement, ScoreMatrixAction>
                    (new ScoreMatrixDecisionComparer());
                int colonizerIndex = 0;
                foreach (var colonizer in colonizers)
                {
                    var pathMap = AIMap.GetPlanet(colonizer.Name).DistanceMapToPathingList;
                    var entries = targets
                        .Where(t => pathMap.ContainsKey(t.PlanetName)
                                 && pathMap[t.PlanetName].NumNodes
                                        <= AIMap.GameAIConstants.maxPathNodesForResourceDistribution)
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
                    
                }
            }

            // ── Food ─────────────────────────────────────────────────────────

            private void ProcessFoodShortage(
                List<Planet.PlanetUpdateResult> results,
                List<GameAI.GameAIOrder>        orders)
            {
                var matrix = BuildResourceMatrix(results,
                    Planet.PlanetUpdateResult.PlanetUpdateResultType.PlanetUpdateResultTypeFoodShortage,
                    Planet.PlanetUpdateResult.PlanetUpdateResultType.PlanetUpdateResultTypeFoodSurplus,
                    incomingCheck: name => AIMap.GetPlanet(name).FoodShipmentIncoming,
                    out var surplusResults);

                if (matrix == null) return;

                foreach (var action in matrix.GenerateActionList(
                             actionFactory: (origin, element) => new ResourceAction {ChosenChoiceElement = element},
                             ChoiceCompare:       null)
                             )
                EmitResourceOrders(action, surplusResults,
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
                var matrix = BuildResourceMatrix(results,
                    Planet.PlanetUpdateResult.PlanetUpdateResultType.PlanetUpdateResultTypeGrotsitsShortage,
                    Planet.PlanetUpdateResult.PlanetUpdateResultType.PlanetUpdateResultTypeGrotsitsSurplus,
                    incomingCheck: name => AIMap.GetPlanet(name).GrotsitsShipmentIncoming,
                    out var surplusResults);

                if (matrix == null) return;

                foreach (var action in matrix.GenerateActionList(
                             actionFactory: (origin, element) => new ResourceAction {ChosenChoiceElement = element},
                             ChoiceCompare:       null))
                    EmitResourceOrders(action, surplusResults,
                        GameAI.GameAIOrder.OrderType.OrderTypeGrotsitsTransport,
                        GameAI.GameAIOrder.OrderType.OrderTypeGrotsitsChange,
                        GameAI.GameAIOrder.OrderType.OrderTypeGrotsitsTransportInProgress,
                        orders);
            }

            /// <summary>
            /// Shared matrix-building logic for any surplus→shortage resource.
            /// Returns null if there is nothing to do.
            /// </summary>
            private ScoreMatrix<ScoreMatrixDecisionElement, ResourceChoiceElement, ResourceAction> BuildResourceMatrix(
                List<Planet.PlanetUpdateResult>                  results,
                Planet.PlanetUpdateResult.PlanetUpdateResultType shortageType,
                Planet.PlanetUpdateResult.PlanetUpdateResultType surplusType,
                Func<string, bool>                               incomingCheck,
                out List<Planet.PlanetUpdateResult>              surplusResults)
            {
                surplusResults = null;

                var shortages = results.FindAll(x => x.Result == shortageType && !incomingCheck(x.Name));
                if (shortages.Count == 0) return null;

                surplusResults = results.FindAll(x => x.Result == surplusType);
                if (surplusResults.Count == 0) return null;

                var matrix = new ScoreMatrix<ScoreMatrixDecisionElement, ResourceChoiceElement, ResourceAction  >
                    (new ScoreMatrixDecisionComparer());

                foreach (var shortage in shortages)
                {
                    var pathMap = AIMap.GetPlanet(shortage.Name).DistanceMapToPathingList;
                    var entries = surplusResults
                        .Where(s => pathMap[s.Name].NumNodes
                                    <= AIMap.GameAIConstants.maxPathNodesForResourceDistribution)
                        .Select(s => new ResourceChoiceElement
                        {
                            SurplusResult = s,
                            ShortageResult = shortage,
                            Cost =  pathMap[s.Name].Cost
                        })
                        .ToList();

                    if (entries.Count > 0)
                        matrix.MatrixElements.Add( 
                            new ScoreMatrixDecisionElement
                            {
                                Target =  shortage.Name,
                                Priority = Convert.ToSingle(shortage.Data),
                            },
                            entries);
                }

                return matrix;
            }

            /// <summary>
            /// Emits the three standard orders (transport, deduct, in-progress)
            /// for a resource shipment action.
            /// </summary>
            private void EmitResourceOrders(
                ResourceAction              action,
                List<Planet.PlanetUpdateResult> surplusResults,
                GameAI.GameAIOrder.OrderType    transportType,
                GameAI.GameAIOrder.OrderType    changeType,
                GameAI.GameAIOrder.OrderType    inProgressType,
                List<GameAI.GameAIOrder>        orders)
            {
                var originPlanet = AIMap.GetPlanet(action.Origin);
                var amount       = Convert.ToSingle(
                    surplusResults.Find(x => x.Name == action.Origin).Data)
                    * originPlanet.GetPopulationFraction(Player.playerID);

                if (amount <= 0.0f) return;

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

            // Lower multiplier = higher preference. 1.0f = neutral (cost only).
            // Add entries for AIStrategyConsolidate and AIStrategyAmass when needed.
            private static readonly Dictionary<AIStrategy, Dictionary<string, float>> ResearchPriorityTable =
                new Dictionary<AIStrategy, Dictionary<string, float>>
                {
                    {
                        AIStrategy.AIStrategyExpand, new Dictionary<string, float>
                        {
                            { "Food",          0.7f },  // food upgrades biggest boost
                            { "Industry",      0.8f },  // least useful while expanding
                            { "Grotsits",      1.1f },  // least useful while expanding
                            { "Research",      1.1f },  // least useful while expanding
                            { "ColonyShip",   0.8f },  // ships useful but secondary
                            { "Warship",       1.3f },  // least useful while expanding
                        }
                    },
                    // AIStrategyConsolidate — add when needed
                    // AIStrategyAmass       — add when needed
                };

            private void ProcessResearch(
                List<Planet.PlanetUpdateResult> results,
                List<GameAI.GameAIOrder>        orders)
            {
                var researchResults = results
                    .Where(p => p.Result == Planet.PlanetUpdateResult.PlanetUpdateResultType
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
                    CompleteReserch(orders);
                    ChooseNewResearch(orders);
                }
            }

            private void CompleteReserch(List<GameAI.GameAIOrder> orders)
            {
                if (currentResearch == null)
                    return;
                var completedResearchName = currentResearch.name;
                currentResearch.researched = true;
                foreach( var dependantItem in ProductionCatalog.catalogItems.FindAll(x => x.requiredTech == currentResearch.itemName))
                {
                    dependantItem.researched  = true;
                }
                Gameboard.Instance.CreateNotificationsForCompletedResearch(completedResearchName, Player.playerID);

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
                    ChoiceCompare:       ResearchCompare);

                if (actions.Count == 0) return;

                currentResearch = actions[0].ChosenItem;
                if (Player.playerID == 0)
                {
                    Debug.Log("Turn: " + Gameboard.Instance.TurnNumber + "New Research: " + currentResearch.name);
                }

                orders.Add(MakeOrder(
                    GameAI.GameAIOrder.OrderType.OrderTypeResearchSet,
                    GameAI.GameAIOrder.OrderTimingType.OrderTimingTypeImmediate,
                    0, 0, 0f, currentResearch.itemName, currentResearch.itemName));
                Gameboard.Instance.CreateNotificationsForNewResearch(currentResearch?.itemName, Player.playerID);
            }

            /// <summary>
            /// Builds a one-row choice matrix: a single "AI" origin mapped to all
            /// available research choices, scored by cost and strategy priority.
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
                    Item     = item,
                    Priority = GetResearchPriority(item, strategy),
                }).ToList();

                matrix.MatrixElements.Add(new ScoreMatrixDecisionElement
                {
                    Target = "Research",
                    Priority = 0f
                }, entries);
                return matrix;
            }

            /// <summary>
            /// Returns a priority score for a research item given the current strategy.
            /// Lower = more preferred. Multiplier comes from the priority table;
            /// falls back to cost-only if the strategy or type has no entry.
            /// </summary>
            private static float  GetResearchPriority(CatalogItem item, AIStrategy strategy)
            {
                if (ResearchPriorityTable.TryGetValue(strategy, out var typeWeights) &&
                    typeWeights.TryGetValue(item.subType, out var multiplier))
                {
                    return item.cost * multiplier * Random.Range(0.9f, 1.1f);
                }

                return item.cost;  // fallback: cost only, no strategy preference
            }

            /// <summary>
            /// Compare for research elements: lowest Priority score wins.
            /// Priority already folds in cost and strategy weighting.
            /// </summary>
            private static int ResearchCompare(ResearchChoiceElement x, ResearchChoiceElement y)
                => x.Priority.CompareTo(y.Priority);

            // ── Industry ─────────────────────────────────────────────────────
            // Lower multiplier = higher preference. 1.0f = neutral (cost only).
            // Add entries for AIStrategyConsolidate and AIStrategyAmass when needed.
            private static int ProductionCompare(IndustryChoiceElement x, IndustryChoiceElement y)
                => x.Priority.CompareTo(y.Priority);
            
            private static readonly Dictionary<AIStrategy, Dictionary<string, float>> IndustryPriorityTable =
                new Dictionary<AIStrategy, Dictionary<string, float>>
                {
                    {
                        AIStrategy.AIStrategyExpand, new Dictionary<string, float>
                        {
                            { "Food",          0.8f },  // food needed for pop growth
                            { "Industry",      1.0f },  // slightly useful while expanding
                            { "Grotsits",      1.1f },  // build the base
                            { "Research",      1.1f },  // build the base
                            { "ColonyShip",    0.8f },  // ships colony ships needed
                            { "Warship",       1.3f },  // least useful while expanding
                        }
                    },
                    // AIStrategyConsolidate — add when needed
                    // AIStrategyAmass       — add when needed
                };
            private float  GetIndustryPriority(CatalogItem item, AIStrategy strategy, string planetName)
            {
                var cost = item.cost;
                var costMultiplier = 1f;
                if (ResearchPriorityTable.TryGetValue(strategy, out var typeWeights) &&
                    typeWeights.TryGetValue(item.subType, out var strategyMultiplier))
                {
                    costMultiplier = strategyMultiplier * Random.Range(0.9f, 1.1f);
                }
                costMultiplier *= GetIndustrySituationalCostMultiplier(item, planetName);
                cost *= costMultiplier;
                return cost;  // fallback: cost only, no strategy preference
            }

            private float GetIndustrySituationalCostMultiplier(CatalogItem item, string planetName)
            {
                var multiplier = 1f;
                if (item.subType == "ColonyShip")
                {
                    if (PlanetHasColonyShip(planetName))
                        multiplier = float.MaxValue;
                    else if (PlanetCanColonize(planetName))
                        multiplier = -1f;
                }
                return multiplier;
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
                             ChoiceCompare:       ProductionCompare)
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
                var surplusResults = productionCompleteResults.FindAll(x =>
                    x.Result is Planet.PlanetUpdateResult.PlanetUpdateResultType.PlanetUpdateResultTypeIndustrySurplus);
                
                if (productionCompleteResults.Count == 0)
                    return null;

                var matrix = new ScoreMatrix<ScoreMatrixMultipleDecisionElement, IndustryChoiceElement, IndustryAction  >
                    (new ScoreMatrixMultipleDecisionComparer());

                var decisionIndex = 0;
                foreach (var planetName in productionCompleteResults.Select(x => x.Name).Distinct())
                {
                    
                    var planetResults = productionCompleteResults.FindAll(x => x.Name == planetName);
                    var planet = AIMap.GetPlanet(planetName);
                    var potentialProduction = ProductionCatalog.catalogItems.FindAll(x => x.researched == true 
                        && !(planet.CompletedImprovements.Select(y => y.Item1).ToList().Contains(x.name)) );

                    var planetSurplus = surplusResults.FindIndex(x => x.Name == planetName) == -1
                        ? 0f
                        : Convert.ToSingle(surplusResults.Find(x => x.Name == planetName).Data); 
                    var entries = potentialProduction.Select(item => new IndustryChoiceElement
                    {
                        Item     = item,
                        Priority = GetIndustryPriority(item, strategy, planetName),
                        Surplus = planetSurplus,
                        PlanetName = planetName,
                    }).ToList();

                    if(entries.Count == 0) continue;
                    
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
