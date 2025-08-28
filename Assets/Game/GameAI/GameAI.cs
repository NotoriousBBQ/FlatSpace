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
    public void InitGameAI(List<Planet> planetList)
    {
        _gameAIMap = this.AddComponent<GameAIMap>() as GameAIMap;
        _gameAIMap.GameAIMapInit(planetList);
    }

    public void GameAIUpdate(List<Planet> planetList)
    {
        List<GameAIOrder> gameAIOrders;
        List<Planet.PlanetUpdateResult> planetUpdateResults;
        ProcessCurrentOrders(planetList);
        PlanetaryProductionUpdate(planetList, out planetUpdateResults);
        ProcessResults(planetUpdateResults, planetList, out gameAIOrders);
        ProcessNewOrders(gameAIOrders);
    }

    private void ProcessCurrentOrders(List<Planet> planetList)
    {
        foreach (var gameAIOrder in _currentAIOrders)
        {
            gameAIOrder.TimingDelay--;
        }
        
        var executableOrders = _currentAIOrders.FindAll(x => x.TimingDelay <= 0);
        foreach (var executableOrder in executableOrders)
        {
            ExecuteOrder(executableOrder, planetList);
        }
        _currentAIOrders.RemoveAll(x => x.TimingDelay <= 0);
    }

    private void ExecuteOrder(GameAIOrder executableOrder, List<Planet> planetList)
    {
        // onmly population surplus currently active
        if (executableOrder.Type == GameAIOrder.OrderType.OrderTypePopulationTransport)
        {
            var targetPlanet = planetList.Find(x=>x.PlanetName == executableOrder.Target);
            var numTransported = (int)executableOrder.Data;
            targetPlanet.Population += numTransported;
        }
    }
    private void ProcessNewOrders(List<GameAIOrder> newOrders)
    {
        _currentAIOrders.AddRange(newOrders.FindAll(x => x.TimingType == GameAIOrder.OrderTimingType.OrderTimingTypeDelayed));
    }
    private static void PlanetaryProductionUpdate(List<Planet> planetList, out List<Planet.PlanetUpdateResult> planetUpdateResults)
    {
        planetUpdateResults = new List<Planet.PlanetUpdateResult>();
        foreach (var planet in planetList)
        {
            planet.PlanetProductionUpdate(planetUpdateResults);
        }
    }


    private static void ProcessResults(List<Planet.PlanetUpdateResult> results, List<Planet> planetList, out List<GameAIOrder> orders)
    {
        orders = new List<GameAIOrder>();
        var surplusUpdateResults = 
            results.FindAll(x => x.Result == Planet.PlanetUpdateResult.PlanetUpdateResultType.PlanetUpdateResultTypePopulationSurplus);
        ProcessPopulationSurplus(surplusUpdateResults, planetList, orders);
    }

    private static void ProcessPopulationSurplus(List<Planet.PlanetUpdateResult> results, List<Planet> planetList, List<GameAIOrder> orders)
    {

        foreach (var result in results)
        {
            var sourceNode = PathingSystem.Instance.PathNodes[result.Name];
            
            List<(string Name, float Score)> scoreMatrix = new List<(string, float)>();
            foreach (var connection in sourceNode.Connections)
            {
                (string Name, float Score) scoreTuple = (connection.NodeName, connection.Cost);
                var possibleDestination = planetList.Find(x => x.PlanetName == connection.NodeName);
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

    private static void ProcessOrders(List<GameAIOrder> orders)
    {
        
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
