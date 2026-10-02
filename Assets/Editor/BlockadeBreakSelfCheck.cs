using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using FlatSpace.AI;
using Flatspace.Objects.Production;

public static class BlockadeBreakSelfCheck
{
    [MenuItem("FlatSpace/AI/Run Blockade Breaking Self-Check")]
    public static void Run() => RunChecks();

    public static bool RunChecks()
    {
        var ok = RunIncomingOffenseCheck();
        ok &= RunBlockadeTargetCheck();
        ok &= RunBlockadeRankingCheck();
        ok &= RunChokepointRankingCheck();
        ok &= RunChokepointColonizationCheck();
        ok &= RunBlockadePlanCheck();
        ok &= RunTrackerCheck();
        ok &= RunPlanShipActionsCheck();
        ok &= RunResearchBoostCheck();
        ok &= RunTargetKindsCheck();
        ok &= RunHeldPlanetsCheck();
        ok &= RunSkippedCandidatesCheck();
        ok &= RunSkipTrackerCheck();
        Debug.Log(ok
            ? "[BlockadeBreakSelfCheck] ALL PASSED"
            : "[BlockadeBreakSelfCheck] FAILURES (see errors above)");
        return ok;
    }

    private static bool Check(bool condition, string label)
    {
        if (!condition) Debug.LogError($"[BlockadeBreakSelfCheck] FAIL: {label}");
        return condition;
    }

    private static bool Near(float a, float b) => Mathf.Abs(a - b) < 0.01f;

    // Every planet needs a distinct position (PathingSystem.FindPath tie-break fragility).
    private static PlanetSpawnData Spawn(string name, float x, float y, IEnumerable<string> connections = null)
    {
        var resourceData = ScriptableObject.CreateInstance<PlanetResourceData>();
        resourceData._initialPopulation = 0;
        resourceData._maxPopulation = 5;
        resourceData._baseFoodProduction = 1f;

        var spawn = ScriptableObject.CreateInstance<PlanetSpawnData>();
        spawn._planetName = name;
        spawn._planetPosition = new Vector3(x, y, 0f);
        spawn._planetType = Planet.PlanetType.PlanetTypeNormal;
        spawn._resourceData = resourceData;
        spawn._connections = connections != null ? new List<string>(connections) : new List<string>();
        return spawn;
    }

    // Warship template: offense 10 (max 30); MakeResearch has five Offense tiers, so each tier a ship carries adds 4.
    // An empty snapshot is offense 10, {"Off 1"} is 14, {"Off 1","Off 2"} is 18.
    private static readonly string[] None = new string[0];
    private static readonly string[] Off1 = { "Off 1" };
    private static readonly string[] Off12 = { "Off 1", "Off 2" };

    private sealed class Scenario : System.IDisposable
    {
        public GameObject MapGo, PlayerGo;
        public GameAIMap Map;
        public PlayerAI AI;
        public GameAIConstants Constants;
        public ShipData Template;
        public List<CatalogItem> Research;
        public BlockadeMemory Memory = new BlockadeMemory();

        private static Scenario Create(string name)
        {
            var s = new Scenario();
            s.Template = WarshipSelfCheck.MakeTemplate();
            s.Constants = WarshipSelfCheck.MakeConstants(s.Template);
            s.Constants.defaultTravelSpeed = 1f;
            s.Constants.maxPathNodesForShipTransport = 10;
            s.Constants.maxPathNodesForKnowledge = 6;
            s.Constants.blockadeMemoryTurns = 20;
            s.Constants.garrisonOuter = 6;
            s.Research = WarshipSelfCheck.MakeResearch();
            s.MapGo = new GameObject("BBSelfCheckMap_" + name);
            s.PlayerGo = new GameObject("BBSelfCheckPlayer_" + name);
            return s;
        }

        private void Finish(params string[] playerZeroPlanets)
        {
            foreach (var planet in playerZeroPlanets) Colonize(planet, 0);
            var player = PlayerGo.AddComponent<Player>();
            AI = PlayerGo.AddComponent<PlayerAI>();
            AI.Player = player;
            AI.AIMap = Map;
            player.playerID = 0;
            AI.ResearchCatalog = PlayerGo.AddComponent<Catalog>();
            AI.ResearchCatalog.catalogItems = Research;
            Map.Knowledge.Update(Map, 2, 6);
        }

        // A(0,0) - B(100,0) - C(200,0) - D(300,0), plus an unconnected Z(900,0). Player 0 holds A and B, player 1 holds D.
        public static Scenario Line()
        {
            var s = Create("Line");
            s.Map = s.MapGo.AddComponent<GameAIMap>();
            s.Map.GameAIMapInit(new List<PlanetSpawnData>
            {
                Spawn("A", 0f, 0f, new[] { "B" }),
                Spawn("B", 100f, 0f, new[] { "A", "C" }),
                Spawn("C", 200f, 0f, new[] { "B", "D" }),
                Spawn("D", 300f, 0f, new[] { "C" }),
                Spawn("Z", 900f, 0f),
            }, s.Constants);
            s.Colonize("D", 1);
            s.Finish("A", "B");
            return s;
        }

        // A(0,0) - B(100,0); B - C1(200,0); B - C2(100,150). Player 0 holds A and B.
        public static Scenario Fork()
        {
            var s = Create("Fork");
            s.Map = s.MapGo.AddComponent<GameAIMap>();
            s.Map.GameAIMapInit(new List<PlanetSpawnData>
            {
                Spawn("A", 0f, 0f, new[] { "B" }),
                Spawn("B", 100f, 0f, new[] { "A", "C1", "C2" }),
                Spawn("C1", 200f, 0f, new[] { "B" }),
                Spawn("C2", 100f, 150f, new[] { "B" }),
            }, s.Constants);
            s.Finish("A", "B");
            return s;
        }

