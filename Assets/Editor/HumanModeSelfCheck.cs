using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using FlatSpace.AI;
using FlatSpace.Human;
using Flatspace.Objects.Production;

// Self-check for human player mode (docs/superpowers/specs/2026-10-23-human-player-mode-design.md). Like the AI suites it never
// touches Gameboard.Instance: it builds a minimal GameAIMap / Planet / PlayerAI set directly.
public static class HumanModeSelfCheck
{
    private static int _checkCount;

    [MenuItem("FlatSpace/Human/Run Human Mode Self-Check")]
    public static void Run()
    {
        var ok = RunChecks();
        Debug.Log(ok
            ? $"[HumanModeSelfCheck] ALL PASSED ({_checkCount} assertions ran)"
            : $"[HumanModeSelfCheck] FAILURES (see errors above; {_checkCount} assertions ran)");
    }

    public static bool RunChecks()
    {
        _checkCount = 0;
        var ok = RunMatchSettingsChecks();
        ok &= RunSaveSettingsChecks();
        ok &= RunHumanFlagChecks();
        ok &= RunCommandParsingChecks();
        ok &= RunFleetCommandChecks();
        ok &= RunColonizeCommandChecks();
        ok &= RunProductionCommandChecks();
        ok &= RunResearchQueueChecks();
        ok &= RunHumanRoutineChecks();
        ok &= RunDiplomacyCommandChecks();
        ok &= RunTurnEventChecks();
        ok &= RunPauseSettingsChecks();
        ok &= RunDefeatChecks();
        ok &= RunReviewFixChecks();
        return ok;
    }

    private static bool Check(bool condition, string label)
    {
        _checkCount++;
        if (!condition) Debug.LogError($"[HumanModeSelfCheck] FAIL: {label}");
        return condition;
    }

    #region Fixtures

    private static PlanetSpawnData MakeSpawn(string name, float x, IEnumerable<string> connections = null, float baseFood = 1f)
    {
        var resourceData = ScriptableObject.CreateInstance<PlanetResourceData>();
        resourceData._initialPopulation = 0;
        resourceData._maxPopulation = 5;
        resourceData._baseFoodProduction = baseFood;   // below 1 a colonist needs a food rider (Planet.NeedsColonyFoodRider)

        var spawn = ScriptableObject.CreateInstance<PlanetSpawnData>();
        spawn._planetName = name;
        spawn._planetPosition = new Vector3(x, 0f, 0f);   // distinct positions: PathingSystem tie hazard
        spawn._planetType = Planet.PlanetType.PlanetTypeNormal;
        spawn._resourceData = resourceData;
        spawn._connections = connections != null ? new List<string>(connections) : new List<string>();
        return spawn;
    }

    private static void Dock(Planet planet, Ship.ShipKind kind, int owner, int count)
    {
        for (var i = 0; i < count; i++)
            planet.DockShipFromSave(kind, owner, new List<string>());
    }

    // A chain P0 - P1 - ... of populated-by-nobody planets at x = 0, 100, 200 ... with a human PlayerAI for HumanId that knows
    // every planet. Dispose destroys the GameObjects.
    private sealed class Fixture : System.IDisposable
    {
        public const int HumanId = 0;
        public GameAIMap Map;
        public GameAIConstants Constants;
        public Player HumanPlayer;
        public PlayerAI Human;
        private readonly GameObject _mapGo;
        private readonly GameObject _playerGo;

        public Fixture(int planetCount, System.Action<GameAIConstants> tune = null, float baseFood = 1f)
        {
            _mapGo = new GameObject("HumanSelfCheckMap");
            _playerGo = new GameObject("HumanSelfCheckHuman");
            Map = _mapGo.AddComponent<GameAIMap>();
            Constants = ScriptableObject.CreateInstance<GameAIConstants>();
            Constants.defaultTravelSpeed = 1f;
            Constants.maxPathNodesForShipTransport = 10;
            Constants.maxPathNodesForColonization = 10;
            Constants.maxPathNodesForResourceDistribution = 10;
            tune?.Invoke(Constants);
            var spawns = new List<PlanetSpawnData>();
            for (var i = 0; i < planetCount; i++)
                spawns.Add(MakeSpawn("P" + i, i * 100f, i + 1 < planetCount ? new[] { "P" + (i + 1) } : null, baseFood));
            Map.GameAIMapInit(spawns, Constants);

            HumanPlayer = _playerGo.AddComponent<Player>();
            Human = _playerGo.AddComponent<PlayerAI>();
            Human.Player = HumanPlayer;
            Human.AIMap = Map;
            if (Human.ProductionCatalog == null) Human.ProductionCatalog = _playerGo.AddComponent<Catalog>();
            if (Human.ResearchCatalog == null) Human.ResearchCatalog = _playerGo.AddComponent<Catalog>();
            HumanPlayer.playerID = HumanId;
            HumanPlayer.isHuman = true;
            Map.Knowledge.SetKnownPlanets(HumanId, spawns.ConvertAll(s => s._planetName));
        }

        public Planet P(int index) => Map.GetPlanet("P" + index);

        public void Own(int index, int population, int player = HumanId)
        {
            P(index).ChangePopulation(population, player);   // also sets Planet.Owner
        }

        public void Dispose()
        {
            Object.DestroyImmediate(_playerGo);
            Object.DestroyImmediate(_mapGo);
        }
    }

    private static HumanCommand Cmd(string type, string origin = null, string target = null)
        => new HumanCommand { type = type, origin = origin, target = target };

    #endregion

    private static void DockDamaged(Planet planet, Ship.ShipKind kind, int owner, params float[] damages)
    {
        foreach (var damage in damages)
            planet.DockShipFromSave(kind, owner, new List<string>(), damage);
    }

    private static HumanCommand Move(string origin, string target, string kind = null, int count = 0, params int[] ships)
        => new HumanCommand { type = "move", origin = origin, target = target, kind = kind, count = count, ships = ships.Length > 0 ? new List<int>(ships) : null };

    private static string Refusal(HumanCommandProcessor processor, HumanCommand command)
    {
        var results = processor.Apply(new[] { command }, new List<GameAI.GameAIOrder>());
        return results[0].applied ? "" : results[0].reason;
    }

    #region Task 4: fleet commands

