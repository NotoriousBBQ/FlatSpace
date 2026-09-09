# Fog of War — Design

Status: draft, awaiting user review before implementation planning.

## Problem

FlatSpace renders the whole board unconditionally. Every planet marker, connection line, and order
line is drawn at full strength regardless of which player is "looking," and empty space is just the
camera background. There is no notion of a player's knowledge of the map.

We want a fog of war that expresses three states, per player, over the whole map area:

- **Unseen** — never observed by this player. Rendered fully greyed out (near-opaque dark overlay).
- **Explored** — observed before, not currently observed. Rendered partially greyed (semi-transparent
  dark overlay); planets show as a dimmed marker with name only, no live stats.
- **Visible** — currently observed. Rendered normally.

A location is **visible** to a player when it is within a tunable distance of one of that player's
vision sources (a colonised planet or a ship). Open space far from any planet or connection gets a
more generous vision distance, so the voids between systems do not stay permanently black.

Because every player in FlatSpace is AI-driven and there is no human player, fog of war is a
**development / debugging aid**, not a gameplay mechanic. It needs a debug control to switch the
displayed view between any single player, a union of all players, and "no fog" (today's rendering).

## Load-bearing facts about the current code

- **The simulation has no per-player knowledge model.** `GameAIMap` and `PlayerAI` read full planet
  state directly. Fog must be computed as a new, separate layer.
- **Planets have continuous `Vector2` positions, not a grid.** Positions span hundreds of world units
  (the pathing graph links planets within `MaxConnectionSize` = 400). There is no tilemap.
- **A `Planet` is a component in `Dictionary<string, Planet>` keyed by name**; there is no
  GameObject-per-planet in the simulation. The presentation layer has one `PlanetUIObject` per planet
  (world-space marker: a `SpriteRenderer` at `sortingOrder` 2 plus a world-space `Canvas` for stats)
  and one `LineDrawObject` per connection, all parented to the `Gameboard` GameObject.
- **Ships in transit have no GameObject.** A colony-ship run exists only as a
  `GameAI.GameAIOrder` of type `OrderTypePopulationTransport` moving along a path;
  `Gameboard.DisplayOrderGraphics` draws it by interpolating
  `(TotalDelay − TimingDelay) / TotalDelay` along `GameAIMap.GetPath(Origin, Target)`.
  `OrderTypeShipTransport` exists as a mechanism but no AI code creates one yet.
- **The turn loop is `Gameboard.SingleUpdate()`**: `GameAI.GameAIUpdate()` then `TurnNumber++` then
  `PlanetaryUIUpdate()` + `BoardUIUpdate()`. `SingleUpdate` runs once per Next Turn click, or on a
  timer via the `TimedUpdate` coroutine.
- **`Gameboard` already carries `owningPlayerId` (= 0)**, used only for the status-bar readout in
  `BoardUIUpdate`. It is not yet a general "which player am I viewing" concept.
- **Debug buttons are UI Toolkit**: `GameButtons.uxml` declares `Button`s, wired in
  `GameButtonHandler.Setup(root, …)`, which is called from `MainScreenUIController.OnEnable`. The HUD
  is a screen-space `UIDocument`, always rendered above world-space objects.
- **Saves** use `JsonUtility` on `SaveLoadSystem.GameSave`, with a per-player `PlayerSave`. Serialized
  field names and enum ordering are load-bearing (referenced from JSON and scene YAML); new fields
  and new enum values must be **appended**, never inserted or reordered.
- **`GameAIConstants` is simulation tuning.** The active asset is
  `Assets/GameAIConstants4ProductionTypes.asset`. `BoardConfiguration` / `PlanetSpawnData` /
  `CatalogItem` are the other ScriptableObject families.

## Goals

- A per-player visibility model over the whole map area, recomputed each turn, with sticky "explored"
  history.
- A graphical fog: a dark overlay over unseen / explored empty space, plus dimming / hiding of
  planet markers, connection lines, and order lines that are not visible.
- Circular (not blocky) vision falloff.
- A more generous vision radius in open space, so voids do not stay permanently black.
- Vision from: any planet where the player has population; any planet holding a ship the player owns;
  and the player's in-flight colony ships (and future `OrderTypeShipTransport`), moving with the
  order.
- A debug HUD control to switch the displayed view: `No Fog`, `All Players`, or `Player n`.
- Per-player explored history persisted in the game save.
- All tuning in a dedicated `FogOfWarSettings` ScriptableObject.

## Non-goals

- **No effect on the simulation or AI.** `PlayerAI`, `ScoreMatrix`, order generation, and pathing are
  untouched. The AI stays omniscient. (Gating the AI on visibility is a possible later task and is
  why the visibility model is built as its own seam.)
- **No "last known state" for explored planets.** An explored planet, when clicked, still opens the
  normal `PlanetDetailUIController` with full live data. Restricting that to a snapshot is deferred
  (see Follow-ups).
- **No per-player filtering of notifications or the status bar** in this work. The fog view selector
  and `owningPlayerId` stay separate concepts for now (see Follow-ups).
- No new scene, no change to `MapDesigner`.
- No custom URP shader. The overlay uses a stock transparent/sprite material and a CPU-updated
  texture.
- No animated / time-based fade of the fog; state changes snap at turn boundaries (the soft rim is
  spatial, not temporal).

## Decisions taken during brainstorming

- **Presentation-only.** Fog is a new layer; the simulation does not read it.
- **Rendering: continuous-value grid overlay + per-object tinting.** A low-resolution grid stores a
  continuous visibility *strength* per cell (not a boolean); it is baked to a `Texture2D` with
  bilinear filtering and drawn as one world-space overlay quad, giving smooth circular falloff.
  Planet markers and lines are separately hidden / dimmed by sampling the grid at their position, so
  their state is crisp regardless of grid resolution and their stats can be fully suppressed.
- **Planet vision: any planet where the player has population** (`GetPopulationFraction(p) > 0`), not
  only owned planets — contested worlds still give eyes.
- **In-flight colony ships grant vision**, interpolated along the order path, fading to "explored"
  behind them.
- **Explored history is persisted** per player in the save (compact bit-packed grid).
- **Tunables live in a new `FogOfWarSettings` ScriptableObject**, referenced by `Gameboard`.
- **Debug control is a `DropdownField` in the game HUD**, default `No Fog`.
- **The detail-panel restriction for explored planets is deferred.**

## Architecture

A new presentation-side component, `FogOfWarSystem`, created and owned by `Gameboard` alongside
`GameAI` (this.AddComponent pattern). It follows the existing "system" convention (`PathingSystem`,
`SaveLoadSystem`).

```
Gameboard
 ├─ GameAI (simulation, unchanged)
 ├─ FogOfWarSystem  ◄── new
 │    • static emptiness field (baked once at map load)
 │    • per-player VisibilityGrid { float[] visibleStrength; ulong[] exploredBits }
 │    • FogOverlay GameObject + Texture2D
 │    • FogViewMode { NoFog, AllPlayers, Player(n) }
 │    • Recompute()               – rebuild all players' grids for the current turn
 │    • SetViewMode(mode)         – re-resolve displayed grid, re-push object states
 │    • SampleVisibility(worldPos) → FogSample { strength, explored }
 │    • GetExploredPacked(p) / SetExploredPacked(p, bytes, cols, rows)
 └─ PlanetUIObject[] / LineDrawObject[] (presentation)
      • PlanetUIObject.SetFogState(Hidden | Explored | Visible)   ◄── new
      • LineDrawObject.SetFogState(Hidden | Explored | Visible)   ◄── new
```

`Gameboard` drives the wiring: it calls `Recompute()` in `SingleUpdate` and after load, then a new
`FogUIUpdate()` that samples the system for each `PlanetUIObject` / `LineDrawObject` / order line and
pushes a fog state. `GameAIMap` is untouched.

### Component boundaries

- **`FogOfWarSystem`** — knows nothing about UXML or the turn loop. Given the planet list, the
  connection segments, and the settings asset, it owns all grid math and the overlay texture. Inputs:
  planet positions + populations, docked ships, in-flight orders (queried from `GameAI` each
  `Recompute`), the settings asset. Output: the overlay texture (self-managed) and `SampleVisibility`.
- **`Gameboard`** — the only place that connects `FogOfWarSystem` to the turn loop, to the
  `PlanetUIObject` / `LineDrawObject` collections, and to the debug dropdown.
- **`GameButtonHandler`** — owns the dropdown element and translates its value into a
  `Gameboard.SetFogViewMode(...)` call. No fog logic.
- **`PlanetUIObject` / `LineDrawObject`** — receive a state enum and apply it to their own renderers.
  They do not know how the state was computed.

## Data model

### Grid

Computed once per match, when `FogOfWarSystem` is initialised (after `GameAIMap` init, planets
known):

- `Bounds` = AABB of all planet positions, expanded by `settings.boundsMargin` on every side.
- `cellSize` = `settings.cellSize` (world units).
- `cols`, `rows` = `ceil(Bounds.size / cellSize)`.
- `gridOrigin` = world position of the `[0,0]` cell's corner (`Bounds.min`).
- Cell → world (centre): `gridOrigin + (x + 0.5, y + 0.5) * cellSize`.
- World → cell: `floor((world − gridOrigin) / cellSize)`, clamped to `[0, cols) × [0, rows)`.

### Emptiness field

`float[] openness01` of length `cols * rows`, baked once and never changed. For each cell centre:

```
d = min( distance to nearest planet position,
         distance to nearest connection segment )
openness01[cell] = smoothstep(settings.openSpaceThreshold,
                              settings.openSpaceThreshold + settings.openSpaceFalloff,
                              d)
```

Connection segments are the same edges `Gameboard.InitPathGraphics` draws — obtained from
`PathingSystem` (`PathingSystem.Instance.ConnectionVectors(list)` already returns each edge as a
`(Vector3, Vector3)` pair). `openness01` is `0` in populated space and ramps to `1` in deep void; the
smooth ramp avoids a visible seam where the effective vision radius changes.

Cost: `cols * rows * (planetCount + segmentCount)` once — a few hundred thousand ops for a typical
board. Negligible.

### Per-player visibility grid

For each player `p`:

- `float[] visibleStrength` — length `cols * rows`, range `[0, 1]`, **replaced** every `Recompute`.
- explored history — `cols * rows` bits packed into a `ulong[]` (or `byte[]`), **OR-accumulated**,
  never cleared during a match.

Memory: for a 100×100 grid, 2 players ≈ `100*100*4*2` bytes strength + `100*100/8*2` bytes explored
≈ 82 KB. Fine.

### Scratch grid for `AllPlayers`

Two `float[] / bit[]` buffers of the same size, filled on demand when the view mode is `AllPlayers`
or on `Recompute` while that mode is active: per cell `strength = max over p`, `explored = any p`.

## Visibility computation

`Recompute()` runs **for every player**, not just the displayed one, so persisted explored history
stays correct regardless of which view is active.

For player `p`:

1. **Gather vision sources** (list of `(Vector2 pos, float radius)`):
   - Each planet with `planet.GetPopulationFraction(p) > 0` → `(planet.Position, settings.planetVisionRadius)`.
   - Each planet with a docked `Ship` where `Owner == p` → `(planet.Position, settings.shipVisionRadius)`.
   - Each `GameAI.GameAIOrder` with `PlayerId == p` and
     `Type ∈ { OrderTypePopulationTransport, OrderTypeShipTransport }` → the **traversed corridor**
     of `GameAIMap.GetPath(Origin, Target)`. With
     `progress = clamp((TotalDelay − TimingDelay) / TotalDelay, 0, 1)` and
     `revealed = totalPathLength × progress`, emit a source `(pt, settings.shipVisionRadius)` at
     every point along the path from distance 0 to `revealed`, spaced
     `settings.shipVisionRadius × 0.75` apart (plus the exact endpoints). Overlapping the samples
     this way means no planet the ship passed between two turn positions is skipped — the earlier
     single-interpolated-point form let a fast ship "jump over" intermediate planets. Sampling the
     whole traversed length each turn is stateless; the corridor reads bright during the flight and
     fades to explored once the order completes.
2. **Per source**, compute one effective radius from the openness at the *source's own position*:
   ```
   effectiveRadius = lerp(source.radius,
                          source.radius * settings.openSpaceRadiusMultiplier,
                          openness01[cellOf(source.pos)])
   ```
   Then **for each cell** (centre `c`), over all sources, take the maximum of:
   ```
   strength = saturate((source.effectiveRadius − distance(c, source.pos)) / settings.edgeSoftness)
   ```
   A cell well inside any circle is `1`; the rim ramps to `0` over `edgeSoftness` world units. Each
   source is a uniform circle — larger when the source sits in open space, smaller among neighbours.
   (Scaling the radius by the *cell's* openness instead makes `strength` non-monotonic in distance
   and produces a detached outer arc of visibility on the void-facing side of edge planets.)
3. **Store**: `visibleStrength[cell] = maxStrength`. If `maxStrength > settings.visibleCutoff`, set
   the explored bit for `cell`.

Cost per turn: `players * cols * rows * sourceCount`. For 2 players, a 100×100 grid, ~50 sources ≈
1M cheap ops. Negligible at the 0.25 s timer tick.

**Recompute triggers:**
- End of `Gameboard.SingleUpdate()`, after `GameAI.GameAIUpdate()` and `TurnNumber++`.
- After `InitGame` (new match / designer load).
- After a save is loaded (`InitGameFromGaveSave` → once `SetSimulationStatus` has populated the sim).
- On `SetViewMode` only the *display* is re-resolved (and object states re-pushed); the per-player
  grids are not recomputed.

Zoom and pan trigger nothing — the overlay is a world-space object.

## Rendering

### Empty-space overlay

- One `FogOverlay` GameObject parented to `Gameboard`, with a `SpriteRenderer`.
- Backing `Texture2D(cols, rows, RGBA32, mipChain: false)`, `filterMode = Bilinear`,
  `wrapMode = Clamp`. Wrapped in a `Sprite` whose world size equals `Bounds.size` (choose
  `pixelsPerUnit = cols / Bounds.size.x`), positioned at `Bounds.center`.
- `sortingLayer` = default, `sortingOrder` = a constant well above the planet markers (they are at
  `sortingOrder` 2; world-space stat canvases at 0). The HUD `UIDocument` is screen-space and always
  renders on top, so the dropdown is never covered.
- Per cell, from the **displayed** grid:
  ```
  baseAlpha = explored ? settings.exploredColor.a : settings.unseenColor.a
  rgb       = explored ? settings.exploredColor.rgb : settings.unseenColor.rgb
  alpha     = lerp(baseAlpha, 0, visibleStrength)      // visible → clear, with soft rim
  ```
  Written via `SetPixels32` + `Apply()` each `Recompute` / `SetViewMode` (~10k pixels).
- `FogViewMode.NoFog` disables the `SpriteRenderer`.

### Per-object fog state

`Gameboard.FogUIUpdate()` iterates the existing collections and, for each, samples
`FogOfWarSystem.SampleVisibility(worldPos)` (bilinear over the displayed grid) → a `FogState`:

```
strength > visibleThreshold        → Visible
else if explored at that point     → Explored
else                               → Hidden
```

- **`PlanetUIObject.SetFogState(state)`** (new method):
  - `Visible` → current behaviour (all renderers on, stats live).
  - `Explored` → planet `SpriteRenderer` colour multiplied by `settings.exploredObjectDim`; name
    label shown; the stats `Canvas` (population / food / grotsits / morale) and the fleet icon
    hidden.
  - `Hidden` → whole GameObject `SetActive(false)`. This also removes it from the
    `EventSystem.RaycastAll` used by `PlanetUIObject.OnPointerClick` and
    `Gameboard.IsPointerOverPlanet`, so an unseen planet cannot be selected.
- **`LineDrawObject.SetFogState(state)`** (new method) for connection lines: sample at both endpoints
  and the midpoint, take the most-visible result. `Visible` → normal; `Explored` → line + arrow
  colour dimmed by `settings.exploredObjectDim`; `Hidden` → renderer(s) disabled.
- **Order lines** (`LineDrawObject` tagged `OrderLineDraw`, rebuilt each turn in
  `DisplayOrderGraphics`): after creation, set `Hidden` unless the order's `Origin` or `Target`
  planet is at least `Explored` in the displayed view; otherwise `Visible`.

`FogViewMode.NoFog` forces every object to `Visible` and skips sampling.

### View mode application

`SetViewMode` (from the dropdown) and `Recompute` (from the turn loop / load) both end by:
1. resolving the displayed grid (`Player n` → that player's grids; `AllPlayers` → scratch union;
   `NoFog` → none),
2. rewriting the overlay texture,
3. calling `Gameboard.FogUIUpdate()` to re-push object states.

## Debug view control

- `GameButtons.uxml` gains `<ui:DropdownField name="FogViewDropdown" />` near the button column.
- `GameButtonHandler`:
  - `Setup` queries `Gameboard.Instance.NumPlayers` and sets
    `choices = ["No Fog", "All Players", "Player 0", …, "Player {N-1}"]`, `value = "No Fog"`.
  - `RegisterValueChangedCallback` maps the string to `FogViewMode` + player index and calls
    `Gameboard.SetFogViewMode(mode, playerIndex)` → `FogOfWarSystem.SetViewMode(...)`.
  - A `RefreshFogView()` method rebuilds `choices` (player count can differ between matches) and
    resets to `No Fog`; `Gameboard` calls it at the end of `InitGame`.
- Default `No Fog` keeps default runs visually identical to today until a developer opts in.

## `FogOfWarSettings` ScriptableObject

`[CreateAssetMenu(menuName = "Scriptable Objects/FogOfWarSettings")]`. One default asset committed
under `Assets/` (alongside `GameAIConstants4ProductionTypes.asset`). Referenced by a `[SerializeField]`
on `Gameboard`, passed to `FogOfWarSystem` at init.

| Field | Type | Purpose |
|---|---|---|
| `cellSize` | `float` | grid resolution, world units |
| `planetVisionRadius` | `float` | base vision radius from a populated planet |
| `shipVisionRadius` | `float` | base vision radius from a ship (docked or in flight) |
| `openSpaceRadiusMultiplier` | `float` | radius multiplier where `openness01` = 1 |
| `openSpaceThreshold` | `float` | distance from nearest planet/segment where open space begins |
| `openSpaceFalloff` | `float` | width of the smooth ramp above the threshold |
| `edgeSoftness` | `float` | soft-rim width of a vision circle, world units |
| `visibleCutoff` | `float` | strength above which a cell becomes permanently explored |
| `visibleThreshold` | `float` | strength above which an object samples as `Visible` |
| `unseenColor` | `Color` | RGB tint + alpha for never-seen space |
| `exploredColor` | `Color` | RGB tint + alpha for explored-not-visible space |
| `exploredObjectDim` | `float` | 0–1 colour multiplier for explored planets / lines |
| `boundsMargin` | `float` | world padding added around the planet AABB |

## Save / load

`SaveLoadSystem.PlayerSave` gains three **appended** fields:

| Field | Type | Notes |
|---|---|---|
| `exploredGrid` | `string` | base64 of the 1-bit-per-cell packed explored array for this player |
| `exploredCols` | `int` | grid width the bits were saved at |
| `exploredRows` | `int` | grid height the bits were saved at |

- **Save**: `FogOfWarSystem.GetExploredPacked(p)` returns `(byte[] bits, int cols, int rows)`;
  `SaveLoadSystem` base64-encodes `bits`.
- **Load**: after `FogOfWarSystem` is initialised for the loaded board (grid dimensions known),
  `SetExploredPacked(p, bits, cols, rows)` — applied only if `cols == grid.cols && rows == grid.rows`
  (i.e. `cellSize` and the board are unchanged since the save); on mismatch the explored history is
  discarded and the player starts unexplored. Then `Recompute()` fills `visibleStrength`.
- Only `string` / `int` fields — no new nested serializable types, `JsonUtility`-safe.
- An older save with no `exploredGrid` deserialises to `""` → treated as "start unexplored."

## Lifecycle & integration points

| Site | Change |
|---|---|
| `Gameboard.InitGame(List<PlanetSpawnData>)` | After `GameAI.InitGameAI` + `InitPlanetGraphics` + `InitPathGraphics`: create `FogOfWarSystem` if null; `Init(planetList, connectionSegments, settings)` (compute bounds, bake emptiness, allocate per-player grids); `buttonHandler.RefreshFogView()`; `Recompute()`; `FogUIUpdate()`. |
| `Gameboard.ClearGraphics` / `ClearExistingGameState` | Destroy the `FogOverlay` GameObject and clear `FogOfWarSystem` per-player state, mirroring existing graphics teardown. |
| `Gameboard.SingleUpdate` | After `GameAI.GameAIUpdate()` and `TurnNumber++`, before/within `BoardUIUpdate`: `_fogOfWarSystem.Recompute()`; `FogUIUpdate()`. |
| `Gameboard.PlanetaryUIUpdate` / new `FogUIUpdate` | For each `PlanetUIObject`, after `UIUpdate()`, call `SetFogState(...)`. `FogUIUpdate` also handles connection lines and order lines. |
| `Gameboard.DisplayOrderGraphics` | After building each order line, apply its fog state. |
| `Gameboard.InitGameFromGaveSave` | After `SetSimulationStatus`: init `FogOfWarSystem`, `SetExploredPacked` per player, `Recompute()`, `FogUIUpdate()`. Covers both the designer-board and addressable-board load paths. |
| `SaveLoadSystem` save/load | Read/write the three new `PlayerSave` fields. |
| `GameButtons.uxml` / `GameButtonHandler` | Add and wire `FogViewDropdown`. |
| new `FogOfWarSettings.cs` + default asset | Tunables. |
| new `FogOfWarSystem.cs` | The system. |
| `PlanetUIObject.cs` / `LineDrawObject.cs` | Add `SetFogState`. |

No change to `OnScrollPerformed`, pan, `GameAIMap`, `PlayerAI`, `PathingSystem`, or any simulation
code.

## Namespace / naming

- `FogOfWarSystem` and `FogOfWarSettings`: place in a `Fog/` folder under `Assets/Flatspace/` and use
  a `FlatSpace`-cased namespace consistent with neighbouring presentation code (`FlatSpace.Game` is
  the closest — confirm against the folder chosen during implementation). `PlanetUIObject` and
  `LineDrawObject` are in the global namespace today; the new methods stay with their class.
- New `FogViewMode` and `FogState` enums are new code (not serialized to JSON/YAML), so ordering is
  not load-bearing — but `FogState` is not persisted and `FogViewMode` is only held at runtime.

## Manual verification

No automated test infrastructure exists in the project. Verify in Play mode from
`Assets/Scenes/Flatspace.unity`:

1. **Per-player views differ.** New match, dropdown → `Player 0`: only player 0's capital region is
   clear, the rest near-black. → `Player 1`: a different region is clear. → `All Players`: the union
   is clear. → `No Fog`: rendering is identical to `main` before this change.
2. **Circular falloff.** Vision boundaries are smooth curves, not stair-stepped at cell edges.
3. **Open space.** The void between two systems is dark-grey / partially lit, never permanently
   full-black, with `openSpaceRadiusMultiplier > 1`.
4. **Ship vision travels.** Run turns until a colony ship launches; its route lights a corridor while
   in flight, which decays to explored-grey once it passes.
5. **Explored history.** Let player 0 reveal an area, move vision away, confirm it drops to
   explored-grey (not black). Save, reload → the explored-grey area persists; currently-visible is
   recomputed.
6. **Camera.** Zoom and pan keep the overlay aligned to the board with no seam or drift.
7. **Interaction.** A `Hidden` planet cannot be clicked/selected; an `Explored` planet shows a dimmed
   marker with name only and no stats; the HUD dropdown renders above the overlay.
8. **Teardown.** Load a different board / save mid-match; no leftover overlay, no stale grids, dropdown
   choices match the new player count.

Optional: a `FogOfWarSystem` editor gizmo drawing the grid bounds and each current source's radius.

## Follow-ups (out of scope, recorded for later)

- **Gate the AI on visibility.** The reason `FogOfWarSystem` is a standalone seam: a later task can
  feed it into `PlayerAI` / `ScoreMatrix` so the AI only acts on what it currently sees.
- **Explored-planet detail panel.** Show a "last known" snapshot in `PlanetDetailUIController` for an
  explored-but-not-visible planet instead of full live data.
- **Player-scoped presentation.** Converge the fog view selector with `owningPlayerId` so choosing
  `Player n` also filters notifications, the status bar, and any other player-specific HUD to that
  player.
- **Temporal fade.** Animate the overlay between states across a few frames instead of snapping at
  the turn boundary.

### Deferred during implementation (2026-09-08)

- **Overlay should cover the whole background, not just the planet bounding box.** Today the grid
  bounds are the planet AABB + `boundsMargin`; a planet in the corner of that region shows a hard
  fog edge when the camera scrolls past it into empty space. The overlay (or a second, always-dark
  backdrop behind it) should extend to cover the full scrollable board area / camera view so there
  is no visible edge.
- **Adjacency visibility.** From any planet that meets the visible criteria (player population,
  owned docked ship), its directly-connected (`Planet.Connections`) neighbour planets and the
  `LineDrawObject`s of those connections should also read as visible, independent of the distance
  grid.
- **Debug dropdown selection does not persist across save/load.** After a load the fog view resets
  to "No Fog" (the dropdown is rebuilt by `RefreshFogView`); persisting the selection would be nicer
  for iterative testing.
- **Explored planets show no name.** `PlanetUIObject.SetFogState` disables the whole `_statsCanvas`
  for explored planets, which also hides the name label; the spec wants the name visible for
  explored planets. Needs a serialized reference to just the stats sub-panel (or splitting the name
  onto its own canvas).
- **Debug dropdown closed-field value text is clipped.** The `DropdownField`'s closed-state value
  label renders collapsed (~100×8 px) in the runtime panel; `labelElement`-hide and `TextElement`
  flex/height/font forcing did not fix it. Likely needs a USS stylesheet on the HUD `PanelSettings`.
- **`visibleThreshold` (0.35) < `visibleCutoff` (0.5) is a tunables trap.** A cell whose strength
  lands in that band classifies as `Visible` but never sets its explored bit, so on losing vision
  the planet goes `Visible → Hidden` (vanishes) instead of `Visible → Explored` (dims). Either keep
  `visibleThreshold >= visibleCutoff` by default, or mark explored using the classification
  threshold. Document the intended relationship on the fields.
- **`RecomputeFromSources` has no spatial culling and `CellCount` has no cap.** Every source is
  distance-tested against every cell each turn; a small `cellSize` on a large procedural board
  produces a multi-megapixel `Texture2D` re-uploaded every turn. Add per-source bounding-box
  iteration (squared distance) and a cell-budget `Debug.LogWarning` in `InitCore`.
- **Minor leaks / polish:** the `Sprite` from `Sprite.Create` is never destroyed by `DestroyOverlay`
  (one leak per `InitGame`); `SetExploredPacked` triggers a full overlay rebuild per player on load;
  in-flight ship vision uses `progress` clamped `[0,1]` while the drawn ship icon is clamped
  `[0.15, 0.85]` (tip/marker disagree); `_planetNames` is dead state.
