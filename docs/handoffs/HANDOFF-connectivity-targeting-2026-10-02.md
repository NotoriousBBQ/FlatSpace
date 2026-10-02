# Handoff: Connectivity Targeting

Saved from the Claude Code session named **"connectivity-targeting"** (renamed with `/rename` near the end of the work),
on 2026-10-02. All the work was done on 2026-10-02. Resume it with `/resume` (pick it) or
`claude --resume "connectivity-targeting"`. This file is the written version so nothing depends on that history.

Read `CLAUDE.md` first (architecture, conventions, the self-check pattern, the new "Connectivity (chokepoints)" section,
"Warships and Blockade", "AI Tuning Log"), then `FUTURE_FEATURES.md` (the session rules in `docs/session_configuration.md`
require it before any design). Spec and plan:
`docs/superpowers/specs/2026-10-02-connectivity-targeting-design.md` and
`docs/superpowers/plans/2026-10-02-connectivity-targeting.md`. The earlier handoff is `HANDOFF-blockade-breaking-2026-10-01.md`.

## Where things stand

**Asked:** start blockade sub-project 4 ("connectivity for garrison and colonization targeting") with the brainstorming
skill, and examine whether a connectivity test also makes sense for colonization decisions; then write the spec and plan,
execute natively, run Play-mode tuning runs, and merge and push. **Sub-project 4 is done, merged to `main` and pushed**
(`origin/main` is at `f1560a5`; the merge itself is `2e3ec5a`). The local branch `connectivity-targeting` was deleted at your
request. After the merge you also asked for: a `chokepointPercentile` 0.8 against 0.9 comparison (result: keep 0.9, see Decisions),
the review's doc minors, `/update_feature_list` (sub-project 4 is now in `completed_features.md`), deferring the remaining open
items, and an exit-hook change. Sub-project 5 (in-flight colonist avoidance) is the only item left in the blockade unit and has
not been started.

| Area | What it does | Key code |
|---|---|---|
| Betweenness score | for every planet, counts the stored all-pairs shortest paths that pass THROUGH it (endpoints excluded; a stub or 2-node path adds nothing), as a percentile: strictly lower betweenness / (N-1); built once in `GameAIMapInit`, never saved | `PlanetCentrality` (new, pure), `GameAIMap.Betweenness/Chokepoint/IsChokepoint/TopChokepoints/ChokepointSummary` |
| Chokepoint | betweenness > 0 and percentile >= `chokepointPercentile` (default 0.9; above 1 means none) | `GameAIMap.IsChokepoint` |
| Garrison | category 4 is now "is a chokepoint" (the neighbour-count test and `highTrafficConnectionCount` are gone); under Consolidate a colonized chokepoint also keeps a garrison (round 1), beside outer planets | `ShipTransportPlanner.ApplicableCategories`, `MaintainsGarrison` |
| Blockade ranking | new step after the recent-cut step and before the smallest offense needed: higher chokepoint percentile first, counting only real chokepoints (everything below the threshold ties at 0, so `chokepointPercentile > 1` switches it off); new reason label `Chokepoint` | `AssaultPlanner.ChooseBlockadeTarget`, `ReasonChokepoint` |
| Colonization tilt | Consolidate only: choice cost = `route.Cost / (1 + colonizationChokepointWeight x percentile)` (default 0.5; 0 or below is off; Expand unchanged); the colonist order's delay now always uses the real `route.Cost` | `PlayerAI.ProcessColonizers`, `ColonizationCostDivisor` |
| Log lines | `T0\|P-1\|Chokepoints\|<planet>=<betweenness>%<percentile>,...` (top 10, once per `InitGame`), `ChokepointColonize\|<origin>-><target>\|<routeCost>\|<percentile>\|<nearestTarget>\|<nearestCost>` (per launch when the tilt moved the pick), `ChokepointGarrison\|<colonized>\|<boardTotal>\|<shipsOnThem>\|<allShips>` (per player every 25 turns), and `Chokepoint` as a new `BlockadeTarget` reason | `AITuningLogger`, `GameAI.LogEconomySummary`, `Gameboard.InitGame` |
| Self-checks | new `FlatSpace -> AI -> Run Chokepoint Self-Check` (registered in Run All AI Self-Checks, now 9 suites); the ranking and colonization checks are in `BlockadeBreakSelfCheck` (new `Scenario.Hub`) | `Assets/Editor/ChokepointSelfCheck.cs`, `BlockadeBreakSelfCheck.cs` |
| Tunables | `chokepointPercentile` 0.9, `colonizationChokepointWeight` 0.5 (in-code defaults on `GameAIConstants`; the constants asset needed no new keys) | `GameAIConstants.cs` |

