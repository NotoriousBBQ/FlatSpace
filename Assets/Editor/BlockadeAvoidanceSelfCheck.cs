using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using FlatSpace.AI;
using FlatSpace.Pathing;
using Flatspace.Objects.Production;
using Flatspace.Objects.Resource;

public static class BlockadeAvoidanceSelfCheck
{
    [MenuItem("FlatSpace/AI/Run Blockade Avoidance Self-Check")]
    public static void Run() => RunChecks();

    public static bool RunChecks()
    {
        var ok = RunBlockadeViewCheck();
        ok &= RunRoutePlannerCheck();
        ok &= RunShortestPathAvoidingCheck();
        ok &= RunShipmentPlannerCheck();
        ok &= RunCarriedRouteCheck();
        ok &= RunShipmentOriginCheck();
        ok &= RunColonizationRoutingCheck();
        ok &= RunShipmentPlanningCheck();
        ok &= RunShipmentMinFractionCheck();
        ok &= RunProductionResponseCheck();
        ok &= RunColonizeHeldBackStateCheck();
        ok &= RunBlockadeMemoryCheck();
        ok &= RunBlockadeCutsCheck();
        ok &= RunLearningRoutingCheck();
        ok &= RunRememberedBlockadeSaveCheck();
        Debug.Log(ok
            ? "[BlockadeAvoidanceSelfCheck] ALL PASSED"
            : "[BlockadeAvoidanceSelfCheck] FAILURES (see errors above)");
        return ok;
    }

    private static bool Check(bool condition, string label)
    {
        if (!condition) Debug.LogError($"[BlockadeAvoidanceSelfCheck] FAIL: {label}");
        return condition;
    }

    private static bool Near(float a, float b) => Mathf.Abs(a - b) < 0.01f;

    // Explicit positions: every planet needs a distinct position (PathingSystem.FindPath tie-break fragility).
    public static PlanetSpawnData Spawn(string name, float x, float y, IEnumerable<string> connections = null,
        int initialPopulation = 0)
    {
        var resourceData = ScriptableObject.CreateInstance<PlanetResourceData>();
        resourceData._initialPopulation = initialPopulation;
        resourceData._maxPopulation = 5;
        resourceData._baseFoodProduction = 1f;   // so a colonist never needs a food rider

        var spawn = ScriptableObject.CreateInstance<PlanetSpawnData>();
        spawn._planetName = name;
        spawn._planetPosition = new Vector3(x, y, 0f);
        spawn._planetType = Planet.PlanetType.PlanetTypeNormal;
        spawn._resourceData = resourceData;
        spawn._connections = connections != null ? new List<string>(connections) : new List<string>();
        return spawn;
    }

    public static GameAIMap Build(GameObject go, GameAIConstants constants, params PlanetSpawnData[] spawns)
    {
        var map = go.AddComponent<GameAIMap>();
        map.GameAIMapInit(new List<PlanetSpawnData>(spawns), constants);
        return map;
    }

    // A - B - C - D in a line, 100 apart.
    private static GameAIMap BuildChain(GameObject go, GameAIConstants constants)
        => Build(go, constants,
            Spawn("A", 0f, 0f, new[] { "B" }),
            Spawn("B", 100f, 0f, new[] { "A", "C" }),
            Spawn("C", 200f, 0f, new[] { "B", "D" }),
            Spawn("D", 300f, 0f, new[] { "C" }));

    public static bool RunBlockadeViewCheck()
    {
        var ok = true;
        var go = new GameObject("BASelfCheckMap_View");
        var template = WarshipSelfCheck.MakeTemplate();       // offense 10 per warship
        var constants = WarshipSelfCheck.MakeConstants(template);
        var research = WarshipSelfCheck.MakeResearch();
        try
        {
            var map = BuildChain(go, constants);
            var blockade = new BlockadeSystem(map, research);

            // Player 0's only presence is a colony ship at A (no offense): A is a vision source, B its neighbour.
            map.GetPlanet("A").DockShipFromSave(Ship.ShipKind.ColonyShip, 0, new List<string>());
            WarshipSelfCheck.DockWarships(map.GetPlanet("B"), 1, 1);   // player 1: 10 at B (visible, next to A)
            WarshipSelfCheck.DockWarships(map.GetPlanet("C"), 1, 1);   // player 1: 10 at C (two hops away: NOT visible)

            var view = BlockadeView.Build(map, 0, blockade);
            ok &= Check(view.IsBlockaded("B") && Near(view.Value("B"), 10f) && view.Blocker("B") == 1,
                "a blockaded direct neighbour of my presence is seen: value 10, blocker player 1");
            ok &= Check(!view.IsBlockaded("C"), "a blockaded planet two hops from any presence is not visible");
            ok &= Check(!view.IsBlockaded("D") && !view.IsBlockaded("A"), "unblockaded planets are not listed");
            ok &= Check(new List<string>(view.BlockadedNames).Count == 1, "exactly one blockaded planet is visible");
            ok &= Check(!view.IsBlockaded(null) && view.Value(null) == 0f, "a null name is never blockaded");

            // My own docked offense at B cancels the blockade there.
            WarshipSelfCheck.DockWarships(map.GetPlanet("B"), 0, 1);
            ok &= Check(!BlockadeView.Build(map, 0, blockade).IsBlockaded("B"),
                "my own equal docked offense cancels the blockade");

            // A blockaded planet I have presence at is seen (own planets are always visible).
            WarshipSelfCheck.DockWarships(map.GetPlanet("A"), 1, 1);
            ok &= Check(BlockadeView.Build(map, 0, blockade).IsBlockaded("A"),
                "my own presence planet, blockaded by another player, is in my view");

            // Player 1's own view of their own presence: nothing is blockaded against them at B or C.
            var view1 = BlockadeView.Build(map, 1, blockade);
            ok &= Check(!view1.IsBlockaded("B") || view1.Value("B") <= 0f,
                "player 1 is not blockaded at B: player 0's docked offense there only equals its own");

            var of = BlockadeView.Of("X", "Y");
            ok &= Check(of.IsBlockaded("X") && of.IsBlockaded("Y") && !of.IsBlockaded("Z"),
                "BlockadeView.Of lists exactly the given planets");
        }
        finally
        {
            WarshipSelfCheck.DestroyAll(research);
            Object.DestroyImmediate(go);
            Object.DestroyImmediate(constants);
            Object.DestroyImmediate(template);
        }
        return ok;
    }

    // Z is deliberately disconnected: it exercises FindPath's 1-node no-route stub. PathingSystem logs a harmless red
    // "planet 'Z' has no connections" error when this self-check runs; that is not a failure (other suites accept it too).
    // Diamond A - B - D and A - C - D, plus a disconnected Z. A to D is about 224 via B and about 361 via C.
    private static GameAIMap BuildDiamond(GameObject go, GameAIConstants constants)
        => Build(go, constants,
            Spawn("A", 0f, 0f, new[] { "B", "C" }),
            Spawn("B", 100f, 50f, new[] { "A", "D" }),
            Spawn("C", 100f, -150f, new[] { "A", "D" }),
            Spawn("D", 200f, 0f, new[] { "B", "C" }),
            Spawn("Z", 1000f, 1000f));

    private static string Join(RoutePlanner.PlannedRoute route)
        => route == null ? "null" : string.Join(">", route.Nodes);

