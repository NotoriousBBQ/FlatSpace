using System;
using System.Collections.Generic;
using System.Linq;
using FlatSpace.Game;
using Unity.VisualScripting;
using UnityEngine;
using Random = System.Random;

namespace FlatSpace
{
    namespace AI
    {
        public class GameAI : MonoBehaviour
        {
            [Serializable]
            public class GameAIOrder
            {
                public enum OrderType
                {
                    OrderTypeNone,
                    OrderTypePopulationTransport,
                    OrderTypePopulationChange,
                    OrderTypePopulationTransferInProgress,
                    OrderTypeFoodTransport,
                    OrderTypeFoodChange,
                    OrderTypeFoodTransportInProgress,
                    OrderTypeGrotsitsTransport,
                    OrderTypeGrotsitsChange,
                    OrderTypeGrotsitsTransportInProgress,
                    OrderTypeResearchChange,
                    OrderTypeResearchSet,
                    OrderTypeIndustryChange,
                    OrderTypeIndustrySetProduction,
                    OrderTypeIndustryTransport,
                    OrderTypeRemoveShip,
                    OrderTypeShipTransport,
                    OrderTypeShipDeparture,
                    OrderTypeShipTransferInProgress,
                    // Appended last: OrderType serializes as an int. Delayed; Data is the food a colony ship
                    // carries, added to the target planet when the colonist lands.
                    OrderTypeColonyFoodRider
                }

                public enum OrderTimingType
                {
                    OrderTimingTypeDelayed,
                    OrderTimingTypeImmediate,
                    OrderTimingTypeHold,
                }

                public OrderType Type;
                public OrderTimingType TimingType;
                public int TimingDelay;
                public int TotalDelay;
                public object Data;
                public string Target;
                public string Origin;
                public int PlayerId;

                // Ships carried by a ship order. Not serialized by Unity; saves go through GameSave.ShipSave.
                [NonSerialized] public ShipFleetPayload Fleet;

                // The planned route of a colonist order (origin to target inclusive), so blockade is applied along the route
                // actually flown, not the shortest path. Not serialized by Unity; saves go through OrderSave.route. Null
                // (no route recorded) means "use the shortest path".
                [NonSerialized] public List<string> Route;

                /// <summary>A saved route of fewer than 2 nodes (older saves, non-route orders) restores as none.</summary>
                public static List<string> RouteFromSave(List<string> saved)
                    => saved != null && saved.Count >= 2 ? new List<string>(saved) : null;

                /// <summary>What a fleet in flight is: the ship kind and one research snapshot per ship.</summary>
                public class ShipFleetPayload
                {
                    public Ship.ShipKind Kind = Ship.ShipKind.WarShip;
                    public List<List<string>> Snapshots = new List<List<string>>();

                    public List<SaveLoadSystem.GameSave.ShipSave> ToSave(int owner)
                    {
                        return Snapshots.Select(snapshot => new SaveLoadSystem.GameSave.ShipSave
                        {
                            kind = Kind,
                            owner = owner,
                            researchSnapshot = new List<string>(snapshot),
                        }).ToList();
                    }

                    /// <summary>Null or empty (older saves, non-ship orders) yields no payload.</summary>
                    public static ShipFleetPayload FromSave(List<SaveLoadSystem.GameSave.ShipSave> ships)
                    {
                        if (ships == null || ships.Count == 0) return null;
                        return new ShipFleetPayload
                        {
                            Kind = ships[0].kind,
                            Snapshots = ships
                                .Select(s => new List<string>(s.researchSnapshot ?? new List<string>()))
                                .ToList(),
                        };
                    }
                }
            }
            public GameAIMap GameAIMap { get; private set; }
            public List<GameAIOrder> CurrentAIOrders { get; private set; } = new List<GameAIOrder>();
            public static readonly Random Rand = new Random();

            public void InitGameAI(List<PlanetSpawnData> spawnDataList, GameAIConstants gameAIConstants)
            {
                GameAIMap = this.AddComponent<GameAIMap>() as GameAIMap;
                GameAIMap.GameAIMapInit(spawnDataList, gameAIConstants);
            }

