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
}
