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
- **New log lines (after a logging change):** when the user says a logging change was made ("verify the logging
  changes took effect"), check that before any other analysis: `grep -c` each new or changed code, print one sample
  line per code and confirm its fields match the documented format (CLAUDE.md, "AI Tuning Log"), and confirm removed or
  renamed codes are gone. Report "all lines showed up" or exactly which are missing. A code tied to a rare condition
  (`ShipmentLossy`, `BlockadeLearned`, `ColonizeCancelled`) can be legitimately absent from one quiet run, so look across
  every log the user just made before calling it missing.

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
  Resource shipping is blockade-aware too: split `OrderBlocked` by order type (`OrderTypeFoodTransport` /
  `OrderTypeGrotsitsTransport` should fall sharply against a pre-change run on the same board); count
  `ShipmentLossy` (shipments sent through unavoidable blockades) and `ShipmentCancelled` (a shortage whose every
  source was removed by blockades; state-change only, so one line is one episode; the line is
  `ShipmentCancelled|<target>|<reason>|<source>|<loss>|<amount>|<blocked planets>`: reason is `Blockade` (loss >= amount) or
  `LowYield` (only `shipmentMinDeliveredFraction` refused it), source/loss/amount describe the dropped pair closest to
  shipping, and the blocked planets are the `name=value` blockades on that route, `-` when none; older logs stop after the
  reason). To tune that fraction (default 0.5), compute each
  `ShipmentLossy` line's delivered share `(amount - loss) / amount` (it should now sit at or above the fraction) and count
  `LowYield` episodes per target: a target that keeps logging `LowYield` while `PopulationLoss`/`PlanetDead` lines appear
  for it is being starved by the threshold (lower it, ideally per target through `MinDeliveredFractionFor`). `RouteDetour` now comes from both
  colonists and shipments: attribute each by the neighbouring `ColonizeStart` or `FoodShip`/`GrotsitsShip` line.
  Shipments are also cut at a blockaded ORIGIN on their first turn, so a run on a board with lasting blockades is not
  directly comparable with one from before this change; say so when comparing.
- **Lasting occupations:** per run, rank planets by `Blockade` events (`T<turn>|P<victim>|Blockade|<planet>|<blocker>|<value>`)
  and report the top three with their share of all blockade events, the blocking player, the victim(s), the turn range,
  and how the value grows (first and last value). A planet taking about half or more of a run's events is a lasting
  occupation (on `4p.json` one hub took 60 to 90% in every run); give its share of shipment cuts too (`OrderBlocked` for
  food/grotsits over `FoodShip` + `GrotsitsShip`). Say what kind of planet it is: type, connection count from
  `Assets/Flatspace/BoardConfigs/<board>.json`, and betweenness rank from the `T0|P-1|Chokepoints|` line (top 10, as
  `planet=betweenness%percentile`) or, for a planet outside it,
  `./tools/planet-centrality.ps1 -Board <config> -Highlight "<planet>"`: a chokepoint (high rank, e.g. `Industrial 4`) or
  a specialised producer (low rank, e.g. `Verdant 4`). The user watches this because it feeds the "Weight planet value by
  connectivity" idea (`FUTURE_FEATURES.md`): mention it when the occupied planets are strategic.
- **Who blockades whom:** a victim x blocker matrix of `Blockade` events per run. Link each occupier to that player's
  `AssaultTarget` lines: a blockade normally comes from the assault logic sending a fleet to its target (e.g. P1's
  `Normal 6` blockade was its own assault target; not a bug), so report a lasting blockade that was not preceded by an
  `AssaultTarget` on that planet as the surprising case.
- **Colonist cuts against the blockade memory:** for `OrderBlocked|OrderTypePopulationTransport`, count cuts per player and
  planet, list repeat clusters (3 or more cuts at one planet; name the worst), and classify each cut against that
  player's `BlockadeLearned` for the planet: *in flight when learned* (launched before the learn turn and cut within a few
  turns after it; only in-flight rerouting, sub-project 5, fixes these), *after the memory expired* (more than
  `blockadeMemoryTurns` since the last learn or cut; a longer memory would fix it), or *no memory yet* (the first cut).
  Give the colonist arrive/start ratio next to it.
- **Contested planets (cross-player recolonization):** report as its own headline, not buried in `ColonizeArrive` totals: a
  `ColonizeArrive` on a planet whose previous owner (an earlier `ColonizeArrive`, or the owner before a `PlanetDead`) is a
  different player. Per run give the event count, the number of distinct planets, and the most contested planet (e.g.
  `Industrial 16` changing hands 9 times). Planets changing hands is a desired outcome, so call it out and do not flag
  it as a defect.