            // Which planets are short of grotsits right now, so GrotsitsShort is logged on change only (log-only state).
            private readonly GrotsitsShortTracker _grotsitsShort = new GrotsitsShortTracker();

            public void ClearGameAI()
            {
                CurrentAIOrders.Clear();
                _grotsitsShort.Clear();

            }

            public void GameAIUpdate()
            {
                var gameAIOrders = new List<GameAIOrder>();
                var planetUpdateResults = new List<Planet.PlanetUpdateResult>();
                ProcessCurrentOrders();
                planetUpdateResults.Clear();
                UpdateAllPlanets(planetUpdateResults);
                AITuningLogger.LogPlanetEvents(Gameboard.Instance.TurnNumber, planetUpdateResults);
                LogGrotsitsShortChanges(Gameboard.Instance.TurnNumber);
                LogEconomySummary(Gameboard.Instance.TurnNumber, Gameboard.Instance.players.Count);
                GameAIMap.Knowledge.Update(GameAIMap, Gameboard.Instance.players.Count,
                    GameAIMap.GameAIConstants.maxPathNodesForKnowledge);
                // The AI sizes blockade-breaking fleets against offense already in flight (see AssaultPlanner).
                GameAIMap.RecomputeIncomingOffense(CurrentAIOrders,
                    new WarshipStats(BlockadeSystem.ResearchItemsFrom(Gameboard.Instance.players)));
                ProcessResults(planetUpdateResults, gameAIOrders);
                Gameboard.Instance.CreateNotificationsForNewOrders(gameAIOrders);
                AITuningLogger.LogNewOrders(Gameboard.Instance.TurnNumber, gameAIOrders);
                ProcessNewOrders(gameAIOrders);
            }

            // One GrotsitsShort line when a populated planet becomes short of grotsits (Start) or recovers or empties (End), with
            // the numbers that explain it, so a log says which planets tip a player into the shortage and morale loop.
            private void LogGrotsitsShortChanges(int turnNumber)
            {
                var populated = GameAIMap.PlanetList.Where(p => p.Population.Count > 0)
                    .Select(p => (p.PlanetName, p.GrotsitsShort));
                foreach (var change in _grotsitsShort.Update(populated))
                {
                    var planet = GameAIMap.GetPlanet(change.Planet);
                    if (planet == null) continue;
                    AITuningLogger.LogGrotsitsShort(turnNumber, planet.Owner, planet.PlanetName, change.Started,
                        planet.Population.Count, planet.GetGrotsitsCapacity(), planet.GetImprovementMaintenanceCost(),
                        planet.Morale);
                }
            }

            // Every 25 turns, one line per player: how many planets it owns, how many were short of grotsits,
            // their mean morale, and the total improvement upkeep, so upkeep and grotsits shipping can be tuned from logs.
            // Beside it, how many chokepoints it colonizes and how many of its warships sit on them (ChokepointGarrison).
            private void LogEconomySummary(int turnNumber, int playerCount)
            {
                if (turnNumber % 25 != 0) return;
                for (var player = 0; player < playerCount; player++)
                {
                    var owned = GameAIMap.PlanetList.FindAll(p => p.Owner == player && p.Population.Count > 0);
                    if (owned.Count == 0) continue;
                    AITuningLogger.LogEconomy(turnNumber, player, owned.Count,
                        owned.Count(p => p.GrotsitsShort), owned.Average(p => p.Morale),
                        owned.Sum(p => p.GetImprovementMaintenanceCost()));
                    var chokepoints = GameAIMap.ChokepointSummary(player);
                    AITuningLogger.LogChokepointGarrison(turnNumber, player, chokepoints.colonized, chokepoints.boardTotal,
                        chokepoints.shipsOnThem, chokepoints.allShips);
                }
            }

