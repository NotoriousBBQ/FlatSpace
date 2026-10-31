// PlayerAI.cs
using System;
using System.Collections.Generic;
using System.Linq;
using FlatSpace.Game;
using Flatspace.Objects.Production;
using Flatspace.Objects.Resource;
using Unity.VisualScripting;
using UnityEngine;

namespace FlatSpace
{
    namespace AI
    {
        public class PlayerAI : MonoBehaviour
        {
            // ── Public state ─────────────────────────────────────────────────

            public enum AIStrategy
            {
                AIStrategyNone = 0,
                AIStrategyExpand,
                AIStrategyConsolidate,
                AIStrategyAmass
            }

            /// <summary>
            /// Consolidate and Amass share every rule written as "Consolidate only" (the warship fleet-shortfall boost, the
            /// ColonyShip boost while targets remain, the chokepoint colonization tilt, the planner's garrisons, the assault and the
            /// blockade-breaking targets). Amass differs only through its own weight tables and the war gate on the assault.
            /// </summary>
            public static bool IsConsolidateLike(AIStrategy strategy)
                => strategy == AIStrategy.AIStrategyConsolidate || strategy == AIStrategy.AIStrategyAmass;

            public AIStrategy Strategy { get; set; } = AIStrategy.AIStrategyExpand;
            public Player      Player  { get; set; }

            /// <summary>
            /// True while a human controls this player (Player.isHuman). It is read from the Player, not stored here, because
            /// Gameboard recreates its Player components on every init and the PlayerAI is only attached on their first frame.
            /// A human player runs ProcessResultsHuman instead of the AI routine; every other player is unchanged.
            /// </summary>
            public bool HumanControlled => Player != null && Player.isHuman;

            /// <summary>Whether a player is the human one (Player.isHuman). False without a Gameboard, so a self-check that builds a PlayerAI directly sees no human.</summary>
            private static bool IsHumanPlayer(int playerId)
            {
                var board = Gameboard.Instance;
                return board != null && playerId >= 0 && playerId < board.players.Count && board.players[playerId].isHuman;
            }
            public GameAIMap   AIMap   { get; set; }

            public Catalog ProductionCatalog { get; set; }
            public Catalog ResearchCatalog   { get; set; }

            // ── Blockade awareness ───────────────────────────────────────────

            private BlockadeView _blockadeView;

            /// <summary>This player's view of visible blockades, rebuilt at the start of every ProcessResults; null before the first.</summary>
            public BlockadeView CurrentBlockadeView => _blockadeView;

            /// <summary>Rebuilds the view from current docked ships. Public for the self-check.</summary>
            public void RefreshBlockadeView(int turn = -1)
            {
                if (turn < 0) turn = Gameboard.Instance != null ? Gameboard.Instance.TurnNumber : 0;
                var items = ResearchCatalog != null ? ResearchCatalog.catalogItems : null;
                _blockadeView = BlockadeView.Build(AIMap, Player.playerID, new BlockadeSystem(AIMap, items),
                    _blockadeMemory, turn, AIMap.GameAIConstants.blockadeMemoryTurns);
            }

            // Planets where one of this player's own orders was cut by a blockade, remembered for a while so colonization
            // routes around them even when the player cannot currently see them (see BlockadeMemory).
            private readonly BlockadeMemory _blockadeMemory = new BlockadeMemory();

            /// <summary>Remembers a planet where one of this player's orders was cut. True (and logged) only when newly remembered.</summary>
            public bool LearnBlockade(string planetName, float value, int turn)
            {
                var news = _blockadeMemory.Learn(planetName, value, turn, AIMap.GameAIConstants.blockadeMemoryTurns);
                if (news) AITuningLogger.LogBlockadeLearned(turn, Player.playerID, planetName, value);
                return news;
            }

            public bool IsBlockadeRemembered(string planetName, int turn)
                => _blockadeMemory.IsActive(planetName, turn, AIMap.GameAIConstants.blockadeMemoryTurns);

            /// <summary>The active remembered blockades, for saving.</summary>
            public List<BlockadeMemory.Entry> RememberedBlockades(int turn)
                => _blockadeMemory.Snapshot(turn, AIMap.GameAIConstants.blockadeMemoryTurns);

            public void RestoreRememberedBlockades(IEnumerable<BlockadeMemory.Entry> entries)
                => _blockadeMemory.Restore(entries);

            // A null view (self-checks, before the first turn) means nothing is known to be blockaded.
            private bool IsBlockaded(string planetName)
                => _blockadeView != null && _blockadeView.IsBlockaded(planetName);

            // Why each ready colonizer was last held back (planet name -> reason), so ColonizeCancelled is logged when that
            // changes and not on every turn the condition holds. Log-only state: not saved (a load re-logs it once).
            private readonly Dictionary<string, string> _colonizeHeldBack = new Dictionary<string, string>();

            /// <summary>The reason this planet's colonizer was last held back, or null when it is not. Public for the self-check.</summary>
            public string ColonizeHeldBackReason(string planetName)
                => _colonizeHeldBack.TryGetValue(planetName, out var reason) ? reason : null;

            /// <summary>
            /// Records that a ready colonizer was held back. True when this is news (its first turn held back, or a different
            /// reason than last time), i.e. the caller should log it. Public for the self-check.
            /// </summary>
            public bool NoteColonizeHeldBack(string planetName, string reason)
            {
                if (_colonizeHeldBack.TryGetValue(planetName, out var last) && last == reason) return false;
                _colonizeHeldBack[planetName] = reason;
                return true;
            }

            // Why each shortage last went unsupplied ("<transportType>:<target>" -> reason), so ShipmentCancelled is logged on
            // a change and not every turn the blockade holds. Log-only state: not saved.
            private readonly Dictionary<string, string> _shipmentHeldBack = new Dictionary<string, string>();

            /// <summary>
            /// Why a shortage went unsupplied: the dropped (source, shortage) pair closest to shipping, for the
            /// ShipmentCancelled log line (log-only, not saved). BlockedNodes lists the route's blockaded planets with their
            /// values ("Normal 23=22,Farm 4=10", "-" when none).
            /// </summary>
            public class ShipmentHold
            {
                public string Reason;       // "Blockade" (loss >= amount) or "LowYield" (below the minimum delivered fraction)
                public string Source;
                public float Loss;
                public float Amount;
                public string BlockedNodes;
            }

            private readonly Dictionary<string, ShipmentHold> _shipmentHeldBackDetail = new Dictionary<string, ShipmentHold>();

            /// <summary>The record behind the last ShipmentCancelled for this shortage, or null. Public for the self-check.</summary>
            public ShipmentHold ShipmentHeldBackDetail(GameAI.GameAIOrder.OrderType transportType, string target)
                => _shipmentHeldBackDetail.TryGetValue(ShipmentKey(transportType, target), out var detail) ? detail : null;

            /// <summary>The route's planets the player sees as blockaded, with their values, for the ShipmentCancelled line.</summary>
            public string BlockedNodeSummary(IEnumerable<string> routeNodes)
            {
                if (_blockadeView == null || routeNodes == null) return "-";
                var parts = routeNodes.Where(n => _blockadeView.Value(n) > 0f)
                    .Select(n => n + "=" + _blockadeView.Value(n).ToString("0.#", System.Globalization.CultureInfo.InvariantCulture))
                    .ToList();
                return parts.Count == 0 ? "-" : string.Join(",", parts);
            }

            private void ForgetShipmentHold(string key)
            {
                _shipmentHeldBack.Remove(key);
                _shipmentHeldBackDetail.Remove(key);
            }

            private static string ShipmentKey(GameAI.GameAIOrder.OrderType transportType, string target)
                => $"{transportType}:{target}";

            /// <summary>The reason this shortage was last left unsupplied by blockades, or null. Public for the self-check.</summary>
            public string ShipmentHeldBackReason(GameAI.GameAIOrder.OrderType transportType, string target)
                => _shipmentHeldBack.TryGetValue(ShipmentKey(transportType, target), out var reason) ? reason : null;

            /// <summary>True when this is news (first turn held back, or a different reason), i.e. the caller should log it. Public for the self-check.</summary>
            public bool NoteShipmentHeldBack(GameAI.GameAIOrder.OrderType transportType, string target, string reason)
            {
                var key = ShipmentKey(transportType, target);
                if (_shipmentHeldBack.TryGetValue(key, out var last) && last == reason) return false;
                _shipmentHeldBack[key] = reason;
                return true;
            }

            // ── Entry point ──────────────────────────────────────────────────

            public void ProcessResults(
                List<Planet.UpdateResult> results,
                List<GameAI.GameAIOrder>        orders)
            {
                if (HumanControlled)
                {
                    ProcessResultsHuman(results, orders);
                    return;
                }

                // Self-checks (e.g. PlayerAIResourceSelfCheck) call this with no Gameboard in the scene.
                TryEnterConsolidate(Gameboard.Instance != null ? Gameboard.Instance.TurnNumber : 0);
                // Stances committed by last turn's orders: the Consolidate/Amass switch and the forced-war lines read them here,
                // before this turn's routine, so a new strategy takes effect on this turn's routine as it always has.
                ApplyWarState(Gameboard.Instance != null ? Gameboard.Instance.TurnNumber : 0);
                // This turn's combat losses (WarshipsLost results for my player) feed the hostility loss terms below.
                RecordLosses(results, Gameboard.Instance != null ? Gameboard.Instance.TurnNumber : 0);
                RefreshBlockadeView();

                switch (Strategy)
                {
                    case AIStrategy.AIStrategyExpand:
                    case AIStrategy.AIStrategyConsolidate:
                    case AIStrategy.AIStrategyAmass:
                        // Consolidate and Amass have no routine of their own; they play like Expand (Amass with its own
                        // weight tables, see the research and industry tables below).
                        ProcessResultsStrategyExpand(results, Player, ref orders);
                        break;
                }

                // After the routine and the blockade view. The stance decision is an order, executed with the rest in
                // ProcessNewOrders after every player has decided.
                UpdateDiplomacy(Gameboard.Instance != null ? Gameboard.Instance.TurnNumber : 0, orders);
            }

            /// <summary>
            /// The human's switch for resource shipping (food and grotsits, distribution centers included). Off by default: the human
            /// orders shipments. On, this player's shipments are planned exactly as an AI player's are. Saved with the player.
            /// </summary>
            public bool ShipmentsAutomated { get; set; }

            /// <summary>
            /// The reduced routine of a human-controlled player: the human gives the orders, so nothing here chooses ship moves,
            /// colonists or stances. What does run is the bookkeeping the human cannot do (research accounting, the strategy tables the
            /// automated parts read, combat memory, the blockade view), shipping while ShipmentsAutomated, and production for the
            /// planets switched to automatic (a manual planet is skipped in BuildIndustryMatrix).
            /// </summary>
            private void ProcessResultsHuman(
                List<Planet.UpdateResult> results,
                List<GameAI.GameAIOrder>        orders)
            {
                var turn = Gameboard.Instance != null ? Gameboard.Instance.TurnNumber : 0;
                TryEnterConsolidate(turn);
                ApplyWarState(turn);
                RecordLosses(results, turn);
                RefreshBlockadeView();

                if (ShipmentsAutomated)
                {
                    UpdateDistributionCenters(results, turn);
                    ProcessFoodShortage(results, orders);
                    ProcessGrotsitsShortage(results, orders);
                }
                ProcessResearch(results, orders);
                ProcessIndustry(results, orders);
            }

            /// <summary>
            /// One-way switch from Expand to Consolidate on first contact with another player.
            /// Public (and free of Gameboard.Instance) so the self-check can drive it directly.
            /// Returns true only on the turn the switch happens.
            /// </summary>
            public bool TryEnterConsolidate(int turnNumber)
            {
                if (Strategy != AIStrategy.AIStrategyExpand) return false;
                if (!AIMap.Knowledge.HasContact(AIMap, Player.playerID)) return false;

                Strategy = AIStrategy.AIStrategyConsolidate;
                AITuningLogger.LogStrategyChange(turnNumber, Player.playerID,
                    AIStrategy.AIStrategyExpand.ToString(), AIStrategy.AIStrategyConsolidate.ToString());
                return true;
            }

            // ── Diplomacy ────────────────────────────────────────────────────

            /// <summary>
            /// The players I am at war with right now. Diplomacy off (legacy, and any map a self-check builds directly): every
            /// player I have contact with is an enemy, as before. On: my own War stances plus the wars rivals declared on me
            /// that I have contact with (DiplomacyState.WarRivals).
            /// </summary>
            public SortedSet<int> WarRivals()
            {
                var contact = AIMap.Knowledge.ContactPlayers(AIMap, Player.playerID);
                return AIMap.Diplomacy.Enabled
                    ? AIMap.Diplomacy.WarRivals(Player.playerID, contact)
                    : new SortedSet<int>(contact);
            }

            /// <summary>What the assault may attack: null (every other player) while diplomacy is off, else the war set.</summary>
            public System.Collections.Generic.ISet<int> AssaultWarFilter() => AIMap.Diplomacy.Enabled ? WarRivals() : null;

            // Rivals whose forced war (declared on me, I have contact) was already logged as Start, so Start and End are logged
            // on a change only. Log-only state: not saved (a load logs each current forced war once more).
            private readonly HashSet<int> _warForcedLogged = new HashSet<int>();

            /// <summary>The rivals whose declaration currently forces me into war (I have not declared on them). Public for the self-check.</summary>
            public IReadOnlyCollection<int> WarForcedRivals => _warForcedLogged;

            // Per rival, what it did to my warships over the last lossWindowTurns: (turn, ships destroyed, their strength, my
            // strength engaged that turn). Player-private and not saved: a load starts the window empty (like the pending
            // blockade cuts).
            private readonly Dictionary<int, List<(int turn, float ships, float strength, float engaged)>> _losses
                = new Dictionary<int, List<(int turn, float ships, float strength, float engaged)>>();
            // Rivals whose significant-loss drop was logged as Start (log-only, so a load logs each current one once more).
            private readonly HashSet<int> _lossDropLogged = new HashSet<int>();

            /// <summary>Reads this turn's WarshipsLost results for my own player (the list is shared by every player).</summary>
            public void RecordLosses(List<Planet.UpdateResult> results, int turn)
            {
                foreach (var result in results)
                {
                    if (result.Result == Planet.UpdateResult.UpdateResultType.UpdateResultTypeConversion
                        && result.Data is ConversionEvent flip && flip.OwnerChanged && flip.OldOwner == Player.playerID)
                    {
                        if (!_planetLosses.TryGetValue(flip.NewOwner, out var taken))
                            _planetLosses[flip.NewOwner] = taken = new List<(int turn, float value)>();
                        taken.Add((turn, AIMap.GetPlanet(result.Name)?.PlanetValue ?? 0f));
                        continue;
                    }
                    if (result.Result != Planet.UpdateResult.UpdateResultType.UpdateResultTypeWarshipsLost) continue;
                    if (result.PlayerID != Player.playerID || !(result.Data is CombatLoss loss)) continue;
                    if (!_losses.TryGetValue(loss.Attacker, out var list))
                        _losses[loss.Attacker] = list = new List<(int turn, float ships, float strength, float engaged)>();
                    list.Add((turn, loss.Ships, loss.StrengthDrop, loss.Engaged));   // the share's numerator is the strength drop (damage included), not only ships destroyed
                }
            }

            /// <summary>How much of my engaged strength this rival took off me (the strength drop of every fight round, damage included) / my strength engaged against it, both totalled over the window; 0 when nothing was engaged. Prunes entries older than the window.</summary>
            public float LossShareToward(int rival, int turn)
            {
                var engaged = EngagedToward(rival, turn);
                return engaged <= 0f ? 0f : _losses[rival].Sum(e => e.strength) / engaged;
            }

            /// <summary>My strength engaged against this rival over the window (the loss share's denominator). Prunes entries older than the window.</summary>
            public float EngagedToward(int rival, int turn)
            {
                if (!_losses.TryGetValue(rival, out var list)) return 0f;
                var window = AIMap.GameAIConstants.lossWindowTurns;
                list.RemoveAll(e => turn - e.turn >= window);
                return list.Sum(e => e.engaged);
            }

            // Per rival, the planets it took from me over the last lossWindowTurns: (turn, planet value). Player-private, not saved.
            private readonly Dictionary<int, List<(int turn, float value)>> _planetLosses
                = new Dictionary<int, List<(int turn, float value)>>();

            /// <summary>
            /// What the war has cost me toward this rival over the window: the share of my fleet it destroyed (LossShareToward) x
            /// ownFleetValue, plus the value of the planets it took from me. Prunes entries older than the window.
            /// </summary>
            public float WarCostToward(int attacker, int turn, float ownFleetValue)
            {
                var cost = LossShareToward(attacker, turn) * ownFleetValue;
                if (_planetLosses.TryGetValue(attacker, out var list))
                {
                    list.RemoveAll(e => turn - e.turn >= AIMap.GameAIConstants.lossWindowTurns);
                    cost += list.Sum(e => e.value);
                }
                return cost;
            }

            /// <summary>
            /// The not-yet-researched items of a tech's prerequisite chain on this player's research catalog (the item plus every
            /// item reached through requiredTech); empty when the item is unknown or already researched.
            /// </summary>
            public List<CatalogItem> TechChain(string itemName)
                => DealEvaluator.MissingChain(ResearchCatalog != null ? ResearchCatalog.catalogItems : null, itemName);

            /// <summary>
            /// Grants a tech by deal: the item and every unresearched prerequisite become researched, the production items they unlock
            /// are flipped (as CompleteResearch does), each leaves ResearchQueue and currentResearch is cleared when it is one of them.
            /// No notifications or log. Returns the names newly granted (empty when unknown or already researched). No Gameboard.Instance.
            /// </summary>
            public List<string> GrantTech(string itemName)
            {
                var granted = new List<string>();
                foreach (var item in TechChain(itemName))
                {
                    item.researched = true;
                    granted.Add(item.itemName);
                    if (ProductionCatalog != null)
                        foreach (var dependent in ProductionCatalog.catalogItems.FindAll(x => x.requiredTech == item.itemName))
                            dependent.researched = true;
                    ResearchQueue.RemoveAll(n => n == item.itemName);
                    if (currentResearch == item) currentResearch = null;
                }
                return granted;
            }

            private float ShipsLostThisTurn(int rival, int turn)
                => _losses.TryGetValue(rival, out var list) ? list.Where(e => e.turn == turn).Sum(e => e.ships) : 0f;

