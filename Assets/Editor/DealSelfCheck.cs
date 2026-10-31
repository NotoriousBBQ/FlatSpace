using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using FlatSpace.AI;
using Flatspace.Objects.Production;
using System.Linq;

public static class DealSelfCheck
{
    [MenuItem("FlatSpace/AI/Run Deal Self-Check")]
    public static void Run() => RunChecks();

    public static bool RunChecks()
    {
        var ok = RunConstantsCheck();
        ok &= RunWarCostChecks();
        ok &= RunEvaluatorChecks();
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
}
