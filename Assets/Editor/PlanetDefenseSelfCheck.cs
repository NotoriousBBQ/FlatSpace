using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using FlatSpace.AI;

public static class PlanetDefenseSelfCheck
{
    [MenuItem("FlatSpace/AI/Run Planet Defense Self-Check")]
    public static void Run() => RunChecks();

    public static bool RunChecks()
    {
        var ok = RunKindsAndDefaultsCheck();
        Debug.Log(ok
            ? "[PlanetDefenseSelfCheck] ALL PASSED"
            : "[PlanetDefenseSelfCheck] FAILURES (see errors above)");
        return ok;
    }

    private static bool Check(bool condition, string label)
    {
        if (!condition) Debug.LogError($"[PlanetDefenseSelfCheck] FAIL: {label}");
        return condition;
    }

    public static bool RunKindsAndDefaultsCheck()
    {
        var ok = true;
        ok &= Check((int)Ship.ShipKind.ColonyShip == 0 && (int)Ship.ShipKind.WarShip == 1, "existing kinds keep their serialized ints");
        ok &= Check((int)Ship.ShipKind.SiegeShip == 2, "SiegeShip is appended as 2");
        ok &= Check((int)Ship.ShipKind.Bunker == 3, "Bunker is appended as 3");

        var constants = ScriptableObject.CreateInstance<GameAIConstants>();
        try
        {
            ok &= Check(constants.siegeshipCostRatio == 1.5f, "siegeshipCostRatio defaults to 1.5");
            ok &= Check(constants.bunkerCostRatio == 1.5f, "bunkerCostRatio defaults to 1.5");
        }
        finally { Object.DestroyImmediate(constants); }

        ok &= Check(Ship.IsFightingKind(Ship.ShipKind.WarShip), "WarShip is a fighting kind");
        ok &= Check(Ship.IsFightingKind(Ship.ShipKind.SiegeShip), "SiegeShip is a fighting kind");
        ok &= Check(!Ship.IsFightingKind(Ship.ShipKind.ColonyShip), "ColonyShip is not a fighting kind");
        ok &= Check(!Ship.IsFightingKind(Ship.ShipKind.Bunker), "Bunker is not a fighting kind");
        return ok;
    }

    // Same line map as CombatSelfCheck.Fixture (A-B-C-D, diplomacy on) plus Siegeship and Bunker templates.
    // Warship template: speed 150, health 100/200, defense 5/15, offense 10/30 (bombard per WarshipSelfCheck.MakeTemplate).
    public sealed class Fixture : System.IDisposable
    {
        public GameObject MapGo;
        public GameAIMap Map;
        public GameAIConstants Constants;
        public ShipData Template;
        public ShipData SiegeTemplate;
        public ShipData BunkerTemplate;
        public List<CatalogItem> Research;
        public WarshipStats Stats;

        public static Fixture Line()
        {
            var f = new Fixture();
            f.Template = WarshipSelfCheck.MakeTemplate();
            f.SiegeTemplate = MakeData(120f, 100f, 200f, 3f, 9f, 3f, 9f, 20f, 60f);
            f.BunkerTemplate = MakeData(0f, 150f, 300f, 8f, 20f, 12f, 36f, 0f, 0f);
            f.Constants = WarshipSelfCheck.MakeConstants(f.Template);
            f.Constants.siegeShipData = f.SiegeTemplate;
            f.Constants.bunkerData = f.BunkerTemplate;
            f.Research = WarshipSelfCheck.MakeResearch();
            f.Stats = new WarshipStats(f.Research);
            f.MapGo = new GameObject("PlanetDefenseSelfCheckMap");
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

        private static ShipData MakeData(float speed, float hp, float hpMax, float def, float defMax,
            float off, float offMax, float bombard, float bombardMax)
        {
            var t = ScriptableObject.CreateInstance<ShipData>();
            t.shipSpeed = speed;
            t.shipHealth = hp; t.shipHealthMax = hpMax;
            t.shipDefense = def; t.shipDefenseMax = defMax;
            t.shipOffense = off; t.shipOffenseMax = offMax;
            t.shipBombard = bombard; t.shipBombardMax = bombardMax;
            return t;
        }

        public Planet P(string name) => Map.GetPlanet(name);

        public void Colonize(string name, int player)
        {
            var planet = P(name);
            planet.Owner = player;
            planet.Population.Add(new Planet.Inhabitant { Player = player });
        }

        private void Dock(string planet, Ship.ShipKind kind, int owner, int count, float damage)
        {
            var p = P(planet);
            for (var i = 0; i < count; i++)
            {
                p.DockShipFromSave(kind, owner, new List<string>(), damage);
            }
        }

        public void Ships(string planet, int owner, int count, float damage = 0f) => Dock(planet, Ship.ShipKind.WarShip, owner, count, damage);
        public void Bunkers(string planet, int owner, int count, float damage = 0f) => Dock(planet, Ship.ShipKind.Bunker, owner, count, damage);
        public void Siege(string planet, int owner, int count, float damage = 0f) => Dock(planet, Ship.ShipKind.SiegeShip, owner, count, damage);

        public void War(int a, int b) => Map.Diplomacy.SetStance(a, b, Stance.War, 0);

        public void Dispose()
        {
            WarshipSelfCheck.DestroyAll(Research);
            Object.DestroyImmediate(MapGo);
            Object.DestroyImmediate(Constants);
            Object.DestroyImmediate(Template);
            Object.DestroyImmediate(SiegeTemplate);
            Object.DestroyImmediate(BunkerTemplate);
        }
    }
}
