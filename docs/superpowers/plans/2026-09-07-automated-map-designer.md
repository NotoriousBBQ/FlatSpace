# Automated Map Designer Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add an editor-invoked procedural board generator that turns a small parameter set (player count, planet count, per-type frequency weights) into a complete, validated, playable FlatSpace board.

**Architecture:** A `MapGenSettings` ScriptableObject holds the parameters. A pure C# `MapGenerator.Generate(settings)` runs the pipeline — blue-noise planet placement, farthest-point Prime placement, a spanning-tree + extra-edge connection graph, constrained (no same-type-adjacency) type assignment, validation with seeded retries — and returns a `GenerationResult`. A `[ContextMenu]` on `BoardDesigner` calls it and populates the `MapDesigner` scene with `PlanetDesigner` children. Connections become first-class data: an explicit edge list rides on `PlanetDesigner` → `BoardDesignerSave` → `PlanetSpawnData` → `Planet`, and `PathingSystem` honors it, falling back to the existing within-`MaxConnectionSize` rule whenever a board carries no explicit edges.

**Tech Stack:** Unity 6000.4.1f1, C#, `JsonUtility` saves, `UnityEngine.Random`/`System.Random`, uGUI designer scene. No automated test framework.

**Spec:** `docs/superpowers/specs/2026-09-07-automated-map-designer-design.md`

## Global Constraints

- **No automated test framework exists in this repo** (`com.unity.test-framework` is a dependency but unused — see `CLAUDE.md`). Every task's verification is: (a) a Rider compile/inspection check via the `mcp__rider__*` tools, and (b) where there is observable behavior, a specific manual check the executor runs (or hands to the user) in the Unity Editor. **Do not add a test assembly** — that is new infrastructure the user has not asked for.
- **`Planet.PlanetType` is serialized by `JsonUtility` as its underlying int** (via `BoardDesignerSave.BoardDesignerEntry.planetType` and `PlanetSpawnData._planetType`). Do not reorder the `Planet.PlanetType` enum.
- **Namespace casing is inconsistent and load-bearing.** `BoardDesigner`/`PlanetDesigner` live in `namespace FlatSpace.Tools`. `PathingSystem` lives in `namespace FlatSpace.Pathing`. `Planet`, `PlanetSpawnData`, `BoardConfiguration` are in the **global namespace**. New files match the namespace of the folder/type they most belong to (stated per task).
- **Misspelled identifiers are load-bearing** (`IntialBoardState`, `InitGameFromGaveSave`, etc.). Do not "fix" any existing identifier spelling.
- **New serialized fields must default to empty/zero** so existing `BoardDesignerSave` JSON, `BoardConfiguration` assets, and hand-built `MapDesigner` scenes keep working with no migration. The fallback rule is: a board where **no** planet carries any explicit connection uses the current distance-only graph.
- **Prefab/scene YAML is not hand-authored.** The one new asset (a `MapGenSettings` instance) and the one new serialized reference on the `BoardDesigner` component (the `PlanetDesigner` prefab) are called out as explicit manual Editor steps with exact values.
- **`WarShip` / ships are out of scope.** This plan does not touch `Ship`, `DockedShips`, orders, or the AI.

---

## Task 1: `PlanetTypeDefaults` helper + refactor `GenerateNames`

Extract the inlined per-type strategy/short-name knowledge out of `BoardDesigner.GenerateNames` into one shared helper so the generator (Task 7) and the existing context-menu action cannot drift. **No behavior change.**

**Files:**
- Create: `Assets/Flatspace/BoardDesigner/PlanetTypeDefaults.cs`
- Modify: `Assets/Flatspace/BoardDesigner/BoardDesigner.cs` (the `GenerateNames` method, lines ~65-163)

**Interfaces:**
- Produces:
  - `FlatSpace.Tools.PlanetTypeDefaults.StrategyFor(Planet.PlanetType) -> Planet.PlanetStrategy`
  - `FlatSpace.Tools.PlanetTypeDefaults.ShortName(Planet.PlanetType) -> string`
  - `FlatSpace.Tools.PlanetTypeDefaults.AllTypes -> Planet.PlanetType[]` (enum values in declaration order)

- [ ] **Step 1: Create `PlanetTypeDefaults.cs`**

```csharp
using System;
using UnityEngine;

namespace FlatSpace
{
    namespace Tools
    {
        /// <summary>
        /// Single source of truth for the per-planet-type designer defaults:
        /// the default <see cref="Planet.PlanetStrategy"/> and the short display
        /// name used for "{ShortName} {n}" planet naming. Used by both the manual
        /// "Generate Names And Strategies" action and the random board generator.
        /// </summary>
        public static class PlanetTypeDefaults
        {
            public static readonly Planet.PlanetType[] AllTypes =
                (Planet.PlanetType[])Enum.GetValues(typeof(Planet.PlanetType));

            public static Planet.PlanetStrategy StrategyFor(Planet.PlanetType type)
            {
                switch (type)
                {
                    case Planet.PlanetType.PlanetTypeDesolate:   return Planet.PlanetStrategy.PlanetStrategyFocusedGrotsits;
                    case Planet.PlanetType.PlanetTypeFarm:       return Planet.PlanetStrategy.PlanetStrategyFood;
                    case Planet.PlanetType.PlanetTypeIndustrial: return Planet.PlanetStrategy.PlanetStrategyFocusedIndustry;
                    case Planet.PlanetType.PlanetTypeNormal:     return Planet.PlanetStrategy.PlanetStrategyBalanced;
                    case Planet.PlanetType.PlanetTypePrime:      return Planet.PlanetStrategy.PlanetStrategyBalanced;
                    case Planet.PlanetType.PlanetTypeVerdant:    return Planet.PlanetStrategy.PlanetStrategyFocusedFood;
                    case Planet.PlanetType.PlanetTypeOcean:      return Planet.PlanetStrategy.PlanetStrategyFocusedResearch;
                    case Planet.PlanetType.PlanetTypeDesert:     return Planet.PlanetStrategy.PlanetStrategyIndustry;
                    default:                                     return Planet.PlanetStrategy.PlanetStrategyBalanced;
                }
            }

            public static string ShortName(Planet.PlanetType type)
            {
                const string prefix = "PlanetType";
                var name = type.ToString();
                return name.StartsWith(prefix) ? name.Substring(prefix.Length) : name;
            }
        }
    }
}
```

- [ ] **Step 2: Compile-check the new file**

Run `mcp__rider__get_file_problems` on `Assets/Flatspace/BoardDesigner/PlanetTypeDefaults.cs` with `errorsOnly: true`.
Expected: no errors.

- [ ] **Step 3: Refactor `BoardDesigner.GenerateNames` to use the helper**

Replace the entire body of the `GenerateNames()` method (currently one hardcoded `foreach` block per type, lines ~67-162) with:

```csharp
[ContextMenu("Generate Names And Strategies")]
public void GenerateNames()
{
    var planetList = new List<PlanetDesigner>();
    foreach (Transform child in transform)
    {
        if (child.GetComponent<PlanetDesigner>())
            planetList.Add(child.GetComponent<PlanetDesigner>());
    }

    Debug.Log($"Total Num Planets {planetList.Count}");

    foreach (var type in PlanetTypeDefaults.AllTypes)
    {
        var count = 0;
        foreach (var planet in planetList.FindAll(x => x.type == type))
        {
            planet.name = $"{PlanetTypeDefaults.ShortName(type)} {count}";
            planet.planetName = planet.name;
            planet.strategy = PlanetTypeDefaults.StrategyFor(type);
            planet.UpdateGraphic();
            count++;
        }
        Debug.Log($"{count} {PlanetTypeDefaults.ShortName(type)}");
    }
}
```

This produces identical names (`"Desolate 0"`, `"Farm 0"`, …), identical strategies, and the same per-type `Debug.Log` lines as before — only the ordering of the log lines changes (now enum-declaration order: Prime, Normal, Farm, Verdant, Industrial, Desolate, Ocean, Desert).

