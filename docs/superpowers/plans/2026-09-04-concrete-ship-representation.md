# Concrete Ship Representation & Fleet UI Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Give `ColonyShip`/`WarShip` a concrete, per-instance representation (a real `Ship` component docked on its `Planet`), surface their presence via icons on the gameboard and Planet Detail, add a Fleet UI panel to inspect them, make in-flight order lines follow the real A* path, and add a (currently untriggered) order type for moving WarShips between planets.

**Architecture:** `Ship` is a `MonoBehaviour` (one class, a `ShipKind` enum — mirrors `Planet`'s own `PlanetType` convention) stacked onto a `Planet`'s GameObject via `this.AddComponent<Ship>()`, tracked in a new `Planet.DockedShips` list that replaces the old `HasColonyShip` bool. A new `OrderTypeShipTransport` order type (mechanism only, nothing creates it yet) docks/undocks WarShips at a target planet using a signed count, following the existing signed-`Data`-at-`Target` pattern already used by `OrderTypePopulationChange`. Order-line visualization is upgraded to walk the already-computed A* `Path` (exposed via a new `GameAIMap.GetPath`) instead of interpolating a straight line between two planet endpoints. UI additions (gameboard icon, Planet Detail icon, Fleet UI panel) reuse the existing per-turn UI refresh, `IPointerClickHandler`, and outside-click-to-close mechanisms already in the codebase rather than inventing new ones.

**Tech Stack:** Unity 6000.4.1f1, C#, UI Toolkit (Fleet UI, Planet Detail icon), uGUI (gameboard icon), New Input System.

**Spec:** `docs/superpowers/specs/2026-09-04-concrete-ship-representation-design.md`

## Global Constraints

- The Production catalog spells the WarShip subType `"Warship"` (lowercase *s*) — every subType string comparison for WarShip must use that exact casing. The Research catalog also uses `"Warship"` for its `"Ship Improvement"` entry, and `"ColonyShip"` for both catalogs' ColonyShip entries — no casing mismatch there.
- `GameAIOrder.OrderType` is serialized by `JsonUtility` as its underlying **int** value (not by name) via `GameSave.OrderSave.type`. Any new enum value **must** be appended at the very end of the enum, never inserted in the middle, or every existing save file's order types silently renumber.
- No automated test framework is wired up in this repo (`com.unity.test-framework` is a dependency but unused — see `CLAUDE.md`). Every task's verification step is therefore: (a) a Rider compile/inspection check via the `mcp__rider__*` tools, run by whoever executes this task, and (b) for tasks with player-observable behavior, a specific manual Play-mode check. Do not introduce a test assembly as part of this plan — that would be new project infrastructure the user hasn't asked for.
- Three tasks (9, 10, 11) touch prefabs or the scene file. Prefab/scene binary-ish YAML edits are **not** to be hand-authored — instead the gameboard icon is built entirely at runtime in code (so it applies uniformly to all ~9 per-planet-type prefabs without touching any of them), and the one genuinely new scene object (the Fleet UI panel's `UIDocument`) is called out as an explicit manual Unity Editor step with exact values to enter.

---

## Task 1: `Ship` component

**Files:**
- Create: `Assets/Flatspace/Objects/Ships/Ship.cs`

**Interfaces:**
- Produces: `Ship : MonoBehaviour` with `public enum ShipKind { ColonyShip, WarShip }`, `public ShipKind Kind`, `public int Owner`, `public ShipData Template`, `public List<string> ResearchSnapshot`. Consumed by every later task.

- [ ] **Step 1: Write `Ship.cs`**

```csharp
using System.Collections.Generic;
using UnityEngine;

public class Ship : MonoBehaviour
{
    public enum ShipKind
    {
        ColonyShip,
        WarShip
    }

    public ShipKind Kind;
    public int Owner = Planet.NoOwner;
    public ShipData Template;
    public List<string> ResearchSnapshot = new List<string>();
}
```

- [ ] **Step 2: Compile-check**

Run `mcp__rider__get_file_problems` on `Assets/Flatspace/Objects/Ships/Ship.cs` with `errorsOnly: true`.
Expected: no errors. (`Planet.NoOwner` is a pre-existing `public static int` on `Planet` — this compiles even though no other task has run yet.)

- [ ] **Step 3: Commit**

```bash
git add Assets/Flatspace/Objects/Ships/Ship.cs Assets/Flatspace/Objects/Ships/Ship.cs.meta
git commit -m "Add Ship component for docked ColonyShip/WarShip instances"
```

---

## Task 2: Ship templates on `GameAIConstants`

**Files:**
- Modify: `Assets/Flatspace/GameAI/GameAIConstants.cs`
- Modify: `Assets/GameAIConstants4ProductionTypes.asset` (the active constants asset — direct YAML text edit, not a scene/prefab)

**Interfaces:**
- Consumes: nothing new.
- Produces: `GameAIConstants.colonyShipData` / `.warShipData` (type `ShipData`), read by `Planet` in Task 3.

- [ ] **Step 1: Add the two fields**

In `Assets/Flatspace/GameAI/GameAIConstants.cs`, change:

```csharp
    public List<ModifierListForPlanetStrategy> productionModifierLists;
    public List<PlanetResourceData> resourceData;

}
```
to:
```csharp
    public List<ModifierListForPlanetStrategy> productionModifierLists;
    public List<PlanetResourceData> resourceData;
    public ShipData colonyShipData;
    public ShipData warShipData;

}
```

- [ ] **Step 2: Wire the two existing placeholder assets into the active constants asset**

`WarShip.asset`'s GUID is `b8400d812184ad7438279ac32978464d`, `ColonyShip.asset`'s GUID is
`f6b5445eaddc90a4b8c222c727f858ef` (both read from their `.meta` files; both assets use Unity's
standard ScriptableObject `fileID: 11400000`). In `Assets/GameAIConstants4ProductionTypes.asset`,
after the existing `resourceData:` list (the last field in the file), append:

```yaml
  colonyShipData: {fileID: 11400000, guid: f6b5445eaddc90a4b8c222c727f858ef, type: 2}
  warShipData: {fileID: 11400000, guid: b8400d812184ad7438279ac32978464d, type: 2}
```

- [ ] **Step 3: Compile-check**

Run `mcp__rider__get_file_problems` on `Assets/Flatspace/GameAI/GameAIConstants.cs`, `errorsOnly: true`. Expected: no errors.

- [ ] **Step 4: Manual verification**

Open the project in Unity, select `Assets/GameAIConstants4ProductionTypes.asset` in the Project window, and confirm the Inspector shows "Colony Ship Data" → `ColonyShip` and "War Ship Data" → `WarShip` populated (not "None"). This confirms the hand-written YAML parsed correctly.

- [ ] **Step 5: Commit**

```bash
git add Assets/Flatspace/GameAI/GameAIConstants.cs Assets/GameAIConstants4ProductionTypes.asset
git commit -m "Add ColonyShip/WarShip template references to GameAIConstants"
```

---

## Task 3: `Planet` docking model

**Files:**
- Modify: `Assets/Flatspace/Objects/Planets/Planet.cs`

**Interfaces:**
- Consumes: `Ship`, `Ship.ShipKind` (Task 1), `GameAIConstants.colonyShipData`/`.warShipData` (Task 2).
- Produces: `Planet.DockedShips` (`List<Ship>`), `Planet.HasDockedShip(Ship.ShipKind)`, `Planet.DockNewShip(Ship.ShipKind)`, `Planet.DockShipFromSave(Ship.ShipKind, int, List<string>)`, `Planet.UndockShip(Ship.ShipKind)` — all consumed by Tasks 4, 5, 12.

- [ ] **Step 1: Remove `HasColonyShip`, add `DockedShips`**

Change (around line 138):
```csharp
    public bool HasColonyShip {get; set;} = false;
```
to:
```csharp
    public List<Ship> DockedShips = new List<Ship>();
```

- [ ] **Step 2: Add docking/undocking methods and the research snapshot helper**

Change `StageCompletedProductionItem` (around line 829) from:
```csharp
    private void StageCompletedProductionItem(List<PlanetUpdateResult> resultList)
    {
        if (CurrentProduction == null) return;
        if (CurrentProduction?.Item.type == "Improvement")
        {
            AddActiveImprovement(CurrentProduction);
        }
        else if (CurrentProduction?.Item.subType == "ColonyShip")
        {
            HasColonyShip = true;
        }
        
        // create game object from CurrentProduction
    }
```
to:
```csharp
    private void StageCompletedProductionItem(List<PlanetUpdateResult> resultList)
    {
        if (CurrentProduction == null) return;
        if (CurrentProduction?.Item.type == "Improvement")
        {
            AddActiveImprovement(CurrentProduction);
        }
        else if (CurrentProduction?.Item.subType == "ColonyShip")
        {
            DockNewShip(Ship.ShipKind.ColonyShip);
        }
        else if (CurrentProduction?.Item.subType == "Warship")
        {
            DockNewShip(Ship.ShipKind.WarShip);
        }
    }

    public bool HasDockedShip(Ship.ShipKind kind)
    {
        return DockedShips.Exists(s => s.Kind == kind);
    }

    private Ship CreateShip(Ship.ShipKind kind, int owner)
    {
        var template = kind == Ship.ShipKind.ColonyShip
            ? _gameAIConstants.colonyShipData
            : _gameAIConstants.warShipData;
        var ship = this.AddComponent<Ship>();
        ship.Kind = kind;
        ship.Owner = owner;
        ship.Template = template;
        return ship;
    }

    public void DockNewShip(Ship.ShipKind kind)
    {
        var ship = CreateShip(kind, Owner);
        ship.ResearchSnapshot = BuildResearchSnapshot(kind);
        DockedShips.Add(ship);
    }

    public void DockShipFromSave(Ship.ShipKind kind, int owner, List<string> researchSnapshot)
    {
        var ship = CreateShip(kind, owner);
        ship.ResearchSnapshot = new List<string>(researchSnapshot);
        DockedShips.Add(ship);
    }

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
        var subType = kind == Ship.ShipKind.ColonyShip ? "ColonyShip" : "Warship";
        var researchCatalog = Gameboard.Instance.players[Owner].playerAI.ResearchCatalog;
        return researchCatalog.catalogItems
            .Where(x => x.researched && x.type == "Ship Improvement" && x.subType == subType)
            .Select(x => x.itemName)
            .ToList();
    }
```

(`Planet.cs` already has `using System.Linq;` at the top, so `.Where`/`.Select`/`.ToList()` need no new using.)

- [ ] **Step 3: Compile-check**

Run `mcp__rider__get_file_problems` on `Assets/Flatspace/Objects/Planets/Planet.cs`, `errorsOnly: true`.
Expected errors at this point: exactly two, both `CS...` "does not contain a definition for 'HasColonyShip'" — one in `Assets/Flatspace/GameAI/PlayerAI.cs`, one in `Assets/Flatspace/GameAI/GameAI.cs`. These are expected and fixed in Task 4. Confirm no *other* errors appear in `Planet.cs` itself.

- [ ] **Step 4: Commit**

```bash
git add Assets/Flatspace/Objects/Planets/Planet.cs
git commit -m "Planet: replace HasColonyShip bool with a DockedShips list"
```

---

## Task 4: Repoint the two `HasColonyShip` call sites

**Files:**
- Modify: `Assets/Flatspace/GameAI/PlayerAI.cs`
- Modify: `Assets/Flatspace/GameAI/GameAI.cs`

**Interfaces:**
- Consumes: `Planet.HasDockedShip`, `Planet.UndockShip` (Task 3).

- [ ] **Step 1: Fix `PlayerAI.PlanetHasColonyShip`**

In `Assets/Flatspace/GameAI/PlayerAI.cs`, change:
```csharp
            private bool PlanetHasColonyShip(string planetName)
            {
                var planet = AIMap.GetPlanet(planetName);
                if (planet != null)
                {
                    return planet.HasColonyShip;
                }
                return false;
            }
```
to:
```csharp
            private bool PlanetHasColonyShip(string planetName)
            {
                var planet = AIMap.GetPlanet(planetName);
                if (planet != null)
                {
                    return planet.HasDockedShip(Ship.ShipKind.ColonyShip);
                }
                return false;
            }
```

`PlayerAI.cs` is in namespace `FlatSpace.AI`, the same namespace `Ship` needs no qualifying `using` for (it's a global-namespace class, same as `Planet`) — this matches how `Planet` itself is referenced unqualified throughout this file already.

- [ ] **Step 2: Fix `GameAI.ExecuteOrder`'s `OrderTypeRemoveShip` case**

In `Assets/Flatspace/GameAI/GameAI.cs`, change:
```csharp
                    case GameAIOrder.OrderType.OrderTypeRemoveShip:
                        targetPlanet.HasColonyShip = false;
                        break;
```
to:
```csharp
                    case GameAIOrder.OrderType.OrderTypeRemoveShip:
                        targetPlanet.UndockShip(Ship.ShipKind.ColonyShip);
                        break;
```

- [ ] **Step 3: Compile-check**

Run `mcp__rider__get_file_problems` on both files, `errorsOnly: true`. Expected: zero errors in either file. Then run `mcp__rider__get_project_problems` with `severity: "Error"` to confirm no `HasColonyShip` errors remain project-wide.

- [ ] **Step 4: Manual verification**

In Play mode, let a colonizer-eligible planet reach the colonize-ready state and colonize a target, exactly as before this change. Confirm colonization still completes (population moves, target becomes owned) — this is the "behaves exactly as it currently does" check from the spec.

- [ ] **Step 5: Commit**

```bash
git add Assets/Flatspace/GameAI/PlayerAI.cs Assets/Flatspace/GameAI/GameAI.cs
git commit -m "Repoint HasColonyShip call sites at the new DockedShips list"
```

---

## Task 5: `OrderTypeShipTransport`

**Files:**
- Modify: `Assets/Flatspace/GameAI/GameAI.cs`

**Interfaces:**
- Consumes: `Planet.DockNewShip`, `Planet.UndockShip` (Task 3).
- Produces: `GameAIOrder.OrderType.OrderTypeShipTransport` — reachable, compiled, but **not called by anything yet** (per spec scope; a later task adds the AI logic that creates these orders).

- [ ] **Step 1: Append the new enum value at the very end of `OrderType`**

Change:
```csharp
                    OrderTypeIndustryTransport,
                    OrderTypeRemoveShip
                }
```
to:
```csharp
                    OrderTypeIndustryTransport,
                    OrderTypeRemoveShip,
                    OrderTypeShipTransport
                }
```

Per the Global Constraints section, this **must** stay the last entry — inserting it anywhere else renumbers every later enum value and corrupts existing save files' `OrderSave.type` ints.

- [ ] **Step 2: Add the `ExecuteOrder` case**

Change:
```csharp
                    case GameAIOrder.OrderType.OrderTypeRemoveShip:
                        targetPlanet.UndockShip(Ship.ShipKind.ColonyShip);
                        break;
                    default:
                        break;
                }
```
to:
```csharp
                    case GameAIOrder.OrderType.OrderTypeRemoveShip:
                        targetPlanet.UndockShip(Ship.ShipKind.ColonyShip);
                        break;
                    case GameAIOrder.OrderType.OrderTypeShipTransport:
                        var shipDelta = Convert.ToInt32(executableOrder.Data);
                        if (shipDelta < 0)
                        {
                            for (var i = 0; i < -shipDelta; i++)
                                targetPlanet.UndockShip(Ship.ShipKind.WarShip);
                        }
                        else
                        {
                            for (var i = 0; i < shipDelta; i++)
                                targetPlanet.DockNewShip(Ship.ShipKind.WarShip);
                        }
                        break;
                    default:
                        break;
                }
```

A negative `Data` (an immediate order, `Target` = the origin planet) undocks `-Data` WarShips right away; a positive `Data` (a delayed order, `Target` = the destination planet, completing after the path-cost delay) docks `Data` new WarShips on arrival — the same signed-amount-at-`Target` shape `OrderTypePopulationChange`/`OrderTypeFoodChange` already use.

- [ ] **Step 3: Compile-check**

Run `mcp__rider__get_file_problems` on `Assets/Flatspace/GameAI/GameAI.cs`, `errorsOnly: true`. Expected: no errors.

- [ ] **Step 4: Commit**

```bash
git add Assets/Flatspace/GameAI/GameAI.cs
git commit -m "Add OrderTypeShipTransport (mechanism only, nothing creates it yet)"
```

---

## Task 6: Expose the real A* path from `GameAIMap`

**Files:**
- Modify: `Assets/Flatspace/GameAI/GameAIMap.cs`

**Interfaces:**
- Consumes: existing private `_planetPathings` list and `Planet.DistanceMapToPathingList`.
- Produces: `public FlatSpace.Pathing.Path GetPath(string origin, string destination)`, consumed by Task 8.

- [ ] **Step 1: Add `GetPath`**

Add this method to `GameAIMap` (e.g. directly below `PlanetAILocation`):

```csharp
            public Path GetPath(string origin, string destination)
            {
                var entry = _planets[origin].DistanceMapToPathingList[destination];
                var path = _planetPathings[entry.PathingIndex].Path1To2;
                if (!entry.PathReversed) return path;

                var reversed = new Path { Cost = path.Cost, NumNodes = path.NumNodes };
                reversed.PathNodes.AddRange(path.PathNodes);
                reversed.PathNodes.Reverse();
                return reversed;
            }
```

`Path` is `FlatSpace.Pathing.Path` — `GameAIMap.cs` already has `using FlatSpace.Pathing;` at the top, so it resolves unqualified. Building a **new** `Path` for the reversed case (rather than reversing `path.PathNodes` in place) is required: `_planetPathings[entry.PathingIndex]` is the single cached `Path` object shared by *both* directions between those two planets — mutating it in place would silently corrupt whichever direction is queried next.

- [ ] **Step 2: Compile-check**

Run `mcp__rider__get_file_problems` on `Assets/Flatspace/GameAI/GameAIMap.cs`, `errorsOnly: true`. Expected: no errors.

- [ ] **Step 3: Commit**

```bash
git add Assets/Flatspace/GameAI/GameAIMap.cs
git commit -m "Expose the computed A* path via GameAIMap.GetPath"
```

---

## Task 7: `LineDrawObject.SetPath`

**Files:**
- Modify: `Assets/Flatspace/UI/LineDrawObject.cs`

**Interfaces:**
- Consumes: nothing new.
- Produces: `public virtual void SetPath(List<Vector3> pathPoints, float progressAmount = 0.0f)`, consumed by Task 8. Existing `SetPoints` is left untouched (still used by `GameBoard.InitPathGraphics`'s static two-point connection-grid lines).

- [ ] **Step 1: Add the `System.Collections.Generic` using**

Change:
```csharp
using System;
using Unity.VisualScripting;
using UnityEngine;
```
to:
```csharp
using System;
using System.Collections.Generic;
using Unity.VisualScripting;
using UnityEngine;
```

- [ ] **Step 2: Add `SetPath`**

Add this method to `LineDrawObject`, alongside `SetPoints`:

```csharp
    public virtual void SetPath(List<Vector3> pathPoints, float progressAmount = 0.0f)
    {
        if (pathPoints == null || pathPoints.Count < 2) return;

        lineRenderer.positionCount = pathPoints.Count;
        lineRenderer.SetPositions(pathPoints.ToArray());

        if (!spriteRenderer) return;

        var totalLength = 0.0f;
        for (var i = 1; i < pathPoints.Count; i++)
            totalLength += Vector3.Distance(pathPoints[i - 1], pathPoints[i]);

        var targetDistance = totalLength * progressAmount;
        var traveled = 0.0f;
        for (var i = 1; i < pathPoints.Count; i++)
        {
            var segmentStart = pathPoints[i - 1];
            var segmentEnd = pathPoints[i];
            var segmentLength = Vector3.Distance(segmentStart, segmentEnd);
            var reachedTarget = traveled + segmentLength >= targetDistance;
            var isLastSegment = i == pathPoints.Count - 1;
            if (reachedTarget || isLastSegment)
            {
                var segmentProgress = segmentLength > 0.0f
                    ? Math.Clamp((targetDistance - traveled) / segmentLength, 0.0f, 1.0f)
                    : 0.0f;
                var position = segmentStart + (segmentEnd - segmentStart) * segmentProgress;
                spriteRenderer.transform.localPosition = position;
                var angle = Vector2.SignedAngle(Vector2.up, segmentEnd - segmentStart);
                spriteRenderer.transform.rotation = Quaternion.AngleAxis(angle, Vector3.forward);
                break;
            }
            traveled += segmentLength;
        }
    }
```

- [ ] **Step 3: Compile-check**

Run `mcp__rider__get_file_problems` on `Assets/Flatspace/UI/LineDrawObject.cs`, `errorsOnly: true`. Expected: no errors.

- [ ] **Step 4: Commit**

```bash
git add Assets/Flatspace/UI/LineDrawObject.cs
git commit -m "LineDrawObject: add SetPath to draw and place a marker along a real polyline"
```

---

## Task 8: Wire real path positions into `GameBoard.DisplayOrderGraphics`

**Files:**
- Modify: `Assets/Flatspace/Objects/Board/GameBoard.cs`

**Interfaces:**
- Consumes: `GameAIMap.GetPath` (Task 6), `LineDrawObject.SetPath` (Task 7), `OrderTypeShipTransport` (Task 5).

- [ ] **Step 1: Add `OrderTypeShipTransport` to the graphic/color lookups**

Change:
```csharp
            private static bool OrderHasGraphic(GameAI.GameAIOrder order)
            {
                var hasGraphicList = new GameAI.GameAIOrder.OrderType[3]
                {
                    GameAI.GameAIOrder.OrderType.OrderTypeFoodTransport, 
                    GameAI.GameAIOrder.OrderType.OrderTypePopulationTransport,
                    GameAI.GameAIOrder.OrderType.OrderTypeGrotsitsTransport,
                };
                
                return Array.Exists(hasGraphicList, t => t == order.Type);

            }
```
to:
```csharp
            private static bool OrderHasGraphic(GameAI.GameAIOrder order)
            {
                var hasGraphicList = new GameAI.GameAIOrder.OrderType[4]
                {
                    GameAI.GameAIOrder.OrderType.OrderTypeFoodTransport, 
                    GameAI.GameAIOrder.OrderType.OrderTypePopulationTransport,
                    GameAI.GameAIOrder.OrderType.OrderTypeGrotsitsTransport,
                    GameAI.GameAIOrder.OrderType.OrderTypeShipTransport,
                };
                
                return Array.Exists(hasGraphicList, t => t == order.Type);

            }
```

Change:
```csharp
                    case GameAI.GameAIOrder.OrderType.OrderTypeGrotsitsTransport:
                        color = new Color32(210, 105, 30, 255);
                        break;
                    default:
```
to:
```csharp
                    case GameAI.GameAIOrder.OrderType.OrderTypeGrotsitsTransport:
                        color = new Color32(210, 105, 30, 255);
                        break;
                    case GameAI.GameAIOrder.OrderType.OrderTypeShipTransport:
                        color = new Color32(255, 0, 255, 255);
                        break;
                    default:
```

- [ ] **Step 2: Replace the straight-line endpoint logic with real path positions**

Change:
```csharp
                foreach (var order in orders)
                {
                    if(!OrderHasGraphic(order))
                        continue;
                    var lineDrawObject = Instantiate<LineDrawObject>(prefab,transform) as LineDrawObject;

                    if (lineDrawObject)
                    {
                        var point1 = _planetUIObjects.Find(x => x._planetName == order.Origin).transform.localPosition;
                        var point2 = _planetUIObjects.Find(x => x._planetName == order.Target).transform.localPosition;
                        float offset = 10.0f;
                        if (order.Type == GameAI.GameAIOrder.OrderType.OrderTypeFoodTransport)
                            offset = -10.0f;
                        else if (order.Type == GameAI.GameAIOrder.OrderType.OrderTypeGrotsitsTransport)
                            offset = 20.0f;
                        var linePoints = (new Vector3(point1.x + offset, point1.y + offset, 0.0f), new Vector3(point2.x + offset, point2.y + offset, 0.0f) );
                        var progressAmount =
                            Math.Clamp(
                                Convert.ToSingle(order.TotalDelay - (order.TimingDelay)) /
                                Convert.ToSingle(order.TotalDelay), 0.15f, 0.85f);
                        lineDrawObject.SetPoints(linePoints,progressAmount );
                        lineDrawObject.SetColor(ColorForOrderType(order.Type));
                    }
                }
```
to:
```csharp
                foreach (var order in orders)
                {
                    if(!OrderHasGraphic(order))
                        continue;
                    var lineDrawObject = Instantiate<LineDrawObject>(prefab,transform) as LineDrawObject;

                    if (lineDrawObject)
                    {
                        float offset = 10.0f;
                        if (order.Type == GameAI.GameAIOrder.OrderType.OrderTypeFoodTransport)
                            offset = -10.0f;
                        else if (order.Type == GameAI.GameAIOrder.OrderType.OrderTypeGrotsitsTransport)
                            offset = 20.0f;
                        else if (order.Type == GameAI.GameAIOrder.OrderType.OrderTypeShipTransport)
                            offset = 30.0f;

                        var path = GameAI.GameAIMap.GetPath(order.Origin, order.Target);
                        var pathPoints = new List<Vector3>();
                        foreach (var node in path.PathNodes)
                            pathPoints.Add(new Vector3(node.Position.x + offset, node.Position.y + offset, 0.0f));

                        var progressAmount =
                            Math.Clamp(
                                Convert.ToSingle(order.TotalDelay - (order.TimingDelay)) /
                                Convert.ToSingle(order.TotalDelay), 0.15f, 0.85f);
                        lineDrawObject.SetPath(pathPoints, progressAmount);
                        lineDrawObject.SetColor(ColorForOrderType(order.Type));
                    }
                }
```

`path.PathNodes[0].Position` and `path.PathNodes[last].Position` are numerically identical to what `_planetUIObjects.Find(...).transform.localPosition` returned before: `Planet.Init` sets `Position = spawnData._planetPosition` (`Planet.cs:199`), `PathingSystem.InitializePathMap` builds every `PathNode.Position` from that same `Planet.Position`, and the planet's `PlanetUIObject` is placed via `uiObject.transform.localPosition += spawnData._planetPosition` (`GameBoard.cs:305`) — all three ultimately come from the same `spawnData._planetPosition`, so switching the endpoints to `PathNode.Position` changes nothing for planets with no intermediate hops, and adds the real intermediate hops for planets with a multi-hop path.

- [ ] **Step 3: Compile-check**

Run `mcp__rider__get_file_problems` on `Assets/Flatspace/Objects/Board/GameBoard.cs`, `errorsOnly: true`. Expected: no errors.

- [ ] **Step 4: Manual verification**

In Play mode, trigger a food or population shipment between two planets that are **not** directly connected (so the path has at least one intermediate hop) and confirm the order line now bends through the intermediate planet(s) instead of cutting a straight diagonal, and that the progress marker sits on that bent line, not floating off it.

- [ ] **Step 5: Commit**

```bash
git add Assets/Flatspace/Objects/Board/GameBoard.cs
git commit -m "DisplayOrderGraphics: draw order lines along the real A* path"
```

---

## Task 9: Gameboard fleet icon (runtime-created, no prefab edits)

**Files:**
- Create: `Assets/Flatspace/UI/FleetIconClickHandler.cs`
- Modify: `Assets/Flatspace/UI/PlanetUIObject.cs`

**Interfaces:**
- Consumes: `Planet.DockedShips` (Task 3).
- Produces: a visible/invisible fleet icon per planet marker; clicking it calls `Gameboard.Instance.ShowFleetUI(planetName)` (implemented in Task 11 — this task compiles against that call but it won't be reachable/functional in-game until Task 11 lands, which is fine since nothing auto-runs this path without a click).

- [ ] **Step 1: Write the click-handler component**

```csharp
using UnityEngine;
using UnityEngine.EventSystems;

public class FleetIconClickHandler : MonoBehaviour, IPointerClickHandler
{
    private PlanetUIObject _owner;

    public void Init(PlanetUIObject owner)
    {
        _owner = owner;
    }

    public void OnPointerClick(PointerEventData eventData)
    {
        Gameboard.Instance.ShowFleetUI(_owner._planetName);
    }
}
```

- [ ] **Step 2: Create the icon at runtime and toggle it every `UIUpdate()`**

In `Assets/Flatspace/UI/PlanetUIObject.cs`, add a field and a creation method, and call the creation method from the existing (currently empty) `Start()`:

```csharp
    private UnityEngine.UI.Image _fleetIconImage;

    private void CreateFleetIcon()
    {
        var iconObject = new GameObject("FleetIcon");
        iconObject.transform.SetParent(_statsCanvas.transform, false);
        var rect = iconObject.AddComponent<RectTransform>();
        rect.sizeDelta = new Vector2(16, 16);
        rect.anchoredPosition = new Vector2(20, 20);
        _fleetIconImage = iconObject.AddComponent<Image>();
        _fleetIconImage.color = new Color32(255, 215, 0, 255); // placeholder gold badge -- swap for real art later
        iconObject.AddComponent<FleetIconClickHandler>().Init(this);
        _fleetIconImage.gameObject.SetActive(false);
    }

    void Start()
    {
        CreateFleetIcon();
    }
```

(`PlanetUIObject.cs` already has `using UnityEngine.UI;`, so `Image` resolves unqualified — the field above is written qualified only to make this diff unambiguous next to `TextMeshProUGUI`/`Canvas` fields already in the file; write it as `private Image _fleetIconImage;` in the actual file.)

Then update the existing `UIUpdate()` method — change:
```csharp
    public void UIUpdate()
    {
        var planet = Gameboard.Instance.GetPlanet(_planetName);
        if (!planet)
            return;
        _populationTextField.text = planet.Population.Count.ToString();
        _foodTextField.text = Math.Floor(planet.Food).ToString();
        _grotsitsTextField.text = Math.Floor(planet.Grotsits).ToString();
        _moraleTextField.text = Math.Floor(planet.Morale).ToString();
        SetOwnerColor(planet.Owner);
    }
```
to:
```csharp
    public void UIUpdate()
    {
        var planet = Gameboard.Instance.GetPlanet(_planetName);
        if (!planet)
            return;
        _populationTextField.text = planet.Population.Count.ToString();
        _foodTextField.text = Math.Floor(planet.Food).ToString();
        _grotsitsTextField.text = Math.Floor(planet.Grotsits).ToString();
        _moraleTextField.text = Math.Floor(planet.Morale).ToString();
        SetOwnerColor(planet.Owner);
        if (_fleetIconImage)
            _fleetIconImage.gameObject.SetActive(planet.DockedShips.Count > 0);
    }
```

- [ ] **Step 3: Compile-check**

Run `mcp__rider__get_file_problems` on both new/modified files, `errorsOnly: true`. Expected: no errors (the `Gameboard.Instance.ShowFleetUI` call in `FleetIconClickHandler` will not exist until Task 11 — if this task is executed strictly in order, Task 11 hasn't run yet, so this **will** show a "does not contain a definition for 'ShowFleetUI'" error. That's expected and matches how Task 3 → Task 4 was sequenced; note it and continue, or reorder to do Task 11's `Gameboard` plumbing first if your executor prefers zero-error intermediate states — either order reaches the same end state).

- [ ] **Step 4: Commit**

```bash
git add Assets/Flatspace/UI/FleetIconClickHandler.cs Assets/Flatspace/UI/PlanetUIObject.cs
git commit -m "Add a runtime-created fleet-presence icon to the gameboard planet marker"
```

---

## Task 10: Planet Detail fleet icon

**Files:**
- Modify: `Assets/Flatspace/UI/MainGameScreenUI/PlanetDetailUI.uxml`
- Modify: `Assets/Flatspace/UI/MainGameScreenUI/PlanetDetailUIController.cs`

**Interfaces:**
- Consumes: `Planet.DockedShips` (Task 3).
- Produces: `PlanetDetailUIController.CurrentPlanetName` (string), `PlanetDetailUIController.ContainsFleetIconScreenPoint(Vector2)` (bool) — both consumed by Task 11.

- [ ] **Step 1: Add the icon element to the uxml**

Change:
```xml
        <ui:VisualElement name="TitleElement" picking-mode="Ignore" style="flex-grow: 1; flex-direction: row; height: 60px; max-height: 60px;">
            <ui:Image name="PlanetIcon" picking-mode="Ignore" style="height: 60px; width: 60px;"/>
            <ui:Label text="Label" name="PlanetName" picking-mode="Ignore" style="height: 60px; width: 499px; -unity-text-align: upper-center;"/>
        </ui:VisualElement>
```
to:
```xml
        <ui:VisualElement name="TitleElement" picking-mode="Ignore" style="flex-grow: 1; flex-direction: row; height: 60px; max-height: 60px;">
            <ui:Image name="PlanetIcon" picking-mode="Ignore" style="height: 60px; width: 60px;"/>
            <ui:Label text="Label" name="PlanetName" picking-mode="Ignore" style="height: 60px; width: 479px; -unity-text-align: upper-center;"/>
            <ui:VisualElement name="FleetIcon" picking-mode="Ignore" style="width: 20px; height: 20px; align-self: center; background-color: rgb(255, 215, 0);"/>
        </ui:VisualElement>
```

(placeholder gold square, same as the gameboard icon — swap both for real art in the same follow-up pass.)

- [ ] **Step 2: Bind and toggle it, and expose the two new members**

In `Assets/Flatspace/UI/MainGameScreenUI/PlanetDetailUIController.cs`, add a field next to `_element`/`_panelElement`:
```csharp
    private VisualElement _fleetIcon;
```

In `Awake()`, change:
```csharp
        _productionProgress = _element.Q<Label>("ProductionProgress");
        enabled = false;
```
to:
```csharp
        _productionProgress = _element.Q<Label>("ProductionProgress");
        _fleetIcon = _element.Q<VisualElement>("FleetIcon");
        enabled = false;
```

In `UpdatePlanetDetail()`, add this line (anywhere in the method body — e.g. right after the `_planetName.text` line in `SetPlanet`, or as the first line of `UpdatePlanetDetail`; add it to `UpdatePlanetDetail` since that already re-runs every turn while the panel is open):
```csharp
        if (_fleetIcon != null)
            _fleetIcon.style.display = _planet.DockedShips.Count > 0 ? DisplayStyle.Flex : DisplayStyle.None;
```

Add these two new public members (anywhere after `ContainsScreenPoint`):
```csharp
    public string CurrentPlanetName => _planet?.PlanetName;

    public bool ContainsFleetIconScreenPoint(Vector2 screenPosition)
    {
        if (_fleetIcon == null || _fleetIcon.panel == null) return false;
        if (_fleetIcon.style.display == DisplayStyle.None) return false;
        var panelPosition = RuntimePanelUtils.ScreenToPanel(_fleetIcon.panel, screenPosition);
        return _fleetIcon.worldBound.Contains(panelPosition);
    }
```

- [ ] **Step 3: Compile-check**

Run `mcp__rider__get_file_problems` on `Assets/Flatspace/UI/MainGameScreenUI/PlanetDetailUIController.cs`, `errorsOnly: true`. Expected: no errors.

- [ ] **Step 4: Commit**

```bash
git add Assets/Flatspace/UI/MainGameScreenUI/PlanetDetailUI.uxml Assets/Flatspace/UI/MainGameScreenUI/PlanetDetailUIController.cs
git commit -m "Add fleet-presence icon to Planet Detail"
```

---

## Task 11: Fleet UI panel + `Gameboard` show/hide/click-routing plumbing

**Files:**
- Create: `Assets/Flatspace/UI/MainGameScreenUI/FleetUI.uxml`
- Create: `Assets/Flatspace/UI/MainGameScreenUI/FleetUIController.cs`
- Modify: `Assets/Flatspace/Objects/Board/GameBoard.cs`
- Modify: `Assets/Flatspace/UI/MainGameScreenUI/GameButtonHandler.cs`
- Manual Unity Editor step: add the new `UIDocument` GameObject to the `Flatspace` scene.

**Interfaces:**
- Consumes: `Planet.DockedShips`/`Ship` (Task 3), `PlanetDetailUIController.CurrentPlanetName`/`.ContainsFleetIconScreenPoint` (Task 10), `FleetIconClickHandler`'s call to `Gameboard.Instance.ShowFleetUI` (Task 9).
- Produces: `Gameboard.ShowFleetUI(string)`, `Gameboard.HideFleetUI()`, `Gameboard.FleetUIShowing()` — the last new public surface this feature adds.

- [ ] **Step 1: Write `FleetUI.uxml`**

```xml
<ui:UXML xmlns:ui="UnityEngine.UIElements" xmlns:uie="UnityEditor.UIElements" editor-extension-mode="False">
    <ui:VisualElement name="FleetUIElement" picking-mode="Ignore" enabled="true" style="flex-grow: initial; flex-direction: column; background-color: rgb(0, 0, 0); color: rgb(255, 255, 255); -unity-text-outline-color: rgb(0, 0, 0); border-left-color: rgb(255, 0, 0); border-right-color: rgb(255, 0, 0); border-top-color: rgb(255, 0, 0); border-bottom-color: rgb(255, 0, 0); border-top-width: 2px; border-right-width: 2px; border-bottom-width: 2px; border-left-width: 2px; border-top-left-radius: 2px; border-top-right-radius: 2px; border-bottom-right-radius: 2px; border-bottom-left-radius: 2px; top: 25%; left: 30%; align-items: stretch; align-self: center; width: 400px; height: 300px;">
        <ui:Label text="Fleet" name="FleetTitle" picking-mode="Ignore" style="height: 30px; -unity-text-align: upper-center; font-size: 18px;"/>
        <ui:ScrollView name="ShipListScrollView" picking-mode="Ignore" style="flex-grow: 1;">
            <ui:VisualElement name="ShipListContainer" picking-mode="Ignore" style="flex-grow: 1;"/>
        </ui:ScrollView>
    </ui:VisualElement>
</ui:UXML>
```

- [ ] **Step 2: Write `FleetUIController.cs`**

```csharp
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

public class FleetUIController : MonoBehaviour
{
    public UIDocument uiDocument;

    private VisualElement _element;
    private VisualElement _panelElement;
    private VisualElement _shipListContainer;
    private Planet _planet;

    private void OnEnable()
    {
        if (_element == null) return;
        _element.SetEnabled(true);
        _element.visible = true;
        _element.pickingMode = PickingMode.Ignore;
    }

    private void OnDisable()
    {
        if (_element == null) return;
        _element.SetEnabled(false);
        _element.visible = false;
        _element.pickingMode = PickingMode.Ignore;
    }

    public void Awake()
    {
        _element = uiDocument.rootVisualElement;
        _panelElement = _element.Q<VisualElement>("FleetUIElement");
        _shipListContainer = _element.Q<VisualElement>("ShipListContainer");
        enabled = false;
    }

    public void SetPlanet(Planet planet)
    {
        _planet = planet;
        RefreshShipList();
    }

    public void RefreshShipList()
    {
        if (_shipListContainer == null) return;
        _shipListContainer.Clear();
        if (_planet == null) return;

        foreach (var ship in _planet.DockedShips)
        {
            var row = new Label(
                $"{ship.Kind} - {ship.Template?.shipName} " +
                $"(Spd {ship.Template?.shipSpeed:0.#}, " +
                $"Off {ship.Template?.shipOffense:0.#}, " +
                $"Def {ship.Template?.shipDefense:0.#})")
            {
                pickingMode = PickingMode.Ignore
            };
            _shipListContainer.Add(row);
        }
    }

    // Same bounds-test pattern as PlanetDetailUIController.ContainsScreenPoint.
    public bool ContainsScreenPoint(Vector2 screenPosition)
    {
        if (_panelElement == null || _panelElement.panel == null) return false;
        var panelPosition = RuntimePanelUtils.ScreenToPanel(_panelElement.panel, screenPosition);
        return _panelElement.worldBound.Contains(panelPosition);
    }
}
```

- [ ] **Step 3: Add `Gameboard` show/hide/showing methods, make Planet Detail and Fleet UI mutually exclusive, and route icon/outside clicks**

Add a field next to `_planetDetailUIController`:
```csharp
            private FleetUIController _fleetUIController;
```

In `Awake()`, change:
```csharp
                _mainScreenUIController = GetComponentInChildren<MainScreenUIController>();
                _planetDetailUIController = GetComponentInChildren<PlanetDetailUIController>();
```
to:
```csharp
                _mainScreenUIController = GetComponentInChildren<MainScreenUIController>();
                _planetDetailUIController = GetComponentInChildren<PlanetDetailUIController>();
                _fleetUIController = GetComponentInChildren<FleetUIController>();
```

Change:
```csharp
            public void ShowPlanetDetail(string planetName)
            {
                _planetDetailUIController.SetPlanet(GetPlanet(planetName));
                _planetDetailUIController.enabled = true;
            }

            public void HidePlanetDetail()
            {
                _planetDetailUIController.enabled = false;
            }

            public bool PlanetDetailShowing()
            {
                return _planetDetailUIController.enabled;
            }
```
to:
```csharp
            public void ShowPlanetDetail(string planetName)
            {
                HideFleetUI();
                _planetDetailUIController.SetPlanet(GetPlanet(planetName));
                _planetDetailUIController.enabled = true;
            }

            public void HidePlanetDetail()
            {
                _planetDetailUIController.enabled = false;
            }

            public bool PlanetDetailShowing()
            {
                return _planetDetailUIController.enabled;
            }

            public void ShowFleetUI(string planetName)
            {
                HidePlanetDetail();
                _fleetUIController.SetPlanet(GetPlanet(planetName));
                _fleetUIController.enabled = true;
            }

            public void HideFleetUI()
            {
                _fleetUIController.enabled = false;
            }

            public bool FleetUIShowing()
            {
                return _fleetUIController.enabled;
            }
```

Change the `Update()` method (and leave `IsPointerOverPlanet` as-is below it) from:
```csharp
            void Update()
            {
                if (PlanetDetailShowing() && Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame)
                {
                    var mousePosition = Mouse.current.position.ReadValue();
                    // A click on a planet is handled by PlanetUIObject.OnPointerClick (which shows
                    // that planet's detail), so only close here for a click that lands neither on
                    // the detail panel nor on a planet -- same as pressing Escape.
                    if (!_planetDetailUIController.ContainsScreenPoint(mousePosition) && !IsPointerOverPlanet(mousePosition))
                        HidePlanetDetail();
                }
            }
```
to:
```csharp
            void Update()
            {
                if (Mouse.current == null || !Mouse.current.leftButton.wasPressedThisFrame)
                    return;

                var mousePosition = Mouse.current.position.ReadValue();

                if (PlanetDetailShowing() && _planetDetailUIController.ContainsFleetIconScreenPoint(mousePosition))
                {
                    ShowFleetUI(_planetDetailUIController.CurrentPlanetName);
                    return;
                }

                if (FleetUIShowing())
                {
                    if (!_fleetUIController.ContainsScreenPoint(mousePosition))
                        HideFleetUI();
                    return;
                }

                // A click on a planet is handled by PlanetUIObject.OnPointerClick (which shows
                // that planet's detail), so only close here for a click that lands neither on
                // the detail panel nor on a planet -- same as pressing Escape.
                if (PlanetDetailShowing()
                    && !_planetDetailUIController.ContainsScreenPoint(mousePosition)
                    && !IsPointerOverPlanet(mousePosition))
                {
                    HidePlanetDetail();
                }
            }
```

- [ ] **Step 4: Make Escape close the Fleet UI too**

In `Assets/Flatspace/UI/MainGameScreenUI/GameButtonHandler.cs`, change:
```csharp
        public void EscapeButtonPressed()
        {
            if (Gameboard.Instance.PlanetDetailShowing())
            {
                Gameboard.Instance.HidePlanetDetail();
                return;
            }

            _saveButton.visible = !_saveButton.visible;
            _loadButton.visible = !_loadButton.visible;
        }
```
to:
```csharp
        public void EscapeButtonPressed()
        {
            if (Gameboard.Instance.FleetUIShowing())
            {
                Gameboard.Instance.HideFleetUI();
                return;
            }

            if (Gameboard.Instance.PlanetDetailShowing())
            {
                Gameboard.Instance.HidePlanetDetail();
                return;
            }

            _saveButton.visible = !_saveButton.visible;
            _loadButton.visible = !_loadButton.visible;
        }
```

- [ ] **Step 5: Manual Unity Editor step — add the Fleet UI GameObject to the scene**

In the Unity Editor, open the `Flatspace` scene. Under the `Board` GameObject (the same parent that already holds `PlanetDetailUIDocument` and `MainScreenUIDocument`), create a new child GameObject named `FleetUIDocument`:
1. Add a `UI Document` component to it.
2. Set its **Panel Settings** to the same asset already referenced by `PlanetDetailUIDocument`'s `UI Document` (open that sibling object to see which asset it is — GUID `1076588f40c3bd844bad78a2946dbc9d`).
3. Set its **Source Asset** to `Assets/Flatspace/UI/MainGameScreenUI/FleetUI.uxml` (created in Step 1 of this task).
4. Set **Sorting Order** to `3` (one above `PlanetDetailUIDocument`'s `2`, so the Fleet UI draws on top when both would otherwise overlap).
5. Add the `FleetUIController` component to the same GameObject, and drag its own `UI Document` component into the `Fleet UI Controller`'s `Ui Document` field.
6. Save the scene.

- [ ] **Step 6: Compile-check**

Run `mcp__rider__get_file_problems` on `Assets/Flatspace/Objects/Board/GameBoard.cs`, `Assets/Flatspace/UI/MainGameScreenUI/FleetUIController.cs`, and `Assets/Flatspace/UI/MainGameScreenUI/GameButtonHandler.cs`, `errorsOnly: true`. Expected: no errors. Then run `mcp__rider__get_project_problems` with `severity: "Error"` — expected: zero project-wide, confirming Task 9's `FleetIconClickHandler` call to `ShowFleetUI` now resolves.

- [ ] **Step 7: Manual verification**

In Play mode: build a ColonyShip or WarShip on a planet, confirm the gameboard icon and Planet Detail icon both appear once production completes. Click the gameboard icon — confirm the Fleet UI opens and lists the ship with its stats (all zero, since the placeholder `ShipData` assets are still unfilled — that's expected). Click elsewhere — confirm it closes. Open Planet Detail, click its fleet icon — confirm the Fleet UI opens (and Planet Detail closes). Press Escape while the Fleet UI is open — confirm it closes.

- [ ] **Step 8: Commit**

```bash
git add Assets/Flatspace/UI/MainGameScreenUI/FleetUI.uxml Assets/Flatspace/UI/MainGameScreenUI/FleetUIController.cs Assets/Flatspace/Objects/Board/GameBoard.cs Assets/Flatspace/UI/MainGameScreenUI/GameButtonHandler.cs Assets/Scenes/Flatspace.unity
git commit -m "Add Fleet UI panel and wire icon/outside-click routing through Gameboard"
```

---

## Task 12: Save/load docked ships

**Files:**
- Modify: `Assets/Flatspace/SaveSystem/SaveLoadSystem.cs`
- Modify: `Assets/Flatspace/GameAI/GameAIMap.cs`

**Interfaces:**
- Consumes: `Ship`, `Ship.ShipKind` (Task 1), `Planet.DockedShips`/`.DockShipFromSave` (Task 3).
- Produces: `GameSave.PlanetSave.dockedShips` (`List<GameSave.ShipSave>`).

- [ ] **Step 1: Add the `ShipSave` struct and the `dockedShips` field**

In `Assets/Flatspace/SaveSystem/SaveLoadSystem.cs`, add a new nested struct next to `ProductionSave` (inside `GameSave`):
```csharp
        [Serializable]
        public struct ShipSave
        {
            public Ship.ShipKind kind;
            public int owner;
            public List<string> researchSnapshot;
        }
```

Change `PlanetSave` from:
```csharp
        [Serializable]
        public struct PlanetSave
        {
            public string name;
            public Planet.PlanetType planetType;
            public Planet.PlanetStrategy planetStrategy;
            public float food;
            public int[] population;
            public float grotsits;
            public float research;
            public float industry;
            public GameSave.ProductionSave? currentProduction;
            [NotNull] public List<GameSave.ProductionSave> productionQueue;
            public float morale;
            public int owner;
            public List<int> populationTransferInProgress;
            public bool foodTransferInProgress;
            public bool grotsitsTransferInProgress;
        }
```
to:
```csharp
        [Serializable]
        public struct PlanetSave
        {
            public string name;
            public Planet.PlanetType planetType;
            public Planet.PlanetStrategy planetStrategy;
            public float food;
            public int[] population;
            public float grotsits;
            public float research;
            public float industry;
            public GameSave.ProductionSave? currentProduction;
            [NotNull] public List<GameSave.ProductionSave> productionQueue;
            public float morale;
            public int owner;
            public List<int> populationTransferInProgress;
            public bool foodTransferInProgress;
            public bool grotsitsTransferInProgress;
            public List<GameSave.ShipSave> dockedShips;
        }
```

- [ ] **Step 2: Populate it when a save is created**

In the `GameSave(GameAI gameAI)` constructor, change:
```csharp
                var planetSave = new PlanetSave
                {         
                    name = planet.PlanetName,
                    planetType = planet.Type,
                    planetStrategy = planet.CurrentStrategy,
                    food = planet.Food,
                    grotsits = planet.Grotsits,
                    research = planet.Research,
                    industry = planet.Industry,
                    currentProduction = planet.CurrentProduction == null? null : new GameSave.ProductionSave(planet.CurrentProduction),
                    productionQueue = new List<GameSave.ProductionSave>(),
                    morale = planet.Morale,
                    owner = planet.Owner,
                    populationTransferInProgress = planet.IncomingPopulationSource,
                    foodTransferInProgress = planet.FoodShipmentIncoming,
                    grotsitsTransferInProgress = planet.GrotsitsShipmentIncoming,
                    population = new int[Gameboard.Instance.players.Count]
                };
```
to:
```csharp
                var planetSave = new PlanetSave
                {         
                    name = planet.PlanetName,
                    planetType = planet.Type,
                    planetStrategy = planet.CurrentStrategy,
                    food = planet.Food,
                    grotsits = planet.Grotsits,
                    research = planet.Research,
                    industry = planet.Industry,
                    currentProduction = planet.CurrentProduction == null? null : new GameSave.ProductionSave(planet.CurrentProduction),
                    productionQueue = new List<GameSave.ProductionSave>(),
                    morale = planet.Morale,
                    owner = planet.Owner,
                    populationTransferInProgress = planet.IncomingPopulationSource,
                    foodTransferInProgress = planet.FoodShipmentIncoming,
                    grotsitsTransferInProgress = planet.GrotsitsShipmentIncoming,
                    population = new int[Gameboard.Instance.players.Count],
                    dockedShips = new List<GameSave.ShipSave>()
                };

                foreach (var ship in planet.DockedShips)
                {
                    planetSave.dockedShips.Add(new GameSave.ShipSave
                    {
                        kind = ship.Kind,
                        owner = ship.Owner,
                        researchSnapshot = new List<string>(ship.ResearchSnapshot)
                    });
                }
```

- [ ] **Step 3: Restore docked ships when a save is loaded**

In `Assets/Flatspace/GameAI/GameAIMap.cs`, in `SetPlanetSimulationStats`, change:
```csharp
                    for (var i = 0; i < planetStatus.population.Length; i++)
                    {
                        if (planetStatus.population[i] <= 0)
                            continue;
                        for (var j = 0; j < planetStatus.population[i]; j++)
                            planet.Population.Add(new Planet.Inhabitant { Player = i });
                    }
                }
```
to:
```csharp
                    for (var i = 0; i < planetStatus.population.Length; i++)
                    {
                        if (planetStatus.population[i] <= 0)
                            continue;
                        for (var j = 0; j < planetStatus.population[i]; j++)
                            planet.Population.Add(new Planet.Inhabitant { Player = i });
                    }

                    foreach (var shipSave in planetStatus.dockedShips)
                    {
                        planet.DockShipFromSave(shipSave.kind, shipSave.owner, shipSave.researchSnapshot);
                    }
                }
```

`DockShipFromSave` restores the exact saved `researchSnapshot` rather than recomputing it from the current (post-load) research state — the snapshot is a point-in-time record with no other source of truth, per the design spec.

- [ ] **Step 4: Compile-check**

Run `mcp__rider__get_file_problems` on `Assets/Flatspace/SaveSystem/SaveLoadSystem.cs` and `Assets/Flatspace/GameAI/GameAIMap.cs`, `errorsOnly: true`. Expected: no errors.

- [ ] **Step 5: Manual verification**

In Play mode: build a WarShip, save the game, load it back, and confirm the gameboard/Planet Detail fleet icons are still showing for that planet after load (open the Fleet UI to confirm the ship and its (empty, since nothing is researched yet) research snapshot round-tripped).

- [ ] **Step 6: Commit**

```bash
git add Assets/Flatspace/SaveSystem/SaveLoadSystem.cs Assets/Flatspace/GameAI/GameAIMap.cs
git commit -m "Save/load docked ships"
```

---

## Task 13: Final project-wide verification pass

**Files:** none (verification only).

- [ ] **Step 1: Full project compile check**

Run `mcp__rider__get_project_problems` with `severity: "Error"`. Expected: `{"problems": [], "totalCount": 0}`.

- [ ] **Step 2: Grep for any missed `HasColonyShip` reference**

Run (Bash): `grep -rn "HasColonyShip" Assets/ --include=*.cs`
Expected: no matches (confirms Task 4 fully removed the old bool from every call site, not just the two found during planning).

- [ ] **Step 3: End-to-end manual verification checklist in Play mode**

- [ ] Build a ColonyShip; confirm colonization still proceeds exactly as before (Task 4's check, re-confirmed after all later tasks).
- [ ] Build a WarShip; confirm both fleet icons appear and the Fleet UI lists it.
- [ ] Trigger a multi-hop food/population shipment; confirm the order line bends through intermediate planets and the marker tracks it (Task 8's check, re-confirmed).
- [ ] Save and reload; confirm docked ships and their (currently-empty) research snapshots survive the round trip (Task 12's check, re-confirmed).
- [ ] Confirm `OrderTypeShipTransport` is not being created anywhere during normal play (expected — no AI logic creates it yet; this is intentional per the design spec's scope).
