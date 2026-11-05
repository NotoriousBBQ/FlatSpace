using System;
using System.Collections.Generic;
using System.Linq;
using Flatspace.Objects.Production;

namespace FlatSpace.AI
{
    /// <summary>
    /// Validates and applies an accepted deal. Give items go from ctx.Proposer to ctx.Rival, Get items from ctx.Rival to ctx.Proposer.
    /// Pure: no Gameboard.Instance, so a self-check drives it directly.
    /// </summary>
    public static class DealExecutor
    {
        public const string EmptyDeal = "EmptyDeal";
        public const string NotOwned = "NotOwned";
        public const string LastPlanet = "LastPlanet";
        public const string Capital = "Capital";
        public const string TechUnavailable = "TechUnavailable";
        public const string NotAtWar = "NotAtWar";
        public const string UnknownKind = "UnknownKind";
        public const string DuplicateItem = "DuplicateItem";
        public const string UnknownThirdParty = "UnknownThirdParty";
        public const string AlreadyAtWar = "AlreadyAtWar";
        public const string NotAtWarWithThem = "NotAtWarWithThem";
        public const string Truce = "Truce";

        private static bool TryKind(DealItem item, out Ship.ShipKind kind)
        {
            kind = default;
            return !string.IsNullOrEmpty(item.name)
                   && Enum.TryParse(item.name, true, out kind)
                   && Enum.IsDefined(typeof(Ship.ShipKind), kind);
        }

        private static IEnumerable<(DealItem item, int giver, int receiver)> Sides(DealContext ctx, Deal deal)
        {
            foreach (var item in deal.Give) yield return (item, ctx.Proposer, ctx.Rival);
            foreach (var item in deal.Get) yield return (item, ctx.Rival, ctx.Proposer);
        }

        /// <summary>
        /// Null when every item can be applied now, else the first closed reason: EmptyDeal, NotOwned, LastPlanet, Capital,
        /// TechUnavailable (the giver lacks the tech or the receiver already has it), NotAtWar, UnknownKind (a bad fleet kind, a kind
        /// other than WarShip or SiegeShip (colony ships and bunkers are not tradable), or a bad item kind), DuplicateItem (the same planet, tech or
        /// planet+kind fleet twice on one side). capitalOf maps a player id to its capital planet name (null for none).
        /// </summary>
        public static string Validate(DealContext ctx, Deal deal, Func<int, string> capitalOf)
        {
            if (capitalOf == null) throw new ArgumentNullException(nameof(capitalOf));
            var items = Sides(ctx, deal).ToList();
            if (items.Count == 0) return EmptyDeal;

            var planetsGiven = new HashSet<(int giver, string planet)>();
            var seen = new HashSet<(int giver, string kind, string name, string planet)>();
            foreach (var (item, giver, receiver) in items)
            {
                if (item == null) return UnknownKind;
                switch (item.kind)
                {
                    case "planet":
                    {
                        var planet = item.name != null ? ctx.Map.GetPlanet(item.name) : null;
                        if (planet == null || planet.Owner != giver) return NotOwned;
                        if (!planetsGiven.Add((giver, planet.PlanetName))) return DuplicateItem;
                        break;
                    }
                    case "fleet":
                    {
                        if (!TryKind(item, out var kind) || !Ship.IsFightingKind(kind)) return UnknownKind;
                        var planet = item.planet != null ? ctx.Map.GetPlanet(item.planet) : null;
                        if (planet == null || item.count <= 0) return NotOwned;
                        if (!seen.Add((giver, "fleet", kind.ToString(), planet.PlanetName))) return DuplicateItem;
                        if (planet.DockedShips.Count(s => s.Kind == kind && s.Owner == giver) < item.count) return NotOwned;
                        break;
                    }
                    case "tech":
                    {
                        if (!seen.Add((giver, "tech", item.name, null))) return DuplicateItem;
                        var giverHas = (ctx.Research?.Invoke(giver) ?? Enumerable.Empty<CatalogItem>())
                            .Any(i => i != null && i.itemName == item.name && i.researched);
                        if (!giverHas || DealEvaluator.MissingChain(ctx.Research?.Invoke(receiver), item.name).Count == 0)
                            return TechUnavailable;
                        break;
                    }
                    case "endWar":
                        if (!seen.Add((giver, "endWar", null, null))) return DuplicateItem;
                        if (!ctx.Map.Diplomacy.IsAtWar(ctx.Proposer, ctx.Rival, true)) return NotAtWar;
                        break;
                    case "startWar":
                    case "endWarWith":
                    {
                        var askRefusal = ValidateAsk(ctx, item, giver, seen);
                        if (askRefusal != null) return askRefusal;
                        break;
                    }
                    default:
                        return UnknownKind;
                }
            }

            // Planet rules last, after ownership: not the giver's last populated planet (every planet of this deal counts as gone), not its capital.
            foreach (var (giver, name) in planetsGiven)
            {
                var remaining = ctx.Map.PlanetList.Count(p => p.Owner == giver && p.Population.Count > 0
                                                              && !planetsGiven.Contains((giver, p.PlanetName)));
                if (remaining == 0) return LastPlanet;
                if (capitalOf(giver) == name) return Capital;
            }
            return null;
        }

