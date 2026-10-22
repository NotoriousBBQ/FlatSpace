# Handoff: Planet defense

Saved from the Claude Code session named **"planet-defense"** (a name created from the content; the session was unnamed), on 2026-10-22. The work spanned
2026-10-21 to 2026-10-22. This file is the written version so nothing depends on that history.

Read `CLAUDE.md` first, then `Assets/Flatspace/GameAI/CLAUDE.md` ("Planet defense" and the "Invasion (conversion)" dominance paragraph), then
`FUTURE_FEATURES.md`. Spec `docs/superpowers/specs/2026-10-21-planet-defense-design.md`, plan `docs/superpowers/plans/2026-10-21-planet-defense.md`.

## Where things stand

**Asked:** build planetary defense (bunkers, siegeships, a bombard stat, a separate bombardment step, a bunker gate on conversion, bombard-scaled
conversion speed, bunker fire in the fight projection, threat-weighted bunker building, siegeship assault composition, a follower arrival gate, escorted
colonists), then "measure first, then merge". The measure-and-tune loop (you ran the self-checks and Play runs on `4p.json` and `test2.json`, I read the logs
with `/tuning-log`) added three changes. Then you said the feature is done for now and asked to commit, merge and push.

**Done and merged to `main`, pushed (`origin/main` is at `8c3c3c0`).** The branch `planet-defense` still exists locally (not deleted, not pushed).

| Change in the tuning loop | What it does | Key code |
|---|---|---|
| Pace term | the assault also wants `siegePaceFraction` (1) x `conversionBombardReference` (60) of siegeship bombard against an enemy-occupied target, so bunkerless targets get siegeships too | `AssaultPlanner.RequiredBombard`, `GameAIConstants` |
| Conversion carry | a session that ends `DominanceLost` keeps its progress for `conversionCarryTurns` (3); the same dominator back in time resumes it; not saved; logged as `ConversionResumed|planet|progress` | `Planet.ConversionCarry*`, `ConversionSystem.Step`, `AITuningLogger.LogConversionResumed` |
| Ratio dominance | a player's docked fleet is contested only when at-war rival WARSHIPS that hold inhabitants on the planet, summed, exceed `dominanceRivalRatio` (0.5) x its fighting strength (offense x (health + defense)); rival siegeships and landless rivals never contest; a negative ratio is the old any-rival-ship rule | `ConversionSystem.Dominator` |

### What was verified, and how

- **Self-checks:** you ran `Run Planet Defense Self-Check` and `Run All AI Self-Checks`; both passed after the last change (head `45d0ca5`). The first run of
  the new ratio checks had three failures (the test fixtures gave only one side inhabitants); fixed in the fixtures, not the rule.
- **Play runs, 6 per board per batch, read with `/tuning-log`, all to T399.** `OwnerChanged` per run (baseline from 10-18, before this feature: 4p 15-28, test2 13-33):

| Batch | 4p | test2 |
|---|---|---|
| First planet-defense runs | 0-4 | 0-4 |
| Pace term | ~8 (1-12) | ~7 (0-13) |
| + carry | mean 11.7 (2-22) | mean 10.0 (4-29) |
| + carry + ratio (final) | mean 9.5 (6-13) | mean 6.3 (4-8) |

  `bombardFactor` sits at 0.73-0.93. The ratio rule cut `DominanceLost` (5-31 a run) and sessions started, but not conquests; medians match the carry batch.

### What was not verified

- The final batch's `PlanetDead`/`ColonizeArrive` against baselines was only glanced at (`PlanetDead` 5-34 on 4p, 9-30 on test2), and the fleet-cap
  invariant and `FleetHealth` were not re-checked for the last two batches.
- The merged tree was not re-run in the Editor after the merge itself (it equals the tested `45d0ca5` tree plus the earlier committed work).

## Files and commits

- Committed and merged: `45d0ca5` (pace term, carry, ratio dominance, docs, self-checks, tuning-log skill), merge commit `8c3c3c0` on `main`. Before them on the
  branch: 26 commits off `3339949`, head `841e432` (the 16 plan tasks and the final fix wave).
