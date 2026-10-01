# Blockade Breaking Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Under Consolidate, the AI sends enough warship offense to a planet blockaded against it that the blockade value falls to 0 or less, researches warship offense faster while blockaded, and logs the whole episode.

**Architecture:** `AssaultPlanner` stays the one planner. It gains an optional `BlockadeView`, `WarshipStats`, `BlockadeMemory` and turn; `ChooseTarget` first ranks blockaded planets (committed offense, recent cut, smallest need, path cost, name) and falls back to the existing enemy-occupied rule; `Plan` sizes a blockade force by real per-ship offense. In-flight offense is a derived per-planet figure recomputed once per turn from the in-flight ship orders. A pure `BlockadeTargetTracker` turns target changes into log lines, and a research situational multiplier boosts the Warship Offense line.

**Tech Stack:** Unity 6000.4.1f1, C#, ScriptableObject tunables (`GameAIConstants`), Editor self-checks under `Assets/Editor/` (no test framework).

**Spec:** `docs/superpowers/specs/2026-10-01-blockade-breaking-design.md` (commit `d69ae00`). Roadmap entry: `FUTURE_FEATURES.md`, "AI avoids blockaded routes and uses blockades", item (3). Read `CLAUDE.md` first (sections "Warships and Blockade", "Ship Transport", "AI Tuning Log", the self-check pattern).

## Global Constraints

- Consolidate only; no change to Expand behaviour. An `AssaultPlanner` built with the old two-argument constructor must behave exactly as before (existing `ShipTransportSelfCheck` assault checks must keep passing unchanged).
- One sticky target: blockaded planets rank first, then the existing enemy-occupied rule (spec decisions 1, 2).
- Ranking, first difference wins: (1) most of my offense committed (docked + incoming) descending, (2) cut one of my orders within `blockadeTargetRecentTurns` turns, (3) smallest offense still needed ascending, (4) cheapest path from a holder of my warships, (5) planet name ordinal.
- `needed = value x (1 + blockadeBreakMargin) - incomingOffense`; covered when `needed <= 0`.
- Tunables on `GameAIConstants`, all with in-code defaults: `blockadeTargetRecentTurns` = 5, `blockadeBreakMargin` = 0.1, `blockadedOffenseResearchBoost` = 2.
- Research multiplier applies only to items whose `effect` is `Warship Offense` (`WarshipStats.OffenseKey`), after `NormalizeResearchWeightsBySubtype`, only while the player's `BlockadeView` holds any blockade. It is a situational multiplier, never a table value (session configuration rule 3).
- Log lines: `BlockadeTarget|<planet>|<blocker>|<value>|<neededOffense>|<Committed, RecentCut or Cheapest>`, `BlockadeForce|<planet>|<ships>|<offense>|<stillNeeded>`, `BlockadeTargetEnd|<planet>|<Cleared, Switched or Unreachable>|<turnsHeld>`, `OffenseResearchBoost|<item>|<multiplier>`. `<blocker>` is `-1` for a remembered, unseen planet.
- No new saved state. `AssaultPlanner`, `BlockadeTargetTracker` and the new self-check must never touch `Gameboard.Instance`.
- A method a self-check calls directly is `public` (never `internal`). Every planet in a self-check map needs a distinct position (the A* tie-break note in `CLAUDE.md`). Never put an assertion exactly on the margin boundary (float caveat in `CLAUDE.md`).
- A new script's `.meta` file is committed with it. Stage explicit paths only. Commit only on the `blockade-breaking` branch; never push or merge unless asked.
- Namespace and style: match the file being edited (`FlatSpace.AI` for `Assets/Flatspace/GameAI/*`; `Planet.cs` is in the global namespace and writes `System.Math`).
- Verification: Unity cannot be run from Claude Code here. RED is "Rider `get_file_problems` reports errors for the missing members" (a brand-new file reports "not included in any project" until Unity regenerates it, so for new files ask the user to focus the Editor). GREEN is the user focusing the Editor (recompile, check the Console) and running `FlatSpace -> AI -> Run Blockade Breaking Self-Check`, then `Run All AI Self-Checks`.

## Proposed tuning log output

Required by `docs/session_configuration.md` section 4. All lines use `T<turn>|P<playerId>|<EventCode>|<fields...>`, are
written by `AITuningLogger` (no-ops when no match is logging), and are added in Task 4 (Task 5 for the last one).

| Line | Logged when | Question it answers | Repeat control, test |
|---|---|---|---|
| `BlockadeTarget\|<planet>\|<blocker>\|<value>\|<neededOffense>\|<Committed, RecentCut or Cheapest>` | the blockade target changes | which planet did the assault pick, against whom, how much offense does it need, and which ranking step decided it (is the recent-cut step ever the deciding one?) | `BlockadeTargetTracker` start transition; `RunTrackerCheck` |
| `BlockadeForce\|<planet>\|<ships>\|<offense>\|<stillNeeded>` | each turn ships are sent at a blockade target | how much of the need does each wave cover; is `blockadeBreakMargin` too small (waves end with `stillNeeded > 0` and the blockade stays) or too large | once per turn a force is sent, so it is naturally sparse; `RunPlanShipActionsCheck` covers the send, the field values are covered by `RunBlockadePlanCheck` through `LastBlockadeForce` |
| `BlockadeTargetEnd\|<planet>\|<Cleared, Switched or Unreachable>\|<turnsHeld>` | a blockade target stops being the target | how long does clearing take; are targets abandoned (`Switched`, `Unreachable`) before they clear | `BlockadeTargetTracker` end transition; `RunTrackerCheck` |
| `OffenseResearchBoost\|<item>\|<multiplier>` | a research start picks a Warship Offense item while a blockade is visible | does the boost actually change which research is picked, and does it show only while blockaded | once per research start (not per turn); not self-checkable (needs `Gameboard`), verified in Play mode |

Existing lines these read alongside: `Blockade`, `OrderBlocked`, `BlockadeLearned`, `AssaultTarget` (now logged only for a
non-blockade target), `WarshipBoost`, `ShipMove`, `ShipArrive`. The `tuning-log` skill and the "AI Tuning Log" section of
`CLAUDE.md` are updated in Task 6 (new codes plus the analyses listed there).

## Deviation from the spec (read before Task 1)

The spec says incoming offense is "maintained at the same three places" as the incoming ship counter. This plan instead recomputes it once per turn from the in-flight `OrderTypeShipTransport` orders (`GameAIMap.RecomputeIncomingOffense`, called in `GameAI.GameAIUpdate` just before `ProcessResults`). The planner sees exactly the same set of ships the counter shows at that moment (the counter changes only when a ship-transfer-in-progress order executes, at the end of the previous turn, and the order is then in `CurrentAIOrders`), it cannot drift, and it needs no arrival or load bookkeeping and no stats inside the static `ApplyShip*` methods. Task 1 updates the spec text to match.

## Review Focus

Failure modes the spec implies that no task's happy-path tests exercise; each has a pinned test in the task named.

1. A blockaded planet that is only remembered (no blocker known, `Planet.NoOwner`, value from memory): must be a valid target and never throw (Task 2, `BlockadeView.WithValues`).
2. A blockaded planet with no usable path from any holder of my warships: must not be a candidate, so the fallback runs (Task 2, isolated planet `Z`).
3. In-flight orders with a null `Fleet` or a ColonyShip fleet: must add no incoming offense and not throw (Task 1).
4. Warship stats unavailable (planner built with null `WarshipStats`, so every ship has offense 0): `Plan` must send all spare ships and terminate, not loop (Task 3).
5. An `AssaultPlanner` built without a view (the old constructor, and every existing caller): no blockade candidates, behaviour unchanged (Task 2).

## File Structure

| File | Change | Responsibility |
|---|---|---|
| `Assets/Flatspace/GameAI/GameAIConstants.cs` | modify | three new tunables |
| `Assets/Flatspace/Objects/Planets/Planet.cs` | modify | derived `GetIncomingOffense`/`AddIncomingOffense`/`ClearIncomingOffense` |
| `Assets/Flatspace/GameAI/GameAIMap.cs` | modify | `RecomputeIncomingOffense(orders, stats)` |
| `Assets/Flatspace/GameAI/GameAI.cs` | modify | call it before `ProcessResults` |
| `Assets/Flatspace/GameAI/AssaultPlanner.cs` | modify | blockade candidates, ranking, need, offense-sized `Plan` |
| `Assets/Flatspace/GameAI/BlockadeTargetTracker.cs` (+ `.meta`) | create | pure target start/end transitions for the log |
| `Assets/Flatspace/Diagnostics/AITuningLogger.cs` | modify | four new `Log*` methods |
| `Assets/Flatspace/GameAI/PlayerAI.cs` | modify | wiring in `PlanShipActions`, tracker state, research multiplier |
| `Assets/Editor/BlockadeBreakSelfCheck.cs` (+ `.meta`) | create | the subsystem self-check |
| `Assets/Editor/AllAISelfChecks.cs` | modify | register the new suite |
| `CLAUDE.md`, `FUTURE_FEATURES.md`, `.claude/skills/tuning-log/SKILL.md`, the spec | modify | docs |

---

### Task 1: Tunables and derived incoming offense

**Files:**
- Modify: `Assets/Flatspace/GameAI/GameAIConstants.cs` (after the "Blockade response" fields, near line 55)
- Modify: `Assets/Flatspace/Objects/Planets/Planet.cs` (after `ClearIncomingShips`, near line 1046)
- Modify: `Assets/Flatspace/GameAI/GameAIMap.cs` (after `RecomputeIncomingShips`, near line 266)
- Modify: `Assets/Flatspace/GameAI/GameAI.cs` (`GameAIUpdate`, before `ProcessResults`, near line 134)
- Modify: `docs/superpowers/specs/2026-10-01-blockade-breaking-design.md` (section 2, "Incoming offense")
- Create: `Assets/Editor/BlockadeBreakSelfCheck.cs` (+ `.meta`)
- Modify: `Assets/Editor/AllAISelfChecks.cs`

**Interfaces:**
- Produces: `GameAIConstants.blockadeTargetRecentTurns` (int), `.blockadeBreakMargin` (float), `.blockadedOffenseResearchBoost` (float); `Planet.GetIncomingOffense(Ship.ShipKind kind, int owner)` returns float; `Planet.AddIncomingOffense(Ship.ShipKind kind, int owner, float delta)`; `Planet.ClearIncomingOffense()`; `GameAIMap.RecomputeIncomingOffense(List<GameAI.GameAIOrder> orders, WarshipStats stats)`; the self-check's `Scenario` class (`Line()`, `Fork()`, `Ships`, `Dock`, `View`, `Planner`) that later tasks extend.
- Consumes: `WarshipStats.Offense(ShipData, ICollection<string>)`, `GameAIConstants.warShipData`, `BlockadeSystem.ResearchItemsFrom`.

