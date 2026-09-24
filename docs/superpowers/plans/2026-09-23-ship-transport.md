# Ship Transport AI Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Let each AI player move surplus warships between its own colonized planets to fill tunable, category-based garrisons (with multi-round filling), using the existing ScoreMatrix pattern, with fleet orders that preserve each ship's owner and research snapshot.

**Architecture:** A pure planner class (`ShipTransportPlanner`) computes categories, garrisons, the current fill round, source/target roles and a `ScoreMatrix` (rows = source planets, choices = target planets) and returns `ShipAction`s. `PlayerAI.ProcessShipActions` turns each action into three orders (delayed `OrderTypeShipTransport` arrival carrying a `ShipFleetPayload`, immediate `OrderTypeShipDeparture`, immediate `OrderTypeShipTransferInProgress`). `GameAI` executes them through small public static helpers; saves carry the payload by reusing `GameSave.ShipSave`.

**Tech Stack:** Unity 6000.4.1f1, C#, JsonUtility saves, Editor self-check (`[MenuItem]` under `Assets/Editor/`, no test framework).

**Spec:** `docs/superpowers/specs/2026-09-23-ship-transport-design.md` (read its "Plan-time refinements" section at the end: it overrides the body where they differ).

## Global Constraints

- Class is `Gameboard` (not `GameBoard`), file `GameBoard.cs`. Planet is a global-namespace `MonoBehaviour`; `PlayerAI`/`GameAI`/`GameAIMap` live in `FlatSpace.AI`. `IScoreMatrix*` types and the `*Matrix.cs` structs are global namespace.
- `OrderType` serializes as an int: new values are appended last, never inserted.
- A method a self-check calls directly must be `public`, not `internal` (`Assets/Editor` is a separate assembly).
- A self-check must never depend on `Gameboard.Instance` (avoid `Planet.DockNewShip`, `GetPopulationFraction`, `BuildResearchSnapshot`); planets in checks need distinct X positions (`PathingSystem.FindPath` tie hazard); an isolated planet logs a harmless `[PathingSystem] ... no connections` error.
- Do not modify `ScoreMatrix.cs` or `ScoreMatrixDecisionComparer`.
- Every new script's `.meta` is committed alongside it (Unity generates it when the Editor regains focus).
- Commit messages end with `Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>`.
- Compile/verify only through the Unity Editor: "run" steps mean **ask the user** to focus the Unity Editor, wait for recompile, check the Console for compile errors, then run the menu item and report the Console output. Rider MCP builds are unreliable here.
- Pathing: `Planet.DistanceMapToPathingList[name]` has `.Cost` and `.NumNodes`; a missing key means unreachable.

## Review Focus

- Two source planets with the same spare-ship count must not crash the matrix (`ScoreMatrixDecisionComparer` returns 0 for some equal positive priorities). Tested in Task 5.
- Travel delay that rounds to 0 must not execute the arrival twice (a `Delayed` order with `TimingDelay <= 0` is both queued and executed). Clamp to 1; tested in Task 8.
- A player with no colonized planets, a single planet, or no spare ships gets an empty plan and no exception. Tested in Task 4/5.
- A colonized planet with no path entries (isolated) must neither crash the planner nor pin the round at 1. Tested in Task 4.
- An older save (order entries with no `fleetShips`) loads with no fleet, and arrival falls back to a rebuilt snapshot instead of throwing. Tested in Task 7 (payload null) and Task 6 (count larger than snapshots is handled by code path review; the fallback needs `Gameboard`, so it is verified in Play mode).

## File Structure

- Create `Assets/Flatspace/GameAI/ShipMatrix.cs`: `ShipChoiceElement`, `ShipAction`.
- Create `Assets/Flatspace/GameAI/ShipTransportPlanner.cs`: categories, garrisons, round, roles, matrix, plan.
- Create `Assets/Editor/ShipTransportSelfCheck.cs`: all checks, menu `FlatSpace/AI/Run Ship Transport Self-Check`.
- Modify `GameAIConstants.cs` (tunables), `Planet.cs` (incoming counter, peek/undock, edit-safe destroy, rebuilt-snapshot dock), `GameAI.cs` (enum, payload, statics, ExecuteOrder), `GameAIMap.cs` (`RecomputeIncomingShips`), `SaveLoadSystem.cs` (`fleetShips`), `PlayerAI.cs` (`ProcessShipActions`, `EmitShipOrders`), `GameBoard.cs` (notifications), `AITuningLogger.cs` (log codes), `CLAUDE.md`.

---

### Task 1: Tunables, matrix types, self-check skeleton

**Files:**
- Modify: `Assets/Flatspace/GameAI/GameAIConstants.cs`
- Create: `Assets/Flatspace/GameAI/ShipMatrix.cs`
- Create: `Assets/Editor/ShipTransportSelfCheck.cs`

**Interfaces:**
- Produces: `GameAIConstants` fields `maxPathNodesForShipTransport` (int), `garrisonSpecialized`, `garrisonOuter`, `garrisonPrime`, `garrisonHighTraffic`, `garrisonHighlySpecialized` (int), `highTrafficConnectionCount` (int), `category5UnlockShipsPerColonizedPlanet` (float). `ShipChoiceElement` (`TargetPlanet`, `Category`, `PathCost`, `SpareShips`, `Deficit`; equality on `TargetPlanet` only). `ShipAction` (`Origin`, `Target`, `Cost`, `Count`, `Kind`). Self-check helpers `Check`, `NewConstants`, `MakeSpawn(name, type, connections)`, `BuildMap`, `Colonize`, `Dock`.

- [ ] **Step 1: Write the self-check skeleton with the matrix-type check (fails to compile until Step 3)**

Create `Assets/Editor/ShipTransportSelfCheck.cs`:

```csharp
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using FlatSpace.AI;

public static class ShipTransportSelfCheck
{
    private static float _nextPlanetX;

    [MenuItem("FlatSpace/AI/Run Ship Transport Self-Check")]
    public static void Run()
    {
        var ok = RunMatrixTypesCheck();
        Debug.Log(ok
            ? "[ShipTransportSelfCheck] ALL PASSED"
            : "[ShipTransportSelfCheck] FAILURES (see errors above)");
    }

    private static bool Check(bool condition, string label)
    {
        if (!condition) Debug.LogError($"[ShipTransportSelfCheck] FAIL: {label}");
        return condition;
    }

    // Values chosen so category/garrison arithmetic in the checks is easy to read.
    private static GameAIConstants NewConstants()
    {
        var c = ScriptableObject.CreateInstance<GameAIConstants>();
        c.defaultTravelSpeed = 100f;
        c.expandPopulationTrigger = 0.8f;
        c.maxPathNodesForResourceDistribution = 10;
        c.maxPathNodesForShipTransport = 10;
        c.garrisonSpecialized = 4;
        c.garrisonOuter = 6;
        c.garrisonPrime = 4;
        c.garrisonHighTraffic = 2;
        c.garrisonHighlySpecialized = 1;
        c.highTrafficConnectionCount = 4;
        c.category5UnlockShipsPerColonizedPlanet = 2f;
        return c;
    }

    // Distinct, strictly increasing X per planet (PathingSystem.FindPath tie hazard). Reset
    // _nextPlanetX = 0 at the start of each check so path costs are 100 per hop.
    private static PlanetSpawnData MakeSpawn(string name, Planet.PlanetType type,
        IEnumerable<string> connections = null)
    {
        var resourceData = ScriptableObject.CreateInstance<PlanetResourceData>();
        resourceData._initialPopulation = 0;
        resourceData._maxPopulation = 5;

        var spawn = ScriptableObject.CreateInstance<PlanetSpawnData>();
        spawn._planetName = name;
        spawn._planetPosition = new Vector3(_nextPlanetX, 0f, 0f);
        _nextPlanetX += 100f;
        spawn._planetType = type;
        spawn._resourceData = resourceData;
        spawn._connections = connections != null ? new List<string>(connections) : new List<string>();
        return spawn;
    }

    private static GameAIMap BuildMap(GameObject go, GameAIConstants constants, params PlanetSpawnData[] spawns)
    {
        var map = go.AddComponent<GameAIMap>();
        map.GameAIMapInit(new List<PlanetSpawnData>(spawns), constants);
        return map;
    }

    private static Planet Colonize(GameAIMap map, string name, int player = 0)
    {
        var planet = map.GetPlanet(name);
        planet.Owner = player;
        planet.Population.Add(new Planet.Inhabitant { Player = player });
        return planet;
    }

    private static void Dock(Planet planet, int count, int owner = 0)
    {
        for (var i = 0; i < count; i++)
            planet.DockShipFromSave(Ship.ShipKind.WarShip, owner, new List<string>());
    }

    public static bool RunMatrixTypesCheck()
    {
        var ok = true;
        var a = new ShipChoiceElement { TargetPlanet = "X", Category = 1, PathCost = 100f, SpareShips = 3, Deficit = 4 };
        var b = new ShipChoiceElement { TargetPlanet = "X", Category = 5, PathCost = 999f, SpareShips = 9, Deficit = 1 };
        var c = new ShipChoiceElement { TargetPlanet = "Y", Category = 1, PathCost = 100f, SpareShips = 3, Deficit = 4 };
        ok &= Check(a.Equals(b), "choices for the same target are equal regardless of source-specific fields");
        ok &= Check(!a.Equals(c), "choices for different targets are not equal");
        ok &= Check(a.Target == "X", "ShipChoiceElement.Target is the target planet name");
        IScoreMatrixChoiceElement iface = a;
        ok &= Check(iface.Cost == 100f && iface.Surplus == 3f && iface.Shortage == 4f,
            "interface Cost/Surplus/Shortage map to PathCost/SpareShips/Deficit");
        return ok;
    }
}
```

- [ ] **Step 2: Ask the user to focus the Editor; expect compile errors** (`ShipChoiceElement`, `GameAIConstants` fields missing). This is the expected red state.

- [ ] **Step 3: Add the tunables**

Replace the body of `Assets/Flatspace/GameAI/GameAIConstants.cs` class (keep existing fields) so it ends:

