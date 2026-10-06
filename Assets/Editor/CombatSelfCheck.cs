using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using FlatSpace.AI;
using Flatspace.Objects.Production;

public static class CombatSelfCheck
{
    [MenuItem("FlatSpace/AI/Run Combat Self-Check")]
    public static void Run() => RunChecks();

    public static bool RunChecks()
    {
        var ok = RunTunableDefaultsCheck();
        ok &= RunEffectiveStatsCheck();
        ok &= RunDamageTravelsCheck();
        ok &= RunEffectiveReadersCheck();
        ok &= RunCombatResolutionCheck();
        ok &= RunRepairAndColonyShipCheck();
        ok &= RunRoundCheck();
        Debug.Log(ok
            ? "[CombatSelfCheck] ALL PASSED"
            : "[CombatSelfCheck] FAILURES (see errors above)");
        return ok;
    }

    private static bool Check(bool condition, string label)
    {
        if (!condition) Debug.LogError($"[CombatSelfCheck] FAIL: {label}");
        return condition;
    }

    private static bool Near(float a, float b) => Mathf.Abs(a - b) < 0.01f;

    // A(0,0) - B(100,0) - C(200,0) - D(300,0). Diplomacy is switched on (a map a fixture builds is legacy mode otherwise).
    // Template: Offense 10, Health 100, Defense 5; with combatDamageK 20 a hit does 0.8 of its offense.
    public sealed class Fixture : System.IDisposable
    {
        public GameObject MapGo;
        public GameAIMap Map;
        public GameAIConstants Constants;
        public ShipData Template;
        public List<CatalogItem> Research;
        public WarshipStats Stats;

        public static Fixture Line()
        {
            var f = new Fixture();
            f.Template = WarshipSelfCheck.MakeTemplate();
            f.Constants = WarshipSelfCheck.MakeConstants(f.Template);
            f.Research = WarshipSelfCheck.MakeResearch();
            f.Stats = new WarshipStats(f.Research);
            f.MapGo = new GameObject("CombatSelfCheckMap");
            f.Map = f.MapGo.AddComponent<GameAIMap>();
            f.Map.GameAIMapInit(new List<PlanetSpawnData>
            {
                ChokepointSelfCheck.Spawn("A", 0f, 0f, new[] { "B" }),
                ChokepointSelfCheck.Spawn("B", 100f, 0f, new[] { "A", "C" }),
                ChokepointSelfCheck.Spawn("C", 200f, 0f, new[] { "B", "D" }),
                ChokepointSelfCheck.Spawn("D", 300f, 0f, new[] { "C" }),
            }, f.Constants);
            f.Map.Diplomacy.Enabled = true;
            return f;
        }

        public Planet P(string name) => Map.GetPlanet(name);

        public void Colonize(string name, int player)
        {
            var planet = P(name);
            planet.Owner = player;
            planet.Population.Add(new Planet.Inhabitant { Player = player });
        }

        // Docks `count` warships for `owner`; each gets `damage` (0 = full health).
        public void Ships(string planet, int owner, int count, float damage = 0f)
        {
            var p = P(planet);
            var before = p.DockedShips.Count;
            WarshipSelfCheck.DockWarships(p, owner, count);
            for (var i = before; i < p.DockedShips.Count; i++) p.DockedShips[i].Damage = damage;
        }

        public void War(int a, int b) => Map.Diplomacy.SetStance(a, b, Stance.War, 0);

        public void Dispose()
        {
            WarshipSelfCheck.DestroyAll(Research);
            Object.DestroyImmediate(MapGo);
            Object.DestroyImmediate(Constants);
            Object.DestroyImmediate(Template);
        }
    }