    public static bool RunFleetCommandChecks()
    {
        var ok = true;
        using (var f = new Fixture(5))
        {
            var p0 = f.P(0);
            DockDamaged(p0, Ship.ShipKind.WarShip, Fixture.HumanId, 0f, 5f, 0f, 7f);
            Dock(p0, Ship.ShipKind.WarShip, 1, 2);   // a rival's ships docked in the same place must never be sent
            var processor = new HumanCommandProcessor(f.Map, f.Human, 3);

            // Specific ships: dock indices 1 and 3 are the damaged ones.
            var orders = new List<GameAI.GameAIOrder>();
            var result = processor.Apply(new[] { Move("P0", "P1", ships: new[] { 1, 3 }) }, orders)[0];
            ok &= Check(result.applied, "moving two specific warships to a neighbour is applied: " + result.reason);
            var transport = orders.Find(o => o.Type == GameAI.GameAIOrder.OrderType.OrderTypeShipTransport);
            ok &= Check(transport != null && transport.Origin == "P0" && transport.Target == "P1" && transport.PlayerId == Fixture.HumanId,
                "a move emits a ship transport order from the origin to the target for the human");
            ok &= Check(transport != null && transport.Fleet.Kind == Ship.ShipKind.WarShip && transport.Fleet.Damage.Count == 2
                        && transport.Fleet.Damage[0] == 5f && transport.Fleet.Damage[1] == 7f,
                "the fleet carries the two chosen (damaged) ships' damage");
            ok &= Check(transport != null && transport.TimingDelay >= 1, "the transport delay is at least one turn");
            ok &= Check(orders.Exists(o => o.Type == GameAI.GameAIOrder.OrderType.OrderTypeShipDeparture && o.Origin == "P0")
                        && orders.Exists(o => o.Type == GameAI.GameAIOrder.OrderType.OrderTypeShipTransferInProgress && o.Target == "P1"),
                "a move emits the departure and the incoming-flag orders too");
            ok &= Check(p0.DockedShips[0].Damage == 5f && p0.DockedShips[1].Damage == 7f,
                "the chosen ships were put first, so the departure order takes exactly them");

            // A second move in the same batch cannot reuse those ships: 4 own warships, 2 claimed, 2 left.
            var again = new List<GameAI.GameAIOrder>();
            var second = processor.Apply(new[]
            {
                Move("P0", "P2", "Warship", 3),
                Move("P0", "P2", "Warship", 2),
            }, again);
            ok &= Check(!second[0].applied && second[0].reason == "NotEnoughShips", "asking for more ships than are left after earlier commands is NotEnoughShips");
            ok &= Check(second[1].applied, "the two ships still unclaimed can be moved");
        }

        using (var f = new Fixture(5))
        {
            var p0 = f.P(0);
            DockDamaged(p0, Ship.ShipKind.WarShip, Fixture.HumanId, 3f, 0f, 9f, 1f);
            var processor = new HumanCommandProcessor(f.Map, f.Human, 3);
            var orders = new List<GameAI.GameAIOrder>();
            processor.Apply(new[] { Move("P0", "P1", "Warship", 2) }, orders);
            var transport = orders.Find(o => o.Type == GameAI.GameAIOrder.OrderType.OrderTypeShipTransport);
            ok &= Check(transport != null && transport.Fleet.Damage.Count == 2 && transport.Fleet.Damage.TrueForAll(d => d <= 1f),
                "a count per kind sends the healthiest ships first");

            // Refusals.
            ok &= Check(Refusal(processor, Move("Nowhere", "P1", "Warship", 1)) == "UnknownPlanet", "an unknown origin is UnknownPlanet");
            ok &= Check(Refusal(processor, Move("P0", "Nowhere", "Warship", 1)) == "UnknownPlanet", "an unknown target is UnknownPlanet");
            ok &= Check(Refusal(processor, Move("P2", "P1", "Warship", 1)) == "NotYourPlanet", "an origin where the human has no ships or people is NotYourPlanet");
            ok &= Check(Refusal(processor, Move("P0", "P0", "Warship", 1)) == "SameDestination", "moving to the same planet is SameDestination");
            ok &= Check(Refusal(processor, Move("P0", "P1", "Bunker", 1)) == "UnknownKind", "bunkers cannot move: UnknownKind");
            ok &= Check(Refusal(processor, Move("P0", "P1", "Warship", 0)) == "BadCount", "a zero count with no ships listed is BadCount");
            ok &= Check(Refusal(processor, Move("P0", "P1", ships: new[] { 9 })) == "BadShipIndex", "a dock index that does not exist is BadShipIndex");
            ok &= Check(Refusal(processor, Move("P0", "P1")) == "BadCount", "a move naming neither ships nor a kind is BadCount");
        }

        using (var f = new Fixture(5, c => c.maxPathNodesForShipTransport = 3))
        {
            DockDamaged(f.P(0), Ship.ShipKind.WarShip, Fixture.HumanId, 0f, 0f);
            Dock(f.P(0), Ship.ShipKind.WarShip, 1, 1);
            Dock(f.P(0), Ship.ShipKind.ColonyShip, Fixture.HumanId, 1);
            var processor = new HumanCommandProcessor(f.Map, f.Human, 3);
            ok &= Check(Refusal(processor, Move("P0", "P3", "Warship", 1)) == "OutOfRange", "a target beyond the ship-transport node limit is OutOfRange");
            ok &= Check(Refusal(processor, Move("P0", "P2", "Warship", 1)) == "", "a target exactly at the node limit is accepted");
            ok &= Check(Refusal(processor, Move("P0", "P1", ships: new[] { 2 })) == "NotYourShips", "a dock index that is a rival's ship is NotYourShips");
            ok &= Check(Refusal(processor, Move("P0", "P1", "ColonyShip", 1)) == "", "a colony ship moves like any other ship");

            f.Map.Knowledge.SetKnownPlanets(Fixture.HumanId, new[] { "P0", "P1" });
            ok &= Check(Refusal(processor, Move("P0", "P2", "Warship", 1)) == "TargetUnknown", "a planet the human does not know is TargetUnknown");
        }

        using (var f = new Fixture(3))
        {
            DockDamaged(f.P(0), Ship.ShipKind.WarShip, Fixture.HumanId, 0f, 4f);
            Dock(f.P(0), Ship.ShipKind.ColonyShip, Fixture.HumanId, 1);
            Dock(f.P(0), Ship.ShipKind.WarShip, 1, 1);
            var docked = HumanFleetView.Docked(f.P(0), Fixture.HumanId);
            ok &= Check(docked.Count == 3, "the docked view lists only the human's ships");
            ok &= Check(docked.Exists(s => s.Kind == Ship.ShipKind.WarShip && s.Damaged && s.Damage == 4f)
                        && docked.Exists(s => s.Kind == Ship.ShipKind.WarShip && !s.Damaged)
                        && docked.TrueForAll(s => f.P(0).DockedShips[s.DockIndex].Owner == Fixture.HumanId),
                "damaged ships are flagged and each entry carries its dock index");

            var processor = new HumanCommandProcessor(f.Map, f.Human, 3);
            var orders = new List<GameAI.GameAIOrder>();
            processor.Apply(new[] { Move("P0", "P2", "Warship", 2) }, orders);
            var flights = HumanFleetView.InFlight(orders, Fixture.HumanId);
            ok &= Check(flights.Count == 1 && flights[0].Count == 2 && flights[0].Origin == "P0" && flights[0].Target == "P2" && flights[0].TurnsLeft >= 1,
                "the in-flight view lists the fleet just sent with its destination and turns left");
        }
        return ok;
    }

    #endregion


    #region Task 5: colonize

    private static HumanCommand Colonize(string origin, string target)
        => new HumanCommand { type = "colonize", origin = origin, target = target };

    public static bool RunColonizeCommandChecks()
    {
        var ok = true;
        using (var f = new Fixture(4))   // base food 1: a colonist feeds itself, so no rider
        {
            f.Own(0, 2);
            Dock(f.P(0), Ship.ShipKind.ColonyShip, Fixture.HumanId, 1);
            var processor = new HumanCommandProcessor(f.Map, f.Human, 3);
            var orders = new List<GameAI.GameAIOrder>();
            var result = processor.Apply(new[] { Colonize("P0", "P2") }, orders)[0];
            ok &= Check(result.applied, "colonizing an empty known planet with a docked colony ship is applied: " + result.reason);
            var colonist = orders.Find(o => o.Type == GameAI.GameAIOrder.OrderType.OrderTypePopulationTransport);
            ok &= Check(colonist != null && colonist.Origin == "P0" && colonist.Target == "P2" && colonist.PlayerId == Fixture.HumanId
                        && colonist.TimingType == GameAI.GameAIOrder.OrderTimingType.OrderTimingTypeDelayed && colonist.TimingDelay >= 1
                        && colonist.Route != null && colonist.Route.Count >= 2,
                "the colonist order is a delayed transport from the origin to the target with its route");
            ok &= Check(orders.Exists(o => o.Type == GameAI.GameAIOrder.OrderType.OrderTypePopulationChange && o.Origin == "P0" && System.Convert.ToSingle(o.Data) == -1f),
                "the colonist is taken from the origin");
            ok &= Check(orders.Exists(o => o.Type == GameAI.GameAIOrder.OrderType.OrderTypePopulationTransferInProgress && o.Target == "P2"),
                "the target is flagged as having a colonist inbound");
            ok &= Check(orders.Exists(o => o.Type == GameAI.GameAIOrder.OrderType.OrderTypeRemoveShip && o.Origin == "P0"),
                "the colony ship is consumed at the origin");
            ok &= Check(!orders.Exists(o => o.Type == GameAI.GameAIOrder.OrderType.OrderTypeColonyFoodRider),
                "a target that feeds a colonist gets no food rider");

            var again = processor.Apply(new[] { Colonize("P0", "P3") }, new List<GameAI.GameAIOrder>())[0];
            ok &= Check(!again.applied && again.reason == "NoColonyShip", "the only colony ship was used by the first command of the batch: NoColonyShip");
        }

        using (var f = new Fixture(4, baseFood: 0f))   // base food 0: a colonist needs the rider
        {
            f.Own(0, 2);
            Dock(f.P(0), Ship.ShipKind.ColonyShip, Fixture.HumanId, 2);
            var processor = new HumanCommandProcessor(f.Map, f.Human, 3);
            f.P(0).Food = 5f;
            ok &= Check(Refusal(processor, Colonize("P0", "P2")) == "CannotPayRider", "an origin holding less food than the rider cannot colonize a target that needs one");

            f.P(0).Food = 40f;
            var orders = new List<GameAI.GameAIOrder>();
            var result = processor.Apply(new[] { Colonize("P0", "P2") }, orders)[0];
            ok &= Check(result.applied, "with enough food the colonization is applied: " + result.reason);
            var rider = orders.Find(o => o.Type == GameAI.GameAIOrder.OrderType.OrderTypeColonyFoodRider);
            ok &= Check(rider != null && rider.Target == "P2" && System.Convert.ToSingle(rider.Data) == f.Constants.colonyFoodRider,
                "the food rider is delivered to the target with the colonist (applied on every human use)");
            ok &= Check(orders.Exists(o => o.Type == GameAI.GameAIOrder.OrderType.OrderTypeFoodChange && o.Origin == "P0"
                                          && System.Convert.ToSingle(o.Data) == -f.Constants.colonyFoodRider),
                "the rider's food is deducted at the origin");
        }

        using (var f = new Fixture(5, c => c.maxPathNodesForColonization = 3))
        {
            f.Own(0, 2);
            f.Own(1, 1);
            Dock(f.P(0), Ship.ShipKind.ColonyShip, Fixture.HumanId, 1);
            Dock(f.P(1), Ship.ShipKind.ColonyShip, Fixture.HumanId, 1);
            var processor = new HumanCommandProcessor(f.Map, f.Human, 3);
            ok &= Check(Refusal(processor, Colonize("Nowhere", "P2")) == "UnknownPlanet", "an unknown origin is UnknownPlanet");
            ok &= Check(Refusal(processor, Colonize("P0", "Nowhere")) == "UnknownPlanet", "an unknown target is UnknownPlanet");
            ok &= Check(Refusal(processor, Colonize("P2", "P3")) == "NotYourPlanet", "a planet the human has no people or ships on is NotYourPlanet");
            ok &= Check(Refusal(processor, Colonize("P0", "P0")) == "SameDestination", "colonizing the origin itself is SameDestination");
            ok &= Check(Refusal(processor, Colonize("P0", "P4")) == "OutOfRange", "a target beyond the colonization node limit is OutOfRange");
            ok &= Check(Refusal(processor, Colonize("P1", "P2")) == "OriginTooSmall", "a colonist cannot be taken from a planet with a single inhabitant");

            f.Own(2, 5, player: 1);   // a full planet of the rival's
            ok &= Check(Refusal(processor, Colonize("P0", "P2")) == "TargetNotValid", "a target the colonization rules reject (full) is TargetNotValid");
            f.P(2).Population.Clear();   // empty again, so only the unknown-ness can refuse it
            f.Map.Knowledge.SetKnownPlanets(Fixture.HumanId, new[] { "P0", "P1" });
            ok &= Check(Refusal(processor, Colonize("P0", "P2")) == "TargetNotValid", "a target the human does not know is TargetNotValid");
        }
        return ok;
    }

