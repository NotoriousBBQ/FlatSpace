using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using FlatSpace.AI;
using Flatspace.Objects.Production;

// Self-check for AI-initiated blockades (docs/superpowers/specs/2026-10-22-ai-initiated-blockades-design.md): producer value,
// hold-force sizing, the tracker, target selection, the log lines and the PlayerAI integration.
public static class BlockadeInitiateSelfCheck
{
    [MenuItem("FlatSpace/AI/Run Blockade Initiation Self-Check")]
    public static void Run() => RunChecks();

    public static bool RunChecks()
    {
        var ok = RunProducerValueCheck();
        ok &= RunHoldForceCheck();
        ok &= RunTrackerCheck();
        ok &= RunInitiatorCheck();
        ok &= RunFormatCheck();
        ok &= RunPlanIntegrationCheck();
        ok &= RunFleetTermCheck();
        Debug.Log(ok
            ? "[BlockadeInitiateSelfCheck] ALL PASSED"
            : "[BlockadeInitiateSelfCheck] FAILURES (see errors above)");
        return ok;
    }

    private static bool Check(bool condition, string label)
    {
        if (!condition) Debug.LogError($"[BlockadeInitiateSelfCheck] FAIL: {label}");
        return condition;
    }

    private static bool Near(float a, float b) => Mathf.Abs(a - b) < 0.01f;

    private const string ResourceDataFolder = "Assets/Flatspace/Objects/Planets/ScritpableObjects/ResourceData/NoPopResourceData/";

    private static PlanetResourceData LoadData(string assetName)
        => AssetDatabase.LoadAssetAtPath<PlanetResourceData>(ResourceDataFolder + assetName + ".asset");

    // Every planet needs a distinct position (PathingSystem.FindPath tie-break fragility).
    private static PlanetSpawnData Spawn(string name, float x, float y, IEnumerable<string> connections = null,
        PlanetResourceData data = null)
    {
        var resourceData = data;
        if (resourceData == null)
        {
            resourceData = ScriptableObject.CreateInstance<PlanetResourceData>();
            resourceData._initialPopulation = 0;
            resourceData._maxPopulation = 5;
            resourceData._baseFoodProduction = 1f;
        }

        var spawn = ScriptableObject.CreateInstance<PlanetSpawnData>();
        spawn._planetName = name;
        spawn._planetPosition = new Vector3(x, y, 0f);
        spawn._planetType = Planet.PlanetType.PlanetTypeNormal;
        spawn._resourceData = resourceData;
        spawn._connections = connections != null ? new List<string>(connections) : new List<string>();
        return spawn;
    }

    // A small map with the shared warship template, constants and research (the BlockadeBreakSelfCheck pattern).
    private sealed class Scenario : System.IDisposable
    {
        public GameObject MapGo;
        public GameAIMap Map;
        public GameAIConstants Constants;
        public ShipData Template;
        public List<CatalogItem> Research;
        public GameObject PlayerGo;
        public PlayerAI AI;

        // Ships with an empty snapshot have offense 10.
        private static readonly string[] None = new string[0];

        public static Scenario Create(string name, List<PlanetSpawnData> spawns)
        {
            var s = new Scenario { Template = WarshipSelfCheck.MakeTemplate() };
            s.Constants = WarshipSelfCheck.MakeConstants(s.Template);
            s.Constants.defaultTravelSpeed = 1f;
            s.Constants.maxPathNodesForShipTransport = 10;
            s.Constants.maxPathNodesForKnowledge = 6;
            s.Constants.blockadeMemoryTurns = 20;
            s.Constants.garrisonOuter = 6;
            s.Research = WarshipSelfCheck.MakeResearch();
            s.MapGo = new GameObject("BISelfCheckMap_" + name);
            s.Map = s.MapGo.AddComponent<GameAIMap>();
            s.Map.GameAIMapInit(spawns, s.Constants);
            return s;
        }

        // A(0,0) - B(100,0) - C(200,0) - D(300,0), plus an unconnected Z(900,0).
        public static Scenario Line() => Create("Line", new List<PlanetSpawnData>
        {
            Spawn("A", 0f, 0f, new[] { "B" }),
            Spawn("B", 100f, 0f, new[] { "A", "C" }),
            Spawn("C", 200f, 0f, new[] { "B", "D" }),
            Spawn("D", 300f, 0f, new[] { "C" }),
            Spawn("Z", 900f, 0f),
        });

        // The ChokepointSelfCheck hub: A - H, H - X1, X2, X3, A - Y. H is the chokepoint (percentile 1), A 0.8, the rest leaves.
        public static Scenario Hub(PlanetResourceData yData = null)
        {
            var spawns = ChokepointSelfCheck.HubSpawns();
            if (yData != null) spawns.First(x => x._planetName == "Y")._resourceData = yData;
            return Create("Hub", spawns);
        }

        public Planet P(string name) => Map.GetPlanet(name);

        public void Colonize(string name, int player)
        {
            var planet = P(name);
            planet.Owner = player;
            planet.Population.Add(new Planet.Inhabitant { Player = player });
        }

        public void Ships(string planet, int owner, int count)
        {
            for (var i = 0; i < count; i++) P(planet).DockShipFromSave(Ship.ShipKind.WarShip, owner, new List<string>(None));
        }

        public WarshipStats Stats => new WarshipStats(Research);

        // Player 0's AI on this map (a research catalog so the blockade view and warship stats work), Consolidate, diplomacy on.
        public void AttachAI()
        {
            Map.Diplomacy.Enabled = true;
            PlayerGo = new GameObject("BISelfCheckPlayer");
            var player = PlayerGo.AddComponent<Player>();
            AI = PlayerGo.AddComponent<PlayerAI>();
            AI.Player = player;
            AI.AIMap = Map;
            player.playerID = 0;
            AI.ResearchCatalog = PlayerGo.AddComponent<Catalog>();
            AI.ResearchCatalog.catalogItems = Research;
            AI.Strategy = PlayerAI.AIStrategy.AIStrategyConsolidate;
            Map.Knowledge.Update(Map, 2, 6);
        }