- [ ] **Step 1: Write the self-check file with the incoming-offense check (RED)**

Create `Assets/Editor/BlockadeBreakSelfCheck.cs`. The `Scenario` class is shared by every later task; the planner/AI members are used from Task 2 on but written now.

```csharp
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using FlatSpace.AI;
using Flatspace.Objects.Production;

public static class BlockadeBreakSelfCheck
{
    [MenuItem("FlatSpace/AI/Run Blockade Breaking Self-Check")]
    public static void Run() => RunChecks();

    public static bool RunChecks()
    {
        var ok = RunIncomingOffenseCheck();
        Debug.Log(ok
            ? "[BlockadeBreakSelfCheck] ALL PASSED"
            : "[BlockadeBreakSelfCheck] FAILURES (see errors above)");
        return ok;
    }

    private static bool Check(bool condition, string label)
    {
        if (!condition) Debug.LogError($"[BlockadeBreakSelfCheck] FAIL: {label}");
        return condition;
    }

    private static bool Near(float a, float b) => Mathf.Abs(a - b) < 0.01f;

    // Every planet needs a distinct position (PathingSystem.FindPath tie-break fragility).
    private static PlanetSpawnData Spawn(string name, float x, float y, IEnumerable<string> connections = null)
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

    // Warship template: offense 10 (max 30); MakeResearch has five Offense tiers, so each tier a ship carries adds 4.
    // An empty snapshot is offense 10, {"Off 1"} is 14, {"Off 1","Off 2"} is 18.
    private static readonly string[] None = new string[0];
    private static readonly string[] Off1 = { "Off 1" };
    private static readonly string[] Off12 = { "Off 1", "Off 2" };

    private sealed class Scenario : System.IDisposable
    {
        public GameObject MapGo, PlayerGo;
        public GameAIMap Map;
        public PlayerAI AI;
        public GameAIConstants Constants;
        public ShipData Template;
        public List<CatalogItem> Research;
        public BlockadeMemory Memory = new BlockadeMemory();

        private static Scenario Create(string name)
        {
            var s = new Scenario();
            s.Template = WarshipSelfCheck.MakeTemplate();
            s.Constants = WarshipSelfCheck.MakeConstants(s.Template);
            s.Constants.defaultTravelSpeed = 1f;
            s.Constants.maxPathNodesForShipTransport = 10;
            s.Constants.maxPathNodesForKnowledge = 6;
            s.Constants.blockadeMemoryTurns = 20;
            s.Constants.garrisonOuter = 6;
            s.Research = WarshipSelfCheck.MakeResearch();
            s.MapGo = new GameObject("BBSelfCheckMap_" + name);
            s.PlayerGo = new GameObject("BBSelfCheckPlayer_" + name);
            return s;
        }

        private void Finish(params string[] playerZeroPlanets)
        {
            foreach (var planet in playerZeroPlanets) Colonize(planet, 0);
            var player = PlayerGo.AddComponent<Player>();
            AI = PlayerGo.AddComponent<PlayerAI>();
            AI.Player = player;
            AI.AIMap = Map;
            player.playerID = 0;
            AI.ResearchCatalog = PlayerGo.AddComponent<Catalog>();
            AI.ResearchCatalog.catalogItems = Research;
            Map.Knowledge.Update(Map, 2, 6);
        }

        // A(0,0) - B(100,0) - C(200,0) - D(300,0), plus an unconnected Z(900,0). Player 0 holds A and B, player 1 holds D.
        public static Scenario Line()
        {
            var s = Create("Line");
            s.Map = s.MapGo.AddComponent<GameAIMap>();
            s.Map.GameAIMapInit(new List<PlanetSpawnData>
            {
                Spawn("A", 0f, 0f, new[] { "B" }),
                Spawn("B", 100f, 0f, new[] { "A", "C" }),
                Spawn("C", 200f, 0f, new[] { "B", "D" }),
                Spawn("D", 300f, 0f, new[] { "C" }),
                Spawn("Z", 900f, 0f),
            }, s.Constants);
            s.Colonize("D", 1);
            s.Finish("A", "B");
            return s;
        }

        // A(0,0) - B(100,0); B - C1(200,0); B - C2(100,150). Player 0 holds A and B.
        public static Scenario Fork()
        {
            var s = Create("Fork");
            s.Map = s.MapGo.AddComponent<GameAIMap>();
            s.Map.GameAIMapInit(new List<PlanetSpawnData>
            {
                Spawn("A", 0f, 0f, new[] { "B" }),
                Spawn("B", 100f, 0f, new[] { "A", "C1", "C2" }),
                Spawn("C1", 200f, 0f, new[] { "B" }),
                Spawn("C2", 100f, 150f, new[] { "B" }),
            }, s.Constants);
            s.Finish("A", "B");
            return s;
        }

        public Planet P(string name) => Map.GetPlanet(name);

        public void Colonize(string name, int player)
        {
            var planet = Map.GetPlanet(name);
            planet.Owner = player;
            planet.Population.Add(new Planet.Inhabitant { Player = player });
        }

        public void Dock(string planet, int owner, params string[][] snapshots)
        {
            foreach (var snapshot in snapshots)
                P(planet).DockShipFromSave(Ship.ShipKind.WarShip, owner, new List<string>(snapshot));
        }

        public void Ships(string planet, int owner, int count)
        {
            for (var i = 0; i < count; i++) Dock(planet, owner, None);
        }

        public BlockadeView View(int turn)
            => BlockadeView.Build(Map, 0, new BlockadeSystem(Map, Research), Memory, turn, Constants.blockadeMemoryTurns);

        public AssaultPlanner Planner(BlockadeView view, int turn)
            => new AssaultPlanner(Map, 0, view, new WarshipStats(Research), Memory, turn);

        public void Dispose()
        {
            WarshipSelfCheck.DestroyAll(Research);
            Object.DestroyImmediate(MapGo);
            Object.DestroyImmediate(PlayerGo);
            Object.DestroyImmediate(Constants);
            Object.DestroyImmediate(Template);
        }
    }

    private static GameAI.GameAIOrder ShipOrder(string target, int owner, Ship.ShipKind kind, params string[][] snapshots)
        => new GameAI.GameAIOrder
        {
            Type = GameAI.GameAIOrder.OrderType.OrderTypeShipTransport,
            Target = target,
            PlayerId = owner,
            Data = snapshots.Length,
            Fleet = new GameAI.GameAIOrder.ShipFleetPayload
            {
                Kind = kind,
                Snapshots = snapshots.Select(x => new List<string>(x)).ToList(),
            },
        };

    // In-flight offense is recomputed from the in-flight ship orders: real per-ship offense from each snapshot, per
    // (kind, owner), warships only; a null fleet and a ColonyShip fleet add nothing, and recomputing never accumulates.
    public static bool RunIncomingOffenseCheck()
    {
        var ok = true;
        using (var s = Scenario.Line())
        {
            var stats = new WarshipStats(s.Research);
            var c = s.P("C");
            ok &= Check(Near(c.GetIncomingOffense(Ship.ShipKind.WarShip, 0), 0f), "no orders: no incoming offense");

            var orders = new List<GameAI.GameAIOrder>
            {
                ShipOrder("C", 0, Ship.ShipKind.WarShip, None, Off1),     // 10 + 14
                ShipOrder("C", 0, Ship.ShipKind.WarShip, Off12),          // 18
                ShipOrder("C", 1, Ship.ShipKind.WarShip, None),           // another player's fleet: 10
                ShipOrder("C", 0, Ship.ShipKind.ColonyShip, None),        // not a warship: ignored
                new GameAI.GameAIOrder
                {
                    Type = GameAI.GameAIOrder.OrderType.OrderTypeShipTransport, Target = "C", PlayerId = 0, Data = 1,
                    Fleet = null,                                         // no payload: ignored, no exception
                },
                new GameAI.GameAIOrder
                {
                    Type = GameAI.GameAIOrder.OrderType.OrderTypeFoodTransport, Target = "C", PlayerId = 0, Data = 5f,
                },                                                        // not a ship order: ignored
                ShipOrder("Nowhere", 0, Ship.ShipKind.WarShip, None),     // unknown target: ignored
            };
            s.Map.RecomputeIncomingOffense(orders, stats);
            ok &= Check(Near(c.GetIncomingOffense(Ship.ShipKind.WarShip, 0), 42f),
                "player 0's incoming offense at C is 10 + 14 + 18 = 42 (bad orders ignored)");
            ok &= Check(Near(c.GetIncomingOffense(Ship.ShipKind.WarShip, 1), 10f), "per owner: player 1's fleet is 10");
            ok &= Check(Near(c.GetIncomingOffense(Ship.ShipKind.ColonyShip, 0), 0f), "colony ships add no offense");

            s.Map.RecomputeIncomingOffense(orders, stats);
            ok &= Check(Near(c.GetIncomingOffense(Ship.ShipKind.WarShip, 0), 42f), "recomputing again does not accumulate");

            s.Map.RecomputeIncomingOffense(new List<GameAI.GameAIOrder>(), stats);
            ok &= Check(Near(c.GetIncomingOffense(Ship.ShipKind.WarShip, 0), 0f), "no orders in flight clears it");

            c.AddIncomingOffense(Ship.ShipKind.WarShip, 0, -5f);
            ok &= Check(Near(c.GetIncomingOffense(Ship.ShipKind.WarShip, 0), 0f), "the counter never goes below 0");
        }
        return ok;
    }
}
```

- [ ] **Step 2: Confirm RED**

Run `mcp__rider__get_file_problems` on `Assets/Editor/BlockadeBreakSelfCheck.cs`. Expected: errors for the missing `GetIncomingOffense`, `AddIncomingOffense`, `RecomputeIncomingOffense` and for the `AssaultPlanner` six-argument constructor (the other members the file uses, such as `maxPathNodesForShipTransport`, `blockadeMemoryTurns` and `garrisonOuter`, already exist). If Rider says "not included in any project", ask the user to focus the Editor and read the Console instead.

- [ ] **Step 3: Add the tunables**

