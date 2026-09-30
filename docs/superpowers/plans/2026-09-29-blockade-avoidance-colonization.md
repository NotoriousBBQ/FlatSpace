# Blockade Avoidance for Colonization Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make colonization blockade-aware: colony ships route around planets the player can see are blockaded against it, colonization is cancelled when no route exists, a blockaded planet builds no colony ships and launches none, and a blockaded planet builds warships (boosted, exempt from the fleet cap) to break the blockade.

**Architecture:** A per-player, per-turn `BlockadeView` (visible planets whose docked-offense value is positive against that player) feeds a `RoutePlanner` (normal shortest path when clean, otherwise a fresh Dijkstra that skips visibly blockaded planets, with no node limit). Colonist orders carry their planned route (`GameAIOrder.Route`, saved as `OrderSave.route`) so `BlockadeSystem` applies blockades along the route actually flown. `PlayerAI` uses the planner in colonization and production weights.

**Tech Stack:** Unity 6000.4.1f1, C#, JsonUtility saves, Editor-menu self-checks (no test framework; see CLAUDE.md "Tests").

**Spec:** `docs/superpowers/specs/2026-09-29-blockade-avoidance-colonization-design.md`

## Global Constraints

- Work on a feature branch `blockade-avoidance` (not `main`); merge only when the user says so.
- No command-line build. Compile check = user focuses the Unity Editor and reads the Console; a self-check runs from its `FlatSpace → AI` menu item. Rider MCP build tools are unreliable (CLAUDE.md); `get_file_problems` is a weak signal only. The Editor may be unavailable while the plan runs: every "run the self-check" step is then PENDING for the user, and code is verified by careful reading plus hand-walking each new assertion.
- A self-check must never depend on `Gameboard.Instance`; `Planet.DockNewShip`/`DockShipRebuiltSnapshot` need it, so use `DockShipFromSave`.
- Methods a self-check calls are `public`, not `internal` (`Assets/Editor` is a separate assembly).
- `OrderType` serializes as an int: never insert an enum value (this plan adds none).
- Every new script's `.meta` file is committed with it; when the Editor is unavailable hand-write it as exactly two lines, `fileFormatVersion: 2` then `guid: <32 random lowercase hex>` (generate with PowerShell `[guid]::NewGuid().ToString('N')`), no BOM, no other content.
- Namespace `FlatSpace.AI` for new AI classes; `Path`/`PathNode`/`Connection`/`PathingSystem` are in `FlatSpace.Pathing`; catalog types in `Flatspace.Objects.Production`; `Planet`, `Ship`, `ShipData`, `Player`, `AITuningLogger`, `SaveLoadSystem` are in the global namespace; `Gameboard` is `FlatSpace.Game.Gameboard`.
- Visibility (spec): a player sees the docked ships, hence the blockade value, of every planet it has presence at (population or a docked ship, via `GameAIMap.GetVisionSourcePlanets`) and of their direct neighbours (`GameAIMap.GetNeighbours`), recomputed each turn, not sticky. Never the sticky `PlayerKnowledge` set and never the fog-of-war grid. A planet the player cannot see is treated as unblockaded.
- Detours (spec): computed only when the target is in range by node count (`2 <= NumNodes <= maxPathNodesForColonization`) but its shortest path passes a visibly blockaded planet; the detour may exceed the node range; a blockaded (visible) target has no route; a target out of range by node count is never made reachable; the 1-node no-route stub is never a route.
- Fleet cap (spec): a blockaded planet is exempt from the Consolidate fleet-cap cutoff: its Warship multiplier is floored at 1, then multiplied by `blockadedWarshipBoost` (in-code default 3). Update Warship gets the same boost, but its existing 0 (nothing to upgrade) stays 0. ColonyShip weight is 0 on a blockaded planet. State-dependent factors live in `PlayerAI.GetIndustrySituationalWeightMultiplier`.
- Log lines (no toggle checks at call sites): `T<turn>|P<id>|RouteDetour|<origin>-><target>|<nodes joined by '>'>|<cost>` and `T<turn>|P<id>|ColonizeCancelled|<origin>|<BlockadedOrigin or NoRoute>`.
- A missing/older `OrderSave.route` loads as no route and falls back to the shortest path.
- Commit messages end with `Co-Authored-By: Claude Sonnet 5.5 <noreply@anthropic.com>`.
- Planets in self-checks need distinct positions (the `PathingSystem.FindPath` tie-break note in CLAUDE.md).

## Review Focus

- A colonizer whose every route is blocked emits no colonist order and stays ready (retries next turn). (Task 4 check.)
- A visibly blockaded target is never chosen, and a target whose only route passes a blockade is dropped. (Tasks 2 and 4 checks.)
- A null `BlockadeView` (self-checks, or before the first turn) behaves as "nothing blockaded": colonization is exactly as before. (Task 4 check.)
- An order saved before this change (no `route`) still gets its blockade checked along the shortest path. (Task 3 checks.)
- A target with only a 1-node no-route stub (unreachable) is now dropped by the planner instead of receiving a free zero-cost colonist (deliberate tightening of `ProcessColonizers`, which used to accept `NumNodes <= max`). (Task 2 check.)

---

### Task 1: `BlockadeView`, shared self-check helpers and the new suite

**Files:**
- Create: `Assets/Flatspace/GameAI/BlockadeView.cs` (+ `.meta`)
- Create: `Assets/Editor/BlockadeAvoidanceSelfCheck.cs` (+ `.meta`)
- Modify: `Assets/Editor/WarshipSelfCheck.cs` (three helpers become `public`)
- Modify: `Assets/Editor/AllAISelfChecks.cs` (register the new suite)

