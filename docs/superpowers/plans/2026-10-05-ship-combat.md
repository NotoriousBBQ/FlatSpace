# Ship to Ship Combat Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Let docked warships of players at war fight (deterministic, persistent damage, focus fire, repair at home), let the rest of the AI see the result (effective offense, hostility loss terms, surrender with a truce), and show it in the tuning log.

**Architecture:** A pure `CombatSystem` runs once per turn in `GameAI.GameAIUpdate()` (after arrivals, before the planets update) and appends `PlanetUpdateResult`s (`WarshipsLost`, `ColonyShipsLost`). `Ship.Damage` stores damage taken; `WarshipStats` derives current health and effective offense; every reader of a ship's offense switches to it. Damage rides in the fleet payload and in saves. `PlayerAI` reads the loss results into hostility inputs and a Surrender choice in `StanceMatrix`; surrender is an order executed by a pure `GameAI.ApplySurrender`, which sets both stances to Peace and stamps a truce on the pair.

**Tech Stack:** Unity 6000.4.1f1 C# (`FlatSpace.AI` namespace), `JsonUtility` saves, Editor self-checks (`Assets/Editor/`), Rider MCP for compile signals.

**Spec:** `docs/superpowers/specs/2026-10-05-ship-combat-design.md` (read it and `CLAUDE.md` first; the owner's answers are in `2026-10-05-ship-combat-decisions-so-far.md`; `FUTURE_FEATURES.md` milestone `ship_combat`, element 2).

## Global Constraints

- New runtime code stays in the namespace its file uses (`FlatSpace.AI`, written `namespace FlatSpace { namespace AI { ... } }` in the GameAI folder; `WarshipStats.cs` uses a file-scoped `namespace FlatSpace.AI`); `Ship`, `Planet` and `FleetUIController` are global namespace. New Editor scripts are global namespace with `public static bool RunChecks()`.
- Pure code (`CombatSystem`, `WarshipStats` additions, `GameAI.ApplySurrender`, `DiplomacyState`, `HostilityCalculator`, `StanceMatrix`) must never touch `Gameboard.Instance`, so self-checks can drive it directly. `GameAI.ExecuteOrder` and `GameAIUpdate` (which need it) only wire and log.
- A method a self-check calls is `public`, never `internal` (`Assets/Editor` is a separate assembly).
- New enum values are appended LAST (`PlanetUpdateResultType.WarshipsLost`, then `ColonyShipsLost`; `OrderType.OrderTypeSurrender` after `OrderTypeMakePeace`); they serialize as ints. `Stance` is NOT extended (it is saved): Surrender is a decision flag, never a stored stance.
- Saves: `ShipSave.damage` (0 = full health) and `StanceSave.truceUntil` (0 = no truce); an older save loads full health and no truce. Damage is stored, never current health.
- Legacy mode (`Diplomacy.Enabled` false, and every `GameAIMap` a self-check builds directly): `CombatSystem.Resolve` returns at once; no loss terms, no Surrender. `CombatSelfCheck` switches diplomacy on explicitly (`map.Diplomacy.Enabled = true`).
- No randomness. No offense floor. No retreat or tactics (out of scope).
- Existing self-check expectations must stay valid: an undamaged ship's numbers are unchanged everywhere (damage 0 means the old formulas), so no existing assertion is edited unless a task says so.
- Tunables live on `GameAIConstants` with in-code defaults AND a key written into `Assets/GameAIConstantsProductionTypes.asset` (a loaded asset instance keeps old defaults across recompiles): `combatDamageK` 20, `repairFractionPerTurn` 0.1, `hostilityPerShipLost` 1, `lossWindowTurns` 10, `significantLossFraction` 0.3, `significantLossHostilityDrop` 4, `surrenderTruceTurns` 30, `surrenderMidpoint` 0.6, `surrenderSteepness` 0.1.
- Self-check maps need a distinct position per planet and no assertion exactly on a float boundary; use `Near` with a tolerance of 0.01 for combat arithmetic.
- Commit messages end with `Co-Authored-By: Claude Sonnet 5.5 <noreply@anthropic.com>`. Work on the feature branch `ship-combat`; never push or merge unprompted. Every new script's `.meta` is created and committed with it (Task 1 gives the PowerShell helper).
- Unity cannot be run from Claude Code. The compile signal is Rider's `get_file_problems` with `rootFolder: "C:/Projects/FlatSpace"` (it cannot analyse brand-new files and an empty list is not proof). The real check is the user focusing the Unity Editor and running `FlatSpace -> AI -> Run Combat Self-Check` / `Run All AI Self-Checks`. A "RED" step means Rider shows the not-yet-written members as unresolved, or the Editor run fails the new assertion. Editor runs are requested after Task 4 (the first compile of the new combat code) and after Task 8.

## Review Focus

Conditions the spec implies that no obvious test would reach, most likely first (each has a test in the task named):

1. Three players' fleets at one planet: one pool per victim, hostility-weighted shares, no dependence on the order the ships were docked (Task 4).
2. A fleet that leaves wounded must arrive wounded, through departure, flight, `ShipSave` and load; a fleet with a short or missing `Damage` list docks the rest at full health (Task 2).
3. Division by zero and empty cases: a Health stat of 0, a victim with no pool, my strength 0 in the loss share, `max <= 0` (Tasks 1, 4, 7).
4. A truce: a Declare War order refused, a rival's old declaration not forcing a war, Surrender not offered at peace or when winning, War not offered in the truce (Tasks 6 and 7).
5. Colony ships when the owner's last warship dies the same turn (the rule runs after combat), when a rival arrives alone, and on a planet the owner populates (Task 5).

---

## File Structure

| File | Change |
|---|---|
| `Assets/Flatspace/GameAI/GameAIConstants.cs`, `Assets/GameAIConstantsProductionTypes.asset` | modify: "Ship combat" header, nine tunables |
| `Assets/Flatspace/Objects/Ships/Ship.cs` | modify: `Damage` |
| `Assets/Flatspace/GameAI/WarshipStats.cs` | modify: `CurrentHealth`, `EffectiveOffense` (+ `Ship` overloads) |
| `Assets/Flatspace/Objects/Planets/Planet.cs` | modify: `DockShipFromSave` damage, `PeekShipDamage`, `DestroyDockedShip`, two result types |
| `Assets/Flatspace/GameAI/GameAI.cs` | modify: payload `Damage`, arrival, combat call, `Turn`, `OrderTypeSurrender`, `ApplySurrender`, logging calls |
| `Assets/Flatspace/GameAI/CombatSystem.cs` (+ `.meta`) | create: `Resolve`, `CombatLoss`, `ColonyLoss`, `CombatReport` |
| `Assets/Flatspace/GameAI/FleetStrength.cs`, `BlockadeSystem.cs`, `AssaultPlanner.cs`, `GameAIMap.cs`, `UI/MainGameScreenUI/FleetUIController.cs` | modify: effective offense / current health |
| `Assets/Flatspace/SaveSystem/SaveLoadSystem.cs`, `Objects/Board/GameBoard.cs` | modify: `damage`, `truceUntil` |
| `Assets/Flatspace/GameAI/DiplomacyState.cs` | modify: `TruceUntil`, `Turn`, `InTruce`, truce in `IsAtWar`, save entry |
| `Assets/Flatspace/GameAI/HostilityCalculator.cs`, `StanceMatrix.cs`, `PlayerAI.cs` | modify: loss terms, Surrender choice, loss history, order emission |
| `Assets/Flatspace/Diagnostics/AITuningLogger.cs` | modify: `LogCombat`, `LogColonyShipsLost`, `LogLossDrop`, `LogSurrender`, `LogFleetHealth` |
| `Assets/Editor/CombatSelfCheck.cs` (+ `.meta`) | create: the suite |
| `Assets/Editor/DiplomacySelfCheck.cs`, `SimultaneitySelfCheck.cs`, `AllAISelfChecks.cs` | modify: new cases, registration (13 suites) |
| `CLAUDE.md`, `FUTURE_FEATURES.md`, `.claude/skills/tuning-log/SKILL.md` | modify: docs |

---

### Task 1: Tunables, `Ship.Damage`, effective stats

**Files:**
- Modify: `Assets/Flatspace/GameAI/GameAIConstants.cs` (after `stanceHoldTurns`, line 143)
- Modify: `Assets/GameAIConstantsProductionTypes.asset` (after `stanceSteepness: 4`, line 61)
- Modify: `Assets/Flatspace/Objects/Ships/Ship.cs`
- Modify: `Assets/Flatspace/GameAI/WarshipStats.cs`
- Create: `Assets/Editor/CombatSelfCheck.cs` (+ `.meta`)
- Modify: `Assets/Editor/AllAISelfChecks.cs`

**Interfaces:**
- Produces: `Ship.Damage` (float, 0 = full health); `WarshipStats.CurrentHealth(ShipData, ICollection<string>, float damage)`, `WarshipStats.EffectiveOffense(ShipData, ICollection<string>, float damage)`, `WarshipStats.CurrentHealth(Ship)`, `WarshipStats.EffectiveOffense(Ship)`; the nine tunables above; `CombatSelfCheck.Fixture` (private) with `Line()`, `P(name)`, `Colonize(name, player)`, `Ships(planet, owner, count, damage = 0f)`, `War(a, b)`.

- [ ] **Step 0: Create the branch**

```bash
cd /c/Projects/FlatSpace && git switch -c ship-combat && git log -1 --format=%h
```
Expected: on `ship-combat`, at the spec commit `1c190f2` or later.

- [ ] **Step 1: Write the failing check.** Create `Assets/Editor/CombatSelfCheck.cs`:

```csharp
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using FlatSpace.AI;
using Flatspace.Objects.Production;

public static class CombatSelfCheck
{
    [MenuItem("FlatSpace/AI/Run Combat Self-Check")]
    public static void Run() => RunChecks();

    public static bool RunChecks()
    {
        var ok = RunTunableDefaultsCheck();
        ok &= RunEffectiveStatsCheck();
        Debug.Log(ok
            ? "[CombatSelfCheck] ALL PASSED"
            : "[CombatSelfCheck] FAILURES (see errors above)");
        return ok;
    }

    private static bool Check(bool condition, string label)
    {
        if (!condition) Debug.LogError($"[CombatSelfCheck] FAIL: {label}");
        return condition;
    }

    private static bool Near(float a, float b) => Mathf.Abs(a - b) < 0.01f;

    // A(0,0) - B(100,0) - C(200,0) - D(300,0). Diplomacy is switched on (a map a fixture builds is legacy mode otherwise).
    // Template: Offense 10, Health 100, Defense 5; with combatDamageK 20 a hit does 0.8 of its offense.
    private sealed class Fixture : System.IDisposable
    {
        public GameObject MapGo;
        public GameAIMap Map;
        public GameAIConstants Constants;
        public ShipData Template;
        public List<CatalogItem> Research;
        public WarshipStats Stats;

        public static Fixture Line()
        {
            var f = new Fixture();
            f.Template = WarshipSelfCheck.MakeTemplate();
            f.Constants = WarshipSelfCheck.MakeConstants(f.Template);
            f.Research = WarshipSelfCheck.MakeResearch();
            f.Stats = new WarshipStats(f.Research);
            f.MapGo = new GameObject("CombatSelfCheckMap");
            f.Map = f.MapGo.AddComponent<GameAIMap>();
            f.Map.GameAIMapInit(new List<PlanetSpawnData>
            {
                ChokepointSelfCheck.Spawn("A", 0f, 0f, new[] { "B" }),
                ChokepointSelfCheck.Spawn("B", 100f, 0f, new[] { "A", "C" }),
                ChokepointSelfCheck.Spawn("C", 200f, 0f, new[] { "B", "D" }),
                ChokepointSelfCheck.Spawn("D", 300f, 0f, new[] { "C" }),
            }, f.Constants);
            f.Map.Diplomacy.Enabled = true;
            return f;
        }

        public Planet P(string name) => Map.GetPlanet(name);

        public void Colonize(string name, int player)
        {
            var planet = P(name);
            planet.Owner = player;
            planet.Population.Add(new Planet.Inhabitant { Player = player });
        }

        // Docks `count` warships for `owner`; each gets `damage` (0 = full health).
        public void Ships(string planet, int owner, int count, float damage = 0f)
        {
            var p = P(planet);
            var before = p.DockedShips.Count;
            WarshipSelfCheck.DockWarships(p, owner, count);
            for (var i = before; i < p.DockedShips.Count; i++) p.DockedShips[i].Damage = damage;
        }

        public void War(int a, int b) => Map.Diplomacy.SetStance(a, b, Stance.War, 0);

        public void Dispose()
        {
            WarshipSelfCheck.DestroyAll(Research);
            Object.DestroyImmediate(MapGo);
            Object.DestroyImmediate(Constants);
            Object.DestroyImmediate(Template);
        }
    }

    // The nine combat tunables keep their documented in-code defaults.
    public static bool RunTunableDefaultsCheck()
    {
        var ok = true;
        var c = ScriptableObject.CreateInstance<GameAIConstants>();
        try
        {
            ok &= Check(Near(c.combatDamageK, 20f) && Near(c.repairFractionPerTurn, 0.1f), "combatDamageK 20, repairFractionPerTurn 0.1");
            ok &= Check(Near(c.hostilityPerShipLost, 1f) && c.lossWindowTurns == 10, "hostilityPerShipLost 1, lossWindowTurns 10");
            ok &= Check(Near(c.significantLossFraction, 0.3f) && Near(c.significantLossHostilityDrop, 4f),
                "significantLossFraction 0.3, significantLossHostilityDrop 4");
            ok &= Check(c.surrenderTruceTurns == 30 && Near(c.surrenderMidpoint, 0.6f) && Near(c.surrenderSteepness, 0.1f),
                "surrenderTruceTurns 30, surrenderMidpoint 0.6, surrenderSteepness 0.1");
        }
        finally { Object.DestroyImmediate(c); }
        return ok;
    }

    // Effective offense = Offense x current health / Health stat, no floor; damage is stored, current health derived.
    public static bool RunEffectiveStatsCheck()
    {
        var ok = true;
        using (var f = Fixture.Line())
        {
            var none = new List<string>();
            ok &= Check(Near(f.Stats.EffectiveOffense(f.Template, none, 0f), 10f), "undamaged: the plain offense");
            ok &= Check(Near(f.Stats.EffectiveOffense(f.Template, none, 50f), 5f), "half the health, half the offense");
            ok &= Check(Near(f.Stats.EffectiveOffense(f.Template, none, 90f), 1f), "no floor: 10% health is 10% offense");
            ok &= Check(Near(f.Stats.EffectiveOffense(f.Template, none, 100f), 0f), "no health left: no offense");
            ok &= Check(Near(f.Stats.EffectiveOffense(f.Template, none, 150f), 0f), "damage beyond the Health stat never goes negative");
            ok &= Check(Near(f.Stats.CurrentHealth(f.Template, none, 30f), 70f) && Near(f.Stats.CurrentHealth(f.Template, none, 150f), 0f),
                "current health is the Health stat minus damage, never below 0");
            ok &= Check(f.Stats.EffectiveOffense(null, none, 0f) == 0f && f.Stats.CurrentHealth(null, none, 0f) == 0f,
                "a null template has no offense and no health, no crash");

            var zeroHealth = WarshipSelfCheck.MakeTemplate();
            zeroHealth.shipHealth = 0f;
            zeroHealth.shipHealthMax = 0f;
            ok &= Check(f.Stats.EffectiveOffense(zeroHealth, none, 0f) == 0f, "a Health stat of 0 gives 0 offense, no division by zero");
            Object.DestroyImmediate(zeroHealth);

            var hp5 = new List<string> { "Hp 1", "Hp 2", "Hp 3", "Hp 4", "Hp 5" };
            ok &= Check(Near(f.Stats.CurrentHealth(f.Template, hp5, 0f), 200f) && Near(f.Stats.EffectiveOffense(f.Template, hp5, 100f), 5f),
                "the Health stat comes from the ship's research snapshot: 200 max, 100 damage is half the offense");

            f.Ships("A", 0, 1, 25f);
            ok &= Check(Near(f.Stats.EffectiveOffense(f.P("A").DockedShips[0]), 7.5f)
                        && Near(f.Stats.CurrentHealth(f.P("A").DockedShips[0]), 75f),
                "the Ship overloads read the ship's own template, snapshot and Damage");
        }
        return ok;
    }
}
```

- [ ] **Step 2: Register the suite.** In `AllAISelfChecks.cs` add `("Combat", CombatSelfCheck.RunChecks),` after the `Simultaneity` line.

- [ ] **Step 3: Verify RED.** Rider `get_file_problems` on `CombatSelfCheck.cs` (it may say "not included in any project": then the Editor compile is the RED). Expected unresolved: `Ship.Damage`, `EffectiveOffense`, `CurrentHealth`, the nine constants.

- [ ] **Step 4: Implement.**

(a) `GameAIConstants.cs`, after `public int stanceHoldTurns = 10;`:

```csharp

    [Header("Ship combat")]
    // Warships of players at war that share a planet fight once a turn (CombatSystem). A hit does offense x K / (K + the target's
    // Defense). A damaged warship heals repairFractionPerTurn of its Health stat a turn while docked at a planet its owner
    // populates with no at-war warship present.
    public float combatDamageK = 20f;
    public float repairFractionPerTurn = 0.1f;
    // Hostility toward a rival grows by hostilityPerShipLost for each of my warships it destroyed, and falls by
    // significantLossHostilityDrop a turn while the share of my fleet strength it destroyed over the last lossWindowTurns
    // is at least significantLossFraction.
    public float hostilityPerShipLost = 1f;
    public int lossWindowTurns = 10;
    public float significantLossFraction = 0.3f;
    public float significantLossHostilityDrop = 4f;
    // Surrender: a stance choice offered while I am at war and weaker, weighted 1 / (1 + e^(-(loss share - surrenderMidpoint)
    // / surrenderSteepness)). It ends the war for both sides and locks the pair against new declarations (and forced wars)
    // for surrenderTruceTurns.
    public int surrenderTruceTurns = 30;
    public float surrenderMidpoint = 0.6f;
    public float surrenderSteepness = 0.1f;
```

(b) `GameAIConstantsProductionTypes.asset`, after the `  stanceSteepness: 4` line (keep two-space indent):

```
  combatDamageK: 20
  repairFractionPerTurn: 0.1
  hostilityPerShipLost: 1
  lossWindowTurns: 10
  significantLossFraction: 0.3
  significantLossHostilityDrop: 4
  surrenderTruceTurns: 30
  surrenderMidpoint: 0.6
  surrenderSteepness: 0.1
```

(c) `Ship.cs`: add after `ResearchSnapshot`:

```csharp
    // Damage taken (0 = full health). Stored, not current health: an older save loads at full health, and the Health stat
    // (from the research snapshot) is fixed per ship. Current health is WarshipStats.CurrentHealth.
    public float Damage;
```

(d) `WarshipStats.cs`, after `Defense(...)`:

```csharp
        /// <summary>Health left: the Health stat minus the damage taken, never below 0.</summary>
        public float CurrentHealth(ShipData template, ICollection<string> snapshot, float damage)
            => System.Math.Max(0f, Health(template, snapshot) - damage);

        /// <summary>
        /// Offense scaled by how much health is left (Offense x current health / Health stat). No floor, by the owner's
        /// choice; 0 when the Health stat is 0 (colony ships, a ship with no template).
        /// </summary>
        public float EffectiveOffense(ShipData template, ICollection<string> snapshot, float damage)
        {
            var max = Health(template, snapshot);
            if (max <= 0f) return 0f;
            return Offense(template, snapshot) * CurrentHealth(template, snapshot, damage) / max;
        }

        public float CurrentHealth(Ship ship) => CurrentHealth(ship.Template, ship.ResearchSnapshot, ship.Damage);

        public float EffectiveOffense(Ship ship) => EffectiveOffense(ship.Template, ship.ResearchSnapshot, ship.Damage);
```

(e) `.meta` (PowerShell; the GUID is random, the block is what Unity writes for a script):

```powershell
function New-Meta($path) {
  $text = "fileFormatVersion: 2`nguid: $([guid]::NewGuid().ToString('N'))`nMonoImporter:`n  externalObjects: {}`n  serializedVersion: 2`n  defaultReferences: []`n  executionOrder: 0`n  icon: {instanceID: 0}`n  userData: `n  assetBundleName: `n  assetBundleVariant: `n"
  [System.IO.File]::WriteAllText("$path.meta", $text, (New-Object System.Text.UTF8Encoding $false))
}
cd C:\Projects\FlatSpace
New-Meta "Assets\Editor\CombatSelfCheck.cs"
```

- [ ] **Step 5: Verify (Rider).** `get_file_problems` on `Ship.cs`, `WarshipStats.cs`, `GameAIConstants.cs`, `AllAISelfChecks.cs` (the last may still show one unresolved suite symbol until Unity regenerates the project: that is the known limit). No Editor run yet.

- [ ] **Step 6: Commit**

```bash
cd /c/Projects/FlatSpace && git add Assets/Flatspace/GameAI/GameAIConstants.cs Assets/GameAIConstantsProductionTypes.asset Assets/Flatspace/Objects/Ships/Ship.cs Assets/Flatspace/GameAI/WarshipStats.cs Assets/Editor/CombatSelfCheck.cs Assets/Editor/CombatSelfCheck.cs.meta Assets/Editor/AllAISelfChecks.cs && git commit -q -m "$(cat <<'EOF'
feat: ship combat tunables, Ship.Damage and effective stats

