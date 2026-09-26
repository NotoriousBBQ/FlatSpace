using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using FlatSpace.AI;

public static class FleetSummarySelfCheck
{
    private static int _checkCount;

    [MenuItem("FlatSpace/UI/Run Fleet Summary Self-Check")]
    public static void Run()
    {
        _checkCount = 0;
        var ok = RunGroupingCheck();
        ok &= RunColorCheck();
        Debug.Log(ok
            ? $"[FleetSummarySelfCheck] ALL PASSED ({_checkCount} assertions ran)"
            : $"[FleetSummarySelfCheck] FAILURES (see errors above; {_checkCount} assertions ran)");
    }

    private static bool Check(bool condition, string label)
    {
        _checkCount++;
        if (!condition) Debug.LogError($"[FleetSummarySelfCheck] FAIL: {label}");
        return condition;
    }

    private static PlanetSpawnData MakeSpawn(string name, float x, IEnumerable<string> connections = null)
    {
        var resourceData = ScriptableObject.CreateInstance<PlanetResourceData>();
        resourceData._initialPopulation = 0;
        resourceData._maxPopulation = 5;

        var spawn = ScriptableObject.CreateInstance<PlanetSpawnData>();
        spawn._planetName = name;
        spawn._planetPosition = new Vector3(x, 0f, 0f);   // distinct positions: PathingSystem tie hazard
        spawn._planetType = Planet.PlanetType.PlanetTypeNormal;
        spawn._resourceData = resourceData;
        spawn._connections = connections != null ? new List<string>(connections) : new List<string>();
        return spawn;
    }

    private static void Dock(Planet planet, Ship.ShipKind kind, int owner, int count)
    {
        for (var i = 0; i < count; i++)
            planet.DockShipFromSave(kind, owner, new List<string>());
    }

    public static bool RunGroupingCheck()
    {
        var ok = true;
        var go = new GameObject("FSSelfCheckMap");
        try
        {
            var map = go.AddComponent<GameAIMap>();
            map.GameAIMapInit(new List<PlanetSpawnData>
            {
                MakeSpawn("A", 0f, new[] { "B" }),
                MakeSpawn("B", 100f),
            }, ScriptableObject.CreateInstance<GameAIConstants>());
            var a = map.GetPlanet("A");

            ok &= Check(FleetSummary.ForPlanet(a).Count == 0, "a planet with no ships has no fleet groups");

            Dock(a, Ship.ShipKind.ColonyShip, 1, 2);
            Dock(a, Ship.ShipKind.WarShip, 0, 3);
            Dock(a, Ship.ShipKind.ColonyShip, 0, 1);
            Dock(a, Ship.ShipKind.WarShip, -1, 1);

            var groups = FleetSummary.ForPlanet(a);
            ok &= Check(groups.Count == 3, "three owners (0, 1 and ownerless) give three groups");
            ok &= Check(groups[0].Owner == 0 && groups[1].Owner == 1 && groups[2].Owner == -1,
                "groups are ordered by player id, ownerless ships last");
            ok &= Check(groups[0].Count == 4, "player 0's 3 warships + 1 colony ship count as one fleet of 4");
            ok &= Check(groups[0].IconKind == Ship.ShipKind.WarShip, "any warship makes the fleet a warship icon");
            ok &= Check(groups[1].Count == 2 && groups[1].IconKind == Ship.ShipKind.ColonyShip,
                "colony-ships-only fleet shows the colony ship icon");
            ok &= Check(groups[2].Count == 1 && groups[2].IconKind == Ship.ShipKind.WarShip,
                "ownerless ships form their own group and are not merged into a player's fleet");
        }
        finally { Object.DestroyImmediate(go); }
        return ok;
    }

    public static bool RunColorCheck()
    {
        var ok = true;
        ok &= Check(Player.PlayerColors[0].Equals(FleetSummary.ColorFor(0)),
            "a valid player id maps to that player's color");
        ok &= Check(Player.NoPlayerColor.Equals(FleetSummary.ColorFor(-1)),
            "an ownerless fleet gets the neutral color");
        ok &= Check(Player.NoPlayerColor.Equals(FleetSummary.ColorFor(Player.PlayerColors.Count)),
            "an out-of-range owner falls back to the neutral color instead of throwing");
        return ok;
    }
}
