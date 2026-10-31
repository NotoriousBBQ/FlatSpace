using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using FlatSpace.AI;
using Flatspace.Objects.Production;

public static class PlayerValueSelfCheck
{
    [MenuItem("FlatSpace/AI/Run Player Value Self-Check")]
    public static void Run() => RunChecks();

    public static bool RunChecks()
    {
        var ok = RunShipCostChecks();
        ok &= RunHealthFractionChecks();
        ok &= RunDefaultsCheck();
        ok &= RunScoreChecks();
        Debug.Log(ok
            ? "[PlayerValueSelfCheck] ALL PASSED"
            : "[PlayerValueSelfCheck] FAILURES (see errors above)");
        return ok;
    }

    private static bool Check(bool condition, string label)
    {
        if (!condition) Debug.LogError($"[PlayerValueSelfCheck] FAIL: {label}");
        return condition;
    }

    private static bool Near(float a, float b) => Mathf.Abs(a - b) < 0.001f;

    public static bool RunShipCostChecks()
    {
        var ok = true;
        ok &= Check(Near(WarshipCosts.ShipCost(Ship.ShipKind.WarShip, 0, 100f, 300f, 0.1f, 1.5f, 1.5f), 100f), "warship with no improvements costs 100");
        ok &= Check(Near(WarshipCosts.ShipCost(Ship.ShipKind.WarShip, 3, 100f, 300f, 0.1f, 1.5f, 1.5f), 130f), "warship with 3 improvements costs 130");
        ok &= Check(Near(WarshipCosts.ShipCost(Ship.ShipKind.SiegeShip, 3, 100f, 300f, 0.1f, 1.5f, 1.5f), 195f), "siegeship with 3 improvements costs 195");
        ok &= Check(Near(WarshipCosts.ShipCost(Ship.ShipKind.Bunker, 0, 100f, 300f, 0.1f, 1.5f, 1.5f), 150f), "bunker with no improvements costs 150");
        ok &= Check(Near(WarshipCosts.ShipCost(Ship.ShipKind.ColonyShip, 5, 100f, 300f, 0.1f, 1.5f, 1.5f), 300f), "colony ship costs its catalog cost");
        return ok;
    }

    public static bool RunHealthFractionChecks()
    {
        var ok = true;
        var stats = new WarshipStats(null);
        var t = WarshipSelfCheck.MakeTemplate();
        ok &= Check(Near(stats.HealthFraction(t, null, 0f, "Warship"), 1f), "no damage is a full fraction");
        ok &= Check(Near(stats.HealthFraction(t, null, 25f, "Warship"), 0.75f), "25 damage of 100 leaves 0.75");
        ok &= Check(Near(stats.HealthFraction(t, null, 150f, "Warship"), 0f), "damage beyond health leaves 0");
        t.shipHealth = 0f;
        ok &= Check(Near(stats.HealthFraction(t, null, 10f, "Warship"), 1f), "a Health stat of 0 reads as 1");
        Object.DestroyImmediate(t);
        ok &= Check(Near(stats.HealthFraction(null, null, 10f, "Warship"), 1f), "a null template reads as 1");
        return ok;
    }

    public static bool RunDefaultsCheck()
    {
        var c = ScriptableObject.CreateInstance<GameAIConstants>();
        var ok = Check(Near(c.fleetValueDivisor, 50f), "fleetValueDivisor defaults to 50");
        ok &= Check(Near(c.techValueDivisor, 2000f), "techValueDivisor defaults to 2000");
        Object.DestroyImmediate(c);
        return ok;
    }

    private static float _nextX;