```csharp
    public ShipData colonyShipData;
    public ShipData warShipData;

    [Header("Ship transport")]
    // Longest trip (path node count) a ship transport may take.
    public int maxPathNodesForShipTransport = 10;
    // Desired garrison (warships) per category. A planet uses the largest of its categories.
    public int garrisonSpecialized = 4;         // category 1: Desert, Industrial, Farm, Ocean
    public int garrisonOuter = 6;               // category 2: colonized with an uncolonized neighbour
    public int garrisonPrime = 4;               // category 3: Prime
    public int garrisonHighTraffic = 2;         // category 4: many connections
    public int garrisonHighlySpecialized = 1;   // category 5: Verdant, Desolate
    // A planet with at least this many neighbours is "high traffic".
    public int highTrafficConnectionCount = 4;
    // Category-5-only planets take ships only once total warships >= this * colonized planet count.
    public float category5UnlockShipsPerColonizedPlanet = 3f;
}
```

Field initializers are the defaults for the existing asset (missing YAML fields keep them), so `GameAIConstants4ProductionTypes.asset` needs no edit to work.

- [ ] **Step 4: Create `Assets/Flatspace/GameAI/ShipMatrix.cs`**

```csharp
// ShipMatrix.cs
using System;

// ── Ship choice element ──────────────────────────────────────────────────────

/// <summary>
/// One candidate target planet for a source planet's spare ships.
/// Equality is on the TARGET PLANET ONLY: ScoreMatrix removes a chosen choice from every other
/// row via Equals, and the same target appears in each row with different cost/spare values,
/// so equality must ignore them for "a target is claimed once per turn" to hold.
/// </summary>
public struct ShipChoiceElement : IScoreMatrixChoiceElement
{
    public string TargetPlanet { get; set; }
    public int    Category     { get; set; }   // 1 (best) .. 5; int.MaxValue = none
    public float  PathCost     { get; set; }
    public float  SpareShips   { get; set; }   // ships the source can spare
    public float  Deficit      { get; set; }   // ships the target still needs

    // IScoreMatrixChoiceElement
    public string Target => TargetPlanet;
    float IScoreMatrixChoiceElement.Cost     => PathCost;
    float IScoreMatrixChoiceElement.Surplus  => SpareShips;
    float IScoreMatrixChoiceElement.Shortage => Deficit;

    public bool Equals(IScoreMatrixChoiceElement other)
        => other is ShipChoiceElement s && s.TargetPlanet == TargetPlanet;
    public static bool operator ==(ShipChoiceElement a, ShipChoiceElement b) => a.Equals(b);
    public static bool operator !=(ShipChoiceElement a, ShipChoiceElement b) => !a.Equals(b);
    public override bool Equals(object obj) => obj is IScoreMatrixChoiceElement s && Equals(s);
    public override int GetHashCode() => (TargetPlanet ?? string.Empty).GetHashCode();
}

// ── Ship action ──────────────────────────────────────────────────────────────

public struct ShipAction : IScoreMatrixAction
{
    public string        Origin { get; set; }
    public string        Target { get; set; }
    public float         Cost   { get; set; }
    public int           Count  { get; set; }
    public Ship.ShipKind Kind   { get; set; }
}
```

- [ ] **Step 5: Ask the user to focus the Editor, then run `FlatSpace → AI → Run Ship Transport Self-Check`.** Expected Console: `[ShipTransportSelfCheck] ALL PASSED`, no compile errors.

- [ ] **Step 6: Commit** (after Unity generated the `.meta` files: `git status` must show `ShipMatrix.cs.meta` and `ShipTransportSelfCheck.cs.meta`)

```bash
git add Assets/Flatspace/GameAI/GameAIConstants.cs Assets/Flatspace/GameAI/ShipMatrix.cs Assets/Flatspace/GameAI/ShipMatrix.cs.meta Assets/Editor/ShipTransportSelfCheck.cs Assets/Editor/ShipTransportSelfCheck.cs.meta
git commit -m "feat(ai): add ship transport tunables, matrix types and self-check skeleton

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>"
```

---

### Task 2: Planet ship helpers

**Files:**
- Modify: `Assets/Flatspace/Objects/Planets/Planet.cs` (around lines 868-900)
- Modify: `Assets/Editor/ShipTransportSelfCheck.cs`

**Interfaces:**
- Consumes: `Planet.DockedShips`, `Planet.DockShipFromSave(kind, owner, snapshot)`.
- Produces: `List<List<string>> Planet.PeekShipSnapshots(Ship.ShipKind kind, int owner, int count)`; `int Planet.UndockShips(Ship.ShipKind kind, int owner, int count)` (returns removed count, removes the same first-N ships peek reports); `int GetIncomingShips(kind)`; `void AddIncomingShips(kind, int delta)` (clamped at 0); `void ClearIncomingShips()`; `void DockShipRebuiltSnapshot(kind, int owner)`.

- [ ] **Step 1: Add the failing check.** In the self-check add this method and add `ok &= RunPlanetShipHelpersCheck();` to `Run()`:

```csharp
    public static bool RunPlanetShipHelpersCheck()
    {
        var ok = true;
        _nextPlanetX = 0f;
        var go = new GameObject("STSelfCheckMap_PlanetShips");
        try
        {
            var map = BuildMap(go, NewConstants(),
                MakeSpawn("A", Planet.PlanetType.PlanetTypeNormal, new[] { "B" }),
                MakeSpawn("B", Planet.PlanetType.PlanetTypeNormal));
            var a = map.GetPlanet("A");
            a.DockShipFromSave(Ship.ShipKind.WarShip, 0, new List<string> { "s1" });
            a.DockShipFromSave(Ship.ShipKind.WarShip, 0, new List<string> { "s2" });
            a.DockShipFromSave(Ship.ShipKind.WarShip, 1, new List<string> { "other" });
            a.DockShipFromSave(Ship.ShipKind.ColonyShip, 0, new List<string> { "c" });

            var snaps = a.PeekShipSnapshots(Ship.ShipKind.WarShip, 0, 2);
            ok &= Check(snaps.Count == 2 && snaps[0][0] == "s1" && snaps[1][0] == "s2",
                "peek returns the first N matching ships' snapshots in dock order");
            ok &= Check(a.PeekShipSnapshots(Ship.ShipKind.WarShip, 0, 5).Count == 2,
                "peek is capped at the ships that exist for that owner and kind");
            ok &= Check(a.DockedShips.Count == 4, "peek does not remove ships");

            var removed = a.UndockShips(Ship.ShipKind.WarShip, 0, 1);
            ok &= Check(removed == 1 && a.DockedShips.Count == 3, "undock removes exactly N ships");
            ok &= Check(!a.DockedShips.Exists(s => s.ResearchSnapshot.Count > 0 && s.ResearchSnapshot[0] == "s1"),
                "undock removed the same first ship peek reported (s1)");
            ok &= Check(a.DockedShips.Exists(s => s.ResearchSnapshot.Count > 0 && s.ResearchSnapshot[0] == "s2"),
                "the second ship (s2) remains");
            ok &= Check(a.DockedShips.Exists(s => s.Owner == 1), "another player's ship is untouched");
            ok &= Check(a.DockedShips.Exists(s => s.Kind == Ship.ShipKind.ColonyShip), "colony ship is untouched");
            ok &= Check(a.UndockShips(Ship.ShipKind.WarShip, 0, 9) == 1, "undock is capped at available ships");

            ok &= Check(a.GetIncomingShips(Ship.ShipKind.WarShip) == 0, "incoming starts at 0");
            a.AddIncomingShips(Ship.ShipKind.WarShip, 3);
            ok &= Check(a.GetIncomingShips(Ship.ShipKind.WarShip) == 3, "incoming adds");
            a.AddIncomingShips(Ship.ShipKind.WarShip, -5);
            ok &= Check(a.GetIncomingShips(Ship.ShipKind.WarShip) == 0, "incoming never goes below 0");
            a.AddIncomingShips(Ship.ShipKind.WarShip, 2);
            a.ClearIncomingShips();
            ok &= Check(a.GetIncomingShips(Ship.ShipKind.WarShip) == 0, "ClearIncomingShips resets");
        }
        finally { Object.DestroyImmediate(go); }
        return ok;
    }
```

- [ ] **Step 2: Ask the user to focus the Editor. Expected:** compile errors for the missing `Planet` methods (red).

- [ ] **Step 3: Implement.** In `Planet.cs` replace `DockNewShip` … `BuildResearchSnapshot` region. `Edit` old_string 1:

```csharp
    public void DockNewShip(Ship.ShipKind kind)
    {
        var ship = CreateShip(kind, Owner);
        ship.ResearchSnapshot = BuildResearchSnapshot(kind);
        DockedShips.Add(ship);
    }
```
new_string:

```csharp
    public void DockNewShip(Ship.ShipKind kind)
    {
        DockShipRebuiltSnapshot(kind, Owner);
    }

    // Docks a ship for `owner` whose snapshot is rebuilt from that owner's CURRENT research.
    // Used for newly built ships and as the fallback for a fleet that arrives without a snapshot.
    // Needs Gameboard.Instance, so self-checks must not call it.
    public void DockShipRebuiltSnapshot(Ship.ShipKind kind, int owner)
    {
        var ship = CreateShip(kind, owner);
        ship.ResearchSnapshot = BuildResearchSnapshot(kind, owner);
        DockedShips.Add(ship);
    }
```

Old_string 2:

```csharp
    public bool UndockShip(Ship.ShipKind kind)
    {
        var ship = DockedShips.Find(s => s.Kind == kind);
        if (ship == null) return false;
        DockedShips.Remove(ship);
        Destroy(ship);
        return true;
    }

    private List<string> BuildResearchSnapshot(Ship.ShipKind kind)
    {
        if (Owner < 0 || Owner >= Gameboard.Instance.players.Count) return new List<string>();
        var subType = kind == Ship.ShipKind.ColonyShip ? "ColonyShip" : "Warship";
        var researchCatalog = Gameboard.Instance.players[Owner].playerAI.ResearchCatalog;
```
new_string:

