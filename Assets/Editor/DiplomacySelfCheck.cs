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
                ("Food", 1.0f), ("Industry", 1.5f), ("Grotsits", 1.5f), ("Research", 0.5f), ("ColonyShip", 0.5f),
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