- [ ] **Step 4: Compile-check `BoardDesigner.cs`**

Run `mcp__rider__get_file_problems` on `Assets/Flatspace/BoardDesigner/BoardDesigner.cs` with `errorsOnly: true`.
Expected: no errors.

- [ ] **Step 5: Commit**

```bash
git add Assets/Flatspace/BoardDesigner/PlanetTypeDefaults.cs Assets/Flatspace/BoardDesigner/PlanetTypeDefaults.cs.meta Assets/Flatspace/BoardDesigner/BoardDesigner.cs
git commit -m "Extract per-type designer defaults into PlanetTypeDefaults"
```

---

## Task 2: Explicit-connection runtime data model

Add the fields that carry an explicit edge list from spawn data into the pathing graph, and teach `PathingSystem` to use them — with the current distance rule as the fallback. No generator yet; nothing writes a non-empty connection list after this task, so behavior is unchanged.

**Files:**
- Modify: `Assets/Flatspace/Objects/Planets/ScritpableObjects/SpawnData/PlanetSpawnData.cs`
- Modify: `Assets/Flatspace/Objects/Planets/Planet.cs` (field block near line 124; `Init` near line 188)
- Modify: `Assets/Flatspace/Objects/Pathing/PathingSystem.cs` (`InitializePathMap`, lines 60-84)

**Interfaces:**
- Consumes: nothing new.
- Produces:
  - `PlanetSpawnData._connections : List<string>` (planet names; empty by default)
  - `Planet.Connections : List<string>` (public field, populated in `Planet.Init`)
  - `PathingSystem.InitializePathMap(List<Planet>)` unchanged signature; now builds from explicit edges when any planet has them.

- [ ] **Step 1: Add `_connections` to `PlanetSpawnData`**

Full new file contents:

```csharp
using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(fileName = "PlanetSpawnData", menuName = "Scriptable Objects/PlanetSpawnData")]
public class PlanetSpawnData : ScriptableObject
{
    public string _planetName;
    public Vector3 _planetPosition;
    public Planet.PlanetType _planetType;
    public PlanetResourceData _resourceData;

    // Names of planets this planet is directly connected to. Empty => the
    // pathing graph falls back to the "everything within MaxConnectionSize"
    // rule for this board (see PathingSystem.InitializePathMap).
    public List<string> _connections = new List<string>();
}
```

- [ ] **Step 2: Add `Connections` to `Planet` and populate it in `Init`**

In the public field block (near `public Vector2 Position { get; private set; }`, line ~125) add:

```csharp
    // Names of directly-connected planets for the pathing graph. Empty on a
    // board that predates the random generator => distance-rule fallback.
    public List<string> Connections = new List<string>();
```

In `Planet.Init` (near line 201, right after `Position = new Vector2(...)`) add:

```csharp
        Connections = spawnData._connections != null
            ? new List<string>(spawnData._connections)
            : new List<string>();
```

- [ ] **Step 3: Teach `PathingSystem.InitializePathMap` to use explicit edges**

Replace the body of `InitializePathMap` (lines 60-84) with:

```csharp
public void InitializePathMap(List<Planet> planets)
{
    PathNodes.Clear();
    foreach (var planet in planets)
    {
        PathNodes[planet.PlanetName] =
            new PathNode(planet.PlanetName, new Vector2(planet.Position.x, planet.Position.y));
    }

    var useExplicit = planets.Any(p => p.Connections != null && p.Connections.Count > 0);
    if (useExplicit)
        BuildExplicitConnections(planets);
    else
        BuildDistanceConnections();
}

// Current behavior: an edge between every pair of nodes closer than MaxConnectionSize.
private void BuildDistanceConnections()
{
    foreach (var pathNode in PathNodes.Values)
    {
        foreach (var possibleNeighborNode in PathNodes.Values)
        {
            if (possibleNeighborNode.Name == pathNode.Name)
                continue;
            var distance = Vector2.Distance(pathNode.Position, possibleNeighborNode.Position);
            if (distance <= MaxConnectionSize)
                pathNode.Connections.Add(new Connection(possibleNeighborNode.Name, distance));
        }
    }
}

// Board-authored graph: edges come from Planet.Connections. Symmetrized (an
// edge A-B is added even if only A lists B) and de-duplicated. Unknown names
// are logged and skipped. Cost is straight-line distance, keeping the A*
// heuristic in FindPath admissible.
private void BuildExplicitConnections(List<Planet> planets)
{
    void AddEdge(string from, string to)
    {
        if (!PathNodes.ContainsKey(from) || !PathNodes.ContainsKey(to))
        {
            Debug.LogWarning($"[PathingSystem] connection '{from}' -> '{to}' names an unknown planet; skipped");
            return;
        }
        var node = PathNodes[from];
        if (node.Connections.Any(c => c.NodeName == to))
            return;
        var cost = Vector2.Distance(node.Position, PathNodes[to].Position);
        node.Connections.Add(new Connection(to, cost));
    }

    foreach (var planet in planets)
    {
        if (planet.Connections == null)
            continue;
        foreach (var neighbor in planet.Connections)
        {
            AddEdge(planet.PlanetName, neighbor);
            AddEdge(neighbor, planet.PlanetName);
        }
    }
}
```

`System.Linq` is already imported at the top of `PathingSystem.cs` (line 3). `PathNode.Connections` is a reference-type `List<Connection>` created in the `PathNode` constructor, so mutating `node.Connections` via the `foreach` copy still updates the stored node (the list instance is shared) — this matches how `BuildDistanceConnections` already works.

- [ ] **Step 4: Compile-check**

Run `mcp__rider__get_file_problems` on each of the three modified files with `errorsOnly: true`.
Expected: no errors.

- [ ] **Step 5: Manual regression check (hand-built board still identical)**

Ask the user to, in the Unity Editor:
1. Open `Assets/Scenes/Flatspace.unity`, enter Play mode with an existing hand-built board config (the normal startup path).
2. Confirm the map loads, connection lines draw, and a few turns run with no console errors.

Because no planet has a non-empty `Connections` list, `BuildDistanceConnections` runs and the graph is byte-identical to before. Record the result in the ledger (see Task 9).

- [ ] **Step 6: Commit**

```bash
git add Assets/Flatspace/Objects/Planets/ScritpableObjects/SpawnData/PlanetSpawnData.cs Assets/Flatspace/Objects/Planets/Planet.cs Assets/Flatspace/Objects/Pathing/PathingSystem.cs
git commit -m "Carry an explicit connection list into the pathing graph (distance rule stays the fallback)"
```

---

## Task 3: Save/load + designer connection plumbing

Persist explicit connections through the board designer save format and store them on `PlanetDesigner` so they survive a domain reload. Repoint the manual "Generate Connections" action and the preview drawing at the new serialized list.

**Files:**
- Modify: `Assets/Flatspace/BoardDesigner/PlanetDesigner.cs`
- Modify: `Assets/Flatspace/SaveSystem/SaveLoadSystem.cs` (`BoardDesignerSave`, lines 258-287)
- Modify: `Assets/Flatspace/Objects/Board/GameBoard.cs` (`InitGame(BoardDesignerSave)`, lines 277-309)
- Modify: `Assets/Flatspace/BoardDesigner/BoardDesigner.cs` (`GenerateStarConnections`, `GetConnectionVectors`, `DrawConnections`)

**Interfaces:**
- Consumes: `PlanetSpawnData._connections` (Task 2).
- Produces:
  - `PlanetDesigner.connectionNames : List<string>` (serialized)
  - `PlanetDesigner.RebuildConnectionsFromNames(IReadOnlyDictionary<string, PlanetDesigner>)` — repopulates the in-memory `Connections` cache
  - `PlanetDesigner.SetConnectionNames(IEnumerable<string>)` — replaces `connectionNames`
  - `SaveLoadSystem.BoardDesignerSave.BoardDesignerEntry.connections : List<string>`

