using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using FlatSpace.AI;
using Flatspace.Objects.Resource;
using Flatspace.Objects.Production;

namespace FlatSpace
{
    namespace Human
    {
        /// <summary>
        /// Validates the human's commands and turns them into the orders the AI emits for the same actions, so a command never derives
        /// delays or the deduction / in-progress order pairs by hand. Pure: it reads a GameAIMap and the human's PlayerAI and never
        /// touches Gameboard.Instance, so a self-check drives it directly. A command is applied or refused with a reason from a closed
        /// set; nothing here throws on bad input.
        ///
        /// Command types (fields read): move (origin, target, ships | kind + count), colonize (origin, target), queueProduction (origin,
        /// item), removeProduction (origin, index), moveProduction (origin, index, toIndex), queueResearch (item), removeResearch
        /// (index), setShipments (flag), setDevelopment (origin, flag), declareWar / makePeace / surrender (rival), setPause (eventName, flag).
        /// </summary>
        public class HumanCommandProcessor
        {
            private readonly GameAIMap _map;
            private readonly PlayerAI _human;
            private readonly int _turn;

            public HumanCommandProcessor(GameAIMap map, PlayerAI human, int turn)
            {
                _map = map;
                _human = human;
                _turn = turn;
            }

            public int HumanId => _human.Player.playerID;

            /// <summary>The pause settings the setPause command edits; null (a self-check, or no human yet) refuses it.</summary>
            public HumanPauseSettings PauseSettings { get; set; }

            /// <summary>
            /// Whether a command would be accepted right now, without applying it: null when it would, else the refusal reason. Covers
            /// the commands a screen offers a choice of (queueProduction, queueResearch, colonize, shipResource); any other type is
            /// "CheckNotSupported". It runs the command on a scratch processor and order list, so no queue, claim or order survives it.
            /// </summary>
            public string Check(HumanCommand command)
            {
                if (command == null) return "UnknownCommand";
                var scratch = new HumanCommandProcessor(_map, _human, _turn) { PauseSettings = PauseSettings };
                switch (command.type)
                {
                    case "queueResearch":
                        return scratch.ValidateQueueResearch(command, out _);
                    case "queueProduction":
                    case "colonize":
                    case "shipResource":
                    case "move":
                        scratch._dryRun = true;
                        try
                        {
                            return scratch.Dispatch(command, new List<GameAI.GameAIOrder>(), out _);
                        }
                        catch (Exception e)
                        {
                            return "Error: " + e.Message;
                        }
                    default:
                        return "CheckNotSupported";
                }
            }

            /// <summary>
            /// Checks a whole batch the way Apply would run it, claims included (two shipments cannot together exceed a stock), without
            /// applying any of it. Covers queueProduction, colonize and shipResource; any other command gets "CheckNotSupported". Lets a
            /// dialog refuse a batch up front so none of it is sent when part of it would be refused.
            /// </summary>
            public List<HumanCommandResult> CheckBatch(IEnumerable<HumanCommand> commands)
            {
                var scratch = new HumanCommandProcessor(_map, _human, _turn) { PauseSettings = PauseSettings, _dryRun = true };
                var scratchOrders = new List<GameAI.GameAIOrder>();
                var results = new List<HumanCommandResult>();
                foreach (var command in commands)
                {
                    var supported = command != null
                        && (command.type == "queueProduction" || command.type == "colonize" || command.type == "shipResource" || command.type == "move");
                    results.Add(supported
                        ? scratch.Apply(new[] { command }, scratchOrders)[0]
                        : new HumanCommandResult { type = command?.type ?? "", applied = false, reason = "CheckNotSupported", detail = "" });
                }
                return results;
            }

            /// <summary>Parses a commands file body. Never throws: bad input gives an empty list and a message in error.</summary>
            public static List<HumanCommand> Parse(string json, out string error)
            {
                error = null;
                if (string.IsNullOrWhiteSpace(json))
                {
                    error = "Empty";
                    return new List<HumanCommand>();
                }
                try
                {
                    var batch = JsonUtility.FromJson<HumanCommandBatch>(json);
                    if (batch == null || batch.commands == null) return new List<HumanCommand>();
                    return batch.commands;
                }
                catch (Exception e)
                {
                    error = e.Message;
                    return new List<HumanCommand>();
                }
            }

