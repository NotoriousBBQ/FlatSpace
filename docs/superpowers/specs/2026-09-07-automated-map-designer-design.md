# Automated Map Designer — Design

Status: draft, awaiting user review before implementation planning.

## Problem

The Map Designer (`MapDesigner` scene + `BoardDesigner` / `PlanetDesigner`) is entirely manual. A
designer places `PlanetDesigner` prefab instances as child GameObjects by hand, then runs the
`[ContextMenu]` actions "Generate Connections" (connect every pair within `MaxConnectionSize`) and
"Generate Names And Strategies" (name each planet `"{Type} {n}"` and assign a per-type default
`PlanetStrategy`), and finally the Save button (`SaveLoadSystem.SaveBoardDesign`) writes a
`BoardDesignerSave` JSON.

There is no way to produce a playable board from a handful of parameters. Building a test map means
dragging and positioning every planet.

### Load-bearing fact: connections are not data

Connections are derived purely from geometry, twice, and never persisted:

- `PathingSystem.InitializePathMap(List<Planet>)` rebuilds the graph at game load: for **every pair of
  planets closer than `MaxConnectionSize` (400) it adds a bidirectional edge** weighted by distance.
  That is the only rule.
- `BoardDesigner.GenerateStarConnections` does the identical calculation only to draw preview lines.
- `BoardDesignerSave.BoardDesignerEntry` is `{ string name; Planet.PlanetType planetType; Vector2
  position; }` — no edge list. `PlanetDesigner.Connections` (a `List<DesignerConnection>`) is an
  in-memory preview structure that is never serialized and is lost on domain reload.

So today "A and B are connected" means exactly "A and B are within 400 units." There is no
representation of two nearby planets that are deliberately *not* linked.

## Goals

Add an editor-invoked generator that produces a complete, valid, playable board from parameters:

- Inputs: player count, total planet count, and relative frequency weights for the non-Prime planet
  types.
- Planets are placed with blue-noise (minimum-separation) scatter; map extent scales with planet
  count.
- The number of Prime planets equals the player count. Primes are spread apart (fair starts) and no
  edge connects two Primes.
- Connections are generated as an explicit, deliberately sparse graph: a route exists between every
  planet and every other planet, but a planet is **not** required to connect to every planet in
  range, and a planet with a single connection is valid.
- Except for Normal-type planets, no two planets joined by an edge share a type.
- Generation is seeded for reproducibility.

## Non-goals

- No runtime "new game with a random map" flow. Generation is an editor step that populates the
  `MapDesigner` scene; the existing Save button then writes the board exactly as for a hand-built
  map.
- No new UI window. Parameters live in a ScriptableObject asset; generation is a `[ContextMenu]`
  action, matching the existing designer workflow.
- No change to the meaning of `MaxConnectionSize` for existing hand-built maps — the distance rule
  remains the fallback whenever a board carries no explicit connections.
- No automatic tuning of the density knob (`extraEdgeFraction` stays a code constant).
- No editor undo integration beyond "regenerate replaces the previous result."

## Decisions taken during brainstorming

1. **Explicit edge list (not distance-only).** Three requirements — "not every in-range pair,"
   "one-connection planets are valid," and cleanly enforcing "no same-type adjacency" — only have
   meaning if connections are first-class data. The save format, `PathingSystem`, and the designer
   preview are extended to carry and honor explicit edges, with the current distance rule kept as the
   fallback for boards that provide none.
2. **ScriptableObject preset + `[ContextMenu]`.** A `MapGenSettings` asset holds the parameters
   (reusable presets); `BoardDesigner` references one and a context-menu action runs it.
3. **Blue-noise scatter, extent derived from planet count.** Positions are random with a minimum
   separation; map area scales with planet count to keep density roughly constant, using
   `MaxConnectionSize` as the reference spacing.
4. **Spanning tree + ~25% extra edges.** Candidate edges are pairs within a proximity radius; a
   minimum spanning tree guarantees full connectivity; ~25% of the leftover candidates are added back
   for loops and alternate routes. Degree-1 planets fall out naturally as tree leaves. The 25% is a
   fixed code constant, not exposed in the preset yet.
5. **Primes spread out, never Prime-adjacent.** Primes are placed first by farthest-point sampling;
   no edge may join two Primes; Primes otherwise obey the same no-same-type-neighbor rule.
