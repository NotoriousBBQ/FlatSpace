# Handoff: Colonization Redirect

Saved from the Claude Code session named **"colonization_redirect"** (renamed with `/rename` near the end; session ID
`e1811146-2a41-473a-b8c7-4cbaf088ebbe`), on 2026-10-02. All the work was done on 2026-10-02. Resume it with `/resume`
(pick it) or `claude --resume "colonization_redirect"`. This file is the written version so nothing depends on that history.

Read `CLAUDE.md` first (architecture, conventions, the self-check pattern; the new "Colonist redirect" bullet under "Warships
and Blockade", and "AI Tuning Log"), then `FUTURE_FEATURES.md` (the session rules in `docs/session_configuration.md` require it
before any design). Spec and plan: `docs/superpowers/specs/2026-10-02-colonist-redirect-design.md` and
`docs/superpowers/plans/2026-10-02-colonist-redirect.md`. The earlier handoff is `HANDOFF-connectivity-targeting-2026-10-02.md`.

## Where things stand

**Asked:** "let's go with the colony redirect", meaning blockade sub-project 5 (colonization avoidance for orders already in
flight; the last item of the blockade "unit" milestone). Brainstormed (architectural path, one question at a time), wrote the spec
and plan, executed natively on a feature branch, ran a fresh whole-branch review, fixed what it found, tuned from Play-mode logs,
merged and pushed. **Sub-project 5 is done, merged to `main` (`b84c316`) and pushed (`origin/main` is at `521484b`).** The local
branch `colonist-redirect` was deleted and its scratch workspace cleared at your request. After the merge you also asked for
`/update_feature_list` (sub-project 5 is now in `completed_features.md`), for the deferred review minors to be remembered, and for a
new `ship_combat` milestone in `FUTURE_FEATURES.md`.

| Area | What it does | Key code |
|---|---|---|
| Redirect planner | once per turn, after `ApplyBlockades`, a colonist whose REMAINING route crosses a planet in its owner's `BlockadeView` (visible planets plus blockade memory, never live values) is re-planned: a clean detour to the same target, else a diversion to another candidate, else it continues and is cut as before. The colonist counts as standing on the last node it passed; the clock restarts at `max(1, route.Cost / defaultTravelSpeed)` | `ColonistRedirect.Plan/Apply` (new, pure), `BlockadeSystem.CurrentNode/NodesAhead`, `GameAI.RedirectColonists` |
| Diversion candidates | known planets that are empty, or have the player's colonist already inbound, or are colonized below max population (own planets included). `CanSupportColony` is not applied. Ordinary launches keep `IsValidColonizationTarget` | `PlayerAI.IsDiversionTarget` |
| Detour ratio | a detour is declined for a diversion when its REAL route cost is more than `colonistDetourDivertRatio` (default 2, 0 = always detour first) times the diversion's real cost; no candidate means the detour is always taken | `ColonistRedirect.Plan`, `GameAIConstants.colonistDetourDivertRatio` |
| Rider and flags | the food rider (matched on player, origin, old target and equal `TimingDelay`) follows a divert and takes the new delays; the old target's transfer flag is cleared only when no other colonist of the player still heads there | `ColonistRedirect.Apply` |
| Arrival rule | below max population the colonist joins; at or above max on a planet the order's player OWNS it docks one colony ship (`DockShipRebuiltSnapshot`); a full planet owned by another player (or tied) still takes the colonist as before | `GameAI.ApplyColonistArrival` |
| Cut fix | a blockade cut removes only that colonist and ITS rider and clears the target's flag only when no other colonist heads there | `BlockadeSystem.ApplyToColony` |
| Display fix | order lines and fog corridors use the order's carried route (or the shortest path), never `GetPath(order.Origin, order.Target)` | `GameAIMap.OrderPathPoints`, `FogOfWarSystem.Recompute`, `GameBoard` |
| Log lines | `ColonistRedirect\|<origin>-><oldTarget>\|<Detour or Divert>\|<newTarget>\|<nodes>\|<cost>\|<blocked>\|<declined detour cost or ->` (once per redirect), `ColonistRedirectFailed\|<origin>-><target>\|<blocked>` (once per order, log-only set in `GameAI`), `ColonistDocked\|<planet>\|<amount>` | `AITuningLogger`, `GameAI` |
| Self-check | new `FlatSpace -> AI -> Run Colonist Redirect Self-Check` (registered in Run All AI Self-Checks, now 10 suites) | `Assets/Editor/ColonistRedirectSelfCheck.cs` |

### What was verified, and how
- You ran `Run All AI Self-Checks` and reported all passing after the first three tasks, and again after the review fixes, after the
  crash fix and after the detour ratio (the last report is on the tree that is on `main`, except the docs commits).