            /// <summary>
            /// Once per turn, after the strategy routine: updates the hostility score toward each rival I have contact with
            /// (my own private view, written directly), lets the stance matrix decide each rival that is not held and emits an
            /// order for each decision that differs from the held stance. It writes no stance and changes no strategy: the
            /// orders are executed in ProcessNewOrders after every player has decided, and ApplyWarState reads them next
            /// turn. Does nothing while diplomacy is off. Public (and free of Gameboard.Instance) so the self-check can drive
            /// it directly.
            /// </summary>
            public void UpdateDiplomacy(int turn, List<GameAI.GameAIOrder> orders)
            {
                var diplomacy = AIMap.Diplomacy;
                if (!diplomacy.Enabled) return;

                var me = Player.playerID;
                var constants = AIMap.GameAIConstants;
                var stats = new WarshipStats(ResearchCatalog != null ? ResearchCatalog.catalogItems : null);
                var contact = AIMap.Knowledge.ContactPlayers(AIMap, me);
                var myStrength = FleetStrength.Mine(AIMap, me, stats);
                var warRivals = WarRivals();   // committed stances: a war at this moment (my own, or a forced one)

                var rows = new List<StanceMatrix.Row>();
                foreach (var rival in contact)
                {
                    var pair = diplomacy.Get(me, rival);
                    var rivalStrength = FleetStrength.VisibleOf(AIMap, me, rival, stats);
                    var nearShips = HostilityCalculator.CountNearShips(AIMap, me, rival);
                    var lossShare = LossShareToward(rival, turn);
                    LogLossDropChange(turn, rival, lossShare, constants);
                    var result = HostilityCalculator.Compute(new HostilityCalculator.Inputs
                    {
                        Previous = pair.Hostility,
                        Cuts = diplomacy.TakeCuts(me, rival),
                        Conversions = diplomacy.TakeConversions(me, rival),
                        NearShips = nearShips,
                        MyStrength = myStrength,
                        RivalStrength = rivalStrength,
                        ShipsLost = ShipsLostThisTurn(rival, turn),
                        LossShare = lossShare,
                    }, constants);
                    pair.Hostility = result.Hostility;
                    pair.CutsTerm = result.CutsTerm;
                    pair.NearTerm = result.NearTerm;
                    pair.StrengthTerm = result.StrengthTerm;
                    pair.ConversionTerm = result.ConversionTerm;
                    pair.MyStrength = myStrength;
                    pair.RivalStrength = rivalStrength;
                    pair.NearShips = nearShips;
                    pair.LossShare = lossShare;   // log-only: the per-rival share the Hostility line reports (and the Surrender line on a surrender)
                    pair.Engaged = EngagedToward(rival, turn);   // log-only: the share's denominator
                    // Log-only: how much of the hostility ship losses account for, decayed like the hostility itself and fed only by the
                    // anger per ship lost (not the significant-loss drop), so a Stance line shows whether a war came from losses.
                    pair.LossAccum = pair.LossAccum * (1f - constants.hostilityDecay) + ShipsLostThisTurn(rival, turn) * constants.hostilityPerShipLost;
                    diplomacy.Set(me, rival, pair);
                    rows.Add(new StanceMatrix.Row
                    {
                        Rival = rival,
                        Hostility = result.Hostility,
                        Current = pair.Stance,
                        TurnsSinceChange = diplomacy.TurnsSinceChange(me, rival, turn),
                        LossShare = lossShare,
                        AtWar = warRivals.Contains(rival),
                        MyStrength = myStrength,
                        RivalStrength = rivalStrength,
                        Truce = diplomacy.InTruce(me, rival, turn),
                        RivalIsHuman = IsHumanPlayer(rival),
                    });
                }
                // A war I declared on a rival I no longer have contact with must still be able to end (my own War stance counts
                // without contact): its hostility only decays (no cuts, near ships or strength term) and the matrix decides it
                // like any other row, so Amass cannot outlive a rival that is gone.
                foreach (var rival in diplomacy.Rivals(me)
                             .Where(r => !contact.Contains(r) && diplomacy.StanceToward(me, r) == Stance.War).ToList())
                {
                    var pair = diplomacy.Get(me, rival);
                    pair.Hostility = HostilityCalculator.Compute(
                        new HostilityCalculator.Inputs { Previous = pair.Hostility }, constants).Hostility;
                    pair.CutsTerm = 0f;
                    pair.NearTerm = 0f;
                    pair.StrengthTerm = 0f;
                    pair.ConversionTerm = 0f;
                    pair.LossAccum *= 1f - constants.hostilityDecay;   // no contact: it only decays, like the hostility
                    diplomacy.Set(me, rival, pair);
                    rows.Add(new StanceMatrix.Row
                    {
                        Rival = rival,
                        Hostility = pair.Hostility,
                        Current = Stance.War,
                        TurnsSinceChange = diplomacy.TurnsSinceChange(me, rival, turn),
                        LossShare = LossShareToward(rival, turn),
                        AtWar = true,
                        MyStrength = myStrength,
                        RivalStrength = 0f,   // out of contact the rival's fleet is not visible, so I am never "weaker": no surrender to it
                        Truce = diplomacy.InTruce(me, rival, turn),
                        RivalIsHuman = IsHumanPlayer(rival),
                    });
                }
                diplomacy.DiscardCuts(me);   // cuts by players I have no contact with must not pile up for later
                diplomacy.DiscardConversions(me);

                foreach (var decision in StanceMatrix.Decide(me, rows, constants))
                {
                    if (decision.Surrender)
                    {
                        // A surrender is an order too (executed in ProcessNewOrders): both stances go to Peace and the pair is locked.
                        var row = rows.First(r => r.Rival == decision.Rival);
                        var surrenderPair = diplomacy.Get(me, decision.Rival);
                        surrenderPair.LossShare = row.LossShare;                                       // read by the Surrender log line
                        surrenderPair.PSurrender = StanceMatrix.SurrenderWeight(row.LossShare, constants);
                        diplomacy.Set(me, decision.Rival, surrenderPair);
                        orders.Add(MakeOrder(GameAI.GameAIOrder.OrderType.OrderTypeSurrender,
                            GameAI.GameAIOrder.OrderTimingType.OrderTimingTypeImmediate,
                            0, 0, decision.Rival, string.Empty, string.Empty));
                        continue;
                    }
                    if (diplomacy.StanceToward(me, decision.Rival) == decision.Stance) continue;   // the held stance again: nothing to order
                    var pair = diplomacy.Get(me, decision.Rival);
                    pair.PWar = decision.PWar;   // read by GameAI.ExecuteOrder when the order logs the Stance line
                    diplomacy.Set(me, decision.Rival, pair);
                    orders.Add(MakeOrder(
                        decision.Stance == Stance.War
                            ? GameAI.GameAIOrder.OrderType.OrderTypeDeclareWar
                            : GameAI.GameAIOrder.OrderType.OrderTypeMakePeace,
                        GameAI.GameAIOrder.OrderTimingType.OrderTimingTypeImmediate,
                        0, 0, decision.Rival, string.Empty, string.Empty));
                }

            }

            // LossDrop Start/End when the loss share crosses significantLossFraction (log-only state, so no repeat every turn).
            private void LogLossDropChange(int turn, int rival, float lossShare, GameAIConstants constants)
            {
                var significant = lossShare >= constants.significantLossFraction;
                if (significant && _lossDropLogged.Add(rival))
                    AITuningLogger.LogLossDrop(turn, Player.playerID, rival, true, lossShare);
                else if (!significant && _lossDropLogged.Remove(rival))
                    AITuningLogger.LogLossDrop(turn, Player.playerID, rival, false, lossShare);
            }

            /// <summary>
            /// At the start of ProcessResults, from the stances the previous turn's orders committed: logs the wars a rival
            /// declared on me (I have contact, I did not declare) when they start and end, then switches Consolidate to Amass
            /// while I am at war with anyone and Amass back to Consolidate once I am at war with nobody. Expand and None are
            /// never switched here. Does nothing while diplomacy is off. Public (and free of Gameboard.Instance) so the
            /// self-check can drive it directly.
            /// </summary>
            public void ApplyWarState(int turn)
            {
                var diplomacy = AIMap.Diplomacy;
                if (!diplomacy.Enabled) return;
                var me = Player.playerID;

                var warRivals = WarRivals();
                var forced = new HashSet<int>(warRivals.Where(r => diplomacy.StanceToward(me, r) != Stance.War));
                foreach (var rival in forced.Where(r => !_warForcedLogged.Contains(r)).ToList())
                {
                    _warForcedLogged.Add(rival);
                    AITuningLogger.LogWarForced(turn, me, rival, true);
                }
                foreach (var rival in _warForcedLogged.Where(r => !forced.Contains(r)).ToList())
                {
                    _warForcedLogged.Remove(rival);
                    AITuningLogger.LogWarForced(turn, me, rival, false);
                }

                var atWar = warRivals.Count > 0;
                if (Strategy == AIStrategy.AIStrategyConsolidate && atWar)
                    SwitchStrategy(AIStrategy.AIStrategyAmass, turn);
                else if (Strategy == AIStrategy.AIStrategyAmass && !atWar)
                    SwitchStrategy(AIStrategy.AIStrategyConsolidate, turn);
            }

            private void SwitchStrategy(AIStrategy to, int turn)
            {
                AITuningLogger.LogStrategyChange(turn, Player.playerID, Strategy.ToString(), to.ToString());
                Strategy = to;
            }

            // ── Strategy: Expand ─────────────────────────────────────────────

            private void ProcessResultsStrategyExpand(
                List<Planet.UpdateResult> results,
                Player                          player,
                ref List<GameAI.GameAIOrder>    orders)
            {
                ProcessColonizers(results, orders);
                UpdateDistributionCenters(results, Gameboard.Instance != null ? Gameboard.Instance.TurnNumber : 0);
                ProcessFoodShortage(results, orders);
                ProcessGrotsitsShortage(results, orders);
                ProcessResearch(results, orders);
                ProcessIndustry(results, orders);
                ProcessShipActions(orders, results);
            }

            // ── Colonization ─────────────────────────────────────────────────
            private bool PlanetHasColonyShip(string planetName)
            {
                var planet = AIMap.GetPlanet(planetName);
                if (planet != null)
                {
                    return planet.HasDockedShip(Ship.ShipKind.ColonyShip);
                }
                return false;
            }

            private bool PlanetCanColonize(string planetName)
                => IsValidColonizer(planetName) && PlanetHasColonizationTarget(planetName);

            /// <summary>
            /// A known, valid colonization target is within reach of this planet, whether or not the planet is
            /// ready to colonize yet. Drives whether a planet should be building colony ships at all.
            /// </summary>
            private bool PlanetHasColonizationTarget(string planetName)
            {
                var origin = AIMap.GetPlanet(planetName);
                var maxNodes = AIMap.GameAIConstants.maxPathNodesForColonization;
                return origin.DistanceMapToPathingList.Any(t =>
                    t.Value.NumNodes <= maxNodes
                    && IsValidColonizationTarget(AIMap.GetPlanet(t.Key))
                    && CanSupportColony(origin, AIMap.GetPlanet(t.Key))
                    // a route must exist that avoids the planets I can see are blockaded (the planner also rejects the
                    // 1-node no-route stub)
                    && RoutePlanner.PlanRoute(AIMap, planetName, t.Key, _blockadeView, maxNodes) != null);
            }

            /// <summary>
            /// A target that cannot feed a colonist by itself (Planet.NeedsColonyFoodRider) is only viable from an
            /// origin that can pay the colony ship's food rider; any other target needs nothing. Public for the
            /// self-check.
            /// </summary>
            public bool CanSupportColony(Planet origin, Planet target)
                => !target.NeedsColonyFoodRider || origin.Food >= AIMap.GameAIConstants.colonyFoodRider;

            // ── Distribution Centers ────────────────────────────────────────

            public List<string> FoodDistributionCenters { get; private set; } = new List<string>();
            public List<string> GrotsitsDistributionCenters { get; private set; } = new List<string>();

            /// <summary>The player's own surplus-reporting planets as of the last UpdateDistributionCenters
            /// call — one turn stale by the time an order executes, since order execution runs before this
            /// turn's UpdateResults exist. Used by IsCoverageGap (Task 7).</summary>
            public List<string> LastFoodSurplusPlanets { get; private set; } = new List<string>();
            public List<string> LastGrotsitsSurplusPlanets { get; private set; } = new List<string>();

            /// <summary>Restores a saved DC list; a missing/older field passes an empty list here, i.e.
            /// "no DC yet, select fresh." Public for SaveLoadSystem/GameBoard restore.</summary>
            public void SetDistributionCenters(string resource, List<string> names)
            {
                if (resource == "Food") FoodDistributionCenters = names ?? new List<string>();
                else if (resource == "Grotsits") GrotsitsDistributionCenters = names ?? new List<string>();
            }

            /// <summary>
            /// Sticky, per-resource DC selection and pruning, once per turn. Public and free of
            /// Gameboard.Instance so the self-check can drive it directly.
            /// </summary>
            public void UpdateDistributionCenters(List<Planet.UpdateResult> results, int turnNumber)
            {
                // Keep the lists as they stood before this turn's update, so another player's decision this turn can read them (below).
                _dcBeforeUpdate = new List<string>(FoodDistributionCenters);
                _dcBeforeUpdate.AddRange(GrotsitsDistributionCenters);
                _dcUpdatedTurn = turnNumber;
                LastFoodSurplusPlanets = results
                    .Where(x => x.PlayerID == Player.playerID
                             && x.Result == Planet.UpdateResult.UpdateResultType.UpdateResultTypeFoodSurplus)
                    .Select(x => x.Name).ToList();
                LastGrotsitsSurplusPlanets = results
                    .Where(x => x.PlayerID == Player.playerID
                             && x.Result == Planet.UpdateResult.UpdateResultType.UpdateResultTypeGrotsitsSurplus)
                    .Select(x => x.Name).ToList();

                UpdateDistributionCentersForResource(turnNumber, "Food", LastFoodSurplusPlanets, FoodDistributionCenters);
                UpdateDistributionCentersForResource(turnNumber, "Grotsits", LastGrotsitsSurplusPlanets, GrotsitsDistributionCenters);
            }

            private List<string> _dcBeforeUpdate = new List<string>();
            private int _dcUpdatedTurn = -1;

            /// <summary>
            /// Every DC (food and grotsits) as it stood at the start of `turnNumber`: the pre-update lists when this player's update has
            /// already run this turn, else the current ones. Another player's decision reads this, so the result does not depend on
            /// who is processed first (nothing one player writes this turn is read by another's decision the same turn).
            /// </summary>
            public IEnumerable<string> DistributionCentersAtTurnStart(int turnNumber)
                => _dcUpdatedTurn == turnNumber ? _dcBeforeUpdate : FoodDistributionCenters.Concat(GrotsitsDistributionCenters);

            private void UpdateDistributionCentersForResource(
                int turnNumber, string resource, List<string> producers, List<string> current)
            {
                for (var i = current.Count - 1; i >= 0; i--)
                {
                    var planet = AIMap.GetPlanet(current[i]);
                    if (planet != null && planet.Owner == Player.playerID && planet.Population.Count > 0) continue;
                    AITuningLogger.LogDCLost(turnNumber, Player.playerID, current[i], resource);
                    current.RemoveAt(i);
                }

                var allowedSlots = AllowedDistributionCenterSlots();
                if (current.Count >= allowedSlots) return;

                var colonized = AIMap.PlanetList.Where(p => p.Owner == Player.playerID && p.Population.Count > 0).ToList();
                var candidates = colonized.Where(p => !current.Contains(p.PlanetName)).ToList();

                // Treat already-selected DCs as producers for scoring purposes (empty on the first slot, so
                // this only affects a second+ slot): SelectDistributionCenter excludes anything a producer
                // already reaches from a candidate's coverage score, so without this a second DC could score
                // just as well sitting redundantly next to the first as it would reaching genuinely new
                // territory -- this makes it correctly score near zero instead, pushing selection outward.
                var scoringProducers = producers.Union(current).ToList();
                var chosen = SelectDistributionCenter(candidates, colonized, scoringProducers, current);
                if (chosen == null) return;

                current.Add(chosen);
                AITuningLogger.LogDCSelected(turnNumber, Player.playerID, chosen, resource);
            }

            /// <summary>
            /// 0 below minPlanetsForDistributionCenters, 1 below minPlanetsForSecondDistributionCenter, else
            /// 2. Counts colonized planets on the WHOLE BOARD (every player), matching the topology probe
            /// this design is based on — not this player's own colonized count.
            /// </summary>
            private int AllowedDistributionCenterSlots()
            {
                var totalColonized = AIMap.TotalColonizedPlanetCount();
                if (totalColonized < AIMap.GameAIConstants.minPlanetsForDistributionCenters) return 0;
                return totalColonized < AIMap.GameAIConstants.minPlanetsForSecondDistributionCenter ? 1 : 2;
            }

            /// <summary>
            /// Coverage-maximizing DC candidate selection. A candidate must be reachable
            /// (NumNodes &lt;= maxPathNodesForResourceDistribution) from at least one producer to be
            /// eligible at all. Scored by how many OTHER colonized, non-producer planets it would newly
            /// reach that no producer already reaches; tied scores are broken by being farthest (in
            /// NumNodes) from the nearest already-selected DC for this resource (so a second+ slot that
            /// ties on coverage prefers spreading out over clustering near an existing DC), then by being
            /// furthest from whichever producer supplies the candidate, then by the cheapest path cost to
            /// that producer. Returns null if no candidate is reachable from any producer.
            /// </summary>
            private string SelectDistributionCenter(
                List<Planet> candidates, List<Planet> allColonized, List<string> producers, List<string> existingDCs)
            {
                string best = null;
                var bestScore = -1;
                var bestDcDistance = -1;
                var bestSupplyDistance = -1;
                var bestSupplyCost = float.MaxValue;
                var maxNodes = AIMap.GameAIConstants.maxPathNodesForResourceDistribution;

                // A path's NumNodes lower bound is 2 (a real 2-planet-minimum route), matching the
                // reachability convention already established by ShipTransportPlanner.IsUsablePath and
                // AssaultPlanner.IsUsablePath: PathingSystem.FindPath does not throw when no route
                // exists, it returns a 1-node, zero-cost stub (ConstructPath's while loop never runs
                // because the destination's ParentName was never set), which must not be read as a free
                // adjacent trip. GameAIMap precomputes DistanceMapToPathingList for every pair
                // unconditionally, including disconnected ones, so this check is required here too.
                bool IsUsablePath(GameAIMap.DestinationToPathingListEntry entry)
                    => entry.NumNodes >= 2 && entry.NumNodes <= maxNodes;

                foreach (var candidate in candidates)
                {
                    var pathMap = candidate.DistanceMapToPathingList;

                    var supplyingProducers = producers
                        .Where(p => pathMap.ContainsKey(p) && IsUsablePath(pathMap[p]))
                        .ToList();
                    if (supplyingProducers.Count == 0) continue;

                    var supplyDistance = supplyingProducers.Max(p => pathMap[p].NumNodes);
                    var supplyCost = supplyingProducers.Min(p => pathMap[p].Cost);

                    // Hops to the nearest already-selected DC for this resource. int.MaxValue (and so,
                    // via the "farther is better" comparison below, treated as the best possible value)
                    // when there are no existing DCs yet, or none are within range -- this tie-break is
                    // inert for the first slot and never penalizes a candidate outside every existing DC's
                    // reach, since that candidate is unambiguously independent of them.
                    var dcDistance = existingDCs
                        .Where(dc => pathMap.ContainsKey(dc) && IsUsablePath(pathMap[dc]))
                        .Select(dc => pathMap[dc].NumNodes)
                        .DefaultIfEmpty(int.MaxValue)
                        .Min();

                    var score = 0;
                    foreach (var other in allColonized)
                    {
                        if (other.PlanetName == candidate.PlanetName) continue;
                        if (producers.Contains(other.PlanetName)) continue; // a producer trivially reaches itself
                        if (!pathMap.ContainsKey(other.PlanetName) || !IsUsablePath(pathMap[other.PlanetName])) continue;

                        var otherPathMap = other.DistanceMapToPathingList;
                        var reachedByProducer = producers.Any(p =>
                            otherPathMap.ContainsKey(p) && IsUsablePath(otherPathMap[p]));
                        if (reachedByProducer) continue;

                        score++;
                    }

                    var better = score > bestScore
                        || (score == bestScore && dcDistance > bestDcDistance)
                        || (score == bestScore && dcDistance == bestDcDistance && supplyDistance > bestSupplyDistance)
                        || (score == bestScore && dcDistance == bestDcDistance && supplyDistance == bestSupplyDistance
                            && supplyCost < bestSupplyCost);
                    if (!better) continue;

                    best = candidate.PlanetName;
                    bestScore = score;
                    bestDcDistance = dcDistance;
                    bestSupplyDistance = supplyDistance;
                    bestSupplyCost = supplyCost;
                }

                return best;
            }

            /// <summary>
            /// True when the given planet is unreachable, for one resource, from EVERY planet in
            /// lastKnownSurplusPlanets AND every planet in distributionCenters — i.e. sticky DC selection
            /// isn't giving this planet any path to get resupplied. Uses the CACHED last-known surplus set
            /// (LastFoodSurplusPlanets/LastGrotsitsSurplusPlanets), not this turn's live results, because
            /// order execution (where this is called from) runs before this turn's UpdateResults
            /// exist — one turn stale, acceptable for a diagnostic. Public and free of Gameboard.Instance
            /// so the self-check can drive it directly.
            /// </summary>
            public bool IsCoverageGap(Planet planet, List<string> lastKnownSurplusPlanets, List<string> distributionCenters)
            {
                var maxNodes = AIMap.GameAIConstants.maxPathNodesForResourceDistribution;
                var pathMap = planet.DistanceMapToPathingList;
                // NumNodes >= 2: PathingSystem.FindPath returns a 1-node, zero-cost stub for an
                // unreachable destination rather than throwing (see SelectDistributionCenter's
                // IsUsablePath for the full explanation) — a bare "<= maxNodes" would misread that stub
                // as reachable and never report a real gap.
                return !lastKnownSurplusPlanets.Concat(distributionCenters)
                    .Any(s => pathMap.ContainsKey(s) && pathMap[s].NumNodes >= 2 && pathMap[s].NumNodes <= maxNodes);
            }

