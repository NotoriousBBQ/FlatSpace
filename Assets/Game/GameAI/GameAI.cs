using System;
using System.Collections.Generic;
using System.Linq;
using Unity.VisualScripting;
using UnityEngine;
using ScoreMatricElementList = System.Collections.Generic.List<(string Target, float Distance, float Surplus)>;
using ScoreMatrix = System.Collections.Generic.Dictionary<string, System.Collections.Generic.List<(string Target, float Distance, float Surplus)>>;

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
            OrderTypeColonizationInProgress,
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
    }

    public enum AIStrategy
    {
        AIStrategyExpand,
        AIStrategyConsolidate,
        AIStrategyAmass
    }
    public GameAIMap GameAIMap {get; private set;}
    public List<GameAIOrder> CurrentAIOrders { get; private set; }= new List<GameAIOrder>();
    
    public AIStrategy Strategy { get; set; } = AIStrategy.AIStrategyExpand;
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
        List<GameAIOrder> gameAIOrders;
        List<Planet.PlanetUpdateResult> planetUpdateResults;
        ProcessCurrentOrders();
        PlanetaryProductionUpdate(out planetUpdateResults);
        ProcessResults(planetUpdateResults, out gameAIOrders);
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
                targetPlanet.ColonizationInProgress = false;
                break;
            case GameAIOrder.OrderType.OrderTypePopulationChange:
                targetPlanet.Population += Convert.ToInt32(executableOrder.Data);
                break;
            case GameAIOrder.OrderType.OrderTypeColonizationInProgress:
                targetPlanet.ColonizationInProgress = true;
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
    private void PlanetaryProductionUpdate(out List<Planet.PlanetUpdateResult> planetUpdateResults)
    {
        planetUpdateResults = new List<Planet.PlanetUpdateResult>();
        GameAIMap.PlanetaryProductionUpdate(out planetUpdateResults);
    }


    private void ProcessResults(List<Planet.PlanetUpdateResult> results, out List<GameAIOrder> orders)
    {
        orders = new List<GameAIOrder>();

        switch (Strategy)
        {
            case AIStrategy.AIStrategyExpand:
                ProcessResultsStrategyExpand(results, orders);
                break;
            case AIStrategy.AIStrategyConsolidate:
                break;
            case AIStrategy.AIStrategyAmass:
                break;
        }        
    }

    private void ProcessResultsStrategyExpand(List<Planet.PlanetUpdateResult> results, List<GameAIOrder> orders)
    {
        ProcessColonizers(results, orders);
        ProcessFoodShortage(results, orders);
        ProcessGrotsitsShortage(results, orders);
 
    }

    private static int CompareScoreNTuples((string Target, float Distance, float Surplus) x, (string Target, float Distance, float Surplus) y)
    {
        var xScore = x.Distance - x.Surplus;
        var yScore = y.Distance - y.Surplus;
        return xScore.CompareTo(yScore);
    }

    private static void GenerateActionList(ScoreMatrix scoreMatrix, out List<(string, string, float)> actionList)
    {
        actionList = new List<(string, string, float)>();
        foreach (var nNTupleList in scoreMatrix.Values)
        {
            nNTupleList.Sort(CompareScoreNTuples);
        }
        var minimumDistanceList = new List<(string Target, float Distance, float Surplus)>();
        while (scoreMatrix.Count > 0 && scoreMatrix.Values.First().Count > 0)
        {

            foreach (var key in scoreMatrix.Keys)
            {
                if (scoreMatrix[key].Count <= 0)
                    continue;
                minimumDistanceList.Add((key, scoreMatrix[key][0].Item2, scoreMatrix[key][0].Item3));
            }

            minimumDistanceList.Sort(CompareScoreNTuples);
            string closestTargetName =  scoreMatrix[minimumDistanceList[0].Item1][0].Item1;
            actionList.Add(( minimumDistanceList[0].Item1, closestTargetName, minimumDistanceList[0].Item2));

            scoreMatrix.Remove(minimumDistanceList[0].Item1);
            foreach (var nNTupleList in scoreMatrix.Values)
            {
                nNTupleList.RemoveAll(x => x.Item1 == closestTargetName);
            }
            
            minimumDistanceList.Clear();
        }
    }
    
    private void ProcessGrotsitsShortage(List<Planet.PlanetUpdateResult> results, List<GameAIOrder> orders)
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
            var validMatrixEntries = new ScoreMatricElementList();
            var surplusPathMap = GetPlanet(surplusProducer.Name).DistanceMapToPathingList;
            foreach (var shortageResult in shortageResults)
            {
                if(surplusPathMap[shortageResult.Name].NumNodes <= GameAIMap.GameAIConstants.maxPathNodesForResourceDistribution)
                    validMatrixEntries.Add((shortageResult.Name, surplusPathMap[shortageResult.Name].Cost, (float)surplusProducer.Data));
            }
            if (validMatrixEntries.Count <= 0)
                continue;
            scoreMatrix.Add(surplusProducer.Name,validMatrixEntries);
        }
        
        ShipGrotsits(scoreMatrix, orders, surplusResults);
    }

    private void ShipGrotsits(ScoreMatrix scoreMatrix, List<GameAIOrder> orders, List<Planet.PlanetUpdateResult> surplusResults)
    {
        List<(string, string, float)> actionList;
        
        GenerateActionList(scoreMatrix, out actionList);

        foreach (var actionNTuple in actionList)
        {
            float changeAmount = Convert.ToSingle(surplusResults.Find(x => x.Name == actionNTuple.Item1).Data);
            orders.Add(new GameAIOrder
            {
                Type = GameAIOrder.OrderType.OrderTypeGrotsitsTransport,
                TimingType = GameAIOrder.OrderTimingType.OrderTimingTypeDelayed,
                TimingDelay = Convert.ToInt32(actionNTuple.Item3 / GameAIMap.GameAIConstants.defaultTravelSpeed),
                TotalDelay = Convert.ToInt32(actionNTuple.Item3 / GameAIMap.GameAIConstants.defaultTravelSpeed),
                Data = changeAmount,
                Origin = actionNTuple.Item1,
                Target = actionNTuple.Item2,
            });
            orders.Add(new GameAIOrder
            {
                Type = GameAIOrder.OrderType.OrderTypeGrotsitsChange,
                TimingType = GameAIOrder.OrderTimingType.OrderTimingTypeImmediate,
                TimingDelay = 0,
                TotalDelay = 0,
                Data =  changeAmount * -1.0f,
                Origin = actionNTuple.Item1,
                Target = actionNTuple.Item1

            });

        }
    }

    private void ProcessFoodShortage(List<Planet.PlanetUpdateResult> results, List<GameAIOrder> orders)
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
            var validMatrixEntries = new ScoreMatricElementList();
            var surplusPathMap = GetPlanet(surplusProducer.Name).DistanceMapToPathingList;
            foreach (var shortageResult in shortageResults)
            {
                if(surplusPathMap[shortageResult.Name].NumNodes <= GameAIMap.GameAIConstants.maxPathNodesForResourceDistribution)
                    validMatrixEntries.Add((shortageResult.Name, surplusPathMap[shortageResult.Name].Cost,  (float)surplusProducer.Data));
            }

            if (validMatrixEntries.Count <= 0)
                continue;
            scoreMatrix.Add(surplusProducer.Name, validMatrixEntries);
        }
        
        ShipFood(scoreMatrix, orders, surplusResults);
    }

    private void ShipFood(ScoreMatrix scoreMatrix, List<GameAIOrder> orders, List<Planet.PlanetUpdateResult> surplusResults)
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
            });
            orders.Add(new GameAIOrder
            {
                Type = GameAIOrder.OrderType.OrderTypeFoodChange,
                TimingType = GameAIOrder.OrderTimingType.OrderTimingTypeImmediate,
                TimingDelay = 0,
                TotalDelay = 0,
                Data = changeAmount * -1.0f,
                Origin = actionNTuple.Item1,
                Target = actionNTuple.Item1

            });

        }
    }

    void ProcessColonizers(List<Planet.PlanetUpdateResult> results, List<GameAIOrder> orders)
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
            x => x.Population == 0 && !x.ColonizationInProgress);
        if (possibleColonizerTargets.Count > 0)
        {
            // create a score matrix for each potential colonizer's distance to each possible target
            var scoreMatrix = new ScoreMatrix();

            foreach (var colonizer in possibleColonizers)
            {
                var validMatrixEntries = new ScoreMatricElementList();
                var colonizerPathMap = GetPlanet(colonizer.Name).DistanceMapToPathingList;
                foreach (var colonizerTarget in possibleColonizerTargets)
                {
                    if(colonizerPathMap[colonizerTarget.PlanetName].NumNodes <= GameAIMap.GameAIConstants.maxPathNodesForResourceDistribution)
                        validMatrixEntries.Add((colonizerTarget.PlanetName, colonizerPathMap[colonizerTarget.PlanetName].Cost, 1.0f));
                }

                if (validMatrixEntries.Count <= 0)
                    continue;
                scoreMatrix.Add(colonizer.Name, validMatrixEntries);
            }
            SendColonizers(scoreMatrix, orders, possibleColonizers);
        }
    }

    private void SendColonizers(ScoreMatrix scoreMatrix, List<GameAIOrder> orders, List<Planet.PlanetUpdateResult> possibleColonizers)
    {
        List<(string, string, float)> actionList;
        
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
                Origin = actionNTuple.Item1,
                Target = actionNTuple.Item2,
            });
            orders.Add(new GameAIOrder
            {
                Type = GameAIOrder.OrderType.OrderTypePopulationChange,
                TimingType = GameAIOrder.OrderTimingType.OrderTimingTypeImmediate,
                TimingDelay = 0,
                TotalDelay = 0,
                Data = changeAmount * -1.0f,
                Origin = actionNTuple.Item1,
                Target = actionNTuple.Item1

            });

            orders.Add(new GameAIOrder
            {
                Type = GameAIOrder.OrderType.OrderTypeColonizationInProgress,
                TimingType = GameAIOrder.OrderTimingType.OrderTimingTypeImmediate,
                TimingDelay = 0,
                TotalDelay = 0,
                Data = changeAmount,
                Origin = actionNTuple.Item1,
                Target = actionNTuple.Item2

            });
        }
    }

    public void SetSimulationStats(SaveLoadSystem.GameSave gameSave)
    {
        Strategy = gameSave.strategy;
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
            });            
        }
        
        GameAIMap.SetPlanetSimulationStats(gameSave);
    }

    public Planet GetPlanet(string planetName)
    {
        return GameAIMap.GetPlanet(planetName);
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
