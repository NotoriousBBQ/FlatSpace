using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using FlatSpace.AI;

public static class DistributionCenterSelfCheck
{
    private static float _nextPlanetX;

    [MenuItem("FlatSpace/AI/Run Distribution Center Self-Check")]
    public static void Run()
    {
        var ok = RunBoardSizeGatingCheck();
        ok &= RunCoverageMaximizingSelectionCheck();
        ok &= RunTieBreakByDistanceCheck();
        ok &= RunNoReachableCandidateCheck();
        ok &= RunStickySelectionCheck();
        ok &= RunPruningCheck();
        ok &= RunCoverageGapCheck();
        ok &= RunSaveRoundTripCheck();
        Debug.Log(ok
            ? "[DistributionCenterSelfCheck] ALL PASSED"
            : "[DistributionCenterSelfCheck] FAILURES (see errors above)");
    }

    private static bool Check(bool condition, string label)
    {
        if (!condition) Debug.LogError($"[DistributionCenterSelfCheck] FAIL: {label}");
        return condition;
    }

    private static PlanetSpawnData MakeSpawn(string name, int initialPopulation, IEnumerable<string> connections = null)
    {
        var resourceData = ScriptableObject.CreateInstance<PlanetResourceData>();
        resourceData._initialPopulation = initialPopulation;
        resourceData._maxPopulation = 5;

        var spawn = ScriptableObject.CreateInstance<PlanetSpawnData>();
        spawn._planetName = name;
        spawn._planetPosition = new Vector3(_nextPlanetX, 0f, 0f);
        _nextPlanetX += 100f;
        spawn._planetType = Planet.PlanetType.PlanetTypeNormal;
        spawn._resourceData = resourceData;
        spawn._connections = connections != null ? new List<string>(connections) : new List<string>();
        return spawn;
    }

    // NOTE: Planet.Owner is only ever assigned by the private SetPlanetOwnership() (called during a real
    // Planet.UpdatePlanet() simulation tick), which these self-checks never run — it is NOT derived from
    // Population automatically. Every planet built with population here must have its Owner set
    // explicitly, or PlayerAI's Owner-filtered "colonized planets" queries see nothing. This helper does
    // that for every planet MakeSpawn gave a population > 0; a test that needs a planet owned by a
    // DIFFERENT player overrides .Owner afterward (see RunBoardSizeGatingCheck).
    private static (GameAIMap map, PlayerAI playerAI, GameObject mapGo, GameObject playerGo) BuildPlayer(
        List<PlanetSpawnData> spawns, GameAIConstants constants, int playerId, string mapGoName, string playerGoName)
    {
        var mapGo = new GameObject(mapGoName);
        var playerGo = new GameObject(playerGoName);
        var map = mapGo.AddComponent<GameAIMap>();
        map.GameAIMapInit(spawns, constants);
        foreach (var planet in map.PlanetList)
            if (planet.Population.Count > 0)
                planet.Owner = playerId;
        var player = playerGo.AddComponent<Player>();
        var playerAI = playerGo.AddComponent<PlayerAI>();
        playerAI.Player = player;
        playerAI.AIMap = map;
        player.playerID = playerId;
        return (map, playerAI, mapGo, playerGo);
    }

    private static Planet.PlanetUpdateResult FoodSurplus(string name, int playerId, float amount = 10f)
        => new Planet.PlanetUpdateResult(name,
            Planet.PlanetUpdateResult.PlanetUpdateResultType.PlanetUpdateResultTypeFoodSurplus, amount, playerId);

    // A -- Home(producer). B -- Far(candidate, reaches ConsumerA/ConsumerB) that no producer reaches
    // directly. maxPathNodesForResourceDistribution = 2 (direct neighbours only). Far must score higher
    // than Near (which reaches nothing new) and be selected.
    public static bool RunCoverageMaximizingSelectionCheck()
    {
        var ok = true;
        (GameAIMap map, PlayerAI playerAI, GameObject mapGo, GameObject playerGo) built = default;
        try
        {
            var constants = ScriptableObject.CreateInstance<GameAIConstants>();
            constants.defaultTravelSpeed = 1f;
            constants.maxPathNodesForResourceDistribution = 2;
            constants.minPlanetsForDistributionCenters = 0;
            constants.minPlanetsForSecondDistributionCenter = 10000;

            var spawns = new List<PlanetSpawnData>
            {
                MakeSpawn("Home", 1, new[] { "NearA", "FarB" }),
                MakeSpawn("NearA", 1),
                MakeSpawn("FarB", 1, new[] { "Home", "Consumer1", "Consumer2" }),
                MakeSpawn("Consumer1", 1),
                MakeSpawn("Consumer2", 1),
            };
            built = BuildPlayer(spawns, constants, 0, "DCSelfCheckMap1", "DCSelfCheckPlayer1");

            var results = new List<Planet.PlanetUpdateResult> { FoodSurplus("Home", 0) };
            built.playerAI.UpdateDistributionCenters(results, turnNumber: 1);

            ok &= Check(built.playerAI.FoodDistributionCenters.Count == 1
                        && built.playerAI.FoodDistributionCenters[0] == "FarB",
                "FarB (reaches 2 new consumers) is selected over NearA (reaches 0 new consumers)");
        }
        finally
        {
            Object.DestroyImmediate(built.playerGo);
            Object.DestroyImmediate(built.mapGo);
        }
        return ok;
    }