```csharp
    public bool UndockShip(Ship.ShipKind kind)
    {
        var ship = DockedShips.Find(s => s.Kind == kind);
        if (ship == null) return false;
        DockedShips.Remove(ship);
        DestroyShipComponent(ship);
        return true;
    }

    // Snapshots of the first `count` docked ships of this kind and owner, in dock order.
    // UndockShips removes the SAME ships, so a fleet's payload matches the ships that leave.
    public List<List<string>> PeekShipSnapshots(Ship.ShipKind kind, int owner, int count)
    {
        return DockedShips
            .Where(s => s.Kind == kind && s.Owner == owner)
            .Take(count)
            .Select(s => new List<string>(s.ResearchSnapshot))
            .ToList();
    }

    public int UndockShips(Ship.ShipKind kind, int owner, int count)
    {
        var toRemove = DockedShips
            .Where(s => s.Kind == kind && s.Owner == owner)
            .Take(count)
            .ToList();
        foreach (var ship in toRemove)
        {
            DockedShips.Remove(ship);
            DestroyShipComponent(ship);
        }
        return toRemove.Count;
    }

    // Destroy() is illegal outside Play mode (self-checks run in the Editor).
    private static void DestroyShipComponent(Ship ship)
    {
        if (Application.isPlaying) Destroy(ship);
        else DestroyImmediate(ship);
    }

    // Ships of each kind currently in flight toward this planet (planned but not yet arrived).
    private readonly Dictionary<Ship.ShipKind, int> _incomingShips = new Dictionary<Ship.ShipKind, int>();
    public int GetIncomingShips(Ship.ShipKind kind)
        => _incomingShips.TryGetValue(kind, out var n) ? n : 0;
    public void AddIncomingShips(Ship.ShipKind kind, int delta)
        => _incomingShips[kind] = System.Math.Max(0, GetIncomingShips(kind) + delta);
    public void ClearIncomingShips() => _incomingShips.Clear();

    private List<string> BuildResearchSnapshot(Ship.ShipKind kind, int owner)
    {
        if (owner < 0 || owner >= Gameboard.Instance.players.Count) return new List<string>();
        var subType = kind == Ship.ShipKind.ColonyShip ? "ColonyShip" : "Warship";
        var researchCatalog = Gameboard.Instance.players[owner].playerAI.ResearchCatalog;
```

Confirm the file has `using System.Linq;` and `using System.Collections.Generic;` at the top (it already uses `.Select`/`.ToList()` and lists); add if missing.

- [ ] **Step 4: Ask the user to focus the Editor and run the self-check.** Expected: `ALL PASSED`.

- [ ] **Step 5: Commit**

```bash
git add Assets/Flatspace/Objects/Planets/Planet.cs Assets/Editor/ShipTransportSelfCheck.cs
git commit -m "feat(planet): add incoming-ship counter and peek/undock helpers for fleets

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>"
```

---

### Task 3: Planner — categories and garrisons

**Files:**
- Create: `Assets/Flatspace/GameAI/ShipTransportPlanner.cs`
- Modify: `Assets/Editor/ShipTransportSelfCheck.cs`

**Interfaces:**
- Consumes: `GameAIConstants` tunables (Task 1), `GameAIMap.GetNeighbours/GetPlanet/PlanetList`.
- Produces: `class ShipTransportPlanner` (namespace `FlatSpace.AI`), ctor `(GameAIMap map, int playerId)`, `const int NoCategory = int.MaxValue`, `bool IsColonized(Planet)`, `bool IsOuter(Planet)`, `List<int> ApplicableCategories(Planet)`, `int Category(Planet)` (lowest applicable or `NoCategory`), `int Garrison(Planet)` (largest applicable garrison, 0 if none).

- [ ] **Step 1: Add the failing check** (add `ok &= RunCategoryCheck();` to `Run()`):

```csharp
    public static bool RunCategoryCheck()
    {
        var ok = true;
        _nextPlanetX = 0f;
        var go = new GameObject("STSelfCheckMap_Categories");
        try
        {
            // A(Farm)-B(Normal)-C(Desert)-D(Normal, uncolonized). H is a hub with 4 neighbours.
            var map = BuildMap(go, NewConstants(),
                MakeSpawn("A", Planet.PlanetType.PlanetTypeFarm, new[] { "B" }),
                MakeSpawn("B", Planet.PlanetType.PlanetTypeNormal, new[] { "C" }),
                MakeSpawn("C", Planet.PlanetType.PlanetTypeDesert, new[] { "D" }),
                MakeSpawn("D", Planet.PlanetType.PlanetTypeNormal),
                MakeSpawn("H", Planet.PlanetType.PlanetTypeNormal, new[] { "X1", "X2", "X3", "X4" }),
                MakeSpawn("X1", Planet.PlanetType.PlanetTypeNormal),
                MakeSpawn("X2", Planet.PlanetType.PlanetTypeNormal),
                MakeSpawn("X3", Planet.PlanetType.PlanetTypeNormal),
                MakeSpawn("X4", Planet.PlanetType.PlanetTypeNormal),
                MakeSpawn("P", Planet.PlanetType.PlanetTypePrime, new[] { "Q" }),
                MakeSpawn("Q", Planet.PlanetType.PlanetTypeNormal),
                MakeSpawn("V", Planet.PlanetType.PlanetTypeVerdant));
            foreach (var n in new[] { "A", "B", "C", "H", "X1", "X2", "X3", "X4", "P", "V" }) Colonize(map, n);
            var planner = new ShipTransportPlanner(map, 0);

            ok &= Check(planner.Category(map.GetPlanet("A")) == 1 && planner.Garrison(map.GetPlanet("A")) == 4,
                "A (Farm) is category 1 with the specialized garrison");
            ok &= Check(!planner.IsOuter(map.GetPlanet("A")), "A has only colonized neighbours: not outer");
            ok &= Check(planner.Category(map.GetPlanet("B")) == ShipTransportPlanner.NoCategory
                        && planner.Garrison(map.GetPlanet("B")) == 0,
                "B (Normal, 2 connections, all neighbours colonized) has no category and garrison 0");
            ok &= Check(planner.IsOuter(map.GetPlanet("C")), "C has uncolonized neighbour D: outer");
            ok &= Check(planner.Category(map.GetPlanet("C")) == 1,
                "overlap: Desert+outer takes the best (lowest) category for priority");
            ok &= Check(planner.Garrison(map.GetPlanet("C")) == 6,
                "overlap: Desert+outer takes the LARGEST garrison (outer 6 > specialized 4)");
            ok &= Check(!planner.IsColonized(map.GetPlanet("D")), "D is not colonized");
            ok &= Check(planner.Category(map.GetPlanet("H")) == 4 && planner.Garrison(map.GetPlanet("H")) == 2,
                "H with 4 neighbours is high traffic (category 4, garrison 2)");
            ok &= Check(planner.Category(map.GetPlanet("X1")) == ShipTransportPlanner.NoCategory,
                "a leaf planet with one neighbour has no category");
            ok &= Check(planner.Category(map.GetPlanet("P")) == 2 && planner.Garrison(map.GetPlanet("P")) == 6,
                "Prime with an uncolonized neighbour is outer: category 2, garrison 6");
            Colonize(map, "Q");
            ok &= Check(planner.Category(map.GetPlanet("P")) == 3 && planner.Garrison(map.GetPlanet("P")) == 4,
                "once Q is colonized, P is just Prime: category 3, garrison 4");
            ok &= Check(planner.Category(map.GetPlanet("V")) == 5 && planner.Garrison(map.GetPlanet("V")) == 1,
                "Verdant is category 5 with the smallest garrison");
        }
        finally { Object.DestroyImmediate(go); }
        return ok;
    }
```

- [ ] **Step 2: Ask the user to focus the Editor. Expected:** compile error for `ShipTransportPlanner` (red).

- [ ] **Step 3: Create `Assets/Flatspace/GameAI/ShipTransportPlanner.cs`**

```csharp
// ShipTransportPlanner.cs
using System;
using System.Collections.Generic;
using System.Linq;

namespace FlatSpace
{
    namespace AI
    {
        /// <summary>
        /// Decides which of a player's planets send spare warships to which planets short of them.
        /// Pure with respect to the simulation: it reads planet/map state and returns ShipActions.
        /// Must not touch Gameboard.Instance so the Editor self-check can drive it directly.
        /// </summary>
        public class ShipTransportPlanner
        {
            public const int NoCategory = int.MaxValue;

            private readonly GameAIMap _map;
            private readonly int _playerId;
            private readonly GameAIConstants _constants;

            public ShipTransportPlanner(GameAIMap map, int playerId)
            {
                _map = map;
                _playerId = playerId;
                _constants = map.GameAIConstants;
            }

            // ── Categories and garrisons ─────────────────────────────────────

            public bool IsColonized(Planet planet)
                => planet.Owner == _playerId && planet.Population.Count > 0;

            // Colonized by this player with at least one uncolonized (no population) neighbour.
            public bool IsOuter(Planet planet)
            {
                if (!IsColonized(planet)) return false;
                foreach (var name in _map.GetNeighbours(planet.PlanetName))
                {
                    var neighbour = _map.GetPlanet(name);
                    if (neighbour != null && neighbour.Population.Count == 0) return true;
                }
                return false;
            }

            /// <summary>Every category (1..5) that applies to the planet.</summary>
            public List<int> ApplicableCategories(Planet planet)
            {
                var result = new List<int>();
                switch (planet.Type)
                {
                    case Planet.PlanetType.PlanetTypeDesert:
                    case Planet.PlanetType.PlanetTypeIndustrial:
                    case Planet.PlanetType.PlanetTypeFarm:
                    case Planet.PlanetType.PlanetTypeOcean:
                        result.Add(1);
                        break;
                }
                if (IsOuter(planet)) result.Add(2);
                if (planet.Type == Planet.PlanetType.PlanetTypePrime) result.Add(3);
                if (_map.GetNeighbours(planet.PlanetName).Count >= _constants.highTrafficConnectionCount)
                    result.Add(4);
                if (planet.Type == Planet.PlanetType.PlanetTypeVerdant
                    || planet.Type == Planet.PlanetType.PlanetTypeDesolate)
                    result.Add(5);
                return result;
            }

            /// <summary>Target priority: the best (lowest) applicable category, or NoCategory.</summary>
            public int Category(Planet planet)
            {
                var categories = ApplicableCategories(planet);
                return categories.Count == 0 ? NoCategory : categories.Min();
            }

            /// <summary>Base garrison: the largest garrison among applicable categories; 0 if none.</summary>
            public int Garrison(Planet planet) => GarrisonOf(ApplicableCategories(planet));

            protected int GarrisonOf(List<int> categories)
                => categories.Count == 0 ? 0 : categories.Max(GarrisonForCategory);

            private int GarrisonForCategory(int category)
            {
                switch (category)
                {
                    case 1: return _constants.garrisonSpecialized;
                    case 2: return _constants.garrisonOuter;
                    case 3: return _constants.garrisonPrime;
                    case 4: return _constants.garrisonHighTraffic;
                    case 5: return _constants.garrisonHighlySpecialized;
                    default: return 0;
                }
            }
        }
    }
}
```

