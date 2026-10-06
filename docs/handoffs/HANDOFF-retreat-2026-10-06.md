# Handoff: Retreat

Saved from the Claude Code session named **"retreat"** (renamed with `/rename` mid-session; it started as the unnamed session that
read the three newest handoffs), on 2026-10-06. All the work was done on 2026-10-06. Resume it with `/resume` (pick it) or
`claude --resume "retreat"`. This file is the written version so nothing depends on that history. It builds on
`HANDOFF-ship-combat-signoff-2026-10-05.md` (combat, diplomacy-as-orders, the tuning pass); read that one for the combat background.

Read `CLAUDE.md` first (architecture, conventions, the self-check pattern; "Ship combat" now has a "Retreat" bullet, "Diplomacy",
"AI Tuning Log"), then `FUTURE_FEATURES.md` (the session rules in `docs/session_configuration.md` require it before any design).
Spec and plan for this work: `docs/superpowers/specs/2026-10-06-retreat-and-engaged-loss-share-design.md` (with two dated amendments
at the end of its loss-share section) and `docs/superpowers/plans/2026-10-06-retreat-and-engaged-loss-share.md`.

## Where things stand

**Asked:** "next" (the session-start hook had the three newest handoffs read), then "brainstorm loss-share and retreat". The
brainstorm became a spec, a plan, a native (inline) execution of the 12-task plan with one fresh whole-branch review, then two Play-mode
tuning rounds that led to follow-up changes. **Everything is merged to `main` and pushed (`origin/main` and `main` are at `14a0d40`).**
The branch `retreat` was deleted after the merge. Nothing is left uncommitted except a pre-existing unstaged
`.idea/.idea.FlatSpace/.idea/indexLayout.xml` deletion that is not ours.

| Area | What it does | Key code |
|---|---|---|
| Rename | `PlanetUpdateResult` is now `UpdateResult` (struct, `UpdateResultType`, `UpdateResultPriority`, all values); nested in `Planet`, no behavior change, nothing serialized | `Planet.cs` and 15 other `.cs` files (252 occurrences) |
| Combat core | `CombatSystem.Round` is the pure per-planet fight (over `CombatUnit`s), shared by real combat and the projection; real combat is bit-identical | `CombatSystem.cs` |
| Engaged loss share | `LossShareToward` = strength drop the rival took off me (damage included, destroyed ships in full) / my strength engaged against it, both totalled over the 10-turn window; `CombatSystem` appends a `WarshipsLost` result on EVERY fight turn (0 lost when nothing died) | `CombatLoss.Engaged`, `CombatLoss.StrengthDrop`, `PlayerAI.RecordLosses/LossShareToward/EngagedToward` |
| Fight projection | after combat each turn, one `UpdateResultTypeFightProjection` per (planet, player) in a war fight: the fight played to its end (cap 20 rounds), docked ships plus my own in-flight ships landing there; plus 8 samples with the rival seen at 1 +- 25% | `FightProjector`, `FightProjection`, `GameAI.AppendFightProjections` |
| Retreat decision | `PlanShipActions` runs `RetreatPlanner` FIRST: deterministic gate on the PERCEIVED values (mean loss >= `retreatCheckFraction` 0.3 and the rival surviving in half the samples; own colony: wiped in half the samples), then `RetreatMatrix` (stance-matrix pattern, `IndependentRows`, roulette) weighs Stay against Retreat, `1 / (1 + e^(-(loss - 0.5) / 0.1))` | `RetreatPlanner`, `RetreatMatrix`, `PlayerAI.PlanRetreats` |
| Destination | tier 1 a planet I populate; 2 a known empty planet or one populated only by players I am not at war with; 3 a war rival's planet whose REMEMBERED blockade is under my group's offense / (1 + `blockadeBreakMargin`). Tiers 1 and 2: no war-rival warship docked and not blockaded by a war rival; routes avoid war-rival blockades. Every valid planet of the best tier is its own Retreat choice weighted by `(cheapest / cost)^2` | `RetreatPlanner.Destinations`, `RetreatMatrix.DestinationShares`, `BlockadeView.OnlyFrom/Without/WithBlockers` |
| Precedence | retreating ships leave the garrison planner (`ShipTransportPlanner.Retreating`); a planet is off the assault target list for `retreatCooldownTurns` 10 (`AssaultPlanner.ExcludedTargets`) and is not refilled (`ShipTransportPlanner.RefillBlocked`, garrison 0, ships already docked stay spare). Cooldown is player-private, not saved | `PlayerAI` (`_retreatCooldown`, `CooldownPlanets`) |
| Orders | a retreat is the ordinary ship-transport order trio; no new `OrderType` | `PlayerAI.EmitShipOrders` |
| Log lines | `Retreat`, `RetreatStay`, `RetreatHeld`, `RetreatArrive`; `Hostility` gained a last `engagedStrength` field (formats below) | `AITuningLogger`, `RetreatTracker` |
| Constants | `retreatCheckFraction` 0.3, `retreatLossFraction` 0.5, `retreatSteepness` 0.1, `retreatProjectionTurns` 20, `retreatCooldownTurns` 10, `retreatRivalUncertainty` 0.25, `retreatUncertaintySamples` 8, `retreatDestinationCostExponent` 2; and recalibrated `significantLossFraction` 0.15, `surrenderMidpoint` 0.3, `surrenderSteepness` 0.03 | `GameAIConstants` and `Assets/GameAIConstantsProductionTypes.asset` (both edited) |
| Self-checks | new `FlatSpace -> AI -> Run Retreat Self-Check` (`RetreatSelfCheck`: projection, perceived projection, `BlockadeView` helpers, matrix weights and destination shares, planner tiers, perceived planner gate, planner exclusions and cooldown refill, `PlayerAI` wiring, tracker, `LargestOtherDockedOffense`); a retreat case in `SimultaneitySelfCheck`; `RunRoundCheck` and `RunAttritionShareCheck` in `CombatSelfCheck`; Run All AI Self-Checks is now 14 suites | `Assets/Editor/` |