        public void War(int a, int b) => Map.Diplomacy.SetStance(a, b, Stance.War, 0);

        // The planner's actions for turn `turn` (the blockade view is refreshed first, as the existing plan checks do).
        public List<ShipAction> Plan(int turn)
        {
            AI.RefreshBlockadeView(turn);
            return AI.PlanShipActions(turn);
        }

        public AssaultPlanner Planner() => new AssaultPlanner(Map, 0, stats: Stats);

        public void Dispose()
        {
            WarshipSelfCheck.DestroyAll(Research);
            Object.DestroyImmediate(MapGo);
            if (PlayerGo != null) Object.DestroyImmediate(PlayerGo);
            Object.DestroyImmediate(Constants);
            Object.DestroyImmediate(Template);
        }
    }

    // RoundGarrison 0: every docked ship is spare, so the checks aim at the sizing arithmetic alone.
    private static List<ShipTransportPlanner.PlanetState> States(Scenario s, params string[] planets)
        => planets.Select(n => new ShipTransportPlanner.PlanetState { Planet = s.P(n), Docked = s.P(n).DockedShips.Count, RoundGarrison = 0 }).ToList();

    public static bool RunHoldForceCheck()
    {
        var ok = true;

        // Line: A and B hold my ships (B is nearer to C), three rival warships at C (value 30 against me).
        using (var s = Scenario.Line())
        {
            s.Ships("A", 0, 3); s.Ships("B", 0, 2); s.Ships("C", 1, 3);
            var planner = s.Planner();
            var margin = 1f + s.Constants.blockadeBreakMargin;
            ok &= Check(Near(planner.DockedOffenseOf(s.P("C"), 1), 30f) && Near(planner.DockedOffenseOf(s.P("C"), 0), 0f),
                "DockedOffenseOf: the rival's three ships are 30 offense at C, mine 0");
            ok &= Check(Near(planner.HoldNeededOffense(s.P("C"), 1), 30f * margin),
                "HoldNeededOffense: the rival's 30 docked offense x (1 + margin), none of mine there");

            var actions = planner.PlanHoldForce(s.P("C"), 20f, States(s, "A", "B"), new Dictionary<string, int>());
            ok &= Check(actions.Sum(a => a.Count) == 2 && actions.All(a => a.Origin == "B" && a.Target == "C"),
                "needed 20: two ships, from the nearer B, are enough");
            ok &= Check(planner.LastBlockadeForce.Ships == 2 && Near(planner.LastBlockadeForce.Offense, 20f)
                        && Near(planner.LastBlockadeForce.StillNeeded, 0f), "LastBlockadeForce records two ships, offense 20, nothing still needed");

            ok &= Check(planner.PlanHoldForce(s.P("C"), 0f, States(s, "A", "B"), new Dictionary<string, int>()).Count == 0
                        && planner.PlanHoldForce(s.P("C"), -5f, States(s, "A", "B"), new Dictionary<string, int>()).Count == 0,
                "nothing needed: no ships are sent");

            var sent = new Dictionary<string, int> { { "B", 2 } };   // earlier plans already take B's two ships
            actions = planner.PlanHoldForce(s.P("C"), 20f, States(s, "A", "B"), sent);
            ok &= Check(actions.Count == 1 && actions[0].Origin == "A" && actions[0].Count == 2,
                "ships already sent from B are not spare: the force comes from A");

            s.Ships("C", 0, 2);   // two of mine now dock at C
            ok &= Check(Near(planner.HoldNeededOffense(s.P("C"), 1), 30f * margin - 20f),
                "my docked offense at the target lowers the need");
        }

        using (var s = Scenario.Line())
        {
            s.Ships("A", 0, 1);
            var planner = s.Planner();
            var actions = planner.PlanHoldForce(s.P("C"), 20f, States(s, "A"), new Dictionary<string, int>());
            ok &= Check(actions.Sum(a => a.Count) == 1 && Near(planner.LastBlockadeForce.StillNeeded, 10f),
                "spare for one ship only: it is sent and 10 offense is still needed");

            ok &= Check(Near(planner.HoldNeededOffense(s.P("Z"), 1), 0.001f),
                "an empty planet with none of mine: a token need, so exactly one ship is sent");
            s.Ships("Z", 0, 1);
            ok &= Check(Near(planner.HoldNeededOffense(s.P("Z"), 1), 0f), "one of my ships already docked there: nothing more is needed");
        }
        return ok;
    }

