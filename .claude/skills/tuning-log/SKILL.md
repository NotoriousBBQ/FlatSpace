---
name: tuning-log
description: Load the most recent AI tuning log from AITuningLogs/ and run the standard health-check analysis (pace, colonization, research, shipments, economy, fleet discipline). Use when the user asks to check/analyze the latest tuning run, or invokes /tuning-log.
---

# Analyze the latest AI tuning log

Reproduces the analysis pattern used throughout AI tuning sessions on this project. Read this whole
file before starting; don't skip steps because they seem obvious from past runs — logs and boards
change.

## 1. Find the file

The log directory is hard-coded, at the project root, a sibling of `Assets/`:

```
AITuningLogs/
```

Pick the most recently modified `.txt` file there (`ls -t AITuningLogs/*.txt | head -1`, or
equivalent). If the user names a specific file instead, use that one and skip this step. If the most
recent file is tiny (well under ~1000 lines / a few KB), it's likely a near-instant test (someone
pressed Play and stopped almost immediately) rather than the match meant for analysis — say so and
ask, rather than silently analyzing a near-empty log.

## 2. Identify the run before analyzing anything

- **Board:** `grep BoardConfig <file>`. `InitGame` can run more than once per match (the scene's
  default board, then a designer/save load), so if there are two `BoardConfig` lines, **the last one
  is the real board**. Report which one you're using.
- **Match length:** `T<turn>` in the last line, or max turn across all lines.
- **Players:** highest `P<n>` seen, plus `StrategyChange` lines (when each player left Expand for
  Consolidate, if at all — a player that never switches stays on Expand rules the whole match).
- **Effective `improvementUpkeepScale`:** don't trust the saved asset value alone — a value changed
  live in the Unity Inspector during Play mode does not persist to disk, so the asset can say one
  thing while the match actually ran on another. Cross-check by replaying the log:
  1. For each planet, from `ProductionComplete` events up to some checkpoint turn (e.g. T200), track
     the best (highest) tier completed per resource subtype, matching the game's own rule (only the
     best tier per resource is charged upkeep, tiers replace rather than add). **Don't parse this from
     the item name's leading word** — look each `ProductionComplete` item name up in
     `Assets/Flatspace/Catalogs/Production/Production Catalog.json`, keep only entries with
     `"type": "Improvement"` (this is what excludes `WarShipProduction`/`ColonyShipProduction`, which
     are `"type": "Ship"`), and key the best-tier tracking by that item's own `subType` field. This
     matters because **Research improvements charge upkeep too** (`Planet.RecordImprovement` keys
     `_bestImprovement` by `item.subType` for any `type == "Improvement"`, with no resource-type
     filter), and so do the tier-0 `Base*` items (`BaseFoodImprovement`, `BaseGrotsitsProduction`,
     `BaseIndustryProduction`, `BaseResearchImprovement`, each `maintenanceCost: 3`). A name-prefix
     regex like `(Food|Grotsits|Industry)(\d+)Production` silently drops both of these categories,
     which only happens to not matter when a player hasn't completed a Research improvement (or is
     still sitting on `Base*`) by the checkpoint turn — it produced correct-looking scales in two runs
     and then wildly wrong ones (0.85/1.79/1.01 instead of a consistent 0.75) in a third. Always go
     through the catalog's `type`/`subType` fields, never the item name string.
  2. Sum each planet's owner's best-tier `maintenanceCost` values from that lookup — this is the
     **unscaled** total.
  3. Compare against the logged `Economy|<planets>|<short>|<morale>|<totalUpkeep>` line for the same
     player at the same checkpoint turn. `logged / unscaled` is the effective scale; it should come
     out the same (to a couple decimal places) for every player if the run is internally consistent.
  4. If this doesn't match what's in the asset (`Assets/GameAIConstantsProductionTypes.asset`,
     `improvementUpkeepScale`), say so explicitly — that's a live-edit-didn't-persist situation the
     user needs to know about, not a quiet detail.

## 3. Compute the standard metrics

All of these are cheap `awk`/`grep` passes over the log; do them all rather than a subset unless the
user asked about one specific thing.

