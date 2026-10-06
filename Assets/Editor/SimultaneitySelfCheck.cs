using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using FlatSpace.AI;
using Flatspace.Objects.Production;

// Every player decides against the same world: running the players' ProcessResults in forward and in reversed order over
// the same state must give each player the same orders, the same stances and the same strategy. The orders are compared by
// content, not list position (the list is concatenated in the order the players ran).
public static class SimultaneitySelfCheck
{
    [MenuItem("FlatSpace/AI/Run Simultaneity Self-Check")]
    public static void Run() => RunChecks();

    public static bool RunChecks()
    {
        var ok = RunPlayerOrderIndependenceCheck();
        ok &= RunLossOrderIndependenceCheck();
        ok &= RunRetreatOrderIndependenceCheck();
        Debug.Log(ok
            ? "[SimultaneitySelfCheck] ALL PASSED"
            : "[SimultaneitySelfCheck] FAILURES (see errors above)");
        return ok;
    }

    private static bool Check(bool condition, string label)
    {
        if (!condition) Debug.LogError($"[SimultaneitySelfCheck] FAIL: {label}");
        return condition;
    }

    private sealed class Outcome
    {
        public List<string> Orders = new List<string>();   // sorted keys, one per order
        public string Stances;                              // this player's stance toward every other player after the orders ran
        public string StrategyAfterRound;                   // before the next turn's ApplyWarState
        public string StrategyNextTurn;                     // after it
    }

