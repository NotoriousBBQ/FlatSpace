using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using FlatSpace.AI;
using Flatspace.Objects.Production;

public static class WarshipSelfCheck
{
    private static float _nextPlanetX;

    [MenuItem("FlatSpace/AI/Run Warship Self-Check")]
    public static void Run() => RunChecks();

    public static bool RunChecks()
    {
        _nextPlanetX = 0f;
        var ok = RunStatsCheck();
        ok &= RunCostCheck();
        ok &= RunUpdateWarshipCheck();
        ok &= RunUpdateWarshipAIWeightCheck();
        ok &= RunBlockadeValueCheck();
        ok &= RunBlockadeRouteCheck();
        Debug.Log(ok
            ? "[WarshipSelfCheck] ALL PASSED"
            : "[WarshipSelfCheck] FAILURES (see errors above)");
        return ok;
    }

    private static bool Check(bool condition, string label)
    {
        if (!condition) Debug.LogError($"[WarshipSelfCheck] FAIL: {label}");
        return condition;
    }

    private static bool Near(float a, float b) => Mathf.Abs(a - b) < 0.001f;

    public static ShipData MakeTemplate()
    {
        var t = ScriptableObject.CreateInstance<ShipData>();
        t.shipSpeed = 150f;
        t.shipHealth = 100f; t.shipHealthMax = 200f;
        t.shipDefense = 5f; t.shipDefenseMax = 15f;
        t.shipOffense = 10f; t.shipOffenseMax = 30f;
        return t;
    }

    private static CatalogItem Improvement(string name, string effect, int tier, bool researched)
    {
        var item = ScriptableObject.CreateInstance<CatalogItem>();
        item.itemName = name; item.name = name;
        item.type = "Ship Improvement"; item.subType = "Warship";
        item.tier = tier; item.effect = effect; item.researched = researched;
        return item;
    }

    /// <summary>Three 5-tier lines (Off n / Hp n / Def n). `researchedTiers` of each line are marked researched.</summary>
    public static List<CatalogItem> MakeResearch(int researchedTiers = 0)
    {
        var items = new List<CatalogItem>();
        for (var tier = 1; tier <= 5; tier++)
        {
            items.Add(Improvement("Off " + tier, WarshipStats.OffenseKey, tier, tier <= researchedTiers));
            items.Add(Improvement("Hp " + tier, WarshipStats.HealthKey, tier, tier <= researchedTiers));
            items.Add(Improvement("Def " + tier, WarshipStats.DefenseKey, tier, tier <= researchedTiers));
        }
        return items;
    }

    private static void DestroyAll(List<CatalogItem> items)
    {
        foreach (var i in items) Object.DestroyImmediate(i);
    }

    private static CatalogItem ProductionItem(string name, string subType, float cost)
    {
        var item = ScriptableObject.CreateInstance<CatalogItem>();
        item.itemName = name; item.name = name; item.type = "Ship"; item.subType = subType; item.cost = cost;
        return item;
    }

    public static bool RunCostCheck()
    {
        var ok = true;
        ok &= Check(Near(WarshipCosts.BuildCost(100f, 0.1f, 0), 100f), "no improvements: base cost");
        ok &= Check(Near(WarshipCosts.BuildCost(100f, 0.1f, 15), 250f), "15 improvements at 0.1: 2.5x base");
        ok &= Check(Near(WarshipCosts.BuildCost(100f, 0f, 15), 100f), "a factor of 0 switches the surcharge off");
        ok &= Check(Near(WarshipCosts.UpdateCost(100f, 0.1f, 5), 50f)
                    && Near(WarshipCosts.UpdateCost(100f, 0.1f, 5),
                            WarshipCosts.BuildCost(100f, 0.1f, 15) - WarshipCosts.BuildCost(100f, 0.1f, 10)),
            "update cost = fully upgraded cost - the ship's own cost");
        ok &= Check(Near(WarshipCosts.UpdateCost(100f, 0f, 5), 1f), "an update never costs less than 1");

        var research = MakeResearch(3);   // 9 researched warship improvements
        var warship = ProductionItem("Warship", "Warship", 100f);
        var colony = ProductionItem("Colony Ship Production", "ColonyShip", 20f);
        try
        {
            ok &= Check(Near(WarshipCosts.ProductionCost(warship, null, 0, research, 100f, 0.1f), 190f),
                "a Warship built with 9 researched improvements costs 100 x (1 + 0.1 x 9)");
            ok &= Check(Near(WarshipCosts.ProductionCost(colony, null, 0, research, 100f, 0.1f), 20f),
                "a ColonyShip keeps its catalog cost");

            var withFixed = new Planet.ProductionItem(warship, 190f);
            var without = new Planet.ProductionItem(warship);
            ok &= Check(Near(withFixed.Cost, 190f) && Near(without.Cost, 100f),
                "ProductionItem.Cost is the fixed cost when set, else the catalog cost");
        }
        finally
        {
            DestroyAll(research);
            Object.DestroyImmediate(warship);
            Object.DestroyImmediate(colony);
        }
        return ok;
    }