    public static bool RunTrackerCheck()
    {
        var ok = true;
        var t = new InitiatedBlockadeTracker();
        InitiatedBlockadeTracker.Held H(string planet, BlockadeKind kind = BlockadeKind.Dc)
            => new InitiatedBlockadeTracker.Held { Planet = planet, Kind = kind, Rival = 1, Stance = "War", Score = 1f, Needed = 5f, Ships = 2 };

        var changes = t.Update(10, new List<InitiatedBlockadeTracker.Held> { H("A") }, h => "x");
        ok &= Check(changes.Count == 1 && changes[0].Started && changes[0].Planet == "A" && changes[0].Kind == BlockadeKind.Dc
                    && changes[0].Held.Needed == 5f, "first sight: one Start carrying the hold");
        ok &= Check(t.IsActive("A") && t.KindOf("A") == BlockadeKind.Dc && t.KindOf("B") == null, "the planet is active with its kind");
        ok &= Check(t.Update(11, new List<InitiatedBlockadeTracker.Held> { H("A") }, h => "x").Count == 0, "unchanged: no transitions");

        changes = t.Update(15, new List<InitiatedBlockadeTracker.Held> { H("B", BlockadeKind.Lane) }, h => h.Planet == "A" ? "LostDc" : null);
        ok &= Check(changes.Count == 2 && !changes[0].Started && changes[0].Planet == "A" && changes[0].EndReason == "LostDc"
                    && changes[0].TurnsHeld == 5 && changes[1].Started && changes[1].Planet == "B",
            "a dropped hold ends with the callback's reason and turns held, before the new Start");
        changes = t.Update(16, new List<InitiatedBlockadeTracker.Held>(), h => null);
        ok &= Check(changes.Count == 1 && changes[0].EndReason == "Outmatched" && changes[0].TurnsHeld == 1,
            "a null end reason is read as Outmatched");

        // Skips are reported on change only.
        var sk = new InitiatedBlockadeTracker.Skip { Planet = "C", Kind = BlockadeKind.Lane, Reason = "NoSpare" };
        ok &= Check(t.UpdateSkips(new List<InitiatedBlockadeTracker.Skip> { sk }).Count == 1, "a new skip is reported");
        ok &= Check(t.UpdateSkips(new List<InitiatedBlockadeTracker.Skip> { sk }).Count == 0, "the same skip is not reported twice");
        sk.Reason = "NoPath";
        ok &= Check(t.UpdateSkips(new List<InitiatedBlockadeTracker.Skip> { sk }).Count == 1, "a changed reason is reported again");
        t.UpdateSkips(new List<InitiatedBlockadeTracker.Skip>());
        ok &= Check(t.UpdateSkips(new List<InitiatedBlockadeTracker.Skip> { sk }).Count == 1, "a skip that went away is reported again if it returns");

        // Lane hysteresis (Review Focus 3): end count 2, end turns 3.
        var lanes = new InitiatedBlockadeTracker();
        ok &= Check(lanes.LaneStillHeld("H", 1, 2, 3) && lanes.LaneStillHeld("H", 1, 2, 3), "two cool turns: still held");
        ok &= Check(!lanes.LaneStillHeld("H", 1, 2, 3), "the third cool turn in a row releases it");
        var flap = new InitiatedBlockadeTracker();
        var held = true;
        for (var i = 0; i < 12; i++) held &= flap.LaneStillHeld("H", i % 2 == 0 ? 3 : 1, 2, 3);
        ok &= Check(held, "a count oscillating between 3 and 1 never releases the lane (no flapping)");
        var reset = new InitiatedBlockadeTracker();
        reset.LaneStillHeld("H", 1, 2, 3); reset.LaneStillHeld("H", 1, 2, 3); reset.LaneStillHeld("H", 2, 2, 3);
        ok &= Check(reset.LaneStillHeld("H", 1, 2, 3) && reset.LaneStillHeld("H", 1, 2, 3), "a count back at the end value resets the cooling");

        // Give-up counters.
        var g = new InitiatedBlockadeTracker();
        var n = 0;
        for (var i = 0; i < 10; i++) n = g.BumpGiveUp("H", "Outmatched");
        ok &= Check(n == 10, "ten consecutive turns of one cause count to 10");
        ok &= Check(g.BumpGiveUp("H", "NoSpare") == 1, "a different cause restarts at 1");
        g.ClearGiveUp("H");
        ok &= Check(g.BumpGiveUp("H", "NoSpare") == 1, "ClearGiveUp resets the count");

        // Give-up cooldown: closed before the until-turn, open from it; independent of the hold's state.
        ok &= Check(!g.InCooldown("H", 5), "no cooldown before one is started");
        g.StartCooldown("H", 25);
        ok &= Check(g.InCooldown("H", 5) && g.InCooldown("H", 24) && !g.InCooldown("H", 25), "a cooldown until turn 25 covers turns before 25");
        ok &= Check(!g.InCooldown("Other", 5), "a cooldown is per planet");

        // The fleet term survives updates.
        t.RecordWantedShips(5);
        t.Update(20, new List<InitiatedBlockadeTracker.Held> { H("A") }, h => null);
        ok &= Check(t.WantedShipsLastTurn == 5, "the recorded wanted ships are read back and survive Update");
        t.RecordWantedShips(-3);
        ok &= Check(t.WantedShipsLastTurn == 0, "a negative record is clamped to 0");
        return ok;
    }

