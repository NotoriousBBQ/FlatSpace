# Retreat and the engaged-force loss share: design

Date: 2026-10-06. Status: draft for the owner's review (brainstorm answers recorded below). Works from `FUTURE_FEATURES.md`
under the ship combat milestone, element (2) "Still open" (the surrender/loss-share measure and the retreat logic, both deferred
there to "retreat and defense") and the handoff `HANDOFF-ship-combat-signoff-2026-10-05.md`. Builds on
`2026-10-05-ship-combat-design.md` and `2026-10-05-diplomacy-orders-simultaneity-design.md`.

## Purpose

1. A player whose warships are losing a fight at a planet should be able to pull them out, so a lost fight does not cost the whole
   group. The damaged-ship rout was left unhandled on purpose (no offense floor); this is that handling.
2. The loss share that drives the hostility loss drop and surrender is measured against the force actually engaged at the fight
   planets, not against the whole fleet. Today it reaches only 0.02-0.14 because fights are single-planet skirmishes, so surrender
   and the loss drop are dormant on `test2.json`.

Success: retreat fires on lost fights and preserves ships; the loss share is a meaningful fraction of the engaged force, so
surrender and the loss drop can fire on real defeats on both boards (after the calibration step below); economy and war counts stay
in the current ranges; no cross-player dependency is introduced (the simultaneity self-check still passes).

## Owner decisions from the brainstorm (all 2026-10-06)

- Retreat decides **per fight planet**, and **ship defense is part of the calculation**.
- Losing is judged by **projecting the fight to its end**, recomputed whenever forces change; my **own in-flight ships landing at
  that planet** count, rival in-flight ships do not.
- A **deterministic gate** decides when retreat is considered: the rival survives the projection **and** I would lose at least
  `retreatCheckFraction` of my strength there (if I win I stay however costly it is). Past the gate, **retreat or stay is a
  weighted `ScoreMatrix` decision** (a roulette, heavily weighted by the projected loss), so it is not fully deterministic. The
  destination stays deterministic (best tier, then cost).
- Destination is tiered (below), the first tier with a valid planet wins, cheapest path within a tier, **blockade-aware: only
  blockades by players I am at war with count**, for the destination and for the route.
- Retreat runs **first** and wins over the assault, holds and garrison planners; a retreated-from planet is off the assault
  target list for `retreatCooldownTurns`.
- At a planet I **populate**, retreat only when the projection shows my group wiped out.
- The loss share becomes **window total lost / window total engaged** per rival.
- Implementation pattern: **the existing `UpdateResult` -> ScoreMatrix -> `GameAIOrder` flow**, new matrix types acceptable;
  `PlanetUpdateResult` is renamed `UpdateResult`.

## Architecture: the existing flow, extended

Every AI decision today is: an engine step appends results to the shared list, each player's `ProcessResults` turns them into
scored choices, and the result is `GameAIOrder`s executed by `ProcessNewOrders`. Retreat follows it exactly:

1. **Engine step (new), once a turn, in `GameAI.GameAIUpdate` right after `CombatSystem.Resolve`/repair and before
   `UpdateAllPlanets`:** `FightProjector` (pure) appends one `UpdateResultTypeFightProjection` per (planet, player) where that
   player has docked warships and a player it is at war with also has some (`Diplomacy.IsAtWar(a, b, true)`, the combat rule).
   `PlayerID` = the player, `Data` = `FightProjection`. It reads only the post-combat committed state, so every player decides
   against the same projection (simultaneity preserved).
2. **Decision (new), in `PlayerAI.ProcessResults`, before the existing ship planning:** `RetreatPlanner` reads this player's
   `FightProjection` results, applies the deterministic gate, finds each gated group's best destination, and lets a
   `ScoreMatrix` (the stance-matrix pattern) weigh Stay against Retreat for each.
3. **Orders:** a retreat is the existing ship-transport trio (`OrderTypeShipTransport`, `OrderTypeShipDeparture`,
   `OrderTypeShipTransferInProgress`) emitted through `PlayerAI.EmitShipOrders`. **No new `OrderType`.** Ships leave in
   `ProcessNewOrders` of the same turn, so they do not fight on the next turn's combat step.

### The decision: a deterministic gate, then a weighted matrix (the stance-matrix pattern)
The gate is a plain test (below). Past it, retreat or stay is a choice between two candidates with a weight each, which is what
`ScoreMatrix` is for, and the owner wants it heavily weighted but not fixed.

