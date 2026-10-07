# Invasion by Dominance and Conversion Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** A player whose warships dominate a planet (docked, no at-war rival warship) slowly converts its inhabitants, and with them the planet, with a colony-ship speed-up, a fleet hold, a tuning log and self-checks.

**Architecture:** A new pure `ConversionSystem` (the `CombatSystem` pattern) runs in the engine step right after combat and flips one `Inhabitant.Player` at a time, with per-planet session state saved on `Planet`. The AI side reuses existing seams: `AssaultPlanner` gets a second hold list beside `ContestedHolds`, `PlayerAI.IsValidColonizationTarget` / `ColonizationCostDivisor` get a dominated-planet case, and hostility for non-war victims rides the existing `DiplomacyState` charge pattern (like blockade cuts).

**Tech Stack:** Unity 6000.4.1f1 C#, JsonUtility saves, Editor self-checks (no test framework).

**Spec:** `docs/superpowers/specs/2026-10-06-invasion-conversion-design.md` (read it first; it is the source of every rule below).

## Global Constraints

- **No command-line build or test runner.** Every "Run" step means: the user focuses the Unity Editor window so it recompiles, then runs the named menu item and reads the Console. A compile error appears in the Console on focus. Rider cannot see brand-new scripts until the Editor regenerates the project, and `get_file_problems` is not proof of a compile.
- **Do not edit code while a Play-mode run is going.**
- **Self-checks never use `Gameboard.Instance`.** Build `GameAIMap`/`Planet`/`PlayerAI` directly (`CombatSelfCheck.Fixture.Line()`; planets A(0,0) B(100,0) C(200,0) D(300,0) in a line, max population 5). `Planet.ChangePopulation` touches `Gameboard.Instance`, so new `Planet` methods must not call it. Give test planets distinct positions.
- **Never put a self-check exactly on a float boundary.** Where an exact flip is asserted the plan uses `conversionTurnsBase` 4 or 1 (binary-exact progress steps).
- `UpdateResultType` serializes as an int: the new value is appended LAST.
- **A newly created script's `.meta` must be committed with it.** After the Editor imports a new script, `git status` and stage the `.meta`.
- **A new default in `GameAIConstants` does not reach the loaded asset:** every new key is also written into `Assets/GameAIConstantsProductionTypes.asset` (Task 1).
- Namespace for new runtime files: `FlatSpace.AI`. Match surrounding style (comment density, `internal`-free: anything a self-check calls is `public`).
- Commits follow the user's instruction at execution time: this plan assumes a feature branch `invasion` in the main working directory (the Unity Editor reads that directory, so no worktree), created in Task 0, with a commit per task. Push only when asked.
- Spec values copied verbatim: `conversionTurnsBase` 6, `hostilityPerConversion` 3, `conversionColonizeWeight` 0.5; pace `turnsPerFlip = max(1, conversionTurnsBase x (1 - p))`, `p` = the dominator's share of the planet's current inhabitants.

## Review Focus

Each line below has a test in the task named after it.

1. A planet where two holders tie in count and the dominator is at war with both: the lower player id converts first (Task 3, victim order).
2. A planet with no inhabitants at all, or only the dominator's: nothing happens, no session, no division by zero (Task 3, start gate).
3. A save made mid-session and an older save with no conversion fields: the session restores, or loads as "no session", never as player 0 converting (Task 5).
4. The dominating fleet leaves or is joined by an at-war rival mid-session: progress resets to 0 and the hold lets go (Tasks 3 and 6).
5. A truce or peace with the only at-war holder while a non-war holder remains: the session ends, the non-war tail does not continue (Task 3).
6. Legacy mode (`Diplomacy.Enabled` false): nothing converts, no hold, no colonization tilt (Tasks 3, 6, 7).

## File Structure

Create:
- `Assets/Flatspace/GameAI/ConversionSystem.cs` — `ConversionEvent`, `ConversionReport`, `ConversionEndReason`, and the pure `ConversionSystem` (`TurnsPerFlip`, `Dominator`, `Resolve`, `PlayersIn`).
- `Assets/Flatspace/GameAI/ConversionTracker.cs` — log-only state: session start turn and flip count per planet, players already reported out of planets.
- `Assets/Flatspace/GameAI/ConversionHoldTracker.cs` — on-change reporting for the `ConversionHoldSpare` audit (the `RetreatTracker` pattern).
- `Assets/Editor/ConversionSelfCheck.cs` — this feature's self-check.
- A `.meta` for each of the four new scripts (Unity generates them).

Modify:
- `Assets/Flatspace/GameAI/GameAIConstants.cs`, `Assets/GameAIConstantsProductionTypes.asset` — three tunables.
- `Assets/Flatspace/Objects/Planets/Planet.cs` — session fields, `ConvertInhabitant`, the new result type.
- `Assets/Flatspace/GameAI/DiplomacyState.cs`, `HostilityCalculator.cs`, `PlayerAI.cs` — the hostility charge.
- `Assets/Flatspace/GameAI/GameAI.cs` — run conversion after combat, logging, the `Stance`/`Hostility` field.
- `Assets/Flatspace/Diagnostics/AITuningLogger.cs` — the new lines.
- `Assets/Flatspace/SaveSystem/SaveLoadSystem.cs`, `Assets/Flatspace/GameAI/GameAIMap.cs` — saves.
- `Assets/Flatspace/GameAI/AssaultPlanner.cs`, `PlayerAI.cs` — hold, audit, colonization.
- `Assets/Editor/AllAISelfChecks.cs`, `Assets/Editor/SimultaneitySelfCheck.cs` — registration and the order-independence case.
- `CLAUDE.md`, `FUTURE_FEATURES.md`, `.claude/skills/tuning-log/SKILL.md` — docs.

## Tuning log additions (required by the session rules)

Format `T<turn>|P<playerId>|<EventCode>|<fields...>`; `P` is the player the line is about (the dominator unless noted). Added in Tasks 2, 4 and 8; the matching `tuning-log` skill and `CLAUDE.md` "AI Tuning Log" updates are Task 9.

| Line | When logged and how it is kept from repeating | Question | Task |
|---|---|---|---|
| `ConversionStart\|<planet>\|<fromPlayers>\|<myPop>\|<totalPop>\|<turnsPerFlip>` | session start; `ConversionTracker` also logs each session found after a load once more | how often and where conversions begin | 4 |
| `Convert\|<planet>\|<fromPlayer>\|<AtWar or NonWar>\|<myPop>\|<totalPop>\|<progress>` | per converted inhabitant, no tracker (an event) | the real pace and the snowball | 4 |
| `ConversionEnd\|<planet>\|<Clean, DominanceLost or WarEnded>\|<turnsHeld>\|<converted>` | session end; `ConversionTracker.Finish` | why sessions stop (the "continue until clean" reevaluation) | 4 |
| `OwnerChanged\|<planet>\|<oldOwner>\|<newOwner>\|<clearedItem or ->` (P = new owner) | per ownership change (an event) | what conquest costs the old owner | 4 |
| `PlayerOutOfPlanets\|<player>` (P = that player) | first time a player has no inhabitants on any planet; `ConversionTracker.NoteOutOfPlanets` | how often elimination would matter | 4 |
| `ConversionHoldSpare\|<planet>\|<heldShips>\|<heldOffense>\|<shipsACallWanted>\|<Garrison, Assault or Blockade or ->\|<callTarget or ->\|<rivalOffenseNearby>\|<progress>` | on change only; `ConversionHoldTracker` | whether offense-scaled conversion (option 3) is worth building | 8 |
| `ConversionColonize\|<origin>-><target>\|<myPop>\|<totalPop>\|<routeCost>\|<nearestTarget>\|<nearestCost>` | per colonist launched at a dominated planet (an event) | whether the colony-ship speed-up is used, and whether the tilt passed a nearer target | 7 |
| `Stance` and `Hostility` gain a trailing `conversionTerm` | existing cadence (change only / every 25 turns) | how much hostility conversions cause in non-war players | 2 |

---

### Task 0: Branch

- [ ] **Step 1:** Confirm with the user that task commits on a feature branch in the main working directory are wanted, then `git switch -c invasion` (from `main` at the spec commit `a3492df`).

---

### Task 1: Tunables, planet session state, `ConvertInhabitant`, the result type

**Files:**
- Modify: `Assets/Flatspace/GameAI/GameAIConstants.cs` (after `retreatDestinationCostExponent`, line ~183)
- Modify: `Assets/GameAIConstantsProductionTypes.asset` (after `retreatDestinationCostExponent: 2`, line ~78)
- Modify: `Assets/Flatspace/Objects/Planets/Planet.cs` (enum at line ~43, priority switch at line ~64, fields after `public int Owner = NoOwner;` at line ~116)
- Create: `Assets/Editor/ConversionSelfCheck.cs`
- Modify: `Assets/Editor/AllAISelfChecks.cs`

**Interfaces:**
- Produces: `GameAIConstants.conversionTurnsBase` / `hostilityPerConversion` / `conversionColonizeWeight` (float); `Planet.ConversionBy` (int, `Planet.NoOwner` = none), `Planet.ConversionProgress` (float), `Planet.ConversionWarMask` (int), `Planet.EndConversionSession()`, `Planet.SavedConversionBy` (int), `Planet.RestoreConversion(int savedBy, float progress, int warMask)`, `bool Planet.ConvertInhabitant(int from, int to)` (true when `Owner` changed), `Planet.UpdateResult.UpdateResultType.UpdateResultTypeConversion`; `ConversionSelfCheck.Populate(Planet, params (int player, int count)[])`.

- [ ] **Step 1: Write the failing self-check**

Create `Assets/Editor/ConversionSelfCheck.cs`:

```csharp
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using FlatSpace.AI;
using Flatspace.Objects.Production;

// Invasion by dominance and conversion: the planet session state, the conversion rule, the hostility charge, the saves, the
// holds, the colonization rule and the log trackers. Spec docs/superpowers/specs/2026-10-06-invasion-conversion-design.md.
public static class ConversionSelfCheck
{
    [MenuItem("FlatSpace/AI/Run Conversion Self-Check")]
    public static void Run() => RunChecks();

    public static bool RunChecks()
    {
        var ok = RunTunableDefaultsCheck();
        ok &= RunConvertInhabitantCheck();
        ok &= RunSessionStateCheck();
        Debug.Log(ok
            ? "[ConversionSelfCheck] ALL PASSED"
            : "[ConversionSelfCheck] FAILURES (see errors above)");
        return ok;
    }

    private static bool Check(bool condition, string label)
    {
        if (!condition) Debug.LogError($"[ConversionSelfCheck] FAIL: {label}");
        return condition;
    }

    private static bool Near(float a, float b) => Mathf.Abs(a - b) < 0.001f;

    /// <summary>Replaces a planet's inhabitants (one list entry each) and sets its owner as the game does.</summary>
    public static void Populate(Planet planet, params (int player, int count)[] holders)
    {
        planet.Population.Clear();
        foreach (var (player, count) in holders)
            for (var i = 0; i < count; i++)
                planet.Population.Add(new Planet.Inhabitant { Player = player });
        planet.Owner = planet.PlayerWithMostPopulation();
    }

    public static bool RunTunableDefaultsCheck()
    {
        var c = ScriptableObject.CreateInstance<GameAIConstants>();
        try
        {
            var ok = Check(Near(c.conversionTurnsBase, 6f), "conversionTurnsBase defaults to 6");
            ok &= Check(Near(c.hostilityPerConversion, 3f), "hostilityPerConversion defaults to 3");
            ok &= Check(Near(c.conversionColonizeWeight, 0.5f), "conversionColonizeWeight defaults to 0.5");
            return ok;
        }
        finally { Object.DestroyImmediate(c); }
    }

    // One inhabitant flips; only an ownership change clears the production item and queue (no refund).
    public static bool RunConvertInhabitantCheck()
    {
        var ok = true;
        var item = ScriptableObject.CreateInstance<CatalogItem>();
        item.itemName = "Test Item";
        try
        {
            using (var f = CombatSelfCheck.Fixture.Line())
            {
                var a = f.P("A");

                Populate(a, (1, 2), (0, 1));
                a.CurrentProduction = new Planet.ProductionItem(item);
                a.ProductionQueue.Add(new Planet.ProductionItem(item));
                ok &= Check(a.Owner == 1, "precondition: player 1 owns A (2 against 1)");
                var changed = a.ConvertInhabitant(1, 0);
                ok &= Check(changed && a.Owner == 0, "2:1 becomes 1:2 after one flip: the owner changes to player 0");
                ok &= Check(a.CurrentProduction == null && a.ProductionQueue.Count == 0, "an ownership change clears the production item and the queue");

                Populate(a, (1, 4));
                a.CurrentProduction = new Planet.ProductionItem(item);
                a.ProductionQueue.Add(new Planet.ProductionItem(item));
                changed = a.ConvertInhabitant(1, 0);
                ok &= Check(!changed && a.Owner == 1 && a.Population.Count(p => p.Player == 0) == 1, "4:0 becomes 3:1: the owner stays player 1");
                ok &= Check(a.CurrentProduction != null && a.ProductionQueue.Count == 1, "no ownership change: production is untouched");

                Populate(a, (1, 3), (0, 1));
                a.CurrentProduction = new Planet.ProductionItem(item);
                changed = a.ConvertInhabitant(1, 0);
                ok &= Check(changed && a.Owner == Planet.NoOwner && a.CurrentProduction == null,
                    "3:1 becomes 2:2, a tie: the owner becomes NoOwner, which is a change, so production is cleared");

                Populate(a, (1, 2));
                a.CurrentProduction = new Planet.ProductionItem(item);
                changed = a.ConvertInhabitant(2, 0);
                ok &= Check(!changed && a.Population.Count(p => p.Player == 1) == 2 && a.CurrentProduction != null,
                    "no inhabitant of the named player: nothing changes");
            }
        }
        finally { Object.DestroyImmediate(item); }
        return ok;
    }

    // The saved form is player id + 1 so that 0, what an older save reads back, means "no session".
    public static bool RunSessionStateCheck()
    {
        var ok = true;
        using (var f = CombatSelfCheck.Fixture.Line())
        {
            var a = f.P("A");
            ok &= Check(a.ConversionBy == Planet.NoOwner && Near(a.ConversionProgress, 0f) && a.ConversionWarMask == 0 && a.SavedConversionBy == 0,
                "a fresh planet has no session and saves as 0");
            a.ConversionBy = 2; a.ConversionProgress = 0.5f; a.ConversionWarMask = 3;
            ok &= Check(a.SavedConversionBy == 3, "player 2 saves as 3");
            a.RestoreConversion(3, 0.5f, 3);
            ok &= Check(a.ConversionBy == 2 && Near(a.ConversionProgress, 0.5f) && a.ConversionWarMask == 3, "a saved 3 restores player 2, its progress and mask");
            a.RestoreConversion(0, 0.7f, 5);
            ok &= Check(a.ConversionBy == Planet.NoOwner && Near(a.ConversionProgress, 0f) && a.ConversionWarMask == 0,
                "a saved 0 (also an older save) restores no session whatever else rides along");
            a.ConversionBy = 1; a.ConversionProgress = 0.4f; a.ConversionWarMask = 2;
            a.EndConversionSession();
            ok &= Check(a.ConversionBy == Planet.NoOwner && Near(a.ConversionProgress, 0f) && a.ConversionWarMask == 0, "EndConversionSession clears all three");
        }
        return ok;
    }
}
```

