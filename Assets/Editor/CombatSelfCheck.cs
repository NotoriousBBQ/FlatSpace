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
    private sealed class Fixture : System.IDisposable
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

    // The nine combat tunables keep their documented in-code defaults.
    public static bool RunTunableDefaultsCheck()
    {
        var ok = true;
        var c = ScriptableObject.CreateInstance<GameAIConstants>();
        try
        {
            ok &= Check(Near(c.combatDamageK, 20f) && Near(c.repairFractionPerTurn, 0.1f), "combatDamageK 20, repairFractionPerTurn 0.1");
            ok &= Check(Near(c.hostilityPerShipLost, 1f) && c.lossWindowTurns == 10, "hostilityPerShipLost 1, lossWindowTurns 10");
            ok &= Check(Near(c.significantLossFraction, 0.3f) && Near(c.significantLossHostilityDrop, 4f),
                "significantLossFraction 0.3, significantLossHostilityDrop 4");
            ok &= Check(c.surrenderTruceTurns == 30 && Near(c.surrenderMidpoint, 0.6f) && Near(c.surrenderSteepness, 0.1f),
                "surrenderTruceTurns 30, surrenderMidpoint 0.6, surrenderSteepness 0.1");
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
