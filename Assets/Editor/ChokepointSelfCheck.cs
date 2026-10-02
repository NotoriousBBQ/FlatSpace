using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using FlatSpace.AI;
using Flatspace.Objects.Production;

public static class ChokepointSelfCheck
{
    [MenuItem("FlatSpace/AI/Run Chokepoint Self-Check")]
    public static void Run() => RunChecks();

    public static bool RunChecks()
    {
        var ok = RunCentralityCheck();
        ok &= RunMapCentralityCheck();
        ok &= RunGarrisonCheck();
        ok &= RunSummaryAndFormatCheck();
        Debug.Log(ok
            ? "[ChokepointSelfCheck] ALL PASSED"
            : "[ChokepointSelfCheck] FAILURES (see errors above)");
        return ok;
    }

    private static bool Check(bool condition, string label)
    {
        if (!condition) Debug.LogError($"[ChokepointSelfCheck] FAIL: {label}");
        return condition;
    }

    private static bool Near(float a, float b) => Mathf.Abs(a - b) < 0.001f;

    private static IReadOnlyList<string> P(params string[] nodes) => nodes;

    // Every planet needs a distinct position (PathingSystem.FindPath tie-break fragility).
    public static PlanetSpawnData Spawn(string name, float x, float y, IEnumerable<string> connections = null)
    {
        var resourceData = ScriptableObject.CreateInstance<PlanetResourceData>();
        resourceData._initialPopulation = 0;
        resourceData._maxPopulation = 5;
        resourceData._baseFoodProduction = 1f;

        var spawn = ScriptableObject.CreateInstance<PlanetSpawnData>();
        spawn._planetName = name;
        spawn._planetPosition = new Vector3(x, y, 0f);
        spawn._planetType = Planet.PlanetType.PlanetTypeNormal;
        spawn._resourceData = resourceData;
        spawn._connections = connections != null ? new List<string>(connections) : new List<string>();
        return spawn;
    }

    // A(0,0) - H(100,0); H - X1, X2, X3; A - Y(0,80). A tree, so every shortest path is unique:
    // H is on 9 paths (betweenness 9, percentile 1), A on 4 (0.8), the other four planets are leaves (0).
    public static List<PlanetSpawnData> HubSpawns() => new List<PlanetSpawnData>
    {
        Spawn("A", 0f, 0f, new[] { "H", "Y" }),
        Spawn("H", 100f, 0f, new[] { "A", "X1", "X2", "X3" }),
        Spawn("X1", 200f, 0f, new[] { "H" }),
        Spawn("X2", 100f, -100f, new[] { "H" }),
        Spawn("X3", 100f, 100f, new[] { "H" }),
        Spawn("Y", 0f, 80f, new[] { "A" }),
    };

    public static GameAIConstants Constants(float chokepointPercentile = 0.9f)
    {
        var c = ScriptableObject.CreateInstance<GameAIConstants>();
        c.defaultTravelSpeed = 1f;
        c.garrisonHighTraffic = 2;
        c.chokepointPercentile = chokepointPercentile;
        return c;
    }