    public static bool RunStatsCheck()
    {
        var ok = true;
        var template = MakeTemplate();
        var research = MakeResearch();
        try
        {
            var stats = new WarshipStats(research);
            ok &= Check(stats.TierCount(WarshipStats.OffenseKey) == 5, "each line has 5 tiers");
            ok &= Check(Near(stats.Offense(template, new List<string>()), 10f), "no improvements: base offense");
            ok &= Check(Near(stats.Offense(template, new List<string> { "Off 1" }), 14f), "one offense tier adds (30-10)/5 = 4");
            ok &= Check(Near(stats.Offense(template, new List<string> { "Off 1", "Off 2", "Off 3", "Off 4", "Off 5" }), 30f),
                "all five offense tiers reach the max");
            ok &= Check(Near(stats.Health(template, new List<string> { "Off 1", "Off 2" }), 100f)
                        && Near(stats.Defense(template, new List<string> { "Off 1", "Off 2" }), 5f),
                "offense tiers do not change health or defense");
            ok &= Check(Near(stats.Health(template, new List<string> { "Hp 3" }), 160f)
                        && Near(stats.Defense(template, new List<string> { "Def 2", "Def 5" }), 9f),
                "health and defense lines scale independently (+20 and +2 per tier)");
            ok &= Check(Near(stats.Offense(template, null), 10f), "a null snapshot is treated as no improvements");
            ok &= Check(stats.Offense(null, new List<string>()) == 0f, "a null template has 0 offense, no crash");

            var noLines = new WarshipStats(new List<CatalogItem>());
            ok &= Check(Near(noLines.Offense(template, new List<string> { "Off 1" }), 10f),
                "a stat with no research line stays at base (no divide by zero)");

            var researched = MakeResearch(2);
            try
            {
                var names = WarshipStats.ResearchedNames(researched);
                ok &= Check(names.Count == 6 && names.Contains("Off 2") && !names.Contains("Off 3"),
                    "ResearchedNames lists only researched warship improvements");
            }
            finally { DestroyAll(researched); }
        }
        finally
        {
            DestroyAll(research);
            Object.DestroyImmediate(template);
        }
        return ok;
    }

    private static PlanetSpawnData MakeSpawn(string name, IEnumerable<string> connections = null)
    {
        var resourceData = ScriptableObject.CreateInstance<PlanetResourceData>();
        resourceData._initialPopulation = 1;
        resourceData._maxPopulation = 5;
        var spawn = ScriptableObject.CreateInstance<PlanetSpawnData>();
        spawn._planetName = name;
        spawn._planetPosition = new Vector3(_nextPlanetX, 0f, 0f);
        _nextPlanetX += 100f;
        spawn._planetType = Planet.PlanetType.PlanetTypeNormal;
        spawn._resourceData = resourceData;
        spawn._connections = connections != null ? new List<string>(connections) : new List<string>();
        return spawn;
    }

    private static GameAIConstants MakeConstants(ShipData warship)
    {
        var constants = ScriptableObject.CreateInstance<GameAIConstants>();
        constants.defaultTravelSpeed = 1f;
        constants.warShipData = warship;
        return constants;
    }

