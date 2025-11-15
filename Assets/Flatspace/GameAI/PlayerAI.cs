using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using ScoreMatrixElementList = System.Collections.Generic.List<ScoreMatrix.ScoreMatrixElement>;

namespace FlatSpace
{
    namespace AI
    {
        public class PlayerAI : MonoBehaviour
        {
            public enum AIStrategy
            {
                AIStrategyNone = 0,
                AIStrategyExpand,
                AIStrategyConsolidate,
                AIStrategyAmass
            }

            public AIStrategy Strategy { get; set; } = AIStrategy.AIStrategyExpand;
            public Player Player { get; set; }
            public GameAIMap AIMap { get; set; }

            public void ProcessResults(List<Planet.PlanetUpdateResult> results, List<GameAI.GameAIOrder> orders)
            {
                switch (Strategy)
                {
                    case PlayerAI.AIStrategy.AIStrategyExpand:
                        ProcessResultsStrategyExpand(results, Player, ref orders);
                        break;
                    case PlayerAI.AIStrategy.AIStrategyConsolidate:
                        break;
                    case PlayerAI.AIStrategy.AIStrategyAmass:
                        break;
                }                
            }
            private void ProcessResultsStrategyExpand(List<Planet.PlanetUpdateResult> results, Player player, 
                ref List<GameAI.GameAIOrder> orders)
            { 
                ProcessColonizers(results, orders);
                ProcessFoodShortage(results, orders);
                ProcessGrotsitsShortage(results, orders);
                ProcessResearch(results, orders);
                ProcessIndustry(results, orders);
            }
            private static void GenerateActionList(ScoreMatrix scoreMatrix,
                out List<(string Origin, string Target, float Cost)> actionList)
            {
                actionList = new List<(string, string, float)>();
                foreach (var scoreMatrixElementList in scoreMatrix.MatrixElements.Values)
                {
                    scoreMatrixElementList.Sort(ScoreMatrix.ScoreMatrixElementCompare);
                }

                var minimumDistanceList = new ScoreMatrixElementList();
                while (scoreMatrix.MatrixElements.Count > 0 && scoreMatrix.MatrixElements.Values.First().Count > 0)
                {
                    foreach (var key in scoreMatrix.MatrixElements.Keys)
                    {
                        if (scoreMatrix.MatrixElements[key].Count <= 0)
                            continue;
                        minimumDistanceList.Add(new ScoreMatrix.ScoreMatrixElement
                        {
                            Target = key,
                            Cost = scoreMatrix.MatrixElements[key].First().Cost,
                            Surplus = scoreMatrix.MatrixElements[key].First().Surplus,
                            Shortage = scoreMatrix.MatrixElements[key].First().Shortage,
                        });
                    }

                    minimumDistanceList.Sort(ScoreMatrix.ScoreMatrixElementCompare);
                    var closestTargetName = scoreMatrix.MatrixElements[minimumDistanceList[0].Target][0].Target;
                    actionList.Add((minimumDistanceList[0].Target, closestTargetName, minimumDistanceList[0].Cost));

                    scoreMatrix.MatrixElements.Remove(minimumDistanceList[0].Target);
                    foreach (var nNTupleList in scoreMatrix.MatrixElements.Values)
                    {
                        nNTupleList.RemoveAll(x => x.Target == closestTargetName);
                    }

                    minimumDistanceList.Clear();
                }
            }

            private void ProcessGrotsitsShortage(List<Planet.PlanetUpdateResult> results, List<GameAI.GameAIOrder> orders)
            {
                var shortageResults = results.FindAll(x =>
                    x.Result is Planet.PlanetUpdateResult.PlanetUpdateResultType
                        .PlanetUpdateResultTypeGrotsitsShortage);
                if (shortageResults.Count <= 0)
                    return;

                var surplusResults = results.FindAll(x =>
                    x.Result == Planet.PlanetUpdateResult.PlanetUpdateResultType.PlanetUpdateResultTypeGrotsitsSurplus);
                if (surplusResults.Count <= 0)
                    return;

                var scoreMatrix = new ScoreMatrix();
                foreach (var surplusProducer in surplusResults)
                {
                    var validMatrixEntries = new ScoreMatrixElementList();
                    var surplusPathMap = AIMap.GetPlanet(surplusProducer.Name).DistanceMapToPathingList;
                    foreach (var shortageResult in shortageResults)
                    {
                        if(AIMap.GetPlanet(shortageResult.Name).GrotsitsShipmentIncoming == true)
                            continue;
                        if (surplusPathMap[shortageResult.Name].NumNodes <=
                            AIMap.GameAIConstants.maxPathNodesForResourceDistribution)
                            validMatrixEntries.Add(new ScoreMatrix.ScoreMatrixElement
                            {
                                Surplus = (float)surplusProducer.Data,
                                Target = shortageResult.Name,
                                Cost = surplusPathMap[shortageResult.Name].Cost,
                                Shortage = (float)shortageResult.Data
                            });
                    }

                    if (validMatrixEntries.Count <= 0)
                        continue;
                    scoreMatrix.MatrixElements.Add(surplusProducer.Name, validMatrixEntries);
                }

                ShipGrotsits(scoreMatrix, orders, surplusResults);
            }

