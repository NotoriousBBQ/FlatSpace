using System.Collections.Generic;
using System.IO;
using System.Linq;
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
        ok &= RunProductionCostCheck();
        ok &= RunCatalogCheck();
        ok &= RunBunkerCapCheck();
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

    private static CatalogItem ProductionItem(string name, string subType, float cost)
    {
        var item = ScriptableObject.CreateInstance<CatalogItem>();
        item.itemName = name; item.name = name;
        item.type = "Ship"; item.subType = subType; item.cost = cost;
        return item;
    }

    public static bool RunProductionCostCheck()
    {
        var ok = true;
        var warship = ProductionItem("WarShipProduction", "Warship", 100f);
        var siege = ProductionItem("SiegeshipProduction", "Siegeship", 150f);
        var bunker = ProductionItem("BunkerProduction", "Bunker", 150f);
        var research = new List<CatalogItem>();
        try
        {
            // No research: 1.5 x the base warship cost.
            ok &= Check(Near(WarshipCosts.ProductionCost(siege, null, 0, research, 100f, 0.1f, 1.5f, 1.5f), 150f),
                "a siegeship with no research costs 1.5 x the base warship cost");
            ok &= Check(Near(WarshipCosts.ProductionCost(bunker, null, 0, research, 100f, 0.1f, 1.5f, 1.5f), 150f),
                "a bunker with no research costs 1.5 x the base warship cost");
            ok &= Check(Near(WarshipCosts.ProductionCost(siege, null, 0, research, 100f, 0.1f), 150f),
                "the ratio parameters default to 1.5");

            research.Add(Line("S1", "Siegeship", "Siegeship Offense", 1));
            research.Add(Line("S2", "Siegeship", "Siegeship Bombard", 1));
            research.Add(Line("B1", "Bunker", "Bunker Defense", 1));
            research.Add(Line("B2", "Bunker", "Bunker Health", 1));
            research.Add(Line("W1", "Warship", "Warship Offense", 1));
            // 150 x (1 + 0.1 x 2) = 180 for each; the other subtypes' items add nothing.
            ok &= Check(Near(WarshipCosts.ProductionCost(siege, null, 0, research, 100f, 0.1f, 1.5f, 1.5f), 180f),
                "a siegeship with 2 siegeship improvements at factor 0.1 costs 180");
            ok &= Check(Near(WarshipCosts.ProductionCost(bunker, null, 0, research, 100f, 0.1f, 1.5f, 1.5f), 180f),
                "a bunker with 2 bunker improvements at factor 0.1 costs 180");
            ok &= Check(Near(WarshipCosts.ProductionCost(siege, null, 0, research, 100f, 0.1f, 2f, 1.5f), 240f),
                "the siegeship ratio is applied (100 x 2 x 1.2)");
            ok &= Check(Near(WarshipCosts.ProductionCost(bunker, null, 0, research, 100f, 0.1f, 2f, 3f), 360f),
                "the bunker ratio is applied (100 x 3 x 1.2)");
            ok &= Check(Near(WarshipCosts.ProductionCost(warship, null, 0, research, 100f, 0.1f, 1.5f, 1.5f), 110f),
                "a warship is unchanged: 100 x (1 + 0.1 x 1 warship improvement)");
        }
        finally
        {
            WarshipSelfCheck.DestroyAll(research);
            Object.DestroyImmediate(warship);
            Object.DestroyImmediate(siege);
            Object.DestroyImmediate(bunker);
        }
        return ok;
    }

    private static Catalog.CatalogSaveData ReadCatalog(string folder, string file)
    {
        var path = Application.dataPath + "/Flatspace/Catalogs/" + folder + "/" + file + ".json";
        if (!File.Exists(path))
        {
            Debug.LogError($"[PlanetDefenseSelfCheck] FAIL: catalog file not found: {path}");
            return null;
        }
        return JsonUtility.FromJson<Catalog.CatalogSaveData>(File.ReadAllText(path));
    }

    // Reads the shipped JSON catalogs from disk (no Gameboard) and checks the planet-defense items.
    public static bool RunCatalogCheck()
    {
        var research = ReadCatalog("Research", "Research Catalog");
        var production = ReadCatalog("Production", "Production Catalog");
        if (!Check(research != null && production != null, "both catalog files load")) return false;

        var ok = true;
        var byName = new Dictionary<string, Catalog.CatalogSaveData.ItemSaveData>();
        foreach (var item in research.items)
        {
            ok &= Check(!byName.ContainsKey(item.itemName), $"research item name is unique: {item.itemName}");
            byName[item.itemName] = item;
        }

        // Every requiredTech in the research catalog is "" or another research item.
        foreach (var item in research.items)
        {
            ok &= Check(string.IsNullOrEmpty(item.requiredTech) || byName.ContainsKey(item.requiredTech),
                $"{item.itemName}: requiredTech '{item.requiredTech}' exists in the research catalog");
        }

        // (name prefix, subtype, effect, first tier's requirement)
        var lines = new[]
        {
            ("Warship Bombard Upgrade", "Warship", "Warship Bombard", "Warship 1"),
            ("Siegeship Offense", "Siegeship", "Siegeship Offense", "Siegeship 1"),
            ("Siegeship Armor", "Siegeship", "Siegeship Health", "Siegeship 1"),
            ("Siegeship Shields", "Siegeship", "Siegeship Defense", "Siegeship 1"),
            ("Siegeship Bombard", "Siegeship", "Siegeship Bombard", "Siegeship 1"),
            ("Bunker Weapons", "Bunker", "Bunker Offense", "Bunker 1"),
            ("Bunker Armor", "Bunker", "Bunker Health", "Bunker 1"),
            ("Bunker Shields", "Bunker", "Bunker Defense", "Bunker 1"),
        };
        foreach (var (prefix, subType, effect, first) in lines)
        {
            for (var t = 1; t <= 5; t++)
            {
                var name = prefix + " " + t;
                if (!Check(byName.TryGetValue(name, out var item), $"research item exists: {name}")) { ok = false; continue; }
                var requires = t == 1 ? first : prefix + " " + (t - 1);
                ok &= Check(item.type == "Ship Improvement" && item.subType == subType && item.effect == effect,
                    $"{name}: type Ship Improvement, subType {subType}, effect {effect}");
                ok &= Check(item.tier == t && Near(item.cost, 3000f * t), $"{name}: tier {t}, cost {3000 * t}");
                ok &= Check(item.requiredTech == requires, $"{name}: requires {requires}");
            }
        }

        foreach (var unlock in new[] { "Siegeship 1", "Bunker 1" })
        {
            if (!Check(byName.TryGetValue(unlock, out var item), $"unlock item exists: {unlock}")) { ok = false; continue; }
            var subType = unlock.Split(' ')[0];
            ok &= Check(item.type == "Ship Type" && item.requiredTech == "Warship 1" && item.subType == subType,
                $"{unlock}: type Ship Type, subType {subType}, requires Warship 1");
        }

        var productionByName = production.items.ToDictionary(i => i.itemName);
        foreach (var (name, subType, requires) in new[]
                 { ("SiegeshipProduction", "Siegeship", "Siegeship 1"), ("BunkerProduction", "Bunker", "Bunker 1") })
        {
            if (!Check(productionByName.TryGetValue(name, out var item), $"production item exists: {name}")) { ok = false; continue; }
            ok &= Check(item.type == "Ship" && item.subType == subType && item.effect == subType
                        && item.requiredTech == requires && Near(item.cost, 150f),
                $"{name}: type Ship, subType/effect {subType}, requires {requires}, cost 150");
            ok &= Check(byName.ContainsKey(item.requiredTech), $"{name}: requiredTech exists in the research catalog");
        }
        return ok;
    }

    // Bunker cap (MaxPopulation), survival over the cap, death cleanup and the kind-to-subtype mapping that
    // BuildResearchSnapshot uses. Planet.DockNewShip / StageCompletedProductionItem need Gameboard.Instance, so docking goes
    // through DockShipFromSave and the death path is tested through ClearBunkers (the call ConsumeFood makes when the last
    // inhabitant dies; reaching it through UpdatePlanet would need full resource data and, with mixed owners, Gameboard).
    public static bool RunBunkerCapCheck()
    {
        var ok = true;
        ok &= Check(WarshipStats.SubtypeOf(Ship.ShipKind.ColonyShip) == "ColonyShip"
                    && WarshipStats.SubtypeOf(Ship.ShipKind.WarShip) == "Warship"
                    && WarshipStats.SubtypeOf(Ship.ShipKind.SiegeShip) == "Siegeship"
                    && WarshipStats.SubtypeOf(Ship.ShipKind.Bunker) == "Bunker",
            "SubtypeOf maps every kind to its catalog subtype (the research snapshot filter)");

        using (var f = Fixture.Line())
        {
            var a = f.P("A");
            ok &= Check(!a.CanBuildBunker(), "a planet with no owner cannot build a bunker");

            f.Colonize("A", 0);
            var cap = a.MaxPopulation;
            ok &= Check(cap > 1, "precondition: the fixture planet has a max population above 1");
            ok &= Check(a.CanBuildBunker() && a.BunkerCount(0) == 0, "an owned planet with no bunkers can build one");

            f.Bunkers("A", 0, cap - 1);
            ok &= Check(a.BunkerCount(0) == cap - 1 && a.CanBuildBunker(), "one below the cap can still build");

            f.Bunkers("A", 0, 1);
            ok &= Check(a.BunkerCount(0) == cap && !a.CanBuildBunker(), "MaxPopulation bunkers is the cap: cannot build");

            f.Bunkers("A", 1, 1);
            ok &= Check(a.BunkerCount(0) == cap && a.BunkerCount(1) == 1, "BunkerCount counts per owner");

            // Losing inhabitants while some remain: bunkers stay (a planet can only hold more bunkers than MaxPopulation
            // after an owner change; here the planet stays at the cap and still cannot add one).
            a.ChangePopulation(1, 0);
            a.ChangePopulation(-1, 0);
            ok &= Check(a.Population.Count == 1 && a.Owner == 0 && a.BunkerCount(0) == cap && !a.CanBuildBunker(),
                "a population loss that leaves inhabitants keeps the bunkers");

            // The last inhabitant dying through the public ChangePopulation path (GameAI population orders) clears every bunker.
            a.ChangePopulation(-1, 0);
            ok &= Check(a.Population.Count == 0 && a.Owner == Planet.NoOwner && a.BunkerCount(0) == 0
                        && a.BunkerCount(1) == 0 && a.DockedShips.Count == 0,
                "ChangePopulation removing the last inhabitant destroys the planet's bunkers");

            f.Colonize("A", 0);
            f.Bunkers("A", 0, 2);
            f.Ships("A", 0, 1);
            a.ClearBunkers();
            ok &= Check(a.BunkerCount(0) == 0 && a.DockedShips.Count == 1 && a.HasDockedShip(Ship.ShipKind.WarShip),
                "ClearBunkers destroys the bunkers and leaves the warship");

            // Other docked ships are untouched by the cleanup.
            f.Ships("B", 0, 2);
            f.Siege("B", 0, 1);
            f.Bunkers("B", 0, 2);
            var b = f.P("B");
            b.ClearBunkers();
            ok &= Check(b.DockedShips.Count == 3 && !b.HasDockedShip(Ship.ShipKind.Bunker)
                        && b.DockedShips.Count(s => s.Kind == Ship.ShipKind.WarShip) == 2
                        && b.DockedShips.Count(s => s.Kind == Ship.ShipKind.SiegeShip) == 1,
                "ClearBunkers leaves warships and siegeships docked");
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
