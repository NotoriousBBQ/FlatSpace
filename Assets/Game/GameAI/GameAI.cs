using System;
using System.Collections.Generic;
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
        PlanetaryProductionUpdate(planetList, out planetUpdateResults);
        ProcessResults(planetUpdateResults, out gameAIOrders);
        ProcessOrders(gameAIOrders);
    }
    
    private void PlanetaryProductionUpdate(List<Planet> planetList, out List<Planet.PlanetUpdateResult> planetUpdateResults)
    {
        planetUpdateResults = new List<Planet.PlanetUpdateResult>();
        foreach (Planet planet in planetList)
        {
            planet.PlanetProductionUpdate(planetUpdateResults);
        }
    }


    private void ProcessResults(List<Planet.PlanetUpdateResult> results, out List<GameAIOrder> orders)
    {
        orders = new List<GameAIOrder>();
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