            private void ShipGrotsits(ScoreMatrix scoreMatrix, List<GameAI.GameAIOrder> orders,
                List<Planet.PlanetUpdateResult> surplusResults)
            {
                List<(string Origin, string Target, float Cost)> actionList;

                GenerateActionList(scoreMatrix, out actionList);

                foreach (var actionNTuple in actionList)
                {
                    var changeAmount = Convert.ToSingle(surplusResults.Find(x => x.Name == actionNTuple.Origin).Data);
                    var originPlanet = AIMap.GetPlanet(actionNTuple.Origin);
                    var playerPopFraction = originPlanet.GetPopulationFraction(Player.playerID);
                    changeAmount *= playerPopFraction;
                    if (changeAmount <= 0.0f)
                        continue;
                    orders.Add(new GameAI.GameAIOrder
                    {
                        Type = GameAI.GameAIOrder.OrderType.OrderTypeGrotsitsTransport,
                        TimingType = GameAI.GameAIOrder.OrderTimingType.OrderTimingTypeDelayed,
                        TimingDelay = Convert.ToInt32(actionNTuple.Cost / AIMap.GameAIConstants.defaultTravelSpeed),
                        TotalDelay = Convert.ToInt32(actionNTuple.Cost / AIMap.GameAIConstants.defaultTravelSpeed),
                        Data = changeAmount,
                        Origin = actionNTuple.Origin,
                        Target = actionNTuple.Target,
                        PlayerId = Player.playerID
                    });
                    orders.Add(new GameAI.GameAIOrder
                    {
                        Type = GameAI.GameAIOrder.OrderType.OrderTypeGrotsitsChange,
                        TimingType = GameAI.GameAIOrder.OrderTimingType.OrderTimingTypeImmediate,
                        TimingDelay = 0,
                        TotalDelay = 0,
                        Data = changeAmount * -1.0f,
                        Origin = actionNTuple.Origin,
                        Target = actionNTuple.Origin,
                        PlayerId = Player.playerID
                    });
                    orders.Add(new GameAI.GameAIOrder
                    {
                        Type = GameAI.GameAIOrder.OrderType.OrderTypeGrotsitsTransportInProgress,
                        TimingType = GameAI.GameAIOrder.OrderTimingType.OrderTimingTypeImmediate,
                        TimingDelay = 0,
                        TotalDelay = 0,
                        Data = changeAmount,
                        Origin = actionNTuple.Origin,
                        Target = actionNTuple.Target,
                        PlayerId = Player.playerID
                    });
                }
            }

            private void ProcessResearch(List<Planet.PlanetUpdateResult> results,
                List<GameAI.GameAIOrder> orders)
            {
                var researchResults = results
                    .Where(p
                        => p.Result == Planet.PlanetUpdateResult.PlanetUpdateResultType
                            .PlanetUpdateResultTypeResearchProduced).ToList();
                if (researchResults.Count <= 0)
                    return;
                var totalResearch = researchResults.Sum(p => (float)p.Data); // Sum the Price field
                if(totalResearch > 0.0f)
                    UpdateResearch(totalResearch, orders);
                foreach (var researchResult in researchResults)
                {
                    orders.Add(new GameAI.GameAIOrder
                    {
                        Type = GameAI.GameAIOrder.OrderType.OrderTypeResearchChange,
                        TimingType = GameAI.GameAIOrder.OrderTimingType.OrderTimingTypeImmediate,
                        TimingDelay = 0,
                        TotalDelay = 0,
                        Data = (float)researchResult.Data * -1.0f,
                        Origin = researchResult.Name,
                        Target = researchResult.Name,
                        PlayerId = Player.playerID
                    });

                }
            }

            private void UpdateResearch(float totalResearch, List<GameAI.GameAIOrder> orders)
            {
                // check to see if current research is done and choose another
            }

            private void ProcessIndustry(List<Planet.PlanetUpdateResult> results,
                List<GameAI.GameAIOrder> orders)
            {
                //PlanetUpdateResultTypeIndustryProductionComplete, select new produciton

                //PlanetUpdateResultTypeIndustrySurplus, ship industry
            }

