# Handoff: Diplomacy

Saved from the Claude Code session named **"diplomacy"** (renamed with `/rename` mid-session), on 2026-10-04. All the work was
done on 2026-10-04. Resume it with `/resume` (pick it) or `claude --resume "diplomacy"`. This file is the written version so
nothing depends on that history.

Read `CLAUDE.md` first (architecture, conventions, the self-check pattern, the new "Diplomacy" section, "AI Tuning Log"), then
`FUTURE_FEATURES.md` (the session rules in `docs/session_configuration.md` require it before any design). Spec and plan:
`docs/superpowers/specs/2026-10-04-diplomacy-design.md` and `docs/superpowers/plans/2026-10-04-diplomacy.md`. The earlier
handoff is `HANDOFF-colonization-redirect-2026-10-02.md`.

## Where things stand

**Asked:** "where are we" (the session-start hook had the three newest handoffs read), then `/superpowers:brainstorming` on the
first open item, element (1) of the `ship_combat` milestone: a simplified diplomacy simulation. Brainstormed one question at a time,
wrote the spec and plan, executed natively on a feature branch, ran a fresh whole-branch review, fixed what it found, merged, tuned
from Play-mode logs, pushed, and moved the item to `completed_features.md`. **Element (1) is done, merged to `main` and pushed
(`origin/main` is at `7f3b464`).** Elements (2) to (5) of the milestone (ship to ship combat, planetary invasion, planet defense,
the AI blockading on purpose) are not started.

| Area | What it does | Key code |
|---|---|---|
| Stance state | every ordered player pair holds Peace or War, a hostility score and the turn of its last change; the cut counters wait to be charged. Effective war is DERIVED: P is at war with Q when P's own stance toward Q is War, or Q's stance toward P is War and P has contact with Q | `DiplomacyState` (`GameAIMap.Diplomacy`), `PlayerKnowledge.ContactPlayers/HasContactWith` |
| Hostility | per turn per rival with contact: `H = clamp(H x (1 - 0.05) + cuts x 5 + near ships x 0.5 + 1 x log2(myStrength / rivalStrength) clamped to +-2, 0..100)`; a rival's garrison on a planet it populates (and I do not) is not a near ship; strength = sum of docked warships' Offense x (Health + Defense), mine anywhere, the rival's on planets I know, in-flight ships not counted | `HostilityCalculator`, `FleetStrength`, `BlockadeSystem.BlockadeCut.BlockerId` |
| Decision | one independent row per rival, Peace or War, weight = logistic of the score (midpoint 30, steepness 4), the held stance x3, a 10-turn hold after a change | `StanceMatrix`, `ScoreMatrix.IndependentRows` (new flag, default false) |
| Strategy switch | at the END of `ProcessResults` (after `RefreshBlockadeView` and the `switch (Strategy)`): Consolidate at war with anyone becomes Amass, Amass at war with nobody returns to Consolidate; Expand and None are untouched | `PlayerAI.UpdateDiplomacy` |
| Amass | runs the Expand routine (it used to be an empty case, which would have frozen a player) with its own research and industry tables (Warship 4.0, WarshipUpdate 4.0, industry ColonyShip 1.0); every "Consolidate only" rule goes through `IsConsolidateLike` | `PlayerAI.AmassResearchWeights/AmassIndustryWeights`, `PlayerAI.IsConsolidateLike`, `ShipTransportPlanner` |
| War gate | the assault attacks only players at war (target and force sizing); `WantedWarships` adds the assault force only against a war rival; `ContestedHolds` skips a standoff on a planet a non-war player populates (unless blockaded against me); blockades and blockade-breaking targets are unchanged | `AssaultPlanner` (optional `warRivals`), `PlayerAI.WarRivals/AssaultWarFilter` |
| War without contact | my own War stance counts without contact (no flicker), and such a rival keeps a decay-only row so the war can end | `DiplomacyState.WarRivals`, `PlayerAI.UpdateDiplomacy` |
| Legacy mode | `GameAIConstants.diplomacyEnabled` (default true) is copied by `GameAI.InitGameAI` onto `Diplomacy.Enabled`; false, and any `GameAIMap` a self-check builds directly, means every rival with contact is an enemy, no matrix, no Amass | `GameAI.InitGameAI` |
| Saves | `PlayerSave.stances` (rival, stance as int, hostility, last-change turn); older saves load all Peace | `SaveLoadSystem.GameSave.StanceSave`, `GameBoard` |
| Log lines | `Stance|<rival>|<Peace or War>|<hostility>|<cuts>|<near>|<strength>|<pWar>` (on a change), `WarForced|<rival>|<Start or End>`, `Hostility|<rival>|<H>|<myStrength>|<rivalStrength>|<nearShips>` (every 25 turns); `StrategyChange` now also logs Consolidate to Amass | `AITuningLogger`, `PlayerAI`, `GameAI.LogEconomySummary` |
| Self-check | new `FlatSpace -> AI -> Run Diplomacy Self-Check` (registered in Run All AI Self-Checks, now 11 suites) | `Assets/Editor/DiplomacySelfCheck.cs` |

