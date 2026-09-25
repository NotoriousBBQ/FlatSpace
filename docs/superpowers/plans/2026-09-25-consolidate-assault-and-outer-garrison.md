# Consolidate Outer-Only Garrisons and Assault Targeting Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Under Consolidate, warships garrison only outer planets (round 1) and the rest mass on one known enemy-occupied planet in a force sized to the enemy's known docked fleet.

**Architecture:** `ShipTransportPlanner` gains an optional strategy parameter (Expand default = unchanged behavior), a shared "outer = neighbour not colonized by me" definition, a `MaintainsGarrison` seam, a `HeldPlanet`, an outer-first `Rank`, and retained `LastStates`. A new `AssaultPlanner` picks the target, sizes the force and emits `ShipAction`s from spare ships left after the home plan. `PlayerAI.PlanShipActions` orchestrates both; `EmitShipOrders` is reused.

**Tech Stack:** Unity 6000.4.1f1, C#, Editor self-check (`Assets/Editor/ShipTransportSelfCheck.cs`).

**Spec:** `docs/superpowers/specs/2026-09-25-consolidate-assault-and-outer-garrison-design.md`

## Global Constraints

- There is no command-line build or test runner. Per-step "RED/GREEN" uses `mcp__rider__get_file_problems` (Roslyn; can be stale). The real run is the user focusing the Unity Editor and running `FlatSpace -> AI -> Run Ship Transport Self-Check` (plus the Player Knowledge and PlayerAI Resource self-checks). That user run happens once, at the end of Task 3.
- A self-check must never depend on `Gameboard.Instance`, and **any runtime code reachable from a self-check must null-guard it** (`Gameboard.Instance != null ? ... : 0`). Last cycle `PlayerAIResourceSelfCheck` broke because a new line dereferenced it. `PlayerAI.ProcessShipActions` is called by `RunPlayerAIShipOrdersCheck` with no Gameboard.
- Give test planets distinct positions (the existing `MakeSpawn` in `ShipTransportSelfCheck` already does); reset `_nextPlanetX = 0f` at the start of each check. Isolated planets log a harmless `[PathingSystem]` error, so keep test graphs connected.
- A method a self-check calls directly is `public`, not `internal`.
- Expand behavior must not change: every existing `ShipTransportSelfCheck` assertion must still pass untouched.
- `OrderType` is untouched (no new order types).
- `AssaultPlanner.cs` is a new script: its `.meta` file (Unity creates it when the Editor focuses) must be committed with it.
- Never `git add -A`; the tree has unrelated uncommitted files. Stage explicit paths only.
- Commit messages end with: `Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>`

## Spec deviation (deliberate; surface to the user at handoff)

The spec defines `Rank` as "0 for outer planets, otherwise the category". Under Consolidate only outer planets are garrisoned, so that makes every target rank 0 and reduces ordering among targets to path cost alone, losing today's Farm-before-Normal ordering, and an "outer sorts before interior" test would be impossible since interior planets are never targets. This plan uses `Rank = (outer ? 0 : 100) + Category` under Consolidate (Expand: `Rank = Category`). Outer still sorts first, intra-outer ordering is preserved, and the rank still does the right thing if non-outer garrisons are enabled later.

## Review Focus

- Warships docked on the assault target are never reclassified as spare and sent home (`HeldPlanet`).
- Enemy ships on unknown planets, and ships with no owner (`Owner == -1`), are never counted in the enemy total.
- A player whose only warships already sit on the target keeps that target (no path from "other" holders needed).
- Ships needed by an outer garrison are never also committed to the assault.
- Expand never targets an enemy planet, and Expand's outer definition change is a no-op on existing boards.

---

### Task 1: Strategy-aware `ShipTransportPlanner`

**Files:**
- Modify: `Assets/Flatspace/GameAI/ShipTransportPlanner.cs`
- Modify: `Assets/Flatspace/GameAI/ShipMatrix.cs` (add `Rank` to `ShipChoiceElement`)
- Modify: `Assets/Editor/ShipTransportSelfCheck.cs`

**Interfaces:**
- Consumes: existing `ShipTransportPlanner(GameAIMap, int)`, `PlayerAI.AIStrategy`.
- Produces: `ShipTransportPlanner(GameAIMap map, int playerId, PlayerAI.AIStrategy strategy = AIStrategyExpand)`; `string HeldPlanet { get; set; }`; `bool MaintainsGarrison(Planet)`; `int TargetRank(Planet)`; `List<PlanetState> LastStates { get; }`; `int PlanetState.Rank`; `int ShipChoiceElement.Rank`.

- [ ] **Step 1: Write the failing self-checks**

In `Run()` add after `ok &= RunPlayerAIShipOrdersCheck();`:

```csharp
        ok &= RunConsolidatePlannerChecks();
```

Add before the final closing brace of the class:

```csharp
    private static ShipTransportPlanner ConsolidatePlanner(GameAIMap map, string heldPlanet = null)
        => new ShipTransportPlanner(map, 0, PlayerAI.AIStrategy.AIStrategyConsolidate) { HeldPlanet = heldPlanet };

    public static bool RunConsolidatePlannerChecks()
    {
        var ok = RunSharedOuterDefinitionCheck();
        ok &= RunConsolidateRolesCheck();
        ok &= RunConsolidateRankCheck();
        ok &= RunHeldPlanetCheck();
        return ok;
    }

    private static bool RunSharedOuterDefinitionCheck()
    {
        var ok = true;
        _nextPlanetX = 0f;
        var go = new GameObject("STSelfCheckMap_SharedOuter");
        try
        {
            // A0 - A1 - E: A0 and A1 are player 0's, E is player 1's.
            var map = BuildMap(go, NewConstants(),
                MakeSpawn("A0", Planet.PlanetType.PlanetTypeNormal, new[] { "A1" }),
                MakeSpawn("A1", Planet.PlanetType.PlanetTypeNormal, new[] { "E" }),
                MakeSpawn("E", Planet.PlanetType.PlanetTypeNormal));
            var a0 = Colonize(map, "A0"); var a1 = Colonize(map, "A1");
            var e = Colonize(map, "E", 1);
            var planner = new ShipTransportPlanner(map, 0);

            ok &= Check(!planner.IsOuter(a0), "A0's only neighbour is its own colony: not outer");
            ok &= Check(planner.IsOuter(a1), "A1 borders an enemy-held planet: outer (a neighbour not colonized by me)");

            e.Owner = Planet.NoOwner;   // contested / tied: still not colonized by me
            ok &= Check(planner.IsOuter(a1), "a contested (ownerless) neighbour also makes A1 outer");
        }
        finally { Object.DestroyImmediate(go); }
        return ok;
    }

    private static bool RunConsolidateRolesCheck()
    {
        var ok = true;

        // I(Farm) - O(Normal) - U(empty): O is outer, I is interior.
        _nextPlanetX = 0f;
        var go = new GameObject("STSelfCheckMap_ConsolidateRoles");
        try
        {
            var map = BuildMap(go, NewConstants(),
                MakeSpawn("I", Planet.PlanetType.PlanetTypeFarm, new[] { "O" }),
                MakeSpawn("O", Planet.PlanetType.PlanetTypeNormal, new[] { "U" }),
                MakeSpawn("U", Planet.PlanetType.PlanetTypeNormal));
            var i = Colonize(map, "I"); var o = Colonize(map, "O");
            Dock(o, 12); Dock(i, 5);

            var consolidate = ConsolidatePlanner(map);
            var states = consolidate.BuildStates();
            ok &= Check(consolidate.LastRound == 1, "Consolidate: the round is fixed at 1");
            var oState = states.Find(s => s.Planet == o);
            ok &= Check(oState.Garrison == 6 && oState.RoundGarrison == 6 && oState.Spare == 6,
                "Consolidate: outer O keeps a round-1 garrison of 6 and 12 docked leaves 6 spare");
            var iState = states.Find(s => s.Planet == i);
            ok &= Check(iState.Garrison == 0 && iState.Category == ShipTransportPlanner.NoCategory && iState.Spare == 5,
                "Consolidate: interior I keeps no garrison, so all 5 of its ships are spare");

            var expand = new ShipTransportPlanner(map, 0);
            var expandStates = expand.BuildStates();
            ok &= Check(expand.LastRound == 2 && expandStates.Find(s => s.Planet == i).Garrison == 4,
                "Expand is unchanged: round 2 and I still garrisons 4");
        }
        finally { Object.DestroyImmediate(go); }

        // V(Verdant) - O - U: a category-5-only planet is locked under Expand but is a spare-only source under Consolidate.
        _nextPlanetX = 0f;
        go = new GameObject("STSelfCheckMap_ConsolidateLocked");
        try
        {
            var map = BuildMap(go, NewConstants(),
                MakeSpawn("V", Planet.PlanetType.PlanetTypeVerdant, new[] { "O" }),
                MakeSpawn("O", Planet.PlanetType.PlanetTypeNormal, new[] { "U" }),
                MakeSpawn("U", Planet.PlanetType.PlanetTypeNormal));
            var v = Colonize(map, "V"); Colonize(map, "O");
            Dock(v, 3);   // 3 warships < 2 x 2 colonized planets, so V is locked under Expand

            ok &= Check(!new ShipTransportPlanner(map, 0).BuildStates().Exists(s => s.Planet == v),
                "Expand: a locked category-5-only planet is left out");
            var vState = ConsolidatePlanner(map).BuildStates().Find(s => s.Planet == v);
            ok &= Check(vState != null && vState.Spare == 3,
                "Consolidate: the same planet is included and all 3 of its ships are spare");
        }
        finally { Object.DestroyImmediate(go); }
        return ok;
    }

    private static bool RunConsolidateRankCheck()
    {
        var ok = true;
        _nextPlanetX = 0f;
        var go = new GameObject("STSelfCheckMap_Rank");
        try
        {
            // F(Farm, outer via W) - O(Normal, outer via U) - I(Desert, interior).
            var map = BuildMap(go, NewConstants(),
                MakeSpawn("F", Planet.PlanetType.PlanetTypeFarm, new[] { "W", "O" }),
                MakeSpawn("O", Planet.PlanetType.PlanetTypeNormal, new[] { "U", "I" }),
                MakeSpawn("I", Planet.PlanetType.PlanetTypeDesert),
                MakeSpawn("U", Planet.PlanetType.PlanetTypeNormal),
                MakeSpawn("W", Planet.PlanetType.PlanetTypeNormal));
            var f = Colonize(map, "F"); var o = Colonize(map, "O"); var i = Colonize(map, "I");

            var expand = new ShipTransportPlanner(map, 0);
            ok &= Check(expand.TargetRank(f) == 1 && expand.TargetRank(o) == 2 && expand.TargetRank(i) == 1,
                "Expand: rank equals category (interior Desert is not pushed behind outer Normal)");

            var consolidate = ConsolidatePlanner(map);
            ok &= Check(consolidate.TargetRank(f) == 1 && consolidate.TargetRank(o) == 2,
                "Consolidate: outer planets keep their category order (Farm before Normal)");
            ok &= Check(consolidate.TargetRank(i) == 101 && consolidate.TargetRank(i) > consolidate.TargetRank(o),
                "Consolidate: an interior planet ranks behind every outer planet");
        }
        finally { Object.DestroyImmediate(go); }
        return ok;
    }

    private static bool RunHeldPlanetCheck()
    {
        var ok = true;
        _nextPlanetX = 0f;
        var go = new GameObject("STSelfCheckMap_Held");
        try
        {
            // O(mine, outer because of E) - E(enemy). 3 of my warships sit on E; O is 4 short.
            var map = BuildMap(go, NewConstants(),
                MakeSpawn("O", Planet.PlanetType.PlanetTypeNormal, new[] { "E" }),
                MakeSpawn("E", Planet.PlanetType.PlanetTypeNormal));
            var o = Colonize(map, "O"); var e = Colonize(map, "E", 1);
            Dock(o, 2); Dock(e, 3);

            var free = ConsolidatePlanner(map);
            ok &= Check(free.BuildStates().Exists(s => s.Planet == e && s.Spare == 3),
                "without a held planet, ships on an enemy planet are stranded (all spare)");
            ok &= Check(free.Plan().Exists(a => a.Origin == "E" && a.Target == "O" && a.Count == 3),
                "and would be sent home to fill O's garrison");

            var held = ConsolidatePlanner(map, "E");
            ok &= Check(!held.BuildStates().Exists(s => s.Planet == e),
                "with E held, its ships are not a state at all");
            ok &= Check(!held.Plan().Exists(a => a.Origin == "E"),
                "and the home plan never moves them");
        }
        finally { Object.DestroyImmediate(go); }
        return ok;
    }
```