            /// <summary>Parses and applies a commands file body. A file that does not parse gives one ParseError result.</summary>
            public List<HumanCommandResult> ApplyJson(string json, List<GameAI.GameAIOrder> ordersOut)
            {
                var commands = Parse(json, out var error);
                if (error != null)
                    return new List<HumanCommandResult>
                    {
                        new HumanCommandResult { type = "", applied = false, reason = "ParseError: " + error, detail = "" }
                    };
                return Apply(commands, ordersOut);
            }

            /// <summary>
            /// Applies the commands in order against the current state, appending the orders they produce to ordersOut. Commands in one
            /// batch see each other's claims (ships already sent, queue entries already added), so two moves of the same ships cannot
            /// both succeed.
            /// </summary>
            public List<HumanCommandResult> Apply(IEnumerable<HumanCommand> commands, List<GameAI.GameAIOrder> ordersOut)
            {
                var results = new List<HumanCommandResult>();
                foreach (var command in commands)
                {
                    var result = new HumanCommandResult { type = command?.type ?? "", applied = false, reason = "", detail = "" };
                    try
                    {
                        result.reason = command == null ? "UnknownCommand" : Dispatch(command, ordersOut, out result.detail) ?? "";
                    }
                    catch (Exception e)
                    {
                        // A rule should refuse before this point; this keeps one bad command from losing the rest of the batch.
                        result.reason = "Error: " + e.Message;
                    }
                    result.applied = result.reason.Length == 0;
                    results.Add(result);
                }
                return results;
            }

            // The refusal reason, or null when the command was applied. detail is what to log for an applied command.
            private string Dispatch(HumanCommand command, List<GameAI.GameAIOrder> orders, out string detail)
            {
                detail = "";
                switch (command.type)
                {
                    case "move":
                        return HandleMove(command, orders, out detail);
                    case "setFleet":
                        return HandleSetFleet(command, out detail);
                    case "mergeFleets":
                        return HandleMergeFleets(command, out detail);
                    case "colonize":
                        return HandleColonize(command, orders, out detail);
                    case "shipResource":
                        return HandleShipResource(command, orders, out detail);
                    case "queueProduction":
                        return HandleQueueProduction(command, orders, out detail);
                    case "removeProduction":
                        return HandleRemoveProduction(command, out detail);
                    case "moveProduction":
                        return HandleMoveProduction(command, out detail);
                    case "setDevelopment":
                        return HandleSetDevelopment(command, out detail);
                    case "declareWar":
                    case "makePeace":
                    case "surrender":
                        return HandleStance(command, orders, out detail);
                    case "setPause":
                        return HandleSetPause(command, out detail);
                    case "setShipments":
                        _human.ShipmentsAutomated = command.flag;
                        detail = command.flag ? "automatic" : "manual";
                        return null;
                    case "queueResearch":
                        return HandleQueueResearch(command, out detail);
                    case "removeResearch":
                        return HandleRemoveResearch(command, out detail);
                    case "moveResearch":
                        return HandleMoveResearch(command, out detail);
                    default:
                        return "UnknownCommand";
                }
            }

            // ── Fleets ───────────────────────────────────────────────────────

            // Ships already sent by an earlier command of this batch, per (planet, kind) in the order they were claimed. Nothing is undocked
            // until the orders execute, so a later command must skip them (the claimedByOrigin pattern of PlayerAI.ProcessShipActions).
            private readonly Dictionary<(string planet, Ship.ShipKind kind), List<Ship>> _claimed
                = new Dictionary<(string planet, Ship.ShipKind kind), List<Ship>>();

            private List<Ship> ClaimedAt(string planet, Ship.ShipKind kind)
            {
                if (!_claimed.TryGetValue((planet, kind), out var list))
                    _claimed[(planet, kind)] = list = new List<Ship>();
                return list;
            }

            private bool IsClaimed(Ship ship)
                => _claimed.Values.Any(list => list.Contains(ship));

            // The dock list of each origin as it stood the first time a command of this batch used it. PrioritizeShips reorders the real
            // list, so a ship index must be resolved against this copy: it names the ship the human saw at that index.
            private readonly Dictionary<string, List<Ship>> _dockAtBatchStart = new Dictionary<string, List<Ship>>();

