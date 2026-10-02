# Connectivity Targeting Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build one shared shortest-path betweenness score (a percentile per planet) and use it for the garrison "high traffic" category, the blockade target ranking, and a Consolidate-only colonization tilt, with tuning-log output for each.

**Architecture:** A pure `PlanetCentrality` counts interior nodes over the all-pairs paths `GameAIMap` already stores and turns them into a 0..1 percentile; `GameAIMap` computes it once at init and exposes `Chokepoint(name)` / `IsChokepoint(name)`. Three consumers read it: `ShipTransportPlanner` (category 4 and the Consolidate garrison seam), `AssaultPlanner.ChooseBlockadeTarget` (one new ranking step) and `PlayerAI.ProcessColonizers` (a cost divisor under Consolidate). Each has a `GameAIConstants` tunable with an in-code default.

**Tech Stack:** Unity 6000.4.1f1 C#, Editor self-checks (`Assets/Editor/*SelfCheck.cs`), `AITuningLogger`. No test framework; Unity cannot be run from Claude Code (see "Verification" below).

**Spec:** `docs/superpowers/specs/2026-10-02-connectivity-targeting-design.md`. Roadmap: `FUTURE_FEATURES.md`, "AI avoids blockaded routes and uses blockades" item (4), and "Weight planet value by connectivity".

## Global Constraints