    // The pure core: units in, damage / destroyed / per-attacker losses out, nothing mutated.
    public static bool RunRoundCheck()
    {
        var ok = true;
        CombatUnit Unit(int id, int owner) => CombatUnit.Make(id, owner, id, 10f, 100f, 5f, 0f);
        System.Func<int, int, bool> war = (a, b) => a != b;
        System.Func<int, int, float> hostility = (a, b) => 1f;

        var outcome = CombatSystem.Round(new List<CombatUnit> { Unit(0, 0), Unit(1, 1) }, war, hostility, 20f);
        ok &= Check(outcome.Destroyed.Count == 0 && Near(outcome.Damage[0], 8f) && Near(outcome.Damage[1], 8f),
            "1 against 1, offense 10 vs Defense 5, K 20: each takes 10 x 0.8 = 8, nobody dies");
        ok &= Check(outcome.Losses.Count == 2 && outcome.Losses.All(l => Near(l.Engaged, 1050f) && l.Ships == 0f && l.StrengthLost == 0f),
            "one loss entry per directed pair; the victim's engaged strength is 10 x (100 + 5) = 1050; nothing died");

        var weak = CombatUnit.Make(1, 1, 1, 10f, 100f, 5f, 90f);   // health 10, offense 1, strength 15
        var lethal = CombatSystem.Round(new List<CombatUnit> { Unit(0, 0), weak }, war, hostility, 20f);
        ok &= Check(lethal.Destroyed.Count == 0 && Near(lethal.Damage[1], 8f), "a 10 pool needs 12.5 to kill a ship with 10 health: it survives with 8 more damage");
        var bigPool = new List<CombatUnit> { Unit(0, 0), CombatUnit.Make(2, 0, 2, 10f, 100f, 5f, 0f), CombatUnit.Make(3, 0, 3, 10f, 100f, 5f, 0f), weak };
        var kill = CombatSystem.Round(bigPool, war, hostility, 20f);
        ok &= Check(kill.Destroyed.SequenceEqual(new[] { 1 }) && kill.Losses.Any(l => l.Victim == 1 && Near(l.Ships, 1f) && Near(l.StrengthLost, weak.Strength)),
            "a 30 pool kills the 10-health ship (12.5 needed): destroyed, one ship and its strength attributed to the attacker");

        ok &= Check(CombatSystem.Round(new List<CombatUnit> { Unit(0, 0), Unit(1, 0) }, war, hostility, 20f).Losses.Count == 0,
            "one player alone: no fight, an empty outcome");
        return ok;
    }

    // The nine combat tunables keep their documented in-code defaults.
    public static bool RunTunableDefaultsCheck()
    {
        var ok = true;
        var c = ScriptableObject.CreateInstance<GameAIConstants>();
        try
        {
            ok &= Check(Near(c.combatDamageK, 20f) && Near(c.repairFractionPerTurn, 0.1f), "combatDamageK 20, repairFractionPerTurn 0.1");
            ok &= Check(Near(c.hostilityPerShipLost, 1f) && c.lossWindowTurns == 10, "hostilityPerShipLost 1, lossWindowTurns 10");
            ok &= Check(Near(c.significantLossFraction, 0.08f) && Near(c.significantLossHostilityDrop, 4f),
                "significantLossFraction 0.08, significantLossHostilityDrop 4");
            ok &= Check(c.surrenderTruceTurns == 30 && Near(c.surrenderMidpoint, 0.15f) && Near(c.surrenderSteepness, 0.015f),
                "surrenderTruceTurns 30, surrenderMidpoint 0.15, surrenderSteepness 0.015");
            // The default curve sits in the range real fights reach (a per-rival 10-turn loss share of 0.02-0.14 in the 2026-10-05 logs on both boards):
            // no surrender without losses, a real chance at a heavy loss, near certain beyond it.
            ok &= Check(StanceMatrix.SurrenderWeight(0f, c) < 0.0001f, "no losses: the surrender weight is below 0.0001 (it was 0.002, which fired at share 0)");
            ok &= Check(StanceMatrix.SurrenderWeight(0.05f, c) < 0.005f, "a 5% loss share: under 0.5% a turn");
            ok &= Check(StanceMatrix.SurrenderWeight(0.1f, c) > 0.01f && StanceMatrix.SurrenderWeight(0.1f, c) < 0.1f, "a 10% loss share: a few percent");
            ok &= Check(Near(StanceMatrix.SurrenderWeight(0.15f, c), 0.5f), "a 15% loss share: even odds");
            ok &= Check(StanceMatrix.SurrenderWeight(0.2f, c) > 0.95f, "a 20% loss share: near certain");
        }
        finally { Object.DestroyImmediate(c); }
        return ok;
    }