            private List<Ship> DockAtBatchStart(Planet planet)
            {
                if (!_dockAtBatchStart.TryGetValue(planet.PlanetName, out var dock))
                    _dockAtBatchStart[planet.PlanetName] = dock = new List<Ship>(planet.DockedShips);
                return dock;
            }

            // Bunkers do not move (speed 0), so only these kinds have a name a command can use.
            private static bool TryParseKind(string name, out Ship.ShipKind kind)
            {
                switch ((name ?? "").ToLowerInvariant())
                {
                    case "warship": kind = Ship.ShipKind.WarShip; return true;
                    case "siegeship": kind = Ship.ShipKind.SiegeShip; return true;
                    case "colonyship": kind = Ship.ShipKind.ColonyShip; return true;
                    default: kind = Ship.ShipKind.WarShip; return false;
                }
            }

            private static bool IsMovable(Ship.ShipKind kind) => kind != Ship.ShipKind.Bunker;

            // Source of the arrival group ids of GameAIOrder.ArrivalGroup (unique within a session; a dry-run Check only burns numbers).
            private static int _nextArrivalGroup;

            // Set on the scratch processors of Check / CheckBatch: a move is validated and its claims recorded, but the real dock list is not reordered.
            private bool _dryRun;

            // setFleet: origin, ships (dock indices), index = the fleet id to put them in (it must exist), or negative for a new fleet. Instant, no orders.
            private string HandleSetFleet(HumanCommand command, out string detail)
            {
                detail = "";
                var origin = PlanetNamed(command.origin);
                if (origin == null) return "UnknownPlanet";
                if (!IsHumanPlanet(origin)) return "NotYourPlanet";
                if (command.ships == null || command.ships.Count == 0) return "BadShipIndex";

                var dock = DockAtBatchStart(origin);
                var chosen = new List<Ship>();
                foreach (var index in command.ships)
                {
                    if (index < 0 || index >= dock.Count) return "BadShipIndex";
                    var ship = dock[index];
                    if (ship.Owner != HumanId) return "NotYourShips";
                    if (!IsMovable(ship.Kind)) return "UnknownKind";
                    if (IsClaimed(ship) || chosen.Contains(ship)) return "NotEnoughShips";
                    chosen.Add(ship);
                }
                if (command.index >= 0 && !HasFleet(origin, command.index)) return "UnknownFleet";

                var fleetId = command.index >= 0 ? command.index : origin.NextFleetId(HumanId);
                foreach (var ship in chosen) ship.FleetId = fleetId;
                detail = $"{origin.PlanetName} {chosen.Count} ships -> fleet {fleetId}";
                return null;
            }

            // mergeFleets: origin, index = the fleet to merge from, toIndex = the fleet it joins. Both must exist and differ. Instant, no orders.
            private string HandleMergeFleets(HumanCommand command, out string detail)
            {
                detail = "";
                var origin = PlanetNamed(command.origin);
                if (origin == null) return "UnknownPlanet";
                if (!IsHumanPlanet(origin)) return "NotYourPlanet";
                if (command.index == command.toIndex) return "SameFleet";
                if (!HasFleet(origin, command.index) || !HasFleet(origin, command.toIndex)) return "UnknownFleet";

                var merged = 0;
                foreach (var ship in origin.DockedShips)
                {
                    if (ship.Owner != HumanId || !IsMovable(ship.Kind) || ship.FleetId != command.index) continue;
                    ship.FleetId = command.toIndex;
                    merged++;
                }
                detail = $"{origin.PlanetName} fleet {command.index} -> fleet {command.toIndex} ({merged} ships)";
                return null;
            }

            private bool HasFleet(Planet planet, int fleetId)
                => planet.DockedShips.Exists(s => s.Owner == HumanId && IsMovable(s.Kind) && s.FleetId == fleetId);

            // ── Turn flow ────────────────────────────────────────────────────

