using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using FlatSpace.AI;

public static class PlayerAIResourceSelfCheck
{
    [MenuItem("FlatSpace/AI/Run PlayerAI Resource Self-Check")]
    public static void Run()
    {
        var ok = RunFoodShortageScopingCheck();
        ok &= RunPartialShipmentRoundsCheck();
        ok &= RunShipmentCappedAtSurplusCheck();
        ok &= RunDistributionCenterPullsSurplusCheck();
        ok &= RunRealShortageOutranksSyntheticDemandCheck();
        ok &= RunNoDuplicateRowForDCWithRealShortageCheck();
        ok &= RunDistributionCenterAlsoReportsSurplusCheck();
        ok &= RunWorseShortageServedFirstCheck();
        ok &= RunDCToDCShippingCheck();
        ok &= RunSecondDCPrefersDistanceFromFirstOnTieCheck();
        Debug.Log(ok
            ? "[PlayerAIResourceSelfCheck] ALL PASSED"
            : "[PlayerAIResourceSelfCheck] FAILURES (see errors above)");
    }

    private static bool Check(bool condition, string label)
    {
        if (!condition) Debug.LogError($"[PlayerAIResourceSelfCheck] FAIL: {label}");
        return condition;
    }

    private static float _nextX;

    private static PlanetSpawnData MakeSpawn(string name, IEnumerable<string> connections = null)
    {
        var resourceData = ScriptableObject.CreateInstance<PlanetResourceData>();
        resourceData._initialPopulation = 0;
        resourceData._maxPopulation = 5;

        var spawn = ScriptableObject.CreateInstance<PlanetSpawnData>();
        spawn._planetName = name;
        // Distinct positions: PathingSystem.FindPath now throws on a same-position tie
        // instead of hanging (see fix/pathing-tie-break-assert) rather than silently
        // working around it.
        spawn._planetPosition = new Vector3(_nextX, 0f, 0f);
        _nextX += 100f;
        spawn._planetType = Planet.PlanetType.PlanetTypeNormal;
        spawn._resourceData = resourceData;
        spawn._connections = connections != null ? new List<string>(connections) : new List<string>();
        return spawn;
    }

    public static bool RunFoodShortageScopingCheck()
    {
        var ok = true;
        var mapGo = new GameObject("PAResSelfCheckMap");
        var p0Go = new GameObject("PAResSelfCheckPlayer0");
        var p1Go = new GameObject("PAResSelfCheckPlayer1");
        try
        {
            var map = mapGo.AddComponent<GameAIMap>();
            var constants = ScriptableObject.CreateInstance<GameAIConstants>();
            constants.defaultTravelSpeed = 1f;
            constants.maxPathNodesForResourceDistribution = 10;

            // P0Shortage -- P0Surplus -- P1Surplus, a connected chain so every pair has a
            // real path (avoids relying on PathingSystem's degenerate unreachable-path case).
            var spawns = new List<PlanetSpawnData>
            {
                MakeSpawn("P0Shortage", connections: new[] { "P0Surplus" }),
                MakeSpawn("P0Surplus", connections: new[] { "P1Surplus" }),
                MakeSpawn("P1Surplus"),
            };
            map.GameAIMapInit(spawns, constants);

            // Give the surplus planets real population owned by the matching player, so
            // EmitResourceOrders' GetPopulationFraction(Player.playerID) > 0 and an order
            // actually gets emitted rather than silently skipped.
            map.GetPlanet("P0Surplus").Population.Add(new Planet.Inhabitant { Player = 0 });
            map.GetPlanet("P1Surplus").Population.Add(new Planet.Inhabitant { Player = 1 });

            var results = new List<Planet.PlanetUpdateResult>
            {
                new Planet.PlanetUpdateResult("P0Shortage",
                    Planet.PlanetUpdateResult.PlanetUpdateResultType.PlanetUpdateResultTypeFoodShortage,
                    10f, playerID: 0),
                new Planet.PlanetUpdateResult("P0Surplus",
                    Planet.PlanetUpdateResult.PlanetUpdateResultType.PlanetUpdateResultTypeFoodSurplus,
                    20f, playerID: 0),
                new Planet.PlanetUpdateResult("P1Surplus",
                    Planet.PlanetUpdateResult.PlanetUpdateResultType.PlanetUpdateResultTypeFoodSurplus,
                    20f, playerID: 1),
            };

            var player0 = p0Go.AddComponent<Player>();
            var player0AI = p0Go.AddComponent<PlayerAI>();
            player0AI.Player = player0;
            player0AI.AIMap = map;
            player0.playerID = 0;

            var player1 = p1Go.AddComponent<Player>();
            var player1AI = p1Go.AddComponent<PlayerAI>();
            player1AI.Player = player1;
            player1AI.AIMap = map;
            player1.playerID = 1;

            var orders0 = new List<GameAI.GameAIOrder>();
            player0AI.ProcessResults(results, orders0);
            ok &= Check(orders0.Exists(o =>
                    o.Type == GameAI.GameAIOrder.OrderType.OrderTypeFoodTransport &&
                    o.Origin == "P0Surplus" && o.Target == "P0Shortage"),
                "player 0 ships its own surplus to its own shortage (positive control)");

            var orders1 = new List<GameAI.GameAIOrder>();
            player1AI.ProcessResults(results, orders1);
            ok &= Check(!orders1.Exists(o =>
                    o.Type == GameAI.GameAIOrder.OrderType.OrderTypeFoodTransport &&
                    o.Target == "P0Shortage"),
                "player 1 does not ship its surplus to player 0's shortage");
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(p1Go);
            UnityEngine.Object.DestroyImmediate(p0Go);
            UnityEngine.Object.DestroyImmediate(mapGo);
        }
        return ok;
    }

