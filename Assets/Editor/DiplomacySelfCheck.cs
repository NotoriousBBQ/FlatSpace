using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using FlatSpace.AI;
using Flatspace.Objects.Production;

public static class DiplomacySelfCheck
{
    [MenuItem("FlatSpace/AI/Run Diplomacy Self-Check")]
    public static void Run() => RunChecks();

    public static bool RunChecks()
    {
        var ok = RunDiplomacyStateCheck();
        ok &= RunContactPlayersCheck();
        ok &= RunCutAttributionCheck();
        ok &= RunFleetStrengthCheck();
        ok &= RunHostilityCheck();
        ok &= RunStanceMatrixCheck();
        ok &= RunAmassTablesCheck();
        ok &= RunConsolidateLikeSeamsCheck();
        ok &= RunAmassRunsTheRoutineCheck();
        ok &= RunAssaultGateCheck();
        ok &= RunWarRivalsCheck();
        ok &= RunWantedFleetGateCheck();
        ok &= RunUpdateDiplomacyCheck();
        ok &= RunStrategySwitchCheck();
        ok &= RunForcedWarCheck();
        ok &= RunCutsAndLegacyCheck();
        ok &= RunStanceSaveCheck();
        ok &= RunLostContactWarCanEndCheck();
        ok &= RunPeacefulHoldsReleaseCheck();
        ok &= RunNearShipsIgnoreRivalGarrisonCheck();
        ok &= RunStanceOrderCheck();
        ok &= RunStanceOrdersEmittedCheck();
        ok &= RunTruceAndSurrenderOrderCheck();
        Debug.Log(ok
            ? "[DiplomacySelfCheck] ALL PASSED"
            : "[DiplomacySelfCheck] FAILURES (see errors above)");
        return ok;
    }

    private static bool Check(bool condition, string label)
    {
        if (!condition) Debug.LogError($"[DiplomacySelfCheck] FAIL: {label}");
        return condition;
    }

    private static bool Near(float a, float b) => Mathf.Abs(a - b) < 0.01f;

    // A(0,0) - B(100,0) - C(200,0) - D(300,0). Player 0 holds A and has a PlayerAI. Diplomacy stays in legacy mode
    // (Enabled false) until a check turns it on.
    private sealed class Fixture : System.IDisposable
    {
        public GameObject MapGo, PlayerGo;
        public GameAIMap Map;
        public GameAIConstants Constants;
        public ShipData Template;
        public List<CatalogItem> Research;
        public PlayerAI AI;

        public static Fixture Line()
        {
            var f = new Fixture();
            f.Template = WarshipSelfCheck.MakeTemplate();
            f.Constants = WarshipSelfCheck.MakeConstants(f.Template);
            f.Constants.maxPathNodesForKnowledge = 6;
            f.Constants.maxPathNodesForShipTransport = 10;
            f.Research = WarshipSelfCheck.MakeResearch();
            f.MapGo = new GameObject("DipSelfCheckMap");
            f.Map = f.MapGo.AddComponent<GameAIMap>();
            f.Map.GameAIMapInit(new List<PlanetSpawnData>
            {
                ChokepointSelfCheck.Spawn("A", 0f, 0f, new[] { "B" }),
                ChokepointSelfCheck.Spawn("B", 100f, 0f, new[] { "A", "C" }),
                ChokepointSelfCheck.Spawn("C", 200f, 0f, new[] { "B", "D" }),
                ChokepointSelfCheck.Spawn("D", 300f, 0f, new[] { "C" }),
            }, f.Constants);
            f.Colonize("A", 0);
            f.PlayerGo = new GameObject("DipSelfCheckPlayer");
            var player = f.PlayerGo.AddComponent<Player>();
            f.AI = f.PlayerGo.AddComponent<PlayerAI>();
            f.AI.Player = player;
            f.AI.AIMap = f.Map;
            player.playerID = 0;
            f.AI.ResearchCatalog = f.PlayerGo.AddComponent<Catalog>();
            f.AI.ResearchCatalog.catalogItems = f.Research;
            return f;
        }

        public Planet P(string name) => Map.GetPlanet(name);

        public void Colonize(string name, int player)
        {
            var planet = Map.GetPlanet(name);
            planet.Owner = player;
            planet.Population.Add(new Planet.Inhabitant { Player = player });
        }

        public void Ships(string planet, int owner, int count) => WarshipSelfCheck.DockWarships(P(planet), owner, count);

        public void Know(int players = 3) => Map.Knowledge.Update(Map, players, 6);

        public void Dispose()
        {
            WarshipSelfCheck.DestroyAll(Research);
            Object.DestroyImmediate(MapGo);
            Object.DestroyImmediate(PlayerGo);
            Object.DestroyImmediate(Constants);
            Object.DestroyImmediate(Template);
        }
    }

    // Stances, hostility, effective war, pending cuts and the save entries, all without a map.
    public static bool RunDiplomacyStateCheck()
    {
        var ok = true;
        var d = new DiplomacyState();

        ok &= Check(d.StanceToward(0, 1) == Stance.Peace && Near(d.Hostility(0, 1), 0f), "a fresh pair is Peace at 0 hostility");
        ok &= Check(d.TurnsSinceChange(0, 1, 5) > 1000, "a pair that never changed has no hold (a huge number of turns since)");

        ok &= Check(d.SetStance(0, 1, Stance.War, 7), "Peace to War reports a change");
        ok &= Check(!d.SetStance(0, 1, Stance.War, 8), "War to War reports no change");
        ok &= Check(d.TurnsSinceChange(0, 1, 10) == 3, "turns since change counts from the change (7 to 10)");
        ok &= Check(d.StanceToward(1, 0) == Stance.Peace, "the opposite direction is its own stance");

        // Effective war: own War always; the rival's War only with contact.
        ok &= Check(d.IsAtWar(0, 1, false), "my own War counts without contact");
        ok &= Check(!d.IsAtWar(1, 0, false), "the rival's War does not force me without contact");
        ok &= Check(d.IsAtWar(1, 0, true), "the rival's War forces me once I have contact");
        ok &= Check(!d.IsAtWar(0, 2, true), "no stance either way: Peace");

        d.SetStance(0, 1, Stance.Peace, 20);
        ok &= Check(!d.IsAtWar(1, 0, true), "a forced war ends when the declarer returns to Peace");

        // WarRivals: contact players plus anyone I declared on or who declared on me.
        d.SetStance(0, 3, Stance.War, 30);
        d.SetStance(2, 0, Stance.War, 30);
        var war = d.WarRivals(0, new[] { 1 });
        ok &= Check(war.SetEquals(new[] { 3 }), "no contact with 2: its declaration does not count; my own War on 3 does without contact");
        var warWithContact = d.WarRivals(0, new[] { 1, 2 });
        ok &= Check(warWithContact.SetEquals(new[] { 2, 3 }), "with contact with 2 its declaration counts too");

        // Cuts are counted per (victim, blocker), consumed once, and discarded for non-contact blockers.
        d.RecordCut(0, 1); d.RecordCut(0, 1); d.RecordCut(0, 2); d.RecordCut(0, 0); d.RecordCut(0, -1);
        ok &= Check(d.TakeCuts(0, 1) == 2, "two cuts by player 1 are counted");
        ok &= Check(d.TakeCuts(0, 1) == 0, "taking cuts consumes them");
        d.DiscardCuts(0);
        ok &= Check(d.TakeCuts(0, 2) == 0, "DiscardCuts drops cuts nobody consumed");
        d.RecordCut(1, 0);
        ok &= Check(d.TakeCuts(0, 1) == 0 && d.TakeCuts(1, 0) == 1, "cuts are per victim");

        // Save entries round trip; restoring null leaves the player all Peace; another player is untouched.
        var pair = d.Get(0, 3);
        pair.Hostility = 42.5f;
        d.Set(0, 3, pair);
        var entries = d.Snapshot(0);
        var restored = new DiplomacyState();
        restored.SetStance(1, 0, Stance.War, 4);
        restored.Restore(0, entries);
        ok &= Check(restored.StanceToward(0, 3) == Stance.War && Near(restored.Hostility(0, 3), 42.5f)
                    && restored.TurnsSinceChange(0, 3, 40) == 10,
            "stance, hostility and last-change turn survive Snapshot and Restore");
        ok &= Check(restored.StanceToward(1, 0) == Stance.War, "restoring player 0 leaves player 1 alone");
        restored.Restore(0, null);
        ok &= Check(restored.StanceToward(0, 3) == Stance.Peace && Near(restored.Hostility(0, 3), 0f),
            "restoring nothing (an older save) is all Peace at 0");
        ok &= Check(d.Rivals(0).SequenceEqual(new[] { 1, 3 }), "Rivals lists the players with a recorded pair, ascending");
        return ok;
    }

