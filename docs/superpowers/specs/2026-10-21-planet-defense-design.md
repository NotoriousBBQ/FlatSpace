# Planet Defense: Bunkers, Siegeships and Bombardment (design)

Date: 2026-10-21. Source: `FUTURE_FEATURES.md` entry (4) "Planet defense" under the `ship_combat` milestone. Brainstormed with the owner;
every requirement below is the owner's unless marked **(my call)**. Read `CLAUDE.md` and `Assets/Flatspace/GameAI/CLAUDE.md` first
(Ship combat, Invasion, Blockade, Ship Transport sections).

## Intent

Conversion (invasion, finished 2026-10-07) lets a fleet take a planet by sitting on it. There is nothing a planet can do about it. This
feature gives planets a defense (**bunkers**), gives attackers the tool to defeat it (**siegeships** and a **bombard** stat), and makes
conversion depend on bombard so that siegeships, not warships, become the conquering ship. Warships become the escort that fights other
ships and screens the siegeships.

The owner declined to tune conversion speed for existing ships first (`offense-scaled conversion speed`) because bombard is expected to be
the more useful value; that item is folded into this one (requirement 16).

## Goals

- A planet can build bunkers that hold off an attacking fleet until siegeships destroy them.
- Conversion cannot happen while any bunker stands, and its speed comes from bombard.
- The AI builds bunkers where there is a threat, builds siegeships when it needs to break bunkers, and assembles escorted fleets.
- No fleet stalls waiting for siegeships (see Anti-goals).

## Non-goals

- Multiple combat rounds per turn: parked in `FUTURE_FEATURES.md` ("Multiple combat rounds per turn", 2026-10-21). Combat stays one
  round a turn.
- Combat for ships in flight. Ships in flight still cannot be attacked.
- A colony ship that physically flies as a ship (requirement 18 uses option B below).
- Producer value, connectivity weighting of targets and the other open `FUTURE_FEATURES.md` items.

## Anti-goals

- **A mixed fleet's departure stalled because enough siegeships have not been built yet.** It is acceptable to send later siegeships to the
  contested planet after the first force arrives, and to send siegeships from different planets, as long as (1) they will not be destroyed
  while travelling and (2) they will not be outmatched by the enemy's warships on arrival.

## Requirements

1. **Defense bunker.** A Production Item, closer to warships than to planetary improvements. It has health, offense (attack) and defense.
   A planet can hold as many bunkers as its max population.
2. **Bunker research.** A research path for the bunker similar to the warship's.
3. **Bombard.** A new warship stat, used only when attacking bunkers, improved through research like any other ship stat.
4. **Existing warships** get a bombard stat, **very low** (not zero: requirement 16 needs it above zero).
5. **Siegeship.** A new ship type with all the warship stats; bombard is high, attack and defense are low. (Owner's spelling "Seigeship";
   this document uses Siegeship.)
6. **Siegeship research.** A research path for siegeship upgrades similar to the warship's.
7. **Damage order.** When docked siegeships and warships are in ship-to-ship combat with other docked ships, warships take damage first.
8. **Bunker engagement.** Once all the enemy's warships are removed from a planet, the fleet engages the bunkers: total bombard value is
   compared with total bunker defense value to damage bunkers.
9. **Bunker damage to ships.** Bunker attack value is compared with docked ship defense value to determine damage to ships.
10. **Conversion gate.** Conversion cannot take place until all bunkers are destroyed.
11. **Bunker repair.** Bunkers not under attack repair as warships do, **only on a non-dominated planet**.
12. **Mixed fleets built by the AI.** Warship plus siegeship, or warship plus colony ship, with tunable composition parameters.
13. **AI support.** The AI adjusts all ship-related functions as necessary to support siegeships and mixed fleets.
14. **ScoreMatrix.** AI decisions about fleet composition and actions are made by `ScoreMatrix` whenever possible.
15. **Reuse first.** Reuse or modify existing components instead of designing new systems whenever possible (session rule 2).
16. **Bombard replaces offense in conversion.** Bombard value is used for conversion instead of attack value. Dominance itself keeps its
    current rule, with "has offense" becoming "has bombard above zero". Only the speed share (`ConversionSystem`, my share `p`) uses bombard.
