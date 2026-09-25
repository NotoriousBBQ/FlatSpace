# Consolidate: Outer-Only Garrisons and Assault Targeting

## Purpose

`FUTURE_FEATURES.md` lists AIStrategyConsolidate's fleet build. This spec covers the **ship-transport half**
of it: once a player is in Consolidate (see `2026-09-25-consolidate-first-contact-design.md`), its warships
(a) hold only the front-line ("outer") planets at home and (b) mass on a known enemy-occupied planet in a
force sized to the enemy's docked fleet.

Nothing on the board can fight yet (ship-to-ship combat, blockade and invasion are future work), so an
assault fleet that arrives simply docks beside the enemy's ships. This cycle builds the *targeting and
sizing*; the payoff arrives with combat.

## Out of scope

- Production/industry weights (the Warship weight scaling with enemy docked ships): a separate later cycle.
- Combat, blockade, invasion, distribution centers.
- Garrisons at non-outer planets in Consolidate (a seam is left for it, below, but no behavior).
- Launching an assault fleet together (see Known limitations).
- Expand's category numbering, garrison sizes, rounds, and target priority: unchanged.

## Current state (verified)

- `ShipTransportPlanner` (`FlatSpace.AI`) is constructed with `(GameAIMap, playerId)` and has no notion of
  strategy. `IsOuter` is: colonized by this player and having a neighbour with `Population.Count == 0`.
- Category (lower = better): 1 Desert/Industrial/Farm/Ocean, 2 outer, 3 Prime, 4 high traffic, 5
  Verdant/Desolate. A planet's priority is its best applicable category and its garrison is the largest among
  its categories. Target choices sort by category, then path cost.
- `BuildStates` treats warships docked on a planet the player does *not* hold as "stranded": source-only,
  garrison 0, so entirely spare. An assault fleet sitting on an enemy planet would therefore be reclassified
  as spare and sent home.
- Garrison rounds: `round = 1 + min(floor((docked + incoming) / garrison))` over garrisoned, reachable planets.
- Category-5-only planets are left out entirely until total warships reach a per-colonized-planet threshold.
- `PlayerAI.ProcessShipActions` builds the planner, turns its `ShipAction`s into the order trio with
  `EmitShipOrders`, and today runs for Consolidate too (Consolidate plays the Expand routine).
- `PlayerKnowledge` limits what the AI may reason about: enemy ships on unknown planets must not be counted.

## Design

### 1. Shared outer definition (both strategies)

`IsOuter` becomes: colonized by this player and having a neighbour that is **not colonized by this player**
(`!IsColonized(neighbour)`, which covers empty, enemy-held and contested planets). This is effectively a
no-op for Expand: a player enters Consolidate on the turn it first sees an enemy on a known planet, and
every neighbour of its own planets is known, so an Expand planner never has an enemy-held neighbour to
react to.

### 2. Strategy-aware `ShipTransportPlanner`

The constructor gains an optional third parameter, `PlayerAI.AIStrategy strategy = AIStrategyExpand`, so
existing callers and `ShipTransportSelfCheck` are unaffected. Under Consolidate only:

- **Seam for future non-outer maintenance:** a single method `MaintainsGarrison(Planet)`: true for every
  planet under Expand; under Consolidate true only for outer planets. A colonized planet where it is false
  gets garrison 0 and no category, so every ship on it is spare, and it is included as a source even if it
  is category-5-only and locked (the lock check runs after this one). To garrison non-outer planets later,
  change this one method. No setting or constant is added.
- **Round 1 only:** the round is fixed at 1, so outer planets fill to their garrison once and never enter a
  second fill.
- **Outer first:** a separate `Rank` (used only for sorting target choices, carried on `PlanetState` and
  `ShipChoiceElement`) is 0 for any planet whose applicable categories include outer, otherwise its
  category; Expand's rank equals its category. `CompareChoices` sorts by rank, then path cost. Category
  numbers and garrison sizes do not change.
- **Held planet:** an optional `HeldPlanet` name (the assault target). Warships docked there are not
  "stranded", so they are neither sources nor targets of the home plan.
- **Exposed states:** `Plan()` retains its `PlanetState` list as `LastStates` so the assault planner can
  read each planet's spare ships after home defence.

### 3. `AssaultPlanner` (new)

New file `Assets/Flatspace/GameAI/AssaultPlanner.cs`, namespace `FlatSpace.AI`, plain class, constructed
with `(GameAIMap, playerId)`, never touching `Gameboard.Instance`.

- **Enemy total:** the sum of docked warships with `Owner >= 0 && Owner != playerId` on the player's
  *known* planets (`Knowledge.KnownPlanets`). The `Owner >= 0` guard deliberately excludes ownerless ships.
