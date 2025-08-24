using System.Collections.Generic;
using UnityEngine;

namespace FlatSpace
{
    namespace Pathing
    {
        public struct Connection
        {
            public PathNode Node;
            public float Cost;

            public Connection(PathNode node, float cost)
            {
                Node = node;
                Cost = cost;
            }
        }
        public struct PathNode
        {
            public string Name;
            public Vector2 Position;
            public List<Connection> Connections;

            public PathNode(string name, Vector2 position)
            {
                Name = name;
                Position = position;
                Connections = new List<Connection>();
            }
        }

        public class Path
        {
            private int _cost;
            public List<PathNode> PathNodes = new List<PathNode>();
        }
        
        public class PathingSystem : MonoBehaviour
        {
            private static PathingSystem _instance;
            public static PathingSystem Instance => _instance ?? (_instance = new GameObject("PathingSystem").AddComponent<PathingSystem>());

            public readonly static float MaxConnectionSize = 400.0f;
            public List<PathNode> PathNodes { get; } = new List<PathNode>();
            private List<PathNode> _openPathNodes = new List<PathNode>();
            private List<PathNode> _closedPathNodes = new List<PathNode>();

            public void InitializePathMap(List<Planet> planets)
            {
                // create nodes for all planets
                PathNodes.Clear();
                foreach (var planet in planets)
                {
                    PathNodes.Add(new PathNode(planet.PlanetName, new Vector2(planet.Position.x, planet.Position.y)));
                }
                // for each node, for wach other node that is closer than max distance, add a connection 
                foreach (var pathNode in PathNodes)
                {
                    // a little inefficient here, since all connections are 2-way, but the sample set is small
                    // and this will only be done once
                    foreach (var possibleNeighborNode in PathNodes)
                    {
                        if (possibleNeighborNode.Name == pathNode.Name)
                            continue;
                        float distance = Vector2.Distance(pathNode.Position, possibleNeighborNode.Position);
                        if (distance <= MaxConnectionSize)
                        {
                            pathNode.Connections.Add(new Connection(possibleNeighborNode, distance));
                        }
                    }
                }
            }

            public void ConnectionVectors(List<(Vector3, Vector3)> connectionVectorList)
            {
                List<string> alreadySeen = new List<string>();
                foreach (var node in PathNodes)
                {
                    alreadySeen.Add(node.Name);
                    foreach (var connection in node.Connections)
                    {
                        if (alreadySeen.Contains(connection.Node.Name))
                            continue;
                        Vector3 p1 = new Vector3(node.Position.x, node.Position.y, 0.0f);
                        Vector3 p2 = new Vector3(connection.Node.Position.x, connection.Node.Position.y, 0.0f);
                        connectionVectorList.Add((p1, p2));
                    }
                    
                }
            }
           
        }
    }
}