### Log line formats (new or changed, 2026-10-06)

- `T|P|Retreat|<planet>|<destination>|<tier>|<ships>|<projectedLossPct>|<rivalSurvivorsPct>|<routeCost>|<cooldownUntil>|<pRetreat>|<exactLossPct>` (tier 3
  appends `|<rememberedBlockade>|<myOffense>`). `projectedLossPct` is the PERCEIVED loss the decision used. Logs from before the imperfect-intel
  commit (`a49608e`, 15:38) lack the last two-or-more fields: guard `NF >= 13`.
- `RetreatStay|<planet>|<projectedLossPct>|<pRetreat>`, `RetreatHeld|<planet>|<NoDestination or OwnPlanetNotWiped>|<projectedLossPct>`: on change only.
- `RetreatArrive|<planet>|<rememberedBlockade>|<rivalOffense>` (tier 3 only). Logs made before commit `19759dd` (15:18) logged the blockade VALUE
  (rival offense minus mine, 0 once my ships dock) as the second number, so they cannot show staleness.
- `Hostility|...|<lossShare>|<lossAccum>|<engagedStrength>` (every 25 turns); `lossShare` changed meaning (see decisions); guard `NF >= 10`.

### What was verified, and how

- **Editor self-checks:** you ran `Run All AI Self-Checks` and reported all passing after: the first 11 plan tasks (head `59d0b3d`), the
  strength-drop numerator (`1e5e98d`), the constants and `RetreatArrive` change (`19759dd`), and the final three changes (`a49608e`). The merged
  tree is identical to the tested `a49608e` tree, so the self-checks were not re-run on `14a0d40`.
- **Whole-branch review** (a fresh most-capable-model reviewer, read-only): found no failing assertion by hand trace, confirmed the `Round`
  extraction and the `PlanShipActions` rewrite change nothing when no retreat applies, and one Important finding (the attrition flaw, fixed, see
  decisions) plus minors (below).
- **Play-mode runs analysed with `/tuning-log`** (all to T399, fleet-cap violations 0, declarations inside a surrender truce 0, retreat-cooldown
  violations 0 in every run): 6 `test2.json` + 6 `4p.json` on the first constants (14:51-15:12), 3 + 3 after the recalibration (15:20-15:28), and one
  of each after the final change (`test2.json` 15:40-49, `4p.json` 15:42-46).

### What was not verified

- Rider's `get_file_problems` cannot see a brand-new file until the Editor regenerates the project, so per-task compile checks were weak; the Editor
  runs were the real checks. The RED of several steps was argued from Rider's unresolved-symbol errors, not run.
- The last change set (imperfect intel, weighted destination, cooldown refill) has ONE Play run per board, so its comparisons are directional. The
  declaration count looked higher in that pair (`test2.json` 38 against 27-29, `4p.json` 60 against 37-52); one run cannot say whether it is real.
- A real save and load in Play mode was not done (nothing new is saved: the cooldown, trackers and loss window are player-private).
- `RetreatStay` is still rare (0-1 a run) and the roll is still close to settled (see Findings); stance flips were not measured.