- [ ] **Step 1: Add serialized connection storage to `PlanetDesigner`**

In `PlanetDesigner` (namespace `FlatSpace.Tools`), add a serialized field next to `Connections` (line ~30):

```csharp
            // Serialized source of truth for this planet's connections in the
            // designer. The Connections list above is an in-memory cache rebuilt
            // from these names via RebuildConnectionsFromNames.
            public List<string> connectionNames = new List<string>();
```

Add these methods to the class:

```csharp
            public void SetConnectionNames(IEnumerable<string> names)
            {
                connectionNames = new List<string>();
                foreach (var n in names)
                {
                    if (!string.IsNullOrEmpty(n) && n != planetName && !connectionNames.Contains(n))
                        connectionNames.Add(n);
                }
            }

            public void RebuildConnectionsFromNames(IReadOnlyDictionary<string, PlanetDesigner> byName)
            {
                Connections.Clear();
                foreach (var name in connectionNames)
                {
                    if (!byName.TryGetValue(name, out var target) || target == this)
                        continue;
                    var cost = Vector2.Distance(
                        new Vector2(transform.localPosition.x, transform.localPosition.y),
                        new Vector2(target.transform.localPosition.x, target.transform.localPosition.y));
                    Connections.Add(new DesignerConnection(target, cost));
                }
            }
```

- [ ] **Step 2: Add `connections` to `BoardDesignerSave.BoardDesignerEntry`**

In `SaveLoadSystem.cs`, change the struct and constructor (lines 263-286):

```csharp
        [Serializable]
        public struct BoardDesignerEntry
        {
            public string name;
            public Planet.PlanetType planetType;
            public Vector2 position;
            public List<string> connections;
        }

        public List<BoardDesignerEntry> planetEntries = new List<BoardDesignerEntry>();

        public BoardDesignerSave(List<PlanetDesigner> planetList)
        {
            planetEntries.Clear();
            foreach (var planet in planetList)
            {
                planetEntries.Add(new BoardDesignerEntry
                {
                    name = planet.planetName,
                    planetType = planet.type,
                    position = planet.transform.localPosition,
                    connections = new List<string>(planet.connectionNames ?? new List<string>()),
                });
            }
        }
```

Old save JSON without a `connections` key deserializes that field as `null` — handled in Step 3.

- [ ] **Step 3: Copy connections into spawn data in `GameBoard.InitGame(BoardDesignerSave)`**

In the `foreach (var planetDesignData in boardData.planetEntries)` loop (lines 295-305), after `spawnData._planetType = ...` add:

```csharp
                    spawnData._connections = planetDesignData.connections != null
                        ? new List<string>(planetDesignData.connections)
                        : new List<string>();
```

Confirm `System.Collections.Generic` is imported in `GameBoard.cs` (it is — `List<>` is used throughout).

- [ ] **Step 4: Repoint `GenerateStarConnections` and preview drawing at `connectionNames`**

In `BoardDesigner.cs`, rewrite `GenerateStarConnections` so it writes the serialized names, then rebuilds caches, then draws:

```csharp
[ContextMenu("Generate Connections")]
public void GenerateStarConnections()
{
    var planetList = new List<PlanetDesigner>();
    foreach (Transform child in transform)
    {
        if (child.GetComponent<PlanetDesigner>())
            planetList.Add(child.GetComponent<PlanetDesigner>());
    }

    // Build the within-range name lists (symmetric).
    var names = new Dictionary<PlanetDesigner, List<string>>();
    foreach (var planet in planetList)
        names[planet] = new List<string>();

    for (var i = 0; i < planetList.Count; i++)
    {
        for (var j = i + 1; j < planetList.Count; j++)
        {
            var a = planetList[i];
            var b = planetList[j];
            var distance = Vector2.Distance(
                new Vector2(a.transform.localPosition.x, a.transform.localPosition.y),
                new Vector2(b.transform.localPosition.x, b.transform.localPosition.y));
            if (distance <= MaxConnectionSize)
            {
                names[a].Add(b.planetName);
                names[b].Add(a.planetName);
            }
        }
    }

    foreach (var planet in planetList)
        planet.SetConnectionNames(names[planet]);

    RebuildAllConnectionCaches(planetList);
    DrawConnections(planetList);
}

private void RebuildAllConnectionCaches(List<PlanetDesigner> planetList)
{
    var byName = new Dictionary<string, PlanetDesigner>();
    foreach (var planet in planetList)
        if (!string.IsNullOrEmpty(planet.planetName))
            byName[planet.planetName] = planet;
    foreach (var planet in planetList)
        planet.RebuildConnectionsFromNames(byName);
}
```

`GenerateStarConnections` now depends on planet names being set, so its context-menu help text is effectively "run Generate Names And Strategies first." Add a guard at the top of the method, right after `planetList` is built:

```csharp
    if (planetList.Any(p => string.IsNullOrEmpty(p.planetName)))
    {
        Debug.LogError("[BoardDesigner] Generate Connections needs planet names; run 'Generate Names And Strategies' first.");
        return;
    }
```

Add `using System.Linq;` to the top of `BoardDesigner.cs` if not present (it is not — add it).

`GetConnectionVectors` and `DrawConnections` are unchanged: they already read `planet.Connections`, which `RebuildAllConnectionCaches` now populates.

- [ ] **Step 5: Compile-check**

Run `mcp__rider__get_file_problems` on `PlanetDesigner.cs`, `SaveLoadSystem.cs`, `GameBoard.cs`, `BoardDesigner.cs` with `errorsOnly: true`.
Expected: no errors.

- [ ] **Step 6: Manual check (designer round-trip)**

Ask the user to, in the Unity Editor:
1. Open `Assets/Scenes/MapDesigner.unity`.
2. On the `BoardDesigner` component, run `Generate Names And Strategies`, then `Generate Connections`. Confirm preview lines still draw as before.
3. Click the Save button; save to a temp `.json`. Open the JSON and confirm each entry now has a `connections` list of planet names.
4. Load that JSON via the game's load path into `Flatspace` and confirm the map plays. Because every planet now has explicit connections, `BuildExplicitConnections` runs; the edge set matches what the designer drew.

- [ ] **Step 7: Commit**

```bash
git add Assets/Flatspace/BoardDesigner/PlanetDesigner.cs Assets/Flatspace/SaveSystem/SaveLoadSystem.cs Assets/Flatspace/Objects/Board/GameBoard.cs Assets/Flatspace/BoardDesigner/BoardDesigner.cs
git commit -m "Persist explicit designer connections through save/load"
```

---

## Task 4: `MapGenSettings` ScriptableObject + `MapGenerator` skeleton + dry-run hook

Create the parameter asset type, the generator entry point (returning validation errors and the resolved type-count distribution only), and a `[ContextMenu]` on `BoardDesigner` that runs the generator and logs — the verification surface for Tasks 4-7.

**Files:**
- Create: `Assets/Flatspace/BoardDesigner/MapGenSettings.cs`
- Create: `Assets/Flatspace/BoardDesigner/MapGenerator.cs`
- Modify: `Assets/Flatspace/BoardDesigner/BoardDesigner.cs`

**Interfaces:**
- Consumes: `PlanetTypeDefaults` (Task 1).
- Produces:
  - `MapGenSettings : ScriptableObject` (global namespace, to match `PlanetSpawnData`) with fields: `int seed`, `int playerCount`, `int totalPlanetCount`, `List<TypeWeight> typeWeights`, `float minPlanetSeparation`, `float connectionRadius`, `float nominalSpacing`, `int seedRetryBudget`.
  - `struct TypeWeight { Planet.PlanetType type; float weight; }` (global namespace)
  - `FlatSpace.Tools.MapGenerator` (static) with `GenerationResult Generate(MapGenSettings settings)`
  - `FlatSpace.Tools.GeneratedPlanet` struct: `string Name; Planet.PlanetType Type; Planet.PlanetStrategy Strategy; Vector2 Position; List<string> Connections;`
  - `FlatSpace.Tools.GenerationResult` class: `bool Success; string Error; int EffectiveSeed; List<GeneratedPlanet> Planets;`
  - `BoardDesigner.mapGenSettings` serialized field; `BoardDesigner.MapGenDryRun()` `[ContextMenu]`