        // An ask is legal only in Get (the rival is the giver) and names a third player both sides have met. A war ask is tested
        // against the rival's OWN stance; a peace ask against EFFECTIVE war (either side's War, no truce), because accepting it
        // sets both stances to Peace. Duplicate: the same third player asked twice, whatever the kinds.
        private static string ValidateAsk(DealContext ctx, DealItem item, int giver,
            HashSet<(int giver, string kind, string name, string planet)> seen)
        {
            if (giver != ctx.Rival) return UnknownKind;
            var x = DealEvaluator.AskTarget(item);
            if (x < 0 || x == ctx.Proposer || x == ctx.Rival) return UnknownThirdParty;
            var met = ctx.Met != null ? ctx.Met(x) : ctx.Map.Knowledge.ContactPlayers(ctx.Map, ctx.Proposer).Contains(x);
            if (!met || !ctx.Map.Knowledge.ContactPlayers(ctx.Map, ctx.Rival).Contains(x)) return UnknownThirdParty;
            if (!seen.Add((giver, "ask", x.ToString(), null))) return DuplicateItem;
            var diplomacy = ctx.Map.Diplomacy;
            if (item.kind == "startWar")
            {
                if (diplomacy.StanceToward(ctx.Rival, x) == Stance.War) return AlreadyAtWar;
                if (diplomacy.InTruce(ctx.Rival, x, diplomacy.Turn)) return Truce;
            }
            else if (!diplomacy.IsAtWar(ctx.Rival, x, true)) return NotAtWarWithThem;   // peace ask: effective war (either side's War, no truce)
            return null;
        }

        /// <summary>
        /// Inhabitants of `giver` that must convert to `receiver` for the receiver to hold the most population (a tie owns nothing).
        /// populationByPlayer is indexed by player id. Stops when the giver has none left.
        /// </summary>
        public static int ConversionsNeeded(IList<int> populationByPlayer, int giver, int receiver)
        {
            var counts = populationByPlayer.ToArray();
            var converted = 0;
            while (!ReceiverLeads(counts, receiver) && counts[giver] > 0)
            {
                counts[giver]--;
                counts[receiver]++;
                converted++;
            }
            return converted;
        }

        private static bool ReceiverLeads(int[] counts, int receiver)
        {
            for (var i = 0; i < counts.Length; i++)
                if (i != receiver && counts[i] >= counts[receiver]) return false;
            return counts[receiver] > 0;
        }

        /// <summary>
        /// Applies a deal. Re-runs Validate first and throws InvalidOperationException (applying nothing) when it fails. capitalOf is the
        /// same delegate Validate takes and is required, so the capital rule can never be skipped (null throws ArgumentNullException).
        /// askResults, when given, receives one (kind, player, target, changed) per stance an ask set: a war ask reports the rival's
        /// stance toward X; a peace ask reports two entries (the rival toward X, then X toward the rival). changed is whether it really changed.
        /// </summary>
        public static void Execute(DealContext ctx, Deal deal, int turn, GameAIConstants constants, Func<int, PlayerAI> aiOf,
            Func<int, string> capitalOf, List<(string kind, int player, int target, bool changed)> askResults = null)
        {
            if (capitalOf == null) throw new ArgumentNullException(nameof(capitalOf));
            var refusal = Validate(ctx, deal, capitalOf);
            if (refusal != null) throw new InvalidOperationException("Deal is not valid: " + refusal);

            foreach (var (item, giver, receiver) in Sides(ctx, deal).ToList())
            {
                switch (item.kind)
                {
                    case "planet":
                    {
                        var planet = ctx.Map.GetPlanet(item.name);
                        while (planet.PlayerWithMostPopulation() != receiver && planet.Population.Any(i => i.Player == giver))
                            planet.ConvertInhabitant(giver, receiver);
                        break;
                    }
                    case "fleet":
                    {
                        TryKind(item, out var kind);
                        var planet = ctx.Map.GetPlanet(item.planet);
                        var ships = planet.DockedShips
                            .Where(s => s.Kind == kind && s.Owner == giver)
                            .OrderBy(s => s.Damage)   // healthiest first
                            .Take(item.count)
                            .ToList();
                        planet.TransferShips(ships, receiver);
                        break;
                    }
                    case "tech":
                        aiOf(receiver).GrantTech(item.name);
                        break;
                    case "startWar":
                    case "endWarWith":
                        // A war ask changes only the rival's OWN stance toward the third player; a peace ask sets BOTH stances of
                        // the pair to Peace (no truce). Validate guaranteed a legal ask.
                    {
                        var x = DealEvaluator.AskTarget(item);
                        var war = item.kind == "startWar";
                        var type = war ? GameAI.GameAIOrder.OrderType.OrderTypeDeclareWar : GameAI.GameAIOrder.OrderType.OrderTypeMakePeace;
                        var changed = GameAI.ApplyStanceOrder(ctx.Map.Diplomacy, new GameAI.GameAIOrder
                        {
                            Type = type,
                            TimingType = GameAI.GameAIOrder.OrderTimingType.OrderTimingTypeImmediate,
                            PlayerId = ctx.Rival,
                            Data = x,
                        }, turn);
                        askResults?.Add((item.kind, ctx.Rival, x, changed));
                        if (!war)
                        {
                            var changedOther = GameAI.ApplyStanceOrder(ctx.Map.Diplomacy, new GameAI.GameAIOrder
                            {
                                Type = type,
                                TimingType = GameAI.GameAIOrder.OrderTimingType.OrderTimingTypeImmediate,
                                PlayerId = x,
                                Data = ctx.Rival,
                            }, turn);
                            askResults?.Add((item.kind, x, ctx.Rival, changedOther));
                        }
                        break;
                    }
                    case "endWar":
                    {
                        var order = new GameAI.GameAIOrder
                        {
                            Type = GameAI.GameAIOrder.OrderType.OrderTypeSurrender,
                            TimingType = GameAI.GameAIOrder.OrderTimingType.OrderTimingTypeImmediate,
                            PlayerId = ctx.Proposer,
                            Data = ctx.Rival,
                        };
                        GameAI.ApplySurrender(ctx.Map.Diplomacy, order, turn, constants.dealTruceTurns);
                        break;
                    }
                }
            }
        }
    }
}
