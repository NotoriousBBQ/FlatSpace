using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using FlatSpace.AI;
using Flatspace.Objects.Production;
using System.Linq;
using FlatSpace.Human;

public static class DealSelfCheck
{
    [MenuItem("FlatSpace/AI/Run Deal Self-Check")]
    public static void Run() => RunChecks();

    public static bool RunChecks()
    {
        var ok = RunConstantsCheck();
        ok &= RunWarCostChecks();
        ok &= RunEvaluatorChecks();
        ok &= RunFleetValueChecks();
        ok &= RunExecutorChecks();
        ok &= RunProposeChecks();
        Debug.Log(ok
            ? "[DealSelfCheck] ALL PASSED"
            : "[DealSelfCheck] FAILURES (see errors above)");
        return ok;
    }

    private static bool Check(bool condition, string label)
    {
        if (!condition) Debug.LogError($"[DealSelfCheck] FAIL: {label}");
        return condition;
    }

    private static bool Near(float a, float b) => Mathf.Abs(a - b) < 0.001f;

    public static bool RunConstantsCheck()
    {
        var c = ScriptableObject.CreateInstance<GameAIConstants>();
        var ok = Check(c.dealTruceTurns == 20, "dealTruceTurns defaults to 20");
        ok &= Check(Near(c.dealHostilityPerValue, 1f), "dealHostilityPerValue defaults to 1");
        ok &= Check(Near(c.dealHostilityCap, 10f), "dealHostilityCap defaults to 10");
        ok &= Check(Near(c.peacePriceCapFraction, 0.5f), "peacePriceCapFraction defaults to 0.5");
        ok &= Check(Near(c.peaceHostilityPenaltyValue, 50f), "peaceHostilityPenaltyValue defaults to 50");
        ok &= Check(Near(c.dealMeterScale, 10f), "dealMeterScale defaults to 10");
        Object.DestroyImmediate(c);
        return ok;
    }

    private static PlanetSpawnData MakeSpawn(string name, float value, float x, List<Object> junk)
    {
        var resourceData = ScriptableObject.CreateInstance<PlanetResourceData>();
        resourceData._initialPopulation = 0;
        resourceData._maxPopulation = 5;
        resourceData._planetValue = value;
        var spawn = ScriptableObject.CreateInstance<PlanetSpawnData>();
        spawn._planetName = name;
        spawn._planetPosition = new Vector3(x, 0f, 0f);   // distinct positions (see PathingSystem.FindPath)
        spawn._planetType = Planet.PlanetType.PlanetTypeNormal;
        spawn._resourceData = resourceData;
        spawn._connections = new List<string>();
        junk.Add(resourceData);
        junk.Add(spawn);
        return spawn;
    }

    private static Planet.UpdateResult Flip(string planet, int victim, int newOwner)
        => new Planet.UpdateResult(planet, Planet.UpdateResult.UpdateResultType.UpdateResultTypeConversion,
            new ConversionEvent { OwnerChanged = true, OldOwner = victim, NewOwner = newOwner, From = victim, Dominator = newOwner }, victim);

    public static bool RunWarCostChecks()
    {
        var ok = true;
        var junk = new List<Object>();
        var mapGo = new GameObject("DealSelfCheckMap");
        var playerGo = new GameObject("DealSelfCheckPlayer");
        try
        {
            var map = mapGo.AddComponent<GameAIMap>();
            var constants = ScriptableObject.CreateInstance<GameAIConstants>();
            junk.Add(constants);
            map.GameAIMapInit(new List<PlanetSpawnData> { MakeSpawn("Lost", 20f, 0f, junk), MakeSpawn("Other", 20f, 100f, junk) }, constants);

            var player = playerGo.AddComponent<Player>();
            var ai = playerGo.AddComponent<PlayerAI>();
            ai.Player = player;
            ai.AIMap = map;
            player.playerID = 0;

            var window = constants.lossWindowTurns;
            ok &= Check(Near(ai.WarCostToward(1, 5, 30f), 0f), "no records: war cost is 0");

            var results = new List<Planet.UpdateResult>
            {
                new Planet.UpdateResult("Lost", Planet.UpdateResult.UpdateResultType.UpdateResultTypeWarshipsLost,
                    new CombatLoss { Attacker = 1, Ships = 1f, StrengthDrop = 5f, Engaged = 10f }, 0),
                Flip("Lost", 0, 1),
                Flip("Other", 2, 1),   // another victim: not mine, ignored
            };
            ai.RecordLosses(results, 5);

            // loss share 5 / 10 = 0.5, x fleet value 30 = 15, plus the planet worth 20
            ok &= Check(Near(ai.WarCostToward(1, 5, 30f), 35f), "war cost toward player 1 is 0.5 x 30 + 20 = 35");
            ok &= Check(Near(ai.WarCostToward(2, 5, 30f), 0f), "another attacker costs nothing");
            ok &= Check(Near(ai.WarCostToward(1, 5, 0f), 20f), "with no fleet only the planet counts");
            ok &= Check(Near(ai.WarCostToward(1, 5 + window - 1, 30f), 35f), "still inside the window one turn before it ends");
            ok &= Check(Near(ai.WarCostToward(1, 5 + window, 30f), 0f), "entries older than lossWindowTurns drop out");
            ok &= Check(Near(ai.WarCostToward(1, 5 + window, 30f), 0f), "pruning stays pruned");
        }
        finally
        {
            Object.DestroyImmediate(playerGo);
            Object.DestroyImmediate(mapGo);
            foreach (var o in junk) if (o != null) Object.DestroyImmediate(o);
        }
        return ok;
    }