    #endregion

    #region Task 6: production queue and the development switch

    private static CatalogItem MakeItem(string name, string type, string subType, int tier = 1, bool researched = true)
    {
        var item = ScriptableObject.CreateInstance<CatalogItem>();
        item.itemName = name;
        item.name = name;
        item.type = type;
        item.subType = subType;
        item.tier = tier;
        item.cost = 10f;
        item.effect = "10";
        item.researched = researched;
        return item;
    }

    private static HumanCommand Queue(string planet, string item)
        => new HumanCommand { type = "queueProduction", origin = planet, item = item };

    public static bool RunProductionCommandChecks()
    {
        var ok = true;
        using (var f = new Fixture(3))
        {
            f.Own(0, 2);
            f.Own(1, 1, player: 1);
            var food1 = MakeItem("TestFood1", "Improvement", "Food", 1);
            var food2 = MakeItem("TestFood2", "Improvement", "Food", 2);
            var locked = MakeItem("TestLocked", "Improvement", "Industry", 1, researched: false);
            var bunker = MakeItem("TestBunker", "Defense", "Bunker");
            f.Human.ProductionCatalog.catalogItems.AddRange(new[] { food1, food2, locked, bunker });
            var processor = new HumanCommandProcessor(f.Map, f.Human, 4);

            var orders = new List<GameAI.GameAIOrder>();
            var results = processor.Apply(new[] { Queue("P0", "TestFood1"), Queue("P0", "TestFood2") }, orders);
            ok &= Check(results[0].applied && results[1].applied, "two valid items can be queued on an owned planet: " + results[0].reason + " " + results[1].reason);
            var setOrders = orders.FindAll(o => o.Type == GameAI.GameAIOrder.OrderType.OrderTypeIndustrySetProduction);
            ok &= Check(setOrders.Count == 2 && setOrders[0].Data.ToString() == "TestFood1" && setOrders[1].Data.ToString() == "TestFood2"
                        && setOrders[0].Origin == "P0" && setOrders[0].Target == "P0" && setOrders[0].PlayerId == Fixture.HumanId
                        && setOrders[0].TimingType == GameAI.GameAIOrder.OrderTimingType.OrderTimingTypeImmediate,
                "queued items become immediate set-production orders in the order given");

            ok &= Check(Refusal(processor, Queue("P0", "NoSuchItem")) == "UnknownItem", "an item not in the production catalog is UnknownItem");
            ok &= Check(Refusal(processor, Queue("P0", "TestLocked")) == "NotUnlocked", "an unresearched item is NotUnlocked");
            ok &= Check(Refusal(processor, Queue("Nowhere", "TestFood1")) == "UnknownPlanet", "an unknown planet is UnknownPlanet");
            ok &= Check(Refusal(processor, Queue("P1", "TestFood1")) == "NotYourPlanet", "a planet another player owns is NotYourPlanet");
            ok &= Check(Refusal(processor, Queue("P2", "TestFood1")) == "NotYourPlanet", "an unowned planet is NotYourPlanet");

            f.P(0).RecordImprovement(food2);   // the tier-2 food improvement is built: tier 1 is superseded and tier 2 is built
            ok &= Check(Refusal(processor, Queue("P0", "TestFood2")) == "AlreadyBuilt", "an improvement the planet already has is AlreadyBuilt");
            ok &= Check(Refusal(processor, Queue("P0", "TestFood1")) == "Superseded", "a tier below the planet's best is Superseded");

            for (var i = 0; i < f.P(0).MaxPopulation; i++)
                Dock(f.P(0), Ship.ShipKind.Bunker, Fixture.HumanId, 1);
            ok &= Check(Refusal(processor, Queue("P0", "TestBunker")) == "BunkerCap", "a bunker over the population cap is BunkerCap");
        }

        using (var f = new Fixture(2))
        {
            f.Own(0, 2);
            var a = MakeItem("QA", "Ship Type", "Warship");
            var b = MakeItem("QB", "Ship Type", "Warship");
            var c = MakeItem("QC", "Ship Type", "Warship");
            f.Human.ProductionCatalog.catalogItems.AddRange(new[] { a, b, c });
            foreach (var item in new[] { a, b, c })
                f.P(0).ProductionQueue.Add(new Planet.ProductionItem(item));
            var processor = new HumanCommandProcessor(f.Map, f.Human, 4);

            ok &= Check(processor.Apply(new[] { new HumanCommand { type = "moveProduction", origin = "P0", index = 2, toIndex = 0 } },
                new List<GameAI.GameAIOrder>())[0].applied, "a queue entry can be moved to the front");
            ok &= Check(f.P(0).ProductionQueue.ConvertAll(p => p.Item.itemName).SequenceEqual(new[] { "QC", "QA", "QB" }),
                "moving index 2 to 0 reorders the queue");
            ok &= Check(processor.Apply(new[] { new HumanCommand { type = "removeProduction", origin = "P0", index = 1 } },
                new List<GameAI.GameAIOrder>())[0].applied && f.P(0).ProductionQueue.ConvertAll(p => p.Item.itemName).SequenceEqual(new[] { "QC", "QB" }),
                "a queue entry can be removed by index");
            ok &= Check(Refusal(processor, new HumanCommand { type = "removeProduction", origin = "P0", index = 5 }) == "BadIndex", "removing past the end is BadIndex");
            ok &= Check(Refusal(processor, new HumanCommand { type = "moveProduction", origin = "P0", index = 0, toIndex = 9 }) == "BadIndex", "moving to past the end is BadIndex");
            ok &= Check(Refusal(processor, new HumanCommand { type = "removeProduction", origin = "P1", index = 0 }) == "NotYourPlanet", "editing the queue of a planet the human does not own is NotYourPlanet");

            // The development switch: flag true = automatic (the AI chooses what the planet builds).
            f.P(0).ManualDevelopment = true;
            ok &= Check(processor.Apply(new[] { new HumanCommand { type = "setDevelopment", origin = "P0", flag = true } }, new List<GameAI.GameAIOrder>())[0].applied
                        && !f.P(0).ManualDevelopment, "setDevelopment true makes the planet automatic");
            processor.Apply(new[] { new HumanCommand { type = "setDevelopment", origin = "P0", flag = false } }, new List<GameAI.GameAIOrder>());
            ok &= Check(f.P(0).ManualDevelopment, "setDevelopment false makes the planet manual");
            ok &= Check(Refusal(processor, new HumanCommand { type = "setDevelopment", origin = "P1", flag = true }) == "NotYourPlanet", "switching a planet the human does not own is NotYourPlanet");

            // A queue head that stopped being valid is dropped on a manual planet and reported.
            var built = MakeItem("QBuilt", "Improvement", "Food", 1);
            f.P(0).ProductionQueue.Clear();
            f.P(0).RecordImprovement(built);
            f.P(0).ProductionQueue.Add(new Planet.ProductionItem(built));
            f.P(0).ProductionQueue.Add(new Planet.ProductionItem(a));
            var dropResults = new List<Planet.UpdateResult>();
            f.P(0).StartNextProduction(dropResults);
            ok &= Check(f.P(0).CurrentProduction.HasValue && f.P(0).CurrentProduction.Value.Item.itemName == "QA",
                "a manual planet skips an invalid queue head and starts the next valid item");
            ok &= Check(dropResults.Exists(r => r.Result == Planet.UpdateResult.UpdateResultType.UpdateResultTypeProductionDropped && r.Data.ToString() == "QBuilt"),
                "the dropped item is reported as a ProductionDropped result");
        }

        // The AI keeps choosing for planets it develops, and leaves a manual planet to its owner.
        using (var f = new Fixture(2))
        {
            f.HumanPlayer.isHuman = false;   // the same PlayerAI, now an ordinary AI player
            f.Own(0, 2);
            var ship = MakeItem("AutoWarship", "Ship Type", "Warship");
            f.Human.ProductionCatalog.catalogItems.Add(ship);
            var results = new List<Planet.UpdateResult>
            {
                new Planet.UpdateResult("P0", Planet.UpdateResult.UpdateResultType.UpdateResultTypeIndustryProductionQueueEmpty, null, Fixture.HumanId),
            };
            var automatic = f.Human.BuildIndustryMatrix(results, PlayerAI.AIStrategy.AIStrategyExpand);
            ok &= Check(automatic != null && automatic.MatrixElements.Count == 1, "positive control: an automatic planet with an empty queue gets a production decision");
            f.P(0).ManualDevelopment = true;
            var manual = f.Human.BuildIndustryMatrix(results, PlayerAI.AIStrategy.AIStrategyExpand);
            ok &= Check(manual == null || manual.MatrixElements.Count == 0, "a manual planet gets no production decision from the AI");
        }

        // A planet that changes hands takes the new owner's default: manual for a human, automatic for anyone else.
        using (var f = new Fixture(2))
        {
            Planet.HumanOwnerTest = id => id == Fixture.HumanId;
            try
            {
                f.Own(0, 1, player: 1);
                ok &= Check(!f.P(0).ManualDevelopment, "a planet an AI player takes stays automatic");
                f.Own(1, 1);
                ok &= Check(f.P(1).ManualDevelopment, "a planet the human takes becomes manual");
                f.P(1).ConvertInhabitant(Fixture.HumanId, 1);
                ok &= Check(!f.P(1).ManualDevelopment, "a planet the human loses to an AI player becomes automatic again");
            }
            finally { Planet.HumanOwnerTest = null; }
        }
        return ok;
    }

    #endregion


    #region Final review fixes

