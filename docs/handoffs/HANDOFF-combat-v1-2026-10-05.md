# Handoff: Combat v1

Saved from the Claude Code session named **"combat-v1"** (renamed with `/rename` at the end), on 2026-10-05. All the work was
done on 2026-10-05. Resume it with `/resume` (pick it) or `claude --resume "combat-v1"`. This file is the written version so
nothing depends on that history.

Read `CLAUDE.md` first (architecture, conventions, the self-check pattern; the new "Ship combat" section, "Diplomacy" with the
Simultaneity paragraph, "AI Tuning Log"), then `FUTURE_FEATURES.md` (the session rules in `docs/session_configuration.md` require
it before any design). Specs and plans, in order: `docs/superpowers/specs/2026-10-05-diplomacy-orders-simultaneity-design.md` and
`docs/superpowers/plans/2026-10-05-diplomacy-orders-simultaneity.md`; then `docs/superpowers/specs/2026-10-05-ship-combat-design.md`
and `docs/superpowers/plans/2026-10-05-ship-combat.md`. The owner's brainstorm answers for combat are recorded in
`docs/superpowers/specs/2026-10-05-ship-combat-decisions-so-far.md`. The earlier handoff is `HANDOFF-diplomacy-2026-10-04.md`.

## Where things stand

**Asked:** "go" (the session-start hook had the three newest handoffs read), then to run the self-checks, then to start the
`ship_combat` element (2) brainstorm. The brainstorm turned up a prerequisite (diplomacy decisions through orders and player-order
independence), so the work ran in two parts: **(A) diplomacy as orders and simultaneity**, then **(B) ship to ship combat**. Both
are merged to `main` and pushed (`origin/main` is at `18aebe8`). The tuning/testing pass for each was run from your logs. Elements
(3) to (5) of the milestone (planetary invasion, planet defense, the AI blockading on purpose) are not started.

### Part A: diplomacy as orders, simultaneity (merged `7b88aa4`, pushed)

| Area | What it does | Key code |
|---|---|---|
| Stance orders | `OrderTypeDeclareWar` / `OrderTypeMakePeace` (immediate, `Data` = rival id, empty `Origin`/`Target`, never in `CurrentAIOrders`); `PlayerAI.UpdateDiplomacy(turn, orders)` emits one per decision that differs from the held stance and writes no stance | `GameAI.ApplyStanceOrder` (pure), `GameAI.ExecuteOrder`, `PlayerAI.UpdateDiplomacy` |
| Committed stances | the Consolidate/Amass switch and the `WarForced` lines moved to the START of `ProcessResults`, reading stances committed by the previous turn's orders; a rival's declaration is first seen one `ProcessResults` later | `PlayerAI.ApplyWarState` |
| Simultaneity | `SimultaneitySelfCheck` runs three players' `ProcessResults` forward and reversed over the same state and asserts per player the same orders, stances and strategy; `GameAI.Rand` is no longer `readonly` so the check can re-seed it per player; audit found no other cross-player write | `Assets/Editor/SimultaneitySelfCheck.cs` |

### Part B: ship to ship combat (merged `18aebe8`, pushed)