        // The ChokepointSelfCheck hub: A(0,0) - H(100,0) with H - X1, X2, X3, and A - Y(0,80). H is the chokepoint
        // (percentile 1), A 0.8, the rest leaves. Player 0 holds A.
        public static Scenario Hub()
        {
            var s = Create("Hub");
            s.Constants.maxPathNodesForColonization = 6;
            s.Map = s.MapGo.AddComponent<GameAIMap>();
            s.Map.GameAIMapInit(ChokepointSelfCheck.HubSpawns(), s.Constants);
            s.Finish("A");
            return s;
        }

        public Planet P(string name) => Map.GetPlanet(name);

        public void Colonize(string name, int player)
        {
            var planet = Map.GetPlanet(name);
            planet.Owner = player;
            planet.Population.Add(new Planet.Inhabitant { Player = player });
        }

        public void Dock(string planet, int owner, params string[][] snapshots)
        {
            foreach (var snapshot in snapshots)
                P(planet).DockShipFromSave(Ship.ShipKind.WarShip, owner, new List<string>(snapshot));
        }

        public void Ships(string planet, int owner, int count)
        {
            for (var i = 0; i < count; i++) Dock(planet, owner, None);
        }

        public BlockadeView View(int turn)
            => BlockadeView.Build(Map, 0, new BlockadeSystem(Map, Research), Memory, turn, Constants.blockadeMemoryTurns);

        public AssaultPlanner Planner(BlockadeView view, int turn)
            => new AssaultPlanner(Map, 0, view, new WarshipStats(Research), Memory, turn);

        public void Dispose()
        {
            WarshipSelfCheck.DestroyAll(Research);
            Object.DestroyImmediate(MapGo);
            Object.DestroyImmediate(PlayerGo);
            Object.DestroyImmediate(Constants);
            Object.DestroyImmediate(Template);
        }
    }

    private static GameAI.GameAIOrder ShipOrder(string target, int owner, Ship.ShipKind kind, params string[][] snapshots)
        => new GameAI.GameAIOrder
        {
            Type = GameAI.GameAIOrder.OrderType.OrderTypeShipTransport,
            Target = target,
            PlayerId = owner,
            Data = snapshots.Length,
            Fleet = new GameAI.GameAIOrder.ShipFleetPayload
            {
                Kind = kind,
                Snapshots = snapshots.Select(x => new List<string>(x)).ToList(),
            },
        };

    // In-flight offense is recomputed from the in-flight ship orders: real per-ship offense from each snapshot, per
    // (kind, owner), warships only; a null fleet and a ColonyShip fleet add nothing, and recomputing never accumulates.
    public static bool RunIncomingOffenseCheck()
    {
        var ok = true;
        using (var s = Scenario.Line())
        {
            var stats = new WarshipStats(s.Research);
            var c = s.P("C");
            ok &= Check(Near(c.GetIncomingOffense(Ship.ShipKind.WarShip, 0), 0f), "no orders: no incoming offense");

            var orders = new List<GameAI.GameAIOrder>
            {
                ShipOrder("C", 0, Ship.ShipKind.WarShip, None, Off1),     // 10 + 14
                ShipOrder("C", 0, Ship.ShipKind.WarShip, Off12),          // 18
                ShipOrder("C", 1, Ship.ShipKind.WarShip, None),           // another player's fleet: 10
                ShipOrder("C", 0, Ship.ShipKind.ColonyShip, None),        // not a warship: ignored
                new GameAI.GameAIOrder
                {
                    Type = GameAI.GameAIOrder.OrderType.OrderTypeShipTransport, Target = "C", PlayerId = 0, Data = 1,
                    Fleet = null,                                         // no payload: ignored, no exception
                },
                new GameAI.GameAIOrder
                {
                    Type = GameAI.GameAIOrder.OrderType.OrderTypeFoodTransport, Target = "C", PlayerId = 0, Data = 5f,
                },                                                        // not a ship order: ignored
                ShipOrder("Nowhere", 0, Ship.ShipKind.WarShip, None),     // unknown target: ignored
            };
            s.Map.RecomputeIncomingOffense(orders, stats);
            ok &= Check(Near(c.GetIncomingOffense(Ship.ShipKind.WarShip, 0), 42f),
                "player 0's incoming offense at C is 10 + 14 + 18 = 42 (bad orders ignored)");
            ok &= Check(Near(c.GetIncomingOffense(Ship.ShipKind.WarShip, 1), 10f), "per owner: player 1's fleet is 10");
            ok &= Check(Near(c.GetIncomingOffense(Ship.ShipKind.ColonyShip, 0), 0f), "colony ships add no offense");

            s.Map.RecomputeIncomingOffense(orders, stats);
            ok &= Check(Near(c.GetIncomingOffense(Ship.ShipKind.WarShip, 0), 42f), "recomputing again does not accumulate");

            s.Map.RecomputeIncomingOffense(new List<GameAI.GameAIOrder>(), stats);
            ok &= Check(Near(c.GetIncomingOffense(Ship.ShipKind.WarShip, 0), 0f), "no orders in flight clears it");

            c.AddIncomingOffense(Ship.ShipKind.WarShip, 0, -5f);
            ok &= Check(Near(c.GetIncomingOffense(Ship.ShipKind.WarShip, 0), 0f), "the counter never goes below 0");
        }
        return ok;
    }

