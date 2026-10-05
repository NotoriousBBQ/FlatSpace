# Ship to ship combat: design

Date: 2026-10-05. Path: architectural. `ship_combat` milestone, element (2) in `FUTURE_FEATURES.md`. Session rules: `docs/session_configuration.md`.
Follows the diplomacy spec (`2026-10-04-diplomacy-design.md`) and the diplomacy-as-orders spec (`2026-10-05-diplomacy-orders-simultaneity-design.md`,
merged `7b88aa4`: stance changes are orders, player decisions are order-independent). The brainstorm record, with every answer the owner gave, is
`2026-10-05-ship-combat-decisions-so-far.md`; this spec is the design that came out of it.

## Purpose

Warships stop being only a blockade and garrison force: warships of players at war that share a planet fight, ships take persistent damage and can be
destroyed, and the rest of the AI reacts through numbers it already reads (fleet strength, blockade value, wanted fleet, hostility). Combat also gives
diplomacy two things it lacked: a loss term in hostility and a way for a losing side to end a war (surrender).

Success: at war, docked warships at a shared planet damage and destroy each other deterministically; damaged ships deal less, heal slowly at home, and carry
their damage when they fly; every existing reader of a ship's offense sees effective (damaged) offense; hostility moves with losses; a heavily beaten
player can surrender, which ends the war for both sides for a set time; and the tuning log shows all of it.

## Out of scope (recorded, not built)

Retreat and reinforcement tactics (the rout from damaged ships is handled when retreat is designed, elements 4 and 5); planetary invasion (3); planet
defense (4); the AI moving warships to blockade on purpose (5); combat for ships in flight; any randomness; an offense floor (the owner chose none);
rotating the start player in `ProcessNewOrders` (recorded follow-up in `FUTURE_FEATURES.md`). Surrender terms are shipped at plain defaults and revisited
with the diplomacy tuning (memory note `surrender-terms-revisit-with-diplomacy`).

## Decisions (owner's answers, 2026-10-05)

1. Docked-only: combat happens at a planet between docked warships; ships in flight cannot fight.
2. Persistent per-ship damage; a ship dies at 0 health.
3. Deterministic, proportional defense: `damage = offense x K / (K + Defense)`.
4. Focus fire, most damaged ship first, overflow carries on, both sides fire simultaneously from the start-of-turn state.
5. With several war rivals present, a side's damage is split in proportion to its hostility toward each (floor of 1).
6. Gradual repair at home.
7. Resolution plus the wiring that has to follow; no new tactics.
8. A damaged ship deals proportionally less (no floor).
9. A player's colony ships at a planet die when it has no docked warships there and a war rival has docked warships there, everywhere (own colonies included).
10. Hostility gets a loss term (1 per ship lost) and a drop while losses are a significant share; surrender is a third stance choice that is an order and ends the war.
11. Structure: a pure `CombatSystem` run once per turn in `GameAIUpdate`, appending `PlanetUpdateResult`s; surrender is an order; damage travels with a ship in flight.

## Section 1: ship state and stats

