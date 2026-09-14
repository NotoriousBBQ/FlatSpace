# Player Knowledge — Design

Status: draft, awaiting user review before implementation planning.

## Problem

Every `PlayerAI` decision except one is already scoped to a player's own planets, via a `PlayerID`
filter on the `Planet.PlanetUpdateResult` list each decision reads (food/grotsits shortages,
production, research). A player already knows everything about its own colonies by definition, so
fog of war is irrelevant there.

The one exception is colonization targeting. `ProcessColonizers` picks targets from
`AIMap.PlanetList.FindAll(IsValidColonizationTarget)` — the full global planet list, no ownership or
visibility filter — and `GetIndustrySituationalWeightMultiplier`'s "urgently boost ColonyShip
production" check reaches the same unfiltered pool via `PlanetCanColonize`. Every AI player is
omniscient about colonization opportunities anywhere on the map, regardless of whether it has ever
had a ship or population near that planet.

Fog-of-war (`FlatSpace.Fog`, see `2026-09-08-fog-of-war-design.md`) already computes a similar
"has this player seen it" concept, but it is deliberately presentation-only: *"No effect on the
simulation or AI... The AI stays omniscient... Gating the AI on visibility is a possible later
task."* This spec is that later task — but rather than have `PlayerAI` read the fog system directly
(which would couple a simulation decision to a debug-oriented MonoBehaviour and to render-only
radius/gradient math, and would also read stale data — see Load-bearing facts), it introduces a small
simulation-owned concept, `PlayerKnowledge`, as the single source of truth for "does player P know
planet X exists." Colonization targeting is the only consumer wired up in this pass, but the class is
deliberately reusable so a future AI decision — or, eventually, a human-player-facing system — can
query the same API without a redesign.

## Load-bearing facts about the current code

- **Turn ordering matters.** `GameAI.GameAIUpdate()` runs `ProcessCurrentOrders()` (this turn's
  arrivals/colonizations land here) → `UpdateAllPlanets()` → `ProcessResults()` (this is where
  `PlayerAI.ProcessColonizers` runs) → `ProcessNewOrders()`. Fog's own `Recompute()` runs *after* all
  of this, from `Gameboard.SingleUpdate()`, so a design that read fog data from inside
  `ProcessResults()` would always be one turn stale.
- **The only cross-empire AI query is colonization targeting.** `IsValidColonizationTarget(Planet
  planet)` is the single choke point both `ProcessColonizers` and `PlanetCanColonize` (via
  `GetIndustrySituationalWeightMultiplier`) funnel through.
- **`GameAIMap` already owns the planet list and per-planet pathing** (`PlanetList`, `GetPlanet`,
  `DistanceMapToPathingList`), and `PlayerAI` already holds a `GameAIMap AIMap` reference — the
  natural home for a new simulation-owned concept.
- **`Planet.Connections`** is a plain `List<string>` of planet names and is not guaranteed symmetric
  (a link can be listed on only one side). `FogOfWarSystem.BuildAdjacency` already has to symmetrize
  it into an adjacency list for its own "adjacent planet becomes Explored" rule — the same
  symmetrization problem this design must solve, and a spot of literal duplicated logic if solved
  twice.
- **`FogOfWarSystem.Recompute()` already gathers, per player, "which planets are vision sources"**
  (`GetPopulationFraction(p) > 0`, or a docked `Ship` owned by `p`) as its first step, independently
  re-deriving the same predicate `PlayerKnowledge` needs.