    public static bool RunRoutePlannerCheck()
    {
        var ok = true;
        var go = new GameObject("BASelfCheckMap_Planner");
        var constants = ScriptableObject.CreateInstance<GameAIConstants>();
        try
        {
            var map = BuildDiamond(go, constants);

            var clean = RoutePlanner.PlanRoute(map, "A", "D", null, 6);
            ok &= Check(Join(clean) == "A>B>D" && !clean.IsDetour && clean.Cost > 0f && clean.NumNodes == 3,
                "with no view the route is the normal shortest path A>B>D, not a detour");
            var elsewhere = RoutePlanner.PlanRoute(map, "A", "D", BlockadeView.Of("C"), 6);
            ok &= Check(Join(elsewhere) == "A>B>D" && !elsewhere.IsDetour,
                "a blockade off the shortest path does not change the route");

            var detour = RoutePlanner.PlanRoute(map, "A", "D", BlockadeView.Of("B"), 6);
            ok &= Check(Join(detour) == "A>C>D" && detour.IsDetour && detour.Cost > clean.Cost,
                "B blockaded: the route detours A>C>D, marked as a detour and costing more");

            ok &= Check(RoutePlanner.PlanRoute(map, "A", "D", BlockadeView.Of("B", "C"), 6) == null,
                "both ways blockaded: no route (colonization is cancelled)");
            ok &= Check(RoutePlanner.PlanRoute(map, "A", "D", BlockadeView.Of("D"), 6) == null,
                "a visibly blockaded target has no route");
            ok &= Check(RoutePlanner.PlanRoute(map, "A", "D", null, 2) == null,
                "a target beyond the node range (3 nodes, max 2) is not reachable");
            ok &= Check(RoutePlanner.PlanRoute(map, "A", "D", BlockadeView.Of("B"), 2) == null,
                "a target beyond the node range is not made reachable by a detour either");
            ok &= Check(RoutePlanner.PlanRoute(map, "A", "Z", null, 6) == null,
                "an unreachable planet (FindPath's 1-node stub) is never a route");
            ok &= Check(RoutePlanner.PlanRoute(map, "A", "A", null, 6) == null
                        && RoutePlanner.PlanRoute(map, "A", "Nowhere", null, 6) == null
                        && RoutePlanner.PlanRoute(map, "Nowhere", "D", null, 6) == null,
                "origin == target and unknown planets have no route, without throwing");
            ok &= Check(Join(RoutePlanner.PlanRoute(map, "D", "A", BlockadeView.Of("B"), 6)) == "D>C>A",
                "the reverse trip detours too: D>C>A");
        }
        finally
        {
            Object.DestroyImmediate(go);
            Object.DestroyImmediate(constants);
        }

        // A detour may exceed the node range: A - B - D is 3 nodes, the way round is 5.
        var go2 = new GameObject("BASelfCheckMap_PlannerLong");
        var constants2 = ScriptableObject.CreateInstance<GameAIConstants>();
        try
        {
            var map = Build(go2, constants2,
                Spawn("A", 0f, 0f, new[] { "B", "C1" }),
                Spawn("B", 100f, 0f, new[] { "A", "D" }),
                Spawn("D", 200f, 0f, new[] { "B", "C3" }),
                Spawn("C1", 0f, 200f, new[] { "A", "C2" }),
                Spawn("C2", 100f, 300f, new[] { "C1", "C3" }),
                Spawn("C3", 200f, 200f, new[] { "C2", "D" }));
            ok &= Check(Join(RoutePlanner.PlanRoute(map, "A", "D", null, 3)) == "A>B>D",
                "the direct route is within the range of 3 nodes");
            var longWay = RoutePlanner.PlanRoute(map, "A", "D", BlockadeView.Of("B"), 3);
            ok &= Check(Join(longWay) == "A>C1>C2>C3>D" && longWay.IsDetour && longWay.NumNodes == 5,
                "with B blockaded the detour takes 5 nodes, beyond the normal maximum of 3");
        }
        finally
        {
            Object.DestroyImmediate(go2);
            Object.DestroyImmediate(constants2);
        }
        return ok;
    }

    // PlanShipmentRoute: a clean route when one exists (even beyond the node range), else the least-loss route; the
    // origin's own blockade counts as loss; an out-of-range source is never reachable.
    public static bool RunShipmentPlannerCheck()
    {
        var ok = true;
        var go = new GameObject("BASelfCheckMap_ShipPlanner");
        var constants = ScriptableObject.CreateInstance<GameAIConstants>();
        try
        {
            var map = BuildDiamond(go, constants);   // A to D: about 224 via B, about 361 via C

            var none = RoutePlanner.PlanShipmentRoute(map, "A", "D", null, 6);
            ok &= Check(Join(none) == "A>B>D" && !none.IsDetour && Near(none.Loss, 0f),
                "a null view: the ordinary shortest path with no loss");
            var clean = RoutePlanner.PlanShipmentRoute(map, "A", "D", BlockadeView.Of("B"), 6);
            ok &= Check(Join(clean) == "A>C>D" && clean.IsDetour && Near(clean.Loss, 0f),
                "B blockaded: the clean detour A>C>D, no loss");

            var tie = RoutePlanner.PlanShipmentRoute(map, "A", "D", BlockadeView.Of("B", "C"), 6);
            ok &= Check(Join(tie) == "A>B>D" && !tie.IsDetour && Near(tie.Loss, 1f),
                "both ways blockaded (1 each): equal loss, so the cheaper path A>B>D, loss 1, not a detour");
            var lighter = RoutePlanner.PlanShipmentRoute(map, "A", "D",
                BlockadeView.WithValues(("B", 2f), ("C", 1f)), 6);
            ok &= Check(Join(lighter) == "A>C>D" && lighter.IsDetour && Near(lighter.Loss, 1f),
                "B worth 2, C worth 1: the least-loss route is the longer A>C>D, loss 1");

            var target = RoutePlanner.PlanShipmentRoute(map, "A", "D", BlockadeView.Of("D"), 6);
            ok &= Check(Join(target) == "A>B>D" && Near(target.Loss, 1f),
                "a blockaded target is not refused: its value counts (loss 1)");
            var origin = RoutePlanner.PlanShipmentRoute(map, "A", "D", BlockadeView.Of("A"), 6);
            ok &= Check(Join(origin) == "A>B>D" && !origin.IsDetour && Near(origin.Loss, 1f),
                "a blockaded origin is not refused: its value counts (loss 1)");
            var all = RoutePlanner.PlanShipmentRoute(map, "A", "D",
                BlockadeView.WithValues(("A", 3f), ("B", 5f), ("C", 1f), ("D", 2f)), 6);
            ok &= Check(Join(all) == "A>C>D" && Near(all.Loss, 6f),
                "origin 3 + C 1 + target 2 = 6, cheaper than via B (3 + 5 + 2)");

            ok &= Check(RoutePlanner.PlanShipmentRoute(map, "A", "D", null, 2) == null,
                "a target beyond the node range (3 nodes, max 2) is unreachable");
            ok &= Check(RoutePlanner.PlanShipmentRoute(map, "A", "D", BlockadeView.Of("B", "C"), 2) == null,
                "a blockade does not bring an out-of-range target into range");
            ok &= Check(RoutePlanner.PlanShipmentRoute(map, "A", "Z", null, 6) == null
                        && RoutePlanner.PlanShipmentRoute(map, "A", "A", null, 6) == null
                        && RoutePlanner.PlanShipmentRoute(map, "A", "Nowhere", null, 6) == null,
                "the no-route stub, origin == target and unknown planets have no route");
        }
        finally
        {
            Object.DestroyImmediate(go);
            Object.DestroyImmediate(constants);
        }

        // A detour may exceed the node range: A - B - D is 3 nodes, the way round is 5.
        var go2 = new GameObject("BASelfCheckMap_ShipPlannerLong");
        var constants2 = ScriptableObject.CreateInstance<GameAIConstants>();
        try
        {
            var map = Build(go2, constants2,
                Spawn("A", 0f, 0f, new[] { "B", "C1" }),
                Spawn("B", 100f, 0f, new[] { "A", "D" }),
                Spawn("D", 200f, 0f, new[] { "B", "C3" }),
                Spawn("C1", 0f, 200f, new[] { "A", "C2" }),
                Spawn("C2", 100f, 300f, new[] { "C1", "C3" }),
                Spawn("C3", 200f, 200f, new[] { "C2", "D" }));
            var longWay = RoutePlanner.PlanShipmentRoute(map, "A", "D", BlockadeView.Of("B"), 3);
            ok &= Check(Join(longWay) == "A>C1>C2>C3>D" && longWay.NumNodes == 5 && Near(longWay.Loss, 0f),
                "with B blockaded the clean detour takes 5 nodes, beyond the maximum of 3, with no loss");
            var cheap = RoutePlanner.PlanShipmentRoute(map, "A", "D",
                BlockadeView.WithValues(("B", 1f), ("C2", 5f)), 3);
            ok &= Check(Join(cheap) == "A>B>D" && Near(cheap.Loss, 1f),
                "no clean route (B and C2 both blockaded): the least-loss route is through B, loss 1");
        }
        finally
        {
            Object.DestroyImmediate(go2);
            Object.DestroyImmediate(constants2);
        }
        return ok;
    }

    // Dijkstra on a hand-built graph: the origin is never skipped, blocked nodes are, ties break by node name.
    public static bool RunShortestPathAvoidingCheck()
    {
        var ok = true;
        var graph = new Dictionary<string, PathNode>();
        PathNode Node(string name, float x)
        {
            var node = new PathNode(name, new Vector2(x, 0f));
            graph[name] = node;
            return node;
        }
        void Edge(string a, string b, float cost)
        {
            graph[a].Connections.Add(new Connection(b, cost));
            graph[b].Connections.Add(new Connection(a, cost));
        }
        Node("S", 0f); Node("M1", 1f); Node("M2", 2f); Node("T", 3f);
        Edge("S", "M1", 10f); Edge("S", "M2", 10f); Edge("M1", "T", 10f); Edge("M2", "T", 10f);

        var tie = RoutePlanner.ShortestPathAvoiding(graph, "S", "T", new HashSet<string>());
        ok &= Check(tie != null && string.Join(">", tie.Nodes) == "S>M1>T" && Near(tie.Cost, 20f) && tie.IsDetour,
            "equal-cost routes break the tie by node name (M1 before M2), deterministically");
        var skipM1 = RoutePlanner.ShortestPathAvoiding(graph, "S", "T", new HashSet<string> { "M1" });
        ok &= Check(skipM1 != null && string.Join(">", skipM1.Nodes) == "S>M2>T", "a blocked node is skipped");
        ok &= Check(RoutePlanner.ShortestPathAvoiding(graph, "S", "T", new HashSet<string> { "M1", "M2" }) == null,
            "every way blocked: null");
        var blockedOrigin = RoutePlanner.ShortestPathAvoiding(graph, "S", "T", new HashSet<string> { "S" });
        ok &= Check(blockedOrigin != null && string.Join(">", blockedOrigin.Nodes) == "S>M1>T",
            "the origin is never skipped even if it is in the blocked set");
        ok &= Check(RoutePlanner.ShortestPathAvoiding(graph, "S", "Nowhere", null) == null
                    && RoutePlanner.ShortestPathAvoiding(graph, "Nowhere", "T", null) == null,
            "an unknown origin or target gives null");
        return ok;
    }

