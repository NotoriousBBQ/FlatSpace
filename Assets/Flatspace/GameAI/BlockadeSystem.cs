using System.Collections.Generic;
using System.Linq;
using Flatspace.Objects.Production;
using UnityEngine;

namespace FlatSpace.AI
{
    /// <summary>
    /// Blockade: at a planet, the value against an order's owner is the largest single OTHER player's docked warship
    /// offense minus the order owner's own docked offense there, counted only when positive. Only docked ships count.
    /// Pure (no Gameboard.Instance): it reads a GameAIMap and a research item list, so self-checks drive it directly.
    /// </summary>
    public class BlockadeSystem
    {
        public struct RouteNode
        {
            public string Name;
            public float Fraction;   // how far along the route this node is: cumulative cost / total cost, target = 1
        }

        private const float Epsilon = 0.0001f;

        private readonly GameAIMap _map;
        private readonly WarshipStats _stats;

        public BlockadeSystem(GameAIMap map, IEnumerable<CatalogItem> researchItems)
        {
            _map = map;
            _stats = new WarshipStats(researchItems);
        }

        public float DockedOffense(Planet planet, int owner)
        {
            var sum = 0f;
            foreach (var ship in planet.DockedShips)
                if (ship.Kind == Ship.ShipKind.WarShip && ship.Owner == owner)
                    sum += _stats.Offense(ship.Template, ship.ResearchSnapshot);
            return sum;
        }

        /// <summary>The blockade against `orderOwner` at `planet`; `blocker` is the player imposing it (or NoOwner).</summary>
        public float Value(Planet planet, int orderOwner, out int blocker)
        {
            blocker = Planet.NoOwner;
            var own = DockedOffense(planet, orderOwner);
            var best = 0f;
            var others = planet.DockedShips
                .Where(s => s.Kind == Ship.ShipKind.WarShip && s.Owner != orderOwner && s.Owner != Planet.NoOwner)
                .Select(s => s.Owner).Distinct().OrderBy(o => o);
            foreach (var other in others)
            {
                var value = DockedOffense(planet, other) - own;
                if (value > best)   // strictly greater: ties keep the lowest player id
                {
                    best = value;
                    blocker = other;
                }
            }
            return best;
        }

        /// <summary>
        /// Every node after the origin, in order, with its fraction of the trip. When no route exists (FindPath
        /// returns a 1-node stub, or a name is unknown / the same) only the target is listed.
        /// </summary>
        public List<RouteNode> Route(string origin, string target)
        {
            var only = new List<RouteNode> { new RouteNode { Name = target, Fraction = 1f } };
            if (origin == target || _map.GetPlanet(origin) == null || _map.GetPlanet(target) == null) return only;

            var nodes = _map.GetPath(origin, target).PathNodes;
            if (nodes.Count < 2) return only;

            var cumulative = new float[nodes.Count];
            for (var i = 1; i < nodes.Count; i++)
            {
                var edge = 0f;
                foreach (var connection in nodes[i - 1].Connections)
                    if (connection.NodeName == nodes[i].Name)
                    {
                        edge = connection.Cost;
                        break;
                    }
                cumulative[i] = cumulative[i - 1] + edge;
            }

            var total = cumulative[nodes.Count - 1];
            var result = new List<RouteNode>();
            for (var i = 1; i < nodes.Count; i++)
                result.Add(new RouteNode
                {
                    Name = nodes[i].Name,
                    Fraction = total > 0f ? cumulative[i] / total : (float)i / (nodes.Count - 1),
                });
            return result;
        }

        /// <summary>0 when an order is just launched, 1 when it has arrived.</summary>
        public static float Progress(int timingDelay, int totalDelay)
            => totalDelay <= 0 ? 1f : Mathf.Clamp01(1f - (float)timingDelay / totalDelay);

        /// <summary>
        /// The route nodes an order passed this turn. Call after the order's TimingDelay was decremented: the previous
        /// turn's progress is derived from TimingDelay + 1, so a node is reported exactly once.
        /// </summary>
        public List<RouteNode> PassedNodes(GameAI.GameAIOrder order)
        {
            var now = Progress(order.TimingDelay, order.TotalDelay);
            var previous = order.TotalDelay <= 0 ? 0f : Progress(order.TimingDelay + 1, order.TotalDelay);
            return Route(order.Origin, order.Target)
                .Where(n => n.Fraction > previous + Epsilon && n.Fraction <= now + Epsilon)
                .ToList();
        }
    }
}
