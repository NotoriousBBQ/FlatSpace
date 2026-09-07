# Concrete ship representation & fleet UI — execution ledger

Companion to
[2026-09-04-concrete-ship-representation.md](2026-09-04-concrete-ship-representation.md) (the plan) and
[2026-09-04-concrete-ship-representation-design.md](../specs/2026-09-04-concrete-ship-representation-design.md)
(the design spec). Records the rulings made while executing the plan's 13 tasks plus the final
whole-branch review, and the manual steps still needed to bring the branch to a playable state.

## Rulings

1. **Pre-flight.** Task 9 (`FleetIconClickHandler`) forward-references `Gameboard.ShowFleetUI`, which
   Task 11 doesn't define until later. This is a known, plan-acknowledged intermediate compile state —
   not a defect — so tasks were dispatched in sequence rather than blocked on it.
2. **Task 3.** The plan's brief for replacing `Planet.HasColonyShip` missed a third call site
   (`Planet.CheckColonizationReady`) — a gap in the plan's research, not implementer overreach. The
   implementer's in-scope fix was accepted.
3. **Task 3.** Rider flags `this.AddComponent<Ship>()` as unresolvable. Investigated and confirmed
   (independently, twice more later in the branch) to be a Rider PSI tooling artifact, not a real compile
   error — the identical `AddComponent<T>()` pattern compiles clean everywhere else in the codebase
   (`GameAI`, `Player`, etc.).
4. **Task 9.** The plan's brief code for `FleetIconClickHandler.cs` omitted a needed
   `using FlatSpace.Game;` — another gap in the plan, correctly fixed by the implementer.
5. **Final review fix wave.** The fix implementer additionally claimed a 7th "prerequisite bug" (a
   missing `using Unity.VisualScripting;`). Independently verified — by the orchestrator, then a scoped
   re-reviewer — as unused/incorrect, reproducing the same Rider-cache artifact from ruling 3. Parked as
   harmless dead code; the claim that it was load-bearing doesn't hold.

## Outcome

All 13 plan tasks implemented and individually reviewed; a final whole-branch review caught and fixed 6
real issues in one fix round (null-safety, save compat, owner-index guard, Fleet UI refresh — see
`cdd91db`). Project-wide Rider compile check is clean (zero errors) as of the last check in this session.

## Remaining manual steps (need a live Unity Editor)

1. ~~**Scene wiring** (plan Task 11, step 5): add a `FleetUIDocument` GameObject under `Board`.~~ Done —
   confirmed in the uncommitted `Assets/Scenes/Flatspace.unity` diff (`FleetUIDocument` GameObject with
   `UIDocument` + `FleetUIController` components).
2. **Play-mode checklist:**
   - Build a ColonyShip/WarShip and confirm both the gameboard fleet-presence icon and the Fleet UI panel
     show it.
   - Confirm a multi-hop shipment line bends through intermediate planets along the real A* path.
   - Confirm save/reload round-trips docked ships (kind, owner, research snapshot).

## Post-verification fixes (2026-09-05)

Play-mode testing surfaced four fleet-UI issues; fixed together:

1. **Fleet UI panel off-centre / offscreen.** `FleetUI.uxml` combined `align-self: center` with a
   `left: 30%` relative offset, shoving the panel 30% of the panel width to the right. Changed to
   `left: 0` (matching `PlanetDetailUI.uxml`) and `top: 20%`.
2. **Planet Detail fleet-icon click never registered (Y always outside `worldBound`).** The manual
   `RuntimePanelUtils.ScreenToPanel` + `worldBound.Contains` test in
   `PlanetDetailUIController.ContainsFleetIconScreenPoint` (polled from `Gameboard.Update`) was too
   fragile for a 20px target. Replaced with a real `ClickEvent` on the `FleetIcon` element
   (`pickingMode = Position`, callback registered in `Awake`), which uses UI Toolkit's own picking
   against the resolved layout. `ContainsFleetIconScreenPoint` / `CurrentPlanetName` and the
   `Gameboard.Update` branch that called them are gone.
3. **Same-frame close race.** `Gameboard.Update` polls the raw mouse independently of the EventSystem,
   so the click that opened the Fleet UI could be re-processed as an outside-click and close it the
   same frame. Added a `_panelClickFrame` guard set by `ShowFleetUI`.
4. **Gameboard fleet icon not clickable at some positions / ignoring zoom.** The world-space icon is a
   child graphic on the same canvas as the planet, so whether the click is picked up by the planet
   collider (`Physics2DRaycaster`, sprite sorting order 2) or the icon's `Image` (world
   `GraphicRaycaster`, canvas sorting order 0) depends on overlap. Removed `FleetIconClickHandler`
   entirely; `PlanetUIObject.OnPointerClick` (which the click bubbles to either way) now disambiguates
   against the icon's live screen rect via `RectTransformUtility.RectangleContainsScreenPoint`, so it
   works regardless of zoom or which raycaster won. The icon also now gets explicit centre
   anchors/pivot so its `anchoredPosition` is predictable.

Rider's solution build reports pre-existing errors in Unity package cache files
(`com.unity.ugui/MenuOptions.cs`, `com.unity.render-pipelines.core/PassesData.cs`) unrelated to this
work; per-file analysis of the three edited game scripts is clean. Unity Editor recompile is the real
check.