    // Damage travels: it is saved with a docked ship, rides in the fleet payload (ToSave, FromSave, departure, arrival) and a
    // fleet with a short or missing Damage list docks the rest at full health.
    public static bool RunDamageTravelsCheck()
    {
        var ok = true;
        using (var f = Fixture.Line())
        {
            var a = f.P("A");
            a.DockShipFromSave(Ship.ShipKind.WarShip, 0, new List<string>(), 25f);
            ok &= Check(Near(a.DockedShips[0].Damage, 25f), "DockShipFromSave with a damage argument sets Ship.Damage");
            a.DockShipFromSave(Ship.ShipKind.WarShip, 0, new List<string>());
            ok &= Check(Near(a.DockedShips[1].Damage, 0f), "the old three-argument call docks at full health");

            // Peek reads the damage in the same order as the snapshots, with skip.
            var b = f.P("B");
            f.Ships("B", 0, 1, 10f); f.Ships("B", 0, 1, 20f); f.Ships("B", 0, 1, 30f); f.Ships("B", 1, 1, 99f);
            var damages = b.PeekShipDamage(Ship.ShipKind.WarShip, 0, 2);
            ok &= Check(damages.Count == 2 && Near(damages[0], 10f) && Near(damages[1], 20f), "peek gives the first two ships' damage, in dock order");
            var skipped = b.PeekShipDamage(Ship.ShipKind.WarShip, 0, 2, 1);
            ok &= Check(skipped.Count == 2 && Near(skipped[0], 20f) && Near(skipped[1], 30f), "peek with skip 1 gives the next two");
            ok &= Check(b.PeekShipSnapshots(Ship.ShipKind.WarShip, 0, 2).Count == 2, "the snapshot peek is unchanged");

            // The ships that leave are the ships the payload was read from.
            var leave = new GameAI.GameAIOrder { Type = GameAI.GameAIOrder.OrderType.OrderTypeShipDeparture, PlayerId = 0, Data = 2 };
            GameAI.ApplyShipDeparture(b, leave);
            var remaining = b.DockedShips.Where(s => s.Owner == 0).ToList();
            ok &= Check(remaining.Count == 1 && Near(remaining[0].Damage, 30f), "departure removes the first two ships, the third (damage 30) stays");

            // Arrival docks each ship with the damage it left with; a short list docks the rest at full health.
            var arrive = new GameAI.GameAIOrder
            {
                Type = GameAI.GameAIOrder.OrderType.OrderTypeShipTransport,
                PlayerId = 1,
                Data = 2,
                Fleet = new GameAI.GameAIOrder.ShipFleetPayload
                {
                    Kind = Ship.ShipKind.WarShip,
                    Snapshots = new List<List<string>> { new List<string>(), new List<string>() },
                    Damage = new List<float> { 40f, 0f },
                },
            };
            var c = f.P("C");
            GameAI.ApplyShipArrival(c, arrive);
            ok &= Check(c.DockedShips.Count == 2 && Near(c.DockedShips[0].Damage, 40f) && Near(c.DockedShips[1].Damage, 0f),
                "arrival docks the wounded ship wounded (40) and the other at full health");
            arrive.Fleet.Damage = new List<float> { 15f };
            var d = f.P("D");
            GameAI.ApplyShipArrival(d, arrive);
            ok &= Check(d.DockedShips.Count == 2 && Near(d.DockedShips[0].Damage, 15f) && Near(d.DockedShips[1].Damage, 0f),
                "a Damage list shorter than the fleet docks the rest at full health");

            // Payload save round trip, and an older save with no damage.
            var payload = new GameAI.GameAIOrder.ShipFleetPayload
            {
                Kind = Ship.ShipKind.WarShip,
                Snapshots = new List<List<string>> { new List<string> { "x" }, new List<string> { "y" } },
                Damage = new List<float> { 12.5f, 0f },
            };
            var saved = payload.ToSave(3);
            ok &= Check(saved.Count == 2 && Near(saved[0].damage, 12.5f) && Near(saved[1].damage, 0f) && saved[0].owner == 3,
                "ToSave writes each ship's damage");
            var restored = GameAI.GameAIOrder.ShipFleetPayload.FromSave(saved);
            ok &= Check(restored.Damage.Count == 2 && Near(restored.Damage[0], 12.5f) && Near(restored.DamageAt(1), 0f)
                        && Near(restored.DamageAt(7), 0f),
                "FromSave restores the damage list; DamageAt past the end is 0");
            var viaJson = JsonUtility.FromJson<SaveLoadSystem.GameSave.ShipSave>(JsonUtility.ToJson(saved[0]));
            ok &= Check(Near(viaJson.damage, 12.5f), "damage survives JsonUtility");
            var old = JsonUtility.FromJson<SaveLoadSystem.GameSave.ShipSave>("{\"kind\":1,\"owner\":0}");
            ok &= Check(Near(old.damage, 0f), "an older ship save with no damage key loads at full health");
        }

        // EmitShipOrders puts the departing ships' damage in the payload.
        using (var f = Fixture.Line())
        {
            f.Ships("A", 0, 1, 10f); f.Ships("A", 0, 1, 20f); f.Ships("A", 0, 1, 30f);
            var go = new GameObject("CombatSelfCheckPlayer");
            try
            {
                var player = go.AddComponent<Player>();
                var ai = go.AddComponent<PlayerAI>();
                ai.Player = player;
                ai.AIMap = f.Map;
                player.playerID = 0;
                var orders = new List<GameAI.GameAIOrder>();
                ai.EmitShipOrders(new ShipAction { Origin = "A", Target = "B", Cost = 100f, Count = 2, Kind = Ship.ShipKind.WarShip }, orders);
                var transport = orders.Find(o => o.Type == GameAI.GameAIOrder.OrderType.OrderTypeShipTransport);
                ok &= Check(transport != null && transport.Fleet.Damage.Count == 2 && Near(transport.Fleet.Damage[0], 10f)
                            && Near(transport.Fleet.Damage[1], 20f),
                    "a fleet order carries the damage of the ships it takes (10 and 20)");
            }
            finally { Object.DestroyImmediate(go); }
        }
        return ok;
    }