**Interfaces:**
- Consumes: `BlockadeSystem(GameAIMap, IEnumerable<CatalogItem>)` and `.Value(Planet, int, out int)`; `GameAIMap.GetVisionSourcePlanets(int)` (returns `List<VisionSource>` with `.Planet`), `GameAIMap.GetNeighbours(string)`, `GameAIMap.GetPlanet(string)`; `Planet.NoOwner`.
- Produces (`FlatSpace.AI.BlockadeView`): `static BlockadeView Build(GameAIMap map, int playerId, BlockadeSystem blockade)`; `static BlockadeView Of(params string[] blockadedPlanets)` (test/planning helper: each listed planet has value 1, blocker `Planet.NoOwner`); `bool IsBlockaded(string name)`; `float Value(string name)`; `int Blocker(string name)`; `IEnumerable<string> BlockadedNames`.
- Produces (`WarshipSelfCheck`, now public): `MakeConstants(ShipData)`, `DockWarships(Planet, int owner, int count, params string[] snapshot)`, `DestroyAll(List<CatalogItem>)`.
- Produces (`BlockadeAvoidanceSelfCheck`): `RunChecks()`, private `Check`/`Near`/`Spawn`/`Build`/`BuildDiamond`, and a private `Scenario` class later tasks extend.

- [ ] **Step 1: Create `BlockadeView.cs`**

```csharp
using System.Collections.Generic;

namespace FlatSpace.AI
{
    /// <summary>
    /// One player's view of blockades this turn: the planets it can SEE whose docked-offense blockade value against it is
    /// positive. Visible = every planet the player has presence at (a GetVisionSourcePlanets planet: population or a
    /// docked ship) plus their direct neighbours. A planet the player cannot see is treated as unblockaded. Pure (no
    /// Gameboard.Instance) and derived, so it is rebuilt each turn and never saved.
    /// </summary>
    public class BlockadeView
    {
        private readonly Dictionary<string, (float value, int blocker)> _blockaded
            = new Dictionary<string, (float value, int blocker)>();

        public IEnumerable<string> BlockadedNames => _blockaded.Keys;

        public bool IsBlockaded(string planetName)
            => planetName != null && _blockaded.ContainsKey(planetName);

        public float Value(string planetName)
            => planetName != null && _blockaded.TryGetValue(planetName, out var entry) ? entry.value : 0f;

        public int Blocker(string planetName)
            => planetName != null && _blockaded.TryGetValue(planetName, out var entry) ? entry.blocker : Planet.NoOwner;

        public static BlockadeView Build(GameAIMap map, int playerId, BlockadeSystem blockade)
        {
            var view = new BlockadeView();
            var visible = new HashSet<string>();
            foreach (var source in map.GetVisionSourcePlanets(playerId))
            {
                visible.Add(source.Planet.PlanetName);
                foreach (var neighbour in map.GetNeighbours(source.Planet.PlanetName))
                    visible.Add(neighbour);
            }

            foreach (var name in visible)
            {
                var planet = map.GetPlanet(name);
                if (planet == null) continue;
                var value = blockade.Value(planet, playerId, out var blocker);
                if (value > 0f) view._blockaded[name] = (value, blocker);
            }
            return view;
        }

        /// <summary>A view in which exactly these planets are blockaded (value 1, no blocker). For planning tests.</summary>
        public static BlockadeView Of(params string[] blockadedPlanets)
        {
            var view = new BlockadeView();
            foreach (var name in blockadedPlanets) view._blockaded[name] = (1f, Planet.NoOwner);
            return view;
        }
    }
}
```

Create its `.meta` (two lines, see Global Constraints).

- [ ] **Step 2: Make three `WarshipSelfCheck` helpers public**

In `Assets/Editor/WarshipSelfCheck.cs` change exactly these three signatures (keep bodies):

```csharp
    public static GameAIConstants MakeConstants(ShipData warship)
```
(was `private static GameAIConstants MakeConstants(ShipData warship)`)

```csharp
    public static void DockWarships(Planet planet, int owner, int count, params string[] snapshot)
```
(was `private static void DockWarships(...)`)

```csharp
    public static void DestroyAll(List<CatalogItem> items)
```
(was `private static void DestroyAll(List<CatalogItem> items)`)

- [ ] **Step 3: Create `BlockadeAvoidanceSelfCheck.cs` with the view check**

```csharp
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
}
```

Hand-walk check (the implementer must do this since it cannot run): at B player 1 has 10, player 0 has 10 after the own dock, so `Value(B, 0)` is 10 - 10 = 0 (cancelled) and `Value(B, 1)` is 10 - 10 = 0; the last-but-one assertion (`view1`) therefore expects `B` not blockaded for player 1. At C player 1 has 10 and player 0 has 0, so from player 1's view C is not blockaded either (its own ships). Keep the assertion as written (`!IsBlockaded || value <= 0`).

Create the `.meta` files for `BlockadeView.cs` and `BlockadeAvoidanceSelfCheck.cs`.

- [ ] **Step 4: Register the suite in `AllAISelfChecks.cs`**

In the `suites` list add, after the Warship entry:

```csharp
            ("Blockade Avoidance", BlockadeAvoidanceSelfCheck.RunChecks),
```

- [ ] **Step 5: Verify**

Ask the user to focus the Unity Editor (recompile), fix any Console errors, then run `FlatSpace → AI → Run Blockade Avoidance Self-Check`. Expected: `[BlockadeAvoidanceSelfCheck] ALL PASSED`. Also run `Run Warship Self-Check` to confirm the visibility change broke nothing. If the Editor is unavailable: PENDING; hand-walk every assertion.

- [ ] **Step 6: Commit**

```bash
git add Assets/Flatspace/GameAI/BlockadeView.cs Assets/Flatspace/GameAI/BlockadeView.cs.meta Assets/Editor/BlockadeAvoidanceSelfCheck.cs Assets/Editor/BlockadeAvoidanceSelfCheck.cs.meta Assets/Editor/WarshipSelfCheck.cs Assets/Editor/AllAISelfChecks.cs
git commit -m "feat(blockade): add BlockadeView and the blockade avoidance self-check suite

Co-Authored-By: Claude Sonnet 5.5 <noreply@anthropic.com>"
```

---

### Task 2: `RoutePlanner`

**Files:**
- Create: `Assets/Flatspace/GameAI/RoutePlanner.cs` (+ `.meta`)
- Modify: `Assets/Editor/BlockadeAvoidanceSelfCheck.cs`