- [ ] **Step 2: Verify RED**

Run `mcp__rider__get_file_problems` (errorsOnly) on `Assets/Editor/ShipTransportSelfCheck.cs`. Expected: errors such as `'ShipTransportPlanner' does not contain a constructor that takes 3 arguments`, `Cannot resolve symbol 'HeldPlanet'`, `'TargetRank'`, `'LastStates'`.

- [ ] **Step 3: Implement**

In `ShipMatrix.cs`, add to `ShipChoiceElement` after the `Category` line:

```csharp
    public int    Rank         { get; set; }   // sort key: lower first; equals Category except under Consolidate
```

In `ShipTransportPlanner.cs`:

(a) Replace the fields and constructor (lines 19-28) with:

```csharp
            private readonly GameAIMap _map;
            private readonly int _playerId;
            private readonly GameAIConstants _constants;
            private readonly PlayerAI.AIStrategy _strategy;

            /// <summary>
            /// Under Consolidate: the assault target. Warships docked there are the assault force, so
            /// they are not "stranded" (neither sources nor targets of the home plan).
            /// </summary>
            public string HeldPlanet { get; set; }

            public ShipTransportPlanner(GameAIMap map, int playerId,
                PlayerAI.AIStrategy strategy = PlayerAI.AIStrategy.AIStrategyExpand)
            {
                _map = map;
                _playerId = playerId;
                _constants = map.GameAIConstants;
                _strategy = strategy;
            }
```

(b) Replace `IsOuter` (lines 35-45) with:

```csharp
            // Colonized by this player with at least one neighbour that is not colonized by this
            // player (empty, enemy-held, or contested).
            public bool IsOuter(Planet planet)
            {
                if (!IsColonized(planet)) return false;
                foreach (var name in _map.GetNeighbours(planet.PlanetName))
                {
                    var neighbour = _map.GetPlanet(name);
                    if (neighbour != null && !IsColonized(neighbour)) return true;
                }
                return false;
            }

            /// <summary>
            /// The seam for garrison policy: does this colonized planet hold a garrison? Every planet
            /// does under Expand; under Consolidate only outer planets do, so every ship elsewhere is
            /// spare. To garrison non-outer planets under Consolidate later, change this one method.
            /// </summary>
            public bool MaintainsGarrison(Planet planet)
                => _strategy != PlayerAI.AIStrategy.AIStrategyConsolidate || IsOuter(planet);
```

(c) After `Category` (line 75) add:

```csharp
            /// <summary>
            /// Sort key for target choices (lower first). Expand: the category. Consolidate: outer
            /// planets first (0 + category), everything else behind them (100 + category).
            /// </summary>
            public int TargetRank(Planet planet)
            {
                var category = Category(planet);
                if (category == NoCategory || _strategy != PlayerAI.AIStrategy.AIStrategyConsolidate)
                    return category;
                return (IsOuter(planet) ? 0 : 100) + category;
            }
```

(d) In `PlanetState` add after `public int Category;`:

```csharp
                public int Rank;            // sort key, see TargetRank
```

(e) Add after `LastRound`:

```csharp
            /// <summary>The states built by the last BuildStates/Plan call.</summary>
            public List<PlanetState> LastStates { get; private set; } = new List<PlanetState>();
```

(f) Replace the whole `BuildStates` method with:

```csharp
            public List<PlanetState> BuildStates()
            {
                var colonized = _map.PlanetList.Where(IsColonized).ToList();
                // Planets the player holds ships on but no longer has colonized (ownership can flip
                // while a fleet is in flight): they are source-only so those ships are not stranded.
                // The held (assault target) planet is excluded: those ships are the assault force.
                var stranded = _map.PlanetList
                    .Where(p => !IsColonized(p) && p.PlanetName != HeldPlanet && CountWarships(p) > 0)
                    .ToList();
                // Ships in flight still belong to the player, so they count toward the unlock total.
                var totalWarships = colonized.Concat(stranded)
                    .Sum(p => CountWarships(p) + p.GetIncomingShips(Ship.ShipKind.WarShip));
                var category5Unlocked = totalWarships
                    >= _constants.category5UnlockShipsPerColonizedPlanet * colonized.Count;

                var states = new List<PlanetState>();
                foreach (var planet in stranded)
                {
                    states.Add(new PlanetState
                    {
                        Planet   = planet,
                        Category = NoCategory,
                        Rank     = NoCategory,
                        Garrison = 0,
                        Docked   = CountWarships(planet),
                        Incoming = planet.GetIncomingShips(Ship.ShipKind.WarShip),
                    });
                }
                foreach (var planet in colonized)
                {
                    if (!MaintainsGarrison(planet))
                    {
                        // Spare-only: no garrison, no category, every ship here is available.
                        states.Add(new PlanetState
                        {
                            Planet   = planet,
                            Category = NoCategory,
                            Rank     = NoCategory,
                            Garrison = 0,
                            Docked   = CountWarships(planet),
                            Incoming = planet.GetIncomingShips(Ship.ShipKind.WarShip),
                        });
                        continue;
                    }

                    var categories = ApplicableCategories(planet);
                    if (!category5Unlocked && categories.Count == 1 && categories[0] == 5) continue;
                    states.Add(new PlanetState
                    {
                        Planet   = planet,
                        Category = categories.Count == 0 ? NoCategory : categories.Min(),
                        Rank     = TargetRank(planet),
                        Garrison = GarrisonOf(categories),
                        Docked   = CountWarships(planet),
                        Incoming = planet.GetIncomingShips(Ship.ShipKind.WarShip),
                    });
                }

                LastRound = _strategy == PlayerAI.AIStrategy.AIStrategyConsolidate ? 1 : ComputeRound(states);
                foreach (var state in states) state.RoundGarrison = state.Garrison * LastRound;
                LastStates = states;
                return states;
            }
```