### Tunables (`GameAIConstants`, in-code defaults)
`hostilityPerCut` 5, `hostilityPerNearShip` 0.5, `hostilityStrengthWeight` 1, `hostilityDecay` 0.05, `hostilityMax` 100,
`stanceMidpoint` 30, **`stanceSteepness` 4** (8 at first; also written into `Assets/GameAIConstantsProductionTypes.asset`, see Gotchas),
`stanceStickiness` 3, `stanceHoldTurns` 10, `diplomacyEnabled` true. Amass industry ColonyShip weight 1.0 (0.5 at first).

### What was verified, and how
- You ran `Run All AI Self-Checks` and reported all passing: once after the `ISet` compile fix (`d32af65`), once after the two
  review fixes (`2909db4`), and once after the steepness change (`261ac54`; you said "all self checks pass, 6 more logs").
- A fresh whole-branch review (a most-capable-model reviewer) found no Critical issue, three Important (all fixed, see Decisions) and
  five Minor (deferred). It hand-traced all 17 `Run*` methods against the production code and every expected value matched.
- Play-mode runs, analysed with `/tuning-log` (logs gitignored in `AITuningLogs/`, all to T399; fleet-cap violations 0 in every
  run, using the exact `WarShipProduction` item name):
  - **`test2.json`, steepness 8** (12 runs, 22:28-22:35 and a replicate 22:47-22:52; the replicate was meant to be steepness 4 but
    was not, see Gotchas) against 6 no-diplomacy runs (10-02 18:19-18:23): 29 war declarations per run, 8 of them at a war
    probability of 0.1 or less (random early wars), first war T4 to T56, 37 stance flips within 20 turns, about 320 of 399 turns
    in Amass; P2 ended with 9 to 11 planets against 15.5 (clear), arrivals 57 against 68, research 53 against 57.5, Update
    Warship starts about 2 times the baseline.
  - **`test2.json`, steepness 4** (6 runs, 23:08-23:14, confirmed 3.96 to 4.04 by back-solving): wars 16 per run (13 to 21),
    declarations at 0.1 or less 2.8, first war T54 to T71, flips 15, average war length 19 turns, Amass about 304 turns, but
    hostility still saturates late (5.5 of 6 pairs at 30 or more by T300, mean 77); planets 36.5 against 38.3, P2 13.0 against
    15.5 (ranges overlap now).
  - **`4p.json`, steepness 4, Amass ColonyShip 0.5** (6 runs, 23:16-23:26): first war T58 to T121, Amass about 268 turns, 6.2 of
    12 pairs at 30 or more at T300; but planets 87.5 against 97.6 (11 no-diplomacy runs from 10-02), arrivals 118 against 153,
    research 84 against 96.5.
  - **`4p.json`, steepness 4, Amass ColonyShip 1.0** (3 runs, 23:38-23:42): planets 96.0 (94 to 99), arrivals 149 (131 to 179),
    research 94 (93 to 96): back inside the no-diplomacy ranges; warship starts 839 against 700 (ranges overlap), Update Warship
    starts 173 against 99, Amass about 285 turns, 9 of 12 pairs at 30 or more at T300, `PlanetDead` 27 (one run 53, the usual
    one-or-two-planet churn, baseline runs reached 43 and 45).

### What was not verified
- Unity cannot be run from Claude Code: every per-task "compile" signal was Rider's `get_file_problems`, which cannot analyse
  brand-new files ("not included in any project") and showed empty lists that were not proof (the real compile error, the `ISet`
  ambiguity, only showed in the Editor). Your Editor runs were the real checks.
- **No self-check run is on record after `94a3576` (the asset key) and `99fe6ef` (Amass ColonyShip 1.0, which also changed an
  expected value in `DiplomacySelfCheck`).** The three `4p.json` Play runs after them finished normally, and the one assertion
  changed (`("ColonyShip", 1.0f)` in `RunAmassTablesCheck`) matches the code, but run `Run All AI Self-Checks` once to close it.