    public static bool RunBlockadeTargetCheck()
    {
        var ok = true;
        using (var s = Scenario.Line())
        {
            s.Ships("A", 0, 3);
            s.Ships("C", 1, 2);                       // player 1 blockades C against me: value 2 x 10 = 20
            var view = s.View(10);
            var planner = s.Planner(view, 10);

            var target = planner.ChooseBlockadeTarget(out var reason);
            ok &= Check(target == s.P("C") && reason == AssaultPlanner.ReasonCheapest,
                "a visibly blockaded, reachable planet is the target (the only candidate decides on 'Cheapest')");
            ok &= Check(planner.IsBlockadeTarget(s.P("C")) && !planner.IsBlockadeTarget(s.P("D")),
                "IsBlockadeTarget is true for the blockaded planet only");
            ok &= Check(Near(planner.NeededOffense(s.P("C")), 22f),
                "needed = value 20 x (1 + margin 0.1) = 22");
            s.P("C").AddIncomingOffense(Ship.ShipKind.WarShip, 0, 8f);
            ok &= Check(Near(planner.NeededOffense(s.P("C")), 14f), "offense already in flight is subtracted: 22 - 8 = 14");
            ok &= Check(planner.ChooseTarget() == s.P("C"), "ChooseTarget picks the blockade target first");

            // An enemy-occupied planet D exists, but the blockade outranks it.
            ok &= Check(planner.ChooseEnemyTarget() == s.P("D"), "the enemy-occupied fallback still finds D");
        }

        // No blockade anywhere: the existing enemy-occupied rule runs unchanged.
        using (var s = Scenario.Line())
        {
            s.Ships("A", 0, 3);
            var planner = s.Planner(s.View(10), 10);
            ok &= Check(planner.ChooseBlockadeTarget(out var reason) == null && reason == null,
                "nothing blockaded: no blockade target");
            ok &= Check(planner.ChooseTarget() == s.P("D"), "nothing blockaded: the enemy-occupied planet D is the target");
        }

        // Review focus 5: no view at all (the old constructor, every existing caller) behaves exactly as before.
        using (var s = Scenario.Line())
        {
            s.Ships("A", 0, 3);
            s.Ships("C", 1, 2);
            var legacy = new AssaultPlanner(s.Map, 0);
            ok &= Check(legacy.ChooseBlockadeTarget(out _) == null, "no view: no blockade candidates");
            ok &= Check(!legacy.IsBlockadeTarget(s.P("C")), "no view: nothing is a blockade target");
            ok &= Check(legacy.ChooseTarget() == s.P("D"),
                "no view: ChooseTarget is the enemy-occupied rule (C holds enemy ships but no population, D is the target)");
            ok &= Check(Near(legacy.NeededOffense(s.P("C")), 0f), "no view: nothing is needed anywhere");
        }

        // Review focus 1: a planet known only from memory (no blocker, value only) is a valid target and nothing throws.
        using (var s = Scenario.Line())
        {
            s.Ships("A", 0, 3);
            var remembered = new AssaultPlanner(s.Map, 0, BlockadeView.WithValues(("C", 20f)),
                new WarshipStats(s.Research), s.Memory, 10);
            ok &= Check(remembered.ChooseBlockadeTarget(out _) == s.P("C"),
                "a blockade with no known blocker (remembered, unseen) is still a target");
            ok &= Check(Near(remembered.NeededOffense(s.P("C")), 22f), "its remembered value sizes the need: 20 x 1.1");
        }

        // Review focus 2: a blockaded planet nobody of mine can reach is not a candidate, so the fallback runs.
        using (var s = Scenario.Line())
        {
            s.Ships("A", 0, 3);
            var unreachable = new AssaultPlanner(s.Map, 0, BlockadeView.WithValues(("Z", 20f)),
                new WarshipStats(s.Research), s.Memory, 10);
            ok &= Check(unreachable.ChooseBlockadeTarget(out _) == null, "an unreachable blockaded planet is not a candidate");
            ok &= Check(unreachable.ChooseTarget() == s.P("D"), "so the enemy-occupied fallback (D) runs");
        }
        return ok;
    }

    // Ranking: committed offense, then a recent cut, then the smallest offense still needed, then path cost, then name.
    public static bool RunBlockadeRankingCheck()
    {
        var ok = true;

        // C1: 3 enemy ships (value 30, cheaper path); C2: 2 enemy ships (value 20, dearer path).
        using (var s = Scenario.Fork())
        {
            s.Ships("A", 0, 2); s.Ships("C1", 1, 3); s.Ships("C2", 1, 2);
            var planner = s.Planner(s.View(10), 10);
            ok &= Check(planner.ChooseBlockadeTarget(out var reason) == s.P("C2") && reason == AssaultPlanner.ReasonCheapest,
                "nothing committed, no cut: the smaller need (C2: 22 against C1: 33) wins");
        }

        using (var s = Scenario.Fork())
        {
            s.Ships("A", 0, 2); s.Ships("C1", 1, 3); s.Ships("C2", 1, 2);
            s.Memory.Learn("C1", 30f, 8, 20);               // turn 10: cut 2 turns ago, inside the 5-turn window
            var planner = s.Planner(s.View(10), 10);
            ok &= Check(planner.ChooseBlockadeTarget(out var reason) == s.P("C1") && reason == AssaultPlanner.ReasonRecentCut,
                "a planet that cut my order 2 turns ago outranks a smaller need");
        }

        using (var s = Scenario.Fork())
        {
            s.Ships("A", 0, 2); s.Ships("C1", 1, 3); s.Ships("C2", 1, 2);
            s.Memory.Learn("C1", 30f, 3, 20);               // turn 10: cut 7 turns ago, outside the window
            var planner = s.Planner(s.View(10), 10);
            ok &= Check(planner.ChooseBlockadeTarget(out _) == s.P("C2"),
                "a cut older than blockadeTargetRecentTurns no longer outranks the smaller need");
        }

        using (var s = Scenario.Fork())
        {
            s.Ships("A", 0, 2); s.Ships("C1", 1, 3); s.Ships("C2", 1, 2);
            s.Ships("C1", 0, 1);                            // I already hold one ship (offense 10) at C1
            var planner = s.Planner(s.View(10), 10);
            ok &= Check(planner.CommittedOffense(s.P("C1")) > 9.99f, "docked offense counts as committed");
            ok &= Check(planner.ChooseBlockadeTarget(out var reason) == s.P("C1") && reason == AssaultPlanner.ReasonCommitted,
                "committed offense wins first, even though C1's remaining value (20) ties C2's");
        }

        using (var s = Scenario.Fork())
        {
            s.Ships("A", 0, 2); s.Ships("C1", 1, 3); s.Ships("C2", 1, 2);
            s.P("C2").AddIncomingShips(Ship.ShipKind.WarShip, 0, 1);
            s.P("C2").AddIncomingOffense(Ship.ShipKind.WarShip, 0, 10f);
            var planner = s.Planner(s.View(10), 10);
            ok &= Check(planner.ChooseBlockadeTarget(out var reason) == s.P("C2") && reason == AssaultPlanner.ReasonCommitted,
                "offense in flight toward a planet counts as committed");
        }
        return ok;
    }

