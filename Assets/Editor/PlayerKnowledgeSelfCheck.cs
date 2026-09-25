using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using FlatSpace.AI;
using Flatspace.Objects.Production;

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
        ok &= RunFirstContactChecks();
        ok &= RunConsolidateSwitchCheck();
        ok &= RunConsolidateWeightAliasCheck();
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

    // A - B - C, all Normal planets; A starts populated by player 0.
    private static GameAIMap BuildContactLine(GameObject mapGo)
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
        return map;
    }

    public static bool RunFirstContactChecks()
    {
        var ok = true;

        // No rival anywhere; player 0's own ship on a known planet must not count.
        var go = new GameObject("PKSelfCheckMap_Contact_None");
        try
        {
            var map = BuildContactLine(go);
            map.GetPlanet("B").DockShipFromSave(Ship.ShipKind.WarShip, 0, new List<string>());
            var knowledge = new PlayerKnowledge();
            knowledge.Update(map, numPlayers: 2);
            ok &= Check(!knowledge.HasContact(map, 0), "no rival: own population and own ship are not contact");
            ok &= Check(!knowledge.HasContact(map, 1), "player 1 knows nothing, so it has no contact");
        }
        finally { Object.DestroyImmediate(go); }

        // Rival population on a direct neighbour of A (B is known to player 0).
        go = new GameObject("PKSelfCheckMap_Contact_Population");
        try
        {
            var map = BuildContactLine(go);
            map.GetPlanet("B").Population.Add(new Planet.Inhabitant { Player = 1 });
            var knowledge = new PlayerKnowledge();
            knowledge.Update(map, numPlayers: 2);
            ok &= Check(knowledge.HasContact(map, 0), "rival population on a known neighbour is contact");
            ok &= Check(knowledge.HasContact(map, 1), "contact is mutual: player 1 knows A, which player 0 populates");
        }
        finally { Object.DestroyImmediate(go); }

        // Rival docked ship, no population, on a known neighbour.
        go = new GameObject("PKSelfCheckMap_Contact_Ship");
        try
        {
            var map = BuildContactLine(go);
            map.GetPlanet("B").DockShipFromSave(Ship.ShipKind.WarShip, 1, new List<string>());
            var knowledge = new PlayerKnowledge();
            knowledge.Update(map, numPlayers: 2);
            ok &= Check(knowledge.HasContact(map, 0), "a rival docked ship on a known planet is contact");
        }
        finally { Object.DestroyImmediate(go); }

        // Rival two hops from A: C is not in player 0's known set, and A is not in player 1's.
        go = new GameObject("PKSelfCheckMap_Contact_TwoHops");
        try
        {
            var map = BuildContactLine(go);
            map.GetPlanet("C").Population.Add(new Planet.Inhabitant { Player = 1 });
            var knowledge = new PlayerKnowledge();
            knowledge.Update(map, numPlayers: 2);
            ok &= Check(!knowledge.IsKnown(0, "C"), "precondition: C is two hops from A and unknown to player 0");
            ok &= Check(!knowledge.HasContact(map, 0), "a rival outside the known set is not contact");
            ok &= Check(!knowledge.HasContact(map, 1), "and neither is player 0's A for player 1 (also two hops)");
        }
        finally { Object.DestroyImmediate(go); }

        // Single-player board.
        go = new GameObject("PKSelfCheckMap_Contact_Solo");
        try
        {
            var map = BuildContactLine(go);
            var knowledge = new PlayerKnowledge();
            knowledge.Update(map, numPlayers: 1);
            ok &= Check(!knowledge.HasContact(map, 0), "a lone player never has contact");
        }
        finally { Object.DestroyImmediate(go); }

        return ok;
    }

    public static bool RunConsolidateSwitchCheck()
    {
        var ok = true;
        var mapGo = new GameObject("PKSelfCheckMap_Switch");
        var playerGo = new GameObject("PKSelfCheckPlayer_Switch");
        try
        {
            var map = BuildContactLine(mapGo);
            var player = playerGo.AddComponent<Player>();
            var playerAI = playerGo.AddComponent<PlayerAI>();
            playerAI.Player = player;
            playerAI.AIMap = map;
            player.playerID = 0;

            map.Knowledge.Update(map, numPlayers: 2);
            ok &= Check(playerAI.Strategy == PlayerAI.AIStrategy.AIStrategyExpand,
                "a new PlayerAI starts in Expand");
            ok &= Check(!playerAI.TryEnterConsolidate(1) &&
                        playerAI.Strategy == PlayerAI.AIStrategy.AIStrategyExpand,
                "no contact: stays in Expand");

            var rival = new Planet.Inhabitant { Player = 1 };
            map.GetPlanet("B").Population.Add(rival);
            map.Knowledge.Update(map, numPlayers: 2);
            ok &= Check(playerAI.TryEnterConsolidate(2) &&
                        playerAI.Strategy == PlayerAI.AIStrategy.AIStrategyConsolidate,
                "contact: switches to Consolidate and reports it");

            map.GetPlanet("B").Population.Remove(rival);
            map.Knowledge.Update(map, numPlayers: 2);
            ok &= Check(!playerAI.TryEnterConsolidate(3) &&
                        playerAI.Strategy == PlayerAI.AIStrategy.AIStrategyConsolidate,
                "sticky: stays Consolidate after the rival is gone, and does not report a second switch");

            playerAI.Strategy = PlayerAI.AIStrategy.AIStrategyAmass;
            map.GetPlanet("B").Population.Add(rival);
            map.Knowledge.Update(map, numPlayers: 2);
            ok &= Check(!playerAI.TryEnterConsolidate(4) &&
                        playerAI.Strategy == PlayerAI.AIStrategy.AIStrategyAmass,
                "an Amass player is never switched to Consolidate");
        }
        finally
        {
            Object.DestroyImmediate(playerGo);
            Object.DestroyImmediate(mapGo);
        }
        return ok;
    }

    public static bool RunConsolidateWeightAliasCheck()
    {
        var ok = true;
        var item = ScriptableObject.CreateInstance<CatalogItem>();
        try
        {
            foreach (var subType in new[] { "Food", "Industry", "Grotsits", "Research", "ColonyShip", "Warship" })
            {
                item.subType = subType;
                ok &= Check(
                    PlayerAI.GetResearchWeight(item, PlayerAI.AIStrategy.AIStrategyConsolidate) ==
                    PlayerAI.GetResearchWeight(item, PlayerAI.AIStrategy.AIStrategyExpand),
                    $"Consolidate research weight matches Expand for {subType}");
                ok &= Check(
                    PlayerAI.GetIndustryStrategyWeight(item, PlayerAI.AIStrategy.AIStrategyConsolidate) ==
                    PlayerAI.GetIndustryStrategyWeight(item, PlayerAI.AIStrategy.AIStrategyExpand),
                    $"Consolidate industry weight matches Expand for {subType}");
            }

            // Guard against both sides silently being the neutral default.
            item.subType = "Food";
            ok &= Check(PlayerAI.GetResearchWeight(item, PlayerAI.AIStrategy.AIStrategyConsolidate) == 3.0f,
                "Consolidate Food research weight is Expand's 3.0, not the neutral default");
            ok &= Check(PlayerAI.GetIndustryStrategyWeight(item, PlayerAI.AIStrategy.AIStrategyConsolidate) == 2.5f,
                "Consolidate Food industry weight is Expand's 2.5, not the neutral default");
        }
        finally
        {
            Object.DestroyImmediate(item);
        }
        return ok;
    }
}