- Changed in the commit: `Assets/Flatspace/GameAI/{ConversionSystem,AssaultPlanner,GameAIConstants,GameAI,PlayerAI,CLAUDE.md}`,
  `Assets/Flatspace/Objects/Planets/Planet.cs`, `Assets/Flatspace/Diagnostics/AITuningLogger.cs`, `Assets/Editor/{PlanetDefenseSelfCheck,WarshipSelfCheck}.cs`,
  `.claude/skills/tuning-log/SKILL.md`.
- **Uncommitted (from the close-out, not committed):** `FUTURE_FEATURES.md` (entry (4) replaced by a "moved to completed_features on 2026-10-22" line, a measured
  note added to the tuning item), `completed_features.md` (a "(4) Planet defense done" entry under the ship_combat milestone), and this handoff.
- Never staged (noise): `Assets/Game/Input/MapInputActions.cs` (Unity-regenerated), `.idea/.idea.FlatSpace/.idea/indexLayout.xml`.

## Decisions made and why

- **Measure first, then merge** (yours). Merged only after you called the feature done.
- **Pace term first, then loosen the follower gate (`followerMaxLossFraction` 0.5 to 0.75): no effect, so reverted** at your request.
- **Carry with a 3-turn window** after the investigation showed ~90% of 342 `DominanceLost` events were same-turn combat and a lone surviving rival ship reset progress.
- **Dominance ratio, your two-pronged idea:** rival siegeships alone never break dominance, and rival warships are weighted by strength. Multiple rivals are
  summed, and only rivals with inhabitants on the planet count. The consequence you accepted: an invader with no inhabitants never contests the defender
  (only the defender's ships contest the invader; higher bombard decides when both are uncontested).
- **A fleet keeps converting while a rival's ships fight it**: intended ("high orbit/low orbit paradigm"), not a defect.
- **Accepted the lower conquest rate** (about half to two thirds of the pre-defense baseline) and closed the feature without tuning the follower gate or escorts.
- **Close-out judgment call:** I moved only sub feature (4) to `completed_features.md` (it carried no "done" label; I treated it as done), and left the
  ship_combat milestone in "Partially done" rather than marking it fully done, because its "Still open from ..." notes (surrender terms, colonist deferred minors, etc.) are
  unfinished work that moving the milestone would have deleted.

## Open items and next steps

None are asked for; you said you do not care about these. In order of value if the conquest rate matters later:
1. **Follower gate:** `SiegeHeld` outnumbers `SiegeFollow` about 4 to 1, about half `Wiped` (the projection says the group would be destroyed).
2. **Escorts:** `EscortLaunch` 0-14 per run, mostly `NoSpare`.
3. **Asymmetric contest** from the population rule (a landless invader never contests); a symmetric variant is possible if logs show defenders holding planets against much larger fleets.
4. Still listed in `FUTURE_FEATURES.md` under (4): bunkers invisible in the fleet list and planet UI, retreat moving warships only, the other behaviour gaps and the deferred review minors.
5. Branch `planet-defense` can be deleted when you like (merged, local only).
6. The memory file `planet-defense-awaiting-play-measurement.md` is now stale (it says not merged); update or delete it.

## Gotchas

- Unity cannot run from the command line: every check is you running the `[MenuItem]` self-checks and Play runs; a self-check must never use `Gameboard.Instance`.
- `WarshipSelfCheck.MakeConstants` sets `dominanceRivalRatio = -1` and `conversionCarryTurns = 0` on purpose so older checks keep their old meaning; new ratio and carry
  checks set their own values.
- Bare `ISet<>` is ambiguous in `PlayerAI.cs` and `GameAI.cs` (write `System.Collections.Generic.ISet<T>`); subtype strings are case-exact ("Warship", "Siegeship", "Bunker").
- A big bash heredoc of Python failed once ("unexpected EOF"): write patch scripts to a file and run them. Several sources use CRLF.
- The conquest numbers are only comparable with the 10-18 baseline directionally (pace is bombard-scaled now and a bunker stops conversion entirely); per-run variance is large.
