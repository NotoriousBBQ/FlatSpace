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
        ok &= RunBunkerChokepointCheck();
        ok &= RunSiegeSizingCheck();
        ok &= RunSiegeshipWeightCheck();
        ok &= RunAssaultCompositionCheck();
        ok &= RunFollowerGateCheck();
        ok &= RunEscortCheck();
        ok &= RunEscortReservationCheck();
        ok &= RunConversionBlockedCheck();
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
                ok &= Check(item.tier == t && Near(item.cost, 6000f * t), $"{name}: tier {t}, cost {6000 * t}");
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

    // Chokepoint weighting: on the line A-B-C-D, B and C tie at percentile 0.67 (A and D are leaves), so 0.6 makes exactly B and C
    // chokepoints; C is also outer (D is empty), B interior. Rival warships docked on B and C, at war.
    public static bool RunBunkerChokepointCheck()
    {
        var ok = true;
        var consolidate = PlayerAI.AIStrategy.AIStrategyConsolidate;

        using (var f = Fixture.Line())
        {
            f.Constants.chokepointPercentile = 0.6f;
            f.Colonize("A", 0); f.Colonize("B", 0); f.Colonize("C", 0);
            f.Ships("B", 1, 1); f.Ships("C", 1, 1);
            f.War(0, 1);
            ok &= Check(f.Map.IsChokepoint("B") && f.Map.IsChokepoint("C") && !f.Map.IsChokepoint("A"),
                "precondition: B and C are chokepoints, A is not");

            using (var t = new ThreatAi(f, consolidate))
            {
                ok &= Check(Near(t.Weight("A"), 2f), "a non-chokepoint interior planet beside a rival ship is unchanged: 1 x 1 x 2 = 2");
                ok &= Check(Near(t.Weight("B"), 6f), "an interior chokepoint holding a rival ship: 1 x 3 x 2 = 6");
                ok &= Check(Near(t.Weight("C"), 6f), "an outer chokepoint takes the larger factor (3 over 2), not the product: 1 x 3 x 2 = 6");
            }

            f.Constants.bunkerChokepointFactor = 2f;
            using (var t = new ThreatAi(f, consolidate))
                ok &= Check(Near(t.Weight("B"), 4f), "chokepoint factor 2: 1 x 2 x 2 = 4 (the factor is flat, not scaled by percentile)");
            f.Constants.bunkerChokepointFactor = 1f;
            using (var t = new ThreatAi(f, consolidate))
                ok &= Check(Near(t.Weight("B"), 2f), "chokepoint factor 1: B is weighted as an interior planet again");

            f.Constants.bunkerChokepointFactor = 3f;
            f.Constants.bunkerOuterFactor = 5f;
            using (var t = new ThreatAi(f, consolidate))
                ok &= Check(Near(t.Weight("C"), 10f), "outer 5 beats chokepoint 3 on a planet that is both: 1 x 5 x 2 = 10, not 30");
        }

        // The gate is unchanged: a chokepoint with no rival ship nearby and no known enemy planet still gets nothing.
        using (var f = Fixture.Line())
        {
            f.Constants.chokepointPercentile = 0.6f;
            f.Colonize("A", 0); f.Colonize("B", 0); f.Colonize("C", 0);
            f.War(0, 1);
            f.Map.Knowledge.Update(f.Map, 2);
            using (var t = new ThreatAi(f, consolidate))
                ok &= Check(t.Weight("B") == 0f && t.Weight("C") == 0f,
                    "a chokepoint with no rival nearby and no known enemy planet: weight 0");
        }

        // The log line carries the chokepoint flag as the trailing field.
        ok &= Check(AITuningLogger.FormatBunkerBuilt(12, 1, "Normal 2", 3, 7, 6f, true) == "T12|P1|BunkerBuilt|Normal 2|3|7|6|1",
            "BunkerBuilt ends with chokepoint 1");
        ok &= Check(AITuningLogger.FormatBunkerBuilt(12, 1, "Normal 2", 3, 7, 6f, false) == "T12|P1|BunkerBuilt|Normal 2|3|7|6|0",
            "BunkerBuilt ends with chokepoint 0");
        return ok;
    }

    // Task 12: the threat-weighted Bunker situational multiplier (defaults: base 1, outer factor 2, chokepoint factor 3, blockaded boost 3,
    // rival-adjacent boost 2). Line map A-B-C-D, player 0 colonizes A, B, C (D empty): C is outer, A and B are interior; the
    // chokepoint percentile is set above 1 so no planet is a chokepoint.
    public static bool RunBunkerThreatWeightCheck()
    {
        var ok = true;
        var consolidate = PlayerAI.AIStrategy.AIStrategyConsolidate;

        using (var f = Fixture.Line())
        {
            ok &= Check(Near(f.Constants.bunkerBaseWeight, 1f) && Near(f.Constants.bunkerOuterFactor, 2f)
                        && Near(f.Constants.bunkerBlockadedBoost, 3f) && Near(f.Constants.bunkerRivalAdjacentBoost, 2f)
                        && Near(f.Constants.bunkerChokepointFactor, 3f),
                "the bunker tunables default to 1, 2, 3 and 2, with a chokepoint factor of 3");
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

    // Task 13 fixture: line A-B-C-D, player 0 colonizes A, B and C, player 1 colonizes D, at war, knowledge updated (D is known as
    // C's neighbour). With `bunkers`, D holds 4 of player 1's bunkers of Defense 25 (no Bunker research lines: the base) = 100.
    // Siegeship template: bombard 20, health 100 (no Siegeship research lines). Garrisons are 0, so every warship is spare.
    private static Fixture BunkeredLine(bool bunkers)
    {
        var f = Fixture.Line();
        f.BunkerTemplate.shipDefense = 25f;
        f.Constants.garrisonOuter = 0;
        f.Colonize("A", 0); f.Colonize("B", 0); f.Colonize("C", 0); f.Colonize("D", 1);
        if (bunkers) f.Bunkers("D", 1, 4);
        f.War(0, 1);
        f.Map.Knowledge.Update(f.Map, 2);
        return f;
    }

    private static AssaultPlanner SiegePlanner(Fixture f)
        => new AssaultPlanner(f.Map, 0, stats: f.Stats, warRivals: new HashSet<int> { 1 });

    private static GameAI.GameAIOrder SiegeFleet(string origin, string target, int player, int ships)
    {
        var fleet = new GameAI.GameAIOrder.ShipFleetPayload
        {
            Kind = Ship.ShipKind.SiegeShip,
            Snapshots = Enumerable.Range(0, ships).Select(_ => new List<string>()).ToList(),
            Damage = Enumerable.Repeat(0f, ships).ToList(),
        };
        return new GameAI.GameAIOrder
        {
            Type = GameAI.GameAIOrder.OrderType.OrderTypeShipTransport, PlayerId = player, Origin = origin, Target = target,
            TimingDelay = 2, Data = ships, Fleet = fleet,
        };
    }

    // Task 13: bombard needed = known bunker defense x siegeBombardMargin; siegeships still wanted = ceil((needed - committed
    // bombard) / bombard per siegeship), capped so siegeships <= siegeshipMaxFleetShare of the fleet:
    // cap = floor(share x W / (1 - share)), W = max(RequiredForce, my warships docked + incoming at the target).
    public static bool RunSiegeSizingCheck()
    {
        var ok = true;
        using (var f = Fixture.Line())
        {
            ok &= Check(Near(f.Constants.siegeBombardMargin, 1.3f) && Near(f.Constants.siegeshipMaxFleetShare, 0.5f),
                "siegeBombardMargin defaults to 1.3 and siegeshipMaxFleetShare to 0.5");
            f.Colonize("C", 0); f.Colonize("D", 1); f.Bunkers("D", 1, 2); f.War(0, 1);
            ok &= Check(SiegePlanner(f).KnownBunkerDefense(f.P("D")) == 0f, "an unknown planet's bunkers are not seen: defense 0");
        }

        using (var f = BunkeredLine(true))
        {
            var d = f.P("D");
            var assault = SiegePlanner(f);
            ok &= Check(Near(assault.KnownBunkerDefense(d), 100f), "4 known bunkers of defense 25: 100");
            ok &= Check(Near(assault.RequiredBombard(d), 130f), "bombard needed: 100 x 1.3 = 130");
            ok &= Check(assault.RequiredForce() == 3 && assault.SiegeshipCap(d) == 3,
                "no rival warships: force 3 (the minimum), cap floor(0.5 x 3 / 0.5) = 3");
            ok &= Check(assault.WantedSiegeships(d, 20f) == 3, "ceil(130 / 20) = 7 wanted, stopped at the share cap of 3");
            ok &= Check(assault.WantedSiegeships(d, 0f) == 0, "a siegeship with no bombard adds nothing: 0 wanted");

            f.Constants.assaultMinimumShips = 10;   // W = 10, cap 10: the need decides
            ok &= Check(assault.SiegeshipCap(d) == 10 && assault.WantedSiegeships(d, 20f) == 7,
                "with a force of 10 the cap is 10 and ceil(130 / 20) = 7 are wanted");

            // Two siegeships in flight (40 bombard) count toward the need: ceil(90 / 20) = 5, room 10 - 2 = 8.
            var orders = new List<GameAI.GameAIOrder> { SiegeFleet("B", "D", 0, 2) };
            f.Map.RecomputeIncomingShips(orders);
            f.Map.RecomputeIncomingOffense(orders, f.Stats);
            ok &= Check(Near(d.GetIncomingBombard(Ship.ShipKind.SiegeShip, 0), 40f) && assault.CommittedSiegeships(d) == 2,
                "precondition: 2 siegeships and 40 bombard in flight to D");
            ok &= Check(Near(assault.CommittedSiegeBombard(d), 40f) && assault.WantedSiegeships(d, 20f) == 5,
                "incoming siegeship bombard counts toward the need: ceil(90 / 20) = 5 still wanted");

            // One more docked at D (20): ceil(70 / 20) = 4.
            f.Siege("D", 0, 1);
            ok &= Check(Near(assault.CommittedSiegeBombard(d), 60f) && assault.WantedSiegeships(d, 20f) == 4,
                "docked siegeship bombard counts too: ceil(70 / 20) = 4 still wanted");

            // Back to the minimum force of 3: the 3 committed fill the cap, none more.
            f.Constants.assaultMinimumShips = 3;
            ok &= Check(assault.WantedSiegeships(d, 20f) == 0, "3 committed siegeships fill a cap of 3: none more wanted");

            f.Constants.siegeshipMaxFleetShare = 0f;
            ok &= Check(assault.SiegeshipCap(d) == 0, "a share of 0: no siegeships");
            f.Constants.siegeshipMaxFleetShare = 1f;
            ok &= Check(assault.SiegeshipCap(d) == int.MaxValue, "a share of 1: no cap");
        }

        using (var f = BunkeredLine(false))
        {
            var d = f.P("D");
            var assault = SiegePlanner(f);
            ok &= Check(assault.KnownBunkerDefense(d) == 0f && assault.RequiredBombard(d) == 0f && assault.WantedSiegeships(d, 20f) == 0,
                "no bunkers: no bombard needed and 0 siegeships wanted");

            // Pace term: an enemy-occupied target wants siegePaceFraction x conversionBombardReference even without bunkers.
            f.Constants.conversionBombardReference = 60f;
            f.Constants.siegePaceFraction = 1f;
            ok &= Check(Near(assault.RequiredBombard(d), 60f) && assault.WantedSiegeships(d, 20f) == 3,
                "pace term: 60 bombard wanted at a bunkerless enemy planet, ceil(60 / 20) = 3 (the cap of 3)");
            f.Constants.siegePaceFraction = 0.5f;
            ok &= Check(Near(assault.RequiredBombard(d), 30f), "a fraction of 0.5 wants 30");
            f.Constants.siegePaceFraction = 0f;
            ok &= Check(assault.RequiredBombard(d) == 0f, "a fraction of 0 switches the pace term off");
            f.Constants.siegePaceFraction = 1f;
            ok &= Check(assault.RequiredBombard(f.P("A")) == 0f, "no enemy inhabitant at A: no pace need");
        }

        // The pace term never lowers a bunker need: 130 (bunkers) beats 60 (pace).
        using (var f = BunkeredLine(true))
        {
            f.Constants.conversionBombardReference = 60f;
            f.Constants.siegePaceFraction = 1f;
            ok &= Check(Near(SiegePlanner(f).RequiredBombard(f.P("D")), 130f), "the larger need decides: 130, not 60");
        }
        return ok;
    }

    // Task 13: the Siegeship situational multiplier is the Warship shortfall shape on wanted vs owned siegeships (boost 2, cap 1.5),
    // 0 when none is wanted (no bunkers) and 0 outside Consolidate/Amass; computed once per production turn.
    public static bool RunSiegeshipWeightCheck()
    {
        var ok = true;
        var consolidate = PlayerAI.AIStrategy.AIStrategyConsolidate;
        using (var f = BunkeredLine(true))
        {
            f.Ships("B", 0, 2);   // a holder of my warships, so D is the assault target
            using (var t = new ThreatAi(f, consolidate))
            {
                t.Item.subType = "Siegeship";
                ok &= Check(t.Ai.WantedSiegeships() == 3 && t.Ai.OwnedSiegeships() == 0,
                    "precondition: 3 siegeships wanted (the cap of 3), none owned");
                ok &= Check(Near(t.Weight("A"), 3f), "none owned: 1 + 2 x 3/3 = 3");
                ok &= Check(Near(t.Weight("B"), 3f) && t.Ai.SiegeMultiplierBuilds == 1,
                    "the multiplier is computed once per production turn");
            }
            f.Siege("A", 0, 1);
            using (var t = new ThreatAi(f, consolidate))
            {
                t.Item.subType = "Siegeship";
                ok &= Check(Near(t.Weight("A"), 1f + 2f * 2f / 3f), "one owned: 1 + 2 x 2/3");
            }
            using (var t = new ThreatAi(f, PlayerAI.AIStrategy.AIStrategyAmass))
            {
                t.Item.subType = "Siegeship";
                ok &= Check(t.Weight("A") > 1f, "Amass is Consolidate-like: boosted too");
            }
            foreach (var strategy in new[] { PlayerAI.AIStrategy.AIStrategyExpand, PlayerAI.AIStrategy.AIStrategyNone })
            {
                using var t = new ThreatAi(f, strategy);
                t.Item.subType = "Siegeship";
                ok &= Check(t.Weight("A") == 0f, $"{strategy} builds no siegeships: weight 0");
            }
        }
        using (var f = BunkeredLine(false))
        {
            f.Ships("B", 0, 2);
            using var t = new ThreatAi(f, consolidate);
            t.Item.subType = "Siegeship";
            ok &= Check(t.Ai.WantedSiegeships() == 0 && t.Weight("A") == 0f,
                "no bunkers anywhere: 0 siegeships wanted and the Siegeship weight is 0 (the choice is dropped)");
        }
        return ok;
    }

    private static List<ShipAction> PlanAt(Fixture f, AssaultPlanner assault, string target)
    {
        var transport = new ShipTransportPlanner(f.Map, 0, PlayerAI.AIStrategy.AIStrategyConsolidate) { HeldPlanet = target };
        var home = transport.Plan();
        return assault.Plan(f.P(target), transport.LastStates, home);
    }

    // Task 13: AssaultPlanner.Plan sends the warship force and, as separate SiegeShip actions, the siegeships that exist (cheapest
    // first, up to the bombard need and the share cap). With no siegeship the warships leave alone (no stall). The garrison planner
    // never moves siegeships.
    public static bool RunAssaultCompositionCheck()
    {
        var ok = true;
        // 5 warships at B; 2 siegeships at C (path 100) and 2 at A (path 300). Force 3, cap 3, need 130 (> 3 x 20).
        using (var f = BunkeredLine(true))
        {
            f.Ships("B", 0, 5); f.Siege("C", 0, 2); f.Siege("A", 0, 2);
            var actions = PlanAt(f, SiegePlanner(f), "D");
            ok &= Check(actions.Count == 3, $"three actions: the warship force and two siegeship groups (got {actions.Count})");
            ok &= Check(actions.Exists(a => a.Kind == Ship.ShipKind.WarShip && a.Origin == "B" && a.Target == "D" && a.Count == 3),
                "the warship force: 3 from B, sized as before");
            ok &= Check(actions.Exists(a => a.Kind == Ship.ShipKind.SiegeShip && a.Origin == "C" && a.Count == 2)
                        && actions.Exists(a => a.Kind == Ship.ShipKind.SiegeShip && a.Origin == "A" && a.Count == 1),
                "siegeships: C's 2 (cheapest) then 1 of A's 2, stopping at the cap of 3");

            var gated = SiegePlanner(f);
            gated.SiegeshipGate = (origin, target, count, departing) => origin != "C";
            var gatedActions = PlanAt(f, gated, "D");
            ok &= Check(!gatedActions.Exists(a => a.Origin == "C")
                        && gatedActions.Exists(a => a.Kind == Ship.ShipKind.SiegeShip && a.Origin == "A" && a.Count == 2)
                        && gatedActions.Exists(a => a.Kind == Ship.ShipKind.WarShip && a.Count == 3),
                "the Task 14 seam: a refused source is skipped, the next one sends, the warships are unaffected");
        }

        // Fix round 1: siegeships at a conversion hold stay (their bombard is its conversion pace). C gets an inhabitant of player 1
        // (at war): my 2 siegeships there dominate it (no rival ship, no bunker), so C is a ConversionHold and not a source. A is
        // not a hold (only my inhabitants), so its 2 siegeships go (cap 3, need 130 > 40). Control: the same fixture without the
        // rival inhabitant has no conversion holds and gives C 2 + A 1, as above.
        using (var f = BunkeredLine(true))
        {
            f.Ships("B", 0, 5); f.Siege("C", 0, 2); f.Siege("A", 0, 2);
            f.P("C").Population.Add(new Planet.Inhabitant { Player = 1 });
            var assault = SiegePlanner(f);
            var holds = assault.ConversionHolds();
            ok &= Check(holds.Count == 1 && holds[0] == "C", "precondition: C (dominated, an at-war inhabitant left) is the only conversion hold");
            var actions = PlanAt(f, assault, "D");
            ok &= Check(actions.Count == 2
                        && actions.Exists(a => a.Kind == Ship.ShipKind.WarShip && a.Origin == "B" && a.Count == 3)
                        && actions.Exists(a => a.Kind == Ship.ShipKind.SiegeShip && a.Origin == "A" && a.Count == 2)
                        && !actions.Exists(a => a.Origin == "C"),
                "siegeships at the conversion hold C stay; A's 2 (not a hold) go with the 3 warships");
        }
        using (var f = BunkeredLine(true))
        {
            f.Ships("B", 0, 5); f.Siege("C", 0, 2); f.Siege("A", 0, 2);
            var assault = SiegePlanner(f);
            ok &= Check(assault.ConversionHolds().Count == 0, "control: no conversion holds without a rival inhabitant");
            var actions = PlanAt(f, assault, "D");
            ok &= Check(actions.Exists(a => a.Kind == Ship.ShipKind.SiegeShip && a.Origin == "C" && a.Count == 2)
                        && actions.Exists(a => a.Kind == Ship.ShipKind.SiegeShip && a.Origin == "A" && a.Count == 1),
                "control: with no conversion hold the plan is unchanged (C 2, A 1)");
        }

        // No siegeship exists: the warships leave alone, the same force (no stall).
        using (var f = BunkeredLine(true))
        {
            f.Ships("B", 0, 5);
            var actions = PlanAt(f, SiegePlanner(f), "D");
            ok &= Check(actions.Count == 1 && actions[0].Kind == Ship.ShipKind.WarShip && actions[0].Origin == "B" && actions[0].Count == 3,
                "no siegeship built: the 3 warships depart anyway");
        }

        // No bunkers: siegeships wait where they are.
        using (var f = BunkeredLine(false))
        {
            f.Ships("B", 0, 5); f.Siege("A", 0, 2);
            var actions = PlanAt(f, SiegePlanner(f), "D");
            ok &= Check(actions.Count == 1 && actions[0].Kind == Ship.ShipKind.WarShip,
                "no bunkers at the target: no siegeship is sent");
        }

        // Garrison planner: siegeships are neither sources nor counted as garrison.
        using (var f = BunkeredLine(true))
        {
            f.Constants.garrisonOuter = 2;   // C is outer: garrison 2
            f.Siege("C", 0, 3); f.Siege("A", 0, 4);
            var transport = new ShipTransportPlanner(f.Map, 0, PlayerAI.AIStrategy.AIStrategyConsolidate);
            var home = transport.Plan();
            var c = transport.LastStates.Find(s => s.Planet.PlanetName == "C");
            ok &= Check(home.Count == 0 && c != null && c.Docked == 0 && c.Deficit == 2,
                "siegeships fill no garrison and are no garrison source");
        }

        // The order trio: warships and siegeships leaving the same planet the same turn each carry their own kind's ships.
        // (Task 14: the follower gate is live here. defaultTravelSpeed is 1, so B -> D (cost 200) lands beyond the 20-turn projection
        // and both fleets are projected landing on round 20 together: one round, warship 1 takes 48 x 0.8 = 38.4, loss 0.17: they go.)
        using (var f = BunkeredLine(true))
        {
            f.Ships("B", 0, 5);
            for (var i = 0; i < 2; i++) f.P("B").DockShipFromSave(Ship.ShipKind.SiegeShip, 0, new List<string> { "sg" });
            using var t = new ThreatAi(f, PlayerAI.AIStrategy.AIStrategyConsolidate);
            var orders = new List<GameAI.GameAIOrder>();
            t.Ai.ProcessShipActions(orders);
            var transports = orders.FindAll(o => o.Type == GameAI.GameAIOrder.OrderType.OrderTypeShipTransport && o.Origin == "B");
            var siege = transports.Find(o => o.Fleet != null && o.Fleet.Kind == Ship.ShipKind.SiegeShip);
            var war = transports.Find(o => o.Fleet != null && o.Fleet.Kind == Ship.ShipKind.WarShip);
            ok &= Check(war != null && war.Fleet.Snapshots.Count == 3, "B's warship fleet of 3 is emitted");
            ok &= Check(siege != null && siege.Fleet.Snapshots.Count == 2 && siege.Fleet.Snapshots.All(s => s.Count == 1 && s[0] == "sg"),
                "B's siegeship fleet of 2 is emitted with the siegeships' own snapshots (claims are per kind)");
        }
        return ok;
    }

    // Task 14 fixture: line A-B-C-D (path costs C-D 100, B-D 200), defaultTravelSpeed 100 so a fleet from C lands in round 1 and
    // one from B in round 2; player 0 colonizes A, B and C, player 1 colonizes D and holds 2 bunkers there (12 / 150 / 8), at war,
    // D known. Garrisons 0. Bombard needed at D: 2 x 8 x 1.3 = 20.8, so one siegeship (20) is not enough and two are.
    private static Fixture FollowerLine()
    {
        var f = Fixture.Line();
        f.Constants.defaultTravelSpeed = 100f;
        f.Constants.garrisonOuter = 0;
        f.Colonize("A", 0); f.Colonize("B", 0); f.Colonize("C", 0); f.Colonize("D", 1);
        f.Bunkers("D", 1, 2);
        f.War(0, 1);
        f.Map.Knowledge.Update(f.Map, 2);
        return f;
    }

    private static GameAI.GameAIOrder Fleet(Ship.ShipKind kind, string origin, string target, int player, int ships, int delay)
        => new GameAI.GameAIOrder
        {
            Type = GameAI.GameAIOrder.OrderType.OrderTypeShipTransport, PlayerId = player, Origin = origin, Target = target,
            TimingDelay = delay, TotalDelay = delay, Data = ships,
            Fleet = new GameAI.GameAIOrder.ShipFleetPayload
            {
                Kind = kind,
                Snapshots = Enumerable.Range(0, ships).Select(_ => new List<string>()).ToList(),
                Damage = Enumerable.Repeat(0f, ships).ToList(),
            },
        };

    private static List<ShipAction> SiegeActions(List<ShipAction> actions) => actions.FindAll(a => a.Kind == Ship.ShipKind.SiegeShip);

    // Task 14: a later siegeship leaves for the sticky assault target only when the fight projection of its arrival (my ships
    // docked there, in flight there, leaving this turn, and the group itself, against bunker fire) shows it not wiped and losing
    // less than followerMaxLossFraction (0.5). Templates: warship 10 / 100 / 5 bombard 1, siegeship 3 / 100 / 3 bombard 20,
    // bunker 12 / 150 / 8, K 20. Strengths: warship 10 x 105 = 1050, siegeship 3 x 103 = 309, bunker 12 x 158 = 1896. The
    // projections were traced turn by turn (bunker fire 24 x K / (K + Defense) on the weakest-class ship, bombard x 20 / 28 on the
    // most damaged bunker) with an independent re-implementation of FightProjector's loop; the losses below come from that trace.
    public static bool RunFollowerGateCheck()
    {
        var ok = true;
        var consolidate = PlayerAI.AIStrategy.AIStrategyConsolidate;
        using (var f = Fixture.Line())
            ok &= Check(Near(f.Constants.followerMaxLossFraction, 0.5f), "followerMaxLossFraction defaults to 0.5");

        // The first force is gone and the bunkers stand: my only warships garrison C (outer, garrison 2, so none is spare) and
        // nothing of mine is at D or on the way. One siegeship from C lands in round 1 alone: the bunkers' 24 a turn (x 20 / 23)
        // destroy it by turn 6 while its bombard (20, falling with its health) only wounds the first bunker. It stays.
        using (var f = FollowerLine())
        {
            f.Constants.garrisonOuter = 2;
            f.Ships("C", 0, 2); f.Siege("C", 0, 1);
            using var t = new ThreatAi(f, consolidate);
            var d = f.P("D");
            var p = t.Ai.ProjectArrival("C", d, 1);
            ok &= Check(p != null && p.MyShips == 0 && p.InboundShips == 1 && Near(p.MyStrength, 309f) && Near(p.RivalStrength, 3792f),
                "first force gone: the arriving group is the lone siegeship (strength 309) against 2 bunkers (2 x 1896 = 3792)");
            ok &= Check(p != null && p.MyGroupWiped && Near(p.ProjectedLossFraction, 1f) && p.RivalSurvives && p.Turns == 6,
                "the bunkers destroy it on the sixth projected turn and stand: a loss of 1");
            ok &= Check(!t.Ai.ArrivalNotOutmatched("C", d, 1), "the gate refuses: the follower stays");

            var actions = t.Ai.PlanShipActions(0);
            ok &= Check(SiegeActions(actions).Count == 0 && !actions.Exists(a => a.Target == "D"),
                "PlanShipActions: no siegeship (and no warship: C keeps its garrison) leaves for D");
            var held = t.Ai.LastSiegeFollowerChanges;
            ok &= Check(held.Count == 1 && held[0].Kind == SiegeFollowerTracker.KindHeld && held[0].Origin == "C" && held[0].Target == "D"
                        && held[0].Reason == SiegeFollowerTracker.ReasonWiped && held[0].Ships == 1,
                "SiegeHeld|C|D|Wiped is reported");
            t.Ai.PlanShipActions(1);
            ok &= Check(t.Ai.LastSiegeFollowerChanges.Count == 0, "the same hold on the next turn is not reported again (on change only)");
        }

        // A friendly warship force holds D (4 warships docked, the bunkers fire at them, warships first). Two siegeships from C land
        // in round 1 and break both bunkers by turn 10; the first warship ends at health 97.65 when they fall, the rest unhurt:
        // strength 4 x 1050 + 2 x 309 = 4818, left 3720.44, loss 0.2278 < 0.5. They leave.
        using (var f = FollowerLine())
        {
            f.Ships("D", 0, 4); f.Siege("C", 0, 2);
            using var t = new ThreatAi(f, consolidate);
            var d = f.P("D");
            var p = t.Ai.ProjectArrival("C", d, 2);
            ok &= Check(p != null && p.MyShips == 4 && p.InboundShips == 2 && Near(p.MyStrength, 4818f),
                "warships hold D: the arriving group is 4 docked warships + 2 siegeships, strength 4818");
            ok &= Check(p != null && !p.MyGroupWiped && !p.RivalSurvives && p.Turns == 10 && Mathf.Abs(p.ProjectedLossFraction - 0.2278f) < 0.001f,
                "both bunkers fall on turn 10 at a loss of 0.2278");
            ok &= Check(t.Ai.ArrivalNotOutmatched("C", d, 2), "the gate lets them go");

            var actions = t.Ai.PlanShipActions(0);
            ok &= Check(SiegeActions(actions).Count == 1 && actions.Exists(a => a.Kind == Ship.ShipKind.SiegeShip && a.Origin == "C"
                                                                                && a.Target == "D" && a.Count == 2)
                        && !actions.Exists(a => a.Kind == Ship.ShipKind.WarShip && a.Target == "D"),
                "PlanShipActions: C's 2 siegeships follow to D (no warship needed: 4 docked >= the force of 3)");
            var follow = t.Ai.LastSiegeFollowerChanges;
            ok &= Check(follow.Count == 1 && follow[0].Kind == SiegeFollowerTracker.KindFollow && follow[0].Origin == "C" && follow[0].Ships == 2
                        && Mathf.Abs(follow[0].LossFraction - 0.2278f) < 0.001f,
                "SiegeFollow|C->D|2|23 is reported");

            // Side-effect free and deterministic: the same answer twice, nothing on the map or in the in-flight list changed.
            var inFlight = new List<GameAI.GameAIOrder> { Fleet(Ship.ShipKind.WarShip, "B", "C", 0, 1, 1) };
            t.Ai.InFlightOrders = inFlight;
            string State() => string.Join(";", f.Map.PlanetList.Select(pl => pl.PlanetName + ":" + string.Join(",",
                pl.DockedShips.Select(s => $"{s.Kind}/{s.Owner}/{s.Damage}")) + "/in" + pl.GetIncomingShips(Ship.ShipKind.SiegeShip, 0)
                + "/" + pl.GetIncomingShips(Ship.ShipKind.WarShip, 0)));
            var before = State();
            var first = t.Ai.ProjectArrival("C", d, 2);
            var second = t.Ai.ProjectArrival("C", d, 2);
            ok &= Check(t.Ai.ArrivalNotOutmatched("C", d, 2) == t.Ai.ArrivalNotOutmatched("C", d, 2)
                        && first.MyStrengthLeft == second.MyStrengthLeft && first.Turns == second.Turns,
                "the gate is deterministic: the same projection and answer twice");
            ok &= Check(State() == before && inFlight.Count == 1 && inFlight[0].Target == "C",
                "the gate leaves no state: docked ships, damage, incoming counters and the in-flight list are unchanged");
        }

        // A friendly force too small: 3 warships and the 1 siegeship C has. The bunkers outlast it long enough to cost 0.604 of the
        // group (3459 strength): not wiped, but outmatched. It stays, with the reason Outmatched.
        using (var f = FollowerLine())
        {
            f.Ships("D", 0, 3); f.Siege("C", 0, 1);
            using var t = new ThreatAi(f, consolidate);
            var p = t.Ai.ProjectArrival("C", f.P("D"), 1);
            ok &= Check(p != null && !p.MyGroupWiped && Near(p.MyStrength, 3459f) && Mathf.Abs(p.ProjectedLossFraction - 0.6041f) < 0.001f,
                "3 warships + 1 siegeship: not wiped, but a loss of 0.604");
            t.Ai.PlanShipActions(0);
            var held = t.Ai.LastSiegeFollowerChanges;
            ok &= Check(held.Count == 1 && held[0].Kind == SiegeFollowerTracker.KindHeld && held[0].Reason == SiegeFollowerTracker.ReasonOutmatched,
                "SiegeHeld|C|D|Outmatched: a loss at or above followerMaxLossFraction holds the follower");
            f.Constants.followerMaxLossFraction = 1f;
            ok &= Check(t.Ai.ArrivalNotOutmatched("C", f.P("D"), 1), "followerMaxLossFraction 1: only a wipe-out holds a follower back");
        }

        // My ships in flight to D count: 4 warships landing in round 1 (InFlightOrders) and 2 siegeships from C give the same fight
        // as the docked force above (same dock order): MyShips 0, 6 inbound, loss 0.2278. Without the in-flight order the 2
        // siegeships alone are destroyed (turn 19). Another player's order and an order to another planet are ignored.
        using (var f = FollowerLine())
        {
            f.Siege("C", 0, 2);
            using var t = new ThreatAi(f, consolidate);
            var d = f.P("D");
            var alone = t.Ai.ProjectArrival("C", d, 2);
            ok &= Check(alone != null && alone.MyGroupWiped && !t.Ai.ArrivalNotOutmatched("C", d, 2),
                "nothing of mine at or bound for D: 2 siegeships alone would be destroyed");
            t.Ai.InFlightOrders = new List<GameAI.GameAIOrder>
            {
                Fleet(Ship.ShipKind.WarShip, "B", "D", 0, 4, 1),
                Fleet(Ship.ShipKind.WarShip, "B", "D", 1, 5, 1),   // not mine
                Fleet(Ship.ShipKind.WarShip, "A", "C", 0, 5, 1),   // not bound for D
            };
            var p = t.Ai.ProjectArrival("C", d, 2);
            ok &= Check(p != null && p.MyShips == 0 && p.InboundShips == 6 && Near(p.MyStrength, 4818f) && !p.MyGroupWiped
                        && Mathf.Abs(p.ProjectedLossFraction - 0.2278f) < 0.001f && t.Ai.ArrivalNotOutmatched("C", d, 2),
                "4 of my warships in flight to D join the projection: loss 0.2278, the siegeships may go");
        }

        // Siegeships from two origins both leave when safe: 5 warships hold D (cap 5), 1 siegeship at C and 1 at B. C's alone:
        // 5 x 1050 + 309 = 5559, loss 0.3656. B's is projected with C's (leaving the same turn, round 1) and its own (round 2):
        // 5868, loss 0.2076. Both pass.
        using (var f = FollowerLine())
        {
            f.Ships("D", 0, 5); f.Siege("C", 0, 1); f.Siege("B", 0, 1);
            using var t = new ThreatAi(f, consolidate);
            var d = f.P("D");
            var fromC = t.Ai.ProjectArrival("C", d, 1);
            var cAction = new ShipAction { Origin = "C", Target = "D", Cost = 100f, Count = 1, Kind = Ship.ShipKind.SiegeShip };
            var fromB = t.Ai.ProjectArrival("B", d, 1, new List<ShipAction> { cAction });
            ok &= Check(fromC != null && Near(fromC.MyStrength, 5559f) && Mathf.Abs(fromC.ProjectedLossFraction - 0.3656f) < 0.001f,
                "C's siegeship with the 5 warships: loss 0.3656");
            ok &= Check(fromB != null && fromB.InboundShips == 2 && Near(fromB.MyStrength, 5868f) && Mathf.Abs(fromB.ProjectedLossFraction - 0.2076f) < 0.001f,
                "B's siegeship projected with C's leaving the same turn: loss 0.2076");

            var actions = t.Ai.PlanShipActions(0);
            var siege = SiegeActions(actions);
            ok &= Check(siege.Count == 2 && siege.Exists(a => a.Origin == "C" && a.Count == 1) && siege.Exists(a => a.Origin == "B" && a.Count == 1),
                "PlanShipActions: the siegeships at C and at B both leave for D");
            var changes = t.Ai.LastSiegeFollowerChanges;
            ok &= Check(changes.Count == 2 && changes.All(c => c.Kind == SiegeFollowerTracker.KindFollow)
                        && changes[0].Origin == "B" && changes[1].Origin == "C",
                "two SiegeFollow lines, one per origin");
        }

        // No sticky target: no warship of mine anywhere, so the assault has no target and no siegeship leaves, however safe.
        using (var f = FollowerLine())
        {
            f.Siege("C", 0, 2); f.Siege("B", 0, 2);
            using var t = new ThreatAi(f, consolidate);
            ok &= Check(new AssaultPlanner(f.Map, 0, stats: f.Stats, warRivals: new HashSet<int> { 1 }).ChooseTarget() == null,
                "precondition: no warship of mine, no assault target");
            var actions = t.Ai.PlanShipActions(0);
            ok &= Check(actions.Count == 0 && t.Ai.LastSiegeFollowerChanges.Count == 0,
                "no follower leaves (and none is gated or logged) before a sticky target exists");
        }

        // The tracker: reported on first sight and on a change of kind or reason, cleared when the route leaves the list.
        {
            var tracker = new SiegeFollowerTracker();
            SiegeFollowerTracker.Entry E(string kind, string reason) =>
                new SiegeFollowerTracker.Entry { Origin = "C", Target = "D", Kind = kind, Reason = reason, Ships = 1 };
            var held = E(SiegeFollowerTracker.KindHeld, SiegeFollowerTracker.ReasonWiped);
            ok &= Check(tracker.Update(new[] { held }).Count == 1 && tracker.Update(new[] { held }).Count == 0,
                "tracker: a hold is reported once");
            ok &= Check(tracker.Update(new[] { E(SiegeFollowerTracker.KindHeld, SiegeFollowerTracker.ReasonOutmatched) }).Count == 1,
                "tracker: a new reason is reported");
            ok &= Check(tracker.Update(new[] { E(SiegeFollowerTracker.KindFollow, string.Empty) }).Count == 1,
                "tracker: the follower leaving is reported");
            ok &= Check(tracker.Update(new SiegeFollowerTracker.Entry[0]).Count == 0 && tracker.Update(new[] { held }).Count == 1,
                "tracker: a route that left the list is reported again when it returns");
        }
        return ok;
    }

    // A colonist order flying A>B>C, launched with TotalDelay 2: at TimingDelay 1 its progress is 0.5, so it passes B (fraction 0.5).
    private static GameAI.GameAIOrder EscortedColonist(float escortOffense)
        => new GameAI.GameAIOrder
        {
            Type = GameAI.GameAIOrder.OrderType.OrderTypePopulationTransport,
            TimingType = GameAI.GameAIOrder.OrderTimingType.OrderTimingTypeDelayed,
            TimingDelay = 1, TotalDelay = 2, Data = 1, Origin = "A", Target = "C", PlayerId = 0,
            Route = new List<string> { "A", "B", "C" },
            EscortOffense = escortOffense,
        };

    // Task 15: escorted colonists (escort option B). The colonist order carries the escort's offense, which BlockadeSystem subtracts
    // from the blockade value at each node (passed when Value - EscortOffense <= 0); a dominated target gets an escort of at least one
    // spare warship, flown as the ordinary ship-transport trio; EscortOffense is saved (an older order loads as 0). The planning
    // against a known blockade on the way (escort versus detour) is in BlockadeAvoidanceSelfCheck.RunEscortPlanningCheck.
    public static bool RunEscortCheck()
    {
        var ok = true;

        // Simulation. Player 1 holds B with one warship: offense 10, so the blockade against player 0 at B is 10.
        using (var f = Fixture.Line())
        {
            f.Ships("B", 1, 1);
            var blockade = new BlockadeSystem(f.Map, f.Research);
            ok &= Check(Near(blockade.Value(f.P("B"), 0, out _), 10f), "precondition: the blockade at B is 10");

            // An escort of 11 (= 10 x 1.1, what the planner asks for): the colonist passes B.
            var escorted = EscortedColonist(11f);
            var orders = new List<GameAI.GameAIOrder> { escorted };
            var cuts = blockade.Apply(orders, 1);
            ok &= Check(orders.Count == 1 && cuts.Count == 0, "escort 11 against a blockade of 10: 10 - 11 <= 0, the colonist passes B uncut");

            // The same colonist without an escort is cut at B, and one short of the blockade (9.5) is cut too.
            var plain = EscortedColonist(0f);
            orders = new List<GameAI.GameAIOrder> { plain };
            cuts = blockade.Apply(orders, 1);
            ok &= Check(orders.Count == 0 && cuts.Count == 1 && cuts[0].Planet == "B" && Near(cuts[0].Value, 10f),
                "no escort: the colonist is cut at B (value 10)");
            orders = new List<GameAI.GameAIOrder> { EscortedColonist(9.5f) };
            ok &= Check(blockade.Apply(orders, 1).Count == 1 && orders.Count == 0, "escort 9.5 against 10: 10 - 9.5 > 0, cut");
        }

        // A dominated target launches an escort. Line A-B-C-D: player 0 colonizes C (my only colonizer, food 100), player 1 has one
        // inhabitant on D, at war; my one warship docked at D (bombard 1 > 0, no at-war defender ship) makes me D's dominator. A and B
        // carry an inbound-colonist flag, so D is the only target. Garrisons 0: both warships at C are spare. No blockade anywhere, so
        // the needed offense is 0 and the escort is the minimum of one ship: offense 10. Route C>D (cost 100, travel speed 1: delay 100).
        using (var f = Fixture.Line())
        {
            f.Constants.garrisonOuter = 0;
            f.Colonize("C", 0);
            f.P("C").Food = 100f;
            f.Colonize("D", 1);
            f.Ships("C", 0, 2);
            f.Ships("D", 0, 1);
            f.War(0, 1);
            f.P("A").SetPopulationTransferInProgress(0);
            f.P("B").SetPopulationTransferInProgress(0);
            f.Map.Knowledge.Update(f.Map, 2, 8);
            using var t = new ThreatAi(f, PlayerAI.AIStrategy.AIStrategyExpand);
            t.Ai.RefreshBlockadeView(1);
            var d = f.P("D");
            ok &= Check(t.Ai.IsConversionTarget(d) && t.Ai.IsValidColonizationTarget(d), "precondition: D is a dominated, valid colonization target");

            var shortest = RoutePlanner.ShortestRoute(f.Map, "C", "D", 6);
            ok &= Check(shortest != null && string.Join(">", shortest.Nodes) == "C>D" && Near(shortest.Cost, 100f) && !shortest.IsDetour,
                "ShortestRoute C>D, cost 100");
            var plan = t.Ai.PlanEscort(f.P("C"), d, shortest, f.Stats);
            ok &= Check(plan.HasValue && plan.Value.ships == 1 && Near(plan.Value.offense, 10f),
                "PlanEscort for a dominated target with no blockade: 1 ship, offense 10");

            var results = new List<Planet.UpdateResult>
            {
                new Planet.UpdateResult("C", Planet.UpdateResult.UpdateResultType.UpdateResultTypeColonizerReady, 1, 0),
            };
            var orders = new List<GameAI.GameAIOrder>();
            t.Ai.ProcessColonizers(results, orders);
            var colonist = orders.Find(o => o.Type == GameAI.GameAIOrder.OrderType.OrderTypePopulationTransport);
            var transport = orders.Find(o => o.Type == GameAI.GameAIOrder.OrderType.OrderTypeShipTransport);
            var departure = orders.Find(o => o.Type == GameAI.GameAIOrder.OrderType.OrderTypeShipDeparture);
            var inProgress = orders.Find(o => o.Type == GameAI.GameAIOrder.OrderType.OrderTypeShipTransferInProgress);
            ok &= Check(colonist != null && colonist.Target == "D" && string.Join(">", colonist.Route) == "C>D" && Near(colonist.EscortOffense, 10f),
                "the colonist to D carries route C>D and EscortOffense 10");
            ok &= Check(transport != null && transport.Origin == "C" && transport.Target == "D" && System.Convert.ToInt32(transport.Data) == 1
                        && transport.Fleet != null && transport.Fleet.Kind == Ship.ShipKind.WarShip && transport.Fleet.Snapshots.Count == 1
                        && transport.TimingDelay == 100 && colonist != null && colonist.TimingDelay == 100,
                "the escort is one warship C->D on the ordinary transport order, launched with the colonist (both delay 100)");
            ok &= Check(departure != null && departure.Origin == "C" && System.Convert.ToInt32(departure.Data) == 1
                        && inProgress != null && inProgress.Target == "D",
                "with its departure (C, 1 ship) and in-progress (D) orders: the EmitShipOrders trio");
            ok &= Check(t.Ai.EscortClaimed("C") == 1, "the escort's ship is claimed at C for this turn's ship planners");

            // No spare warship: the dominated target is still colonized, unescorted.
            f.Constants.garrisonOuter = 2;   // C (outer) keeps both its ships
            orders = new List<GameAI.GameAIOrder>();
            t.Ai.ProcessColonizers(results, orders);
            var unescorted = orders.Find(o => o.Type == GameAI.GameAIOrder.OrderType.OrderTypePopulationTransport);
            ok &= Check(unescorted != null && unescorted.Target == "D" && Near(unescorted.EscortOffense, 0f)
                        && !orders.Exists(o => o.Type == GameAI.GameAIOrder.OrderType.OrderTypeShipTransport)
                        && t.Ai.EscortClaimed("C") == 0,
                "no spare warship at C: the colonist still goes to D, without an escort");
        }

        // Saves: EscortOffense round-trips through OrderSave (writer and loader copy the field one to one); an older order has no key
        // and loads as 0.
        var save = new SaveLoadSystem.GameSave.OrderSave
        {
            type = GameAI.GameAIOrder.OrderType.OrderTypePopulationTransport, origin = "C", target = "D", playerId = 0, data = 1f,
            dataType = "int", route = new List<string> { "C", "D" }, escortOffense = 22.5f,
        };
        var back = JsonUtility.FromJson<SaveLoadSystem.GameSave.OrderSave>(JsonUtility.ToJson(save));
        ok &= Check(Near(back.escortOffense, 22.5f), "save: escortOffense 22.5 survives a JsonUtility round trip");
        var older = JsonUtility.FromJson<SaveLoadSystem.GameSave.OrderSave>(
            "{\"type\":1,\"origin\":\"C\",\"target\":\"D\",\"playerId\":0,\"data\":1.0,\"dataType\":\"int\"}");
        ok &= Check(Near(older.escortOffense, 0f), "save: an older order JSON without escortOffense loads as 0");
        ok &= Check(Near(JsonUtility.FromJson<GameAI.GameAIOrder>(JsonUtility.ToJson(EscortedColonist(7f))).EscortOffense, 7f),
            "the order's EscortOffense field itself is serialized");

        // The tracker: a declined escort is reported on first sight and on a change of reason, and again after it left the list.
        {
            var tracker = new EscortDetourTracker();
            EscortDetourTracker.Entry E(string reason) => new EscortDetourTracker.Entry { Origin = "A", Target = "D", Reason = reason };
            ok &= Check(tracker.Update(new[] { E(EscortDetourTracker.ReasonNoSpare) }).Count == 1
                        && tracker.Update(new[] { E(EscortDetourTracker.ReasonNoSpare) }).Count == 0,
                "tracker: a declined escort is reported once");
            ok &= Check(tracker.Update(new[] { E(EscortDetourTracker.ReasonShortOffense) }).Count == 1, "tracker: a new reason is reported");
            ok &= Check(tracker.Update(new EscortDetourTracker.Entry[0]).Count == 0
                        && tracker.Update(new[] { E(EscortDetourTracker.ReasonShortOffense) }).Count == 1,
                "tracker: a route that left the list is reported again when it returns");
        }
        return ok;
    }

    private static List<GameAI.GameAIOrder> Transports(List<GameAI.GameAIOrder> orders, string origin, Ship.ShipKind kind)
        => orders.FindAll(o => o.Type == GameAI.GameAIOrder.OrderType.OrderTypeShipTransport && o.Origin == origin
                               && o.Fleet != null && o.Fleet.Kind == kind);

    private static int Departing(List<GameAI.GameAIOrder> orders, string origin, Ship.ShipKind kind)
        => orders.Where(o => o.Type == GameAI.GameAIOrder.OrderType.OrderTypeShipDeparture && o.Origin == origin
                             && o.Fleet != null && o.Fleet.Kind == kind)
            .Sum(o => System.Convert.ToInt32(o.Data));

    private static List<Planet.UpdateResult> ColonizerReady(string planet) => new List<Planet.UpdateResult>
    {
        new Planet.UpdateResult(planet, Planet.UpdateResult.UpdateResultType.UpdateResultTypeColonizerReady, 1, 0),
    };

    // Task 15 fix round 1: the escort's reservation is visible to the ship planners BEFORE they plan (ShipTransportPlanner.Reserved,
    // AssaultPlanner.PreDeparting), so the assault force, the siegeship gate's projection and the emitted actions agree; a retreat has
    // priority (no escort from an origin with a fight projection of mine); with no escort everything is as before.
    public static bool RunEscortReservationCheck()
    {
        var ok = true;
        var consolidate = PlayerAI.AIStrategy.AIStrategyConsolidate;

        // (a) and (d): blockade force from an escort origin. Line A-B-C-D, Consolidate, garrisons 0. Player 0 colonizes A and B (B the
        // ready colonizer, food 100); player 1's one warship at C (offense 10) blockades it (no war needed); C carries an inbound flag
        // so D (empty, two hops) is the only target. B holds 4 warships in dock order: 2 at full health (offense 10), 2 with damage 50
        // (effective offense 10 x 50 / 100 = 5). B is outer: garrison 0, all 4 spare.
        Fixture BlockadeLine()
        {
            var f = Fixture.Line();
            f.Constants.garrisonOuter = 0;
            f.Colonize("A", 0); f.Colonize("B", 0);
            f.P("B").Food = 100f;
            f.Ships("C", 1, 1);
            f.P("C").SetPopulationTransferInProgress(0);
            f.Ships("B", 0, 2); f.Ships("B", 0, 2, 50f);
            f.Map.Knowledge.Update(f.Map, 2, 8);
            return f;
        }

        // (a) The colonist B>C>D (no detour on a line) needs 10 x 1.1 = 11: the escort takes B's first 2 ships (10 + 10 = 20). The
        // blockade force for C (needed 11) is then planned from B's remaining spare, 2, skipping the escort's ships: the 2 damaged ones,
        // 5 + 5 = 10 (still 1 short, every spare ship sent). Planned = emitted: B->C 2 ships, damage 50 and 50; 4 ships leave B in all.
        using (var f = BlockadeLine())
        {
            using var t = new ThreatAi(f, consolidate);
            t.Ai.RefreshBlockadeView(1);
            var orders = new List<GameAI.GameAIOrder>();
            t.Ai.ProcessColonizers(ColonizerReady("B"), orders);
            var colonist = orders.Find(o => o.Type == GameAI.GameAIOrder.OrderType.OrderTypePopulationTransport);
            ok &= Check(colonist != null && colonist.Target == "D" && Near(colonist.EscortOffense, 20f) && t.Ai.EscortClaimed("B") == 2,
                "reservation: the colonist B->D leaves with a 2-ship escort (offense 20)");
            var escort = Transports(orders, "B", Ship.ShipKind.WarShip);
            ok &= Check(escort.Count == 1 && escort[0].Target == "D" && escort[0].Fleet.Damage.All(d => Near(d, 0f)),
                "the escort is B's first 2 ships (full health)");

            var planned = t.Ai.PlanShipActions(0, new List<Planet.UpdateResult>());
            var plannedToC = planned.Where(a => a.Origin == "B" && a.Target == "C" && a.Kind == Ship.ShipKind.WarShip).ToList();
            ok &= Check(plannedToC.Count == 1 && plannedToC[0].Count == 2,
                "the blockade force is planned from B's spare after the escort: 2 ships (4 docked - 2 reserved)");

            t.Ai.ProcessShipActions(orders, new List<Planet.UpdateResult>());
            var fleets = Transports(orders, "B", Ship.ShipKind.WarShip);
            var toC = fleets.Find(o => o.Target == "C");
            ok &= Check(toC != null && System.Convert.ToInt32(toC.Data) == 2 && toC.Fleet.Snapshots.Count == 2
                        && toC.Fleet.Damage.Count == 2 && toC.Fleet.Damage.All(d => Near(d, 50f)),
                "emitted = planned: B->C 2 ships, the 2 damaged ones the planner sized (5 + 5 = 10 offense), not trimmed");
            ok &= Check(fleets.Count == 2 && Departing(orders, "B", Ship.ShipKind.WarShip) == 4,
                "the escort (2) and the force (2) leave B: 4 departures for 4 docked ships, none sent twice");
            ok &= Check(t.Ai.EscortClaimed("B") == 0, "ProcessShipActions consumed the escort reservation");
        }

        // (d) The same board with no colonizer ready: no escort, so B's 4 spare ships feed the force, first in dock order: the 2 full
        // ships (10 + 10 = 20 >= 11) go, as before the escort existed (Reserved and PreDeparting empty change nothing).
        using (var f = BlockadeLine())
        {
            using var t = new ThreatAi(f, consolidate);
            t.Ai.RefreshBlockadeView(1);
            var orders = new List<GameAI.GameAIOrder>();
            t.Ai.ProcessColonizers(new List<Planet.UpdateResult>(), orders);
            ok &= Check(orders.Count == 0 && t.Ai.EscortClaimed("B") == 0, "no colonizer: no escort, nothing reserved");
            t.Ai.ProcessShipActions(orders, new List<Planet.UpdateResult>());
            var fleets = Transports(orders, "B", Ship.ShipKind.WarShip);
            ok &= Check(fleets.Count == 1 && fleets[0].Target == "C" && System.Convert.ToInt32(fleets[0].Data) == 2
                        && fleets[0].Fleet.Damage.All(d => Near(d, 0f)),
                "no escort: the force is B's first 2 ships at full health (20 >= 11), unchanged behaviour");
        }

        // (b) The siegeship gate sees the escort. FollowerLine (A, B, C mine; D player 1's with 2 bunkers, at war; garrisons 0) plus my
        // 4 warships docked at D (they dominate it: bunkers do not block dominance) and, at C (the ready colonizer, food 100), 2
        // siegeships and 1 warship. D is a dominated target with a clean route C>D (no warship blockade): the escort is the minimum
        // one ship, C's only (spare) warship, offense 10. The assault target is D (sticky: my ships there; 4 docked >= the force of 3,
        // so no warship goes), and C's 2 siegeships ask the gate. The gate's departing list now starts with the escort, which lands
        // with them (cost 100, speed 100: round 1): MyShips 4, InboundShips 3, strength 4 x 1050 + 1050 + 2 x 309 = 5868 (without the
        // escort: InboundShips 2, 4818, loss 0.2278, see RunFollowerGateCheck).
        using (var f = FollowerLine())
        {
            f.Ships("D", 0, 4); f.Siege("C", 0, 2); f.Ships("C", 0, 1);
            f.P("C").Food = 100f;
            using var t = new ThreatAi(f, consolidate);
            t.Ai.RefreshBlockadeView(1);
            var d = f.P("D");
            ok &= Check(t.Ai.IsConversionTarget(d), "precondition: D is dominated (4 warships with bombard, only bunkers against them)");
            var orders = new List<GameAI.GameAIOrder>();
            t.Ai.ProcessColonizers(ColonizerReady("C"), orders);
            var colonist = orders.Find(o => o.Type == GameAI.GameAIOrder.OrderType.OrderTypePopulationTransport);
            ok &= Check(colonist != null && colonist.Target == "D" && Near(colonist.EscortOffense, 10f) && t.Ai.EscortClaimed("C") == 1,
                "the colonist C->D leaves with C's one warship as escort (offense 10)");

            var escortAction = new ShipAction { Origin = "C", Target = "D", Cost = 100f, Count = 1, Kind = Ship.ShipKind.WarShip };
            var withEscort = t.Ai.ProjectArrival("C", d, 2, new List<ShipAction> { escortAction });
            var withoutEscort = t.Ai.ProjectArrival("C", d, 2);
            ok &= Check(withEscort != null && withEscort.MyShips == 4 && withEscort.InboundShips == 3 && Near(withEscort.MyStrength, 5868f),
                "projection with the escort departing: 4 docked + 3 inbound (the escort and 2 siegeships), strength 5868");
            ok &= Check(withoutEscort != null && withoutEscort.InboundShips == 2 && Near(withoutEscort.MyStrength, 4818f)
                        && Mathf.Abs(withEscort.PerceivedLossFraction - withoutEscort.PerceivedLossFraction) > 0.001f,
                "without it: 2 inbound, strength 4818, a different loss");

            t.Ai.ProcessShipActions(orders, new List<Planet.UpdateResult>());
            var follow = t.Ai.LastSiegeFollowerChanges;
            ok &= Check(follow.Count == 1 && follow[0].Kind == SiegeFollowerTracker.KindFollow && follow[0].Origin == "C" && follow[0].Ships == 2
                        && Mathf.Abs(follow[0].LossFraction - withEscort.PerceivedLossFraction) < 0.0001f,
                "the gate's call (SiegeFollow) used the projection that includes the escort: same loss as withEscort");
            ok &= Check(Transports(orders, "C", Ship.ShipKind.SiegeShip).Exists(o => System.Convert.ToInt32(o.Data) == 2 && o.Target == "D")
                        && Transports(orders, "C", Ship.ShipKind.WarShip).Count == 1
                        && Departing(orders, "C", Ship.ShipKind.WarShip) == 1,
                "emitted: the 2 siegeships and only the escort's warship leave C (1 warship docked, 1 departure)");
        }

        // (c) A retreat has priority. Line A-B-C-D, Expand (so C is no contested hold and its 2 ships are spare: garrison 0), at war;
        // player 0 colonizes B and C (C the ready colonizer, food 100); player 1 has an inhabitant on D, where my one warship dominates
        // it (a dominated target, so C escorts with one ship). At C my 2 warships share the planet with player 1's 1 (10 - 20 < 0: not
        // blockaded). Control: with no fight result the escort leaves (1 ship, offense 10). With a hand-made projection saying my group
        // is wiped (a retreat at my own colony needs a wipe; steepness 0.001 makes the roll certain) the escort is refused at C (its
        // spare counts as 0), the colonist goes unescorted, and the retreat takes both ships C->B (tier 1, my colony), whole.
        using (var f = Fixture.Line())
        {
            f.Constants.garrisonOuter = 0;
            f.Constants.retreatSteepness = 0.001f;
            f.Colonize("B", 0); f.Colonize("C", 0); f.Colonize("D", 1);
            f.P("C").Food = 100f;
            f.P("A").SetPopulationTransferInProgress(0);
            f.Ships("C", 0, 2); f.Ships("C", 1, 1); f.Ships("D", 0, 1);
            f.War(0, 1);
            f.Map.Knowledge.Update(f.Map, 2, 8);
            using var t = new ThreatAi(f, PlayerAI.AIStrategy.AIStrategyExpand);
            t.Ai.RefreshBlockadeView(1);
            ok &= Check(t.Ai.IsConversionTarget(f.P("D")) && !t.Ai.CurrentBlockadeView.IsBlockaded("C"),
                "precondition: D is a dominated target and C is not blockaded");
            var control = new List<GameAI.GameAIOrder>();
            t.Ai.ProcessColonizers(ColonizerReady("C"), control);
            var controlColonist = control.Find(o => o.Type == GameAI.GameAIOrder.OrderType.OrderTypePopulationTransport);
            ok &= Check(controlColonist != null && Near(controlColonist.EscortOffense, 10f) && t.Ai.EscortClaimed("C") == 1,
                "control: with no fight at C the colonist leaves with a one-ship escort (offense 10)");

            var fight = new FightProjection
            {
                Planet = "C", Player = 0, MyShips = 2, MyStrength = 2100f, MyStrengthLeft = 0f,
                RivalStrength = 3000f, RivalStrengthLeft = 2500f, RivalSurvives = true, Turns = 5, Rivals = new List<int> { 1 },
            };
            var results = ColonizerReady("C");
            results.Add(new Planet.UpdateResult("C", Planet.UpdateResult.UpdateResultType.UpdateResultTypeFightProjection, fight, 0));

            var orders = new List<GameAI.GameAIOrder>();
            t.Ai.ProcessColonizers(results, orders);
            var colonist = orders.Find(o => o.Type == GameAI.GameAIOrder.OrderType.OrderTypePopulationTransport);
            ok &= Check(colonist != null && colonist.Target == "D" && Near(colonist.EscortOffense, 0f) && t.Ai.EscortClaimed("C") == 0
                        && Transports(orders, "C", Ship.ShipKind.WarShip).Count == 0,
                "a fight at C: no escort leaves C, the colonist to D goes unescorted");

            GameAI.Rand = new System.Random(11);
            t.Ai.ProcessShipActions(orders, results);
            var fleets = Transports(orders, "C", Ship.ShipKind.WarShip);
            ok &= Check(fleets.Count == 1 && fleets[0].Target == "B" && System.Convert.ToInt32(fleets[0].Data) == 2
                        && Departing(orders, "C", Ship.ShipKind.WarShip) == 2,
                "the retreat takes both ships C->B, untrimmed; 2 departures for 2 docked ships, none also escorted");
        }

        // The decline reason for a blockaded route from an origin in a fight is Fight (log-only tracker reason).
        ok &= Check(EscortDetourTracker.ReasonFight == "Fight", "EscortDetour reason Fight exists");
        return ok;
    }

    // Bunker cap (MaxPopulation), survival over the cap, death cleanup and the kind-to-subtype mapping that
    // BuildResearchSnapshot uses. Planet.DockNewShip / StageCompletedProductionItem need Gameboard.Instance, so docking goes
    // through DockShipFromSave. The death path is tested through the public ChangePopulation(int, int), which calls ClearBunkers
    // once the last inhabitant is gone (the economy's starvation loss ends there too), and through ClearBunkers directly.
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

        // A mixed planet: a player whose last inhabitant leaves loses its own bunkers only. C holds A (player 0) x2 and B (player 1) x1,
        // bunkers 0:2 and 1:1.
        using (var f = Fixture.Line())
        {
            var c = f.P("C");
            ConversionSelfCheck.Populate(c, (0, 2), (1, 1));
            f.Bunkers("C", 0, 2);
            f.Bunkers("C", 1, 1);
            c.ChangePopulation(-1, 0);
            ok &= Check(c.Population.Count == 2 && c.BunkerCount(0) == 2 && c.BunkerCount(1) == 1,
                "removing one of two inhabitants keeps that player's bunkers");
            c.ChangePopulation(-1, 0);
            ok &= Check(c.Population.Count == 1 && c.Owner == 1 && c.BunkerCount(0) == 0 && c.BunkerCount(1) == 1,
                "removing a player's last inhabitant destroys its bunkers; the other player's stay");
            c.ChangePopulation(-1, 1);
            ok &= Check(c.Population.Count == 0 && c.BunkerCount(1) == 0 && c.DockedShips.Count == 0, "the planet emptying clears the rest");
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
        // The ratio rule (dominanceRivalRatio 0.5): a warship is 10 x (100 + 5) = 1050. Only at-war WARSHIPS of a player with inhabitants
        // here contest, summed, against more than half my fleet strength.
        using (var f = Fixture.Line())
        {
            f.Constants.dominanceRivalRatio = 0.5f;
            var a = f.P("A");
            ConversionSelfCheck.Populate(a, (0, 2), (1, 2));   // both sides hold inhabitants, so both can contest
            f.Ships("A", 0, 4); f.Ships("A", 1, 1); f.War(0, 1);   // 4200 against 1050 <= 2100
            ok &= Check(ConversionSystem.Dominator(a, f.Map, f.Stats) == 0, "ratio: 1 rival warship against my 4 does not contest");
            f.Ships("A", 1, 2);                                     // 3150 > 2100
            ok &= Check(ConversionSystem.Dominator(a, f.Map, f.Stats) == Planet.NoOwner, "ratio: 3 rival warships against my 4 contest, nobody dominates");
        }
        using (var f = Fixture.Line())   // a negative ratio is the old rule
        {
            f.Constants.dominanceRivalRatio = -1f;
            var a = f.P("A");
            f.Ships("A", 0, 4); f.Ships("A", 1, 1); f.War(0, 1);
            ok &= Check(ConversionSystem.Dominator(a, f.Map, f.Stats) == Planet.NoOwner, "ratio -1 (the old rule): any at-war rival ship contests");
        }
        using (var f = Fixture.Line())   // a rival's siegeships never contest
        {
            f.Constants.dominanceRivalRatio = 0.5f;
            var a = f.P("A");
            ConversionSelfCheck.Populate(a, (0, 2), (1, 2));
            f.Ships("A", 0, 4); f.Siege("A", 1, 5); f.War(0, 1);
            ok &= Check(ConversionSystem.Dominator(a, f.Map, f.Stats) == 0, "ratio: a rival's siegeships do not contest, even five of them");
        }
        using (var f = Fixture.Line())   // a rival with no inhabitants here never contests, however large its fleet
        {
            f.Constants.dominanceRivalRatio = 0.5f;
            var a = f.P("A");
            ConversionSelfCheck.Populate(a, (1, 4));
            f.Ships("A", 0, 6); f.Ships("A", 2, 5); f.War(0, 2);   // player 2 holds nobody on A; its bombard 5 < my 6
            ok &= Check(ConversionSystem.Dominator(a, f.Map, f.Stats) == 0, "ratio: a landless at-war fleet does not contest; more bombard (6 against 5) dominates");
        }
        using (var f = Fixture.Line())   // two rivals with inhabitants add up
        {
            f.Constants.dominanceRivalRatio = 0.5f;
            var a = f.P("A");
            ConversionSelfCheck.Populate(a, (0, 1), (1, 2), (2, 1));
            f.Ships("A", 0, 4); f.Ships("A", 1, 1); f.Ships("A", 2, 2); f.War(0, 1); f.War(0, 2);   // 3150 > 2100
            ok &= Check(ConversionSystem.Dominator(a, f.Map, f.Stats) == Planet.NoOwner, "ratio: two at-war holders' warships are summed (1 + 2 against my 4)");
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
                ok &= Check(r1.All(r => r.What == ConversionReport.Kind.Blocked) && a.ConversionBy == Planet.NoOwner && Near(a.ConversionProgress, 0f) && a.Population.Count(p => p.Player == 1) == 4,
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

        // The carry: a session that loses dominance keeps its progress for conversionCarryTurns; the same player back in time resumes it.
        using (var f = Fixture.Line())
        {
            f.Constants.conversionBombardReference = 60f; f.Constants.conversionTurnsBase = 4f; f.Constants.conversionCarryTurns = 3;
            var a = f.P("A");
            ConversionSelfCheck.Populate(a, (1, 4));
            f.Siege("A", 0, 3); f.War(0, 1);   // 60 bombard: factor 1, 0.25 a turn
            ConvTurn(f, 1); ConvTurn(f, 2);
            ok &= Check(a.ConversionBy == 0 && Near(a.ConversionProgress, 0.5f), "carry: precondition, a session at progress 0.5");
            f.Ships("A", 1, 1);   // a lone rival warship docks: dominance lost
            var lost = ConvTurn(f, 3);
            ok &= Check(lost.Any(r => r.What == ConversionReport.Kind.End && r.EndReason == ConversionEndReason.DominanceLost)
                        && a.ConversionBy == Planet.NoOwner && Near(a.ConversionCarryProgress, 0.5f) && a.ConversionCarryBy == 0,
                "dominance lost: the session ends and keeps 0.5 as the carry");
            foreach (var ship in a.DockedShips.Where(s => s.Owner == 1).ToList()) a.DestroyDockedShip(ship);
            var back = ConvTurn(f, 5);   // 2 turns later, inside the window
            ok &= Check(back.Any(r => r.What == ConversionReport.Kind.Start && Near(r.ResumedProgress, 0.5f)) && Near(a.ConversionProgress, 0.75f),
                "back within 3 turns: the session resumes at 0.5 and adds a turn (0.75)");
            ok &= Check(a.ConversionCarryBy == Planet.NoOwner, "a resumed carry is spent");
        }
        using (var f = Fixture.Line())
        {
            f.Constants.conversionBombardReference = 60f; f.Constants.conversionTurnsBase = 4f; f.Constants.conversionCarryTurns = 3;
            var a = f.P("A");
            ConversionSelfCheck.Populate(a, (1, 4));
            f.Siege("A", 0, 3); f.War(0, 1);
            ConvTurn(f, 1); ConvTurn(f, 2);
            f.Ships("A", 1, 1);
            ConvTurn(f, 3);
            foreach (var ship in a.DockedShips.Where(s => s.Owner == 1).ToList()) a.DestroyDockedShip(ship);
            var late = ConvTurn(f, 7);   // 4 turns later, past the window (turn 3 + 3 = 6)
            ok &= Check(late.Any(r => r.What == ConversionReport.Kind.Start && Near(r.ResumedProgress, 0f)) && Near(a.ConversionProgress, 0.25f),
                "back after the window: a fresh start from 0");
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

    private static List<BunkerBlockTracker.Entry> Blocked(List<ConversionReport> reports)
        => reports.Where(r => r.What == ConversionReport.Kind.Blocked)
            .Select(r => new BunkerBlockTracker.Entry { Planet = r.Planet, Dominator = r.Dominator, Bunkers = r.Bunkers }).ToList();

    // The ConversionBlocked report (ConversionSystem.Step) and its change-only bookkeeping (BunkerBlockTracker).
    public static bool RunConversionBlockedCheck()
    {
        var ok = true;
        using (var f = Fixture.Line())
        {
            f.Constants.conversionTurnsBase = 4f;
            var a = f.P("A");
            ConversionSelfCheck.Populate(a, (1, 4));
            f.Ships("A", 0, 1); f.War(0, 1);
            f.Bunkers("A", 1, 2);
            var tracker = new BunkerBlockTracker();

            var r1 = ConvTurn(f, 1);
            ok &= Check(Blocked(r1).Count == 1 && Blocked(r1)[0].Bunkers == 2 && Blocked(r1)[0].Dominator == 0 && Blocked(r1)[0].Planet == "A",
                "dominated + war + foreign inhabitant + 2 bunkers: one Blocked report (A, dominator 0, 2 bunkers)");
            ok &= Check(tracker.Update(Blocked(r1)).Count == 1, "the first blocked turn logs one line");
            ok &= Check(tracker.Update(Blocked(ConvTurn(f, 2))).Count == 0, "a second turn with the same bunker count logs nothing");

            a.DestroyDockedShip(a.DockedShips.First(s => s.Kind == Ship.ShipKind.Bunker));
            var r3 = ConvTurn(f, 3);
            var lines3 = tracker.Update(Blocked(r3));
            ok &= Check(lines3.Count == 1 && lines3[0].Bunkers == 1, "one bunker destroyed (2 -> 1): the count change logs one line with 1");

            a.DestroyDockedShip(a.DockedShips.First(s => s.Kind == Ship.ShipKind.Bunker));
            var r4 = ConvTurn(f, 4);
            ok &= Check(Blocked(r4).Count == 0 && r4.Any(r => r.What == ConversionReport.Kind.Start) && tracker.Update(Blocked(r4)).Count == 0,
                "the last bunker destroyed: no Blocked report, the conversion starts, nothing is logged");

            f.Bunkers("A", 1, 1);
            var r5 = ConvTurn(f, 5);
            ok &= Check(Blocked(r5).Count == 1 && tracker.Update(Blocked(r5)).Count == 1, "a new bunker after the clearing blocks again and logs again");
        }
        using (var f = Fixture.Line())     // no war with a holder: no session could ever start, so nothing is reported
        {
            var a = f.P("A");
            ConversionSelfCheck.Populate(a, (1, 4));
            f.Ships("A", 0, 1); f.Bunkers("A", 1, 1);
            ok &= Check(Blocked(ConvTurn(f, 1)).Count == 0, "peace with the only holder: no Blocked report");
        }
        using (var f = Fixture.Line())     // nothing foreign to convert
        {
            var a = f.P("A");
            ConversionSelfCheck.Populate(a, (0, 4));
            f.Ships("A", 0, 1); f.War(0, 1); f.Bunkers("A", 1, 1);
            ok &= Check(Blocked(ConvTurn(f, 1)).Count == 0, "the dominator holds every inhabitant: no Blocked report");
        }
        using (var f = Fixture.Line())     // no dominator (no warship of mine): nothing is blocked
        {
            var a = f.P("A");
            ConversionSelfCheck.Populate(a, (1, 4));
            f.War(0, 1); f.Bunkers("A", 1, 1);
            ok &= Check(Blocked(ConvTurn(f, 1)).Count == 0, "no dominator: no Blocked report");
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
            // Task 13: siegeship bombard 20, health 100 (no Siegeship research lines): 20 x 95/100 + 20 = 39.
            ok &= Check(Near(a.GetIncomingBombard(Ship.ShipKind.SiegeShip, 1), 39f),
                "incoming bombard: the siegeship fleet adds 19 (5 damage) + 20 = 39");
            ok &= Check(Near(a.GetIncomingBombard(Ship.ShipKind.WarShip, 1), 0f),
                "incoming bombard: a fleet-less order adds none");
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
            f.Constants.siegePaceFraction = 0f;   // the pace term has its own check; the rest test the bunker need alone
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