    public static bool RunInitiatorCheck()
    {
        var ok = true;

        // Valid rivals (Review Focus 4): war, hostility at peace, the 101 switch, diplomacy off.
        using (var s = Scenario.Line())
        {
            var d = new DiplomacyState { Enabled = true };
            d.SetStance(0, 1, Stance.War, 0);
            var pair = d.Get(0, 2); pair.Hostility = 59f; d.Set(0, 2, pair);
            var contact = new[] { 1, 2 };
            var others = new[] { 1, 2 };
            s.Constants.blockadeInitiateHostility = 60f;
            var valid = BlockadeInitiator.ValidRivals(0, d, contact, others, s.Constants);
            ok &= Check(valid.Contains(1) && !valid.Contains(2), "at war with 1 yes; at peace with 2 at hostility 59 (value 60) no");
            pair = d.Get(0, 2); pair.Hostility = 60f; d.Set(0, 2, pair);
            valid = BlockadeInitiator.ValidRivals(0, d, contact, others, s.Constants);
            ok &= Check(valid.Contains(1) && valid.Contains(2), "hostility 60 at peace reaches the value: valid");
            s.Constants.blockadeInitiateHostility = 101f;
            pair = d.Get(0, 2); pair.Hostility = 100f; d.Set(0, 2, pair);
            valid = BlockadeInitiator.ValidRivals(0, d, contact, others, s.Constants);
            ok &= Check(valid.Contains(1) && !valid.Contains(2), "a value above 100 means no peace-time blockades");
            ok &= Check(BlockadeInitiator.ValidRivals(0, new DiplomacyState { Enabled = false }, new int[0], others, s.Constants).SetEquals(others),
                "diplomacy off: every other player is a valid rival");
        }

        // Lane tally from stored paths: A -> H -> X1 and A -> H -> X2 for rival 1.
        using (var s = Scenario.Hub())
        {
            GameAI.GameAIOrder Order(GameAI.GameAIOrder.OrderType type, string o, string t, int player)
                => new GameAI.GameAIOrder { Type = type, Origin = o, Target = t, PlayerId = player };
            var orders = new List<GameAI.GameAIOrder>
            {
                Order(GameAI.GameAIOrder.OrderType.OrderTypeGrotsitsTransport, "A", "X1", 1),
                Order(GameAI.GameAIOrder.OrderType.OrderTypeFoodTransport, "A", "X2", 1),
                Order(GameAI.GameAIOrder.OrderType.OrderTypeFoodTransport, "A", "X3", 0),        // mine: ignored
                Order(GameAI.GameAIOrder.OrderType.OrderTypeFoodTransport, "A", "Y", 2),         // not a valid rival: ignored
                Order(GameAI.GameAIOrder.OrderType.OrderTypeShipTransport, "A", "X1", 1),        // not a resource order: ignored
            };
            var counts = BlockadeInitiator.LaneCounts(s.Map, orders, new HashSet<int> { 1 });
            int Get(string p) => counts.TryGetValue((p, 1), out var c) ? c : 0;
            ok &= Check(Get("A") == 2 && Get("H") == 2 && Get("X1") == 1 && Get("X2") == 1 && Get("X3") == 0 && Get("Y") == 0,
                "lane counts: A and H on both routes, X1 and X2 once, nothing for mine, a non-rival or a ship order");
        }

        // Selection: kinds, order, caps, sticky holds.
        using (var s = Scenario.Hub(LoadData("VerdantPlanetTypeDataNoPop")))
        {
            s.Colonize("A", 0);
            s.Map.Knowledge.Update(s.Map, 2, 6);
            ok &= Check(new[] { "H", "X1", "Y" }.All(n => s.Map.Knowledge.IsKnown(0, n)), "precondition: player 0 knows H, X1 and Y");
            s.Constants.blockadeInitiateMaxActive = 2;
            s.Constants.blockadeColonyMinValue = 1f;
            s.Constants.blockadeLaneStartCount = 4;
            var tracker = new InitiatedBlockadeTracker();
            BlockadeInitiator.Inputs Inputs(Dictionary<string, int> dc, Dictionary<(string, int), int> lanes, bool colonyAllowed = true)
                => new BlockadeInitiator.Inputs
                {
                    Rivals = new HashSet<int> { 1 }, DcOwner = dc, Lanes = lanes, Tracker = tracker,
                    ColonyStartAllowed = p => colonyAllowed,
                };
            var noDc = new Dictionary<string, int>();
            var noLanes = new Dictionary<(string, int), int>();

            var sel = BlockadeInitiator.Select(s.Map, 0, Inputs(new Dictionary<string, int> { { "X1", 1 } },
                new Dictionary<(string, int), int> { { ("H", 1), 5 }, { ("Y", 1), 3 } }));
            ok &= Check(sel.Active.Count == 2 && sel.Active[0].Planet == "X1" && sel.Active[0].Kind == BlockadeKind.Dc
                        && sel.Active[1].Planet == "H" && sel.Active[1].Kind == BlockadeKind.Lane,
                "a Dc comes before a Lane; Y's lane count 3 is below the start 4 and H is a lane (not a Colony) target");
            ok &= Check(sel.Skipped.Any(x => x.Planet == "Y" && x.Kind == BlockadeKind.Colony && x.Reason == "OverCap"),
                "the Verdant planet Y is a Colony candidate (value 1) that the cap of 2 passes over: OverCap");

            s.Constants.blockadeInitiateMaxActive = 0;
            ok &= Check(BlockadeInitiator.Select(s.Map, 0, Inputs(new Dictionary<string, int> { { "X1", 1 } }, noLanes)).Active.Count == 0,
                "blockadeInitiateMaxActive 0: nothing is selected");
            s.Constants.blockadeInitiateMaxActive = 2;

            sel = BlockadeInitiator.Select(s.Map, 0, Inputs(noDc, noLanes));
            ok &= Check(sel.Active.Select(c => c.Planet).OrderBy(n => n).SequenceEqual(new[] { "H", "Y" })
                        && sel.Active.All(c => c.Kind == BlockadeKind.Colony),
                "colony targets: the chokepoint H (value 1) and the Verdant planet Y (producer value 1) reach the minimum 1; leaves X1-X3 (0) do not");
            s.Constants.blockadeColonyProducerWeight = 0.5f;
            ok &= Check(BlockadeInitiator.Select(s.Map, 0, Inputs(noDc, noLanes)).Active.Select(c => c.Planet).SequenceEqual(new[] { "H" }),
                "a producer weight of 0.5 puts Y at 0.5, under the minimum: only H remains");
            s.Constants.blockadeColonyProducerWeight = 1f;
            ok &= Check(BlockadeInitiator.Select(s.Map, 0, Inputs(noDc, noLanes, colonyAllowed: false)).Active.Count == 0,
                "a colony ship that can already reach the planet: no Colony target is started");

            // An already-active Colony hold stays while the planet is unpopulated, even if a colony ship can now reach it.
            tracker.Update(30, new List<InitiatedBlockadeTracker.Held>
            {
                new InitiatedBlockadeTracker.Held { Planet = "Y", Kind = BlockadeKind.Colony, Rival = 1, Stance = "War" },
            }, h => null);
            sel = BlockadeInitiator.Select(s.Map, 0, Inputs(new Dictionary<string, int> { { "X1", 1 } },
                new Dictionary<(string, int), int> { { ("H", 1), 5 } }, colonyAllowed: false));
            ok &= Check(sel.Active.Count == 2 && sel.Active[0].Planet == "Y" && sel.Active[0].Kind == BlockadeKind.Colony
                        && sel.Active[1].Planet == "X1" && sel.Skipped.Any(x => x.Planet == "H" && x.Reason == "OverCap"),
                "the held Colony target Y comes first and stays; the best new target X1 fills the second slot; the Lane H is OverCap");
            ok &= Check(sel.Ranked.Count >= 3 && sel.Ranked[0].Planet == "Y" && sel.Ranked.Any(c => c.Planet == "H"),
                "Ranked lists every candidate in rank order, the held one first, including the one past the cap");
            tracker.StartCooldown("X1", 100);
            sel = BlockadeInitiator.Select(s.Map, 0, Inputs(new Dictionary<string, int> { { "X1", 1 } },
                new Dictionary<(string, int), int> { { ("H", 1), 5 } }, colonyAllowed: false));
            ok &= Check(!sel.Ranked.Any(c => c.Planet == "X1"), "a planet in its give-up cooldown is not a candidate");
            tracker.StartCooldown("X1", 0);
            var notColonizable = Inputs(noDc, noLanes, colonyAllowed: false);
            notColonizable.ColonyHoldValid = p => false;
            ok &= Check(!BlockadeInitiator.Select(s.Map, 0, notColonizable).Ranked.Any(c => c.Planet == "Y"),
                "a held Colony target that is no longer colonizable from my planets is dropped (the hold ends)");
            s.Colonize("Y", 1);
            sel = BlockadeInitiator.Select(s.Map, 0, Inputs(noDc, noLanes, colonyAllowed: false));
            ok &= Check(!sel.Active.Any(c => c.Planet == "Y"), "once Y is populated it is no longer a Colony target");

            // Without a valid rival nothing is held.
            var noRival = Inputs(noDc, noLanes);
            noRival.Rivals = new HashSet<int>();
            ok &= Check(BlockadeInitiator.Select(s.Map, 0, noRival).Active.Count == 0, "no valid rival: no Colony target either");
        }

        // A Dc the AI does not know is not a target.
        using (var s = Scenario.Line())
        {
            s.Colonize("A", 0);
            s.Map.Knowledge.Update(s.Map, 2, 6);
            ok &= Check(!s.Map.Knowledge.IsKnown(0, "Z"), "precondition: the unconnected Z is not known");
            s.Constants.blockadeInitiateMaxActive = 2;
            var sel = BlockadeInitiator.Select(s.Map, 0, new BlockadeInitiator.Inputs
            {
                Rivals = new HashSet<int> { 1 }, DcOwner = new Dictionary<string, int> { { "Z", 1 } },
                Lanes = new Dictionary<(string, int), int>(), Tracker = new InitiatedBlockadeTracker(),
            });
            ok &= Check(sel.Active.Count == 0, "an unknown planet is never a Dc target");
        }

        // Lane hysteresis in selection: an active lane stays at the end count and is released after the cooling turns.
        using (var s = Scenario.Hub())
        {
            s.Colonize("A", 0);
            s.Map.Knowledge.Update(s.Map, 2, 6);
            s.Constants.blockadeInitiateMaxActive = 2;
            s.Constants.blockadeLaneStartCount = 4; s.Constants.blockadeLaneEndCount = 2; s.Constants.blockadeLaneEndTurns = 2;
            s.Constants.blockadeColonyMinValue = 99f;   // no colony targets in this case
            var tracker = new InitiatedBlockadeTracker();
            Dictionary<(string, int), int> Lane(int count) => new Dictionary<(string, int), int> { { ("H", 1), count } };
            BlockadeInitiator.Inputs In(int count) => new BlockadeInitiator.Inputs
            {
                Rivals = new HashSet<int> { 1 }, DcOwner = new Dictionary<string, int>(), Lanes = Lane(count), Tracker = tracker,
            };
            ok &= Check(BlockadeInitiator.Select(s.Map, 0, In(3)).Active.Count == 0, "a count of 3 does not start a lane (start 4)");
            var sel = BlockadeInitiator.Select(s.Map, 0, In(4));
            ok &= Check(sel.Active.Count == 1 && sel.Active[0].Kind == BlockadeKind.Lane, "a count of 4 starts it");
            tracker.Update(1, sel.Active.Select(c => new InitiatedBlockadeTracker.Held { Planet = c.Planet, Kind = c.Kind, Rival = c.Rival }).ToList(), h => null);
            ok &= Check(BlockadeInitiator.Select(s.Map, 0, In(2)).Active.Count == 1, "an active lane stays at the end count 2");
            ok &= Check(BlockadeInitiator.Select(s.Map, 0, In(1)).Active.Count == 1, "one cool turn: still held");
            ok &= Check(BlockadeInitiator.Select(s.Map, 0, In(1)).Active.Count == 0, "the second cool turn in a row releases it (end turns 2)");

            // The rival stops being valid (peace, hostility under the value): the held lane is dropped at once, not after the cooling turns.
            var laneTracker = new InitiatedBlockadeTracker();
            laneTracker.Update(1, new List<InitiatedBlockadeTracker.Held>
            {
                new InitiatedBlockadeTracker.Held { Planet = "H", Kind = BlockadeKind.Lane, Rival = 1 },
            }, h => null);
            var noRivalLane = new BlockadeInitiator.Inputs
            {
                Rivals = new HashSet<int> { 2 }, DcOwner = new Dictionary<string, int>(), Lanes = Lane(5), Tracker = laneTracker,
            };
            ok &= Check(BlockadeInitiator.Select(s.Map, 0, noRivalLane).Active.Count == 0,
                "a held lane whose rival is no longer a valid rival is dropped at once (WarEnded), even with its count high");
        }
        return ok;
    }