    private static HumanCommand Shipment(string origin, string target, string kind, int amount)
        => new HumanCommand { type = "shipResource", origin = origin, target = target, kind = kind, count = amount };

    public static bool RunReviewFixChecks()
    {
        var ok = true;

        // I2: ship indices name the dock list as the human saw it, however many ships earlier commands of the batch already moved.
        using (var f = new Fixture(3))
        {
            DockDamaged(f.P(0), Ship.ShipKind.WarShip, Fixture.HumanId, 0f, 1f, 2f, 3f);   // the damage tells the ships apart
            var processor = new HumanCommandProcessor(f.Map, f.Human, 3);
            var orders = new List<GameAI.GameAIOrder>();
            var results = processor.Apply(new[]
            {
                Move("P0", "P1", ships: new[] { 3 }),
                Move("P0", "P2", ships: new[] { 1 }),
                Move("P0", "P1", ships: new[] { 0 }),
                Move("P0", "P1", ships: new[] { 3 }),
            }, orders);
            var fleets = orders.FindAll(o => o.Type == GameAI.GameAIOrder.OrderType.OrderTypeShipTransport);
            ok &= Check(results[0].applied && results[1].applied && results[2].applied, "three moves by index in one batch are applied: " + results[1].reason + " " + results[2].reason);
            ok &= Check(fleets.Count >= 3 && fleets[0].Fleet.Damage[0] == 3f && fleets[1].Fleet.Damage[0] == 1f && fleets[2].Fleet.Damage[0] == 0f,
                "each index names the ship it named when the batch began, not whichever ship an earlier command moved into that slot");
            ok &= Check(!results[3].applied && results[3].reason == "NotEnoughShips", "an index whose ship an earlier command already sent is NotEnoughShips");
        }

        // I5: colonize commands in one batch share the origin's people and food.
        using (var f = new Fixture(4))
        {
            f.Own(0, 2);
            Dock(f.P(0), Ship.ShipKind.ColonyShip, Fixture.HumanId, 2);
            var processor = new HumanCommandProcessor(f.Map, f.Human, 3);
            var results = processor.Apply(new[] { Colonize("P0", "P2"), Colonize("P0", "P3") }, new List<GameAI.GameAIOrder>());
            ok &= Check(results[0].applied && !results[1].applied && results[1].reason == "OriginTooSmall",
                "a second colonist cannot be taken from a two-person planet in the same batch: " + results[1].reason);
        }
        using (var f = new Fixture(4, baseFood: 0f))
        {
            f.Own(0, 4);
            f.P(0).Food = 12f;   // pays one food rider of 10, not two
            Dock(f.P(0), Ship.ShipKind.ColonyShip, Fixture.HumanId, 2);
            var processor = new HumanCommandProcessor(f.Map, f.Human, 3);
            var results = processor.Apply(new[] { Colonize("P0", "P2"), Colonize("P0", "P3") }, new List<GameAI.GameAIOrder>());
            ok &= Check(results[0].applied && !results[1].applied && results[1].reason == "CannotPayRider",
                "a second food rider that the origin's remaining food cannot pay is CannotPayRider: " + results[1].reason);
        }

        // I4: with shipments off the human orders them.
        using (var f = new Fixture(4, c => c.maxPathNodesForResourceDistribution = 3))
        {
            f.Own(0, 2);
            f.Own(1, 1, player: 1);
            f.P(0).Food = 50f;
            f.P(0).Grotsits = 30f;
            var processor = new HumanCommandProcessor(f.Map, f.Human, 3);
            var orders = new List<GameAI.GameAIOrder>();
            var result = processor.Apply(new[] { Shipment("P0", "P2", "food", 20) }, orders)[0];
            ok &= Check(result.applied, "shipping food to a known planet in range is applied: " + result.reason);
            var transport = orders.Find(o => o.Type == GameAI.GameAIOrder.OrderType.OrderTypeFoodTransport);
            ok &= Check(transport != null && transport.Origin == "P0" && transport.Target == "P2" && transport.PlayerId == Fixture.HumanId
                        && System.Convert.ToSingle(transport.Data) == 20f && transport.TimingType == GameAI.GameAIOrder.OrderTimingType.OrderTimingTypeDelayed
                        && transport.TimingDelay >= 1 && transport.Route != null && transport.Route.Count == 3,
                "a food shipment is a delayed transport order with its route");
            ok &= Check(orders.Exists(o => o.Type == GameAI.GameAIOrder.OrderType.OrderTypeFoodChange && o.Origin == "P0" && System.Convert.ToSingle(o.Data) == -20f)
                        && orders.Exists(o => o.Type == GameAI.GameAIOrder.OrderType.OrderTypeFoodTransportInProgress && o.Target == "P2"),
                "the food is deducted at the origin and the target is flagged as expecting a shipment");

            var grotsits = new List<GameAI.GameAIOrder>();
            processor.Apply(new[] { Shipment("P0", "P1", "grotsits", 10) }, grotsits);
            ok &= Check(grotsits.Exists(o => o.Type == GameAI.GameAIOrder.OrderType.OrderTypeGrotsitsTransport)
                        && grotsits.Exists(o => o.Type == GameAI.GameAIOrder.OrderType.OrderTypeGrotsitsChange)
                        && grotsits.Exists(o => o.Type == GameAI.GameAIOrder.OrderType.OrderTypeGrotsitsTransportInProgress),
                "grotsits ship the same way with the grotsits orders");

            var batch = processor.Apply(new[] { Shipment("P0", "P1", "food", 30), Shipment("P0", "P1", "food", 30) }, new List<GameAI.GameAIOrder>());
            ok &= Check(batch[0].applied && !batch[1].applied && batch[1].reason == "NotEnoughStock",
                "two shipments in one batch cannot together exceed the origin's stock (50 - 20 sent = 30 left... the first batch command took 30 of the remaining 30)");

            var fresh = new HumanCommandProcessor(f.Map, f.Human, 3);
            ok &= Check(Refusal(fresh, Shipment("Nowhere", "P1", "food", 5)) == "UnknownPlanet", "an unknown origin is UnknownPlanet");
            ok &= Check(Refusal(fresh, Shipment("P1", "P0", "food", 5)) == "NotYourPlanet", "shipping from a planet the human does not populate is NotYourPlanet");
            ok &= Check(Refusal(fresh, Shipment("P0", "P0", "food", 5)) == "SameDestination", "shipping to the same planet is SameDestination");
            ok &= Check(Refusal(fresh, Shipment("P0", "P1", "gold", 5)) == "UnknownKind", "a resource other than food or grotsits is UnknownKind");
            ok &= Check(Refusal(fresh, Shipment("P0", "P1", "food", 0)) == "BadCount", "a zero amount is BadCount");
            ok &= Check(Refusal(fresh, Shipment("P0", "P3", "food", 5)) == "OutOfRange", "a target beyond the resource-distribution node limit is OutOfRange");
            f.Map.Knowledge.SetKnownPlanets(Fixture.HumanId, new[] { "P0", "P1" });
            ok &= Check(Refusal(fresh, Shipment("P0", "P2", "food", 5)) == "TargetUnknown", "a planet the human does not know is TargetUnknown");
        }
        return ok;
    }

    #endregion

    #region Task 12: defeat

    public static bool RunDefeatChecks()
    {
        var ok = true;
        using (var f = new Fixture(3))
        {
            var noOrders = new List<GameAI.GameAIOrder>();
            f.Own(0, 1);
            ok &= Check(!HumanStatus.IsDefeated(f.Map, noOrders, Fixture.HumanId, out var populated, out _, out _) && populated == 1,
                "a player with a populated planet is not defeated");
            f.P(0).ChangePopulation(-1, Fixture.HumanId);

            Dock(f.P(1), Ship.ShipKind.ColonyShip, Fixture.HumanId, 1);
            ok &= Check(!HumanStatus.IsDefeated(f.Map, noOrders, Fixture.HumanId, out populated, out var ships, out _) && populated == 0 && ships == 1,
                "no people but a docked colony ship is not defeated");
            f.P(1).UndockShip(Ship.ShipKind.ColonyShip);

            var colonist = new List<GameAI.GameAIOrder>
            {
                OrderOf(GameAI.GameAIOrder.OrderType.OrderTypePopulationTransport, Fixture.HumanId, "P0", "P2"),
                OrderOf(GameAI.GameAIOrder.OrderType.OrderTypePopulationTransport, 1, "P0", "P2"),
            };
            ok &= Check(!HumanStatus.IsDefeated(f.Map, colonist, Fixture.HumanId, out _, out _, out var colonists) && colonists == 1,
                "no people and no ship but a colonist in flight is not defeated (a rival's colonist does not count)");

            Dock(f.P(2), Ship.ShipKind.ColonyShip, 1, 1);
            Dock(f.P(2), Ship.ShipKind.WarShip, Fixture.HumanId, 3);
            ok &= Check(HumanStatus.IsDefeated(f.Map, new List<GameAI.GameAIOrder> { colonist[1] }, Fixture.HumanId, out populated, out ships, out colonists)
                        && populated == 0 && ships == 0 && colonists == 0,
                "no people, no colony ship (warships and a rival's colony ship do not count) and no colonist: defeated");
        }
        return ok;
    }

    #endregion

    #region Task 11: pause settings