    private static CatalogItem Tech(string name, string requires, float cost, bool researched)
    {
        var item = ScriptableObject.CreateInstance<CatalogItem>();
        item.itemName = name;
        item.requiredTech = requires;
        item.cost = cost;
        item.researched = researched;
        return item;
    }

    public static bool RunEvaluatorChecks()
    {
        var ok = true;
        var junk = new List<Object>();
        var mapGo = new GameObject("DealSelfCheckEvalMap");
        try
        {
            var map = mapGo.AddComponent<GameAIMap>();
            var constants = ScriptableObject.CreateInstance<GameAIConstants>();
            junk.Add(constants);
            map.GameAIMapInit(new List<PlanetSpawnData> { MakeSpawn("Big", 20f, 0f, junk), MakeSpawn("Small", 5f, 100f, junk) }, constants);

            var a = Tech("A", "", 100f, false);
            var b = Tech("B", "A", 200f, false);
            var c = Tech("C", "B", 400f, false);
            junk.Add(a); junk.Add(b); junk.Add(c);
            var rivalHas = new List<CatalogItem> { a, b, c };
            var warCost = 0f;
            var rivalScore = 1000f;
            var ctx = new DealContext
            {
                Map = map, Proposer = 0, Rival = 1,
                Settings = new PlayerValue.ValueSettings { TechDivisor = 1f, FleetDivisor = 1f },
                Stats = new WarshipStats(null),
                ScoreOf = p => new PlayerValue.ScoreBreakdown { Planets = rivalScore },
                WarCost = (victim, attacker) => warCost,
                Research = p => p == 1 ? rivalHas : new List<CatalogItem>(),
            };
            System.Func<string, DealItem> planet = n => new DealItem { kind = "planet", name = n };
            var endWar = new DealItem { kind = "endWar" };

            // a gift of a planet worth 20 for nothing
            var gift = new Deal(); gift.Give.Add(planet("Big"));
            var v = DealEvaluator.Evaluate(ctx, gift);
            ok &= Check(Near(v.Receive, 20f) && Near(v.Net, 20f) && v.Accept, "gift of 20: Receive 20, Net 20, Accept");

            // asking a planet worth 20 for a gift worth 5
            var ask = new Deal(); ask.Give.Add(planet("Small")); ask.Get.Add(planet("Big"));
            v = DealEvaluator.Evaluate(ctx, ask);
            ok &= Check(Near(v.Net, -15f) && !v.Accept, "ask 20 for 5: Net -15, declined");
            ok &= Check(v.Band == DealBand.Unlikely && DealEvaluator.BandOf(-15f, 10f) == DealBand.Unlikely, "-15 at scale 10 is Unlikely");
            ok &= Check(v.Factors.Any(f => f.Text == "They want Small" && f.Helps)
                        && v.Factors.Any(f => f.Text == "They would give up Big" && !f.Helps), "item factors");

            ok &= Check(DealEvaluator.BandOf(-25f, 10f) == DealBand.VeryUnlikely, "band -25 VeryUnlikely");
            ok &= Check(DealEvaluator.BandOf(-10f, 10f) == DealBand.Unlikely, "band -10 Unlikely");
            ok &= Check(DealEvaluator.BandOf(-3f, 10f) == DealBand.Uncertain, "band -3 Uncertain");
            ok &= Check(DealEvaluator.BandOf(0f, 10f) == DealBand.Likely, "band 0 Likely");
            ok &= Check(DealEvaluator.BandOf(25f, 10f) == DealBand.VeryLikely, "band 25 VeryLikely");

            // end the war: war cost 30, hostility 50 / 100 -> penalty 25
            var peace = new Deal(); peace.Get.Add(endWar);
            warCost = 30f;
            var pair = map.Diplomacy.Get(1, 0); pair.Hostility = 50f; map.Diplomacy.Set(1, 0, pair);
            v = DealEvaluator.Evaluate(ctx, peace);
            ok &= Check(Near(v.PeaceWorth, 5f) && Near(v.Net, 5f) && v.Accept, "peace: war cost 30, penalty 25 -> PeaceWorth 5, Accept");
            ok &= Check(v.Factors.Any(f => f.Text == "The war is costing them" && f.Helps), "war-cost factor helps");
            ok &= Check(v.Factors.Any(f => f.Text == "They are angry with you" && !f.Helps), "angry factor hurts");

            pair.Hostility = 100f; map.Diplomacy.Set(1, 0, pair);
            v = DealEvaluator.Evaluate(ctx, peace);
            ok &= Check(Near(v.PeaceWorth, -20f) && !v.Accept, "peace at hostility 100: PeaceWorth -20, declined");

            // the floor: rival score 40 -> -0.5 x 40 = -20 however angry
            rivalScore = 40f; warCost = 0f;
            constants.peaceHostilityPenaltyValue = 500f;
            v = DealEvaluator.Evaluate(ctx, peace);
            ok &= Check(Near(v.PeaceWorth, -20f), "peace worth floored at -0.5 x rival score");
            constants.peaceHostilityPenaltyValue = 50f;

            // an exact tie accepts
            ok &= Check(DealDecision.Accepts(new DealVerdict { Net = 0f }), "Net == 0 accepts");
            ok &= Check(!DealDecision.Accepts(new DealVerdict { Net = -0.01f }), "Net < 0 declines");
            ok &= Check(DealDecision.Accepts(new DealVerdict { Net = 0.01f }), "Net > 0 accepts");
            ok &= Check(!DealDecision.Accepts(new DealVerdict { Net = 5f, Refusal = "Invalid" }), "a refusal never accepts");

            // tech: the receiver's missing chain (the proposer gives C; the rival has A, B and C unresearched)
            var techGift = new Deal(); techGift.Give.Add(new DealItem { kind = "tech", name = "C" });
            v = DealEvaluator.Evaluate(ctx, techGift);
            ok &= Check(Near(v.Receive, 700f), "tech value is the receiver's missing chain A+B+C = 700");
            b.researched = true;
            ok &= Check(DealEvaluator.MissingChain(rivalHas, "C").Select(i => i.itemName).SequenceEqual(new[] { "C" }), "chain stops at a researched item");
            ok &= Check(DealEvaluator.MissingChain(rivalHas, "B").Count == 0, "researched tech has an empty chain");
            ok &= Check(DealEvaluator.MissingChain(rivalHas, "Nope").Count == 0, "unknown tech has an empty chain");
        }
        finally
        {
            Object.DestroyImmediate(mapGo);
            foreach (var o in junk) if (o != null) Object.DestroyImmediate(o);
        }
        return ok;
    }

