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
            public float DeclinedDetourCost = -1f;   // a Divert chosen over an available detour: that detour's real cost; else -1
        }

        public static Result Plan(GameAIMap map, BlockadeSystem blockade, GameAI.GameAIOrder order, BlockadeView view,
            int maxNodes, Func<Planet, bool> isCandidate, Func<string, float> costDivisor, float detourDivertRatio = 0f)
        {
            var result = new Result { Target = order.Target, CurrentNode = blockade.CurrentNode(order) };
            if (view == null || order.TimingDelay <= 0) return result;

            result.BlockedAhead = blockade.NodesAhead(order).Where(view.IsBlockaded).ToList();
            if (result.BlockedAhead.Count == 0) return result;

            var detour = RoutePlanner.PlanRoute(map, result.CurrentNode, order.Target, view, maxNodes);
            // A detour is taken at once unless the ratio rule is on, in which case a diversion is looked for first.
            if (detour != null && detourDivertRatio <= 0f)
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

            // A detour is kept unless a diversion exists and the detour costs more than the ratio times the diversion's real cost.
            if (detour != null && (bestRoute == null || detour.Cost <= detourDivertRatio * bestRoute.Cost))
            {
                result.Kind = RedirectKind.Detour;
                result.Nodes = detour.Nodes;
                result.Cost = detour.Cost;
                return result;
            }

            if (bestRoute == null) return result;
            if (detour != null) result.DeclinedDetourCost = detour.Cost;
            result.Kind = RedirectKind.Divert;
            result.Target = bestTarget;
            result.Nodes = bestRoute.Nodes;
            result.Cost = bestRoute.Cost;
            return result;
        }

        /// <summary>
        /// Applies a Plan result to the colonist order: new route, delay restarted from the new route's real cost (at
        /// least 1), and for a divert the new target. Its food rider (matched on player, origin, old target and the
        /// colonist's own delay, so a twin colonist's rider is never touched) follows; the transfer flags follow a divert.
        /// </summary>
        public static void Apply(GameAIMap map, List<GameAI.GameAIOrder> orders, GameAI.GameAIOrder colonist, Result result)
        {
            if (result.Kind == RedirectKind.None) return;

            var oldTarget = colonist.Target;
            var oldDelay = colonist.TimingDelay;
            var rider = orders.Find(o => o.Type == GameAI.GameAIOrder.OrderType.OrderTypeColonyFoodRider
                                         && o.PlayerId == colonist.PlayerId && o.Origin == colonist.Origin
                                         && o.Target == oldTarget && o.TimingDelay == oldDelay);

            var delay = Math.Max(1, Convert.ToInt32(result.Cost / map.GameAIConstants.defaultTravelSpeed));
            colonist.Route = new List<string>(result.Nodes);
            colonist.TotalDelay = delay;
            colonist.TimingDelay = delay;
            if (rider != null)
            {
                rider.TotalDelay = delay;
                rider.TimingDelay = delay;
            }

            if (result.Kind != RedirectKind.Divert) return;

            colonist.Target = result.Target;
            if (rider != null) rider.Target = result.Target;

            var stillHeadingThere = orders.Exists(o => o != colonist
                && o.Type == GameAI.GameAIOrder.OrderType.OrderTypePopulationTransport
                && o.PlayerId == colonist.PlayerId && o.Target == oldTarget);
            if (!stillHeadingThere) map.GetPlanet(oldTarget)?.SetPopulationTransferInProgress(colonist.PlayerId, false);
            map.GetPlanet(result.Target)?.SetPopulationTransferInProgress(colonist.PlayerId);
        }
    }
}
