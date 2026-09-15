using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using FlatSpace.AI;

public static class PlayerKnowledgeSelfCheck
{
    private static float _nextPlanetX;

    [MenuItem("FlatSpace/AI/Run Player Knowledge Self-Check")]
    public static void Run()
    {
        var ok = RunGameAIMapSharedQueriesCheck();
        ok &= RunPlayerKnowledgeChecks();
        ok &= RunColonizationKnowledgeGateCheck();
        ok &= RunKnownPlanetsSaveRoundTripCheck();
        Debug.Log(ok
            ? "[PlayerKnowledgeSelfCheck] ALL PASSED"
            : "[PlayerKnowledgeSelfCheck] FAILURES (see errors above)");
    }

    private static bool Check(bool condition, string label)
    {
        if (!condition) Debug.LogError($"[PlayerKnowledgeSelfCheck] FAIL: {label}");
        return condition;
    }

    private static PlanetSpawnData MakeSpawn(string name, int initialPopulation,
        IEnumerable<string> connections = null)
    {
        var resourceData = ScriptableObject.CreateInstance<PlanetResourceData>();
        resourceData._initialPopulation = initialPopulation;
        resourceData._maxPopulation = 5;

        var spawn = ScriptableObject.CreateInstance<PlanetSpawnData>();
        spawn._planetName = name;
        // Distinct, non-zero positions: PathingSystem.FindPath's A* tie-breaking reopens an
        // already-closed node whenever a new score is not strictly worse, which every planet
        // sharing one position triggered constantly (zero-cost edges, zero heuristics) — this
        // either stalls FindPath's own search loop or creates a cycle in a reopened node's
        // parent chain that hangs ConstructPath instead. Strictly increasing X keeps every
        // pairwise distance positive, so no tie is ever hit for these tree-shaped test graphs.
        spawn._planetPosition = new Vector3(_nextPlanetX, 0f, 0f);
        _nextPlanetX += 100f;
        spawn._planetType = Planet.PlanetType.PlanetTypeNormal;
        spawn._resourceData = resourceData;
        spawn._connections = connections != null ? new List<string>(connections) : new List<string>();
        return spawn;
    }

    public static bool RunGameAIMapSharedQueriesCheck()
    {
        var ok = true;
        var mapGo = new GameObject("PKSelfCheckMap_SharedQueries");
        try
        {
            var map = mapGo.AddComponent<GameAIMap>();
            var constants = ScriptableObject.CreateInstance<GameAIConstants>();

            // C and D are deliberately unconnected (below) to exercise GetNeighbours on an
            // isolated planet. GameAIMapInit's PathingSystem.InitializePathMap will log a
            // [PathingSystem] "has no connections" Error for each of them — that diagnostic is
            // correct for a real board (an unreachable planet is usually a mistake) and is
            // expected, harmless noise here, not a self-check failure.
            var spawns = new List<PlanetSpawnData>
            {
                MakeSpawn("A", initialPopulation: 1, connections: new[] { "B" }), // only A declares the link
                MakeSpawn("B", initialPopulation: 0),
                MakeSpawn("C", initialPopulation: 0),
                MakeSpawn("D", initialPopulation: 0),
            };
            map.GameAIMapInit(spawns, constants);
            map.GetPlanet("C").DockShipFromSave(Ship.ShipKind.WarShip, 0, new List<string>());

            var sourcesForPlayer0 = map.GetVisionSourcePlanets(0);
            ok &= Check(sourcesForPlayer0.Exists(s => s.Planet.PlanetName == "A" && s.HasPopulation && !s.HasOwnedShip),
                "A is a population source for player 0");
            ok &= Check(sourcesForPlayer0.Exists(s => s.Planet.PlanetName == "C" && s.HasOwnedShip && !s.HasPopulation),
                "C is a ship source for player 0");
            ok &= Check(!sourcesForPlayer0.Exists(s => s.Planet.PlanetName == "B" || s.Planet.PlanetName == "D"),
                "B and D are not sources for player 0");

            var sourcesForPlayer1 = map.GetVisionSourcePlanets(1);
            ok &= Check(sourcesForPlayer1.Count == 0,
                "player 1 has no sources (A's population and C's ship both belong to player 0)");

            ok &= Check(map.GetNeighbours("A").Contains("B"), "A's declared connection to B is a neighbour");
            ok &= Check(map.GetNeighbours("B").Contains("A"),
                "B is symmetrized as A's neighbour even though B never declared it");
            ok &= Check(map.GetNeighbours("C").Count == 0, "C has no connections");
            ok &= Check(map.GetNeighbours("NoSuchPlanet").Count == 0,
                "unknown planet name returns an empty neighbour list, not null/throw");
        }
        finally
        {
            Object.DestroyImmediate(mapGo);
        }
        return ok;
    }

    public static bool RunPlayerKnowledgeChecks()
    {
        var basics = RunPlayerKnowledgeBasicsCheck();
        var growth = RunPlayerKnowledgeGrowthCheck();
        return basics && growth;
    }