    private static GameAIMap BuildMap(GameObject go, GameAIConstants constants, params PlanetSpawnData[] spawns)
    {
        var map = go.AddComponent<GameAIMap>();
        map.GameAIMapInit(new List<PlanetSpawnData>(spawns), constants);
        return map;
    }

    private static void DockWarships(Planet planet, int owner, int count, params string[] snapshot)
    {
        for (var i = 0; i < count; i++)
            planet.DockShipFromSave(Ship.ShipKind.WarShip, owner, new List<string>(snapshot));
    }

    public static bool RunUpdateWarshipCheck()
    {
        var ok = true;
        _nextPlanetX = 0f;
        var go = new GameObject("WarshipSelfCheckMap_Update");
        var template = MakeTemplate();
        var constants = MakeConstants(template);
        var research = MakeResearch(2);   // 6 researched: Off 1-2, Hp 1-2, Def 1-2
        var warshipItem = ProductionItem("Warship", "Warship", 100f);
        var updateItem = ProductionItem("Update Warship", "WarshipUpdate", 50f);
        try
        {
            var map = BuildMap(go, constants, MakeSpawn("A"));
            var planet = map.GetPlanet("A");
            var researched = WarshipStats.ResearchedNames(research);

            ok &= Check(planet.FindWarshipUpdateTarget(0, researched) == null, "no ships docked: nothing to update");

            planet.DockShipFromSave(Ship.ShipKind.ColonyShip, 0, new List<string>());
            DockWarships(planet, 1, 1);                                        // another player's ship, missing everything
            DockWarships(planet, 0, 1, "Off 1", "Off 2", "Hp 1", "Hp 2", "Def 1", "Def 2");   // up to date
            DockWarships(planet, 0, 1, "Off 1");                               // missing 5
            DockWarships(planet, 0, 1);                                        // missing 6
            var target = planet.FindWarshipUpdateTarget(0, researched);
            ok &= Check(target != null && target.ResearchSnapshot.Count == 0,
                "the target is my warship with the most missing improvements (not a colony ship, not another player's)");

            ok &= Check(Near(WarshipCosts.ProductionCost(updateItem, planet, 0, research, 100f, 0.1f), 60f),
                "Update Warship costs base x factor x missing = 100 x 0.1 x 6");

            ok &= Check(planet.ApplyWarshipUpdate(0, researched) && target.ResearchSnapshot.Count == 6,
                "applying the update gives the target every researched improvement");
            var next = planet.FindWarshipUpdateTarget(0, researched);
            ok &= Check(next != null && next.ResearchSnapshot.Count == 1, "the next target is the ship missing 5");
            planet.ApplyWarshipUpdate(0, researched);
            ok &= Check(planet.FindWarshipUpdateTarget(0, researched) == null && !planet.ApplyWarshipUpdate(0, researched),
                "with every owned ship up to date there is no target and applying does nothing");
            ok &= Check(Near(WarshipCosts.ProductionCost(updateItem, planet, 0, research, 100f, 0.1f), 50f),
                "with no target the cost falls back to the catalog cost");
            ok &= Check(planet.DockedShips.FindAll(s => s.Owner == 1)[0].ResearchSnapshot.Count == 0,
                "another player's ship is never touched");

            var newlyResearched = MakeResearch(3);
            try
            {
                ok &= Check(planet.FindWarshipUpdateTarget(0, WarshipStats.ResearchedNames(newlyResearched)) != null,
                    "research finishing later makes the ships upgradable again");
            }
            finally { DestroyAll(newlyResearched); }
        }
        finally
        {
            DestroyAll(research);
            Object.DestroyImmediate(warshipItem);
            Object.DestroyImmediate(updateItem);
            Object.DestroyImmediate(go);
            Object.DestroyImmediate(constants);
            Object.DestroyImmediate(template);
        }
        return ok;
    }

