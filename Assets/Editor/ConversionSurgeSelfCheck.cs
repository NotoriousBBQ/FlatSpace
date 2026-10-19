using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using FlatSpace.AI;
using Flatspace.Objects.Production;

// The colony ship surge on dominated planets: the tunables, the incoming-colonist counter, the planner, the production weight and
// the launch rule. Spec docs/superpowers/specs/2026-10-18-conversion-colony-surge-design.md.
public static class ConversionSurgeSelfCheck
{
    [MenuItem("FlatSpace/AI/Run Conversion Surge Self-Check")]
    public static void Run() => RunChecks();

    public static bool RunChecks()
    {
        var ok = RunTunableDefaultsCheck();
        ok &= RunIncomingColonistsCheck();
        ok &= RunWantedCheck();
        ok &= RunEffectiveWantedCheck();
        ok &= RunBuildCheck();
        ok &= RunReachCheck();
        ok &= RunSurgeWeightCheck();
        ok &= RunSurgeLaunchCheck();
        ok &= RunSameTurnCapCheck();
        ok &= RunTargetRoomCheck();
        ok &= RunSurgeArrivalCheck();
        ok &= RunSurgeTrackerCheck();
        Debug.Log(ok
            ? "[ConversionSurgeSelfCheck] ALL PASSED"
            : "[ConversionSurgeSelfCheck] FAILURES (see errors above)");
        return ok;
    }

    private static bool Check(bool condition, string label)
    {
        if (!condition) Debug.LogError($"[ConversionSurgeSelfCheck] FAIL: {label}");
        return condition;
    }

    private static bool Near(float a, float b) => Mathf.Abs(a - b) < 0.001f;

    private static GameAI.GameAIOrder Colonist(int player, string target, int amount = 1, int delay = 3)
        => new GameAI.GameAIOrder
        {
            Type = GameAI.GameAIOrder.OrderType.OrderTypePopulationTransport,
            TimingType = GameAI.GameAIOrder.OrderTimingType.OrderTimingTypeDelayed,
            TimingDelay = delay,
            TotalDelay = 3,
            Data = amount,
            Origin = "D",
            Target = target,
            PlayerId = player,
        };

    private static CatalogItem ColonyShipItem()
    {
        var item = ScriptableObject.CreateInstance<CatalogItem>();
        item.itemName = "Colony Ship"; item.name = "Colony Ship"; item.type = "Ship"; item.subType = "ColonyShip"; item.cost = 50f;
        return item;
    }

    private static void DockColonyShip(Planet planet, int owner)
        => planet.DockShipFromSave(Ship.ShipKind.ColonyShip, owner, new List<string>());

    // wanted = ceil(total / max(1, mine) x scale); none for an empty planet or a scale of 0.
    public static bool RunWantedCheck()
    {
        var ok = Check(ConversionSurge.Wanted(7, 1, 1f) == 7, "7 inhabitants, 1 mine: 7 ships wanted");
        ok &= Check(ConversionSurge.Wanted(7, 2, 1f) == 4, "7 inhabitants, 2 mine: 3.5 rounds up to 4");
        ok &= Check(ConversionSurge.Wanted(7, 0, 1f) == 7, "none of mine: the share is floored at 1 inhabitant");
        ok &= Check(ConversionSurge.Wanted(0, 0, 1f) == 0, "nobody there: nothing wanted");
        ok &= Check(ConversionSurge.Wanted(7, 1, 0f) == 0 && ConversionSurge.Wanted(7, 1, -1f) == 0, "a scale of 0 or below: no surge");
        ok &= Check(ConversionSurge.Wanted(6, 1, 0.75f) == 5, "6 / 1 x 0.75 = 4.5 rounds up to 5");
        return ok;
    }