**Interfaces:**
- Consumes: `BlockadeView` (`IsBlockaded`, `BlockadedNames`) from Task 1; `GameAIMap.GetPath(string, string)` (returns `Path` with ordered `PathNodes`, `Cost`, `NumNodes`), `Planet.DistanceMapToPathingList` (`GameAIMap.DestinationToPathingListEntry.NumNodes`), `PathingSystem.Instance.PathNodes` (`Dictionary<string, PathNode>`; `PathNode.Connections` is a `List<Connection>` with `NodeName`, `Cost`).
- Produces (`FlatSpace.AI.RoutePlanner`): nested `class PlannedRoute { List<string> Nodes; float Cost; int NumNodes; bool IsDetour; }`; `static PlannedRoute PlanRoute(GameAIMap map, string origin, string target, BlockadeView view, int maxNodes)` (null = cancel); `static PlannedRoute ShortestPathAvoiding(IReadOnlyDictionary<string, PathNode> graph, string origin, string target, ISet<string> blocked)` (null when there is no path; result has `IsDetour = true`).

- [ ] **Step 1: Add the planner checks**

In `BlockadeAvoidanceSelfCheck.RunChecks()` add `ok &= RunRoutePlannerCheck();` and `ok &= RunShortestPathAvoidingCheck();` after `RunBlockadeViewCheck()`, and add these members:

```csharp
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
```

- [ ] **Step 2: Create `RoutePlanner.cs`**

```csharp
using System.Collections.Generic;
using System.Linq;
using FlatSpace.Pathing;

namespace FlatSpace.AI
{
    /// <summary>
    /// Plans a colonization route that avoids planets the player can SEE are blockaded against it. Pure (no
    /// Gameboard.Instance). The normal shortest path is used whenever it is clean, so behavior on unblockaded routes is
    /// exactly as before; otherwise a fresh Dijkstra skips the blockaded planets (FindPath's A* tie-breaking is fragile,
    /// see CLAUDE.md, so it is left alone).
    /// </summary>
    public static class RoutePlanner
    {
        public class PlannedRoute
        {
            public List<string> Nodes = new List<string>();   // origin to target inclusive
            public float Cost;
            public int NumNodes => Nodes.Count;
            public bool IsDetour;
        }

        /// <summary>
        /// The route from origin to target, or null when colonization must be cancelled: the target (or origin) is
        /// unknown or the same, the target is visibly blockaded, the target is out of range by node count or has only
        /// FindPath's 1-node no-route stub, or every way round the blockaded planets is closed. A target in range whose
        /// shortest path is blocked gets a detour, which may exceed maxNodes.
        /// </summary>
        public static PlannedRoute PlanRoute(GameAIMap map, string origin, string target, BlockadeView view, int maxNodes)
        {
            if (origin == target) return null;
            var originPlanet = map.GetPlanet(origin);
            if (originPlanet == null || map.GetPlanet(target) == null) return null;
            if (view != null && view.IsBlockaded(target)) return null;
            if (!originPlanet.DistanceMapToPathingList.TryGetValue(target, out var entry)) return null;
            // 1 node is the no-route stub (unreachable); more than maxNodes is beyond the normal range.
            if (entry.NumNodes < 2 || entry.NumNodes > maxNodes) return null;

            var shortest = map.GetPath(origin, target);
            var names = shortest.PathNodes.Select(n => n.Name).ToList();
            if (view == null || !names.Skip(1).Any(view.IsBlockaded))
                return new PlannedRoute { Nodes = names, Cost = shortest.Cost, IsDetour = false };

            return ShortestPathAvoiding(PathingSystem.Instance.PathNodes, origin, target,
                new HashSet<string>(view.BlockadedNames));
        }

        /// <summary>
        /// Dijkstra over the explicit graph skipping every node in `blocked` (the origin is never expanded into, so it is
        /// never skipped). Ties in distance expand the lexicographically smaller node name first. Null when there is no
        /// path. The result is marked IsDetour.
        /// </summary>
        public static PlannedRoute ShortestPathAvoiding(IReadOnlyDictionary<string, PathNode> graph, string origin,
            string target, ISet<string> blocked)
        {
            if (!graph.ContainsKey(origin) || !graph.ContainsKey(target)) return null;

            var dist = new Dictionary<string, float> { [origin] = 0f };
            var previous = new Dictionary<string, string>();
            var done = new HashSet<string>();
            while (true)
            {
                string current = null;
                var best = float.MaxValue;
                foreach (var pair in dist)
                {
                    if (done.Contains(pair.Key)) continue;
                    if (current == null || pair.Value < best
                        || (pair.Value == best && string.CompareOrdinal(pair.Key, current) < 0))
                    {
                        current = pair.Key;
                        best = pair.Value;
                    }
                }
                if (current == null) return null;   // the reachable frontier is exhausted: no path
                if (current == target) break;

                done.Add(current);
                foreach (var edge in graph[current].Connections)
                {
                    var next = edge.NodeName;
                    if (done.Contains(next) || !graph.ContainsKey(next)) continue;
                    if (blocked != null && blocked.Contains(next)) continue;
                    var candidate = dist[current] + edge.Cost;
                    if (!dist.TryGetValue(next, out var existing) || candidate < existing)
                    {
                        dist[next] = candidate;
                        previous[next] = current;
                    }
                }
            }

            var nodes = new List<string>();
            var at = target;
            while (at != null)
            {
                nodes.Add(at);
                at = previous.TryGetValue(at, out var parent) ? parent : null;
            }
            nodes.Reverse();
            return new PlannedRoute { Nodes = nodes, Cost = dist[target], IsDetour = true };
        }
    }
}
```

Create its `.meta`.