- **Saves use `JsonUtility` on `SaveLoadSystem.GameSave`**, with a per-player `PlayerSave`. New fields
  must be appended, never inserted (this is already true of the fog `exploredGrid` fields added in
  the previous spec). `JsonUtility` serializes `List<string>` fields directly — no packing needed for
  a per-player list of planet names (unlike fog's per-cell bitset, this data is small).

## Goals

- A simulation-owned, per-player set of "known" planets that AI logic can query directly, with no
  dependency on `Gameboard`, `FogOfWarSystem`, or any presentation-tier component.
- Colonization targeting (`IsValidColonizationTarget`, and therefore both `ProcessColonizers` and
  `PlanetCanColonize`) only considers known planets.
- Sticky knowledge: once known, always known for the rest of the match — a planet doesn't become
  "un-known" because the source that revealed it moved away. Since a newly colonized planet becomes a
  new source next turn, knowledge keeps reaching outward hop by hop as the empire grows; a target
  doesn't have to be adjacent to a *current* source to remain eligible, only to have been discovered
  at some point.
- Correct turn ordering: this turn's arrivals are reflected before this turn's colonization decisions
  run (see Load-bearing facts).
- **No duplicated "who is a vision source" or "who is adjacent to whom" logic.** The two raw facts
  `PlayerKnowledge` and `FogOfWarSystem` both need — a player's vision-source planets, and a planet's
  symmetrized neighbours — move to `GameAIMap` as shared queries, and `FogOfWarSystem` is updated to
  call them instead of re-deriving its own copies. This is the direct fix for the "two independent
  trackers will drift" risk raised during brainstorming: the *fact* is computed once; each consumer
  (`PlayerKnowledge`'s sticky adjacency-only knowledge, `FogOfWarSystem`'s radius/gradient/corridor
  math) still builds its own further-specialized answer on top, because those really do answer
  different questions at different granularities.
- Persisted across save/load.

## Non-goals

- **No radius/openness math in `PlayerKnowledge`.** A planet becomes known via a source planet
  (population or docked ship) or via direct `Planet.Connections` adjacency to a source planet — the
  same one-hop rule fog's `ApplyAdjacency` already uses for its own "Explored" marking. Fog's
  soft-edged vision *circle*, which can catch a planet that is geometrically close but not
  graph-connected, is not ported — colonization targeting already only considers
  pathing-reachable planets, and adjacency is the pathing-relevant relationship.
- **No in-flight/corridor knowledge.** A `PlayerKnowledge`-known planet requires an actual source
  (population or a docked ship) at some point; a ship merely passing through on a delayed transport
  order does not make intermediate planets known. (Fog reveals those corridors for the player's
  benefit while the debug view is open; the simulation doesn't need that nuance for colonization
  decisions.)
- **No change to fog's own vision computation.** `FogOfWarSystem` keeps its own radius/openness/
  corridor/rendering logic entirely as-is; only its two raw inputs (vision-source planets, symmetrized
  neighbours) move to shared `GameAIMap` queries.
- **No gating of any other AI decision in this pass** — food/grotsits/production/research stay
  scoped to a player's own planets as today, per the confirmed scope.
- No UI. This is a pure simulation-layer change; there is nothing for a human to see or toggle.

## Decisions taken during brainstorming

- **A new simulation-owned class, not a read of `FogOfWarSystem`.** Reading fog directly would be
  one turn stale (see Load-bearing facts) and would couple simulation decisions to a presentation
  MonoBehaviour's lifecycle.
- **Owned by `GameAIMap`.** Consistent with where the planet list and pathing already live, and where
  `PlayerAI` already reaches for map data.
- **Sticky, adjacency-only, source rule simplified from fog's** (population/ship presence + one-hop
  `Planet.Connections`, no radius math) — confirmed acceptable given colonization targeting already
  gates on pathing reachability separately.
- **The two raw facts shared with fog** (vision-source planets; symmetrized neighbours) move to
  `GameAIMap`, and `FogOfWarSystem` is refactored to consume them, closing the exact duplication gap
  raised during brainstorming.
- **Updated inside `GameAI.GameAIUpdate()`**, after `UpdateAllPlanets()` and before `ProcessResults()`,
  so this turn's arrivals are known before this turn's colonization decisions run.
- **Single integration point**: `PlayerAI.IsValidColonizationTarget` gains one guard clause, covering
  both existing callers without touching either of them.

## Architecture

```
GameAIMap
 ├─ PlanetList / GetPlanet / DistanceMapToPathingList   (existing)
 ├─ GetVisionSourcePlanets(playerId)      ◄── new, shared
 │     planets where GetPopulationFraction(playerId) > 0, or a docked Ship owned by playerId
 ├─ GetNeighbours(planetName)             ◄── new, shared
 │     Planet.Connections, symmetrized and cached once at GameAIMapInit
 └─ Knowledge : PlayerKnowledge           ◄── new
       • per-player sticky HashSet<string> of known planet names
       • Update()  – called from GameAI.GameAIUpdate(), between UpdateAllPlanets() and ProcessResults()
       • IsKnown(playerId, planetName)
       • KnownPlanets(playerId)           – for future consumers / save

PlayerAI.IsValidColonizationTarget(planet)
    + if (!AIMap.Knowledge.IsKnown(Player.playerID, planet.PlanetName)) return false;   ◄── new guard

FogOfWarSystem.Recompute() / BuildAdjacency()
    – now call GameAIMap.GetVisionSourcePlanets(p) / GetNeighbours(name)
      instead of re-deriving the same predicates independently          ◄── refactor, no behaviour change
```