            private void ProcessFoodShortage(List<Planet.PlanetUpdateResult> results, List<GameAI.GameAIOrder> orders)
            {
                var shortageResults = results.FindAll(x =>
                    x.Result == Planet.PlanetUpdateResult.PlanetUpdateResultType.PlanetUpdateResultTypeFoodShortage);
                if (shortageResults.Count <= 0)
                    return;

                var surplusResults = results.FindAll(x =>
                    x.Result == Planet.PlanetUpdateResult.PlanetUpdateResultType.PlanetUpdateResultTypeFoodSurplus);
                if (surplusResults.Count <= 0)
                    return;

                var scoreMatrix = new ScoreMatrix();
                foreach (var surplusProducer in surplusResults)
                {
                    var validMatrixEntries = new ScoreMatrixElementList();
                    var surplusPathMap = AIMap.GetPlanet(surplusProducer.Name).DistanceMapToPathingList;
                    foreach (var shortageResult in shortageResults)
                    {
                        if(AIMap.GetPlanet(shortageResult.Name).FoodShipmentIncoming == true)
                            continue;
                        if (surplusPathMap[shortageResult.Name].NumNodes <=
                            AIMap.GameAIConstants.maxPathNodesForResourceDistribution)
                            validMatrixEntries.Add(new ScoreMatrix.ScoreMatrixElement
                            {
                                Surplus = (float)surplusProducer.Data,
                                Target = shortageResult.Name,
                                Cost = surplusPathMap[shortageResult.Name].Cost,
                                Shortage = (float)shortageResult.Data
                            });
                    }

                    if (validMatrixEntries.Count <= 0)
                        continue;
                    scoreMatrix.MatrixElements.Add(surplusProducer.Name, validMatrixEntries);
                }

                ShipFood(scoreMatrix, orders, surplusResults);
            }

            private void ShipFood(ScoreMatrix scoreMatrix, List<GameAI.GameAIOrder> orders,
                List<Planet.PlanetUpdateResult> surplusResults)
            {
                GenerateActionList(scoreMatrix, out var actionList);

                foreach (var actionNTuple in actionList)
                {
                    float changeAmount = Convert.ToSingle(surplusResults.Find(x => x.Name == actionNTuple.Item1).Data);
                    var originPlanet = AIMap.GetPlanet(actionNTuple.Origin);

                    var playerPopFraction = originPlanet.GetPopulationFraction(Player.playerID);
                    changeAmount *= playerPopFraction;
                    if (changeAmount <= 0.0f)
                        continue;
                    orders.Add(new GameAI.GameAIOrder
                    {
                        Type = GameAI.GameAIOrder.OrderType.OrderTypeFoodTransport,
                        TimingType = GameAI.GameAIOrder.OrderTimingType.OrderTimingTypeDelayed,
                        TimingDelay =
                            Convert.ToInt32(actionNTuple.Cost / AIMap.GameAIConstants.defaultTravelSpeed),
                        TotalDelay = Convert.ToInt32(actionNTuple.Cost / AIMap.GameAIConstants.defaultTravelSpeed),
                        Data = changeAmount,
                        Origin = actionNTuple.Origin,
                        Target = actionNTuple.Target,
                        PlayerId = Player.playerID
                    });
                    orders.Add(new GameAI.GameAIOrder
                    {
                        Type = GameAI.GameAIOrder.OrderType.OrderTypeFoodChange,
                        TimingType = GameAI.GameAIOrder.OrderTimingType.OrderTimingTypeImmediate,
                        TimingDelay = 0,
                        TotalDelay = 0,
                        Data = changeAmount * -1.0f,
                        Origin = actionNTuple.Origin,
                        Target = actionNTuple.Origin,
                        PlayerId = Player.playerID
                    });
                    orders.Add(new GameAI.GameAIOrder
                    {
                        Type = GameAI.GameAIOrder.OrderType.OrderTypeFoodTransportInProgress,
                        TimingType = GameAI.GameAIOrder.OrderTimingType.OrderTimingTypeImmediate,
                        TimingDelay = 0,
                        TotalDelay = 0,
                        Data = changeAmount,
                        Origin = actionNTuple.Origin,
                        Target = actionNTuple.Target,
                        PlayerId = Player.playerID
                    });
                }
            }

            private bool IsValidColonizerForResult(Planet.PlanetUpdateResult result)
            {
                if(result.Result is Planet.PlanetUpdateResult.PlanetUpdateResultType.PlanetUpdateResultTypePopulationGain
                        or Planet.PlanetUpdateResult.PlanetUpdateResultType.PlanetUpdateResultTypePopulationMax)
                {
                    var resultPlanet = AIMap.GetPlanet(result.Name); 
                    if (resultPlanet.Owner == Player.playerID &&  
                        resultPlanet.Population.Count >= resultPlanet.MaxPopulation * AIMap.GameAIConstants.expandPopulationTrigger)
                    {
                        return true;
                    }
                }
                return false;
            }

