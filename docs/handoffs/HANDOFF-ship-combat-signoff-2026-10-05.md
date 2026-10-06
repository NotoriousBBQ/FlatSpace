# Handoff: Ship Combat Signoff

Saved from the Claude Code session named **"ship_combat_signoff"** (renamed with `/rename` at the very end; earlier in the same
session it was named "combat-v1" and then "diplomacy_tuning"), on 2026-10-05. All the work was done on 2026-10-05. Resume it with
`/resume` (pick it) or `claude --resume "ship_combat_signoff"`. This file is the written version so nothing depends on that history.
It **supersedes** `HANDOFF-combat-v1-2026-10-05.md` and `HANDOFF-diplomacy-tuning-2026-10-05.md` (both written earlier in the same
session; the second one's open items and "uncommitted" notes are stale, see below): everything in them is folded in here with the
final state.

Read `CLAUDE.md` first (architecture, conventions, the self-check pattern; "Ship combat", "Diplomacy" including its Simultaneity
paragraph, "AI Tuning Log"), then `FUTURE_FEATURES.md` (the session rules in `docs/session_configuration.md` require it before any
design). Specs and plans, in order: `docs/superpowers/specs/2026-10-05-diplomacy-orders-simultaneity-design.md` and
`docs/superpowers/plans/2026-10-05-diplomacy-orders-simultaneity.md`; then `docs/superpowers/specs/2026-10-05-ship-combat-design.md`
and `docs/superpowers/plans/2026-10-05-ship-combat.md`. The owner's brainstorm answers for combat are in
`docs/superpowers/specs/2026-10-05-ship-combat-decisions-so-far.md`. The earlier handoff in the chain is
`HANDOFF-diplomacy-2026-10-04.md`.

## Where things stand

**Asked:** "go" (the session-start hook had the three newest handoffs read), then to run the self-checks, then to start the
`ship_combat` element (2) brainstorm. The brainstorm found a prerequisite (stance changes as orders, and player-order independence),
so the session ran in three parts, **all merged to `main` and pushed (`origin/main` is at `e2c85cf`, the working tree is clean
except a pre-existing unstaged `.idea/.idea.FlatSpace/.idea/indexLayout.xml` deletion that is not ours):**

1. **Diplomacy as orders and simultaneity** (merged `7b88aa4`).
2. **Ship to ship combat** (merged `18aebe8`).
3. **The diplomacy tuning pass** (commits `843506c`, `56f340b`, `e2c85cf`): closed.

Elements (3) to (5) of the `ship_combat` milestone (planetary invasion, planet defense, the AI blockading on purpose) are not started.
`/update_feature_list` was run (`43183e8`): ship combat and diplomacy-as-orders are in `completed_features.md`.

### Part 1: diplomacy as orders, simultaneity

| Area | What it does | Key code |
|---|---|---|
| Stance orders | `OrderTypeDeclareWar` / `OrderTypeMakePeace` (immediate, `Data` = rival id, empty `Origin`/`Target`, never in `CurrentAIOrders`); `PlayerAI.UpdateDiplomacy(turn, orders)` emits one per decision that differs from the held stance and writes no stance | `GameAI.ApplyStanceOrder` (pure), `GameAI.ExecuteOrder`, `PlayerAI.UpdateDiplomacy` |
| Committed stances | the Consolidate/Amass switch and the `WarForced` lines moved to the START of `ProcessResults`, reading stances committed by the previous turn's orders; a rival's declaration is first seen one `ProcessResults` later | `PlayerAI.ApplyWarState` |
| Simultaneity | `SimultaneitySelfCheck` runs three players' `ProcessResults` forward and reversed and asserts per player the same orders, stances and strategy; `GameAI.Rand` is no longer `readonly` so the check re-seeds it per player; the audit found no other cross-player write | `Assets/Editor/SimultaneitySelfCheck.cs` |

### Part 2: ship to ship combat

| Area | What it does | Key code |
|---|---|---|
| Ship state | `Ship.Damage` (damage taken, 0 = full health; stored, never current health); `WarshipStats.CurrentHealth` / `EffectiveOffense` (Offense x current health / Health stat, **no floor**); every offense reader uses it (fleet strength, blockade value, assault, in-flight offense, fleet row) | `Ship.cs`, `WarshipStats.cs`, `FleetStrength`, `BlockadeSystem`, `AssaultPlanner`, `GameAIMap.RecomputeIncomingOffense`, `FleetUIController` |
| Damage travels | `ShipFleetPayload.Damage` parallel to `Snapshots`; docked on arrival; saves via `ShipSave.damage`; no repair in flight | `Planet.PeekShipDamage`, `GameAI.ApplyShipArrival`, `PlayerAI.EmitShipOrders` |
| Combat step | pure `CombatSystem.Resolve`, once a turn after arrivals and before the planet update: war pairs by `IsAtWar(a, b, true)`; start-of-turn snapshot; pools split by hostility (floor 1); ONE POOL PER VICTIM; focus fire (lowest health fraction, then dock index) scaled by `K / (K + Defense)`; simultaneous apply; losses attributed per attacker; off in legacy mode | `Assets/Flatspace/GameAI/CombatSystem.cs`, `GameAI.RunCombat` |
| Repair, colony ships | a damaged warship heals 10% of its Health stat a turn at a planet its owner populates with no at-war warship present; a player's colony ships die when it has no warship at a planet and an at-war rival has one (everywhere, own colonies included) | `CombatSystem` |
| Results | `PlanetUpdateResultTypeWarshipsLost` (`Data` = `CombatLoss`), `...ColonyShipsLost` (`Data` = `ColonyLoss`), appended last in the enum | `Planet.cs` |
| Hostility | per ship lost +1; a significant loss share (>= 0.08 of strength over 10 turns) subtracts 4 a turn; the loss history is player-private and not saved | `HostilityCalculator`, `PlayerAI.RecordLosses` / `LossShareToward` |
| Surrender and truce | a third `StanceMatrix` choice (a flag on Peace; `Stance` is NOT extended), offered while at war, weaker and not in a truce; `OrderTypeSurrender`; `GameAI.ApplySurrender` sets both stances Peace and a 30-turn `TruceUntil` on both pair rows; in a truce `IsAtWar` is false, Declare War is refused and War is not offered; `StanceMatrix.Weights` scales Peace and War so the surrender weight IS its probability | `StanceMatrix`, `DiplomacyState.Turn` / `InTruce` / `TruceUntil`, `GameAI.ApplySurrender` |
| Log lines | `Combat`, `ColonyShipsLost`, `LossDrop`, `Surrender`, `FleetHealth`; later `lossShare` and `lossAccum` fields (see Part 3) | `AITuningLogger`, `GameAI`, `PlayerAI` |
| Self-checks | new `FlatSpace -> AI -> Run Combat Self-Check`; `DiplomacySelfCheck` gained stance orders, loss terms, the Surrender choice, the truce and `lossAccum`; Run All AI Self-Checks is now 13 suites | `Assets/Editor/CombatSelfCheck.cs` |

### Part 3: the diplomacy tuning pass (closed)

Final tuned values (`GameAIConstants`, each also written into `Assets/GameAIConstantsProductionTypes.asset`): `combatDamageK` 20,
`repairFractionPerTurn` 0.1, `hostilityPerShipLost` 1, `lossWindowTurns` 10, `significantLossFraction` **0.08**, `significantLossHostilityDrop`
4, `surrenderTruceTurns` 30, `surrenderMidpoint` **0.15**, `surrenderSteepness` **0.015**. The last three were retuned twice (0.3 / 0.6 / 0.1
as built, then 0.2 / 0.3 / 0.03, then 0.08 / 0.15 / 0.015).

| Item | Finding | Outcome |
|---|---|---|
| 1. Surrender and `LossDrop` calibration | with the built terms there were 26 surrenders in 12 runs, 22 of them at loss share 0-0.01 (weight 0.002-0.003, a logistic tail), and `LossDrop` never fired; per-rival 10-turn loss shares (the new `lossShare` field) reach only 0.02-0.14 because the share divides by the whole fleet while fights are single-planet skirmishes | retuned twice (your picks A then B); the final terms fired on REAL defeats on `4p.json` (one run: `LossDrop` 4 times at shares 0.09-0.10, two surrenders at 0.18 and 0.11, truces held); surrender stays dormant on `test2.json`; **you chose to stop here** |
| 2. More wars and flips on `test2.json` | with combat 16-31 declarations a run against 12-17, flips 13-44 against 8-19; `4p.json` unchanged (31-46 against 31-42 over 3 runs); the loss term is only about 2-7% of hostility at declaration (the new `lossAccum` field); the cause is near-ship pressure falling (rival ships beside my planets 7-15 down to 1-3), so hostility no longer saturates and cycles instead | accepted (your pick A) |
| 3. Hostility saturation | pairs still at war at T399: 4-6 of 6 on `test2.json` and 9-11 of 12 on `4p.json` without combat, 2-4 and 4-9 with it | resolved by combat |
| 4. Update Warship starts | 1.7x the no-diplomacy baseline under diplomacy; with combat 1.15x on `test2.json` and 1.5x on `4p.json`, economy unchanged | accepted (your pick A), Amass `WarshipUpdate` weight stays 4.0 |
| 5. Five deferred diplomacy minors | none showed up in any combat log | closed as "leave deferred" |

### What was verified, and how
- **Editor self-checks:** you ran `Run All AI Self-Checks` and reported all passing after: the diplomacy-orders work, its review fix,
  Tasks 1-4 of combat, all of combat, the combat review fixes (13/13 on `4d258b3`), and, when asked, the `lossAccum` change ("yes, it
  passes", which also covers the retune before it, since that run was after both). Unity cannot be run from Claude Code: every per-task
  signal was Rider's `get_file_problems`, which cannot analyse brand-new files; the Editor runs were the real checks (they caught one
  missing `using` in `SimultaneitySelfCheck`).
- **Two fresh whole-branch reviews** (most capable model). Orders/simultaneity: 0 Critical, 1 Important (the check had no teeth:
  symmetric hostility meant it also passed on the old design; fixed by forcing player 1's war), 6 Minor. Combat: 0 Critical, 2
  Important (P(surrender) was only the weight inside the stance hold; the simultaneity check never reached Surrender; both fixed), 5
  Minor. All minors are deferred (listed under Open items).
- **Play-mode runs, analysed with `/tuning-log`** (logs gitignored in `AITuningLogs/`, all to T399, fleet-cap violations 0 in every run):
  - orders, `test2.json` (6 runs) against the steepness-4 diplomacy runs: every metric overlaps; the intended change shows exactly (a
    rival's declaration seen 1 turn later in 47 of 47 cases, the declarer's Amass switch 1 turn later in 23 of 23);
  - combat, `test2.json` (6 old terms, 6 step-1 terms, 3 step-2 terms, 6 with `lossAccum`) and `4p.json` (6 old terms, 6 step-1 terms, 3
    with `lossAccum`): planets at T375 `test2.json` 35-40 and `4p.json` 85-100 (the one 85-planet run, first war T32, did not repeat in
    the 15 later `4p.json` runs, all 96-100), arrivals in range, 174-560 warships destroyed per run (about half of those built), fleet
    health at T300 93-100%, 0-2 colony ships lost per run, no declaration inside any truce.

### What was not verified
- No `4p.json` baseline on `main` at `7b88aa4` exists, so the 4p comparisons are directional (three older diplomacy runs).
- The REDs for several fixes were argued, not run (Unity cannot be run, and the old code was gone); GREEN was your Editor runs.
- Saves: the `damage` and `truceUntil` round trips are covered through `JsonUtility` in the self-checks; a real save and load in Play mode
  was not done.
- Surrender on `test2.json` never fired on a real defeat in any of 15 runs (only on `4p.json`), so its behavior there is untested.

## Files and commits (all on `main`, pushed to `origin`)

| Commit | What |
|---|---|
| `1b416a5`, `8396ede` | diplomacy-orders spec (with the combat decisions note), then its plan |
| `20b7436`, `abc75b0` | stance orders and `ApplyStanceOrder`; `UpdateDiplomacy` emits orders, `ApplyWarState`, docs |
| `f5a34f6`, `d2f4686`, `c014fa1`, `fa865fe` | `SimultaneitySelfCheck`; the missing `using`; docs; review fix (forced war so the check can fail) |
| `7b88aa4` | merge of `diplomacy-orders` (`--no-ff`) |
| `1c190f2`, `6144748` | ship combat design spec, plan |
| `5054519`, `7e5866d`, `591333c` | combat tunables and `Ship.Damage`/stats; damage travels; effective-offense readers |
| `afd2288`, `874814c`, `2b97705`, `fb5e7ac` | `CombatSystem` resolution; repair and colony-ship rule; surrender order and truce; loss terms, Surrender choice, `LossDrop`, `FleetHealth` |
| `4533205`, `4d258b3` | docs and simultaneity with losses; review fixes (surrender probability, the check reaches Surrender) |
| `18aebe8` | merge of `ship-combat` (`--no-ff`) |
| `9ede8ea` | handoff `HANDOFF-combat-v1-2026-10-05.md` |
| `43183e8` | `/update_feature_list`: combat and diplomacy-orders moved to `completed_features.md` |
| `fd09214` | handoff `HANDOFF-diplomacy-tuning-2026-10-05.md` (written mid-pass, now stale) |
| `843506c` | tune: the surrender and loss-drop retune plus the `lossShare` field on the `Hostility` line |
| `56f340b` | log: `lossAccum` on the `Stance` and `Hostility` lines |
| `e2c85cf` | docs: tuning pass closed (saturation resolved, Update Warship accepted, minors left deferred) |

Pushes: `7f3b464..7b88aa4`, `7b88aa4..18aebe8`, `..43183e8`, `..fd09214`, `..843506c`, `..56f340b`, `..e2c85cf`. New scripts, each with its
`.meta` committed: `Assets/Flatspace/GameAI/CombatSystem.cs`, `Assets/Editor/SimultaneitySelfCheck.cs`, `Assets/Editor/CombatSelfCheck.cs`.
Both feature branches were deleted after merging; the scratch workspaces were deleted after their reviews. This handoff file is
uncommitted. Memory notes written this session: `surrender-terms-revisit-with-diplomacy` (kept current to the final decision).

## Decisions and why

- **Brainstorm answers for combat (all your picks, recommendation first each time):** docked-only fights; persistent per-ship damage;
  deterministic proportional defense `K / (K + Defense)` (not flat subtraction or hit chance); focus fire on the most damaged ship, both
  sides firing simultaneously; with several war rivals a side's damage is split by hostility toward each (floor 1; your prompt "we
  track hostility toward each rival" led there); gradual repair at home; resolution plus the wiring that has to follow, no new
  tactics; damaged ships deal proportionally less (you chose B over my recommended A), **no offense floor** ("we'll handle the rout
  when we look at retreating"); colony ships destroyed when the owner has no warship and an at-war rival has one, **everywhere, own
  colonies included**; `hostilityPerShipLost` 1 (you changed my 2), plus a hostility drop for significant proportional losses (your
  idea, measured as the share of my fleet strength lost over a window, not "versus enemy strength"); **surrender as a third stance
  choice** (your pick over my fixed threshold), an order that ends the war for both sides with a 30-turn truce lock; damage travels
  with a ship in flight.
- **Your structural calls:** the combat step follows the planet-results pattern (combat appends `PlanetUpdateResult`s, surrender is an
  order); stance changes should be orders too, done first, then simultaneity, then a tuning/testing pass, then combat; execution was
  native with one fresh most-capable-model reviewer at the end (my recommendation, your choice).
- **Tuning picks:** surrender calibration option A (recalibrate to the reachable range), then B (retune to the share the game really
  produces), then A (stop here, accept dormant, revisit the measure with retreat and defense); loss attribution option B (confirm the
  cause by logging `lossAccum`) which showed the loss term is not the cause; extra wars accepted; Update Warship accepted; item 5
  closed. **Rejected or held back:** a lower `hostilityPerShipLost` (option C, moot once `lossAccum` showed 2-7%); raising
  `stanceHoldTurns` (not needed); changing the loss-share measure to the force engaged at the fight planets (held back as a spec change,
  to be raised with retreat and planet defense); shortening the loss window.
- **Rulings I made** (ledgered; cost if wrong in brackets): per-task Unity runs replaced by Rider per task plus Editor runs at the end of
  groups (a failure surfaces late; one did); a war with a rival I have no contact with gets `RivalStrength` 0 in its `StanceMatrix` row so
  I never surrender to an invisible rival (a player cannot surrender to a rival it lost sight of); the combat review's Important 1
  fixed by scaling Peace and War so the surrender weight is its probability (a beaten player surrenders somewhat sooner after the stance
  hold than the plan's numbers gave); legacy mode (`diplomacyEnabled` false) means combat is off entirely; `/update_feature_list` read
  combat element (2) as done although it was labelled "built, pending the tuning pass" (flagged to you, you accepted).
- **Still true from earlier handoffs:** do not change the blockade ranking to answer starved blockades; a large committed force outranking a
  small blockade is desired; do not propose a colonization supply gate (churn is desired); no more P3 logging; Option A worker
  allocation and the industry ideas are recorded only; recommend one option with every choice; brainstorm one question at a time and get
  a yes before coding; test first; commit and push only when asked.

## Open items, most important first

1. **Combat elements (3) planetary invasion, (4) planet defense, (5) the AI blockading on purpose** are the next milestone work (none
   started; use `superpowers:brainstorming` first). **Retreat and reinforcement logic** belongs there: the damaged-ship rout is
   deliberately unhandled (no offense floor), and the **loss-share measure** (dividing by the whole fleet makes the loss drop and
   surrender nearly dormant on `test2.json`) should be revisited there, as an option C spec change (measure against the force engaged at
   the fight planets).
2. **Deferred review minors** (not all in `FUTURE_FEATURES.md`; address only if symptomatic):
   - from the orders review: an Expand player declared on at first contact goes Expand -> Consolidate -> Amass in one turn; the
     declarer's own `StrategyChange` moved from turn t to t+1 (Amass-share metrics shift a turn against older logs); `ApplyStanceOrder`
     lives in `GameAI`, not `DiplomacyState`, and no self-check covers the `Stance` log line at execution; `SimultaneitySelfCheck`
     leaves `GameAI.Rand` re-seeded, compares only some order fields and uses mismatched turn numbers;
   - from the combat review: an unused `turn` on `CombatSystem.Resolve`, `DiplomacyState.Turn` being 0 after a load until the first update
     and no case for "no fight inside a truce"; a partly damaged ship can be left at exactly 0 health (never targeted again, still counted
     as a warship); `significantLossFraction` of 0 makes the hostility drop permanent; a `LossDrop Start` can lack an End when contact is
     lost; the dock-order focus-fire tie-break;
   - the five older diplomacy minors in `FUTURE_FEATURES.md` (closed as "leave deferred").
3. **Recorded follow-up:** `ProcessNewOrders` executes player 0's orders first (a bias on collisions between two players' orders);
   rotating the start player would change every economy number, so only if the logs show it matters.
4. **Handoffs:** `HANDOFF-combat-v1-2026-10-05.md` and `HANDOFF-diplomacy-tuning-2026-10-05.md` are committed but superseded by this file;
   the second still lists tuning items as open and the retune as uncommitted. Commit this file when you want it kept.
5. **Carried over from earlier handoffs:** the nine colonist-redirect minors (memory note `colonist-redirect-deferred-minors`), the
   sub-project 4 deferrals in `FUTURE_FEATURES.md` (producer value by planet type, the spare-ship calculation, the chokepoint ranking
   option, the `ChokepointColonize` attribution fix), re-checking the detour ratio, and the recorded-only ideas.

## Gotchas for a fresh session

- **A new default in `GameAIConstants` does not reach an already-loaded asset instance:** the combat keys are written into
  `Assets/GameAIConstantsProductionTypes.asset` and were edited there too when retuned; confirm a changed value from the logs (a
  `Surrender` line's `pSurrender` is the weight at that loss share).
- **A `ScoreMatrix` row whose weights are all 0 picks uniformly** (a zero-weight choice can still be picked), so `StanceMatrix` leaves War
  out of a truce row and the surrender self-checks make the roulette deterministic with stickiness 0 and very steep curves.
- **`Stance` is saved as an int and must not be extended** (Surrender is a flag on Peace). New `OrderType` and `PlanetUpdateResultType`
  values are appended last.
- **`DiplomacyState.Turn` is not saved**; `GameAI.GameAIUpdate` and the self-check `Turn` helper set it; a truce is tested against it.
- **`GameAIMap.GetPlanet(null)` throws** (dictionary lookup); orders with no planet use empty strings, never null.
- **Log formats changed:** the 25-turn `Hostility` line gained `|<lossShare>|<lossAccum>` and the `Stance` line `|<lossAccum>` (older logs
  lack them; guard `NF >= 9` / `NF >= 11` in analysis scripts). `Combat` is `T|P<attacker>|Combat|planet|a->v|damage|ships`, `Surrender`
  is `...|rival|lossShare|pSurrender|truceUntil`.
- **Rider:** `get_file_problems` needs `rootFolder: "C:/Projects/FlatSpace"` and cannot analyse a brand-new file ("not included in any
  project"); an empty problem list is not proof of a compile.
- **Python hung in Git Bash three times** (a heredoc with no script, a bare `python3` call); use awk or the Edit tool. The analysis scripts
  lived in the session scratchpad and are gone; the `tuning-log` skill (now with "Ship combat", `lossShare` and `lossAccum` notes)
  describes the analyses.
- **`/handoff` arrives as `C:/Program Files/Git/handoff`:** Git Bash expands the leading slash; type it without the slash or in a Windows
  shell. The exit hook (`.claude/hooks/handoff-on-exit.sh`) runs a headless `/handoff` only for NAMED sessions and then commits only new
  or changed handoff files locally (never pushes); it ignores `/clear` and `/resume` exits.
- **Baselines for the next tuning run:** `test2.json` with combat and the final terms: 10-05 22:56-23:01 (6 runs, with `lossAccum`) and
  18:10-18:12 (3 runs); `4p.json` with combat and the final terms: 10-05 23:03-23:08 (3 runs, with `lossAccum`) and 17:50-18:04 (6 runs,
  the middle terms); no-combat: `test2.json` 10-05 14:29-14:38 and `4p.json` 10-04 23:38-23:42. The 170-byte `7PlanetBoardState` files are
  default-scene stubs, not matches.
- **Check the branch before a Play run:** `git log -1` should show `e2c85cf` or later on `main`.
