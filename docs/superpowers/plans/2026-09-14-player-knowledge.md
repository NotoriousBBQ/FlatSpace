# Player Knowledge Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Gate AI colonization targeting on a per-player, simulation-owned "known planets" set instead of the full global planet list, via a new `PlayerKnowledge` class, and close the duplicated-logic risk this raised during brainstorming by giving `PlayerKnowledge` and `FogOfWarSystem` one shared source of "who has vision from where" on `GameAIMap`.

**Architecture:** `GameAIMap` gains two read-only queries over data it already owns (`GetVisionSourcePlanets`, `GetNeighbours`) and a `PlayerKnowledge` instance, updated once per turn from `GameAI.GameAIUpdate()`. `PlayerAI.IsValidColonizationTarget` gains one guard clause reading it. `FogOfWarSystem` is refactored to consume the same two `GameAIMap` queries instead of independently re-deriving them, with no change to its own radius/gradient/rendering behavior.

**Tech Stack:** Unity 6000.4.1f1, C#, UI Toolkit HUD (untouched by this plan), `JsonUtility` saves. No automated test framework exists — verification is a new Editor self-check (`[MenuItem]`, following `Assets/Editor/FogSelfCheck.cs`'s established pattern) plus manual Play-mode testing.

**Spec:** `docs/superpowers/specs/2026-09-14-player-knowledge-design.md`

## Global Constraints

- **No automated test runner or command-line build.** Every task's "run the test" step is: (a) a Rider MCP compile check (`mcp__rider__get_file_problems`) — treat a referenced-but-undefined member as the TDD "red," a clean result as "green" — and (b) the project's actual regression signal, invoking the relevant `[MenuItem]` self-check from the Unity Editor and reading the Console. Since I cannot invoke Unity Editor menu items myself, every task ends by asking the user to run the self-check and report the result before moving on.
- **Namespace casing is inconsistent; match the file you're editing.** `GameAIMap`, `GameAI`, `PlayerAI`, `PlayerKnowledge` are `FlatSpace.AI`. `Planet`, `Ship`, `Player`, `PlanetSpawnData`, `PlanetResourceData`, `GameAIConstants` are in the global namespace — no `using` needed for them.
- **`JsonUtility` save fields are append-only.** The new `knownPlanets` field on `SaveLoadSystem.GameSave.PlayerSave` (Task 6) must be added at the end of the struct, after the existing fog fields — never inserted.
- **A test/self-check must never depend on `Gameboard.Instance`.** `Planet.GetPopulationFraction` currently does, transitively, through `GetPopulationDistribution` — Task 1 removes that dependency (a small, behavior-preserving fix) specifically so `GameAIMap.GetVisionSourcePlanets` — the query this whole feature is centered on — can be exercised by an isolated self-check exactly the way `FogOfWarSystem`'s own test seams already avoid touching `Gameboard`.
- **`Planet.PlanetType.PlanetTypePrime` triggers `GameAIMap.SetInitialOwnership()`** (auto-assigns planet ownership and population ownership). Every synthetic test planet in this plan's self-checks uses `PlanetType.PlanetTypeNormal` instead, to keep test setup independent of that side effect.
- Follow the commit-message and attribution convention already used on this branch's history (`git log` in this repo): a one-paragraph body explaining why, ending with the `Co-Authored-By` / `Claude-Session` lines from this session's system reminder.

---

### Task 1: `GameAIMap` shared vision-source and neighbour queries

**Files:**
- Modify: `Assets/Flatspace/Objects/Planets/Planet.cs` (`GetPopulationDistribution`, around line 724)
- Modify: `Assets/Flatspace/GameAI/GameAIMap.cs` (add queries; call the new build step from `GameAIMapInit`, around line 76)
- Create: `Assets/Editor/PlayerKnowledgeSelfCheck.cs`

**Interfaces:**
- Produces: `GameAIMap.VisionSource { Planet Planet; bool HasPopulation; bool HasOwnedShip; }`
- Produces: `GameAIMap.GetVisionSourcePlanets(int playerId) -> List<GameAIMap.VisionSource>`
- Produces: `GameAIMap.GetNeighbours(string planetName) -> IReadOnlyList<string>`
- Produces (test-only, reused by Tasks 2, 4, 6): `PlayerKnowledgeSelfCheck.MakeSpawn(string name, int initialPopulation, IEnumerable<string> connections = null) -> PlanetSpawnData`

- [ ] **Step 1: Fix `Planet.GetPopulationDistribution` to not depend on `Gameboard.Instance`**

Open `Assets/Flatspace/Objects/Planets/Planet.cs`. Find (around line 724):

```csharp
    private void GetPopulationDistribution(out Dictionary<int, int> popDistribution)
    {
        popDistribution = new Dictionary<int, int>();
        for (var i = 0; i < Gameboard.Instance.players.Count; i++)
        {
            var popByPlayer = Population.FindAll(x => x.Player == i).Count;
            if(popByPlayer > 0)
                popDistribution.Add(i, popByPlayer);
        }
    }
```

Replace it with:

```csharp
    private void GetPopulationDistribution(out Dictionary<int, int> popDistribution)
    {
        popDistribution = Population
            .GroupBy(x => x.Player)
            .ToDictionary(g => g.Key, g => g.Count());
    }
```

This is behavior-preserving: the old loop only ever added an entry for a player ID with at least one
inhabitant (guarded by `if (popByPlayer > 0)`), and the new grouping does exactly the same — it just no
longer needs to know the total player count up front to do it, which is what let it depend on
`Gameboard.Instance`. `System.Linq` is already imported at the top of this file.

- [ ] **Step 2: Write the failing self-check**

Create `Assets/Editor/PlayerKnowledgeSelfCheck.cs`:

```csharp
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using FlatSpace.AI;

public static class PlayerKnowledgeSelfCheck
{
    [MenuItem("FlatSpace/AI/Run Player Knowledge Self-Check")]
    public static void Run()
    {
        var ok = RunGameAIMapSharedQueriesCheck();
        Debug.Log(ok
            ? "[PlayerKnowledgeSelfCheck] ALL PASSED"
            : "[PlayerKnowledgeSelfCheck] FAILURES (see errors above)");
    }

    private static bool Check(bool condition, string label)
    {
        if (!condition) Debug.LogError($"[PlayerKnowledgeSelfCheck] FAIL: {label}");
        return condition;
    }

    private static PlanetSpawnData MakeSpawn(string name, int initialPopulation,
        IEnumerable<string> connections = null)
    {
        var resourceData = ScriptableObject.CreateInstance<PlanetResourceData>();
        resourceData._initialPopulation = initialPopulation;
        resourceData._maxPopulation = 5;

        var spawn = ScriptableObject.CreateInstance<PlanetSpawnData>();
        spawn._planetName = name;
        spawn._planetPosition = Vector3.zero;
        spawn._planetType = Planet.PlanetType.PlanetTypeNormal;
        spawn._resourceData = resourceData;
        spawn._connections = connections != null ? new List<string>(connections) : new List<string>();
        return spawn;
    }

    public static bool RunGameAIMapSharedQueriesCheck()
    {
        var ok = true;
        var mapGo = new GameObject("PKSelfCheckMap_SharedQueries");
        try
        {
            var map = mapGo.AddComponent<GameAIMap>();
            var constants = ScriptableObject.CreateInstance<GameAIConstants>();

            var spawns = new List<PlanetSpawnData>
            {
                MakeSpawn("A", initialPopulation: 1, connections: new[] { "B" }), // only A declares the link
                MakeSpawn("B", initialPopulation: 0),
                MakeSpawn("C", initialPopulation: 0),
                MakeSpawn("D", initialPopulation: 0),
            };
            map.GameAIMapInit(spawns, constants);
            map.GetPlanet("C").DockShipFromSave(Ship.ShipKind.WarShip, 0, new List<string>());

            var sourcesForPlayer0 = map.GetVisionSourcePlanets(0);
            ok &= Check(sourcesForPlayer0.Exists(s => s.Planet.PlanetName == "A" && s.HasPopulation && !s.HasOwnedShip),
                "A is a population source for player 0");
            ok &= Check(sourcesForPlayer0.Exists(s => s.Planet.PlanetName == "C" && s.HasOwnedShip && !s.HasPopulation),
                "C is a ship source for player 0");
            ok &= Check(!sourcesForPlayer0.Exists(s => s.Planet.PlanetName == "B" || s.Planet.PlanetName == "D"),
                "B and D are not sources for player 0");

            var sourcesForPlayer1 = map.GetVisionSourcePlanets(1);
            ok &= Check(sourcesForPlayer1.Count == 0,
                "player 1 has no sources (A's population and C's ship both belong to player 0)");

            ok &= Check(map.GetNeighbours("A").Contains("B"), "A's declared connection to B is a neighbour");
            ok &= Check(map.GetNeighbours("B").Contains("A"),
                "B is symmetrized as A's neighbour even though B never declared it");
            ok &= Check(map.GetNeighbours("C").Count == 0, "C has no connections");
            ok &= Check(map.GetNeighbours("NoSuchPlanet").Count == 0,
                "unknown planet name returns an empty neighbour list, not null/throw");
        }
        finally
        {
            Object.DestroyImmediate(mapGo);
        }
        return ok;
    }
}
```

- [ ] **Step 3: Compile-check to confirm it fails**

Run: `mcp__rider__get_file_problems` on `Assets/Editor/PlayerKnowledgeSelfCheck.cs` and
`Assets/Flatspace/GameAI/GameAIMap.cs`.
Expected: errors like `'GameAIMap' does not contain a definition for 'GetVisionSourcePlanets'` and
`'GameAIMap' does not contain a definition for 'GetNeighbours'` — the members referenced above don't
exist yet.

- [ ] **Step 4: Implement `GameAIMap`'s shared queries**

Open `Assets/Flatspace/GameAI/GameAIMap.cs`. Add, as new public members of the `GameAIMap` class
(e.g. directly after the existing `GetPlanet` method around line 158):

```csharp
            public struct VisionSource
            {
                public Planet Planet;
                public bool HasPopulation;
                public bool HasOwnedShip;
            }

            /// <summary>
            /// Planets that currently give playerId vision: population presence or a docked ship
            /// they own. Shared by PlayerKnowledge (sticky, planet-level knowledge) and
            /// FogOfWarSystem (radius/gradient rendering) so both read the exact same underlying
            /// fact instead of two independently-derived copies of it.
            /// </summary>
            public List<VisionSource> GetVisionSourcePlanets(int playerId)
            {
                var result = new List<VisionSource>();
                foreach (var planet in PlanetList)
                {
                    var hasPopulation = planet.GetPopulationFraction(playerId) > 0f;
                    var hasOwnedShip = planet.DockedShips.Exists(s => s.Owner == playerId);
                    if (hasPopulation || hasOwnedShip)
                        result.Add(new VisionSource
                        {
                            Planet = planet,
                            HasPopulation = hasPopulation,
                            HasOwnedShip = hasOwnedShip
                        });
                }
                return result;
            }

            private static readonly List<string> EmptyNeighbours = new List<string>();
            private Dictionary<string, List<string>> _neighbours;

            /// <summary>
            /// Planet.Connections, symmetrized: a link declared on only one side still appears in
            /// both planets' neighbour lists. Built once in GameAIMapInit.
            /// </summary>
            public IReadOnlyList<string> GetNeighbours(string planetName)
            {
                return _neighbours != null && _neighbours.TryGetValue(planetName, out var list)
                    ? list
                    : EmptyNeighbours;
            }

            private void BuildNeighbours()
            {
                _neighbours = new Dictionary<string, List<string>>();
                foreach (var planet in PlanetList)
                    _neighbours[planet.PlanetName] = new List<string>();

                foreach (var planet in PlanetList)
                {
                    if (planet.Connections == null) continue;
                    foreach (var name in planet.Connections)
                    {
                        if (name == null || name == planet.PlanetName || !_neighbours.ContainsKey(name))
                            continue;
                        if (!_neighbours[planet.PlanetName].Contains(name))
                            _neighbours[planet.PlanetName].Add(name);
                        if (!_neighbours[name].Contains(planet.PlanetName))
                            _neighbours[name].Add(planet.PlanetName);
                    }
                }
            }
```