    // The chokepoint step: after committed offense and a recent cut, before the smaller need and the cheaper path.
    // Hub layout: H is a chokepoint (percentile 1), Y a leaf (0); H needs more offense and is no cheaper than Y.
    public static bool RunChokepointRankingCheck()
    {
        var ok = true;

        using (var s = Scenario.Hub())
        {
            s.Ships("A", 0, 2); s.Ships("H", 1, 3); s.Ships("Y", 1, 1);   // H value 30 (need 33), Y value 10 (need 11)
            var planner = s.Planner(s.View(10), 10);
            ok &= Check(Near(s.Map.Chokepoint("H"), 1f) && Near(s.Map.Chokepoint("Y"), 0f), "hub layout: H is the top chokepoint, Y a leaf");
            ok &= Check(planner.ChooseBlockadeTarget(out var reason) == s.P("H") && reason == AssaultPlanner.ReasonChokepoint,
                "nothing committed, no cut: the chokepoint H outranks the smaller need at Y");
        }

        using (var s = Scenario.Hub())
        {
            s.Ships("A", 0, 2); s.Ships("H", 1, 3); s.Ships("Y", 1, 1);
            s.Memory.Learn("Y", 10f, 8, 20);                // Y cut my order 2 turns ago
            var planner = s.Planner(s.View(10), 10);
            ok &= Check(planner.ChooseBlockadeTarget(out var reason) == s.P("Y") && reason == AssaultPlanner.ReasonRecentCut,
                "a recent cut still outranks the chokepoint step");
        }

        using (var s = Scenario.Hub())
        {
            s.Ships("A", 0, 2); s.Ships("H", 1, 3); s.Ships("Y", 1, 2); s.Ships("Y", 0, 1);   // I hold one ship at Y (value 20 - 10)
            var planner = s.Planner(s.View(10), 10);
            ok &= Check(planner.ChooseBlockadeTarget(out var reason) == s.P("Y") && reason == AssaultPlanner.ReasonCommitted,
                "committed offense still outranks the chokepoint step");
        }

        // Equal chokepoint percentile falls through to the old order (the Fork leaves C1 and C2 are both 0).
        using (var s = Scenario.Fork())
        {
            s.Ships("A", 0, 2); s.Ships("C1", 1, 3); s.Ships("C2", 1, 2);
            var planner = s.Planner(s.View(10), 10);
            ok &= Check(Near(s.Map.Chokepoint("C1"), s.Map.Chokepoint("C2"))
                        && planner.ChooseBlockadeTarget(out var reason) == s.P("C2") && reason == AssaultPlanner.ReasonCheapest,
                "tied chokepoint percentile: the smaller need decides, as before");
        }
        return ok;
    }

    private static GameAI.GameAIOrder LaunchColonist(Scenario s)
    {
        var results = new List<Planet.PlanetUpdateResult>
        {
            new Planet.PlanetUpdateResult("A",
                Planet.PlanetUpdateResult.PlanetUpdateResultType.PlanetUpdateResultTypeColonizerReady, 1, 0),
        };
        var orders = new List<GameAI.GameAIOrder>();
        s.AI.ProcessColonizers(results, orders);
        return orders.Find(o => o.Type == GameAI.GameAIOrder.OrderType.OrderTypePopulationTransport);
    }

    // Consolidate only: the choice cost is route cost / (1 + weight x chokepoint percentile). Hub layout, A is the ready
    // colonizer: Y is a leaf at cost 80, H a chokepoint (percentile 1) at cost 100, X1-X3 leaves at 200 or more.
    public static bool RunChokepointColonizationCheck()
    {
        var ok = true;
        using (var s = Scenario.Hub())
        {
            s.Constants.colonizationChokepointWeight = 0.5f;
            s.AI.Strategy = PlayerAI.AIStrategy.AIStrategyConsolidate;
            var tilted = LaunchColonist(s);
            ok &= Check(tilted != null && tilted.Target == "H",
                "Consolidate, weight 0.5: the hub H (100 / 1.5 = 66.7) beats the nearer leaf Y (80)");
            ok &= Check(tilted != null && tilted.TotalDelay == 100 && tilted.TimingDelay == 100,
                "the order delay comes from the real route cost (100 at speed 1), not the tilted cost");

            s.Constants.colonizationChokepointWeight = 0.1f;
            ok &= Check(LaunchColonist(s)?.Target == "Y", "weight 0.1: H is 100 / 1.1 = 90.9, so the nearer leaf Y wins");

            s.Constants.colonizationChokepointWeight = 0f;
            ok &= Check(LaunchColonist(s)?.Target == "Y", "weight 0 switches the tilt off: nearest first");

            s.Constants.colonizationChokepointWeight = -1f;
            ok &= Check(LaunchColonist(s)?.Target == "Y", "a negative weight is off, not an inverted tilt (review focus 4)");

            s.Constants.colonizationChokepointWeight = 0.5f;
            s.AI.Strategy = PlayerAI.AIStrategy.AIStrategyExpand;
            var expand = LaunchColonist(s);
            ok &= Check(expand != null && expand.Target == "Y" && expand.TotalDelay == 80,
                "Expand ignores the tilt: nearest first, delay 80");
        }
        return ok;
    }

    private static List<ShipTransportPlanner.PlanetState> States(Scenario s)
        => new List<ShipTransportPlanner.PlanetState>
        {
            // RoundGarrison 0: every docked ship is spare, so the checks aim at the assault arithmetic alone.
            new ShipTransportPlanner.PlanetState { Planet = s.P("A"), Docked = s.P("A").DockedShips.Count, RoundGarrison = 0 },
            new ShipTransportPlanner.PlanetState { Planet = s.P("B"), Docked = s.P("B").DockedShips.Count, RoundGarrison = 0 },
        };

