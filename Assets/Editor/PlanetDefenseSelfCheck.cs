using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using FlatSpace.AI;
using Flatspace.Objects.Production;

public static class PlanetDefenseSelfCheck
{
    [MenuItem("FlatSpace/AI/Run Planet Defense Self-Check")]
    public static void Run() => RunChecks();

    public static bool RunChecks()
    {
        var ok = RunKindsAndDefaultsCheck();
        ok &= RunBombardStatsCheck();
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

    private static bool Near(float a, float b) => Mathf.Abs(a - b) < 0.001f;

    private static CatalogItem Line(string name, string subType, string effect, int tier)
    {
        var item = ScriptableObject.CreateInstance<CatalogItem>();
        item.itemName = name; item.name = name;
        item.type = "Ship Improvement"; item.subType = subType;
        item.tier = tier; item.effect = effect; item.researched = true;
        return item;
    }

    public static bool RunBombardStatsCheck()
    {
        var ok = true;
        using (var f = Fixture.Line())
        {
            // Extra research lines on top of the warship Off/Hp/Def fixture: Siegeship Bombard (5 tiers),
            // Siegeship Health (5 tiers) and Bunker Defense (5 tiers).
            var extra = new List<CatalogItem>();
            for (var t = 1; t <= 5; t++)
            {
                extra.Add(Line("SBomb " + t, "Siegeship", "Siegeship Bombard", t));
                extra.Add(Line("SHp " + t, "Siegeship", "Siegeship Health", t));
                extra.Add(Line("BDef " + t, "Bunker", "Bunker Defense", t));
            }
            try
            {
                var all = new List<CatalogItem>(f.Research);
                all.AddRange(extra);
                var stats = new WarshipStats(all);

                ok &= Check(WarshipStats.SubtypeOf(Ship.ShipKind.WarShip) == "Warship"
                            && WarshipStats.SubtypeOf(Ship.ShipKind.SiegeShip) == "Siegeship"
                            && WarshipStats.SubtypeOf(Ship.ShipKind.Bunker) == "Bunker"
                            && WarshipStats.SubtypeOf(Ship.ShipKind.ColonyShip) == "ColonyShip",
                    "SubtypeOf maps each kind to its catalog subtype string");

                // Siegeship carrying 2 bombard tiers: base + 2 x (max - base) / 5 = 20 + 2 x 40 / 5 = 36.
                var twoBombard = new List<string> { "SBomb 1", "SBomb 2" };
                ok &= Check(Near(stats.Bombard(f.SiegeTemplate, twoBombard, "Siegeship"), 36f),
                    "a siegeship with 2 of 5 bombard tiers has base + 2 x (max - base) / 5");
                ok &= Check(Near(stats.Bombard(f.SiegeTemplate, new List<string>(), "Siegeship"), 20f),
                    "a siegeship with no tiers has the base bombard");
                ok &= Check(Near(stats.Bombard(f.SiegeTemplate, null, "Siegeship"), 20f),
                    "a null snapshot is the base bombard");

                // A warship has no "Warship Bombard" line: the template base, whatever it carries.
                ok &= Check(Near(stats.Bombard(f.Template, new List<string> { "SBomb 1", "Off 1" }), 1f),
                    "a warship with no Warship Bombard line returns the template base bombard (1)");
                ok &= Check(stats.TierCount("Warship Bombard") == 0 && stats.TierCount("Siegeship Bombard", "Siegeship") == 5,
                    "TierCount counts a line per subtype");
                ok &= Check(Near(stats.Bombard(null, twoBombard, "Siegeship"), 0f), "a null template has 0 bombard");

                // Warship offense is unchanged by the new lines: 2 Off tiers add 2 x (30 - 10) / 5.
                var warshipOff = new List<string> { "Off 1", "Off 2" };
                ok &= Check(Near(stats.Offense(f.Template, warshipOff), 18f)
                            && Near(stats.Offense(f.Template, warshipOff, "Warship"), 18f),
                    "warship offense with 2 Off tiers is 10 + 2 x 20 / 5 = 18, with or without the subtype");

                // Subtypes do not leak: siegeship tiers count only Siegeship items, a bunker snapshot only Bunker items.
                ok &= Check(Near(stats.Health(f.SiegeTemplate, new List<string> { "SHp 1", "Hp 1" }, "Siegeship"), 120f),
                    "a siegeship's Health counts Siegeship Health items only (100 + 1 x 100 / 5)");
                var bunkerSnapshot = new List<string> { "BDef 1", "BDef 2", "Off 1", "Hp 1", "SHp 1" };
                ok &= Check(Near(stats.Defense(f.BunkerTemplate, bunkerSnapshot, "Bunker"), 8f + 2f * (20f - 8f) / 5f),
                    "a bunker snapshot only counts Bunker items");
                ok &= Check(Near(stats.Health(f.BunkerTemplate, bunkerSnapshot, "Bunker"), 150f)
                            && Near(stats.Offense(f.BunkerTemplate, bunkerSnapshot, "Bunker"), 12f),
                    "other warship/siegeship items in a bunker snapshot add nothing");

                // Ship-based overloads and effective bombard.
                var planet = f.P("A");
                planet.DockShipFromSave(Ship.ShipKind.SiegeShip, 0, twoBombard, 0f);
                planet.DockShipFromSave(Ship.ShipKind.SiegeShip, 0, twoBombard, 50f);
                planet.DockShipFromSave(Ship.ShipKind.WarShip, 0, warshipOff, 0f);
                planet.DockShipFromSave(Ship.ShipKind.SiegeShip, 0, new List<string>(), 100f);
                var healthy = planet.DockedShips[0];
                var hurt = planet.DockedShips[1];
                var warship = planet.DockedShips[2];
                var dead = planet.DockedShips[3];

                ok &= Check(Near(stats.EffectiveBombard(healthy), 36f), "an undamaged siegeship's effective bombard is its bombard");
                // Siege health 100 (no health tiers carried), damage 50: half health, half bombard.
                ok &= Check(Near(stats.EffectiveBombard(hurt), 18f),
                    "a damaged siegeship's effective bombard scales with remaining health (36 x 50 / 100)");
                ok &= Check(Near(stats.EffectiveBombard(dead), 0f), "a ship at 0 health has 0 effective bombard");
                ok &= Check(Near(stats.Offense(healthy), 3f) && Near(stats.Health(healthy), 100f) && Near(stats.Defense(healthy), 3f),
                    "Ship overloads use the siegeship template");
                ok &= Check(Near(stats.CurrentHealth(hurt), 50f) && Near(stats.EffectiveOffense(hurt), 1.5f),
                    "Ship CurrentHealth/EffectiveOffense use the kind's subtype and the ship's damage");
                ok &= Check(Near(stats.Offense(warship), stats.Offense(f.Template, warshipOff))
                            && Near(stats.EffectiveBombard(warship), 1f),
                    "a WarShip through the Ship overloads equals the Warship template path");

                // ResearchedNames filters by subtype.
                var names = WarshipStats.ResearchedNames(all, "Siegeship");
                ok &= Check(names.Count == 10 && names.Contains("SBomb 3") && !names.Contains("Off 1"),
                    "ResearchedNames(subtype) lists only that subtype's researched items");
                ok &= Check(WarshipStats.ResearchedNames(all).Count == 0 && !WarshipStats.ResearchedNames(all).Contains("SBomb 1"),
                    "ResearchedNames defaults to Warship (the fixture's warship items are unresearched)");
            }
            finally
            {
                WarshipSelfCheck.DestroyAll(extra);
            }
        }
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
