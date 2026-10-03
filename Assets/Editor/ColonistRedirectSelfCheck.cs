using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using FlatSpace.AI;

public static class ColonistRedirectSelfCheck
{
    [MenuItem("FlatSpace/AI/Run Colonist Redirect Self-Check")]
    public static void Run() => RunChecks();

    public static bool RunChecks()
    {
        var ok = RunPlannerCheck();
        ok &= RunApplyCheck();
        ok &= RunCandidateCheck();
        ok &= RunArrivalCheck();
        Debug.Log(ok
            ? "[ColonistRedirectSelfCheck] ALL PASSED"
            : "[ColonistRedirectSelfCheck] FAILURES (see errors above)");
        return ok;
    }

    private static bool Check(bool condition, string label)
    {
        if (!condition) Debug.LogError($"[ColonistRedirectSelfCheck] FAIL: {label}");
        return condition;
    }

    // A colonist from A to D along A>B>D (the diamond: B route ~224, C route ~361), delays 10 turns, `elapsed` turns flown.
    private static GameAI.GameAIOrder Colonist(int elapsed, string target = "D", List<string> route = null)
        => new GameAI.GameAIOrder
        {
            Type = GameAI.GameAIOrder.OrderType.OrderTypePopulationTransport,
            TimingType = GameAI.GameAIOrder.OrderTimingType.OrderTimingTypeDelayed,
            TotalDelay = 10, TimingDelay = 10 - elapsed, Data = 1, Origin = "A", Target = target, PlayerId = 0,
            Route = route ?? new List<string> { "A", "B", "D" },
        };

    private static ColonistRedirect.Result Plan(BlockadeAvoidanceSelfCheck.Scenario s, GameAI.GameAIOrder order,
        BlockadeView view, System.Func<Planet, bool> candidate = null)
        => ColonistRedirect.Plan(s.Map, new BlockadeSystem(s.Map, s.Research), order, view, 6,
            candidate ?? (p => false), name => 1f);