- [ ] **Step 1: Create `MapGenSettings.cs`**

```csharp
using System;
using System.Collections.Generic;
using UnityEngine;

[Serializable]
public struct TypeWeight
{
    public Planet.PlanetType type;
    public float weight;
}

[CreateAssetMenu(fileName = "MapGenSettings", menuName = "Scriptable Objects/MapGenSettings")]
public class MapGenSettings : ScriptableObject
{
    [Header("Composition")]
    [Tooltip("0 = pick and log a random seed; non-zero = reproducible output.")]
    public int seed = 0;

    [Tooltip("Number of Prime (capital) planets == number of players.")]
    public int playerCount = 2;

    [Tooltip("Total planets, including the Primes.")]
    public int totalPlanetCount = 20;

    [Tooltip("Relative frequency weights for the non-Prime types. weight 0 excludes a type. Prime entries are ignored.")]
    public List<TypeWeight> typeWeights = new List<TypeWeight>();

    [Header("Geometry")]
    [Tooltip("Minimum distance between any two planets (blue-noise rejection radius).")]
    public float minPlanetSeparation = 120f;

    [Tooltip("Two planets closer than this are candidates for a connection.")]
    public float connectionRadius = 400f;

    [Tooltip("Reference spacing; map side length = sqrt(totalPlanetCount) * this.")]
    public float nominalSpacing = 200f;

    [Header("Robustness")]
    [Tooltip("How many different seeds to try before giving up when constraints can't be satisfied.")]
    public int seedRetryBudget = 8;
}
```

- [ ] **Step 2: Create `MapGenerator.cs` (skeleton: validation + seed + type counts)**

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Random = System.Random;

namespace FlatSpace
{
    namespace Tools
    {
        public struct GeneratedPlanet
        {
            public string Name;
            public Planet.PlanetType Type;
            public Planet.PlanetStrategy Strategy;
            public Vector2 Position;
            public List<string> Connections;
        }

        public class GenerationResult
        {
            public bool Success;
            public string Error;
            public int EffectiveSeed;
            public List<GeneratedPlanet> Planets = new List<GeneratedPlanet>();

            public static GenerationResult Fail(string error, int seed) =>
                new GenerationResult { Success = false, Error = error, EffectiveSeed = seed };
        }

        public static class MapGenerator
        {
            private const float ExtraEdgeFraction = 0.25f;

            public static GenerationResult Generate(MapGenSettings settings)
            {
                var settingsError = ValidateSettings(settings);
                if (settingsError != null)
                    return GenerationResult.Fail(settingsError, 0);

                var baseSeed = settings.seed != 0 ? settings.seed : new Random().Next(1, int.MaxValue);

                GenerationResult lastFailure = null;
                for (var attempt = 0; attempt < Mathf.Max(1, settings.seedRetryBudget); attempt++)
                {
                    var seed = baseSeed + attempt;
                    Debug.Log($"[MapGenerator] attempt {attempt + 1}, seed {seed}");
                    var result = TryGenerate(settings, seed);
                    if (result.Success)
                        return result;
                    lastFailure = result;
                }
                return lastFailure ?? GenerationResult.Fail("generation failed with no diagnostic", baseSeed);
            }

            private static string ValidateSettings(MapGenSettings s)
            {
                if (s == null) return "MapGenSettings is null";
                if (s.playerCount < 1) return "playerCount must be >= 1";
                if (s.totalPlanetCount <= s.playerCount)
                    return $"totalPlanetCount ({s.totalPlanetCount}) must be > playerCount ({s.playerCount})";
                var eligible = EligibleWeights(s);
                if (eligible.Count == 0)
                    return "no typeWeights entry with weight > 0 and a non-Prime type";
                if (s.minPlanetSeparation <= 0f) return "minPlanetSeparation must be > 0";
                if (s.connectionRadius <= 0f) return "connectionRadius must be > 0";
                if (s.nominalSpacing <= 0f) return "nominalSpacing must be > 0";
                return null;
            }

            private static List<TypeWeight> EligibleWeights(MapGenSettings s) =>
                s.typeWeights
                    .Where(w => w.weight > 0f && w.type != Planet.PlanetType.PlanetTypePrime)
                    .ToList();

            // Distribute nonPrimeCount planets across the eligible types by their
            // normalized weights, using largest-remainder rounding so the parts
            // sum to exactly nonPrimeCount.
            private static List<Planet.PlanetType> ResolveTypeMultiset(MapGenSettings s, int nonPrimeCount)
            {
                var weights = EligibleWeights(s);
                var total = weights.Sum(w => w.weight);
                var exact = weights.Select(w => (type: w.type, value: w.weight / total * nonPrimeCount)).ToList();

                var counts = exact.Select(e => (e.type, count: Mathf.FloorToInt((float)e.value))).ToList();
                var assigned = counts.Sum(c => c.count);
                var remainders = exact
                    .Select((e, i) => (i, frac: e.value - Mathf.FloorToInt((float)e.value)))
                    .OrderByDescending(x => x.frac)
                    .ToList();

                var idx = 0;
                while (assigned < nonPrimeCount)
                {
                    var slot = remainders[idx % remainders.Count].i;
                    counts[slot] = (counts[slot].type, counts[slot].count + 1);
                    assigned++;
                    idx++;
                }

                var multiset = new List<Planet.PlanetType>();
                foreach (var c in counts)
                    for (var k = 0; k < c.count; k++)
                        multiset.Add(c.type);
                return multiset;
            }

            // Filled in over Tasks 5-7. For now: resolve counts and report them,
            // then fail so the dry-run has something to print.
            private static GenerationResult TryGenerate(MapGenSettings settings, int seed)
            {
                var rng = new Random(seed);
                var primeCount = settings.playerCount;
                var nonPrimeCount = settings.totalPlanetCount - primeCount;
                var typeMultiset = ResolveTypeMultiset(settings, nonPrimeCount);

                var summary = string.Join(", ", typeMultiset
                    .GroupBy(t => t)
                    .Select(g => $"{PlanetTypeDefaults.ShortName(g.Key)}:{g.Count()}"));
                Debug.Log($"[MapGenerator] seed {seed}: {primeCount} Prime, non-Prime [{summary}]");

                return GenerationResult.Fail("placement not implemented yet (Task 5)", seed);
            }
        }
    }
}
```

- [ ] **Step 3: Add the settings reference and dry-run action to `BoardDesigner`**

Add a serialized field near `lineDrawObjectPrefab`:

```csharp
            [SerializeField] private MapGenSettings mapGenSettings;
```

Add:

```csharp
            [ContextMenu("Map Gen: Dry Run")]
            public void MapGenDryRun()
            {
                if (!mapGenSettings)
                {
                    Debug.LogError("[BoardDesigner] assign a MapGenSettings asset first");
                    return;
                }
                var result = FlatSpace.Tools.MapGenerator.Generate(mapGenSettings);
                Debug.Log(result.Success
                    ? $"[BoardDesigner] dry run OK: {result.Planets.Count} planets, seed {result.EffectiveSeed}"
                    : $"[BoardDesigner] dry run failed: {result.Error}");
            }
