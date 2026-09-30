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
}