    // Home -- A -- Mid -- ConsumerA (A scores 1, distance 2 from Home).
    // Home -- Relay -- B -- ConsumerB (B scores 1, distance 3 from Home).
    // maxPathNodesForResourceDistribution = 3. Equal score; B must win on "furthest from its producer".
    public static bool RunTieBreakByDistanceCheck()
    {
        var ok = true;
        (GameAIMap map, PlayerAI playerAI, GameObject mapGo, GameObject playerGo) built = default;
        try
        {
            var constants = ScriptableObject.CreateInstance<GameAIConstants>();
            constants.defaultTravelSpeed = 1f;
            constants.maxPathNodesForResourceDistribution = 3;
            constants.minPlanetsForDistributionCenters = 0;
            constants.minPlanetsForSecondDistributionCenter = 10000;

            var spawns = new List<PlanetSpawnData>
            {
                MakeSpawn("Home", 1, new[] { "A", "Relay" }),
                MakeSpawn("A", 1, new[] { "Home", "Mid" }),
                MakeSpawn("Mid", 0, new[] { "A", "ConsumerA" }),
                MakeSpawn("ConsumerA", 1, new[] { "Mid" }),
                MakeSpawn("Relay", 0, new[] { "Home", "B" }),
                MakeSpawn("B", 1, new[] { "Relay", "ConsumerB" }),
                MakeSpawn("ConsumerB", 1, new[] { "B" }),
            };
            built = BuildPlayer(spawns, constants, 0, "DCSelfCheckMap2", "DCSelfCheckPlayer2");

            var results = new List<Planet.PlanetUpdateResult> { FoodSurplus("Home", 0) };
            built.playerAI.UpdateDistributionCenters(results, turnNumber: 1);

            ok &= Check(built.playerAI.FoodDistributionCenters.Count == 1
                        && built.playerAI.FoodDistributionCenters[0] == "B",
                "B (distance 3 from Home) wins the tie-break over A (distance 2), both scoring 1");
        }
        finally
        {
            Object.DestroyImmediate(built.playerGo);
            Object.DestroyImmediate(built.mapGo);
        }
        return ok;
    }

    // A lone, disconnected candidate: no producer reaches it at all. Selection must find nothing.
    public static bool RunNoReachableCandidateCheck()
    {
        var ok = true;
        (GameAIMap map, PlayerAI playerAI, GameObject mapGo, GameObject playerGo) built = default;
        try
        {
            var constants = ScriptableObject.CreateInstance<GameAIConstants>();
            constants.defaultTravelSpeed = 1f;
            constants.maxPathNodesForResourceDistribution = 2;
            constants.minPlanetsForDistributionCenters = 0;
            constants.minPlanetsForSecondDistributionCenter = 10000;

            // HomeNeighbor exists only to force PathingSystem into explicit-connection mode
            // (InitializePathMap's useExplicit check is true the moment ANY planet declares a
            // connection). Without it, two planets with empty Connections lists both fall back to
            // BuildDistanceConnections, which auto-connects any pair within 400 units — and MakeSpawn's
            // 100-unit spacing would silently connect Home to "Isolated" anyway, defeating this test.
            // Neither Home nor HomeNeighbor names "Isolated", so under explicit mode it gets zero edges.
            var spawns = new List<PlanetSpawnData>
            {
                MakeSpawn("Home", 1, new[] { "HomeNeighbor" }),
                MakeSpawn("HomeNeighbor", 0),
                MakeSpawn("Isolated", 1), // no connections at all, and nothing points at it either
            };
            built = BuildPlayer(spawns, constants, 0, "DCSelfCheckMap3", "DCSelfCheckPlayer3");

            var results = new List<Planet.PlanetUpdateResult> { FoodSurplus("Home", 0) };
            built.playerAI.UpdateDistributionCenters(results, turnNumber: 1);

            ok &= Check(built.playerAI.FoodDistributionCenters.Count == 0,
                "no DC is selected when no candidate is reachable from any producer");
        }
        finally
        {
            Object.DestroyImmediate(built.playerGo);
            Object.DestroyImmediate(built.mapGo);
        }
        return ok;
    }

