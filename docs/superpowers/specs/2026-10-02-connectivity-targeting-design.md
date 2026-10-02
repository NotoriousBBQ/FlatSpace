# Connectivity for garrison, blockade and colonization targeting (blockade unit, sub-project 4)

Date: 2026-10-02. Roadmap entry: `FUTURE_FEATURES.md`, "AI avoids blockaded routes and uses blockades", item (4), and
"Weight planet value by connectivity". Earlier specs: `2026-10-01-blockade-breaking-design.md` (sub-project 3, which left
connectivity out of its ranking on purpose).

## Goal

Build one shared, tested, shortest-path betweenness score in C# (the offline version is `tools/planet-centrality.ps1`) and use it
in the three places that ignore a planet's connectivity today:

1. the garrison "high traffic" category in `ShipTransportPlanner` (a plain neighbour count today);
2. the blockade target ranking in `AssaultPlanner.ChooseBlockadeTarget` (built without connectivity on purpose);
3. colonization target choice under Consolidate (path cost alone today).

Success: chokepoints (for example `Industrial 4` on `4p.json`, 4th of 100) are garrisoned under Consolidate, rank higher among
otherwise tied blockade targets, and are preferred, mildly, as colonization targets under Consolidate; every effect can be switched
off by a tunable, and the tuning log shows whether each one matters.

## Decisions already made with the owner

- Scope is "B": the score, the blockade ranking, the garrison category, and a Consolidate-only colonization tilt. Distribution Center
  selection and the per-target shipment threshold stay out.
- **Producer value is not built here.** It is agreed (by planet type, authored as data: Verdant 1.0, Desolate 1.0, Farm 0.5, others 0)
  and recorded for later tuning (memory note `producer-value-per-type-deferred`). The design keeps chokepoint value as its own
  component with its own tunables so a producer component can be added beside it later.
- Compute from the stored all-pairs paths (approach 1), not a second shortest-path implementation (Brandes); score as a percentile.
- Garrison: option B (swap the category 4 test to chokepoints, and under Consolidate also garrison own chokepoints at round 1).
- **The spare-ship calculation is not changed** (the owner said not now).
- The blockade ranking's committed-offense step is not changed (a large committed force outranking small blockades is desired).

## Design

### 1. `PlanetCentrality` (new, pure)

`Assets/Flatspace/GameAI/PlanetCentrality.cs`, namespace `FlatSpace.AI`, no `Gameboard.Instance`.

- Input: the planet names and a sequence of paths (each a list of node names). Each unordered planet pair appears once.
- For each path, every node except the two endpoints gets +1 betweenness. A path with fewer than 3 nodes adds nothing, which also
  covers `FindPath`'s 1-node no-route stub.
- Output: raw betweenness per planet (`int`) and a percentile per planet: `(number of planets with strictly lower betweenness) / (N - 1)`.
  Planets with equal betweenness share a score, leaves score 0, and a board of 0 or 1 planets scores 0 for every planet.
- Wiring: `GameAIMapInit` runs it once, after the all-pairs table is built (the `_planetPathings` list holds each pair's
  `Path1To2`). `GameAIMap.Betweenness(name)` and `GameAIMap.Chokepoint(name)` (the percentile; 0 for an unknown name) expose it.
  Derived from static board data, never saved, recomputed on every init and load.
- It reads the paths ships actually use, so it agrees with the game by construction. Ties between equal-cost paths go to whichever
  path `FindPath` stored (the script has the same property).

### 2. Garrison (`ShipTransportPlanner`)

- **Category 4** becomes "chokepoint percentile >= `GameAIConstants.chokepointPercentile`" (new, in-code default 0.9, i.e. the top
  10% of the board). It replaces `GetNeighbours(name).Count >= highTrafficConnectionCount`; that tunable is removed after checking the
  assets and self-checks for it. The category's garrison keeps its name and value (`garrisonHighTraffic`).
- **`MaintainsGarrison`** (the existing seam) becomes: Expand garrisons every planet (unchanged); Consolidate garrisons an outer planet
  **or a colonized chokepoint** (percentile at or above the same threshold), at round 1 only as before.
- Not changed: how spare ships are computed, `HeldPlanets`/`HeldPlanet`, the category 5 lock, `TargetRank`.

### 3. Blockade target ranking (`AssaultPlanner.ChooseBlockadeTarget`)

- A new step, **higher chokepoint percentile first, counting only chokepoints (planets below `chokepointPercentile` all tie at 0, so a value above 1 switches the step off; added after the final review, which found the raw percentile had no off switch and decided nearly every uncommitted tie)**, goes after the recent-cut step and before the smallest-offense-needed step.
  Order: committed offense, recent cut, **chokepoint**, smallest offense still needed, cheapest path, name.
- A new reason label `Chokepoint` (beside `Committed`, `RecentCut`, `Cheapest`) is set when that step is the first difference, and
  shows in the `BlockadeTarget` log line.
- Expectation, not a defect: committed offense decides 83-96% of targets in the logs, so this step only decides among tied
  candidates, mostly fresh blockades with no committed force.