- **Candidates:** known planets with enemy population (`Population.Exists(p => p.Player != playerId)`),
  reachable by the same rule as other ship transport (`2 <= NumNodes <= maxPathNodesForShipTransport`) from
  at least one planet where the player holds warships.
- **Target (sticky):** the candidate where the player already has the most warships docked plus incoming;
  with none, the candidate with the cheapest path from a planet holding warships; ties by planet name
  (ordinal). Re-chosen each turn from state, so nothing is persisted.
- **Required force:** `max(assaultMinimumShips, ceil(enemyTotal * assaultRatio))`.
- **Deficit:** required force minus the player's warships already docked or incoming at the target. No
  target, or a deficit of 0, means no actions.
- **Sources and orders:** planets with spare ships remaining after the home plan (their `Spare` minus what
  the home actions already send), reachable to the target, cheapest path first; each sends
  `min(remaining spare, deficit left)`. There is one target, so no `ScoreMatrix` is needed. Output is
  `ShipAction`s (Kind WarShip), emitted by the existing `EmitShipOrders`.

### 4. Orchestration in `PlayerAI.ProcessShipActions`

- Expand (and any non-Consolidate strategy): unchanged.
- Consolidate: choose the assault target, build the planner with `HeldPlanet` set to it, run the home plan,
  then run the assault plan on `LastStates` and the home actions, and emit both sets of actions.

### 5. Tunables

Two new fields on `GameAIConstants`, with in-code defaults so the asset needs no edit until tuned:
`assaultRatio = 1.5f` and `assaultMinimumShips = 3`.

### 6. Logging

`AITuningLogger.LogAssaultTarget(turn, playerId, target, requiredForce)` writes
`T<turn>|P<id>|AssaultTarget|<planet>|<required>`, emitted by `PlayerAI` whenever the chosen target differs
from the last one it logged (remembered in a plain field; not saved, so a load re-logs once). The existing
`ShipMove`/`ShipArrive` lines already cover the transports.

### Persistence

None new. Every input (target, force, held planet) is derived from planet state each turn.

## Known limitations

- **Ships trickle to the target and accumulate there** rather than launching together. This is simple, reuses
  the existing order path, and is harmless while no combat exists. It becomes a real weakness once combat is
  added, because ships arriving one at a time would meet the enemy piecemeal. Revisit in the combat cycle.
- The assault target is chosen over *known* planets only, so an enemy the player has not yet discovered is
  never attacked.
- If the target planet changes, ships left on the old target become stranded (spare) and are re-dispatched.

## Testing

Extend `Assets/Editor/ShipTransportSelfCheck.cs` (`FlatSpace -> AI -> Run Ship Transport Self-Check`),
building minimal `GameAIMap`/`Planet` sets directly with distinct planet positions. Cases:

1. **Shared outer definition:** a colonized planet whose only non-colonized neighbour is enemy-held is
   outer; one whose neighbours are all its owner's is not.
2. **Expand unchanged:** all existing assertions still pass with the default strategy.
3. **Consolidate outer-first:** an outer Farm sorts ahead of an interior Farm as a target, and Expand's
   ordering is unchanged for the same board.
4. **Round 1 only:** with every outer planet full, no outer planet is a target for a second fill, and ships
   above its garrison are spare.
5. **`MaintainsGarrison`:** non-outer planets have garrison 0 and are spare-only under Consolidate,
   including a category-5-only planet that would be locked under Expand.
6. **Enemy total** counts only known planets and only ships with a valid, different owner.
7. **Required force** applies the ratio and the minimum floor.
8. **Target choice:** picks the nearest reachable enemy-occupied known planet, is sticky once ships are
   committed, and ignores unreachable and unknown planets.
9. **Deficit stops sending:** ships docked plus incoming at the target reduce the deficit, and it is 0 when
   met.
10. **Held planet:** warships docked on the assault target are not reclassified as spare.
11. **Home before assault:** ships needed by an outer garrison are not also committed to the assault.

`AITuningLogger.LogAssaultTarget` writes to the real filesystem and so has no self-check, by design;
verify it in a Play-mode run with `_logAIEvents` on.

## Files touched

- `Assets/Flatspace/GameAI/ShipTransportPlanner.cs`
- `Assets/Flatspace/GameAI/AssaultPlanner.cs` and `AssaultPlanner.cs.meta` (new; the `.meta` must be committed)
- `Assets/Flatspace/GameAI/ShipMatrix.cs` (`Rank` on `ShipChoiceElement`)
- `Assets/Flatspace/GameAI/GameAIConstants.cs`
- `Assets/Flatspace/GameAI/PlayerAI.cs`
- `Assets/Flatspace/Diagnostics/AITuningLogger.cs`
- `Assets/Editor/ShipTransportSelfCheck.cs`
- `CLAUDE.md` (Ship Transport and AI Tuning Log sections) and `FUTURE_FEATURES.md`