    private static PlanetSpawnData MakeValueSpawn(string name, float value, List<Object> junk, IEnumerable<string> connections = null)
    {
        var resourceData = ScriptableObject.CreateInstance<PlanetResourceData>();
        resourceData._initialPopulation = 0;
        resourceData._maxPopulation = 5;
        resourceData._planetValue = value;
        var spawn = ScriptableObject.CreateInstance<PlanetSpawnData>();
        spawn._planetName = name;
        spawn._planetPosition = new Vector3(_nextX, 0f, 0f);   // distinct positions (see PathingSystem.FindPath)
        _nextX += 100f;
        spawn._planetType = Planet.PlanetType.PlanetTypeNormal;
        spawn._resourceData = resourceData;
        spawn._connections = connections != null ? new List<string>(connections) : new List<string>();
        junk.Add(resourceData);
        junk.Add(spawn);
        return spawn;
    }

    private static CatalogItem MakeItem(string subType, float cost, bool researched, List<Object> junk)
    {
        var item = ScriptableObject.CreateInstance<CatalogItem>();
        item.itemName = subType; item.name = subType; item.subType = subType; item.cost = cost; item.researched = researched;
        junk.Add(item);
        return item;
    }

    private static GameAI.GameAIOrder FleetOrder(int player, int ships)
    {
        var fleet = new GameAI.GameAIOrder.ShipFleetPayload { Kind = Ship.ShipKind.WarShip };
        for (var i = 0; i < ships; i++) fleet.Snapshots.Add(new List<string>());
        return new GameAI.GameAIOrder
        {
            Type = GameAI.GameAIOrder.OrderType.OrderTypeShipTransport,
            PlayerId = player,
            Fleet = fleet,
        };
    }

