# Diplomacy Simulation Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Give every ordered pair of players a Peace/War stance, driven by a hostility score and decided by a ScoreMatrix variant, so war puts a player in `AIStrategyAmass`, peace with everyone returns it to `AIStrategyConsolidate`, and the assault attacks only players it is at war with.

**Architecture:** One pure `DiplomacyState` on `GameAIMap` holds stances, hostility and pending blockade cuts. A pure `HostilityCalculator` (with `FleetStrength`) updates the score once per turn; `StanceMatrix` (the `ScoreMatrix` with a new `IndependentRows` mode) picks Peace or War per rival. `PlayerAI.UpdateDiplomacy` runs at the end of `ProcessResults`, after `RefreshBlockadeView` and the `switch (Strategy)` block, and switches Consolidate and Amass. `IsConsolidateLike` lets Amass inherit every Consolidate rule; Amass gets its own two weight tables.

**Tech Stack:** Unity 6000.4.1f1 C# (`FlatSpace.AI` namespace), `JsonUtility` saves, Editor self-checks (`Assets/Editor/`), Rider MCP for compile signals.

**Spec:** `docs/superpowers/specs/2026-10-04-diplomacy-design.md` (read it and `CLAUDE.md` first; `FUTURE_FEATURES.md` milestone `ship_combat`, element 1).

## Global Constraints

- Namespace for new runtime code is `FlatSpace.AI` (written `namespace FlatSpace { namespace AI { ... } }` like the sibling files); new Editor scripts are in the global namespace with a `public static bool RunChecks()`.
- Pure code (`DiplomacyState`, `HostilityCalculator`, `FleetStrength`, `StanceMatrix`, `PlayerKnowledge` additions) must never touch `Gameboard.Instance`, so the self-check can drive it directly.
- A method a self-check calls is `public`, never `internal` (`Assets/Editor` is a separate assembly).
- Every new script's `.meta` file is created and committed with it (Task 1 gives the PowerShell helper).
- Self-check maps need a distinct position per planet (A* tie-break note) and no assertion exactly on a float boundary.
- `OrderType` values are not touched. Saves: `Stance` serializes as an `int`; an older save without `stances` loads as all Peace at 0 hostility.
- Tunables live on `GameAIConstants` with in-code defaults (the constants asset needs no edit): `diplomacyEnabled` true, `hostilityPerCut` 5, `hostilityPerNearShip` 0.5, `hostilityStrengthWeight` 1, `hostilityDecay` 0.05, `hostilityMax` 100, `stanceMidpoint` 30, `stanceSteepness` 8, `stanceStickiness` 3, `stanceHoldTurns` 10.
- Amass tables (spec, verbatim): Research Food 1.0, Industry 2.5, Grotsits 2.0, Research 1.0, ColonyShip 0.5, Warship 4.0. Industry Food 1.0, Industry 1.5, Grotsits 1.5, Research 0.5, ColonyShip 0.5, Warship 4.0, WarshipUpdate 4.0.
- Commit messages end with `Co-Authored-By: Claude Sonnet 5.5 <noreply@anthropic.com>`. Commit only on the feature branch `diplomacy`; never push or merge unprompted.
- Unity cannot be run from Claude Code. The compile signal is Rider's `get_file_problems` with `rootFolder: "C:/Projects/FlatSpace"` (it cannot analyse brand-new files and an empty list is not proof). The real check is the user focusing the Unity Editor and running `FlatSpace -> AI -> Run Diplomacy Self-Check` (and `Run All AI Self-Checks` where a task says so). A "RED" step means Rider shows the not-yet-written members as unresolved, or the Editor run fails the new assertion.

## Deviations from the spec (decided while planning; the user is told in the summary)

1. **`ScoreMatrix` gains an `IndependentRows` flag** (default false, so every existing matrix behaves exactly as before) instead of a separate class: the spec's "variation of ScoreMatrix" is one flag that stops a chosen choice being removed from other rows. `StanceMatrix` is the static wrapper that builds the rows.
2. **Diplomacy is switched on by `GameAI.InitGameAI`** (`GameAIMap.Diplomacy.Enabled = constants.diplomacyEnabled`), not by editing every older fixture. A `GameAIMap` built directly by a self-check stays in legacy mode (every rival with contact is an enemy, no stance matrix, no Amass switch), which is exactly what `diplomacyEnabled = false` means. `DiplomacySelfCheck` turns it on explicitly. Same effect as the spec, about 40 fewer edits.
3. **`AssaultPlanner.EnemyWarshipTotal` is also filtered by the war set** (a peaceful rival's docked fleet must not size the force against someone else). The spec filtered only the target; this keeps the force consistent with it.
4. **The Amass table assertions live in `DiplomacySelfCheck`**, not `PlayerKnowledgeSelfCheck`.
5. **`WarRivals` counts a player's own War stance even without current contact** (the stance can only exist after contact, and contact can flicker when a rival's ship leaves); only the forced war from the rival's stance needs contact (spec rule B).
6. **`PlayerKnowledge.HasContact` is untouched** (it also counts ownerless ships); the new `ContactPlayers` ignores ownerless ships.

## Review Focus

The inputs and conditions the spec implies but no obvious test would reach, most likely first (each has a test in the task named):

1. A war stance must survive losing contact: when the rival's ship leaves, the declarer must not flip back to Consolidate and re-enter Amass (Task 6).
2. A forced war must end when the declarer returns to Peace while contact holds (Task 6).
3. Ownerless ships (`Planet.NoOwner`) are never a rival, a contact or a war target (Tasks 1 and 5).
4. Blockade cuts by a player I have no contact with must not pile up and inflate hostility later (Task 6).
5. `diplomacyEnabled = false` (legacy) must keep every rival an enemy, run no matrix and never reach Amass; a map built by a fixture is legacy by default (Tasks 5 and 6).

---

## File Structure

| File | Change |
|---|---|
| `Assets/Flatspace/GameAI/GameAIConstants.cs` | modify: `Diplomacy` header with the ten tunables |
| `Assets/Flatspace/GameAI/DiplomacyState.cs` (+ `.meta`) | create: `Stance`, `DiplomacyState` (stances, hostility, cuts, war rivals, save entries) |
| `Assets/Flatspace/GameAI/FleetStrength.cs` (+ `.meta`) | create: strength of docked warships |
| `Assets/Flatspace/GameAI/HostilityCalculator.cs` (+ `.meta`) | create: the per-turn score update and near-ship count |
| `Assets/Flatspace/GameAI/StanceMatrix.cs` (+ `.meta`) | create: element types and `Decide` |
| `Assets/Flatspace/GameAI/ScoreMatrix.cs` | modify: `IndependentRows` flag |
| `Assets/Flatspace/GameAI/PlayerKnowledge.cs` | modify: `ContactPlayers`, `HasContactWith` |
| `Assets/Flatspace/GameAI/BlockadeSystem.cs` | modify: `BlockadeCut.BlockerId` |
| `Assets/Flatspace/GameAI/GameAIMap.cs` | modify: `Diplomacy` property |
| `Assets/Flatspace/GameAI/PlayerAI.cs` | modify: `IsConsolidateLike`, Amass tables and case, assault filter, `UpdateDiplomacy`, `WarRivals` |
| `Assets/Flatspace/GameAI/ShipTransportPlanner.cs` | modify: three `IsConsolidateLike` uses |
| `Assets/Flatspace/GameAI/AssaultPlanner.cs` | modify: optional war set |
| `Assets/Flatspace/GameAI/GameAI.cs` | modify: enable, record cuts, periodic `Hostility` line |
| `Assets/Flatspace/Diagnostics/AITuningLogger.cs` | modify: `LogStance`, `LogWarForced`, `LogHostility` |
| `Assets/Flatspace/SaveSystem/SaveLoadSystem.cs`, `Assets/Flatspace/Objects/Board/GameBoard.cs` | modify: stance save and restore |
| `Assets/Editor/DiplomacySelfCheck.cs` (+ `.meta`) | create: the suite |
| `Assets/Editor/AllAISelfChecks.cs` | modify: register the suite |
| `CLAUDE.md`, `.claude/skills/tuning-log/SKILL.md` | modify: docs |

---

### Task 1: Foundation (tunables, DiplomacyState, contact, cut blocker, suite scaffold)

**Files:**
- Create: `Assets/Flatspace/GameAI/DiplomacyState.cs`, `Assets/Editor/DiplomacySelfCheck.cs` (and both `.meta`)
- Modify: `GameAIConstants.cs`, `PlayerKnowledge.cs`, `BlockadeSystem.cs`, `GameAIMap.cs`, `AllAISelfChecks.cs`

**Interfaces:**
- Produces: `enum Stance { Peace = 0, War = 1 }`; `DiplomacyState` with `bool Enabled`, `Pair Get(int me, int rival)`, `void Set(int me, int rival, Pair)`, `Stance StanceToward(int me, int rival)`, `float Hostility(int me, int rival)`, `int TurnsSinceChange(int me, int rival, int turn)`, `bool SetStance(int me, int rival, Stance, int turn)` (true when it changed), `bool IsAtWar(int me, int rival, bool meHasContact)`, `SortedSet<int> WarRivals(int me, IEnumerable<int> contactPlayers)`, `List<int> Rivals(int me)`, `void RecordCut(int victim, int blocker)`, `int TakeCuts(int victim, int blocker)`, `void DiscardCuts(int victim)`, `List<Entry> Snapshot(int me)`, `void Restore(int me, IEnumerable<Entry>)`; `DiplomacyState.Pair` (fields `Stance, Hostility, LastChangeTurn, CutsTerm, NearTerm, StrengthTerm, PWar, MyStrength, RivalStrength, NearShips`); `DiplomacyState.Entry { Rival, Stance, Hostility, LastChangeTurn }`; `DiplomacyState.NeverChanged`; `PlayerKnowledge.ContactPlayers(GameAIMap, int) : List<int>`; `PlayerKnowledge.HasContactWith(GameAIMap, int, int) : bool`; `BlockadeSystem.BlockadeCut.BlockerId`; `GameAIMap.Diplomacy`.

- [ ] **Step 1: Create the feature branch**

```bash
cd /c/Projects/FlatSpace && git switch -c diplomacy
```

- [ ] **Step 2: Write the failing tests** — create `Assets/Editor/DiplomacySelfCheck.cs` (the fixture and the first three checks; later tasks add more `Run...` methods and list them in `RunChecks`):