6. **Retry seeds, then fail loudly.** Constrained type assignment uses backtracking; on failure the
   generator regenerates with a new seed up to a budget, then aborts with a diagnostic naming the
   constraint it could not satisfy. Nothing is written to the scene on failure.
7. **Seed field, 0 = random.** `MapGenSettings` carries an `int seed`; 0 picks and logs a fresh
   random seed, non-zero is fully reproducible.
8. **Relative weights.** Per-type frequency inputs are relative weights, normalized and distributed
   across `totalPlanetCount − playerCount` by largest-remainder rounding. Weight 0 excludes a type.
9. **Serializable connection storage on `PlanetDesigner`.** Generated (and manually generated)
   connections are stored in a serialized `List<string>` of planet names so they survive a domain
   reload and are the source of truth for Save.

## A. `MapGenSettings` ScriptableObject

New file `Assets/Flatspace/BoardDesigner/MapGenSettings.cs`, `[CreateAssetMenu(menuName =
"Scriptable Objects/MapGenSettings")]`.

```csharp
[Serializable]
public struct TypeWeight
{
    public Planet.PlanetType type;
    public float weight;
}

public class MapGenSettings : ScriptableObject
{
    public int seed = 0;                       // 0 = random, logged
    public int playerCount = 2;                // == number of Prime planets
    public int totalPlanetCount = 20;          // includes the Primes

    public List<TypeWeight> typeWeights = new();// non-Prime types; weight 0 excludes; Prime entries ignored

    public float minPlanetSeparation = 120f;   // blue-noise rejection radius
    public float connectionRadius = 400f;      // candidate-edge cutoff; matches MaxConnectionSize
    public float nominalSpacing = 200f;        // drives map extent

    public int seedRetryBudget = 8;            // seeds tried before failing
}
```

`extraEdgeFraction = 0.25f` is a `const` in `MapGenerator`, not a field here.

Validation on the settings (checked at the top of generation, reported via `Debug.LogError`, no scene
mutation):

- `playerCount >= 1`
- `totalPlanetCount > playerCount`
- At least one `typeWeights` entry with `weight > 0` and `type != PlanetTypePrime`.

## B. `MapGenerator` — plain C#, no MonoBehaviour

New file `Assets/Flatspace/BoardDesigner/MapGenerator.cs`. A pure function from settings to a result;
it performs **no scene work** so it stays inspectable and independently reasoned about.

```csharp
public struct GeneratedPlanet
{
    public string Name;
    public Planet.PlanetType Type;
    public Planet.PlanetStrategy Strategy;
    public Vector2 Position;
    public List<string> Connections;          // symmetric: if A lists B, B lists A
}

public class GenerationResult
{
    public bool Success;
    public string Error;                      // populated when Success == false
    public int EffectiveSeed;
    public List<GeneratedPlanet> Planets = new();
}

public static class MapGenerator
{
    public static GenerationResult Generate(MapGenSettings settings);
}
```

### Pipeline

1. **Resolve seed.** `effectiveSeed = settings.seed != 0 ? settings.seed : new System.Random().Next()`.
   Construct one `System.Random(effectiveSeed)` and thread it through every random draw. Always
   `Debug.Log($"[MapGenerator] seed {effectiveSeed}")`.

2. **Counts.** `primeCount = settings.playerCount`. `nonPrimeCount = settings.totalPlanetCount −
   primeCount`. Take the `typeWeights` entries with `weight > 0` and `type != PlanetTypePrime`,
   normalize the weights, multiply by `nonPrimeCount`, and resolve to integers by largest-remainder
   rounding so they sum to exactly `nonPrimeCount`. Produces a type multiset for the non-Prime nodes.

3. **Placement (blue-noise).** Square extent centered on the origin (planets use `localPosition`, as
   the existing designer does), side length `= sqrt(totalPlanetCount) * settings.nominalSpacing`.
   Rejection sampling: draw a uniform point in the extent, accept only if it is at least
   `minPlanetSeparation` from every accepted point. After a stall (e.g. 30 consecutive rejections)
   grow the extent by 10% and continue. Continue until `totalPlanetCount` points are accepted.

4. **Prime nodes first.** From the accepted point set, choose Prime node positions by farthest-point
   sampling: first Prime is a random accepted point; each subsequent Prime is the accepted point
   maximizing the minimum distance to the already-chosen Primes. The remaining accepted points are
   the non-Prime nodes.