```

- [ ] **Step 4: Compile-check**

Run `mcp__rider__get_file_problems` on `MapGenSettings.cs`, `MapGenerator.cs`, `BoardDesigner.cs` with `errorsOnly: true`.
Expected: no errors.

- [ ] **Step 5: Manual smoke test**

Ask the user to:
1. Create a `MapGenSettings` asset (`Assets/ → Create → Scriptable Objects → MapGenSettings`), name it `MapGenSettings_Default`, put it under `Assets/Flatspace/BoardDesigner/`.
2. Set `playerCount = 2`, `totalPlanetCount = 20`, and add `typeWeights` entries: Normal 3, Farm 2, Verdant 1, Industrial 2, Desolate 1.
3. Open `MapDesigner.unity`, assign the asset to `BoardDesigner.mapGenSettings`.
4. Run the `Map Gen: Dry Run` context menu. Expected console output: the per-attempt seed line, the composition line (`2 Prime, non-Prime [Normal:X, Farm:Y, ...]` summing to 18), and `dry run failed: placement not implemented yet (Task 5)`.

- [ ] **Step 6: Commit**

```bash
git add Assets/Flatspace/BoardDesigner/MapGenSettings.cs Assets/Flatspace/BoardDesigner/MapGenSettings.cs.meta Assets/Flatspace/BoardDesigner/MapGenerator.cs Assets/Flatspace/BoardDesigner/MapGenerator.cs.meta Assets/Flatspace/BoardDesigner/BoardDesigner.cs
git commit -m "Add MapGenSettings asset and MapGenerator skeleton with dry-run hook"
```

---

## Task 5: `MapGenerator` — blue-noise placement + Prime selection

Fill in planet placement: blue-noise scatter in a count-scaled extent, then pick Prime slots by farthest-point sampling.

**Files:**
- Modify: `Assets/Flatspace/BoardDesigner/MapGenerator.cs`

**Interfaces:**
- Consumes: `ResolveTypeMultiset` (Task 4).
- Produces (internal, consumed by Tasks 6-7): a `List<Vector2> positions` and a `HashSet<int> primeIndices` inside `TryGenerate`, plus a private `PlacePlanets` helper.

- [ ] **Step 1: Add placement helpers to `MapGenerator`**

```csharp
            // Rejection-sampled blue-noise scatter. Returns null if it cannot
            // place `count` points respecting `minSeparation` even after growing
            // the extent several times.
            private static List<Vector2> PlacePlanets(Random rng, int count, float minSeparation, float nominalSpacing)
            {
                var side = Mathf.Sqrt(count) * nominalSpacing;
                var half = side / 2f;
                var minSqr = minSeparation * minSeparation;
                var points = new List<Vector2>();

                var stall = 0;
                var grows = 0;
                while (points.Count < count)
                {
                    var candidate = new Vector2(
                        (float)(rng.NextDouble() * 2.0 - 1.0) * half,
                        (float)(rng.NextDouble() * 2.0 - 1.0) * half);

                    var ok = true;
                    foreach (var p in points)
                    {
                        if ((p - candidate).sqrMagnitude < minSqr) { ok = false; break; }
                    }

                    if (ok)
                    {
                        points.Add(candidate);
                        stall = 0;
                    }
                    else if (++stall > 40)
                    {
                        if (++grows > 6)
                            return null;
                        half *= 1.1f;
                        stall = 0;
                    }
                }
                return points;
            }

            // Farthest-point sampling: pick `primeCount` indices out of `positions`
            // that are spread as far apart as possible.
            private static HashSet<int> PickPrimeIndices(Random rng, List<Vector2> positions, int primeCount)
            {
                var chosen = new HashSet<int> { rng.Next(positions.Count) };
                while (chosen.Count < primeCount)
                {
                    var bestIdx = -1;
                    var bestDist = -1f;
                    for (var i = 0; i < positions.Count; i++)
                    {
                        if (chosen.Contains(i)) continue;
                        var nearest = float.MaxValue;
                        foreach (var c in chosen)
                            nearest = Mathf.Min(nearest, (positions[i] - positions[c]).sqrMagnitude);
                        if (nearest > bestDist) { bestDist = nearest; bestIdx = i; }
                    }
                    chosen.Add(bestIdx);
                }
                return chosen;
            }
```

- [ ] **Step 2: Wire placement into `TryGenerate`**

Replace the body of `TryGenerate` with:

```csharp
            private static GenerationResult TryGenerate(MapGenSettings settings, int seed)
            {
                var rng = new Random(seed);
                var primeCount = settings.playerCount;
                var nonPrimeCount = settings.totalPlanetCount - primeCount;
                var typeMultiset = ResolveTypeMultiset(settings, nonPrimeCount);

                var positions = PlacePlanets(rng, settings.totalPlanetCount,
                    settings.minPlanetSeparation, settings.nominalSpacing);
                if (positions == null)
                    return GenerationResult.Fail(
                        "could not place planets: minPlanetSeparation too large for the planet count", seed);

                var primeIndices = PickPrimeIndices(rng, positions, primeCount);

                Debug.Log($"[MapGenerator] seed {seed}: placed {positions.Count} planets, " +
                          $"prime indices [{string.Join(",", primeIndices.OrderBy(i => i))}]");

                return GenerationResult.Fail("connection graph not implemented yet (Task 6)", seed);
            }
```

- [ ] **Step 3: Compile-check**

Run `mcp__rider__get_file_problems` on `MapGenerator.cs` with `errorsOnly: true`.
Expected: no errors.

- [ ] **Step 4: Manual smoke test**

Run `Map Gen: Dry Run` again. Expected: a `placed 20 planets, prime indices [i,j]` line, then `dry run failed: connection graph not implemented yet (Task 6)`. Try `minPlanetSeparation = 5000` and confirm the "could not place planets" failure instead.

- [ ] **Step 5: Commit**

```bash
git add Assets/Flatspace/BoardDesigner/MapGenerator.cs
git commit -m "MapGenerator: blue-noise placement and farthest-point Prime selection"
```

---

## Task 6: `MapGenerator` — connectivity graph

Build the explicit edge set: candidate edges within radius (never Prime-Prime), a minimum spanning tree for guaranteed connectivity, inter-component bridging, then ~25% extra edges.

**Files:**
- Modify: `Assets/Flatspace/BoardDesigner/MapGenerator.cs`

**Interfaces:**
- Consumes: `positions`, `primeIndices` (Task 5).
- Produces (internal, consumed by Task 7): `HashSet<(int, int)> edges` (each pair stored with `a < b`) inside `TryGenerate`, plus private `BuildGraph` helper returning `null` on unrecoverable disconnection.

- [ ] **Step 1: Add the graph builder to `MapGenerator`**

```csharp
            private readonly struct Edge
            {
                public readonly int A;
                public readonly int B;
                public readonly float Cost;
                public Edge(int a, int b, float cost) { A = a; B = b; Cost = cost; }
            }

            // Union-find for Kruskal / component tracking.
            private class DisjointSet
            {
                private readonly int[] _parent;
                public DisjointSet(int n)
                {
                    _parent = new int[n];
                    for (var i = 0; i < n; i++) _parent[i] = i;
                }
                public int Find(int x) => _parent[x] == x ? x : (_parent[x] = Find(_parent[x]));
                public bool Union(int a, int b)
                {
                    int ra = Find(a), rb = Find(b);
                    if (ra == rb) return false;
                    _parent[ra] = rb;
                    return true;
                }
            }

            private static bool IsPrimePair(HashSet<int> primes, int a, int b) =>
                primes.Contains(a) && primes.Contains(b);

            // Returns the undirected edge set (pairs with A < B), or null if the
            // graph cannot be made connected without a Prime-Prime edge.
            private static HashSet<(int, int)> BuildGraph(
                Random rng, List<Vector2> positions, HashSet<int> primes, float connectionRadius)
            {
                var n = positions.Count;

                var candidates = new List<Edge>();
                for (var i = 0; i < n; i++)
                for (var j = i + 1; j < n; j++)
                {
                    if (IsPrimePair(primes, i, j)) continue;
                    var d = Vector2.Distance(positions[i], positions[j]);
                    if (d <= connectionRadius)
                        candidates.Add(new Edge(i, j, d));
                }

                var edges = new HashSet<(int, int)>();
                var ds = new DisjointSet(n);

                // Kruskal MST over in-radius candidates.
                foreach (var e in candidates.OrderBy(e => e.Cost))
                {
                    if (ds.Union(e.A, e.B))
                        edges.Add((e.A, e.B));
                }

                // Bridge any remaining components with the shortest non-Prime-Prime
                // inter-component edge, ignoring the radius limit.
                while (true)
                {
                    var roots = Enumerable.Range(0, n).Select(ds.Find).Distinct().ToList();
                    if (roots.Count <= 1) break;

                    Edge? best = null;
                    for (var i = 0; i < n; i++)
                    for (var j = i + 1; j < n; j++)
                    {
                        if (ds.Find(i) == ds.Find(j)) continue;
                        if (IsPrimePair(primes, i, j)) continue;
                        var d = Vector2.Distance(positions[i], positions[j]);
                        if (best == null || d < best.Value.Cost)
                            best = new Edge(i, j, d);
                    }

                    if (best == null) return null; // only Prime-Prime bridges remain
                    ds.Union(best.Value.A, best.Value.B);
                    edges.Add((best.Value.A, best.Value.B));
                }

                // Add back a fraction of the unused in-radius candidates for loops.
                var unused = candidates
                    .Where(e => !edges.Contains((e.A, e.B)))
                    .ToList();
                var extra = Mathf.RoundToInt(ExtraEdgeFraction * unused.Count);
                for (var k = 0; k < extra && unused.Count > 0; k++)
                {
                    var pick = rng.Next(unused.Count);
                    edges.Add((unused[pick].A, unused[pick].B));
                    unused.RemoveAt(pick);
                }

                return edges;
            }