    // A(0,0) B C D E F(500,0) in a line. Players 0, 1 and 2 hold A+B, C+D and E+F; each has a food shortage on its first planet
    // and a surplus on its second. Players 0 and 1 are at hostility 100 toward each other (both will declare); player 2 is calm.
    private static Dictionary<int, Outcome> RunRound(bool reversed, bool withLosses = false, bool withRetreat = false)
    {
        var template = WarshipSelfCheck.MakeTemplate();
        var constants = WarshipSelfCheck.MakeConstants(template);
        constants.maxPathNodesForKnowledge = 8;
        constants.maxPathNodesForShipTransport = 10;
        constants.maxPathNodesForResourceDistribution = 10;
        constants.defaultTravelSpeed = 1f;
        constants.stanceSteepness = 0.01f;     // exactly 0 or 1 war probability: no roulette luck in the stance
        constants.stanceMidpoint = 30f;
        if (withLosses)
        {
            constants.surrenderMidpoint = 0.1f;    // a loss share of about 0.59 then gives a surrender weight of exactly 1
            constants.surrenderSteepness = 0.01f;
        }
        var research = WarshipSelfCheck.MakeResearch();
        var mapGo = new GameObject("SimultaneityMap");
        var gos = new List<GameObject> { mapGo };
        try
        {
            var map = mapGo.AddComponent<GameAIMap>();
            map.GameAIMapInit(new List<PlanetSpawnData>
            {
                ChokepointSelfCheck.Spawn("A", 0f, 0f, new[] { "B" }),
                ChokepointSelfCheck.Spawn("B", 100f, 0f, new[] { "A", "C" }),
                ChokepointSelfCheck.Spawn("C", 200f, 0f, new[] { "B", "D" }),
                ChokepointSelfCheck.Spawn("D", 300f, 0f, new[] { "C", "E" }),
                ChokepointSelfCheck.Spawn("E", 400f, 0f, new[] { "D", "F" }),
                ChokepointSelfCheck.Spawn("F", 500f, 0f, new[] { "E" }),
            }, constants);
            map.Diplomacy.Enabled = true;

            var homes = new[] { ("A", "B"), ("C", "D"), ("E", "F") };
            var ais = new List<PlayerAI>();
            var results = new List<Planet.UpdateResult>();
            for (var p = 0; p < 3; ++p)
            {
                foreach (var name in new[] { homes[p].Item1, homes[p].Item2 })
                {
                    var planet = map.GetPlanet(name);
                    planet.Owner = p;
                    planet.Population.Add(new Planet.Inhabitant { Player = p });
                }
                WarshipSelfCheck.DockWarships(map.GetPlanet(homes[p].Item1), p, 2);
                var go = new GameObject("SimultaneityPlayer" + p);
                gos.Add(go);
                var player = go.AddComponent<Player>();
                var ai = go.AddComponent<PlayerAI>();
                ai.Player = player;
                ai.AIMap = map;
                player.playerID = p;
                ai.ResearchCatalog = go.AddComponent<Catalog>();
                ai.ResearchCatalog.catalogItems = research;
                ai.Strategy = PlayerAI.AIStrategy.AIStrategyConsolidate;
                ais.Add(ai);
                results.Add(new Planet.UpdateResult(homes[p].Item1,
                    Planet.UpdateResult.UpdateResultType.UpdateResultTypeFoodShortage, 10f, playerID: p));
                results.Add(new Planet.UpdateResult(homes[p].Item2,
                    Planet.UpdateResult.UpdateResultType.UpdateResultTypeFoodSurplus, 20f, playerID: p));
            }
            map.Knowledge.Update(map, 3, 8);
            if (withLosses)
            {
                // Combat losses reach every player through the shared results list: player 1 lost 5 ships to player 0 (a loss
                // share above the significant threshold) while player 0 is the stronger side, so the loss terms and the Surrender
                // choice are exercised in both player orders.
                // Player 0 is already at war with player 1 (the stance was committed earlier), so player 1 is at war, weaker (2 ships
                // against 5) and has lost most of its fleet: it must be offered, and with these constants take, a Surrender.
                map.Diplomacy.SetStance(0, 1, Stance.War, 0);
                WarshipSelfCheck.DockWarships(map.GetPlanet("A"), 0, 3);
                results.Add(new Planet.UpdateResult("C",
                    Planet.UpdateResult.UpdateResultType.UpdateResultTypeWarshipsLost,
                    new CombatLoss { Attacker = 0, Ships = 5f, StrengthLost = 3000f, StrengthDrop = 3000f, Engaged = 5100f }, 1));
            }
            if (withRetreat)
            {
                // Player 0 has one warship at D (player 1's planet) against player 1's three: a lost fight for player 0, a won one
                // for player 1. Both are at war; the projection results are built from the same state for every player, so the
                // retreat decision (a roulette made deterministic below) must not depend on which player runs first.
                map.Diplomacy.SetStance(0, 1, Stance.War, 0);
                map.Diplomacy.SetStance(1, 0, Stance.War, 0);
                WarshipSelfCheck.DockWarships(map.GetPlanet("D"), 0, 1);
                WarshipSelfCheck.DockWarships(map.GetPlanet("D"), 1, 3);
                constants.retreatSteepness = 0.001f;
                var retreatStats = new WarshipStats(research);
                map.Knowledge.Update(map, 3, 8);        // player 0's new ship at D makes D known to it
                GameAI.AppendFightProjections(map, retreatStats, constants, new List<GameAI.GameAIOrder>(), results);
            }
            // Only 0 -> 1: player 1's war is FORCED on it (it never declares), so it can only show up through the committed
            // stance. A design where a stance is written the moment it is decided makes player 1's strategy depend on whether it
            // ran after player 0, which is exactly what this check must catch.
            var seed = map.Diplomacy.Get(0, 1);
            seed.Hostility = 100f;
            map.Diplomacy.Set(0, 1, seed);

            var order = Enumerable.Range(0, 3).ToList();
            if (reversed) order.Reverse();
            var perPlayer = new Dictionary<int, List<GameAI.GameAIOrder>>();
            var all = new List<GameAI.GameAIOrder>();
            foreach (var p in order)
            {
                GameAI.Rand = new System.Random(500 + p);      // the same random stream for a player whatever its position
                var orders = new List<GameAI.GameAIOrder>();
                ais[p].ProcessResults(results, orders);
                perPlayer[p] = orders;
                all.AddRange(orders);
            }
            var strategyAfter = ais.Select(a => a.Strategy.ToString()).ToList();
            foreach (var o in all.Where(o => o.Type == GameAI.GameAIOrder.OrderType.OrderTypeDeclareWar
                                             || o.Type == GameAI.GameAIOrder.OrderType.OrderTypeMakePeace))
                GameAI.ApplyStanceOrder(map.Diplomacy, o, 10);
            foreach (var o in all.Where(o => o.Type == GameAI.GameAIOrder.OrderType.OrderTypeSurrender))
                GameAI.ApplySurrender(map.Diplomacy, o, 10, constants.surrenderTruceTurns);
            map.Diplomacy.Turn = 11;
            foreach (var p in order) ais[p].ApplyWarState(11);

            var outcome = new Dictionary<int, Outcome>();
            for (var p = 0; p < 3; ++p)
            {
                outcome[p] = new Outcome
                {
                    Orders = perPlayer[p].Select(x => $"{x.Type}|{x.PlayerId}|{x.Origin}|{x.Target}|{x.Data}").OrderBy(s => s).ToList(),
                    Stances = string.Join(",", Enumerable.Range(0, 3).Where(r => r != p)
                        .Select(r => $"{r}:{map.Diplomacy.StanceToward(p, r)}")),
                    StrategyAfterRound = strategyAfter[p],
                    StrategyNextTurn = ais[p].Strategy.ToString(),
                };
            }
            return outcome;
        }
        finally
        {
            WarshipSelfCheck.DestroyAll(research);
            foreach (var go in gos) Object.DestroyImmediate(go);
            Object.DestroyImmediate(constants);
            Object.DestroyImmediate(template);
        }
    }