    // Contact is per rival, from planets the viewer knows; ownerless ships are never a rival.
    public static bool RunContactPlayersCheck()
    {
        var ok = true;
        using (var f = Fixture.Line())
        {
            f.Know();
            ok &= Check(f.Map.Knowledge.ContactPlayers(f.Map, 0).Count == 0, "alone: no contact players");

            f.Colonize("B", 1);
            f.Ships("C", 2, 1);
            f.Ships("B", Planet.NoOwner, 1);
            f.Know();
            var contact = f.Map.Knowledge.ContactPlayers(f.Map, 0);
            ok &= Check(contact.SequenceEqual(new[] { 1, 2 }),
                "population on a known planet and a docked ship are contact; an ownerless ship is not a player");
            ok &= Check(f.Map.Knowledge.HasContactWith(f.Map, 0, 1) && f.Map.Knowledge.HasContactWith(f.Map, 0, 2)
                        && !f.Map.Knowledge.HasContactWith(f.Map, 0, 3),
                "contact is per rival");
            ok &= Check(!f.Map.Knowledge.ContactPlayers(f.Map, 0).Contains(0), "I am never my own contact");
        }
        return ok;
    }

    // Strength = sum over docked warships of Offense x (Health + Defense). The test template is offense 10, health 100,
    // defense 5, so one unresearched ship is 10 x 105 = 1050.
    public static bool RunFleetStrengthCheck()
    {
        var ok = true;
        using (var f = Fixture.Line())
        {
            var stats = new WarshipStats(f.Research);
            f.Ships("A", 0, 2);
            f.Ships("B", 1, 3);
            f.Ships("D", 1, 4);                      // beyond what player 0 knows once knowledge is small
            f.P("A").DockShipFromSave(Ship.ShipKind.ColonyShip, 0, new List<string>());

            ok &= Check(Near(FleetStrength.Of(f.P("A").DockedShips.Where(s => s.Owner == 0), stats), 2100f),
                "two warships are 2 x 1050; the colony ship adds nothing");
            ok &= Check(Near(FleetStrength.Of(new List<Ship>(), stats), 0f), "no ships: 0");
            ok &= Check(Near(FleetStrength.Mine(f.Map, 0, stats), 2100f), "Mine counts my docked warships anywhere");

            f.Map.Knowledge.Update(f.Map, 3, 2);     // source + direct neighbours: player 0 knows A and B only
            ok &= Check(f.Map.Knowledge.IsKnown(0, "B") && !f.Map.Knowledge.IsKnown(0, "D"), "precondition: B known, D unknown");
            ok &= Check(Near(FleetStrength.VisibleOf(f.Map, 0, 1, stats), 3150f),
                "the rival's strength counts only on planets I know: 3 x 1050 on B, not the ships on D");
            ok &= Check(Near(FleetStrength.VisibleOf(f.Map, 0, 2, stats), 0f), "a player with no ships there is 0");

            var withResearch = new List<string> { "Off 1" };      // one Offense tier: +4 offense (10 to 14)
            f.P("B").DockShipFromSave(Ship.ShipKind.WarShip, 1, withResearch);
            ok &= Check(FleetStrength.VisibleOf(f.Map, 0, 1, stats) > 3150f + 1050f,
                "a researched ship is worth more than an unresearched one");
        }
        return ok;
    }

    public static bool RunHostilityCheck()
    {
        var ok = true;
        var c = ScriptableObject.CreateInstance<GameAIConstants>();   // defaults: cut 5, near 0.5, weight 1, decay 0.05, max 100
        try
        {
            ok &= Check(Near(HostilityCalculator.StrengthTerm(2000f, 1000f, 1f), 1f), "twice as strong: log2(2) = +1");
            ok &= Check(Near(HostilityCalculator.StrengthTerm(4000f, 1000f, 1f), 2f), "four times as strong: +2");
            ok &= Check(Near(HostilityCalculator.StrengthTerm(16000f, 1000f, 1f), 2f), "sixteen times as strong clamps at +2");
            ok &= Check(Near(HostilityCalculator.StrengthTerm(1000f, 4000f, 1f), -2f), "a quarter as strong: -2");
            ok &= Check(Near(HostilityCalculator.StrengthTerm(1000f, 64000f, 1f), -2f), "far weaker clamps at -2");
            ok &= Check(Near(HostilityCalculator.StrengthTerm(500f, 0f, 1f), 2f), "a rival with no visible fleet counts as +2");
            ok &= Check(Near(HostilityCalculator.StrengthTerm(0f, 500f, 1f), -2f), "no fleet against a fleet: -2");
            ok &= Check(Near(HostilityCalculator.StrengthTerm(0f, 0f, 1f), 0f), "two empty fleets: 0");
            ok &= Check(Near(HostilityCalculator.StrengthTerm(2000f, 1000f, 3f), 3f), "the weight scales the term");

            var decayOnly = HostilityCalculator.Compute(new HostilityCalculator.Inputs { Previous = 100f }, c);
            ok &= Check(Near(decayOnly.Hostility, 95f), "no inputs: 100 decays by 5% to 95");

            var all = HostilityCalculator.Compute(new HostilityCalculator.Inputs
                { Previous = 0f, Cuts = 2, NearShips = 4, MyStrength = 1000f, RivalStrength = 1000f }, c);
            ok &= Check(Near(all.CutsTerm, 10f) && Near(all.NearTerm, 2f) && Near(all.StrengthTerm, 0f) && Near(all.Hostility, 12f),
                "2 cuts x 5 + 4 ships x 0.5 + an even fleet = 12");

            var high = HostilityCalculator.Compute(new HostilityCalculator.Inputs { Previous = 100f, Cuts = 10 }, c);
            ok &= Check(Near(high.Hostility, 100f), "clamped to the maximum of 100");
            var low = HostilityCalculator.Compute(new HostilityCalculator.Inputs
                { Previous = 0f, MyStrength = 1000f, RivalStrength = 64000f }, c);
            ok &= Check(Near(low.Hostility, 0f), "clamped at 0: a weaker player is never below neutral");
        }
        finally { Object.DestroyImmediate(c); }

        // Near ships: rival warships docked on a planet I hold or beside one of my populated planets, known planets only.
        using (var f = Fixture.Line())
        {
            f.Ships("A", 1, 1);                       // on a planet I hold
            f.Ships("B", 1, 2);                       // beside it
            f.Ships("C", 1, 3);                       // two hops away: not near
            f.Ships("B", 2, 5);                       // another rival's ships do not count
            f.Ships("A", 0, 4);                       // my own ships do not count
            f.Know();
            ok &= Check(HostilityCalculator.CountNearShips(f.Map, 0, 1) == 3, "1 on my planet + 2 beside it = 3; C is too far");
            ok &= Check(HostilityCalculator.CountNearShips(f.Map, 0, 2) == 5, "per rival: player 2's 5 ships on B");
            ok &= Check(HostilityCalculator.CountNearShips(f.Map, 0, 3) == 0, "a rival with no ships: 0");
        }
        return ok;
    }