- **Colonize-die churn:** per run, count `ColonizeArrive` (target = the name after `->`) and `PlanetDead` per planet, and
  list the planets with repeated cycles (a `ColonizeArrive` followed by a `PlanetDead` or `PopulationLoss` run that empties
  it, three or more times, or five or more deaths), with the turn range and the colonizing players. Report the share of all
  `PlanetDead` events that the top two planets account for (for example `Verdant 0` and `Industrial 1` were 63 of 100 deaths
  in one set of `test2.json` runs against 8 of 44 before) and compare the per-planet death counts with the baseline runs, since
  a jump in total `PlanetDead` or `ColonizeArrive` is usually one or two planets, not a systemic change. For each flagged
  planet separate the two causes:
  1. *Designed failure:* a planet that cannot feed itself (Desolate; the colony food rider, `ColonyRider`, is a bridge, not a
     guarantee), so a few deaths per run are expected. Check that it is supplied while alive (grotsits exported, food in).
  2. *Unsupplied churn:* a non-Desolate planet recolonized again and again with almost no `FoodShip`/`GrotsitsShip` arriving
     for it (sum the amounts with it as target, per player). Find out why nothing reaches it: its `ShipmentCancelled`
     episodes (reason `Blockade` or `LowYield`), `Blockade` cuts at the planet and at its neighbours, `BlockadedProduction`,
     `AssaultTarget` lines on it or its neighbours, and its place on the board (connection count from the board config: a leaf
     behind a hub that is blockaded or contested cannot be fed; `Industrial 1` behind `Verdant 0` on `test2.json`).
  For the flagged (churn) planets only, give the grotsits-to-food ratio per planet per run: grotsits shipped OUT (`GrotsitsShip`
  with the planet as origin) over food RECEIVED (`FoodArrive` with it as target), with `ColonyRider` amounts shown separately
  and also added in; a planet that exports nothing gets grotsits received over food received instead. On `test2.json` the
  churning Desolate planet (`Desolate 0`) exports about 10 grotsits per food received (about 6 counting riders), while the
  non-Desolate churn planets import about 3 to 4 grotsits per food; an importer with near-zero inflow of both is the
  unsupplied case. Pool the ratios by planet type for the summary.
  Give the churn cost (colonize starts spent on the planet, for example 15 starts and 14 arrivals over 230 turns) and say
  which run-to-run variation it is (the same planet in every run, or only in the runs where a rival occupies its hub). The
  Colonize-die churn, including on planets the AI cannot currently supply (a leaf behind a contested hub), is desired
  behaviour: describe the mechanism neutrally and report it as information, never as a defect or gap, and do not propose a
  supply-reachability gate or a colonization back-off unless the user's own question is about survivability tuning.
- **Grotsits shortage (which planets, when):** from `GrotsitsShort|<planet>|<Start or End>|<population>|<capacity>|<upkeep>|<morale>`
  (older logs lack it; the `Economy` line only counts short planets). For a player, list the first planets to go short with
  their turn and type (the planet name carries the type), how many are short at once over time, and how long each episode
  lasts (Start to End; one never ended means short to the end of the run). Read the numbers with it: demand is about
  population + upkeep, so `capacity` below that explains the shortage (a food specialist with too little grotsits capacity,
  upkeep from improvements) and capacity above it points at shipping (check `GrotsitsShip` into that planet and
  `ShipmentCancelled` for it). Count Starts per planet to spot flapping (a planet that toggles every few turns). Use it
  to say what tips a player into the shortage-morale-fewer-colony-ships loop before morale falls under about 100.
- **Weak start:** flag a player whose morale or planet count lags early (morale under about 100 or several planets short
  at T100, noticeably fewer planets at T250 than the others) and say whether it recovers. Check its blockade and colonist
  cut counts to say whether it looks like a start-position or balance effect or blockade damage.
- **Shipment delivery:** per run, total the amount fields of `FoodShip`/`GrotsitsShip` (`T|P|FoodShip|<origin>-><target>|<amount>`)
  against `FoodArrive`/`GrotsitsArrive`, and report delivered / sent for each resource. Counts alone hide blockade losses
  (arrivals are reduced, not removed); with blockade-aware shipping this should sit near 0.99 (it was about 0.82 to 0.94
  before shipment avoidance).