- `AITuningLogger` has no self-check by design; the three new lines were checked from real logs (formats match).
- The 3-run `4p.json` result is thin (ranges overlap the baseline on warship starts, blockades and assaults). The `4p.json`
  ColonyShip 1.0 runs were not compared against a ColonyShip 0.5 set on `test2.json`.
- The cause of the stale steepness (a loaded asset instance keeping an old default across recompiles) is the explanation that
  fits the evidence; it was not proven. Writing the key into the asset fixed the observed value.
- Saves: the round trip through `JsonUtility` is covered by `RunStanceSaveCheck`; a real save and load in Play mode was not done.

## Files and commits (all on `main`, pushed to `origin`)

| Commit | What |
|---|---|
| `6cbc3d2` | spec |
| `7217cda` | plan |
| `fd749b8` | Task 1: tunables, `DiplomacyState`, per-rival contact, `BlockerId`, suite scaffold (also removed a stray word from `PlayerAI.ProcessResults`' signature, see Decisions) |
| `63ff65e` | Task 2: `FleetStrength`, `HostilityCalculator` |
| `cd4c550` | Task 3: `ScoreMatrix.IndependentRows`, `StanceMatrix` |
| `5168b36` | Task 4: Amass runs the Expand routine, `IsConsolidateLike`, Amass tables |
| `759de63` | Task 5: the war gate on the assault and the wanted fleet |
| `fef2e30` | Task 6: `UpdateDiplomacy`, the Consolidate/Amass switch, game wiring, the three log lines |
| `9237202` | Task 7: stance saves |
| `e9b07b7` | Task 8: `CLAUDE.md` Diplomacy section, the `tuning-log` skill |
| `d32af65` | fix: `ISet` ambiguous with `Unity.VisualScripting` in `PlayerAI.cs` |
| `db88e9c` | review fix: a war without contact decays and can end |
| `2909db4` | review fixes: `ContestedHolds` releases peaceful standoffs; a rival's garrison is not a near ship |
| `ecbac19` | merge commit (`--no-ff`) |
| `261ac54` | tune: `stanceSteepness` 8 to 4 in code |
| `94a3576` | tune: `stanceSteepness: 4` written into `Assets/GameAIConstantsProductionTypes.asset` |
| `99fe6ef` | tune: Amass ColonyShip industry weight 0.5 to 1.0 |
| `7f3b464` | docs: diplomacy moved to `completed_features.md` (`/update_feature_list`), the remainder noted in `FUTURE_FEATURES.md` |

Pushes: `613a90e..99fe6ef`, then `99fe6ef..7f3b464`. New scripts, each with its `.meta` committed:
`Assets/Flatspace/GameAI/{DiplomacyState,FleetStrength,HostilityCalculator,StanceMatrix}.cs`, `Assets/Editor/DiplomacySelfCheck.cs`.
Changed: `GameAIConstants.cs`, `GameAIMap.cs`, `PlayerKnowledge.cs`, `BlockadeSystem.cs`, `ScoreMatrix.cs`, `PlayerAI.cs`,
`ShipTransportPlanner.cs`, `AssaultPlanner.cs`, `GameAI.cs`, `AITuningLogger.cs`, `SaveLoadSystem.cs`, `GameBoard.cs`,
`AllAISelfChecks.cs`, `GameAIConstantsProductionTypes.asset`, `CLAUDE.md`, `FUTURE_FEATURES.md`, `completed_features.md`,
`.claude/skills/tuning-log/SKILL.md`, and the spec (amended after the review and the tuning). The local branch `diplomacy` still
exists (merged, redundant); it was not deleted because you did not ask. The scratch workspace
`.superpowers/sdd/2026-10-04-diplomacy/` was deleted after the review. This handoff file is uncommitted. No new memory notes
were written this session.

## Decisions and why

- **Brainstorming answers (all your picks, recommendation first each time):**
  - Scope B: a peace/war gate plus a drifting attitude score (not a bare gate A, not alliances and proposals C). War triggers
    `AIStrategyAmass`; peace with all other players returns to `AIStrategyConsolidate`. The decision must be a new ScoreMatrix
    variation (your words).
  - Amass: option A, Consolidate's behaviour with a war-tuned build (it was an empty case; B stricter rules and C leave-it-empty
    were not chosen).
  - Each player holds its own stance, war needs only one side (A), and the other player is forced into war once it has contact
    with the declarer (your rule B: per-pair contact; not immediate, not after a hostile act).
  - Hostility inputs 1 (blockade cuts), 2 (rival warships near my planets), 4 (relative fleet strength) and 5 (decay); input 3
    (colonization competition) and 6 (shared borders) were left out because contests are routine and desired. You asked for 4 to
    include total defense: strength = Offense x (Health + Defense) (A), not two separate ratios or a plain sum.
  - A weighted pick with the held stance favoured and a hold period (A), not thresholds or an un-sticky roulette.
  - The gate (B): assaults on enemy-occupied planets are war-only; blockades and blockade-breaking targets stay as they are so cuts
    can happen at peace.
  - You corrected my ordering: the stance update and strategy switch go after `RefreshBlockadeView` and after the `switch (Strategy)`.
  - Execution: native, with one fresh most-capable-model reviewer at the end (your choice, my recommendation).
