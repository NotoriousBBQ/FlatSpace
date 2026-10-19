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