17. **Escort to a dominated planet.** The AI escorts the colony ship to a dominated planet.
18. **Escort through known blockades.** If the AI wants to colonize a **non-dominated** planet and knows of a blockade on the way, it builds an
    escort of comparable combat value, and the group passes through the known blockade instead of taking the long way round. Decided as
    **option B**: the colonist is launched as today and the escort warships fly the same route; the escort's offense is subtracted from the
    blockade value at each blockaded node, the way docked offense is subtracted now. The colony ship does not fly as a ship.

## Decided design

### Combat: a separate bombardment step (owner's call)

Per planet each turn:

1. **Ship-to-ship step**, as today (`CombatSystem.FightAt` / `Round`). Attack values hit ships. Bunkers take no part and bombard is unused.
   Damage order: the victim's warships first, then siegeships (a new first sort key ahead of the existing "lowest health fraction, then dock
   index"). **(my call)** Siegeships join the side's attack pool at their own (low) attack value.
2. **Bombardment step**, on every planet where, after step 1, a player with docked ships has a war rival with bunkers there and that rival has
   **no docked ship of either kind** (warship or siegeship, owner's choice: a surviving siegeship still protects the bunkers). This includes
   the same turn the last protecting ship dies. Bunkers fire only in this step, never in step 1 (owner's call).
   - Attackers' total effective bombard is compared with the bunkers' total defense to damage bunkers.
   - The bunkers' total attack is compared with the attackers' ship defense to damage ships.
   - **Simultaneous** (owner's call): both read the start-of-step values. **(my call)** Reuse the existing damage arithmetic, a hit scaled by
     `K / (K + defense)` with `combatDamageK`, focus fire on the lowest health fraction, one pool per victim, hostility split among rivals.
     The bombardment is a second small pure function next to `CombatSystem.Round`, not an edit of `Round`, so `FightProjector` and the retreat
     logic are unchanged unless noted below.
   - **(my call)** A siegeship's effective bombard scales with its remaining health like effective offense does.
3. **Repair**, as now for warships, and for bunkers: heal `repairFractionPerTurn` of the Health stat per turn, but a bunker repairs only when
   its planet is not dominated by an at-war rival. Dominated means the existing dominance rule: a rival has docked bombard above zero and the
   bunker owner has no at-war ship docked. **(my call)** A defender's docked siegeship counts, like a warship, as a blocker of dominance.
4. **Conversion** (`ConversionSystem`) starts and continues only while the planet has no bunkers. Speed uses bombard share instead of offense
   share (requirement 16).

### Data model

- **Ship kinds.** `Ship.ShipKind` gains `SiegeShip` and `Bunker` appended last (the enum serializes as an int). **(my call)** A bunker is a
  docked `Ship` that never undocks, so combat, damage, saves and the fleet UI reuse the existing ship plumbing. A planet's cap is its max
  population, checked when production is offered; a planet that loses population keeps any extra bunkers.
- **Stats.** `ShipData` gains `shipBombard` and `shipBombardMax`. Siegeship and Bunker are new `ShipData` assets. Existing warship assets get a
  very low bombard. **(my call)** Stats come from a `WarshipStats` generalised by ship kind, since the formula (base + tiers carried x
  (max - base) / tiers in the line) is the same; its research keys gain `Bombard` lines for warships and parallel lines for the siegeship
  and bunker subtypes. The catalogs' subtype strings are load-bearing (cf. `"Warship"`).
- **Research.** New "Ship Improvement" catalog items for the siegeship (offense, health, defense, bombard lines) and the bunker (attack,
  health, defense lines), and a bombard line for the warship. `PlayerAI.NormalizeResearchWeightsBySubtype` already balances parallel lines.
- **Production.** New Production catalog items "Siegeship" and "Defense Bunker" (required tech = the first research item of the line).
  **Starting cost (owner's call, 2026-10-21): 150% of the Warship's catalog cost, as a ratio, not a fixed number**, for both the Siegeship
  and the Defense Bunker, so it tracks any later warship retuning (the runtime `Production Catalog.json` has the Warship at 100, so 150 today;
  the `WarShipProduction.asset` file's 20 is not what the game loads). The ratio is a tunable on `GameAIConstants` with an in-code default of 1.5.
  **(my call)** Cost then scales with improvements researched like the warship (`warshipImprovementCostFactor`, `FixedCost`), each with its own
  factor. An "update" item for bunkers and siegeships is not part of this feature (the warship update item stays warship-only).
- **Saves.** Ships already save kind, owner, snapshot and damage; the new kinds ride on `ShipSave`. New kinds default safely in older saves.

### AI (stage 2)

- **Bunker building.** A situational weight in `GetIndustrySituationalWeightMultiplier` (not in the strategy tables): highest on outer planets
  and chokepoints (the garrison categories in `ShipTransportPlanner`), boosted when the planet is blockaded or an at-war rival's ships are
  docked beside it, zero when no rival is known nearby and zero at the cap. Consolidate and Amass only (`IsConsolidateLike`). Tunables on
  `GameAIConstants` with in-code defaults; compute at most once per production turn when it reads many planets.
- **Siegeship production.** The same fleet-shortfall boost the warship has, with the siegeship count wanted by the assault plan.
- **Assault composition, sized by need.** The warship force is sized as today (`assaultRatio`, `assaultMinimumShips`). Siegeships are added
  until their total bombard covers the target's known bunker defense times a tunable margin, up to a tunable cap on the siegeship share of the
  fleet. **(my call)** The AI sees a known planet's bunkers the way it sees docked warships. Siegeships never count toward garrisons, so they
  stay spare for assaults.
- **No stalls.** The assault force departs with what exists. Later siegeships follow to the target (the sticky assault target already pulls
  ships). A siegeship leaves its planet for a contested target only when the existing fight projection (`FightProjector`, which already counts
  my in-flight ships landing there) shows the arriving group is not outmatched by the enemy warships there. Ships in flight cannot be
  destroyed today, so condition (1) holds as long as that stays true; if combat for ships in flight is ever added, this must be revisited.
- **Escorts.** For requirements 17 and 18: a colonist headed for a dominated planet, or for a non-dominated planet whose route crosses a known
  blockade, is launched with an escort whose offense covers the blockade value times `1 + blockadeBreakMargin` (**my call**, existing
  tunable, 0.1). `RoutePlanner` compares the escorted route's cost with the detour and `ScoreMatrix` picks. The escort travels with the
  existing ship-transport order trio; `BlockadeSystem` subtracts an order's escort offense from the blockade value at each node.
- **Decisions use `ScoreMatrix`** for bunker building, siegeship production, escort-or-detour and assault composition (requirement 14).

## Tuning-log output (session rule 4; to be finalised in the plan)

Proposed lines, each state-change only through a tracker or log-only state, and the `tuning-log` skill and the "AI Tuning Log" section of
`CLAUDE.md` updated with them: `BunkerBuilt|<planet>|<count>|<cap>|<weight>`; `Bombard|<planet>|<attacker>|<victim>|<bombardDealt>|<bunkersLeft>|<shipDamageTaken>`
each bombardment turn; `ConversionBlocked|<planet>|<bunkers>` on a state change; `SiegeSized|<target>|<bunkerDefense>|<siegeBombard>|<siegeships>|<cap>`;
`SiegeFollow|<origin>-><target>|<ships>|<projectedLossPct>` and `SiegeHeld|<origin>|<target>|<reason>` (outmatched on arrival);
`EscortLaunch|<origin>-><target>|<escortOffense>|<blockadeValue>` and `EscortDetour|...` when an escort was declined. They answer: do bunkers
slow conquest, are siegeships built in time, do followers arrive safely, does the escort beat the detour.

## Self-checks

Extend or add under `Assets/Editor/` (never touching `Gameboard.Instance`): a bombardment/bunker suite (damage both ways, the trigger,
simultaneity, repair rule, the conversion gate and bombard speed), plus cases in `CombatSelfCheck`, `ConversionSelfCheck`, `WarshipSelfCheck`
(stats with a bombard line), `ShipTransportSelfCheck`, `SimultaneitySelfCheck` and the AI suites listed in `AllAISelfChecks`.

## Left to the builder

All numbers (bunker and siegeship stats, costs, research tiers, the very low warship bombard, the AI tunables and defaults); the exact sort
implementation of the damage order; the UI for bunkers and siegeships in the fleet panel and planet detail; file and class names; the
staging of the plan into the two stages above; whether bunkers appear in the fog/knowledge systems beyond "known planet shows its bunkers".

## Known interactions to watch (not decisions)

- Conversion pace: `conversionTurnsBase` (3) was tuned on offense share; bombard share with a very low warship bombard will be far slower
  until siegeships exist. Retune from Play logs.
- `FleetStrength`, surrender terms, hostility and retreat read warship offense; whether siegeships and bunkers join those measures is left to
  the plan, default no change.

## Calls settled after the builder check (2026-10-21; my calls unless marked)

- **Bunkers in existing ship code.** A bunker is in `Planet.DockedShips` but is never counted in any existing ship metric: garrisons and
  spare counts, dominance and blockade checks, `FleetStrength`, surrender and hostility terms, retreat, ship-transport sources and sinks,
  fleet summary and fleet UI grouping. Every existing reader filters on `Ship.Kind` (a shared helper, one audit task in the plan). A bunker
  can never be undocked, shipped or launched.
- **What the AI knows.** A known planet's bunkers are read as current state, like its docked warships (no remembered snapshot, no new save
  field). Stale-knowledge handling is not part of this feature.
- **Damage order.** The victim's ships are ordered by class first (warships, then siegeships), then by the existing lowest health fraction and
  dock index. It applies to whichever side is being hit, in `Round` and in `FightProjector`.
- **Dominance and repair.** A lone warship with bombard above zero dominates a bunkered planet and stops bunker repair there, even though it
  cannot convert. That follows from the owner's rule and is intended.
- **Escort mechanics (option B).** The escort is existing spare warships only (no new production request); if too few are spare to cover the
  blockade, the colonist takes the detour. The escort's trio and the colonist's launch on the same turn, and the colonist order records the
  escort offense at launch (a new field on the order, saved in `OrderSave`), which `BlockadeSystem` subtracts at each node. The two arrive
  independently: nothing in flight can be attacked, so the colonist does not depend on the escort surviving. On arrival the escort is
  ordinary docked warships, spare for the garrison and assault planners. No fight on arrival is forced for a dominated planet.
- **Bunker economy.** Bunkers go through the industry matrix as their own subtype, with no grotsits upkeep and outside
  `CanAffordImprovement` (the owner: closer to warships than to improvements). `BestTierPerSubtype` applies as it does for other tiers.
- **Followers.** A later siegeship leaves for the sticky assault target only when the fight projection (including bunker fire, see below)
  shows the arriving group not outmatched. With the first force gone and bunkers standing, the projection shows a loss and the siegeship stays.
- **Siegeships in transport roles.** Siegeships are never garrison sources or sinks and take no part in Expand. They wait where built until an
  assault plan pulls them.
- **Projection includes bunker fire (owner's call, 2026-10-21).** `FightProjector` gains the bombardment step after its ship-to-ship rounds
  (same trigger, same arithmetic as the real step), so `RetreatPlanner`'s gate and loss fraction, and the follower check, count bunker fire.
  A fleet losing to a bunkered planet can therefore retreat. Rival repair and rival in-flight ships stay unmodelled, as today.
