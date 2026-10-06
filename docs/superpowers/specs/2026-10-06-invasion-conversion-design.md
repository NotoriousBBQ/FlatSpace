# Invasion by dominance and conversion — design

Date: 2026-10-06. Element (3) of the `ship_combat` milestone ("Planetary invasion"). Brainstormed with the owner; every choice below is theirs
(recommendation first each time). Builds on ship combat, diplomacy and retreat (all in `completed_features.md`).

## Purpose
Give wars territorial stakes: a fleet that wins and holds a planet slowly takes its population, and with it the planet. No new ship type, no
new order. Planet defense (element 4) and the AI blockading on purpose (element 5) come later; this design leaves places for them to plug in.

## Terminology (the owner's)
- **Dominance:** a player's warships are docked at a planet (effective offense above 0) and no warship of a player at war with it is docked there.
- **Conversion** (convert, converting, converted): the process of flipping one inhabitant's `Player` to the dominator.
- **Session:** one unbroken conversion at a planet, by one dominator; state saved on the planet.

## The rule (`ConversionSystem`, pure)
`ConversionSystem.Resolve` (new, `Assets/Flatspace/GameAI/`, namespace `FlatSpace.AI`, no `Gameboard.Instance`) runs in `GameAI.GameAIUpdate`
right after `RunCombat` and before `RunFightProjections`/`UpdateAllPlanets`, in the engine step, so no player's decision order matters and a fight
that clears the last rival warship starts Dominance the same turn. It does nothing while `Diplomacy.Enabled` is false (legacy).

1. **Dominator.** Player D dominates planet P when D has at least one docked warship with `EffectiveOffense > 0` and no player with
   `IsAtWar(D, X, true)` has such a ship there. If two players that are not at war with each other both qualify, the one with more docked
   effective offense dominates, ties to the lower id.
2. **Start.** A session starts only when D dominates P and D is at war (`IsAtWar(D, X, true)`, so a truce is not war) with at least one player X
   holding inhabitants at P. The gate is on the inhabitants' holders, not on `Planet.Owner` (which is `NoOwner` on a tie and becomes D mid-session).
3. **Pace.** Each turn: `p` = D's share of P's current inhabitants; `turnsPerFlip = max(1, conversionTurnsBase x (1 - p))`;
   `ConversionProgress += 1 / turnsPerFlip`. At 1 or more one inhabitant converts and the remainder carries (at most one per turn, by the floor).
4. **Victim order.** The largest at-war holder's inhabitants first (ties to the lower id). When no at-war inhabitants remain the session
   continues against the remaining non-war holders (largest first, ties to the lower id) for as long as the session is unbroken. "Continue until
   clean": the session ends only when nothing foreign is left.
5. **Stops, progress reset to 0 and `ConversionBy` cleared:** D's dominance ends (its fleet leaves or an at-war rival warship arrives); nothing
   foreign is left; D is no longer at war with any player in `conversionWarMask`, the players it was at war with that held inhabitants at P at the session
start (peace, surrender or a truce:
   conversion stops regardless of fleet strength); the dominator changes.
6. **The flip.** `Planet.ConvertInhabitant(from, to)` (new; edits one `Inhabitant.Player`, never touches `Gameboard.Instance`, unlike
   `ChangePopulation`) then `SetPlanetOwnership`. If `Owner` changed: `CurrentProduction = null` and `ProductionQueue.Clear()`, no refund
   (so the conquest cannot hand over a ship the old owner paid for). Improvements, stocks, morale and Distribution Center bookkeeping are kept
   (the existing DC prune handles a lost DC).
7. **Hostility.** Each inhabitant converted away from a player D is NOT at war with adds `hostilityPerConversion` to that player's hostility
   toward D. Conversions of an at-war player's inhabitants add nothing (the war already drives it). Pattern: the engine step records the charge
   on `DiplomacyState` (like blockade cuts, never saved), the victim's `UpdateDiplomacy` charges it, so no decision reads another's mid-turn write.
   If the added hostility makes it declare war, its inhabitants become at-war ones through the existing stance logic; no special case.
8. **Reporting.** A new `UpdateResultType` appended LAST (the enum serializes as an int) for each conversion.

Tunables on `GameAIConstants` (in-code defaults, also written into `Assets/GameAIConstantsProductionTypes.asset`): `conversionTurnsBase` 6,
`hostilityPerConversion` 3 (against `hostilityPerCut` 5), `conversionColonizeWeight` 0.5. Starting points; tuned from the logs.

## The AI side
- **Conversion hold.** `AssaultPlanner.ConversionHolds()` next to `ContestedHolds()`: a planet is held when I have docked effective offense there,
  no at-war rival warship is docked (that is a contested hold already), and either an at-war holder still has inhabitants there or my session
  (`ConversionBy` is me) is active with foreign inhabitants left. The two lists are combined into `ShipTransportPlanner.HeldPlanets`, minus
  retreating planets (retreat still runs first and wins). Held ships are never stranded sources; a held colony is sink-only.
- **Assault target:** no change to ranking or sizing. The existing enemy-occupied rule (war filter) already picks and sticks to a planet holding an
  at-war player's inhabitants; the hold keeps the fleet, including through the non-war tail.
- **Colonization (the speed-up).** `PlayerAI.IsValidColonizationTarget` accepts a planet where I dominate, I have at least one inhabitant, it is below
  max population and a foreign inhabitant is still present. `IsPopulationTransferInProgress` still blocks duplicate launches; the colonist
  arrival rule already handles a planet that filled in transit. `PlanetHasColonizationTarget` shares the guard, so colony ship production sees
  these planets. Only under Dominance, so the AI does not colonize its own healthy planets.
