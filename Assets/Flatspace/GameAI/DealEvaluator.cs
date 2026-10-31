using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Flatspace.Objects.Production;

namespace FlatSpace.AI
{
    /// <summary>
    /// Everything the evaluator reads, as delegates so a self-check can fake it and no Gameboard.Instance is touched.
    /// WarCost is (victim, attacker); Research returns a player's research catalog items.
    /// </summary>
    public class DealContext
    {
        public GameAIMap Map;
        public int Proposer, Rival;
        public PlayerValue.ValueSettings Settings;
        public WarshipStats Stats;
        public Func<int, PlayerValue.ScoreBreakdown> ScoreOf;
        public Func<int, int, float> WarCost;
        public Func<int, IEnumerable<CatalogItem>> Research;
    }

    /// <summary>How the rival values a deal: what it receives, what it gives up and the worth of ending a war. Pure.</summary>
    public static class DealEvaluator
    {
        /// <summary>
        /// The not-yet-researched items of a tech's prerequisite chain: the item itself plus every item reached through
        /// requiredTech, stopping at an empty value, an item already researched, an unknown name or a cycle. Empty when the
        /// item is unknown or already researched.
        /// </summary>
        public static List<CatalogItem> MissingChain(IEnumerable<CatalogItem> items, string itemName)
        {
            var chain = new List<CatalogItem>();
            if (items == null || string.IsNullOrEmpty(itemName)) return chain;
            var list = items.Where(i => i != null).ToList();
            var name = itemName;
            while (!string.IsNullOrEmpty(name))
            {
                var item = list.FirstOrDefault(i => i.itemName == name);
                if (item == null || item.researched || chain.Contains(item)) break;
                chain.Add(item);
                name = item.requiredTech;
            }
            return chain;
        }

        public static DealBand BandOf(float net, float scale)
        {
            var r = net / (scale > 0f ? scale : 1f);
            if (r < -2f) return DealBand.VeryUnlikely;
            if (r < -0.5f) return DealBand.Unlikely;
            if (r < 0f) return DealBand.Uncertain;
            if (r < 2f) return DealBand.Likely;
            return DealBand.VeryLikely;
        }

        /// <summary>The value of one item as held by `owner` (the other side is the receiver).</summary>
        public static float ItemValue(DealContext ctx, int owner, DealItem item)
        {
            if (item == null) return 0f;
            switch (item.kind)
            {
                case "planet":
                {
                    var planet = ctx.Map.GetPlanet(item.name);
                    return planet != null ? planet.PlanetValue : 0f;
                }
                case "fleet":
                {
                    var planet = ctx.Map.GetPlanet(item.planet);
                    if (planet == null || !Enum.TryParse(item.name, true, out Ship.ShipKind kind)) return 0f;
                    return planet.DockedShips
                        .Where(s => s.Kind == kind && s.Owner == owner)
                        .OrderBy(s => s.Damage)   // healthiest first
                        .Take(Math.Max(item.count, 0))
                        .Sum(s => PlayerValue.ShipValue(s, ctx.Stats, ctx.Settings));
                }
                case "tech":
                {
                    var receiver = owner == ctx.Proposer ? ctx.Rival : ctx.Proposer;
                    var research = ctx.Research != null ? ctx.Research(receiver) : null;
                    // PlayerValue.TechValue counts only researched items and the chain is by definition unresearched,
                    // so the chain's cost is summed the same way (cost / TechDivisor).
                    return MissingChain(research, item.name).Sum(i => i.cost) / ctx.Settings.TechDivisor;
                }
                default:
                    return 0f;
            }
        }

        private static string Describe(DealItem item)
        {
            switch (item.kind)
            {
                case "planet": return item.name;
                case "fleet": return item.count.ToString(CultureInfo.InvariantCulture) + " " + item.name + " at " + item.planet;
                case "tech": return item.name;
                default: return null;
            }
        }

        public static DealVerdict Evaluate(DealContext ctx, Deal deal)
        {
            var constants = ctx.Map.GameAIConstants;
            var verdict = new DealVerdict();
            foreach (var item in deal.Give) verdict.Receive += ItemValue(ctx, ctx.Proposer, item);
            foreach (var item in deal.Get) verdict.Give += ItemValue(ctx, ctx.Rival, item);

            var endsWar = deal.Give.Concat(deal.Get).Any(i => i != null && i.kind == "endWar");
            if (endsWar)
            {
                var warCost = ctx.WarCost(ctx.Rival, ctx.Proposer);
                var hostility = ctx.Map.Diplomacy.Hostility(ctx.Rival, ctx.Proposer);
                var penalty = hostility / constants.hostilityMax * constants.peaceHostilityPenaltyValue;
                var floor = -constants.peacePriceCapFraction * ctx.ScoreOf(ctx.Rival).Total;
                verdict.PeaceWorth = Math.Max(warCost - penalty, floor);
                if (warCost > 0f) verdict.Factors.Add(new DealFactor { Text = "The war is costing them", Helps = true });
                if (penalty > 0f) verdict.Factors.Add(new DealFactor { Text = "They are angry with you", Helps = false });
            }

            foreach (var item in deal.Give)
            {
                var text = item != null ? Describe(item) : null;
                if (text != null) verdict.Factors.Add(new DealFactor { Text = "They want " + text, Helps = true });
            }
            foreach (var item in deal.Get)
            {
                var text = item != null ? Describe(item) : null;
                if (text != null) verdict.Factors.Add(new DealFactor { Text = "They would give up " + text, Helps = false });
            }

            verdict.TangibleDelta = verdict.Receive - verdict.Give;
            verdict.Net = verdict.Receive - verdict.Give + verdict.PeaceWorth;
            verdict.Band = BandOf(verdict.Net, constants.dealMeterScale);
            verdict.Accept = DealDecision.Accepts(verdict);
            return verdict;
        }
    }
}