            // setPause: eventName (a HumanEventKind name, any case), flag (true = the timer stops on it).
            private string HandleSetPause(HumanCommand command, out string detail)
            {
                detail = "";
                if (PauseSettings == null) return "NoPauseSettings";
                if (!Enum.TryParse<HumanEventKind>(command.eventName ?? "", true, out var kind)
                    || !Enum.IsDefined(typeof(HumanEventKind), kind))
                    return "UnknownEvent";
                if (!HumanPauseSettings.CanPause(kind)) return "NotAPauseEvent";
                PauseSettings.Set(kind, command.flag);
                detail = $"{kind} {(command.flag ? "on" : "off")}";
                return null;
            }

            // ── Diplomacy ────────────────────────────────────────────────────

            // declareWar / makePeace / surrender: rival (a player id). The same immediate stance orders the AI emits (data = the rival,
            // no planets, executed through GameAI.ApplyStanceOrder / ApplySurrender), so they have the same effects, truce included.
            private string HandleStance(HumanCommand command, List<GameAI.GameAIOrder> orders, out string detail)
            {
                detail = "";
                var diplomacy = _map.Diplomacy;
                if (!diplomacy.Enabled) return "DiplomacyOff";
                var rival = command.rival;
                if (rival < 0 || rival == HumanId) return "UnknownRival";
                var hasContact = _map.Knowledge.ContactPlayers(_map, HumanId).Contains(rival);

                GameAI.GameAIOrder.OrderType type;
                switch (command.type)
                {
                    case "declareWar":
                        if (diplomacy.StanceToward(HumanId, rival) == Stance.War) return "AlreadyAtWar";
                        if (diplomacy.InTruce(HumanId, rival, _turn)) return "Truce";
                        if (!hasContact) return "NoContact";
                        type = GameAI.GameAIOrder.OrderType.OrderTypeDeclareWar;
                        break;
                    case "makePeace":
                        if (diplomacy.StanceToward(HumanId, rival) != Stance.War) return "AlreadyPeace";
                        type = GameAI.GameAIOrder.OrderType.OrderTypeMakePeace;
                        break;
                    default:   // surrender
                        if (!diplomacy.IsAtWar(HumanId, rival, hasContact)) return "NotAtWar";
                        type = GameAI.GameAIOrder.OrderType.OrderTypeSurrender;
                        break;
                }
                orders.Add(new GameAI.GameAIOrder
                {
                    Type = type,
                    TimingType = GameAI.GameAIOrder.OrderTimingType.OrderTimingTypeImmediate,
                    TimingDelay = 0,
                    TotalDelay = 0,
                    Data = rival,
                    Origin = string.Empty,
                    Target = string.Empty,
                    PlayerId = HumanId,
                });
                detail = $"{command.type} {rival}";
                return null;
            }

            // ── Research ─────────────────────────────────────────────────────

            // queueResearch: item. Appended to the player-wide queue; an item whose prerequisite is neither researched nor queued earlier
            // would never be startable, so it is refused.
            // The refusal for a queueResearch command, or null when it would be accepted. Changes nothing, so Check can use it.
            private string ValidateQueueResearch(HumanCommand command, out CatalogItem item)
            {
                item = string.IsNullOrEmpty(command.item) ? null : _human.ResearchCatalog.GetItem(command.item);
                if (item == null) return "UnknownItem";
                if (item.researched) return "AlreadyResearched";
                if (_human.ResearchQueue.Contains(item.itemName) || _human.currentResearch == item) return "AlreadyQueued";
                if (!string.IsNullOrEmpty(item.requiredTech))
                {
                    var prerequisite = _human.ResearchCatalog.GetItem(item.requiredTech);
                    if (prerequisite != null && !prerequisite.researched
                        && _human.currentResearch != prerequisite && !_human.ResearchQueue.Contains(item.requiredTech))
                        return "PrerequisiteMissing";
                }
                return null;
            }

            private string HandleQueueResearch(HumanCommand command, out string detail)
            {
                detail = "";
                var refusal = ValidateQueueResearch(command, out var item);
                if (refusal != null) return refusal;
                _human.ResearchQueue.Add(item.itemName);
                detail = "+" + item.itemName;
                return null;
            }