```csharp
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using FlatSpace.AI;
using Flatspace.Objects.Production;

public static class DiplomacySelfCheck
{
    [MenuItem("FlatSpace/AI/Run Diplomacy Self-Check")]
    public static void Run() => RunChecks();

    public static bool RunChecks()
    {
        var ok = RunDiplomacyStateCheck();
        ok &= RunContactPlayersCheck();
        ok &= RunCutAttributionCheck();
        Debug.Log(ok
            ? "[DiplomacySelfCheck] ALL PASSED"
            : "[DiplomacySelfCheck] FAILURES (see errors above)");
        return ok;
    }

    private static bool Check(bool condition, string label)
    {
        if (!condition) Debug.LogError($"[DiplomacySelfCheck] FAIL: {label}");
        return condition;
    }

    private static bool Near(float a, float b) => Mathf.Abs(a - b) < 0.01f;

    // A(0,0) - B(100,0) - C(200,0) - D(300,0). Player 0 holds A and has a PlayerAI. Diplomacy stays in legacy mode
    // (Enabled false) until a check turns it on.
    private sealed class Fixture : System.IDisposable
    {
        public GameObject MapGo, PlayerGo;
        public GameAIMap Map;
        public GameAIConstants Constants;
        public ShipData Template;
        public List<CatalogItem> Research;
        public PlayerAI AI;

        public static Fixture Line()
        {
            var f = new Fixture();
            f.Template = WarshipSelfCheck.MakeTemplate();
            f.Constants = WarshipSelfCheck.MakeConstants(f.Template);
            f.Constants.maxPathNodesForKnowledge = 6;
            f.Constants.maxPathNodesForShipTransport = 10;
            f.Research = WarshipSelfCheck.MakeResearch();
            f.MapGo = new GameObject("DipSelfCheckMap");
            f.Map = f.MapGo.AddComponent<GameAIMap>();
            f.Map.GameAIMapInit(new List<PlanetSpawnData>
            {
                ChokepointSelfCheck.Spawn("A", 0f, 0f, new[] { "B" }),
                ChokepointSelfCheck.Spawn("B", 100f, 0f, new[] { "A", "C" }),
                ChokepointSelfCheck.Spawn("C", 200f, 0f, new[] { "B", "D" }),
                ChokepointSelfCheck.Spawn("D", 300f, 0f, new[] { "C" }),
            }, f.Constants);
            f.Colonize("A", 0);
            f.PlayerGo = new GameObject("DipSelfCheckPlayer");
            var player = f.PlayerGo.AddComponent<Player>();
            f.AI = f.PlayerGo.AddComponent<PlayerAI>();
            f.AI.Player = player;
            f.AI.AIMap = f.Map;
            player.playerID = 0;
            f.AI.ResearchCatalog = f.PlayerGo.AddComponent<Catalog>();
            f.AI.ResearchCatalog.catalogItems = f.Research;
            return f;
        }

        public Planet P(string name) => Map.GetPlanet(name);

        public void Colonize(string name, int player)
        {
            var planet = Map.GetPlanet(name);
            planet.Owner = player;
            planet.Population.Add(new Planet.Inhabitant { Player = player });
        }

        public void Ships(string planet, int owner, int count) => WarshipSelfCheck.DockWarships(P(planet), owner, count);

        public void Know(int players = 3) => Map.Knowledge.Update(Map, players, 6);

        public void Dispose()
        {
            WarshipSelfCheck.DestroyAll(Research);
            Object.DestroyImmediate(MapGo);
            Object.DestroyImmediate(PlayerGo);
            Object.DestroyImmediate(Constants);
            Object.DestroyImmediate(Template);
        }
    }

    // Stances, hostility, effective war, pending cuts and the save entries, all without a map.
    public static bool RunDiplomacyStateCheck()
    {
        var ok = true;
        var d = new DiplomacyState();

        ok &= Check(d.StanceToward(0, 1) == Stance.Peace && Near(d.Hostility(0, 1), 0f), "a fresh pair is Peace at 0 hostility");
        ok &= Check(d.TurnsSinceChange(0, 1, 5) > 1000, "a pair that never changed has no hold (a huge number of turns since)");

        ok &= Check(d.SetStance(0, 1, Stance.War, 7), "Peace to War reports a change");
        ok &= Check(!d.SetStance(0, 1, Stance.War, 8), "War to War reports no change");
        ok &= Check(d.TurnsSinceChange(0, 1, 10) == 3, "turns since change counts from the change (7 to 10)");
        ok &= Check(d.StanceToward(1, 0) == Stance.Peace, "the opposite direction is its own stance");

        // Effective war: own War always; the rival's War only with contact.
        ok &= Check(d.IsAtWar(0, 1, false), "my own War counts without contact");
        ok &= Check(!d.IsAtWar(1, 0, false), "the rival's War does not force me without contact");
        ok &= Check(d.IsAtWar(1, 0, true), "the rival's War forces me once I have contact");
        ok &= Check(!d.IsAtWar(0, 2, true), "no stance either way: Peace");

        d.SetStance(0, 1, Stance.Peace, 20);
        ok &= Check(!d.IsAtWar(1, 0, true), "a forced war ends when the declarer returns to Peace");

        // WarRivals: contact players plus anyone I declared on or who declared on me.
        d.SetStance(0, 3, Stance.War, 30);
        d.SetStance(2, 0, Stance.War, 30);
        var war = d.WarRivals(0, new[] { 1 });
        ok &= Check(war.SetEquals(new[] { 3 }), "no contact with 2: its declaration does not count; my own War on 3 does without contact");
        var warWithContact = d.WarRivals(0, new[] { 1, 2 });
        ok &= Check(warWithContact.SetEquals(new[] { 2, 3 }), "with contact with 2 its declaration counts too");

        // Cuts are counted per (victim, blocker), consumed once, and discarded for non-contact blockers.
        d.RecordCut(0, 1); d.RecordCut(0, 1); d.RecordCut(0, 2); d.RecordCut(0, 0); d.RecordCut(0, -1);
        ok &= Check(d.TakeCuts(0, 1) == 2, "two cuts by player 1 are counted");
        ok &= Check(d.TakeCuts(0, 1) == 0, "taking cuts consumes them");
        d.DiscardCuts(0);
        ok &= Check(d.TakeCuts(0, 2) == 0, "DiscardCuts drops cuts nobody consumed");
        d.RecordCut(1, 0);
        ok &= Check(d.TakeCuts(0, 1) == 0 && d.TakeCuts(1, 0) == 1, "cuts are per victim");

        // Save entries round trip; restoring null leaves the player all Peace; another player is untouched.
        var pair = d.Get(0, 3);
        pair.Hostility = 42.5f;
        d.Set(0, 3, pair);
        var entries = d.Snapshot(0);
        var restored = new DiplomacyState();
        restored.SetStance(1, 0, Stance.War, 4);
        restored.Restore(0, entries);
        ok &= Check(restored.StanceToward(0, 3) == Stance.War && Near(restored.Hostility(0, 3), 42.5f)
                    && restored.TurnsSinceChange(0, 3, 40) == 10,
            "stance, hostility and last-change turn survive Snapshot and Restore");
        ok &= Check(restored.StanceToward(1, 0) == Stance.War, "restoring player 0 leaves player 1 alone");
        restored.Restore(0, null);
        ok &= Check(restored.StanceToward(0, 3) == Stance.Peace && Near(restored.Hostility(0, 3), 0f),
            "restoring nothing (an older save) is all Peace at 0");
        ok &= Check(d.Rivals(0).SequenceEqual(new[] { 1, 3 }), "Rivals lists the players with a recorded pair, ascending");
        return ok;
    }

    // Contact is per rival, from planets the viewer knows; ownerless ships are never a rival.
    public static bool RunContactPlayersCheck()
    {
        var ok = true;
        using (var f = Fixture.Line())
        {
            f.Know();
            ok &= Check(f.Map.Knowledge.ContactPlayers(f.Map, 0).Count == 0, "alone: no contact players");

            f.Colonize("B", 1);
            f.Ships("C", 2, 1);
            f.Ships("B", Planet.NoOwner, 1);
            f.Know();
            var contact = f.Map.Knowledge.ContactPlayers(f.Map, 0);
            ok &= Check(contact.SequenceEqual(new[] { 1, 2 }),
                "population on a known planet and a docked ship are contact; an ownerless ship is not a player");
            ok &= Check(f.Map.Knowledge.HasContactWith(f.Map, 0, 1) && f.Map.Knowledge.HasContactWith(f.Map, 0, 2)
                        && !f.Map.Knowledge.HasContactWith(f.Map, 0, 3),
                "contact is per rival");
            ok &= Check(!f.Map.Knowledge.ContactPlayers(f.Map, 0).Contains(0), "I am never my own contact");
        }
        return ok;
    }

    // A blockade cut carries the player whose offense set the blockade value, so hostility can be charged to it.
    public static bool RunCutAttributionCheck()
    {
        var ok = true;
        using (var f = Fixture.Line())
        {
            f.Ships("B", 1, 1);                       // player 1 blockades B: offense 10 against my 0
            var order = new GameAI.GameAIOrder
            {
                Type = GameAI.GameAIOrder.OrderType.OrderTypeFoodTransport,
                Origin = "A", Target = "C", PlayerId = 0, Data = 10f,
                TimingDelay = 1, TotalDelay = 2,      // halfway: passes B this turn
            };
            var orders = new List<GameAI.GameAIOrder> { order };
            var cuts = new BlockadeSystem(f.Map, f.Research).Apply(orders, 5);
            ok &= Check(cuts.Count == 1 && cuts[0].Planet == "B" && cuts[0].PlayerId == 0 && cuts[0].BlockerId == 1,
                "the cut at B names player 1 as the blocker");
        }
        return ok;
    }
}
```

- [ ] **Step 3: Run to verify it fails (RED)**

Run Rider `get_file_problems` on `Assets/Editor/DiplomacySelfCheck.cs` (a brand-new file, so it may report "not included in any project"; that is expected). Expected: unresolved `Stance`, `DiplomacyState`, `ContactPlayers`, `HasContactWith`, `BlockerId`.

- [ ] **Step 4: Add the tunables** — in `GameAIConstants.cs`, after the `Knowledge` block (before `[Header("Distribution Centers")]`):

```csharp
    [Header("Diplomacy")]
    // False = today's behaviour: every rival with contact is an enemy, no stance matrix, no Amass switch. GameAI.InitGameAI
    // copies this onto GameAIMap.Diplomacy.Enabled; a GameAIMap a self-check builds directly stays in that legacy mode.
    public bool diplomacyEnabled = true;
    // Hostility toward a rival grows each turn by: its blockade cuts on my orders x hostilityPerCut, its warships docked on or
    // beside my planets x hostilityPerNearShip, and hostilityStrengthWeight x log2(my fleet strength / its visible fleet
    // strength), clamped to +-2 (a stronger player drifts toward war). It then decays by hostilityDecay a turn and is clamped
    // to 0..hostilityMax.
    public float hostilityPerCut = 5f;
    public float hostilityPerNearShip = 0.5f;
    public float hostilityStrengthWeight = 1f;
    public float hostilityDecay = 0.05f;
    public float hostilityMax = 100f;
    // War probability = 1 / (1 + e^(-(hostility - stanceMidpoint) / stanceSteepness)). The stance held now has its weight
    // multiplied by stanceStickiness, and a stance is not reconsidered for stanceHoldTurns turns after it changes.
    public float stanceMidpoint = 30f;
    public float stanceSteepness = 8f;
    public float stanceStickiness = 3f;
    public int stanceHoldTurns = 10;
```

- [ ] **Step 5: Create `Assets/Flatspace/GameAI/DiplomacyState.cs`**

```csharp
using System.Collections.Generic;
using System.Linq;

namespace FlatSpace
{
    namespace AI
    {
        public enum Stance { Peace = 0, War = 1 }

        /// <summary>
        /// Every player's stance toward every rival, the hostility score behind it, and the blockade cuts waiting to be
        /// charged to a rival's hostility. Pure (no Gameboard.Instance). Effective war is derived, never stored: player P is
        /// at war with Q when P's own stance toward Q is War, or Q's stance toward P is War and P has contact with Q.
        /// </summary>
        public class DiplomacyState
        {
            /// <summary>A pair that never changed has no hold: its turns-since-change is far above any hold.</summary>
            public const int NeverChanged = -1000000;

            public struct Pair
            {
                public Stance Stance;
                public float Hostility;
                public int LastChangeTurn;
                // Log-only: the last turn's terms and strengths. Not saved.
                public float CutsTerm, NearTerm, StrengthTerm, PWar, MyStrength, RivalStrength;
                public int NearShips;
            }

            /// <summary>What a save keeps of a pair.</summary>
            public struct Entry
            {
                public int Rival;
                public Stance Stance;
                public float Hostility;
                public int LastChangeTurn;
            }

            /// <summary>False = legacy: every rival with contact is an enemy and no stance is ever decided.</summary>
            public bool Enabled { get; set; }

            private readonly Dictionary<(int, int), Pair> _pairs = new Dictionary<(int, int), Pair>();
            private readonly Dictionary<(int, int), int> _cuts = new Dictionary<(int, int), int>();

            public Pair Get(int me, int rival)
                => _pairs.TryGetValue((me, rival), out var pair)
                    ? pair
                    : new Pair { Stance = Stance.Peace, LastChangeTurn = NeverChanged };

            public void Set(int me, int rival, Pair pair) => _pairs[(me, rival)] = pair;

            public Stance StanceToward(int me, int rival) => Get(me, rival).Stance;

            public float Hostility(int me, int rival) => Get(me, rival).Hostility;

            public int TurnsSinceChange(int me, int rival, int turn) => turn - Get(me, rival).LastChangeTurn;

            /// <summary>Sets a stance; true only when it changed (the caller logs and holds on that).</summary>
            public bool SetStance(int me, int rival, Stance stance, int turn)
            {
                var pair = Get(me, rival);
                if (pair.Stance == stance) return false;
                pair.Stance = stance;
                pair.LastChangeTurn = turn;
                Set(me, rival, pair);
                return true;
            }

            public bool IsAtWar(int me, int rival, bool meHasContactWithRival)
                => StanceToward(me, rival) == Stance.War
                   || (meHasContactWithRival && StanceToward(rival, me) == Stance.War);

            /// <summary>
            /// The players `me` is at war with: its contact players, plus anyone it declared on (a stance can only exist after
            /// contact, and contact can flicker) or who declared on it, each tested with IsAtWar.
            /// </summary>
            public SortedSet<int> WarRivals(int me, IEnumerable<int> contactPlayers)
            {
                var contact = new HashSet<int>(contactPlayers);
                var candidates = new SortedSet<int>(contact);
                foreach (var key in _pairs.Keys)
                {
                    if (key.Item1 == me) candidates.Add(key.Item2);
                    if (key.Item2 == me) candidates.Add(key.Item1);
                }
                var result = new SortedSet<int>();
                foreach (var candidate in candidates)
                    if (candidate != me && IsAtWar(me, candidate, contact.Contains(candidate)))
                        result.Add(candidate);
                return result;
            }

            /// <summary>The rivals `me` has a recorded pair with, ascending.</summary>
            public List<int> Rivals(int me)
                => _pairs.Keys.Where(k => k.Item1 == me).Select(k => k.Item2).OrderBy(r => r).ToList();

            // ── Blockade cuts waiting to be charged ──────────────────────────

            /// <summary>One of `victim`'s orders was cut by `blocker`'s blockade. Self and ownerless blockers are ignored.</summary>
            public void RecordCut(int victim, int blocker)
            {
                if (blocker < 0 || blocker == victim) return;
                _cuts.TryGetValue((victim, blocker), out var count);
                _cuts[(victim, blocker)] = count + 1;
            }

            /// <summary>The cuts by `blocker` on `victim`'s orders since the last take; consumed.</summary>
            public int TakeCuts(int victim, int blocker)
            {
                if (!_cuts.TryGetValue((victim, blocker), out var count)) return 0;
                _cuts.Remove((victim, blocker));
                return count;
            }

            /// <summary>Drops every cut still waiting for `victim` (blockers it has no contact with must not pile up).</summary>
            public void DiscardCuts(int victim)
            {
                foreach (var key in _cuts.Keys.Where(k => k.Item1 == victim).ToList())
                    _cuts.Remove(key);
            }

            // ── Saves ────────────────────────────────────────────────────────

            public List<Entry> Snapshot(int me)
                => _pairs.Where(kv => kv.Key.Item1 == me)
                    .OrderBy(kv => kv.Key.Item2)
                    .Select(kv => new Entry
                    {
                        Rival = kv.Key.Item2,
                        Stance = kv.Value.Stance,
                        Hostility = kv.Value.Hostility,
                        LastChangeTurn = kv.Value.LastChangeTurn,
                    }).ToList();

            /// <summary>Replaces `me`'s pairs with the entries; null or empty (an older save) leaves it all Peace at 0.</summary>
            public void Restore(int me, IEnumerable<Entry> entries)
            {
                foreach (var key in _pairs.Keys.Where(k => k.Item1 == me).ToList())
                    _pairs.Remove(key);
                if (entries == null) return;
                foreach (var entry in entries)
                    _pairs[(me, entry.Rival)] = new Pair
                    {
                        Stance = entry.Stance,
                        Hostility = entry.Hostility,
                        LastChangeTurn = entry.LastChangeTurn,
                    };
            }
        }
    }
}
```

- [ ] **Step 6: Contact per rival** — in `PlayerKnowledge.cs`, add `using System.Linq;` at the top and these two methods directly after `HasContact` (leave `HasContact` itself untouched):

```csharp
            /// <summary>
            /// The other players holding population or a docked ship on a planet this player knows, ascending. Ownerless
            /// ships (Owner &lt; 0) are not a player. HasContact (any such presence, ownerless included) is left as it was.
            /// </summary>
            public List<int> ContactPlayers(GameAIMap map, int playerId)
            {
                var found = new SortedSet<int>();
                foreach (var name in KnownPlanets(playerId))
                {
                    var planet = map.GetPlanet(name);
                    if (planet == null) continue;
                    foreach (var inhabitant in planet.Population)
                        if (inhabitant.Player != playerId && inhabitant.Player >= 0) found.Add(inhabitant.Player);
                    foreach (var ship in planet.DockedShips)
                        if (ship.Owner != playerId && ship.Owner >= 0) found.Add(ship.Owner);
                }
                return found.ToList();
            }

            public bool HasContactWith(GameAIMap map, int playerId, int rivalId)
                => ContactPlayers(map, playerId).Contains(rivalId);
```