    // Deterministic cases use a very steep curve (steepness 0.01), so the war probability is exactly 0 below the midpoint and
    // exactly 1 above it; the roulette then never picks a zero-weight stance.
    public static bool RunStanceMatrixCheck()
    {
        var ok = true;
        var c = ScriptableObject.CreateInstance<GameAIConstants>();
        try
        {
            ok &= Check(Near(StanceMatrix.WarProbability(30f, c), 0.5f), "at the midpoint the war probability is 0.5");
            ok &= Check(StanceMatrix.WarProbability(0f, c) < 0.05f && StanceMatrix.WarProbability(100f, c) > 0.95f,
                "the default curve is low at 0 and high at 100");
            ok &= Check(StanceMatrix.WarProbability(20f, c) < StanceMatrix.WarProbability(40f, c), "it rises with hostility");

            var peaceRow = new StanceMatrix.Row { Rival = 1, Hostility = 30f, Current = Stance.Peace, TurnsSinceChange = 99 };
            var warRow = new StanceMatrix.Row { Rival = 1, Hostility = 30f, Current = Stance.War, TurnsSinceChange = 99 };
            ok &= Check(Near(StanceMatrix.PeaceWeight(peaceRow, c), 1.5f) && Near(StanceMatrix.WarWeight(peaceRow, c), 0.5f),
                "at 0.5 the held stance (Peace) is x3: Peace 1.5, War 0.5");
            ok &= Check(Near(StanceMatrix.WarWeight(warRow, c), 1.5f) && Near(StanceMatrix.PeaceWeight(warRow, c), 0.5f),
                "the held stance (War) is x3: War 1.5, Peace 0.5");

            c.stanceSteepness = 0.01f;
            c.stanceMidpoint = 30f;
            c.stanceHoldTurns = 10;

            // Rows are independent: two rivals can both get War (one shared matrix would hand War to only one of them).
            var both = StanceMatrix.Decide(0, new List<StanceMatrix.Row>
            {
                new StanceMatrix.Row { Rival = 1, Hostility = 100f, Current = Stance.Peace, TurnsSinceChange = 99 },
                new StanceMatrix.Row { Rival = 2, Hostility = 100f, Current = Stance.Peace, TurnsSinceChange = 99 },
            }, c);
            ok &= Check(both.Count == 2 && both.All(d => d.Stance == Stance.War) && both.Select(d => d.Rival).SequenceEqual(new[] { 1, 2 }),
                "two rivals above the midpoint both get War, in rival order");
            ok &= Check(both.All(d => Near(d.PWar, 1f)), "the decision carries the war probability for the log");

            var mixed = StanceMatrix.Decide(0, new List<StanceMatrix.Row>
            {
                new StanceMatrix.Row { Rival = 1, Hostility = 0f, Current = Stance.War, TurnsSinceChange = 99 },
                new StanceMatrix.Row { Rival = 2, Hostility = 100f, Current = Stance.War, TurnsSinceChange = 99 },
            }, c);
            ok &= Check(mixed.Count == 2 && mixed[0].Stance == Stance.Peace && mixed[1].Stance == Stance.War,
                "below the midpoint War becomes Peace, above it War stays War");

            var held = StanceMatrix.Decide(0, new List<StanceMatrix.Row>
            {
                new StanceMatrix.Row { Rival = 1, Hostility = 100f, Current = Stance.Peace, TurnsSinceChange = 3 },
                new StanceMatrix.Row { Rival = 2, Hostility = 100f, Current = Stance.Peace, TurnsSinceChange = 10 },
            }, c);
            ok &= Check(held.Count == 1 && held[0].Rival == 2, "a row within stanceHoldTurns of its last change is not decided; at the hold it is");
            ok &= Check(StanceMatrix.Decide(0, new List<StanceMatrix.Row>(), c).Count == 0, "no rows: no decisions");
        }
        finally { Object.DestroyImmediate(c); }

        // The generic matrix is unchanged by default: a choice claimed by one row is removed from the others.
        var matrix = new ScoreMatrix<ScoreMatrixDecisionElement, ScoreMatrixChoiceElement, ScoreMatrixAction>(new ScoreMatrixDecisionComparer());
        matrix.MatrixElements.Add(new ScoreMatrixDecisionElement { Target = "R1", Priority = 2f },
            new List<ScoreMatrixChoiceElement> { new ScoreMatrixChoiceElement { Target = "X", Cost = 1f } });
        matrix.MatrixElements.Add(new ScoreMatrixDecisionElement { Target = "R2", Priority = 1f },
            new List<ScoreMatrixChoiceElement> { new ScoreMatrixChoiceElement { Target = "X", Cost = 1f } });
        var shared = matrix.GenerateActionList((d, ch) => new ScoreMatrixAction { Origin = d.Target, Target = ch.Target });
        ok &= Check(shared.Count == 1, "default: both rows want X, only one gets it");

        var independent = new ScoreMatrix<ScoreMatrixDecisionElement, ScoreMatrixChoiceElement, ScoreMatrixAction>(new ScoreMatrixDecisionComparer())
            { IndependentRows = true };
        independent.MatrixElements.Add(new ScoreMatrixDecisionElement { Target = "R1", Priority = 2f },
            new List<ScoreMatrixChoiceElement> { new ScoreMatrixChoiceElement { Target = "X", Cost = 1f } });
        independent.MatrixElements.Add(new ScoreMatrixDecisionElement { Target = "R2", Priority = 1f },
            new List<ScoreMatrixChoiceElement> { new ScoreMatrixChoiceElement { Target = "X", Cost = 1f } });
        var both2 = independent.GenerateActionList((d, ch) => new ScoreMatrixAction { Origin = d.Target, Target = ch.Target });
        ok &= Check(both2.Count == 2, "IndependentRows: both rows get X");
        return ok;
    }

    private static CatalogItem Item(string subType)
    {
        var item = ScriptableObject.CreateInstance<CatalogItem>();
        item.subType = subType;
        return item;
    }

    public static bool RunAmassTablesCheck()
    {
        var ok = true;
        var items = new List<CatalogItem>();
        try
        {
            CatalogItem I(string subType) { var i = Item(subType); items.Add(i); return i; }
            var research = new (string subType, float weight)[]
                { ("Food", 1.0f), ("Industry", 2.5f), ("Grotsits", 2.0f), ("Research", 1.0f), ("ColonyShip", 0.5f), ("Warship", 4.0f) };
            foreach (var (subType, weight) in research)
                ok &= Check(Near(PlayerAI.GetResearchWeight(I(subType), PlayerAI.AIStrategy.AIStrategyAmass), weight),
                    $"Amass research weight for {subType} is {weight}");

            var industry = new (string subType, float weight)[]
            {
                ("Food", 1.0f), ("Industry", 1.5f), ("Grotsits", 1.5f), ("Research", 0.5f), ("ColonyShip", 1.0f),
                ("Warship", 4.0f), ("WarshipUpdate", 4.0f),
            };
            foreach (var (subType, weight) in industry)
                ok &= Check(Near(PlayerAI.GetIndustryStrategyWeight(I(subType), PlayerAI.AIStrategy.AIStrategyAmass), weight),
                    $"Amass industry weight for {subType} is {weight}");

            ok &= Check(PlayerAI.GetIndustryStrategyWeight(I("Warship"), PlayerAI.AIStrategy.AIStrategyAmass)
                        > PlayerAI.GetIndustryStrategyWeight(I("Warship"), PlayerAI.AIStrategy.AIStrategyConsolidate),
                "Amass builds warships harder than Consolidate");
            ok &= Check(PlayerAI.GetIndustryStrategyWeight(I("Warship"), PlayerAI.AIStrategy.AIStrategyConsolidate) == 2.5f,
                "Consolidate's Warship weight is unchanged");
        }
        finally { foreach (var i in items) Object.DestroyImmediate(i); }
        return ok;
    }

