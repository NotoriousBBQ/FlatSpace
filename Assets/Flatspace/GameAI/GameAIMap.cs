using System.Collections.Generic;
using System.Linq;
using FlatSpace.Game;
using Flatspace.Objects.Production;
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
            private PlanetCentrality _centrality;

            private Dictionary<string, Planet> _planets;
            public GameAIConstants GameAIConstants { get; private set; }
            public PlayerKnowledge Knowledge { get; private set; }
            public DiplomacyState Diplomacy { get; private set; }

            public List<Planet> PlanetList
            {
                get { return _planets.Values.ToList(); }
            }

            /// <summary>
            /// Colonized planets across the WHOLE board, every owner — used to gate board-size-dependent AI
            /// features (Distribution Centers) that key off overall board scale, not one player's territory.
            /// </summary>
            public int TotalColonizedPlanetCount() => PlanetList.Count(p => p.Population.Count > 0);

            public Vector2 PlanetAILocation(string planetName)
            {
                return _planets[planetName].Position;
            }

            public Path GetPath(string origin, string destination)
            {
                var entry = _planets[origin].DistanceMapToPathingList[destination];
                var path = _planetPathings[entry.PathingIndex].Path1To2;
                if (!entry.PathReversed) return path;

                var reversed = new Path { Cost = path.Cost, NumNodes = path.NumNodes };
                reversed.PathNodes.AddRange(path.PathNodes);
                reversed.PathNodes.Reverse();
                return reversed;
            }

            /// <summary>
            /// The positions along the path an order flies, for display (order lines, fog corridors): the order's carried
            /// route when it has one (a redirected colonist's route starts at the node it redirected from and differs from
            /// the shortest path, and may end at its own origin), else the shortest path from Origin to Target. Empty when
            /// neither exists (same planet, unknown or unpathed pair): a planet's own name is never in its path table, so
            /// GetPath(origin, target) must not be called for such a pair.
            /// </summary>
            public List<Vector2> OrderPathPoints(GameAI.GameAIOrder order)
            {
                var points = new List<Vector2>();
                if (order.Route != null && order.Route.Count >= 2)
                {
                    foreach (var name in order.Route)
                        if (_planets.TryGetValue(name, out var routePlanet)) points.Add(routePlanet.Position);
                    return points;
                }
                if (order.Origin != order.Target && order.Origin != null && order.Target != null
                    && _planets.TryGetValue(order.Origin, out var origin)
                    && origin.DistanceMapToPathingList.ContainsKey(order.Target))
                    foreach (var node in GetPath(order.Origin, order.Target).PathNodes)
                        points.Add(node.Position);
                return points;
            }

            /// <summary>The connection cost between two adjacent planets; 0 when they are not adjacent or unknown.</summary>
            public float EdgeCost(string a, string b)
            {
                if (!PathingSystem.Instance.PathNodes.TryGetValue(a, out var node)) return 0f;
                foreach (var connection in node.Connections)
                    if (connection.NodeName == b)
                        return connection.Cost;
                return 0f;
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
                Knowledge = new PlayerKnowledge();
                Diplomacy = new DiplomacyState();

                foreach (var planetSpawnData in spawnDataList)
                {
                    var planet = this.AddComponent<Planet>() as Planet;
                    planet.Init(planetSpawnData, this.transform, GameAIConstants);
                    _planets[planetSpawnData._planetName] = planet;
                }

                BuildNeighbours();

                PathingSystem.Instance.InitializePathMap(PlanetList);

                // painfully inefficient process here
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

                // Betweenness from the paths just stored (once per board: the graph never changes during a match).
                _centrality = PlanetCentrality.Compute(
                    PlanetList.Select(p => p.PlanetName),
                    _planetPathings.Select(p => (IReadOnlyList<string>)p.Path1To2.PathNodes.Select(n => n.Name).ToList()));

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

            public void UpdateAllPlanets(List<Planet.UpdateResult> resultList)
            {
                foreach (var planet in PlanetList)
                {
                    planet.UpdatePlanet(resultList);
                }
            }

            public Planet GetPlanet(string planetName)
            {
                Planet planet = null;
                _planets.TryGetValue(planetName, out planet);
                return planet;
            }

            public struct VisionSource
            {
                public Planet Planet;
                public bool HasPopulation;
                public bool HasOwnedShip;
            }

            /// <summary>
            /// Planets that currently give playerId vision: population presence or a docked ship
            /// they own. Shared by PlayerKnowledge (sticky, planet-level knowledge) and
            /// FogOfWarSystem (radius/gradient rendering) so both read the exact same underlying
            /// fact instead of two independently-derived copies of it.
            /// </summary>
            public List<VisionSource> GetVisionSourcePlanets(int playerId)
            {
                var result = new List<VisionSource>();
                foreach (var planet in PlanetList)
                {
                    var hasPopulation = planet.GetPopulationFraction(playerId) > 0f;
                    var hasOwnedShip = planet.DockedShips.Exists(s => s.Owner == playerId);
                    if (hasPopulation || hasOwnedShip)
                        result.Add(new VisionSource
                        {
                            Planet = planet,
                            HasPopulation = hasPopulation,
                            HasOwnedShip = hasOwnedShip
                        });
                }
                return result;
            }

            private static readonly List<string> EmptyNeighbours = new List<string>();
            private Dictionary<string, List<string>> _neighbours;

            /// <summary>
            /// Planet.Connections, symmetrized: a link declared on only one side still appears in
            /// both planets' neighbour lists. Built once in GameAIMapInit.
            /// </summary>
            public IReadOnlyList<string> GetNeighbours(string planetName)
            {
                return _neighbours != null && _neighbours.TryGetValue(planetName, out var list)
                    ? list
                    : EmptyNeighbours;
            }

            /// <summary>Paths through the planet over every stored shortest path (endpoints excluded); 0 when unknown.</summary>
            public int Betweenness(string planetName) => _centrality?.Betweenness(planetName) ?? 0;

            /// <summary>The planet's betweenness percentile, 0..1 (see PlanetCentrality); 0 when unknown.</summary>
            public float Chokepoint(string planetName) => _centrality?.Percentile(planetName) ?? 0f;

            /// <summary>
            /// A chokepoint: on at least one stored path and at or above GameAIConstants.chokepointPercentile.
            /// </summary>
            public bool IsChokepoint(string planetName)
                => Betweenness(planetName) > 0 && Chokepoint(planetName) >= GameAIConstants.chokepointPercentile;

            /// <summary>The count planets with the highest betweenness, for the Chokepoints log line.</summary>
            public IEnumerable<(string name, int betweenness, float percentile)> TopChokepoints(int count)
                => _centrality != null
                    ? _centrality.Top(count)
                    : Enumerable.Empty<(string name, int betweenness, float percentile)>();

            /// <summary>
            /// For one player: the chokepoints it colonizes, the chokepoints on the board, its docked warships on those it
            /// colonizes, and its docked warships everywhere. Feeds the ChokepointGarrison log line.
            /// </summary>
            public (int colonized, int boardTotal, int shipsOnThem, int allShips) ChokepointSummary(int playerId)
            {
                var colonized = 0; var boardTotal = 0; var shipsOnThem = 0; var allShips = 0;
                foreach (var planet in PlanetList)
                {
                    var chokepoint = IsChokepoint(planet.PlanetName);
                    if (chokepoint) boardTotal++;
                    var ships = planet.DockedShips.Count(s => s.Kind == Ship.ShipKind.WarShip && s.Owner == playerId);
                    allShips += ships;
                    if (chokepoint && planet.Owner == playerId && planet.Population.Count > 0)
                    {
                        colonized++;
                        shipsOnThem += ships;
                    }
                }
                return (colonized, boardTotal, shipsOnThem, allShips);
            }

            private void BuildNeighbours()
            {
                _neighbours = new Dictionary<string, List<string>>();
                foreach (var planet in PlanetList)
                    _neighbours[planet.PlanetName] = new List<string>();

                foreach (var planet in PlanetList)
                {
                    if (planet.Connections == null) continue;
                    foreach (var name in planet.Connections)
                    {
                        if (name == null || name == planet.PlanetName || !_neighbours.ContainsKey(name))
                            continue;
                        if (!_neighbours[planet.PlanetName].Contains(name))
                            _neighbours[planet.PlanetName].Add(name);
                        if (!_neighbours[name].Contains(planet.PlanetName))
                            _neighbours[name].Add(planet.PlanetName);
                    }
                }
            }

            public Planet GetPlayerCapitol(int playerID)
            {
                return PlanetList.Find(x => x.Owner == playerID && x.Type == Planet.PlanetType.PlanetTypePrime);
            }

            /// <summary>
            /// The capitol's name, or empty when the player has none: conversion can take a Prime planet away, so notifications must not
            /// assume one exists (an empty view target simply does not move the camera).
            /// </summary>
            public string GetPlayerCapitolName(int playerID) => GetPlayerCapitol(playerID)?.PlanetName ?? string.Empty;

            /// <summary>
            /// Rebuilds every planet's incoming-ship count from in-flight ShipTransport orders.
            /// The counter is derived state, so it is recomputed on load instead of being saved.
            /// </summary>
            public void RecomputeIncomingShips(List<GameAI.GameAIOrder> orders)
            {
                foreach (var planet in PlanetList) planet.ClearIncomingShips();
                foreach (var order in orders)
                {
                    if (order.Type != GameAI.GameAIOrder.OrderType.OrderTypeShipTransport) continue;
                    var target = GetPlanet(order.Target);
                    if (target == null) continue;
                    var kind = order.Fleet != null ? order.Fleet.Kind : Ship.ShipKind.WarShip;
                    target.AddIncomingShips(kind, order.PlayerId, System.Convert.ToInt32(order.Data));
                }
            }

            /// <summary>
            /// Rebuilds every planet's in-flight warship offense from the in-flight ShipTransport orders: the real offense
            /// of each ship from the snapshot it left with. Derived state, recomputed once per turn before the AI decides
            /// (GameAI.GameAIUpdate), so it matches the incoming ship counter and never drifts. Orders without a warship
            /// fleet are ignored.
            /// </summary>
            public void RecomputeIncomingOffense(List<GameAI.GameAIOrder> orders, WarshipStats stats)
            {
                foreach (var planet in PlanetList) planet.ClearIncomingOffense();
                var template = GameAIConstants.warShipData;
                foreach (var order in orders)
                {
                    if (order.Type != GameAI.GameAIOrder.OrderType.OrderTypeShipTransport) continue;
                    if (order.Fleet == null || order.Fleet.Kind != Ship.ShipKind.WarShip) continue;
                    var target = GetPlanet(order.Target);
                    if (target == null) continue;
                    var offense = order.Fleet.Snapshots
                        .Select((snapshot, i) => stats.EffectiveOffense(template, snapshot, order.Fleet.DamageAt(i))).Sum();
                    target.AddIncomingOffense(Ship.ShipKind.WarShip, order.PlayerId, offense);
                }
            }

            public void SetPlanetSimulationStats(SaveLoadSystem.GameSave gameSave)
            {
                var catalog = Gameboard.Instance.GetComponent<Catalog>();
                foreach (var planetStatus in gameSave.planetStatuses)
                {
                    var planet = _planets[planetStatus.name];
                    planet.CurrentStrategy = planetStatus.planetStrategy;
                    planet.Food = planetStatus.food;
                    planet.Morale = planetStatus.morale;
                    planet.Grotsits = planetStatus.grotsits;
                    planet.Research = planetStatus.research;
                    planet.Industry = planetStatus.industry;
                    if (planetStatus.hasCurrentProduction)
                    {
                        // An item the catalog no longer has is skipped: a ProductionItem with a null Item would throw on Cost.
                        var currentItem = catalog.catalogItems.Find(x => x.itemName == planetStatus.currentProduction.Name);
                        if (currentItem != null)
                            planet.CurrentProduction = new Planet.ProductionItem
                            {
                                Progress = planetStatus.currentProduction.Progress,
                                FixedCost = planetStatus.currentProduction.FixedCost,
                                Item = currentItem
                            };
                    }
                    if (planetStatus.productionQueue.Count > 0)
                    {
                        foreach (var production in planetStatus.productionQueue)
                        {
                            planet.ProductionQueue.Add(new Planet.ProductionItem
                            {
                                Progress = 0.0f,
                                FixedCost = production.FixedCost,
                                Item = catalog.catalogItems.Find(x => x.itemName ==  production.Name)
                            });
                        }
                    }
                    // Rebuild the planet's improvements (yields and upkeep) from the catalog by name; unknown names
                    // (a catalog that no longer has the item) are skipped. A missing list (older save) restores none.
                    if (planetStatus.completedImprovements != null)
                        foreach (var improvementName in planetStatus.completedImprovements)
                        {
                            var improvement = catalog.GetItem(improvementName);
                            if (improvement != null) planet.RecordImprovement(improvement);
                        }
                    planet.Owner = planetStatus.owner;
                    planet.RestoreConversion(planetStatus.conversionBy, planetStatus.conversionProgress, planetStatus.conversionWarMask);
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

                    if (planetStatus.dockedShips != null)
                    {
                        foreach (var shipSave in planetStatus.dockedShips)
                        {
                            planet.DockShipFromSave(shipSave.kind, shipSave.owner, shipSave.researchSnapshot, shipSave.damage);
                        }
                    }
                }
            }

            private void DEBUG_LogResults(List<Planet.UpdateResult> resultList)
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