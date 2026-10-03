# Colonist Redirect Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** A colonist already in flight whose remaining route crosses a blockade its owner knows about is detoured to the same target, else diverted to another target, instead of being cut; a colonist that arrives at a full planet docks as a colony ship.

**Architecture:** A pure planner (`ColonistRedirect`) decides per order (detour, divert or none) from the owner's `BlockadeView`, reusing `RoutePlanner.PlanRoute`. `GameAI.RedirectColonists()` runs it every turn right after `ApplyBlockades` and applies the result to the colonist order, its food-rider twin and the transfer flags. A new `GameAI.ApplyColonistArrival` adds the colonist or docks a ship. No save-format change, no new tunable.

**Tech Stack:** Unity 6000.4.1f1 C#, `JsonUtility` saves (untouched), Editor self-check suites under `Assets/Editor/`. Unity cannot be run from Claude Code: the user's Editor run of `FlatSpace -> AI -> Run All AI Self-Checks` is the real check (see Global Constraints).

**Spec:** `docs/superpowers/specs/2026-10-02-colonist-redirect-design.md` (read it first; this plan implements it). Working from `FUTURE_FEATURES.md` entry (5).

## Global Constraints

- Namespace `FlatSpace.AI` for runtime GameAI code; Editor self-checks are in the global namespace with `using FlatSpace.AI;` like `BlockadeAvoidanceSelfCheck.cs`.
- A method a self-check calls directly must be `public`, never `internal` (`Assets/Editor/` is a separate assembly).
- Self-checks must never depend on `Gameboard.Instance`. `Planet.DockShipRebuiltSnapshot` needs it, so the arrival helper takes a dock delegate (Task 2).
- Every test planet needs a distinct position (A* tie-break fragility in `PathingSystem.FindPath`); never put an assertion exactly on a float boundary.
- A new script's `.meta` must be committed with it (`git status` after adding; write the `.meta` with `fileFormatVersion: 2`, a new GUID and a `MonoImporter` block as the previous sub-project did, or let Unity generate it and commit it).
- `OrderType` is serialized as an int: this plan adds no new order type.
- Redirect decisions use only the owner's `PlayerAI.CurrentBlockadeView`, never live blockade values. A null view means "nothing is blockaded", so no redirect.
- Redirected delays are clamped to at least 1 turn: `Math.Max(1, Convert.ToInt32(route.Cost / defaultTravelSpeed))` (a `Delayed` order with `TimingDelay <= 0` is both queued and executed by `ProcessNewOrders`).
- Verification per task: Rider's `get_file_problems` with `rootFolder: "C:/Projects/FlatSpace"` (cannot analyse brand-new files; an empty list is not proof of a compile). The real checks are the user's Editor runs (after Task 3 and after Task 5). Commit only the files a task lists. End commit messages with `Co-Authored-By: Claude Sonnet 5.5 <noreply@anthropic.com>`.
- Do not change ordinary launch rules (`IsValidColonizationTarget`, `ProcessColonizers`).

## Review Focus

- A colonist whose carried route is `null` (older save, route fewer than 2 nodes): `RouteFor` falls back to the shortest path from `Origin`; the planner must not throw and must treat `Origin` as the current node. Pinned in Task 1.
- Two colonists from the same origin to the same target with riders: redirecting one must not retime the other's rider. Pinned in Task 2 (rider matched on player, origin, old target and equal `TimingDelay`).
- Target already blockaded and unseen candidates only: no candidate, no change, the order is still cut later by `BlockadeSystem` as today. Pinned in Task 1.
- A colonist on the turn of arrival (`TimingDelay <= 0`) or with no node ahead: never redirected. Pinned in Task 1/3.
- Arrival at a planet that holds another player's population, below max: the colonist still adds (contested planets are desired); only `Population.Count >= MaxPopulation` docks. Pinned in Task 2.

---

## File Structure

| File | Responsibility |
|---|---|
| `Assets/Flatspace/GameAI/ColonistRedirect.cs` (new) | Pure planner (`Plan`) and order mutation (`Apply`). No `Gameboard.Instance`. |
| `Assets/Flatspace/GameAI/BlockadeSystem.cs` | `CurrentNode(order)` and `NodesAhead(order)` helpers beside `RouteFor`. |
| `Assets/Flatspace/GameAI/PlayerAI.cs` | `IsDiversionTarget` (public); `ColonizationCostDivisor` made public. |
| `Assets/Flatspace/GameAI/GameAI.cs` | `RedirectColonists()`, `ApplyColonistArrival`, failed-log set, ExecuteOrder arrival wiring. |
| `Assets/Flatspace/Diagnostics/AITuningLogger.cs` | Three log lines. |
| `Assets/Editor/ColonistRedirectSelfCheck.cs` (new) | This feature's suite. |
| `Assets/Editor/AllAISelfChecks.cs` | Register the suite (10 suites). |
| `Assets/Editor/BlockadeAvoidanceSelfCheck.cs` | Make `Scenario`, `Spawn`, `Build` reachable (Task 1). |
| `CLAUDE.md`, `FUTURE_FEATURES.md`, `.claude/skills/tuning-log/SKILL.md` | Docs (Task 5). |

**Spec notes (corrections found while planning):** the transfer-in-progress flags are saved on the planet (`PlanetSave.populationTransferInProgress`), not derived from orders; this changes nothing here. The `<blocked planets>` log field reuses `PlayerAI.BlockedNodeSummary` (`name=value` joined by `,`, `-` when none), the same format as `ShipmentCancelled`, instead of bare names.

---

### Task 1: Current-node helpers and the pure planner

**Files:**
- Modify: `Assets/Flatspace/GameAI/BlockadeSystem.cs` (after `PassedNodes`, around line 160)
- Create: `Assets/Flatspace/GameAI/ColonistRedirect.cs` (+ `.meta`)
- Create: `Assets/Editor/ColonistRedirectSelfCheck.cs` (+ `.meta`)
- Modify: `Assets/Editor/BlockadeAvoidanceSelfCheck.cs` (visibility only)