    private static ShipAction Find(List<ShipAction> actions, string origin, string target)
        => actions.FirstOrDefault(a => a.Origin == origin && a.Target == target);

    // Sources are walked cheapest path first; each sends only the ships its real offense needs, using the exact ships that
    // would leave (after the ones home defence already claimed); a short fleet sends everything it has spare.
    public static bool RunBlockadePlanCheck()
    {
        var ok = true;

        // Value 30 against my ships at A (10, 14, 18) and B (10, 10); margin 0 for exact arithmetic.
        using (var s = Scenario.Line())
        {
            s.Constants.blockadeBreakMargin = 0f;
            s.Dock("A", 0, None, Off1, Off12); s.Ships("B", 0, 2); s.Ships("C", 1, 3);
            var planner = s.Planner(s.View(10), 10);
            var actions = planner.Plan(s.P("C"), States(s), new List<ShipAction>());
            ok &= Check(actions.Count == 2 && Find(actions, "B", "C").Count == 2 && Find(actions, "A", "C").Count == 1,
                "B (cheaper path) sends both ships (20), A sends one (10): exactly the 30 needed, nothing more");
            ok &= Check(Find(actions, "B", "C").Cost == 100f && Find(actions, "A", "C").Cost == 200f,
                "each action carries its path cost");
            var force = planner.LastBlockadeForce;
            ok &= Check(force.Ships == 3 && Near(force.Offense, 30f) && Near(force.StillNeeded, 0f),
                "LastBlockadeForce reports 3 ships, offense 30, nothing still needed");
        }

        // Partial: 10 enemy ships (value 100) against 62 of mine; everything spare is sent.
        using (var s = Scenario.Line())
        {
            s.Constants.blockadeBreakMargin = 0f;
            s.Dock("A", 0, None, Off1, Off12); s.Ships("B", 0, 2); s.Ships("C", 1, 10);
            var planner = s.Planner(s.View(10), 10);
            var actions = planner.Plan(s.P("C"), States(s), new List<ShipAction>());
            ok &= Check(Find(actions, "B", "C").Count == 2 && Find(actions, "A", "C").Count == 3,
                "a force that cannot cover the need sends every spare ship");
            ok &= Check(Near(planner.LastBlockadeForce.Offense, 62f) && Near(planner.LastBlockadeForce.StillNeeded, 38f),
                "62 sent, 38 still needed");
        }

        // Ships home defence already claimed are skipped by identity: the 18-offense ship is the one left at A.
        using (var s = Scenario.Line())
        {
            s.Constants.blockadeBreakMargin = 0f;
            s.Dock("A", 0, None, Off1, Off12); s.Ships("B", 0, 2); s.Ships("C", 1, 4);   // value 40
            var home = new List<ShipAction>
            {
                new ShipAction { Origin = "A", Target = "B", Count = 2, Kind = Ship.ShipKind.WarShip },
            };
            var planner = s.Planner(s.View(10), 10);
            var actions = planner.Plan(s.P("C"), States(s), home);
            ok &= Check(Find(actions, "A", "C").Count == 1, "A has 3 ships, home defence takes 2: one is left for the assault");
            ok &= Check(Near(planner.LastBlockadeForce.Offense, 38f) && Near(planner.LastBlockadeForce.StillNeeded, 2f),
                "B's 20 plus A's remaining ship (offense 18, not the first-docked 10): 38 sent, 2 still needed");
        }

        // Offense already in flight is subtracted from the need.
        using (var s = Scenario.Line())
        {
            s.Constants.blockadeBreakMargin = 0f;
            s.Ships("A", 0, 3); s.Ships("B", 0, 2); s.Ships("C", 1, 3);                   // value 30
            s.P("C").AddIncomingOffense(Ship.ShipKind.WarShip, 0, 25f);
            var planner = s.Planner(s.View(10), 10);
            var actions = planner.Plan(s.P("C"), States(s), new List<ShipAction>());
            ok &= Check(actions.Count == 1 && actions[0].Origin == "B" && actions[0].Count == 1,
                "30 needed, 25 in flight: one ship (10) from the cheapest source");

            s.P("C").AddIncomingOffense(Ship.ShipKind.WarShip, 0, 10f);                   // now 35 in flight
            ok &= Check(planner.Plan(s.P("C"), States(s), new List<ShipAction>()).Count == 0
                        && planner.LastBlockadeForce.Ships == 0,
                "offense in flight already covers the blockade: nothing is sent");
        }

        // The margin sizes the force above the value: 30 x 1.1 = 33 needs four 10-offense ships.
        using (var s = Scenario.Line())
        {
            s.Ships("A", 0, 3); s.Ships("B", 0, 2); s.Ships("C", 1, 3);
            var actions = s.Planner(s.View(10), 10).Plan(s.P("C"), States(s), new List<ShipAction>());
            ok &= Check(actions.Sum(a => a.Count) == 4, "value 30 with margin 0.1 needs 33 offense: four ships of 10");
        }

        // Review focus 4: no warship stats means every ship counts 0 offense; the plan sends all spare ships and ends.
        using (var s = Scenario.Line())
        {
            s.Constants.blockadeBreakMargin = 0f;
            s.Ships("A", 0, 3); s.Ships("B", 0, 2); s.Ships("C", 1, 3);
            var noStats = new AssaultPlanner(s.Map, 0, s.View(10), null, s.Memory, 10);
            var actions = noStats.Plan(s.P("C"), States(s), new List<ShipAction>());
            ok &= Check(actions.Sum(a => a.Count) == 5, "without stats every spare ship is sent, and the plan terminates");
        }

        // A target that is not blockaded keeps the ship-count rule: 3 enemy ships known at C x 1.5 = ceil(4.5) = 5.
        using (var s = Scenario.Line())
        {
            s.Ships("A", 0, 3); s.Ships("B", 0, 2); s.Ships("C", 1, 3);
            var planner = s.Planner(BlockadeView.WithValues(), 10);   // nothing blockaded
            var actions = planner.Plan(s.P("D"), States(s), new List<ShipAction>());
            ok &= Check(actions.Sum(a => a.Count) == 5 && planner.LastBlockadeForce.Ships == 0,
                "an ordinary enemy-occupied target is still sized by ship count (5), and no blockade force is reported");
        }
        return ok;
    }

