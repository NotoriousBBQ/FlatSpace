using UnityEditor;
using UnityEngine;
using FlatSpace.AI;

public static class PlayerValueSelfCheck
{
    [MenuItem("FlatSpace/AI/Run Player Value Self-Check")]
    public static void Run() => RunChecks();

    public static bool RunChecks()
    {
        var ok = RunShipCostChecks();
        ok &= RunHealthFractionChecks();
        ok &= RunDefaultsCheck();
        Debug.Log(ok
            ? "[PlayerValueSelfCheck] ALL PASSED"
            : "[PlayerValueSelfCheck] FAILURES (see errors above)");
        return ok;
    }

    private static bool Check(bool condition, string label)
    {
        if (!condition) Debug.LogError($"[PlayerValueSelfCheck] FAIL: {label}");
        return condition;
    }

    private static bool Near(float a, float b) => Mathf.Abs(a - b) < 0.001f;

    public static bool RunShipCostChecks()
    {
        var ok = true;
        ok &= Check(Near(WarshipCosts.ShipCost(Ship.ShipKind.WarShip, 0, 100f, 300f, 0.1f, 1.5f, 1.5f), 100f), "warship with no improvements costs 100");
        ok &= Check(Near(WarshipCosts.ShipCost(Ship.ShipKind.WarShip, 3, 100f, 300f, 0.1f, 1.5f, 1.5f), 130f), "warship with 3 improvements costs 130");
        ok &= Check(Near(WarshipCosts.ShipCost(Ship.ShipKind.SiegeShip, 3, 100f, 300f, 0.1f, 1.5f, 1.5f), 195f), "siegeship with 3 improvements costs 195");
        ok &= Check(Near(WarshipCosts.ShipCost(Ship.ShipKind.Bunker, 0, 100f, 300f, 0.1f, 1.5f, 1.5f), 150f), "bunker with no improvements costs 150");
        ok &= Check(Near(WarshipCosts.ShipCost(Ship.ShipKind.ColonyShip, 5, 100f, 300f, 0.1f, 1.5f, 1.5f), 300f), "colony ship costs its catalog cost");
        return ok;
    }

    public static bool RunHealthFractionChecks()
    {
        var ok = true;
        var stats = new WarshipStats(null);
        var t = WarshipSelfCheck.MakeTemplate();
        ok &= Check(Near(stats.HealthFraction(t, null, 0f, "Warship"), 1f), "no damage is a full fraction");
        ok &= Check(Near(stats.HealthFraction(t, null, 25f, "Warship"), 0.75f), "25 damage of 100 leaves 0.75");
        ok &= Check(Near(stats.HealthFraction(t, null, 150f, "Warship"), 0f), "damage beyond health leaves 0");
        t.shipHealth = 0f;
        ok &= Check(Near(stats.HealthFraction(t, null, 10f, "Warship"), 1f), "a Health stat of 0 reads as 1");
        Object.DestroyImmediate(t);
        ok &= Check(Near(stats.HealthFraction(null, null, 10f, "Warship"), 1f), "a null template reads as 1");
        return ok;
    }

    public static bool RunDefaultsCheck()
    {
        var c = ScriptableObject.CreateInstance<GameAIConstants>();
        var ok = Check(Near(c.fleetValueDivisor, 50f), "fleetValueDivisor defaults to 50");
        ok &= Check(Near(c.techValueDivisor, 2000f), "techValueDivisor defaults to 2000");
        Object.DestroyImmediate(c);
        return ok;
    }
}