```

- [ ] **Step 2: Wire the graph into `TryGenerate`**

Replace the final two lines of `TryGenerate` (the `Debug.Log` after `PickPrimeIndices` and the `return GenerationResult.Fail("connection graph not implemented yet (Task 6)", seed);`) with:

```csharp
                var edges = BuildGraph(rng, positions, primeIndices, settings.connectionRadius);
                if (edges == null)
                    return GenerationResult.Fail(
                        "could not connect the map without joining two Prime planets; increase totalPlanetCount or connectionRadius", seed);

                var degree = new int[positions.Count];
                foreach (var (a, b) in edges) { degree[a]++; degree[b]++; }
                Debug.Log($"[MapGenerator] seed {seed}: {edges.Count} edges, " +
                          $"degree min {degree.Min()} max {degree.Max()}, " +
                          $"connected {IsConnected(positions.Count, edges)}");

                return GenerationResult.Fail("type assignment not implemented yet (Task 7)", seed);
```

Add the connectivity check helper:

```csharp
            private static bool IsConnected(int n, HashSet<(int, int)> edges)
            {
                if (n == 0) return true;
                var adj = new Dictionary<int, List<int>>();
                for (var i = 0; i < n; i++) adj[i] = new List<int>();
                foreach (var (a, b) in edges) { adj[a].Add(b); adj[b].Add(a); }

                var seen = new HashSet<int> { 0 };
                var stack = new Stack<int>();
                stack.Push(0);
                while (stack.Count > 0)
                {
                    foreach (var next in adj[stack.Pop()])
                        if (seen.Add(next)) stack.Push(next);
                }
                return seen.Count == n;
            }
```

- [ ] **Step 3: Compile-check**

Run `mcp__rider__get_file_problems` on `MapGenerator.cs` with `errorsOnly: true`.
Expected: no errors.

- [ ] **Step 4: Manual smoke test**

Run `Map Gen: Dry Run`. Expected: an edges line reporting `connected True`, `degree min 1` or higher, then `dry run failed: type assignment not implemented yet (Task 7)`.

- [ ] **Step 5: Commit**

```bash
git add Assets/Flatspace/BoardDesigner/MapGenerator.cs
git commit -m "MapGenerator: spanning-tree connection graph with bridging and extra edges"
```

---

## Task 7: `MapGenerator` — type assignment, validation, emit

Assign the non-Prime type multiset to non-Prime nodes so no edge joins two same-type planets (except Normal-Normal), validate the finished board, and emit the `GeneratedPlanet` list. This is the task that makes `MapGenerator.Generate` succeed.

**Files:**
- Modify: `Assets/Flatspace/BoardDesigner/MapGenerator.cs`

**Interfaces:**
- Consumes: `positions`, `primeIndices`, `edges`, `typeMultiset` (Tasks 5-6).
- Produces: a populated `GenerationResult` (`Success = true`, `Planets` filled) from `Generate` when constraints are satisfiable.

- [ ] **Step 1: Add the constrained type solver**

```csharp
            // Assign `multiset` types to the non-prime node indices so that no
            // edge joins two nodes of the same type unless that type is Normal.
            // Randomized greedy with bounded backtracking. Returns a
            // node-index -> type map, or null on failure.
            private static Dictionary<int, Planet.PlanetType> AssignTypes(
                Random rng, int n, HashSet<int> primes,
                HashSet<(int, int)> edges, List<Planet.PlanetType> multiset)
            {
                var adj = new Dictionary<int, List<int>>();
                for (var i = 0; i < n; i++) adj[i] = new List<int>();
                foreach (var (a, b) in edges) { adj[a].Add(b); adj[b].Add(a); }

                var nodes = Enumerable.Range(0, n)
                    .Where(i => !primes.Contains(i))
                    .OrderByDescending(i => adj[i].Count)
                    .ToList();

                var assignment = new Dictionary<int, Planet.PlanetType>();
                foreach (var p in primes) assignment[p] = Planet.PlanetType.PlanetTypePrime;

                // Remaining count of each type available to hand out.
                var pool = multiset.GroupBy(t => t).ToDictionary(g => g.Key, g => g.Count());
                var steps = 0;
                const int maxSteps = 20000;

                bool Conflicts(int node, Planet.PlanetType type)
                {
                    if (type == Planet.PlanetType.PlanetTypeNormal) return false;
                    foreach (var nb in adj[node])
                        if (assignment.TryGetValue(nb, out var t) && t == type)
                            return true;
                    return false;
                }

                bool Recurse(int idx)
                {
                    if (++steps > maxSteps) return false;
                    if (idx == nodes.Count) return true;
                    var node = nodes[idx];

                    var types = pool.Where(kv => kv.Value > 0).Select(kv => kv.Key).ToList();
                    for (var i = types.Count - 1; i > 0; i--)
                    {
                        var j = rng.Next(i + 1);
                        (types[i], types[j]) = (types[j], types[i]);
                    }

                    foreach (var type in types)
                    {
                        if (Conflicts(node, type)) continue;
                        assignment[node] = type;
                        pool[type]--;
                        if (Recurse(idx + 1)) return true;
                        pool[type]++;
                        assignment.Remove(node);
                    }
                    return false;
                }

                return Recurse(0) ? assignment : null;
            }
```

- [ ] **Step 2: Add validation and replace the tail of `TryGenerate`**

Replace the tail of `TryGenerate` (from the `degree`/`Debug.Log`/`return GenerationResult.Fail("type assignment not implemented yet (Task 7)", seed);` block added in Task 6) with:

```csharp
                var assignment = AssignTypes(rng, positions.Count, primeIndices, edges, typeMultiset);
                if (assignment == null)
                    return GenerationResult.Fail(
                        "could not assign types without same-type neighbors; loosen the frequency weights " +
                        $"(non-Prime multiset was [{Summarize(typeMultiset)}])", seed);

                var validationError = Validate(positions.Count, primeCount, primeIndices, edges, assignment);
                if (validationError != null)
                    return GenerationResult.Fail(validationError, seed);

                var planets = Emit(positions, edges, assignment);
                Debug.Log($"[MapGenerator] seed {seed} OK: {planets.Count} planets, {edges.Count} edges, " +
                          $"non-Prime [{Summarize(typeMultiset)}], connectivity OK");
                return new GenerationResult
                {
                    Success = true,
                    EffectiveSeed = seed,
                    Planets = planets,
                };
