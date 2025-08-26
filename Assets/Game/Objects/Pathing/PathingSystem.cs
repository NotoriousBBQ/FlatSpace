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
            public List<PathNode> PathNodes = new List<PathNode>();
        }
        
        public class PathingSystem : MonoBehaviour
        {
            private static PathingSystem _instance;
            public static PathingSystem Instance => _instance ?? (_instance = new GameObject("PathingSystem").AddComponent<PathingSystem>());

            private const float MaxConnectionSize = 400.0f;
            private readonly Dictionary<string, PathNode> _pathNodes = new Dictionary<string, PathNode>();
            private List<PathNode> _openPathNodes = new List<PathNode>();
            private List<PathNode> _closedPathNodes = new List<PathNode>();
            private List<NodeScore> _nodeScores = new List<NodeScore>();

            public void InitializePathMap(List<Planet> planets)
            {
                // create nodes for all planets
                _pathNodes.Clear();
                foreach (var planet in planets)
                {
                    _pathNodes[planet.PlanetName]= new PathNode(planet.PlanetName, new Vector2(planet.Position.x, planet.Position.y));
                }
                // for each node, for wach other node that is closer than max distance, add a connection 
                foreach (var pathNode in _pathNodes.Values)
                {
                    // a little inefficient here, since all connections are 2-way, but the sample set is small
                    // and this will only be done once
                    foreach (var possibleNeighborNode in _pathNodes.Values)
                    {
                        if (possibleNeighborNode.Name == pathNode.Name)
                            continue;
                        var distance = Vector2.Distance(pathNode.Position, possibleNeighborNode.Position);
                        if (distance <= MaxConnectionSize)
                        {
                            pathNode.Connections.Add(new Connection(possibleNeighborNode.Name, distance));
                        }
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

                var tempDestination = _pathNodes[destinationName];
                var destinationPosition = tempDestination.Position;
                
                // initialize search nodes
                var keyList = _pathNodes.Keys.ToList();
                foreach (var key in keyList)
                {
                    var tempNode = _pathNodes[key];
                    if (tempNode.Name == originName || tempNode.Name == destinationName)
                        tempNode.HValue = 0.0f;
                    else
                        tempNode.HValue = Vector2.Distance(tempNode.Position, destinationPosition);

                    tempNode.ParentName = string.Empty;
                    tempNode.FValue = tempNode.GValue = 0.0f;
                    _pathNodes[tempNode.Name] = tempNode;
                }
                // start the search
                openList.Add((originName, 0.0f));
                while (openList.Count > 0)
                {
                    openList.Sort((p1, p2) => p1.Item2.CompareTo(p2.Item2));
                    var currentNode = _pathNodes[openList[0].Item1];
                    openList.RemoveAt(0);

                    for(var i = 0; i < currentNode.Connections.Count; i++)
                    {
                        Connection currentConnection = currentNode.Connections[i];
                        var nodeGValue = currentNode.GValue + currentConnection.Cost;
                        var nodeFValue = nodeGValue + _pathNodes[currentConnection.NodeName].HValue;
                        
                        if (currentConnection.NodeName == destinationName)
                        {
                            var tempDestinationNode = _pathNodes[currentConnection.NodeName];
                            tempDestinationNode.ParentName = currentNode.Name;
                            tempDestinationNode.GValue = nodeGValue;
                            _pathNodes[currentConnection.NodeName] = tempDestinationNode;
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
                        var tempConnectionNode = _pathNodes[currentConnection.NodeName];
                        tempConnectionNode.FValue = nodeFValue;
                        tempConnectionNode.GValue = nodeGValue;
                        tempConnectionNode.ParentName = currentNode.Name;
                        _pathNodes[tempConnectionNode.Name] = tempConnectionNode;
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
                ConstructPath(_pathNodes[destinationName], ref path);
                return;
            }

            private void ConstructPath(PathNode destination, ref Path path)
            {
                path.Cost = destination.GValue;
                var node = destination;
                while (!string.IsNullOrEmpty(node.ParentName))
                {
                    path.PathNodes.Add(node);
                    node = _pathNodes[node.ParentName];
                }
                path.PathNodes.Add(node);
                path.PathNodes.Reverse();
            }

            public void ConnectionVectors(List<(Vector3, Vector3)> connectionVectorList)
            {
                var alreadySeen = new List<string>();
                foreach (var node in _pathNodes.Values)
                {
                    alreadySeen.Add(node.Name);
                    foreach (var connection in node.Connections)
                    {
                        if (alreadySeen.Contains(connection.NodeName))
                            continue;
                        var p1 = new Vector3(node.Position.x, node.Position.y, 0.0f);
                        var p2 = new Vector3(_pathNodes[connection.NodeName].Position.x, _pathNodes[connection.NodeName].Position.y, 0.0f);
                        connectionVectorList.Add((p1, p2));
                    }
                    
                }
            }
           
        }
    }
}