5. **Candidate edges.** All node pairs within `settings.connectionRadius`, **excluding any pair where
   both endpoints are Prime nodes**. Each candidate carries `cost = Vector2.Distance(a, b)`.

6. **Connectivity graph.**
   - Minimum spanning tree over the candidate edges (weight = `cost`). This guarantees a route
     between every pair.
   - If the candidate graph is disconnected (a node has no candidate within radius, or Prime-Prime
     exclusion isolated a Prime), bridge each remaining component to the rest with the single
     shortest inter-component edge — nearest-neighbor, even if it exceeds `connectionRadius`, and
     still never Prime-Prime. If the only available bridge is Prime-Prime (pathological tiny maps),
     the graph fails validation in step 8 and the seed is retried.
   - Add back `round(extraEdgeFraction * remainingCandidateCount)` of the non-tree candidate edges,
     chosen at random, for loops and alternate routes.

7. **Type assignment with the adjacency constraint.**
   - Prime nodes are fixed as `PlanetTypePrime`.
   - The non-Prime type multiset from step 2 is assigned to the non-Prime nodes so that no edge joins
     two nodes of the same type **unless that type is `PlanetTypeNormal`** (Normal may neighbor
     Normal; Prime may not neighbor Prime and that is already guaranteed by step 5).
   - Solver: order non-Prime nodes by descending degree. For each node, try the still-available types
     in a randomized order; skip a type if it equals an already-assigned neighbor's type and the
     type is not Normal. Backtrack on a dead end. Cap total backtracking steps (e.g. 10,000); on cap
     exhaustion this attempt fails and the seed is retried.

8. **Validation.** Assert, over the finished graph:
   - Connected: BFS from node 0 reaches all nodes.
   - No forbidden adjacency: no edge joins equal types except Normal-Normal.
   - `primeCount` nodes are Prime, and no edge joins two Primes.
   - Every node has degree ≥ 1.
   On any failure, increment the seed and restart from step 1, up to `settings.seedRetryBudget`
   attempts. After the budget is exhausted, return `Success = false` with an `Error` string naming
   the failing condition and the per-type counts, so the designer knows which weight to loosen.

9. **Emit `GeneratedPlanet` list.** Per node: position; type; name `"{TypeShortName} {n}"` using the
   same scheme as `BoardDesigner.GenerateNames` (`"Prime 0"`, `"Farm 3"`, …); strategy from the
   per-type default map lifted from `GenerateNames` into a shared
   `PlanetTypeDefaults.StrategyFor(Planet.PlanetType)` helper (so the generator and the existing
   context-menu action cannot drift); and the symmetric connection-name list built from the final
   edge set.

On success also `Debug.Log` a one-line report: per-type counts, edge count, min/max node degree,
"connectivity OK".

## C. Explicit connections — data model changes

Every new field defaults to empty. A board that carries no explicit connections anywhere falls
through to the current within-`MaxConnectionSize` rule, so existing hand-built `BoardDesignerSave`
JSON and authored `BoardConfiguration` / `PlanetSpawnData` assets are unaffected with no migration.

### `PlanetDesigner`

- Add `public List<string> connectionNames = new();` — **serialized**, the source of truth for a
  planet's connections in the designer.
- The existing in-memory `Connections` (`List<DesignerConnection>`) becomes a derived cache. Add
  `RebuildConnectionsFromNames(IReadOnlyDictionary<string, PlanetDesigner> byName)` that repopulates
  it (target reference + distance cost) from `connectionNames`, called before preview drawing.
- `BoardDesigner.GenerateStarConnections` is updated to also write `connectionNames` (symmetric) when
  it builds the within-range preview, so the manual flow and the generator produce the same
  serialized shape.

### `SaveLoadSystem.BoardDesignerSave.BoardDesignerEntry`

- Add `public List<string> connections;`, populated from `planet.connectionNames` in the
  `BoardDesignerSave(List<PlanetDesigner>)` constructor.
- `JsonUtility` reads a missing `connections` key as `null`; treat null as empty on load.

### `PlanetSpawnData`

- Add `public List<string> _connections = new();`.

### `GameBoard.InitGame(SaveLoadSystem.BoardDesignerSave)`