Hand-walk notes for the implementer: in the diamond, A to D via B is 111.8 + 111.8 = 223.6 and via C is 180.3 + 180.3 = 360.6, so `FindPath` (which terminates when a neighbour of the popped node is the destination) yields `A>B>D`; the tie graph has S-M1-T and S-M2-T at equal cost 20 (expand order M1 then M2 because both have distance 10 and "M1" < "M2"; T is first set via M1 and M2's relaxation `20 < 20` is false, so the route stays via M1). The Z stub: `FindPath` returns a 1-node path so `DistanceMapToPathingList["Z"].NumNodes == 1`.

- [ ] **Step 3: Verify**

Recompile and run `Run Blockade Avoidance Self-Check` (expect `ALL PASSED`). If the Editor is unavailable: PENDING; hand-walk each assertion.

- [ ] **Step 4: Commit**

```bash
git add Assets/Flatspace/GameAI/RoutePlanner.cs Assets/Flatspace/GameAI/RoutePlanner.cs.meta Assets/Editor/BlockadeAvoidanceSelfCheck.cs
git commit -m "feat(blockade): add RoutePlanner with blockade-avoiding detours

Co-Authored-By: Claude Sonnet 5.5 <noreply@anthropic.com>"
```

---

### Task 3: Orders carry their route

**Files:**
- Modify: `Assets/Flatspace/GameAI/GameAI.cs` (`GameAIOrder.Route`, `RouteFromSave`, restore in `SetSimulationStats`)
- Modify: `Assets/Flatspace/SaveSystem/SaveLoadSystem.cs` (`OrderSave.route`, write it)
- Modify: `Assets/Flatspace/GameAI/GameAIMap.cs` (`EdgeCost`)
- Modify: `Assets/Flatspace/GameAI/BlockadeSystem.cs` (`RouteFor`, `PassedNodes` uses it)
- Modify: `Assets/Editor/BlockadeAvoidanceSelfCheck.cs`

**Interfaces:**
- Consumes: `PathingSystem.Instance.PathNodes`, `BlockadeSystem.Route(origin, target)`, `PassedNodes`, `Apply` (existing, Task 4/5 of the previous plan).
- Produces: `GameAI.GameAIOrder.Route` (`List<string>`, `[NonSerialized]`, origin to target inclusive); `static List<string> GameAI.GameAIOrder.RouteFromSave(List<string> saved)` (null when fewer than 2 nodes); `SaveLoadSystem.GameSave.OrderSave.route` (`List<string>`); `GameAIMap.EdgeCost(string a, string b)` (float; 0 when there is no such edge); `BlockadeSystem.RouteFor(GameAI.GameAIOrder order)` (`List<RouteNode>`).

- [ ] **Step 1: Add the checks**

In `RunChecks()` add `ok &= RunCarriedRouteCheck();` and add:

```csharp
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
```

Hand-walk: the detour A>C>D has edge costs 180.3 and 180.3, so C's fraction is 0.5; `TimingDelay 2` of `TotalDelay 4` gives progress 0.5 and previous 0.25, so C is passed this turn. On the shortest path A>B>D the edge costs are 111.8 each, so B's fraction is 0.5 and is likewise passed. One warship (offense 10) at a planet blockades a colonist owned by player 0 (no own offense) by 10.

- [ ] **Step 2: `GameAIMap.EdgeCost`**

In `GameAIMap.cs`, after `GetPath` (which ends with `return reversed;\n            }`), add:

```csharp
            /// <summary>The connection cost between two adjacent planets; 0 when they are not adjacent or unknown.</summary>
            public float EdgeCost(string a, string b)
            {
                if (!PathingSystem.Instance.PathNodes.TryGetValue(a, out var node)) return 0f;
                foreach (var connection in node.Connections)
                    if (connection.NodeName == b)
                        return connection.Cost;
                return 0f;
            }
```

- [ ] **Step 3: `GameAIOrder.Route` and `RouteFromSave`**

In `GameAI.cs`, inside `GameAIOrder`, after the `Fleet` field:

```csharp
                // The planned route of a colonist order (origin to target inclusive), so blockade is applied along the route
                // actually flown, not the shortest path. Not serialized by Unity; saves go through OrderSave.route. Null
                // (no route recorded) means "use the shortest path".
                [NonSerialized] public List<string> Route;

                /// <summary>A saved route of fewer than 2 nodes (older saves, non-route orders) restores as none.</summary>
                public static List<string> RouteFromSave(List<string> saved)
                    => saved != null && saved.Count >= 2 ? new List<string>(saved) : null;
```

In `GameAI.SetSimulationStats`, in the `new GameAIOrder { ... }` initializer add after the `Fleet = ...` line (keeping a comma before it):

```csharp
                        Route = GameAIOrder.RouteFromSave(orderStatus.route),
```

- [ ] **Step 4: `OrderSave.route` and writing it**

In `SaveLoadSystem.cs`, in `OrderSave` after `fleetShips`:

```csharp
            // A colonist order's planned route (null/empty for every other order and for older saves).
            public List<string> route;
```

and in the save loop's `new OrderSave { ... }` add after `fleetShips = ...`:

```csharp
                        route = order.Route != null ? new List<string>(order.Route) : null
```
(add a comma after the `fleetShips = order.Fleet?.ToSave(order.PlayerId)` line).

- [ ] **Step 5: `BlockadeSystem.RouteFor`**

In `BlockadeSystem.cs`, add after `Route(string origin, string target)`:

```csharp
        /// <summary>
        /// The nodes an order will pass, after its origin. A carried route (2 or more nodes) is used as planned, with
        /// fractions from edge costs; an order with no route (older saves, or one planned before routes were carried)
        /// falls back to the shortest path.
        /// </summary>
        public List<RouteNode> RouteFor(GameAI.GameAIOrder order)
        {
            var route = order.Route;
            if (route == null || route.Count < 2) return Route(order.Origin, order.Target);

            var cumulative = new float[route.Count];
            for (var i = 1; i < route.Count; i++)
                cumulative[i] = cumulative[i - 1] + _map.EdgeCost(route[i - 1], route[i]);

            var total = cumulative[route.Count - 1];
            var result = new List<RouteNode>();
            for (var i = 1; i < route.Count; i++)
                result.Add(new RouteNode
                {
                    Name = route[i],
                    Fraction = total > 0f ? cumulative[i] / total : (float)i / (route.Count - 1),
                });
            return result;
        }
```

and in `PassedNodes` replace `return Route(order.Origin, order.Target)` with `return RouteFor(order)`.

- [ ] **Step 6: Verify**

Recompile; run `Run Blockade Avoidance Self-Check`, `Run Warship Self-Check` (its blockade checks use orders with no route and must still pass) and `Run Ship Transport Self-Check`. Expected: all pass. PENDING if the Editor is unavailable.

- [ ] **Step 7: Commit**

```bash
git add Assets/Flatspace/GameAI/GameAI.cs Assets/Flatspace/SaveSystem/SaveLoadSystem.cs Assets/Flatspace/GameAI/GameAIMap.cs Assets/Flatspace/GameAI/BlockadeSystem.cs Assets/Editor/BlockadeAvoidanceSelfCheck.cs
git commit -m "feat(blockade): colonist orders carry their planned route

Co-Authored-By: Claude Sonnet 5.5 <noreply@anthropic.com>"
```

---

### Task 4: Colonization wiring and logging

**Files:**
- Modify: `Assets/Flatspace/GameAI/PlayerAI.cs` (`ProcessResults`, view, `ProcessColonizers`, `PlanetHasColonizationTarget`)
- Modify: `Assets/Flatspace/Diagnostics/AITuningLogger.cs` (two log methods)
- Modify: `Assets/Editor/BlockadeAvoidanceSelfCheck.cs`

**Interfaces:**
- Consumes: `BlockadeView.Build`, `RoutePlanner.PlanRoute/PlannedRoute`, `GameAIOrder.Route`, `BlockadeSystem(GameAIMap, IEnumerable<CatalogItem>)`.
- Produces: `PlayerAI.RefreshBlockadeView()` (public), `PlayerAI.CurrentBlockadeView` (public, `BlockadeView`, null before the first refresh), private `PlayerAI.IsBlockaded(string)`; `AITuningLogger.LogRouteDetour(int turn, int playerId, string origin, string target, IEnumerable<string> nodes, float cost)`, `AITuningLogger.LogColonizeCancelled(int turn, int playerId, string origin, string reason)`.

- [ ] **Step 1: Add the scenario and checks**

In `BlockadeAvoidanceSelfCheck` add this private class and check, and add `ok &= RunColonizationRoutingCheck();` to `RunChecks()`:

```csharp
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
```

Hand-walk notes: `Colonize()` returns the four colonist orders (transport, change, in-progress, remove-ship) when it launches (base food 1 at D so no rider). Delay is `Convert.ToInt32(route.Cost / 1)`; A-B-D is about 223.6 (delay 224) and A-C-D about 360.6 (delay 361). B and C are not valid targets (`IsPopulationTransferInProgress(0)`), so D is the only target. The one-warship blockader has offense 10 against my colony-ship-only presence (no offense).

- [ ] **Step 2: Logger methods**

In `AITuningLogger.cs`, after `LogOrderBlocked` add:

```csharp
    /// <summary>A colonist took a detour around visible blockades: T&lt;turn&gt;|P&lt;id&gt;|RouteDetour|origin-&gt;target|nodes joined by '&gt;'|cost.</summary>
    public static void LogRouteDetour(int turnNumber, int playerId, string origin, string target,
        IEnumerable<string> nodes, float cost)
    {
        if (_currentLogPath == null) return;
        AppendLines(new List<string> { FormatLine(turnNumber, playerId, "RouteDetour", $"{origin}->{target}",
            string.Join(">", nodes), cost.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture)) });
    }

    /// <summary>A ready colonizer was held back: T&lt;turn&gt;|P&lt;id&gt;|ColonizeCancelled|origin|BlockadedOrigin or NoRoute. Repeats every turn the condition holds.</summary>
    public static void LogColonizeCancelled(int turnNumber, int playerId, string origin, string reason)
    {
        if (_currentLogPath == null) return;
        AppendLines(new List<string> { FormatLine(turnNumber, playerId, "ColonizeCancelled", origin, reason) });
    }
```

- [ ] **Step 3: `PlayerAI` view, `ProcessResults` and the helper**

In `PlayerAI.cs`, after the `ResearchCatalog` property (line ~32) add:

```csharp
            // ── Blockade awareness ───────────────────────────────────────────

            private BlockadeView _blockadeView;

            /// <summary>This player's view of visible blockades, rebuilt at the start of every ProcessResults; null before the first.</summary>
            public BlockadeView CurrentBlockadeView => _blockadeView;

            /// <summary>Rebuilds the view from current docked ships. Public for the self-check.</summary>
            public void RefreshBlockadeView()
            {
                var items = ResearchCatalog != null ? ResearchCatalog.catalogItems : null;
                _blockadeView = BlockadeView.Build(AIMap, Player.playerID, new BlockadeSystem(AIMap, items));
            }

            // A null view (self-checks, before the first turn) means nothing is known to be blockaded.
            private bool IsBlockaded(string planetName)
                => _blockadeView != null && _blockadeView.IsBlockaded(planetName);
```

In `ProcessResults`, right after the `TryEnterConsolidate(...)` line add:

```csharp
                RefreshBlockadeView();
```

- [ ] **Step 4: `PlanetHasColonizationTarget` uses the planner**

Replace the method (currently `PlayerAI.cs:105-112`) with:

```csharp
            private bool PlanetHasColonizationTarget(string planetName)
            {
                var origin = AIMap.GetPlanet(planetName);
                var maxNodes = AIMap.GameAIConstants.maxPathNodesForColonization;
                return origin.DistanceMapToPathingList.Any(t =>
                    t.Value.NumNodes <= maxNodes
                    && IsValidColonizationTarget(AIMap.GetPlanet(t.Key))
                    && CanSupportColony(origin, AIMap.GetPlanet(t.Key))
                    // a route must exist that avoids the planets I can see are blockaded (the planner also rejects the
                    // 1-node no-route stub)
                    && RoutePlanner.PlanRoute(AIMap, planetName, t.Key, _blockadeView, maxNodes) != null);
            }
```

- [ ] **Step 5: `ProcessColonizers` uses the planner and skips a blockaded origin**

Replace the whole method (`public void ProcessColonizers(...)` through its closing brace, currently `PlayerAI.cs:328-418`) with:

```csharp
            public void ProcessColonizers(
                List<Planet.PlanetUpdateResult> results,
                List<GameAI.GameAIOrder>        orders)
            {
                var colonizers = results.FindAll(
                    x => x.PlayerID == Player.playerID 
                         && x.Result == Planet.PlanetUpdateResult.PlanetUpdateResultType.PlanetUpdateResultTypeColonizerReady);
                if (colonizers.Count == 0) return;

                var targets = AIMap.PlanetList.FindAll(IsValidColonizationTarget);
                if (targets.Count == 0) return;

                var turn = Gameboard.Instance != null ? Gameboard.Instance.TurnNumber : 0;
                var maxNodes = AIMap.GameAIConstants.maxPathNodesForColonization;
                // The route planned for each (origin, target) choice; the chosen one rides on the colonist order below.
                var plannedRoutes = new Dictionary<(string origin, string target), RoutePlanner.PlannedRoute>();

                var matrix = new ScoreMatrix<ScoreMatrixDecisionElement, ScoreMatrixChoiceElement, ScoreMatrixAction>
                    (new ScoreMatrixDecisionComparer());
                int colonizerIndex = 0;
                foreach (var colonizer in colonizers)
                {
                    // A blockaded planet keeps its colony ship until the blockade lifts.
                    if (IsBlockaded(colonizer.Name))
                    {
                        AITuningLogger.LogColonizeCancelled(turn, Player.playerID, colonizer.Name, "BlockadedOrigin");
                        continue;
                    }

                    var colonizerPlanet = AIMap.GetPlanet(colonizer.Name);
                    var pathMap = colonizerPlanet.DistanceMapToPathingList;
                    var entries = new List<ScoreMatrixChoiceElement>();
                    var hadCandidate = false;
                    foreach (var t in targets)
                    {
                        if (!pathMap.ContainsKey(t.PlanetName)
                            || pathMap[t.PlanetName].NumNodes > maxNodes
                            || !CanSupportColony(colonizerPlanet, t))
                            continue;
                        hadCandidate = true;

                        // Avoid planets I can see are blockaded; a target with no such route is dropped.
                        var route = RoutePlanner.PlanRoute(AIMap, colonizer.Name, t.PlanetName, _blockadeView, maxNodes);
                        if (route == null) continue;
                        plannedRoutes[(colonizer.Name, t.PlanetName)] = route;
                        entries.Add(new ScoreMatrixChoiceElement
                        {
                            Surplus  = 1.0f,
                            Target   = t.PlanetName,
                            Cost     = route.Cost,
                            Shortage = 1.0f,
                        });
                    }

                    if (entries.Count == 0 && hadCandidate)
                        AITuningLogger.LogColonizeCancelled(turn, Player.playerID, colonizer.Name, "NoRoute");

                    if (entries.Count > 0)
                    {
                        
                            matrix.MatrixElements.Add(
                                new ScoreMatrixDecisionElement
                                {
                                    Target = colonizer.Name,
                                    Priority = 0f
                                }, entries);
                        colonizerIndex++;
                    }
                }

                foreach (var action in matrix.GenerateActionList(
                             actionFactory: (origin, element) => new ScoreMatrixAction
                             {
                                 Origin = origin.Target,
                                 Target = element.Target,
                                 Cost = element.Cost
                             }, null))
                {
                    var amount = Convert.ToInt32(
                        colonizers.Find(x => x.Name == action.Origin).Data);
                    var delay  = Convert.ToInt32(
                        action.Cost / AIMap.GameAIConstants.defaultTravelSpeed);
                    var route  = plannedRoutes[(action.Origin, action.Target)];

                    var colonist = MakeOrder(GameAI.GameAIOrder.OrderType.OrderTypePopulationTransport,
                        GameAI.GameAIOrder.OrderTimingType.OrderTimingTypeDelayed,
                        delay, delay, amount, action.Origin, action.Target);
                    colonist.Route = new List<string>(route.Nodes);
                    orders.Add(colonist);
                    if (route.IsDetour)
                        AITuningLogger.LogRouteDetour(turn, Player.playerID, action.Origin, action.Target,
                            route.Nodes, route.Cost);

                    orders.Add(MakeOrder(GameAI.GameAIOrder.OrderType.OrderTypePopulationChange,
                        GameAI.GameAIOrder.OrderTimingType.OrderTimingTypeImmediate,
                        0, 0, amount * -1.0f, action.Origin, action.Origin));

                    orders.Add(MakeOrder(GameAI.GameAIOrder.OrderType.OrderTypePopulationTransferInProgress,
                        GameAI.GameAIOrder.OrderTimingType.OrderTimingTypeImmediate,
                        0, 0, amount, action.Origin, action.Target));
                                    
                    orders.Add(MakeOrder(GameAI.GameAIOrder.OrderType.OrderTypeRemoveShip,
                        GameAI.GameAIOrder.OrderTimingType.OrderTimingTypeImmediate,
                        0, 0, 1, action.Origin, action.Origin));

                    // A target that cannot feed a colonist gets a food rider as its own order: paid by the origin
                    // now (an existing food change), delivered with the colonist (same delay), and unrelated to the
                    // food shipping system. It bridges the gap until a shortage can be answered; it does not
                    // guarantee survival.
                    if (AIMap.GetPlanet(action.Target).NeedsColonyFoodRider)
                    {
                        var rider = AIMap.GameAIConstants.colonyFoodRider;
                        orders.Add(MakeOrder(GameAI.GameAIOrder.OrderType.OrderTypeColonyFoodRider,
                            GameAI.GameAIOrder.OrderTimingType.OrderTimingTypeDelayed,
                            delay, delay, rider, action.Origin, action.Target));
                        orders.Add(MakeOrder(GameAI.GameAIOrder.OrderType.OrderTypeFoodChange,
                            GameAI.GameAIOrder.OrderTimingType.OrderTimingTypeImmediate,
                            0, 0, rider * -1.0f, action.Origin, action.Origin));
                    }
                }
            }
```

Notes for the implementer: this keeps every existing order and its order exactly as before; only the choice list (via the planner) and the colonist order's `Route` are new. `PlayerAI` needs no new `using` (`RoutePlanner`, `BlockadeView`, `BlockadeSystem` are in `FlatSpace.AI`, and `Gameboard` is already imported through `FlatSpace.Game`). The existing checks `RunColonyFoodRiderCheck` and the colonization checks in `PlayerKnowledgeSelfCheck` must still pass: their view is null, so the planner returns the normal shortest path, and their adjacent planets have `NumNodes == 2`.

- [ ] **Step 6: Verify**

Recompile; run `Run Blockade Avoidance Self-Check`, `Run Player Knowledge Self-Check` (colonization gating and the food-rider check exercise `ProcessColonizers` and `PlanetHasColonizationTarget`), `Run PlayerAI Resource Self-Check` and `Run Ship Transport Self-Check`. Expected: all pass. PENDING if the Editor is unavailable.

- [ ] **Step 7: Commit**

```bash
git add Assets/Flatspace/GameAI/PlayerAI.cs Assets/Flatspace/Diagnostics/AITuningLogger.cs Assets/Editor/BlockadeAvoidanceSelfCheck.cs
git commit -m "feat(blockade): colonization avoids visible blockades and cancels when no route exists

Co-Authored-By: Claude Sonnet 5.5 <noreply@anthropic.com>"
```

---

### Task 5: Production response (colony ships, warship boost, fleet-cap exemption)

**Files:**
- Modify: `Assets/Flatspace/GameAI/GameAIConstants.cs` (`blockadedWarshipBoost`)
- Modify: `Assets/Flatspace/GameAI/PlayerAI.cs` (`GetIndustrySituationalWeightMultiplier`)
- Modify: `Assets/Editor/BlockadeAvoidanceSelfCheck.cs`

**Interfaces:**
- Consumes: `PlayerAI.IsBlockaded` (private), `PlayerAI.CurrentBlockadeView`, `RefreshBlockadeView()` (Task 4); `Scenario` (Task 4); existing `PlayerAI.GetIndustrySituationalWeightMultiplier(CatalogItem, string)`, `ComputeWarshipMultiplier(AIStrategy)`, `Planet.FindWarshipUpdateTarget`.
- Produces: `GameAIConstants.blockadedWarshipBoost` (float, default 3).

- [ ] **Step 1: Add the checks**

Add `ok &= RunProductionResponseCheck();` to `RunChecks()` and add:

```csharp
    private static CatalogItem ProductionEntry(string name, string subType)
    {
        var item = ScriptableObject.CreateInstance<CatalogItem>();
        item.itemName = name; item.name = name; item.type = "Ship"; item.subType = subType; item.cost = 100f;
        return item;
    }

    public static bool RunProductionResponseCheck()
    {
        var ok = true;
        ok &= Check(Near(ScriptableObject.CreateInstance<GameAIConstants>().blockadedWarshipBoost, 3f),
            "blockadedWarshipBoost defaults to 3");

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
                var freshMap = s.Map.GetPlanet("A");
                foreach (var ship in freshMap.DockedShips)
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
```

Hand-walk notes: with 30 own warships at A the Consolidate `WantedWarships()` is at most `warshipsPerColonizedPlanet` (8) x 1 colonized planet = 8, so `wanted x warshipFleetCap` is at most 12 and `have` (30) is beyond it, giving a plain multiplier of 0. In the last block the ships' snapshots were filled to hold every researched name, so `FindWarshipUpdateTarget` returns null for my ships (player 1's blockading ships are another owner). The `s.Research` swap keeps `Dispose` destroying the live list.