| Area | What it does | Key code |
|---|---|---|
| Ship state | `Ship.Damage` (damage taken, 0 = full health; stored, never current health); `WarshipStats.CurrentHealth` / `EffectiveOffense` (Offense x current health / Health stat, **no floor**); every offense reader uses it (fleet strength, blockade value, assault, in-flight offense, fleet row) | `Ship.cs`, `WarshipStats.cs`, `FleetStrength`, `BlockadeSystem`, `AssaultPlanner`, `GameAIMap.RecomputeIncomingOffense`, `FleetUIController` |
| Damage travels | `ShipFleetPayload.Damage` parallel to `Snapshots`; docked on arrival; saves via `ShipSave.damage`; no repair in flight | `Planet.PeekShipDamage`, `GameAI.ApplyShipArrival`, `PlayerAI.EmitShipOrders` |
| Combat step | pure `CombatSystem.Resolve`, once a turn after arrivals and before the planet update: war pairs by `IsAtWar(a, b, true)`; start-of-turn snapshot; pools split by hostility (floor 1); ONE POOL PER VICTIM; focus fire (lowest health fraction, then dock index) scaled by `K / (K + Defense)`; simultaneous apply; losses attributed per attacker; off in legacy mode | `Assets/Flatspace/GameAI/CombatSystem.cs`, `GameAI.RunCombat` |
| Repair, colony ships | a damaged warship heals 10% of its Health stat a turn at a planet its owner populates with no at-war warship present; a player's colony ships die when it has no warship at a planet and an at-war rival has one (everywhere, own colonies included) | `CombatSystem` |
| Results | `PlanetUpdateResultTypeWarshipsLost` (`Data` = `CombatLoss`), `...ColonyShipsLost` (`Data` = `ColonyLoss`), appended last in the enum | `Planet.cs` |
| Hostility | per ship lost +1; a significant loss share (>= 0.3 of strength over 10 turns) subtracts 4 a turn; the loss history is player-private and not saved | `HostilityCalculator`, `PlayerAI.RecordLosses` / `LossShareToward` |
| Surrender and truce | a third `StanceMatrix` choice (a flag on Peace; `Stance` is NOT extended), offered while at war, weaker and not in a truce; `OrderTypeSurrender`; `GameAI.ApplySurrender` sets both stances Peace and a 30-turn `TruceUntil` on both pair rows; in a truce `IsAtWar` is false, Declare War is refused and War is not offered; `StanceMatrix.Weights` scales Peace and War so the surrender weight IS its probability | `StanceMatrix`, `DiplomacyState.Turn` / `InTruce` / `TruceUntil`, `GameAI.ApplySurrender` |
| Log lines | `Combat`, `ColonyShipsLost`, `LossDrop`, `Surrender`, `FleetHealth` (formats in `CLAUDE.md` "AI Tuning Log" and the `tuning-log` skill) | `AITuningLogger`, `GameAI` |
| Self-checks | new `FlatSpace -> AI -> Run Combat Self-Check`; `DiplomacySelfCheck` gained stance orders, loss terms, the Surrender choice and the truce; Run All AI Self-Checks is now 13 suites | `Assets/Editor/CombatSelfCheck.cs` |

### Tunables (`GameAIConstants`; each also has a key in `Assets/GameAIConstantsProductionTypes.asset`)
`combatDamageK` 20, `repairFractionPerTurn` 0.1, `hostilityPerShipLost` 1 (you changed it from 2), `lossWindowTurns` 10,
`significantLossFraction` 0.3, `significantLossHostilityDrop` 4, `surrenderTruceTurns` 30, `surrenderMidpoint` 0.6,
`surrenderSteepness` 0.1.

### What was verified, and how
- **Editor self-checks:** you ran `Run All AI Self-Checks` and reported all passing after: the diplomacy-orders work, its review
  fix, Tasks 1-4 of combat, all of combat, and the combat review fixes (last on the final tree `4d258b3`: 13/13 suites).
  Unity cannot be run from Claude Code, so every per-task compile signal was Rider's `get_file_problems`, which cannot analyse
  brand-new files; the Editor runs were the real checks (they caught one missing `using` in `SimultaneitySelfCheck`).
- **Two fresh whole-branch reviews** (most capable model). Orders/simultaneity: 0 Critical, 1 Important (the check had no teeth:
  symmetric hostility meant it also passed on the old design; fixed by forcing player 1's war), 6 Minor (deferred). Combat: 0
  Critical, 2 Important (P(surrender) was only the weight inside the stance hold; the simultaneity check never reached Surrender;
  both fixed), 5 Minor (deferred).
- **Play-mode runs, analysed with `/tuning-log`** (logs gitignored in `AITuningLogs/`, all to T399, fleet-cap violations 0 in
  every run):
  - **Orders, `test2.json`** (6 runs, 14:29-14:38) against the steepness-4 diplomacy runs (10-04 23:08-23:14): ranges overlap on
    every metric; the intended change shows exactly: a rival's declaration is seen 1 turn later in 47 of 47 cases (it was 0 or 1
    depending on player id), the declarer's Amass switch 1 turn later in 23 of 23 (was 0 in 35 of 35). First war T62-117 against
    T54-71 (two runs later than any baseline, read as noise).
  - **Combat, `test2.json`** (6 runs, 16:47-16:52) against the orders runs: planets at T375 35-39 (avg 37.2 against 37.5),
    arrivals 54-80 against 58-84; 174-344 warships destroyed per run (about half of those built), about 25 pairs fight per run,
    fleet health at T300 93-100% (repair keeps pace), colony ships lost 0-2 per run, no declaration inside any of 14 truces. Wars
    declared 17-26 against 12-17 and forced wars 14-25 against 9-15 (more wars: ship losses now feed hostility), Amass share
    54-73% against 66-80%, hostility saturation lower (3-4 of 6 pairs at 30+ by T300 against 4-6).
  - **Combat, `4p.json`** (6 runs, 17:05-17:23) against three diplomacy runs of 10-04 23:38-23:42 (directional only, they predate
    orders): planets at T375 85-100, arrivals 117-163 against 131-179, research 72-110 against 93-96, `PlanetDead` 0-26 against
    10-53, blockade events 15-82 against 106-115, pairs at 30+ at T300 0-7 of 12 against 8-10, 220-560 ships destroyed per run,
    only 2 colony ships lost in all six runs, no declaration inside any truce.