- Branch: work on a feature branch `connectivity-targeting` in the working tree (no worktree: the user's Unity Editor reads this tree). Commit only the files named in each task; do not push or merge unless the user asks.
- Percentile = `(planets with strictly lower betweenness) / (N - 1)`; N of 0 or 1 gives 0. Chokepoint = `Betweenness > 0 && percentile >= GameAIConstants.chokepointPercentile` (default **0.9**).
- Colonization tilt: Consolidate only; choice cost = `route.Cost / (1 + colonizationChokepointWeight x percentile)`; default weight **0.5**; weight `<= 0` switches it off; Expand is unchanged. The order delay always uses the real `route.Cost`.
- Blockade ranking order: committed offense, recent cut, **chokepoint (higher first)**, smallest offense needed, cheapest path, name. Committed offense and the recent-cut step are not changed. New reason label `Chokepoint`.
- Garrison: category 4 becomes "is a chokepoint" and keeps `garrisonHighTraffic`; under Consolidate `MaintainsGarrison` is "outer, or a colonized chokepoint", round 1 only. **The spare-ship calculation, `HeldPlanets`, `ContestedHolds`, category 5, and `TargetRank` are not changed.**
- `highTrafficConnectionCount` is removed (code, `Assets/GameAIConstantsProductionTypes.asset`, self-check).
- Producer value is NOT built (memory note `producer-value-per-type-deferred`).
- Every new script gets its `.meta` committed with it. A method a self-check calls is `public`, never `internal`.
- Self-check rules: never depend on `Gameboard.Instance`; give every test planet a distinct position; never put an assertion exactly on a float boundary (use thresholds like 0.9, 0.5, 2 against percentiles 0, 0.8, 1).
- New log lines go through `AITuningLogger`'s `FormatLine`, no-op when no match is logging, and need no tracker (each is once-per-match, per-launch or on a 25-turn cadence).
- Match the surrounding code's comment density and naming; namespace `FlatSpace.AI` for GameAI files.

## Verification (applies to every task)

Unity cannot be run from here. The red/green signal is Rider's `mcp__rider__get_file_problems` (pass `rootFolder: "C:/Projects/FlatSpace"`): before the implementation it shows unresolved members in the new test code (RED); after, it shows none (GREEN). Brand-new scripts report "not included in any project" until Unity regenerates the project, and their users show "cannot resolve" cascades; that is not a real defect. The real check is the user focusing the Editor and running `FlatSpace -> AI -> Run Chokepoint Self-Check` and `FlatSpace -> AI -> Run All AI Self-Checks`; ask for that at the end of each task and do not claim a task verified before they report. Self-checks run in an Editor session append stray lines (turns 0-20) to the last match's tuning log; ignore them.

## Review Focus

Failure modes the spec implies but no obvious test covers, most likely first. Each has a test in the task named.

1. A tiny board where the middle of a 3-planet chain is trivially the "top 10%" (percentile 1.0): the score must behave, not crash, and is accepted as a chokepoint. (Task 1, `RunCentralityCheck`)
2. Disconnected components and `FindPath`'s 1-node no-route stub, and a path naming an unknown planet: they add nothing and do not throw. (Task 1)
3. Path order must not change the result (the stored list order is arbitrary): shuffling the paths gives identical scores. (Task 1)
4. A negative `colonizationChokepointWeight` or a `chokepointPercentile` above 1 must mean "off", not an inverted tilt or a crash. (Tasks 2 and 4)
5. An unknown planet name passed to `Chokepoint` / `IsChokepoint` (stale names in orders, saves) returns 0 / false. (Task 1)

## File Structure

| File | Responsibility |
|---|---|
| `Assets/Flatspace/GameAI/PlanetCentrality.cs` (new) | pure betweenness + percentile from paths; no Unity, no `Gameboard` |
| `Assets/Flatspace/GameAI/GameAIMap.cs` | builds it once at init; `Betweenness`, `Chokepoint`, `IsChokepoint`, `TopChokepoints`, `ChokepointSummary` |
| `Assets/Flatspace/GameAI/GameAIConstants.cs` | `chokepointPercentile`, `colonizationChokepointWeight`; removes `highTrafficConnectionCount` |
| `Assets/Flatspace/GameAI/ShipTransportPlanner.cs` | category 4 test, `MaintainsGarrison` |
| `Assets/Flatspace/GameAI/AssaultPlanner.cs` | the ranking step and the `Chokepoint` reason |
| `Assets/Flatspace/GameAI/PlayerAI.cs` | colonization cost divisor, real-cost delay, `ChokepointColonize` log |
| `Assets/Flatspace/GameAI/GameAI.cs` | the 25-turn `ChokepointGarrison` summary |
| `Assets/Flatspace/Objects/Board/GameBoard.cs` | the once-per-match `Chokepoints` board line |
| `Assets/Flatspace/Diagnostics/AITuningLogger.cs` | three new log methods and one pure formatter |
| `Assets/Editor/ChokepointSelfCheck.cs` (new) | centrality, map, garrison, summary and formatter checks; shared hub layout |
| `Assets/Editor/BlockadeBreakSelfCheck.cs` | hub scenario, ranking and colonization checks |
| `Assets/Editor/ShipTransportSelfCheck.cs`, `WarshipSelfCheck.cs`, `PlayerKnowledgeSelfCheck.cs`, `AllAISelfChecks.cs` | fixture neutralisation, old category-4 check, suite registration |
| `CLAUDE.md`, `FUTURE_FEATURES.md`, `.claude/skills/tuning-log/SKILL.md`, the spec | docs (Task 5) |

## Proposed tuning log output (rule 4 of `docs/session_configuration.md`)

Deviation from the spec, ledgered: the periodic line is named `ChokepointGarrison` (the spec reused `Chokepoints`, which would put two formats under one code) and its fields are `<colonized>|<boardTotal>|<shipsOnThem>|<allShips>` (the spec's `held` and `spare` would need planner state; the share of ships on chokepoints answers the same question). Task 5 updates the spec to match.

| Line | Fields; when | Question | Repeat control; added by; test |
|---|---|---|---|
| `T0\|P-1\|Chokepoints\|<planet>=<betweenness>%<percentile>,...` | top 10 planets by betweenness; once per `InitGame` (the last is the real board, like `BoardConfig`) | which planets are chokepoints on this board, without running the script | once per init; Task 5; `AITuningLogger.FormatChokepointList` is checked in `ChokepointSelfCheck` |
| `T<turn>\|P<id>\|ChokepointColonize\|<origin>-><target>\|<routeCost>\|<percentile>\|<nearestTarget>\|<nearestCost>` | each Consolidate colonist launched when the tilt changed the pick away from the nearest candidate of that origin | how often does the tilt matter, how much farther does it reach | per launch, no tracker; Task 4; behaviour covered by `RunChokepointColonizationCheck` (the logger itself has no self-check, by design) |
| `T<turn>\|P<id>\|ChokepointGarrison\|<colonized>\|<boardTotal>\|<shipsOnThem>\|<allShips>` | per player every 25 turns beside `Economy` (colonized = chokepoints the player colonizes) | do chokepoint garrisons take ships from the assault | 25-turn cadence; Task 5; numbers from `GameAIMap.ChokepointSummary`, checked in `ChokepointSelfCheck` |
| `BlockadeTarget\|...\|Chokepoint` | existing line, new reason value | how often the new ranking step decides | existing `BlockadeTargetTracker`; Task 3 |

The `tuning-log` skill and the "AI Tuning Log" section of `CLAUDE.md` get the matching text in Task 5.

---

### Task 1: `PlanetCentrality`, map exposure, tunables, fixture neutralisation, self-check scaffold

**Files:**
- Create: `Assets/Flatspace/GameAI/PlanetCentrality.cs` (+ `.meta`)
- Create: `Assets/Editor/ChokepointSelfCheck.cs` (+ `.meta`)
- Modify: `Assets/Flatspace/GameAI/GameAIMap.cs` (field near line 31; init after line 120; new methods near `GetNeighbours`)
- Modify: `Assets/Flatspace/GameAI/GameAIConstants.cs` (after the Ship transport block)
- Modify: `Assets/Editor/AllAISelfChecks.cs` (suite list)
- Modify: `Assets/Editor/WarshipSelfCheck.cs` (`MakeConstants`, line 225), `Assets/Editor/PlayerKnowledgeSelfCheck.cs` (constants blocks at about lines 428 and 708)

**Interfaces:**
- Produces: `PlanetCentrality.Compute(IEnumerable<string> planetNames, IEnumerable<IReadOnlyList<string>> paths)`; instance members `int Betweenness(string name)`, `float Percentile(string name)`, `IEnumerable<(string name, int betweenness, float percentile)> Top(int count)`.
- Produces: `GameAIMap.Betweenness(string)`, `float Chokepoint(string)`, `bool IsChokepoint(string)`, `IEnumerable<(string name, int betweenness, float percentile)> TopChokepoints(int count)`, `(int colonized, int boardTotal, int shipsOnThem, int allShips) ChokepointSummary(int playerId)` (the last is added in Task 5; the others here).
- Produces: `GameAIConstants.chokepointPercentile` (0.9f), `GameAIConstants.colonizationChokepointWeight` (0.5f).
- Produces: `ChokepointSelfCheck.Spawn(string, float, float, IEnumerable<string>)` and `ChokepointSelfCheck.HubSpawns()`, both `public static`, used by later tasks.

- [ ] **Step 1: Create the feature branch**

```bash
cd /c/Projects/FlatSpace && git checkout -b connectivity-targeting && git status --short
```
Expected: `Switched to a new branch 'connectivity-targeting'`; the only pre-existing change is the deleted `.idea/.idea.FlatSpace/.idea/indexLayout.xml` (leave it unstaged).

- [ ] **Step 2: Write the failing self-check (pure centrality and map checks)**

Create `Assets/Editor/ChokepointSelfCheck.cs`:

```csharp
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using FlatSpace.AI;
using Flatspace.Objects.Production;

public static class ChokepointSelfCheck
{
    [MenuItem("FlatSpace/AI/Run Chokepoint Self-Check")]
    public static void Run() => RunChecks();

    public static bool RunChecks()
    {
        var ok = RunCentralityCheck();
        ok &= RunMapCentralityCheck();
        Debug.Log(ok
            ? "[ChokepointSelfCheck] ALL PASSED"
            : "[ChokepointSelfCheck] FAILURES (see errors above)");
        return ok;
    }

    private static bool Check(bool condition, string label)
    {
        if (!condition) Debug.LogError($"[ChokepointSelfCheck] FAIL: {label}");
        return condition;
    }

    private static bool Near(float a, float b) => Mathf.Abs(a - b) < 0.001f;

    private static IReadOnlyList<string> P(params string[] nodes) => nodes;

    // Every planet needs a distinct position (PathingSystem.FindPath tie-break fragility).
    public static PlanetSpawnData Spawn(string name, float x, float y, IEnumerable<string> connections = null)
    {
        var resourceData = ScriptableObject.CreateInstance<PlanetResourceData>();
        resourceData._initialPopulation = 0;
        resourceData._maxPopulation = 5;
        resourceData._baseFoodProduction = 1f;

        var spawn = ScriptableObject.CreateInstance<PlanetSpawnData>();
        spawn._planetName = name;
        spawn._planetPosition = new Vector3(x, y, 0f);
        spawn._planetType = Planet.PlanetType.PlanetTypeNormal;
        spawn._resourceData = resourceData;
        spawn._connections = connections != null ? new List<string>(connections) : new List<string>();
        return spawn;
    }

    // A(0,0) - H(100,0); H - X1, X2, X3; A - Y(0,80). A tree, so every shortest path is unique:
    // H is on 9 paths (betweenness 9, percentile 1), A on 4 (0.8), the other four planets are leaves (0).
    public static List<PlanetSpawnData> HubSpawns() => new List<PlanetSpawnData>
    {
        Spawn("A", 0f, 0f, new[] { "H", "Y" }),
        Spawn("H", 100f, 0f, new[] { "A", "X1", "X2", "X3" }),
        Spawn("X1", 200f, 0f, new[] { "H" }),
        Spawn("X2", 100f, -100f, new[] { "H" }),
        Spawn("X3", 100f, 100f, new[] { "H" }),
        Spawn("Y", 0f, 80f, new[] { "A" }),
    };

    public static GameAIConstants Constants(float chokepointPercentile = 0.9f)
    {
        var c = ScriptableObject.CreateInstance<GameAIConstants>();
        c.defaultTravelSpeed = 1f;
        c.garrisonHighTraffic = 2;
        c.chokepointPercentile = chokepointPercentile;
        return c;
    }

    // Interior nodes of each path score; endpoints do not. Percentile = strictly lower / (N - 1).
    public static bool RunCentralityCheck()
    {
        var ok = true;

        // Line A-B-C-D, one path per pair.
        var line = new[]
        {
            P("A", "B"), P("A", "B", "C"), P("A", "B", "C", "D"), P("B", "C"), P("B", "C", "D"), P("C", "D"),
        };
        var lineScore = PlanetCentrality.Compute(new[] { "A", "B", "C", "D" }, line);
        ok &= Check(lineScore.Betweenness("B") == 2 && lineScore.Betweenness("C") == 2
                    && lineScore.Betweenness("A") == 0 && lineScore.Betweenness("D") == 0,
            "line: the two interior planets are on 2 paths each, the ends on none");
        ok &= Check(Near(lineScore.Percentile("B"), 2f / 3f) && Near(lineScore.Percentile("C"), 2f / 3f),
            "line: tied betweenness shares one percentile (2 lower of 3 others)");
        ok &= Check(Near(lineScore.Percentile("A"), 0f), "line: an end scores 0");

        // Star: hub H with leaves L1..L3.
        var star = new[]
        {
            P("H", "L1"), P("H", "L2"), P("H", "L3"),
            P("L1", "H", "L2"), P("L1", "H", "L3"), P("L2", "H", "L3"),
        };
        var starScore = PlanetCentrality.Compute(new[] { "H", "L1", "L2", "L3" }, star);
        ok &= Check(starScore.Betweenness("H") == 3 && Near(starScore.Percentile("H"), 1f),
            "star: the hub is on 3 paths and is the top percentile (1)");
        ok &= Check(starScore.Top(1).First().name == "H", "Top(1) is the hub");
        ok &= Check(starScore.Top(10).Count() == 4, "Top(count) is capped at the planets that exist");

        // Review focus 1: a 3-planet chain makes the middle planet the top percentile.
        var tiny = PlanetCentrality.Compute(new[] { "A", "B", "C" }, new[] { P("A", "B"), P("A", "B", "C"), P("B", "C") });
        ok &= Check(tiny.Betweenness("B") == 1 && Near(tiny.Percentile("B"), 1f),
            "a 3-planet chain: the middle planet is the top percentile (accepted on tiny boards)");

        // Review focus 2: the 1-node no-route stub, a 2-node path and a path naming an unknown planet add nothing.
        var odd = PlanetCentrality.Compute(new[] { "A", "B" },
            new[] { P("A"), P("A", "B"), P("A", "Nowhere", "B") });
        ok &= Check(odd.Betweenness("A") == 0 && odd.Betweenness("B") == 0 && odd.Betweenness("Nowhere") == 0,
            "a stub, a 2-node path and an unknown interior planet add nothing and do not throw");
        ok &= Check(Near(odd.Percentile("A"), 0f) && Near(odd.Percentile("B"), 0f),
            "everything tied at 0: every percentile is 0, so there are no chokepoints");

        // Review focus 3: the order of the stored paths does not change the result.
        var shuffled = star.Reverse().ToList();
        var shuffledScore = PlanetCentrality.Compute(new[] { "L3", "L2", "L1", "H" }, shuffled);
        ok &= Check(shuffledScore.Betweenness("H") == starScore.Betweenness("H")
                    && Near(shuffledScore.Percentile("L1"), starScore.Percentile("L1"))
                    && Near(shuffledScore.Percentile("H"), starScore.Percentile("H")),
            "reversed path list and planet order give the same scores");

        // Review focus 5 (pure side): unknown names score 0.
        ok &= Check(starScore.Betweenness("Nowhere") == 0 && Near(starScore.Percentile("Nowhere"), 0f),
            "an unknown planet name scores 0");

        // Empty and single-planet boards.
        var none = PlanetCentrality.Compute(new string[0], new List<IReadOnlyList<string>>());
        ok &= Check(!none.Top(5).Any(), "no planets: nothing to rank");
        var one = PlanetCentrality.Compute(new[] { "A" }, new List<IReadOnlyList<string>>());
        ok &= Check(Near(one.Percentile("A"), 0f), "one planet: percentile 0 (no division by zero)");
        return ok;
    }

    // The map builds the score once from its own all-pairs paths.
    public static bool RunMapCentralityCheck()
    {
        var ok = true;
        var go = new GameObject("ChokepointSelfCheckMap_Centrality");
        var constants = Constants();
        try
        {
            var map = go.AddComponent<GameAIMap>();
            map.GameAIMapInit(HubSpawns(), constants);

            ok &= Check(map.Betweenness("H") == 9 && map.Betweenness("A") == 4
                        && map.Betweenness("X1") == 0 && map.Betweenness("Y") == 0,
                "hub layout: H is on 9 stored paths, A on 4, leaves on none");
            ok &= Check(Near(map.Chokepoint("H"), 1f) && Near(map.Chokepoint("A"), 0.8f) && Near(map.Chokepoint("Y"), 0f),
                "hub layout percentiles: H 1, A 0.8, a leaf 0");
            ok &= Check(map.IsChokepoint("H") && !map.IsChokepoint("A") && !map.IsChokepoint("Y"),
                "at the default 0.9 only H is a chokepoint");
            constants.chokepointPercentile = 0.5f;
            ok &= Check(map.IsChokepoint("H") && map.IsChokepoint("A") && !map.IsChokepoint("X1"),
                "at 0.5 A joins H, a leaf never does (betweenness 0)");
            constants.chokepointPercentile = 2f;
            ok &= Check(!map.IsChokepoint("H"), "a percentile above 1 switches chokepoints off (review focus 4)");
            ok &= Check(Near(map.Chokepoint("Nowhere"), 0f) && !map.IsChokepoint("Nowhere") && map.Betweenness("Nowhere") == 0,
                "an unknown planet name is not a chokepoint (review focus 5)");
            ok &= Check(map.TopChokepoints(2).Select(t => t.name).SequenceEqual(new[] { "H", "A" }),
                "TopChokepoints orders by betweenness: H then A");
        }
        finally
        {
            Object.DestroyImmediate(go);
            Object.DestroyImmediate(constants);
        }
        return ok;
    }
}
```

Create its `.meta` and the other new script's `.meta` (Unity may rewrite them on import; harmless):

```powershell
foreach ($f in @("Assets\Editor\ChokepointSelfCheck.cs", "Assets\Flatspace\GameAI\PlanetCentrality.cs")) {
  $guid = [guid]::NewGuid().ToString('N')
  $meta = "fileFormatVersion: 2`nguid: $guid`nMonoImporter:`n  externalObjects: {}`n  serializedVersion: 2`n  defaultReferences: []`n  executionOrder: 0`n  icon: {instanceID: 0}`n  userData: `n  assetBundleName: `n  assetBundleVariant: `n"
  [System.IO.File]::WriteAllText("C:\Projects\FlatSpace\$f.meta", $meta)
}
```
(`PlanetCentrality.cs` does not exist yet; create the `.meta` after Step 4 if the loop fails on a missing path, the `.meta` write itself does not need the `.cs`.)

- [ ] **Step 3: Verify RED**

Run `mcp__rider__get_file_problems` on `Assets/Editor/ChokepointSelfCheck.cs` (`rootFolder: "C:/Projects/FlatSpace"`). Expected: errors for `PlanetCentrality`, `GameAIMap.Betweenness/Chokepoint/IsChokepoint/TopChokepoints` and `GameAIConstants.chokepointPercentile` (plus the "not included in any project" note for the new file).

- [ ] **Step 4: Write `PlanetCentrality`**

Create `Assets/Flatspace/GameAI/PlanetCentrality.cs`:

```csharp
// PlanetCentrality.cs
using System;
using System.Collections.Generic;
using System.Linq;

namespace FlatSpace
{
    namespace AI
    {
        /// <summary>
        /// Shortest-path betweenness of every planet: how many planet pairs have a stored shortest path that passes THROUGH
        /// it (endpoints excluded), and that count as a percentile of the board. A high percentile marks a chokepoint such as
        /// a crossroads, which a plain neighbour count misses. Pure: it reads only the planet names and the paths it is
        /// given (the all-pairs paths GameAIMap stores, the routes ships actually fly), so a self-check drives it directly
        /// and the result does not depend on the order the paths arrive in. One path per pair counts, so equal-cost ties go
        /// to whichever path was stored (tools/planet-centrality.ps1 has the same property).
        /// </summary>
        public class PlanetCentrality
        {
            private readonly Dictionary<string, int> _betweenness = new Dictionary<string, int>();
            private readonly Dictionary<string, float> _percentile = new Dictionary<string, float>();

            /// <summary>
            /// Each path is a list of planet names from one planet to another. A path with fewer than 3 nodes (including
            /// FindPath's 1-node no-route stub) and any node that is not in planetNames add nothing.
            /// </summary>
            public static PlanetCentrality Compute(IEnumerable<string> planetNames, IEnumerable<IReadOnlyList<string>> paths)
            {
                var result = new PlanetCentrality();
                var names = planetNames.Distinct().ToList();
                foreach (var name in names) result._betweenness[name] = 0;

                foreach (var path in paths)
                    for (var i = 1; i < path.Count - 1; i++)
                        if (result._betweenness.ContainsKey(path[i]))
                            result._betweenness[path[i]]++;

                // Percentile: the fraction of the OTHER planets with strictly lower betweenness, so ties share a score,
                // leaves score 0 and the top planet scores 1. A board of 0 or 1 planets scores 0.
                var values = names.Select(n => result._betweenness[n]).ToList();
                foreach (var name in names)
                {
                    var own = result._betweenness[name];
                    result._percentile[name] = names.Count <= 1
                        ? 0f
                        : values.Count(v => v < own) / (float)(names.Count - 1);
                }
                return result;
            }

            /// <summary>Paths through the planet (endpoints excluded); 0 for an unknown name.</summary>
            public int Betweenness(string name)
                => name != null && _betweenness.TryGetValue(name, out var value) ? value : 0;

            /// <summary>0..1 share of the other planets with lower betweenness; 0 for an unknown name.</summary>
            public float Percentile(string name)
                => name != null && _percentile.TryGetValue(name, out var value) ? value : 0f;

            /// <summary>The count planets with the highest betweenness, ties by name.</summary>
            public IEnumerable<(string name, int betweenness, float percentile)> Top(int count)
                => _betweenness
                    .OrderByDescending(kv => kv.Value)
                    .ThenBy(kv => kv.Key, StringComparer.Ordinal)
                    .Take(Math.Max(0, count))
                    .Select(kv => (kv.Key, kv.Value, _percentile[kv.Key]));
        }
    }
}
```

- [ ] **Step 5: Add the tunables, remove nothing yet**

In `Assets/Flatspace/GameAI/GameAIConstants.cs`, after the line `public float category5UnlockShipsPerColonizedPlanet = 3f;` add:

```csharp

    [Header("Connectivity (chokepoints)")]
    // A planet whose shortest-path betweenness percentile (0..1, see PlanetCentrality) is at least this is a chokepoint:
    // garrison category 4, and under Consolidate it keeps a garrison. A value above 1 means no planet qualifies.
    public float chokepointPercentile = 0.9f;
    // Consolidate colonization: a target's choice cost is its route cost / (1 + this x its chokepoint percentile), so a
    // hub may be farther and still win. 0 (or below) switches the tilt off; Expand never uses it.
    public float colonizationChokepointWeight = 0.5f;
```

- [ ] **Step 6: Build the score in `GameAIMap` and expose it**

In `Assets/Flatspace/GameAI/GameAIMap.cs`:

1. After `private Dictionary<string, Planet> _planets;` add `private PlanetCentrality _centrality;`
2. In `GameAIMapInit`, immediately before `SetInitialOwnership();` add:

```csharp
                // Betweenness from the paths just stored (once per board: the graph never changes during a match).
                _centrality = PlanetCentrality.Compute(
                    PlanetList.Select(p => p.PlanetName),
                    _planetPathings.Select(p => (IReadOnlyList<string>)p.Path1To2.PathNodes.Select(n => n.Name).ToList()));
```
3. Next to `GetNeighbours` add:

```csharp
            /// <summary>Paths through the planet over every stored shortest path (endpoints excluded); 0 when unknown.</summary>
            public int Betweenness(string planetName) => _centrality?.Betweenness(planetName) ?? 0;

            /// <summary>The planet's betweenness percentile, 0..1 (see PlanetCentrality); 0 when unknown.</summary>
            public float Chokepoint(string planetName) => _centrality?.Percentile(planetName) ?? 0f;

            /// <summary>
            /// A chokepoint: on at least one stored path and at or above GameAIConstants.chokepointPercentile.
            /// </summary>
            public bool IsChokepoint(string planetName)
                => Betweenness(planetName) > 0 && Chokepoint(planetName) >= GameAIConstants.chokepointPercentile;

            /// <summary>The count planets with the highest betweenness, for the Chokepoints log line.</summary>
            public IEnumerable<(string name, int betweenness, float percentile)> TopChokepoints(int count)
                => _centrality != null
                    ? _centrality.Top(count)
                    : Enumerable.Empty<(string name, int betweenness, float percentile)>();
```

- [ ] **Step 7: Neutralise the new behaviour in the older fixtures**

So the older suites keep their meaning (a 3-planet chain's middle is trivially a chokepoint, and Consolidate checks would otherwise start garrisoning it), add these two lines to every constants block that older Consolidate checks use:

- `Assets/Editor/WarshipSelfCheck.cs`, `MakeConstants`, after `constants.warShipData = warship;`:

```csharp
        constants.chokepointPercentile = 2f;            // chokepoints off: older checks keep their meaning
        constants.colonizationChokepointWeight = 0f;
```
- `Assets/Editor/PlayerKnowledgeSelfCheck.cs`: the constants blocks at about lines 428 and 708 (the ones the Consolidate checks `RunColonyShipSituationalCheck` and `RunColonyFoodRiderCheck` use), right after each `CreateInstance<GameAIConstants>()` line, the same two lines with `constants.` as the variable.

(`ShipTransportSelfCheck.NewConstants` is handled in Task 2 with the `highTrafficConnectionCount` removal.)

- [ ] **Step 8: Register the suite**

In `Assets/Editor/AllAISelfChecks.cs` add `("Chokepoint", ChokepointSelfCheck.RunChecks),` as the last entry of the `suites` list.

- [ ] **Step 9: Verify GREEN**

`get_file_problems` on `ChokepointSelfCheck.cs`, `PlanetCentrality.cs`, `GameAIMap.cs`, `GameAIConstants.cs`, `WarshipSelfCheck.cs`, `PlayerKnowledgeSelfCheck.cs`, `AllAISelfChecks.cs`: no real errors. Then ask the user to focus the Editor and run `FlatSpace -> AI -> Run Chokepoint Self-Check` (expect `[ChokepointSelfCheck] ALL PASSED`) and `Run All AI Self-Checks` (expect all suites pass). Wait for their result.

- [ ] **Step 10: Commit**

```bash
cd /c/Projects/FlatSpace && git add Assets/Flatspace/GameAI/PlanetCentrality.cs Assets/Flatspace/GameAI/PlanetCentrality.cs.meta Assets/Editor/ChokepointSelfCheck.cs Assets/Editor/ChokepointSelfCheck.cs.meta Assets/Flatspace/GameAI/GameAIMap.cs Assets/Flatspace/GameAI/GameAIConstants.cs Assets/Editor/AllAISelfChecks.cs Assets/Editor/WarshipSelfCheck.cs Assets/Editor/PlayerKnowledgeSelfCheck.cs && git commit -m "$(cat <<'EOF'
feat: planet centrality (betweenness percentile) from the stored all-pairs paths

Co-Authored-By: Claude Sonnet 5.5 <noreply@anthropic.com>
EOF
)"
```

---

### Task 2: Garrison uses chokepoints

**Files:**
- Modify: `Assets/Flatspace/GameAI/ShipTransportPlanner.cs` (lines 67-73 and 90-91)
- Modify: `Assets/Flatspace/GameAI/GameAIConstants.cs` (lines 25-28: remove `highTrafficConnectionCount`)
- Modify: `Assets/GameAIConstantsProductionTypes.asset` (the `highTrafficConnectionCount: 4` line)
- Modify: `Assets/Editor/ShipTransportSelfCheck.cs` (`NewConstants` lines 59-80; `RunCategoryCheck` lines 168-218)
- Modify: `Assets/Editor/ChokepointSelfCheck.cs` (new `RunGarrisonCheck`)

**Interfaces:**
- Consumes: `GameAIMap.IsChokepoint(string)`, `GameAIConstants.chokepointPercentile` (Task 1); `ChokepointSelfCheck.HubSpawns()`, `ChokepointSelfCheck.Constants(float)` (Task 1).
- Produces: `ShipTransportPlanner.IsChokepoint(Planet)`; `MaintainsGarrison` now also true for a colonized chokepoint under Consolidate.

- [ ] **Step 1: Write the failing garrison check**

In `Assets/Editor/ChokepointSelfCheck.cs` add `ok &= RunGarrisonCheck();` to `RunChecks` and this method:

```csharp
    private static Planet Colonize(GameAIMap map, string name, int player = 0)
    {
        var planet = map.GetPlanet(name);
        planet.Owner = player;
        planet.Population.Add(new Planet.Inhabitant { Player = player });
        return planet;
    }

    // Category 4 is "is a chokepoint" (not a neighbour count); under Consolidate a colonized chokepoint keeps a garrison.
    // Hub layout, all six planets colonized by player 0 (so none is outer): H is the chokepoint (percentile 1), A is 0.8.
    public static bool RunGarrisonCheck()
    {
        var ok = true;

        foreach (var percentile in new[] { 0.9f, 0.5f, 2f })
        {
            var go = new GameObject("ChokepointSelfCheckMap_Garrison");
            var constants = Constants(percentile);
            try
            {
                var map = go.AddComponent<GameAIMap>();
                map.GameAIMapInit(HubSpawns(), constants);
                foreach (var n in new[] { "A", "H", "X1", "X2", "X3", "Y" }) Colonize(map, n);
                var expand = new ShipTransportPlanner(map, 0);
                var consolidate = new ShipTransportPlanner(map, 0, PlayerAI.AIStrategy.AIStrategyConsolidate);
                Planet P(string n) => map.GetPlanet(n);

                if (percentile == 0.9f)
                {
                    ok &= Check(expand.Category(P("H")) == 4 && expand.Garrison(P("H")) == 2,
                        "0.9: H (4 neighbours and the top chokepoint) is category 4 with garrison 2");
                    ok &= Check(expand.Category(P("A")) == ShipTransportPlanner.NoCategory && expand.Garrison(P("A")) == 0,
                        "0.9: A (percentile 0.8) is not a chokepoint: no category");
                    ok &= Check(expand.Category(P("X1")) == ShipTransportPlanner.NoCategory, "0.9: a leaf has no category");

                    ok &= Check(consolidate.MaintainsGarrison(P("H")),
                        "0.9 Consolidate: a colonized, non-outer chokepoint maintains a garrison");
                    ok &= Check(!consolidate.MaintainsGarrison(P("A")) && !consolidate.MaintainsGarrison(P("X1")),
                        "0.9 Consolidate: a non-outer non-chokepoint does not");
                    var states = consolidate.BuildStates();
                    var h = states.Find(s => s.Planet == P("H"));
                    var a = states.Find(s => s.Planet == P("A"));
                    ok &= Check(consolidate.LastRound == 1 && h.Garrison == 2 && h.RoundGarrison == 2 && h.Category == 4,
                        "0.9 Consolidate: H garrisons at round 1 with garrisonHighTraffic");
                    ok &= Check(a.Garrison == 0 && a.Category == ShipTransportPlanner.NoCategory,
                        "0.9 Consolidate: A is spare-only (no garrison, no category)");

                    // Expand with every planet colonized: the garrison round logic is unchanged for a chokepoint.
                    ok &= Check(expand.BuildStates().Find(s => s.Planet == P("H")).Garrison == 2,
                        "0.9 Expand: H garrisons through the ordinary category garrison");
                }
                else if (percentile == 0.5f)
                {
                    // A has only 2 neighbours (below the old threshold of 4) yet is category 4: it is the percentile, not the count.
                    ok &= Check(map.GetNeighbours("A").Count == 2 && expand.Category(P("A")) == 4,
                        "0.5: A has 2 neighbours but is category 4 (the test is centrality, not connection count)");
                    ok &= Check(consolidate.MaintainsGarrison(P("A")), "0.5 Consolidate: A now garrisons too");
                    ok &= Check(expand.Category(P("X1")) == ShipTransportPlanner.NoCategory,
                        "0.5: a leaf (betweenness 0) never qualifies");
                }
                else
                {
                    ok &= Check(expand.Category(P("H")) == ShipTransportPlanner.NoCategory
                                && !consolidate.MaintainsGarrison(P("H")),
                        "2: a percentile above 1 switches it off: no category and no Consolidate garrison (review focus 4)");
                }
            }
            finally
            {
                Object.DestroyImmediate(go);
                Object.DestroyImmediate(constants);
            }
        }

        // An outer chokepoint is unchanged: still outer, category 2 wins as the lower number, garrison is the larger.
        var go2 = new GameObject("ChokepointSelfCheckMap_GarrisonOuter");
        var constants2 = Constants();
        constants2.garrisonOuter = 6;
        try
        {
            var map = go2.AddComponent<GameAIMap>();
            map.GameAIMapInit(HubSpawns(), constants2);
            foreach (var n in new[] { "A", "H", "X1", "X2", "Y" }) Colonize(map, n);   // X3 stays empty: H is outer
            var planner = new ShipTransportPlanner(map, 0);
            ok &= Check(planner.IsOuter(map.GetPlanet("H")) && planner.Category(map.GetPlanet("H")) == 2
                        && planner.Garrison(map.GetPlanet("H")) == 6,
                "an outer chokepoint keeps category 2 and the larger (outer) garrison");
        }
        finally
        {
            Object.DestroyImmediate(go2);
            Object.DestroyImmediate(constants2);
        }
        return ok;
    }