    // Diagnostic for the plan-level cases: what the last plan did (printed so a failing case shows its state).
    private static void Dump(string label, Scenario s, List<ShipAction> actions)
        => Debug.Log("[BlockadeInitiateSelfCheck] " + label + ": actions [" + string.Join("; ", actions.Select(a => a.Kind + " " + a.Origin + "->" + a.Target + " x" + a.Count))
                     + "] skips [" + string.Join("; ", s.AI.LastInitiatedSkips.Select(k => k.Planet + " " + k.Kind + " " + k.Reason))
                     + "] changes [" + string.Join("; ", s.AI.LastInitiatedChanges.Select(c => (c.Started ? "start " : "end " + c.EndReason + " ") + c.Planet))
                     + "] active [" + string.Join(", ", s.AI.InitiatedBlockades.ActivePlanets) + "]");

    private static bool Same(ShipAction a, ShipAction b)
        => a.Origin == b.Origin && a.Target == b.Target && a.Count == b.Count && a.Kind == b.Kind;

    // Line: A(0) - B(100) - C(200) - D(300), plus Z. Player 0 holds A and B (B is outer: garrison 6) and docks 30 ships at B, 24 of them
    // spare; rival 1 holds D with two warships (the assault wants 3 of the spare). The rival's Distribution Center is C (a seam).
    private static Scenario DcLine(bool dcOn)
    {
        var s = Scenario.Line();
        s.Colonize("A", 0); s.Colonize("B", 0); s.Colonize("D", 1);
        s.Ships("B", 0, 30); s.Ships("D", 1, 2);
        s.AttachAI();
        s.War(0, 1);
        s.Constants.blockadeInitiateMaxActive = dcOn ? 2 : 0;
        s.AI.RivalDistributionCenters = r => r == 1 ? new[] { "C" } : new string[0];
        return s;
    }

