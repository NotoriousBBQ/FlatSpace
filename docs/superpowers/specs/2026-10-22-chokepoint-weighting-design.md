# Chokepoint weighting for assault, conversion and bunkers - design

Date 2026-10-22. Session "choke_point_changes". Works from the `FUTURE_FEATURES.md` entry "Weight planet value by connectivity" (consumers (1) assault
target choice and (3) defending the player's own hubs). Related, not part of this spec: AI-initiated blockades (next in the list) and producer value by
planet type (agreed 2026-10-02, still not built).

## Intent

The owner wants to see **more activity at chokepoints**: a higher rate of assault targeting, conversion (the fleet that is sent also converts), and
bunker creation on chokepoint planets, for both attacking enemy-held chokepoints and defending owned ones, equally. Chokepoints are already ranked
(`PlanetCentrality`, `GameAIMap.Chokepoint(name)` percentile, `IsChokepoint(name)`) and already used by garrison category 4, the blockade-break target
ranking and the Consolidate colonization tilt. Assault target choice ignores them, and bunker weighting treats them only as "outer or chokepoint".

## Goals

1. `AssaultPlanner.ChooseEnemyTarget` prefers chokepoint planets, all else equal.
2. Bunker production is weighted higher on chokepoints I own, separately from outer planets.
3. The side effects of that tilt are named, logged and minimized (below).

## Non-goals

- AI-initiated blockades (separate step after this).
- Producer value by planet type, DC selection, the shipment threshold, any change to the spare-ship calculation.
- Changing `ChooseBlockadeTarget` (already chokepoint-ranked) or the sticky "most ships already there" rule.
- Moving assault target choice into `ScoreMatrix` (decided: it is a sorted pick with a sticky rule; a matrix would change behavior for no gain).
- Any separate "conversion targeting" code: conversion has no target picker; a fleet converts where assault sent it, so (1) covers it.

## Anti-goals (would make this a failure even if it works)

- A fleet pulled off a committed target, or sent a long way for a marginal hub.
- All fleets ending up on chokepoints while the rest of the map goes unattended.
- Two players trading the same chokepoint back and forth every few turns.
- Tilting Expand behavior or older self-checks (fixtures keep their meaning).

## Design

### 1. Assault and conversion: cost divisor in `ChooseEnemyTarget`

`ChooseEnemyTarget` keeps its order: most own warships docked plus incoming first (sticky), then the cheapest path. Only the **cost compared** changes:

`choiceCost = pathCost / (1 + assaultChokepointWeight x percentile)`, using `_map.IsChokepoint(name) ? _map.Chokepoint(name) : 0f` (the same gating as
`ChooseBlockadeTarget`: only real chokepoints tilt; a percentile above 1 turns it off). Same pattern as `PlayerAI.ChokepointDivisor` for colonization.

- New tunable `assaultChokepointWeight` on `GameAIConstants`, in-code default (starting point 0.5, like colonization), 0 or below = off.
- The real `pathCost` is still what `CheapestPathCost` returns and what flight timing uses; the tilt only affects which target is picked.
- Applies under every strategy (builder's note: this is the designer's call; assault only runs when a known at-war enemy planet exists, so Expand is largely unaffected in practice).
- `ChooseTarget()` (blockade first, then `ChooseEnemyTarget`) is unchanged; callers `PlayerAI.cs` lines near 1831, 2445, 2530 need no change.

### 2. Bunkers: a chokepoint factor in `BunkerWeight`

`PlayerAI.BunkerWeight` today multiplies by `bunkerOuterFactor` when `special = IsOuter || IsChokepoint`. Split it:

- outer keeps `bunkerOuterFactor`;
- a chokepoint (`ctx.Planner.IsChokepoint(planet)`) gets new `bunkerChokepointFactor` (default starting point 3, owner decision 2026-10-22, so the first batch tests the goal;
  `bunkerOuterFactor` stays 2); a planet that is both takes the larger of the two, not the product.
- The gating (`rivalBeside || (special && HasKnownEnemyPlanet)`) is unchanged, so a chokepoint still gets bunkers only when a rival is near or a known enemy planet exists.
- Weight stays inside the situational multiplier (`BunkerThreatMultiplier`); no strategy-table or `ScoreMatrix` change. The per-turn context is still built once.

### 3. Defending owned chokepoints

Covered by (2) and by the existing garrison category 4. No new code.

### 4. Side effects to watch for, each with a log line

| Side effect | Why it can happen | Log / measure |
|---|---|---|
| Cheap nearby targets neglected | the divisor makes a far hub beat a near planet | `ChokepointAssault`: fires when the tilt changed the pick (like `ChokepointColonize`) |
| Fleets over-concentrate on chokepoints | targets and garrisons both pull to hubs | existing `ChokepointGarrison` (shipsOnThem/allShips), read per run |
| Same chokepoint changes hands repeatedly | more fleets, more bunkers, same few planets | `OwnerChanged` per chokepoint, derived from existing lines plus `Chokepoints` |
| Bunkers spent on chokepoints that are never attacked | factor too high | `BunkerBuilt` gains a trailing `chokepoint` 0/1 field |

Minimizing: the sticky rule is untouched; the weight starts moderate; both tunables can be set to the neutral value to switch off.

## Constants (all on `GameAIConstants`, in-code defaults)

- `assaultChokepointWeight` (0.5 start, <= 0 off)
- `bunkerChokepointFactor` (3 start; a non-chokepoint outer planet keeps `bunkerOuterFactor` 2)

Owner decision 2026-10-22: the sticky rule stays first (reading (a)); the assault tilt only decides among targets with no ships of mine committed, so expect a modest effect and
a rare `ChokepointAssault` line. Letting a chokepoint outrank a target with some committed ships is not built; revisit if the logs show too little effect.

## Tuning log output

| Line | When | Fields | Question it answers | Kept from repeating by |
|---|---|---|---|---|
| `T<t>\|P<id>\|ChokepointAssault\|target\|pathCost\|percentile\|nearestTarget\|nearestCost` | the turn an assault target is chosen and the tilt changed it vs the untilted pick | as listed | is the tilt moving fleets, and how much farther? | log-only state in `PlayerAI` (last logged target per player), logs on change |
| `BunkerBuilt` trailing `chokepoint` (0/1) | existing line, on completion | one added field | are bunkers landing on chokepoints? | already once per completion |

Also update `.claude/skills/tuning-log/SKILL.md` (new line, new field, a "chokepoint activity" check: share of assault targets, `BunkerBuilt` and `OwnerChanged`
on chokepoints, against the 10-22 baseline) and the "AI Tuning Log" and "Connectivity" sections of `CLAUDE.md` / `Assets/Flatspace/GameAI/CLAUDE.md`.

## Tests

Extend `BlockadeBreakSelfCheck` / `ChokepointSelfCheck` (in `AllAISelfChecks`): (a) with weight 0, `ChooseEnemyTarget` is unchanged; (b) a farther chokepoint beats a nearer
non-chokepoint at a weight where the divisor allows it, and not when the sticky rule applies; (c) `BunkerWeight` uses the larger of outer and chokepoint factors; (d) the new
`ChokepointAssault` formatter. Fixtures that need the old behavior set `assaultChokepointWeight = 0` (as older ones set `colonizationChokepointWeight = 0`). No
self-check may use `Gameboard.Instance`.

## Success measure

Compare Play runs on `4p.json` and `test2.json` (6 per board, read with `/tuning-log`) against the 2026-10-22 planet-defense batch: share of assault targets,
`OwnerChanged` and `BunkerBuilt` on chokepoints rises; neglect, concentration and churn stay within the watch limits above (limits to be set after the first batch).

## Calls made by the designer (minor, from the builder check; the owner can override)

- **"Untilted pick" for `ChokepointAssault`** = the same loop and sticky rule with the weight at 0, tracked as the best untilted candidate in the same pass (one loop, no second run).
  The log's `pathCost` is the real (untilted) path cost of the chosen target; `nearestCost` is the untilted pick's real cost. Repeat guard key: target plus nearest target, per player.
  Targets with ships already committed (cost 0) never tilt, so the line cannot fire for them.
- **Bunker chokepoint factor is flat** (a bool from `ctx.Planner.IsChokepoint`, no percentile scaling), and a chokepoint still satisfies `special` in the existing gate, so the gate
  (`rivalBeside || (special && HasKnownEnemyPlanet)`) is unchanged: a chokepoint with no rival near and no known enemy planet still gets no bunkers.
- **`BunkerBuilt` `chokepoint` field** = chokepoint status at completion time. It is appended last; the only consumer is the `tuning-log` skill text (line ~338), updated in step.
- **Baseline:** the 2026-10-22 planet-defense batch logs exist in `AITuningLogs/` (gitignored). The success check reads chokepoint share from them with the existing `Chokepoints`
  and `BunkerBuilt` lines, so no extra baseline run is needed.

## Left to the builder

Exact default values after the first batch; exact placement of the log-only "last target" state; whether `CheapestPathCost` is reused as is or returns the path
alongside the cost for the log line.
