# Distribution Centers (DCs) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Give each player's AI a sticky, per-resource "Distribution Center" (DC) planet that stockpiles Food/Grotsits ahead of real need and re-ships it onward, extending effective delivery range to roughly `2 x maxPathNodesForResourceDistribution` — plus the colonization/knowledge range widening this depends on, so an empire's frontier can actually outrun direct shipping range in the first place.

**Architecture:** All new logic lives in `PlayerAI` (data model, selection heuristic, synthetic demand) and `PlayerKnowledge` (wider knowledge growth), reusing the existing `ScoreMatrix`/`ProcessResourceShipments` pipeline for shipping and the existing save/load and `AITuningLogger` patterns for persistence and diagnostics. Two small UI additions surface DC status to a human watching a match.

**Tech Stack:** Unity 6000.4.1f1, C#, UI Toolkit (`PlanetDetailUI.uxml`) and uGUI (`PlanetUIObject`). No automated test framework — verification is the Unity Editor self-check pattern (see Global Constraints).

**Spec:** `docs/superpowers/specs/2026-09-27-distribution-centers-design.md`

## Global Constraints

- There is no CLI test runner. "RED" for a self-check assertion is a **compile error** (the assertion calls a method/field that doesn't exist yet) — verify it with `mcp__rider__get_file_problems`, not by running anything. "GREEN" is zero compile errors from that same tool, followed by asking the user to focus the Unity Editor (forces recompile), run the named `FlatSpace` menu item, and confirm the exact text `ALL PASSED (N assertions ran)`.
- A self-check must never depend on `Gameboard.Instance` — build a minimal `GameAIMap`/`Planet`/`PlayerAI` set directly (see existing self-check files for the pattern). Any new method a self-check needs to call directly must be `public`, not `internal` (`Assets/Editor` is a separate assembly with no `InternalsVisibleTo`).
- A brand-new script's `.meta` file must be committed alongside it. After creating `Assets/Editor/DistributionCenterSelfCheck.cs`, run `git status` and confirm the matching `.meta` is staged too before committing.
- Namespace casing: match whatever the file already uses (`FlatSpace.AI` for `PlayerAI.cs`/`PlayerKnowledge.cs`/`GameAIMap.cs`/`GameAI.cs`, no namespace wrapper for `AITuningLogger`/`GameAIConstants`/`PlanetUIObject`/`PlanetDetailUIController`, `Flatspace.Objects.Production` for `CatalogItem`).
- `OrderType` (in `GameAI.cs`) is not touched by this plan — no new order types are needed.
- Commit after each task, following this repo's existing commit style (`git commit` with a `Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>` trailer per this session's established convention, if that convention still applies when this plan is executed — check recent `git log` first).

## Review Focus

- **Board-size gating must count the whole board, every player's colonized planets — not just the acting player's own territory.** The spec is explicit about this (it mirrors the topology probe, which was board-size based); an implementer reaching for the existing per-player `ColonizedPlanetCount()` instead of a new whole-board count would silently make the gate never trigger on typical single-player-dominant test boards. Task 5's gating test uses a second player's colonized planet specifically to catch this.
- **A DC's synthetic demand must never outrank a real shortage for the same round's surplus.** If the priority sentinel is derived from the gap size instead of being an absolute constant, a very large synthetic gap could out-prioritize a small real shortage, starving a planet that's actually in trouble to feed a forward stockpile. Task 6's test constructs exactly this: a huge DC gap alongside a tiny real shortage, both wanting the same single unit of surplus.
- **A DC that also reports a real shortage the same turn must not create two decision rows for the same planet+resource.** `BuildResourceMatrix` only logs `Debug.LogError("Duplicate Key...")` and silently drops the second entry rather than crashing — a naive implementation that adds the synthetic entry unconditionally would trip this every time a DC's own population briefly outgrows its stock. Task 6's test covers this directly.
- **Restoring a save from before this feature must not throw or select every planet.** `PlayerSave`'s new fields will be `null` on an old save exactly like `knownPlanets` is; the restore code must null-coalesce to an empty list (Task 8's test loads a save missing the fields and confirms an empty, not exploded, DC list).
- **Sticky selection must not flap.** Calling the per-turn update twice in a row, with an equally-or-better-scoring alternate candidate available on the second call, must not replace the first call's choice. Task 5's test calls the entry point twice and asserts the same planet both times.

---

## Task 1: New tunables on GameAIConstants

**Files:**
- Modify: `Assets/Flatspace/GameAI/GameAIConstants.cs`
- Modify: `Assets/GameAIConstantsProductionTypes.asset`

**Interfaces:**
- Produces: `GameAIConstants.maxPathNodesForColonization` (int), `GameAIConstants.maxPathNodesForKnowledge` (int), `GameAIConstants.minPlanetsForDistributionCenters` (int), `GameAIConstants.minPlanetsForSecondDistributionCenter` (int), `GameAIConstants.distributionCenterFoodTargetStock` (float), `GameAIConstants.distributionCenterGrotsitsTargetStock` (float) — all consumed by later tasks.

- [ ] **Step 1: Add the six fields to `GameAIConstants.cs`**

In `Assets/Flatspace/GameAI/GameAIConstants.cs`, find the existing `[Header("Colonization")]` block:

```csharp
    [Header("Colonization")]
    // Food a colony ship carries to a planet that produces no food (e.g. Desolate), so the colony survives long
    // enough for the food shipping system to notice its shortage. The origin pays it and must hold at least this
    // much, otherwise that target is not viable from that origin. It is a bridge, not a guarantee: colonies can
    // still fail.
    public float colonyFoodRider = 10f;
```

Replace it with (adds `maxPathNodesForColonization` to the same header, plus two new headers after it):

```csharp
    [Header("Colonization")]
    // Food a colony ship carries to a planet that produces no food (e.g. Desolate), so the colony survives long
    // enough for the food shipping system to notice its shortage. The origin pays it and must hold at least this
    // much, otherwise that target is not viable from that origin. It is a bridge, not a guarantee: colonies can
    // still fail.
    public float colonyFoodRider = 10f;
    // Longest trip (path node count) a colony ship may take, independent of maxPathNodesForResourceDistribution
    // so an empire's frontier can outrun direct shipping range (a prerequisite for Distribution Centers to have
    // any territory to serve). Requires maxPathNodesForKnowledge to be at least this large, otherwise knowledge
    // becomes the tighter gate again and this constant has no effect.
    public int maxPathNodesForColonization = 6;

    [Header("Knowledge")]
    // PlayerKnowledge.Update grants knowledge out to this many path nodes from each vision source (2 = source +
    // direct neighbours only, the behavior every existing call site keeps by default). Should be >= the largest
    // of maxPathNodesForColonization/maxPathNodesForResourceDistribution, otherwise it becomes the real gate
    // underneath whichever of those is wider.
    public int maxPathNodesForKnowledge = 6;

    [Header("Distribution Centers")]
    // At or above this many colonized planets on the WHOLE BOARD (every player, not just this one), a player's
    // AI may designate one Distribution Center per resource (Food and/or Grotsits).
    public int minPlanetsForDistributionCenters = 30;
    // At or above this many (whole board), up to two DCs per resource. Left far above any tested board size for
    // now so only the one-DC-per-resource tier is exercised; lower it once that tier is validated.
    public int minPlanetsForSecondDistributionCenter = 10000;
    // Flat target stock a Food DC tries to accumulate before its synthetic demand drops to zero. Starting point,
    // not deeply tuned yet — retune via a /tuning-log pass once the mechanism itself is validated.
    public float distributionCenterFoodTargetStock = 50f;
    public float distributionCenterGrotsitsTargetStock = 50f;
```

- [ ] **Step 2: Add matching defaults to the active constants asset**

In `Assets/GameAIConstantsProductionTypes.asset`, find the last two lines:

```yaml
  improvementUpkeepScale: 0.5
  colonyFoodRider: 10
```

Append after `colonyFoodRider: 10`:

```yaml
  maxPathNodesForColonization: 6
  maxPathNodesForKnowledge: 6
  minPlanetsForDistributionCenters: 30
  minPlanetsForSecondDistributionCenter: 10000
  distributionCenterFoodTargetStock: 50
  distributionCenterGrotsitsTargetStock: 50
```

- [ ] **Step 3: Verify it compiles**

Run `mcp__rider__get_file_problems` on `Assets/Flatspace/GameAI/GameAIConstants.cs`, `errorsOnly: true`. Expected: no errors (this task adds fields only, nothing references them yet).

- [ ] **Step 4: Commit**

```bash
git add Assets/Flatspace/GameAI/GameAIConstants.cs Assets/GameAIConstantsProductionTypes.asset
git commit -m "feat(ai): add Distribution Center and knowledge/colonization-range tunables"
```

---

## Task 2: Decouple colonization range from resource-shipping range

