# Ship combat (ship_combat element 2): decisions so far

Brainstorm notes from 2026-10-05, paused so the diplomacy-as-orders retrofit, simultaneity and a tuning/testing pass come first.
This is NOT the spec. It records the owner's answers so the combat brainstorm can resume without re-asking.

## Decided
1. **Where:** docked-only. Combat happens at a planet, once per turn, between docked warships of players at effective war. At peace ships coexist and blockades work as today.
2. **Damage state:** persistent per-ship damage (a saved current-health field on `Ship`); a ship dies at 0 health.
3. **Damage formula:** proportional, `damage per hit = Offense x K / (K + Defense)`, K a tunable (example 20). Deterministic, no randomness.
4. **Fire distribution:** focus fire. A side's damage goes to enemy ships one at a time, most damaged first (ties by arrival order), overflow carries on. Both sides fire simultaneously from the start-of-turn state.
5. **Several war rivals at one planet:** a side's damage is split among the war rivals present in proportion to its hostility toward each, floor of 1 so a forced war at hostility 0 still gets a share; focus fire within each rival's ships.
6. **Repair:** gradual. A damaged ship regains a tunable fraction of max health per turn (example 10%) while docked at a planet its owner populates with no at-war enemy warships there.
7. **AI scope:** resolution plus the wiring that has to follow (fleet strength and hostility use current health, blockade value and fleet counts drop destroyed ships, logging). No new tactics (retreat, reinforcement, defense belong to elements 4 and 5).
8. **Damaged ships deal less:** effective Offense = Offense x (current health / the ship's Health stat). Consequences: blockade value and fleet strength use the effective value; the damage feedback loop needs a tuning lever (a minimum-effective-offense floor is the obvious one; raise it in the design).
9. **Colony ships:** destroyed when their owner's docked warships at that planet are all gone and an enemy at war has warships docked there. Pin the exact condition in the design (a lone colony ship when an enemy arrives; the planet's own colony). Needs a log line.
10. **Hostility:** a loss term (`hostilityPerShipLost`, about 2 per ship of mine a rival destroys; kills add nothing). Plus a drop for significant proportional losses: the share of my fleet strength lost to a rival over a recent window (example 10 turns) above a threshold (example 30%) lowers hostility toward that rival by a tunable amount per turn. Not "versus enemy strength" (the existing strength term already covers that once destroyed ships leave `FleetStrength`).
11. **Surrender:** a third choice in `StanceMatrix` beside Peace and War, driven by a "surrender terms" weight. A surrender ends the war for both sides (both stances to Peace) and locks the pair against new declarations and forced wars for a tunable number of turns. Surrender is an ORDER. Ship the terms at plain defaults; revisit them with the diplomacy tuning (memory note `surrender-terms-revisit-with-diplomacy`).
12. **Structure:** a new pure `CombatSystem` (no `Gameboard.Instance`), run once per turn in `GameAI.GameAIUpdate()` after arrivals and before `UpdateAllPlanets`, appending new `PlanetUpdateResult` types (warship damaged, warship destroyed, colony ship destroyed, each with owner and attacker, appended last in the enum) to the shared list. `PlayerAI` reads them (filtering by `PlayerID`) for the loss terms and the surrender decision. Repair runs after combat. Own `CombatSelfCheck` in Run All AI Self-Checks.

## Decided after the diplomacy-orders work merged (2026-10-05, `7b88aa4`)
13. **No offense floor:** effective Offense is purely proportional to current health (Offense x current health / the ship's Health stat). The rout is handled when retreat logic is designed (elements 4 and 5).
14. **Colony ships die everywhere:** a player's docked colony ships at a planet are destroyed at the end of the combat step when that player has no docked warships left there and a war rival has docked warships there, including on a planet the owner populates. Log it.
15. **Hostility defaults (GameAIConstants):** `hostilityPerShipLost` 1 (the owner changed it from 2), `lossWindowTurns` 10, `significantLossFraction` 0.3, `significantLossHostilityDrop` 4 per turn while significant.
16. **Surrender defaults:** `surrenderTruceTurns` 30 (the pair cannot declare on each other and a rival's declaration cannot force a war between them); surrender weight = logistic of my loss share, `surrenderMidpoint` 0.6, `surrenderSteepness` 0.1; offered only while I am at war with that rival and my strength is below its; Peace stays as today.
17. **Damage travels with the ship:** `ShipFleetPayload` gets a parallel per-ship list (saved with the order through `ShipSave`); arrival docks each ship with the damage it left with; no repair in flight.

## Sequence the owner set (2026-10-05)
1. Retrofit diplomacy to the order pattern (stance changes as orders: Declare War, Make Peace, Surrender, executed in `ProcessNewOrders`).
2. Simultaneity of `ProcessResults` across players.
3. A tuning/testing pass.
4. Then combat, from the decisions above (resume the brainstorm; remaining design points: effective-offense floor, exact colony-ship rule, window and threshold defaults, the `PlanetUpdateResult` fields, saves, log lines, self-check list).