- [ ] **Step 4: Ask the user to focus the Editor and run the self-check.** Expected: `ALL PASSED`.

- [ ] **Step 5: Commit** (include `ShipTransportPlanner.cs.meta`)

```bash
git add Assets/Flatspace/GameAI/ShipTransportPlanner.cs Assets/Flatspace/GameAI/ShipTransportPlanner.cs.meta Assets/Editor/ShipTransportSelfCheck.cs
git commit -m "feat(ai): add ship transport planner categories and garrisons

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>"
```

---

### Task 4: Planner — round and roles

**Files:**
- Modify: `Assets/Flatspace/GameAI/ShipTransportPlanner.cs`
- Modify: `Assets/Editor/ShipTransportSelfCheck.cs`

**Interfaces:**
- Consumes: Task 3 members; `Planet.GetIncomingShips` (Task 2).
- Produces: `class ShipTransportPlanner.PlanetState { Planet Planet; int Category; int Garrison; int Docked; int Incoming; int RoundGarrison; int Spare; int Deficit }`, `List<PlanetState> BuildStates()`, `int LastRound` (set by `BuildStates`), `int CountWarships(Planet)`.

Rules (from the spec): eligible = colonized planets, excluding category-5-only planets while locked (`total warships < ratio x colonized count`; locked planets are neither source nor target). Round `r = 1 + min(floor((docked+incoming)/garrison))` over planets with garrison > 0 that have a reachable peer (another participating planet within `maxPathNodesForShipTransport`); if none, `r = 1`. `RoundGarrison = r x Garrison`. `Spare = max(0, Docked - RoundGarrison)`, `Deficit = max(0, RoundGarrison - (Docked + Incoming))`.

- [ ] **Step 1: Add the failing check** (`ok &= RunRoundAndRolesCheck();`):

```csharp
    public static bool RunRoundAndRolesCheck()
    {
        var ok = true;

        // Chain A(Farm g4) - B(Desert g4) - C(Normal g0), all colonized, no uncolonized neighbours.
        _nextPlanetX = 0f;
        var go = new GameObject("STSelfCheckMap_Rounds");
        try
        {
            var map = BuildMap(go, NewConstants(),
                MakeSpawn("A", Planet.PlanetType.PlanetTypeFarm, new[] { "B" }),
                MakeSpawn("B", Planet.PlanetType.PlanetTypeDesert, new[] { "C" }),
                MakeSpawn("C", Planet.PlanetType.PlanetTypeNormal));
            var a = Colonize(map, "A"); var b = Colonize(map, "B"); var c = Colonize(map, "C");
            var planner = new ShipTransportPlanner(map, 0);

            var states = planner.BuildStates();
            ok &= Check(planner.LastRound == 1, "empty garrisons: round 1");
            ok &= Check(states.Find(s => s.Planet == a).Deficit == 4, "A needs its full garrison of 4");
            ok &= Check(states.Find(s => s.Planet == c).RoundGarrison == 0, "C has no garrison");

            Dock(c, 10);
            states = planner.BuildStates();
            ok &= Check(states.Find(s => s.Planet == c).Spare == 10, "all of C's ships are spare (garrison 0)");
            ok &= Check(states.Find(s => s.Planet == a).Spare == 0, "A holds nothing spare");

            Dock(a, 4); Dock(b, 4);
            states = planner.BuildStates();
            ok &= Check(planner.LastRound == 2, "all garrisons full: round advances to 2");
            ok &= Check(states.Find(s => s.Planet == a).RoundGarrison == 8 && states.Find(s => s.Planet == a).Deficit == 4,
                "round 2: A's target is 2 x 4 and it is 4 short");

            a.AddIncomingShips(Ship.ShipKind.WarShip, 2);
            states = planner.BuildStates();
            ok &= Check(states.Find(s => s.Planet == a).Deficit == 2, "incoming ships count against the deficit");
            a.ClearIncomingShips();
        }
        finally { Object.DestroyImmediate(go); }

        ok &= RunNewPlanetDropsRoundCheck();
        ok &= RunUnreachablePlanetCheck();
        ok &= RunCategory5UnlockCheck();
        ok &= RunNoColonizedPlanetsCheck();
        return ok;
    }

    private static bool RunNewPlanetDropsRoundCheck()
    {
        var ok = true;
        _nextPlanetX = 0f;
        var go = new GameObject("STSelfCheckMap_NewPlanet");
        try
        {
            // A(Farm) - B(Desert) - D(Farm, new and empty)
            var map = BuildMap(go, NewConstants(),
                MakeSpawn("A", Planet.PlanetType.PlanetTypeFarm, new[] { "B" }),
                MakeSpawn("B", Planet.PlanetType.PlanetTypeDesert, new[] { "D" }),
                MakeSpawn("D", Planet.PlanetType.PlanetTypeFarm));
            var a = Colonize(map, "A"); var b = Colonize(map, "B"); var d = Colonize(map, "D");
            Dock(a, 6); Dock(b, 4);
            var planner = new ShipTransportPlanner(map, 0);
            var states = planner.BuildStates();
            ok &= Check(planner.LastRound == 1, "an empty new planet pins the round at 1 (new planets fill first)");
            ok &= Check(states.Find(s => s.Planet == a).Spare == 2, "A holds 2 above 1 x garrison: spare 2");
            ok &= Check(states.Find(s => s.Planet == d).Deficit == 4, "the new planet D is 4 short");
            Dock(d, 4);
            planner.BuildStates();
            ok &= Check(planner.LastRound == 2, "once D is filled the round returns to 2");
        }
        finally { Object.DestroyImmediate(go); }
        return ok;
    }

    private static bool RunUnreachablePlanetCheck()
    {
        var ok = true;
        _nextPlanetX = 0f;
        var go = new GameObject("STSelfCheckMap_Unreachable");
        try
        {
            // Z is colonized and isolated. GameAIMapInit logs a harmless "[PathingSystem] ... no connections" error for it.
            var map = BuildMap(go, NewConstants(),
                MakeSpawn("A", Planet.PlanetType.PlanetTypeFarm, new[] { "B" }),
                MakeSpawn("B", Planet.PlanetType.PlanetTypeDesert),
                MakeSpawn("Z", Planet.PlanetType.PlanetTypeFarm));
            var a = Colonize(map, "A"); var b = Colonize(map, "B"); Colonize(map, "Z");
            Dock(a, 4); Dock(b, 4);
            var planner = new ShipTransportPlanner(map, 0);
            planner.BuildStates();
            ok &= Check(planner.LastRound == 2, "an unreachable empty planet does not pin the round at 1");
        }
        finally { Object.DestroyImmediate(go); }
        return ok;
    }

    private static bool RunCategory5UnlockCheck()
    {
        var ok = true;
        foreach (var extraColonized in new[] { false, true })
        {
            _nextPlanetX = 0f;
            var go = new GameObject("STSelfCheckMap_Cat5");
            try
            {
                var spawns = new List<PlanetSpawnData>
                {
                    MakeSpawn("A", Planet.PlanetType.PlanetTypeFarm, new[] { "B" }),
                    MakeSpawn("B", Planet.PlanetType.PlanetTypeDesert, new[] { "C" }),
                    MakeSpawn("C", Planet.PlanetType.PlanetTypeNormal, new[] { "V" }),
                    MakeSpawn("V", Planet.PlanetType.PlanetTypeVerdant, extraColonized ? new[] { "E" } : null),
                };
                if (extraColonized) spawns.Add(MakeSpawn("E", Planet.PlanetType.PlanetTypeNormal));
                var map = BuildMap(go, NewConstants(), spawns.ToArray());
                var a = Colonize(map, "A"); var b = Colonize(map, "B"); Colonize(map, "C"); Colonize(map, "V");
                if (extraColonized) Colonize(map, "E");
                Dock(a, 4); Dock(b, 3);    // 7 warships
                var planner = new ShipTransportPlanner(map, 0);

                // Threshold = ratio 2 x colonized planets: 4 planets -> 8, 5 planets -> 10.
                ok &= Check(!planner.BuildStates().Exists(s => s.Planet.PlanetName == "V"),
                    $"7 ships is below the unlock threshold: V is excluded (extraColonized={extraColonized})");
                Dock(b, 1);                // 8 warships
                var unlocked = planner.BuildStates().Exists(s => s.Planet.PlanetName == "V");
                ok &= Check(unlocked == !extraColonized,
                    $"8 ships unlocks V only when the threshold is 8, not 10 (extraColonized={extraColonized})");
                if (extraColonized)
                {
                    Dock(b, 2);            // 10 warships
                    ok &= Check(planner.BuildStates().Exists(s => s.Planet.PlanetName == "V"),
                        "10 ships unlocks V once the threshold has grown to 10");
                }
            }
            finally { Object.DestroyImmediate(go); }
        }
        return ok;
    }

    private static bool RunNoColonizedPlanetsCheck()
    {
        var ok = true;
        _nextPlanetX = 0f;
        var go = new GameObject("STSelfCheckMap_NoColonized");
        try
        {
            var map = BuildMap(go, NewConstants(),
                MakeSpawn("A", Planet.PlanetType.PlanetTypeFarm, new[] { "B" }),
                MakeSpawn("B", Planet.PlanetType.PlanetTypeDesert));
            var planner = new ShipTransportPlanner(map, 0);
            ok &= Check(planner.BuildStates().Count == 0, "a player with no colonized planets has no participants");
            ok &= Check(planner.LastRound == 1, "round defaults to 1 with no participants");
        }
        finally { Object.DestroyImmediate(go); }
        return ok;
    }
```

- [ ] **Step 2: Ask the user to focus the Editor. Expected:** compile errors for `BuildStates` / `LastRound` (red).

- [ ] **Step 3: Implement.** In `ShipTransportPlanner.cs`, insert before the closing brace of the class:

```csharp
            // ── Round and roles ──────────────────────────────────────────────

            public class PlanetState
            {
                public Planet Planet;
                public int Category;
                public int Garrison;        // base garrison
                public int Docked;
                public int Incoming;
                public int RoundGarrison;   // Garrison x current round
                public int Spare   => Math.Max(0, Docked - RoundGarrison);
                public int Deficit => Math.Max(0, RoundGarrison - (Docked + Incoming));
            }

            /// <summary>Current garrison round set by the last BuildStates call.</summary>
            public int LastRound { get; private set; } = 1;

            public int CountWarships(Planet planet)
                => planet.DockedShips.Count(s => s.Kind == Ship.ShipKind.WarShip && s.Owner == _playerId);

            /// <summary>
            /// Participating planets with this turn's round garrison filled in. Locked
            /// category-5-only planets are left out entirely (neither source nor target).
            /// </summary>
            public List<PlanetState> BuildStates()
            {
                var colonized = _map.PlanetList.Where(IsColonized).ToList();
                var totalWarships = colonized.Sum(CountWarships);
                var category5Unlocked = totalWarships
                    >= _constants.category5UnlockShipsPerColonizedPlanet * colonized.Count;

                var states = new List<PlanetState>();
                foreach (var planet in colonized)
                {
                    var categories = ApplicableCategories(planet);
                    if (!category5Unlocked && categories.Count == 1 && categories[0] == 5) continue;
                    states.Add(new PlanetState
                    {
                        Planet   = planet,
                        Category = categories.Count == 0 ? NoCategory : categories.Min(),
                        Garrison = GarrisonOf(categories),
                        Docked   = CountWarships(planet),
                        Incoming = planet.GetIncomingShips(Ship.ShipKind.WarShip),
                    });
                }

                LastRound = ComputeRound(states);
                foreach (var state in states) state.RoundGarrison = state.Garrison * LastRound;
                return states;
            }

            // r = 1 + min(floor((docked + incoming) / garrison)) over planets that have a garrison
            // and can be reached from another participant. Unreachable planets are excluded so one
            // stranded planet cannot pin the round at 1 forever.
            private int ComputeRound(List<PlanetState> states)
            {
                var pool = states.Where(s => s.Garrison > 0 && HasReachablePeer(s, states)).ToList();
                if (pool.Count == 0) return 1;
                return 1 + pool.Min(s => (s.Docked + s.Incoming) / s.Garrison);
            }

            private bool HasReachablePeer(PlanetState state, List<PlanetState> states)
            {
                var paths = state.Planet.DistanceMapToPathingList;
                return states.Any(other => other != state
                    && paths.TryGetValue(other.Planet.PlanetName, out var entry)
                    && entry.NumNodes <= _constants.maxPathNodesForShipTransport);
            }
```

Also change `GarrisonOf` in Task 3 from `protected` to `private` only if you prefer; it is used solely inside the class.

- [ ] **Step 4: Ask the user to focus the Editor and run the self-check.** Expected: `ALL PASSED` (an `[PathingSystem] ... no connections` error for `Z` is expected noise).

- [ ] **Step 5: Commit**

```bash
git add Assets/Flatspace/GameAI/ShipTransportPlanner.cs Assets/Editor/ShipTransportSelfCheck.cs
git commit -m "feat(ai): add garrison rounds and source/target roles to the ship planner

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>"
```

---

### Task 5: Planner — matrix and plan

**Files:**
- Modify: `Assets/Flatspace/GameAI/ShipTransportPlanner.cs`
- Modify: `Assets/Editor/ShipTransportSelfCheck.cs`

**Interfaces:**
- Consumes: `BuildStates`, `PlanetState`, `ShipChoiceElement`, `ShipAction` (Task 1).
- Produces: `List<ShipAction> ShipTransportPlanner.Plan()` — at most one action per source planet, each target used at most once, `Count = min(spare, deficit)`, `Kind = WarShip`.

- [ ] **Step 1: Add the failing check** (`ok &= RunPlanCheck();`):

```csharp
    public static bool RunPlanCheck()
    {
        var ok = true;

        // Chain A(Farm) - B(Desert) - C(Normal, spare 3). C is nearest to B.
        _nextPlanetX = 0f;
        var go = new GameObject("STSelfCheckMap_Plan1");
        try
        {
            var map = BuildMap(go, NewConstants(),
                MakeSpawn("A", Planet.PlanetType.PlanetTypeFarm, new[] { "B" }),
                MakeSpawn("B", Planet.PlanetType.PlanetTypeDesert, new[] { "C" }),
                MakeSpawn("C", Planet.PlanetType.PlanetTypeNormal));
            Colonize(map, "A"); Colonize(map, "B"); var c = Colonize(map, "C");
            Dock(c, 3);
            var actions = new ShipTransportPlanner(map, 0).Plan();
            ok &= Check(actions.Count == 1, "one source produces one action");
            ok &= Check(actions[0].Origin == "C" && actions[0].Target == "B",
                "same category: the nearer target (B) wins on path cost");
            ok &= Check(actions[0].Count == 3 && actions[0].Kind == Ship.ShipKind.WarShip,
                "count = min(spare 3, deficit 4) warships");
            ok &= Check(actions[0].Cost > 0f, "the action carries the path cost");

            Dock(c, 9);   // 12 spare
            actions = new ShipTransportPlanner(map, 0).Plan();
            ok &= Check(actions.Count == 1 && actions[0].Count == 4,
                "count is capped at the target's deficit (4), the rest stays home");
        }
        finally { Object.DestroyImmediate(go); }

        // Two sources with IDENTICAL spare counts and two targets: no crash, each target claimed once.
        _nextPlanetX = 0f;
        go = new GameObject("STSelfCheckMap_Plan2");
        try
        {
            var map = BuildMap(go, NewConstants(),
                MakeSpawn("A", Planet.PlanetType.PlanetTypeFarm, new[] { "B" }),
                MakeSpawn("B", Planet.PlanetType.PlanetTypeDesert, new[] { "C", "D" }),
                MakeSpawn("C", Planet.PlanetType.PlanetTypeNormal),
                MakeSpawn("D", Planet.PlanetType.PlanetTypeNormal));
            Colonize(map, "A"); Colonize(map, "B"); var c = Colonize(map, "C"); var d = Colonize(map, "D");
            Dock(c, 3); Dock(d, 3);
            var actions = new ShipTransportPlanner(map, 0).Plan();
            ok &= Check(actions.Count == 2, "two equal-spare sources both produce an action (no comparer collision)");
            ok &= Check(actions.Select(x => x.Target).Distinct().Count() == 2,
                "a target claimed by one source is removed for the other");
            ok &= Check(actions.All(x => x.Count == 3), "each action moves the source's 3 spare ships");
        }
        finally { Object.DestroyImmediate(go); }

        // Category beats cost: source S sits between Prime P (cat 3) and Desert Z (cat 1), equal distance.
        _nextPlanetX = 0f;
        go = new GameObject("STSelfCheckMap_Plan3");
        try
        {
            var map = BuildMap(go, NewConstants(),
                MakeSpawn("P", Planet.PlanetType.PlanetTypePrime, new[] { "S" }),
                MakeSpawn("S", Planet.PlanetType.PlanetTypeNormal, new[] { "Z" }),
                MakeSpawn("Z", Planet.PlanetType.PlanetTypeDesert));
            Colonize(map, "P"); var s = Colonize(map, "S"); Colonize(map, "Z");
            Dock(s, 2);
            var actions = new ShipTransportPlanner(map, 0).Plan();
            ok &= Check(actions.Count == 1 && actions[0].Target == "Z",
                "at equal cost the better category (Desert, 1) beats Prime (3)");
        }
        finally { Object.DestroyImmediate(go); }

        // Trip length limit, and empty plans.
        _nextPlanetX = 0f;
        go = new GameObject("STSelfCheckMap_Plan4");
        try
        {
            var constants = NewConstants();
            var map = BuildMap(go, constants,
                MakeSpawn("A", Planet.PlanetType.PlanetTypeFarm, new[] { "B" }),
                MakeSpawn("B", Planet.PlanetType.PlanetTypeNormal));
            Colonize(map, "A"); var b = Colonize(map, "B");
            var planner = new ShipTransportPlanner(map, 0);
            ok &= Check(planner.Plan().Count == 0, "no spare ships: empty plan, no exception");
            Dock(b, 2);
            ok &= Check(planner.Plan().Count == 1, "spare ships and a reachable target: one action");
            constants.maxPathNodesForShipTransport = 0;
            ok &= Check(planner.Plan().Count == 0, "targets beyond maxPathNodesForShipTransport are excluded");
            ok &= Check(new ShipTransportPlanner(map, 1).Plan().Count == 0,
                "a player who owns nothing gets an empty plan");
        }
        finally { Object.DestroyImmediate(go); }
        return ok;
    }
```

- [ ] **Step 2: Ask the user to focus the Editor. Expected:** compile error for `Plan` (red).

- [ ] **Step 3: Implement `Plan()`.** Add to the class (before its closing brace):

```csharp
            // ── Matrix / plan ────────────────────────────────────────────────

            /// <summary>
            /// One row per source planet (largest spare first); choices are reachable target planets
            /// ordered by category then path cost. ScoreMatrix removes a chosen target from every
            /// other row, so each target is claimed once per turn. Count = min(spare, deficit).
            /// </summary>
            public List<ShipAction> Plan()
            {
                var actions = new List<ShipAction>();
                var states = BuildStates();
                if (states.Count == 0) return actions;

                var sources = states.Where(s => s.Spare > 0)
                    .OrderByDescending(s => s.Spare)
                    .ThenBy(s => s.Planet.PlanetName, StringComparer.Ordinal)
                    .ToList();
                var targets = states.Where(s => s.Deficit > 0).ToList();
                if (sources.Count == 0 || targets.Count == 0) return actions;

                var matrix = new ScoreMatrix<ScoreMatrixDecisionElement, ShipChoiceElement, ShipAction>(
                    new ScoreMatrixDecisionComparer());

                for (var i = 0; i < sources.Count; i++)
                {
                    var source = sources[i];
                    var paths = source.Planet.DistanceMapToPathingList;
                    var entries = targets
                        .Where(t => t != source
                                    && paths.TryGetValue(t.Planet.PlanetName, out var e)
                                    && e.NumNodes <= _constants.maxPathNodesForShipTransport)
                        .Select(t => new ShipChoiceElement
                        {
                            TargetPlanet = t.Planet.PlanetName,
                            Category     = t.Category,
                            PathCost     = paths[t.Planet.PlanetName].Cost,
                            SpareShips   = source.Spare,
                            Deficit      = t.Deficit,
                        })
                        .ToList();
                    if (entries.Count == 0) continue;

                    // Priority must be UNIQUE per row: ScoreMatrixDecisionComparer can report two
                    // distinct rows as equal when their positive priorities tie (SortedDictionary
                    // would then throw). Rank order (largest spare first) is the priority.
                    matrix.MatrixElements.Add(
                        new ScoreMatrixDecisionElement
                        {
                            Target   = source.Planet.PlanetName,
                            Priority = sources.Count - i,
                        },
                        entries);
                }

                foreach (var action in matrix.GenerateActionList(
                             (origin, element) => new ShipAction
                             {
                                 Origin = origin.Target,
                                 Target = element.Target,
                                 Cost   = element.PathCost,
                                 Count  = (int)Math.Min(element.SpareShips, element.Deficit),
                                 Kind   = Ship.ShipKind.WarShip,
                             },
                             CompareChoices))
                {
                    if (action.Count > 0) actions.Add(action);
                }
                return actions;
            }

            private static int CompareChoices(ShipChoiceElement a, ShipChoiceElement b)
            {
                var byCategory = a.Category.CompareTo(b.Category);
                if (byCategory != 0) return byCategory;
                var byCost = a.PathCost.CompareTo(b.PathCost);
                return byCost != 0 ? byCost : string.CompareOrdinal(a.TargetPlanet, b.TargetPlanet);
            }
```