            private bool IsValidColonizer(string planetName)
            {
                var planet = AIMap.GetPlanet(planetName);
                if (planet.Owner == Player.playerID &&
                    planet.Population.Count >= planet.MaxPopulation
                    * AIMap.GameAIConstants.expandPopulationTrigger)
                    return true;
                
                return false;
            }
            // Public for the FlatSpace/AI self-check (Assets/Editor is a separate assembly).
            public bool IsValidColonizationTarget(Planet planet) => IsValidColonizationTarget(planet, true);

            /// <summary>
            /// The same rule; requireKnown false skips the knowledge test, for the human's Planets screen, which lists only planets the human has seen
            /// (the fog) and must not wait for the AI's knowledge to be updated by a turn.
            /// </summary>
            public bool IsValidColonizationTarget(Planet planet, bool requireKnown)
            {
                if (requireKnown && !AIMap.Knowledge.IsKnown(Player.playerID, planet.PlanetName)) return false;
                if (planet.IsPopulationTransferInProgress(Player.playerID))       return false;
                if (planet.Population.Count == 0)                                return true;
                if (planet.Population.Count >= planet.MaxPopulation)             return false;
                if (planet.PlayerWithMostPopulation() != Player.playerID)        return true;
                return IsConversionColonizeTarget(planet);   // I hold the plurality but a conversion is still running
            }

            /// <summary>
            /// A planet I dominate where I already have an inhabitant, below max population, with a foreign inhabitant left: a colonist
            /// lands as my inhabitant (it needs no conversion and raises my share, so it shortens every later flip). Without dominance
            /// the AI does not colonize its own healthy planets. Public for the self-check.
            /// </summary>
            public bool IsConversionColonizeTarget(Planet planet)
                => planet.Population.Exists(p => p.Player == Player.playerID) && IsConversionTarget(planet);

            /// <summary>
            /// A planet I dominate where a conversion runs or could start: below max population, a foreign inhabitant left, and either
            /// my own session is running or a player I am at war with holds an inhabitant (without one nothing can ever convert there,
            /// so a colonist would only fill my own planet). Needs no inhabitant of mine: the earliest stage of a conversion gets the
            /// biggest tilt. Public for the self-check.
            /// </summary>
            public bool IsConversionTarget(Planet planet)
            {
                if (!AIMap.Diplomacy.Enabled || planet.Population.Count >= planet.MaxPopulation) return false;
                if (ConversionSystem.HasStuckBunker(planet, Player.playerID, AIMap.Diplomacy)) return false;   // bunkers I will never bombard away
                if (!planet.Population.Exists(p => p.Player != Player.playerID)) return false;
                if (planet.ConversionBy != Player.playerID
                    && !planet.Population.Exists(p => p.Player != Player.playerID && AIMap.Diplomacy.IsAtWar(Player.playerID, p.Player, true)))
                    return false;
                var stats = new WarshipStats(ResearchCatalog != null ? ResearchCatalog.catalogItems : null);
                return ConversionSystem.Dominator(planet, AIMap, stats) == Player.playerID;
            }

            /// <summary>
            /// The colonization choice cost of a dominated target is divided by 1 + conversionColonizeWeight x (1 - my share of its
            /// inhabitants): a colonist is worth most early in a conversion and least on a planet that is nearly mine. 1 for any other
            /// planet and when the weight is 0 or below. Public for ColonizationCostDivisor, ColonistRedirect and the self-check.
            /// </summary>
            public float ConversionColonizeDivisor(string targetName)
            {
                var weight = AIMap.GameAIConstants.conversionColonizeWeight;
                if (weight <= 0f) return 1f;
                var planet = AIMap.GetPlanet(targetName);
                if (planet == null || !IsConversionTarget(planet)) return 1f;
                var share = (float)planet.Population.Count(p => p.Player == Player.playerID) / planet.Population.Count;
                return 1f + weight * (1f - share);
            }
            // The wider target set for a colonist already in flight (see ColonistRedirect): known, and empty, or my
            // colonist is already inbound, or colonized below max population (my own planets included). A full planet is
            // excluded unless my colonist is already inbound (then it is a candidate and the arrival rule decides: dock on my
            // own planet, join a contested one). Ordinary launches keep IsValidColonizationTarget. Public for the self-check.
            public bool IsDiversionTarget(Planet planet)
            {
                if (!AIMap.Knowledge.IsKnown(Player.playerID, planet.PlanetName)) return false;
                if (planet.Population.Count == 0)                                return true;
                if (planet.IsPopulationTransferInProgress(Player.playerID))       return true;
                return planet.Population.Count < planet.MaxPopulation;
            }
            // Consolidate and Amass tilt colonization toward chokepoints: a target's choice cost is its route cost divided by
            // 1 + colonizationChokepointWeight x its chokepoint percentile, so a hub may be farther and still win. Expand,
            // a weight of 0 (or below) and unknown planets leave it at 1 (nearest first). The order delay never uses it.
            // Public for ColonistRedirect (diversion choice) and the self-check.
            public float ColonizationCostDivisor(string targetName) => ChokepointDivisor(targetName) * ConversionColonizeDivisor(targetName);

            // The Consolidate/Amass chokepoint tilt alone (the ChokepointColonize log asks only about this part).
            private float ChokepointDivisor(string targetName)
            {
                if (!IsConsolidateLike(Strategy)) return 1f;
                var weight = AIMap.GameAIConstants.colonizationChokepointWeight;
                return weight <= 0f ? 1f : 1f + weight * AIMap.Chokepoint(targetName);
            }

            // ── Escorted colonists (Task 15, escort option B) ─────────────────

            // The escorts emitted this turn (one ShipAction each, as EmitShipOrders sent it). ProcessColonizers runs first in the routine
            // (ProcessResultsStrategyExpand: ProcessColonizers, ..., ProcessShipActions) and fills it; PlanShipActions hands it to the
            // planners BEFORE they plan (ShipTransportPlanner.Reserved, AssaultPlanner.PreDeparting), so sizing, the siegeship gate's
            // projection and the emitted actions all see the escort's ships as gone; ProcessShipActions starts its claims from it and
            // clears it. Nothing undocks until the orders execute, so without it the escort's ships would be sent a second time.
            private readonly List<ShipAction> _escortActions = new List<ShipAction>();

            /// <summary>The escorts' warships per origin (the planners' Reserved map). Empty when no escort left this turn.</summary>
            private Dictionary<string, int> EscortReserved()
            {
                var reserved = new Dictionary<string, int>();
                foreach (var escort in _escortActions)
                {
                    if (escort.Kind != Ship.ShipKind.WarShip) continue;
                    reserved.TryGetValue(escort.Origin, out var count);
                    reserved[escort.Origin] = count + escort.Count;
                }
                return reserved;
            }

            // Log-only: declined escorts, reported on change only (EscortDetour).
            private readonly EscortDetourTracker _escortDetours = new EscortDetourTracker();

            /// <summary>The EscortDetour entries the last ProcessColonizers reported (log-only; public for the self-check).</summary>
            public List<EscortDetourTracker.Entry> LastEscortDetourChanges { get; private set; } = new List<EscortDetourTracker.Entry>();

            /// <summary>The escort ships ProcessColonizers claimed this turn per origin (warships), not yet consumed by ProcessShipActions. Public for the self-check.</summary>
            public int EscortClaimed(string origin)
                => EscortReserved().TryGetValue(origin, out var claimed) ? claimed : 0;

            /// <summary>
            /// Spare warships per planet for an escort: the garrison planner's states built the way PlanShipActions builds them (the
            /// retreat cooldown; under Consolidate/Amass the contested holds and the conversion keep), without the assault target, which is
            /// never a colonizing origin (it is enemy-occupied or blockaded).
            /// </summary>
            private Dictionary<string, int> EscortSpareShips(int turn, WarshipStats stats)
            {
                var planner = new ShipTransportPlanner(AIMap, Player.playerID, Strategy) { RefillBlocked = CooldownPlanets(turn) };
                if (IsConsolidateLike(Strategy))
                {
                    var assault = new AssaultPlanner(AIMap, Player.playerID, _blockadeView, stats, _blockadeMemory, turn, AssaultWarFilter());
                    planner.HeldPlanets = assault.ContestedHolds();
                    planner.ConversionKeep = assault.ConversionKeep();
                }
                var spare = new Dictionary<string, int>();
                foreach (var state in planner.BuildStates()) spare[state.Planet.PlanetName] = state.Spare;
                return spare;
            }

            /// <summary>
            /// The escort a colonist needs on `route` (normally the shortest route): needed offense = the largest blockade value my view
            /// knows along it (after the origin) x (1 + blockadeBreakMargin), and at least one ship when the target is a planet I dominate
            /// (IsConversionTarget). The ships are the origin's existing SPARE warships only (no production request), taken in dock
            /// order (the ones EmitShipOrders sends) until the offense covers the need. Null when nothing is needed (a clean route to a
            /// planet I do not dominate) or the spare ships fall short. `spareByOrigin` is ProcessColonizers' once-per-turn cache; null
            /// builds it. Public for the self-check.
            /// </summary>
            public (int ships, float offense)? PlanEscort(Planet origin, Planet target, RoutePlanner.PlannedRoute shortest, WarshipStats stats,
                IDictionary<string, int> spareByOrigin = null)
            {
                if (origin == null || target == null || shortest == null) return null;
                var needed = RoutePlanner.MaxBlockadeValue(shortest, _blockadeView) * (1f + AIMap.GameAIConstants.blockadeBreakMargin);
                var minShips = IsConversionTarget(target) ? 1 : 0;
                if (needed <= 0f && minShips == 0) return null;

                stats = stats ?? new WarshipStats(ResearchCatalog != null ? ResearchCatalog.catalogItems : null);
                var spares = spareByOrigin
                             ?? EscortSpareShips(Gameboard.Instance != null ? Gameboard.Instance.TurnNumber : 0, stats);
                spares.TryGetValue(origin.PlanetName, out var spare);

                var ships = 0;
                var offense = 0f;
                foreach (var ship in origin.DockedShips)
                {
                    if (ship.Kind != Ship.ShipKind.WarShip || ship.Owner != Player.playerID) continue;
                    if (ships >= minShips && offense >= needed) break;
                    if (ships >= spare) break;
                    ships++;
                    offense += stats.EffectiveOffense(ship);
                }
                if (ships < minShips || offense < needed) return null;
                return (ships, offense);
            }

            // Public for the FlatSpace/AI self-check (Assets/Editor is a separate assembly).
            public void ProcessColonizers(
                List<Planet.UpdateResult> results,
                List<GameAI.GameAIOrder>        orders)
            {
                _escortActions.Clear();   // a new turn's escorts (ProcessShipActions consumes them later in this routine)
                var colonizers = results.FindAll(
                    x => x.PlayerID == Player.playerID
                         && x.Result == Planet.UpdateResult.UpdateResultType.UpdateResultTypeColonizerReady);
                if (colonizers.Count == 0)
                {
                    _colonizeHeldBack.Clear();   // nobody is ready, so nobody is being held back
                    LastEscortDetourChanges = _escortDetours.Update(new List<EscortDetourTracker.Entry>());
                    return;
                }

                var targets = AIMap.PlanetList.FindAll(IsValidColonizationTarget);
                if (targets.Count == 0)
                {
                    _colonizeHeldBack.Clear();   // nothing to colonize: not held back by a blockade
                    LastEscortDetourChanges = _escortDetours.Update(new List<EscortDetourTracker.Entry>());
                    return;
                }

                // A planet that is no longer a ready colonizer is no longer held back.
                foreach (var heldBackPlanet in _colonizeHeldBack.Keys.ToList())
                    if (!colonizers.Exists(c => c.Name == heldBackPlanet))
                        _colonizeHeldBack.Remove(heldBackPlanet);

                var turn = Gameboard.Instance != null ? Gameboard.Instance.TurnNumber : 0;
                var maxNodes = AIMap.GameAIConstants.maxPathNodesForColonization;
                // The route planned for each (origin, target) choice; the chosen one rides on the colonist order below.
                var plannedRoutes = new Dictionary<(string origin, string target), RoutePlanner.PlannedRoute>();
                // Per origin, the candidate with the lowest route cost, so the log shows when the chokepoint tilt moved the pick.
                var nearest = new Dictionary<string, (string target, float cost)>();
                // The escort planned for each (origin, target) choice whose route is escorted (Task 15); built lazily, once per call.
                var plannedEscorts = new Dictionary<(string origin, string target), (int ships, float offense)>();
                var declinedEscorts = new List<EscortDetourTracker.Entry>();
                WarshipStats escortStats = null;
                Dictionary<string, int> escortSpares = null;

                var matrix = new ScoreMatrix<ScoreMatrixDecisionElement, ScoreMatrixChoiceElement, ScoreMatrixAction>
                    (new ScoreMatrixDecisionComparer());
                int colonizerIndex = 0;
                foreach (var colonizer in colonizers)
                {
                    // A blockaded planet keeps its colony ship until the blockade lifts.
                    if (IsBlockaded(colonizer.Name))
                    {
                        if (NoteColonizeHeldBack(colonizer.Name, "BlockadedOrigin"))
                            AITuningLogger.LogColonizeCancelled(turn, Player.playerID, colonizer.Name, "BlockadedOrigin");
                        continue;
                    }

                    var colonizerPlanet = AIMap.GetPlanet(colonizer.Name);
                    var pathMap = colonizerPlanet.DistanceMapToPathingList;
                    var entries = new List<ScoreMatrixChoiceElement>();
                    var hadCandidate = false;
                    foreach (var t in targets)
                    {
                        if (!pathMap.ContainsKey(t.PlanetName)
                            || pathMap[t.PlanetName].NumNodes > maxNodes
                            || pathMap[t.PlanetName].NumNodes < 2   // FindPath's 1-node no-route stub is not a candidate
                            || !CanSupportColony(colonizerPlanet, t))
                            continue;
                        hadCandidate = true;

                        // Avoid planets I can see are blockaded; a target with no such route is dropped.
                        var route = RoutePlanner.PlanRoute(AIMap, colonizer.Name, t.PlanetName, _blockadeView, maxNodes);

                        // Escort (Task 15): a dominated target, or a shortest route that crosses a known blockade (the target
                        // included). The escorted shortest route and the detour are the two ways to this target; the choice
                        // carries the cheaper one that is possible (see the report: the matrix's greedy pick within a row always
                        // takes the cheaper of two same-target entries, so one entry at that cost is the same decision).
                        var shortest = RoutePlanner.ShortestRoute(AIMap, colonizer.Name, t.PlanetName, maxNodes);
                        var shortestBlockade = RoutePlanner.MaxBlockadeValue(shortest, _blockadeView);
                        var dominated = shortest != null && IsConversionTarget(t);
                        (int ships, float offense)? escort = null;
                        if (shortest != null && (dominated || shortestBlockade > 0f))
                        {
                            escortStats = escortStats ?? new WarshipStats(ResearchCatalog != null ? ResearchCatalog.catalogItems : null);
                            if (escortSpares == null)
                            {
                                escortSpares = EscortSpareShips(turn, escortStats);
                                // A retreat has priority over an escort: retreats only leave planets with a fight projection of mine
                                // this turn (RetreatPlanner reads those, later in this routine), so such an origin has no spare for an
                                // escort, and no ship is ever both retreated and escorted.
                                foreach (var fight in results.Where(r => r.PlayerID == Player.playerID
                                             && r.Result == Planet.UpdateResult.UpdateResultType.UpdateResultTypeFightProjection))
                                    escortSpares[fight.Name] = 0;
                            }
                            if (shortestBlockade <= 0f)
                            {
                                // A dominated target on a clean route: the planned route already is the shortest; one ship escorts.
                                escort = PlanEscort(colonizerPlanet, t, shortest, escortStats, escortSpares);
                            }
                            else if (route == null || shortest.Cost < route.Cost)
                            {
                                escort = PlanEscort(colonizerPlanet, t, shortest, escortStats, escortSpares);
                                if (escort != null)
                                {
                                    route = shortest;
                                }
                                else
                                {
                                    escortSpares.TryGetValue(colonizer.Name, out var spareHere);
                                    var inFight = results.Exists(r => r.PlayerID == Player.playerID && r.Name == colonizer.Name
                                        && r.Result == Planet.UpdateResult.UpdateResultType.UpdateResultTypeFightProjection);
                                    declinedEscorts.Add(new EscortDetourTracker.Entry
                                    {
                                        Origin = colonizer.Name,
                                        Target = t.PlanetName,
                                        Reason = inFight ? EscortDetourTracker.ReasonFight
                                            : spareHere <= 0 ? EscortDetourTracker.ReasonNoSpare : EscortDetourTracker.ReasonShortOffense,
                                    });
                                    // Too few spare ships to cover the blockade: the detour (if any). A dominated target still
                                    // gets its one-ship escort along the clean detour.
                                    if (dominated && route != null)
                                        escort = PlanEscort(colonizerPlanet, t, route, escortStats, escortSpares);
                                }
                            }
                        }

                        if (route == null) continue;
                        plannedRoutes[(colonizer.Name, t.PlanetName)] = route;
                        if (escort != null) plannedEscorts[(colonizer.Name, t.PlanetName)] = escort.Value;
                        if (!nearest.TryGetValue(colonizer.Name, out var best) || route.Cost < best.cost
                            || (route.Cost == best.cost && string.CompareOrdinal(t.PlanetName, best.target) < 0))
                            nearest[colonizer.Name] = (t.PlanetName, route.Cost);
                        entries.Add(new ScoreMatrixChoiceElement
                        {
                            Surplus  = 1.0f,
                            Target   = t.PlanetName,
                            Cost     = route.Cost / ColonizationCostDivisor(t.PlanetName),
                            Shortage = 1.0f,
                        });
                    }

                    if (entries.Count == 0 && hadCandidate)
                    {
                        if (NoteColonizeHeldBack(colonizer.Name, "NoRoute"))
                            AITuningLogger.LogColonizeCancelled(turn, Player.playerID, colonizer.Name, "NoRoute");
                    }
                    else
                    {
                        _colonizeHeldBack.Remove(colonizer.Name);   // it launches (or has nothing to colonize): no longer held back
                    }

                    if (entries.Count > 0)
                    {
                        
                            matrix.MatrixElements.Add(
                                new ScoreMatrixDecisionElement
                                {
                                    Target = colonizer.Name,
                                    Priority = 0f
                                }, entries);
                        colonizerIndex++;
                    }
                }

                LastEscortDetourChanges = _escortDetours.Update(declinedEscorts);
                foreach (var declined in LastEscortDetourChanges)
                    AITuningLogger.LogEscortDetour(turn, Player.playerID, declined.Origin, declined.Target, declined.Reason);

                foreach (var action in matrix.GenerateActionList(
                             actionFactory: (origin, element) => new ScoreMatrixAction
                             {
                                 Origin = origin.Target,
                                 Target = element.Target,
                                 Cost = element.Cost
                             }, null))
                {
                    var amount = Convert.ToInt32(
                        colonizers.Find(x => x.Name == action.Origin).Data);
                    var route  = plannedRoutes[(action.Origin, action.Target)];
                    // The real route cost: action.Cost may carry the chokepoint tilt, which must not shorten the flight.
                    var delay  = Convert.ToInt32(
                        route.Cost / AIMap.GameAIConstants.defaultTravelSpeed);
                    if (nearest.TryGetValue(action.Origin, out var near) && near.target != action.Target
                        && ChokepointDivisor(action.Target) > 1f)
                        AITuningLogger.LogChokepointColonize(turn, Player.playerID, action.Origin, action.Target,
                            route.Cost, AIMap.Chokepoint(action.Target), near.target, near.cost);
                    var launchTarget = AIMap.GetPlanet(action.Target);
                    if (launchTarget != null && IsConversionTarget(launchTarget))
                    {
                        nearest.TryGetValue(action.Origin, out var nearTarget);
                        AITuningLogger.LogConversionColonize(turn, Player.playerID, action.Origin, action.Target,
                            launchTarget.Population.Count(p => p.Player == Player.playerID), launchTarget.Population.Count,
                            route.Cost, nearTarget.target ?? action.Target, nearTarget.target == null ? route.Cost : nearTarget.cost);
                    }

                    var escortOffense = 0f;
                    // The escort: the origin's spare warships fly the same route as the ordinary ship-transport trio, launched this
                    // turn; the colonist records their offense, which BlockadeSystem subtracts at each node. On arrival they are
                    // ordinary docked warships. The colonist does not depend on them (nothing in flight is attacked).
                    if (plannedEscorts.TryGetValue((action.Origin, action.Target), out var escortPlan))
                    {
                        var before = orders.Count;
                        var escortAction = new ShipAction
                        {
                            Origin = action.Origin,
                            Target = action.Target,
                            Cost   = route.Cost,
                            Count  = escortPlan.ships,
                            Kind   = Ship.ShipKind.WarShip,
                        };
                        EmitShipOrders(escortAction, orders, EscortClaimed(action.Origin));
                        if (orders.Count > before)
                        {
                            escortOffense = escortPlan.offense;
                            _escortActions.Add(escortAction);
                            AITuningLogger.LogEscortLaunch(turn, Player.playerID, action.Origin, action.Target, escortPlan.offense,
                                RoutePlanner.MaxBlockadeValue(route, _blockadeView));
                        }
                    }
                    EmitColonistOrders(action.Origin, action.Target, amount, delay, route, escortOffense, orders);
                    if (route.IsDetour)
                        AITuningLogger.LogRouteDetour(turn, Player.playerID, action.Origin, action.Target,
                            route.Nodes, route.Cost);
                }
            }

