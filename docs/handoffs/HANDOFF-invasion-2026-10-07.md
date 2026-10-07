# Handoff: Invasion (dominance and conversion)

Saved from the Claude Code session named **"invasion"** (session ID `6f9b5d5f-ac60-4e2a-8b04-1ec88d6d12b7`), 2026-10-07 (the work ran
2026-10-06 to 2026-10-07). This file is the written version so nothing depends on that history.

Read `CLAUDE.md` first (architecture, conventions, the self-check pattern; the new "Invasion (conversion)" section is under Ship combat).
The design is in `docs/superpowers/specs/2026-10-06-invasion-conversion-design.md` and the plan in
`docs/superpowers/plans/2026-10-06-invasion-conversion.md`.

## Where things stand

**Asked:** "let's talk about invasion" (element (3) of the `ship_combat` milestone). **Done:** brainstormed (one question at a time, the owner
chose every option), wrote and committed the spec and plan, implemented the plan natively on a branch (9 tasks, test first), had a fresh
whole-branch review (no Critical, 3 Important, 5 Minor), fixed the Important ones, fixed a crash the owner's Play runs found, merged, then
tuned from Play-mode logs: a partial release of held fleets, and the conversion pace. Everything is merged to `main`.

**Verified:** the owner ran `Run Conversion Self-Check` and `Run All AI Self-Checks` in the Editor after every change set and reported all
passing (I could not run Unity from this environment; the one signal I had was `Editor.log`, which showed no `error CS`). More than 70 Play-mode
runs were analysed (see the findings). **Not verified:** nothing is pushed (`main` is 21 commits ahead of `origin/main`); the colony-ship
speed-up (`conversionColonizeWeight`) and the non-war hostility path (`hostilityPerConversion`) fired too rarely to judge.

### What the feature does

A planet changes hands by **dominance and conversion**, not troops. `ConversionSystem` (pure) runs right after combat, in the engine step.

| Area | What it does | Key code |
|---|---|---|
| Dominance | my warships with offense are docked and no warship of a player at war with me is; two non-war dominators: more offense, then the lower id | `ConversionSystem.Dominator` |
| Session | starts only against a holder I am at war with; progress += `1 / max(1, conversionTurnsBase x (1 - my share))`; at 1 one inhabitant flips (at-war holders first, largest, lower id; then the non-war tail) until clean, dominance lost or the war ends | `ConversionSystem.Resolve`, state on `Planet` (`ConversionBy`, `ConversionProgress`, `ConversionWarMask`) |
| Flip | `Planet.ConvertInhabitant`; an `Owner` change clears `CurrentProduction` and `ProductionQueue`, no refund; improvements, stocks, morale pass on | `Planet.cs` |
| Hostility | `hostilityPerConversion` toward the converter per inhabitant converted from a player NOT at war with it; recorded on `DiplomacyState`, charged by the victim's `UpdateDiplomacy` | `DiplomacyState.RecordConversion`, `HostilityCalculator` |
| Hold and release | a converting fleet is not stripped, but ships beyond `max(1, ceil(keepFraction x nearby rival offense / offense per ship))` go to garrison and blockade calls; the assault target and contested holds keep every ship | `AssaultPlanner.ConversionHolds/ConversionKeep`, `ShipTransportPlanner.ConversionKeep` |
| Colonization | a dominated planet where a conversion can run and I hold an inhabitant is a valid target; choice cost divided by `1 + weight x (1 - my share)` | `PlayerAI.IsConversionTarget/IsConversionColonizeTarget/ConversionColonizeDivisor` |
| Audit | `ConversionHoldSpare` reruns the planners without any keep to log the full demand, plus `keep` and `released` | `PlayerAI.AuditConversionHolds`, `ConversionHoldTracker` |
| Saves | `PlanetSave.conversionBy` (id + 1, so an older save loads with none), `conversionProgress`, `conversionWarMask` | `SaveLoadSystem`, `GameAIMap.SetPlanetSimulationStats` |
| Logging | `ConversionStart`, `Convert`, `ConversionEnd`, `OwnerChanged`, `PlayerOutOfPlanets`, `ConversionHoldSpare`, `ConversionColonize`, trailing `conversionTerm` on `Stance`/`Hostility` | `AITuningLogger`, `ConversionTracker`; `tuning-log` skill updated |

### Tunables (`GameAIConstants`, in code and in `Assets/GameAIConstantsProductionTypes.asset`)