    private static BlockadeTargetTracker.Target Tg(string planet, string reason = "Cheapest")
        => new BlockadeTargetTracker.Target { Planet = planet, Blocker = 1, Value = 20f, Needed = 22f, Reason = reason };

    private static string Describe(List<BlockadeTargetTracker.Change> changes)
        => string.Join(",", changes.Select(c => c.Started ? "+" + c.Planet : "-" + c.Planet + ":" + c.EndReason + ":" + c.TurnsHeld));

    // Start and end transitions only (never a line per turn), End before Start in one call, and the end reason:
    // Cleared when the planet is no longer blockaded, Switched when another target replaced it, else Unreachable.
    public static bool RunTrackerCheck()
    {
        var ok = true;
        var t = new BlockadeTargetTracker();
        ok &= Check(Describe(t.Update(1, Tg("C"), n => true)) == "+C", "the first target starts");
        ok &= Check(t.Current == "C", "Current is the tracked planet");
        ok &= Check(t.Update(2, Tg("C"), n => true).Count == 0, "the same target on a later turn reports nothing");
        ok &= Check(Describe(t.Update(5, Tg("D"), n => true)) == "-C:Switched:4,+D",
            "a new target ends the old one as Switched (held 4 turns) and starts D, End first");
        ok &= Check(Describe(t.Update(8, null, n => n != "D")) == "-D:Cleared:3",
            "no target and D no longer blockaded: Cleared after 3 turns");
        ok &= Check(t.Current == null, "Current is empty after an end");
        ok &= Check(t.Update(9, null, n => false).Count == 0, "nothing tracked, nothing reported");
        t.Update(9, Tg("E"), n => true);
        ok &= Check(Describe(t.Update(10, null, n => true)) == "-E:Unreachable:1",
            "no target while E is still blockaded: Unreachable");
        t.Update(12, Tg("F"), n => true);
        t.Clear();
        ok &= Check(t.Update(13, null, n => true).Count == 0 && t.Current == null, "Clear forgets the tracked target silently");
        return ok;
    }

    // End to end through PlayerAI: under Consolidate a blockaded C beats the enemy-occupied D, the home garrison at the
    // outer planet B is kept, and only B's two spare ships go.
    public static bool RunPlanShipActionsCheck()
    {
        var ok = true;
        using (var s = Scenario.Line())
        {
            s.Ships("B", 0, 8);                       // outer garrison 6, so 2 are spare
            s.Ships("C", 1, 3);                       // blockade value 30 against me
            s.AI.Strategy = PlayerAI.AIStrategy.AIStrategyConsolidate;
            s.AI.RefreshBlockadeView(5);
            var actions = s.AI.PlanShipActions(5);
            ok &= Check(actions.Count == 1 && actions[0].Origin == "B" && actions[0].Target == "C" && actions[0].Count == 2,
                "Consolidate sends B's two spare ships at the blockaded C, not at D");
        }
        using (var s = Scenario.Line())
        {
            s.Ships("B", 0, 8);
            s.AI.Strategy = PlayerAI.AIStrategy.AIStrategyConsolidate;
            s.AI.RefreshBlockadeView(5);
            var actions = s.AI.PlanShipActions(5);
            ok &= Check(actions.Count == 1 && actions[0].Target == "D" && actions[0].Count == 2,
                "with nothing blockaded the assault still goes to the enemy-occupied D");
        }
        return ok;
    }

    // A blockaded planet may be enemy-occupied, my own colony or empty; all are targets and all are sized by offense.
    public static bool RunTargetKindsCheck()
    {
        var ok = true;

        // Enemy-occupied: ship-count sizing would send ceil(3 x 1.5) = 5; offense sizing needs 30 x 1.1 = 33, so 4 ships.
        using (var s = Scenario.Line())
        {
            s.Colonize("C", 1);
            s.Ships("A", 0, 5); s.Ships("C", 1, 3);
            var planner = s.Planner(s.View(10), 10);
            ok &= Check(planner.ChooseBlockadeTarget(out _) == s.P("C"), "a blockaded enemy-occupied planet is a blockade target");
            var actions = planner.Plan(s.P("C"), States(s), new List<ShipAction>());
            ok &= Check(actions.Sum(a => a.Count) == 4 && planner.LastBlockadeForce.Ships == 4,
                "it is sized by offense (4 ships), not by the enemy ship count (5)");
        }

        // My own colony with the rival's ships parked on it.
        using (var s = Scenario.Line())
        {
            s.Colonize("C", 0);
            s.Ships("A", 0, 5); s.Ships("C", 1, 3);
            var planner = s.Planner(s.View(10), 10);
            ok &= Check(planner.ChooseBlockadeTarget(out _) == s.P("C"), "a blockaded colony of mine is a blockade target");
            var actions = planner.Plan(s.P("C"), States(s), new List<ShipAction>());
            ok &= Check(actions.Sum(a => a.Count) == 4, "and is sized by offense too (4 ships)");
        }
        return ok;
    }