            /// <summary>
            /// Emits the orders of one colonization launch: the delayed colonist (carrying its planned route and any escort offense), the
            /// immediate deduction at the origin, the target's "colonist incoming" flag, the colony ship's removal and, when the target
            /// cannot feed a colonist, the food rider pair. Shared by ProcessColonizers and the human Colonize command; the caller
            /// decides the delay (the AI keeps its own rounding) and any escort.
            /// </summary>
            public void EmitColonistOrders(string origin, string target, int amount, int delay,
                RoutePlanner.PlannedRoute route, float escortOffense, List<GameAI.GameAIOrder> orders)
            {
                var colonist = MakeOrder(GameAI.GameAIOrder.OrderType.OrderTypePopulationTransport,
                    GameAI.GameAIOrder.OrderTimingType.OrderTimingTypeDelayed,
                    delay, delay, amount, origin, target);
                colonist.Route = new List<string>(route.Nodes);
                colonist.EscortOffense = escortOffense;
                orders.Add(colonist);

                orders.Add(MakeOrder(GameAI.GameAIOrder.OrderType.OrderTypePopulationChange,
                    GameAI.GameAIOrder.OrderTimingType.OrderTimingTypeImmediate,
                    0, 0, amount * -1.0f, origin, origin));

                orders.Add(MakeOrder(GameAI.GameAIOrder.OrderType.OrderTypePopulationTransferInProgress,
                    GameAI.GameAIOrder.OrderTimingType.OrderTimingTypeImmediate,
                    0, 0, amount, origin, target));

                orders.Add(MakeOrder(GameAI.GameAIOrder.OrderType.OrderTypeRemoveShip,
                    GameAI.GameAIOrder.OrderTimingType.OrderTimingTypeImmediate,
                    0, 0, 1, origin, origin));

                // A target that cannot feed a colonist gets a food rider as its own order: paid by the origin
                // now (an existing food change), delivered with the colonist (same delay), and unrelated to the
                // food shipping system. It bridges the gap until a shortage can be answered; it does not
                // guarantee survival.
                if (AIMap.GetPlanet(target).NeedsColonyFoodRider)
                {
                    var rider = AIMap.GameAIConstants.colonyFoodRider;
                    orders.Add(MakeOrder(GameAI.GameAIOrder.OrderType.OrderTypeColonyFoodRider,
                        GameAI.GameAIOrder.OrderTimingType.OrderTimingTypeDelayed,
                        delay, delay, rider, origin, target));
                    orders.Add(MakeOrder(GameAI.GameAIOrder.OrderType.OrderTypeFoodChange,
                        GameAI.GameAIOrder.OrderTimingType.OrderTimingTypeImmediate,
                        0, 0, rider * -1.0f, origin, origin));
                }
            }

            // ── Food ─────────────────────────────────────────────────────────

            // public for the self-check
            public void ProcessFoodShortage(
                List<Planet.UpdateResult> results,
                List<GameAI.GameAIOrder>        orders)
            {
                ProcessResourceShipments(results,
                    Planet.UpdateResult.UpdateResultType.UpdateResultTypeFoodShortage,
                    Planet.UpdateResult.UpdateResultType.UpdateResultTypeFoodSurplus,
                    incomingCheck: name => AIMap.GetPlanet(name).FoodShipmentIncoming,
                    FoodDistributionCenters,
                    AIMap.GameAIConstants.distributionCenterFoodTargetStock,
                    currentStockSelector: p => p.Food,
                    GameAI.GameAIOrder.OrderType.OrderTypeFoodTransport,
                    GameAI.GameAIOrder.OrderType.OrderTypeFoodChange,
                    GameAI.GameAIOrder.OrderType.OrderTypeFoodTransportInProgress,
                    orders);
            }

            // ── Grotsits ─────────────────────────────────────────────────────

            // public for the self-check
            public void ProcessGrotsitsShortage(
                List<Planet.UpdateResult> results,
                List<GameAI.GameAIOrder>        orders)
            {
                ProcessResourceShipments(results,
                    Planet.UpdateResult.UpdateResultType.UpdateResultTypeGrotsitsShortage,
                    Planet.UpdateResult.UpdateResultType.UpdateResultTypeGrotsitsSurplus,
                    incomingCheck: name => AIMap.GetPlanet(name).GrotsitsShipmentIncoming,
                    GrotsitsDistributionCenters,
                    AIMap.GameAIConstants.distributionCenterGrotsitsTargetStock,
                    currentStockSelector: p => p.Grotsits,
                    GameAI.GameAIOrder.OrderType.OrderTypeGrotsitsTransport,
                    GameAI.GameAIOrder.OrderType.OrderTypeGrotsitsChange,
                    GameAI.GameAIOrder.OrderType.OrderTypeGrotsitsTransportInProgress,
                    orders);
            }

            // Below any real shortage's priority: real shortages' Priority is -Data (a positive magnitude,
            // worse shortage sorts first), so an ordinary fixed negative constant is enough to always lose
            // to every real shortage regardless of magnitude.
            private const float DistributionCenterPrioritySentinel = float.MinValue / 2f;

            /// <summary>
            /// The least share of a shipment that must still arrive, through unavoidable blockades, for this target to be
            /// supplied (GameAIConstants.shipmentMinDeliveredFraction, clamped to 0..1). One method so a later change can
            /// ask for less on a high value target (a Distribution Center, a specialist producer, a chokepoint) and more on
            /// an ordinary one; today every target shares the constant. Public for the self-check.
            /// </summary>
            public float MinDeliveredFractionFor(string targetPlanetName)
                => Mathf.Clamp01(AIMap.GameAIConstants.shipmentMinDeliveredFraction);

            /// <summary>
            /// Choice order within a shortage row: clean routes before lossy ones (a nearer source that loses part of the
            /// shipment never beats a clean one), then the ScoreMatrix default, cost minus surplus. Public for the self-check.
            /// </summary>
            public static int CompareResourceChoices(ResourceChoiceElement x, ResourceChoiceElement y)
            {
                var lossy = (x.Loss > 0f).CompareTo(y.Loss > 0f);
                return lossy != 0 ? lossy : (x.Cost - x.Surplus).CompareTo(y.Cost - y.Surplus);
            }

            /// <summary>
            /// Shared shipment logic for any surplus -> shortage resource. A shipment is capped at
            /// min(source's remaining surplus, target's remaining shortfall) rather than always draining
            /// the source's entire surplus, so an overshoot no longer silently turns the target into an
            /// accidental new source next turn. Because a source can therefore have surplus left over
            /// after serving one shortage, the matrix is rebuilt and re-run in rounds against the
            /// remaining balances, letting a single source serve multiple shortages within the same turn
            /// (each round strictly zeroes out at least one side, so this always terminates within
            /// shortages.Count + surpluses.Count rounds).
            ///
            /// A designated Distribution Center below its target stock, with no REAL shortage already
            /// reported for it this turn, gets a synthetic shortage-shaped entry added to the same list —
            /// same rounds-based capping, same shipment orders — at a fixed low-priority sentinel so real
            /// shortages always claim surplus first.
            /// </summary>
            private void ProcessResourceShipments(
                List<Planet.UpdateResult>                  results,
                Planet.UpdateResult.UpdateResultType shortageType,
                Planet.UpdateResult.UpdateResultType surplusType,
                Func<string, bool>                               incomingCheck,
                List<string>                                     distributionCenters,
                float                                             distributionCenterTargetStock,
                Func<Planet, float>                               currentStockSelector,
                GameAI.GameAIOrder.OrderType                     transportType,
                GameAI.GameAIOrder.OrderType                     changeType,
                GameAI.GameAIOrder.OrderType                     inProgressType,
                List<GameAI.GameAIOrder>                         orders)
            {
                var surplusResults = results.FindAll(x => x.PlayerID == Player.playerID && x.Result == surplusType);
                if (surplusResults.Count == 0)
                {
                    PruneShipmentHeldBack(transportType, null);   // nothing to ship: nothing is held back by a blockade
                    return;
                }

                var shortages = results.FindAll(x =>
                    x.Result == shortageType && x.PlayerID == Player.playerID && !incomingCheck(x.Name));

                // Tracks ONLY the entries this call synthesizes below — deliberately not the same list as
                // distributionCenters, because a DC can also have a genuine real shortage the same turn
                // (see the guard just below), and that real shortage must keep its normal, gap-derived
                // priority rather than being demoted just because the planet happens to hold a DC role.
                var syntheticShortageNames = new List<string>();
                foreach (var dcName in distributionCenters)
                {
                    if (shortages.Exists(s => s.Name == dcName)) continue; // a real shortage already covers it
                    if (incomingCheck(dcName)) continue; // a shipment (real or synthetic) is already en route
                    var dcPlanet = AIMap.GetPlanet(dcName);
                    if (dcPlanet == null) continue;
                    var gap = distributionCenterTargetStock - currentStockSelector(dcPlanet);
                    if (gap <= 0f) continue;
                    shortages.Add(new Planet.UpdateResult(dcName, shortageType, -gap, Player.playerID));
                    syntheticShortageNames.Add(dcName);
                }

                if (shortages.Count == 0)
                {
                    PruneShipmentHeldBack(transportType, null);   // no shortage left to be held back
                    return;
                }

                // Real PlanetUpdatePlanet shortages carry a negative Data; the magnitude is what matters here.
                // A real shortage is filled up to shipmentBufferTurns turns of its deficit; a DC's synthetic gap is already a stock target.
                var bufferTurns = Mathf.Max(1f, AIMap.GameAIConstants.shipmentBufferTurns);
                var remainingShortage = shortages.ToDictionary(s => s.Name,
                    s => Mathf.Abs(Convert.ToSingle(s.Data)) * (syntheticShortageNames.Contains(s.Name) ? 1f : bufferTurns));
                var remainingSurplus = surplusResults.ToDictionary(s => s.Name,
                    s => Convert.ToSingle(s.Data) * AIMap.GetPlanet(s.Name).GetPopulationFraction(Player.playerID));

                var maxRounds = shortages.Count + surplusResults.Count;
                // Rows where blockades removed every source (in any round): "Blockade" (loss >= amount) or "LowYield"
                // (only the minimum delivered fraction refused them).
                var blockedRows = new Dictionary<string, ShipmentHold>();
                var servedRows = new HashSet<string>();
                for (var round = 0; round < maxRounds; round++)
                {
                    var matrix = BuildResourceMatrix(shortages, surplusResults, remainingShortage, remainingSurplus,
                        syntheticShortageNames, blockedRows);
                    if (matrix == null) break;

                    var actions = matrix.GenerateActionList(
                        actionFactory: (origin, element) => new ResourceAction { ChosenChoiceElement = element },
                        ChoiceCompare: CompareResourceChoices);
                    if (actions.Count == 0) break;

                    foreach (var action in actions)
                    {
                        var amount = Mathf.Min(remainingSurplus[action.Origin], remainingShortage[action.Target]);
                        if (amount <= 0f) continue;

                        EmitResourceOrders(action, amount, transportType, changeType, inProgressType, orders);
                        remainingSurplus[action.Origin]  -= amount;
                        remainingShortage[action.Target] -= amount;
                        servedRows.Add(action.Target);
                    }
                }

                // ShipmentCancelled is logged when a shortage's blocked state changes, not on every turn it holds.
                var turn = Gameboard.Instance != null ? Gameboard.Instance.TurnNumber : 0;
                foreach (var shortage in shortages)
                {
                    if (!servedRows.Contains(shortage.Name) && blockedRows.TryGetValue(shortage.Name, out var hold))
                    {
                        _shipmentHeldBackDetail[ShipmentKey(transportType, shortage.Name)] = hold;
                        if (NoteShipmentHeldBack(transportType, shortage.Name, hold.Reason))
                            AITuningLogger.LogShipmentCancelled(turn, Player.playerID, shortage.Name, hold.Reason,
                                hold.Source, hold.Loss, hold.Amount, hold.BlockedNodes);
                    }
                    else
                    {
                        ForgetShipmentHold(ShipmentKey(transportType, shortage.Name));
                    }
                }
                PruneShipmentHeldBack(transportType, shortages.Select(s => s.Name));
            }

            // Forgets this resource's held-back records for shortages that are no longer reported, so a later blockade
            // episode for the same planet is logged again (activeTargets null = no shortage is active).
            private void PruneShipmentHeldBack(GameAI.GameAIOrder.OrderType transportType, IEnumerable<string> activeTargets)
            {
                var prefix = transportType + ":";
                var active = new HashSet<string>(activeTargets ?? Enumerable.Empty<string>());
                foreach (var key in _shipmentHeldBack.Keys.ToList())
                    if (key.StartsWith(prefix, StringComparison.Ordinal) && !active.Contains(key.Substring(prefix.Length)))
                        ForgetShipmentHold(key);
            }

            /// <summary>
            /// Builds one decision row per shortage still owed resource, offering every reachable
            /// surplus planet that still has some left. A row whose name is in syntheticShortageNames
            /// (this call's own synthetic Distribution Center entries — NOT every DC-owned planet; a DC's
            /// genuine real shortage keeps its normal priority) uses a fixed low-priority sentinel instead
            /// of its gap size, so it never outranks a real shortage. Returns null if nothing remains to
            /// match.
            /// </summary>
            private ScoreMatrix<ScoreMatrixDecisionElement, ResourceChoiceElement, ResourceAction> BuildResourceMatrix(
                List<Planet.UpdateResult> shortages,
                List<Planet.UpdateResult> surplusResults,
                Dictionary<string, float>       remainingShortage,
                Dictionary<string, float>       remainingSurplus,
                List<string>                    syntheticShortageNames,
                Dictionary<string, ShipmentHold> blockedRows)
            {
                var matrix = new ScoreMatrix<ScoreMatrixDecisionElement, ResourceChoiceElement, ResourceAction  >
                    (new ScoreMatrixDecisionComparer());

                foreach (var shortage in shortages)
                {
                    if (remainingShortage[shortage.Name] <= 0f) continue;

                    var maxNodes = AIMap.GameAIConstants.maxPathNodesForResourceDistribution;
                    // Every (source, shortage) pair is planned through RoutePlanner.PlanShipmentRoute (range rule included:
                    // the shortest path must be 2..maxNodes nodes, which also rejects FindPath's no-route stub; the route
                    // actually taken may be longer). s.Name != shortage.Name stays: a DC can be BOTH this shortage row
                    // (synthetic demand) AND a real surplus source the same turn, and must never ship to itself.
                    // A blockaded source or target is not dropped: its value is part of the route's Loss, and the pair is
                    // offered only while Loss < the amount it would carry (a shipment that would arrive with 0 is cancelled)
                    // and it delivers at least MinDeliveredFractionFor(shortage) of that amount (LowYield otherwise).
                    var entries = new List<ResourceChoiceElement>();
                    ShipmentHold dropped = null;   // the dropped pair closest to shipping (Blockade outranks LowYield)
                    var minFraction = MinDeliveredFractionFor(shortage.Name);
                    foreach (var s in surplusResults)
                    {
                        if (s.Name == shortage.Name || remainingSurplus[s.Name] <= 0f) continue;
                        var route = RoutePlanner.PlanShipmentRoute(AIMap, s.Name, shortage.Name, _blockadeView, maxNodes);
                        if (route == null) continue;
                        var amount = Mathf.Min(remainingSurplus[s.Name], remainingShortage[shortage.Name]);
                        var dropReason = route.Loss >= amount ? "Blockade"
                            : amount - route.Loss < minFraction * amount ? "LowYield" : null;
                        if (dropReason != null)
                        {
                            if (dropped == null || (dropReason == "Blockade" && dropped.Reason == "LowYield")
                                || (dropReason == dropped.Reason && route.Loss < dropped.Loss))
                                dropped = new ShipmentHold
                                {
                                    Reason = dropReason, Source = s.Name, Loss = route.Loss, Amount = amount,
                                    BlockedNodes = BlockedNodeSummary(route.Nodes),
                                };
                            continue;
                        }
                        entries.Add(new ResourceChoiceElement
                        {
                            SurplusResult = s,
                            ShortageResult = shortage,
                            Cost = route.Cost,
                            Route = route.Nodes,
                            Loss = route.Loss,
                            IsDetour = route.IsDetour,
                        });
                    }
                    if (entries.Count == 0 && dropped != null
                        && (!blockedRows.TryGetValue(shortage.Name, out var already)
                            || (dropped.Reason == "Blockade" && already.Reason == "LowYield")
                            || (dropped.Reason == already.Reason && dropped.Loss < already.Loss)))
                        blockedRows[shortage.Name] = dropped;

                    if (entries.Count > 0)
                    {
                        var decision = new ScoreMatrixDecisionElement
                        {
                            Target = shortage.Name,
                            // shortage.Data is the already-negative deficit (e.g. -20). Negate it so a worse
                            // shortage (more negative Data, larger magnitude) sorts to a HIGHER priority --
                            // ScoreMatrixDecisionComparer processes descending, and rows are processed in
                            // order with each claiming its best choice before the next row runs, so priority
                            // order is who gets first pick of scarce surplus. Un-negated, a mild -5 shortage
                            // outranked a severe -20 one and got served first; this was backwards.
                            Priority = syntheticShortageNames.Contains(shortage.Name)
                                ? DistributionCenterPrioritySentinel
                                : -Convert.ToSingle(shortage.Data),
                        };

                        if (matrix.MatrixElements.ContainsKey(decision))
                        {
                            Debug.LogError("Duplicate Key in Build Resource Matrix");
                        }
                        else
                        {
                            matrix.MatrixElements.Add(decision, entries);
                        }
                    }
                }

                return matrix.MatrixElements.Count == 0 ? null : matrix;
            }

            /// <summary>
            /// Emits the three standard orders (transport, deduct, in-progress) for a resource shipment
            /// action, for a caller-computed amount (already capped at the target's remaining need).
            /// </summary>
            // Public: the human's shipResource command emits the same three orders.
            public void EmitResourceOrders(
                ResourceAction               action,
                float                        amount,
                GameAI.GameAIOrder.OrderType transportType,
                GameAI.GameAIOrder.OrderType changeType,
                GameAI.GameAIOrder.OrderType inProgressType,
                List<GameAI.GameAIOrder>     orders)
            {
                var delay = Convert.ToInt32(action.Cost / AIMap.GameAIConstants.defaultTravelSpeed);

                var transport = MakeOrder(transportType,
                    GameAI.GameAIOrder.OrderTimingType.OrderTimingTypeDelayed,
                    delay, delay, amount, action.Origin, action.Target);
                if (action.Route != null) transport.Route = new List<string>(action.Route);
                orders.Add(transport);

                var turn = Gameboard.Instance != null ? Gameboard.Instance.TurnNumber : 0;
                if (action.IsDetour)
                    AITuningLogger.LogRouteDetour(turn, Player.playerID, action.Origin, action.Target, action.Route, action.Cost);
                if (action.Loss > 0f)
                    AITuningLogger.LogShipmentLossy(turn, Player.playerID, action.Origin, action.Target, amount, action.Loss);

                orders.Add(MakeOrder(changeType,
                    GameAI.GameAIOrder.OrderTimingType.OrderTimingTypeImmediate,
                    0, 0, amount * -1.0f, action.Origin, action.Origin));

                orders.Add(MakeOrder(inProgressType,
                    GameAI.GameAIOrder.OrderTimingType.OrderTimingTypeImmediate,
                    0, 0, amount, action.Origin, action.Target));
            }

