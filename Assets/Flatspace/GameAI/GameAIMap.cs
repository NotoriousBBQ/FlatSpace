using System.Collections.Generic;
using System.Linq;
using FlatSpace.Game;
using FlatSpace.Pathing;
using Unity.VisualScripting;
using UnityEngine;

namespace FlatSpace
{
    namespace AI
    {
        public class GameAIMap : MonoBehaviour
        {
            // Start is called once before the first execution of UpdatePlanet after the MonoBehaviour is created
            private struct GameAIPlanetPathing
            {
                public string Planet1Name;
                public string Planet2Name;
                public Path Path1To2;
            }

            public struct DestinationToPathingListEntry
            {
                public float Cost;
                public int NumNodes;
                public int PathingIndex;
                public bool PathReversed;
            }

            private List<GameAIPlanetPathing> _planetPathings;

            private Dictionary<string, Planet> _planets;
            public GameAIConstants GameAIConstants { get; private set; }

            public List<Planet> PlanetList
            {
                get { return _planets.Values.ToList(); }
            }

            public Vector2 PlanetAILocation(string planetName)
            {
                return _planets[planetName].Position;
            }

            public void ClearGameAIMap()
            {
                _planets.Clear();
                _planetPathings.Clear();
            }

            public void GameAIMapInit(List<PlanetSpawnData> spawnDataList, GameAIConstants gameAIConstants)
            {
                GameAIConstants = gameAIConstants;
                _planets = new Dictionary<string, Planet>();

                foreach (var planetSpawnData in spawnDataList)
                {
                    var planet = this.AddComponent<Planet>() as Planet;
                    planet.Init(planetSpawnData, this.transform, GameAIConstants);
                    _planets[planetSpawnData._planetName] = planet;
                }

                PathingSystem.Instance.InitializePathMap(PlanetList);

                // painfully ineffeceint process here
                _planetPathings = new List<GameAIPlanetPathing>();

                for (var i = 0; i < PlanetList.Count - 1; i++)
                {
                    var planet1 = PlanetList[i];
                    for (var j = i + 1; j < PlanetList.Count; j++)
                    {
                        var planet2 = PlanetList[j];
                        var planetPathing = new GameAIPlanetPathing
                        {
                            Planet1Name = planet1.PlanetName,
                            Planet2Name = planet2.PlanetName
                        };

                        PathingSystem.Instance.FindPath(planet1.PlanetName, planet2.PlanetName,
                            out planetPathing.Path1To2);

                        // must be done before adding planetPathing to list
                        AddPathingToPlanet(planetPathing, _planetPathings.Count);
                        _planetPathings.Add(planetPathing);
                    }
                }

                SetInitialOwnership();
            }

            private void SetInitialOwnership()
            {
                var playerID = 0;
                foreach (var planet in PlanetList.FindAll(x => x.Type == Planet.PlanetType.PlanetTypePrime))
                {

                    planet.Owner = playerID;
                    for (var i = 0; i < planet.Population.Count; i++)
                    {
                        var inhabitant = planet.Population[i];
                        inhabitant.Player = playerID;
                        planet.Population[i] = inhabitant;
                    }

                    playerID++;
                }
            }

            private void AddPathingToPlanet(GameAIPlanetPathing planetPathing, int pathIndex)
            {

                _planets[planetPathing.Planet1Name].DistanceMapToPathingList[planetPathing.Planet2Name]
                    = new GameAIMap.DestinationToPathingListEntry
                    {
                        PathingIndex = pathIndex,
                        Cost = planetPathing.Path1To2.Cost,
                        PathReversed = false,
                        NumNodes = planetPathing.Path1To2.NumNodes
                    };

                _planets[planetPathing.Planet2Name].DistanceMapToPathingList[planetPathing.Planet1Name]
                    = new GameAIMap.DestinationToPathingListEntry
                    {
                        PathingIndex = pathIndex,
                        Cost = planetPathing.Path1To2.Cost,
                        PathReversed = true,
                        NumNodes = planetPathing.Path1To2.NumNodes
                    };
            }

            public void UpdateAllPlanets(List<Planet.PlanetUpdateResult> resultList)
            {
                foreach (var planet in PlanetList)
                {
                    planet.UpdatePlanet(resultList);
                }
            }

            public Planet GetPlanet(string planetName)
            {
                return _planets[planetName];
            }

            public Planet GetPlayerCapitol(int playerID)
            {
                return PlanetList.Find(x => x.Owner == playerID && x.Type == Planet.PlanetType.PlanetTypePrime);
            }

            public void SetPlanetSimulationStats(SaveLoadSystem.GameSave gameSave)
            {
                foreach (var planetStatus in gameSave.planetStatuses)
                {
                    var planet = _planets[planetStatus.name];
                    planet.CurrentStrategy = planetStatus.planetStrategy;
                    planet.Food = planetStatus.food;
                    planet.Morale = planetStatus.morale;
                    planet.Grotsits = planetStatus.grotsits;
                    planet.Owner = planetStatus.owner;
                    planet.FoodShipmentIncoming = planetStatus.foodTransferInProgress;
                    planet.GrotsitsShipmentIncoming = planetStatus.grotsitsTransferInProgress;
                    foreach(var playerID in planetStatus.populationTransferInProgress)
                        planet.SetPopulationTransferInProgress(playerID);
                    for (var i = 0; i < planetStatus.population.Length; i++)
                    {
                        if (planetStatus.population[i] <= 0)
                            continue;
                        for (var j = 0; j < planetStatus.population[i]; j++)
                            planet.Population.Add(new Planet.Inhabitant { Player = i });
                    }
                }
            }

            private void DEBUG_LogResults(List<Planet.PlanetUpdateResult> resultList)
            {
                Debug.Log($"Turn: {Gameboard.Instance.TurnNumber} Results count: {resultList.Count}");
                foreach (var result in resultList)
                {
                    Debug.Log($"{result.Name}: {result.Result.ToString()} {result.Data?.ToString()}");
                }
            }
        }
    }
}