Co-Authored-By: Claude Sonnet 5.5 <noreply@anthropic.com>
EOF
)" && git log -1 --oneline
```

---

### Task 2: Damage travels with the ship (payload and saves)

**Files:**
- Modify: `Assets/Flatspace/Objects/Planets/Planet.cs` (`DockShipFromSave` line 986, add `PeekShipDamage` after `PeekShipSnapshots` line 1014)
- Modify: `Assets/Flatspace/GameAI/GameAI.cs` (`ShipFleetPayload` line 73-104, `ApplyShipArrival` line 432)
- Modify: `Assets/Flatspace/GameAI/PlayerAI.cs` (`EmitShipOrders` line ~1768)
- Modify: `Assets/Flatspace/SaveSystem/SaveLoadSystem.cs` (`ShipSave` line 49, planet ship save line 242)
- Modify: `Assets/Flatspace/GameAI/GameAIMap.cs` (`SetPlanetSimulationStats` line 422)
- Test: `Assets/Editor/CombatSelfCheck.cs`

**Interfaces:**
- Consumes: `Ship.Damage` (Task 1), `Fixture`.
- Produces: `Planet.DockShipFromSave(kind, owner, snapshot, float damage = 0f)`; `Planet.PeekShipDamage(kind, owner, count, skip = 0)` returning `List<float>` parallel to `PeekShipSnapshots`; `ShipFleetPayload.Damage` (`List<float>`) and `ShipFleetPayload.DamageAt(int)`; `ShipSave.damage`.

- [ ] **Step 1: Write the failing check.** In `CombatSelfCheck.cs` register `ok &= RunDamageTravelsCheck();` and add (needs `using System;`-free code; `JsonUtility` is in `UnityEngine`):

```csharp
    // Damage travels: it is saved with a docked ship, rides in the fleet payload (ToSave, FromSave, departure, arrival) and a
    // fleet with a short or missing Damage list docks the rest at full health.
    public static bool RunDamageTravelsCheck()
    {
        var ok = true;
        using (var f = Fixture.Line())
        {
            var a = f.P("A");
            a.DockShipFromSave(Ship.ShipKind.WarShip, 0, new List<string>(), 25f);
            ok &= Check(Near(a.DockedShips[0].Damage, 25f), "DockShipFromSave with a damage argument sets Ship.Damage");
            a.DockShipFromSave(Ship.ShipKind.WarShip, 0, new List<string>());
            ok &= Check(Near(a.DockedShips[1].Damage, 0f), "the old three-argument call docks at full health");

            // Peek reads the damage in the same order as the snapshots, with skip.
            var b = f.P("B");
            f.Ships("B", 0, 1, 10f); f.Ships("B", 0, 1, 20f); f.Ships("B", 0, 1, 30f); f.Ships("B", 1, 1, 99f);
            var damages = b.PeekShipDamage(Ship.ShipKind.WarShip, 0, 2);
            ok &= Check(damages.Count == 2 && Near(damages[0], 10f) && Near(damages[1], 20f), "peek gives the first two ships' damage, in dock order");
            var skipped = b.PeekShipDamage(Ship.ShipKind.WarShip, 0, 2, 1);
            ok &= Check(skipped.Count == 2 && Near(skipped[0], 20f) && Near(skipped[1], 30f), "peek with skip 1 gives the next two");
            ok &= Check(b.PeekShipSnapshots(Ship.ShipKind.WarShip, 0, 2).Count == 2, "the snapshot peek is unchanged");

            // The ships that leave are the ships the payload was read from.
            var leave = new GameAI.GameAIOrder { Type = GameAI.GameAIOrder.OrderType.OrderTypeShipDeparture, PlayerId = 0, Data = 2 };
            GameAI.ApplyShipDeparture(b, leave);
            var remaining = b.DockedShips.Where(s => s.Owner == 0).ToList();
            ok &= Check(remaining.Count == 1 && Near(remaining[0].Damage, 30f), "departure removes the first two ships, the third (damage 30) stays");

            // Arrival docks each ship with the damage it left with; a short list docks the rest at full health.
            var arrive = new GameAI.GameAIOrder
            {
                Type = GameAI.GameAIOrder.OrderType.OrderTypeShipTransport,
                PlayerId = 1,
                Data = 2,
                Fleet = new GameAI.GameAIOrder.ShipFleetPayload
                {
                    Kind = Ship.ShipKind.WarShip,
                    Snapshots = new List<List<string>> { new List<string>(), new List<string>() },
                    Damage = new List<float> { 40f, 0f },
                },
            };
            var c = f.P("C");
            GameAI.ApplyShipArrival(c, arrive);
            ok &= Check(c.DockedShips.Count == 2 && Near(c.DockedShips[0].Damage, 40f) && Near(c.DockedShips[1].Damage, 0f),
                "arrival docks the wounded ship wounded (40) and the other at full health");
            arrive.Fleet.Damage = new List<float> { 15f };
            var d = f.P("D");
            GameAI.ApplyShipArrival(d, arrive);
            ok &= Check(d.DockedShips.Count == 2 && Near(d.DockedShips[0].Damage, 15f) && Near(d.DockedShips[1].Damage, 0f),
                "a Damage list shorter than the fleet docks the rest at full health");

            // Payload save round trip, and an older save with no damage.
            var payload = new GameAI.GameAIOrder.ShipFleetPayload
            {
                Kind = Ship.ShipKind.WarShip,
                Snapshots = new List<List<string>> { new List<string> { "x" }, new List<string> { "y" } },
                Damage = new List<float> { 12.5f, 0f },
            };
            var saved = payload.ToSave(3);
            ok &= Check(saved.Count == 2 && Near(saved[0].damage, 12.5f) && Near(saved[1].damage, 0f) && saved[0].owner == 3,
                "ToSave writes each ship's damage");
            var restored = GameAI.GameAIOrder.ShipFleetPayload.FromSave(saved);
            ok &= Check(restored.Damage.Count == 2 && Near(restored.Damage[0], 12.5f) && Near(restored.DamageAt(1), 0f)
                        && Near(restored.DamageAt(7), 0f),
                "FromSave restores the damage list; DamageAt past the end is 0");
            var viaJson = JsonUtility.FromJson<SaveLoadSystem.GameSave.ShipSave>(JsonUtility.ToJson(saved[0]));
            ok &= Check(Near(viaJson.damage, 12.5f), "damage survives JsonUtility");
            var old = JsonUtility.FromJson<SaveLoadSystem.GameSave.ShipSave>("{\"kind\":1,\"owner\":0}");
            ok &= Check(Near(old.damage, 0f), "an older ship save with no damage key loads at full health");
        }

        // EmitShipOrders puts the departing ships' damage in the payload.
        using (var f = Fixture.Line())
        {
            f.Ships("A", 0, 1, 10f); f.Ships("A", 0, 1, 20f); f.Ships("A", 0, 1, 30f);
            var go = new GameObject("CombatSelfCheckPlayer");
            try
            {
                var player = go.AddComponent<Player>();
                var ai = go.AddComponent<PlayerAI>();
                ai.Player = player;
                ai.AIMap = f.Map;
                player.playerID = 0;
                var orders = new List<GameAI.GameAIOrder>();
                ai.EmitShipOrders(new ShipAction { Origin = "A", Target = "B", Cost = 100f, Count = 2, Kind = Ship.ShipKind.WarShip }, orders);
                var transport = orders.Find(o => o.Type == GameAI.GameAIOrder.OrderType.OrderTypeShipTransport);
                ok &= Check(transport != null && transport.Fleet.Damage.Count == 2 && Near(transport.Fleet.Damage[0], 10f)
                            && Near(transport.Fleet.Damage[1], 20f),
                    "a fleet order carries the damage of the ships it takes (10 and 20)");
            }
            finally { Object.DestroyImmediate(go); }
        }
        return ok;
    }