            // ── Research ─────────────────────────────────────────────────────

            public float       researchTotal   = 0.0f;
            public CatalogItem currentResearch = null;

            // Weight for a choice whose subType is not in the strategy table.
            private const float DefaultChoiceWeight = 1f;
            // Weight multiplier applied to a ColonyShip on a planet that is ready to colonize.
            private const float ColonyShipUrgentBoost = 2f;

            // Roulette-wheel weight per item subType. Higher = more likely to be picked.
            // 1.0f = neutral. Amass has its own table (AmassResearchWeights).
            private static readonly Dictionary<string, float> ExpandResearchWeights =
                new Dictionary<string, float>
                {
                    { "Food",          3.0f },  // food upgrades biggest boost
                    { "Industry",      2.0f },  // useful but secondary
                    { "Grotsits",      1.0f },  // least useful while expanding
                    { "Research",      1.0f },  // least useful while expanding
                    { "ColonyShip",    2.5f },  // ships useful but secondary
                    { "Warship",       1.5f },  // Updated priority
                    { "Siegeship",     0.75f },  // planet defense line: starting point, half of Warship's
                    { "Bunker",        0.5f },   // starting point
                };
            private static readonly Dictionary<string, float> ConsolidateResearchWeights =
                new Dictionary<string, float>
                {
                    { "Food",          1.0f },  // food upgrades biggest boost
                    { "Industry",      3.0f },  // useful but secondary
                    { "Grotsits",      2.0f },  // least useful while expanding
                    { "Research",      1.0f },  // least useful while expanding
                    { "ColonyShip",    0.5f },  // ships useful but secondary
                    { "Warship",       2.5f },  // Updated priority
                    { "Siegeship",     2.0f },  // starting point
                    { "Bunker",        1.5f },  // starting point
                };
            private static readonly Dictionary<string, float> AmassResearchWeights =
                new Dictionary<string, float>
                {
                    { "Food",          1.0f },
                    { "Industry",      2.5f },
                    { "Grotsits",      2.0f },
                    { "Research",      1.0f },
                    { "ColonyShip",    0.5f },
                    { "Warship",       4.0f },  // at war: weapons first
                    { "Siegeship",     3.0f },  // starting point
                    { "Bunker",        2.0f },  // starting point
                };
            private static readonly Dictionary<AIStrategy, Dictionary<string, float>> ResearchWeightTable =
                new Dictionary<AIStrategy, Dictionary<string, float>>
                {
                    { AIStrategy.AIStrategyExpand,      ExpandResearchWeights },
                    { AIStrategy.AIStrategyConsolidate, ConsolidateResearchWeights },
                    { AIStrategy.AIStrategyAmass,       AmassResearchWeights },
                };

            private void ProcessResearch(
                List<Planet.UpdateResult> results,
                List<GameAI.GameAIOrder>        orders)
            {
                var researchResults = results
                    .Where(p => p.PlayerID == Player.playerID 
                                && p.Result == Planet.UpdateResult.UpdateResultType
                        .UpdateResultTypeResearchProduced)
                    .ToList();

                if (researchResults.Count == 0) return;

                var researchThisTurn = researchResults.Sum(p => (float)p.Data);
                if (researchThisTurn > 0.0f) UpdateResearch(researchThisTurn, orders);

                foreach (var r in researchResults)
                    orders.Add(MakeOrder(
                        GameAI.GameAIOrder.OrderType.OrderTypeResearchChange,
                        GameAI.GameAIOrder.OrderTimingType.OrderTimingTypeImmediate,
                        0, 0, (float)r.Data * -1.0f, r.Name, r.Name));
            }

            // Public for the human-mode self-check (Assets/Editor is a separate assembly).
            public void UpdateResearch(float researchThisTurn, List<GameAI.GameAIOrder> orders)
            {
                researchTotal += researchThisTurn;
                if (currentResearch == null)
                {
                    ChooseNewResearch(orders);
                }
                else if (researchTotal >= currentResearch.cost)
                {
                    researchTotal -= currentResearch?.cost ?? 0.0f;
                    CompleteResearch(orders);
                    ChooseNewResearch(orders);
                }
            }

            private void CompleteResearch(List<GameAI.GameAIOrder> orders)
            {
                if (currentResearch == null)
                    return;
                var completedResearchName = currentResearch.name;
                _researchCompletedThisTurn = currentResearch.itemName;
                currentResearch.researched = true;
                foreach( var dependentItem in ProductionCatalog.catalogItems.FindAll(x => x.requiredTech == currentResearch.itemName))
                {
                    dependentItem.researched  = true;
                }
                if (Gameboard.Instance != null)
                {
                    Gameboard.Instance.CreateNotificationsForCompletedResearch(completedResearchName, Player.playerID);
                    AITuningLogger.LogResearchComplete(Gameboard.Instance.TurnNumber, Player.playerID, completedResearchName);
                }

            }
            private void ChooseNewResearch(List<GameAI.GameAIOrder> orders)
            {
                if (HumanControlled)
                {
                    ChooseNextQueuedResearch(orders);
                    return;
                }
                var researchChoices = ResearchCatalog.catalogItems.FindAll(x => !x.researched).ToList();
                researchChoices = researchChoices.FindAll(x =>
                    (string.IsNullOrEmpty(x.requiredTech) || ResearchCatalog.GetItem(x.requiredTech).researched)).ToList();   
  
                var matrix = BuildChoiceMatrix(researchChoices, Strategy);
                if (matrix == null) return;

                var actions = matrix.GenerateActionList(
                    actionFactory: (origin, element) => new ResearchAction { ChosenItem = element.Item },
                    weightSelector:      element => element.Weight);

                if (actions.Count == 0) return;

                currentResearch = actions[0].ChosenItem;
                var offenseBoost = GetResearchSituationalMultiplier(currentResearch);
                if (offenseBoost > 1f)
                    AITuningLogger.LogOffenseResearchBoost(Gameboard.Instance.TurnNumber, Player.playerID,
                        currentResearch.itemName, offenseBoost);
                if (Player.playerID == 0 && Gameboard.Instance != null)
                {
                    Debug.Log("Turn: " + Gameboard.Instance.TurnNumber + " New Research: " + currentResearch.name);
                }

                orders.Add(MakeOrder(
                    GameAI.GameAIOrder.OrderType.OrderTypeResearchSet,
                    GameAI.GameAIOrder.OrderTimingType.OrderTimingTypeImmediate,
                    0, 0, 0f, currentResearch.itemName, currentResearch.itemName));
                Gameboard.Instance.CreateNotificationsForNewResearch(currentResearch?.itemName, Player.playerID);
                AITuningLogger.LogResearchStart(Gameboard.Instance.TurnNumber, Player.playerID, currentResearch?.itemName);
            }

            // ── Human research queue ─────────────────────────────────────────

            /// <summary>The human's research queue, worked through in order (item names). Unused by AI players.</summary>
            public List<string> ResearchQueue { get; private set; } = new List<string>();

            private string _researchCompletedThisTurn;

            /// <summary>The research that completed since the last call (null when none); clears it. Read once per turn by the human's event list.</summary>
            public string TakeResearchCompleted()
            {
                var name = _researchCompletedThisTurn;
                _researchCompletedThisTurn = null;
                return name;
            }

            /// <summary>A human player with nothing to research: points keep accumulating in researchTotal until something is queued.</summary>
            public bool ResearchIdle => HumanControlled && currentResearch == null;

            public void SetResearchQueue(IEnumerable<string> names)
            {
                ResearchQueue = names != null ? new List<string>(names) : new List<string>();
            }

            /// <summary>
            /// Starts the first queued item that can still be researched, dropping entries that cannot (unknown, researched meanwhile,
            /// prerequisite not researched). Leaves currentResearch null when the queue holds none.
            /// </summary>
            /// <summary>
            /// A human who queues research while nothing is in progress sees it start at once, not on the next research turn. Does nothing
            /// for an AI player or while something is being researched. Public for Gameboard.SubmitHumanCommands and the self-check.
            /// </summary>
            public void StartQueuedResearchIfIdle(List<GameAI.GameAIOrder> orders)
            {
                if (!HumanControlled || currentResearch != null) return;
                ChooseNextQueuedResearch(orders);
            }

            private void ChooseNextQueuedResearch(List<GameAI.GameAIOrder> orders)
            {
                currentResearch = null;
                while (ResearchQueue.Count > 0)
                {
                    var name = ResearchQueue[0];
                    ResearchQueue.RemoveAt(0);
                    var item = ResearchCatalog.GetItem(name);
                    if (item == null || item.researched) continue;
                    if (!string.IsNullOrEmpty(item.requiredTech))
                    {
                        var prerequisite = ResearchCatalog.GetItem(item.requiredTech);
                        if (prerequisite != null && !prerequisite.researched) continue;
                    }
                    currentResearch = item;
                    break;
                }
                if (currentResearch == null) return;

                orders.Add(MakeOrder(
                    GameAI.GameAIOrder.OrderType.OrderTypeResearchSet,
                    GameAI.GameAIOrder.OrderTimingType.OrderTimingTypeImmediate,
                    0, 0, 0f, currentResearch.itemName, currentResearch.itemName));
                if (Gameboard.Instance != null)
                {
                    Gameboard.Instance.CreateNotificationsForNewResearch(currentResearch.itemName, Player.playerID);
                    AITuningLogger.LogResearchStart(Gameboard.Instance.TurnNumber, Player.playerID, currentResearch.itemName);
                }
            }

            /// <summary>
            /// Builds a one-row choice matrix: a single "AI" origin mapped to all
            /// available research choices, weighted by strategy preference.
            /// </summary>
            private ScoreMatrix<ScoreMatrixDecisionElement, ResearchChoiceElement, ResearchAction> BuildChoiceMatrix(
                List<CatalogItem> choices,
                AIStrategy        strategy)
            {
                if (choices.Count == 0) return null;

                var matrix  = new ScoreMatrix<ScoreMatrixDecisionElement, ResearchChoiceElement, ResearchAction>
                    (new ScoreMatrixDecisionComparer());
                var entries = ApplyResearchSituationalWeights(NormalizeResearchWeightsBySubtype(
                    choices.Select(item => new ResearchChoiceElement
                    {
                        Item   = item,
                        Weight = GetResearchWeight(item, strategy),
                    }).ToList()));

                matrix.MatrixElements.Add(new ScoreMatrixDecisionElement
                {
                    Target = "Research",
                    Priority = 0f
                }, entries);
                return matrix;
            }

            /// <summary>
            /// Divides each research choice's weight by the number of choices sharing its subtype, so a subtype's
            /// total weight stays its table weight however many of its items are eligible at once (Warship has three
            /// parallel research lines: Weapons, Armor, Shields). A subtype with one choice is unchanged.
            /// </summary>
            public static List<ResearchChoiceElement> NormalizeResearchWeightsBySubtype(List<ResearchChoiceElement> choices)
            {
                var counts = choices
                    .GroupBy(c => c.Item.subType ?? string.Empty)
                    .ToDictionary(g => g.Key, g => g.Count());
                return choices.Select(c =>
                {
                    var normalized = c;
                    normalized.Weight = c.Weight / counts[c.Item.subType ?? string.Empty];
                    return normalized;
                }).ToList();
            }

            /// <summary>
            /// Roulette-wheel weight for a research item given the current strategy.
            /// Higher = more likely to be picked. Falls back to a neutral weight if the
            /// strategy or subType has no table entry.
            /// </summary>
            public static float GetResearchWeight(CatalogItem item, AIStrategy strategy)
            {
                if (ResearchWeightTable.TryGetValue(strategy, out var typeWeights) &&
                    typeWeights.TryGetValue(item.subType, out var weight))
                {
                    return weight;
                }

                return DefaultChoiceWeight;
            }

            /// <summary>
            /// The situational factor on a research item's weight: blockadedOffenseResearchBoost for a Warship Offense item
            /// while any blockade against me is visible (blockade value is offense against offense), otherwise 1. Kept out
            /// of the static strategy table, as production's situational weights are.
            /// </summary>
            public float GetResearchSituationalMultiplier(CatalogItem item)
            {
                if (item == null || !WarshipStats.IsWarshipImprovement(item) || item.effect != WarshipStats.OffenseKey)
                    return 1f;
                if (_blockadeView == null || !_blockadeView.BlockadedNames.Any()) return 1f;
                return AIMap.GameAIConstants.blockadedOffenseResearchBoost;
            }

            /// <summary>
            /// Applies the situational multiplier to already-normalized research weights (so Armor and Shields do not dilute
            /// the boost). Returns a new list; the input is not mutated.
            /// </summary>
            public List<ResearchChoiceElement> ApplyResearchSituationalWeights(List<ResearchChoiceElement> choices)
                => choices.Select(c =>
                {
                    var adjusted = c;
                    adjusted.Weight = c.Weight * GetResearchSituationalMultiplier(c.Item);
                    return adjusted;
                }).ToList();

            // ── Industry ─────────────────────────────────────────────────────
            // Roulette-wheel weight per item subType. Higher = more likely to be picked.
            // 1.0f = neutral. Amass has its own table (AmassIndustryWeights).
            private static readonly Dictionary<string, float> ExpandIndustryWeights =
                new Dictionary<string, float>
                {
                    { "Food",          2.5f },  // food needed for pop growth
                    { "Industry",      1.5f },  // slightly useful while expanding
                    { "Grotsits",      1.0f },  // build the base
                    { "Research",      1.0f },  // build the base
                    { "ColonyShip",    2.5f },  // colony ships needed
                    { "Warship",       1.5f },  // Updated priority
                    { "WarshipUpdate", 1.5f },  // same as Warship
                    { "Siegeship",     0.5f },  // starting point (planet defense)
                    { "Bunker",        0.5f },  // starting point
                };

            private static readonly Dictionary<string, float> ConsolidateIndustryWeights =
                new Dictionary<string, float>
                {
                    { "Food",          1.0f },  // not as growth focused
                    { "Industry",      2.0f },  // building focused
                    { "Grotsits",      1.5f },  // support
                    { "Research",      1.0f },  // research can slack off a bit
                    { "ColonyShip",    1.0f },  // colony ships needed as much
                    { "Warship",       2.5f },  // highest priority
                    { "WarshipUpdate", 2.5f },  // same as Warship
                    { "Siegeship",     2.0f },  // starting point
                    { "Bunker",        1.5f },  // starting point
                };
            private static readonly Dictionary<string, float> AmassIndustryWeights =
                new Dictionary<string, float>
                {
                    { "Food",          1.0f },
                    { "Industry",      1.5f },
                    { "Grotsits",      1.5f },
                    { "Research",      0.5f },
                    { "ColonyShip",    1.0f },  // as Consolidate: 0.5 left ~10 planets unclaimed by T375 on 4p.json (arrivals -23%)
                    { "Warship",       4.0f },
                    { "WarshipUpdate", 4.0f },  // same as Warship
                    { "Siegeship",     3.0f },  // starting point
                    { "Bunker",        2.0f },  // starting point
                };
            private static readonly Dictionary<AIStrategy, Dictionary<string, float>> IndustryWeightTable =
                new Dictionary<AIStrategy, Dictionary<string, float>>
                {
                    { AIStrategy.AIStrategyExpand,      ExpandIndustryWeights },
                    { AIStrategy.AIStrategyConsolidate, ConsolidateIndustryWeights },
                    { AIStrategy.AIStrategyAmass,       AmassIndustryWeights },
                };

            public static float GetIndustryStrategyWeight(CatalogItem item, AIStrategy strategy)
            {
                if (IndustryWeightTable.TryGetValue(strategy, out var typeWeights) &&
                    typeWeights.TryGetValue(item.subType, out var strategyWeight))
                {
                    return strategyWeight;
                }

                return DefaultChoiceWeight;
            }

            private float GetIndustryWeight(CatalogItem item, AIStrategy strategy, string planetName)
            {
                return GetIndustryStrategyWeight(item, strategy) * GetIndustrySituationalWeightMultiplier(item, planetName);
            }

            /// <summary>
            /// Below the wanted fleet: 1 + boost x (shortfall / wanted). From the wanted fleet up to
            /// wanted x cap: tapers linearly from 1 to 0. At or beyond wanted x cap: 0. 1 when nothing
            /// is wanted (no basis to shut warships off). A cap at or below 1 cuts off as soon as the
            /// wanted fleet is met (capFleet then never exceeds wanted, so there is no divide by zero).
            /// </summary>
            public static float WarshipShortfallMultiplier(int wanted, int have, float boost, float cap)
            {
                if (wanted <= 0) return 1f;
                if (have < wanted) return 1f + boost * (wanted - have) / wanted;

                var capFleet = wanted * cap;
                if (have >= capFleet) return 0f;
                return 1f - (have - wanted) / (capFleet - wanted);
            }

            /// <summary>
            /// The fleet Consolidate wants: round-1 garrisons for every outer planet and every colonized
            /// chokepoint (from the Consolidate transport planner), plus the assault's required force whenever a known planet of a player I am at war with exists
            /// (not ChooseTarget, which is null while I hold no warships, exactly when I most need to build).
            /// </summary>
            public int WantedWarships()
            {
                var transport = new ShipTransportPlanner(AIMap, Player.playerID, AIStrategy.AIStrategyConsolidate);
                var garrisons = transport.BuildStates().Sum(s => s.RoundGarrison);
                var assault = new AssaultPlanner(AIMap, Player.playerID, warRivals: AssaultWarFilter());
                var unbounded = garrisons + (assault.HasKnownEnemyPlanet() ? assault.RequiredForce() : 0);

                // Bounded by my economy: the assault force follows the enemies' fleets, which follow mine.
                var ceiling = (int)(AIMap.GameAIConstants.warshipsPerColonizedPlanet * ColonizedPlanetCount());
                // Plus the ships last turn's initiated blockades wanted (bounded per blockade), after the economy ceiling.
                return Math.Min(unbounded, ceiling) + _initiated.WantedShipsLastTurn;
            }

            /// <summary>
            /// Drops every production choice whose weight is not a positive finite number (the values
            /// ScoreMatrix.WeightedPick would treat as 0), keeping the rest in order. Applies to every
            /// strategy: 0 always meant "exclude" (see the ColonyShip situational multiplier).
            /// </summary>
            public static List<IndustryChoiceElement> OfferedChoices(List<IndustryChoiceElement> choices)
                => choices.FindAll(c => c.Weight > 0f && !float.IsNaN(c.Weight) && !float.IsInfinity(c.Weight));

            /// <summary>
            /// Keeps only the highest tier per subtype among the remaining choices. Once several tiers of the
            /// same resource are unlocked and affordable, building anything but the best one is wasted industry
            /// (yield and upkeep only ever count a planet's best tier per resource, regardless of build order),
            /// and a backlog of same-resource tiers would otherwise inflate that resource's share of the roulette
            /// wheel relative to Warship/ColonyShip, which only ever have one catalog entry each.
            /// </summary>
            public static List<IndustryChoiceElement> BestTierPerSubtype(List<IndustryChoiceElement> choices)
                => choices
                    .GroupBy(c => c.Item.subType)
                    .Select(g => g.OrderByDescending(c => c.Item.tier).First())
                    .ToList();

            /// <summary>Planets I own with population (the same test as ShipTransportPlanner.IsColonized).</summary>
            public int ColonizedPlanetCount()
                => AIMap.PlanetList.Count(p => p.Owner == Player.playerID && p.Population.Count > 0);

            /// <summary>Every warship I own: docked anywhere plus my own in-flight ships. Ships still in production are not counted.</summary>
            public int OwnedWarships()
                => AIMap.PlanetList.Sum(p =>
                    p.DockedShips.Count(s => s.Kind == Ship.ShipKind.WarShip && s.Owner == Player.playerID)
                    + p.GetIncomingShips(Ship.ShipKind.WarShip, Player.playerID));

            /// <summary>The Warship production multiplier for a strategy; 1 for Expand and None.</summary>
            public float ComputeWarshipMultiplier(AIStrategy strategy)
                => ComputeWarshipMultiplier(strategy, out _, out _);