    public static bool RunUpdateWarshipAIWeightCheck()
    {
        var ok = true;
        _nextPlanetX = 0f;
        var mapGo = new GameObject("WarshipSelfCheckMap_UpdateAI");
        var playerGo = new GameObject("WarshipSelfCheckPlayer_UpdateAI");
        var template = MakeTemplate();
        var constants = MakeConstants(template);
        var research = MakeResearch(1);
        var updateItem = ProductionItem("Update Warship", "WarshipUpdate", 50f);
        try
        {
            var map = BuildMap(mapGo, constants, MakeSpawn("A"));
            var player = playerGo.AddComponent<Player>();
            var playerAI = playerGo.AddComponent<PlayerAI>();
            playerAI.Player = player;
            playerAI.AIMap = map;
            player.playerID = 0;
            playerAI.Strategy = PlayerAI.AIStrategy.AIStrategyExpand;
            playerAI.ResearchCatalog = playerGo.AddComponent<Catalog>();
            playerAI.ResearchCatalog.catalogItems = research;

            ok &= Check(playerAI.GetIndustrySituationalWeightMultiplier(updateItem, "A") == 0f,
                "no docked warship: Update Warship is not offered");
            DockWarships(map.GetPlanet("A"), 0, 1);
            ok &= Check(playerAI.GetIndustrySituationalWeightMultiplier(updateItem, "A") == 1f,
                "a docked warship missing a researched improvement: offered at the plain multiplier");
            ok &= Check(PlayerAI.GetIndustryStrategyWeight(updateItem, PlayerAI.AIStrategy.AIStrategyExpand) == 1.5f
                        && PlayerAI.GetIndustryStrategyWeight(updateItem, PlayerAI.AIStrategy.AIStrategyConsolidate) == 2.5f,
                "Update Warship has Warship's table weights (Expand 1.5, Consolidate 2.5)");
        }
        finally
        {
            DestroyAll(research);
            Object.DestroyImmediate(updateItem);
            Object.DestroyImmediate(playerGo);
            Object.DestroyImmediate(mapGo);
            Object.DestroyImmediate(constants);
            Object.DestroyImmediate(template);
        }
        return ok;
    }

    private static GameAI.GameAIOrder MakeOrder(GameAI.GameAIOrder.OrderType type, int player, string origin,
        string target, int timingDelay, int totalDelay, object data)
        => new GameAI.GameAIOrder
        {
            Type = type,
            TimingType = GameAI.GameAIOrder.OrderTimingType.OrderTimingTypeDelayed,
            TimingDelay = timingDelay, TotalDelay = totalDelay,
            Data = data, Origin = origin, Target = target, PlayerId = player,
        };

    // Line A - B - C, planets 100 apart. Returns the map; the caller destroys `go`.
    private static GameAIMap BuildLine(GameObject go, GameAIConstants constants)
    {
        _nextPlanetX = 0f;
        return BuildMap(go, constants,
            MakeSpawn("A", new[] { "B" }),
            MakeSpawn("B", new[] { "A", "C" }),
            MakeSpawn("C", new[] { "B" }));
    }