```

Add these helpers:

```csharp
            private static string Summarize(IEnumerable<Planet.PlanetType> types) =>
                string.Join(", ", types.GroupBy(t => t)
                    .Select(g => $"{PlanetTypeDefaults.ShortName(g.Key)}:{g.Count()}"));

            private static string Validate(
                int n, int primeCount, HashSet<int> primes,
                HashSet<(int, int)> edges, Dictionary<int, Planet.PlanetType> assignment)
            {
                if (!IsConnected(n, edges))
                    return "internal error: graph not connected after assignment";

                if (assignment.Count(kv => kv.Value == Planet.PlanetType.PlanetTypePrime) != primeCount)
                    return "internal error: Prime count mismatch after assignment";

                var degree = new int[n];
                foreach (var (a, b) in edges)
                {
                    degree[a]++; degree[b]++;
                    if (IsPrimePair(primes, a, b))
                        return "internal error: an edge joins two Prime planets";
                    var ta = assignment[a];
                    var tb = assignment[b];
                    if (ta == tb && ta != Planet.PlanetType.PlanetTypeNormal)
                        return $"internal error: edge joins two {PlanetTypeDefaults.ShortName(ta)} planets";
                }
                for (var i = 0; i < n; i++)
                    if (degree[i] == 0)
                        return "internal error: a planet has no connections";

                return null;
            }

            private static List<GeneratedPlanet> Emit(
                List<Vector2> positions, HashSet<(int, int)> edges,
                Dictionary<int, Planet.PlanetType> assignment)
            {
                var adj = new Dictionary<int, List<int>>();
                for (var i = 0; i < positions.Count; i++) adj[i] = new List<int>();
                foreach (var (a, b) in edges) { adj[a].Add(b); adj[b].Add(a); }

                // Per-type running index for "{ShortName} {n}" names.
                var typeIndex = new Dictionary<Planet.PlanetType, int>();
                var names = new string[positions.Count];
                for (var i = 0; i < positions.Count; i++)
                {
                    var type = assignment[i];
                    typeIndex.TryGetValue(type, out var next);
                    names[i] = $"{PlanetTypeDefaults.ShortName(type)} {next}";
                    typeIndex[type] = next + 1;
                }

                var planets = new List<GeneratedPlanet>();
                for (var i = 0; i < positions.Count; i++)
                {
                    var type = assignment[i];
                    planets.Add(new GeneratedPlanet
                    {
                        Name = names[i],
                        Type = type,
                        Strategy = PlanetTypeDefaults.StrategyFor(type),
                        Position = positions[i],
                        Connections = adj[i].Select(j => names[j]).OrderBy(s => s).ToList(),
                    });
                }
                return planets;
            }
```

- [ ] **Step 3: Update `BoardDesigner.MapGenDryRun` to print a richer success line**

Replace the success branch of the `Debug.Log` in `MapGenDryRun` with:

```csharp
                if (result.Success)
                {
                    var byType = string.Join(", ", result.Planets
                        .GroupBy(p => p.Type)
                        .Select(g => $"{g.Key}:{g.Count()}"));
                    var degrees = result.Planets.Select(p => p.Connections.Count).ToList();
                    Debug.Log($"[BoardDesigner] dry run OK: {result.Planets.Count} planets " +
                              $"[{byType}], degree {degrees.Min()}-{degrees.Max()}, seed {result.EffectiveSeed}");
                }
                else
                {
                    Debug.LogError($"[BoardDesigner] dry run failed: {result.Error}");
                }
```

Ensure `using System.Linq;` is present in `BoardDesigner.cs` (added in Task 3).

- [ ] **Step 4: Compile-check**

Run `mcp__rider__get_file_problems` on `MapGenerator.cs` and `BoardDesigner.cs` with `errorsOnly: true`.
Expected: no errors.

- [ ] **Step 5: Manual verification**

Ask the user to run `Map Gen: Dry Run` several times (with `seed = 0` for variety) and confirm:
- `dry run OK` with planet count == `totalPlanetCount`, Prime count == `playerCount`.
- `degree` minimum ≥ 1.
- Then set `typeWeights` to a single non-Normal type (e.g. only Farm 1) with `totalPlanetCount = 20`, `playerCount = 2` and confirm it fails after `seedRetryBudget` attempts with the "loosen the frequency weights" message.
- Set `typeWeights` to only `Normal 1` and confirm it *succeeds* (Normal may neighbor Normal).

- [ ] **Step 6: Commit**

```bash
git add Assets/Flatspace/BoardDesigner/MapGenerator.cs Assets/Flatspace/BoardDesigner/BoardDesigner.cs
git commit -m "MapGenerator: constrained type assignment, validation, and emit"
```

---

## Task 8: `BoardDesigner.GenerateRandomBoard` + batch self-check

Turn a successful `GenerationResult` into `PlanetDesigner` GameObjects in the `MapDesigner` scene, and add a batch self-check that runs many seeds and reports the pass rate.

**Files:**
- Modify: `Assets/Flatspace/BoardDesigner/BoardDesigner.cs`

**Interfaces:**
- Consumes: `MapGenerator.Generate`, `GeneratedPlanet`, `PlanetDesigner.SetConnectionNames`, `PlanetDesigner.RebuildConnectionsFromNames`, `RebuildAllConnectionCaches` (Task 3), `PlanetTypeDefaults` (Task 1).
- Produces: `BoardDesigner.GenerateRandomBoard()` and `BoardDesigner.MapGenSelfCheck()` `[ContextMenu]` actions; `BoardDesigner.planetDesignerPrefab` serialized field.

- [ ] **Step 1: Add the prefab reference field**

```csharp
            [SerializeField] private PlanetDesigner planetDesignerPrefab;
```

- [ ] **Step 2: Add `GenerateRandomBoard`**

```csharp
            [ContextMenu("Generate Random Board")]
            public void GenerateRandomBoard()
            {
                if (!mapGenSettings)
                {
                    Debug.LogError("[BoardDesigner] assign a MapGenSettings asset first");
                    return;
                }
                if (!planetDesignerPrefab)
                {
                    Debug.LogError("[BoardDesigner] assign the PlanetDesigner prefab first");
                    return;
                }

                var result = FlatSpace.Tools.MapGenerator.Generate(mapGenSettings);
                if (!result.Success)
                {
                    Debug.LogError($"[BoardDesigner] Generate Random Board failed: {result.Error}");
                    return; // scene left untouched
                }

                // Clear the existing board.
                ClearConnections();
                var existing = new List<PlanetDesigner>();
                foreach (Transform child in transform)
                    if (child.GetComponent<PlanetDesigner>())
                        existing.Add(child.GetComponent<PlanetDesigner>());
                foreach (var pd in existing)
                    DestroyImmediate(pd.gameObject);

                // Instantiate the generated planets.
                var spawned = new List<PlanetDesigner>();
                foreach (var gp in result.Planets)
                {
                    var pd = Instantiate(planetDesignerPrefab, transform);
                    pd.name = gp.Name;
                    pd.planetName = gp.Name;
                    pd.type = gp.Type;
                    pd.strategy = gp.Strategy;
                    pd.transform.localPosition = new Vector3(gp.Position.x, gp.Position.y, 0f);
                    pd.SetConnectionNames(gp.Connections);
                    pd.UpdateGraphic();
                    spawned.Add(pd);
                }

                RebuildAllConnectionCaches(spawned);
                DrawConnections(spawned);

                Debug.Log($"[BoardDesigner] Generated {spawned.Count} planets (seed {result.EffectiveSeed}). " +
                          "Review, then use the Save button.");
            }