### What was verified, and how
- You ran `Run All AI Self-Checks` twice and reported "all self checks pass": once on the tree after the five plan tasks
  (`c70cb35`) and once after the review fix (`27806c2`, which is what is on `main`).
- A fresh whole-branch review (a most-capable-model reviewer, which hand-traced every new assertion) found no Critical
  issue and one Important one (fixed, see Decisions) plus three minors (deferred, see Open items).
- Play-mode runs, analysed with `/tuning-log` (logs are gitignored in `AITuningLogs/`, all to T399, fleet-cap violations 0 in
  every run):
  - **6 `test2.json` runs** (12:33-12:39) against the 3 clean baseline runs of 2026-10-01 16:25-16:27: no difference outside the
    overlapping ranges (`Blockade` 41-257 against 101-161; `PlanetDead` 8-20 against 18-29). `Chokepoint` decided 5-10 of 84-139
    `BlockadeTarget` lines. The board has 4 chokepoints (Ocean 2, Ocean 1, Farm 0, Verdant 0; Industrial 0 prints `0.90` but is
    just under the threshold). `ChokepointColonize` fired 7-12 times per run, 44-92 more path cost than the nearest target on
    average. P2 and P1 held 20-24% of their warships on chokepoints.
  - **6 `4p.json` runs** (12:40-12:48) against 6 earlier runs (10-01 16:46-16:49 and 17:07-17:11): `Blockade` 60-209 against
    48-216, `PlanetDead` 7-28 against 4-26, planets at T375 96-99 against 79-99. `Chokepoint` decided 11-19 of 105-224 targets
    (about 8-14%). Assault was busier, not starved (`BlockadeForce` waves 105-236 against 72-164). Warship starts were about 13%
    higher on average (653-914 against 542-814, ranges overlap). Players colonize 0-6 of the board's 10 chokepoints, chokepoint
    garrisons hold about 12-30% of a player's warships (peak 46%). `ChokepointColonize` fired 26-36 times per run, 65-93 extra
    path cost on average (max 184-363).
  - **6 `2Player10Planet.json` runs** (12:51-12:56, no baseline, run for interest): all finish; one chokepoint (Farm 1; with 10
    planets the percentile moves in steps of 1/9 so 0.9 admits one planet; Ocean 0 is 0.89); lopsided results (five of six won
    by one player), blockade events 1-281, churn concentrated on one or two planets (top-two share 100%).
  - **Planet churn and supply** (`test2.json`, per the skill's churn analysis): churn did not worsen (deaths 8-20 against 18-29,
    top-two share 62-80% against 78-95%); cross-player recolonization 4-13 against 3-5 on `test2.json` and up to 26 on `4p.json`
    (information only, contested planets are desired). Desolate 0 exports grotsits as before (ratio 8.1-11.8 against 9.5-11.6);
    Desolate 1 got about 4 times the food inflow of the baseline. The extreme case was `Industrial 5` (12 arrivals, 12 deaths in
    one run, P2 recolonizing it every ~10 turns with no P2 shipment, cancellation or food rider for it and no `Blockade` event at
    it; two connections). Only 2 of those 12 colonists were chokepoint tilts. The same pattern as `Industrial 1`/`Industrial 2` in
    the baseline.
  - **Tilt reach** (colonizations launched at or landing on chokepoints, per the top-10 set on `4p.json`): launches at chokepoints
    roughly doubled (about 33 against about 17 per run) and chokepoints held at T200 rose about 1.5 on average; by T375 almost all
    are held in every set. On `test2.json` all 4 chokepoints were held by T200 in every run, baseline included, so the tilt could
    not help there.
  - **`chokepointPercentile` 0.8 against 0.9** (you set 0.8 in the asset for these runs, then restored it; the logs confirmed it
    through `ChokepointGarrison`'s board total: 20 chokepoints on `4p.json`, 8 on `test2.json`). `test2.json`, 6 runs at 0.8
    (13:23-13:27): no measurable difference from 0.9 (`PlanetDead` 6-34, `Blockade` 50-230, planets 35-39), more garrison weight
    (8 chokepoints). `4p.json`, 6 runs at 0.8 (13:16-13:19 and 13:29-13:33): chokepoints held at T200 14-19 of 20 against 10-16 at
    0.9 and 8-14 in the baseline; warship starts 705-952 against 653-914 and 542-814; `PlanetDead` 62, 42, 24, 20, 18, 7 (two runs
    above anything seen, on Industrial 5/13 and Desert 4, none a chokepoint; the other four normal); grotsits-short planet-turns
    159-186 against 193-311 (non-overlapping, cause unknown); fleet-cap violations 0; lowest T375 morale 123.
  - The newest logs after those (13-04-58 stopped at T133, 13-07-43 at T119, plus two stubs) are partial runs and were not
    analysed.

### What was not verified
- Unity cannot be run from Claude Code: every per-task "compile" signal was Rider's `get_file_problems` (it cannot analyse
  brand-new files, "not included in any project", so the two new scripts were never checked by Rider). Your Editor runs were
  the real checks.