Edit `Assets/Editor/AllAISelfChecks.cs`: after `("Retreat", RetreatSelfCheck.RunChecks),` add `("Conversion", ConversionSelfCheck.RunChecks),`.

- [ ] **Step 2: Run it and see it fail**

Run: the user focuses the Editor. Expected: Console compile errors (`conversionTurnsBase`, `ConvertInhabitant`, `SavedConversionBy`... do not exist).

- [ ] **Step 3: Implement**

`GameAIConstants.cs`, replace `    public float retreatDestinationCostExponent = 2f;` with:

```csharp
    public float retreatDestinationCostExponent = 2f;

    [Header("Invasion (conversion)")]
    // A planet I dominate (my warships docked, no warship of a player at war with me) converts one inhabitant each time its progress
    // reaches 1; the progress grows each turn by 1 / max(1, conversionTurnsBase x (1 - my share of the inhabitants)).
    public float conversionTurnsBase = 6f;
    // Hostility a player I am NOT at war with gains toward me for each of its inhabitants I convert (an at-war player's add nothing).
    public float hostilityPerConversion = 3f;
    // Colonization: a dominated target's choice cost is divided by 1 + this x (1 - my share of the inhabitants). 0 or below = off.
    public float conversionColonizeWeight = 0.5f;
```

`Assets/GameAIConstantsProductionTypes.asset`: replace the line `  retreatDestinationCostExponent: 2` with

```
  retreatDestinationCostExponent: 2
  conversionTurnsBase: 6
  hostilityPerConversion: 3
  conversionColonizeWeight: 0.5
```

`Planet.cs`: (a) replace

```csharp
            UpdateResultTypeFightProjection
        }
```
with
```csharp
            UpdateResultTypeFightProjection,
            // Appended last: serialized as an int. Data is a ConversionEvent (ConversionSystem); PlayerID is the victim.
            UpdateResultTypeConversion
        }
```
(b) in the constructor switch replace `                case ResultType.UpdateResultTypeFightProjection:` with
```csharp
                case ResultType.UpdateResultTypeFightProjection:
                case ResultType.UpdateResultTypeConversion:
```
(c) after the line `    public int Owner = NoOwner;` add:

```csharp

    // Invasion (see ConversionSystem): the player converting this planet's inhabitants (NoOwner = no session), the progress toward
    // its next flip, and the bitmask of the players it was at war with that held inhabitants here when the session started.
    public int ConversionBy = NoOwner;
    public float ConversionProgress = 0f;
    public int ConversionWarMask = 0;

    public void EndConversionSession()
    {
        ConversionBy = NoOwner;
        ConversionProgress = 0f;
        ConversionWarMask = 0;
    }

    /// <summary>Saved as player id + 1, so 0 (what a save without the field reads back) means "no session".</summary>
    public int SavedConversionBy => ConversionBy + 1;

    public void RestoreConversion(int savedBy, float progress, int warMask)
    {
        ConversionBy = savedBy - 1;
        ConversionProgress = savedBy > 0 ? progress : 0f;
        ConversionWarMask = savedBy > 0 ? warMask : 0;
    }

    /// <summary>
    /// Flips one inhabitant of `from` to `to` and recomputes the owner. Returns true when the owner changed; then the production item
    /// and queue are cleared, no refund, so a conquest never hands over a ship the old owner paid for. Unlike ChangePopulation it never
    /// touches Gameboard.Instance, so a self-check can drive it. False (and nothing changes) when `from` has no inhabitant here.
    /// </summary>
    public bool ConvertInhabitant(int from, int to)
    {
        var index = Population.FindIndex(x => x.Player == from);
        if (index < 0) return false;
        var before = Owner;
        Population[index] = new Inhabitant { Player = to };
        SetPlanetOwnership();
        if (Owner == before) return false;
        CurrentProduction = null;
        ProductionQueue.Clear();
        return true;
    }
```

- [ ] **Step 4: Run it and see it pass**

Run: focus the Editor, then `FlatSpace → AI → Run Conversion Self-Check`. Expected: `[ConversionSelfCheck] ALL PASSED`.

- [ ] **Step 5: Commit**

