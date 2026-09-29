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
