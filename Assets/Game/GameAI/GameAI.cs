using System;
using System.Collections.Generic;
using System.Linq;
using Unity.VisualScripting;
using UnityEngine;
using ScoreMatrix = System.Collections.Generic.Dictionary<string, System.Collections.Generic.List<(string, float)>>;

public class GameAI : MonoBehaviour
{
    public class GameAIOrder
    {
        public enum OrderType
        {
            OrderTypeNone,
            OrderTypePopulationTransport,
            OrderTypePopulationChange,
            OrderTypeColonizationInProgress
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
    private GameAIMap _gameAIMap;
    public List<GameAIOrder> CurrentAIOrders { get; private set; }= new List<GameAIOrder>();
    
    private AIStrategy _aiStrategy = AIStrategy.AIStrategyExpand;
    public void InitGameAI(List<PlanetSpawnData> spawnDataList, GameAIConstants gameAIConstants)
    {
        _gameAIMap = this.AddComponent<GameAIMap>() as GameAIMap;
        _gameAIMap.GameAIMapInit(spawnDataList, gameAIConstants);
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
        var targetPlanet = _gameAIMap.GetPlanet(executableOrder.Target);
        switch (executableOrder.Type)
        {
            case GameAIOrder.OrderType.OrderTypePopulationTransport:
                targetPlanet.Population += (int)executableOrder.Data;
                targetPlanet.ColonizationInProgress = false;
                break;
            case GameAIOrder.OrderType.OrderTypePopulationChange:
                targetPlanet.Population += (int)executableOrder.Data;
                break;
            case GameAIOrder.OrderType.OrderTypeColonizationInProgress:
                targetPlanet.ColonizationInProgress = true;
                break;
            default:
                break;
        }
        
        if (executableOrder.Type == GameAIOrder.OrderType.OrderTypePopulationTransport || executableOrder.Type == GameAIOrder.OrderType.OrderTypePopulationChange)
        {
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
        _gameAIMap.PlanetaryProductionUpdate(out planetUpdateResults);
    }


    private void ProcessResults(List<Planet.PlanetUpdateResult> results, out List<GameAIOrder> orders)
    {
        orders = new List<GameAIOrder>();

        switch (_aiStrategy)
        {
            case AIStrategy.AIStrategyExpand:
                ProcessResultsStrategyExpand(results, orders);
                break;
            case AIStrategy.AIStrategyConsolidate:
                break;
            case AIStrategy.AIStrategyAmass:
                break;
        }        
     /*   var surplusUpdateResults = 
            results.FindAll(x => x.Result == Planet.PlanetUpdateResult.PlanetUpdateResultType.PlanetUpdateResultTypePopulationSurplus);
        ProcessPopulationSurplus(surplusUpdateResults, orders);*/
    }

    private void ProcessResultsStrategyExpand(List<Planet.PlanetUpdateResult> results, List<GameAIOrder> orders)
    {
        // find every gain pop result where the total pop is over the colonization trigger
        var possibleColonizers = results.FindAll(x =>
            x.Result == Planet.PlanetUpdateResult.PlanetUpdateResultType.PlanetUpdateResultTypePopulationGain 
            && GetPlanet(x.Name).Population >= GetPlanet(x.Name).MaxPopulation * _gameAIMap.GameAIConstants.ExpandPopultionTrigger);
        if (possibleColonizers.Count > 0)
        {
            // find all planets with no population
            var possibleColonizerTargets = _gameAIMap.PlanetList.FindAll(
                x => x.Population == 0 && !x.ColonizationInProgress);
            if (possibleColonizerTargets.Count > 0)
            {
                // create a score matrix for each potential colonizer's distance to each possible target
                ScoreMatrix scoreMatrix = new ScoreMatrix();

                foreach (var colonizer in possibleColonizers)
                {
                    scoreMatrix.Add(colonizer.Name, new List<(string, float)>());
                    var colonizerPathMap = GetPlanet(colonizer.Name).DistanceMapToPathingList;
                    foreach (var colonizerTarget in possibleColonizerTargets)
                    {
                        scoreMatrix[colonizer.Name].Add((colonizerTarget.PlanetName, colonizerPathMap[colonizerTarget.PlanetName].Cost));
                    }
                }
                
                SendColonizers(scoreMatrix, orders, possibleColonizers);
            }
        }
    }

    private static int CompareScoreTuples((string, float) x, (string , float) y)
    {
        if (x.Item2 == y.Item2)
            return 0;
        return x.Item2.CompareTo(y.Item2);
    }

    private void SendColonizers(ScoreMatrix scoreMatrix, List<GameAIOrder> orders, List<Planet.PlanetUpdateResult> possibleColonizers)
    {
        foreach (var tupleList in scoreMatrix.Values)
        {
            tupleList.Sort(CompareScoreTuples);
        }
        var minimumDistanceList = new List<(string, float)>();
        while (scoreMatrix.Count > 0 && scoreMatrix.Values.First().Count > 0)
        {

            foreach (var key in scoreMatrix.Keys)
                minimumDistanceList.Add((key, scoreMatrix[key][0].Item2));

            minimumDistanceList.Sort(CompareScoreTuples);
            string closestTargetName =  scoreMatrix[minimumDistanceList[0].Item1][0].Item1;
            orders.Add(new GameAIOrder
            {
                Type = GameAIOrder.OrderType.OrderTypePopulationTransport,
                TimingType = GameAIOrder.OrderTimingType.OrderTimingTypeDelayed,
                TimingDelay =
                    Convert.ToInt32(minimumDistanceList[0].Item2 / _gameAIMap.GameAIConstants.DefaultTravelSpeed),
                Data = possibleColonizers.Find(x => x.Name == minimumDistanceList[0].Item1).Data,
                Origin = minimumDistanceList[0].Item1,
                Target = closestTargetName
            });
            orders.Add(new GameAIOrder
            {
                Type = GameAIOrder.OrderType.OrderTypePopulationChange,
                TimingType = GameAIOrder.OrderTimingType.OrderTimingTypeImmediate,
                TimingDelay = 0,
                Data = -1,
                Origin = minimumDistanceList[0].Item1,
                Target = minimumDistanceList[0].Item1

            });

            orders.Add(new GameAIOrder
            {
                Type = GameAIOrder.OrderType.OrderTypeColonizationInProgress,
                TimingType = GameAIOrder.OrderTimingType.OrderTimingTypeImmediate,
                TimingDelay = 0,
                Data = -1,
                Origin = minimumDistanceList[0].Item1,
                Target = closestTargetName

            });
            scoreMatrix.Remove(minimumDistanceList[0].Item1);

            foreach (var tupleList in scoreMatrix.Values)
            {
                tupleList.RemoveAll(x => x.Item1 == closestTargetName);
            }
            
            minimumDistanceList.Clear();
        }

    }
    private void ProcessPopulationSurplus(List<Planet.PlanetUpdateResult> results, List<GameAI.GameAIOrder> orders)
    {

        foreach (var result in results)
        {
            var planet = _gameAIMap.GetPlanet(result.Name);
            
            List<(string Name, float Score)> scoreMatrix = new List<(string, float)>();
            int maxNodes = _gameAIMap.GameAIConstants.MaxPathNodesToSearch;
            
            foreach (var pathMap in planet.DistanceMapToPathingList)
            {
                var possibleDestination = GetPlanet(pathMap.Key);
                if (pathMap.Value.NumNodes <= maxNodes && possibleDestination.Population < possibleDestination.MaxPopulation)
                {
                    (string Name, float Score) scoreTuple = (pathMap.Key, pathMap.Value.Cost * (Convert.ToSingle(possibleDestination.Population + 1) / Convert.ToSingle(possibleDestination.MaxPopulation)));
                    scoreMatrix.Add(scoreTuple);                    
                }

            }

            if (scoreMatrix.Count > 0)
            {
                scoreMatrix.Sort((a, b) => a.Score.CompareTo(b.Score));
                var chosenTarget = scoreMatrix[0].Name;
                orders.Add(new GameAI.GameAIOrder
                {
                    Type = GameAI.GameAIOrder.OrderType.OrderTypePopulationTransport,
                    Origin = result.Name,
                    Target = chosenTarget,
                    TimingType = GameAI.GameAIOrder.OrderTimingType.OrderTimingTypeDelayed,
                    TimingDelay = 1,
                    Data = result.Data,
                });
            }
        }
    }

    public Planet GetPlanet(string planetName)
    {
        return _gameAIMap.GetPlanet(planetName);
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
