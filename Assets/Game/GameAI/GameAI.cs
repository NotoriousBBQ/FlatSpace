using System;
using System.Collections.Generic;
using FlatSpace.Pathing;
using Unity.VisualScripting;
using UnityEngine;

public class GameAI : MonoBehaviour
{
    public class GameAIOrder
    {
        
    }
    
    private GameAIMap _gameAIMap;
    public void InitGameAI(List<Planet> planetList)
    {
        _gameAIMap = this.AddComponent<GameAIMap>() as GameAIMap;
        _gameAIMap.GameAIMapInit(planetList);
    }

    public void GameAIUpdate(List<Planet> planetList)
    {
        List<GameAIOrder> gameAIOrders;
        List<Planet.PlanetUpdateResult> planetUpdateResults;
        ProcessCurrentOrders();
        PlanetaryProductionUpdate(planetList, out planetUpdateResults);
        ProcessResults(planetUpdateResults, planetList, out gameAIOrders);
        ProcessOrders(gameAIOrders);
    }

    private void ProcessCurrentOrders()
    {
        
    }
    
    private void PlanetaryProductionUpdate(List<Planet> planetList, out List<Planet.PlanetUpdateResult> planetUpdateResults)
    {
        planetUpdateResults = new List<Planet.PlanetUpdateResult>();
        foreach (Planet planet in planetList)
        {
            planet.PlanetProductionUpdate(planetUpdateResults);
        }
    }


    private void ProcessResults(List<Planet.PlanetUpdateResult> results, List<Planet> planetList, out List<GameAIOrder> orders)
    {
        orders = new List<GameAIOrder>();
        List<Planet.PlanetUpdateResult> SurplusUpdateResults = 
            results.FindAll(x => x._resultType == Planet.PlanetUpdateResult.PlanetUpdateResultType.PlanetUpdateResultTypePopulationSurplus);
        ProcessPopulationSurplus(SurplusUpdateResults, planetList, orders);
    }

    private void ProcessPopulationSurplus(List<Planet.PlanetUpdateResult> results, List<Planet> planetList, List<GameAIOrder> orders)
    {

        foreach (var result in results)
        {
            var sourceNode = PathingSystem.Instance.PathNodes[result._name];
            
            List<(string Name, float Score)> scoreMatrix = new List<(string, float)>();
            foreach (var connection in sourceNode.Connections)
            {
                (string Name, float Score) scoreTuple = (connection.NodeName, connection.Cost);
                var possibleDestination = planetList.Find(x => x.PlanetName == connection.NodeName);
                if(possibleDestination.Population >= possibleDestination.MaxPopulation)
                {
                    scoreTuple.Score = 0.0f;
                }
                else
                {
                    scoreTuple.Score += possibleDestination.Population * 100.0f;
                }
                scoreMatrix.Add(scoreTuple);
            }
            
            scoreMatrix.Sort((a, b) => a.Item2.CompareTo(b.Item2));
            var chosenTarget = scoreMatrix[0].Name;
        }
    }

    private void ProcessOrders(List<GameAIOrder> orders)
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