- A fresh whole-branch review (a most-capable-model reviewer) found no Critical issue and two Important ones (both fixed, see
  Decisions) plus nine minors (deferred, see Open items).
- Play-mode runs, analysed with `/tuning-log` (logs gitignored in `AITuningLogs/`, all to T399, fleet-cap violations 0 in every run):
  - **6 `test2.json` runs** (18:19-18:23) against the 6 clean baseline runs of 12:33-12:38: colonist cuts 4-9 (avg 7.3) to 2-6 (avg 3.7,
    ranges overlap, a trend), arrive/start 0.85-0.94 to 0.87-0.97, planets at T375 inside the old ranges, 25 redirects (22 diverts, 3
    detours).
  - **8 `4p.json` runs** (18:25-18:41, before the detour ratio) against the 6 baseline runs of 12:40-12:48: colonist cuts 9-35 (avg 22)
    to 4-11 (avg 6, non-overlapping), arrive/start 0.76-0.92 to 0.92-0.98 (non-overlapping), planets at T375 94-99 against 96-99,
    `PlanetDead` 3-43 against 7-28 (the 43 was `Farm 11` 32 times: one-planet churn), 98 redirects (84 diverts, 14 detours). Detours
    averaged cost 1787 and 79% never arrived; diverts averaged 533 and 11% never arrived (the basis for the ratio rule).
  - **3 `4p.json` runs with the ratio rule** (18:55-18:58): the rule fired 7 times in 63 redirects (declined detours cost 965-5169, 2.2 to
    21 times the diversion), all 7 diverts arrived; detours 3 averaging cost 853 (1 of 3 never arrived); diverts 17% never arrived
    (5 of the 10 were one T344 event, five P2 colonists cut together at `Normal 20`); colonist cuts 7-11; fleet cap clean.
  - **The 4p crash** (`KeyNotFoundException` for `Desolate 2` in `FogOfWarSystem.Recompute`, T321 of run 18-33-38) was a colonist
    diverted back to its own origin; fixed, and two later runs contain diverts home (T315, T375) and finished cleanly.

### What was not verified
- Unity cannot be run from Claude Code: every per-task "compile" signal was Rider's `get_file_problems`, which cannot analyse brand-new
  files. Your Editor runs were the real checks.
- `AITuningLogger` has no self-check by design; the three new lines were checked from real logs.
- The detour ratio default (2) rests on three `4p.json` runs; `test2.json` was not run with it. The ratio was never varied.
- "Never arrived" is a loose proxy: arrivals were matched by player and planet pair and cuts by player, not by individual order.
- The review fixes were not given a fresh re-review (the executing-plans skill forbids it); the self-checks covering them passed.
- Docked colonists were observed (33 across eight `4p.json` runs, 25 launched within 30 turns) but not tuned.

## Files and commits (all on `main`, pushed to `origin`; the feature `a12a4b5..b84c316`, then `32419b9` and `521484b`)

| Commit | What |
|---|---|
| `a12a4b5` | spec |
| `c7d9b5a` | plan |
| `a97a4b2` | `ColonistRedirect` planner, `BlockadeSystem.CurrentNode/NodesAhead`, first self-check cases |
| `03e4c2d` | `Apply`, `IsDiversionTarget`, `ApplyColonistArrival`, `LogColonistDocked` |
| `e3b61f0` | per-turn `RedirectColonists`, the two other log lines, suite registered |
| `4f5783d` | docs (`CLAUDE.md`, `FUTURE_FEATURES.md`, `tuning-log` skill) |
| `4dcd337` | review fixes: dock only on an owned planet; a cut removes only its own colonist and rider |
| `ce4cf47` | crash fix: `GameAIMap.OrderPathPoints` for fog corridors and order lines |
| `d6cb2da` | detour ratio rule (`colonistDetourDivertRatio`) and the extra log field |
| `b84c316` | merge commit (`--no-ff`) |
| `32419b9` | docs: sub-project 5 moved to `completed_features.md`, milestone reached |
| `521484b` | docs: `ship_combat` milestone in `FUTURE_FEATURES.md` |