    // A force that broke a blockade must stay: the planet leaves the view the moment the value reaches 0, so without a hold
    // its ships would become spare the same turn and the blockade would re-form. Held colonies are sink-only for the
    // home plan as well.
    public static bool RunHeldPlanetsCheck()
    {
        var ok = true;

        using (var s = Scenario.Line())
        {
            s.Ships("B", 0, 8);                       // outer garrison 6, so 2 are spare
            s.Ships("C", 0, 4);                       // my force: offense 40
            s.Ships("C", 1, 3);                       // the blocker: offense 30, so the value 30 - 40 is no blockade
            s.AI.Strategy = PlayerAI.AIStrategy.AIStrategyConsolidate;
            s.AI.RefreshBlockadeView(5);
            ok &= Check(!s.AI.CurrentBlockadeView.IsBlockaded("C"), "the standoff at C is no longer a blockade against me");

            var planner = s.Planner(s.AI.CurrentBlockadeView, 5);
            ok &= Check(planner.ContestedHolds().SequenceEqual(new[] { "C" }),
                "C is a contested hold: my warships and an enemy's are both docked there");
            var actions = s.AI.PlanShipActions(5);
            ok &= Check(!actions.Any(a => a.Origin == "C"), "the ships holding C are not sent away");
            ok &= Check(actions.Any(a => a.Origin == "B" && a.Target == "D" && a.Count == 2),
                "B's two spare ships still go on to the enemy-occupied D");

            s.P("C").UndockShips(Ship.ShipKind.WarShip, 1, 3);    // the enemy leaves
            s.AI.RefreshBlockadeView(6);
            ok &= Check(s.Planner(s.AI.CurrentBlockadeView, 6).ContestedHolds().Count == 0,
                "no enemy docked at C any more: nothing to hold");
            ok &= Check(s.AI.PlanShipActions(6).Any(a => a.Origin == "C"), "so the ships at C are spare again");

            var noStats = new AssaultPlanner(s.Map, 0, null, null, s.Memory, 5);
            ok &= Check(noStats.ContestedHolds().Count == 0, "without warship stats nothing counts as contested");
        }

        // A held colony is sink-only: it keeps every ship even above its garrison.
        using (var s = Scenario.Line())
        {
            s.Colonize("C", 0);
            s.Ships("C", 0, 8);                       // an outer colony (D is the rival's): garrison 6, 2 would be spare
            var open = new ShipTransportPlanner(s.Map, 0, PlayerAI.AIStrategy.AIStrategyConsolidate);
            ok &= Check(open.BuildStates().First(x => x.Planet == s.P("C")).Spare == 2,
                "an ordinary outer colony with 8 ships (garrison 6) has 2 spare");

            var byName = new ShipTransportPlanner(s.Map, 0, PlayerAI.AIStrategy.AIStrategyConsolidate) { HeldPlanet = "C" };
            var heldState = byName.BuildStates().First(x => x.Planet == s.P("C"));
            ok &= Check(heldState.Spare == 0 && heldState.Docked == 8, "HeldPlanet: the colony keeps all 8 ships");

            var bySet = new ShipTransportPlanner(s.Map, 0, PlayerAI.AIStrategy.AIStrategyConsolidate)
                { HeldPlanets = new List<string> { "C" } };
            ok &= Check(bySet.BuildStates().First(x => x.Planet == s.P("C")).Spare == 0, "HeldPlanets: the same");
        }
        return ok;
    }

    // Every blockaded planet that is not chosen is reported with why: no usable path, or outranked by the winner (whose
    // committed offense shows whether garrison ships already docked there decided it). Reporting changes no choice.
    public static bool RunSkippedCandidatesCheck()
    {
        var ok = true;

        // C1 (value 30) and C2 (value 20): the smaller need wins, C1 is outranked with nothing committed anywhere.
        using (var s = Scenario.Fork())
        {
            s.Ships("A", 0, 2); s.Ships("C1", 1, 3); s.Ships("C2", 1, 2);
            var planner = s.Planner(s.View(10), 10);
            ok &= Check(planner.ChooseBlockadeTarget(out _) == s.P("C2"), "the choice itself is unchanged: C2 wins");
            var skipped = planner.LastSkipped;
            ok &= Check(skipped.Count == 1 && skipped[0].Planet == "C1" && skipped[0].Reason == AssaultPlanner.SkipOutranked
                        && skipped[0].Winner == "C2" && Near(skipped[0].Value, 30f) && Near(skipped[0].WinnerCommitted, 0f),
                "C1 is Outranked by C2: value 30, the winner has nothing committed");
        }

        // The winner's committed offense is reported: a ship of mine already docked at C1 outranks the smaller need at C2.
        using (var s = Scenario.Fork())
        {
            s.Ships("A", 0, 2); s.Ships("C1", 1, 3); s.Ships("C2", 1, 2); s.Ships("C1", 0, 1);
            var planner = s.Planner(s.View(10), 10);
            ok &= Check(planner.ChooseBlockadeTarget(out _) == s.P("C1"), "the docked ship makes C1 the winner");
            var skipped = planner.LastSkipped;
            ok &= Check(skipped.Count == 1 && skipped[0].Planet == "C2" && skipped[0].Winner == "C1"
                        && Near(skipped[0].WinnerCommitted, 10f),
                "C2 is Outranked by C1 and the report carries C1's committed offense (10)");
        }

        // A planet nobody can reach is reported as NoPath, without a winner.
        using (var s = Scenario.Line())
        {
            s.Ships("A", 0, 3);
            var planner = new AssaultPlanner(s.Map, 0, BlockadeView.WithValues(("C", 20f), ("Z", 25f)),
                new WarshipStats(s.Research), s.Memory, 10);
            ok &= Check(planner.ChooseBlockadeTarget(out _) == s.P("C"), "C is the target");
            var skipped = planner.LastSkipped;
            ok &= Check(skipped.Count == 1 && skipped[0].Planet == "Z" && skipped[0].Reason == AssaultPlanner.SkipNoPath
                        && skipped[0].Winner == "-" && Near(skipped[0].Value, 25f) && Near(skipped[0].WinnerCommitted, 0f),
                "unreachable Z is reported as NoPath with its value and no winner");
        }

        // No view, or nothing blockaded: nothing is reported, and a new call replaces the previous report.
        using (var s = Scenario.Line())
        {
            s.Ships("A", 0, 3);
            var legacy = new AssaultPlanner(s.Map, 0);
            legacy.ChooseBlockadeTarget(out _);
            ok &= Check(legacy.LastSkipped.Count == 0, "no view: nothing skipped");

            var planner = new AssaultPlanner(s.Map, 0, BlockadeView.WithValues(("C", 20f), ("Z", 25f)),
                new WarshipStats(s.Research), s.Memory, 10);
            planner.ChooseBlockadeTarget(out _);
            ok &= Check(planner.LastSkipped.Count == 1, "one skipped planet after the first call");
            ok &= Check(new AssaultPlanner(s.Map, 0, BlockadeView.WithValues(("C", 20f)),
                new WarshipStats(s.Research), s.Memory, 10).LastSkipped.Count == 0,
                "a planner that has not chosen yet reports nothing");
        }
        return ok;
    }