- [ ] **Step 4: Ask the user to focus the Editor and run the self-check.** Expected: `ALL PASSED`.

- [ ] **Step 5: Commit**

```bash
git add Assets/Flatspace/GameAI/ShipTransportPlanner.cs Assets/Editor/ShipTransportSelfCheck.cs
git commit -m "feat(ai): plan ship transports with a ScoreMatrix of sources and targets

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>"
```

---

### Task 6: Order types, payload and execution

**Files:**
- Modify: `Assets/Flatspace/GameAI/GameAI.cs` (enum lines 18-37, class fields ~46-53, `ExecuteOrder` lines 160-175)
- Modify: `Assets/Flatspace/GameAI/GameAIMap.cs`
- Modify: `Assets/Editor/ShipTransportSelfCheck.cs`

**Interfaces:**
- Consumes: Task 2 planet helpers; `GameSave.ShipSave` (fields `kind`, `owner`, `researchSnapshot`).
- Produces: `OrderType.OrderTypeShipDeparture`, `OrderType.OrderTypeShipTransferInProgress` (appended after `OrderTypeShipTransport`); `GameAI.GameAIOrder.ShipFleetPayload { Ship.ShipKind Kind; List<List<string>> Snapshots; List<SaveLoadSystem.GameSave.ShipSave> ToSave(int owner); static ShipFleetPayload FromSave(List<SaveLoadSystem.GameSave.ShipSave>) }` and field `GameAIOrder.Fleet` (`[NonSerialized]`); `public static void GameAI.ApplyShipDeparture(Planet origin, GameAIOrder order)`, `ApplyShipTransferInProgress(Planet target, GameAIOrder order)`, `ApplyShipArrival(Planet target, GameAIOrder order)`; `GameAIMap.RecomputeIncomingShips(List<GameAI.GameAIOrder> orders)`.

- [ ] **Step 1: Add the failing check** (`ok &= RunOrderExecutionCheck();`):

```csharp
    public static bool RunOrderExecutionCheck()
    {
        var ok = true;
        _nextPlanetX = 0f;
        var go = new GameObject("STSelfCheckMap_Orders");
        try
        {
            var map = BuildMap(go, NewConstants(),
                MakeSpawn("A", Planet.PlanetType.PlanetTypeNormal, new[] { "B" }),
                MakeSpawn("B", Planet.PlanetType.PlanetTypeNormal));
            var a = map.GetPlanet("A"); var b = map.GetPlanet("B");
            a.DockShipFromSave(Ship.ShipKind.WarShip, 1, new List<string> { "x" });
            a.DockShipFromSave(Ship.ShipKind.WarShip, 1, new List<string> { "y" });
            a.DockShipFromSave(Ship.ShipKind.WarShip, 1, new List<string> { "z" });

            var fleet = new GameAI.GameAIOrder.ShipFleetPayload
            {
                Kind = Ship.ShipKind.WarShip,
                Snapshots = a.PeekShipSnapshots(Ship.ShipKind.WarShip, 1, 2),
            };
            var departure = new GameAI.GameAIOrder
            {
                Type = GameAI.GameAIOrder.OrderType.OrderTypeShipDeparture,
                Data = 2, Origin = "A", Target = "A", PlayerId = 1, Fleet = fleet,
            };
            var inProgress = new GameAI.GameAIOrder
            {
                Type = GameAI.GameAIOrder.OrderType.OrderTypeShipTransferInProgress,
                Data = 2, Origin = "A", Target = "B", PlayerId = 1, Fleet = fleet,
            };
            var arrival = new GameAI.GameAIOrder
            {
                Type = GameAI.GameAIOrder.OrderType.OrderTypeShipTransport,
                Data = 2, Origin = "A", Target = "B", PlayerId = 1, Fleet = fleet,
            };

            GameAI.ApplyShipDeparture(a, departure);
            ok &= Check(a.DockedShips.Count == 1 && a.DockedShips[0].ResearchSnapshot[0] == "z",
                "departure undocks the first 2 ships (x, y); z stays");
            GameAI.ApplyShipTransferInProgress(b, inProgress);
            ok &= Check(b.GetIncomingShips(Ship.ShipKind.WarShip) == 2, "in-progress raises the target's incoming count");
            GameAI.ApplyShipArrival(b, arrival);
            ok &= Check(b.DockedShips.Count == 2, "arrival docks 2 ships");
            ok &= Check(b.DockedShips.All(s => s.Owner == 1 && s.Kind == Ship.ShipKind.WarShip),
                "arrived ships belong to the ORDER's player, not the planet's owner");
            ok &= Check(b.DockedShips[0].ResearchSnapshot[0] == "x" && b.DockedShips[1].ResearchSnapshot[0] == "y",
                "arrived ships keep the snapshots they left with");
            ok &= Check(b.GetIncomingShips(Ship.ShipKind.WarShip) == 0, "arrival clears the incoming count");

            // Recompute from in-flight orders (used after loading a save).
            b.AddIncomingShips(Ship.ShipKind.WarShip, 7);   // stale value
            map.RecomputeIncomingShips(new List<GameAI.GameAIOrder> { arrival, departure });
            ok &= Check(b.GetIncomingShips(Ship.ShipKind.WarShip) == 2,
                "recompute counts only in-flight ShipTransport orders and drops stale values");
            ok &= Check(a.GetIncomingShips(Ship.ShipKind.WarShip) == 0, "other planets are reset to 0");
        }
        finally { Object.DestroyImmediate(go); }
        return ok;
    }
```

- [ ] **Step 2: Ask the user to focus the Editor. Expected:** compile errors (red).

- [ ] **Step 3: Implement in `GameAI.cs`.**

(a) Append to the enum:

```csharp
                    OrderTypeRemoveShip,
                    OrderTypeShipTransport,
                    OrderTypeShipDeparture,
                    OrderTypeShipTransferInProgress
                }
```

(b) Add to `GameAIOrder` after `public int PlayerId;`:

```csharp
                // Ships carried by a ship order. Not serialized by Unity; saves go through GameSave.ShipSave.
                [NonSerialized] public ShipFleetPayload Fleet;

                /// <summary>What a fleet in flight is: the ship kind and one research snapshot per ship.</summary>
                public class ShipFleetPayload
                {
                    public Ship.ShipKind Kind = Ship.ShipKind.WarShip;
                    public List<List<string>> Snapshots = new List<List<string>>();

                    public List<SaveLoadSystem.GameSave.ShipSave> ToSave(int owner)
                    {
                        return Snapshots.Select(snapshot => new SaveLoadSystem.GameSave.ShipSave
                        {
                            kind = Kind,
                            owner = owner,
                            researchSnapshot = new List<string>(snapshot),
                        }).ToList();
                    }

                    /// <summary>Null or empty (older saves, non-ship orders) yields no payload.</summary>
                    public static ShipFleetPayload FromSave(List<SaveLoadSystem.GameSave.ShipSave> ships)
                    {
                        if (ships == null || ships.Count == 0) return null;
                        return new ShipFleetPayload
                        {
                            Kind = ships[0].kind,
                            Snapshots = ships
                                .Select(s => new List<string>(s.researchSnapshot ?? new List<string>()))
                                .ToList(),
                        };
                    }
                }
```

(c) Replace the whole `case GameAIOrder.OrderType.OrderTypeShipTransport:` block in `ExecuteOrder` (from `case ...OrderTypeShipTransport:` through its `break;`) with:

```csharp
                    case GameAIOrder.OrderType.OrderTypeShipTransport:
                        ApplyShipArrival(targetPlanet, executableOrder);
                        break;
                    case GameAIOrder.OrderType.OrderTypeShipDeparture:
                        ApplyShipDeparture(targetPlanet, executableOrder);
                        break;
                    case GameAIOrder.OrderType.OrderTypeShipTransferInProgress:
                        ApplyShipTransferInProgress(targetPlanet, executableOrder);
                        break;
```

(d) Add these public static methods to the `GameAI` class (public for the self-check):

```csharp
            private static Ship.ShipKind FleetKind(GameAIOrder order)
                => order.Fleet != null ? order.Fleet.Kind : Ship.ShipKind.WarShip;

            // Immediate: takes the fleet's ships off the origin planet (same first-N ships the payload was read from).
            public static void ApplyShipDeparture(Planet origin, GameAIOrder order)
            {
                origin.UndockShips(FleetKind(order), order.PlayerId, Convert.ToInt32(order.Data));
            }

            // Immediate: marks ships as on their way so the target's deficit accounts for them.
            public static void ApplyShipTransferInProgress(Planet target, GameAIOrder order)
            {
                target.AddIncomingShips(FleetKind(order), Convert.ToInt32(order.Data));
            }

            // Delayed arrival: docks the fleet for the ORDER's player with the snapshots it left with.
            // A fleet with fewer snapshots than ships (should not happen) falls back to a rebuilt one.
            public static void ApplyShipArrival(Planet target, GameAIOrder order)
            {
                var kind = FleetKind(order);
                var count = Convert.ToInt32(order.Data);
                for (var i = 0; i < count; i++)
                {
                    if (order.Fleet != null && i < order.Fleet.Snapshots.Count)
                        target.DockShipFromSave(kind, order.PlayerId, order.Fleet.Snapshots[i]);
                    else
                        target.DockShipRebuiltSnapshot(kind, order.PlayerId);
                }
                target.AddIncomingShips(kind, -count);
            }
```

