using System.Collections.Generic;
using System.Linq;
using Flatspace.Objects.Production;

namespace FlatSpace.AI
{
    /// <summary>
    /// What a player is worth: its planets (authored per type), its ships (industry cost scaled by health left) and its
    /// completed research (cost). Pure: reads a GameAIMap, never Gameboard.Instance, so self-checks can drive it.
    /// </summary>
    public static class PlayerValue
    {
        public struct ScoreBreakdown
        {
            public float Planets;
            public float Fleets;
            public float Tech;
            public float Total => Planets + Fleets + Tech;
        }

        public class ValueSettings
        {
            public float BaseWarshipCost;
            public float ColonyShipCost;
            public float ImprovementFactor;
            public float SiegeshipRatio = 1.5f;
            public float BunkerRatio = 1.5f;
            public float FleetDivisor = 1f;
            public float TechDivisor = 1f;
            public ShipData ColonyShipData;
            public ShipData WarShipData;
            public ShipData SiegeShipData;
            public ShipData BunkerData;

            public static ValueSettings From(GameAIConstants constants, IEnumerable<CatalogItem> productionCatalog)
            {
                var items = productionCatalog != null ? productionCatalog.Where(i => i != null).ToList() : new List<CatalogItem>();
                var warship = items.FirstOrDefault(i => i.subType == "Warship");
                var colony = items.FirstOrDefault(i => i.subType == "ColonyShip");
                return new ValueSettings
                {
                    BaseWarshipCost = warship != null ? warship.cost : 0f,
                    ColonyShipCost = colony != null ? colony.cost : 0f,
                    ImprovementFactor = constants.warshipImprovementCostFactor,
                    SiegeshipRatio = constants.siegeshipCostRatio,
                    BunkerRatio = constants.bunkerCostRatio,
                    FleetDivisor = constants.fleetValueDivisor > 0f ? constants.fleetValueDivisor : 1f,
                    TechDivisor = constants.techValueDivisor > 0f ? constants.techValueDivisor : 1f,
                    ColonyShipData = constants.colonyShipData,
                    WarShipData = constants.warShipData,
                    SiegeShipData = constants.siegeShipData,
                    BunkerData = constants.bunkerData,
                };
            }

            public ShipData TemplateFor(Ship.ShipKind kind)
            {
                switch (kind)
                {
                    case Ship.ShipKind.ColonyShip: return ColonyShipData;
                    case Ship.ShipKind.SiegeShip: return SiegeShipData;
                    case Ship.ShipKind.Bunker: return BunkerData;
                    default: return WarShipData;
                }
            }
        }

        public static float ShipValue(Ship ship, WarshipStats stats, ValueSettings s)
            => ShipValue(ship.Kind, ship.Template, ship.ResearchSnapshot, ship.Damage, stats, s);

        public static float ShipValue(Ship.ShipKind kind, ShipData template, ICollection<string> snapshot, float damage,
            WarshipStats stats, ValueSettings s)
        {
            var improvements = snapshot != null ? snapshot.Count : 0;
            var cost = WarshipCosts.ShipCost(kind, improvements, s.BaseWarshipCost, s.ColonyShipCost,
                s.ImprovementFactor, s.SiegeshipRatio, s.BunkerRatio);
            var fraction = stats.HealthFraction(template, snapshot, damage, WarshipStats.SubtypeOf(kind));
            return cost / s.FleetDivisor * fraction;
        }

        public static float TechValue(IEnumerable<CatalogItem> research, ValueSettings s)
        {
            if (research == null) return 0f;
            return research.Where(i => i != null && i.researched).Sum(i => i.cost) / s.TechDivisor;
        }

        public static ScoreBreakdown ScoreOf(GameAIMap map, int player, IEnumerable<GameAI.GameAIOrder> orders,
            IEnumerable<CatalogItem> research, WarshipStats stats, ValueSettings s)
        {
            var result = new ScoreBreakdown();
            foreach (var planet in map.PlanetList)
            {
                if (planet.Owner == player) result.Planets += planet.PlanetValue;
                foreach (var ship in planet.DockedShips)
                    if (ship.Owner == player) result.Fleets += ShipValue(ship, stats, s);
            }
            if (orders != null)
            {
                foreach (var order in orders)
                {
                    if (order == null || order.PlayerId != player || order.Fleet == null) continue;
                    if (order.Type != GameAI.GameAIOrder.OrderType.OrderTypeShipTransport) continue;
                    var template = s.TemplateFor(order.Fleet.Kind);
                    for (var i = 0; i < order.Fleet.Snapshots.Count; i++)
                        result.Fleets += ShipValue(order.Fleet.Kind, template, order.Fleet.Snapshots[i], order.Fleet.DamageAt(i), stats, s);
                }
            }
            result.Tech = TechValue(research, s);
            return result;
        }
    }
}