### Component boundaries

- **`GameAIMap`** — gains two read-only queries over already-owned data (`GetVisionSourcePlanets`,
  `GetNeighbours`) and a `PlayerKnowledge Knowledge` property. It has no notion of "why" a caller wants
  these facts.
- **`PlayerKnowledge`** — a plain C# class (no `MonoBehaviour`), owns only the sticky per-player known
  set and the one-hop update rule. It depends on `GameAIMap`'s two queries and nothing else — not on
  `Gameboard`, not on `FogOfWarSystem`, not on any presentation type.
- **`PlayerAI`** — one new guard clause in one existing method. No new decision-making structure.
- **`FogOfWarSystem`** — unchanged behaviourally; its private source-gathering and `BuildAdjacency`
  are replaced by calls to the new `GameAIMap` queries.

## Data model

```csharp
namespace FlatSpace.AI
{
    public class PlayerKnowledge
    {
        private readonly Dictionary<int, HashSet<string>> _known = new();

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
                    set.Add(source.PlanetName);
                    foreach (var neighbourName in map.GetNeighbours(source.PlanetName))
                        set.Add(neighbourName);
                }
            }
        }

        // Save/load — see Save / load section.
        public void SetKnownPlanets(int playerId, IEnumerable<string> names) =>
            _known[playerId] = new HashSet<string>(names);
    }
}
```

`GameAIMap` gains:

```csharp
public List<Planet> GetVisionSourcePlanets(int playerId) =>
    PlanetList.FindAll(p =>
        p.GetPopulationFraction(playerId) > 0f ||
        p.DockedShips.Exists(s => s.Owner == playerId));

public IReadOnlyList<string> GetNeighbours(string planetName) =>
    _neighbours.TryGetValue(planetName, out var list) ? list : Array.Empty<string>();
```

with `_neighbours` (`Dictionary<string, List<string>>`) built once at the end of `GameAIMapInit`, by
symmetrizing every planet's `Connections` exactly the way `FogOfWarSystem.BuildAdjacency` does today
(if `A.Connections` contains `B`, both `_neighbours[A]` and `_neighbours[B]` get an entry, regardless
of which side originally listed it).

`PlayerKnowledge.Update` is called once per turn as `GameAIMap.Knowledge.Update(GameAIMap,
Gameboard.Instance.players.Count)`, from `GameAI.GameAIUpdate()`, between `UpdateAllPlanets()` and
`ProcessResults()`.

## Integration point

`PlayerAI.IsValidColonizationTarget` gains one guard clause at the top:

```csharp
private bool IsValidColonizationTarget(Planet planet)
{
    if (!AIMap.Knowledge.IsKnown(Player.playerID, planet.PlanetName)) return false;
    if (planet.IsPopulationTransferInProgress(Player.playerID)) return false;
    if (planet.Population.Count == 0)                           return true;
    if (planet.Population.Count >= planet.MaxPopulation)        return false;
    return planet.PlayerWithMostPopulation() != Player.playerID;
}
```

Both existing callers — `ProcessColonizers`'s global target list, and
`GetIndustrySituationalWeightMultiplier`'s "urgently boost ColonyShip production" check via
`PlanetCanColonize` — funnel through this one method, so this single change covers both.

## Save / load

`SaveLoadSystem.PlayerSave` gains one appended field:

| Field | Type | Notes |
|---|---|---|
| `knownPlanets` | `List<string>` | planet names known to this player; `JsonUtility`-safe directly, no packing needed |

- **Save**: `PlayerKnowledge.KnownPlanets(p)` written directly into the new field.
- **Load**: after `GameAIMap` is initialised for the loaded board (so planet names are valid),
  `Knowledge.SetKnownPlanets(p, gameSave.playerSaves[p].knownPlanets)`. An older save with no
  `knownPlanets` deserializes to an empty list — that player starts with no known planets, same as a
  new match (its own home planet becomes known again on the very next `Update()`, since it is always
  a vision source).
- Unlike fog's `exploredGrid`, there is no grid-dimension mismatch case to handle — planet names are
  the key, not grid coordinates, so a mismatched board simply has no matching names and the loaded
  list is harmlessly empty for any name that doesn't exist.