Then, in `GameAIMapInit` (around line 76), call it right after the planet-creation loop and before
pathing is built:

```csharp
                foreach (var planetSpawnData in spawnDataList)
                {
                    var planet = this.AddComponent<Planet>() as Planet;
                    planet.Init(planetSpawnData, this.transform, GameAIConstants);
                    _planets[planetSpawnData._planetName] = planet;
                }

                BuildNeighbours();

                PathingSystem.Instance.InitializePathMap(PlanetList);
```

- [ ] **Step 5: Compile-check to confirm it passes**

Run: `mcp__rider__get_file_problems` on the same two files.
Expected: no errors.

- [ ] **Step 6: Ask the user to run the self-check in the Unity Editor**

Ask the user to open the Unity Editor (letting it recompile) and run
`FlatSpace > AI > Run Player Knowledge Self-Check`, then report the Console output. Do not proceed to
Task 2 until they confirm `[PlayerKnowledgeSelfCheck] ALL PASSED`.

- [ ] **Step 7: Commit**

```bash
git add Assets/Flatspace/Objects/Planets/Planet.cs Assets/Flatspace/GameAI/GameAIMap.cs Assets/Editor/PlayerKnowledgeSelfCheck.cs
git commit -m "feat(ai): add shared vision-source and neighbour queries to GameAIMap"
```

---

### Task 2: `PlayerKnowledge` class

**Files:**
- Create: `Assets/Flatspace/GameAI/PlayerKnowledge.cs`
- Modify: `Assets/Editor/PlayerKnowledgeSelfCheck.cs` (add checks, wire into `Run()`)

**Interfaces:**
- Consumes: `GameAIMap.GetVisionSourcePlanets(int) -> List<GameAIMap.VisionSource>`, `GameAIMap.GetNeighbours(string) -> IReadOnlyList<string>` (Task 1); `PlayerKnowledgeSelfCheck.MakeSpawn(...)` (Task 1)
- Produces: `PlayerKnowledge.IsKnown(int playerId, string planetName) -> bool`
- Produces: `PlayerKnowledge.KnownPlanets(int playerId) -> IReadOnlyCollection<string>`
- Produces: `PlayerKnowledge.Update(GameAIMap map, int numPlayers)`
- Produces: `PlayerKnowledge.SetKnownPlanets(int playerId, IEnumerable<string> names)`

- [ ] **Step 1: Write the failing self-checks**

Add to `Assets/Editor/PlayerKnowledgeSelfCheck.cs`, inside the `PlayerKnowledgeSelfCheck` class:

```csharp
    public static bool RunPlayerKnowledgeChecks()
    {
        var basics = RunPlayerKnowledgeBasicsCheck();
        var growth = RunPlayerKnowledgeGrowthCheck();
        return basics && growth;
    }

    private static bool RunPlayerKnowledgeBasicsCheck()
    {
        var ok = true;
        var mapGo = new GameObject("PKSelfCheckMap_Knowledge");
        try
        {
            var map = mapGo.AddComponent<GameAIMap>();
            var constants = ScriptableObject.CreateInstance<GameAIConstants>();

            var spawns = new List<PlanetSpawnData>
            {
                MakeSpawn("A", initialPopulation: 1, connections: new[] { "B" }),
                MakeSpawn("B", initialPopulation: 0),
                MakeSpawn("C", initialPopulation: 0), // not connected to A or B
            };
            map.GameAIMapInit(spawns, constants);

            var knowledge = new PlayerKnowledge();
            knowledge.Update(map, numPlayers: 1);

            ok &= Check(knowledge.IsKnown(0, "A"), "source planet A is known");
            ok &= Check(knowledge.IsKnown(0, "B"), "A's direct neighbour B is known");
            ok &= Check(!knowledge.IsKnown(0, "C"), "C is unconnected and unknown");
            ok &= Check(!knowledge.IsKnown(1, "A"), "player 1 has no sources and knows nothing");

            // Stickiness: A's population disappears (e.g. colonized away), but knowledge persists
            // through the next Update() rather than being recomputed from scratch.
            map.GetPlanet("A").Population.Clear();
            knowledge.Update(map, numPlayers: 1);

            ok &= Check(knowledge.IsKnown(0, "A"), "A stays known after its only source disappears (sticky)");
            ok &= Check(knowledge.IsKnown(0, "B"), "B stays known too, for the same reason");
            ok &= Check(!knowledge.IsKnown(0, "C"), "C is still unknown - nothing ever made it a source or neighbour");
        }
        finally
        {
            Object.DestroyImmediate(mapGo);
        }
        return ok;
    }

    private static bool RunPlayerKnowledgeGrowthCheck()
    {
        var ok = true;
        var mapGo = new GameObject("PKSelfCheckMap_Growth");
        try
        {
            var map = mapGo.AddComponent<GameAIMap>();
            var constants = ScriptableObject.CreateInstance<GameAIConstants>();

            var spawns = new List<PlanetSpawnData>
            {
                MakeSpawn("A", initialPopulation: 1, connections: new[] { "B" }),
                MakeSpawn("B", initialPopulation: 0, connections: new[] { "C" }),
                MakeSpawn("C", initialPopulation: 0),
            };
            map.GameAIMapInit(spawns, constants);

            var knowledge = new PlayerKnowledge();
            knowledge.Update(map, numPlayers: 1);
            ok &= Check(!knowledge.IsKnown(0, "C"),
                "C is two hops from A and unknown while B is not yet a source");

            map.GetPlanet("B").Population.Add(new Planet.Inhabitant { Player = 0 });
            knowledge.Update(map, numPlayers: 1);
            ok &= Check(knowledge.IsKnown(0, "C"),
                "C becomes known once B (its neighbour) becomes a source and Update() runs again");
        }
        finally
        {
            Object.DestroyImmediate(mapGo);
        }
        return ok;
    }
```