    // Every "Consolidate only" rule now covers Amass: the warship multiplier, the colonization tilt, the planner's garrisons.
    public static bool RunConsolidateLikeSeamsCheck()
    {
        var ok = true;
        ok &= Check(PlayerAI.IsConsolidateLike(PlayerAI.AIStrategy.AIStrategyConsolidate)
                    && PlayerAI.IsConsolidateLike(PlayerAI.AIStrategy.AIStrategyAmass)
                    && !PlayerAI.IsConsolidateLike(PlayerAI.AIStrategy.AIStrategyExpand)
                    && !PlayerAI.IsConsolidateLike(PlayerAI.AIStrategy.AIStrategyNone),
            "IsConsolidateLike is Consolidate and Amass only");

        var go = new GameObject("DipSelfCheckMap_Seams");
        var playerGo = new GameObject("DipSelfCheckPlayer_Seams");
        var template = WarshipSelfCheck.MakeTemplate();
        var constants = WarshipSelfCheck.MakeConstants(template);
        try
        {
            constants.chokepointPercentile = 0.9f;
            constants.colonizationChokepointWeight = 0.5f;
            constants.garrisonOuter = 6;
            constants.garrisonHighTraffic = 2;
            constants.warshipShortfallBoost = 2f;
            constants.warshipsPerColonizedPlanet = 100f;
            var map = go.AddComponent<GameAIMap>();
            map.GameAIMapInit(ChokepointSelfCheck.HubSpawns(), constants);
            foreach (var n in new[] { "A", "H", "X1", "X2", "X3", "Y" })
            {
                map.GetPlanet(n).Owner = 0;
                map.GetPlanet(n).Population.Add(new Planet.Inhabitant { Player = 0 });
            }

            var player = playerGo.AddComponent<Player>();
            var ai = playerGo.AddComponent<PlayerAI>();
            ai.Player = player;
            ai.AIMap = map;
            player.playerID = 0;

            var consolidate = new ShipTransportPlanner(map, 0, PlayerAI.AIStrategy.AIStrategyConsolidate);
            var amass = new ShipTransportPlanner(map, 0, PlayerAI.AIStrategy.AIStrategyAmass);
            var expand = new ShipTransportPlanner(map, 0, PlayerAI.AIStrategy.AIStrategyExpand);
            foreach (var n in new[] { "H", "X1", "A" })
                ok &= Check(amass.MaintainsGarrison(map.GetPlanet(n)) == consolidate.MaintainsGarrison(map.GetPlanet(n)),
                    $"Amass garrisons {n} exactly as Consolidate does");
            ok &= Check(consolidate.MaintainsGarrison(map.GetPlanet("H")) && !consolidate.MaintainsGarrison(map.GetPlanet("X1")),
                "precondition: the chokepoint H garrisons under Consolidate, a leaf does not");
            ok &= Check(expand.MaintainsGarrison(map.GetPlanet("X1")), "Expand still garrisons every planet");
            ok &= Check(amass.TargetRank(map.GetPlanet("H")) == consolidate.TargetRank(map.GetPlanet("H")),
                "Amass ranks garrison targets as Consolidate does");

            ok &= Check(Near(ai.ComputeWarshipMultiplier(PlayerAI.AIStrategy.AIStrategyAmass),
                             ai.ComputeWarshipMultiplier(PlayerAI.AIStrategy.AIStrategyConsolidate)),
                "the warship shortfall multiplier is the same under Amass and Consolidate");
            ok &= Check(Near(ai.ComputeWarshipMultiplier(PlayerAI.AIStrategy.AIStrategyExpand), 1f)
                        && ai.ComputeWarshipMultiplier(PlayerAI.AIStrategy.AIStrategyAmass) > 1f,
                "Expand has no multiplier; a fleet shortfall boosts Amass above 1");

            ai.Strategy = PlayerAI.AIStrategy.AIStrategyAmass;
            ok &= Check(Near(ai.ColonizationCostDivisor("H"), 1.5f), "Amass tilts colonization toward the chokepoint like Consolidate (1 + 0.5 x 1)");
            ai.Strategy = PlayerAI.AIStrategy.AIStrategyExpand;
            ok &= Check(Near(ai.ColonizationCostDivisor("H"), 1f), "Expand never tilts");
        }
        finally
        {
            Object.DestroyImmediate(playerGo);
            Object.DestroyImmediate(go);
            Object.DestroyImmediate(constants);
            Object.DestroyImmediate(template);
        }
        return ok;
    }

    // Amass used to be an empty case (a frozen player). It now runs the Expand routine: a food shortage is answered.
    public static bool RunAmassRunsTheRoutineCheck()
    {
        var ok = true;
        var mapGo = new GameObject("DipSelfCheckMap_Amass");
        var playerGo = new GameObject("DipSelfCheckPlayer_Amass");
        var constants = ScriptableObject.CreateInstance<GameAIConstants>();
        try
        {
            constants.defaultTravelSpeed = 1f;
            constants.maxPathNodesForResourceDistribution = 10;
            var map = mapGo.AddComponent<GameAIMap>();
            map.GameAIMapInit(new List<PlanetSpawnData>
            {
                ChokepointSelfCheck.Spawn("Shortage", 0f, 0f, new[] { "Surplus" }),
                ChokepointSelfCheck.Spawn("Surplus", 100f, 0f, new[] { "Shortage" }),
            }, constants);
            map.GetPlanet("Surplus").Population.Add(new Planet.Inhabitant { Player = 0 });

            var player = playerGo.AddComponent<Player>();
            var ai = playerGo.AddComponent<PlayerAI>();
            ai.Player = player;
            ai.AIMap = map;
            player.playerID = 0;
            ai.Strategy = PlayerAI.AIStrategy.AIStrategyAmass;

            var results = new List<Planet.PlanetUpdateResult>
            {
                new Planet.PlanetUpdateResult("Shortage",
                    Planet.PlanetUpdateResult.PlanetUpdateResultType.PlanetUpdateResultTypeFoodShortage, 10f, playerID: 0),
                new Planet.PlanetUpdateResult("Surplus",
                    Planet.PlanetUpdateResult.PlanetUpdateResultType.PlanetUpdateResultTypeFoodSurplus, 20f, playerID: 0),
            };
            var orders = new List<GameAI.GameAIOrder>();
            ai.ProcessResults(results, orders);
            ok &= Check(orders.Exists(o => o.Type == GameAI.GameAIOrder.OrderType.OrderTypeFoodTransport
                                           && o.Origin == "Surplus" && o.Target == "Shortage"),
                "an Amass player still ships food to its shortage (the economy keeps running)");
            ok &= Check(ai.Strategy == PlayerAI.AIStrategy.AIStrategyAmass, "ProcessResults does not change an Amass player's strategy by itself");
        }
        finally
        {
            Object.DestroyImmediate(playerGo);
            Object.DestroyImmediate(mapGo);
            Object.DestroyImmediate(constants);
        }
        return ok;
    }

    // A(P0, warships) - B - C(P2 population) - D(P1 population). The assault attacks only the players in the war set; a null
    // set (diplomacy off) keeps today's rule, every other player is an enemy.
    public static bool RunAssaultGateCheck()
    {
        var ok = true;
        using (var f = Fixture.Line())
        {
            f.Colonize("C", 2);
            f.Colonize("D", 1);
            f.Ships("A", 0, 2);
            f.Ships("D", 1, 2);
            f.Ships("C", 2, 3);
            f.Know();
            var stats = new WarshipStats(f.Research);
            AssaultPlanner Planner(ISet<int> war) => new AssaultPlanner(f.Map, 0, null, stats, null, 0, war);

            ok &= Check(Planner(null).ChooseEnemyTarget().PlanetName == "C", "no war set (legacy): the cheapest enemy planet, C");
            ok &= Check(Planner(new HashSet<int> { 1 }).ChooseEnemyTarget().PlanetName == "D", "at war with 1 only: D");
            ok &= Check(Planner(new HashSet<int> { 2 }).ChooseEnemyTarget().PlanetName == "C", "at war with 2 only: C");
            ok &= Check(Planner(new HashSet<int>()).ChooseEnemyTarget() == null, "at war with nobody: no enemy target");

            ok &= Check(Planner(null).HasKnownEnemyPlanet() && Planner(new HashSet<int> { 1 }).HasKnownEnemyPlanet(),
                "a known enemy planet exists in legacy mode and at war with 1");
            ok &= Check(!Planner(new HashSet<int>()).HasKnownEnemyPlanet(), "at peace with everyone there is no known enemy planet");

            ok &= Check(Planner(null).EnemyWarshipTotal() == 5, "legacy: 2 + 3 enemy warships");
            ok &= Check(Planner(new HashSet<int> { 1 }).EnemyWarshipTotal() == 2, "at war with 1: only its 2 ships size the force");
            ok &= Check(Planner(new HashSet<int> { 2 }).EnemyWarshipTotal() == 3, "at war with 2: only its 3 ships");
            ok &= Check(Planner(new HashSet<int>()).EnemyWarshipTotal() == 0, "at peace: no enemy fleet");

            // Ownerless ships are never an enemy fleet, in either mode.
            f.Ships("B", Planet.NoOwner, 4);
            ok &= Check(Planner(null).EnemyWarshipTotal() == 5, "ownerless ships are not an enemy fleet");
        }
        return ok;
    }

