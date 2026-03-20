// PlayerAI.cs
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace FlatSpace
{
    namespace AI
    {
        public class PlayerAI : MonoBehaviour
        {
            // ── Public state ─────────────────────────────────────────────────

            public enum AIStrategy
            {
                AIStrategyNone = 0,
                AIStrategyExpand,
                AIStrategyConsolidate,
                AIStrategyAmass
            }

            public AIStrategy Strategy { get; set; } = AIStrategy.AIStrategyExpand;
            public Player      Player  { get; set; }
            public GameAIMap   AIMap   { get; set; }

            // ── Entry point ──────────────────────────────────────────────────

            public void ProcessResults(
                List<Planet.PlanetUpdateResult> results,
                List<GameAI.GameAIOrder>        orders)
            {
                switch (Strategy)
                {
                    case AIStrategy.AIStrategyExpand:
                        ProcessResultsStrategyExpand(results, Player, ref orders);
                        break;
                    case AIStrategy.AIStrategyConsolidate:
                        break;
                    case AIStrategy.AIStrategyAmass:
                        break;
                }
            }

            // ── Strategy: Expand ─────────────────────────────────────────────

            private void ProcessResultsStrategyExpand(
                List<Planet.PlanetUpdateResult> results,
                Player                          player,
                ref List<GameAI.GameAIOrder>    orders)
            {
                ProcessColonizers(results, orders);
                ProcessFoodShortage(results, orders);
                ProcessGrotsitsShortage(results, orders);
                ProcessResearch(results, orders);
                ProcessIndustry(results, orders);
            }

            // ── Colonization ─────────────────────────────────────────────────

            private bool IsValidColonizerForResult(Planet.PlanetUpdateResult result)
            {
                if (result.Result is
                    Planet.PlanetUpdateResult.PlanetUpdateResultType.PlanetUpdateResultTypePopulationGain
                    or Planet.PlanetUpdateResult.PlanetUpdateResultType.PlanetUpdateResultTypePopulationMax)
                {
                    var planet = AIMap.GetPlanet(result.Name);
                    if (planet.Owner == Player.playerID &&
                        planet.Population.Count >= planet.MaxPopulation
                            * AIMap.GameAIConstants.expandPopulationTrigger)
                        return true;
                }
                return false;
            }

            private bool IsValidColonizationTarget(Planet planet)
            {
                if (planet.IsPopulationTransferInProgress(Player.playerID)) return false;
                if (planet.Population.Count == 0)                           return true;
                if (planet.Population.Count >= planet.MaxPopulation)        return false;
                return planet.PlayerWithMostPopulation() != Player.playerID;
            }

            private void ProcessColonizers(
                List<Planet.PlanetUpdateResult> results,
                List<GameAI.GameAIOrder>        orders)
            {
                var colonizers = results.FindAll(IsValidColonizerForResult);
                if (colonizers.Count == 0) return;

                var targets = AIMap.PlanetList.FindAll(IsValidColonizationTarget);
                if (targets.Count == 0) return;

                var matrix = new ScoreMatrix();
                foreach (var colonizer in colonizers)
                {
                    var pathMap = AIMap.GetPlanet(colonizer.Name).DistanceMapToPathingList;
                    var entries = targets
                        .Where(t => pathMap.ContainsKey(t.PlanetName)
                                 && pathMap[t.PlanetName].NumNodes
                                        <= AIMap.GameAIConstants.maxPathNodesForResourceDistribution)
                        .Select(t => new ScoreMatrix.ScoreMatrixElement
                        {
                            Surplus  = 1.0f,
                            Target   = t.PlanetName,
                            Cost     = pathMap[t.PlanetName].Cost,
                            Shortage = 1.0f,
                        })
                        .ToList();

                    if (entries.Count > 0)
                        matrix.MatrixElements.Add(colonizer.Name, entries);
                }

                foreach (var action in matrix.GenerateActionList())
                {
                    var amount = Convert.ToInt32(
                        colonizers.Find(x => x.Name == action.Origin).Data);
                    var delay  = Convert.ToInt32(
                        action.Cost / AIMap.GameAIConstants.defaultTravelSpeed);

                    orders.Add(MakeOrder(GameAI.GameAIOrder.OrderType.OrderTypePopulationTransport,
                        GameAI.GameAIOrder.OrderTimingType.OrderTimingTypeDelayed,
                        delay, delay, amount, action.Origin, action.Target));

                    orders.Add(MakeOrder(GameAI.GameAIOrder.OrderType.OrderTypePopulationChange,
                        GameAI.GameAIOrder.OrderTimingType.OrderTimingTypeImmediate,
                        0, 0, amount * -1.0f, action.Origin, action.Origin));

                    orders.Add(MakeOrder(GameAI.GameAIOrder.OrderType.OrderTypePopulationTransferInProgress,
                        GameAI.GameAIOrder.OrderTimingType.OrderTimingTypeImmediate,
                        0, 0, amount, action.Origin, action.Target));
                }
            }

            // ── Food ─────────────────────────────────────────────────────────

            private void ProcessFoodShortage(
                List<Planet.PlanetUpdateResult> results,
                List<GameAI.GameAIOrder>        orders)
            {
                var matrix = BuildResourceMatrix(results,
                    Planet.PlanetUpdateResult.PlanetUpdateResultType.PlanetUpdateResultTypeFoodShortage,
                    Planet.PlanetUpdateResult.PlanetUpdateResultType.PlanetUpdateResultTypeFoodSurplus,
                    incomingCheck: name => AIMap.GetPlanet(name).FoodShipmentIncoming,
                    out var surplusResults);

                if (matrix == null) return;

                foreach (var action in matrix.GenerateActionList())
                    EmitResourceOrders(action, surplusResults,
                        GameAI.GameAIOrder.OrderType.OrderTypeFoodTransport,
                        GameAI.GameAIOrder.OrderType.OrderTypeFoodChange,
                        GameAI.GameAIOrder.OrderType.OrderTypeFoodTransportInProgress,
                        orders);
            }

            // ── Grotsits ─────────────────────────────────────────────────────

            private void ProcessGrotsitsShortage(
                List<Planet.PlanetUpdateResult> results,
                List<GameAI.GameAIOrder>        orders)
            {
                var matrix = BuildResourceMatrix(results,
                    Planet.PlanetUpdateResult.PlanetUpdateResultType.PlanetUpdateResultTypeGrotsitsShortage,
                    Planet.PlanetUpdateResult.PlanetUpdateResultType.PlanetUpdateResultTypeGrotsitsSurplus,
                    incomingCheck: name => AIMap.GetPlanet(name).GrotsitsShipmentIncoming,
                    out var surplusResults);

                if (matrix == null) return;

                foreach (var action in matrix.GenerateActionList())
                    EmitResourceOrders(action, surplusResults,
                        GameAI.GameAIOrder.OrderType.OrderTypeGrotsitsTransport,
                        GameAI.GameAIOrder.OrderType.OrderTypeGrotsitsChange,
                        GameAI.GameAIOrder.OrderType.OrderTypeGrotsitsTransportInProgress,
                        orders);
            }

            /// <summary>
            /// Shared matrix-building logic for any surplus→shortage resource.
            /// Returns null if there is nothing to do.
            /// </summary>
            private ScoreMatrix BuildResourceMatrix(
                List<Planet.PlanetUpdateResult>                              results,
                Planet.PlanetUpdateResult.PlanetUpdateResultType             shortageType,
                Planet.PlanetUpdateResult.PlanetUpdateResultType             surplusType,
                Func<string, bool>                                           incomingCheck,
                out List<Planet.PlanetUpdateResult>                          surplusResults)
            {
                surplusResults = null;

                var shortages = results.FindAll(x => x.Result == shortageType);
                if (shortages.Count == 0) return null;

                surplusResults = results.FindAll(x => x.Result == surplusType);
                if (surplusResults.Count == 0) return null;

                var matrix = new ScoreMatrix();
                foreach (var surplus in surplusResults)
                {
                    var pathMap = AIMap.GetPlanet(surplus.Name).DistanceMapToPathingList;
                    var entries = shortages
                        .Where(s => !incomingCheck(s.Name)
                                 && pathMap[s.Name].NumNodes
                                        <= AIMap.GameAIConstants.maxPathNodesForResourceDistribution)
                        .Select(s => new ScoreMatrix.ScoreMatrixElement
                        {
                            Surplus  = (float)surplus.Data,
                            Target   = s.Name,
                            Cost     = pathMap[s.Name].Cost,
                            Shortage = (float)s.Data,
                        })
                        .ToList();

                    if (entries.Count > 0)
                        matrix.MatrixElements.Add(surplus.Name, entries);
                }

                return matrix;
            }

            /// <summary>
            /// Emits the three standard orders (transport, deduct, in-progress)
            /// for a resource shipment action.
            /// </summary>
            private void EmitResourceOrders(
                ScoreMatrix.Action               action,
                List<Planet.PlanetUpdateResult>  surplusResults,
                GameAI.GameAIOrder.OrderType     transportType,
                GameAI.GameAIOrder.OrderType     changeType,
                GameAI.GameAIOrder.OrderType     inProgressType,
                List<GameAI.GameAIOrder>         orders)
            {
                var originPlanet = AIMap.GetPlanet(action.Origin);
                var amount       = Convert.ToSingle(
                    surplusResults.Find(x => x.Name == action.Origin).Data)
                    * originPlanet.GetPopulationFraction(Player.playerID);

                if (amount <= 0.0f) return;

                var delay = Convert.ToInt32(action.Cost / AIMap.GameAIConstants.defaultTravelSpeed);

                orders.Add(MakeOrder(transportType,
                    GameAI.GameAIOrder.OrderTimingType.OrderTimingTypeDelayed,
                    delay, delay, amount, action.Origin, action.Target));

                orders.Add(MakeOrder(changeType,
                    GameAI.GameAIOrder.OrderTimingType.OrderTimingTypeImmediate,
                    0, 0, amount * -1.0f, action.Origin, action.Origin));

                orders.Add(MakeOrder(inProgressType,
                    GameAI.GameAIOrder.OrderTimingType.OrderTimingTypeImmediate,
                    0, 0, amount, action.Origin, action.Target));
            }

            // ── Research ─────────────────────────────────────────────────────

            private void ProcessResearch(
                List<Planet.PlanetUpdateResult> results,
                List<GameAI.GameAIOrder>        orders)
            {
                var researchResults = results
                    .Where(p => p.Result == Planet.PlanetUpdateResult.PlanetUpdateResultType
                        .PlanetUpdateResultTypeResearchProduced)
                    .ToList();

                if (researchResults.Count == 0) return;

                var total = researchResults.Sum(p => (float)p.Data);
                if (total > 0.0f) UpdateResearch(total, orders);

                foreach (var r in researchResults)
                    orders.Add(MakeOrder(
                        GameAI.GameAIOrder.OrderType.OrderTypeResearchChange,
                        GameAI.GameAIOrder.OrderTimingType.OrderTimingTypeImmediate,
                        0, 0, (float)r.Data * -1.0f, r.Name, r.Name));
            }

            private void UpdateResearch(float totalResearch, List<GameAI.GameAIOrder> orders)
            {
                // TODO: check if current research is done and choose another
            }

            // ── Industry ─────────────────────────────────────────────────────

            private void ProcessIndustry(
                List<Planet.PlanetUpdateResult> results,
                List<GameAI.GameAIOrder>        orders)
            {
                // TODO: PlanetUpdateResultTypeIndustryProductionComplete — select new production
                // TODO: PlanetUpdateResultTypeIndustrySurplus — ship industry
            }

            // ── Order factory ────────────────────────────────────────────────

            private GameAI.GameAIOrder MakeOrder(
                GameAI.GameAIOrder.OrderType       type,
                GameAI.GameAIOrder.OrderTimingType timing,
                int                                timingDelay,
                int                                totalDelay,
                float                              data,
                string                             origin,
                string                             target)
            => new GameAI.GameAIOrder
            {
                Type        = type,
                TimingType  = timing,
                TimingDelay = timingDelay,
                TotalDelay  = totalDelay,
                Data        = data,
                Origin      = origin,
                Target      = target,
                PlayerId    = Player.playerID,
            };

            // ── MonoBehaviour ────────────────────────────────────────────────

            void Start()  { }
            void Update() { }
        }
    }
}