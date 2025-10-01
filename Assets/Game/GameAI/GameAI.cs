using System;
using System.Collections.Generic;
using System.Linq;
using FlatSpace.Game;
using Unity.VisualScripting;
using UnityEngine;
using ScoreMatrixElementList = System.Collections.Generic.List<ScoreMatrix.ScoreMatrixElement>;

public class GameAI : MonoBehaviour
{
    [Serializable]
    public class GameAIOrder
    {
        public enum OrderType
        {
            OrderTypeNone,
            OrderTypePopulationTransport,
            OrderTypePopulationChange,
            OrderTypePopulationTransferInProgress,
            OrderTypeFoodTransport,
            OrderTypeFoodChange,
            OrderTypeGrotsitsTransport,
            OrderTypeGrotsitsChange,
        }
        
        public enum OrderTimingType
        {
            OrderTimingTypeDelayed,
            OrderTimingTypeImmediate,
            OrderTimingTypeHold,
        }
        
        public OrderType Type;
        public OrderTimingType TimingType;
        public int TimingDelay;
        public int TotalDelay;
        public object Data;
        public string Target;
        public string Origin;
        public int PlayerId;
    }

    public GameAIMap GameAIMap {get; private set;}
    public List<GameAIOrder> CurrentAIOrders { get; private set; }= new List<GameAIOrder>();
    
    public void InitGameAI(List<PlanetSpawnData> spawnDataList, GameAIConstants gameAIConstants)
    {
        GameAIMap = this.AddComponent<GameAIMap>() as GameAIMap;
        GameAIMap.GameAIMapInit(spawnDataList, gameAIConstants);
    }

    public void ClearGameAI()
    {
        CurrentAIOrders.Clear();
        
    }

    public void GameAIUpdate()
    {
        var gameAIOrders = new List<GameAIOrder>();
        var planetUpdateResults = new List<Planet.PlanetUpdateResult>();
        ProcessCurrentOrders();
        for(var i = 0; i < Gameboard.Instance.players.Count; i++)
        {
            planetUpdateResults.Clear();
            PlanetaryProductionUpdate(planetUpdateResults, i);
            ProcessResults(planetUpdateResults, i, gameAIOrders);
            
        }
        ProcessNewOrders(gameAIOrders);
    }

    private void ProcessCurrentOrders()
    {
        foreach (var gameAIOrder in CurrentAIOrders)
        {
            gameAIOrder.TimingDelay--;
        }
        
        var executableOrders = CurrentAIOrders.FindAll(x => x.TimingDelay <= 0);
        foreach (var executableOrder in executableOrders)
        {
            ExecuteOrder(executableOrder);
        }
        CurrentAIOrders.RemoveAll(x => x.TimingDelay <= 0);
    }

    private void ExecuteOrder(GameAIOrder executableOrder)
    {
        var targetPlanet = GameAIMap.GetPlanet(executableOrder.Target);
        switch (executableOrder.Type)
        {
            case GameAIOrder.OrderType.OrderTypePopulationTransport:
                targetPlanet.Population += Convert.ToInt32(executableOrder.Data);
                if (targetPlanet.PopulationTransferInProgress == true)
                {
                    targetPlanet.Owner = executableOrder.PlayerId;
                    targetPlanet.PopulationTransferInProgress = false;
                }
                break;
            case GameAIOrder.OrderType.OrderTypePopulationChange:
                targetPlanet.Population += Convert.ToInt32(executableOrder.Data);
                break;
            case GameAIOrder.OrderType.OrderTypePopulationTransferInProgress:
                targetPlanet.PopulationTransferInProgress = true;
                break;
            case GameAIOrder.OrderType.OrderTypeFoodTransport:
            case GameAIOrder.OrderType.OrderTypeFoodChange:
                targetPlanet.Food += Convert.ToSingle(executableOrder.Data);
                break;
            case GameAIOrder.OrderType.OrderTypeGrotsitsTransport:
            case GameAIOrder.OrderType.OrderTypeGrotsitsChange:
                targetPlanet.Grotsits += Convert.ToSingle(executableOrder.Data);
                break;
            default:
                break;
        }

    }
    private void ProcessNewOrders(List<GameAIOrder> newOrders)
    {
        CurrentAIOrders.AddRange(newOrders.FindAll(x => x.TimingType == GameAIOrder.OrderTimingType.OrderTimingTypeDelayed));
        var executableOrders = newOrders.FindAll(x => x.TimingDelay <= 0);
        foreach (var executableOrder in executableOrders)
            ExecuteOrder(executableOrder);
    }
    private void PlanetaryProductionUpdate(List<Planet.PlanetUpdateResult> planetUpdateResults, int playerID)
    {
        GameAIMap.PlanetaryProductionUpdate(planetUpdateResults, playerID);
    }