    // WarRivals on PlayerAI: legacy = every contact player; diplomacy on = the derived war set.
    public static bool RunWarRivalsCheck()
    {
        var ok = true;
        using (var f = Fixture.Line())
        {
            f.Colonize("B", 1);
            f.Colonize("C", 2);
            f.Ships("B", Planet.NoOwner, 1);
            f.Know();

            ok &= Check(!f.Map.Diplomacy.Enabled, "precondition: a map a fixture builds is in legacy mode");
            ok &= Check(f.AI.WarRivals().SetEquals(new[] { 1, 2 }), "legacy: every contact player is an enemy (ownerless ships are not a player)");
            ok &= Check(f.AI.AssaultWarFilter() == null, "legacy: the assault gets no filter");

            f.Map.Diplomacy.Enabled = true;
            ok &= Check(f.AI.WarRivals().Count == 0, "diplomacy on, all Peace: no war");
            ok &= Check(f.AI.AssaultWarFilter() != null && f.AI.AssaultWarFilter().Count == 0, "diplomacy on: an empty filter, nobody to attack");
            f.Map.Diplomacy.SetStance(0, 1, Stance.War, 3);
            f.Map.Diplomacy.SetStance(2, 0, Stance.War, 3);
            ok &= Check(f.AI.WarRivals().SetEquals(new[] { 1, 2 }), "my own War on 1 and 2's War on me (I have contact) both make war");
        }
        return ok;
    }

    // WantedWarships adds the assault force only while there is a known enemy planet in the war set.
    public static bool RunWantedFleetGateCheck()
    {
        var ok = true;
        using (var f = Fixture.Line())
        {
            f.Constants.warshipsPerColonizedPlanet = 100f;
            f.Constants.assaultMinimumShips = 3;
            f.Constants.garrisonOuter = 6;
            f.Colonize("D", 1);
            f.Ships("D", 1, 2);
            f.Know();
            f.AI.Strategy = PlayerAI.AIStrategy.AIStrategyConsolidate;

            var legacy = f.AI.WantedWarships();                       // outer garrison + the assault force
            f.Map.Diplomacy.Enabled = true;
            var atPeace = f.AI.WantedWarships();                      // outer garrison only
            f.Map.Diplomacy.SetStance(0, 1, Stance.War, 3);
            var atWar = f.AI.WantedWarships();

            ok &= Check(atPeace == f.Constants.garrisonOuter, "at peace the wanted fleet is just the garrisons");
            ok &= Check(legacy > atPeace && atWar == legacy, "legacy and war both add the assault force; peace does not");
        }
        return ok;
    }

    private static void ApplyStanceOrders(Fixture f, List<GameAI.GameAIOrder> orders, int turn)
    {
        foreach (var order in orders.Where(o => o.Type == GameAI.GameAIOrder.OrderType.OrderTypeDeclareWar
                                                || o.Type == GameAI.GameAIOrder.OrderType.OrderTypeMakePeace))
            GameAI.ApplyStanceOrder(f.Map.Diplomacy, order, turn);
    }

    // One diplomacy turn the way GameAI runs it: the start-of-turn war state (switch, forced-war lines), the decision, then
    // the emitted stance orders executed (ProcessNewOrders). Returns the orders so a case can inspect them.
    private static List<GameAI.GameAIOrder> Turn(Fixture f, int turn)
    {
        f.Map.Diplomacy.Turn = turn;   // the truce is tested against it (no truce in the older cases, so nothing else changes)
        f.AI.ApplyWarState(turn);
        var orders = new List<GameAI.GameAIOrder>();
        f.AI.UpdateDiplomacy(turn, orders);
        ApplyStanceOrders(f, orders, turn);
        return orders;
    }

    // P1 holds B (a neighbour of player 0's A). Diplomacy on.
    private static Fixture WithRival()
    {
        var f = Fixture.Line();
        f.Colonize("B", 1);
        f.Know(2);
        f.Map.Diplomacy.Enabled = true;
        f.AI.Strategy = PlayerAI.AIStrategy.AIStrategyConsolidate;
        // A very steep curve: below the midpoint the war probability is exactly 0, above it exactly 1 (no roulette luck).
        f.Constants.stanceSteepness = 0.01f;
        f.Constants.stanceMidpoint = 30f;
        return f;
    }

    // One turn of hostility and the stance decision.
    public static bool RunUpdateDiplomacyCheck()
    {
        var ok = true;
        using (var f = WithRival())
        {
            f.Ships("A", 0, 2);                                    // I am much stronger: the strength term is +1 (2100 against 1050)
            f.Ships("A", 1, 1);                                    // a rival ship on my planet: near term 0.5 (on its own B it would be a garrison)
            f.Map.Diplomacy.RecordCut(0, 1);
            f.Map.Diplomacy.RecordCut(0, 1);                       // two cuts: 10
            Turn(f, 10);

            var pair = f.Map.Diplomacy.Get(0, 1);
            ok &= Check(Near(pair.CutsTerm, 10f) && Near(pair.NearTerm, 0.5f) && Near(pair.StrengthTerm, 1f),
                "the terms are charged: 2 cuts x 5, 1 ship x 0.5, log2(2100/1050) = 1");
            ok &= Check(Near(pair.Hostility, 10f + 0.5f + 1f), "hostility = 10 + 0.5 + 1 after one turn from 0");
            ok &= Check(pair.NearShips == 1 && pair.MyStrength > pair.RivalStrength, "the log-only numbers are stored");
            ok &= Check(pair.Stance == Stance.Peace, "below the midpoint the stance stays Peace");
            ok &= Check(f.AI.Strategy == PlayerAI.AIStrategy.AIStrategyConsolidate, "and the strategy stays Consolidate");

            // Above the midpoint: War, and the strategy follows at the next turn's start.
            pair.Hostility = 100f;
            f.Map.Diplomacy.Set(0, 1, pair);
            Turn(f, 20);
            ok &= Check(f.Map.Diplomacy.StanceToward(0, 1) == Stance.War, "hostility 95+ is War");
            f.AI.ApplyWarState(21);
            ok &= Check(f.AI.Strategy == PlayerAI.AIStrategy.AIStrategyAmass, "war switches Consolidate to Amass");
            ok &= Check(Near(f.Map.Diplomacy.Get(0, 1).PWar, 1f), "the war probability is kept for the log");
        }
        return ok;
    }