    public static bool RunPlannerCheck()
    {
        var ok = true;
        using (var s = BlockadeAvoidanceSelfCheck.Scenario.Diamond())
        {
            var bs = new BlockadeSystem(s.Map, s.Research);

            // Current node: B sits at fraction ~0.5 of A>B>D (A-B ~112, B-D ~112).
            ok &= Check(bs.CurrentNode(Colonist(0)) == "A", "just launched: the current node is the origin");
            ok &= Check(bs.CurrentNode(Colonist(3)) == "A", "30% flown: still on the origin (B is at ~50%)");
            ok &= Check(bs.CurrentNode(Colonist(6)) == "B", "60% flown: B was the last node passed");
            ok &= Check(string.Join(">", bs.NodesAhead(Colonist(6))) == "D", "60% flown: only D is ahead");
            ok &= Check(string.Join(">", bs.NodesAhead(Colonist(0))) == "B>D", "just launched: B and D are ahead");
            var noRoute = Colonist(6);
            noRoute.Route = null;   // an older save: no carried route, the shortest path from Origin is used
            ok &= Check(bs.CurrentNode(noRoute) == "B", "no carried route: falls back to the shortest path from the origin");

            // Clean: nothing blocked ahead leaves the order alone.
            var clean = Plan(s, Colonist(0), BlockadeView.Of());
            ok &= Check(clean.Kind == ColonistRedirect.RedirectKind.None && clean.BlockedAhead.Count == 0,
                "no blockade ahead: no redirect");

            // A blockade BEHIND the colonist is not ahead of it.
            var behind = Plan(s, Colonist(6), BlockadeView.Of("A"));
            ok &= Check(behind.Kind == ColonistRedirect.RedirectKind.None, "a blockade behind the colonist is ignored");

            // B blockaded and the colonist still at A: detour A>C>D, same target.
            var detour = Plan(s, Colonist(0), BlockadeView.Of("B"));
            ok &= Check(detour.Kind == ColonistRedirect.RedirectKind.Detour && detour.Target == "D"
                        && string.Join(">", detour.Nodes) == "A>C>D" && detour.CurrentNode == "A"
                        && detour.BlockedAhead.Count == 1 && detour.BlockedAhead[0] == "B",
                "B ahead is blockaded: detour A>C>D to the same target");
            ok &= Check(detour.Cost > 300f, "the detour's cost is the real (longer) route cost");

            // Target blockaded: no detour exists; with no candidate nothing changes.
            var none = Plan(s, Colonist(0), BlockadeView.Of("D"));
            ok &= Check(none.Kind == ColonistRedirect.RedirectKind.None && none.BlockedAhead.Contains("D"),
                "blockaded target and no candidate: no change, the blocked planet is reported");

            // Target blockaded: divert to the cheapest valid candidate. C is a candidate; B is too but nearer.
            var divert = Plan(s, Colonist(0), BlockadeView.Of("D"), p => p.PlanetName == "B" || p.PlanetName == "C");
            ok &= Check(divert.Kind == ColonistRedirect.RedirectKind.Divert && divert.Target == "B"
                        && string.Join(">", divert.Nodes) == "A>B",
                "blockaded target: divert to the cheapest candidate (B, nearer than C)");

            // The current target is never its own diversion, and a blockaded candidate is skipped.
            var self = Plan(s, Colonist(0), BlockadeView.Of("D"), p => p.PlanetName == "D");
            ok &= Check(self.Kind == ColonistRedirect.RedirectKind.None, "the current target is not a diversion candidate");
            var skipped = Plan(s, Colonist(0), BlockadeView.Of("D", "B", "C"), p => p.PlanetName == "B" || p.PlanetName == "C");
            ok &= Check(skipped.Kind == ColonistRedirect.RedirectKind.None, "blockaded candidates are not chosen");

            // The cost divisor tilts the choice but the reported cost stays real.
            var tilted = ColonistRedirect.Plan(s.Map, bs, Colonist(0), BlockadeView.Of("D"), 6,
                p => p.PlanetName == "B" || p.PlanetName == "C", name => name == "C" ? 10f : 1f);
            ok &= Check(tilted.Kind == ColonistRedirect.RedirectKind.Divert && tilted.Target == "C"
                        && tilted.Cost > 150f,
                "a large divisor on C makes it win; Result.Cost is still C's real route cost");

            // Arrival turn: never redirected.
            ok &= Check(Plan(s, Colonist(10), BlockadeView.Of("D")).Kind == ColonistRedirect.RedirectKind.None,
                "an order with no delay left is never redirected");

            // Null view: nothing is blockaded, so nothing to redirect.
            ok &= Check(Plan(s, Colonist(0), null).Kind == ColonistRedirect.RedirectKind.None, "a null view never redirects");
        }
        return ok;
    }

    private static GameAI.GameAIOrder Rider(int elapsed, string target = "D", string origin = "A")
        => new GameAI.GameAIOrder
        {
            Type = GameAI.GameAIOrder.OrderType.OrderTypeColonyFoodRider,
            TimingType = GameAI.GameAIOrder.OrderTimingType.OrderTimingTypeDelayed,
            TotalDelay = 10, TimingDelay = 10 - elapsed, Data = 10f, Origin = origin, Target = target, PlayerId = 0,
        };

