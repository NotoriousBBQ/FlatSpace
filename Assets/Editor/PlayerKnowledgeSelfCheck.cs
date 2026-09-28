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
        ok &= RunColonizationUsesOwnRangeCheck();
        ok &= RunKnowledgeWideningCheck();
        ok &= RunKnowledgeBfsPassesThroughKnownTerritoryCheck();
        ok &= RunKnownPlanetsSaveRoundTripCheck();
        ok &= RunFirstContactChecks();
        ok &= RunConsolidateSwitchCheck();
        ok &= RunConsolidateWeightTablesCheck();
        ok &= RunColonyShipSituationalCheck();
        ok &= RunColonyFoodRiderCheck();
        Debug.Log(ok
            ? "[PlayerKnowledgeSelfCheck] ALL PASSED"
            : "[PlayerKnowledgeSelfCheck] FAILURES (see errors above)");
        Debug.Log($"[PlayerKnowledgeSelfCheck] float probe (informational, not an assertion): " +
                  $"4 >= 5 * 0.8f is {FloatProbe(4, 5, 0.8f)} " +
                  "(True = the product is single precision; False = this runtime evaluates it wider than float, " +
                  "so a trigger whose product with MaxPopulation is a whole number can be off by one)");
    }

    // The game's readiness test is `Population.Count >= MaxPopulation * expandPopulationTrigger`. Exact-in-decimal
    // intent says 4 of 5 at 0.8 is ready; whether the runtime agrees depends on the precision of the product.
    private static bool FloatProbe(int population, int maxPopulation, float trigger)
        => population >= maxPopulation * trigger;

    private static bool Check(bool condition, string label)
    {
        if (!condition) Debug.LogError($"[PlayerKnowledgeSelfCheck] FAIL: {label}");
        return condition;
    }

    // Test planets feed themselves by default (base food 1). `sterile` = a Desolate-like planet that produces no
    // food at all, which is what makes a colony there need a food rider.
    private static PlanetSpawnData MakeSpawn(string name, int initialPopulation,
        IEnumerable<string> connections = null, bool sterile = false)
    {
        var resourceData = ScriptableObject.CreateInstance<PlanetResourceData>();
        resourceData._initialPopulation = initialPopulation;
        resourceData._maxPopulation = 5;
        resourceData._baseFoodProduction = sterile ? 0f : 1f;

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

    // Colonization must use its OWN range constant, not maxPathNodesForResourceDistribution — a
    // prerequisite for Distribution Centers to ever have territory beyond direct shipping range to serve.
    // Home -- Mid -- Target (2 hops from Home). maxPathNodesForResourceDistribution is set too small to
    // reach Target at all; maxPathNodesForColonization is generous. Knowledge is set directly (bypassing
    // the knowledge gate, which is covered by RunColonizationKnowledgeGateCheck) so only the distance
    // constant is under test.
    public static bool RunColonizationUsesOwnRangeCheck()
    {
        var ok = true;
        var mapGo = new GameObject("PKSelfCheckMap_ColonizationRange");
        var playerGo = new GameObject("PKSelfCheckPlayer_ColonizationRange");
        try
        {
            var map = mapGo.AddComponent<GameAIMap>();
            var constants = ScriptableObject.CreateInstance<GameAIConstants>();
            constants.defaultTravelSpeed = 1f;
            constants.expandPopulationTrigger = 0.1f;
            constants.maxPathNodesForResourceDistribution = 1; // too small to reach even a direct neighbour
            constants.maxPathNodesForColonization = 5;         // generous

            var spawns = new List<PlanetSpawnData>
            {
                MakeSpawn("Home", initialPopulation: 5, connections: new[] { "Mid" }),
                MakeSpawn("Mid", initialPopulation: 0, connections: new[] { "Target" }),
                MakeSpawn("Target", initialPopulation: 0),
            };
            map.GameAIMapInit(spawns, constants);
            map.Knowledge.SetKnownPlanets(0, new List<string> { "Home", "Mid", "Target" });

            // Give Mid a population owned by player 0, which makes it an invalid colonization target
            // (IsValidColonizationTarget rejects planets where PlayerWithMostPopulation() == this player).
            // This leaves Target as the only valid, reachable choice, so ProcessColonizers will colonize it.
            map.GetPlanet("Mid").Population.Add(new Planet.Inhabitant { Player = 0 });

            var player = playerGo.AddComponent<Player>();
            var playerAI = playerGo.AddComponent<PlayerAI>();
            playerAI.Player = player;
            playerAI.AIMap = map;
            player.playerID = 0;

            var results = new List<Planet.PlanetUpdateResult>
            {
                new Planet.PlanetUpdateResult("Home",
                    Planet.PlanetUpdateResult.PlanetUpdateResultType.PlanetUpdateResultTypeColonizerReady,
                    1, playerID: 0),
            };
            var orders = new List<GameAI.GameAIOrder>();
            playerAI.ProcessColonizers(results, orders);

            ok &= Check(orders.Exists(o =>
                    o.Type == GameAI.GameAIOrder.OrderType.OrderTypePopulationTransport &&
                    o.Origin == "Home" && o.Target == "Target"),
                "a target 2 hops away is colonized when maxPathNodesForColonization allows it, even though " +
                "maxPathNodesForResourceDistribution alone would have excluded it");
        }
        finally
        {
            Object.DestroyImmediate(playerGo);
            Object.DestroyImmediate(mapGo);
        }
        return ok;
    }

    // Home -- Mid -- Target (Target is 2 hops / NumNodes 3 from Home). With the default parameter
    // (unwidened, matching today's behavior), Target must stay unknown. Passing a wider
    // maxPathNodesForKnowledge must reveal it. Two fresh PlayerKnowledge instances so the sticky,
    // never-forgets nature of one doesn't leak into the other's assertion.
    public static bool RunKnowledgeWideningCheck()
    {
        var ok = true;
        var mapGo = new GameObject("PKSelfCheckMap_Widening");
        try
        {
            var map = mapGo.AddComponent<GameAIMap>();
            var constants = ScriptableObject.CreateInstance<GameAIConstants>();
            var spawns = new List<PlanetSpawnData>
            {
                MakeSpawn("Home", initialPopulation: 1, connections: new[] { "Mid" }),
                MakeSpawn("Mid", initialPopulation: 0, connections: new[] { "Target" }),
                MakeSpawn("Target", initialPopulation: 0),
            };
            map.GameAIMapInit(spawns, constants);

            var narrow = new PlayerKnowledge();
            narrow.Update(map, numPlayers: 1); // default maxPathNodesForKnowledge: 2
            ok &= Check(narrow.IsKnown(0, "Mid"), "a direct neighbour is still known with the default (2)");
            ok &= Check(!narrow.IsKnown(0, "Target"),
                "a planet 2 hops out stays unknown with the default (2), matching today's behavior");

            var wide = new PlayerKnowledge();
            wide.Update(map, numPlayers: 1, maxPathNodesForKnowledge: 3);
            ok &= Check(wide.IsKnown(0, "Target"),
                "the same planet becomes known once maxPathNodesForKnowledge widens to 3");
        }
        finally
        {
            Object.DestroyImmediate(mapGo);
        }
        return ok;
    }

    // A second vision source's BFS must be able to pass THROUGH planets already known (from an earlier
    // call or a different source) to reach genuinely new territory beyond them. H-P1-P2-...-P8, a straight
    // chain. The first call (source H only) reveals up to P5 (5 hops). Colonizing P3 makes it a second
    // source; its own 5-hop BFS must reach P8 (3 hops further out than P5), passing through the
    // already-known P2/P4 on the way — a version that only expands the frontier through NEWLY-discovered
    // planets would stop dead at P2/P4 and never reach P6-P8.
    public static bool RunKnowledgeBfsPassesThroughKnownTerritoryCheck()
    {
        var ok = true;
        var mapGo = new GameObject("PKSelfCheckMap_BFSFrontier");
        try
        {
            var map = mapGo.AddComponent<GameAIMap>();
            var constants = ScriptableObject.CreateInstance<GameAIConstants>();
            var spawns = new List<PlanetSpawnData>
            {
                MakeSpawn("H", initialPopulation: 1, connections: new[] { "P1" }),
                MakeSpawn("P1", initialPopulation: 0, connections: new[] { "P2" }),
                MakeSpawn("P2", initialPopulation: 0, connections: new[] { "P3" }),
                MakeSpawn("P3", initialPopulation: 0, connections: new[] { "P4" }),
                MakeSpawn("P4", initialPopulation: 0, connections: new[] { "P5" }),
                MakeSpawn("P5", initialPopulation: 0, connections: new[] { "P6" }),
                MakeSpawn("P6", initialPopulation: 0, connections: new[] { "P7" }),
                MakeSpawn("P7", initialPopulation: 0, connections: new[] { "P8" }),
                MakeSpawn("P8", initialPopulation: 0),
            };
            map.GameAIMapInit(spawns, constants);

            var knowledge = new PlayerKnowledge();
            knowledge.Update(map, numPlayers: 1, maxPathNodesForKnowledge: 6); // hops=5, reveals H..P5

            ok &= Check(knowledge.IsKnown(0, "P5") && !knowledge.IsKnown(0, "P6"),
                "first call from H alone reveals exactly 5 hops out (through P5, not P6)");

            // Colonize P3 (a second vision source) and update again with the SAME sticky instance.
            map.GetPlanet("P3").Population.Add(new Planet.Inhabitant { Player = 0 });
            knowledge.Update(map, numPlayers: 1, maxPathNodesForKnowledge: 6);

            ok &= Check(knowledge.IsKnown(0, "P8"),
                "a second source's BFS must pass THROUGH already-known planets (P2, P4) to reach " +
                "genuinely new territory (P6, P7, P8) up to 5 hops from P3");
        }
        finally
        {
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

    // Research and industry: Consolidate has its own hand-tuned table for each (Expand's tables are unchanged).
    // A missing strategy key silently falls back to a neutral 1.0, so every value is asserted explicitly, which
    // also catches an accidental edit of any of the four tables.
    public static bool RunConsolidateWeightTablesCheck()
    {
        var ok = true;
        var expandResearch = new Dictionary<string, float>
        {
            { "Food", 3.0f }, { "Industry", 2.0f }, { "Grotsits", 1.0f },
            { "Research", 1.0f }, { "ColonyShip", 2.5f }, { "Warship", 1.5f },
        };
        var consolidateResearch = new Dictionary<string, float>
        {
            { "Food", 1.0f }, { "Industry", 3.0f }, { "Grotsits", 2.0f },
            { "Research", 1.0f }, { "ColonyShip", 0.5f }, { "Warship", 2.5f },
        };
        var expandIndustry = new Dictionary<string, float>
        {
            { "Food", 2.5f }, { "Industry", 1.5f }, { "Grotsits", 1.0f },
            { "Research", 1.0f }, { "ColonyShip", 2.5f }, { "Warship", 1.5f },
        };
        var consolidateIndustry = new Dictionary<string, float>
        {
            { "Food", 1.0f }, { "Industry", 2.0f }, { "Grotsits", 1.5f },
            { "Research", 1.0f }, { "ColonyShip", 1.0f }, { "Warship", 2.5f },
        };

        var item = ScriptableObject.CreateInstance<CatalogItem>();
        try
        {
            foreach (var subType in new[] { "Food", "Industry", "Grotsits", "Research", "ColonyShip", "Warship" })
            {
                item.subType = subType;
                ok &= Check(
                    PlayerAI.GetResearchWeight(item, PlayerAI.AIStrategy.AIStrategyExpand) == expandResearch[subType],
                    $"Expand research weight for {subType} is {expandResearch[subType]}");
                ok &= Check(
                    PlayerAI.GetResearchWeight(item, PlayerAI.AIStrategy.AIStrategyConsolidate) == consolidateResearch[subType],
                    $"Consolidate research weight for {subType} is {consolidateResearch[subType]}");
                ok &= Check(
                    PlayerAI.GetIndustryStrategyWeight(item, PlayerAI.AIStrategy.AIStrategyExpand) == expandIndustry[subType],
                    $"Expand industry weight for {subType} is {expandIndustry[subType]}");
                ok &= Check(
                    PlayerAI.GetIndustryStrategyWeight(item, PlayerAI.AIStrategy.AIStrategyConsolidate) == consolidateIndustry[subType],
                    $"Consolidate industry weight for {subType} is {consolidateIndustry[subType]}");
            }
        }
        finally
        {
            Object.DestroyImmediate(item);
        }
        return ok;
    }

    // The ColonyShip production multiplier: 0 when a colony ship is docked or nothing is left to colonize (so a
    // planet stops building useless colony ships), x2 when the planet is ready to colonize, and, under Consolidate
    // only, x2 while targets remain even if the planet is not ready yet (Expand's weight is not doubled).
    public static bool RunColonyShipSituationalCheck()
    {
        var ok = true;
        var item = ScriptableObject.CreateInstance<CatalogItem>();
        item.subType = "ColonyShip";
        try
        {
            // Home (max population 5, trigger 0.8 -> ready at 4) - Target (empty, known once Knowledge updates).
            BuildColonyScenario(out var mapGo, out var playerGo, out var map, out var ai, knowTarget: true);
            try
            {
                var home = map.GetPlanet("Home"); var target = map.GetPlanet("Target");
                var expand = PlayerAI.AIStrategy.AIStrategyExpand;
                var consolidate = PlayerAI.AIStrategy.AIStrategyConsolidate;

                ai.Strategy = expand;
                ok &= Check(ai.GetIndustrySituationalWeightMultiplier(item, "Home") == 1f,
                    "Expand, target remains, planet not ready (pop 1 of 5): multiplier 1, unchanged");
                ai.Strategy = consolidate;
                ok &= Check(ai.GetIndustrySituationalWeightMultiplier(item, "Home") == 2f,
                    "Consolidate, target remains, planet not ready: x2 while targets remain");

                // Ready = population at or above 5 x 0.8. Use a full planet (5 of 5) rather than exactly 4, which sits
                // on the float boundary of 5 x 0.8f and made this assertion depend on rounding.
                for (var i = 0; i < 4; i++) home.Population.Add(new Planet.Inhabitant { Player = 0 });
                ai.Strategy = expand;
                var readyMultiplier = ai.GetIndustrySituationalWeightMultiplier(item, "Home");
                ok &= Check(readyMultiplier == 2f,
                    $"Expand, planet ready to colonize with a target: x2 (unchanged); got {readyMultiplier} " +
                    $"(pop {home.Population.Count} of {home.MaxPopulation}, owner {home.Owner})");

                home.DockShipFromSave(Ship.ShipKind.ColonyShip, 0, new List<string>());
                ok &= Check(ai.GetIndustrySituationalWeightMultiplier(item, "Home") == 0f,
                    "Expand, a colony ship already docked: 0");
                ai.Strategy = consolidate;
                ok &= Check(ai.GetIndustrySituationalWeightMultiplier(item, "Home") == 0f,
                    "Consolidate, a colony ship already docked: 0");
                home.UndockShip(Ship.ShipKind.ColonyShip);

                for (var i = 0; i < 5; i++) target.Population.Add(new Planet.Inhabitant { Player = 0 });   // full: invalid target
                ai.Strategy = expand;
                ok &= Check(ai.GetIndustrySituationalWeightMultiplier(item, "Home") == 0f,
                    "Expand, no valid target left (target is full): 0, so no useless colony ships");
                ai.Strategy = consolidate;
                ok &= Check(ai.GetIndustrySituationalWeightMultiplier(item, "Home") == 0f,
                    "Consolidate, no valid target left: 0 as well");

                item.subType = "Food";
                ok &= Check(ai.GetIndustrySituationalWeightMultiplier(item, "Home") == 1f,
                    "another subType is unaffected by the colony ship rule");
                item.subType = "ColonyShip";
            }
            finally
            {
                Object.DestroyImmediate(playerGo);
                Object.DestroyImmediate(mapGo);
            }

            // A target the player does not know about yet is not a target: nothing to colonize.
            BuildColonyScenario(out mapGo, out playerGo, out map, out ai, knowTarget: false);
            try
            {
                ai.Strategy = PlayerAI.AIStrategy.AIStrategyConsolidate;
                ok &= Check(ai.GetIndustrySituationalWeightMultiplier(item, "Home") == 0f,
                    "an undiscovered target does not count: 0");
            }
            finally
            {
                Object.DestroyImmediate(playerGo);
                Object.DestroyImmediate(mapGo);
            }
        }
        finally
        {
            Object.DestroyImmediate(item);
        }
        return ok;
    }

    private static void BuildColonyScenario(out GameObject mapGo, out GameObject playerGo, out GameAIMap map,
        out PlayerAI ai, bool knowTarget, bool sterileTarget = false)
    {
        _nextPlanetX = 0f;
        mapGo = new GameObject("PKSelfCheckMap_ColonyShip");
        playerGo = new GameObject("PKSelfCheckPlayer_ColonyShip");
        map = mapGo.AddComponent<GameAIMap>();
        var constants = ScriptableObject.CreateInstance<GameAIConstants>();
        constants.defaultTravelSpeed = 1f;
        constants.expandPopulationTrigger = 0.8f;
        constants.maxPathNodesForResourceDistribution = 10;
        map.GameAIMapInit(new List<PlanetSpawnData>
        {
            MakeSpawn("Home", initialPopulation: 1, connections: new[] { "Target" }),
            MakeSpawn("Target", initialPopulation: 0, sterile: sterileTarget),
        }, constants);
        map.GetPlanet("Home").Owner = 0;

        var player = playerGo.AddComponent<Player>();
        ai = playerGo.AddComponent<PlayerAI>();
        ai.Player = player;
        ai.AIMap = map;
        player.playerID = 0;
        if (knowTarget) map.Knowledge.Update(map, numPlayers: 1);
    }

    // A colony on a planet that produces no food (Desolate) starves the turn it lands. The colony ship therefore
    // carries a food rider as its OWN order (not part of the food shipping system): the origin pays it, the target
    // receives it with the colonist, and a target is only viable from an origin that can afford it. Colonies are
    // still allowed to fail afterwards (nothing here guarantees survival).
    public static bool RunColonyFoodRiderCheck()
    {
        var ok = true;
        var item = ScriptableObject.CreateInstance<CatalogItem>();
        item.subType = "ColonyShip";
        try
        {
            BuildColonyScenario(out var mapGo, out var playerGo, out var map, out var ai,
                knowTarget: true, sterileTarget: true);
            try
            {
                var home = map.GetPlanet("Home"); var target = map.GetPlanet("Target");
                for (var i = 0; i < 4; i++) home.Population.Add(new Planet.Inhabitant { Player = 0 });   // 5 of 5: ready
                ai.Strategy = PlayerAI.AIStrategy.AIStrategyConsolidate;

                ok &= Check(target.NeedsColonyFoodRider, "a planet that produces no food needs a food rider");
                ok &= Check(!home.NeedsColonyFoodRider, "a planet with base food does not");

                home.Food = 5f;
                ok &= Check(!ai.CanSupportColony(home, target), "an origin with 5 food cannot afford the 10-food rider");
                ok &= Check(ai.GetIndustrySituationalWeightMultiplier(item, "Home") == 0f,
                    "so no viable target is in reach and the planet stops building colony ships");
                home.Food = 10f;
                ok &= Check(ai.CanSupportColony(home, target), "an origin with exactly 10 food can afford it");
                ok &= Check(ai.GetIndustrySituationalWeightMultiplier(item, "Home") == 2f,
                    "and the target is viable again: colony ships are wanted");
                ok &= Check(ai.CanSupportColony(target, home),
                    "a target that can feed itself needs no rider, whatever the origin holds");

                // Orders: the usual four, plus a delayed rider that lands with the colonist and an immediate payment.
                var results = new List<Planet.PlanetUpdateResult>
                {
                    new Planet.PlanetUpdateResult("Home",
                        Planet.PlanetUpdateResult.PlanetUpdateResultType.PlanetUpdateResultTypeColonizerReady, 1, 0),
                };
                var orders = new List<GameAI.GameAIOrder>();
                ai.ProcessColonizers(results, orders);

                var colonist = orders.Find(o => o.Type == GameAI.GameAIOrder.OrderType.OrderTypePopulationTransport);
                var rider = orders.Find(o => o.Type == GameAI.GameAIOrder.OrderType.OrderTypeColonyFoodRider);
                ok &= Check(colonist != null, "the colony ship still sends its colonist");
                ok &= Check(rider != null && rider.Origin == "Home" && rider.Target == "Target"
                            && rider.TimingType == GameAI.GameAIOrder.OrderTimingType.OrderTimingTypeDelayed
                            && System.Convert.ToSingle(rider.Data) == 10f,
                    "a separate delayed order carries the 10-food rider from Home to Target");
                ok &= Check(rider != null && colonist != null && rider.TimingDelay == colonist.TimingDelay
                            && rider.TotalDelay == colonist.TotalDelay,
                    "the rider takes exactly as long as the colonist, so they land together");
                var payment = orders.Find(o => o.Type == GameAI.GameAIOrder.OrderType.OrderTypeFoodChange
                                               && o.Origin == "Home");
                ok &= Check(payment != null && payment.TimingType == GameAI.GameAIOrder.OrderTimingType.OrderTimingTypeImmediate
                            && System.Convert.ToSingle(payment.Data) == -10f,
                    "the origin pays the rider immediately (an existing food change of -10, not a food shipment)");
                ok &= Check(!orders.Exists(o => o.Type == GameAI.GameAIOrder.OrderType.OrderTypeFoodTransport),
                    "the food shipping order type is not involved");

                // Arrival adds the rider to the target's food.
                target.Food = 0f;
                GameAI.ApplyColonyFoodRider(target, rider);
                ok &= Check(target.Food == 10f, "arrival adds the rider to the target's food");
            }
            finally
            {
                Object.DestroyImmediate(playerGo);
                Object.DestroyImmediate(mapGo);
            }

            // A target that feeds itself gets no rider at all.
            BuildColonyScenario(out mapGo, out playerGo, out map, out ai, knowTarget: true, sterileTarget: false);
            try
            {
                var home = map.GetPlanet("Home");
                for (var i = 0; i < 4; i++) home.Population.Add(new Planet.Inhabitant { Player = 0 });
                home.Food = 50f;
                var orders = new List<GameAI.GameAIOrder>();
                ai.ProcessColonizers(new List<Planet.PlanetUpdateResult>
                {
                    new Planet.PlanetUpdateResult("Home",
                        Planet.PlanetUpdateResult.PlanetUpdateResultType.PlanetUpdateResultTypeColonizerReady, 1, 0),
                }, orders);
                ok &= Check(orders.Exists(o => o.Type == GameAI.GameAIOrder.OrderType.OrderTypePopulationTransport),
                    "a normal target is colonized as before");
                ok &= Check(!orders.Exists(o => o.Type == GameAI.GameAIOrder.OrderType.OrderTypeColonyFoodRider),
                    "and needs no rider");
                ok &= Check(!orders.Exists(o => o.Type == GameAI.GameAIOrder.OrderType.OrderTypeFoodChange),
                    "and the origin pays nothing");
            }
            finally
            {
                Object.DestroyImmediate(playerGo);
                Object.DestroyImmediate(mapGo);
            }
        }
        finally
        {
            Object.DestroyImmediate(item);
        }
        return ok;
    }
}
