using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using FlatSpace.AI;
using Flatspace.Objects.Production;

public static class DiplomacySelfCheck
{
    [MenuItem("FlatSpace/AI/Run Diplomacy Self-Check")]
    public static void Run() => RunChecks();

    public static bool RunChecks()
    {
        var ok = RunDiplomacyStateCheck();
        ok &= RunContactPlayersCheck();
        ok &= RunCutAttributionCheck();
        Debug.Log(ok
            ? "[DiplomacySelfCheck] ALL PASSED"
            : "[DiplomacySelfCheck] FAILURES (see errors above)");
        return ok;
    }

    private static bool Check(bool condition, string label)
    {
        if (!condition) Debug.LogError($"[DiplomacySelfCheck] FAIL: {label}");
        return condition;
    }

    private static bool Near(float a, float b) => Mathf.Abs(a - b) < 0.01f;

    // A(0,0) - B(100,0) - C(200,0) - D(300,0). Player 0 holds A and has a PlayerAI. Diplomacy stays in legacy mode
    // (Enabled false) until a check turns it on.
    private sealed class Fixture : System.IDisposable
    {
        public GameObject MapGo, PlayerGo;
        public GameAIMap Map;
        public GameAIConstants Constants;
        public ShipData Template;
        public List<CatalogItem> Research;
        public PlayerAI AI;

        public static Fixture Line()
        {
            var f = new Fixture();
            f.Template = WarshipSelfCheck.MakeTemplate();
            f.Constants = WarshipSelfCheck.MakeConstants(f.Template);
            f.Constants.maxPathNodesForKnowledge = 6;
            f.Constants.maxPathNodesForShipTransport = 10;
            f.Research = WarshipSelfCheck.MakeResearch();
            f.MapGo = new GameObject("DipSelfCheckMap");
            f.Map = f.MapGo.AddComponent<GameAIMap>();
            f.Map.GameAIMapInit(new List<PlanetSpawnData>
            {
                ChokepointSelfCheck.Spawn("A", 0f, 0f, new[] { "B" }),
                ChokepointSelfCheck.Spawn("B", 100f, 0f, new[] { "A", "C" }),
                ChokepointSelfCheck.Spawn("C", 200f, 0f, new[] { "B", "D" }),
                ChokepointSelfCheck.Spawn("D", 300f, 0f, new[] { "C" }),
            }, f.Constants);
            f.Colonize("A", 0);
            f.PlayerGo = new GameObject("DipSelfCheckPlayer");
            var player = f.PlayerGo.AddComponent<Player>();
            f.AI = f.PlayerGo.AddComponent<PlayerAI>();
            f.AI.Player = player;
            f.AI.AIMap = f.Map;
            player.playerID = 0;
            f.AI.ResearchCatalog = f.PlayerGo.AddComponent<Catalog>();
            f.AI.ResearchCatalog.catalogItems = f.Research;
            return f;
        }

        public Planet P(string name) => Map.GetPlanet(name);

        public void Colonize(string name, int player)
        {
            var planet = Map.GetPlanet(name);
            planet.Owner = player;
            planet.Population.Add(new Planet.Inhabitant { Player = player });
        }

        public void Ships(string planet, int owner, int count) => WarshipSelfCheck.DockWarships(P(planet), owner, count);

        public void Know(int players = 3) => Map.Knowledge.Update(Map, players, 6);

        public void Dispose()
        {
            WarshipSelfCheck.DestroyAll(Research);
            Object.DestroyImmediate(MapGo);
            Object.DestroyImmediate(PlayerGo);
            Object.DestroyImmediate(Constants);
            Object.DestroyImmediate(Template);
        }
    }

