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
        _checkCount = 0;
        var ok = RunMatrixTypesCheck();
        ok &= RunPlanetShipHelpersCheck();
        ok &= RunCategoryCheck();
        ok &= RunRoundAndRolesCheck();
        ok &= RunPlanCheck();
        ok &= RunOrderExecutionCheck();
        ok &= RunFleetSaveCheck();
        ok &= RunPlayerAIShipOrdersCheck();
        ok &= RunConsolidatePlannerChecks();
        ok &= RunAssaultChecks();
        ok &= RunConsolidatePlanShipActionsCheck();
        ok &= RunIncomingPerPlayerCheck();
        ok &= RunAssaultTargetExcludesOwnPlanetsCheck();
        ok &= RunSameOriginSnapshotCheck();
        Debug.Log(ok
            ? $"[ShipTransportSelfCheck] ALL PASSED ({_checkCount} assertions ran)"
            : $"[ShipTransportSelfCheck] FAILURES (see errors above; {_checkCount} assertions ran)");
    }

    // Reported in the summary so a stale run (Unity kept the old assembly after a compile error) is visible.
    private static int _checkCount;

    private static bool Check(bool condition, string label)
    {
        _checkCount++;
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
        c.assaultRatio = 1.5f;
        c.assaultMinimumShips = 3;
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

    public static bool RunPlanetShipHelpersCheck()
    {
        var ok = true;
        _nextPlanetX = 0f;
        var go = new GameObject("STSelfCheckMap_PlanetShips");
        try
        {
            var map = BuildMap(go, NewConstants(),
                MakeSpawn("A", Planet.PlanetType.PlanetTypeNormal, new[] { "B" }),
                MakeSpawn("B", Planet.PlanetType.PlanetTypeNormal));
            var a = map.GetPlanet("A");
            a.DockShipFromSave(Ship.ShipKind.WarShip, 0, new List<string> { "s1" });
            a.DockShipFromSave(Ship.ShipKind.WarShip, 0, new List<string> { "s2" });
            a.DockShipFromSave(Ship.ShipKind.WarShip, 1, new List<string> { "other" });
            a.DockShipFromSave(Ship.ShipKind.ColonyShip, 0, new List<string> { "c" });

            var snaps = a.PeekShipSnapshots(Ship.ShipKind.WarShip, 0, 2);
            ok &= Check(snaps.Count == 2 && snaps[0][0] == "s1" && snaps[1][0] == "s2",
                "peek returns the first N matching ships' snapshots in dock order");
            ok &= Check(a.PeekShipSnapshots(Ship.ShipKind.WarShip, 0, 5).Count == 2,
                "peek is capped at the ships that exist for that owner and kind");
            ok &= Check(a.DockedShips.Count == 4, "peek does not remove ships");

            var removed = a.UndockShips(Ship.ShipKind.WarShip, 0, 1);
            ok &= Check(removed == 1 && a.DockedShips.Count == 3, "undock removes exactly N ships");
            ok &= Check(!a.DockedShips.Exists(s => s.ResearchSnapshot.Count > 0 && s.ResearchSnapshot[0] == "s1"),
                "undock removed the same first ship peek reported (s1)");
            ok &= Check(a.DockedShips.Exists(s => s.ResearchSnapshot.Count > 0 && s.ResearchSnapshot[0] == "s2"),
                "the second ship (s2) remains");
            ok &= Check(a.DockedShips.Exists(s => s.Owner == 1), "another player's ship is untouched");
            ok &= Check(a.DockedShips.Exists(s => s.Kind == Ship.ShipKind.ColonyShip), "colony ship is untouched");
            ok &= Check(a.UndockShips(Ship.ShipKind.WarShip, 0, 9) == 1, "undock is capped at available ships");

            ok &= Check(a.GetIncomingShips(Ship.ShipKind.WarShip, 0) == 0, "incoming starts at 0");
            a.AddIncomingShips(Ship.ShipKind.WarShip, 0, 3);
            ok &= Check(a.GetIncomingShips(Ship.ShipKind.WarShip, 0) == 3, "incoming adds");
            a.AddIncomingShips(Ship.ShipKind.WarShip, 0, -5);
            ok &= Check(a.GetIncomingShips(Ship.ShipKind.WarShip, 0) == 0, "incoming never goes below 0");
            a.AddIncomingShips(Ship.ShipKind.WarShip, 0, 2);
            a.ClearIncomingShips();
            ok &= Check(a.GetIncomingShips(Ship.ShipKind.WarShip, 0) == 0, "ClearIncomingShips resets");
        }
        finally { Object.DestroyImmediate(go); }
        return ok;
    }

    public static bool RunCategoryCheck()
    {
        var ok = true;
        _nextPlanetX = 0f;
        var go = new GameObject("STSelfCheckMap_Categories");
        try
        {
            // A(Farm)-B(Normal)-C(Desert)-D(Normal, uncolonized). H is a hub with 4 neighbours.
            var map = BuildMap(go, NewConstants(),
                MakeSpawn("A", Planet.PlanetType.PlanetTypeFarm, new[] { "B" }),
                MakeSpawn("B", Planet.PlanetType.PlanetTypeNormal, new[] { "C" }),
                MakeSpawn("C", Planet.PlanetType.PlanetTypeDesert, new[] { "D" }),
                MakeSpawn("D", Planet.PlanetType.PlanetTypeNormal),
                MakeSpawn("H", Planet.PlanetType.PlanetTypeNormal, new[] { "X1", "X2", "X3", "X4" }),
                MakeSpawn("X1", Planet.PlanetType.PlanetTypeNormal),
                MakeSpawn("X2", Planet.PlanetType.PlanetTypeNormal),
                MakeSpawn("X3", Planet.PlanetType.PlanetTypeNormal),
                MakeSpawn("X4", Planet.PlanetType.PlanetTypeNormal),
                MakeSpawn("P", Planet.PlanetType.PlanetTypePrime, new[] { "Q" }),
                MakeSpawn("Q", Planet.PlanetType.PlanetTypeNormal),
                MakeSpawn("V", Planet.PlanetType.PlanetTypeVerdant));
            foreach (var n in new[] { "A", "B", "C", "H", "X1", "X2", "X3", "X4", "P", "V" }) Colonize(map, n);
            var planner = new ShipTransportPlanner(map, 0);

            ok &= Check(planner.Category(map.GetPlanet("A")) == 1 && planner.Garrison(map.GetPlanet("A")) == 4,
                "A (Farm) is category 1 with the specialized garrison");
            ok &= Check(!planner.IsOuter(map.GetPlanet("A")), "A has only colonized neighbours: not outer");
            ok &= Check(planner.Category(map.GetPlanet("B")) == ShipTransportPlanner.NoCategory
                        && planner.Garrison(map.GetPlanet("B")) == 0,
                "B (Normal, 2 connections, all neighbours colonized) has no category and garrison 0");
            ok &= Check(planner.IsOuter(map.GetPlanet("C")), "C has uncolonized neighbour D: outer");
            ok &= Check(planner.Category(map.GetPlanet("C")) == 1,
                "overlap: Desert+outer takes the best (lowest) category for priority");
            ok &= Check(planner.Garrison(map.GetPlanet("C")) == 6,
                "overlap: Desert+outer takes the LARGEST garrison (outer 6 > specialized 4)");
            ok &= Check(!planner.IsColonized(map.GetPlanet("D")), "D is not colonized");
            ok &= Check(planner.Category(map.GetPlanet("H")) == 4 && planner.Garrison(map.GetPlanet("H")) == 2,
                "H with 4 neighbours is high traffic (category 4, garrison 2)");
            ok &= Check(planner.Category(map.GetPlanet("X1")) == ShipTransportPlanner.NoCategory,
                "a leaf planet with one neighbour has no category");
            ok &= Check(planner.Category(map.GetPlanet("P")) == 2 && planner.Garrison(map.GetPlanet("P")) == 6,
                "Prime with an uncolonized neighbour is outer: category 2, garrison 6");
            Colonize(map, "Q");
            ok &= Check(planner.Category(map.GetPlanet("P")) == 3 && planner.Garrison(map.GetPlanet("P")) == 4,
                "once Q is colonized, P is just Prime: category 3, garrison 4");
            ok &= Check(planner.Category(map.GetPlanet("V")) == 5 && planner.Garrison(map.GetPlanet("V")) == 1,
                "Verdant is category 5 with the smallest garrison");
        }
        finally { Object.DestroyImmediate(go); }
        return ok;
    }

    public static bool RunRoundAndRolesCheck()
    {
        var ok = true;

        // Chain A(Farm g4) - B(Desert g4) - C(Normal g0), all colonized, no uncolonized neighbours.
        _nextPlanetX = 0f;
        var go = new GameObject("STSelfCheckMap_Rounds");
        try
        {
            var map = BuildMap(go, NewConstants(),
                MakeSpawn("A", Planet.PlanetType.PlanetTypeFarm, new[] { "B" }),
                MakeSpawn("B", Planet.PlanetType.PlanetTypeDesert, new[] { "C" }),
                MakeSpawn("C", Planet.PlanetType.PlanetTypeNormal));
            var a = Colonize(map, "A"); var b = Colonize(map, "B"); var c = Colonize(map, "C");
            var planner = new ShipTransportPlanner(map, 0);

            var states = planner.BuildStates();
            ok &= Check(planner.LastRound == 1, "empty garrisons: round 1");
            ok &= Check(states.Find(s => s.Planet == a).Deficit == 4, "A needs its full garrison of 4");
            ok &= Check(states.Find(s => s.Planet == c).RoundGarrison == 0, "C has no garrison");

            Dock(c, 10);
            states = planner.BuildStates();
            ok &= Check(states.Find(s => s.Planet == c).Spare == 10, "all of C's ships are spare (garrison 0)");
            ok &= Check(states.Find(s => s.Planet == a).Spare == 0, "A holds nothing spare");

            Dock(a, 4); Dock(b, 4);
            states = planner.BuildStates();
            ok &= Check(planner.LastRound == 2, "all garrisons full: round advances to 2");
            ok &= Check(states.Find(s => s.Planet == a).RoundGarrison == 8 && states.Find(s => s.Planet == a).Deficit == 4,
                "round 2: A's target is 2 x 4 and it is 4 short");

            a.AddIncomingShips(Ship.ShipKind.WarShip, 0, 2);
            states = planner.BuildStates();
            ok &= Check(states.Find(s => s.Planet == a).Deficit == 2, "incoming ships count against the deficit");
            a.ClearIncomingShips();
        }
        finally { Object.DestroyImmediate(go); }

        ok &= RunNewPlanetDropsRoundCheck();
        ok &= RunUnreachablePlanetCheck();
        ok &= RunCategory5UnlockCheck();
        ok &= RunNoColonizedPlanetsCheck();
        return ok;
    }

    private static bool RunNewPlanetDropsRoundCheck()
    {
        var ok = true;
        _nextPlanetX = 0f;
        var go = new GameObject("STSelfCheckMap_NewPlanet");
        try
        {
            // A(Farm) - B(Desert) - D(Farm, new and empty)
            var map = BuildMap(go, NewConstants(),
                MakeSpawn("A", Planet.PlanetType.PlanetTypeFarm, new[] { "B" }),
                MakeSpawn("B", Planet.PlanetType.PlanetTypeDesert, new[] { "D" }),
                MakeSpawn("D", Planet.PlanetType.PlanetTypeFarm));
            var a = Colonize(map, "A"); var b = Colonize(map, "B"); var d = Colonize(map, "D");
            Dock(a, 6); Dock(b, 4);
            var planner = new ShipTransportPlanner(map, 0);
            var states = planner.BuildStates();
            ok &= Check(planner.LastRound == 1, "an empty new planet pins the round at 1 (new planets fill first)");
            ok &= Check(states.Find(s => s.Planet == a).Spare == 2, "A holds 2 above 1 x garrison: spare 2");
            ok &= Check(states.Find(s => s.Planet == d).Deficit == 4, "the new planet D is 4 short");
            Dock(d, 4);
            planner.BuildStates();
            ok &= Check(planner.LastRound == 2, "once D is filled the round returns to 2");
        }
        finally { Object.DestroyImmediate(go); }
        return ok;
    }

    private static bool RunUnreachablePlanetCheck()
    {
        var ok = true;
        _nextPlanetX = 0f;
        var go = new GameObject("STSelfCheckMap_Unreachable");
        try
        {
            // Z is colonized and isolated. GameAIMapInit logs a harmless "[PathingSystem] ... no connections" error for it.
            var map = BuildMap(go, NewConstants(),
                MakeSpawn("A", Planet.PlanetType.PlanetTypeFarm, new[] { "B" }),
                MakeSpawn("B", Planet.PlanetType.PlanetTypeDesert),
                MakeSpawn("Z", Planet.PlanetType.PlanetTypeFarm));
            var a = Colonize(map, "A"); var b = Colonize(map, "B"); Colonize(map, "Z");
            Dock(a, 4); Dock(b, 4);
            var planner = new ShipTransportPlanner(map, 0);
            planner.BuildStates();
            ok &= Check(planner.LastRound == 2, "an unreachable empty planet does not pin the round at 1");
        }
        finally { Object.DestroyImmediate(go); }
        return ok;
    }

    private static bool RunCategory5UnlockCheck()
    {
        var ok = true;
        foreach (var extraColonized in new[] { false, true })
        {
            _nextPlanetX = 0f;
            var go = new GameObject("STSelfCheckMap_Cat5");
            try
            {
                var spawns = new List<PlanetSpawnData>
                {
                    MakeSpawn("A", Planet.PlanetType.PlanetTypeFarm, new[] { "B" }),
                    MakeSpawn("B", Planet.PlanetType.PlanetTypeDesert, new[] { "C" }),
                    MakeSpawn("C", Planet.PlanetType.PlanetTypeNormal, new[] { "V" }),
                    MakeSpawn("V", Planet.PlanetType.PlanetTypeVerdant, extraColonized ? new[] { "E" } : null),
                };
                if (extraColonized) spawns.Add(MakeSpawn("E", Planet.PlanetType.PlanetTypeNormal));
                var map = BuildMap(go, NewConstants(), spawns.ToArray());
                var a = Colonize(map, "A"); var b = Colonize(map, "B"); Colonize(map, "C"); Colonize(map, "V");
                if (extraColonized) Colonize(map, "E");
                Dock(a, 4); Dock(b, 3);    // 7 warships
                var planner = new ShipTransportPlanner(map, 0);

                // Threshold = ratio 2 x colonized planets: 4 planets -> 8, 5 planets -> 10.
                ok &= Check(!planner.BuildStates().Exists(s => s.Planet.PlanetName == "V"),
                    $"7 ships is below the unlock threshold: V is excluded (extraColonized={extraColonized})");
                b.AddIncomingShips(Ship.ShipKind.WarShip, 0, 1);   // one ship in flight still counts: 7 + 1
                ok &= Check(planner.BuildStates().Exists(s => s.Planet.PlanetName == "V") == !extraColonized,
                    $"ships in flight count toward the unlock total (extraColonized={extraColonized})");
                b.ClearIncomingShips();
                Dock(b, 1);                // 8 warships
                var unlocked = planner.BuildStates().Exists(s => s.Planet.PlanetName == "V");
                ok &= Check(unlocked == !extraColonized,
                    $"8 ships unlocks V only when the threshold is 8, not 10 (extraColonized={extraColonized})");
                if (extraColonized)
                {
                    Dock(b, 2);            // 10 warships
                    ok &= Check(planner.BuildStates().Exists(s => s.Planet.PlanetName == "V"),
                        "10 ships unlocks V once the threshold has grown to 10");
                }
            }
            finally { Object.DestroyImmediate(go); }
        }
        return ok;
    }

    private static bool RunNoColonizedPlanetsCheck()
    {
        var ok = true;
        _nextPlanetX = 0f;
        var go = new GameObject("STSelfCheckMap_NoColonized");
        try
        {
            var map = BuildMap(go, NewConstants(),
                MakeSpawn("A", Planet.PlanetType.PlanetTypeFarm, new[] { "B" }),
                MakeSpawn("B", Planet.PlanetType.PlanetTypeDesert));
            var planner = new ShipTransportPlanner(map, 0);
            ok &= Check(planner.BuildStates().Count == 0, "a player with no colonized planets has no participants");
            ok &= Check(planner.LastRound == 1, "round defaults to 1 with no participants");
        }
        finally { Object.DestroyImmediate(go); }
        return ok;
    }

    public static bool RunPlanCheck()
    {
        var ok = true;

        // Chain A(Farm) - B(Desert) - C(Normal, spare 3). C is nearest to B.
        _nextPlanetX = 0f;
        var go = new GameObject("STSelfCheckMap_Plan1");
        try
        {
            var map = BuildMap(go, NewConstants(),
                MakeSpawn("A", Planet.PlanetType.PlanetTypeFarm, new[] { "B" }),
                MakeSpawn("B", Planet.PlanetType.PlanetTypeDesert, new[] { "C" }),
                MakeSpawn("C", Planet.PlanetType.PlanetTypeNormal));
            Colonize(map, "A"); Colonize(map, "B"); var c = Colonize(map, "C");
            Dock(c, 3);
            var actions = new ShipTransportPlanner(map, 0).Plan();
            ok &= Check(actions.Count == 1, "one source produces one action");
            ok &= Check(actions.Count == 1 && actions[0].Origin == "C" && actions[0].Target == "B",
                "same category: the nearer target (B) wins on path cost");
            ok &= Check(actions.Count == 1 && actions[0].Count == 3 && actions[0].Kind == Ship.ShipKind.WarShip,
                "count = min(spare 3, deficit 4) warships");
            ok &= Check(actions.Count == 1 && actions[0].Cost > 0f, "the action carries the path cost");

            Dock(c, 9);   // 12 spare
            actions = new ShipTransportPlanner(map, 0).Plan();
            ok &= Check(actions.Count == 1 && actions[0].Count == 4,
                "count is capped at the target's deficit (4), the rest stays home");
        }
        finally { Object.DestroyImmediate(go); }

        // Two sources with IDENTICAL spare counts and two targets: no crash, each target claimed once.
        _nextPlanetX = 0f;
        go = new GameObject("STSelfCheckMap_Plan2");
        try
        {
            var map = BuildMap(go, NewConstants(),
                MakeSpawn("A", Planet.PlanetType.PlanetTypeFarm, new[] { "B" }),
                MakeSpawn("B", Planet.PlanetType.PlanetTypeDesert, new[] { "C", "D" }),
                MakeSpawn("C", Planet.PlanetType.PlanetTypeNormal),
                MakeSpawn("D", Planet.PlanetType.PlanetTypeNormal));
            Colonize(map, "A"); Colonize(map, "B"); var c = Colonize(map, "C"); var d = Colonize(map, "D");
            Dock(c, 3); Dock(d, 3);
            var actions = new ShipTransportPlanner(map, 0).Plan();
            ok &= Check(actions.Count == 2, "two equal-spare sources both produce an action (no comparer collision)");
            ok &= Check(actions.Select(x => x.Target).Distinct().Count() == 2,
                "a target claimed by one source is removed for the other");
            ok &= Check(actions.All(x => x.Count == 3), "each action moves the source's 3 spare ships");
        }
        finally { Object.DestroyImmediate(go); }

        // Category beats cost: source S sits between Prime P (cat 3) and Desert Z (cat 1), equal distance.
        _nextPlanetX = 0f;
        go = new GameObject("STSelfCheckMap_Plan3");
        try
        {
            var map = BuildMap(go, NewConstants(),
                MakeSpawn("P", Planet.PlanetType.PlanetTypePrime, new[] { "S" }),
                MakeSpawn("S", Planet.PlanetType.PlanetTypeNormal, new[] { "Z" }),
                MakeSpawn("Z", Planet.PlanetType.PlanetTypeDesert));
            Colonize(map, "P"); var s = Colonize(map, "S"); Colonize(map, "Z");
            Dock(s, 2);
            var actions = new ShipTransportPlanner(map, 0).Plan();
            ok &= Check(actions.Count == 1 && actions[0].Target == "Z",
                "at equal cost the better category (Desert, 1) beats Prime (3)");
        }
        finally { Object.DestroyImmediate(go); }

        // Trip length limit, and empty plans.
        _nextPlanetX = 0f;
        go = new GameObject("STSelfCheckMap_Plan4");
        try
        {
            var constants = NewConstants();
            var map = BuildMap(go, constants,
                MakeSpawn("A", Planet.PlanetType.PlanetTypeFarm, new[] { "B" }),
                MakeSpawn("B", Planet.PlanetType.PlanetTypeNormal));
            Colonize(map, "A"); var b = Colonize(map, "B");
            var planner = new ShipTransportPlanner(map, 0);
            ok &= Check(planner.Plan().Count == 0, "no spare ships: empty plan, no exception");
            Dock(b, 2);
            ok &= Check(planner.Plan().Count == 1, "spare ships and a reachable target: one action");
            constants.maxPathNodesForShipTransport = 0;
            ok &= Check(planner.Plan().Count == 0, "targets beyond maxPathNodesForShipTransport are excluded");
            ok &= Check(new ShipTransportPlanner(map, 1).Plan().Count == 0,
                "a player who owns nothing gets an empty plan");
        }
        finally { Object.DestroyImmediate(go); }

        // A colonized planet with no route: PathingSystem reports a 1-node zero-cost path for it,
        // which must not be planned as a free trip. S-Y carry an explicit connection so the map uses
        // explicit-connection mode (with no connections anywhere it would connect planets by distance
        // and Z would NOT be isolated). Logs a harmless "[PathingSystem] ... no connections" error for Z.
        _nextPlanetX = 0f;
        go = new GameObject("STSelfCheckMap_Plan5");
        try
        {
            var map = BuildMap(go, NewConstants(),
                MakeSpawn("S", Planet.PlanetType.PlanetTypeNormal, new[] { "Y" }),
                MakeSpawn("Y", Planet.PlanetType.PlanetTypeNormal),
                MakeSpawn("Z", Planet.PlanetType.PlanetTypeFarm));
            var s = Colonize(map, "S"); Colonize(map, "Y"); Colonize(map, "Z");
            Dock(s, 2);
            ok &= Check(new ShipTransportPlanner(map, 0).Plan().Count == 0,
                "an isolated (unroutable) target is not planned as a reachable trip");
        }
        finally { Object.DestroyImmediate(go); }

        // Ships docked on a planet the player no longer has colonized (e.g. it flipped owner while
        // they were in flight) must still be movable: they count as spare (garrison 0).
        _nextPlanetX = 0f;
        go = new GameObject("STSelfCheckMap_Plan6");
        try
        {
            var map = BuildMap(go, NewConstants(),
                MakeSpawn("A", Planet.PlanetType.PlanetTypeFarm, new[] { "B" }),
                MakeSpawn("B", Planet.PlanetType.PlanetTypeNormal, new[] { "C" }),
                MakeSpawn("C", Planet.PlanetType.PlanetTypeNormal));
            Colonize(map, "A"); Colonize(map, "B");
            Dock(map.GetPlanet("C"), 2);   // C is not colonized by player 0
            var actions = new ShipTransportPlanner(map, 0).Plan();
            ok &= Check(actions.Exists(x => x.Origin == "C"),
                "ships stranded on a planet the player does not hold are sent out again");
        }
        finally { Object.DestroyImmediate(go); }
        return ok;
    }

    public static bool RunOrderExecutionCheck()
    {
        var ok = true;
        _nextPlanetX = 0f;
        var go = new GameObject("STSelfCheckMap_Orders");
        try
        {
            var map = BuildMap(go, NewConstants(),
                MakeSpawn("A", Planet.PlanetType.PlanetTypeNormal, new[] { "B" }),
                MakeSpawn("B", Planet.PlanetType.PlanetTypeNormal));
            var a = map.GetPlanet("A"); var b = map.GetPlanet("B");
            a.DockShipFromSave(Ship.ShipKind.WarShip, 1, new List<string> { "x" });
            a.DockShipFromSave(Ship.ShipKind.WarShip, 1, new List<string> { "y" });
            a.DockShipFromSave(Ship.ShipKind.WarShip, 1, new List<string> { "z" });

            var fleet = new GameAI.GameAIOrder.ShipFleetPayload
            {
                Kind = Ship.ShipKind.WarShip,
                Snapshots = a.PeekShipSnapshots(Ship.ShipKind.WarShip, 1, 2),
            };
            var departure = new GameAI.GameAIOrder
            {
                Type = GameAI.GameAIOrder.OrderType.OrderTypeShipDeparture,
                Data = 2, Origin = "A", Target = "A", PlayerId = 1, Fleet = fleet,
            };
            var inProgress = new GameAI.GameAIOrder
            {
                Type = GameAI.GameAIOrder.OrderType.OrderTypeShipTransferInProgress,
                Data = 2, Origin = "A", Target = "B", PlayerId = 1, Fleet = fleet,
            };
            var arrival = new GameAI.GameAIOrder
            {
                Type = GameAI.GameAIOrder.OrderType.OrderTypeShipTransport,
                Data = 2, Origin = "A", Target = "B", PlayerId = 1, Fleet = fleet,
            };

            GameAI.ApplyShipDeparture(a, departure);
            ok &= Check(a.DockedShips.Count == 1 && a.DockedShips[0].ResearchSnapshot[0] == "z",
                "departure undocks the first 2 ships (x, y); z stays");
            GameAI.ApplyShipTransferInProgress(b, inProgress);
            ok &= Check(b.GetIncomingShips(Ship.ShipKind.WarShip, 1) == 2, "in-progress raises the target's incoming count");
            GameAI.ApplyShipArrival(b, arrival);
            ok &= Check(b.DockedShips.Count == 2, "arrival docks 2 ships");
            ok &= Check(b.DockedShips.All(s => s.Owner == 1 && s.Kind == Ship.ShipKind.WarShip),
                "arrived ships belong to the ORDER's player, not the planet's owner");
            ok &= Check(b.DockedShips[0].ResearchSnapshot[0] == "x" && b.DockedShips[1].ResearchSnapshot[0] == "y",
                "arrived ships keep the snapshots they left with");
            ok &= Check(b.GetIncomingShips(Ship.ShipKind.WarShip, 1) == 0, "arrival clears the incoming count");

            // Recompute from in-flight orders (used after loading a save).
            b.AddIncomingShips(Ship.ShipKind.WarShip, 1, 7);   // stale value
            map.RecomputeIncomingShips(new List<GameAI.GameAIOrder> { arrival, departure });
            ok &= Check(b.GetIncomingShips(Ship.ShipKind.WarShip, 1) == 2,
                "recompute counts only in-flight ShipTransport orders and drops stale values");
            ok &= Check(a.GetIncomingShips(Ship.ShipKind.WarShip, 1) == 0, "other planets are reset to 0");
        }
        finally { Object.DestroyImmediate(go); }
        return ok;
    }

    public static bool RunFleetSaveCheck()
    {
        var ok = true;
        var fleet = new GameAI.GameAIOrder.ShipFleetPayload
        {
            Kind = Ship.ShipKind.WarShip,
            Snapshots = new List<List<string>> { new List<string> { "r1", "r2" }, new List<string>() },
        };
        var save = new SaveLoadSystem.GameSave.OrderSave
        {
            type = GameAI.GameAIOrder.OrderType.OrderTypeShipTransport,
            timingType = GameAI.GameAIOrder.OrderTimingType.OrderTimingTypeDelayed,
            timingDelay = 3, totalDelay = 3, data = 2f, dataType = "int",
            origin = "A", target = "B", playerId = 1,
            fleetShips = fleet.ToSave(1),
        };
        var json = JsonUtility.ToJson(save);
        var loaded = JsonUtility.FromJson<SaveLoadSystem.GameSave.OrderSave>(json);
        var restored = GameAI.GameAIOrder.ShipFleetPayload.FromSave(loaded.fleetShips);
        ok &= Check(restored != null && restored.Kind == Ship.ShipKind.WarShip, "fleet kind survives JSON");
        ok &= Check(restored != null && restored.Snapshots.Count == 2
                    && restored.Snapshots[0].SequenceEqual(new[] { "r1", "r2" })
                    && restored.Snapshots[1].Count == 0,
            "each ship's snapshot survives JSON, including an empty one");
        ok &= Check(loaded.fleetShips != null && loaded.fleetShips.All(s => s.owner == 1),
            "saved ships record the fleet owner");

        // An older save: an order entry with no fleetShips field at all.
        const string oldJson = "{\"type\":16,\"timingType\":0,\"timingDelay\":1,\"totalDelay\":1,\"data\":1.0,"
                               + "\"dataType\":\"int\",\"target\":\"B\",\"origin\":\"A\",\"playerId\":0}";
        var old = JsonUtility.FromJson<SaveLoadSystem.GameSave.OrderSave>(oldJson);
        ok &= Check(GameAI.GameAIOrder.ShipFleetPayload.FromSave(old.fleetShips) == null,
            "an older save with no fleetShips loads as no fleet");
        ok &= Check(GameAI.GameAIOrder.ShipFleetPayload.FromSave(null) == null, "null list gives no fleet");
        ok &= Check(GameAI.GameAIOrder.ShipFleetPayload.FromSave(new List<SaveLoadSystem.GameSave.ShipSave>()) == null,
            "empty list gives no fleet");
        return ok;
    }

    public static bool RunPlayerAIShipOrdersCheck()
    {
        var ok = true;
        _nextPlanetX = 0f;
        var mapGo = new GameObject("STSelfCheckMap_PlayerAI");
        var playerGo = new GameObject("STSelfCheckPlayer");
        try
        {
            var constants = NewConstants();
            constants.defaultTravelSpeed = 1000000f;   // cost / speed rounds to 0: the delay must be clamped to 1
            var map = BuildMap(mapGo, constants,
                MakeSpawn("A", Planet.PlanetType.PlanetTypeFarm, new[] { "B" }),
                MakeSpawn("B", Planet.PlanetType.PlanetTypeNormal));
            Colonize(map, "A"); var b = Colonize(map, "B");
            b.DockShipFromSave(Ship.ShipKind.WarShip, 0, new List<string> { "s1" });
            b.DockShipFromSave(Ship.ShipKind.WarShip, 0, new List<string> { "s2" });

            var player = playerGo.AddComponent<Player>();
            var playerAI = playerGo.AddComponent<PlayerAI>();
            playerAI.Player = player;
            playerAI.AIMap = map;
            player.playerID = 0;

            var orders = new List<GameAI.GameAIOrder>();
            playerAI.ProcessShipActions(orders);

            ok &= Check(orders.Count == 3, "one action yields the trio: transport, departure, in-progress");
            var transport = orders.Find(o => o.Type == GameAI.GameAIOrder.OrderType.OrderTypeShipTransport);
            var departure = orders.Find(o => o.Type == GameAI.GameAIOrder.OrderType.OrderTypeShipDeparture);
            var progress  = orders.Find(o => o.Type == GameAI.GameAIOrder.OrderType.OrderTypeShipTransferInProgress);
            ok &= Check(transport != null && departure != null && progress != null, "all three order types are present");
            if (transport == null || departure == null || progress == null) return false;

            ok &= Check(transport.TimingType == GameAI.GameAIOrder.OrderTimingType.OrderTimingTypeDelayed
                        && transport.Origin == "B" && transport.Target == "A" && System.Convert.ToInt32(transport.Data) == 2,
                "the arrival is delayed, B -> A, carrying 2 ships");
            ok &= Check(transport.TimingDelay >= 1 && transport.TotalDelay >= 1,
                "a delay that rounds to 0 is clamped to at least 1 (never runs twice)");
            ok &= Check(departure.TimingType == GameAI.GameAIOrder.OrderTimingType.OrderTimingTypeImmediate
                        && departure.Origin == "B" && departure.Target == "B",
                "departure is immediate and acts on the origin");
            ok &= Check(progress.TimingType == GameAI.GameAIOrder.OrderTimingType.OrderTimingTypeImmediate
                        && progress.Target == "A", "in-progress is immediate and flags the destination");
            ok &= Check(transport.Fleet != null && transport.Fleet.Snapshots.Count == 2
                        && transport.Fleet.Snapshots[0][0] == "s1" && transport.Fleet.Snapshots[1][0] == "s2",
                "the payload carries the snapshots of the ships that will leave");
            ok &= Check(transport.PlayerId == 0, "orders are stamped with the player");

            // Apply the trio the way ProcessNewOrders would: immediates now, the arrival later.
            GameAI.ApplyShipDeparture(b, departure);
            GameAI.ApplyShipTransferInProgress(map.GetPlanet("A"), progress);
            ok &= Check(b.DockedShips.Count == 0, "the ships left B");
            GameAI.ApplyShipArrival(map.GetPlanet("A"), transport);
            var arrived = map.GetPlanet("A").DockedShips;
            ok &= Check(arrived.Count == 2 && arrived[0].ResearchSnapshot[0] == "s1" && arrived[1].ResearchSnapshot[0] == "s2",
                "the ships arrive at A with their snapshots");
        }
        finally
        {
            Object.DestroyImmediate(playerGo);
            Object.DestroyImmediate(mapGo);
        }
        return ok;
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

    private static ShipTransportPlanner ConsolidatePlanner(GameAIMap map, string heldPlanet = null)
        => new ShipTransportPlanner(map, 0, PlayerAI.AIStrategy.AIStrategyConsolidate) { HeldPlanet = heldPlanet };

    public static bool RunConsolidatePlannerChecks()
    {
        var ok = RunSharedOuterDefinitionCheck();
        ok &= RunConsolidateRolesCheck();
        ok &= RunConsolidateRankCheck();
        ok &= RunHeldPlanetCheck();
        return ok;
    }

    private static bool RunSharedOuterDefinitionCheck()
    {
        var ok = true;
        _nextPlanetX = 0f;
        var go = new GameObject("STSelfCheckMap_SharedOuter");
        try
        {
            // A0 - A1 - E: A0 and A1 are player 0's, E is player 1's.
            var map = BuildMap(go, NewConstants(),
                MakeSpawn("A0", Planet.PlanetType.PlanetTypeNormal, new[] { "A1" }),
                MakeSpawn("A1", Planet.PlanetType.PlanetTypeNormal, new[] { "E" }),
                MakeSpawn("E", Planet.PlanetType.PlanetTypeNormal));
            var a0 = Colonize(map, "A0"); var a1 = Colonize(map, "A1");
            var e = Colonize(map, "E", 1);
            var planner = new ShipTransportPlanner(map, 0);

            ok &= Check(!planner.IsOuter(a0), "A0's only neighbour is its own colony: not outer");
            ok &= Check(planner.IsOuter(a1), "A1 borders an enemy-held planet: outer (a neighbour not colonized by me)");

            e.Owner = Planet.NoOwner;   // contested / tied: still not colonized by me
            ok &= Check(planner.IsOuter(a1), "a contested (ownerless) neighbour also makes A1 outer");
        }
        finally { Object.DestroyImmediate(go); }
        return ok;
    }

    private static bool RunConsolidateRolesCheck()
    {
        var ok = true;

        // I(Farm) - O(Normal) - U(empty): O is outer, I is interior.
        _nextPlanetX = 0f;
        var go = new GameObject("STSelfCheckMap_ConsolidateRoles");
        try
        {
            var map = BuildMap(go, NewConstants(),
                MakeSpawn("I", Planet.PlanetType.PlanetTypeFarm, new[] { "O" }),
                MakeSpawn("O", Planet.PlanetType.PlanetTypeNormal, new[] { "U" }),
                MakeSpawn("U", Planet.PlanetType.PlanetTypeNormal));
            var i = Colonize(map, "I"); var o = Colonize(map, "O");
            Dock(o, 12); Dock(i, 5);

            var consolidate = ConsolidatePlanner(map);
            var states = consolidate.BuildStates();
            ok &= Check(consolidate.LastRound == 1, "Consolidate: the round is fixed at 1");
            var oState = states.Find(s => s.Planet == o);
            ok &= Check(oState.Garrison == 6 && oState.RoundGarrison == 6 && oState.Spare == 6,
                "Consolidate: outer O keeps a round-1 garrison of 6 and 12 docked leaves 6 spare");
            var iState = states.Find(s => s.Planet == i);
            ok &= Check(iState.Garrison == 0 && iState.Category == ShipTransportPlanner.NoCategory && iState.Spare == 5,
                "Consolidate: interior I keeps no garrison, so all 5 of its ships are spare");

            var expand = new ShipTransportPlanner(map, 0);
            var expandStates = expand.BuildStates();
            ok &= Check(expand.LastRound == 2 && expandStates.Find(s => s.Planet == i).Garrison == 4,
                "Expand is unchanged: round 2 and I still garrisons 4");
        }
        finally { Object.DestroyImmediate(go); }

        // V(Verdant) - O - U: a category-5-only planet is locked under Expand but is a spare-only source under Consolidate.
        _nextPlanetX = 0f;
        go = new GameObject("STSelfCheckMap_ConsolidateLocked");
        try
        {
            var map = BuildMap(go, NewConstants(),
                MakeSpawn("V", Planet.PlanetType.PlanetTypeVerdant, new[] { "O" }),
                MakeSpawn("O", Planet.PlanetType.PlanetTypeNormal, new[] { "U" }),
                MakeSpawn("U", Planet.PlanetType.PlanetTypeNormal));
            var v = Colonize(map, "V"); Colonize(map, "O");
            Dock(v, 3);   // 3 warships < 2 x 2 colonized planets, so V is locked under Expand

            ok &= Check(!new ShipTransportPlanner(map, 0).BuildStates().Exists(s => s.Planet == v),
                "Expand: a locked category-5-only planet is left out");
            var vState = ConsolidatePlanner(map).BuildStates().Find(s => s.Planet == v);
            ok &= Check(vState != null && vState.Spare == 3,
                "Consolidate: the same planet is included and all 3 of its ships are spare");
        }
        finally { Object.DestroyImmediate(go); }
        return ok;
    }

    private static bool RunConsolidateRankCheck()
    {
        var ok = true;
        _nextPlanetX = 0f;
        var go = new GameObject("STSelfCheckMap_Rank");
        try
        {
            // F(Farm, outer via W) - O(Normal, outer via U) - I(Desert, interior).
            var map = BuildMap(go, NewConstants(),
                MakeSpawn("F", Planet.PlanetType.PlanetTypeFarm, new[] { "W", "O" }),
                MakeSpawn("O", Planet.PlanetType.PlanetTypeNormal, new[] { "U", "I" }),
                MakeSpawn("I", Planet.PlanetType.PlanetTypeDesert),
                MakeSpawn("U", Planet.PlanetType.PlanetTypeNormal),
                MakeSpawn("W", Planet.PlanetType.PlanetTypeNormal));
            var f = Colonize(map, "F"); var o = Colonize(map, "O"); var i = Colonize(map, "I");

            var expand = new ShipTransportPlanner(map, 0);
            ok &= Check(expand.TargetRank(f) == 1 && expand.TargetRank(o) == 2 && expand.TargetRank(i) == 1,
                "Expand: rank equals category (interior Desert is not pushed behind outer Normal)");

            var consolidate = ConsolidatePlanner(map);
            ok &= Check(consolidate.TargetRank(f) == 1 && consolidate.TargetRank(o) == 2,
                "Consolidate: outer planets keep their category order (Farm before Normal)");
            ok &= Check(consolidate.TargetRank(i) == 101 && consolidate.TargetRank(i) > consolidate.TargetRank(o),
                "Consolidate: an interior planet ranks behind every outer planet");
        }
        finally { Object.DestroyImmediate(go); }
        return ok;
    }

    private static bool RunHeldPlanetCheck()
    {
        var ok = true;
        _nextPlanetX = 0f;
        var go = new GameObject("STSelfCheckMap_Held");
        try
        {
            // O(mine, outer because of E) - E(enemy). 3 of my warships sit on E; O is 4 short.
            var map = BuildMap(go, NewConstants(),
                MakeSpawn("O", Planet.PlanetType.PlanetTypeNormal, new[] { "E" }),
                MakeSpawn("E", Planet.PlanetType.PlanetTypeNormal));
            var o = Colonize(map, "O"); var e = Colonize(map, "E", 1);
            Dock(o, 2); Dock(e, 3);

            var free = ConsolidatePlanner(map);
            ok &= Check(free.BuildStates().Exists(s => s.Planet == e && s.Spare == 3),
                "without a held planet, ships on an enemy planet are stranded (all spare)");
            ok &= Check(free.Plan().Exists(a => a.Origin == "E" && a.Target == "O" && a.Count == 3),
                "and would be sent home to fill O's garrison");

            var held = ConsolidatePlanner(map, "E");
            ok &= Check(!held.BuildStates().Exists(s => s.Planet == e),
                "with E held, its ships are not a state at all");
            ok &= Check(!held.Plan().Exists(a => a.Origin == "E"),
                "and the home plan never moves them");
        }
        finally { Object.DestroyImmediate(go); }
        return ok;
    }

    // A - B - E - F. A and B are player 0's; E and F are player 1's. F is two hops from B, so it is
    // unknown to player 0. Knowledge is updated for two players.
    private static GameAIMap BuildAssaultLine(GameObject go)
    {
        var map = BuildMap(go, NewConstants(),
            MakeSpawn("A", Planet.PlanetType.PlanetTypeNormal, new[] { "B" }),
            MakeSpawn("B", Planet.PlanetType.PlanetTypeNormal, new[] { "E" }),
            MakeSpawn("E", Planet.PlanetType.PlanetTypeNormal, new[] { "F" }),
            MakeSpawn("F", Planet.PlanetType.PlanetTypeNormal));
        Colonize(map, "A"); Colonize(map, "B"); Colonize(map, "E", 1); Colonize(map, "F", 1);
        return map;
    }

    public static bool RunAssaultChecks()
    {
        var ok = RunAssaultSizingCheck();
        ok &= RunAssaultTargetCheck();
        ok &= RunAssaultPlanCheck();
        return ok;
    }

    private static bool RunAssaultSizingCheck()
    {
        var ok = true;
        _nextPlanetX = 0f;
        var go = new GameObject("STSelfCheckMap_AssaultSizing");
        try
        {
            var map = BuildAssaultLine(go);
            var e = map.GetPlanet("E"); var f = map.GetPlanet("F");
            Dock(e, 4, owner: 1);
            Dock(f, 10, owner: 1);    // unknown to player 0: must not count
            Dock(e, 2, owner: -1);    // ownerless: must not count
            map.Knowledge.Update(map, numPlayers: 2);
            var assault = new AssaultPlanner(map, 0);

            ok &= Check(assault.EnemyWarshipTotal() == 4,
                "enemy total counts only known planets and only ships with a valid, different owner");
            ok &= Check(assault.RequiredForce() == 6, "required force is ceil(4 x 1.5) = 6");

            // Deficit counts my docked ships and incoming ships at the target.
            ok &= Check(assault.Deficit(e) == 6, "nothing committed yet: the deficit is the full 6");
            Dock(e, 2);
            e.AddIncomingShips(Ship.ShipKind.WarShip, 0, 1);
            ok &= Check(assault.Deficit(e) == 3, "2 docked + 1 incoming leaves a deficit of 3");
            Dock(e, 3);
            ok &= Check(assault.Deficit(e) == 0, "5 docked + 1 incoming meets the force: deficit 0");
        }
        finally { Object.DestroyImmediate(go); }

        _nextPlanetX = 0f;
        go = new GameObject("STSelfCheckMap_AssaultFloor");
        try
        {
            var map = BuildAssaultLine(go);   // no enemy ships anywhere
            map.Knowledge.Update(map, numPlayers: 2);
            ok &= Check(new AssaultPlanner(map, 0).RequiredForce() == 3,
                "with no enemy ships the minimum of 3 applies");
        }
        finally { Object.DestroyImmediate(go); }
        return ok;
    }

    private static bool RunAssaultTargetCheck()
    {
        var ok = true;

        // No holders / no known enemy -> no target.
        _nextPlanetX = 0f;
        var go = new GameObject("STSelfCheckMap_AssaultNoTarget");
        try
        {
            var map = BuildAssaultLine(go);
            map.Knowledge.Update(map, numPlayers: 2);
            ok &= Check(new AssaultPlanner(map, 0).ChooseTarget() == null,
                "no warships anywhere: no target");
            Dock(map.GetPlanet("B"), 3);
            ok &= Check(new AssaultPlanner(map, 0).ChooseTarget() == map.GetPlanet("E"),
                "warships at B and a known enemy-occupied E: E is the target");
            ok &= Check(new AssaultPlanner(map, 0).ChooseTarget() != map.GetPlanet("F"),
                "F is unknown to player 0 and never a candidate");
        }
        finally { Object.DestroyImmediate(go); }

        // Two enemy planets off B: nearest first, then sticky once ships are committed.
        _nextPlanetX = 0f;
        go = new GameObject("STSelfCheckMap_AssaultSticky");
        try
        {
            var map = BuildMap(go, NewConstants(),
                MakeSpawn("A", Planet.PlanetType.PlanetTypeNormal, new[] { "B" }),
                MakeSpawn("B", Planet.PlanetType.PlanetTypeNormal, new[] { "E1", "E2" }),
                MakeSpawn("E1", Planet.PlanetType.PlanetTypeNormal),
                MakeSpawn("E2", Planet.PlanetType.PlanetTypeNormal));
            Colonize(map, "A"); var b = Colonize(map, "B");
            var e1 = Colonize(map, "E1", 1); var e2 = Colonize(map, "E2", 1);
            Dock(b, 2);
            map.Knowledge.Update(map, numPlayers: 2);
            var assault = new AssaultPlanner(map, 0);

            ok &= Check(assault.ChooseTarget() == e1, "nothing committed: the cheapest path (E1) wins");
            Dock(e2, 1);
            ok &= Check(assault.ChooseTarget() == e2, "sticky: a planet with my ships already docked beats a cheaper one");
            b.UndockShips(Ship.ShipKind.WarShip, 0, 2);
            ok &= Check(assault.ChooseTarget() == e2,
                "sticky even when E2 holds ALL my warships (no other holder to path from)");
        }
        finally { Object.DestroyImmediate(go); }
        return ok;
    }

    private static bool RunAssaultPlanCheck()
    {
        var ok = true;

        // Spare ships beyond B's outer garrison (10 - 6 = 4) and all of interior A (5) go to E: cheapest first.
        _nextPlanetX = 0f;
        var go = new GameObject("STSelfCheckMap_AssaultPlan");
        try
        {
            var map = BuildAssaultLine(go);
            Dock(map.GetPlanet("E"), 4, owner: 1);
            Dock(map.GetPlanet("B"), 10); Dock(map.GetPlanet("A"), 5);
            map.Knowledge.Update(map, numPlayers: 2);
            var assault = new AssaultPlanner(map, 0);
            var target = assault.ChooseTarget();
            var transport = ConsolidatePlanner(map, target.PlanetName);
            var home = transport.Plan();
            ok &= Check(home.Count == 0, "B's outer garrison is full and A holds none: no home actions");

            var actions = assault.Plan(target, transport.LastStates, home);
            ok &= Check(actions.Count == 2
                        && actions.Exists(a => a.Origin == "B" && a.Target == "E" && a.Count == 4)
                        && actions.Exists(a => a.Origin == "A" && a.Target == "E" && a.Count == 2),
                "required 6 = B's 4 spare (cheapest path) + 2 of A's 5");
        }
        finally { Object.DestroyImmediate(go); }

        // Ships an outer garrison needs are not also committed to the assault.
        _nextPlanetX = 0f;
        go = new GameObject("STSelfCheckMap_AssaultAfterHome");
        try
        {
            var map = BuildAssaultLine(go);
            Dock(map.GetPlanet("E"), 4, owner: 1);
            Dock(map.GetPlanet("B"), 3); Dock(map.GetPlanet("A"), 9);
            map.Knowledge.Update(map, numPlayers: 2);
            var assault = new AssaultPlanner(map, 0);
            var target = assault.ChooseTarget();
            var transport = ConsolidatePlanner(map, target.PlanetName);
            var home = transport.Plan();
            ok &= Check(home.Count == 1 && home[0].Origin == "A" && home[0].Target == "B" && home[0].Count == 3,
                "home first: A sends 3 to fill outer B's garrison of 6");

            var actions = assault.Plan(target, transport.LastStates, home);
            ok &= Check(actions.Count == 1 && actions[0].Origin == "A" && actions[0].Target == "E" && actions[0].Count == 6,
                "the assault gets only A's remaining 6 (9 - 3), never the ships home defence claimed");
        }
        finally { Object.DestroyImmediate(go); }
        return ok;
    }

    public static bool RunConsolidatePlanShipActionsCheck()
    {
        var ok = true;
        _nextPlanetX = 0f;
        var mapGo = new GameObject("STSelfCheckMap_PlanShipActions");
        var playerGo = new GameObject("STSelfCheckPlayer_PlanShipActions");
        try
        {
            var map = BuildAssaultLine(mapGo);
            Dock(map.GetPlanet("E"), 4, owner: 1);
            Dock(map.GetPlanet("B"), 10); Dock(map.GetPlanet("A"), 5);
            map.Knowledge.Update(map, numPlayers: 2);

            var player = playerGo.AddComponent<Player>();
            var playerAI = playerGo.AddComponent<PlayerAI>();
            playerAI.Player = player;
            playerAI.AIMap = map;
            player.playerID = 0;

            playerAI.Strategy = PlayerAI.AIStrategy.AIStrategyConsolidate;
            var actions = playerAI.PlanShipActions(0);
            ok &= Check(actions.Count == 2
                        && actions.Exists(a => a.Origin == "B" && a.Target == "E" && a.Count == 4)
                        && actions.Exists(a => a.Origin == "A" && a.Target == "E" && a.Count == 2),
                "Consolidate: PlanShipActions sends the assault force to the enemy planet E");

            playerAI.Strategy = PlayerAI.AIStrategy.AIStrategyExpand;
            ok &= Check(!playerAI.PlanShipActions(0).Exists(a => a.Target == "E"),
                "Expand never targets an enemy-occupied planet");
        }
        finally
        {
            Object.DestroyImmediate(playerGo);
            Object.DestroyImmediate(mapGo);
        }
        return ok;
    }

    // Review fix 1: in-flight ships are counted per player, so an enemy fleet heading for its own
    // planet is not mistaken for mine (sticky target, deficit) and mine does not under-fill its garrison.
    public static bool RunIncomingPerPlayerCheck()
    {
        var ok = true;

        _nextPlanetX = 0f;
        var go = new GameObject("STSelfCheckMap_IncomingPerPlayer");
        try
        {
            var map = BuildAssaultLine(go);
            var e = map.GetPlanet("E");
            e.AddIncomingShips(Ship.ShipKind.WarShip, 1, 4);
            ok &= Check(e.GetIncomingShips(Ship.ShipKind.WarShip, 1) == 4
                        && e.GetIncomingShips(Ship.ShipKind.WarShip, 0) == 0,
                "incoming ships are counted per owning player");

            var arrival = new GameAI.GameAIOrder
            {
                Type = GameAI.GameAIOrder.OrderType.OrderTypeShipTransport,
                Data = 2, Origin = "B", Target = "E", PlayerId = 0,
                Fleet = new GameAI.GameAIOrder.ShipFleetPayload
                {
                    Kind = Ship.ShipKind.WarShip,
                    Snapshots = new List<List<string>> { new List<string>(), new List<string>() },
                },
            };
            GameAI.ApplyShipTransferInProgress(e, arrival);
            ok &= Check(e.GetIncomingShips(Ship.ShipKind.WarShip, 0) == 2
                        && e.GetIncomingShips(Ship.ShipKind.WarShip, 1) == 4,
                "my fleet's in-progress order raises only MY counter");
            GameAI.ApplyShipArrival(e, arrival);
            ok &= Check(e.GetIncomingShips(Ship.ShipKind.WarShip, 0) == 0
                        && e.GetIncomingShips(Ship.ShipKind.WarShip, 1) == 4,
                "my fleet's arrival lowers only MY counter");

            map.RecomputeIncomingShips(new List<GameAI.GameAIOrder> { arrival });
            ok &= Check(e.GetIncomingShips(Ship.ShipKind.WarShip, 0) == 2
                        && e.GetIncomingShips(Ship.ShipKind.WarShip, 1) == 0,
                "recompute after load keeps each order's ships under that order's player");
        }
        finally { Object.DestroyImmediate(go); }

        // Two enemy planets off B; enemy ships heading for the dearer one must not make it my sticky target.
        _nextPlanetX = 0f;
        go = new GameObject("STSelfCheckMap_EnemyIncomingTarget");
        try
        {
            var map = BuildMap(go, NewConstants(),
                MakeSpawn("A", Planet.PlanetType.PlanetTypeNormal, new[] { "B" }),
                MakeSpawn("B", Planet.PlanetType.PlanetTypeNormal, new[] { "E1", "E2" }),
                MakeSpawn("E1", Planet.PlanetType.PlanetTypeNormal),
                MakeSpawn("E2", Planet.PlanetType.PlanetTypeNormal));
            Colonize(map, "A"); var b = Colonize(map, "B");
            var e1 = Colonize(map, "E1", 1); var e2 = Colonize(map, "E2", 1);
            Dock(b, 2);
            e2.AddIncomingShips(Ship.ShipKind.WarShip, 1, 4);
            map.Knowledge.Update(map, numPlayers: 2);
            var assault = new AssaultPlanner(map, 0);
            ok &= Check(assault.ChooseTarget() == e1,
                "enemy ships in flight to E2 do not make E2 my sticky target: cheapest E1 still wins");
            ok &= Check(assault.Deficit(e2) == assault.RequiredForce(),
                "enemy ships in flight do not reduce MY deficit");
        }
        finally { Object.DestroyImmediate(go); }
        return ok;
    }

    // Review fix 2: a planet I hold with a few enemy colonists is not an enemy planet to attack;
    // otherwise my own garrison there makes it the sticky target and the assault never leaves home.
    public static bool RunAssaultTargetExcludesOwnPlanetsCheck()
    {
        var ok = true;
        _nextPlanetX = 0f;
        var go = new GameObject("STSelfCheckMap_MixedOwnPlanet");
        try
        {
            var map = BuildAssaultLine(go);
            var b = map.GetPlanet("B");
            b.Population.Add(new Planet.Inhabitant { Player = 1 });   // B is mine (Owner 0) but has an enemy colonist
            Dock(b, 6);
            map.Knowledge.Update(map, numPlayers: 2);

            var assault = new AssaultPlanner(map, 0);
            ok &= Check(assault.ChooseTarget() == map.GetPlanet("E"),
                "the target is the enemy-held E, not my own mixed planet B holding my 6-ship garrison");
        }
        finally { Object.DestroyImmediate(go); }
        return ok;
    }

    // Review fix 3: two fleets leaving one planet in the same turn (home defence + assault) must carry
    // disjoint ships' research snapshots, matching the ships each departure order actually undocks.
    public static bool RunSameOriginSnapshotCheck()
    {
        var ok = true;
        _nextPlanetX = 0f;
        var mapGo = new GameObject("STSelfCheckMap_SameOrigin");
        var playerGo = new GameObject("STSelfCheckPlayer_SameOrigin");
        try
        {
            var map = BuildAssaultLine(mapGo);
            Dock(map.GetPlanet("E"), 4, owner: 1);
            Dock(map.GetPlanet("B"), 3);
            var a = map.GetPlanet("A");
            for (var i = 1; i <= 9; i++)
                a.DockShipFromSave(Ship.ShipKind.WarShip, 0, new List<string> { "s" + i });
            map.Knowledge.Update(map, numPlayers: 2);

            var player = playerGo.AddComponent<Player>();
            var playerAI = playerGo.AddComponent<PlayerAI>();
            playerAI.Player = player;
            playerAI.AIMap = map;
            player.playerID = 0;
            playerAI.Strategy = PlayerAI.AIStrategy.AIStrategyConsolidate;

            var orders = new List<GameAI.GameAIOrder>();
            playerAI.ProcessShipActions(orders);

            var fromA = orders.FindAll(o => o.Type == GameAI.GameAIOrder.OrderType.OrderTypeShipTransport && o.Origin == "A");
            ok &= Check(fromA.Count == 2, "A sends two fleets in one turn (3 home to B, 6 to E)");

            var seen = new HashSet<string>();
            var total = 0;
            var distinct = true;
            foreach (var order in fromA)
                foreach (var snapshot in order.Fleet.Snapshots)
                {
                    total++;
                    if (!seen.Add(snapshot[0])) distinct = false;
                }
            ok &= Check(total == 9 && distinct,
                "the two fleets carry 9 different ships' snapshots, none duplicated");
        }
        finally
        {
            Object.DestroyImmediate(playerGo);
            Object.DestroyImmediate(mapGo);
        }
        return ok;
    }
}