```

- [ ] **Step 2: Verify RED.** Rider on `CombatSelfCheck.cs`: unresolved `PeekShipDamage`, `Damage` (payload), `DamageAt`, `ShipSave.damage`, the four-argument `DockShipFromSave`.

- [ ] **Step 3: Implement.**

(a) `Planet.cs`: replace `DockShipFromSave`:

```csharp
    public void DockShipFromSave(Ship.ShipKind kind, int owner, List<string> researchSnapshot, float damage = 0f)
    {
        var ship = CreateShip(kind, owner);
        ship.ResearchSnapshot = new List<string>(researchSnapshot);
        ship.Damage = damage;
        DockedShips.Add(ship);
    }
```
and add after `PeekShipSnapshots`:

```csharp
    // The damage of the same ships PeekShipSnapshots returns, in the same order, so a fleet's payload keeps each ship's wounds.
    public List<float> PeekShipDamage(Ship.ShipKind kind, int owner, int count, int skip = 0)
    {
        return DockedShips
            .Where(s => s.Kind == kind && s.Owner == owner)
            .Skip(skip)
            .Take(count)
            .Select(s => s.Damage)
            .ToList();
    }
```

(b) `GameAI.cs`, in `ShipFleetPayload`: add the field and helper, and write damage in `ToSave`, read it in `FromSave`:

```csharp
                    public List<float> Damage = new List<float>();   // one per snapshot; a short or empty list means full health

                    public float DamageAt(int index) => index >= 0 && index < Damage.Count ? Damage[index] : 0f;
```
`ToSave`: replace the `Select` with an indexed one:
```csharp
                        return Snapshots.Select((snapshot, i) => new SaveLoadSystem.GameSave.ShipSave
                        {
                            kind = Kind,
                            owner = owner,
                            researchSnapshot = new List<string>(snapshot),
                            damage = DamageAt(i),
                        }).ToList();
```
`FromSave`: add `Damage = ships.Select(s => s.damage).ToList(),` inside the object initializer. In `ApplyShipArrival` change the docking line to `target.DockShipFromSave(kind, order.PlayerId, order.Fleet.Snapshots[i], order.Fleet.DamageAt(i));`.

(c) `PlayerAI.EmitShipOrders`: after the `snapshots` check, `var fleet = new ... { Kind = action.Kind, Snapshots = snapshots, Damage = origin.PeekShipDamage(action.Kind, Player.playerID, action.Count, skipShips) };`.

(d) `SaveLoadSystem.cs`: add `public float damage;` to `ShipSave` (after `researchSnapshot`), and `damage = ship.Damage,` in the planet save loop (line ~242-247). `GameAIMap.SetPlanetSimulationStats`: `planet.DockShipFromSave(shipSave.kind, shipSave.owner, shipSave.researchSnapshot, shipSave.damage);`.

(e) `AssaultPlanner.Plan` (the loop at line ~407-418) reads damage too: after `snapshots` add `var damages = _map.GetPlanet(source.Name).PeekShipDamage(Ship.ShipKind.WarShip, _playerId, source.Remaining, skip);` and iterate by index, using `_stats.EffectiveOffense(template, snapshots[i], damages[i])` (Task 3 finishes the effective-offense switch; do this one here because it reads the same peek).

- [ ] **Step 4: Verify (Rider)** on `Planet.cs`, `GameAI.cs`, `PlayerAI.cs`, `SaveLoadSystem.cs`, `GameAIMap.cs`, `AssaultPlanner.cs`.

- [ ] **Step 5: Commit**

```bash
cd /c/Projects/FlatSpace && git add Assets/Flatspace/Objects/Planets/Planet.cs Assets/Flatspace/GameAI/GameAI.cs Assets/Flatspace/GameAI/PlayerAI.cs Assets/Flatspace/SaveSystem/SaveLoadSystem.cs Assets/Flatspace/GameAI/GameAIMap.cs Assets/Flatspace/GameAI/AssaultPlanner.cs Assets/Editor/CombatSelfCheck.cs && git commit -q -m "$(cat <<'EOF'
feat: damage travels with a ship (payload, arrival, saves)

Co-Authored-By: Claude Sonnet 5.5 <noreply@anthropic.com>
EOF
)" && git log -1 --oneline
```

---

### Task 3: Every offense reader uses effective offense

**Files:**
- Modify: `Assets/Flatspace/GameAI/FleetStrength.cs`, `BlockadeSystem.cs`, `AssaultPlanner.cs` (lines 142, 167), `GameAIMap.cs` (line 356)
- Modify: `Assets/Flatspace/UI/MainGameScreenUI/FleetUIController.cs` (`FormatShipRow`)
- Test: `Assets/Editor/CombatSelfCheck.cs`

**Interfaces:**
- Consumes: `WarshipStats.EffectiveOffense/CurrentHealth` (Task 1), `ShipFleetPayload.DamageAt` (Task 2).
- Produces: strength, blockade value, assault sizing, in-flight offense and the fleet row all reflect damage (undamaged ships unchanged).

- [ ] **Step 1: Write the failing check.** Register `ok &= RunEffectiveReadersCheck();` and add:

```csharp
    // Every reader of a ship's offense sees the effective (damaged) value. Undamaged ships are unchanged (the old checks pin that).
    public static bool RunEffectiveReadersCheck()
    {
        var ok = true;
        using (var f = Fixture.Line())
        {
            f.Ships("A", 0, 1);             // Off 10, health 100, Def 5: strength 10 x 105 = 1050
            f.Ships("A", 0, 1, 50f);        // Off 5, health 50, Def 5: strength 5 x 55 = 275
            ok &= Check(Near(FleetStrength.Of(f.P("A").DockedShips.Where(s => s.Owner == 0), f.Stats), 1325f),
                "fleet strength = effective offense x (current health + Defense): 1050 + 275");

            var blockade = new BlockadeSystem(f.Map, f.Research);
            ok &= Check(Near(blockade.DockedOffense(f.P("A"), 0), 15f), "blockade value counts effective offense: 10 + 5");

            var planner = new AssaultPlanner(f.Map, 0, null, f.Stats);
            ok &= Check(Near(planner.CommittedOffense(f.P("A")), 15f), "the assault's committed offense counts effective offense");

            var orders = new List<GameAI.GameAIOrder>
            {
                new GameAI.GameAIOrder
                {
                    Type = GameAI.GameAIOrder.OrderType.OrderTypeShipTransport,
                    PlayerId = 0,
                    Data = 2,
                    Target = "B",
                    Fleet = new GameAI.GameAIOrder.ShipFleetPayload
                    {
                        Kind = Ship.ShipKind.WarShip,
                        Snapshots = new List<List<string>> { new List<string>(), new List<string>() },
                        Damage = new List<float> { 50f, 0f },
                    },
                },
            };
            f.Map.RecomputeIncomingOffense(orders, f.Stats);
            ok &= Check(Near(f.P("B").GetIncomingOffense(Ship.ShipKind.WarShip, 0), 15f),
                "in-flight offense uses the payload's damage: 5 + 10");

            f.Template.shipName = "Warship";
            ok &= Check(FleetUIController.FormatShipRow(f.P("A").DockedShips[1], f.Research)
                        == "WarShip - Warship (Spd 150, HP 50/100, Off 5/10, Def 5)",
                "a damaged ship's fleet row shows current/maximum health and effective/base offense");
            ok &= Check(FleetUIController.FormatShipRow(f.P("A").DockedShips[0], f.Research)
                        == "WarShip - Warship (Spd 150, HP 100, Off 10, Def 5)",
                "an undamaged ship's row is unchanged");
        }
        return ok;
    }