- `AITuningLogger` has no self-check by design; the new lines were verified from real logs (formats match). The `Chokepoints`
  line appears twice in a log when `InitGame` runs twice (the scene's default board first); use the last.
- Whether the garrison, tilt and ranking defaults (0.9, 0.5) are good values: the runs show no regression, not that they are
  tuned. Only `chokepointPercentile` was compared (0.9 against 0.8); `colonizationChokepointWeight` was never varied. Baselines are
  thin (3 runs a side on `test2.json`, 6 on `4p.json`); differences were only called real where ranges did not overlap.
- The exit-hook change (below) was tested only through its commit step in a throwaway repo, not through a real session exit.
- The cause of the `Industrial 5` churn on P2's side is a reading from missing log lines (nothing of P2's reaches it), not a
  checked fact about P2's range.
- The `P1` morale of 90 at T375 in one `4p.json` run (12-43-25) was not investigated.

## Files and commits (all on `main`, pushed to `origin`; the feature `769cc63..2e3ec5a`, then `fe81118..f1560a5`)

| Commit | What |
|---|---|
| `ff9c481` | spec |
| `05a7faa` | `PlanetCentrality`, `GameAIMap` exposure, tunables, `ChokepointSelfCheck` scaffold, fixture neutralisation |
| `5f6e968` | garrison category 4 and the Consolidate chokepoint garrison; `highTrafficConnectionCount` removed |
| `3743f3b` | blockade ranking step and the `Chokepoint` reason |
| `21784e5` | Consolidate colonization tilt, real-route-cost delay, `ChokepointColonize` |
| `c70cb35` | `Chokepoints` and `ChokepointGarrison` log lines, `ChokepointSummary`, docs |
| `27806c2` | review fix: the ranking step only counts real chokepoints (`chokepointPercentile` switches it off) |
| `4fc59d4` | the plan file |
| `2e3ec5a` | merge commit (`--no-ff`) |
| `1ffcc45` | docs: wanted fleet includes colonized chokepoints; chokepoint garrison size (the review's doc minors) |
| `5ac9e30` | docs: sub-project 4 moved to `completed_features.md` (`/update_feature_list`), deferred items recorded in `FUTURE_FEATURES.md` |
| `f1560a5` | exit hook now commits the handoff file (`.claude/hooks/handoff-on-exit.sh`) |

`fe81118` ("color order-line triangles by owning player") is a separate commit of yours that was pushed in the same range.

New scripts, each with its `.meta` committed: `Assets/Flatspace/GameAI/PlanetCentrality.cs`, `Assets/Editor/ChokepointSelfCheck.cs`.
Changed: `GameAIMap.cs`, `GameAIConstants.cs`, `ShipTransportPlanner.cs`, `AssaultPlanner.cs`, `PlayerAI.cs`, `GameAI.cs`,
`GameBoard.cs`, `AITuningLogger.cs`, `BlockadeTargetTracker.cs` (comment), `GameAIConstantsProductionTypes.asset` (the
`highTrafficConnectionCount` key removed), `AllAISelfChecks.cs`, `ShipTransportSelfCheck.cs`, `WarshipSelfCheck.cs`,
`PlayerKnowledgeSelfCheck.cs`, `BlockadeBreakSelfCheck.cs`, `CLAUDE.md`, `FUTURE_FEATURES.md`,
`.claude/skills/tuning-log/SKILL.md`, `completed_features.md`. This handoff file was deliberately left uncommitted for the exit hook
to commit when the session ends (you said "leave it"). The feature branch was deleted after the merge, and the scratch workspace
(`.superpowers/sdd/2026-10-02-connectivity-targeting/`) was deleted. `.idea/.idea.FlatSpace/.idea/indexLayout.xml` is still a
pre-existing unstaged deletion, left alone. `Assets/GameAIConstantsProductionTypes.asset` was edited by you to 0.8 for the
comparison and then restored from git (`git restore`), so it has no `chokepointPercentile` or `colonizationChokepointWeight` keys
and both use their in-code defaults; Unity may re-save it with those keys and the earlier tunables.

New memory notes written this session: `producer-value-per-type-deferred`, `chokepoint-before-recent-cut-tuning-option`.

## Decisions and why
- **Brainstorming answers (all your picks, recommendation first each time):** scope B (the score, blockade ranking, garrison, and a
  Consolidate-only colonization tilt; DC selection and the shipment threshold stay out); garrison option B (swap the category 4
  test to chokepoints AND under Consolidate garrison own chokepoints at round 1); compute from the stored all-pairs paths
  (approach 1) with a percentile score, not Brandes' algorithm.
- **Colonization assessment I gave:** yes, but Consolidate only and mild (hubs are contested and farther, which means more
  exposure to cuts); Expand stays nearest-first; weight 0 switches it off; it is not a supply gate, so it does not conflict with
  your "churn is desired" ruling.
- **Producer value deferred:** you agreed the values (Verdant 1.0, Desolate 1.0, Farm 0.5, others 0, authored as data on
  `PlanetResourceData`; Desert, Industrial and Ocean excluded because their output is local-only) but said not to build it "at
  least at first" (memory note `producer-value-per-type-deferred`). The design keeps chokepoint value as its own component.
- **Spare-ship calculation not changed:** you said "we could change the spare ship calculation, but don't do that now".
- **Moving chokepoint before recent-cut is a recorded tuning option, not built** (memory note
  `chokepoint-before-recent-cut-tuning-option`; also in the spec and `FUTURE_FEATURES.md`).
- **`chokepointPercentile` stays 0.9:** I recommended keeping it (small, late-fading gains at 0.8, the unexplained death tail in two
  of six `4p.json` runs, and 0.9 keeps the single constant sensible on small boards); you then restored the asset from git. 0.8 is
  a defensible choice if earlier chokepoint holding and the lower grotsits-short numbers matter more.
- **Open items 2-4 deferred (your call, none started):** the `ChokepointColonize` attribution fix, producer value and the spare-ship
  change, and the optional tuning. Recorded in `FUTURE_FEATURES.md` under the moved (4) placeholder.
- **Exit hook:** `handoff-on-exit.sh` now runs the headless `/handoff` in a detached worker and then commits only new or changed
  `docs/handoffs/HANDOFF-*.md` (`git commit --only`, never pushes, skipped mid merge or rebase, no `Co-Authored-By` trailer);
  `bash .claude/hooks/handoff-on-exit.sh --commit-only` commits pending handoff files by hand.
- **Execution:** native (your choice, my recommendation), with one fresh reviewer at the end; feature branch in the working
  tree, no worktree.
- **Ledgered ruling, per-task verification:** Unity cannot be run, so the plan's "wait for the Editor run after each task" was
  replaced by Rider per task and one Editor run after the last task, to keep execution continuous; you ran it once at the end
  and then after the fix. Cost if wrong: a failure would have shown up late (none did).
- **Ledgered ruling, the ranking gate (review Important 1):** the first version ranked on the raw percentile, so almost every
  non-leaf planet differed and the step decided nearly every uncommitted tie, and `chokepointPercentile` could not switch it
  off. I changed the key to `IsChokepoint ? percentile : 0`. This departs from the spec's literal "higher percentile first"; the
  spec and `CLAUDE.md` were updated. You were told in the summary; revert is one line in `ChooseBlockadeTarget`.
- **Plan deviation, ledgered:** the periodic line is `ChokepointGarrison|<colonized>|<boardTotal>|<shipsOnThem>|<allShips>`, not
  the spec's `Chokepoints|<held>|<total>|<shipsOnThem>|<spare>` (a spare count needs planner state, and two formats under one
  code is confusing); the spec was updated.