- [ ] **Step 7: The blocker on a cut** — in `BlockadeSystem.cs` change the struct and the two `cuts.Add` calls:

```csharp
        public struct BlockadeCut
        {
            public int PlayerId;
            public string Planet;
            public float Value;
            public int BlockerId;   // the player whose docked offense set Value (see Value(...)'s `blocker`)
        }
```
In `ApplyToColony` and `ApplyToShipment` replace `cuts.Add(new BlockadeCut { PlayerId = order.PlayerId, Planet = node.Name, Value = value });` with:

```csharp
                cuts.Add(new BlockadeCut { PlayerId = order.PlayerId, Planet = node.Name, Value = value, BlockerId = blocker });
```

- [ ] **Step 8: The state on the map** — in `GameAIMap.cs` add the property under `Knowledge`:

```csharp
            public DiplomacyState Diplomacy { get; private set; }
```
and in `GameAIMapInit`, right after `Knowledge = new PlayerKnowledge();`:

```csharp
                Diplomacy = new DiplomacyState();
```

- [ ] **Step 9: Register the suite** — in `AllAISelfChecks.cs` add `("Diplomacy", DiplomacySelfCheck.RunChecks),` after the Colonist Redirect line.

- [ ] **Step 10: Create the `.meta` files** (PowerShell; the GUID is random, the block is what Unity writes for a script):

```powershell
function New-Meta($path) {
  $text = "fileFormatVersion: 2`nguid: $([guid]::NewGuid().ToString('N'))`nMonoImporter:`n  externalObjects: {}`n  serializedVersion: 2`n  defaultReferences: []`n  executionOrder: 0`n  icon: {instanceID: 0}`n  userData: `n  assetBundleName: `n  assetBundleVariant: `n"
  [System.IO.File]::WriteAllText("$path.meta", $text, (New-Object System.Text.UTF8Encoding $false))
}
cd C:\Projects\FlatSpace
New-Meta "Assets\Flatspace\GameAI\DiplomacyState.cs"
New-Meta "Assets\Editor\DiplomacySelfCheck.cs"
```

- [ ] **Step 11: Verify (GREEN)**

Rider `get_file_problems` on `DiplomacyState.cs`, `PlayerKnowledge.cs`, `BlockadeSystem.cs`, `GameAIMap.cs`, `GameAIConstants.cs`. Then ask the user to focus the Unity Editor and run `FlatSpace -> AI -> Run Diplomacy Self-Check`. Expected: `[DiplomacySelfCheck] ALL PASSED`.

- [ ] **Step 12: Commit**

```bash
git add Assets/Flatspace/GameAI/DiplomacyState.cs Assets/Flatspace/GameAI/DiplomacyState.cs.meta Assets/Editor/DiplomacySelfCheck.cs Assets/Editor/DiplomacySelfCheck.cs.meta Assets/Flatspace/GameAI/GameAIConstants.cs Assets/Flatspace/GameAI/PlayerKnowledge.cs Assets/Flatspace/GameAI/BlockadeSystem.cs Assets/Flatspace/GameAI/GameAIMap.cs Assets/Editor/AllAISelfChecks.cs
git commit -m "feat: DiplomacyState, per-rival contact, blockade cut blocker, diplomacy self-check scaffold"
```
(End the message with the `Co-Authored-By` line from Global Constraints.)

---

### Task 2: FleetStrength and HostilityCalculator

**Files:**
- Create: `Assets/Flatspace/GameAI/FleetStrength.cs`, `Assets/Flatspace/GameAI/HostilityCalculator.cs` (and `.meta`)
- Modify: `Assets/Editor/DiplomacySelfCheck.cs`

**Interfaces:**
- Consumes: `WarshipStats.Offense/Health/Defense(ShipData, ICollection<string>)`, `GameAIMap.PlanetList/GetPlanet/GetNeighbours/Knowledge`, `GameAIConstants` hostility fields.
- Produces: `FleetStrength.Of(IEnumerable<Ship>, WarshipStats) : float`, `FleetStrength.Mine(GameAIMap, int, WarshipStats) : float`, `FleetStrength.VisibleOf(GameAIMap, int viewer, int rival, WarshipStats) : float`; `HostilityCalculator.Inputs { Previous, Cuts, NearShips, MyStrength, RivalStrength }`, `HostilityCalculator.Result { Hostility, CutsTerm, NearTerm, StrengthTerm }`, `HostilityCalculator.StrengthTerm(float mine, float rival, float weight) : float`, `HostilityCalculator.Compute(Inputs, GameAIConstants) : Result`, `HostilityCalculator.CountNearShips(GameAIMap, int me, int rival) : int`.

- [ ] **Step 1: Write the failing tests** — add to `DiplomacySelfCheck.cs`: in `RunChecks` add `ok &= RunFleetStrengthCheck(); ok &= RunHostilityCheck();` before the `Debug.Log`, and add these methods inside the class:

```csharp
    // Strength = sum over docked warships of Offense x (Health + Defense). The test template is offense 10, health 100,
    // defense 5, so one unresearched ship is 10 x 105 = 1050.
    public static bool RunFleetStrengthCheck()
    {
        var ok = true;
        using (var f = Fixture.Line())
        {
            var stats = new WarshipStats(f.Research);
            f.Ships("A", 0, 2);
            f.Ships("B", 1, 3);
            f.Ships("D", 1, 4);                      // beyond what player 0 knows once knowledge is small
            f.P("A").DockShipFromSave(Ship.ShipKind.ColonyShip, 0, new List<string>());

            ok &= Check(Near(FleetStrength.Of(f.P("A").DockedShips.Where(s => s.Owner == 0), stats), 2100f),
                "two warships are 2 x 1050; the colony ship adds nothing");
            ok &= Check(Near(FleetStrength.Of(new List<Ship>(), stats), 0f), "no ships: 0");
            ok &= Check(Near(FleetStrength.Mine(f.Map, 0, stats), 2100f), "Mine counts my docked warships anywhere");

            f.Map.Knowledge.Update(f.Map, 3, 2);     // source + direct neighbours: player 0 knows A and B only
            ok &= Check(f.Map.Knowledge.IsKnown(0, "B") && !f.Map.Knowledge.IsKnown(0, "D"), "precondition: B known, D unknown");
            ok &= Check(Near(FleetStrength.VisibleOf(f.Map, 0, 1, stats), 3150f),
                "the rival's strength counts only on planets I know: 3 x 1050 on B, not the ships on D");
            ok &= Check(Near(FleetStrength.VisibleOf(f.Map, 0, 2, stats), 0f), "a player with no ships there is 0");

            var withResearch = new List<string> { "Off 1" };      // one Offense tier: +4 offense (10 to 14)
            f.P("B").DockShipFromSave(Ship.ShipKind.WarShip, 1, withResearch);
            ok &= Check(FleetStrength.VisibleOf(f.Map, 0, 1, stats) > 3150f + 1050f,
                "a researched ship is worth more than an unresearched one");
        }
        return ok;
    }

    public static bool RunHostilityCheck()
    {
        var ok = true;
        var c = ScriptableObject.CreateInstance<GameAIConstants>();   // defaults: cut 5, near 0.5, weight 1, decay 0.05, max 100
        try
        {
            ok &= Check(Near(HostilityCalculator.StrengthTerm(2000f, 1000f, 1f), 1f), "twice as strong: log2(2) = +1");
            ok &= Check(Near(HostilityCalculator.StrengthTerm(4000f, 1000f, 1f), 2f), "four times as strong: +2");
            ok &= Check(Near(HostilityCalculator.StrengthTerm(16000f, 1000f, 1f), 2f), "sixteen times as strong clamps at +2");
            ok &= Check(Near(HostilityCalculator.StrengthTerm(1000f, 4000f, 1f), -2f), "a quarter as strong: -2");
            ok &= Check(Near(HostilityCalculator.StrengthTerm(1000f, 64000f, 1f), -2f), "far weaker clamps at -2");
            ok &= Check(Near(HostilityCalculator.StrengthTerm(500f, 0f, 1f), 2f), "a rival with no visible fleet counts as +2");
            ok &= Check(Near(HostilityCalculator.StrengthTerm(0f, 500f, 1f), -2f), "no fleet against a fleet: -2");
            ok &= Check(Near(HostilityCalculator.StrengthTerm(0f, 0f, 1f), 0f), "two empty fleets: 0");
            ok &= Check(Near(HostilityCalculator.StrengthTerm(2000f, 1000f, 3f), 3f), "the weight scales the term");

            var decayOnly = HostilityCalculator.Compute(new HostilityCalculator.Inputs { Previous = 100f }, c);
            ok &= Check(Near(decayOnly.Hostility, 95f), "no inputs: 100 decays by 5% to 95");

            var all = HostilityCalculator.Compute(new HostilityCalculator.Inputs
                { Previous = 0f, Cuts = 2, NearShips = 4, MyStrength = 1000f, RivalStrength = 1000f }, c);
            ok &= Check(Near(all.CutsTerm, 10f) && Near(all.NearTerm, 2f) && Near(all.StrengthTerm, 0f) && Near(all.Hostility, 12f),
                "2 cuts x 5 + 4 ships x 0.5 + an even fleet = 12");

            var high = HostilityCalculator.Compute(new HostilityCalculator.Inputs { Previous = 100f, Cuts = 10 }, c);
            ok &= Check(Near(high.Hostility, 100f), "clamped to the maximum of 100");
            var low = HostilityCalculator.Compute(new HostilityCalculator.Inputs
                { Previous = 0f, MyStrength = 1000f, RivalStrength = 64000f }, c);
            ok &= Check(Near(low.Hostility, 0f), "clamped at 0: a weaker player is never below neutral");
        }
        finally { Object.DestroyImmediate(c); }

        // Near ships: rival warships docked on a planet I hold or beside one of my populated planets, known planets only.
        using (var f = Fixture.Line())
        {
            f.Ships("A", 1, 1);                       // on a planet I hold
            f.Ships("B", 1, 2);                       // beside it
            f.Ships("C", 1, 3);                       // two hops away: not near
            f.Ships("B", 2, 5);                       // another rival's ships do not count
            f.Ships("A", 0, 4);                       // my own ships do not count
            f.Know();
            ok &= Check(HostilityCalculator.CountNearShips(f.Map, 0, 1) == 3, "1 on my planet + 2 beside it = 3; C is too far");
            ok &= Check(HostilityCalculator.CountNearShips(f.Map, 0, 2) == 5, "per rival: player 2's 5 ships on B");
            ok &= Check(HostilityCalculator.CountNearShips(f.Map, 0, 3) == 0, "a rival with no ships: 0");
        }
        return ok;
    }
```

- [ ] **Step 2: Run to verify it fails (RED)** — Rider `get_file_problems` on `DiplomacySelfCheck.cs`: unresolved `FleetStrength`, `HostilityCalculator`.

- [ ] **Step 3: Create `Assets/Flatspace/GameAI/FleetStrength.cs`**

```csharp
using System.Collections.Generic;
using System.Linq;

namespace FlatSpace
{
    namespace AI
    {
        /// <summary>
        /// How strong a set of docked warships is: the sum of Offense x (Health + Defense) over each ship (WarshipStats, so
        /// research counts). One pure place for the formula, so ship combat can replace it later without touching the callers.
        /// </summary>
        public static class FleetStrength
        {
            public static float Of(IEnumerable<Ship> ships, WarshipStats stats)
            {
                var sum = 0f;
                foreach (var ship in ships)
                {
                    if (ship.Kind != Ship.ShipKind.WarShip) continue;
                    var offense = stats.Offense(ship.Template, ship.ResearchSnapshot);
                    var durability = stats.Health(ship.Template, ship.ResearchSnapshot)
                                     + stats.Defense(ship.Template, ship.ResearchSnapshot);
                    sum += offense * durability;
                }
                return sum;
            }

            /// <summary>My docked warships anywhere (in-flight ships are not counted).</summary>
            public static float Mine(GameAIMap map, int player, WarshipStats stats)
                => map.PlanetList.Sum(p => Of(p.DockedShips.Where(s => s.Owner == player), stats));

            /// <summary>The rival's docked warships on planets the viewer knows (the same limit AssaultPlanner uses).</summary>
            public static float VisibleOf(GameAIMap map, int viewer, int rival, WarshipStats stats)
            {
                var sum = 0f;
                foreach (var name in map.Knowledge.KnownPlanets(viewer))
                {
                    var planet = map.GetPlanet(name);
                    if (planet == null) continue;
                    sum += Of(planet.DockedShips.Where(s => s.Owner == rival), stats);
                }
                return sum;
            }
        }
    }
}
```

- [ ] **Step 4: Create `Assets/Flatspace/GameAI/HostilityCalculator.cs`**

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace FlatSpace
{
    namespace AI
    {
        /// <summary>
        /// The per-turn hostility update for one (player, rival) pair: the previous score decays, then the rival's blockade cuts,
        /// its warships near my planets and the strength ratio are added, all clamped to 0..hostilityMax. Pure.
        /// </summary>
        public static class HostilityCalculator
        {
            public const float StrengthLogClamp = 2f;

            public struct Inputs
            {
                public float Previous;
                public int Cuts;
                public int NearShips;
                public float MyStrength;
                public float RivalStrength;
            }

            public struct Result
            {
                public float Hostility;
                public float CutsTerm;
                public float NearTerm;
                public float StrengthTerm;
            }

            /// <summary>weight x log2(mine / rival) clamped to +-2; no rival fleet is +2, no fleet of mine against one is -2, two empty fleets 0.</summary>
            public static float StrengthTerm(float mine, float rival, float weight)
            {
                if (mine <= 0f && rival <= 0f) return 0f;
                float log;
                if (rival <= 0f) log = StrengthLogClamp;
                else if (mine <= 0f) log = -StrengthLogClamp;
                else log = Mathf.Clamp((float)Math.Log(mine / rival, 2.0), -StrengthLogClamp, StrengthLogClamp);
                return weight * log;
            }

            public static Result Compute(Inputs input, GameAIConstants constants)
            {
                var cuts = input.Cuts * constants.hostilityPerCut;
                var near = input.NearShips * constants.hostilityPerNearShip;
                var strength = StrengthTerm(input.MyStrength, input.RivalStrength, constants.hostilityStrengthWeight);
                var hostility = input.Previous * (1f - constants.hostilityDecay) + cuts + near + strength;
                return new Result
                {
                    Hostility = Mathf.Clamp(hostility, 0f, constants.hostilityMax),
                    CutsTerm = cuts,
                    NearTerm = near,
                    StrengthTerm = strength,
                };
            }

            /// <summary>The rival's docked warships on planets I hold or beside my populated planets (known planets only).</summary>
            public static int CountNearShips(GameAIMap map, int me, int rival)
            {
                var near = new HashSet<string>();
                foreach (var planet in map.PlanetList)
                {
                    if (planet.Owner != me || planet.Population.Count == 0) continue;
                    near.Add(planet.PlanetName);
                    foreach (var neighbour in map.GetNeighbours(planet.PlanetName)) near.Add(neighbour);
                }

                var count = 0;
                foreach (var name in near)
                {
                    if (!map.Knowledge.IsKnown(me, name)) continue;
                    var planet = map.GetPlanet(name);
                    if (planet == null) continue;
                    count += planet.DockedShips.Count(s => s.Kind == Ship.ShipKind.WarShip && s.Owner == rival);
                }
                return count;
            }
        }
    }
}
```

