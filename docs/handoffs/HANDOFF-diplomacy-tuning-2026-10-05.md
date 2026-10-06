# Handoff: Diplomacy Tuning

Saved from the Claude Code session named **"diplomacy_tuning"** (renamed with `/rename` during the work, after being named
"combat-v1" earlier the same session), on 2026-10-05. All the work was done on 2026-10-05. Resume it with `/resume` (pick it)
or `claude --resume "diplomacy_tuning"`. This file is the written version so nothing depends on that history. It covers the
part of the session AFTER `HANDOFF-combat-v1-2026-10-05.md` (read that one first: it describes the diplomacy-as-orders and ship
combat work, both merged and pushed).

Read `CLAUDE.md` first (architecture, conventions, the self-check pattern; "Ship combat", "Diplomacy" and "AI Tuning Log"), then
`FUTURE_FEATURES.md` (the session rules in `docs/session_configuration.md` require it before any design).

## Where things stand

**Asked:** `/update_feature_list`, then "start the diplomacy tuning pass" (the deferred pass recorded in `FUTURE_FEATURES.md`, now
with combat data). Only the **first of the pass's five items** has been worked, and it is **not finished**: the surrender and
loss-drop terms are retuned in the working tree, uncommitted, and the underlying measure turned out to be the real problem.
Items 2 to 5 have not been started.

The five items of the pass: (1) surrender and `LossDrop` calibration, (2) more wars and stance flips on `test2.json` with
combat, (3) hostility saturation (it fell with combat, so just re-read it), (4) Update Warship starts running above the
no-diplomacy baseline, (5) the five deferred diplomacy review minors in `FUTURE_FEATURES.md`.

