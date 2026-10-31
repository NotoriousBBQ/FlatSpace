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

        /// <summary>
        /// What one ship of this kind is worth in industry: a colony ship its catalog cost, the others the Warship base
        /// cost (times the Siegeship or Bunker ratio) through BuildCost with the improvements the ship carries.
        /// </summary>
        public static float ShipCost(Ship.ShipKind kind, int improvements, float baseWarshipCost, float colonyShipCost,
            float factor, float siegeshipRatio, float bunkerRatio)
        {
            switch (kind)
            {
                case Ship.ShipKind.ColonyShip: return colonyShipCost;
                case Ship.ShipKind.SiegeShip: return BuildCost(baseWarshipCost * siegeshipRatio, factor, improvements);
                case Ship.ShipKind.Bunker: return BuildCost(baseWarshipCost * bunkerRatio, factor, improvements);
                default: return BuildCost(baseWarshipCost, factor, improvements);
            }
        }

        public static float UpdateCost(float baseWarshipCost, float factor, int missing)
            => Math.Max(1f, baseWarshipCost * factor * missing);

        /// <summary>
        /// The cost fixed into a production item when it is scheduled. `research` is the owner's research catalog
        /// items; `baseWarshipCost` is the Warship production item's catalog cost.
        /// </summary>
        public static float ProductionCost(CatalogItem item, Planet planet, int owner,
            IEnumerable<CatalogItem> research, float baseWarshipCost, float factor,
            float siegeshipRatio = 1.5f, float bunkerRatio = 1.5f)
        {
            switch (item.subType)
            {
                case "Warship":
                    return BuildCost(item.cost, factor, WarshipStats.ResearchedNames(research).Count);
                case "Siegeship":
                    return BuildCost(baseWarshipCost * siegeshipRatio, factor,
                        WarshipStats.ResearchedNames(research, "Siegeship").Count);
                case "Bunker":
                    return BuildCost(baseWarshipCost * bunkerRatio, factor,
                        WarshipStats.ResearchedNames(research, "Bunker").Count);
                case "WarshipUpdate":
                {
                    var researched = WarshipStats.ResearchedNames(research);
                    var target = planet != null ? planet.FindWarshipUpdateTarget(owner, researched) : null;
                    if (target == null) return item.cost;   // nothing to upgrade right now: fall back to the catalog cost
                    var missing = researched.Count(n => !target.ResearchSnapshot.Contains(n));
                    return UpdateCost(baseWarshipCost, factor, missing);
                }
                default:
                    return item.cost;
            }
        }
    }
}