    // A shipment is capped at min(source's remaining surplus, target's remaining shortfall), rebuilding
    // and re-running the matrix in rounds so a source with leftover surplus can serve a second shortage
    // in the same turn instead of dumping its entire surplus on whichever shortage claims it first.
    public static bool RunPartialShipmentRoundsCheck()
    {
        var ok = true;
        var mapGo = new GameObject("PAResSelfCheckMap3");
        var playerGo = new GameObject("PAResSelfCheckPlayer3");
        try
        {
            var map = mapGo.AddComponent<GameAIMap>();
            var constants = ScriptableObject.CreateInstance<GameAIConstants>();
            constants.defaultTravelSpeed = 1f;
            constants.maxPathNodesForResourceDistribution = 10;

            var spawns = new List<PlanetSpawnData>
            {
                MakeSpawn("Source", connections: new[] { "ShortageA", "ShortageB" }),
                MakeSpawn("ShortageA"),
                MakeSpawn("ShortageB"),
            };
            map.GameAIMapInit(spawns, constants);
            map.GetPlanet("Source").Population.Add(new Planet.Inhabitant { Player = 0 });

            var results = new List<Planet.PlanetUpdateResult>
            {
                new Planet.PlanetUpdateResult("ShortageA",
                    Planet.PlanetUpdateResult.PlanetUpdateResultType.PlanetUpdateResultTypeFoodShortage,
                    -10f, playerID: 0),
                new Planet.PlanetUpdateResult("ShortageB",
                    Planet.PlanetUpdateResult.PlanetUpdateResultType.PlanetUpdateResultTypeFoodShortage,
                    -15f, playerID: 0),
                new Planet.PlanetUpdateResult("Source",
                    Planet.PlanetUpdateResult.PlanetUpdateResultType.PlanetUpdateResultTypeFoodSurplus,
                    30f, playerID: 0),
            };

            var player = playerGo.AddComponent<Player>();
            var playerAI = playerGo.AddComponent<PlayerAI>();
            playerAI.Player = player;
            playerAI.AIMap = map;
            player.playerID = 0;

            var orders = new List<GameAI.GameAIOrder>();
            playerAI.ProcessResults(results, orders);

            var toA = orders.Find(o =>
                o.Type == GameAI.GameAIOrder.OrderType.OrderTypeFoodTransport && o.Target == "ShortageA");
            var toB = orders.Find(o =>
                o.Type == GameAI.GameAIOrder.OrderType.OrderTypeFoodTransport && o.Target == "ShortageB");

            ok &= Check(toA.Origin == "Source" && Mathf.Approximately(Convert.ToSingle(toA.Data), 10f),
                "ShortageA (need 10) gets exactly 10 from Source's 30 surplus, not the whole 30");
            ok &= Check(toB.Origin == "Source" && Mathf.Approximately(Convert.ToSingle(toB.Data), 15f),
                "with 20 left after serving A, Source also serves ShortageB (need 15) in the same turn");
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(playerGo);
            UnityEngine.Object.DestroyImmediate(mapGo);
        }
        return ok;
    }

