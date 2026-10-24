using System;
using System.Collections.Generic;
using System.Linq;
using FlatSpace.AI;

namespace FlatSpace
{
    namespace Human
    {
        /// <summary>
        /// Everything the human is told about, one kind per kind of message or pause trigger. The pause-event subset is chosen in
        /// HumanPauseSettings; the rest are messages only. New research and new production starts are deliberately not events.
        /// New kinds are appended last: the pause settings are saved as ints.
        /// </summary>
        public enum HumanEventKind
        {
            PlanetAttacked, PlanetBlockaded, PlanetLost, FirstContact, WarDeclared, PeaceMade, FleetArrived,
            ShortageFood, ShortageGrotsits,
            ProductionComplete, ResearchComplete, ColonyShipReady, PlanetIdle, ResearchIdle,
            PopulationGained, PopulationLost, PlanetColonized, ProductionQueueEmpty,
            ShipmentLost, ConversionBegan, ConversionEnded, TransportOrdered,
            ColonyShipBuilt,
        }

        /// <summary>One thing that happened this turn. Rival means another player did it on a planet the human can see (or between two players the human has met).</summary>
        public struct HumanEvent
        {
            public HumanEventKind Kind;
            public string Planet;   // empty when the event has no planet (stance changes, research); for first contact, where the player was seen
            public int Player;      // the player it is about (the other side for contact and stance events); -1 when none
            public bool Rival;
        }

        /// <summary>What a turn produced, handed to HumanTurnEvents.Collect. The simulation keeps these lists for the turn just played (GameAI.Last*).</summary>
        public class HumanTurnInput
        {
            public IList<Planet.UpdateResult> Results;
            public IList<GameAI.GameAIOrder> Executed;        // orders that executed this turn (arrivals, stance changes ...)
            public IList<GameAI.GameAIOrder> NewOrders;       // orders issued this turn
            public IList<BlockadeSystem.BlockadeCut> Cuts;
            public IList<ConversionReport> Conversions;
            public IEnumerable<string> BlockadedPlanets;      // planets blockaded against the human right now (BlockadeView)
            public string ResearchCompleted;                  // the research that completed this turn, or null
            public bool ShipmentsAutomated;
            // Whether a planet is in the human's sight right now (the fog view). Rival events, and who counts as met, depend on it. Null falls
            // back to the planets the human has ever known (PlayerKnowledge), which is what a self-check without a fog system uses.
            public Func<string, bool> IsVisible;
        }

        /// <summary>
        /// Turns a turn's raw results into the events the human sees, for the message list and for pausing the timer. Pure (it reads a
        /// GameAIMap and the human's PlayerAI) and stateful only for the kinds that are a change of state: meeting a player, a planet
        /// changing hands, a blockade starting, research going idle. The first Collect only takes those snapshots.
        /// A player is met when they are seen on a planet in sight, and stance news (a war declared, peace) about a player the human has not
        /// met is not reported: the human cannot act on, or even explain, what they have not seen.
        /// </summary>
        public class HumanTurnEvents
        {
            private bool _initialized;
            private readonly Dictionary<string, int> _owners = new Dictionary<string, int>();
            private readonly HashSet<int> _met = new HashSet<int>();
            private readonly HashSet<string> _blockaded = new HashSet<string>();
            private bool _researchIdle;

