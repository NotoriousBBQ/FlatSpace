# Warship Stats, Upgrades and Blockade Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Give warships real research-driven stats and cost, add an "Update Warship" production item, and add blockade of colony ships and food/grotsits shipments by docked warship offense.

**Architecture:** Stats are always derived from a ship's `ShipData` template plus its `ResearchSnapshot` (a pure `WarshipStats` class). Cost is fixed into the production item at scheduling time (`ProductionItem.FixedCost`). Blockade is a pure `BlockadeSystem` that `GameAI.ProcessCurrentOrders` calls once per turn to remove or shrink in-flight orders as they pass planets along their route (routes come from the existing `GameAIMap.GetPath`).

**Tech Stack:** Unity 6000.4.1f1, C#, JsonUtility saves, Editor-menu self-checks (no test framework; see CLAUDE.md "Tests").

**Spec:** `docs/superpowers/specs/2026-09-28-warship-stats-and-blockade-design.md`

## Global Constraints

- No command-line build. Compile check = user focuses the Unity Editor and reads the Console; a self-check runs from its `FlatSpace → AI` menu item. Rider MCP build tools are unreliable (CLAUDE.md); `get_file_problems` is a weak signal only.
- A self-check must never depend on `Gameboard.Instance`. `Planet.DockNewShip`/`DockShipRebuiltSnapshot` need it; use `DockShipFromSave`.
- Methods a self-check calls are `public`, not `internal` (`Assets/Editor` is a separate assembly).
- `OrderType` is serialized as an int: never insert an enum value (this plan adds none).
- Every new script's `.meta` file is committed with it.
- Namespace: `FlatSpace.AI` for new AI classes (match neighbours). Catalog types are in `Flatspace.Objects.Production`.
- Subtype string is `"Warship"` (lowercase s); the new production subtype is `"WarshipUpdate"`.
- Planets in self-checks need distinct positions (pathing tie-break note in CLAUDE.md).
- Commit messages end with `Co-Authored-By: Claude Sonnet 5.5 <noreply@anthropic.com>`.
- Spec deviations decided while planning (fix the spec in Task 7): (1) no all-pairs precompute change is needed: `GameAIMap.GetPath(origin, dest)` already returns the ordered node list (origin first) with per-edge costs in `PathNode.Connections`; (2) blockade is not evaluated "at order creation": a new order has progress 0 so it has passed no node; (3) `IndustryChoiceElement.Cost` (a matrix sort key) keeps the catalog cost, only actual production progress uses the fixed cost.

## Review Focus

- A warship research line missing from the catalog (tier count 0): stats must equal base, never divide by zero. (Task 1 check.)
- A docked ship whose `Template` is null: offense 0, no crash. (Task 1 and Task 4 checks.)
- `GetPath` for an unreachable or identical origin/target: `FindPath` returns a 1-node stub; blockade must check only the target and never throw. (Task 4 check.)
- Two blockaders of equal offense: the lowest player id is reported, deterministically. (Task 4 check.)
- Zero-delay delayed orders (`TotalDelay <= 0`) are executed by `ProcessNewOrders`, which this plan does not touch; they are never blockaded (documented limitation, Task 5 note).
- Warship cost fixed at scheduling but the ship built at completion carries whatever is researched then; a mid-build research completion gives a slightly better ship than paid for (accepted, noted in CLAUDE.md, Task 7).

---

### Task 1: Stat data and `WarshipStats`

**Files:**
- Create: `Assets/Flatspace/GameAI/WarshipStats.cs` (+ `.meta`)
- Create: `Assets/Editor/WarshipSelfCheck.cs` (+ `.meta`)
- Modify: `Assets/Flatspace/Objects/Ships/WarShip.asset` (stat values)
- Modify: `Assets/Flatspace/Catalogs/Research/Research Catalog.json` (effect keys + Armor and Shields lines)

**Interfaces:**
- Produces (`FlatSpace.AI.WarshipStats`):
  - `const string OffenseKey = "Warship Offense"`, `HealthKey = "Warship Health"`, `DefenseKey = "Warship Defense"`
  - `WarshipStats(IEnumerable<CatalogItem> researchItems)`
  - `static bool IsWarshipImprovement(CatalogItem item)`
  - `int TierCount(string key)`
  - `float Offense(ShipData template, ICollection<string> snapshot)`, `Health(...)`, `Defense(...)` (null template returns 0)
  - `static List<string> ResearchedNames(IEnumerable<CatalogItem> researchItems)` (names of researched warship improvements)
- Produces (`WarshipSelfCheck`): `public static bool RunChecks()`, private helpers `Check`, `MakeTemplate()`, `MakeResearch()`, `MakeSpawn(...)`, `BuildMap(...)` reused by later tasks.

- [ ] **Step 1: Create `WarshipStats.cs`**

```csharp
using System.Collections.Generic;
using System.Linq;
using Flatspace.Objects.Production;

namespace FlatSpace.AI
{
    /// <summary>
    /// A warship's effective stats: its ShipData template value plus, for each stat, the tiers of that stat's
    /// research line the ship carries (its ResearchSnapshot) x (Max - base) / (tiers in that line). Pure: it never
    /// touches Gameboard.Instance, so self-checks can drive it directly. The formula lives only here.
    /// </summary>
    public class WarshipStats
    {
        public const string OffenseKey = "Warship Offense";
        public const string HealthKey = "Warship Health";
        public const string DefenseKey = "Warship Defense";

        private readonly List<CatalogItem> _items;

        public WarshipStats(IEnumerable<CatalogItem> researchItems)
        {
            _items = researchItems != null ? researchItems.ToList() : new List<CatalogItem>();
        }

        public static bool IsWarshipImprovement(CatalogItem item)
            => item != null && item.type == "Ship Improvement" && item.subType == "Warship";

        /// <summary>Number of catalog tiers in the research line that improves this stat (effect == key).</summary>
        public int TierCount(string key)
            => _items.Count(i => IsWarshipImprovement(i) && i.effect == key);

        private int TiersCarried(ICollection<string> snapshot, string key)
            => snapshot == null
                ? 0
                : _items.Count(i => IsWarshipImprovement(i) && i.effect == key && snapshot.Contains(i.itemName));

        private float Stat(float baseValue, float maxValue, string key, ICollection<string> snapshot)
        {
            var tiers = TierCount(key);
            if (tiers == 0) return baseValue;   // no research line for this stat: nothing to add, nothing to divide
            return baseValue + TiersCarried(snapshot, key) * (maxValue - baseValue) / tiers;
        }

        public float Offense(ShipData template, ICollection<string> snapshot)
            => template == null ? 0f : Stat(template.shipOffense, template.shipOffenseMax, OffenseKey, snapshot);

        public float Health(ShipData template, ICollection<string> snapshot)
            => template == null ? 0f : Stat(template.shipHealth, template.shipHealthMax, HealthKey, snapshot);

        public float Defense(ShipData template, ICollection<string> snapshot)
            => template == null ? 0f : Stat(template.shipDefense, template.shipDefenseMax, DefenseKey, snapshot);

        /// <summary>Names of the warship improvements already researched, in catalog order.</summary>
        public static List<string> ResearchedNames(IEnumerable<CatalogItem> researchItems)
            => researchItems == null
                ? new List<string>()
                : researchItems.Where(i => i.researched && IsWarshipImprovement(i)).Select(i => i.itemName).ToList();
    }
}
```