    private static GameAI.GameAIOrder ColonistOrder(string origin, string target, int timingDelay, int totalDelay,
        List<string> route)
        => new GameAI.GameAIOrder
        {
            Type = GameAI.GameAIOrder.OrderType.OrderTypePopulationTransport,
            TimingType = GameAI.GameAIOrder.OrderTimingType.OrderTimingTypeDelayed,
            TimingDelay = timingDelay, TotalDelay = totalDelay,
            Data = 1, Origin = origin, Target = target, PlayerId = 0,
            Route = route,
        };

    public static bool RunCarriedRouteCheck()
    {
        var ok = true;
        var go = new GameObject("BASelfCheckMap_CarriedRoute");
        var template = WarshipSelfCheck.MakeTemplate();
        var constants = WarshipSelfCheck.MakeConstants(template);
        var research = WarshipSelfCheck.MakeResearch();
        try
        {
            var map = BuildDiamond(go, constants);
            var blockade = new BlockadeSystem(map, research);

            ok &= Check(Near(map.EdgeCost("A", "B"), 111.8f) && map.EdgeCost("A", "D") == 0f && map.EdgeCost("Q", "B") == 0f,
                "EdgeCost is the connection cost between adjacent nodes, 0 when there is no such edge");

            var detourRoute = new List<string> { "A", "C", "D" };
            var flown = blockade.RouteFor(ColonistOrder("A", "D", 4, 4, detourRoute));
            ok &= Check(flown.Count == 2 && flown[0].Name == "C" && Near(flown[0].Fraction, 0.5f)
                        && flown[1].Name == "D" && Near(flown[1].Fraction, 1f),
                "a carried route yields its own nodes after the origin, C halfway (A-C and C-D cost the same)");
            var fallback = blockade.RouteFor(ColonistOrder("A", "D", 4, 4, null));
            ok &= Check(fallback.Count == 2 && fallback[0].Name == "B",
                "an order with no route falls back to the shortest path A>B>D");
            var tooShort = blockade.RouteFor(ColonistOrder("A", "D", 4, 4, new List<string> { "A" }));
            ok &= Check(tooShort.Count == 2 && tooShort[0].Name == "B",
                "a carried route of fewer than 2 nodes is ignored");

            // A blockader at B (on the shortest path only). The detour order does not pass B.
            WarshipSelfCheck.DockWarships(map.GetPlanet("B"), 1, 1);
            var orders = new List<GameAI.GameAIOrder> { ColonistOrder("A", "D", 2, 4, detourRoute) };
            blockade.Apply(orders, 1);
            ok &= Check(orders.Count == 1, "a blockade only on the shortest path does not touch an order flying the detour");

            // The same order WITHOUT a carried route is checked on the shortest path and is removed at B.
            orders = new List<GameAI.GameAIOrder> { ColonistOrder("A", "D", 2, 4, null) };
            blockade.Apply(orders, 1);
            ok &= Check(orders.Count == 0, "an older order with no route is still blockaded along the shortest path");

            // A blockader at C blocks the detour order at the halfway node.
            map.GetPlanet("B").UndockShips(Ship.ShipKind.WarShip, 1, 99);
            WarshipSelfCheck.DockWarships(map.GetPlanet("C"), 1, 1);
            orders = new List<GameAI.GameAIOrder> { ColonistOrder("A", "D", 2, 4, detourRoute) };
            blockade.Apply(orders, 1);
            ok &= Check(orders.Count == 0, "a blockade on the carried route removes the colonist order");
        }
        finally
        {
            WarshipSelfCheck.DestroyAll(research);
            Object.DestroyImmediate(go);
            Object.DestroyImmediate(constants);
            Object.DestroyImmediate(template);
        }

        // Saves: the route survives a JsonUtility round trip; an older order with no route loads as none.
        var save = new SaveLoadSystem.GameSave.OrderSave
        {
            origin = "A", target = "D", playerId = 0,
            route = new List<string> { "A", "C", "D" },
        };
        var loaded = JsonUtility.FromJson<SaveLoadSystem.GameSave.OrderSave>(JsonUtility.ToJson(save));
        var restored = GameAI.GameAIOrder.RouteFromSave(loaded.route);
        ok &= Check(restored != null && string.Join(">", restored) == "A>C>D", "an order route survives a save round trip");
        var older = JsonUtility.FromJson<SaveLoadSystem.GameSave.OrderSave>("{\"origin\":\"A\",\"target\":\"D\"}");
        ok &= Check(GameAI.GameAIOrder.RouteFromSave(older.route) == null, "an older order with no route restores as none");
        ok &= Check(GameAI.GameAIOrder.RouteFromSave(new List<string> { "A" }) == null
                    && GameAI.GameAIOrder.RouteFromSave(null) == null,
            "a saved route of fewer than 2 nodes, or none, restores as none");
        return ok;
    }

    // The origin of a food/grotsits shipment is blockaded like any other node, once, on the first turn the order is
    // processed (TimingDelay + 1 >= TotalDelay, TotalDelay > 0). Colonists are unchanged.
    public static bool RunShipmentOriginCheck()
    {
        var ok = true;
        var go = new GameObject("BASelfCheckMap_ShipOrigin");
        var template = WarshipSelfCheck.MakeTemplate();
        var constants = WarshipSelfCheck.MakeConstants(template);
        var research = WarshipSelfCheck.MakeResearch();
        try
        {
            var map = BuildDiamond(go, constants);
            var blockade = new BlockadeSystem(map, research);
            var route = new List<string> { "A", "B", "D" };
            WarshipSelfCheck.DockWarships(map.GetPlanet("A"), 1, 1);   // 10 against player 0 at the origin A

            var order = ShipmentOrder("A", "D", 3, 4, route, 50f);
            var orders = new List<GameAI.GameAIOrder> { order };
            var cuts = blockade.Apply(orders, 1);
            ok &= Check(Near(System.Convert.ToSingle(order.Data), 40f) && cuts.Count == 1 && cuts[0].Planet == "A"
                        && Near(cuts[0].Value, 10f),
                "a shipment loses the origin's blockade value on its first processed turn (50 to 40) and reports the cut");
            order.TimingDelay = 2;
            blockade.Apply(orders, 2);
            ok &= Check(Near(System.Convert.ToSingle(order.Data), 40f), "the origin is cut once, not again on later turns");

            var small = ShipmentOrder("A", "D", 3, 4, route, 8f);
            map.GetPlanet("D").FoodShipmentIncoming = true;
            orders = new List<GameAI.GameAIOrder> { small };
            blockade.Apply(orders, 1);
            ok &= Check(orders.Count == 0 && !map.GetPlanet("D").FoodShipmentIncoming,
                "a shipment smaller than the origin's blockade is removed and the target's incoming flag cleared");

            orders = new List<GameAI.GameAIOrder> { ColonistOrder("A", "D", 3, 4, route) };
            blockade.Apply(orders, 1);
            ok &= Check(orders.Count == 1, "a colonist's origin is still never checked");

            var zero = ShipmentOrder("A", "D", 0, 0, route, 50f);
            orders = new List<GameAI.GameAIOrder> { zero };
            blockade.Apply(orders, 1);
            ok &= Check(Near(System.Convert.ToSingle(zero.Data), 50f),
                "a zero-delay order (the known double-execution quirk) never has its origin cut");

            var mid = ShipmentOrder("A", "D", 1, 4, route, 50f);
            orders = new List<GameAI.GameAIOrder> { mid };
            blockade.Apply(orders, 1);
            ok &= Check(Near(System.Convert.ToSingle(mid.Data), 50f),
                "an order already past its first turn (e.g. loaded mid-flight) is not cut at the origin");

            var older = ShipmentOrder("A", "D", 3, 4, null, 50f);
            orders = new List<GameAI.GameAIOrder> { older };
            blockade.Apply(orders, 1);
            ok &= Check(Near(System.Convert.ToSingle(older.Data), 40f),
                "an older shipment with no carried route still has its origin cut on its first turn");
        }
        finally
        {
            WarshipSelfCheck.DestroyAll(research);
            Object.DestroyImmediate(go);
            Object.DestroyImmediate(constants);
            Object.DestroyImmediate(template);
        }
        return ok;
    }

    // The diamond with player 0 present at A: 5 inhabitants (a full planet, ready to colonize), B and C not valid
    // targets (a colonist is already inbound), D the only target. Player 0's research catalog holds MakeResearch().
    public sealed class Scenario : System.IDisposable
    {
        public GameObject MapGo, PlayerGo;
        public GameAIMap Map;
        public PlayerAI AI;
        public GameAIConstants Constants;
        public ShipData Template;
        public List<CatalogItem> Research;