    // When total demand exceeds what a source has, the shipment still caps at the source's remaining
    // surplus rather than overshipping — this behavior already held before the partial-shipment change
    // and must keep holding now that the amount is computed by the round-robin loop instead of inline.
    public static bool RunShipmentCappedAtSurplusCheck()
    {
        var ok = true;
        var mapGo = new GameObject("PAResSelfCheckMap4");
        var playerGo = new GameObject("PAResSelfCheckPlayer4");
        try
        {
            var map = mapGo.AddComponent<GameAIMap>();
            var constants = ScriptableObject.CreateInstance<GameAIConstants>();
            constants.defaultTravelSpeed = 1f;
            constants.maxPathNodesForResourceDistribution = 10;

            var spawns = new List<PlanetSpawnData>
            {
                MakeSpawn("SmallSource", connections: new[] { "BigShortage" }),
                MakeSpawn("BigShortage"),
            };
            map.GameAIMapInit(spawns, constants);
            map.GetPlanet("SmallSource").Population.Add(new Planet.Inhabitant { Player = 0 });

            var results = new List<Planet.PlanetUpdateResult>
            {
                new Planet.PlanetUpdateResult("BigShortage",
                    Planet.PlanetUpdateResult.PlanetUpdateResultType.PlanetUpdateResultTypeFoodShortage,
                    -50f, playerID: 0),
                new Planet.PlanetUpdateResult("SmallSource",
                    Planet.PlanetUpdateResult.PlanetUpdateResultType.PlanetUpdateResultTypeFoodSurplus,
                    5f, playerID: 0),
            };

            var player = playerGo.AddComponent<Player>();
            var playerAI = playerGo.AddComponent<PlayerAI>();
            playerAI.Player = player;
            playerAI.AIMap = map;
            player.playerID = 0;

            var orders = new List<GameAI.GameAIOrder>();
            playerAI.ProcessResults(results, orders);

            var toShortage = orders.Find(o =>
                o.Type == GameAI.GameAIOrder.OrderType.OrderTypeFoodTransport && o.Target == "BigShortage");

            ok &= Check(toShortage.Origin == "SmallSource" && Mathf.Approximately(Convert.ToSingle(toShortage.Data), 5f),
                "a shortfall (50) bigger than the source's surplus (5) still only ships the 5 available");
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(playerGo);
            UnityEngine.Object.DestroyImmediate(mapGo);
        }
        return ok;
    }

    // A DC below its target stock, with NO real shortage anywhere, must still pull surplus toward it —
    // proving the synthetic demand path actually reaches the shipment pipeline.
    public static bool RunDistributionCenterPullsSurplusCheck()
    {
        var ok = true;
        var mapGo = new GameObject("PAResSelfCheckMap_DC1");
        var playerGo = new GameObject("PAResSelfCheckPlayer_DC1");
        try
        {
            var map = mapGo.AddComponent<GameAIMap>();
            var constants = ScriptableObject.CreateInstance<GameAIConstants>();
            constants.defaultTravelSpeed = 1f;
            constants.maxPathNodesForResourceDistribution = 10;
            constants.distributionCenterFoodTargetStock = 20f;
            constants.minPlanetsForDistributionCenters = 0; // default (30) would block selection on this 2-planet map

            var spawns = new List<PlanetSpawnData>
            {
                MakeSpawn("Source", connections: new[] { "DC" }),
                MakeSpawn("DC"),
            };
            map.GameAIMapInit(spawns, constants);
            // Owner is only ever set by Planet's private SetPlanetOwnership() during a real UpdatePlanet()
            // tick, which this self-check never runs — it must be assigned explicitly, or UpdateDistribution-
            // Centers' Owner-filtered "colonized planets" query would never see "DC" as a valid candidate.
            map.GetPlanet("Source").Population.Add(new Planet.Inhabitant { Player = 0 });
            map.GetPlanet("Source").Owner = 0;
            map.GetPlanet("DC").Population.Add(new Planet.Inhabitant { Player = 0 });
            map.GetPlanet("DC").Owner = 0;
            map.GetPlanet("DC").Food = 5f; // 15 short of its 20 target

            var player = playerGo.AddComponent<Player>();
            var playerAI = playerGo.AddComponent<PlayerAI>();
            playerAI.Player = player;
            playerAI.AIMap = map;
            player.playerID = 0;

            var results = new List<Planet.PlanetUpdateResult>
            {
                new Planet.PlanetUpdateResult("Source",
                    Planet.PlanetUpdateResult.PlanetUpdateResultType.PlanetUpdateResultTypeFoodSurplus,
                    30f, playerID: 0),
            };

            playerAI.UpdateDistributionCenters(results, turnNumber: 1); // designates "DC" as the Food DC
            var orders = new List<GameAI.GameAIOrder>();
            playerAI.ProcessResults(results, orders);

            var toDC = orders.Find(o =>
                o.Type == GameAI.GameAIOrder.OrderType.OrderTypeFoodTransport && o.Target == "DC");
            ok &= Check(toDC.Origin == "Source" && Mathf.Approximately(Convert.ToSingle(toDC.Data), 15f),
                "the DC pulls exactly its 15-unit gap (target 20 minus current 5), not the source's whole 30 surplus");
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(playerGo);
            UnityEngine.Object.DestroyImmediate(mapGo);
        }
        return ok;
    }