- [ ] **Step 2: The constant**

In `GameAIConstants.cs`, after `warshipsPerColonizedPlanet` add:

```csharp

    [Header("Blockade response")]
    // Multiplies the Warship and Update Warship production weights on a planet that is blockaded against its owner (new
    // ships dock where they are built, so building there lifts the blockade). On such a planet the Consolidate fleet-cap
    // cutoff no longer applies: the Warship multiplier is floored at 1 before this boost. Update Warship's existing 0
    // (nothing to upgrade) stays 0.
    public float blockadedWarshipBoost = 3f;
```

- [ ] **Step 3: `GetIndustrySituationalWeightMultiplier`**

In `PlayerAI.cs`:

(a) At the top of the `if (item.subType == "ColonyShip")` block, as its first statement, add:

```csharp
                    if (IsBlockaded(planetName))
                        return 0f;                       // a blockaded planet builds no colony ships (it launches none either)
```

(b) Replace the two trailing blocks (the `WarshipUpdate` early return and the `Warship`/Consolidate block, currently ending with `return 1f;`) with:

```csharp
                var blockaded = IsBlockaded(planetName);
                var blockadeBoost = blockaded ? AIMap.GameAIConstants.blockadedWarshipBoost : 1f;
                if (item.subType == "WarshipUpdate")
                {
                    if (AIMap.GetPlanet(planetName).FindWarshipUpdateTarget(
                            Player.playerID, WarshipStats.ResearchedNames(ResearchCatalog.catalogItems)) == null)
                        return 0f;                       // nothing docked here is missing an improvement — do not offer it
                    return blockadeBoost;
                }
                if (item.subType == "Warship")
                {
                    var shortfall = 1f;                  // Expand has no fleet cap
                    if (Strategy == AIStrategy.AIStrategyConsolidate)
                    {
                        if (_warshipMultiplierThisTurn == null)
                        {
                            _warshipMultiplierThisTurn = ComputeWarshipMultiplier(Strategy, out var wanted, out var have);
                            AITuningLogger.LogWarshipBoost(
                                Gameboard.Instance != null ? Gameboard.Instance.TurnNumber : 0,
                                Player.playerID, wanted, have, _warshipMultiplierThisTurn.Value);
                        }
                        shortfall = _warshipMultiplierThisTurn.Value;   // shortfall boost / surplus taper, 0 at the cap
                    }
                    // A blockaded planet is exempt from the fleet-cap cutoff: floor at 1, then boost.
                    return (blockaded ? Math.Max(shortfall, 1f) : shortfall) * blockadeBoost;
                }
                return 1f;
```