- **Fixtures neutralised so older suites keep their meaning:** a 3-planet chain's middle planet is trivially a chokepoint, so
  `ShipTransportSelfCheck.NewConstants`, `WarshipSelfCheck.MakeConstants` and two `PlayerKnowledgeSelfCheck` blocks set
  `chokepointPercentile = 2f` and `colonizationChokepointWeight = 0`; `RunCategoryCheck` sets 0.9 locally.
- **Rejected or avoided by you (still true):** do not change the blockade ranking to answer starved blockades, and a large
  committed force outranking small blockades is desired (memory note `big-committed-forces-outranking-small-blockades-is-desired`);
  do not propose a colonization supply gate (churn is desired, memory note `contested-planets-are-desired-not-bugs`); no more P3
  logging; Option A worker allocation and the industry ideas are recorded only.

## Open items, most important first
1. **Sub-project 5: colonization avoidance for orders already in flight** (see `FUTURE_FEATURES.md`). It finishes the blockade
   "unit" milestone. Use `superpowers:brainstorming` first.
2. ~~Run `/update_feature_list`~~ **Done** (`5ac9e30`): sub-project 4 is in `completed_features.md` under "Partially done", and the
   deferred remainder stays in `FUTURE_FEATURES.md`.
3. **Tuning from logs, deferred, none requested:** the tunables are `chokepointPercentile` (0.9 kept; 0.8 tried; on small boards 0.85
   would give two chokepoints on the 10-planet board), `colonizationChokepointWeight` (0.5, never varied; a comparison at 1.0 against
   0.5 on `4p.json` was offered and not done), and moving the chokepoint ranking step ahead of the recent-cut step. Each touches the
   blockade ranking, garrisons or the wanted fleet, so tell the user before proposing a change.
