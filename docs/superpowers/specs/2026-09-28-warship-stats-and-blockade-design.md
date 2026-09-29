# Warship Stats, Upgrades and Blockade — Design

Date: 2026-09-28

## Goal

Give warships real stats that research improves, make build cost follow those improvements, add an
"Update Warship" production item that brings one existing ship up to date, and add the first warship
activity: **blockade**. Ship-to-ship combat and invasion stay out of scope (see `FUTURE_FEATURES.md`).

Decisions agreed with the user during brainstorming:

- Each researched tier adds `(Max - base) / 5` to its stat (option A). The user may change this formula later, so it
  lives in one place (`WarshipStats`).
- Blockade value against an order is the **largest single other player's** docked offense at the planet minus the
  **order owner's** docked offense at that planet. Only a positive value has any effect.
- Cost factor, Update Warship targeting and route-progress blockade checks (below) were all accepted as written.

## 1. Stats

- `ShipData` (`Assets/Flatspace/Objects/Ships/ShipData.cs`) already has `shipSpeed`, `shipHealth/Max`,
  `shipDefense/Max` and `shipOffense/Max`. Fill in real values on the warship `ShipData` asset (base and max). Speed
  has no research line and stays fixed.
- Research (`Research Catalog.json`), three parallel lines of 5 tiers, each chained by `requiredTech`, all subType
  `Warship`, type `Ship Improvement`:
  - Offense: the existing "Warship Weapons Upgrade 1-5".
  - Health: new "Warship Armor 1-5". Defense: new "Warship Shields 1-5".
  - Tier 1 of each new line requires "Warship 1"; tier costs mirror the weapons line (3000 ... 15000).
- The item's `effect` string becomes the stat key: `Warship Offense`, `Warship Health`, `Warship Defense`
  (replacing "Improve warship stats"; nothing reads that text today, but grep before changing it).
- `WarshipStats` (new, namespace `FlatSpace.AI`, no `Gameboard.Instance`): given a `ShipData`, a research catalog's
  items and a ship's `ResearchSnapshot`, returns effective Offense/Health/Defense/Speed. For each stat, stat =
  base + (tiers of that stat present in the snapshot) x (Max - base) / (tier count of that stat in the catalog).
- Ships keep their `ResearchSnapshot` as the record of what they carry; stats are always derived, never stored.
  A ship's docked offense is `WarshipStats` Offense.

## 2. Build cost

- Warship cost = base cost x (1 + `warshipImprovementCostFactor` x improvements the ship carries). New
  `GameAIConstants.warshipImprovementCostFactor`, in-code default 0.1 (a fully upgraded ship, 15 improvements,
  costs 2.5x base).