## Findings from the Play runs (for tuning)

| Per run | First retreat build (old constants) | Recalibrated constants | After imperfect intel + weighted destination + no refill (1 run) |
|---|---|---|---|
| `test2.json` ships destroyed | 69-99 (pre-retreat baseline 176-330) | 124-233 | 137 |
| `test2.json` surrenders | 14-19 (0-1 before the measure change) | 9-10 | 10 |
| `test2.json` `Retreat` lines | 57-105 | 170-237 | 120 |
| `test2.json` repeat retreats (same player and planet within 10 turns) | 13-30 | 68-106 | 28 |
| `4p.json` ships destroyed | 58-147 (pre-retreat 240-508) | 83-127 | 109 |
| `4p.json` surrenders | 18-33 | 9-13 | 18 |
| `4p.json` repeat retreats | 12-24 | 46-73 | 41 |
| planets at T375 (`test2.json` all players / `4p.json` all players) | 37-40 / 96-100 | 35-38 / 90-98 | 38 / 98 |

- The retreat preserves the fleet at no visible economic cost (ships destroyed fell 2 to 6 times against the pre-retreat baselines; planets and colonize arrivals held).
- With the old constants about half of all wars ended by surrender (median share at surrender 0.17-0.18); after the recalibration about a third do, at
  a median 0.31-0.37. The loss drop barely moved (31-39 episodes on `test2.json`).
- 93-99% of retreats are at a projected loss of 100%: fights are lopsided, so a rival seen 25% stronger or weaker rarely flips them. After imperfect intel
  the perceived loss is 100% in about 62% of retreats, 90-99% in 20%, 70-89% in 15-19%, none below 70%; the retreat weight is 0.9-0.99 in about 30%.
- Destination predictability fell clearly: for planets retreated from at least 4 times, 45% of retreats go to the most common destination (it was 69-90%),
  with about 4 distinct destinations per such planet (it was 1.5-2.6).
- `RetreatArrive` (after the fix): tier 3 mostly lands on rival planets with no rival ships docked (remembered 0, actual 0); about a quarter show real rival
  offense, a few far worse than remembered (for example 26.5 against 104, 3.4 against 57).

## Files and commits (all on `main`, pushed to `origin`)

| Commit | What |
|---|---|
| `269277a`, `f064e84` | spec, plan (committed on `main` before branching) |
| `de8e573` | rename `PlanetUpdateResult` to `UpdateResult` |
| `bbf8dd7` | tunables and `RetreatSelfCheck` shell |
| `bf6fe26` | `CombatSystem.Round` extraction |
| `095fd6f` | engaged-force loss share (`CombatLoss.Engaged`, a result every fight turn) |
| `70465e0` | `FightProjector`, `FightProjection`, `AppendFightProjections` |
| `ea77e26`, `47ba068`, `572bc42` | `BlockadeView` helpers; `RetreatMatrix`; `RetreatPlanner` |
| `e5c1c9d`, `2cc7d4c` | `PlayerAI` wiring (retreat first, exclusions, cooldown); the four log lines and `RetreatTracker` |
| `59d0b3d`, `7b93233` | docs and the simultaneity retreat case; the `.meta` files of the four new scripts |
| `1e5e98d` | loss share numerator is the per-round strength drop (after the review) |
| `19759dd` | recalibrated constants (0.15 / 0.3 / 0.03) and the `RetreatArrive` fix (`BlockadeSystem.LargestOtherDockedOffense`) |
| `27874ae`, `a49608e` | no refill during the cooldown; imperfect intel and the weighted destination |
| `14a0d40` | merge of `retreat` (`--no-ff`) |

Push: `e2c85cf..14a0d40`. New scripts (each with its `.meta` committed): `Assets/Flatspace/GameAI/FightProjector.cs`, `RetreatMatrix.cs`,
`RetreatPlanner.cs`, `RetreatTracker.cs`, `Assets/Editor/RetreatSelfCheck.cs`. A local tag `retreat-v1-fallback` marks `19759dd` (before imperfect
intel); it was NOT pushed. This handoff file is uncommitted. Memory note written and kept current: `surrender-terms-revisit-with-diplomacy`.

## Decisions and why