4. **Chokepoint garrisons raise the wanted fleet:** `PlayerAI.WantedWarships` sums the Consolidate planner's round garrisons, so
   warship starts were about 13% higher on `4p.json` (ranges overlap). Bounded by `warshipsPerColonizedPlanet`; the fleet cap
   held in every run. This touches the fleet cap input, so mention it when tuning.
5. **Agreed but not built:** producer value by planet type, and any change to the spare-ship calculation.
6. **Watch items from the runs:** P1 morale 90 at T375 in one `4p.json` run (12-43-25; other values 108-165); cross-player
   recolonization has a higher tail (16 and 26 events in two `4p.json` runs against 2-12 before); grotsits-short planet-turns
   overlap the baseline (199-311 against 193-269).
7. **Review minors:** the two doc fixes (the "wanted fleet" wording and the largest-garrison note) are **done** (`1ffcc45`). One is
   still deferred: `ChokepointColonize` can blame the tilt when another colonizer row already claimed the nearest target (`nearest`
   is computed before `ScoreMatrix` assigns claims).
8. **Recorded but not to be started unprompted:** Option A worker allocation, industry production centers and shippable industry,
   a per-target shipment delivery threshold and `shipmentMinDeliveredFraction` tuning, the AI moving warships to impose a blockade,
   and the connectivity-weighting consumers still open in `FUTURE_FEATURES.md` (DC selection, the shipment threshold).