(g) In `Plan()`, inside the `.Select(t => new ShipChoiceElement { ... })` add `Rank = t.Rank,` after `Category = t.Category,`, and replace `CompareChoices`' first lines with:

```csharp
            private static int CompareChoices(ShipChoiceElement a, ShipChoiceElement b)
            {
                var byRank = a.Rank.CompareTo(b.Rank);
                if (byRank != 0) return byRank;
```
(the rest of the method is unchanged).

- [ ] **Step 4: Verify GREEN**

`get_file_problems` (errorsOnly) on `ShipTransportPlanner.cs`, `ShipMatrix.cs`, `ShipTransportSelfCheck.cs`. Expected: no errors. (The Unity run of the self-check is at the end of Task 3.)

- [ ] **Step 5: Commit**

```bash
git add Assets/Flatspace/GameAI/ShipTransportPlanner.cs Assets/Flatspace/GameAI/ShipMatrix.cs Assets/Editor/ShipTransportSelfCheck.cs
git commit -m "feat(ai): strategy-aware ship transport planner with outer-only Consolidate garrisons

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>"
```

---

### Task 2: `AssaultPlanner` and its tunables

**Files:**
- Create: `Assets/Flatspace/GameAI/AssaultPlanner.cs` (and its `.meta`, generated by Unity)
- Modify: `Assets/Flatspace/GameAI/GameAIConstants.cs`
- Modify: `Assets/Editor/ShipTransportSelfCheck.cs`

**Interfaces:**
- Consumes: `ShipTransportPlanner.PlanetState` (`Planet`, `Spare`), `ShipTransportPlanner.LastStates`, `ShipAction`, `GameAIMap.Knowledge.KnownPlanets`, `Planet.DistanceMapToPathingList`, `Planet.GetIncomingShips`.
- Produces: `AssaultPlanner(GameAIMap map, int playerId)`; `int EnemyWarshipTotal()`; `bool IsEnemyOccupied(Planet)`; `int RequiredForce()`; `Planet ChooseTarget()`; `int Deficit(Planet target)`; `List<ShipAction> Plan(Planet target, List<ShipTransportPlanner.PlanetState> states, List<ShipAction> homeActions)`; `GameAIConstants.assaultRatio` (float), `GameAIConstants.assaultMinimumShips` (int).

- [ ] **Step 1: Write the failing self-checks**

In `NewConstants()` add before `return c;`:

```csharp
        c.assaultRatio = 1.5f;
        c.assaultMinimumShips = 3;
```

In `Run()` add after `ok &= RunConsolidatePlannerChecks();`:

```csharp
        ok &= RunAssaultChecks();
```

Add before the final closing brace of the class:

```csharp
    // A - B - E - F. A and B are player 0's; E and F are player 1's. F is two hops from B, so it is
    // unknown to player 0. Knowledge is updated for two players.
    private static GameAIMap BuildAssaultLine(GameObject go)
    {
        var map = BuildMap(go, NewConstants(),
            MakeSpawn("A", Planet.PlanetType.PlanetTypeNormal, new[] { "B" }),
            MakeSpawn("B", Planet.PlanetType.PlanetTypeNormal, new[] { "E" }),
            MakeSpawn("E", Planet.PlanetType.PlanetTypeNormal, new[] { "F" }),
            MakeSpawn("F", Planet.PlanetType.PlanetTypeNormal));
        Colonize(map, "A"); Colonize(map, "B"); Colonize(map, "E", 1); Colonize(map, "F", 1);
        return map;
    }

    public static bool RunAssaultChecks()
    {
        var ok = RunAssaultSizingCheck();
        ok &= RunAssaultTargetCheck();
        ok &= RunAssaultPlanCheck();
        return ok;
    }

    private static bool RunAssaultSizingCheck()
    {
        var ok = true;
        _nextPlanetX = 0f;
        var go = new GameObject("STSelfCheckMap_AssaultSizing");
        try
        {
            var map = BuildAssaultLine(go);
            var e = map.GetPlanet("E"); var f = map.GetPlanet("F");
            Dock(e, 4, owner: 1);
            Dock(f, 10, owner: 1);    // unknown to player 0: must not count
            Dock(e, 2, owner: -1);    // ownerless: must not count
            map.Knowledge.Update(map, numPlayers: 2);
            var assault = new AssaultPlanner(map, 0);

            ok &= Check(assault.EnemyWarshipTotal() == 4,
                "enemy total counts only known planets and only ships with a valid, different owner");
            ok &= Check(assault.RequiredForce() == 6, "required force is ceil(4 x 1.5) = 6");

            // Deficit counts my docked ships and incoming ships at the target.
            ok &= Check(assault.Deficit(e) == 6, "nothing committed yet: the deficit is the full 6");
            Dock(e, 2);
            e.AddIncomingShips(Ship.ShipKind.WarShip, 1);
            ok &= Check(assault.Deficit(e) == 3, "2 docked + 1 incoming leaves a deficit of 3");
            Dock(e, 3);
            ok &= Check(assault.Deficit(e) == 0, "5 docked + 1 incoming meets the force: deficit 0");
        }
        finally { Object.DestroyImmediate(go); }

        _nextPlanetX = 0f;
        go = new GameObject("STSelfCheckMap_AssaultFloor");
        try
        {
            var map = BuildAssaultLine(go);   // no enemy ships anywhere
            map.Knowledge.Update(map, numPlayers: 2);
            ok &= Check(new AssaultPlanner(map, 0).RequiredForce() == 3,
                "with no enemy ships the minimum of 3 applies");
        }
        finally { Object.DestroyImmediate(go); }
        return ok;
    }

    private static bool RunAssaultTargetCheck()
    {
        var ok = true;

        // No holders / no known enemy -> no target.
        _nextPlanetX = 0f;
        var go = new GameObject("STSelfCheckMap_AssaultNoTarget");
        try
        {
            var map = BuildAssaultLine(go);
            map.Knowledge.Update(map, numPlayers: 2);
            ok &= Check(new AssaultPlanner(map, 0).ChooseTarget() == null,
                "no warships anywhere: no target");
            Dock(map.GetPlanet("B"), 3);
            ok &= Check(new AssaultPlanner(map, 0).ChooseTarget() == map.GetPlanet("E"),
                "warships at B and a known enemy-occupied E: E is the target");
            ok &= Check(new AssaultPlanner(map, 0).ChooseTarget() != map.GetPlanet("F"),
                "F is unknown to player 0 and never a candidate");
        }
        finally { Object.DestroyImmediate(go); }

        // Two enemy planets off B: nearest first, then sticky once ships are committed.
        _nextPlanetX = 0f;
        go = new GameObject("STSelfCheckMap_AssaultSticky");
        try
        {
            var map = BuildMap(go, NewConstants(),
                MakeSpawn("A", Planet.PlanetType.PlanetTypeNormal, new[] { "B" }),
                MakeSpawn("B", Planet.PlanetType.PlanetTypeNormal, new[] { "E1", "E2" }),
                MakeSpawn("E1", Planet.PlanetType.PlanetTypeNormal),
                MakeSpawn("E2", Planet.PlanetType.PlanetTypeNormal));
            Colonize(map, "A"); var b = Colonize(map, "B");
            var e1 = Colonize(map, "E1", 1); var e2 = Colonize(map, "E2", 1);
            Dock(b, 2);
            map.Knowledge.Update(map, numPlayers: 2);
            var assault = new AssaultPlanner(map, 0);

            ok &= Check(assault.ChooseTarget() == e1, "nothing committed: the cheapest path (E1) wins");
            Dock(e2, 1);
            ok &= Check(assault.ChooseTarget() == e2, "sticky: a planet with my ships already docked beats a cheaper one");
            b.UndockShips(Ship.ShipKind.WarShip, 0, 2);
            ok &= Check(assault.ChooseTarget() == e2,
                "sticky even when E2 holds ALL my warships (no other holder to path from)");
        }
        finally { Object.DestroyImmediate(go); }
        return ok;
    }

    private static bool RunAssaultPlanCheck()
    {
        var ok = true;

        // Spare ships beyond B's outer garrison (10 - 6 = 4) and all of interior A (5) go to E: cheapest first.
        _nextPlanetX = 0f;
        var go = new GameObject("STSelfCheckMap_AssaultPlan");
        try
        {
            var map = BuildAssaultLine(go);
            Dock(map.GetPlanet("E"), 4, owner: 1);
            Dock(map.GetPlanet("B"), 10); Dock(map.GetPlanet("A"), 5);
            map.Knowledge.Update(map, numPlayers: 2);
            var assault = new AssaultPlanner(map, 0);
            var target = assault.ChooseTarget();
            var transport = ConsolidatePlanner(map, target.PlanetName);
            var home = transport.Plan();
            ok &= Check(home.Count == 0, "B's outer garrison is full and A holds none: no home actions");

            var actions = assault.Plan(target, transport.LastStates, home);
            ok &= Check(actions.Count == 2
                        && actions.Exists(a => a.Origin == "B" && a.Target == "E" && a.Count == 4)
                        && actions.Exists(a => a.Origin == "A" && a.Target == "E" && a.Count == 2),
                "required 6 = B's 4 spare (cheapest path) + 2 of A's 5");
        }
        finally { Object.DestroyImmediate(go); }

        // Ships an outer garrison needs are not also committed to the assault.
        _nextPlanetX = 0f;
        go = new GameObject("STSelfCheckMap_AssaultAfterHome");
        try
        {
            var map = BuildAssaultLine(go);
            Dock(map.GetPlanet("E"), 4, owner: 1);
            Dock(map.GetPlanet("B"), 3); Dock(map.GetPlanet("A"), 9);
            map.Knowledge.Update(map, numPlayers: 2);
            var assault = new AssaultPlanner(map, 0);
            var target = assault.ChooseTarget();
            var transport = ConsolidatePlanner(map, target.PlanetName);
            var home = transport.Plan();
            ok &= Check(home.Count == 1 && home[0].Origin == "A" && home[0].Target == "B" && home[0].Count == 3,
                "home first: A sends 3 to fill outer B's garrison of 6");

            var actions = assault.Plan(target, transport.LastStates, home);
            ok &= Check(actions.Count == 1 && actions[0].Origin == "A" && actions[0].Target == "E" && actions[0].Count == 6,
                "the assault gets only A's remaining 6 (9 - 3), never the ships home defence claimed");
        }
        finally { Object.DestroyImmediate(go); }
        return ok;
    }
```