```

- [ ] **Step 3: Add `MapGenSelfCheck`**

```csharp
            [ContextMenu("Map Gen: Self Check (50 seeds)")]
            public void MapGenSelfCheck()
            {
                if (!mapGenSettings)
                {
                    Debug.LogError("[BoardDesigner] assign a MapGenSettings asset first");
                    return;
                }

                var baseSettings = Instantiate(mapGenSettings);
                var pass = 0;
                var failures = new List<string>();
                for (var i = 1; i <= 50; i++)
                {
                    baseSettings.seed = i * 1000;
                    var r = FlatSpace.Tools.MapGenerator.Generate(baseSettings);
                    if (r.Success) pass++;
                    else failures.Add($"seed base {i * 1000}: {r.Error}");
                }
                DestroyImmediate(baseSettings);

                Debug.Log($"[BoardDesigner] Map Gen self check: {pass}/50 passed");
                foreach (var f in failures.Take(5))
                    Debug.LogWarning($"[BoardDesigner]   {f}");
            }
```

- [ ] **Step 4: Compile-check**

Run `mcp__rider__get_file_problems` on `BoardDesigner.cs` with `errorsOnly: true`.
Expected: no errors.

- [ ] **Step 5: Manual verification**

Ask the user to, in `MapDesigner.unity`:
1. Assign the `PlanetDesigner` prefab (`Assets/Flatspace/BoardDesigner/PlanetDesigner.prefab`) to `BoardDesigner.planetDesignerPrefab`.
2. Run `Map Gen: Self Check (50 seeds)` with the default settings — expect close to `50/50 passed` (a handful of retries internally is fine).
3. Run `Generate Random Board`. Confirm: the old planets are replaced; new planets appear scattered with no overlaps; connection lines are drawn and are visibly sparser than "every planet in range"; every planet has at least one line; two Prime planets are never directly linked; no two same-colored non-Normal planets share a line.
4. Run `Generate Random Board` a second time and confirm it fully replaces the previous result.

- [ ] **Step 6: Commit**

```bash
git add Assets/Flatspace/BoardDesigner/BoardDesigner.cs
git commit -m "BoardDesigner: Generate Random Board and batch self-check actions"
```

---

## Task 9: End-to-end editor validation + ledger

No code — a scripted manual pass proving a generated board plays, plus an execution ledger for the rulings and manual steps (mirroring `docs/superpowers/plans/2026-09-04-concrete-ship-representation-ledger.md`).

**Files:**
- Create: `docs/superpowers/plans/2026-09-07-automated-map-designer-ledger.md`

- [ ] **Step 1: Full project compile check**

Run `mcp__rider__build_solution_start` then poll `mcp__rider__build_solution_state`. Expected: no new errors in game scripts. (Pre-existing Unity package-cache warnings unrelated to this work — e.g. `com.unity.ugui`, `com.unity.render-pipelines.core` — are noted, not fixed, per the ship-representation ledger's precedent.)

- [ ] **Step 2: Generate → save → play**

Ask the user to:
1. In `MapDesigner.unity`, `Generate Random Board` with `MapGenSettings_Default` (2 players, 20 planets).
2. Click Save; save as `Assets/Flatspace/<name>.json` (editor path).
3. Load that board into `Flatspace.unity` via the normal load path.
4. Confirm: 20 planet markers, 2 owned capitals, connection lines matching the designer preview, and the simulation runs ~20 turns with no console errors (AI issues shipment/colonization orders, order lines follow the sparse multi-hop paths).

- [ ] **Step 3: Regression — hand-built board unchanged**

Ask the user to load a pre-existing hand-built board config (one that predates this branch, so its `BoardDesignerSave` JSON has no `connections` key or empty lists) and confirm it plays exactly as before — `PathingSystem` takes the `BuildDistanceConnections` path.

- [ ] **Step 4: Write the ledger**

Create `docs/superpowers/plans/2026-09-07-automated-map-designer-ledger.md` recording: any rulings made during execution, the Rider-PSI-artifact caveat if `AddComponent`/new-type resolution warnings appear, the manual Editor steps performed (asset creation, prefab wiring), and the results of Steps 2-3.

- [ ] **Step 5: Commit**

```bash
git add docs/superpowers/plans/2026-09-07-automated-map-designer-ledger.md
git commit -m "Add execution ledger for the automated map designer"
```

---

## Self-Review

**Spec coverage:**

| Spec section | Task(s) |
|---|---|
| A. `MapGenSettings` ScriptableObject | Task 4 (all fields, validation) |
| B. `MapGenerator` pipeline — seed resolution | Task 4 (`Generate`, base-seed + attempt offset) |
| B. counts / largest-remainder | Task 4 (`ResolveTypeMultiset`) |
| B. blue-noise placement | Task 5 (`PlacePlanets`) |
| B. Prime farthest-point selection | Task 5 (`PickPrimeIndices`) |
| B. candidate edges, no Prime-Prime | Task 6 (`BuildGraph`) |
| B. MST + component bridging | Task 6 (`BuildGraph`, Kruskal + bridge loop) |
| B. 25% extra edges | Task 6 (`ExtraEdgeFraction`, unused-candidate sampling) |
| B. constrained type assignment + backtracking | Task 7 (`AssignTypes`) |
| B. validation + seed retry + fail-loud | Task 7 (`Validate`) + Task 4 (`Generate` retry loop) |
| B. emit names/strategies/symmetric connections | Task 7 (`Emit`, using `PlanetTypeDefaults`) |
| B. shared `PlanetTypeDefaults` (no drift) | Task 1 |
| C. `PlanetDesigner.connectionNames` (serialized) | Task 3 |
| C. `RebuildConnectionsFromNames` cache | Task 3 |
| C. `GenerateStarConnections` writes names | Task 3 |
| C. `BoardDesignerEntry.connections` | Task 3 |
| C. `PlanetSpawnData._connections` | Task 2 |
| C. `GameBoard.InitGame` copies connections | Task 3 |
| C. `Planet.Connections` + `Init` | Task 2 |
| C. `PathingSystem` explicit-edge branch + fallback | Task 2 |
| C. old JSON / authored assets fall back | Task 2 + Task 3 (null handling), Task 9 Step 3 (regression) |
| D. `BoardDesigner` refs + `Generate Random Board` | Task 8 |
| D. clear-then-populate scene | Task 8 |
| D. existing actions untouched | Task 1 (GenerateNames refactor is behavior-neutral), Task 3 (GenerateStarConnections repointed, same output) |
| E. diagnostics / logging / no scene mutation on failure | Task 4-8 (`Debug.Log`/`LogError`, `GenerateRandomBoard` early return) |
| F. verification approach | Every task's manual step; Task 8 self-check; Task 9 end-to-end |

No gaps.

**Placeholder scan:** The phrases "Filled in over Tasks 5-7" / "for now" in Task 4 Step 2 describe an intentional incremental stub whose replacement code is given verbatim in Tasks 5, 6, and 7 — not an unfilled placeholder. All code steps contain complete code. No "TBD"/"handle edge cases"/"add validation" hand-waves.

**Type consistency:**
- `MapGenerator.Generate(MapGenSettings) -> GenerationResult` — consistent across Tasks 4-8.
- `GeneratedPlanet` fields (`Name`, `Type`, `Strategy`, `Position`, `Connections`) — defined Task 4, consumed identically Task 7 (`Emit`) and Task 8 (`GenerateRandomBoard`).
- `PlanetDesigner.SetConnectionNames` / `RebuildConnectionsFromNames` / `connectionNames` — defined Task 3, consumed Task 8.
- `BoardDesigner.RebuildAllConnectionCaches(List<PlanetDesigner>)` — defined Task 3, reused Task 8.
- `PlanetTypeDefaults.StrategyFor` / `ShortName` / `AllTypes` — defined Task 1, used Tasks 4, 7.
- `TryGenerate` internal shape (`positions`, `primeIndices`, `edges`, `typeMultiset`) — introduced progressively; each task quotes the exact lines it replaces.
- `IsConnected(int, HashSet<(int,int)>)` — defined Task 6, reused Task 7 (`Validate`).
- `IsPrimePair(HashSet<int>, int, int)` — defined Task 6, reused Task 7 (`Validate`).
- `PathingSystem` helper names `BuildDistanceConnections` / `BuildExplicitConnections` — defined and called in Task 2 only.

Consistent.