    // A real shortage and a DC's synthetic demand compete for the same single unit of surplus. The real
    // shortage must win even though it is numerically SMALLER than the DC's gap: with the sentinel
    // applied, the DC's row priority never depends on the gap size at all. (An earlier version of this
    // test gave the DC a huge gap, e.g. 1000, which does not actually discriminate: even a naive,
    // un-sentineled gap-derived priority of -1000 would still lose to the real shortage's -5 under this
    // codebase's "higher priority processes first" rule, since -1000 < -5. Giving the DC a SMALL gap
    // instead means a regressed implementation — one that used the DC's gap-derived priority instead of
    // the sentinel — would rank the DC's row ABOVE the real shortage and wrongly serve it first.)
    public static bool RunRealShortageOutranksSyntheticDemandCheck()
    {
        var ok = true;
        var mapGo = new GameObject("PAResSelfCheckMap_DC2");
        var playerGo = new GameObject("PAResSelfCheckPlayer_DC2");
        try
        {
            var map = mapGo.AddComponent<GameAIMap>();
            var constants = ScriptableObject.CreateInstance<GameAIConstants>();
            constants.defaultTravelSpeed = 1f;
            constants.maxPathNodesForResourceDistribution = 10;
            // Small gap (1), not huge — see the comment above for why a huge gap fails to discriminate.
            constants.distributionCenterFoodTargetStock = 1f;
            constants.minPlanetsForDistributionCenters = 0; // default (30) would block selection on this small map

            var spawns = new List<PlanetSpawnData>
            {
                MakeSpawn("Source", connections: new[] { "DC", "Shortage" }),
                MakeSpawn("DC", connections: new[] { "Source" }),
                MakeSpawn("Shortage", connections: new[] { "Source" }),
            };
            map.GameAIMapInit(spawns, constants);
            // See the note in RunDistributionCenterPullsSurplusCheck: Owner must be set explicitly. Only
            // Source and DC need it (DC must be a valid candidate); Shortage stays uncolonized-for-DC-
            // purposes on purpose, so it can't itself be considered as a DC candidate and complicate the
            // scoring this test is checking.
            map.GetPlanet("Source").Population.Add(new Planet.Inhabitant { Player = 0 });
            map.GetPlanet("Source").Owner = 0;
            map.GetPlanet("DC").Population.Add(new Planet.Inhabitant { Player = 0 });
            map.GetPlanet("DC").Owner = 0;
            map.GetPlanet("DC").Food = 0f;

            var player = playerGo.AddComponent<Player>();
            var playerAI = playerGo.AddComponent<PlayerAI>();
            playerAI.Player = player;
            playerAI.AIMap = map;
            player.playerID = 0;

            var results = new List<Planet.PlanetUpdateResult>
            {
                new Planet.PlanetUpdateResult("Source",
                    Planet.PlanetUpdateResult.PlanetUpdateResultType.PlanetUpdateResultTypeFoodSurplus,
                    5f, playerID: 0),
                new Planet.PlanetUpdateResult("Shortage",
                    Planet.PlanetUpdateResult.PlanetUpdateResultType.PlanetUpdateResultTypeFoodShortage,
                    -5f, playerID: 0),
            };

            playerAI.UpdateDistributionCenters(results, turnNumber: 1); // designates "DC"
            var orders = new List<GameAI.GameAIOrder>();
            playerAI.ProcessResults(results, orders);

            var toShortage = orders.Find(o =>
                o.Type == GameAI.GameAIOrder.OrderType.OrderTypeFoodTransport && o.Target == "Shortage");
            ok &= Check(toShortage.Origin == "Source" && Mathf.Approximately(Convert.ToSingle(toShortage.Data), 5f),
                "the real shortage (5) claims the source's entire surplus (5) even though the DC's own gap " +
                "(1) is numerically smaller, because the DC's priority is a fixed low sentinel, not " +
                "gap-derived — a regressed, un-sentineled implementation would rank the DC's -1 above the " +
                "real shortage's -5 and serve the DC first instead, leaving the real shortage only 4");

            ok &= Check(!orders.Exists(o =>
                    o.Type == GameAI.GameAIOrder.OrderType.OrderTypeFoodTransport && o.Target == "DC"),
                "nothing is left over for the DC once the real shortage is served");
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(playerGo);
            UnityEngine.Object.DestroyImmediate(mapGo);
        }
        return ok;
    }