    public static bool RunFleetValueChecks()
    {
        var ok = true;
        var junk = new List<Object>();
        var mapGo = new GameObject("DealSelfCheckFleetMap");
        try
        {
            var map = mapGo.AddComponent<GameAIMap>();
            var constants = ScriptableObject.CreateInstance<GameAIConstants>();
            junk.Add(constants);
            map.GameAIMapInit(new List<PlanetSpawnData> { MakeSpawn("Dock", 10f, 0f, junk), MakeSpawn("Far", 10f, 100f, junk) }, constants);
            var dock = map.GetPlanet("Dock");

            // Null templates: health fraction is 1 whatever the damage, so ships differ by research snapshot length.
            // Warship value = 100 x (1 + 0.1 x snapshot size): 0 -> 100, 1 -> 110, 3 -> 130, 5 -> 150.
            // Healthiest first = lowest Damage (the rule HumanCommandProcessor.HandleMove uses: OrderBy(Damage)).
            // The dock order is deliberately not the damage order, so dropping the OrderBy(Damage) would fail the sums.
            dock.DockShipFromSave(Ship.ShipKind.WarShip, 0, new List<string> { "a" }, 20f);              // 110
            dock.DockShipFromSave(Ship.ShipKind.WarShip, 0, new List<string> { "a", "b", "c" }, 0f);    // healthiest, 130
            dock.DockShipFromSave(Ship.ShipKind.WarShip, 0, new List<string>(), 10f);                    // 100
            dock.DockShipFromSave(Ship.ShipKind.WarShip, 1, new List<string> { "a", "b", "c", "d", "e" }, 0f);   // another owner: never counts
            dock.DockShipFromSave(Ship.ShipKind.SiegeShip, 0, new List<string>(), 0f);               // another kind: never counts

            var ctx = new DealContext
            {
                Map = map, Proposer = 0, Rival = 1,
                Settings = new PlayerValue.ValueSettings { BaseWarshipCost = 100f, ImprovementFactor = 0.1f, FleetDivisor = 1f, TechDivisor = 1f },
                Stats = new WarshipStats(null),
                ScoreOf = p => new PlayerValue.ScoreBreakdown(),
                WarCost = (victim, attacker) => 0f,
                Research = p => null,
            };
            System.Func<string, int, DealItem> fleet = (kind, n) => new DealItem { kind = "fleet", name = kind, planet = "Dock", count = n };

            ok &= Check(Near(DealEvaluator.ItemValue(ctx, 0, fleet("WarShip", 2)), 230f), "fleet of 2: the two healthiest (130 + 100), not 110 + 100 or 130 + 110");
            ok &= Check(Near(DealEvaluator.ItemValue(ctx, 0, fleet("WarShip", 1)), 130f), "fleet of 1: the healthiest, 130");
            ok &= Check(Near(DealEvaluator.ItemValue(ctx, 0, fleet("WarShip", 9)), 340f), "count above the ships available values only the 3 owned: 340");
            ok &= Check(Near(DealEvaluator.ItemValue(ctx, 1, fleet("WarShip", 9)), 150f), "the other owner's fleet is only its own ship: 150");
            ok &= Check(Near(DealEvaluator.ItemValue(ctx, 0, fleet("Bogus", 2)), 0f), "an unparseable kind values 0");
            ok &= Check(Near(DealEvaluator.ItemValue(ctx, 0, fleet("WarShip", 0)), 0f), "count 0 values 0");

            // Get-side tech: the rival gives it, so the receiver is the PROPOSER and the chain is the proposer's missing items.
            var p1 = Tech("P1", "", 30f, false);
            var p2 = Tech("P2", "P1", 70f, false);
            var r1 = Tech("R1", "", 999f, false);
            junk.Add(p1); junk.Add(p2); junk.Add(r1);
            var proposerHas = new List<CatalogItem> { p1, p2 };
            var rivalHas = new List<CatalogItem> { r1 };
            ctx.Research = p => p == 0 ? proposerHas : p == 1 ? rivalHas : null;
            var deal = new Deal();
            deal.Get.Add(new DealItem { kind = "tech", name = "P2" });
            var v = DealEvaluator.Evaluate(ctx, deal);
            ok &= Check(Near(v.Give, 100f), "Get-side tech P2 is the proposer's missing chain P1 + P2 = 100");
            p1.researched = true;
            ok &= Check(Near(DealEvaluator.ItemValue(ctx, 1, deal.Get[0]), 70f), "with P1 researched only P2 is missing: 70");
        }
        finally
        {
            Object.DestroyImmediate(mapGo);
            foreach (var o in junk) if (o != null) Object.DestroyImmediate(o);
        }
        return ok;
    }