    private static bool RunPlayerKnowledgeBasicsCheck()
    {
        var ok = true;
        var mapGo = new GameObject("PKSelfCheckMap_Knowledge");
        try
        {
            var map = mapGo.AddComponent<GameAIMap>();
            var constants = ScriptableObject.CreateInstance<GameAIConstants>();

            var spawns = new List<PlanetSpawnData>
            {
                MakeSpawn("A", initialPopulation: 1, connections: new[] { "B" }),
                MakeSpawn("B", initialPopulation: 0),
                // C is deliberately unconnected to A or B. GameAIMapInit's PathingSystem will log
                // a [PathingSystem] "has no connections" Error for it — expected, harmless noise
                // here (see the identical note in RunGameAIMapSharedQueriesCheck above), not a
                // self-check failure.
                MakeSpawn("C", initialPopulation: 0),
            };
            map.GameAIMapInit(spawns, constants);

            var knowledge = new PlayerKnowledge();
            knowledge.Update(map, numPlayers: 1);

            ok &= Check(knowledge.IsKnown(0, "A"), "source planet A is known");
            ok &= Check(knowledge.IsKnown(0, "B"), "A's direct neighbour B is known");
            ok &= Check(!knowledge.IsKnown(0, "C"), "C is unconnected and unknown");
            ok &= Check(!knowledge.IsKnown(1, "A"), "player 1 has no sources and knows nothing");

            // Stickiness: A's population disappears (e.g. colonized away), but knowledge persists
            // through the next Update() rather than being recomputed from scratch.
            map.GetPlanet("A").Population.Clear();
            knowledge.Update(map, numPlayers: 1);

            ok &= Check(knowledge.IsKnown(0, "A"), "A stays known after its only source disappears (sticky)");
            ok &= Check(knowledge.IsKnown(0, "B"), "B stays known too, for the same reason");
            ok &= Check(!knowledge.IsKnown(0, "C"), "C is still unknown - nothing ever made it a source or neighbour");
        }
        finally
        {
            Object.DestroyImmediate(mapGo);
        }
        return ok;
    }

    private static bool RunPlayerKnowledgeGrowthCheck()
    {
        var ok = true;
        var mapGo = new GameObject("PKSelfCheckMap_Growth");
        try
        {
            var map = mapGo.AddComponent<GameAIMap>();
            var constants = ScriptableObject.CreateInstance<GameAIConstants>();

            var spawns = new List<PlanetSpawnData>
            {
                MakeSpawn("A", initialPopulation: 1, connections: new[] { "B" }),
                MakeSpawn("B", initialPopulation: 0, connections: new[] { "C" }),
                MakeSpawn("C", initialPopulation: 0),
            };
            map.GameAIMapInit(spawns, constants);

            var knowledge = new PlayerKnowledge();
            knowledge.Update(map, numPlayers: 1);
            ok &= Check(!knowledge.IsKnown(0, "C"),
                "C is two hops from A and unknown while B is not yet a source");

            map.GetPlanet("B").Population.Add(new Planet.Inhabitant { Player = 0 });
            knowledge.Update(map, numPlayers: 1);
            ok &= Check(knowledge.IsKnown(0, "C"),
                "C becomes known once B (its neighbour) becomes a source and Update() runs again");
        }
        finally
        {
            Object.DestroyImmediate(mapGo);
        }
        return ok;
    }

    public static bool RunColonizationKnowledgeGateCheck()
    {
        var ok = true;
        var mapGo = new GameObject("PKSelfCheckMap_Colonization");
        var playerGo = new GameObject("PKSelfCheckPlayer_Colonization");
        try
        {
            var map = mapGo.AddComponent<GameAIMap>();
            var constants = ScriptableObject.CreateInstance<GameAIConstants>();
            constants.defaultTravelSpeed = 1f;
            constants.expandPopulationTrigger = 0.8f;
            constants.maxPathNodesForResourceDistribution = 10;

            var spawns = new List<PlanetSpawnData>
            {
                MakeSpawn("Home", initialPopulation: 1, connections: new[] { "Target" }),
                MakeSpawn("Target", initialPopulation: 0),
            };
            map.GameAIMapInit(spawns, constants);

            var player = playerGo.AddComponent<Player>();
            var playerAI = playerGo.AddComponent<PlayerAI>();
            playerAI.Player = player;
            playerAI.AIMap = map;
            player.playerID = 0;

            var target = map.GetPlanet("Target");

            ok &= Check(!playerAI.IsValidColonizationTarget(target),
                "an undiscovered planet is not a valid colonization target");

            map.Knowledge.Update(map, numPlayers: 1);

            ok &= Check(playerAI.IsValidColonizationTarget(target),
                "the same planet becomes valid once PlayerKnowledge marks it known");
        }
        finally
        {
            Object.DestroyImmediate(playerGo);
            Object.DestroyImmediate(mapGo);
        }
        return ok;
    }

    public static bool RunKnownPlanetsSaveRoundTripCheck()
    {
        var ok = true;
        var knowledge = new PlayerKnowledge();
        knowledge.SetKnownPlanets(0, new List<string> { "A", "B" });

        var savedNames = new List<string>(knowledge.KnownPlanets(0));

        var reloaded = new PlayerKnowledge();
        reloaded.SetKnownPlanets(0, savedNames);

        ok &= Check(reloaded.IsKnown(0, "A") && reloaded.IsKnown(0, "B"),
            "known planets round-trip through KnownPlanets/SetKnownPlanets");
        ok &= Check(!reloaded.IsKnown(0, "C"), "an unlisted planet stays unknown after round trip");
        ok &= Check(reloaded.KnownPlanets(0).Count == 2, "round trip does not add or drop entries");
        return ok;
    }
}