### Done and pushed
- `/update_feature_list` (commit `43183e8`, pushed): ship combat and diplomacy-as-orders moved to `completed_features.md` (dated
  2026-10-05, under the ship_combat milestone's "Partially done"); the unfinished remainder stays in `FUTURE_FEATURES.md`.
  The combat handoff `HANDOFF-combat-v1-2026-10-05.md` was committed and pushed first (`9ede8ea`).

### Done in the working tree, NOT committed
| Area | What changed | Where |
|---|---|---|
| Surrender and loss-drop defaults, step 1 (option A) | `significantLossFraction` 0.3 to 0.2, `surrenderMidpoint` 0.6 to 0.3, `surrenderSteepness` 0.1 to 0.03 | `GameAIConstants.cs`, constants asset |
| Same, step 2 (option B), the current values | `significantLossFraction` **0.08**, `surrenderMidpoint` **0.15**, `surrenderSteepness` **0.015** (weight about 0.00005 at share 0, 3% at 0.1, 50% at 0.15, above 95% at 0.2) | `GameAIConstants.cs`, `Assets/GameAIConstantsProductionTypes.asset` |
| New log field | the 25-turn `Hostility` line gained a last field `<lossShare>` (the per-rival share the loss drop and the surrender read); the pair stores it each turn | `AITuningLogger.LogHostility`, `PlayerAI.UpdateDiplomacy` (`pair.LossShare`), `GameAI.LogEconomySummary` |
| Self-checks | tunable-default and curve-shape assertions in `CombatSelfCheck`; `DiplomacySelfCheck` uses the constants' own midpoint and its loss-terms case lowered to a 5% share | `Assets/Editor/CombatSelfCheck.cs`, `DiplomacySelfCheck.cs` |
| Docs | `CLAUDE.md` (Ship combat bullets, Hostility line format), the `tuning-log` skill (`lossShare` field), `FUTURE_FEATURES.md` (the surrender terms entry), the memory note `surrender-terms-revisit-with-diplomacy` and its `MEMORY.md` line | |

Uncommitted files: `.claude/skills/tuning-log/SKILL.md`, `Assets/Editor/CombatSelfCheck.cs`, `Assets/Editor/DiplomacySelfCheck.cs`,
`Assets/Flatspace/Diagnostics/AITuningLogger.cs`, `Assets/Flatspace/GameAI/GameAI.cs`, `Assets/Flatspace/GameAI/GameAIConstants.cs`,
`Assets/Flatspace/GameAI/PlayerAI.cs`, `Assets/GameAIConstantsProductionTypes.asset`, `CLAUDE.md`, `FUTURE_FEATURES.md`, plus the
pre-existing unstaged `.idea/.idea.FlatSpace/.idea/indexLayout.xml` deletion (leave it alone). `origin/main` is at `43183e8`.

### What was found, and how
- **The loss-share measure:** the AI's loss share is lost fleet strength over (current + lost), per rival, over 10 turns, divided
  by the WHOLE fleet; fights are single-planet skirmishes. From 12 earlier combat runs an upper-bound estimate from the `Combat` and
  `FleetHealth` lines gave mean 4-10%, max 0.15-0.44 over all attackers (only 9 of about 490 samples at 0.3 or more); the per-rival
  `lossShare` logged by the new field is lower still (per-run maximum 0.016-0.135 on `test2.json` and `4p.json`, one sample of
  about 1400 at 0.1 or more).
- **Old terms** (0.3 / 0.6 / 0.1): 26 surrenders in 12 runs, 22 of them at loss share 0-0.01 with weight 0.002-0.003 (logistic tail
  events: a weaker player at war conceding with no losses), `LossDrop` never fired.
- **Step 1 terms** (0.2 / 0.3 / 0.03): zero surrenders in 12 runs (6 `test2.json` 17:42-17:47, 6 `4p.json` 17:50-18:04), `LossDrop`
  once; economy unchanged (`test2.json` planets 37-39, arrivals 57-70; `4p.json` planets 96-100, arrivals 115-165, the weak 85-planet
  run of the earlier set did not repeat); wars and flips on `test2.json` stayed above the no-combat runs (wars 16-30, flips 16-37),
  and the share of player-turns in Amass went back up to 63-75% because surrenders no longer end wars.
- **Step 2 terms** (0.08 / 0.15 / 0.015; only 3 `test2.json` runs, 18:10-18:12): zero surrenders, `LossDrop` once (T285 at 0.08,
  back at T286), per-rival `lossShare` maximum 0.021-0.033, planets 35-38, arrivals 51-55 (the low edge of the earlier 54-80, three
  runs cannot separate it from noise), fleet-cap violations 0. **Surrender stays dormant**: real shares sit below the curve's useful
  range, so retuning constants does not make it fire.
- **Conclusion:** three retunes removed the share-0 surrenders and found nothing real beneath them; the measure itself (a share of
  the whole fleet) is too diluted. Option C below is the real fix.

### What was not verified
- **`Run All AI Self-Checks` on the final state (step 2 terms) was not run or reported.** The Play runs ran on it (so it compiles),
  and Rider is clean on every changed file, but the Editor assertions (the updated `CombatSelfCheck` curve shape and the lowered
  `DiplomacySelfCheck` case) are unconfirmed. Run it first.
- No `4p.json` runs on the step 2 terms (the step 1 `4p.json` set exists), and only 3 `test2.json` runs.
- The RED for the new assertions was argued (they fail on the previous defaults), not run.
- The weak `4p.json` run from the combat set (85 planets, first war T32) was not investigated.

## Decisions and why

- **Your picks (recommendation first each time):** step 1: **A, recalibrate to the reachable range** (over B making surrender almost
  never happen, and C shortening the window); step 2: **B, retune to the range the game produces** (over A leave dormant and C change
  the measure); **"Let me get back to you a bit later"** on the final choice below, so it is OPEN.
- **Not decided yet (the open question):** after the step 2 runs I recommended **A, stop here**: keep B, accept that surrender and
  the loss drop are dormant but harmless (they no longer fire at share 0), and revisit the measure with retreat and defense logic
  (elements 4 and 5). The alternatives: **B, do option C now** (divide the loss share by the force engaged at the fight planets
  instead of the whole fleet: a spec change plus new tests, would make surrender and the loss drop real, then retune), or **C, run
  `4p.json` on the step 2 terms first**. You have not chosen.
- **Rulings I made:** none this part of the session beyond reading your choices as above; the surrender curve shape was checked
  against the observed share range before proposing each step. The first `FUTURE_FEATURES.md` reading of element (2) as "done" for
  `/update_feature_list` (it was labelled "built, pending the tuning pass") is a judgment call I flagged to you; you accepted it by
  asking me to commit.
- **Still true from earlier handoffs:** no tuning of anything beyond what you pick; recommend one option with every choice; brainstorm
  or option one question at a time; commit and push only when asked.

## Open items, most important first

1. **Decide the surrender measure** (A stop here, B change it to the engaged force, or C run `4p.json` first), per "Decisions".
2. **Run `Run All AI Self-Checks`** on the working tree, then **commit the tuning edits** (and push if you want): nothing from this
   part of the session is committed except `43183e8` and `9ede8ea`. Suggested commit: the retune, the `lossShare` field, the
   self-check updates and the docs together.
3. **The remaining diplomacy tuning items (2) to (5)**, one at a time, each with data first:
   - (2) wars and stance flips: `test2.json` with combat has 16-30 wars a run against 12-17 without combat, and flips 14-37 against
     8-19; `4p.json` is unchanged. Cause is probably the loss term (about 190-560 ships destroyed a run at +1 hostility each, plus
     fights keeping near-ship counts high); candidates are `hostilityPerShipLost` (1) and `stanceStickiness` / `stanceHoldTurns`.
   - (3) hostility saturation: lower with combat (3-4 of 6 pairs at 30+ by T300 on `test2.json`, 0-7 of 12 on `4p.json`), so
     probably no action; re-read.
   - (4) Update Warship starts: 43-203 a run against the no-diplomacy baseline of about 99; Amass weight for `WarshipUpdate` is 4.0.
   - (5) the five deferred diplomacy minors (`FUTURE_FEATURES.md` under element (1)).
4. **Deferred combat review minors** (see `HANDOFF-combat-v1-2026-10-05.md`): an unused `turn` on `CombatSystem.Resolve` and
   `DiplomacyState.Turn` being 0 after a load, a partly damaged ship left at exactly 0 health, `significantLossFraction` of 0 making the
   drop permanent, a `LossDrop Start` with no End when contact is lost, the dock-order focus-fire tie-break.
5. **Handoffs to commit:** this file and `HANDOFF-combat-v1-2026-10-05.md` (already committed) when you want it kept; the combat
   handoff does not mention the tuning steps (this one does).
6. **Carried over:** combat elements 3 to 5 (invasion, planet defense, the AI blockading on purpose; retreat logic belongs there),
   the colonist-redirect minors, the sub-project 4 deferrals, and the recorded-only ideas in `FUTURE_FEATURES.md`.

## Gotchas for a fresh session

- **A new default in `GameAIConstants` does not reach an already-loaded asset instance:** the surrender keys are in
  `Assets/GameAIConstantsProductionTypes.asset` and were edited there too; confirm from the `LossDrop` / `Surrender` / `Hostility`
  lines that the running values are the step 2 ones (a `Surrender` line's `pSurrender` is the weight at that share).
- **A `ScoreMatrix` row whose weights are all 0 picks uniformly**; the surrender self-checks make the roulette deterministic with
  stickiness 0 and very steep curves. The surrender weight is its probability (`StanceMatrix.Weights` scales Peace and War to share
  `1 - surrender`).
- **`Hostility` line format changed** (a last `lossShare` field, added 2026-10-05; older logs lack it): analysis scripts that read
  `$9` must guard `NF >= 9`.
- **The analysis scripts lived in the session scratchpad and are gone**; the `tuning-log` skill describes the analyses. The loss-share
  upper bound I used for the first step: per player, ships destroyed (`Combat` lines, victim = the part after `->`) over the 10 turns
  up to a 25-turn mark, divided by (the `FleetHealth` warship count at that mark + lost).
- **A python probe hung in Git Bash again** (a bare `python3` call); use awk or the Edit tool.
- **Baselines for the next run:** `test2.json` with combat and the OLD terms: 10-05 16:47-16:52; step 1 terms: 17:42-17:47; step 2
  terms: 18:10-18:12 (three runs); no-combat: 14:29-14:38. `4p.json` with combat and the old terms: 17:05-17:23; step 1 terms:
  17:50-18:04. The 170-byte `7PlanetBoardState` files are default-scene stubs, not matches.
- **Check the working tree before a Play run:** the edits are uncommitted on `main` at `43183e8`; `git status` should show the files
  listed above.