    public static bool RunScoreChecks()
    {
        var ok = true;
        _nextX = 0f;
        var junk = new List<Object>();
        var go = new GameObject("PlayerValueSelfCheckMap");
        var template = WarshipSelfCheck.MakeTemplate();   // Health 100
        var constants = ScriptableObject.CreateInstance<GameAIConstants>();
        var zeroDivisors = ScriptableObject.CreateInstance<GameAIConstants>();
        try
        {
            constants.defaultTravelSpeed = 1f;
            constants.warShipData = template;
            constants.siegeShipData = template;
            constants.bunkerData = template;
            constants.warshipImprovementCostFactor = 0.1f;
            constants.siegeshipCostRatio = 1.5f;
            constants.bunkerCostRatio = 1.5f;
            constants.fleetValueDivisor = 50f;
            constants.techValueDivisor = 2000f;
            zeroDivisors.techValueDivisor = 0f;
            zeroDivisors.fleetValueDivisor = 0f;

            var catalog = new List<CatalogItem> { MakeItem("Warship", 100f, false, junk), MakeItem("ColonyShip", 300f, false, junk) };
            var s = PlayerValue.ValueSettings.From(constants, catalog);
            var stats = new WarshipStats(null);

            // Planets: A 10 and B 2 (player 0), C 7 (player 1), D 5 (tied, NoOwner). Owner is set directly: no Gameboard needed.
            var spawns = new List<PlanetSpawnData>
            {
                MakeValueSpawn("A", 10f, junk, new[] { "B" }),
                MakeValueSpawn("B", 2f, junk, new[] { "C" }),
                MakeValueSpawn("C", 7f, junk, new[] { "D" }),
                MakeValueSpawn("D", 5f, junk),
            };
            var map = go.AddComponent<GameAIMap>();
            map.GameAIMapInit(spawns, constants);
            map.GetPlanet("A").Owner = 0;
            map.GetPlanet("B").Owner = 0;
            map.GetPlanet("C").Owner = 1;
            map.GetPlanet("D").Owner = Planet.NoOwner;

            var empty = PlayerValue.ScoreOf(map, 0, null, null, stats, s);
            ok &= Check(Near(empty.Planets, 12f), "player 0 owns planets worth 10 and 2: Planets is 12 (others and a tied planet add 0)");
            ok &= Check(Near(empty.Fleets, 0f) && Near(empty.Tech, 0f), "no ships, null research and null orders: Fleets and Tech are 0");
            ok &= Check(Near(PlayerValue.ScoreOf(map, 5, null, null, stats, s).Total, 0f), "a player with nothing scores 0");

            // Ships, on planet A.
            var a = map.GetPlanet("A");
            a.DockShipFromSave(Ship.ShipKind.WarShip, 0, new List<string>());
            ok &= Check(Near(PlayerValue.ShipValue(a.DockedShips[0], stats, s), 2f), "new warship: 100 / 50 = 2");
            a.DockShipFromSave(Ship.ShipKind.WarShip, 0, new List<string> { "x", "y", "z" });
            ok &= Check(Near(PlayerValue.ShipValue(a.DockedShips[1], stats, s), 2.6f), "warship with 3 improvements: 130 / 50 = 2.6");
            a.DockShipFromSave(Ship.ShipKind.WarShip, 0, new List<string>(), 50f);
            ok &= Check(Near(PlayerValue.ShipValue(a.DockedShips[2], stats, s), 1f), "warship at 50 damage of 100 health: 1.0");
            a.DockShipFromSave(Ship.ShipKind.Bunker, 0, new List<string>());
            ok &= Check(Near(PlayerValue.ShipValue(a.DockedShips[3], stats, s), 3f), "bunker: 100 x 1.5 / 50 = 3");
            a.DockShipFromSave(Ship.ShipKind.ColonyShip, 0, new List<string>());
            ok &= Check(Near(PlayerValue.ShipValue(a.DockedShips[4], stats, s), 6f), "colony ship: 300 / 50 = 6");
            a.DockShipFromSave(Ship.ShipKind.WarShip, 1, new List<string>());   // another player's ship

            var withShips = PlayerValue.ScoreOf(map, 0, null, null, stats, s);
            ok &= Check(Near(withShips.Fleets, 2f + 2.6f + 1f + 3f + 6f), "docked ships of player 0 sum into Fleets; another player's ship adds 0");

            // Research.
            var research = new List<CatalogItem> { MakeItem("x", 6000f, true, junk), MakeItem("x", 300f, true, junk), MakeItem("x", 12000f, false, junk) };
            ok &= Check(Near(PlayerValue.TechValue(research, s), 3.15f), "researched 6000 + 300 over 2000 = 3.15; unresearched adds 0");
            ok &= Check(Near(PlayerValue.TechValue(null, s), 0f), "null research is 0");
            var sZero = PlayerValue.ValueSettings.From(zeroDivisors, catalog);
            var tz = PlayerValue.TechValue(research, sZero);
            ok &= Check(!float.IsInfinity(tz) && !float.IsNaN(tz) && Near(tz, 6300f), "a divisor of 0 reads as 1: TechValue is finite");

            // Ships in flight.
            var orders = new List<GameAI.GameAIOrder> { FleetOrder(0, 2) };
            ok &= Check(Near(PlayerValue.ScoreOf(map, 5, orders, null, stats, s).Fleets, 0f), "an order of player 0 adds nothing to another player");
            var flight0 = PlayerValue.ScoreOf(map, 0, orders, null, stats, s);
            ok &= Check(Near(flight0.Fleets - withShips.Fleets, 4f), "an in-flight 2-warship payload for player 0 adds 4");
            var theirs = new List<GameAI.GameAIOrder> { FleetOrder(1, 2) };
            ok &= Check(Near(PlayerValue.ScoreOf(map, 0, theirs, null, stats, s).Fleets, withShips.Fleets), "the same order for player 1 adds 0 to player 0");

            var total = PlayerValue.ScoreOf(map, 0, orders, research, stats, s);
            ok &= Check(Near(total.Total, total.Planets + total.Fleets + total.Tech), "Total is Planets + Fleets + Tech");
            ok &= Check(Near(total.Tech, 3.15f) && Near(total.Planets, 12f), "ScoreOf carries the planet and tech parts");
        }
        finally
        {
            Object.DestroyImmediate(go);
            Object.DestroyImmediate(constants);
            Object.DestroyImmediate(zeroDivisors);
            Object.DestroyImmediate(template);
            foreach (var o in junk) Object.DestroyImmediate(o);
        }
        return ok;
    }
}
