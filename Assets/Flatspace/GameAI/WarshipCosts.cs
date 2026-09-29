using System;
using System.Collections.Generic;
using System.Linq;
using Flatspace.Objects.Production;

namespace FlatSpace.AI
{
    /// <summary>
    /// Warship production costs. A warship costs base x (1 + factor x improvements it carries); an Update Warship
    /// costs the difference between the fully upgraded ship and the ship's own cost, which is base x factor x missing.
    /// Pure so self-checks can drive it.
    /// </summary>
    public static class WarshipCosts
    {
        public static float BuildCost(float baseCost, float factor, int improvements)
            => baseCost * (1f + factor * improvements);

        public static float UpdateCost(float baseWarshipCost, float factor, int missing)
            => Math.Max(1f, baseWarshipCost * factor * missing);

        /// <summary>
        /// The cost fixed into a production item when it is scheduled. `research` is the owner's research catalog
        /// items; `baseWarshipCost` is the Warship production item's catalog cost.
        /// </summary>
        public static float ProductionCost(CatalogItem item, Planet planet, int owner,
            IEnumerable<CatalogItem> research, float baseWarshipCost, float factor)
        {
            switch (item.subType)
            {
                case "Warship":
                    return BuildCost(item.cost, factor, WarshipStats.ResearchedNames(research).Count);
                default:
                    return item.cost;
            }
        }
    }
}