- **Lossy shipments:** for each `ShipmentLossy|<origin>-><target>|<amount>|<loss>` compute the delivered share
  `(amount - loss) / amount` and total amount sent against planned loss per run (the shipment minimum delivered fraction,
  `shipmentMinDeliveredFraction`, should keep every line at or above it). Then join each food/grotsits `OrderBlocked`
  to a lossy plan by player and the plan's origin or target within about 14 turns after it, and report cuts at the
  origin, at the target, mid-route and unexplained. Planned and actual cuts should agree; a handful of unexplained cuts
  are blockades the player could not see, many mean the view or memory is missing something.
- **Detour lanes:** a `RouteDetour` with no `ColonizeStart` for the same player, turn and route is a shipment detour.
  Report colonist and shipment detour counts per run, the top lanes (origin->target pairs, how many of that pair's
  shipments detoured), and the average node count and cost. A lane with dozens of detours over hundreds of turns is a
  lasting blockade being routed around (working as intended), but note the extra transit time it costs.
- **Cancelled episodes per target:** `ShipmentCancelled` counts per player, target and reason. One line is one episode, yet
  a flickering shortage repeats, so a target with dozens of episodes deserves a look: which planets are named in its blocked
  planets field (count them per player across the run: one blockaded planet, often the source itself, usually explains a
  whole cluster, for example a small blockade on the only surplus planet cancelling every small shipment), who blockades
  those planets, and is the target's population falling (`PopulationLoss`/`PlanetDead` for that planet)?
- **Variance with few runs:** with three runs per side, print each metric's per-run values next to the average (and the
  planets owned at T375 per player per run, which swing between runs) and call a difference real only when the ranges do
  not overlap (for example `Blockade` 166 to 321 before against 6 to 43 after); an overlapping difference
  (`PopulationLoss` 94 to 215 against 138 to 286) is a watch item, not a finding.
- **Colony failures:** count `PopulationLoss` and `PlanetDead`. The colony food rider is a bridge, not
  a guarantee — a few failures are expected and fine; zero is fine too; a lot might mean the rider
  amount needs raising.
- **Combat activity:** `AssaultTarget` count/targets, if present (multi-player boards only). `AssaultTarget` is now logged
  only for an ordinary enemy-occupied target; a blockade target is logged as `BlockadeTarget` instead.