```

- [ ] **Step 2: Verify RED.** The check compiles after Task 2, so RED is the Editor run failing (strength would read 2100 instead of 1325, and so on). If Rider is the only signal, RED is "no production line changed yet": confirm by reading the five sites listed in Step 3 still call `Offense(`.

- [ ] **Step 3: Implement.**
  - `FleetStrength.Of`: `var offense = stats.EffectiveOffense(ship); var durability = stats.CurrentHealth(ship) + stats.Defense(ship.Template, ship.ResearchSnapshot);`.
  - `BlockadeSystem.DockedOffense`: `sum += _stats.EffectiveOffense(ship);`.
  - `AssaultPlanner.DockedOffense` (line 142): `sum += _stats.EffectiveOffense(ship);`. `ContestedHolds` (line 167): `_stats.EffectiveOffense(s) > 0f`. (`Plan`'s loop was switched in Task 2.)
  - `GameAIMap.RecomputeIncomingOffense` (line 356): `var offense = order.Fleet.Snapshots.Select((snapshot, i) => stats.EffectiveOffense(template, snapshot, order.Fleet.DamageAt(i))).Sum();` (add `using System.Linq;` if missing).
  - `FleetUIController.FormatShipRow`: compute `maxHp = stats.Health(...)`, `hp = stats.CurrentHealth(ship)`, `baseOff = stats.Offense(...)`, `off = stats.EffectiveOffense(ship)`. Print `HP {hp:0.#}` when `ship.Damage <= 0f`, else `HP {hp:0.#}/{maxHp:0.#}`; likewise `Off {off:0.#}` or `Off {off:0.#}/{baseOff:0.#}`.

- [ ] **Step 4: Verify (Rider)** on the five files, and that `WarshipSelfCheck`'s existing `FormatShipRow` strings are unchanged for undamaged ships (they are: damage 0 prints the old format).

- [ ] **Step 5: Commit**

```bash
cd /c/Projects/FlatSpace && git add Assets/Flatspace/GameAI/FleetStrength.cs Assets/Flatspace/GameAI/BlockadeSystem.cs Assets/Flatspace/GameAI/AssaultPlanner.cs Assets/Flatspace/GameAI/GameAIMap.cs Assets/Flatspace/UI/MainGameScreenUI/FleetUIController.cs Assets/Editor/CombatSelfCheck.cs && git commit -q -m "$(cat <<'EOF'
feat: strength, blockade, assault and fleet row use effective offense

Co-Authored-By: Claude Sonnet 5.5 <noreply@anthropic.com>
EOF
)" && git log -1 --oneline
```

---

### Task 4: `CombatSystem` resolution

**Files:**
- Create: `Assets/Flatspace/GameAI/CombatSystem.cs` (+ `.meta`)
- Modify: `Assets/Flatspace/Objects/Planets/Planet.cs` (two result types, priority cases, `DestroyDockedShip`)
- Modify: `Assets/Flatspace/GameAI/GameAI.cs` (`GameAIUpdate`: stats, `Diplomacy.Turn`, combat call, logging), `Assets/Flatspace/Diagnostics/AITuningLogger.cs`
- Test: `Assets/Editor/CombatSelfCheck.cs`

**Interfaces:**
- Consumes: `WarshipStats.EffectiveOffense/CurrentHealth/Defense/Health`, `DiplomacyState.IsAtWar(me, rival, true)`, `DiplomacyState.Hostility(me, rival)`, the tunables.
- Produces: `CombatSystem.Resolve(GameAIMap map, WarshipStats stats, GameAIConstants constants, int turn, List<Planet.PlanetUpdateResult> results)` returning `List<CombatReport>`; `CombatLoss { int Attacker; float Ships; float StrengthLost; }`; `ColonyLoss { int Count; int ByPlayer; }`; `CombatReport { string Planet; int Attacker; int Victim; float DamageDealt; float ShipsDestroyed; }`; `PlanetUpdateResultType.WarshipsLost`, `ColonyShipsLost`; `Planet.DestroyDockedShip(Ship)`; `AITuningLogger.LogCombat(int turn, int attacker, string planet, int victim, float damage, float ships)`.

- [ ] **Step 1: Write the failing check.** Register `ok &= RunCombatResolutionCheck();` and add (the helper `Resolve` runs one turn):

```csharp
    private static List<Planet.PlanetUpdateResult> Results() => new List<Planet.PlanetUpdateResult>();

    private static List<CombatReport> Resolve(Fixture f, List<Planet.PlanetUpdateResult> results)
        => CombatSystem.Resolve(f.Map, f.Stats, f.Constants, 1, results);

    private static float DamageOf(Fixture f, string planet, int owner, int index)
        => f.P(planet).DockedShips.Where(s => s.Owner == owner).ElementAt(index).Damage;

    // Who fights, how much damage lands, which ship it lands on, simultaneity, the hostility-weighted split, one pool per victim.
    public static bool RunCombatResolutionCheck()
    {
        var ok = true;

        using (var f = Fixture.Line())    // peace: nothing happens
        {
            f.Ships("A", 0, 2); f.Ships("A", 1, 2);
            var results = Results();
            ok &= Check(Resolve(f, results).Count == 0 && results.Count == 0 && DamageOf(f, "A", 0, 0) == 0f,
                "players at peace do not fight");
        }

        using (var f = Fixture.Line())    // war 3 against 2: simultaneous, first ship takes it all (factor 20/25 = 0.8)
        {
            f.Ships("A", 0, 3); f.Ships("A", 1, 2); f.War(0, 1);
            var results = Results();
            var reports = Resolve(f, results);
            ok &= Check(Near(DamageOf(f, "A", 1, 0), 24f) && Near(DamageOf(f, "A", 1, 1), 0f),
                "player 0's pool of 30 lands on player 1's first ship: 30 x 0.8 = 24");
            ok &= Check(Near(DamageOf(f, "A", 0, 0), 16f) && Near(DamageOf(f, "A", 0, 1), 0f),
                "simultaneous: player 1's pool of 20 lands on player 0's first ship: 16");
            ok &= Check(results.Count == 0, "no ship died, so no loss result");
            ok &= Check(reports.Count == 2 && Near(reports.First(r => r.Attacker == 0).DamageDealt, 24f)
                        && Near(reports.First(r => r.Attacker == 1).DamageDealt, 16f),
                "one report per directed pair with the damage dealt");
        }

        using (var f = Fixture.Line())    // focus fire: most damaged first, overflow carries, a dying ship still fires
        {
            f.Ships("A", 0, 10);
            f.Ships("A", 1, 1, 90f);      // dock index 0: health 10
            f.Ships("A", 1, 1);           // dock index 1: health 100
            f.War(0, 1);
            var results = Results();
            Resolve(f, results);
            var survivors = f.P("A").DockedShips.Where(s => s.Owner == 1).ToList();
            ok &= Check(survivors.Count == 1 && Near(survivors[0].Damage, 70f),
                "pool 100: the weakest ship (needs 10 / 0.8 = 12.5) dies, the rest 87.5 x 0.8 = 70 lands on the next");
            ok &= Check(Near(DamageOf(f, "A", 0, 0), 8.8f),
                "the dying ship still fired this turn: 1 + 10 = 11 offense, x 0.8 = 8.8 on player 0's first ship");
            var loss = results.Where(r => r.Result == Planet.PlanetUpdateResult.PlanetUpdateResultType.PlanetUpdateResultTypeWarshipsLost).ToList();
            ok &= Check(loss.Count == 1 && loss[0].PlayerID == 1 && loss[0].Name == "A", "one WarshipsLost result, victim player 1, at A");
            var data = loss.Count == 1 ? (CombatLoss)loss[0].Data : null;
            ok &= Check(data != null && data.Attacker == 0 && Near(data.Ships, 1f) && Near(data.StrengthLost, 15f),
                "attacker 0, 1 ship, strength 15 (effective offense 1 x (health 10 + Defense 5))");
        }

        using (var f = Fixture.Line())    // defense: Defense 15 gives 20/35
        {
            f.Ships("A", 0, 3);
            f.P("A").DockShipFromSave(Ship.ShipKind.WarShip, 1, new List<string> { "Def 1", "Def 2", "Def 3", "Def 4", "Def 5" });
            f.War(0, 1);
            Resolve(f, Results());
            ok &= Check(Near(DamageOf(f, "A", 1, 0), 30f * 20f / 35f), "Defense 15 lowers a hit to 20/35 of its offense");
        }

        foreach (var reversedDock in new[] { false, true })    // three players: hostility-weighted split, one pool per victim
        {
            using (var f = Fixture.Line())
            {
                foreach (var owner in reversedDock ? new[] { 2, 1, 0 } : new[] { 0, 1, 2 }) f.Ships("A", owner, 1);
                f.War(0, 1); f.War(0, 2);
                var h01 = f.Map.Diplomacy.Get(0, 1); h01.Hostility = 30f; f.Map.Diplomacy.Set(0, 1, h01);
                var h02 = f.Map.Diplomacy.Get(0, 2); h02.Hostility = 10f; f.Map.Diplomacy.Set(0, 2, h02);
                Resolve(f, Results());
                var tag = reversedDock ? " (docked in reverse order)" : "";
                ok &= Check(Near(DamageOf(f, "A", 1, 0), 6f), "player 0's pool of 10 splits 30:10, so player 1 takes 7.5 x 0.8 = 6" + tag);
                ok &= Check(Near(DamageOf(f, "A", 2, 0), 2f), "and player 2 takes 2.5 x 0.8 = 2" + tag);
                ok &= Check(Near(DamageOf(f, "A", 0, 0), 16f),
                    "one pool per victim: players 1 and 2 are at war with 0 only, their 10 + 10 land together: 20 x 0.8 = 16" + tag);
            }
        }

        using (var f = Fixture.Line())    // attribution: the victim's loss is shared by each attacker's part of its pool
        {
            f.Ships("A", 0, 3); f.Ships("A", 2, 1); f.Ships("A", 1, 1, 95f);
            f.War(0, 1); f.War(2, 1);
            var results = Results();
            Resolve(f, results);
            var losses = results.Where(r => r.Result == Planet.PlanetUpdateResult.PlanetUpdateResultType.PlanetUpdateResultTypeWarshipsLost)
                .OrderBy(r => ((CombatLoss)r.Data).Attacker).ToList();
            ok &= Check(losses.Count == 2 && ((CombatLoss)losses[0].Data).Attacker == 0 && ((CombatLoss)losses[1].Data).Attacker == 2,
                "one loss result per attacker");
            ok &= Check(Near(((CombatLoss)losses[0].Data).Ships, 0.75f) && Near(((CombatLoss)losses[1].Data).Ships, 0.25f),
                "the one ship lost is shared 30:10 = 0.75 and 0.25");
            ok &= Check(Near(((CombatLoss)losses[0].Data).StrengthLost, 3.75f) && Near(((CombatLoss)losses[1].Data).StrengthLost, 1.25f),
                "its strength 0.5 x (5 + 5) = 5 is shared the same way");
        }

        using (var f = Fixture.Line())    // legacy mode: no stances, no combat
        {
            f.Ships("A", 0, 2); f.Ships("A", 1, 2); f.War(0, 1);
            f.Map.Diplomacy.Enabled = false;
            var results = Results();
            ok &= Check(Resolve(f, results).Count == 0 && DamageOf(f, "A", 1, 0) == 0f && results.Count == 0, "legacy mode: combat is off");
        }

        using (var f = Fixture.Line())    // ownerless ships neither fire nor take fire
        {
            f.Ships("A", 0, 1); f.Ships("A", 1, 1); f.Ships("A", Planet.NoOwner, 5); f.War(0, 1);
            Resolve(f, Results());
            ok &= Check(Near(DamageOf(f, "A", 1, 0), 8f), "ownerless ships add nothing to a pool: 10 x 0.8 = 8");
            ok &= Check(Near(DamageOf(f, "A", Planet.NoOwner, 0), 0f), "and take no damage");
        }

        using (var f = Fixture.Line())    // a Health stat of 0: no division by zero, takes no part
        {
            f.Ships("A", 0, 1); f.Ships("A", 1, 1); f.Ships("A", 1, 1); f.War(0, 1);
            var zero = WarshipSelfCheck.MakeTemplate();
            zero.shipHealth = 0f; zero.shipHealthMax = 0f;
            f.P("A").DockedShips.Where(s => s.Owner == 1).Last().Template = zero;
            var results = Results();
            Resolve(f, results);
            ok &= Check(Near(DamageOf(f, "A", 1, 0), 8f) && Near(DamageOf(f, "A", 1, 1), 0f),
                "the ship with no Health stat is never targeted and the real ship takes the 8");
            Object.DestroyImmediate(zero);
        }
        return ok;
    }
```

- [ ] **Step 2: Verify RED.** Rider: `CombatSystem`, `CombatLoss`, `CombatReport`, the result types are unresolved.

- [ ] **Step 3: Implement.**

(a) `Planet.cs`: append the two result types after `PlanetUpdateResultTypeColonizerReady` (add a comma):

```csharp
            PlanetUpdateResultTypeColonizerReady,
            // Appended last: serialized as an int. Data is a CombatLoss / ColonyLoss (CombatSystem); PlayerID is the victim.
            PlanetUpdateResultTypeWarshipsLost,
            PlanetUpdateResultTypeColonyShipsLost
```
and in the priority switch (line ~74-79) add both to the `Priority = High` group (`case ResultType.PlanetUpdateResultTypeWarshipsLost: case ResultType.PlanetUpdateResultTypeColonyShipsLost:`). Add beside `UndockShips`:

```csharp
    // Removes one specific docked ship (a ship destroyed in combat).
    public bool DestroyDockedShip(Ship ship)
    {
        if (!DockedShips.Remove(ship)) return false;
        DestroyShipComponent(ship);
        return true;
    }
```

(b) Create `Assets/Flatspace/GameAI/CombatSystem.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Linq;

namespace FlatSpace
{
    namespace AI
    {
        /// <summary>The loss one attacker caused a victim at a planet this turn (carried in a WarshipsLost result's Data).</summary>
        public class CombatLoss
        {
            public int Attacker;
            public float Ships;           // the attacker's share of the ships destroyed
            public float StrengthLost;    // the same share of the destroyed ships' strength at the start of the turn
        }

        /// <summary>Colony ships destroyed at a planet (carried in a ColonyShipsLost result's Data).</summary>
        public class ColonyLoss
        {
            public int Count;
            public int ByPlayer;
        }

        /// <summary>One line of the tuning log: what one attacker dealt to one victim at a planet this turn.</summary>
        public struct CombatReport
        {
            public string Planet;
            public int Attacker;
            public int Victim;
            public float DamageDealt;
            public float ShipsDestroyed;
        }

        /// <summary>
        /// Docked warships of players at war that share a planet fight once a turn: deterministic, simultaneous, focus fire
        /// on the most damaged ship, damage persistent on the ship (Ship.Damage). Pure (no Gameboard.Instance). Off in legacy
        /// mode (no stances to fight on). Appends WarshipsLost and ColonyShipsLost results, like Planet.UpdatePlanet does.
        /// </summary>
        public static class CombatSystem
        {
            private struct Unit
            {
                public Ship Ship;
                public int Owner;
                public int Index;       // dock order
                public float Health;
                public float Max;
                public float Defense;
                public float Offense;   // effective
                public float Strength;  // offense x (health + Defense)
            }

            public static List<CombatReport> Resolve(GameAIMap map, WarshipStats stats, GameAIConstants constants, int turn,
                List<Planet.PlanetUpdateResult> results)
            {
                var reports = new List<CombatReport>();
                if (map == null || stats == null || !map.Diplomacy.Enabled) return reports;
                foreach (var planet in map.PlanetList)
                {
                    FightAt(planet, map, stats, constants, results, reports);
                    Repair(planet, map, stats, constants);
                    DestroyStrandedColonyShips(planet, map, results);
                }
                return reports;
            }

            private static void FightAt(Planet planet, GameAIMap map, WarshipStats stats, GameAIConstants constants,
                List<Planet.PlanetUpdateResult> results, List<CombatReport> reports)
            {
                // Start-of-turn snapshot of every warship that can fight (a ship with no health left, or no Health stat, cannot).
                var units = new List<Unit>();
                for (var i = 0; i < planet.DockedShips.Count; i++)
                {
                    var ship = planet.DockedShips[i];
                    if (ship.Kind != Ship.ShipKind.WarShip || ship.Owner == Planet.NoOwner) continue;
                    var health = stats.CurrentHealth(ship);
                    if (health <= 0f) continue;
                    var offense = stats.EffectiveOffense(ship);
                    var defense = stats.Defense(ship.Template, ship.ResearchSnapshot);
                    units.Add(new Unit
                    {
                        Ship = ship, Owner = ship.Owner, Index = i, Health = health,
                        Max = stats.Health(ship.Template, ship.ResearchSnapshot),
                        Defense = defense, Offense = offense, Strength = offense * (health + defense),
                    });
                }
                var players = units.Select(u => u.Owner).Distinct().OrderBy(p => p).ToList();
                if (players.Count < 2) return;

                // pools[victim][attacker]: the offense an attacker aims at a victim (hostility-weighted over its war rivals).
                var pools = new Dictionary<int, Dictionary<int, float>>();
                foreach (var attacker in players)
                {
                    var pool = units.Where(u => u.Owner == attacker).Sum(u => u.Offense);
                    if (pool <= 0f) continue;
                    var targets = players.Where(v => v != attacker && map.Diplomacy.IsAtWar(attacker, v, true)).ToList();
                    if (targets.Count == 0) continue;
                    var weights = targets.ToDictionary(v => v, v => Math.Max(1f, map.Diplomacy.Hostility(attacker, v)));
                    var total = weights.Values.Sum();
                    foreach (var victim in targets)
                    {
                        if (!pools.ContainsKey(victim)) pools[victim] = new Dictionary<int, float>();
                        pools[victim][attacker] = pool * weights[victim] / total;
                    }
                }

                var damage = new Dictionary<Ship, float>();
                var destroyed = new List<Ship>();
                foreach (var victim in pools.Keys.OrderBy(v => v))
                {
                    var contributions = pools[victim];
                    var pool = contributions.Values.Sum();    // one pool per victim: the order of the attackers cannot matter
                    var lostShips = 0;
                    var lostStrength = 0f;
                    var dealt = 0f;
                    var ordered = units.Where(u => u.Owner == victim).OrderBy(u => u.Health / u.Max).ThenBy(u => u.Index);
                    foreach (var target in ordered)
                    {
                        if (pool <= 0f) break;
                        var factor = constants.combatDamageK / (constants.combatDamageK + target.Defense);
                        var needed = target.Health / factor;
                        if (pool >= needed)
                        {
                            destroyed.Add(target.Ship);
                            lostShips++;
                            lostStrength += target.Strength;
                            dealt += target.Health;
                            pool -= needed;
                        }
                        else
                        {
                            damage[target.Ship] = (damage.TryGetValue(target.Ship, out var d) ? d : 0f) + pool * factor;
                            dealt += pool * factor;
                            pool = 0f;
                        }
                    }

                    var totalContribution = contributions.Values.Sum();
                    foreach (var attacker in contributions.Keys.OrderBy(a => a))
                    {
                        var share = contributions[attacker] / totalContribution;
                        reports.Add(new CombatReport
                        {
                            Planet = planet.PlanetName, Attacker = attacker, Victim = victim,
                            DamageDealt = dealt * share, ShipsDestroyed = lostShips * share,
                        });
                        if (lostShips > 0)
                            results.Add(new Planet.PlanetUpdateResult(planet.PlanetName,
                                Planet.PlanetUpdateResult.PlanetUpdateResultType.PlanetUpdateResultTypeWarshipsLost,
                                new CombatLoss { Attacker = attacker, Ships = lostShips * share, StrengthLost = lostStrength * share },
                                victim));
                    }
                }

                foreach (var pair in damage) pair.Key.Damage += pair.Value;    // applied after every side was computed
                foreach (var ship in destroyed) planet.DestroyDockedShip(ship);
            }

            // Task 5 fills these two in.
            private static void Repair(Planet planet, GameAIMap map, WarshipStats stats, GameAIConstants constants) { }

            private static void DestroyStrandedColonyShips(Planet planet, GameAIMap map, List<Planet.PlanetUpdateResult> results) { }
        }
    }
}
```
(The two empty methods are the Task 5 seams, not placeholders for this task's tests.)

(c) `GameAI.cs`: at the top of `GameAIUpdate` add `GameAIMap.Diplomacy.Turn = Gameboard.Instance.TurnNumber;` is added in Task 6; for now, after `planetUpdateResults.Clear();` insert `RunCombat(planetUpdateResults);` and add:

```csharp
            // Docked warships of players at war fight before the planets update, so this turn's losses reach ProcessResults.
            private void RunCombat(List<Planet.PlanetUpdateResult> results)
            {
                var stats = new WarshipStats(BlockadeSystem.ResearchItemsFrom(Gameboard.Instance.players));
                var turn = Gameboard.Instance.TurnNumber;
                foreach (var report in CombatSystem.Resolve(GameAIMap, stats, GameAIMap.GameAIConstants, turn, results))
                    AITuningLogger.LogCombat(turn, report.Attacker, report.Planet, report.Victim, report.DamageDealt,
                        report.ShipsDestroyed);
            }
```

(d) `AITuningLogger.cs` (beside `LogStrategyChange`):

```csharp
    /// <summary>Combat at a planet: T&lt;turn&gt;|P&lt;attacker&gt;|Combat|planet|attacker-&gt;victim|damageDealt|shipsDestroyed (ships may be fractional: the attacker's share).</summary>
    public static void LogCombat(int turnNumber, int attacker, string planet, int victim, float damage, float ships)
    {
        if (_currentLogPath == null) return;
        var ci = System.Globalization.CultureInfo.InvariantCulture;
        AppendLines(new List<string> { FormatLine(turnNumber, attacker, "Combat", planet, $"{attacker}->{victim}",
            damage.ToString("0.#", ci), ships.ToString("0.##", ci)) });
    }
```
and in `LogPlanetEvents`' switch add a case for `PlanetUpdateResultTypeColonyShipsLost` (Task 5 uses it): `var loss = (ColonyLoss)result.Data; lines.Add(FormatLine(turnNumber, result.PlayerID, "ColonyShipsLost", result.Name, result.PlayerID.ToString(), loss.Count.ToString(), loss.ByPlayer.ToString()));` (add `using FlatSpace.AI;` if `ColonyLoss` does not resolve).

- [ ] **Step 4: Create the `.meta`** with the Task 1 helper: `New-Meta "Assets\Flatspace\GameAI\CombatSystem.cs"`.

- [ ] **Step 5: Verify (Rider)** on `Planet.cs`, `GameAI.cs`, `AITuningLogger.cs`, `CombatSelfCheck.cs`. Then ask the user to focus the Unity Editor and run `FlatSpace -> AI -> Run Combat Self-Check` and `Run All AI Self-Checks`. Expected: Combat passes, all 13 suites pass. Any compile error: fix and ask again.

- [ ] **Step 6: Commit**

```bash
cd /c/Projects/FlatSpace && git add Assets/Flatspace/GameAI/CombatSystem.cs Assets/Flatspace/GameAI/CombatSystem.cs.meta Assets/Flatspace/Objects/Planets/Planet.cs Assets/Flatspace/GameAI/GameAI.cs Assets/Flatspace/Diagnostics/AITuningLogger.cs Assets/Editor/CombatSelfCheck.cs && git commit -q -m "$(cat <<'EOF'
feat: CombatSystem resolves docked fights (focus fire, simultaneous, persistent damage)

Co-Authored-By: Claude Sonnet 5.5 <noreply@anthropic.com>
EOF
)" && git log -1 --oneline
```

---

### Task 5: Repair and the colony-ship rule

**Files:**
- Modify: `Assets/Flatspace/GameAI/CombatSystem.cs` (the two seams)
- Test: `Assets/Editor/CombatSelfCheck.cs`

**Interfaces:**
- Consumes: `CombatSystem.Resolve` (Task 4), `Planet.UndockShips(kind, owner, count)`, `ColonyLoss`, `repairFractionPerTurn`.
- Produces: repair and `ColonyShipsLost` results.

- [ ] **Step 1: Write the failing check.** Register `ok &= RunRepairAndColonyShipCheck();` and add:

```csharp
    // Repair: gradual, at home, not with an at-war enemy present. Colony ships: destroyed when the owner has no warship left
    // and an at-war rival has one, everywhere, after combat.
    public static bool RunRepairAndColonyShipCheck()
    {
        var ok = true;

        using (var f = Fixture.Line())    // repair at home: 50 - 0.1 x 100 = 40
        {
            f.Colonize("A", 0);
            f.Ships("A", 0, 1, 50f);
            Resolve(f, Results());
            ok &= Check(Near(DamageOf(f, "A", 0, 0), 40f), "a damaged ship at its owner's populated planet heals 10% of its Health stat a turn");
            Resolve(f, Results()); Resolve(f, Results()); Resolve(f, Results()); Resolve(f, Results());
            Resolve(f, Results());
            ok &= Check(Near(DamageOf(f, "A", 0, 0), 0f), "and never below 0 damage");
        }

        using (var f = Fixture.Line())    // no repair away from home
        {
            f.Colonize("A", 1);            // someone else's planet
            f.Ships("A", 0, 1, 50f);
            Resolve(f, Results());
            ok &= Check(Near(DamageOf(f, "A", 0, 0), 50f), "no repair at a planet the owner does not hold");
        }
        using (var f = Fixture.Line())
        {
            f.P("A").Owner = 0;             // owned but unpopulated
            f.Ships("A", 0, 1, 50f);
            Resolve(f, Results());
            ok &= Check(Near(DamageOf(f, "A", 0, 0), 50f), "no repair at an unpopulated planet");
        }

        using (var f = Fixture.Line())    // an at-war enemy present: no repair (the enemy's 8 damage lands, nothing heals)
        {
            f.Colonize("A", 0);
            f.Ships("A", 0, 1, 50f); f.Ships("A", 1, 1); f.War(0, 1);
            Resolve(f, Results());
            ok &= Check(Near(DamageOf(f, "A", 0, 0), 58f), "with an at-war enemy docked there: 50 + 8 and no healing");
        }
        using (var f = Fixture.Line())    // a peaceful rival does not stop repair
        {
            f.Colonize("A", 0);
            f.Ships("A", 0, 1, 50f); f.Ships("A", 1, 1);
            Resolve(f, Results());
            ok &= Check(Near(DamageOf(f, "A", 0, 0), 40f), "a rival at peace does not stop repair");
        }

        using (var f = Fixture.Line())    // colony ship, owner has no warship, at-war rival arrives alone: destroyed, even on its own planet
        {
            f.Colonize("A", 0);
            f.P("A").DockShipFromSave(Ship.ShipKind.ColonyShip, 0, new List<string>());
            f.Ships("A", 1, 1); f.War(0, 1);
            var results = Results();
            Resolve(f, results);
            var lost = results.Where(r => r.Result == Planet.PlanetUpdateResult.PlanetUpdateResultType.PlanetUpdateResultTypeColonyShipsLost).ToList();
            ok &= Check(f.P("A").DockedShips.All(s => s.Kind != Ship.ShipKind.ColonyShip),
                "the owner's colony ship is destroyed when its planet holds no warship of the owner and an at-war rival has one");
            ok &= Check(lost.Count == 1 && lost[0].PlayerID == 0 && ((ColonyLoss)lost[0].Data).Count == 1 && ((ColonyLoss)lost[0].Data).ByPlayer == 1,
                "one ColonyShipsLost result: owner 0, 1 ship, by player 1");
        }
        using (var f = Fixture.Line())    // the owner keeps a warship: the colony ship lives
        {
            f.P("A").DockShipFromSave(Ship.ShipKind.ColonyShip, 0, new List<string>());
            f.Ships("A", 0, 1); f.Ships("A", 1, 1); f.War(0, 1);
            Resolve(f, Results());
            ok &= Check(f.P("A").DockedShips.Any(s => s.Kind == Ship.ShipKind.ColonyShip), "a surviving warship protects the colony ship");
        }
        using (var f = Fixture.Line())    // the owner's last warship dies in the same turn: the rule runs after combat
        {
            f.P("A").DockShipFromSave(Ship.ShipKind.ColonyShip, 0, new List<string>());
            f.Ships("A", 0, 1, 95f); f.Ships("A", 1, 3); f.War(0, 1);
            Resolve(f, Results());
            ok &= Check(f.P("A").DockedShips.All(s => s.Owner != 0), "the last warship and then the colony ship are gone in the same turn");
        }
        using (var f = Fixture.Line())    // peace, or only a rival colony ship, or legacy: nothing happens
        {
            f.P("A").DockShipFromSave(Ship.ShipKind.ColonyShip, 0, new List<string>());
            f.Ships("A", 1, 1);
            Resolve(f, Results());
            ok &= Check(f.P("A").DockedShips.Any(s => s.Kind == Ship.ShipKind.ColonyShip && s.Owner == 0), "a rival at peace does not kill colony ships");
            f.War(0, 1);
            f.P("A").UndockShips(Ship.ShipKind.WarShip, 1, 9);
            f.P("A").DockShipFromSave(Ship.ShipKind.ColonyShip, 1, new List<string>());
            Resolve(f, Results());
            ok &= Check(f.P("A").DockedShips.Count(s => s.Kind == Ship.ShipKind.ColonyShip) == 2, "a rival's colony ship is not a warship: nothing dies");
            f.P("A").DockShipFromSave(Ship.ShipKind.WarShip, 1, new List<string>());
            f.Map.Diplomacy.Enabled = false;
            Resolve(f, Results());
            ok &= Check(f.P("A").DockedShips.Count(s => s.Kind == Ship.ShipKind.ColonyShip) == 2, "legacy mode: nothing dies");
        }
        return ok;
    }
```

- [ ] **Step 2: Verify RED.** The seams are empty: the Editor run fails the new assertions (and Rider shows no unresolved symbol, since the API exists). Note this in the ledger.

- [ ] **Step 3: Implement** the two seams in `CombatSystem.cs`:

```csharp
            // After combat, a damaged warship heals repairFractionPerTurn of its Health stat while docked at a planet its owner
            // populates with no warship of a player it is at war with there.
            private static void Repair(Planet planet, GameAIMap map, WarshipStats stats, GameAIConstants constants)
            {
                if (constants.repairFractionPerTurn <= 0f) return;
                foreach (var ship in planet.DockedShips)
                {
                    if (ship.Kind != Ship.ShipKind.WarShip || ship.Owner == Planet.NoOwner || ship.Damage <= 0f) continue;
                    if (planet.Owner != ship.Owner || planet.Population.Count == 0) continue;
                    var enemyHere = planet.DockedShips.Any(s => s.Kind == Ship.ShipKind.WarShip && s.Owner != Planet.NoOwner
                        && s.Owner != ship.Owner && map.Diplomacy.IsAtWar(ship.Owner, s.Owner, true));
                    if (enemyHere) continue;
                    var max = stats.Health(ship.Template, ship.ResearchSnapshot);
                    ship.Damage = Math.Max(0f, ship.Damage - constants.repairFractionPerTurn * max);
                }
            }

            // After combat and repair: a player with colony ships here but no warship, where a player at war with it has a
            // warship, loses its colony ships. Everywhere, a planet the owner populates included.
            private static void DestroyStrandedColonyShips(Planet planet, GameAIMap map, List<Planet.PlanetUpdateResult> results)
            {
                var owners = planet.DockedShips.Where(s => s.Kind == Ship.ShipKind.ColonyShip && s.Owner != Planet.NoOwner)
                    .Select(s => s.Owner).Distinct().OrderBy(o => o).ToList();
                foreach (var owner in owners)
                {
                    if (planet.DockedShips.Any(s => s.Kind == Ship.ShipKind.WarShip && s.Owner == owner)) continue;
                    var raiders = planet.DockedShips
                        .Where(s => s.Kind == Ship.ShipKind.WarShip && s.Owner != Planet.NoOwner && s.Owner != owner
                                    && map.Diplomacy.IsAtWar(owner, s.Owner, true))
                        .Select(s => s.Owner).Distinct().OrderBy(o => o).ToList();
                    if (raiders.Count == 0) continue;
                    var count = planet.UndockShips(Ship.ShipKind.ColonyShip, owner, int.MaxValue);
                    results.Add(new Planet.PlanetUpdateResult(planet.PlanetName,
                        Planet.PlanetUpdateResult.PlanetUpdateResultType.PlanetUpdateResultTypeColonyShipsLost,
                        new ColonyLoss { Count = count, ByPlayer = raiders[0] }, owner));
                }
            }
```
Remove the "Task 5 fills these two in" comment.

- [ ] **Step 4: Verify (Rider)** on `CombatSystem.cs`. The Editor run comes after Task 8 (one run was requested after Task 4; this task's cases are exercised then).

- [ ] **Step 5: Commit**

```bash
cd /c/Projects/FlatSpace && git add Assets/Flatspace/GameAI/CombatSystem.cs Assets/Editor/CombatSelfCheck.cs && git commit -q -m "$(cat <<'EOF'
feat: repair at home and the colony-ship rule

Co-Authored-By: Claude Sonnet 5.5 <noreply@anthropic.com>
EOF
)" && git log -1 --oneline
```

---

### Task 6: Truce and surrender orders

**Files:**
- Modify: `Assets/Flatspace/GameAI/DiplomacyState.cs`
- Modify: `Assets/Flatspace/GameAI/GameAI.cs` (`OrderType`, `ApplyStanceOrder`, `ApplySurrender`, `ExecuteOrder`, `GameAIUpdate` sets `Diplomacy.Turn`)
- Modify: `Assets/Flatspace/SaveSystem/SaveLoadSystem.cs` (`StanceSave`)
- Modify: `Assets/Flatspace/Diagnostics/AITuningLogger.cs` (`LogSurrender`)
- Test: `Assets/Editor/DiplomacySelfCheck.cs`

**Interfaces:**
- Consumes: `DiplomacyState` (existing), the surrender tunables.
- Produces: `DiplomacyState.Pair.TruceUntil`, `DiplomacyState.Pair.LossShare` and `.PSurrender` (log-only), `DiplomacyState.Entry.TruceUntil`, `DiplomacyState.Turn` (set each turn by `GameAI`; read by `IsAtWar`), `DiplomacyState.InTruce(int a, int b, int turn)`, `OrderType.OrderTypeSurrender`, `public static bool GameAI.ApplySurrender(DiplomacyState diplomacy, GameAIOrder order, int turn, int truceTurns)`, `StanceSave.truceUntil`, `AITuningLogger.LogSurrender(int turn, int me, int rival, float lossShare, float pSurrender, int truceUntil)`.

- [ ] **Step 1: Write the failing checks.** In `DiplomacySelfCheck.cs` register `ok &= RunTruceAndSurrenderOrderCheck();` and add (uses the existing `Check`, `Near`, `Fixture`):

```csharp
    // A surrender ends the war for both sides and locks the pair: no Declare War, no forced war, until the truce ends.
    public static bool RunTruceAndSurrenderOrderCheck()
    {
        var ok = true;
        GameAI.GameAIOrder Order(GameAI.GameAIOrder.OrderType type, int me, int rival) => new GameAI.GameAIOrder
        {
            Type = type,
            TimingType = GameAI.GameAIOrder.OrderTimingType.OrderTimingTypeImmediate,
            Data = rival,
            Origin = string.Empty,
            Target = string.Empty,
            PlayerId = me,
        };

        var d = new DiplomacyState { Enabled = true };
        d.SetStance(0, 1, Stance.War, 5);
        d.SetStance(1, 0, Stance.War, 5);                       // both declared
        ok &= Check(GameAI.ApplySurrender(d, Order(GameAI.GameAIOrder.OrderType.OrderTypeSurrender, 0, 1), 20, 30),
            "a surrender reports a change");
        ok &= Check(d.StanceToward(0, 1) == Stance.Peace && d.StanceToward(1, 0) == Stance.Peace,
            "both stances go to Peace (a one-sided surrender ends the war for both)");
        ok &= Check(d.Get(0, 1).TruceUntil == 50 && d.Get(1, 0).TruceUntil == 50, "both pair rows are locked until turn 20 + 30");
        d.Turn = 30;
        ok &= Check(d.InTruce(0, 1, 30) && d.InTruce(1, 0, 30), "turn 30 is inside the truce, from either side");
        ok &= Check(!d.InTruce(0, 1, 50) && !d.InTruce(0, 2, 30), "the truce ends at turn 50 and covers only that pair");

        ok &= Check(!GameAI.ApplyStanceOrder(d, Order(GameAI.GameAIOrder.OrderType.OrderTypeDeclareWar, 1, 0), 30),
            "a Declare War inside the truce is refused");
        ok &= Check(d.StanceToward(1, 0) == Stance.Peace, "and the stance stays Peace");
        ok &= Check(GameAI.ApplyStanceOrder(d, Order(GameAI.GameAIOrder.OrderType.OrderTypeMakePeace, 1, 0), 30) == false,
            "Make Peace still works (nothing to change: already Peace)");
        ok &= Check(GameAI.ApplyStanceOrder(d, Order(GameAI.GameAIOrder.OrderType.OrderTypeDeclareWar, 1, 0), 50),
            "after the truce a Declare War is accepted again");

        // A rival's earlier declaration must not force a war during the truce.
        var forced = new DiplomacyState { Enabled = true };
        forced.SetStance(1, 0, Stance.War, 5);
        forced.Turn = 6;
        ok &= Check(forced.IsAtWar(0, 1, true), "precondition: without a truce player 1's declaration forces player 0 into war");
        GameAI.ApplySurrender(forced, Order(GameAI.GameAIOrder.OrderType.OrderTypeSurrender, 0, 1), 6, 30);
        forced.SetStance(1, 0, Stance.War, 7);                  // a stale declaration set again inside the truce
        forced.Turn = 7;
        ok &= Check(!forced.IsAtWar(0, 1, true) && !forced.IsAtWar(1, 0, true), "inside a truce the pair is not at war, whatever the stances say");
        ok &= Check(!forced.WarRivals(0, new[] { 1 }).Contains(1), "and WarRivals leaves the truce partner out");
        forced.Turn = 36;
        ok &= Check(forced.IsAtWar(0, 1, true), "once the truce ends the stale declaration counts again");

        // Saves: the truce survives a snapshot and an older entry loads with none.
        var snap = d.Snapshot(0);
        var back = new DiplomacyState();
        back.Restore(0, snap);
        ok &= Check(back.Get(0, 1).TruceUntil == 50, "TruceUntil survives Snapshot and Restore");
        var entry = SaveLoadSystem.GameSave.StanceSave.From(new DiplomacyState.Entry { Rival = 1, Stance = Stance.Peace, TruceUntil = 77 }).ToEntry();
        ok &= Check(entry.TruceUntil == 77, "StanceSave carries truceUntil");
        var old = JsonUtility.FromJson<SaveLoadSystem.GameSave.StanceSave>("{\"rival\":1,\"stance\":1,\"hostility\":10,\"lastChangeTurn\":3}");
        ok &= Check(old.truceUntil == 0, "an older stance save loads with no truce");
        return ok;
    }
```
(`DiplomacySelfCheck.cs` already has `using UnityEngine;`.)

- [ ] **Step 2: Verify RED.** Rider: `TruceUntil`, `Turn`, `InTruce`, `ApplySurrender`, `OrderTypeSurrender`, `truceUntil` unresolved.

- [ ] **Step 3: Implement.**

(a) `DiplomacyState.cs`: add `public int TruceUntil;` and log-only `public float LossShare, PSurrender;` to `Pair` (the log-only line beside `CutsTerm`); add `public int TruceUntil;` to `Entry`; carry it in `Snapshot` (`TruceUntil = kv.Value.TruceUntil`) and `Restore` (`TruceUntil = entry.TruceUntil`). Add the turn and the truce:

```csharp
            /// <summary>The current turn, set by GameAI each turn (and by self-checks): the truce is tested against it.</summary>
            public int Turn { get; set; }

            /// <summary>True while either side of the pair carries a truce that has not ended at `turn`.</summary>
            public bool InTruce(int a, int b, int turn) => turn < Get(a, b).TruceUntil || turn < Get(b, a).TruceUntil;
```
and change `IsAtWar` to be false inside a truce:

```csharp
            public bool IsAtWar(int me, int rival, bool meHasContactWithRival)
                => !InTruce(me, rival, Turn)
                   && (StanceToward(me, rival) == Stance.War
                       || (meHasContactWithRival && StanceToward(rival, me) == Stance.War));
```

(b) `GameAI.cs`: append `OrderTypeSurrender` after `OrderTypeMakePeace` (add a comma; comment: immediate, `Data` = the rival's id, executed by `ApplySurrender`). `ApplyStanceOrder` refuses a War inside a truce:

```csharp
            public static bool ApplyStanceOrder(DiplomacyState diplomacy, GameAIOrder order, int turn)
            {
                var rival = Convert.ToInt32(order.Data);
                var stance = order.Type == GameAIOrder.OrderType.OrderTypeDeclareWar ? Stance.War : Stance.Peace;
                if (stance == Stance.War && diplomacy.InTruce(order.PlayerId, rival, turn)) return false;
                return diplomacy.SetStance(order.PlayerId, rival, stance, turn);
            }

            // Immediate: the player surrenders to the rival in order.Data. Both stances go to Peace and both pair rows are locked
            // against new declarations (and forced wars) until turn + truceTurns. Pure: no Gameboard.Instance.
            public static bool ApplySurrender(DiplomacyState diplomacy, GameAIOrder order, int turn, int truceTurns)
            {
                var me = order.PlayerId;
                var rival = Convert.ToInt32(order.Data);
                diplomacy.SetStance(me, rival, Stance.Peace, turn);
                diplomacy.SetStance(rival, me, Stance.Peace, turn);
                foreach (var key in new[] { (me, rival), (rival, me) })
                {
                    var pair = diplomacy.Get(key.Item1, key.Item2);
                    pair.TruceUntil = turn + truceTurns;
                    diplomacy.Set(key.Item1, key.Item2, pair);
                }
                return true;
            }
```
`ExecuteOrder`: add

```csharp
                    case GameAIOrder.OrderType.OrderTypeSurrender:
                    {
                        var surrenderTurn = Gameboard.Instance.TurnNumber;
                        var surrenderRival = Convert.ToInt32(executableOrder.Data);
                        ApplySurrender(GameAIMap.Diplomacy, executableOrder, surrenderTurn, GameAIMap.GameAIConstants.surrenderTruceTurns);
                        var surrenderPair = GameAIMap.Diplomacy.Get(executableOrder.PlayerId, surrenderRival);
                        AITuningLogger.LogSurrender(surrenderTurn, executableOrder.PlayerId, surrenderRival, surrenderPair.LossShare,
                            surrenderPair.PSurrender, surrenderPair.TruceUntil);
                        break;
                    }
```
and at the very top of `GameAIUpdate()` add `GameAIMap.Diplomacy.Turn = Gameboard.Instance.TurnNumber;`.

(c) `SaveLoadSystem.StanceSave`: add `public int truceUntil;`; `From` sets `truceUntil = e.TruceUntil`; `ToEntry` sets `TruceUntil = truceUntil`.

(d) `AITuningLogger.cs`:

```csharp
    /// <summary>A surrender: T&lt;turn&gt;|P&lt;surrenderer&gt;|Surrender|rival|lossShare|pSurrender|truceUntil.</summary>
    public static void LogSurrender(int turnNumber, int me, int rival, float lossShare, float pSurrender, int truceUntil)
    {
        if (_currentLogPath == null) return;
        var ci = System.Globalization.CultureInfo.InvariantCulture;
        AppendLines(new List<string> { FormatLine(turnNumber, me, "Surrender", rival.ToString(ci), lossShare.ToString("0.##", ci),
            pSurrender.ToString("0.###", ci), truceUntil.ToString(ci)) });
    }
```

- [ ] **Step 4: Fix the existing self-check helper.** In `DiplomacySelfCheck.Turn(...)` (and `SimultaneitySelfCheck` where it applies orders) set `f.Map.Diplomacy.Turn = turn;` first, so truce-aware `IsAtWar` reads the right turn (truces are 0 in every older case, so nothing else changes).

- [ ] **Step 5: Verify (Rider)** on `DiplomacyState.cs`, `GameAI.cs`, `SaveLoadSystem.cs`, `AITuningLogger.cs`, `DiplomacySelfCheck.cs`.

- [ ] **Step 6: Commit**

```bash
cd /c/Projects/FlatSpace && git add Assets/Flatspace/GameAI/DiplomacyState.cs Assets/Flatspace/GameAI/GameAI.cs Assets/Flatspace/SaveSystem/SaveLoadSystem.cs Assets/Flatspace/Diagnostics/AITuningLogger.cs Assets/Editor/DiplomacySelfCheck.cs Assets/Editor/SimultaneitySelfCheck.cs && git commit -q -m "$(cat <<'EOF'
feat: surrender order and the truce lock

Co-Authored-By: Claude Sonnet 5.5 <noreply@anthropic.com>
EOF
)" && git log -1 --oneline
```

---

### Task 7: Loss terms, the Surrender choice, `LossDrop` and `FleetHealth`

**Files:**
- Modify: `Assets/Flatspace/GameAI/HostilityCalculator.cs`, `StanceMatrix.cs`, `PlayerAI.cs`
- Modify: `Assets/Flatspace/GameAI/GameAI.cs` (`LogEconomySummary`), `Assets/Flatspace/Diagnostics/AITuningLogger.cs`
- Test: `Assets/Editor/DiplomacySelfCheck.cs`

**Interfaces:**
- Consumes: `CombatLoss` results (Task 4), `DiplomacyState.TruceUntil/InTruce/Turn` (Task 6), `OrderTypeSurrender`, the loss and surrender tunables, `PlayerAI.WarRivals()`.
- Produces: `HostilityCalculator.Inputs.ShipsLost` (float) and `.LossShare` (float), `Result.LossTerm`; `StanceMatrix.Row.LossShare/MyStrength/RivalStrength/AtWar/Truce`, `StanceMatrix.Decision.Surrender` (bool), `StanceMatrix.SurrenderWeight(float lossShare, GameAIConstants)`; `PlayerAI.RecordLosses(List<Planet.PlanetUpdateResult> results, int turn)` (public), `PlayerAI.LossShareToward(int rival, int turn, float myStrength)` (public); `AITuningLogger.LogLossDrop(...)`, `LogFleetHealth(...)`.

- [ ] **Step 1: Write the failing checks.** In `DiplomacySelfCheck.cs` register `ok &= RunLossTermsCheck();` and `ok &= RunSurrenderChoiceCheck();` and add:

```csharp
    private static Planet.PlanetUpdateResult Loss(int victim, int attacker, float ships, float strength)
        => new Planet.PlanetUpdateResult("A",
            Planet.PlanetUpdateResult.PlanetUpdateResultType.PlanetUpdateResultTypeWarshipsLost,
            new CombatLoss { Attacker = attacker, Ships = ships, StrengthLost = strength }, victim);

    // Each ship lost adds hostilityPerShipLost; a significant share of my strength lost over the window pulls hostility down;
    // the share is lost / (current + lost) over the last lossWindowTurns, 0 when both are 0.
    public static bool RunLossTermsCheck()
    {
        var ok = true;
        var c = ScriptableObject.CreateInstance<GameAIConstants>();
        try
        {
            var plain = HostilityCalculator.Compute(new HostilityCalculator.Inputs { Previous = 10f, ShipsLost = 3f, LossShare = 0.1f }, c);
            ok &= Check(Near(plain.Hostility, 10f * 0.95f + 3f) && Near(plain.LossTerm, 3f),
                "3 ships lost add 3 (hostilityPerShipLost 1); a 10% loss share is below the 30% threshold");
            var big = HostilityCalculator.Compute(new HostilityCalculator.Inputs { Previous = 20f, ShipsLost = 2f, LossShare = 0.5f }, c);
            ok &= Check(Near(big.Hostility, 20f * 0.95f + 2f - 4f) && Near(big.LossTerm, -2f),
                "a 50% loss share (>= 30%) subtracts 4 a turn: 19 + 2 - 4");
            var floor = HostilityCalculator.Compute(new HostilityCalculator.Inputs { Previous = 1f, LossShare = 0.9f }, c);
            ok &= Check(Near(floor.Hostility, 0f), "hostility is clamped at 0");
            var none = HostilityCalculator.Compute(new HostilityCalculator.Inputs { Previous = 10f }, c);
            ok &= Check(Near(none.Hostility, 9.5f) && Near(none.LossTerm, 0f), "no losses: the old formula (decay only)");
        }
        finally { Object.DestroyImmediate(c); }

        using (var f = WithRival())
        {
            f.AI.RecordLosses(new List<Planet.PlanetUpdateResult> { Loss(0, 1, 2f, 100f), Loss(5, 1, 9f, 999f), Loss(0, 1, 1f, 50f) }, 10);
            ok &= Check(Near(f.AI.LossShareToward(1, 10, 350f), 150f / 500f),
                "share = lost 150 / (current 350 + lost 150) = 0.3; another player's loss result is ignored");
            ok &= Check(Near(f.AI.LossShareToward(1, 19, 350f), 0.3f), "turn 19 is still inside a 10-turn window opened at turn 10");
            ok &= Check(Near(f.AI.LossShareToward(1, 20, 350f), 0f), "turn 20 is outside it: the entries are pruned");
            ok &= Check(Near(f.AI.LossShareToward(2, 10, 350f), 0f), "no losses to a rival: 0");
            f.AI.RecordLosses(new List<Planet.PlanetUpdateResult> { Loss(0, 1, 1f, 80f) }, 30);
            ok &= Check(Near(f.AI.LossShareToward(1, 30, 0f), 1f), "my strength 0 with something lost: 1");
            ok &= Check(Near(f.AI.LossShareToward(3, 30, 0f), 0f), "both 0: 0, no division by zero");
        }
        return ok;
    }

    // Surrender is a third choice: offered only while I am at war with the rival, weaker, and not in a truce; weight is a
    // logistic of the loss share; it needs no hold; War is not offered in a truce.
    public static bool RunSurrenderChoiceCheck()
    {
        var ok = true;
        var c = ScriptableObject.CreateInstance<GameAIConstants>();
        try
        {
            // Deterministic roulette: a very steep stance curve and surrender curve, and no stickiness, so at hostility 100 with the
            // War stance held the War weight (1 x 0) and the Peace weight (1 - 1) are both 0 and only Surrender has weight.
            c.stanceSteepness = 0.01f; c.stanceMidpoint = 30f; c.stanceStickiness = 0f; c.surrenderSteepness = 0.01f;
            ok &= Check(Near(StanceMatrix.SurrenderWeight(0.6f, c), 0.5f), "at the midpoint (0.6) the surrender weight is 0.5");
            ok &= Check(StanceMatrix.SurrenderWeight(0.9f, c) > 0.9f && StanceMatrix.SurrenderWeight(0.1f, c) < 0.01f,
                "a heavy loss share is near 1, a light one near 0");

            StanceMatrix.Row Row(float hostility, Stance current, int sinceChange, float lossShare, bool atWar, float mine, float rival, bool truce = false)
                => new StanceMatrix.Row
                {
                    Rival = 1, Hostility = hostility, Current = current, TurnsSinceChange = sinceChange,
                    LossShare = lossShare, AtWar = atWar, MyStrength = mine, RivalStrength = rival, Truce = truce,
                };

            // Offered: at war, weaker, a huge loss share. Hostility 100 with the War stance held: War weight 1 x 0, Peace weight 0,
            // Surrender weight 1.0, so Surrender is the only choice with weight.
            var surrender = StanceMatrix.Decide(0, new List<StanceMatrix.Row> { Row(100f, Stance.War, 100, 0.99f, true, 10f, 100f) }, c);
            ok &= Check(surrender.Count == 1 && surrender[0].Surrender, "at war, weaker, nearly wiped out: Surrender");

            // Not offered: winning, not at war, or in a truce (the decision is then Peace or War, never Surrender).
            var winning = StanceMatrix.Decide(0, new List<StanceMatrix.Row> { Row(100f, Stance.War, 100, 0.99f, true, 100f, 10f) }, c);
            ok &= Check(winning.Count == 1 && !winning[0].Surrender, "never when my strength is not below the rival's");
            var peace = StanceMatrix.Decide(0, new List<StanceMatrix.Row> { Row(100f, Stance.Peace, 100, 0.99f, false, 10f, 100f) }, c);
            ok &= Check(peace.Count == 1 && !peace[0].Surrender, "never when I am not at war with the rival");
            var truce = StanceMatrix.Decide(0, new List<StanceMatrix.Row> { Row(100f, Stance.War, 100, 0.99f, true, 10f, 100f, truce: true) }, c);
            ok &= Check(truce.Count == 1 && !truce[0].Surrender, "never inside a truce (the pair is not at war)");

            // A truce removes War: even at hostility 100 the matrix does not pick War.
            var noWar = StanceMatrix.Decide(0, new List<StanceMatrix.Row> { Row(100f, Stance.Peace, 100, 0f, false, 100f, 100f, truce: true) }, c);
            ok &= Check(noWar.Count == 1 && noWar[0].Stance == Stance.Peace, "inside a truce War is never offered, whatever the hostility");

            // No hold: a row inside the stance hold can still surrender, and otherwise stays held (no decision).
            var heldSurrender = StanceMatrix.Decide(0, new List<StanceMatrix.Row> { Row(0f, Stance.War, 1, 0.99f, true, 10f, 100f) }, c);
            ok &= Check(heldSurrender.Count == 1 && heldSurrender[0].Surrender, "a surrender needs no hold: a stance changed one turn ago can still be surrendered");
            var held = StanceMatrix.Decide(0, new List<StanceMatrix.Row> { Row(100f, Stance.Peace, 1, 0f, false, 10f, 100f) }, c);
            ok &= Check(held.Count == 0, "a held row with nothing to surrender is left alone, as before");
        }
        finally { Object.DestroyImmediate(c); }

        // End to end: PlayerAI emits OrderTypeSurrender, stores the numbers for the log line, and the order executes.
        using (var f = WithRival())
        {
            f.Constants.surrenderSteepness = 0.01f;
            f.Constants.surrenderMidpoint = 0.6f;
            f.Constants.stanceStickiness = 0f;                   // deterministic roulette: War weight 0 (see the cases above)
            var hot = f.Map.Diplomacy.Get(0, 1); hot.Hostility = 100f; f.Map.Diplomacy.Set(0, 1, hot);   // pWar 1, so Peace weight 0
            f.Ships("A", 0, 1);                                  // my strength 1050
            f.Ships("B", 1, 3);                                  // theirs 3150 (player 1 holds B, visible to me)
            f.Map.Diplomacy.SetStance(0, 1, Stance.War, 0);
            f.AI.Strategy = PlayerAI.AIStrategy.AIStrategyAmass;
            f.AI.RecordLosses(new List<Planet.PlanetUpdateResult> { Loss(0, 1, 5f, 9000f) }, 20);   // share 9000 / (1050 + 9000) = 0.9
            var orders = new List<GameAI.GameAIOrder>();
            f.Map.Diplomacy.Turn = 20;
            f.AI.UpdateDiplomacy(20, orders);
            var surrenderOrder = orders.FirstOrDefault(o => o.Type == GameAI.GameAIOrder.OrderType.OrderTypeSurrender);
            ok &= Check(surrenderOrder != null && System.Convert.ToInt32(surrenderOrder.Data) == 1 && surrenderOrder.PlayerId == 0,
                "a beaten, weaker player at war emits a Surrender order against the rival");
            var pair = f.Map.Diplomacy.Get(0, 1);
            ok &= Check(Near(pair.LossShare, 0.9f) && pair.PSurrender > 0.9f, "the loss share and surrender probability are stored for the Surrender log line");
            ok &= Check(f.Map.Diplomacy.StanceToward(0, 1) == Stance.War, "UpdateDiplomacy itself still writes no stance");
            GameAI.ApplySurrender(f.Map.Diplomacy, surrenderOrder, 20, f.Constants.surrenderTruceTurns);
            f.Map.Diplomacy.Turn = 21;
            f.AI.ApplyWarState(21);
            ok &= Check(f.Map.Diplomacy.StanceToward(0, 1) == Stance.Peace && !f.AI.WarRivals().Contains(1)
                        && f.AI.Strategy == PlayerAI.AIStrategy.AIStrategyConsolidate,
                "after the order the war is over and Amass returns to Consolidate");
        }
        return ok;
    }
```
(`DiplomacySelfCheck`'s `Fixture` has `Ships` and `Know`; `WithRival()` leaves player 1 on B. Add `using FlatSpace.AI;` is already there.)

- [ ] **Step 2: Verify RED.** Rider: `ShipsLost`, `LossShare`, `LossTerm`, `RecordLosses`, `LossShareToward`, `SurrenderWeight`, `Row.AtWar`, `Decision.Surrender` unresolved.

- [ ] **Step 3: Implement.**

(a) `HostilityCalculator`: `Inputs` gains `public float ShipsLost; public float LossShare;`; `Result` gains `public float LossTerm;`; `Compute`:

```csharp
                var loss = input.ShipsLost * constants.hostilityPerShipLost;
                var drop = input.LossShare >= constants.significantLossFraction ? -constants.significantLossHostilityDrop : 0f;
                var hostility = input.Previous * (1f - constants.hostilityDecay) + cuts + near + strength + loss + drop;
                return new Result
                {
                    Hostility = Mathf.Clamp(hostility, 0f, constants.hostilityMax),
                    CutsTerm = cuts,
                    NearTerm = near,
                    StrengthTerm = strength,
                    LossTerm = loss + drop,
                };
```

(b) `StanceMatrix`: `Row` gains `public float LossShare; public bool AtWar; public float MyStrength; public float RivalStrength; public bool Truce;`; `Decision` gains `public bool Surrender;`; `StanceChoiceElement` gains `public bool IsSurrender;` (included in `Equals` and `GetHashCode`: `Rival * 31 + (int)Stance + (IsSurrender ? 1000 : 0)`); `StanceAction` gains `public bool IsSurrender;`. Add:

```csharp
            public static float SurrenderWeight(float lossShare, GameAIConstants constants)
            {
                var steepness = Math.Max(constants.surrenderSteepness, 0.0001f);
                return (float)(1.0 / (1.0 + Math.Exp(-(lossShare - constants.surrenderMidpoint) / steepness)));
            }

            // Offered only while I am at war with the rival, weaker than it, and not in a truce.
            private static bool CanSurrender(Row row) => row.AtWar && !row.Truce && row.MyStrength < row.RivalStrength;
```
Inside a truce the War choice is left out of the row altogether (a row with only the Peace choice, never `WarWeight` 0: a `ScoreMatrix` row whose weights are all 0 picks uniformly, so a zero-weight War could still be picked). In `Decide`: for a row inside the hold (`TurnsSinceChange < stanceHoldTurns`): if `CanSurrender(row)` add a row with two choices (`Surrender` weight `SurrenderWeight`, and the current stance as `Stance = row.Current` with weight `1 - SurrenderWeight`); else `continue` as before. For a free row add the Surrender choice (weight `SurrenderWeight`, `IsSurrender = true`, `Stance = Stance.Peace`) as a third choice when `CanSurrender(row)`. The final `Select` sets `Surrender = a.IsSurrender` (the action factory copies `IsSurrender`); keep `pWar[...]` set for every row added.

(c) `PlayerAI`: add

```csharp
            // Per rival, the warships it destroyed (turn, ships, strength) over the last lossWindowTurns. Player-private and not
            // saved: a load starts the window empty (like the pending blockade cuts).
            private readonly Dictionary<int, List<(int turn, float ships, float strength)>> _losses
                = new Dictionary<int, List<(int turn, float ships, float strength)>>();
            // Rivals whose significant-loss drop was logged as Start (log-only, so a load logs each current one once more).
            private readonly HashSet<int> _lossDropLogged = new HashSet<int>();

            /// <summary>Reads this turn's WarshipsLost results for my own player (the list is shared by every player).</summary>
            public void RecordLosses(List<Planet.PlanetUpdateResult> results, int turn)
            {
                foreach (var result in results)
                {
                    if (result.Result != Planet.PlanetUpdateResult.PlanetUpdateResultType.PlanetUpdateResultTypeWarshipsLost) continue;
                    if (result.PlayerID != Player.playerID || !(result.Data is CombatLoss loss)) continue;
                    if (!_losses.TryGetValue(loss.Attacker, out var list))
                        _losses[loss.Attacker] = list = new List<(int, float, float)>();
                    list.Add((turn, loss.Ships, loss.StrengthLost));
                }
            }

            /// <summary>lost / (current + lost) over the window, 0 when both are 0; prunes entries older than the window.</summary>
            public float LossShareToward(int rival, int turn, float myStrength)
            {
                if (!_losses.TryGetValue(rival, out var list)) return 0f;
                var window = AIMap.GameAIConstants.lossWindowTurns;
                list.RemoveAll(e => turn - e.turn >= window);
                var lost = list.Sum(e => e.strength);
                var total = myStrength + lost;
                return total <= 0f ? 0f : lost / total;
            }

            private float ShipsLostThisTurn(int rival, int turn)
                => _losses.TryGetValue(rival, out var list) ? list.Where(e => e.turn == turn).Sum(e => e.ships) : 0f;
```
In `ProcessResults`, first line after the `TryEnterConsolidate`/`ApplyWarState` calls: `RecordLosses(results, Gameboard.Instance != null ? Gameboard.Instance.TurnNumber : 0);`. In `UpdateDiplomacy`: get `var warRivals = WarRivals();` once near the top (committed stances); for each contact rival compute `var share = LossShareToward(rival, turn, myStrength); var shipsLost = ShipsLostThisTurn(rival, turn);` pass `ShipsLost = shipsLost, LossShare = share` into `HostilityCalculator.Inputs`, and set the row's `LossShare = share, AtWar = warRivals.Contains(rival), MyStrength = myStrength, RivalStrength = rivalStrength, Truce = diplomacy.InTruce(me, rival, turn)`; the lost-contact rows get `AtWar = true`, `LossShare = LossShareToward(rival, turn, myStrength)`, `MyStrength = myStrength`, `RivalStrength = float.MaxValue`, `Truce = diplomacy.InTruce(...)`. The `LossDrop` log: per rival compute `significant = share >= constants.significantLossFraction`; if `significant && _lossDropLogged.Add(rival)` call `AITuningLogger.LogLossDrop(turn, me, rival, true, share)`; if `!significant && _lossDropLogged.Remove(rival)` call it with false. In the decision loop, when `decision.Surrender`: store `pair.LossShare` (the row's) and `pair.PSurrender = StanceMatrix.SurrenderWeight(row.LossShare, constants)` on the pair and emit `MakeOrder(OrderTypeSurrender, Immediate, 0, 0, decision.Rival, string.Empty, string.Empty)`; otherwise the existing War/Peace emission (guarded by `StanceToward != decision.Stance`).

(d) `AITuningLogger`:

```csharp
    /// <summary>The significant-loss hostility drop started or ended: T&lt;turn&gt;|P&lt;id&gt;|LossDrop|rival|Start or End|lossShare.</summary>
    public static void LogLossDrop(int turnNumber, int me, int rival, bool started, float lossShare)
    {
        if (_currentLogPath == null) return;
        var ci = System.Globalization.CultureInfo.InvariantCulture;
        AppendLines(new List<string> { FormatLine(turnNumber, me, "LossDrop", rival.ToString(ci), started ? "Start" : "End",
            lossShare.ToString("0.##", ci)) });
    }

    /// <summary>Every 25 turns per player: T&lt;turn&gt;|P&lt;id&gt;|FleetHealth|warships|damaged|meanHealthPct.</summary>
    public static void LogFleetHealth(int turnNumber, int me, int warships, int damaged, float meanHealthPct)
    {
        if (_currentLogPath == null) return;
        var ci = System.Globalization.CultureInfo.InvariantCulture;
        AppendLines(new List<string> { FormatLine(turnNumber, me, "FleetHealth", warships.ToString(ci), damaged.ToString(ci),
            meanHealthPct.ToString("0.#", ci)) });
    }
```
`GameAI.LogEconomySummary`: after the `Hostility` block inside the per-player loop, add (with `var stats = new WarshipStats(BlockadeSystem.ResearchItemsFrom(Gameboard.Instance.players));` computed once before the loop):

```csharp
                    var fleet = GameAIMap.PlanetList.SelectMany(p => p.DockedShips)
                        .Where(s => s.Owner == player && s.Kind == Ship.ShipKind.WarShip).ToList();
                    if (fleet.Count > 0)
                        AITuningLogger.LogFleetHealth(turnNumber, player, fleet.Count, fleet.Count(s => s.Damage > 0f),
                            100f * fleet.Average(s => stats.Health(s.Template, s.ResearchSnapshot) <= 0f
                                ? 0f : stats.CurrentHealth(s) / stats.Health(s.Template, s.ResearchSnapshot)));
```

- [ ] **Step 4: Verify (Rider)** on `HostilityCalculator.cs`, `StanceMatrix.cs`, `PlayerAI.cs`, `GameAI.cs`, `AITuningLogger.cs`, `DiplomacySelfCheck.cs`. Every existing `Run*` case in `DiplomacySelfCheck` builds `StanceMatrix.Row`s only through `Decide`/`WithRival`, so the new fields default to 0/false and behavior is unchanged.

- [ ] **Step 5: Commit**

```bash
cd /c/Projects/FlatSpace && git add Assets/Flatspace/GameAI/HostilityCalculator.cs Assets/Flatspace/GameAI/StanceMatrix.cs Assets/Flatspace/GameAI/PlayerAI.cs Assets/Flatspace/GameAI/GameAI.cs Assets/Flatspace/Diagnostics/AITuningLogger.cs Assets/Editor/DiplomacySelfCheck.cs && git commit -q -m "$(cat <<'EOF'
feat: hostility loss terms, the Surrender stance choice, LossDrop and FleetHealth logging

Co-Authored-By: Claude Sonnet 5.5 <noreply@anthropic.com>
EOF
)" && git log -1 --oneline
```

---

### Task 8: Simultaneity extended, suites registered, docs

**Files:**
- Modify: `Assets/Editor/SimultaneitySelfCheck.cs`
- Modify: `CLAUDE.md`, `FUTURE_FEATURES.md`, `.claude/skills/tuning-log/SKILL.md`

**Interfaces:**
- Consumes: everything above.
- Produces: a simultaneity fight case; docs.

- [ ] **Step 1: Extend the simultaneity check.** In `SimultaneitySelfCheck.cs` give `RunRound` a parameter `bool withLosses = false`; when true, after `map.Knowledge.Update(...)` add player 0's extra ships and a loss for player 1:

```csharp
            if (withLosses)
            {
                WarshipSelfCheck.DockWarships(map.GetPlanet("A"), 0, 3);   // player 0 is the stronger side
                results.Add(new Planet.PlanetUpdateResult("C",
                    Planet.PlanetUpdateResult.PlanetUpdateResultType.PlanetUpdateResultTypeWarshipsLost,
                    new CombatLoss { Attacker = 0, Ships = 5f, StrengthLost = 3000f }, 1));
            }
```
Add `RunLossOrderIndependenceCheck` (registered in `RunChecks`) that runs `RunRound(reversed: false, withLosses: true)` and `RunRound(reversed: true, withLosses: true)` and, for each player, asserts the same orders, stances and strategies (the same four assertions as the existing check, without the preconditions: a surrender may legitimately end player 1's war). Name the labels "with losses".

- [ ] **Step 2: Docs.**
  - `CLAUDE.md`: new section "### Ship combat" after "Warships and Blockade": `Ship.Damage`, `WarshipStats.CurrentHealth/EffectiveOffense` (no floor), every offense reader, `CombatSystem.Resolve` (where it runs, the steps: who fights, start-of-turn snapshot, hostility-weighted pools, one pool per victim, focus fire with `combatDamageK`, simultaneous apply, attribution, repair, the colony-ship rule), the result types, damage in the fleet payload and saves (`ShipSave.damage`), legacy mode off, the tunables. Update "Diplomacy": the loss terms (`hostilityPerShipLost`, `lossWindowTurns`, `significantLossFraction`, `significantLossHostilityDrop`), Surrender as a third `StanceMatrix` choice (offer rule, weight, no hold), `OrderTypeSurrender`/`ApplySurrender`, `TruceUntil`/`Turn`/`InTruce`/truce in `IsAtWar`, the new `Pair` log-only fields. Update "Orders" (`OrderTypeSurrender`), the self-check list (`Run Combat Self-Check`, 13 suites), and "AI Tuning Log" with the five new lines exactly as in the spec: `Combat`, `ColonyShipsLost`, `LossDrop`, `Surrender`, `FleetHealth` (formats and why each is logged when it is).
  - `.claude/skills/tuning-log/SKILL.md`: a "Ship combat" bullet beside "Diplomacy": for each new line what to report (fight length and lethality from `Combat`, what the colony-ship rule costs from `ColonyShipsLost`, how often the demoralization drop fires from `LossDrop`, surrender episodes and their `pSurrender` from `Surrender`, whether repair keeps pace from `FleetHealth`, fleet-cap discipline still 0 violations, hostility saturation against the earlier baselines).
  - `FUTURE_FEATURES.md`: under milestone element (2), note "built (branch `ship-combat`), pending the tuning pass", keep the surrender-terms revisit note and the retreat note, and keep (3) to (5) as they are.

- [ ] **Step 3: Verify.** Ask the user to focus the Unity Editor and run `FlatSpace -> AI -> Run All AI Self-Checks`. Expected: `ALL 13 SUITES PASSED`. If a compile error or failure appears, fix and ask again.

- [ ] **Step 4: Commit**

```bash
cd /c/Projects/FlatSpace && git add Assets/Editor/SimultaneitySelfCheck.cs CLAUDE.md FUTURE_FEATURES.md .claude/skills/tuning-log/SKILL.md && git commit -q -m "$(cat <<'EOF'
docs: ship combat in CLAUDE.md, the tuning-log skill and the feature list; simultaneity with losses

Co-Authored-By: Claude Sonnet 5.5 <noreply@anthropic.com>
EOF
)" && git log -1 --oneline
```

---

## Tuning log

Five new lines, all in the existing `T<turn>|P<playerId>|<EventCode>|<fields>` format, added across Tasks 4, 6, 7 (the spec's list):

- `Combat|<planet>|<attacker>-><victim>|<damageDealt>|<shipsDestroyed>` (P = the attacker): an event per planet and directed pair each turn combat happens, so a standoff repeats; no tracker needed (like `Blockade`). Task 4 (`GameAI.RunCombat` through `AITuningLogger.LogCombat`). Answers how long fights run and how lethal they are.
- `ColonyShipsLost|<planet>|<owner>|<count>|<byPlayer>`: per event, from the `ColonyShipsLost` result in `LogPlanetEvents`. Task 4. Answers what the colony-ship rule costs expansion.
- `LossDrop|<rival>|<Start or End>|<lossShare>`: when the loss share crosses `significantLossFraction`, kept from repeating by the log-only set `_lossDropLogged` in `PlayerAI` (a load logs each current one once more). Task 7. Answers how often the demoralization term fires.
- `Surrender|<rival>|<lossShare>|<pSurrender>|<truceUntil>`: at order execution, once per surrender. Task 6. Answers how the surrender weight behaves, the lever to revisit.
- `FleetHealth|<warships>|<damaged>|<meanHealthPct>`: every 25 turns per player beside `Economy`, so no repeat control is needed. Task 7. Answers whether repair keeps pace with combat.

`Stance` and `Hostility` keep their formats. The `tuning-log` skill and `CLAUDE.md` updates are in Task 8. No self-check covers the logger itself (by design); the lines are checked from real Play-mode logs.

## After the plan (not part of it)

A tuning/testing pass follows: Play-mode runs on `test2.json` and `4p.json` compared with the diplomacy-orders baselines (planets, arrivals, warship starts, fleet-cap violations, war length, Amass share, blockade counts), plus the combat numbers above. The surrender terms are revisited with the diplomacy tuning, not here.
