using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace FlatSpace
{
    namespace Pathing
    {
        public struct Connection
        {
            public string NodeName;
            public readonly float Cost;

            public Connection(string nodeName, float cost)
            {
                NodeName = nodeName;
                Cost = cost;
            }
        }
        public struct PathNode
        {
            public readonly string Name;
            public Vector2 Position;
            public readonly List<Connection> Connections;

            public float GValue;
            public float HValue;
            public float FValue;
            public string ParentName;

            public PathNode(string name, Vector2 position)
            {
                Name = name;
                Position = position;
                Connections = new List<Connection>();
                GValue = HValue = FValue = 0.0f;
                ParentName = "";
            }
        }

        public class Path
        {
            public float Cost;
            public int NumNodes;
            public List<PathNode> PathNodes = new List<PathNode>();
        }
        
        public class PathingSystem : MonoBehaviour
        {
            private static PathingSystem _instance;
            public static PathingSystem Instance => _instance ?? (_instance = new GameObject("PathingSystem").AddComponent<PathingSystem>());

            private const float MaxConnectionSize = 400.0f;
            public Dictionary<string, PathNode> PathNodes = new Dictionary<string, PathNode>();
            private List<PathNode> _openPathNodes = new List<PathNode>();
            private List<PathNode> _closedPathNodes = new List<PathNode>();
            private List<NodeScore> _nodeScores = new List<NodeScore>();

            public void InitializePathMap(List<Planet> planets)
            {
                PathNodes.Clear();
                foreach (var planet in planets)
                {
                    PathNodes[planet.PlanetName] =
                        new PathNode(planet.PlanetName, new Vector2(planet.Position.x, planet.Position.y));
                }

                var useExplicit = planets.Any(p => p.Connections != null && p.Connections.Count > 0);
                if (useExplicit)
                    BuildExplicitConnections(planets);
                else
                    BuildDistanceConnections();

                Debug.Log($"[PathingSystem] graph mode: {(useExplicit ? "explicit" : "distance")}, {PathNodes.Count} nodes");
                foreach (var node in PathNodes.Values)
                    if (node.Connections.Count == 0)
                        Debug.LogError($"[PathingSystem] planet '{node.Name}' has no connections; pathing to/from it will be wrong");
            }

            // Current behavior: an edge between every pair of nodes closer than MaxConnectionSize.
            private void BuildDistanceConnections()
            {
                foreach (var pathNode in PathNodes.Values)
                {
                    foreach (var possibleNeighborNode in PathNodes.Values)
                    {
                        if (possibleNeighborNode.Name == pathNode.Name)
                            continue;
                        var distance = Vector2.Distance(pathNode.Position, possibleNeighborNode.Position);
                        if (distance <= MaxConnectionSize)
                            pathNode.Connections.Add(new Connection(possibleNeighborNode.Name, distance));
                    }
                }
            }

            // Board-authored graph: edges come from Planet.Connections. Symmetrized (an
            // edge A-B is added even if only A lists B) and de-duplicated. Unknown names
            // are logged and skipped. Cost is straight-line distance, keeping the A*
            // heuristic in FindPath admissible.
            private void BuildExplicitConnections(List<Planet> planets)
            {
                void AddEdge(string from, string to)
                {
                    if (from == to) return;
                    if (!PathNodes.ContainsKey(from) || !PathNodes.ContainsKey(to))
                        return;
                    var node = PathNodes[from];
                    if (node.Connections.Any(c => c.NodeName == to))
                        return;
                    var cost = Vector2.Distance(node.Position, PathNodes[to].Position);
                    node.Connections.Add(new Connection(to, cost));
                }

                foreach (var planet in planets)
                {
                    if (planet.Connections == null)
                        continue;
                    if (!PathNodes.ContainsKey(planet.PlanetName))
                        continue;
                    foreach (var neighbor in planet.Connections)
                    {
                        if (!PathNodes.ContainsKey(neighbor))
                        {
                            Debug.LogWarning($"[PathingSystem] connection '{planet.PlanetName}' -> '{neighbor}' names an unknown planet; skipped");
                            continue;
                        }
                        AddEdge(planet.PlanetName, neighbor);
                        AddEdge(neighbor, planet.PlanetName);
                    }
                }
            }

            private struct NodeScore
            {
                public string Name;
                public float FValue;
                public float GValue;
                public float HValue;
                public string ParentName;

                NodeScore(string name)
                {
                    Name = name;
                    FValue = 0.0f;
                    GValue = 0.0f;
                    HValue = 0.0f;
                    ParentName = string.Empty;
                }
            }
            public void FindPath(string originName, string destinationName, out Path path)
            {
                var openList = new List<(string, float)>();
                var closedList = new List<(string, float)>();

                var tempDestination = PathNodes[destinationName];
                var destinationPosition = tempDestination.Position;
                
                // initialize search nodes
                var keyList = PathNodes.Keys.ToList();
                foreach (var key in keyList)
                {
                    var tempNode = PathNodes[key];
                    if (tempNode.Name == originName || tempNode.Name == destinationName)
                        tempNode.HValue = 0.0f;
                    else
                        tempNode.HValue = Vector2.Distance(tempNode.Position, destinationPosition);

                    tempNode.ParentName = string.Empty;
                    tempNode.FValue = tempNode.GValue = 0.0f;
                    PathNodes[tempNode.Name] = tempNode;
                }
                // start the search
                openList.Add((originName, 0.0f));
                while (openList.Count > 0)
                {
                    openList.Sort((p1, p2) => p1.Item2.CompareTo(p2.Item2));
                    var currentNode = PathNodes[openList[0].Item1];
                    openList.RemoveAt(0);

                    for(var i = 0; i < currentNode.Connections.Count; i++)
                    {
                        Connection currentConnection = currentNode.Connections[i];
                        var nodeGValue = currentNode.GValue + currentConnection.Cost;
                        var nodeFValue = nodeGValue + PathNodes[currentConnection.NodeName].HValue;
                        
                        if (currentConnection.NodeName == destinationName)
                        {
                            var tempDestinationNode = PathNodes[currentConnection.NodeName];
                            tempDestinationNode.ParentName = currentNode.Name;
                            tempDestinationNode.GValue = nodeGValue;
                            PathNodes[currentConnection.NodeName] = tempDestinationNode;
                            openList.Clear();
                            break;
                        }
                        
                        var closedListElement = closedList.Find(x => x.Item1 == currentConnection.NodeName);
                        if (closedListElement.Item1 != null)
                        {
                            if (closedListElement.Item2 < nodeFValue)
                                continue;
                        }
                        
                        var openListElement = openList.Find(x => x.Item1 == currentConnection.NodeName);
                        if (openListElement.Item1 != null)
                        {
                            if (openListElement.Item2 < nodeFValue)
                                continue;
                        }
                        var tempConnectionNode = PathNodes[currentConnection.NodeName];
                        tempConnectionNode.FValue = nodeFValue;
                        tempConnectionNode.GValue = nodeGValue;
                        tempConnectionNode.ParentName = currentNode.Name;
                        PathNodes[tempConnectionNode.Name] = tempConnectionNode;
                        if(closedListElement.Item1 != null)
                            closedList.Remove(closedListElement);
                        if(openListElement.Item1 != null)
                            openList.Remove(openListElement);
                        if (tempConnectionNode.Name != destinationName)
                            openList.Add((tempConnectionNode.Name, nodeFValue));

                    }
                    closedList.Add((currentNode.Name, currentNode.FValue));
                }
               
                path = new Path();
                ConstructPath(PathNodes[destinationName], ref path);
                return;
            }

            private void ConstructPath(PathNode destination, ref Path path)
            {
                path.Cost = destination.GValue;
                var node = destination;
                while (!string.IsNullOrEmpty(node.ParentName))
                {
                    path.PathNodes.Add(node);
                    node = PathNodes[node.ParentName];
                }
                path.PathNodes.Add(node);
                path.PathNodes.Reverse();
                path.NumNodes = path.PathNodes.Count;
            }

            public void ConnectionVectors(List<(Vector3, Vector3)> connectionVectorList)
            {
                var alreadySeen = new List<string>();
                foreach (var node in PathNodes.Values)
                {
                    alreadySeen.Add(node.Name);
                    foreach (var connection in node.Connections)
                    {
                        if (alreadySeen.Contains(connection.NodeName))
                            continue;
                        var p1 = new Vector3(node.Position.x, node.Position.y, 0.0f);
                        var p2 = new Vector3(PathNodes[connection.NodeName].Position.x, PathNodes[connection.NodeName].Position.y, 0.0f);
                        connectionVectorList.Add((p1, p2));
                    }
                }
            }
           
        }
    }
}