```

- [ ] **Step 2: Verify RED**

`get_file_problems` on `ChokepointSelfCheck.cs` shows no unresolved members (the check only uses existing APIs), so the RED here is behavioural: the check cannot pass until Step 3. Do not run the Editor yet. (If you want a compile-level RED, add a call to `expand.IsChokepoint(P("H"))` first; Step 3 defines it. Not required.)

- [ ] **Step 3: Change the planner**

In `Assets/Flatspace/GameAI/ShipTransportPlanner.cs`:

Replace the `MaintainsGarrison` doc comment and body (lines 67-73) with:

```csharp
            /// <summary>Is the planet a chokepoint (a high shortest-path betweenness percentile, see PlanetCentrality)?</summary>
            public bool IsChokepoint(Planet planet) => _map.IsChokepoint(planet.PlanetName);

            /// <summary>
            /// The seam for garrison policy: does this colonized planet hold a garrison? Every planet
            /// does under Expand; under Consolidate only outer planets and colonized chokepoints do (at round 1), so
            /// every ship elsewhere is spare. To garrison more planets under Consolidate later, change this one method.
            /// </summary>
            public bool MaintainsGarrison(Planet planet)
                => _strategy != PlayerAI.AIStrategy.AIStrategyConsolidate
                   || IsOuter(planet)
                   || (IsColonized(planet) && IsChokepoint(planet));