- **Brainstorm answers (all your picks, recommendation first each time):** retreat decides per fight planet and counts ship defense (the projection uses the real
  `K / (K + Defense)`, not a linear add-on); the fight is projected to its end and recomputed whenever forces change, counting my own in-flight ships landing
  there (not the rival's in-flight ships); retreat when a rival survives AND I would lose at least a threshold of my strength (a win stays however costly);
  destination: my own populated planet, then a known empty or not-at-war planet, then a war rival's planet with a low remembered blockade (offense above
  `value x (1 + blockadeBreakMargin)`), else stay; blockades count only from players at war with me, for the destination AND the route (an unknown remembered
  blocker is ignored); retreat runs first and wins over the assault, holds and garrison planner, with a cooldown; at a planet I populate retreat only when wiped
  out; the loss share is lost over ENGAGED strength, window total. You also asked to reuse the existing `UpdateResult` / `ScoreMatrix` / `GameAIOrder` pattern
  (new matrix types acceptable) and to rename `PlanetUpdateResult` to `UpdateResult`.
- **Not fully deterministic (your instruction):** a deterministic gate decides when retreat is considered, then retreat-or-stay is a weighted `ScoreMatrix`
  roulette (my first design had a fixed threshold; you rejected it). Later, on the gaming concern, you chose imperfect intel (option 1) and a weighted
  destination (option 4) from my list, and approved adding the no-refill cooldown change as a third item.