    // Same topology as RunCoverageMaximizingSelectionCheck, but this player's own colonized count (2) is
    // below the whole-board threshold (5) until a SECOND PLAYER's planets push the board-wide total over
    // it — proving the gate counts the whole board, not this player's own territory.
    public static bool RunBoardSizeGatingCheck()
    {
        var ok = true;
        (GameAIMap map, PlayerAI playerAI, GameObject mapGo, GameObject playerGo) built = default;
        (GameAIMap map, PlayerAI playerAI, GameObject mapGo, GameObject playerGo) blocked = default;
        try
        {
            var constants = ScriptableObject.CreateInstance<GameAIConstants>();
            constants.defaultTravelSpeed = 1f;
            constants.maxPathNodesForResourceDistribution = 2;
            constants.minPlanetsForDistributionCenters = 5;
            constants.minPlanetsForSecondDistributionCenter = 10000;

            var spawns = new List<PlanetSpawnData>
            {
                MakeSpawn("Home", 1, new[] { "NearA", "FarB" }),
                MakeSpawn("NearA", 1),
                MakeSpawn("FarB", 1, new[] { "Home", "Consumer1", "Consumer2" }),
                MakeSpawn("Consumer1", 1),
                MakeSpawn("Consumer2", 1),
            };
            // BuildPlayer(..., playerId: 0, ...) sets every populated planet's Owner to 0 first; these
            // three are then reassigned to player 1, so only 2 of the 5 are actually THIS player's own —
            // proving the gate counts the board-wide total (5), not this player's territory (2).
            built = BuildPlayer(spawns, constants, 0, "DCSelfCheckMap4", "DCSelfCheckPlayer4");
            built.map.GetPlanet("FarB").Owner = 1;
            built.map.GetPlanet("Consumer1").Owner = 1;
            built.map.GetPlanet("Consumer2").Owner = 1;

            var results = new List<Planet.PlanetUpdateResult> { FoodSurplus("Home", 0) };
            built.playerAI.UpdateDistributionCenters(results, turnNumber: 1);

            // Otherwise-identical board, gate raised one above the board-wide total (6 > 5), to prove it
            // WOULD have blocked selection at the lower total above.
            var blockedConstants = ScriptableObject.CreateInstance<GameAIConstants>();
            blockedConstants.defaultTravelSpeed = 1f;
            blockedConstants.maxPathNodesForResourceDistribution = 2;
            blockedConstants.minPlanetsForDistributionCenters = 6;
            blockedConstants.minPlanetsForSecondDistributionCenter = 10000;

            var blockedSpawns = new List<PlanetSpawnData>
            {
                MakeSpawn("BHome", 1, new[] { "BNearA", "BFarB" }),
                MakeSpawn("BNearA", 1),
                MakeSpawn("BFarB", 1, new[] { "BHome", "BConsumer1", "BConsumer2" }),
                MakeSpawn("BConsumer1", 1),
                MakeSpawn("BConsumer2", 1),
            };
            blocked = BuildPlayer(blockedSpawns, blockedConstants, 0, "DCSelfCheckMap4b", "DCSelfCheckPlayer4b");
            blocked.map.GetPlanet("BFarB").Owner = 1;
            blocked.map.GetPlanet("BConsumer1").Owner = 1;
            blocked.map.GetPlanet("BConsumer2").Owner = 1;

            var blockedResults = new List<Planet.PlanetUpdateResult> { FoodSurplus("BHome", 0) };
            blocked.playerAI.UpdateDistributionCenters(blockedResults, turnNumber: 1);

            ok &= Check(built.playerAI.FoodDistributionCenters.Count == 1,
                "at exactly the board-wide threshold (5 total colonized, including another player's), a DC is selected");
            ok &= Check(blocked.playerAI.FoodDistributionCenters.Count == 0,
                "one above the board-wide threshold on an otherwise-identical board, no DC is selected");
        }
        finally
        {
            Object.DestroyImmediate(blocked.playerGo);
            Object.DestroyImmediate(blocked.mapGo);
            Object.DestroyImmediate(built.playerGo);
            Object.DestroyImmediate(built.mapGo);
        }
        return ok;
    }