    private static BlockadeSkipTracker.Skip Sk(string planet, string reason, string winner = "-")
        => new BlockadeSkipTracker.Skip { Planet = planet, Reason = reason, Winner = winner, Value = 20f, WinnerCommitted = 0f };

    private static string DescribeSkips(List<BlockadeSkipTracker.Skip> skips)
        => string.Join(",", skips.Select(k => k.Planet + ":" + k.Reason + ":" + k.Winner));

    // One report when a skipped planet first appears or its reason or winner changes; nothing while it stays the same; the
    // state clears when the planet is no longer skipped (chosen or not blockaded), so it is reported again later.
    public static bool RunSkipTrackerCheck()
    {
        var ok = true;
        var t = new BlockadeSkipTracker();
        ok &= Check(DescribeSkips(t.Update(new[] { Sk("C1", "Outranked", "C2") })) == "C1:Outranked:C2", "a new skip is reported");
        ok &= Check(t.Update(new[] { Sk("C1", "Outranked", "C2") }).Count == 0, "the same skip on a later turn is not repeated");
        ok &= Check(DescribeSkips(t.Update(new[] { Sk("C1", "Outranked", "C3") })) == "C1:Outranked:C3", "a new winner is reported");
        ok &= Check(DescribeSkips(t.Update(new[] { Sk("C1", "NoPath") })) == "C1:NoPath:-", "a new reason is reported");
        ok &= Check(DescribeSkips(t.Update(new[] { Sk("B", "NoPath"), Sk("C1", "NoPath") })) == "B:NoPath:-",
            "only the planet that changed is reported (C1 is unchanged), in name order");
        ok &= Check(DescribeSkips(t.Update(new[] { Sk("Z", "NoPath"), Sk("A", "Outranked", "B"), Sk("B", "NoPath"), Sk("C1", "NoPath") }))
                    == "A:Outranked:B,Z:NoPath:-", "several new skips are all reported, sorted by name");
        ok &= Check(t.Update(new BlockadeSkipTracker.Skip[0]).Count == 0, "nothing skipped: nothing reported");
        ok &= Check(DescribeSkips(t.Update(new[] { Sk("C1", "NoPath") })) == "C1:NoPath:-",
            "after the state cleared, the same skip is reported again");
        t.Clear();
        ok &= Check(DescribeSkips(t.Update(new[] { Sk("C1", "NoPath") })) == "C1:NoPath:-", "Clear forgets everything");
        return ok;
    }

    private static ResearchChoiceElement Choice(CatalogItem item, float weight)
        => new ResearchChoiceElement { Item = item, Weight = weight };

    // Warship Offense items get the boost only while a blockade against me is visible; Health, Defense and everything else
    // never do; the input list is not mutated.
    public static bool RunResearchBoostCheck()
    {
        var ok = true;
        using (var s = Scenario.Line())
        {
            var off = s.Research.First(i => i.itemName == "Off 1");
            var hp = s.Research.First(i => i.itemName == "Hp 1");
            var food = ScriptableObject.CreateInstance<CatalogItem>();
            food.itemName = "Food 1"; food.type = "Planet Improvement"; food.subType = "Food";
            try
            {
                var input = new List<ResearchChoiceElement> { Choice(off, 0.5f), Choice(hp, 0.5f), Choice(food, 1f) };

                s.AI.RefreshBlockadeView(5);   // nothing blockaded yet
                ok &= Check(Near(s.AI.GetResearchSituationalMultiplier(off), 1f), "no blockade: the multiplier is 1");
                var calm = s.AI.ApplyResearchSituationalWeights(input);
                ok &= Check(Near(calm[0].Weight, 0.5f) && Near(calm[1].Weight, 0.5f) && Near(calm[2].Weight, 1f),
                    "no blockade: weights unchanged");

                s.Ships("B", 0, 1); s.Ships("C", 1, 2);
                s.AI.RefreshBlockadeView(5);   // C is blockaded against me
                ok &= Check(Near(s.AI.GetResearchSituationalMultiplier(off), 2f), "blockaded: Warship Offense is x2");
                ok &= Check(Near(s.AI.GetResearchSituationalMultiplier(hp), 1f), "Health is not boosted");
                ok &= Check(Near(s.AI.GetResearchSituationalMultiplier(food), 1f), "other subtypes are not boosted");
                ok &= Check(Near(s.AI.GetResearchSituationalMultiplier(null), 1f), "a null item is neutral");
                var boosted = s.AI.ApplyResearchSituationalWeights(input);
                ok &= Check(Near(boosted[0].Weight, 1f) && Near(boosted[1].Weight, 0.5f) && Near(boosted[2].Weight, 1f),
                    "blockaded: only Off 1 doubles (0.5 to 1.0, applied on the already-normalized weight)");
                ok &= Check(Near(input[0].Weight, 0.5f), "the input list is not mutated");

                s.Constants.blockadedOffenseResearchBoost = 1f;
                ok &= Check(Near(s.AI.GetResearchSituationalMultiplier(off), 1f), "a boost of 1 disables it");
            }
            finally { Object.DestroyImmediate(food); }
        }
        return ok;
    }
}