        public static Scenario Diamond()
        {
            var s = new Scenario();
            s.Template = WarshipSelfCheck.MakeTemplate();
            s.Constants = WarshipSelfCheck.MakeConstants(s.Template);
            s.Constants.defaultTravelSpeed = 1f;
            s.Constants.expandPopulationTrigger = 0.8f;
            s.Constants.maxPathNodesForColonization = 6;
            s.Constants.maxPathNodesForResourceDistribution = 6;
            s.Constants.shipmentMinDeliveredFraction = 0f;   // the older shipment checks assume any loss < amount ships
            s.Constants.maxPathNodesForKnowledge = 6;
            s.Research = WarshipSelfCheck.MakeResearch();
            s.MapGo = new GameObject("BASelfCheckMap_Scenario");
            s.PlayerGo = new GameObject("BASelfCheckPlayer_Scenario");
            s.Map = BuildDiamond(s.MapGo, s.Constants);

            var a = s.Map.GetPlanet("A");
            a.Owner = 0;
            for (var i = 0; i < 5; i++) a.Population.Add(new Planet.Inhabitant { Player = 0 });
            a.Food = 100f;
            s.Map.GetPlanet("B").SetPopulationTransferInProgress(0);
            s.Map.GetPlanet("C").SetPopulationTransferInProgress(0);

            var player = s.PlayerGo.AddComponent<Player>();
            s.AI = s.PlayerGo.AddComponent<PlayerAI>();
            s.AI.Player = player;
            s.AI.AIMap = s.Map;
            player.playerID = 0;
            s.AI.ResearchCatalog = s.PlayerGo.AddComponent<Catalog>();
            s.AI.ResearchCatalog.catalogItems = s.Research;
            s.Map.Knowledge.Update(s.Map, 1, 6);
            return s;
        }

        // A hub graph: A(0,0) X(100,0) H(200,0) D(300,0) Y(0,200) Z(200,250); edges A-X, X-H, H-D, A-Y, Y-Z, Z-D. The
        // direct route A>X>H>D costs 300, the long way A>Y>Z>D about 675. Player 0 is present at A only, so H (two hops
        // away) is NOT visible to it; D is the only valid target (X, H, Y, Z carry an inbound-colonist flag).
        public static Scenario Hub()
        {
            var s = new Scenario();
            s.Template = WarshipSelfCheck.MakeTemplate();
            s.Constants = WarshipSelfCheck.MakeConstants(s.Template);
            s.Constants.defaultTravelSpeed = 1f;
            s.Constants.expandPopulationTrigger = 0.8f;
            s.Constants.maxPathNodesForColonization = 6;
            s.Constants.maxPathNodesForKnowledge = 6;
            s.Research = WarshipSelfCheck.MakeResearch();
            s.MapGo = new GameObject("BASelfCheckMap_Hub");
            s.PlayerGo = new GameObject("BASelfCheckPlayer_Hub");
            s.Map = Build(s.MapGo, s.Constants,
                Spawn("A", 0f, 0f, new[] { "X", "Y" }),
                Spawn("X", 100f, 0f, new[] { "A", "H" }),
                Spawn("H", 200f, 0f, new[] { "X", "D" }),
                Spawn("D", 300f, 0f, new[] { "H", "Z" }),
                Spawn("Y", 0f, 200f, new[] { "A", "Z" }),
                Spawn("Z", 200f, 250f, new[] { "Y", "D" }));

            var a = s.Map.GetPlanet("A");
            a.Owner = 0;
            for (var i = 0; i < 5; i++) a.Population.Add(new Planet.Inhabitant { Player = 0 });
            a.Food = 100f;
            foreach (var name in new[] { "X", "H", "Y", "Z" })
                s.Map.GetPlanet(name).SetPopulationTransferInProgress(0);

            var player = s.PlayerGo.AddComponent<Player>();
            s.AI = s.PlayerGo.AddComponent<PlayerAI>();
            s.AI.Player = player;
            s.AI.AIMap = s.Map;
            player.playerID = 0;
            s.AI.ResearchCatalog = s.PlayerGo.AddComponent<Catalog>();
            s.AI.ResearchCatalog.catalogItems = s.Research;
            s.Map.Knowledge.Update(s.Map, 1, 6);
            return s;
        }

        public void Blockade(string planet, int warships = 1)
            => WarshipSelfCheck.DockWarships(Map.GetPlanet(planet), 1, warships);

        public void Unblockade(string planet)
            => Map.GetPlanet(planet).UndockShips(Ship.ShipKind.WarShip, 1, 999);

        public static Scenario Long()
        {
            var s = new Scenario();
            s.Template = WarshipSelfCheck.MakeTemplate();
            s.Constants = WarshipSelfCheck.MakeConstants(s.Template);
            s.Constants.defaultTravelSpeed = 1f;
            s.Constants.maxPathNodesForResourceDistribution = 3;   // the direct route A>B>D is 3 nodes; the way round is 5
            s.Constants.maxPathNodesForKnowledge = 6;
            s.Research = WarshipSelfCheck.MakeResearch();
            s.MapGo = new GameObject("BASelfCheckMap_ScenarioLong");
            s.PlayerGo = new GameObject("BASelfCheckPlayer_ScenarioLong");
            s.Map = Build(s.MapGo, s.Constants,
                Spawn("A", 0f, 0f, new[] { "B", "C1" }),
                Spawn("B", 100f, 0f, new[] { "A", "D" }),
                Spawn("D", 200f, 0f, new[] { "B", "C3" }),
                Spawn("C1", 0f, 200f, new[] { "A", "C2" }),
                Spawn("C2", 100f, 300f, new[] { "C1", "C3" }),
                Spawn("C3", 200f, 200f, new[] { "C2", "D" }));

            var a = s.Map.GetPlanet("A");
            a.Owner = 0;
            for (var i = 0; i < 5; i++) a.Population.Add(new Planet.Inhabitant { Player = 0 });

            var player = s.PlayerGo.AddComponent<Player>();
            s.AI = s.PlayerGo.AddComponent<PlayerAI>();
            s.AI.Player = player;
            s.AI.AIMap = s.Map;
            player.playerID = 0;
            s.AI.ResearchCatalog = s.PlayerGo.AddComponent<Catalog>();
            s.AI.ResearchCatalog.catalogItems = s.Research;
            s.Map.Knowledge.Update(s.Map, 1, 6);
            return s;
        }

        // A populated A (surplus `surplus` food) ships to D (shortage `shortage` food): the food shipment orders.
        public List<GameAI.GameAIOrder> ShipFood(float shortage, float surplus = 100f)
        {
            var results = new List<Planet.PlanetUpdateResult>
            {
                new Planet.PlanetUpdateResult("D",
                    Planet.PlanetUpdateResult.PlanetUpdateResultType.PlanetUpdateResultTypeFoodShortage, -shortage, 0),
                new Planet.PlanetUpdateResult("A",
                    Planet.PlanetUpdateResult.PlanetUpdateResultType.PlanetUpdateResultTypeFoodSurplus, surplus, 0),
            };
            var orders = new List<GameAI.GameAIOrder>();
            AI.ProcessFoodShortage(results, orders);
            return orders;
        }

        public List<GameAI.GameAIOrder> Colonize()
        {
            var results = new List<Planet.PlanetUpdateResult>
            {
                new Planet.PlanetUpdateResult("A",
                    Planet.PlanetUpdateResult.PlanetUpdateResultType.PlanetUpdateResultTypeColonizerReady, 1, 0),
            };
            var orders = new List<GameAI.GameAIOrder>();
            AI.ProcessColonizers(results, orders);
            return orders;
        }

        public void Dispose()
        {
            WarshipSelfCheck.DestroyAll(Research);
            Object.DestroyImmediate(PlayerGo);
            Object.DestroyImmediate(MapGo);
            Object.DestroyImmediate(Constants);
            Object.DestroyImmediate(Template);
        }
    }

    private static GameAI.GameAIOrder Colonist(List<GameAI.GameAIOrder> orders)
        => orders.Find(o => o.Type == GameAI.GameAIOrder.OrderType.OrderTypePopulationTransport);