            private void ProcessCurrentOrders()
            {
                foreach (var gameAIOrder in CurrentAIOrders)
                {
                    gameAIOrder.TimingDelay--;
                }

                ApplyBlockades();

                var executableOrders = CurrentAIOrders.FindAll(x => x.TimingDelay <= 0);
                Gameboard.Instance.CreateNotificationsForExecutingOrders(executableOrders);
                AITuningLogger.LogExecutingOrders(Gameboard.Instance.TurnNumber, executableOrders);
                foreach (var executableOrder in executableOrders)
                {
                    ExecuteOrder(executableOrder);
                }

                CurrentAIOrders.RemoveAll(x => x.TimingDelay <= 0);
            }

            // Every player's research catalog holds the same items, so any player's supplies the stat lines.
            private void ApplyBlockades()
            {
                // Null on the first turn of a repeat run: the new Players exist but their PlayerAI is built in
                // Player.Start next frame. No orders are in flight then (ClearGameAI emptied them), so skipping is safe.
                var research = BlockadeSystem.ResearchItemsFrom(Gameboard.Instance.players);
                if (research == null) return;
                var turn = Gameboard.Instance.TurnNumber;
                var cuts = new BlockadeSystem(GameAIMap, research).Apply(CurrentAIOrders, turn);

                // The order owner learns where its orders were cut, so colonization can route around it later.
                foreach (var cut in cuts)
                {
                    if (cut.PlayerId < 0 || cut.PlayerId >= Gameboard.Instance.players.Count) continue;
                    var owner = Gameboard.Instance.players[cut.PlayerId];
                    if (owner && owner.playerAI) owner.playerAI.LearnBlockade(cut.Planet, cut.Value, turn);
                }
            }