- When building each `PlanetSpawnData` from a `BoardDesignerEntry`, copy `planetDesignData.connections
  ?? new()` into `spawnData._connections`.

### `Planet`

- Add `public List<string> Connections;`, assigned in `Planet.Init` from `spawnData._connections`.

### `PathingSystem.InitializePathMap(List<Planet> planets)`

- If `planets.Any(p => p.Connections is { Count: > 0 })`: build the node graph from the **symmetrized
  union** of the per-planet `Connections` lists. For each listed pair add a bidirectional
  `Connection` with `cost = Vector2.Distance(a.Position, b.Position)`. A name in a `Connections` list
  that does not resolve to a known planet is logged and skipped.
- Otherwise: the current behavior — every pair within `MaxConnectionSize`.
- Edge cost stays euclidean distance in both branches, so the A* heuristic (straight-line distance to
  destination) remains admissible.
- `GameAIMap.GameAIMapInit`'s all-pairs precompute consumes the resulting `PathNodes` unchanged.

## D. Designer integration

### `BoardDesigner`

- Add `[SerializeField] private MapGenSettings mapGenSettings;` and `[SerializeField] private
  PlanetDesigner planetDesignerPrefab;`.
- Add:

  ```csharp
  [ContextMenu("Generate Random Board")]
  public void GenerateRandomBoard()
  ```

  which:
  1. Validates `mapGenSettings` and `planetDesignerPrefab` are assigned (`Debug.LogError` + return
     otherwise).
  2. Calls `MapGenerator.Generate(mapGenSettings)`.
  3. On `!result.Success`: `Debug.LogError($"[MapGenerator] {result.Error}")` and return — the scene
     is left exactly as it was.
  4. On success: `DestroyImmediate` every existing child `PlanetDesigner` GameObject and
     `ClearConnections()`; instantiate one `planetDesignerPrefab` per `GeneratedPlanet` as a child of
     `transform`, set `type` / `planetName` / `name` / `strategy` / `localPosition` /
     `connectionNames`, call `UpdateGraphic()`; then draw the preview via the existing
     `DrawConnections` path (which now reads `connectionNames` through
     `RebuildConnectionsFromNames`).

- `GenerateStarConnections`, `GenerateNames`, `ClearConnections`, `SaveBoardConfig`, and the
  `SaveDesignButton` UI wiring are otherwise unchanged and remain usable for hand-built maps.

### Shared per-type defaults

Extract the `Planet.PlanetType → Planet.PlanetStrategy` mapping currently inlined in
`BoardDesigner.GenerateNames` into `Assets/Flatspace/BoardDesigner/PlanetTypeDefaults.cs`
(`static Planet.PlanetStrategy StrategyFor(Planet.PlanetType type)` plus
`static string ShortName(Planet.PlanetType type)` for the `"{Type} {n}"` naming). `GenerateNames` is
refactored to call it; the generator calls the same helper. Behavior is identical — this only removes
the duplication so the two paths cannot drift.

## E. Failure handling and diagnostics

- The effective seed is always logged, first thing.
- Settings validation failures and post-retry generation failures are `Debug.LogError` with a
  specific message (which setting, which constraint, the per-type counts). The scene is never mutated
  on any failure path.
- On success, a single summary `Debug.Log`: per-type counts, edge count, min/max degree,
  "connectivity OK".

## F. Testing and verification

No automated test framework exists in this repo (per `CLAUDE.md`). Verification is:

- Rider MCP compile / inspection checks after each implementation step.
- Generate several `MapGenSettings` presets (small/large planet counts, 2–4 players, even and skewed
  weights) and confirm from the summary log and a visual pass in the `MapDesigner` scene that:
  Prime count matches player count; Primes are spread and never adjacent; no forbidden same-type
  adjacency; the graph is connected; some planets have a single connection; not every in-range pair
  is linked.
- Deliberately skewed weights (one type at ~90%) should fail after the retry budget with a clear
  diagnostic and leave the scene untouched.
- Save a generated board, load it into the `Flatspace` scene, and confirm pathing resolves and the
  AI runs a few turns without error.
- Regression: load an existing hand-built `BoardDesignerSave` (no `connections` key) and confirm
  `PathingSystem` still produces the identical distance-rule graph.
- `OrderTypeShipTransport` and other simulation systems need no changes — they consume the pathing
  graph, which keeps the same shape.