New scripts, each with its `.meta` committed: `Assets/Flatspace/GameAI/ColonistRedirect.cs`, `Assets/Editor/ColonistRedirectSelfCheck.cs`.
Changed: `BlockadeSystem.cs`, `PlayerAI.cs`, `GameAI.cs`, `GameAIMap.cs`, `GameAIConstants.cs`, `AITuningLogger.cs`,
`FogOfWarSystem.cs`, `GameBoard.cs`, `AllAISelfChecks.cs`, `BlockadeAvoidanceSelfCheck.cs` (only `Scenario`, `Spawn` and `Build` made
`public`), `CLAUDE.md`, `FUTURE_FEATURES.md`, `completed_features.md`, `.claude/skills/tuning-log/SKILL.md`. `colonistDetourDivertRatio`
is not in `Assets/GameAIConstantsProductionTypes.asset`, so it runs on its in-code default; Unity may add the key when it re-saves the
asset. This handoff file was left uncommitted (the exit hook commits it). `.idea/.idea.FlatSpace/.idea/indexLayout.xml` is still a
pre-existing unstaged deletion, left alone. The branch `colonist-redirect` and `.superpowers/sdd/2026-10-02-colonist-redirect/` are gone.

New memory note written this session: `colonist-redirect-deferred-minors`.

## Decisions and why
- **Brainstorming answers (all your picks, recommendation first each time):** when no clean route exists, divert to another target (not
  hold, not return); the wider candidate set (colonies under max population, colonies with your colonist inbound) applies to diversion
  ONLY, ordinary launches keep their rules; detour first, divert second; the colonist counts as standing on the last node it passed and a
  redirect restarts the clock (not an interpolated mid-edge position); decisions use only the owner's `BlockadeView`. Execution: native,
  with one fresh most-capable-model reviewer at the end (your choice, my recommendation).
- **Arrival rule for every colonist** (your rule: add to the planet, or dock the ship when it is at max). **Ruling:** the dock applies only on
  a planet the colonist's player owns; a full foreign or tied planet takes the colonist over max as before. Reason: no colony-ship query
  checks a ship's owner, so a foreign ship docked there would be launched by that planet's owner or sit unused (review Important 1). Cost if
  wrong: a colonist at a full foreign planet joins over max instead of docking.
- **Origin stays an eligible divert target** (ruling): the spec's "colonized below max" includes it (it is below max once the colonist left),
  so a blocked colonist can turn back home. Only the display code was made safe. Excluding it is one line in `ColonistRedirect.Plan`.
- **Detour tuning (your "tune the detours", then "yes" to my proposed rule):** ratio rule with default 2, comparing REAL route costs (not the
  chokepoint-tilted choice cost), 0 switches it off. Rejected alternatives: an absolute cost cap (board-size dependent) and comparing
  against the original remaining route (undefined when the target itself is blockaded).
- **Review fixes:** Important 1 (above) and Important 2 (a cut removed every colonist and rider sharing origin and target, possible after a
  divert). Both written test-first.
- **Deferred minors are NOT in `FUTURE_FEATURES.md`:** you want them addressed only if they become symptomatic and kept in memory (see Open
  items).
- **`/update_feature_list`:** the blockade feature is "partially done", not done, because two unnumbered open items remain (the AI moving
  warships to blockade, now in `ship_combat`; and the deferred items from sub-project 4). Sub-project 5 moved to `completed_features.md`; the
  blockade milestone line says all of (2) to (5) are done.
- **`ship_combat` milestone** (added to `FUTURE_FEATURES.md` where "Ship to ship combat" was): (1) Simplified diplomacy simulation (written by
  me from the code, since you gave no description; marked not started and to be brainstormed), (2) Ship to ship combat, (3) Planetary
  invasion, (4) Planet defense (moved from AIStrategyConsolidate's remainder, which now says "Remaining: strategic colonization"), (5) The AI
  blockades on purpose (moved from the blockade feature at your request). The "Blockade Targets" section stays where it is and is pointed
  at by (5).
- **Still true from before:** do not change the blockade ranking to answer starved blockades; a large committed force outranking small
  blockades is desired; do not propose a colonization supply gate (churn is desired, so diverts to Desolate planets are information, not a
  defect); no more P3 logging; Option A worker allocation and the industry ideas are recorded only.

## Open items, most important first
1. **`ship_combat` milestone, element (1): simplified diplomacy simulation.** Not started. Use `superpowers:brainstorming` first; the
   states, what changes them and which AI decisions read them are undecided. Today every other player is an enemy: first contact flips
   Expand to Consolidate (`PlayerKnowledge.HasContact`), `BlockadeSystem` counts the largest single other player, and the assault targets
   any other player's planet.
2. **Re-check the detour ratio with more logs.** Only three `4p.json` runs have it and no `test2.json` run. The `tuning-log` skill's "Colonist
   redirect" section lists what to compare (detours against diverts: count, average cost, share that never arrived; how often the rule
   fired). Baselines: no ratio rule on `4p.json` is the 18:25-18:41 set (minus the crashed 18-33-38); before redirect, 12:40-12:48.