- **Review fixes:** Important 1 (a War stance toward a rival with no contact never ended) I fixed myself, test first. Important 2
  (assault ships parked on a rival's planet after peace feed its hostility) and Important 3 (a rival's garrison on its own planet
  beside mine counted as near ships, so a shared border drifted to war in about 10 turns) touched `ContestedHolds` and the
  hostility inputs, so I asked; you picked option A for both.
- **Tuning (all your calls):** `stanceSteepness` 8 to 4 after the first `test2.json` set showed random early wars at hostility 0;
  Amass ColonyShip industry weight 0.5 to 1.0 after `4p.json` showed about 10 planets unclaimed by T375 and 23% fewer colonization
  arrivals; **no further tuning for now**, because you expect things to change once ships can actually be destroyed.
- **Rulings I made:**
  - Removed a stray word in `PlayerAI.cs` (`public void that ProcessResults(` was an uncommitted edit in your working tree, not
    mine, which would not have compiled); restored the committed text in the Task 1 commit. Cost if wrong: you re-add what you meant.
  - Per-task verification was Rider only, with the Editor self-check run after the last task (the ruling held in the two earlier
    sub-projects); `task-done`'s command was `git diff --check` excluding `.meta` files.
  - Six plan deviations (all judged sound by the reviewer): `IndependentRows` flag on `ScoreMatrix` instead of a separate class;
    diplomacy enabled by `GameAI.InitGameAI` so older fixtures needed no edits; `EnemyWarshipTotal` filtered by the war set;
    the Amass table assertions live in `DiplomacySelfCheck`; a player's own War counts without contact; `HasContact` untouched
    (it still counts ownerless ships, `ContactPlayers` does not).
  - Fleet strength counts docked ships only on both sides (I had said it would include my in-flight ships; changed while planning).
  - Writing `stanceSteepness: 4` into the asset YAML by hand, after the logs proved the running value was 8.
- **Still true from before:** do not change the blockade ranking to answer starved blockades; a large committed force outranking a
  small blockade is desired; do not propose a colonization supply gate (churn is desired); no more P3 logging; Option A worker
  allocation and the industry ideas are recorded only.

## Open items, most important first

1. **Run `Run All AI Self-Checks` once** to close the gap after `94a3576` and `99fe6ef` (see "What was not verified").
2. **`ship_combat` element (2): ship to ship combat** is next in the milestone. Not started; use `superpowers:brainstorming` first.
   Diplomacy gives it someone to be at war with; today a war changes only the strategy, the assault gate, the wanted fleet and the
   standoff holds, and docked warships still only blockade.
