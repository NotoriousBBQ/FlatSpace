using System;
using System.Collections.Generic;
using System.Linq;
using Flatspace.Objects.Production;
using FlatSpace.AI;

namespace FlatSpace
{
    namespace Human
    {
        /// <summary>One row of the Add item list.</summary>
        public struct BuildOption
        {
            public CatalogItem Item;
            public string Group;   // "Improvements", "Ships" or "Defenses"
            public float Cost;
            public int? Turns;     // at the planet's current industry; null when it makes none
        }

        /// <summary>What a planet can be told to build right now, and when its queue will finish. Pure.</summary>
        public static class HumanBuildList
        {
            private static readonly string[] GroupOrder = { "Improvements", "Ships", "Defenses" };

            /// <summary>
            /// Every production item that can be built now (Planet.IsProductionItemValid: unlocked, not built, not superseded, under the
            /// bunker cap), with the cost it would be scheduled at and an estimate of turns. An Update Warship is offered only when a
            /// docked warship needs one. Locked items are not listed.
            /// </summary>
            public static List<BuildOption> Options(Planet planet, PlayerAI human)
            {
                var options = new List<BuildOption>();
                if (planet == null || human == null) return options;
                var catalog = human.ProductionCatalog.catalogItems;
                var research = human.ResearchCatalog.catalogItems;
                var constants = human.AIMap.GameAIConstants;
                var baseWarship = catalog.Find(x => x.subType == "Warship");
                var baseWarshipCost = baseWarship != null ? baseWarship.cost : 0f;
                var owner = human.Player.playerID;

                foreach (var item in catalog)
                {
                    if (!planet.IsProductionItemValid(item)) continue;
                    // An improvement is built once: not again while one is queued or in production (ships can be queued repeatedly).
                    if (item.type == "Improvement"
                        && (planet.ProductionQueue.Exists(q => q.Item == item)
                            || (planet.CurrentProduction.HasValue && planet.CurrentProduction.Value.Item == item)))
                        continue;
                    if (item.subType == "WarshipUpdate"
                        && planet.FindWarshipUpdateTarget(owner, WarshipStats.ResearchedNames(research)) == null)
                        continue;
                    var cost = WarshipCosts.ProductionCost(item, planet, owner, research, baseWarshipCost,
                        constants.warshipImprovementCostFactor, constants.siegeshipCostRatio, constants.bunkerCostRatio);
                    options.Add(new BuildOption { Item = item, Group = GroupOf(item), Cost = cost, Turns = TurnsFor(cost, planet.IndustryProduced) });
                }
                return options
                    .OrderBy(o => Array.IndexOf(GroupOrder, o.Group))
                    .ThenBy(o => o.Item.tier)
                    .ThenBy(o => o.Item.itemName, StringComparer.Ordinal)
                    .ToList();
            }

            /// <summary>
            /// Cumulative turns until each entry finishes at the current industry: the item in production first (when there is one),
            /// then each queued item, every one adding its own ceil(cost / industry). All null while the planet makes no industry.
            /// </summary>
            public static List<int?> QueueEstimates(Planet planet)
            {
                var estimates = new List<int?>();
                var costs = new List<float>();
                if (planet.CurrentProduction.HasValue)
                    costs.Add(Math.Max(0f, planet.CurrentProduction.Value.Cost - planet.CurrentProduction.Value.Progress));
                foreach (var queued in planet.ProductionQueue)
                    costs.Add(queued.Cost);

                var elapsed = 0;
                foreach (var cost in costs)
                {
                    var turns = TurnsFor(cost, planet.IndustryProduced);
                    if (turns == null) { estimates.Add(null); continue; }
                    elapsed += turns.Value;
                    estimates.Add(estimates.Count > 0 && estimates[estimates.Count - 1] == null ? (int?)null : elapsed);
                }
                return estimates;
            }

            private static int? TurnsFor(float cost, float industryPerTurn)
                => industryPerTurn > 0f ? (int)Math.Ceiling(cost / industryPerTurn) : (int?)null;

            private static string GroupOf(CatalogItem item)
            {
                if (item.type == "Improvement") return "Improvements";
                return item.subType == "Bunker" ? "Defenses" : "Ships";
            }
        }
    }
}