- [ ] **Step 5: `.meta` files** — run the `New-Meta` function from Task 1 Step 10 for `Assets\Flatspace\GameAI\FleetStrength.cs` and `Assets\Flatspace\GameAI\HostilityCalculator.cs`.

- [ ] **Step 6: Verify (GREEN)** — Rider on the two new files; ask the user to run `Run Diplomacy Self-Check`. Expected: `ALL PASSED`.

- [ ] **Step 7: Commit**

```bash
git add Assets/Flatspace/GameAI/FleetStrength.cs Assets/Flatspace/GameAI/FleetStrength.cs.meta Assets/Flatspace/GameAI/HostilityCalculator.cs Assets/Flatspace/GameAI/HostilityCalculator.cs.meta Assets/Editor/DiplomacySelfCheck.cs
git commit -m "feat: FleetStrength and HostilityCalculator"
```

---

### Task 3: ScoreMatrix.IndependentRows and StanceMatrix

**Files:**
- Create: `Assets/Flatspace/GameAI/StanceMatrix.cs` (and `.meta`)
- Modify: `Assets/Flatspace/GameAI/ScoreMatrix.cs`, `Assets/Editor/DiplomacySelfCheck.cs`

**Interfaces:**
- Consumes: `ScoreMatrix<TDecision, TChoice, TAction>`, `IScoreMatrix*` interfaces, `GameAIConstants` stance fields, `Stance`.
- Produces: `ScoreMatrix.IndependentRows { get; set; }`; `StanceMatrix.Row { Rival, Hostility, Current, TurnsSinceChange }`, `StanceMatrix.Decision { Rival, Stance, PWar }`, `StanceMatrix.WarProbability(float hostility, GameAIConstants) : float`, `StanceMatrix.WarWeight(Row, GameAIConstants)`, `StanceMatrix.PeaceWeight(Row, GameAIConstants)`, `StanceMatrix.Decide(int player, List<Row>, GameAIConstants) : List<Decision>`.

- [ ] **Step 1: Write the failing tests** — add `ok &= RunStanceMatrixCheck();` to `RunChecks` and this method:

```csharp
    // Deterministic cases use a very steep curve (steepness 0.01), so the war probability is exactly 0 below the midpoint and
    // exactly 1 above it; the roulette then never picks a zero-weight stance.
    public static bool RunStanceMatrixCheck()
    {
        var ok = true;
        var c = ScriptableObject.CreateInstance<GameAIConstants>();
        try
        {
            ok &= Check(Near(StanceMatrix.WarProbability(30f, c), 0.5f), "at the midpoint the war probability is 0.5");
            ok &= Check(StanceMatrix.WarProbability(0f, c) < 0.05f && StanceMatrix.WarProbability(100f, c) > 0.95f,
                "the default curve is low at 0 and high at 100");
            ok &= Check(StanceMatrix.WarProbability(20f, c) < StanceMatrix.WarProbability(40f, c), "it rises with hostility");

            var peaceRow = new StanceMatrix.Row { Rival = 1, Hostility = 30f, Current = Stance.Peace, TurnsSinceChange = 99 };
            var warRow = new StanceMatrix.Row { Rival = 1, Hostility = 30f, Current = Stance.War, TurnsSinceChange = 99 };
            ok &= Check(Near(StanceMatrix.PeaceWeight(peaceRow, c), 1.5f) && Near(StanceMatrix.WarWeight(peaceRow, c), 0.5f),
                "at 0.5 the held stance (Peace) is x3: Peace 1.5, War 0.5");
            ok &= Check(Near(StanceMatrix.WarWeight(warRow, c), 1.5f) && Near(StanceMatrix.PeaceWeight(warRow, c), 0.5f),
                "the held stance (War) is x3: War 1.5, Peace 0.5");

            c.stanceSteepness = 0.01f;
            c.stanceMidpoint = 30f;
            c.stanceHoldTurns = 10;

            // Rows are independent: two rivals can both get War (one shared matrix would hand War to only one of them).
            var both = StanceMatrix.Decide(0, new List<StanceMatrix.Row>
            {
                new StanceMatrix.Row { Rival = 1, Hostility = 100f, Current = Stance.Peace, TurnsSinceChange = 99 },
                new StanceMatrix.Row { Rival = 2, Hostility = 100f, Current = Stance.Peace, TurnsSinceChange = 99 },
            }, c);
            ok &= Check(both.Count == 2 && both.All(d => d.Stance == Stance.War) && both.Select(d => d.Rival).SequenceEqual(new[] { 1, 2 }),
                "two rivals above the midpoint both get War, in rival order");
            ok &= Check(both.All(d => Near(d.PWar, 1f)), "the decision carries the war probability for the log");

            var mixed = StanceMatrix.Decide(0, new List<StanceMatrix.Row>
            {
                new StanceMatrix.Row { Rival = 1, Hostility = 0f, Current = Stance.War, TurnsSinceChange = 99 },
                new StanceMatrix.Row { Rival = 2, Hostility = 100f, Current = Stance.War, TurnsSinceChange = 99 },
            }, c);
            ok &= Check(mixed.Count == 2 && mixed[0].Stance == Stance.Peace && mixed[1].Stance == Stance.War,
                "below the midpoint War becomes Peace, above it War stays War");

            var held = StanceMatrix.Decide(0, new List<StanceMatrix.Row>
            {
                new StanceMatrix.Row { Rival = 1, Hostility = 100f, Current = Stance.Peace, TurnsSinceChange = 3 },
                new StanceMatrix.Row { Rival = 2, Hostility = 100f, Current = Stance.Peace, TurnsSinceChange = 10 },
            }, c);
            ok &= Check(held.Count == 1 && held[0].Rival == 2, "a row within stanceHoldTurns of its last change is not decided; at the hold it is");
            ok &= Check(StanceMatrix.Decide(0, new List<StanceMatrix.Row>(), c).Count == 0, "no rows: no decisions");
        }
        finally { Object.DestroyImmediate(c); }

        // The generic matrix is unchanged by default: a choice claimed by one row is removed from the others.
        var matrix = new ScoreMatrix<ScoreMatrixDecisionElement, ScoreMatrixChoiceElement, ScoreMatrixAction>(new ScoreMatrixDecisionComparer());
        matrix.MatrixElements.Add(new ScoreMatrixDecisionElement { Target = "R1", Priority = 2f },
            new List<ScoreMatrixChoiceElement> { new ScoreMatrixChoiceElement { Target = "X", Cost = 1f } });
        matrix.MatrixElements.Add(new ScoreMatrixDecisionElement { Target = "R2", Priority = 1f },
            new List<ScoreMatrixChoiceElement> { new ScoreMatrixChoiceElement { Target = "X", Cost = 1f } });
        var shared = matrix.GenerateActionList((d, ch) => new ScoreMatrixAction { Origin = d.Target, Target = ch.Target });
        ok &= Check(shared.Count == 1, "default: both rows want X, only one gets it");

        var independent = new ScoreMatrix<ScoreMatrixDecisionElement, ScoreMatrixChoiceElement, ScoreMatrixAction>(new ScoreMatrixDecisionComparer())
            { IndependentRows = true };
        independent.MatrixElements.Add(new ScoreMatrixDecisionElement { Target = "R1", Priority = 2f },
            new List<ScoreMatrixChoiceElement> { new ScoreMatrixChoiceElement { Target = "X", Cost = 1f } });
        independent.MatrixElements.Add(new ScoreMatrixDecisionElement { Target = "R2", Priority = 1f },
            new List<ScoreMatrixChoiceElement> { new ScoreMatrixChoiceElement { Target = "X", Cost = 1f } });
        var both2 = independent.GenerateActionList((d, ch) => new ScoreMatrixAction { Origin = d.Target, Target = ch.Target });
        ok &= Check(both2.Count == 2, "IndependentRows: both rows get X");
        return ok;
    }
```

- [ ] **Step 2: Run to verify it fails (RED)** — Rider: unresolved `StanceMatrix`, `IndependentRows`.

- [ ] **Step 3: The flag** — in `ScoreMatrix.cs`, inside `ScoreMatrix<...>` after the `MatrixElements` field add:

```csharp
    /// <summary>
    /// False (default): a chosen choice is removed from every other row, so one surplus is never spent twice. True: rows are
    /// independent decisions (only the chosen row loses it), e.g. one stance decision per rival, where "War" toward one rival
    /// must not take "War" away from another.
    /// </summary>
    public bool IndependentRows { get; set; }
```
and in `GenerateActionList` replace the inner removal loop:

```csharp
                actionList.Add(actionFactory(decision.Key, chosenChoice));
                if (IndependentRows)
                {
                    decision.Value.RemoveAll(v => v.Equals(chosenChoice));
                }
                else
                {
                    foreach (var remaining in MatrixElements)
                    {
                        if (remaining.Value.Count > 0)
                            remaining.Value.RemoveAll(v => v.Equals(chosenChoice));
                    }
                }
                choiceIndex++;
```

- [ ] **Step 4: Create `Assets/Flatspace/GameAI/StanceMatrix.cs`**

```csharp
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace FlatSpace
{
    namespace AI
    {
        public struct StanceDecisionElement : IScoreMatrixDecisionElement
        {
            public string Target { get; set; }
            public float Priority { get; set; }
            public int NumChoices => 1;
        }

        public class StanceDecisionComparer : IComparer<StanceDecisionElement>
        {
            // Higher priority first, then by target name (never compare the name diff against the priority itself).
            public int Compare(StanceDecisionElement x, StanceDecisionElement y)
                => x.Priority == y.Priority ? string.CompareOrdinal(x.Target, y.Target) : y.Priority.CompareTo(x.Priority);
        }

        public struct StanceChoiceElement : IScoreMatrixChoiceElement
        {
            public int Rival;
            public Stance Stance;
            public float Weight;

            public string Target => Rival.ToString(CultureInfo.InvariantCulture);
            public float Cost => 0f;
            public float Surplus => 0f;
            public float Shortage => 0f;

            public bool Equals(IScoreMatrixChoiceElement other)
                => other is StanceChoiceElement s && s.Rival == Rival && s.Stance == Stance;
            public override bool Equals(object obj) => obj is IScoreMatrixChoiceElement s && Equals(s);
            public override int GetHashCode() => Rival * 31 + (int)Stance;
        }

        public struct StanceAction : IScoreMatrixAction
        {
            public int Player;
            public int Rival;
            public Stance Stance;

            public string Origin => Player.ToString(CultureInfo.InvariantCulture);
            public string Target => Rival.ToString(CultureInfo.InvariantCulture);
            public float Cost => 0f;
        }

        /// <summary>
        /// The war-or-peace decision, a ScoreMatrix with IndependentRows: one row per rival, the choices Peace and War weighted
        /// from the hostility score, the stance held now favoured (stanceStickiness) and a row left alone for stanceHoldTurns
        /// after its last change. Pure.
        /// </summary>
        public static class StanceMatrix
        {
            public struct Row
            {
                public int Rival;
                public float Hostility;
                public Stance Current;
                public int TurnsSinceChange;
            }

            public struct Decision
            {
                public int Rival;
                public Stance Stance;
                public float PWar;
            }

            public static float WarProbability(float hostility, GameAIConstants constants)
            {
                var steepness = Math.Max(constants.stanceSteepness, 0.0001f);
                return (float)(1.0 / (1.0 + Math.Exp(-(hostility - constants.stanceMidpoint) / steepness)));
            }

            public static float WarWeight(Row row, GameAIConstants constants)
                => WarProbability(row.Hostility, constants) * (row.Current == Stance.War ? constants.stanceStickiness : 1f);

            public static float PeaceWeight(Row row, GameAIConstants constants)
                => (1f - WarProbability(row.Hostility, constants)) * (row.Current == Stance.Peace ? constants.stanceStickiness : 1f);

            public static List<Decision> Decide(int player, List<Row> rows, GameAIConstants constants)
            {
                var matrix = new ScoreMatrix<StanceDecisionElement, StanceChoiceElement, StanceAction>(new StanceDecisionComparer())
                {
                    IndependentRows = true,
                };
                var pWar = new Dictionary<int, float>();
                var rank = 0f;
                foreach (var row in rows.OrderBy(r => r.Rival))
                {
                    if (row.TurnsSinceChange < constants.stanceHoldTurns) continue;   // held since its last change
                    pWar[row.Rival] = WarProbability(row.Hostility, constants);
                    matrix.MatrixElements.Add(
                        new StanceDecisionElement { Target = row.Rival.ToString(CultureInfo.InvariantCulture), Priority = -rank++ },
                        new List<StanceChoiceElement>
                        {
                            new StanceChoiceElement { Rival = row.Rival, Stance = Stance.Peace, Weight = PeaceWeight(row, constants) },
                            new StanceChoiceElement { Rival = row.Rival, Stance = Stance.War, Weight = WarWeight(row, constants) },
                        });
                }

                var actions = matrix.GenerateActionList(
                    (decision, choice) => new StanceAction { Player = player, Rival = choice.Rival, Stance = choice.Stance },
                    null,
                    choice => choice.Weight);
                return actions
                    .OrderBy(a => a.Rival)
                    .Select(a => new Decision { Rival = a.Rival, Stance = a.Stance, PWar = pWar[a.Rival] })
                    .ToList();
            }
        }
    }
}
```
(`Priority = -rank++` gives unique, descending priorities in rival order, so the row order is the rival order.)

- [ ] **Step 5: `.meta`** — `New-Meta "Assets\Flatspace\GameAI\StanceMatrix.cs"`.