    // A DC that ALSO reports a real shortage the same turn must not create two decision rows for the same
    // planet+resource — without the `shortages.Exists` guard, shortages.ToDictionary would throw
    // ArgumentException on the duplicate key before BuildResourceMatrix ever runs, so the synthetic entry
    // must never be added in the first place, not merely be caught later.
    //
    // This also proves the DC's real shortage keeps its own normal, gap-derived priority rather than
    // being wrongly demoted to the DC sentinel: Source has just enough surplus (8) for ONE of DC's (-20)
    // or Other's (-8) real shortages, not both, forcing a genuine priority contest instead of both being
    // served across rounds. DC's shortage is WORSE (more negative) than Other's, so under correct priority
    // ordering (worse shortage processed first) DC's -20 must win and claim the surplus. If DC's row were
    // wrongly given the low-priority sentinel instead, Other would win instead and DC would get nothing —
    // an earlier version of this test had only one shortage row, so priority ordering was never actually
    // exercised and this regression would have gone undetected.
    public static bool RunNoDuplicateRowForDCWithRealShortageCheck()
    {
        var ok = true;
        var mapGo = new GameObject("PAResSelfCheckMap_DC3");
        var playerGo = new GameObject("PAResSelfCheckPlayer_DC3");
        try
        {
            var map = mapGo.AddComponent<GameAIMap>();
            var constants = ScriptableObject.CreateInstance<GameAIConstants>();
            constants.defaultTravelSpeed = 1f;
            constants.maxPathNodesForResourceDistribution = 10;
            constants.distributionCenterFoodTargetStock = 100f;
            constants.minPlanetsForDistributionCenters = 0; // default (30) would block selection on this small map

            var spawns = new List<PlanetSpawnData>
            {
                MakeSpawn("Source", connections: new[] { "DC", "Other" }),
                MakeSpawn("DC"),
                MakeSpawn("Other"),
            };
            map.GameAIMapInit(spawns, constants);
            // See the note in RunDistributionCenterPullsSurplusCheck: Owner must be set explicitly. Other
            // stays uncolonized-for-DC-purposes on purpose (a plain real-shortage planet, not a candidate).
            map.GetPlanet("Source").Population.Add(new Planet.Inhabitant { Player = 0 });
            map.GetPlanet("Source").Owner = 0;
            map.GetPlanet("DC").Population.Add(new Planet.Inhabitant { Player = 0 });
            map.GetPlanet("DC").Owner = 0;
            map.GetPlanet("DC").Food = 0f;

            var player = playerGo.AddComponent<Player>();
            var playerAI = playerGo.AddComponent<PlayerAI>();
            playerAI.Player = player;
            playerAI.AIMap = map;
            player.playerID = 0;

            var results = new List<Planet.PlanetUpdateResult>
            {
                new Planet.PlanetUpdateResult("Source",
                    Planet.PlanetUpdateResult.PlanetUpdateResultType.PlanetUpdateResultTypeFoodSurplus,
                    8f, playerID: 0),
                // DC ALSO has a real shortage this turn.
                new Planet.PlanetUpdateResult("DC",
                    Planet.PlanetUpdateResult.PlanetUpdateResultType.PlanetUpdateResultTypeFoodShortage,
                    -20f, playerID: 0),
                new Planet.PlanetUpdateResult("Other",
                    Planet.PlanetUpdateResult.PlanetUpdateResultType.PlanetUpdateResultTypeFoodShortage,
                    -8f, playerID: 0),
            };

            playerAI.UpdateDistributionCenters(results, turnNumber: 1); // designates "DC"
            var orders = new List<GameAI.GameAIOrder>();
            playerAI.ProcessResults(results, orders);

            var toDC = orders.FindAll(o =>
                o.Type == GameAI.GameAIOrder.OrderType.OrderTypeFoodTransport && o.Target == "DC");
            var toOther = orders.FindAll(o =>
                o.Type == GameAI.GameAIOrder.OrderType.OrderTypeFoodTransport && o.Target == "Other");

            ok &= Check(toDC.Count == 1 && Mathf.Approximately(Convert.ToSingle(toDC[0].Data), 8f),
                "exactly one shipment reaches the DC, capped at the source's surplus (8), not a second " +
                "synthetic entry stacked on top of it");
            ok &= Check(toOther.Count == 0,
                "DC's worse real shortage (-20) correctly outranks Other's milder real shortage (-8) and " +
                "claims the source's only surplus, proving DC's row kept its normal, gap-derived priority " +
                "instead of being wrongly demoted to the DC sentinel (which would let Other, or neither, win)");
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(playerGo);
            UnityEngine.Object.DestroyImmediate(mapGo);
        }
        return ok;
    }