            // moveResearch: index to toIndex within the waiting queue. An item may not come before the queued item it requires, and a
            // prerequisite may not go below a queued item that needs it (PrerequisiteOrder); the research in progress is not part of it.
            private string HandleMoveResearch(HumanCommand command, out string detail)
            {
                detail = "";
                var count = _human.ResearchQueue.Count;
                if (command.index < 0 || command.index >= count || command.toIndex < 0 || command.toIndex >= count) return "BadIndex";
                if (command.index == command.toIndex) return null;
                if (!HumanResearchTree.MoveKeepsPrerequisites(_human.ResearchQueue, n => _human.ResearchCatalog.GetItem(n)?.requiredTech, command.index, command.toIndex))
                    return "PrerequisiteOrder";
                var name = _human.ResearchQueue[command.index];
                _human.ResearchQueue.RemoveAt(command.index);
                _human.ResearchQueue.Insert(command.toIndex, name);
                detail = $"{name} {command.index}->{command.toIndex}";
                return null;
            }

            // removeResearch: index (into the waiting queue, not the research in progress).
            private string HandleRemoveResearch(HumanCommand command, out string detail)
            {
                detail = "";
                if (command.index < 0 || command.index >= _human.ResearchQueue.Count) return "BadIndex";
                // Whatever in the queue needs a removed item (directly or through another) goes with it: it could never be started.
                var removed = new List<string> { _human.ResearchQueue[command.index] };
                bool grew;
                do
                {
                    grew = false;
                    foreach (var name in _human.ResearchQueue)
                    {
                        if (removed.Contains(name)) continue;
                        var item = _human.ResearchCatalog.GetItem(name);
                        if (item != null && !string.IsNullOrEmpty(item.requiredTech) && removed.Contains(item.requiredTech))
                        {
                            removed.Add(name);
                            grew = true;
                        }
                    }
                } while (grew);
                _human.ResearchQueue.RemoveAll(removed.Contains);
                detail = "-" + string.Join(", -", removed);
                return null;
            }

            // ── Production ───────────────────────────────────────────────────

            private Planet OwnedPlanet(string name, out string refusal)
            {
                var planet = PlanetNamed(name);
                refusal = planet == null ? "UnknownPlanet" : planet.Owner != HumanId ? "NotYourPlanet" : null;
                return refusal == null ? planet : null;
            }

            // queueProduction: origin (an owned planet), item. The same immediate set-production order the AI emits, which appends the item
            // to the planet's production queue (and starts it when nothing is in production), so the fixed cost is stamped the AI's way.
            private string HandleQueueProduction(HumanCommand command, List<GameAI.GameAIOrder> orders, out string detail)
            {
                detail = "";
                var planet = OwnedPlanet(command.origin, out var refusal);
                if (planet == null) return refusal;
                var item = string.IsNullOrEmpty(command.item) ? null : _human.ProductionCatalog.catalogItems.Find(x => x.itemName == command.item);
                if (item == null) return "UnknownItem";
                if (!item.researched) return "NotUnlocked";
                if (planet.CompletedImprovements.Exists(c => c.Item1 == item.itemName)) return "AlreadyBuilt";
                if (planet.IsImprovementSuperseded(item)) return "Superseded";
                if (item.subType == "Bunker" && !planet.CanBuildBunker()) return "BunkerCap";

                orders.Add(new GameAI.GameAIOrder
                {
                    Type = GameAI.GameAIOrder.OrderType.OrderTypeIndustrySetProduction,
                    TimingType = GameAI.GameAIOrder.OrderTimingType.OrderTimingTypeImmediate,
                    TimingDelay = 0,
                    TotalDelay = 0,
                    Data = item.itemName,
                    Origin = planet.PlanetName,
                    Target = planet.PlanetName,
                    PlayerId = HumanId,
                });
                detail = $"{planet.PlanetName} +{item.itemName}";
                return null;
            }

            // removeProduction: origin, index (into the waiting queue, not the item in production).
            private string HandleRemoveProduction(HumanCommand command, out string detail)
            {
                detail = "";
                var planet = OwnedPlanet(command.origin, out var refusal);
                if (planet == null) return refusal;
                if (command.index < 0 || command.index >= planet.ProductionQueue.Count) return "BadIndex";
                detail = $"{planet.PlanetName} -{planet.ProductionQueue[command.index].Item.itemName}";
                planet.ProductionQueue.RemoveAt(command.index);
                return null;
            }