    private static void SetPop(Planet planet, int owner, params int[] players)
    {
        planet.Population.Clear();
        foreach (var p in players) planet.Population.Add(new Planet.Inhabitant { Player = p });
        planet.Owner = owner;
    }

    private static int Count(Planet planet, int player) => planet.Population.Count(i => i.Player == player);

    public static bool RunExecutorChecks()
    {
        var ok = true;
        var junk = new List<Object>();
        var mapGo = new GameObject("DealSelfCheckExecMap");
        var aiGo = new GameObject("DealSelfCheckExecAI");
        try
        {
            // ConversionsNeeded: indexed by player id, giver 0, receiver 1, third 2.
            ok &= Check(DealExecutor.ConversionsNeeded(new[] { 5, 0, 0 }, 0, 1) == 3, "giver 5 alone: 3 conversions");
            ok &= Check(DealExecutor.ConversionsNeeded(new[] { 5, 0, 2 }, 0, 1) == 3, "giver 5 + third 2: 3 conversions");
            ok &= Check(DealExecutor.ConversionsNeeded(new[] { 5, 0, 4 }, 0, 1) == 5, "giver 5 + third 4: 5 conversions (a tie owns nothing)");
            ok &= Check(DealExecutor.ConversionsNeeded(new[] { 1, 0, 0 }, 0, 1) == 1, "one inhabitant: 1 conversion");

            var map = mapGo.AddComponent<GameAIMap>();
            var constants = ScriptableObject.CreateInstance<GameAIConstants>();
            junk.Add(constants);
            map.GameAIMapInit(new List<PlanetSpawnData>
            {
                MakeSpawn("A", 10f, 0f, junk), MakeSpawn("B", 10f, 100f, junk),
                MakeSpawn("C", 10f, 200f, junk), MakeSpawn("D", 10f, 300f, junk),
            }, constants);
            var a = map.GetPlanet("A"); var b = map.GetPlanet("B"); var c = map.GetPlanet("C"); var d = map.GetPlanet("D");
            SetPop(a, 0, 0, 0, 0, 0, 0);
            SetPop(b, 0, 0);
            SetPop(c, Planet.NoOwner);
            SetPop(d, 1, 1, 1);

            // tech: proposer (0) has T1..T3; receiver (1) has only T1
            var g1 = Tech("T1", "", 10f, true); var g2 = Tech("T2", "T1", 20f, true); var g3 = Tech("T3", "T2", 30f, true);
            var r1 = Tech("T1", "", 10f, true); var r2 = Tech("T2", "T1", 20f, false); var r3 = Tech("T3", "T2", 30f, false);
            var rx = Tech("X", "", 5f, false);
            var d1 = Tech("D1", "T1", 1f, false); var d2 = Tech("D2", "T2", 1f, false); var d3 = Tech("D3", "T3", 1f, false);
            junk.AddRange(new Object[] { g1, g2, g3, r1, r2, r3, rx, d1, d2, d3 });
            var giverItems = new List<CatalogItem> { g1, g2, g3 };
            var ai1 = aiGo.AddComponent<PlayerAI>();
            ai1.Player = aiGo.AddComponent<Player>();
            ai1.Player.playerID = 1;
            ai1.AIMap = map;
            ai1.ResearchCatalog = aiGo.AddComponent<Catalog>();
            ai1.ResearchCatalog.catalogItems = new List<CatalogItem> { r1, r2, r3, rx };
            ai1.ProductionCatalog = aiGo.AddComponent<Catalog>();
            ai1.ProductionCatalog.catalogItems = new List<CatalogItem> { d1, d2, d3 };
            ai1.SetResearchQueue(new[] { "T2", "X", "T3" });
            ai1.currentResearch = r2;

            var ctx = new DealContext
            {
                Map = map, Proposer = 0, Rival = 1,
                Settings = new PlayerValue.ValueSettings { BaseWarshipCost = 100f, ImprovementFactor = 0.1f, FleetDivisor = 1f, TechDivisor = 1f },
                Stats = new WarshipStats(null),
                ScoreOf = p => new PlayerValue.ScoreBreakdown(),
                WarCost = (victim, attacker) => 0f,
                Research = p => p == 0 ? giverItems : p == 1 ? ai1.ResearchCatalog.catalogItems : null,
            };
            System.Func<int, string> noCapital = p => null;
            System.Func<int, PlayerAI> aiOf = p => p == 1 ? ai1 : null;
            System.Func<string, DealItem> planet = n => new DealItem { kind = "planet", name = n };

            // Validate refusals
            ok &= Check(DealExecutor.Validate(ctx, new Deal(), noCapital) == "EmptyDeal", "empty deal");
            var deal = new Deal(); deal.Give.Add(planet("D"));
            ok &= Check(DealExecutor.Validate(ctx, deal, noCapital) == "NotOwned", "a planet the proposer does not own");
            deal = new Deal(); deal.Give.Add(planet("A")); deal.Give.Add(planet("B"));
            ok &= Check(DealExecutor.Validate(ctx, deal, noCapital) == "LastPlanet", "giving both populated planets is the last planet");
            deal = new Deal(); deal.Get.Add(planet("D"));
            ok &= Check(DealExecutor.Validate(ctx, deal, noCapital) == "LastPlanet", "the rival only populated planet");
            deal = new Deal(); deal.Give.Add(planet("A"));
            ok &= Check(DealExecutor.Validate(ctx, deal, noCapital) == null, "giving one of two populated planets is valid");
            ok &= Check(DealExecutor.Validate(ctx, deal, p => p == 0 ? "A" : null) == "Capital", "the capital cannot be given");
            deal = new Deal(); deal.Give.Add(new DealItem { kind = "tech", name = "T1" });
            ok &= Check(DealExecutor.Validate(ctx, deal, noCapital) == "TechUnavailable", "a tech the receiver has");
            deal = new Deal(); deal.Get.Add(new DealItem { kind = "tech", name = "T3" });
            ok &= Check(DealExecutor.Validate(ctx, deal, noCapital) == "TechUnavailable", "a tech the giver (rival) lacks");
            deal = new Deal(); deal.Give.Add(new DealItem { kind = "endWar" });
            ok &= Check(DealExecutor.Validate(ctx, deal, noCapital) == "NotAtWar", "endWar while at peace");
            deal = new Deal(); deal.Give.Add(new DealItem { kind = "fleet", name = "Bogus", planet = "C", count = 1 });
            ok &= Check(DealExecutor.Validate(ctx, deal, noCapital) == "UnknownKind", "a bad fleet kind");
            deal.Give[0].name = "7";
            ok &= Check(DealExecutor.Validate(ctx, deal, noCapital) == "UnknownKind", "a numeric undefined fleet kind");
            deal.Give[0].name = "WarShip";
            ok &= Check(DealExecutor.Validate(ctx, deal, noCapital) == "NotOwned", "a fleet the giver does not have");

            // Planet gifts: the minimum conversions on a real planet
            deal = new Deal(); deal.Give.Add(planet("A"));
            SetPop(a, 0, 0, 0, 0, 0, 0);
            DealExecutor.Execute(ctx, deal, 1, constants, aiOf, noCapital);
            ok &= Check(a.Owner == 1 && Count(a, 0) == 2 && Count(a, 1) == 3, "giver 5 alone: owner flips after 3, giver keeps 2");
            SetPop(a, 0, 0, 0, 0, 0, 0, 2, 2);
            DealExecutor.Execute(ctx, deal, 1, constants, aiOf, noCapital);
            ok &= Check(a.Owner == 1 && Count(a, 0) == 2 && Count(a, 1) == 3 && Count(a, 2) == 2, "giver 5 + third 2: 3 conversions");
            SetPop(a, 0, 0, 0, 0, 0, 0, 2, 2, 2, 2);
            DealExecutor.Execute(ctx, deal, 1, constants, aiOf, noCapital);
            ok &= Check(a.Owner == 1 && Count(a, 0) == 0 && Count(a, 1) == 5 && Count(a, 2) == 4, "giver 5 + third 4: 5 conversions");
            SetPop(a, 0, 0);
            DealExecutor.Execute(ctx, deal, 1, constants, aiOf, noCapital);
            ok &= Check(a.Owner == 1 && Count(a, 1) == 1, "one inhabitant: 1 conversion");

            // Fleet gift: the two healthiest of three; the rival own ship, the damaged third and another kind stay
            c.DockShipFromSave(Ship.ShipKind.WarShip, 0, new List<string>(), 20f);
            c.DockShipFromSave(Ship.ShipKind.WarShip, 0, new List<string>(), 0f);
            c.DockShipFromSave(Ship.ShipKind.WarShip, 0, new List<string>(), 10f);
            c.DockShipFromSave(Ship.ShipKind.WarShip, 1, new List<string>(), 5f, 0);
            c.DockShipFromSave(Ship.ShipKind.SiegeShip, 0, new List<string>(), 0f);
            deal = new Deal(); deal.Give.Add(new DealItem { kind = "fleet", name = "WarShip", planet = "C", count = 2 });
            DealExecutor.Execute(ctx, deal, 1, constants, aiOf, noCapital);
            var mine = c.DockedShips.Where(s => s.Kind == Ship.ShipKind.WarShip && s.Owner == 0).ToList();
            var theirs = c.DockedShips.Where(s => s.Kind == Ship.ShipKind.WarShip && s.Owner == 1).ToList();
            ok &= Check(mine.Count == 1 && Near(mine[0].Damage, 20f), "the most damaged warship stays with the giver");
            ok &= Check(theirs.Count == 3, "the rival holds its own ship plus the 2 gifted");
            ok &= Check(theirs.Count(s => Near(s.Damage, 0f)) == 1 && theirs.Count(s => Near(s.Damage, 10f)) == 1 && theirs.Count(s => Near(s.Damage, 5f)) == 1,
                "the gifted ships are the damage 0 and damage 10 ones");
            ok &= Check(theirs.Where(s => !Near(s.Damage, 5f)).All(s => s.FleetId == 1), "the gifted ships share one fresh fleet id (1, above the rival fleet 0)");
            ok &= Check(c.DockedShips.Count(s => s.Kind == Ship.ShipKind.SiegeShip && s.Owner == 0) == 1, "another kind never moves");

            // Tech gift: T3 to a receiver with T1 only grants T3 and T2
            deal = new Deal(); deal.Give.Add(new DealItem { kind = "tech", name = "T3" });
            ok &= Check(DealExecutor.Validate(ctx, deal, noCapital) == null, "tech T3 is valid before the gift");
            DealExecutor.Execute(ctx, deal, 1, constants, aiOf, noCapital);
            ok &= Check(r2.researched && r3.researched && !rx.researched, "tiers 2 and 3 researched");
            ok &= Check(!d1.researched && d2.researched && d3.researched, "dependent production items of the granted tiers flipped");
            ok &= Check(ai1.ResearchQueue.SequenceEqual(new[] { "X" }), "granted tiers leave the queue");
            ok &= Check(ai1.currentResearch == null, "currentResearch cleared");
            ok &= Check(ai1.GrantTech("T3").Count == 0, "a tech already held grants nothing");
            ok &= Check(DealExecutor.Validate(ctx, deal, noCapital) == "TechUnavailable", "validate now refuses the held tech");

            // Execute applies nothing when the deal is invalid (the planet is valid, the tech is held)
            SetPop(a, 0, 0, 0, 0, 0, 0);
            deal = new Deal(); deal.Give.Add(planet("A")); deal.Give.Add(new DealItem { kind = "tech", name = "T3" });
            var threw = false;
            try { DealExecutor.Execute(ctx, deal, 1, constants, aiOf, noCapital); }
            catch (System.InvalidOperationException) { threw = true; }
            ok &= Check(threw, "Execute throws on an invalid deal");
            ok &= Check(a.Owner == 0 && Count(a, 0) == 5, "an invalid deal applied nothing");

            // Execute with the giver's capital: refused, nothing applied
            SetPop(a, 0, 0, 0, 0, 0, 0);
            deal = new Deal(); deal.Give.Add(planet("A"));
            threw = false;
            try { DealExecutor.Execute(ctx, deal, 1, constants, aiOf, p => p == 0 ? "A" : null); }
            catch (System.InvalidOperationException) { threw = true; }
            ok &= Check(threw && a.Owner == 0 && Count(a, 0) == 5, "Execute throws for the giver capital and applies nothing");
            threw = false;
            try { DealExecutor.Execute(ctx, deal, 1, constants, aiOf, null); }
            catch (System.ArgumentNullException) { threw = true; }
            ok &= Check(threw && a.Owner == 0, "a null capitalOf throws ArgumentNullException");

            // endWar
            map.Diplomacy.SetStance(0, 1, Stance.War, 1);
            deal = new Deal(); deal.Give.Add(new DealItem { kind = "endWar" });
            ok &= Check(DealExecutor.Validate(ctx, deal, noCapital) == null, "endWar valid while at war");
            DealExecutor.Execute(ctx, deal, 5, constants, aiOf, noCapital);
            ok &= Check(map.Diplomacy.StanceToward(0, 1) == Stance.Peace && map.Diplomacy.StanceToward(1, 0) == Stance.Peace, "endWar sets both stances to Peace");
            ok &= Check(map.Diplomacy.Get(0, 1).TruceUntil == 25 && map.Diplomacy.Get(1, 0).TruceUntil == 25, "truce until turn + 20 on both rows");
        }
        finally
        {
            Object.DestroyImmediate(aiGo);
            Object.DestroyImmediate(mapGo);
            foreach (var o in junk) if (o != null) Object.DestroyImmediate(o);
        }
        return ok;
    }