3. **Revisit the diplomacy tuning once ships can be destroyed** (your call, recorded in `FUTURE_FEATURES.md`): late-game
   saturation (on `test2.json` every pair is at hostility 30 or more by T300 and players spend about 75% of the match in Amass; on
   `4p.json` 8 to 10 of 12 pairs by T375), driven by the near-ship term (about 29 rival ships beside each player's planets by T300)
   and the strength term (+2 a turn whenever the rival's fleet is out of sight; alone it settles hostility at 40); the Update
   Warship starts running 1.7 to 2 times the baseline under the Amass weight of 4.0 for `WarshipUpdate`. The lever I recommended if
   saturation persists: cap the near-ship term at about 6 ships per rival.
4. **Five deferred review minors** (in `FUTURE_FEATURES.md` under element (1); address only if symptomatic): `WarForced End` can
   fire while my own matrix also picks War; `CountNearShips` ignores a tied planet where I have population; the periodic
   `Hostility` line skips a war with a rival I have no contact with and prints 0 strengths on the first line after a load; a save
   made in Amass and loaded with `diplomacyEnabled` false stays in Amass; `ApplyBlockades` records cuts in legacy mode that nothing
   consumes.
5. **Delete the merged local branch `diplomacy`** if you want (`git branch -d diplomacy`); commit this handoff when you want it kept.
6. **Carried over, still open from earlier handoffs:** the nine deferred colonist-redirect minors (memory note
   `colonist-redirect-deferred-minors`), the sub-project 4 deferrals in `FUTURE_FEATURES.md` (producer value by planet type, the
   spare-ship calculation, the chokepoint ranking tuning option, the `ChokepointColonize` attribution fix), re-checking the
   detour ratio with more logs, and the recorded-only ideas (Option A worker allocation, industry production centers, a per-target
   shipment threshold, connectivity weighting consumers).

## Gotchas for a fresh session

- **A new default in `GameAIConstants` does not reach an already-loaded asset instance.** The Editor kept the old
  `stanceSteepness` of 8 across a recompile although the code default was 4 and the asset YAML had no key; the logs showed 8.00 in
  six runs. Write the value into `Assets/GameAIConstantsProductionTypes.asset` (as done for `stanceSteepness`) or restart/reimport,
  and verify from logs: for every `Stance` line the implied steepness is `(30 - H) / ln(1/p - 1)` with `H` field 6 and `p` field 10
  (use lines with `0.0005 < p < 0.98` and `H < 28`). The asset otherwise has no diplomacy keys, so every other new tunable runs on
  its in-code default (and the same stale-instance risk applies to any future default change).
- **`Unity.VisualScripting` has its own `ISet<T>`.** `PlayerAI.cs`, `GameAI.cs`, `GameAIMap.cs`, `Planet.cs`, `Player.cs`,
  `GameBoard.cs` and `LineDrawObject.cs` import it; use `System.Collections.Generic.ISet<int>` there (`PlayerAI.AssaultWarFilter`
  does). `AssaultPlanner.cs`, `RoutePlanner.cs` and the Editor suites do not import it.
- **Check the branch before a Play run.** At 23:34:07 a `git checkout diplomacy` (not mine) put the working tree on the old branch
  tip, where steepness is 8 and the Amass ColonyShip weight is 0.5, just before the `4p.json` runs; I caught it from the reflog and
  you switched back. `git log -1` should show `7f3b464` or later on `main`.
- **A fixture-built `GameAIMap` is legacy mode** (`Diplomacy.Enabled` false) because only `GameAI.InitGameAI` turns it on; a self-check
  that needs stances must set `map.Diplomacy.Enabled = true` (as `DiplomacySelfCheck` does). Its fixtures use a very steep curve
  (`stanceSteepness = 0.01`) so the war probability is exactly 0 or 1 and the roulette never picks a zero-weight stance.
- **Rival ships on the rival's own planet are not "near ships"** (the review fix), so a self-check that docks a rival ship to test
  the near term must put it on my planet or an empty neighbour (`RunUpdateDiplomacyCheck` docks it on `A`).
- **`task-done` exits 1 silently on a test command that prints nothing** (it reads the last output line under `set -e`); give the
  command a pass message. `git diff --check` flags the trailing spaces Unity writes in `.meta` files, so exclude them.
- **Fleet-cap invariant check:** match `ProductionSet|...|WarShipProduction` exactly. A pattern that also matches Update Warship
  items reports false violations (it showed 11 in a baseline run).
- **The analysis scripts lived in the session scratchpad and are gone** (the scratchpad became unavailable); the `tuning-log`
  skill's new "Diplomacy" section describes the analyses (first war turn, share of players in Amass, war length, forced wars,
  flips within 20 turns, where hostility settles, warship starts by strategy, fleet-cap violations, assault targets only at war
  rivals). Use awk, not python heredocs, in Git Bash.
- **Baselines:** `test2.json` no-diplomacy: the six 10-02 18:19-18:23 files (excluding the 18-21-34 stub); `4p.json` no-diplomacy:
  the 11 completed 10-02 18:25-18:58 files. A run from before diplomacy is comparable only with `diplomacyEnabled` false.
- **`/handoff` arrived as `C:/Program Files/Git/handoff` again** (Git Bash expands the leading slash). Typing it in a Windows shell
  or without the leading slash avoids it.
- **Rider:** `get_file_problems` needs `rootFolder: "C:/Projects/FlatSpace"` and cannot analyse a brand-new file; an empty problem
  list is not proof of a compile.
- **Things you said to avoid or want:** no additional tuning until ship destruction exists; recommend one option with every choice;
  brainstorm one question at a time and get a yes before coding; test first; commit and push only when asked (you asked for the
  spec and plan commits, the merge, both pushes and the list commit).