- [ ] **Step 2: Verify RED**

`get_file_problems` (errorsOnly) on `Assets/Editor/ShipTransportSelfCheck.cs`. Expected: `Cannot resolve symbol 'AssaultPlanner'`, `'assaultRatio'`, `'assaultMinimumShips'`.

- [ ] **Step 3: Implement**

In `GameAIConstants.cs`, after `category5UnlockShipsPerColonizedPlanet`, add:

```csharp

    [Header("Assault (Consolidate)")]
    // Force committed to a known enemy-occupied planet: ceil(enemy known docked warships x this)...
    public float assaultRatio = 1.5f;
    // ...but never fewer than this.
    public int assaultMinimumShips = 3;
```

Create `Assets/Flatspace/GameAI/AssaultPlanner.cs`:

```csharp
// AssaultPlanner.cs
using System;
using System.Collections.Generic;
using System.Linq;

namespace FlatSpace
{
    namespace AI
    {
        /// <summary>
        /// Consolidate only. Picks a known enemy-occupied planet, sizes the force to the enemy's known
        /// docked fleet, and sends the ships home defence left spare. Pure with respect to the
        /// simulation and free of Gameboard.Instance so the Editor self-check can drive it directly.
        /// </summary>
        public class AssaultPlanner
        {
            private readonly GameAIMap _map;
            private readonly int _playerId;
            private readonly GameAIConstants _constants;

            public AssaultPlanner(GameAIMap map, int playerId)
            {
                _map = map;
                _playerId = playerId;
                _constants = map.GameAIConstants;
            }

            private int CountWarships(Planet planet)
                => planet.DockedShips.Count(s => s.Kind == Ship.ShipKind.WarShip && s.Owner == _playerId);

            /// <summary>Another player's population is present (same test as PlayerKnowledge.HasContact).</summary>
            public bool IsEnemyOccupied(Planet planet)
                => planet.Population.Exists(p => p.Player != _playerId);

            /// <summary>
            /// Enemy warships docked on planets THIS player knows. Ownerless ships (Owner &lt; 0) are not
            /// an enemy fleet, and unknown planets are outside the AI's view.
            /// </summary>
            public int EnemyWarshipTotal()
            {
                var total = 0;
                foreach (var name in _map.Knowledge.KnownPlanets(_playerId))
                {
                    var planet = _map.GetPlanet(name);
                    if (planet == null) continue;
                    total += planet.DockedShips.Count(s => s.Kind == Ship.ShipKind.WarShip
                                                           && s.Owner >= 0 && s.Owner != _playerId);
                }
                return total;
            }

            public int RequiredForce()
                => Math.Max(_constants.assaultMinimumShips,
                    (int)Math.Ceiling(EnemyWarshipTotal() * _constants.assaultRatio));

            /// <summary>Ships still needed at the target: required force minus my docked + incoming there.</summary>
            public int Deficit(Planet target)
                => Math.Max(0, RequiredForce()
                               - (CountWarships(target) + target.GetIncomingShips(Ship.ShipKind.WarShip)));

            // Mirrors ShipTransportPlanner.IsUsablePath: FindPath returns a 1-node zero-cost "path"
            // (it does not throw) when no route exists, so NumNodes < 2 means unreachable.
            private bool IsUsablePath(GameAIMap.DestinationToPathingListEntry entry)
                => entry.NumNodes >= 2 && entry.NumNodes <= _constants.maxPathNodesForShipTransport;

            private float? CheapestPathCost(List<Planet> holders, Planet target)
            {
                float? best = null;
                foreach (var holder in holders)
                {
                    if (holder == target) continue;
                    if (holder.DistanceMapToPathingList.TryGetValue(target.PlanetName, out var entry)
                        && IsUsablePath(entry)
                        && (best == null || entry.Cost < best.Value))
                        best = entry.Cost;
                }
                return best;
            }

            /// <summary>
            /// The known, reachable, enemy-occupied planet where I already have the most warships
            /// docked + incoming (sticky); otherwise the cheapest path from a planet holding warships;
            /// ties by name. Null when there is no such planet.
            /// </summary>
            public Planet ChooseTarget()
            {
                var holders = _map.PlanetList.Where(p => CountWarships(p) > 0).ToList();
                if (holders.Count == 0) return null;

                Planet best = null;
                var bestOwn = -1;
                var bestCost = float.MaxValue;
                foreach (var name in _map.Knowledge.KnownPlanets(_playerId).OrderBy(n => n, StringComparer.Ordinal))
                {
                    var planet = _map.GetPlanet(name);
                    if (planet == null || !IsEnemyOccupied(planet)) continue;

                    var own = CountWarships(planet) + planet.GetIncomingShips(Ship.ShipKind.WarShip);
                    // Ships already committed make a target valid without needing another holder to path from.
                    var cost = own > 0 ? 0f : CheapestPathCost(holders, planet);
                    if (cost == null) continue;

                    if (own > bestOwn || (own == bestOwn && cost.Value < bestCost))
                    {
                        best = planet;
                        bestOwn = own;
                        bestCost = cost.Value;
                    }
                }
                return best;
            }

            private struct Source
            {
                public string Name;
                public int    Remaining;
                public float  Cost;
            }

            /// <summary>
            /// Sends spare ships to the target, cheapest path first, until the deficit is met. A source's
            /// spare is its state's Spare minus what the home actions already send from it.
            /// </summary>
            public List<ShipAction> Plan(Planet target, List<ShipTransportPlanner.PlanetState> states,
                List<ShipAction> homeActions)
            {
                var actions = new List<ShipAction>();
                if (target == null) return actions;
                var deficit = Deficit(target);
                if (deficit <= 0) return actions;

                var sentByOrigin = homeActions
                    .GroupBy(a => a.Origin)
                    .ToDictionary(g => g.Key, g => g.Sum(a => a.Count));

                var sources = new List<Source>();
                foreach (var state in states)
                {
                    if (state.Planet == target) continue;
                    sentByOrigin.TryGetValue(state.Planet.PlanetName, out var sent);
                    var remaining = state.Spare - sent;
                    if (remaining <= 0) continue;
                    if (!state.Planet.DistanceMapToPathingList.TryGetValue(target.PlanetName, out var entry)
                        || !IsUsablePath(entry)) continue;
                    sources.Add(new Source { Name = state.Planet.PlanetName, Remaining = remaining, Cost = entry.Cost });
                }

                foreach (var source in sources
                             .OrderBy(s => s.Cost)
                             .ThenBy(s => s.Name, StringComparer.Ordinal))
                {
                    var count = Math.Min(source.Remaining, deficit);
                    actions.Add(new ShipAction
                    {
                        Origin = source.Name,
                        Target = target.PlanetName,
                        Cost   = source.Cost,
                        Count  = count,
                        Kind   = Ship.ShipKind.WarShip,
                    });
                    deficit -= count;
                    if (deficit <= 0) break;
                }
                return actions;
            }
        }
    }
}
```