Keep the `Improvement` affordability line between the ColonyShip block and these blocks exactly as it is. Unblockaded behavior is unchanged: Expand Warship returns 1, Consolidate returns the cached multiplier, Update Warship returns 0 or 1, and the existing assertions in `PlayerKnowledgeSelfCheck`/`ShipTransportSelfCheck` (view null, so `blockaded` is false) still hold.

- [ ] **Step 4: Verify**

Recompile; run `Run All AI Self-Checks` (Player Knowledge and Ship Transport exercise the situational weights). Expected: `ALL 6 SUITES PASSED`. PENDING if the Editor is unavailable.

- [ ] **Step 5: Commit**

```bash
git add Assets/Flatspace/GameAI/GameAIConstants.cs Assets/Flatspace/GameAI/PlayerAI.cs Assets/Editor/BlockadeAvoidanceSelfCheck.cs
git commit -m "feat(blockade): blockaded planets stop colony ships and boost warship production

Co-Authored-By: Claude Sonnet 5.5 <noreply@anthropic.com>"
```

---

### Task 6: Documentation

**Files:**
- Modify: `CLAUDE.md`
- Modify: `FUTURE_FEATURES.md`
- Modify (untracked, not committed): `.claude/skills/tuning-log/SKILL.md`

- [ ] **Step 1: `CLAUDE.md`**