    // A DC's stock can sit above its own small population-based need (a real surplus, per
    // Planet.UpdatePlanet) while still below its much larger target stock (synthetic demand) — the same
    // planet then appears in BOTH shortages and surplusResults the same turn. This is the routine case,
    // not an edge case, and must not throw or cause the DC to ship to itself.
    public static bool RunDistributionCenterAlsoReportsSurplusCheck()
    {
        var ok = true;
        var mapGo = new GameObject("PAResSelfCheckMap_DC4");
        var playerGo = new GameObject("PAResSelfCheckPlayer_DC4");
        try
        {
            var map = mapGo.AddComponent<GameAIMap>();
            var constants = ScriptableObject.CreateInstance<GameAIConstants>();
            constants.defaultTravelSpeed = 1f;
            constants.maxPathNodesForResourceDistribution = 10;
            constants.distributionCenterFoodTargetStock = 50f;
            constants.minPlanetsForDistributionCenters = 0;

            var spawns = new List<PlanetSpawnData>
            {
                MakeSpawn("Source", connections: new[] { "DC" }),
                MakeSpawn("DC"),
            };
            map.GameAIMapInit(spawns, constants);
            map.GetPlanet("Source").Population.Add(new Planet.Inhabitant { Player = 0 });
            map.GetPlanet("Source").Owner = 0;
            map.GetPlanet("DC").Population.Add(new Planet.Inhabitant { Player = 0 });
            map.GetPlanet("DC").Owner = 0;
            map.GetPlanet("DC").Food = 20f; // below the 50 target (synthetic demand), but this self-check
            // hand-constructs results directly (bypassing Planet.UpdatePlanet), so DC's real-surplus
            // status below is asserted explicitly rather than derived from this stock value.

            var player = playerGo.AddComponent<Player>();
            var playerAI = playerGo.AddComponent<PlayerAI>();
            playerAI.Player = player;
            playerAI.AIMap = map;
            player.playerID = 0;

            var results = new List<Planet.PlanetUpdateResult>
            {
                new Planet.PlanetUpdateResult("Source",
                    Planet.PlanetUpdateResult.PlanetUpdateResultType.PlanetUpdateResultTypeFoodSurplus,
                    30f, playerID: 0),
                // DC is ALSO a real surplus source this turn, independent of its synthetic demand below.
                new Planet.PlanetUpdateResult("DC",
                    Planet.PlanetUpdateResult.PlanetUpdateResultType.PlanetUpdateResultTypeFoodSurplus,
                    5f, playerID: 0),
            };

            playerAI.UpdateDistributionCenters(results, turnNumber: 1); // designates "DC"
            var orders = new List<GameAI.GameAIOrder>();
            var threw = false;
            try
            {
                playerAI.ProcessResults(results, orders);
            }
            catch (Exception)
            {
                threw = true;
            }

            ok &= Check(!threw,
                "a DC that is ALSO a real surplus source the same turn (routine: stock above its own " +
                "population's need but below its much larger target) does not throw KeyNotFoundException");
            ok &= Check(!orders.Exists(o =>
                    o.Type == GameAI.GameAIOrder.OrderType.OrderTypeFoodTransport &&
                    o.Origin == "DC" && o.Target == "DC"),
                "the DC never ships to itself");
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(playerGo);
            UnityEngine.Object.DestroyImmediate(mapGo);
        }
        return ok;
    }

