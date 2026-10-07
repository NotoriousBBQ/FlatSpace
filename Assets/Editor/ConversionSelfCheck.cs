using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using FlatSpace.AI;
using Flatspace.Objects.Production;

// Invasion by dominance and conversion: the planet session state, the conversion rule, the hostility charge, the saves, the
// holds, the colonization rule and the log trackers. Spec docs/superpowers/specs/2026-10-06-invasion-conversion-design.md.
public static class ConversionSelfCheck
{
    [MenuItem("FlatSpace/AI/Run Conversion Self-Check")]
    public static void Run() => RunChecks();

    public static bool RunChecks()
    {
        var ok = RunTunableDefaultsCheck();
        ok &= RunConvertInhabitantCheck();
        ok &= RunSessionStateCheck();
        Debug.Log(ok
            ? "[ConversionSelfCheck] ALL PASSED"
            : "[ConversionSelfCheck] FAILURES (see errors above)");
        return ok;
    }

    private static bool Check(bool condition, string label)
    {
        if (!condition) Debug.LogError($"[ConversionSelfCheck] FAIL: {label}");
        return condition;
    }

    private static bool Near(float a, float b) => Mathf.Abs(a - b) < 0.001f;

    /// <summary>Replaces a planet's inhabitants (one list entry each) and sets its owner as the game does.</summary>
    public static void Populate(Planet planet, params (int player, int count)[] holders)
    {
        planet.Population.Clear();
        foreach (var (player, count) in holders)
            for (var i = 0; i < count; i++)
                planet.Population.Add(new Planet.Inhabitant { Player = player });
        planet.Owner = planet.PlayerWithMostPopulation();
    }

    public static bool RunTunableDefaultsCheck()
    {
        var c = ScriptableObject.CreateInstance<GameAIConstants>();
        try
        {
            var ok = Check(Near(c.conversionTurnsBase, 6f), "conversionTurnsBase defaults to 6");
            ok &= Check(Near(c.hostilityPerConversion, 3f), "hostilityPerConversion defaults to 3");
            ok &= Check(Near(c.conversionColonizeWeight, 0.5f), "conversionColonizeWeight defaults to 0.5");
            return ok;
        }
        finally { Object.DestroyImmediate(c); }
    }

    // One inhabitant flips; only an ownership change clears the production item and queue (no refund).
    public static bool RunConvertInhabitantCheck()
    {
        var ok = true;
        var item = ScriptableObject.CreateInstance<CatalogItem>();
        item.itemName = "Test Item";
        try
        {
            using (var f = CombatSelfCheck.Fixture.Line())
            {
                var a = f.P("A");

                Populate(a, (1, 2), (0, 1));
                a.CurrentProduction = new Planet.ProductionItem(item);
                a.ProductionQueue.Add(new Planet.ProductionItem(item));
                ok &= Check(a.Owner == 1, "precondition: player 1 owns A (2 against 1)");
                var changed = a.ConvertInhabitant(1, 0);
                ok &= Check(changed && a.Owner == 0, "2:1 becomes 1:2 after one flip: the owner changes to player 0");
                ok &= Check(a.CurrentProduction == null && a.ProductionQueue.Count == 0, "an ownership change clears the production item and the queue");

                Populate(a, (1, 4));
                a.CurrentProduction = new Planet.ProductionItem(item);
                a.ProductionQueue.Add(new Planet.ProductionItem(item));
                changed = a.ConvertInhabitant(1, 0);
                ok &= Check(!changed && a.Owner == 1 && a.Population.Count(p => p.Player == 0) == 1, "4:0 becomes 3:1: the owner stays player 1");
                ok &= Check(a.CurrentProduction != null && a.ProductionQueue.Count == 1, "no ownership change: production is untouched");

                Populate(a, (1, 3), (0, 1));
                a.CurrentProduction = new Planet.ProductionItem(item);
                changed = a.ConvertInhabitant(1, 0);
                ok &= Check(changed && a.Owner == Planet.NoOwner && a.CurrentProduction == null,
                    "3:1 becomes 2:2, a tie: the owner becomes NoOwner, which is a change, so production is cleared");

                Populate(a, (1, 2));
                a.CurrentProduction = new Planet.ProductionItem(item);
                changed = a.ConvertInhabitant(2, 0);
                ok &= Check(!changed && a.Population.Count(p => p.Player == 1) == 2 && a.CurrentProduction != null,
                    "no inhabitant of the named player: nothing changes");
            }
        }
        finally { Object.DestroyImmediate(item); }
        return ok;
    }

    // The saved form is player id + 1 so that 0, what an older save reads back, means "no session".
    public static bool RunSessionStateCheck()
    {
        var ok = true;
        using (var f = CombatSelfCheck.Fixture.Line())
        {
            var a = f.P("A");
            ok &= Check(a.ConversionBy == Planet.NoOwner && Near(a.ConversionProgress, 0f) && a.ConversionWarMask == 0 && a.SavedConversionBy == 0,
                "a fresh planet has no session and saves as 0");
            a.ConversionBy = 2; a.ConversionProgress = 0.5f; a.ConversionWarMask = 3;
            ok &= Check(a.SavedConversionBy == 3, "player 2 saves as 3");
            a.RestoreConversion(3, 0.5f, 3);
            ok &= Check(a.ConversionBy == 2 && Near(a.ConversionProgress, 0.5f) && a.ConversionWarMask == 3, "a saved 3 restores player 2, its progress and mask");
            a.RestoreConversion(0, 0.7f, 5);
            ok &= Check(a.ConversionBy == Planet.NoOwner && Near(a.ConversionProgress, 0f) && a.ConversionWarMask == 0,
                "a saved 0 (also an older save) restores no session whatever else rides along");
            a.ConversionBy = 1; a.ConversionProgress = 0.4f; a.ConversionWarMask = 2;
            a.EndConversionSession();
            ok &= Check(a.ConversionBy == Planet.NoOwner && Near(a.ConversionProgress, 0f) && a.ConversionWarMask == 0, "EndConversionSession clears all three");
        }
        return ok;
    }
}