- **Pace per 100-turn window:** `ProductionSet` count, `ProductionComplete` count, `ColonizeStart`
  count, `WarShipProduction` starts. Look for where growth plateaus or collapses. A late-game collapse
  has **three distinct possible causes** — check the signals below before concluding which one, since
  they look identical in the pace numbers alone but mean very different things for tuning:
  1. **Content exhaustion:** everyone maxed research tiers (tier 10 is the ceiling) and hit the fleet
     cap. Check research tiers per player and the `WarshipBoost` zero-multiplier rate — both pinned at
     their ceiling confirms this. Healthy/expected on a long enough match; nothing to tune.
  2. **Economic throttling:** upkeep pulling workers off industry, with real shortages and declining
     morale in the `Economy` trend. This is `improvementUpkeepScale` (or the import allowance) doing
     its job under real pressure.
  3. **Affordability-ceiling saturation:** production stalls (including warship starts) even though
     morale stays flat/healthy and shortage lines stay near zero, and research tiers are nowhere near
     the ceiling. This means `CanAffordImprovement` is proactively refusing every remaining choice
     because no planet has grotsits *headroom* left for more upkeep, even though nothing has actually
     gone short. Distinguish from (1) by checking research tiers are still low, and from (2) by
     checking morale/shortages stayed clean the whole time. First observed at `improvementUpkeepScale
     0.75` on `test2.json` (T301-400: sets/completes fell 374/430 → 20/42, 0 warship starts, while
     morale sat at/near 200 the entire match). Worth flagging explicitly when found — it means the
     gate is working exactly as designed, but the cost is starved production rather than prevented
     shortage, and it can show up at a lower scale than either of the other two causes.
- **Per player:** colonies gained (`ColonizeArrive` count), research completed (`ResearchComplete`
  count), and the highest tier reached per resource (parse `Food/Grotsits/Industry/Research
  Improvement N` from `ResearchComplete` item names — tier 10 is the current catalog ceiling).
- **Shipments:** `GrotsitsShip` / `FoodShip` counts. If a board-to-planet-type mapping is available
  (parse the relevant `Assets/Flatspace/BoardConfigs/<name>.json`'s `planetEntries` for `planetType`,
  0-7 = Prime/Normal/Farm/Verdant/Industrial/Desolate/Ocean/Desert), break shipments down by origin
  type — Desolate exporting grotsits and importing food, Farm exporting food, is the intended pattern
  and worth calling out whether it's showing up.
- **Economy per player:** every `Economy` line (`T<turn>|P<id>|Economy|planets|planetsShort|
  meanMorale|totalUpkeep`), full trend, not just the last one — a flat-200-morale run and a run that
  *declines* to 200 by the end look identical in a single snapshot but mean very different things.
  Count total shortage lines (`$5 > 0`) as the headline shortage signal.
- **Fleet cap discipline:** for every `WarshipBoost` line with multiplier `0`, confirm zero
  `WarShipProduction` starts for that player on that turn. This invariant should always hold; if it
  doesn't, that's a real bug, not tuning variance. Exception: planets blockaded against their owner are
  exempt from the fleet cap (`blockadedWarshipBoost`), so a multiplier of `0` with a `WarShipProduction`
  start is expected only when a `BlockadedProduction` line for that planet, player and turn exists;
  anything else is a real bug.
- **Blockade:** count `Blockade` and `OrderBlocked` lines; `BlockadeLearned|<planet>|<value>` is logged when a
  player newly remembers a planet where its order was cut (not on refreshes), so repeated `Blockade` losses at
  one planet should now be followed by `RouteDetour` lines; compare `ColonizeStart` to `ColonizeArrive` per
  player (all of the shortfall used to be blockade losses); read `RouteDetour` (detours taken) and
  `ColonizeCancelled` (`BlockadedOrigin`/`NoRoute`; logged when a planet's hold-back state changes, not every
  turn, so one line is one episode. Logs from before that change repeat every turn, so count distinct
  planets there).
- **Colony failures:** count `PopulationLoss` and `PlanetDead`. The colony food rider is a bridge, not
  a guarantee — a few failures are expected and fine; zero is fine too; a lot might mean the rider
  amount needs raising.
- **Combat activity:** `AssaultTarget` count/targets, if present (multi-player boards only).

## 4. Compare against a previous run when one exists

If the user is testing a specific change (a tunable, a wiring fix, a board), find the most recent
*previous* log on the **same board** (check `BoardConfig`) to diff against — same-board comparisons
are far more meaningful than cross-board ones. State plainly which prior run you're comparing to and
why (same board, same code state) or why not (different board — note it and don't force a comparison
table that isn't apples-to-apples).

## 5. Report

Lead with anything surprising or actionable (a mechanism that didn't do what was intended, a player
that fell behind and why, an invariant that broke), not a wall of raw numbers. Use comparison tables
when there's a previous run. Investigate one level deeper than the raw counts when something looks
odd — e.g. "P2 has fewer colonies" is a number; "P2 never got a single Desolate or Verdant planet, and
its Prime only had 2 connections" is the reason. Keep terminal output compact: run the analysis
scripts, don't paste raw log excerpts into the reply.