### The matrix
- New `RetreatMatrix.cs` beside `ShipMatrix.cs` and `StanceMatrix.cs`: `ScoreMatrix<RetreatDecisionElement, RetreatChoiceElement,
  ShipAction>`. `RetreatDecisionElement` is one fight planet (planet, group size, projected loss fraction; the row priority is a unique
  rank, most endangered first, as `ShipTransportPlanner` does). `RetreatChoiceElement` is `Stay` or `Retreat` and, for Retreat, carries
  the destination (`TargetPlanet`, `Category` = tier 1..3, `PathCost`). The action is the existing `ShipAction`.
- **Two choices per row:** `Stay` and `Retreat` to the **best destination** (deterministic: lowest tier, then path cost, then planet
  name, the `ShipTransportPlanner` `TargetRank` convention). A row with no valid destination offers `Stay` only (so it never leaves).
- **Weights (like `StanceMatrix.SurrenderWeight`):** `pRetreat = 1 / (1 + e^(-(projectedLossFraction - retreatLossFraction) /
  retreatSteepness))`; `Retreat` weight = `pRetreat`, `Stay` weight = `1 - pRetreat`. Neither is exactly 0 for a gated row, so the
  all-zero uniform pick (`ScoreMatrix.WeightedPick`) cannot occur; `OfferedChoices`-style filtering of non-positive weights applies
  anyway. `GenerateActionList` is called with a `weightSelector`, i.e. a roulette pick over the two weights (the same call the
  stance and production matrices make). A `Stay` pick emits no action.
- **`IndependentRows = true`** (the existing flag): rows are independent decisions, and a destination chosen by one row is not removed
  from the others, because two groups retreating to the same safe planet is fine.
- **The shared random stream:** the pick draws from `GameAI.Rand`, so a player's draws depend on how many came before (existing
  accepted behavior for the stance and production rolls). `SimultaneitySelfCheck` already re-seeds `GameAI.Rand` per player and
  must cover the retreat roll too. Retreat self-checks make the roll deterministic with very steep curves, as the surrender checks do.
- Combining with the garrison matrix was considered and rejected: `ShipTransportPlanner`'s matrix is claim-once-per-target with
  deficit-capped counts, the opposite of what a retreat needs. The two share types and `EmitShipOrders`, not a matrix instance.
- **Combining with the garrison matrix was considered and rejected:** `ShipTransportPlanner`'s matrix is claim-once-per-target
  with deficit-capped counts, the opposite of what a retreat needs. The two share types and `EmitShipOrders`; they do not share a
  matrix instance.

## Components

### 1. `CombatSystem` core extraction (reuse, no second formula)
`CombatSystem.Resolve` today computes a planet's fight inside the loop over live `Planet` objects. The per-planet core (start-of-turn
snapshot -> pools by hostility -> one pool per victim -> focus fire with `K / (K + Defense)` -> damage and destroyed list) moves
into a pure method over a list of value-type ship states (`owner`, `effective offense`, `current health`, `defense`, `dock
index`, `strength`) returning damage and destroyed sets plus the per-attacker `CombatReport`/loss figures. `Resolve` builds the states
from the planet, calls the core and applies the outcome exactly as before. `FightProjector` calls the same core on copies. The
existing combat self-checks must pass unchanged (a refactor, no behavior change). **If the core cannot be extracted cleanly the
work stops and comes back to the owner** (session rule: no parallel implementation without asking).

### 2. `FightProjector` (pure, `Assets/Flatspace/GameAI/FightProjector.cs`)
Input: the planet, the committed stances/hostility (`DiplomacyState`), `WarshipStats`, the in-flight ship orders (their fleet
payloads carry damage and snapshots, arrival turn = `TimingDelay`), and constants. For one player's group at one planet it:
- builds states for my docked warships, the war rivals' docked warships there, and my own in-flight ships on their arrival turns
  (a ship landing with `TimingDelay` d joins before projected turn d; a fleet whose order is a retreat away does not count, it
  is not heading here);
- repeats the combat core turn by turn (damage persists; no repair, which does not happen while an at-war warship is present;
  rival in-flight ships and rival repair are not modeled) until either side has no ships or `retreatProjectionTurns` turns have run;