Focus the Editor so `ConversionSelfCheck.cs.meta` exists, then:
```bash
git add Assets/Editor/ConversionSelfCheck.cs Assets/Editor/ConversionSelfCheck.cs.meta Assets/Editor/AllAISelfChecks.cs Assets/Flatspace/GameAI/GameAIConstants.cs Assets/GameAIConstantsProductionTypes.asset Assets/Flatspace/Objects/Planets/Planet.cs
git commit -m "feat: invasion tunables, planet conversion session state and ConvertInhabitant"
```
(end the message with the attribution line from the session's reminder.)

---

### Task 2: The hostility charge for non-war victims

**Files:**
- Modify: `Assets/Flatspace/GameAI/DiplomacyState.cs`
- Modify: `Assets/Flatspace/GameAI/HostilityCalculator.cs`
- Modify: `Assets/Flatspace/GameAI/PlayerAI.cs` (`UpdateDiplomacy`, lines ~299-362)
- Modify: `Assets/Flatspace/GameAI/GameAI.cs` (the `LogStance` call ~429 and `LogHostility` call ~242)
- Modify: `Assets/Flatspace/Diagnostics/AITuningLogger.cs` (`LogStance`, `LogHostility`)
- Test: `Assets/Editor/ConversionSelfCheck.cs`

**Interfaces:**
- Consumes: Task 1's `hostilityPerConversion`.
- Produces: `DiplomacyState.RecordConversion(int victim, int converter)`, `PeekConversions(victim, converter)`, `TakeConversions(victim, converter)`, `DiscardConversions(victim)`; `HostilityCalculator.Inputs.Conversions` (int), `Result.ConversionTerm` (float); `DiplomacyState.Pair.ConversionTerm` (log-only float); `AITuningLogger.LogStance(..., float lossAccum = 0f, float conversionTerm = 0f)`, `LogHostility(..., float engaged = 0f, float conversionTerm = 0f)`.

- [ ] **Step 1: Write the failing tests**

In `ConversionSelfCheck.cs` add `ok &= RunConversionHostilityCheck();` to `RunChecks` and:

```csharp
    private static PlayerAI MakeAI(CombatSelfCheck.Fixture f, int id, List<GameObject> gos)
    {
        var go = new GameObject("ConversionPlayer" + id);
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

    // The charge waiting on DiplomacyState, the calculator term, and the victim's UpdateDiplomacy adding exactly the charge.
    public static bool RunConversionHostilityCheck()
    {
        var ok = true;
        var d = new DiplomacyState();
        d.RecordConversion(2, 0); d.RecordConversion(2, 0); d.RecordConversion(1, 0);
        ok &= Check(d.PeekConversions(2, 0) == 2 && d.PeekConversions(1, 0) == 1, "charges are counted per (victim, converter)");
        d.RecordConversion(2, 2); d.RecordConversion(2, -1);
        ok &= Check(d.PeekConversions(2, 2) == 0 && d.PeekConversions(2, -1) == 0, "a self or ownerless converter is ignored");
        ok &= Check(d.TakeConversions(2, 0) == 2 && d.TakeConversions(2, 0) == 0, "taking consumes the charges");
        d.DiscardConversions(1);
        ok &= Check(d.PeekConversions(1, 0) == 0, "DiscardConversions drops what nobody consumed");

        var c = ScriptableObject.CreateInstance<GameAIConstants>();
        try
        {
            var r = HostilityCalculator.Compute(new HostilityCalculator.Inputs { Previous = 10f, Conversions = 2 }, c);
            ok &= Check(Near(r.ConversionTerm, 6f), "two conversions at hostilityPerConversion 3 give a term of 6");
            ok &= Check(Near(r.Hostility, 10f * (1f - c.hostilityDecay) + 6f), "the term is added after the decay");
        }
        finally { Object.DestroyImmediate(c); }

        // The victim reads exactly the charge in its UpdateDiplomacy: the same fixture with and without two charges.
        float HostilityAfter(int conversions)
        {
            var gos = new List<GameObject>();
            try
            {
                using (var f = CombatSelfCheck.Fixture.Line())
                {
                    f.Colonize("A", 2);
                    f.Ships("A", 0, 1);                                   // player 0's ship at player 2's planet: contact
                    f.Map.Knowledge.Update(f.Map, 3, 8);
                    var pair = f.Map.Diplomacy.Get(2, 0);
                    pair.Hostility = 50f;
                    f.Map.Diplomacy.Set(2, 0, pair);
                    for (var i = 0; i < conversions; i++) f.Map.Diplomacy.RecordConversion(2, 0);
                    MakeAI(f, 2, gos).UpdateDiplomacy(10, new List<GameAI.GameAIOrder>());
                    return f.Map.Diplomacy.Get(2, 0).Hostility;
                }
            }
            finally { foreach (var go in gos) Object.DestroyImmediate(go); }
        }
        ok &= Check(Near(HostilityAfter(2) - HostilityAfter(0), 6f), "UpdateDiplomacy adds exactly 2 x hostilityPerConversion for two charges");
        return ok;
    }
```

- [ ] **Step 2: Run it and see it fail** (compile errors: `RecordConversion` missing).

- [ ] **Step 3: Implement**

`DiplomacyState.cs`: after the `DiscardCuts` method (before `// ── Saves`) add

```csharp
        // ── Conversions waiting to be charged ────────────────────────────

        private readonly Dictionary<(int, int), int> _conversions = new Dictionary<(int, int), int>();

        /// <summary>`converter` converted one of `victim`'s inhabitants while not at war with it. Self and ownerless converters are ignored.</summary>
        public void RecordConversion(int victim, int converter)
        {
            if (converter < 0 || converter == victim) return;
            _conversions.TryGetValue((victim, converter), out var count);
            _conversions[(victim, converter)] = count + 1;
        }

        public int PeekConversions(int victim, int converter)
            => _conversions.TryGetValue((victim, converter), out var count) ? count : 0;

        /// <summary>The conversions by `converter` of `victim`'s inhabitants since the last take; consumed.</summary>
        public int TakeConversions(int victim, int converter)
        {
            if (!_conversions.TryGetValue((victim, converter), out var count)) return 0;
            _conversions.Remove((victim, converter));
            return count;
        }

        /// <summary>Drops every conversion charge still waiting for `victim` (converters it has no contact with must not pile up).</summary>
        public void DiscardConversions(int victim)
        {
            foreach (var key in _conversions.Keys.Where(k => k.Item1 == victim).ToList())
                _conversions.Remove(key);
        }
```
and in `Pair` after `public float CutsTerm, NearTerm, StrengthTerm, PWar, MyStrength, RivalStrength;` add `public float ConversionTerm;   // log-only: the last turn's conversion term`.

`HostilityCalculator.cs`: `Inputs` add `public int Conversions;   // my inhabitants this rival converted this turn (it was not at war with it)`; `Result` add `public float ConversionTerm;`; in `Compute` add `var conversions = input.Conversions * constants.hostilityPerConversion;`, change the sum to `... + strength + loss + drop + conversions;` and add `ConversionTerm = conversions,` to the returned `Result`.

`PlayerAI.UpdateDiplomacy` (use `Edit` with these exact anchors): after `                        Cuts = diplomacy.TakeCuts(me, rival),` add `                        Conversions = diplomacy.TakeConversions(me, rival),`; after `                    pair.StrengthTerm = result.StrengthTerm;` add `                    pair.ConversionTerm = result.ConversionTerm;`; after `                    pair.StrengthTerm = 0f;` (the no-contact loop) add `                    pair.ConversionTerm = 0f;`; after `                diplomacy.DiscardCuts(me);   // cuts by players I have no contact with must not pile up for later` add `                diplomacy.DiscardConversions(me);`.

`AITuningLogger.cs`: `LogStance` signature `float pWar, float lossAccum = 0f, float conversionTerm = 0f)` and append `, conversionTerm.ToString("0.#", ci)` as the last `FormatLine` field; `LogHostility` signature `float engaged = 0f, float conversionTerm = 0f)` and append the same last field; update both summaries (`...|lossAccum|conversionTerm`, `...|engagedStrength|conversionTerm`).

`GameAI.cs`: the `LogStance` call gains `stancePair.ConversionTerm` after `stancePair.LossAccum`; the `LogHostility` call gains `pair.ConversionTerm` after `pair.Engaged`.

- [ ] **Step 4: Run it and see it pass** (`Run Conversion Self-Check`; also run `Run Diplomacy Self-Check` to confirm nothing regressed: `Inputs.Conversions` defaults to 0).

- [ ] **Step 5: Commit**
```bash
git add Assets/Editor/ConversionSelfCheck.cs Assets/Flatspace/GameAI/DiplomacyState.cs Assets/Flatspace/GameAI/HostilityCalculator.cs Assets/Flatspace/GameAI/PlayerAI.cs Assets/Flatspace/GameAI/GameAI.cs Assets/Flatspace/Diagnostics/AITuningLogger.cs
git commit -m "feat: hostility charge for a non-war player whose inhabitants are converted"
```

---

### Task 3: `ConversionSystem` (the rule)

**Files:**
- Create: `Assets/Flatspace/GameAI/ConversionSystem.cs`
- Test: `Assets/Editor/ConversionSelfCheck.cs`

**Interfaces:**
- Consumes: Task 1 (`Planet` session fields, `ConvertInhabitant`, `UpdateResultTypeConversion`), Task 2 (`DiplomacyState.RecordConversion`).
- Produces: `ConversionSystem.TurnsPerFlip(int mine, int total, GameAIConstants)`, `ConversionSystem.Dominator(Planet, GameAIMap, WarshipStats)` (player id or `Planet.NoOwner`), `ConversionSystem.Resolve(GameAIMap, WarshipStats, GameAIConstants, int turn, List<Planet.UpdateResult>)` returning `List<ConversionReport>`, `ConversionSystem.PlayersIn(int mask)` (string); `ConversionReport` (`What` Start/Flip/End, `Planet`, `Dominator`, `WarMask`, `MyPopulation`, `TotalPopulation`, `TurnsPerFlip`, `Flip`, `EndReason`); `ConversionEvent` (`Dominator`, `From`, `AtWar`, `MyPopulation`, `TotalPopulation`, `Progress`, `OwnerChanged`, `OldOwner`, `NewOwner`, `ClearedItem`); `ConversionEndReason` (`Clean`, `DominanceLost`, `WarEnded`).

- [ ] **Step 1: Write the failing tests**

Add to `RunChecks`: `ok &= RunDominatorCheck(); ok &= RunPaceCheck(); ok &= RunStartGateCheck(); ok &= RunVictimOrderCheck(); ok &= RunSessionEndsCheck(); ok &= RunOwnershipFlipCheck();` and add to `ConversionSelfCheck.cs`:

```csharp
    private static List<ConversionReport> Turn(CombatSelfCheck.Fixture f, int turn, List<Planet.UpdateResult> results = null)
    {
        f.Map.Diplomacy.Turn = turn;
        return ConversionSystem.Resolve(f.Map, f.Stats, f.Constants, turn, results ?? new List<Planet.UpdateResult>());
    }

    private static int Count(Planet planet, int player) => planet.Population.Count(p => p.Player == player);

    private static int Flips(IEnumerable<ConversionReport> reports) => reports.Count(r => r.What == ConversionReport.Kind.Flip);

    // Dominance: warships docked with offense, no at-war rival warship; two non-war dominators: more offense, then the lower id.
    public static bool RunDominatorCheck()
    {
        var ok = true;
        using (var f = CombatSelfCheck.Fixture.Line())
        {
            var a = f.P("A");
            ok &= Check(ConversionSystem.Dominator(a, f.Map, f.Stats) == Planet.NoOwner, "no ships: nobody dominates");
            f.Ships("A", 0, 1);
            ok &= Check(ConversionSystem.Dominator(a, f.Map, f.Stats) == 0, "one warship: its owner dominates");
            f.Ships("A", 1, 1);
            ok &= Check(ConversionSystem.Dominator(a, f.Map, f.Stats) == 0, "two players at peace, equal offense: the lower id dominates");
            f.Ships("A", 1, 1);
            ok &= Check(ConversionSystem.Dominator(a, f.Map, f.Stats) == 1, "two players at peace: the one with more offense dominates");
            f.War(0, 1);
            ok &= Check(ConversionSystem.Dominator(a, f.Map, f.Stats) == Planet.NoOwner, "at-war warships on the same planet: nobody dominates");
        }
        using (var f = CombatSelfCheck.Fixture.Line())
        {
            f.Ships("A", 0, 1, 100f);     // 100 damage on a Health stat of 100: health 0, offense 0
            ok &= Check(ConversionSystem.Dominator(f.P("A"), f.Map, f.Stats) == Planet.NoOwner, "a ship with no health left does not dominate");
            WarshipSelfCheck.DockWarships(f.P("B"), Planet.NoOwner, 2);
            ok &= Check(ConversionSystem.Dominator(f.P("B"), f.Map, f.Stats) == Planet.NoOwner, "ownerless ships never dominate");
        }
        return ok;
    }

    // turnsPerFlip = max(1, N x (1 - my share)); progress carries; the snowball shortens the second flip.
    public static bool RunPaceCheck()
    {
        var ok = true;
        var c = ScriptableObject.CreateInstance<GameAIConstants>();
        try
        {
            c.conversionTurnsBase = 6f;
            ok &= Check(Near(ConversionSystem.TurnsPerFlip(0, 4, c), 6f), "no inhabitants of mine: N turns");
            ok &= Check(Near(ConversionSystem.TurnsPerFlip(1, 4, c), 4.5f), "1 of 4: 6 x 0.75 = 4.5");
            ok &= Check(Near(ConversionSystem.TurnsPerFlip(9, 10, c), 1f), "9 of 10: 0.6 is floored at 1 turn");
            ok &= Check(Near(ConversionSystem.TurnsPerFlip(0, 0, c), 6f), "an empty planet does not divide by zero");
        }
        finally { Object.DestroyImmediate(c); }

        using (var f = CombatSelfCheck.Fixture.Line())
        {
            f.Constants.conversionTurnsBase = 4f;     // 1 / 4 is exact in binary: the first flip lands on turn 4
            var a = f.P("A");
            Populate(a, (1, 4));
            f.Ships("A", 0, 1); f.War(0, 1);
            for (var t = 1; t <= 3; t++)
                ok &= Check(Flips(Turn(f, t)) == 0 && Count(a, 1) == 4, $"turn {t}: no flip yet");
            ok &= Check(Near(a.ConversionProgress, 0.75f) && a.ConversionBy == 0, "after 3 turns: progress 0.75 in a session of player 0");
            var fourth = Turn(f, 4);
            ok &= Check(Flips(fourth) == 1 && Count(a, 1) == 3 && Count(a, 0) == 1 && a.Owner == 1, "turn 4: one flip, 3 : 1, the owner stays player 1");
            ok &= Check(Near(a.ConversionProgress, 0f), "the carry after the flip is 0");
            for (var t = 5; t <= 6; t++) Turn(f, t);
            ok &= Check(Count(a, 1) == 3, "with 1 of 4 mine the next flip takes 3 turns, so none yet after 2");
            for (var t = 7; t <= 8; t++) Turn(f, t);
            ok &= Check(Count(a, 1) == 2, "the snowball: the second flip lands within 4 turns of the first (3 needed), faster than the first's 4");
        }
        return ok;
    }

    // A session starts only with dominance and a holder at war; nothing happens on an empty or all-mine planet.
    public static bool RunStartGateCheck()
    {
        var ok = true;
        using (var f = CombatSelfCheck.Fixture.Line())
        {
            f.Constants.conversionTurnsBase = 1f;
            var a = f.P("A");
            Populate(a, (1, 4));
            f.Ships("A", 0, 1);                                                  // no stance: peace
            for (var t = 1; t <= 3; t++) Turn(f, t);
            ok &= Check(a.ConversionBy == Planet.NoOwner && Count(a, 1) == 4, "at peace: no session, no flip");

            Populate(a, (2, 4));
            f.War(0, 1);                                                         // at war with 1, but only player 2 lives here
            for (var t = 4; t <= 6; t++) Turn(f, t);
            ok &= Check(a.ConversionBy == Planet.NoOwner && Count(a, 2) == 4, "only a non-war holder: a session never starts");

            Populate(a);                                                         // nobody lives here
            ok &= Check(Turn(f, 7).Count == 0 && a.ConversionBy == Planet.NoOwner, "an empty planet: nothing happens");
            Populate(a, (0, 3));
            ok &= Check(Turn(f, 8).Count == 0 && a.ConversionBy == Planet.NoOwner, "only the dominator's own inhabitants: nothing happens");
        }
        using (var f = CombatSelfCheck.Fixture.Line())
        {
            f.Constants.conversionTurnsBase = 1f;
            Populate(f.P("A"), (1, 4));
            f.Ships("A", 0, 1, 100f); f.War(0, 1);                               // a dead ship dominates nothing
            Turn(f, 1);
            ok &= Check(f.P("A").ConversionBy == Planet.NoOwner, "a ship with no health left starts no session");
        }
        using (var f = CombatSelfCheck.Fixture.Line())
        {
            f.Constants.conversionTurnsBase = 1f;
            Populate(f.P("A"), (1, 4));
            f.Ships("A", 0, 1); f.War(0, 1);
            f.Map.Diplomacy.Enabled = false;                                     // legacy mode
            ok &= Check(Turn(f, 1).Count == 0 && f.P("A").ConversionBy == Planet.NoOwner && Count(f.P("A"), 1) == 4, "legacy mode: nothing converts");
        }
        return ok;
    }

    // At-war holders first (largest, then the lower id), then the non-war tail; a charge only for the non-war victim.
    public static bool RunVictimOrderCheck()
    {
        var ok = true;
        using (var f = CombatSelfCheck.Fixture.Line())
        {
            f.Constants.conversionTurnsBase = 1f;                                // every turn is exactly one flip
            var a = f.P("A");
            Populate(a, (1, 2), (2, 3));
            f.Ships("A", 0, 1); f.War(0, 1);
            Turn(f, 1);
            ok &= Check(Count(a, 1) == 1 && Count(a, 2) == 3 && Count(a, 0) == 1, "the at-war holder converts first although the non-war holder has more");
            Turn(f, 2);
            ok &= Check(Count(a, 1) == 0 && Count(a, 2) == 3 && Count(a, 0) == 2, "its last inhabitant converts next");
            Turn(f, 3);
            ok &= Check(Count(a, 2) == 2 && a.ConversionBy == 0, "the tail: the session goes on against the non-war holder while my fleet stays");
            Turn(f, 4);
            var last = Turn(f, 5);
            ok &= Check(Count(a, 2) == 0 && Count(a, 0) == 5 && a.Owner == 0, "clean after five flips and the planet is mine");
            ok &= Check(a.ConversionBy == Planet.NoOwner && last.Any(r => r.What == ConversionReport.Kind.End && r.EndReason == ConversionEndReason.Clean),
                "the session ends Clean in the turn of the last flip");
            ok &= Check(f.Map.Diplomacy.PeekConversions(2, 0) == 3 && f.Map.Diplomacy.PeekConversions(1, 0) == 0,
                "a charge for each of the non-war player's 3 inhabitants and none for the at-war player's");
        }
        using (var f = CombatSelfCheck.Fixture.Line())
        {
            f.Constants.conversionTurnsBase = 1f;
            var a = f.P("A");
            Populate(a, (1, 2), (2, 2));
            f.Ships("A", 0, 1); f.War(0, 1); f.War(0, 2);
            Turn(f, 1);
            ok &= Check(Count(a, 1) == 1 && Count(a, 2) == 2, "two at-war holders tied at 2: the lower id converts first");
        }
        return ok;
    }

    // Progress resets and the session ends when dominance, or the last war with a player of the session, ends.
    public static bool RunSessionEndsCheck()
    {
        var ok = true;
        using (var f = CombatSelfCheck.Fixture.Line())     // a truce with the only at-war holder, a non-war holder remaining
        {
            f.Constants.conversionTurnsBase = 4f;
            var a = f.P("A");
            Populate(a, (1, 2), (2, 2));
            f.Ships("A", 0, 1); f.War(0, 1);
            Turn(f, 1); Turn(f, 2);
            ok &= Check(a.ConversionBy == 0 && Near(a.ConversionProgress, 0.5f) && a.ConversionWarMask == (1 << 1), "precondition: a session with progress 0.5 and player 1 in the war mask");
            var pair = f.Map.Diplomacy.Get(0, 1);
            pair.TruceUntil = 100;
            f.Map.Diplomacy.Set(0, 1, pair);
            var reports = Turn(f, 3);
            ok &= Check(reports.Any(r => r.What == ConversionReport.Kind.End && r.EndReason == ConversionEndReason.WarEnded), "a truce ends the session (WarEnded)");
            ok &= Check(a.ConversionBy == Planet.NoOwner && Near(a.ConversionProgress, 0f), "progress is back to 0");
            ok &= Check(!reports.Any(r => r.What == ConversionReport.Kind.Start) && Count(a, 2) == 2 && Count(a, 1) == 2,
                "the non-war tail does not continue, and nothing new starts");
        }
        using (var f = CombatSelfCheck.Fixture.Line())     // a rival warship arrives
        {
            f.Constants.conversionTurnsBase = 4f;
            var a = f.P("A");
            Populate(a, (1, 4));
            f.Ships("A", 0, 1); f.War(0, 1);
            Turn(f, 1); Turn(f, 2);
            f.Ships("A", 1, 1);
            var reports = Turn(f, 3);
            ok &= Check(reports.Any(r => r.What == ConversionReport.Kind.End && r.EndReason == ConversionEndReason.DominanceLost)
                        && a.ConversionBy == Planet.NoOwner && Near(a.ConversionProgress, 0f),
                "an at-war rival warship arriving ends the session (DominanceLost) and resets progress");
        }
        using (var f = CombatSelfCheck.Fixture.Line())     // the fleet leaves
        {
            f.Constants.conversionTurnsBase = 4f;
            var a = f.P("A");
            Populate(a, (1, 4));
            f.Ships("A", 0, 1); f.War(0, 1);
            Turn(f, 1); Turn(f, 2);
            a.DockedShips.Clear();
            var reports = Turn(f, 3);
            ok &= Check(reports.Any(r => r.What == ConversionReport.Kind.End && r.EndReason == ConversionEndReason.DominanceLost)
                        && a.ConversionBy == Planet.NoOwner && Near(a.ConversionProgress, 0f),
                "the fleet leaving ends the session and resets progress");
            f.Ships("A", 0, 1);
            Turn(f, 4);
            ok &= Check(Near(a.ConversionProgress, 0.25f), "a fleet that returns starts again from 0 (one turn of progress)");
        }
        return ok;
    }

    // An ownership change clears production and is reported with the cleared item; the result carries the event to the victim.
    public static bool RunOwnershipFlipCheck()
    {
        var ok = true;
        var item = ScriptableObject.CreateInstance<CatalogItem>();
        item.itemName = "Test Item";
        try
        {
            using (var f = CombatSelfCheck.Fixture.Line())
            {
                f.Constants.conversionTurnsBase = 1f;
                var a = f.P("A");
                Populate(a, (1, 2));
                a.CurrentProduction = new Planet.ProductionItem(item);
                f.Ships("A", 0, 1); f.War(0, 1);
                var results = new List<Planet.UpdateResult>();
                var first = Turn(f, 1, results);
                var flip = first.First(r => r.What == ConversionReport.Kind.Flip).Flip;
                ok &= Check(flip.OwnerChanged && flip.OldOwner == 1 && flip.NewOwner == Planet.NoOwner && flip.ClearedItem == "Test Item"
                            && flip.AtWar && flip.From == 1 && flip.MyPopulation == 1 && flip.TotalPopulation == 2,
                    "1:1 is a tie: the owner changes from 1 to NoOwner and the item is reported as cleared");
                ok &= Check(a.CurrentProduction == null, "production was cleared");
                ok &= Check(results.Count == 1 && results[0].Result == Planet.UpdateResult.UpdateResultType.UpdateResultTypeConversion
                            && results[0].PlayerID == 1 && results[0].Data is ConversionEvent,
                    "one Conversion result per flip, for the victim, carrying the event");
                var second = Turn(f, 2);
                var flip2 = second.First(r => r.What == ConversionReport.Kind.Flip).Flip;
                ok &= Check(flip2.OwnerChanged && flip2.NewOwner == 0 && flip2.ClearedItem == null && a.Owner == 0, "the second flip gives the planet to player 0; nothing left to clear");
            }
            using (var f = CombatSelfCheck.Fixture.Line())
            {
                f.Constants.conversionTurnsBase = 1f;
                var a = f.P("A");
                Populate(a, (1, 4));
                a.CurrentProduction = new Planet.ProductionItem(item);
                f.Ships("A", 0, 1); f.War(0, 1);
                var flip = Turn(f, 1).First(r => r.What == ConversionReport.Kind.Flip).Flip;
                ok &= Check(!flip.OwnerChanged && flip.ClearedItem == null && a.CurrentProduction != null, "no ownership change: the production item stays");
            }
        }
        finally { Object.DestroyImmediate(item); }
        return ok;
    }
```

- [ ] **Step 2: Run it and see it fail** (compile errors: `ConversionSystem` missing).

- [ ] **Step 3: Implement** `Assets/Flatspace/GameAI/ConversionSystem.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Linq;

namespace FlatSpace.AI
{
    /// <summary>One conversion, carried in an UpdateResultTypeConversion result's Data (the result's PlayerID is the victim).</summary>
    public class ConversionEvent
    {
        public int Dominator;
        public int From;               // the player whose inhabitant flipped
        public bool AtWar;             // the dominator was at war with it
        public int MyPopulation;       // the dominator's inhabitants after the flip
        public int TotalPopulation;
        public float Progress;         // the carry after the flip
        public bool OwnerChanged;
        public int OldOwner;
        public int NewOwner;
        public string ClearedItem;     // the production item an ownership change cleared, null when none
    }

    public enum ConversionEndReason { Clean, DominanceLost, WarEnded }

    /// <summary>One line of the tuning log: a session started, an inhabitant flipped, or a session ended.</summary>
    public class ConversionReport
    {
        public enum Kind { Start, Flip, End }

        public Kind What;
        public string Planet;
        public int Dominator;
        public int WarMask;                      // Start: the at-war holders
        public int MyPopulation, TotalPopulation;   // Start: before the first flip
        public float TurnsPerFlip;               // Start
        public ConversionEvent Flip;             // Flip
        public ConversionEndReason EndReason;    // End
    }

    /// <summary>
    /// Invasion: a planet one player dominates (docked warships with offense, no warship of a player at war with it) converts one
    /// inhabitant each time its progress reaches 1; the progress grows by 1 / max(1, conversionTurnsBase x (1 - my share)) a turn.
    /// A session starts only against a holder the dominator is at war with, then continues against any remaining holder until the
    /// planet is clean, dominance is lost, or the dominator is at war with none of the players it started against. Pure (no
    /// Gameboard.Instance); runs in the engine step right after combat, so no player's decision order can matter. Off in legacy mode.
    /// </summary>
    public static class ConversionSystem
    {
        public static float TurnsPerFlip(int mine, int total, GameAIConstants constants)
        {
            var share = total <= 0 ? 0f : (float)mine / total;
            return Math.Max(1f, constants.conversionTurnsBase * (1f - share));
        }

        /// <summary>
        /// The player that dominates the planet, or Planet.NoOwner. Players with no docked warship of effective offense above 0 never
        /// dominate; a player with an at-war rival's such ship docked beside it does not either; of two that are not at war with
        /// each other the one with more docked offense wins, ties to the lower id.
        /// </summary>
        public static int Dominator(Planet planet, GameAIMap map, WarshipStats stats)
        {
            var offense = new SortedDictionary<int, float>();
            foreach (var ship in planet.DockedShips)
            {
                if (ship.Kind != Ship.ShipKind.WarShip || ship.Owner == Planet.NoOwner) continue;
                var value = stats.EffectiveOffense(ship);
                if (value <= 0f) continue;
                offense[ship.Owner] = (offense.TryGetValue(ship.Owner, out var sum) ? sum : 0f) + value;
            }
            var best = Planet.NoOwner;
            var bestOffense = 0f;
            foreach (var pair in offense)   // ascending id: a tie keeps the lower id
            {
                var player = pair.Key;
                if (offense.Keys.Any(other => other != player && map.Diplomacy.IsAtWar(player, other, true))) continue;
                if (pair.Value > bestOffense) { best = player; bestOffense = pair.Value; }
            }
            return best;
        }

        public static string PlayersIn(int mask)
        {
            var players = Enumerable.Range(0, 31).Where(p => (mask & (1 << p)) != 0).ToList();
            return players.Count == 0 ? "-" : string.Join(",", players);
        }

        public static List<ConversionReport> Resolve(GameAIMap map, WarshipStats stats, GameAIConstants constants, int turn,
            List<Planet.UpdateResult> results)
        {
            var reports = new List<ConversionReport>();
            if (map == null || stats == null || !map.Diplomacy.Enabled) return reports;
            foreach (var planet in map.PlanetList)
                Step(planet, map, stats, constants, results, reports);
            return reports;
        }

        private static void Step(Planet planet, GameAIMap map, WarshipStats stats, GameAIConstants constants,
            List<Planet.UpdateResult> results, List<ConversionReport> reports)
        {
            var diplomacy = map.Diplomacy;
            var dominator = Dominator(planet, map, stats);

            // A running session ends when its conditions no longer hold; progress goes back to 0.
            if (planet.ConversionBy != Planet.NoOwner)
            {
                var reason = EndReason(planet, dominator, diplomacy);
                if (reason.HasValue)
                {
                    reports.Add(new ConversionReport
                    {
                        What = ConversionReport.Kind.End, Planet = planet.PlanetName, Dominator = planet.ConversionBy, EndReason = reason.Value,
                    });
                    planet.EndConversionSession();
                }
            }
            if (dominator == Planet.NoOwner) return;
            if (!planet.Population.Exists(p => p.Player != dominator)) return;

            if (planet.ConversionBy == Planet.NoOwner)
            {
                var mask = WarMask(planet, dominator, diplomacy);
                if (mask == 0) return;       // a session starts only against a holder I am at war with
                planet.ConversionBy = dominator;
                planet.ConversionProgress = 0f;
                planet.ConversionWarMask = mask;
                var mineNow = planet.Population.Count(p => p.Player == dominator);
                reports.Add(new ConversionReport
                {
                    What = ConversionReport.Kind.Start, Planet = planet.PlanetName, Dominator = dominator, WarMask = mask,
                    MyPopulation = mineNow, TotalPopulation = planet.Population.Count,
                    TurnsPerFlip = TurnsPerFlip(mineNow, planet.Population.Count, constants),
                });
            }

            var mine = planet.Population.Count(p => p.Player == dominator);
            var total = planet.Population.Count;
            planet.ConversionProgress += 1f / TurnsPerFlip(mine, total, constants);
            if (planet.ConversionProgress < 1f) return;
            planet.ConversionProgress -= 1f;

            var victim = ChooseVictim(planet, dominator, diplomacy);
            var atWar = diplomacy.IsAtWar(dominator, victim, true);
            var oldOwner = planet.Owner;
            var cleared = planet.CurrentProduction?.Item?.itemName;
            var ownerChanged = planet.ConvertInhabitant(victim, dominator);
            if (!atWar) diplomacy.RecordConversion(victim, dominator);   // a charge for a player I am not at war with

            var flip = new ConversionEvent
            {
                Dominator = dominator, From = victim, AtWar = atWar, MyPopulation = mine + 1, TotalPopulation = total,
                Progress = planet.ConversionProgress, OwnerChanged = ownerChanged, OldOwner = oldOwner, NewOwner = planet.Owner,
                ClearedItem = ownerChanged ? cleared : null,
            };
            results.Add(new Planet.UpdateResult(planet.PlanetName, Planet.UpdateResult.UpdateResultType.UpdateResultTypeConversion, flip, victim));
            reports.Add(new ConversionReport { What = ConversionReport.Kind.Flip, Planet = planet.PlanetName, Dominator = dominator, Flip = flip });

            if (!planet.Population.Exists(p => p.Player != dominator))
            {
                reports.Add(new ConversionReport
                {
                    What = ConversionReport.Kind.End, Planet = planet.PlanetName, Dominator = dominator, EndReason = ConversionEndReason.Clean,
                });
                planet.EndConversionSession();
            }
        }

        private static ConversionEndReason? EndReason(Planet planet, int dominator, DiplomacyState diplomacy)
        {
            if (dominator != planet.ConversionBy) return ConversionEndReason.DominanceLost;
            if (!planet.Population.Exists(p => p.Player != dominator)) return ConversionEndReason.Clean;
            for (var p = 0; p < 31; p++)
                if ((planet.ConversionWarMask & (1 << p)) != 0 && diplomacy.IsAtWar(dominator, p, true))
                    return null;
            return ConversionEndReason.WarEnded;
        }

        // The players the dominator is at war with that hold inhabitants here, as a bitmask.
        private static int WarMask(Planet planet, int dominator, DiplomacyState diplomacy)
        {
            var mask = 0;
            foreach (var holder in planet.Population.Select(p => p.Player).Distinct())
                if (holder != dominator && holder >= 0 && holder < 31 && diplomacy.IsAtWar(dominator, holder, true))
                    mask |= 1 << holder;
            return mask;
        }

        // At-war holders first, then the largest, then the lower id.
        private static int ChooseVictim(Planet planet, int dominator, DiplomacyState diplomacy)
            => planet.Population.Where(p => p.Player != dominator)
                .GroupBy(p => p.Player)
                .Select(g => (player: g.Key, count: g.Count(), war: diplomacy.IsAtWar(dominator, g.Key, true)))
                .OrderByDescending(c => c.war).ThenByDescending(c => c.count).ThenBy(c => c.player)
                .First().player;
    }
}
```

- [ ] **Step 4: Run it and see it pass** (`Run Conversion Self-Check`).

- [ ] **Step 5: Commit** (stage `ConversionSystem.cs` and its `.meta`, the self-check):
```bash
git add Assets/Flatspace/GameAI/ConversionSystem.cs Assets/Flatspace/GameAI/ConversionSystem.cs.meta Assets/Editor/ConversionSelfCheck.cs
git commit -m "feat: ConversionSystem, the dominance and conversion rule"
```

---

### Task 4: Run it in the turn loop, the log tracker and the log lines

**Files:**
- Create: `Assets/Flatspace/GameAI/ConversionTracker.cs`
- Modify: `Assets/Flatspace/GameAI/GameAI.cs` (`ClearGameAI`, `GameAIUpdate`, new methods)
- Modify: `Assets/Flatspace/Diagnostics/AITuningLogger.cs`
- Test: `Assets/Editor/ConversionSelfCheck.cs`

**Interfaces:**
- Consumes: Task 3 (`ConversionSystem.Resolve`, `ConversionReport`, `PlayersIn`, `TurnsPerFlip`).
- Produces: `ConversionTracker` (`Clear()`, `Tracking(planet)`, `Begin(planet, turn)`, `CountFlip(planet)`, `Finish(planet, turn)` returning `(int turnsHeld, int converted)`, `NoteOutOfPlanets(player)` true only the first time); `AITuningLogger.LogConversionStart/LogConvert/LogConversionEnd/LogOwnerChanged/LogPlayerOutOfPlanets`.

- [ ] **Step 1: Write the failing test** (add `ok &= RunConversionTrackerCheck();` to `RunChecks`):

```csharp
    // Log-only state: sessions in progress and players already reported out of planets. The logger itself is verified by a Play run.
    public static bool RunConversionTrackerCheck()
    {
        var ok = true;
        var t = new ConversionTracker();
        ok &= Check(!t.Tracking("A"), "nothing is tracked at first");
        t.Begin("A", 10);
        t.CountFlip("A"); t.CountFlip("A");
        ok &= Check(t.Tracking("A"), "a begun session is tracked");
        var done = t.Finish("A", 17);
        ok &= Check(done.turnsHeld == 7 && done.converted == 2 && !t.Tracking("A"), "finishing reports the turns held and the flips, and forgets it");
        var unknown = t.Finish("B", 20);
        ok &= Check(unknown.turnsHeld == 0 && unknown.converted == 0, "a session never seen (a load) finishes as 0 and 0");
        t.CountFlip("C");
        ok &= Check(!t.Tracking("C"), "a flip for an untracked planet is ignored");
        ok &= Check(t.NoteOutOfPlanets(2) && !t.NoteOutOfPlanets(2) && t.NoteOutOfPlanets(3), "a player is reported out of planets once");
        t.Begin("D", 1); t.NoteOutOfPlanets(5);
        t.Clear();
        ok &= Check(!t.Tracking("D") && t.NoteOutOfPlanets(2), "Clear forgets sessions and reported players");
        return ok;
    }
```

- [ ] **Step 2: Run it and see it fail** (compile error: `ConversionTracker`).

- [ ] **Step 3: Implement**

`Assets/Flatspace/GameAI/ConversionTracker.cs`:

```csharp
using System.Collections.Generic;

namespace FlatSpace.AI
{
    /// <summary>
    /// Log-only state for the tuning log: when each conversion session started and how many inhabitants it has flipped (so the End
    /// line can report turns held and flips), and which players were already reported out of planets. Never saved: a load finds
    /// sessions on the planets and begins tracking them again (the Start line is logged once more).
    /// </summary>
    public class ConversionTracker
    {
        private readonly Dictionary<string, (int startTurn, int converted)> _sessions = new Dictionary<string, (int startTurn, int converted)>();
        private readonly HashSet<int> _outOfPlanets = new HashSet<int>();

        public void Clear()
        {
            _sessions.Clear();
            _outOfPlanets.Clear();
        }

        public bool Tracking(string planet) => _sessions.ContainsKey(planet);

        public void Begin(string planet, int turn) => _sessions[planet] = (turn, 0);

        public void CountFlip(string planet)
        {
            if (_sessions.TryGetValue(planet, out var session)) _sessions[planet] = (session.startTurn, session.converted + 1);
        }

        public (int turnsHeld, int converted) Finish(string planet, int turn)
        {
            if (!_sessions.TryGetValue(planet, out var session)) return (0, 0);
            _sessions.Remove(planet);
            return (turn - session.startTurn, session.converted);
        }

        /// <summary>True only the first time a player is reported out of planets.</summary>
        public bool NoteOutOfPlanets(int player) => _outOfPlanets.Add(player);
    }
}
```

`AITuningLogger.cs`: add before `LogBoardConfig`'s doc comment (after `LogHostility`):

```csharp
    /// <summary>A conversion session began: T&lt;turn&gt;|P&lt;dominator&gt;|ConversionStart|planet|fromPlayers|myPop|totalPop|turnsPerFlip.</summary>
    public static void LogConversionStart(int turnNumber, int playerId, string planet, string fromPlayers, int myPop, int totalPop, float turnsPerFlip)
    {
        if (_currentLogPath == null) return;
        var ci = System.Globalization.CultureInfo.InvariantCulture;
        AppendLines(new List<string> { FormatLine(turnNumber, playerId, "ConversionStart", planet, fromPlayers,
            myPop.ToString(ci), totalPop.ToString(ci), turnsPerFlip.ToString("0.##", ci)) });
    }

    /// <summary>One inhabitant converted: T&lt;turn&gt;|P&lt;dominator&gt;|Convert|planet|fromPlayer|AtWar or NonWar|myPop|totalPop|progress.</summary>
    public static void LogConvert(int turnNumber, int playerId, string planet, int fromPlayer, bool atWar, int myPop, int totalPop, float progress)
    {
        if (_currentLogPath == null) return;
        var ci = System.Globalization.CultureInfo.InvariantCulture;
        AppendLines(new List<string> { FormatLine(turnNumber, playerId, "Convert", planet, fromPlayer.ToString(ci),
            atWar ? "AtWar" : "NonWar", myPop.ToString(ci), totalPop.ToString(ci), progress.ToString("0.##", ci)) });
    }

    /// <summary>A conversion session ended: T&lt;turn&gt;|P&lt;dominator&gt;|ConversionEnd|planet|Clean, DominanceLost or WarEnded|turnsHeld|converted.</summary>
    public static void LogConversionEnd(int turnNumber, int playerId, string planet, string reason, int turnsHeld, int converted)
    {
        if (_currentLogPath == null) return;
        var ci = System.Globalization.CultureInfo.InvariantCulture;
        AppendLines(new List<string> { FormatLine(turnNumber, playerId, "ConversionEnd", planet, reason,
            turnsHeld.ToString(ci), converted.ToString(ci)) });
    }

    /// <summary>A planet changed owner through conversion (P = the new owner, -1 for a tie): OwnerChanged|planet|oldOwner|newOwner|clearedItem or -.</summary>
    public static void LogOwnerChanged(int turnNumber, int newOwner, string planet, int oldOwner, string clearedItem)
    {
        if (_currentLogPath == null) return;
        var ci = System.Globalization.CultureInfo.InvariantCulture;
        AppendLines(new List<string> { FormatLine(turnNumber, newOwner, "OwnerChanged", planet, oldOwner.ToString(ci),
            newOwner.ToString(ci), string.IsNullOrEmpty(clearedItem) ? "-" : clearedItem) });
    }

    /// <summary>A player has no inhabitants left on any planet (once per player): T&lt;turn&gt;|P&lt;player&gt;|PlayerOutOfPlanets.</summary>
    public static void LogPlayerOutOfPlanets(int turnNumber, int playerId)
    {
        if (_currentLogPath == null) return;
        AppendLines(new List<string> { FormatLine(turnNumber, playerId, "PlayerOutOfPlanets") });
    }
```

`GameAI.cs`: (a) add a field next to `_redirectFailedLogged`:

```csharp
            // Log-only: conversion sessions in progress (start turn, flips) and the players already reported out of planets.
            private readonly ConversionTracker _conversions = new ConversionTracker();
```
(b) in `ClearGameAI` add `_conversions.Clear();`. (c) in `GameAIUpdate` after `RunCombat(planetUpdateResults);` add `RunConversion(planetUpdateResults);`. (d) add after `RunCombat`:

```csharp
            // After combat: a planet one player dominates converts one inhabitant at a time (see ConversionSystem). The reports are
            // only for the tuning log.
            private void RunConversion(List<Planet.UpdateResult> results)
            {
                var stats = new WarshipStats(BlockadeSystem.ResearchItemsFrom(Gameboard.Instance.players));
                var turn = Gameboard.Instance.TurnNumber;
                LogConversion(turn, ConversionSystem.Resolve(GameAIMap, stats, GameAIMap.GameAIConstants, turn, results));
            }

            private void LogConversion(int turn, List<ConversionReport> reports)
            {
                foreach (var report in reports)
                {
                    switch (report.What)
                    {
                        case ConversionReport.Kind.Start:
                            _conversions.Begin(report.Planet, turn);
                            AITuningLogger.LogConversionStart(turn, report.Dominator, report.Planet,
                                ConversionSystem.PlayersIn(report.WarMask), report.MyPopulation, report.TotalPopulation, report.TurnsPerFlip);
                            break;
                        case ConversionReport.Kind.Flip:
                        {
                            var flip = report.Flip;
                            _conversions.CountFlip(report.Planet);
                            AITuningLogger.LogConvert(turn, flip.Dominator, report.Planet, flip.From, flip.AtWar,
                                flip.MyPopulation, flip.TotalPopulation, flip.Progress);
                            if (flip.OwnerChanged)
                                AITuningLogger.LogOwnerChanged(turn, flip.NewOwner, report.Planet, flip.OldOwner, flip.ClearedItem);
                            if (!GameAIMap.PlanetList.Any(p => p.Population.Exists(i => i.Player == flip.From))
                                && _conversions.NoteOutOfPlanets(flip.From))
                                AITuningLogger.LogPlayerOutOfPlanets(turn, flip.From);
                            break;
                        }
                        case ConversionReport.Kind.End:
                        {
                            var held = _conversions.Finish(report.Planet, turn);
                            AITuningLogger.LogConversionEnd(turn, report.Dominator, report.Planet, report.EndReason.ToString(),
                                held.turnsHeld, held.converted);
                            break;
                        }
                    }
                }
                // A session found on a planet after a load has no tracker entry yet: begin it and log its Start once more.
                foreach (var planet in GameAIMap.PlanetList)
                {
                    if (planet.ConversionBy == Planet.NoOwner || _conversions.Tracking(planet.PlanetName)) continue;
                    _conversions.Begin(planet.PlanetName, turn);
                    var mine = planet.Population.Count(p => p.Player == planet.ConversionBy);
                    AITuningLogger.LogConversionStart(turn, planet.ConversionBy, planet.PlanetName,
                        ConversionSystem.PlayersIn(planet.ConversionWarMask), mine, planet.Population.Count,
                        ConversionSystem.TurnsPerFlip(mine, planet.Population.Count, GameAIMap.GameAIConstants));
                }
            }
```

- [ ] **Step 4: Run** `Run Conversion Self-Check` (passes). The logger and wiring are checked in a Play run (Task 10): with `_logAIEvents` on, a war with a conversion shows the lines.

- [ ] **Step 5: Commit** (stage `ConversionTracker.cs` and its `.meta`).
```bash
git add Assets/Flatspace/GameAI/ConversionTracker.cs Assets/Flatspace/GameAI/ConversionTracker.cs.meta Assets/Flatspace/GameAI/GameAI.cs Assets/Flatspace/Diagnostics/AITuningLogger.cs Assets/Editor/ConversionSelfCheck.cs
git commit -m "feat: run conversion after combat and log its sessions, flips and owner changes"
```

---

### Task 5: Saves

**Files:**
- Modify: `Assets/Flatspace/SaveSystem/SaveLoadSystem.cs` (`PlanetSave` ~line 81, the `new PlanetSave { ... }` at ~225-245)
- Modify: `Assets/Flatspace/GameAI/GameAIMap.cs` (`SetPlanetSimulationStats`, after `planet.Owner = planetStatus.owner;` ~406)
- Test: `Assets/Editor/ConversionSelfCheck.cs`

**Interfaces:**
- Consumes: Task 1 (`SavedConversionBy`, `RestoreConversion`).
- Produces: `GameSave.PlanetSave.conversionProgress` (float), `conversionBy` (int, id + 1), `conversionWarMask` (int).

- [ ] **Step 1: Write the failing test** (add `ok &= RunConversionSaveCheck();`):

```csharp
    // The save fields survive JsonUtility, and an older save (no keys) loads as "no session", never as player 0 converting.
    public static bool RunConversionSaveCheck()
    {
        var ok = true;
        using (var f = CombatSelfCheck.Fixture.Line())
        {
            var a = f.P("A");
            a.ConversionBy = 2; a.ConversionProgress = 0.5f; a.ConversionWarMask = 3;
            var save = new SaveLoadSystem.GameSave.PlanetSave
            {
                name = "A", conversionBy = a.SavedConversionBy, conversionProgress = a.ConversionProgress, conversionWarMask = a.ConversionWarMask,
            };
            var back = JsonUtility.FromJson<SaveLoadSystem.GameSave.PlanetSave>(JsonUtility.ToJson(save));
            var b = f.P("B");
            b.RestoreConversion(back.conversionBy, back.conversionProgress, back.conversionWarMask);
            ok &= Check(b.ConversionBy == 2 && Near(b.ConversionProgress, 0.5f) && b.ConversionWarMask == 3, "a session survives the JSON round trip");

            var old = JsonUtility.FromJson<SaveLoadSystem.GameSave.PlanetSave>("{\"name\":\"A\"}");
            var c = f.P("C");
            c.RestoreConversion(old.conversionBy, old.conversionProgress, old.conversionWarMask);
            ok &= Check(old.conversionBy == 0 && c.ConversionBy == Planet.NoOwner && Near(c.ConversionProgress, 0f), "an older save with no keys loads with no session");
        }
        return ok;
    }
```

- [ ] **Step 2: Run it and see it fail** (compile error: `conversionBy`).

- [ ] **Step 3: Implement**

`SaveLoadSystem.cs`: in `PlanetSave`, after `public List<string> completedImprovements;` add

```csharp
            // Invasion (ConversionSystem): the converting player saved as id + 1 (0 = no session, which is also what an older save reads
            // back), its progress toward the next flip and the bitmask of the players it was at war with when the session started.
            public int conversionBy;
            public float conversionProgress;
            public int conversionWarMask;
```
and in the initializer replace `                    completedImprovements = planet.CompletedImprovements.ConvertAll(c => c.Item1)` with

```csharp
                    completedImprovements = planet.CompletedImprovements.ConvertAll(c => c.Item1),
                    conversionBy = planet.SavedConversionBy,
                    conversionProgress = planet.ConversionProgress,
                    conversionWarMask = planet.ConversionWarMask
```
`GameAIMap.cs`: after `                    planet.Owner = planetStatus.owner;` add `                    planet.RestoreConversion(planetStatus.conversionBy, planetStatus.conversionProgress, planetStatus.conversionWarMask);`.

- [ ] **Step 4: Run it and see it pass.** Then, in Play mode (later, Task 10): save mid-conversion, load, and confirm the `ConversionStart` line appears once more.

- [ ] **Step 5: Commit**
```bash
git add Assets/Flatspace/SaveSystem/SaveLoadSystem.cs Assets/Flatspace/GameAI/GameAIMap.cs Assets/Editor/ConversionSelfCheck.cs
git commit -m "feat: save and restore a planet's conversion session"
```

---

### Task 6: The conversion hold

**Files:**
- Modify: `Assets/Flatspace/GameAI/AssaultPlanner.cs` (after `ContestedHolds`, ~line 179)
- Modify: `Assets/Flatspace/GameAI/PlayerAI.cs` (`PlanShipActions`, ~lines 1893-1899)
- Test: `Assets/Editor/ConversionSelfCheck.cs`

**Interfaces:**
- Consumes: Task 3 (`ConversionSystem.Dominator`), Task 1 (`Planet.ConversionBy`).
- Produces: `AssaultPlanner.ConversionHolds()` (`List<string>`), `AssaultPlanner.HeldOffense(Planet)` (float), `AssaultPlanner.NearbyRivalOffense(Planet)` (float).

- [ ] **Step 1: Write the failing test** (add `ok &= RunConversionHoldCheck();`):

```csharp
    // A planet I dominate (and convert, or could) is held: the home plan never strips it. The non-war tail has no assault target,
    // so only the hold keeps the fleet there.
    public static bool RunConversionHoldCheck()
    {
        var ok = true;
        AssaultPlanner Planner(CombatSelfCheck.Fixture f) => new AssaultPlanner(f.Map, 0, null, f.Stats, null, 0, new HashSet<int> { 1 });

        using (var f = CombatSelfCheck.Fixture.Line())
        {
            Populate(f.P("A"), (1, 3));
            f.Ships("A", 0, 1); f.War(0, 1);
            ok &= Check(Planner(f).ConversionHolds().SequenceEqual(new[] { "A" }), "dominated, with an at-war holder: held");
            f.Ships("A", 1, 1);
            ok &= Check(Planner(f).ConversionHolds().Count == 0, "an at-war rival warship there too: not dominated, not a conversion hold (it is a contested hold)");
        }
        using (var f = CombatSelfCheck.Fixture.Line())
        {
            Populate(f.P("A"), (1, 3));
            f.Ships("A", 0, 1);
            ok &= Check(Planner(f).ConversionHolds().Count == 0, "no war: not held");
            f.War(0, 1);
            f.Map.Diplomacy.Enabled = false;
            ok &= Check(Planner(f).ConversionHolds().Count == 0, "legacy mode: not held");
        }
        using (var f = CombatSelfCheck.Fixture.Line())
        {
            Populate(f.P("A"), (2, 2), (0, 3));            // a non-war holder is left; my session is running
            f.Ships("A", 0, 1); f.War(0, 1);
            f.P("A").ConversionBy = 0;
            ok &= Check(Planner(f).ConversionHolds().SequenceEqual(new[] { "A" }), "the non-war tail: my running session holds the planet");
            f.P("A").EndConversionSession();
            ok &= Check(Planner(f).ConversionHolds().Count == 0, "no session and no at-war holder: not held");
            Populate(f.P("A"), (0, 3));
            f.P("A").ConversionBy = 0;
            ok &= Check(Planner(f).ConversionHolds().Count == 0, "a session on a planet with nothing foreign left: not held");
        }
        using (var f = CombatSelfCheck.Fixture.Line())
        {
            Populate(f.P("A"), (1, 3));
            f.Ships("B", 1, 2);                               // a rival fleet beside A
            f.Ships("A", 0, 1); f.War(0, 1);
            f.Map.Knowledge.Update(f.Map, 2, 8);
            var planner = Planner(f);
            ok &= Check(planner.HeldOffense(f.P("A")) > 0f, "HeldOffense reads my docked offense");
            ok &= Check(Near(planner.NearbyRivalOffense(f.P("A")), planner.HeldOffense(f.P("A")) * 2f), "the rival's offense on a neighbouring known planet is 2 ships' worth");
        }

        // End to end through PlayerAI: the tail has no assault target, so only the hold keeps the ships; without it they go to B.
        var gos = new List<GameObject>();
        try
        {
            float ShipsSentFromA(bool session)
            {
                using (var f = CombatSelfCheck.Fixture.Line())
                {
                    f.Constants.maxPathNodesForKnowledge = 8;
                    f.Constants.maxPathNodesForShipTransport = 10;
                    f.Colonize("B", 0);
                    Populate(f.P("A"), (2, 2), (0, 1));
                    f.P("A").Owner = Planet.NoOwner;
                    f.Ships("A", 0, 2); f.War(0, 1);
                    if (session) f.P("A").ConversionBy = 0;
                    f.Map.Knowledge.Update(f.Map, 3, 8);
                    var ai = MakeAI(f, 0, gos);
                    return ai.PlanShipActions(1).Where(a => a.Origin == "A").Sum(a => a.Count);
                }
            }
            ok &= Check(ShipsSentFromA(true) == 0, "with my session running on A, the planner sends nothing away from it");
            ok &= Check(ShipsSentFromA(false) > 0, "control: with no session the two ships on A are spare and go to garrison B");
        }
        finally { foreach (var go in gos) Object.DestroyImmediate(go); }
        return ok;
    }
```

- [ ] **Step 2: Run it and see it fail** (compile error: `ConversionHolds`).

- [ ] **Step 3: Implement**

`AssaultPlanner.cs`, after `ContestedHolds()`:

```csharp
            /// <summary>My real docked warship offense at the planet. Public for the conversion-hold audit.</summary>
            public float HeldOffense(Planet planet) => DockedOffense(planet);

            /// <summary>
            /// Planets I dominate (my warships docked with offense, no warship of a player at war with me) and convert, or could: an
            /// at-war holder still has inhabitants there, or my own conversion session is running with inhabitants of others left (the
            /// non-war tail has no assault target, so only this hold keeps the fleet). Stateless; empty without warship stats and in
            /// legacy mode (nothing converts there). The home plan never strips these planets, so the dominance, and with it the
            /// session's progress, is not lost the turn after a fight is won. Retreat still runs first and wins.
            /// </summary>
            public List<string> ConversionHolds()
            {
                var holds = new List<string>();
                if (_stats == null || !_map.Diplomacy.Enabled) return holds;
                foreach (var planet in _map.PlanetList)
                {
                    if (DockedOffense(planet) <= 0f) continue;
                    if (ConversionSystem.Dominator(planet, _map, _stats) != _playerId) continue;
                    var session = planet.ConversionBy == _playerId && planet.Population.Exists(p => p.Player != _playerId);
                    var atWarHolder = planet.Population.Exists(p => p.Player != _playerId && _map.Diplomacy.IsAtWar(_playerId, p.Player, true));
                    if (session || atWarHolder) holds.Add(planet.PlanetName);
                }
                return holds;
            }

            /// <summary>
            /// The largest single at-war rival's docked offense on the planet and its known neighbours: what could come back to
            /// break a hold. Reporting only (the ConversionHoldSpare audit).
            /// </summary>
            public float NearbyRivalOffense(Planet planet)
            {
                if (_stats == null) return 0f;
                var names = new List<string> { planet.PlanetName };
                names.AddRange(_map.GetNeighbours(planet.PlanetName));
                var perPlayer = new Dictionary<int, float>();
                foreach (var name in names.Distinct())
                {
                    if (!_map.Knowledge.IsKnown(_playerId, name)) continue;
                    var near = _map.GetPlanet(name);
                    if (near == null) continue;
                    foreach (var ship in near.DockedShips)
                    {
                        if (ship.Kind != Ship.ShipKind.WarShip || ship.Owner == Planet.NoOwner || ship.Owner == _playerId) continue;
                        if (!_map.Diplomacy.IsAtWar(_playerId, ship.Owner, true)) continue;
                        perPlayer[ship.Owner] = (perPlayer.TryGetValue(ship.Owner, out var sum) ? sum : 0f) + _stats.EffectiveOffense(ship);
                    }
                }
                return perPlayer.Count == 0 ? 0f : perPlayer.Values.Max();
            }
```

`PlayerAI.PlanShipActions`: replace

```csharp
                var transport = new ShipTransportPlanner(AIMap, Player.playerID, Strategy)
                {
                    HeldPlanet  = targetName,
                    HeldPlanets = assault.ContestedHolds().Where(p => !retreat.Retreating.Contains(p)).ToList(),
```
with
```csharp
                var contested = assault.ContestedHolds().Where(p => !retreat.Retreating.Contains(p)).ToList();
                var conversionHolds = assault.ConversionHolds().Where(p => !retreat.Retreating.Contains(p)).ToList();
                var transport = new ShipTransportPlanner(AIMap, Player.playerID, Strategy)
                {
                    HeldPlanet  = targetName,
                    HeldPlanets = contested.Union(conversionHolds).ToList(),
```
(the rest of the initializer is unchanged).

- [ ] **Step 4: Run it and see it pass.** If the `PlanShipActions` control case (`ShipsSentFromA(false) > 0`) fails because the garrison planner keeps the ships (an unexpected planner rule), read `ShipTransportPlanner`'s garrison for B, adjust the fixture so B is clearly short of its garrison (more planets of mine, or `f.Constants.garrisonOuter`), and keep the pair of assertions: held sends 0, control sends more than 0.

- [ ] **Step 5: Commit**
```bash
git add Assets/Flatspace/GameAI/AssaultPlanner.cs Assets/Flatspace/GameAI/PlayerAI.cs Assets/Editor/ConversionSelfCheck.cs
git commit -m "feat: the home plan holds a planet its fleet is converting"
```

---

### Task 7: Colonization of a dominated planet and its priority

**Files:**
- Modify: `Assets/Flatspace/GameAI/PlayerAI.cs` (`IsValidColonizationTarget` ~693, `ColonizationCostDivisor` ~716, `ProcessColonizers` log ~835)
- Modify: `Assets/Flatspace/Diagnostics/AITuningLogger.cs`
- Test: `Assets/Editor/ConversionSelfCheck.cs`

**Interfaces:**
- Consumes: Task 3 (`ConversionSystem.Dominator`), Task 1 (`conversionColonizeWeight`).
- Produces: `PlayerAI.IsConversionColonizeTarget(Planet)` (public), `PlayerAI.ConversionColonizeDivisor(string targetName)` (public), `ColonizationCostDivisor` including it, `AITuningLogger.LogConversionColonize(...)`.

- [ ] **Step 1: Write the failing test** (add `ok &= RunConversionColonizeCheck();`):

```csharp
    // A dominated planet where I hold at least one inhabitant and which is below max is a valid target even when I already hold
    // the plurality; the choice cost is divided by 1 + weight x (1 - my share).
    public static bool RunConversionColonizeCheck()
    {
        var ok = true;
        var gos = new List<GameObject>();
        try
        {
            using (var f = CombatSelfCheck.Fixture.Line())
            {
                f.Constants.maxPathNodesForKnowledge = 8;
                var ai = MakeAI(f, 0, gos);
                var a = f.P("A");
                Populate(a, (0, 3), (1, 1));               // 4 of 5: below max, I hold the plurality
                f.War(0, 1);
                f.Map.Knowledge.Update(f.Map, 2, 8);
                ok &= Check(!ai.IsValidColonizationTarget(a), "precondition: a planet where I hold the plurality is not a target without dominance");
                f.Ships("A", 0, 1);
                ok &= Check(ai.IsConversionColonizeTarget(a) && ai.IsValidColonizationTarget(a), "dominated, 3 of 4 mine, below max: a valid target");
                ok &= Check(Near(ai.ConversionColonizeDivisor("A"), 1f + 0.5f * (1f - 0.75f)), "the divisor is 1 + 0.5 x (1 - 0.75) = 1.125");
                ok &= Check(Near(ai.ColonizationCostDivisor("A"), 1.125f), "ColonizationCostDivisor includes it (the chokepoint tilt is off here)");

                Populate(a, (0, 1), (1, 3));
                ok &= Check(Near(ai.ConversionColonizeDivisor("A"), 1f + 0.5f * (1f - 0.25f)), "1 of 4 mine: 1.375, a colonist is worth more early");

                Populate(a, (0, 3), (1, 2));
                ok &= Check(!ai.IsConversionColonizeTarget(a) && !ai.IsValidColonizationTarget(a), "at max population (5): not a target");
                Populate(a, (1, 3));
                ok &= Check(!ai.IsConversionColonizeTarget(a), "none of my inhabitants there: the rule does not apply");
                Populate(a, (0, 4));
                ok &= Check(!ai.IsConversionColonizeTarget(a) && Near(ai.ConversionColonizeDivisor("A"), 1f), "nothing foreign left: not a conversion target, divisor 1");

                Populate(a, (0, 3), (1, 1));
                a.DockedShips.Clear();
                ok &= Check(!ai.IsConversionColonizeTarget(a) && Near(ai.ConversionColonizeDivisor("A"), 1f), "no fleet: not dominated, divisor 1");
                f.Ships("A", 0, 1);
                f.Constants.conversionColonizeWeight = 0f;
                ok &= Check(Near(ai.ConversionColonizeDivisor("A"), 1f) && ai.IsValidColonizationTarget(a), "a weight of 0 switches the tilt off, the target stays valid");
                f.Constants.conversionColonizeWeight = 0.5f;
                f.Map.Diplomacy.Enabled = false;
                ok &= Check(!ai.IsConversionColonizeTarget(a), "legacy mode: no conversion rule");
            }
        }
        finally { foreach (var go in gos) Object.DestroyImmediate(go); }
        return ok;
    }
```

- [ ] **Step 2: Run it and see it fail** (compile error).

- [ ] **Step 3: Implement**

`PlayerAI.cs`: replace the body of `IsValidColonizationTarget` ending

```csharp
                if (planet.Population.Count >= planet.MaxPopulation)             return false;
                return planet.PlayerWithMostPopulation() != Player.playerID;
```
with
```csharp
                if (planet.Population.Count >= planet.MaxPopulation)             return false;
                if (planet.PlayerWithMostPopulation() != Player.playerID)        return true;
                return IsConversionColonizeTarget(planet);   // I hold the plurality but a conversion is still running
```
and add after `IsDiversionTarget`:

```csharp
            /// <summary>
            /// A planet I dominate where I already have an inhabitant, below max population, with a foreign inhabitant left: a colonist
            /// lands as my inhabitant (it needs no conversion and raises my share, so it shortens every later flip). Without dominance
            /// the AI does not colonize its own healthy planets. Public for the self-check.
            /// </summary>
            public bool IsConversionColonizeTarget(Planet planet)
            {
                if (!AIMap.Diplomacy.Enabled || planet.Population.Count >= planet.MaxPopulation) return false;
                if (!planet.Population.Exists(p => p.Player == Player.playerID) || !planet.Population.Exists(p => p.Player != Player.playerID)) return false;
                var stats = new WarshipStats(ResearchCatalog != null ? ResearchCatalog.catalogItems : null);
                return ConversionSystem.Dominator(planet, AIMap, stats) == Player.playerID;
            }

            /// <summary>
            /// The colonization choice cost of a dominated target is divided by 1 + conversionColonizeWeight x (1 - my share of its
            /// inhabitants): a colonist is worth most early in a conversion and least on a planet that is nearly mine. 1 for any other
            /// planet and when the weight is 0 or below. Public for ColonizationCostDivisor, ColonistRedirect and the self-check.
            /// </summary>
            public float ConversionColonizeDivisor(string targetName)
            {
                var weight = AIMap.GameAIConstants.conversionColonizeWeight;
                if (weight <= 0f) return 1f;
                var planet = AIMap.GetPlanet(targetName);
                if (planet == null || !IsConversionColonizeTarget(planet)) return 1f;
                var share = (float)planet.Population.Count(p => p.Player == Player.playerID) / planet.Population.Count;
                return 1f + weight * (1f - share);
            }
```
Replace the whole `ColonizationCostDivisor` method with:

```csharp
            public float ColonizationCostDivisor(string targetName) => ChokepointDivisor(targetName) * ConversionColonizeDivisor(targetName);

            // The Consolidate/Amass chokepoint tilt alone (the ChokepointColonize log asks only about this part).
            private float ChokepointDivisor(string targetName)
            {
                if (!IsConsolidateLike(Strategy)) return 1f;
                var weight = AIMap.GameAIConstants.colonizationChokepointWeight;
                return weight <= 0f ? 1f : 1f + weight * AIMap.Chokepoint(targetName);
            }
```
(keep the existing explanatory comment above `ColonizationCostDivisor`, adding "and a dominated target by `ConversionColonizeDivisor`"). In `ProcessColonizers` replace

```csharp
                    if (nearest.TryGetValue(action.Origin, out var near) && near.target != action.Target
                        && ColonizationCostDivisor(action.Target) > 1f)
                        AITuningLogger.LogChokepointColonize(turn, Player.playerID, action.Origin, action.Target,
                            route.Cost, AIMap.Chokepoint(action.Target), near.target, near.cost);
```
with
```csharp
                    if (nearest.TryGetValue(action.Origin, out var near) && near.target != action.Target
                        && ChokepointDivisor(action.Target) > 1f)
                        AITuningLogger.LogChokepointColonize(turn, Player.playerID, action.Origin, action.Target,
                            route.Cost, AIMap.Chokepoint(action.Target), near.target, near.cost);
                    var launchTarget = AIMap.GetPlanet(action.Target);
                    if (launchTarget != null && IsConversionColonizeTarget(launchTarget))
                    {
                        nearest.TryGetValue(action.Origin, out var nearTarget);
                        AITuningLogger.LogConversionColonize(turn, Player.playerID, action.Origin, action.Target,
                            launchTarget.Population.Count(p => p.Player == Player.playerID), launchTarget.Population.Count,
                            route.Cost, nearTarget.target ?? action.Target, nearTarget.target == null ? route.Cost : nearTarget.cost);
                    }
```
`AITuningLogger.cs`:

```csharp
    /// <summary>A colonist launched at a planet I dominate: ConversionColonize|origin->target|myPop|totalPop|routeCost|nearestTarget|nearestCost.</summary>
    public static void LogConversionColonize(int turnNumber, int playerId, string origin, string target, int myPop, int totalPop,
        float routeCost, string nearestTarget, float nearestCost)
    {
        if (_currentLogPath == null) return;
        var ci = System.Globalization.CultureInfo.InvariantCulture;
        AppendLines(new List<string> { FormatLine(turnNumber, playerId, "ConversionColonize", $"{origin}->{target}",
            myPop.ToString(ci), totalPop.ToString(ci), routeCost.ToString("0.#", ci), nearestTarget, nearestCost.ToString("0.#", ci)) });
    }
```

- [ ] **Step 4: Run it and see it pass** (`Run Conversion Self-Check`, then `Run Chokepoint Self-Check` and `Run Colonist Redirect Self-Check`, which read `ColonizationCostDivisor` and must be unchanged).

- [ ] **Step 5: Commit**
```bash
git add Assets/Flatspace/GameAI/PlayerAI.cs Assets/Flatspace/Diagnostics/AITuningLogger.cs Assets/Editor/ConversionSelfCheck.cs
git commit -m "feat: colonize a dominated planet and prioritize it by how little of it is mine"
```

---

### Task 8: The `ConversionHoldSpare` audit

**Files:**
- Create: `Assets/Flatspace/GameAI/ConversionHoldTracker.cs`
- Modify: `Assets/Flatspace/GameAI/PlayerAI.cs` (`PlanShipActions` end, a new private method, a field)
- Modify: `Assets/Flatspace/Diagnostics/AITuningLogger.cs`
- Test: `Assets/Editor/ConversionSelfCheck.cs`

**Interfaces:**
- Consumes: Task 6 (`ConversionHolds`, `HeldOffense`, `NearbyRivalOffense`), the existing `ShipTransportPlanner.Plan()` / `LastStates`, `AssaultPlanner.Plan(target, states, homeActions)`.
- Produces: `ConversionHoldTracker.Entry` (`Planet`, `HeldShips`, `HeldOffense`, `Wanted`, `Call`, `CallTarget`, `RivalNearby`, `Progress`) and `ConversionHoldTracker.Update(IEnumerable<Entry>)` returning the changed entries; `PlayerAI.AuditConversionHolds(...)`; `AITuningLogger.LogConversionHoldSpare(...)`.

- [ ] **Step 1: Write the failing tests** (add `ok &= RunConversionHoldTrackerCheck(); ok &= RunConversionHoldAuditCheck();`):

```csharp
    public static bool RunConversionHoldTrackerCheck()
    {
        var ok = true;
        var t = new ConversionHoldTracker();
        ConversionHoldTracker.Entry E(string planet, int wanted, string call = "Garrison", string target = "B")
            => new ConversionHoldTracker.Entry { Planet = planet, HeldShips = 2, HeldOffense = 20f, Wanted = wanted, Call = call, CallTarget = target, RivalNearby = 0f, Progress = 0.5f };
        ok &= Check(t.Update(new[] { E("A", 1), E("C", 0, "-", "-") }).Select(e => e.Planet).SequenceEqual(new[] { "A", "C" }), "the first sighting of each held planet is reported, ordered by name");
        ok &= Check(t.Update(new[] { E("A", 1), E("C", 0, "-", "-") }).Count == 0, "the same wanted count, call and target again: not reported");
        ok &= Check(t.Update(new[] { E("A", 2), E("C", 0, "-", "-") }).Select(e => e.Planet).SequenceEqual(new[] { "A" }), "a changed wanted count is reported");
        ok &= Check(t.Update(new[] { E("A", 2, "Assault", "D"), E("C", 0, "-", "-") }).Count == 1, "a changed call is reported");
        t.Update(new ConversionHoldTracker.Entry[0]);
        ok &= Check(t.Update(new[] { E("A", 2, "Assault", "D") }).Count == 1, "a planet that left the holds is forgotten, so a later hold is reported afresh");
        t.Clear();
        ok &= Check(t.Update(new[] { E("A", 2, "Assault", "D") }).Count == 1, "Clear forgets everything");
        return ok;
    }

    // The audit runs the planners a second time without the conversion hold and counts what a call would have taken from the held planet.
    public static bool RunConversionHoldAuditCheck()
    {
        var ok = true;
        var gos = new List<GameObject>();
        try
        {
            using (var f = CombatSelfCheck.Fixture.Line())
            {
                f.Constants.maxPathNodesForKnowledge = 8;
                f.Constants.maxPathNodesForShipTransport = 10;
                f.Colonize("B", 0);
                Populate(f.P("A"), (2, 2), (0, 1));
                f.P("A").Owner = Planet.NoOwner;
                f.Ships("A", 0, 2); f.War(0, 1);
                f.P("A").ConversionBy = 0; f.P("A").ConversionProgress = 0.25f;
                f.Map.Knowledge.Update(f.Map, 3, 8);
                var ai = MakeAI(f, 0, gos);
                var entries = ai.AuditConversionHolds(1);
                ok &= Check(entries.Count == 1 && entries[0].Planet == "A" && entries[0].HeldShips == 2 && entries[0].Wanted > 0 && entries[0].Call == "Garrison" && entries[0].CallTarget == "B",
                    "A is held and B's garrison would have taken ships from it");
                ok &= Check(Near(entries[0].Progress, 0.25f) && entries[0].HeldOffense > 0f, "the entry carries the conversion progress and the held offense");
                f.P("A").ConversionBy = Planet.NoOwner;
                ok &= Check(ai.AuditConversionHolds(2).Count == 0, "no hold, no audit entry");
            }
            using (var f = CombatSelfCheck.Fixture.Line())     // nothing else wants the ships: wanted 0, call -
            {
                f.Constants.maxPathNodesForKnowledge = 8;
                Populate(f.P("A"), (2, 2), (0, 1));
                f.P("A").Owner = Planet.NoOwner;
                f.Ships("A", 0, 1); f.War(0, 1);
                f.P("A").ConversionBy = 0;
                f.Map.Knowledge.Update(f.Map, 3, 8);
                var entries = MakeAI(f, 0, gos).AuditConversionHolds(1);
                ok &= Check(entries.Count == 1 && entries[0].Wanted == 0 && entries[0].Call == "-" && entries[0].CallTarget == "-", "with no other call the entry says so");
            }
        }
        finally { foreach (var go in gos) Object.DestroyImmediate(go); }
        return ok;
    }
```

- [ ] **Step 2: Run it and see it fail** (compile errors).

- [ ] **Step 3: Implement**

`Assets/Flatspace/GameAI/ConversionHoldTracker.cs`:

```csharp
using System.Collections.Generic;
using System.Linq;

namespace FlatSpace.AI
{
    /// <summary>
    /// Turns each turn's "held conversion planets and what a call would have taken from them" into reports on change only, so the
    /// tuning log shows whether held ships were ever wanted elsewhere (evidence for offense-scaled conversion) without a line per
    /// planet per turn. A planet is reported the first turn it is held and again when its wanted count, call or call target
    /// changes; its state clears when it leaves the holds. Pure, log-only, never saved (a load reports each current one once more).
    /// </summary>
    public class ConversionHoldTracker
    {
        public struct Entry
        {
            public string Planet;
            public int HeldShips;
            public float HeldOffense;
            public int Wanted;          // held ships a call would have taken
            public string Call;         // Garrison, Assault, Blockade or -
            public string CallTarget;   // the planet the call was for, or -
            public float RivalNearby;   // the largest at-war rival's offense on or beside the planet
            public float Progress;
        }

        private readonly Dictionary<string, (int wanted, string call, string target)> _state = new Dictionary<string, (int wanted, string call, string target)>();

        public void Clear() => _state.Clear();

        public List<Entry> Update(IEnumerable<Entry> current)
        {
            var now = current.ToList();
            var changed = now
                .Where(e => !_state.TryGetValue(e.Planet, out var last) || last.wanted != e.Wanted || last.call != e.Call || last.target != e.CallTarget)
                .OrderBy(e => e.Planet, System.StringComparer.Ordinal)
                .ToList();
            _state.Clear();
            foreach (var e in now) _state[e.Planet] = (e.Wanted, e.Call, e.CallTarget);
            return changed;
        }
    }
}
```

`AITuningLogger.cs`:

```csharp
    /// <summary>A planet held for conversion and what a call would have taken from it: ConversionHoldSpare|planet|heldShips|heldOffense|shipsACallWanted|Garrison, Assault, Blockade or -|callTarget or -|rivalOffenseNearby|progress (on change only).</summary>
    public static void LogConversionHoldSpare(int turnNumber, int playerId, string planet, int heldShips, float heldOffense, int wanted,
        string call, string callTarget, float rivalNearby, float progress)
    {
        if (_currentLogPath == null) return;
        var ci = System.Globalization.CultureInfo.InvariantCulture;
        AppendLines(new List<string> { FormatLine(turnNumber, playerId, "ConversionHoldSpare", planet, heldShips.ToString(ci),
            heldOffense.ToString("0", ci), wanted.ToString(ci), call, callTarget, rivalNearby.ToString("0", ci), progress.ToString("0.##", ci)) });
    }
```

`PlayerAI.cs`: next to `_blockadeSkips` add `private readonly ConversionHoldTracker _conversionHolds = new ConversionHoldTracker();`. Refactor `PlanShipActions` minimally: after `var force = assault.LastBlockadeForce; ... LogBlockadeForce(...)` and before `return actions;` insert:

```csharp
                foreach (var entry in _conversionHolds.Update(AuditConversionHolds(turnNumber, assault, target, blockadeTarget != null, retreat, excluded)))
                    AITuningLogger.LogConversionHoldSpare(turnNumber, Player.playerID, entry.Planet, entry.HeldShips, entry.HeldOffense,
                        entry.Wanted, entry.Call, entry.CallTarget, entry.RivalNearby, entry.Progress);
```
and add the audit (a public wrapper that builds its own planners so the self-check can call it with just a turn, plus the worker):

```csharp
            /// <summary>
            /// Log-only audit of the conversion holds: the planners run a second time with only the contested holds (no orders are
            /// emitted, nothing is mutated), and the held planet's ships they would have sent say whether a call (a garrison deficit, an
            /// assault target or a blockade target) wanted them. Public for the self-check; this overload builds the planners itself.
            /// </summary>
            public List<ConversionHoldTracker.Entry> AuditConversionHolds(int turnNumber)
            {
                var stats = new WarshipStats(ResearchCatalog != null ? ResearchCatalog.catalogItems : null);
                var retreat = new RetreatPlanner.Plan();   // no retreat: the audit only needs the conversion holds
                var excluded = new HashSet<string>(CooldownPlanets(turnNumber));
                var assault = new AssaultPlanner(AIMap, Player.playerID, _blockadeView, stats, _blockadeMemory, turnNumber,
                    AssaultWarFilter()) { ExcludedTargets = excluded };
                var blockadeTarget = assault.ChooseBlockadeTarget(out _);
                var target = blockadeTarget ?? assault.ChooseEnemyTarget();
                return AuditConversionHolds(turnNumber, assault, target, blockadeTarget != null, retreat, excluded);
            }

            private List<ConversionHoldTracker.Entry> AuditConversionHolds(int turnNumber, AssaultPlanner assault, Planet target,
                bool isBlockadeTarget, RetreatPlanner.Plan retreat, ISet<string> excluded)
            {
                var entries = new List<ConversionHoldTracker.Entry>();
                var holds = assault.ConversionHolds().Where(p => !retreat.Retreating.Contains(p)).ToList();
                if (holds.Count == 0) return entries;
                var contested = assault.ContestedHolds().Where(p => !retreat.Retreating.Contains(p)).ToList();
                var audit = new ShipTransportPlanner(AIMap, Player.playerID, Strategy)
                {
                    HeldPlanet    = target?.PlanetName,
                    HeldPlanets   = contested,
                    Retreating    = retreat.Retreating,
                    RefillBlocked = CooldownPlanets(turnNumber),
                };
                var homeActions = audit.Plan();
                var assaultActions = assault.Plan(target, audit.LastStates, homeActions);
                foreach (var name in holds)
                {
                    var planet = AIMap.GetPlanet(name);
                    var garrison = homeActions.Where(a => a.Origin == name && a.Kind == Ship.ShipKind.WarShip).ToList();
                    var offense = assaultActions.Where(a => a.Origin == name && a.Kind == Ship.ShipKind.WarShip).ToList();
                    var call = "-"; var callTarget = "-"; var wanted = 0;
                    if (garrison.Count > 0)
                    {
                        call = "Garrison"; callTarget = garrison[0].Target; wanted = garrison.Sum(a => a.Count);
                    }
                    else if (offense.Count > 0)
                    {
                        call = isBlockadeTarget ? "Blockade" : "Assault"; callTarget = offense[0].Target; wanted = offense.Sum(a => a.Count);
                    }
                    entries.Add(new ConversionHoldTracker.Entry
                    {
                        Planet = name,
                        HeldShips = planet.DockedShips.Count(s => s.Kind == Ship.ShipKind.WarShip && s.Owner == Player.playerID),
                        HeldOffense = assault.HeldOffense(planet),
                        Wanted = wanted, Call = call, CallTarget = callTarget,
                        RivalNearby = assault.NearbyRivalOffense(planet),
                        Progress = planet.ConversionProgress,
                    });
                }
                return entries;
            }
```
`retreat` in `PlanShipActions` is a `RetreatPlanner.Plan` (the value `PlanRetreats` returns; `new RetreatPlanner.Plan()` is its empty form, with `Retreating` a `HashSet<string>`). `AssaultPlanner.Plan` is called exactly as `PlanShipActions` already calls it: `assault.Plan(target, transport.LastStates, homeActions)`.

- [ ] **Step 4: Run it and see it pass** (`Run Conversion Self-Check`). If an audit expectation fails because the planner's garrison rule keeps the ships (as in Task 6 step 4), adjust the fixture, not the assertion's meaning.

- [ ] **Step 5: Commit** (stage `ConversionHoldTracker.cs` and its `.meta`).
```bash
git add Assets/Flatspace/GameAI/ConversionHoldTracker.cs Assets/Flatspace/GameAI/ConversionHoldTracker.cs.meta Assets/Flatspace/GameAI/PlayerAI.cs Assets/Flatspace/Diagnostics/AITuningLogger.cs Assets/Editor/ConversionSelfCheck.cs
git commit -m "feat: ConversionHoldSpare audit of held conversion fleets"
```

---

### Task 9: Order independence, docs and the `tuning-log` skill

**Files:**
- Modify: `Assets/Editor/SimultaneitySelfCheck.cs`
- Modify: `CLAUDE.md`, `FUTURE_FEATURES.md`, `.claude/skills/tuning-log/SKILL.md`

- [ ] **Step 1: Write the failing check.** In `SimultaneitySelfCheck.cs`: (a) add `ok &= RunConversionOrderIndependenceCheck();` after `ok &= RunRetreatOrderIndependenceCheck();`; (b) add `public string Hostility;` to `Outcome`; (c) change the `RunRound` signature to `RunRound(bool reversed, bool withLosses = false, bool withRetreat = false, bool withConversion = false)`; (d) after the `if (withRetreat) { ... }` block add:

```csharp
            if (withConversion)
            {
                // Player 0 dominates D, player 1's planet, where player 2 also holds two inhabitants: two conversions (one at-war, one
                // of the non-war player 2) run in the engine step BEFORE any player decides, so player 2's hostility charge is the same
                // world for every player whatever the order they run in.
                map.Diplomacy.SetStance(0, 1, Stance.War, 0);
                map.Diplomacy.SetStance(1, 0, Stance.War, 0);
                constants.conversionTurnsBase = 1f;
                var conversionStats = new WarshipStats(research);
                var d = map.GetPlanet("D");
                d.Population.Add(new Planet.Inhabitant { Player = 2 });
                d.Population.Add(new Planet.Inhabitant { Player = 2 });
                d.Owner = d.PlayerWithMostPopulation();
                WarshipSelfCheck.DockWarships(d, 0, 2);
                map.Knowledge.Update(map, 3, 8);
                for (var t = 1; t <= 2; t++)
                {
                    map.Diplomacy.Turn = t;
                    ConversionSystem.Resolve(map, conversionStats, constants, t, new List<Planet.UpdateResult>());
                }
            }
```
(e) add `Hostility = string.Join(",", Enumerable.Range(0, 3).SelectMany(p => Enumerable.Range(0, 3).Where(r => r != p).Select(r => $"{p}{r}:{map.Diplomacy.Get(p, r).Hostility:0.###}"))),` to the `new Outcome { ... }` initializer; (f) add the check:

```csharp
    // The conversion runs in the engine step before the players decide; the charge it leaves for the non-war player must give every
    // player the same orders, stances and hostility whatever the order the players run in.
    public static bool RunConversionOrderIndependenceCheck()
    {
        var ok = true;
        var forward = RunRound(reversed: false, withConversion: true);
        var reverse = RunRound(reversed: true, withConversion: true);
        ok &= Check(forward[2].Hostility.Contains("20:") && forward[2].Hostility != RunRound(reversed: false)[2].Hostility,
            "precondition: the conversion changed player 2's hostility toward player 0 (the charge was read)");
        for (var p = 0; p < 3; ++p)
        {
            ok &= Check(forward[p].Orders.SequenceEqual(reverse[p].Orders), $"with a conversion, player {p}: the same orders whatever the player order");
            ok &= Check(forward[p].Stances == reverse[p].Stances, $"with a conversion, player {p}: the same stances");
            ok &= Check(forward[p].Hostility == reverse[p].Hostility, $"with a conversion, player {p}: the same hostility");
        }
        return ok;
    }
```
(The precondition compares against a round without conversion: the strings differ because player 2's hostility toward player 0 carries the charge.)

- [ ] **Step 2: Run** `Run Simultaneity Self-Check` before the implementation is considered done: it must pass (the conversion code exists from earlier tasks). If the precondition fails, the charge was not read: confirm player 2 has contact with player 0 after the `Knowledge.Update` (player 0's ships at D, beside player 2's planet E) and fix the fixture, not the assertion.

- [ ] **Step 3: Docs.**

`CLAUDE.md` (use `Edit`; every `old_string` below occurs once):
1. Replace `` `FlatSpace → AI → Run All AI Self-Checks` in `Assets/Editor/AllAISelfChecks.cs` (runs every AI suite: `` with
```
`FlatSpace → AI → Run Conversion Self-Check` in `Assets/Editor/ConversionSelfCheck.cs` (dominance, the conversion pace and victim order, session ends, ownership flip and production clear, the hostility charge, saves, the conversion hold and its audit, the colonization rule and the log trackers; a conversion in `SimultaneitySelfCheck`);
`FlatSpace → AI → Run All AI Self-Checks` in `Assets/Editor/AllAISelfChecks.cs` (runs every AI suite:
```
and replace `Combat, Retreat; each suite` with `Combat, Retreat, Conversion; each suite`.
2. In the Ship combat "Out of scope" bullet, replace `planetary invasion, planet defense,` with `planet defense,`.
3. Insert before `### Diplomacy` (anchor `### Diplomacy\n\n`DiplomacyState``) a new section:

```
### Invasion (conversion)

Spec `docs/superpowers/specs/2026-10-06-invasion-conversion-design.md`. A planet changes hands by **dominance and conversion**, not by troops.
`ConversionSystem` (`Assets/Flatspace/GameAI/ConversionSystem.cs`, pure) runs in `GameAI.GameAIUpdate` right after `RunCombat` (so a won fight
starts dominance the same turn), in the engine step, so no player's decision order matters; off in legacy mode.
- **Dominance:** my warships docked with effective offense above 0 and no warship of a player I am at war with (`IsAtWar(me, x, true)`, a truce
  is not war). Two dominators not at war with each other: more docked offense, then the lower id.
- **Session** (state on `Planet`: `ConversionBy`, `ConversionProgress`, `ConversionWarMask`; saved as id + 1, so an older save loads with none):
  starts only against a holder I am at war with (the gate is on holders, never on `Owner`, which is `NoOwner` on a tie); each turn
  progress += `1 / max(1, conversionTurnsBase x (1 - p))`, `p` my share of the current inhabitants; at 1 one inhabitant flips (the
  largest at-war holder first, then the non-war tail, ties to the lower id) and the remainder carries. It ends, progress back to 0, on
  dominance lost, nothing foreign left (Clean), or war with every player in `ConversionWarMask` over (peace, surrender, truce: `WarEnded`).
- **The flip:** `Planet.ConvertInhabitant` (no `Gameboard.Instance`); an `Owner` change clears `CurrentProduction` and `ProductionQueue`
  with no refund; improvements, stocks, morale are kept. Each flip appends an `UpdateResultTypeConversion` result (Data `ConversionEvent`,
  `PlayerID` the victim).
- **Hostility:** `hostilityPerConversion` toward the converter for each inhabitant converted from a player NOT at war with it (none for an
  at-war victim), recorded on `DiplomacyState.RecordConversion` in the engine step and charged by the victim's `UpdateDiplomacy`
  (the blockade-cut pattern).
- **Hold:** `AssaultPlanner.ConversionHolds()` (dominated planet with an at-war holder, or my running session with others left) joins
  `ContestedHolds()` in `ShipTransportPlanner.HeldPlanets`; retreat still wins. The `ConversionHoldSpare` audit reruns the planners without
  the conversion hold (`PlayerAI.AuditConversionHolds`) to log whether a call (garrison, assault, blockade) wanted the held ships.
- **Colonization:** `IsValidColonizationTarget` also accepts a dominated planet below max where I hold an inhabitant and a foreign one remains
  (`IsConversionColonizeTarget`); its choice cost is divided by `ConversionColonizeDivisor` = `1 + conversionColonizeWeight x (1 - my share)`
  (a colonist is worth most early), multiplied into `ColonizationCostDivisor` with the chokepoint tilt. The ColonyShip production weight is
  unchanged. Tunables on `GameAIConstants`: `conversionTurnsBase` 6, `hostilityPerConversion` 3, `conversionColonizeWeight` 0.5.
- **Reevaluate after the first tuning pass (owner's list):** offense-scaled conversion speed (use the audit and the `Convert` pace);
  continue-until-clean versus stop at ownership; a minimum force for dominance (today any one ship with offense); elimination of a player
  that loses its last planet (`PlayerOutOfPlanets` shows how often); the three tunables; a colony ship production boost for dominated planets.
Self-check: `Assets/Editor/ConversionSelfCheck.cs`.
```
4. In "AI Tuning Log", replace `` `FleetHealth|<warships>|<damaged>|<meanHealthPct>` beside `Economy`, for `` with
```
`FleetHealth|<warships>|<damaged>|<meanHealthPct>` beside `Economy`, and the invasion lines (see Invasion (conversion)): `ConversionStart|<planet>|<fromPlayers>|<myPop>|<totalPop>|<turnsPerFlip>` (session start, through `ConversionTracker`, which also logs each session found after a load once more), `Convert|<planet>|<fromPlayer>|<AtWar or NonWar>|<myPop>|<totalPop>|<progress>` (per converted inhabitant), `ConversionEnd|<planet>|<Clean, DominanceLost or WarEnded>|<turnsHeld>|<converted>`, `OwnerChanged|<planet>|<oldOwner>|<newOwner>|<clearedItem or ->` (P = the new owner), `PlayerOutOfPlanets|<player>` (once per player), `ConversionHoldSpare|<planet>|<heldShips>|<heldOffense>|<shipsACallWanted>|<Garrison, Assault, Blockade or ->|<callTarget or ->|<rivalOffenseNearby>|<progress>` (on change only, through `ConversionHoldTracker`), `ConversionColonize|<origin>-><target>|<myPop>|<totalPop>|<routeCost>|<nearestTarget>|<nearestCost>` (per colonist launched at a dominated planet) and a trailing `conversionTerm` on the `Stance` and `Hostility` lines, for
```

`FUTURE_FEATURES.md`: replace `  - (3) **Planetary invasion** — taking over another player's planet.` with
```
  - (3) **Planetary invasion** — taking over another player's planet. Designed and built 2026-10-06 as dominance and conversion (spec `docs/superpowers/specs/2026-10-06-invasion-conversion-design.md`, plan `docs/superpowers/plans/2026-10-06-invasion-conversion.md`); move to completed_features after the first tuning pass. Reevaluate after that pass (owner's list): (a) conversion speed scaled by docked offense (evidence: the `ConversionHoldSpare` audit and the `Convert` pace); (b) continue until clean versus stop at ownership; (c) a minimum force for dominance (today any one ship with effective offense above 0); (d) elimination of a player that loses its last planet (`PlayerOutOfPlanets`); (e) `conversionTurnsBase`, `hostilityPerConversion`, `conversionColonizeWeight`; (f) a colony ship production boost for dominated planets (the ColonyShip weight is unchanged).
```

`.claude/skills/tuning-log/SKILL.md`: after the Retreat bullet (the line ending `` `cooldownUntil`. ``) add:

```
  - **Invasion (conversion):** `ConversionStart|<planet>|<fromPlayers>|<myPop>|<totalPop>|<turnsPerFlip>` begins a session (P = the dominator;
    also logged once more per session found after a load), `Convert|<planet>|<fromPlayer>|<AtWar or NonWar>|<myPop>|<totalPop>|<progress>`
    is one converted inhabitant, `ConversionEnd|<planet>|<Clean, DominanceLost or WarEnded>|<turnsHeld>|<converted>` ends it,
    `OwnerChanged|<planet>|<oldOwner>|<newOwner>|<clearedItem or ->` (P = the new owner) is an ownership change, `PlayerOutOfPlanets|<player>`
    is logged once per player, `ConversionHoldSpare|<planet>|<heldShips>|<heldOffense>|<shipsACallWanted>|<Garrison, Assault, Blockade or ->|<callTarget or ->|<rivalOffenseNearby>|<progress>`
    is on change only, `ConversionColonize|<origin>-><target>|<myPop>|<totalPop>|<routeCost>|<nearestTarget>|<nearestCost>` is per colonist
    launched at a dominated planet, and `Stance`/`Hostility` lines end with `conversionTerm`. Report per run: sessions started, ended by
    reason (Clean against DominanceLost against WarEnded: many DominanceLost means holds or retreats are letting go), turns held and
    flips per session, the pace (turns between `Convert` lines on a planet against `turnsPerFlip`: does the snowball show), planets
    changing hands per player and the clearedItem on `OwnerChanged`, non-war conversions (`NonWar`) against the `conversionTerm` on the
    victims' later `Stance ... War` lines (are they declaring because of it), `ConversionColonize` launches per session and whether the
    session's pace sped up after one, and the **option 3 evidence**: for each `ConversionHoldSpare` with `shipsACallWanted` above 0, how
    long it lasted and how large `rivalOffenseNearby` was against `heldOffense` (a long call with no rival nearby means offense-scaled
    conversion or a smaller hold would have served). Also: no `Convert` line may name a planet whose session was not Started or
    re-Started after a load, `PlayerOutOfPlanets` counts, and that warship starts and the fleet cap still hold (a conquered planet's
    production was cleared, so no free ships appear).
```

- [ ] **Step 4: Run** `Run All AI Self-Checks`. Expected: `[AllAISelfChecks] ALL 15 SUITES PASSED`.

- [ ] **Step 5: Commit**
```bash
git add Assets/Editor/SimultaneitySelfCheck.cs CLAUDE.md FUTURE_FEATURES.md .claude/skills/tuning-log/SKILL.md
git commit -m "docs: invasion in CLAUDE.md, the roadmap and the tuning-log skill; conversion order independence"
```

---

### Task 10: Play-mode verification (by the user)

- [ ] **Step 1:** The user turns `_logAIEvents` on (`MainMenu` component), enters Play on a board with two players (`test2.json` or `4p.json`), lets a war and an assault run, then reads the newest `AITuningLogs/` file with `/tuning-log`.
- [ ] **Step 2:** Confirm in the log: `ConversionStart` / `Convert` / `ConversionEnd` appear; every `Convert ... NonWar` has a later effect on that victim's `Hostility`; `OwnerChanged` appears with the cleared item; no exceptions in the Console; saving mid-session and loading logs `ConversionStart` once more.
- [ ] **Step 3:** Report the first-pass findings (pace, sessions ended by reason, `ConversionHoldSpare` call counts) and the reevaluation list from the spec to the user; the roadmap entry moves to `completed_features.md` only after that tuning pass (`/update_feature_list`).

---

## Self-Review

**Spec coverage:** dominance rule, pace formula, accumulator, victim order, non-war tail, session ends and the war mask, ownership flip and production clear, hostility to non-war only, `ConversionSystem` placement and result type, tunables (Task 1), saves with id + 1 (Task 5), conversion hold (Task 6), colonization rule and tilt (Task 7), `ConversionHoldSpare` audit (Task 8), all log lines (Tasks 2, 4, 7, 8; the table above), tests incl. simultaneity, docs and the reevaluation list (Task 9), edge case player out of planets (Task 4 line). The spec's "a conversion in `SimultaneitySelfCheck`" is Task 9. The spec said the colonization tilt "applies to every strategy": `ConversionColonizeDivisor` has no strategy gate.

**Placeholder scan:** none open. Two fixtures (Task 6's `PlanShipActions` control and Task 8's audit) depend on how `ShipTransportPlanner` treats ships on a planet I do not own as spare; each step says to adjust the fixture, not the assertion's meaning, if the Editor run shows otherwise.

**Type consistency:** `ConversionReport.Kind.Start/Flip/End`, `ConversionEvent` fields, `ConversionEndReason`, `Planet.ConversionBy/Progress/WarMask`, `ConversionTracker` and `ConversionHoldTracker.Entry` field names match across Tasks 3, 4, 6, 8, 9. `LogStance`/`LogHostility` gain the same trailing `conversionTerm`.