            public List<HumanEvent> Collect(GameAIMap map, PlayerAI human, HumanTurnInput input)
            {
                var events = new List<HumanEvent>();
                var me = human.Player.playerID;

                bool Known(string planet) => !string.IsNullOrEmpty(planet)
                    && (input.IsVisible != null ? input.IsVisible(planet) : map.Knowledge.IsKnown(me, planet));
                bool HumanPresent(string planetName)
                {
                    var planet = map.GetPlanet(planetName);
                    return planet != null && (planet.Owner == me || planet.Population.Exists(p => p.Player == me)
                                              || planet.DockedShips.Exists(s => s.Owner == me));
                }
                void Add(HumanEventKind kind, string planet, int player, bool rival)
                {
                    var e = new HumanEvent { Kind = kind, Planet = planet ?? "", Player = player, Rival = rival };
                    if (!events.Contains(e)) events.Add(e);
                }

                // ── Who is in sight: a player is met the first time they are seen on a planet in sight ──
                var present = new SortedDictionary<int, string>();
                foreach (var planet in map.PlanetList)
                {
                    if (!Known(planet.PlanetName)) continue;
                    foreach (var inhabitant in planet.Population)
                        if (inhabitant.Player != me && inhabitant.Player >= 0 && !present.ContainsKey(inhabitant.Player)) present[inhabitant.Player] = planet.PlanetName;
                    foreach (var ship in planet.DockedShips)
                        if (ship.Owner != me && ship.Owner >= 0 && Ship.CountsAsShip(ship.Kind) && !present.ContainsKey(ship.Owner)) present[ship.Owner] = planet.PlanetName;
                }
                foreach (var seen in present)
                    if (_met.Add(seen.Key) && _initialized)
                        Add(HumanEventKind.FirstContact, seen.Value, seen.Key, false);

                // ── This turn's planet results ──
                foreach (var r in input.Results ?? new List<Planet.UpdateResult>())
                {
                    var mine = r.PlayerID == me;
                    switch (r.Result)
                    {
                        case Planet.UpdateResult.UpdateResultType.UpdateResultTypeFoodShortage:
                            if (mine) Add(HumanEventKind.ShortageFood, r.Name, me, false);
                            break;
                        case Planet.UpdateResult.UpdateResultType.UpdateResultTypeGrotsitsShortage:
                            if (mine) Add(HumanEventKind.ShortageGrotsits, r.Name, me, false);
                            break;
                        case Planet.UpdateResult.UpdateResultType.UpdateResultTypeIndustryProductionComplete:
                            if (mine)
                            {
                                Add(HumanEventKind.ProductionComplete, r.Name, me, false);
                                var builtItem = r.Data != null && human.ProductionCatalog != null ? human.ProductionCatalog.GetItem(r.Data.ToString()) : null;
                                if (builtItem != null && builtItem.subType == "ColonyShip") Add(HumanEventKind.ColonyShipBuilt, r.Name, me, false);
                            }
                            else if (r.PlayerID >= 0 && Known(r.Name)) Add(HumanEventKind.ProductionComplete, r.Name, r.PlayerID, true);
                            break;
                        case Planet.UpdateResult.UpdateResultType.UpdateResultTypeIndustryProductionQueueEmpty:
                            if (!mine) break;   // another player's empty queue is not an accomplishment
                            // A planet the human develops by hand is idle (they are waiting on it); one the AI develops just had its queue emptied.
                            if (map.GetPlanet(r.Name) != null && map.GetPlanet(r.Name).ManualDevelopment)
                                Add(HumanEventKind.PlanetIdle, r.Name, me, false);
                            else
                                Add(HumanEventKind.ProductionQueueEmpty, r.Name, me, false);
                            break;
                        case Planet.UpdateResult.UpdateResultType.UpdateResultTypeColonizerReady:
                            if (mine) Add(HumanEventKind.ColonyShipReady, r.Name, me, false);
                            break;
                        case Planet.UpdateResult.UpdateResultType.UpdateResultTypePopulationGain:
                            if (mine) Add(HumanEventKind.PopulationGained, r.Name, me, false);
                            break;
                        case Planet.UpdateResult.UpdateResultType.UpdateResultTypePopulationLoss:
                            if (mine) Add(HumanEventKind.PopulationLost, r.Name, me, false);
                            break;
                        case Planet.UpdateResult.UpdateResultType.UpdateResultTypeWarshipsLost:
                        case Planet.UpdateResult.UpdateResultType.UpdateResultTypeColonyShipsLost:
                            // Reported on every turn a fight lasts, once per (victim, attacker): one event per planet is enough.
                            if (mine || HumanPresent(r.Name)) Add(HumanEventKind.PlanetAttacked, r.Name, me, false);
                            else if (Known(r.Name)) Add(HumanEventKind.PlanetAttacked, r.Name, r.PlayerID, true);
                            break;
                    }
                }

                // ── Orders that executed: arrivals and stance changes ──
                foreach (var order in input.Executed ?? new List<GameAI.GameAIOrder>())
                {
                    switch (order.Type)
                    {
                        case GameAI.GameAIOrder.OrderType.OrderTypeShipTransport:
                            if (order.PlayerId == me) Add(HumanEventKind.FleetArrived, order.Target, me, false);
                            else if (Known(order.Target)) Add(HumanEventKind.FleetArrived, order.Target, order.PlayerId, true);
                            break;
                        case GameAI.GameAIOrder.OrderType.OrderTypeDeclareWar:
                        case GameAI.GameAIOrder.OrderType.OrderTypeMakePeace:
                        case GameAI.GameAIOrder.OrderType.OrderTypeSurrender:
                        {
                            var rival = Convert.ToInt32(order.Data);
                            var kind = order.Type == GameAI.GameAIOrder.OrderType.OrderTypeDeclareWar ? HumanEventKind.WarDeclared : HumanEventKind.PeaceMade;
                            if (order.PlayerId == me || rival == me)
                            {
                                var other = order.PlayerId == me ? rival : order.PlayerId;
                                if (_met.Contains(other)) Add(kind, "", other, false);   // news about someone the human has not seen is not news
                            }
                            else if (_met.Contains(order.PlayerId) && _met.Contains(rival))
                                Add(kind, "", order.PlayerId, true);
                            break;
                        }
                    }
                }

                // ── Orders issued: automated shipping ──
                if (input.ShipmentsAutomated)
                    foreach (var order in input.NewOrders ?? new List<GameAI.GameAIOrder>())
                        if (order.PlayerId == me && (order.Type == GameAI.GameAIOrder.OrderType.OrderTypeFoodTransport
                                                     || order.Type == GameAI.GameAIOrder.OrderType.OrderTypeGrotsitsTransport))
                            Add(HumanEventKind.TransportOrdered, order.Target, me, false);

                // ── Shipments cut by blockades, conversions ──
                foreach (var cut in input.Cuts ?? new List<BlockadeSystem.BlockadeCut>())
                {
                    if (cut.Type != GameAI.GameAIOrder.OrderType.OrderTypeFoodTransport
                        && cut.Type != GameAI.GameAIOrder.OrderType.OrderTypeGrotsitsTransport) continue;
                    if (cut.PlayerId == me) Add(HumanEventKind.ShipmentLost, cut.Planet, me, false);
                    else if (Known(cut.Planet)) Add(HumanEventKind.ShipmentLost, cut.Planet, cut.PlayerId, true);
                }
                foreach (var report in input.Conversions ?? new List<ConversionReport>())
                {
                    HumanEventKind kind;
                    if (report.What == ConversionReport.Kind.Start) kind = HumanEventKind.ConversionBegan;
                    else if (report.What == ConversionReport.Kind.End) kind = HumanEventKind.ConversionEnded;
                    else continue;
                    if (HumanPresent(report.Planet)) Add(kind, report.Planet, report.Dominator, false);
                    else if (Known(report.Planet)) Add(kind, report.Planet, report.Dominator, true);
                }

                // ── Research ──
                if (!string.IsNullOrEmpty(input.ResearchCompleted))
                    Add(HumanEventKind.ResearchComplete, "", me, false);
                var idle = human.ResearchIdle;
                if (idle && !_researchIdle) Add(HumanEventKind.ResearchIdle, "", me, false);
                _researchIdle = idle;

                // ── Changes of state (the first call only takes the snapshots) ──
                var blockadedNow = new HashSet<string>(input.BlockadedPlanets ?? new string[0]);
                foreach (var name in blockadedNow)
                    if (_initialized && !_blockaded.Contains(name)) Add(HumanEventKind.PlanetBlockaded, name, -1, false);
                _blockaded.Clear();
                _blockaded.UnionWith(blockadedNow);

                foreach (var planet in map.PlanetList)
                {
                    var now = planet.Owner;
                    if (_initialized && _owners.TryGetValue(planet.PlanetName, out var before) && before != now)
                    {
                        if (before == me && now != me) Add(HumanEventKind.PlanetLost, planet.PlanetName, now, false);
                        else if (now == me && before == Planet.NoOwner) Add(HumanEventKind.PlanetColonized, planet.PlanetName, me, false);
                        else if (before == Planet.NoOwner && now >= 0 && now != me && Known(planet.PlanetName))
                            Add(HumanEventKind.PlanetColonized, planet.PlanetName, now, true);
                    }
                    _owners[planet.PlanetName] = now;
                }
                _initialized = true;
                return events;
            }
        }
    }
}