    // EW = max(0, min(wanted - enRoute, room)): wanted is a total, room is what the planet can still hold.
    public static bool RunEffectiveWantedCheck()
    {
        var ok = Check(ConversionSurge.EffectiveWanted(7, 0, 3) == 3, "room binds");
        ok &= Check(ConversionSurge.EffectiveWanted(2, 1, 5) == 1, "one of the two wanted is already on its way");
        ok &= Check(ConversionSurge.EffectiveWanted(2, 2, 5) == 0, "wanted fully committed: nothing more");
        ok &= Check(ConversionSurge.EffectiveWanted(2, 3, 5) == 0, "never negative");
        ok &= Check(ConversionSurge.EffectiveWanted(5, 0, 0) == 0 && ConversionSurge.EffectiveWanted(5, 0, -1) == 0, "no room: nothing");
        return ok;
    }

    // The plan on the A-B-C-D line: B is dominated, A and C are its neighbours.
    public static bool RunBuildCheck()
    {
        var ok = true;
        var items = new List<CatalogItem>();
        try
        {
            using (var f = CombatSelfCheck.Fixture.Line())
            {
                f.Colonize("A", 0);
                f.Colonize("C", 0);
                ConversionSelfCheck.Populate(f.P("B"), (0, 1), (1, 2));      // 3 of 5: room 2, mine 1, wanted 3
                System.Func<Planet, bool> onlyB = p => p.PlanetName == "B";

                var plan = ConversionSurge.Build(f.Map, 0, onlyB);
                var b = plan.Targets.Find(t => t.Planet == "B");
                ok &= Check(plan.Targets.Count == 1 && b != null, "one dominated planet, one target");
                ok &= Check(b != null && b.Wanted == 3 && b.Room == 2 && b.EnRoute == 0 && b.EffectiveWanted == 2 && b.Pipeline == 0
                            && b.MyPop == 1 && b.TotalPop == 3, "B: wanted 3, room 2, EW 2, nothing in the pipeline, 1 of 3 mine");
                ok &= Check(b != null && b.Servers.OrderBy(n => n).SequenceEqual(new[] { "A", "C" }), "A and C serve B");
                ok &= Check(plan.For("A") == b && plan.For("C") == b && plan.For("D") == null, "For() maps a server to its target, others to null");

                DockColonyShip(f.P("A"), 0);
                var queued = ColonyShipItem();
                items.Add(queued);
                f.P("C").ProductionQueue.Add(new Planet.ProductionItem(queued));
                f.P("C").CurrentProduction = new Planet.ProductionItem(queued);
                b = ConversionSurge.Build(f.Map, 0, onlyB).Targets[0];
                ok &= Check(b.Pipeline == 3, "docked on A, plus one current and one queued colony ship on C: pipeline 3");
                f.P("C").ProductionQueue.Clear();
                f.P("C").CurrentProduction = null;
                f.P("A").DockedShips.Clear();

                f.Map.RecomputeIncomingColonists(new List<GameAI.GameAIOrder> { Colonist(0, "B") });
                b = ConversionSurge.Build(f.Map, 0, onlyB).Targets[0];
                ok &= Check(b.EnRoute == 1 && b.Room == 1 && b.EffectiveWanted == 1, "a colonist in flight: room 1, EW = min(3 - 1, 1) = 1");
                f.Map.RecomputeIncomingColonists(new List<GameAI.GameAIOrder>());

                ConversionSelfCheck.Populate(f.P("B"), (0, 1), (1, 4));     // at max population
                ok &= Check(ConversionSurge.Build(f.Map, 0, onlyB).Targets.Count == 0, "a full planet has no room: no target");
                ConversionSelfCheck.Populate(f.P("B"), (0, 1), (1, 2));

                f.P("C").Owner = 1;
                b = ConversionSurge.Build(f.Map, 0, onlyB).Targets[0];
                ok &= Check(b.Servers.SequenceEqual(new[] { "A" }), "a neighbour owned by someone else does not serve");
                f.P("C").Owner = 0;

                // C is next to both B and D; the planet that wants more ships claims it, once.
                ConversionSelfCheck.Populate(f.P("D"), (0, 1), (1, 3));      // 4 of 5: wanted 4, room 1
                plan = ConversionSurge.Build(f.Map, 0, p => p.PlanetName == "B" || p.PlanetName == "D");
                var d = plan.Targets.Find(t => t.Planet == "D");
                b = plan.Targets.Find(t => t.Planet == "B");
                ok &= Check(d != null && b != null && d.Servers.SequenceEqual(new[] { "C" }) && b.Servers.SequenceEqual(new[] { "A" }),
                    "C serves D (wanted 4 beats 3) and only D; B keeps A");
                ok &= Check(plan.For("C") == d, "For(C) is D");

                f.Constants.conversionSurgeShipsPerRatio = 0f;
                ok &= Check(ConversionSurge.Build(f.Map, 0, onlyB).Targets.Count == 0, "a scale of 0 switches the surge off");
                f.Constants.conversionSurgeShipsPerRatio = 1f;
                f.Map.Diplomacy.Enabled = false;
                ok &= Check(ConversionSurge.Build(f.Map, 0, onlyB).Targets.Count == 0, "legacy mode: no surge");
            }
        }
        finally { foreach (var item in items) Object.DestroyImmediate(item); }
        return ok;
    }

