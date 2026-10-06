# Retreat and Engaged-Force Loss Share Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** A player whose warships are losing a fight at a planet can pull them out (a projected-to-the-end fight, a deterministic gate, a weighted Stay-or-Retreat `ScoreMatrix` decision, a tiered blockade-aware destination), and the loss share that drives the hostility loss drop and surrender is measured against the force engaged at the fight planets.

**Architecture:** The existing `UpdateResult` -> `ScoreMatrix` -> `GameAIOrder` flow. A pure `FightProjector` appends one `FightProjection` result per (planet, player) after combat each turn. `RetreatPlanner` (called first in `PlayerAI.PlanShipActions`) reads this player's projections, applies the gate, finds the best tiered destination and lets a new `RetreatMatrix` (stance-matrix pattern, `IndependentRows`, roulette) weigh Stay against Retreat; a retreat is the existing ship-transport order trio (no new `OrderType`). `CombatSystem`'s per-planet core is extracted into a pure `Round` that both real combat and the projection call. The loss share becomes window lost / window engaged strength (`CombatLoss.Engaged`).

**Tech Stack:** Unity 6000.4.1f1 C#, no test framework: the self-check pattern (`Assets/Editor/*SelfCheck.cs`, `[MenuItem]` under `FlatSpace/AI`), Rider MCP `get_file_problems`, the Unity Editor for compile and run.

**Spec:** `docs/superpowers/specs/2026-10-06-retreat-and-engaged-loss-share-design.md` (read it first; this plan implements it task by task).

## Global Constraints