    public static bool RunPauseSettingsChecks()
    {
        var ok = true;
        var on = new[]
        {
            HumanEventKind.PlanetAttacked, HumanEventKind.PlanetBlockaded, HumanEventKind.PlanetLost, HumanEventKind.FirstContact,
            HumanEventKind.WarDeclared, HumanEventKind.PeaceMade, HumanEventKind.FleetArrived, HumanEventKind.ShortageFood, HumanEventKind.ShortageGrotsits,
        };
        var off = new[]
        {
            HumanEventKind.ProductionComplete, HumanEventKind.ResearchComplete, HumanEventKind.ColonyShipReady, HumanEventKind.PlanetIdle, HumanEventKind.ResearchIdle,
        };
        var defaults = HumanPauseSettings.Defaults();
        ok &= Check(on.All(defaults.IsEnabled), "the default pause events are on: attacked, blockaded, lost, contact, war, peace, fleet arrived, both shortages");
        ok &= Check(off.All(k => !defaults.IsEnabled(k)), "production, research, colony ship, planet idle and research idle are off by default");
        ok &= Check(!defaults.IsEnabled(HumanEventKind.PopulationGained) && !defaults.IsEnabled(HumanEventKind.TransportOrdered),
            "message-only kinds can never pause");

        var e = new HumanEvent { Kind = HumanEventKind.ShortageFood, Planet = "P0", Player = 0 };
        ok &= Check(defaults.ShouldPause(new[] { e }, out var first) && first.Planet == "P0", "an enabled event pauses and is reported");
        ok &= Check(!defaults.ShouldPause(new[] { new HumanEvent { Kind = HumanEventKind.PopulationLost } }, out _), "a message-only event does not pause");
        ok &= Check(!defaults.ShouldPause(new[] { new HumanEvent { Kind = HumanEventKind.FleetArrived, Rival = true } }, out _), "a rival's event never pauses");
        ok &= Check(!defaults.ShouldPause(new HumanEvent[0], out _), "no events do not pause");

        defaults.Set(HumanEventKind.ShortageFood, false);
        defaults.Set(HumanEventKind.ProductionComplete, true);
        ok &= Check(!defaults.ShouldPause(new[] { e }, out _), "a disabled event no longer pauses");
        ok &= Check(defaults.ShouldPause(new[] { new HumanEvent { Kind = HumanEventKind.ProductionComplete } }, out _), "an enabled optional event pauses");
        defaults.Set(HumanEventKind.PopulationGained, true);
        ok &= Check(!defaults.IsEnabled(HumanEventKind.PopulationGained), "Set ignores a message-only kind");

        var restored = HumanPauseSettings.FromSave(true, defaults.ToSave());
        ok &= Check(!restored.IsEnabled(HumanEventKind.ShortageFood) && restored.IsEnabled(HumanEventKind.ProductionComplete) && restored.IsEnabled(HumanEventKind.PlanetLost),
            "settings round-trip through the save list");
        var older = HumanPauseSettings.FromSave(false, null);
        ok &= Check(on.All(older.IsEnabled) && off.All(k => !older.IsEnabled(k)), "a save without pause settings restores the defaults");
        var emptyButSaved = HumanPauseSettings.FromSave(true, new List<int>());
        ok &= Check(!emptyButSaved.IsEnabled(HumanEventKind.PlanetLost), "a saved empty list means nothing pauses");

        using (var f = new Fixture(2))
        {
            var settings = HumanPauseSettings.Defaults();
            var processor = new HumanCommandProcessor(f.Map, f.Human, 1) { PauseSettings = settings };
            ok &= Check(processor.Apply(new[] { new HumanCommand { type = "setPause", eventName = "ProductionComplete", flag = true } }, new List<GameAI.GameAIOrder>())[0].applied
                        && settings.IsEnabled(HumanEventKind.ProductionComplete), "setPause turns an event on");
            ok &= Check(processor.Apply(new[] { new HumanCommand { type = "setPause", eventName = "shortagefood", flag = false } }, new List<GameAI.GameAIOrder>())[0].applied
                        && !settings.IsEnabled(HumanEventKind.ShortageFood), "event names are not case sensitive");
            ok &= Check(Refusal(processor, new HumanCommand { type = "setPause", eventName = "Nonsense", flag = true }) == "UnknownEvent", "an unknown event name is UnknownEvent");
            ok &= Check(Refusal(processor, new HumanCommand { type = "setPause", eventName = "PopulationGained", flag = true }) == "NotAPauseEvent", "a message-only kind is NotAPauseEvent");
            var noSettings = new HumanCommandProcessor(f.Map, f.Human, 1);
            ok &= Check(Refusal(noSettings, new HumanCommand { type = "setPause", eventName = "PlanetLost", flag = true }) == "NoPauseSettings", "without settings attached setPause is NoPauseSettings, not an exception");
        }
        return ok;
    }

    #endregion

    #region Task 10: turn events

    private static bool Has(List<HumanEvent> events, HumanEventKind kind, string planet = null, bool rival = false, int player = int.MinValue)
        => events.Exists(e => e.Kind == kind && (planet == null || e.Planet == planet) && e.Rival == rival
                              && (player == int.MinValue || e.Player == player));

    private static Planet.UpdateResult Result(string planet, Planet.UpdateResult.UpdateResultType type, int player, object data = null)
        => new Planet.UpdateResult(planet, type, data, player);

    private static GameAI.GameAIOrder OrderOf(GameAI.GameAIOrder.OrderType type, int player, string origin = "", string target = "", object data = null)
        => new GameAI.GameAIOrder { Type = type, PlayerId = player, Origin = origin, Target = target, Data = data ?? 1 };

    private static HumanTurnInput Turn(
        List<Planet.UpdateResult> results = null, List<GameAI.GameAIOrder> executed = null, List<GameAI.GameAIOrder> newOrders = null,
        List<BlockadeSystem.BlockadeCut> cuts = null, List<ConversionReport> conversions = null,
        IEnumerable<string> blockaded = null, string researchCompleted = null, bool shipmentsAutomated = false)
        => new HumanTurnInput
        {
            Results = results ?? new List<Planet.UpdateResult>(),
            Executed = executed ?? new List<GameAI.GameAIOrder>(),
            NewOrders = newOrders ?? new List<GameAI.GameAIOrder>(),
            Cuts = cuts ?? new List<BlockadeSystem.BlockadeCut>(),
            Conversions = conversions ?? new List<ConversionReport>(),
            BlockadedPlanets = blockaded ?? new string[0],
            ResearchCompleted = researchCompleted,
            ShipmentsAutomated = shipmentsAutomated,
        };

