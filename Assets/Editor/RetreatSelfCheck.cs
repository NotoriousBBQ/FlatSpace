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
        ok &= RunRetreatPlannerCheck();
        ok &= RunPlannerExclusionCheck();
        ok &= RunPlayerAIRetreatCheck();
        ok &= RunRetreatTrackerCheck();
        ok &= RunLargestOtherOffenseCheck();
        ok &= RunPerceivedProjectionCheck();
        ok &= RunPerceivedPlannerCheck();
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

            // The destination shares inside the best tier: cost^-exponent, relative to the cheapest, normalised.
            var even = RetreatMatrix.DestinationShares(new[] { 100f, 100f }, 2f);
            ok &= Check(Near(even[0], 0.5f) && Near(even[1], 0.5f), "equal costs share equally");
            var quad = RetreatMatrix.DestinationShares(new[] { 100f, 200f }, 2f);
            ok &= Check(Near(quad[0], 0.8f) && Near(quad[1], 0.2f), "exponent 2, costs 100 and 200: 80% and 20%");
            var flat = RetreatMatrix.DestinationShares(new[] { 100f, 400f }, 0f);
            ok &= Check(Near(flat[0], 0.5f) && Near(flat[1], 0.5f), "exponent 0 ignores the cost");
            var steep = RetreatMatrix.DestinationShares(new[] { 100f, 200f }, 50f);
            ok &= Check(steep[0] > 0.999f && steep[1] < 0.001f, "a huge exponent is the strictly nearest");
            ok &= Check(Near(RetreatMatrix.DestinationShares(new[] { 0f }, 2f)[0], 1f), "a single candidate (even at cost 0) takes everything");

            // A deterministic roll: a very steep curve gives exactly 0 or 1, and a huge cost exponent picks the nearest.
            c.retreatSteepness = 0.001f;
            c.retreatDestinationCostExponent = 50f;
            RetreatMatrix.Row Row(string planet, float loss, string destination, int tier = 1, float cost = 100f)
                => new RetreatMatrix.Row
                {
                    Planet = planet, Ships = 3, LossFraction = loss,
                    Candidates = string.IsNullOrEmpty(destination)
                        ? new List<RetreatMatrix.Candidate>()
                        : new List<RetreatMatrix.Candidate> { new RetreatMatrix.Candidate { Planet = destination, Tier = tier, PathCost = cost } },
                };
            GameAI.Rand = new System.Random(7);

            var twoCandidates = RetreatMatrix.Decide(new List<RetreatMatrix.Row>
            {
                new RetreatMatrix.Row
                {
                    Planet = "X", Ships = 3, LossFraction = 0.95f,
                    Candidates = new List<RetreatMatrix.Candidate>
                    {
                        new RetreatMatrix.Candidate { Planet = "FAR", Tier = 1, PathCost = 300f },
                        new RetreatMatrix.Candidate { Planet = "NEAR", Tier = 1, PathCost = 100f },
                    },
                },
            }, c);
            ok &= Check(twoCandidates.Count == 1 && twoCandidates[0].Retreat && twoCandidates[0].Action.Target == "NEAR" && Near(twoCandidates[0].Action.Cost, 100f),
                "with several candidates one roll picks both whether and where: a huge exponent picks the nearest");
            c.retreatDestinationCostExponent = 0f;
            var seen = new HashSet<string>();
            for (var seed = 0; seed < 40; seed++)
            {
                GameAI.Rand = new System.Random(seed);
                var pick = RetreatMatrix.Decide(new List<RetreatMatrix.Row>
                {
                    new RetreatMatrix.Row
                    {
                        Planet = "X", Ships = 3, LossFraction = 0.95f,
                        Candidates = new List<RetreatMatrix.Candidate>
                        {
                            new RetreatMatrix.Candidate { Planet = "FAR", Tier = 1, PathCost = 300f },
                            new RetreatMatrix.Candidate { Planet = "NEAR", Tier = 1, PathCost = 100f },
                        },
                    },
                }, c);
                seen.Add(pick[0].Action.Target);
            }
            ok &= Check(seen.Contains("FAR") && seen.Contains("NEAR"), "with exponent 0 both destinations get picked over 40 seeds: the destination is not predictable");
            c.retreatDestinationCostExponent = 50f;
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

    private static RetreatPlanner.Plan Plan(CombatSelfCheck.Fixture f, BlockadeView view, params FightProjection[] projections)
    {
        f.Map.Knowledge.Update(f.Map, 2, 8);
        return new RetreatPlanner(f.Map, 0, new SortedSet<int> { 1 }, view, f.Stats).Decide(projections);
    }

    // A hand-made projection for the planner (the projector has its own check).
    private static FightProjection Doomed(string planet, float lossFraction, bool rivalSurvives = true)
        => new FightProjection
        {
            Planet = planet, Player = 0, MyShips = 1, MyStrength = 1000f, MyStrengthLeft = 1000f * (1f - lossFraction),
            RivalStrength = 3000f, RivalStrengthLeft = 2500f, RivalSurvives = rivalSurvives, Turns = 5, Rivals = new List<int> { 1 },
        };

    public static bool RunRetreatPlannerCheck()
    {
        var ok = true;
        void Prepare(CombatSelfCheck.Fixture f)
        {
            f.Constants.retreatSteepness = 0.001f;    // a deterministic roll
            f.Constants.retreatDestinationCostExponent = 50f;   // and the strictly nearest destination
            GameAI.Rand = new System.Random(3);
        }

        // Tier 1: my own populated planet with no war-rival ship, the cheapest path: B (100) over A (200).
        using (var f = CombatSelfCheck.Fixture.Line())
        {
            Prepare(f);
            f.Colonize("A", 0); f.Colonize("B", 0); f.Colonize("D", 1);
            f.Ships("C", 0, 1); f.Ships("C", 1, 3); f.War(0, 1);
            var plan = Plan(f, null, Doomed("C", 0.95f));
            ok &= Check(plan.Actions.Count == 1 && plan.Actions[0].Origin == "C" && plan.Actions[0].Target == "B" && plan.Actions[0].Count == 1,
                "tier 1: the group at C retreats to B, my nearest populated planet");
            ok &= Check(plan.Retreats.Count == 1 && plan.Retreats[0].Tier == 1 && plan.Retreating.Contains("C"), "tier 1 recorded, C is retreating");
        }

        // The gate: a projected win, or a loss under retreatCheckFraction, never reaches the matrix.
        using (var f = CombatSelfCheck.Fixture.Line())
        {
            Prepare(f);
            f.Colonize("A", 0); f.Colonize("B", 0); f.Colonize("D", 1);
            f.Ships("C", 0, 1); f.Ships("C", 1, 1); f.War(0, 1);
            ok &= Check(Plan(f, null, Doomed("C", 0.95f, rivalSurvives: false)).Actions.Count == 0, "the rival does not survive the projection: stay");
            ok &= Check(Plan(f, null, Doomed("C", 0.2f)).Actions.Count == 0, "a loss of 20% is under the 30% gate: stay, nothing rolled");
        }

        // Tier 2: A holds a war-rival warship, so the empty known planet B is the next choice.
        using (var f = CombatSelfCheck.Fixture.Line())
        {
            Prepare(f);
            f.Colonize("A", 0); f.Colonize("D", 1);
            f.Ships("A", 1, 1);                                        // a war rival's warship at my only populated planet
            f.Ships("C", 0, 1); f.Ships("C", 1, 3); f.War(0, 1);
            var plan = Plan(f, null, Doomed("C", 0.95f));
            ok &= Check(plan.Actions.Count == 1 && plan.Actions[0].Target == "B" && plan.Retreats[0].Tier == 2,
                "tier 2: A holds a war-rival warship, so the empty known planet B is chosen");
        }

        // Blockades count only when a war rival is the blocker, for the destination and the route.
        using (var f = CombatSelfCheck.Fixture.Line())
        {
            Prepare(f);
            f.Colonize("A", 0); f.Colonize("B", 0); f.Colonize("D", 1);
            f.Ships("C", 0, 1); f.Ships("C", 1, 3); f.War(0, 1);
            ok &= Check(Plan(f, BlockadeView.WithBlockers(("B", 5f, 2)), Doomed("C", 0.95f)).Actions[0].Target == "B",
                "B blockaded by player 2, who is not a war rival: still the destination");
            ok &= Check(Plan(f, BlockadeView.WithValues(("B", 5f)), Doomed("C", 0.95f)).Actions[0].Target == "B",
                "a remembered blockade with an unknown blocker is ignored");
            var byRival = Plan(f, BlockadeView.WithBlockers(("B", 5f, 1)), Doomed("C", 0.95f));
            ok &= Check(byRival.Actions.Count == 0 || byRival.Actions[0].Target != "B",
                "B blockaded by a war rival is not a destination, and the route C-B-A would cross it, so A is out too (tier 3 D may be chosen)");
        }

        // Tier 3: a war rival's planet whose remembered blockade is lower than my offense x (1 + margin).
        using (var f = CombatSelfCheck.Fixture.Line())
        {
            Prepare(f);
            f.Colonize("D", 1);
            f.Ships("C", 0, 1); f.War(0, 1);                           // my group's offense is 10; the rival has no ships docked here
            // B is blockaded by the rival too, so the empty known planet B is not a tier 2 destination and tier 3 is reached.
            var low = Plan(f, BlockadeView.WithBlockers(("B", 5f, 1), ("D", 5f, 1)), Doomed("C", 0.95f));
            ok &= Check(low.Actions.Count == 1 && low.Actions[0].Target == "D" && low.Retreats[0].Tier == 3
                        && Near(low.Retreats[0].RememberedBlockade, 5f) && Near(low.Retreats[0].MyOffense, 10f),
                "tier 3: D is a war rival's planet with remembered blockade 5, under 10 / 1.1");
            var high = Plan(f, BlockadeView.WithBlockers(("B", 5f, 1), ("D", 20f, 1)), Doomed("C", 0.95f));
            ok &= Check(high.Actions.Count == 0 && high.Holds.Count == 1 && high.Holds[0].Reason == RetreatPlanner.HoldNoDestination,
                "a remembered blockade of 20 is over 10 / 1.1: no destination, the ships stay (RetreatHeld NoDestination)");
        }

        // At a planet I populate, only a wipe-out is gated.
        using (var f = CombatSelfCheck.Fixture.Line())
        {
            Prepare(f);
            f.Colonize("B", 0); f.Colonize("C", 0); f.Colonize("D", 1);
            f.Ships("C", 0, 2); f.Ships("C", 1, 3); f.War(0, 1);
            var hurt = Plan(f, null, Doomed("C", 0.6f));
            ok &= Check(hurt.Actions.Count == 0 && hurt.Holds.Count == 1 && hurt.Holds[0].Reason == RetreatPlanner.HoldOwnPlanetNotWiped,
                "my own colony, 60% projected loss: the garrison stays (RetreatHeld OwnPlanetNotWiped)");
            var wiped = Plan(f, null, Doomed("C", 1f));
            ok &= Check(wiped.Actions.Count == 1 && wiped.Actions[0].Target == "B", "my own colony, wiped out: the group retreats to B");
        }
        return ok;
    }

    // The other planners keep off a retreating group: no garrison state for it, never an assault target.
    public static bool RunPlannerExclusionCheck()
    {
        var ok = true;
        using (var f = CombatSelfCheck.Fixture.Line())
        {
            f.Colonize("A", 0);
            f.Ships("C", 0, 2);                                        // not colonized: a stranded, source-only state
            var planner = new ShipTransportPlanner(f.Map, 0);
            ok &= Check(planner.BuildStates().Any(s => s.Planet.PlanetName == "C"), "without a retreat C (my ships on an uncolonized planet) has a state");
            planner = new ShipTransportPlanner(f.Map, 0) { Retreating = new List<string> { "C" } };
            ok &= Check(!planner.BuildStates().Any(s => s.Planet.PlanetName == "C"), "a retreating planet has no state: neither a source nor a sink");
        }
        // A planet inside its retreat cooldown is not refilled by the garrison planner (it stays a source for any ship still there).
        using (var f = CombatSelfCheck.Fixture.Line())
        {
            f.Colonize("A", 0);                                        // outer (B is uncolonized): it carries a garrison
            f.Ships("A", 0, 2);
            var plain = new ShipTransportPlanner(f.Map, 0).BuildStates().First(s => s.Planet.PlanetName == "A");
            ok &= Check(plain.Garrison > 0, "precondition: the outer colony A carries a garrison without a cooldown");
            var blocked = new ShipTransportPlanner(f.Map, 0) { RefillBlocked = new List<string> { "A" } }
                .BuildStates().First(s => s.Planet.PlanetName == "A");
            ok &= Check(blocked.Garrison == 0 && blocked.Deficit == 0, "inside the cooldown A's garrison is 0, so it is never a target");
            ok &= Check(blocked.Spare == 2, "its two docked ships are still spare: the cooldown only stops refills, it does not pin ships");
        }
        using (var f = CombatSelfCheck.Fixture.Line())
        {
            f.Ships("A", 0, 1);
            var view = BlockadeView.Of("C");
            var assault = new AssaultPlanner(f.Map, 0, view, f.Stats);
            ok &= Check(assault.ChooseBlockadeTarget(out _)?.PlanetName == "C", "without an exclusion the blockaded planet C is the target");
            assault = new AssaultPlanner(f.Map, 0, view, f.Stats) { ExcludedTargets = new HashSet<string> { "C" } };
            ok &= Check(assault.ChooseBlockadeTarget(out _) == null, "an excluded planet is not a blockade target");
        }
        return ok;
    }

    private static PlayerAI MakeAI(CombatSelfCheck.Fixture f, int id, List<GameObject> gos)
    {
        var go = new GameObject("RetreatPlayer" + id);
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

    // End to end through PlayerAI: the projection result in, a retreat ship action out first, a cooldown set; legacy mode untouched.
    public static bool RunPlayerAIRetreatCheck()
    {
        var ok = true;
        var gos = new List<GameObject>();
        try
        {
            using (var f = CombatSelfCheck.Fixture.Line())
            {
                f.Constants.retreatSteepness = 0.001f;
                f.Constants.retreatDestinationCostExponent = 50f;
                f.Constants.maxPathNodesForKnowledge = 8;
                f.Constants.maxPathNodesForShipTransport = 10;
                f.Colonize("A", 0); f.Colonize("B", 0); f.Colonize("D", 1);
                f.Ships("C", 0, 1); f.Ships("C", 1, 3); f.War(0, 1);
                f.Map.Knowledge.Update(f.Map, 2, 8);
                var ai = MakeAI(f, 0, gos);
                var results = new List<Planet.UpdateResult>();
                GameAI.AppendFightProjections(f.Map, f.Stats, f.Constants, new List<GameAI.GameAIOrder>(), results);
                GameAI.Rand = new System.Random(11);
                var actions = ai.PlanShipActions(5, results);
                ok &= Check(actions.Count > 0 && actions[0].Origin == "C" && actions[0].Target == "B" && actions[0].Count == 1,
                    "the retreat action comes first: C to B, the one ship");
                ok &= Check(ai.RetreatCooldownUntil("C") == 5 + f.Constants.retreatCooldownTurns, "C is off the assault list for retreatCooldownTurns");
                ok &= Check(actions.Count(a => a.Origin == "C") == 1, "no other planner sends the retreating ship anywhere else");

                var orders = new List<GameAI.GameAIOrder>();
                GameAI.Rand = new System.Random(11);
                ai.ProcessShipActions(orders, results);
                ok &= Check(orders.Any(o => o.Type == GameAI.GameAIOrder.OrderType.OrderTypeShipTransport && o.Origin == "C" && o.Target == "B"),
                    "ProcessShipActions turns it into the ordinary ship-transport order trio (no new order type)");

                f.Map.Diplomacy.Enabled = false;
                var legacy = new List<Planet.UpdateResult>();
                GameAI.AppendFightProjections(f.Map, f.Stats, f.Constants, new List<GameAI.GameAIOrder>(), legacy);
                ok &= Check(legacy.Count == 0, "legacy mode: no projections, so there is nothing to retreat from");
            }
        }
        finally { foreach (var go in gos) Object.DestroyImmediate(go); }
        return ok;
    }

    public static bool RunRetreatTrackerCheck()
    {
        var ok = true;
        var tracker = new RetreatTracker();
        RetreatTracker.Entry Stay(string planet, float loss = 0.4f)
            => new RetreatTracker.Entry { Planet = planet, Kind = RetreatTracker.KindStay, Reason = "", LossFraction = loss, PRetreat = 0.2f };
        RetreatTracker.Entry Held(string planet, string reason)
            => new RetreatTracker.Entry { Planet = planet, Kind = RetreatTracker.KindHeld, Reason = reason, LossFraction = 0.9f, PRetreat = 0f };

        var first = tracker.Update(new[] { Stay("X"), Held("Y", RetreatPlanner.HoldNoDestination) });
        ok &= Check(first.Select(e => e.Planet).SequenceEqual(new[] { "X", "Y" }), "the first sighting of each planet is reported, ordered by name");
        ok &= Check(tracker.Update(new[] { Stay("X", 0.6f), Held("Y", RetreatPlanner.HoldNoDestination) }).Count == 0,
            "the same kind and reason again (even with a different loss) is not reported: a standoff does not log every turn");
        var changed = tracker.Update(new[] { Stay("X"), Held("Y", RetreatPlanner.HoldOwnPlanetNotWiped) });
        ok &= Check(changed.Count == 1 && changed[0].Planet == "Y", "a changed reason is reported");
        tracker.Update(new RetreatTracker.Entry[0]);
        ok &= Check(tracker.Update(new[] { Stay("X") }).Count == 1, "a planet that left the gate is forgotten, so a later one is reported afresh");
        tracker.Clear();
        ok &= Check(tracker.Update(new[] { Stay("X") }).Count == 1, "Clear forgets everything");
        return ok;
    }

    // What RetreatArrive logs as the blockade found on landing: the largest single rival's docked offense, so it stays
    // comparable with the remembered value whatever ships of mine have docked since (the value against me falls as mine land).
    public static bool RunLargestOtherOffenseCheck()
    {
        var ok = true;
        using (var f = CombatSelfCheck.Fixture.Line())
        {
            f.Ships("C", 0, 3);                       // my ships landed
            f.Ships("C", 1, 2); f.Ships("C", 2, 1);   // rivals: 20 and 10 offense
            var blockade = new BlockadeSystem(f.Map, f.Research);
            ok &= Check(Near(blockade.LargestOtherDockedOffense(f.P("C"), 0), 20f), "the largest single other player's offense is 20 (player 1's two ships), not their sum");
            ok &= Check(Near(blockade.Value(f.P("C"), 0, out _), 0f), "the blockade value against me is 0 once my 30 offense has landed: that is why it cannot show staleness");
            ok &= Check(Near(blockade.LargestOtherDockedOffense(f.P("D"), 0), 0f), "an empty planet: 0");
            ok &= Check(Near(blockade.LargestOtherDockedOffense(f.P("C"), 1), 30f), "from player 1's side the largest other is player 0 with 30");
        }
        return ok;
    }

    // Imperfect intel: the projection is also run against a rival seen at a factor of 1 +- retreatRivalUncertainty, so an even
    // fight reads as lost in some samples and won in others instead of exactly 0% or 100%.
    public static bool RunPerceivedProjectionCheck()
    {
        var ok = true;
        using (var f = CombatSelfCheck.Fixture.Line())
        {
            f.Ships("A", 0, 3); f.Ships("A", 1, 3); f.War(0, 1);
            var none = new GameAI.GameAIOrder[0];
            var weaker = FightProjector.Project(f.Map, f.P("A"), 0, f.Stats, f.Constants, none, 0.75f);
            var stronger = FightProjector.Project(f.Map, f.P("A"), 0, f.Stats, f.Constants, none, 1.25f);
            ok &= Check(!weaker.RivalSurvives && weaker.ProjectedLossFraction < 1f, "a rival seen at 0.75 of its offense and health: I win, with a loss under 100%");
            ok &= Check(stronger.RivalSurvives && stronger.MyGroupWiped, "a rival seen at 1.25: it survives and my group is wiped");

            var exact = Project(f, "A", 0);
            var perceived = FightProjector.ProjectPerceived(f.Map, f.P("A"), 0, f.Stats, f.Constants, none, new System.Random(5));
            ok &= Check(Near(perceived.ProjectedLossFraction, exact.ProjectedLossFraction), "the exact projection is kept as it was");
            ok &= Check(perceived.Samples.Count == f.Constants.retreatUncertaintySamples, "one sample per retreatUncertaintySamples (8)");
            ok &= Check(perceived.Samples.All(s => s.Factor >= 0.75f - 0.001f && s.Factor <= 1.25f + 0.001f)
                        && perceived.Samples.Any(s => s.Factor < 1f) && perceived.Samples.Any(s => s.Factor > 1f),
                "the factors are spread over 1 +- 25%, on both sides of 1 (a stratified draw with a random phase)");
            ok &= Check(perceived.Samples.Any(s => s.RivalSurvives) && perceived.Samples.Any(s => !s.RivalSurvives),
                "an even fight is judged lost in some samples and won in others");
            ok &= Check(perceived.PerceivedLossFraction > 0f && perceived.PerceivedLossFraction < 1f
                        && perceived.PerceivedRivalSurvivesShare > 0f && perceived.PerceivedRivalSurvivesShare < 1f,
                "so the perceived loss and the share of samples where the rival survives are strictly between 0 and 1");

            f.Constants.retreatRivalUncertainty = 0f;
            var off = FightProjector.ProjectPerceived(f.Map, f.P("A"), 0, f.Stats, f.Constants, none, new System.Random(5));
            ok &= Check(off.Samples.Count == 0 && Near(off.PerceivedLossFraction, off.ProjectedLossFraction),
                "uncertainty 0 switches it off: no samples, the perceived loss is the exact loss");
            f.Constants.retreatRivalUncertainty = 0.25f;
            f.Constants.retreatUncertaintySamples = 0;
            ok &= Check(FightProjector.ProjectPerceived(f.Map, f.P("A"), 0, f.Stats, f.Constants, none, new System.Random(5)).Samples.Count == 0,
                "0 samples switches it off too");
            f.Constants.retreatUncertaintySamples = 8;

            var results = new List<Planet.UpdateResult>();
            GameAI.AppendFightProjections(f.Map, f.Stats, f.Constants, new List<GameAI.GameAIOrder>(), results, new System.Random(3));
            ok &= Check(results.Count == 2 && results.All(r => ((FightProjection)r.Data).Samples.Count == 8),
                "the engine step appends the sampled projection for every player in the fight (drawn before any player decides)");
        }
        return ok;
    }

    private static FightProjection Sampled(FightProjection exact, params (float loss, bool survives, bool wiped)[] samples)
    {
        foreach (var s in samples)
            exact.Samples.Add(new FightProjection.Sample { Factor = 1f, LossFraction = s.loss, RivalSurvives = s.survives, Wiped = s.wiped });
        return exact;
    }

    // The planner reads the perceived values (the mean loss, the share of samples where the rival survives or the group is wiped).
    public static bool RunPerceivedPlannerCheck()
    {
        var ok = true;
        using (var f = CombatSelfCheck.Fixture.Line())
        {
            f.Constants.retreatSteepness = 0.001f; f.Constants.retreatDestinationCostExponent = 50f;
            GameAI.Rand = new System.Random(3);
            f.Colonize("A", 0); f.Colonize("B", 0); f.Colonize("D", 1);
            f.Ships("C", 0, 1); f.Ships("C", 1, 3); f.War(0, 1);

            // The exact projection says "win, 0% lost"; the samples say 60% lost on average and the rival survives in half of them.
            var gated = Plan(f, null, Sampled(Doomed("C", 0f, rivalSurvives: false),
                (1f, true, true), (1f, true, true), (0.2f, false, false), (0.2f, false, false)));
            ok &= Check(gated.Actions.Count == 1 && gated.Actions[0].Target == "B", "perceived loss 0.6 with the rival surviving in half the samples passes the gate");
            ok &= Check(gated.Retreats.Count == 1 && Near(gated.Retreats[0].LossFraction, 0.6f) && Near(gated.Retreats[0].ExactLossFraction, 0f),
                "the retreat records the perceived loss (0.6) and the exact one (0)");

            var rare = Plan(f, null, Sampled(Doomed("C", 0f, rivalSurvives: false),
                (1f, true, true), (0.5f, false, false), (0.5f, false, false), (0.5f, false, false)));
            ok &= Check(rare.Actions.Count == 0, "the rival survives in only a quarter of the samples: under the half, no gate");
        }
        using (var f = CombatSelfCheck.Fixture.Line())    // my own colony: gated when a wipe-out is seen in at least half the samples
        {
            f.Constants.retreatSteepness = 0.001f; f.Constants.retreatDestinationCostExponent = 50f;
            GameAI.Rand = new System.Random(3);
            f.Colonize("B", 0); f.Colonize("C", 0); f.Colonize("D", 1);
            f.Ships("C", 0, 2); f.Ships("C", 1, 3); f.War(0, 1);
            var wipe = Plan(f, null, Sampled(Doomed("C", 0.5f),
                (1f, true, true), (1f, true, true), (0.4f, true, false), (0.4f, true, false)));
            ok &= Check(wipe.Actions.Count == 1 && wipe.Actions[0].Target == "B", "own colony: a wipe-out in half the samples (mean loss 0.7) retreats");
            var hurt = Plan(f, null, Sampled(Doomed("C", 0.5f),
                (1f, true, true), (0.8f, true, false), (0.8f, true, false), (0.8f, true, false)));
            ok &= Check(hurt.Actions.Count == 0 && hurt.Holds.Count == 1 && hurt.Holds[0].Reason == RetreatPlanner.HoldOwnPlanetNotWiped,
                "own colony: a wipe-out in only a quarter of the samples stays (RetreatHeld OwnPlanetNotWiped)");
        }
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
            ok &= Check(Near(c.retreatRivalUncertainty, 0.25f) && c.retreatUncertaintySamples == 8 && Near(c.retreatDestinationCostExponent, 2f),
                "retreatRivalUncertainty 0.25, retreatUncertaintySamples 8, retreatDestinationCostExponent 2");
        }
        finally { Object.DestroyImmediate(c); }
        return ok;
    }
}