    // Stances, hostility, effective war, pending cuts and the save entries, all without a map.
    public static bool RunDiplomacyStateCheck()
    {
        var ok = true;
        var d = new DiplomacyState();

        ok &= Check(d.StanceToward(0, 1) == Stance.Peace && Near(d.Hostility(0, 1), 0f), "a fresh pair is Peace at 0 hostility");
        ok &= Check(d.TurnsSinceChange(0, 1, 5) > 1000, "a pair that never changed has no hold (a huge number of turns since)");

        ok &= Check(d.SetStance(0, 1, Stance.War, 7), "Peace to War reports a change");
        ok &= Check(!d.SetStance(0, 1, Stance.War, 8), "War to War reports no change");
        ok &= Check(d.TurnsSinceChange(0, 1, 10) == 3, "turns since change counts from the change (7 to 10)");
        ok &= Check(d.StanceToward(1, 0) == Stance.Peace, "the opposite direction is its own stance");

        // Effective war: own War always; the rival's War only with contact.
        ok &= Check(d.IsAtWar(0, 1, false), "my own War counts without contact");
        ok &= Check(!d.IsAtWar(1, 0, false), "the rival's War does not force me without contact");
        ok &= Check(d.IsAtWar(1, 0, true), "the rival's War forces me once I have contact");
        ok &= Check(!d.IsAtWar(0, 2, true), "no stance either way: Peace");

        d.SetStance(0, 1, Stance.Peace, 20);
        ok &= Check(!d.IsAtWar(1, 0, true), "a forced war ends when the declarer returns to Peace");

        // WarRivals: contact players plus anyone I declared on or who declared on me.
        d.SetStance(0, 3, Stance.War, 30);
        d.SetStance(2, 0, Stance.War, 30);
        var war = d.WarRivals(0, new[] { 1 });
        ok &= Check(war.SetEquals(new[] { 3 }), "no contact with 2: its declaration does not count; my own War on 3 does without contact");
        var warWithContact = d.WarRivals(0, new[] { 1, 2 });
        ok &= Check(warWithContact.SetEquals(new[] { 2, 3 }), "with contact with 2 its declaration counts too");

        // Cuts are counted per (victim, blocker), consumed once, and discarded for non-contact blockers.
        d.RecordCut(0, 1); d.RecordCut(0, 1); d.RecordCut(0, 2); d.RecordCut(0, 0); d.RecordCut(0, -1);
        ok &= Check(d.TakeCuts(0, 1) == 2, "two cuts by player 1 are counted");
        ok &= Check(d.TakeCuts(0, 1) == 0, "taking cuts consumes them");
        d.DiscardCuts(0);
        ok &= Check(d.TakeCuts(0, 2) == 0, "DiscardCuts drops cuts nobody consumed");
        d.RecordCut(1, 0);
        ok &= Check(d.TakeCuts(0, 1) == 0 && d.TakeCuts(1, 0) == 1, "cuts are per victim");

        // Save entries round trip; restoring null leaves the player all Peace; another player is untouched.
        var pair = d.Get(0, 3);
        pair.Hostility = 42.5f;
        d.Set(0, 3, pair);
        var entries = d.Snapshot(0);
        var restored = new DiplomacyState();
        restored.SetStance(1, 0, Stance.War, 4);
        restored.Restore(0, entries);
        ok &= Check(restored.StanceToward(0, 3) == Stance.War && Near(restored.Hostility(0, 3), 42.5f)
                    && restored.TurnsSinceChange(0, 3, 40) == 10,
            "stance, hostility and last-change turn survive Snapshot and Restore");
        ok &= Check(restored.StanceToward(1, 0) == Stance.War, "restoring player 0 leaves player 1 alone");
        restored.Restore(0, null);
        ok &= Check(restored.StanceToward(0, 3) == Stance.Peace && Near(restored.Hostility(0, 3), 0f),
            "restoring nothing (an older save) is all Peace at 0");
        ok &= Check(d.Rivals(0).SequenceEqual(new[] { 1, 3 }), "Rivals lists the players with a recorded pair, ascending");
        return ok;
    }

    // Contact is per rival, from planets the viewer knows; ownerless ships are never a rival.
    public static bool RunContactPlayersCheck()
    {
        var ok = true;
        using (var f = Fixture.Line())
        {
            f.Know();
            ok &= Check(f.Map.Knowledge.ContactPlayers(f.Map, 0).Count == 0, "alone: no contact players");

            f.Colonize("B", 1);
            f.Ships("C", 2, 1);
            f.Ships("B", Planet.NoOwner, 1);
            f.Know();
            var contact = f.Map.Knowledge.ContactPlayers(f.Map, 0);
            ok &= Check(contact.SequenceEqual(new[] { 1, 2 }),
                "population on a known planet and a docked ship are contact; an ownerless ship is not a player");
            ok &= Check(f.Map.Knowledge.HasContactWith(f.Map, 0, 1) && f.Map.Knowledge.HasContactWith(f.Map, 0, 2)
                        && !f.Map.Knowledge.HasContactWith(f.Map, 0, 3),
                "contact is per rival");
            ok &= Check(!f.Map.Knowledge.ContactPlayers(f.Map, 0).Contains(0), "I am never my own contact");
        }
        return ok;
    }

    // A blockade cut carries the player whose offense set the blockade value, so hostility can be charged to it.
    public static bool RunCutAttributionCheck()
    {
        var ok = true;
        using (var f = Fixture.Line())
        {
            f.Ships("B", 1, 1);                       // player 1 blockades B: offense 10 against my 0
            var order = new GameAI.GameAIOrder
            {
                Type = GameAI.GameAIOrder.OrderType.OrderTypeFoodTransport,
                Origin = "A", Target = "C", PlayerId = 0, Data = 10f,
                TimingDelay = 1, TotalDelay = 2,      // halfway: passes B this turn
            };
            var orders = new List<GameAI.GameAIOrder> { order };
            var cuts = new BlockadeSystem(f.Map, f.Research).Apply(orders, 5);
            ok &= Check(cuts.Count == 1 && cuts[0].Planet == "B" && cuts[0].PlayerId == 0 && cuts[0].BlockerId == 1,
                "the cut at B names player 1 as the blocker");
        }
        return ok;
    }
}