    public static bool RunTurnEventChecks()
    {
        var ok = true;
        const int rivalId = 1;
        using (var f = new Fixture(4))
        {
            f.Own(0, 2);
            f.Own(1, 1);
            var events = new HumanTurnEvents();
            var first = events.Collect(f.Map, f.Human, Turn());
            ok &= Check(first.Count == 1 && Has(first, HumanEventKind.ResearchIdle),
                "an empty first turn takes the snapshots and reports only that research has nothing queued");

            // Results of the human's planets.
            var e = events.Collect(f.Map, f.Human, Turn(results: new List<Planet.UpdateResult>
            {
                Result("P0", Planet.UpdateResult.UpdateResultType.UpdateResultTypeFoodShortage, Fixture.HumanId, 5f),
                Result("P1", Planet.UpdateResult.UpdateResultType.UpdateResultTypeGrotsitsShortage, Fixture.HumanId, 5f),
                Result("P0", Planet.UpdateResult.UpdateResultType.UpdateResultTypeIndustryProductionComplete, Fixture.HumanId, "x"),
                Result("P0", Planet.UpdateResult.UpdateResultType.UpdateResultTypeColonizerReady, Fixture.HumanId, 1),
                Result("P1", Planet.UpdateResult.UpdateResultType.UpdateResultTypePopulationGain, Fixture.HumanId, 1),
                Result("P1", Planet.UpdateResult.UpdateResultType.UpdateResultTypePopulationLoss, Fixture.HumanId, 1),
                Result("P0", Planet.UpdateResult.UpdateResultType.UpdateResultTypeFoodShortage, rivalId, 5f),   // another player's shortage
            }));
            ok &= Check(Has(e, HumanEventKind.ShortageFood, "P0") && Has(e, HumanEventKind.ShortageGrotsits, "P1"), "shortages on the human's planets are events");
            ok &= Check(Has(e, HumanEventKind.ProductionComplete, "P0") && Has(e, HumanEventKind.ColonyShipReady, "P0"), "production complete and colony ship ready are events");
            ok &= Check(Has(e, HumanEventKind.PopulationGained, "P1") && Has(e, HumanEventKind.PopulationLost, "P1"), "population gain and loss are events");
            ok &= Check(e.FindAll(x => x.Kind == HumanEventKind.ShortageFood).Count == 1, "another player's shortage is not the human's event");

            // An empty queue: always a message, and an idle event only on a planet the human develops by hand.
            f.P(0).ManualDevelopment = true;
            f.P(1).ManualDevelopment = false;
            e = events.Collect(f.Map, f.Human, Turn(results: new List<Planet.UpdateResult>
            {
                Result("P0", Planet.UpdateResult.UpdateResultType.UpdateResultTypeIndustryProductionQueueEmpty, Fixture.HumanId),
                Result("P1", Planet.UpdateResult.UpdateResultType.UpdateResultTypeIndustryProductionQueueEmpty, Fixture.HumanId),
            }));
            ok &= Check(Has(e, HumanEventKind.ProductionQueueEmpty, "P0") && Has(e, HumanEventKind.ProductionQueueEmpty, "P1"), "an empty queue is a message on both planets");
            ok &= Check(Has(e, HumanEventKind.PlanetIdle, "P0") && !Has(e, HumanEventKind.PlanetIdle, "P1"), "an empty queue is an idle event only on the manual planet");

            // Fights, rival activity and visibility.
            e = events.Collect(f.Map, f.Human, Turn(results: new List<Planet.UpdateResult>
            {
                Result("P0", Planet.UpdateResult.UpdateResultType.UpdateResultTypeWarshipsLost, Fixture.HumanId),
                Result("P0", Planet.UpdateResult.UpdateResultType.UpdateResultTypeWarshipsLost, rivalId),
                Result("P3", Planet.UpdateResult.UpdateResultType.UpdateResultTypeWarshipsLost, rivalId),
                Result("P3", Planet.UpdateResult.UpdateResultType.UpdateResultTypeIndustryProductionComplete, rivalId, "x"),
            }));
            ok &= Check(Has(e, HumanEventKind.PlanetAttacked, "P0") && e.FindAll(x => x.Kind == HumanEventKind.PlanetAttacked && x.Planet == "P0").Count == 1,
                "a fight on the human's planet is one attack event however many results it produced");
            ok &= Check(Has(e, HumanEventKind.PlanetAttacked, "P3", rival: true) && Has(e, HumanEventKind.ProductionComplete, "P3", rival: true),
                "a rival's fight and production on a visible planet are rival events");
            f.Map.Knowledge.SetKnownPlanets(Fixture.HumanId, new[] { "P0", "P1", "P2" });
            e = events.Collect(f.Map, f.Human, Turn(results: new List<Planet.UpdateResult>
            {
                Result("P3", Planet.UpdateResult.UpdateResultType.UpdateResultTypeWarshipsLost, rivalId),
                Result("P3", Planet.UpdateResult.UpdateResultType.UpdateResultTypeIndustryProductionComplete, rivalId, "x"),
            }));
            ok &= Check(e.Count == 0, "the same events on a planet the human cannot see are not reported");
            f.Map.Knowledge.SetKnownPlanets(Fixture.HumanId, new[] { "P0", "P1", "P2", "P3" });

            // Orders: arrivals and stance changes.
            e = events.Collect(f.Map, f.Human, Turn(executed: new List<GameAI.GameAIOrder>
            {
                OrderOf(GameAI.GameAIOrder.OrderType.OrderTypeShipTransport, Fixture.HumanId, "P0", "P1"),
                OrderOf(GameAI.GameAIOrder.OrderType.OrderTypeShipTransport, rivalId, "P2", "P3"),
                OrderOf(GameAI.GameAIOrder.OrderType.OrderTypeDeclareWar, rivalId, data: Fixture.HumanId),
                OrderOf(GameAI.GameAIOrder.OrderType.OrderTypeMakePeace, Fixture.HumanId, data: rivalId),
                OrderOf(GameAI.GameAIOrder.OrderType.OrderTypeSurrender, rivalId, data: Fixture.HumanId),
            }));
            ok &= Check(Has(e, HumanEventKind.FleetArrived, "P1") && Has(e, HumanEventKind.FleetArrived, "P3", rival: true), "the human's and a rival's visible fleet arrivals are events");
            ok &= Check(Has(e, HumanEventKind.WarDeclared, player: rivalId) && e.FindAll(x => x.Kind == HumanEventKind.PeaceMade).Count == 1 && Has(e, HumanEventKind.PeaceMade, player: rivalId),
                "a war declared on the human is an event; a peace the human made and a surrender to the human are one 'peace with that player' event");

            // A war between two rivals is visible only when the human has met both.
            e = events.Collect(f.Map, f.Human, Turn(executed: new List<GameAI.GameAIOrder>
            {
                OrderOf(GameAI.GameAIOrder.OrderType.OrderTypeDeclareWar, 2, data: 3),
            }));
            ok &= Check(e.Count == 0, "a war between two players the human has not met is not reported");

            // Contact, blockades, ownership.
            Dock(f.P(1), Ship.ShipKind.WarShip, rivalId, 1);
            e = events.Collect(f.Map, f.Human, Turn(blockaded: new[] { "P0" }));
            ok &= Check(Has(e, HumanEventKind.FirstContact, player: rivalId), "a rival's ship on a planet the human knows is first contact");
            ok &= Check(Has(e, HumanEventKind.PlanetBlockaded, "P0"), "a planet newly blockaded is an event");
            e = events.Collect(f.Map, f.Human, Turn(blockaded: new[] { "P0" }));
            ok &= Check(e.Count == 0, "contact and an unchanged blockade are reported once");
            events.Collect(f.Map, f.Human, Turn());
            e = events.Collect(f.Map, f.Human, Turn(blockaded: new[] { "P0" }));
            ok &= Check(Has(e, HumanEventKind.PlanetBlockaded, "P0"), "a blockade that lifted and came back is reported again");

            f.P(2).ChangePopulation(1, Fixture.HumanId);
            f.P(3).ChangePopulation(1, rivalId);
            f.P(1).ChangePopulation(-1, Fixture.HumanId);
            e = events.Collect(f.Map, f.Human, Turn());
            ok &= Check(Has(e, HumanEventKind.PlanetColonized, "P2"), "the human taking an unowned planet is PlanetColonized");
            ok &= Check(Has(e, HumanEventKind.PlanetColonized, "P3", rival: true), "a rival taking an unowned visible planet is a rival PlanetColonized");
            ok &= Check(Has(e, HumanEventKind.PlanetLost, "P1"), "a planet the human no longer owns is PlanetLost");

            // Shipments cut, conversions, transports.
            e = events.Collect(f.Map, f.Human, Turn(
                cuts: new List<BlockadeSystem.BlockadeCut>
                {
                    new BlockadeSystem.BlockadeCut { PlayerId = Fixture.HumanId, Planet = "P1", Value = 3f, BlockerId = rivalId, Type = GameAI.GameAIOrder.OrderType.OrderTypeFoodTransport },
                    new BlockadeSystem.BlockadeCut { PlayerId = Fixture.HumanId, Planet = "P0", Value = 3f, BlockerId = rivalId, Type = GameAI.GameAIOrder.OrderType.OrderTypePopulationTransport },
                    new BlockadeSystem.BlockadeCut { PlayerId = rivalId, Planet = "P2", Value = 3f, BlockerId = 2, Type = GameAI.GameAIOrder.OrderType.OrderTypeGrotsitsTransport },
                },
                conversions: new List<ConversionReport>
                {
                    new ConversionReport { What = ConversionReport.Kind.Start, Planet = "P0", Dominator = rivalId },
                    new ConversionReport { What = ConversionReport.Kind.End, Planet = "P0", Dominator = rivalId },
                    new ConversionReport { What = ConversionReport.Kind.Start, Planet = "P3", Dominator = 2 },
                    new ConversionReport { What = ConversionReport.Kind.Flip, Planet = "P0", Dominator = rivalId },
                },
                newOrders: new List<GameAI.GameAIOrder> { OrderOf(GameAI.GameAIOrder.OrderType.OrderTypeFoodTransport, Fixture.HumanId, "P1", "P0") },
                shipmentsAutomated: false));
            ok &= Check(Has(e, HumanEventKind.ShipmentLost, "P1") && !Has(e, HumanEventKind.ShipmentLost, "P0") && Has(e, HumanEventKind.ShipmentLost, "P2", rival: true),
                "a lost food or grotsits shipment is an event; a lost colonist is not; a rival's visible loss is a rival event");
            ok &= Check(Has(e, HumanEventKind.ConversionBegan, "P0") && Has(e, HumanEventKind.ConversionEnded, "P0") && Has(e, HumanEventKind.ConversionBegan, "P3", rival: true),
                "conversion starting and ending on the human's and on visible planets are events");
            ok &= Check(!e.Exists(x => x.Kind == HumanEventKind.TransportOrdered), "transport orders are not events while shipments are not automated");
            e = events.Collect(f.Map, f.Human, Turn(
                newOrders: new List<GameAI.GameAIOrder> { OrderOf(GameAI.GameAIOrder.OrderType.OrderTypeFoodTransport, Fixture.HumanId, "P1", "P0") },
                shipmentsAutomated: true));
            ok &= Check(Has(e, HumanEventKind.TransportOrdered, "P0"), "with shipments automated a new transport order is an event");

            // Research.
            e = events.Collect(f.Map, f.Human, Turn(researchCompleted: "Laser"));
            ok &= Check(Has(e, HumanEventKind.ResearchComplete), "a completed research is an event");
            ok &= Check(!Has(e, HumanEventKind.ResearchIdle), "research idle was already reported and is not repeated every turn");
            f.Human.currentResearch = MakeResearch("Busy");
            ok &= Check(!Has(events.Collect(f.Map, f.Human, Turn()), HumanEventKind.ResearchIdle), "research in progress is not idle");
            f.Human.currentResearch = null;
            ok &= Check(Has(events.Collect(f.Map, f.Human, Turn()), HumanEventKind.ResearchIdle), "research that runs out of work is reported idle again");
        }
        return ok;
    }

    #endregion

    #region Task 9: diplomacy commands

    private static HumanCommand Diplo(string type, int rival) => new HumanCommand { type = type, rival = rival };

