using System.Collections.Generic;
using FlatSpace.Pathing;
using UnityEngine;

public class GameAIMap : MonoBehaviour
{
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    private struct GameAIPlanetPathing
    {
        public string Planet1Name;
        public string Planet2Name;
        public Path Path1To2;
    }

    private struct GameAIPlanetNode
    {
        public string PlanetName;

        public GameAIPlanetNode(string planetName)
        {
            PlanetName = planetName;
        }
    }

    private List<GameAIPlanetPathing> _planetPathings;
    private List<GameAIPlanetNode> _planetAINodes;
    public void GameAIMapInit(List<Planet> planetList)
    {
        // painfully ineffeceint process here
       _planetPathings = new List<GameAIPlanetPathing>();
       _planetAINodes = new List<GameAIPlanetNode>();
       for (var i = 0; i < planetList.Count - 1; i++)
       {
           var planet1 = planetList[i];
           _planetAINodes.Add(new GameAIPlanetNode(planet1.PlanetName));
           for (var j = i + 1; j < planetList.Count; j++)
           {
               var planet2 = planetList[j];
               var planetPathing = new GameAIPlanetPathing{
                   Planet1Name=planet1.PlanetName, 
                   Planet2Name=planet2.PlanetName
               };

               PathingSystem.Instance.FindPath(planet1.PlanetName, planet2.PlanetName, out planetPathing.Path1To2);
               _planetPathings.Add(planetPathing);
           }
       }
       _planetAINodes.Add(new GameAIPlanetNode(planetList[^1].PlanetName));
    }
}
