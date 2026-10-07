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
        ok &= RunConversionHostilityCheck();
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

    private static PlayerAI MakeAI(CombatSelfCheck.Fixture f, int id, List<GameObject> gos)
    {
        var go = new GameObject("ConversionPlayer" + id);
        gos.Add(go);
        var player = go.AddComponent<Player>();
        var ai = go.AddComponent<PlayerAI>();
        ai.Player = player;
        ai.AIMap = f.Map;
        player.playerID = id;
        ai.ResearchCatalog = go.AddComponent<Catalog>();
        ai.ResearchCatalog.catalogItems = f.Research;
        ai.Strategy = PlayerAI.AIStrategy.AIStrategyConsolidate;
        return ai;
    }

    // The charge waiting on DiplomacyState, the calculator term, and the victim's UpdateDiplomacy adding exactly the charge.
    public static bool RunConversionHostilityCheck()
    {
        var ok = true;
        var d = new DiplomacyState();
        d.RecordConversion(2, 0); d.RecordConversion(2, 0); d.RecordConversion(1, 0);
        ok &= Check(d.PeekConversions(2, 0) == 2 && d.PeekConversions(1, 0) == 1, "charges are counted per (victim, converter)");
        d.RecordConversion(2, 2); d.RecordConversion(2, -1);
        ok &= Check(d.PeekConversions(2, 2) == 0 && d.PeekConversions(2, -1) == 0, "a self or ownerless converter is ignored");
        ok &= Check(d.TakeConversions(2, 0) == 2 && d.TakeConversions(2, 0) == 0, "taking consumes the charges");
        d.DiscardConversions(1);
        ok &= Check(d.PeekConversions(1, 0) == 0, "DiscardConversions drops what nobody consumed");

        var c = ScriptableObject.CreateInstance<GameAIConstants>();
        try
        {
            var r = HostilityCalculator.Compute(new HostilityCalculator.Inputs { Previous = 10f, Conversions = 2 }, c);
            ok &= Check(Near(r.ConversionTerm, 6f), "two conversions at hostilityPerConversion 3 give a term of 6");
            ok &= Check(Near(r.Hostility, 10f * (1f - c.hostilityDecay) + 6f), "the term is added after the decay");
        }
        finally { Object.DestroyImmediate(c); }

        // The victim reads exactly the charge in its UpdateDiplomacy: the same fixture with and without two charges.
        float HostilityAfter(int conversions)
        {
            var gos = new List<GameObject>();
            try
            {
                using (var f = CombatSelfCheck.Fixture.Line())
                {
                    f.Colonize("A", 2);
                    f.Ships("A", 0, 1);                                   // player 0's ship at player 2's planet: contact
                    f.Map.Knowledge.Update(f.Map, 3, 8);
                    var pair = f.Map.Diplomacy.Get(2, 0);
                    pair.Hostility = 50f;
                    f.Map.Diplomacy.Set(2, 0, pair);
                    for (var i = 0; i < conversions; i++) f.Map.Diplomacy.RecordConversion(2, 0);
                    MakeAI(f, 2, gos).UpdateDiplomacy(10, new List<GameAI.GameAIOrder>());
                    return f.Map.Diplomacy.Get(2, 0).Hostility;
                }
            }
            finally { foreach (var go in gos) Object.DestroyImmediate(go); }
        }
        ok &= Check(Near(HostilityAfter(2) - HostilityAfter(0), 6f), "UpdateDiplomacy adds exactly 2 x hostilityPerConversion for two charges");
        return ok;
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