- [ ] **Step 2: Create `WarshipSelfCheck.cs` with the stats check**

Values used by every check in this file: Offense base 10 / max 30 (+4 per tier of 5), Health 100 / 200 (+20), Defense 5 / 15 (+2).

```csharp
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
```

- [ ] **Step 3: Set the warship stat values in `WarShip.asset`**

Replace lines 15-22 (`shipName` through `shipOffenseMax`) with (150 = `defaultTravelSpeed` in `GameAIConstantsProductionTypes.asset`; the other values are tunable starting points):

```yaml
  shipName: Warship
  shipSpeed: 150
  shipHealth: 100
  shipHealthMax: 200
  shipDefense: 5
  shipDefenseMax: 15
  shipOffense: 10
  shipOffenseMax: 30
```

- [ ] **Step 4: Research catalog: effect keys and the two new lines**

The five existing "Warship Weapons Upgrade 1-5" items each have `"effect": "Improve warship stats"`; that string appears only on those five. Run this PowerShell from the project root (it edits the file in place, preserving the existing text):

```powershell
$path = "Assets\Flatspace\Catalogs\Research\Research Catalog.json"
$text = [System.IO.File]::ReadAllText((Resolve-Path $path))
$text = $text.Replace('"effect": "Improve warship stats"', '"effect": "Warship Offense"')

$block = New-Object System.Text.StringBuilder
foreach ($line in @(@("Warship Armor", "Warship Health", "Increases Warship 1 Health"), @("Warship Shields", "Warship Defense", "Increases Warship 1 Defense"))) {
  for ($tier = 1; $tier -le 5; $tier++) {
    $req = if ($tier -eq 1) { "Warship 1" } else { "$($line[0]) $($tier - 1)" }
    $desc = if ($tier -eq 1) { $line[2] } else { "$($line[2]) (upgrade $tier)" }
    [void]$block.AppendLine("        {")
    [void]$block.AppendLine("          `"itemName`": `"$($line[0]) $tier`",")
    [void]$block.AppendLine("          `"description`": `"$desc`",")
    [void]$block.AppendLine("          `"type`": `"Ship Improvement`",")
    [void]$block.AppendLine("          `"subType`" : `"Warship`",")
    [void]$block.AppendLine("          `"tier`" : $tier,")
    [void]$block.AppendLine("          `"cost`": $($tier * 3000).0,")
    [void]$block.AppendLine("          `"effect`": `"$($line[1])`",")
    [void]$block.AppendLine("          `"requiredTech`": `"$req`",")
    [void]$block.AppendLine("          `"researched`": false,")
    [void]$block.AppendLine("          `"maintenanceCost`": 0.0")
    [void]$block.AppendLine("        },")
  }
}
$marker = '"itemName": "ColonyShip Improvement 1"'
$idx = $text.IndexOf($marker)
if ($idx -lt 0) { throw "marker not found" }
$open = $text.LastIndexOf("{", $idx)
$lineStart = $text.LastIndexOf("`n", $open) + 1
$text = $text.Insert($lineStart, $block.ToString())
[System.IO.File]::WriteAllText((Resolve-Path $path), $text)
```

Then read the file around the insertion and confirm: the JSON is still valid (commas between every item, no trailing comma issue), Weapons 1-5 say `"Warship Offense"`, and Armor 1-5 and Shields 1-5 follow Weapons Upgrade 5. Expected: 10 new items.

- [ ] **Step 5: Run the check**

Ask the user to focus the Unity Editor (recompile), fix any Console errors, then run `FlatSpace → AI → Run Warship Self-Check`. Expected Console: `[WarshipSelfCheck] ALL PASSED`. Also have them open the Research catalog on a player's `Catalog` component (or trust the check) to confirm the JSON loads: a JSON error shows in the Console when a match starts.

- [ ] **Step 6: Commit**

```bash
git add Assets/Flatspace/GameAI/WarshipStats.cs Assets/Flatspace/GameAI/WarshipStats.cs.meta Assets/Editor/WarshipSelfCheck.cs Assets/Editor/WarshipSelfCheck.cs.meta Assets/Flatspace/Objects/Ships/WarShip.asset "Assets/Flatspace/Catalogs/Research/Research Catalog.json"
git commit -m "feat(ships): add warship stats, Armor/Shields research lines and WarshipStats

Co-Authored-By: Claude Sonnet 5.5 <noreply@anthropic.com>"
```

---

### Task 2: Build cost and fixed production cost

**Files:**
- Create: `Assets/Flatspace/GameAI/WarshipCosts.cs` (+ `.meta`)
- Modify: `Assets/Flatspace/GameAI/GameAIConstants.cs` (new tunable)
- Modify: `Assets/Flatspace/Objects/Planets/Planet.cs:813-836` (ProductionItem, ScheduleProductionItem), `:867-891` (cost reads)
- Modify: `Assets/Flatspace/SaveSystem/SaveLoadSystem.cs:33-43` (ProductionSave)
- Modify: `Assets/Flatspace/GameAI/GameAIMap.cs:270-285` (restore fixed cost)
- Modify: `Assets/Flatspace/GameAI/GameAI.cs:216-221` (compute cost at scheduling)
- Modify: `Assets/Flatspace/UI/MainGameScreenUI/PlanetDetailUIController.cs:195`
- Modify: `Assets/Editor/WarshipSelfCheck.cs`

**Interfaces:**
- Consumes: `WarshipStats.ResearchedNames` (Task 1).
- Produces:
  - `GameAIConstants.warshipImprovementCostFactor` (float, default 0.1)
  - `WarshipCosts.BuildCost(float baseCost, float factor, int improvements)`, `WarshipCosts.UpdateCost(float baseWarshipCost, float factor, int missing)`, `WarshipCosts.ProductionCost(CatalogItem item, Planet planet, int owner, IEnumerable<CatalogItem> research, float baseWarshipCost, float factor)`
  - `Planet.ProductionItem.FixedCost` (0 = use catalog cost) and `.Cost` property; `Planet.ScheduleProductionItem(CatalogItem item, float fixedCost = 0f)`
  - `ProductionSave.FixedCost`

Note: `ProductionCost` for subtype `WarshipUpdate` calls `Planet.FindWarshipUpdateTarget`, which Task 3 adds. To keep Task 2 compilable, Task 2 implements only `"Warship"` and default; Task 3 adds the `"WarshipUpdate"` case.

- [ ] **Step 1: Add the check (compiles after Step 2)**

In `WarshipSelfCheck.RunChecks()` add `ok &= RunCostCheck();` after `RunStatsCheck()`, and add:

```csharp
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
```

- [ ] **Step 2: Create `WarshipCosts.cs`**

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using Flatspace.Objects.Production;

namespace FlatSpace.AI
{
    /// <summary>
    /// Warship production costs. A warship costs base x (1 + factor x improvements it carries); an Update Warship
    /// costs the difference between the fully upgraded ship and the ship's own cost, which is base x factor x missing.
    /// Pure so self-checks can drive it.
    /// </summary>
    public static class WarshipCosts
    {
        public static float BuildCost(float baseCost, float factor, int improvements)
            => baseCost * (1f + factor * improvements);

        public static float UpdateCost(float baseWarshipCost, float factor, int missing)
            => Math.Max(1f, baseWarshipCost * factor * missing);

        /// <summary>
        /// The cost fixed into a production item when it is scheduled. `research` is the owner's research catalog
        /// items; `baseWarshipCost` is the Warship production item's catalog cost.
        /// </summary>
        public static float ProductionCost(CatalogItem item, Planet planet, int owner,
            IEnumerable<CatalogItem> research, float baseWarshipCost, float factor)
        {
            switch (item.subType)
            {
                case "Warship":
                    return BuildCost(item.cost, factor, WarshipStats.ResearchedNames(research).Count);
                default:
                    return item.cost;
            }
        }
    }
}
```

- [ ] **Step 3: Add the tunable to `GameAIConstants.cs`**

After the `warshipsPerColonizedPlanet` field (line 48), add:

```csharp

    [Header("Warships")]
    // Each researched Warship improvement a ship carries adds this fraction of the base cost to building it
    // (0.1 with 15 improvements = 2.5x). Update Warship costs base x this x the improvements the ship is missing.
    public float warshipImprovementCostFactor = 0.1f;
```

- [ ] **Step 4: `Planet.ProductionItem` and scheduling**

Replace the struct and `ScheduleProductionItem` (`Planet.cs:813-836`) with:

```csharp
    public struct ProductionItem
    {
        public float Progress;
        public CatalogItem Item;
        // Cost fixed when the item was scheduled (0 = use the catalog cost). Research finishing mid-build must not
        // move the goalposts, so warship costs are fixed here rather than read from Item.cost each turn.
        public float FixedCost;
        public float Cost => FixedCost > 0f ? FixedCost : Item.cost;

        public ProductionItem(CatalogItem item, float fixedCost = 0f)
        {
            Item = item;
            Progress = 0.0f;
            FixedCost = fixedCost;
        }

    }
    
    public ProductionItem? CurrentProduction { get; set; } = null;
    public List<ProductionItem> ProductionQueue = new List<ProductionItem>();

    public void ScheduleProductionItem(CatalogItem productionItem, float fixedCost = 0f)
    {
        ProductionQueue.Add(new ProductionItem(productionItem, fixedCost));
        if (CurrentProduction == null)
        {
            UpdateProduction();
        }
    }
```

In `ContinueProduction` change `CurrentProduction?.Item.cost` to `CurrentProduction?.Cost` (line 875), and in `CompleteProduction` (line 886) `CurrentProduction?.Item.cost` to `CurrentProduction?.Cost`.

In `PlanetDetailUIController.cs:195` change `_planet.CurrentProduction?.Item.cost.ToString() ?? "X"` to `_planet.CurrentProduction?.Cost.ToString() ?? "X"`.

Leave `IndustryMatrix.cs` (`Item.cost` as a choice sort key) unchanged.

- [ ] **Step 5: Save and load the fixed cost**

`SaveLoadSystem.cs`, `ProductionSave`:

```csharp
        [Serializable]
        public struct ProductionSave
        {
            public string Name;
            public float Progress;
            // 0 (also what an older save reads back as) means "use the catalog cost".
            public float FixedCost;

            public ProductionSave(Planet.ProductionItem? productionItem)
            {
                Name = productionItem?.Item.itemName;
                Progress = productionItem?.Progress ?? 0.0f;
                FixedCost = productionItem?.FixedCost ?? 0.0f;
            }
        }
```

`GameAIMap.SetPlanetSimulationStats`: in the `CurrentProduction = new Planet.ProductionItem { ... }` initializer add `FixedCost = planetStatus.currentProduction?.FixedCost ?? 0.0f,`; in the queue loop's initializer add `FixedCost = production.FixedCost,`.

- [ ] **Step 6: Fix the cost when the order runs**

In `GameAI.ExecuteOrder`, replace the `OrderTypeIndustrySetProduction` case (lines 216-221) with:

```csharp
                    case GameAIOrder.OrderType.OrderTypeIndustrySetProduction:
                        var productionAI = Gameboard.Instance.players[executableOrder.PlayerId].playerAI;
                        var newProductionItem = productionAI.ProductionCatalog.catalogItems
                            .Find(x => x.itemName == executableOrder.Data.ToString());
                        var baseWarship = productionAI.ProductionCatalog.catalogItems.Find(x => x.subType == "Warship");
                        var fixedCost = WarshipCosts.ProductionCost(newProductionItem, targetPlanet,
                            executableOrder.PlayerId, productionAI.ResearchCatalog.catalogItems,
                            baseWarship != null ? baseWarship.cost : 0f,
                            GameAIMap.GameAIConstants.warshipImprovementCostFactor);
                        targetPlanet.ScheduleProductionItem(newProductionItem, fixedCost);
                        break;
```

- [ ] **Step 7: Verify**

Grep `Item\.cost` under `Assets/Flatspace`: the only remaining hits must be `ProductionItem.Cost`'s own fallback and `IndustryMatrix.cs`. Ask the user to recompile and run `Run Warship Self-Check`: expect `ALL PASSED`. Also run `FlatSpace → AI → Run Ship Transport Self-Check` (it exercises planets/production) to confirm no regression.

- [ ] **Step 8: Commit**

```bash
git add Assets/Flatspace/GameAI/WarshipCosts.cs Assets/Flatspace/GameAI/WarshipCosts.cs.meta Assets/Flatspace/GameAI/GameAIConstants.cs Assets/Flatspace/Objects/Planets/Planet.cs Assets/Flatspace/SaveSystem/SaveLoadSystem.cs Assets/Flatspace/GameAI/GameAIMap.cs Assets/Flatspace/GameAI/GameAI.cs Assets/Flatspace/UI/MainGameScreenUI/PlanetDetailUIController.cs Assets/Editor/WarshipSelfCheck.cs
git commit -m "feat(ships): warship build cost scales with researched improvements

Co-Authored-By: Claude Sonnet 5.5 <noreply@anthropic.com>"
```

---

### Task 3: Update Warship

**Files:**
- Modify: `Assets/Flatspace/Objects/Planets/Planet.cs` (target finder, apply, production completion branch)
- Modify: `Assets/Flatspace/GameAI/WarshipCosts.cs` (WarshipUpdate case)
- Modify: `Assets/Flatspace/GameAI/PlayerAI.cs:811-831` (weights), `:940-967` (situational weight)
- Modify: `Assets/Flatspace/Catalogs/Production/Production Catalog.json` (new item after Warship)
- Modify: `Assets/Editor/WarshipSelfCheck.cs`, `Assets/Editor/PlayerKnowledgeSelfCheck.cs` (weight-table assertions)

**Interfaces:**
- Consumes: `WarshipStats.ResearchedNames`, `WarshipCosts`, `Planet.ProductionItem` (Tasks 1-2).
- Produces:
  - `Ship Planet.FindWarshipUpdateTarget(int owner, ICollection<string> researchedNames)` (docked warship of that owner with the most missing improvements, null if none is missing anything)
  - `bool Planet.ApplyWarshipUpdate(int owner, ICollection<string> researchedNames)`
  - `PlayerAI` weight for subType `"WarshipUpdate"`: Expand 1.5, Consolidate 2.5, situational 0 when no upgradable docked ship

- [ ] **Step 1: Add the checks**

In `RunChecks()` add `ok &= RunUpdateWarshipCheck();`, and add these helpers/checks to `WarshipSelfCheck`:

```csharp
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
```

Also add `ok &= RunUpdateWarshipAIWeightCheck();` to `RunChecks()`. (Do not destroy `research` twice: the AI-weight check hands the list to the catalog and destroys the items itself.)

- [ ] **Step 2: Planet methods and completion branch**

In `Planet.cs`, after `HasDockedShip` (line 910-913) add:

```csharp
    /// <summary>
    /// The docked warship this owner should upgrade next: the one missing the most of the researched warship
    /// improvements. Null when every owned warship already has them all (or there are none).
    /// </summary>
    public Ship FindWarshipUpdateTarget(int owner, ICollection<string> researchedNames)
    {
        Ship best = null;
        var bestMissing = 0;
        foreach (var ship in DockedShips)
        {
            if (ship.Kind != Ship.ShipKind.WarShip || ship.Owner != owner) continue;
            var missing = researchedNames.Count(n => !ship.ResearchSnapshot.Contains(n));
            if (missing > bestMissing)
            {
                best = ship;
                bestMissing = missing;
            }
        }
        return best;
    }

    /// <summary>Adds every researched improvement the target ship lacks. False when no ship needed it.</summary>
    public bool ApplyWarshipUpdate(int owner, ICollection<string> researchedNames)
    {
        var ship = FindWarshipUpdateTarget(owner, researchedNames);
        if (ship == null) return false;
        foreach (var name in researchedNames)
            if (!ship.ResearchSnapshot.Contains(name))
                ship.ResearchSnapshot.Add(name);
        return true;
    }
```

In `StageCompletedProductionItem`, after the `"Warship"` branch, add:

```csharp
        else if (CurrentProduction?.Item.subType == "WarshipUpdate")
        {
            // Needs Gameboard.Instance, like DockNewShip's snapshot; the self-check calls ApplyWarshipUpdate directly.
            if (Owner >= 0 && Owner < Gameboard.Instance.players.Count)
                ApplyWarshipUpdate(Owner, WarshipStats.ResearchedNames(
                    Gameboard.Instance.players[Owner].playerAI.ResearchCatalog.catalogItems));
        }
```

(If `Planet.cs` lacks `using FlatSpace.AI;` add it. If the ship left before completion and none other needs it, `ApplyWarshipUpdate` returns false and the industry is spent, as in the spec.)

- [ ] **Step 3: `WarshipUpdate` cost**

In `WarshipCosts.ProductionCost` add before `default`:

```csharp
                case "WarshipUpdate":
                {
                    var researched = WarshipStats.ResearchedNames(research);
                    var target = planet != null ? planet.FindWarshipUpdateTarget(owner, researched) : null;
                    if (target == null) return item.cost;   // nothing to upgrade right now: fall back to the catalog cost
                    var missing = researched.Count(n => !target.ResearchSnapshot.Contains(n));
                    return UpdateCost(baseWarshipCost, factor, missing);
                }
```

- [ ] **Step 4: AI weights and situational multiplier**

In `PlayerAI.cs` add `{ "WarshipUpdate", 1.5f },  // same as Warship` to `ExpandIndustryWeights` (after the `Warship` entry, line 819) and `{ "WarshipUpdate", 2.5f },  // same as Warship` to `ConsolidateIndustryWeights` (line 830).

In `GetIndustrySituationalWeightMultiplier`, before the `Warship`/Consolidate branch add:

```csharp
                if (item.subType == "WarshipUpdate"
                    && AIMap.GetPlanet(planetName).FindWarshipUpdateTarget(
                        Player.playerID, WarshipStats.ResearchedNames(ResearchCatalog.catalogItems)) == null)
                    return 0f;                           // nothing docked here is missing an improvement — do not offer it
```

- [ ] **Step 5: Production catalog item**

In `Production Catalog.json`, directly after the `"Warship"` item (which ends with `"maintenanceCost": 5.0` and `},` at lines 159-160), insert:

```json
        {
            "itemName": "Update Warship",
            "description": "Add every researched improvement a docked warship is missing",
            "type": "Ship",
            "subType" : "WarshipUpdate",
            "tier" : 1,
            "cost": 50.0,
            "effect": "UpdateWarship",
            "requiredTech": "Warship 1",
            "researched": false,
            "maintenanceCost": 0.0
        },
```

Completing "Warship 1" flips `researched` on every production item whose `requiredTech` matches, so this unlocks with Warship.

- [ ] **Step 6: Extend the weight-table check**

In `PlayerKnowledgeSelfCheck.cs` near line 585 (the loop over the six subtypes) add a second assertion after it that `PlayerAI.GetIndustryStrategyWeight` for a `WarshipUpdate` item equals Expand 1.5 / Consolidate 2.5 (mirror the existing assertion's style: create a `CatalogItem`, set `subType = "WarshipUpdate"`, destroy it in `finally`).

- [ ] **Step 7: Verify**

Ask the user to recompile and run `Run Warship Self-Check` (expect `ALL PASSED`), then `Run Player Knowledge Self-Check` and `Run Ship Transport Self-Check` (no regression). Then a short Play-mode run with `_logAIEvents` on: `Update Warship` should appear as `ProductionSet` once warship research is done and some warship is docked.

- [ ] **Step 8: Commit**

```bash
git add Assets/Flatspace/Objects/Planets/Planet.cs Assets/Flatspace/GameAI/WarshipCosts.cs Assets/Flatspace/GameAI/PlayerAI.cs "Assets/Flatspace/Catalogs/Production/Production Catalog.json" Assets/Editor/WarshipSelfCheck.cs Assets/Editor/PlayerKnowledgeSelfCheck.cs
git commit -m "feat(ships): add Update Warship production item

Co-Authored-By: Claude Sonnet 5.5 <noreply@anthropic.com>"
```

---

### Task 4: `BlockadeSystem` value and route logic

**Files:**
- Create: `Assets/Flatspace/GameAI/BlockadeSystem.cs` (+ `.meta`)
- Modify: `Assets/Editor/WarshipSelfCheck.cs`

**Interfaces:**
- Consumes: `WarshipStats.Offense`, `GameAIMap.GetPath/GetPlanet`, `Planet.DockedShips`, `Planet.NoOwner`.
- Produces (`FlatSpace.AI.BlockadeSystem`):
  - `BlockadeSystem(GameAIMap map, IEnumerable<CatalogItem> researchItems)`
  - `float DockedOffense(Planet planet, int owner)`
  - `float Value(Planet planet, int orderOwner, out int blocker)`
  - `struct RouteNode { string Name; float Fraction; }`, `List<RouteNode> Route(string origin, string target)`
  - `static float Progress(int timingDelay, int totalDelay)`
  - `List<RouteNode> PassedNodes(GameAI.GameAIOrder order)` (nodes passed this turn; called after the delay was decremented)
  - `void Apply(List<GameAI.GameAIOrder> orders, int turnNumber)` is added in Task 5.

- [ ] **Step 1: Add the checks**

In `RunChecks()` add `ok &= RunBlockadeValueCheck();` and `ok &= RunBlockadeRouteCheck();`, and add:

```csharp
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
```

- [ ] **Step 2: Create `BlockadeSystem.cs`**

```csharp
using System.Collections.Generic;
using System.Linq;
using Flatspace.Objects.Production;
using UnityEngine;

namespace FlatSpace.AI
{
    /// <summary>
    /// Blockade: at a planet, the value against an order's owner is the largest single OTHER player's docked warship
    /// offense minus the order owner's own docked offense there, counted only when positive. Only docked ships count.
    /// Pure (no Gameboard.Instance): it reads a GameAIMap and a research item list, so self-checks drive it directly.
    /// </summary>
    public class BlockadeSystem
    {
        public struct RouteNode
        {
            public string Name;
            public float Fraction;   // how far along the route this node is: cumulative cost / total cost, target = 1
        }

        private const float Epsilon = 0.0001f;

        private readonly GameAIMap _map;
        private readonly WarshipStats _stats;

        public BlockadeSystem(GameAIMap map, IEnumerable<CatalogItem> researchItems)
        {
            _map = map;
            _stats = new WarshipStats(researchItems);
        }

        public float DockedOffense(Planet planet, int owner)
        {
            var sum = 0f;
            foreach (var ship in planet.DockedShips)
                if (ship.Kind == Ship.ShipKind.WarShip && ship.Owner == owner)
                    sum += _stats.Offense(ship.Template, ship.ResearchSnapshot);
            return sum;
        }

        /// <summary>The blockade against `orderOwner` at `planet`; `blocker` is the player imposing it (or NoOwner).</summary>
        public float Value(Planet planet, int orderOwner, out int blocker)
        {
            blocker = Planet.NoOwner;
            var own = DockedOffense(planet, orderOwner);
            var best = 0f;
            var others = planet.DockedShips
                .Where(s => s.Kind == Ship.ShipKind.WarShip && s.Owner != orderOwner && s.Owner != Planet.NoOwner)
                .Select(s => s.Owner).Distinct().OrderBy(o => o);
            foreach (var other in others)
            {
                var value = DockedOffense(planet, other) - own;
                if (value > best)   // strictly greater: ties keep the lowest player id
                {
                    best = value;
                    blocker = other;
                }
            }
            return best;
        }

        /// <summary>
        /// Every node after the origin, in order, with its fraction of the trip. When no route exists (FindPath
        /// returns a 1-node stub, or a name is unknown / the same) only the target is listed.
        /// </summary>
        public List<RouteNode> Route(string origin, string target)
        {
            var only = new List<RouteNode> { new RouteNode { Name = target, Fraction = 1f } };
            if (origin == target || _map.GetPlanet(origin) == null || _map.GetPlanet(target) == null) return only;

            var nodes = _map.GetPath(origin, target).PathNodes;
            if (nodes.Count < 2) return only;

            var cumulative = new float[nodes.Count];
            for (var i = 1; i < nodes.Count; i++)
            {
                var edge = 0f;
                foreach (var connection in nodes[i - 1].Connections)
                    if (connection.NodeName == nodes[i].Name)
                    {
                        edge = connection.Cost;
                        break;
                    }
                cumulative[i] = cumulative[i - 1] + edge;
            }

            var total = cumulative[nodes.Count - 1];
            var result = new List<RouteNode>();
            for (var i = 1; i < nodes.Count; i++)
                result.Add(new RouteNode
                {
                    Name = nodes[i].Name,
                    Fraction = total > 0f ? cumulative[i] / total : (float)i / (nodes.Count - 1),
                });
            return result;
        }

        /// <summary>0 when an order is just launched, 1 when it has arrived.</summary>
        public static float Progress(int timingDelay, int totalDelay)
            => totalDelay <= 0 ? 1f : Mathf.Clamp01(1f - (float)timingDelay / totalDelay);

        /// <summary>
        /// The route nodes an order passed this turn. Call after the order's TimingDelay was decremented: the previous
        /// turn's progress is derived from TimingDelay + 1, so a node is reported exactly once.
        /// </summary>
        public List<RouteNode> PassedNodes(GameAI.GameAIOrder order)
        {
            var now = Progress(order.TimingDelay, order.TotalDelay);
            var previous = order.TotalDelay <= 0 ? 0f : Progress(order.TimingDelay + 1, order.TotalDelay);
            return Route(order.Origin, order.Target)
                .Where(n => n.Fraction > previous + Epsilon && n.Fraction <= now + Epsilon)
                .ToList();
        }
    }
}
```

- [ ] **Step 3: Verify and commit**

Ask the user to recompile and run `Run Warship Self-Check`: expect `ALL PASSED`.

```bash
git add Assets/Flatspace/GameAI/BlockadeSystem.cs Assets/Flatspace/GameAI/BlockadeSystem.cs.meta Assets/Editor/WarshipSelfCheck.cs
git commit -m "feat(blockade): blockade value and route-progress logic

Co-Authored-By: Claude Sonnet 5.5 <noreply@anthropic.com>"
```

---

### Task 5: Blockade effects on orders, logging and turn hook

**Files:**
- Modify: `Assets/Flatspace/GameAI/BlockadeSystem.cs` (`Apply`)
- Modify: `Assets/Flatspace/Diagnostics/AITuningLogger.cs` (two log methods)
- Modify: `Assets/Flatspace/GameAI/GameAI.cs:141-149` (hook)
- Modify: `Assets/Editor/WarshipSelfCheck.cs`

**Interfaces:**
- Consumes: everything from Task 4; `Planet.SetPopulationTransferInProgress(int, bool)`, `Planet.FoodShipmentIncoming`, `Planet.GrotsitsShipmentIncoming`.
- Produces: `void BlockadeSystem.Apply(List<GameAI.GameAIOrder> orders, int turnNumber)`; `AITuningLogger.LogBlockade(turn, orderOwner, planet, blocker, value)`, `AITuningLogger.LogOrderBlocked(turn, orderOwner, orderType, planet, remaining)`.

Behaviour recap (spec section 4): only `OrderTypePopulationTransport` (removes it and its `OrderTypeColonyFoodRider` twin and clears the target's population-transfer flag), `OrderTypeFoodTransport` and `OrderTypeGrotsitsTransport` (reduced by the value at each passed node, removed at <= 0 with the incoming flag cleared) are affected. Limitation to record: orders with `TotalDelay <= 0` are executed by `ProcessNewOrders` and never reach this hook.

- [ ] **Step 1: Add the checks**

In `RunChecks()` add `ok &= RunBlockadeEffectsCheck();` and add:

```csharp
    public static bool RunBlockadeEffectsCheck()
    {
        var ok = true;
        var go = new GameObject("WarshipSelfCheckMap_BlockadeEffects");
        var template = MakeTemplate();
        var constants = MakeConstants(template);
        var research = MakeResearch();
        try
        {
            var map = BuildLine(go, constants);
            var a = map.GetPlanet("A"); var b = map.GetPlanet("B"); var c = map.GetPlanet("C");
            var blockade = new BlockadeSystem(map, research);
            var colonyType = GameAI.GameAIOrder.OrderType.OrderTypePopulationTransport;
            var riderType = GameAI.GameAIOrder.OrderType.OrderTypeColonyFoodRider;
            var foodType = GameAI.GameAIOrder.OrderType.OrderTypeFoodTransport;

            // Colony ship A -> C, B passed this turn (2 of 4 turns left). Player 1 holds B with 2 warships (20).
            DockWarships(b, 1, 2);
            var orders = new List<GameAI.GameAIOrder>
            {
                MakeOrder(colonyType, 0, "A", "C", 2, 4, 1),
                MakeOrder(riderType, 0, "A", "C", 2, 4, 10f),
            };
            c.SetPopulationTransferInProgress(0);
            blockade.Apply(orders, 1);
            ok &= Check(orders.Count == 0, "a positive blockade at a passed node removes the colonist and its food rider");
            ok &= Check(!c.IsPopulationTransferInProgress(0), "and clears the target's population-transfer flag");

            // The owner's own ships cancel it: player 0 also has 2 warships at B.
            DockWarships(b, 0, 2);
            orders = new List<GameAI.GameAIOrder> { MakeOrder(colonyType, 0, "A", "C", 2, 4, 1) };
            blockade.Apply(orders, 1);
            ok &= Check(orders.Count == 1, "equal docked offense of the order owner: no blockade");

            // A blockade only at the ORIGIN never affects the order.
            var go2 = new GameObject("WarshipSelfCheckMap_BlockadeOrigin");
            try
            {
                var map2 = BuildLine(go2, constants);
                DockWarships(map2.GetPlanet("A"), 1, 5);
                var blockade2 = new BlockadeSystem(map2, research);
                orders = new List<GameAI.GameAIOrder>
                {
                    MakeOrder(colonyType, 0, "A", "C", 2, 4, 1),
                    MakeOrder(foodType, 0, "A", "C", 2, 4, 30f),
                };
                blockade2.Apply(orders, 1);
                ok &= Check(orders.Count == 2, "a blockade at the origin does not stop what is leaving it");

                // A node already passed on an earlier turn is not checked again (1 turn left: progress .75, B at .5).
                var go3 = new GameObject("WarshipSelfCheckMap_BlockadePassed");
                try
                {
                    var map3 = BuildLine(go3, constants);
                    DockWarships(map3.GetPlanet("B"), 1, 5);
                    var blockade3 = new BlockadeSystem(map3, research);
                    orders = new List<GameAI.GameAIOrder> { MakeOrder(colonyType, 0, "A", "C", 1, 4, 1) };
                    blockade3.Apply(orders, 1);
                    ok &= Check(orders.Count == 1, "a node passed on an earlier turn is not blockaded retroactively");
                }
                finally { Object.DestroyImmediate(go3); }
            }
            finally { Object.DestroyImmediate(go2); }

            // Food: 30 shipped A -> C. Player 1 has 20 at B (passed now) then 20 at C (arrival).
            var go4 = new GameObject("WarshipSelfCheckMap_BlockadeFood");
            try
            {
                var map4 = BuildLine(go4, constants);
                DockWarships(map4.GetPlanet("B"), 1, 2);
                DockWarships(map4.GetPlanet("C"), 1, 2);
                map4.GetPlanet("C").FoodShipmentIncoming = true;
                var blockade4 = new BlockadeSystem(map4, research);
                var food = MakeOrder(foodType, 0, "A", "C", 2, 4, 30f);
                var grot = MakeOrder(GameAI.GameAIOrder.OrderType.OrderTypeGrotsitsTransport, 0, "A", "C", 2, 4, 15f);
                var ship = MakeOrder(GameAI.GameAIOrder.OrderType.OrderTypeShipTransport, 0, "A", "C", 2, 4, 3);
                orders = new List<GameAI.GameAIOrder> { food, grot, ship };
                map4.GetPlanet("C").GrotsitsShipmentIncoming = true;

                blockade4.Apply(orders, 1);
                ok &= Check(orders.Contains(food) && Near(System.Convert.ToSingle(food.Data), 10f),
                    "food is reduced by the blockade value at each passed node: 30 - 20 = 10");
                ok &= Check(!orders.Contains(grot) && !map4.GetPlanet("C").GrotsitsShipmentIncoming,
                    "grotsits 15 - 20 <= 0: the order is removed and the incoming flag cleared");
                ok &= Check(orders.Contains(ship), "ship transport orders are unaffected");

                food.TimingDelay = 0;   // arrival turn: C is passed, and player 1's 20 there exceeds the remaining 10
                blockade4.Apply(orders, 2);
                ok &= Check(!orders.Contains(food) && !map4.GetPlanet("C").FoodShipmentIncoming,
                    "the remaining 10 - 20 <= 0 at the target: removed, food flag cleared");
            }
            finally { Object.DestroyImmediate(go4); }
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
```

Note `Planet.IsPopulationTransferInProgress(int)` is used by `GameAI.ExecuteOrder`; if its access is not public, make it public (self-check rule).

- [ ] **Step 2: Logger methods**

In `AITuningLogger.cs` after `LogWarshipBoost` add:

```csharp
    /// <summary>A blockade was found at a planet an order passed: T&lt;turn&gt;|P&lt;order owner&gt;|Blockade|planet|blocker|value.</summary>
    public static void LogBlockade(int turnNumber, int playerId, string planetName, int blocker, float value)
    {
        if (_currentLogPath == null) return;
        AppendLines(new List<string> { FormatLine(turnNumber, playerId, "Blockade", planetName, blocker.ToString(),
            value.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture)) });
    }

    /// <summary>An order was cut by a blockade: T&lt;turn&gt;|P&lt;order owner&gt;|OrderBlocked|orderType|planet|amountRemaining (0 = removed).</summary>
    public static void LogOrderBlocked(int turnNumber, int playerId, string orderType, string planetName, float remaining)
    {
        if (_currentLogPath == null) return;
        AppendLines(new List<string> { FormatLine(turnNumber, playerId, "OrderBlocked", orderType, planetName,
            remaining.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture)) });
    }
```

- [ ] **Step 3: `Apply`**

Add to `BlockadeSystem` (inside the class):

```csharp
        /// <summary>
        /// Once per turn, after in-flight delays were decremented and before orders execute: applies blockades to the
        /// orders that passed a planet this turn. Colony orders are removed (colonist and food rider are lost, the
        /// origin already paid for them); food and grotsits shipments lose the blockade value at every blockaded
        /// node they pass and are removed when nothing is left.
        /// </summary>
        public void Apply(List<GameAI.GameAIOrder> orders, int turnNumber)
        {
            foreach (var order in orders.ToList())
            {
                if (!orders.Contains(order)) continue;   // already removed with its twin
                switch (order.Type)
                {
                    case GameAI.GameAIOrder.OrderType.OrderTypePopulationTransport:
                        ApplyToColony(orders, order, turnNumber);
                        break;
                    case GameAI.GameAIOrder.OrderType.OrderTypeFoodTransport:
                    case GameAI.GameAIOrder.OrderType.OrderTypeGrotsitsTransport:
                        ApplyToShipment(orders, order, turnNumber);
                        break;
                }
            }
        }

        private void ApplyToColony(List<GameAI.GameAIOrder> orders, GameAI.GameAIOrder order, int turnNumber)
        {
            foreach (var node in PassedNodes(order))
            {
                var value = Value(_map.GetPlanet(node.Name), order.PlayerId, out var blocker);
                if (value <= 0f) continue;

                AITuningLogger.LogBlockade(turnNumber, order.PlayerId, node.Name, blocker, value);
                orders.RemoveAll(o => o.PlayerId == order.PlayerId && o.Origin == order.Origin && o.Target == order.Target
                    && (o.Type == GameAI.GameAIOrder.OrderType.OrderTypePopulationTransport
                        || o.Type == GameAI.GameAIOrder.OrderType.OrderTypeColonyFoodRider));
                _map.GetPlanet(order.Target)?.SetPopulationTransferInProgress(order.PlayerId, false);
                AITuningLogger.LogOrderBlocked(turnNumber, order.PlayerId, order.Type.ToString(), node.Name, 0f);
                return;
            }
        }

        private void ApplyToShipment(List<GameAI.GameAIOrder> orders, GameAI.GameAIOrder order, int turnNumber)
        {
            foreach (var node in PassedNodes(order))
            {
                var value = Value(_map.GetPlanet(node.Name), order.PlayerId, out var blocker);
                if (value <= 0f) continue;

                AITuningLogger.LogBlockade(turnNumber, order.PlayerId, node.Name, blocker, value);
                var remaining = System.Convert.ToSingle(order.Data) - value;
                AITuningLogger.LogOrderBlocked(turnNumber, order.PlayerId, order.Type.ToString(), node.Name,
                    System.Math.Max(0f, remaining));
                if (remaining <= 0f)
                {
                    orders.Remove(order);
                    var target = _map.GetPlanet(order.Target);
                    if (target != null)
                    {
                        if (order.Type == GameAI.GameAIOrder.OrderType.OrderTypeFoodTransport)
                            target.FoodShipmentIncoming = false;
                        else
                            target.GrotsitsShipmentIncoming = false;
                    }
                    return;
                }
                order.Data = remaining;
            }
        }
```

- [ ] **Step 4: Hook into the turn**

In `GameAI.cs`, `ProcessCurrentOrders`, right after the decrement `foreach` (line 146) and before `var executableOrders`, add:

```csharp
                ApplyBlockades();
```

and add this method next to `ProcessCurrentOrders`:

```csharp
            // Every player's research catalog holds the same items, so player 0's supplies the stat lines.
            private void ApplyBlockades()
            {
                if (Gameboard.Instance.players.Count == 0) return;
                var research = Gameboard.Instance.players[0].playerAI.ResearchCatalog.catalogItems;
                new BlockadeSystem(GameAIMap, research).Apply(CurrentAIOrders, Gameboard.Instance.TurnNumber);
            }
```

- [ ] **Step 5: Verify and commit**

Ask the user to recompile, run `Run Warship Self-Check` (expect `ALL PASSED`) and `Run Ship Transport Self-Check` (no regression). Then a Play-mode run of ~150 turns on a multi-player board with `_logAIEvents` on: expect `Blockade|` and `OrderBlocked|` lines only after warship research and some warships docked, and no exceptions in the Console.

```bash
git add Assets/Flatspace/GameAI/BlockadeSystem.cs Assets/Flatspace/GameAI/GameAI.cs Assets/Flatspace/Diagnostics/AITuningLogger.cs Assets/Editor/WarshipSelfCheck.cs
git commit -m "feat(blockade): blockade removes colony orders and reduces shipments

Co-Authored-By: Claude Sonnet 5.5 <noreply@anthropic.com>"
```

---

### Task 6: "Run All AI Self-Checks" menu item

**Files:**
- Create: `Assets/Editor/AllAISelfChecks.cs` (+ `.meta`)
- Modify: `Assets/Editor/PlayerKnowledgeSelfCheck.cs:12-34`
- Modify: `Assets/Editor/PlayerAIResourceSelfCheck.cs:9-25`
- Modify: `Assets/Editor/ShipTransportSelfCheck.cs:13-43`
- Modify: `Assets/Editor/DistributionCenterSelfCheck.cs:10-24`

**Interfaces:**
- Consumes: `WarshipSelfCheck.RunChecks()` (Task 1).
- Produces: `public static bool RunChecks()` on each of `PlayerKnowledgeSelfCheck`, `PlayerAIResourceSelfCheck`, `ShipTransportSelfCheck`, `DistributionCenterSelfCheck`; menu `FlatSpace/AI/Run All AI Self-Checks`.

Each existing `Run()` becomes `RunChecks()` (same body, returns `ok` as its last statement) and `Run()` stays as the menu item calling it. Behaviour of the individual menu items is unchanged.

- [ ] **Step 1: Refactor the four suites**

`DistributionCenterSelfCheck.cs` lines 10-24 become:

```csharp
    [MenuItem("FlatSpace/AI/Run Distribution Center Self-Check")]
    public static void Run() => RunChecks();

    public static bool RunChecks()
    {
        var ok = RunBoardSizeGatingCheck();
        ok &= RunCoverageMaximizingSelectionCheck();
        ok &= RunTieBreakByDistanceCheck();
        ok &= RunNoReachableCandidateCheck();
        ok &= RunStickySelectionCheck();
        ok &= RunPruningCheck();
        ok &= RunCoverageGapCheck();
        ok &= RunSaveRoundTripCheck();
        Debug.Log(ok
            ? "[DistributionCenterSelfCheck] ALL PASSED"
            : "[DistributionCenterSelfCheck] FAILURES (see errors above)");
        return ok;
    }
```

`PlayerAIResourceSelfCheck.cs`: replace lines 9-10 (`[MenuItem(...)]` + `public static void Run()`) with:

```csharp
    [MenuItem("FlatSpace/AI/Run PlayerAI Resource Self-Check")]
    public static void Run() => RunChecks();

    public static bool RunChecks()
```

and add `return ok;` as the last statement of the method (after the closing `Debug.Log(...)`, before the `}` on line 25).

`PlayerKnowledgeSelfCheck.cs`: replace lines 12-13 with the same pattern (`[MenuItem("FlatSpace/AI/Run Player Knowledge Self-Check")]`, `public static void Run() => RunChecks();`, blank line, `public static bool RunChecks()`) and add `return ok;` after the second `Debug.Log` (the float-probe one, ending at line 33), before the closing brace.

`ShipTransportSelfCheck.cs`: replace lines 13-14 with the same pattern (`"FlatSpace/AI/Run Ship Transport Self-Check"`) and add `return ok;` after the summary `Debug.Log(...)` (ending line 42), before the closing brace at line 43.

- [ ] **Step 2: Create `AllAISelfChecks.cs`**

```csharp
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

public static class AllAISelfChecks
{
    [MenuItem("FlatSpace/AI/Run All AI Self-Checks")]
    public static void Run()
    {
        // Every suite runs even after a failure, so one run shows everything that is broken.
        var suites = new List<(string name, System.Func<bool> run)>
        {
            ("Player Knowledge", PlayerKnowledgeSelfCheck.RunChecks),
            ("PlayerAI Resource", PlayerAIResourceSelfCheck.RunChecks),
            ("Ship Transport", ShipTransportSelfCheck.RunChecks),
            ("Distribution Center", DistributionCenterSelfCheck.RunChecks),
            ("Warship", WarshipSelfCheck.RunChecks),
        };

        var failed = new List<string>();
        foreach (var (name, run) in suites)
        {
            bool passed;
            try
            {
                passed = run();
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[AllAISelfChecks] {name} threw: {e}");
                passed = false;
            }
            if (!passed) failed.Add(name);
        }

        Debug.Log(failed.Count == 0
            ? $"[AllAISelfChecks] ALL {suites.Count} SUITES PASSED"
            : $"[AllAISelfChecks] {failed.Count} of {suites.Count} SUITES FAILED: {string.Join(", ", failed)}");
    }
}
```

- [ ] **Step 3: Verify and commit**

Ask the user to recompile and run `FlatSpace → AI → Run All AI Self-Checks`. Expected Console: each suite's own summary line and finally `[AllAISelfChecks] ALL 5 SUITES PASSED`. Also run one individual menu item (e.g. `Run Distribution Center Self-Check`) to confirm it still works alone.

```bash
git add Assets/Editor/AllAISelfChecks.cs Assets/Editor/AllAISelfChecks.cs.meta Assets/Editor/PlayerKnowledgeSelfCheck.cs Assets/Editor/PlayerAIResourceSelfCheck.cs Assets/Editor/ShipTransportSelfCheck.cs Assets/Editor/DistributionCenterSelfCheck.cs
git commit -m "test: add Run All AI Self-Checks menu item

Co-Authored-By: Claude Sonnet 5.5 <noreply@anthropic.com>"
```

---

### Task 7: Documentation and spec corrections

**Files:**
- Modify: `CLAUDE.md`
- Modify: `FUTURE_FEATURES.md`
- Modify: `docs/superpowers/specs/2026-09-28-warship-stats-and-blockade-design.md`

- [ ] **Step 1: `CLAUDE.md`**

In "Tests", add to the menu list: `FlatSpace → AI → Run Warship Self-Check` (`Assets/Editor/WarshipSelfCheck.cs`) and `FlatSpace → AI → Run All AI Self-Checks` (`Assets/Editor/AllAISelfChecks.cs`, runs every AI suite: Player Knowledge, PlayerAI Resource, Ship Transport, Distribution Center, Warship; each suite exposes `public static bool RunChecks()`; add new AI suites to its list). Add a "### Warships and Blockade" section (after Ship Transport) covering: stats derived by `WarshipStats` from `ShipData` + `ResearchSnapshot` with `(Max - base) / tiers` per tier and the effect-key strings; cost `base x (1 + warshipImprovementCostFactor x improvements)` fixed in `ProductionItem.FixedCost` (0 = catalog cost; saved as `ProductionSave.FixedCost`), the accepted quirk that a mid-build research completion gives a slightly better ship than paid for, `IndustryMatrix` cost left as the catalog cost; Update Warship (`WarshipUpdate` subtype, target = most missing, cost base x factor x missing, Warship's table weights, situational 0 when nothing to upgrade); blockade (`BlockadeSystem`: largest single other blockader minus the order owner's docked offense, only positive counts; applied once per turn in `GameAI.ProcessCurrentOrders` after delays decrement; route from `GameAIMap.GetPath`, nodes passed this turn by order progress, origin excluded; colony orders removed with colonist and rider lost, food/grotsits reduced per node and removed at <= 0; zero-delay orders executed by `ProcessNewOrders` are not blockaded; the AI does not yet avoid blockaded routes). Add the two log lines (`Blockade`, `OrderBlocked`) to the AI Tuning Log section.

- [ ] **Step 2: `FUTURE_FEATURES.md`**

Change the "Blockade planet with docked ships" bullet to done wording (`*Done: docked offense blockades colony ships and food/grotsits shipments; see CLAUDE.md "Warships and Blockade".*`) and add a new bullet: **AI avoids blockaded routes and uses blockades** — the AI still launches colony ships and shipments into blockaded routes (wasted), and never moves warships in order to blockade (see Blockade Targets below).

- [ ] **Step 3: Correct the spec**

In the spec, section 4, replace the "Routes." paragraph with: routes come from `GameAIMap.GetPath` (already returns the ordered node list); no precompute change. In "Evaluation." remove "and once more when a new order is created" (a new order has progress 0 and passes no node). Remove the memory risk bullet about node lists. Add a note under section 2 that `IndustryMatrix.Cost` keeps the catalog cost.

- [ ] **Step 4: Commit**

```bash
git add CLAUDE.md FUTURE_FEATURES.md docs/superpowers/specs/2026-09-28-warship-stats-and-blockade-design.md
git commit -m "docs: document warship stats, Update Warship and blockade

Co-Authored-By: Claude Sonnet 5.5 <noreply@anthropic.com>"
```

---

## Self-review notes

- **Spec coverage:** section 1 stats (Task 1), 2 cost (Task 2), 3 Update Warship (Task 3), 4 blockade (Tasks 4-5), 5 saves (Task 2 for fixed cost; stats/blockade derive from existing saved snapshots), 6 self-checks + Run All (Tasks 1-6), 7 docs (Task 7). The logging lines are in Task 5.
- **Type consistency:** `WarshipStats.ResearchedNames` returns `List<string>` and is passed where `ICollection<string>` is expected (Tasks 2, 3, 5); `Planet.ProductionItem.Cost`/`FixedCost` used the same in Tasks 2 and its callers; `BlockadeSystem.PassedNodes` (Task 4) is what `Apply` (Task 5) calls; `MakeOrder`, `BuildLine`, `BuildMap`, `DockWarships`, `MakeConstants`, `MakeSpawn`, `MakeTemplate`, `MakeResearch` are all defined in `WarshipSelfCheck` before use (Tasks 1, 3, 4).
- **Known compile-order note:** Task 2's `ProductionCost` only handles `"Warship"`; Task 3 adds `"WarshipUpdate"` in the same file, so every task compiles on its own.