- [ ] **Step 6: Verify (GREEN)** — Rider on `StanceMatrix.cs` and `ScoreMatrix.cs`; the user runs `Run All AI Self-Checks` (the flag touched shared code, so every suite must still pass). Expected: `ALL 11 SUITES PASSED`.

- [ ] **Step 7: Commit**

```bash
git add Assets/Flatspace/GameAI/StanceMatrix.cs Assets/Flatspace/GameAI/StanceMatrix.cs.meta Assets/Flatspace/GameAI/ScoreMatrix.cs Assets/Editor/DiplomacySelfCheck.cs
git commit -m "feat: ScoreMatrix IndependentRows and the StanceMatrix war-or-peace decision"
```

---

### Task 4: Amass behaviour (IsConsolidateLike, tables, the routine)

**Files:**
- Modify: `PlayerAI.cs`, `ShipTransportPlanner.cs`, `Assets/Editor/DiplomacySelfCheck.cs`

**Interfaces:**
- Produces: `PlayerAI.IsConsolidateLike(AIStrategy) : bool` (public static); Amass entries in `ResearchWeightTable` and `IndustryWeightTable`; `ProcessResults` runs the Expand routine for Amass.

- [ ] **Step 1: Write the failing tests** — add `ok &= RunAmassTablesCheck(); ok &= RunConsolidateLikeSeamsCheck(); ok &= RunAmassRunsTheRoutineCheck();` to `RunChecks` and these methods (the resource fixture mirrors `PlayerAIResourceSelfCheck.RunFoodShortageScopingCheck`):

```csharp
    private static CatalogItem Item(string subType)
    {
        var item = ScriptableObject.CreateInstance<CatalogItem>();
        item.subType = subType;
        return item;
    }

    public static bool RunAmassTablesCheck()
    {
        var ok = true;
        var items = new List<CatalogItem>();
        try
        {
            CatalogItem I(string subType) { var i = Item(subType); items.Add(i); return i; }
            var research = new (string subType, float weight)[]
                { ("Food", 1.0f), ("Industry", 2.5f), ("Grotsits", 2.0f), ("Research", 1.0f), ("ColonyShip", 0.5f), ("Warship", 4.0f) };
            foreach (var (subType, weight) in research)
                ok &= Check(Near(PlayerAI.GetResearchWeight(I(subType), PlayerAI.AIStrategy.AIStrategyAmass), weight),
                    $"Amass research weight for {subType} is {weight}");

            var industry = new (string subType, float weight)[]
            {
                ("Food", 1.0f), ("Industry", 1.5f), ("Grotsits", 1.5f), ("Research", 0.5f), ("ColonyShip", 0.5f),
                ("Warship", 4.0f), ("WarshipUpdate", 4.0f),
            };
            foreach (var (subType, weight) in industry)
                ok &= Check(Near(PlayerAI.GetIndustryStrategyWeight(I(subType), PlayerAI.AIStrategy.AIStrategyAmass), weight),
                    $"Amass industry weight for {subType} is {weight}");

            ok &= Check(PlayerAI.GetIndustryStrategyWeight(I("Warship"), PlayerAI.AIStrategy.AIStrategyAmass)
                        > PlayerAI.GetIndustryStrategyWeight(I("Warship"), PlayerAI.AIStrategy.AIStrategyConsolidate),
                "Amass builds warships harder than Consolidate");
            ok &= Check(PlayerAI.GetIndustryStrategyWeight(I("Warship"), PlayerAI.AIStrategy.AIStrategyConsolidate) == 2.5f,
                "Consolidate's Warship weight is unchanged");
        }
        finally { foreach (var i in items) Object.DestroyImmediate(i); }
        return ok;
    }

    // Every "Consolidate only" rule now covers Amass: the warship multiplier, the colonization tilt, the planner's garrisons.
    public static bool RunConsolidateLikeSeamsCheck()
    {
        var ok = true;
        ok &= Check(PlayerAI.IsConsolidateLike(PlayerAI.AIStrategy.AIStrategyConsolidate)
                    && PlayerAI.IsConsolidateLike(PlayerAI.AIStrategy.AIStrategyAmass)
                    && !PlayerAI.IsConsolidateLike(PlayerAI.AIStrategy.AIStrategyExpand)
                    && !PlayerAI.IsConsolidateLike(PlayerAI.AIStrategy.AIStrategyNone),
            "IsConsolidateLike is Consolidate and Amass only");

        var go = new GameObject("DipSelfCheckMap_Seams");
        var playerGo = new GameObject("DipSelfCheckPlayer_Seams");
        var template = WarshipSelfCheck.MakeTemplate();
        var constants = WarshipSelfCheck.MakeConstants(template);
        try
        {
            constants.chokepointPercentile = 0.9f;
            constants.colonizationChokepointWeight = 0.5f;
            constants.garrisonOuter = 6;
            constants.garrisonHighTraffic = 2;
            constants.warshipShortfallBoost = 2f;
            constants.warshipsPerColonizedPlanet = 100f;
            var map = go.AddComponent<GameAIMap>();
            map.GameAIMapInit(ChokepointSelfCheck.HubSpawns(), constants);
            foreach (var n in new[] { "A", "H", "X1", "X2", "X3", "Y" })
            {
                map.GetPlanet(n).Owner = 0;
                map.GetPlanet(n).Population.Add(new Planet.Inhabitant { Player = 0 });
            }

            var player = playerGo.AddComponent<Player>();
            var ai = playerGo.AddComponent<PlayerAI>();
            ai.Player = player;
            ai.AIMap = map;
            player.playerID = 0;

            var consolidate = new ShipTransportPlanner(map, 0, PlayerAI.AIStrategy.AIStrategyConsolidate);
            var amass = new ShipTransportPlanner(map, 0, PlayerAI.AIStrategy.AIStrategyAmass);
            var expand = new ShipTransportPlanner(map, 0, PlayerAI.AIStrategy.AIStrategyExpand);
            foreach (var n in new[] { "H", "X1", "A" })
                ok &= Check(amass.MaintainsGarrison(map.GetPlanet(n)) == consolidate.MaintainsGarrison(map.GetPlanet(n)),
                    $"Amass garrisons {n} exactly as Consolidate does");
            ok &= Check(consolidate.MaintainsGarrison(map.GetPlanet("H")) && !consolidate.MaintainsGarrison(map.GetPlanet("X1")),
                "precondition: the chokepoint H garrisons under Consolidate, a leaf does not");
            ok &= Check(expand.MaintainsGarrison(map.GetPlanet("X1")), "Expand still garrisons every planet");
            ok &= Check(amass.TargetRank(map.GetPlanet("H")) == consolidate.TargetRank(map.GetPlanet("H")),
                "Amass ranks garrison targets as Consolidate does");

            ok &= Check(Near(ai.ComputeWarshipMultiplier(PlayerAI.AIStrategy.AIStrategyAmass),
                             ai.ComputeWarshipMultiplier(PlayerAI.AIStrategy.AIStrategyConsolidate)),
                "the warship shortfall multiplier is the same under Amass and Consolidate");
            ok &= Check(Near(ai.ComputeWarshipMultiplier(PlayerAI.AIStrategy.AIStrategyExpand), 1f)
                        && ai.ComputeWarshipMultiplier(PlayerAI.AIStrategy.AIStrategyAmass) > 1f,
                "Expand has no multiplier; a fleet shortfall boosts Amass above 1");

            ai.Strategy = PlayerAI.AIStrategy.AIStrategyAmass;
            ok &= Check(Near(ai.ColonizationCostDivisor("H"), 1.5f), "Amass tilts colonization toward the chokepoint like Consolidate (1 + 0.5 x 1)");
            ai.Strategy = PlayerAI.AIStrategy.AIStrategyExpand;
            ok &= Check(Near(ai.ColonizationCostDivisor("H"), 1f), "Expand never tilts");
        }
        finally
        {
            Object.DestroyImmediate(playerGo);
            Object.DestroyImmediate(go);
            Object.DestroyImmediate(constants);
            Object.DestroyImmediate(template);
        }
        return ok;
    }

    // Amass used to be an empty case (a frozen player). It now runs the Expand routine: a food shortage is answered.
    public static bool RunAmassRunsTheRoutineCheck()
    {
        var ok = true;
        var mapGo = new GameObject("DipSelfCheckMap_Amass");
        var playerGo = new GameObject("DipSelfCheckPlayer_Amass");
        var constants = ScriptableObject.CreateInstance<GameAIConstants>();
        try
        {
            constants.defaultTravelSpeed = 1f;
            constants.maxPathNodesForResourceDistribution = 10;
            var map = mapGo.AddComponent<GameAIMap>();
            map.GameAIMapInit(new List<PlanetSpawnData>
            {
                ChokepointSelfCheck.Spawn("Shortage", 0f, 0f, new[] { "Surplus" }),
                ChokepointSelfCheck.Spawn("Surplus", 100f, 0f, new[] { "Shortage" }),
            }, constants);
            map.GetPlanet("Surplus").Population.Add(new Planet.Inhabitant { Player = 0 });

            var player = playerGo.AddComponent<Player>();
            var ai = playerGo.AddComponent<PlayerAI>();
            ai.Player = player;
            ai.AIMap = map;
            player.playerID = 0;
            ai.Strategy = PlayerAI.AIStrategy.AIStrategyAmass;

            var results = new List<Planet.PlanetUpdateResult>
            {
                new Planet.PlanetUpdateResult("Shortage",
                    Planet.PlanetUpdateResult.PlanetUpdateResultType.PlanetUpdateResultTypeFoodShortage, 10f, playerID: 0),
                new Planet.PlanetUpdateResult("Surplus",
                    Planet.PlanetUpdateResult.PlanetUpdateResultType.PlanetUpdateResultTypeFoodSurplus, 20f, playerID: 0),
            };
            var orders = new List<GameAI.GameAIOrder>();
            ai.ProcessResults(results, orders);
            ok &= Check(orders.Exists(o => o.Type == GameAI.GameAIOrder.OrderType.OrderTypeFoodTransport
                                           && o.Origin == "Surplus" && o.Target == "Shortage"),
                "an Amass player still ships food to its shortage (the economy keeps running)");
            ok &= Check(ai.Strategy == PlayerAI.AIStrategy.AIStrategyAmass, "ProcessResults does not change an Amass player's strategy by itself");
        }
        finally
        {
            Object.DestroyImmediate(playerGo);
            Object.DestroyImmediate(mapGo);
            Object.DestroyImmediate(constants);
        }
        return ok;
    }
```

- [ ] **Step 2: Run to verify it fails (RED)** — Rider: unresolved `IsConsolidateLike`; after that exists, the Editor run fails the Amass table and routine assertions (neutral 1.0 weights, no order).

- [ ] **Step 3: The helper** — in `PlayerAI.cs`, directly under the `AIStrategy` enum:

```csharp
            /// <summary>
            /// Consolidate and Amass share every rule written as "Consolidate only" (the warship fleet-shortfall boost, the
            /// ColonyShip boost while targets remain, the chokepoint colonization tilt, the planner's garrisons, the assault and the
            /// blockade-breaking targets). Amass differs only through its own weight tables and the war gate on the assault.
            /// </summary>
            public static bool IsConsolidateLike(AIStrategy strategy)
                => strategy == AIStrategy.AIStrategyConsolidate || strategy == AIStrategy.AIStrategyAmass;
```

- [ ] **Step 4: The routine** — in `ProcessResults`, replace the `switch (Strategy)` block with:

```csharp
                switch (Strategy)
                {
                    case AIStrategy.AIStrategyExpand:
                    case AIStrategy.AIStrategyConsolidate:
                    case AIStrategy.AIStrategyAmass:
                        // Consolidate and Amass have no routine of their own; they play like Expand (Amass with its own
                        // weight tables, see the research and industry tables below).
                        ProcessResultsStrategyExpand(results, Player, ref orders);
                        break;
                }
```