    // Servers come from the stored path tables (path nodes, the planet itself counted), not from direct neighbours.
    public static bool RunReachCheck()
    {
        var ok = true;
        using (var f = CombatSelfCheck.Fixture.Line())
        {
            f.Colonize("A", 0);
            f.Colonize("C", 0);
            f.Colonize("D", 0);
            ConversionSelfCheck.Populate(f.P("B"), (0, 1), (1, 2));      // 3 of 5: wanted 3
            System.Func<Planet, bool> onlyB = p => p.PlanetName == "B";
            System.Func<Planet, bool> onlyD = p => p.PlanetName == "D";
            ConversionSelfCheck.Populate(f.P("D"), (0, 1), (1, 2));      // D as a target: 3 of 5 as well
            f.P("D").Owner = 0;

            f.Constants.conversionSurgeReachNodes = 3;
            var b = ConversionSurge.Build(f.Map, 0, onlyB).Targets[0];
            ok &= Check(b.Servers.OrderBy(n => n).SequenceEqual(new[] { "A", "C", "D" }), "reach 3: D, three path nodes from B, serves it: " + string.Join(",", b.Servers));

            f.Constants.conversionSurgeReachNodes = 2;
            b = ConversionSurge.Build(f.Map, 0, onlyB).Targets[0];
            ok &= Check(b.Servers.OrderBy(n => n).SequenceEqual(new[] { "A", "C" }), "reach 2: direct neighbours only, as before: " + string.Join(",", b.Servers));

            f.Constants.conversionSurgeReachNodes = 1;
            b = ConversionSurge.Build(f.Map, 0, onlyB).Targets[0];
            ok &= Check(b.Servers.Count == 0, "reach 1 or below: nobody serves, no exception");

            // The far end of the line: A is four nodes from D, so it never serves D at reach 3.
            f.Constants.conversionSurgeReachNodes = 3;
            var d = ConversionSurge.Build(f.Map, 0, onlyD).Targets[0];
            ok &= Check(d.Servers.SequenceEqual(new[] { "C" }),
                "D is served by C only: B is a rival's planet and A is four nodes away: " + string.Join(",", d.Servers));

            // A planet that is empty or a rival's never serves, however close.
            f.P("C").Owner = 1;
            b = ConversionSurge.Build(f.Map, 0, onlyB).Targets[0];
            ok &= Check(!b.Servers.Contains("C") && b.Servers.Contains("A") && b.Servers.Contains("D"), "a rival-owned planet in range does not serve");
            f.P("C").Owner = -1;
            b = ConversionSurge.Build(f.Map, 0, onlyB).Targets[0];
            ok &= Check(!b.Servers.Contains("C"), "an unowned planet in range does not serve");
            f.P("C").Owner = 0;

            // A server in range of two dominated planets is claimed once, by the one that wants more ships.
            ConversionSelfCheck.Populate(f.P("D"), (0, 1), (1, 3));      // 4 of 5: wanted 4 beats B's 3
            f.P("D").Owner = 0;
            var plan = ConversionSurge.Build(f.Map, 0, p => p.PlanetName == "B" || p.PlanetName == "D");
            var tb = plan.Targets.Find(t => t.Planet == "B");
            var td = plan.Targets.Find(t => t.Planet == "D");
            ok &= Check(td != null && tb != null && td.Servers.Contains("C") && !tb.Servers.Contains("C"),
                "C is in range of both B and D: only D (wanted 4) claims it");
            ok &= Check(plan.Targets.SelectMany(t => t.Servers).GroupBy(n => n).All(g => g.Count() == 1), "no planet serves two targets");
            ok &= Check(plan.For("C") == td && td.Servers.All(n => plan.For(n) == td) && tb.Servers.All(n => plan.For(n) == tb),
                "For() agrees with Servers");
        }
        return ok;
    }