    // Every reader of a ship's offense sees the effective (damaged) value. Undamaged ships are unchanged (the old checks pin that).
    public static bool RunEffectiveReadersCheck()
    {
        var ok = true;
        using (var f = Fixture.Line())
        {
            f.Ships("A", 0, 1);             // Off 10, health 100, Def 5: strength 10 x 105 = 1050
            f.Ships("A", 0, 1, 50f);        // Off 5, health 50, Def 5: strength 5 x 55 = 275
            ok &= Check(Near(FleetStrength.Of(f.P("A").DockedShips.Where(s => s.Owner == 0), f.Stats), 1325f),
                "fleet strength = effective offense x (current health + Defense): 1050 + 275");

            var blockade = new BlockadeSystem(f.Map, f.Research);
            ok &= Check(Near(blockade.DockedOffense(f.P("A"), 0), 15f), "blockade value counts effective offense: 10 + 5");

            var planner = new AssaultPlanner(f.Map, 0, null, f.Stats);
            ok &= Check(Near(planner.CommittedOffense(f.P("A")), 15f), "the assault's committed offense counts effective offense");

            var orders = new List<GameAI.GameAIOrder>
            {
                new GameAI.GameAIOrder
                {
                    Type = GameAI.GameAIOrder.OrderType.OrderTypeShipTransport,
                    PlayerId = 0,
                    Data = 2,
                    Target = "B",
                    Fleet = new GameAI.GameAIOrder.ShipFleetPayload
                    {
                        Kind = Ship.ShipKind.WarShip,
                        Snapshots = new List<List<string>> { new List<string>(), new List<string>() },
                        Damage = new List<float> { 50f, 0f },
                    },
                },
            };
            f.Map.RecomputeIncomingOffense(orders, f.Stats);
            ok &= Check(Near(f.P("B").GetIncomingOffense(Ship.ShipKind.WarShip, 0), 15f),
                "in-flight offense uses the payload's damage: 5 + 10");

            f.Template.shipName = "Warship";
            ok &= Check(FleetUIController.FormatShipRow(f.P("A").DockedShips[1], f.Research)
                        == "WarShip - Warship (Spd 150, HP 50/100, Off 5/10, Def 5)",
                "a damaged ship's fleet row shows current/maximum health and effective/base offense");
            ok &= Check(FleetUIController.FormatShipRow(f.P("A").DockedShips[0], f.Research)
                        == "WarShip - Warship (Spd 150, HP 100, Off 10, Def 5)",
                "an undamaged ship's row is unchanged");
        }
        return ok;
    }

    private static List<Planet.UpdateResult> Results() => new List<Planet.UpdateResult>();

    private static List<CombatReport> Resolve(Fixture f, List<Planet.UpdateResult> results)
        => CombatSystem.Resolve(f.Map, f.Stats, f.Constants, 1, results);