    public static bool RunDiplomacyCommandChecks()
    {
        var ok = true;
        using (var f = new Fixture(3))
        {
            f.Map.Diplomacy.Enabled = true;
            f.Own(0, 2);
            Dock(f.P(1), Ship.ShipKind.WarShip, 1, 1);   // player 1 is in contact (its ship sits on a planet the human knows); player 2 is not
            var processor = new HumanCommandProcessor(f.Map, f.Human, 4);

            var orders = new List<GameAI.GameAIOrder>();
            var declare = processor.Apply(new[] { Diplo("declareWar", 1) }, orders)[0];
            ok &= Check(declare.applied, "declaring war on a player in contact is applied: " + declare.reason);
            var order = orders.Find(o => o.Type == GameAI.GameAIOrder.OrderType.OrderTypeDeclareWar);
            ok &= Check(order != null && order.PlayerId == Fixture.HumanId && System.Convert.ToInt32(order.Data) == 1
                        && order.TimingType == GameAI.GameAIOrder.OrderTimingType.OrderTimingTypeImmediate && string.IsNullOrEmpty(order.Origin) && string.IsNullOrEmpty(order.Target),
                "a stance command is an immediate order with the rival as its data and no planets");

            ok &= Check(Refusal(processor, Diplo("declareWar", 2)) == "NoContact", "declaring war on a player the human has not met is NoContact");
            ok &= Check(Refusal(processor, Diplo("declareWar", Fixture.HumanId)) == "UnknownRival", "declaring war on oneself is UnknownRival");
            ok &= Check(Refusal(processor, Diplo("declareWar", -1)) == "UnknownRival", "a negative rival id is UnknownRival");
            ok &= Check(Refusal(processor, Diplo("makePeace", 1)) == "AlreadyPeace", "making peace while at peace is AlreadyPeace");
            ok &= Check(Refusal(processor, Diplo("surrender", 1)) == "NotAtWar", "surrendering when not at war is NotAtWar");

            f.Map.Diplomacy.SetStance(Fixture.HumanId, 1, Stance.War, 3);
            ok &= Check(Refusal(processor, Diplo("declareWar", 1)) == "AlreadyAtWar", "declaring war when already at war is AlreadyAtWar");
            var peaceOrders = new List<GameAI.GameAIOrder>();
            ok &= Check(processor.Apply(new[] { Diplo("makePeace", 1) }, peaceOrders)[0].applied
                        && peaceOrders.Exists(o => o.Type == GameAI.GameAIOrder.OrderType.OrderTypeMakePeace && System.Convert.ToInt32(o.Data) == 1),
                "making peace while at war emits a make-peace order");
            var surrenderOrders = new List<GameAI.GameAIOrder>();
            ok &= Check(processor.Apply(new[] { Diplo("surrender", 1) }, surrenderOrders)[0].applied
                        && surrenderOrders.Exists(o => o.Type == GameAI.GameAIOrder.OrderType.OrderTypeSurrender && System.Convert.ToInt32(o.Data) == 1),
                "surrendering while at war emits a surrender order");

            // After a surrender both sides are in a truce, in which a new declaration is refused.
            f.Map.Diplomacy.SetStance(Fixture.HumanId, 1, Stance.Peace, 4);
            GameAI.ApplySurrender(f.Map.Diplomacy, surrenderOrders.Find(o => o.Type == GameAI.GameAIOrder.OrderType.OrderTypeSurrender), 4, 30);
            ok &= Check(Refusal(processor, Diplo("declareWar", 1)) == "Truce", "declaring war during a truce is Truce");
        }

        using (var f = new Fixture(2))
        {
            f.Map.Diplomacy.Enabled = false;
            Dock(f.P(1), Ship.ShipKind.WarShip, 1, 1);
            var processor = new HumanCommandProcessor(f.Map, f.Human, 4);
            ok &= Check(Refusal(processor, Diplo("declareWar", 1)) == "DiplomacyOff", "with diplomacy disabled the stance commands are DiplomacyOff");
        }
        return ok;
    }

    #endregion

    #region Task 8: the human's reduced routine and the shipments switch

    public static bool RunHumanRoutineChecks()
    {
        var ok = true;
        using (var f = new Fixture(3))
        {
            f.Own(0, 2);
            f.Own(1, 2);
            Dock(f.P(0), Ship.ShipKind.ColonyShip, Fixture.HumanId, 1);
            var results = new List<Planet.UpdateResult>
            {
                new Planet.UpdateResult("P0", Planet.UpdateResult.UpdateResultType.UpdateResultTypeFoodShortage, 10f, playerID: Fixture.HumanId),
                new Planet.UpdateResult("P1", Planet.UpdateResult.UpdateResultType.UpdateResultTypeFoodSurplus, 20f, playerID: Fixture.HumanId),
                new Planet.UpdateResult("P0", Planet.UpdateResult.UpdateResultType.UpdateResultTypeColonizerReady, 1, playerID: Fixture.HumanId),
            };

            ok &= Check(!f.Human.ShipmentsAutomated, "a human's shipments are off by default");
            var off = new List<GameAI.GameAIOrder>();
            f.Human.ProcessResults(results, off);
            ok &= Check(off.Count == 0, "with shipments off the human's ProcessResults emits nothing (no shipment, no colonist, no ship move, no stance)");

            var processor = new HumanCommandProcessor(f.Map, f.Human, 4);
            ok &= Check(processor.Apply(new[] { new HumanCommand { type = "setShipments", flag = true } }, new List<GameAI.GameAIOrder>())[0].applied
                        && f.Human.ShipmentsAutomated, "setShipments true turns shipments on");
            var on = new List<GameAI.GameAIOrder>();
            f.Human.ProcessResults(results, on);
            ok &= Check(on.Exists(o => o.Type == GameAI.GameAIOrder.OrderType.OrderTypeFoodTransport && o.Origin == "P1" && o.Target == "P0"),
                "with shipments on the surplus is shipped to the shortage as the AI would");
            ok &= Check(!on.Exists(o => o.Type == GameAI.GameAIOrder.OrderType.OrderTypePopulationTransport),
                "even with shipments on, the human's colony ship is never launched for them");
            ok &= Check(!on.Exists(o => o.Type == GameAI.GameAIOrder.OrderType.OrderTypeDeclareWar || o.Type == GameAI.GameAIOrder.OrderType.OrderTypeMakePeace
                                        || o.Type == GameAI.GameAIOrder.OrderType.OrderTypeSurrender || o.Type == GameAI.GameAIOrder.OrderType.OrderTypeShipTransport),
                "nor does the human's PlayerAI declare war, make peace, surrender or move ships");

            processor.Apply(new[] { new HumanCommand { type = "setShipments", flag = false } }, new List<GameAI.GameAIOrder>());
            ok &= Check(!f.Human.ShipmentsAutomated, "setShipments false turns them off again");
        }

        // Development: the human's PlayerAI still develops a planet the human switched to automatic, and only that planet.
        using (var f = new Fixture(3))
        {
            f.Own(0, 2);
            f.Own(1, 2);
            f.Human.ProductionCatalog.catalogItems.Add(MakeItem("HumanAutoWarship", "Ship Type", "Warship"));
            var results = new List<Planet.UpdateResult>
            {
                new Planet.UpdateResult("P0", Planet.UpdateResult.UpdateResultType.UpdateResultTypeIndustryProductionQueueEmpty, null, Fixture.HumanId),
                new Planet.UpdateResult("P1", Planet.UpdateResult.UpdateResultType.UpdateResultTypeIndustryProductionQueueEmpty, null, Fixture.HumanId),
            };
            f.P(0).ManualDevelopment = false;
            f.P(1).ManualDevelopment = true;
            var orders = new List<GameAI.GameAIOrder>();
            f.Human.ProcessResults(results, orders);
            var set = orders.FindAll(o => o.Type == GameAI.GameAIOrder.OrderType.OrderTypeIndustrySetProduction);
            ok &= Check(set.Count == 1 && set[0].Origin == "P0", "the human's PlayerAI chooses production only for the planet switched to automatic");
        }
        return ok;
    }

    #endregion

    #region Task 7: research queue

    private static CatalogItem MakeResearch(string name, string requiredTech = "", float cost = 10f)
    {
        var item = ScriptableObject.CreateInstance<CatalogItem>();
        item.itemName = name;
        item.name = name;
        item.type = "Research";
        item.subType = "Test";
        item.cost = cost;
        item.requiredTech = requiredTech;
        item.researched = false;
        return item;
    }

    private static HumanCommand QueueResearch(string item) => new HumanCommand { type = "queueResearch", item = item };

    public static bool RunResearchQueueChecks()
    {
        var ok = true;
        using (var f = new Fixture(1))
        {
            var a = MakeResearch("RA");
            var b = MakeResearch("RB", requiredTech: "RA");
            var c = MakeResearch("RC");
            var done = MakeResearch("RDone");
            done.researched = true;
            f.Human.ResearchCatalog.catalogItems.AddRange(new[] { a, b, c, done });
            var processor = new HumanCommandProcessor(f.Map, f.Human, 4);
            var noOrders = new List<GameAI.GameAIOrder>();

            ok &= Check(Refusal(processor, QueueResearch("RB")) == "PrerequisiteMissing", "an item whose prerequisite is neither researched nor queued ahead of it is PrerequisiteMissing");
            ok &= Check(Refusal(processor, QueueResearch("Nope")) == "UnknownItem", "an item not in the research catalog is UnknownItem");
            ok &= Check(Refusal(processor, QueueResearch("RDone")) == "AlreadyResearched", "a researched item is AlreadyResearched");
            ok &= Check(processor.Apply(new[] { QueueResearch("RA"), QueueResearch("RB"), QueueResearch("RC") }, noOrders).TrueForAll(r => r.applied),
                "an item can be queued after the prerequisite that is queued before it");
            ok &= Check(f.Human.ResearchQueue.SequenceEqual(new[] { "RA", "RB", "RC" }), "the queue keeps the order given");
            ok &= Check(Refusal(processor, QueueResearch("RA")) == "AlreadyQueued", "queueing an item twice is AlreadyQueued");
            ok &= Check(processor.Apply(new[] { new HumanCommand { type = "removeResearch", index = 2 } }, noOrders)[0].applied
                        && f.Human.ResearchQueue.SequenceEqual(new[] { "RA", "RB" }), "a queue entry can be removed by index");
            ok &= Check(Refusal(processor, new HumanCommand { type = "removeResearch", index = 7 }) == "BadIndex", "removing past the end is BadIndex");

            // Research accumulates and the queue is worked through in order.
            var orders = new List<GameAI.GameAIOrder>();
            f.Human.UpdateResearch(5f, orders);
            ok &= Check(f.Human.currentResearch == a && !f.Human.ResearchIdle, "the first queued item becomes the current research");
            ok &= Check(orders.Exists(o => o.Type == GameAI.GameAIOrder.OrderType.OrderTypeResearchSet && o.Target == "RA"), "starting it emits a research-set order");
            f.Human.UpdateResearch(5f, orders);
            ok &= Check(a.researched && f.Human.currentResearch == b, "when RA is paid for it completes and the next queued item (RB) starts");
            f.Human.UpdateResearch(10f, orders);
            ok &= Check(b.researched && f.Human.currentResearch == null && f.Human.ResearchIdle, "an empty queue leaves research idle");
            f.Human.UpdateResearch(7f, orders);
            ok &= Check(f.Human.researchTotal == 7f && f.Human.currentResearch == null, "idle research keeps accumulating points instead of discarding them");

            processor.Apply(new[] { QueueResearch("RC") }, noOrders);
            f.Human.UpdateResearch(5f, orders);
            ok &= Check(f.Human.currentResearch == c, "queueing while idle starts the item on the next research turn");
            f.Human.UpdateResearch(1f, orders);
            ok &= Check(c.researched, "the points banked while idle count toward the new item");
        }

        using (var f = new Fixture(1))
        {
            // A queued item that is no longer valid (researched elsewhere, or its prerequisite is gone) is skipped, not stuck on.
            var a = MakeResearch("SA");
            var b = MakeResearch("SB", requiredTech: "SA");
            var c = MakeResearch("SC");
            f.Human.ResearchCatalog.catalogItems.AddRange(new[] { a, b, c });
            f.Human.ResearchQueue.AddRange(new[] { "SB", "Gone", "SC" });   // SB cannot start (SA is not researched), Gone is not a research item
            f.Human.UpdateResearch(1f, new List<GameAI.GameAIOrder>());
            ok &= Check(f.Human.currentResearch == c && f.Human.ResearchQueue.Count == 0,
                "queue entries that cannot be researched are skipped and the next valid one starts");
        }
        return ok;
    }

