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
}