    // A: human, value 30 (its capital). B: human, 20. C: rival 1, 20. D: rival 1, 5. Two human warships at A, each worth 5.
    private sealed class DealScene : System.IDisposable
    {
        public GameAIMap Map;
        public GameAIConstants Constants;
        public PlayerAI Human;
        public HumanCommandProcessor Processor;
        public float WarCost;
        private readonly GameObject _mapGo, _playerGo;
        private readonly List<Object> _junk = new List<Object>();

        public DealScene(bool withContext = true)
        {
            _mapGo = new GameObject("DealSelfCheckProposeMap");
            _playerGo = new GameObject("DealSelfCheckProposePlayer");
            Map = _mapGo.AddComponent<GameAIMap>();
            Constants = ScriptableObject.CreateInstance<GameAIConstants>();
            _junk.Add(Constants);
            Map.GameAIMapInit(new List<PlanetSpawnData>
            {
                MakeSpawn("A", 30f, 0f, _junk), MakeSpawn("B", 20f, 100f, _junk),
                MakeSpawn("C", 20f, 200f, _junk), MakeSpawn("D", 5f, 300f, _junk),
            }, Constants);
            SetPop(Map.GetPlanet("A"), 0, 0, 0, 0, 0, 0);
            SetPop(Map.GetPlanet("B"), 0, 0, 0, 0, 0, 0);
            SetPop(Map.GetPlanet("C"), 1, 1, 1, 1);
            SetPop(Map.GetPlanet("D"), 1, 1, 1);
            for (var i = 0; i < 2; i++) Map.GetPlanet("A").DockShipFromSave(Ship.ShipKind.WarShip, 0, new List<string>());

            var player = _playerGo.AddComponent<Player>();
            Human = _playerGo.AddComponent<PlayerAI>();
            Human.Player = player;
            Human.AIMap = Map;
            player.playerID = 0;
            Map.Knowledge.SetKnownPlanets(0, new List<string> { "A", "B", "C", "D" });
            Map.Diplomacy.Enabled = true;
            Map.Diplomacy.Turn = 5;

            Processor = new HumanCommandProcessor(Map, Human, 5);
            if (withContext)
            {
                Processor.Deal = new DealContext
                {
                    Settings = new PlayerValue.ValueSettings { BaseWarshipCost = 5f, ImprovementFactor = 0.1f, FleetDivisor = 1f, TechDivisor = 1f },
                    Stats = new WarshipStats(null),
                    ScoreOf = p => new PlayerValue.ScoreBreakdown { Planets = 1000f },
                    WarCost = (victim, attacker) => WarCost,
                    Research = p => new List<CatalogItem>(),
                };
                Processor.CapitalOf = p => p == 0 ? "A" : null;
            }
        }