    public static bool RunPlanIntegrationCheck()
    {
        var ok = true;

        // Review Focus 2: with the feature on, every action the plan made before is still there, nothing leaves an origin beyond its spare,
        // and the extra ships go at the initiated target.
        List<ShipAction> baseline;
        using (var off = DcLine(false))
            baseline = off.Plan(5);
        using (var s = DcLine(true))
        {
            ok &= Check(s.Map.Knowledge.IsKnown(0, "C") && s.Map.Knowledge.IsKnown(0, "D"), "precondition: player 0 knows C and D");
            var actions = s.Plan(5);
            ok &= Check(baseline.All(b => actions.Any(a => Same(a, b))), "every action of the feature-off plan is unchanged in the feature-on plan");
            var extra = actions.Where(a => !baseline.Any(b => Same(a, b))).ToList();
            ok &= Check(extra.Count >= 1 && extra.All(a => a.Target == "C" && a.Origin == "B" && a.Kind == Ship.ShipKind.WarShip),
                "the extra actions are warships from B at the rival's Distribution Center C");
            ok &= Check(actions.Where(a => a.Origin == "B" && a.Kind == Ship.ShipKind.WarShip).Sum(a => a.Count) <= 30 - 6,
                "no origin sends more than its spare (B keeps its garrison of 6)");
            ok &= Check(s.AI.InitiatedBlockades.IsActive("C") && s.AI.LastInitiatedChanges.Any(c => c.Started && c.Planet == "C" && c.Kind == BlockadeKind.Dc),
                "C is an active Dc hold and a Start was reported");

            // Next turn: still held while it stays a rival DC.
            s.Plan(6);
            ok &= Check(s.AI.InitiatedBlockades.IsActive("C") && s.AI.LastInitiatedChanges.Count == 0, "turn 6: the hold persists with no transition");

            // LostDc.
            s.AI.RivalDistributionCenters = r => new string[0];
            s.Plan(7);
            ok &= Check(!s.AI.InitiatedBlockades.IsActive("C")
                        && s.AI.LastInitiatedChanges.Any(c => !c.Started && c.Planet == "C" && c.EndReason == "LostDc" && c.TurnsHeld == 2),
                "the planet stops being a rival DC: released as LostDc after 2 turns");

            // WarEnded: peace with hostility below the value.
            s.AI.RivalDistributionCenters = r => r == 1 ? new[] { "C" } : new string[0];
            s.Plan(8);
            ok &= Check(s.AI.InitiatedBlockades.IsActive("C"), "the DC is a target again");
            var pair = s.Map.Diplomacy.Get(0, 1); pair.Stance = Stance.Peace; pair.Hostility = 0f; s.Map.Diplomacy.Set(0, 1, pair);
            s.Plan(9);
            ok &= Check(!s.AI.InitiatedBlockades.IsActive("C") && s.AI.LastInitiatedChanges.Any(c => !c.Started && c.EndReason == "WarEnded"),
                "the war ends and hostility is under the value: released as WarEnded");
            pair.Hostility = s.Constants.blockadeInitiateHostility; s.Map.Diplomacy.Set(0, 1, pair);
            s.Plan(10);
            ok &= Check(s.AI.InitiatedBlockades.IsActive("C") && s.AI.LastInitiatedChanges.Any(c => c.Started && c.Held.Stance == "Peace"),
                "hostility at the value makes the rival at peace a valid target again: a Peace-stance Start");
        }

        // Expand never initiates (Review Focus 4).
        using (var s = DcLine(true))
        {
            s.AI.Strategy = PlayerAI.AIStrategy.AIStrategyExpand;
            s.Plan(5);
            ok &= Check(s.AI.InitiatedBlockades.ActivePlanets.Count == 0, "Expand never initiates a blockade");
        }

        // NoSpare: B holds only its garrison, so there is nothing to send; a target that cannot be sent a ship does not start (and so
        // does not take a slot), and the skip is reported.
        using (var s = DcLine(true))
        {
            foreach (var ship in s.P("B").DockedShips.ToList()) s.P("B").DockedShips.Remove(ship);
            s.Ships("B", 0, 6);
            var actions = s.Plan(5);
            ok &= Check(actions.All(a => a.Target != "C"), "no spare ship: nothing is sent at C");
            ok &= Check(s.AI.LastInitiatedSkips.Any(k => k.Planet == "C" && k.Reason == "NoSpare") && !s.AI.InitiatedBlockades.IsActive("C"),
                "the skip is NoSpare and the hold does not start");
        }

        // The arrival gate sees the warships being sent (not siegeships): rival ships at H, nothing else of mine heading there.
        using (var s = Scenario.Hub())
        {
            s.Colonize("A", 0); s.Colonize("X1", 1);
            s.Ships("A", 0, 40); s.Ships("H", 1, 3);
            s.AttachAI();
            s.War(0, 1);
            s.Map.Knowledge.Update(s.Map, 2, 6);
            s.Constants.followerMaxLossFraction = 0f;   // 0 = never into a projected fight
            ok &= Check(!s.AI.ArrivalNotOutmatched("A", s.P("H"), 5, null, Ship.ShipKind.WarShip),
                "warships into three rival warships are projected and refused at fraction 0 (A has no siegeships, so the old siegeship projection saw nothing)");
            ok &= Check(s.AI.ArrivalNotOutmatched("A", s.P("H"), 5),
                "the default (siegeship) projection of the same send sees no group and allows it: the kind is what matters");
        }

        // Ships docked at another hold are that hold's force, not spare: X2 does not take X1's ships.
        using (var s = Scenario.Hub())
        {
            s.Colonize("A", 0); s.Colonize("Y", 1);
            s.Ships("X1", 0, 4); s.Ships("X2", 1, 6);
            s.AttachAI();
            s.War(0, 1);
            s.Map.Knowledge.Update(s.Map, 2, 6);
            s.Constants.blockadeInitiateMaxActive = 2;
            s.Constants.blockadeColonyMinValue = 99f;
            s.AI.RivalDistributionCenters = r => r == 1 ? new[] { "X1", "X2" } : new string[0];
            var actions = s.Plan(5);
            Dump("hold-to-hold case", s, actions);
            ok &= Check(s.AI.InitiatedBlockades.IsActive("X1"), "precondition: X1 (nothing of the rival's there, 4 of my ships docked) is a Dc hold");
            ok &= Check(!actions.Any(a => a.Origin == "X1" && a.Target == "X2"), "the ships docked at the hold X1 are not sent to the hold X2");
            ok &= Check(!s.AI.InitiatedBlockades.IsActive("X2") && s.AI.LastInitiatedSkips.Any(k => k.Planet == "X2" && k.Reason == "NoSpare"),
                "X2 has no other spare ship to send: it does not start (NoSpare)");
        }

        // A hold whose need the force cannot cover is released, and cannot restart at once. The hold starts while C is empty; then the rival
        // docks 100 ships there (and one of mine is there), a need no spare force covers. Whether the ordinary assault eats the spare, the
        // arrival gate refuses the group, or the pass sends ships that fall short, every turn is short: after the give-up turns the hold is
        // released as Outmatched, and the planet is in its cooldown.
        using (var s = DcLine(true))
        {
            s.Constants.blockadeInitiateGiveUpTurns = 3;
            s.Constants.blockadeInitiateCooldownTurns = 20;
            s.Plan(5);
            ok &= Check(s.AI.InitiatedBlockades.IsActive("C"), "precondition: the Dc hold on C starts while C is empty");
            s.Ships("C", 1, 100); s.Ships("C", 0, 1);
            Dump("uncovered case turn 6", s, s.Plan(6));
            ok &= Check(s.AI.InitiatedBlockades.IsActive("C"), "turn 1 short of the need: still held");
            s.Plan(7);
            ok &= Check(s.AI.InitiatedBlockades.IsActive("C"), "turn 2 short of the need: still held");
            s.Plan(8);
            ok &= Check(!s.AI.InitiatedBlockades.IsActive("C")
                        && s.AI.LastInitiatedChanges.Any(c => !c.Started && c.Planet == "C" && c.EndReason == "Outmatched"),
                "a hold that stays short of its need for the give-up turns is released as Outmatched");
            s.Plan(9);
            ok &= Check(!s.AI.InitiatedBlockades.IsActive("C") && s.AI.InitiatedBlockades.InCooldown("C", 9),
                "the planet that was given up is in its cooldown: it does not restart the next turn");
        }

        // Another player's DC list is read as of the start of the turn, whatever the processing order.
        using (var s = Scenario.Line())
        {
            s.Colonize("A", 0);
            s.AttachAI();
            s.AI.SetDistributionCenters("Food", new List<string> { "A" });
            s.P("A").Owner = 1;                                   // A is lost: this turn's update prunes it
            s.AI.UpdateDistributionCenters(new List<Planet.UpdateResult>(), 7);
            ok &= Check(!s.AI.FoodDistributionCenters.Contains("A"), "precondition: A was pruned from the Food DC list by the turn 7 update");
            ok &= Check(s.AI.DistributionCentersAtTurnStart(7).Contains("A"), "read during turn 7: the list as it stood before the update");
            ok &= Check(!s.AI.DistributionCentersAtTurnStart(8).Contains("A"), "read on turn 8: the updated list");
        }

        // Colonized: a Colony hold ends once the planet is populated.
        using (var s = Scenario.Hub())
        {
            s.Colonize("A", 0); s.Colonize("X1", 1);
            s.Ships("A", 0, 30);
            s.AttachAI();
            s.War(0, 1);
            s.Map.Knowledge.Update(s.Map, 2, 6);
            s.Constants.blockadeColonyMinValue = 1f;
            s.Constants.blockadeInitiateMaxActive = 1;
            s.AI.RivalDistributionCenters = r => new string[0];
            var actions = s.Plan(5);
            ok &= Check(s.AI.InitiatedBlockades.IsActive("H") && s.AI.InitiatedBlockades.KindOf("H") == BlockadeKind.Colony
                        && actions.Any(a => a.Target == "H" && a.Origin == "A"),
                "an unpopulated chokepoint H no colony ship can reach: a Colony hold with ships from A");
            s.Colonize("H", 1);
            s.Plan(6);
            ok &= Check(!s.AI.InitiatedBlockades.IsActive("H")
                        && s.AI.LastInitiatedChanges.Any(c => !c.Started && c.Planet == "H" && c.EndReason == "Colonized"),
                "H is populated: released as Colonized");
        }
        return ok;
    }