| Tunable | Value | Meaning |
|---|---|---|
| `conversionTurnsBase` | 3 | turns for the first flip at 0% mine; the pace |
| `hostilityPerConversion` | 3 | hostility for a non-war victim, per inhabitant |
| `conversionColonizeWeight` | 0.5 | colonization tilt toward a dominated planet (1 off, 0 or below disables) |
| `conversionHoldKeepFraction` | 0.5 | share of the nearby rival's offense a converting fleet keeps; negative keeps every ship (the original hold) |

### Tuning findings (details in the commit messages and `CLAUDE.md`)

- **Pace.** At `conversionTurnsBase` 6 real conquests (under half the planet mine) finished 5% of the time (median session 5 turns, a typical
  conquest needs about 30); 12 nearly stopped conquest; **3** gave real conquests finishing 16 to 20%, about 12 conquests a run (3 at 6) and
  nothing else moved. Clean-ups (most of the planet already mine) finish 82 to 87% at every setting.
- **Release.** At 0.5 about 34 ships a run are released and nothing measurable changed. 0.25 doubled the releases but `4p.json` blockade events
  (41 to 85 a run), food and grotsits shipment cuts (about 36 to 79) and colony failures rose; reverted to 0.5. Part of that rise (colony failures,
  distinct planets blockaded) also appeared at 0.5 with a slow pace, so batch drift on `4p.json` is real and the 0.25 verdict is weaker than
  first presented; the control run (keep -1) was dropped by the owner.
- **Why conversions fail.** About 52 to 56% of sessions end by lost dominance, almost always a rival warship arriving (a fight), not a retreat
  or a fleet leaving; 21 to 25% end with the war.
- **Siege starvation.** Extra colony failures sit on converted planets within 30 turns of a conversion, 87% within 15 turns of a
  `ShipmentCancelled` for that planet: a dominating fleet is an at-war docked fleet, hence a blockade. Left as is (contested planets and the
  blockade mechanic are desired).
- **DCs.** 12 of 13 `DCLost` events were planets conquered by conversion; moving a DC would not have helped (76% of cancelled supply had the
  destination itself blockaded).

## Files and commits

**New:** `Assets/Flatspace/GameAI/ConversionSystem.cs`, `ConversionTracker.cs`, `ConversionHoldTracker.cs`, `Assets/Editor/ConversionSelfCheck.cs`
(each with its `.meta`), the spec and plan above.
**Changed:** `Planet.cs`, `GameAIConstants.cs` and the constants asset, `DiplomacyState.cs`, `HostilityCalculator.cs`, `GameAI.cs`, `GameAIMap.cs`,
`PlayerAI.cs`, `AssaultPlanner.cs`, `ShipTransportPlanner.cs`, `AITuningLogger.cs`, `SaveLoadSystem.cs`, `GameBoard.cs`,
`AllAISelfChecks.cs`, `SimultaneitySelfCheck.cs`, `CLAUDE.md`, `FUTURE_FEATURES.md`, `.claude/skills/tuning-log/SKILL.md`.

Commits (all local, **none pushed**; local branches `invasion` and `conversion-release` are merged and can be deleted):

| Commits | What |
|---|---|
| `a3492df`, `9525454` | spec, plan |
| `fd3b87a` to `5d1c0a0` (8 commits) | the nine plan tasks (tunables and planet state, hostility charge, `ConversionSystem`, run and log, saves, hold, colonization, audit) |
| `1d79b2c` | docs, tuning-log skill, simultaneity case |
| `cfda7b0`, `9fe0f02` | review fixes: colonization narrowed to planets where a conversion can run, tilt from the first stage, script metas committed; doc |
| `9fcb142` | fix: a conquered capitol crashed the research notifications (`GameAIMap.GetPlayerCapitolName`) |
| `ff098b4` | merge `invasion` |
| `2396c23`, `3ff3771` | partial release of held fleets; merge `conversion-release` |
| `34a83d2`, `18cd554` | keep fraction 0.5 to 0.25, then back to 0.5 |
| `5f47dbb` | `conversionTurnsBase` default 6 to 3 |
| `f549783` | re-evaluation list reordered, continue-until-clean closed |

**Uncommitted (from `/feature_close`):** `FUTURE_FEATURES.md`, `completed_features.md` (element (3) moved, the open remainder left in place),
and this handoff file. Memory note written and current: `invasion-reevaluate-after-tuning`.

## Decisions and why (all the owner's, recommendation first each time)

- **Mechanism: dominance conversion**, not troop ships or bombardment, because it reuses existing code and leaves planet defense (element 4)
  the smallest surface to plug into. **Pace:** gradual, one inhabitant at a time, `max(1, N x (1 - p))` with a progress accumulator that resets when
  dominance ends; the owner's snowball idea. **Conversion hold:** a stateless hold so a won fight does not strip the fleet. **Order:** at-war
  holders first, then continue against non-war holders while the fleet stays (hostility only to the non-war victim); a truce stops conversion
  regardless of fleet strength. **Continue until clean** (closed 2026-10-07 as kept). **Keep everything, clear the production item** on an owner change.
  **Minimum force: any one ship** (later data: force size is not the constraint). **Colonization tilt inverted** by the owner (a colonist is worth
  most early): `1 + w x (1 - share)`.
