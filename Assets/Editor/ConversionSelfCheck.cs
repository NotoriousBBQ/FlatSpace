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
        ok &= RunConversionTrackerCheck();
        ok &= RunConversionSaveCheck();
        ok &= RunConversionHoldCheck();
        ok &= RunConversionColonizeCheck();
        ok &= RunConversionHoldTrackerCheck();
        ok &= RunConversionHoldAuditCheck();
        ok &= RunCapitolLostCheck();
        ok &= RunConversionKeepCheck();
        ok &= RunConversionPlannerKeepCheck();
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

    // Log-only state: sessions in progress and players already reported out of planets. The logger itself is verified by a Play run.
    public static bool RunConversionTrackerCheck()
    {
        var ok = true;
        var t = new ConversionTracker();
        ok &= Check(!t.Tracking("A"), "nothing is tracked at first");
        t.Begin("A", 10);
        t.CountFlip("A"); t.CountFlip("A");
        ok &= Check(t.Tracking("A"), "a begun session is tracked");
        var done = t.Finish("A", 17);
        ok &= Check(done.turnsHeld == 7 && done.converted == 2 && !t.Tracking("A"), "finishing reports the turns held and the flips, and forgets it");
        var unknown = t.Finish("B", 20);
        ok &= Check(unknown.turnsHeld == 0 && unknown.converted == 0, "a session never seen (a load) finishes as 0 and 0");
        t.CountFlip("C");
        ok &= Check(!t.Tracking("C"), "a flip for an untracked planet is ignored");
        ok &= Check(t.NoteOutOfPlanets(2) && !t.NoteOutOfPlanets(2) && t.NoteOutOfPlanets(3), "a player is reported out of planets once");
        t.Begin("D", 1); t.NoteOutOfPlanets(5);
        t.Clear();
        ok &= Check(!t.Tracking("D") && t.NoteOutOfPlanets(2), "Clear forgets sessions and reported players");
        return ok;
    }

    // The save fields survive JsonUtility, and an older save (no keys) loads as "no session", never as player 0 converting.
    public static bool RunConversionSaveCheck()
    {
        var ok = true;
        using (var f = CombatSelfCheck.Fixture.Line())
        {
            var a = f.P("A");
            a.ConversionBy = 2; a.ConversionProgress = 0.5f; a.ConversionWarMask = 3;
            var save = new SaveLoadSystem.GameSave.PlanetSave
            {
                name = "A", conversionBy = a.SavedConversionBy, conversionProgress = a.ConversionProgress, conversionWarMask = a.ConversionWarMask,
            };
            var back = JsonUtility.FromJson<SaveLoadSystem.GameSave.PlanetSave>(JsonUtility.ToJson(save));
            var b = f.P("B");
            b.RestoreConversion(back.conversionBy, back.conversionProgress, back.conversionWarMask);
            ok &= Check(b.ConversionBy == 2 && Near(b.ConversionProgress, 0.5f) && b.ConversionWarMask == 3, "a session survives the JSON round trip");

            var old = JsonUtility.FromJson<SaveLoadSystem.GameSave.PlanetSave>("{\"name\":\"A\"}");
            var c = f.P("C");
            c.RestoreConversion(old.conversionBy, old.conversionProgress, old.conversionWarMask);
            ok &= Check(old.conversionBy == 0 && c.ConversionBy == Planet.NoOwner && Near(c.ConversionProgress, 0f), "an older save with no keys loads with no session");
        }
        return ok;
    }

    // A planet I dominate (and convert, or could) is held: the home plan never strips it. The non-war tail has no assault target,
    // so only the hold keeps the fleet there.
    public static bool RunConversionHoldCheck()
    {
        var ok = true;
        AssaultPlanner Planner(CombatSelfCheck.Fixture f) => new AssaultPlanner(f.Map, 0, null, f.Stats, null, 0, new HashSet<int> { 1 });

        using (var f = CombatSelfCheck.Fixture.Line())
        {
            Populate(f.P("A"), (1, 3));
            f.Ships("A", 0, 1); f.War(0, 1);
            ok &= Check(Planner(f).ConversionHolds().SequenceEqual(new[] { "A" }), "dominated, with an at-war holder: held");
            f.Ships("A", 1, 1);
            ok &= Check(Planner(f).ConversionHolds().Count == 0, "an at-war rival warship there too: not dominated, not a conversion hold (it is a contested hold)");
        }
        using (var f = CombatSelfCheck.Fixture.Line())
        {
            Populate(f.P("A"), (1, 3));
            f.Ships("A", 0, 1);
            ok &= Check(Planner(f).ConversionHolds().Count == 0, "no war: not held");
            f.War(0, 1);
            f.Map.Diplomacy.Enabled = false;
            ok &= Check(Planner(f).ConversionHolds().Count == 0, "legacy mode: not held");
        }
        using (var f = CombatSelfCheck.Fixture.Line())
        {
            Populate(f.P("A"), (2, 2), (0, 3));            // a non-war holder is left; my session is running
            f.Ships("A", 0, 1); f.War(0, 1);
            f.P("A").ConversionBy = 0;
            ok &= Check(Planner(f).ConversionHolds().SequenceEqual(new[] { "A" }), "the non-war tail: my running session holds the planet");
            f.P("A").EndConversionSession();
            ok &= Check(Planner(f).ConversionHolds().Count == 0, "no session and no at-war holder: not held");
            Populate(f.P("A"), (0, 3));
            f.P("A").ConversionBy = 0;
            ok &= Check(Planner(f).ConversionHolds().Count == 0, "a session on a planet with nothing foreign left: not held");
        }
        using (var f = CombatSelfCheck.Fixture.Line())
        {
            Populate(f.P("A"), (1, 3));
            f.Ships("B", 1, 2);                               // a rival fleet beside A
            f.Ships("A", 0, 1); f.War(0, 1);
            f.Map.Knowledge.Update(f.Map, 2, 8);
            var planner = Planner(f);
            ok &= Check(planner.HeldOffense(f.P("A")) > 0f, "HeldOffense reads my docked offense");
            ok &= Check(Near(planner.NearbyRivalOffense(f.P("A")), planner.HeldOffense(f.P("A")) * 2f), "the rival's offense on a neighbouring known planet is 2 ships' worth");
        }

        // End to end through PlayerAI: the tail has no assault target, so only the hold keeps the ships; without it they go to B.
        var gos = new List<GameObject>();
        try
        {
            int ShipsSentFromA(bool session, float keepFraction = -1f)
            {
                using (var f = CombatSelfCheck.Fixture.Line())
                {
                    f.Constants.conversionHoldKeepFraction = keepFraction;   // negative: keep every ship, the original hold
                    f.Constants.maxPathNodesForKnowledge = 8;
                    f.Constants.maxPathNodesForShipTransport = 10;
                    f.Colonize("B", 0);
                    Populate(f.P("A"), (2, 2), (0, 1));
                    f.P("A").Owner = Planet.NoOwner;
                    f.Ships("A", 0, 2); f.War(0, 1);
                    if (session) f.P("A").ConversionBy = 0;
                    f.Map.Knowledge.Update(f.Map, 3, 8);
                    var ai = MakeAI(f, 0, gos);
                    return ai.PlanShipActions(1).Where(a => a.Origin == "A").Sum(a => a.Count);
                }
            }
            ok &= Check(ShipsSentFromA(true) == 0, "with my session running on A and a negative keep fraction, the planner sends nothing away from it");
            ok &= Check(ShipsSentFromA(true, 0.5f) == 1, "with the default 0.5 and no rival nearby it keeps 1 of A's 2 ships and releases the other to garrison B");
            ok &= Check(ShipsSentFromA(false) > 0, "control: with no session the two ships on A are spare and go to garrison B");
        }
        finally { foreach (var go in gos) Object.DestroyImmediate(go); }
        return ok;
    }

    // A dominated planet where I hold at least one inhabitant and which is below max is a valid target even when I already hold
    // the plurality; the choice cost is divided by 1 + weight x (1 - my share).
    public static bool RunConversionColonizeCheck()
    {
        var ok = true;
        var gos = new List<GameObject>();
        try
        {
            using (var f = CombatSelfCheck.Fixture.Line())
            {
                f.Constants.maxPathNodesForKnowledge = 8;
                var ai = MakeAI(f, 0, gos);
                var a = f.P("A");
                Populate(a, (0, 3), (1, 1));               // 4 of 5: below max, I hold the plurality
                f.War(0, 1);
                f.Map.Knowledge.Update(f.Map, 2, 8);
                ok &= Check(!ai.IsValidColonizationTarget(a), "precondition: a planet where I hold the plurality is not a target without dominance");
                f.Ships("A", 0, 1);
                ok &= Check(ai.IsConversionColonizeTarget(a) && ai.IsValidColonizationTarget(a), "dominated, 3 of 4 mine, below max: a valid target");
                ok &= Check(Near(ai.ConversionColonizeDivisor("A"), 1f + 0.5f * (1f - 0.75f)), "the divisor is 1 + 0.5 x (1 - 0.75) = 1.125");
                ok &= Check(Near(ai.ColonizationCostDivisor("A"), 1.125f), "ColonizationCostDivisor includes it (the chokepoint tilt is off here)");

                Populate(a, (0, 1), (1, 3));
                ok &= Check(Near(ai.ConversionColonizeDivisor("A"), 1f + 0.5f * (1f - 0.25f)), "1 of 4 mine: 1.375, a colonist is worth more early");

                Populate(a, (0, 3), (1, 2));
                ok &= Check(!ai.IsConversionColonizeTarget(a) && !ai.IsValidColonizationTarget(a), "at max population (5): not a target");
                Populate(a, (1, 3));
                ok &= Check(!ai.IsConversionColonizeTarget(a), "none of my inhabitants there: the acceptance extension does not apply");
                ok &= Check(ai.IsValidColonizationTarget(a), "...but a foreign-held planet is already a valid target by the old rule");
                ok &= Check(Near(ai.ConversionColonizeDivisor("A"), 1.5f), "dominated with 0 of 3 mine: the earliest stage gets the biggest tilt, 1 + 0.5 x 1");
                Populate(a, (2, 2), (0, 1));
                f.P("A").ConversionBy = Planet.NoOwner;
                ok &= Check(!ai.IsConversionColonizeTarget(a) && Near(ai.ConversionColonizeDivisor("A"), 1f),
                    "a planet where only a player I am NOT at war with is foreign and no session runs: no conversion can happen, so no target and no tilt");
                f.P("A").ConversionBy = 0;
                ok &= Check(ai.IsConversionColonizeTarget(a) && ai.IsValidColonizationTarget(a), "the non-war tail with my session running: a target");
                f.P("A").EndConversionSession();
                Populate(a, (0, 4));
                ok &= Check(!ai.IsConversionColonizeTarget(a) && Near(ai.ConversionColonizeDivisor("A"), 1f), "nothing foreign left: not a conversion target, divisor 1");

                Populate(a, (0, 3), (1, 1));
                a.DockedShips.Clear();
                ok &= Check(!ai.IsConversionColonizeTarget(a) && Near(ai.ConversionColonizeDivisor("A"), 1f), "no fleet: not dominated, divisor 1");
                f.Ships("A", 0, 1);
                f.Constants.conversionColonizeWeight = 0f;
                ok &= Check(Near(ai.ConversionColonizeDivisor("A"), 1f) && ai.IsValidColonizationTarget(a), "a weight of 0 switches the tilt off, the target stays valid");
                f.Constants.conversionColonizeWeight = 0.5f;
                f.Map.Diplomacy.Enabled = false;
                ok &= Check(!ai.IsConversionColonizeTarget(a), "legacy mode: no conversion rule");
            }
        }
        finally { foreach (var go in gos) Object.DestroyImmediate(go); }
        return ok;
    }

    public static bool RunConversionHoldTrackerCheck()
    {
        var ok = true;
        var t = new ConversionHoldTracker();
        ConversionHoldTracker.Entry E(string planet, int wanted, string call = "Garrison", string target = "B", int keep = 1, int released = 0)
            => new ConversionHoldTracker.Entry { Planet = planet, HeldShips = 2, HeldOffense = 20f, Wanted = wanted, Call = call, CallTarget = target, RivalNearby = 0f, Progress = 0.5f, Keep = keep, Released = released };
        ok &= Check(t.Update(new[] { E("A", 1), E("C", 0, "-", "-") }).Select(e => e.Planet).SequenceEqual(new[] { "A", "C" }), "the first sighting of each held planet is reported, ordered by name");
        ok &= Check(t.Update(new[] { E("A", 1), E("C", 0, "-", "-") }).Count == 0, "the same wanted count, call and target again: not reported");
        ok &= Check(t.Update(new[] { E("A", 2), E("C", 0, "-", "-") }).Select(e => e.Planet).SequenceEqual(new[] { "A" }), "a changed wanted count is reported");
        ok &= Check(t.Update(new[] { E("A", 2, "Assault", "D"), E("C", 0, "-", "-") }).Count == 1, "a changed call is reported");
        ok &= Check(t.Update(new[] { E("A", 2, "Assault", "D", 1, 1), E("C", 0, "-", "-") }).Select(e => e.Planet).SequenceEqual(new[] { "A" }), "a changed released count is reported");
        ok &= Check(t.Update(new[] { E("A", 2, "Assault", "D", 2, 1), E("C", 0, "-", "-") }).Count == 1, "a changed keep is reported");
        t.Update(new ConversionHoldTracker.Entry[0]);
        ok &= Check(t.Update(new[] { E("A", 2, "Assault", "D") }).Count == 1, "a planet that left the holds is forgotten, so a later hold is reported afresh");
        t.Clear();
        ok &= Check(t.Update(new[] { E("A", 2, "Assault", "D") }).Count == 1, "Clear forgets everything");
        return ok;
    }

    // The audit runs the planners a second time without the conversion hold and counts what a call would have taken from the held planet.
    public static bool RunConversionHoldAuditCheck()
    {
        var ok = true;
        var gos = new List<GameObject>();
        try
        {
            using (var f = CombatSelfCheck.Fixture.Line())
            {
                f.Constants.maxPathNodesForKnowledge = 8;
                f.Constants.maxPathNodesForShipTransport = 10;
                f.Colonize("B", 0);
                Populate(f.P("A"), (2, 2), (0, 1));
                f.P("A").Owner = Planet.NoOwner;
                f.Ships("A", 0, 2); f.War(0, 1);
                f.P("A").ConversionBy = 0; f.P("A").ConversionProgress = 0.25f;
                f.Map.Knowledge.Update(f.Map, 3, 8);
                var ai = MakeAI(f, 0, gos);
                var entries = ai.AuditConversionHolds(1);
                ok &= Check(entries.Count == 1 && entries[0].Planet == "A" && entries[0].HeldShips == 2 && entries[0].Wanted > 0 && entries[0].Call == "Garrison" && entries[0].CallTarget == "B",
                    "A is held and B's garrison would have taken ships from it");
                ok &= Check(Near(entries[0].Progress, 0.25f) && entries[0].HeldOffense > 0f, "the entry carries the conversion progress and the held offense");
                f.P("A").ConversionBy = Planet.NoOwner;
                ok &= Check(ai.AuditConversionHolds(2).Count == 0, "no hold, no audit entry");
            }
            using (var f = CombatSelfCheck.Fixture.Line())     // nothing else wants the ships: wanted 0, call -
            {
                f.Constants.maxPathNodesForKnowledge = 8;
                Populate(f.P("A"), (2, 2), (0, 1));
                f.P("A").Owner = Planet.NoOwner;
                f.Ships("A", 0, 1); f.War(0, 1);
                f.P("A").ConversionBy = 0;
                f.Map.Knowledge.Update(f.Map, 3, 8);
                var entries = MakeAI(f, 0, gos).AuditConversionHolds(1);
                ok &= Check(entries.Count == 1 && entries[0].Wanted == 0 && entries[0].Call == "-" && entries[0].CallTarget == "-", "with no other call the entry says so");
            }
        }
        finally { foreach (var go in gos) Object.DestroyImmediate(go); }
        return ok;
    }

    // A capitol is the Prime planet a player owns. Conversion can take it away (a 400-turn test2.json run crashed at T270 in
    // Gameboard.CreateNotificationsForCompletedResearch when player 2's Prime was conquered), so the name a notification links to
    // must be empty, not an exception, for a player without one.
    public static bool RunCapitolLostCheck()
    {
        var ok = true;
        using (var f = CombatSelfCheck.Fixture.Line())      // the fixture has no Prime planet: nobody has a capitol
        {
            ok &= Check(f.Map.GetPlayerCapitol(2) == null, "precondition: a player with no Prime planet has no capitol");
            string name = null;
            try { name = f.Map.GetPlayerCapitolName(2); }
            catch (System.Exception e) { Debug.LogError($"[ConversionSelfCheck] GetPlayerCapitolName threw: {e.GetType().Name}"); }
            ok &= Check(name == string.Empty, "the capitol name for a player without one is empty, and asking does not throw");
        }
        return ok;
    }

    // How many ships a conversion hold keeps: max(1, ceil(fraction x nearby rival offense / offense per ship)), capped at the ships there;
    // a negative fraction keeps every ship (the original hold). Rival counts never sit on a ceil boundary (fractions chosen off it).
    public static bool RunConversionKeepCheck()
    {
        var ok = true;
        int KeepOn(float fraction, int rivalShips)
        {
            using (var f = CombatSelfCheck.Fixture.Line())
            {
                f.Constants.conversionHoldKeepFraction = fraction;
                Populate(f.P("A"), (1, 3));
                f.Ships("A", 0, 6); f.War(0, 1);
                if (rivalShips > 0) f.Ships("B", 1, rivalShips);          // the rival beside A, known to me
                f.Map.Knowledge.Update(f.Map, 2, 8);
                var planner = new AssaultPlanner(f.Map, 0, null, f.Stats, null, 0, new HashSet<int> { 1 });
                return planner.ConversionKeep().TryGetValue("A", out var keep) ? keep : -1;
            }
        }
        ok &= Check(KeepOn(0.5f, 0) == 1, "no rival nearby: keep 1 ship");
        ok &= Check(KeepOn(0.5f, 3) == 2, "a rival of 3 ships' offense at 0.5: ceil(1.5) = keep 2 of 6");
        ok &= Check(KeepOn(0.9f, 3) == 3, "at 0.9: ceil(2.7) = keep 3");
        ok &= Check(KeepOn(0.5f, 20) == 6, "a huge rival never keeps more than the 6 ships there");
        ok &= Check(KeepOn(0f, 3) == 1, "a fraction of 0 keeps 1 ship whatever the rival");
        ok &= Check(KeepOn(-1f, 3) == 6 && KeepOn(-1f, 0) == 6, "a negative fraction keeps every ship (the original hold)");
        using (var f = CombatSelfCheck.Fixture.Line())
        {
            Populate(f.P("A"), (1, 3));
            f.Ships("A", 0, 6); f.War(0, 1);
            var planner = new AssaultPlanner(f.Map, 0, null, f.Stats, null, 0, new HashSet<int> { 1 });
            ok &= Check(planner.ConversionKeep(new[] { "A" }).Count == 0, "a retreating planet is left out of the keep map (retreat claims its ships)");
            f.Map.Diplomacy.Enabled = false;
            ok &= Check(planner.ConversionKeep().Count == 0, "legacy mode: no keep map");
        }
        return ok;
    }

    // The planner reads the keep map: Spare = docked - max(garrison, keep); a fully held planet (assault target, contested) ignores it.
    public static bool RunConversionPlannerKeepCheck()
    {
        var ok = true;
        using (var f = CombatSelfCheck.Fixture.Line())            // a stranded conversion planet: A holds 4 ships, keep 1, B wants a garrison
        {
            f.Constants.maxPathNodesForShipTransport = 10;
            f.Colonize("B", 0);
            Populate(f.P("A"), (1, 3));
            f.P("A").Owner = Planet.NoOwner;
            f.Ships("A", 0, 4);
            var released = new ShipTransportPlanner(f.Map, 0, PlayerAI.AIStrategy.AIStrategyConsolidate)
                { ConversionKeep = new Dictionary<string, int> { { "A", 1 } } }.Plan();
            ok &= Check(released.Where(a => a.Origin == "A").Sum(a => a.Count) == 3, "keep 1 of 4 on a stranded planet releases 3 to B's garrison");
            var held = new ShipTransportPlanner(f.Map, 0, PlayerAI.AIStrategy.AIStrategyConsolidate) { HeldPlanets = new[] { "A" } }.Plan();
            ok &= Check(held.Count(a => a.Origin == "A") == 0, "control: a fully held A sends nothing");
            var heldBeatsKeep = new ShipTransportPlanner(f.Map, 0, PlayerAI.AIStrategy.AIStrategyConsolidate)
                { HeldPlanet = "A", ConversionKeep = new Dictionary<string, int> { { "A", 1 } } }.Plan();
            ok &= Check(heldBeatsKeep.Count(a => a.Origin == "A") == 0, "an assault target stays fully held even with a keep entry");
        }
        using (var f = CombatSelfCheck.Fixture.Line())            // a colonized conversion planet: the garrison is a floor under the keep
        {
            f.Constants.maxPathNodesForShipTransport = 10;
            f.Colonize("B", 0);
            Populate(f.P("B"), (0, 3), (1, 1));
            f.P("B").Owner = 0;
            var garrison = new ShipTransportPlanner(f.Map, 0, PlayerAI.AIStrategy.AIStrategyConsolidate).Garrison(f.P("B"));
            f.Ships("B", 0, garrison + 3);
            int SpareOnB(int keep)
            {
                var planner = new ShipTransportPlanner(f.Map, 0, PlayerAI.AIStrategy.AIStrategyConsolidate)
                    { ConversionKeep = new Dictionary<string, int> { { "B", keep } } };
                planner.BuildStates();
                return planner.LastStates.First(s => s.Planet.PlanetName == "B").Spare;
            }
            ok &= Check(SpareOnB(1) == 3, "keep 1 under a garrison floor: the garrison binds, spare is docked - garrison = 3");
            ok &= Check(SpareOnB(garrison + 2) == 1, "a keep above the garrison binds instead: spare = docked - keep = 1");
        }
        return ok;
    }

    public static bool RunTunableDefaultsCheck()
    {
        var c = ScriptableObject.CreateInstance<GameAIConstants>();
        try
        {
            var ok = Check(Near(c.conversionTurnsBase, 6f), "conversionTurnsBase defaults to 6");
            ok &= Check(Near(c.conversionHoldKeepFraction, 0.5f), "conversionHoldKeepFraction defaults to 0.5");
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