    // The ColonyShip situational weight on the neighbours of a dominated planet: today's 2 becomes 2 x 3 while the planet needs ships.
    public static bool RunSurgeWeightCheck()
    {
        var ok = true;
        var gos = new List<GameObject>();
        var items = new List<CatalogItem>();
        try
        {
            using (var f = CombatSelfCheck.Fixture.Line())
            {
                f.Constants.maxPathNodesForKnowledge = 8;
                f.Constants.expandPopulationTrigger = 0.8f;
                f.Constants.conversionSurgeBoost = 3f;
                var ai = ConversionSelfCheck.MakeAI(f, 0, gos);
                var colony = ColonyShipItem();
                items.Add(colony);
                f.Colonize("A", 0);
                f.Colonize("C", 0);
                f.Colonize("D", 0);
                ConversionSelfCheck.Populate(f.P("B"), (0, 1), (1, 2));      // 3 of 5: wanted 3, room 2, EW 2
                f.Ships("B", 0, 1);
                f.War(0, 1);
                f.Map.Knowledge.Update(f.Map, 2, 8);
                ai.RefreshSurgePlan();

                ok &= Check(Near(ai.GetIndustrySituationalWeightMultiplier(colony, "A"), 6f), "Consolidate: a neighbour of B wants colony ships at 2 x 3");
                ok &= Check(Near(ai.GetIndustrySituationalWeightMultiplier(colony, "C"), 6f), "the other neighbour too");
                f.Constants.conversionSurgeReachNodes = 2;          // D is three path nodes from B: out of reach at 2
                ai.RefreshSurgePlan();
                ok &= Check(Near(ai.GetIndustrySituationalWeightMultiplier(colony, "D"), 2f), "a planet that serves no target keeps the plain weight");
                f.Constants.conversionSurgeReachNodes = 3;
                ai.RefreshSurgePlan();
                ok &= Check(Near(ai.GetIndustrySituationalWeightMultiplier(colony, "D"), 6f), "reach 3: D, two hops from B, wants colony ships at 2 x 3");
                f.Constants.conversionSurgeReachNodes = 2;
                ai.RefreshSurgePlan();
                ai.Strategy = PlayerAI.AIStrategy.AIStrategyExpand;
                ok &= Check(Near(ai.GetIndustrySituationalWeightMultiplier(colony, "A"), 3f),
                    "Expand, planet not ready: its plain weight is 1, the surge makes it 3");
                ai.Strategy = PlayerAI.AIStrategy.AIStrategyConsolidate;

                f.Constants.conversionSurgeBoost = 1f;
                ok &= Check(Near(ai.GetIndustrySituationalWeightMultiplier(colony, "A"), 2f), "a boost of 1 switches the production surge off");
                f.Constants.conversionSurgeBoost = 3f;

                DockColonyShip(f.P("A"), 0);
                ai.RefreshSurgePlan();
                ok &= Check(ai.GetIndustrySituationalWeightMultiplier(colony, "A") == 0f, "a neighbour holding a colony ship builds none (unchanged)");
                f.P("A").DockedShips.Clear();

                f.P("C").ProductionQueue.Add(new Planet.ProductionItem(colony));
                f.P("C").ProductionQueue.Add(new Planet.ProductionItem(colony));
                ai.RefreshSurgePlan();
                ok &= Check(Near(ai.GetIndustrySituationalWeightMultiplier(colony, "A"), 2f),
                    "two ships already building (pipeline 2 = EW 2): the surge ends");
                f.P("C").ProductionQueue.Clear();

                // A colonist already flying to B sets the one-at-a-time flag, which stops B being an ordinary target; the neighbours
                // must still count it as a target while it needs ships, or the production surge would switch off for the whole flight.
                f.Map.RecomputeIncomingColonists(new List<GameAI.GameAIOrder> { Colonist(0, "B") });
                f.P("B").SetPopulationTransferInProgress(0);
                ai.RefreshSurgePlan();
                ok &= Check(Near(ai.GetIndustrySituationalWeightMultiplier(colony, "A"), 6f),
                    "one colonist in flight (flag set), room 1 and nothing building: the surge goes on (6)");
                f.P("B").SetPopulationTransferInProgress(0, false);
                f.Map.RecomputeIncomingColonists(new List<GameAI.GameAIOrder>());

                f.Map.RecomputeIncomingColonists(new List<GameAI.GameAIOrder> { Colonist(0, "B"), Colonist(0, "B") });
                ai.RefreshSurgePlan();
                // B is full once they land, so it is no longer a colonization target either: A has nothing to colonize (weight 0).
                ok &= Check(ai.GetIndustrySituationalWeightMultiplier(colony, "A") <= 2f,
                    "two colonists en route fill the room (3 + 2 = 5): no surge (and no target left)");
                f.Map.RecomputeIncomingColonists(new List<GameAI.GameAIOrder>());
            }
        }
        finally
        {
            foreach (var go in gos) Object.DestroyImmediate(go);
            foreach (var item in items) Object.DestroyImmediate(item);
        }
        return ok;
    }