- **Colonization priority (the owner's inversion).** A dominated target's choice cost is divided by
  `1 + conversionColonizeWeight x (1 - p)`, `p` being my share of the planet's inhabitants: a colonist is worth most early in a conversion
  (it needs no conversion and raises `p` for every later flip), least on a planet that is nearly mine. It multiplies the chokepoint divisor
  in `PlayerAI.ColonizationCostDivisor` (which `ColonistRedirect` also reads), applies to every strategy (it is 1 off a dominated planet), and the
  order's delay still uses the real route cost. `conversionColonizeWeight` default 0.5; 0 or below switches it off. The colony ship production
  weight (`GetIndustrySituationalWeightMultiplier`) is NOT changed; whether it should be is on the reevaluation list.
- **`ConversionHoldSpare` audit (log only; the evidence for reevaluating option 3 below).** Each turn a planet is on conversion hold, the planners
  run a second time without that hold (pure, no orders emitted) and record how many held ships a "call" would have taken. A call is a garrison
  deficit at another of my planets, an assault target's needed offense or a blockade target's needed offense (retreat is not a call; it runs first).
  Alongside: held offense, the largest at-war rival's visible offense within one hop, and the conversion progress.

## Saves
`GameSave.PlanetSave` gains `conversionProgress` (float), `conversionBy` stored as player id + 1 (0 = no session: `JsonUtility` loads a
missing field as 0, which a plain id would read as player 0) and `conversionWarMask` (int bitmask of the players D was at war with and that
held inhabitants at the session start; step 5's "war ended" test reads it). An older save loads with no session. Not saved: the hostility charges waiting on
`DiplomacyState`, the audit and the log trackers.

## Edge case: a player loses its last planet
Nothing eliminates players today and this design does not add it. A player with no planets keeps any docked ships. A `PlayerOutOfPlanets` log
line (once per player, through a tracker) shows how often it happens before elimination is designed.

## Tuning log (in the existing `T<turn>|P<playerId>|<EventCode>|<fields...>` format)
| Line | When | Question |
|---|---|---|
| `ConversionStart\|<planet>\|<fromPlayers>\|<myPop>\|<totalPop>\|<turnsPerFlip>` | session start, `ConversionTracker` (log-only state; a load logs each current session once more) | how often and where conversions begin |
| `Convert\|<planet>\|<fromPlayer>\|<AtWar or NonWar>\|<myPop>\|<totalPop>\|<progress>` | per converted inhabitant, no tracker | the real pace and the snowball |
| `ConversionEnd\|<planet>\|<Clean, DominanceLost or WarEnded>\|<turnsHeld>\|<converted>` | session end, tracker | why sessions stop (the "continue until clean" reevaluation) |
| `OwnerChanged\|<planet>\|<oldOwner>\|<newOwner>\|<clearedItem or ->` | each ownership change | what conquest costs the old owner |
| `ConversionHoldSpare\|<planet>\|<heldShips>\|<heldOffense>\|<shipsACallWanted>\|<Garrison, Assault or Blockade>\|<callTarget>\|<rivalOffenseNearby>\|<progress>` | on state change, tracker | whether option 3 is worth building |
| `ConversionColonize\|<origin>-><target>\|<myPop>\|<totalPop>\|<routeCost>\|<nearestTarget>\|<nearestCost>` | per colonist launched at a dominated planet, no tracker | whether the colony-ship speed-up is used, and whether the tilt sent a colonist past a nearer target (like `ChokepointColonize`) |
| `PlayerOutOfPlanets\|<player>` | once per player, tracker | how often elimination would matter |
| `Stance` and `Hostility` lines gain a trailing `conversionTerm` | existing cadence | how much hostility conversions cause in non-war players |

The `tuning-log` skill (`.claude/skills/tuning-log/SKILL.md`) and the "AI Tuning Log" section of `CLAUDE.md` get the matching updates.

## Tests
New `Assets/Editor/ConversionSelfCheck.cs` (`FlatSpace -> AI -> Run Conversion Self-Check`, `public static bool RunChecks()`, added to
`AllAISelfChecks`), built from `GameAIMap`/`Planet`/`PlayerAI` directly, never `Gameboard.Instance` (give planets distinct positions): the formula,
the floor and the carry; each Dominance condition (a truce, a rival warship, zero offense, the tie-break between two non-war dominators); victim
order and the non-war tail; every session reset; production cleared on an ownership change only; hostility charged to the non-war player and
never the at-war one; `ConversionHolds`; the colonization case; the audit count; a save round trip with and without the new fields. A
`SimultaneitySelfCheck` case runs a conversion forward and reversed. Never put a case exactly on a float boundary.

## Docs
A new "Invasion (conversion)" section in `CLAUDE.md` (and the "Out of scope" line under Ship combat updated); `FUTURE_FEATURES.md` element (3)
updated with the reevaluation list below; the `tuning-log` skill.

## Reevaluate after the first tuning pass (owner's list)
1. **Option 3:** conversion speed scaled by docked offense (use the `ConversionHoldSpare` audit and the `Convert` pace).
2. **Continue until clean versus stop at ownership.**
3. A minimum force for Dominance (today any one ship with effective offense above 0 dominates; the slow start is the only brake).
4. Elimination of a player that loses its last planet.
5. `conversionTurnsBase`, `hostilityPerConversion` and `conversionColonizeWeight`.
6. A colony ship production boost for dominated planets (the `ColonyShip` situational weight is unchanged today).

## Out of scope
Troop ships; planetary defense (element 4: reinforcing or retreating from a planet under conversion, force-based resistance);
the AI blockading on purpose (5); bombardment; morale effects of conquest; elimination; conversion by ships in flight.