In `Assets/Flatspace/GameAI/GameAIConstants.cs`, after the `blockadedWarshipBoost` field (the "Blockade response" header block), add a new block:

```csharp
    [Header("Blockade breaking (Consolidate)")]
    // A planet that cut one of my orders within this many turns ranks first among blockade-breaking targets (after the
    // planet where I already have the most offense committed). Read from the player's BlockadeMemory.
    public int blockadeTargetRecentTurns = 5;
    // The force sent to a blockaded planet is value x (1 + this) offense, so any small overshoot clears the blockade (a
    // blockade only counts while its value is strictly positive) and float edges or a blocker that adds a ship do not
    // leave it standing.
    public float blockadeBreakMargin = 0.1f;
    // Multiplies the research weight of Warship Offense items while a blockade against the player is visible (1 disables).
    // Blockade value is offense against offense; Health and Defense do nothing until ship combat exists.
    public float blockadedOffenseResearchBoost = 2f;
```

- [ ] **Step 4: Add the derived counter to `Planet`**

In `Assets/Flatspace/Objects/Planets/Planet.cs`, directly after `public void ClearIncomingShips() => _incomingShips.Clear();`:

```csharp

    // Offense of the warships in flight toward this planet, per owning player. Derived like the ship counter, but
    // recomputed once per turn from the in-flight ship orders (GameAIMap.RecomputeIncomingOffense), never saved.
    private readonly Dictionary<(Ship.ShipKind kind, int owner), float> _incomingOffense
        = new Dictionary<(Ship.ShipKind kind, int owner), float>();
    public float GetIncomingOffense(Ship.ShipKind kind, int owner)
        => _incomingOffense.TryGetValue((kind, owner), out var offense) ? offense : 0f;
    public void AddIncomingOffense(Ship.ShipKind kind, int owner, float delta)
        => _incomingOffense[(kind, owner)] = System.Math.Max(0f, GetIncomingOffense(kind, owner) + delta);
    public void ClearIncomingOffense() => _incomingOffense.Clear();
```

- [ ] **Step 5: Add `RecomputeIncomingOffense`**

In `Assets/Flatspace/GameAI/GameAIMap.cs`, directly after `RecomputeIncomingShips`:

```csharp

            /// <summary>
            /// Rebuilds every planet's in-flight warship offense from the in-flight ShipTransport orders: the real offense
            /// of each ship from the snapshot it left with. Derived state, recomputed once per turn before the AI decides
            /// (GameAI.GameAIUpdate), so it matches the incoming ship counter and never drifts. Orders without a warship
            /// fleet are ignored.
            /// </summary>
            public void RecomputeIncomingOffense(List<GameAI.GameAIOrder> orders, WarshipStats stats)
            {
                foreach (var planet in PlanetList) planet.ClearIncomingOffense();
                var template = GameAIConstants.warShipData;
                foreach (var order in orders)
                {
                    if (order.Type != GameAI.GameAIOrder.OrderType.OrderTypeShipTransport) continue;
                    if (order.Fleet == null || order.Fleet.Kind != Ship.ShipKind.WarShip) continue;
                    var target = GetPlanet(order.Target);
                    if (target == null) continue;
                    var offense = order.Fleet.Snapshots.Sum(snapshot => stats.Offense(template, snapshot));
                    target.AddIncomingOffense(Ship.ShipKind.WarShip, order.PlayerId, offense);
                }
            }
```

- [ ] **Step 6: Call it each turn**

In `Assets/Flatspace/GameAI/GameAI.cs`, in `GameAIUpdate`, immediately before `ProcessResults(planetUpdateResults, gameAIOrders);`:

```csharp
                // The AI sizes blockade-breaking fleets against offense already in flight (see AssaultPlanner).
                GameAIMap.RecomputeIncomingOffense(CurrentAIOrders,
                    new WarshipStats(BlockadeSystem.ResearchItemsFrom(Gameboard.Instance.players)));
```

- [ ] **Step 7: Register the suite**

In `Assets/Editor/AllAISelfChecks.cs`, after the `Grotsits Short` entry:

```csharp
            ("Blockade Breaking", BlockadeBreakSelfCheck.RunChecks),
```

- [ ] **Step 8: Update the spec text**

In `docs/superpowers/specs/2026-10-01-blockade-breaking-design.md`, section 2, replace the paragraph starting "**Incoming offense.**" with:

```
**Incoming offense.** `Planet.GetIncomingOffense(kind, owner)` is new, a float parallel to the incoming ship counter
(`GetIncomingShips`). Unlike the counter it is not maintained incrementally: `GameAIMap.RecomputeIncomingOffense(orders, stats)`
rebuilds it once per turn from the in-flight `OrderTypeShipTransport` orders, just before `ProcessResults`, summing
`WarshipStats.Offense` over each order's `Fleet` snapshots. The planner sees the same set of ships the counter shows at that
moment, and there is no arrival or load bookkeeping. It is derived state (never saved) and clamped at 0 like the counter.
`WarshipStats` is built from the research items (`BlockadeSystem.ResearchItemsFrom`), as `BlockadeSystem` is.
```

- [ ] **Step 9: Create the `.meta` file for the new script**