- [ ] **Step 5: The seams** — replace each strategy test with the helper:
  - `ColonizationCostDivisor`: `if (Strategy != AIStrategy.AIStrategyConsolidate) return 1f;` → `if (!IsConsolidateLike(Strategy)) return 1f;` (also change its comment's "Consolidate tilts" to "Consolidate and Amass tilt").
  - `ComputeWarshipMultiplier` (private overload): `if (strategy != AIStrategy.AIStrategyConsolidate) return 1f;` → `if (!IsConsolidateLike(strategy)) return 1f;` and its `<summary>` "1 for anything but Consolidate" → "1 for Expand and None".
  - `GetIndustrySituationalWeightMultiplier`: both `if (Strategy == AIStrategy.AIStrategyConsolidate)` (ColonyShip x2 and the Warship cap) → `if (IsConsolidateLike(Strategy))`.
  - `PlanShipActions`: `if (Strategy != AIStrategy.AIStrategyConsolidate)` → `if (!IsConsolidateLike(Strategy))`.
  - `ShipTransportPlanner.cs`: `MaintainsGarrison`: `_strategy != PlayerAI.AIStrategy.AIStrategyConsolidate` → `!PlayerAI.IsConsolidateLike(_strategy)`; `TargetRank`: `_strategy != PlayerAI.AIStrategy.AIStrategyConsolidate` → `!PlayerAI.IsConsolidateLike(_strategy)`; the `LastRound` line: `_strategy == PlayerAI.AIStrategy.AIStrategyConsolidate` → `PlayerAI.IsConsolidateLike(_strategy)`.

- [ ] **Step 6: The tables** — in `PlayerAI.cs` add next to the Consolidate research table and wire it:

```csharp
            private static readonly Dictionary<string, float> AmassResearchWeights =
                new Dictionary<string, float>
                {
                    { "Food",          1.0f },
                    { "Industry",      2.5f },
                    { "Grotsits",      2.0f },
                    { "Research",      1.0f },
                    { "ColonyShip",    0.5f },
                    { "Warship",       4.0f },  // at war: weapons first
                };
```
and replace `// AIStrategyAmass — add when needed` in `ResearchWeightTable` with `{ AIStrategy.AIStrategyAmass, AmassResearchWeights },`. Next to the industry tables:

```csharp
            private static readonly Dictionary<string, float> AmassIndustryWeights =
                new Dictionary<string, float>
                {
                    { "Food",          1.0f },
                    { "Industry",      1.5f },
                    { "Grotsits",      1.5f },
                    { "Research",      0.5f },
                    { "ColonyShip",    0.5f },  // a player at war colonizes less
                    { "Warship",       4.0f },
                    { "WarshipUpdate", 4.0f },  // same as Warship
                };
```
and `{ AIStrategy.AIStrategyAmass, AmassIndustryWeights },` in `IndustryWeightTable`. Update the two "Add an entry for AIStrategyAmass when needed" comments to "Amass has its own table below".

- [ ] **Step 7: Verify (GREEN)** — Rider on `PlayerAI.cs`, `ShipTransportPlanner.cs`; the user runs `Run All AI Self-Checks`. Expected: `ALL 11 SUITES PASSED` (the older suites never use Amass, so nothing else moves).

- [ ] **Step 8: Commit**

```bash
git add Assets/Flatspace/GameAI/PlayerAI.cs Assets/Flatspace/GameAI/ShipTransportPlanner.cs Assets/Editor/DiplomacySelfCheck.cs
git commit -m "feat: Amass runs the Expand routine, IsConsolidateLike seams and Amass weight tables"
```

---

### Task 5: The war gate on the assault and the wanted fleet

**Files:**
- Modify: `AssaultPlanner.cs`, `PlayerAI.cs`, `Assets/Editor/DiplomacySelfCheck.cs`

**Interfaces:**
- Consumes: `DiplomacyState.WarRivals`, `PlayerKnowledge.ContactPlayers`.
- Produces: `AssaultPlanner(GameAIMap, int, BlockadeView = null, WarshipStats = null, BlockadeMemory = null, int turn = 0, ISet<int> warRivals = null)`; `PlayerAI.WarRivals() : SortedSet<int>`; `PlayerAI.AssaultWarFilter() : ISet<int>` (null while diplomacy is off); `PlayerAI.WarForcedRivals : IReadOnlyCollection<int>` is added in Task 6.

- [ ] **Step 1: Write the failing tests** — add `ok &= RunAssaultGateCheck(); ok &= RunWarRivalsCheck(); ok &= RunWantedFleetGateCheck();` to `RunChecks` and:

```csharp
    // A(P0, warships) - B - C(P2 population) - D(P1 population). The assault attacks only the players in the war set; a null
    // set (diplomacy off) keeps today's rule, every other player is an enemy.
    public static bool RunAssaultGateCheck()
    {
        var ok = true;
        using (var f = Fixture.Line())
        {
            f.Colonize("C", 2);
            f.Colonize("D", 1);
            f.Ships("A", 0, 2);
            f.Ships("D", 1, 2);
            f.Ships("C", 2, 3);
            f.Know();
            var stats = new WarshipStats(f.Research);
            AssaultPlanner Planner(ISet<int> war) => new AssaultPlanner(f.Map, 0, null, stats, null, 0, war);

            ok &= Check(Planner(null).ChooseEnemyTarget().PlanetName == "C", "no war set (legacy): the cheapest enemy planet, C");
            ok &= Check(Planner(new HashSet<int> { 1 }).ChooseEnemyTarget().PlanetName == "D", "at war with 1 only: D");
            ok &= Check(Planner(new HashSet<int> { 2 }).ChooseEnemyTarget().PlanetName == "C", "at war with 2 only: C");
            ok &= Check(Planner(new HashSet<int>()).ChooseEnemyTarget() == null, "at war with nobody: no enemy target");

            ok &= Check(Planner(null).HasKnownEnemyPlanet() && Planner(new HashSet<int> { 1 }).HasKnownEnemyPlanet(),
                "a known enemy planet exists in legacy mode and at war with 1");
            ok &= Check(!Planner(new HashSet<int>()).HasKnownEnemyPlanet(), "at peace with everyone there is no known enemy planet");

            ok &= Check(Planner(null).EnemyWarshipTotal() == 5, "legacy: 2 + 3 enemy warships");
            ok &= Check(Planner(new HashSet<int> { 1 }).EnemyWarshipTotal() == 2, "at war with 1: only its 2 ships size the force");
            ok &= Check(Planner(new HashSet<int> { 2 }).EnemyWarshipTotal() == 3, "at war with 2: only its 3 ships");
            ok &= Check(Planner(new HashSet<int>()).EnemyWarshipTotal() == 0, "at peace: no enemy fleet");

            // Ownerless ships are never an enemy fleet, in either mode.
            f.Ships("B", Planet.NoOwner, 4);
            ok &= Check(Planner(null).EnemyWarshipTotal() == 5, "ownerless ships are not an enemy fleet");
        }
        return ok;
    }

    // WarRivals on PlayerAI: legacy = every contact player; diplomacy on = the derived war set.
    public static bool RunWarRivalsCheck()
    {
        var ok = true;
        using (var f = Fixture.Line())
        {
            f.Colonize("B", 1);
            f.Colonize("C", 2);
            f.Ships("B", Planet.NoOwner, 1);
            f.Know();

            ok &= Check(!f.Map.Diplomacy.Enabled, "precondition: a map a fixture builds is in legacy mode");
            ok &= Check(f.AI.WarRivals().SetEquals(new[] { 1, 2 }), "legacy: every contact player is an enemy (ownerless ships are not a player)");
            ok &= Check(f.AI.AssaultWarFilter() == null, "legacy: the assault gets no filter");

            f.Map.Diplomacy.Enabled = true;
            ok &= Check(f.AI.WarRivals().Count == 0, "diplomacy on, all Peace: no war");
            ok &= Check(f.AI.AssaultWarFilter() != null && f.AI.AssaultWarFilter().Count == 0, "diplomacy on: an empty filter, nobody to attack");
            f.Map.Diplomacy.SetStance(0, 1, Stance.War, 3);
            f.Map.Diplomacy.SetStance(2, 0, Stance.War, 3);
            ok &= Check(f.AI.WarRivals().SetEquals(new[] { 1, 2 }), "my own War on 1 and 2's War on me (I have contact) both make war");
        }
        return ok;
    }

    // WantedWarships adds the assault force only while there is a known enemy planet in the war set.
    public static bool RunWantedFleetGateCheck()
    {
        var ok = true;
        using (var f = Fixture.Line())
        {
            f.Constants.warshipsPerColonizedPlanet = 100f;
            f.Constants.assaultMinimumShips = 3;
            f.Constants.garrisonOuter = 6;
            f.Colonize("D", 1);
            f.Ships("D", 1, 2);
            f.Know();
            f.AI.Strategy = PlayerAI.AIStrategy.AIStrategyConsolidate;

            var legacy = f.AI.WantedWarships();                       // outer garrison + the assault force
            f.Map.Diplomacy.Enabled = true;
            var atPeace = f.AI.WantedWarships();                      // outer garrison only
            f.Map.Diplomacy.SetStance(0, 1, Stance.War, 3);
            var atWar = f.AI.WantedWarships();

            ok &= Check(atPeace == f.Constants.garrisonOuter, "at peace the wanted fleet is just the garrisons");
            ok &= Check(legacy > atPeace && atWar == legacy, "legacy and war both add the assault force; peace does not");
        }
        return ok;
    }
```

- [ ] **Step 2: Run to verify it fails (RED)** — Rider: the `AssaultPlanner` seventh parameter, `WarRivals`, `AssaultWarFilter` are unresolved.

- [ ] **Step 3: The planner** — in `AssaultPlanner.cs`: add a field and constructor parameter, then use it:

```csharp
            private readonly ISet<int> _warRivals;

            /// <summary>
            /// `view`, `stats` and `memory` are optional: without a view nothing is a blockade target and the planner behaves
            /// exactly as it did before blockade breaking existed. `turn` is only used to test BlockadeMemory for recent cuts.
            /// `warRivals` is the set of players I am at war with: only their planets are enemy-occupied and only their docked
            /// warships size the force. Null (the default, and diplomacy off) means every other player is an enemy.
            /// </summary>
            public AssaultPlanner(GameAIMap map, int playerId, BlockadeView view = null, WarshipStats stats = null,
                BlockadeMemory memory = null, int turn = 0, ISet<int> warRivals = null)
            {
                _map = map;
                _playerId = playerId;
                _constants = map.GameAIConstants;
                _view = view;
                _stats = stats;
                _memory = memory;
                _turn = turn;
                _warRivals = warRivals;
            }

            private bool IsRival(int player) => player != _playerId && (_warRivals == null || _warRivals.Contains(player));
```
Replace `IsEnemyOccupied`:

```csharp
            /// <summary>A player I am at war with has population here (without a war set: any other player; same test as PlayerKnowledge.HasContact).</summary>
            public bool IsEnemyOccupied(Planet planet)
                => planet.Population.Exists(p => IsRival(p.Player));
```
and in `EnemyWarshipTotal` replace the ship test `s.Owner >= 0 && s.Owner != _playerId` with `s.Owner >= 0 && IsRival(s.Owner)`. Update the class `<summary>` first line to "Consolidate and Amass only."

- [ ] **Step 4: The war set on PlayerAI** — add, near `TryEnterConsolidate`:

```csharp
            // ── Diplomacy ────────────────────────────────────────────────────

            /// <summary>
            /// The players I am at war with right now. Diplomacy off (legacy, and any map a self-check builds directly): every
            /// player I have contact with is an enemy, as before. On: my own War stances plus the wars rivals declared on me
            /// that I have contact with (DiplomacyState.WarRivals).
            /// </summary>
            public SortedSet<int> WarRivals()
            {
                var contact = AIMap.Knowledge.ContactPlayers(AIMap, Player.playerID);
                return AIMap.Diplomacy.Enabled
                    ? AIMap.Diplomacy.WarRivals(Player.playerID, contact)
                    : new SortedSet<int>(contact);
            }

            /// <summary>What the assault may attack: null (every other player) while diplomacy is off, else the war set.</summary>
            public ISet<int> AssaultWarFilter() => AIMap.Diplomacy.Enabled ? WarRivals() : null;
```
In `WantedWarships` change the assault line to `var assault = new AssaultPlanner(AIMap, Player.playerID, warRivals: AssaultWarFilter());`. In `PlanShipActions` change the construction to `new AssaultPlanner(AIMap, Player.playerID, _blockadeView, stats, _blockadeMemory, turnNumber, AssaultWarFilter())`. Update the `WantedWarships` summary: "plus the assault's required force whenever a known planet of a player I am at war with exists".

- [ ] **Step 5: Verify (GREEN)** — Rider on `AssaultPlanner.cs`, `PlayerAI.cs`; the user runs `Run All AI Self-Checks`. Expected: all 11 suites pass (older suites build maps in legacy mode, where `AssaultWarFilter()` is null, so their assaults are unchanged).

- [ ] **Step 6: Commit**

```bash
git add Assets/Flatspace/GameAI/AssaultPlanner.cs Assets/Flatspace/GameAI/PlayerAI.cs Assets/Editor/DiplomacySelfCheck.cs
git commit -m "feat: the assault and the wanted fleet only count players at war"
```

---

### Task 6: UpdateDiplomacy, the strategy switch, the game wiring and the log lines

**Files:**
- Modify: `PlayerAI.cs`, `GameAI.cs`, `AITuningLogger.cs`, `Assets/Editor/DiplomacySelfCheck.cs`

**Interfaces:**
- Consumes: Tasks 1 to 5.
- Produces: `PlayerAI.UpdateDiplomacy(int turn)` (public); `PlayerAI.WarForcedRivals : IReadOnlyCollection<int>`; `AITuningLogger.LogStance`, `LogWarForced`, `LogHostility`.

- [ ] **Step 1: Write the failing tests** — add `ok &= RunUpdateDiplomacyCheck(); ok &= RunStrategySwitchCheck(); ok &= RunForcedWarCheck(); ok &= RunCutsAndLegacyCheck();` to `RunChecks` and:

```csharp
    // P1 holds B (a neighbour of player 0's A). Diplomacy on.
    private static Fixture WithRival()
    {
        var f = Fixture.Line();
        f.Colonize("B", 1);
        f.Know(2);
        f.Map.Diplomacy.Enabled = true;
        f.AI.Strategy = PlayerAI.AIStrategy.AIStrategyConsolidate;
        // A very steep curve: below the midpoint the war probability is exactly 0, above it exactly 1 (no roulette luck).
        f.Constants.stanceSteepness = 0.01f;
        f.Constants.stanceMidpoint = 30f;
        return f;
    }

    // One turn of hostility and the stance decision.
    public static bool RunUpdateDiplomacyCheck()
    {
        var ok = true;
        using (var f = WithRival())
        {
            f.Ships("A", 0, 2);                                    // I am much stronger: the strength term is +2
            f.Ships("B", 1, 1);                                    // a rival ship beside my planet: near term 0.5
            f.Map.Diplomacy.RecordCut(0, 1);
            f.Map.Diplomacy.RecordCut(0, 1);                       // two cuts: 10
            f.AI.UpdateDiplomacy(10);

            var pair = f.Map.Diplomacy.Get(0, 1);
            ok &= Check(Near(pair.CutsTerm, 10f) && Near(pair.NearTerm, 0.5f) && Near(pair.StrengthTerm, 1f),
                "the terms are charged: 2 cuts x 5, 1 ship x 0.5, log2(2100/1050) = 1");
            ok &= Check(Near(pair.Hostility, 10f + 0.5f + 1f), "hostility = 10 + 0.5 + 1 after one turn from 0");
            ok &= Check(pair.NearShips == 1 && pair.MyStrength > pair.RivalStrength, "the log-only numbers are stored");
            ok &= Check(pair.Stance == Stance.Peace, "below the midpoint the stance stays Peace");
            ok &= Check(f.AI.Strategy == PlayerAI.AIStrategy.AIStrategyConsolidate, "and the strategy stays Consolidate");

            // Above the midpoint: War, and the strategy follows in the same call.
            pair.Hostility = 100f;
            f.Map.Diplomacy.Set(0, 1, pair);
            f.AI.UpdateDiplomacy(20);
            ok &= Check(f.Map.Diplomacy.StanceToward(0, 1) == Stance.War, "hostility 95+ is War");
            ok &= Check(f.AI.Strategy == PlayerAI.AIStrategy.AIStrategyAmass, "war switches Consolidate to Amass");
            ok &= Check(Near(f.Map.Diplomacy.Get(0, 1).PWar, 1f), "the war probability is kept for the log");
        }
        return ok;
    }

    public static bool RunStrategySwitchCheck()
    {
        var ok = true;
        using (var f = WithRival())
        {
            f.Map.Diplomacy.SetStance(0, 1, Stance.War, 5);
            f.AI.UpdateDiplomacy(6);                               // inside the hold: the stance is kept, the war is real
            ok &= Check(f.AI.Strategy == PlayerAI.AIStrategy.AIStrategyAmass, "any war: Consolidate becomes Amass");

            f.Map.Diplomacy.SetStance(0, 1, Stance.Peace, 7);
            f.AI.UpdateDiplomacy(8);
            ok &= Check(f.AI.Strategy == PlayerAI.AIStrategy.AIStrategyConsolidate, "peace with everyone: Amass returns to Consolidate");

            f.AI.Strategy = PlayerAI.AIStrategy.AIStrategyExpand;
            f.Map.Diplomacy.SetStance(0, 1, Stance.War, 9);
            f.AI.UpdateDiplomacy(10);
            ok &= Check(f.AI.Strategy == PlayerAI.AIStrategy.AIStrategyExpand, "an Expand player is never switched by diplomacy");
            f.AI.Strategy = PlayerAI.AIStrategy.AIStrategyNone;
            f.AI.UpdateDiplomacy(11);
            ok &= Check(f.AI.Strategy == PlayerAI.AIStrategy.AIStrategyNone, "nor a None player");

            // Review focus 1: a war survives losing contact; the declarer does not flicker back to Consolidate.
            f.AI.Strategy = PlayerAI.AIStrategy.AIStrategyConsolidate;
            f.P("B").Population.Clear();
            f.Know(2);
            ok &= Check(f.Map.Knowledge.ContactPlayers(f.Map, 0).Count == 0, "precondition: the rival left, no contact now");
            f.AI.UpdateDiplomacy(12);
            ok &= Check(f.Map.Diplomacy.StanceToward(0, 1) == Stance.War && f.AI.Strategy == PlayerAI.AIStrategy.AIStrategyAmass,
                "my own War stance stands without contact: still at war, still Amass");

            // War with one of two rivals keeps Amass; peace with that one but war with the other too.
            f.Colonize("C", 2);
            f.Know(3);
            f.Map.Diplomacy.SetStance(0, 2, Stance.War, 20);
            f.Map.Diplomacy.SetStance(0, 1, Stance.Peace, 20);
            f.AI.UpdateDiplomacy(21);
            ok &= Check(f.AI.Strategy == PlayerAI.AIStrategy.AIStrategyAmass, "at war with one of two rivals: Amass stays");
            f.Map.Diplomacy.SetStance(0, 2, Stance.Peace, 22);
            f.AI.UpdateDiplomacy(23);
            ok &= Check(f.AI.Strategy == PlayerAI.AIStrategy.AIStrategyConsolidate, "peace with both: back to Consolidate");
        }
        return ok;
    }

    // A war the rival declared forces mine once I have contact (spec rule B), and ends when the rival returns to Peace.
    public static bool RunForcedWarCheck()
    {
        var ok = true;
        using (var f = WithRival())
        {
            f.Map.Diplomacy.SetStance(1, 0, Stance.War, 5);        // player 1 declares on me
            f.AI.UpdateDiplomacy(6);
            ok &= Check(f.Map.Diplomacy.StanceToward(0, 1) == Stance.Peace, "my own stance is not changed by being declared on");
            ok &= Check(f.AI.Strategy == PlayerAI.AIStrategy.AIStrategyAmass, "I have contact: the declaration forces me into war and Amass");
            ok &= Check(f.AI.WarForcedRivals.Contains(1), "the forced war is tracked for the WarForced log line");

            // Review focus 2: it ends when the declarer returns to Peace while contact holds.
            f.Map.Diplomacy.SetStance(1, 0, Stance.Peace, 7);
            f.AI.UpdateDiplomacy(8);
            ok &= Check(f.AI.Strategy == PlayerAI.AIStrategy.AIStrategyConsolidate && f.AI.WarForcedRivals.Count == 0,
                "the declarer's Peace ends the forced war and I return to Consolidate");
        }

        using (var f = WithRival())
        {
            f.P("B").Population.Clear();
            f.Know(2);                                             // no contact with player 1
            f.Map.Diplomacy.SetStance(1, 0, Stance.War, 5);
            f.AI.UpdateDiplomacy(6);
            ok &= Check(f.AI.Strategy == PlayerAI.AIStrategy.AIStrategyConsolidate && f.AI.WarForcedRivals.Count == 0,
                "a declaration from a player I have never met changes nothing until contact");
        }
        return ok;
    }

    // Review focus 4 and 5: cuts by a blocker I have no contact with are dropped; diplomacy off is legacy.
    public static bool RunCutsAndLegacyCheck()
    {
        var ok = true;
        using (var f = WithRival())
        {
            f.Map.Diplomacy.RecordCut(0, 2);                       // player 2: no contact
            f.AI.UpdateDiplomacy(10);
            ok &= Check(f.Map.Diplomacy.TakeCuts(0, 2) == 0, "a cut by a player I have no contact with does not wait around");
            f.Colonize("C", 2);
            f.Know(3);
            f.AI.UpdateDiplomacy(11);
            ok &= Check(Near(f.Map.Diplomacy.Get(0, 2).CutsTerm, 0f), "so it cannot inflate hostility once we do meet");
        }

        using (var f = WithRival())
        {
            f.Map.Diplomacy.Enabled = false;                       // legacy
            f.Map.Diplomacy.SetStance(0, 1, Stance.War, 5);
            f.AI.UpdateDiplomacy(6);
            ok &= Check(f.AI.Strategy == PlayerAI.AIStrategy.AIStrategyConsolidate, "legacy: UpdateDiplomacy does nothing, never Amass");
            ok &= Check(f.AI.WarRivals().SetEquals(new[] { 1 }) && f.AI.AssaultWarFilter() == null,
                "legacy: every contact player is an enemy and the assault gets no filter");
            ok &= Check(Near(f.Map.Diplomacy.Get(0, 1).Hostility, 0f), "legacy: no hostility is computed");
        }
        return ok;
    }
```

- [ ] **Step 2: Run to verify it fails (RED)** — Rider: unresolved `UpdateDiplomacy`, `WarForcedRivals`.

- [ ] **Step 3: The log lines** — in `AITuningLogger.cs`, after `LogChokepointGarrison`:

```csharp
    /// <summary>A stance changed: Stance|rival|Peace or War|hostility|cutsTerm|nearTerm|strengthTerm|pWar (on a change only).</summary>
    public static void LogStance(int turnNumber, int playerId, int rival, string stance, float hostility, float cutsTerm,
        float nearTerm, float strengthTerm, float pWar)
    {
        if (_currentLogPath == null) return;
        var ci = System.Globalization.CultureInfo.InvariantCulture;
        AppendLines(new List<string> { FormatLine(turnNumber, playerId, "Stance", rival.ToString(ci), stance,
            hostility.ToString("0.#", ci), cutsTerm.ToString("0.#", ci), nearTerm.ToString("0.#", ci),
            strengthTerm.ToString("0.##", ci), pWar.ToString("0.##", ci)) });
    }

    /// <summary>A war arrived or ended through the rival's stance: WarForced|rival|Start or End (on a change only).</summary>
    public static void LogWarForced(int turnNumber, int playerId, int rival, bool started)
    {
        if (_currentLogPath == null) return;
        AppendLines(new List<string> { FormatLine(turnNumber, playerId, "WarForced",
            rival.ToString(System.Globalization.CultureInfo.InvariantCulture), started ? "Start" : "End") });
    }

    /// <summary>Every 25 turns per player and rival with contact: Hostility|rival|hostility|myStrength|rivalStrength|nearShips.</summary>
    public static void LogHostility(int turnNumber, int playerId, int rival, float hostility, float myStrength,
        float rivalStrength, int nearShips)
    {
        if (_currentLogPath == null) return;
        var ci = System.Globalization.CultureInfo.InvariantCulture;
        AppendLines(new List<string> { FormatLine(turnNumber, playerId, "Hostility", rival.ToString(ci),
            hostility.ToString("0.#", ci), myStrength.ToString("0", ci), rivalStrength.ToString("0", ci),
            nearShips.ToString(ci)) });
    }
```

- [ ] **Step 4: UpdateDiplomacy** — in `PlayerAI.cs`, in the Diplomacy region added in Task 5:

```csharp
            // Rivals whose forced war (declared on me, I have contact) was already logged as Start, so Start and End are logged
            // on a change only. Log-only state: not saved (a load logs each current forced war once more).
            private readonly HashSet<int> _warForcedLogged = new HashSet<int>();

            /// <summary>The rivals whose declaration currently forces me into war (I have not declared on them). Public for the self-check.</summary>
            public IReadOnlyCollection<int> WarForcedRivals => _warForcedLogged;

            /// <summary>
            /// Once per turn, after the strategy routine: updates the hostility score toward each rival I have contact with,
            /// lets the stance matrix decide each rival that is not held, logs the changes, then switches Consolidate to Amass
            /// while I am at war with anyone and Amass back to Consolidate once I am at war with nobody. Expand and None are
            /// never switched here (first contact still moves Expand to Consolidate in TryEnterConsolidate). Does nothing while
            /// diplomacy is off. Public (and free of Gameboard.Instance) so the self-check can drive it directly.
            /// </summary>
            public void UpdateDiplomacy(int turn)
            {
                var diplomacy = AIMap.Diplomacy;
                if (!diplomacy.Enabled) return;

                var me = Player.playerID;
                var constants = AIMap.GameAIConstants;
                var stats = new WarshipStats(ResearchCatalog != null ? ResearchCatalog.catalogItems : null);
                var contact = AIMap.Knowledge.ContactPlayers(AIMap, me);
                var myStrength = FleetStrength.Mine(AIMap, me, stats);

                var rows = new List<StanceMatrix.Row>();
                foreach (var rival in contact)
                {
                    var pair = diplomacy.Get(me, rival);
                    var rivalStrength = FleetStrength.VisibleOf(AIMap, me, rival, stats);
                    var nearShips = HostilityCalculator.CountNearShips(AIMap, me, rival);
                    var result = HostilityCalculator.Compute(new HostilityCalculator.Inputs
                    {
                        Previous = pair.Hostility,
                        Cuts = diplomacy.TakeCuts(me, rival),
                        NearShips = nearShips,
                        MyStrength = myStrength,
                        RivalStrength = rivalStrength,
                    }, constants);
                    pair.Hostility = result.Hostility;
                    pair.CutsTerm = result.CutsTerm;
                    pair.NearTerm = result.NearTerm;
                    pair.StrengthTerm = result.StrengthTerm;
                    pair.MyStrength = myStrength;
                    pair.RivalStrength = rivalStrength;
                    pair.NearShips = nearShips;
                    diplomacy.Set(me, rival, pair);
                    rows.Add(new StanceMatrix.Row
                    {
                        Rival = rival,
                        Hostility = result.Hostility,
                        Current = pair.Stance,
                        TurnsSinceChange = diplomacy.TurnsSinceChange(me, rival, turn),
                    });
                }
                diplomacy.DiscardCuts(me);   // cuts by players I have no contact with must not pile up for later

                foreach (var decision in StanceMatrix.Decide(me, rows, constants))
                {
                    if (!diplomacy.SetStance(me, decision.Rival, decision.Stance, turn)) continue;
                    var pair = diplomacy.Get(me, decision.Rival);
                    pair.PWar = decision.PWar;
                    diplomacy.Set(me, decision.Rival, pair);
                    AITuningLogger.LogStance(turn, me, decision.Rival, decision.Stance.ToString(), pair.Hostility,
                        pair.CutsTerm, pair.NearTerm, pair.StrengthTerm, decision.PWar);
                }

                // Wars the rival declared on me (I have contact, I did not declare): logged when they start and end.
                var warRivals = WarRivals();
                var forced = new HashSet<int>(warRivals.Where(r => diplomacy.StanceToward(me, r) != Stance.War));
                foreach (var rival in forced.Where(r => !_warForcedLogged.Contains(r)).ToList())
                {
                    _warForcedLogged.Add(rival);
                    AITuningLogger.LogWarForced(turn, me, rival, true);
                }
                foreach (var rival in _warForcedLogged.Where(r => !forced.Contains(r)).ToList())
                {
                    _warForcedLogged.Remove(rival);
                    AITuningLogger.LogWarForced(turn, me, rival, false);
                }

                var atWar = warRivals.Count > 0;
                if (Strategy == AIStrategy.AIStrategyConsolidate && atWar)
                    SwitchStrategy(AIStrategy.AIStrategyAmass, turn);
                else if (Strategy == AIStrategy.AIStrategyAmass && !atWar)
                    SwitchStrategy(AIStrategy.AIStrategyConsolidate, turn);
            }

            private void SwitchStrategy(AIStrategy to, int turn)
            {
                AITuningLogger.LogStrategyChange(turn, Player.playerID, Strategy.ToString(), to.ToString());
                Strategy = to;
            }
```
(`_warForcedLogged` contains the first test's `WarForcedRivals`.) In `ProcessResults`, after the `switch (Strategy)` block, add:

```csharp
                // After the routine and the blockade view: a new strategy takes effect on the next turn's routine.
                UpdateDiplomacy(Gameboard.Instance != null ? Gameboard.Instance.TurnNumber : 0);
```

- [ ] **Step 5: The game wiring** — in `GameAI.cs`:
  - `InitGameAI`: after `GameAIMap.GameAIMapInit(...)` add `GameAIMap.Diplomacy.Enabled = gameAIConstants.diplomacyEnabled;`
  - `ApplyBlockades`: inside the `foreach (var cut in cuts)` loop, after the range `continue` line and before `var owner`, add `GameAIMap.Diplomacy.RecordCut(cut.PlayerId, cut.BlockerId);`
  - `LogEconomySummary`: inside the per-player loop after the `LogChokepointGarrison` call add:

```csharp
                    if (GameAIMap.Diplomacy.Enabled)
                        foreach (var rival in GameAIMap.Knowledge.ContactPlayers(GameAIMap, player))
                        {
                            var pair = GameAIMap.Diplomacy.Get(player, rival);
                            AITuningLogger.LogHostility(turnNumber, player, rival, pair.Hostility, pair.MyStrength,
                                pair.RivalStrength, pair.NearShips);
                        }
```

- [ ] **Step 6: Verify (GREEN)** — Rider on the four files; the user runs `Run All AI Self-Checks`. Expected: all 11 suites pass.

- [ ] **Step 7: Commit**

```bash
git add Assets/Flatspace/GameAI/PlayerAI.cs Assets/Flatspace/GameAI/GameAI.cs Assets/Flatspace/Diagnostics/AITuningLogger.cs Assets/Editor/DiplomacySelfCheck.cs
git commit -m "feat: stance update, Consolidate/Amass switch, game wiring and Stance/WarForced/Hostility log lines"
```

---

### Task 7: Saves

**Files:**
- Modify: `Assets/Flatspace/SaveSystem/SaveLoadSystem.cs`, `Assets/Flatspace/Objects/Board/GameBoard.cs`, `Assets/Editor/DiplomacySelfCheck.cs`

**Interfaces:**
- Consumes: `DiplomacyState.Snapshot/Restore`.
- Produces: `SaveLoadSystem.GameSave.StanceSave { rival, stance, hostility, lastChangeTurn; From(DiplomacyState.Entry); ToEntry() }`, `PlayerSave.stances`.

- [ ] **Step 1: Write the failing test** — add `ok &= RunStanceSaveCheck();` to `RunChecks` and:

```csharp
    public static bool RunStanceSaveCheck()
    {
        var ok = true;
        var entry = new DiplomacyState.Entry { Rival = 2, Stance = Stance.War, Hostility = 61.5f, LastChangeTurn = 140 };
        var back = SaveLoadSystem.GameSave.StanceSave.From(entry).ToEntry();
        ok &= Check(back.Rival == 2 && back.Stance == Stance.War && Near(back.Hostility, 61.5f) && back.LastChangeTurn == 140,
            "From and ToEntry are inverse");

        var save = new SaveLoadSystem.GameSave.PlayerSave
        {
            playerId = 1,
            stances = new List<SaveLoadSystem.GameSave.StanceSave> { SaveLoadSystem.GameSave.StanceSave.From(entry) },
        };
        var loaded = JsonUtility.FromJson<SaveLoadSystem.GameSave.PlayerSave>(JsonUtility.ToJson(save));
        ok &= Check(loaded.stances != null && loaded.stances.Count == 1 && loaded.stances[0].rival == 2
                    && loaded.stances[0].stance == (int)Stance.War && Near(loaded.stances[0].hostility, 61.5f)
                    && loaded.stances[0].lastChangeTurn == 140,
            "a PlayerSave's stances survive a JsonUtility round trip");

        var state = new DiplomacyState();
        state.SetStance(1, 2, Stance.War, 140);
        var pair = state.Get(1, 2);
        pair.Hostility = 61.5f;
        state.Set(1, 2, pair);
        var restored = new DiplomacyState();
        restored.Restore(1, JsonUtility.FromJson<SaveLoadSystem.GameSave.PlayerSave>(
            JsonUtility.ToJson(new SaveLoadSystem.GameSave.PlayerSave
            {
                playerId = 1,
                stances = state.Snapshot(1).ConvertAll(SaveLoadSystem.GameSave.StanceSave.From),
            })).stances.ConvertAll(s => s.ToEntry()));
        ok &= Check(restored.StanceToward(1, 2) == Stance.War && Near(restored.Hostility(1, 2), 61.5f)
                    && restored.TurnsSinceChange(1, 2, 150) == 10,
            "the whole path state -> save -> JSON -> restore keeps stance, hostility and the hold");

        var older = JsonUtility.FromJson<SaveLoadSystem.GameSave.PlayerSave>("{\"playerId\":1}");
        var none = new DiplomacyState();
        none.SetStance(1, 2, Stance.War, 5);
        none.Restore(1, older.stances?.ConvertAll(s => s.ToEntry()));
        ok &= Check(none.StanceToward(1, 2) == Stance.Peace && none.Snapshot(1).Count == 0,
            "an older save (no stances) restores all Peace");
        return ok;
    }
```

- [ ] **Step 2: Run to verify it fails (RED)** — Rider: unresolved `StanceSave`, `stances`.

- [ ] **Step 3: The save type** — in `SaveLoadSystem.cs`, directly after the `RememberedBlockadeSave` struct (before `PlayerSave`), add (the neighbouring struct is `[Serializable] public struct` with a `From` and a `ToEntry`):

```csharp
        [Serializable]
        public struct StanceSave
        {
            public int rival;
            public int stance;          // a Stance, as an int
            public float hostility;
            public int lastChangeTurn;

            public static StanceSave From(FlatSpace.AI.DiplomacyState.Entry e)
                => new StanceSave { rival = e.Rival, stance = (int)e.Stance, hostility = e.Hostility, lastChangeTurn = e.LastChangeTurn };

            public FlatSpace.AI.DiplomacyState.Entry ToEntry()
                => new FlatSpace.AI.DiplomacyState.Entry
                {
                    Rival = rival,
                    Stance = (FlatSpace.AI.Stance)stance,
                    Hostility = hostility,
                    LastChangeTurn = lastChangeTurn,
                };
        }
```
(If the file already has `using FlatSpace.AI;`, the short names work; the full names are always valid.) Add to `PlayerSave`, after `rememberedBlockades`:

```csharp
            // Null in older saves: every stance is Peace at 0 hostility.
            public List<StanceSave> stances;
```
In the save-writing initializer (the one that sets `rememberedBlockades = ...`), add:

```csharp
                        stances = gameAI.GameAIMap.Diplomacy.Snapshot(i).ConvertAll(StanceSave.From),
```

- [ ] **Step 4: The restore** — in `GameBoard.cs`, right after the `RestoreRememberedBlockades(...)` call:

```csharp
                    GameAI.GameAIMap.Diplomacy.Restore(playerSave.playerId,
                        playerSave.stances?.ConvertAll(s => s.ToEntry()));
```

- [ ] **Step 5: Verify (GREEN)** — Rider on the three files; the user runs `Run Diplomacy Self-Check`. Then ask the user to start a Play run, save a game after a few turns and load it (no error in the Console).

- [ ] **Step 6: Commit**

```bash
git add Assets/Flatspace/SaveSystem/SaveLoadSystem.cs Assets/Flatspace/Objects/Board/GameBoard.cs Assets/Editor/DiplomacySelfCheck.cs
git commit -m "feat: save and restore stances and hostility"
```

---

### Task 8: Documentation and final verification

**Files:**
- Modify: `CLAUDE.md`, `.claude/skills/tuning-log/SKILL.md`

- [ ] **Step 1: `CLAUDE.md`** — (a) in the "Tests" list add `FlatSpace → AI → Run Diplomacy Self-Check` in `Assets/Editor/DiplomacySelfCheck.cs` (fleet strength, hostility, the stance matrix, effective war, the Consolidate/Amass switch, forced wars, the assault gate and the wanted fleet, the Amass tables, `IsConsolidateLike`, stance saves) and change "(runs every AI suite: ..." to list Diplomacy too. (b) Replace the Player Knowledge sentence "Amass/None are never switched" with "Amass/None are never switched by `TryEnterConsolidate`; Consolidate and Amass are switched by diplomacy (below)" and change "Consolidate has no behavior of its own yet" wording wherever it appears to say Consolidate and Amass play like Expand through their own tables. (c) Add this section before "### Connectivity (chokepoints)":

```markdown
### Diplomacy

`DiplomacyState` (`Assets/Flatspace/GameAI/DiplomacyState.cs`, on `GameAIMap.Diplomacy`, pure) holds each player's `Stance` (Peace or War) toward each rival, a hostility score, the turn of the last change and the blockade cuts waiting to be charged. **Effective war is derived, never stored:** player P is at war with Q when P's own stance toward Q is War, or Q's stance toward P is War and P has contact with Q (`PlayerKnowledge.ContactPlayers`/`HasContactWith`, per rival; ownerless ships are not a player; `HasContact` itself is unchanged). A declaration against a player the declarer never met changes nothing until the empires touch. `PlayerAI.WarRivals()` is that set (it counts my own War stance even without current contact).
`PlayerAI.UpdateDiplomacy(turn)` runs at the END of `ProcessResults`, after `RefreshBlockadeView` and the `switch (Strategy)` block, so a new strategy takes effect on the next turn's routine. Per rival with contact it updates the hostility (`HostilityCalculator`): `H = clamp(H x (1 - hostilityDecay) + cuts x hostilityPerCut + rival warships docked on or beside my planets x hostilityPerNearShip + hostilityStrengthWeight x log2(myStrength / rivalStrength) clamped to +-2, 0..hostilityMax)`. Cuts come from `BlockadeSystem.BlockadeCut.BlockerId` (recorded in `GameAI.ApplyBlockades`; cuts by players I have no contact with are discarded each turn). `FleetStrength` = sum of docked warships' Offense x (Health + Defense) (mine anywhere, the rival's on planets I know; in-flight ships are not counted). `StanceMatrix` then decides each rival not held (`stanceHoldTurns` since its last change): a `ScoreMatrix` with the new `IndependentRows` flag (a chosen choice is not removed from other rows), choices Peace and War, War probability `1 / (1 + e^(-(H - stanceMidpoint) / stanceSteepness))`, the held stance x `stanceStickiness`. Finally a Consolidate player at war with anyone becomes Amass and an Amass player at war with nobody returns to Consolidate; Expand and None are never switched here.
`PlayerAI.IsConsolidateLike` (Consolidate or Amass) is used wherever a rule was "Consolidate only" (warship fleet-shortfall boost, ColonyShip boost, chokepoint colonization tilt, `ShipTransportPlanner` garrisons and ranking, assault and blockade-breaking), so **Amass differs from Consolidate only by its own research and industry tables** (Warship 4.0, WarshipUpdate 4.0, ColonyShip 0.5, ...) **and the war gate**. The gate: `AssaultPlanner` takes an optional war set (`PlayerAI.AssaultWarFilter()`, null while diplomacy is off): only a war rival's planets are enemy-occupied and only its docked warships size the force; blockade-breaking targets and blockades themselves are unchanged. `PlayerAI.WantedWarships` therefore adds the assault force only while a known planet of a player I am at war with exists (a Consolidate player at peace with everyone wants fewer warships than before).
**Legacy mode:** `GameAIConstants.diplomacyEnabled` (default true) is copied by `GameAI.InitGameAI` onto `GameAIMap.Diplomacy.Enabled`. False, and any `GameAIMap` a self-check builds directly, means every rival with contact is an enemy, no stance matrix and no Amass switch (today's behaviour; the clean A/B against older logs). `DiplomacySelfCheck` switches it on explicitly. **Saves:** `PlayerSave.stances` (rival, stance as an int, hostility, last-change turn); an older save loads all Peace at 0; the effective state, the cut counters and the log-only numbers are not saved. Tunables on `GameAIConstants` (all in-code defaults): `hostilityPerCut` 5, `hostilityPerNearShip` 0.5, `hostilityStrengthWeight` 1, `hostilityDecay` 0.05, `hostilityMax` 100, `stanceMidpoint` 30, `stanceSteepness` 8, `stanceStickiness` 3, `stanceHoldTurns` 10. Self-check: `Assets/Editor/DiplomacySelfCheck.cs`.
```
(d) In "AI Tuning Log" add the three lines to the line list: `Stance|<rival>|<Peace or War>|<hostility>|<cutsTerm>|<nearTerm>|<strengthTerm>|<pWar>` (a stance change, reported by `DiplomacyState.SetStance`), `WarForced|<rival>|<Start or End>` (a war a rival declared on me that I have contact with, log-only set in `PlayerAI`, so a load logs each current one once more), `Hostility|<rival>|<H>|<myStrength>|<rivalStrength>|<nearShips>` (every 25 turns per player and rival with contact, beside `Economy`), and note `StrategyChange` now also logs Consolidate to Amass and back.

- [ ] **Step 2: The `tuning-log` skill** — read `.claude/skills/tuning-log/SKILL.md`, then after its "Colonist redirect" section add a "Diplomacy" section in the same style covering: the first `Stance|...|War` turn per match and per player; the share of players in Amass over time (from `StrategyChange`); war length (War to the next Peace per pair); forced wars (`WarForced Start`) against wars chosen (`Stance ... War`) and the turns between a declaration and the forced start; stance flips within 20 turns of the previous change for the same pair (the stickiness check; many means raise `stanceStickiness` or `stanceHoldTurns`); where hostility settles (the `Hostility` lines at 25-turn steps, against the 30 midpoint); warship starts per planet under Amass against Consolidate turns (the table's effect) and that fleet-cap violations stay 0 under Amass; assaults (`AssaultTarget` lines) only against players at war; and a note that a baseline from before diplomacy is comparable only with `diplomacyEnabled` false (everyone an enemy).

- [ ] **Step 3: Final verification** — ask the user to focus the Unity Editor and run `FlatSpace -> AI -> Run All AI Self-Checks`. Expected: `[AllAISelfChecks] ALL 11 SUITES PASSED`. Then ask for Play-mode runs of `test2.json` and `4p.json` with logging on and run `/tuning-log` (the "Diplomacy" section).

- [ ] **Step 4: Commit**

```bash
git add CLAUDE.md .claude/skills/tuning-log/SKILL.md
git commit -m "docs: diplomacy section, tuning-log lines and skill update"
```

---

## Self-Review

**Spec coverage:** per-pair stance and derived war (Task 1), per-rival contact (Task 1), cut blocker (Task 1), FleetStrength and the four hostility inputs (Task 2), StanceMatrix with stickiness, hold and independence (Task 3), Amass tables, `IsConsolidateLike` and the empty-case fix (Task 4), the assault gate and the wanted fleet (Task 5), strategy switch after `RefreshBlockadeView` and the `switch`, per-rival perception (rule B), legacy switch and the three log lines (Task 6), saves (Task 7), docs and the skill (Task 8). The spec's "Out of scope" list is untouched. Gaps: none found.

**Placeholders:** none; every code step has the code. The edits to existing code give the old and new text.

**Type consistency:** `Stance`, `DiplomacyState.Pair/Entry`, `HostilityCalculator.Inputs/Result`, `StanceMatrix.Row/Decision`, `StanceChoiceElement.Weight`, `PlayerAI.WarRivals()/AssaultWarFilter()/UpdateDiplomacy(int)/WarForcedRivals`, `AssaultPlanner`'s seventh parameter `warRivals`, and the logger signatures match across the tasks that define and use them.

**Review Focus:** items 1 and 2 are tested in Task 6 (`RunStrategySwitchCheck`, `RunForcedWarCheck`), item 3 in Tasks 1 and 5 (`RunContactPlayersCheck`, `RunAssaultGateCheck`), item 4 in Task 6 (`RunCutsAndLegacyCheck`), item 5 in Tasks 5 and 6 (`RunWarRivalsCheck`, `RunCutsAndLegacyCheck`).