            // moveProduction: origin, index, toIndex (both into the waiting queue).
            private string HandleMoveProduction(HumanCommand command, out string detail)
            {
                detail = "";
                var planet = OwnedPlanet(command.origin, out var refusal);
                if (planet == null) return refusal;
                var count = planet.ProductionQueue.Count;
                // The first queued item moved up (toIndex -1) takes the place of the current production, which goes to the head of the queue
                // with the progress it made (Planet.UpdateProductionQueue adds the stored industry to it when it resumes).
                if (command.index == 0 && command.toIndex == -1)
                {
                    if (count == 0 || !planet.CurrentProduction.HasValue) return "BadIndex";
                    var promoted = planet.ProductionQueue[0];
                    var displaced = planet.CurrentProduction.Value;
                    planet.ProductionQueue[0] = displaced;
                    planet.CurrentProduction = promoted;
                    detail = $"{planet.PlanetName} {promoted.Item.itemName} replaces {displaced.Item.itemName} ({displaced.Progress:0.#} kept)";
                    return null;
                }
                if (command.index < 0 || command.index >= count || command.toIndex < 0 || command.toIndex >= count) return "BadIndex";
                var entry = planet.ProductionQueue[command.index];
                planet.ProductionQueue.RemoveAt(command.index);
                planet.ProductionQueue.Insert(command.toIndex, entry);
                detail = $"{planet.PlanetName} {entry.Item.itemName} {command.index}->{command.toIndex}";
                return null;
            }

            // setDevelopment: origin, flag (true = automatic: the AI chooses what the planet builds; false = the human's queue).
            private string HandleSetDevelopment(HumanCommand command, out string detail)
            {
                detail = "";
                var planet = OwnedPlanet(command.origin, out var refusal);
                if (planet == null) return refusal;
                planet.ManualDevelopment = !command.flag;
                detail = $"{planet.PlanetName} {(command.flag ? "automatic" : "manual")}";
                return null;
            }

            // Colonists and food rider food already committed per origin by earlier colonize commands of this batch.
            private readonly Dictionary<string, int> _colonistsTaken = new Dictionary<string, int>();
            private readonly Dictionary<string, float> _foodSpent = new Dictionary<string, float>();

            // shipResource: origin (populated by the human), target, kind ("food" or "grotsits"), count (the amount). The AI's three shipment
            // orders (delayed transport with its route, deduction at the origin, "shipment incoming" flag at the target), for when the
            // shipments switch is off and the human orders them. Earlier shipments of the batch count against the origin's stock.
            private readonly Dictionary<(string planet, bool food), float> _shipped = new Dictionary<(string planet, bool food), float>();

            private string HandleShipResource(HumanCommand command, List<GameAI.GameAIOrder> orders, out string detail)
            {
                detail = "";
                var origin = PlanetNamed(command.origin);
                var target = PlanetNamed(command.target);
                if (origin == null || target == null) return "UnknownPlanet";
                if (!origin.Population.Exists(p => p.Player == HumanId)) return "NotYourPlanet";
                if (origin == target) return "SameDestination";
                var kind = (command.kind ?? "").ToLowerInvariant();
                if (kind != "food" && kind != "grotsits") return "UnknownKind";
                var isFood = kind == "food";
                if (command.count <= 0) return "BadCount";
                if (!_map.Knowledge.IsKnown(HumanId, target.PlanetName)) return "TargetUnknown";

                var route = RoutePlanner.ShortestRoute(_map, origin.PlanetName, target.PlanetName,
                    _map.GameAIConstants.maxPathNodesForResourceDistribution);
                if (route == null) return "OutOfRange";   // out of range, unknown, or FindPath's no-route stub

                _shipped.TryGetValue((origin.PlanetName, isFood), out var alreadySent);
                if ((isFood ? origin.Food : origin.Grotsits) - alreadySent < command.count) return "NotEnoughStock";

                var amount = (float)command.count;
                var surplus = new Planet.UpdateResult(origin.PlanetName,
                    isFood ? Planet.UpdateResult.UpdateResultType.UpdateResultTypeFoodSurplus : Planet.UpdateResult.UpdateResultType.UpdateResultTypeGrotsitsSurplus,
                    amount, HumanId);
                var shortage = new Planet.UpdateResult(target.PlanetName,
                    isFood ? Planet.UpdateResult.UpdateResultType.UpdateResultTypeFoodShortage : Planet.UpdateResult.UpdateResultType.UpdateResultTypeGrotsitsShortage,
                    -amount, HumanId);
                var action = new ResourceAction
                {
                    ChosenChoiceElement = new ResourceChoiceElement
                    {
                        SurplusResult = surplus,
                        ShortageResult = shortage,
                        // At least one turn of travel: a Delayed order with TimingDelay <= 0 is both queued and executed at once.
                        Cost = Math.Max(route.Cost, _map.GameAIConstants.defaultTravelSpeed),
                        Route = new List<string>(route.Nodes),
                    },
                };
                _human.EmitResourceOrders(action, amount,
                    isFood ? GameAI.GameAIOrder.OrderType.OrderTypeFoodTransport : GameAI.GameAIOrder.OrderType.OrderTypeGrotsitsTransport,
                    isFood ? GameAI.GameAIOrder.OrderType.OrderTypeFoodChange : GameAI.GameAIOrder.OrderType.OrderTypeGrotsitsChange,
                    isFood ? GameAI.GameAIOrder.OrderType.OrderTypeFoodTransportInProgress : GameAI.GameAIOrder.OrderType.OrderTypeGrotsitsTransportInProgress,
                    orders);
                _shipped[(origin.PlanetName, isFood)] = alreadySent + amount;
                detail = $"{origin.PlanetName}->{target.PlanetName} {command.count} {kind}";
                return null;
            }

