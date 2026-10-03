using System;
using System.Collections.Generic;
using System.Linq;

namespace FlatSpace.AI
{
    /// <summary>
    /// Re-plans a colonist already in flight when the owner now sees a blockade ahead of it: first a clean route to
    /// the same target (a detour, however long), else a clean route to another candidate target, else nothing (the
    /// order is cut as before). Pure: no Gameboard.Instance, so the self-check drives it directly. The colonist is
    /// treated as standing on the last route node it passed (BlockadeSystem.CurrentNode).
    /// </summary>
    public static class ColonistRedirect
    {
        public enum RedirectKind { None, Detour, Divert }

        public class Result
        {
            public RedirectKind Kind = RedirectKind.None;
            public string CurrentNode;
            public string Target;            // the target after the redirect (the order's own for None and Detour)
            public List<string> Nodes;       // the new route, from CurrentNode; null for None
            public float Cost;               // the REAL route cost (never tilted by the divisor); 0 for None
            public List<string> BlockedAhead = new List<string>();   // remaining route nodes the view says are blockaded
        }

        public static Result Plan(GameAIMap map, BlockadeSystem blockade, GameAI.GameAIOrder order, BlockadeView view,
            int maxNodes, Func<Planet, bool> isCandidate, Func<string, float> costDivisor)
        {
            var result = new Result { Target = order.Target, CurrentNode = blockade.CurrentNode(order) };
            if (view == null || order.TimingDelay <= 0) return result;

            result.BlockedAhead = blockade.NodesAhead(order).Where(view.IsBlockaded).ToList();
            if (result.BlockedAhead.Count == 0) return result;

            var detour = RoutePlanner.PlanRoute(map, result.CurrentNode, order.Target, view, maxNodes);
            if (detour != null)
            {
                result.Kind = RedirectKind.Detour;
                result.Nodes = detour.Nodes;
                result.Cost = detour.Cost;
                return result;
            }

            string bestTarget = null;
            RoutePlanner.PlannedRoute bestRoute = null;
            var bestChoiceCost = 0f;
            foreach (var planet in map.PlanetList)
            {
                var name = planet.PlanetName;
                if (name == order.Target || name == result.CurrentNode || !isCandidate(planet)) continue;
                var route = RoutePlanner.PlanRoute(map, result.CurrentNode, name, view, maxNodes);
                if (route == null) continue;
                var choiceCost = route.Cost / Math.Max(costDivisor(name), 0.0001f);
                if (bestRoute == null || choiceCost < bestChoiceCost
                    || (choiceCost == bestChoiceCost && string.CompareOrdinal(name, bestTarget) < 0))
                {
                    bestTarget = name;
                    bestRoute = route;
                    bestChoiceCost = choiceCost;
                }
            }

            if (bestRoute == null) return result;
            result.Kind = RedirectKind.Divert;
            result.Target = bestTarget;
            result.Nodes = bestRoute.Nodes;
            result.Cost = bestRoute.Cost;
            return result;
        }
    }
}
