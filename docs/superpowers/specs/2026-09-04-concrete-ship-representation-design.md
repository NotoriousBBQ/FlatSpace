# Concrete Ship Representation & Fleet UI — Design

Status: approved by user, ready for implementation planning.

## Problem

`ColonyShip` and `WarShip` are currently abstract:

- A planet only tracks `Planet.HasColonyShip` (a single bool). There is no equivalent for
  `WarShip` at all — `Planet.StageCompletedProductionItem` has a literal `// create game object
  from CurrentProduction` comment where WarShip handling should go, and does nothing for it.
- A second `ColonyShip` completing production while one is already docked silently does nothing
  (`HasColonyShip = true` is idempotent), so the second ship is lost.
- There is no gameboard or Planet Detail indication that ships are present anywhere, and no UI to
  inspect them.
- The `LineDrawObject` used to visualize in-flight orders draws a straight line between the origin
  and target planet and interpolates the progress marker linearly along that straight line — not
  along the real (possibly multi-hop) A* path the order's cost was computed from.
- `ShipData` (a `ScriptableObject` with `shipName`/`shipSpeed`/`shipHealth`/`shipHealthMax`/
  `shipDefense`/`shipDefenseMax`/`shipOffense`/`shipOffenseMax`/`shipIcon`) already exists as two
  placeholder assets (`WarShip.asset`, `ColonyShip.asset`, all-zero) but nothing references it.

## Scope

In scope:
- Concrete ship instances ("game objects") for both ColonyShip and WarShip, created when
  `StageCompletedProductionItem` runs for either.
- A fleet-presence icon on the gameboard planet marker and on Planet Detail, shown whenever a
  planet has one or more docked ships.
- A Fleet UI panel, opened by clicking either icon, listing the ships docked **at that one
  planet** with their stats.
- A new order type that moves a count of WarShips from one planet to another and docks them at
  the target on arrival — the mechanical plumbing only.
- `LineDrawObject`/order-graphics changes so the line and progress marker follow the real
  A*-computed path instead of a straight line between endpoints.
- Save/load support for docked ships.

Explicitly out of scope (deferred to a later task):
- Any AI decision-making for *when or why* to build up and send a group of WarShips somewhere.
  Nothing in this pass creates a ship-transport order — the mechanism exists and is wired into
  `GameAI.ExecuteOrder`, but is genuinely untriggered until a future task adds that AI logic.
- Combat, ship damage, or any other use of the mutable-looking `shipHealth` field on `ShipData`.
- A manual/debug way to trigger a ship-transport order for testing — deferred with the AI logic
  above.

## A. Ship representation & docking

- New `Ship : MonoBehaviour` class — **one** class, not a `WarShip`/`ColonyShip` subclass pair —
  with:
  - `public enum ShipKind { ColonyShip, WarShip }` and a `public ShipKind Kind` field.
  - `public int Owner` (playerId), matching `Planet.Owner`'s convention.
  - `public ShipData Template` — a reference to the existing `WarShip.asset` / `ColonyShip.asset`,
    used read-only for display (name/speed/offense/defense/icon). No per-instance mutable stats
    are cloned — nothing in this pass can change them (no combat system), so a live reference to
    the shared template is enough.

  This mirrors `Planet`'s own convention: one `MonoBehaviour` class, a kind-enum distinguishing
  flavor (like `Planet.PlanetType`), not a subclass hierarchy.

- `GameAIConstants` (already the ScriptableObject holding per-type template references, e.g.
  `resourceData`) gets two new fields:
  ```csharp
  public ShipData colonyShipData;
  public ShipData warShipData;
  ```
  `Planet` already holds a private `_gameAIConstants` reference, so `StageCompletedProductionItem`
  resolves the right template directly — no new lookup path.

- `Planet` gets `public List<Ship> DockedShips = new List<Ship>();`.

- `Planet.StageCompletedProductionItem` changes from:
  ```csharp
  else if (CurrentProduction?.Item.subType == "ColonyShip")
  {
      HasColonyShip = true;
  }
  ```
  to creating a real docked `Ship` for both ColonyShip and WarShip:
  ```csharp
  else if (CurrentProduction?.Item.subType == "ColonyShip")
  {
      DockShip(ShipKind.ColonyShip, _gameAIConstants.colonyShipData);
  }
  else if (CurrentProduction?.Item.subType == "Warship") // catalog JSON spells it this way — see gotcha below
  {
      DockShip(ShipKind.WarShip, _gameAIConstants.warShipData);
  }
  ```
  where `DockShip` does `var ship = this.AddComponent<Ship>(); ship.Kind = kind; ship.Owner =
  Owner; ship.Template = template; DockedShips.Add(ship);` — `this.AddComponent<T>()` on `Planet`
  is the same pattern `GameBoard`/`GameAIMap` already use for `GameAI`/`Player`, so the ship is a
  real component stacked on the planet's own GameObject.

- **Gotcha to carry into implementation:** `Production Catalog.json` spells the WarShip subType
  `"Warship"` (lowercase *s*), not `"WarShip"`. The subType string check must match that exact
  casing — this is the same class of load-bearing-spelling trap `CLAUDE.md` already documents for
  other identifiers in this codebase.

## B. Colonization stays behavior-identical

- `Planet.HasColonyShip` (bool) is removed and replaced everywhere it's read with:
  ```csharp
  DockedShips.Any(s => s.Kind == Ship.ShipKind.ColonyShip)
  ```
  Call sites: `PlayerAI.PlanetHasColonyShip`, and the same expression inline wherever
  `IsValidColonizer`'s urgency scoring reads it. Behavior is identical — same boolean answer, now
  sourced from the list instead of a flag.