        public void SetHostility(float value)
        {
            var pair = Map.Diplomacy.Get(1, 0);
            pair.Hostility = value;
            Map.Diplomacy.Set(1, 0, pair);
        }

        public float Hostility => Map.Diplomacy.Hostility(1, 0);

        public HumanCommandResult Propose(List<DealItem> give, List<DealItem> get, int rival = 1)
            => Processor.Apply(new[] { new HumanCommand { type = "propose", rival = rival, dealGive = give, dealGet = get } },
                new List<GameAI.GameAIOrder>())[0];

        public void Dispose()
        {
            Object.DestroyImmediate(_playerGo);
            Object.DestroyImmediate(_mapGo);
            foreach (var o in _junk) if (o != null) Object.DestroyImmediate(o);
        }
    }

    private static DealItem PlanetItem(string name) => new DealItem { kind = "planet", name = name };
    private static DealItem WarshipItem(int count) => new DealItem { kind = "fleet", name = "WarShip", planet = "A", count = count };
    private static List<DealItem> Items(params DealItem[] items) => new List<DealItem>(items);

    public static bool RunProposeChecks()
    {
        var ok = true;
        var none = new List<DealItem>();
        var endWar = new DealItem { kind = "endWar" };

        // An accepted planet gift: the planet flips, hostility falls by min(cap 10, value 20) = 10 (50 -> 40).
        using (var s = new DealScene())
        {
            s.SetHostility(50f);
            var preview = s.Processor.Preview(new HumanCommand { type = "propose", rival = 1, dealGive = Items(PlanetItem("B")), dealGet = none });
            ok &= Check(preview.Refusal == null && preview.Accept && Near(preview.Net, 20f) && Near(preview.TangibleDelta, 20f), "preview of a gift of B: accepted, Net 20");
            ok &= Check(s.Map.GetPlanet("B").Owner == 0, "a preview changes nothing");
            ok &= Check(s.Processor.Check(new HumanCommand { type = "propose", rival = 1, dealGive = Items(PlanetItem("B")), dealGet = none }) == null, "Check allows a valid proposal");
            ok &= Check(s.Map.GetPlanet("B").Owner == 0 && Near(s.Hostility, 50f), "Check applies nothing");
            var result = s.Propose(Items(PlanetItem("B")), none);
            ok &= Check(result.applied && result.detail.StartsWith("accepted"), "gift applied, detail starts accepted: " + result.reason + " " + result.detail);
            ok &= Check(s.Map.GetPlanet("B").Owner == 1, "the planet changed hands");
            ok &= Check(Near(s.Hostility, 40f), "hostility 50 + clamp(-20, -10, 10) = 40");
        }

        // A fleet gift worth 5 at hostility 4: -5 would go below 0, so it stops at 0.
        using (var s = new DealScene())
        {
            s.SetHostility(4f);
            var result = s.Propose(Items(WarshipItem(1)), none);
            ok &= Check(result.applied && result.detail.StartsWith("accepted"), "fleet gift accepted");
            ok &= Check(Near(s.Hostility, 0f), "hostility 4 - 5 stays at 0");
            ok &= Check(s.Map.GetPlanet("A").DockedShips.Count(x => x.Owner == 0) == 1 && s.Map.GetPlanet("A").DockedShips.Count(x => x.Owner == 1) == 1, "one ship moved to the rival");
        }

        // Declined: asking planet C for nothing changes no owner, no hostility, no truce.
        using (var s = new DealScene())
        {
            s.SetHostility(50f);
            var result = s.Propose(none, Items(PlanetItem("C")));
            ok &= Check(result.applied && result.reason == "" && result.detail.StartsWith("declined"), "a declined deal is applied with detail declined: " + result.detail);
            ok &= Check(s.Map.GetPlanet("C").Owner == 1 && Near(s.Hostility, 50f), "declined: owner and hostility unchanged");
            ok &= Check(s.Map.Diplomacy.Get(0, 1).TruceUntil == 0 && s.Map.Diplomacy.Get(1, 0).TruceUntil == 0, "declined: no truce");
        }

        // End the war alone: war cost 30, hostility 20 -> penalty 10, PeaceWorth 20: accepted, Peace both ways, truce to 5 + 20 = 25, hostility untouched.
        using (var s = new DealScene())
        {
            s.Map.Diplomacy.SetStance(0, 1, Stance.War, 1);
            s.WarCost = 30f;
            s.SetHostility(20f);
            var result = s.Propose(none, Items(endWar));
            ok &= Check(result.applied && result.detail.StartsWith("accepted"), "end-war deal accepted: " + result.reason + " " + result.detail);
            ok &= Check(s.Map.Diplomacy.StanceToward(0, 1) == Stance.Peace && s.Map.Diplomacy.StanceToward(1, 0) == Stance.Peace, "Peace both ways");
            ok &= Check(s.Map.Diplomacy.Get(0, 1).TruceUntil == 25 && s.Map.Diplomacy.Get(1, 0).TruceUntil == 25, "20-turn truce");
            ok &= Check(Near(s.Hostility, 20f), "a peace deal with no tangible items leaves hostility unchanged");
        }

        // The sign: the rival gives C (20), gets a ship (5) and still accepts for the peace (45 - 25 = 20): net 5, tangible -15, hostility + min(10, 15).
        using (var s = new DealScene())
        {
            s.Map.Diplomacy.SetStance(0, 1, Stance.War, 1);
            s.WarCost = 45f;
            s.SetHostility(50f);
            var result = s.Propose(Items(WarshipItem(1)), Items(PlanetItem("C"), endWar));
            ok &= Check(result.applied && result.detail.StartsWith("accepted"), "peace carries a lopsided trade: " + result.reason + " " + result.detail);
            ok &= Check(s.Map.GetPlanet("C").Owner == 0, "the rival gave C");
            ok &= Check(Near(s.Hostility, 60f), "hostility 50 + min(10, 15) = 60");
        }
        // The same at hostility 98 (war cost 100, penalty 49, peace worth 51): 98 + 10 stops at 100.
        using (var s = new DealScene())
        {
            s.Map.Diplomacy.SetStance(0, 1, Stance.War, 1);
            s.WarCost = 100f;
            s.SetHostility(98f);
            var result = s.Propose(Items(WarshipItem(1)), Items(PlanetItem("C"), endWar));
            ok &= Check(result.applied && result.detail.StartsWith("accepted"), "accepted at hostility 98: " + result.reason);
            ok &= Check(Near(s.Hostility, 100f), "hostility stops at hostilityMax");
        }

        // Refusals
        using (var s = new DealScene())
        {
            ok &= Check(s.Propose(none, none).reason == "EmptyDeal", "EmptyDeal");
            ok &= Check(s.Processor.Preview(new HumanCommand { type = "propose", rival = 1 }).Refusal == "EmptyDeal", "preview carries EmptyDeal in Verdict.Refusal");
            ok &= Check(s.Processor.Check(new HumanCommand { type = "propose", rival = 1 }) == "EmptyDeal", "Check reports EmptyDeal");
            ok &= Check(s.Propose(Items(PlanetItem("B")), none, -1).reason == "UnknownRival", "UnknownRival for a negative id");
            ok &= Check(s.Propose(Items(PlanetItem("B")), none, 0).reason == "UnknownRival", "UnknownRival for the human itself");
            ok &= Check(s.Propose(Items(PlanetItem("B")), none, 5).reason == "NoContact", "NoContact for a player never met");
            ok &= Check(s.Propose(Items(PlanetItem("A")), none).reason == "Capital", "the capital cannot be given (Validate reason)");
            s.Map.Diplomacy.Enabled = false;
            ok &= Check(s.Propose(Items(PlanetItem("B")), none).reason == "DiplomacyOff", "DiplomacyOff");
        }
        using (var s = new DealScene(false))
        {
            ok &= Check(s.Propose(Items(PlanetItem("B")), none).reason == "NoDealContext", "a processor with no deal context refuses");
            ok &= Check(s.Map.GetPlanet("B").Owner == 0, "and applies nothing");
        }

        // Old JSON without the new fields still parses; new fields round-trip.
        var old = HumanCommandProcessor.Parse("{\"commands\":[{\"type\":\"declareWar\",\"rival\":1}]}", out var error);
        ok &= Check(error == null && old.Count == 1 && old[0].type == "declareWar" && (old[0].dealGive == null || old[0].dealGive.Count == 0)
                    && (old[0].dealGet == null || old[0].dealGet.Count == 0), "JSON without dealGive/dealGet parses");
        var fresh = HumanCommandProcessor.Parse("{\"commands\":[{\"type\":\"propose\",\"rival\":1,\"dealGive\":[{\"kind\":\"planet\",\"name\":\"B\"}]}]}", out error);
        ok &= Check(error == null && fresh.Count == 1 && fresh[0].dealGive.Count == 1 && fresh[0].dealGive[0].name == "B", "dealGive parses from JSON");
        return ok;
    }
}