    private void ProcessResults(List<Planet.PlanetUpdateResult> results, int playerID, List<GameAIOrder> orders)
    {
        switch (Gameboard.Instance.players[playerID].Strategy)
        {
            case Player.AIStrategy.AIStrategyExpand:
                ProcessResultsStrategyExpand(results, playerID, ref orders);
                break;
            case Player.AIStrategy.AIStrategyConsolidate:
                break;
            case Player.AIStrategy.AIStrategyAmass:
                break;
        }        
    }

    private void ProcessResultsStrategyExpand(List<Planet.PlanetUpdateResult> results, int playerID, ref List<GameAIOrder> orders)
    {
        ProcessColonizers(results, playerID, orders);
        ProcessFoodShortage(results, playerID, orders);
        ProcessGrotsitsShortage(results, playerID, orders);
 
    }

    private static void GenerateActionList(ScoreMatrix scoreMatrix, out List<(string Origin, string Target, float Cost)> actionList)
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
            var closestTargetName =  scoreMatrix.MatrixElements[minimumDistanceList[0].Target][0].Target;
            actionList.Add(( minimumDistanceList[0].Target, closestTargetName, minimumDistanceList[0].Cost));

            scoreMatrix.MatrixElements.Remove(minimumDistanceList[0].Target);
            foreach (var nNTupleList in scoreMatrix.MatrixElements.Values)
            {
                nNTupleList.RemoveAll(x => x.Target == closestTargetName);
            }
            minimumDistanceList.Clear();
        }
    }
    
    private void ProcessGrotsitsShortage(List<Planet.PlanetUpdateResult> results, int playerID, List<GameAIOrder> orders)
    {
        var shortageResults = results.FindAll(x =>
            x.Result is Planet.PlanetUpdateResult.PlanetUpdateResultType.PlanetUpdateResultTypeGrotsitsShortage);
        if(shortageResults.Count <= 0)
            return;

        var surplusResults = results.FindAll(x =>
            x.Result == Planet.PlanetUpdateResult.PlanetUpdateResultType.PlanetUpdateResultTypeGrotsitsSurplus);
        if (surplusResults.Count <= 0)
            return;
        
        var scoreMatrix = new ScoreMatrix();
        foreach (var surplusProducer in surplusResults)
        {
            var validMatrixEntries = new ScoreMatrixElementList();
            var surplusPathMap = GetPlanet(surplusProducer.Name).DistanceMapToPathingList;
            foreach (var shortageResult in shortageResults)
            {
                if(surplusPathMap[shortageResult.Name].NumNodes <= GameAIMap.GameAIConstants.maxPathNodesForResourceDistribution)
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
            scoreMatrix.MatrixElements.Add(surplusProducer.Name,validMatrixEntries);
        }
        
        ShipGrotsits(scoreMatrix, playerID, orders, surplusResults);
    }

    private void ShipGrotsits(ScoreMatrix scoreMatrix, int playerID, List<GameAIOrder> orders, List<Planet.PlanetUpdateResult> surplusResults)
    {
        List<(string Origin, string Target, float Cost)> actionList;
        
        GenerateActionList(scoreMatrix, out actionList);

        foreach (var actionNTuple in actionList)
        {
            float changeAmount = Convert.ToSingle(surplusResults.Find(x => x.Name == actionNTuple.Origin).Data);
            orders.Add(new GameAIOrder
            {
                Type = GameAIOrder.OrderType.OrderTypeGrotsitsTransport,
                TimingType = GameAIOrder.OrderTimingType.OrderTimingTypeDelayed,
                TimingDelay = Convert.ToInt32(actionNTuple.Cost / GameAIMap.GameAIConstants.defaultTravelSpeed),
                TotalDelay = Convert.ToInt32(actionNTuple.Cost / GameAIMap.GameAIConstants.defaultTravelSpeed),
                Data = changeAmount,
                Origin = actionNTuple.Origin,
                Target = actionNTuple.Target,
                PlayerId = playerID
            });
            orders.Add(new GameAIOrder
            {
                Type = GameAIOrder.OrderType.OrderTypeGrotsitsChange,
                TimingType = GameAIOrder.OrderTimingType.OrderTimingTypeImmediate,
                TimingDelay = 0,
                TotalDelay = 0,
                Data =  changeAmount * -1.0f,
                Origin = actionNTuple.Origin,
                Target = actionNTuple.Origin,
                PlayerId = playerID

            });

        }
    }

    private void ProcessFoodShortage(List<Planet.PlanetUpdateResult> results, int playerID, List<GameAIOrder> orders)
    {
        var shortageResults = results.FindAll(x =>
            x.Result == Planet.PlanetUpdateResult.PlanetUpdateResultType.PlanetUpdateResultTypeFoodShortage);
        if(shortageResults.Count <= 0)
            return;

        var surplusResults = results.FindAll(x =>
            x.Result == Planet.PlanetUpdateResult.PlanetUpdateResultType.PlanetUpdateResultTypeFoodSurplus);
        if (surplusResults.Count <= 0)
            return;
        
        var scoreMatrix = new ScoreMatrix();
        foreach (var surplusProducer in surplusResults)
        {
            var validMatrixEntries = new ScoreMatrixElementList();
            var surplusPathMap = GetPlanet(surplusProducer.Name).DistanceMapToPathingList;
            foreach (var shortageResult in shortageResults)
            {
                if (surplusPathMap[shortageResult.Name].NumNodes <=
                    GameAIMap.GameAIConstants.maxPathNodesForResourceDistribution)
                    validMatrixEntries.Add(new ScoreMatrix.ScoreMatrixElement {
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
        
        ShipFood(scoreMatrix, playerID, orders, surplusResults);
    }

    private void ShipFood(ScoreMatrix scoreMatrix, int playerID, List<GameAIOrder> orders, List<Planet.PlanetUpdateResult> surplusResults)
    {
        GenerateActionList(scoreMatrix, out var actionList);

        foreach (var actionNTuple in actionList)
        {
            float changeAmount =Convert.ToSingle(surplusResults.Find(x => x.Name == actionNTuple.Item1).Data);
            orders.Add(new GameAIOrder
            {
                Type = GameAIOrder.OrderType.OrderTypeFoodTransport,
                TimingType = GameAIOrder.OrderTimingType.OrderTimingTypeDelayed,
                TimingDelay = Convert.ToInt32(actionNTuple.Item3 / GameAIMap.GameAIConstants.defaultTravelSpeed),
                TotalDelay = Convert.ToInt32(actionNTuple.Item3 / GameAIMap.GameAIConstants.defaultTravelSpeed),
                Data = changeAmount,
                Origin = actionNTuple.Item1,
                Target = actionNTuple.Item2,
                PlayerId = playerID
            });
            orders.Add(new GameAIOrder
            {
                Type = GameAIOrder.OrderType.OrderTypeFoodChange,
                TimingType = GameAIOrder.OrderTimingType.OrderTimingTypeImmediate,
                TimingDelay = 0,
                TotalDelay = 0,
                Data = changeAmount * -1.0f,
                Origin = actionNTuple.Item1,
                Target = actionNTuple.Item1,
                PlayerId = playerID
            });

        }
    }

    void ProcessColonizers(List<Planet.PlanetUpdateResult> results, int PlayerID, List<GameAIOrder> orders)
    {
        // find every gain pop result where the total pop is over the colonization trigger
        var possibleColonizers = results.FindAll(x =>
            (x.Result is Planet.PlanetUpdateResult.PlanetUpdateResultType.PlanetUpdateResultTypePopulationGain 
                or Planet.PlanetUpdateResult.PlanetUpdateResultType.PlanetUpdateResultTypePopulationMax) 
            && GetPlanet(x.Name).Population >= GetPlanet(x.Name).MaxPopulation * GameAIMap.GameAIConstants.expandPopulationTrigger);
        if (possibleColonizers.Count <= 0)
            return;
        // find all planets with no population
        var possibleColonizerTargets = GameAIMap.PlanetList.FindAll(
            x => x.Population == 0 && !x.PopulationTransferInProgress);
        if (possibleColonizerTargets.Count > 0)
        {
            // create a score matrix for each potential colonizer's distance to each possible target
            var scoreMatrix = new ScoreMatrix();

            foreach (var colonizer in possibleColonizers)
            {
                var validMatrixEntries = new ScoreMatrixElementList();
                var colonizerPathMap = GetPlanet(colonizer.Name).DistanceMapToPathingList;
                foreach (var colonizerTarget in possibleColonizerTargets)
                {
                    if (colonizerPathMap[colonizerTarget.PlanetName].NumNodes <=
                        GameAIMap.GameAIConstants.maxPathNodesForResourceDistribution)
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
            SendColonizers(scoreMatrix, orders, PlayerID, possibleColonizers);
        }
    }

    private void SendColonizers(ScoreMatrix scoreMatrix, List<GameAIOrder> orders, int PlayerID, List<Planet.PlanetUpdateResult> possibleColonizers)
    {
        List<(string Origin, string Target, float Cost)> actionList;
        
        GenerateActionList(scoreMatrix, out actionList);
        foreach (var actionNTuple in actionList)
        {
            Int32 changeAmount = Convert.ToInt32(possibleColonizers.Find(x => x.Name == actionNTuple.Item1).Data);
            orders.Add(new GameAIOrder
            {
                Type = GameAIOrder.OrderType.OrderTypePopulationTransport,
                TimingType = GameAIOrder.OrderTimingType.OrderTimingTypeDelayed,
                TimingDelay = Convert.ToInt32(actionNTuple.Item3 / GameAIMap.GameAIConstants.defaultTravelSpeed),
                TotalDelay = Convert.ToInt32(actionNTuple.Item3 / GameAIMap.GameAIConstants.defaultTravelSpeed),
                Data = changeAmount,
                Origin = actionNTuple.Origin,
                Target = actionNTuple.Target,
                PlayerId = PlayerID
            });
            orders.Add(new GameAIOrder
            {
                Type = GameAIOrder.OrderType.OrderTypePopulationChange,
                TimingType = GameAIOrder.OrderTimingType.OrderTimingTypeImmediate,
                TimingDelay = 0,
                TotalDelay = 0,
                Data = changeAmount * -1.0f,
                Origin = actionNTuple.Origin,
                Target = actionNTuple.Origin,
                PlayerId = PlayerID
            });

            orders.Add(new GameAIOrder
            {
                Type = GameAIOrder.OrderType.OrderTypePopulationTransferInProgress,
                TimingType = GameAIOrder.OrderTimingType.OrderTimingTypeImmediate,
                TimingDelay = 0,
                TotalDelay = 0,
                Data = changeAmount,
                Origin = actionNTuple.Origin,
                Target = actionNTuple.Target

            });
        }
    }

    public void SetSimulationStats(SaveLoadSystem.GameSave gameSave)
    {
        foreach (var orderStatus in gameSave.orders)
        {
            CurrentAIOrders.Add(new GameAIOrder
            {
                Type = orderStatus.type,
                TimingType = orderStatus.timingType,
                TimingDelay = orderStatus.timingDelay,
                TotalDelay = orderStatus.totalDelay,
                Data = (orderStatus.dataType == "float" ? (float)orderStatus.data : (int)orderStatus.data),
                Origin = orderStatus.origin,
                Target = orderStatus.target,
                PlayerId = orderStatus.playerId
            });            
        }
        
        GameAIMap.SetPlanetSimulationStats(gameSave);
    }

    public Planet GetPlanet(string planetName)
    {
        return GameAIMap.GetPlanet(planetName);
    }

    public Planet GetPlayerCapitol(int playerID)
    {
        return GameAIMap.GetPlayerCapitol(playerID);
    }

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        
    }

    // Update is called once per frame
    void Update()
    {
        
    }
}
