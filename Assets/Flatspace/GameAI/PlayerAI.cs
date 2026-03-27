// PlayerAI.cs
using System;
using System.Collections.Generic;
using System.Linq;
using FlatSpace.Game;
using Flatspace.Objects.Production;
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

            // ── Production complete ───────────────────────────────────────────

            private void ProcessProductionComplete(
                List<Planet.PlanetUpdateResult> results,
                List<GameAI.GameAIOrder>        orders)
            {
                var productionCompleteResults = results.FindAll(x =>
                    x.Result == Planet.PlanetUpdateResult.PlanetUpdateResultType
                        .PlanetUpdateResultTypeIndustryProductionComplete);
                if (productionCompleteResults.Count > 0)
                    return;

                foreach (var productionCompleteResult in productionCompleteResults)
                {
                    // TODO
                }
            }

            // ── Colonization ─────────────────────────────────────────────────

            private bool IsValidColonizerForResult(Planet.PlanetUpdateResult result)
            {
                if (result.Result is
                    Planet.PlanetUpdateResult.PlanetUpdateResultType.PlanetUpdateResultTypePopulationGain
                    or Planet.PlanetUpdateResult.PlanetUpdateResultType.PlanetUpdateResultTypePopulationMax)
                {
                    var planet = AIMap.GetPlanet(result.Name);
                    if (planet.Owner == Player.playerID &&
                        planet.Population.Count >= planet.MaxPopulation
                            * AIMap.GameAIConstants.expandPopulationTrigger)
                        return true;
                }
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
                var colonizers = results.FindAll(IsValidColonizerForResult);
                if (colonizers.Count == 0) return;

                var targets = AIMap.PlanetList.FindAll(IsValidColonizationTarget);
                if (targets.Count == 0) return;

                var matrix = new ScoreMatrix();
                foreach (var colonizer in colonizers)
                {
                    var pathMap = AIMap.GetPlanet(colonizer.Name).DistanceMapToPathingList;
                    var entries = targets
                        .Where(t => pathMap.ContainsKey(t.PlanetName)
                                 && pathMap[t.PlanetName].NumNodes
                                        <= AIMap.GameAIConstants.maxPathNodesForResourceDistribution)
                        .Select(t => new ScoreMatrix.ScoreMatrixElement
                        {
                            Surplus  = 1.0f,
                            Target   = t.PlanetName,
                            Cost     = pathMap[t.PlanetName].Cost,
                            Shortage = 1.0f,
                        })
                        .ToList();

                    if (entries.Count > 0)
                        matrix.MatrixElements.Add(colonizer.Name, entries);
                }

                foreach (var action in matrix.GenerateActionList())
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

                foreach (var action in matrix.GenerateActionList())
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

                foreach (var action in matrix.GenerateActionList())
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
            private ScoreMatrix BuildResourceMatrix(
                List<Planet.PlanetUpdateResult>                  results,
                Planet.PlanetUpdateResult.PlanetUpdateResultType shortageType,
                Planet.PlanetUpdateResult.PlanetUpdateResultType surplusType,
                Func<string, bool>                               incomingCheck,
                out List<Planet.PlanetUpdateResult>              surplusResults)
            {
                surplusResults = null;

                var shortages = results.FindAll(x => x.Result == shortageType);
                if (shortages.Count == 0) return null;

                surplusResults = results.FindAll(x => x.Result == surplusType);
                if (surplusResults.Count == 0) return null;

                var matrix = new ScoreMatrix();
                foreach (var surplus in surplusResults)
                {
                    var pathMap = AIMap.GetPlanet(surplus.Name).DistanceMapToPathingList;
                    var entries = shortages
                        .Where(s => !incomingCheck(s.Name)
                                 && pathMap[s.Name].NumNodes
                                        <= AIMap.GameAIConstants.maxPathNodesForResourceDistribution)
                        .Select(s => new ScoreMatrix.ScoreMatrixElement
                        {
                            Surplus  = (float)surplus.Data,
                            Target   = s.Name,
                            Cost     = pathMap[s.Name].Cost,
                            Shortage = (float)s.Data,
                        })
                        .ToList();

                    if (entries.Count > 0)
                        matrix.MatrixElements.Add(surplus.Name, entries);
                }

                return matrix;
            }

            /// <summary>
            /// Emits the three standard orders (transport, deduct, in-progress)
            /// for a resource shipment action.
            /// </summary>
            private void EmitResourceOrders(
                ScoreMatrix.Action              action,
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
            private CatalogItem currentResearch = null;

            // Lower multiplier = higher preference. 1.0f = neutral (cost only).
            // Add entries for AIStrategyConsolidate and AIStrategyAmass when needed.
            private static readonly Dictionary<AIStrategy, Dictionary<string, float>> ResearchPriorityTable =
                new Dictionary<AIStrategy, Dictionary<string, float>>
                {
                    {
                        AIStrategy.AIStrategyExpand, new Dictionary<string, float>
                        {
                            { "Planetary Improvement", 0.5f },  // expansion favours planet upgrades
                            { "Ship Type",             0.8f },  // ships useful but secondary
                            { "Ship Improvement",      1.2f },  // least useful while expanding
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
                var completedResearchName = currentResearch?.name; 
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
                Gameboard.Instance.CreateNotificationsForResearch(completedResearchName, currentResearch?.itemName, Player.playerID);
            }

            private void CompleteReserch(List<GameAI.GameAIOrder> orders)
            {
                if (currentResearch == null)
                    return;
                currentResearch.researched = true;
                foreach( var dependantItem in ProductionCatalog.catalogItems.FindAll(x => x.requiredTech == currentResearch.itemName))
                {
                    dependantItem.researched  = true;
                }

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
                    compare:       ResearchCompare);

                if (actions.Count == 0) return;

                currentResearch = actions[0].ChosenItem;

                orders.Add(MakeOrder(
                    GameAI.GameAIOrder.OrderType.OrderTypeResearchSet,
                    GameAI.GameAIOrder.OrderTimingType.OrderTimingTypeImmediate,
                    0, 0, 0f, currentResearch.itemName, currentResearch.itemName));
            }

            /// <summary>
            /// Builds a one-row choice matrix: a single "AI" origin mapped to all
            /// available research choices, scored by cost and strategy priority.
            /// </summary>
            private ScoreMatrix<ResearchElement, ResearchAction> BuildChoiceMatrix(
                List<CatalogItem> choices,
                AIStrategy        strategy)
            {
                if (choices.Count == 0) return null;

                var matrix  = new ScoreMatrix<ResearchElement, ResearchAction>();
                var entries = choices.Select(item => new ResearchElement
                {
                    Item     = item,
                    Priority = GetResearchPriority(item, strategy),
                }).ToList();

                matrix.MatrixElements.Add("AI", entries);
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
                    typeWeights.TryGetValue(item.type, out var multiplier))
                {
                    return item.cost * multiplier;
                }

                return item.cost;  // fallback: cost only, no strategy preference
            }

            /// <summary>
            /// Compare for research elements: lowest Priority score wins.
            /// Priority already folds in cost and strategy weighting.
            /// </summary>
            private static int ResearchCompare(ResearchElement x, ResearchElement y)
                => x.Priority.CompareTo(y.Priority);

            // ── Industry ─────────────────────────────────────────────────────

            private void ProcessIndustry(
                List<Planet.PlanetUpdateResult> results,
                List<GameAI.GameAIOrder>        orders)
            {
                // TODO: PlanetUpdateResultTypeIndustryProductionComplete — select new production
                // TODO: PlanetUpdateResultTypeIndustrySurplus — ship industry
            }

            /*
            private void ProcessIndustrySurplus(List<Planet.PlanetUpdateResult> results, List<GameAI.GameAIOrder> orders)
            {
                var industrySurplusResults = results.FindAll(x =>
                    x.Result == Planet.PlanetUpdateResult.PlanetUpdateResultType.PlanetUpdateResultTypeIndustrySurplus);
                if (industrySurplusResults.Count <= 0)
                    return;

                var scoreMatrix = new ScoreMatrix();
                var productionHubList = AIMap.PlanetList.FindAll(x => x.IsIndustryProductionHub);
                foreach (var surplusProducer in industrySurplusResults)
                {
                    var validMatrixEntries = new ScoreMatrixElementList();
                    var sourcePlanet = AIMap.GetPlanet(surplusProducer.Name);
                    if (sourcePlanet == null || sourcePlanet.IsIndustryProductionHub)
                        continue;
                    var surplusPathMap = sourcePlanet.DistanceMapToPathingList;
                    foreach (var hubPlanet in productionHubList)
                    {
                        validMatrixEntries.Add(new ScoreMatrix.ScoreMatrixElement
                        {
                            Surplus = (float)surplusProducer.Data,
                            Target = hubPlanet.PlanetName,
                            Cost = surplusPathMap[hubPlanet.PlanetName].Cost,
                            Shortage = 0.0f
                        });
                    }

                    if (validMatrixEntries.Count <= 0)
                        continue;
                    scoreMatrix.MatrixElements.Add(surplusProducer.Name, validMatrixEntries);
                }

                ShipIndustry(scoreMatrix, orders, industrySurplusResults);
            }
            */

            // ── Order factory ────────────────────────────────────────────────

            private GameAI.GameAIOrder MakeOrder(
                GameAI.GameAIOrder.OrderType       type,
                GameAI.GameAIOrder.OrderTimingType timing,
                int                                timingDelay,
                int                                totalDelay,
                float                              data,
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

            void Start()
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
