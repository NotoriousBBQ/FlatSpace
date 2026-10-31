using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using FlatSpace.AI;

public static class DealSelfCheck
{
    [MenuItem("FlatSpace/AI/Run Deal Self-Check")]
    public static void Run() => RunChecks();

    public static bool RunChecks()
    {
        var ok = RunConstantsCheck();
        ok &= RunWarCostChecks();
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
}
