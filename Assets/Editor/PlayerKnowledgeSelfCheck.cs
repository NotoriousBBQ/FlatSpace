using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using FlatSpace.AI;

public static class PlayerKnowledgeSelfCheck
{
    [MenuItem("FlatSpace/AI/Run Player Knowledge Self-Check")]
    public static void Run()
    {
        var ok = RunGameAIMapSharedQueriesCheck();
        Debug.Log(ok
            ? "[PlayerKnowledgeSelfCheck] ALL PASSED"
            : "[PlayerKnowledgeSelfCheck] FAILURES (see errors above)");
    }

    private static bool Check(bool condition, string label)
    {
        if (!condition) Debug.LogError($"[PlayerKnowledgeSelfCheck] FAIL: {label}");
        return condition;
    }

    private static PlanetSpawnData MakeSpawn(string name, int initialPopulation,
        IEnumerable<string> connections = null)
    {
        var resourceData = ScriptableObject.CreateInstance<PlanetResourceData>();
        resourceData._initialPopulation = initialPopulation;
        resourceData._maxPopulation = 5;

        var spawn = ScriptableObject.CreateInstance<PlanetSpawnData>();
        spawn._planetName = name;
        spawn._planetPosition = Vector3.zero;
        spawn._planetType = Planet.PlanetType.PlanetTypeNormal;
        spawn._resourceData = resourceData;
        spawn._connections = connections != null ? new List<string>(connections) : new List<string>();
        return spawn;
    }

    public static bool RunGameAIMapSharedQueriesCheck()
    {
        var ok = true;
        var mapGo = new GameObject("PKSelfCheckMap_SharedQueries");
        try
        {
            var map = mapGo.AddComponent<GameAIMap>();
            var constants = ScriptableObject.CreateInstance<GameAIConstants>();

            var spawns = new List<PlanetSpawnData>
            {
                MakeSpawn("A", initialPopulation: 1, connections: new[] { "B" }), // only A declares the link
                MakeSpawn("B", initialPopulation: 0),
                MakeSpawn("C", initialPopulation: 0),
                MakeSpawn("D", initialPopulation: 0),
            };
            map.GameAIMapInit(spawns, constants);
            map.GetPlanet("C").DockShipFromSave(Ship.ShipKind.WarShip, 0, new List<string>());

            var sourcesForPlayer0 = map.GetVisionSourcePlanets(0);
            ok &= Check(sourcesForPlayer0.Exists(s => s.Planet.PlanetName == "A" && s.HasPopulation && !s.HasOwnedShip),
                "A is a population source for player 0");
            ok &= Check(sourcesForPlayer0.Exists(s => s.Planet.PlanetName == "C" && s.HasOwnedShip && !s.HasPopulation),
                "C is a ship source for player 0");
            ok &= Check(!sourcesForPlayer0.Exists(s => s.Planet.PlanetName == "B" || s.Planet.PlanetName == "D"),
                "B and D are not sources for player 0");

            var sourcesForPlayer1 = map.GetVisionSourcePlanets(1);
            ok &= Check(sourcesForPlayer1.Count == 0,
                "player 1 has no sources (A's population and C's ship both belong to player 0)");

            ok &= Check(map.GetNeighbours("A").Contains("B"), "A's declared connection to B is a neighbour");
            ok &= Check(map.GetNeighbours("B").Contains("A"),
                "B is symmetrized as A's neighbour even though B never declared it");
            ok &= Check(map.GetNeighbours("C").Count == 0, "C has no connections");
            ok &= Check(map.GetNeighbours("NoSuchPlanet").Count == 0,
                "unknown planet name returns an empty neighbour list, not null/throw");
        }
        finally
        {
            Object.DestroyImmediate(mapGo);
        }
        return ok;
    }
}