In PowerShell (a new script's `.meta` is committed with it; Unity may rewrite it on first import, which is harmless):

```powershell
$guid = [guid]::NewGuid().ToString('N')
Set-Content -Path "Assets/Editor/BlockadeBreakSelfCheck.cs.meta" -Value "fileFormatVersion: 2`nguid: $guid" -Encoding ascii
```

- [ ] **Step 10: Confirm GREEN**

Ask the user to focus the Unity Editor (recompile, no Console errors), then run `FlatSpace -> AI -> Run Blockade Breaking Self-Check`. Expected Console line: `[BlockadeBreakSelfCheck] ALL PASSED`. Then `Run All AI Self-Checks`: expected `ALL 8 SUITES PASSED` (the existing seven plus this one). The new suite will not compile until Task 2 adds the `AssaultPlanner` six-argument constructor, so do Step 10 together with Task 2's Step 4 if the Editor reports that error.

- [ ] **Step 11: Commit**

```bash
git add Assets/Flatspace/GameAI/GameAIConstants.cs Assets/Flatspace/Objects/Planets/Planet.cs Assets/Flatspace/GameAI/GameAIMap.cs Assets/Flatspace/GameAI/GameAI.cs Assets/Editor/BlockadeBreakSelfCheck.cs Assets/Editor/BlockadeBreakSelfCheck.cs.meta Assets/Editor/AllAISelfChecks.cs docs/superpowers/specs/2026-10-01-blockade-breaking-design.md
git commit -m "feat: derived in-flight warship offense and blockade-breaking tunables"
```

---

### Task 2: Blockade targets in `AssaultPlanner`

**Files:**
- Modify: `Assets/Flatspace/GameAI/AssaultPlanner.cs` (constructor, fields, `ChooseTarget`)
- Modify: `Assets/Editor/BlockadeBreakSelfCheck.cs`

**Interfaces:**
- Consumes: `BlockadeView.BlockadedNames`/`IsBlockaded`/`Value` (existing), `BlockadeMemory.IsActive(planet, turn, lifetime)` (existing), `Planet.GetIncomingOffense` (Task 1), `WarshipStats.Offense`.
- Produces: `new AssaultPlanner(GameAIMap map, int playerId, BlockadeView view = null, WarshipStats stats = null, BlockadeMemory memory = null, int turn = 0)`; `bool IsBlockadeTarget(Planet)`; `float CommittedOffense(Planet)`; `float NeededOffense(Planet)`; `Planet ChooseBlockadeTarget(out string reason)` (reason is one of `AssaultPlanner.ReasonCommitted`, `ReasonRecentCut`, `ReasonCheapest`; null when there is no target); `Planet ChooseEnemyTarget()` (the old `ChooseTarget` body); `Planet ChooseTarget()` (blockade first, then enemy).

- [ ] **Step 1: Add the failing checks**

In `BlockadeBreakSelfCheck.RunChecks`, add `ok &= RunBlockadeTargetCheck(); ok &= RunBlockadeRankingCheck();` after the incoming-offense check, and add these methods before the final closing brace:

```csharp
    public static bool RunBlockadeTargetCheck()
    {
        var ok = true;
        using (var s = Scenario.Line())
        {
            s.Ships("A", 0, 3);
            s.Ships("C", 1, 2);                       // player 1 blockades C against me: value 2 x 10 = 20
            var view = s.View(10);
            var planner = s.Planner(view, 10);

            var target = planner.ChooseBlockadeTarget(out var reason);
            ok &= Check(target == s.P("C") && reason == AssaultPlanner.ReasonCheapest,
                "a visibly blockaded, reachable planet is the target (the only candidate decides on 'Cheapest')");
            ok &= Check(planner.IsBlockadeTarget(s.P("C")) && !planner.IsBlockadeTarget(s.P("D")),
                "IsBlockadeTarget is true for the blockaded planet only");
            ok &= Check(Near(planner.NeededOffense(s.P("C")), 22f),
                "needed = value 20 x (1 + margin 0.1) = 22");
            s.P("C").AddIncomingOffense(Ship.ShipKind.WarShip, 0, 8f);
            ok &= Check(Near(planner.NeededOffense(s.P("C")), 14f), "offense already in flight is subtracted: 22 - 8 = 14");
            ok &= Check(planner.ChooseTarget() == s.P("C"), "ChooseTarget picks the blockade target first");

            // An enemy-occupied planet D exists, but the blockade outranks it.
            ok &= Check(planner.ChooseEnemyTarget() == s.P("D"), "the enemy-occupied fallback still finds D");
        }

        // No blockade anywhere: the existing enemy-occupied rule runs unchanged.
        using (var s = Scenario.Line())
        {
            s.Ships("A", 0, 3);
            var planner = s.Planner(s.View(10), 10);
            ok &= Check(planner.ChooseBlockadeTarget(out var reason) == null && reason == null,
                "nothing blockaded: no blockade target");
            ok &= Check(planner.ChooseTarget() == s.P("D"), "nothing blockaded: the enemy-occupied planet D is the target");
        }

        // Review focus 5: no view at all (the old constructor, every existing caller) behaves exactly as before.
        using (var s = Scenario.Line())
        {
            s.Ships("A", 0, 3);
            s.Ships("C", 1, 2);
            var legacy = new AssaultPlanner(s.Map, 0);
            ok &= Check(legacy.ChooseBlockadeTarget(out _) == null, "no view: no blockade candidates");
            ok &= Check(!legacy.IsBlockadeTarget(s.P("C")), "no view: nothing is a blockade target");
            ok &= Check(legacy.ChooseTarget() == s.P("D"),
                "no view: ChooseTarget is the enemy-occupied rule (C holds enemy ships but no population, D is the target)");
            ok &= Check(Near(legacy.NeededOffense(s.P("C")), 0f), "no view: nothing is needed anywhere");
        }

        // Review focus 1: a planet known only from memory (no blocker, value only) is a valid target and nothing throws.
        using (var s = Scenario.Line())
        {
            s.Ships("A", 0, 3);
            var remembered = new AssaultPlanner(s.Map, 0, BlockadeView.WithValues(("C", 20f)),
                new WarshipStats(s.Research), s.Memory, 10);
            ok &= Check(remembered.ChooseBlockadeTarget(out _) == s.P("C"),
                "a blockade with no known blocker (remembered, unseen) is still a target");
            ok &= Check(Near(remembered.NeededOffense(s.P("C")), 22f), "its remembered value sizes the need: 20 x 1.1");
        }

        // Review focus 2: a blockaded planet nobody of mine can reach is not a candidate, so the fallback runs.
        using (var s = Scenario.Line())
        {
            s.Ships("A", 0, 3);
            var unreachable = new AssaultPlanner(s.Map, 0, BlockadeView.WithValues(("Z", 20f)),
                new WarshipStats(s.Research), s.Memory, 10);
            ok &= Check(unreachable.ChooseBlockadeTarget(out _) == null, "an unreachable blockaded planet is not a candidate");
            ok &= Check(unreachable.ChooseTarget() == s.P("D"), "so the enemy-occupied fallback (D) runs");
        }
        return ok;
    }

    // Ranking: committed offense, then a recent cut, then the smallest offense still needed, then path cost, then name.
    public static bool RunBlockadeRankingCheck()
    {
        var ok = true;

        // C1: 3 enemy ships (value 30, cheaper path); C2: 2 enemy ships (value 20, dearer path).
        using (var s = Scenario.Fork())
        {
            s.Ships("A", 0, 2); s.Ships("C1", 1, 3); s.Ships("C2", 1, 2);
            var planner = s.Planner(s.View(10), 10);
            ok &= Check(planner.ChooseBlockadeTarget(out var reason) == s.P("C2") && reason == AssaultPlanner.ReasonCheapest,
                "nothing committed, no cut: the smaller need (C2: 22 against C1: 33) wins");
        }

        using (var s = Scenario.Fork())
        {
            s.Ships("A", 0, 2); s.Ships("C1", 1, 3); s.Ships("C2", 1, 2);
            s.Memory.Learn("C1", 30f, 8, 20);               // turn 10: cut 2 turns ago, inside the 5-turn window
            var planner = s.Planner(s.View(10), 10);
            ok &= Check(planner.ChooseBlockadeTarget(out var reason) == s.P("C1") && reason == AssaultPlanner.ReasonRecentCut,
                "a planet that cut my order 2 turns ago outranks a smaller need");
        }

        using (var s = Scenario.Fork())
        {
            s.Ships("A", 0, 2); s.Ships("C1", 1, 3); s.Ships("C2", 1, 2);
            s.Memory.Learn("C1", 30f, 3, 20);               // turn 10: cut 7 turns ago, outside the window
            var planner = s.Planner(s.View(10), 10);
            ok &= Check(planner.ChooseBlockadeTarget(out _) == s.P("C2"),
                "a cut older than blockadeTargetRecentTurns no longer outranks the smaller need");
        }

        using (var s = Scenario.Fork())
        {
            s.Ships("A", 0, 2); s.Ships("C1", 1, 3); s.Ships("C2", 1, 2);
            s.Ships("C1", 0, 1);                            // I already hold one ship (offense 10) at C1
            var planner = s.Planner(s.View(10), 10);
            ok &= Check(planner.CommittedOffense(s.P("C1")) > 9.99f, "docked offense counts as committed");
            ok &= Check(planner.ChooseBlockadeTarget(out var reason) == s.P("C1") && reason == AssaultPlanner.ReasonCommitted,
                "committed offense wins first, even though C1's remaining value (20) ties C2's");
        }

        using (var s = Scenario.Fork())
        {
            s.Ships("A", 0, 2); s.Ships("C1", 1, 3); s.Ships("C2", 1, 2);
            s.P("C2").AddIncomingShips(Ship.ShipKind.WarShip, 0, 1);
            s.P("C2").AddIncomingOffense(Ship.ShipKind.WarShip, 0, 10f);
            var planner = s.Planner(s.View(10), 10);
            ok &= Check(planner.ChooseBlockadeTarget(out var reason) == s.P("C2") && reason == AssaultPlanner.ReasonCommitted,
                "offense in flight toward a planet counts as committed");
        }
        return ok;
    }
```

- [ ] **Step 2: Confirm RED**

`get_file_problems` on the self-check: errors for the missing constructor overload, `ChooseBlockadeTarget`, `ChooseEnemyTarget`, `IsBlockadeTarget`, `NeededOffense`, `CommittedOffense`, `ReasonCommitted`, `ReasonRecentCut`, `ReasonCheapest`.

- [ ] **Step 3: Implement**

In `Assets/Flatspace/GameAI/AssaultPlanner.cs`, replace the fields and constructor (lines 17-26) with:

```csharp
            public const string ReasonCommitted = "Committed";
            public const string ReasonRecentCut = "RecentCut";
            public const string ReasonCheapest  = "Cheapest";

            private readonly GameAIMap _map;
            private readonly int _playerId;
            private readonly GameAIConstants _constants;
            private readonly BlockadeView _view;
            private readonly WarshipStats _stats;
            private readonly BlockadeMemory _memory;
            private readonly int _turn;

            /// <summary>
            /// `view`, `stats` and `memory` are optional: without a view nothing is a blockade target and the planner behaves
            /// exactly as it did before blockade breaking existed. `turn` is only used to test BlockadeMemory for recent cuts.
            /// </summary>
            public AssaultPlanner(GameAIMap map, int playerId, BlockadeView view = null, WarshipStats stats = null,
                BlockadeMemory memory = null, int turn = 0)
            {
                _map = map;
                _playerId = playerId;
                _constants = map.GameAIConstants;
                _view = view;
                _stats = stats;
                _memory = memory;
                _turn = turn;
            }
```

After `CheapestPathCost` (before the old `ChooseTarget`), add:

```csharp
            // ── Blockade breaking ────────────────────────────────────────────

            /// <summary>The planet is blockaded against me in my view.</summary>
            public bool IsBlockadeTarget(Planet planet)
                => _view != null && planet != null && _view.IsBlockaded(planet.PlanetName);

            /// <summary>My real warship offense docked at the planet (0 without WarshipStats).</summary>
            private float DockedOffense(Planet planet)
            {
                if (_stats == null) return 0f;
                var sum = 0f;
                foreach (var ship in planet.DockedShips)
                    if (ship.Kind == Ship.ShipKind.WarShip && ship.Owner == _playerId)
                        sum += _stats.Offense(ship.Template, ship.ResearchSnapshot);
                return sum;
            }

            /// <summary>My offense committed at the planet: docked plus in flight.</summary>
            public float CommittedOffense(Planet planet)
                => DockedOffense(planet) + planet.GetIncomingOffense(Ship.ShipKind.WarShip, _playerId);

            /// <summary>
            /// Offense still to send to break the blockade: value x (1 + margin) minus offense already in flight. The view's
            /// value already subtracts my docked offense. 0 when the planet is not blockaded; at or below 0 when covered.
            /// </summary>
            public float NeededOffense(Planet planet)
            {
                if (!IsBlockadeTarget(planet)) return 0f;
                return _view.Value(planet.PlanetName) * (1f + _constants.blockadeBreakMargin)
                       - planet.GetIncomingOffense(Ship.ShipKind.WarShip, _playerId);
            }

            private struct BlockadeCandidate
            {
                public Planet Planet;
                public float  Committed;
                public bool   Recent;
                public float  Needed;
                public float  Cost;
            }

            /// <summary>
            /// The blockaded, reachable planet to break. Ranked: most of my offense committed there, then a recent cut of one
            /// of my orders, then the smallest offense still needed, then the cheapest path from a holder of my warships,
            /// then name. `reason` says which step decided it (null with no target). A planet is reachable when I have ships
            /// committed there or a usable path from a planet holding my warships; remembered, unseen planets count.
            /// </summary>
            public Planet ChooseBlockadeTarget(out string reason)
            {
                reason = null;
                if (_view == null) return null;

                var holders = _map.PlanetList.Where(p => CountWarships(p) > 0).ToList();
                var candidates = new List<BlockadeCandidate>();
                foreach (var name in _view.BlockadedNames.OrderBy(n => n, StringComparer.Ordinal))
                {
                    var planet = _map.GetPlanet(name);
                    if (planet == null) continue;

                    var own = CountWarships(planet) + planet.GetIncomingShips(Ship.ShipKind.WarShip, _playerId);
                    var cost = own > 0 ? 0f : CheapestPathCost(holders, planet);
                    if (cost == null) continue;

                    candidates.Add(new BlockadeCandidate
                    {
                        Planet    = planet,
                        Committed = CommittedOffense(planet),
                        Recent    = _memory != null
                                    && _memory.IsActive(name, _turn, _constants.blockadeTargetRecentTurns),
                        Needed    = NeededOffense(planet),
                        Cost      = cost.Value,
                    });
                }
                if (candidates.Count == 0) return null;

                var ranked = candidates
                    .OrderByDescending(c => c.Committed)
                    .ThenByDescending(c => c.Recent)
                    .ThenBy(c => c.Needed)
                    .ThenBy(c => c.Cost)
                    .ThenBy(c => c.Planet.PlanetName, StringComparer.Ordinal)
                    .ToList();
                var best = ranked[0];
                if (ranked.Count == 1)
                    reason = best.Committed > 0f ? ReasonCommitted : best.Recent ? ReasonRecentCut : ReasonCheapest;
                else if (best.Committed != ranked[1].Committed) reason = ReasonCommitted;
                else if (best.Recent != ranked[1].Recent) reason = ReasonRecentCut;
                else reason = ReasonCheapest;
                return best.Planet;
            }

            // ── Targets ──────────────────────────────────────────────────────

            /// <summary>A blockaded planet first (see ChooseBlockadeTarget), else the enemy-occupied rule.</summary>
            public Planet ChooseTarget() => ChooseBlockadeTarget(out _) ?? ChooseEnemyTarget();
```

Then rename the existing `ChooseTarget` method (its doc comment and body stay exactly as they are) to `ChooseEnemyTarget`:

```csharp
            public Planet ChooseEnemyTarget()
```

- [ ] **Step 4: Confirm GREEN**

Ask the user to focus the Editor and run `FlatSpace -> AI -> Run Blockade Breaking Self-Check` (expected `ALL PASSED`), then `Run Ship Transport Self-Check` (the existing assault checks call `new AssaultPlanner(map, 0).ChooseTarget()`; expected `ALL PASSED`, same assertion count as before plus none removed), then `Run All AI Self-Checks` (`ALL 8 SUITES PASSED`).

- [ ] **Step 5: Commit**

```bash
git add Assets/Flatspace/GameAI/AssaultPlanner.cs Assets/Editor/BlockadeBreakSelfCheck.cs
git commit -m "feat: assault planner ranks visibly blockaded planets as targets"
```

---

### Task 3: Offense-sized force in `AssaultPlanner.Plan`

**Files:**
- Modify: `Assets/Flatspace/GameAI/AssaultPlanner.cs` (`Source`, `Plan`)
- Modify: `Assets/Editor/BlockadeBreakSelfCheck.cs`

**Interfaces:**
- Consumes: `NeededOffense`, `IsBlockadeTarget` (Task 2), `Planet.PeekShipSnapshots(kind, owner, count, skip)` (existing), `ShipTransportPlanner.PlanetState` (public class: `Planet`, `Docked`, `RoundGarrison`, `Spare`).
- Produces: `AssaultPlanner.BlockadeForce` struct (`int Ships`, `float Offense`, `float StillNeeded`) and `AssaultPlanner.LastBlockadeForce` (set by every `Plan` call, all zero for a non-blockade target); `Plan(Planet target, List<ShipTransportPlanner.PlanetState> states, List<ShipAction> homeActions)` keeps its signature.

- [ ] **Step 1: Add the failing checks**

Add `ok &= RunBlockadePlanCheck();` to `RunChecks` and these methods:

```csharp
    private static List<ShipTransportPlanner.PlanetState> States(Scenario s)
        => new List<ShipTransportPlanner.PlanetState>
        {
            // RoundGarrison 0: every docked ship is spare, so the checks aim at the assault arithmetic alone.
            new ShipTransportPlanner.PlanetState { Planet = s.P("A"), Docked = s.P("A").DockedShips.Count, RoundGarrison = 0 },
            new ShipTransportPlanner.PlanetState { Planet = s.P("B"), Docked = s.P("B").DockedShips.Count, RoundGarrison = 0 },
        };

    private static ShipAction Find(List<ShipAction> actions, string origin, string target)
        => actions.FirstOrDefault(a => a.Origin == origin && a.Target == target);

    // Sources are walked cheapest path first; each sends only the ships its real offense needs, using the exact ships that
    // would leave (after the ones home defence already claimed); a short fleet sends everything it has spare.
    public static bool RunBlockadePlanCheck()
    {
        var ok = true;

        // Value 30 against my ships at A (10, 14, 18) and B (10, 10); margin 0 for exact arithmetic.
        using (var s = Scenario.Line())
        {
            s.Constants.blockadeBreakMargin = 0f;
            s.Dock("A", 0, None, Off1, Off12); s.Ships("B", 0, 2); s.Ships("C", 1, 3);
            var planner = s.Planner(s.View(10), 10);
            var actions = planner.Plan(s.P("C"), States(s), new List<ShipAction>());
            ok &= Check(actions.Count == 2 && Find(actions, "B", "C").Count == 2 && Find(actions, "A", "C").Count == 1,
                "B (cheaper path) sends both ships (20), A sends one (10): exactly the 30 needed, nothing more");
            ok &= Check(Find(actions, "B", "C").Cost == 100f && Find(actions, "A", "C").Cost == 200f,
                "each action carries its path cost");
            var force = planner.LastBlockadeForce;
            ok &= Check(force.Ships == 3 && Near(force.Offense, 30f) && Near(force.StillNeeded, 0f),
                "LastBlockadeForce reports 3 ships, offense 30, nothing still needed");
        }

        // Partial: 10 enemy ships (value 100) against 62 of mine; everything spare is sent.
        using (var s = Scenario.Line())
        {
            s.Constants.blockadeBreakMargin = 0f;
            s.Dock("A", 0, None, Off1, Off12); s.Ships("B", 0, 2); s.Ships("C", 1, 10);
            var planner = s.Planner(s.View(10), 10);
            var actions = planner.Plan(s.P("C"), States(s), new List<ShipAction>());
            ok &= Check(Find(actions, "B", "C").Count == 2 && Find(actions, "A", "C").Count == 3,
                "a force that cannot cover the need sends every spare ship");
            ok &= Check(Near(planner.LastBlockadeForce.Offense, 62f) && Near(planner.LastBlockadeForce.StillNeeded, 38f),
                "62 sent, 38 still needed");
        }

        // Ships home defence already claimed are skipped by identity: the 18-offense ship is the one left at A.
        using (var s = Scenario.Line())
        {
            s.Constants.blockadeBreakMargin = 0f;
            s.Dock("A", 0, None, Off1, Off12); s.Ships("B", 0, 2); s.Ships("C", 1, 4);   // value 40
            var home = new List<ShipAction>
            {
                new ShipAction { Origin = "A", Target = "B", Count = 2, Kind = Ship.ShipKind.WarShip },
            };
            var planner = s.Planner(s.View(10), 10);
            var actions = planner.Plan(s.P("C"), States(s), home);
            ok &= Check(Find(actions, "A", "C").Count == 1, "A has 3 ships, home defence takes 2: one is left for the assault");
            ok &= Check(Near(planner.LastBlockadeForce.Offense, 38f) && Near(planner.LastBlockadeForce.StillNeeded, 2f),
                "B's 20 plus A's remaining ship (offense 18, not the first-docked 10): 38 sent, 2 still needed");
        }

        // Offense already in flight is subtracted from the need.
        using (var s = Scenario.Line())
        {
            s.Constants.blockadeBreakMargin = 0f;
            s.Ships("A", 0, 3); s.Ships("B", 0, 2); s.Ships("C", 1, 3);                   // value 30
            s.P("C").AddIncomingOffense(Ship.ShipKind.WarShip, 0, 25f);
            var planner = s.Planner(s.View(10), 10);
            var actions = planner.Plan(s.P("C"), States(s), new List<ShipAction>());
            ok &= Check(actions.Count == 1 && actions[0].Origin == "B" && actions[0].Count == 1,
                "30 needed, 25 in flight: one ship (10) from the cheapest source");

            s.P("C").AddIncomingOffense(Ship.ShipKind.WarShip, 0, 10f);                   // now 35 in flight
            ok &= Check(planner.Plan(s.P("C"), States(s), new List<ShipAction>()).Count == 0
                        && planner.LastBlockadeForce.Ships == 0,
                "offense in flight already covers the blockade: nothing is sent");
        }

        // The margin sizes the force above the value: 30 x 1.1 = 33 needs four 10-offense ships.
        using (var s = Scenario.Line())
        {
            s.Ships("A", 0, 3); s.Ships("B", 0, 2); s.Ships("C", 1, 3);
            var actions = s.Planner(s.View(10), 10).Plan(s.P("C"), States(s), new List<ShipAction>());
            ok &= Check(actions.Sum(a => a.Count) == 4, "value 30 with margin 0.1 needs 33 offense: four ships of 10");
        }

        // Review focus 4: no warship stats means every ship counts 0 offense; the plan sends all spare ships and ends.
        using (var s = Scenario.Line())
        {
            s.Constants.blockadeBreakMargin = 0f;
            s.Ships("A", 0, 3); s.Ships("B", 0, 2); s.Ships("C", 1, 3);
            var noStats = new AssaultPlanner(s.Map, 0, s.View(10), null, s.Memory, 10);
            var actions = noStats.Plan(s.P("C"), States(s), new List<ShipAction>());
            ok &= Check(actions.Sum(a => a.Count) == 5, "without stats every spare ship is sent, and the plan terminates");
        }

        // A target that is not blockaded keeps the ship-count rule: 3 enemy ships known at C x 1.5 = ceil(4.5) = 5.
        using (var s = Scenario.Line())
        {
            s.Ships("A", 0, 3); s.Ships("B", 0, 2); s.Ships("C", 1, 3);
            var planner = s.Planner(BlockadeView.WithValues(), 10);   // nothing blockaded
            var actions = planner.Plan(s.P("D"), States(s), new List<ShipAction>());
            ok &= Check(actions.Sum(a => a.Count) == 5 && planner.LastBlockadeForce.Ships == 0,
                "an ordinary enemy-occupied target is still sized by ship count (5), and no blockade force is reported");
        }
        return ok;
    }
```

- [ ] **Step 2: Confirm RED**

`get_file_problems`: errors for `LastBlockadeForce` and the `BlockadeForce` members.

- [ ] **Step 3: Implement**

In `AssaultPlanner.cs`, add above the `Source` struct:

```csharp
            /// <summary>What the last Plan call sent at a blockade target; all zero for any other target.</summary>
            public struct BlockadeForce
            {
                public int   Ships;
                public float Offense;
                public float StillNeeded;
            }

            public BlockadeForce LastBlockadeForce { get; private set; }
```

Replace the whole `Plan` method (and its doc comment) with the following, which keeps the original ship-count path unchanged and adds the offense path:

```csharp
            /// <summary>The ships a planet can spare for `target`, cheapest path first, ties by name.</summary>
            private List<Source> SpareSources(Planet target, List<ShipTransportPlanner.PlanetState> states,
                Dictionary<string, int> sentByOrigin)
            {
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
                return sources.OrderBy(s => s.Cost).ThenBy(s => s.Name, StringComparer.Ordinal).ToList();
            }

            /// <summary>
            /// Sends spare ships to the target, cheapest path first. An ordinary target is sized by ship count until the
            /// deficit is met; a blockaded target is sized by real offense until NeededOffense is covered (or every spare
            /// ship is sent). A source's spare is its state's Spare minus what the home actions already send from it.
            /// </summary>
            public List<ShipAction> Plan(Planet target, List<ShipTransportPlanner.PlanetState> states,
                List<ShipAction> homeActions)
            {
                LastBlockadeForce = default;
                var actions = new List<ShipAction>();
                if (target == null) return actions;

                var sentByOrigin = homeActions
                    .GroupBy(a => a.Origin)
                    .ToDictionary(g => g.Key, g => g.Sum(a => a.Count));

                if (IsBlockadeTarget(target))
                    return PlanBlockadeForce(target, states, sentByOrigin);

                var deficit = Deficit(target);
                if (deficit <= 0) return actions;

                foreach (var source in SpareSources(target, states, sentByOrigin))
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

            // Walks the sources and, for each, the exact ships that would leave (the first docked ones after those home
            // defence claimed), subtracting each ship's real offense from what is still needed. Without stats a ship
            // counts 0, so every spare ship is sent.
            private List<ShipAction> PlanBlockadeForce(Planet target, List<ShipTransportPlanner.PlanetState> states,
                Dictionary<string, int> sentByOrigin)
            {
                var actions = new List<ShipAction>();
                var needed = NeededOffense(target);
                if (needed <= 0f) return actions;

                var template = _constants.warShipData;
                var ships = 0;
                var offense = 0f;
                foreach (var source in SpareSources(target, states, sentByOrigin))
                {
                    if (needed <= 0f) break;
                    sentByOrigin.TryGetValue(source.Name, out var skip);
                    var snapshots = _map.GetPlanet(source.Name)
                        .PeekShipSnapshots(Ship.ShipKind.WarShip, _playerId, source.Remaining, skip);

                    var taken = 0;
                    foreach (var snapshot in snapshots)
                    {
                        if (needed <= 0f) break;
                        var shipOffense = _stats != null ? _stats.Offense(template, snapshot) : 0f;
                        needed -= shipOffense;
                        offense += shipOffense;
                        taken++;
                    }
                    if (taken == 0) continue;

                    actions.Add(new ShipAction
                    {
                        Origin = source.Name,
                        Target = target.PlanetName,
                        Cost   = source.Cost,
                        Count  = taken,
                        Kind   = Ship.ShipKind.WarShip,
                    });
                    ships += taken;
                }
                LastBlockadeForce = new BlockadeForce { Ships = ships, Offense = offense, StillNeeded = Math.Max(0f, needed) };
                return actions;
            }
```

- [ ] **Step 4: Confirm GREEN**

Ask the user to focus the Editor and run `Run Blockade Breaking Self-Check` (`ALL PASSED`), `Run Ship Transport Self-Check` (unchanged, `ALL PASSED`; the existing `RunAssaultPlanCheck` exercises the ship-count `Plan` path that was refactored), then `Run All AI Self-Checks`.

- [ ] **Step 5: Commit**

```bash
git add Assets/Flatspace/GameAI/AssaultPlanner.cs Assets/Editor/BlockadeBreakSelfCheck.cs
git commit -m "feat: size the blockade-breaking force by real warship offense"
```

---

### Task 4: Wiring, target tracker and tuning log

**Files:**
- Create: `Assets/Flatspace/GameAI/BlockadeTargetTracker.cs` (+ `.meta`)
- Modify: `Assets/Flatspace/Diagnostics/AITuningLogger.cs` (after `LogAssaultTarget`, near line 157)
- Modify: `Assets/Flatspace/GameAI/PlayerAI.cs` (`PlanShipActions`, near lines 1438-1465)
- Modify: `Assets/Editor/BlockadeBreakSelfCheck.cs`

**Interfaces:**
- Consumes: `AssaultPlanner` (Tasks 2, 3), `BlockadeView.Blocker`/`Value`/`IsBlockaded`.
- Produces: `BlockadeTargetTracker` with `struct Target { string Planet; int Blocker; float Value; float Needed; string Reason; }`, `struct Change { bool Started; string Planet; string EndReason; int TurnsHeld; Target Target; }`, `List<Change> Update(int turn, Target? current, System.Func<string, bool> isBlockaded)`, `void Clear()`, `string Current { get; }`; `AITuningLogger.LogBlockadeTarget(int turn, int playerId, string planet, int blocker, float value, float needed, string reason)`, `LogBlockadeForce(int turn, int playerId, string planet, int ships, float offense, float stillNeeded)`, `LogBlockadeTargetEnd(int turn, int playerId, string planet, string reason, int turnsHeld)`.

- [ ] **Step 1: Add the failing checks**

Add `ok &= RunTrackerCheck(); ok &= RunPlanShipActionsCheck();` to `RunChecks` and:

```csharp
    private static BlockadeTargetTracker.Target Tg(string planet, string reason = "Cheapest")
        => new BlockadeTargetTracker.Target { Planet = planet, Blocker = 1, Value = 20f, Needed = 22f, Reason = reason };

    private static string Describe(List<BlockadeTargetTracker.Change> changes)
        => string.Join(",", changes.Select(c => c.Started ? "+" + c.Planet : "-" + c.Planet + ":" + c.EndReason + ":" + c.TurnsHeld));

    // Start and end transitions only (never a line per turn), End before Start in one call, and the end reason:
    // Cleared when the planet is no longer blockaded, Switched when another target replaced it, else Unreachable.
    public static bool RunTrackerCheck()
    {
        var ok = true;
        var t = new BlockadeTargetTracker();
        ok &= Check(Describe(t.Update(1, Tg("C"), n => true)) == "+C", "the first target starts");
        ok &= Check(t.Current == "C", "Current is the tracked planet");
        ok &= Check(t.Update(2, Tg("C"), n => true).Count == 0, "the same target on a later turn reports nothing");
        ok &= Check(Describe(t.Update(5, Tg("D"), n => true)) == "-C:Switched:4,+D",
            "a new target ends the old one as Switched (held 4 turns) and starts D, End first");
        ok &= Check(Describe(t.Update(8, null, n => n != "D")) == "-D:Cleared:3",
            "no target and D no longer blockaded: Cleared after 3 turns");
        ok &= Check(t.Current == null, "Current is empty after an end");
        ok &= Check(t.Update(9, null, n => false).Count == 0, "nothing tracked, nothing reported");
        t.Update(9, Tg("E"), n => true);
        ok &= Check(Describe(t.Update(10, null, n => true)) == "-E:Unreachable:1",
            "no target while E is still blockaded: Unreachable");
        t.Update(12, Tg("F"), n => true);
        t.Clear();
        ok &= Check(t.Update(13, null, n => true).Count == 0 && t.Current == null, "Clear forgets the tracked target silently");
        return ok;
    }

    // End to end through PlayerAI: under Consolidate a blockaded C beats the enemy-occupied D, the home garrison at the
    // outer planet B is kept, and only B's two spare ships go.
    public static bool RunPlanShipActionsCheck()
    {
        var ok = true;
        using (var s = Scenario.Line())
        {
            s.Ships("B", 0, 8);                       // outer garrison 6, so 2 are spare
            s.Ships("C", 1, 3);                       // blockade value 30 against me
            s.AI.Strategy = PlayerAI.AIStrategy.AIStrategyConsolidate;
            s.AI.RefreshBlockadeView(5);
            var actions = s.AI.PlanShipActions(5);
            ok &= Check(actions.Count == 1 && actions[0].Origin == "B" && actions[0].Target == "C" && actions[0].Count == 2,
                "Consolidate sends B's two spare ships at the blockaded C, not at D");
        }
        using (var s = Scenario.Line())
        {
            s.Ships("B", 0, 8);
            s.AI.Strategy = PlayerAI.AIStrategy.AIStrategyConsolidate;
            s.AI.RefreshBlockadeView(5);
            var actions = s.AI.PlanShipActions(5);
            ok &= Check(actions.Count == 1 && actions[0].Target == "D" && actions[0].Count == 2,
                "with nothing blockaded the assault still goes to the enemy-occupied D");
        }
        return ok;
    }
```

- [ ] **Step 2: Confirm RED**

`get_file_problems`: errors for `BlockadeTargetTracker` (a brand-new file: ask the user to focus the Editor to regenerate the project, then read the Console).

- [ ] **Step 3: Create the tracker**

Create `Assets/Flatspace/GameAI/BlockadeTargetTracker.cs`:

```csharp
using System;
using System.Collections.Generic;

namespace FlatSpace.AI
{
    /// <summary>
    /// Turns each turn's blockade-breaking target into start and end transitions, so the tuning log can say which planet was
    /// chosen, why, and how it ended without a line per turn. Pure (no Gameboard.Instance, no Planet), fed by PlayerAI;
    /// log-only state, never saved, so a load just reports the current target once more.
    /// </summary>
    public class BlockadeTargetTracker
    {
        public struct Target
        {
            public string Planet;
            public int    Blocker;   // -1 (Planet.NoOwner) for a remembered, unseen planet
            public float  Value;
            public float  Needed;
            public string Reason;    // Committed, RecentCut or Cheapest
        }

        public struct Change
        {
            public bool   Started;   // true: the planet became the target; false: it stopped being the target
            public string Planet;
            public string EndReason; // Cleared, Switched or Unreachable (ends only)
            public int    TurnsHeld; // turns since it became the target (ends only)
            public Target Target;    // the new target's details (starts only)
        }

        private Target? _current;
        private int _startTurn;

        public string Current => _current?.Planet;

        public void Clear() => _current = null;

        /// <summary>
        /// `current` is this turn's blockade target (null when the assault has none or goes to an ordinary enemy planet);
        /// `isBlockaded` says whether a planet is still blockaded in my view. Returns the transitions since the last call,
        /// an end before the start that replaces it. An ended target is Cleared when it is no longer blockaded, else
        /// Switched when another target replaced it, else Unreachable.
        /// </summary>
        public List<Change> Update(int turn, Target? current, Func<string, bool> isBlockaded)
        {
            var changes = new List<Change>();
            if (_current != null && (current == null || current.Value.Planet != _current.Value.Planet))
            {
                var ended = _current.Value.Planet;
                var reason = !isBlockaded(ended) ? "Cleared" : current != null ? "Switched" : "Unreachable";
                changes.Add(new Change { Started = false, Planet = ended, EndReason = reason, TurnsHeld = turn - _startTurn });
                _current = null;
            }
            if (current != null && _current == null)
            {
                _current = current;
                _startTurn = turn;
                changes.Add(new Change { Started = true, Planet = current.Value.Planet, Target = current.Value });
            }
            return changes;
        }
    }
}
```

Create its `.meta` (PowerShell, as in Task 1 Step 9, with the path `Assets/Flatspace/GameAI/BlockadeTargetTracker.cs.meta`).

- [ ] **Step 4: Add the logger methods**

In `Assets/Flatspace/Diagnostics/AITuningLogger.cs`, after `LogAssaultTarget`:

```csharp

    /// <summary>The blockade-breaking target changed: T&lt;turn&gt;|P&lt;id&gt;|BlockadeTarget|planet|blocker|value|neededOffense|Committed, RecentCut or Cheapest. Blocker -1 = remembered, unseen.</summary>
    public static void LogBlockadeTarget(int turnNumber, int playerId, string planet, int blocker, float value,
        float neededOffense, string reason)
    {
        if (_currentLogPath == null) return;
        var ci = System.Globalization.CultureInfo.InvariantCulture;
        AppendLines(new List<string> { FormatLine(turnNumber, playerId, "BlockadeTarget", planet, blocker.ToString(ci),
            value.ToString("0.#", ci), neededOffense.ToString("0.#", ci), reason) });
    }

    /// <summary>Ships were sent at the blockade target this turn: T&lt;turn&gt;|P&lt;id&gt;|BlockadeForce|planet|ships|offense|stillNeeded.</summary>
    public static void LogBlockadeForce(int turnNumber, int playerId, string planet, int ships, float offense,
        float stillNeeded)
    {
        if (_currentLogPath == null) return;
        var ci = System.Globalization.CultureInfo.InvariantCulture;
        AppendLines(new List<string> { FormatLine(turnNumber, playerId, "BlockadeForce", planet, ships.ToString(ci),
            offense.ToString("0.#", ci), stillNeeded.ToString("0.#", ci)) });
    }

    /// <summary>A blockade target stopped being the target: T&lt;turn&gt;|P&lt;id&gt;|BlockadeTargetEnd|planet|Cleared, Switched or Unreachable|turnsHeld.</summary>
    public static void LogBlockadeTargetEnd(int turnNumber, int playerId, string planet, string reason, int turnsHeld)
    {
        if (_currentLogPath == null) return;
        AppendLines(new List<string> { FormatLine(turnNumber, playerId, "BlockadeTargetEnd", planet, reason,
            turnsHeld.ToString(System.Globalization.CultureInfo.InvariantCulture)) });
    }

    /// <summary>A research start picked a Warship Offense item while the blockade boost was active: T&lt;turn&gt;|P&lt;id&gt;|OffenseResearchBoost|item|multiplier.</summary>
    public static void LogOffenseResearchBoost(int turnNumber, int playerId, string itemName, float multiplier)
    {
        if (_currentLogPath == null) return;
        AppendLines(new List<string> { FormatLine(turnNumber, playerId, "OffenseResearchBoost", itemName,
            multiplier.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture)) });
    }
```

- [ ] **Step 5: Wire `PlayerAI.PlanShipActions`**

In `Assets/Flatspace/GameAI/PlayerAI.cs`, replace the body of `PlanShipActions` from `var assault = new AssaultPlanner(...)` to the final `return actions;` (and update its summary) with:

```csharp
            // Log-only: which blockade-breaking target the assault has, so start and end are logged on change only.
            private readonly BlockadeTargetTracker _blockadeTargets = new BlockadeTargetTracker();

            /// <summary>
            /// Expand: the home garrison plan, unchanged. Consolidate: choose the assault target (a planet blockaded against
            /// me first, else the enemy-occupied rule), plan home defence with those ships held out, then send whatever is
            /// still spare to the target. Public (and free of Gameboard.Instance) so the self-check can drive it directly.
            /// </summary>
            public List<ShipAction> PlanShipActions(int turnNumber)
            {
                if (Strategy != AIStrategy.AIStrategyConsolidate)
                    return new ShipTransportPlanner(AIMap, Player.playerID).Plan();

                var stats = new WarshipStats(ResearchCatalog != null ? ResearchCatalog.catalogItems : null);
                var assault = new AssaultPlanner(AIMap, Player.playerID, _blockadeView, stats, _blockadeMemory, turnNumber);
                var blockadeTarget = assault.ChooseBlockadeTarget(out var blockadeReason);
                var target = blockadeTarget ?? assault.ChooseEnemyTarget();

                var targetName = target?.PlanetName;
                LogBlockadeTargetChanges(turnNumber, assault, blockadeTarget, blockadeReason);
                if (blockadeTarget == null && targetName != null && targetName != _lastLoggedAssaultTarget)
                    AITuningLogger.LogAssaultTarget(turnNumber, Player.playerID, targetName, assault.RequiredForce());
                _lastLoggedAssaultTarget = blockadeTarget == null ? targetName : null;

                var transport = new ShipTransportPlanner(AIMap, Player.playerID, Strategy)
                {
                    HeldPlanet = targetName,
                };
                var actions = transport.Plan();
                actions.AddRange(assault.Plan(target, transport.LastStates, actions));

                var force = assault.LastBlockadeForce;
                if (blockadeTarget != null && force.Ships > 0)
                    AITuningLogger.LogBlockadeForce(turnNumber, Player.playerID, blockadeTarget.PlanetName,
                        force.Ships, force.Offense, force.StillNeeded);
                return actions;
            }

            private void LogBlockadeTargetChanges(int turnNumber, AssaultPlanner assault, Planet blockadeTarget, string reason)
            {
                BlockadeTargetTracker.Target? current = null;
                if (blockadeTarget != null)
                {
                    var name = blockadeTarget.PlanetName;
                    current = new BlockadeTargetTracker.Target
                    {
                        Planet  = name,
                        Blocker = _blockadeView.Blocker(name),
                        Value   = _blockadeView.Value(name),
                        Needed  = assault.NeededOffense(blockadeTarget),
                        Reason  = reason,
                    };
                }
                foreach (var change in _blockadeTargets.Update(turnNumber, current,
                             name => _blockadeView != null && _blockadeView.IsBlockaded(name)))
                {
                    if (change.Started)
                        AITuningLogger.LogBlockadeTarget(turnNumber, Player.playerID, change.Planet, change.Target.Blocker,
                            change.Target.Value, change.Target.Needed, change.Target.Reason);
                    else
                        AITuningLogger.LogBlockadeTargetEnd(turnNumber, Player.playerID, change.Planet, change.EndReason,
                            change.TurnsHeld);
                }
            }
```

Keep the existing `private string _lastLoggedAssaultTarget;` field line above it untouched.

- [ ] **Step 6: Confirm GREEN**

Ask the user to focus the Editor and run `Run Blockade Breaking Self-Check` (`ALL PASSED`), `Run Ship Transport Self-Check` (the existing `RunConsolidatePlanShipActionsCheck` drives `PlanShipActions` with a null `_blockadeView`, so it must be unchanged), `Run Blockade Avoidance Self-Check`, then `Run All AI Self-Checks`.

- [ ] **Step 7: Commit**

```bash
git add Assets/Flatspace/GameAI/BlockadeTargetTracker.cs Assets/Flatspace/GameAI/BlockadeTargetTracker.cs.meta Assets/Flatspace/Diagnostics/AITuningLogger.cs Assets/Flatspace/GameAI/PlayerAI.cs Assets/Editor/BlockadeBreakSelfCheck.cs
git commit -m "feat: assault goes to blockaded planets first, with BlockadeTarget logging"
```

---

### Task 5: Offense research boost while blockaded

**Files:**
- Modify: `Assets/Flatspace/GameAI/PlayerAI.cs` (`ChooseNewResearch`, `BuildChoiceMatrix`, near lines 1007-1060)
- Modify: `Assets/Editor/BlockadeBreakSelfCheck.cs`

**Interfaces:**
- Consumes: `WarshipStats.OffenseKey`, `WarshipStats.IsWarshipImprovement`, `BlockadeView.BlockadedNames`, `GameAIConstants.blockadedOffenseResearchBoost`, `AITuningLogger.LogOffenseResearchBoost` (Task 4), `ResearchChoiceElement` (a struct with `Item` and `Weight`).
- Produces: `public float PlayerAI.GetResearchSituationalMultiplier(CatalogItem item)`; `public List<ResearchChoiceElement> PlayerAI.ApplyResearchSituationalWeights(List<ResearchChoiceElement> choices)`.

- [ ] **Step 1: Add the failing check**

Add `ok &= RunResearchBoostCheck();` to `RunChecks` and:

```csharp
    private static ResearchChoiceElement Choice(CatalogItem item, float weight)
        => new ResearchChoiceElement { Item = item, Weight = weight };

    // Warship Offense items get the boost only while a blockade against me is visible; Health, Defense and everything else
    // never do; the input list is not mutated.
    public static bool RunResearchBoostCheck()
    {
        var ok = true;
        using (var s = Scenario.Line())
        {
            var off = s.Research.First(i => i.itemName == "Off 1");
            var hp = s.Research.First(i => i.itemName == "Hp 1");
            var food = ScriptableObject.CreateInstance<CatalogItem>();
            food.itemName = "Food 1"; food.type = "Planet Improvement"; food.subType = "Food";
            try
            {
                var input = new List<ResearchChoiceElement> { Choice(off, 0.5f), Choice(hp, 0.5f), Choice(food, 1f) };

                s.AI.RefreshBlockadeView(5);   // nothing blockaded yet
                ok &= Check(Near(s.AI.GetResearchSituationalMultiplier(off), 1f), "no blockade: the multiplier is 1");
                var calm = s.AI.ApplyResearchSituationalWeights(input);
                ok &= Check(Near(calm[0].Weight, 0.5f) && Near(calm[1].Weight, 0.5f) && Near(calm[2].Weight, 1f),
                    "no blockade: weights unchanged");

                s.Ships("B", 0, 1); s.Ships("C", 1, 2);
                s.AI.RefreshBlockadeView(5);   // C is blockaded against me
                ok &= Check(Near(s.AI.GetResearchSituationalMultiplier(off), 2f), "blockaded: Warship Offense is x2");
                ok &= Check(Near(s.AI.GetResearchSituationalMultiplier(hp), 1f), "Health is not boosted");
                ok &= Check(Near(s.AI.GetResearchSituationalMultiplier(food), 1f), "other subtypes are not boosted");
                ok &= Check(Near(s.AI.GetResearchSituationalMultiplier(null), 1f), "a null item is neutral");
                var boosted = s.AI.ApplyResearchSituationalWeights(input);
                ok &= Check(Near(boosted[0].Weight, 1f) && Near(boosted[1].Weight, 0.5f) && Near(boosted[2].Weight, 1f),
                    "blockaded: only Off 1 doubles (0.5 to 1.0, applied on the already-normalized weight)");
                ok &= Check(Near(input[0].Weight, 0.5f), "the input list is not mutated");

                s.Constants.blockadedOffenseResearchBoost = 1f;
                ok &= Check(Near(s.AI.GetResearchSituationalMultiplier(off), 1f), "a boost of 1 disables it");
            }
            finally { Object.DestroyImmediate(food); }
        }
        return ok;
    }
```

- [ ] **Step 2: Confirm RED**

`get_file_problems`: errors for `GetResearchSituationalMultiplier` and `ApplyResearchSituationalWeights`.

- [ ] **Step 3: Implement**

In `PlayerAI.cs`, in `BuildChoiceMatrix`, replace the `var entries = NormalizeResearchWeightsBySubtype(...)` statement with:

```csharp
                var entries = ApplyResearchSituationalWeights(NormalizeResearchWeightsBySubtype(
                    choices.Select(item => new ResearchChoiceElement
                    {
                        Item   = item,
                        Weight = GetResearchWeight(item, strategy),
                    }).ToList()));
```

Directly after `GetResearchWeight`, add:

```csharp
            /// <summary>
            /// The situational factor on a research item's weight: blockadedOffenseResearchBoost for a Warship Offense item
            /// while any blockade against me is visible (blockade value is offense against offense), otherwise 1. Kept out
            /// of the static strategy table, as production's situational weights are.
            /// </summary>
            public float GetResearchSituationalMultiplier(CatalogItem item)
            {
                if (item == null || !WarshipStats.IsWarshipImprovement(item) || item.effect != WarshipStats.OffenseKey)
                    return 1f;
                if (_blockadeView == null || !_blockadeView.BlockadedNames.Any()) return 1f;
                return AIMap.GameAIConstants.blockadedOffenseResearchBoost;
            }

            /// <summary>
            /// Applies the situational multiplier to already-normalized research weights (so Armor and Shields do not dilute
            /// the boost). Returns a new list; the input is not mutated.
            /// </summary>
            public List<ResearchChoiceElement> ApplyResearchSituationalWeights(List<ResearchChoiceElement> choices)
                => choices.Select(c =>
                {
                    var adjusted = c;
                    adjusted.Weight = c.Weight * GetResearchSituationalMultiplier(c.Item);
                    return adjusted;
                }).ToList();
```

In `ChooseNewResearch`, after `currentResearch = actions[0].ChosenItem;` add:

```csharp
                var offenseBoost = GetResearchSituationalMultiplier(currentResearch);
                if (offenseBoost > 1f)
                    AITuningLogger.LogOffenseResearchBoost(Gameboard.Instance.TurnNumber, Player.playerID,
                        currentResearch.itemName, offenseBoost);
```

- [ ] **Step 4: Confirm GREEN**

Focus the Editor, run `Run Blockade Breaking Self-Check` (`ALL PASSED`), `Run Warship Self-Check` (its `RunResearchWeightNormalizationCheck` must still pass), then `Run All AI Self-Checks`.

- [ ] **Step 5: Commit**

```bash
git add Assets/Flatspace/GameAI/PlayerAI.cs Assets/Editor/BlockadeBreakSelfCheck.cs
git commit -m "feat: boost warship offense research while blockaded"
```

---

### Task 6: Documentation and Play-mode check

**Files:**
- Modify: `CLAUDE.md`
- Modify: `FUTURE_FEATURES.md`
- Modify: `.claude/skills/tuning-log/SKILL.md`

- [ ] **Step 1: `CLAUDE.md`**

(a) In the Tests list, add after the `Run Grotsits Short Self-Check` entry: `FlatSpace → AI → Run Blockade Breaking Self-Check` in `Assets/Editor/BlockadeBreakSelfCheck.cs` (target ranking, offense sizing, incoming offense, the target tracker, the research boost), and add `Blockade Breaking` to the `AllAISelfChecks` suite list sentence.

(b) In "Warships and Blockade", after the "Blockade avoidance (resource shipping)" bullet, add:

```
- **Blockade breaking (assault):** under Consolidate the assault's single sticky target is a planet blockaded against the
  player in its `BlockadeView` first (enemy-occupied, own or empty; remembered unseen planets count), else the old
  enemy-occupied rule (`AssaultPlanner.ChooseBlockadeTarget` / `ChooseEnemyTarget` / `ChooseTarget`; an `AssaultPlanner`
  built without a view behaves as before). Ranking, first difference wins: most of my offense committed there (docked +
  in flight), a cut of one of my orders within `blockadeTargetRecentTurns` (default 5, `BlockadeMemory`), the smallest
  offense still needed, the cheapest path from a holder, name. **Connectivity is deliberately not in the ranking yet:
  add the shortest-path betweenness step after the recent-cut step when sub-project 4 builds it.** A blockade target is
  sized by real offense, not ship count: `needed = value x (1 + blockadeBreakMargin) - incoming offense` (margin default
  0.1); `Plan` walks sources cheapest path first, takes the exact ships that would leave (after home defence's claims, via
  `PeekShipSnapshots`) and stops when covered, or sends every spare ship when the fleet falls short (docked offense still
  lowers the blockade value, and the sticky first rank keeps the target while the rest arrives). In-flight offense is
  `Planet.GetIncomingOffense(kind, owner)`, derived and recomputed once per turn from the in-flight ship orders
  (`GameAIMap.RecomputeIncomingOffense`, called in `GameAI.GameAIUpdate` before `ProcessResults`), never saved. Research:
  `PlayerAI.GetResearchSituationalMultiplier` multiplies `Warship Offense` items' weight by `blockadedOffenseResearchBoost`
  (default 2) while any blockade against the player is visible, after `NormalizeResearchWeightsBySubtype`.
  Consolidate-only costs nothing: a visible blockade means a known planet holds another player's ship, which is the
  first-contact test.
```

(c) In the AI Tuning Log paragraph's list of codes add: a blockade-breaking target as `BlockadeTarget|<planet>|<blocker>|<value>|<neededOffense>|<Committed, RecentCut or Cheapest>` (blocker -1 for a remembered unseen planet; logged on change, through `BlockadeTargetTracker`, log-only state, so a load logs the current target once more), the force sent as `BlockadeForce|<planet>|<ships>|<offense>|<stillNeeded>`, a target ending as `BlockadeTargetEnd|<planet>|<Cleared, Switched or Unreachable>|<turnsHeld>`, and a boosted offense research start as `OffenseResearchBoost|<item>|<multiplier>`.

- [ ] **Step 2: `FUTURE_FEATURES.md`**

In item (4) "Revisit garrison and colonization targeting with connectivity", add a sub-bullet:

```
    - *Blockade-breaking target ranking (sub-project 3):* the ranking in `AssaultPlanner.ChooseBlockadeTarget` (committed offense, recent cut, smallest offense needed, path cost, name) was built without connectivity on purpose. When this sub-project builds the C# shortest-path betweenness (from `GameAIMap`'s all-pairs paths), also add it to that ranking as a step after the recent-cut step, weighing chokepoint value and specialised-producer value (see "Weight planet value by connectivity").
```

Change item (3) to mark it built and awaiting the Play-mode check, in the same style the file uses for finished work (so `/update_feature_list` can move it), and keep its remaining sentence "the AI never moves warships in order to blockade" as an open note.

- [ ] **Step 3: `tuning-log` skill**

In `.claude/skills/tuning-log/SKILL.md`, add the new codes to the line-code list and these analyses: turns to clear per blockade (`BlockadeTarget` to `BlockadeTargetEnd|Cleared`, `turnsHeld`), how often `RecentCut` decides a target, whether `Blockade`/`OrderBlocked` events at a planet stop after its `Cleared`, wave coverage from `BlockadeForce` (`stillNeeded` falling to 0, margin tuning), fleet-cap discipline while a force is committed (`WarshipBoost` unchanged), and that `OffenseResearchBoost` lines appear only while a `Blockade` line is recent. Follow the file's existing format for analyses.

- [ ] **Step 4: Play-mode check and hand-off**

Ask the user to run `4p.json` and `test2.json` with `_logAIEvents` on and then `/tuning-log`. Expected: `BlockadeTarget` lines appear after contact, `BlockadeForce` waves precede `BlockadeTargetEnd|...|Cleared`, no fleet-cap violation, no new exceptions in the Console. After the user confirms, run `/update_feature_list` to move item (3) to `completed_features.md`.

- [ ] **Step 5: Commit**

```bash
git add CLAUDE.md FUTURE_FEATURES.md .claude/skills/tuning-log/SKILL.md
git commit -m "docs: blockade breaking in CLAUDE.md, FUTURE_FEATURES.md and the tuning-log skill"
```

---

## Self-review

**Spec coverage.** Candidates and one-target flow (spec 1): Task 2. Ranking including `blockadeTargetRecentTurns`: Task 2. Offense need, margin, incoming offense, exact per-ship planning, partial force, home-claim skip: Tasks 1 and 3 (incoming offense recomputed per turn: documented deviation, spec text updated in Task 1). Research multiplier (spec 3): Task 5. Four log lines, tracker, skill analyses (spec 4): Tasks 4 and 6. No new saved state (spec 5): nothing added to `SaveLoadSystem`. Three tunables (spec 6): Task 1. Self-check coverage list: candidates, ranking, need, planning, incoming offense, research, tracker are in Tasks 1 to 5. Docs and the sub-project 4 follow-up: Task 6.

**Placeholder scan.** No TBD, no "similar to Task N", and every code step shows its code. The Task 6 documentation steps give the exact wording for `CLAUDE.md` and describe the `FUTURE_FEATURES.md` and skill edits in prose because those files have their own formats the implementer must match.

**Type consistency.** `AssaultPlanner(map, playerId, view, stats, memory, turn)`, `ChooseBlockadeTarget(out string)`, `ChooseEnemyTarget()`, `NeededOffense`, `CommittedOffense`, `IsBlockadeTarget`, `Plan`, `LastBlockadeForce` (`Ships`, `Offense`, `StillNeeded`) are used identically in Tasks 2 to 4; `BlockadeTargetTracker.Update(turn, Target?, Func<string,bool>)`, `Target`/`Change` fields match the logger call sites; `GetIncomingOffense`/`AddIncomingOffense`/`ClearIncomingOffense`/`RecomputeIncomingOffense` match between Tasks 1, 2 and 3; the tunable names match everywhere.

**Review Focus.** All five lines have a pinned test: 1 and 2 and 5 in Task 2, 3 in Task 1, 4 in Task 3.
