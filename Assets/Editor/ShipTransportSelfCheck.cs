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
        ok &= RunPlanetShipHelpersCheck();
        ok &= RunCategoryCheck();
        ok &= RunRoundAndRolesCheck();
        ok &= RunPlanCheck();
        ok &= RunOrderExecutionCheck();
        ok &= RunFleetSaveCheck();
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

            ok &= Check(a.GetIncomingShips(Ship.ShipKind.WarShip) == 0, "incoming starts at 0");
            a.AddIncomingShips(Ship.ShipKind.WarShip, 3);
            ok &= Check(a.GetIncomingShips(Ship.ShipKind.WarShip) == 3, "incoming adds");
            a.AddIncomingShips(Ship.ShipKind.WarShip, -5);
            ok &= Check(a.GetIncomingShips(Ship.ShipKind.WarShip) == 0, "incoming never goes below 0");
            a.AddIncomingShips(Ship.ShipKind.WarShip, 2);
            a.ClearIncomingShips();
            ok &= Check(a.GetIncomingShips(Ship.ShipKind.WarShip) == 0, "ClearIncomingShips resets");
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

            a.AddIncomingShips(Ship.ShipKind.WarShip, 2);
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
        // which must not be planned as a free trip. Logs a harmless "[PathingSystem] ... no connections" error.
        _nextPlanetX = 0f;
        go = new GameObject("STSelfCheckMap_Plan5");
        try
        {
            var map = BuildMap(go, NewConstants(),
                MakeSpawn("S", Planet.PlanetType.PlanetTypeNormal),
                MakeSpawn("Z", Planet.PlanetType.PlanetTypeFarm));
            var s = Colonize(map, "S"); Colonize(map, "Z");
            Dock(s, 2);
            ok &= Check(new ShipTransportPlanner(map, 0).Plan().Count == 0,
                "an isolated (unroutable) target is not planned as a reachable trip");
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
            ok &= Check(b.GetIncomingShips(Ship.ShipKind.WarShip) == 2, "in-progress raises the target's incoming count");
            GameAI.ApplyShipArrival(b, arrival);
            ok &= Check(b.DockedShips.Count == 2, "arrival docks 2 ships");
            ok &= Check(b.DockedShips.All(s => s.Owner == 1 && s.Kind == Ship.ShipKind.WarShip),
                "arrived ships belong to the ORDER's player, not the planet's owner");
            ok &= Check(b.DockedShips[0].ResearchSnapshot[0] == "x" && b.DockedShips[1].ResearchSnapshot[0] == "y",
                "arrived ships keep the snapshots they left with");
            ok &= Check(b.GetIncomingShips(Ship.ShipKind.WarShip) == 0, "arrival clears the incoming count");

            // Recompute from in-flight orders (used after loading a save).
            b.AddIncomingShips(Ship.ShipKind.WarShip, 7);   // stale value
            map.RecomputeIncomingShips(new List<GameAI.GameAIOrder> { arrival, departure });
            ok &= Check(b.GetIncomingShips(Ship.ShipKind.WarShip) == 2,
                "recompute counts only in-flight ShipTransport orders and drops stale values");
            ok &= Check(a.GetIncomingShips(Ship.ShipKind.WarShip) == 0, "other planets are reset to 0");
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