    private static float DamageOf(Fixture f, string planet, int owner, int index)
        => f.P(planet).DockedShips.Where(s => s.Owner == owner).ElementAt(index).Damage;

    private const Planet.UpdateResult.UpdateResultType WarshipsLost =
        Planet.UpdateResult.UpdateResultType.UpdateResultTypeWarshipsLost;

    // Who fights, how much damage lands, which ship it lands on, simultaneity, the hostility-weighted split, one pool per victim.
    public static bool RunCombatResolutionCheck()
    {
        var ok = true;

        using (var f = Fixture.Line())    // peace: nothing happens
        {
            f.Ships("A", 0, 2); f.Ships("A", 1, 2);
            var results = Results();
            ok &= Check(Resolve(f, results).Count == 0 && results.Count == 0 && DamageOf(f, "A", 0, 0) == 0f,
                "players at peace do not fight");
        }

        using (var f = Fixture.Line())    // war 3 against 2: simultaneous, first ship takes it all (factor 20/25 = 0.8)
        {
            f.Ships("A", 0, 3); f.Ships("A", 1, 2); f.War(0, 1);
            var results = Results();
            var reports = Resolve(f, results);
            ok &= Check(Near(DamageOf(f, "A", 1, 0), 24f) && Near(DamageOf(f, "A", 1, 1), 0f),
                "player 0's pool of 30 lands on player 1's first ship: 30 x 0.8 = 24");
            ok &= Check(Near(DamageOf(f, "A", 0, 0), 16f) && Near(DamageOf(f, "A", 0, 1), 0f),
                "simultaneous: player 1's pool of 20 lands on player 0's first ship: 16");
            ok &= Check(results.Count == 2 && results.All(r => r.Result == WarshipsLost && ((CombatLoss)r.Data).Ships == 0f),
                "no ship died, but a fight turn still appends a zero-loss WarshipsLost result per victim");
            ok &= Check(Near(((CombatLoss)results.First(r => r.PlayerID == 0).Data).Engaged, 3150f)
                        && Near(((CombatLoss)results.First(r => r.PlayerID == 1).Data).Engaged, 2100f),
                "each result carries its victim's engaged strength: player 0's 3 ships 3150, player 1's 2 ships 2100");
            ok &= Check(reports.Count == 2 && Near(reports.First(r => r.Attacker == 0).DamageDealt, 24f)
                        && Near(reports.First(r => r.Attacker == 1).DamageDealt, 16f),
                "one report per directed pair with the damage dealt");
        }

        using (var f = Fixture.Line())    // focus fire: most damaged first, overflow carries, a dying ship still fires
        {
            f.Ships("A", 0, 10);
            f.Ships("A", 1, 1, 90f);      // dock order first: health 10
            f.Ships("A", 1, 1);           // health 100
            f.War(0, 1);
            var results = Results();
            Resolve(f, results);
            var survivors = f.P("A").DockedShips.Where(s => s.Owner == 1).ToList();
            ok &= Check(survivors.Count == 1 && Near(survivors[0].Damage, 70f),
                "pool 100: the weakest ship (needs 10 / 0.8 = 12.5) dies, the rest 87.5 x 0.8 = 70 lands on the next");
            ok &= Check(Near(DamageOf(f, "A", 0, 0), 8.8f),
                "the dying ship still fired this turn: 1 + 10 = 11 offense, x 0.8 = 8.8 on player 0's first ship");
            var loss = results.Where(r => r.Result == WarshipsLost && r.PlayerID == 1).ToList();
            ok &= Check(loss.Count == 1 && loss[0].PlayerID == 1 && loss[0].Name == "A", "one WarshipsLost result, victim player 1, at A");
            var data = loss.Count == 1 ? (CombatLoss)loss[0].Data : null;
            ok &= Check(data != null && data.Attacker == 0 && Near(data.Ships, 1f) && Near(data.StrengthLost, 15f),
                "attacker 0, 1 ship, strength 15 (effective offense 1 x (health 10 + Defense 5))");
        }

        using (var f = Fixture.Line())    // defense: Defense 15 gives 20/35
        {
            f.Ships("A", 0, 3);
            f.P("A").DockShipFromSave(Ship.ShipKind.WarShip, 1, new List<string> { "Def 1", "Def 2", "Def 3", "Def 4", "Def 5" });
            f.War(0, 1);
            Resolve(f, Results());
            ok &= Check(Near(DamageOf(f, "A", 1, 0), 30f * 20f / 35f), "Defense 15 lowers a hit to 20/35 of its offense");
        }

        foreach (var reversedDock in new[] { false, true })    // three players: hostility-weighted split, one pool per victim
        {
            using (var f = Fixture.Line())
            {
                foreach (var owner in reversedDock ? new[] { 2, 1, 0 } : new[] { 0, 1, 2 }) f.Ships("A", owner, 1);
                f.War(0, 1); f.War(0, 2);
                var h01 = f.Map.Diplomacy.Get(0, 1); h01.Hostility = 30f; f.Map.Diplomacy.Set(0, 1, h01);
                var h02 = f.Map.Diplomacy.Get(0, 2); h02.Hostility = 10f; f.Map.Diplomacy.Set(0, 2, h02);
                Resolve(f, Results());
                var tag = reversedDock ? " (docked in reverse order)" : "";
                ok &= Check(Near(DamageOf(f, "A", 1, 0), 6f), "player 0's pool of 10 splits 30:10, so player 1 takes 7.5 x 0.8 = 6" + tag);
                ok &= Check(Near(DamageOf(f, "A", 2, 0), 2f), "and player 2 takes 2.5 x 0.8 = 2" + tag);
                ok &= Check(Near(DamageOf(f, "A", 0, 0), 16f),
                    "one pool per victim: players 1 and 2 are at war with 0 only, their 10 + 10 land together: 20 x 0.8 = 16" + tag);
            }
        }

        using (var f = Fixture.Line())    // attribution: the victim's loss is shared by each attacker's part of its pool
        {
            f.Ships("A", 0, 3); f.Ships("A", 2, 1); f.Ships("A", 1, 1, 95f);
            f.War(0, 1); f.War(2, 1);
            var results = Results();
            Resolve(f, results);
            var losses = results.Where(r => r.Result == WarshipsLost && r.PlayerID == 1).OrderBy(r => ((CombatLoss)r.Data).Attacker).ToList();
            ok &= Check(losses.Count == 2 && ((CombatLoss)losses[0].Data).Attacker == 0 && ((CombatLoss)losses[1].Data).Attacker == 2,
                "one loss result per attacker");
            ok &= Check(losses.Count == 2 && Near(((CombatLoss)losses[0].Data).Ships, 0.75f) && Near(((CombatLoss)losses[1].Data).Ships, 0.25f),
                "the one ship lost is shared 30:10 = 0.75 and 0.25");
            ok &= Check(losses.Count == 2 && Near(((CombatLoss)losses[0].Data).StrengthLost, 3.75f)
                        && Near(((CombatLoss)losses[1].Data).StrengthLost, 1.25f),
                "its strength 0.5 x (5 + 5) = 5 is shared the same way");
            ok &= Check(losses.Count == 2 && Near(((CombatLoss)losses[0].Data).Engaged, 3.75f) && Near(((CombatLoss)losses[1].Data).Engaged, 1.25f),
                "the victim's engaged strength 5 is shared 30:10 like its loss, so two attackers count it once in total");
        }

        using (var f = Fixture.Line())    // legacy mode: no stances, no combat
        {
            f.Ships("A", 0, 2); f.Ships("A", 1, 2); f.War(0, 1);
            f.Map.Diplomacy.Enabled = false;
            var results = Results();
            ok &= Check(Resolve(f, results).Count == 0 && DamageOf(f, "A", 1, 0) == 0f && results.Count == 0, "legacy mode: combat is off");
        }

        using (var f = Fixture.Line())    // ownerless ships neither fire nor take fire
        {
            f.Ships("A", 0, 1); f.Ships("A", 1, 1); f.Ships("A", Planet.NoOwner, 5); f.War(0, 1);
            Resolve(f, Results());
            ok &= Check(Near(DamageOf(f, "A", 1, 0), 8f), "ownerless ships add nothing to a pool: 10 x 0.8 = 8");
            ok &= Check(Near(DamageOf(f, "A", Planet.NoOwner, 0), 0f), "and take no damage");
        }

        using (var f = Fixture.Line())    // a Health stat of 0: no division by zero, takes no part
        {
            f.Ships("A", 0, 1); f.Ships("A", 1, 1); f.Ships("A", 1, 1); f.War(0, 1);
            var zero = WarshipSelfCheck.MakeTemplate();
            zero.shipHealth = 0f; zero.shipHealthMax = 0f;
            f.P("A").DockedShips.Where(s => s.Owner == 1).Last().Template = zero;
            Resolve(f, Results());
            ok &= Check(Near(DamageOf(f, "A", 1, 0), 8f) && Near(DamageOf(f, "A", 1, 1), 0f),
                "the ship with no Health stat is never targeted and the real ship takes the 8");
            Object.DestroyImmediate(zero);
        }
        return ok;
    }