- `Ship` gains `public float Damage` (damage taken, 0 = full health). Damage is stored, not current health, so an older save loads at full health with no
  migration, and the maximum (the ship's Health stat, from its research snapshot) is fixed per ship. Current health = `max(0, HealthStat - Damage)`.
- `WarshipStats` gains `EffectiveOffense(template, snapshot, damage)` = `Offense x currentHealth / HealthStat` (no floor; 0 when the Health stat is 0).
  Pure. `WarshipStats.CurrentHealth(template, snapshot, damage)` is the helper. The existing `Offense`, `Health`, `Defense` are unchanged.
- Every reader that sums or tests a ship's offense switches to the effective value: `AssaultPlanner` (the three `_stats.Offense` uses), `FleetStrength`
  (effective offense x (current health + Defense)), `BlockadeSystem` (docked offense), and `GameAIMap` (in-flight offense from the fleet payload).
  `FleetUIController` shows current health. Nothing else changes: blockade values, assault sizing, hostility strength, `WantedWarships` and the fleet cap
  therefore reflect damage and losses on their own.
- `GameAIOrder.ShipFleetPayload` gets a parallel `Damage` list (one float per snapshot). `ApplyShipDeparture` fills it from the departing ships,
  `ApplyShipArrival` docks each ship with its damage, and ships in flight do not repair. `ShipSave` gains `damage`; `PlanetSave.dockedShips` and
  `OrderSave.fleetShips` reuse it, so no other save type changes. A fleet with a missing or shorter `Damage` list (older saves) docks at full health.

## Section 2: the combat step

`CombatSystem` (`Assets/Flatspace/GameAI/CombatSystem.cs`, namespace `FlatSpace.AI`, pure: no `Gameboard.Instance`) exposes one entry point,
`Resolve(GameAIMap map, WarshipStats stats, GameAIConstants constants, int turn, List<Planet.PlanetUpdateResult> results)`, which mutates ship damage,
removes destroyed ships and appends results, like `UpdateAllPlanets`. It is called from `GameAI.GameAIUpdate()` after `ProcessCurrentOrders()` (arrivals
have docked) and after the `planetUpdateResults.Clear()`, before `UpdateAllPlanets`, so this turn's results reach `ProcessResults`.

`Resolve` does nothing while `map.Diplomacy.Enabled` is false (legacy mode: no stances, no war to fight).

For each planet with docked warships of two or more players:

1. **Who fights whom.** A fights B when A is at war with B: `map.Diplomacy.IsAtWar(A, B, true)`. Docked together counts as contact (a ship at a
   planet makes it known, but `PlayerKnowledge` only updates later in the turn). A peaceful pair is left alone, and a pair under a truce (Section 3) is
   not at war.
2. **Start-of-turn snapshot.** Every warship's effective offense, current health, Defense and dock index are read once, before any damage is applied.
3. **Offense pools.** A side's pool is the sum of its warships' effective offense. It is split among its war rivals present in proportion to
   `max(1, hostility toward that rival)`; a rival that is not at war with it gets no share.
4. **One pool per victim.** For each victim player V, the shares aimed at V by all attackers are summed into a single pool (so the result does not depend
   on the order the attackers are processed), and V's ships are ordered by lowest current-health fraction first, ties by dock index.
5. **Focus fire.** For the current target ship with current health `h` and Defense `d`, the factor is `f = K / (K + d)` (`K` = `combatDamageK`).
   If `pool x f >= h` the ship is destroyed and `h / f` of the pool is spent; otherwise the ship takes `pool x f` damage and the pool is spent. The pool
   carries to the next ship until it runs out. A ship already at 0 effective health is not targeted.
6. **Apply.** Damage and removals are applied after every side has been computed (simultaneous). A destroyed ship is removed from `Planet.DockedShips`.
7. **Attribution.** The strength and ship count a victim lost are attributed to each attacker in proportion to its share of that victim's pool.
   One `PlanetUpdateResultTypeWarshipsLost` result per planet, victim and attacker (`Name` = planet, `PlayerID` = the victim, `Data` = a `CombatLoss`
   with `Attacker`, `Ships` (float, the attributed share of the ships destroyed) and `StrengthLost` (the same share of the destroyed ships' strength,
   offense x (health + Defense) at the start of the turn)) is appended whenever the attributed ships are above 0.
8. **Repair** (after combat, same call): a damaged warship heals `repairFractionPerTurn` of its Health stat (damage never below 0) when it is docked at
   a planet its owner populates (`planet.Owner == owner && planet.Population.Count > 0`) and no docked warship of a player the owner is at war with is
   present there.
9. **Colony ships** (after combat and repair): for each planet and player P with docked colony ships, if P has no docked warship left there and some
   player at war with P has a docked warship there, all of P's docked colony ships there are destroyed, with one
   `PlanetUpdateResultTypeColonyShipsLost` (`Name` = planet, `PlayerID` = P, `Data` = a `ColonyLoss` with `Count` and `ByPlayer` (the lowest-numbered such
   rival)). The rule runs on every planet, a planet P populates included.

Colony ships take no part in combat otherwise (Health, Offense and Defense are all 0); ownerless ships are never an attacker or a victim.
`PlanetUpdateResultType` gets `WarshipsLost` and `ColonyShipsLost` appended last (the enum serializes as an int; `CombatLoss` and `ColonyLoss` are
plain classes carried in `Data`). New tunables on `GameAIConstants` with in-code defaults: `combatDamageK` 20, `repairFractionPerTurn` 0.10.

## Section 3: the AI side

- **Reading losses.** `PlayerAI.ProcessResults` reads the `WarshipsLost` results with `PlayerID == me` (the shared list is passed to every player) and
  keeps a private per-rival history of `(turn, ships, strengthLost)`, pruned to `lossWindowTurns` (10). The history is player-private and not saved:
  a load starts the window empty (like the pending blockade cuts).
- **Loss share.** `lostInWindow / (myCurrentStrength + lostInWindow)`, using strength lost summed over the window; 0 when both are 0, 1 when my current
  strength is 0 and something was lost.
- **Hostility.** `HostilityCalculator.Inputs` gains `ShipsLost` (this turn's ships lost to that rival) and `LossShare`. Each ship lost adds
  `hostilityPerShipLost` (1); while `LossShare >= significantLossFraction` (0.3) the score also falls by `significantLossHostilityDrop` (4) each turn;
  the usual clamp to 0..`hostilityMax` applies after. Kills add nothing. Existing terms (cuts, near ships, strength, decay) are unchanged.
- **Surrender is a third stance choice.** `StanceMatrix` offers Surrender for a rival only while I am at war with it (my own War, or a forced war) and my
  visible strength is below its; Peace and War keep their current weights, hold and stickiness. Surrender's weight is a logistic of the loss share centered
  at `surrenderMidpoint` (0.6) with steepness `surrenderSteepness` (0.1) and takes no stickiness or hold. A chosen Surrender emits `OrderTypeSurrender`
  (appended last; immediate; `Data` = the rival's id; empty `Origin` and `Target`), not a stance write.
- **Executing a surrender.** `GameAI.ExecuteOrder` calls a new pure `GameAI.ApplySurrender(DiplomacyState, GameAIOrder, int turn)`: both players' stances
  toward each other go to Peace (stamping the change turn) and both pair rows get `TruceUntil = turn + surrenderTruceTurns` (30). While
  `turn < TruceUntil` for the pair: `ApplyStanceOrder` refuses a Declare War, `DiplomacyState.IsAtWar`/`WarRivals` treat the pair as not at war (so a rival's
  earlier declaration cannot force a war), and `StanceMatrix` does not offer War. Make Peace still works. `DiplomacyState.Pair` gains `TruceUntil`.
- **What reacts without new rules.** Destroyed ships leave `FleetStrength`, `WantedWarships`, blockade values and fleet-cap counts, so the strength term,
  production (through the existing fleet-shortfall multiplier) and blockade breaking respond. No retreat, reinforcement or defense logic is added.
- **Order independence.** Combat runs in `GameAIUpdate`, outside the per-player loop; the loss history and Surrender decision are player-private and the
  surrender is an order, so `SimultaneitySelfCheck` stays true (and is extended with a fight).

New tunables on `GameAIConstants`, in-code defaults: `hostilityPerShipLost` 1, `lossWindowTurns` 10, `significantLossFraction` 0.3,
`significantLossHostilityDrop` 4, `surrenderTruceTurns` 30, `surrenderMidpoint` 0.6, `surrenderSteepness` 0.1. Note (gotcha from the diplomacy work): a new
default does not reach an already-loaded constants asset instance; write the keys into `Assets/GameAIConstantsProductionTypes.asset` if a run shows the old value.

## Section 4: saves, logging, self-checks, docs

- **Saves.** `ShipSave.damage`; `StanceSave.truceUntil`. Older saves load at full health and with no truce. The loss history, the combat results and the
  repair state are derived or transient.
- **Tuning log** (all `T<turn>|P<playerId>|<EventCode>|<fields>`):
  - `Combat|<planet>|<attacker>-><victim>|<damageDealt>|<shipsDestroyed>` per planet and directed pair each turn combat happens (an event; a standoff
    repeats each turn). Logged by `GameAI` from the results and the `Resolve` return. Answers: how long fights run, how lethal they are.
  - `ColonyShipsLost|<planet>|<owner>|<count>|<byPlayer>` per event. Answers: what the colony-ship rule costs expansion.
  - `LossDrop|<rival>|<Start or End>|<lossShare>` when the share crosses `significantLossFraction`, through a log-only set in `PlayerAI` (a load logs each
    current one once more). Answers: how often the demoralization term fires.
  - `Surrender|<rival>|<lossShare>|<pSurrender>|<truceUntil>` at execution. Answers: how the surrender weight behaves (the lever to revisit).
  - `FleetHealth|<warships>|<damaged>|<meanHealthPct>` every 25 turns per player, beside `Economy`. Answers: whether repair keeps pace with combat.
  - `Stance` and `Hostility` keep their formats. The plan carries the matching `tuning-log` skill and `CLAUDE.md` updates.
- **Self-checks.** New `Assets/Editor/CombatSelfCheck.cs` (registered in Run All): effective offense (no floor, zero Health stat), focus-fire ordering and
  overflow, the proportional reduction, the hostility-weighted split across rivals, simultaneity and attacker-order independence (one pool per victim),
  attribution, repair and its conditions, the colony-ship rule (including a populated planet and a peaceful rival), peace leaving ships alone, ownerless
  ships ignored, damage riding in the fleet payload, and the save round trips for `damage` and `truceUntil`. `DiplomacySelfCheck` gains the loss terms,
  the Surrender choice, `ApplySurrender` and the truce. `SimultaneitySelfCheck` gains a fight.
- **Docs.** `CLAUDE.md` (new "Ship combat" section; Orders; Diplomacy; self-check list), `FUTURE_FEATURES.md`, the `tuning-log` skill.
- **Tuning and testing pass** after the build, comparing with the diplomacy-orders runs on `test2.json` and `4p.json` (what changes: fleet sizes, war length,
  Amass share, warship production, blockade counts; fleet-cap discipline must still hold).

## Review focus (conditions the tests must pin)

- A planet with three players' fleets: one pool per victim, hostility-weighted shares, no dependence on player order.
- A ship destroyed the turn it arrives; a fleet that leaves wounded and arrives wounded (the `Damage` list survives departure, flight, save and load).
- Division by zero: a Health stat of 0 (colony ships), a victim pool of 0, my strength 0 in the loss share.
- A truce: a Declare War order refused, a forced war not counted, Surrender not offered while at peace or when winning.
- Colony ships on a planet where the owner has warships that die the same turn (the rule runs after combat), and where an at-war rival arrives alone.
- Legacy mode (`Diplomacy.Enabled` false, including every `GameAIMap` a self-check builds directly): combat is OFF (`Resolve` returns at once), so no stance exists to fight on and older logs stay comparable; the loss terms, Surrender and repair-by-war-presence never run either. `CombatSelfCheck` switches diplomacy on explicitly, like `DiplomacySelfCheck`.