    public static bool RunFleetTermCheck()
    {
        var ok = true;
        using (var s = Scenario.Hub())
        {
            s.Colonize("A", 0); s.Colonize("X1", 1);
            s.Ships("A", 0, 40); s.Ships("X1", 1, 6);
            s.AttachAI();
            s.War(0, 1);
            s.Map.Knowledge.Update(s.Map, 2, 6);
            s.Constants.blockadeInitiateMaxActive = 2;
            s.Constants.blockadeInitiateMaxShips = 4;
            s.Constants.blockadeColonyMinValue = 99f;       // Dc targets only
            s.AI.RivalDistributionCenters = r => r == 1 ? new[] { "X1", "X2" } : new string[0];

            var before = s.AI.WantedWarships();
            ok &= Check(s.AI.InitiatedBlockades.WantedShipsLastTurn == 0, "no initiate pass yet: the term is 0");
            s.Plan(5);
            ok &= Check(s.AI.InitiatedBlockades.ActivePlanets.Count == 2, "two Dc holds are active (X1 and X2)");
            // Each hold wants between 1 and 4 ships. (X1 is the rival's own planet, so the ordinary assault also sends ships at it
            // and those count against its need: the term is not forced to the cap of 4.)
            var term = s.AI.InitiatedBlockades.WantedShipsLastTurn;
            ok &= Check(term >= 2 && term <= 5, "the term is 1 to 4 ships per hold: between 2 and 5 for two holds");
            ok &= Check(s.AI.WantedWarships() == before + term, "WantedWarships rises by exactly the term, after the economy ceiling");
            ok &= Check(s.AI.InitiatedBlockades.WantedShipsLastTurn <= s.Constants.blockadeInitiateMaxActive * s.Constants.blockadeInitiateMaxShips,
                "the term never exceeds max active x max ships");

            s.Constants.blockadeInitiateMaxShips = 1;
            s.Plan(5);
            ok &= Check(s.AI.InitiatedBlockades.WantedShipsLastTurn == 2, "with at most 1 ship per hold the term is exactly 2 for two holds");
            s.Constants.blockadeInitiateMaxShips = 4;

            s.Constants.blockadeInitiateMaxActive = 0;
            s.Plan(6);
            ok &= Check(s.AI.InitiatedBlockades.WantedShipsLastTurn == 0 && s.AI.WantedWarships() == before,
                "with the feature off the term returns to 0 and WantedWarships to its old value");
        }
        return ok;
    }