- returns `FightProjection { Planet, MyShips, MyStrength, MyStrengthLost, RivalSurvives, RivalIds, TurnsProjected }` so
  `ProjectedLossFraction = MyStrengthLost / MyStrength`. Strength is the same per-ship strength `CombatLoss` already uses.
Cost: only planets with an actual fight, at most `retreatProjectionTurns` rounds each.

### 3. `RetreatPlanner` (pure, `Assets/Flatspace/GameAI/RetreatPlanner.cs`, no `Gameboard.Instance`)
Per projection result for my player:
- **Gate (deterministic):** a fight planet becomes a matrix row only when `RivalSurvives && ProjectedLossFraction >=
  retreatCheckFraction`. At a planet I populate (`Planet.Owner == me`) the gate is instead "my group is wiped out"
  (`ProjectedLossFraction >= 1`). Below the gate the ships stay and nothing is rolled.
- **Destination tiers** (first tier with at least one valid planet; cheapest path within a tier, ties by name). A planet must be
  known to me (`PlayerKnowledge`), reachable (`2 <= NumNodes <= maxPathNodesForShipTransport`, never `FindPath`'s 1-node stub) and
  not the fight planet:
  1. a planet I populate with no warship of a war rival docked and not blockaded by a war rival (repair works here);
  2. a known uncolonized planet, or one populated by a player I am not at war with, with no war-rival warship docked and not
     blockaded by a war rival (no repair there: repair needs a planet its owner populates);
  3. a known planet populated by a player I am at war with whose **remembered** blockade value (`BlockadeView`, may be stale by
     arrival, accepted) is below the group's **total effective offense / (1 + `blockadeBreakMargin`)**, i.e. offense above
     `value x (1 + blockadeBreakMargin)`, the same unit and margin blockade breaking uses. The destination itself may be blockaded.
  4. none: the row offers `Stay` only, so the group stays and fights (`RetreatHeld`, reason `NoDestination`).
- **Blockade filtering:** a `BlockadeView` filtered to my war set (`WarRivals()`): a planet counts as blockaded for the tiers and the
  route only when `BlockadeView.Blocker(planet)` is a war rival. A remembered blockade with an unknown blocker (`Blocker` = -1) is
  ignored. Routes go through `RoutePlanner.PlanRoute` with that filtered view (it already detours around blockaded planets and
  returns null when every way is blocked, which drops the candidate). Warships in flight are not cut by blockades; the route check
  is the owner's decision, not a game rule.
- **Output:** the matrix's `ShipAction`s (origin = the fight planet, target = destination, count = the whole group, cost = route
  cost) for the rows that picked Retreat, the set of retreating planets, and the cooldown entries. A row that picked Stay
  changes nothing (no cooldown, its ships stay available to the other planners).

### 4. Precedence inside `PlayerAI.PlanShipActions`
`RetreatPlanner` runs first. For every retreating planet:
- its ships are excluded from the assault force, from `ContestedHolds` and from the garrison planner's sources and sinks (a new
  `Retreating` set on `ShipTransportPlanner`/`AssaultPlanner`, like `HeldPlanets`; the retreat's `ShipAction`s are returned with the
  others so the existing `claimedByOrigin` bookkeeping in `ProcessShipActions` stays correct);
- the planet is not an assault or blockade-breaking target until `turn + retreatCooldownTurns` (`AssaultPlanner` gets the same kind
  of exclusion it already has for unreachable targets). The cooldown is player-private state in `PlayerAI`, **not saved** (a load
  forgets it, as it does the loss history).
Retreat applies in Expand too (the planner runs wherever there is a fight; Expand has no assault, so only the exclusion from the
garrison planner matters there). Colony ships do not retreat (out of scope; the existing colony-ship rule applies).

### 5. The engaged-force loss share
- `CombatLoss` gains `Engaged`: the victim's strength at that planet at the start of the turn (the same strength `StrengthLost`
  uses), split among attackers by each attacker's share of the victim's pool, exactly as `StrengthLost` is, so two attackers on one
  victim count its engaged force once in total.
- `CombatSystem` now appends the `WarshipsLost` result on **every fight turn** per (victim, attacker), with `Ships` and
  `StrengthLost` 0 when nothing died (today it only appends when `lostShips > 0`, `CombatSystem.cs:147`). Without it a standoff
  with damage and no kills would not enter the denominator. Check before changing it that the only readers of this result are
  `PlayerAI.RecordLosses` and the log.