- The cost is fixed into the scheduled production item when it is scheduled, so research completing mid-build does
  not change it. Every place that reads `Item.cost` for a production item (`Planet.ContinueProduction`,
  `CompleteProduction`, the industry matrix's `Cost`, the planet detail UI) must read the fixed cost instead.
  ColonyShip and all other items keep their catalog cost.
  `IndustryMatrix.Cost` keeps the catalog cost.
- New warships carry every Warship improvement the owner has researched at the moment of completion (the existing
  `BuildResearchSnapshot`).

## 3. Update Warship

- New production catalog item, subType `WarshipUpdate`, unlocked with "Warship 1". Effect: bring one docked
  warship owned by the planet's owner up to date by adding every researched Warship improvement it lacks.
- Cost = cost of a fully upgraded warship (per the owner's current research) - cost of that ship (from its own
  snapshot count). Fixed at scheduling time.
- Target: the docked warship, owned by the planet's owner, with the most missing improvements. It is only offered
  when such a ship exists. If the chosen ship has left by completion, the planet retargets to another docked ship
  that needs it; if none, the item is spent with no effect.
- AI: offered as an ordinary industry choice. Its table weight equals Warship's in every strategy (a missing
  strategy key falls back to neutral, so add entries alongside Warship's). Situational weight is 0 when no docked
  warship is upgradable, so the existing `OfferedChoices` filter drops it. Follow the convention that
  state-dependent factors go in `GetIndustrySituationalWeightMultiplier`.
- Log `ProductionComplete` as today; upgraded ship logs no extra line.

## 4. Blockade

`BlockadeSystem` (new, `FlatSpace.AI`, pure; must not touch `Gameboard.Instance`):

- `Value(planet, orderOwner)`: for every other player, sum the offense of their docked warships at the planet; take
  the maximum, subtract the sum for `orderOwner`; return it if positive, else 0.
- Only docked ships count. Ships in flight or incoming do not.

**Routes.** Routes come from `GameAIMap.GetPath`, which already returns the ordered node list; no precompute
change.

**Evaluation.** Each turn, in `ProcessCurrentOrders` before delays are applied, each in-flight blockade-sensitive order computes its progress = 1 - `TimingDelay` / `TotalDelay`. A node
"is passed" when its cumulative cost / total cost is at most the progress and it was not passed the previous turn
(the target is passed on arrival). The origin is never checked. Each newly passed node is checked with that turn's
`BlockadeSystem.Value`. Progress is derived, so nothing new is saved.

**Effects:**

- **Colony ship** (`OrderTypePopulationTransport` and its companions): a positive value at any passed node removes
  the order set: the colonist transport, the colony food rider, and the target's population-transfer-in-progress
  flag. The colonist and rider are lost, because the origin already paid for them.
- **Food and grotsits** (`OrderTypeFoodTransport`, `OrderTypeGrotsitsTransport`): each passed node with a positive
  value reduces the carried `Data` by that value. If the remaining amount is <= 0 the order is removed and its
  incoming flag (`FoodShipmentIncoming` / `GrotsitsShipmentIncoming`) cleared. The reduction is cumulative across
  nodes and is written back to the order, so it survives saves (the `float` `Data` already round-trips).
- Ship transport, research, industry and other orders are unaffected.

**Out of scope, recorded in `FUTURE_FEATURES.md`:** the AI does not yet avoid launching colony ships or shipments
into a blockaded route, so some launches will be wasted. The blockade does not change what the AI *does with* its
warships either (no new orders); it acts on whatever is docked.

**Logging.** New `AITuningLogger` lines, no toggle checks at call sites:
`Blockade|<planet>|<blockerPlayer>|<value>` (when a node is first checked with a positive value that turn) and
`OrderBlocked|<orderType>|<planet>|<amountRemaining>` (removal or reduction).

## 5. Saves

- No new save fields for stats: derived from `ResearchSnapshot`, already saved per docked ship.
- Fixed production cost is part of the production item's saved state; older saves without it fall back to the catalog
  cost.
- The new research/production items are picked up by name; older saves match by `itemName` as today.

## 6. Self-checks

New `Assets/Editor/WarshipSelfCheck.cs`, menu `FlatSpace -> AI -> Run Warship Self-Check`, built from minimal
`GameAIMap`/`Planet`/`PlayerAI` sets (never `Gameboard.Instance`, distinct planet positions per the pathing note in
CLAUDE.md). Covers:

- `WarshipStats`: base, one tier, all tiers reaching Max, each stat independent.
- Cost: factor scaling, cost fixed at scheduling.
- Update Warship: cost difference, most-missing target choice, retarget, offered only when upgradable.
- `BlockadeSystem.Value`: largest single blocker rule (two blockers do not add), owner's own ships subtract, zero
  when not positive.
- Order effects: colony order set removed, food/grotsits reduced cumulatively, removed at <= 0 with flags cleared,
  origin never checked, nodes already passed not re-checked.

Also new: **`FlatSpace -> AI -> Run All AI Self-Checks`** (requested addition). Each existing AI self-check
(`PlayerKnowledgeSelfCheck`, `PlayerAIResourceSelfCheck`, `ShipTransportSelfCheck`, `DistributionCenterSelfCheck`)
currently has a `void Run()` that logs its own summary. Each is split so its checks live in a
`public static bool RunChecks()` and `Run()` (the existing menu item) calls it, unchanged in behavior. The new item
calls `RunChecks()` on all four plus `WarshipSelfCheck`, keeps going after a failure, and logs one summary line
listing which suites passed or failed. Fog and UI self-checks are not AI and are not included.

## 7. Docs

Update `CLAUDE.md` (Tests list, a Warship stats/blockade section) and `FUTURE_FEATURES.md` (mark blockade done, add
the AI-avoids-blockade follow-up). New scripts' `.meta` files are committed with them.

## Risks

- Removing orders must clear every flag the order set created, or planets stay stuck "incoming"; the self-check
  asserts the flags.
- Production items caching cost changes a struct several places read; a missed read would silently use the base
  cost, so grep every `Item.cost` use.
