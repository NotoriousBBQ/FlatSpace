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
        ok &= RunBunkersAreNotShipsCheck();
        ok &= RunSiegeshipCombatCheck();
        ok &= RunBombardmentCheck();
        ok &= RunConversionGateCheck();
        ok &= RunProjectionBunkerFireCheck();
        ok &= RunSaveRoundTripCheck();
        ok &= RunNewSubtypeWeightsCheck();
        ok &= RunBunkerThreatWeightCheck();
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

    // Task 11: Siegeship and Bunker have explicit entries in every strategy's industry and research table (a missing key falls
    // back silently to 1, so the explicit values are asserted and the fallback value is never what is expected), and a choice's
    // industry weight is the strategy weight times the situational multiplier (Bunker's 0 at the cap is the Task 4 gate).
    public static bool RunNewSubtypeWeightsCheck()
    {
        var ok = true;
        var strategies = new[] { PlayerAI.AIStrategy.AIStrategyExpand, PlayerAI.AIStrategy.AIStrategyConsolidate, PlayerAI.AIStrategy.AIStrategyAmass };
        // order: Expand, Consolidate, Amass
        var table = new (string subType, float[] industry, float[] research)[]
        {
            ("Siegeship", new[] { 0.5f, 2.0f, 3.0f }, new[] { 0.75f, 2.0f, 3.0f }),
            ("Bunker",    new[] { 0.5f, 1.5f, 2.0f }, new[] { 0.5f, 1.5f, 2.0f }),
        };
        var item = ScriptableObject.CreateInstance<CatalogItem>();
        var playerGo = new GameObject("WeightsCheckPlayer");
        using var f = Fixture.Line();
        try
        {
            foreach (var (subType, industry, research) in table)
            {
                item.subType = subType;
                for (var i = 0; i < strategies.Length; i++)
                {
                    ok &= Check(Near(PlayerAI.GetIndustryStrategyWeight(item, strategies[i]), industry[i]),
                        $"{strategies[i]} industry weight for {subType} is {industry[i]}");
                    ok &= Check(Near(PlayerAI.GetResearchWeight(item, strategies[i]), research[i]),
                        $"{strategies[i]} research weight for {subType} is {research[i]}");
                }
            }

            f.Colonize("A", 0);
            var a = f.P("A");
            var player = playerGo.AddComponent<Player>();
            var ai = playerGo.AddComponent<PlayerAI>();
            ai.Player = player;
            ai.AIMap = f.Map;
            player.playerID = 0;

            foreach (var (subType, industry, _) in table)
            {
                item.subType = subType;
                for (var i = 0; i < strategies.Length; i++)
                {
                    ai.Strategy = strategies[i];
                    var situational = ai.GetIndustrySituationalWeightMultiplier(item, "A");
                    ok &= Check(Near(PlayerAI.GetIndustryStrategyWeight(item, strategies[i]) * situational, industry[i] * situational),
                        $"{subType} choice weight is strategy weight x situational multiplier at {strategies[i]}");
                }
            }

            // The Task 4 gate stays: at the bunker cap the situational multiplier is 0, so the choice weight is 0.
            item.subType = "Bunker";
            ai.Strategy = PlayerAI.AIStrategy.AIStrategyAmass;
            f.Bunkers("A", 0, a.MaxPopulation);
            ok &= Check(!a.CanBuildBunker()
                        && Near(PlayerAI.GetIndustryStrategyWeight(item, PlayerAI.AIStrategy.AIStrategyAmass)
                                * ai.GetIndustrySituationalWeightMultiplier(item, "A"), 0f),
                "a Bunker at the cap keeps weight 0 whatever the strategy weight");
        }
        finally
        {
            Object.DestroyImmediate(item);
            Object.DestroyImmediate(playerGo);
        }
        return ok;
    }

    // A PlayerAI for player 0 on a fixture's map (a research catalog so RefreshBlockadeView works); destroyed on dispose.
    private sealed class ThreatAi : System.IDisposable
    {
        private readonly GameObject _go = new GameObject("BunkerThreatCheckPlayer");
        public readonly PlayerAI Ai;
        public readonly CatalogItem Item = ScriptableObject.CreateInstance<CatalogItem>();

        public ThreatAi(Fixture f, PlayerAI.AIStrategy strategy)
        {
            var player = _go.AddComponent<Player>();
            Ai = _go.AddComponent<PlayerAI>();
            Ai.Player = player;
            Ai.AIMap = f.Map;
            player.playerID = 0;
            Ai.ResearchCatalog = _go.AddComponent<Catalog>();
            Ai.ResearchCatalog.catalogItems = f.Research;
            Ai.Strategy = strategy;
            Item.subType = "Bunker";
        }

        public float Weight(string planet) => Ai.GetIndustrySituationalWeightMultiplier(Item, planet);

        public void Dispose()
        {
            Object.DestroyImmediate(Item);
            Object.DestroyImmediate(_go);
        }
    }

    // Task 12: the threat-weighted Bunker situational multiplier (defaults: base 1, outer/chokepoint factor 2, blockaded boost 3,
    // rival-adjacent boost 2). Line map A-B-C-D, player 0 colonizes A, B, C (D empty): C is outer, A and B are interior; the
    // chokepoint percentile is set above 1 so no planet is a chokepoint.
    public static bool RunBunkerThreatWeightCheck()
    {
        var ok = true;
        var consolidate = PlayerAI.AIStrategy.AIStrategyConsolidate;

        using (var f = Fixture.Line())
        {
            ok &= Check(Near(f.Constants.bunkerBaseWeight, 1f) && Near(f.Constants.bunkerOuterFactor, 2f)
                        && Near(f.Constants.bunkerBlockadedBoost, 3f) && Near(f.Constants.bunkerRivalAdjacentBoost, 2f),
                "the bunker tunables default to 1, 2, 3 and 2");
        }

        // Rival warships docked on B and C, at war: A (beside B) and B (on it) are interior, C is outer.
        using (var f = Fixture.Line())
        {
            f.Constants.chokepointPercentile = 2f;
            f.Colonize("A", 0); f.Colonize("B", 0); f.Colonize("C", 0);
            f.Ships("B", 1, 1); f.Ships("C", 1, 1);
            f.War(0, 1);

            using (var t = new ThreatAi(f, consolidate))
            {
                ok &= Check(Near(t.Weight("A"), 2f), "interior planet beside a rival ship: 1 x 1 x 2 = 2");
                ok &= Check(Near(t.Weight("B"), 2f), "interior planet holding a rival ship: 1 x 1 x 2 = 2");
                ok &= Check(Near(t.Weight("C"), 4f), "outer planet holding a rival ship: 1 x 2 x 2 = 4");
                ok &= Check(t.Weight("C") > t.Weight("B"), "an outer planet outweighs an interior one under the same threat");
                ok &= Check(t.Ai.BunkerThreatContextBuilds == 1, "the per-turn threat context is built once for three planets");
            }
            using (var t = new ThreatAi(f, PlayerAI.AIStrategy.AIStrategyAmass))
                ok &= Check(Near(t.Weight("C"), 4f), "Amass is Consolidate-like: the same weight");
            foreach (var strategy in new[] { PlayerAI.AIStrategy.AIStrategyExpand, PlayerAI.AIStrategy.AIStrategyNone })
            {
                using var t = new ThreatAi(f, strategy);
                ok &= Check(t.Weight("A") == 0f && t.Weight("B") == 0f && t.Weight("C") == 0f,
                    $"{strategy} never builds bunkers: weight 0 even with rivals beside");
            }

            // At the cap: C is full (cap = MaxPopulation), the others are not.
            var cap = f.P("C").MaxPopulation;
            f.Bunkers("C", 0, cap);
            using (var t = new ThreatAi(f, consolidate))
            {
                ok &= Check(!f.P("C").CanBuildBunker() && t.Weight("C") == 0f, "a planet at the bunker cap has weight 0");
                ok &= Check(Near(t.Weight("B"), 2f), "a planet below the cap keeps its weight");
            }
        }

        // No war: nothing is nearby. At war but no rival ship or planet known: still 0, even on the outer planet.
        using (var f = Fixture.Line())
        {
            f.Constants.chokepointPercentile = 2f;
            f.Colonize("A", 0); f.Colonize("B", 0); f.Colonize("C", 0);
            f.Ships("B", 1, 1); f.Ships("C", 1, 1);   // rival ships, but no stance toward them
            using (var t = new ThreatAi(f, consolidate))
                ok &= Check(t.Weight("A") == 0f && t.Weight("B") == 0f && t.Weight("C") == 0f,
                    "rival ships we are not at war with are no threat: weight 0");
        }
        using (var f = Fixture.Line())
        {
            f.Constants.chokepointPercentile = 2f;
            f.Colonize("A", 0); f.Colonize("B", 0); f.Colonize("C", 0);
            f.War(0, 1);
            f.Map.Knowledge.Update(f.Map, 2);
            using (var t = new ThreatAi(f, consolidate))
                ok &= Check(t.Weight("A") == 0f && t.Weight("B") == 0f && t.Weight("C") == 0f,
                    "at war but no rival ship or planet known: weight 0 everywhere");
        }

        // A known rival planet beside the outer planet C: only outer (or chokepoint) planets get a weight, with no adjacent boost.
        using (var f = Fixture.Line())
        {
            f.Constants.chokepointPercentile = 2f;
            f.Colonize("A", 0); f.Colonize("B", 0); f.Colonize("C", 0); f.Colonize("D", 1);
            f.War(0, 1);
            f.Map.Knowledge.Update(f.Map, 2);
            ok &= Check(f.Map.Knowledge.IsKnown(0, "D"), "precondition: player 0 knows the rival's planet D");
            using (var t = new ThreatAi(f, consolidate))
            {
                ok &= Check(Near(t.Weight("C"), 2f), "outer planet with a known rival planet and no ship beside: 1 x 2 = 2");
                ok &= Check(t.Weight("B") == 0f && t.Weight("A") == 0f, "interior planets with no rival ship nearby: 0");
            }
        }

        // Blockaded: two rival warships on C against none of mine. Without the view 1 x 2 x 2 = 4; blockaded the boost x3 = 12.
        using (var f = Fixture.Line())
        {
            f.Constants.chokepointPercentile = 2f;
            f.Colonize("B", 0); f.Colonize("C", 0);
            f.Ships("C", 1, 2);
            f.War(0, 1);
            using (var t = new ThreatAi(f, consolidate))
            {
                ok &= Check(Near(t.Weight("C"), 4f), "precondition: before the blockade view exists the weight is 4");
            }
            using (var t = new ThreatAi(f, consolidate))
            {
                t.Ai.RefreshBlockadeView(0);
                ok &= Check(t.Ai.CurrentBlockadeView.IsBlockaded("C"), "precondition: C is blockaded against player 0");
                ok &= Check(Near(t.Weight("C"), 12f), "a blockaded outer planet with a rival ship: 1 x 2 x 3 x 2 = 12");
                ok &= Check(t.Weight("C") > 4f, "the blockade boosts the bunker weight");
            }
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

    // Task 5: a bunker is a planetary defense, not a ship. It gives no vision, no contact and no fleet icon, and it changes none
    // of the warship-only readers (garrison count, fleet strength, blockade offense). A siegeship counts as presence like a
    // warship but, being warship-only elsewhere, adds nothing to those three readers either.
    public static bool RunBunkersAreNotShipsCheck()
    {
        var ok = true;
        ok &= Check(Ship.CountsAsShip(Ship.ShipKind.ColonyShip) && Ship.CountsAsShip(Ship.ShipKind.WarShip)
                    && Ship.CountsAsShip(Ship.ShipKind.SiegeShip) && !Ship.CountsAsShip(Ship.ShipKind.Bunker),
            "CountsAsShip is false only for Bunker");

        // Contact and vision: player 0 holds A (neighbour B is known); player 1's pieces sit on B and D.
        using (var f = Fixture.Line())
        {
            f.Colonize("A", 0);
            f.Bunkers("B", 1, 2);
            f.Map.Knowledge.Update(f.Map, 2);
            ok &= Check(f.Map.Knowledge.IsKnown(0, "B"), "precondition: player 0 knows the neighbour B");
            ok &= Check(!f.Map.Knowledge.HasContact(f.Map, 0), "a rival's bunkers alone are not contact");
            ok &= Check(f.Map.Knowledge.ContactPlayers(f.Map, 0).Count == 0, "a rival's bunkers alone name no contact player");

            f.Siege("B", 1, 1);
            ok &= Check(f.Map.Knowledge.HasContact(f.Map, 0) && f.Map.Knowledge.ContactPlayers(f.Map, 0).Contains(1),
                "a rival's siegeship is contact, like a warship");
        }
        using (var f = Fixture.Line())
        {
            f.Colonize("A", 0);
            f.Bunkers("B", 1, 1);
            f.Ships("B", 1, 1);
            f.Map.Knowledge.Update(f.Map, 2);
            ok &= Check(f.Map.Knowledge.HasContact(f.Map, 0), "a rival's warship beside its bunkers is contact");
        }
        using (var f = Fixture.Line())
        {
            f.Bunkers("D", 1, 2);
            ok &= Check(f.Map.GetVisionSourcePlanets(1).Count == 0, "bunkers on an unpopulated planet are not a vision source");
            f.Siege("D", 1, 1);
            var sources = f.Map.GetVisionSourcePlanets(1);
            ok &= Check(sources.Count == 1 && sources[0].Planet.PlanetName == "D" && sources[0].HasOwnedShip && !sources[0].HasPopulation,
                "a siegeship on an unpopulated planet is a vision source, like a warship");
        }
        using (var f = Fixture.Line())
        {
            f.Colonize("C", 1);
            f.Bunkers("C", 1, 1);
            var sources = f.Map.GetVisionSourcePlanets(1);
            ok &= Check(sources.Count == 1 && sources[0].HasPopulation && !sources[0].HasOwnedShip,
                "a populated planet's bunkers add presence but no owned ship");
        }

        // Fleet summary: bunkers are ignored; a siegeship-only fleet takes the siegeship icon.
        using (var f = Fixture.Line())
        {
            var d = f.P("D");
            f.Bunkers("D", 1, 3);
            ok &= Check(FleetSummary.ForPlanet(d).Count == 0, "FleetSummary ignores bunkers (no group for a bunker-only owner)");
            f.Siege("D", 1, 1);
            var groups = FleetSummary.ForPlanet(d);
            ok &= Check(groups.Count == 1 && groups[0].Count == 1 && groups[0].IconKind == Ship.ShipKind.SiegeShip,
                "a siegeship fleet counts the siegeship only and shows the siegeship icon");
            f.Ships("D", 1, 1);
            groups = FleetSummary.ForPlanet(d);
            ok &= Check(groups.Count == 1 && groups[0].Count == 2 && groups[0].IconKind == Ship.ShipKind.WarShip,
                "a warship in the fleet wins the icon; bunkers still do not count");
        }

        // Warship-only readers are unchanged by bunkers and siegeships on the same planet.
        using (var f = Fixture.Line())
        {
            f.Ships("B", 0, 2);
            f.Ships("B", 1, 1);
            var b = f.P("B");
            var planner = new ShipTransportPlanner(f.Map, 0, PlayerAI.AIStrategy.AIStrategyConsolidate);
            var blockade = new BlockadeSystem(f.Map, f.Research);
            var garrison = planner.CountWarships(b);
            var strength = FleetStrength.Mine(f.Map, 0, f.Stats);
            var rivalStrength = FleetStrength.VisibleOf(f.Map, 0, 1, f.Stats);
            var offense = blockade.DockedOffense(b, 0);
            var value = blockade.Value(b, 0, out var blocker);
            ok &= Check(garrison == 2 && strength > 0f && offense > 0f, "precondition: two warships give a garrison, strength and offense");

            f.Bunkers("B", 0, 3);
            f.Bunkers("B", 1, 3);
            f.Siege("B", 0, 2);
            f.Siege("B", 1, 2);
            ok &= Check(planner.CountWarships(b) == garrison, "garrison CountWarships ignores bunkers and siegeships");
            ok &= Check(Near(FleetStrength.Mine(f.Map, 0, f.Stats), strength), "FleetStrength.Mine ignores bunkers and siegeships");
            ok &= Check(Near(FleetStrength.VisibleOf(f.Map, 0, 1, f.Stats), rivalStrength), "FleetStrength.VisibleOf ignores bunkers and siegeships");
            ok &= Check(Near(blockade.DockedOffense(b, 0), offense), "BlockadeSystem.DockedOffense ignores bunkers and siegeships");
            ok &= Check(Near(blockade.Value(b, 0, out var blocker2), value) && blocker2 == blocker,
                "the blockade value and its blocker are unchanged by bunkers and siegeships");
        }

        // Fleet rows: siegeship and bunker rows show health, offense, defense and bombard.
        using (var f = Fixture.Line())
        {
            f.Siege("A", 0, 1);
            f.Bunkers("A", 0, 1);
            var siege = f.P("A").DockedShips[0];
            var bunker = f.P("A").DockedShips[1];
            ok &= Check(FleetUIController.FormatShipRow(siege, f.Research) == "SiegeShip (Spd 120, HP 100, Off 3, Def 3, Bomb 20)",
                "a siegeship row shows speed, health, offense, defense and bombard");
            ok &= Check(FleetUIController.FormatShipRow(bunker, f.Research) == "Bunker (Spd 0, HP 150, Off 12, Def 8, Bomb 0)",
                "a bunker row shows health, offense, defense and bombard");
            siege.Damage = 50f;
            ok &= Check(FleetUIController.FormatShipRow(siege, f.Research) == "SiegeShip (Spd 120, HP 50/100, Off 1.5/3, Def 3, Bomb 10/20)",
                "a damaged siegeship shows current/maximum health, effective/base offense and bombard");
        }
        return ok;
    }

    private static List<CombatReport> Resolve(Fixture f, List<Planet.UpdateResult> results)
        => CombatSystem.Resolve(f.Map, f.Stats, f.Constants, 1, results);

    // Task 6: siegeships fight as units of class 1 (hit after warships), bunkers never do, siegeships repair like warships and
    // guard or raid colony ships like warships. Siegeship template: Offense 3, Health 100, Defense 3; warship: 10 / 100 / 5; K 20.
    public static bool RunSiegeshipCombatCheck()
    {
        var ok = true;
        var warLost = Planet.UpdateResult.UpdateResultType.UpdateResultTypeWarshipsLost;
        var colonyLost = Planet.UpdateResult.UpdateResultType.UpdateResultTypeColonyShipsLost;

        using (var f = Fixture.Line())   // the warship dies first although the siegeship has the lower health fraction
        {
            f.Siege("A", 1, 1, 50f);      // dock index 0, health fraction 0.5
            f.Ships("A", 1, 1);           // dock index 1, health fraction 1
            f.Ships("A", 0, 13);          // pool 130: the warship needs 100 / 0.8 = 125
            f.War(0, 1);
            Resolve(f, new List<Planet.UpdateResult>());
            var rival = f.P("A").DockedShips.Where(s => s.Owner == 1).ToList();
            ok &= Check(rival.Count == 1 && rival[0].Kind == Ship.ShipKind.SiegeShip, "the warship is destroyed first, the damaged siegeship survives");
            ok &= Check(rival.Count == 1 && Near(rival[0].Damage, 50f + 5f * 20f / 23f),
                "the 5 left over lands on the siegeship at 20 / (20 + 3): damage 50 + 4.348");
        }

        using (var f = Fixture.Line())   // a side of siegeships alone still takes damage, and its offense joins its pool
        {
            f.Siege("A", 1, 2, 0f);
            f.Ships("A", 0, 1);
            f.War(0, 1);
            var results = new List<Planet.UpdateResult>();
            Resolve(f, results);
            var siege = f.P("A").DockedShips.Where(s => s.Owner == 1).ToList();
            ok &= Check(Near(siege[0].Damage, 10f * 20f / 23f) && Near(siege[1].Damage, 0f),
                "a warship's pool of 10 lands on the first siegeship: 10 x 20 / 23");
            ok &= Check(Near(f.P("A").DockedShips.First(s => s.Owner == 0).Damage, 6f * 0.8f),
                "two siegeships' pool of 3 + 3 lands on the warship at 0.8");
            ok &= Check(results.Count(r => r.Result == warLost) == 2, "one loss result per victim, siegeships included");
        }

        using (var f = Fixture.Line())   // bunkers are never combat units
        {
            f.Ships("A", 0, 1);
            f.Bunkers("A", 1, 3);
            f.War(0, 1);
            var results = new List<Planet.UpdateResult>();
            var reports = Resolve(f, results);
            ok &= Check(reports.Count == 0 && results.Count == 0 && f.P("A").DockedShips.All(s => Near(s.Damage, 0f)),
                "a warship against bunkers alone: no fight, no damage to either side");
        }

        using (var f = Fixture.Line())   // siegeship repair
        {
            f.Colonize("A", 0);
            f.Siege("A", 0, 1, 50f);
            Resolve(f, new List<Planet.UpdateResult>());
            ok &= Check(Near(f.P("A").DockedShips[0].Damage, 40f), "a damaged siegeship at its owner's populated planet heals 10% of its Health stat");
        }
        using (var f = Fixture.Line())
        {
            f.Colonize("A", 0);
            f.Siege("A", 0, 1, 50f);
            f.Siege("A", 1, 1);
            f.War(0, 1);
            Resolve(f, new List<Planet.UpdateResult>());
            ok &= Check(Near(f.P("A").DockedShips[0].Damage, 50f + 3f * 20f / 23f),
                "an at-war siegeship docked there takes the hit and blocks repair");
        }

        using (var f = Fixture.Line())   // colony ships: a docked siegeship protects them, a raiding one destroys them
        {
            f.P("A").DockShipFromSave(Ship.ShipKind.ColonyShip, 0, new List<string>());
            f.Siege("A", 0, 1);
            f.Ships("A", 1, 1);
            f.War(0, 1);
            Resolve(f, new List<Planet.UpdateResult>());
            ok &= Check(f.P("A").DockedShips.Any(s => s.Kind == Ship.ShipKind.ColonyShip), "a docked siegeship of the owner protects its colony ship");
        }
        using (var f = Fixture.Line())
        {
            f.P("A").DockShipFromSave(Ship.ShipKind.ColonyShip, 0, new List<string>());
            f.Siege("A", 1, 1);
            f.War(0, 1);
            var results = new List<Planet.UpdateResult>();
            Resolve(f, results);
            var lost = results.Where(r => r.Result == colonyLost).ToList();
            ok &= Check(f.P("A").DockedShips.All(s => s.Kind != Ship.ShipKind.ColonyShip)
                        && lost.Count == 1 && ((ColonyLoss)lost[0].Data).ByPlayer == 1,
                "an at-war siegeship destroys a colony ship with no guard");
        }
        using (var f = Fixture.Line())
        {
            f.P("A").DockShipFromSave(Ship.ShipKind.ColonyShip, 0, new List<string>());
            f.Bunkers("A", 1, 2);
            f.War(0, 1);
            Resolve(f, new List<Planet.UpdateResult>());
            ok &= Check(f.P("A").DockedShips.Any(s => s.Kind == Ship.ShipKind.ColonyShip), "an at-war bunker is not a raider: the colony ship lives");
        }
        return ok;
    }

    private static List<BombardReport> Bombard(Fixture f, List<Planet.UpdateResult> results)
        => BombardmentSystem.Resolve(f.Map, f.Stats, f.Constants, results);

    private static List<Ship> Of(Planet planet, Ship.ShipKind kind, int owner)
        => planet.DockedShips.Where(s => s.Kind == kind && s.Owner == owner).ToList();

    // Task 7: the bombardment step. Templates (no research lines for them in the fixture, so base values): warship Offense 10,
    // Health 100, Defense 5, Bombard 1; siegeship 3 / 100 / 3, Bombard 20; bunker Offense 12, Health 150, Defense 8; K 20, repair 0.1.
    public static bool RunBombardmentCheck()
    {
        var ok = true;
        var warLost = Planet.UpdateResult.UpdateResultType.UpdateResultTypeWarshipsLost;

        // (1) Trigger. A defender warship, or only a defender siegeship, protects the bunkers; no war, no bombardment.
        foreach (var guard in new[] { Ship.ShipKind.WarShip, Ship.ShipKind.SiegeShip })
        {
            using (var f = Fixture.Line())
            {
                f.Ships("A", 0, 2);
                f.Bunkers("A", 1, 2);
                if (guard == Ship.ShipKind.WarShip) f.Ships("A", 1, 1); else f.Siege("A", 1, 1);
                f.War(0, 1);
                var results = new List<Planet.UpdateResult>();
                var reports = Bombard(f, results);
                ok &= Check(!BombardmentSystem.Triggers(f.P("A"), 1, f.Map) && reports.Count == 0 && results.Count == 0
                            && f.P("A").DockedShips.All(s => Near(s.Damage, 0f)),
                    $"a defender {guard} docked beside its bunkers: no bombardment, nothing damaged");
            }
        }
        using (var f = Fixture.Line())
        {
            f.Ships("A", 0, 2);
            f.Bunkers("A", 1, 2);
            var results = new List<Planet.UpdateResult>();
            var reports = Bombard(f, results);
            ok &= Check(!BombardmentSystem.Triggers(f.P("A"), 1, f.Map) && reports.Count == 0 && results.Count == 0
                        && f.P("A").DockedShips.All(s => Near(s.Damage, 0f)),
                "ships of a player not at war with the bunker owner do not bombard");
        }
        using (var f = Fixture.Line())   // the last defender ship dies in ship combat: bombardment the same turn
        {
            f.Ships("A", 1, 1);
            f.Bunkers("A", 1, 2);
            f.Ships("A", 0, 13);          // pool 130 kills the defender (needs 100 / 0.8 = 125); it hits back 10 x 0.8 = 8
            f.War(0, 1);
            var a = f.P("A");
            ok &= Check(!BombardmentSystem.Triggers(a, 1, f.Map), "precondition: the defender warship protects the bunkers before combat");
            var results = new List<Planet.UpdateResult>();
            Resolve(f, results);
            ok &= Check(Of(a, Ship.ShipKind.WarShip, 1).Count == 0 && BombardmentSystem.Triggers(a, 1, f.Map),
                "the defender's last warship died in ship combat: the bunkers are exposed");
            var reports = Bombard(f, results);
            var bunkers = Of(a, Ship.ShipKind.Bunker, 1);
            var hurt = Of(a, Ship.ShipKind.WarShip, 0).OrderByDescending(s => s.Damage).First();
            // Bombard: 12 healthy x 1 + the hurt ship's 1 x 92 / 100 = 12.92 at 20 / (20 + 8). Bunker fire: 2 x 12 = 24 on the most
            // damaged warship at 20 / (20 + 5): 8 + 19.2.
            ok &= Check(reports.Count == 1 && reports[0].Attacker == 0 && reports[0].Victim == 1 && reports[0].BunkersLeft == 2,
                "bombardment runs on the same turn the last defender ship died (one report, 0 against 1, both bunkers left)");
            ok &= Check(Near(bunkers[0].Damage, 12.92f * 20f / 28f) && Near(bunkers[1].Damage, 0f),
                "the fleet's bombard 12.92 lands on the first bunker at 20 / 28");
            ok &= Check(Near(hurt.Damage, 8f + 24f * 0.8f), "the bunkers' 24 lands on the most damaged warship at 0.8: 8 + 19.2");
        }

        // (2) Simultaneity: a bunker destroyed this step still fires, a siegeship destroyed this step still bombards.
        using (var f = Fixture.Line())
        {
            f.Bunkers("A", 1, 1);
            f.Siege("A", 0, 11);          // bombard 220 >= 150 / (20 / 28) = 210
            f.War(0, 1);
            var reports = Bombard(f, new List<Planet.UpdateResult>());
            var siege = Of(f.P("A"), Ship.ShipKind.SiegeShip, 0);
            ok &= Check(f.P("A").BunkerCount(1) == 0, "eleven siegeships destroy the bunker");
            ok &= Check(siege.Count == 11 && Near(siege[0].Damage, 12f * 20f / 23f) && Near(siege[1].Damage, 0f),
                "the destroyed bunker still fired: 12 x 20 / 23 on the first siegeship");
            ok &= Check(reports.Count == 1 && Near(reports[0].BombardDealt, 150f) && Near(reports[0].BunkersDestroyed, 1f)
                        && reports[0].BunkersLeft == 0 && Near(reports[0].ShipDamage, 12f * 20f / 23f),
                "the report: 150 dealt, 1 bunker destroyed, 0 left, 10.43 ship damage");
        }
        using (var f = Fixture.Line())
        {
            f.Bunkers("A", 1, 1);
            f.Siege("A", 0, 1, 99f);      // health 1: effective bombard 20 x 1 / 100 = 0.2; the bunker's 12 kills it
            f.War(0, 1);
            var results = new List<Planet.UpdateResult>();
            Bombard(f, results);
            var lost = results.Where(r => r.Result == warLost).ToList();
            ok &= Check(Of(f.P("A"), Ship.ShipKind.SiegeShip, 0).Count == 0, "the bunker destroys the wounded siegeship");
            ok &= Check(Near(Of(f.P("A"), Ship.ShipKind.Bunker, 1)[0].Damage, 0.2f * 20f / 28f),
                "the destroyed siegeship still bombarded: 0.2 x 20 / 28 on the bunker (and the hit bunker does not repair)");
            ok &= Check(lost.Count == 1 && lost[0].PlayerID == 0 && lost[0].Data is CombatLoss loss && loss.Attacker == 1
                        && Near(loss.Ships, 1f),
                "the siegeship loss is a WarshipsLost result: victim the ship owner, attacker the bunker owner");
        }

        // (3) Arithmetic: bombard 100 (five siegeships) against one bunker of Defense 20 at K 20: 100 x 20 / 40 = 50.
        using (var f = Fixture.Line())
        {
            f.BunkerTemplate.shipDefense = 20f;
            f.Bunkers("A", 1, 1);
            f.Siege("A", 0, 5);
            f.War(0, 1);
            var reports = Bombard(f, new List<Planet.UpdateResult>());
            ok &= Check(Near(Of(f.P("A"), Ship.ShipKind.Bunker, 1)[0].Damage, 50f), "bombard 100 against defense 20 at K 20 does 50");
            ok &= Check(reports.Count == 1 && Near(reports[0].BombardDealt, 50f) && Near(reports[0].ShipDamage, 12f * 20f / 23f),
                "the report carries 50 dealt and the bunker's 12 x 20 / 23 back");
        }

        // (4) A warship-only fleet (bombard 1 each) against three bunkers: the bunkers win the exchange and barely notice.
        using (var f = Fixture.Line())
        {
            f.Ships("A", 0, 3);
            f.Bunkers("A", 1, 3);
            f.War(0, 1);
            var results = new List<Planet.UpdateResult>();
            var reports = Bombard(f, results);
            var bunkers = Of(f.P("A"), Ship.ShipKind.Bunker, 1);
            var ships = Of(f.P("A"), Ship.ShipKind.WarShip, 0);
            ok &= Check(bunkers.Count == 3 && Near(bunkers[0].Damage, 3f * 20f / 28f) && Near(bunkers[1].Damage, 0f),
                "three warships' bombard 3 does 3 x 20 / 28 = 2.14 to the first bunker; all three survive");
            ok &= Check(ships.Count == 3 && Near(ships[0].Damage, 36f * 0.8f), "the bunkers' 36 does 28.8 to the first warship");
            ok &= Check(reports.Count == 1 && reports[0].ShipDamage > reports[0].BombardDealt && reports[0].BunkersLeft == 3,
                "the bunkers' damage exceeds the fleet's");
            var lost = results.Where(r => r.Result == warLost).ToList();
            ok &= Check(lost.Count == 1 && lost[0].PlayerID == 0 && lost[0].Data is CombatLoss l && l.Attacker == 1
                        && Near(l.Ships, 0f) && Near(l.Engaged, 3f * 10f * 105f),
                "one WarshipsLost result (nothing died, engaged = the fleet's ship strength 3 x 10 x 105)");
        }

        // (5) Repair.
        using (var f = Fixture.Line())
        {
            f.Bunkers("A", 1, 1, 50f);
            Bombard(f, new List<Planet.UpdateResult>());
            ok &= Check(Near(Of(f.P("A"), Ship.ShipKind.Bunker, 1)[0].Damage, 35f),
                "an undamaged-this-turn bunker on a non-dominated planet heals 10% of its Health stat (50 - 15)");
        }
        using (var f = Fixture.Line())
        {
            f.Ships("A", 0, 1);
            f.Bunkers("A", 1, 2, 50f);
            f.War(0, 1);
            ok &= Check(BombardmentSystem.Dominated(f.P("A"), 1, f.Map, f.Stats), "a rival's warship with bombard 1 and no defender ship: dominated");
            Bombard(f, new List<Planet.UpdateResult>());
            var bunkers = Of(f.P("A"), Ship.ShipKind.Bunker, 1);
            ok &= Check(Near(bunkers[0].Damage, 50f + 20f / 28f), "the hit bunker takes 1 x 20 / 28 and does not heal");
            ok &= Check(Near(bunkers[1].Damage, 50f), "the bunker not hit this turn does not heal on a dominated planet");
        }
        using (var f = Fixture.Line())   // hit but not dominated: player 2, at war with 0 and at peace with 1, blocks 0's dominance
        {
            f.Ships("A", 0, 1);
            f.Ships("A", 2, 1);
            f.Bunkers("A", 1, 2, 50f);
            f.War(0, 1);
            f.War(0, 2);
            ok &= Check(!BombardmentSystem.Dominated(f.P("A"), 1, f.Map, f.Stats) && BombardmentSystem.Triggers(f.P("A"), 1, f.Map),
                "precondition: player 0 bombards but player 2 blocks its dominance");
            Bombard(f, new List<Planet.UpdateResult>());
            var bunkers = Of(f.P("A"), Ship.ShipKind.Bunker, 1);
            ok &= Check(Near(bunkers[0].Damage, 50f + 20f / 28f), "a bunker hit this turn does not heal even on a non-dominated planet");
            ok &= Check(Near(bunkers[1].Damage, 35f), "the other bunker heals: not hit, not dominated");
            ok &= Check(Near(Of(f.P("A"), Ship.ShipKind.WarShip, 2)[0].Damage, 0f), "the bunkers never fire at a player they are not at war with");
        }
        using (var f = Fixture.Line())   // a defender siegeship blocks dominance (and protects the bunkers)
        {
            f.Ships("A", 0, 1);
            f.Siege("A", 1, 1);
            f.Bunkers("A", 1, 1, 50f);
            f.War(0, 1);
            ok &= Check(!BombardmentSystem.Dominated(f.P("A"), 1, f.Map, f.Stats), "the bunker owner's siegeship blocks dominance");
            Bombard(f, new List<Planet.UpdateResult>());
            ok &= Check(Near(Of(f.P("A"), Ship.ShipKind.Bunker, 1)[0].Damage, 35f), "so its bunker repairs");
        }

        // (6) No bunkers: no bombardment results at all.
        using (var f = Fixture.Line())
        {
            f.Ships("A", 0, 1);
            f.Ships("A", 1, 1);
            f.War(0, 1);
            var results = new List<Planet.UpdateResult>();
            var reports = Bombard(f, results);
            ok &= Check(reports.Count == 0 && results.Count == 0 && f.P("A").DockedShips.All(s => Near(s.Damage, 0f)),
                "no bunkers: no reports, no results, no damage");
        }
        return ok;
    }

    private static List<ConversionReport> ConvTurn(Fixture f, int turn)
    {
        f.Map.Diplomacy.Turn = turn;
        return ConversionSystem.Resolve(f.Map, f.Stats, f.Constants, turn, new List<Planet.UpdateResult>());
    }

    // Task 8: dominance by bombard, the bombard-scaled pace, and the bunker gate. Fixture (MakeConstants sets the bombard reference
    // to 1; these checks set the real default 60, min factor 0.05): warship bombard 1, siegeship bombard 20, conversionTurnsBase 4 and
    // an all-foreign planet (my share 0) give 4 turns per flip at full pace, so the progress a turn is factor / 4.
    public static bool RunConversionGateCheck()
    {
        var ok = true;

        // Dominance by bombard: a siegeship dominates; more bombard beats more offense; an at-war siegeship blocks.
        using (var f = Fixture.Line())
        {
            var a = f.P("A");
            f.Siege("A", 0, 1);
            ok &= Check(ConversionSystem.Dominator(a, f.Map, f.Stats) == 0, "a siegeship alone (bombard 20) dominates");
            f.Siege("A", 1, 1);
            ok &= Check(ConversionSystem.Dominator(a, f.Map, f.Stats) == 0, "two peaceful siegeships, equal bombard: the lower id");
            f.Ships("A", 1, 1);
            ok &= Check(ConversionSystem.Dominator(a, f.Map, f.Stats) == 1, "player 1 has 20 + 1 bombard against 20: it dominates");
            f.War(0, 1);
            ok &= Check(ConversionSystem.Dominator(a, f.Map, f.Stats) == Planet.NoOwner, "at-war siegeships/warships docked together: nobody dominates");
        }
        using (var f = Fixture.Line())
        {
            f.Ships("A", 0, 1);     // offense 10, bombard 1
            f.Siege("A", 1, 1);     // offense 3, bombard 20
            ok &= Check(ConversionSystem.Dominator(f.P("A"), f.Map, f.Stats) == 1, "the tie-break is total bombard, not offense");
            f.Bunkers("A", 0, 3);
            ok &= Check(ConversionSystem.Dominator(f.P("A"), f.Map, f.Stats) == 1, "bunkers do not enter the dominance rule");
        }

        // A warship alone (bombard 1) dominates but converts at the minimum factor: 0.05 / 4 a turn.
        using (var f = Fixture.Line())
        {
            f.Constants.conversionBombardReference = 60f; f.Constants.conversionTurnsBase = 4f;
            var a = f.P("A");
            ConversionSelfCheck.Populate(a, (1, 4));
            f.Ships("A", 0, 1); f.War(0, 1);
            var reports = ConvTurn(f, 1);
            ok &= Check(a.ConversionBy == 0 && Near(a.ConversionProgress, 0.05f / 4f), "one warship (bombard 1): factor 0.05, progress 0.0125 after a turn");
            ok &= Check(reports.Any(r => r.What == ConversionReport.Kind.Start && Near(r.BombardFactor, 0.05f) && Near(r.TurnsPerFlip, 4f)),
                "the Start report carries the factor 0.05 and the 4 turns per flip");
            ConvTurn(f, 2);
            ok &= Check(Near(a.ConversionProgress, 2f * 0.05f / 4f), "progress is linear in the turns: 0.025 after two");
        }

        // Siegeships: 2 x 20 = 40 of 60 gives 2/3; 3 x 20 = 60 gives 1; 4 x 20 = 80 is clamped to 1.
        foreach (var (count, factor) in new[] { (2, 2f / 3f), (3, 1f), (4, 1f) })
        {
            using (var f = Fixture.Line())
            {
                f.Constants.conversionBombardReference = 60f; f.Constants.conversionTurnsBase = 4f;
                var a = f.P("A");
                ConversionSelfCheck.Populate(a, (1, 4));
                f.Siege("A", 0, count); f.War(0, 1);
                ConvTurn(f, 1);
                ok &= Check(Near(a.ConversionProgress, factor / 4f), $"{count} siegeships (bombard {count * 20}): progress {factor / 4f} a turn");
            }
        }
        using (var f = Fixture.Line())     // a damaged fleet's effective bombard is what counts: 3 siegeships, one at half health = 50
        {
            f.Constants.conversionBombardReference = 60f; f.Constants.conversionTurnsBase = 4f;
            var a = f.P("A");
            ConversionSelfCheck.Populate(a, (1, 4));
            f.Siege("A", 0, 2); f.Siege("A", 0, 1, 50f); f.War(0, 1);
            ConvTurn(f, 1);
            ok &= Check(Near(a.ConversionProgress, (50f / 60f) / 4f), "20 + 20 + 10 = 50 of 60: factor 5/6");
        }

        // A bunker (any owner's) blocks a session from starting, and a running one ends as DominanceLost.
        foreach (var bunkerOwner in new[] { 1, 0 })
        {
            using (var f = Fixture.Line())
            {
                f.Constants.conversionTurnsBase = 4f;   // reference 1 (fixture): factor 1, progress 0.25 a turn
                var a = f.P("A");
                ConversionSelfCheck.Populate(a, (1, 4));
                f.Ships("A", 0, 1); f.War(0, 1);
                f.Bunkers("A", bunkerOwner, 1);
                var r1 = ConvTurn(f, 1);
                ConvTurn(f, 2);
                ok &= Check(r1.Count == 0 && a.ConversionBy == Planet.NoOwner && Near(a.ConversionProgress, 0f) && a.Population.Count(p => p.Player == 1) == 4,
                    $"a bunker of player {bunkerOwner} on the planet: no session starts, no progress");

                // The last bunker destroyed: the very next Resolve starts the session.
                a.DestroyDockedShip(a.DockedShips.First(s => s.Kind == Ship.ShipKind.Bunker));
                var r3 = ConvTurn(f, 3);
                ok &= Check(r3.Any(r => r.What == ConversionReport.Kind.Start) && a.ConversionBy == 0 && Near(a.ConversionProgress, 0.25f),
                    "the last bunker gone: the next Resolve starts the session and adds a turn of progress");
            }
        }
        using (var f = Fixture.Line())
        {
            f.Constants.conversionTurnsBase = 4f;
            var a = f.P("A");
            ConversionSelfCheck.Populate(a, (1, 4));
            f.Ships("A", 0, 1); f.War(0, 1);
            ConvTurn(f, 1); ConvTurn(f, 2);
            ok &= Check(a.ConversionBy == 0 && Near(a.ConversionProgress, 0.5f), "precondition: a session at progress 0.5");
            f.Bunkers("A", 1, 2);
            var r3 = ConvTurn(f, 3);
            ok &= Check(r3.Any(r => r.What == ConversionReport.Kind.End && r.EndReason == ConversionEndReason.DominanceLost)
                        && !r3.Any(r => r.What == ConversionReport.Kind.Start) && a.ConversionBy == Planet.NoOwner && Near(a.ConversionProgress, 0f),
                "a bunker appearing mid-session ends it as DominanceLost and resets progress");
            ConvTurn(f, 4);
            ok &= Check(a.ConversionBy == Planet.NoOwner && Near(a.ConversionProgress, 0f), "and it stays blocked while a bunker stands");
            foreach (var bunker in a.DockedShips.Where(s => s.Kind == Ship.ShipKind.Bunker).ToList()) a.DestroyDockedShip(bunker);
            ConvTurn(f, 5);
            ok &= Check(a.ConversionBy == 0 && Near(a.ConversionProgress, 0.25f), "with every bunker destroyed the conversion starts again from 0 (one turn of progress)");
        }

        // BombardmentSystem.Dominated is the same rule (a bunker owner's own warship still blocks).
        using (var f = Fixture.Line())
        {
            f.Ships("A", 0, 1); f.Bunkers("A", 1, 1); f.Ships("A", 1, 1); f.War(0, 1);
            ok &= Check(!BombardmentSystem.Dominated(f.P("A"), 1, f.Map, f.Stats) && ConversionSystem.Dominator(f.P("A"), f.Map, f.Stats) == Planet.NoOwner,
                "Dominated and Dominator agree: an at-war defender warship means nobody dominates");
        }
        return ok;
    }

    private static FightProjection Project(Fixture f, string planet, int player, params GameAI.GameAIOrder[] orders)
        => FightProjector.Project(f.Map, f.P(planet), player, f.Stats, f.Constants, orders);

    // Task 9: the fight projection runs the bombardment step after its ship-to-ship round each projected turn. Templates as in
    // RunBombardmentCheck: warship 10 / 100 / 5, bombard 1; siegeship 3 / 100 / 3, bombard 20; bunker 12 / 150 / 8; K 20.
    public static bool RunProjectionBunkerFireCheck()
    {
        var ok = true;

        // A warship-only group against three bunkers: its bombard 3 barely scratches them, their 36 x 0.8 a turn wipes it out (it
        // needs 300 / 0.8 = 375 of pool; the pool stays above 32 since the first bunker takes at most 2.14 a turn, so by turn 12).
        using (var f = Fixture.Line())
        {
            f.Ships("A", 0, 3);
            f.Bunkers("A", 1, 3);
            f.War(0, 1);
            var p = Project(f, "A", 0);
            ok &= Check(p != null && p.Rivals.SequenceEqual(new[] { 1 }) && p.MyShips == 3,
                "a warship group against bunkers alone is projected (rival 1 holds only bunkers)");
            ok &= Check(p != null && p.MyGroupWiped && Near(p.ProjectedLossFraction, 1f) && p.RivalSurvives && p.Turns > 0 && p.Turns <= 12,
                "the bunkers wipe the warship-only group out within 12 turns and survive");
            ok &= Check(p != null && Near(p.RivalStrength, 3f * 12f * 158f), "the rival's strength is its bunkers': 3 x 12 x (150 + 8) = 5688");
            var perceived = FightProjector.ProjectPerceived(f.Map, f.P("A"), 0, f.Stats, f.Constants, new GameAI.GameAIOrder[0], new System.Random(5));
            ok &= Check(perceived.Samples.Count == 8 && perceived.Samples.All(s => s.Wiped && s.RivalSurvives),
                "every sample (bunkers seen at 0.75 to 1.25) is a wipe-out too");
            var results = new List<Planet.UpdateResult>();
            GameAI.AppendFightProjections(f.Map, f.Stats, f.Constants, new List<GameAI.GameAIOrder>(), results, new System.Random(3));
            ok &= Check(results.Count == 1 && results[0].PlayerID == 0 && results[0].Data is FightProjection fp && fp.MyGroupWiped,
                "the engine step appends the projection for the warship owner facing only bunkers (none for the bunker owner)");
        }
        using (var f = Fixture.Line())    // bunkers of a player I am not at war with: no rival, no projection
        {
            f.Ships("A", 0, 3);
            f.Bunkers("A", 1, 3);
            ok &= Check(Project(f, "A", 0) == null, "bunkers of a player at peace: nothing to project");
        }

        // Hand-computed: eleven siegeships against one bunker. Bombard 220 >= 150 / (20 / 28) = 210 destroys it in the first turn,
        // and it fires back 12 x 20 / 23 = 10.435 on one siegeship (health 89.565, offense 2.687, strength 2.687 x 92.565 = 248.72).
        // My strength 11 x 3 x 103 = 3399, left 10 x 309 + 248.72 = 3338.72: a loss of 60.28 / 3399 = 0.01774.
        using (var f = Fixture.Line())
        {
            f.Bunkers("A", 1, 1);
            f.Siege("A", 0, 11);
            f.War(0, 1);
            var p = Project(f, "A", 0);
            ok &= Check(p != null && !p.RivalSurvives && !p.MyGroupWiped && p.Turns == 1 && Near(p.RivalStrengthLeft, 0f),
                "eleven siegeships destroy the bunker in the first projected turn");
            ok &= Check(p != null && Near(p.MyStrength, 3399f) && Near(p.RivalStrength, 1896f) && Near(p.ProjectedLossFraction, 60.2813f / 3399f),
                "strength 3399 against 1896; the bunker's last shot costs 0.01774 of my strength");
        }

        // A siegeship-heavy group against two bunkers over several turns: bombard 120 a turn (85.7 after K / (K + 8)) kills the first
        // bunker in turn 2 and the second in turn 4, while the bunkers' fire (24, then less) only wounds the first siegeship.
        using (var f = Fixture.Line())
        {
            f.Bunkers("A", 1, 2);
            f.Siege("A", 0, 6);
            f.War(0, 1);
            var p = Project(f, "A", 0);
            // All bunker fire lands on the first siegeship: 20.870, 14.907, 9.152, 3.544, leaving health 51.528, offense 1.546,
            // strength 1.546 x 54.528 = 84.29 of 309 (strength is quadratic in health): loss (309 - 84.29) / 1854 = 0.1212.
            ok &= Check(p != null && !p.RivalSurvives && !p.MyGroupWiped && p.Turns == 4 && Near(p.ProjectedLossFraction, 0.1212f),
                "six siegeships destroy both bunkers by the fourth projected turn and keep every ship (loss 0.1212, all on one wounded ship)");
        }

        // A bunker owner's ship protects its bunkers in the projection as in the real step: the bunkers only fire after it dies.
        using (var f = Fixture.Line())
        {
            f.Ships("A", 1, 1);
            f.Bunkers("A", 1, 3);
            f.Ships("A", 0, 3);
            f.War(0, 1);
            var guarded = Project(f, "A", 0);
            ok &= Check(guarded != null && guarded.RivalSurvives && guarded.MyGroupWiped,
                "my three warships kill the guard, then the three bunkers wipe them out: the rival survives");
        }

        // An inbound siegeship alone, landing at a planet whose defender holds two warships: my group exists only in flight.
        // Hand-computed strength: one siegeship, 3 x (100 + 3) = 309. The defenders' 20 a turn at 20 / 23 kill it by turn 6.
        using (var f = Fixture.Line())
        {
            f.Ships("A", 1, 2);
            f.War(0, 1);
            ok &= Check(Project(f, "A", 0) == null, "nothing of mine docked or inbound: no projection");
            var follower = new GameAI.GameAIOrder
            {
                Type = GameAI.GameAIOrder.OrderType.OrderTypeShipTransport, PlayerId = 0, Origin = "B", Target = "A", TimingDelay = 1, Data = 1,
                Fleet = new GameAI.GameAIOrder.ShipFleetPayload
                {
                    Kind = Ship.ShipKind.SiegeShip,
                    Snapshots = new List<List<string>> { new List<string>() },
                    Damage = new List<float>(),
                },
            };
            var p = Project(f, "A", 0, follower);
            ok &= Check(p != null && p.MyShips == 0 && p.InboundShips == 1 && Near(p.MyStrength, 309f),
                "the inbound siegeship is the whole group: 0 docked, 1 inbound, strength 309");
            ok &= Check(p != null && p.MyGroupWiped && Near(p.ProjectedLossFraction, 1f) && p.RivalSurvives,
                "it is projected to be destroyed by the defenders' warships (the follower gate reads this as a loss)");

            // The same siegeship landing in round 3: rounds 1 and 2 are quiet waiting turns, then the same fight as above.
            follower.TimingDelay = 3;
            var late = Project(f, "A", 0, follower);
            ok &= Check(late != null && late.InboundShips == 1 && Near(late.MyStrength, 309f) && late.MyGroupWiped
                        && Near(late.ProjectedLossFraction, 1f) && late.RivalSurvives && late.Turns > 3,
                "landing in round 3: strength 309, wiped with a loss of 1 (not 'wiped with loss 0' from ending before it lands)");
        }

        // No bunkers, a docked group wiped before its reinforcement lands: the old behaviour, the projection ends with the docked group.
        // My one warship has health 10 (offense 1, strength 1 x (10 + 5) = 15); the rival's two warships (pool 20 >= 10 / 0.8 = 12.5)
        // kill it in round 1 while it does 1 x 0.8 = 0.8 to one of them (health 99.2, offense 9.92, strength 9.92 x 104.2 = 1033.66).
        // Thirteen warships of mine landing in round 4 would win, but they are never reached: wiped, loss 1, rival left 2083.66.
        using (var f = Fixture.Line())
        {
            f.Ships("A", 1, 2);
            f.Ships("A", 0, 1, 90f);
            f.War(0, 1);
            var reinforcement = new GameAI.GameAIOrder
            {
                Type = GameAI.GameAIOrder.OrderType.OrderTypeShipTransport, PlayerId = 0, Origin = "B", Target = "A", TimingDelay = 4, Data = 13,
                Fleet = new GameAI.GameAIOrder.ShipFleetPayload
                {
                    Kind = Ship.ShipKind.WarShip,
                    Snapshots = Enumerable.Range(0, 13).Select(_ => new List<string>()).ToList(),
                    Damage = new List<float>(),
                },
            };
            var p = Project(f, "A", 0, reinforcement);
            ok &= Check(p != null && p.MyShips == 1 && p.InboundShips == 0 && Near(p.MyStrength, 15f) && Near(p.MyStrengthLeft, 0f)
                        && p.MyGroupWiped && Near(p.ProjectedLossFraction, 1f) && p.RivalSurvives && p.Turns == 1
                        && Near(p.RivalStrength, 2100f) && Near(p.RivalStrengthLeft, 2083.664f),
                "a docked group wiped in round 1 ends the projection: the round-4 reinforcement is not counted (as before bunkers)");
        }

        // A strong inbound group landing in round 3: 11 siegeships against 1 bunker, nothing of mine docked. Same numbers as the docked
        // case above (bunker at dock index 0, siegeships after it): strength 3399, loss 60.28 / 3399 = 0.01774, the bunker dies on turn 3.
        using (var f = Fixture.Line())
        {
            f.Bunkers("A", 1, 1);
            f.War(0, 1);
            var group = new GameAI.GameAIOrder
            {
                Type = GameAI.GameAIOrder.OrderType.OrderTypeShipTransport, PlayerId = 0, Origin = "B", Target = "A", TimingDelay = 3, Data = 11,
                Fleet = new GameAI.GameAIOrder.ShipFleetPayload
                {
                    Kind = Ship.ShipKind.SiegeShip,
                    Snapshots = Enumerable.Range(0, 11).Select(_ => new List<string>()).ToList(),
                    Damage = new List<float>(),
                },
            };
            var p = Project(f, "A", 0, group);
            ok &= Check(p != null && p.MyShips == 0 && p.InboundShips == 11 && Near(p.MyStrength, 3399f),
                "eleven inbound siegeships: 0 docked, 11 inbound, strength 3399");
            ok &= Check(p != null && !p.MyGroupWiped && !p.RivalSurvives && p.Turns == 3 && Near(p.ProjectedLossFraction, 60.2813f / 3399f),
                "they land in round 3 and destroy the bunker that turn: loss 0.01774, not wiped");
        }
        return ok;
    }

    // Saves: siegeship and bunker kinds and damage survive JsonUtility, the restore loop (the one in
    // GameAIMap.SetPlanetSimulationStats, which needs Gameboard.Instance as a whole, so it is mirrored here) docks them with the
    // right kind/owner/damage, a siegeship fleet keeps its Kind through ToSave/FromSave, an older PlanetSave with only
    // warships loads unchanged, and the derived incoming counters cope with a siegeship fleet.
    public static bool RunSaveRoundTripCheck()
    {
        var ok = true;
        var planetSave = new SaveLoadSystem.GameSave.PlanetSave
        {
            name = "A",
            dockedShips = new List<SaveLoadSystem.GameSave.ShipSave>
            {
                new SaveLoadSystem.GameSave.ShipSave { kind = Ship.ShipKind.SiegeShip, owner = 1, researchSnapshot = new List<string> { "s1" }, damage = 12.5f },
                new SaveLoadSystem.GameSave.ShipSave { kind = Ship.ShipKind.Bunker, owner = 0, researchSnapshot = new List<string> { "b1", "b2" }, damage = 40f },
                new SaveLoadSystem.GameSave.ShipSave { kind = Ship.ShipKind.WarShip, owner = 0, researchSnapshot = new List<string>(), damage = 0f },
            },
        };
        var loaded = JsonUtility.FromJson<SaveLoadSystem.GameSave.PlanetSave>(JsonUtility.ToJson(planetSave));
        ok &= Check(loaded.dockedShips != null && loaded.dockedShips.Count == 3, "save: three docked ships survive JsonUtility");
        if (loaded.dockedShips == null || loaded.dockedShips.Count != 3) return false;
        ok &= Check(loaded.dockedShips[0].kind == Ship.ShipKind.SiegeShip && loaded.dockedShips[0].owner == 1
                    && Near(loaded.dockedShips[0].damage, 12.5f) && loaded.dockedShips[0].researchSnapshot.SequenceEqual(new[] { "s1" }),
            "save: a siegeship keeps kind, owner, damage and snapshot");
        ok &= Check(loaded.dockedShips[1].kind == Ship.ShipKind.Bunker && loaded.dockedShips[1].owner == 0
                    && Near(loaded.dockedShips[1].damage, 40f) && loaded.dockedShips[1].researchSnapshot.SequenceEqual(new[] { "b1", "b2" }),
            "save: a bunker keeps kind, owner, damage and snapshot");

        using (var f = Fixture.Line())
        {
            var a = f.P("A");
            foreach (var s in loaded.dockedShips) a.DockShipFromSave(s.kind, s.owner, s.researchSnapshot, s.damage);
            ok &= Check(a.DockedShips.Count == 3, "restore: three ships docked");
            if (a.DockedShips.Count == 3)
            {
                ok &= Check(a.DockedShips[0].Kind == Ship.ShipKind.SiegeShip && a.DockedShips[0].Owner == 1 && Near(a.DockedShips[0].Damage, 12.5f),
                    "restore: the siegeship docks with its kind, owner and damage");
                ok &= Check(a.DockedShips[1].Kind == Ship.ShipKind.Bunker && a.DockedShips[1].Owner == 0 && Near(a.DockedShips[1].Damage, 40f)
                            && a.DockedShips[1].ResearchSnapshot.Count == 2,
                    "restore: the bunker docks with its kind, owner, damage and snapshot");
            }
            ok &= Check(a.BunkerCount(0) == 1, "restore: BunkerCount sees the restored bunker");

            // A siegeship fleet in flight.
            var fleet = new GameAI.GameAIOrder.ShipFleetPayload
            {
                Kind = Ship.ShipKind.SiegeShip,
                Snapshots = new List<List<string>> { new List<string> { "x" }, new List<string>() },
                Damage = new List<float> { 5f, 0f },
            };
            var order = new SaveLoadSystem.GameSave.OrderSave
            {
                type = GameAI.GameAIOrder.OrderType.OrderTypeShipTransport, target = "A", origin = "B", playerId = 1, data = 2f,
                dataType = "int", fleetShips = fleet.ToSave(1),
            };
            var orderBack = JsonUtility.FromJson<SaveLoadSystem.GameSave.OrderSave>(JsonUtility.ToJson(order));
            var restored = GameAI.GameAIOrder.ShipFleetPayload.FromSave(orderBack.fleetShips);
            ok &= Check(restored != null && restored.Kind == Ship.ShipKind.SiegeShip && restored.Snapshots.Count == 2
                        && restored.Snapshots[0].SequenceEqual(new[] { "x" }) && Near(restored.DamageAt(0), 5f) && Near(restored.DamageAt(1), 0f),
                "save: a siegeship fleet keeps its Kind, snapshots and damage through ToSave/JsonUtility/FromSave");

            // Derived counters: a siegeship fleet counts as incoming siegeships and adds no warship offense; a missing Fleet counts as warships.
            var arrival = new GameAI.GameAIOrder
            {
                Type = GameAI.GameAIOrder.OrderType.OrderTypeShipTransport, PlayerId = 1, Origin = "B", Target = "A", TimingDelay = 2,
                Data = 2, Fleet = restored,
            };
            var legacy = new GameAI.GameAIOrder
            {
                Type = GameAI.GameAIOrder.OrderType.OrderTypeShipTransport, PlayerId = 1, Origin = "C", Target = "A", TimingDelay = 2, Data = 3,
            };
            var orders = new List<GameAI.GameAIOrder> { arrival, legacy };
            f.Map.RecomputeIncomingShips(orders);
            f.Map.RecomputeIncomingOffense(orders, f.Stats);
            ok &= Check(a.GetIncomingShips(Ship.ShipKind.SiegeShip, 1) == 2 && a.GetIncomingShips(Ship.ShipKind.WarShip, 1) == 3,
                "incoming: a siegeship fleet counts as siegeships, a fleet-less order as warships");
            ok &= Check(Near(a.GetIncomingOffense(Ship.ShipKind.SiegeShip, 1), 0f),
                "incoming offense: a siegeship fleet adds none (Task 13 adds incoming bombard)");
        }

        // An older save: warships only, no damage key, no bunker or siegeship entries.
        const string oldJson = "{\"name\":\"A\",\"dockedShips\":[{\"kind\":1,\"owner\":0,\"researchSnapshot\":[\"w\"]}]}";
        var old = JsonUtility.FromJson<SaveLoadSystem.GameSave.PlanetSave>(oldJson);
        ok &= Check(old.dockedShips != null && old.dockedShips.Count == 1 && old.dockedShips[0].kind == Ship.ShipKind.WarShip
                    && old.dockedShips[0].owner == 0 && Near(old.dockedShips[0].damage, 0f) && old.dockedShips[0].researchSnapshot.Count == 1,
            "older save: a warship-only dockedShips list loads unchanged at full health");
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
