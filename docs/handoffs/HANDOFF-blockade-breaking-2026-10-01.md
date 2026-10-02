# Handoff: Blockade Breaking

Saved from the Claude Code session named **"blocade-breaking"** (the name as typed with `/rename`; the file name uses the
corrected spelling), on 2026-10-01. All the work was done on 2026-10-01. Resume it with `/resume` (pick it) or
`claude --resume "blocade-breaking"`. This file is the written version so nothing depends on that history.

Read `CLAUDE.md` first (architecture, conventions, the self-check pattern, "Warships and Blockade", "AI Tuning Log"), then
`FUTURE_FEATURES.md` (the session rules in `docs/session_configuration.md` require it before any design). Spec and plan:
`docs/superpowers/specs/2026-10-01-blockade-breaking-design.md` and
`docs/superpowers/plans/2026-10-01-blockade-breaking.md`. The earlier sub-project 2 handoff is
`HANDOFF-resource-blockade-tuning.md`.

## Where things stand

**Asked:** start blockade sub-project 3 ("assault breaks blockades"), then, as the work went on: write the plan, execute it
natively, review it, analyse the tuning logs from Play runs, add `BlockadeSkipped` logging to find out why some blockades are
never answered, merge, update the feature lists, push. **Sub-project 3 is done, merged and pushed** (`main` is at `52a945f` on
`origin`). Sub-projects 4 and 5 are next and have not been started.

| Area | What it does | Key code |
|---|---|---|
| Blockade targets | under Consolidate the single sticky assault target is a planet blockaded against the player (enemy-occupied, own or empty; remembered unseen planets count) first, else the old enemy-occupied rule | `AssaultPlanner.ChooseBlockadeTarget` / `ChooseEnemyTarget` / `ChooseTarget`; an `AssaultPlanner` built without a view behaves as before |
| Ranking | first difference wins: most of my offense committed there (docked + in flight), a cut of my orders within `blockadeTargetRecentTurns` (5), smallest offense still needed, cheapest path, name. **No connectivity term yet, on purpose** | same |
| Force sizing | `needed = value x (1 + blockadeBreakMargin 0.1) - offense in flight`; sources walked cheapest path first, the exact ships that would leave are summed by real per-ship offense; a short fleet sends every spare ship | `AssaultPlanner.Plan` / `PlanBlockadeForce`, `LastBlockadeForce` |
| In-flight offense | derived float per planet, recomputed once per turn from in-flight ship orders before `ProcessResults`, never saved | `Planet.GetIncomingOffense`, `GameAIMap.RecomputeIncomingOffense`, `GameAI.GameAIUpdate` |
| Holding a broken blockade | the planet leaves the view the moment its value hits 0, so without a hold the force left the same turn and the blockade re-formed. Planets where my warships and another player's are both docked are held; a held colony is sink-only | `AssaultPlanner.ContestedHolds`, `ShipTransportPlanner.HeldPlanets`, `PlanetState.Held` |
| Research | Warship Offense items' weight x `blockadedOffenseResearchBoost` (2) while any blockade against the player is in the view, applied after `NormalizeResearchWeightsBySubtype` | `PlayerAI.GetResearchSituationalMultiplier`, `ApplyResearchSituationalWeights` |
| Log lines | `BlockadeTarget`, `BlockadeForce`, `BlockadeTargetEnd`, `OffenseResearchBoost`, `BlockadeSkipped` (formats in `CLAUDE.md` "AI Tuning Log"); target and skip lines on change only, through pure trackers | `BlockadeTargetTracker`, `BlockadeSkipTracker`, `AITuningLogger`, `AssaultPlanner.LastSkipped` |
| Self-check | `FlatSpace -> AI -> Run Blockade Breaking Self-Check`, registered in Run All AI Self-Checks (8 suites) | `Assets/Editor/BlockadeBreakSelfCheck.cs` |
| Tunables | `blockadeTargetRecentTurns` 5, `blockadeBreakMargin` 0.1, `blockadedOffenseResearchBoost` 2 (all in-code defaults on `GameAIConstants`) | `GameAIConstants.cs` |

### What was verified, and how
- You ran the Editor self-checks and reported "checks pass" after the six plan tasks, and "all self checks pass" after the
  `BlockadeSkipped` commit (`3bb5988`), i.e. on the tree that is now on `main`.
- A fresh whole-branch review (most capable model, hand-traced every assertion) found one Critical and two Important issues;
  all were fixed in one pass (`b9dffaa`): the broken blockade was dropped the same turn (the oscillation above), a blockaded
  own colony was not protected by `HeldPlanet`, and the enemy-occupied and own-colony target kinds were untested. The new
  `RunHeldPlanetsCheck` was written first and Rider showed the missing members (RED).