    // A-B-C-D line: B dominated by player 0 (3 of 5, wanted 3, room 2), A and C ready neighbours with a docked colony ship, D empty.
    private static PlayerAI LaunchScene(CombatSelfCheck.Fixture f, List<GameObject> gos)
    {
        f.Constants.defaultTravelSpeed = 1f;
        f.Constants.maxPathNodesForKnowledge = 8;
        f.Constants.maxPathNodesForColonization = 6;
        f.Constants.expandPopulationTrigger = 0.8f;
        f.Constants.conversionColonizeWeight = 0f;          // no cost tilt: only the surge steers
        f.Constants.colonizationChokepointWeight = 0f;
        var ai = ConversionSelfCheck.MakeAI(f, 0, gos);
        ConversionSelfCheck.Populate(f.P("A"), (0, 5));
        ConversionSelfCheck.Populate(f.P("C"), (0, 5));
        DockColonyShip(f.P("A"), 0);
        DockColonyShip(f.P("C"), 0);
        ConversionSelfCheck.Populate(f.P("B"), (0, 1), (1, 2));
        f.Ships("B", 0, 1);
        f.War(0, 1);
        f.Map.Knowledge.Update(f.Map, 2, 8);
        ai.RefreshBlockadeView();
        ai.RefreshSurgePlan();
        return ai;
    }

    private static List<Planet.UpdateResult> Ready(params string[] planets)
        => planets.Select(n => new Planet.UpdateResult(n, Planet.UpdateResult.UpdateResultType.UpdateResultTypeColonizerReady, 1, 0)).ToList();

    private static List<string> Launches(List<GameAI.GameAIOrder> orders)
        => orders.Where(o => o.Type == GameAI.GameAIOrder.OrderType.OrderTypePopulationTransport)
            .Select(o => o.Origin + ">" + o.Target).OrderBy(s => s).ToList();

