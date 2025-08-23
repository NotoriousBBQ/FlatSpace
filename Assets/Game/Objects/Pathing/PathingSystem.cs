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
            private readonly List<PathNode> _allPathNodes = new List<PathNode>();
            private List<PathNode> _openPathNodes = new List<PathNode>();
            private List<PathNode> _closedPathNodes = new List<PathNode>();

            public void InitializePathMap(List<Planet> planets)
            {
                // create nodes for all planets
                _allPathNodes.Clear();
                foreach (var planet in planets)
                {
                    _allPathNodes.Add(new PathNode(planet.PlanetName, new Vector2(planet.Position.x, planet.Position.y)));
                }
                // for each node, for wach other node that is closer than max distance, add a connection 
                foreach (var pathNode in _allPathNodes)
                {
                    // a little inefficient here, since all connections are 2-way, but the sample set is small
                    // and this will only be done once
                    foreach (var possibleNeighborNode in _allPathNodes)
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
           
        }
    }
}