    public static bool RunFormatCheck()
    {
        var ok = true;
        ok &= Check(AITuningLogger.FormatBlockadeInitiateStart(40, 1, "Ocean 2", "Dc", 0, "War", 1.5f, 33f, 3)
                    == "T40|P1|BlockadeInitiateStart|Ocean 2|Dc|0|War|1.5|33|3", "BlockadeInitiateStart fields");
        ok &= Check(AITuningLogger.FormatBlockadeInitiateEnd(52, 1, "Ocean 2", "Dc", "LostDc", 12)
                    == "T52|P1|BlockadeInitiateEnd|Ocean 2|Dc|LostDc|12", "BlockadeInitiateEnd fields");
        ok &= Check(AITuningLogger.FormatBlockadeInitiateSkipped(40, 1, "Farm 1", "Lane", "NoSpare")
                    == "T40|P1|BlockadeInitiateSkipped|Farm 1|Lane|NoSpare", "BlockadeInitiateSkipped fields");
        return ok;
    }

    public static bool RunProducerValueCheck()
    {
        var ok = true;
        var c = ScriptableObject.CreateInstance<GameAIConstants>();
        try
        {
            ok &= Check(c.blockadeInitiateMaxActive == 2 && Near(c.blockadeInitiateHostility, 60f)
                        && c.blockadeLaneStartCount == 4 && c.blockadeLaneEndCount == 2 && c.blockadeLaneEndTurns == 5
                        && Near(c.blockadeColonyMinValue, 1f) && Near(c.blockadeColonyChokepointWeight, 1f)
                        && Near(c.blockadeColonyProducerWeight, 1f) && c.blockadeInitiateGiveUpTurns == 10
                        && c.blockadeInitiateMaxShips == 4 && c.blockadeInitiateCooldownTurns == 20,
                "the initiated blockade tunables default to 2, 60, 4, 2, 5, 1, 1, 1, 10, 4 and 20");
        }
        finally { Object.DestroyImmediate(c); }

        ok &= Check(Near(LoadData("VerdantPlanetTypeDataNoPop")._producerValue, 1f), "Verdant producer value 1");
        ok &= Check(Near(LoadData("DesolatePlanetTypeDataNoPop")._producerValue, 1f), "Desolate producer value 1");
        ok &= Check(Near(LoadData("FarmPlanetTypeDataNoPop")._producerValue, 0.5f), "Farm producer value 0.5");
        foreach (var other in new[] { "NormalPlanetTypeDataNoPop", "IndustrialPlanetTypeDataNoPop", "OceanPlanetResourceDataNoPop",
                     "DesertPlanerResourceDataNoPop", "PrimePlanetTypeDataPopTest" })
            ok &= Check(Near(LoadData(other)._producerValue, 0f), $"{other} producer value 0 (output is local-only today)");

        var template = WarshipSelfCheck.MakeTemplate();
        var constants = WarshipSelfCheck.MakeConstants(template);
        var go = new GameObject("BIProducerValueMap");
        try
        {
            var map = go.AddComponent<GameAIMap>();
            map.GameAIMapInit(new List<PlanetSpawnData>
            {
                Spawn("V", 0f, 0f, new[] { "N" }, LoadData("VerdantPlanetTypeDataNoPop")),
                Spawn("N", 100f, 0f, new[] { "V" }),
            }, constants);
            ok &= Check(Near(map.ProducerValue("V"), 1f), "a Verdant planet's producer value reaches the map: 1");
            ok &= Check(Near(map.ProducerValue("N"), 0f), "a planet with default resource data: 0");
            ok &= Check(Near(map.ProducerValue("nope"), 0f), "an unknown planet name: 0");
        }
        finally
        {
            Object.DestroyImmediate(go);
            Object.DestroyImmediate(constants);
            Object.DestroyImmediate(template);
        }
        return ok;
    }
}
