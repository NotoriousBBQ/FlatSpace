# AIStrategyConsolidate: First-Contact Trigger and Switch

## Purpose

`FUTURE_FEATURES.md` lists `AIStrategyConsolidate` as a strategy phase the AI enters after first contact
with another player (later: fleet build, strategic colonization, planet defense). This spec covers only
the **first cycle**: detecting first contact and switching a player from Expand to Consolidate. Consolidate's
own behavior is deliberately deferred; until then it plays exactly like Expand.

Why this slice first: it builds and verifies the trigger before any behavior depends on it, needs no new
UI, and unblocks distribution centers and combat.

## Out of scope

Fleet build, strategic colonization, planet defense, distribution centers, combat, blockade, invasion,
and any Consolidate-specific weights. `AIStrategyAmass` is untouched.

## Current state (verified)

- `PlayerAI.AIStrategy` already contains `AIStrategyConsolidate`. `PlayerAI.ProcessResults` routes it to
  an empty `case`, so a player switched to it today would do nothing.
- `PlayerAI.ResearchWeightTable` and `IndustryWeightTable` are keyed by strategy. A missing key falls
  back to `DefaultChoiceWeight`, so a strategy with no entry silently loses Expand's tuning.
- `PlayerKnowledge` (`FlatSpace.AI`) is a per-player sticky set of planet names: every vision-source
  planet (population or an owned docked ship) plus its direct neighbours. It records no ownership and
  is not the same as fog-of-war visibility.
- `GameAIMap.Knowledge.Update(...)` runs each turn between `UpdateAllPlanets()` and `ProcessResults()`,
  so knowledge is current when `ProcessResults` runs.
- `PlayerSave.strategy` already round-trips each player's strategy through saves.

## Design

### Contact definition

A player has **contact** when any planet in its known set has population or a docked ship belonging to
*another* player. This is a 1-hop lookout from the player's own presence, and it also catches contested
planets and rival ships that arrive before anyone owns the planet (`Planet.Owner` is `NoOwner` on a
population tie, so ownership alone is not used).

### Components

1. **`PlayerKnowledge.HasContact(GameAIMap map, int playerId, int numPlayers)`** (public, so
   `Assets/Editor/` can call it). Iterates `KnownPlanets(playerId)`; for each planet checks every other
   player id for `GetPopulationFraction(other) > 0f` or a docked ship with `Owner == other`. Must not
   touch `Gameboard.Instance` (`GetPopulationFraction` is already used the same way by
   `GameAIMap.GetVisionSourcePlanets`).
2. **Switch at the top of `PlayerAI.ProcessResults`**, before the strategy `switch`: if `Strategy` is
   Expand and `HasContact` is true, set `Strategy = AIStrategyConsolidate` and log it. One-way and sticky:
   Consolidate never reverts to Expand.
3. **Consolidate behavior:** the `AIStrategyConsolidate` case calls `ProcessResultsStrategyExpand`.
4. **Weight tables:** add `AIStrategyConsolidate` entries to `ResearchWeightTable` and
   `IndustryWeightTable` that alias the same dictionaries as Expand, so the switch changes no weights.
   Remove/replace the "add when needed" comments accordingly, keeping the Amass one.
5. **Logging:** a new `AITuningLogger` event `StrategyChange|<from>|<to>` in the standard
   `T<turn>|P<playerId>|<EventCode>|<fields...>` format, called unconditionally at the switch.

### Data flow

`Knowledge.Update` (turn N) -> `PlayerAI.ProcessResults` -> contact check -> strategy set (if triggered)
-> `switch` runs the (now Consolidate) case -> Expand routine. The switch takes effect the same turn.

### Persistence

None new. `PlayerSave.strategy` saves and restores the current strategy. If a save predates the switch,
contact simply re-triggers on the next turn.

### Error handling

- A player with no known planets, or a single-player game, never has contact.
- `PlayerAI` must resolve `AIMap.Knowledge` and the player count without `Gameboard.Instance`; the
  implementation plan should confirm the available accessors and add one only if none exists.

## Testing

Extend `Assets/Editor/PlayerKnowledgeSelfCheck.cs` (`FlatSpace -> AI -> Run Player Knowledge Self-Check`),
building a minimal `GameAIMap`/`Planet`/`PlayerAI` set directly and giving planets distinct positions
(A* tie-breaking hazard). Cases:

1. Two players far apart, no shared known planets: no contact.
2. Rival population on a planet in the player's known set (a direct neighbour): contact.
3. Rival docked ship, no population, on a known planet: contact.
4. Rival planet outside the known set: no contact.
5. Single player: never contact.
6. `ProcessResults` switches Expand to Consolidate on contact and does not revert on a later turn.
7. The Consolidate weight aliases resolve to the same weights as Expand.

The new `AITuningLogger` event has no self-check by design (it writes to the real filesystem); verify via
a Play-mode run with `_logAIEvents` on and confirm one `StrategyChange` line per player.

## Files touched

- `Assets/Flatspace/GameAI/PlayerKnowledge.cs`
- `Assets/Flatspace/GameAI/PlayerAI.cs`
- `Assets/Flatspace/Diagnostics/AITuningLogger.cs`
- `Assets/Editor/PlayerKnowledgeSelfCheck.cs`
- `CLAUDE.md` (Player Knowledge / AI Tuning Log sections, and the strategy note)
- `FUTURE_FEATURES.md` (mark the first cycle of AIStrategyConsolidate as done)
