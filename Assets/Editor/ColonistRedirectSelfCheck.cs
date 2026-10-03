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
}