- **Rulings I made:** Editor runs batched for the owner (I cannot run Unity), the final review's colonization narrowing (spec said "dominated";
  I required "a conversion can run": an at-war holder or my own session), `AuditConversionHolds` lost an unused parameter, native execution on a
  branch in the main working directory (the Editor reads that directory), experiments set in the constants asset only and left uncommitted until
  agreed.
- **Rejected or tried and reverted:** troop ships; all-at-once conversion; offense-scaled speed (still open); keep fraction 0.25 (reverted);
  `conversionTurnsBase` 12 (reverted; 6 replaced by 3); the `keep -1` control run (dropped); changing DC placement (not the lever).

## Open items, most important first

1. **A colony ship production boost for dominated planets: first item next session** (owner's order). The `ColonyShip` situational weight is
   unchanged; the speed-up fired 0 to 3 times a run. Start from `PlayerAI.GetIndustrySituationalWeightMultiplier` (see the session rules: express
   it as a situational weight with a tunable on `GameAIConstants`, compute at most once per production turn, and include the tuning-log section).
2. **Offense-scaled conversion speed** (use the `ConversionHoldSpare` audit and the `Convert` pace).
3. **Check `test2.json` colony failures at base 3** (170 a run against 134, ranges overlapped): re-check in the next `test2.json` batch.
4. **A minimum force for dominance:** largely answered, close if no new evidence. **Elimination** of a player that loses its last planet
   (`PlayerOutOfPlanets` never fired in more than 70 post-invasion runs, but a lost capitol happened 3 times). **Untested tunables:** `hostilityPerConversion` and
   `conversionColonizeWeight`.
5. **Deferred review minors** (in `FUTURE_FEATURES.md`): `PlayerOutOfPlanets` has no `<player>` field, a weak precondition in
   `RunConversionOrderIndependenceCheck`, `ConversionStart` logged after loading a session in legacy mode, the war mask fixed at session start,
   a `WarshipStats` built per `IsConversionTarget` call.
6. **Housekeeping:** push `main` when the owner says so (21 local commits); `retreat-v1-fallback` is still a local tag (push or delete);
   `FUTURE_FEATURES.md`, `completed_features.md` and this handoff are uncommitted; carried over from earlier handoffs: planet defense (4), the AI
   blockading on purpose (5), the nine colonist-redirect minors, the sub-project 4 deferrals, the diplomacy and retreat minors.

## Gotchas for a fresh session

- **Unity cannot run from here.** Every "run" step means the owner focuses the Editor and clicks the self-check menu item; compile errors show in
  the Console. `%LOCALAPPDATA%\Unity\Editor\Editor.log` (grep `error CS`, check its modification time) is the only signal available, and it only updates
  when the Editor recompiles.
- **A new default in `GameAIConstants` does not reach the loaded asset.** Every tunable is also written into
  `Assets/GameAIConstantsProductionTypes.asset`; an experiment is set in the asset only (the code default stays so the self-check still passes);
  a value changed live in the Inspector during Play does not persist.
- **Log analysis pitfalls:** grep `|Blockade|` also matches `ShipmentCancelled` lines whose reason is "Blockade", so count by field 3 with awk; a
  `cd` into `AITuningLogs/` persists in the shell (use absolute paths); the analysis scripts lived in `/tmp` and are gone, rebuild them from the
  `tuning-log` skill; `python3 -I` worked this session (earlier handoffs say it hung). `4p.json` colony failures and blockade counts drift 20 to 30%
  between batches, so a one-batch difference is a watch item, not a finding; compare per-run ranges and pool at least 12 runs.
- **Conversion is invisible to a log reader unless you split session types:** sessions that start with under half the planet mine ("real")
  finish 5% (base 6) to 16 to 20% (base 3); clean-ups finish about 85%. Pooled numbers hide that.
- **The owner wants** a recommendation with every choice, one brainstorming question at a time, a yes before code, test first, and commits only
  when asked (this session they asked for the spec, the plan, the merges and each tuning commit). Contested planets, big committed forces
  outranking small blockades and the blockade-driven siege are desired, not defects; do not propose ranking fixes.
- **The ledger** (`.superpowers/sdd/2026-10-06-invasion-conversion/progress.md`) is git-ignored scratch; the git history is the record.