    // Interior nodes of each path score; endpoints do not. Percentile = strictly lower / (N - 1).
    public static bool RunCentralityCheck()
    {
        var ok = true;

        // Line A-B-C-D, one path per pair.
        var line = new[]
        {
            P("A", "B"), P("A", "B", "C"), P("A", "B", "C", "D"), P("B", "C"), P("B", "C", "D"), P("C", "D"),
        };
        var lineScore = PlanetCentrality.Compute(new[] { "A", "B", "C", "D" }, line);
        ok &= Check(lineScore.Betweenness("B") == 2 && lineScore.Betweenness("C") == 2
                    && lineScore.Betweenness("A") == 0 && lineScore.Betweenness("D") == 0,
            "line: the two interior planets are on 2 paths each, the ends on none");
        ok &= Check(Near(lineScore.Percentile("B"), 2f / 3f) && Near(lineScore.Percentile("C"), 2f / 3f),
            "line: tied betweenness shares one percentile (2 lower of 3 others)");
        ok &= Check(Near(lineScore.Percentile("A"), 0f), "line: an end scores 0");

        // Star: hub H with leaves L1..L3.
        var star = new[]
        {
            P("H", "L1"), P("H", "L2"), P("H", "L3"),
            P("L1", "H", "L2"), P("L1", "H", "L3"), P("L2", "H", "L3"),
        };
        var starScore = PlanetCentrality.Compute(new[] { "H", "L1", "L2", "L3" }, star);
        ok &= Check(starScore.Betweenness("H") == 3 && Near(starScore.Percentile("H"), 1f),
            "star: the hub is on 3 paths and is the top percentile (1)");
        ok &= Check(starScore.Top(1).First().name == "H", "Top(1) is the hub");
        ok &= Check(starScore.Top(10).Count() == 4, "Top(count) is capped at the planets that exist");

        // Review focus 1: a 3-planet chain makes the middle planet the top percentile.
        var tiny = PlanetCentrality.Compute(new[] { "A", "B", "C" }, new[] { P("A", "B"), P("A", "B", "C"), P("B", "C") });
        ok &= Check(tiny.Betweenness("B") == 1 && Near(tiny.Percentile("B"), 1f),
            "a 3-planet chain: the middle planet is the top percentile (accepted on tiny boards)");

        // Review focus 2: the 1-node no-route stub, a 2-node path and a path naming an unknown planet add nothing.
        var odd = PlanetCentrality.Compute(new[] { "A", "B" },
            new[] { P("A"), P("A", "B"), P("A", "Nowhere", "B") });
        ok &= Check(odd.Betweenness("A") == 0 && odd.Betweenness("B") == 0 && odd.Betweenness("Nowhere") == 0,
            "a stub, a 2-node path and an unknown interior planet add nothing and do not throw");
        ok &= Check(Near(odd.Percentile("A"), 0f) && Near(odd.Percentile("B"), 0f),
            "everything tied at 0: every percentile is 0, so there are no chokepoints");

        // Review focus 3: the order of the stored paths does not change the result.
        var shuffled = star.Reverse().ToList();
        var shuffledScore = PlanetCentrality.Compute(new[] { "L3", "L2", "L1", "H" }, shuffled);
        ok &= Check(shuffledScore.Betweenness("H") == starScore.Betweenness("H")
                    && Near(shuffledScore.Percentile("L1"), starScore.Percentile("L1"))
                    && Near(shuffledScore.Percentile("H"), starScore.Percentile("H")),
            "reversed path list and planet order give the same scores");

        // Review focus 5 (pure side): unknown names score 0.
        ok &= Check(starScore.Betweenness("Nowhere") == 0 && Near(starScore.Percentile("Nowhere"), 0f),
            "an unknown planet name scores 0");

        // Empty and single-planet boards.
        var none = PlanetCentrality.Compute(new string[0], new List<IReadOnlyList<string>>());
        ok &= Check(!none.Top(5).Any(), "no planets: nothing to rank");
        var one = PlanetCentrality.Compute(new[] { "A" }, new List<IReadOnlyList<string>>());
        ok &= Check(Near(one.Percentile("A"), 0f), "one planet: percentile 0 (no division by zero)");
        return ok;
    }

    private static Planet Colonize(GameAIMap map, string name, int player = 0)
    {
        var planet = map.GetPlanet(name);
        planet.Owner = player;
        planet.Population.Add(new Planet.Inhabitant { Player = player });
        return planet;
    }