- `PlayerAI.RecordLosses` keeps `(turn, ships, strength, engaged)`; `LossShareToward(rival, turn)` = window total `strength` / window
  total `engaged` (0 when engaged is 0), pruned to `lossWindowTurns`. The `myStrength` parameter goes; its two callers
  (`PlayerAI.cs:291`, `:348`) lose the argument. Everything downstream (loss-drop term, `StanceMatrix` surrender row, `PSurrender`,
  the `Hostility`/`Stance`/`Surrender` log fields) reads it unchanged.
- Constants (`significantLossFraction` 0.08, `surrenderMidpoint` 0.15, `surrenderSteepness` 0.015) were tuned to the diluted whole-fleet
  range (0.02-0.14) and will probably let surrender fire too early on the new measure. **They ship unchanged and are recalibrated
  from the first tuning logs** (a tuning step on a new measure, not a reopening of the owner's 2026-10-05 "stop retuning" call,
  which was about the old measure).
- A retreating group stops being engaged, so the share stops growing once it leaves; a lost fight reads as a high share.

**Amendment (owner's decision 2026-10-06, after the whole-branch review):** the numerator is the victim's **strength drop** per
round, not only the strength of ships destroyed. A ship worn down over several rounds dies with almost no strength left, so
counting only destroyed ships read about 0.004 for a wipe-out by attrition (1 ship against 3) while every round added its full
engaged strength to the denominator. `CombatLoss` gains `StrengthDrop` (start-of-round victim strength minus end-of-round
strength, destroyed ships counted in full, damaged ships by their lower health, split among attackers by pool share like the
rest) and `PlayerAI.RecordLosses` stores it as the numerator (about 0.5 for the same wipe-out). `StrengthLost` stays for the
ships-destroyed accounting. Everything else in this section (the denominator, the window, the unchanged constants and their
recalibration from logs) stands.

## Tunables (`GameAIConstants`, in-code defaults, also written into `Assets/GameAIConstantsProductionTypes.asset`)
`retreatCheckFraction` 0.3 (the gate), `retreatLossFraction` 0.5 (the curve's midpoint), `retreatSteepness` 0.1 (starting
point: a retreat weight of about 12% at the gate of 0.3, 50% at 0.5 and 88% at 0.7; tuned from logs), `retreatProjectionTurns` 20,
`retreatCooldownTurns` 10. Reused: `blockadeBreakMargin`,
`maxPathNodesForShipTransport`, `lossWindowTurns`, `combatDamageK`. Do not put a self-check exactly on the `retreatLossFraction`
boundary (the float caveat in `CLAUDE.md`).

## The `PlanetUpdateResult` -> `UpdateResult` rename
Owner's instruction, done first as its own mechanical commit with no behavior change. Scope (assumption, flag at review):
the nested class `Planet.PlanetUpdateResult` becomes `Planet.UpdateResult`, its nested enum `PlanetUpdateResultType` becomes
`UpdateResultType`, and each value `PlanetUpdateResultTypeX` becomes `UpdateResultTypeX`, so the stem is consistent. The `using
ResultType = ...` alias in `Planet.cs` is updated. 252 occurrences in 16 `.cs` files (`Planet.cs` 81, `PlayerAIResourceSelfCheck` 52,
`PlayerAI` 33, `BlockadeAvoidanceSelfCheck` 16, `DiplomacySelfCheck` 13, `DistributionCenterSelfCheck` 10, `PlayerKnowledgeSelfCheck` 9,
`CombatSystem` 7, `SimultaneitySelfCheck` 7, `AITuningLogger` 6, `CombatSelfCheck` 6, `GameAI` 4, `BlockadeBreakSelfCheck` 3, `GameAIMap`
2, `ResourceMatrix` 2, `Player` 1). Nothing serializes these (results are transient, never saved; no `.unity`/`.asset` mentions),
so no `FormerlySerializedAs` or asset migration is needed. It stays nested in `Planet` (moving it to a top-level type, which suits
results that no longer come only from planets, is a separate change). Use the Rider rename refactoring where it works, else a
careful mechanical replace; `CLAUDE.md` is updated, historical specs, plans and handoffs are not rewritten. The new
`UpdateResultTypeFightProjection` is appended last in the enum, like the combat results.

## Tuning log (session rule: every plan has this section)
Formats and why they cannot repeat every turn:
- `Retreat|<planet>|<destination>|<tier 1-3>|<ships>|<projectedLossPct>|<rivalSurvivorsPct>|<routeCost>|<cooldownUntil>|<pRetreat>`;
  tier 3 appends `|<rememberedBlockade>|<myOffense>`. One per retreat order (a retreat sends the ships, so it cannot repeat for the
  same group). Answers: does it fire, where do ships go, how often is each tier used, and what weight did the roll have (as
  `pSurrender` does for surrender).
- `RetreatStay|<planet>|<projectedLossPct>|<pRetreat>`: a gated fight that rolled Stay. On change only, through the same log-only
  per-planet tracker as `RetreatHeld` (Start when a planet enters the gate and stays, End when it leaves the gate or retreats), so a
  standoff does not log every turn. Answers: how often the roll keeps a group in a bad fight, for tuning the curve.
- `RetreatHeld|<planet>|<NoDestination or OwnPlanetNotWiped>|<projectedLossPct>`: a fight the projection says to leave, where the
  ships stay. On change only, through a log-only per-planet tracker in `PlayerAI` like `BlockadeSkipTracker` (not saved, so a load logs
  each current hold once more). Answers: are ships dying for lack of a destination.
- `RetreatArrive|<planet>|<rememberedBlockade>|<actualBlockade>`, tier 3 only, on the arrival turn, from a log-only pending map in
  `PlayerAI`. Answers: how stale the remembered value is. The one line with extra state; droppable.
- The 25-turn `Hostility` line gains a final `|<engagedStrength>` (older logs lack it: guard `NF`). Answers: is the new share in a
  sensible range, for the recalibration.
Also update the `tuning-log` skill and the "AI Tuning Log" and "Ship combat" sections of `CLAUDE.md`. Tracker/map classes are pure so
the self-check can cover them.

## Tests (`Assets/Editor`; no `Gameboard.Instance`)
New `RetreatSelfCheck.cs`, added to `AllAISelfChecks` (14 suites): the projection (a win, a wipe-out, inbound own reinforcements
changing the gate, a high-defense fleet an offense-only formula would misjudge); the gate (never on the exact boundary); the
retreat weight curve (about 50% at the midpoint, rising and falling either side, as the surrender curve is checked) and the roll made
deterministic with steep curves; a row with no destination offering Stay only; an own populated planet gated only on a wipe-out;
every destination tier; blockades counted only from war rivals and
a remembered blocker of -1 ignored; the tier 3 offense-and-margin rule; no destination keeps the ships; two groups choosing one
destination (`IndependentRows`); the cooldown excluding the assault target; retreating ships excluded from garrison and holds;
tracker transitions. Existing suites: `CombatSelfCheck` (extraction leaves `Resolve` unchanged; engaged shares sum to the victim's
strength; a zero-loss result on a fight turn), `DiplomacySelfCheck` (loss-share cases move to the new measure),
`SimultaneitySelfCheck` (retreat orders are the same forward and reversed over the same state). A real Play-mode run with
`/tuning-log` verifies behavior (the self-checks cannot run Unity systems).

## Saves
Nothing new. The cooldown and loss history are player-private and not saved; `Ship.Damage` and fleet payloads already save. An
older save loads with an empty window and no cooldown.

## Out of scope
Rival in-flight ships and rival repair in the projection; per-ship rout (a damaged ship leaving alone); retreat of colony ships;
retreat from my own populated planet except on a wipe-out; planetary invasion; planet defense and reinforcing a planet under attack;
the AI blockading on purpose; moving `UpdateResult` out of `Planet`; recalibrating the surrender constants (a follow-up from the first
logs). The deferred combat review minors remain deferred.

## Risks
The retreat roll adds a draw from the shared `GameAI.Rand` (`SimultaneitySelfCheck` must cover it); a lost fight can still roll Stay,
by design (logged as `RetreatStay`). The `Resolve` extraction (stop and ask if it is not clean); projection cost (bounded); ping-pong between retreat and the other
planners (the cooldown); stale remembered blockade values for tier 3 (logged by `RetreatArrive`); the new share range triggering
surrender early (calibration step); a partly damaged ship left at exactly 0 health (an existing deferred minor) must not break the
projection, so the core treats a 0-health ship as not fighting, as `Resolve` does.