- Play-mode runs analysed with `/tuning-log` (today's logs only, `AITuningLogs/`, gitignored): three `test2.json` runs
  (16:25-16:27), and three sets of three `4p.json` runs (16:35-37, 16:46-49, 17:07-11). Findings below.

### What was not verified
- Unity cannot be run from Claude Code: every "compile" signal was Rider's `get_file_problems`, which cannot see brand-new
  files ("not included in any project") and shows a cascade of "cannot resolve" errors for them until Unity regenerates the
  project. The Editor runs above were the real checks.
- `AITuningLogger` has no self-check by design; the new lines were verified from real logs (formats match).
- Baselines are thin: three runs a side, and the `test2.json` baseline (09-30 13:59-14:01) predates the `GrotsitsShort`
  logging. Later comparisons used only today's post-feature runs, as asked.

## Findings from the logs (what the feature does)
- **It works as designed and moves fleets onto the contested hubs.** `Blockade` events rose about 7-10x on both boards
  (`test2.json` 101/135/161 against 16/11/21; `4p.json` in five of six runs at least 74 against a baseline of 5/9/28; one 4p run
  at 17 sat inside the baseline range). Over half of them sit on 2-3 chokepoint planets (`test2.json` `Verdant 0`,
  `Verdant 1`, `Normal 1` are 4th, 7th, 8th of 40 by betweenness). Grotsits `OrderBlocked` rose from 2-13 to 66-163 per run.
  Mechanism: several players park ships on the same hub, and whoever has the most offense blockades the others.
- **Colonies are unaffected:** colonize arrivals, `PlanetDead` and total planets at T375 stay inside the baseline range.
  Real but small effects: P2 morale on `test2.json` (114-127 against 154-177) and P1 morale on `4p.json` (101-141 against
  140-170, six of six runs); total grotsits short planet-turns on 4p are higher in five of six runs (watch item, ranges overlap).
- **Breaking mechanics:** median time to clear 3-6 turns; `Cleared` about 45-107 and `Switched` about 35-61 per run; waves of about
  1.3-1.6 ships, `stillNeeded > 0` in 9-34% of waves (margin looks fine); after a `Cleared` ships left the planet within 10 turns
  in about 4 of roughly 800 cases (the hold works); `Committed` decides 83-96% of targets, `RecentCut` almost never (0-4 per run);
  `Switched` followed by the same planet being targeted again within 10 turns is about 35-40%; fleet-cap violations 0.
- **`BlockadeSkipped` (the point of the last change):** of about 5,300 skip lines only 2 are `NoPath`; 94-96% of `Outranked`
  lines have a winner with committed offense (median 126-232). So cheap lasting blockades go unanswered because planets where
  the player already has a big fleet (often standoffs the rival outguns) always win the committed-offense step. Example
  (4p, 17:07): `Desert 6` (value 4) cut P2's shipments 118 times (55% of the run) while P2 targeted it once and sent 1 ship; P2 built
  230 warships but sent 76 in 49 waves. `Desert 6`, `Desert 10` and `Industrial 14` are all 1-3 hops from the victim's own home.
  **The user ruled this desired (below).**
- **Planet ownership churn, last three 4p runs (17:07-17:11):** cross-player ownership changes 12/13/19 (earlier today 3-17),
  about two thirds onto a still-populated planet, none before T100; same-player recolonizing after death 19/8/10;
  `PlanetDead` 26/14/20 on 5 planets per run, top two planets 71-80%. Heaviest: `Desert 8` (13 arrivals, 13 deaths, P3 12 of
  them), `Desert 4` (13/10), `Desert 6` (10/6), `Desert 0` (7/6), `Desolate 3` (17 arrivals, 4 deaths) and `Desolate 0`.
  Desert planets lead the deaths, which fits the recorded worker-allocation analysis (Industry-strategy planets get no grotsits
  workers) in `FUTURE_FEATURES.md`; reported as information only.
- **`BlockadeSkipped` volume** is 1,600-2,000 lines per run (about 6% of a log), 17-23 per player/planet pair, because the
  winner changes often.

## Files and commits (all on `main`, pushed to `origin`, `a25f8c5..52a945f`, 14 commits)

| Branch (deleted after merge) | Commits | Merge |
|---|---|---|
| `blockade-breaking` | spec `d69ae00`; plan `5a5e77c`; `session_configuration` rule + plan tuning-log section `6265458`; Task 1 tunables and incoming offense `85b8b56`; Task 2 blockade targets `82aaee6`; Task 3 offense-sized force `15bcd7a`; Task 4 wiring, tracker, log `b4f4a17`; Task 5 research boost `26dbb64`; Task 6 docs `f0c7ef9`; review fixes `b9dffaa` | `a9a4a10` |
| `blockade-skipped-logging` | `BlockadeSkipped` logging `3bb5988` | `1138a7c` |
| (on `main`) | `completed_features.md` / `FUTURE_FEATURES.md` move of item (3) `52a945f` | |

New scripts (each with its `.meta`, committed): `Assets/Flatspace/GameAI/BlockadeTargetTracker.cs`,
`Assets/Flatspace/GameAI/BlockadeSkipTracker.cs`, `Assets/Editor/BlockadeBreakSelfCheck.cs`. Changed: `AssaultPlanner.cs`,
`ShipTransportPlanner.cs`, `PlayerAI.cs`, `GameAI.cs`, `GameAIMap.cs`, `GameAIConstants.cs`, `Planet.cs`, `AITuningLogger.cs`,
`AllAISelfChecks.cs`, `CLAUDE.md`, `FUTURE_FEATURES.md`, `completed_features.md`, `.claude/skills/tuning-log/SKILL.md`,
`docs/session_configuration.md`. This handoff file is not committed. Both feature branches and the scratch workspace
(`.superpowers/sdd/2026-10-01-blockade-breaking/`) were deleted at your request.

## Decisions and why
- **Brainstorming answers (all your picks, recommendation first each time):** candidates are any planet blockaded against the
  player, enemy-occupied, own or empty (option A); one sticky target with blockaded planets ranked ahead of the enemy-occupied
  rule (A); force sized by exact per-ship offense including offense in flight (A); research boost on the Warship Offense line only,
  as a situational multiplier (A); ranking without connectivity, added later (A).
- **Connectivity left out of the ranking on purpose:** no C# betweenness exists (only `tools/planet-centrality.ps1`) and
  sub-project 4 needs it for garrison and colonization too. **Remember to add it to the blockade ranking, after the recent-cut
  step, when sub-project 4 builds it** (memory note `blockade-target-ranking-add-connectivity-in-subproject-4`; also recorded
  in `FUTURE_FEATURES.md` item (4)).
- **New session rule (you asked):** `docs/session_configuration.md` section 4: every implementation plan must include a
  "Proposed tuning log output" section (code and fields, when logged, the question it answers, repeat control and test, skill and
  `CLAUDE.md` updates). The plan has one.
- **Native execution** (your choice over subagent-driven, my recommendation) with one fresh reviewer at the end.
- **Plan deviation, ledgered:** in-flight offense is recomputed once per turn instead of maintained at the three incoming-counter
  sites (same data at decision time, no drift, no arrival or load bookkeeping); the spec text was updated to match.
- **Stateless hold:** every planet where my warships and another player's are both docked is held, not just the previous blockade
  target (no remembered state; cost: Consolidate has slightly fewer spare ships for the assault from standoff planets).
- **Rejected by you: changing the ranking to answer starved blockades.** I proposed removing docked (garrison) offense from
  `CommittedOffense`, which would have put cheap cuts ahead of big standoffs. You said: "A large force tying up resources so a
  much smaller force can hold a planet is desired." Not made. Saved as the memory note
  `big-committed-forces-outranking-small-blockades-is-desired`: report starved blockades neutrally, never propose a ranking fix,
  and **tell the user whenever a proposed change touches** the blockade ranking or the meaning of "committed", garrisons and spare-ship
  supply (`ShipTransportPlanner`, `HeldPlanets`, `ContestedHolds`), the fleet cap and `WantedWarships`, `blockadeBreakMargin`
  or force sizing, the per-target shipment delivery threshold (`MinDeliveredFractionFor`), or any idea to protect shipping from
  lasting blockades (cheap unanswered blockades feed the grotsits cuts and shortages).
- **Other rulings I made, ledgered and told to you:** feature branch in the working tree, no worktree (your Editor reads this tree);
  Rider as the compile signal because Unity cannot be run; the wanted-fleet cap (`WantedWarships`) does not include the
  blockade-breaking need (spec silent, tuning question); a hopeless target stays sticky while the blocker reinforces
  (spec-intended); reachability is measured from any warship holder while ships only leave planets with spare ones; in-flight
  offense uses the first player's research catalog (identical across players).
- **Two small additions to `FUTURE_FEATURES.md` beyond the command:** a one-line "still open, not part of (3)" note (the AI never
  moves warships in order to blockade) and the Milestone line now reads "2 and 3 are done; 4 and 5 remain".

## Open items, most important first
1. **Sub-project 4: connectivity for garrison and colonization targeting.** Use `superpowers:brainstorming` first, mention the
   connectivity-weighting idea and `tools/planet-centrality.ps1`, build the C# betweenness once (from `GameAIMap`'s all-pairs paths),
   and add it to the blockade target ranking after the recent-cut step. Tell the user how it interacts with the "big committed
   force" decision above (a connectivity score changes which standoffs win). Sub-projects 3 to 5 are one "unit"; 3 is done.
2. **Sub-project 5: colonization avoidance for orders already in flight** (see `FUTURE_FEATURES.md`).
3. **Watch items from the logs, no action requested:** the grotsits cuts and shortage at the hubs, lower P1/P2 morale, `Switched`
   flip-flop (about 40%), `BlockadeSkipped` volume. If you ever want to act, the least invasive lever I identified is limiting how long
   a force holds a broken blockade, or holding only where my own routes or colonies are affected; that is a design change, so brainstorm
   it first and raise the "big committed force" note.
4. **The AI never moves warships in order to blockade** (the "Blockade Targets" section of `FUTURE_FEATURES.md`); not started.
5. **Five deferred minors from the review:** exact float equality on `Committed` in `ChooseBlockadeTarget` (an ulp difference could
   skip the `RecentCut` step and mislabel the reason); `BlockadeTargetEnd|Cleared` also fires when a planet merely leaves sight
   (inflates "time to clear"); `AssaultPlanner.DockedOffense` duplicates `BlockadeSystem.DockedOffense`; the research boost tests
   `BlockadedNames.Any()`, which includes remembered unseen planets, while the docs say "visible"; `BlockadeBreakSelfCheck.Spawn`
   leaks `ScriptableObject`s per run (check the sibling suites).
6. **Recorded but not to be started unprompted** (from the earlier handoff, still true): Option A worker allocation (Industrial
   and Desert planets get no grotsits workers; the Desert deaths above are consistent with it), industry production centers and
   shippable industry, a per-target shipment delivery threshold, and `shipmentMinDeliveredFraction` tuning.
7. **Self-checks pollute the tuning log.** The logger's static path stays open after Play ends, so self-checks run in the same Editor
   session append their lines (turn numbers 0-20, planets named `A`, `C`, `D`) to the last match's log: the 16:49:01 and 17:11:01
   files ended that way. Not fixed; analysis must truncate at the turn reset (a drop of more than 50 turns). A fix would be a
   one-line reset of the logger path, which was not asked for.

## Gotchas for a fresh session
- **`/handoff` arrived as `C:/Program Files/Git/handoff`:** Git Bash expanded the leading slash into a path. Typing the command in
  a Windows shell or without the leading slash avoids it.
- **Rider:** `get_file_problems` needs `rootFolder: "C:/Projects/FlatSpace"` and cannot analyse a brand-new file until Unity
  regenerates the project; a new script also makes its users show a cascade of "cannot resolve" errors. That is not a real defect.
- **New scripts need their `.meta`:** I wrote each as `fileFormatVersion: 2` plus a GUID with PowerShell; Unity may rewrite them
  on first import (harmless).
- **Windows Python cannot open Git Bash `/c/...` paths;** use `C:\...` paths in scripts. The analysis scripts lived in the session
  scratchpad, which is gone; the `tuning-log` skill describes the analyses (including the new "Blockade breaking" and
  "Starved blockades" sections), and `./tools/planet-centrality.ps1 -Board <config> -Highlight "<planet>"` gives betweenness.
- **Log files:** `AITuningLogs/` is gitignored. A 38-byte stub log (`16-25-14`) and a one-line `16-58-49` log are not matches. Today's
  `4p.json` runs also exist as three sets (16:35-37, 16:46-49, 17:07-11); `test2.json` runs are 16:25-16:27.
- **Float caveat for self-checks:** never put an assertion exactly on a margin boundary (see `CLAUDE.md`). The new check uses
  `30 x 1.1f` and similar, away from boundaries. Self-check maps need distinct planet positions (the A* tie-break note).
- **Things you said to avoid:** do not change the blockade ranking to answer starved blockades; do not propose a colonization
  supply gate (planet churn is desired); do not add more P3 logging; the industry ideas and Option A are recorded only. Recommend
  one option with every choice, brainstorm in chat one question at a time and get a yes before coding, test first. Commit and push
  only when asked (you asked for the merges, the doc commit, the deletions and the push).