    // The decision is an order: UpdateDiplomacy writes no stance and changes no strategy. Executing the order commits the stance,
    // and the strategy follows only at the start of the next turn (ApplyWarState).
    public static bool RunStanceOrdersEmittedCheck()
    {
        var ok = true;
        using (var f = WithRival())
        {
            var pair = f.Map.Diplomacy.Get(0, 1);
            pair.Hostility = 100f;
            f.Map.Diplomacy.Set(0, 1, pair);

            var orders = new List<GameAI.GameAIOrder>();
            f.AI.UpdateDiplomacy(20, orders);
            var declare = orders.Where(o => o.Type == GameAI.GameAIOrder.OrderType.OrderTypeDeclareWar).ToList();
            ok &= Check(declare.Count == 1 && orders.Count == 1, "hostility 100 emits exactly one order: Declare War");
            ok &= Check(declare.Count == 1 && declare[0].PlayerId == 0 && System.Convert.ToInt32(declare[0].Data) == 1
                        && declare[0].TimingType == GameAI.GameAIOrder.OrderTimingType.OrderTimingTypeImmediate
                        && declare[0].Origin == string.Empty && declare[0].Target == string.Empty,
                "the order is immediate, from player 0, Data is rival 1, no planets");
            ok &= Check(f.Map.Diplomacy.StanceToward(0, 1) == Stance.Peace, "UpdateDiplomacy itself writes no stance");
            ok &= Check(f.AI.Strategy == PlayerAI.AIStrategy.AIStrategyConsolidate, "and does not switch the strategy");
            ok &= Check(Near(f.Map.Diplomacy.Get(0, 1).PWar, 1f), "the war probability is stored for the Stance log line");

            ApplyStanceOrders(f, orders, 20);
            ok &= Check(f.Map.Diplomacy.StanceToward(0, 1) == Stance.War, "executing the order commits War");
            ok &= Check(f.AI.Strategy == PlayerAI.AIStrategy.AIStrategyConsolidate, "the strategy still waits for the next turn's start");
            f.AI.ApplyWarState(21);
            ok &= Check(f.AI.Strategy == PlayerAI.AIStrategy.AIStrategyAmass, "the next turn's ApplyWarState switches Consolidate to Amass");

            // Review focus 1: the matrix picks the held stance again: no order, so no churn.
            var again = new List<GameAI.GameAIOrder>();
            pair = f.Map.Diplomacy.Get(0, 1);
            pair.Hostility = 100f;
            f.Map.Diplomacy.Set(0, 1, pair);
            f.AI.UpdateDiplomacy(40, again);
            ok &= Check(again.Count == 0, "a decision equal to the held stance emits no order");

            // Peace: hostility falls below the midpoint, a Make Peace order, then Amass returns on the next start.
            pair = f.Map.Diplomacy.Get(0, 1);
            pair.Hostility = 0f;
            f.Map.Diplomacy.Set(0, 1, pair);
            var peace = new List<GameAI.GameAIOrder>();
            f.AI.UpdateDiplomacy(60, peace);
            ok &= Check(peace.Count == 1 && peace[0].Type == GameAI.GameAIOrder.OrderType.OrderTypeMakePeace,
                "hostility 0 against a War stance emits Make Peace");
            ApplyStanceOrders(f, peace, 60);
            f.AI.ApplyWarState(61);
            ok &= Check(f.AI.Strategy == PlayerAI.AIStrategy.AIStrategyConsolidate, "and Amass returns to Consolidate at the next start");
        }

        // A rival's declaration is first seen on the next turn, never the turn it is committed.
        using (var f = WithRival())
        {
            f.Map.Diplomacy.SetStance(1, 0, Stance.War, 5);
            ok &= Check(f.AI.Strategy == PlayerAI.AIStrategy.AIStrategyConsolidate && f.AI.WarForcedRivals.Count == 0,
                "committing the rival's declaration changes nothing by itself");
            f.AI.ApplyWarState(6);
            ok &= Check(f.AI.Strategy == PlayerAI.AIStrategy.AIStrategyAmass && f.AI.WarForcedRivals.Contains(1),
                "the next ApplyWarState sees the forced war: Amass and the WarForced bookkeeping");
        }

        // Legacy: nothing emitted, nothing switched.
        using (var f = WithRival())
        {
            f.Map.Diplomacy.Enabled = false;
            f.Map.Diplomacy.SetStance(0, 1, Stance.War, 5);
            var orders = new List<GameAI.GameAIOrder>();
            f.AI.UpdateDiplomacy(6, orders);
            f.AI.ApplyWarState(7);
            ok &= Check(orders.Count == 0 && f.AI.Strategy == PlayerAI.AIStrategy.AIStrategyConsolidate,
                "legacy: no stance order and ApplyWarState never switches to Amass");
        }
        return ok;
    }

    public static bool RunStrategySwitchCheck()
    {
        var ok = true;
        using (var f = WithRival())
        {
            f.Map.Diplomacy.SetStance(0, 1, Stance.War, 5);
            Turn(f, 6);                               // inside the hold: the stance is kept, the war is real
            ok &= Check(f.AI.Strategy == PlayerAI.AIStrategy.AIStrategyAmass, "any war: Consolidate becomes Amass");

            f.Map.Diplomacy.SetStance(0, 1, Stance.Peace, 7);
            Turn(f, 8);
            ok &= Check(f.AI.Strategy == PlayerAI.AIStrategy.AIStrategyConsolidate, "peace with everyone: Amass returns to Consolidate");

            f.AI.Strategy = PlayerAI.AIStrategy.AIStrategyExpand;
            f.Map.Diplomacy.SetStance(0, 1, Stance.War, 9);
            Turn(f, 10);
            ok &= Check(f.AI.Strategy == PlayerAI.AIStrategy.AIStrategyExpand, "an Expand player is never switched by diplomacy");
            f.AI.Strategy = PlayerAI.AIStrategy.AIStrategyNone;
            Turn(f, 11);
            ok &= Check(f.AI.Strategy == PlayerAI.AIStrategy.AIStrategyNone, "nor a None player");

            // Review focus 1: a war survives losing contact; the declarer does not flicker back to Consolidate.
            f.AI.Strategy = PlayerAI.AIStrategy.AIStrategyConsolidate;
            f.P("B").Population.Clear();
            f.Know(2);
            ok &= Check(f.Map.Knowledge.ContactPlayers(f.Map, 0).Count == 0, "precondition: the rival left, no contact now");
            Turn(f, 12);
            ok &= Check(f.Map.Diplomacy.StanceToward(0, 1) == Stance.War && f.AI.Strategy == PlayerAI.AIStrategy.AIStrategyAmass,
                "my own War stance stands without contact: still at war, still Amass");

            // War with one of two rivals keeps Amass; peace with that one but war with the other too.
            f.Colonize("C", 2);
            f.Know(3);
            f.Map.Diplomacy.SetStance(0, 2, Stance.War, 20);
            f.Map.Diplomacy.SetStance(0, 1, Stance.Peace, 20);
            Turn(f, 21);
            ok &= Check(f.AI.Strategy == PlayerAI.AIStrategy.AIStrategyAmass, "at war with one of two rivals: Amass stays");
            f.Map.Diplomacy.SetStance(0, 2, Stance.Peace, 22);
            Turn(f, 23);
            ok &= Check(f.AI.Strategy == PlayerAI.AIStrategy.AIStrategyConsolidate, "peace with both: back to Consolidate");
        }
        return ok;
    }

    // A war the rival declared forces mine once I have contact (spec rule B), and ends when the rival returns to Peace.
    public static bool RunForcedWarCheck()
    {
        var ok = true;
        using (var f = WithRival())
        {
            f.Map.Diplomacy.SetStance(1, 0, Stance.War, 5);        // player 1 declares on me
            Turn(f, 6);
            ok &= Check(f.Map.Diplomacy.StanceToward(0, 1) == Stance.Peace, "my own stance is not changed by being declared on");
            ok &= Check(f.AI.Strategy == PlayerAI.AIStrategy.AIStrategyAmass, "I have contact: the declaration forces me into war and Amass");
            ok &= Check(f.AI.WarForcedRivals.Contains(1), "the forced war is tracked for the WarForced log line");

            // Review focus 2: it ends when the declarer returns to Peace while contact holds.
            f.Map.Diplomacy.SetStance(1, 0, Stance.Peace, 7);
            Turn(f, 8);
            ok &= Check(f.AI.Strategy == PlayerAI.AIStrategy.AIStrategyConsolidate && f.AI.WarForcedRivals.Count == 0,
                "the declarer's Peace ends the forced war and I return to Consolidate");
        }

        using (var f = WithRival())
        {
            f.P("B").Population.Clear();
            f.Know(2);                                             // no contact with player 1
            f.Map.Diplomacy.SetStance(1, 0, Stance.War, 5);
            Turn(f, 6);
            ok &= Check(f.AI.Strategy == PlayerAI.AIStrategy.AIStrategyConsolidate && f.AI.WarForcedRivals.Count == 0,
                "a declaration from a player I have never met changes nothing until contact");
        }
        return ok;
    }

    // Review focus 4 and 5: cuts by a blocker I have no contact with are dropped; diplomacy off is legacy.
    public static bool RunCutsAndLegacyCheck()
    {
        var ok = true;
        using (var f = WithRival())
        {
            f.Map.Diplomacy.RecordCut(0, 2);                       // player 2: no contact
            Turn(f, 10);
            ok &= Check(f.Map.Diplomacy.TakeCuts(0, 2) == 0, "a cut by a player I have no contact with does not wait around");
            f.Colonize("C", 2);
            f.Know(3);
            Turn(f, 11);
            ok &= Check(Near(f.Map.Diplomacy.Get(0, 2).CutsTerm, 0f), "so it cannot inflate hostility once we do meet");
        }

        using (var f = WithRival())
        {
            f.Map.Diplomacy.Enabled = false;                       // legacy
            f.Map.Diplomacy.SetStance(0, 1, Stance.War, 5);
            Turn(f, 6);
            ok &= Check(f.AI.Strategy == PlayerAI.AIStrategy.AIStrategyConsolidate, "legacy: UpdateDiplomacy does nothing, never Amass");
            ok &= Check(f.AI.WarRivals().SetEquals(new[] { 1 }) && f.AI.AssaultWarFilter() == null,
                "legacy: every contact player is an enemy and the assault gets no filter");
            ok &= Check(Near(f.Map.Diplomacy.Get(0, 1).Hostility, 0f), "legacy: no hostility is computed");
        }
        return ok;
    }