- [ ] **Step 4: Implement `RecomputeIncomingShips` in `GameAIMap.cs`** (inside the class, near `GetPlayerCapitol`; add `using System;` at the top if absent):

```csharp
            /// <summary>
            /// Rebuilds every planet's incoming-ship count from in-flight ShipTransport orders.
            /// The counter is derived state, so it is recomputed on load instead of being saved.
            /// </summary>
            public void RecomputeIncomingShips(List<GameAI.GameAIOrder> orders)
            {
                foreach (var planet in PlanetList) planet.ClearIncomingShips();
                foreach (var order in orders)
                {
                    if (order.Type != GameAI.GameAIOrder.OrderType.OrderTypeShipTransport) continue;
                    var target = GetPlanet(order.Target);
                    if (target == null) continue;
                    var kind = order.Fleet != null ? order.Fleet.Kind : Ship.ShipKind.WarShip;
                    target.AddIncomingShips(kind, Convert.ToInt32(order.Data));
                }
            }
```

- [ ] **Step 5: Ask the user to focus the Editor and run the self-check.** Expected: `ALL PASSED`.

- [ ] **Step 6: Commit**

```bash
git add Assets/Flatspace/GameAI/GameAI.cs Assets/Flatspace/GameAI/GameAIMap.cs Assets/Editor/ShipTransportSelfCheck.cs
git commit -m "feat(ai): execute ship departure, in-progress and arrival orders with fleet payloads

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>"
```

---

### Task 7: Save and load the fleet payload

**Files:**
- Modify: `Assets/Flatspace/SaveSystem/SaveLoadSystem.cs` (`OrderSave` ~line 75; order write ~line 203)
- Modify: `Assets/Flatspace/GameAI/GameAI.cs` (`SetSimulationStats`)
- Modify: `Assets/Editor/ShipTransportSelfCheck.cs`

**Interfaces:**
- Consumes: `ShipFleetPayload.ToSave/FromSave`, `GameAIMap.RecomputeIncomingShips`.
- Produces: `OrderSave.fleetShips` (`List<GameSave.ShipSave>`); loaded orders carry `Fleet`; load recomputes incoming counters.

- [ ] **Step 1: Add the failing check** (`ok &= RunFleetSaveCheck();`):

```csharp
    public static bool RunFleetSaveCheck()
    {
        var ok = true;
        var fleet = new GameAI.GameAIOrder.ShipFleetPayload
        {
            Kind = Ship.ShipKind.WarShip,
            Snapshots = new List<List<string>> { new List<string> { "r1", "r2" }, new List<string>() },
        };
        var save = new SaveLoadSystem.GameSave.OrderSave
        {
            type = GameAI.GameAIOrder.OrderType.OrderTypeShipTransport,
            timingType = GameAI.GameAIOrder.OrderTimingType.OrderTimingTypeDelayed,
            timingDelay = 3, totalDelay = 3, data = 2f, dataType = "int",
            origin = "A", target = "B", playerId = 1,
            fleetShips = fleet.ToSave(1),
        };
        var json = JsonUtility.ToJson(save);
        var loaded = JsonUtility.FromJson<SaveLoadSystem.GameSave.OrderSave>(json);
        var restored = GameAI.GameAIOrder.ShipFleetPayload.FromSave(loaded.fleetShips);
        ok &= Check(restored != null && restored.Kind == Ship.ShipKind.WarShip, "fleet kind survives JSON");
        ok &= Check(restored.Snapshots.Count == 2 && restored.Snapshots[0].SequenceEqual(new[] { "r1", "r2" })
                    && restored.Snapshots[1].Count == 0,
            "each ship's snapshot survives JSON, including an empty one");
        ok &= Check(loaded.fleetShips.All(s => s.owner == 1), "saved ships record the fleet owner");

        // An older save: an order entry with no fleetShips field at all.
        const string oldJson = "{\"type\":16,\"timingType\":0,\"timingDelay\":1,\"totalDelay\":1,\"data\":1.0,"
                               + "\"dataType\":\"int\",\"target\":\"B\",\"origin\":\"A\",\"playerId\":0}";
        var old = JsonUtility.FromJson<SaveLoadSystem.GameSave.OrderSave>(oldJson);
        ok &= Check(GameAI.GameAIOrder.ShipFleetPayload.FromSave(old.fleetShips) == null,
            "an older save with no fleetShips loads as no fleet");
        ok &= Check(GameAI.GameAIOrder.ShipFleetPayload.FromSave(null) == null, "null list gives no fleet");
        ok &= Check(GameAI.GameAIOrder.ShipFleetPayload.FromSave(new List<SaveLoadSystem.GameSave.ShipSave>()) == null,
            "empty list gives no fleet");
        return ok;
    }
```

- [ ] **Step 2: Ask the user to focus the Editor. Expected:** compile error for `fleetShips` (red).

- [ ] **Step 3: Implement.**

In `SaveLoadSystem.cs`, `OrderSave`, after `public int playerId;` add:

```csharp
            // Ships in flight for a ship-transport order (null/empty for every other order and for older saves).
            public List<GameSave.ShipSave> fleetShips;
```

In the `GameSave` constructor order loop (the `new OrderSave { ... }` initializer) add after `playerId = order.PlayerId`:

```csharp
                        playerId = order.PlayerId,
                        fleetShips = order.Fleet?.ToSave(order.PlayerId)
```
(remove the trailing-comma-less `playerId = order.PlayerId` line's old form accordingly).

In `GameAI.SetSimulationStats`, add `Fleet = GameAIOrder.ShipFleetPayload.FromSave(orderStatus.fleetShips),` to the `new GameAIOrder { ... }` initializer (after `PlayerId = orderStatus.playerId,` add a comma), and after `GameAIMap.SetPlanetSimulationStats(gameSave);` add:

```csharp
                GameAIMap.RecomputeIncomingShips(CurrentAIOrders);
```

- [ ] **Step 4: Ask the user to focus the Editor and run the self-check.** Expected: `ALL PASSED`.

- [ ] **Step 5: Commit**

```bash
git add Assets/Flatspace/SaveSystem/SaveLoadSystem.cs Assets/Flatspace/GameAI/GameAI.cs Assets/Editor/ShipTransportSelfCheck.cs
git commit -m "feat(save): persist ship fleet payloads on in-flight orders

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>"
```

---

### Task 8: PlayerAI wiring

**Files:**
- Modify: `Assets/Flatspace/GameAI/PlayerAI.cs` (`ProcessResultsStrategyExpand` lines 54-64; add methods before `// ── Order factory`)
- Modify: `Assets/Editor/ShipTransportSelfCheck.cs`

**Interfaces:**
- Consumes: `ShipTransportPlanner.Plan()`, `Planet.PeekShipSnapshots`, `GameAI.GameAIOrder.ShipFleetPayload`, `MakeOrder`.
- Produces: `public void PlayerAI.ProcessShipActions(List<GameAI.GameAIOrder> orders)`; `public void PlayerAI.EmitShipOrders(ShipAction action, List<GameAI.GameAIOrder> orders)` (public for the self-check).

- [ ] **Step 1: Add the failing check** (`ok &= RunPlayerAIShipOrdersCheck();`):

```csharp
    public static bool RunPlayerAIShipOrdersCheck()
    {
        var ok = true;
        _nextPlanetX = 0f;
        var mapGo = new GameObject("STSelfCheckMap_PlayerAI");
        var playerGo = new GameObject("STSelfCheckPlayer");
        try
        {
            var constants = NewConstants();
            constants.defaultTravelSpeed = 1000000f;   // cost / speed rounds to 0: the delay must be clamped to 1
            var map = BuildMap(mapGo, constants,
                MakeSpawn("A", Planet.PlanetType.PlanetTypeFarm, new[] { "B" }),
                MakeSpawn("B", Planet.PlanetType.PlanetTypeNormal));
            Colonize(map, "A"); var b = Colonize(map, "B");
            b.DockShipFromSave(Ship.ShipKind.WarShip, 0, new List<string> { "s1" });
            b.DockShipFromSave(Ship.ShipKind.WarShip, 0, new List<string> { "s2" });

            var player = playerGo.AddComponent<Player>();
            var playerAI = playerGo.AddComponent<PlayerAI>();
            playerAI.Player = player;
            playerAI.AIMap = map;
            player.playerID = 0;

            var orders = new List<GameAI.GameAIOrder>();
            playerAI.ProcessShipActions(orders);

            ok &= Check(orders.Count == 3, "one action yields the trio: transport, departure, in-progress");
            var transport = orders.Find(o => o.Type == GameAI.GameAIOrder.OrderType.OrderTypeShipTransport);
            var departure = orders.Find(o => o.Type == GameAI.GameAIOrder.OrderType.OrderTypeShipDeparture);
            var progress  = orders.Find(o => o.Type == GameAI.GameAIOrder.OrderType.OrderTypeShipTransferInProgress);
            ok &= Check(transport != null && departure != null && progress != null, "all three order types are present");
            ok &= Check(transport.TimingType == GameAI.GameAIOrder.OrderTimingType.OrderTimingTypeDelayed
                        && transport.Origin == "B" && transport.Target == "A" && System.Convert.ToInt32(transport.Data) == 2,
                "the arrival is delayed, B -> A, carrying 2 ships");
            ok &= Check(transport.TimingDelay >= 1 && transport.TotalDelay >= 1,
                "a delay that rounds to 0 is clamped to at least 1 (never runs twice)");
            ok &= Check(departure.TimingType == GameAI.GameAIOrder.OrderTimingType.OrderTimingTypeImmediate
                        && departure.Origin == "B" && departure.Target == "B",
                "departure is immediate and acts on the origin");
            ok &= Check(progress.TimingType == GameAI.GameAIOrder.OrderTimingType.OrderTimingTypeImmediate
                        && progress.Target == "A", "in-progress is immediate and flags the destination");
            ok &= Check(transport.Fleet != null && transport.Fleet.Snapshots.Count == 2
                        && transport.Fleet.Snapshots[0][0] == "s1" && transport.Fleet.Snapshots[1][0] == "s2",
                "the payload carries the snapshots of the ships that will leave");
            ok &= Check(transport.PlayerId == 0, "orders are stamped with the player");

            // Apply the trio the way ProcessNewOrders would: immediates now, the arrival later.
            GameAI.ApplyShipDeparture(b, departure);
            GameAI.ApplyShipTransferInProgress(map.GetPlanet("A"), progress);
            ok &= Check(b.DockedShips.Count == 0, "the ships left B");
            GameAI.ApplyShipArrival(map.GetPlanet("A"), transport);
            var arrived = map.GetPlanet("A").DockedShips;
            ok &= Check(arrived.Count == 2 && arrived[0].ResearchSnapshot[0] == "s1" && arrived[1].ResearchSnapshot[0] == "s2",
                "the ships arrive at A with their snapshots");
        }
        finally
        {
            Object.DestroyImmediate(playerGo);
            Object.DestroyImmediate(mapGo);
        }
        return ok;
    }
```

Do not add `using System;` to the self-check file: it would make `Object` ambiguous with `UnityEngine.Object`. The method above uses `System.Convert` fully qualified.

- [ ] **Step 2: Ask the user to focus the Editor. Expected:** compile error for `ProcessShipActions` (red).

- [ ] **Step 3: Implement in `PlayerAI.cs`.**

In `ProcessResultsStrategyExpand`, after `ProcessIndustry(results, orders);` add:

```csharp
                ProcessShipActions(orders);
```

Add before `// ── Order factory ──` :

```csharp
            // ── Ship transport ───────────────────────────────────────────────

            /// <summary>
            /// Moves spare warships toward planets short of their garrison. The decision lives in
            /// ShipTransportPlanner; this turns each resulting ShipAction into orders.
            /// </summary>
            public void ProcessShipActions(List<GameAI.GameAIOrder> orders)
            {
                var planner = new ShipTransportPlanner(AIMap, Player.playerID);
                foreach (var action in planner.Plan())
                    EmitShipOrders(action, orders);
            }

            /// <summary>
            /// Emits the standard order trio for a fleet: delayed arrival (carrying each ship's
            /// snapshot), immediate departure from the origin, immediate "incoming" flag at the target.
            /// </summary>
            public void EmitShipOrders(ShipAction action, List<GameAI.GameAIOrder> orders)
            {
                var origin = AIMap.GetPlanet(action.Origin);
                var snapshots = origin.PeekShipSnapshots(action.Kind, Player.playerID, action.Count);
                if (snapshots.Count < action.Count)
                {
                    Debug.LogWarning($"Ship transport {action.Origin}->{action.Target} skipped: " +
                                     $"wanted {action.Count} ships, only {snapshots.Count} docked.");
                    return;
                }

                var fleet = new GameAI.GameAIOrder.ShipFleetPayload { Kind = action.Kind, Snapshots = snapshots };
                // At least 1: a Delayed order with TimingDelay <= 0 is both queued AND executed
                // immediately by ProcessNewOrders, which would dock the fleet twice.
                var delay = Math.Max(1, Convert.ToInt32(action.Cost / AIMap.GameAIConstants.defaultTravelSpeed));

                var transport = MakeOrder(GameAI.GameAIOrder.OrderType.OrderTypeShipTransport,
                    GameAI.GameAIOrder.OrderTimingType.OrderTimingTypeDelayed,
                    delay, delay, action.Count, action.Origin, action.Target);
                transport.Fleet = fleet;
                orders.Add(transport);

                var departure = MakeOrder(GameAI.GameAIOrder.OrderType.OrderTypeShipDeparture,
                    GameAI.GameAIOrder.OrderTimingType.OrderTimingTypeImmediate,
                    0, 0, action.Count, action.Origin, action.Origin);
                departure.Fleet = fleet;
                orders.Add(departure);

                var inProgress = MakeOrder(GameAI.GameAIOrder.OrderType.OrderTypeShipTransferInProgress,
                    GameAI.GameAIOrder.OrderTimingType.OrderTimingTypeImmediate,
                    0, 0, action.Count, action.Origin, action.Target);
                inProgress.Fleet = fleet;
                orders.Add(inProgress);
            }
```

- [ ] **Step 4: Ask the user to focus the Editor and run the self-check.** Expected: `ALL PASSED`.

- [ ] **Step 5: Commit**

```bash
git add Assets/Flatspace/GameAI/PlayerAI.cs Assets/Editor/ShipTransportSelfCheck.cs
git commit -m "feat(ai): add ProcessShipActions to the Expand strategy

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>"
```

---

### Task 9: Notifications and tuning log

**Files:**
- Modify: `Assets/Flatspace/Objects/Board/GameBoard.cs` (`CreateNotificationsForExecutingOrders` ~line 767, `CreateNotificationsForNewOrders` ~line 805)
- Modify: `Assets/Flatspace/Diagnostics/AITuningLogger.cs` (`LogNewOrders` ~line 40, `LogExecutingOrders` ~line 69)

No self-check (notification text and log output are verified in Play mode, per the spec; the logger has none by design).

- [ ] **Step 1: `GameBoard.cs`.** In `CreateNotificationsForExecutingOrders`, add before `default:`:

```csharp
                        case GameAI.GameAIOrder.OrderType.OrderTypeShipTransport:
                            _playerNotifications.Add(new PlayerNotification
                            {
                                PlayerName = order.PlayerId.ToString(),
                                Message = "Fleet of " + order.Data + " " + FleetKindLabel(order) + " arrived at " +
                                          order.Target + " from " + order.Origin,
                                ViewTarget = order.Target,
                            });
                            break;
```

In `CreateNotificationsForNewOrders`, add before `default:`:

```csharp
                         case GameAI.GameAIOrder.OrderType.OrderTypeShipTransport:
                             _playerNotifications.Add(new PlayerNotification
                             {
                                 PlayerName = order.PlayerId.ToString(),
                                 Message = "Fleet of " + order.Data + " " + FleetKindLabel(order) + " departing " +
                                           order.Origin + " for " + order.Target,
                                 ViewTarget = order.Origin,
                             });
                             break;
```

Add a private helper next to them:

```csharp
            private static string FleetKindLabel(GameAI.GameAIOrder order)
                => order.Fleet != null && order.Fleet.Kind == Ship.ShipKind.ColonyShip ? "colony ships" : "warships";
```

- [ ] **Step 2: `AITuningLogger.cs`.** In `LogNewOrders`'s switch add:

```csharp
                case GameAI.GameAIOrder.OrderType.OrderTypeShipTransport:
                    lines.Add(FormatLine(turnNumber, order.PlayerId, "ShipMove",
                        $"{order.Origin}->{order.Target}", order.Data.ToString()));
                    break;
```

In `LogExecutingOrders`'s switch add:

```csharp
                case GameAI.GameAIOrder.OrderType.OrderTypeShipTransport:
                    lines.Add(FormatLine(turnNumber, order.PlayerId, "ShipArrive",
                        $"{order.Origin}->{order.Target}", order.Data.ToString()));
                    break;
```

- [ ] **Step 3: Ask the user to focus the Editor and confirm there are no compile errors and the self-check still passes.**

- [ ] **Step 4: Commit**

```bash
git add Assets/Flatspace/Objects/Board/GameBoard.cs Assets/Flatspace/Diagnostics/AITuningLogger.cs
git commit -m "feat(ai): notify and log ship fleet departures and arrivals

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>"
```

---

### Task 10: Documentation and Play-mode verification

**Files:**
- Modify: `CLAUDE.md`

- [ ] **Step 1: Update `CLAUDE.md`.**
  - In **Orders**, replace the paragraph starting `OrderType also has OrderTypeShipTransport` with: ship transport is now driven by `PlayerAI.ProcessShipActions` (Expand strategy only) and uses three order types appended last — `OrderTypeShipTransport` (delayed arrival, `Data` = ship count, carries a `Fleet` payload), `OrderTypeShipDeparture` (immediate undock at the origin) and `OrderTypeShipTransferInProgress` (immediate incoming flag at the target). Arrival docks ships for the order's `PlayerId` with the snapshots they left with (`GameAI.ApplyShipArrival`); the transport delay is clamped to >= 1.
  - Add a **Ship Transport** section under Architecture covering: `ShipTransportPlanner` (`Assets/Flatspace/GameAI/`), the five categories, priority = best category and garrison = largest garrison among applicable categories, garrison rounds (`r = 1 + min(floor((docked+incoming)/garrison))`, so a new planet drops the round back to 1), locked category-5-only planets excluded until total warships >= `category5UnlockShipsPerColonizedPlanet` x colonized planets, rows = sources / choices = targets with `ShipChoiceElement` equality on the target only, unique row priorities (the decision comparer collides on equal positive priorities), `Planet.GetIncomingShips` (derived, recomputed on load by `GameAIMap.RecomputeIncomingShips`), payload saved as `OrderSave.fleetShips` reusing `GameSave.ShipSave`, tunables on `GameAIConstants` (`maxPathNodesForShipTransport`, five `garrison*`, `highTrafficConnectionCount`, `category5UnlockShipsPerColonizedPlanet`, all with in-code defaults so the asset needs no edit).
  - In **Tests**, list `FlatSpace → AI → Run Ship Transport Self-Check` (`Assets/Editor/ShipTransportSelfCheck.cs`).
  - In **AI Tuning Log**, add event codes `ShipMove` (departure order created) and `ShipArrive`.

- [ ] **Step 2: Commit**

```bash
git add CLAUDE.md
git commit -m "docs: document ship transport in CLAUDE.md

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>"
```

- [ ] **Step 3: Play-mode verification (ask the user to do this and report).** Set `_logAIEvents` on the `MainMenu` component, start a match from `MainMenu.unity`, run ~50-100 turns, then check: (1) magenta ship lines appear between owned planets and notifications read "Fleet of N warships departing X for Y" / "... arrived at Y from X"; (2) the newest file in `Assets/Flatspace/AITuningLogs` has matching `ShipMove`/`ShipArrive` lines; (3) save mid-match with fleets in flight, reload, and confirm the fleets still arrive and no duplicate ships appear; (4) no exceptions in the Console. Report tuning observations (garrison sizes, category-5 unlock) for the user to adjust on `GameAIConstants4ProductionTypes.asset`.
