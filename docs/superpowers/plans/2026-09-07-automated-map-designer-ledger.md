# Automated map designer — execution ledger

Companion to
[2026-09-07-automated-map-designer.md](2026-09-07-automated-map-designer.md) (the plan) and
[2026-09-07-automated-map-designer-design.md](../specs/2026-09-07-automated-map-designer-design.md)
(the design spec). Records the rulings made while executing the plan's 9 tasks and the manual
Unity-Editor steps performed.

## Outcome

All 9 plan tasks implemented and individually reviewed (spec + quality). One fix round (Task 4).
Every task's per-file Rider compile check was clean. The user ran the full manual verification pass
and reported "all checks OK":

- `Map Gen: Self Check (50 seeds)` — 50/50 passed.
- `Generate Random Board` — planet/Prime counts correct, no overlaps, sparse connections, every
  planet connected, no Prime–Prime edge, no same-type non-Normal adjacency.
- Infeasible-weights case (37 Farm planets) — fails after the retry budget with the "loosen the
  frequency weights" diagnostic, scene untouched.
- Generate → Save → load into `Flatspace` → ~20 turns — map loads, lines match, sim runs with a
  clean Console.
- Regression: a pre-existing hand-built board still plays exactly as before (distance-rule
  fallback path in `PathingSystem`).

## Verification approach

No automated test framework exists in this repo (per `CLAUDE.md`). Verification was:

- A Rider `mcp__rider__get_file_problems` (errorsOnly) check on every modified/created file after
  each task. New `.cs` files (`PlanetTypeDefaults.cs`, `MapGenSettings.cs`, `MapGenerator.cs`)
  could not be Rider-checked until the user did a Unity asset refresh to regenerate the
  `.csproj`; those were verified by controller + reviewer diff reads and confirmed compiling by
  the user's post-refresh Console.
- The incremental `MapGenerator` build (Tasks 4–7) used a temporary `BoardDesigner` context-menu
  (`Map Gen: Dry Run`) as the stand-in test surface; Task 8 added the real `Generate Random
  Board` and the 50-seed `Map Gen: Self Check`.
- The user's manual pass above.

## Rulings

1. **Execution on a branch, not a worktree.** This is a Unity project; a worktree needs its own
   multi-GB `Library` re-import, and the plan's manual verification steps must run against the
   real project open in the Editor. Executed on `automated-map-designer-design`.

2. **New-file tasks get diff-review + a batched user compile check** rather than a per-task Rider
   check, because Unity has not generated a brand-new script's `.csproj` entry until an Editor
   refresh. Applied to Tasks 1 and 4.

3. **Tasks 4–6 legitimately leave `MapGenerator.TryGenerate` returning `Fail("... not implemented
   yet")` and Task 4 leaves an unused `rng` local.** These are the plan's explicit incremental
   scaffolding, not incompleteness defects. Reviewers were told so; none blocked on it.

4. **Task 1's `GenerateNames` refactor reorders the per-type `Debug.Log` count lines** (now enum
   order). Cosmetic editor-console output, no caller — accepted per the plan.

5. **Task 1's missing `.cs.meta` folded into Task 4's fix.** `PlanetTypeDefaults.cs.meta` was
   never committed (didn't exist at Task 1 commit time); Task 4's fix round committed all three
   new-script metas (`5ff7f13`). This repo's script metas are the minimal 2-line
   `fileFormatVersion` + `guid` form — the new ones match.

6. **Task 4 fix round 1 (`.meta` files).** The only fix-loop finding in the run. Resolved by
   resuming the implementer; re-review confirmed ADDRESSED, no new breakage.

## Manual Editor steps performed (by the user)

1. Unity asset refresh after Task 1 and after Task 4 to regenerate project files for the new
   scripts.
2. Created `Assets/Flatspace/BoardDesigner/MapGenSettings_Default.asset` (seed 0, 3 players, 40
   planets, weights across all 7 non-Prime types) and `MapGenSettings_Fail.asset` (37 Farm
   planets — the infeasible fixture). Wired `MapGenSettings_Default` onto the `BoardDesigner`
   component in `MapDesigner.unity`.
3. Assigned `PlanetDesigner.prefab` to `BoardDesigner.planetDesignerPrefab`.
4. Ran `Map Gen: Self Check`, `Generate Random Board` (saved the result into `MapDesigner.unity`,
   replacing the original hand-authored board — kept deliberately), and the Generate → Save →
   Play pass. Saved boards kept as `Assets/Flatspace/BoardConfigs/test.json` and `test2.json`.

## Working-tree changes found at Task 9 that were NOT part of this feature

- **`Assets/Flatspace/Objects/Planets/Planet.cs`** — `CheckColonizationReady` had been
  uncommented and its stale refs fixed (`_gameAIConstants` → `GameAIConstants`). No task in this
  plan touched that method. Surfaced to the user, who chose to keep it; committed **separately**
  as `ac4bc0e` with a message flagging it as unrelated and unreviewed. It is not part of the
  automated-map-designer feature and the final whole-branch review was scoped to exclude it.
- `Assets/Plugins/SimpleFileBrowser/Sprites/SimpleFileBrowserSpriteAtlas.spriteatlas` — Unity
  re-serialization noise, left uncommitted.

## Deferred minor review findings (for the final whole-branch review to triage)

- `PlanetTypeDefaults.cs` — unused `using UnityEngine;`; `static readonly` array `AllTypes` is
  shallow-mutable by callers.
- `PathingSystem.BuildExplicitConnections` — no self-loop guard (`if (from == to) return;`);
  an unknown connection name logs a warning twice (once per direction).
- `BoardDesigner` — planar-distance computation duplicated in three places; `RebuildAllConnectionCaches`
  keys planets by name with silent last-write-wins on a duplicate name; `SetConnectionNames` uses
  `List.Contains` in a loop; `MapGenSelfCheck`'s cloned settings SO is not freed in a `finally`.
- `MapGenerator` — `Validate`'s same-type-edge and `degree == 0` branches are dead given correct
  Tasks 5–6 (intended defense-in-depth); `AssignTypes` uses forward-consistency only (can burn
  extra backtracking steps on tight multisets); no recursion-depth guard for a pathological
  `totalPlanetCount` in the thousands; `PickPrimeIndices` would loop forever if
  `primeCount > positions.Count` (unreachable — `ValidateSettings` guards it).