    // BuildResourceMatrix's row Priority is -Data, so a worse (more negative) shortage sorts first and
    // gets first claim on scarce surplus. Source has only enough surplus (5) to fully cover ONE of
    // Mild's (-5) or Severe's (-20) shortage, forcing a genuine contest instead of both being served.
    // Before the fix, Priority was the raw signed Data, so Mild's -5 (numerically greater than -20)
    // outranked and was served first, leaving the worse-off planet with nothing.
    public static bool RunWorseShortageServedFirstCheck()
    {
        var ok = true;
        var mapGo = new GameObject("PAResSelfCheckMap_Order1");
        var playerGo = new GameObject("PAResSelfCheckPlayer_Order1");
        try
        {
            var map = mapGo.AddComponent<GameAIMap>();
            var constants = ScriptableObject.CreateInstance<GameAIConstants>();
            constants.defaultTravelSpeed = 1f;
            constants.maxPathNodesForResourceDistribution = 10;

            var spawns = new List<PlanetSpawnData>
            {
                MakeSpawn("Source", connections: new[] { "Mild", "Severe" }),
                MakeSpawn("Mild"),
                MakeSpawn("Severe"),
            };
            map.GameAIMapInit(spawns, constants);
            map.GetPlanet("Source").Population.Add(new Planet.Inhabitant { Player = 0 });
            map.GetPlanet("Source").Owner = 0;

            var player = playerGo.AddComponent<Player>();
            var playerAI = playerGo.AddComponent<PlayerAI>();
            playerAI.Player = player;
            playerAI.AIMap = map;
            player.playerID = 0;

            var results = new List<Planet.PlanetUpdateResult>
            {
                new Planet.PlanetUpdateResult("Source",
                    Planet.PlanetUpdateResult.PlanetUpdateResultType.PlanetUpdateResultTypeFoodSurplus,
                    5f, playerID: 0),
                new Planet.PlanetUpdateResult("Mild",
                    Planet.PlanetUpdateResult.PlanetUpdateResultType.PlanetUpdateResultTypeFoodShortage,
                    -5f, playerID: 0),
                new Planet.PlanetUpdateResult("Severe",
                    Planet.PlanetUpdateResult.PlanetUpdateResultType.PlanetUpdateResultTypeFoodShortage,
                    -20f, playerID: 0),
            };

            var orders = new List<GameAI.GameAIOrder>();
            playerAI.ProcessResults(results, orders);

            var toSevere = orders.Find(o =>
                o.Type == GameAI.GameAIOrder.OrderType.OrderTypeFoodTransport && o.Target == "Severe");
            ok &= Check(toSevere.Origin == "Source" && Mathf.Approximately(Convert.ToSingle(toSevere.Data), 5f),
                "the worse shortage (-20) claims the source's entire surplus (5) ahead of the milder one");

            ok &= Check(!orders.Exists(o =>
                    o.Type == GameAI.GameAIOrder.OrderType.OrderTypeFoodTransport && o.Target == "Mild"),
                "nothing is left over for the milder shortage once the worse one is served first");
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(playerGo);
            UnityEngine.Object.DestroyImmediate(mapGo);
        }
        return ok;
    }

    // Verifies a DC can ship to another DC under the current structure: DC1 is a real surplus source
    // this turn, DC2 has no real result at all (its only demand is the synthetic DC gap), and both are
    // pre-set as sticky DCs (bypassing selection, which is not what this test is about). This is
    // independent of the SelectDistributionCenter scoring change (producers.Union(current)) -- that
    // only affects which planet gets picked as a DC, never whether a DC can ship to another DC, since
    // ProcessResourceShipments/BuildResourceMatrix (the shipping path) never calls SelectDistributionCenter.
    public static bool RunDCToDCShippingCheck()
    {
        var ok = true;
        var mapGo = new GameObject("PAResSelfCheckMap_DC5");
        var playerGo = new GameObject("PAResSelfCheckPlayer_DC5");
        try
        {
            var map = mapGo.AddComponent<GameAIMap>();
            var constants = ScriptableObject.CreateInstance<GameAIConstants>();
            constants.defaultTravelSpeed = 1f;
            constants.maxPathNodesForResourceDistribution = 10;
            constants.distributionCenterFoodTargetStock = 20f;

            var spawns = new List<PlanetSpawnData>
            {
                MakeSpawn("DC1", connections: new[] { "DC2" }),
                MakeSpawn("DC2"),
            };
            map.GameAIMapInit(spawns, constants);
            map.GetPlanet("DC1").Population.Add(new Planet.Inhabitant { Player = 0 });
            map.GetPlanet("DC1").Owner = 0;
            map.GetPlanet("DC2").Population.Add(new Planet.Inhabitant { Player = 0 });
            map.GetPlanet("DC2").Owner = 0;
            map.GetPlanet("DC2").Food = 0f; // below the 20 target stock, no real shortage reported below

            var player = playerGo.AddComponent<Player>();
            var playerAI = playerGo.AddComponent<PlayerAI>();
            playerAI.Player = player;
            playerAI.AIMap = map;
            player.playerID = 0;

            // Both already sticky-selected as Food DCs; this test is about shipping, not selection.
            playerAI.SetDistributionCenters("Food", new List<string> { "DC1", "DC2" });

            var results = new List<Planet.PlanetUpdateResult>
            {
                new Planet.PlanetUpdateResult("DC1",
                    Planet.PlanetUpdateResult.PlanetUpdateResultType.PlanetUpdateResultTypeFoodSurplus,
                    30f, playerID: 0),
                // DC2 reports nothing real -- its only demand is the synthetic DC-gap entry.
            };

            var orders = new List<GameAI.GameAIOrder>();
            playerAI.ProcessResults(results, orders);

            var toDC2 = orders.Find(o =>
                o.Type == GameAI.GameAIOrder.OrderType.OrderTypeFoodTransport && o.Target == "DC2");
            ok &= Check(toDC2.Origin == "DC1" && Mathf.Approximately(Convert.ToSingle(toDC2.Data), 20f),
                "DC1 (a real surplus source this turn) ships to DC2's synthetic gap (target 20, current 0), " +
                "capped at the gap (20) not DC1's whole surplus (30) -- confirms DC-to-DC shipping works " +
                "under the current structure, independent of how either DC was selected");
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(playerGo);
            UnityEngine.Object.DestroyImmediate(mapGo);
        }
        return ok;
    }