    // Review finding: after peace the assault force must go home. A standoff on a planet a peaceful rival populates is no
    // longer held (the held ships would sit beside the rival's planet and feed its hostility), unless the planet is
    // blockaded against me (breaking a blockade needs no war).
    public static bool RunPeacefulHoldsReleaseCheck()
    {
        var ok = true;
        using (var f = Fixture.Line())
        {
            f.Colonize("D", 1);
            f.Ships("D", 0, 2);                                    // my ships (offense 20) beside player 1's...
            f.Ships("D", 1, 2);                                    // ...equal offense, so D is not blockaded
            f.Know();
            var stats = new WarshipStats(f.Research);
            AssaultPlanner Planner(ISet<int> war, BlockadeView view = null) => new AssaultPlanner(f.Map, 0, view, stats, null, 0, war);

            ok &= Check(Planner(null).ContestedHolds().SequenceEqual(new[] { "D" }), "legacy (no war set): the standoff at D is held");
            ok &= Check(Planner(new HashSet<int> { 1 }).ContestedHolds().SequenceEqual(new[] { "D" }), "at war with 1: held");
            ok &= Check(Planner(new HashSet<int> { 2 }).ContestedHolds().Count == 0,
                "at war with someone else only: player 1's planet is not held, the ships may go home");
            ok &= Check(Planner(new HashSet<int>()).ContestedHolds().Count == 0, "at peace with everyone: nothing is held on a rival's planet");

            // A planet I hold is still held against a peaceful rival's docked ships (it is my own colony).
            f.Colonize("A", 0);
            f.Ships("A", 0, 1);
            f.Ships("A", 1, 1);
            ok &= Check(Planner(new HashSet<int>()).ContestedHolds().SequenceEqual(new[] { "A" }),
                "my own colony with a rival's ships docked is still a held standoff at peace");

            // Blockaded against me: held even at peace (breaking a blockade needs no war).
            f.Ships("D", 1, 1);                                    // 30 against my 20: value 10
            var view = BlockadeView.Build(f.Map, 0, new BlockadeSystem(f.Map, f.Research), new BlockadeMemory(), 5,
                f.Constants.blockadeMemoryTurns);
            ok &= Check(view.IsBlockaded("D"), "precondition: D is blockaded against me");
            ok &= Check(Planner(new HashSet<int>(), view).ContestedHolds().Contains("D"),
                "a blockaded planet stays held at peace");
        }
        return ok;
    }

    // Review finding: a rival's garrison on its own planets beside mine is not a threat. Only its ships on my planets or on
    // planets it does not populate count toward the near-ship term.
    public static bool RunNearShipsIgnoreRivalGarrisonCheck()
    {
        var ok = true;
        using (var f = Fixture.Line())
        {
            f.Ships("A", 1, 1);                                    // on a planet I hold: counts
            f.Ships("B", 1, 2);                                    // beside it, B empty: counts
            f.Know();
            ok &= Check(HostilityCalculator.CountNearShips(f.Map, 0, 1) == 3, "precondition: 1 + 2 near ships");

            f.Colonize("B", 1);                                    // now B is player 1's own planet, its garrison
            ok &= Check(HostilityCalculator.CountNearShips(f.Map, 0, 1) == 1,
                "the 2 ships docked on the rival's own planet B are a garrison, not a threat; the 1 on my A still counts");

            f.Colonize("B", 0);                                    // B populated by both: my territory too
            ok &= Check(HostilityCalculator.CountNearShips(f.Map, 0, 1) == 3,
                "a planet I populate as well still counts the rival's ships on it");
        }
        return ok;
    }

    // Review finding: a War stance toward a rival I no longer have contact with must still be able to end, otherwise the
    // player stays in Amass for the rest of the match. Its hostility decays (no cuts, near ships or strength term) and the
    // matrix decides it like any other row.
    public static bool RunLostContactWarCanEndCheck()
    {
        var ok = true;
        using (var f = WithRival())
        {
            f.AI.Strategy = PlayerAI.AIStrategy.AIStrategyAmass;
            f.Map.Diplomacy.SetStance(0, 1, Stance.War, 5);
            var pair = f.Map.Diplomacy.Get(0, 1);
            pair.Hostility = 40f;
            f.Map.Diplomacy.Set(0, 1, pair);
            f.P("B").Population.Clear();
            f.Know(2);
            ok &= Check(f.Map.Knowledge.ContactPlayers(f.Map, 0).Count == 0, "precondition: no contact with player 1");

            Turn(f, 50);                              // 40 decays to 38: still above the midpoint, the war stands
            ok &= Check(Near(f.Map.Diplomacy.Hostility(0, 1), 38f), "without contact the hostility still decays (5% a turn)");
            ok &= Check(f.Map.Diplomacy.StanceToward(0, 1) == Stance.War && f.AI.Strategy == PlayerAI.AIStrategy.AIStrategyAmass,
                "above the midpoint the war without contact stands");

            pair = f.Map.Diplomacy.Get(0, 1);
            pair.Hostility = 31f;
            f.Map.Diplomacy.Set(0, 1, pair);
            Turn(f, 51);                              // 31 decays to 29.45: below the midpoint
            ok &= Check(f.Map.Diplomacy.StanceToward(0, 1) == Stance.Peace, "below the midpoint the war without contact ends");
            f.AI.ApplyWarState(52);
            ok &= Check(f.AI.Strategy == PlayerAI.AIStrategy.AIStrategyConsolidate, "and Amass returns to Consolidate");
        }
        return ok;
    }

    // Stance changes are orders: ApplyStanceOrder is the one place a stance order is decoded and applied (GameAI.ExecuteOrder
    // calls it too), and an order with no planets must not trip a planet lookup.
    public static bool RunStanceOrderCheck()
    {
        var ok = true;
        GameAI.GameAIOrder Order(GameAI.GameAIOrder.OrderType type, int me, int rival) => new GameAI.GameAIOrder
        {
            Type = type,
            TimingType = GameAI.GameAIOrder.OrderTimingType.OrderTimingTypeImmediate,
            Data = rival,
            Origin = string.Empty,
            Target = string.Empty,
            PlayerId = me,
        };

        var d = new DiplomacyState();
        ok &= Check(GameAI.ApplyStanceOrder(d, Order(GameAI.GameAIOrder.OrderType.OrderTypeDeclareWar, 0, 1), 7),
            "a Declare War order against a Peace pair reports a change");
        ok &= Check(d.StanceToward(0, 1) == Stance.War && d.Get(0, 1).LastChangeTurn == 7,
            "the order sets War and stamps the turn (the hold starts there)");
        ok &= Check(d.StanceToward(1, 0) == Stance.Peace, "the rival's own stance is not touched by being declared on");
        ok &= Check(!GameAI.ApplyStanceOrder(d, Order(GameAI.GameAIOrder.OrderType.OrderTypeDeclareWar, 0, 1), 8),
            "a second Declare War reports no change and keeps the first turn");
        ok &= Check(d.Get(0, 1).LastChangeTurn == 7, "so the hold is not restarted");
        ok &= Check(GameAI.ApplyStanceOrder(d, Order(GameAI.GameAIOrder.OrderType.OrderTypeMakePeace, 0, 1), 20),
            "a Make Peace order against a War pair reports a change");
        ok &= Check(d.StanceToward(0, 1) == Stance.Peace && d.Get(0, 1).LastChangeTurn == 20, "Peace, stamped turn 20");

        using (var f = Fixture.Line())
            ok &= Check(f.Map.GetPlanet(string.Empty) == null,
                "an order with an empty Target looks up no planet (GetPlanet(\"\") is null, not an exception)");
        return ok;
    }