    // Repair: gradual, at home, not with an at-war enemy present. Colony ships: destroyed when the owner has no warship left
    // and an at-war rival has one, everywhere, after combat.
    public static bool RunRepairAndColonyShipCheck()
    {
        var ok = true;
        const Planet.UpdateResult.UpdateResultType colonyLost =
            Planet.UpdateResult.UpdateResultType.UpdateResultTypeColonyShipsLost;

        using (var f = Fixture.Line())    // repair at home: 50 - 0.1 x 100 = 40
        {
            f.Colonize("A", 0);
            f.Ships("A", 0, 1, 50f);
            Resolve(f, Results());
            ok &= Check(Near(DamageOf(f, "A", 0, 0), 40f), "a damaged ship at its owner's populated planet heals 10% of its Health stat a turn");
            for (var i = 0; i < 5; i++) Resolve(f, Results());
            ok &= Check(Near(DamageOf(f, "A", 0, 0), 0f), "and never below 0 damage");
        }

        using (var f = Fixture.Line())    // no repair away from home
        {
            f.Colonize("A", 1);            // someone else's planet
            f.Ships("A", 0, 1, 50f);
            Resolve(f, Results());
            ok &= Check(Near(DamageOf(f, "A", 0, 0), 50f), "no repair at a planet the owner does not hold");
        }
        using (var f = Fixture.Line())
        {
            f.P("A").Owner = 0;             // owned but unpopulated
            f.Ships("A", 0, 1, 50f);
            Resolve(f, Results());
            ok &= Check(Near(DamageOf(f, "A", 0, 0), 50f), "no repair at an unpopulated planet");
        }

        using (var f = Fixture.Line())    // an at-war enemy present: no repair (the enemy's 8 damage lands, nothing heals)
        {
            f.Colonize("A", 0);
            f.Ships("A", 0, 1, 50f); f.Ships("A", 1, 1); f.War(0, 1);
            Resolve(f, Results());
            ok &= Check(Near(DamageOf(f, "A", 0, 0), 58f), "with an at-war enemy docked there: 50 + 8 and no healing");
        }
        using (var f = Fixture.Line())    // a peaceful rival does not stop repair
        {
            f.Colonize("A", 0);
            f.Ships("A", 0, 1, 50f); f.Ships("A", 1, 1);
            Resolve(f, Results());
            ok &= Check(Near(DamageOf(f, "A", 0, 0), 40f), "a rival at peace does not stop repair");
        }

        using (var f = Fixture.Line())    // colony ship, owner has no warship, at-war rival arrives alone: destroyed, even on its own planet
        {
            f.Colonize("A", 0);
            f.P("A").DockShipFromSave(Ship.ShipKind.ColonyShip, 0, new List<string>());
            f.Ships("A", 1, 1); f.War(0, 1);
            var results = Results();
            Resolve(f, results);
            var lost = results.Where(r => r.Result == colonyLost).ToList();
            ok &= Check(f.P("A").DockedShips.All(s => s.Kind != Ship.ShipKind.ColonyShip),
                "the owner's colony ship is destroyed when its planet holds no warship of the owner and an at-war rival has one");
            ok &= Check(lost.Count == 1 && lost[0].PlayerID == 0 && ((ColonyLoss)lost[0].Data).Count == 1 && ((ColonyLoss)lost[0].Data).ByPlayer == 1,
                "one ColonyShipsLost result: owner 0, 1 ship, by player 1");
        }
        using (var f = Fixture.Line())    // the owner keeps a warship: the colony ship lives
        {
            f.P("A").DockShipFromSave(Ship.ShipKind.ColonyShip, 0, new List<string>());
            f.Ships("A", 0, 1); f.Ships("A", 1, 1); f.War(0, 1);
            Resolve(f, Results());
            ok &= Check(f.P("A").DockedShips.Any(s => s.Kind == Ship.ShipKind.ColonyShip), "a surviving warship protects the colony ship");
        }
        using (var f = Fixture.Line())    // the owner's last warship dies in the same turn: the rule runs after combat
        {
            f.P("A").DockShipFromSave(Ship.ShipKind.ColonyShip, 0, new List<string>());
            f.Ships("A", 0, 1, 95f); f.Ships("A", 1, 3); f.War(0, 1);
            Resolve(f, Results());
            ok &= Check(f.P("A").DockedShips.All(s => s.Owner != 0), "the last warship and then the colony ship are gone in the same turn");
        }
        using (var f = Fixture.Line())    // peace, or only a rival colony ship, or legacy: nothing happens
        {
            f.P("A").DockShipFromSave(Ship.ShipKind.ColonyShip, 0, new List<string>());
            f.Ships("A", 1, 1);
            Resolve(f, Results());
            ok &= Check(f.P("A").DockedShips.Any(s => s.Kind == Ship.ShipKind.ColonyShip && s.Owner == 0), "a rival at peace does not kill colony ships");
            f.War(0, 1);
            f.P("A").UndockShips(Ship.ShipKind.WarShip, 1, 9);
            f.P("A").DockShipFromSave(Ship.ShipKind.ColonyShip, 1, new List<string>());
            Resolve(f, Results());
            ok &= Check(f.P("A").DockedShips.Count(s => s.Kind == Ship.ShipKind.ColonyShip) == 2, "a rival's colony ship is not a warship: nothing dies");
            f.P("A").DockShipFromSave(Ship.ShipKind.WarShip, 1, new List<string>());
            f.Map.Diplomacy.Enabled = false;
            Resolve(f, Results());
            ok &= Check(f.P("A").DockedShips.Count(s => s.Kind == Ship.ShipKind.ColonyShip) == 2, "legacy mode: nothing dies");
        }
        return ok;
    }