Update `Run()` to also call the new check:

```csharp
    public static void Run()
    {
        var ok = RunGameAIMapSharedQueriesCheck();
        ok &= RunPlayerKnowledgeChecks();
        Debug.Log(ok
            ? "[PlayerKnowledgeSelfCheck] ALL PASSED"
            : "[PlayerKnowledgeSelfCheck] FAILURES (see errors above)");
    }
```

- [ ] **Step 2: Compile-check to confirm it fails**

Run: `mcp__rider__get_file_problems` on `Assets/Editor/PlayerKnowledgeSelfCheck.cs`.
Expected: `The type or namespace name 'PlayerKnowledge' could not be found`.

- [ ] **Step 3: Implement `PlayerKnowledge`**

Create `Assets/Flatspace/GameAI/PlayerKnowledge.cs`:

```csharp
using System;
using System.Collections.Generic;

namespace FlatSpace
{
    namespace AI
    {
        /// <summary>
        /// Per-player, sticky "has this player ever discovered planet X" tracker. A planet becomes
        /// known when it is a vision source for that player (population or a docked ship) or when
        /// it is a direct neighbour (GameAIMap.GetNeighbours, symmetrized Planet.Connections) of a
        /// source planet. Knowledge is never removed once granted.
        /// </summary>
        public class PlayerKnowledge
        {
            private readonly Dictionary<int, HashSet<string>> _known = new Dictionary<int, HashSet<string>>();

            public bool IsKnown(int playerId, string planetName) =>
                _known.TryGetValue(playerId, out var set) && set.Contains(planetName);

            public IReadOnlyCollection<string> KnownPlanets(int playerId) =>
                _known.TryGetValue(playerId, out var set) ? set : Array.Empty<string>();

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

            public void SetKnownPlanets(int playerId, IEnumerable<string> names)
            {
                _known[playerId] = new HashSet<string>(names);
            }
        }
    }
}
```

Add `using FlatSpace.AI;` to the top of `Assets/Editor/PlayerKnowledgeSelfCheck.cs` if it is not
already there from Task 1 (it is — `GameAIMap` needed it too).

- [ ] **Step 4: Compile-check to confirm it passes**

Run: `mcp__rider__get_file_problems` on both files.
Expected: no errors.

- [ ] **Step 5: Ask the user to run the self-check**

Ask the user to let the Editor recompile and run `FlatSpace > AI > Run Player Knowledge Self-Check`,
then report the Console output. Do not proceed to Task 3 until they confirm all checks pass.

- [ ] **Step 6: Commit**

```bash
git add Assets/Flatspace/GameAI/PlayerKnowledge.cs Assets/Editor/PlayerKnowledgeSelfCheck.cs
git commit -m "feat(ai): add PlayerKnowledge, a sticky per-player known-planets tracker"
```

---

### Task 3: Wire `PlayerKnowledge` into `GameAIMap` and the turn loop

**Files:**
- Modify: `Assets/Flatspace/GameAI/GameAIMap.cs` (construct `Knowledge` in `GameAIMapInit`)
- Modify: `Assets/Flatspace/GameAI/GameAI.cs` (`GameAIUpdate`, around line 71-81)

**Interfaces:**
- Consumes: `PlayerKnowledge` (Task 2)
- Produces: `GameAIMap.Knowledge` (public property, type `PlayerKnowledge`) — read by Task 4 and by save/load in Task 6

- [ ] **Step 1: Add the `Knowledge` property to `GameAIMap`**

Open `Assets/Flatspace/GameAI/GameAIMap.cs`. Add a public property near the top of the class (next to
`GameAIConstants`):

```csharp
            public PlayerKnowledge Knowledge { get; private set; }
```

In `GameAIMapInit` (the same method touched in Task 1), construct it — add this line right after
`_planets = new Dictionary<string, Planet>();`:

```csharp
                Knowledge = new PlayerKnowledge();
```

- [ ] **Step 2: Compile-check**

Run: `mcp__rider__get_file_problems` on `Assets/Flatspace/GameAI/GameAIMap.cs`.
Expected: no errors (this step only adds a property and a constructor call using a type that already
exists from Task 2).

- [ ] **Step 3: Wire `Update()` into the turn loop**

Open `Assets/Flatspace/GameAI/GameAI.cs`. Find `GameAIUpdate()` (around line 71):

```csharp
            public void GameAIUpdate()
            {
                ...
                ProcessCurrentOrders();
                ...
                UpdateAllPlanets(planetUpdateResults);
                ProcessResults(planetUpdateResults, gameAIOrders);
                ...
                ProcessNewOrders(gameAIOrders);
            }
```

Insert one line between `UpdateAllPlanets(...)` and `ProcessResults(...)`:

```csharp
                UpdateAllPlanets(planetUpdateResults);
                GameAIMap.Knowledge.Update(GameAIMap, Gameboard.Instance.players.Count);
                ProcessResults(planetUpdateResults, gameAIOrders);
```

This runs after this turn's arrivals/colonizations have already been applied
(`ProcessCurrentOrders()`) but before this turn's `PlayerAI` decisions are made
(`ProcessResults()`), so colonization targeting always sees this turn's freshest knowledge with no
lag — see the spec's "Load-bearing facts" section for why that ordering matters.

- [ ] **Step 4: Compile-check**

Run: `mcp__rider__get_file_problems` on `Assets/Flatspace/GameAI/GameAI.cs`.
Expected: no errors.

- [ ] **Step 5: Ask the user to Play-test**

This change has no isolated self-check of its own (it wires two already-tested pieces together
inside a method that itself depends on a live `Gameboard` scene — see the Global Constraints note on
avoiding `Gameboard.Instance` in self-checks). Ask the user to enter Play mode from
`Assets/Scenes/Flatspace.unity`, run a few turns, and confirm nothing throws and the game behaves as
before (this task changes no observable behavior yet — `IsValidColonizationTarget` isn't reading
`Knowledge` until Task 4). Do not proceed to Task 4 until they confirm this.

- [ ] **Step 6: Commit**