    public static bool RunColonizationRoutingCheck()
    {
        var ok = true;
        using (var s = Scenario.Diamond())
        {
            // No view has been built yet (null): nothing is blockaded, colonization is exactly as before.
            s.Blockade("B");
            var before = Colonist(s.Colonize());
            ok &= Check(before != null && string.Join(">", before.Route) == "A>B>D",
                "with no view yet, a blockade at B is unknown: the colonist takes the shortest route A>B>D");
            s.Unblockade("B");

            // Clean: the shortest route, carried on the order, delay from the route's cost.
            s.AI.RefreshBlockadeView();
            var clean = Colonist(s.Colonize());
            var cleanRoute = RoutePlanner.PlanRoute(s.Map, "A", "D", s.AI.CurrentBlockadeView, 6);
            ok &= Check(clean != null && string.Join(">", clean.Route) == "A>B>D"
                        && clean.TimingDelay == System.Convert.ToInt32(cleanRoute.Cost) && clean.TotalDelay == clean.TimingDelay,
                "unblockaded: the colonist carries A>B>D and its delay comes from that route's cost");

            // B blockaded (visible: a neighbour of A): the colonist detours through C, with the detour's delay.
            s.Blockade("B");
            s.AI.RefreshBlockadeView();
            ok &= Check(s.AI.CurrentBlockadeView.IsBlockaded("B"), "B is in player 0's view (a neighbour of its presence)");
            var detoured = Colonist(s.Colonize());
            var detourRoute = RoutePlanner.PlanRoute(s.Map, "A", "D", s.AI.CurrentBlockadeView, 6);
            ok &= Check(detoured != null && string.Join(">", detoured.Route) == "A>C>D"
                        && detoured.TotalDelay == System.Convert.ToInt32(detourRoute.Cost)
                        && detoured.TotalDelay > clean.TotalDelay,
                "B blockaded: the colonist detours A>C>D and the longer trip has a longer delay");

            // Both ways blockaded: colonization is cancelled and the colony ship stays (no orders at all).
            s.Blockade("C");
            s.AI.RefreshBlockadeView();
            ok &= Check(s.Colonize().Count == 0, "every route blockaded: no colonist order and no other order is emitted");

            // C freed again: the colonist goes that way again (the colonizer stayed ready, nothing was consumed).
            s.Unblockade("C");
            s.AI.RefreshBlockadeView();
            ok &= Check(Colonist(s.Colonize()) != null, "when a way opens again the colonizer launches");
            s.Unblockade("B");

            // Blockaded origin: no colonist launches.
            s.Blockade("A");
            s.AI.RefreshBlockadeView();
            ok &= Check(s.AI.CurrentBlockadeView.IsBlockaded("A") && s.Colonize().Count == 0,
                "a blockaded origin launches nothing");
            s.Unblockade("A");

            // A blockade the player cannot see is not avoided: D is two hops from A's presence, so its blockade is unseen.
            s.Blockade("D");
            s.AI.RefreshBlockadeView();
            ok &= Check(!s.AI.CurrentBlockadeView.IsBlockaded("D"), "D is not visible from A's presence");
            var unseen = Colonist(s.Colonize());
            ok &= Check(unseen != null && string.Join(">", unseen.Route) == "A>B>D",
                "an unseen blockade at the target is not avoided (the colonist is sent and may be lost)");
            s.Unblockade("D");

            // A visible blockaded target is never chosen: give player 0 presence at C so D is a visible neighbour.
            s.Map.GetPlanet("C").DockShipFromSave(Ship.ShipKind.ColonyShip, 0, new List<string>());
            s.Blockade("D");
            s.AI.RefreshBlockadeView();
            ok &= Check(s.AI.CurrentBlockadeView.IsBlockaded("D") && s.Colonize().Count == 0,
                "a visibly blockaded target is never chosen: colonization is cancelled");
        }
        return ok;
    }

    private static GameAI.GameAIOrder FoodShipment(List<GameAI.GameAIOrder> orders)
        => orders.Find(o => o.Type == GameAI.GameAIOrder.OrderType.OrderTypeFoodTransport);

    private static float FoodDeducted(List<GameAI.GameAIOrder> orders)
    {
        var change = orders.Find(o => o.Type == GameAI.GameAIOrder.OrderType.OrderTypeFoodChange);
        return change == null ? 0f : System.Convert.ToSingle(change.Data);
    }

    public static bool RunShipmentPlanningCheck()
    {
        var ok = true;
        const GameAI.GameAIOrder.OrderType food = GameAI.GameAIOrder.OrderType.OrderTypeFoodTransport;
        using (var s = Scenario.Diamond())
        {
            // No view yet (null): nothing is blockaded, shipping is exactly as before.
            s.Blockade("B");
            var before = FoodShipment(s.ShipFood(50f));
            ok &= Check(before != null && string.Join(">", before.Route) == "A>B>D",
                "with no view yet, a blockade at B is unknown: the shipment takes the shortest route A>B>D");
            s.Unblockade("B");

            // Clean: the shortest route rides on the order; the delay comes from its cost.
            s.AI.RefreshBlockadeView();
            var cleanOrders = s.ShipFood(50f);
            var clean = FoodShipment(cleanOrders);
            var cleanRoute = RoutePlanner.PlanShipmentRoute(s.Map, "A", "D", s.AI.CurrentBlockadeView, 6);
            ok &= Check(clean != null && string.Join(">", clean.Route) == "A>B>D"
                        && clean.TotalDelay == System.Convert.ToInt32(cleanRoute.Cost) && Near(FoodDeducted(cleanOrders), -50f),
                "unblockaded: the shipment carries A>B>D, its delay comes from the route cost, the origin is charged 50");

            // B blockaded (visible): a clean detour through C, longer delay, nothing lost.
            s.Blockade("B");
            s.AI.RefreshBlockadeView();
            var detoured = FoodShipment(s.ShipFood(50f));
            ok &= Check(detoured != null && string.Join(">", detoured.Route) == "A>C>D" && detoured.TotalDelay > clean.TotalDelay,
                "B blockaded: the shipment detours A>C>D and the longer trip has a longer delay");

            // Both ways blockaded: least loss wins even though it is the longer way (B worth 20, C worth 10).
            s.Blockade("B");                 // B now holds 2 warships
            s.Blockade("C");
            s.AI.RefreshBlockadeView();
            var lossyOrders = s.ShipFood(50f);
            var lossy = FoodShipment(lossyOrders);
            ok &= Check(lossy != null && string.Join(">", lossy.Route) == "A>C>D" && Near(FoodDeducted(lossyOrders), -50f),
                "no clean route: the least-loss route A>C>D (10 against B's 20) is taken and the full 50 is sent");

            // loss >= amount cancels; the state is noted once; a shipment just above the loss ships.
            s.Unblockade("B");
            s.Blockade("B");                 // B and C now 10 each: every route loses 10
            s.AI.RefreshBlockadeView();
            ok &= Check(s.ShipFood(8f).Count == 0, "a shipment smaller than the unavoidable loss (8 against 10) is cancelled");
            ok &= Check(s.AI.ShipmentHeldBackReason(food, "D") == "Blockade", "the cancelled shortage is remembered as held back");
            var detail = s.AI.ShipmentHeldBackDetail(food, "D");
            ok &= Check(detail != null && detail.Source == "A" && Near(detail.Loss, 10f) && Near(detail.Amount, 8f)
                        && detail.BlockedNodes == "B=10",
                "the held-back record names the dropped source (A), its loss (10), the amount (8) and the blockaded route node (B=10)");
            ok &= Check(!s.AI.NoteShipmentHeldBack(food, "D", "Blockade") && s.AI.NoteShipmentHeldBack(food, "D", "Other"),
                "the held-back note is news only when the reason changes (so the log line is not repeated every turn)");
            ok &= Check(s.ShipFood(10f).Count == 0, "a shipment exactly equal to the loss (10 against 10) is cancelled: nothing would arrive");
            ok &= Check(FoodShipment(s.ShipFood(11f)) != null, "a shipment just above the loss (11 against 10) still ships");
            ok &= Check(s.AI.ShipmentHeldBackReason(food, "D") == null, "a shipment that goes out clears the held-back state");

            // The record is forgotten once the shortage is gone, so a later episode for the same planet logs again.
            ok &= Check(s.ShipFood(8f).Count == 0 && s.AI.ShipmentHeldBackReason(food, "D") == "Blockade",
                "D is held back again by the blockade");
            s.AI.ProcessFoodShortage(new List<Planet.PlanetUpdateResult>
            {
                new Planet.PlanetUpdateResult("A",
                    Planet.PlanetUpdateResult.PlanetUpdateResultType.PlanetUpdateResultTypeFoodSurplus, 100f, 0),
            }, new List<GameAI.GameAIOrder>());
            ok &= Check(s.AI.ShipmentHeldBackReason(food, "D") == null,
                "when D stops reporting a shortage its held-back record is forgotten");
            s.AI.ProcessFoodShortage(new List<Planet.PlanetUpdateResult>(), new List<GameAI.GameAIOrder>());
            ok &= Check(s.ShipFood(8f).Count == 0 && s.AI.ShipmentHeldBackReason(food, "D") == "Blockade"
                        && s.AI.NoteShipmentHeldBack(food, "D", "Other"),
                "a later episode is news again (the record was forgotten, then re-set to Blockade)");

            // A way opens: it ships again.
            s.Unblockade("C");
            s.AI.RefreshBlockadeView();
            ok &= Check(string.Join(">", FoodShipment(s.ShipFood(8f)).Route) == "A>C>D",
                "when a clean way opens, even a small shipment goes");
            s.Unblockade("B");

            // A blockaded SOURCE is not dropped: its value is loss. 8 is lost entirely, 50 ships.
            s.Blockade("A");
            s.AI.RefreshBlockadeView();
            ok &= Check(s.ShipFood(8f).Count == 0, "a blockaded source (10) cancels a shipment of 8");
            var sourceDetail = s.AI.ShipmentHeldBackDetail(food, "D");
            ok &= Check(sourceDetail != null && sourceDetail.Source == "A" && sourceDetail.BlockedNodes == "A=10",
                "a blockaded source is named as the blocked node (A=10)");
            var fromBlockaded = FoodShipment(s.ShipFood(50f));
            ok &= Check(fromBlockaded != null && string.Join(">", fromBlockaded.Route) == "A>B>D",
                "a blockaded source still ships a shipment larger than its blockade, by the ordinary route");
            s.Unblockade("A");

            // A blockaded TARGET is not dropped either: give player 0 presence at C so D is visible.
            s.Map.GetPlanet("C").DockShipFromSave(Ship.ShipKind.ColonyShip, 0, new List<string>());
            s.Blockade("D");
            s.AI.RefreshBlockadeView();
            ok &= Check(s.AI.CurrentBlockadeView.IsBlockaded("D"), "D is visible and blockaded");
            ok &= Check(s.ShipFood(8f).Count == 0, "a blockaded target (10) cancels a shipment of 8");
            ok &= Check(FoodShipment(s.ShipFood(50f)) != null, "a blockaded target still receives a shipment larger than its blockade");
            s.Unblockade("D");

            // Range: D is 3 nodes from A; with a maximum of 2 nothing is reachable, detour or not.
            s.Constants.maxPathNodesForResourceDistribution = 2;
            s.AI.RefreshBlockadeView();
            ok &= Check(s.ShipFood(50f).Count == 0, "a target beyond the node range is not shipped to");
            s.Blockade("B");
            s.AI.RefreshBlockadeView();
            ok &= Check(s.ShipFood(50f).Count == 0, "a blockade does not bring an out-of-range target into range");
        }

        // The detour may exceed the node range: direct A>B>D is 3 nodes (the maximum), the clean way round is 5.
        using (var s = Scenario.Long())
        {
            s.Blockade("B");
            s.AI.RefreshBlockadeView();
            var around = FoodShipment(s.ShipFood(50f));
            ok &= Check(around != null && string.Join(">", around.Route) == "A>C1>C2>C3>D",
                "a clean detour of 5 nodes is taken although the maximum is 3");
        }

        // Clean routes sort before lossy ones; otherwise by cost minus surplus as before.
        var surplusPlanet = new Planet.PlanetUpdateResult("X",
            Planet.PlanetUpdateResult.PlanetUpdateResultType.PlanetUpdateResultTypeFoodSurplus, 10f, 0);
        var cleanFar = new ResourceChoiceElement { SurplusResult = surplusPlanet, Cost = 500f, Loss = 0f };
        var lossyNear = new ResourceChoiceElement { SurplusResult = surplusPlanet, Cost = 100f, Loss = 5f };
        var cleanNear = new ResourceChoiceElement { SurplusResult = surplusPlanet, Cost = 100f, Loss = 0f };
        ok &= Check(PlayerAI.CompareResourceChoices(cleanFar, lossyNear) < 0
                    && PlayerAI.CompareResourceChoices(lossyNear, cleanFar) > 0,
            "a clean far source sorts before a lossy near one");
        ok &= Check(PlayerAI.CompareResourceChoices(cleanNear, cleanFar) < 0,
            "between clean routes the cheaper sorts first (the existing order)");
        return ok;
    }