```

Replace lines 90-91 (the neighbour-count test) with:

```csharp
                if (IsChokepoint(planet))
                    result.Add(4);
```

- [ ] **Step 4: Remove `highTrafficConnectionCount`**

In `Assets/Flatspace/GameAI/GameAIConstants.cs` replace

```csharp
    public int garrisonHighTraffic = 2;         // category 4: many connections
    public int garrisonHighlySpecialized = 1;   // category 5: Verdant, Desolate
    // A planet with at least this many neighbours is "high traffic".
    public int highTrafficConnectionCount = 4;
```
with
```csharp
    public int garrisonHighTraffic = 2;         // category 4: chokepoint (see chokepointPercentile)
    public int garrisonHighlySpecialized = 1;   // category 5: Verdant, Desolate
```

Remove the asset key (Unity ignores an unknown key, but keep the YAML clean). First `Grep` for `highTrafficConnectionCount` in `Assets/GameAIConstantsProductionTypes.asset` to confirm the exact line and its line ending, then `Edit` it away. Then `Grep` the whole of `Assets/` for `highTrafficConnectionCount`; the only remaining hit must be `ShipTransportSelfCheck.cs` (Step 5).

- [ ] **Step 5: Fix the older ship-transport check**

In `Assets/Editor/ShipTransportSelfCheck.cs` `NewConstants`, replace `c.highTrafficConnectionCount = 4;` with:

```csharp
        c.chokepointPercentile = 2f;           // chokepoints off: older checks keep their meaning (ChokepointSelfCheck covers them)
        c.colonizationChokepointWeight = 0f;