    public static bool RunApplyCheck()
    {
        var ok = true;
        using (var s = BlockadeAvoidanceSelfCheck.Scenario.Diamond())
        {
            s.Constants.defaultTravelSpeed = 100f;

            // Detour: route and both delays change, target and flags do not.
            var colonist = Colonist(0);
            var rider = Rider(0);
            var orders = new List<GameAI.GameAIOrder> { colonist, rider };
            s.Map.GetPlanet("D").SetPopulationTransferInProgress(0);
            var detour = Plan(s, colonist, BlockadeView.Of("B"));
            ColonistRedirect.Apply(s.Map, orders, colonist, detour);
            ok &= Check(string.Join(">", colonist.Route) == "A>C>D" && colonist.Target == "D",
                "a detour replaces the route and keeps the target");
            ok &= Check(colonist.TotalDelay == colonist.TimingDelay
                        && colonist.TotalDelay == System.Math.Max(1, System.Convert.ToInt32(detour.Cost / 100f)),
                "the delay restarts from the new route's real cost (at least 1)");
            ok &= Check(rider.TimingDelay == colonist.TimingDelay && rider.TotalDelay == colonist.TotalDelay && rider.Target == "D",
                "the food rider keeps its target and takes the colonist's new delays");
            ok &= Check(s.Map.GetPlanet("D").IsPopulationTransferInProgress(0), "a detour keeps the target's inbound flag");

            // Divert: target, route, delays, rider and flags all move.
            var colonist2 = Colonist(0);
            var rider2 = Rider(0);
            var orders2 = new List<GameAI.GameAIOrder> { colonist2, rider2 };
            s.Map.GetPlanet("C").SetPopulationTransferInProgress(0, false);
            var divert = Plan(s, colonist2, BlockadeView.Of("D"), p => p.PlanetName == "C");
            ColonistRedirect.Apply(s.Map, orders2, colonist2, divert);
            ok &= Check(colonist2.Target == "C" && string.Join(">", colonist2.Route) == "A>C" && rider2.Target == "C",
                "a divert moves the colonist and its rider to the new target");
            ok &= Check(!s.Map.GetPlanet("D").IsPopulationTransferInProgress(0)
                        && s.Map.GetPlanet("C").IsPopulationTransferInProgress(0),
                "a divert clears the old target's inbound flag and sets the new one's");
            ok &= Check(colonist2.Origin == "A", "the order keeps its real origin");

            // Another colonist still heading for the old target keeps its flag.
            s.Map.GetPlanet("D").SetPopulationTransferInProgress(0);
            var other = Colonist(2);
            var colonist3 = Colonist(0);
            var orders3 = new List<GameAI.GameAIOrder> { other, colonist3 };
            ColonistRedirect.Apply(s.Map, orders3, colonist3, Plan(s, colonist3, BlockadeView.Of("D"), p => p.PlanetName == "B"));
            ok &= Check(colonist3.Target == "B" && s.Map.GetPlanet("D").IsPopulationTransferInProgress(0),
                "the old target's flag stays while another colonist of the player still targets it");

            // Two colonists, same origin and target, two riders: redirecting one leaves the other's rider alone.
            var cA = Colonist(0); var rA = Rider(0);
            var cB = Colonist(4); var rB = Rider(4);
            var orders4 = new List<GameAI.GameAIOrder> { cA, rA, cB, rB };
            ColonistRedirect.Apply(s.Map, orders4, cB, Plan(s, cB, BlockadeView.Of("D"), p => p.PlanetName == "C"));
            ok &= Check(rB.Target == "C" && rA.Target == "D" && rA.TimingDelay == 10,
                "the rider is matched on equal delay: the other colonist's rider is untouched");

            // None is a no-op.
            var idle = Colonist(0);
            ColonistRedirect.Apply(s.Map, new List<GameAI.GameAIOrder> { idle }, idle, Plan(s, idle, BlockadeView.Of()));
            ok &= Check(idle.Target == "D" && string.Join(">", idle.Route) == "A>B>D" && idle.TimingDelay == 10,
                "no redirect leaves the order untouched");

            // A very cheap route still takes at least one turn.
            s.Constants.defaultTravelSpeed = 100000f;
            var quick = Colonist(0);
            ColonistRedirect.Apply(s.Map, new List<GameAI.GameAIOrder> { quick }, quick, Plan(s, quick, BlockadeView.Of("B")));
            ok &= Check(quick.TimingDelay == 1 && quick.TotalDelay == 1, "a redirected delay is never below 1 turn");
        }
        return ok;
    }

