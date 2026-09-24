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
                    OrderTypeShipTransferInProgress
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

            public void ClearGameAI()
            {
                CurrentAIOrders.Clear();

            }

            public void GameAIUpdate()
            {
                var gameAIOrders = new List<GameAIOrder>();
                var planetUpdateResults = new List<Planet.PlanetUpdateResult>();
                ProcessCurrentOrders();
                planetUpdateResults.Clear();
                UpdateAllPlanets(planetUpdateResults);
                AITuningLogger.LogPlanetEvents(Gameboard.Instance.TurnNumber, planetUpdateResults);
                GameAIMap.Knowledge.Update(GameAIMap, Gameboard.Instance.players.Count);
                ProcessResults(planetUpdateResults, gameAIOrders);
                Gameboard.Instance.CreateNotificationsForNewOrders(gameAIOrders);
                AITuningLogger.LogNewOrders(Gameboard.Instance.TurnNumber, gameAIOrders);
                ProcessNewOrders(gameAIOrders);
            }

            private void ProcessCurrentOrders()
            {
                foreach (var gameAIOrder in CurrentAIOrders)
                {
                    gameAIOrder.TimingDelay--;
                }

                var executableOrders = CurrentAIOrders.FindAll(x => x.TimingDelay <= 0);
                Gameboard.Instance.CreateNotificationsForExecutingOrders(executableOrders);
                AITuningLogger.LogExecutingOrders(Gameboard.Instance.TurnNumber, executableOrders);
                foreach (var executableOrder in executableOrders)
                {
                    ExecuteOrder(executableOrder);
                }

                CurrentAIOrders.RemoveAll(x => x.TimingDelay <= 0);
            }

            private void ExecuteOrder(GameAIOrder executableOrder)
            {
                var targetPlanet = GameAIMap.GetPlanet(executableOrder.Target);
                switch (executableOrder.Type)
                {
                    case GameAIOrder.OrderType.OrderTypePopulationTransport:
                        targetPlanet.ChangePopulation(Convert.ToInt32(executableOrder.Data), executableOrder.PlayerId);

                        if (targetPlanet.IsPopulationTransferInProgress(executableOrder.PlayerId))
                        {
                            targetPlanet.SetPopulationTransferInProgress(executableOrder.PlayerId, false);
                        }

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
                        var newProductionItem = 
                            Gameboard.Instance.players[executableOrder.PlayerId].playerAI.ProductionCatalog.catalogItems
                                .Find(x => x.itemName == executableOrder.Data.ToString());
                        targetPlanet.ScheduleProductionItem(newProductionItem);
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
                    default:
                        break;
                }

            }

            private static Ship.ShipKind FleetKind(GameAIOrder order)
                => order.Fleet != null ? order.Fleet.Kind : Ship.ShipKind.WarShip;

            // Immediate: takes the fleet's ships off the origin planet (same first-N ships the payload was read from).
            public static void ApplyShipDeparture(Planet origin, GameAIOrder order)
            {
                origin.UndockShips(FleetKind(order), order.PlayerId, Convert.ToInt32(order.Data));
            }

            // Immediate: marks ships as on their way so the target's deficit accounts for them.
            public static void ApplyShipTransferInProgress(Planet target, GameAIOrder order)
            {
                target.AddIncomingShips(FleetKind(order), Convert.ToInt32(order.Data));
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
                target.AddIncomingShips(kind, -count);
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
                        PlayerId = orderStatus.playerId
                    });
                }

                GameAIMap.SetPlanetSimulationStats(gameSave);
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