- **Review finding, your call:** the new numerator (strength of ships only when destroyed) read about 0.004 for a wipe-out by attrition. I did not change it
  on my own (it was the spec's measure); you chose a per-round strength-drop numerator. The `StrengthLost` field stays for the ships-destroyed accounting.
- **Constants:** you asked to recalibrate after the first Play data. I used steepness 0.03, not the "about 0.04" I suggested, to keep the share-0 weight at 0.00005
  (0.04 would be 0.00055, and earlier tuning found tail weights of 0.002-0.003 firing surrenders at share 0). After the final runs you said the surrender
  rate "is fine in its current state".
- **Rulings I made** (cost if wrong in brackets): a branch in the main working directory instead of a worktree, because your Unity Editor reads that directory
  (none); Rider per task plus Editor runs batched later because Unity cannot be run from here (a failure surfaces late; none did); the imperfect-intel samples are
  drawn in the engine step, once before any player decides, so decisions cannot depend on player order (they are the player's own perception, drawn centrally);
  the projected loss counts a wounded survivor as partly lost (a nearly dead "winner" should still leave); the `RetreatArrive` second value changed to the rival's
  docked offense after the first version read 0 in 34 of 36 arrivals; tags and the older handoffs left alone.
- **Rejected or held back:** a fixed retreat threshold with no roll; per-ship rout; retreat everywhere including own colonies (chosen: only on a wipe-out);
  the stakes tilt and a commitment delay (options 2 and 5 of the unpredictability list, held for later); raising `retreatRivalUncertainty` to about 0.4 (I advised
  against it for now, it costs ships).
- **Still true from earlier handoffs:** do not change the blockade ranking to answer starved blockades; a big committed force outranking a small blockade is desired;
  no colonization supply gate; Option A worker allocation and the industry ideas are recorded only; recommend one option with every choice (your global CLAUDE.md);
  brainstorm one question at a time and get a yes before coding; test first; commit and push only when asked (this session you asked for the spec and plan commits,
  the fallback commit, and the merge and push).

## Open items, most important first

1. **Combat elements (3) planetary invasion, (4) planet defense, (5) the AI blockading on purpose** are the next milestone work (none started; use
   `superpowers:brainstorming` first). Retreat from my own colonies except on a wipe-out belongs with planet defense.
2. **Confirm the last change set with more runs.** One Play run per board followed it. Run a few more on both boards and read, with `/tuning-log`: whether
   declarations are really higher (38 and 60 against 27-29 and 37-52), repeat retreats, `RetreatStay` frequency, ships destroyed and planets at T375.
3. **More unpredictability, if wanted:** the stay-or-retreat decision is still close to settled because fights are lopsided. Options held back: a stakes tilt
   (shift the willingness to stay by planet value, chokepoint, strategy), a commitment delay, a higher `retreatRivalUncertainty` (about 0.4, costs ships).
4. **Deferred minors from the review** (not in `FUTURE_FEATURES.md`; address only if symptomatic): `RetreatStay` has no End line (the spec asked for Start and End);
   a known but unseen, unremembered war-rival planet reads blockade 0 in tier 3; the roll repeats every turn so `pRetreat` is per turn, not per fight (about 47% to
   retreat within 5 turns at the gate); `ProjectedLossFraction` is quadratic in health (about 17% even damage already reads as 30%), keep it in mind when tuning
   `retreatCheckFraction`; the category-5 unlock count drops retreating stranded ships but not retreating colonized ones; `_retreatCooldown` is never pruned; the
   retreat destination can still be a garrison sink the same turn; stale comment at `DiplomacySelfCheck.cs:1035`; no test of a cooldown planet being excluded from the
   assault on a later turn, nor of `ContestedHolds` filtering a retreating planet.
5. **Still out of scope for retreat:** per-ship rout (a damaged ship leaving alone), retreat of colony ships (the existing rule destroys them), rival in-flight ships in
   the projection, reinforcement logic.
6. **Housekeeping:** `retreat-v1-fallback` is a local tag (push or delete it); `.idea/.idea.FlatSpace/.idea/indexLayout.xml` deletion is not ours; the older handoffs
   `HANDOFF-combat-v1-2026-10-05.md` and `HANDOFF-diplomacy-tuning-2026-10-05.md` are superseded by `HANDOFF-ship-combat-signoff-2026-10-05.md`, which this file builds on;
   commit this file when you want it kept. Carried over from earlier handoffs: the nine colonist-redirect minors, the sub-project 4 deferrals in `FUTURE_FEATURES.md`
   (producer value by planet type, the spare-ship calculation, the chokepoint ranking option, the `ChokepointColonize` attribution fix), the diplomacy minors, and the
   recorded-only ideas. `FUTURE_FEATURES.md` still lists the retreat entry under element (2); run `/update_feature_list` when you consider it done.

## Gotchas for a fresh session

- **Rider cannot see new scripts** until the Editor regenerates the project (it saw the four new files only after your later Editor runs); an unresolved-symbol error in a
  file that uses a brand-new type is expected, and an empty problem list is not proof of a compile.
- **Editing code while a Play run is going** recompiles the Editor and can disturb the run; wait for the user to say the runs are done.
- **Write needs a fresh Read after a `sed` rename** ("file modified since read"); after `sed -i` use Read, then Write or Edit.
- **A new default in `GameAIConstants` does not reach the loaded asset:** every key here is also written into `Assets/GameAIConstantsProductionTypes.asset`; confirm a changed
  value from the logs.
- **Self-checks make the roll deterministic** with `retreatSteepness` 0.001 and `retreatDestinationCostExponent` 50 (weights 1 and about 1e-15, so the nearest planet always
  wins); a seeded `System.Random` is passed to `AppendFightProjections` where the sample draw matters (`SimultaneitySelfCheck` uses 900). Do not put a self-check exactly on
  a gate or threshold boundary.
- **`CombatLoss.StrengthDrop`, not `StrengthLost`, is the loss share's numerator** (`RecordLosses` stores `StrengthDrop`); `StrengthLost` is destroyed ships only.
  `LossShareToward(rival, turn)` no longer takes a strength argument.
- **A zero-health ship** is skipped by combat and the projection; a group made only of such ships never projects (an existing deferred combat minor).
- **`FormatLine` is `params`**; tier 3 `Retreat` lines carry two extra fields after `exactLossPct`, so parse by field count.
- **Python hung in Git Bash again** in earlier sessions; this session's analyses used awk scripts in the scratchpad (gone after the session): metrics per run (planets at T375
  from `Economy`, `ColonizeArrive`, `PlanetDead`, sum of `Combat` ships, `Stance` War count, `Surrender`, `LossDrop`), retreat bins (tier, loss%, `pRetreat`), repeats within 10 turns,
  cooldown violations (a `Retreat` planet that becomes an `AssaultTarget`/`BlockadeTarget` of that player before `cooldownUntil`), destination concentration, and the fleet-cap
  check (`WarshipBoost` multiplier 0 with a `ProductionSet|...|WarShipProduction` on the same turn and player, minus `BlockadedProduction`; match the item name exactly, a loose
  `warship` match counts `Update Warship` starts and reports false violations). The `tuning-log` skill describes them.
- **Baselines:** `test2.json` pre-retreat with combat 10-05 22:56-23:01 (6), `4p.json` 10-05 23:03 and 23:06 (2 complete) and 17:50-18:02 (6, older terms); retreat build with the
  old constants `test2.json` 10-06 14:51-14:57 (6), `4p.json` 15:00-15:12 (6); recalibrated constants `test2.json` 15:20-15:22 (3), `4p.json` 15:24-15:28 (3); after the final change
  `test2.json` 15:40-49, `4p.json` 15:42-46. The 170-byte `7PlanetBoardState` files are default-scene stubs, not matches.
- **Check the branch before a Play run:** `git log -1` should show `14a0d40` or later on `main`.