```bash
git add Assets/Flatspace/GameAI/GameAIMap.cs Assets/Flatspace/GameAI/GameAI.cs
git commit -m "feat(ai): update PlayerKnowledge once per turn, before AI decisions run"
```

---

### Task 4: Gate colonization targeting on `PlayerKnowledge`

**Files:**
- Modify: `Assets/Flatspace/GameAI/PlayerAI.cs` (`IsValidColonizationTarget`, around line 102)
- Modify: `Assets/Editor/PlayerKnowledgeSelfCheck.cs` (add check, wire into `Run()`)

**Interfaces:**
- Consumes: `GameAIMap.Knowledge` (Task 3), `PlayerAI.Player`/`PlayerAI.AIMap` (existing public
  settable properties), `PlayerKnowledgeSelfCheck.MakeSpawn(...)` (Task 1)
- Produces: `PlayerAI.IsValidColonizationTarget(Planet planet) -> bool` (visibility raised from
  `private` to `public`, same reason `FogOfWarSystem.InitForTest` had to become `public` earlier this
  project: `Assets/Editor/` is a separate assembly and cannot see `internal` members without an
  `InternalsVisibleTo` that isn't configured here)

- [ ] **Step 1: Write the failing self-check**

Add to `Assets/Editor/PlayerKnowledgeSelfCheck.cs`:

```csharp
    public static bool RunColonizationKnowledgeGateCheck()
    {
        var ok = true;
        var mapGo = new GameObject("PKSelfCheckMap_Colonization");
        var playerGo = new GameObject("PKSelfCheckPlayer_Colonization");
        try
        {
            var map = mapGo.AddComponent<GameAIMap>();
            var constants = ScriptableObject.CreateInstance<GameAIConstants>();
            constants.defaultTravelSpeed = 1f;
            constants.expandPopulationTrigger = 0.8f;
            constants.maxPathNodesForResourceDistribution = 10;

            var spawns = new List<PlanetSpawnData>
            {
                MakeSpawn("Home", initialPopulation: 1, connections: new[] { "Target" }),
                MakeSpawn("Target", initialPopulation: 0),
            };
            map.GameAIMapInit(spawns, constants);

            var player = playerGo.AddComponent<Player>();
            var playerAI = playerGo.AddComponent<PlayerAI>();
            playerAI.Player = player;
            playerAI.AIMap = map;
            player.playerID = 0;

            var target = map.GetPlanet("Target");

            ok &= Check(!playerAI.IsValidColonizationTarget(target),
                "an undiscovered planet is not a valid colonization target");

            map.Knowledge.Update(map, numPlayers: 1);

            ok &= Check(playerAI.IsValidColonizationTarget(target),
                "the same planet becomes valid once PlayerKnowledge marks it known");
        }
        finally
        {
            Object.DestroyImmediate(playerGo);
            Object.DestroyImmediate(mapGo);
        }
        return ok;
    }
```

Update `Run()`:

```csharp
    public static void Run()
    {
        var ok = RunGameAIMapSharedQueriesCheck();
        ok &= RunPlayerKnowledgeChecks();
        ok &= RunColonizationKnowledgeGateCheck();
        Debug.Log(ok
            ? "[PlayerKnowledgeSelfCheck] ALL PASSED"
            : "[PlayerKnowledgeSelfCheck] FAILURES (see errors above)");
    }
```

- [ ] **Step 2: Compile-check to confirm it fails**

Run: `mcp__rider__get_file_problems` on `Assets/Editor/PlayerKnowledgeSelfCheck.cs`.
Expected: `'PlayerAI.IsValidColonizationTarget(Planet)' is inaccessible due to its protection level`.

- [ ] **Step 3: Add the guard clause and raise visibility**

Open `Assets/Flatspace/GameAI/PlayerAI.cs`. Find (around line 102):

```csharp
            private bool IsValidColonizationTarget(Planet planet)
            {
                if (planet.IsPopulationTransferInProgress(Player.playerID)) return false;
                if (planet.Population.Count == 0)                           return true;
                if (planet.Population.Count >= planet.MaxPopulation)        return false;
                return planet.PlayerWithMostPopulation() != Player.playerID;
            }
```

Replace it with:

```csharp
            // Public for the FlatSpace/AI self-check (Assets/Editor is a separate assembly).
            public bool IsValidColonizationTarget(Planet planet)
            {
                if (!AIMap.Knowledge.IsKnown(Player.playerID, planet.PlanetName)) return false;
                if (planet.IsPopulationTransferInProgress(Player.playerID))       return false;
                if (planet.Population.Count == 0)                                return true;
                if (planet.Population.Count >= planet.MaxPopulation)             return false;
                return planet.PlayerWithMostPopulation() != Player.playerID;
            }
```

Both existing callers — `ProcessColonizers`'s global target list, and
`GetIndustrySituationalWeightMultiplier`'s "urgently boost ColonyShip production" check via
`PlanetCanColonize` — funnel through this one method, so this single change covers both.

- [ ] **Step 4: Compile-check to confirm it passes**

Run: `mcp__rider__get_file_problems` on both files.
Expected: no errors.

- [ ] **Step 5: Ask the user to run the self-check, then Play-test**

Ask the user to let the Editor recompile, run `FlatSpace > AI > Run Player Knowledge Self-Check`, and
report the Console output. Once that passes, ask them to Play-test from
`Assets/Scenes/Flatspace.unity`: run turns until an AI colonizes a new planet, and confirm colonization
still happens normally (each player's home planet and its connected neighbours are known from turn
one, since a player's home planet is always a population source). Do not proceed to Task 5 until both
are confirmed.

- [ ] **Step 6: Commit**

```bash
git add Assets/Flatspace/GameAI/PlayerAI.cs Assets/Editor/PlayerKnowledgeSelfCheck.cs
git commit -m "feat(ai): gate colonization targeting on PlayerKnowledge"
```

---

### Task 5: `FogOfWarSystem` consumes the shared `GameAIMap` queries

**Files:**
- Modify: `Assets/Flatspace/Fog/FogOfWarSystem.cs` (`Init`, `BuildAdjacency`, `Recompute`)
- Modify: `Assets/Flatspace/Objects/Board/GameBoard.cs` (`InitFogOfWar`, the `Init(...)` call site,
  around line 310)

**Interfaces:**
- Consumes: `GameAIMap.GetVisionSourcePlanets(int)`, `GameAIMap.GetNeighbours(string)` (Task 1)
- Changes: `FogOfWarSystem.Init(IReadOnlyList<Planet> planets, FogOfWarSettings settings, int
  numPlayers)` → `FogOfWarSystem.Init(GameAIMap map, FogOfWarSettings settings, int numPlayers)`.
  `FogOfWarSystem.InitForTest` (the existing self-check test seam) is untouched — it already builds
  its own `_adjacency` directly and never calls `BuildAdjacency`.

This task is a pure refactor: fog's own radius/openness/corridor/gradient behavior does not change,
only where it gets its two raw inputs (which planets are sources; which planets are neighbours) from.
Verification is re-running the *existing* `FogSelfCheck` (unchanged by this task) to confirm no
regression, plus Play-mode comparison.

- [ ] **Step 1: Change `Init`'s signature and `BuildAdjacency` to use the shared queries**

Open `Assets/Flatspace/Fog/FogOfWarSystem.cs`. Replace (around line 40):

```csharp
        public void Init(IReadOnlyList<Planet> planets, FogOfWarSettings settings, int numPlayers)
        {
            var positions = new List<Vector2>(planets.Count);
            _planetNames = new List<string>(planets.Count);
            foreach (var p in planets)
            {
                positions.Add(p.Position);
                _planetNames.Add(p.PlanetName);
            }
            BuildAdjacency(planets);
            var segments = GatherSegments();
            InitCore(positions, segments, settings, numPlayers);
        }

        private void BuildAdjacency(IReadOnlyList<Planet> planets)
        {
            var nameToIndex = new Dictionary<string, int>(planets.Count);
            for (var i = 0; i < planets.Count; i++) nameToIndex[planets[i].PlanetName] = i;

            _adjacency = new List<int>[planets.Count];
            for (var i = 0; i < planets.Count; i++) _adjacency[i] = new List<int>();
            for (var i = 0; i < planets.Count; i++)
            {
                var conns = planets[i].Connections;
                if (conns == null) continue;
                foreach (var name in conns)
                    if (name != null && nameToIndex.TryGetValue(name, out var j) && j != i)
                    {
                        if (!_adjacency[i].Contains(j)) _adjacency[i].Add(j);
                        if (!_adjacency[j].Contains(i)) _adjacency[j].Add(i);
                    }
            }
        }
```

with:

```csharp
        public void Init(GameAIMap map, FogOfWarSettings settings, int numPlayers)
        {
            var planets = map.PlanetList;
            var positions = new List<Vector2>(planets.Count);
            _planetNames = new List<string>(planets.Count);
            foreach (var p in planets)
            {
                positions.Add(p.Position);
                _planetNames.Add(p.PlanetName);
            }
            BuildAdjacency(map, planets);
            var segments = GatherSegments();
            InitCore(positions, segments, settings, numPlayers);
        }

        private void BuildAdjacency(GameAIMap map, IReadOnlyList<Planet> planets)
        {
            var nameToIndex = new Dictionary<string, int>(planets.Count);
            for (var i = 0; i < planets.Count; i++) nameToIndex[planets[i].PlanetName] = i;

            _adjacency = new List<int>[planets.Count];
            for (var i = 0; i < planets.Count; i++) _adjacency[i] = new List<int>();
            for (var i = 0; i < planets.Count; i++)
                foreach (var name in map.GetNeighbours(planets[i].PlanetName))
                    if (name != null && nameToIndex.TryGetValue(name, out var j) && j != i)
                        if (!_adjacency[i].Contains(j)) _adjacency[i].Add(j);
        }
```

(`map.GetNeighbours` already returns a symmetrized list, so the manual "add both directions" mirroring
the old code needed is no longer necessary — each planet's own call already carries its full symmetric
set.)

- [ ] **Step 2: Update `Recompute`'s source-gathering to use the shared query**

In the same file, find `Recompute()` (around line 143). Replace:

```csharp
            var sources = new List<(int player, Vector2 pos, float radius)>();
            var planetList = gameAI.GameAIMap.PlanetList;

            for (var pl = 0; pl < _players.Length; pl++)
            {
                foreach (var planet in planetList)
                {
                    if (planet.GetPopulationFraction(pl) > 0f)
                        sources.Add((pl, planet.Position, _settings.planetVisionRadius));
                    foreach (var ship in planet.DockedShips)
                        if (ship.Owner == pl)
                        {
                            sources.Add((pl, planet.Position, _settings.shipVisionRadius));
                            break;
                        }
                }
            }
```

with:

```csharp
            var sources = new List<(int player, Vector2 pos, float radius)>();

            for (var pl = 0; pl < _players.Length; pl++)
            {
                foreach (var source in gameAI.GameAIMap.GetVisionSourcePlanets(pl))
                {
                    if (source.HasPopulation)
                        sources.Add((pl, source.Planet.Position, _settings.planetVisionRadius));
                    if (source.HasOwnedShip)
                        sources.Add((pl, source.Planet.Position, _settings.shipVisionRadius));
                }
            }
```

(The `planetList` local is no longer read anywhere else in `Recompute`, so it is simply dropped rather
than left unused.)

- [ ] **Step 3: Update the call site in `Gameboard`**

Open `Assets/Flatspace/Objects/Board/GameBoard.cs`. Find, in `InitFogOfWar()` (around line 310):

```csharp
                _fogOfWarSystem.Init(GameAI.GameAIMap.PlanetList, _fogOfWarSettings, NumPlayers);
```

Replace with:

```csharp
                _fogOfWarSystem.Init(GameAI.GameAIMap, _fogOfWarSettings, NumPlayers);
```

- [ ] **Step 4: Compile-check**

Run: `mcp__rider__get_file_problems` on `Assets/Flatspace/Fog/FogOfWarSystem.cs` and
`Assets/Flatspace/Objects/Board/GameBoard.cs`.
Expected: no errors.

- [ ] **Step 5: Ask the user to re-run the existing fog self-check, then Play-test**

Ask the user to let the Editor recompile, run `FlatSpace > Fog > Run Self-Check` (the pre-existing
check, unmodified by this task), and confirm `[FogSelfCheck] ALL PASSED` — this is the regression
signal that the refactor changed nothing observable. Then ask them to Play-test from
`Assets/Scenes/Flatspace.unity`: switch the fog debug dropdown through `No Fog` / `All Players` /
each `Player n`, and confirm the map looks exactly as it did before this task (same vision circles,
same adjacency-explored neighbours). Do not proceed to Task 6 until both are confirmed.

- [ ] **Step 6: Commit**

```bash
git add Assets/Flatspace/Fog/FogOfWarSystem.cs Assets/Flatspace/Objects/Board/GameBoard.cs
git commit -m "refactor(fog): read vision sources and neighbours from GameAIMap"
```

---

### Task 6: Persist `PlayerKnowledge` in the save file

**Files:**
- Modify: `Assets/Flatspace/SaveSystem/SaveLoadSystem.cs` (`PlayerSave` struct around line 89; `GameSave`
  constructor around line 137)
- Modify: `Assets/Flatspace/Objects/Board/GameBoard.cs` (`SetSimulationStatus`, around line 230)
- Modify: `Assets/Editor/PlayerKnowledgeSelfCheck.cs` (add check, wire into `Run()`)

**Interfaces:**
- Consumes: `PlayerKnowledge.KnownPlanets(int)`, `PlayerKnowledge.SetKnownPlanets(int, IEnumerable<string>)` (Task 2); `GameAIMap.Knowledge` (Task 3)
- Produces: `SaveLoadSystem.GameSave.PlayerSave.knownPlanets` (new, appended field, type `List<string>`)

- [ ] **Step 1: Write the failing self-check**

Add to `Assets/Editor/PlayerKnowledgeSelfCheck.cs`:

```csharp
    public static bool RunKnownPlanetsSaveRoundTripCheck()
    {
        var ok = true;
        var knowledge = new PlayerKnowledge();
        knowledge.SetKnownPlanets(0, new List<string> { "A", "B" });

        var savedNames = new List<string>(knowledge.KnownPlanets(0));

        var reloaded = new PlayerKnowledge();
        reloaded.SetKnownPlanets(0, savedNames);

        ok &= Check(reloaded.IsKnown(0, "A") && reloaded.IsKnown(0, "B"),
            "known planets round-trip through KnownPlanets/SetKnownPlanets");
        ok &= Check(!reloaded.IsKnown(0, "C"), "an unlisted planet stays unknown after round trip");
        ok &= Check(reloaded.KnownPlanets(0).Count == 2, "round trip does not add or drop entries");
        return ok;
    }
```

Update `Run()`:

```csharp
    public static void Run()
    {
        var ok = RunGameAIMapSharedQueriesCheck();
        ok &= RunPlayerKnowledgeChecks();
        ok &= RunColonizationKnowledgeGateCheck();
        ok &= RunKnownPlanetsSaveRoundTripCheck();
        Debug.Log(ok
            ? "[PlayerKnowledgeSelfCheck] ALL PASSED"
            : "[PlayerKnowledgeSelfCheck] FAILURES (see errors above)");
    }
```

This check doesn't reference anything new — `SetKnownPlanets`/`KnownPlanets` already exist from Task
2 — so it should compile and pass immediately. It exists to lock in the round-trip contract that
Steps 3-4 below rely on before the save/load wiring is added.

- [ ] **Step 2: Compile-check and run to confirm it passes on its own**

Run: `mcp__rider__get_file_problems` on `Assets/Editor/PlayerKnowledgeSelfCheck.cs`.
Expected: no errors. Ask the user to run `FlatSpace > AI > Run Player Knowledge Self-Check` and
confirm this new check passes before continuing (it is testing existing Task 2 behavior, so it
should — this confirms the harness itself is wired correctly before Step 3 changes anything).

- [ ] **Step 3: Add the field and wire the save side**

Open `Assets/Flatspace/SaveSystem/SaveLoadSystem.cs`. Find `PlayerSave` (around line 89):

```csharp
        [Serializable]
        public struct PlayerSave
        {
            public int playerId;
            public PlayerAI.AIStrategy strategy;
            public CatalogSave researchCatalogSave;
            public CatalogSave productionCatalogSave;
            public string currentResearchItem;
            public float currentResearch;
            public string exploredGrid;
            public int exploredCols;
            public int exploredRows;
        }
```

Add one appended field at the end:

```csharp
        [Serializable]
        public struct PlayerSave
        {
            public int playerId;
            public PlayerAI.AIStrategy strategy;
            public CatalogSave researchCatalogSave;
            public CatalogSave productionCatalogSave;
            public string currentResearchItem;
            public float currentResearch;
            public string exploredGrid;
            public int exploredCols;
            public int exploredRows;
            public List<string> knownPlanets;
        }
```

Find the `PlayerSave` construction inside the `GameSave` constructor (around line 137):

```csharp
                players.Add(
                    new PlayerSave
                    { 
                        playerId = i,
                        strategy = Gameboard.Instance.players[i].GetStrategy(),
                        researchCatalogSave = new CatalogSave(Gameboard.Instance.players[i].playerAI.ResearchCatalog),
                        productionCatalogSave = new CatalogSave(Gameboard.Instance.players[i].playerAI.ProductionCatalog),
                        currentResearchItem = Gameboard.Instance.players[i].playerAI.currentResearch?.itemName ?? string.Empty,
                        currentResearch = Gameboard.Instance.players[i].playerAI.researchTotal,
                        exploredGrid = FogExplored.Encode(i),
                        exploredCols = Gameboard.Instance.FogOfWar != null ? Gameboard.Instance.FogOfWar.GridCols : 0,
                        exploredRows = Gameboard.Instance.FogOfWar != null ? Gameboard.Instance.FogOfWar.GridRows : 0,
                    });
```

Add one line:

```csharp
                players.Add(
                    new PlayerSave
                    { 
                        playerId = i,
                        strategy = Gameboard.Instance.players[i].GetStrategy(),
                        researchCatalogSave = new CatalogSave(Gameboard.Instance.players[i].playerAI.ResearchCatalog),
                        productionCatalogSave = new CatalogSave(Gameboard.Instance.players[i].playerAI.ProductionCatalog),
                        currentResearchItem = Gameboard.Instance.players[i].playerAI.currentResearch?.itemName ?? string.Empty,
                        currentResearch = Gameboard.Instance.players[i].playerAI.researchTotal,
                        exploredGrid = FogExplored.Encode(i),
                        exploredCols = Gameboard.Instance.FogOfWar != null ? Gameboard.Instance.FogOfWar.GridCols : 0,
                        exploredRows = Gameboard.Instance.FogOfWar != null ? Gameboard.Instance.FogOfWar.GridRows : 0,
                        knownPlanets = new List<string>(gameAI.GameAIMap.Knowledge.KnownPlanets(i)),
                    });
```

- [ ] **Step 4: Wire the load side**

Open `Assets/Flatspace/Objects/Board/GameBoard.cs`. Find `SetSimulationStatus` (around line 227):

```csharp
            private void SetSimulationStatus(SaveLoadSystem.GameSave gameSave)
            {
                TurnNumber = gameSave.turnNumber;
                foreach (var playerSave in gameSave.players)
                {
                    players[playerSave.playerId].SetStrategy(playerSave.strategy);
                    players[playerSave.playerId].SetSavedCatalogCompleted(playerSave.researchCatalogSave);
                    players[playerSave.playerId].SetSavedCatalogCompleted(playerSave.productionCatalogSave);
                    players[playerSave.playerId].playerAI.researchTotal = playerSave.currentResearch;
                    players[playerSave.playerId].playerAI.currentResearch =
                        players[playerSave.playerId].playerAI.ResearchCatalog.catalogItems
                            .Find(x => x.itemName == playerSave.currentResearchItem);

                }
```

Add one line inside the loop:

```csharp
            private void SetSimulationStatus(SaveLoadSystem.GameSave gameSave)
            {
                TurnNumber = gameSave.turnNumber;
                foreach (var playerSave in gameSave.players)
                {
                    players[playerSave.playerId].SetStrategy(playerSave.strategy);
                    players[playerSave.playerId].SetSavedCatalogCompleted(playerSave.researchCatalogSave);
                    players[playerSave.playerId].SetSavedCatalogCompleted(playerSave.productionCatalogSave);
                    players[playerSave.playerId].playerAI.researchTotal = playerSave.currentResearch;
                    players[playerSave.playerId].playerAI.currentResearch =
                        players[playerSave.playerId].playerAI.ResearchCatalog.catalogItems
                            .Find(x => x.itemName == playerSave.currentResearchItem);
                    GameAI.GameAIMap.Knowledge.SetKnownPlanets(
                        playerSave.playerId, playerSave.knownPlanets ?? new List<string>());
                }
```

An older save with no `knownPlanets` field deserializes that field to `null` under `JsonUtility`, so
the `?? new List<string>()` fallback keeps that player starting with no known planets — the same
outcome as a brand-new match, since their home planet becomes known again on the very next
`PlayerKnowledge.Update()` call (it is always a vision source).

- [ ] **Step 5: Compile-check**

Run: `mcp__rider__get_file_problems` on `Assets/Flatspace/SaveSystem/SaveLoadSystem.cs` and
`Assets/Flatspace/Objects/Board/GameBoard.cs`.
Expected: no errors.

- [ ] **Step 6: Ask the user to Play-test save/load**

Ask the user to Play-test from `Assets/Scenes/Flatspace.unity`: run a few turns so at least one
player's `PlayerKnowledge` has more than just its starting planet known, save, reload, and confirm (via
a temporary log or the debugger — `Gameboard.Instance.GameAI.GameAIMap.Knowledge.KnownPlanets(0)`) that
the known set survived the round trip and colonization targeting still behaves the same as before
saving. Do not proceed to Task 7 until this is confirmed.

- [ ] **Step 7: Commit**

```bash
git add Assets/Flatspace/SaveSystem/SaveLoadSystem.cs Assets/Flatspace/Objects/Board/GameBoard.cs Assets/Editor/PlayerKnowledgeSelfCheck.cs
git commit -m "feat(ai): persist PlayerKnowledge in the save file"
```

---

### Task 7: Final self-review and manual verification pass

**Files:** none (verification only)

**Interfaces:** none — this task re-runs everything built in Tasks 1-6 as a whole and checks it
against the spec's Manual Verification section.

- [ ] **Step 1: Full self-check pass**

Ask the user to let the Editor recompile, then run both `FlatSpace > AI > Run Player Knowledge
Self-Check` and `FlatSpace > Fog > Run Self-Check` and confirm both report `ALL PASSED`.

- [ ] **Step 2: Play-mode walkthrough against the spec's verification checklist**

Ask the user to verify, from `Assets/Scenes/Flatspace.unity`:

1. A fresh match: `ProcessColonizers` never targets a planet outside
   `GameAIMap.Knowledge.KnownPlanets(playerId)` — colonization proceeds normally, expanding outward
   turn by turn rather than jumping to a distant, never-visited planet.
2. Fog-of-war rendering (dropdown through `No Fog` / `All Players` / `Player n`) looks identical to
   before this plan — Task 5 was a pure refactor.
3. Save mid-match, reload, and confirm colonization behavior is unaffected (Task 6).

- [ ] **Step 3: Report and close out**

Summarize the outcome to the user: which of the spec's Goals are now met (colonization gated on
discovery, no duplicated source/adjacency logic, correct turn ordering, persisted across save/load),
and which Follow-ups from the spec remain open for a future pass (other AI decisions, human-player-facing
knowledge, advanced AI knowledge stages, auto-explore). No commit for this task — it produces no code
changes.