    public static bool RunBlockadeValueCheck()
    {
        var ok = true;
        var go = new GameObject("WarshipSelfCheckMap_BlockadeValue");
        var template = MakeTemplate();   // offense 10 per ship with no research
        var constants = MakeConstants(template);
        var research = MakeResearch();
        try
        {
            var map = BuildLine(go, constants);
            var b = map.GetPlanet("B");
            var blockade = new BlockadeSystem(map, research);

            ok &= Check(blockade.Value(b, 0, out _) == 0f, "an empty planet blocks nothing");

            DockWarships(b, 1, 3);   // player 1: offense 30
            DockWarships(b, 0, 1);   // player 0: offense 10
            ok &= Check(Near(blockade.DockedOffense(b, 1), 30f), "docked offense is the sum over that player's warships");
            ok &= Check(Near(blockade.Value(b, 0, out var blocker), 20f) && blocker == 1,
                "player 1 blockades player 0's order by 30 - 10 = 20");
            ok &= Check(blockade.Value(b, 1, out _) == 0f, "the stronger player is not blockaded by the weaker");

            DockWarships(b, 2, 3);   // player 2: 30 as well
            ok &= Check(Near(blockade.Value(b, 0, out blocker), 20f) && blocker == 1,
                "two blockaders do not add up: the largest single one counts (ties go to the lowest id)");

            DockWarships(b, 0, 2);   // player 0 now 30
            ok &= Check(blockade.Value(b, 0, out _) == 0f, "equal offense is not a positive blockade");

            b.DockShipFromSave(Ship.ShipKind.ColonyShip, 1, new List<string>());
            ok &= Check(Near(blockade.DockedOffense(b, 1), 30f), "colony ships add no offense");

            // A ship docked while the constants have no warship template counts as 0 offense.
            var c = map.GetPlanet("C");
            var savedTemplate = constants.warShipData;
            constants.warShipData = null;
            DockWarships(c, 1, 2);
            constants.warShipData = savedTemplate;
            ok &= Check(blockade.DockedOffense(c, 1) == 0f, "a docked ship with a null template has 0 offense, no crash");
        }
        finally
        {
            DestroyAll(research);
            Object.DestroyImmediate(go);
            Object.DestroyImmediate(constants);
            Object.DestroyImmediate(template);
        }
        return ok;
    }

    public static bool RunBlockadeRouteCheck()
    {
        var ok = true;
        var go = new GameObject("WarshipSelfCheckMap_BlockadeRoute");
        var template = MakeTemplate();
        var constants = MakeConstants(template);
        var research = MakeResearch();
        try
        {
            var map = BuildLine(go, constants);
            var blockade = new BlockadeSystem(map, research);

            var route = blockade.Route("A", "C");
            ok &= Check(route.Count == 2 && route[0].Name == "B" && route[1].Name == "C",
                "the route lists every node after the origin, ending at the target");
            ok &= Check(Near(route[0].Fraction, 0.5f) && Near(route[1].Fraction, 1f),
                "B is halfway along an even route, the target is at 1");

            var single = blockade.Route("A", "A");
            ok &= Check(single.Count == 1 && single[0].Name == "A", "origin == target checks just the target, no throw");
            var unknown = blockade.Route("A", "Nowhere");
            ok &= Check(unknown.Count == 1 && unknown[0].Name == "Nowhere", "an unknown planet checks just the target, no throw");

            ok &= Check(Near(BlockadeSystem.Progress(4, 4), 0f) && Near(BlockadeSystem.Progress(2, 4), 0.5f)
                        && Near(BlockadeSystem.Progress(0, 4), 1f) && Near(BlockadeSystem.Progress(-1, 4), 1f)
                        && Near(BlockadeSystem.Progress(0, 0), 1f),
                "progress runs 0 -> 1 as the delay runs out and is clamped");

            var o = MakeOrder(GameAI.GameAIOrder.OrderType.OrderTypeFoodTransport, 0, "A", "C", 3, 4, 30f);
            ok &= Check(blockade.PassedNodes(o).Count == 0, "3 of 4 turns left: nothing passed yet (progress .25)");
            o.TimingDelay = 2;
            var passed = blockade.PassedNodes(o);
            ok &= Check(passed.Count == 1 && passed[0].Name == "B", "2 of 4 left: B was passed this turn");
            o.TimingDelay = 1;
            ok &= Check(blockade.PassedNodes(o).Count == 0, "B is not passed twice; C is not reached yet");
            o.TimingDelay = 0;
            passed = blockade.PassedNodes(o);
            ok &= Check(passed.Count == 1 && passed[0].Name == "C", "on arrival the target is passed");

            var fast = MakeOrder(GameAI.GameAIOrder.OrderType.OrderTypeFoodTransport, 0, "A", "C", 0, 1, 30f);
            ok &= Check(blockade.PassedNodes(fast).Count == 2, "a 1-turn trip passes every node in that one turn");
        }
        finally
        {
            DestroyAll(research);
            Object.DestroyImmediate(go);
            Object.DestroyImmediate(constants);
            Object.DestroyImmediate(template);
        }
        return ok;
    }
}
