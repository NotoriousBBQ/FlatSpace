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
        /// <summary>Strength(observer, subject): the warship strength `observer` can see of `subject` (observer == subject = its own full strength). May be null: every strength term is then 0.</summary>
        public Func<int, int, float> Strength;
        /// <summary>True when the proposer has met that player. May be null (the validator then falls back to the AI's contact set).</summary>
        public Func<int, bool> Met;
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

        /// <summary>True for the asks of the rival: "startWar" and "endWarWith".</summary>
        public static bool IsAsk(DealItem item) => item != null && (item.kind == "startWar" || item.kind == "endWarWith");

        /// <summary>The third player's id an ask names, or -1 when it does not parse.</summary>
        public static int AskTarget(DealItem item)
            => item != null && int.TryParse(item.name, NumberStyles.Integer, CultureInfo.InvariantCulture, out var id) ? id : -1;

        // log2 of (rival's view of `subject`'s strength / its own), clamped to +-2, times thirdPartyStrengthValue; 0 when unknown.
        private static float StrengthTerm(DealContext ctx, int subject, GameAIConstants constants)
        {
            if (ctx.Strength == null) return 0f;
            var own = ctx.Strength(ctx.Rival, ctx.Rival);
            var seen = ctx.Strength(ctx.Rival, subject);
            if (own <= 0f || seen <= 0f) return 0f;
            var log = (float)Math.Log(seen / own, 2.0);
            return Math.Max(-2f, Math.Min(2f, log)) * constants.thirdPartyStrengthValue;
        }

        private static void AddAskFactor(DealVerdict verdict, string kind, int third, string term, float value)
        {
            if (Math.Abs(value) < 1e-6f) return;
            verdict.Factors.Add(new DealFactor { Text = AskFactorText(kind, term, value > 0f, "player " + third.ToString(CultureInfo.InvariantCulture)), Helps = value > 0f });
        }

        /// <summary>
        /// The reason line for one term of an ask. term: "hostility", "proposer", "target" or "losses"; positive = it pushes toward
        /// acceptance. A plain sentence with no numbers, distinct per (kind, term, sign).
        /// </summary>
        public static string AskFactorText(string kind, string term, bool positive, string label)
        {
            var war = kind == "startWar";
            switch (term)
            {
                case "hostility":
                    if (war) return positive ? "They already hate " + label : "They have no quarrel with " + label;
                    return positive ? "They would rather be at peace with " + label : "They want " + label + " beaten";
                case "proposer":
                    if (war) return positive ? "They think you are strong enough to back them against " + label : "They think you are too weak to back them against " + label;
                    return positive ? "They think you are strong enough to mediate with " + label : "They think you are too weak to mediate with " + label;
                case "target":
                    if (war) return positive ? "They think " + label + " is weak" : "They think " + label + " is too strong to fight";
                    return positive ? "They fear the strength of " + label : "They think " + label + " is weak and a peace gains them nothing";
                default:
                    return war
                        ? (positive ? "They have gained from fighting " + label : "Fighting " + label + " has been costly for them")
                        : (positive ? "Their war with " + label + " is costing them" : "Their war with " + label + " has gone well for them");
            }
        }

        public static DealVerdict Evaluate(DealContext ctx, Deal deal)
        {
            var constants = ctx.Map.GameAIConstants;
            var verdict = new DealVerdict();
            foreach (var item in deal.Give) verdict.Receive += ItemValue(ctx, ctx.Proposer, item);
            foreach (var item in deal.Get) verdict.Give += ItemValue(ctx, ctx.Rival, item);

            var combinedWorth = 0f;
            var endsWar = deal.Give.Concat(deal.Get).Any(i => i != null && i.kind == "endWar");
            var hasAsk = deal.Get.Any(i => i != null && IsAsk(i));
            if (endsWar || hasAsk)
            {
                var penalty = ctx.Map.Diplomacy.Hostility(ctx.Rival, ctx.Proposer) / constants.hostilityMax * constants.peaceHostilityPenaltyValue;
                var floor = -constants.peacePriceCapFraction * ctx.ScoreOf(ctx.Rival).Total;
                var warCost = endsWar ? ctx.WarCost(ctx.Rival, ctx.Proposer) : 0f;

                foreach (var item in deal.Get)
                {
                    if (item == null || !IsAsk(item)) continue;
                    var x = AskTarget(item);
                    if (x < 0) continue;
                    var war = item.kind == "startWar";
                    var sign = war ? 1f : -1f;   // a peace ask is the mirror of a war ask on the hostility and X-strength terms

                    var pWar = StanceMatrix.WarProbability(ctx.Map.Diplomacy.Hostility(ctx.Rival, x), constants);
                    var hostilityTerm = sign * (pWar - 0.5f) * constants.warAskHostilityValue;
                    var proposerTerm = StrengthTerm(ctx, ctx.Proposer, constants);
                    var targetTerm = -sign * StrengthTerm(ctx, x, constants);
                    var lossTerm = -sign * ctx.WarCost(ctx.Rival, x);

                    var total = hostilityTerm + proposerTerm + targetTerm + lossTerm;
                    verdict.ThirdPartyWorth += total;
                    verdict.AskTerms.Add(new AskTerm
                    {
                        kind = item.kind, third = x, worth = total,
                        host = hostilityTerm, proposerStrength = proposerTerm, thirdStrength = targetTerm, loss = lossTerm,
                    });
                    if (!war && ctx.Map.Diplomacy.StanceToward(x, ctx.Rival) == Stance.War)
                        verdict.Factors.Add(new DealFactor { Text = "Player " + x.ToString(CultureInfo.InvariantCulture) + " is still at war with them; the fighting may go on", Helps = false });
                    AddAskFactor(verdict, item.kind, x, "hostility", hostilityTerm);
                    AddAskFactor(verdict, item.kind, x, "proposer", proposerTerm);
                    AddAskFactor(verdict, item.kind, x, "target", targetTerm);
                    AddAskFactor(verdict, item.kind, x, "losses", lossTerm);
                }

                var combined = Math.Max(warCost + verdict.ThirdPartyWorth - penalty, floor);
                combinedWorth = combined;
                verdict.PeaceWorth = combined - verdict.ThirdPartyWorth;
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
            verdict.Net = verdict.Receive - verdict.Give + combinedWorth;   // not PeaceWorth + ThirdPartyWorth: that sum can round off by 1e-6
            verdict.Band = BandOf(verdict.Net, constants.dealMeterScale);
            verdict.Accept = DealDecision.Accepts(verdict);
            return verdict;
        }
    }
}
