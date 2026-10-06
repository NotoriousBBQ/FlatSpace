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
                    OrderTypeColonyFoodRider,
                    // Appended last: OrderType serializes as an int. Immediate; Data is the rival's player id (an int),
                    // PlayerId the player that decided; Origin and Target are empty. Executed by ApplyStanceOrder.
                    OrderTypeDeclareWar,
                    OrderTypeMakePeace,
                    // Appended last. Immediate; Data is the rival's player id; executed by ApplySurrender: both stances go
                    // to Peace and the pair is locked against declarations for surrenderTruceTurns.
                    OrderTypeSurrender
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
                    public List<float> Damage = new List<float>();   // one per snapshot; a short or empty list means full health

                    public float DamageAt(int index) => index >= 0 && index < Damage.Count ? Damage[index] : 0f;

                    public List<SaveLoadSystem.GameSave.ShipSave> ToSave(int owner)
                    {
                        return Snapshots.Select((snapshot, i) => new SaveLoadSystem.GameSave.ShipSave
                        {
                            kind = Kind,
                            owner = owner,
                            researchSnapshot = new List<string>(snapshot),
                            damage = DamageAt(i),
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
                            Damage = ships.Select(s => s.damage).ToList(),
                        };
                    }
                }
            }
            public GameAIMap GameAIMap { get; private set; }
            public List<GameAIOrder> CurrentAIOrders { get; private set; } = new List<GameAIOrder>();
            public static Random Rand = new Random();   // not readonly: SimultaneitySelfCheck re-seeds it per player

            public void InitGameAI(List<PlanetSpawnData> spawnDataList, GameAIConstants gameAIConstants)
            {
                GameAIMap = this.AddComponent<GameAIMap>() as GameAIMap;
                GameAIMap.GameAIMapInit(spawnDataList, gameAIConstants);
                GameAIMap.Diplomacy.Enabled = gameAIConstants.diplomacyEnabled;
            }

            // Which planets are short of grotsits right now, so GrotsitsShort is logged on change only (log-only state).
            private readonly GrotsitsShortTracker _grotsitsShort = new GrotsitsShortTracker();

            // Orders already logged as ColonistRedirectFailed, so it is logged once per order and not every turn the blockade
            // stays ahead of it (log-only state, pruned as orders leave; a load logs each still-failing order once more).
            private readonly HashSet<GameAIOrder> _redirectFailedLogged = new HashSet<GameAIOrder>();

            public void ClearGameAI()
            {
                CurrentAIOrders.Clear();
                _grotsitsShort.Clear();
                _redirectFailedLogged.Clear();

            }

            public void GameAIUpdate()
            {
                GameAIMap.Diplomacy.Turn = Gameboard.Instance.TurnNumber;   // the truce is tested against it
                var gameAIOrders = new List<GameAIOrder>();
                var planetUpdateResults = new List<Planet.UpdateResult>();
                ProcessCurrentOrders();
                planetUpdateResults.Clear();
                RunCombat(planetUpdateResults);
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

            // Docked warships of players at war fight before the planets update, so this turn's losses reach ProcessResults.
            private void RunCombat(List<Planet.UpdateResult> results)
            {
                var stats = new WarshipStats(BlockadeSystem.ResearchItemsFrom(Gameboard.Instance.players));
                var turn = Gameboard.Instance.TurnNumber;
                foreach (var report in CombatSystem.Resolve(GameAIMap, stats, GameAIMap.GameAIConstants, turn, results))
                    AITuningLogger.LogCombat(turn, report.Attacker, report.Planet, report.Victim, report.DamageDealt,
                        report.ShipsDestroyed);
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
                var healthStats = new WarshipStats(BlockadeSystem.ResearchItemsFrom(Gameboard.Instance.players));
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
                    if (GameAIMap.Diplomacy.Enabled)
                        foreach (var rival in GameAIMap.Knowledge.ContactPlayers(GameAIMap, player))
                        {
                            var pair = GameAIMap.Diplomacy.Get(player, rival);
                            AITuningLogger.LogHostility(turnNumber, player, rival, pair.Hostility, pair.MyStrength,
                                pair.RivalStrength, pair.NearShips, pair.LossShare, pair.LossAccum);
                        }
                    // How hurt the player's warships are, so a log shows whether repair keeps pace with combat.
                    var fleet = GameAIMap.PlanetList.SelectMany(p => p.DockedShips)
                        .Where(s => s.Owner == player && s.Kind == Ship.ShipKind.WarShip).ToList();
                    if (fleet.Count > 0)
                        AITuningLogger.LogFleetHealth(turnNumber, player, fleet.Count, fleet.Count(s => s.Damage > 0f),
                            100f * fleet.Average(s =>
                            {
                                var max = healthStats.Health(s.Template, s.ResearchSnapshot);
                                return max <= 0f ? 0f : healthStats.CurrentHealth(s) / max;
                            }));
                }
            }

            private void ProcessCurrentOrders()
            {
                foreach (var gameAIOrder in CurrentAIOrders)
                {
                    gameAIOrder.TimingDelay--;
                }

                ApplyBlockades();
                RedirectColonists();

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
                    GameAIMap.Diplomacy.RecordCut(cut.PlayerId, cut.BlockerId);   // hostility toward the blocker (see UpdateDiplomacy)
                    var owner = Gameboard.Instance.players[cut.PlayerId];
                    if (owner && owner.playerAI) owner.playerAI.LearnBlockade(cut.Planet, cut.Value, turn);
                }
            }

            // Once per turn, after blockades were applied and before orders execute: a colonist whose remaining route now
            // crosses a blockade its owner can see is detoured or diverted (ColonistRedirect); one that cannot be saved is
            // left to be cut as before and logged once. Uses only the owner's BlockadeView.
            private void RedirectColonists()
            {
                var research = BlockadeSystem.ResearchItemsFrom(Gameboard.Instance.players);
                if (research == null) return;
                var turn = Gameboard.Instance.TurnNumber;
                var blockade = new BlockadeSystem(GameAIMap, research);
                var maxNodes = GameAIMap.GameAIConstants.maxPathNodesForColonization;

                _redirectFailedLogged.RemoveWhere(o => !CurrentAIOrders.Contains(o));
                var colonists = CurrentAIOrders.FindAll(o =>
                    o.Type == GameAIOrder.OrderType.OrderTypePopulationTransport && o.TimingDelay > 0);
                foreach (var colonist in colonists)
                {
                    if (colonist.PlayerId < 0 || colonist.PlayerId >= Gameboard.Instance.players.Count) continue;
                    var ai = Gameboard.Instance.players[colonist.PlayerId]?.playerAI;
                    if (ai == null || ai.CurrentBlockadeView == null) continue;

                    var result = ColonistRedirect.Plan(GameAIMap, blockade, colonist, ai.CurrentBlockadeView, maxNodes,
                        ai.IsDiversionTarget, ai.ColonizationCostDivisor,
                        GameAIMap.GameAIConstants.colonistDetourDivertRatio);
                    if (result.Kind == ColonistRedirect.RedirectKind.None)
                    {
                        if (result.BlockedAhead.Count > 0 && _redirectFailedLogged.Add(colonist))
                            AITuningLogger.LogColonistRedirectFailed(turn, colonist.PlayerId, colonist.Origin, colonist.Target,
                                ai.BlockedNodeSummary(result.BlockedAhead));
                        continue;
                    }

                    var oldTarget = colonist.Target;
                    var blocked = ai.BlockedNodeSummary(result.BlockedAhead);
                    ColonistRedirect.Apply(GameAIMap, CurrentAIOrders, colonist, result);
                    AITuningLogger.LogColonistRedirect(turn, colonist.PlayerId, colonist.Origin, oldTarget,
                        result.Kind.ToString(), result.Target, result.Nodes, result.Cost, blocked, result.DeclinedDetourCost);
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
                    case GameAIOrder.OrderType.OrderTypeDeclareWar:
                    case GameAIOrder.OrderType.OrderTypeMakePeace:
                    {
                        var stanceTurn = Gameboard.Instance.TurnNumber;
                        if (ApplyStanceOrder(GameAIMap.Diplomacy, executableOrder, stanceTurn))
                        {
                            var stanceRival = Convert.ToInt32(executableOrder.Data);
                            var stancePair = GameAIMap.Diplomacy.Get(executableOrder.PlayerId, stanceRival);
                            AITuningLogger.LogStance(stanceTurn, executableOrder.PlayerId, stanceRival,
                                stancePair.Stance.ToString(), stancePair.Hostility, stancePair.CutsTerm,
                                stancePair.NearTerm, stancePair.StrengthTerm, stancePair.PWar, stancePair.LossAccum);
                        }
                        break;
                    }
                    case GameAIOrder.OrderType.OrderTypeSurrender:
                    {
                        var surrenderTurn = Gameboard.Instance.TurnNumber;
                        var surrenderRival = Convert.ToInt32(executableOrder.Data);
                        ApplySurrender(GameAIMap.Diplomacy, executableOrder, surrenderTurn,
                            GameAIMap.GameAIConstants.surrenderTruceTurns);
                        var surrenderPair = GameAIMap.Diplomacy.Get(executableOrder.PlayerId, surrenderRival);
                        AITuningLogger.LogSurrender(surrenderTurn, executableOrder.PlayerId, surrenderRival, surrenderPair.LossShare,
                            surrenderPair.PSurrender, surrenderPair.TruceUntil);
                        break;
                    }
                    default:
                        break;
                }

            }

            private static Ship.ShipKind FleetKind(GameAIOrder order)
                => order.Fleet != null ? order.Fleet.Kind : Ship.ShipKind.WarShip;

            // Immediate: player order.PlayerId takes its stance toward the rival in order.Data to War or Peace. True only
            // when the stance changed (the caller logs on that). Pure: no Gameboard.Instance.
            public static bool ApplyStanceOrder(DiplomacyState diplomacy, GameAIOrder order, int turn)
            {
                var rival = Convert.ToInt32(order.Data);
                var stance = order.Type == GameAIOrder.OrderType.OrderTypeDeclareWar ? Stance.War : Stance.Peace;
                if (stance == Stance.War && diplomacy.InTruce(order.PlayerId, rival, turn)) return false;   // locked by a surrender
                return diplomacy.SetStance(order.PlayerId, rival, stance, turn);
            }

            // Immediate: the player surrenders to the rival in order.Data. Both stances go to Peace and both pair rows are locked
            // against new declarations (and forced wars) until turn + truceTurns. Pure: no Gameboard.Instance.
            public static bool ApplySurrender(DiplomacyState diplomacy, GameAIOrder order, int turn, int truceTurns)
            {
                var me = order.PlayerId;
                var rival = Convert.ToInt32(order.Data);
                diplomacy.SetStance(me, rival, Stance.Peace, turn);
                diplomacy.SetStance(rival, me, Stance.Peace, turn);
                foreach (var key in new[] { (me, rival), (rival, me) })
                {
                    var pair = diplomacy.Get(key.Item1, key.Item2);
                    pair.TruceUntil = turn + truceTurns;
                    diplomacy.Set(key.Item1, key.Item2, pair);
                }
                return true;
            }

            // Delayed: the food a colony ship carried lands with the colonist. Deliberately its own order, separate
            // from the food shipping system (no FoodShipmentIncoming flag), so neither system has to know the other.
            public static void ApplyColonyFoodRider(Planet target, GameAIOrder order)
            {
                target.Food += Convert.ToSingle(order.Data);
            }

            // A colonist lands: below max population it joins the planet as before; at or above max on a planet the order's
            // player owns it docks as a colony ship instead (eligible for the next colonization pass). A full planet owned by
            // someone else (or tied) still takes the colonist, as before: colony-ship queries (CheckColonizationReady,
            // UndockShip, PlanetHasColonyShip) do not check a ship's owner, so a foreign ship docked there would be launched
            // by that planet's owner or sit unused. The ship's research snapshot is rebuilt from the owner's current research
            // (a colonist order carries none) via the default dock, which needs Gameboard.Instance; the self-check passes its
            // own. Returns true when a ship docked.
            public static bool ApplyColonistArrival(Planet target, GameAIOrder order, Action<Planet, int> dockColonyShip = null)
            {
                target.SetPopulationTransferInProgress(order.PlayerId, false);
                if (target.Population.Count >= target.MaxPopulation && target.Owner == order.PlayerId)
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
                        target.DockShipFromSave(kind, order.PlayerId, order.Fleet.Snapshots[i], order.Fleet.DamageAt(i));
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

            private void UpdateAllPlanets(List<Planet.UpdateResult> planetUpdateResults)
            {
                GameAIMap.UpdateAllPlanets(planetUpdateResults);
            }


            private void ProcessResults(List<Planet.UpdateResult> results, List<GameAIOrder> orders)
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
