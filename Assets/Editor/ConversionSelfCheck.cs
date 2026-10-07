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
        ok &= RunDominatorCheck();
        ok &= RunPaceCheck();
        ok &= RunStartGateCheck();
        ok &= RunVictimOrderCheck();
        ok &= RunSessionEndsCheck();
        ok &= RunOwnershipFlipCheck();
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

    private static List<ConversionReport> Turn(CombatSelfCheck.Fixture f, int turn, List<Planet.UpdateResult> results = null)
    {
        f.Map.Diplomacy.Turn = turn;
        return ConversionSystem.Resolve(f.Map, f.Stats, f.Constants, turn, results ?? new List<Planet.UpdateResult>());
    }

    private static int Count(Planet planet, int player) => planet.Population.Count(p => p.Player == player);

    private static int Flips(IEnumerable<ConversionReport> reports) => reports.Count(r => r.What == ConversionReport.Kind.Flip);

    // Dominance: warships docked with offense, no at-war rival warship; two non-war dominators: more offense, then the lower id.
    public static bool RunDominatorCheck()
    {
        var ok = true;
        using (var f = CombatSelfCheck.Fixture.Line())
        {
            var a = f.P("A");
            ok &= Check(ConversionSystem.Dominator(a, f.Map, f.Stats) == Planet.NoOwner, "no ships: nobody dominates");
            f.Ships("A", 0, 1);
            ok &= Check(ConversionSystem.Dominator(a, f.Map, f.Stats) == 0, "one warship: its owner dominates");
            f.Ships("A", 1, 1);
            ok &= Check(ConversionSystem.Dominator(a, f.Map, f.Stats) == 0, "two players at peace, equal offense: the lower id dominates");
            f.Ships("A", 1, 1);
            ok &= Check(ConversionSystem.Dominator(a, f.Map, f.Stats) == 1, "two players at peace: the one with more offense dominates");
            f.War(0, 1);
            ok &= Check(ConversionSystem.Dominator(a, f.Map, f.Stats) == Planet.NoOwner, "at-war warships on the same planet: nobody dominates");
        }
        using (var f = CombatSelfCheck.Fixture.Line())
        {
            f.Ships("A", 0, 1, 100f);     // 100 damage on a Health stat of 100: health 0, offense 0
            ok &= Check(ConversionSystem.Dominator(f.P("A"), f.Map, f.Stats) == Planet.NoOwner, "a ship with no health left does not dominate");
            WarshipSelfCheck.DockWarships(f.P("B"), Planet.NoOwner, 2);
            ok &= Check(ConversionSystem.Dominator(f.P("B"), f.Map, f.Stats) == Planet.NoOwner, "ownerless ships never dominate");
        }
        return ok;
    }

    // turnsPerFlip = max(1, N x (1 - my share)); progress carries; the snowball shortens the second flip.
    public static bool RunPaceCheck()
    {
        var ok = true;
        var c = ScriptableObject.CreateInstance<GameAIConstants>();
        try
        {
            c.conversionTurnsBase = 6f;
            ok &= Check(Near(ConversionSystem.TurnsPerFlip(0, 4, c), 6f), "no inhabitants of mine: N turns");
            ok &= Check(Near(ConversionSystem.TurnsPerFlip(1, 4, c), 4.5f), "1 of 4: 6 x 0.75 = 4.5");
            ok &= Check(Near(ConversionSystem.TurnsPerFlip(9, 10, c), 1f), "9 of 10: 0.6 is floored at 1 turn");
            ok &= Check(Near(ConversionSystem.TurnsPerFlip(0, 0, c), 6f), "an empty planet does not divide by zero");
        }
        finally { Object.DestroyImmediate(c); }

        using (var f = CombatSelfCheck.Fixture.Line())
        {
            f.Constants.conversionTurnsBase = 4f;     // 1 / 4 is exact in binary: the first flip lands on turn 4
            var a = f.P("A");
            Populate(a, (1, 4));
            f.Ships("A", 0, 1); f.War(0, 1);
            for (var t = 1; t <= 3; t++)
                ok &= Check(Flips(Turn(f, t)) == 0 && Count(a, 1) == 4, $"turn {t}: no flip yet");
            ok &= Check(Near(a.ConversionProgress, 0.75f) && a.ConversionBy == 0, "after 3 turns: progress 0.75 in a session of player 0");
            var fourth = Turn(f, 4);
            ok &= Check(Flips(fourth) == 1 && Count(a, 1) == 3 && Count(a, 0) == 1 && a.Owner == 1, "turn 4: one flip, 3 : 1, the owner stays player 1");
            ok &= Check(Near(a.ConversionProgress, 0f), "the carry after the flip is 0");
            for (var t = 5; t <= 6; t++) Turn(f, t);
            ok &= Check(Count(a, 1) == 3, "with 1 of 4 mine the next flip takes 3 turns, so none yet after 2");
            for (var t = 7; t <= 8; t++) Turn(f, t);
            ok &= Check(Count(a, 1) == 2, "the snowball: the second flip lands within 4 turns of the first (3 needed), faster than the first's 4");
        }
        return ok;
    }

    // A session starts only with dominance and a holder at war; nothing happens on an empty or all-mine planet.
    public static bool RunStartGateCheck()
    {
        var ok = true;
        using (var f = CombatSelfCheck.Fixture.Line())
        {
            f.Constants.conversionTurnsBase = 1f;
            var a = f.P("A");
            Populate(a, (1, 4));
            f.Ships("A", 0, 1);                                                  // no stance: peace
            for (var t = 1; t <= 3; t++) Turn(f, t);
            ok &= Check(a.ConversionBy == Planet.NoOwner && Count(a, 1) == 4, "at peace: no session, no flip");

            Populate(a, (2, 4));
            f.War(0, 1);                                                         // at war with 1, but only player 2 lives here
            for (var t = 4; t <= 6; t++) Turn(f, t);
            ok &= Check(a.ConversionBy == Planet.NoOwner && Count(a, 2) == 4, "only a non-war holder: a session never starts");

            Populate(a);                                                         // nobody lives here
            ok &= Check(Turn(f, 7).Count == 0 && a.ConversionBy == Planet.NoOwner, "an empty planet: nothing happens");
            Populate(a, (0, 3));
            ok &= Check(Turn(f, 8).Count == 0 && a.ConversionBy == Planet.NoOwner, "only the dominator's own inhabitants: nothing happens");
        }
        using (var f = CombatSelfCheck.Fixture.Line())
        {
            f.Constants.conversionTurnsBase = 1f;
            Populate(f.P("A"), (1, 4));
            f.Ships("A", 0, 1, 100f); f.War(0, 1);                               // a dead ship dominates nothing
            Turn(f, 1);
            ok &= Check(f.P("A").ConversionBy == Planet.NoOwner, "a ship with no health left starts no session");
        }
        using (var f = CombatSelfCheck.Fixture.Line())
        {
            f.Constants.conversionTurnsBase = 1f;
            Populate(f.P("A"), (1, 4));
            f.Ships("A", 0, 1); f.War(0, 1);
            f.Map.Diplomacy.Enabled = false;                                     // legacy mode
            ok &= Check(Turn(f, 1).Count == 0 && f.P("A").ConversionBy == Planet.NoOwner && Count(f.P("A"), 1) == 4, "legacy mode: nothing converts");
        }
        return ok;
    }

    // At-war holders first (largest, then the lower id), then the non-war tail; a charge only for the non-war victim.
    public static bool RunVictimOrderCheck()
    {
        var ok = true;
        using (var f = CombatSelfCheck.Fixture.Line())
        {
            f.Constants.conversionTurnsBase = 1f;                                // every turn is exactly one flip
            var a = f.P("A");
            Populate(a, (1, 2), (2, 3));
            f.Ships("A", 0, 1); f.War(0, 1);
            Turn(f, 1);
            ok &= Check(Count(a, 1) == 1 && Count(a, 2) == 3 && Count(a, 0) == 1, "the at-war holder converts first although the non-war holder has more");
            Turn(f, 2);
            ok &= Check(Count(a, 1) == 0 && Count(a, 2) == 3 && Count(a, 0) == 2, "its last inhabitant converts next");
            Turn(f, 3);
            ok &= Check(Count(a, 2) == 2 && a.ConversionBy == 0, "the tail: the session goes on against the non-war holder while my fleet stays");
            Turn(f, 4);
            var last = Turn(f, 5);
            ok &= Check(Count(a, 2) == 0 && Count(a, 0) == 5 && a.Owner == 0, "clean after five flips and the planet is mine");
            ok &= Check(a.ConversionBy == Planet.NoOwner && last.Any(r => r.What == ConversionReport.Kind.End && r.EndReason == ConversionEndReason.Clean),
                "the session ends Clean in the turn of the last flip");
            ok &= Check(f.Map.Diplomacy.PeekConversions(2, 0) == 3 && f.Map.Diplomacy.PeekConversions(1, 0) == 0,
                "a charge for each of the non-war player's 3 inhabitants and none for the at-war player's");
        }
        using (var f = CombatSelfCheck.Fixture.Line())
        {
            f.Constants.conversionTurnsBase = 1f;
            var a = f.P("A");
            Populate(a, (1, 2), (2, 2));
            f.Ships("A", 0, 1); f.War(0, 1); f.War(0, 2);
            Turn(f, 1);
            ok &= Check(Count(a, 1) == 1 && Count(a, 2) == 2, "two at-war holders tied at 2: the lower id converts first");
        }
        return ok;
    }

    // Progress resets and the session ends when dominance, or the last war with a player of the session, ends.
    public static bool RunSessionEndsCheck()
    {
        var ok = true;
        using (var f = CombatSelfCheck.Fixture.Line())     // a truce with the only at-war holder, a non-war holder remaining
        {
            f.Constants.conversionTurnsBase = 4f;
            var a = f.P("A");
            Populate(a, (1, 2), (2, 2));
            f.Ships("A", 0, 1); f.War(0, 1);
            Turn(f, 1); Turn(f, 2);
            ok &= Check(a.ConversionBy == 0 && Near(a.ConversionProgress, 0.5f) && a.ConversionWarMask == (1 << 1), "precondition: a session with progress 0.5 and player 1 in the war mask");
            var pair = f.Map.Diplomacy.Get(0, 1);
            pair.TruceUntil = 100;
            f.Map.Diplomacy.Set(0, 1, pair);
            var reports = Turn(f, 3);
            ok &= Check(reports.Any(r => r.What == ConversionReport.Kind.End && r.EndReason == ConversionEndReason.WarEnded), "a truce ends the session (WarEnded)");
            ok &= Check(a.ConversionBy == Planet.NoOwner && Near(a.ConversionProgress, 0f), "progress is back to 0");
            ok &= Check(!reports.Any(r => r.What == ConversionReport.Kind.Start) && Count(a, 2) == 2 && Count(a, 1) == 2,
                "the non-war tail does not continue, and nothing new starts");
        }
        using (var f = CombatSelfCheck.Fixture.Line())     // a rival warship arrives
        {
            f.Constants.conversionTurnsBase = 4f;
            var a = f.P("A");
            Populate(a, (1, 4));
            f.Ships("A", 0, 1); f.War(0, 1);
            Turn(f, 1); Turn(f, 2);
            f.Ships("A", 1, 1);
            var reports = Turn(f, 3);
            ok &= Check(reports.Any(r => r.What == ConversionReport.Kind.End && r.EndReason == ConversionEndReason.DominanceLost)
                        && a.ConversionBy == Planet.NoOwner && Near(a.ConversionProgress, 0f),
                "an at-war rival warship arriving ends the session (DominanceLost) and resets progress");
        }
        using (var f = CombatSelfCheck.Fixture.Line())     // the fleet leaves
        {
            f.Constants.conversionTurnsBase = 4f;
            var a = f.P("A");
            Populate(a, (1, 4));
            f.Ships("A", 0, 1); f.War(0, 1);
            Turn(f, 1); Turn(f, 2);
            a.DockedShips.Clear();
            var reports = Turn(f, 3);
            ok &= Check(reports.Any(r => r.What == ConversionReport.Kind.End && r.EndReason == ConversionEndReason.DominanceLost)
                        && a.ConversionBy == Planet.NoOwner && Near(a.ConversionProgress, 0f),
                "the fleet leaving ends the session and resets progress");
            f.Ships("A", 0, 1);
            Turn(f, 4);
            ok &= Check(Near(a.ConversionProgress, 0.25f), "a fleet that returns starts again from 0 (one turn of progress)");
        }
        return ok;
    }

    // An ownership change clears production and is reported with the cleared item; the result carries the event to the victim.
    public static bool RunOwnershipFlipCheck()
    {
        var ok = true;
        var item = ScriptableObject.CreateInstance<CatalogItem>();
        item.itemName = "Test Item";
        try
        {
            using (var f = CombatSelfCheck.Fixture.Line())
            {
                f.Constants.conversionTurnsBase = 1f;
                var a = f.P("A");
                Populate(a, (1, 2));
                a.CurrentProduction = new Planet.ProductionItem(item);
                f.Ships("A", 0, 1); f.War(0, 1);
                var results = new List<Planet.UpdateResult>();
                var first = Turn(f, 1, results);
                var flip = first.First(r => r.What == ConversionReport.Kind.Flip).Flip;
                ok &= Check(flip.OwnerChanged && flip.OldOwner == 1 && flip.NewOwner == Planet.NoOwner && flip.ClearedItem == "Test Item"
                            && flip.AtWar && flip.From == 1 && flip.MyPopulation == 1 && flip.TotalPopulation == 2,
                    "1:1 is a tie: the owner changes from 1 to NoOwner and the item is reported as cleared");
                ok &= Check(a.CurrentProduction == null, "production was cleared");
                ok &= Check(results.Count == 1 && results[0].Result == Planet.UpdateResult.UpdateResultType.UpdateResultTypeConversion
                            && results[0].PlayerID == 1 && results[0].Data is ConversionEvent,
                    "one Conversion result per flip, for the victim, carrying the event");
                var second = Turn(f, 2);
                var flip2 = second.First(r => r.What == ConversionReport.Kind.Flip).Flip;
                ok &= Check(flip2.OwnerChanged && flip2.NewOwner == 0 && flip2.ClearedItem == null && a.Owner == 0, "the second flip gives the planet to player 0; nothing left to clear");
            }
            using (var f = CombatSelfCheck.Fixture.Line())
            {
                f.Constants.conversionTurnsBase = 1f;
                var a = f.P("A");
                Populate(a, (1, 4));
                a.CurrentProduction = new Planet.ProductionItem(item);
                f.Ships("A", 0, 1); f.War(0, 1);
                var flip = Turn(f, 1).First(r => r.What == ConversionReport.Kind.Flip).Flip;
                ok &= Check(!flip.OwnerChanged && flip.ClearedItem == null && a.CurrentProduction != null, "no ownership change: the production item stays");
            }
        }
        finally { Object.DestroyImmediate(item); }
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