**Interfaces:**
- Produces (`BlockadeSystem`): `public string CurrentNode(GameAI.GameAIOrder order)` (last route node whose fraction is `<= progress`; the route's first node, or `order.Origin` when it has no carried route, when none passed); `public List<string> NodesAhead(GameAI.GameAIOrder order)` (names of route nodes with fraction `> progress`, in order).
- Produces (`ColonistRedirect`):
  ```csharp
  public enum RedirectKind { None, Detour, Divert }
  public class Result { public RedirectKind Kind; public string CurrentNode; public string Target;
                        public List<string> Nodes; public float Cost; public List<string> BlockedAhead; }
  public static Result Plan(GameAIMap map, BlockadeSystem blockade, GameAI.GameAIOrder order, BlockadeView view,
                            int maxNodes, System.Func<Planet, bool> isCandidate, System.Func<string, float> costDivisor)
  ```
  `Result.Cost` is the REAL route cost (never tilted); `BlockedAhead` lists the order's remaining nodes the view says are blockaded (empty when `Kind` is `None` because nothing is blocked).
- Consumes: `RoutePlanner.PlanRoute(GameAIMap, string, string, BlockadeView, int)` returning `PlannedRoute { Nodes, Cost }` or null; `BlockadeSystem.RouteFor/Progress`.

- [ ] **Step 1: Make the shared self-check scaffolding reachable**

In `Assets/Editor/BlockadeAvoidanceSelfCheck.cs` change `private static PlanetSpawnData Spawn(` and `private static GameAIMap Build(` to `public static`, and make the nested `Scenario` class and its `Diamond()`/`Blockade`/`Unblockade`/`Colonize` members `public` if they are not (check the declaration around line 500; `Hub()` and `Long()` are already `public static`). Do not change behavior.

- [ ] **Step 2: Write the failing test**

Create `Assets/Editor/ColonistRedirectSelfCheck.cs`:

```csharp
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using FlatSpace.AI;

public static class ColonistRedirectSelfCheck
{
    [MenuItem("FlatSpace/AI/Run Colonist Redirect Self-Check")]
    public static void Run() => RunChecks();

    public static bool RunChecks()
    {
        var ok = RunPlannerCheck();
        Debug.Log(ok
            ? "[ColonistRedirectSelfCheck] ALL PASSED"
            : "[ColonistRedirectSelfCheck] FAILURES (see errors above)");
        return ok;
    }

    private static bool Check(bool condition, string label)
    {
        if (!condition) Debug.LogError($"[ColonistRedirectSelfCheck] FAIL: {label}");
        return condition;
    }

    // A colonist from A to D along A>B>D (the diamond: B route ~224, C route ~361), delays 10 turns, `elapsed` turns flown.
    private static GameAI.GameAIOrder Colonist(int elapsed, string target = "D", List<string> route = null)
        => new GameAI.GameAIOrder
        {
            Type = GameAI.GameAIOrder.OrderType.OrderTypePopulationTransport,
            TimingType = GameAI.GameAIOrder.OrderTimingType.OrderTimingTypeDelayed,
            TotalDelay = 10, TimingDelay = 10 - elapsed, Data = 1, Origin = "A", Target = target, PlayerId = 0,
            Route = route ?? new List<string> { "A", "B", "D" },
        };

    private static ColonistRedirect.Result Plan(BlockadeAvoidanceSelfCheck.Scenario s, GameAI.GameAIOrder order,
        BlockadeView view, System.Func<Planet, bool> candidate = null)
        => ColonistRedirect.Plan(s.Map, new BlockadeSystem(s.Map, s.Research), order, view, 6,
            candidate ?? (p => false), name => 1f);

    public static bool RunPlannerCheck()
    {
        var ok = true;
        using (var s = BlockadeAvoidanceSelfCheck.Scenario.Diamond())
        {
            var bs = new BlockadeSystem(s.Map, s.Research);

            // Current node: B sits at fraction ~0.5 of A>B>D (A-B ~112, B-D ~112).
            ok &= Check(bs.CurrentNode(Colonist(0)) == "A", "just launched: the current node is the origin");
            ok &= Check(bs.CurrentNode(Colonist(3)) == "A", "30% flown: still on the origin (B is at ~50%)");
            ok &= Check(bs.CurrentNode(Colonist(6)) == "B", "60% flown: B was the last node passed");
            ok &= Check(string.Join(">", bs.NodesAhead(Colonist(6))) == "D", "60% flown: only D is ahead");
            ok &= Check(string.Join(">", bs.NodesAhead(Colonist(0))) == "B>D", "just launched: B and D are ahead");
            var noRoute = Colonist(6);
            noRoute.Route = null;   // an older save: no carried route, the shortest path from Origin is used
            ok &= Check(bs.CurrentNode(noRoute) == "B", "no carried route: falls back to the shortest path from the origin");

            // Clean: nothing blocked ahead leaves the order alone.
            var clean = Plan(s, Colonist(0), BlockadeView.Of());
            ok &= Check(clean.Kind == ColonistRedirect.RedirectKind.None && clean.BlockedAhead.Count == 0,
                "no blockade ahead: no redirect");

            // A blockade BEHIND the colonist is not ahead of it.
            var behind = Plan(s, Colonist(6), BlockadeView.Of("A"));
            ok &= Check(behind.Kind == ColonistRedirect.RedirectKind.None, "a blockade behind the colonist is ignored");

            // B blockaded and the colonist still at A: detour A>C>D, same target.
            var detour = Plan(s, Colonist(0), BlockadeView.Of("B"));
            ok &= Check(detour.Kind == ColonistRedirect.RedirectKind.Detour && detour.Target == "D"
                        && string.Join(">", detour.Nodes) == "A>C>D" && detour.CurrentNode == "A"
                        && detour.BlockedAhead.Count == 1 && detour.BlockedAhead[0] == "B",
                "B ahead is blockaded: detour A>C>D to the same target");
            ok &= Check(detour.Cost > 300f, "the detour's cost is the real (longer) route cost");

            // Target blockaded: no detour exists; with no candidate nothing changes.
            var none = Plan(s, Colonist(0), BlockadeView.Of("D"));
            ok &= Check(none.Kind == ColonistRedirect.RedirectKind.None && none.BlockedAhead.Contains("D"),
                "blockaded target and no candidate: no change, the blocked planet is reported");

            // Target blockaded: divert to the cheapest valid candidate. C is a candidate; B is too but nearer.
            var divert = Plan(s, Colonist(0), BlockadeView.Of("D"), p => p.PlanetName == "B" || p.PlanetName == "C");
            ok &= Check(divert.Kind == ColonistRedirect.RedirectKind.Divert && divert.Target == "B"
                        && string.Join(">", divert.Nodes) == "A>B",
                "blockaded target: divert to the cheapest candidate (B, nearer than C)");

            // The current target is never its own diversion, and a candidate with no clean route is skipped.
            var self = Plan(s, Colonist(0), BlockadeView.Of("D"), p => p.PlanetName == "D");
            ok &= Check(self.Kind == ColonistRedirect.RedirectKind.None, "the current target is not a diversion candidate");
            var skipped = Plan(s, Colonist(0), BlockadeView.Of("D", "B", "C"), p => p.PlanetName == "B" || p.PlanetName == "C");
            ok &= Check(skipped.Kind == ColonistRedirect.RedirectKind.None, "blockaded candidates are not chosen");

            // The cost divisor tilts the choice but the reported cost stays real.
            var tilted = ColonistRedirect.Plan(s.Map, bs, Colonist(0), BlockadeView.Of("D"), 6,
                p => p.PlanetName == "B" || p.PlanetName == "C", name => name == "C" ? 10f : 1f);
            ok &= Check(tilted.Kind == ColonistRedirect.RedirectKind.Divert && tilted.Target == "C"
                        && tilted.Cost > 300f,
                "a large divisor on C makes it win; Result.Cost is still C's real route cost");

            // Arrival turn: never redirected.
            var arriving = Colonist(10);
            ok &= Check(Plan(s, arriving, BlockadeView.Of("D")).Kind == ColonistRedirect.RedirectKind.None,
                "an order with no delay left is never redirected");

            // Null view: nothing is blockaded, so nothing to redirect.
            ok &= Check(Plan(s, Colonist(0), null).Kind == ColonistRedirect.RedirectKind.None, "a null view never redirects");
        }
        return ok;
    }
}
```

Note `Scenario` implements `IDisposable` (it has `Dispose`); if it does not declare `: System.IDisposable`, add it in Step 1. The diamond node positions come from `BlockadeAvoidanceSelfCheck.BuildDiamond` (A(0,0), B(100,50), C(100,-150), D(200,0)); with `defaultTravelSpeed = 1` the B route costs about 224 and the C route about 361, so the 10-turn fixtures above put B at about 50% progress. If `Scenario.Diamond()` leaves `defaultTravelSpeed` at another value that only changes delays, not fractions, so the `CurrentNode` assertions hold regardless.

- [ ] **Step 3: Run it to verify it fails**

The Editor cannot be run from here: confirm via Rider's `get_file_problems` on `ColonistRedirectSelfCheck.cs` that it reports unresolved `ColonistRedirect` / `CurrentNode` / `NodesAhead`. Expected: those unresolved-symbol errors.

- [ ] **Step 4: Implement the `BlockadeSystem` helpers**

In `BlockadeSystem.cs`, after `PassedNodes`:

```csharp
        /// <summary>
        /// The last route node an order has passed (fraction at or below its progress), or the route's first node
        /// (the order's Origin when it carries no route) when none has been. A colonist is treated as standing on it.
        /// </summary>
        public string CurrentNode(GameAI.GameAIOrder order)
        {
            var progress = Progress(order.TimingDelay, order.TotalDelay);
            var current = order.Route != null && order.Route.Count >= 2 ? order.Route[0] : order.Origin;
            foreach (var node in RouteFor(order))
            {
                if (node.Fraction > progress + Epsilon) break;
                current = node.Name;
            }
            return current;
        }

        /// <summary>The route nodes an order has not yet passed, in order (the target last).</summary>
        public List<string> NodesAhead(GameAI.GameAIOrder order)
        {
            var progress = Progress(order.TimingDelay, order.TotalDelay);
            return RouteFor(order).Where(n => n.Fraction > progress + Epsilon).Select(n => n.Name).ToList();
        }
```

- [ ] **Step 5: Implement the planner**

Create `Assets/Flatspace/GameAI/ColonistRedirect.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Linq;

namespace FlatSpace.AI
{
    /// <summary>
    /// Re-plans a colonist already in flight when the owner now sees a blockade ahead of it: first a clean route to
    /// the same target (a detour, however long), else a clean route to another candidate target, else nothing (the
    /// order is cut as before). Pure: no Gameboard.Instance, so the self-check drives it directly. The colonist is
    /// treated as standing on the last route node it passed (BlockadeSystem.CurrentNode).
    /// </summary>
    public static class ColonistRedirect
    {
        public enum RedirectKind { None, Detour, Divert }

        public class Result
        {
            public RedirectKind Kind = RedirectKind.None;
            public string CurrentNode;
            public string Target;            // the target after the redirect (the order's own for None and Detour)
            public List<string> Nodes;       // the new route, from CurrentNode; null for None
            public float Cost;               // the REAL route cost (never tilted by the divisor); 0 for None
            public List<string> BlockedAhead = new List<string>();   // remaining route nodes the view says are blockaded
        }

        public static Result Plan(GameAIMap map, BlockadeSystem blockade, GameAI.GameAIOrder order, BlockadeView view,
            int maxNodes, Func<Planet, bool> isCandidate, Func<string, float> costDivisor)
        {
            var result = new Result { Target = order.Target, CurrentNode = blockade.CurrentNode(order) };
            if (view == null || order.TimingDelay <= 0) return result;

            result.BlockedAhead = blockade.NodesAhead(order).Where(view.IsBlockaded).ToList();
            if (result.BlockedAhead.Count == 0) return result;

            var detour = RoutePlanner.PlanRoute(map, result.CurrentNode, order.Target, view, maxNodes);
            if (detour != null)
            {
                result.Kind = RedirectKind.Detour;
                result.Nodes = detour.Nodes;
                result.Cost = detour.Cost;
                return result;
            }

            string bestTarget = null;
            RoutePlanner.PlannedRoute bestRoute = null;
            var bestChoiceCost = 0f;
            foreach (var planet in map.PlanetList)
            {
                var name = planet.PlanetName;
                if (name == order.Target || name == result.CurrentNode || !isCandidate(planet)) continue;
                var route = RoutePlanner.PlanRoute(map, result.CurrentNode, name, view, maxNodes);
                if (route == null) continue;
                var choiceCost = route.Cost / Math.Max(costDivisor(name), 0.0001f);
                if (bestRoute == null || choiceCost < bestChoiceCost
                    || (choiceCost == bestChoiceCost && string.CompareOrdinal(name, bestTarget) < 0))
                {
                    bestTarget = name;
                    bestRoute = route;
                    bestChoiceCost = choiceCost;
                }
            }

            if (bestRoute == null) return result;
            result.Kind = RedirectKind.Divert;
            result.Target = bestTarget;
            result.Nodes = bestRoute.Nodes;
            result.Cost = bestRoute.Cost;
            return result;
        }
    }
}
```

`map.PlanetList` is the existing `GameAIMap.PlanetList` property (line 38).

- [ ] **Step 6: Verify and commit**

Rider `get_file_problems` on the three changed/new files: expect no errors other than "not included in any project" for the two new files. Then:

```bash
git add Assets/Flatspace/GameAI/ColonistRedirect.cs Assets/Flatspace/GameAI/ColonistRedirect.cs.meta Assets/Flatspace/GameAI/BlockadeSystem.cs Assets/Editor/ColonistRedirectSelfCheck.cs Assets/Editor/ColonistRedirectSelfCheck.cs.meta Assets/Editor/BlockadeAvoidanceSelfCheck.cs
git commit -m "feat: ColonistRedirect planner and current-node helpers

Co-Authored-By: Claude Sonnet 5.5 <noreply@anthropic.com>"
```

---

### Task 2: Applying a redirect, the candidate rule and the arrival rule

**Files:**
- Modify: `Assets/Flatspace/GameAI/ColonistRedirect.cs` (add `Apply`)
- Modify: `Assets/Flatspace/GameAI/PlayerAI.cs` (`IsDiversionTarget`; `ColonizationCostDivisor` -> `public`)
- Modify: `Assets/Flatspace/GameAI/GameAI.cs` (`ApplyColonistArrival`, ExecuteOrder wiring)
- Modify: `Assets/Editor/ColonistRedirectSelfCheck.cs`

**Interfaces:**
- Consumes: Task 1's `Result`, `RedirectKind`.
- Produces:
  - `public static void ColonistRedirect.Apply(GameAIMap map, List<GameAI.GameAIOrder> orders, GameAI.GameAIOrder colonist, Result result)`: no-op for `Kind == None`. Mutates the colonist (`Route`, `TotalDelay`, `TimingDelay`, and `Target` for a divert), its food-rider twin, and the transfer flags.
  - `public bool PlayerAI.IsDiversionTarget(Planet planet)`.
  - `public float PlayerAI.ColonizationCostDivisor(string targetName)` (was private).
  - `public static bool GameAI.ApplyColonistArrival(Planet target, GameAIOrder order, Action<Planet, int> dockColonyShip = null)`: true when the colonist docked as a ship, false when it was added to the population.

- [ ] **Step 1: Write the failing tests**

Append to `ColonistRedirectSelfCheck.cs` (and call both from `RunChecks`: `ok &= RunApplyCheck(); ok &= RunCandidateCheck(); ok &= RunArrivalCheck();`):

```csharp
    private static GameAI.GameAIOrder Rider(int elapsed, string target = "D", string origin = "A")
        => new GameAI.GameAIOrder
        {
            Type = GameAI.GameAIOrder.OrderType.OrderTypeColonyFoodRider,
            TimingType = GameAI.GameAIOrder.OrderTimingType.OrderTimingTypeDelayed,
            TotalDelay = 10, TimingDelay = 10 - elapsed, Data = 10f, Origin = origin, Target = target, PlayerId = 0,
        };

    public static bool RunApplyCheck()
    {
        var ok = true;
        using (var s = BlockadeAvoidanceSelfCheck.Scenario.Diamond())
        {
            s.Constants.defaultTravelSpeed = 100f;
            var bs = new BlockadeSystem(s.Map, s.Research);

            // Detour: route and both delays change, target and flags do not.
            var colonist = Colonist(0);
            var rider = Rider(0);
            var orders = new List<GameAI.GameAIOrder> { colonist, rider };
            s.Map.GetPlanet("D").SetPopulationTransferInProgress(0);
            var detour = Plan(s, colonist, BlockadeView.Of("B"));
            ColonistRedirect.Apply(s.Map, orders, colonist, detour);
            ok &= Check(string.Join(">", colonist.Route) == "A>C>D" && colonist.Target == "D",
                "a detour replaces the route and keeps the target");
            ok &= Check(colonist.TotalDelay == colonist.TimingDelay
                        && colonist.TotalDelay == System.Math.Max(1, System.Convert.ToInt32(detour.Cost / 100f)),
                "the delay restarts from the new route's real cost (at least 1)");
            ok &= Check(rider.TimingDelay == colonist.TimingDelay && rider.TotalDelay == colonist.TotalDelay && rider.Target == "D",
                "the food rider keeps its target and takes the colonist's new delays");
            ok &= Check(s.Map.GetPlanet("D").IsPopulationTransferInProgress(0), "a detour keeps the target's inbound flag");

            // Divert: target, route, delays, rider and flags all move.
            var colonist2 = Colonist(0);
            var rider2 = Rider(0);
            var orders2 = new List<GameAI.GameAIOrder> { colonist2, rider2 };
            var divert = Plan(s, colonist2, BlockadeView.Of("D"), p => p.PlanetName == "C");
            ColonistRedirect.Apply(s.Map, orders2, colonist2, divert);
            ok &= Check(colonist2.Target == "C" && string.Join(">", colonist2.Route) == "A>C" && rider2.Target == "C",
                "a divert moves the colonist and its rider to the new target");
            ok &= Check(!s.Map.GetPlanet("D").IsPopulationTransferInProgress(0)
                        && s.Map.GetPlanet("C").IsPopulationTransferInProgress(0),
                "a divert clears the old target's inbound flag and sets the new one's");
            ok &= Check(colonist2.Origin == "A", "the order keeps its real origin");

            // Another colonist still heading for the old target keeps its flag.
            s.Map.GetPlanet("D").SetPopulationTransferInProgress(0);
            var other = Colonist(2); other.Origin = "A";
            var colonist3 = Colonist(0);
            var orders3 = new List<GameAI.GameAIOrder> { other, colonist3 };
            ColonistRedirect.Apply(s.Map, orders3, colonist3, Plan(s, colonist3, BlockadeView.Of("D"), p => p.PlanetName == "B"));
            ok &= Check(colonist3.Target == "B" && s.Map.GetPlanet("D").IsPopulationTransferInProgress(0),
                "the old target's flag stays while another colonist of the player still targets it");

            // Two colonists, same origin and target, two riders: redirecting one leaves the other's rider alone.
            var cA = Colonist(0); var rA = Rider(0);
            var cB = Colonist(4); var rB = Rider(4);
            var orders4 = new List<GameAI.GameAIOrder> { cA, rA, cB, rB };
            ColonistRedirect.Apply(s.Map, orders4, cB, Plan(s, cB, BlockadeView.Of("D"), p => p.PlanetName == "C"));
            ok &= Check(rB.Target == "C" && rA.Target == "D" && rA.TimingDelay == 10,
                "the rider is matched on equal delay: the other colonist's rider is untouched");

            // None is a no-op.
            var idle = Colonist(0);
            ColonistRedirect.Apply(s.Map, new List<GameAI.GameAIOrder> { idle }, idle, Plan(s, idle, BlockadeView.Of()));
            ok &= Check(idle.Target == "D" && string.Join(">", idle.Route) == "A>B>D" && idle.TimingDelay == 10,
                "no redirect leaves the order untouched");
        }
        return ok;
    }

    // A - B - C - D in a line; player 0 lives at A and its knowledge reaches direct neighbours only (radius 2 hops),
    // so A and B are known, C and D are not. Max population is 5 everywhere (BlockadeAvoidanceSelfCheck.Spawn).
    public static bool RunCandidateCheck()
    {
        var ok = true;
        var template = WarshipSelfCheck.MakeTemplate();
        var constants = WarshipSelfCheck.MakeConstants(template);
        var research = WarshipSelfCheck.MakeResearch();
        var mapGo = new GameObject("CRSelfCheckMap_Candidates");
        var playerGo = new GameObject("CRSelfCheckPlayer_Candidates");
        try
        {
            var map = BlockadeAvoidanceSelfCheck.Build(mapGo, constants,
                BlockadeAvoidanceSelfCheck.Spawn("A", 0f, 0f, new[] { "B" }),
                BlockadeAvoidanceSelfCheck.Spawn("B", 100f, 0f, new[] { "A", "C" }),
                BlockadeAvoidanceSelfCheck.Spawn("C", 200f, 0f, new[] { "B", "D" }),
                BlockadeAvoidanceSelfCheck.Spawn("D", 300f, 0f, new[] { "C" }));
            var a = map.GetPlanet("A");
            a.Owner = 0;
            for (var i = 0; i < 5; i++) a.Population.Add(new Planet.Inhabitant { Player = 0 });

            var player = playerGo.AddComponent<Player>();
            var ai = playerGo.AddComponent<PlayerAI>();
            ai.Player = player;
            ai.AIMap = map;
            player.playerID = 0;
            map.Knowledge.Update(map, 1, 2);

            var b = map.GetPlanet("B");
            ok &= Check(map.Knowledge.IsKnown(0, "B") && !map.Knowledge.IsKnown(0, "C"),
                "fixture: B (a neighbour of A) is known, C is not");
            ok &= Check(!ai.IsDiversionTarget(a), "my own full planet is not a diversion candidate");
            a.Population.RemoveRange(0, 2);   // 3 of 5
            ok &= Check(ai.IsDiversionTarget(a), "my own planet below max is a diversion candidate");
            ok &= Check(ai.IsDiversionTarget(b), "a known empty planet is a candidate");
            b.SetPopulationTransferInProgress(0);
            ok &= Check(ai.IsDiversionTarget(b), "a known planet with my colonist already inbound is a candidate");
            for (var i = 0; i < 5; i++) b.Population.Add(new Planet.Inhabitant { Player = 1 });
            b.SetPopulationTransferInProgress(0, false);
            ok &= Check(!ai.IsDiversionTarget(b), "a full planet held by another player is not a candidate");
            b.Population.RemoveRange(0, 3);   // 2 of 5, player 1
            ok &= Check(ai.IsDiversionTarget(b), "a planet held by another player below max is a candidate");
            ok &= Check(!ai.IsDiversionTarget(map.GetPlanet("C")), "an unknown planet is never a candidate");
            ok &= Check(ai.ColonizationCostDivisor("C") >= 1f, "the cost divisor is public and at least 1");
        }
        finally
        {
            Object.DestroyImmediate(playerGo);
            Object.DestroyImmediate(mapGo);
            WarshipSelfCheck.DestroyAll(research);
            Object.DestroyImmediate(constants);
            Object.DestroyImmediate(template);
        }
        return ok;
    }

    public static bool RunArrivalCheck()
    {
        var ok = true;
        using (var s = BlockadeAvoidanceSelfCheck.Scenario.Diamond())
        {
            var docked = new List<(string planet, int owner)>();
            System.Action<Planet, int> dock = (p, owner) =>
            {
                docked.Add((p.PlanetName, owner));
                p.DockShipFromSave(Ship.ShipKind.ColonyShip, owner, new List<string>());
            };

            var d = s.Map.GetPlanet("D");
            var order = Colonist(10);
            d.SetPopulationTransferInProgress(0);
            var added = GameAI.ApplyColonistArrival(d, order, dock);
            ok &= Check(!added && d.Population.Count == 1 && d.Population[0].Player == 0 && docked.Count == 0,
                "below max: the colonist joins the population and no ship docks");
            ok &= Check(!d.IsPopulationTransferInProgress(0), "arrival clears the inbound flag");

            // Fill to max (5): the next colonist docks instead.
            for (var i = d.Population.Count; i < d.MaxPopulation; i++) d.Population.Add(new Planet.Inhabitant { Player = 0 });
            d.SetPopulationTransferInProgress(0);
            var dockedNow = GameAI.ApplyColonistArrival(d, Colonist(10), dock);
            ok &= Check(dockedNow && d.Population.Count == d.MaxPopulation && docked.Count == 1
                        && docked[0] == ("D", 0) && d.DockedShips.Exists(sh => sh.Kind == Ship.ShipKind.ColonyShip && sh.Owner == 0),
                "at max: a colony ship docks for the order's player and the population is unchanged");
            ok &= Check(!d.IsPopulationTransferInProgress(0), "docking also clears the inbound flag");

            // A contested planet below max still takes the colonist (the plurality rule is not the arrival rule).
            var c = s.Map.GetPlanet("C");
            for (var i = 0; i < 3; i++) c.Population.Add(new Planet.Inhabitant { Player = 1 });
            var contested = GameAI.ApplyColonistArrival(c, Colonist(10, "C"), dock);
            ok &= Check(!contested && c.Population.Count == 4, "a planet held by another player below max still takes the colonist");
        }
        return ok;
    }
```

- [ ] **Step 2: Run to verify failure**

Rider `get_file_problems` on the self-check: expect unresolved `Apply`, `IsDiversionTarget`, `ApplyColonistArrival`, `ColonizationCostDivisor` inaccessible.

- [ ] **Step 3: Implement `Apply`**

Add to `ColonistRedirect`:

```csharp
        /// <summary>
        /// Applies a Plan result to the colonist order: new route, delay restarted from the new route's real cost (at
        /// least 1), and for a divert the new target. Its food rider (matched on player, origin, old target and the
        /// colonist's own delay, so a twin colonist's rider is never touched) follows; the transfer flags follow a divert.
        /// </summary>
        public static void Apply(GameAIMap map, List<GameAI.GameAIOrder> orders, GameAI.GameAIOrder colonist, Result result)
        {
            if (result.Kind == RedirectKind.None) return;

            var oldTarget = colonist.Target;
            var oldDelay = colonist.TimingDelay;
            var rider = orders.Find(o => o.Type == GameAI.GameAIOrder.OrderType.OrderTypeColonyFoodRider
                                         && o.PlayerId == colonist.PlayerId && o.Origin == colonist.Origin
                                         && o.Target == oldTarget && o.TimingDelay == oldDelay);

            var delay = Math.Max(1, Convert.ToInt32(result.Cost / map.GameAIConstants.defaultTravelSpeed));
            colonist.Route = new List<string>(result.Nodes);
            colonist.TotalDelay = delay;
            colonist.TimingDelay = delay;
            if (rider != null)
            {
                rider.TotalDelay = delay;
                rider.TimingDelay = delay;
            }

            if (result.Kind != RedirectKind.Divert) return;

            colonist.Target = result.Target;
            if (rider != null) rider.Target = result.Target;

            var stillHeadingThere = orders.Exists(o => o != colonist
                && o.Type == GameAI.GameAIOrder.OrderType.OrderTypePopulationTransport
                && o.PlayerId == colonist.PlayerId && o.Target == oldTarget);
            if (!stillHeadingThere) map.GetPlanet(oldTarget)?.SetPopulationTransferInProgress(colonist.PlayerId, false);
            map.GetPlanet(result.Target)?.SetPopulationTransferInProgress(colonist.PlayerId);
        }
```

- [ ] **Step 4: Implement `IsDiversionTarget` and make the divisor public**

In `PlayerAI.cs`, after `IsValidColonizationTarget` (line ~448):

```csharp
            // The wider target set for a colonist already in flight (see ColonistRedirect): known, and empty, or my
            // colonist is already inbound, or colonized below max population (my own planets included). Full planets are
            // excluded. Ordinary launches keep IsValidColonizationTarget. Public for the self-check.
            public bool IsDiversionTarget(Planet planet)
            {
                if (!AIMap.Knowledge.IsKnown(Player.playerID, planet.PlanetName)) return false;
                if (planet.Population.Count == 0)                                return true;
                if (planet.IsPopulationTransferInProgress(Player.playerID))       return true;
                return planet.Population.Count < planet.MaxPopulation;
            }
```

Change `private float ColonizationCostDivisor(string targetName)` to `public float ColonizationCostDivisor(string targetName)` (comment above it unchanged, add "Public for ColonistRedirect.").

- [ ] **Step 5: Implement the arrival rule**

In `GameAI.cs`, add near `ApplyShipArrival`:

```csharp
            // A colonist lands: below max population it joins the planet as before; at or above max the colonist docks as a
            // colony ship for the order's player instead (eligible for the next colonization pass). The ship's research
            // snapshot is rebuilt from the owner's current research (a colonist order carries none) via the default dock,
            // which needs Gameboard.Instance; the self-check passes its own. Returns true when a ship docked.
            public static bool ApplyColonistArrival(Planet target, GameAIOrder order, Action<Planet, int> dockColonyShip = null)
            {
                target.SetPopulationTransferInProgress(order.PlayerId, false);
                if (target.Population.Count >= target.MaxPopulation)
                {
                    (dockColonyShip ?? DefaultDockColonyShip)(target, order.PlayerId);
                    return true;
                }
                target.ChangePopulation(Convert.ToInt32(order.Data), order.PlayerId);
                return false;
            }

            private static void DefaultDockColonyShip(Planet target, int owner)
                => target.DockShipRebuiltSnapshot(Ship.ShipKind.ColonyShip, owner);
```

In `ExecuteOrder`, replace the first lines of the `OrderTypePopulationTransport` case:

```csharp
                    case GameAIOrder.OrderType.OrderTypePopulationTransport:
                        targetPlanet.ChangePopulation(Convert.ToInt32(executableOrder.Data), executableOrder.PlayerId);

                        if (targetPlanet.IsPopulationTransferInProgress(executableOrder.PlayerId))
                        {
                            targetPlanet.SetPopulationTransferInProgress(executableOrder.PlayerId, false);
                        }
```

with:

```csharp
                    case GameAIOrder.OrderType.OrderTypePopulationTransport:
                        if (ApplyColonistArrival(targetPlanet, executableOrder))
                            AITuningLogger.LogColonistDocked(Gameboard.Instance.TurnNumber, executableOrder.PlayerId,
                                targetPlanet.PlanetName, Convert.ToInt32(executableOrder.Data));
```

(the DC coverage-gap lines after it stay as they are). `LogColonistDocked` is added in Task 4; to keep this task compiling add it now as a stub-free method in Task 4's step order, or implement Task 4 Step 3's `LogColonistDocked` here. Do the latter: add exactly this to `AITuningLogger.cs` after `LogRouteDetour`:

```csharp
    /// <summary>A colonist arrived at a full planet and docked as a colony ship: T&lt;turn&gt;|P&lt;id&gt;|ColonistDocked|planet|amount.</summary>
    public static void LogColonistDocked(int turnNumber, int playerId, string planetName, int amount)
    {
        if (_currentLogPath == null) return;
        AppendLines(new List<string> { FormatLine(turnNumber, playerId, "ColonistDocked", planetName,
            amount.ToString(System.Globalization.CultureInfo.InvariantCulture)) });
    }
```

- [ ] **Step 6: Verify and commit**

Rider `get_file_problems` on `ColonistRedirect.cs`, `PlayerAI.cs`, `GameAI.cs`, `AITuningLogger.cs`, `ColonistRedirectSelfCheck.cs`. Then:

```bash
git add Assets/Flatspace/GameAI/ColonistRedirect.cs Assets/Flatspace/GameAI/PlayerAI.cs Assets/Flatspace/GameAI/GameAI.cs Assets/Flatspace/Diagnostics/AITuningLogger.cs Assets/Editor/ColonistRedirectSelfCheck.cs
git commit -m "feat: apply colonist redirects; diversion candidates; colonists dock at full planets

Co-Authored-By: Claude Sonnet 5.5 <noreply@anthropic.com>"
```

---

### Task 3: The per-turn step, and register the suite

**Files:**
- Modify: `Assets/Flatspace/GameAI/GameAI.cs` (`RedirectColonists`, hook in `ProcessCurrentOrders`, `ClearGameAI`)
- Modify: `Assets/Flatspace/Diagnostics/AITuningLogger.cs` (two lines)
- Modify: `Assets/Editor/AllAISelfChecks.cs`

**Interfaces:**
- Consumes: Tasks 1-2 (`ColonistRedirect.Plan/Apply`, `PlayerAI.IsDiversionTarget`, `PlayerAI.ColonizationCostDivisor`, `PlayerAI.CurrentBlockadeView`, `PlayerAI.BlockedNodeSummary`).
- Produces: `AITuningLogger.LogColonistRedirect(int turn, int playerId, string origin, string oldTarget, string kind, string newTarget, IEnumerable<string> nodes, float cost, string blockedSummary)` and `AITuningLogger.LogColonistRedirectFailed(int turn, int playerId, string origin, string target, string blockedSummary)`.

The step itself touches `Gameboard.Instance` and players, so it has no self-check (like `ApplyBlockades`); its logic is covered by Tasks 1-2. This task is verified by a Play-mode run (Task 5).

- [ ] **Step 1: Add the two log lines**

In `AITuningLogger.cs` after `LogColonistDocked`:

```csharp
    /// <summary>A colonist in flight was redirected: T&lt;turn&gt;|P&lt;id&gt;|ColonistRedirect|origin-&gt;oldTarget|Detour or Divert|newTarget|nodes joined by '&gt;'|cost|blocked planets (name=value joined by ',', '-' when none).</summary>
    public static void LogColonistRedirect(int turnNumber, int playerId, string origin, string oldTarget, string kind,
        string newTarget, IEnumerable<string> nodes, float cost, string blockedSummary)
    {
        if (_currentLogPath == null) return;
        AppendLines(new List<string> { FormatLine(turnNumber, playerId, "ColonistRedirect", $"{origin}->{oldTarget}", kind,
            newTarget, string.Join(">", nodes), cost.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture),
            blockedSummary) });
    }

    /// <summary>A colonist in flight could not be saved from a blockade ahead: T&lt;turn&gt;|P&lt;id&gt;|ColonistRedirectFailed|origin-&gt;target|blocked planets. Once per order.</summary>
    public static void LogColonistRedirectFailed(int turnNumber, int playerId, string origin, string target, string blockedSummary)
    {
        if (_currentLogPath == null) return;
        AppendLines(new List<string> { FormatLine(turnNumber, playerId, "ColonistRedirectFailed", $"{origin}->{target}",
            blockedSummary) });
    }
```

- [ ] **Step 2: Add the per-turn step**

In `GameAI.cs`, next to `_grotsitsShort`:

```csharp
            // Orders already logged as ColonistRedirectFailed, so it is logged once per order and not every turn the blockade
            // stays ahead of it (log-only state, pruned as orders leave; a load logs each still-failing order once more).
            private readonly HashSet<GameAIOrder> _redirectFailedLogged = new HashSet<GameAIOrder>();
```

Add `_redirectFailedLogged.Clear();` to `ClearGameAI()`. In `ProcessCurrentOrders` call it right after `ApplyBlockades();`:

```csharp
                ApplyBlockades();
                RedirectColonists();
```

Add the method after `ApplyBlockades`:

```csharp
            // Once per turn, after blockades were applied and before orders execute: a colonist whose remaining route now
            // crosses a blockade its owner can see is detoured or diverted (ColonistRedirect); one that cannot be saved is
            // left to be cut as before and logged once. Uses only the owner's BlockadeView.
            private void RedirectColonists()
            {
                var research = BlockadeSystem.ResearchItemsFrom(Gameboard.Instance.players);
                if (research == null) return;
                var turn = Gameboard.Instance.TurnNumber;
                var blockade = new BlockadeSystem(GameAIMap, research);
                var maxNodes = GameAIMap.GameAIConstants.maxPathNodesForColonization;

                _redirectFailedLogged.RemoveWhere(o => !CurrentAIOrders.Contains(o));
                var colonists = CurrentAIOrders.FindAll(o =>
                    o.Type == GameAIOrder.OrderType.OrderTypePopulationTransport && o.TimingDelay > 0);
                foreach (var colonist in colonists)
                {
                    if (colonist.PlayerId < 0 || colonist.PlayerId >= Gameboard.Instance.players.Count) continue;
                    var ai = Gameboard.Instance.players[colonist.PlayerId]?.playerAI;
                    if (ai == null || ai.CurrentBlockadeView == null) continue;

                    var result = ColonistRedirect.Plan(GameAIMap, blockade, colonist, ai.CurrentBlockadeView, maxNodes,
                        ai.IsDiversionTarget, ai.ColonizationCostDivisor);
                    if (result.Kind == ColonistRedirect.RedirectKind.None)
                    {
                        if (result.BlockedAhead.Count > 0 && _redirectFailedLogged.Add(colonist))
                            AITuningLogger.LogColonistRedirectFailed(turn, colonist.PlayerId, colonist.Origin, colonist.Target,
                                ai.BlockedNodeSummary(result.BlockedAhead));
                        continue;
                    }

                    var oldTarget = colonist.Target;
                    var blocked = ai.BlockedNodeSummary(result.BlockedAhead);
                    ColonistRedirect.Apply(GameAIMap, CurrentAIOrders, colonist, result);
                    AITuningLogger.LogColonistRedirect(turn, colonist.PlayerId, colonist.Origin, oldTarget,
                        result.Kind.ToString(), result.Target, result.Nodes, result.Cost, blocked);
                }
            }
```

`playerAI` is the existing field on `Player` used in `ApplyBlockades`/`ExecuteOrder`. `ai.IsDiversionTarget` and `ai.ColonizationCostDivisor` convert to `Func<Planet,bool>` / `Func<string,float>` as method groups.

- [ ] **Step 3: Register the suite**

In `AllAISelfChecks.cs` add `("Colonist Redirect", ColonistRedirectSelfCheck.RunChecks),` after `("Chokepoint", ...)`.

- [ ] **Step 4: Verify, then Editor run (user)**

Rider `get_file_problems` on `GameAI.cs`, `AITuningLogger.cs`, `AllAISelfChecks.cs`. Then ask the user to focus the Editor and run `FlatSpace -> AI -> Run All AI Self-Checks`: expect `ALL 10 SUITES PASSED` (earlier suites must still pass: `BlockadeAvoidanceSelfCheck` was only made more visible). Fix any failure before committing; note any assertion dropped in Task 2.

- [ ] **Step 5: Commit**

```bash
git add Assets/Flatspace/GameAI/GameAI.cs Assets/Flatspace/Diagnostics/AITuningLogger.cs Assets/Editor/AllAISelfChecks.cs
git commit -m "feat: redirect in-flight colonists each turn; log redirects and failures

Co-Authored-By: Claude Sonnet 5.5 <noreply@anthropic.com>"
```

---

### Task 4: Play-mode verification

**Files:** none changed (verification; fix anything it finds as a new commit).

- [ ] **Step 1:** Ask the user to run Play mode (`Assets/Scenes/MainMenu.unity`, `_logAIEvents` on) on `4p.json` and on `test2.json` to T399, 3 runs each if time allows (the `Farm 1` T379-T382 colonist losses are the original symptom).
- [ ] **Step 2:** Run `/tuning-log` on the new logs. Check: `ColonistRedirect` lines exist, with both `Detour` and `Divert`; `ColonistRedirectFailed` count against colonist `OrderBlocked` losses (`OrderBlocked|OrderTypePopulationTransport`), compared with the 2026-10-02 12:40-12:48 `4p.json` runs; `ColonistDocked` fires and the docked ship then launches (a later `ColonizerReady`/`ColonyStart` at that planet); no fleet-cap or economy regression (planets at T375, `PlanetDead`, `Blockade` counts inside the earlier ranges); no turn where a redirect line repeats for the same order.
- [ ] **Step 3:** If a diverted colonist is lost on a planet that became full, or a `ColonistDocked` ship sits unused for many turns, report it to the user before changing anything (colonization readiness is `Planet.CheckColonizationReady`; do not tune without telling them).

---

### Task 5: Docs and the tuning-log skill

**Files:**
- Modify: `CLAUDE.md` (Orders/Warships and Blockade section: a "Colonist redirect" bullet; the "AI Tuning Log" section: the three lines; the Tests list: the new suite and `Run All AI Self-Checks` now 10 suites)
- Modify: `FUTURE_FEATURES.md` (mark (5) done; the milestone line: all of (2)-(5) done)
- Modify: `.claude/skills/tuning-log/SKILL.md` (a "Colonist redirect" analysis section)

- [ ] **Step 1:** In `CLAUDE.md` under "Warships and Blockade" add a bullet after "Blockade breaking": what `ColonistRedirect` does (trigger after `ApplyBlockades`, owner's `BlockadeView` only, current node = last passed, detour then divert, clock restart, candidate rule `IsDiversionTarget`, rider and flag handling, arrival rule `ApplyColonistArrival`, accepted limits from the spec). Add the three log lines to the "AI Tuning Log" paragraph in the format already used there. Update the self-check list entry and "Run All AI Self-Checks" suite count/names.
- [ ] **Step 2:** In `FUTURE_FEATURES.md`, rewrite entry (5) as done (or move it with `/update_feature_list`) and state that the blockade unit milestone is complete.
- [ ] **Step 3:** In the `tuning-log` skill add a "Colonist redirect" section: count `ColonistRedirect` by kind, `ColonistRedirectFailed`, `ColonistDocked`, and colonist `OrderBlocked` losses; compare against the baseline runs listed in Task 4.
- [ ] **Step 4: Commit**

```bash
git add CLAUDE.md FUTURE_FEATURES.md .claude/skills/tuning-log/SKILL.md
git commit -m "docs: colonist redirect (CLAUDE.md, FUTURE_FEATURES.md, tuning-log skill)

Co-Authored-By: Claude Sonnet 5.5 <noreply@anthropic.com>"
```

---

## Self-review (spec coverage)

- Trigger after `ApplyBlockades`, `TimingDelay > 0`, owner's view only: Task 3 (`RedirectColonists`), Task 1 (`Plan` returns `None` for null view / no delay).
- Current node, nodes ahead: Task 1. Detour then divert then none: Task 1. Candidate rule and no `CanSupportColony`: Task 2. No claim tracking: nothing built, arrival rule absorbs.
- Rider and flags (including the "another colonist still targets it" rule): Task 2 `Apply`.
- Arrival rule for every colonist, dock via rebuilt snapshot: Task 2 (`ApplyColonistArrival`, ExecuteOrder wiring); the DC coverage-gap logging after it is unchanged.
- Logging (3 lines, tracker for the failed line): Tasks 2 and 3; `tuning-log` skill and `CLAUDE.md`: Task 5.
- Self-check suite registered (10 suites): Tasks 1-3. Zero-turn delay clamp: Task 2 `Apply` (`Math.Max(1, ...)`, asserted). Ordinary launches unchanged: no edit to `IsValidColonizationTarget` or `ProcessColonizers`.
- Saves/tunables: none, per spec.