    // Docked ships at the neighbours of a dominated planet go there, up to the planet's effective wanted count and room.
    public static bool RunSurgeLaunchCheck()
    {
        var ok = true;
        var gos = new List<GameObject>();
        try
        {
            using (var f = CombatSelfCheck.Fixture.Line())
            {
                var ai = LaunchScene(f, gos);
                var orders = new List<GameAI.GameAIOrder>();
                ai.ProcessColonizers(Ready("A", "C"), orders);
                ok &= Check(Launches(orders).SequenceEqual(new[] { "A>B", "C>B" }),
                    "EW 2: both ready neighbours send their ship to B, though C also has the empty D in reach: " + string.Join(",", Launches(orders)));

                // EW 1 (4 of 5): one ship goes to B, the cheapest route first (ties by name); B's room is then used up, so C goes elsewhere.
                ConversionSelfCheck.Populate(f.P("B"), (0, 1), (1, 3));
                ai.RefreshSurgePlan();
                orders = new List<GameAI.GameAIOrder>();
                ai.ProcessColonizers(Ready("A", "C"), orders);
                var toB = Launches(orders).Count(s => s.EndsWith(">B"));
                ok &= Check(toB == 1 && Launches(orders).Contains("A>B"), "EW 1: exactly one colonist reaches for B (A, by name): " + string.Join(",", Launches(orders)));
                ok &= Check(Launches(orders).Contains("C>D"), "...and the ordinary rule sends C to the empty D: " + string.Join(",", Launches(orders)));
                ConversionSelfCheck.Populate(f.P("B"), (0, 1), (1, 2));

                // A colonist already in flight (the one-at-a-time flag is set) does not stop the surge; the room does.
                f.P("B").SetPopulationTransferInProgress(0);
                f.Map.RecomputeIncomingColonists(new List<GameAI.GameAIOrder> { Colonist(0, "B") });
                ai.RefreshSurgePlan();
                orders = new List<GameAI.GameAIOrder>();
                ai.ProcessColonizers(Ready("A", "C"), orders);
                ok &= Check(Launches(orders).Count(s => s.EndsWith(">B")) == 1, "one en route, room 1: one more is sent: " + string.Join(",", Launches(orders)));
                f.P("B").SetPopulationTransferInProgress(0, false);
                f.Map.RecomputeIncomingColonists(new List<GameAI.GameAIOrder>());
                ai.RefreshSurgePlan();

                // Only a ready neighbour launches (no ColonizerReady result for C).
                orders = new List<GameAI.GameAIOrder>();
                ai.ProcessColonizers(Ready("A"), orders);
                ok &= Check(Launches(orders).SequenceEqual(new[] { "A>B" }), "an unready neighbour launches nothing: " + string.Join(",", Launches(orders)));

                // A blockaded neighbour keeps its ship.
                f.Ships("C", 1, 2);
                ai.RefreshBlockadeView();
                ai.RefreshSurgePlan();
                orders = new List<GameAI.GameAIOrder>();
                ai.ProcessColonizers(Ready("A", "C"), orders);
                ok &= Check(Launches(orders).All(s => !s.StartsWith("C>")), "a blockaded neighbour launches nothing: " + string.Join(",", Launches(orders)));
            }
        }
        finally { foreach (var go in gos) Object.DestroyImmediate(go); }
        return ok;
    }