            // colonize: origin (holds a colony ship), target. The AI's colonization launch, chosen by the human: a colonist and, when the
            // target cannot feed one, the food rider, under the AI's own viability rules (known and empty or room, an origin that can pay
            // the rider, a route that avoids visible blockades). The colony ship is consumed at the origin.
            private string HandleColonize(HumanCommand command, List<GameAI.GameAIOrder> orders, out string detail)
            {
                detail = "";
                var origin = PlanetNamed(command.origin);
                var target = PlanetNamed(command.target);
                if (origin == null || target == null) return "UnknownPlanet";
                if (!IsHumanPlanet(origin)) return "NotYourPlanet";
                if (origin == target) return "SameDestination";

                var claimed = ClaimedAt(origin.PlanetName, Ship.ShipKind.ColonyShip);
                var ship = origin.DockedShips.Find(s => s.Kind == Ship.ShipKind.ColonyShip && s.Owner == HumanId && !IsClaimed(s));
                if (ship == null) return "NoColonyShip";
                // The colonist is taken from the origin's people; the last inhabitant would leave the planet dead. Earlier commands of
                // this batch have already taken theirs.
                _colonistsTaken.TryGetValue(origin.PlanetName, out var colonistsTaken);
                if (origin.Population.Count(p => p.Player == HumanId) - colonistsTaken < 2) return "OriginTooSmall";

                var maxNodes = _map.GameAIConstants.maxPathNodesForColonization;
                if (!origin.DistanceMapToPathingList.TryGetValue(target.PlanetName, out var path)
                    || path.NumNodes < 2 || path.NumNodes > maxNodes)
                    return "OutOfRange";
                if (!_human.IsValidColonizationTarget(target)) return "TargetNotValid";
                // The origin pays the food rider of a target that cannot feed a colonist, out of what earlier commands of the batch left.
                _foodSpent.TryGetValue(origin.PlanetName, out var foodSpent);
                var rider = _map.GameAIConstants.colonyFoodRider;
                if (target.NeedsColonyFoodRider && origin.Food - foodSpent < rider) return "CannotPayRider";
                var route = RoutePlanner.PlanRoute(_map, origin.PlanetName, target.PlanetName, _human.CurrentBlockadeView, maxNodes);
                if (route == null) return "NoRoute";

                // At least one turn: a Delayed order with TimingDelay <= 0 is both queued and executed by ProcessNewOrders.
                var delay = Math.Max(1, Convert.ToInt32(route.Cost / _map.GameAIConstants.defaultTravelSpeed));
                _human.EmitColonistOrders(origin.PlanetName, target.PlanetName, 1, delay, route, 0f, orders);
                claimed.Add(ship);
                _colonistsTaken[origin.PlanetName] = colonistsTaken + 1;
                if (target.NeedsColonyFoodRider) _foodSpent[origin.PlanetName] = foodSpent + rider;
                detail = $"{origin.PlanetName}->{target.PlanetName}" + (target.NeedsColonyFoodRider ? " with food rider" : "");
                return null;
            }