9. **Self-checks pollute the tuning log** (carried over): the logger path stays open after Play ends, so self-checks run in the same
   Editor session append lines (turns 0-20, planets named `A`, `C`, `D`) to the last match's log. My analysis scripts dropped any
   line more than 50 turns before the previous one.

## Gotchas for a fresh session
- **`/handoff` arrives as `C:/Program Files/Git/handoff`:** Git Bash expands the leading slash into a path. Typing the command in a
  Windows shell or without the leading slash avoids it (this happened twice this session).
- **Exit hook and this file:** a named session exiting launches a headless `/handoff` that, finding this file already present, may
  refuse to overwrite it; the hook's commit step then commits whatever is untracked or changed under `docs/handoffs/`, so this file
  gets committed either way.
- **Partial logs:** `AITuningLogs/` also holds short stub logs from the default scene board (170 bytes, board `7PlanetBoardState`) and
  runs stopped early; check the board and the max turn before analysing the newest file.
- **Rider:** `get_file_problems` needs `rootFolder: "C:/Projects/FlatSpace"` and cannot analyse a brand-new file until Unity
  regenerates the project ("not included in any project"); an empty problem list is not proof of a compile.
- **New scripts need their `.meta`:** I wrote each with PowerShell (`fileFormatVersion: 2` and a GUID, MonoImporter block); Unity may
  rewrite them on import (harmless).
- **Self-check fixtures:** older suites rely on `chokepointPercentile = 2f` and weight 0 in their shared constants helpers; a new
  Consolidate-based check that builds its own `GameAIConstants` on a small board needs the same, or a middle planet starts
  garrisoning. Hub layout used by the new checks: A(0,0)-H(100,0), H-X1/X2/X3, A-Y(0,80) (H betweenness 9 = percentile 1, A 4 = 0.8).
  Never put an assertion exactly on a float boundary; every test planet needs a distinct position (the A* tie-break note).
- **Log analysis:** the `AITuningLogs/` files are gitignored; today's runs are `test2.json` 12:33-12:39, `4p.json` 12:40-12:48
  (the 12-45-51 file is a 170-byte stub from the default scene, not a match), `2Player10Planet.json` 12:51-12:56. The baselines
  are the 10-01 files named above. The ad-hoc awk scripts lived in `/tmp` and are gone; the `tuning-log` skill describes the
  analyses (now including a "Chokepoints" section). A python probe in Git Bash hung once (heredoc with an empty script); use awk.
- **Things you said to avoid:** see Decisions (ranking for starved blockades, a colonization supply gate, more P3 logging, starting
  Option A or the industry ideas unprompted). Recommend one option with every choice, brainstorm in chat one question at a time and
  get a yes before coding, test first. Commit and push only when asked (you asked for the plan-file commit, the merge, the push and
  the branch deletion).