```

In `RunCategoryCheck` the hub assertion `"H with 4 neighbours is high traffic (category 4, garrison 2)"` still needs category 4, now through betweenness. Replace

```csharp
            var map = BuildMap(go, NewConstants(),
                MakeSpawn("A", Planet.PlanetType.PlanetTypeFarm, new[] { "B" }),
```
with
```csharp
            var constants = NewConstants();
            constants.chokepointPercentile = 0.9f;   // H is the top chokepoint on this board; B and C (percentile 9/11) are not
            var map = BuildMap(go, constants,
                MakeSpawn("A", Planet.PlanetType.PlanetTypeFarm, new[] { "B" }),
```
and change that assertion's label to `"H (the hub, top betweenness) is a chokepoint: category 4, garrison 2"`. (Board: A-B-C-D chain, H with X1-X4, P-Q, V; H is on 6 paths and is the top; B and C are on 2 each, percentile 9/11 = 0.82, below 0.9.)

- [ ] **Step 6: Verify GREEN**

`get_file_problems` on `ShipTransportPlanner.cs`, `GameAIConstants.cs`, `ShipTransportSelfCheck.cs`, `ChokepointSelfCheck.cs`: no real errors, and a `Grep` for `highTrafficConnectionCount` over `Assets/` returns nothing. Ask the user to run `Run Chokepoint Self-Check`, `Run Ship Transport Self-Check` and `Run All AI Self-Checks`; expect all to pass. If a Ship Transport or Blockade Breaking assertion fails because a middle planet is now a chokepoint, that fixture needs `chokepointPercentile = 2f` (the plan's neutralisation intent); fix the fixture, not the planner.

- [ ] **Step 7: Commit**

```bash
cd /c/Projects/FlatSpace && git add Assets/Flatspace/GameAI/ShipTransportPlanner.cs Assets/Flatspace/GameAI/GameAIConstants.cs Assets/GameAIConstantsProductionTypes.asset Assets/Editor/ShipTransportSelfCheck.cs Assets/Editor/ChokepointSelfCheck.cs && git commit -m "$(cat <<'EOF'
feat: garrison category 4 and the Consolidate garrison use chokepoints

Co-Authored-By: Claude Sonnet 5.5 <noreply@anthropic.com>
EOF
)"
```

---

### Task 3: Blockade target ranking uses chokepoints

**Files:**
- Modify: `Assets/Flatspace/GameAI/AssaultPlanner.cs` (reason constants near line 19; `BlockadeCandidate` line 182; doc comment line 191; candidates line 222; ranking line 234; reason chain lines 251-255)
- Modify: `Assets/Flatspace/GameAI/BlockadeTargetTracker.cs:19` and `Assets/Flatspace/Diagnostics/AITuningLogger.cs:158` (comment text only)
- Modify: `Assets/Editor/BlockadeBreakSelfCheck.cs` (`Scenario.Hub`, `RunChokepointRankingCheck`, `RunChecks`)

**Interfaces:**
- Consumes: `GameAIMap.Chokepoint(string)` (Task 1); `ChokepointSelfCheck.HubSpawns()` (Task 1).
- Produces: `AssaultPlanner.ReasonChokepoint` (`"Chokepoint"`); `BlockadeBreakSelfCheck.Scenario.Hub()`.

- [ ] **Step 1: Write the failing ranking check**

In `Assets/Editor/BlockadeBreakSelfCheck.cs` add to `RunChecks` (after `RunBlockadeRankingCheck`): `ok &= RunChokepointRankingCheck();`

Add to `Scenario` (after `Fork()`):

```csharp
        // The ChokepointSelfCheck hub: A(0,0) - H(100,0) with H - X1, X2, X3, and A - Y(0,80). H is the chokepoint
        // (percentile 1), A 0.8, the rest leaves. Player 0 holds A.
        public static Scenario Hub()
        {
            var s = Create("Hub");
            s.Constants.maxPathNodesForColonization = 6;
            s.Map = s.MapGo.AddComponent<GameAIMap>();
            s.Map.GameAIMapInit(ChokepointSelfCheck.HubSpawns(), s.Constants);
            s.Finish("A");
            return s;
        }
```

Add the check (after `RunBlockadeRankingCheck`):

```csharp
    // The chokepoint step: after committed offense and a recent cut, before the smaller need and the cheaper path.
    // Hub layout: H is a chokepoint (percentile 1), Y a leaf (0); H needs more offense and is no cheaper than Y.
    public static bool RunChokepointRankingCheck()
    {
        var ok = true;

        using (var s = Scenario.Hub())
        {
            s.Ships("A", 0, 2); s.Ships("H", 1, 3); s.Ships("Y", 1, 1);   // H value 30 (need 33), Y value 10 (need 11)
            var planner = s.Planner(s.View(10), 10);
            ok &= Check(Near(s.Map.Chokepoint("H"), 1f) && Near(s.Map.Chokepoint("Y"), 0f), "hub layout: H is the top chokepoint, Y a leaf");
            ok &= Check(planner.ChooseBlockadeTarget(out var reason) == s.P("H") && reason == AssaultPlanner.ReasonChokepoint,
                "nothing committed, no cut: the chokepoint H outranks the smaller need at Y");
        }

        using (var s = Scenario.Hub())
        {
            s.Ships("A", 0, 2); s.Ships("H", 1, 3); s.Ships("Y", 1, 1);
            s.Memory.Learn("Y", 10f, 8, 20);                // Y cut my order 2 turns ago
            var planner = s.Planner(s.View(10), 10);
            ok &= Check(planner.ChooseBlockadeTarget(out var reason) == s.P("Y") && reason == AssaultPlanner.ReasonRecentCut,
                "a recent cut still outranks the chokepoint step");
        }

        using (var s = Scenario.Hub())
        {
            s.Ships("A", 0, 2); s.Ships("H", 1, 3); s.Ships("Y", 1, 2); s.Ships("Y", 0, 1);   // I hold one ship at Y (value 20 - 10)
            var planner = s.Planner(s.View(10), 10);
            ok &= Check(planner.ChooseBlockadeTarget(out var reason) == s.P("Y") && reason == AssaultPlanner.ReasonCommitted,
                "committed offense still outranks the chokepoint step");
        }

        // Equal chokepoint percentile falls through to the old order (the Fork leaves C1 and C2 are both 0).
        using (var s = Scenario.Fork())
        {
            s.Ships("A", 0, 2); s.Ships("C1", 1, 3); s.Ships("C2", 1, 2);
            var planner = s.Planner(s.View(10), 10);
            ok &= Check(Near(s.Map.Chokepoint("C1"), s.Map.Chokepoint("C2"))
                        && planner.ChooseBlockadeTarget(out var reason) == s.P("C2") && reason == AssaultPlanner.ReasonCheapest,
                "tied chokepoint percentile: the smaller need decides, as before");
        }
        return ok;
    }
```

- [ ] **Step 2: Verify RED**

`get_file_problems` on `BlockadeBreakSelfCheck.cs`: unresolved `AssaultPlanner.ReasonChokepoint`.

- [ ] **Step 3: Implement the ranking step**

In `Assets/Flatspace/GameAI/AssaultPlanner.cs`:

1. After `public const string ReasonRecentCut = "RecentCut";` add `public const string ReasonChokepoint = "Chokepoint";`
2. In `BlockadeCandidate` add `public float  Chokepoint;` after `Recent`.
3. In the `candidates.Add(...)` initializer add `Chokepoint = _map.Chokepoint(name),` after the `Recent = ...` expression.
4. Ranking: insert `.ThenByDescending(c => c.Chokepoint)` between `.ThenByDescending(c => c.Recent)` and `.ThenBy(c => c.Needed)`.
5. Reason chain: insert between the `Recent` branch and the final `else`:

```csharp
                else if (best.Chokepoint != ranked[1].Chokepoint) reason = ReasonChokepoint;
```
6. Update the doc comment above `ChooseBlockadeTarget`: "...then a recent cut of one of my orders, then the more central planet (higher chokepoint percentile), then the smallest offense still needed, ...".

In `BlockadeTargetTracker.cs:19` and `AITuningLogger.cs:158` change the comment text "Committed, RecentCut or Cheapest" to "Committed, RecentCut, Chokepoint or Cheapest".

- [ ] **Step 4: Verify GREEN**

`get_file_problems` on `AssaultPlanner.cs`, `BlockadeBreakSelfCheck.cs`: clean. Ask the user to run `Run Blockade Breaking Self-Check` and `Run All AI Self-Checks`; expect all to pass (the existing Fork-based ranking assertions are unchanged because C1 and C2 tie at 0).

- [ ] **Step 5: Commit**

```bash
cd /c/Projects/FlatSpace && git add Assets/Flatspace/GameAI/AssaultPlanner.cs Assets/Flatspace/GameAI/BlockadeTargetTracker.cs Assets/Flatspace/Diagnostics/AITuningLogger.cs Assets/Editor/BlockadeBreakSelfCheck.cs && git commit -m "$(cat <<'EOF'
feat: blockade target ranking prefers chokepoints after a recent cut

Co-Authored-By: Claude Sonnet 5.5 <noreply@anthropic.com>
EOF
)"
```

---

### Task 4: Consolidate colonization tilt

**Files:**
- Modify: `Assets/Flatspace/GameAI/PlayerAI.cs` (`ProcessColonizers`, lines 450-580; new private helper next to it)
- Modify: `Assets/Flatspace/Diagnostics/AITuningLogger.cs` (new `LogChokepointColonize`, next to `LogColonizeCancelled`)
- Modify: `Assets/Editor/BlockadeBreakSelfCheck.cs` (`RunChokepointColonizationCheck`, `RunChecks`)

**Interfaces:**
- Consumes: `GameAIMap.Chokepoint(string)`, `GameAIConstants.colonizationChokepointWeight` (Task 1); `Scenario.Hub()` (Task 3).
- Produces: `AITuningLogger.LogChokepointColonize(int turn, int playerId, string origin, string target, float routeCost, float percentile, string nearestTarget, float nearestCost)`.

- [ ] **Step 1: Write the failing colonization check**

In `Assets/Editor/BlockadeBreakSelfCheck.cs` add `ok &= RunChokepointColonizationCheck();` to `RunChecks`, and:

```csharp
    private static GameAI.GameAIOrder LaunchColonist(Scenario s)
    {
        var results = new List<Planet.PlanetUpdateResult>
        {
            new Planet.PlanetUpdateResult("A",
                Planet.PlanetUpdateResult.PlanetUpdateResultType.PlanetUpdateResultTypeColonizerReady, 1, 0),
        };
        var orders = new List<GameAI.GameAIOrder>();
        s.AI.ProcessColonizers(results, orders);
        return orders.Find(o => o.Type == GameAI.GameAIOrder.OrderType.OrderTypePopulationTransport);
    }

    // Consolidate only: the choice cost is route cost / (1 + weight x chokepoint percentile). Hub layout, A is the ready
    // colonizer: Y is a leaf at cost 80, H a chokepoint (percentile 1) at cost 100, X1-X3 leaves at 200 or more.
    public static bool RunChokepointColonizationCheck()
    {
        var ok = true;
        using (var s = Scenario.Hub())
        {
            s.Constants.colonizationChokepointWeight = 0.5f;
            s.AI.Strategy = PlayerAI.AIStrategy.AIStrategyConsolidate;
            var tilted = LaunchColonist(s);
            ok &= Check(tilted != null && tilted.Target == "H",
                "Consolidate, weight 0.5: the hub H (100 / 1.5 = 66.7) beats the nearer leaf Y (80)");
            ok &= Check(tilted != null && tilted.TotalDelay == 100 && tilted.TimingDelay == 100,
                "the order delay comes from the real route cost (100 at speed 1), not the tilted cost");

            s.Constants.colonizationChokepointWeight = 0.1f;
            ok &= Check(LaunchColonist(s)?.Target == "Y", "weight 0.1: H is 100 / 1.1 = 90.9, so the nearer leaf Y wins");

            s.Constants.colonizationChokepointWeight = 0f;
            ok &= Check(LaunchColonist(s)?.Target == "Y", "weight 0 switches the tilt off: nearest first");

            s.Constants.colonizationChokepointWeight = -1f;
            ok &= Check(LaunchColonist(s)?.Target == "Y", "a negative weight is off, not an inverted tilt (review focus 4)");

            s.Constants.colonizationChokepointWeight = 0.5f;
            s.AI.Strategy = PlayerAI.AIStrategy.AIStrategyExpand;
            var expand = LaunchColonist(s);
            ok &= Check(expand != null && expand.Target == "Y" && expand.TotalDelay == 80,
                "Expand ignores the tilt: nearest first, delay 80");
        }
        return ok;
    }