    // Category 4 is "is a chokepoint" (not a neighbour count); under Consolidate a colonized chokepoint keeps a garrison.
    // Hub layout, all six planets colonized by player 0 (so none is outer): H is the chokepoint (percentile 1), A is 0.8.
    public static bool RunGarrisonCheck()
    {
        var ok = true;

        foreach (var percentile in new[] { 0.9f, 0.5f, 2f })
        {
            var go = new GameObject("ChokepointSelfCheckMap_Garrison");
            var constants = Constants(percentile);
            try
            {
                var map = go.AddComponent<GameAIMap>();
                map.GameAIMapInit(HubSpawns(), constants);
                foreach (var n in new[] { "A", "H", "X1", "X2", "X3", "Y" }) Colonize(map, n);
                var expand = new ShipTransportPlanner(map, 0);
                var consolidate = new ShipTransportPlanner(map, 0, PlayerAI.AIStrategy.AIStrategyConsolidate);
                Planet P(string n) => map.GetPlanet(n);

                if (percentile == 0.9f)
                {
                    ok &= Check(expand.Category(P("H")) == 4 && expand.Garrison(P("H")) == 2,
                        "0.9: H (4 neighbours and the top chokepoint) is category 4 with garrison 2");
                    ok &= Check(expand.Category(P("A")) == ShipTransportPlanner.NoCategory && expand.Garrison(P("A")) == 0,
                        "0.9: A (percentile 0.8) is not a chokepoint: no category");
                    ok &= Check(expand.Category(P("X1")) == ShipTransportPlanner.NoCategory, "0.9: a leaf has no category");

                    ok &= Check(consolidate.MaintainsGarrison(P("H")),
                        "0.9 Consolidate: a colonized, non-outer chokepoint maintains a garrison");
                    ok &= Check(!consolidate.MaintainsGarrison(P("A")) && !consolidate.MaintainsGarrison(P("X1")),
                        "0.9 Consolidate: a non-outer non-chokepoint does not");
                    var states = consolidate.BuildStates();
                    var h = states.Find(s => s.Planet == P("H"));
                    var a = states.Find(s => s.Planet == P("A"));
                    ok &= Check(consolidate.LastRound == 1 && h.Garrison == 2 && h.RoundGarrison == 2 && h.Category == 4,
                        "0.9 Consolidate: H garrisons at round 1 with garrisonHighTraffic");
                    ok &= Check(a.Garrison == 0 && a.Category == ShipTransportPlanner.NoCategory,
                        "0.9 Consolidate: A is spare-only (no garrison, no category)");

                    // Expand with every planet colonized: the garrison round logic is unchanged for a chokepoint.
                    ok &= Check(expand.BuildStates().Find(s => s.Planet == P("H")).Garrison == 2,
                        "0.9 Expand: H garrisons through the ordinary category garrison");
                }
                else if (percentile == 0.5f)
                {
                    // A has only 2 neighbours (below the old threshold of 4) yet is category 4: it is the percentile, not the count.
                    ok &= Check(map.GetNeighbours("A").Count == 2 && expand.Category(P("A")) == 4,
                        "0.5: A has 2 neighbours but is category 4 (the test is centrality, not connection count)");
                    ok &= Check(consolidate.MaintainsGarrison(P("A")), "0.5 Consolidate: A now garrisons too");
                    ok &= Check(expand.Category(P("X1")) == ShipTransportPlanner.NoCategory,
                        "0.5: a leaf (betweenness 0) never qualifies");
                }
                else
                {
                    ok &= Check(expand.Category(P("H")) == ShipTransportPlanner.NoCategory
                                && !consolidate.MaintainsGarrison(P("H")),
                        "2: a percentile above 1 switches it off: no category and no Consolidate garrison (review focus 4)");
                }
            }
            finally
            {
                Object.DestroyImmediate(go);
                Object.DestroyImmediate(constants);
            }
        }

        // An outer chokepoint is unchanged: still outer, category 2 wins as the lower number, garrison is the larger.
        var go2 = new GameObject("ChokepointSelfCheckMap_GarrisonOuter");
        var constants2 = Constants();
        constants2.garrisonOuter = 6;
        try
        {
            var map = go2.AddComponent<GameAIMap>();
            map.GameAIMapInit(HubSpawns(), constants2);
            foreach (var n in new[] { "A", "H", "X1", "X2", "Y" }) Colonize(map, n);   // X3 stays empty: H is outer
            var planner = new ShipTransportPlanner(map, 0);
            ok &= Check(planner.IsOuter(map.GetPlanet("H")) && planner.Category(map.GetPlanet("H")) == 2
                        && planner.Garrison(map.GetPlanet("H")) == 6,
                "an outer chokepoint keeps category 2 and the larger (outer) garrison");
        }
        finally
        {
            Object.DestroyImmediate(go2);
            Object.DestroyImmediate(constants2);
        }
        return ok;
    }

