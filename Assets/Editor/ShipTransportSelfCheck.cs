using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using FlatSpace.AI;

public static class ShipTransportSelfCheck
{
    private static float _nextPlanetX;

    [MenuItem("FlatSpace/AI/Run Ship Transport Self-Check")]
    public static void Run()
    {
        var ok = RunMatrixTypesCheck();
        Debug.Log(ok
            ? "[ShipTransportSelfCheck] ALL PASSED"
            : "[ShipTransportSelfCheck] FAILURES (see errors above)");
    }

    private static bool Check(bool condition, string label)
    {
        if (!condition) Debug.LogError($"[ShipTransportSelfCheck] FAIL: {label}");
        return condition;
    }

    // Values chosen so category/garrison arithmetic in the checks is easy to read.
    private static GameAIConstants NewConstants()
    {
        var c = ScriptableObject.CreateInstance<GameAIConstants>();
        c.defaultTravelSpeed = 100f;
        c.expandPopulationTrigger = 0.8f;
        c.maxPathNodesForResourceDistribution = 10;
        c.maxPathNodesForShipTransport = 10;
        c.garrisonSpecialized = 4;
        c.garrisonOuter = 6;
        c.garrisonPrime = 4;
        c.garrisonHighTraffic = 2;
        c.garrisonHighlySpecialized = 1;
        c.highTrafficConnectionCount = 4;
        c.category5UnlockShipsPerColonizedPlanet = 2f;
        return c;
    }

    // Distinct, strictly increasing X per planet (PathingSystem.FindPath tie hazard). Reset
    // _nextPlanetX = 0 at the start of each check so path costs are 100 per hop.
    private static PlanetSpawnData MakeSpawn(string name, Planet.PlanetType type,
        IEnumerable<string> connections = null)
    {
        var resourceData = ScriptableObject.CreateInstance<PlanetResourceData>();
        resourceData._initialPopulation = 0;
        resourceData._maxPopulation = 5;

        var spawn = ScriptableObject.CreateInstance<PlanetSpawnData>();
        spawn._planetName = name;
        spawn._planetPosition = new Vector3(_nextPlanetX, 0f, 0f);
        _nextPlanetX += 100f;
        spawn._planetType = type;
        spawn._resourceData = resourceData;
        spawn._connections = connections != null ? new List<string>(connections) : new List<string>();
        return spawn;
    }

    private static GameAIMap BuildMap(GameObject go, GameAIConstants constants, params PlanetSpawnData[] spawns)
    {
        var map = go.AddComponent<GameAIMap>();
        map.GameAIMapInit(new List<PlanetSpawnData>(spawns), constants);
        return map;
    }

    private static Planet Colonize(GameAIMap map, string name, int player = 0)
    {
        var planet = map.GetPlanet(name);
        planet.Owner = player;
        planet.Population.Add(new Planet.Inhabitant { Player = player });
        return planet;
    }

    private static void Dock(Planet planet, int count, int owner = 0)
    {
        for (var i = 0; i < count; i++)
            planet.DockShipFromSave(Ship.ShipKind.WarShip, owner, new List<string>());
    }

    public static bool RunMatrixTypesCheck()
    {
        var ok = true;
        var a = new ShipChoiceElement { TargetPlanet = "X", Category = 1, PathCost = 100f, SpareShips = 3, Deficit = 4 };
        var b = new ShipChoiceElement { TargetPlanet = "X", Category = 5, PathCost = 999f, SpareShips = 9, Deficit = 1 };
        var c = new ShipChoiceElement { TargetPlanet = "Y", Category = 1, PathCost = 100f, SpareShips = 3, Deficit = 4 };
        ok &= Check(a.Equals(b), "choices for the same target are equal regardless of source-specific fields");
        ok &= Check(!a.Equals(c), "choices for different targets are not equal");
        ok &= Check(a.Target == "X", "ShipChoiceElement.Target is the target planet name");
        IScoreMatrixChoiceElement iface = a;
        ok &= Check(iface.Cost == 100f && iface.Surplus == 3f && iface.Shortage == 4f,
            "interface Cost/Surplus/Shortage map to PathCost/SpareShips/Deficit");
        return ok;
    }
}