- **Recorded tuning alternative (not built):** if the logs show the step is too weak, move it **ahead of the recent-cut step**
  (committed offense, chokepoint, recent cut, ...). The owner asked for this to be remembered as a tuning adjustment.

### 4. Colonization tilt (`PlayerAI.ProcessColonizers`, Consolidate only)

- The choice cost fed to `ScoreMatrix` becomes `route.Cost / (1 + colonizationChokepointWeight x percentile(target))`. Weight is a new
  `GameAIConstants` tunable, in-code default 0.5 (a top hub may be 50% farther and still tie with a nearer planet); 0 switches the tilt
  off. Expand is unchanged (nearest first).
- **Order delay must use the real route cost.** The delay is computed from `action.Cost` today; with a tilted cost that would make a
  hub flight faster than its route. Take the delay from the planned route's `Cost` (already looked up per action).
- `PlanetHasColonizationTarget`, validity and reachability rules, `CanSupportColony` and the blockade route planning are untouched.
- Known trade-off, accepted: hubs are contested and usually farther, so a longer flight means more exposure to cuts; the weight is
  mild for that reason. Colonize-and-die churn is desired and is not gated.

### 5. Proposed tuning log output

| Line | Fields and when | Question it answers | Repeat control, task, test |
|---|---|---|---|
| `T0\|P-1\|Chokepoints\|<planet>=<betweenness>%<percentile>,...` | the top 10 planets by betweenness, once per match at board init (the last `InitGame` is the real one, as for `BoardConfig`) | Which planets count as chokepoints on this board, without running the script | once per init; logged where `BoardConfig` is; format covered by the self-check's formatter if one is added, else by a real run |
| `T<turn>\|P<id>\|ChokepointColonize\|<origin>-><target>\|<routeCost>\|<percentile>\|<nearestTarget>\|<nearestCost>` | each colonist launched under Consolidate whose tilted pick differs from the nearest candidate by route cost | How often does the tilt change a colonization choice, and how much farther does it reach | one line per launch, no tracker needed |
| `T<turn>\|P<id>\|ChokepointGarrison\|<colonized>\|<boardTotal>\|<shipsOnThem>\|<allShips>` | per player every 25 turns, beside `Economy`; colonized = chokepoints the player colonizes, boardTotal = chokepoints on the board, shipsOnThem = its docked warships on those, allShips = its docked warships everywhere (renamed from `Chokepoints` so the two lines do not share a code; a spare count would need planner state, and the share of ships on chokepoints answers the same question) | Do chokepoint garrisons take ships from the assault | every-25-turn cadence like `Economy`, so no tracker |

Also: the `tuning-log` skill (`.claude/skills/tuning-log/SKILL.md`) gains the three lines and an analysis for them (the existing
"betweenness rank from `planet-centrality.ps1`" step can read the `Chokepoints` line instead), and the "AI Tuning Log" and the
Ship Transport / Warships and Blockade / Player Knowledge-style sections of `CLAUDE.md` are updated, including removing the "connectivity
is deliberately not in the ranking yet" sentence.

### 6. Tests (`Assets/Editor/ChokepointSelfCheck.cs`, registered in `AllAISelfChecks`)

Plain assertions, no `Gameboard.Instance`, distinct planet positions (A* tie note), nothing exactly on a float boundary:

- `PlanetCentrality`: a line (interior nodes score, endpoints 0), a star (hub top, leaves 0), tied betweenness shares a percentile,
  the no-route stub and short paths add nothing, N of 0 or 1.
- Garrison: a chokepoint planet gets category 4 at or above the threshold and not below it; under Consolidate a non-outer chokepoint
  maintains a garrison and a non-outer non-chokepoint does not; Expand is unchanged.
- Ranking: two blockade candidates tied on committed offense and recent cut are split by chokepoint (reason `Chokepoint`); committed
  offense and recent cut still outrank it.
- Colonization: under Consolidate a farther hub beats a nearer leaf at the default weight; weight 0 and Expand both pick the nearest;
  the order delay equals the route cost over travel speed, not the tilted cost.
- Existing suites keep passing, in particular the Ship Transport and Blockade Breaking checks that referenced the old category 4 test.

## Out of scope

Producer value (see the memory note), the spare-ship calculation, Distribution Center selection, the per-target shipment delivery
threshold, moving warships to impose a blockade, sub-project 5 (in-flight colonist avoidance), and any change to the committed-offense
rule or `ContestedHolds`.

## Files

New: `PlanetCentrality.cs` and `ChokepointSelfCheck.cs` (each with its `.meta`). Changed: `GameAIMap.cs`, `GameAIConstants.cs`,
`ShipTransportPlanner.cs`, `AssaultPlanner.cs`, `PlayerAI.cs`, `AITuningLogger.cs`, `GameAI.cs` or `Gameboard.cs` (the log call
sites), `AllAISelfChecks.cs`, `ShipTransportSelfCheck.cs` (the old category 4 test), `CLAUDE.md`, `FUTURE_FEATURES.md`,
`.claude/skills/tuning-log/SKILL.md`.