            private float ComputeWarshipMultiplier(AIStrategy strategy, out int wanted, out int have)
            {
                wanted = 0;
                have = 0;
                if (!IsConsolidateLike(strategy)) return 1f;
                wanted = WantedWarships();
                have = OwnedWarships();
                return WarshipShortfallMultiplier(wanted, have,
                    AIMap.GameAIConstants.warshipShortfallBoost, AIMap.GameAIConstants.warshipFleetCap);
            }

            // The Warship multiplier reads every planet and is logged, so it is computed at most once per
            // production turn: the first Warship choice evaluated fills it, BuildIndustryMatrix resets it.
            private float? _warshipMultiplierThisTurn;

            // ── Siegeships (assault composition) ─────────────────────────────

            /// <summary>Bombard of a siegeship built now: the Siegeship template at full health with my researched Siegeship improvements.</summary>
            private float NewSiegeshipBombard(WarshipStats stats)
            {
                var subtype = WarshipStats.SubtypeOf(Ship.ShipKind.SiegeShip);
                var researched = WarshipStats.ResearchedNames(ResearchCatalog != null ? ResearchCatalog.catalogItems : null, subtype);
                return stats.Bombard(AIMap.GameAIConstants.siegeShipData, researched, subtype);
            }

            /// <summary>
            /// The siegeships the current assault wants: those already committed to the assault target (docked + in flight) plus
            /// AssaultPlanner.WantedSiegeships (the bombard still missing against the target's known bunkers, in new-siegeship bombard,
            /// capped by siegeshipMaxFleetShare). The target is the one PlanShipActions would pick (blockade first, else enemy-occupied,
            /// retreat cooldowns excluded); 0 with no target or no known bunker there. Free of Gameboard.Instance apart from the turn.
            /// </summary>
            public int WantedSiegeships()
            {
                var turn = Gameboard.Instance != null ? Gameboard.Instance.TurnNumber : 0;
                var stats = new WarshipStats(ResearchCatalog != null ? ResearchCatalog.catalogItems : null);
                var assault = new AssaultPlanner(AIMap, Player.playerID, _blockadeView, stats, _blockadeMemory, turn,
                    AssaultWarFilter()) { ExcludedTargets = new HashSet<string>(CooldownPlanets(turn)) };
                var target = assault.ChooseTarget();
                if (target == null || assault.RequiredBombard(target) <= 0f) return 0;   // no bunker and no pace need: none wanted
                return assault.CommittedSiegeships(target) + assault.WantedSiegeships(target, NewSiegeshipBombard(stats));
            }

            /// <summary>Every siegeship I own: docked anywhere plus my own in flight.</summary>
            public int OwnedSiegeships()
                => AIMap.PlanetList.Sum(p =>
                    p.DockedShips.Count(s => s.Kind == Ship.ShipKind.SiegeShip && s.Owner == Player.playerID)
                    + p.GetIncomingShips(Ship.ShipKind.SiegeShip, Player.playerID));

            /// <summary>
            /// The Siegeship production multiplier: 0 unless the strategy is Consolidate-like and the assault wants siegeships;
            /// otherwise the Warship shortfall shape (WarshipShortfallMultiplier with warshipShortfallBoost and warshipFleetCap) on
            /// wanted vs owned siegeships. Unlike Warship, nothing wanted means 0, so no siegeship is built with no bunker to break.
            /// </summary>
            public float ComputeSiegeshipMultiplier(AIStrategy strategy)
            {
                if (!IsConsolidateLike(strategy)) return 0f;
                var wanted = WantedSiegeships();
                if (wanted <= 0) return 0f;
                return WarshipShortfallMultiplier(wanted, OwnedSiegeships(),
                    AIMap.GameAIConstants.warshipShortfallBoost, AIMap.GameAIConstants.warshipFleetCap);
            }

            // Like the Warship multiplier: reads every planet, so computed at most once per production turn (the first Siegeship
            // choice fills it, BuildIndustryMatrix resets it). SiegeMultiplierBuilds counts the computations (the self-check's proof).
            private float? _siegeMultiplierThisTurn;

            public int SiegeMultiplierBuilds { get; private set; }

            /// <summary>
            /// The follower gate (AssaultPlanner.SiegeshipGate, wired by PlanShipActions): may `count` siegeships leave `origin` for
            /// `target` this turn? ArrivalNotOutmatched with the actions already planned this turn. An unknown target allows the send.
            /// </summary>
            public bool CanSendSiegeship(string origin, string target, int count,
                System.Collections.Generic.IReadOnlyList<ShipAction> departing)
            {
                var planet = AIMap.GetPlanet(target);
                return planet == null || ArrivalNotOutmatched(origin, planet, count, departing);
            }

            /// <summary>
            /// Every in-flight order (GameAI.CurrentAIOrders), set by GameAI before ProcessResults; the arrival projection reads my
            /// ship transports to the target from it. Null (a self-check that does not set it) means nothing is in flight.
            /// </summary>
            public System.Collections.Generic.IReadOnlyList<GameAI.GameAIOrder> InFlightOrders { get; set; }

            /// <summary>
            /// The fight projection (FightProjector.Project, exact: no random draw) of `count` siegeships leaving `origin` now and
            /// landing at `target`: my ships already docked there and my ships in flight there (InFlightOrders), the ships `departing`
            /// this turn for the target (actions planned before this one: the warship force, siegeship groups already let through)
            /// and the group itself, each fleet carrying the real snapshots and damage of the ships that would leave (Peek, skipping
            /// ships an earlier action from the same origin takes) and landing after the EmitShipOrders delay. A delay beyond
            /// retreatProjectionTurns is projected as landing on that last round. Null when there is no fight to project (no war rival's
            /// ship or bunker at the target, or diplomacy off). Pure: reads the map, writes nothing. Public for the self-check.
            /// </summary>
            public FightProjection ProjectArrival(string origin, Planet target, int count,
                System.Collections.Generic.IReadOnlyList<ShipAction> departing = null, Ship.ShipKind groupKind = Ship.ShipKind.SiegeShip)
            {
                if (target == null || count <= 0) return null;
                var constants = AIMap.GameAIConstants;
                var stats = new WarshipStats(ResearchCatalog != null ? ResearchCatalog.catalogItems : null);
                var orders = new List<GameAI.GameAIOrder>();
                if (InFlightOrders != null)
                    orders.AddRange(InFlightOrders.Where(o => o.Type == GameAI.GameAIOrder.OrderType.OrderTypeShipTransport
                                                              && o.PlayerId == Player.playerID && o.Target == target.PlanetName));

                // Ships leaving earlier in the same turn claim their origin's first ships of their kind (as in ProcessShipActions).
                var claimed = new Dictionary<(string origin, Ship.ShipKind kind), int>();
                void Leave(string from, Ship.ShipKind kind, int ships, float cost, bool toTarget)
                {
                    claimed.TryGetValue((from, kind), out var skip);
                    claimed[(from, kind)] = skip + ships;
                    if (!toTarget) return;
                    var fleet = HypotheticalFleet(from, kind, ships, skip, cost, target.PlanetName, constants);
                    if (fleet != null) orders.Add(fleet);
                }
                if (departing != null)
                    foreach (var action in departing)
                        Leave(action.Origin, action.Kind, action.Count, action.Cost, action.Target == target.PlanetName);

                var originPlanet = AIMap.GetPlanet(origin);
                if (originPlanet == null || !originPlanet.DistanceMapToPathingList.TryGetValue(target.PlanetName, out var path)) return null;
                Leave(origin, groupKind, count, path.Cost, true);
                return FightProjector.Project(AIMap, target, Player.playerID, stats, constants, orders);
            }

            /// <summary>
            /// The follower gate's rule: the arrival projection (ProjectArrival) shows the arriving group not wiped and losing less than
            /// followerMaxLossFraction of its strength. With no fight to project (no war rival there) the group may go. Deterministic
            /// and free of side effects (the planner consults it twice a turn).
            /// </summary>
            public bool ArrivalNotOutmatched(string origin, Planet target, int count,
                System.Collections.Generic.IReadOnlyList<ShipAction> departing = null, Ship.ShipKind groupKind = Ship.ShipKind.SiegeShip)
                => ArrivalVerdict(ProjectArrival(origin, target, count, departing, groupKind), out _);

            // The gate's reading of an arrival projection; `reason` is the SiegeHeld reason when it refuses (null when it allows).
            private bool ArrivalVerdict(FightProjection projection, out string reason)
            {
                reason = null;
                if (projection == null) return true;
                if (projection.MyGroupWiped) reason = SiegeFollowerTracker.ReasonWiped;
                else if (!(projection.PerceivedLossFraction < AIMap.GameAIConstants.followerMaxLossFraction))
                    reason = SiegeFollowerTracker.ReasonOutmatched;
                return reason == null;
            }

            // A ship transport order as EmitShipOrders would emit it (same snapshots, damage and delay), never queued: the projection's
            // stand-in for ships that leave this turn. Null when too few such ships are docked (EmitShipOrders would skip the fleet).
            private GameAI.GameAIOrder HypotheticalFleet(string origin, Ship.ShipKind kind, int count, int skip, float cost, string target,
                GameAIConstants constants)
            {
                var planet = AIMap.GetPlanet(origin);
                if (planet == null) return null;
                var snapshots = planet.PeekShipSnapshots(kind, Player.playerID, count, skip);
                if (snapshots.Count < count) return null;
                var delay = Math.Min(TravelDelay(cost), Math.Max(1, constants.retreatProjectionTurns));
                return new GameAI.GameAIOrder
                {
                    Type = GameAI.GameAIOrder.OrderType.OrderTypeShipTransport,
                    TimingType = GameAI.GameAIOrder.OrderTimingType.OrderTimingTypeDelayed,
                    TimingDelay = delay, TotalDelay = delay, Data = count, Origin = origin, Target = target, PlayerId = Player.playerID,
                    Fleet = new GameAI.GameAIOrder.ShipFleetPayload
                    {
                        Kind = kind, Snapshots = snapshots, Damage = planet.PeekShipDamage(kind, Player.playerID, count, skip),
                    },
                };
            }

            /// <summary>A ship transport's delay in turns: path cost / defaultTravelSpeed, at least 1 (see EmitShipOrders).</summary>
            private int TravelDelay(float cost) => Math.Max(1, Convert.ToInt32(cost / AIMap.GameAIConstants.defaultTravelSpeed));

            // Log-only: the follower-gate decisions, reported on change only (SiegeFollow / SiegeHeld).
            private readonly SiegeFollowerTracker _siegeFollowers = new SiegeFollowerTracker();

            /// <summary>The SiegeFollow / SiegeHeld entries the last PlanShipActions reported (log-only; public for the self-check).</summary>
            public List<SiegeFollowerTracker.Entry> LastSiegeFollowerChanges { get; private set; } = new List<SiegeFollowerTracker.Entry>();

            // Re-projects each gate call of the real plan (same inputs, so the same verdict) for its loss and reason, and logs the changes.
            private void LogSiegeFollowers(int turnNumber, List<AssaultPlanner.SiegeGateCall> calls)
            {
                var entries = new List<SiegeFollowerTracker.Entry>();
                foreach (var call in calls)
                {
                    var target = AIMap.GetPlanet(call.Target);
                    var projection = ProjectArrival(call.Origin, target, call.Count, call.Departing);
                    ArrivalVerdict(projection, out var reason);
                    entries.Add(new SiegeFollowerTracker.Entry
                    {
                        Origin = call.Origin, Target = call.Target, Ships = call.Count,
                        Kind = call.Allowed ? SiegeFollowerTracker.KindFollow : SiegeFollowerTracker.KindHeld,
                        Reason = call.Allowed ? string.Empty : reason ?? SiegeFollowerTracker.ReasonOutmatched,
                        LossFraction = projection != null ? projection.PerceivedLossFraction : 0f,
                    });
                }
                LastSiegeFollowerChanges = _siegeFollowers.Update(entries);
                foreach (var entry in LastSiegeFollowerChanges)
                {
                    if (entry.Kind == SiegeFollowerTracker.KindFollow)
                        AITuningLogger.LogSiegeFollow(turnNumber, Player.playerID, entry.Origin, entry.Target, entry.Ships, entry.LossFraction);
                    else
                        AITuningLogger.LogSiegeHeld(turnNumber, Player.playerID, entry.Origin, entry.Target, entry.Reason);
                }
            }

            // Log-only: the last SiegeSized state logged (null = nothing logged or no bunkered target), so the line fires on change only.
            private string _lastSiegeSized;

            /// <summary>
            /// SiegeSized|target|bunkerDefense|bombardNeeded|siegeships|cap on change only, for an assault target with known bunkers.
            /// siegeships is the sized siegeship fleet (committed + still wanted, the production figure), cap the share cap.
            /// </summary>
            private void LogSiegeSizing(int turnNumber, AssaultPlanner assault, Planet target, WarshipStats stats)
            {
                var defense = target != null ? assault.KnownBunkerDefense(target) : 0f;
                if (defense <= 0f)
                {
                    _lastSiegeSized = null;
                    return;
                }
                var needed = assault.RequiredBombard(target);
                var sized = assault.CommittedSiegeships(target) + assault.WantedSiegeships(target, NewSiegeshipBombard(stats));
                var cap = assault.SiegeshipCap(target);
                var ci = System.Globalization.CultureInfo.InvariantCulture;
                var key = string.Join("|", target.PlanetName, defense.ToString("0.#", ci), needed.ToString("0.#", ci),
                    sized.ToString(ci), cap.ToString(ci));
                if (key == _lastSiegeSized) return;
                _lastSiegeSized = key;
                AITuningLogger.LogSiegeSized(turnNumber, Player.playerID, target.PlanetName, defense, needed, sized, cap);
            }

            // ── Initiated blockades (docs/superpowers/specs/2026-10-22-ai-initiated-blockades-design.md) ──────────

            private readonly InitiatedBlockadeTracker _initiated = new InitiatedBlockadeTracker();

            /// <summary>The initiated blockades this player holds (state only; public for the self-check).</summary>
            public InitiatedBlockadeTracker InitiatedBlockades => _initiated;

            /// <summary>The last initiate pass's transitions and every skip it met (not only the changed ones); for the self-check.</summary>
            public List<InitiatedBlockadeTracker.Change> LastInitiatedChanges { get; private set; } = new List<InitiatedBlockadeTracker.Change>();
            public List<InitiatedBlockadeTracker.Skip> LastInitiatedSkips { get; private set; } = new List<InitiatedBlockadeTracker.Skip>();

            /// <summary>
            /// The planets a valid rival currently has as a Distribution Center. Null (the default) reads every rival's PlayerAI through
            /// Gameboard.Instance (empty without one); a self-check sets it.
            /// </summary>
            public Func<int, IEnumerable<string>> RivalDistributionCenters { get; set; }

            private IEnumerable<string> RivalDcs(int rival, int turnNumber)
            {
                if (RivalDistributionCenters != null) return RivalDistributionCenters(rival);
                var board = Gameboard.Instance;
                if (board == null || rival < 0 || rival >= board.players.Count) return Enumerable.Empty<string>();
                var ai = board.players[rival].playerAI;
                return ai == null ? Enumerable.Empty<string>() : ai.DistributionCentersAtTurnStart(turnNumber);
            }

            /// <summary>
            /// A planet I could actually colonize: one of my populated planets is within maxPathNodesForColonization nodes and can
            /// support the colonist (CanSupportColony). A Colony hold needs this to start and to stay (it is not a target otherwise).
            /// </summary>
            public bool ColonizableFromMyPlanets(Planet target)
            {
                var limit = AIMap.GameAIConstants.maxPathNodesForColonization;
                foreach (var origin in AIMap.PlanetList)
                {
                    if (origin == target || origin.Owner != Player.playerID || origin.Population.Count == 0) continue;
                    if (origin.DistanceMapToPathingList.TryGetValue(target.PlanetName, out var entry)
                        && entry.NumNodes >= 2 && entry.NumNodes <= limit && CanSupportColony(origin, target)) return true;
                }
                return false;
            }

            /// <summary>
            /// A colony ship of mine can already get to the planet: a colonist of mine is in flight there, or a colony ship is docked on a
            /// planet with a usable path of at most maxPathNodesForColonization nodes. A Colony blockade is only started when this is false.
            /// </summary>
            public bool ColonyShipCanReach(Planet target)
            {
                var me = Player.playerID;
                if (InFlightOrders != null)
                    foreach (var order in InFlightOrders)
                        if (order.Type == GameAI.GameAIOrder.OrderType.OrderTypePopulationTransport && order.PlayerId == me
                            && order.Target == target.PlanetName) return true;
                var limit = AIMap.GameAIConstants.maxPathNodesForColonization;
                foreach (var planet in AIMap.PlanetList)
                {
                    if (planet == target || !planet.DockedShips.Exists(s => s.Kind == Ship.ShipKind.ColonyShip && s.Owner == me)) continue;
                    if (planet.DistanceMapToPathingList.TryGetValue(target.PlanetName, out var entry)
                        && entry.NumNodes >= 2 && entry.NumNodes <= limit) return true;
                }
                return false;
            }

            private float NewWarshipOffense(WarshipStats stats)
                => stats.Offense(AIMap.GameAIConstants.warShipData,
                    WarshipStats.ResearchedNames(ResearchCatalog != null ? ResearchCatalog.catalogItems : null, WarshipStats.WarshipSubtype));