- **Blockade breaking:** under Consolidate the assault goes to a planet blockaded against the player first.
  `BlockadeTarget|<planet>|<blocker>|<value>|<neededOffense>|<Committed, RecentCut, Chokepoint or Cheapest>` logs a target change (the
  last field is the ranking step that decided it; blocker `-1` is a remembered, unseen planet),
  `BlockadeForce|<planet>|<ships>|<offense>|<stillNeeded>` each turn ships are sent at it, and
  `BlockadeTargetEnd|<planet>|<Cleared, Switched or Unreachable>|<turnsHeld>` when it stops being the target. Report per run:
  - **Time to clear:** `turnsHeld` of every `Cleared` end, and how many targets ended `Switched` or `Unreachable` instead
    (abandoned before clearing). A planet that is a target again and again is a lasting occupation the force cannot beat.
  - **Wave coverage:** the `BlockadeForce` lines per target. `stillNeeded` should fall to 0 within a few waves; waves that
    end with `stillNeeded > 0` while the blockade stands mean the margin (`blockadeBreakMargin`) is too small or the fleet
    is. Compare `neededOffense` on the `BlockadeTarget` line with the total `offense` sent.
  - **Effect:** after a `Cleared`, `Blockade` and `OrderBlocked` lines at that planet against the same player should stop;
    count them in the 20 turns before and after.
  - **Ranking:** how often `RecentCut` decides a target (it needs `BlockadeLearned` for that planet within
    `blockadeTargetRecentTurns`) and how often `Chokepoint` decides one (the more central planet wins a tie on committed
    offense and recent cut), against `Committed` and `Cheapest`. If `Chokepoint` almost never decides, say so: the step is
    weak by design. Moving it ahead of the recent-cut step is a recorded tuning option (it touches the blockade ranking, so
    tell the user).
  - **Chokepoints:** `T0|P-1|Chokepoints|...` names the board's top chokepoints. `ChokepointColonize|<origin>-><target>|<routeCost>|<percentile>|<nearestTarget>|<nearestCost>`
    is logged only when the Consolidate tilt moved a colonization away from the nearest candidate: report how often per
    player, how much farther (`routeCost` against `nearestCost`) it reached, and whether those colonies survived (a
    `PopulationLoss` or `PlanetDead` at the target afterwards). `ChokepointGarrison|<colonized>|<boardTotal>|<shipsOnThem>|<allShips>`
    (every 25 turns per player): report the share `shipsOnThem / allShips` and whether the assault's spare ships
    (`BlockadeForce` and `AssaultTarget` waves) fell after chokepoint garrisons began. Producer value (Verdant 1.0, Desolate
    1.0, Farm 0.5, others 0) is agreed but not built; mention it when tuning connectivity weights.
  - **Discipline:** the fleet cap still holds while a force is committed (`WarshipBoost` multiplier `0` with zero
    `WarShipProduction` starts, except where a `BlockadedProduction` line exists).
  - **Starved blockades (skipped, never answered):** `BlockadeSkipped|<planet>|<NoPath or Outranked>|<value>|<winner>|<winnerCommitted>`
    is logged on change only when a blockaded planet is passed over (winner `-` for `NoPath`). For every victim/planet pair
    with 15 or more `Blockade` cuts, list its cuts, the victim's `BlockadeTarget` lines for it, `BlockadeForce` waves sent
    there, and its `BlockadeSkipped` reasons (a pair with many cuts and few or no targets is starved). Then name the cause:
    `NoPath` (no route from any warship holder), `Outranked` with a large `winnerCommitted` (garrison ships already docked at
    the winner decided it, the committed-offense step), or neither (no skip line, no target: not in the player's view) and
    a target with no `BlockadeForce` after it (no spare ships). `Desert 6`, `Desert 10` and `Industrial 14` on `4p.json`
    were cut 91, 74 and 98 times with 3, 0 and 5 targets before this line existed.
  - **Research boost:** `OffenseResearchBoost|<item>|<multiplier>` should appear only while a `Blockade` line for that
    player is recent; compare the turn the first Warship Offense item completed against a run without blockades.
  - **Colonist redirect:** `ColonistRedirect|<origin>-><oldTarget>|<Detour or Divert>|<newTarget>|<nodes>|<cost>|<blocked>|<declinedDetourCost>` is
    logged once per redirect, `ColonistRedirectFailed|<origin>-><target>|<blocked>` once per colonist that could not be saved,
    `ColonistDocked|<planet>|<amount>` for each colonist arriving at a full planet. Report, per player: redirects by kind
    (Detour against Divert), failures, docked colonists, and colonist losses (`OrderBlocked|OrderTypePopulationTransport`)
    against the earlier runs on the same board (the 4p.json runs of 2026-10-02 12:40-12:48 are the pre-redirect baseline).
    A divert's new target should usually show a colonization soon after (`ColonizeArrive`/`PopulationLoss` at it); a
    `ColonistDocked` planet's colony ship should launch within a few turns (`ColonizerReady` then a colonist from it). The
    same order must never log `ColonistRedirect` twice for the same blocked node, and `ColonistRedirectFailed` repeating
    every turn for one order is a bug (it is once per order). **Detour length:** report detours against diverts (count, average
    `cost`, share that never reached a `ColonizeArrive` for the same origin and target) and how often the detour-ratio rule fired
    (a `Divert` whose last field is not `-`: the declined detour cost, which is above `colonistDetourDivertRatio` times that
    divert's `cost`). Baseline before the rule (4p.json, 8 runs): 14 detours averaging cost 1787, 79% never arrived; 84 diverts
    averaging 533, 11% never arrived. A detour still long after the rule means no diversion candidate existed.
  - **Diplomacy:** `Stance|<rival>|<Peace or War>|<hostility>|<cutsTerm>|<nearTerm>|<strengthTerm>|<pWar>|<lossAccum>` (lossAccum added 2026-10-05: the part of the hostility that ship losses account for; read lossAccum / hostility on each `Stance ... War` line to see how many wars come from losses; older logs lack it) is one line per stance
    change, `WarForced|<rival>|<Start or End>` one per war a rival declared on a player that has contact with it,
    `Hostility|<rival>|<H>|<myStrength>|<rivalStrength>|<nearShips>|<lossShare>|<lossAccum>` (lossShare and lossAccum added 2026-10-05: lossShare is the per-rival share of my fleet strength it destroyed over the 10-turn window, the number the loss drop and the Surrender weight read; older logs lack the field) every 25 turns per player and rival with contact, and
    `StrategyChange` now also logs Consolidate to Amass and back. Stances commit through orders (`Stance` is logged when the
    order executes), so a rival's declaration is first seen (`WarForced Start`, the Amass switch) one `ProcessResults` after it
    was decided, which is intended. Report: the first `Stance ... War` turn per player and pair;
    the share of players in Amass over time (from `StrategyChange`); war length (a War to the next Peace per pair); forced
    wars (`WarForced Start`) against chosen ones (`Stance ... War`) and the turns between a declaration and the forced start;
    **stance flips** within 20 turns of the previous change for the same pair (the stickiness check: many means raise
    `stanceStickiness` or `stanceHoldTurns`); where hostility settles (the `Hostility` lines against the 30 midpoint, and which
    term dominates in the `Stance` lines); warship starts per planet-turn while in Amass against while in Consolidate (the
    table's effect), and fleet-cap violations (must stay 0 under Amass); and that `AssaultTarget` lines only name planets of a
    player the assaulting player is at war with. A run from before diplomacy is comparable only with `diplomacyEnabled` false
    (everyone an enemy, no Amass). The wanted fleet of a Consolidate player at peace is lower than before, so fewer warships
    there is expected.
  - **Ship combat:** `Combat|<planet>|<attacker>-><victim>|<damageDealt>|<shipsDestroyed>` (P = the attacker) is an event per
    planet and directed pair each turn they fight, so a standoff repeats; `ColonyShipsLost|<planet>|<owner>|<count>|<byPlayer>`
    per event; `LossDrop|<rival>|<Start or End>|<lossShare>` when a player's loss share crosses 0.3 (state change only);
    `Surrender|<rival>|<lossShare>|<pSurrender>|<truceUntil>` once per surrender; `FleetHealth|<warships>|<damaged>|<meanHealthPct>`
    every 25 turns per player. Report per run: fights (distinct planet and pair, turns each lasts, total damage and ships
    destroyed per attacker; who wins and how fast), fleet size over time against the earlier no-combat runs (warship starts, the
    fleet-cap discipline must still hold with zero violations), colony ships lost and where (and whether expansion slowed:
    `ColonizeStart`/`ColonizeArrive` against the diplomacy-orders baselines), how often `LossDrop` starts and how long it lasts,
    surrenders (count, the `lossShare` and `pSurrender` at the time, the truce, whether a new declaration followed within 30
    turns: it must not), and `FleetHealth` (does the damaged share and the mean health recover between fights, i.e. does repair
    keep pace). Also re-read hostility saturation (the 25-turn `Hostility` lines against the earlier baselines): ship losses
    now feed it. The surrender terms are tuning levers to revisit with the diplomacy tuning, not yet tuned.
    **Since 2026-10-06 `lossShare` is the strength drop I took from the rival (damage included) / strength ENGAGED against the rival over the window** (the `Hostility` line's
    last field `engagedStrength` is its denominator; guard `NF >= 10`; older logs divide by the whole fleet and read 0.02-0.14,
    so the two are not comparable, and the surrender terms need recalibrating to the new range).
  - **Retreat:** `Retreat|<planet>|<destination>|<tier>|<ships>|<projectedLossPct>|<rivalSurvivorsPct>|<routeCost>|<cooldownUntil>|<pRetreat>|<exactLossPct>`
    (since 2026-10-06 `projectedLossPct` is the PERCEIVED loss, the mean over the imperfect-intel samples, and `exactLossPct` is
    the exact projection's; older logs have no last field, so guard `NF`; with imperfect intel retreats should no longer all sit at
    100% loss and `pRetreat` should spread, and the destination is a weighted pick so one planet should not take every retreat;
    repeat retreats from one planet within 10 turns should fall now that a planet in its cooldown is not refilled)
    (tier 3 appends `|<rememberedBlockade>|<myOffense>`) is one line per retreat order; `RetreatStay|<planet>|<projectedLossPct>|<pRetreat>`
    and `RetreatHeld|<planet>|<NoDestination or OwnPlanetNotWiped>|<projectedLossPct>` are on-change lines for a gated fight whose
    ships stayed (the roll kept them, or no destination existed); `RetreatArrive|<planet>|<rememberedBlockade>|<rivalOffense>`
    follows a tier 3 retreat when it lands (the second value is the largest single rival's docked offense on landing; logs from
    made before the RetreatArrive fix on 2026-10-06 logged the blockade value instead, which reads 0 once my ships dock, so they cannot show staleness). Report per run: retreats per player and per tier (tier 3 should be rare), the
    projected loss at which retreats happened against the `RetreatStay` losses (is the curve too timid or too eager: a
    `RetreatStay` at a high loss followed by `Combat` destroying that player's ships on the planet within 3 turns is a retreat the
    roll declined), `RetreatHeld NoDestination` counts, the stale-blockade gap in `RetreatArrive`, ships preserved (warships at T300
    and `FleetHealth` against the earlier combat baselines; ships destroyed per run should fall), and that no planet named in a
    `Retreat` line becomes an `AssaultTarget` or `BlockadeTarget` of that player within `cooldownUntil`.
  - **Invasion (conversion):** `ConversionStart|<planet>|<fromPlayers>|<myPop>|<totalPop>|<turnsPerFlip>` begins a session (P = the dominator;
    also logged once more per session found after a load), `Convert|<planet>|<fromPlayer>|<AtWar or NonWar>|<myPop>|<totalPop>|<progress>`
    is one converted inhabitant, `ConversionEnd|<planet>|<Clean, DominanceLost or WarEnded>|<turnsHeld>|<converted>` ends it,
    `OwnerChanged|<planet>|<oldOwner>|<newOwner>|<clearedItem or ->` (P = the new owner) is an ownership change, `PlayerOutOfPlanets|<player>`
    is logged once per player, `ConversionHoldSpare|<planet>|<heldShips>|<heldOffense>|<shipsACallWanted>|<Garrison, Assault, Blockade or ->|<callTarget or ->|<rivalOffenseNearby>|<progress>|<keep>|<released>`
    is on change only (since 2026-10-06 the hold releases ships down to `keep`, so `released` is what the real plan sent away and
    `shipsACallWanted` is still the full demand; older logs have no last two fields, guard `NF`), `ConversionColonize|<origin>-><target>|<myPop>|<totalPop>|<routeCost>|<nearestTarget>|<nearestCost>` is per colonist
    launched at a dominated planet, and `Stance`/`Hostility` lines end with `conversionTerm`. Report per run: sessions started, ended by
    reason (Clean against DominanceLost against WarEnded: many DominanceLost means holds or retreats are letting go), turns held and
    flips per session, the pace (turns between `Convert` lines on a planet against `turnsPerFlip`: does the snowball show), planets
    changing hands per player and the clearedItem on `OwnerChanged`, non-war conversions (`NonWar`) against the `conversionTerm` on the
    victims' later `Stance ... War` lines (are they declaring because of it), `ConversionColonize` launches per session and whether the
    session's pace sped up after one, and the **option 3 evidence**: for each `ConversionHoldSpare` with `shipsACallWanted` above 0, how
    long it lasted and how large `rivalOffenseNearby` was against `heldOffense` (a long call with no rival nearby means offense-scaled
    conversion or a smaller hold would have served). **Partial release (2026-10-06):** per run, `released` against `shipsACallWanted`
    (how much of the demand the release met), the `released` ships' arrival at the call target (a `ShipArrive` or a `BlockadeForce`
    wave from that planet), and whether the sessions that released still finished: Clean share for sessions with `released > 0` against
    those with none, and against the pooled 23% before the change (a sharp fall means raise `conversionHoldKeepFraction`; many
    `DominanceLost` within 5 turns of a release means the rival came back). Also: no `Convert` line may name a planet whose session was not Started or
    re-Started after a load, `PlayerOutOfPlanets` counts, and that warship starts and the fleet cap still hold (a conquered planet's
    production was cleared, so no free ships appear).

## 4. Compare against a previous run when one exists

If the user is testing a specific change (a tunable, a wiring fix, a board), find the most recent
*previous* log on the **same board** (check `BoardConfig`) to diff against — same-board comparisons
are far more meaningful than cross-board ones. State plainly which prior run you're comparing to and
why (same board, same code state) or why not (different board — note it and don't force a comparison
table that isn't apples-to-apples). When the previous runs predate a mechanism or a tuned value (compare the log
file timestamps with `git log --format='%h %ad %s' --date=format:'%m-%d %H:%M'`), say the comparison is only
directional and name the code difference; a baseline made after the last tuned value and before the change under
test is the clean one.

## 5. Report

Lead with anything surprising or actionable (a mechanism that didn't do what was intended, a player
that fell behind and why, an invariant that broke), not a wall of raw numbers. Use comparison tables
when there's a previous run. Investigate one level deeper than the raw counts when something looks
odd — e.g. "P2 has fewer colonies" is a number; "P2 never got a single Desolate or Verdant planet, and
its Prime only had 2 connections" is the reason. Keep terminal output compact: run the analysis
scripts, don't paste raw log excerpts into the reply.