    // shipmentMinDeliveredFraction: a lossy pair is also refused when it would deliver less than that share of the
    // shipment (LowYield); clean routes are unaffected; 0 is the plain "loss < amount" rule. Amounts avoid the exact
    // boundary (20 here): a float product can land either side of it (see CLAUDE.md, float thresholds).
    public static bool RunShipmentMinFractionCheck()
    {
        var ok = true;
        const GameAI.GameAIOrder.OrderType food = GameAI.GameAIOrder.OrderType.OrderTypeFoodTransport;
        var defaults = ScriptableObject.CreateInstance<GameAIConstants>();
        ok &= Check(Near(defaults.shipmentMinDeliveredFraction, 0.5f), "shipmentMinDeliveredFraction defaults to 0.5");
        Object.DestroyImmediate(defaults);

        using (var s = Scenario.Diamond())
        {
            // Both ways blockaded, 10 each: every route loses 10.
            s.Blockade("B");
            s.Blockade("C");
            s.AI.RefreshBlockadeView();

            s.Constants.shipmentMinDeliveredFraction = 0.5f;
            ok &= Check(Near(s.AI.MinDeliveredFractionFor("D"), 0.5f), "the seam returns the tunable for an ordinary target");
            ok &= Check(s.ShipFood(15f).Count == 0 && s.AI.ShipmentHeldBackReason(food, "D") == "LowYield",
                "15 against a loss of 10 delivers 33% (< 50%): cancelled as LowYield");
            var lowDetail = s.AI.ShipmentHeldBackDetail(food, "D");
            ok &= Check(lowDetail != null && lowDetail.Reason == "LowYield" && lowDetail.Source == "A"
                        && Near(lowDetail.Loss, 10f) && Near(lowDetail.Amount, 15f) && lowDetail.BlockedNodes == "B=10",
                "a LowYield record carries the same detail (source A, loss 10, amount 15, blocked B=10)");
            ok &= Check(FoodShipment(s.ShipFood(30f)) != null,
                "30 against a loss of 10 delivers 67% (>= 50%): it still ships");
            ok &= Check(s.AI.ShipmentHeldBackReason(food, "D") == null, "a shipment that goes out clears the held-back state");
            ok &= Check(s.ShipFood(8f).Count == 0 && s.AI.ShipmentHeldBackReason(food, "D") == "Blockade",
                "loss >= amount is still reported as Blockade, not LowYield");

            s.Constants.shipmentMinDeliveredFraction = 0f;
            ok &= Check(FoodShipment(s.ShipFood(15f)) != null, "with the fraction at 0 the plain rule returns: 15 against 10 ships");

            s.Constants.shipmentMinDeliveredFraction = 1f;
            ok &= Check(s.ShipFood(30f).Count == 0, "a fraction of 1 refuses every lossy shipment (30 against 10)");
            s.Unblockade("B");
            s.Unblockade("C");
            s.AI.RefreshBlockadeView();
            ok &= Check(FoodShipment(s.ShipFood(5f)) != null, "a clean route is never refused, even at a fraction of 1");

            s.Constants.shipmentMinDeliveredFraction = 2f;
            ok &= Check(Near(s.AI.MinDeliveredFractionFor("D"), 1f), "a fraction above 1 is clamped to 1");
            s.Constants.shipmentMinDeliveredFraction = -1f;
            ok &= Check(Near(s.AI.MinDeliveredFractionFor("D"), 0f), "a negative fraction is clamped to 0");
        }
        return ok;
    }

    private static CatalogItem ProductionEntry(string name, string subType)
    {
        var item = ScriptableObject.CreateInstance<CatalogItem>();
        item.itemName = name; item.name = name; item.type = "Ship"; item.subType = subType; item.cost = 100f;
        return item;
    }

    // ColonizeCancelled is logged when a colonizer's hold-back state changes, not on every turn it holds: the per-planet
    // reason is remembered (NoteColonizeHeldBack returns true only for news) and forgotten when it launches or stops
    // being a ready colonizer.
    public static bool RunColonizeHeldBackStateCheck()
    {
        var ok = true;
        using (var s = Scenario.Diamond())
        {
            ok &= Check(s.AI.ColonizeHeldBackReason("A") == null, "nothing is held back at first");
            ok &= Check(s.AI.NoteColonizeHeldBack("A", "NoRoute"), "the first turn a colonizer is held back is news");
            ok &= Check(!s.AI.NoteColonizeHeldBack("A", "NoRoute"), "the same reason again is not news");
            ok &= Check(s.AI.NoteColonizeHeldBack("A", "BlockadedOrigin"), "a different reason is news");
            ok &= Check(!s.AI.NoteColonizeHeldBack("A", "BlockadedOrigin"), "and then it is quiet again");

            // Nobody ready: the state is forgotten.
            s.AI.ProcessColonizers(new List<Planet.PlanetUpdateResult>(), new List<GameAI.GameAIOrder>());
            ok &= Check(s.AI.ColonizeHeldBackReason("A") == null, "with no ready colonizer the held-back state is forgotten");

            // Every way blocked: held back as NoRoute. Later turns find it already recorded, so nothing new is logged.
            s.Blockade("B");
            s.Blockade("C");
            s.AI.RefreshBlockadeView();
            s.Colonize();
            ok &= Check(s.AI.ColonizeHeldBackReason("A") == "NoRoute", "all routes blocked: A is held back as NoRoute");
            s.Colonize();
            ok &= Check(!s.AI.NoteColonizeHeldBack("A", "NoRoute"),
                "the next turn's identical hold-back is already recorded, so it is not news (and not logged again)");

            // A way opens: the colonizer launches and the state clears.
            s.Unblockade("C");
            s.AI.RefreshBlockadeView();
            ok &= Check(Colonist(s.Colonize()) != null, "with C open the colonizer launches");
            ok &= Check(s.AI.ColonizeHeldBackReason("A") == null, "launching clears the held-back state");

            // Blockaded origin: held back for a different reason; then cleared once nothing is ready.
            s.Unblockade("B");
            s.Blockade("A");
            s.AI.RefreshBlockadeView();
            s.Colonize();
            ok &= Check(s.AI.ColonizeHeldBackReason("A") == "BlockadedOrigin", "a blockaded origin is held back as BlockadedOrigin");
            s.AI.ProcessColonizers(new List<Planet.PlanetUpdateResult>(), new List<GameAI.GameAIOrder>());
            ok &= Check(s.AI.ColonizeHeldBackReason("A") == null, "and forgotten again when nothing is ready");
        }
        return ok;
    }