```

- [ ] **Step 2: Verify RED**

`get_file_problems` on `BlockadeBreakSelfCheck.cs`: no unresolved members (it only uses existing APIs), so the RED is behavioural (the tilted assertion cannot pass yet). Do not run the Editor before Step 3.

- [ ] **Step 3: Implement the tilt, the real-cost delay and the log**

In `Assets/Flatspace/GameAI/PlayerAI.cs`, add this helper directly above `// Public for the FlatSpace/AI self-check` / `public void ProcessColonizers(`:

```csharp
            // Consolidate tilts colonization toward chokepoints: a target's choice cost is its route cost divided by
            // 1 + colonizationChokepointWeight x its chokepoint percentile, so a hub may be farther and still win. Expand,
            // a weight of 0 (or below) and unknown planets leave it at 1 (nearest first). The order delay never uses it.
            private float ColonizationCostDivisor(string targetName)
            {
                if (Strategy != AIStrategy.AIStrategyConsolidate) return 1f;
                var weight = AIMap.GameAIConstants.colonizationChokepointWeight;
                return weight <= 0f ? 1f : 1f + weight * AIMap.Chokepoint(targetName);
            }
```

In `ProcessColonizers`:

1. After `var plannedRoutes = new Dictionary<...>();` add:

```csharp
                // Per origin, the candidate with the lowest route cost, so the log shows when the chokepoint tilt moved the pick.
                var nearest = new Dictionary<string, (string target, float cost)>();
```
2. Inside the target loop, directly after `plannedRoutes[(colonizer.Name, t.PlanetName)] = route;` add:

```csharp
                        if (!nearest.TryGetValue(colonizer.Name, out var best) || route.Cost < best.cost
                            || (route.Cost == best.cost && string.CompareOrdinal(t.PlanetName, best.target) < 0))
                            nearest[colonizer.Name] = (t.PlanetName, route.Cost);
```
3. Change `Cost = route.Cost,` in the `ScoreMatrixChoiceElement` initializer to `Cost = route.Cost / ColonizationCostDivisor(t.PlanetName),`.
4. In the action loop, replace

```csharp
                    var delay  = Convert.ToInt32(
                        action.Cost / AIMap.GameAIConstants.defaultTravelSpeed);
                    var route  = plannedRoutes[(action.Origin, action.Target)];
```
with
```csharp
                    var route  = plannedRoutes[(action.Origin, action.Target)];
                    // The real route cost: action.Cost may carry the chokepoint tilt, which must not shorten the flight.
                    var delay  = Convert.ToInt32(
                        route.Cost / AIMap.GameAIConstants.defaultTravelSpeed);
                    if (nearest.TryGetValue(action.Origin, out var near) && near.target != action.Target
                        && ColonizationCostDivisor(action.Target) > 1f)
                        AITuningLogger.LogChokepointColonize(turn, Player.playerID, action.Origin, action.Target,
                            route.Cost, AIMap.Chokepoint(action.Target), near.target, near.cost);
```
(`turn` is already declared earlier in the method.)

In `Assets/Flatspace/Diagnostics/AITuningLogger.cs`, after `LogColonizeCancelled` add:

```csharp
    /// <summary>A Consolidate colonist went to a different planet than the nearest candidate because of the chokepoint tilt: T&lt;turn&gt;|P&lt;id&gt;|ChokepointColonize|origin-&gt;target|routeCost|percentile|nearestTarget|nearestCost.</summary>
    public static void LogChokepointColonize(int turnNumber, int playerId, string origin, string target, float routeCost,
        float percentile, string nearestTarget, float nearestCost)
    {
        if (_currentLogPath == null) return;
        var ci = System.Globalization.CultureInfo.InvariantCulture;
        AppendLines(new List<string> { FormatLine(turnNumber, playerId, "ChokepointColonize", $"{origin}->{target}",
            routeCost.ToString("0.#", ci), percentile.ToString("0.00", ci), nearestTarget,
            nearestCost.ToString("0.#", ci)) });
    }
```

- [ ] **Step 4: Verify GREEN**

`get_file_problems` on `PlayerAI.cs`, `AITuningLogger.cs`, `BlockadeBreakSelfCheck.cs`: clean. Ask the user to run `Run Blockade Breaking Self-Check`, `Run Blockade Avoidance Self-Check`, `Run Player Knowledge Self-Check` and `Run All AI Self-Checks`; expect all to pass (Expand paths and the existing delay assertions are unchanged because the divisor is 1 there and `route.Cost` equals the old `action.Cost`).

- [ ] **Step 5: Commit**

```bash
cd /c/Projects/FlatSpace && git add Assets/Flatspace/GameAI/PlayerAI.cs Assets/Flatspace/Diagnostics/AITuningLogger.cs Assets/Editor/BlockadeBreakSelfCheck.cs && git commit -m "$(cat <<'EOF'
feat: Consolidate colonization tilts toward chokepoints (delay keeps the real route cost)

Co-Authored-By: Claude Sonnet 5.5 <noreply@anthropic.com>
EOF
)"
```

---

### Task 5: Tuning-log lines and documentation

**Files:**
- Modify: `Assets/Flatspace/GameAI/GameAIMap.cs` (`ChokepointSummary`)
- Modify: `Assets/Flatspace/Diagnostics/AITuningLogger.cs` (`FormatChokepointList`, `LogChokepoints`, `LogChokepointGarrison`)
- Modify: `Assets/Flatspace/GameAI/GameAI.cs` (`LogEconomySummary`, lines 161-172)
- Modify: `Assets/Flatspace/Objects/Board/GameBoard.cs` (`InitGame`, after line 322)
- Modify: `Assets/Editor/ChokepointSelfCheck.cs` (`RunSummaryAndFormatCheck`)
- Modify: `CLAUDE.md`, `FUTURE_FEATURES.md`, `.claude/skills/tuning-log/SKILL.md`, `docs/superpowers/specs/2026-10-02-connectivity-targeting-design.md`

**Interfaces:**
- Consumes: `GameAIMap.IsChokepoint`, `TopChokepoints` (Task 1), `ChokepointSelfCheck.HubSpawns/Constants/Colonize` (Tasks 1-2).
- Produces: `GameAIMap.ChokepointSummary(int)`; `AITuningLogger.FormatChokepointList(IEnumerable<(string name, int betweenness, float percentile)>)` (public static, pure), `LogChokepoints(IEnumerable<...>)`, `LogChokepointGarrison(int turn, int playerId, int colonized, int boardTotal, int shipsOnThem, int allShips)`.

- [ ] **Step 1: Write the failing summary and formatter check**

In `Assets/Editor/ChokepointSelfCheck.cs` add `ok &= RunSummaryAndFormatCheck();` to `RunChecks` and:

```csharp
    // The numbers behind the ChokepointGarrison line, and the Chokepoints line's field format.
    public static bool RunSummaryAndFormatCheck()
    {
        var ok = true;
        var go = new GameObject("ChokepointSelfCheckMap_Summary");
        var constants = Constants();
        try
        {
            var map = go.AddComponent<GameAIMap>();
            map.GameAIMapInit(HubSpawns(), constants);
            Colonize(map, "H"); Colonize(map, "A");
            for (var i = 0; i < 2; i++) map.GetPlanet("H").DockShipFromSave(Ship.ShipKind.WarShip, 0, new List<string>());
            for (var i = 0; i < 3; i++) map.GetPlanet("A").DockShipFromSave(Ship.ShipKind.WarShip, 0, new List<string>());
            map.GetPlanet("H").DockShipFromSave(Ship.ShipKind.WarShip, 1, new List<string>());   // another player's ship
            map.GetPlanet("H").DockShipFromSave(Ship.ShipKind.ColonyShip, 0, new List<string>()); // not a warship

            var summary = map.ChokepointSummary(0);
            ok &= Check(summary.colonized == 1 && summary.boardTotal == 1 && summary.shipsOnThem == 2 && summary.allShips == 5,
                "player 0: 1 of the board's 1 chokepoints colonized, 2 warships on it, 5 warships in all");
            var rival = map.ChokepointSummary(1);
            ok &= Check(rival.colonized == 0 && rival.boardTotal == 1 && rival.shipsOnThem == 0 && rival.allShips == 1,
                "player 1 colonizes no chokepoint; its one warship counts only in the total");
        }
        finally
        {
            Object.DestroyImmediate(go);
            Object.DestroyImmediate(constants);
        }

        ok &= Check(AITuningLogger.FormatChokepointList(new[] { ("Industrial 4", 312, 1f), ("Normal 9", 207, 0.9f) })
                    == "Industrial 4=312%1.00,Normal 9=207%0.90",
            "the Chokepoints field is name=betweenness%percentile joined by commas");
        ok &= Check(AITuningLogger.FormatChokepointList(new (string, int, float)[0]) == "-",
            "an empty list is logged as -");
        return ok;
    }
```

- [ ] **Step 2: Verify RED**

`get_file_problems` on `ChokepointSelfCheck.cs`: unresolved `ChokepointSummary` and `FormatChokepointList`.

- [ ] **Step 3: Implement the summary, the formatter and the three log calls**

`GameAIMap.cs`, next to `TopChokepoints`:

```csharp
            /// <summary>
            /// For one player: the chokepoints it colonizes, the chokepoints on the board, its docked warships on those it
            /// colonizes, and its docked warships everywhere. Feeds the ChokepointGarrison log line.
            /// </summary>
            public (int colonized, int boardTotal, int shipsOnThem, int allShips) ChokepointSummary(int playerId)
            {
                var colonized = 0; var boardTotal = 0; var shipsOnThem = 0; var allShips = 0;
                foreach (var planet in PlanetList)
                {
                    var chokepoint = IsChokepoint(planet.PlanetName);
                    if (chokepoint) boardTotal++;
                    var ships = planet.DockedShips.Count(s => s.Kind == Ship.ShipKind.WarShip && s.Owner == playerId);
                    allShips += ships;
                    if (chokepoint && planet.Owner == playerId && planet.Population.Count > 0)
                    {
                        colonized++;
                        shipsOnThem += ships;
                    }
                }
                return (colonized, boardTotal, shipsOnThem, allShips);
            }
```

`AITuningLogger.cs`, after `LogBoardConfig`:

