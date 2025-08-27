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
    
    private struct DestinationToPathingListEntry
    {
        public float Cost;
        public int PathingIndex;
        public bool PathReversed;
    }
    private struct GameAIPlanetNode
    {
        public Dictionary<string, DestinationToPathingListEntry> DistanceMapToPathingList;
    }
    
    private List<GameAIPlanetPathing> _planetPathings;
    private Dictionary<string, GameAIPlanetNode> _planetAINodes;

    public void GameAIMapInit(List<Planet> planetList)
    {
        // painfully ineffeceint process here
       _planetPathings = new List<GameAIPlanetPathing>();
       _planetAINodes = new Dictionary<string,GameAIPlanetNode>();
       
       //first create ai nodes for each planet
       for (var i = 0; i < planetList.Count; i++)
       {
           _planetAINodes[planetList[i].PlanetName] = new GameAIPlanetNode{
           DistanceMapToPathingList = new Dictionary<string, DestinationToPathingListEntry>() 
           };
           
       }
       for (var i = 0; i < planetList.Count - 1; i++)
       {
           var planet1 = planetList[i];
           for (var j = i + 1; j < planetList.Count; j++)
           {
               var planet2 = planetList[j];
               var planetPathing = new GameAIPlanetPathing{
                   Planet1Name=planet1.PlanetName, 
                   Planet2Name=planet2.PlanetName
               };

               PathingSystem.Instance.FindPath(planet1.PlanetName, planet2.PlanetName, out planetPathing.Path1To2);
               
               // must be done before adding planetPathing to list
               AddPathingToAINode(planetPathing, _planetPathings.Count);
               _planetPathings.Add(planetPathing);
           }
       }
    }

    private void AddPathingToAINode(GameAIPlanetPathing planetPathing, int pathIndex)
    {
        _planetAINodes[planetPathing.Planet1Name].DistanceMapToPathingList[planetPathing.Planet2Name]
            = new DestinationToPathingListEntry
            {
                PathingIndex = pathIndex,
                Cost = planetPathing.Path1To2.Cost,
                PathReversed = false
            };
        
        _planetAINodes[planetPathing.Planet2Name].DistanceMapToPathingList[planetPathing.Planet1Name]
            = new DestinationToPathingListEntry
            {
                PathingIndex = pathIndex,
                Cost = planetPathing.Path1To2.Cost,
                PathReversed = true
            };
    }
}
