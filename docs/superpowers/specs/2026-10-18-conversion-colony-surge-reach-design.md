# Colony ship surge: reach, same-turn cap and a held-launch log (design amendment)

Date: 2026-10-18. Amends `docs/superpowers/specs/2026-10-18-conversion-colony-surge-design.md` (merged as `d1ec4eb`), whose rules all stay unless
this document says otherwise. Evidence: the first Play batches after the merge (6 runs of `test2.json`, 19:35 to 19:43, and 6 of `4p.json`,
19:52 to 20:06, read with `/tuning-log`).

## Why

The surge is almost inert. Per run it logs 8 to 46 `ConversionSurge` lines, but `SurgeBuild` totals 1 (`test2.json`) and 3 (`4p.json`) over six runs
each and `SurgeLaunch` totals 4 and 0. Outcomes did not move (real conquests finishing 16 vs 19% on `test2.json`, 20 vs 15% on `4p.json`, ranges
overlap). Causes the logs show:

1. **Reach.** About 70% of plan lines on `test2.json` (81 of 116) and 40% on `4p.json` had no serving neighbour of mine: a dominated planet's
   direct neighbours are usually not mine.
2. **Time.** A plan lasts about 10 to 20 turns; a colony ship started for it often completes after the conversion ended.
3. **Silent skips.** A surge launch dropped because the target has no route (for example it is blockaded in the player's view) is not logged, so the
   cases where a ready server did not send (one on `4p.json` at `Desert 4`, T215 to T222; one 10-turn delay on `test2.json` at `Farm 6`) cannot be
   explained from the log.

It also exposed a gap in the owner's cap rule (below).

## Goals

1. **Wider reach.** A dominated planet D is served by my planets within `conversionSurgeReachNodes` path nodes of it (default 3: itself, one planet
   between, the server), taken from the stored all-pairs paths (`Planet.DistanceMapToPathingList`: `2 <= NumNodes <= reach`) instead of only the
   direct neighbours from `GameAIMap.GetNeighbours`. A value of 2 is exactly today's behaviour. Everything else about serving is unchanged: a server
   is claimed by one target only (the larger wanted count first, then name), pipeline counts the claimed servers' docked and in-production colony
   ships, the launch budget is EW, cheapest route first.
2. **The cap holds within one turn.** Today every serving planet sees the same snapshot pipeline, so several of them can each pick ColonyShip in the
   same production pass and push `population + en route + pipeline` past max population (the owner's rule). After a surged ColonyShip start is emitted,
   the plan's `Pipeline` for that target is raised by one; a later surged ColonyShip pick in the same pass on a server of a target whose
   `Pipeline >= EffectiveWanted` is dropped (that planet starts nothing this turn and re-checks next turn, as a planet with no choices already does).
3. **Held launches are logged.** `SurgeHeld|<origin>|<planet>|<BlockadedOrigin, NoRoute or OutOfRange>` on change only: a ready server of a surge target
   that could not launch (its own planet is blockaded; no route to D round the blockades it can see, which includes D itself being blockaded in its
   view; D beyond `maxPathNodesForColonization`). Reuse the log-only hold-back tracking `ProcessColonizers` already has (`NoteColonizeHeldBack`) so a
   state is logged once, not every turn.
4. **The plan line says how many servers it has.** `ConversionSurge` gets a last field `<servers>` (older logs lack it; guard `NF`), so a log reader no
   longer has to infer ownership from other lines.

## Non-goals

- No change to wanted, EW, room, the x3 production multiplier, the readiness rule or the one-colonist-at-a-time handling.
- Docked ships on an unready server keep counting in the pipeline. I had floated dropping them; the owner's cap rule needs them counted (a ship that
  will launch when its planet is ready is committed room), so the surge pausing while such a ship waits stays, and `SurgeHeld` will not report it.
- No new order type, no save change.

## Decisions

Owner's (earlier): the surge exists, adjacent planets serve, a docked ship goes to D first, readiness stays, the cap rule.
Mine, to check:
- **Default reach 3 path nodes** (two hops), a tunable; 2 reproduces the merged behaviour. Chosen because a dominated planet usually sits one planet
  beyond my border; the first Play batch will show whether 3 is enough.
- **Servers are taken by path-node count, not cost**, for the same reason `maxPathNodes*` limits are: the existing range rules use nodes.
- **Same-turn reservation drops the later pick instead of reducing its weight**, because the weight was already used to make the pick; dropping is the
  smallest change and mirrors "a planet left with no choices starts nothing".
- **Claim order stays "largest wanted, then name"**; with a wider reach one server can be in range of several targets and this keeps a ship counted once.

## Tuning log output

- `SurgeHeld|<origin>|<planet>|<BlockadedOrigin, NoRoute or OutOfRange>` as above (on change only; log-only state in `PlayerAI`, so a load logs each
  current one once more). Answers: why a ready server did not send.
- `ConversionSurge|...|<totalPop>|<servers>`: a trailing field (the count of claimed servers). Answers: how often a plan has nobody to serve it, and
  whether the reach value is enough.
- Update the `tuning-log` skill (the surge bullet: report servers per plan, the share of plans with 0 servers against the 70% and 40% baselines,
  `SurgeHeld` counts by reason, `SurgeBuild`/`SurgeLaunch` per run against 0.2 and 0.7 on `test2.json` and 0.5 and 0 on `4p.json`) and the "AI Tuning
  Log" and surge bullets of `CLAUDE.md`.

## Tests (extend `Assets/Editor/ConversionSurgeSelfCheck.cs`)

- Reach: a server two hops from D is claimed at reach 3, not at reach 2; a server three hops away is not; a planet in range of two targets is claimed
  by the larger wanted only; `For()` and `Servers` agree; a unowned or rival planet in range never serves.
- Same-turn cap: two servers of one target with EW 1 both pick ColonyShip in one pass: exactly one production start is emitted and `Pipeline` rose by
  one; with EW 2 both start. (Drive `BuildIndustryMatrix`/`EmitProductionOrders` the way `PlayerAIResourceSelfCheck` drives the industry matrix.)
- `SurgeHeld`: a ready server whose D is blockaded in its view logs `NoRoute` once and again only after a change; a blockaded server logs
  `BlockadedOrigin`; a server beyond `maxPathNodesForColonization` logs `OutOfRange`; a server that launches logs nothing.
- Defaults: `conversionSurgeReachNodes` is 3; the plan line formatter carries the servers field.

## Left to the builder

How the reach lookup is written (a helper on `GameAIMap` or inside `ConversionSurge.Build`), where the same-turn reservation is kept (it must reset
with each `RefreshSurgePlan`), and the exact wiring of the held-launch reasons into `NoteColonizeHeldBack`.