- Namespace: new AI files use `namespace FlatSpace.AI` (the file style of `StanceMatrix.cs` is `namespace FlatSpace { namespace AI { ... } }`; `RoutePlanner.cs`/`BlockadeView.cs` use `namespace FlatSpace.AI`; either is fine, match the nearest sibling file named in the task).
- `Planet.UpdateResult` (after Task 1) is the result type everywhere; never write `PlanetUpdateResult` again.
- New `OrderType` and `UpdateResultType` values are appended last; `Stance` is never extended; **no new `OrderType` is added by this work**.
- Pure classes (`CombatSystem.Round`, `FightProjector`, `RetreatMatrix`, `RetreatPlanner`, `RetreatTracker`) must not touch `Gameboard.Instance` (the self-checks drive them with no Gameboard).
- A method a self-check calls is `public`, not `internal` (`Assets/Editor` is a separate assembly).
- A new script's `.meta` file is committed with it (`git add` the `.cs` and `.cs.meta` together).
- `GameAIMap.GetPlanet(null)` throws; orders with no planet use empty strings, never null.
- Do not put a self-check exactly on a float threshold boundary (`retreatCheckFraction`, `retreatLossFraction`, the blockade margin).
- `GameAI.Rand` is the shared random stream; the Stay/Retreat roll draws from it, so every self-check that needs a deterministic roll uses a very steep `retreatSteepness` (0.001) and a loss fraction far from `retreatLossFraction`, and re-seeds `GameAI.Rand` per player where players are compared.
- A new default in `GameAIConstants` does not reach the already-loaded asset: every new key is also written into `Assets/GameAIConstantsProductionTypes.asset` (Task 2).
- Commits end with `Co-Authored-By: Claude Sonnet 5.5 <noreply@anthropic.com>` and are made only at the commit steps below (the repo rule is "commit only when asked"; the user asked for this plan to be executed, each task's commit step is part of it). Do not push.
- **How to check a change compiles and passes (used by every task, called "the check"):** (1) Rider MCP `get_file_problems` with `rootFolder: "C:/Projects/FlatSpace"` on each changed file (it cannot analyse brand-new files and an empty list is not proof of a compile); (2) ask the user to focus the Unity Editor so it recompiles, then run `FlatSpace -> AI -> Run Retreat Self-Check` (from Task 2 on) and `FlatSpace -> AI -> Run All AI Self-Checks`, and read the Console. Unity cannot be run from Claude Code. Code that only runs in Play mode needs the user to exit and re-enter Play mode.
- Floats in tests: use `Near(a, b)` (tolerance 0.01) and inequalities; the fixture template is Offense 10, Health 100, Defense 5, `combatDamageK` 20, so a hit does 0.8 of its offense and one full-health ship has strength 10 x (100 + 5) = 1050.

## Review Focus

Inputs and conditions the spec implies but the main tests could miss, most likely first (each has a test in the task that owns the code):
1. A warship at exactly 0 health in a group (the existing deferred minor): it must not fight, count, or crash the projection (Task 5, `RunProjectionCheck`).
2. Two fight planets retreating to the same destination in one turn: both go, neither removes the other (`IndependentRows`) (Task 7, `RunRetreatMatrixCheck`).
3. A third player's ships at the fight planet who is at war with neither side: they are not counted as my rival and do not change who survives (Task 5).
4. Legacy mode (`diplomacyEnabled` false): no projection results and no retreat (Task 5 wiring check, Task 9).
5. An own in-flight fleet whose arrival is beyond the projection cap, or heading elsewhere: ignored (Task 5).

## Tuning Log Output (session rule)

| Line | Added in | Kept from repeating by |
|---|---|---|
| `Retreat\|<planet>\|<destination>\|<tier>\|<ships>\|<projectedLossPct>\|<rivalSurvivorsPct>\|<routeCost>\|<cooldownUntil>\|<pRetreat>` (+ `\|<rememberedBlockade>\|<myOffense>` for tier 3) | Task 10 | once per retreat order; a retreat sends the ships |
| `RetreatStay\|<planet>\|<projectedLossPct>\|<pRetreat>` | Task 10 | `RetreatTracker` (log-only, on change) |
| `RetreatHeld\|<planet>\|<NoDestination or OwnPlanetNotWiped>\|<projectedLossPct>` | Task 10 | `RetreatTracker` (log-only, on change) |
| `RetreatArrive\|<planet>\|<rememberedBlockade>\|<actualBlockade>` | Task 10 | a log-only pending list in `PlayerAI`, one entry per tier 3 retreat |
| `Hostility` line gains a final `\|<engagedStrength>` | Task 4 | already every 25 turns |

The `tuning-log` skill and the "AI Tuning Log" / "Ship combat" sections of `CLAUDE.md` are updated in Task 11.

---

## File Structure

| File | Action | Responsibility |
|---|---|---|
| `Assets/Flatspace/Objects/Planets/Planet.cs` | modify | rename (Task 1); append `UpdateResultTypeFightProjection` (Task 5) |
| every `.cs` containing `PlanetUpdateResult` (16 files) | modify | the rename (Task 1) |
| `Assets/Flatspace/GameAI/GameAIConstants.cs`, `Assets/GameAIConstantsProductionTypes.asset` | modify | five retreat tunables (Task 2) |
| `Assets/Editor/RetreatSelfCheck.cs` (+ `.meta`) | create | the new suite (Task 2, grows each task) |
| `Assets/Editor/AllAISelfChecks.cs` | modify | register the suite (Task 2) |
| `Assets/Flatspace/GameAI/CombatSystem.cs` | modify | extract `CombatUnit` / `Round` (Task 3); `Engaged` and a result every fight turn (Task 4) |
| `Assets/Flatspace/GameAI/PlayerAI.cs` | modify | engaged loss share (Task 4); retreat integration (Task 9); logging (Task 10) |
| `Assets/Flatspace/GameAI/DiplomacyState.cs` | modify | `Pair.Engaged` (Task 4) |
| `Assets/Flatspace/GameAI/FightProjector.cs` (+ `.meta`) | create | `FightProjection` data and the pure projection (Task 5) |
| `Assets/Flatspace/GameAI/GameAI.cs` | modify | `RunFightProjections` after `RunCombat` (Task 5); `Pair.Engaged` in the Hostility log (Task 4) |
| `Assets/Flatspace/GameAI/BlockadeView.cs` | modify | `OnlyFrom`, `Without`, `WithBlockers` (Task 6) |
| `Assets/Flatspace/GameAI/RetreatMatrix.cs` (+ `.meta`) | create | the Stay/Retreat matrix (Task 7) |
| `Assets/Flatspace/GameAI/RetreatPlanner.cs` (+ `.meta`) | create | gate, tiered destination, plan (Task 8) |
| `Assets/Flatspace/GameAI/ShipTransportPlanner.cs`, `AssaultPlanner.cs` | modify | `Retreating`, `ExcludedTargets` (Task 9) |
| `Assets/Flatspace/GameAI/RetreatTracker.cs` (+ `.meta`) | create | log-only on-change tracker (Task 10) |
| `Assets/Flatspace/Diagnostics/AITuningLogger.cs` | modify | the new log lines (Tasks 4, 10) |
| `Assets/Editor/CombatSelfCheck.cs`, `DiplomacySelfCheck.cs`, `SimultaneitySelfCheck.cs` | modify | updated for the new measure and results (Task 4), simultaneity (Task 11) |
| `CLAUDE.md`, `.claude/skills/tuning-log/SKILL.md`, `FUTURE_FEATURES.md` | modify | docs (Task 11) |

---

### Task 1: Rename `PlanetUpdateResult` to `UpdateResult`

A mechanical, behavior-free commit. `PlanetUpdateResult` is a nested struct of `Planet`; the stem is also in its nested enums (`PlanetUpdateResultType`, `PlanetUpdateResultPriority`) and their values, so one substring replace renames all consistently. Verified beforehand: no identifier named `UpdateResult` exists in `Assets/**/*.cs`; nothing serializes these types (results are transient; no `.unity`/`.asset` mentions).

**Files:**
- Modify: every file under `Assets` that contains `PlanetUpdateResult` (`Planet.cs` 81, `PlayerAIResourceSelfCheck.cs` 52, `PlayerAI.cs` 33, `BlockadeAvoidanceSelfCheck.cs` 16, `DiplomacySelfCheck.cs` 13, `DistributionCenterSelfCheck.cs` 10, `PlayerKnowledgeSelfCheck.cs` 9, `CombatSystem.cs` 7, `SimultaneitySelfCheck.cs` 7, `AITuningLogger.cs` 6, `CombatSelfCheck.cs` 6, `GameAI.cs` 4, `BlockadeBreakSelfCheck.cs` 3, `GameAIMap.cs` 2, `ResourceMatrix.cs` 2, `Player.cs` 1)
- Modify: `CLAUDE.md` (the one place that names the type, if any; do not touch historical specs, plans or handoffs)

**Interfaces:**
- Produces: `Planet.UpdateResult` (struct), `Planet.UpdateResult.UpdateResultType` (enum, values `UpdateResultTypeX`), `Planet.UpdateResult.UpdateResultPriority` (enum, values `UpdateResultPriorityX`). The `using ResultType = Planet.UpdateResult.UpdateResultType;` and `using ResultPriority = ...` aliases in `Planet.cs` keep their names.

- [ ] **Step 1: Confirm the starting count and that nothing collides**

Run (Git Bash, from `C:/Projects/FlatSpace`):
```bash
grep -rc "PlanetUpdateResult" Assets --include=*.cs | grep -v ":0"
grep -rn "\bUpdateResult\b" Assets --include=*.cs
```
Expected: the 16 files above with their counts (252 total); the second command prints nothing.

- [ ] **Step 2: Replace**

```bash
grep -rl "PlanetUpdateResult" Assets --include=*.cs | xargs sed -i 's/PlanetUpdateResult/UpdateResult/g'
sed -i 's/PlanetUpdateResult/UpdateResult/g' CLAUDE.md
```
(`sed -i` preserves the files' line endings.)

- [ ] **Step 3: Verify nothing is left and the shape is right**

```bash
grep -rn "PlanetUpdateResult" Assets CLAUDE.md
grep -n "struct UpdateResult\|enum UpdateResultType\|enum UpdateResultPriority\|using ResultType\|using ResultPriority" Assets/Flatspace/Objects/Planets/Planet.cs
git diff --stat | tail -3
```
Expected: the first prints nothing; the second shows `public struct UpdateResult`, `public enum UpdateResultType`, `public enum UpdateResultPriority` and the two `using` aliases now naming `Planet.UpdateResult.UpdateResultType` / `...UpdateResultPriority`; the diff stat shows 16 `.cs` files plus `CLAUDE.md` changed and nothing else.

- [ ] **Step 4: The check**

Run the check (Global Constraints). Expected: no new problems in `Planet.cs` and `PlayerAI.cs`; after the Editor recompiles, the Console has no errors and `Run All AI Self-Checks` reports all 13 suites passed (it did before this change).

- [ ] **Step 5: Commit**

```bash
git add -A Assets CLAUDE.md
git commit -m "refactor: rename PlanetUpdateResult to UpdateResult (struct, enums and values; no behavior change)

Co-Authored-By: Claude Sonnet 5.5 <noreply@anthropic.com>"
```

---

### Task 2: Tunables and the `RetreatSelfCheck` shell

**Files:**
- Modify: `Assets/Flatspace/GameAI/GameAIConstants.cs` (after `surrenderSteepness`, line 163)
- Modify: `Assets/GameAIConstantsProductionTypes.asset` (after `surrenderSteepness: 0.015`, line 70)
- Create: `Assets/Editor/RetreatSelfCheck.cs`
- Modify: `Assets/Editor/AllAISelfChecks.cs` (the `suites` list)

**Interfaces:**
- Produces: `GameAIConstants.retreatCheckFraction` (float, 0.3), `retreatLossFraction` (float, 0.5), `retreatSteepness` (float, 0.1), `retreatProjectionTurns` (int, 20), `retreatCooldownTurns` (int, 10); `RetreatSelfCheck.RunChecks()` (public static bool) and its private helpers `Check(bool, string)` / `Near(float, float)`.

- [ ] **Step 1: Write the failing check**

Create `Assets/Editor/RetreatSelfCheck.cs`:
```csharp
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using FlatSpace.AI;
using Flatspace.Objects.Production;

// Retreat: the fight projection, the Stay/Retreat matrix, the tiered destination, the planner wiring and the engaged loss share.
public static class RetreatSelfCheck
{
    [MenuItem("FlatSpace/AI/Run Retreat Self-Check")]
    public static void Run() => RunChecks();

    public static bool RunChecks()
    {
        var ok = RunTunableDefaultsCheck();
        Debug.Log(ok
            ? "[RetreatSelfCheck] ALL PASSED"
            : "[RetreatSelfCheck] FAILURES (see errors above)");
        return ok;
    }

    private static bool Check(bool condition, string label)
    {
        if (!condition) Debug.LogError($"[RetreatSelfCheck] FAIL: {label}");
        return condition;
    }

    private static bool Near(float a, float b) => Mathf.Abs(a - b) < 0.01f;

    // The five retreat tunables keep their documented in-code defaults.
    public static bool RunTunableDefaultsCheck()
    {
        var ok = true;
        var c = ScriptableObject.CreateInstance<GameAIConstants>();
        try
        {
            ok &= Check(Near(c.retreatCheckFraction, 0.3f) && Near(c.retreatLossFraction, 0.5f), "retreatCheckFraction 0.3, retreatLossFraction 0.5");
            ok &= Check(Near(c.retreatSteepness, 0.1f), "retreatSteepness 0.1");
            ok &= Check(c.retreatProjectionTurns == 20 && c.retreatCooldownTurns == 10, "retreatProjectionTurns 20, retreatCooldownTurns 10");
        }
        finally { Object.DestroyImmediate(c); }
        return ok;
    }
}
```

- [ ] **Step 2: Register the suite**

In `Assets/Editor/AllAISelfChecks.cs` add after the `("Combat", ...)` line:
```csharp
            ("Retreat", RetreatSelfCheck.RunChecks),
```

- [ ] **Step 3: The check fails**

Run the check. Expected: a compile error (`retreatCheckFraction` does not exist). This is the RED.

- [ ] **Step 4: Add the tunables**

In `GameAIConstants.cs`, after the `surrenderSteepness` line (and before `[Header("Distribution Centers")]`) insert:
```csharp

    [Header("Retreat")]
    // A fight planet is considered for retreat only when the projected fight (FightProjector) leaves a war rival alive and
    // my group would lose at least retreatCheckFraction of its strength (at a planet I populate: only a wipe-out). Past that
    // gate Stay or Retreat is a weighted roulette: the Retreat weight is 1 / (1 + e^(-(loss - retreatLossFraction) /
    // retreatSteepness)). A retreated-from planet is off the assault target list for retreatCooldownTurns.
    public float retreatCheckFraction = 0.3f;
    public float retreatLossFraction = 0.5f;
    public float retreatSteepness = 0.1f;
    public int retreatProjectionTurns = 20;
    public int retreatCooldownTurns = 10;
```
In `Assets/GameAIConstantsProductionTypes.asset`, directly after the line `  surrenderSteepness: 0.015` insert (two-space indent, like its neighbours):
```
  retreatCheckFraction: 0.3
  retreatLossFraction: 0.5
  retreatSteepness: 0.1
  retreatProjectionTurns: 20
  retreatCooldownTurns: 10
```

- [ ] **Step 5: The check passes**

Run the check. Expected: `[RetreatSelfCheck] ALL PASSED`, `Run All AI Self-Checks` 14 suites passed.

- [ ] **Step 6: Commit**

```bash
git add Assets/Flatspace/GameAI/GameAIConstants.cs Assets/GameAIConstantsProductionTypes.asset Assets/Editor/RetreatSelfCheck.cs Assets/Editor/RetreatSelfCheck.cs.meta Assets/Editor/AllAISelfChecks.cs
git commit -m "feat: retreat tunables and the RetreatSelfCheck suite shell

Co-Authored-By: Claude Sonnet 5.5 <noreply@anthropic.com>"
```

---

### Task 3: Extract the combat core into a pure `CombatSystem.Round`

A refactor with no behavior change: the existing combat checks must pass untouched. `Resolve` keeps its public signature. (If the extraction turns out not to be clean, stop and report to the owner; the spec forbids a parallel combat formula.)

**Files:**
- Modify: `Assets/Flatspace/GameAI/CombatSystem.cs` (replace the private `Unit` struct and `FightAt`, lines 41-157; the file's `CombatLoss`, `ColonyLoss`, `CombatReport`, `Repair` and `DestroyStrandedColonyShips` stay)
- Modify: `Assets/Editor/CombatSelfCheck.cs` (add `RunRoundCheck`; make `Fixture` public)
- Modify: `Assets/Editor/RetreatSelfCheck.cs` is **not** touched here

**Interfaces:**
- Produces (all in `FlatSpace.AI`, in `CombatSystem.cs`):
  - `public struct CombatUnit { int Id; int Owner; int Index; float Health; float Max; float Defense; float BaseOffense; float Offense; float Strength; static CombatUnit Make(int id, int owner, int index, float baseOffense, float max, float defense, float damage); CombatUnit WithDamage(float more); }`
  - `public struct RoundLoss { int Victim; int Attacker; float DamageDealt; float Ships; float StrengthLost; float Engaged; }`
  - `public class RoundOutcome { Dictionary<int,float> Damage; List<int> Destroyed; List<RoundLoss> Losses; }`
  - `public static RoundOutcome CombatSystem.Round(List<CombatUnit> units, Func<int,int,bool> atWar, Func<int,int,float> hostility, float k)`
  - `CombatSelfCheck.Fixture` becomes `public sealed class Fixture` (Task 5 and later use it from `RetreatSelfCheck`).

- [ ] **Step 1: Write the failing check**

In `Assets/Editor/CombatSelfCheck.cs` change `private sealed class Fixture : System.IDisposable` to `public sealed class Fixture : System.IDisposable`. Add to `RunChecks` after `ok &= RunRepairAndColonyShipCheck();`:
```csharp
        ok &= RunRoundCheck();
```
and add the method before `RunTunableDefaultsCheck`:
```csharp
    // The pure core: units in, damage / destroyed / per-attacker losses out, nothing mutated.
    public static bool RunRoundCheck()
    {
        var ok = true;
        CombatUnit Unit(int id, int owner) => CombatUnit.Make(id, owner, id, 10f, 100f, 5f, 0f);
        System.Func<int, int, bool> war = (a, b) => a != b;
        System.Func<int, int, float> hostility = (a, b) => 1f;

        var outcome = CombatSystem.Round(new List<CombatUnit> { Unit(0, 0), Unit(1, 1) }, war, hostility, 20f);
        ok &= Check(outcome.Destroyed.Count == 0 && Near(outcome.Damage[0], 8f) && Near(outcome.Damage[1], 8f),
            "1 against 1, offense 10 vs Defense 5, K 20: each takes 10 x 0.8 = 8, nobody dies");
        ok &= Check(outcome.Losses.Count == 2 && outcome.Losses.All(l => Near(l.Engaged, 1050f) && l.Ships == 0f && l.StrengthLost == 0f),
            "one loss entry per directed pair; the victim's engaged strength is 10 x (100 + 5) = 1050; nothing died");

        var weak = CombatUnit.Make(1, 1, 1, 10f, 100f, 5f, 90f);   // health 10: dies to 12.5 pool
        var lethal = CombatSystem.Round(new List<CombatUnit> { Unit(0, 0), weak }, war, hostility, 20f);
        ok &= Check(lethal.Destroyed.Count == 0 && Near(lethal.Damage[1], 8f), "a 10 pool needs 12.5 to kill a ship with 10 health: it survives with 8 more damage");
        var bigPool = new List<CombatUnit> { Unit(0, 0), CombatUnit.Make(2, 0, 2, 10f, 100f, 5f, 0f), CombatUnit.Make(3, 0, 3, 10f, 100f, 5f, 0f), weak };
        var kill = CombatSystem.Round(bigPool, war, hostility, 20f);
        ok &= Check(kill.Destroyed.SequenceEqual(new[] { 1 }) && kill.Losses.Any(l => l.Victim == 1 && Near(l.Ships, 1f) && Near(l.StrengthLost, weak.Strength)),
            "a 30 pool kills the 10-health ship (12.5 needed): destroyed, one ship and its strength attributed to the attacker");

        ok &= Check(CombatSystem.Round(new List<CombatUnit> { Unit(0, 0), Unit(1, 0) }, war, hostility, 20f).Losses.Count == 0,
            "one player alone: no fight, an empty outcome");
        return ok;
    }
```
(`weak` has 10 health, offense 10 x 10 / 100 = 1 and strength 1 x (10 + 5) = 15.)

- [ ] **Step 2: The check fails**

Run the check. Expected: compile errors (`CombatUnit`, `CombatSystem.Round` do not exist). RED.

- [ ] **Step 3: Implement**

In `CombatSystem.cs` replace the private `Unit` struct (lines 41-51) with, placed just inside `namespace AI` above `public static class CombatSystem`, the three public types:
```csharp
        /// <summary>One warship's state in a fight, pure values so a projection can copy and advance it.</summary>
        public struct CombatUnit
        {
            public int Id;            // the caller's handle (the dock index at the planet, or a projection's own counter)
            public int Owner;
            public int Index;         // dock order, the focus-fire tie-break
            public float Health;      // current health (Health stat minus damage, never below 0)
            public float Max;         // the Health stat
            public float Defense;
            public float BaseOffense; // the Offense stat before damage
            public float Offense;     // effective: BaseOffense x Health / Max, 0 for a Max of 0 (the WarshipStats formula)
            public float Strength;    // Offense x (Health + Defense)

            public static CombatUnit Make(int id, int owner, int index, float baseOffense, float max, float defense, float damage)
            {
                var health = Math.Max(0f, max - damage);
                var offense = max <= 0f ? 0f : baseOffense * health / max;
                return new CombatUnit
                {
                    Id = id, Owner = owner, Index = index, Health = health, Max = max, Defense = defense,
                    BaseOffense = baseOffense, Offense = offense, Strength = offense * (health + defense),
                };
            }

            /// <summary>The same unit after taking `more` further damage.</summary>
            public CombatUnit WithDamage(float more)
                => Make(Id, Owner, Index, BaseOffense, Max, Defense, (Max - Health) + more);
        }

        /// <summary>What one attacker did to one victim in a round. Ships and StrengthLost are the attacker's share of what the victim lost; Engaged is the same share of the victim's strength at the start of the round.</summary>
        public struct RoundLoss
        {
            public int Victim;
            public int Attacker;
            public float DamageDealt;
            public float Ships;
            public float StrengthLost;
            public float Engaged;
        }

        public class RoundOutcome
        {
            public Dictionary<int, float> Damage = new Dictionary<int, float>();   // unit Id -> damage taken this round
            public List<int> Destroyed = new List<int>();                          // unit Ids
            public List<RoundLoss> Losses = new List<RoundLoss>();                 // victim ascending, then attacker ascending
        }
```
Replace `FightAt` (and keep `Resolve`, `Repair`, `DestroyStrandedColonyShips` as they are) with:
```csharp
            /// <summary>
            /// One round of a fight, pure: hostility-weighted pools per attacker, ONE pool per victim, focus fire on the lowest
            /// health fraction (then dock index), a hit scaled by K / (K + Defense), simultaneous. Nothing is mutated; the caller
            /// applies the outcome. Units with no health are ignored by the caller (Make gives them 0 offense and 0 strength).
            /// </summary>
            public static RoundOutcome Round(List<CombatUnit> units, Func<int, int, bool> atWar, Func<int, int, float> hostility, float k)
            {
                var outcome = new RoundOutcome();
                var players = units.Select(u => u.Owner).Distinct().OrderBy(p => p).ToList();
                if (players.Count < 2) return outcome;

                // pools[victim][attacker]: the offense an attacker aims at a victim (hostility-weighted over its war rivals).
                var pools = new Dictionary<int, Dictionary<int, float>>();
                foreach (var attacker in players)
                {
                    var pool = units.Where(u => u.Owner == attacker).Sum(u => u.Offense);
                    if (pool <= 0f) continue;
                    var targets = players.Where(v => v != attacker && atWar(attacker, v)).ToList();
                    if (targets.Count == 0) continue;
                    var weights = targets.ToDictionary(v => v, v => Math.Max(1f, hostility(attacker, v)));
                    var total = weights.Values.Sum();
                    foreach (var victim in targets)
                    {
                        if (!pools.ContainsKey(victim)) pools[victim] = new Dictionary<int, float>();
                        pools[victim][attacker] = pool * weights[victim] / total;
                    }
                }

                foreach (var victim in pools.Keys.OrderBy(v => v))
                {
                    var contributions = pools[victim];
                    var pool = contributions.Values.Sum();    // one pool per victim: the order of the attackers cannot matter
                    var engaged = units.Where(u => u.Owner == victim).Sum(u => u.Strength);
                    var lostShips = 0;
                    var lostStrength = 0f;
                    var dealt = 0f;
                    var ordered = units.Where(u => u.Owner == victim).OrderBy(u => u.Health / u.Max).ThenBy(u => u.Index);
                    foreach (var target in ordered)
                    {
                        if (pool <= 0f) break;
                        var factor = k / (k + target.Defense);
                        var needed = target.Health / factor;
                        if (pool >= needed)
                        {
                            outcome.Destroyed.Add(target.Id);
                            lostShips++;
                            lostStrength += target.Strength;
                            dealt += target.Health;
                            pool -= needed;
                        }
                        else
                        {
                            outcome.Damage[target.Id] = (outcome.Damage.TryGetValue(target.Id, out var d) ? d : 0f) + pool * factor;
                            dealt += pool * factor;
                            pool = 0f;
                        }
                    }

                    var totalContribution = contributions.Values.Sum();
                    foreach (var attacker in contributions.Keys.OrderBy(a => a))
                    {
                        var share = contributions[attacker] / totalContribution;
                        outcome.Losses.Add(new RoundLoss
                        {
                            Victim = victim, Attacker = attacker, DamageDealt = dealt * share, Ships = lostShips * share,
                            StrengthLost = lostStrength * share, Engaged = engaged * share,
                        });
                    }
                }
                return outcome;
            }

            private static void FightAt(Planet planet, GameAIMap map, WarshipStats stats, GameAIConstants constants,
                List<Planet.UpdateResult> results, List<CombatReport> reports)
            {
                // Start-of-turn snapshot of every warship that can fight (a ship with no health left, or no Health stat, cannot).
                var units = new List<CombatUnit>();
                for (var i = 0; i < planet.DockedShips.Count; i++)
                {
                    var ship = planet.DockedShips[i];
                    if (ship.Kind != Ship.ShipKind.WarShip || ship.Owner == Planet.NoOwner) continue;
                    if (stats.CurrentHealth(ship) <= 0f) continue;
                    units.Add(CombatUnit.Make(i, ship.Owner, i, stats.Offense(ship.Template, ship.ResearchSnapshot),
                        stats.Health(ship.Template, ship.ResearchSnapshot), stats.Defense(ship.Template, ship.ResearchSnapshot), ship.Damage));
                }

                var outcome = Round(units, (a, b) => map.Diplomacy.IsAtWar(a, b, true), (a, b) => map.Diplomacy.Hostility(a, b),
                    constants.combatDamageK);
                foreach (var loss in outcome.Losses)
                {
                    reports.Add(new CombatReport
                    {
                        Planet = planet.PlanetName, Attacker = loss.Attacker, Victim = loss.Victim,
                        DamageDealt = loss.DamageDealt, ShipsDestroyed = loss.Ships,
                    });
                    if (loss.Ships > 0f)
                        results.Add(new Planet.UpdateResult(planet.PlanetName,
                            Planet.UpdateResult.UpdateResultType.UpdateResultTypeWarshipsLost,
                            new CombatLoss { Attacker = loss.Attacker, Ships = loss.Ships, StrengthLost = loss.StrengthLost },
                            loss.Victim));
                }
                foreach (var pair in outcome.Damage) planet.DockedShips[pair.Key].Damage += pair.Value;   // applied after every side was computed
                var doomed = outcome.Destroyed.Select(id => planet.DockedShips[id]).ToList();
                foreach (var ship in doomed) planet.DestroyDockedShip(ship);
            }
```
(`CombatUnit.Make` uses `Math.Max(0f, max - damage)`, the same arithmetic as `WarshipStats.CurrentHealth`/`EffectiveOffense`, so a fight resolves bit-for-bit as before.)

- [ ] **Step 4: The check passes**

Run the check. Expected: `[CombatSelfCheck] ALL PASSED` (the new `RunRoundCheck` and every existing combat case, unchanged), `Run All AI Self-Checks` 14 suites passed.

- [ ] **Step 5: Commit**

```bash
git add Assets/Flatspace/GameAI/CombatSystem.cs Assets/Editor/CombatSelfCheck.cs
git commit -m "refactor: extract CombatSystem.Round, the pure per-planet fight core (no behavior change)

Co-Authored-By: Claude Sonnet 5.5 <noreply@anthropic.com>"
```

---

### Task 4: The engaged-force loss share

`CombatLoss` gains `Engaged`; combat appends a `WarshipsLost` result on **every** fight turn; the loss share is window lost / window engaged. Only readers of the result are `PlayerAI.RecordLosses` and `CombatSelfCheck` (verified by grep), and `AITuningLogger.LogPlanetEvents` ignores the type.

**Files:**
- Modify: `Assets/Flatspace/GameAI/CombatSystem.cs` (`CombatLoss`, `FightAt`)
- Modify: `Assets/Flatspace/GameAI/PlayerAI.cs` (`_losses`, `RecordLosses`, `LossShareToward`, its two callers, `Pair.Engaged`)
- Modify: `Assets/Flatspace/GameAI/DiplomacyState.cs` (`Pair`, after `LossAccum`)
- Modify: `Assets/Flatspace/GameAI/GameAI.cs` (the `LogHostility` call in `LogEconomySummary`)
- Modify: `Assets/Flatspace/Diagnostics/AITuningLogger.cs` (`LogHostility`)
- Modify: `Assets/Editor/CombatSelfCheck.cs`, `Assets/Editor/DiplomacySelfCheck.cs`, `Assets/Editor/SimultaneitySelfCheck.cs`

**Interfaces:**
- Produces: `CombatLoss.Engaged` (float); `PlayerAI.LossShareToward(int rival, int turn)` (no strength argument, returns window lost / window engaged, 0 when engaged is 0); `PlayerAI.EngagedToward(int rival, int turn)` (window engaged); `DiplomacyState.Pair.Engaged` (log-only); `AITuningLogger.LogHostility(..., float lossShare, float lossAccum, float engaged)`.

- [ ] **Step 1: Write the failing checks (update the existing assertions to the new behavior)**

`Assets/Editor/CombatSelfCheck.cs`:
- Line ~296 (`"no ship died, so no loss result"`): replace with
```csharp
            ok &= Check(results.Count == 2 && results.All(r => r.Result == WarshipsLost && ((CombatLoss)r.Data).Ships == 0f),
                "no ship died, but a fight turn still appends a zero-loss WarshipsLost result per victim");
            ok &= Check(Near(((CombatLoss)results.First(r => r.PlayerID == 0).Data).Engaged, 3150f)
                        && Near(((CombatLoss)results.First(r => r.PlayerID == 1).Data).Engaged, 2100f),
                "each result carries its victim's engaged strength: player 0's 3 ships 3150, player 1's 2 ships 2100");
```
- Focus-fire case (line ~315): replace `var loss = results.Where(r => r.Result == WarshipsLost).ToList();` with `var loss = results.Where(r => r.Result == WarshipsLost && r.PlayerID == 1).ToList();`
- Attribution case (line ~354): replace the `losses` line with
```csharp
            var losses = results.Where(r => r.Result == WarshipsLost && r.PlayerID == 1).OrderBy(r => ((CombatLoss)r.Data).Attacker).ToList();
```
  and after its last `ok &= Check(...)` add
```csharp
            ok &= Check(Near(((CombatLoss)losses[0].Data).Engaged, 3.75f) && Near(((CombatLoss)losses[1].Data).Engaged, 1.25f),
                "the victim's engaged strength 5 (its one ship: health 5, offense 0.5, 0.5 x (5 + 5)) is shared 30:10 like its loss, so two attackers count it once in total");
```
- In `RunTunableDefaultsCheck`/surrounding nothing else changes.

`Assets/Editor/DiplomacySelfCheck.cs`:
- Replace the `Loss` helper (line 1029-1032) with
```csharp
    private static Planet.UpdateResult Loss(int victim, int attacker, float ships, float strength, float engaged)
        => new Planet.UpdateResult("A",
            Planet.UpdateResult.UpdateResultType.UpdateResultTypeWarshipsLost,
            new CombatLoss { Attacker = attacker, Ships = ships, StrengthLost = strength, Engaged = engaged }, victim);
```
- Replace lines 1057-1065 (the `RecordLosses` / `LossShareToward` block) with
```csharp
            f.AI.RecordLosses(new List<Planet.UpdateResult> { Loss(0, 1, 2f, 100f, 500f), Loss(5, 1, 9f, 999f, 999f), Loss(0, 1, 1f, 50f, 500f) }, 10);
            ok &= Check(Near(f.AI.LossShareToward(1, 10), 150f / 1000f),
                "share = lost 150 / engaged 1000 = 0.15; another player's loss result is ignored");
            ok &= Check(Near(f.AI.EngagedToward(1, 10), 1000f), "the window's engaged total is 1000");
            ok &= Check(Near(f.AI.LossShareToward(1, 19), 0.15f), "turn 19 is still inside a 10-turn window opened at turn 10");
            ok &= Check(Near(f.AI.LossShareToward(1, 20), 0f), "turn 20 is outside it: the entries are pruned");
            ok &= Check(Near(f.AI.LossShareToward(2, 10), 0f), "no losses to a rival: 0");
            f.AI.RecordLosses(new List<Planet.UpdateResult> { Loss(0, 1, 1f, 80f, 80f) }, 30);
            ok &= Check(Near(f.AI.LossShareToward(1, 30), 1f), "everything engaged was lost: 1");
            f.AI.RecordLosses(new List<Planet.UpdateResult> { Loss(0, 3, 0f, 0f, 400f) }, 30);
            ok &= Check(Near(f.AI.LossShareToward(3, 30), 0f), "a fight with nothing lost: 0, and no division by zero");
            ok &= Check(Near(f.AI.LossShareToward(4, 30), 0f), "nothing engaged at all: 0");
```
- The other `Loss(` calls at lines 1072, 1077 and 1171 gain a fifth argument equal to a plausible engaged value: `Loss(0, 1, 3f, 100f, 1000f)`, `Loss(0, 1, 2f, 100f, 1000f)`, and line 1171 `Loss(0, 1, 5f, 9000f, 10000f)` (its comment becomes `// share 9000 / 10000 = 0.9`). Also change every `new List<Planet.PlanetUpdateResult>` there if the rename left any (it did not; they already read `Planet.UpdateResult` after Task 1).

`Assets/Editor/SimultaneitySelfCheck.cs` line ~114: `new CombatLoss { Attacker = 0, Ships = 5f, StrengthLost = 3000f }` becomes `new CombatLoss { Attacker = 0, Ships = 5f, StrengthLost = 3000f, Engaged = 5100f }` (share 0.588, as before), and its comment keeps "about 0.59".

- [ ] **Step 2: The check fails**

Run the check. Expected: compile errors (`CombatLoss.Engaged`, `EngagedToward`, the 5-argument `Loss` use). RED.

- [ ] **Step 3: Implement**

`CombatSystem.cs`: in `CombatLoss` add
```csharp
            public float Engaged;         // the same share of the victim's strength at that planet at the start of the turn (a fight turn's denominator)
```
and in `FightAt` remove the `if (loss.Ships > 0f)` guard and set `Engaged`:
```csharp
                    results.Add(new Planet.UpdateResult(planet.PlanetName,
                        Planet.UpdateResult.UpdateResultType.UpdateResultTypeWarshipsLost,
                        new CombatLoss { Attacker = loss.Attacker, Ships = loss.Ships, StrengthLost = loss.StrengthLost, Engaged = loss.Engaged },
                        loss.Victim));
```
Update the `CombatLoss` doc comment: "(carried in a WarshipsLost result's Data; appended on every fight turn, with Ships and StrengthLost 0 when nothing died)".

`PlayerAI.cs` (lines 231-260):
```csharp
            // Per rival, what it did to my warships over the last lossWindowTurns: (turn, ships destroyed, their strength, my strength
            // engaged that turn). Player-private and not saved: a load starts the window empty (like the pending blockade cuts).
            private readonly Dictionary<int, List<(int turn, float ships, float strength, float engaged)>> _losses
                = new Dictionary<int, List<(int turn, float ships, float strength, float engaged)>>();
```
In `RecordLosses` replace the two lines inside the loop:
```csharp
                    if (!_losses.TryGetValue(loss.Attacker, out var list))
                        _losses[loss.Attacker] = list = new List<(int turn, float ships, float strength, float engaged)>();
                    list.Add((turn, loss.Ships, loss.StrengthLost, loss.Engaged));
```
Replace `LossShareToward` with:
```csharp
            /// <summary>Strength lost to this rival / my strength engaged against it, both totalled over the window; 0 when nothing was engaged. Prunes entries older than the window.</summary>
            public float LossShareToward(int rival, int turn)
            {
                var engaged = EngagedToward(rival, turn);
                return engaged <= 0f ? 0f : _losses[rival].Sum(e => e.strength) / engaged;
            }

            /// <summary>My strength engaged against this rival over the window (the loss share's denominator). Prunes entries older than the window.</summary>
            public float EngagedToward(int rival, int turn)
            {
                if (!_losses.TryGetValue(rival, out var list)) return 0f;
                var window = AIMap.GameAIConstants.lossWindowTurns;
                list.RemoveAll(e => turn - e.turn >= window);
                return list.Sum(e => e.engaged);
            }
```
(`ShipsLostThisTurn` still reads `e.ships` and `e.turn`; it compiles unchanged.) In `UpdateDiplomacy` change both callers: line 291 `var lossShare = LossShareToward(rival, turn);`, and line 348 `LossShare = LossShareToward(rival, turn),`; after `pair.LossShare = lossShare;` (line 310) add `pair.Engaged = EngagedToward(rival, turn);`. `myStrength` is still used elsewhere there, so nothing else changes.

`DiplomacyState.cs`: in `Pair`, after the `LossAccum` line add `public float Engaged;                  // log-only: my strength engaged against this rival over the loss window (the loss share's denominator)`.

`GameAI.cs` (the `LogHostility` call): add `, pair.Engaged` as the last argument.

`AITuningLogger.cs` `LogHostility`: signature gains `float engaged = 0f` after `lossAccum`, and the `FormatLine` argument list gains `, engaged.ToString("0", ci)`. Update its doc comment to `...|lossShare|lossAccum|engagedStrength` (older logs lack the last field).

- [ ] **Step 4: The check passes**

Run the check. Expected: `CombatSelfCheck`, `DiplomacySelfCheck` and `SimultaneitySelfCheck` all pass (the Surrender precondition in simultaneity still holds: share 0.588 against midpoint 0.1 / steepness 0.01), `Run All AI Self-Checks` 14 passed.

- [ ] **Step 5: Commit**

```bash
git add Assets/Flatspace/GameAI/CombatSystem.cs Assets/Flatspace/GameAI/PlayerAI.cs Assets/Flatspace/GameAI/DiplomacyState.cs Assets/Flatspace/GameAI/GameAI.cs Assets/Flatspace/Diagnostics/AITuningLogger.cs Assets/Editor/CombatSelfCheck.cs Assets/Editor/DiplomacySelfCheck.cs Assets/Editor/SimultaneitySelfCheck.cs
git commit -m "feat: loss share measured against the engaged force (CombatLoss.Engaged, a result every fight turn)

Co-Authored-By: Claude Sonnet 5.5 <noreply@anthropic.com>"
```

---

### Task 5: `FightProjector`, the `FightProjection` result and its wiring

**Files:**
- Create: `Assets/Flatspace/GameAI/FightProjector.cs`
- Modify: `Assets/Flatspace/Objects/Planets/Planet.cs` (enum value, priority switch)
- Modify: `Assets/Flatspace/GameAI/GameAI.cs` (`GameAIUpdate`, new `RunFightProjections`)
- Modify: `Assets/Editor/RetreatSelfCheck.cs`

**Interfaces:**
- Consumes: `CombatSystem.Round`, `CombatUnit`, `RoundOutcome` (Task 3); `GameAIConstants.retreatProjectionTurns` (Task 2); `CombatSelfCheck.Fixture` (Task 3).
- Produces:
```csharp
public class FightProjection
{
    public string Planet;
    public int Player;
    public int MyShips;                 // my warships docked there that can fight (health > 0)
    public int InboundShips;            // my own ships that join within the projection
    public float MyStrength;            // strength of every ship of mine that takes part (docked + inbound), at its entry state
    public float MyStrengthLeft;        // strength of my survivors at the end (damage lowers it: a wounded survivor counts as partly lost)
    public float RivalStrength;         // war rivals' strength at the start
    public float RivalStrengthLeft;
    public bool RivalSurvives;          // a player I am at war with still has a warship at the end
    public bool MyGroupWiped => MyStrengthLeft <= 0f;
    public int Turns;                   // rounds projected
    public List<int> Rivals;            // war rivals with warships at the planet, ascending
    public float ProjectedLossFraction => MyStrength <= 0f ? 0f : Math.Min(1f, Math.Max(0f, 1f - MyStrengthLeft / MyStrength));
}
public static FightProjection FightProjector.Project(GameAIMap map, Planet planet, int player, WarshipStats stats, GameAIConstants constants, IEnumerable<GameAI.GameAIOrder> orders)   // null when there is no fight to project
Planet.UpdateResult.UpdateResultType.UpdateResultTypeFightProjection   // PlayerID = the player, Data = FightProjection
```

- [ ] **Step 1: Write the failing check**

In `RetreatSelfCheck.RunChecks` add `ok &= RunProjectionCheck();` after the tunables check, and add:
```csharp
    private static FightProjection Project(CombatSelfCheck.Fixture f, string planet, int player, params GameAI.GameAIOrder[] orders)
        => FightProjector.Project(f.Map, f.P(planet), player, f.Stats, f.Constants, orders);

    private static readonly List<string> MaxDefense = new List<string> { "Def 1", "Def 2", "Def 3", "Def 4", "Def 5" };

    // The projection plays the fight to its end: a win, a wipe-out, defense, inbound reinforcements, no fight, a zero-health ship,
    // a third player at war with neither side, legacy mode.
    public static bool RunProjectionCheck()
    {
        var ok = true;

        using (var f = CombatSelfCheck.Fixture.Line())
        {
            f.Ships("A", 0, 3); f.Ships("A", 1, 1); f.War(0, 1);
            var mine = Project(f, "A", 0);
            ok &= Check(mine != null && !mine.RivalSurvives && mine.ProjectedLossFraction > 0f && mine.ProjectedLossFraction < 0.3f && !mine.MyGroupWiped,
                "3 against 1: I win and lose a little");
            ok &= Check(mine != null && mine.MyShips == 3 && Near(mine.MyStrength, 3150f) && mine.Rivals.SequenceEqual(new[] { 1 }), "3 ships, strength 3150, rival 1");
            var theirs = Project(f, "A", 1);
            ok &= Check(theirs != null && theirs.RivalSurvives && theirs.MyGroupWiped && Near(theirs.ProjectedLossFraction, 1f),
                "1 against 3: wiped, and the rival survives");
        }

        using (var f = CombatSelfCheck.Fixture.Line())    // peace: nothing to project
        {
            f.Ships("A", 0, 2); f.Ships("A", 1, 2);
            ok &= Check(Project(f, "A", 0) == null, "players at peace: no projection");
        }

        // Defense is counted exactly as combat counts it (K / (K + Defense)), not as a linear add-on.
        float EvenFightLoss(bool highDefense)
        {
            using (var f = CombatSelfCheck.Fixture.Line())
            {
                if (highDefense) { f.P("A").DockShipFromSave(Ship.ShipKind.WarShip, 0, MaxDefense); f.P("A").DockShipFromSave(Ship.ShipKind.WarShip, 0, MaxDefense); }
                else f.Ships("A", 0, 2);
                f.Ships("A", 1, 2); f.War(0, 1);
                return Project(f, "A", 0).ProjectedLossFraction;
            }
        }
        ok &= Check(EvenFightLoss(true) < EvenFightLoss(false), "2 high-defense ships lose less of their strength than 2 plain ones against the same rival");

        using (var f = CombatSelfCheck.Fixture.Line())    // inbound own reinforcements change the picture
        {
            f.Ships("A", 0, 1); f.Ships("A", 1, 3); f.War(0, 1);
            var without = Project(f, "A", 0);
            var fleet = new GameAI.GameAIOrder
            {
                Type = GameAI.GameAIOrder.OrderType.OrderTypeShipTransport, PlayerId = 0, Target = "A", Origin = "B", TimingDelay = 2, Data = 3,
                Fleet = new GameAI.GameAIOrder.ShipFleetPayload
                {
                    Kind = Ship.ShipKind.WarShip,
                    Snapshots = new List<List<string>> { new List<string>(), new List<string>(), new List<string>() },
                    Damage = new List<float>(),
                },
            };
            var with = Project(f, "A", 0, fleet);
            ok &= Check(without.MyGroupWiped && with.InboundShips == 3 && with.ProjectedLossFraction < without.ProjectedLossFraction,
                "1 against 3 is a wipe-out; with 3 own ships landing in 2 turns the loss is smaller");
            var theirsFleet = new GameAI.GameAIOrder
            {
                Type = fleet.Type, PlayerId = 1, Target = "A", TimingDelay = 2, Data = 3, Fleet = fleet.Fleet,
            };
            ok &= Check(Project(f, "A", 0, theirsFleet).InboundShips == 0, "a rival's in-flight fleet is not counted");
            var elsewhere = new GameAI.GameAIOrder { Type = fleet.Type, PlayerId = 0, Target = "C", TimingDelay = 2, Data = 3, Fleet = fleet.Fleet };
            ok &= Check(Project(f, "A", 0, elsewhere).InboundShips == 0, "my fleet heading to another planet is not counted");
            var late = new GameAI.GameAIOrder { Type = fleet.Type, PlayerId = 0, Target = "A", TimingDelay = f.Constants.retreatProjectionTurns + 5, Data = 3, Fleet = fleet.Fleet };
            ok &= Check(Project(f, "A", 0, late).InboundShips == 0, "a fleet landing beyond the projection cap is ignored");
        }

        using (var f = CombatSelfCheck.Fixture.Line())    // a ship at exactly 0 health does not fight, count or crash anything
        {
            f.Ships("A", 0, 1, 100f);     // 100 damage on a Health stat of 100: health 0
            f.Ships("A", 0, 1);
            f.Ships("A", 1, 1); f.War(0, 1);
            var p = Project(f, "A", 0);
            ok &= Check(p != null && p.MyShips == 1 && Near(p.MyStrength, 1050f), "the 0-health ship is not counted: 1 ship, strength 1050");
        }

        using (var f = CombatSelfCheck.Fixture.Line())    // a third player at war with neither side
        {
            f.Ships("A", 0, 1); f.Ships("A", 1, 1); f.Ships("A", 2, 5); f.War(0, 1);
            var p = Project(f, "A", 0);
            ok &= Check(p != null && p.Rivals.SequenceEqual(new[] { 1 }), "player 2 is nobody's war rival: only player 1 is mine");
        }

        using (var f = CombatSelfCheck.Fixture.Line())    // the wiring: one result per (planet, player) in a fight, none in legacy mode
        {
            f.Ships("A", 0, 3); f.Ships("A", 1, 1); f.War(0, 1);
            var results = new List<Planet.UpdateResult>();
            GameAI.AppendFightProjections(f.Map, f.Stats, f.Constants, new List<GameAI.GameAIOrder>(), results);
            ok &= Check(results.Count == 2 && results.All(r => r.Result == Planet.UpdateResult.UpdateResultType.UpdateResultTypeFightProjection
                                                                 && r.Data is FightProjection && r.Name == "A"),
                "two projection results, one per player docked in the war fight at A");
            ok &= Check(results.Select(r => r.PlayerID).OrderBy(i => i).SequenceEqual(new[] { 0, 1 }), "their PlayerIDs are 0 and 1");
            f.Map.Diplomacy.Enabled = false;
            var legacy = new List<Planet.UpdateResult>();
            GameAI.AppendFightProjections(f.Map, f.Stats, f.Constants, new List<GameAI.GameAIOrder>(), legacy);
            ok &= Check(legacy.Count == 0, "legacy mode: no projection results");
        }
        return ok;
    }
```

- [ ] **Step 2: The check fails**

Run the check. Expected: compile errors (`FightProjector`, `FightProjection`, `GameAI.AppendFightProjections` do not exist). RED.

- [ ] **Step 3: Implement the projector**

Create `Assets/Flatspace/GameAI/FightProjector.cs`:
```csharp
using System;
using System.Collections.Generic;
using System.Linq;

namespace FlatSpace.AI
{
    /// <summary>
    /// What one player's group at one fight planet is projected to come to: its share of strength lost, and whether a war rival
    /// is still standing, after the fight is played to its end (carried in a FightProjection result's Data).
    /// </summary>
    public class FightProjection
    {
        public string Planet;
        public int Player;
        public int MyShips;                 // my warships docked there that can fight (health > 0)
        public int InboundShips;            // my own ships that join within the projection
        public float MyStrength;            // strength of every ship of mine that takes part (docked + inbound), at its entry state
        public float MyStrengthLeft;        // strength of my survivors at the end (damage lowers it: a wounded survivor counts as partly lost)
        public float RivalStrength;         // war rivals' strength at the start
        public float RivalStrengthLeft;
        public bool RivalSurvives;          // a player I am at war with still has a warship at the end
        public int Turns;                   // rounds projected
        public List<int> Rivals = new List<int>();   // war rivals with warships at the planet, ascending

        public bool MyGroupWiped => MyStrengthLeft <= 0f;
        public float ProjectedLossFraction
            => MyStrength <= 0f ? 0f : Math.Min(1f, Math.Max(0f, 1f - MyStrengthLeft / MyStrength));
        public float RivalSurvivorsFraction => RivalStrength <= 0f ? 0f : Math.Min(1f, RivalStrengthLeft / RivalStrength);
    }

    /// <summary>
    /// Plays a planet's fight forward round by round with the same core real combat uses (CombatSystem.Round), on a copy of the
    /// start-of-turn state: every docked warship at the planet, my own in-flight ships landing there on their known arrival turn.
    /// Rival in-flight ships and repair (which never happens while an at-war warship is present) are not modeled. Pure.
    /// </summary>
    public static class FightProjector
    {
        public static FightProjection Project(GameAIMap map, Planet planet, int player, WarshipStats stats,
            GameAIConstants constants, IEnumerable<GameAI.GameAIOrder> orders)
        {
            if (map == null || planet == null || stats == null || !map.Diplomacy.Enabled) return null;

            var units = new List<CombatUnit>();
            var nextId = 0;
            for (var i = 0; i < planet.DockedShips.Count; i++)
            {
                var ship = planet.DockedShips[i];
                if (ship.Kind != Ship.ShipKind.WarShip || ship.Owner == Planet.NoOwner) continue;
                if (stats.CurrentHealth(ship) <= 0f) continue;
                units.Add(CombatUnit.Make(nextId++, ship.Owner, i, stats.Offense(ship.Template, ship.ResearchSnapshot),
                    stats.Health(ship.Template, ship.ResearchSnapshot), stats.Defense(ship.Template, ship.ResearchSnapshot), ship.Damage));
            }

            Func<int, int, bool> atWar = (a, b) => map.Diplomacy.IsAtWar(a, b, true);
            var rivals = units.Select(u => u.Owner).Distinct().Where(o => o != player && atWar(player, o)).OrderBy(o => o).ToList();
            if (rivals.Count == 0 || !units.Any(u => u.Owner == player)) return null;

            // My own fleets heading here: they join before the combat of the projected round equal to their delay.
            var inbound = new List<(int round, CombatUnit unit)>();
            var index = planet.DockedShips.Count;
            foreach (var order in orders ?? Enumerable.Empty<GameAI.GameAIOrder>())
            {
                if (order.Type != GameAI.GameAIOrder.OrderType.OrderTypeShipTransport || order.PlayerId != player) continue;
                if (order.Target != planet.PlanetName || order.Fleet == null || order.Fleet.Kind != Ship.ShipKind.WarShip) continue;
                var round = Math.Max(1, order.TimingDelay);
                if (round > constants.retreatProjectionTurns) continue;
                var template = constants.warShipData;
                for (var i = 0; i < order.Fleet.Snapshots.Count; i++)
                {
                    var snapshot = order.Fleet.Snapshots[i];
                    var unit = CombatUnit.Make(nextId++, player, index++, stats.Offense(template, snapshot),
                        stats.Health(template, snapshot), stats.Defense(template, snapshot), order.Fleet.DamageAt(i));
                    if (unit.Health > 0f) inbound.Add((round, unit));
                }
            }

            var projection = new FightProjection
            {
                Planet = planet.PlanetName, Player = player, Rivals = rivals,
                MyShips = units.Count(u => u.Owner == player),
                MyStrength = units.Where(u => u.Owner == player).Sum(u => u.Strength),
                RivalStrength = units.Where(u => rivals.Contains(u.Owner)).Sum(u => u.Strength),
            };

            Func<int, int, float> hostility = (a, b) => map.Diplomacy.Hostility(a, b);
            for (var round = 1; round <= constants.retreatProjectionTurns; round++)
            {
                foreach (var arrival in inbound.Where(a => a.round == round))
                {
                    units.Add(arrival.unit);
                    projection.InboundShips++;
                    projection.MyStrength += arrival.unit.Strength;
                }
                if (!units.Any(u => u.Owner == player) || !units.Any(u => rivals.Contains(u.Owner))) break;

                var outcome = CombatSystem.Round(units, atWar, hostility, constants.combatDamageK);
                if (outcome.Damage.Count == 0 && outcome.Destroyed.Count == 0) break;   // nobody can hurt anybody: the fight is over
                units = units.Where(u => !outcome.Destroyed.Contains(u.Id))
                    .Select(u => outcome.Damage.TryGetValue(u.Id, out var damage) ? u.WithDamage(damage) : u).ToList();
                projection.Turns = round;
            }

            projection.MyStrengthLeft = units.Where(u => u.Owner == player).Sum(u => u.Strength);
            projection.RivalStrengthLeft = units.Where(u => rivals.Contains(u.Owner)).Sum(u => u.Strength);
            projection.RivalSurvives = units.Any(u => rivals.Contains(u.Owner));
            return projection;
        }
    }
}
```
In `Planet.cs`: append the enum value and its priority case. After `UpdateResultTypeColonyShipsLost` add a comma and
```csharp
            ,
            // Appended last: serialized as an int. Data is a FightProjection (FightProjector); PlayerID is the player it is about.
            UpdateResultTypeFightProjection
```
(format it as a normal trailing entry: `UpdateResultTypeColonyShipsLost,` then the comment and `UpdateResultTypeFightProjection`). In the constructor's priority `switch`, add `case ResultType.UpdateResultTypeFightProjection:` directly under `case ResultType.UpdateResultTypeNone:` so it takes `UpdateResultPriorityNone`.

In `GameAI.cs` add after `RunCombat`:
```csharp
            // After combat: one FightProjection result per (planet, player) where that player's warships share a planet with a
            // war rival's, so every player decides about leaving against the same projection (see FightProjector, RetreatPlanner).
            private void RunFightProjections(List<Planet.UpdateResult> results)
            {
                var stats = new WarshipStats(BlockadeSystem.ResearchItemsFrom(Gameboard.Instance.players));
                AppendFightProjections(GameAIMap, stats, GameAIMap.GameAIConstants, CurrentAIOrders, results);
            }

            /// <summary>Public and free of Gameboard.Instance so the self-check can drive it. Nothing is appended while diplomacy is off.</summary>
            public static void AppendFightProjections(GameAIMap map, WarshipStats stats, GameAIConstants constants,
                List<GameAIOrder> orders, List<Planet.UpdateResult> results)
            {
                if (!map.Diplomacy.Enabled) return;
                foreach (var planet in map.PlanetList)
                {
                    var owners = planet.DockedShips.Where(s => s.Kind == Ship.ShipKind.WarShip && s.Owner != Planet.NoOwner)
                        .Select(s => s.Owner).Distinct().OrderBy(o => o).ToList();
                    if (owners.Count < 2) continue;
                    foreach (var owner in owners)
                    {
                        var projection = FightProjector.Project(map, planet, owner, stats, constants, orders);
                        if (projection != null)
                            results.Add(new Planet.UpdateResult(planet.PlanetName,
                                Planet.UpdateResult.UpdateResultType.UpdateResultTypeFightProjection, projection, owner));
                    }
                }
            }
```
and in `GameAIUpdate` add `RunFightProjections(planetUpdateResults);` on the line after `RunCombat(planetUpdateResults);`. Add `using System.Linq;` if `GameAI.cs` lacks it (it already uses `.Select`/`.Where`, so it has it).

- [ ] **Step 4: Check no consumer chokes on the new result type**

```bash
grep -rn "UpdateResultTypeWarshipsLost\|switch (.*\.Result)\|switch (result" Assets/Flatspace --include=*.cs
```
Expected: every `switch` over `.Result` has a `default`/ignores unknown values (the same path the `WarshipsLost` and `ColonyShipsLost` types already take). If one throws on an unknown value, add the new type to its ignore case.

- [ ] **Step 5: The check passes**

Run the check. Expected: `[RetreatSelfCheck] ALL PASSED` including `RunProjectionCheck`; `Run All AI Self-Checks` 14 passed.

- [ ] **Step 6: Commit**

```bash
git add Assets/Flatspace/GameAI/FightProjector.cs Assets/Flatspace/GameAI/FightProjector.cs.meta Assets/Flatspace/Objects/Planets/Planet.cs Assets/Flatspace/GameAI/GameAI.cs Assets/Editor/RetreatSelfCheck.cs
git commit -m "feat: FightProjector and the FightProjection result (a fight played to its end, per player and planet)

Co-Authored-By: Claude Sonnet 5.5 <noreply@anthropic.com>"
```

---

### Task 6: `BlockadeView` helpers (war-rival filtering)

**Files:**
- Modify: `Assets/Flatspace/GameAI/BlockadeView.cs`
- Modify: `Assets/Editor/RetreatSelfCheck.cs`

**Interfaces:**
- Produces: `BlockadeView.OnlyFrom(ISet<int> blockers)` (a copy keeping only blockades whose blocker is in the set; a remembered blockade with no known blocker, `Planet.NoOwner`, is dropped), `BlockadeView.Without(string planetName)` (a copy minus one planet), `static BlockadeView.WithBlockers(params (string name, float value, int blocker)[] entries)` (a test helper).

- [ ] **Step 1: Write the failing check**

In `RetreatSelfCheck.RunChecks` add `ok &= RunBlockadeViewCheck();` and add:
```csharp
    public static bool RunBlockadeViewCheck()
    {
        var ok = true;
        var view = BlockadeView.WithBlockers(("B", 5f, 1), ("C", 7f, 2), ("D", 3f, Planet.NoOwner));
        var war = view.OnlyFrom(new SortedSet<int> { 1 });
        ok &= Check(war.IsBlockaded("B") && Near(war.Value("B"), 5f) && war.Blocker("B") == 1, "a blockade by a war rival (1) is kept with its value and blocker");
        ok &= Check(!war.IsBlockaded("C"), "a blockade by a player I am not at war with (2) is dropped");
        ok &= Check(!war.IsBlockaded("D"), "a remembered blockade with an unknown blocker is dropped");
        ok &= Check(view.IsBlockaded("C") && view.IsBlockaded("D"), "the original view is untouched");
        var minus = view.Without("B");
        ok &= Check(!minus.IsBlockaded("B") && minus.IsBlockaded("C") && view.IsBlockaded("B"), "Without removes one planet from a copy only");
        ok &= Check(!new BlockadeView().IsBlockaded("B"), "an empty view blockades nothing");
        return ok;
    }
```

- [ ] **Step 2: The check fails** — run the check; expected compile errors (`WithBlockers`, `OnlyFrom`, `Without`). RED.

- [ ] **Step 3: Implement** — in `BlockadeView.cs` add after `WithValues`:
```csharp

        /// <summary>A view with these blockades and blockers. For planning tests.</summary>
        public static BlockadeView WithBlockers(params (string name, float value, int blocker)[] entries)
        {
            var view = new BlockadeView();
            foreach (var (name, value, blocker) in entries) view._blockaded[name] = (value, blocker);
            return view;
        }

        /// <summary>
        /// A copy that keeps only the blockades whose blocker is in `blockers` (my war rivals). A remembered blockade whose
        /// blocker is unknown (Planet.NoOwner) cannot be matched to a war rival and is dropped.
        /// </summary>
        public BlockadeView OnlyFrom(ISet<int> blockers)
        {
            var view = new BlockadeView();
            foreach (var pair in _blockaded)
                if (pair.Value.blocker != Planet.NoOwner && blockers.Contains(pair.Value.blocker))
                    view._blockaded[pair.Key] = pair.Value;
            return view;
        }

        /// <summary>A copy without one planet (a retreat destination that is itself blockaded is still a valid destination).</summary>
        public BlockadeView Without(string planetName)
        {
            var view = new BlockadeView();
            foreach (var pair in _blockaded)
                if (pair.Key != planetName) view._blockaded[pair.Key] = pair.Value;
            return view;
        }
```

- [ ] **Step 4: The check passes** — run the check; expected `[RetreatSelfCheck] ALL PASSED`.

- [ ] **Step 5: Commit**
```bash
git add Assets/Flatspace/GameAI/BlockadeView.cs Assets/Editor/RetreatSelfCheck.cs
git commit -m "feat: BlockadeView.OnlyFrom / Without / WithBlockers for war-rival blockade filtering

Co-Authored-By: Claude Sonnet 5.5 <noreply@anthropic.com>"
```

---

### Task 7: `RetreatMatrix` (Stay or Retreat, weighted)

**Files:**
- Create: `Assets/Flatspace/GameAI/RetreatMatrix.cs`
- Modify: `Assets/Editor/RetreatSelfCheck.cs`

**Interfaces:**
- Consumes: `ScoreMatrix`, `IScoreMatrixDecisionElement`, `IScoreMatrixChoiceElement`, `ShipAction` (`Origin`, `Target`, `Cost`, `Count`, `Kind`), `GameAIConstants` retreat tunables.
- Produces:
```csharp
public static class RetreatMatrix
{
    public struct Row { string Planet; int Ships; float LossFraction; string Destination; int Tier; float PathCost; }   // Destination empty/null = none: the row can only Stay
    public struct Decision { string Planet; bool Retreat; float PRetreat; ShipAction Action; }                      // Action is meaningful when Retreat
    public static float RetreatProbability(float lossFraction, GameAIConstants constants);
    public static List<Decision> Decide(List<Row> rows, GameAIConstants constants);                                  // one Decision per row, ordered by planet name
}
```

- [ ] **Step 1: Write the failing check**

In `RetreatSelfCheck.RunChecks` add `ok &= RunRetreatMatrixCheck();` and add:
```csharp
    public static bool RunRetreatMatrixCheck()
    {
        var ok = true;
        var c = ScriptableObject.CreateInstance<GameAIConstants>();
        try
        {
            // The curve: even odds at the midpoint, rising and falling either side (1 / (1 + e^(-(loss - 0.5) / 0.1))).
            ok &= Check(Near(RetreatMatrix.RetreatProbability(c.retreatLossFraction, c), 0.5f), "at retreatLossFraction the retreat weight is 0.5");
            ok &= Check(Near(RetreatMatrix.RetreatProbability(0.3f, c), 0.119f) && Near(RetreatMatrix.RetreatProbability(0.7f, c), 0.881f),
                "0.3 gives about 12% and 0.7 about 88% with the default steepness 0.1");
            ok &= Check(RetreatMatrix.RetreatProbability(1f, c) > 0.99f && RetreatMatrix.RetreatProbability(0f, c) < 0.01f, "a wipe-out is near certain, no loss near never");

            // A deterministic roll: a very steep curve gives exactly 0 or 1.
            c.retreatSteepness = 0.001f;
            RetreatMatrix.Row Row(string planet, float loss, string destination, int tier = 1, float cost = 100f)
                => new RetreatMatrix.Row { Planet = planet, Ships = 3, LossFraction = loss, Destination = destination, Tier = tier, PathCost = cost };
            GameAI.Rand = new System.Random(7);

            var decisions = RetreatMatrix.Decide(new List<RetreatMatrix.Row> { Row("X", 0.95f, "H") }, c);
            ok &= Check(decisions.Count == 1 && decisions[0].Retreat && decisions[0].Action.Origin == "X" && decisions[0].Action.Target == "H"
                        && decisions[0].Action.Count == 3 && Near(decisions[0].Action.Cost, 100f) && decisions[0].PRetreat > 0.99f,
                "a loss far above the midpoint retreats: the whole group of 3 from X to H, with its weight");

            decisions = RetreatMatrix.Decide(new List<RetreatMatrix.Row> { Row("X", 0.1f, "H") }, c);
            ok &= Check(decisions.Count == 1 && !decisions[0].Retreat && decisions[0].PRetreat < 0.01f, "a loss far below the midpoint stays");

            decisions = RetreatMatrix.Decide(new List<RetreatMatrix.Row> { Row("X", 0.95f, "") }, c);
            ok &= Check(decisions.Count == 1 && !decisions[0].Retreat, "a row with no destination offers Stay only, so it stays");

            // Two fight planets, one destination: IndependentRows, both go there.
            decisions = RetreatMatrix.Decide(new List<RetreatMatrix.Row> { Row("X", 0.95f, "H"), Row("Y", 0.9f, "H") }, c);
            ok &= Check(decisions.Count == 2 && decisions.All(d => d.Retreat && d.Action.Target == "H"),
                "two groups retreat to the same destination: neither row removes it from the other");
            ok &= Check(decisions[0].Planet == "X" && decisions[1].Planet == "Y", "decisions come back ordered by planet name");
        }
        finally { Object.DestroyImmediate(c); }
        return ok;
    }
```

- [ ] **Step 2: The check fails** — run the check; expected compile errors (`RetreatMatrix`). RED.

- [ ] **Step 3: Implement**

Create `Assets/Flatspace/GameAI/RetreatMatrix.cs` (same namespace style as `StanceMatrix.cs`):
```csharp
using System;
using System.Collections.Generic;
using System.Linq;

namespace FlatSpace
{
    namespace AI
    {
        public struct RetreatDecisionElement : IScoreMatrixDecisionElement
        {
            public string Target { get; set; }     // the fight planet
            public float Priority { get; set; }
            public int NumChoices => 1;
        }

        public class RetreatDecisionComparer : IComparer<RetreatDecisionElement>
        {
            // Higher priority first, then by planet name (never compare the name diff against the priority itself).
            public int Compare(RetreatDecisionElement x, RetreatDecisionElement y)
                => x.Priority == y.Priority ? string.CompareOrdinal(x.Target, y.Target) : y.Priority.CompareTo(x.Priority);
        }

        public struct RetreatChoiceElement : IScoreMatrixChoiceElement
        {
            public string Planet;        // the fight planet (the row)
            public bool Retreat;         // false = Stay
            public string Destination;   // empty for Stay
            public int Ships;
            public float PathCost;
            public float Weight;

            public string Target => Retreat ? Destination : Planet;
            public float Cost => PathCost;
            public float Surplus => 0f;
            public float Shortage => 0f;

            public bool Equals(IScoreMatrixChoiceElement other)
                => other is RetreatChoiceElement r && r.Planet == Planet && r.Retreat == Retreat && r.Destination == Destination;
            public override bool Equals(object obj) => obj is IScoreMatrixChoiceElement s && Equals(s);
            public override int GetHashCode() => ((Planet ?? string.Empty).GetHashCode() * 31 + (Destination ?? string.Empty).GetHashCode()) * 2 + (Retreat ? 1 : 0);
        }

        /// <summary>
        /// The retreat-or-stay decision for the fight planets that passed the gate, a ScoreMatrix with IndependentRows (the
        /// stance-matrix pattern): one row per planet, the choices Stay and Retreat (to the best tiered destination), weighted by
        /// how much of the group the projected fight would cost. A row with no destination offers Stay only. Pure.
        /// </summary>
        public static class RetreatMatrix
        {
            public struct Row
            {
                public string Planet;
                public int Ships;
                public float LossFraction;     // the projected share of the group's strength lost
                public string Destination;     // empty or null = none: the row can only Stay
                public int Tier;
                public float PathCost;
            }

            public struct Decision
            {
                public string Planet;
                public bool Retreat;
                public float PRetreat;         // the weight Retreat had in the roll
                public ShipAction Action;      // meaningful when Retreat
            }

            public static float RetreatProbability(float lossFraction, GameAIConstants constants)
            {
                var steepness = Math.Max(constants.retreatSteepness, 0.0001f);
                return (float)(1.0 / (1.0 + Math.Exp(-(lossFraction - constants.retreatLossFraction) / steepness)));
            }

            public static List<Decision> Decide(List<Row> rows, GameAIConstants constants)
            {
                var matrix = new ScoreMatrix<RetreatDecisionElement, RetreatChoiceElement, ShipAction>(new RetreatDecisionComparer())
                {
                    IndependentRows = true,
                };
                var pRetreat = new Dictionary<string, float>();
                var rank = 0f;
                foreach (var row in rows.OrderByDescending(r => r.LossFraction).ThenBy(r => r.Planet, StringComparer.Ordinal))
                {
                    var p = RetreatProbability(row.LossFraction, constants);
                    pRetreat[row.Planet] = p;
                    var choices = new List<RetreatChoiceElement>
                    {
                        new RetreatChoiceElement { Planet = row.Planet, Retreat = false, Destination = string.Empty, Ships = row.Ships, Weight = 1f - p },
                    };
                    if (!string.IsNullOrEmpty(row.Destination))
                        choices.Add(new RetreatChoiceElement
                        {
                            Planet = row.Planet, Retreat = true, Destination = row.Destination, Ships = row.Ships,
                            PathCost = row.PathCost, Weight = p,
                        });
                    matrix.MatrixElements.Add(new RetreatDecisionElement { Target = row.Planet, Priority = -rank++ }, choices);
                }

                var actions = matrix.GenerateActionList(
                    (decision, choice) => choice.Retreat
                        ? new ShipAction
                        {
                            Origin = choice.Planet, Target = choice.Destination, Cost = choice.PathCost, Count = choice.Ships,
                            Kind = Ship.ShipKind.WarShip,
                        }
                        : new ShipAction { Origin = choice.Planet, Target = string.Empty, Cost = 0f, Count = 0, Kind = Ship.ShipKind.WarShip },
                    null,
                    choice => choice.Weight);
                return actions
                    .OrderBy(a => a.Origin, StringComparer.Ordinal)
                    .Select(a => new Decision { Planet = a.Origin, Retreat = a.Count > 0, PRetreat = pRetreat[a.Origin], Action = a })
                    .ToList();
            }
        }
    }
}
```

- [ ] **Step 4: The check passes** — run the check; expected `[RetreatSelfCheck] ALL PASSED`.

- [ ] **Step 5: Commit**
```bash
git add Assets/Flatspace/GameAI/RetreatMatrix.cs Assets/Flatspace/GameAI/RetreatMatrix.cs.meta Assets/Editor/RetreatSelfCheck.cs
git commit -m "feat: RetreatMatrix, the weighted Stay-or-Retreat decision (stance-matrix pattern)

Co-Authored-By: Claude Sonnet 5.5 <noreply@anthropic.com>"
```

---

### Task 8: `RetreatPlanner` (gate, tiered destination, plan)

**Files:**
- Create: `Assets/Flatspace/GameAI/RetreatPlanner.cs`
- Modify: `Assets/Editor/RetreatSelfCheck.cs`

**Interfaces:**
- Consumes: `FightProjection` (Task 5), `RetreatMatrix` (Task 7), `BlockadeView.OnlyFrom/Without/WithBlockers` (Task 6), `RoutePlanner.PlanRoute(map, origin, target, view, maxNodes)` returning `PlannedRoute { Nodes, Cost }` or null, `PlayerKnowledge.KnownPlanets(playerId)`, `GameAIConstants.blockadeBreakMargin`, `maxPathNodesForShipTransport`, `defaultTravelSpeed`, the retreat tunables, `WarshipStats.EffectiveOffense(Ship)`.
- Produces:
```csharp
public class RetreatPlanner
{
    public class Retreat { string Planet; string Destination; int Tier; int Ships; float LossFraction; float RivalSurvivorsFraction; float RouteCost; float PRetreat; float RememberedBlockade; float MyOffense; }
    public class Stay { string Planet; float LossFraction; float PRetreat; }
    public class Hold { string Planet; string Reason; float LossFraction; }          // Reason: "NoDestination" or "OwnPlanetNotWiped"
    public class Plan { List<ShipAction> Actions; List<Retreat> Retreats; List<Stay> Stays; List<Hold> Holds; HashSet<string> Retreating; }
    public const string HoldNoDestination = "NoDestination";
    public const string HoldOwnPlanetNotWiped = "OwnPlanetNotWiped";
    public RetreatPlanner(GameAIMap map, int playerId, ISet<int> warRivals, BlockadeView view, WarshipStats stats)
    public Plan Decide(IEnumerable<FightProjection> projections)
}
```

- [ ] **Step 1: Write the failing check**

In `RetreatSelfCheck.RunChecks` add `ok &= RunRetreatPlannerCheck();` and add (the fixture is the A-B-C-D line; player 0 is "me", player 1 the rival; `Colonize` makes a planet populated and owned):
```csharp
    private static RetreatPlanner.Plan Plan(CombatSelfCheck.Fixture f, BlockadeView view, params FightProjection[] projections)
    {
        f.Map.Knowledge.Update(f.Map, 2, 8);
        return new RetreatPlanner(f.Map, 0, new SortedSet<int> { 1 }, view, f.Stats).Decide(projections);
    }

    // A hand-made projection for the planner (the projector has its own check).
    private static FightProjection Doomed(string planet, float lossFraction, bool rivalSurvives = true)
        => new FightProjection
        {
            Planet = planet, Player = 0, MyShips = 1, MyStrength = 1000f, MyStrengthLeft = 1000f * (1f - lossFraction),
            RivalStrength = 3000f, RivalStrengthLeft = 2500f, RivalSurvives = rivalSurvives, Turns = 5, Rivals = new List<int> { 1 },
        };

    public static bool RunRetreatPlannerCheck()
    {
        var ok = true;
        void Prepare(CombatSelfCheck.Fixture f)
        {
            f.Constants.retreatSteepness = 0.001f;    // a deterministic roll
            GameAI.Rand = new System.Random(3);
        }

        // Tier 1: my own populated planet with no war-rival ship, the cheapest path: B (100) over A (200).
        using (var f = CombatSelfCheck.Fixture.Line())
        {
            Prepare(f);
            f.Colonize("A", 0); f.Colonize("B", 0); f.Colonize("D", 1);
            f.Ships("C", 0, 1); f.Ships("C", 1, 3); f.War(0, 1);
            var plan = Plan(f, null, Doomed("C", 0.95f));
            ok &= Check(plan.Actions.Count == 1 && plan.Actions[0].Origin == "C" && plan.Actions[0].Target == "B" && plan.Actions[0].Count == 1,
                "tier 1: the group at C retreats to B, my nearest populated planet");
            ok &= Check(plan.Retreats.Count == 1 && plan.Retreats[0].Tier == 1 && plan.Retreating.Contains("C"), "tier 1 recorded, C is retreating");
        }

        // The gate: a projected win, or a loss under retreatCheckFraction, never reaches the matrix.
        using (var f = CombatSelfCheck.Fixture.Line())
        {
            Prepare(f);
            f.Colonize("A", 0); f.Colonize("B", 0); f.Colonize("D", 1);
            f.Ships("C", 0, 1); f.Ships("C", 1, 1); f.War(0, 1);
            ok &= Check(Plan(f, null, Doomed("C", 0.95f, rivalSurvives: false)).Actions.Count == 0, "the rival does not survive the projection: stay");
            ok &= Check(Plan(f, null, Doomed("C", 0.2f)).Actions.Count == 0, "a loss of 20% is under the 30% gate: stay, nothing rolled");
        }

        // Tier 2: my planets are occupied by a war-rival ship, B and C... an empty known planet is the next choice.
        using (var f = CombatSelfCheck.Fixture.Line())
        {
            Prepare(f);
            f.Colonize("A", 0); f.Colonize("D", 1);
            f.Ships("A", 1, 1);                                        // a war rival's warship at my only populated planet
            f.Ships("C", 0, 1); f.Ships("C", 1, 3); f.War(0, 1);
            var plan = Plan(f, null, Doomed("C", 0.95f));
            ok &= Check(plan.Actions.Count == 1 && plan.Actions[0].Target == "B" && plan.Retreats[0].Tier == 2,
                "tier 2: A holds a war-rival warship, so the empty known planet B is chosen");
        }

        // Blockades count only when a war rival is the blocker, for the destination and the route.
        using (var f = CombatSelfCheck.Fixture.Line())
        {
            Prepare(f);
            f.Colonize("A", 0); f.Colonize("B", 0); f.Colonize("D", 1);
            f.Ships("C", 0, 1); f.Ships("C", 1, 3); f.War(0, 1);
            ok &= Check(Plan(f, BlockadeView.WithBlockers(("B", 5f, 2)), Doomed("C", 0.95f)).Actions[0].Target == "B",
                "B blockaded by player 2, who is not a war rival: still the destination");
            ok &= Check(Plan(f, BlockadeView.WithValues(("B", 5f)), Doomed("C", 0.95f)).Actions[0].Target == "B",
                "a remembered blockade with an unknown blocker is ignored");
            var byRival = Plan(f, BlockadeView.WithBlockers(("B", 5f, 1)), Doomed("C", 0.95f));
            ok &= Check(byRival.Actions.Count == 0 || byRival.Actions[0].Target != "B",
                "B blockaded by a war rival is not a destination, and the route C-B-A would cross it, so A is out too (tier 3 D may be chosen)");
        }

        // Tier 3: a war rival's planet whose remembered blockade is lower than my offense x (1 + margin).
        using (var f = CombatSelfCheck.Fixture.Line())
        {
            Prepare(f);
            f.Colonize("D", 1);
            f.Ships("C", 0, 1); f.War(0, 1);                           // my group's offense is 10; the rival has no ships docked here
            // B is blockaded by the rival too, so the empty known planet B is not a tier 2 destination and tier 3 is reached.
            var low = Plan(f, BlockadeView.WithBlockers(("B", 5f, 1), ("D", 5f, 1)), Doomed("C", 0.95f));
            ok &= Check(low.Actions.Count == 1 && low.Actions[0].Target == "D" && low.Retreats[0].Tier == 3
                        && Near(low.Retreats[0].RememberedBlockade, 5f) && Near(low.Retreats[0].MyOffense, 10f),
                "tier 3: D is a war rival's planet with remembered blockade 5, under 10 / 1.1");
            var high = Plan(f, BlockadeView.WithBlockers(("B", 5f, 1), ("D", 20f, 1)), Doomed("C", 0.95f));
            ok &= Check(high.Actions.Count == 0 && high.Holds.Count == 1 && high.Holds[0].Reason == RetreatPlanner.HoldNoDestination,
                "a remembered blockade of 20 is over 10 / 1.1: no destination, the ships stay (RetreatHeld NoDestination)");
        }

        // At a planet I populate, only a wipe-out is gated.
        using (var f = CombatSelfCheck.Fixture.Line())
        {
            Prepare(f);
            f.Colonize("B", 0); f.Colonize("C", 0); f.Colonize("D", 1);
            f.Ships("C", 0, 2); f.Ships("C", 1, 3); f.War(0, 1);
            var hurt = Plan(f, null, Doomed("C", 0.6f));
            ok &= Check(hurt.Actions.Count == 0 && hurt.Holds.Count == 1 && hurt.Holds[0].Reason == RetreatPlanner.HoldOwnPlanetNotWiped,
                "my own colony, 60% projected loss: the garrison stays (RetreatHeld OwnPlanetNotWiped)");
            var wiped = Plan(f, null, Doomed("C", 1f));
            ok &= Check(wiped.Actions.Count == 1 && wiped.Actions[0].Target == "B", "my own colony, wiped out: the group retreats to B");
        }
        return ok;
    }
```
(The 3-planet line: in the byRival case player 0's only populated planets are A and B and both lie behind blockaded B on C's side, so no tier-1 destination remains and the assertion allows either "no action" or a different planet; if the Editor run shows tier 2/3 picking another planet that is fine as long as it is not B.)

- [ ] **Step 2: The check fails** — run the check; expected compile errors (`RetreatPlanner`). RED.

- [ ] **Step 3: Implement**

Create `Assets/Flatspace/GameAI/RetreatPlanner.cs`:
```csharp
using System;
using System.Collections.Generic;
using System.Linq;

namespace FlatSpace.AI
{
    /// <summary>
    /// Decides which of a player's groups leave a fight and where they go. For each of my FightProjection results: a
    /// deterministic gate (a war rival survives and I would lose at least retreatCheckFraction; at a planet I populate, only a
    /// wipe-out), the best tiered destination, then RetreatMatrix weighs Stay against Retreat. Tiers (first non-empty wins,
    /// cheapest path inside a tier): 1 a planet I populate; 2 a known empty planet, or one populated only by players I am not at
    /// war with; 3 a known planet of a war rival whose REMEMBERED blockade value is under my group's offense / (1 + margin)
    /// (it may be stale by arrival). Tiers 1 and 2 must hold no war-rival warship and not be blockaded by a war rival; the route
    /// avoids planets blockaded by a war rival (RoutePlanner). Pure (no Gameboard.Instance).
    /// </summary>
    public class RetreatPlanner
    {
        public const string HoldNoDestination = "NoDestination";
        public const string HoldOwnPlanetNotWiped = "OwnPlanetNotWiped";

        public class Retreat
        {
            public string Planet;
            public string Destination;
            public int Tier;
            public int Ships;
            public float LossFraction;
            public float RivalSurvivorsFraction;
            public float RouteCost;
            public float PRetreat;
            public float RememberedBlockade;   // tier 3 only
            public float MyOffense;            // tier 3 only
        }

        public class Stay
        {
            public string Planet;
            public float LossFraction;
            public float PRetreat;
        }

        public class Hold
        {
            public string Planet;
            public string Reason;
            public float LossFraction;
        }

        public class Plan
        {
            public List<ShipAction> Actions = new List<ShipAction>();
            public List<Retreat> Retreats = new List<Retreat>();
            public List<Stay> Stays = new List<Stay>();
            public List<Hold> Holds = new List<Hold>();
            public HashSet<string> Retreating = new HashSet<string>();
        }

        private class Destination
        {
            public string Planet;
            public int Tier;
            public float Cost;
        }

        private readonly GameAIMap _map;
        private readonly int _me;
        private readonly ISet<int> _warRivals;
        private readonly BlockadeView _view;       // what I remember and see (any blocker)
        private readonly BlockadeView _warView;    // only blockades by my war rivals
        private readonly WarshipStats _stats;
        private readonly GameAIConstants _constants;

        public RetreatPlanner(GameAIMap map, int playerId, ISet<int> warRivals, BlockadeView view, WarshipStats stats)
        {
            _map = map;
            _me = playerId;
            _warRivals = warRivals ?? new SortedSet<int>();
            _view = view ?? new BlockadeView();
            _warView = _view.OnlyFrom(_warRivals);
            _stats = stats;
            _constants = map.GameAIConstants;
        }

        public Plan Decide(IEnumerable<FightProjection> projections)
        {
            var plan = new Plan();
            var rows = new List<RetreatMatrix.Row>();
            var destinations = new Dictionary<string, Destination>();
            var byPlanet = new Dictionary<string, FightProjection>();
            foreach (var projection in projections.Where(p => p != null && p.Player == _me).OrderBy(p => p.Planet, StringComparer.Ordinal))
            {
                var planet = _map.GetPlanet(projection.Planet);
                if (planet == null) continue;
                var group = MyWarships(planet);
                if (group.Count == 0) continue;

                var own = planet.Owner == _me && planet.Population.Count > 0;
                var loss = projection.ProjectedLossFraction;
                var gated = projection.RivalSurvives && (own ? projection.MyGroupWiped : loss >= _constants.retreatCheckFraction);
                if (!gated)
                {
                    if (own && projection.RivalSurvives && loss >= _constants.retreatCheckFraction)
                        plan.Holds.Add(new Hold { Planet = projection.Planet, Reason = HoldOwnPlanetNotWiped, LossFraction = loss });
                    continue;
                }

                var offense = group.Sum(s => _stats.EffectiveOffense(s));
                var destination = BestDestination(planet, offense);
                if (destination == null)
                {
                    plan.Holds.Add(new Hold { Planet = projection.Planet, Reason = HoldNoDestination, LossFraction = loss });
                    continue;
                }
                destinations[projection.Planet] = destination;
                byPlanet[projection.Planet] = projection;
                rows.Add(new RetreatMatrix.Row
                {
                    Planet = projection.Planet, Ships = group.Count, LossFraction = loss, Destination = destination.Planet,
                    Tier = destination.Tier, PathCost = destination.Cost,
                });
            }

            foreach (var decision in RetreatMatrix.Decide(rows, _constants))
            {
                var projection = byPlanet[decision.Planet];
                if (!decision.Retreat)
                {
                    plan.Stays.Add(new Stay { Planet = decision.Planet, LossFraction = projection.ProjectedLossFraction, PRetreat = decision.PRetreat });
                    continue;
                }
                var destination = destinations[decision.Planet];
                plan.Actions.Add(decision.Action);
                plan.Retreating.Add(decision.Planet);
                var retreat = new Retreat
                {
                    Planet = decision.Planet, Destination = destination.Planet, Tier = destination.Tier, Ships = decision.Action.Count,
                    LossFraction = projection.ProjectedLossFraction, RivalSurvivorsFraction = projection.RivalSurvivorsFraction,
                    RouteCost = destination.Cost, PRetreat = decision.PRetreat,
                };
                if (destination.Tier == 3)
                {
                    retreat.RememberedBlockade = _view.Value(destination.Planet);
                    retreat.MyOffense = MyWarships(_map.GetPlanet(decision.Planet)).Sum(s => _stats.EffectiveOffense(s));
                }
                plan.Retreats.Add(retreat);
            }
            return plan;
        }

        private List<Ship> MyWarships(Planet planet)
            => planet.DockedShips.Where(s => s.Kind == Ship.ShipKind.WarShip && s.Owner == _me).ToList();

        private Destination BestDestination(Planet from, float groupOffense)
        {
            var known = _map.Knowledge.KnownPlanets(_me).OrderBy(n => n, StringComparer.Ordinal).ToList();
            var maxNodes = _constants.maxPathNodesForShipTransport;
            for (var tier = 1; tier <= 3; tier++)
            {
                Destination best = null;
                foreach (var name in known)
                {
                    if (name == from.PlanetName) continue;
                    var planet = _map.GetPlanet(name);
                    if (planet == null || TierOf(planet, groupOffense) != tier) continue;
                    var route = RoutePlanner.PlanRoute(_map, from.PlanetName, name, tier == 3 ? _warView.Without(name) : _warView, maxNodes);
                    if (route == null) continue;
                    if (best == null || route.Cost < best.Cost) best = new Destination { Planet = name, Tier = tier, Cost = route.Cost };
                }
                if (best != null) return best;
            }
            return null;
        }

        // 0 = not a destination.
        private int TierOf(Planet planet, float groupOffense)
        {
            var warRivalShips = planet.DockedShips.Any(s => s.Kind == Ship.ShipKind.WarShip && s.Owner != Planet.NoOwner && _warRivals.Contains(s.Owner));
            var blockadedByWar = _warView.IsBlockaded(planet.PlanetName);
            if (planet.Owner == _me && planet.Population.Count > 0) return !warRivalShips && !blockadedByWar ? 1 : 0;
            if (!planet.Population.Exists(p => _warRivals.Contains(p.Player))) return !warRivalShips && !blockadedByWar ? 2 : 0;
            return groupOffense > _view.Value(planet.PlanetName) * (1f + _constants.blockadeBreakMargin) ? 3 : 0;
        }
    }
}
```

- [ ] **Step 4: The check passes** — run the check; expected `[RetreatSelfCheck] ALL PASSED`. If the `byRival` case fails because every candidate is out and an action still appears, read the label: the test only forbids B.

- [ ] **Step 5: Commit**
```bash
git add Assets/Flatspace/GameAI/RetreatPlanner.cs Assets/Flatspace/GameAI/RetreatPlanner.cs.meta Assets/Editor/RetreatSelfCheck.cs
git commit -m "feat: RetreatPlanner, the gate and tiered blockade-aware destination

Co-Authored-By: Claude Sonnet 5.5 <noreply@anthropic.com>"
```

---

### Task 9: Wire retreat into `PlayerAI` first, and keep the other planners off a retreating group

**Files:**
- Modify: `Assets/Flatspace/GameAI/ShipTransportPlanner.cs` (new `Retreating`, skip in `BuildStates`)
- Modify: `Assets/Flatspace/GameAI/AssaultPlanner.cs` (new `ExcludedTargets`)
- Modify: `Assets/Flatspace/GameAI/PlayerAI.cs` (`ProcessResultsStrategyExpand`, `ProcessShipActions`, `PlanShipActions`, the cooldown, `PlanRetreats`)
- Modify: `Assets/Editor/RetreatSelfCheck.cs`

**Interfaces:**
- Consumes: `RetreatPlanner` (Task 8), `FightProjection` results (Task 5).
- Produces: `ShipTransportPlanner.Retreating` (`ICollection<string>`, default empty), `AssaultPlanner.ExcludedTargets` (`ISet<string>`, default null), `PlayerAI.ProcessShipActions(List<GameAIOrder> orders, List<Planet.UpdateResult> results = null)`, `PlayerAI.PlanShipActions(int turnNumber, List<Planet.UpdateResult> results = null)`, `PlayerAI.RetreatCooldownUntil(string planet)` (int, 0 when none).

- [ ] **Step 1: Write the failing check**

In `RetreatSelfCheck.RunChecks` add `ok &= RunPlannerExclusionCheck();` and `ok &= RunPlayerAIRetreatCheck();` and add:
```csharp
    // The other planners keep off a retreating group: no garrison state for it, never an assault target.
    public static bool RunPlannerExclusionCheck()
    {
        var ok = true;
        using (var f = CombatSelfCheck.Fixture.Line())
        {
            f.Colonize("A", 0);
            f.Ships("C", 0, 2);                                        // not colonized: a stranded, source-only state
            var planner = new ShipTransportPlanner(f.Map, 0);
            ok &= Check(planner.BuildStates().Any(s => s.Planet.PlanetName == "C"), "without a retreat C (my ships on an uncolonized planet) has a state");
            planner = new ShipTransportPlanner(f.Map, 0) { Retreating = new List<string> { "C" } };
            ok &= Check(!planner.BuildStates().Any(s => s.Planet.PlanetName == "C"), "a retreating planet has no state: neither a source nor a sink");
        }
        using (var f = CombatSelfCheck.Fixture.Line())
        {
            f.Ships("A", 0, 1);
            var view = BlockadeView.Of("C");
            var assault = new AssaultPlanner(f.Map, 0, view, f.Stats);
            ok &= Check(assault.ChooseBlockadeTarget(out _)?.PlanetName == "C", "without an exclusion the blockaded planet C is the target");
            assault = new AssaultPlanner(f.Map, 0, view, f.Stats) { ExcludedTargets = new HashSet<string> { "C" } };
            ok &= Check(assault.ChooseBlockadeTarget(out _) == null, "an excluded planet is not a blockade target");
        }
        return ok;
    }

    private static PlayerAI MakeAI(CombatSelfCheck.Fixture f, int id, List<GameObject> gos)
    {
        var go = new GameObject("RetreatPlayer" + id);
        gos.Add(go);
        var player = go.AddComponent<Player>();
        var ai = go.AddComponent<PlayerAI>();
        ai.Player = player;
        ai.AIMap = f.Map;
        player.playerID = id;
        ai.ResearchCatalog = go.AddComponent<Catalog>();
        ai.ResearchCatalog.catalogItems = f.Research;
        ai.Strategy = PlayerAI.AIStrategy.AIStrategyConsolidate;
        return ai;
    }

    // End to end through PlayerAI: the projection result in, a retreat ship action out first, a cooldown set; legacy mode untouched.
    public static bool RunPlayerAIRetreatCheck()
    {
        var ok = true;
        var gos = new List<GameObject>();
        try
        {
            using (var f = CombatSelfCheck.Fixture.Line())
            {
                f.Constants.retreatSteepness = 0.001f;
                f.Constants.maxPathNodesForKnowledge = 8;
                f.Constants.maxPathNodesForShipTransport = 10;
                f.Colonize("A", 0); f.Colonize("B", 0); f.Colonize("D", 1);
                f.Ships("C", 0, 1); f.Ships("C", 1, 3); f.War(0, 1);
                f.Map.Knowledge.Update(f.Map, 2, 8);
                var ai = MakeAI(f, 0, gos);
                var results = new List<Planet.UpdateResult>();
                GameAI.AppendFightProjections(f.Map, f.Stats, f.Constants, new List<GameAI.GameAIOrder>(), results);
                GameAI.Rand = new System.Random(11);
                var actions = ai.PlanShipActions(5, results);
                ok &= Check(actions.Count > 0 && actions[0].Origin == "C" && actions[0].Target == "B" && actions[0].Count == 1,
                    "the retreat action comes first: C to B, the one ship");
                ok &= Check(ai.RetreatCooldownUntil("C") == 5 + f.Constants.retreatCooldownTurns, "C is off the assault list for retreatCooldownTurns");
                ok &= Check(actions.Count(a => a.Origin == "C") == 1, "no other planner sends the retreating ship anywhere else");

                var orders = new List<GameAI.GameAIOrder>();
                GameAI.Rand = new System.Random(11);
                ai.ProcessShipActions(orders, results);
                ok &= Check(orders.Any(o => o.Type == GameAI.GameAIOrder.OrderType.OrderTypeShipTransport && o.Origin == "C" && o.Target == "B"),
                    "ProcessShipActions turns it into the ordinary ship-transport order trio (no new order type)");

                f.Map.Diplomacy.Enabled = false;
                var legacy = new List<Planet.UpdateResult>();
                GameAI.AppendFightProjections(f.Map, f.Stats, f.Constants, new List<GameAI.GameAIOrder>(), legacy);
                ok &= Check(legacy.Count == 0, "legacy mode: no projections, so there is nothing to retreat from");
            }
        }
        finally { foreach (var go in gos) Object.DestroyImmediate(go); }
        return ok;
    }
```
- [ ] **Step 2: The check fails** — run the check; expected compile errors (`Retreating`, `ExcludedTargets`, the new `PlanShipActions`/`ProcessShipActions` overloads, `RetreatCooldownUntil`). RED.

- [ ] **Step 3: Implement the planner hooks**

`ShipTransportPlanner.cs`, after the `HeldPlanets` property (line 35):
```csharp

        /// <summary>
        /// Planets whose docked warships are retreating this turn (RetreatPlanner): no state at all, so those ships are neither
        /// a source of this plan nor a sink for it (the retreat has already claimed them).
        /// </summary>
        public ICollection<string> Retreating { get; set; } = new List<string>();

        private bool IsRetreating(string planetName) => Retreating != null && Retreating.Contains(planetName);
```
In `BuildStates`, in the `foreach (var planet in colonized)` loop add as its first statement `if (IsRetreating(planet.PlanetName)) continue;`, and extend the `stranded` filter to `.Where(p => !IsColonized(p) && !IsHeld(p.PlanetName) && !IsRetreating(p.PlanetName) && CountWarships(p) > 0)`.

`AssaultPlanner.cs`, after the constructor add:
```csharp

            /// <summary>
            /// Planets that may not be chosen as a target this turn: a planet I just retreated from (the cooldown) or am retreating
            /// from now, so the assault does not send the same ships straight back. Null = none.
            /// </summary>
            public ISet<string> ExcludedTargets { get; set; }

            private bool IsExcluded(string planetName) => ExcludedTargets != null && ExcludedTargets.Contains(planetName);
```
In `ChooseBlockadeTarget`, directly after `var planet = _map.GetPlanet(name); if (planet == null) continue;` add `if (IsExcluded(name)) continue;`; in `ChooseEnemyTarget`, extend its guard line to `if (planet == null || IsExcluded(name) || !IsEnemyOccupied(planet) || IsHeldByMe(planet)) continue;`.

- [ ] **Step 4: Implement the `PlayerAI` wiring**

In `PlayerAI.cs` fields (next to `_lastLoggedAssaultTarget`, line ~1766) add:
```csharp
            // A planet I retreated from -> the first turn it may be an assault target again (player-private, not saved: a load forgets it).
            private readonly Dictionary<string, int> _retreatCooldown = new Dictionary<string, int>();

            /// <summary>The first turn a planet I retreated from may be an assault target again; 0 when there is no cooldown. Public for the self-check.</summary>
            public int RetreatCooldownUntil(string planet) => _retreatCooldown.TryGetValue(planet, out var until) ? until : 0;
```
Change `ProcessResultsStrategyExpand`'s last line to `ProcessShipActions(orders, results);`. Replace `ProcessShipActions` and `PlanShipActions` (lines 1750-1811) with:
```csharp
            public void ProcessShipActions(List<GameAI.GameAIOrder> orders, List<Planet.UpdateResult> results = null)
            {
                // Self-checks call this with no Gameboard in the scene.
                var turn = Gameboard.Instance != null ? Gameboard.Instance.TurnNumber : 0;

                // Nothing is undocked until the orders execute, so a second fleet leaving the same
                // origin this turn (home defence + assault) must skip the ships the first one takes.
                var claimedByOrigin = new Dictionary<string, int>();
                foreach (var action in PlanShipActions(turn, results))
                {
                    claimedByOrigin.TryGetValue(action.Origin, out var claimed);
                    EmitShipOrders(action, orders, claimed);
                    claimedByOrigin[action.Origin] = claimed + action.Count;
                }
            }
```
and `PlanShipActions`:
```csharp
            public List<ShipAction> PlanShipActions(int turnNumber, List<Planet.UpdateResult> results = null)
            {
                // Retreats first: a group leaving a lost fight is claimed before any other planner can count on it.
                var retreat = PlanRetreats(turnNumber, results);
                var actions = new List<ShipAction>(retreat.Actions);

                if (!IsConsolidateLike(Strategy))
                {
                    actions.AddRange(new ShipTransportPlanner(AIMap, Player.playerID) { Retreating = retreat.Retreating }.Plan());
                    return actions;
                }

                var stats = new WarshipStats(ResearchCatalog != null ? ResearchCatalog.catalogItems : null);
                var excluded = new HashSet<string>(_retreatCooldown.Where(kv => kv.Value > turnNumber).Select(kv => kv.Key));
                excluded.UnionWith(retreat.Retreating);
                var assault = new AssaultPlanner(AIMap, Player.playerID, _blockadeView, stats, _blockadeMemory, turnNumber,
                    AssaultWarFilter()) { ExcludedTargets = excluded };
                var blockadeTarget = assault.ChooseBlockadeTarget(out var blockadeReason);
                var target = blockadeTarget ?? assault.ChooseEnemyTarget();

                var targetName = target?.PlanetName;
                LogBlockadeTargetChanges(turnNumber, assault, blockadeTarget, blockadeReason);
                foreach (var skip in _blockadeSkips.Update(assault.LastSkipped))
                    AITuningLogger.LogBlockadeSkipped(turnNumber, Player.playerID, skip.Planet, skip.Reason, skip.Value,
                        skip.Winner, skip.WinnerCommitted);
                if (blockadeTarget == null && targetName != null && targetName != _lastLoggedAssaultTarget)
                    AITuningLogger.LogAssaultTarget(turnNumber, Player.playerID, targetName, assault.RequiredForce());
                _lastLoggedAssaultTarget = blockadeTarget == null ? targetName : null;

                var transport = new ShipTransportPlanner(AIMap, Player.playerID, Strategy)
                {
                    HeldPlanet  = targetName,
                    HeldPlanets = assault.ContestedHolds().Where(p => !retreat.Retreating.Contains(p)).ToList(),
                    Retreating  = retreat.Retreating,
                };
                var homeActions = transport.Plan();
                actions.AddRange(homeActions);
                actions.AddRange(assault.Plan(target, transport.LastStates, homeActions));

                var force = assault.LastBlockadeForce;
                if (blockadeTarget != null && force.Ships > 0)
                    AITuningLogger.LogBlockadeForce(turnNumber, Player.playerID, blockadeTarget.PlanetName,
                        force.Ships, force.Offense, force.StillNeeded);
                return actions;
            }

            /// <summary>
            /// Reads this player's FightProjection results and lets RetreatPlanner decide which groups leave and where; sets the
            /// assault cooldown of each planet left. Empty when no results are given (a self-check calling the planners
            /// directly) or diplomacy is off (no combat, no projections).
            /// </summary>
            private RetreatPlanner.Plan PlanRetreats(int turnNumber, List<Planet.UpdateResult> results)
            {
                if (results == null || !AIMap.Diplomacy.Enabled) return new RetreatPlanner.Plan();
                var mine = results
                    .Where(r => r.Result == Planet.UpdateResult.UpdateResultType.UpdateResultTypeFightProjection
                                && r.PlayerID == Player.playerID && r.Data is FightProjection)
                    .Select(r => (FightProjection)r.Data).ToList();
                var stats = new WarshipStats(ResearchCatalog != null ? ResearchCatalog.catalogItems : null);
                var plan = new RetreatPlanner(AIMap, Player.playerID, WarRivals(), _blockadeView, stats).Decide(mine);
                foreach (var retreat in plan.Retreats)
                    _retreatCooldown[retreat.Planet] = turnNumber + AIMap.GameAIConstants.retreatCooldownTurns;
                return plan;
            }
```
Note: in the original code `actions = transport.Plan()` was a single list that `assault.Plan(target, transport.LastStates, actions)` appended to and returned; the rewrite keeps `homeActions` separate so the assault still receives exactly the garrison plan's actions as before, and returns the retreat actions first.

- [ ] **Step 5: Existing callers**

`ShipTransportSelfCheck`, `BlockadeBreakSelfCheck` call `playerAI.PlanShipActions(0)` / `s.AI.PlanShipActions(5)` / `ProcessShipActions(orders)`; the new optional parameter keeps them compiling and (no results, so no retreat) behaving exactly as before.

- [ ] **Step 6: The check passes** — run the check; expected `[RetreatSelfCheck] ALL PASSED` and `Run All AI Self-Checks` 14 passed (`ShipTransportSelfCheck` and `BlockadeBreakSelfCheck` unchanged).

- [ ] **Step 7: Commit**
```bash
git add Assets/Flatspace/GameAI/ShipTransportPlanner.cs Assets/Flatspace/GameAI/AssaultPlanner.cs Assets/Flatspace/GameAI/PlayerAI.cs Assets/Editor/RetreatSelfCheck.cs
git commit -m "feat: retreat decides first in PlanShipActions; other planners keep off a retreating group

Co-Authored-By: Claude Sonnet 5.5 <noreply@anthropic.com>"
```

---

### Task 10: Tuning-log lines (`Retreat`, `RetreatStay`, `RetreatHeld`, `RetreatArrive`)

**Files:**
- Create: `Assets/Flatspace/GameAI/RetreatTracker.cs`
- Modify: `Assets/Flatspace/Diagnostics/AITuningLogger.cs`
- Modify: `Assets/Flatspace/GameAI/PlayerAI.cs` (`PlanRetreats`, pending arrivals, the tracker)
- Modify: `Assets/Editor/RetreatSelfCheck.cs`

**Interfaces:**
- Produces: `RetreatTracker` (pure, log-only): `public struct Entry { string Planet; string Kind; string Reason; float LossFraction; float PRetreat; }`, `public List<Entry> Update(IEnumerable<Entry> current)` (returns the entries new or whose Kind/Reason changed since the last call, ordered by planet name, and remembers `current`), `public void Clear()`; constants `RetreatTracker.KindStay = "Stay"`, `KindHeld = "Held"`. `AITuningLogger.LogRetreat`, `LogRetreatStay`, `LogRetreatHeld`, `LogRetreatArrive`.

- [ ] **Step 1: Write the failing check**

In `RetreatSelfCheck.RunChecks` add `ok &= RunRetreatTrackerCheck();` and add:
```csharp
    public static bool RunRetreatTrackerCheck()
    {
        var ok = true;
        var tracker = new RetreatTracker();
        RetreatTracker.Entry Stay(string planet, float loss = 0.4f)
            => new RetreatTracker.Entry { Planet = planet, Kind = RetreatTracker.KindStay, Reason = "", LossFraction = loss, PRetreat = 0.2f };
        RetreatTracker.Entry Held(string planet, string reason)
            => new RetreatTracker.Entry { Planet = planet, Kind = RetreatTracker.KindHeld, Reason = reason, LossFraction = 0.9f, PRetreat = 0f };

        var first = tracker.Update(new[] { Stay("X"), Held("Y", RetreatPlanner.HoldNoDestination) });
        ok &= Check(first.Select(e => e.Planet).SequenceEqual(new[] { "X", "Y" }), "the first sighting of each planet is reported, ordered by name");
        ok &= Check(tracker.Update(new[] { Stay("X", 0.6f), Held("Y", RetreatPlanner.HoldNoDestination) }).Count == 0,
            "the same kind and reason again (even with a different loss) is not reported: a standoff does not log every turn");
        var changed = tracker.Update(new[] { Stay("X"), Held("Y", RetreatPlanner.HoldOwnPlanetNotWiped) });
        ok &= Check(changed.Count == 1 && changed[0].Planet == "Y", "a changed reason is reported");
        tracker.Update(new RetreatTracker.Entry[0]);
        ok &= Check(tracker.Update(new[] { Stay("X") }).Count == 1, "a planet that left the gate is forgotten, so a later one is reported afresh");
        tracker.Clear();
        ok &= Check(tracker.Update(new[] { Stay("X") }).Count == 1, "Clear forgets everything");
        return ok;
    }
```

- [ ] **Step 2: The check fails** — run the check; expected compile errors (`RetreatTracker`). RED.

- [ ] **Step 3: Implement the tracker**

Create `Assets/Flatspace/GameAI/RetreatTracker.cs`:
```csharp
using System.Collections.Generic;
using System.Linq;

namespace FlatSpace.AI
{
    /// <summary>
    /// Turns each turn's "gated fights that stayed, and why" into reports on change only, so the tuning log can show how often
    /// the roll keeps a group in a bad fight (RetreatStay) or no destination exists (RetreatHeld) without a line per planet per
    /// turn. A planet is reported the first turn it appears and again when its kind or reason changes; its state clears when it
    /// leaves the list (it retreated or left the gate). Pure, log-only, never saved (a load reports each current one once more).
    /// </summary>
    public class RetreatTracker
    {
        public const string KindStay = "Stay";
        public const string KindHeld = "Held";

        public struct Entry
        {
            public string Planet;
            public string Kind;        // KindStay or KindHeld
            public string Reason;      // NoDestination / OwnPlanetNotWiped for Held, empty for Stay
            public float LossFraction;
            public float PRetreat;
        }

        private readonly Dictionary<string, (string kind, string reason)> _state = new Dictionary<string, (string kind, string reason)>();

        public void Clear() => _state.Clear();

        public List<Entry> Update(IEnumerable<Entry> current)
        {
            var now = current.ToList();
            var changed = now
                .Where(e => !_state.TryGetValue(e.Planet, out var last) || last.kind != e.Kind || last.reason != e.Reason)
                .OrderBy(e => e.Planet, System.StringComparer.Ordinal)
                .ToList();
            _state.Clear();
            foreach (var e in now) _state[e.Planet] = (e.Kind, e.Reason);
            return changed;
        }
    }
}
```

- [ ] **Step 4: Implement the logger methods**

In `AITuningLogger.cs` after `LogSurrender` add (same style as its neighbours: `_currentLogPath == null` guard, `ci`, `FormatLine`, `AppendLines`):
```csharp

    /// <summary>A group left a lost fight: T&lt;turn&gt;|P&lt;id&gt;|Retreat|planet|destination|tier|ships|projectedLossPct|rivalSurvivorsPct|routeCost|cooldownUntil|pRetreat (tier 3 appends |rememberedBlockade|myOffense).</summary>
    public static void LogRetreat(int turnNumber, int playerId, string planet, string destination, int tier, int ships,
        float lossFraction, float rivalSurvivorsFraction, float routeCost, int cooldownUntil, float pRetreat,
        float rememberedBlockade, float myOffense)
    {
        if (_currentLogPath == null) return;
        var ci = System.Globalization.CultureInfo.InvariantCulture;
        var fields = new List<string>
        {
            planet, destination, tier.ToString(ci), ships.ToString(ci), (100f * lossFraction).ToString("0", ci),
            (100f * rivalSurvivorsFraction).ToString("0", ci), routeCost.ToString("0", ci), cooldownUntil.ToString(ci),
            pRetreat.ToString("0.###", ci),
        };
        if (tier == 3)
        {
            fields.Add(rememberedBlockade.ToString("0.#", ci));
            fields.Add(myOffense.ToString("0.#", ci));
        }
        AppendLines(new List<string> { FormatLine(turnNumber, playerId, "Retreat", fields.ToArray()) });
    }

    /// <summary>A gated fight that rolled Stay (on change only): T&lt;turn&gt;|P&lt;id&gt;|RetreatStay|planet|projectedLossPct|pRetreat.</summary>
    public static void LogRetreatStay(int turnNumber, int playerId, string planet, float lossFraction, float pRetreat)
    {
        if (_currentLogPath == null) return;
        var ci = System.Globalization.CultureInfo.InvariantCulture;
        AppendLines(new List<string> { FormatLine(turnNumber, playerId, "RetreatStay", planet,
            (100f * lossFraction).ToString("0", ci), pRetreat.ToString("0.###", ci)) });
    }

    /// <summary>A fight the projection says to leave where the ships stay anyway (on change only): T&lt;turn&gt;|P&lt;id&gt;|RetreatHeld|planet|NoDestination or OwnPlanetNotWiped|projectedLossPct.</summary>
    public static void LogRetreatHeld(int turnNumber, int playerId, string planet, string reason, float lossFraction)
    {
        if (_currentLogPath == null) return;
        var ci = System.Globalization.CultureInfo.InvariantCulture;
        AppendLines(new List<string> { FormatLine(turnNumber, playerId, "RetreatHeld", planet, reason,
            (100f * lossFraction).ToString("0", ci)) });
    }

    /// <summary>A tier 3 retreat landed: the blockade value it was planned on against the one found there: T&lt;turn&gt;|P&lt;id&gt;|RetreatArrive|planet|rememberedBlockade|actualBlockade.</summary>
    public static void LogRetreatArrive(int turnNumber, int playerId, string planet, float remembered, float actual)
    {
        if (_currentLogPath == null) return;
        var ci = System.Globalization.CultureInfo.InvariantCulture;
        AppendLines(new List<string> { FormatLine(turnNumber, playerId, "RetreatArrive", planet,
            remembered.ToString("0.#", ci), actual.ToString("0.#", ci)) });
    }
```
(If `FormatLine` takes `params string[]`, the `fields.ToArray()` call matches; the neighbouring `LogCombat` call passes its fields as separate arguments, so `FormatLine` is `params`-style.)

- [ ] **Step 5: Log from `PlayerAI.PlanRetreats`**

Add fields beside `_retreatCooldown`:
```csharp
            private readonly RetreatTracker _retreatTracker = new RetreatTracker();
            // Tier 3 retreats still travelling: (planet, the turn they land, the blockade value they were planned on). Log-only, not saved.
            private readonly List<(string planet, int landTurn, float remembered)> _retreatPending = new List<(string planet, int landTurn, float remembered)>();
```
Replace the body of `PlanRetreats` after the `if (results == null ...)` guard with:
```csharp
                LogRetreatArrivals(turnNumber);
                var mine = results
                    .Where(r => r.Result == Planet.UpdateResult.UpdateResultType.UpdateResultTypeFightProjection
                                && r.PlayerID == Player.playerID && r.Data is FightProjection)
                    .Select(r => (FightProjection)r.Data).ToList();
                var stats = new WarshipStats(ResearchCatalog != null ? ResearchCatalog.catalogItems : null);
                var plan = new RetreatPlanner(AIMap, Player.playerID, WarRivals(), _blockadeView, stats).Decide(mine);
                var constants = AIMap.GameAIConstants;
                foreach (var retreat in plan.Retreats)
                {
                    var cooldownUntil = turnNumber + constants.retreatCooldownTurns;
                    _retreatCooldown[retreat.Planet] = cooldownUntil;
                    AITuningLogger.LogRetreat(turnNumber, Player.playerID, retreat.Planet, retreat.Destination, retreat.Tier, retreat.Ships,
                        retreat.LossFraction, retreat.RivalSurvivorsFraction, retreat.RouteCost, cooldownUntil, retreat.PRetreat,
                        retreat.RememberedBlockade, retreat.MyOffense);
                    if (retreat.Tier == 3)
                    {
                        var delay = Math.Max(1, Convert.ToInt32(retreat.RouteCost / constants.defaultTravelSpeed));
                        _retreatPending.Add((retreat.Destination, turnNumber + delay, retreat.RememberedBlockade));
                    }
                }
                var standing = plan.Stays.Select(s => new RetreatTracker.Entry
                    {
                        Planet = s.Planet, Kind = RetreatTracker.KindStay, Reason = string.Empty, LossFraction = s.LossFraction, PRetreat = s.PRetreat,
                    })
                    .Concat(plan.Holds.Select(h => new RetreatTracker.Entry
                    {
                        Planet = h.Planet, Kind = RetreatTracker.KindHeld, Reason = h.Reason, LossFraction = h.LossFraction, PRetreat = 0f,
                    }));
                foreach (var entry in _retreatTracker.Update(standing))
                {
                    if (entry.Kind == RetreatTracker.KindStay)
                        AITuningLogger.LogRetreatStay(turnNumber, Player.playerID, entry.Planet, entry.LossFraction, entry.PRetreat);
                    else
                        AITuningLogger.LogRetreatHeld(turnNumber, Player.playerID, entry.Planet, entry.Reason, entry.LossFraction);
                }
                return plan;
            }

            // A tier 3 retreat that has landed: log the blockade value it was planned on against the one found there now (the
            // view is rebuilt each turn, and my ships docked there make the planet visible).
            private void LogRetreatArrivals(int turnNumber)
            {
                foreach (var pending in _retreatPending.Where(p => p.landTurn <= turnNumber).ToList())
                {
                    var actual = _blockadeView != null ? _blockadeView.Value(pending.planet) : 0f;
                    AITuningLogger.LogRetreatArrive(turnNumber, Player.playerID, pending.planet, pending.remembered, actual);
                    _retreatPending.Remove(pending);
                }
            }
```
(`Math` and `Convert` need `using System;`, which `PlayerAI.cs` already has: `EmitShipOrders` uses both.) The `RetreatTracker` and `_retreatPending` are per-`PlayerAI`, so each player has its own.

- [ ] **Step 6: The check passes** — run the check; expected `[RetreatSelfCheck] ALL PASSED`, `Run All AI Self-Checks` 14 passed. The log lines themselves are verified in the Play-mode run (Task 12): `AITuningLogger` has no self-check by design.

- [ ] **Step 7: Commit**
```bash
git add Assets/Flatspace/GameAI/RetreatTracker.cs Assets/Flatspace/GameAI/RetreatTracker.cs.meta Assets/Flatspace/Diagnostics/AITuningLogger.cs Assets/Flatspace/GameAI/PlayerAI.cs Assets/Editor/RetreatSelfCheck.cs
git commit -m "feat: Retreat, RetreatStay, RetreatHeld and RetreatArrive tuning-log lines

Co-Authored-By: Claude Sonnet 5.5 <noreply@anthropic.com>"
```

---

### Task 11: Simultaneity check for the retreat roll, and the docs

**Files:**
- Modify: `Assets/Editor/SimultaneitySelfCheck.cs`
- Modify: `CLAUDE.md`, `.claude/skills/tuning-log/SKILL.md`, `FUTURE_FEATURES.md`
- Modify: the memory note `surrender-terms-revisit-with-diplomacy` and `MEMORY.md` (at `C:\Users\clapt\.claude\projects\C--Projects-FlatSpace\memory\`)

- [ ] **Step 1: Write the failing check**

In `SimultaneitySelfCheck.RunChecks` add `ok &= RunRetreatOrderIndependenceCheck();`. Extend `RunRound(bool reversed, bool withLosses = false)` to `RunRound(bool reversed, bool withLosses = false, bool withRetreat = false)` and, inside, after the `withLosses` block (before the `// Only 0 -> 1:` comment), add:
```csharp
            if (withRetreat)
            {
                // Player 0 has one warship at D (player 1's planet) against player 1's three: a lost fight for player 0, a won one
                // for player 1. Both are at war; the projection results are built from the same state for every player, so the
                // retreat decision (a roulette made deterministic below) must not depend on which player runs first.
                map.Diplomacy.SetStance(0, 1, Stance.War, 0);
                map.Diplomacy.SetStance(1, 0, Stance.War, 0);
                WarshipSelfCheck.DockWarships(map.GetPlanet("D"), 0, 1);
                WarshipSelfCheck.DockWarships(map.GetPlanet("D"), 1, 3);
                constants.retreatSteepness = 0.001f;
                var retreatStats = new WarshipStats(research);
                map.Knowledge.Update(map, 3, 8);        // player 0's new ship at D makes D known to it
                GameAI.AppendFightProjections(map, retreatStats, constants, new List<GameAI.GameAIOrder>(), results);
            }
```
and add the check method:
```csharp
    // The retreat roll draws from the shared random stream and reads one projection per player: it must give each player the same
    // orders whatever the player order.
    public static bool RunRetreatOrderIndependenceCheck()
    {
        var ok = true;
        var forward = RunRound(reversed: false, withRetreat: true);
        var reverse = RunRound(reversed: true, withRetreat: true);
        ok &= Check(forward[0].Orders.Any(s => s.StartsWith("OrderTypeShipTransport|0|D|")) && reverse[0].Orders.Any(s => s.StartsWith("OrderTypeShipTransport|0|D|")),
            "precondition: player 0 retreats its lone ship from D in both player orders");
        for (var p = 0; p < 3; ++p)
        {
            ok &= Check(forward[p].Orders.SequenceEqual(reverse[p].Orders),
                $"with a retreat, player {p}: the same orders whatever the player order\n  forward: {string.Join("; ", forward[p].Orders)}\n  reverse: {string.Join("; ", reverse[p].Orders)}");
            ok &= Check(forward[p].Stances == reverse[p].Stances, $"with a retreat, player {p}: the same stances");
        }
        return ok;
    }
```
(The existing `Outcome.Orders` key is `Type|PlayerId|Origin|Target|Data`, so the retreat order is `OrderTypeShipTransport|0|D|<dest>|1`.)

- [ ] **Step 2: Run the check**

Run the check. Expected: all suites pass. If the precondition fails (player 0 does not retreat), the destination search found nothing: the players' planets A..F are populated and known via `map.Knowledge.Update(map, 3, 8)` in `RunRound`, so a tier 1 destination (B or A) exists; the retreat order from D to C-or-B is the one asserted. If the equality fails, a draw from `GameAI.Rand` is order-dependent: the per-player `GameAI.Rand = new System.Random(500 + p)` re-seed in `RunRound` must happen before each player's `ProcessResults` (it does).

- [ ] **Step 3: Docs**

`CLAUDE.md`:
- "Ship combat" bullet **Hostility:** replace "`LossShareToward` = lost strength / (my current strength + lost strength) over the window." with "`LossShareToward` = strength lost to the rival / my strength ENGAGED against it, both totalled over the window (`CombatLoss.Engaged`: the victim's strength at the fight planet at the start of each fight turn, split among attackers like the loss; `CombatSystem` appends a `WarshipsLost` result on every fight turn, with 0 lost when nothing died); 0 when nothing was engaged. It replaced a share of the whole fleet that reached only 0.02-0.14. `significantLossFraction`, `surrenderMidpoint` and `surrenderSteepness` were tuned to that old range and are recalibrated from the first logs on the new measure."
- "Ship combat" **Out of scope** bullet: remove "retreat and reinforcement tactics" and add a new bullet before it:
  "**Retreat:** after combat each turn `GameAI.AppendFightProjections` appends an `UpdateResultTypeFightProjection` result per (planet, player) in a war fight (`FightProjector`, pure: the fight played to its end with `CombatSystem.Round`, the same core real combat uses, over every docked warship plus my own in-flight ships landing there, at most `retreatProjectionTurns` rounds; rival in-flight ships and repair are not modeled). `PlayerAI.PlanShipActions` runs `RetreatPlanner` FIRST: a deterministic gate (a war rival survives and I would lose at least `retreatCheckFraction` of my strength, a wounded survivor counting as partly lost; at a planet I populate only a wipe-out), the best destination by tier (1 a planet I populate, 2 a known empty planet or one populated only by players I am not at war with, 3 a war rival's planet whose REMEMBERED blockade value is under my group's offense / (1 + `blockadeBreakMargin`); tiers 1 and 2 hold no war-rival warship and are not blockaded by a war rival, the route avoids war-rival blockades through `RoutePlanner`; `BlockadeView.OnlyFrom`), then `RetreatMatrix` (stance-matrix pattern, `IndependentRows`, a roulette) weighs Stay against Retreat with `1 / (1 + e^(-(loss - retreatLossFraction) / retreatSteepness))`. A retreat is the ordinary ship-transport order trio (no new order type). A retreating planet's ships are removed from the garrison planner (`ShipTransportPlanner.Retreating`) and the planet is off the assault target list for `retreatCooldownTurns` (`AssaultPlanner.ExcludedTargets`; player-private, not saved). Tunables on `GameAIConstants`: `retreatCheckFraction` 0.3, `retreatLossFraction` 0.5, `retreatSteepness` 0.1, `retreatProjectionTurns` 20, `retreatCooldownTurns` 10. Self-check: `Assets/Editor/RetreatSelfCheck.cs`."
- "Tunables" bullet and the list of self-checks at the top: add `FlatSpace -> AI -> Run Retreat Self-Check` (the projection, the matrix weights, the planner tiers, the planner hooks, the tracker) and change "runs every AI suite: ..." to include Retreat (14 suites).
- "AI Tuning Log": add `Retreat|<planet>|<destination>|<tier>|<ships>|<projectedLossPct>|<rivalSurvivorsPct>|<routeCost>|<cooldownUntil>|<pRetreat>` (tier 3 appends `|<rememberedBlockade>|<myOffense>`; once per retreat order), `RetreatStay|<planet>|<projectedLossPct>|<pRetreat>` and `RetreatHeld|<planet>|<NoDestination or OwnPlanetNotWiped>|<projectedLossPct>` (on change only, through `RetreatTracker`, log-only state), `RetreatArrive|<planet>|<rememberedBlockade>|<actualBlockade>` (tier 3 only, from a log-only pending list), and the `Hostility` line's new last field `|<engagedStrength>`.

`.claude/skills/tuning-log/SKILL.md`: add a "Retreat" bullet in the same shape as its other combat bullets: count `Retreat` lines per run and per tier, the share of `RetreatStay` that ended in a lost fight (a `Combat` line with ships destroyed on that planet within 3 turns), `RetreatHeld NoDestination` counts, `RetreatArrive` remembered vs actual (how stale tier 3 values are), ships preserved (compare 25-turn `FleetHealth` and total warships against the baselines in the handoff), and note the `Hostility` line's `engagedStrength` field (guard `NF >= 10`) and that `lossShare` now divides by it.

`FUTURE_FEATURES.md`: in the "(2) Ship to ship combat ... Still open" entry, replace the retreat/loss-share parts ("revisit the measure ... together with retreat and defense logic" and "retreat and reinforcement logic ...") with: "retreat and the engaged-force loss share were built 2026-10-06 (spec `docs/superpowers/specs/2026-10-06-retreat-and-engaged-loss-share-design.md`, plan `docs/superpowers/plans/2026-10-06-retreat-and-engaged-loss-share.md`), pending a Play-mode tuning pass: recalibrate `significantLossFraction` / `surrenderMidpoint` / `surrenderSteepness` to the new share range, and tune the `retreat*` tunables from the `Retreat` lines; still open: per-ship rout, retreat of colony ships, rival in-flight ships in the projection, retreat from my own colonies except on a wipe-out (belongs with planet defense, element 4)." Move the entry to `completed_features.md` only after the tuning pass, with `/update_feature_list`.

Memory: update `surrender-terms-revisit-with-diplomacy.md` to say the measure was changed to the engaged force on 2026-10-06 and the constants must now be recalibrated from the first logs (the old values 0.08 / 0.15 / 0.015 were tuned to the old measure); keep its `MEMORY.md` pointer line consistent.

- [ ] **Step 4: The check passes**

Run the check. Expected: `Run All AI Self-Checks` reports `ALL 14 SUITES PASSED`.

- [ ] **Step 5: Commit**
```bash
git add Assets/Editor/SimultaneitySelfCheck.cs CLAUDE.md .claude/skills/tuning-log/SKILL.md FUTURE_FEATURES.md
git commit -m "docs: retreat and the engaged loss share in CLAUDE.md, the tuning-log skill and FUTURE_FEATURES; simultaneity check for the retreat roll

Co-Authored-By: Claude Sonnet 5.5 <noreply@anthropic.com>"
```

---

### Task 12: Play-mode verification (the user runs it; analysis with `/tuning-log`)

The self-checks cannot run Unity systems or the log, so behavior is verified in a real match. This task produces no code unless the run shows a defect.

- [ ] **Step 1: Confirm the build** — `git log -1` shows the Task 11 commit on the working branch; the Editor Console has no errors; `Run All AI Self-Checks` reports `ALL 14 SUITES PASSED`.
- [ ] **Step 2: Runs** — the user turns `_logAIEvents` on (Main Menu) and plays several matches to T399 on `test2.json` and on `4p.json` (the baselines in the handoff are 6 and 3-6 runs each).
- [ ] **Step 3: Analyse** — run `/tuning-log` on the newest logs and check, per board: `Retreat` lines exist, with a mix of tiers 1-2 (tier 3 rare); `RetreatStay` and `RetreatHeld` are on-change lines (no per-turn flood: roughly the count of retreat decisions, not turns); `RetreatArrive` remembered vs actual; `Hostility` lines carry the 10th field and `lossShare` is now in the range surrender can reach (note it, do not retune); economy (planets at T375, arrivals), war counts and stance flips are inside the 2026-10-05 baseline ranges; fleet-cap violations 0; ships destroyed per run fall against the 174-560 baseline; no `ColonistRedirect`/blockade regression; no exception in the Console.
- [ ] **Step 3b: Failure checks** — a group that retreated and the same planet chosen as an assault target within `retreatCooldownTurns` (a `BlockadeTarget` or `AssaultTarget` line for it) is a bug in the cooldown; a `Retreat` line whose destination holds a war-rival ship is a bug in the tiers; a Console exception from `FightProjector` or `RetreatMatrix` is a bug to fix with `superpowers:systematic-debugging`.
- [ ] **Step 4: Record** — write the findings and any recalibration of the surrender / loss-drop constants as a follow-up for the user to decide (the constants are NOT changed in this plan); report to the user. Nothing to commit unless a defect is fixed.

---

## Spec Coverage

| Spec section | Task |
|---|---|
| Purpose / success criteria | Tasks 4, 9, 12 |
| Architecture (engine step, decision, orders; no new order type) | Tasks 5, 9 |
| The matrix (two choices, weights, `IndependentRows`, roulette, shared `GameAI.Rand`) | Tasks 7, 11 |
| 1. `CombatSystem` core extraction | Task 3 |
| 2. `FightProjector` | Task 5 |
| 3. `RetreatPlanner` (gate, tiers, blockade filtering, output) | Tasks 6, 8 |
| 4. Precedence in `PlanShipActions` (exclusions, cooldown, Expand too) | Task 9 |
| 5. Engaged-force loss share (`Engaged`, a result every fight turn, `LossShareToward`, constants shipped unchanged) | Task 4 |
| Tunables | Task 2 |
| `UpdateResult` rename | Task 1 |
| Tuning-log lines and tracker, `Hostility` field | Tasks 4, 10 |
| Tests (incl. simultaneity, existing suites updated) | Tasks 2-11 |
| Saves (nothing new) | no task needed (no saved field is added) |
| Docs, skill, FUTURE_FEATURES, memory | Task 11 |
| Out of scope / risks | recorded in the spec; the `Resolve` extraction risk is Task 3's stop condition |

**Deviation to flag to the owner:** the spec defines the projected loss as "strength lost"; the plan measures it as `1 - (strength of my survivors at the end) / (strength of my group at entry)`, where a survivor's strength falls with its damage, so a badly wounded survivor counts as partly lost and a wipe-out is exactly 1. That is the quantity a retreat decision needs (a fleet that "wins" with every ship nearly dead should still be pulled out), while the loss share used for hostility and surrender stays "ships destroyed" (`StrengthLost`) as the spec says.

The spec's rename scope lists the class, the type enum and the values; the plan also renames the third nested enum `PlanetUpdateResultPriority` (its `Priority` values) for consistency, which the single substring replace does automatically.