    // Several servers of one target pick a colony ship in the same production pass: the plan's room is shared, so the picks beyond
    // the effective wanted count are dropped; ordinary (unsurged) picks and other items never are.
    public static bool RunSameTurnCapCheck()
    {
        var ok = true;
        var gos = new List<GameObject>();
        var items = new List<CatalogItem>();
        try
        {
            using (var f = CombatSelfCheck.Fixture.Line())
            {
                f.Constants.maxPathNodesForKnowledge = 8;
                f.Constants.expandPopulationTrigger = 0.8f;
                f.Constants.conversionSurgeReachNodes = 2;
                f.Constants.conversionSurgeBoost = 3f;
                var ai = ConversionSelfCheck.MakeAI(f, 0, gos);
                var colony = ColonyShipItem();
                var other = ColonyShipItem();
                other.subType = "Food"; other.itemName = "Other"; other.name = "Other";
                items.Add(colony); items.Add(other);
                f.Colonize("A", 0);
                f.Colonize("C", 0);
                ConversionSelfCheck.Populate(f.P("B"), (0, 1), (1, 2));      // 3 of 5: wanted 3, room 2, EW 2
                f.Ships("B", 0, 1);
                f.War(0, 1);
                f.Map.Knowledge.Update(f.Map, 2, 8);
                ai.RefreshSurgePlan();
                ai.SnapshotSurgedServers();
                var b = ai.CurrentSurgePlan.For("A");
                ok &= Check(b != null && b.EffectiveWanted == 2 && b.Pipeline == 0, "precondition: EW 2, nothing in the pipeline");

                ok &= Check(ai.TryReserveSurgeSlot("A", colony) && ai.TryReserveSurgeSlot("C", colony), "EW 2: both servers may start a colony ship");
                ok &= Check(b.Pipeline == 2, "...and each took a slot (pipeline 2)");
                ok &= Check(!ai.TryReserveSurgeSlot("A", colony) && !ai.TryReserveSurgeSlot("C", colony), "a third pick on either server is dropped");
                ok &= Check(b.Pipeline == 2, "a dropped pick takes no slot");
                ok &= Check(ai.TryReserveSurgeSlot("A", other), "a pick that is not a colony ship is never dropped");
                ok &= Check(b.Pipeline == 2, "...and takes no slot");
                ok &= Check(ai.TryReserveSurgeSlot("D", colony) && b.Pipeline == 2, "a planet that serves no target is never dropped");

                // EW 1: the second pick is dropped.
                ConversionSelfCheck.Populate(f.P("B"), (0, 1), (1, 3));
                ai.RefreshSurgePlan();
                ai.SnapshotSurgedServers();
                ok &= Check(ai.TryReserveSurgeSlot("A", colony) && !ai.TryReserveSurgeSlot("C", colony), "EW 1: exactly one start");

                // A server whose weight was NOT surged when the snapshot was taken (the pipeline was already full) is ordinary
                // expansion and is not dropped.
                ConversionSelfCheck.Populate(f.P("B"), (0, 1), (1, 2));
                f.P("C").ProductionQueue.Add(new Planet.ProductionItem(colony));
                f.P("C").ProductionQueue.Add(new Planet.ProductionItem(colony));
                ai.RefreshSurgePlan();
                ai.SnapshotSurgedServers();
                ok &= Check(ai.TryReserveSurgeSlot("A", colony), "pipeline already at EW at snapshot time: the pick is ordinary, not dropped");
                f.P("C").ProductionQueue.Clear();

                // The snapshot is cleared by a new plan.
                ai.RefreshSurgePlan();
                ok &= Check(ai.TryReserveSurgeSlot("A", colony) && ai.TryReserveSurgeSlot("A", colony) && ai.TryReserveSurgeSlot("A", colony),
                    "RefreshSurgePlan clears the snapshot: with no snapshot nothing is dropped");
            }
        }
        finally
        {
            foreach (var go in gos) Object.DestroyImmediate(go);
            foreach (var item in items) Object.DestroyImmediate(item);
        }
        return ok;
    }

    // The ordinary target rule refuses a conversion target whose room is already spoken for by colonists in flight.
    public static bool RunTargetRoomCheck()
    {
        var ok = true;
        var gos = new List<GameObject>();
        try
        {
            using (var f = CombatSelfCheck.Fixture.Line())
            {
                var ai = LaunchScene(f, gos);
                ok &= Check(ai.IsValidColonizationTarget(f.P("B")), "precondition: B is an ordinary target");
                f.Map.RecomputeIncomingColonists(new List<GameAI.GameAIOrder> { Colonist(0, "B"), Colonist(0, "B") });
                ok &= Check(!ai.IsValidColonizationTarget(f.P("B")), "3 inhabitants + 2 in flight = max (5): no longer a target");
                f.Map.RecomputeIncomingColonists(new List<GameAI.GameAIOrder> { Colonist(0, "B") });
                ok &= Check(ai.IsValidColonizationTarget(f.P("B")), "3 + 1 in flight is below max: still a target");
            }
        }
        finally { foreach (var go in gos) Object.DestroyImmediate(go); }
        return ok;
    }

    // Two colonists to one planet: the first landing clears the one-at-a-time flag, the second still lands.
    public static bool RunSurgeArrivalCheck()
    {
        using (var f = CombatSelfCheck.Fixture.Line())
        {
            var b = f.P("B");
            ConversionSelfCheck.Populate(b, (0, 1), (1, 2));
            b.SetPopulationTransferInProgress(0);
            var docked = 0;
            var first = GameAI.ApplyColonistArrival(b, Colonist(0, "B"), (p, o) => docked++);
            var second = GameAI.ApplyColonistArrival(b, Colonist(0, "B"), (p, o) => docked++);
            var ok = Check(!first && !second && docked == 0, "both colonists land as inhabitants (room for two)");
            ok &= Check(b.Population.Count == 5 && b.Population.Count(p => p.Player == 0) == 3, "B holds 5, 3 of them mine");
            ok &= Check(!b.IsPopulationTransferInProgress(0), "the flag is clear after the arrivals");
            return ok;
        }
    }