```csharp
    /// <summary>The Chokepoints field: name=betweenness%percentile joined by commas, or - when empty.</summary>
    public static string FormatChokepointList(IEnumerable<(string name, int betweenness, float percentile)> chokepoints)
    {
        var inv = System.Globalization.CultureInfo.InvariantCulture;
        var parts = new List<string>();
        foreach (var c in chokepoints)
            parts.Add($"{c.name}={c.betweenness}%{c.percentile.ToString("0.00", inv)}");
        return parts.Count == 0 ? "-" : string.Join(",", parts);
    }

    /// <summary>The board's top chokepoints, once per InitGame (the last is the real board): T0|P-1|Chokepoints|name=betweenness%percentile,...</summary>
    public static void LogChokepoints(IEnumerable<(string name, int betweenness, float percentile)> chokepoints)
    {
        if (_currentLogPath == null) return;
        AppendLines(new List<string> { FormatLine(0, -1, "Chokepoints", FormatChokepointList(chokepoints)) });
    }

    /// <summary>Every 25 turns per player: T&lt;turn&gt;|P&lt;id&gt;|ChokepointGarrison|colonized|boardTotal|shipsOnThem|allShips.</summary>
    public static void LogChokepointGarrison(int turnNumber, int playerId, int colonized, int boardTotal,
        int shipsOnThem, int allShips)
    {
        if (_currentLogPath == null) return;
        AppendLines(new List<string> { FormatLine(turnNumber, playerId, "ChokepointGarrison", colonized.ToString(),
            boardTotal.ToString(), shipsOnThem.ToString(), allShips.ToString()) });
    }
```

`GameAI.cs` `LogEconomySummary`: after the `AITuningLogger.LogEconomy(...)` call (inside the loop) add:

```csharp
                    var chokepoints = GameAIMap.ChokepointSummary(player);
                    AITuningLogger.LogChokepointGarrison(turnNumber, player, chokepoints.colonized, chokepoints.boardTotal,
                        chokepoints.shipsOnThem, chokepoints.allShips);
```
and extend the method's comment: "...and, beside it, how many chokepoints it colonizes and how many of its warships sit on them."

`GameBoard.cs` `InitGame`, right after `GameAI.InitGameAI(planetSpawnData, gameAIConstants);` add:

```csharp
                // The chokepoints of this board (the last InitGame in a match is the real one, like BoardConfig).
                AITuningLogger.LogChokepoints(GameAI.GameAIMap.TopChokepoints(10));
```

- [ ] **Step 4: Verify GREEN**

`get_file_problems` on `GameAIMap.cs`, `AITuningLogger.cs`, `GameAI.cs`, `GameBoard.cs`, `ChokepointSelfCheck.cs`: clean. Ask the user to run `Run Chokepoint Self-Check` and `Run All AI Self-Checks`, then do one Play-mode run with `_logAIEvents` on and confirm `T0|P-1|Chokepoints|...` and (after T25) `ChokepointGarrison` lines appear. Wait for their report.

- [ ] **Step 5: Update the docs**

`CLAUDE.md` (read the surrounding text first; keep each edit local):
- **Tests** list: add `FlatSpace → AI → Run Chokepoint Self-Check` in `Assets/Editor/ChokepointSelfCheck.cs` (centrality, map, garrison, summary and formatter; the ranking and colonization checks live in `BlockadeBreakSelfCheck`), and add Chokepoint to the "Run All AI Self-Checks" suite list (nine suites).
- **Ship Transport**: category 4 is now "chokepoint (`GameAIMap.IsChokepoint`: betweenness percentile >= `chokepointPercentile`, default 0.9)"; remove `highTrafficConnectionCount` from the tunables list and add `chokepointPercentile`; in the Consolidate bullet say `MaintainsGarrison` is "outer or a colonized chokepoint", and add that chokepoint garrisons also count in `WantedWarships` (it sums the Consolidate planner's round garrisons), so they raise the wanted fleet.
- **Blockade breaking (assault)**: replace the sentence "**Connectivity is deliberately not in the ranking yet: add the shortest-path betweenness step after the recent-cut step when sub-project 4 builds it.**" with the new order (committed, recent cut, chokepoint percentile, smallest offense needed, cheapest path, name) and the `Chokepoint` reason; note it rarely decides because committed offense decides most targets.
- New section **Connectivity (chokepoints)** after "Warships and Blockade": `PlanetCentrality` (what it counts, percentile definition, once per board from the stored all-pairs paths, not saved), `GameAIMap.Betweenness/Chokepoint/IsChokepoint/TopChokepoints/ChokepointSummary`, the three consumers, the Consolidate colonization tilt (formula, weight 0 = off, delay uses the real route cost, `ChokepointColonize`), the two tunables, "producer value is deliberately not built (agreed values recorded in `FUTURE_FEATURES.md`)", and the fixture rule (older self-check constants set `chokepointPercentile = 2f` and the weight 0).
- **AI Tuning Log**: add `Chokepoints|<planet>=<betweenness>%<percentile>,...` (once per match at T0, P-1), `ChokepointColonize|<origin>-><target>|<routeCost>|<percentile>|<nearestTarget>|<nearestCost>`, `ChokepointGarrison|<colonized>|<boardTotal>|<shipsOnThem>|<allShips>` (per player every 25 turns), and the new `Chokepoint` value in the `BlockadeTarget` reason list.

`.claude/skills/tuning-log/SKILL.md`: add the three lines to the line list and a short "Chokepoints" analysis (does `Chokepoint` ever decide a `BlockadeTarget`; how often `ChokepointColonize` fires and how much farther it reaches than the nearest target; the `ChokepointGarrison` share `shipsOnThem / allShips` and whether the assault's spare ships fell); change the `Committed, RecentCut or Cheapest` text at line 221 to include `Chokepoint`; at line 137 say the betweenness rank can be read from the `Chokepoints` line instead of running `planet-centrality.ps1`.

`FUTURE_FEATURES.md`: in item (4) append "*Built 2026-10-02 (branch `connectivity-targeting`): `PlanetCentrality`, garrison category 4 and the Consolidate chokepoint garrison, the blockade ranking step, the Consolidate colonization tilt. Producer value (Verdant 1.0, Desolate 1.0, Farm 0.5, others 0, authored as data on `PlanetResourceData`) and a change to the spare-ship calculation are agreed but not built. Tuning option: move the chokepoint ranking step ahead of the recent-cut step.*" and change the Milestone sentence to say 2, 3 and 4 are done and 5 remains. Do not move the entry to `completed_features.md` (the user runs `/update_feature_list`).

Spec `docs/superpowers/specs/2026-10-02-connectivity-targeting-design.md`: in section 5 rename the periodic line to `ChokepointGarrison` with fields `<colonized>|<boardTotal>|<shipsOnThem>|<allShips>` and fix its description (colonized = chokepoints the player colonizes; allShips = its docked warships everywhere), noting why (spare needs planner state).

- [ ] **Step 6: Commit**

```bash
cd /c/Projects/FlatSpace && git add Assets/Flatspace/GameAI/GameAIMap.cs Assets/Flatspace/Diagnostics/AITuningLogger.cs Assets/Flatspace/GameAI/GameAI.cs Assets/Flatspace/Objects/Board/GameBoard.cs Assets/Editor/ChokepointSelfCheck.cs CLAUDE.md FUTURE_FEATURES.md .claude/skills/tuning-log/SKILL.md docs/superpowers/specs/2026-10-02-connectivity-targeting-design.md && git commit -m "$(cat <<'EOF'
feat: chokepoint tuning-log lines (Chokepoints, ChokepointColonize, ChokepointGarrison) and docs

Co-Authored-By: Claude Sonnet 5.5 <noreply@anthropic.com>
EOF
)"
```

---

## Self-review (run by the planner against the spec)

- **Spec coverage:** section 1 (`PlanetCentrality`, percentile, init wiring, `GameAIMap` API) is Task 1; section 2 (category 4, `MaintainsGarrison`, `highTrafficConnectionCount` removal) is Task 2; section 3 (ranking step, `Chokepoint` reason) is Task 3; section 4 (tilt, real-cost delay) is Task 4; section 5 (three log lines, skill and `CLAUDE.md` updates) is Tasks 4 and 5; section 6 (tests) is spread across Tasks 1-5. Recorded tuning alternative (chokepoint before recent cut) is in the spec and in `FUTURE_FEATURES.md` (Task 5). Out-of-scope items are untouched.
- **Ledgered deviations:** `ChokepointGarrison` name and fields (Task 5 updates the spec); `ChokepointColonize` is logged only when the divisor is above 1 for the chosen target (the tilt was active).
- **Types:** `Compute`, `Betweenness`, `Percentile`, `Top`, `Chokepoint`, `IsChokepoint`, `TopChokepoints`, `ChokepointSummary`, `ReasonChokepoint`, `LogChokepointColonize`, `FormatChokepointList`, `LogChokepoints`, `LogChokepointGarrison` are spelled identically wherever used.
- **Known consequence to tell the user:** chokepoint garrisons raise `WantedWarships` (the Consolidate fleet cap input), because it sums the Consolidate planner's round garrisons; bounded by `warshipsPerColonizedPlanet`. The spare-ship calculation itself is unchanged.