    private static bool RunBlockadeMemoryCheck()
    {
        var ok = true;

        var m = new BlockadeMemory();
        ok &= Check(m.Learn("P", 10f, 100, 10), "the first time a planet is learned is news");
        ok &= Check(m.IsActive("P", 100, 10) && m.IsActive("P", 109, 10),
            "learned at T100 with lifetime 10: active through T109");
        ok &= Check(!m.IsActive("P", 110, 10), "learned at T100 with lifetime 10: expired at T110");
        ok &= Check(!m.Learn("P", 12f, 105, 10), "a cut while still remembered is a refresh, not news");
        ok &= Check(m.IsActive("P", 114, 10) && !m.IsActive("P", 115, 10),
            "a refresh at T105 extends the memory to T114");
        ok &= Check(m.Active(114, 10).Count == 1 && Near(m.Active(114, 10)[0].Value, 12f) && m.Active(114, 10)[0].Turn == 105,
            "a refresh replaces the value and the turn");
        ok &= Check(m.Learn("P", 10f, 120, 10), "learning a planet again after it expired is news");
        m.Forget("P");
        ok &= Check(!m.IsActive("P", 120, 10), "Forget removes the entry");
        m.Forget("Nowhere");   // must not throw

        var off = new BlockadeMemory();
        ok &= Check(!off.Learn("P", 10f, 100, 0) && !off.IsActive("P", 100, 0) && off.Active(100, 10).Count == 0,
            "lifetime 0 disables memory: nothing is stored and nothing is news");

        var snap = new BlockadeMemory();
        snap.Learn("Old", 5f, 90, 10);
        snap.Learn("New", 7f, 100, 10);
        var active = snap.Snapshot(104, 10);
        ok &= Check(active.Count == 1 && active[0].Planet == "New" && Near(active[0].Value, 7f) && active[0].Turn == 100,
            "Snapshot lists only active entries (Old, learned T90, expired at T100)");

        snap.Restore(new List<BlockadeMemory.Entry>
            { new BlockadeMemory.Entry { Planet = "Other", Value = 3f, Turn = 102 } });
        ok &= Check(!snap.IsActive("New", 104, 10) && snap.IsActive("Other", 104, 10),
            "Restore replaces the contents");
        snap.Restore(null);
        ok &= Check(snap.Active(104, 10).Count == 0, "Restore(null) empties the memory");

        var pruned = new BlockadeMemory();
        pruned.Learn("Old", 5f, 90, 10);
        pruned.Learn("New", 7f, 100, 10);
        pruned.Prune(105, 10);
        ok &= Check(!pruned.IsActive("Old", 95, 10) && pruned.IsActive("New", 105, 10),
            "Prune drops the expired entry (Old is gone even when asked about an earlier turn) and keeps the active one");
        return ok;
    }

    private static GameAI.GameAIOrder ShipmentOrder(string origin, string target, int timingDelay, int totalDelay,
        List<string> route, float amount)
        => new GameAI.GameAIOrder
        {
            Type = GameAI.GameAIOrder.OrderType.OrderTypeFoodTransport,
            TimingType = GameAI.GameAIOrder.OrderTimingType.OrderTimingTypeDelayed,
            TimingDelay = timingDelay, TotalDelay = totalDelay,
            Data = amount, Origin = origin, Target = target, PlayerId = 0,
            Route = route,
        };

    // BlockadeSystem.Apply reports every cut it made (order owner, planet, value) so the owner can learn from it.
    public static bool RunBlockadeCutsCheck()
    {
        var ok = true;
        var go = new GameObject("BASelfCheckMap_Cuts");
        var template = WarshipSelfCheck.MakeTemplate();
        var constants = WarshipSelfCheck.MakeConstants(template);
        var research = WarshipSelfCheck.MakeResearch();
        try
        {
            var map = BuildDiamond(go, constants);
            var blockade = new BlockadeSystem(map, research);
            var route = new List<string> { "A", "C", "D" };

            // Nothing blockaded: no cut.
            var orders = new List<GameAI.GameAIOrder> { ColonistOrder("A", "D", 2, 4, route) };
            ok &= Check(blockade.Apply(orders, 1).Count == 0 && orders.Count == 1,
                "an order that meets no blockade reports no cut");

            // A colonist cut at C (10 offense): exactly one cut, for player 0, at C, value 10.
            WarshipSelfCheck.DockWarships(map.GetPlanet("C"), 1, 1);
            var cuts = blockade.Apply(orders, 1);
            ok &= Check(orders.Count == 0 && cuts.Count == 1 && cuts[0].PlayerId == 0 && cuts[0].Planet == "C"
                        && Near(cuts[0].Value, 10f),
                "a colonist order cut at C reports one cut: player 0, C, value 10");

            // A shipment reduced (30 - 20 = 10) at one node: one cut with the value 20; the order survives.
            map.GetPlanet("C").UndockShips(Ship.ShipKind.WarShip, 1, 99);
            WarshipSelfCheck.DockWarships(map.GetPlanet("C"), 1, 2);
            orders = new List<GameAI.GameAIOrder> { ShipmentOrder("A", "D", 2, 4, route, 30f) };
            cuts = blockade.Apply(orders, 1);
            ok &= Check(cuts.Count == 1 && cuts[0].PlayerId == 0 && cuts[0].Planet == "C" && Near(cuts[0].Value, 20f),
                "a shipment reduced at one node reports one cut with the value taken (20)");
            ok &= Check(orders.Count == 1 && Near(System.Convert.ToSingle(orders[0].Data), 10f),
                "the reduced shipment survives with 10 left (behavior unchanged)");

            // A shipment passing two blockaded nodes in one turn: two cuts, in route order.
            map.GetPlanet("C").UndockShips(Ship.ShipKind.WarShip, 1, 99);
            WarshipSelfCheck.DockWarships(map.GetPlanet("C"), 1, 1);
            WarshipSelfCheck.DockWarships(map.GetPlanet("D"), 1, 1);
            orders = new List<GameAI.GameAIOrder> { ShipmentOrder("A", "D", 0, 1, route, 30f) };
            cuts = blockade.Apply(orders, 1);
            ok &= Check(cuts.Count == 2 && cuts[0].Planet == "C" && Near(cuts[0].Value, 10f)
                        && cuts[1].Planet == "D" && Near(cuts[1].Value, 10f),
                "a shipment passing two blockaded nodes reports two cuts (C then D, 10 each)");
            ok &= Check(orders.Count == 1 && Near(System.Convert.ToSingle(orders[0].Data), 10f),
                "after both cuts the shipment carries 30 - 10 - 10 = 10");
        }
        finally
        {
            WarshipSelfCheck.DestroyAll(research);
            Object.DestroyImmediate(go);
            Object.DestroyImmediate(constants);
            Object.DestroyImmediate(template);
        }
        return ok;
    }

    // A player learns from a cut order: the planet is remembered blockaded for the lifetime and colonization routes
    // around it even though the player cannot see it; a direct sighting of the planet unblockaded forgets it.
    public static bool RunLearningRoutingCheck()
    {
        var ok = true;
        using (var s = Scenario.Hub())
        {
            ok &= Check(s.Constants.blockadeMemoryTurns == 20, "blockadeMemoryTurns defaults to 20");
            s.Constants.blockadeMemoryTurns = 10;   // the turn numbers below (5, 6, 16, 20) assume a lifetime of 10

            // A warship of player 1 sits at H, two hops from A: unseen and unremembered, so the colonist goes through it.
            s.Blockade("H");
            var unaware = Colonist(s.Colonize());
            ok &= Check(unaware != null && string.Join(">", unaware.Route) == "A>X>H>D",
                "with no view and no memory the colonist takes the direct route A>X>H>D");

            // One of its orders was cut at H at T5: remembered, so the next view lists H and the route detours.
            ok &= Check(s.AI.LearnBlockade("H", 10f, 5), "the first cut at H is news");
            s.AI.RefreshBlockadeView(5);
            ok &= Check(s.AI.CurrentBlockadeView.IsBlockaded("H") && Near(s.AI.CurrentBlockadeView.Value("H"), 10f),
                "H is in the view from memory, although it is not visible from A");
            var aware = Colonist(s.Colonize());
            ok &= Check(aware != null && string.Join(">", aware.Route) == "A>Y>Z>D",
                "after learning, the colonist routes around H: A>Y>Z>D");

            // Another cut at T6 only refreshes it.
            ok &= Check(!s.AI.LearnBlockade("H", 10f, 6), "a second cut at H is a refresh, not news");

            // Learned last at T6: at T16 (16 - 6 = 10) it has expired, the view forgets it and the route is direct again.
            s.AI.RefreshBlockadeView(16);
            ok &= Check(!s.AI.CurrentBlockadeView.IsBlockaded("H") && !s.AI.IsBlockadeRemembered("H", 16),
                "10 turns after the last cut the memory has expired");
            var expired = Colonist(s.Colonize());
            ok &= Check(expired != null && string.Join(">", expired.Route) == "A>X>H>D",
                "once the memory expires the colonist goes through H again");

            // A fresh sighting overrides memory: learn at T20, then gain presence at X (H becomes a visible neighbour)
            // with the blockade gone.
            ok &= Check(s.AI.LearnBlockade("H", 10f, 20), "learning H again after it expired is news");
            s.Map.GetPlanet("X").DockShipFromSave(Ship.ShipKind.ColonyShip, 0, new List<string>());
            s.Unblockade("H");
            s.AI.RefreshBlockadeView(20);
            ok &= Check(!s.AI.IsBlockadeRemembered("H", 20) && !s.AI.CurrentBlockadeView.IsBlockaded("H"),
                "seeing H directly with no blockade forgets the memory");
            var cleared = Colonist(s.Colonize());
            ok &= Check(cleared != null && string.Join(">", cleared.Route) == "A>X>H>D",
                "after the sighting the colonist goes through H");
        }
        return ok;
    }

