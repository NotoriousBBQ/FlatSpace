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