    public static bool RunSurgeTrackerCheck()
    {
        var ok = true;
        var t = new ConversionSurgeTracker();
        ConversionSurgeTracker.Entry E(string planet, int pipeline = 0, int enRoute = 0, int servers = 2)
            => new ConversionSurgeTracker.Entry { Planet = planet, Wanted = 3, Room = 2, EnRoute = enRoute, Pipeline = pipeline, MyPop = 1, TotalPop = 3, Servers = servers };
        ok &= Check(t.Update(new[] { E("C"), E("B") }).Select(e => e.Planet).SequenceEqual(new[] { "B", "C" }), "the first sighting is reported, ordered by name");
        ok &= Check(t.Update(new[] { E("C"), E("B") }).Count == 0, "the same numbers again: not reported");
        ok &= Check(t.Update(new[] { E("C"), E("B", 1) }).Select(e => e.Planet).SequenceEqual(new[] { "B" }), "a changed pipeline is reported");
        ok &= Check(t.Update(new[] { E("C"), E("B", 1, 1) }).Count == 1, "a changed en-route count is reported");
        ok &= Check(t.Update(new[] { E("C"), E("B", 1, 1, 3) }).Select(e => e.Planet).SequenceEqual(new[] { "B" }), "a changed server count is reported");
        t.Update(new ConversionSurgeTracker.Entry[0]);
        ok &= Check(t.Update(new[] { E("B", 1, 1) }).Count == 1, "a planet that left the plan is forgotten, so it is reported afresh");
        t.Clear();
        ok &= Check(t.Update(new[] { E("B", 1, 1) }).Count == 1, "Clear forgets everything");
        return ok;
    }

    public static bool RunTunableDefaultsCheck()
    {
        var c = ScriptableObject.CreateInstance<GameAIConstants>();
        try
        {
            var ok = Check(Near(c.conversionSurgeBoost, 3f), "conversionSurgeBoost defaults to 3");
            ok &= Check(Near(c.conversionSurgeShipsPerRatio, 1f), "conversionSurgeShipsPerRatio defaults to 1");
            ok &= Check(c.conversionSurgeReachNodes == 3, "conversionSurgeReachNodes defaults to 3");
            return ok;
        }
        finally { Object.DestroyImmediate(c); }
    }

    // The derived counter of my colonists in flight toward a planet: per player, rebuilt from the in-flight orders every time.
    public static bool RunIncomingColonistsCheck()
    {
        using (var f = CombatSelfCheck.Fixture.Line())
        {
            var orders = new List<GameAI.GameAIOrder>
            {
                Colonist(0, "A"), Colonist(0, "A", 2), Colonist(1, "A"), Colonist(0, "B"),
                new GameAI.GameAIOrder { Type = GameAI.GameAIOrder.OrderType.OrderTypeFoodTransport, Target = "A", PlayerId = 0, Data = 5 },
            };
            f.Map.RecomputeIncomingColonists(orders);
            var ok = Check(f.P("A").GetIncomingColonists(0) == 3, "player 0 has 1 + 2 colonists inbound at A");
            ok &= Check(f.P("A").GetIncomingColonists(1) == 1, "player 1 has 1 inbound at A (counted per player)");
            ok &= Check(f.P("B").GetIncomingColonists(0) == 1, "one inbound at B");
            ok &= Check(f.P("C").GetIncomingColonists(0) == 0, "nothing inbound at C");
            f.Map.RecomputeIncomingColonists(orders.GetRange(0, 1));
            ok &= Check(f.P("A").GetIncomingColonists(0) == 1 && f.P("B").GetIncomingColonists(0) == 0,
                "a second recompute replaces the counts, it does not add");
            f.Map.RecomputeIncomingColonists(new List<GameAI.GameAIOrder>());
            ok &= Check(f.P("A").GetIncomingColonists(0) == 0, "no orders: zero");
            return ok;
        }
    }
}