            private void ExecuteOrder(GameAIOrder executableOrder)
            {
                var targetPlanet = GameAIMap.GetPlanet(executableOrder.Target);
                switch (executableOrder.Type)
                {
                    case GameAIOrder.OrderType.OrderTypePopulationTransport:
                        if (ApplyColonistArrival(targetPlanet, executableOrder))
                            AITuningLogger.LogColonistDocked(Gameboard.Instance.TurnNumber, executableOrder.PlayerId,
                                targetPlanet.PlanetName, Convert.ToInt32(executableOrder.Data));

                        var arrivingPlayerAI = Gameboard.Instance.players[executableOrder.PlayerId].playerAI;
                        if (arrivingPlayerAI.IsCoverageGap(targetPlanet,
                                arrivingPlayerAI.LastFoodSurplusPlanets, arrivingPlayerAI.FoodDistributionCenters))
                            AITuningLogger.LogDCCoverageGap(Gameboard.Instance.TurnNumber, executableOrder.PlayerId,
                                targetPlanet.PlanetName, "Food");
                        if (arrivingPlayerAI.IsCoverageGap(targetPlanet,
                                arrivingPlayerAI.LastGrotsitsSurplusPlanets, arrivingPlayerAI.GrotsitsDistributionCenters))
                            AITuningLogger.LogDCCoverageGap(Gameboard.Instance.TurnNumber, executableOrder.PlayerId,
                                targetPlanet.PlanetName, "Grotsits");

                        break;
                    case GameAIOrder.OrderType.OrderTypePopulationChange:
                        var changeAmount = Convert.ToInt32(executableOrder.Data);
                        targetPlanet.ChangePopulation(changeAmount, executableOrder.PlayerId);
                        break;
                    case GameAIOrder.OrderType.OrderTypePopulationTransferInProgress:
                        targetPlanet.SetPopulationTransferInProgress(executableOrder.PlayerId);
                        break;
                    case GameAIOrder.OrderType.OrderTypeFoodTransport:
                        targetPlanet.Food += Convert.ToSingle(executableOrder.Data);
                        targetPlanet.FoodShipmentIncoming = false;
                        break;
                    case GameAIOrder.OrderType.OrderTypeFoodChange:
                        targetPlanet.Food += Convert.ToSingle(executableOrder.Data);
                        break;
                    case GameAIOrder.OrderType.OrderTypeFoodTransportInProgress:
                        targetPlanet.FoodShipmentIncoming = true;
                        break;
                    case GameAIOrder.OrderType.OrderTypeGrotsitsTransport:
                        targetPlanet.Grotsits += Convert.ToSingle(executableOrder.Data);
                        targetPlanet.GrotsitsShipmentIncoming = false;
                        break;
                    case GameAIOrder.OrderType.OrderTypeGrotsitsChange:
                        targetPlanet.Grotsits += Convert.ToSingle(executableOrder.Data);
                        break;
                    case GameAIOrder.OrderType.OrderTypeGrotsitsTransportInProgress:
                        targetPlanet.GrotsitsShipmentIncoming = true;
                        break;
                    case GameAIOrder.OrderType.OrderTypeIndustryTransport:
                        targetPlanet.Industry += Convert.ToSingle(executableOrder.Data);
                        break;
                    case GameAIOrder.OrderType.OrderTypeIndustryChange:
                        targetPlanet.Industry += Convert.ToSingle(executableOrder.Data);
                        break;
                    case GameAIOrder.OrderType.OrderTypeIndustrySetProduction:
                        var productionAI = Gameboard.Instance.players[executableOrder.PlayerId].playerAI;
                        var newProductionItem = productionAI.ProductionCatalog.catalogItems
                            .Find(x => x.itemName == executableOrder.Data.ToString());
                        var baseWarship = productionAI.ProductionCatalog.catalogItems.Find(x => x.subType == "Warship");
                        var fixedCost = WarshipCosts.ProductionCost(newProductionItem, targetPlanet,
                            executableOrder.PlayerId, productionAI.ResearchCatalog.catalogItems,
                            baseWarship != null ? baseWarship.cost : 0f,
                            GameAIMap.GameAIConstants.warshipImprovementCostFactor);
                        targetPlanet.ScheduleProductionItem(newProductionItem, fixedCost);
                        break;
                    case GameAIOrder.OrderType.OrderTypeResearchChange:
                        targetPlanet.Research += Convert.ToSingle(executableOrder.Data);
                        break;
                    case GameAIOrder.OrderType.OrderTypeRemoveShip:
                        targetPlanet.UndockShip(Ship.ShipKind.ColonyShip);
                        break;
                    case GameAIOrder.OrderType.OrderTypeShipTransport:
                        ApplyShipArrival(targetPlanet, executableOrder);
                        break;
                    case GameAIOrder.OrderType.OrderTypeShipDeparture:
                        ApplyShipDeparture(targetPlanet, executableOrder);
                        break;
                    case GameAIOrder.OrderType.OrderTypeShipTransferInProgress:
                        ApplyShipTransferInProgress(targetPlanet, executableOrder);
                        break;
                    case GameAIOrder.OrderType.OrderTypeColonyFoodRider:
                        ApplyColonyFoodRider(targetPlanet, executableOrder);
                        break;
                    default:
                        break;
                }

            }

            private static Ship.ShipKind FleetKind(GameAIOrder order)
                => order.Fleet != null ? order.Fleet.Kind : Ship.ShipKind.WarShip;

            // Delayed: the food a colony ship carried lands with the colonist. Deliberately its own order, separate
            // from the food shipping system (no FoodShipmentIncoming flag), so neither system has to know the other.
            public static void ApplyColonyFoodRider(Planet target, GameAIOrder order)
            {
                target.Food += Convert.ToSingle(order.Data);
            }

            // A colonist lands: below max population it joins the planet as before; at or above max the colonist docks as a
            // colony ship for the order's player instead (eligible for the next colonization pass). The ship's research
            // snapshot is rebuilt from the owner's current research (a colonist order carries none) via the default dock,
            // which needs Gameboard.Instance; the self-check passes its own. Returns true when a ship docked.
            public static bool ApplyColonistArrival(Planet target, GameAIOrder order, Action<Planet, int> dockColonyShip = null)
            {
                target.SetPopulationTransferInProgress(order.PlayerId, false);
                if (target.Population.Count >= target.MaxPopulation)
                {
                    (dockColonyShip ?? DefaultDockColonyShip)(target, order.PlayerId);
                    return true;
                }
                target.ChangePopulation(Convert.ToInt32(order.Data), order.PlayerId);
                return false;
            }

