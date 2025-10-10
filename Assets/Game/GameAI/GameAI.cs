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
                    OrderTypeGrotsitsTransport,
                    OrderTypeGrotsitsChange,
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
                PlanetaryProductionUpdate(planetUpdateResults);
                ProcessResults(planetUpdateResults, gameAIOrders);

                ProcessNewOrders(gameAIOrders);
            }

            private void ProcessCurrentOrders()
            {
                foreach (var gameAIOrder in CurrentAIOrders)
                {
                    gameAIOrder.TimingDelay--;
                }

                var executableOrders = CurrentAIOrders.FindAll(x => x.TimingDelay <= 0);
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
                    case GameAIOrder.OrderType.OrderTypeFoodChange:
                        targetPlanet.Food += Convert.ToSingle(executableOrder.Data);
                        break;
                    case GameAIOrder.OrderType.OrderTypeGrotsitsTransport:
                    case GameAIOrder.OrderType.OrderTypeGrotsitsChange:
                        targetPlanet.Grotsits += Convert.ToSingle(executableOrder.Data);
                        break;
                    default:
                        break;
                }

            }

            private void ProcessNewOrders(List<GameAIOrder> newOrders)
            {
                CurrentAIOrders.AddRange(newOrders.FindAll(x =>
                    x.TimingType == GameAIOrder.OrderTimingType.OrderTimingTypeDelayed));
                var executableOrders = newOrders.FindAll(x => x.TimingDelay <= 0);
                foreach (var executableOrder in executableOrders)
                    ExecuteOrder(executableOrder);
            }

            private void PlanetaryProductionUpdate(List<Planet.PlanetUpdateResult> planetUpdateResults)
            {
                GameAIMap.PlanetaryProductionUpdate(planetUpdateResults);
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

            // Start is called once before the first execution of Update after the MonoBehaviour is created
            void Start()
            {

            }

            // Update is called once per frame
            void Update()
            {

            }
        }
    }
}