            // move: origin, target, and either ships (dock indices at the origin) or kind + count (the healthiest ships of the kind first).
            // Merging is just moving to a planet where the human already has ships; splitting is sending fewer than all.
            private string HandleMove(HumanCommand command, List<GameAI.GameAIOrder> orders, out string detail)
            {
                detail = "";
                var origin = PlanetNamed(command.origin);
                var target = PlanetNamed(command.target);
                if (origin == null || target == null) return "UnknownPlanet";
                if (!IsHumanPlanet(origin)) return "NotYourPlanet";
                if (origin == target) return "SameDestination";
                if (!_map.Knowledge.IsKnown(HumanId, target.PlanetName)) return "TargetUnknown";
                if (!origin.DistanceMapToPathingList.TryGetValue(target.PlanetName, out var path)
                    || path.NumNodes < 2 || path.NumNodes > _map.GameAIConstants.maxPathNodesForShipTransport)
                    return "OutOfRange";

                var chosen = new List<Ship>();
                if (command.ships != null && command.ships.Count > 0)
                {
                    var dock = DockAtBatchStart(origin);
                    foreach (var index in command.ships)
                    {
                        if (index < 0 || index >= dock.Count) return "BadShipIndex";
                        var ship = dock[index];
                        if (ship.Owner != HumanId) return "NotYourShips";
                        if (!IsMovable(ship.Kind)) return "UnknownKind";
                        if (IsClaimed(ship) || chosen.Contains(ship)) return "NotEnoughShips";
                        chosen.Add(ship);
                    }
                }
                else
                {
                    if (string.IsNullOrEmpty(command.kind) || command.count <= 0) return "BadCount";
                    if (!TryParseKind(command.kind, out var kind)) return "UnknownKind";
                    var available = origin.DockedShips
                        .Where(s => s.Kind == kind && s.Owner == HumanId && !IsClaimed(s))
                        .OrderBy(s => s.Damage)   // stable: equally healthy ships keep their dock order
                        .ToList();
                    if (available.Count < command.count) return "NotEnoughShips";
                    chosen = available.Take(command.count).ToList();
                }

                var firstNewOrder = orders.Count;
                foreach (var group in chosen.GroupBy(s => s.Kind).OrderBy(g => g.Key))
                {
                    var claimed = ClaimedAt(origin.PlanetName, group.Key);
                    var skip = claimed.Count;
                    // A dry run (Check) must leave the dock list alone; the order it emits is thrown away.
                    if (!_dryRun) origin.PrioritizeShips(group.Key, HumanId, claimed.Concat(group));
                    _human.EmitShipOrders(new ShipAction
                    {
                        Origin = origin.PlanetName,
                        Target = target.PlanetName,
                        Cost = path.Cost,
                        Count = group.Count(),
                        Kind = group.Key,
                    }, orders, skip);
                    claimed.AddRange(group);
                }
                // A fleet of several kinds leaves as one order per kind; one arrival group makes them land as one fleet.
                var arrivalGroup = ++_nextArrivalGroup;
                for (var i = firstNewOrder; i < orders.Count; i++)
                    if (orders[i].Type == GameAI.GameAIOrder.OrderType.OrderTypeShipTransport) orders[i].ArrivalGroup = arrivalGroup;
                detail = $"{origin.PlanetName}->{target.PlanetName} {chosen.Count} ships";
                return null;
            }

            // ── Shared helpers for the handlers ──────────────────────────────

            private Planet PlanetNamed(string name) => string.IsNullOrEmpty(name) ? null : _map.GetPlanet(name);

            /// <summary>True when the human populates the planet or has a ship docked there.</summary>
            private bool IsHumanPlanet(Planet planet)
                => planet.Population.Exists(p => p.Player == HumanId)
                   || planet.DockedShips.Exists(s => s.Owner == HumanId);
        }
    }
}