    #endregion

    #region Task 3: command parsing and dispatch

    public static bool RunCommandParsingChecks()
    {
        var ok = true;
        var empty = HumanCommandProcessor.Parse("", out var emptyError);
        ok &= Check(empty.Count == 0 && !string.IsNullOrEmpty(emptyError), "an empty commands file parses to no commands and reports why");
        var bad = HumanCommandProcessor.Parse("{not json", out var badError);
        ok &= Check(bad.Count == 0 && !string.IsNullOrEmpty(badError), "malformed JSON parses to no commands and reports why, without throwing");
        var none = HumanCommandProcessor.Parse("{\"commands\":[]}", out var noneError);
        ok &= Check(none.Count == 0 && string.IsNullOrEmpty(noneError), "an empty batch is not an error");

        using (var f = new Fixture(2))
        {
            var processor = new HumanCommandProcessor(f.Map, f.Human, 5);
            var orders = new List<GameAI.GameAIOrder>();

            var results = processor.ApplyJson("{\"commands\":[{\"type\":\"warpDrive\"},{\"type\":\"\"}]}", orders);
            ok &= Check(results.Count == 2, "every command in a batch gets a result, in order");
            ok &= Check(!results[0].applied && results[0].reason == "UnknownCommand" && results[0].type == "warpDrive", "an unknown command type is refused as UnknownCommand");
            ok &= Check(!results[1].applied && results[1].reason == "UnknownCommand", "a command with no type is refused as UnknownCommand");
            ok &= Check(orders.Count == 0, "refused commands emit no orders");

            var parseFailure = processor.ApplyJson("{oops", orders);
            ok &= Check(parseFailure.Count == 1 && !parseFailure[0].applied && parseFailure[0].reason.StartsWith("ParseError"),
                "a file that does not parse gives one ParseError result");
            ok &= Check(processor.ApplyJson("", orders).Count == 1, "an empty file gives one ParseError result, not an exception");

            ok &= Check(processor.Apply(new List<HumanCommand> { null }, orders)[0].reason == "UnknownCommand", "a null entry is refused, not thrown on");
        }
        return ok;
    }

    #endregion

    #region Task 1: match settings

    public static bool RunMatchSettingsChecks()
    {
        var ok = true;
        ok &= Check(MatchSettings.ClampTurnLimit(250) == MatchSettings.DefaultTurnLimit, "a turn limit that is not a preset becomes the default");
        ok &= Check(MatchSettings.ClampTurnLimit(0) == 0, "0 (Unlimited) is a preset");
        ok &= Check(MatchSettings.ClampTurnLimit(100) == 100 && MatchSettings.ClampTurnLimit(200) == 200, "100 and 200 are presets");
        ok &= Check(MatchSettings.DefaultTurnLimit == 400, "the default turn limit is 400");

        MatchSettings.TurnLimit = 100;
        MatchSettings.HumanRequested = true;
        MatchSettings.HumanPlayerId = 2;
        MatchSettings.Reset();
        ok &= Check(MatchSettings.TurnLimit == 400 && !MatchSettings.HumanRequested && MatchSettings.HumanPlayerId == -1,
            "Reset restores 400, no human requested, no human id");
        return ok;
    }

    #endregion

    #region Task 2: human flag and saves

    public static bool RunSaveSettingsChecks()
    {
        var ok = true;
        // A save from before human mode has none of the keys: it must load as a zero-human, 400-turn match.
        MatchSettings.Reset();
        MatchSettings.TurnLimit = 100;   // proves the restore overwrites rather than leaves what was there
        var older = JsonUtility.FromJson<SaveLoadSystem.GameSave>("{\"turnNumber\":12}");
        SaveLoadSystem.ApplyMatchSettings(older);
        ok &= Check(!older.hasHuman && !older.hasMatchSettings, "an older save has no human and no match settings keys");
        ok &= Check(!MatchSettings.HumanRequested && MatchSettings.HumanPlayerId == -1 && MatchSettings.TurnLimit == MatchSettings.DefaultTurnLimit,
            "an older save restores a zero-human match with the default turn limit");

        var newer = JsonUtility.FromJson<SaveLoadSystem.GameSave>(
            "{\"hasMatchSettings\":true,\"turnLimit\":200,\"hasHuman\":true,\"humanPlayerId\":1}");
        SaveLoadSystem.ApplyMatchSettings(newer);
        ok &= Check(MatchSettings.HumanRequested && MatchSettings.HumanPlayerId == 1 && MatchSettings.TurnLimit == 200,
            "a save with a human restores the human id and the turn limit");

        var unlimited = JsonUtility.FromJson<SaveLoadSystem.GameSave>("{\"hasMatchSettings\":true,\"turnLimit\":0}");
        SaveLoadSystem.ApplyMatchSettings(unlimited);
        ok &= Check(MatchSettings.TurnLimit == 0 && !MatchSettings.HumanRequested, "a saved Unlimited limit (0) is kept, with no human");
        MatchSettings.Reset();
        return ok;
    }

    public static bool RunHumanFlagChecks()
    {
        var ok = true;
        var mapGo = new GameObject("HumanSelfCheckMap");
        var p0Go = new GameObject("HumanSelfCheckPlayer0");
        var p1Go = new GameObject("HumanSelfCheckPlayer1");
        try
        {
            var map = mapGo.AddComponent<GameAIMap>();
            var constants = ScriptableObject.CreateInstance<GameAIConstants>();
            constants.defaultTravelSpeed = 1f;
            constants.maxPathNodesForResourceDistribution = 10;
            map.GameAIMapInit(new List<PlanetSpawnData>
            {
                MakeSpawn("Short", 0f, new[] { "Surplus" }),
                MakeSpawn("Surplus", 100f),
            }, constants);
            map.GetPlanet("Surplus").Population.Add(new Planet.Inhabitant { Player = 0 });

            var results = new List<Planet.UpdateResult>
            {
                new Planet.UpdateResult("Short", Planet.UpdateResult.UpdateResultType.UpdateResultTypeFoodShortage, 10f, playerID: 0),
                new Planet.UpdateResult("Surplus", Planet.UpdateResult.UpdateResultType.UpdateResultTypeFoodSurplus, 20f, playerID: 0),
            };

            var aiPlayer = p0Go.AddComponent<Player>();
            var aiPlayerAI = p0Go.AddComponent<PlayerAI>();
            aiPlayerAI.Player = aiPlayer;
            aiPlayerAI.AIMap = map;
            var humanPlayer = p1Go.AddComponent<Player>();
            var humanAI = p1Go.AddComponent<PlayerAI>();
            humanAI.Player = humanPlayer;
            humanAI.AIMap = map;
            humanPlayer.playerID = 0;   // the same player id, so the same results apply; only the flag differs

            ok &= Check(!aiPlayerAI.HumanControlled && !humanAI.HumanControlled, "no player is human by default");
            var aiOrders = new List<GameAI.GameAIOrder>();
            aiPlayerAI.ProcessResults(results, aiOrders);
            ok &= Check(aiOrders.Exists(o => o.Type == GameAI.GameAIOrder.OrderType.OrderTypeFoodTransport),
                "positive control: a player that is not human ships surplus food to the shortage");

            humanPlayer.isHuman = true;
            ok &= Check(humanAI.HumanControlled, "HumanControlled follows Player.isHuman");
            var humanOrders = new List<GameAI.GameAIOrder>();
            humanAI.ProcessResults(results, humanOrders);
            ok &= Check(humanOrders.Count == 0, "a human player emits no orders of its own from ProcessResults (shipments are off by default)");
        }
        finally
        {
            Object.DestroyImmediate(p1Go);
            Object.DestroyImmediate(p0Go);
            Object.DestroyImmediate(mapGo);
        }
        return ok;
    }

    #endregion
}