## Lifecycle & integration points

| Site | Change |
|---|---|
| `GameAIMap.GameAIMapInit` | After pathing is built: symmetrize every planet's `Connections` into `_neighbours`; construct `Knowledge = new PlayerKnowledge()`. |
| `GameAIMap` | Add `GetVisionSourcePlanets(playerId)`, `GetNeighbours(planetName)`, `Knowledge` property. |
| `GameAI.GameAIUpdate()` | Between `UpdateAllPlanets(planetUpdateResults)` and `ProcessResults(...)`: `GameAIMap.Knowledge.Update(GameAIMap, Gameboard.Instance.players.Count);`. |
| `PlayerAI.IsValidColonizationTarget` | Add the `IsKnown` guard clause. |
| `FogOfWarSystem.BuildAdjacency` | Replace with a call to `GameAIMap.GetNeighbours` per planet (via the `GameAI`/`GameAIMap` reference `Recompute` already has). |
| `FogOfWarSystem.Recompute` (source gathering step) | Replace the inline `GetPopulationFraction`/`DockedShips` loop with `GameAIMap.GetVisionSourcePlanets(pl)`. |
| `SaveLoadSystem` | Add/read the `knownPlanets` field on `PlayerSave`; write on save, apply on load after `GameAIMap` init. |
| new `Assets/Flatspace/GameAI/PlayerKnowledge.cs` | The class. |
| new `Assets/Editor/PlayerKnowledgeSelfCheck.cs` | Self-check (see Manual verification). |

## Namespace / naming

`PlayerKnowledge` lives in `Assets/Flatspace/GameAI/`, namespace `FlatSpace.AI` (matching
`PlayerAI`, `GameAIMap`, `GameAI` in the same folder). `knownPlanets` is a new, appended
`PlayerSave` field — ordering is load-bearing per the existing `JsonUtility` convention, so it is
added at the end of the struct, after the fog fields from the previous spec.

## Manual verification

No automated test infrastructure exists in the project. Verification is a new self-check reachable
from `FlatSpace/AI/Run Player Knowledge Self-Check` (`[MenuItem]`, following the `FogSelfCheck`
pattern — plain assertions, `Debug.LogError` on failure, a summary `Debug.Log` at the end), covering:

1. A source planet (population > 0 for player p) becomes known to p.
2. Its direct `Planet.Connections` neighbour becomes known to p; a two-hop-away planet does not,
   until the neighbour itself becomes a source (e.g. after colonization) and a subsequent `Update()`
   runs.
3. Connections are treated as symmetric regardless of which planet's list originally declared them
   (mirrors `FogOfWarSystem.BuildAdjacency`'s existing symmetrization).
4. Once known, a planet stays known after its only source is removed (undocked ship, population
   drops to 0) and a subsequent `Update()` runs — stickiness.
5. `PlayerAI.IsValidColonizationTarget` (and therefore `ProcessColonizers`) excludes an unknown but
   otherwise-eligible, reachable planet from the target list, and includes it once known.
6. `SetKnownPlanets`/`KnownPlanets` round-trip a set of names (save/load).

Then in Play mode from `Assets/Scenes/Flatspace.unity`: run turns until an AI colonizes a new planet,
confirm (via a temporary log or the debugger) that `ProcessColonizers` never selects a target outside
`Knowledge.KnownPlanets(playerId)`, and that colonization still proceeds normally (no behavioural
regression from the added filter on a fresh match, where only each player's own capital and its
neighbours are known at first).

## Follow-ups (out of scope, recorded for later)

- **Other AI decisions gated on knowledge.** Military targeting or any future cross-empire AI
  decision can reuse `PlayerKnowledge.IsKnown` without new plumbing.
- **Human-player-facing knowledge.** If FlatSpace ever supports a human player, `PlayerKnowledge` is
  the natural data source for "what can this player currently see/target in the UI," independent of
  the debug fog-of-war view.
- **Advanced AI knowledge stages.** E.g. an AI-difficulty setting that grants a wider knowledge radius,
  or partial knowledge of a planet's contents without full targeting eligibility — both would extend
  `PlayerKnowledge` rather than replace it.
- **Auto-explore.** A prior conversation about a cheap scout-type ship with an auto-explore behavior
  was parked before this spec; a general auto-explore action would be a natural consumer of
  `PlayerKnowledge` (picking the nearest planet *not* in `KnownPlanets` as its destination) once this
  lands.