            /// <summary>
            /// The initiate pass, run last in PlanShipActions on the ships everything else left spare: picks the blockades to hold
            /// (BlockadeInitiator), sends spare warships at each through AssaultPlanner.PlanHoldForce behind the arrival projection, and
            /// keeps the tracker, the give-up counters, the log lines and next turn's wanted-ship total. `alreadyPlanned` is every ship
            /// action planned so far this turn. Returns only the new warship actions.
            /// </summary>
            public List<ShipAction> PlanInitiatedBlockades(int turnNumber, AssaultPlanner assault,
                List<ShipTransportPlanner.PlanetState> states, List<ShipAction> alreadyPlanned)
            {
                var result = new List<ShipAction>();
                var me = Player.playerID;
                var constants = AIMap.GameAIConstants;
                var contact = AIMap.Knowledge.ContactPlayers(AIMap, me);
                var count = Gameboard.Instance != null ? Gameboard.Instance.players.Count : Math.Max(contact.Count + 1, me + 1);
                var rivals = BlockadeInitiator.ValidRivals(me, AIMap.Diplomacy, contact,
                    Enumerable.Range(0, count).Where(i => i != me), constants);

                var dcOwner = new Dictionary<string, int>(StringComparer.Ordinal);
                foreach (var rival in rivals)
                    foreach (var name in RivalDcs(rival, turnNumber)) dcOwner[name] = rival;
                var selection = BlockadeInitiator.Select(AIMap, me, new BlockadeInitiator.Inputs
                {
                    Rivals = rivals,
                    DcOwner = dcOwner,
                    Lanes = BlockadeInitiator.LaneCounts(AIMap, InFlightOrders ?? new List<GameAI.GameAIOrder>(), rivals),
                    ColonyStartAllowed = p => !ColonyShipCanReach(p) && IsValidColonizationTarget(p) && ColonizableFromMyPlanets(p),
                    ColonyHoldValid = ColonizableFromMyPlanets,
                    Tracker = _initiated,
                    Turn = turnNumber,
                });

                var sentByOrigin = new Dictionary<string, int>(StringComparer.Ordinal);
                var planned = new List<ShipAction>(alreadyPlanned);
                foreach (var action in alreadyPlanned.Where(a => a.Kind == Ship.ShipKind.WarShip))
                {
                    sentByOrigin.TryGetValue(action.Origin, out var n);
                    sentByOrigin[action.Origin] = n + action.Count;
                }

                var held = new List<InitiatedBlockadeTracker.Held>();
                var skips = new List<InitiatedBlockadeTracker.Skip>();
                var gaveUp = new Dictionary<string, string>(StringComparer.Ordinal);
                var wantedShips = 0;
                var perShip = Math.Max(0.01f, NewWarshipOffense(new WarshipStats(ResearchCatalog != null ? ResearchCatalog.catalogItems : null)));
                // Walk the whole ranking: a candidate that cannot be sent a ship does not take one of the max-active slots (unless it
                // is already held), so the next one gets the chance.
                foreach (var candidate in selection.Ranked)
                {
                    if (held.Count >= constants.blockadeInitiateMaxActive)
                    {
                        skips.Add(new InitiatedBlockadeTracker.Skip { Planet = candidate.Planet, Kind = candidate.Kind, Reason = "OverCap" });
                        continue;
                    }
                    var planet = AIMap.GetPlanet(candidate.Planet);
                    if (planet == null) continue;
                    var needed = candidate.Kind == BlockadeKind.Colony
                        ? rivals.Max(r => assault.HoldNeededOffense(planet, r))
                        : assault.HoldNeededOffense(planet, candidate.Rival);

                    // Warships the ordinary assault (or an escort) already sent here this turn are not incoming yet: they count as sent.
                    needed = Math.Max(0f, needed - planned.Where(a => a.Kind == Ship.ShipKind.WarShip && a.Target == candidate.Planet)
                        .Sum(a => a.Count) * perShip);
                    // Ships docked at another hold are that hold's force, not spare: two holds do not pass ships back and forth.
                    var holdNames = new HashSet<string>(_initiated.ActivePlanets, StringComparer.Ordinal);
                    foreach (var other in held) holdNames.Add(other.Planet);
                    var sourceStates = states.Where(st => !holdNames.Contains(st.Planet.PlanetName)).ToList();
                    var proposed = assault.PlanHoldForce(planet, needed, sourceStates, sentByOrigin);
                    var stillNeeded = assault.LastBlockadeForce.StillNeeded;
                    var sent = 0;
                    var refused = 0;
                    foreach (var action in proposed)
                    {
                        if (!ArrivalNotOutmatched(action.Origin, planet, action.Count, planned, action.Kind)) { refused++; continue; }
                        result.Add(action);
                        planned.Add(action);
                        sentByOrigin.TryGetValue(action.Origin, out var n);
                        sentByOrigin[action.Origin] = n + action.Count;
                        sent += action.Count;
                    }

                    string cause = null;
                    if (needed > 0f && sent == 0)
                    {
                        var mineThere = planet.DockedShips.Exists(s => s.Kind == Ship.ShipKind.WarShip && s.Owner == me)
                                        || planet.GetIncomingShips(Ship.ShipKind.WarShip, me) > 0;
                        if (refused > 0) cause = "Outmatched";
                        else if (!states.Any(st => st.Planet != planet
                                                   && st.Planet.DistanceMapToPathingList.TryGetValue(planet.PlanetName, out var e)
                                                   && e.NumNodes >= 2 && e.NumNodes <= constants.maxPathNodesForShipTransport))
                            cause = "NoPath";
                        else if (!mineThere) cause = "NoSpare";
                    }
                    if (cause != null)
                    {
                        skips.Add(new InitiatedBlockadeTracker.Skip { Planet = candidate.Planet, Kind = candidate.Kind, Reason = cause });
                        if (!_initiated.IsActive(candidate.Planet)) continue;
                        if (_initiated.BumpGiveUp(candidate.Planet, cause) >= constants.blockadeInitiateGiveUpTurns)
                        {
                            gaveUp[candidate.Planet] = cause == "NoPath" ? "Unreachable" : cause;
                            _initiated.StartCooldown(candidate.Planet, turnNumber + constants.blockadeInitiateCooldownTurns);
                            continue;
                        }
                    }
                    else if (needed > 0f && (stillNeeded > 0f || refused > 0))
                    {
                        // Ships are being sent (or are there) but the force still does not cover the need: a hold that stays short for
                        // the give-up turns is released like one that cannot be sent anything (a rival too strong, or no effect at peace).
                        if (_initiated.IsActive(candidate.Planet)
                            && _initiated.BumpGiveUp(candidate.Planet, "Outmatched") >= constants.blockadeInitiateGiveUpTurns)
                        {
                            gaveUp[candidate.Planet] = "Outmatched";
                            _initiated.StartCooldown(candidate.Planet, turnNumber + constants.blockadeInitiateCooldownTurns);
                            continue;
                        }
                    }
                    else _initiated.ClearGiveUp(candidate.Planet);

                    wantedShips += Math.Min(constants.blockadeInitiateMaxShips, Math.Max(1, (int)Math.Ceiling(needed / perShip)));
                    held.Add(new InitiatedBlockadeTracker.Held
                    {
                        Planet = candidate.Planet, Kind = candidate.Kind, Rival = candidate.Rival, Score = candidate.Score,
                        Stance = AIMap.Diplomacy.IsAtWar(me, candidate.Rival, contact.Contains(candidate.Rival)) ? "War" : "Peace",
                        Needed = needed, Ships = sent,
                    });
                }

                LastInitiatedSkips = new List<InitiatedBlockadeTracker.Skip>(skips);
                LastInitiatedChanges = _initiated.Update(turnNumber, held, last =>
                {
                    if (gaveUp.TryGetValue(last.Planet, out var why)) return why;
                    var planet = AIMap.GetPlanet(last.Planet);
                    if (last.Kind == BlockadeKind.Colony)
                        return planet != null && planet.Population.Count > 0 ? "Colonized" : rivals.Count == 0 ? "WarEnded" : "NoLongerTarget";
                    if (!rivals.Contains(last.Rival)) return "WarEnded";
                    return last.Kind == BlockadeKind.Dc ? "LostDc" : "LaneCooled";
                });
                foreach (var change in LastInitiatedChanges)
                {
                    if (change.Started)
                        AITuningLogger.LogBlockadeInitiateStart(turnNumber, me, change.Planet, change.Kind.ToString(), change.Held.Rival,
                            change.Held.Stance, change.Held.Score, change.Held.Needed, change.Held.Ships);
                    else
                        AITuningLogger.LogBlockadeInitiateEnd(turnNumber, me, change.Planet, change.Kind.ToString(), change.EndReason,
                            change.TurnsHeld);
                }
                foreach (var skip in _initiated.UpdateSkips(skips))
                    AITuningLogger.LogBlockadeInitiateSkipped(turnNumber, me, skip.Planet, skip.Kind.ToString(), skip.Reason);

                _initiated.RecordWantedShips(wantedShips);
                return result;
            }

            // The per-turn part of the Bunker weight (my war rivals, whether a known rival planet exists, the garrison categories). It
            // reads many planets, so it is built once per production turn: the first Bunker choice evaluated fills it,
            // BuildIndustryMatrix resets it. BunkerThreatContextBuilds counts the builds (the self-check's once-per-turn proof).
            private sealed class BunkerThreatContext
            {
                public System.Collections.Generic.ISet<int> Rivals;
                public bool HasKnownEnemyPlanet;
                public ShipTransportPlanner Planner;
            }

            private BunkerThreatContext _bunkerThreatThisTurn;

            public int BunkerThreatContextBuilds { get; private set; }

            private BunkerThreatContext BunkerContext()
            {
                if (_bunkerThreatThisTurn != null) return _bunkerThreatThisTurn;
                BunkerThreatContextBuilds++;
                _bunkerThreatThisTurn = new BunkerThreatContext
                {
                    Rivals = WarRivals(),
                    HasKnownEnemyPlanet = new AssaultPlanner(AIMap, Player.playerID, warRivals: AssaultWarFilter()).HasKnownEnemyPlanet(),
                    Planner = new ShipTransportPlanner(AIMap, Player.playerID, Strategy),
                };
                return _bunkerThreatThisTurn;
            }

            private static bool HoldsRivalShip(Planet planet, System.Collections.Generic.ISet<int> rivals)
                => planet != null && planet.DockedShips.Exists(s =>
                    (s.Kind == Ship.ShipKind.WarShip || s.Kind == Ship.ShipKind.SiegeShip) && s.Owner >= 0 && rivals.Contains(s.Owner));

            /// <summary>
            /// The situational Bunker production weight: 0 when the strategy is not Consolidate-like (Expand and None never build
            /// bunkers) or the planet cannot take another bunker (the cap); otherwise BunkerWeight.
            /// </summary>
            private float BunkerThreatMultiplier(string planetName)
            {
                if (!IsConsolidateLike(Strategy)) return 0f;
                var planet = AIMap.GetPlanet(planetName);
                if (planet == null || !planet.CanBuildBunker()) return 0f;
                return BunkerWeight(planet);
            }

            /// <summary>
            /// The threat weight for a Consolidate-like player: 0 unless an at-war rival is known nearby (its warship or siegeship docked
            /// on the planet or a direct neighbour, or an outer/chokepoint planet while a known at-war rival planet exists), else
            /// base x category factor (outer or chokepoint) x blockaded boost x rival-adjacent boost.
            /// </summary>
            private float BunkerWeight(Planet planet)
            {
                var ctx = BunkerContext();
                var constants = AIMap.GameAIConstants;
                var special = ctx.Planner.IsOuter(planet) || ctx.Planner.IsChokepoint(planet);
                var rivalBeside = HoldsRivalShip(planet, ctx.Rivals)
                                  || AIMap.GetNeighbours(planet.PlanetName).Any(n => HoldsRivalShip(AIMap.GetPlanet(n), ctx.Rivals));
                if (!rivalBeside && !(special && ctx.HasKnownEnemyPlanet)) return 0f;

                var weight = constants.bunkerBaseWeight;
                if (special)
                    weight *= Math.Max(ctx.Planner.IsOuter(planet) ? constants.bunkerOuterFactor : 1f,
                        ctx.Planner.IsChokepoint(planet) ? constants.bunkerChokepointFactor : 1f);
                if (IsBlockaded(planet.PlanetName)) weight *= constants.bunkerBlockadedBoost;
                if (rivalBeside) weight *= constants.bunkerRivalAdjacentBoost;
                return weight;
            }

            /// <summary>
            /// Logs BunkerBuilt for a Bunker production that just completed on a planet of mine: the bunker count after it, the cap
            /// (MaxPopulation) and the threat weight now (0 under Expand/None). A bunker refused at the cap shows count == cap.
            /// </summary>
            public void LogBunkerBuilt(int turn, string planetName)
            {
                var planet = AIMap.GetPlanet(planetName);
                if (planet == null) return;
                _bunkerThreatThisTurn = null;   // the cache may be a turn old; BuildIndustryMatrix resets it again
                var weight = IsConsolidateLike(Strategy) ? BunkerWeight(planet) : 0f;
                _bunkerThreatThisTurn = null;
                AITuningLogger.LogBunkerBuilt(turn, Player.playerID, planetName, planet.BunkerCount(Player.playerID),
                    planet.MaxPopulation, weight, AIMap.IsChokepoint(planetName));
            }

            // Public for the FlatSpace/AI self-check (Assets/Editor is a separate assembly).
            public float GetIndustrySituationalWeightMultiplier(CatalogItem item, string planetName)
            {
                if (item.subType == "ColonyShip")
                {
                    if (IsBlockaded(planetName))
                        return 0f;                       // a blockaded planet builds no colony ships (it launches none either)
                    if (PlanetHasColonyShip(planetName))
                        return 0f;                       // already have one — exclude
                    if (!PlanetHasColonizationTarget(planetName))
                        return 0f;                       // nothing left to colonize — exclude (no useless colony ships)
                    if (IsValidColonizer(planetName))
                        return ColonyShipUrgentBoost;    // ready to colonize — strongly favour
                    if (IsConsolidateLike(Strategy))
                        return ColonyShipUrgentBoost;    // targets remain: keep expanding even before the planet is ready
                }
                if (item.subType == "Bunker")
                    return BunkerThreatMultiplier(planetName);   // 0 at the cap, under Expand/None, or with no rival nearby
                if (item.subType == "Siegeship")
                {
                    if (_siegeMultiplierThisTurn == null)
                    {
                        SiegeMultiplierBuilds++;
                        _siegeMultiplierThisTurn = ComputeSiegeshipMultiplier(Strategy);
                    }
                    return _siegeMultiplierThisTurn.Value;   // 0 under Expand/None or with no known bunker to break
                }
                if (item.type == "Improvement" && !AIMap.GetPlanet(planetName).CanAffordImprovement(item))
                    return 0f;                           // its upkeep would sink the planet's grotsits — do not offer it
                var blockaded = IsBlockaded(planetName);
                var blockadeBoost = blockaded ? AIMap.GameAIConstants.blockadedWarshipBoost : 1f;
                if (item.subType == "WarshipUpdate")
                {
                    if (AIMap.GetPlanet(planetName).FindWarshipUpdateTarget(
                            Player.playerID, WarshipStats.ResearchedNames(ResearchCatalog.catalogItems)) == null)
                        return 0f;                       // nothing docked here is missing an improvement — do not offer it
                    return blockadeBoost;
                }
                if (item.subType == "Warship")
                {
                    var shortfall = 1f;                  // Expand has no fleet cap
                    if (IsConsolidateLike(Strategy))
                    {
                        if (_warshipMultiplierThisTurn == null)
                        {
                            _warshipMultiplierThisTurn = ComputeWarshipMultiplier(Strategy, out var wanted, out var have);
                            AITuningLogger.LogWarshipBoost(
                                Gameboard.Instance != null ? Gameboard.Instance.TurnNumber : 0,
                                Player.playerID, wanted, have, _warshipMultiplierThisTurn.Value);
                        }
                        shortfall = _warshipMultiplierThisTurn.Value;   // shortfall boost / surplus taper, 0 at the cap
                    }
                    // A blockaded planet is exempt from the fleet-cap cutoff: floor at 1, then boost.
                    return (blockaded ? Math.Max(shortfall, 1f) : shortfall) * blockadeBoost;
                }
                return 1f;
            }

            private void ProcessIndustry(
                List<Planet.UpdateResult> results,
                List<GameAI.GameAIOrder>        orders)
            {
                UpdatePlanetaryProduction(results, orders);
                
                // TODO: UpdateResultTypeIndustrySurplus — ship industry
            }

            /// <summary>
            /// Chooses new production item if production is complete, 
            /// or if the production queue is empty
            /// </summary>
            private void UpdatePlanetaryProduction(
                List<Planet.UpdateResult> results,
                List<GameAI.GameAIOrder>        orders)
            {
                var matrix = BuildIndustryMatrix(results, Strategy);
                if (matrix == null) return;

                foreach (var action in matrix.GenerateActionList(
                             actionFactory: (origin, element) =>
                                 new IndustryAction {ChosenItem = element.Item, PlanetName = origin.Target},
                             weightSelector:      element => element.Weight)
                        )
                    EmitProductionOrders(action, orders);
            }
            /// <summary>
            /// Emits the three standard orders (transport, deduct, in-progress)
            /// for a resource shipment action.
            /// </summary>
            private void EmitProductionOrders(
                IndustryAction              action,
                List<GameAI.GameAIOrder>        orders)
            {
                var originPlanet = AIMap.GetPlanet(action.Origin);
                var productionName = action.Target;

                // Mark a warship started under the blockade fleet-cap exemption, so a log can tell it from a cap bug.
                var producedItem = ProductionCatalog.catalogItems.Find(x => x.name == productionName);
                if (producedItem != null && (producedItem.subType == "Warship" || producedItem.subType == "WarshipUpdate")
                    && IsBlockaded(action.Origin))
                    AITuningLogger.LogBlockadedProduction(Gameboard.Instance != null ? Gameboard.Instance.TurnNumber : 0,
                        Player.playerID, action.Origin, productionName, _blockadeView.Value(action.Origin));

                orders.Add(MakeOrder(GameAI.GameAIOrder.OrderType.OrderTypeIndustrySetProduction,
                    GameAI.GameAIOrder.OrderTimingType.OrderTimingTypeImmediate,
                    0, 0, action.Target, action.Origin, action.Origin));
            }

              /// <summary>
            /// Matrix-building logic for any production decisions.
            /// Returns null if there is nothing to do.
            /// </summary>
            // Public for the human-mode self-check (Assets/Editor is a separate assembly).
            public ScoreMatrix<ScoreMatrixMultipleDecisionElement, IndustryChoiceElement, IndustryAction> BuildIndustryMatrix(
                List<Planet.UpdateResult>                  results,
                AIStrategy        strategy)
            {

                // A planet the human develops by hand (Planet.ManualDevelopment) is never given a production decision here.
                var productionCompleteResults = results.FindAll(x => x.PlayerID == Player.playerID
                    && (x.Result is Planet.UpdateResult.UpdateResultType.UpdateResultTypeIndustryProductionComplete
                        or Planet.UpdateResult.UpdateResultType.UpdateResultTypeIndustryProductionQueueEmpty)
                    && !AIMap.GetPlanet(x.Name).ManualDevelopment)
                    .OrderBy(x => x.Name).ThenBy(x => x.GetType()).ToList();
                var surplusResults = productionCompleteResults.FindAll(x => x.PlayerID == Player.playerID
                    && x.Result is Planet.UpdateResult.UpdateResultType.UpdateResultTypeIndustrySurplus);
                
                if (productionCompleteResults.Count == 0)
                    return null;

                var matrix = new ScoreMatrix<ScoreMatrixMultipleDecisionElement, IndustryChoiceElement, IndustryAction  >
                    (new ScoreMatrixMultipleDecisionComparer());

                _warshipMultiplierThisTurn = null;   // recomputed lazily by GetIndustrySituationalWeightMultiplier
                _bunkerThreatThisTurn = null;        // likewise the Bunker threat context
                _siegeMultiplierThisTurn = null;     // and the Siegeship multiplier

                var decisionIndex = 0;
                foreach (var planetName in productionCompleteResults.Select(x => x.Name).Distinct())
                {
                    var planetResults = productionCompleteResults.FindAll(x => x.Name == planetName);
                    var planet = AIMap.GetPlanet(planetName);
                    var potentialProduction = ProductionCatalog.catalogItems.FindAll(x => x.researched == true
                        && !(planet.CompletedImprovements.Select(y => y.Item1).ToList().Contains(x.name))
                        && !planet.IsImprovementSuperseded(x));   // a lower tier than the best already built adds nothing

                    var planetSurplus = surplusResults.FindIndex(x => x.Name == planetName) == -1
                        ? 0f
                        : Convert.ToSingle(surplusResults.Find(x => x.Name == planetName).Data); 
                    var entries = potentialProduction.Select(item => new IndustryChoiceElement
                    {
                        Item     = item,
                        Weight = GetIndustryWeight(item, strategy, planetName),
                        Surplus = planetSurplus,
                        PlanetName = planetName,
                    }).ToList();

                    // A weight of 0 means "do not offer" (already holds a colony ship; Warship at its fleet
                    // cap), but ScoreMatrix treats weights as relative and picks uniformly when every
                    // weight is 0, so such choices must be dropped, not just weighted down.
                    entries = OfferedChoices(entries);
                    entries = BestTierPerSubtype(entries);
                    if(entries.Count == 0) continue;

                    // A planet has one production slot, so it gets exactly one new
                    // item per turn — even when it emitted several production signals
                    // this turn (e.g. ProductionComplete + ProductionQueueEmpty both
                    // fire when the last queued item finishes).
                    matrix.MatrixElements.Add(
                        new ScoreMatrixMultipleDecisionElement
                        {
                            Target =  planetName,
                            Priority = decisionIndex++,
                            NumChoices = planetResults.Count,
                        },
                        entries);

                }
                return matrix;
            }

              // ── Order factory ────────────────────────────────────────────────

            private GameAI.GameAIOrder MakeOrder(
                GameAI.GameAIOrder.OrderType       type,
                GameAI.GameAIOrder.OrderTimingType timing,
                int                                timingDelay,
                int                                totalDelay,
                object                              data,
                string                             origin,
                string                             target)
            => new GameAI.GameAIOrder
            {
                Type        = type,
                TimingType  = timing,
                TimingDelay = timingDelay,
                TotalDelay  = totalDelay,
                Data        = data,
                Origin      = origin,
                Target      = target,
                PlayerId    = Player.playerID,
            };

            // ── Ship transport ───────────────────────────────────────────────

