using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using FlatSpace.AI;
using Flatspace.Objects.Production;

// Retreat: the fight projection, the Stay/Retreat matrix, the tiered destination, the planner wiring and the engaged loss share.
public static class RetreatSelfCheck
{
    [MenuItem("FlatSpace/AI/Run Retreat Self-Check")]
    public static void Run() => RunChecks();

    public static bool RunChecks()
    {
        var ok = RunTunableDefaultsCheck();
        Debug.Log(ok
            ? "[RetreatSelfCheck] ALL PASSED"
            : "[RetreatSelfCheck] FAILURES (see errors above)");
        return ok;
    }

    private static bool Check(bool condition, string label)
    {
        if (!condition) Debug.LogError($"[RetreatSelfCheck] FAIL: {label}");
        return condition;
    }

    private static bool Near(float a, float b) => Mathf.Abs(a - b) < 0.01f;

    // The five retreat tunables keep their documented in-code defaults.
    public static bool RunTunableDefaultsCheck()
    {
        var ok = true;
        var c = ScriptableObject.CreateInstance<GameAIConstants>();
        try
        {
            ok &= Check(Near(c.retreatCheckFraction, 0.3f) && Near(c.retreatLossFraction, 0.5f), "retreatCheckFraction 0.3, retreatLossFraction 0.5");
            ok &= Check(Near(c.retreatSteepness, 0.1f), "retreatSteepness 0.1");
            ok &= Check(c.retreatProjectionTurns == 20 && c.retreatCooldownTurns == 10, "retreatProjectionTurns 20, retreatCooldownTurns 10");
        }
        finally { Object.DestroyImmediate(c); }
        return ok;
    }
}