    // A - B - C - D in a line; player 0 lives at A and its knowledge reaches direct neighbours only (radius 2 hops),
    // so A and B are known, C and D are not. Max population is 5 everywhere (BlockadeAvoidanceSelfCheck.Spawn).
    public static bool RunCandidateCheck()
    {
        var ok = true;
        var template = WarshipSelfCheck.MakeTemplate();
        var constants = WarshipSelfCheck.MakeConstants(template);
        var research = WarshipSelfCheck.MakeResearch();
        var mapGo = new GameObject("CRSelfCheckMap_Candidates");
        var playerGo = new GameObject("CRSelfCheckPlayer_Candidates");
        try
        {
            var map = BlockadeAvoidanceSelfCheck.Build(mapGo, constants,
                BlockadeAvoidanceSelfCheck.Spawn("A", 0f, 0f, new[] { "B" }),
                BlockadeAvoidanceSelfCheck.Spawn("B", 100f, 0f, new[] { "A", "C" }),
                BlockadeAvoidanceSelfCheck.Spawn("C", 200f, 0f, new[] { "B", "D" }),
                BlockadeAvoidanceSelfCheck.Spawn("D", 300f, 0f, new[] { "C" }));
            var a = map.GetPlanet("A");
            a.Owner = 0;
            for (var i = 0; i < 5; i++) a.Population.Add(new Planet.Inhabitant { Player = 0 });

            var player = playerGo.AddComponent<Player>();
            var ai = playerGo.AddComponent<PlayerAI>();
            ai.Player = player;
            ai.AIMap = map;
            player.playerID = 0;
            map.Knowledge.Update(map, 1, 2);

            var b = map.GetPlanet("B");
            ok &= Check(map.Knowledge.IsKnown(0, "B") && !map.Knowledge.IsKnown(0, "C"),
                "fixture: B (a neighbour of A) is known, C is not");
            ok &= Check(!ai.IsDiversionTarget(a), "my own full planet is not a diversion candidate");
            a.Population.RemoveRange(0, 2);   // 3 of 5
            ok &= Check(ai.IsDiversionTarget(a), "my own planet below max is a diversion candidate");
            ok &= Check(ai.IsDiversionTarget(b), "a known empty planet is a candidate");
            b.SetPopulationTransferInProgress(0);
            ok &= Check(ai.IsDiversionTarget(b), "a known planet with my colonist already inbound is a candidate");
            for (var i = 0; i < 5; i++) b.Population.Add(new Planet.Inhabitant { Player = 1 });
            b.SetPopulationTransferInProgress(0, false);
            ok &= Check(!ai.IsDiversionTarget(b), "a full planet held by another player is not a candidate");
            b.Population.RemoveRange(0, 3);   // 2 of 5, player 1
            ok &= Check(ai.IsDiversionTarget(b), "a planet held by another player below max is a candidate");
            ok &= Check(!ai.IsDiversionTarget(map.GetPlanet("C")), "an unknown planet is never a candidate");
            ok &= Check(ai.ColonizationCostDivisor("C") >= 1f, "the cost divisor is public and at least 1");
        }
        finally
        {
            Object.DestroyImmediate(playerGo);
            Object.DestroyImmediate(mapGo);
            WarshipSelfCheck.DestroyAll(research);
            Object.DestroyImmediate(constants);
            Object.DestroyImmediate(template);
        }
        return ok;
    }

    public static bool RunArrivalCheck()
    {
        var ok = true;
        using (var s = BlockadeAvoidanceSelfCheck.Scenario.Diamond())
        {
            var docked = new List<(string planet, int owner)>();
            System.Action<Planet, int> dock = (p, owner) =>
            {
                docked.Add((p.PlanetName, owner));
                p.DockShipFromSave(Ship.ShipKind.ColonyShip, owner, new List<string>());
            };

            var d = s.Map.GetPlanet("D");
            d.SetPopulationTransferInProgress(0);
            var dockedFirst = GameAI.ApplyColonistArrival(d, Colonist(10), dock);
            ok &= Check(!dockedFirst && d.Population.Count == 1 && d.Population[0].Player == 0 && docked.Count == 0,
                "below max: the colonist joins the population and no ship docks");
            ok &= Check(!d.IsPopulationTransferInProgress(0), "arrival clears the inbound flag");

            // Fill to max: the next colonist docks instead.
            for (var i = d.Population.Count; i < d.MaxPopulation; i++) d.Population.Add(new Planet.Inhabitant { Player = 0 });
            d.SetPopulationTransferInProgress(0);
            var dockedNow = GameAI.ApplyColonistArrival(d, Colonist(10), dock);
            ok &= Check(dockedNow && d.Population.Count == d.MaxPopulation && docked.Count == 1
                        && docked[0] == ("D", 0) && d.DockedShips.Exists(sh => sh.Kind == Ship.ShipKind.ColonyShip && sh.Owner == 0),
                "at max: a colony ship docks for the order's player and the population is unchanged");
            ok &= Check(!d.IsPopulationTransferInProgress(0), "docking also clears the inbound flag");

            // A contested planet below max still takes the colonist (the plurality rule is not the arrival rule).
            var c = s.Map.GetPlanet("C");
            for (var i = 0; i < 3; i++) c.Population.Add(new Planet.Inhabitant { Player = 1 });
            var contested = GameAI.ApplyColonistArrival(c, Colonist(10, "C"), dock);
            ok &= Check(!contested && c.Population.Count == 4, "a planet held by another player below max still takes the colonist");
        }
        return ok;
    }
}
