using System;
using System.Collections.Generic;
using FlatSpace.Game;
using FlatSpace.Pathing;
using Unity.VisualScripting;
using Unity.VisualScripting.Dependencies.Sqlite;
using UnityEngine;

public class GameAI : MonoBehaviour
{
    public class GameAIOrder
    {
        public enum OrderType
        {
            OrderTypeNone,
            OrderTypePopulationTransport,
            OrderTypePopulationAssign,
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
    
    private GameAIMap _gameAIMap;
    private List<GameAIOrder> _currentAIOrders = new List<GameAIOrder>();
    public List<GameAIOrder> CurrentAIOrders => _currentAIOrders;
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
        foreach (var gameAIOrder in _currentAIOrders)
        {
            gameAIOrder.TimingDelay--;
        }
        
        var executableOrders = _currentAIOrders.FindAll(x => x.TimingDelay <= 0);
        foreach (var executableOrder in executableOrders)
        {
            ExecuteOrder(executableOrder);
        }
        _currentAIOrders.RemoveAll(x => x.TimingDelay <= 0);
    }

    private void ExecuteOrder(GameAIOrder executableOrder)
    {
        // onmly population surplus currently active
        if (executableOrder.Type == GameAIOrder.OrderType.OrderTypePopulationTransport)
        {
            var targetPlanet = _gameAIMap.GetPlanet(executableOrder.Target);
            var numTransported = (int)executableOrder.Data;
            targetPlanet.Population += numTransported;
        }
    }
    private void ProcessNewOrders(List<GameAIOrder> newOrders)
    {
        _currentAIOrders.AddRange(newOrders.FindAll(x => x.TimingType == GameAIOrder.OrderTimingType.OrderTimingTypeDelayed));
    }
    private void PlanetaryProductionUpdate(out List<Planet.PlanetUpdateResult> planetUpdateResults)
    {
        planetUpdateResults = new List<Planet.PlanetUpdateResult>();
        _gameAIMap.PlanetaryProductionUpdate(out planetUpdateResults);
    }


    private void ProcessResults(List<Planet.PlanetUpdateResult> results, out List<GameAIOrder> orders)
    {
        orders = new List<GameAIOrder>();
        var surplusUpdateResults = 
            results.FindAll(x => x.Result == Planet.PlanetUpdateResult.PlanetUpdateResultType.PlanetUpdateResultTypePopulationSurplus);
        ProcessPopulationSurplus(surplusUpdateResults, orders);
    }

    private void ProcessPopulationSurplus(List<Planet.PlanetUpdateResult> results, List<GameAIOrder> orders)
    {

        foreach (var result in results)
        {
            var sourceNode = PathingSystem.Instance.PathNodes[result.Name];
            
            List<(string Name, float Score)> scoreMatrix = new List<(string, float)>();
            foreach (var connection in sourceNode.Connections)
            {
                (string Name, float Score) scoreTuple = (connection.NodeName, connection.Cost);
                var possibleDestination = GetPlanet(connection.NodeName);
                if(possibleDestination.Population >= possibleDestination.MaxPopulation)
                {
                    scoreTuple.Score = Single.MaxValue;
                }
                else
                {
                    scoreTuple.Score += possibleDestination.Population * 100.0f;
                }
                scoreMatrix.Add(scoreTuple);
            }
            
            scoreMatrix.Sort((a, b) => a.Score.CompareTo(b.Score));
            var chosenTarget = scoreMatrix[0].Name;
            orders.Add(new GameAIOrder
            {
                Type = GameAIOrder.OrderType.OrderTypePopulationTransport,
                Origin = result.Name,
                Target = chosenTarget,
                TimingType = GameAIOrder.OrderTimingType.OrderTimingTypeDelayed,
                TimingDelay = 1,
                Data = result.Data,
            });
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