### What was not verified
- No `4p.json` baseline on `main` at `7b88aa4` exists, so the 4p comparison is rough (three older runs, three players-worth of
  difference in code).
- **Surrender is behaving as a random tail event, not as defeat.** 14 surrenders in 6 `test2.json` runs and 12 in 6 `4p.json`
  runs; all but two happened at loss share 0-0.01 with `pSurrender` 0.002-0.003 (the largest 0.18 share at 0.015), i.e. a weaker
  player at war occasionally concedes with no real losses. `LossDrop` never fired in any run. This is recorded, not tuned (you
  said to leave the surrender terms for the diplomacy tuning; memory note `surrender-terms-revisit-with-diplomacy`).
- RED for several fixes was argued, not run (Unity cannot be run, and the old design was gone); GREEN was your Editor runs.
- Saves: the `damage` and `truceUntil` round trips are covered through `JsonUtility` in the self-checks; a real save and load in
  Play mode was not done.
- The one weak `4p.json` run (17:09: 85 planets, 23 `PlanetDead`, first war T32, a player with no fleet at T300) was not
  investigated.

## Files and commits (all on `main`, pushed)

| Commit | What |
|---|---|
| `1b416a5` | diplomacy-orders spec and the combat decisions note |
| `8396ede` | diplomacy-orders plan |
| `20b7436`, `abc75b0` | stance orders and `ApplyStanceOrder`; `UpdateDiplomacy` emits orders, `ApplyWarState`, docs |
| `f5a34f6`, `d2f4686` | `SimultaneitySelfCheck`; the missing `using` fix |
| `c014fa1` | docs: audit result, `CLAUDE.md`, resume pointer |
| `fa865fe` | review fix: the check forces player 1's war |
| `7b88aa4` | merge of `diplomacy-orders` (`--no-ff`) |
| `1c190f2` | ship combat design spec |
| `6144748` | ship combat plan |
| `5054519`, `7e5866d`, `591333c` | combat tunables and `Ship.Damage`/stats; damage travels; effective offense readers |
| `afd2288`, `874814c` | `CombatSystem` resolution; repair and the colony-ship rule |
| `2b97705`, `fb5e7ac` | surrender order and truce; loss terms, the Surrender choice, `LossDrop`, `FleetHealth` |
| `4533205`, `4d258b3` | docs and simultaneity with losses; review fixes (surrender probability, the check reaches Surrender) |
| `18aebe8` | merge of `ship-combat` (`--no-ff`) |

Pushes: `7f3b464..7b88aa4`, then `7b88aa4..18aebe8`. New scripts, each with its `.meta` committed:
`Assets/Flatspace/GameAI/CombatSystem.cs`, `Assets/Editor/SimultaneitySelfCheck.cs`, `Assets/Editor/CombatSelfCheck.cs`. Changed
(selection): `GameAI.cs`, `PlayerAI.cs`, `DiplomacyState.cs`, `StanceMatrix.cs`, `HostilityCalculator.cs`, `WarshipStats.cs`,
`Ship.cs`, `Planet.cs`, `FleetStrength.cs`, `BlockadeSystem.cs`, `AssaultPlanner.cs`, `GameAIMap.cs`, `GameAIConstants.cs`,
`SaveLoadSystem.cs`, `FleetUIController.cs`, `AITuningLogger.cs`, the constants asset, `DiplomacySelfCheck.cs`,
`AllAISelfChecks.cs`, `CLAUDE.md`, `FUTURE_FEATURES.md`, `.claude/skills/tuning-log/SKILL.md`. Both feature branches were deleted
after merging; the scratch workspaces were deleted after their reviews. This handoff file and
`.idea/.idea.FlatSpace/.idea/indexLayout.xml` (a pre-existing unstaged deletion, left alone) are uncommitted. New memory note
written this session: `surrender-terms-revisit-with-diplomacy`.

