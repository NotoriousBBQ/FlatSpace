using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using FlatSpace.AI;
using FlatSpace.Pathing;
using Flatspace.Objects.Production;

public static class BlockadeAvoidanceSelfCheck
{
    [MenuItem("FlatSpace/AI/Run Blockade Avoidance Self-Check")]
    public static void Run() => RunChecks();

    public static bool RunChecks()
    {
        var ok = RunBlockadeViewCheck();
        ok &= RunRoutePlannerCheck();
        ok &= RunShortestPathAvoidingCheck();
        ok &= RunCarriedRouteCheck();
        ok &= RunColonizationRoutingCheck();
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
    private static PlanetSpawnData Spawn(string name, float x, float y, IEnumerable<string> connections = null,
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

    private static GameAIMap Build(GameObject go, GameAIConstants constants, params PlanetSpawnData[] spawns)
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

    // The diamond with player 0 present at A: 5 inhabitants (a full planet, ready to colonize), B and C not valid
    // targets (a colonist is already inbound), D the only target. Player 0's research catalog holds MakeResearch().
    private sealed class Scenario : System.IDisposable
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

        public void Blockade(string planet, int warships = 1)
            => WarshipSelfCheck.DockWarships(Map.GetPlanet(planet), 1, warships);

        public void Unblockade(string planet)
            => Map.GetPlanet(planet).UndockShips(Ship.ShipKind.WarShip, 1, 999);

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
}