**Files:**
- Modify: `Assets/Flatspace/GameAI/PlayerAI.cs:104-111` (`PlanetHasColonizationTarget`), `PlayerAI.cs:160-164` (`ProcessColonizers`'s target filter)
- Modify: `Assets/Editor/PlayerKnowledgeSelfCheck.cs`

**Interfaces:**
- Consumes: `GameAIConstants.maxPathNodesForColonization` (Task 1).
- Produces: nothing new; `ProcessColonizers` (already public) and `PlanetHasColonizationTarget` (private, driven indirectly through `ProcessColonizers`) now read the new constant instead of `maxPathNodesForResourceDistribution`.

- [ ] **Step 1: Write the failing self-check**

Add to `Assets/Editor/PlayerKnowledgeSelfCheck.cs`, a new method (and register it in `Run()`):

```csharp
    // Colonization must use its OWN range constant, not maxPathNodesForResourceDistribution — a
    // prerequisite for Distribution Centers to ever have territory beyond direct shipping range to serve.
    // Home -- Mid -- Target (2 hops from Home). maxPathNodesForResourceDistribution is set too small to
    // reach Target at all; maxPathNodesForColonization is generous. Knowledge is set directly (bypassing
    // the knowledge gate, which is covered by RunColonizationKnowledgeGateCheck) so only the distance
    // constant is under test.
    public static bool RunColonizationUsesOwnRangeCheck()
    {
        var ok = true;
        var mapGo = new GameObject("PKSelfCheckMap_ColonizationRange");
        var playerGo = new GameObject("PKSelfCheckPlayer_ColonizationRange");
        try
        {
            var map = mapGo.AddComponent<GameAIMap>();
            var constants = ScriptableObject.CreateInstance<GameAIConstants>();
            constants.defaultTravelSpeed = 1f;
            constants.expandPopulationTrigger = 0.1f;
            constants.maxPathNodesForResourceDistribution = 1; // too small to reach even a direct neighbour
            constants.maxPathNodesForColonization = 5;         // generous

            var spawns = new List<PlanetSpawnData>
            {
                MakeSpawn("Home", initialPopulation: 5, connections: new[] { "Mid" }),
                MakeSpawn("Mid", initialPopulation: 0, connections: new[] { "Target" }),
                MakeSpawn("Target", initialPopulation: 0),
            };
            map.GameAIMapInit(spawns, constants);
            map.Knowledge.SetKnownPlanets(0, new List<string> { "Home", "Mid", "Target" });

            var player = playerGo.AddComponent<Player>();
            var playerAI = playerGo.AddComponent<PlayerAI>();
            playerAI.Player = player;
            playerAI.AIMap = map;
            player.playerID = 0;

            var results = new List<Planet.PlanetUpdateResult>
            {
                new Planet.PlanetUpdateResult("Home",
                    Planet.PlanetUpdateResult.PlanetUpdateResultType.PlanetUpdateResultTypeColonizerReady,
                    1, playerID: 0),
            };
            var orders = new List<GameAI.GameAIOrder>();
            playerAI.ProcessColonizers(results, orders);

            ok &= Check(orders.Exists(o =>
                    o.Type == GameAI.GameAIOrder.OrderType.OrderTypePopulationTransport &&
                    o.Origin == "Home" && o.Target == "Target"),
                "a target 2 hops away is colonized when maxPathNodesForColonization allows it, even though " +
                "maxPathNodesForResourceDistribution alone would have excluded it");
        }
        finally
        {
            Object.DestroyImmediate(playerGo);
            Object.DestroyImmediate(mapGo);
        }
        return ok;
    }
```

Register it in `Run()`, right after `ok &= RunColonizationKnowledgeGateCheck();`:

```csharp
        ok &= RunColonizationUsesOwnRangeCheck();
```

- [ ] **Step 2: Verify it fails to compile**

Run `mcp__rider__get_file_problems` on `Assets/Editor/PlayerKnowledgeSelfCheck.cs`, `errorsOnly: true`.
Expected: an error that `GameAIConstants` has no field `maxPathNodesForColonization` — wait, Task 1 already added that field, so this should actually **compile successfully already** (nothing here calls a not-yet-existing method). This step instead confirms the *assertion* would fail at runtime: since `PlayerAI.cs` still reads `maxPathNodesForResourceDistribution` (=1) at both colonization gates, `Target` (2 hops away) is unreachable and `ProcessColonizers` emits no order — the assertion in Step 1 would report `FAIL` if run in the Editor right now. Confirm this by inspection of the current `PlayerAI.cs` lines 104-111 and 160-164 (both still say `maxPathNodesForResourceDistribution`) rather than by running Unity.

- [ ] **Step 3: Make the two replacements in `PlayerAI.cs`**

At line 108 (inside `PlanetHasColonizationTarget`):

```csharp
                return origin.DistanceMapToPathingList.Any(t =>
                    t.Value.NumNodes <= AIMap.GameAIConstants.maxPathNodesForColonization
                    && IsValidColonizationTarget(AIMap.GetPlanet(t.Key))
                    && CanSupportColony(origin, AIMap.GetPlanet(t.Key)));
```

At lines 161-164 (inside `ProcessColonizers`):

```csharp
                    var entries = targets
                        .Where(t => pathMap.ContainsKey(t.PlanetName)
                                 && pathMap[t.PlanetName].NumNodes
                                        <= AIMap.GameAIConstants.maxPathNodesForColonization
                                 && CanSupportColony(colonizerPlanet, t))
```

- [ ] **Step 4: Verify it compiles and the assertion now passes**

Run `mcp__rider__get_file_problems` on `Assets/Flatspace/GameAI/PlayerAI.cs` and `Assets/Editor/PlayerKnowledgeSelfCheck.cs`, `errorsOnly: true`. Expected: no errors on either.

- [ ] **Step 5: Ask the user to run it in the Editor**

Ask the user to focus the Unity Editor (recompile), run **FlatSpace → AI → Run Player Knowledge Self-Check**, and confirm `ALL PASSED (N assertions ran)`.

- [ ] **Step 6: Commit**

```bash
git add Assets/Flatspace/GameAI/PlayerAI.cs Assets/Editor/PlayerKnowledgeSelfCheck.cs
git commit -m "feat(ai): colonization range is independent of maxPathNodesForResourceDistribution"
```

---

## Task 3: Widen PlayerKnowledge's growth rule

**Files:**
- Modify: `Assets/Flatspace/GameAI/PlayerKnowledge.cs`
- Modify: `Assets/Flatspace/GameAI/GameAI.cs:118`
- Modify: `Assets/Editor/PlayerKnowledgeSelfCheck.cs`

**Interfaces:**
- Consumes: `GameAIConstants.maxPathNodesForKnowledge` (Task 1), `GameAIMap.GetNeighbours(string)` (existing), `GameAIMap.GetVisionSourcePlanets(int)` (existing).
- Produces: `PlayerKnowledge.Update(GameAIMap map, int numPlayers, int maxPathNodesForKnowledge = 2)` — the default keeps every one of the 15 existing self-check call sites (which pass only `map, numPlayers:`) working unchanged, exercising today's direct-neighbour-only behavior.

- [ ] **Step 1: Write the failing self-check**

Add to `Assets/Editor/PlayerKnowledgeSelfCheck.cs`, a new method (register it in `Run()`):

```csharp
    // Home -- Mid -- Target (Target is 2 hops / NumNodes 3 from Home). With the default parameter
    // (unwidened, matching today's behavior), Target must stay unknown. Passing a wider
    // maxPathNodesForKnowledge must reveal it. Two fresh PlayerKnowledge instances so the sticky,
    // never-forgets nature of one doesn't leak into the other's assertion.
    public static bool RunKnowledgeWideningCheck()
    {
        var ok = true;
        var mapGo = new GameObject("PKSelfCheckMap_Widening");
        try
        {
            var map = mapGo.AddComponent<GameAIMap>();
            var constants = ScriptableObject.CreateInstance<GameAIConstants>();
            var spawns = new List<PlanetSpawnData>
            {
                MakeSpawn("Home", initialPopulation: 1, connections: new[] { "Mid" }),
                MakeSpawn("Mid", initialPopulation: 0, connections: new[] { "Target" }),
                MakeSpawn("Target", initialPopulation: 0),
            };
            map.GameAIMapInit(spawns, constants);

            var narrow = new PlayerKnowledge();
            narrow.Update(map, numPlayers: 1); // default maxPathNodesForKnowledge: 2
            ok &= Check(narrow.IsKnown(0, "Mid"), "a direct neighbour is still known with the default (2)");
            ok &= Check(!narrow.IsKnown(0, "Target"),
                "a planet 2 hops out stays unknown with the default (2), matching today's behavior");

            var wide = new PlayerKnowledge();
            wide.Update(map, numPlayers: 1, maxPathNodesForKnowledge: 3);
            ok &= Check(wide.IsKnown(0, "Target"),
                "the same planet becomes known once maxPathNodesForKnowledge widens to 3");
        }
        finally
        {
            Object.DestroyImmediate(mapGo);
        }
        return ok;
    }
```

Register it in `Run()`, right after `ok &= RunColonizationUsesOwnRangeCheck();`:

```csharp
        ok &= RunKnowledgeWideningCheck();
```

- [ ] **Step 2: Verify it fails to compile**

Run `mcp__rider__get_file_problems` on `Assets/Editor/PlayerKnowledgeSelfCheck.cs`, `errorsOnly: true`.
Expected: an error that `PlayerKnowledge.Update` has no overload taking a `maxPathNodesForKnowledge:` named argument (its current signature is `Update(GameAIMap map, int numPlayers)`).

- [ ] **Step 3: Widen `PlayerKnowledge.Update`**

In `Assets/Flatspace/GameAI/PlayerKnowledge.cs`, replace the entire `Update` method:

```csharp
            public void Update(GameAIMap map, int numPlayers)
            {
                for (var p = 0; p < numPlayers; p++)
                {
                    if (!_known.TryGetValue(p, out var set))
                        _known[p] = set = new HashSet<string>();

                    foreach (var source in map.GetVisionSourcePlanets(p))
                    {
                        set.Add(source.Planet.PlanetName);
                        foreach (var neighbourName in map.GetNeighbours(source.Planet.PlanetName))
                            set.Add(neighbourName);
                    }
                }
            }
```

with:

```csharp
            /// <summary>
            /// Grants knowledge out to maxPathNodesForKnowledge path nodes from each vision source: a BFS
            /// over GetNeighbours' graph adjacency, converting NumNodes to a hop count (NumNodes - 1). The
            /// default of 2 reproduces this method's original behavior (source + direct neighbours only),
            /// so every existing call site that only passes (map, numPlayers) is unaffected.
            /// </summary>
            public void Update(GameAIMap map, int numPlayers, int maxPathNodesForKnowledge = 2)
            {
                var hops = Math.Max(0, maxPathNodesForKnowledge - 1);
                for (var p = 0; p < numPlayers; p++)
                {
                    if (!_known.TryGetValue(p, out var set))
                        _known[p] = set = new HashSet<string>();

                    foreach (var source in map.GetVisionSourcePlanets(p))
                    {
                        set.Add(source.Planet.PlanetName);
                        var frontier = new List<string> { source.Planet.PlanetName };
                        for (var hop = 0; hop < hops && frontier.Count > 0; hop++)
                        {
                            var next = new List<string>();
                            foreach (var name in frontier)
                                foreach (var neighbourName in map.GetNeighbours(name))
                                    if (set.Add(neighbourName))
                                        next.Add(neighbourName);
                            frontier = next;
                        }
                    }
                }
            }
```

- [ ] **Step 4: Wire the real call site to the tunable**

In `Assets/Flatspace/GameAI/GameAI.cs:118`, replace:

```csharp
                GameAIMap.Knowledge.Update(GameAIMap, Gameboard.Instance.players.Count);
```

with:

```csharp
                GameAIMap.Knowledge.Update(GameAIMap, Gameboard.Instance.players.Count,
                    GameAIMap.GameAIConstants.maxPathNodesForKnowledge);
```

- [ ] **Step 5: Verify it compiles and the assertion now passes**

Run `mcp__rider__get_file_problems` on `Assets/Flatspace/GameAI/PlayerKnowledge.cs`, `Assets/Flatspace/GameAI/GameAI.cs`, and `Assets/Editor/PlayerKnowledgeSelfCheck.cs`, `errorsOnly: true`. Expected: no errors on any.

- [ ] **Step 6: Ask the user to run it in the Editor**

Ask the user to focus the Editor, run **FlatSpace → AI → Run Player Knowledge Self-Check**, and confirm `ALL PASSED (N assertions ran)`.

- [ ] **Step 7: Commit**

```bash
git add Assets/Flatspace/GameAI/PlayerKnowledge.cs Assets/Flatspace/GameAI/GameAI.cs Assets/Editor/PlayerKnowledgeSelfCheck.cs
git commit -m "feat(ai): widen PlayerKnowledge growth to a tunable hop count"
```

---

## Task 4: New AITuningLogger events

**Files:**
- Modify: `Assets/Flatspace/Diagnostics/AITuningLogger.cs`

**Interfaces:**
- Produces: `AITuningLogger.LogDCSelected(int turnNumber, int playerId, string planetName, string resource)`, `AITuningLogger.LogDCLost(int turnNumber, int playerId, string planetName, string resource)`, `AITuningLogger.LogDCCoverageGap(int turnNumber, int playerId, string planetName, string resource)` — all consumed by Task 5 (`LogDCSelected`/`LogDCLost`) and Task 7 (`LogDCCoverageGap`).

This task has no dedicated self-check (there is no existing self-check file for `AITuningLogger`, and every `Log*` method already no-ops safely with `_currentLogPath == null` — the same guard these three will use). Verification is a compile check plus the two call sites added in Tasks 5 and 7, which do exercise them.

- [ ] **Step 1: Add the three methods**

In `Assets/Flatspace/Diagnostics/AITuningLogger.cs`, right after the existing `LogWarshipBoost` method, add:

```csharp
    /// <summary>Logged the turn a Distribution Center is designated for a resource.</summary>
    public static void LogDCSelected(int turnNumber, int playerId, string planetName, string resource)
    {
        if (_currentLogPath == null) return;
        AppendLines(new List<string> { FormatLine(turnNumber, playerId, "DCSelected", planetName, resource) });
    }

    /// <summary>Logged the turn a Distribution Center designation clears (captured or population died out).</summary>
    public static void LogDCLost(int turnNumber, int playerId, string planetName, string resource)
    {
        if (_currentLogPath == null) return;
        AppendLines(new List<string> { FormatLine(turnNumber, playerId, "DCLost", planetName, resource) });
    }

    /// <summary>
    /// Logged on a newly-colonized planet's arrival, once per resource, when that planet is unreachable
    /// from both every currently-surplus-reporting planet and every currently-designated DC for that
    /// resource — evidence for whether sticky single-DC selection is giving good enough coverage.
    /// </summary>
    public static void LogDCCoverageGap(int turnNumber, int playerId, string planetName, string resource)
    {
        if (_currentLogPath == null) return;
        AppendLines(new List<string> { FormatLine(turnNumber, playerId, "DCCoverageGap", planetName, resource) });
    }
```

- [ ] **Step 2: Verify it compiles**

Run `mcp__rider__get_file_problems` on `Assets/Flatspace/Diagnostics/AITuningLogger.cs`, `errorsOnly: true`. Expected: no errors.

- [ ] **Step 3: Commit**

```bash
git add Assets/Flatspace/Diagnostics/AITuningLogger.cs
git commit -m "feat(ai): add DCSelected/DCLost/DCCoverageGap tuning-log events"
```

---

## Task 5: DC data model, board-size gating, and the coverage-maximizing selection heuristic

**Files:**
- Modify: `Assets/Flatspace/GameAI/GameAIMap.cs` (add `TotalColonizedPlanetCount()`)
- Modify: `Assets/Flatspace/GameAI/PlayerAI.cs` (new fields, `UpdateDistributionCenters`, wired into `ProcessResultsStrategyExpand`)
- Create: `Assets/Editor/DistributionCenterSelfCheck.cs` (+ `.meta`, auto-generated by Unity on next Editor focus — commit it once it exists)

**Interfaces:**
- Consumes: `GameAIConstants.minPlanetsForDistributionCenters`/`minPlanetsForSecondDistributionCenter` (Task 1), `AITuningLogger.LogDCSelected`/`LogDCLost` (Task 4), `Planet.DistanceMapToPathingList`, `Planet.Owner`, `Planet.Population`, `Planet.PlanetName` (all existing).
- Produces: `PlayerAI.FoodDistributionCenters` / `GrotsitsDistributionCenters` (`List<string>`, read by Tasks 6, 7, 8, 9, 10), `PlayerAI.UpdateDistributionCenters(List<Planet.PlanetUpdateResult> results, int turnNumber)` (public, called from `ProcessResultsStrategyExpand` and directly by this task's self-check), `PlayerAI.LastFoodSurplusPlanets` / `LastGrotsitsSurplusPlanets` (`List<string>`, read by Task 7), `PlayerAI.SetDistributionCenters(string resource, List<string> names)` (used by Task 8), `GameAIMap.TotalColonizedPlanetCount()` (int).

- [ ] **Step 1: Add `TotalColonizedPlanetCount` to `GameAIMap`**

In `Assets/Flatspace/GameAI/GameAIMap.cs`, right after the `PlanetList` property, add:

```csharp
            /// <summary>
            /// Colonized planets across the WHOLE board, every owner — used to gate board-size-dependent AI
            /// features (Distribution Centers) that key off overall board scale, not one player's territory.
            /// </summary>
            public int TotalColonizedPlanetCount() => PlanetList.Count(p => p.Population.Count > 0);
```

- [ ] **Step 2: Write the failing self-check**

Create `Assets/Editor/DistributionCenterSelfCheck.cs`:

```csharp
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using FlatSpace.AI;

public static class DistributionCenterSelfCheck
{
    private static float _nextPlanetX;

    [MenuItem("FlatSpace/AI/Run Distribution Center Self-Check")]
    public static void Run()
    {
        var ok = RunBoardSizeGatingCheck();
        ok &= RunCoverageMaximizingSelectionCheck();
        ok &= RunTieBreakByDistanceCheck();
        ok &= RunNoReachableCandidateCheck();
        ok &= RunStickySelectionCheck();
        ok &= RunPruningCheck();
        Debug.Log(ok
            ? "[DistributionCenterSelfCheck] ALL PASSED"
            : "[DistributionCenterSelfCheck] FAILURES (see errors above)");
    }

    private static bool Check(bool condition, string label)
    {
        if (!condition) Debug.LogError($"[DistributionCenterSelfCheck] FAIL: {label}");
        return condition;
    }

    private static PlanetSpawnData MakeSpawn(string name, int initialPopulation, IEnumerable<string> connections = null)
    {
        var resourceData = ScriptableObject.CreateInstance<PlanetResourceData>();
        resourceData._initialPopulation = initialPopulation;
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

    // NOTE: Planet.Owner is only ever assigned by the private SetPlanetOwnership() (called during a real
    // Planet.UpdatePlanet() simulation tick), which these self-checks never run — it is NOT derived from
    // Population automatically. Every planet built with population here must have its Owner set
    // explicitly, or PlayerAI's Owner-filtered "colonized planets" queries see nothing. This helper does
    // that for every planet MakeSpawn gave a population > 0; a test that needs a planet owned by a
    // DIFFERENT player overrides .Owner afterward (see RunBoardSizeGatingCheck).
    private static (GameAIMap map, PlayerAI playerAI, GameObject mapGo, GameObject playerGo) BuildPlayer(
        List<PlanetSpawnData> spawns, GameAIConstants constants, int playerId, string mapGoName, string playerGoName)
    {
        var mapGo = new GameObject(mapGoName);
        var playerGo = new GameObject(playerGoName);
        var map = mapGo.AddComponent<GameAIMap>();
        map.GameAIMapInit(spawns, constants);
        foreach (var planet in map.PlanetList)
            if (planet.Population.Count > 0)
                planet.Owner = playerId;
        var player = playerGo.AddComponent<Player>();
        var playerAI = playerGo.AddComponent<PlayerAI>();
        playerAI.Player = player;
        playerAI.AIMap = map;
        player.playerID = playerId;
        return (map, playerAI, mapGo, playerGo);
    }

    private static Planet.PlanetUpdateResult FoodSurplus(string name, int playerId, float amount = 10f)
        => new Planet.PlanetUpdateResult(name,
            Planet.PlanetUpdateResult.PlanetUpdateResultType.PlanetUpdateResultTypeFoodSurplus, amount, playerId);

    // A -- Home(producer). B -- Far(candidate, reaches ConsumerA/ConsumerB) that no producer reaches
    // directly. maxPathNodesForResourceDistribution = 2 (direct neighbours only). Far must score higher
    // than Near (which reaches nothing new) and be selected.
    public static bool RunCoverageMaximizingSelectionCheck()
    {
        var ok = true;
        (GameAIMap map, PlayerAI playerAI, GameObject mapGo, GameObject playerGo) built = default;
        try
        {
            var constants = ScriptableObject.CreateInstance<GameAIConstants>();
            constants.defaultTravelSpeed = 1f;
            constants.maxPathNodesForResourceDistribution = 2;
            constants.minPlanetsForDistributionCenters = 0;
            constants.minPlanetsForSecondDistributionCenter = 10000;

            var spawns = new List<PlanetSpawnData>
            {
                MakeSpawn("Home", 1, new[] { "NearA", "FarB" }),
                MakeSpawn("NearA", 1),
                MakeSpawn("FarB", 1, new[] { "Home", "Consumer1", "Consumer2" }),
                MakeSpawn("Consumer1", 1),
                MakeSpawn("Consumer2", 1),
            };
            built = BuildPlayer(spawns, constants, 0, "DCSelfCheckMap1", "DCSelfCheckPlayer1");

            var results = new List<Planet.PlanetUpdateResult> { FoodSurplus("Home", 0) };
            built.playerAI.UpdateDistributionCenters(results, turnNumber: 1);

            ok &= Check(built.playerAI.FoodDistributionCenters.Count == 1
                        && built.playerAI.FoodDistributionCenters[0] == "FarB",
                "FarB (reaches 2 new consumers) is selected over NearA (reaches 0 new consumers)");
        }
        finally
        {
            Object.DestroyImmediate(built.playerGo);
            Object.DestroyImmediate(built.mapGo);
        }
        return ok;
    }

    // Home -- A -- Mid -- ConsumerA (A scores 1, distance 2 from Home).
    // Home -- Relay -- B -- ConsumerB (B scores 1, distance 3 from Home).
    // maxPathNodesForResourceDistribution = 3. Equal score; B must win on "furthest from its producer".
    public static bool RunTieBreakByDistanceCheck()
    {
        var ok = true;
        (GameAIMap map, PlayerAI playerAI, GameObject mapGo, GameObject playerGo) built = default;
        try
        {
            var constants = ScriptableObject.CreateInstance<GameAIConstants>();
            constants.defaultTravelSpeed = 1f;
            constants.maxPathNodesForResourceDistribution = 3;
            constants.minPlanetsForDistributionCenters = 0;
            constants.minPlanetsForSecondDistributionCenter = 10000;

            var spawns = new List<PlanetSpawnData>
            {
                MakeSpawn("Home", 1, new[] { "A", "Relay" }),
                MakeSpawn("A", 1, new[] { "Home", "Mid" }),
                MakeSpawn("Mid", 0, new[] { "A", "ConsumerA" }),
                MakeSpawn("ConsumerA", 1, new[] { "Mid" }),
                MakeSpawn("Relay", 0, new[] { "Home", "B" }),
                MakeSpawn("B", 1, new[] { "Relay", "ConsumerB" }),
                MakeSpawn("ConsumerB", 1, new[] { "B" }),
            };
            built = BuildPlayer(spawns, constants, 0, "DCSelfCheckMap2", "DCSelfCheckPlayer2");

            var results = new List<Planet.PlanetUpdateResult> { FoodSurplus("Home", 0) };
            built.playerAI.UpdateDistributionCenters(results, turnNumber: 1);

            ok &= Check(built.playerAI.FoodDistributionCenters.Count == 1
                        && built.playerAI.FoodDistributionCenters[0] == "B",
                "B (distance 3 from Home) wins the tie-break over A (distance 2), both scoring 1");
        }
        finally
        {
            Object.DestroyImmediate(built.playerGo);
            Object.DestroyImmediate(built.mapGo);
        }
        return ok;
    }

    // A lone, disconnected candidate: no producer reaches it at all. Selection must find nothing.
    public static bool RunNoReachableCandidateCheck()
    {
        var ok = true;
        (GameAIMap map, PlayerAI playerAI, GameObject mapGo, GameObject playerGo) built = default;
        try
        {
            var constants = ScriptableObject.CreateInstance<GameAIConstants>();
            constants.defaultTravelSpeed = 1f;
            constants.maxPathNodesForResourceDistribution = 2;
            constants.minPlanetsForDistributionCenters = 0;
            constants.minPlanetsForSecondDistributionCenter = 10000;

            var spawns = new List<PlanetSpawnData>
            {
                MakeSpawn("Home", 1),
                MakeSpawn("Isolated", 1), // no connections at all
            };
            built = BuildPlayer(spawns, constants, 0, "DCSelfCheckMap3", "DCSelfCheckPlayer3");

            var results = new List<Planet.PlanetUpdateResult> { FoodSurplus("Home", 0) };
            built.playerAI.UpdateDistributionCenters(results, turnNumber: 1);

            ok &= Check(built.playerAI.FoodDistributionCenters.Count == 0,
                "no DC is selected when no candidate is reachable from any producer");
        }
        finally
        {
            Object.DestroyImmediate(built.playerGo);
            Object.DestroyImmediate(built.mapGo);
        }
        return ok;
    }

    // Same topology as RunCoverageMaximizingSelectionCheck, but this player's own colonized count (2) is
    // below the whole-board threshold (5) until a SECOND PLAYER's planets push the board-wide total over
    // it — proving the gate counts the whole board, not this player's own territory.
    public static bool RunBoardSizeGatingCheck()
    {
        var ok = true;
        (GameAIMap map, PlayerAI playerAI, GameObject mapGo, GameObject playerGo) built = default;
        (GameAIMap map, PlayerAI playerAI, GameObject mapGo, GameObject playerGo) blocked = default;
        try
        {
            var constants = ScriptableObject.CreateInstance<GameAIConstants>();
            constants.defaultTravelSpeed = 1f;
            constants.maxPathNodesForResourceDistribution = 2;
            constants.minPlanetsForDistributionCenters = 5;
            constants.minPlanetsForSecondDistributionCenter = 10000;

            var spawns = new List<PlanetSpawnData>
            {
                MakeSpawn("Home", 1, new[] { "NearA", "FarB" }),
                MakeSpawn("NearA", 1),
                MakeSpawn("FarB", 1, new[] { "Home", "Consumer1", "Consumer2" }),
                MakeSpawn("Consumer1", 1),
                MakeSpawn("Consumer2", 1),
            };
            // BuildPlayer(..., playerId: 0, ...) sets every populated planet's Owner to 0 first; these
            // three are then reassigned to player 1, so only 2 of the 5 are actually THIS player's own —
            // proving the gate counts the board-wide total (5), not this player's territory (2).
            built = BuildPlayer(spawns, constants, 0, "DCSelfCheckMap4", "DCSelfCheckPlayer4");
            built.map.GetPlanet("FarB").Owner = 1;
            built.map.GetPlanet("Consumer1").Owner = 1;
            built.map.GetPlanet("Consumer2").Owner = 1;

            var results = new List<Planet.PlanetUpdateResult> { FoodSurplus("Home", 0) };
            built.playerAI.UpdateDistributionCenters(results, turnNumber: 1);

            // Otherwise-identical board, gate raised one above the board-wide total (6 > 5), to prove it
            // WOULD have blocked selection at the lower total above.
            var blockedConstants = ScriptableObject.CreateInstance<GameAIConstants>();
            blockedConstants.defaultTravelSpeed = 1f;
            blockedConstants.maxPathNodesForResourceDistribution = 2;
            blockedConstants.minPlanetsForDistributionCenters = 6;
            blockedConstants.minPlanetsForSecondDistributionCenter = 10000;

            var blockedSpawns = new List<PlanetSpawnData>
            {
                MakeSpawn("BHome", 1, new[] { "BNearA", "BFarB" }),
                MakeSpawn("BNearA", 1),
                MakeSpawn("BFarB", 1, new[] { "BHome", "BConsumer1", "BConsumer2" }),
                MakeSpawn("BConsumer1", 1),
                MakeSpawn("BConsumer2", 1),
            };
            blocked = BuildPlayer(blockedSpawns, blockedConstants, 0, "DCSelfCheckMap4b", "DCSelfCheckPlayer4b");
            blocked.map.GetPlanet("BFarB").Owner = 1;
            blocked.map.GetPlanet("BConsumer1").Owner = 1;
            blocked.map.GetPlanet("BConsumer2").Owner = 1;

            var blockedResults = new List<Planet.PlanetUpdateResult> { FoodSurplus("BHome", 0) };
            blocked.playerAI.UpdateDistributionCenters(blockedResults, turnNumber: 1);

            ok &= Check(built.playerAI.FoodDistributionCenters.Count == 1,
                "at exactly the board-wide threshold (5 total colonized, including another player's), a DC is selected");
            ok &= Check(blocked.playerAI.FoodDistributionCenters.Count == 0,
                "one above the board-wide threshold on an otherwise-identical board, no DC is selected");
        }
        finally
        {
            Object.DestroyImmediate(blocked.playerGo);
            Object.DestroyImmediate(blocked.mapGo);
            Object.DestroyImmediate(built.playerGo);
            Object.DestroyImmediate(built.mapGo);
        }
        return ok;
    }

    // Calling UpdateDistributionCenters a second time, with an equally-scoring alternate candidate
    // available, must not replace the first call's choice.
    public static bool RunStickySelectionCheck()
    {
        var ok = true;
        (GameAIMap map, PlayerAI playerAI, GameObject mapGo, GameObject playerGo) built = default;
        try
        {
            var constants = ScriptableObject.CreateInstance<GameAIConstants>();
            constants.defaultTravelSpeed = 1f;
            constants.maxPathNodesForResourceDistribution = 2;
            constants.minPlanetsForDistributionCenters = 0;
            constants.minPlanetsForSecondDistributionCenter = 10000;

            // Two equally-scoring arms (Consumer1 behind FarB, Consumer2 behind FarC) so a naive
            // re-score on the second call would have a real alternative to (wrongly) switch to.
            var spawns = new List<PlanetSpawnData>
            {
                MakeSpawn("Home", 1, new[] { "FarB", "FarC" }),
                MakeSpawn("FarB", 1, new[] { "Home", "Consumer1" }),
                MakeSpawn("Consumer1", 1),
                MakeSpawn("FarC", 1, new[] { "Home", "Consumer2" }),
                MakeSpawn("Consumer2", 1),
            };
            built = BuildPlayer(spawns, constants, 0, "DCSelfCheckMap5", "DCSelfCheckPlayer5");

            var results = new List<Planet.PlanetUpdateResult> { FoodSurplus("Home", 0) };
            built.playerAI.UpdateDistributionCenters(results, turnNumber: 1);
            var firstChoice = built.playerAI.FoodDistributionCenters.Count == 1
                ? built.playerAI.FoodDistributionCenters[0] : null;

            built.playerAI.UpdateDistributionCenters(results, turnNumber: 2);

            ok &= Check(firstChoice != null, "a DC was selected on the first call");
            ok &= Check(built.playerAI.FoodDistributionCenters.Count == 1
                        && built.playerAI.FoodDistributionCenters[0] == firstChoice,
                "the second call keeps the same DC rather than re-scoring");
        }
        finally
        {
            Object.DestroyImmediate(built.playerGo);
            Object.DestroyImmediate(built.mapGo);
        }
        return ok;
    }

    // A designated DC that loses population (captured/dead) must be pruned; if a valid replacement
    // candidate exists, this implementation selects it in the SAME call (a deliberate simplification of
    // the spec's "reselection runs the following turn" — see the plan's note on this task).
    public static bool RunPruningCheck()
    {
        var ok = true;
        (GameAIMap map, PlayerAI playerAI, GameObject mapGo, GameObject playerGo) built = default;
        try
        {
            var constants = ScriptableObject.CreateInstance<GameAIConstants>();
            constants.defaultTravelSpeed = 1f;
            constants.maxPathNodesForResourceDistribution = 2;
            constants.minPlanetsForDistributionCenters = 0;
            constants.minPlanetsForSecondDistributionCenter = 10000;

            var spawns = new List<PlanetSpawnData>
            {
                MakeSpawn("Home", 1, new[] { "FarB" }),
                MakeSpawn("FarB", 1, new[] { "Home", "Consumer1" }),
                MakeSpawn("Consumer1", 1),
            };
            built = BuildPlayer(spawns, constants, 0, "DCSelfCheckMap6", "DCSelfCheckPlayer6");

            var results = new List<Planet.PlanetUpdateResult> { FoodSurplus("Home", 0) };
            built.playerAI.UpdateDistributionCenters(results, turnNumber: 1);
            ok &= Check(built.playerAI.FoodDistributionCenters.Count == 1 && built.playerAI.FoodDistributionCenters[0] == "FarB",
                "FarB is selected initially");

            built.map.GetPlanet("FarB").Population.Clear(); // simulate the DC dying out

            built.playerAI.UpdateDistributionCenters(results, turnNumber: 2);
            ok &= Check(!built.playerAI.FoodDistributionCenters.Contains("FarB"),
                "a DC whose population died out is pruned");
        }
        finally
        {
            Object.DestroyImmediate(built.playerGo);
            Object.DestroyImmediate(built.mapGo);
        }
        return ok;
    }
}
```

This will not compile yet: `PlayerAI.UpdateDistributionCenters`, `.FoodDistributionCenters` don't exist, and `GameAIMap.TotalColonizedPlanetCount` (added in Step 1) is the only piece that already exists.

- [ ] **Step 3: Verify it fails to compile**

Run `mcp__rider__get_file_problems` on `Assets/Editor/DistributionCenterSelfCheck.cs`, `errorsOnly: true`.
Expected: errors that `PlayerAI` has no member `UpdateDistributionCenters` / `FoodDistributionCenters`.

- [ ] **Step 4: Implement the data model, gating, and heuristic on `PlayerAI`**

In `Assets/Flatspace/GameAI/PlayerAI.cs`, add these members (a reasonable spot is right after the `CanSupportColony` method, before `IsValidColonizer`, keeping colonization-adjacent AI logic grouped):

```csharp
            // ── Distribution Centers ────────────────────────────────────────

            public List<string> FoodDistributionCenters { get; private set; } = new List<string>();
            public List<string> GrotsitsDistributionCenters { get; private set; } = new List<string>();

            /// <summary>The player's own surplus-reporting planets as of the last UpdateDistributionCenters
            /// call — one turn stale by the time an order executes, since order execution runs before this
            /// turn's PlanetUpdateResults exist. Used by IsCoverageGap (Task 7).</summary>
            public List<string> LastFoodSurplusPlanets { get; private set; } = new List<string>();
            public List<string> LastGrotsitsSurplusPlanets { get; private set; } = new List<string>();

            /// <summary>Restores a saved DC list; a missing/older field passes an empty list here, i.e.
            /// "no DC yet, select fresh." Public for SaveLoadSystem/GameBoard restore.</summary>
            public void SetDistributionCenters(string resource, List<string> names)
            {
                if (resource == "Food") FoodDistributionCenters = names ?? new List<string>();
                else if (resource == "Grotsits") GrotsitsDistributionCenters = names ?? new List<string>();
            }

            /// <summary>
            /// Sticky, per-resource DC selection and pruning, once per turn. Public and free of
            /// Gameboard.Instance so the self-check can drive it directly.
            /// </summary>
            public void UpdateDistributionCenters(List<Planet.PlanetUpdateResult> results, int turnNumber)
            {
                LastFoodSurplusPlanets = results
                    .Where(x => x.PlayerID == Player.playerID
                             && x.Result == Planet.PlanetUpdateResult.PlanetUpdateResultType.PlanetUpdateResultTypeFoodSurplus)
                    .Select(x => x.Name).ToList();
                LastGrotsitsSurplusPlanets = results
                    .Where(x => x.PlayerID == Player.playerID
                             && x.Result == Planet.PlanetUpdateResult.PlanetUpdateResultType.PlanetUpdateResultTypeGrotsitsSurplus)
                    .Select(x => x.Name).ToList();

                UpdateDistributionCentersForResource(turnNumber, "Food", LastFoodSurplusPlanets, FoodDistributionCenters);
                UpdateDistributionCentersForResource(turnNumber, "Grotsits", LastGrotsitsSurplusPlanets, GrotsitsDistributionCenters);
            }

            private void UpdateDistributionCentersForResource(
                int turnNumber, string resource, List<string> producers, List<string> current)
            {
                for (var i = current.Count - 1; i >= 0; i--)
                {
                    var planet = AIMap.GetPlanet(current[i]);
                    if (planet != null && planet.Owner == Player.playerID && planet.Population.Count > 0) continue;
                    AITuningLogger.LogDCLost(turnNumber, Player.playerID, current[i], resource);
                    current.RemoveAt(i);
                }

                var allowedSlots = AllowedDistributionCenterSlots();
                if (current.Count >= allowedSlots) return;

                var colonized = AIMap.PlanetList.Where(p => p.Owner == Player.playerID && p.Population.Count > 0).ToList();
                var candidates = colonized.Where(p => !current.Contains(p.PlanetName)).ToList();

                var chosen = SelectDistributionCenter(candidates, colonized, producers);
                if (chosen == null) return;

                current.Add(chosen);
                AITuningLogger.LogDCSelected(turnNumber, Player.playerID, chosen, resource);
            }

            /// <summary>
            /// 0 below minPlanetsForDistributionCenters, 1 below minPlanetsForSecondDistributionCenter, else
            /// 2. Counts colonized planets on the WHOLE BOARD (every player), matching the topology probe
            /// this design is based on — not this player's own colonized count.
            /// </summary>
            private int AllowedDistributionCenterSlots()
            {
                var totalColonized = AIMap.TotalColonizedPlanetCount();
                if (totalColonized < AIMap.GameAIConstants.minPlanetsForDistributionCenters) return 0;
                return totalColonized < AIMap.GameAIConstants.minPlanetsForSecondDistributionCenter ? 1 : 2;
            }

            /// <summary>
            /// Coverage-maximizing DC candidate selection. A candidate must be reachable
            /// (NumNodes &lt;= maxPathNodesForResourceDistribution) from at least one producer to be
            /// eligible at all. Scored by how many OTHER colonized, non-producer planets it would newly
            /// reach that no producer already reaches; tied scores are broken by being furthest (in
            /// NumNodes) from whichever producer supplies the candidate, then by the cheapest path cost to
            /// that producer. Returns null if no candidate is reachable from any producer.
            /// </summary>
            private string SelectDistributionCenter(List<Planet> candidates, List<Planet> allColonized, List<string> producers)
            {
                string best = null;
                var bestScore = -1;
                var bestSupplyDistance = -1;
                var bestSupplyCost = float.MaxValue;
                var maxNodes = AIMap.GameAIConstants.maxPathNodesForResourceDistribution;

                foreach (var candidate in candidates)
                {
                    var pathMap = candidate.DistanceMapToPathingList;

                    var supplyingProducers = producers
                        .Where(p => pathMap.ContainsKey(p) && pathMap[p].NumNodes <= maxNodes)
                        .ToList();
                    if (supplyingProducers.Count == 0) continue;

                    var supplyDistance = supplyingProducers.Max(p => pathMap[p].NumNodes);
                    var supplyCost = supplyingProducers.Min(p => pathMap[p].Cost);

                    var score = 0;
                    foreach (var other in allColonized)
                    {
                        if (other.PlanetName == candidate.PlanetName) continue;
                        if (producers.Contains(other.PlanetName)) continue; // a producer trivially reaches itself
                        if (!pathMap.ContainsKey(other.PlanetName) || pathMap[other.PlanetName].NumNodes > maxNodes) continue;

                        var otherPathMap = other.DistanceMapToPathingList;
                        var reachedByProducer = producers.Any(p =>
                            otherPathMap.ContainsKey(p) && otherPathMap[p].NumNodes <= maxNodes);
                        if (reachedByProducer) continue;

                        score++;
                    }

                    var better = score > bestScore
                        || (score == bestScore && supplyDistance > bestSupplyDistance)
                        || (score == bestScore && supplyDistance == bestSupplyDistance && supplyCost < bestSupplyCost);
                    if (!better) continue;

                    best = candidate.PlanetName;
                    bestScore = score;
                    bestSupplyDistance = supplyDistance;
                    bestSupplyCost = supplyCost;
                }

                return best;
            }
```

- [ ] **Step 5: Wire it into the per-turn pipeline**

In `Assets/Flatspace/GameAI/PlayerAI.cs`, in `ProcessResultsStrategyExpand`, add a call between `ProcessColonizers` and `ProcessFoodShortage`:

```csharp
            private void ProcessResultsStrategyExpand(
                List<Planet.PlanetUpdateResult> results,
                Player                          player,
                ref List<GameAI.GameAIOrder>    orders)
            {
                ProcessColonizers(results, orders);
                UpdateDistributionCenters(results, Gameboard.Instance != null ? Gameboard.Instance.TurnNumber : 0);
                ProcessFoodShortage(results, orders);
                ProcessGrotsitsShortage(results, orders);
                ProcessResearch(results, orders);
                ProcessIndustry(results, orders);
                ProcessShipActions(orders);
            }
```

- [ ] **Step 6: Verify it compiles**

Run `mcp__rider__get_file_problems` on `Assets/Flatspace/GameAI/PlayerAI.cs`, `Assets/Flatspace/GameAI/GameAIMap.cs`, and `Assets/Editor/DistributionCenterSelfCheck.cs`, `errorsOnly: true`. Expected: no errors on any.

- [ ] **Step 7: Ask the user to run it in the Editor**

Ask the user to focus the Editor (this creates `DistributionCenterSelfCheck.cs.meta` — check `git status` after and stage it), run **FlatSpace → AI → Run Distribution Center Self-Check**, and confirm `ALL PASSED (N assertions ran)`.

- [ ] **Step 8: Commit**

```bash
git add Assets/Flatspace/GameAI/GameAIMap.cs Assets/Flatspace/GameAI/PlayerAI.cs Assets/Editor/DistributionCenterSelfCheck.cs Assets/Editor/DistributionCenterSelfCheck.cs.meta
git commit -m "feat(ai): sticky, coverage-maximizing Distribution Center selection"
```

**Note on the pruning/reselection timing simplification:** the spec says reselection "runs the following turn" after a DC is lost. This implementation prunes and re-selects within the same `UpdateDistributionCentersForResource` call (same turn) for simplicity — a lost DC's slot is refilled as soon as possible rather than strictly one turn later. `RunPruningCheck` above tests pruning only, not the exact turn a replacement appears; if literal next-turn timing matters later, split pruning and selection into separate per-turn passes.

---

## Task 6: Synthetic demand in the resource-shipment pipeline

**Files:**
- Modify: `Assets/Flatspace/GameAI/PlayerAI.cs` (`ProcessFoodShortage`, `ProcessGrotsitsShortage`, `ProcessResourceShipments`, `BuildResourceMatrix`)
- Modify: `Assets/Editor/PlayerAIResourceSelfCheck.cs`

**Interfaces:**
- Consumes: `PlayerAI.FoodDistributionCenters`/`GrotsitsDistributionCenters` (Task 5), `GameAIConstants.distributionCenterFoodTargetStock`/`GrotsitsTargetStock` (Task 1).
- Produces: no new public surface; `ProcessFoodShortage`/`ProcessGrotsitsShortage` now route DC demand through the existing shipment pipeline.

- [ ] **Step 1: Write the failing self-checks**

Add to `Assets/Editor/PlayerAIResourceSelfCheck.cs` (needs `using System;` — already present from the earlier resource-shipment task this session):

```csharp
    // A DC below its target stock, with NO real shortage anywhere, must still pull surplus toward it —
    // proving the synthetic demand path actually reaches the shipment pipeline.
    public static bool RunDistributionCenterPullsSurplusCheck()
    {
        var ok = true;
        var mapGo = new GameObject("PAResSelfCheckMap_DC1");
        var playerGo = new GameObject("PAResSelfCheckPlayer_DC1");
        try
        {
            var map = mapGo.AddComponent<GameAIMap>();
            var constants = ScriptableObject.CreateInstance<GameAIConstants>();
            constants.defaultTravelSpeed = 1f;
            constants.maxPathNodesForResourceDistribution = 10;
            constants.distributionCenterFoodTargetStock = 20f;
            constants.minPlanetsForDistributionCenters = 0; // default (30) would block selection on this 2-planet map

            var spawns = new List<PlanetSpawnData>
            {
                MakeSpawn("Source", connections: new[] { "DC" }),
                MakeSpawn("DC"),
            };
            map.GameAIMapInit(spawns, constants);
            // Owner is only ever set by Planet's private SetPlanetOwnership() during a real UpdatePlanet()
            // tick, which this self-check never runs — it must be assigned explicitly, or UpdateDistribution-
            // Centers' Owner-filtered "colonized planets" query would never see "DC" as a valid candidate.
            map.GetPlanet("Source").Population.Add(new Planet.Inhabitant { Player = 0 });
            map.GetPlanet("Source").Owner = 0;
            map.GetPlanet("DC").Population.Add(new Planet.Inhabitant { Player = 0 });
            map.GetPlanet("DC").Owner = 0;
            map.GetPlanet("DC").Food = 5f; // 15 short of its 20 target

            var player = playerGo.AddComponent<Player>();
            var playerAI = playerGo.AddComponent<PlayerAI>();
            playerAI.Player = player;
            playerAI.AIMap = map;
            player.playerID = 0;

            var results = new List<Planet.PlanetUpdateResult>
            {
                new Planet.PlanetUpdateResult("Source",
                    Planet.PlanetUpdateResult.PlanetUpdateResultType.PlanetUpdateResultTypeFoodSurplus,
                    30f, playerID: 0),
            };

            playerAI.UpdateDistributionCenters(results, turnNumber: 1); // designates "DC" as the Food DC
            var orders = new List<GameAI.GameAIOrder>();
            playerAI.ProcessResults(results, orders);

            var toDC = orders.Find(o =>
                o.Type == GameAI.GameAIOrder.OrderType.OrderTypeFoodTransport && o.Target == "DC");
            ok &= Check(toDC.Origin == "Source" && Mathf.Approximately(Convert.ToSingle(toDC.Data), 15f),
                "the DC pulls exactly its 15-unit gap (target 20 minus current 5), not the source's whole 30 surplus");
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(playerGo);
            UnityEngine.Object.DestroyImmediate(mapGo);
        }
        return ok;
    }

    // A real shortage and a DC's synthetic demand compete for the same single unit of surplus. The real
    // shortage must win even though the DC's gap is far larger in magnitude.
    public static bool RunRealShortageOutranksSyntheticDemandCheck()
    {
        var ok = true;
        var mapGo = new GameObject("PAResSelfCheckMap_DC2");
        var playerGo = new GameObject("PAResSelfCheckPlayer_DC2");
        try
        {
            var map = mapGo.AddComponent<GameAIMap>();
            var constants = ScriptableObject.CreateInstance<GameAIConstants>();
            constants.defaultTravelSpeed = 1f;
            constants.maxPathNodesForResourceDistribution = 10;
            constants.distributionCenterFoodTargetStock = 1000f; // huge synthetic gap
            constants.minPlanetsForDistributionCenters = 0; // default (30) would block selection on this small map

            var spawns = new List<PlanetSpawnData>
            {
                MakeSpawn("Source", connections: new[] { "DC", "Shortage" }),
                MakeSpawn("DC", connections: new[] { "Source" }),
                MakeSpawn("Shortage", connections: new[] { "Source" }),
            };
            map.GameAIMapInit(spawns, constants);
            // See the note in RunDistributionCenterPullsSurplusCheck: Owner must be set explicitly. Only
            // Source and DC need it (DC must be a valid candidate); Shortage stays uncolonized-for-DC-
            // purposes on purpose, so it can't itself be considered as a DC candidate and complicate the
            // scoring this test is checking.
            map.GetPlanet("Source").Population.Add(new Planet.Inhabitant { Player = 0 });
            map.GetPlanet("Source").Owner = 0;
            map.GetPlanet("DC").Population.Add(new Planet.Inhabitant { Player = 0 });
            map.GetPlanet("DC").Owner = 0;
            map.GetPlanet("DC").Food = 0f;

            var player = playerGo.AddComponent<Player>();
            var playerAI = playerGo.AddComponent<PlayerAI>();
            playerAI.Player = player;
            playerAI.AIMap = map;
            player.playerID = 0;

            var results = new List<Planet.PlanetUpdateResult>
            {
                new Planet.PlanetUpdateResult("Source",
                    Planet.PlanetUpdateResult.PlanetUpdateResultType.PlanetUpdateResultTypeFoodSurplus,
                    5f, playerID: 0),
                new Planet.PlanetUpdateResult("Shortage",
                    Planet.PlanetUpdateResult.PlanetUpdateResultType.PlanetUpdateResultTypeFoodShortage,
                    -5f, playerID: 0),
            };

            playerAI.UpdateDistributionCenters(results, turnNumber: 1); // designates "DC"
            var orders = new List<GameAI.GameAIOrder>();
            playerAI.ProcessResults(results, orders);

            var toShortage = orders.Find(o =>
                o.Type == GameAI.GameAIOrder.OrderType.OrderTypeFoodTransport && o.Target == "Shortage");
            ok &= Check(toShortage.Origin == "Source" && Mathf.Approximately(Convert.ToSingle(toShortage.Data), 5f),
                "the real shortage (5) claims the source's entire surplus (5) even though the DC's synthetic " +
                "gap (1000) is vastly larger, because the DC's priority is a fixed low sentinel, not gap-derived");

            var toDC = orders.Find(o =>
                o.Type == GameAI.GameAIOrder.OrderType.OrderTypeFoodTransport && o.Target == "DC");
            ok &= Check(toDC.Origin == null, "nothing is left over for the DC once the real shortage is served");
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(playerGo);
            UnityEngine.Object.DestroyImmediate(mapGo);
        }
        return ok;
    }

    // A DC that ALSO reports a real shortage the same turn must not create two decision rows for the same
    // planet+resource (BuildResourceMatrix logs an error and drops the duplicate rather than crashing, but
    // the synthetic entry must never be added in the first place).
    public static bool RunNoDuplicateRowForDCWithRealShortageCheck()
    {
        var ok = true;
        var mapGo = new GameObject("PAResSelfCheckMap_DC3");
        var playerGo = new GameObject("PAResSelfCheckPlayer_DC3");
        try
        {
            var map = mapGo.AddComponent<GameAIMap>();
            var constants = ScriptableObject.CreateInstance<GameAIConstants>();
            constants.defaultTravelSpeed = 1f;
            constants.maxPathNodesForResourceDistribution = 10;
            constants.distributionCenterFoodTargetStock = 100f;
            constants.minPlanetsForDistributionCenters = 0; // default (30) would block selection on this 2-planet map

            var spawns = new List<PlanetSpawnData>
            {
                MakeSpawn("Source", connections: new[] { "DC" }),
                MakeSpawn("DC"),
            };
            map.GameAIMapInit(spawns, constants);
            // See the note in RunDistributionCenterPullsSurplusCheck: Owner must be set explicitly.
            map.GetPlanet("Source").Population.Add(new Planet.Inhabitant { Player = 0 });
            map.GetPlanet("Source").Owner = 0;
            map.GetPlanet("DC").Population.Add(new Planet.Inhabitant { Player = 0 });
            map.GetPlanet("DC").Owner = 0;
            map.GetPlanet("DC").Food = 0f;

            var player = playerGo.AddComponent<Player>();
            var playerAI = playerGo.AddComponent<PlayerAI>();
            playerAI.Player = player;
            playerAI.AIMap = map;
            player.playerID = 0;

            var results = new List<Planet.PlanetUpdateResult>
            {
                new Planet.PlanetUpdateResult("Source",
                    Planet.PlanetUpdateResult.PlanetUpdateResultType.PlanetUpdateResultTypeFoodSurplus,
                    20f, playerID: 0),
                // DC ALSO has a real shortage this turn.
                new Planet.PlanetUpdateResult("DC",
                    Planet.PlanetUpdateResult.PlanetUpdateResultType.PlanetUpdateResultTypeFoodShortage,
                    -8f, playerID: 0),
            };

            playerAI.UpdateDistributionCenters(results, turnNumber: 1); // designates "DC"
            var orders = new List<GameAI.GameAIOrder>();
            playerAI.ProcessResults(results, orders);

            var toDC = orders.FindAll(o =>
                o.Type == GameAI.GameAIOrder.OrderType.OrderTypeFoodTransport && o.Target == "DC");
            ok &= Check(toDC.Count == 1 && Mathf.Approximately(Convert.ToSingle(toDC[0].Data), 8f),
                "exactly one shipment reaches the DC, sized to the REAL shortage (8), not a second synthetic " +
                "entry stacked on top of it");
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(playerGo);
            UnityEngine.Object.DestroyImmediate(mapGo);
        }
        return ok;
    }
```

Register all three in `Run()`, right after `ok &= RunShipmentCappedAtSurplusCheck();`:

```csharp
        ok &= RunDistributionCenterPullsSurplusCheck();
        ok &= RunRealShortageOutranksSyntheticDemandCheck();
        ok &= RunNoDuplicateRowForDCWithRealShortageCheck();
```

- [ ] **Step 2: Verify it fails to compile**

Run `mcp__rider__get_file_problems` on `Assets/Editor/PlayerAIResourceSelfCheck.cs`, `errorsOnly: true`.
Expected: an error that `GameAIConstants` has no field `distributionCenterFoodTargetStock` — wait, Task 1 already added it. The actual compile error here is that `ProcessResourceShipments`'s current signature (no DC parameters) means these tests will still compile fine syntactically but the FIRST test's assertion would fail at runtime (no synthetic demand exists yet, so `toDC` would be a default `GameAIOrder` with `Origin == null`). Confirm by inspection: `PlayerAI.cs`'s current `ProcessFoodShortage`/`ProcessResourceShipments` (from Task 5's state) do not yet read `FoodDistributionCenters` at all.

- [ ] **Step 3: Implement synthetic demand in `PlayerAI.cs`**

Replace `ProcessFoodShortage`, `ProcessGrotsitsShortage`, `ProcessResourceShipments`, and `BuildResourceMatrix` (as they exist after this session's earlier resource-shipment fix and Task 5's edits) with:

```csharp
            private void ProcessFoodShortage(
                List<Planet.PlanetUpdateResult> results,
                List<GameAI.GameAIOrder>        orders)
            {
                ProcessResourceShipments(results,
                    Planet.PlanetUpdateResult.PlanetUpdateResultType.PlanetUpdateResultTypeFoodShortage,
                    Planet.PlanetUpdateResult.PlanetUpdateResultType.PlanetUpdateResultTypeFoodSurplus,
                    incomingCheck: name => AIMap.GetPlanet(name).FoodShipmentIncoming,
                    FoodDistributionCenters,
                    AIMap.GameAIConstants.distributionCenterFoodTargetStock,
                    currentStockSelector: p => p.Food,
                    GameAI.GameAIOrder.OrderType.OrderTypeFoodTransport,
                    GameAI.GameAIOrder.OrderType.OrderTypeFoodChange,
                    GameAI.GameAIOrder.OrderType.OrderTypeFoodTransportInProgress,
                    orders);
            }

            private void ProcessGrotsitsShortage(
                List<Planet.PlanetUpdateResult> results,
                List<GameAI.GameAIOrder>        orders)
            {
                ProcessResourceShipments(results,
                    Planet.PlanetUpdateResult.PlanetUpdateResultType.PlanetUpdateResultTypeGrotsitsShortage,
                    Planet.PlanetUpdateResult.PlanetUpdateResultType.PlanetUpdateResultTypeGrotsitsSurplus,
                    incomingCheck: name => AIMap.GetPlanet(name).GrotsitsShipmentIncoming,
                    GrotsitsDistributionCenters,
                    AIMap.GameAIConstants.distributionCenterGrotsitsTargetStock,
                    currentStockSelector: p => p.Grotsits,
                    GameAI.GameAIOrder.OrderType.OrderTypeGrotsitsTransport,
                    GameAI.GameAIOrder.OrderType.OrderTypeGrotsitsChange,
                    GameAI.GameAIOrder.OrderType.OrderTypeGrotsitsTransportInProgress,
                    orders);
            }

            // Below any real shortage's priority regardless of sign convention (see the design spec's
            // Future Considerations note on ScoreMatrixDecisionElement.Priority's existing sign ambiguity,
            // which this sentinel is deliberately independent of).
            private const float DistributionCenterPrioritySentinel = float.MinValue / 2f;

            /// <summary>
            /// Shared shipment logic for any surplus -> shortage resource. A shipment is capped at
            /// min(source's remaining surplus, target's remaining shortfall) rather than always draining
            /// the source's entire surplus, so an overshoot no longer silently turns the target into an
            /// accidental new source next turn. Because a source can therefore have surplus left over
            /// after serving one shortage, the matrix is rebuilt and re-run in rounds against the
            /// remaining balances, letting a single source serve multiple shortages within the same turn
            /// (each round strictly zeroes out at least one side, so this always terminates within
            /// shortages.Count + surpluses.Count rounds).
            ///
            /// A designated Distribution Center below its target stock, with no REAL shortage already
            /// reported for it this turn, gets a synthetic shortage-shaped entry added to the same list —
            /// same rounds-based capping, same shipment orders — at a fixed low-priority sentinel so real
            /// shortages always claim surplus first.
            /// </summary>
            private void ProcessResourceShipments(
                List<Planet.PlanetUpdateResult>                  results,
                Planet.PlanetUpdateResult.PlanetUpdateResultType shortageType,
                Planet.PlanetUpdateResult.PlanetUpdateResultType surplusType,
                Func<string, bool>                               incomingCheck,
                List<string>                                     distributionCenters,
                float                                             distributionCenterTargetStock,
                Func<Planet, float>                               currentStockSelector,
                GameAI.GameAIOrder.OrderType                     transportType,
                GameAI.GameAIOrder.OrderType                     changeType,
                GameAI.GameAIOrder.OrderType                     inProgressType,
                List<GameAI.GameAIOrder>                         orders)
            {
                var surplusResults = results.FindAll(x => x.PlayerID == Player.playerID && x.Result == surplusType);
                if (surplusResults.Count == 0) return;

                var shortages = results.FindAll(x =>
                    x.Result == shortageType && x.PlayerID == Player.playerID && !incomingCheck(x.Name));

                // Tracks ONLY the entries this call synthesizes below — deliberately not the same list as
                // distributionCenters, because a DC can also have a genuine real shortage the same turn
                // (see the guard just below), and that real shortage must keep its normal, gap-derived
                // priority rather than being demoted just because the planet happens to hold a DC role.
                var syntheticShortageNames = new List<string>();
                foreach (var dcName in distributionCenters)
                {
                    if (shortages.Exists(s => s.Name == dcName)) continue; // a real shortage already covers it
                    var dcPlanet = AIMap.GetPlanet(dcName);
                    if (dcPlanet == null) continue;
                    var gap = distributionCenterTargetStock - currentStockSelector(dcPlanet);
                    if (gap <= 0f) continue;
                    shortages.Add(new Planet.PlanetUpdateResult(dcName, shortageType, -gap, Player.playerID));
                    syntheticShortageNames.Add(dcName);
                }

                if (shortages.Count == 0) return;

                // Real PlanetUpdatePlanet shortages carry a negative Data; the magnitude is what matters here.
                var remainingShortage = shortages.ToDictionary(s => s.Name, s => Mathf.Abs(Convert.ToSingle(s.Data)));
                var remainingSurplus = surplusResults.ToDictionary(s => s.Name,
                    s => Convert.ToSingle(s.Data) * AIMap.GetPlanet(s.Name).GetPopulationFraction(Player.playerID));

                var maxRounds = shortages.Count + surplusResults.Count;
                for (var round = 0; round < maxRounds; round++)
                {
                    var matrix = BuildResourceMatrix(shortages, surplusResults, remainingShortage, remainingSurplus, syntheticShortageNames);
                    if (matrix == null) break;

                    var actions = matrix.GenerateActionList(
                        actionFactory: (origin, element) => new ResourceAction { ChosenChoiceElement = element },
                        ChoiceCompare: null);
                    if (actions.Count == 0) break;

                    foreach (var action in actions)
                    {
                        var amount = Mathf.Min(remainingSurplus[action.Origin], remainingShortage[action.Target]);
                        if (amount <= 0f) continue;

                        EmitResourceOrders(action, amount, transportType, changeType, inProgressType, orders);
                        remainingSurplus[action.Origin]  -= amount;
                        remainingShortage[action.Target] -= amount;
                    }
                }
            }

            /// <summary>
            /// Builds one decision row per shortage still owed resource, offering every reachable
            /// surplus planet that still has some left. A row whose name is in syntheticShortageNames
            /// (this call's own synthetic Distribution Center entries — NOT every DC-owned planet; a DC's
            /// genuine real shortage keeps its normal priority) uses a fixed low-priority sentinel instead
            /// of its gap size, so it never outranks a real shortage. Returns null if nothing remains to
            /// match.
            /// </summary>
            private ScoreMatrix<ScoreMatrixDecisionElement, ResourceChoiceElement, ResourceAction> BuildResourceMatrix(
                List<Planet.PlanetUpdateResult> shortages,
                List<Planet.PlanetUpdateResult> surplusResults,
                Dictionary<string, float>       remainingShortage,
                Dictionary<string, float>       remainingSurplus,
                List<string>                    syntheticShortageNames)
            {
                var matrix = new ScoreMatrix<ScoreMatrixDecisionElement, ResourceChoiceElement, ResourceAction  >
                    (new ScoreMatrixDecisionComparer());

                foreach (var shortage in shortages)
                {
                    if (remainingShortage[shortage.Name] <= 0f) continue;

                    var pathMap = AIMap.GetPlanet(shortage.Name).DistanceMapToPathingList;
                    var entries = surplusResults
                        .Where(s => remainingSurplus[s.Name] > 0f
                                    && pathMap[s.Name].NumNodes
                                    <= AIMap.GameAIConstants.maxPathNodesForResourceDistribution)
                        .Select(s => new ResourceChoiceElement
                        {
                            SurplusResult = s,
                            ShortageResult = shortage,
                            Cost =  pathMap[s.Name].Cost
                        })
                        .ToList();

                    if (entries.Count > 0)
                    {
                        var decision = new ScoreMatrixDecisionElement
                        {
                            Target = shortage.Name,
                            Priority = syntheticShortageNames.Contains(shortage.Name)
                                ? DistributionCenterPrioritySentinel
                                : Convert.ToSingle(shortage.Data),
                        };

                        if (matrix.MatrixElements.ContainsKey(decision))
                        {
                            Debug.LogError("Duplicate Key in Build Resource Matrix");
                        }
                        else
                        {
                            matrix.MatrixElements.Add(decision, entries);
                        }
                    }
                }

                return matrix.MatrixElements.Count == 0 ? null : matrix;
            }
```

`EmitResourceOrders` is unchanged from this session's earlier resource-shipment fix.

- [ ] **Step 4: Verify it compiles and the assertions now pass**

Run `mcp__rider__get_file_problems` on `Assets/Flatspace/GameAI/PlayerAI.cs` and `Assets/Editor/PlayerAIResourceSelfCheck.cs`, `errorsOnly: true`. Expected: no errors on either.

- [ ] **Step 5: Ask the user to run it in the Editor**

Ask the user to focus the Editor, run **FlatSpace → AI → Run PlayerAI Resource Self-Check**, and confirm `ALL PASSED (N assertions ran)`.

- [ ] **Step 6: Commit**

```bash
git add Assets/Flatspace/GameAI/PlayerAI.cs Assets/Editor/PlayerAIResourceSelfCheck.cs
git commit -m "feat(ai): route Distribution Center synthetic demand through the shipment pipeline"
```

---

## Task 7: DC coverage-gap diagnostic

**Files:**
- Modify: `Assets/Flatspace/GameAI/PlayerAI.cs` (add `IsCoverageGap`)
- Modify: `Assets/Flatspace/GameAI/GameAI.cs` (hook the check into `ExecuteOrder`'s `OrderTypePopulationTransport` case)
- Modify: `Assets/Editor/DistributionCenterSelfCheck.cs`

**Interfaces:**
- Consumes: `PlayerAI.LastFoodSurplusPlanets`/`LastGrotsitsSurplusPlanets`, `FoodDistributionCenters`/`GrotsitsDistributionCenters` (Task 5), `AITuningLogger.LogDCCoverageGap` (Task 4).
- Produces: `PlayerAI.IsCoverageGap(Planet planet, List<string> lastKnownSurplusPlanets, List<string> distributionCenters)` (public, `bool`).

- [ ] **Step 1: Write the failing self-check**

Add to `Assets/Editor/DistributionCenterSelfCheck.cs`, a new method (register it in `Run()`):

```csharp
    public static bool RunCoverageGapCheck()
    {
        var ok = true;
        var mapGo = new GameObject("DCSelfCheckMap7");
        var playerGo = new GameObject("DCSelfCheckPlayer7");
        try
        {
            var constants = ScriptableObject.CreateInstance<GameAIConstants>();
            constants.defaultTravelSpeed = 1f;
            constants.maxPathNodesForResourceDistribution = 2;

            var spawns = new List<PlanetSpawnData>
            {
                MakeSpawn("Home", 1, new[] { "Near" }),
                MakeSpawn("Near", 1, new[] { "Home", "Far" }),
                MakeSpawn("Far", 1, new[] { "Near" }), // 2 hops (NumNodes 3) from Home, out of range 2
            };
            var map = mapGo.AddComponent<GameAIMap>();
            map.GameAIMapInit(spawns, constants);

            var player = playerGo.AddComponent<Player>();
            var playerAI = playerGo.AddComponent<PlayerAI>();
            playerAI.Player = player;
            playerAI.AIMap = map;
            player.playerID = 0;

            var homeAsSource = new List<string> { "Home" };
            var noDCs = new List<string>();

            ok &= Check(!playerAI.IsCoverageGap(map.GetPlanet("Near"), homeAsSource, noDCs),
                "Near (within range of Home) is not a coverage gap");
            ok &= Check(playerAI.IsCoverageGap(map.GetPlanet("Far"), homeAsSource, noDCs),
                "Far (out of range of Home, and no DC) IS a coverage gap");

            var withDCAtNear = new List<string> { "Near" };
            ok &= Check(!playerAI.IsCoverageGap(map.GetPlanet("Far"), homeAsSource, withDCAtNear),
                "Far is no longer a coverage gap once a DC at Near (within Far's range) exists");
        }
        finally
        {
            Object.DestroyImmediate(playerGo);
            Object.DestroyImmediate(mapGo);
        }
        return ok;
    }
```

Register it in `Run()`, right after `ok &= RunPruningCheck();`:

```csharp
        ok &= RunCoverageGapCheck();
```

- [ ] **Step 2: Verify it fails to compile**

Run `mcp__rider__get_file_problems` on `Assets/Editor/DistributionCenterSelfCheck.cs`, `errorsOnly: true`.
Expected: an error that `PlayerAI` has no member `IsCoverageGap`.

- [ ] **Step 3: Implement `IsCoverageGap`**

In `Assets/Flatspace/GameAI/PlayerAI.cs`, add this method in the Distribution Centers section (after `SelectDistributionCenter`):

```csharp
            /// <summary>
            /// True when the given planet is unreachable, for one resource, from EVERY planet in
            /// lastKnownSurplusPlanets AND every planet in distributionCenters — i.e. sticky DC selection
            /// isn't giving this planet any path to get resupplied. Uses the CACHED last-known surplus set
            /// (LastFoodSurplusPlanets/LastGrotsitsSurplusPlanets), not this turn's live results, because
            /// order execution (where this is called from) runs before this turn's PlanetUpdateResults
            /// exist — one turn stale, acceptable for a diagnostic. Public and free of Gameboard.Instance
            /// so the self-check can drive it directly.
            /// </summary>
            public bool IsCoverageGap(Planet planet, List<string> lastKnownSurplusPlanets, List<string> distributionCenters)
            {
                var maxNodes = AIMap.GameAIConstants.maxPathNodesForResourceDistribution;
                var pathMap = planet.DistanceMapToPathingList;
                return !lastKnownSurplusPlanets.Concat(distributionCenters)
                    .Any(s => pathMap.ContainsKey(s) && pathMap[s].NumNodes <= maxNodes);
            }
```

- [ ] **Step 4: Hook it into order execution**

In `Assets/Flatspace/GameAI/GameAI.cs`, in `ExecuteOrder`'s `case GameAIOrder.OrderType.OrderTypePopulationTransport:` block, replace:

```csharp
                    case GameAIOrder.OrderType.OrderTypePopulationTransport:
                        targetPlanet.ChangePopulation(Convert.ToInt32(executableOrder.Data), executableOrder.PlayerId);

                        if (targetPlanet.IsPopulationTransferInProgress(executableOrder.PlayerId))
                        {
                            targetPlanet.SetPopulationTransferInProgress(executableOrder.PlayerId, false);
                        }

                        break;
```

with:

```csharp
                    case GameAIOrder.OrderType.OrderTypePopulationTransport:
                        targetPlanet.ChangePopulation(Convert.ToInt32(executableOrder.Data), executableOrder.PlayerId);

                        if (targetPlanet.IsPopulationTransferInProgress(executableOrder.PlayerId))
                        {
                            targetPlanet.SetPopulationTransferInProgress(executableOrder.PlayerId, false);
                        }

                        var arrivingPlayerAI = Gameboard.Instance.players[executableOrder.PlayerId].playerAI;
                        if (arrivingPlayerAI.IsCoverageGap(targetPlanet,
                                arrivingPlayerAI.LastFoodSurplusPlanets, arrivingPlayerAI.FoodDistributionCenters))
                            AITuningLogger.LogDCCoverageGap(Gameboard.Instance.TurnNumber, executableOrder.PlayerId,
                                targetPlanet.PlanetName, "Food");
                        if (arrivingPlayerAI.IsCoverageGap(targetPlanet,
                                arrivingPlayerAI.LastGrotsitsSurplusPlanets, arrivingPlayerAI.GrotsitsDistributionCenters))
                            AITuningLogger.LogDCCoverageGap(Gameboard.Instance.TurnNumber, executableOrder.PlayerId,
                                targetPlanet.PlanetName, "Grotsits");

                        break;
```

- [ ] **Step 5: Verify it compiles and the assertions now pass**

Run `mcp__rider__get_file_problems` on `Assets/Flatspace/GameAI/PlayerAI.cs`, `Assets/Flatspace/GameAI/GameAI.cs`, and `Assets/Editor/DistributionCenterSelfCheck.cs`, `errorsOnly: true`. Expected: no errors on any.

- [ ] **Step 6: Ask the user to run it in the Editor**

Ask the user to focus the Editor, run **FlatSpace → AI → Run Distribution Center Self-Check**, and confirm `ALL PASSED (N assertions ran)`.

- [ ] **Step 7: Commit**

```bash
git add Assets/Flatspace/GameAI/PlayerAI.cs Assets/Flatspace/GameAI/GameAI.cs Assets/Editor/DistributionCenterSelfCheck.cs
git commit -m "feat(ai): log DCCoverageGap when a new colony has no path to any surplus planet or DC"
```

---

## Task 8: Save/load persistence

**Files:**
- Modify: `Assets/Flatspace/SaveSystem/SaveLoadSystem.cs` (`PlayerSave` struct, `GameSave` constructor)
- Modify: `Assets/Flatspace/Objects/Board/GameBoard.cs` (`InitGameFromSave`)
- Modify: `Assets/Editor/DistributionCenterSelfCheck.cs`

**Interfaces:**
- Consumes: `PlayerAI.SetDistributionCenters` (Task 5), `PlayerAI.FoodDistributionCenters`/`GrotsitsDistributionCenters` (Task 5).
- Produces: `SaveLoadSystem.GameSave.PlayerSave.foodDistributionCenters`/`grotsitsDistributionCenters` (`List<string>`).

- [ ] **Step 1: Write the failing self-check**

Add to `Assets/Editor/DistributionCenterSelfCheck.cs`, a new method (register it in `Run()`):

```csharp
    // Simulates the save/restore round trip directly on PlayerAI (SetDistributionCenters is exactly what
    // GameBoard.InitGameFromSave calls), including the older-save case where the field is missing (null).
    public static bool RunSaveRoundTripCheck()
    {
        var ok = true;
        var mapGo = new GameObject("DCSelfCheckMap8");
        var playerGo = new GameObject("DCSelfCheckPlayer8");
        try
        {
            var map = mapGo.AddComponent<GameAIMap>();
            var constants = ScriptableObject.CreateInstance<GameAIConstants>();
            var spawns = new List<PlanetSpawnData> { MakeSpawn("A", 1) };
            map.GameAIMapInit(spawns, constants);

            var player = playerGo.AddComponent<Player>();
            var playerAI = playerGo.AddComponent<PlayerAI>();
            playerAI.Player = player;
            playerAI.AIMap = map;
            player.playerID = 0;

            playerAI.SetDistributionCenters("Food", new List<string> { "A" });
            ok &= Check(playerAI.FoodDistributionCenters.Count == 1 && playerAI.FoodDistributionCenters[0] == "A",
                "SetDistributionCenters restores a saved list");

            playerAI.SetDistributionCenters("Food", null); // simulates an older save's missing field
            ok &= Check(playerAI.FoodDistributionCenters.Count == 0,
                "a null (older-save) list restores as empty, not a crash");

            playerAI.SetDistributionCenters("Grotsits", new List<string> { "A" });
            ok &= Check(playerAI.GrotsitsDistributionCenters.Count == 1
                        && playerAI.FoodDistributionCenters.Count == 0,
                "Food and Grotsits DC lists are independent");
        }
        finally
        {
            Object.DestroyImmediate(playerGo);
            Object.DestroyImmediate(mapGo);
        }
        return ok;
    }
```

Register it in `Run()`, right after `ok &= RunCoverageGapCheck();`:

```csharp
        ok &= RunSaveRoundTripCheck();
```

- [ ] **Step 2: Verify it compiles (this one should already pass — `SetDistributionCenters` exists from Task 5)**

Run `mcp__rider__get_file_problems` on `Assets/Editor/DistributionCenterSelfCheck.cs`, `errorsOnly: true`. Expected: no errors (this test exercises existing Task 5 surface; it's included here because save/load is what actually calls `SetDistributionCenters` in the real game, and this task is where that wiring gets proven in).

- [ ] **Step 3: Add the fields to `PlayerSave` and populate them on save**

In `Assets/Flatspace/SaveSystem/SaveLoadSystem.cs`, in the `PlayerSave` struct, add after `knownPlanets`:

```csharp
            public List<string> knownPlanets;
            public List<string> foodDistributionCenters;
            public List<string> grotsitsDistributionCenters;
```

In the `GameSave` constructor's player loop, add after `knownPlanets = ...`:

```csharp
                        knownPlanets = new List<string>(gameAI.GameAIMap.Knowledge.KnownPlanets(i)),
                        foodDistributionCenters = new List<string>(Gameboard.Instance.players[i].playerAI.FoodDistributionCenters),
                        grotsitsDistributionCenters = new List<string>(Gameboard.Instance.players[i].playerAI.GrotsitsDistributionCenters),
```

- [ ] **Step 4: Restore them on load**

In `Assets/Flatspace/Objects/Board/GameBoard.cs`, in `InitGameFromSave`'s player loop, add after the `SetKnownPlanets` call:

```csharp
                    GameAI.GameAIMap.Knowledge.SetKnownPlanets(
                        playerSave.playerId, playerSave.knownPlanets ?? new List<string>());
                    players[playerSave.playerId].playerAI.SetDistributionCenters(
                        "Food", playerSave.foodDistributionCenters ?? new List<string>());
                    players[playerSave.playerId].playerAI.SetDistributionCenters(
                        "Grotsits", playerSave.grotsitsDistributionCenters ?? new List<string>());
```

- [ ] **Step 5: Verify it compiles**

Run `mcp__rider__get_file_problems` on `Assets/Flatspace/SaveSystem/SaveLoadSystem.cs`, `Assets/Flatspace/Objects/Board/GameBoard.cs`, and `Assets/Editor/DistributionCenterSelfCheck.cs`, `errorsOnly: true`. Expected: no errors on any.

- [ ] **Step 6: Ask the user to run it in the Editor, then a manual save/load check**

Ask the user to focus the Editor, run **FlatSpace → AI → Run Distribution Center Self-Check**, confirm `ALL PASSED`, then do one manual Play-mode check: start a match large enough to cross `minPlanetsForDistributionCenters`, save, reload, and confirm (via the Task 9/10 UI indicators, once those land, or by inspecting the save file's `foodDistributionCenters`/`grotsitsDistributionCenters` fields directly) that the same planet is still marked as the DC after loading.

- [ ] **Step 7: Commit**

```bash
git add Assets/Flatspace/SaveSystem/SaveLoadSystem.cs Assets/Flatspace/Objects/Board/GameBoard.cs Assets/Editor/DistributionCenterSelfCheck.cs
git commit -m "feat(ai): persist Distribution Center designations across save/load"
```

---

## Task 9: PlanetUIObject visual indicator (world-space marker)

**Files:**
- Modify: `Assets/Flatspace/UI/PlanetUIObject.cs`

**Interfaces:**
- Consumes: `PlayerAI.FoodDistributionCenters`/`GrotsitsDistributionCenters` (Task 5), `Planet.Owner`, `Player.playerID`.
- Produces: `PlanetUIObject.SetDistributionCenterIndicator(bool isFoodDC, bool isGrotsitsDC)` (public).

This is a visual feature with no automated assertion available (self-checks can't inspect rendered UI). Verification is a Play-mode look, per the precedent set by `FleetSummarySelfCheck` ("covers only the per-player grouping... not the icons themselves — those need a Play-mode look").

- [ ] **Step 1: Add the outline components and the update method**

In `Assets/Flatspace/UI/PlanetUIObject.cs`, add two fields near the top (with the other private fields):

```csharp
    private Outline _foodDCOutline;
    private Outline _grotsitsDCOutline;
```

Add this method (a good spot is right after `SetOwnerColor`):

```csharp
    /// <summary>
    /// Green outline for a Food DC, brown for a Grotsits DC, both together for a planet that holds both
    /// roles (a second, larger-offset outline rather than inventing a third "mixed" color).
    /// </summary>
    public void SetDistributionCenterIndicator(bool isFoodDC, bool isGrotsitsDC)
    {
        var statsPanelImage = GetComponentInChildren<Image>();
        if (!statsPanelImage) return;

        if (_foodDCOutline == null)
        {
            _foodDCOutline = statsPanelImage.gameObject.AddComponent<Outline>();
            _foodDCOutline.effectColor = new Color(0f, 0.6f, 0f, 1f);
            _foodDCOutline.effectDistance = new Vector2(2f, -2f);
        }
        if (_grotsitsDCOutline == null)
        {
            _grotsitsDCOutline = statsPanelImage.gameObject.AddComponent<Outline>();
            _grotsitsDCOutline.effectColor = new Color(0.55f, 0.27f, 0.07f, 1f);
            _grotsitsDCOutline.effectDistance = new Vector2(4f, -4f);
        }
        _foodDCOutline.enabled = isFoodDC;
        _grotsitsDCOutline.enabled = isGrotsitsDC;
    }
```

- [ ] **Step 2: Call it from `UIUpdate`**

In `PlanetUIObject.cs`'s `UIUpdate()` method, replace:

```csharp
        SetOwnerColor(planet.Owner);
```

with:

```csharp
        SetOwnerColor(planet.Owner);

        var owner = planet.Owner;
        var ownerAI = owner != Planet.NoOwner && owner < Gameboard.Instance.players.Count
            ? Gameboard.Instance.players[owner].playerAI : null;
        SetDistributionCenterIndicator(
            ownerAI != null && ownerAI.FoodDistributionCenters.Contains(_planetName),
            ownerAI != null && ownerAI.GrotsitsDistributionCenters.Contains(_planetName));
```

- [ ] **Step 3: Verify it compiles**

Run `mcp__rider__get_file_problems` on `Assets/Flatspace/UI/PlanetUIObject.cs`, `errorsOnly: true`. Expected: no errors. (`UnityEngine.UI` is already imported in this file.)

- [ ] **Step 4: Ask the user for a Play-mode look**

Ask the user to focus the Editor, enter Play mode on a board large enough to cross `minPlanetsForDistributionCenters`, run turns until `AITuningLogger`'s log (if enabled) shows a `DCSelected` line, and visually confirm the named planet's stats panel shows a green and/or brown outline.

- [ ] **Step 5: Commit**

```bash
git add Assets/Flatspace/UI/PlanetUIObject.cs
git commit -m "feat(ui): show a colored outline on a planet's stats panel when it holds a DC role"
```

---

## Task 10: PlanetDetailUIController visual indicator (detail panel)

**Files:**
- Modify: `Assets/Flatspace/UI/MainGameScreenUI/PlanetDetailUI.uxml`
- Modify: `Assets/Flatspace/UI/MainGameScreenUI/PlanetDetailUIController.cs`

**Interfaces:**
- Consumes: `PlayerAI.FoodDistributionCenters`/`GrotsitsDistributionCenters` (Task 5).
- Produces: no new public surface; `UpdatePlanetDetail()` now also sets the new label's text.

Also a Play-mode-only visual check, same reasoning as Task 9.

- [ ] **Step 1: Add the new row to the UXML**

In `Assets/Flatspace/UI/MainGameScreenUI/PlanetDetailUI.uxml`, replace the closing sequence:

```xml
        <ui:VisualElement name="ProductionElement" picking-mode="Ignore" style="flex-grow: 1; height: 60px; flex-direction: row; align-items: stretch; justify-content: space-between;">
            <ui:Label text="Producing" name="ProductionName" picking-mode="Ignore"/>
            <ui:Label text="Label" name="ProductionItem" picking-mode="Ignore"/>
            <ui:Label text="Label" name="ProductionProgress" picking-mode="Ignore"/>
        </ui:VisualElement>
    </ui:VisualElement>
</ui:UXML>
```

with (a new row, and the panel's fixed `height` on the outer `PlanetDetailElement` bumped from `230px` to `260px` to fit it):

```xml
        <ui:VisualElement name="ProductionElement" picking-mode="Ignore" style="flex-grow: 1; height: 60px; flex-direction: row; align-items: stretch; justify-content: space-between;">
            <ui:Label text="Producing" name="ProductionName" picking-mode="Ignore"/>
            <ui:Label text="Label" name="ProductionItem" picking-mode="Ignore"/>
            <ui:Label text="Label" name="ProductionProgress" picking-mode="Ignore"/>
        </ui:VisualElement>
        <ui:VisualElement name="DistributionCenterElement" picking-mode="Ignore" style="flex-grow: 1; height: 30px; flex-direction: row; align-items: flex-start; justify-content: flex-start;">
            <ui:Label text="" name="DistributionCenterStatus" picking-mode="Ignore"/>
        </ui:VisualElement>
    </ui:VisualElement>
</ui:UXML>
```

And near the top of the same file, on the `PlanetDetailElement` line, change `height: 230px;` to `height: 260px;` (leave every other style property on that line untouched).

- [ ] **Step 2: Query and set the new label**

In `Assets/Flatspace/UI/MainGameScreenUI/PlanetDetailUIController.cs`, add a field with the others:

```csharp
    private Label _distributionCenterStatus;
```

In `Awake()`, add after the `_productionProgress` query:

```csharp
        _productionProgress = _element.Q<Label>("ProductionProgress");
        _distributionCenterStatus = _element.Q<Label>("DistributionCenterStatus");
```

In `UpdatePlanetDetail()`, add at the end of the method (before the closing brace, after the `_fleetIcon` block):

```csharp
        if (_distributionCenterStatus != null)
        {
            var owner = _planet.Owner;
            var ownerAI = owner != Planet.NoOwner && owner < Gameboard.Instance.players.Count
                ? Gameboard.Instance.players[owner].playerAI : null;
            var isFoodDC = ownerAI != null && ownerAI.FoodDistributionCenters.Contains(_planet.PlanetName);
            var isGrotsitsDC = ownerAI != null && ownerAI.GrotsitsDistributionCenters.Contains(_planet.PlanetName);

            if (isFoodDC && isGrotsitsDC) _distributionCenterStatus.text = "Distribution Center: Food, Grotsits";
            else if (isFoodDC) _distributionCenterStatus.text = "Distribution Center: Food";
            else if (isGrotsitsDC) _distributionCenterStatus.text = "Distribution Center: Grotsits";
            else _distributionCenterStatus.text = "";
        }
```

- [ ] **Step 3: Verify it compiles**

Run `mcp__rider__get_file_problems` on `Assets/Flatspace/UI/MainGameScreenUI/PlanetDetailUIController.cs`, `errorsOnly: true`. Expected: no errors.

- [ ] **Step 4: Ask the user for a Play-mode look**

Ask the user to focus the Editor, enter Play mode on a board large enough to cross `minPlanetsForDistributionCenters`, click the DC planet to open its detail panel, and visually confirm the new "Distribution Center: ..." text appears (and that the panel isn't visually clipped now that it holds one more row — adjust the UXML's row heights/panel height further if it is).

- [ ] **Step 5: Commit**

```bash
git add Assets/Flatspace/UI/MainGameScreenUI/PlanetDetailUI.uxml Assets/Flatspace/UI/MainGameScreenUI/PlanetDetailUIController.cs
git commit -m "feat(ui): show Distribution Center status text on the planet detail panel"
```