    // Effective offense = Offense x current health / Health stat, no floor; damage is stored, current health derived.
    public static bool RunEffectiveStatsCheck()
    {
        var ok = true;
        using (var f = Fixture.Line())
        {
            var none = new List<string>();
            ok &= Check(Near(f.Stats.EffectiveOffense(f.Template, none, 0f), 10f), "undamaged: the plain offense");
            ok &= Check(Near(f.Stats.EffectiveOffense(f.Template, none, 50f), 5f), "half the health, half the offense");
            ok &= Check(Near(f.Stats.EffectiveOffense(f.Template, none, 90f), 1f), "no floor: 10% health is 10% offense");
            ok &= Check(Near(f.Stats.EffectiveOffense(f.Template, none, 100f), 0f), "no health left: no offense");
            ok &= Check(Near(f.Stats.EffectiveOffense(f.Template, none, 150f), 0f), "damage beyond the Health stat never goes negative");
            ok &= Check(Near(f.Stats.CurrentHealth(f.Template, none, 30f), 70f) && Near(f.Stats.CurrentHealth(f.Template, none, 150f), 0f),
                "current health is the Health stat minus damage, never below 0");
            ok &= Check(f.Stats.EffectiveOffense(null, none, 0f) == 0f && f.Stats.CurrentHealth(null, none, 0f) == 0f,
                "a null template has no offense and no health, no crash");

            var zeroHealth = WarshipSelfCheck.MakeTemplate();
            zeroHealth.shipHealth = 0f;
            zeroHealth.shipHealthMax = 0f;
            ok &= Check(f.Stats.EffectiveOffense(zeroHealth, none, 0f) == 0f, "a Health stat of 0 gives 0 offense, no division by zero");
            Object.DestroyImmediate(zeroHealth);

            var hp5 = new List<string> { "Hp 1", "Hp 2", "Hp 3", "Hp 4", "Hp 5" };
            ok &= Check(Near(f.Stats.CurrentHealth(f.Template, hp5, 0f), 200f) && Near(f.Stats.EffectiveOffense(f.Template, hp5, 100f), 5f),
                "the Health stat comes from the ship's research snapshot: 200 max, 100 damage is half the offense");

            f.Ships("A", 0, 1, 25f);
            ok &= Check(Near(f.Stats.EffectiveOffense(f.P("A").DockedShips[0]), 7.5f)
                        && Near(f.Stats.CurrentHealth(f.P("A").DockedShips[0]), 75f),
                "the Ship overloads read the ship's own template, snapshot and Damage");
        }
        return ok;
    }
}
