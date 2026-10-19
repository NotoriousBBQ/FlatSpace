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
        ok &= RunSurgeWeightCheck();
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
                ok &= Check(Near(ai.GetIndustrySituationalWeightMultiplier(colony, "D"), 2f), "a planet that serves no target keeps the plain weight");
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

                f.Map.RecomputeIncomingColonists(new List<GameAI.GameAIOrder> { Colonist(0, "B"), Colonist(0, "B") });
                ai.RefreshSurgePlan();
                ok &= Check(Near(ai.GetIndustrySituationalWeightMultiplier(colony, "A"), 2f),
                    "two colonists en route fill the room (3 + 2 = 5): no surge");
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

    public static bool RunTunableDefaultsCheck()
    {
        var c = ScriptableObject.CreateInstance<GameAIConstants>();
        try
        {
            var ok = Check(Near(c.conversionSurgeBoost, 3f), "conversionSurgeBoost defaults to 3");
            ok &= Check(Near(c.conversionSurgeShipsPerRatio, 1f), "conversionSurgeShipsPerRatio defaults to 1");
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