    // The same claim with a combat loss in the results: the loss terms and the Surrender choice must not depend on player order.
    // (No preconditions: a surrender may legitimately end player 1's war, so only the equality is asserted.)
    public static bool RunLossOrderIndependenceCheck()
    {
        var ok = true;
        var forward = RunRound(reversed: false, withLosses: true);
        var reverse = RunRound(reversed: true, withLosses: true);
        // Precondition (review): the fight must really exercise Surrender, or the equality below compares nothing.
        ok &= Check(forward[1].Orders.Any(s => s.StartsWith("OrderTypeSurrender|1|")) && reverse[1].Orders.Any(s => s.StartsWith("OrderTypeSurrender|1|")),
            "precondition: the beaten, weaker player 1 emits a Surrender order in both player orders");
        ok &= Check(forward[1].Stances.Contains("0:Peace") && forward[0].Stances.Contains("1:Peace"),
            "precondition: the executed surrender leaves both players at Peace");
        for (var p = 0; p < 3; ++p)
        {
            ok &= Check(forward[p].Orders.SequenceEqual(reverse[p].Orders),
                $"with losses, player {p}: the same orders whatever the player order\n  forward: {string.Join("; ", forward[p].Orders)}\n  reverse: {string.Join("; ", reverse[p].Orders)}");
            ok &= Check(forward[p].Stances == reverse[p].Stances, $"with losses, player {p}: the same stances ({forward[p].Stances} against {reverse[p].Stances})");
            ok &= Check(forward[p].StrategyAfterRound == reverse[p].StrategyAfterRound && forward[p].StrategyNextTurn == reverse[p].StrategyNextTurn,
                $"with losses, player {p}: the same strategy after the round and at the next turn's start");
        }
        return ok;
    }

    // The retreat roll draws from the shared random stream and reads one projection per player: it must give each player the same
    // orders whatever the player order.
    public static bool RunRetreatOrderIndependenceCheck()
    {
        var ok = true;
        var forward = RunRound(reversed: false, withRetreat: true);
        var reverse = RunRound(reversed: true, withRetreat: true);
        ok &= Check(forward[0].Orders.Any(s => s.StartsWith("OrderTypeShipTransport|0|D|")) && reverse[0].Orders.Any(s => s.StartsWith("OrderTypeShipTransport|0|D|")),
            "precondition: player 0 retreats its lone ship from D in both player orders");
        for (var p = 0; p < 3; ++p)
        {
            ok &= Check(forward[p].Orders.SequenceEqual(reverse[p].Orders),
                $"with a retreat, player {p}: the same orders whatever the player order\n  forward: {string.Join("; ", forward[p].Orders)}\n  reverse: {string.Join("; ", reverse[p].Orders)}");
            ok &= Check(forward[p].Stances == reverse[p].Stances, $"with a retreat, player {p}: the same stances");
        }
        return ok;
    }

    public static bool RunPlayerOrderIndependenceCheck()
    {
        var ok = true;
        var forward = RunRound(reversed: false);
        var reverse = RunRound(reversed: true);

        ok &= Check(forward[0].Orders.Any(s => s.StartsWith("OrderTypeDeclareWar|0|")),
            "precondition: player 0 declares war on 1 (the stance path is exercised)");
        ok &= Check(!forward[1].Orders.Any(s => s.StartsWith("OrderTypeDeclareWar|1|")),
            "precondition: player 1 declares nothing, its war is forced on it by player 0");
        ok &= Check(forward[0].StrategyNextTurn == "AIStrategyAmass" && forward[2].StrategyNextTurn == "AIStrategyConsolidate",
            "precondition: the declarer is Amass at the next turn's start, the calm player is not");
        foreach (var run in new[] { ("forward", forward), ("reverse", reverse) })
            ok &= Check(run.Item2[1].StrategyAfterRound == "AIStrategyConsolidate" && run.Item2[1].StrategyNextTurn == "AIStrategyAmass",
                $"{run.Item1}: the forced player is still Consolidate right after the round and Amass at the next turn's start " +
                $"(got {run.Item2[1].StrategyAfterRound} then {run.Item2[1].StrategyNextTurn}), whatever the player order");

        for (var p = 0; p < 3; ++p)
        {
            ok &= Check(forward[p].Orders.SequenceEqual(reverse[p].Orders),
                $"player {p}: the same orders whatever the player order\n  forward: {string.Join("; ", forward[p].Orders)}\n  reverse: {string.Join("; ", reverse[p].Orders)}");
            ok &= Check(forward[p].Stances == reverse[p].Stances, $"player {p}: the same stances ({forward[p].Stances} against {reverse[p].Stances})");
            ok &= Check(forward[p].StrategyAfterRound == reverse[p].StrategyAfterRound,
                $"player {p}: the same strategy after the round ({forward[p].StrategyAfterRound} against {reverse[p].StrategyAfterRound})");
            ok &= Check(forward[p].StrategyNextTurn == reverse[p].StrategyNextTurn,
                $"player {p}: the same strategy at the next turn's start ({forward[p].StrategyNextTurn} against {reverse[p].StrategyNextTurn})");
        }
        return ok;
    }
}