            /// <summary>
            /// Moves warships. The decisions live in ShipTransportPlanner (home garrisons) and, under
            /// Consolidate, AssaultPlanner; this turns each resulting ShipAction into orders.
            /// </summary>
            public void ProcessShipActions(List<GameAI.GameAIOrder> orders, List<Planet.UpdateResult> results = null)
            {
                // Self-checks call this with no Gameboard in the scene.
                var turn = Gameboard.Instance != null ? Gameboard.Instance.TurnNumber : 0;

                // Nothing is undocked until the orders execute, so a second fleet leaving the same
                // origin this turn (home defence + assault) must skip the ships the first one takes. Claims are per kind: the
                // skip counts docked ships of the fleet's own kind, so a siegeship fleet never skips past warships.
                // Escorts launched by ProcessColonizers earlier this turn (Task 15) are the first claims: the planners were already
                // told about them (PlanShipActions passes them as Reserved / PreDeparting), so their actions never count those ships,
                // and the emitted fleets skip past them. Nothing is trimmed here; the check below only reports a planner that
                // overcommitted an escort origin (it should never fire; EmitShipOrders still skips a fleet with too few ships).
                var escorts = new List<ShipAction>(_escortActions);
                var claimedByOrigin = new Dictionary<(string origin, Ship.ShipKind kind), int>();
                foreach (var escort in escorts)
                {
                    var escortKey = (escort.Origin, escort.Kind);
                    claimedByOrigin.TryGetValue(escortKey, out var escortClaimed);
                    claimedByOrigin[escortKey] = escortClaimed + escort.Count;
                }
                var actions = PlanShipActions(turn, results);
                _escortActions.Clear();
                foreach (var action in actions)
                {
                    var key = (action.Origin, action.Kind);
                    claimedByOrigin.TryGetValue(key, out var claimed);
                    if (escorts.Exists(e => e.Origin == action.Origin && e.Kind == action.Kind))
                    {
                        var origin = AIMap.GetPlanet(action.Origin);
                        var docked = origin != null ? origin.DockedShips.Count(s => s.Kind == action.Kind && s.Owner == Player.playerID) : 0;
                        if (docked - claimed < action.Count)
                            Debug.LogWarning($"Escort reservation overcommitted at {action.Origin}: {action.Count} {action.Kind} planned " +
                                             $"to {action.Target}, {docked - claimed} left after the escort and earlier claims.");
                    }
                    EmitShipOrders(action, orders, claimed);
                    claimedByOrigin[key] = claimed + action.Count;
                }
            }

            private string _lastLoggedAssaultTarget;
            // Log-only: the target|nearestTarget pair last logged as a ChokepointAssault, so a standing choice logs once.
            private string _lastLoggedChokepointAssault;

            // A planet I retreated from -> the first turn it may be an assault target again (player-private, not saved: a load forgets it).
            private readonly Dictionary<string, int> _retreatCooldown = new Dictionary<string, int>();
            // Log-only: gated fights that stayed, reported on change only (see RetreatTracker).
            private readonly RetreatTracker _retreatTracker = new RetreatTracker();
            // Tier 3 retreats still travelling: (planet, the turn they land, the blockade value they were planned on). Log-only, not saved.
            private readonly List<(string planet, int landTurn, float remembered)> _retreatPending = new List<(string planet, int landTurn, float remembered)>();

            /// <summary>Planets still inside their retreat cooldown this turn: off the assault target list and not refilled by the garrison planner.</summary>
            private List<string> CooldownPlanets(int turnNumber)
                => _retreatCooldown.Where(kv => kv.Value > turnNumber).Select(kv => kv.Key).ToList();

            /// <summary>The first turn a planet I retreated from may be an assault target again; 0 when there is no cooldown. Public for the self-check.</summary>
            public int RetreatCooldownUntil(string planet) => _retreatCooldown.TryGetValue(planet, out var until) ? until : 0;

            /// <summary>
            /// Reads this player's FightProjection results and lets RetreatPlanner decide which groups leave and where; sets the
            /// assault cooldown of each planet left. Empty when no results are given (a self-check calling the planners
            /// directly) or diplomacy is off (no combat, no projections).
            /// </summary>
            private RetreatPlanner.Plan PlanRetreats(int turnNumber, List<Planet.UpdateResult> results)
            {
                if (results == null || !AIMap.Diplomacy.Enabled) return new RetreatPlanner.Plan();
                LogRetreatArrivals(turnNumber);
                var mine = results
                    .Where(r => r.Result == Planet.UpdateResult.UpdateResultType.UpdateResultTypeFightProjection
                                && r.PlayerID == Player.playerID && r.Data is FightProjection)
                    .Select(r => (FightProjection)r.Data).ToList();
                var stats = new WarshipStats(ResearchCatalog != null ? ResearchCatalog.catalogItems : null);
                var plan = new RetreatPlanner(AIMap, Player.playerID, WarRivals(), _blockadeView, stats).Decide(mine);
                var constants = AIMap.GameAIConstants;
                foreach (var retreat in plan.Retreats)
                {
                    var cooldownUntil = turnNumber + constants.retreatCooldownTurns;
                    _retreatCooldown[retreat.Planet] = cooldownUntil;
                    AITuningLogger.LogRetreat(turnNumber, Player.playerID, retreat.Planet, retreat.Destination, retreat.Tier, retreat.Ships,
                        retreat.LossFraction, retreat.RivalSurvivorsFraction, retreat.RouteCost, cooldownUntil, retreat.PRetreat,
                        retreat.ExactLossFraction, retreat.RememberedBlockade, retreat.MyOffense);
                    if (retreat.Tier == 3)
                    {
                        var delay = Math.Max(1, Convert.ToInt32(retreat.RouteCost / constants.defaultTravelSpeed));
                        _retreatPending.Add((retreat.Destination, turnNumber + delay, retreat.RememberedBlockade));
                    }
                }
                var standing = plan.Stays.Select(s => new RetreatTracker.Entry
                    {
                        Planet = s.Planet, Kind = RetreatTracker.KindStay, Reason = string.Empty, LossFraction = s.LossFraction, PRetreat = s.PRetreat,
                    })
                    .Concat(plan.Holds.Select(h => new RetreatTracker.Entry
                    {
                        Planet = h.Planet, Kind = RetreatTracker.KindHeld, Reason = h.Reason, LossFraction = h.LossFraction, PRetreat = 0f,
                    }));
                foreach (var entry in _retreatTracker.Update(standing))
                {
                    if (entry.Kind == RetreatTracker.KindStay)
                        AITuningLogger.LogRetreatStay(turnNumber, Player.playerID, entry.Planet, entry.LossFraction, entry.PRetreat);
                    else
                        AITuningLogger.LogRetreatHeld(turnNumber, Player.playerID, entry.Planet, entry.Reason, entry.LossFraction);
                }
                return plan;
            }

            // A tier 3 retreat that has landed: log the blockade value it was planned on against the rival's docked offense found
            // there now. The view's value would not do: it is the rival's offense MINUS mine, and mine have just docked, so it read 0
            // in 34 of 36 arrivals (2026-10-06 logs) whatever the remembered value had been.
            private void LogRetreatArrivals(int turnNumber)
            {
                var due = _retreatPending.Where(p => p.landTurn <= turnNumber).ToList();
                if (due.Count == 0) return;
                var blockade = new BlockadeSystem(AIMap, ResearchCatalog != null ? ResearchCatalog.catalogItems : null);
                foreach (var pending in due)
                {
                    var planet = AIMap.GetPlanet(pending.planet);
                    var actual = planet != null ? blockade.LargestOtherDockedOffense(planet, Player.playerID) : 0f;
                    AITuningLogger.LogRetreatArrive(turnNumber, Player.playerID, pending.planet, pending.remembered, actual);
                    _retreatPending.Remove(pending);
                }
            }

            // Log-only: which blockade-breaking target the assault has, so start and end are logged on change only.
            private readonly BlockadeTargetTracker _blockadeTargets = new BlockadeTargetTracker();
            // Log-only: which blockaded planets the assault passed over (reported on change only).
            private readonly BlockadeSkipTracker _blockadeSkips = new BlockadeSkipTracker();
            // Log-only: the conversion holds and what a call would have taken from them (reported on change only).
            private readonly ConversionHoldTracker _conversionHolds = new ConversionHoldTracker();

            /// <summary>
            /// Log-only audit of the conversion holds: the planners run a second time with only the contested holds (no orders are
            /// emitted, nothing is mutated), and the held planet's ships they would have sent say whether a call (a garrison deficit, an
            /// assault target or a blockade target) wanted them. Public for the self-check; this overload builds the planners itself.
            /// </summary>
            public List<ConversionHoldTracker.Entry> AuditConversionHolds(int turnNumber)
            {
                var stats = new WarshipStats(ResearchCatalog != null ? ResearchCatalog.catalogItems : null);
                var assault = new AssaultPlanner(AIMap, Player.playerID, _blockadeView, stats, _blockadeMemory, turnNumber,
                    AssaultWarFilter()) { ExcludedTargets = new HashSet<string>(CooldownPlanets(turnNumber)) };
                var blockadeTarget = assault.ChooseBlockadeTarget(out _);
                var target = blockadeTarget ?? assault.ChooseEnemyTarget();
                var none = new RetreatPlanner.Plan();
                return AuditConversionHolds(turnNumber, assault, target, blockadeTarget != null, none, new List<ShipAction>(),
                    assault.ConversionKeep(none.Retreating));
            }

            // `actions` are the ship actions the real plan emitted this turn (the ships it released from a held planet are counted from
            // them); `keep` is the keep map it used. The audit's own planners run WITHOUT any keep, so Wanted is the full demand.
            private List<ConversionHoldTracker.Entry> AuditConversionHolds(int turnNumber, AssaultPlanner assault, Planet target,
                bool isBlockadeTarget, RetreatPlanner.Plan retreat, List<ShipAction> actions, Dictionary<string, int> keep)
            {
                var entries = new List<ConversionHoldTracker.Entry>();
                var holds = assault.ConversionHolds().Where(p => !retreat.Retreating.Contains(p)).ToList();
                if (holds.Count == 0) return entries;
                var contested = assault.ContestedHolds().Where(p => !retreat.Retreating.Contains(p)).ToList();
                var audit = new ShipTransportPlanner(AIMap, Player.playerID, Strategy)
                {
                    HeldPlanet    = target?.PlanetName,
                    HeldPlanets   = contested,
                    Retreating    = retreat.Retreating,
                    RefillBlocked = CooldownPlanets(turnNumber),
                };
                var homeActions = audit.Plan();
                var assaultActions = assault.Plan(target, audit.LastStates, homeActions);
                foreach (var name in holds)
                {
                    var planet = AIMap.GetPlanet(name);
                    var garrison = homeActions.Where(a => a.Origin == name && a.Kind == Ship.ShipKind.WarShip).ToList();
                    var offense = assaultActions.Where(a => a.Origin == name && a.Kind == Ship.ShipKind.WarShip).ToList();
                    var call = "-"; var callTarget = "-"; var wanted = 0;
                    if (garrison.Count > 0)
                    {
                        call = "Garrison"; callTarget = garrison[0].Target; wanted = garrison.Sum(a => a.Count);
                    }
                    else if (offense.Count > 0)
                    {
                        call = isBlockadeTarget ? "Blockade" : "Assault"; callTarget = offense[0].Target; wanted = offense.Sum(a => a.Count);
                    }
                    var heldShips = planet.DockedShips.Count(s => s.Kind == Ship.ShipKind.WarShip && s.Owner == Player.playerID);
                    // The assault target keeps every ship whatever the keep map says (the planner gives it no release).
                    var keepShips = target != null && target.PlanetName == name ? heldShips
                        : keep != null && keep.TryGetValue(name, out var kept) ? kept : heldShips;
                    entries.Add(new ConversionHoldTracker.Entry
                    {
                        Planet = name,
                        HeldShips = heldShips,
                        HeldOffense = assault.HeldOffense(planet),
                        Wanted = wanted, Call = call, CallTarget = callTarget,
                        RivalNearby = assault.NearbyRivalOffense(planet),
                        Progress = planet.ConversionProgress,
                        Keep = keepShips,
                        Released = actions.Where(a => a.Origin == name && a.Kind == Ship.ShipKind.WarShip).Sum(a => a.Count),
                    });
                }
                return entries;
            }

            /// <summary>
            /// Expand: the home garrison plan, unchanged. Consolidate: choose the assault target (a planet blockaded against
            /// me first, else the enemy-occupied rule), plan home defence with those ships held out, then send whatever is
            /// still spare to the target. Public (and free of Gameboard.Instance) so the self-check can drive it directly.
            /// </summary>
            public List<ShipAction> PlanShipActions(int turnNumber, List<Planet.UpdateResult> results = null)
            {
                // Retreats first: a group leaving a lost fight is claimed before any other planner can count on it.
                var retreat = PlanRetreats(turnNumber, results);
                var actions = new List<ShipAction>(retreat.Actions);

                if (!IsConsolidateLike(Strategy))
                {
                    actions.AddRange(new ShipTransportPlanner(AIMap, Player.playerID)
                        { Retreating = retreat.Retreating, RefillBlocked = CooldownPlanets(turnNumber), Reserved = EscortReserved() }.Plan());
                    return actions;
                }

                var stats = new WarshipStats(ResearchCatalog != null ? ResearchCatalog.catalogItems : null);
                var excluded = new HashSet<string>(CooldownPlanets(turnNumber));
                excluded.UnionWith(retreat.Retreating);
                var assault = new AssaultPlanner(AIMap, Player.playerID, _blockadeView, stats, _blockadeMemory, turnNumber,
                    AssaultWarFilter())
                {
                    ExcludedTargets = excluded, SiegeshipGate = CanSendSiegeship,
                    PreDeparting = new List<ShipAction>(_escortActions),   // escorts leave first (Task 15)
                };
                var blockadeTarget = assault.ChooseBlockadeTarget(out var blockadeReason);
                var target = blockadeTarget ?? assault.ChooseEnemyTarget();

                var targetName = target?.PlanetName;
                LogBlockadeTargetChanges(turnNumber, assault, blockadeTarget, blockadeReason);
                foreach (var skip in _blockadeSkips.Update(assault.LastSkipped))
                    AITuningLogger.LogBlockadeSkipped(turnNumber, Player.playerID, skip.Planet, skip.Reason, skip.Value,
                        skip.Winner, skip.WinnerCommitted);
                if (blockadeTarget == null && targetName != null && targetName != _lastLoggedAssaultTarget)
                    AITuningLogger.LogAssaultTarget(turnNumber, Player.playerID, targetName, assault.RequiredForce());
                _lastLoggedAssaultTarget = blockadeTarget == null ? targetName : null;
                var tilt = assault.LastChokepointTilt;
                if (blockadeTarget == null && tilt.Active)
                {
                    var tiltKey = tilt.Target + "|" + tilt.NearestTarget;
                    if (tiltKey != _lastLoggedChokepointAssault)
                        AITuningLogger.LogChokepointAssault(turnNumber, Player.playerID, tilt.Target, tilt.PathCost, tilt.Percentile,
                            tilt.NearestTarget, tilt.NearestCost);
                    _lastLoggedChokepointAssault = tiltKey;
                }
                else _lastLoggedChokepointAssault = null;

                // A planet I am converting keeps only the ships its nearby rival makes necessary (conversionHoldKeepFraction) and releases
                // the rest to the garrison and blockade calls; the assault target and contested holds still keep every ship.
                var contested = assault.ContestedHolds().Where(p => !retreat.Retreating.Contains(p)).ToList();
                var conversionKeep = assault.ConversionKeep(retreat.Retreating);
                var transport = new ShipTransportPlanner(AIMap, Player.playerID, Strategy)
                {
                    HeldPlanet  = targetName,
                    HeldPlanets = contested,
                    ConversionKeep = conversionKeep,
                    Retreating  = retreat.Retreating,
                    RefillBlocked = CooldownPlanets(turnNumber),
                    Reserved = EscortReserved(),   // the escorts' ships are already leaving (Task 15)
                };
                var homeActions = transport.Plan();
                actions.AddRange(homeActions);
                actions.AddRange(assault.Plan(target, transport.LastStates, homeActions));
                LogSiegeSizing(turnNumber, assault, target, stats);
                // Read before the conversion-hold audit below runs Plan again (and replaces the planner's gate calls).
                LogSiegeFollowers(turnNumber, assault.LastSiegeGateCalls);

                var force = assault.LastBlockadeForce;
                if (blockadeTarget != null && force.Ships > 0)
                    AITuningLogger.LogBlockadeForce(turnNumber, Player.playerID, blockadeTarget.PlanetName,
                        force.Ships, force.Offense, force.StillNeeded);
                foreach (var entry in _conversionHolds.Update(AuditConversionHolds(turnNumber, assault, target, blockadeTarget != null,
                             retreat, actions, conversionKeep)))
                    AITuningLogger.LogConversionHoldSpare(turnNumber, Player.playerID, entry.Planet, entry.HeldShips, entry.HeldOffense,
                        entry.Wanted, entry.Call, entry.CallTarget, entry.RivalNearby, entry.Progress, entry.Keep, entry.Released);
                // Last: initiated blockades take only the ships every plan above left spare.
                actions.AddRange(PlanInitiatedBlockades(turnNumber, assault, transport.LastStates, actions));
                return actions;
            }

            private void LogBlockadeTargetChanges(int turnNumber, AssaultPlanner assault, Planet blockadeTarget, string reason)
            {
                BlockadeTargetTracker.Target? current = null;
                if (blockadeTarget != null)
                {
                    var name = blockadeTarget.PlanetName;
                    current = new BlockadeTargetTracker.Target
                    {
                        Planet  = name,
                        Blocker = _blockadeView.Blocker(name),
                        Value   = _blockadeView.Value(name),
                        Needed  = assault.NeededOffense(blockadeTarget),
                        Reason  = reason,
                    };
                }
                foreach (var change in _blockadeTargets.Update(turnNumber, current,
                             name => _blockadeView != null && _blockadeView.IsBlockaded(name)))
                {
                    if (change.Started)
                        AITuningLogger.LogBlockadeTarget(turnNumber, Player.playerID, change.Planet, change.Target.Blocker,
                            change.Target.Value, change.Target.Needed, change.Target.Reason);
                    else
                        AITuningLogger.LogBlockadeTargetEnd(turnNumber, Player.playerID, change.Planet, change.EndReason,
                            change.TurnsHeld);
                }
            }

            /// <summary>
            /// Emits the standard order trio for a fleet: delayed arrival (carrying each ship's
            /// snapshot), immediate departure from the origin, immediate "incoming" flag at the target.
            /// </summary>
            public void EmitShipOrders(ShipAction action, List<GameAI.GameAIOrder> orders, int skipShips = 0)
            {
                var origin = AIMap.GetPlanet(action.Origin);
                var snapshots = origin.PeekShipSnapshots(action.Kind, Player.playerID, action.Count, skipShips);
                if (snapshots.Count < action.Count)
                {
                    Debug.LogWarning($"Ship transport {action.Origin}->{action.Target} skipped: " +
                                     $"wanted {action.Count} ships, only {snapshots.Count} docked.");
                    return;
                }

                var fleet = new GameAI.GameAIOrder.ShipFleetPayload
                {
                    Kind = action.Kind,
                    Snapshots = snapshots,
                    Damage = origin.PeekShipDamage(action.Kind, Player.playerID, action.Count, skipShips),
                };
                // At least 1: a Delayed order with TimingDelay <= 0 is both queued AND executed
                // immediately by ProcessNewOrders, which would dock the fleet twice.
                var delay = TravelDelay(action.Cost);

                var transport = MakeOrder(GameAI.GameAIOrder.OrderType.OrderTypeShipTransport,
                    GameAI.GameAIOrder.OrderTimingType.OrderTimingTypeDelayed,
                    delay, delay, action.Count, action.Origin, action.Target);
                transport.Fleet = fleet;
                orders.Add(transport);

                var departure = MakeOrder(GameAI.GameAIOrder.OrderType.OrderTypeShipDeparture,
                    GameAI.GameAIOrder.OrderTimingType.OrderTimingTypeImmediate,
                    0, 0, action.Count, action.Origin, action.Origin);
                departure.Fleet = fleet;
                orders.Add(departure);

                var inProgress = MakeOrder(GameAI.GameAIOrder.OrderType.OrderTypeShipTransferInProgress,
                    GameAI.GameAIOrder.OrderTimingType.OrderTimingTypeImmediate,
                    0, 0, action.Count, action.Origin, action.Target);
                inProgress.Fleet = fleet;
                orders.Add(inProgress);
            }

            // ── MonoBehaviour ────────────────────────────────────────────────

            void Awake()
            {
                ProductionCatalog = this.AddComponent<Catalog>();
                ProductionCatalog.CatalogName = "Production Catalog";
                ResearchCatalog = this.AddComponent<Catalog>();
                ResearchCatalog.CatalogName = "Research Catalog";
            }

            void Update() { }
        }
    }
}