(a) In "Tests", add to the AI menu list `FlatSpace → AI → Run Blockade Avoidance Self-Check` (`Assets/Editor/BlockadeAvoidanceSelfCheck.cs`) and change the "Run All" sentence to list Blockade Avoidance among the suites.

(b) In "### Warships and Blockade", append after the Blockade bullet's limitation text a bullet **Blockade avoidance (colonization):** `BlockadeView` (per player, rebuilt each `ProcessResults`; visible = presence planets plus direct neighbours; blockade value > 0; null view = nothing blockaded); `RoutePlanner.PlanRoute` (shortest path when clean; a fresh Dijkstra, no node limit, when an in-range target's shortest path is visibly blocked; null when the target is blockaded, out of range, unreachable or every way round is blocked); colonist orders carry their route (`GameAIOrder.Route`, `OrderSave.route`, older saves fall back to the shortest path) and `BlockadeSystem.RouteFor` applies blockades along it; `ProcessColonizers` skips a blockaded origin and drops targets with no route (the colony ship stays docked and retries); `PlanetHasColonizationTarget` uses the planner (and, as a deliberate tightening, the planner rejects `FindPath`'s 1-node no-route stub that the old `NumNodes <= max` filter accepted); ColonyShip weight is 0 on a blockaded planet; a blockaded planet's Warship weight is `max(multiplier, 1) x blockadedWarshipBoost` (default 3), i.e. exempt from the Consolidate fleet-cap cutoff, and Update Warship gets the same boost (its 0 stays 0). Resource shipping and assault are not blockade-aware yet.

(c) In "### AI Tuning Log", add `RouteDetour|<origin>-><target>|<nodes>|<cost>` and `ColonizeCancelled|<origin>|<BlockadedOrigin|NoRoute>` to the event list, and note that `ColonizeCancelled` repeats every turn the condition holds, like `ColonizerReady`, so its raw count overcounts.

- [ ] **Step 2: `FUTURE_FEATURES.md`**

Under the blockade bullet, mark the colonization part done (rules: blockaded planets build no colony ships and do not colonize; colonization routes avoid visible blockaded planets and cancel when none exists; blockaded planets get a tunable warship/upgrade boost) and list what remains as two items: (2) resource shipping: blockaded planets are not eligible sources, routes avoid visible blockaded planets even beyond the maximum range, and with no clean route ship only if the amount exceeds the total blockade values along the best route (a blockaded target counts), otherwise cancel; (3) assault breaks blockades: target visibly blockaded planets with offense-based force sizing (dock enough offense that the value is 0 or less), include research priority changes for warships, and optionally prioritize blockaded planets that cut a colonization or shipment in the last N turns (tunable, default 5).

- [ ] **Step 3: tuning-log skill (local, untracked)**

In `.claude/skills/tuning-log/SKILL.md`, in the "Fleet cap discipline" bullet, add that planets blockaded against their owner are exempt from the fleet cap (`blockadedWarshipBoost`), so a `WarshipBoost` multiplier of 0 with a `WarShipProduction` start on a blockaded planet is expected, and add a "Blockade" bullet: count `Blockade`/`OrderBlocked`, compare `ColonizeStart` to `ColonizeArrive` per player (all shortfall used to be blockade losses), and read `RouteDetour`/`ColonizeCancelled`. This file is not tracked in git: edit it, do not commit it.

- [ ] **Step 4: Commit**

```bash
git add CLAUDE.md FUTURE_FEATURES.md
git commit -m "docs: document blockade avoidance for colonization

Co-Authored-By: Claude Sonnet 5.5 <noreply@anthropic.com>"
```

---

## Self-review notes

- **Spec coverage:** section 1 `BlockadeView` (Task 1); section 2 `RoutePlanner` (Task 2); section 3 route-carrying orders and saves (Task 3); section 4 AI wiring: colonization, `PlanetHasColonizationTarget`, ColonyShip weight, warship boost and cap exemption, logging (Tasks 4 and 5); section 5 saves (Task 3, `blockadedWarshipBoost` default in Task 5); section 6 self-checks (in every task, suite registered in Run All in Task 1); section 7 docs (Task 6). The spec's rule-table row 12 (research priority for warships) belongs to sub-project 3.
- **Type consistency:** `BlockadeView.Build/Of/IsBlockaded/Value/Blocker/BlockadedNames`, `RoutePlanner.PlanRoute/ShortestPathAvoiding/PlannedRoute{Nodes,Cost,NumNodes,IsDetour}`, `GameAIOrder.Route/RouteFromSave`, `OrderSave.route`, `GameAIMap.EdgeCost`, `BlockadeSystem.RouteFor`, `PlayerAI.RefreshBlockadeView/CurrentBlockadeView/IsBlockaded`, `AITuningLogger.LogRouteDetour/LogColonizeCancelled`, and `GameAIConstants.blockadedWarshipBoost` are named identically in every task that uses them; `Scenario` (Task 4) is reused by Task 5 and `BuildDiamond` (Task 2) by Tasks 3 to 5.
- **Known judgement calls:** `Spawn` gives every planet `_baseFoodProduction = 1` so no colonist needs a food rider; the tie-break of the fresh Dijkstra is by node name; `ColonizeCancelled` logs every turn a colonizer is held.
