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
        ok &= RunProjectionCheck();
        ok &= RunBlockadeViewCheck();
        ok &= RunRetreatMatrixCheck();
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

    private static FightProjection Project(CombatSelfCheck.Fixture f, string planet, int player, params GameAI.GameAIOrder[] orders)
        => FightProjector.Project(f.Map, f.P(planet), player, f.Stats, f.Constants, orders);

    private static readonly List<string> MaxDefense = new List<string> { "Def 1", "Def 2", "Def 3", "Def 4", "Def 5" };

    // The projection plays the fight to its end: a win, a wipe-out, defense, inbound reinforcements, no fight, a zero-health ship,
    // a third player at war with neither side, legacy mode.
    public static bool RunProjectionCheck()
    {
        var ok = true;

        using (var f = CombatSelfCheck.Fixture.Line())
        {
            f.Ships("A", 0, 3); f.Ships("A", 1, 1); f.War(0, 1);
            var mine = Project(f, "A", 0);
            ok &= Check(mine != null && !mine.RivalSurvives && mine.ProjectedLossFraction > 0f && mine.ProjectedLossFraction < 0.3f && !mine.MyGroupWiped,
                "3 against 1: I win and lose a little");
            ok &= Check(mine != null && mine.MyShips == 3 && Near(mine.MyStrength, 3150f) && mine.Rivals.SequenceEqual(new[] { 1 }), "3 ships, strength 3150, rival 1");
            var theirs = Project(f, "A", 1);
            ok &= Check(theirs != null && theirs.RivalSurvives && theirs.MyGroupWiped && Near(theirs.ProjectedLossFraction, 1f),
                "1 against 3: wiped, and the rival survives");
        }

        using (var f = CombatSelfCheck.Fixture.Line())    // peace: nothing to project
        {
            f.Ships("A", 0, 2); f.Ships("A", 1, 2);
            ok &= Check(Project(f, "A", 0) == null, "players at peace: no projection");
        }

        // Defense is counted exactly as combat counts it (K / (K + Defense)), not as a linear add-on.
        float EvenFightLoss(bool highDefense)
        {
            using (var f = CombatSelfCheck.Fixture.Line())
            {
                if (highDefense) { f.P("A").DockShipFromSave(Ship.ShipKind.WarShip, 0, MaxDefense); f.P("A").DockShipFromSave(Ship.ShipKind.WarShip, 0, MaxDefense); }
                else f.Ships("A", 0, 2);
                f.Ships("A", 1, 2); f.War(0, 1);
                return Project(f, "A", 0).ProjectedLossFraction;
            }
        }
        ok &= Check(EvenFightLoss(true) < EvenFightLoss(false), "2 high-defense ships lose less of their strength than 2 plain ones against the same rival");

        using (var f = CombatSelfCheck.Fixture.Line())    // inbound own reinforcements change the picture
        {
            f.Ships("A", 0, 1); f.Ships("A", 1, 3); f.War(0, 1);
            var without = Project(f, "A", 0);
            var fleet = new GameAI.GameAIOrder
            {
                Type = GameAI.GameAIOrder.OrderType.OrderTypeShipTransport, PlayerId = 0, Target = "A", Origin = "B", TimingDelay = 2, Data = 3,
                Fleet = new GameAI.GameAIOrder.ShipFleetPayload
                {
                    Kind = Ship.ShipKind.WarShip,
                    Snapshots = new List<List<string>> { new List<string>(), new List<string>(), new List<string>() },
                    Damage = new List<float>(),
                },
            };
            var with = Project(f, "A", 0, fleet);
            ok &= Check(without.MyGroupWiped && with.InboundShips == 3 && with.ProjectedLossFraction < without.ProjectedLossFraction,
                "1 against 3 is a wipe-out; with 3 own ships landing in 2 turns the loss is smaller");
            var theirsFleet = new GameAI.GameAIOrder
            {
                Type = fleet.Type, PlayerId = 1, Target = "A", TimingDelay = 2, Data = 3, Fleet = fleet.Fleet,
            };
            ok &= Check(Project(f, "A", 0, theirsFleet).InboundShips == 0, "a rival's in-flight fleet is not counted");
            var elsewhere = new GameAI.GameAIOrder { Type = fleet.Type, PlayerId = 0, Target = "C", TimingDelay = 2, Data = 3, Fleet = fleet.Fleet };
            ok &= Check(Project(f, "A", 0, elsewhere).InboundShips == 0, "my fleet heading to another planet is not counted");
            var late = new GameAI.GameAIOrder { Type = fleet.Type, PlayerId = 0, Target = "A", TimingDelay = f.Constants.retreatProjectionTurns + 5, Data = 3, Fleet = fleet.Fleet };
            ok &= Check(Project(f, "A", 0, late).InboundShips == 0, "a fleet landing beyond the projection cap is ignored");
        }

        using (var f = CombatSelfCheck.Fixture.Line())    // a ship at exactly 0 health does not fight, count or crash anything
        {
            f.Ships("A", 0, 1, 100f);     // 100 damage on a Health stat of 100: health 0
            f.Ships("A", 0, 1);
            f.Ships("A", 1, 1); f.War(0, 1);
            var p = Project(f, "A", 0);
            ok &= Check(p != null && p.MyShips == 1 && Near(p.MyStrength, 1050f), "the 0-health ship is not counted: 1 ship, strength 1050");
        }

        using (var f = CombatSelfCheck.Fixture.Line())    // a third player at war with neither side
        {
            f.Ships("A", 0, 1); f.Ships("A", 1, 1); f.Ships("A", 2, 5); f.War(0, 1);
            var p = Project(f, "A", 0);
            ok &= Check(p != null && p.Rivals.SequenceEqual(new[] { 1 }), "player 2 is nobody's war rival: only player 1 is mine");
        }

        using (var f = CombatSelfCheck.Fixture.Line())    // the wiring: one result per (planet, player) in a fight, none in legacy mode
        {
            f.Ships("A", 0, 3); f.Ships("A", 1, 1); f.War(0, 1);
            var results = new List<Planet.UpdateResult>();
            GameAI.AppendFightProjections(f.Map, f.Stats, f.Constants, new List<GameAI.GameAIOrder>(), results);
            ok &= Check(results.Count == 2 && results.All(r => r.Result == Planet.UpdateResult.UpdateResultType.UpdateResultTypeFightProjection
                                                                 && r.Data is FightProjection && r.Name == "A"),
                "two projection results, one per player docked in the war fight at A");
            ok &= Check(results.Select(r => r.PlayerID).OrderBy(i => i).SequenceEqual(new[] { 0, 1 }), "their PlayerIDs are 0 and 1");
            f.Map.Diplomacy.Enabled = false;
            var legacy = new List<Planet.UpdateResult>();
            GameAI.AppendFightProjections(f.Map, f.Stats, f.Constants, new List<GameAI.GameAIOrder>(), legacy);
            ok &= Check(legacy.Count == 0, "legacy mode: no projection results");
        }
        return ok;
    }

    public static bool RunBlockadeViewCheck()
    {
        var ok = true;
        var view = BlockadeView.WithBlockers(("B", 5f, 1), ("C", 7f, 2), ("D", 3f, Planet.NoOwner));
        var war = view.OnlyFrom(new SortedSet<int> { 1 });
        ok &= Check(war.IsBlockaded("B") && Near(war.Value("B"), 5f) && war.Blocker("B") == 1, "a blockade by a war rival (1) is kept with its value and blocker");
        ok &= Check(!war.IsBlockaded("C"), "a blockade by a player I am not at war with (2) is dropped");
        ok &= Check(!war.IsBlockaded("D"), "a remembered blockade with an unknown blocker is dropped");
        ok &= Check(view.IsBlockaded("C") && view.IsBlockaded("D"), "the original view is untouched");
        var minus = view.Without("B");
        ok &= Check(!minus.IsBlockaded("B") && minus.IsBlockaded("C") && view.IsBlockaded("B"), "Without removes one planet from a copy only");
        ok &= Check(!new BlockadeView().IsBlockaded("B"), "an empty view blockades nothing");
        return ok;
    }

    public static bool RunRetreatMatrixCheck()
    {
        var ok = true;
        var c = ScriptableObject.CreateInstance<GameAIConstants>();
        try
        {
            // The curve: even odds at the midpoint, rising and falling either side (1 / (1 + e^(-(loss - 0.5) / 0.1))).
            ok &= Check(Near(RetreatMatrix.RetreatProbability(c.retreatLossFraction, c), 0.5f), "at retreatLossFraction the retreat weight is 0.5");
            ok &= Check(Near(RetreatMatrix.RetreatProbability(0.3f, c), 0.119f) && Near(RetreatMatrix.RetreatProbability(0.7f, c), 0.881f),
                "0.3 gives about 12% and 0.7 about 88% with the default steepness 0.1");
            ok &= Check(RetreatMatrix.RetreatProbability(1f, c) > 0.99f && RetreatMatrix.RetreatProbability(0f, c) < 0.01f, "a wipe-out is near certain, no loss near never");

            // A deterministic roll: a very steep curve gives exactly 0 or 1.
            c.retreatSteepness = 0.001f;
            RetreatMatrix.Row Row(string planet, float loss, string destination, int tier = 1, float cost = 100f)
                => new RetreatMatrix.Row { Planet = planet, Ships = 3, LossFraction = loss, Destination = destination, Tier = tier, PathCost = cost };
            GameAI.Rand = new System.Random(7);

            var decisions = RetreatMatrix.Decide(new List<RetreatMatrix.Row> { Row("X", 0.95f, "H") }, c);
            ok &= Check(decisions.Count == 1 && decisions[0].Retreat && decisions[0].Action.Origin == "X" && decisions[0].Action.Target == "H"
                        && decisions[0].Action.Count == 3 && Near(decisions[0].Action.Cost, 100f) && decisions[0].PRetreat > 0.99f,
                "a loss far above the midpoint retreats: the whole group of 3 from X to H, with its weight");

            decisions = RetreatMatrix.Decide(new List<RetreatMatrix.Row> { Row("X", 0.1f, "H") }, c);
            ok &= Check(decisions.Count == 1 && !decisions[0].Retreat && decisions[0].PRetreat < 0.01f, "a loss far below the midpoint stays");

            decisions = RetreatMatrix.Decide(new List<RetreatMatrix.Row> { Row("X", 0.95f, "") }, c);
            ok &= Check(decisions.Count == 1 && !decisions[0].Retreat, "a row with no destination offers Stay only, so it stays");

            // Two fight planets, one destination: IndependentRows, both go there.
            decisions = RetreatMatrix.Decide(new List<RetreatMatrix.Row> { Row("X", 0.95f, "H"), Row("Y", 0.9f, "H") }, c);
            ok &= Check(decisions.Count == 2 && decisions.All(d => d.Retreat && d.Action.Target == "H"),
                "two groups retreat to the same destination: neither row removes it from the other");
            ok &= Check(decisions[0].Planet == "X" && decisions[1].Planet == "Y", "decisions come back ordered by planet name");
        }
        finally { Object.DestroyImmediate(c); }
        return ok;
    }

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