## Decisions and why

- **Brainstorm answers for combat (all your picks, recommendation first each time):** docked-only fights (not in-flight or
  arrival-triggered); persistent per-ship damage; deterministic proportional defense `K / (K + Defense)` (not flat subtraction or
  hit chance); focus fire on the most damaged ship, both sides firing simultaneously; with several war rivals a side's damage is
  split by hostility toward each (floor 1; your prompt "we track hostility toward each rival" led there); gradual repair at home
  (not full repair, not Update Warship only); resolution plus the wiring that has to follow, no new tactics; damaged ships deal
  proportionally less (you chose B over my recommended A), **no offense floor** ("we'll handle the rout when we look at
  retreating"); colony ships destroyed when the owner has no warship and an at-war rival has one, **everywhere, own colonies
  included** (you chose that over my gentler options); `hostilityPerShipLost` 1 (you changed my 2), plus a hostility drop for
  significant proportional losses (your idea, measured as the share of my fleet strength lost over a window, not "versus enemy
  strength", which the existing strength term already covers); **surrender as a third stance choice** (your pick over my
  recommended fixed threshold), an order that ends the war for both sides with a 30-turn truce lock; damage travels with a ship in
  flight (not free full repair on departure).
- **Your structural calls:** the combat step must follow the planet-results pattern (combat appends `PlanetUpdateResult`s, surrender
  is an order); stance changes should be orders too (the retrofit), done first, then simultaneity, then a tuning/testing pass,
  then combat; execution was native with one fresh most-capable-model reviewer at the end (my recommendation, your choice).
- **Surrender terms are a lever to revisit, not to tune now** ("Remember surrender terms when we revisit diplomacy, don't act on
  it now").
- **Rulings I made** (ledgered; cost if wrong in brackets):
  - Per-task Unity runs replaced by Rider per task plus Editor runs at the end of groups (a failure surfaces late; one did, the
    missing `using`).
  - Orders: the spec's "pure `DiplomacyState` method" became `GameAI.ApplyStanceOrder` with the log in `ExecuteOrder`.
  - Combat: a war with a rival I have no contact with gets `RivalStrength` 0 in its `StanceMatrix` row, so I never surrender to an
    invisible rival (the plan said `float.MaxValue`; the spec says surrender when my VISIBLE strength is below the rival's).
  - Combat review Important 1 fixed by scaling Peace and War so the surrender weight is its probability (not by only logging the
    real probability): a beaten player now surrenders somewhat sooner after the stance hold than the plan's numbers gave
    (reviewer's example: about 15% against the weight).
  - Legacy mode (`diplomacyEnabled` false): combat is off entirely (decided in the spec after a self-review found it open).
- **Still true from earlier handoffs:** do not change the blockade ranking to answer starved blockades; a large committed force
  outranking a small blockade is desired; do not propose a colonization supply gate (churn is desired); no more P3 logging;
  Option A worker allocation and the industry ideas are recorded only.

## Open items, most important first

1. **Diplomacy tuning pass** (now has data; your call to start it, per `FUTURE_FEATURES.md` and the memory note): surrender at tiny
   odds (a steeper curve, a higher midpoint, or a minimum loss share would stop surrenders with no losses; `LossDrop` never fires
   at 0.3 over 10 turns, so its threshold or window may be wrong); the `test2.json` rise in wars and flips (ship losses feed
   hostility); late-game hostility saturation (lower now, re-read); Update Warship starts; the five deferred diplomacy minors in
   `FUTURE_FEATURES.md`.
2. **Run `/update_feature_list`** if you want: `FUTURE_FEATURES.md` still lists element (2) as "built, pending the tuning pass" and
   the diplomacy-orders prerequisite as "moved to completed_features when merged".
3. **Commit this handoff** when you want it kept.
4. **Next milestone elements:** (3) planetary invasion, (4) planet defense, (5) the AI blockading on purpose. Retreat and
   reinforcement logic (the damaged-ship rout is deliberately unhandled) belongs with (4)/(5). Use `superpowers:brainstorming`
   first.
5. **Deferred review minors** (not in `FUTURE_FEATURES.md`; address only if symptomatic): from the orders review: an Expand player
   declared on at first contact goes Expand -> Consolidate -> Amass in one turn; the own-declaration `StrategyChange` moved from
   turn t to t+1 (Amass-share metrics shift a turn against older logs); `GameAI.ApplyStanceOrder` lives in `GameAI`, not
   `DiplomacyState`, and no self-check covers the `Stance` log line at execution; `SimultaneitySelfCheck` leaves `GameAI.Rand`
   re-seeded and compares only some order fields; the fixture uses mismatched turn numbers. From the combat review:
   `CombatSystem.Resolve` takes a `turn` it does not use and the truce reads `DiplomacyState.Turn` (0 after a load until the first
   update; no case for "no fight inside a truce"); a partly damaged ship can be left at exactly 0 health (never targeted again,
   still counted as a warship); `significantLossFraction <= 0` makes the hostility drop permanent; a `LossDrop Start` can lack an
   End when contact is lost; the focus-fire tie-break is dock order.
6. **Recorded follow-up:** `ProcessNewOrders` executes player 0's orders first (a bias on collisions between two players'
   orders); rotating the start player would change every economy number, so only if the logs show it matters.
7. **Carried over from earlier handoffs:** the nine colonist-redirect minors (memory note `colonist-redirect-deferred-minors`),
   the sub-project 4 deferrals in `FUTURE_FEATURES.md` (producer value by planet type, the spare-ship calculation, the chokepoint
   ranking option, the `ChokepointColonize` attribution fix), re-checking the detour ratio, and the recorded-only ideas.

## Gotchas for a fresh session

- **A new default in `GameAIConstants` does not reach an already-loaded asset instance** (the Editor kept an old
  `stanceSteepness` across recompiles last session). The nine combat keys were written into
  `Assets/GameAIConstantsProductionTypes.asset`; any future default change needs the same, verified from logs.
- **A `ScoreMatrix` row whose weights are all 0 picks uniformly** (a zero-weight choice can still be picked), so
  `StanceMatrix` leaves War out of a truce row and the surrender self-checks make the roulette deterministic with stickiness 0
  and a very steep curve. Do not "fix" a test by giving a choice weight 0 and expecting it never to be chosen.
- **`Stance` is saved as an int and must not be extended** (Surrender is a flag on Peace). New `OrderType` and
  `PlanetUpdateResultType` values are appended last.
- **`DiplomacyState.Turn` is not saved**; it is set by `GameAI.GameAIUpdate` and by the self-check `Turn` helper. A truce is
  tested against it.
- **`GameAIMap.GetPlanet(null)` throws** (dictionary lookup); orders with no planet use empty strings, never null.
- **Rider:** `get_file_problems` needs `rootFolder: "C:/Projects/FlatSpace"` and cannot analyse a brand-new file ("not included
  in any project"); an empty problem list is not proof of a compile.
- **A python probe hung twice in Git Bash** (a heredoc with no script, and a bare `python3 -` run); use awk or the Edit tool.
- **Analysis scripts lived in the session scratchpad and are gone**; the `tuning-log` skill (now with a "Ship combat" bullet)
  describes the analyses. The log fields used: `Combat` is `T|P<attacker>|Combat|planet|a->v|damage|ships`, `Surrender` is
  `...|rival|lossShare|pSurrender|truceUntil`.
- **Baselines for the next tuning run:** `test2.json` with combat: the six 10-05 16:47-16:52 files; `4p.json` with combat: the six
  10-05 17:05-17:23 files; no-combat `test2.json`: the six 10-05 14:29-14:38 files; the 170-byte stub logs are default-scene
  starts, not matches.
- **Check the branch before a Play run:** `git log -1` should show `18aebe8` or later on `main`.
- **Things you said to avoid or want:** recommend one option with every choice; brainstorm one question at a time and get a yes
  before coding; test first; no tuning of the surrender terms until the diplomacy tuning; commit and push only when asked (you
  asked for the specs and plans commits, both merges, both pushes).