            private static void DefaultDockColonyShip(Planet target, int owner)
                => target.DockShipRebuiltSnapshot(Ship.ShipKind.ColonyShip, owner);

            // Immediate: takes the fleet's ships off the origin planet (same first-N ships the payload was read from).
            public static void ApplyShipDeparture(Planet origin, GameAIOrder order)
            {
                origin.UndockShips(FleetKind(order), order.PlayerId, Convert.ToInt32(order.Data));
            }

            // Immediate: marks ships as on their way so the target's deficit accounts for them.
            public static void ApplyShipTransferInProgress(Planet target, GameAIOrder order)
            {
                target.AddIncomingShips(FleetKind(order), order.PlayerId, Convert.ToInt32(order.Data));
            }

            // Delayed arrival: docks the fleet for the ORDER's player with the snapshots it left with.
            // A fleet with fewer snapshots than ships (should not happen) falls back to a rebuilt one.
            public static void ApplyShipArrival(Planet target, GameAIOrder order)
            {
                var kind = FleetKind(order);
                var count = Convert.ToInt32(order.Data);
                for (var i = 0; i < count; i++)
                {
                    if (order.Fleet != null && i < order.Fleet.Snapshots.Count)
                        target.DockShipFromSave(kind, order.PlayerId, order.Fleet.Snapshots[i]);
                    else
                        target.DockShipRebuiltSnapshot(kind, order.PlayerId);
                }
                target.AddIncomingShips(kind, order.PlayerId, -count);
            }

            private void ProcessNewOrders(List<GameAIOrder> newOrders)
            {
                CurrentAIOrders.AddRange(newOrders.FindAll(x =>
                    x.TimingType == GameAIOrder.OrderTimingType.OrderTimingTypeDelayed));
                var executableOrders = newOrders.FindAll(x => x.TimingDelay <= 0);
                foreach (var executableOrder in executableOrders)
                    ExecuteOrder(executableOrder);
            }

            private void UpdateAllPlanets(List<Planet.PlanetUpdateResult> planetUpdateResults)
            {
                GameAIMap.UpdateAllPlanets(planetUpdateResults);
            }


            private void ProcessResults(List<Planet.PlanetUpdateResult> results, List<GameAIOrder> orders)
            {
                for (var playerID = 0; playerID < Gameboard.Instance.players.Count(); ++playerID)
                {
                    Gameboard.Instance.players[playerID].ProcessResults(results, orders);
                }
            }

            public void SetSimulationStats(SaveLoadSystem.GameSave gameSave)
            {
                foreach (var orderStatus in gameSave.orders)
                {
                    CurrentAIOrders.Add(new GameAIOrder
                    {
                        Type = orderStatus.type,
                        TimingType = orderStatus.timingType,
                        TimingDelay = orderStatus.timingDelay,
                        TotalDelay = orderStatus.totalDelay,
                        Data = (orderStatus.dataType == "float" ? (float)orderStatus.data : (int)orderStatus.data),
                        Origin = orderStatus.origin,
                        Target = orderStatus.target,
                        PlayerId = orderStatus.playerId,
                        Fleet = GameAIOrder.ShipFleetPayload.FromSave(orderStatus.fleetShips),
                        Route = GameAIOrder.RouteFromSave(orderStatus.route),
                    });
                }

                GameAIMap.SetPlanetSimulationStats(gameSave);
                GameAIMap.RecomputeIncomingShips(CurrentAIOrders);
            }

            public Planet GetPlanet(string planetName)
            {
                return GameAIMap.GetPlanet(planetName);
            }

            public Planet GetPlayerCapitol(int playerID)
            {
                return GameAIMap.GetPlayerCapitol(playerID);
            }

            // Start is called once before the first execution of UpdatePlanet after the MonoBehaviour is created
            void Start()
            {

            }

            // UpdatePlanet is called once per frame
            void Update()
            {

            }
        }
    }
}