    // Calling UpdateDistributionCenters a second time, after the board has changed in a way that would
    // make a fresh re-score prefer a DIFFERENT candidate, must not replace the first call's choice.
    // (An earlier version of this test used two arms that scored equally from the start, tie-broken by
    // path cost alone — that tie-break is deterministic regardless of stickiness, so it passed even
    // against a non-sticky "clear and re-select every turn" implementation. Consumer3 is added between
    // the two calls specifically to flip which candidate WOULD win a fresh score, so this test actually
    // discriminates sticky from non-sticky.)
    public static bool RunStickySelectionCheck()
    {
        var ok = true;
        (GameAIMap map, PlayerAI playerAI, GameObject mapGo, GameObject playerGo) built = default;
        try
        {
            var constants = ScriptableObject.CreateInstance<GameAIConstants>();
            constants.defaultTravelSpeed = 1f;
            constants.maxPathNodesForResourceDistribution = 2;
            constants.minPlanetsForDistributionCenters = 0;
            constants.minPlanetsForSecondDistributionCenter = 10000;

            // FarB (score 1, cost 100) beats FarC (score 1, cost 300) on the first call. Consumer3
            // starts uncolonized so it doesn't count yet; giving it population before the second call
            // brings FarC's score to 2, which would beat FarB's 1 outright under a fresh re-score.
            var spawns = new List<PlanetSpawnData>
            {
                MakeSpawn("Home", 1, new[] { "FarB", "FarC" }),
                MakeSpawn("FarB", 1, new[] { "Home", "Consumer1" }),
                MakeSpawn("Consumer1", 1),
                MakeSpawn("FarC", 1, new[] { "Home", "Consumer2", "Consumer3" }),
                MakeSpawn("Consumer2", 1),
                MakeSpawn("Consumer3", 0),
            };
            built = BuildPlayer(spawns, constants, 0, "DCSelfCheckMap5", "DCSelfCheckPlayer5");

            var results = new List<Planet.PlanetUpdateResult> { FoodSurplus("Home", 0) };
            built.playerAI.UpdateDistributionCenters(results, turnNumber: 1);
            var firstChoice = built.playerAI.FoodDistributionCenters.Count == 1
                ? built.playerAI.FoodDistributionCenters[0] : null;
            ok &= Check(firstChoice == "FarB", "FarB (cost 100) is selected over FarC (cost 300) on the first call");

            // Owner must be set explicitly (see BuildPlayer's note) — this planet was uncolonized at
            // GameAIMapInit time, so BuildPlayer's own pass over the map never touched it.
            built.map.GetPlanet("Consumer3").Population.Add(new Planet.Inhabitant { Player = 0 });
            built.map.GetPlanet("Consumer3").Owner = 0;

            built.playerAI.UpdateDistributionCenters(results, turnNumber: 2);

            ok &= Check(built.playerAI.FoodDistributionCenters.Count == 1
                        && built.playerAI.FoodDistributionCenters[0] == "FarB",
                "the second call keeps FarB even though FarC would now score higher (2 vs 1) on a fresh re-score");
        }
        finally
        {
            Object.DestroyImmediate(built.playerGo);
            Object.DestroyImmediate(built.mapGo);
        }
        return ok;
    }

    // A designated DC that loses population (captured/dead) must be pruned; if a valid replacement
    // candidate exists, this implementation selects it in the SAME call (a deliberate simplification of
    // the spec's "reselection runs the following turn" — see the plan's note on this task).
    public static bool RunPruningCheck()
    {
        var ok = true;
        (GameAIMap map, PlayerAI playerAI, GameObject mapGo, GameObject playerGo) built = default;
        try
        {
            var constants = ScriptableObject.CreateInstance<GameAIConstants>();
            constants.defaultTravelSpeed = 1f;
            constants.maxPathNodesForResourceDistribution = 2;
            constants.minPlanetsForDistributionCenters = 0;
            constants.minPlanetsForSecondDistributionCenter = 10000;

            var spawns = new List<PlanetSpawnData>
            {
                MakeSpawn("Home", 1, new[] { "FarB" }),
                MakeSpawn("FarB", 1, new[] { "Home", "Consumer1" }),
                MakeSpawn("Consumer1", 1),
            };
            built = BuildPlayer(spawns, constants, 0, "DCSelfCheckMap6", "DCSelfCheckPlayer6");

            var results = new List<Planet.PlanetUpdateResult> { FoodSurplus("Home", 0) };
            built.playerAI.UpdateDistributionCenters(results, turnNumber: 1);
            ok &= Check(built.playerAI.FoodDistributionCenters.Count == 1 && built.playerAI.FoodDistributionCenters[0] == "FarB",
                "FarB is selected initially");

            built.map.GetPlanet("FarB").Population.Clear(); // simulate the DC dying out

            built.playerAI.UpdateDistributionCenters(results, turnNumber: 2);
            ok &= Check(!built.playerAI.FoodDistributionCenters.Contains("FarB"),
                "a DC whose population died out is pruned");
        }
        finally
        {
            Object.DestroyImmediate(built.playerGo);
            Object.DestroyImmediate(built.mapGo);
        }
        return ok;
    }