    // The numbers behind the ChokepointGarrison line, and the Chokepoints line's field format.
    public static bool RunSummaryAndFormatCheck()
    {
        var ok = true;
        var go = new GameObject("ChokepointSelfCheckMap_Summary");
        var constants = Constants();
        try
        {
            var map = go.AddComponent<GameAIMap>();
            map.GameAIMapInit(HubSpawns(), constants);
            Colonize(map, "H"); Colonize(map, "A");
            for (var i = 0; i < 2; i++) map.GetPlanet("H").DockShipFromSave(Ship.ShipKind.WarShip, 0, new List<string>());
            for (var i = 0; i < 3; i++) map.GetPlanet("A").DockShipFromSave(Ship.ShipKind.WarShip, 0, new List<string>());
            map.GetPlanet("H").DockShipFromSave(Ship.ShipKind.WarShip, 1, new List<string>());   // another player's ship
            map.GetPlanet("H").DockShipFromSave(Ship.ShipKind.ColonyShip, 0, new List<string>()); // not a warship

            var summary = map.ChokepointSummary(0);
            ok &= Check(summary.colonized == 1 && summary.boardTotal == 1 && summary.shipsOnThem == 2 && summary.allShips == 5,
                "player 0: 1 of the board's 1 chokepoints colonized, 2 warships on it, 5 warships in all");
            var rival = map.ChokepointSummary(1);
            ok &= Check(rival.colonized == 0 && rival.boardTotal == 1 && rival.shipsOnThem == 0 && rival.allShips == 1,
                "player 1 colonizes no chokepoint; its one warship counts only in the total");
        }
        finally
        {
            Object.DestroyImmediate(go);
            Object.DestroyImmediate(constants);
        }

        ok &= Check(AITuningLogger.FormatChokepointList(new[] { ("Industrial 4", 312, 1f), ("Normal 9", 207, 0.9f) })
                    == "Industrial 4=312%1.00,Normal 9=207%0.90",
            "the Chokepoints field is name=betweenness%percentile joined by commas");
        ok &= Check(AITuningLogger.FormatChokepointList(new (string, int, float)[0]) == "-",
            "an empty list is logged as -");
        return ok;
    }

    // The map builds the score once from its own all-pairs paths.
    public static bool RunMapCentralityCheck()
    {
        var ok = true;
        var go = new GameObject("ChokepointSelfCheckMap_Centrality");
        var constants = Constants();
        try
        {
            var map = go.AddComponent<GameAIMap>();
            map.GameAIMapInit(HubSpawns(), constants);

            ok &= Check(map.Betweenness("H") == 9 && map.Betweenness("A") == 4
                        && map.Betweenness("X1") == 0 && map.Betweenness("Y") == 0,
                "hub layout: H is on 9 stored paths, A on 4, leaves on none");
            ok &= Check(Near(map.Chokepoint("H"), 1f) && Near(map.Chokepoint("A"), 0.8f) && Near(map.Chokepoint("Y"), 0f),
                "hub layout percentiles: H 1, A 0.8, a leaf 0");
            ok &= Check(map.IsChokepoint("H") && !map.IsChokepoint("A") && !map.IsChokepoint("Y"),
                "at the default 0.9 only H is a chokepoint");
            constants.chokepointPercentile = 0.5f;
            ok &= Check(map.IsChokepoint("H") && map.IsChokepoint("A") && !map.IsChokepoint("X1"),
                "at 0.5 A joins H, a leaf never does (betweenness 0)");
            constants.chokepointPercentile = 2f;
            ok &= Check(!map.IsChokepoint("H"), "a percentile above 1 switches chokepoints off (review focus 4)");
            ok &= Check(Near(map.Chokepoint("Nowhere"), 0f) && !map.IsChokepoint("Nowhere") && map.Betweenness("Nowhere") == 0,
                "an unknown planet name is not a chokepoint (review focus 5)");
            ok &= Check(map.TopChokepoints(2).Select(t => t.name).SequenceEqual(new[] { "H", "A" }),
                "TopChokepoints orders by betweenness: H then A");
        }
        finally
        {
            Object.DestroyImmediate(go);
            Object.DestroyImmediate(constants);
        }
        return ok;
    }
}