3. **The nine deferred review minors** (memory note `colonist-redirect-deferred-minors`; address only if symptomatic): twin riders sharing
   old target and delay can swap riders; a full planet with the player's colonist inbound still qualifies as a candidate; no redirect on
   the first turn after a load (`CurrentBlockadeView` is null until the first `ProcessResults`); spurious `DCCoverageGap` lines when a
   colonist docked; `ColonistRedirectFailed` over-counts losses (it fires on view-or-memory-only blockades); `ColonistDocked` logs
   `order.Data` although one ship docks; spec drift (blocked planets logged as `name=value`, transfer flags saved not derived); no
   self-check that `IsValidColonizationTarget` is unchanged; diverts have no length limit (one cost 4040 and was cut).
4. **Deferred from sub-project 4 (still open, in `FUTURE_FEATURES.md`):** producer value by planet type, the spare-ship calculation, the
   tuning options (chokepoint step ahead of the recent-cut step; `chokepointPercentile` on small boards), the `ChokepointColonize`
   attribution fix (`nearest` is computed before `ScoreMatrix` assigns claims), and the `colonizationChokepointWeight` comparison never done.
5. **Watch items from the redirect runs:** `PlanetDead` tails in single runs (43 and 45 on `4p.json`, each mostly one or two planets, `Farm
   11` and `Industrial 12`); 15 of 22 diverts on `test2.json` and 31 of 84 on `4p.json` went to Desolate planets with no food rider unless
   the original target needed one.
6. **Recorded but not to be started unprompted:** Option A worker allocation, industry production centers and shippable industry, a
   per-target shipment delivery threshold, and the connectivity-weighting consumers still open in `FUTURE_FEATURES.md`.
7. **Housekeeping:** the `.idea/.idea.FlatSpace/.idea/indexLayout.xml` deletion is still unstaged; this handoff file is uncommitted.

## Gotchas for a fresh session
- **`/handoff` arrives as `C:/Program Files/Git/handoff`:** Git Bash expands the leading slash into a path (it happened again at the end of
  this session). Typing the command in a Windows shell or without the leading slash avoids it.
- **Plugin versions:** the superpowers scripts live under `~/.claude/plugins/cache/claude-plugins-official/superpowers/6.4.1/`. An early
  `ls | head -1` picked 6.3.0, which lacks `executing-plans/scripts/task-start` and `task-done`, and I wrongly reported them as missing
  (corrected in the ledger). Use the version the skill's base directory names.
- **A planet's own name is never in its path table** (`Planet.DistanceMapToPathingList`), and a diverted colonist can have `Origin == Target`.
  Never call `GameAIMap.GetPath(order.Origin, order.Target)` for an order; use `GameAIMap.OrderPathPoints(order)`. This crashed fog of war.
- **A redirected order keeps its real `Origin`; its `Route` starts at the node it redirected from.** Anything matching a rider or twin colonist
  uses player, origin, target and `TimingDelay` together.
- **Self-check fixtures:** `BlockadeAvoidanceSelfCheck.Scenario` (Diamond: A(0,0), B(100,50), C(100,-150), D(200,0); player 0 at A with 5
  population, B and C carrying an inbound flag) is now public and reused by `ColonistRedirectSelfCheck`. `DockShipRebuiltSnapshot` needs
  `Gameboard.Instance`, so `ApplyColonistArrival` takes a dock delegate. Never put an assertion exactly on a float boundary (the detour
  ratio check uses ratios 1.5 and 3 against a cost ratio of about 2.0).
- **Log analysis:** `ColonistRedirect` fields are `$4` pair, `$5` kind, `$6` new target, `$7` nodes, `$8` cost, `$9` blocked, `$10` declined
  detour cost. The `Economy` line is `T|P|Economy|planets|short|morale|upkeep`, so `$4` is planets and `$6` is morale. A log with maxT below
  399 is a partial or crashed run; stub logs are 170 bytes (default scene board). The ad-hoc awk scripts lived in the session scratchpad and
  are gone; the `tuning-log` skill describes the analyses. Use awk, not python heredocs, in Git Bash for log work (a python heredoc broke on
  quoting once; writing the script to a file worked).
- **Rider:** `get_file_problems` needs `rootFolder: "C:/Projects/FlatSpace"` and cannot analyse a brand-new file; an empty problem list is
  not proof of a compile.
- **Things you said to avoid:** see Decisions (ranking for starved blockades, a colonization supply gate, more P3 logging, starting Option A
  or the industry ideas unprompted, keeping the deferred minors in `FUTURE_FEATURES.md`). Recommend one option with every choice, brainstorm
  in chat one question at a time and get a yes before coding, test first. Commit and push only when asked (you asked for each doc commit,
  the merge, the pushes, the branch deletion and the workspace cleanup).