    public static bool RunRememberedBlockadeSaveCheck()
    {
        var ok = true;

        var entry = new BlockadeMemory.Entry { Planet = "H", Value = 12.5f, Turn = 42 };
        var back = SaveLoadSystem.GameSave.RememberedBlockadeSave.From(entry).ToEntry();
        ok &= Check(back.Planet == "H" && Near(back.Value, 12.5f) && back.Turn == 42, "From and ToEntry are inverse");

        var save = new SaveLoadSystem.GameSave.PlayerSave
        {
            playerId = 1,
            rememberedBlockades = new List<SaveLoadSystem.GameSave.RememberedBlockadeSave>
                { SaveLoadSystem.GameSave.RememberedBlockadeSave.From(entry) },
        };
        var loaded = JsonUtility.FromJson<SaveLoadSystem.GameSave.PlayerSave>(JsonUtility.ToJson(save));
        ok &= Check(loaded.rememberedBlockades != null && loaded.rememberedBlockades.Count == 1
                    && loaded.rememberedBlockades[0].planet == "H" && Near(loaded.rememberedBlockades[0].value, 12.5f)
                    && loaded.rememberedBlockades[0].turn == 42,
            "a PlayerSave's remembered blockades survive a JsonUtility round trip");

        var older = JsonUtility.FromJson<SaveLoadSystem.GameSave.PlayerSave>("{\"playerId\":1}");
        var none = new BlockadeMemory();
        none.Learn("Stale", 1f, 100, 10);
        none.Restore(older.rememberedBlockades?.ConvertAll(b => b.ToEntry()));
        ok &= Check(older.rememberedBlockades == null || older.rememberedBlockades.Count == 0,
            "an older PlayerSave has no remembered blockades (null or empty)");
        ok &= Check(none.Active(100, 10).Count == 0, "restoring an older save's (missing) list leaves the memory empty");

        // Memory -> save structs -> JSON -> fresh memory reproduces the active entries.
        var source = new BlockadeMemory();
        source.Learn("C", 10f, 100, 10);
        source.Learn("D", 20f, 105, 10);
        source.Learn("E", 30f, 90, 10);   // expired by T108, so not saved
        var toSave = new SaveLoadSystem.GameSave.PlayerSave
        {
            rememberedBlockades = source.Snapshot(108, 10).ConvertAll(SaveLoadSystem.GameSave.RememberedBlockadeSave.From),
        };
        var reloaded = JsonUtility.FromJson<SaveLoadSystem.GameSave.PlayerSave>(JsonUtility.ToJson(toSave));
        var fresh = new BlockadeMemory();
        fresh.Restore(reloaded.rememberedBlockades?.ConvertAll(b => b.ToEntry()));
        var restored = fresh.Active(108, 10);
        ok &= Check(restored.Count == 2 && restored[0].Planet == "C" && Near(restored[0].Value, 10f) && restored[0].Turn == 100
                    && restored[1].Planet == "D" && Near(restored[1].Value, 20f) && restored[1].Turn == 105,
            "a memory survives snapshot, save structs, JSON and restore: C and D, not the expired E");
        return ok;
    }

    public static bool RunProductionResponseCheck()
    {
        var ok = true;
        var defaultConstants = ScriptableObject.CreateInstance<GameAIConstants>();
        ok &= Check(Near(defaultConstants.blockadedWarshipBoost, 3f),
            "blockadedWarshipBoost defaults to 3");
        Object.DestroyImmediate(defaultConstants);

        var colony = ProductionEntry("Colony Ship Production", "ColonyShip");
        var warship = ProductionEntry("Warship", "Warship");
        var update = ProductionEntry("Update Warship", "WarshipUpdate");
        try
        {
            using (var s = Scenario.Diamond())
            {
                s.Constants.blockadedWarshipBoost = 3f;

                // ColonyShip: wanted (2) when a target is reachable; 0 when every route is blockaded or the origin is.
                s.AI.Strategy = PlayerAI.AIStrategy.AIStrategyConsolidate;
                s.AI.RefreshBlockadeView();
                ok &= Check(Near(s.AI.GetIndustrySituationalWeightMultiplier(colony, "A"), 2f),
                    "unblockaded, a ready planet with a reachable target wants colony ships (2)");
                s.Blockade("B");
                s.AI.RefreshBlockadeView();
                ok &= Check(Near(s.AI.GetIndustrySituationalWeightMultiplier(colony, "A"), 2f),
                    "one way blockaded: the detour keeps a target reachable, colony ships still wanted");
                s.Blockade("C");
                s.AI.RefreshBlockadeView();
                ok &= Check(s.AI.GetIndustrySituationalWeightMultiplier(colony, "A") == 0f,
                    "every route blockaded: no reachable target, so no colony ships are built");
                s.Unblockade("B");
                s.Unblockade("C");
                s.Blockade("A");
                s.AI.RefreshBlockadeView();
                ok &= Check(s.AI.GetIndustrySituationalWeightMultiplier(colony, "A") == 0f,
                    "a blockaded planet builds no colony ships");
                s.Unblockade("A");

                // Warship under Expand (no fleet cap): 1, boosted x3 on a blockaded planet.
                s.AI.Strategy = PlayerAI.AIStrategy.AIStrategyExpand;
                s.AI.RefreshBlockadeView();
                ok &= Check(Near(s.AI.GetIndustrySituationalWeightMultiplier(warship, "A"), 1f),
                    "Expand, unblockaded: Warship multiplier 1");
                s.Blockade("A");
                s.AI.RefreshBlockadeView();
                ok &= Check(Near(s.AI.GetIndustrySituationalWeightMultiplier(warship, "A"), 3f),
                    "Expand, blockaded: the boost multiplies (1 x 3)");
                s.Unblockade("A");

                // Warship under Consolidate past the fleet cap: 0, but a blockaded planet is exempt (floor 1, x3).
                s.AI.Strategy = PlayerAI.AIStrategy.AIStrategyConsolidate;
                WarshipSelfCheck.DockWarships(s.Map.GetPlanet("A"), 0, 30);   // my fleet: far beyond wanted x cap
                s.AI.RefreshBlockadeView();
                ok &= Check(s.AI.ComputeWarshipMultiplier(PlayerAI.AIStrategy.AIStrategyConsolidate) == 0f,
                    "precondition: 30 own warships are past the fleet cap, so the plain multiplier is 0");
                ok &= Check(s.AI.GetIndustrySituationalWeightMultiplier(warship, "A") == 0f,
                    "Consolidate past the cap, unblockaded: Warship is not offered (0)");
                s.Blockade("A", 40);   // 400 offense against my 300: blockaded
                s.AI.RefreshBlockadeView();
                ok &= Check(s.AI.CurrentBlockadeView.IsBlockaded("A"), "precondition: A is blockaded (400 against 300)");
                ok &= Check(Near(s.AI.GetIndustrySituationalWeightMultiplier(warship, "A"), 3f),
                    "Consolidate past the cap but blockaded: exempt from the cutoff, floor 1 x boost 3");

                // Update Warship: boosted on a blockaded planet with something to upgrade; 0 when nothing to upgrade.
                s.AI.ResearchCatalog.catalogItems = WarshipSelfCheck.MakeResearch(1);   // 3 researched improvements
                var oldResearch = s.Research;
                s.Research = s.AI.ResearchCatalog.catalogItems;
                WarshipSelfCheck.DestroyAll(oldResearch);
                s.AI.RefreshBlockadeView();
                ok &= Check(Near(s.AI.GetIndustrySituationalWeightMultiplier(update, "A"), 3f),
                    "Update Warship on a blockaded planet with ships to upgrade is boosted x3");
                s.Unblockade("A");
                s.AI.RefreshBlockadeView();
                ok &= Check(Near(s.AI.GetIndustrySituationalWeightMultiplier(update, "A"), 1f),
                    "Update Warship unblockaded, with ships to upgrade: plain 1");
                var homePlanet = s.Map.GetPlanet("A");
                foreach (var ship in homePlanet.DockedShips)
                    if (ship.Kind == Ship.ShipKind.WarShip)
                        foreach (var name in WarshipStats.ResearchedNames(s.Research))
                            if (!ship.ResearchSnapshot.Contains(name)) ship.ResearchSnapshot.Add(name);
                s.Blockade("A", 60);
                s.AI.RefreshBlockadeView();
                ok &= Check(s.AI.GetIndustrySituationalWeightMultiplier(update, "A") == 0f,
                    "Update Warship stays 0 when nothing is upgradable, even on a blockaded planet");
            }
        }
        finally
        {
            Object.DestroyImmediate(colony);
            Object.DestroyImmediate(warship);
            Object.DestroyImmediate(update);
        }
        return ok;
    }
}