    // A surrender ends the war for both sides and locks the pair: no Declare War, no forced war, until the truce ends.
    public static bool RunTruceAndSurrenderOrderCheck()
    {
        var ok = true;
        GameAI.GameAIOrder Order(GameAI.GameAIOrder.OrderType type, int me, int rival) => new GameAI.GameAIOrder
        {
            Type = type,
            TimingType = GameAI.GameAIOrder.OrderTimingType.OrderTimingTypeImmediate,
            Data = rival,
            Origin = string.Empty,
            Target = string.Empty,
            PlayerId = me,
        };

        var d = new DiplomacyState { Enabled = true };
        d.SetStance(0, 1, Stance.War, 5);
        d.SetStance(1, 0, Stance.War, 5);                       // both declared
        ok &= Check(GameAI.ApplySurrender(d, Order(GameAI.GameAIOrder.OrderType.OrderTypeSurrender, 0, 1), 20, 30),
            "a surrender reports a change");
        ok &= Check(d.StanceToward(0, 1) == Stance.Peace && d.StanceToward(1, 0) == Stance.Peace,
            "both stances go to Peace (a one-sided surrender ends the war for both)");
        ok &= Check(d.Get(0, 1).TruceUntil == 50 && d.Get(1, 0).TruceUntil == 50, "both pair rows are locked until turn 20 + 30");
        d.Turn = 30;
        ok &= Check(d.InTruce(0, 1, 30) && d.InTruce(1, 0, 30), "turn 30 is inside the truce, from either side");
        ok &= Check(!d.InTruce(0, 1, 50) && !d.InTruce(0, 2, 30), "the truce ends at turn 50 and covers only that pair");

        ok &= Check(!GameAI.ApplyStanceOrder(d, Order(GameAI.GameAIOrder.OrderType.OrderTypeDeclareWar, 1, 0), 30),
            "a Declare War inside the truce is refused");
        ok &= Check(d.StanceToward(1, 0) == Stance.Peace, "and the stance stays Peace");
        ok &= Check(GameAI.ApplyStanceOrder(d, Order(GameAI.GameAIOrder.OrderType.OrderTypeMakePeace, 1, 0), 30) == false,
            "Make Peace still works (nothing to change: already Peace)");
        ok &= Check(GameAI.ApplyStanceOrder(d, Order(GameAI.GameAIOrder.OrderType.OrderTypeDeclareWar, 1, 0), 50),
            "after the truce a Declare War is accepted again");

        // A rival's earlier declaration must not force a war during the truce.
        var forced = new DiplomacyState { Enabled = true };
        forced.SetStance(1, 0, Stance.War, 5);
        forced.Turn = 6;
        ok &= Check(forced.IsAtWar(0, 1, true), "precondition: without a truce player 1's declaration forces player 0 into war");
        GameAI.ApplySurrender(forced, Order(GameAI.GameAIOrder.OrderType.OrderTypeSurrender, 0, 1), 6, 30);
        forced.SetStance(1, 0, Stance.War, 7);                  // a stale declaration set again inside the truce
        forced.Turn = 7;
        ok &= Check(!forced.IsAtWar(0, 1, true) && !forced.IsAtWar(1, 0, true), "inside a truce the pair is not at war, whatever the stances say");
        ok &= Check(!forced.WarRivals(0, new[] { 1 }).Contains(1), "and WarRivals leaves the truce partner out");
        forced.Turn = 36;
        ok &= Check(forced.IsAtWar(0, 1, true), "once the truce ends the stale declaration counts again");

        // Saves: the truce survives a snapshot and an older entry loads with none.
        var snap = d.Snapshot(0);
        var back = new DiplomacyState();
        back.Restore(0, snap);
        ok &= Check(back.Get(0, 1).TruceUntil == 50, "TruceUntil survives Snapshot and Restore");
        var entry = SaveLoadSystem.GameSave.StanceSave.From(new DiplomacyState.Entry { Rival = 1, Stance = Stance.Peace, TruceUntil = 77 }).ToEntry();
        ok &= Check(entry.TruceUntil == 77, "StanceSave carries truceUntil");
        var old = JsonUtility.FromJson<SaveLoadSystem.GameSave.StanceSave>("{\"rival\":1,\"stance\":1,\"hostility\":10,\"lastChangeTurn\":3}");
        ok &= Check(old.truceUntil == 0, "an older stance save loads with no truce");
        return ok;
    }

    public static bool RunStanceSaveCheck()
    {
        var ok = true;
        var entry = new DiplomacyState.Entry { Rival = 2, Stance = Stance.War, Hostility = 61.5f, LastChangeTurn = 140 };
        var back = SaveLoadSystem.GameSave.StanceSave.From(entry).ToEntry();
        ok &= Check(back.Rival == 2 && back.Stance == Stance.War && Near(back.Hostility, 61.5f) && back.LastChangeTurn == 140,
            "From and ToEntry are inverse");

        var save = new SaveLoadSystem.GameSave.PlayerSave
        {
            playerId = 1,
            stances = new List<SaveLoadSystem.GameSave.StanceSave> { SaveLoadSystem.GameSave.StanceSave.From(entry) },
        };
        var loaded = JsonUtility.FromJson<SaveLoadSystem.GameSave.PlayerSave>(JsonUtility.ToJson(save));
        ok &= Check(loaded.stances != null && loaded.stances.Count == 1 && loaded.stances[0].rival == 2
                    && loaded.stances[0].stance == (int)Stance.War && Near(loaded.stances[0].hostility, 61.5f)
                    && loaded.stances[0].lastChangeTurn == 140,
            "a PlayerSave's stances survive a JsonUtility round trip");

        var state = new DiplomacyState();
        state.SetStance(1, 2, Stance.War, 140);
        var pair = state.Get(1, 2);
        pair.Hostility = 61.5f;
        state.Set(1, 2, pair);
        var restored = new DiplomacyState();
        restored.Restore(1, JsonUtility.FromJson<SaveLoadSystem.GameSave.PlayerSave>(
            JsonUtility.ToJson(new SaveLoadSystem.GameSave.PlayerSave
            {
                playerId = 1,
                stances = state.Snapshot(1).ConvertAll(SaveLoadSystem.GameSave.StanceSave.From),
            })).stances.ConvertAll(s => s.ToEntry()));
        ok &= Check(restored.StanceToward(1, 2) == Stance.War && Near(restored.Hostility(1, 2), 61.5f)
                    && restored.TurnsSinceChange(1, 2, 150) == 10,
            "the whole path state -> save -> JSON -> restore keeps stance, hostility and the hold");

        var older = JsonUtility.FromJson<SaveLoadSystem.GameSave.PlayerSave>("{\"playerId\":1}");
        var none = new DiplomacyState();
        none.SetStance(1, 2, Stance.War, 5);
        none.Restore(1, older.stances?.ConvertAll(s => s.ToEntry()));
        ok &= Check(none.StanceToward(1, 2) == Stance.Peace && none.Snapshot(1).Count == 0,
            "an older save (no stances) restores all Peace");
        return ok;
    }

    // A blockade cut carries the player whose offense set the blockade value, so hostility can be charged to it.
    public static bool RunCutAttributionCheck()
    {
        var ok = true;
        using (var f = Fixture.Line())
        {
            f.Ships("B", 1, 1);                       // player 1 blockades B: offense 10 against my 0
            var order = new GameAI.GameAIOrder
            {
                Type = GameAI.GameAIOrder.OrderType.OrderTypeFoodTransport,
                Origin = "A", Target = "C", PlayerId = 0, Data = 10f,
                TimingDelay = 1, TotalDelay = 2,      // halfway: passes B this turn
            };
            var orders = new List<GameAI.GameAIOrder> { order };
            var cuts = new BlockadeSystem(f.Map, f.Research).Apply(orders, 5);
            ok &= Check(cuts.Count == 1 && cuts[0].Planet == "B" && cuts[0].PlayerId == 0 && cuts[0].BlockerId == 1,
                "the cut at B names player 1 as the blocker");
        }
        return ok;
    }
}