- `GameAI.ExecuteOrder`'s `OrderTypeRemoveShip` case (currently `targetPlanet.HasColonyShip =
  false;`) becomes: find the first docked `Ship` with `Kind == ColonyShip` on the target planet,
  remove it from `DockedShips`, and `Destroy()` the component.
- Nothing about when a colonizer launches, how the population count travels via
  `OrderTypePopulationTransport`, or how colonization resolves on arrival changes — this section
  only repoints the two places that touch `HasColonyShip` today.

## C. WarShip transport order + real path-based line visualization

### New order type

`GameAIOrder.OrderType` gains `OrderTypeShipTransport`, alongside the existing
`OrderType*Transport` entries. `Data` is the ship count (`int`) — same shape as
`OrderTypePopulationTransport`. `GameAI.ExecuteOrder` gets a case for it:

- **Immediate** order at the origin: remove N docked WarShips from the origin planet's
  `DockedShips`, destroying their components (mirrors how population is decremented immediately
  today for colonization).
- **Delayed** order completing at the target: create N new docked WarShips there via
  `GameAIConstants.warShipData` (the same `DockShip` helper from section A) — this is "those ships
  are associated with the target planet."

Nothing in `PlayerAI` constructs this order in this pass (see Scope) — it's real, compiled,
reachable code, just not yet called by any decision logic.

### Path-based visualization (applies to every transport order type, not just ships)

- `GameAIMap` gets `public Path GetPath(string origin, string destination)`, resolving the
  existing `Planet.DistanceMapToPathingList[destination]` entry's `PathingIndex`/`PathReversed`
  back to the already-computed `Path` sitting in the private `_planetPathings` list (computed once
  at init today, just not exposed). `PathReversed` means the node list needs reversing before
  returning it origin-to-destination.
- `GameBoard.DisplayOrderGraphics` fetches that path's `PathNode` positions instead of just the
  two planet endpoints, and computes the marker's position by walking cumulative distance along
  the real polyline to the `progressAmount` fraction (replacing the current straight-line lerp
  between `point1`/`point2`).
- `LineDrawObject` gets a new method, e.g. `SetPath(List<Vector3> pathPoints, float
  progressAmount)`, that:
  - Sets `lineRenderer.positionCount` and all positions to the full polyline (so the *line* itself
    follows the real multi-hop route, not just a straight line — a natural side benefit).
  - Places the marker sprite at the cumulative-distance point along that polyline and orients it
    to the local segment's direction (replacing today's single global direction).
- The existing per-order-type perpendicular offset (so concurrent orders between the same two
  planets don't overlap) still applies, now to every point in the polyline instead of just the two
  endpoints.
- `OrderTypeShipTransport` is added to `OrderHasGraphic`'s list and given a color in
  `ColorForOrderType`, exactly like the three existing transport order types.

This section is a strict visual upgrade — it does not change order timing, cost, or completion
logic, only where the line and marker are drawn.

## D. UI: gameboard icon, Planet Detail icon, and the Fleet UI panel

- **Gameboard icon:** `PlanetUIObject` gets a new child `Image` (fleet icon), `raycastTarget =
  true`, with its own small click-handler component (parallel to `PlanetUIObject`'s own
  `IPointerClickHandler`). Toggled in the existing `UIUpdate()` method (already called every turn
  for every planet, in `Gameboard.PlanetaryUIUpdate()`) based on `planet.DockedShips.Count > 0`.
  No new update loop.
- **Planet Detail icon:** a new named element in `PlanetDetailUI.uxml` (same pattern as
  `PlanetIcon`), shown/hidden in the existing `PlanetDetailUIController.UpdatePlanetDetail()` based
  on the same `DockedShips.Count > 0` check.
- **Fleet UI panel:** new `FleetUIController` + its own `UIDocument`, built as a close analog of
  `PlanetDetailUIController` — same `enabled`-toggle show/hide, same `ContainsScreenPoint` bounds
  test (reusing the pattern from the click-outside-to-close work already done for Planet Detail).
  Lists the docked ships for one planet: kind, `ShipData.shipName`, and the speed/offense/defense
  stats, read-only.
- **Click routing** (all reusing the outside-click mechanism already built for Planet Detail):
  - `Gameboard` tracks which of {Planet Detail, Fleet UI} is currently open, and exposes
    `ShowFleetUI(planetName)` / `HideFleetUI()` / `FleetUIShowing()`, mirroring
    `ShowPlanetDetail`/`HidePlanetDetail`/`PlanetDetailShowing` exactly.
  - A click on the gameboard fleet icon calls `ShowFleetUI` directly (its own small click target,
    bypassing the "is this a planet click" raycast check).
  - A click on the Planet Detail fleet icon does the same.
  - A click outside whichever panel is currently open closes that one — exactly like Escape does
    for Planet Detail today.

## E. Save/load

- `SaveLoadSystem.GameSave.PlanetSave` gets a new list of ship saves, each just `Kind` + `Owner` —
  stats live on the static `ShipData` template and are re-resolved via `GameAIConstants` on load,
  so nothing else needs duplicating.
- `OrderTypeShipTransport` needs no `OrderSave` schema change — `OrderSave` already stores any
  order as an enum + an int/float `data` + `dataType`, the same shape every other transport order
  already uses.

## Testing / verification

No automated test framework exists in this repo (per `CLAUDE.md`). Verification is:
- Compile/inspection checks (Rider MCP tools) after each implementation step.
- Manual verification in Play mode: build a ColonyShip and a WarShip, confirm both icons appear on
  the gameboard and in Planet Detail, confirm the Fleet UI lists them with stats, confirm
  colonization still completes exactly as before, and confirm the order line/marker follow the
  real path.
- `OrderTypeShipTransport` itself has no in-game trigger in this pass (see Scope) and so cannot be
  exercised end-to-end until the later AI-logic task adds a way to create one.