- [ ] **Step 4: Verify GREEN**

`get_file_problems` (errorsOnly) on `AssaultPlanner.cs`, `GameAIConstants.cs`, `ShipTransportSelfCheck.cs`. Expected: no errors.

- [ ] **Step 5: Commit**

Unity generates `AssaultPlanner.cs.meta` when the Editor next focuses; if it exists, stage it too (check with `git status`). If it does not exist yet, commit without it and add it in Task 3's commit.

```bash
git add Assets/Flatspace/GameAI/AssaultPlanner.cs Assets/Flatspace/GameAI/GameAIConstants.cs Assets/Editor/ShipTransportSelfCheck.cs
git add Assets/Flatspace/GameAI/AssaultPlanner.cs.meta 2>/dev/null || true
git commit -m "feat(ai): add AssaultPlanner and assault tunables

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>"
```

---

### Task 3: Orchestration, logging, and the end-to-end check

**Files:**
- Modify: `Assets/Flatspace/GameAI/PlayerAI.cs` (`ProcessShipActions`, lines 682-691)
- Modify: `Assets/Flatspace/Diagnostics/AITuningLogger.cs`
- Modify: `Assets/Editor/ShipTransportSelfCheck.cs`

**Interfaces:**
- Consumes: `ShipTransportPlanner(map, id, strategy) { HeldPlanet }`, `.Plan()`, `.LastStates`; `AssaultPlanner.ChooseTarget/Plan/RequiredForce`.
- Produces: `public List<ShipAction> PlayerAI.PlanShipActions(int turnNumber)`; `public static void AITuningLogger.LogAssaultTarget(int turnNumber, int playerId, string target, int requiredForce)`.

- [ ] **Step 1: Write the failing self-check**

In `Run()` add after `ok &= RunAssaultChecks();`:

```csharp
        ok &= RunConsolidatePlanShipActionsCheck();
```

Add before the final closing brace of the class:

```csharp
    public static bool RunConsolidatePlanShipActionsCheck()
    {
        var ok = true;
        _nextPlanetX = 0f;
        var mapGo = new GameObject("STSelfCheckMap_PlanShipActions");
        var playerGo = new GameObject("STSelfCheckPlayer_PlanShipActions");
        try
        {
            var map = BuildAssaultLine(mapGo);
            Dock(map.GetPlanet("E"), 4, owner: 1);
            Dock(map.GetPlanet("B"), 10); Dock(map.GetPlanet("A"), 5);
            map.Knowledge.Update(map, numPlayers: 2);

            var player = playerGo.AddComponent<Player>();
            var playerAI = playerGo.AddComponent<PlayerAI>();
            playerAI.Player = player;
            playerAI.AIMap = map;
            player.playerID = 0;

            playerAI.Strategy = PlayerAI.AIStrategy.AIStrategyConsolidate;
            var actions = playerAI.PlanShipActions(0);
            ok &= Check(actions.Count == 2
                        && actions.Exists(a => a.Origin == "B" && a.Target == "E" && a.Count == 4)
                        && actions.Exists(a => a.Origin == "A" && a.Target == "E" && a.Count == 2),
                "Consolidate: PlanShipActions sends the assault force to the enemy planet E");

            playerAI.Strategy = PlayerAI.AIStrategy.AIStrategyExpand;
            ok &= Check(!playerAI.PlanShipActions(0).Exists(a => a.Target == "E"),
                "Expand never targets an enemy-occupied planet");
        }
        finally
        {
            Object.DestroyImmediate(playerGo);
            Object.DestroyImmediate(mapGo);
        }
        return ok;
    }
```

- [ ] **Step 2: Verify RED**

`get_file_problems` (errorsOnly) on `ShipTransportSelfCheck.cs`. Expected: `Cannot resolve symbol 'PlanShipActions'`.

- [ ] **Step 3: Implement**

In `AITuningLogger.cs`, after `LogStrategyChange` add:

```csharp
    public static void LogAssaultTarget(int turnNumber, int playerId, string target, int requiredForce)
    {
        if (_currentLogPath == null) return;
        AppendLines(new List<string> { FormatLine(turnNumber, playerId, "AssaultTarget", target, requiredForce.ToString()) });
    }
```

In `PlayerAI.cs`, replace `ProcessShipActions` (lines 682-691, including its summary comment) with:

```csharp
            /// <summary>
            /// Moves warships. The decisions live in ShipTransportPlanner (home garrisons) and, under
            /// Consolidate, AssaultPlanner; this turns each resulting ShipAction into orders.
            /// </summary>
            public void ProcessShipActions(List<GameAI.GameAIOrder> orders)
            {
                // Self-checks call this with no Gameboard in the scene.
                var turn = Gameboard.Instance != null ? Gameboard.Instance.TurnNumber : 0;
                foreach (var action in PlanShipActions(turn))
                    EmitShipOrders(action, orders);
            }

            private string _lastLoggedAssaultTarget;

            /// <summary>
            /// Expand: the home garrison plan, unchanged. Consolidate: choose the assault target, plan
            /// home defence with those ships held out, then send whatever is still spare to the target.
            /// Public (and free of Gameboard.Instance) so the self-check can drive it directly.
            /// </summary>
            public List<ShipAction> PlanShipActions(int turnNumber)
            {
                if (Strategy != AIStrategy.AIStrategyConsolidate)
                    return new ShipTransportPlanner(AIMap, Player.playerID).Plan();

                var assault = new AssaultPlanner(AIMap, Player.playerID);
                var target = assault.ChooseTarget();

                var targetName = target?.PlanetName;
                if (targetName != null && targetName != _lastLoggedAssaultTarget)
                    AITuningLogger.LogAssaultTarget(turnNumber, Player.playerID, targetName, assault.RequiredForce());
                _lastLoggedAssaultTarget = targetName;

                var transport = new ShipTransportPlanner(AIMap, Player.playerID, Strategy)
                {
                    HeldPlanet = targetName,
                };
                var actions = transport.Plan();
                actions.AddRange(assault.Plan(target, transport.LastStates, actions));
                return actions;
            }
```

- [ ] **Step 4: Verify GREEN, then the user's real run**

`get_file_problems` (errorsOnly) on `PlayerAI.cs`, `AITuningLogger.cs`, `ShipTransportSelfCheck.cs`: no errors.

Then ask the user to focus the Unity Editor (check the Console for compile errors first) and run, in this order: `FlatSpace -> AI -> Run Ship Transport Self-Check`, `Run Player Knowledge Self-Check`, `Run PlayerAI Resource Self-Check`. Expected: each ends in `ALL PASSED` with no `FAIL` lines. Every pre-existing `ShipTransportSelfCheck` assertion must still pass (Expand is unchanged).

- [ ] **Step 5: Commit**

```bash
git status --short   # confirm AssaultPlanner.cs.meta now exists
git add Assets/Flatspace/GameAI/PlayerAI.cs Assets/Flatspace/Diagnostics/AITuningLogger.cs Assets/Editor/ShipTransportSelfCheck.cs Assets/Flatspace/GameAI/AssaultPlanner.cs.meta
git commit -m "feat(ai): orchestrate Consolidate home defence and assault, log assault target

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>"
```
(If the `.meta` was already committed in Task 2, omit it here.)

- [ ] **Step 6: Play-mode check (user)**

With `_logAIEvents` on in `MainMenu`, run a two-player match past first contact. Expected in the newest `AITuningLogs/` file: an `AssaultTarget|<planet>|<required>` line for a Consolidate player once it has warships and a known enemy planet, followed by `ShipMove`/`ShipArrive` lines toward that planet; Expand players never produce `AssaultTarget`; the AI keeps colonizing and producing. Turn the toggle back off.

---

### Task 4: Documentation

**Files:**
- Modify: `CLAUDE.md` (Ship Transport section)
- Modify: `FUTURE_FEATURES.md`

**Interfaces:**
- Consumes: the behavior shipped in Tasks 1-3.
- Produces: nothing code-facing.

- [ ] **Step 1: Update `CLAUDE.md`**

In the **Ship Transport** section:

- In the **Categories** bullet, change "2 outer (has an uncolonized neighbour)" to "2 outer (has a neighbour not colonized by this player, whether empty, enemy-held or contested)".
- After the **Tunables** bullet (before the line naming `ShipTransportSelfCheck.cs`), add:

```markdown
- **Consolidate:** `ShipTransportPlanner` takes an optional strategy (default Expand = the rules above,
  unchanged). Under Consolidate `MaintainsGarrison(planet)` is true only for outer planets, so only they
  garrison, at round 1 only; every other ship is spare, including on category-5-only planets that would be
  locked under Expand. Change `MaintainsGarrison` to garrison non-outer planets later. `TargetRank` sorts
  outer planets first (0 + category, others 100 + category; Expand: rank = category). `HeldPlanet` (the
  assault target) removes that planet's ships from the stranded set so the assault force is not sent home.
  `AssaultPlanner` (`Assets/Flatspace/GameAI/AssaultPlanner.cs`) then picks one known enemy-occupied planet
  (sticky: most of my ships docked/incoming there, else cheapest path), sizes the force to
  `max(assaultMinimumShips, ceil(known enemy docked warships x assaultRatio))` (unknown planets and
  ownerless ships are not counted), and sends the ships home defence left spare, cheapest path first.
  `PlayerAI.PlanShipActions` orchestrates both; ships trickle to the target rather than launching together
  (revisit when combat exists). New tunables `assaultRatio`, `assaultMinimumShips` have in-code defaults.
```

- In the **AI Tuning Log** paragraph's event list, add `assault target chosen as AssaultTarget|<planet>|<required>` after the `StrategyChange` mention.

- [ ] **Step 2: Update `FUTURE_FEATURES.md`**

Append to the AIStrategyConsolidate line (after the existing "First cycle done" note): ` *Second cycle done: outer-only garrisons and assault targeting under Consolidate. Remaining: production/fleet-build weights, strategic colonization, planet defense.*`

- [ ] **Step 3: Commit**

```bash
git add CLAUDE.md FUTURE_FEATURES.md
git commit -m "docs: document Consolidate outer-only garrisons and assault targeting

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>"
```
