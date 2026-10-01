# Handoff: Resource Blockade Tuning

Saved from the Claude Code session named **"ResourceBlockadeTuning"** (session ID
`64b1e9c2-433d-4d62-8c8a-4214a6653106`), on 2026-10-01; the work was done on 2026-09-30. Resume it with `/resume`
(pick it) or `claude --resume "ResourceBlockadeTuning"`. This file is the written version so nothing depends on that
history.

Read `CLAUDE.md` first (architecture, conventions, the self-check pattern, the "Warships and Blockade" and "AI Tuning
Log" sections). Spec and plan for the shipping work: `docs/superpowers/specs/2026-09-30-blockade-avoidance-shipping-design.md`
and `docs/superpowers/plans/2026-09-30-blockade-avoidance-shipping.md`. The earlier sub-project 1 spec is
`2026-09-29-blockade-avoidance-colonization-design.md`.

## Where things stand

**Asked:** start blockade sub-project 2 (resource shipping avoids blockaded routes), then, as tuning logs came in,
add a minimum delivered fraction for lossy shipments, richer logging, and analyses of the runs (including why P3
lags on `4p.json`). **Sub-projects 1 and 2 are done, merged and pushed.** Sub-project 3 (assault breaks blockades)
is next and has not been started.

| Area | What it does | Key code |
|---|---|---|
| Shipment route planning | `PlanShipmentRoute`: the clean route (shortest, else `PlanRoute`'s clean detour) if one exists, otherwise the least-loss route then the cheapest; the origin's and target's blockades are ordinary nodes, their value counts as loss | `RoutePlanner.PlanShipmentRoute`, shared `Search` core, `PlannedRoute.Loss`, `BlockadeView.WithValues` |
| Matrix rule | a (source, shortage) pair is offered only if `loss < amount` and `amount - loss >= fraction x amount`; clean routes sort before lossy ones | `PlayerAI.BuildResourceMatrix`, `CompareResourceChoices` |
| Origin cut | a food/grotsits shipment is cut at its blockaded origin once, on its first processed turn (`TotalDelay > 0`, `TimingDelay + 1 >= TotalDelay`); colonists unchanged | `BlockadeSystem.ApplyToShipment` |
| Route on orders | shipment transport orders carry `Route`; the delay uses the route cost | `PlayerAI.EmitResourceOrders` (reuses `GameAIOrder.Route`, `OrderSave.route`) |
| Minimum delivered fraction | `GameAIConstants.shipmentMinDeliveredFraction`, in-code default **0.5** (0 = plain rule, 1 = clean routes only); read through the per-target seam | `PlayerAI.MinDeliveredFractionFor` |
| New log lines | `ShipmentLossy`, `ShipmentCancelled|<target>|<Blockade or LowYield>|<source>|<loss>|<amount>|<blocked planets>`, `GrotsitsShort|<planet>|<Start or End>|<pop>|<capacity>|<upkeep>|<morale>`; `RouteDetour` is shared with colonists | `AITuningLogger`, `PlayerAI.ShipmentHold`, `GrotsitsShortTracker`, `GameAI.LogGrotsitsShortChanges` |
| Self-checks | `BlockadeAvoidanceSelfCheck` (planner, origin cut, shipment planning, min fraction, cancel detail), new `GrotsitsShortSelfCheck`, both inside Run All AI Self-Checks | `Assets/Editor/` |
| tuning-log skill | many new analyses (see below) | `.claude/skills/tuning-log/SKILL.md` |

### What was verified, and how
- You ran the Editor self-checks after each stage and reported "checks pass" (compile and `Run All AI Self-Checks`).
- Test runs on `test2.json` and `4p.json` were analysed with the skill:
  - Blockade cuts fell from about 234 to 16–21 per `test2.json` run and from about 435 to 15–25 per `4p.json` run.
  - Food and grotsits delivered ÷ sent rose to about 0.99.
  - Every lossy shipment now delivers at least 50% (it was 2–6%, about 12% on average, before the fraction).
  - Colonist cuts are unchanged and are all in-flight cases (sub-project 5).
  - Fleet-cap violations: 0.
- A fresh-context whole-branch review of the shipping work found one Important issue, which was fixed (see commits).

### What was not verified
- I could not run Unity myself (Editor open, Pipeline package not installed, batch mode blocked by the project lock; installing
  the package would edit `Packages/manifest.json`). I wrote each test first and used Rider's `get_file_problems` as the
  compile signal (RED = errors for the missing members), and hand-traced assertions. Your Editor runs are the real verification.
- The `ShipmentCancelled` detail and `GrotsitsShort` lines were verified only through later logs you ran (they appeared with
  the expected fields), not by a dedicated check of every format.
- The worker-allocation diagnosis (below) comes from reading `Planet.AssignWorkForStrategy` and the resource data assets, and
  is consistent with the logs, but worker counts were never seen at runtime.

## Files and commits (all on `main`, pushed to `origin`)

| Branch (deleted after merge) | Commits | Merge (pushed range) |
|---|---|---|
| `blockade-shipping` | spec `bb7b44a`; planner `1be1b86`; origin cut `15e22f3`; shipment planning `1504964`; docs + plan `95fcbce`; held-back pruning fix `a02267e` | `dac47ac` (`ec48299..dac47ac`) |
| `shipment-min-delivered-fraction` | tunable `8d91468`; skill additions `e842a98`, `0fd4973` | `f1fda0e` (`dac47ac..f1fda0e`) |
| `tuning-log-churn-analysis` | `5742c14`, `4a35131`, `17e06d4` | `d7d014b` (`f1fda0e..d7d014b`) |
| `shipment-cancel-log-detail` + `grotsits-short-logging` | `d77e282`, `3abf76e` | `57d8bfb` (`d7d014b..57d8bfb`) |
| `future-features-industry` | `431fc71`, `541e3a2` | `4582bc3` (`57d8bfb..4582bc3`) |

New scripts (with their `.meta`, committed): `GrotsitsShortTracker.cs`, `GrotsitsShortSelfCheck.cs`. The stale branches
`concrete-ship-representation-design`, `feature/ship-transport` (local) and `origin/concrete-ship-representation-design`,
`origin/feature/player-knowledge` were deleted at your request (all were already fully merged); only `main` remains.
`a5fedde` (Desert base food boost, run editor in background) is your own commit; the later `0d39bce`, `3500c68`,
`ec1417f` (feature-list housekeeping, `completed_features.md`, the `/handoff` command) came from a separate session. This
file is not committed.

## Decisions and why

- **Reuse sub-project 1 wherever possible** (you asked): `PlanRoute`, `BlockadeView`, `GameAIOrder.Route`, `RouteDetour`, the
  held-back log pattern. The one new piece was a least-loss search, done by generalising `ShortestPathAvoiding`'s Dijkstra into a
  shared (loss, cost) `Search` core (your choice, option A over a separate Dijkstra).
- **Source and target are ordinary blockaded nodes, not dropped outright** (your change); a pair is cancelled when
  `loss >= amount` (my recommendation, accepted: exact equality delivers nothing). To keep simulation and AI consistent the
  shipment origin is now actually cut (your choice). Economy numbers on boards with lasting blockades are not comparable with
  runs from before this change.
- **Detours may exceed `maxPathNodesForResourceDistribution`**, but the target must still be in range by its shortest path.
- **Minimum delivered fraction default 0.5**, with `MinDeliveredFractionFor(target)` as the seam for a future per-target
  threshold for high value planets (recorded in `FUTURE_FEATURES.md`).
- **Planet churn is desired**: the AI repeatedly colonizing planets it cannot currently supply (for example `Industrial 1`
  behind a contested `Verdant 0`) is intended. You rejected my supply-reachability gate ("ignore item 2, the current
  behavior is desired"); it is in the `contested-planets-are-desired-not-bugs` memory and the skill says to report it
  neutrally.
- **You declined more logging for P3** ("I have enough of an idea"): no stock/worker fields were added to `GrotsitsShort`.
- **Industry ideas and the worker-allocation analysis were only recorded** in `FUTURE_FEATURES.md` (you said not to do
  anything yet).
- **Process:** implemented natively (no per-task subagents) with one final whole-branch review on the most capable model; feature
  branches in the working directory (not worktrees, because your Editor reads this tree).

## Analyses added to the tuning-log skill

Lasting occupations, who blockades whom (and the `AssaultTarget` link), colonist cuts against the blockade memory, contested
planets (cross-player recolonization), weak start, verifying new log lines, directional baselines, shipment delivery, lossy
shipments (delivered share, origin/target/mid-route/unexplained cuts), detour lanes, cancelled episodes per target, variance with
few runs, colonize-die churn with the grotsits/food ratio for churn planets, and grotsits shortage (which planets, when).

## Findings from the logs (`test2.json` and `4p.json`, 2026-09-30)

- **P3 on `4p.json`** is the most grotsits-short player (2–4x the others' short planet-turns). It is not capacity-limited (0 of
  909 episodes with demand above capacity) and not blockade-driven in the latest runs; it is an early shortage -> morale <
  100 -> fewer colony ships loop. Across 12 runs, weak P3 starts had 4.5 planets short at T100 against 1.2 in healthy ones and
  built 6–9 colony ships by T125 against 9–15. Its home `Prime 3` has 5 connections, three adjacent Farms and no Desolate (the
  grotsits producer) within 2 hops; the nearest, `Desolate 3`, is 3 hops away and P0 often takes it first.
- **Why Industrial planets starve (from the code):** the Industry strategies allocate Food, Industry, Research, Grotsits, and
  research absorbs every worker left, so grotsits gets 0 workers (modelled: 1 grotsits made against a demand of 11.5 at
  population 10). Farms and Verdant have a structural 2–3 per turn deficit (grotsits rate 1, base 0). Each planet type's base
  production does not change this; the worker requirements ignore base, so each planet assigns about one worker too many.
- **`test2.json`**: `Industrial 1`/`Verdant 0`/`Desolate 0` showed colonise-die churn (`Verdant 0` 33 deaths, `Industrial 1` 30
  in one set) driven by a rival (P2) occupying the `Verdant 0` hub from about T129; the pre-tunable runs showed the same pattern
  when P1 attacked it. `Desolate 0` exports about 10 grotsits per food received (about 6 counting colony riders).

## Open items, most important first

1. **Sub-project 3: assault breaks blockades** (you asked to start here next; see the `next-start-blockade-subproject-3`
   memory). Target visibly blockaded planets with offense-based force sizing (dock enough offense that the value is <= 0), include
   the warship research-priority change you asked for, and optionally prioritise planets that cut an order in the last N turns
   (tunable, default 5; `BlockadeMemory` / `RememberedBlockades` already record planet, value, turn). Use brainstorming first, and
   mention the connectivity-weighting idea and `tools/planet-centrality.ps1` before designing the target scorer (a memory note says
   to). Sub-projects 3 to 5 are one "unit" (milestone): 4 = connectivity for garrison and colonization targeting, 5 = in-flight
   colonist avoidance.
2. **Option A worker allocation** (recorded in `FUTURE_FEATURES.md`, not built): reorder the two Industry strategies to Food,
   Industry, Grotsits, Research, strategy-agnostic. Validation plan: a self-check that an Industrial planet at population 10 and
   stock 0 has `GrotsitsWorkers == 0`, then the swap, then compare P3 short planet-turns, morale and `ResearchComplete` across three
   `4p.json` runs. Optional second step: subtract base production from the food and grotsits requirements. Cost: Industrial
   planets lose most of their research.
3. **Tune `shipmentMinDeliveredFraction`** from logs once more runs exist (watch `LowYield` episodes per target and whether any
   target starves); later make it per target for high value planets (`MinDeliveredFractionFor`).
4. **Four deferred minor review findings:** `CLAUDE.md` still says blockade memory is used only by colonization routing (it also
   feeds shipment planning through the view); `RoutePlanner.Search` compares summed float losses with exact `==` (an epsilon would
   make ties break by cost/name as specified); two test gaps (clean-beats-lossy only tested on the comparer, not through two real
   sources; a blockade off the detour not tested for a food shipment).
5. **Industry production centers / shippable industry** is recorded in `FUTURE_FEATURES.md` and not started.

## Gotchas for a fresh session

- **No automated runner:** Unity can't be driven from Claude Code here (Pipeline package not installed; installing it edits
  `Packages/manifest.json`). Ask the user to focus the Editor and run `FlatSpace -> AI -> Run All AI Self-Checks`.
- **Rider diagnostics** work as a compile signal for edited files, but a brand-new script reports "not included in any project"
  until Unity regenerates the project. I generated the two new `.meta` files myself (just a `fileFormatVersion: 2` and a GUID),
  which Unity may rewrite on first import (harmless, nothing references them yet).
- **`Scenario.Diamond()`** in `BlockadeAvoidanceSelfCheck` sets `shipmentMinDeliveredFraction = 0` so the older shipment checks (any
  loss < amount ships) still hold; the default 0.5 is asserted separately. Never put a self-check exactly on the
  `fraction x amount` boundary (float caveat).
- **`PlayerAI.ProcessFoodShortage`/`ProcessGrotsitsShortage` are public only for the self-check** (a self-check can't call
  `internal`).
- **Log analysis details:** the AITuningLogs for the runs above are gitignored. Yesterday's `test2.json` runs (19:52–19:54) used
  `blockadeMemoryTurns` 10 and no shipment avoidance; the `4p.json` runs from 00:13–00:16 already had memory 20, so they are the clean
  baseline for the shipping change. `14-46-14` was an aborted run (T37); the first `GrotsitsShort` logs are `14-47-38`,
  `14-48-49`, `14-50-24`. The ad-hoc analysis scripts lived in the session scratchpad and are not in the repo; the skill describes
  the analyses instead.
- **Shell editing:** long Python heredocs broke on bash quoting in this environment; edits were done by writing small scripts to the
  scratchpad. `git` warns about LF/CRLF on every commit; it is harmless.
- **Things you said to avoid:** do not propose a colonization supply gate (churn is desired); do not add more P3 logging; the
  industry ideas and Option A are recorded only, do not start them unprompted.
