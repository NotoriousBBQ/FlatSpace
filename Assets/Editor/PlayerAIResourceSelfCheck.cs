using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using FlatSpace.AI;

public static class PlayerAIResourceSelfCheck
{
    [MenuItem("FlatSpace/AI/Run PlayerAI Resource Self-Check")]
    public static void Run()
    {
        var ok = RunFoodShortageScopingCheck();
        Debug.Log(ok
            ? "[PlayerAIResourceSelfCheck] ALL PASSED"
            : "[PlayerAIResourceSelfCheck] FAILURES (see errors above)");
    }

    private static bool Check(bool condition, string label)
    {
        if (!condition) Debug.LogError($"[PlayerAIResourceSelfCheck] FAIL: {label}");
        return condition;
    }

    private static float _nextX;

    private static PlanetSpawnData MakeSpawn(string name, IEnumerable<string> connections = null)
    {
        var resourceData = ScriptableObject.CreateInstance<PlanetResourceData>();
        resourceData._initialPopulation = 0;
        resourceData._maxPopulation = 5;

        var spawn = ScriptableObject.CreateInstance<PlanetSpawnData>();
        spawn._planetName = name;
        // Distinct positions: PathingSystem.FindPath now throws on a same-position tie
        // instead of hanging (see fix/pathing-tie-break-assert) rather than silently
        // working around it.
        spawn._planetPosition = new Vector3(_nextX, 0f, 0f);
        _nextX += 100f;
        spawn._planetType = Planet.PlanetType.PlanetTypeNormal;
        spawn._resourceData = resourceData;
        spawn._connections = connections != null ? new List<string>(connections) : new List<string>();
        return spawn;
    }

    public static bool RunFoodShortageScopingCheck()
    {
        var ok = true;
        var mapGo = new GameObject("PAResSelfCheckMap");
        var p0Go = new GameObject("PAResSelfCheckPlayer0");
        var p1Go = new GameObject("PAResSelfCheckPlayer1");
        try
        {
            var map = mapGo.AddComponent<GameAIMap>();
            var constants = ScriptableObject.CreateInstance<GameAIConstants>();
            constants.defaultTravelSpeed = 1f;
            constants.maxPathNodesForResourceDistribution = 10;

            // P0Shortage -- P0Surplus -- P1Surplus, a connected chain so every pair has a
            // real path (avoids relying on PathingSystem's degenerate unreachable-path case).
            var spawns = new List<PlanetSpawnData>
            {
                MakeSpawn("P0Shortage", connections: new[] { "P0Surplus" }),
                MakeSpawn("P0Surplus", connections: new[] { "P1Surplus" }),
                MakeSpawn("P1Surplus"),
            };
            map.GameAIMapInit(spawns, constants);

            // Give the surplus planets real population owned by the matching player, so
            // EmitResourceOrders' GetPopulationFraction(Player.playerID) > 0 and an order
            // actually gets emitted rather than silently skipped.
            map.GetPlanet("P0Surplus").Population.Add(new Planet.Inhabitant { Player = 0 });
            map.GetPlanet("P1Surplus").Population.Add(new Planet.Inhabitant { Player = 1 });

            var results = new List<Planet.PlanetUpdateResult>
            {
                new Planet.PlanetUpdateResult("P0Shortage",
                    Planet.PlanetUpdateResult.PlanetUpdateResultType.PlanetUpdateResultTypeFoodShortage,
                    10f, playerID: 0),
                new Planet.PlanetUpdateResult("P0Surplus",
                    Planet.PlanetUpdateResult.PlanetUpdateResultType.PlanetUpdateResultTypeFoodSurplus,
                    20f, playerID: 0),
                new Planet.PlanetUpdateResult("P1Surplus",
                    Planet.PlanetUpdateResult.PlanetUpdateResultType.PlanetUpdateResultTypeFoodSurplus,
                    20f, playerID: 1),
            };

            var player0 = p0Go.AddComponent<Player>();
            var player0AI = p0Go.AddComponent<PlayerAI>();
            player0AI.Player = player0;
            player0AI.AIMap = map;
            player0.playerID = 0;

            var player1 = p1Go.AddComponent<Player>();
            var player1AI = p1Go.AddComponent<PlayerAI>();
            player1AI.Player = player1;
            player1AI.AIMap = map;
            player1.playerID = 1;

            var orders0 = new List<GameAI.GameAIOrder>();
            player0AI.ProcessResults(results, orders0);
            ok &= Check(orders0.Exists(o =>
                    o.Type == GameAI.GameAIOrder.OrderType.OrderTypeFoodTransport &&
                    o.Origin == "P0Surplus" && o.Target == "P0Shortage"),
                "player 0 ships its own surplus to its own shortage (positive control)");

            var orders1 = new List<GameAI.GameAIOrder>();
            player1AI.ProcessResults(results, orders1);
            ok &= Check(!orders1.Exists(o =>
                    o.Type == GameAI.GameAIOrder.OrderType.OrderTypeFoodTransport &&
                    o.Target == "P0Shortage"),
                "player 1 does not ship its surplus to player 0's shortage");
        }
        finally
        {
            Object.DestroyImmediate(p1Go);
            Object.DestroyImmediate(p0Go);
            Object.DestroyImmediate(mapGo);
        }
        return ok;
    }
}