            private bool IsValidColonizationTarget(Planet planet)
            {
                if (planet.IsPopulationTransferInProgress(Player.playerID))
                    return false;
                if (planet.Population.Count == 0)
                    return true;
                if (planet.Population.Count >= planet.MaxPopulation)
                    return false;
                return (planet.PlayerWithMostPopulation() != Player.playerID);
            }
            private void ProcessColonizers(List<Planet.PlanetUpdateResult> results, List<GameAI.GameAIOrder> orders)
            {
                // find every gain pop result where the total pop is over the colonization trigger
                var possibleColonizers = results.FindAll(IsValidColonizerForResult);
                if (possibleColonizers.Count <= 0)
                    return;

                var possibleColonizerTargets =
                    AIMap.PlanetList.FindAll(IsValidColonizationTarget);
                if (possibleColonizerTargets.Count <= 0)
                    return;

                // create a score matrix for each potential colonizer's distance to each possible target
                var scoreMatrix = new ScoreMatrix();

                foreach (var colonizer in possibleColonizers)
                {
                    var validMatrixEntries = new ScoreMatrixElementList();
                    var colonizerPathMap = AIMap.GetPlanet(colonizer.Name).DistanceMapToPathingList;
                    foreach (var colonizerTarget in possibleColonizerTargets.FindAll(x =>
                                 colonizerPathMap.ContainsKey(x.PlanetName)
                                 && colonizerPathMap[x.PlanetName].NumNodes <=
                                 AIMap.GameAIConstants.maxPathNodesForResourceDistribution
                             ))
                    {

                        validMatrixEntries.Add(new ScoreMatrix.ScoreMatrixElement
                        {
                            Surplus = 1.0f,
                            Target = colonizerTarget.PlanetName,
                            Cost = colonizerPathMap[colonizerTarget.PlanetName].Cost,
                            Shortage = 1.0f
                        });
                    }

                    if (validMatrixEntries.Count <= 0)
                        continue;
                    scoreMatrix.MatrixElements.Add(colonizer.Name, validMatrixEntries);
                }

                SendColonizers(scoreMatrix, orders, possibleColonizers);

            }

            private void SendColonizers(ScoreMatrix scoreMatrix, List<GameAI.GameAIOrder> orders,
                List<Planet.PlanetUpdateResult> possibleColonizers)
            {
                GenerateActionList(scoreMatrix, out var actionList);
                foreach (var actionNTuple in actionList)
                {
                    var changeAmount =
                        Convert.ToInt32(possibleColonizers.Find(x => x.Name == actionNTuple.Origin).Data);

                    orders.Add(new GameAI.GameAIOrder
                    {
                        Type = GameAI.GameAIOrder.OrderType.OrderTypePopulationTransport,
                        TimingType = GameAI.GameAIOrder.OrderTimingType.OrderTimingTypeDelayed,
                        TimingDelay =
                            Convert.ToInt32(actionNTuple.Cost / AIMap.GameAIConstants.defaultTravelSpeed),
                        TotalDelay = Convert.ToInt32(actionNTuple.Cost / AIMap.GameAIConstants.defaultTravelSpeed),
                        Data = changeAmount,
                        Origin = actionNTuple.Origin,
                        Target = actionNTuple.Target,
                        PlayerId = Player.playerID
                    });
                    orders.Add(new GameAI.GameAIOrder
                    {
                        Type = GameAI.GameAIOrder.OrderType.OrderTypePopulationChange,
                        TimingType = GameAI.GameAIOrder.OrderTimingType.OrderTimingTypeImmediate,
                        TimingDelay = 0,
                        TotalDelay = 0,
                        Data = changeAmount * -1.0f,
                        Origin = actionNTuple.Origin,
                        Target = actionNTuple.Origin,
                        PlayerId = Player.playerID
                    });

                    orders.Add(new GameAI.GameAIOrder
                    {
                        Type = GameAI.GameAIOrder.OrderType.OrderTypePopulationTransferInProgress,
                        TimingType = GameAI.GameAIOrder.OrderTimingType.OrderTimingTypeImmediate,
                        TimingDelay = 0,
                        TotalDelay = 0,
                        Data = changeAmount,
                        Origin = actionNTuple.Origin,
                        Target = actionNTuple.Target,
                        PlayerId = Player.playerID
                    });
                }
            }



            // Start is called once before the first execution of UpdatePlanet after the MonoBehaviour is created
            void Start()
            {

            }

            // UpdatePlanet is called once per frame
            void Update()
            {

            }
        }
    }
}