    // Verifies the new dcDistance tie-break specifically, not just "a second DC gets picked somewhere
    // reasonable". Near and Far each uniquely reach one otherwise-unreachable satellite (equal coverage
    // score, 1 each), and are deliberately built to ALSO tie on the older supplyDistance tie-break (both
    // supplied only by Source, at the same distance) -- so only the new "farther from the existing DC"
    // criterion can be what decides it. Near sits one hop from the existing DC1; Far is unreachable from
    // DC1 at all within maxPathNodesForResourceDistribution. Far must win.
    public static bool RunSecondDCPrefersDistanceFromFirstOnTieCheck()
    {
        var ok = true;
        var mapGo = new GameObject("PAResSelfCheckMap_DC6");
        var playerGo = new GameObject("PAResSelfCheckPlayer_DC6");
        try
        {
            var map = mapGo.AddComponent<GameAIMap>();
            var constants = ScriptableObject.CreateInstance<GameAIConstants>();
            constants.defaultTravelSpeed = 1f;
            constants.maxPathNodesForResourceDistribution = 2; // direct connections only
            constants.minPlanetsForDistributionCenters = 0;
            constants.minPlanetsForSecondDistributionCenter = 0; // allow a second slot immediately

            var spawns = new List<PlanetSpawnData>
            {
                MakeSpawn("Source", connections: new[] { "Near", "Far" }),
                MakeSpawn("Near", connections: new[] { "NearSat", "DC1" }),
                MakeSpawn("Far", connections: new[] { "FarSat" }),
                MakeSpawn("NearSat"),
                MakeSpawn("FarSat"),
                MakeSpawn("DC1"),
            };
            map.GameAIMapInit(spawns, constants);
            foreach (var name in new[] { "Source", "Near", "Far", "NearSat", "FarSat", "DC1" })
            {
                map.GetPlanet(name).Population.Add(new Planet.Inhabitant { Player = 0 });
                map.GetPlanet(name).Owner = 0;
            }

            var player = playerGo.AddComponent<Player>();
            var playerAI = playerGo.AddComponent<PlayerAI>();
            playerAI.Player = player;
            playerAI.AIMap = map;
            player.playerID = 0;

            playerAI.SetDistributionCenters("Food", new List<string> { "DC1" });

            var results = new List<Planet.PlanetUpdateResult>
            {
                new Planet.PlanetUpdateResult("Source",
                    Planet.PlanetUpdateResult.PlanetUpdateResultType.PlanetUpdateResultTypeFoodSurplus,
                    30f, playerID: 0),
            };

            playerAI.UpdateDistributionCenters(results, turnNumber: 1);

            ok &= Check(playerAI.FoodDistributionCenters.Contains("Far"),
                "the second DC picks Far (unreachable from the existing DC1 within range) over Near (one " +
                "hop from DC1), even though both tie on coverage score (1 satellite each) and on the older " +
                "supplyDistance tie-break (both supplied by Source at the same distance)");
            ok &= Check(!playerAI.FoodDistributionCenters.Contains("Near"),
                "Near, despite tying on every older criterion, loses purely for being closer to the existing DC");
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(playerGo);
            UnityEngine.Object.DestroyImmediate(mapGo);
        }
        return ok;
    }
}