    public static bool RunCoverageGapCheck()
    {
        var ok = true;
        var mapGo = new GameObject("DCSelfCheckMap7");
        var playerGo = new GameObject("DCSelfCheckPlayer7");
        try
        {
            var constants = ScriptableObject.CreateInstance<GameAIConstants>();
            constants.defaultTravelSpeed = 1f;
            constants.maxPathNodesForResourceDistribution = 2;

            var spawns = new List<PlanetSpawnData>
            {
                MakeSpawn("Home", 1, new[] { "Near" }),
                MakeSpawn("Near", 1, new[] { "Home", "Far" }),
                MakeSpawn("Far", 1, new[] { "Near" }), // 2 hops (NumNodes 3) from Home, out of range 2
            };
            var map = mapGo.AddComponent<GameAIMap>();
            map.GameAIMapInit(spawns, constants);

            var player = playerGo.AddComponent<Player>();
            var playerAI = playerGo.AddComponent<PlayerAI>();
            playerAI.Player = player;
            playerAI.AIMap = map;
            player.playerID = 0;

            var homeAsSource = new List<string> { "Home" };
            var noDCs = new List<string>();

            ok &= Check(!playerAI.IsCoverageGap(map.GetPlanet("Near"), homeAsSource, noDCs),
                "Near (within range of Home) is not a coverage gap");
            ok &= Check(playerAI.IsCoverageGap(map.GetPlanet("Far"), homeAsSource, noDCs),
                "Far (out of range of Home, and no DC) IS a coverage gap");

            var withDCAtNear = new List<string> { "Near" };
            ok &= Check(!playerAI.IsCoverageGap(map.GetPlanet("Far"), homeAsSource, withDCAtNear),
                "Far is no longer a coverage gap once a DC at Near (within Far's range) exists");
        }
        finally
        {
            Object.DestroyImmediate(playerGo);
            Object.DestroyImmediate(mapGo);
        }
        return ok;
    }

    // Simulates the save/restore round trip directly on PlayerAI (SetDistributionCenters is exactly what
    // GameBoard.InitGameFromSave calls), including the older-save case where the field is missing (null).
    public static bool RunSaveRoundTripCheck()
    {
        var ok = true;
        var mapGo = new GameObject("DCSelfCheckMap8");
        var playerGo = new GameObject("DCSelfCheckPlayer8");
        try
        {
            var map = mapGo.AddComponent<GameAIMap>();
            var constants = ScriptableObject.CreateInstance<GameAIConstants>();
            var spawns = new List<PlanetSpawnData> { MakeSpawn("A", 1) };
            map.GameAIMapInit(spawns, constants);

            var player = playerGo.AddComponent<Player>();
            var playerAI = playerGo.AddComponent<PlayerAI>();
            playerAI.Player = player;
            playerAI.AIMap = map;
            player.playerID = 0;

            playerAI.SetDistributionCenters("Food", new List<string> { "A" });
            ok &= Check(playerAI.FoodDistributionCenters.Count == 1 && playerAI.FoodDistributionCenters[0] == "A",
                "SetDistributionCenters restores a saved list");

            playerAI.SetDistributionCenters("Food", null); // simulates an older save's missing field
            ok &= Check(playerAI.FoodDistributionCenters.Count == 0,
                "a null (older-save) list restores as empty, not a crash");

            playerAI.SetDistributionCenters("Grotsits", new List<string> { "A" });
            ok &= Check(playerAI.GrotsitsDistributionCenters.Count == 1
                        && playerAI.FoodDistributionCenters.Count == 0,
                "Food and Grotsits DC lists are independent");
        }
        finally
        {
            Object.DestroyImmediate(playerGo);
            Object.DestroyImmediate(mapGo);
        }
        return ok;
    }
}
