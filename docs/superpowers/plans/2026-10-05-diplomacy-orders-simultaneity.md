# Diplomacy as Orders and Simultaneous Player Decisions Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make stance changes travel through the order pipeline (Declare War and Make Peace orders executed in `ProcessNewOrders`) and prove, with a self-check, that every player's decisions in a turn are independent of the player order.

**Architecture:** `PlayerAI.UpdateDiplomacy` keeps updating its own hostility but emits `OrderTypeDeclareWar` / `OrderTypeMakePeace` instead of writing a stance; `GameAI.ExecuteOrder` applies them through a public static `GameAI.ApplyStanceOrder` (pure, so self-checks call the same code). The Consolidate/Amass switch and the `WarForced` bookkeeping move to a new `PlayerAI.ApplyWarState`, called at the start of `ProcessResults`, where it reads stances committed by earlier orders. A new `SimultaneitySelfCheck` runs the same three-player fixture with the players in forward and reversed order and compares per-player orders, stances and strategy.

**Tech Stack:** Unity 6000.4.1f1 C# (`FlatSpace.AI` namespace), Editor self-checks (`Assets/Editor/`), Rider MCP for compile signals.

**Spec:** `docs/superpowers/specs/2026-10-05-diplomacy-orders-simultaneity-design.md` (read it and `CLAUDE.md` first; `FUTURE_FEATURES.md` milestone `ship_combat`; the combat decisions this prepares are in `docs/superpowers/specs/2026-10-05-ship-combat-decisions-so-far.md`).

## Global Constraints

- New runtime code stays in the namespaces its file already uses (`FlatSpace.AI`, written `namespace FlatSpace { namespace AI { ... } }`); the new Editor script is global namespace with `public static bool RunChecks()`.
- Pure code (`DiplomacyState`, `GameAI.ApplyStanceOrder`) must never touch `Gameboard.Instance`, so self-checks can drive it directly. `GameAI.ExecuteOrder` (which needs `Gameboard.Instance`) only logs.
- A method a self-check calls is `public`, never `internal` (`Assets/Editor` is a separate assembly).
- `OrderType` values are appended LAST (`OrderTypeDeclareWar`, then `OrderTypeMakePeace`, after `OrderTypeColonyFoodRider`); the enum serializes as an int.
- The new orders are `OrderTimingTypeImmediate`, `TimingDelay` 0, `Data` = the rival's player id as a boxed `int`, `Origin` and `Target` = `string.Empty` (never `null`: `GameAIMap.GetPlanet` does a dictionary lookup that throws on a null key). They are never added to `CurrentAIOrders` (only `Delayed` orders are), so there is no save change.
- Legacy mode (`Diplomacy.Enabled` false, and every `GameAIMap` a self-check builds directly) emits no stance orders and never switches strategy, exactly as today.
- Self-check maps need a distinct position per planet (A* tie-break note) and no assertion exactly on a float boundary.
- No new tunable. No new tuning-log line: `Stance`, `WarForced`, `StrategyChange` keep their formats.
- Commit messages end with `Co-Authored-By: Claude Sonnet 5.5 <noreply@anthropic.com>`. Work on the feature branch `diplomacy-orders`; never push or merge unprompted.
- Unity cannot be run from Claude Code. The compile signal is Rider's `get_file_problems` with `rootFolder: "C:/Projects/FlatSpace"` (it cannot analyse brand-new files and an empty list is not proof). The real check is the user focusing the Unity Editor and running `FlatSpace -> AI -> Run Diplomacy Self-Check` (and `Run All AI Self-Checks` where a task says so). A "RED" step means Rider shows the not-yet-written members as unresolved, or the Editor run fails the new assertion.
- Every new script's `.meta` is created and committed with it (Task 3 gives the PowerShell helper).

## Review Focus

Inputs and conditions the spec implies that no obvious test would reach, most likely first (each has a test in the task named):

1. A stance order for a rival whose stance already matches (the matrix picks the held stance) must not emit an order, or every turn would emit a no-op order and log nothing but churn (Task 2).
2. A declaration made this turn must not change the strategy of the declarer or of any other player this turn; it is acted on at the start of the next `ProcessResults` (Tasks 2 and 3).
3. Orders with empty `Origin`/`Target` must pass through `ProcessNewOrders` and `ExecuteOrder` without a lookup on a null or missing planet (Task 1).
4. Legacy mode must emit no orders and `ApplyWarState` must do nothing (Task 2).
5. The reversed-player-order run must use a re-seeded `GameAI.Rand` per player, or the comparison fails for the wrong reason (Task 3).

---

## File Structure

| File | Change |
|---|---|
| `Assets/Flatspace/GameAI/GameAI.cs` | modify: two `OrderType` values, `ApplyStanceOrder`, two `ExecuteOrder` cases, `Rand` no longer `readonly` |
| `Assets/Flatspace/GameAI/PlayerAI.cs` | modify: `UpdateDiplomacy(turn, orders)` emits orders; new `ApplyWarState(turn)`; `ProcessResults` calls it first |
| `Assets/Editor/DiplomacySelfCheck.cs` | modify: `Turn` helper, existing cases rewritten onto it, new order cases |
| `Assets/Editor/SimultaneitySelfCheck.cs` (+ `.meta`) | create: forward versus reversed player order |
| `Assets/Editor/AllAISelfChecks.cs` | modify: register the suite (12 suites) |
| `CLAUDE.md`, `.claude/skills/tuning-log/SKILL.md` | modify: docs |

---

### Task 1: Stance orders and their execution

**Files:**
- Modify: `Assets/Flatspace/GameAI/GameAI.cs` (enum at line 41, `ExecuteOrder` at line 271, near `ApplyColonyFoodRider` at line 365)
- Modify: `Assets/Editor/DiplomacySelfCheck.cs` (new `RunStanceOrderCheck`, registered in `RunChecks`)

**Interfaces:**
- Consumes: `DiplomacyState.SetStance(int me, int rival, Stance stance, int turn)` (returns true only on a change), `AITuningLogger.LogStance(int turn, int me, int rival, string stance, float hostility, float cuts, float near, float strength, float pWar)`.
- Produces: `GameAI.GameAIOrder.OrderType.OrderTypeDeclareWar`, `OrderTypeMakePeace`; `public static bool GameAI.ApplyStanceOrder(DiplomacyState diplomacy, GameAIOrder order, int turn)`.

- [ ] **Step 0: Create the branch**

```bash
cd /c/Projects/FlatSpace && git switch -c diplomacy-orders && git log -1 --format=%h
```
Expected: on `diplomacy-orders` at `1b416a5` or later.

- [ ] **Step 1: Write the failing check.** In `DiplomacySelfCheck.cs` add `ok &= RunStanceOrderCheck();` after `ok &= RunNearShipsIgnoreRivalGarrisonCheck();` in `RunChecks`, and add this method after `RunStanceSaveCheck`:

```csharp
    // Stance changes are orders: ApplyStanceOrder is the one place a stance order is decoded and applied (GameAI.ExecuteOrder
    // calls it too), and an order with no planets must not trip a planet lookup.
    public static bool RunStanceOrderCheck()
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

        var d = new DiplomacyState();
        ok &= Check(GameAI.ApplyStanceOrder(d, Order(GameAI.GameAIOrder.OrderType.OrderTypeDeclareWar, 0, 1), 7),
            "a Declare War order against a Peace pair reports a change");
        ok &= Check(d.StanceToward(0, 1) == Stance.War && d.Get(0, 1).LastChangeTurn == 7,
            "the order sets War and stamps the turn (the hold starts there)");
        ok &= Check(d.StanceToward(1, 0) == Stance.Peace, "the rival's own stance is not touched by being declared on");
        ok &= Check(!GameAI.ApplyStanceOrder(d, Order(GameAI.GameAIOrder.OrderType.OrderTypeDeclareWar, 0, 1), 8),
            "a second Declare War reports no change and keeps the first turn");
        ok &= Check(d.Get(0, 1).LastChangeTurn == 7, "so the hold is not restarted");
        ok &= Check(GameAI.ApplyStanceOrder(d, Order(GameAI.GameAIOrder.OrderType.OrderTypeMakePeace, 0, 1), 20),
            "a Make Peace order against a War pair reports a change");
        ok &= Check(d.StanceToward(0, 1) == Stance.Peace && d.Get(0, 1).LastChangeTurn == 20, "Peace, stamped turn 20");

        using (var f = Fixture.Line())
            ok &= Check(f.Map.GetPlanet(string.Empty) == null,
                "an order with an empty Target looks up no planet (GetPlanet(\"\") is null, not an exception)");
        return ok;
    }
```

- [ ] **Step 2: Verify RED.** Rider `get_file_problems` on `DiplomacySelfCheck.cs`: `OrderTypeDeclareWar`, `OrderTypeMakePeace` and `GameAI.ApplyStanceOrder` are unresolved.

- [ ] **Step 3: Implement.** In `GameAI.cs`:

(a) Make the shared random replaceable (Task 3 re-seeds it per player). Line 104 becomes:

```csharp
            public static Random Rand = new Random();   // not readonly: SimultaneitySelfCheck re-seeds it per player
```

(b) Append the two order types after `OrderTypeColonyFoodRider` (add a comma after it):

```csharp
                    OrderTypeColonyFoodRider,
                    // Appended last: OrderType serializes as an int. Immediate; Data is the rival's player id (an int),
                    // PlayerId the player that decided; Origin and Target are empty. Executed by ApplyStanceOrder.
                    OrderTypeDeclareWar,
                    OrderTypeMakePeace
```

(c) Add next to `ApplyColonyFoodRider` (public static, so a self-check calls the real code):

```csharp
            // Immediate: player order.PlayerId takes its stance toward the rival in order.Data to War or Peace. True only
            // when the stance changed (the caller logs on that). Pure: no Gameboard.Instance.
            public static bool ApplyStanceOrder(DiplomacyState diplomacy, GameAIOrder order, int turn)
            {
                var stance = order.Type == GameAIOrder.OrderType.OrderTypeDeclareWar ? Stance.War : Stance.Peace;
                return diplomacy.SetStance(order.PlayerId, Convert.ToInt32(order.Data), stance, turn);
            }
```

(d) In `ExecuteOrder`, add before `default:`:

```csharp
                    case GameAIOrder.OrderType.OrderTypeDeclareWar:
                    case GameAIOrder.OrderType.OrderTypeMakePeace:
                    {
                        var stanceTurn = Gameboard.Instance.TurnNumber;
                        if (ApplyStanceOrder(GameAIMap.Diplomacy, executableOrder, stanceTurn))
                        {
                            var stanceRival = Convert.ToInt32(executableOrder.Data);
                            var stancePair = GameAIMap.Diplomacy.Get(executableOrder.PlayerId, stanceRival);
                            AITuningLogger.LogStance(stanceTurn, executableOrder.PlayerId, stanceRival,
                                stancePair.Stance.ToString(), stancePair.Hostility, stancePair.CutsTerm,
                                stancePair.NearTerm, stancePair.StrengthTerm, stancePair.PWar);
                        }
                        break;
                    }
```
`ExecuteOrder` starts with `GameAIMap.GetPlanet(executableOrder.Target)`; with `Target` empty that returns null and the new cases never use it.

- [ ] **Step 4: Verify GREEN.** Rider `get_file_problems` on `GameAI.cs`. Then ask the user to focus the Unity Editor and run `FlatSpace -> AI -> Run Diplomacy Self-Check`. Expected: `[DiplomacySelfCheck] ALL PASSED`.

- [ ] **Step 5: Commit**

```bash
cd /c/Projects/FlatSpace && git add Assets/Flatspace/GameAI/GameAI.cs Assets/Editor/DiplomacySelfCheck.cs && git commit -q -m "$(cat <<'EOF'
feat: stance orders (Declare War, Make Peace) and ApplyStanceOrder

Co-Authored-By: Claude Sonnet 5.5 <noreply@anthropic.com>
EOF
)" && git log -1 --oneline
```

---

### Task 2: UpdateDiplomacy emits orders; the strategy switch reads committed stances

**Files:**
- Modify: `Assets/Flatspace/GameAI/PlayerAI.cs` (`ProcessResults` at line 161, `UpdateDiplomacy` at line 232)
- Modify: `Assets/Editor/DiplomacySelfCheck.cs` (new helpers and cases; rewrite existing `UpdateDiplomacy` calls)
- Modify: `CLAUDE.md`, `.claude/skills/tuning-log/SKILL.md`

**Interfaces:**
- Consumes: `GameAI.ApplyStanceOrder` (Task 1), `PlayerAI.MakeOrder` (private, sets `PlayerId = Player.playerID`), `StanceMatrix.Decide`.
- Produces: `public void PlayerAI.UpdateDiplomacy(int turn, List<GameAI.GameAIOrder> orders)` (replaces `UpdateDiplomacy(int turn)`), `public void PlayerAI.ApplyWarState(int turn)`.

- [ ] **Step 1: Write the failing checks.** In `DiplomacySelfCheck.cs`:

(a) Add the helpers beside `WithRival()`:

```csharp
    private static void ApplyStanceOrders(Fixture f, List<GameAI.GameAIOrder> orders, int turn)
    {
        foreach (var order in orders.Where(o => o.Type == GameAI.GameAIOrder.OrderType.OrderTypeDeclareWar
                                                || o.Type == GameAI.GameAIOrder.OrderType.OrderTypeMakePeace))
            GameAI.ApplyStanceOrder(f.Map.Diplomacy, order, turn);
    }

    // One diplomacy turn the way GameAI runs it: the start-of-turn war state (switch, forced-war lines), the decision, then
    // the emitted stance orders executed (ProcessNewOrders). Returns the orders so a case can inspect them.
    private static List<GameAI.GameAIOrder> Turn(Fixture f, int turn)
    {
        f.AI.ApplyWarState(turn);
        var orders = new List<GameAI.GameAIOrder>();
        f.AI.UpdateDiplomacy(turn, orders);
        ApplyStanceOrders(f, orders, turn);
        return orders;
    }
```

(b) Register `ok &= RunStanceOrdersEmittedCheck();` in `RunChecks` and add this case after `RunUpdateDiplomacyCheck`:

```csharp
    // The decision is an order: UpdateDiplomacy writes no stance and changes no strategy. Executing the order commits the stance,
    // and the strategy follows only at the start of the next turn (ApplyWarState).
    public static bool RunStanceOrdersEmittedCheck()
    {
        var ok = true;
        using (var f = WithRival())
        {
            var pair = f.Map.Diplomacy.Get(0, 1);
            pair.Hostility = 100f;
            f.Map.Diplomacy.Set(0, 1, pair);

            var orders = new List<GameAI.GameAIOrder>();
            f.AI.UpdateDiplomacy(20, orders);
            var declare = orders.Where(o => o.Type == GameAI.GameAIOrder.OrderType.OrderTypeDeclareWar).ToList();
            ok &= Check(declare.Count == 1 && orders.Count == 1, "hostility 100 emits exactly one order: Declare War");
            ok &= Check(declare[0].PlayerId == 0 && System.Convert.ToInt32(declare[0].Data) == 1
                        && declare[0].TimingType == GameAI.GameAIOrder.OrderTimingType.OrderTimingTypeImmediate
                        && declare[0].Origin == string.Empty && declare[0].Target == string.Empty,
                "the order is immediate, from player 0, Data is rival 1, no planets");
            ok &= Check(f.Map.Diplomacy.StanceToward(0, 1) == Stance.Peace, "UpdateDiplomacy itself writes no stance");
            ok &= Check(f.AI.Strategy == PlayerAI.AIStrategy.AIStrategyConsolidate, "and does not switch the strategy");
            ok &= Check(Near(f.Map.Diplomacy.Get(0, 1).PWar, 1f), "the war probability is stored for the Stance log line");

            ApplyStanceOrders(f, orders, 20);
            ok &= Check(f.Map.Diplomacy.StanceToward(0, 1) == Stance.War, "executing the order commits War");
            ok &= Check(f.AI.Strategy == PlayerAI.AIStrategy.AIStrategyConsolidate, "the strategy still waits for the next turn's start");
            f.AI.ApplyWarState(21);
            ok &= Check(f.AI.Strategy == PlayerAI.AIStrategy.AIStrategyAmass, "the next turn's ApplyWarState switches Consolidate to Amass");

            // Review focus 1: the matrix picks the held stance again: no order, so no churn.
            var again = new List<GameAI.GameAIOrder>();
            pair = f.Map.Diplomacy.Get(0, 1);
            pair.Hostility = 100f;
            f.Map.Diplomacy.Set(0, 1, pair);
            f.AI.UpdateDiplomacy(40, again);
            ok &= Check(again.Count == 0, "a decision equal to the held stance emits no order");

            // Peace: hostility falls below the midpoint, a Make Peace order, then Amass returns on the next start.
            pair = f.Map.Diplomacy.Get(0, 1);
            pair.Hostility = 0f;
            f.Map.Diplomacy.Set(0, 1, pair);
            var peace = new List<GameAI.GameAIOrder>();
            f.AI.UpdateDiplomacy(60, peace);
            ok &= Check(peace.Count == 1 && peace[0].Type == GameAI.GameAIOrder.OrderType.OrderTypeMakePeace,
                "hostility 0 against a War stance emits Make Peace");
            ApplyStanceOrders(f, peace, 60);
            f.AI.ApplyWarState(61);
            ok &= Check(f.AI.Strategy == PlayerAI.AIStrategy.AIStrategyConsolidate, "and Amass returns to Consolidate at the next start");
        }

        // A rival's declaration is first seen on the next turn, never the turn it is committed.
        using (var f = WithRival())
        {
            f.Map.Diplomacy.SetStance(1, 0, Stance.War, 5);
            ok &= Check(f.AI.Strategy == PlayerAI.AIStrategy.AIStrategyConsolidate && f.AI.WarForcedRivals.Count == 0,
                "committing the rival's declaration changes nothing by itself");
            f.AI.ApplyWarState(6);
            ok &= Check(f.AI.Strategy == PlayerAI.AIStrategy.AIStrategyAmass && f.AI.WarForcedRivals.Contains(1),
                "the next ApplyWarState sees the forced war: Amass and the WarForced bookkeeping");
        }

        // Legacy: nothing emitted, nothing switched.
        using (var f = WithRival())
        {
            f.Map.Diplomacy.Enabled = false;
            f.Map.Diplomacy.SetStance(0, 1, Stance.War, 5);
            var orders = new List<GameAI.GameAIOrder>();
            f.AI.UpdateDiplomacy(6, orders);
            f.AI.ApplyWarState(7);
            ok &= Check(orders.Count == 0 && f.AI.Strategy == PlayerAI.AIStrategy.AIStrategyConsolidate,
                "legacy: no stance order and ApplyWarState never switches to Amass");
        }
        return ok;
    }
```

(c) Rewrite the existing calls. Mechanical rule, applied to every `f.AI.UpdateDiplomacy(N);` in `RunUpdateDiplomacyCheck`, `RunStrategySwitchCheck`, `RunForcedWarCheck`, `RunCutsAndLegacyCheck` and `RunLostContactWarCanEndCheck`: replace with `Turn(f, N);`. `Turn` runs `ApplyWarState` first, so a case that sets a stance directly (`SetStance`) and then calls `Turn` still sees the switch in the same call, as before. Two cases decided the stance inside the call and asserted the strategy right after; they need one more start-of-turn:
  - `RunUpdateDiplomacyCheck`, after `Turn(f, 20);` and the `"hostility 95+ is War"` assertion, insert `f.AI.ApplyWarState(21);` before the `"war switches Consolidate to Amass"` assertion.
  - `RunLostContactWarCanEndCheck`, after `Turn(f, 51);` and the `"below the midpoint the war without contact ends"` assertion, insert `f.AI.ApplyWarState(52);` before the `"and Amass returns to Consolidate"` assertion.
  Update the two comments that say "the strategy follows in the same call" to "the strategy follows at the next turn's start". `RunLegacy` lines in `RunCutsAndLegacyCheck` call `Turn(f, 6)` with diplomacy disabled; unchanged meaning.

- [ ] **Step 2: Verify RED.** Rider `get_file_problems` on `DiplomacySelfCheck.cs`: `ApplyWarState` and the two-argument `UpdateDiplomacy` are unresolved.

- [ ] **Step 3: Implement** in `PlayerAI.cs`.

(a) `ProcessResults`: call the war state first and pass the orders list to `UpdateDiplomacy`:

```csharp
                TryEnterConsolidate(Gameboard.Instance != null ? Gameboard.Instance.TurnNumber : 0);
                // Stances committed by last turn's orders: the Consolidate/Amass switch and the forced-war lines read them here,
                // before this turn's routine, so a new strategy takes effect on this turn's routine as it always has.
                ApplyWarState(Gameboard.Instance != null ? Gameboard.Instance.TurnNumber : 0);
                RefreshBlockadeView();
```
and at the end:

```csharp
                // After the routine and the blockade view. The stance decision is an order, executed with the rest in
                // ProcessNewOrders after every player has decided.
                UpdateDiplomacy(Gameboard.Instance != null ? Gameboard.Instance.TurnNumber : 0, orders);
```
(`ProcessResults` takes `orders` by value as `List<...> orders`; the routine's `ref orders` reassigns nothing, so the list is the caller's.)

(b) `UpdateDiplomacy` signature and body. Change the signature to `public void UpdateDiplomacy(int turn, List<GameAI.GameAIOrder> orders)`. Replace the decision loop (the `foreach (var decision in StanceMatrix.Decide(...))` block) with:

```csharp
                foreach (var decision in StanceMatrix.Decide(me, rows, constants))
                {
                    if (diplomacy.StanceToward(me, decision.Rival) == decision.Stance) continue;   // the held stance again: nothing to order
                    var pair = diplomacy.Get(me, decision.Rival);
                    pair.PWar = decision.PWar;   // read by GameAI.ExecuteOrder when the order logs the Stance line
                    diplomacy.Set(me, decision.Rival, pair);
                    orders.Add(MakeOrder(
                        decision.Stance == Stance.War
                            ? GameAI.GameAIOrder.OrderType.OrderTypeDeclareWar
                            : GameAI.GameAIOrder.OrderType.OrderTypeMakePeace,
                        GameAI.GameAIOrder.OrderTimingType.OrderTimingTypeImmediate,
                        0, 0, decision.Rival, string.Empty, string.Empty));
                }
```
Delete everything after that loop in `UpdateDiplomacy` (the `// Wars the rival declared on me ...` block through the final `SwitchStrategy` if/else): it moves to `ApplyWarState`. Update the method's summary: it now "lets the stance matrix decide each rival that is not held and emits the stance orders" and no longer switches strategy.

(c) Add the new method after `UpdateDiplomacy` (the moved code, unchanged apart from reading committed stances):

```csharp
            /// <summary>
            /// At the start of ProcessResults, from the stances the previous turn's orders committed: logs the wars a rival
            /// declared on me (I have contact, I did not declare) when they start and end, then switches Consolidate to Amass
            /// while I am at war with anyone and Amass back to Consolidate once I am at war with nobody. Expand and None are
            /// never switched here. Does nothing while diplomacy is off. Public (and free of Gameboard.Instance) so the
            /// self-check can drive it directly.
            /// </summary>
            public void ApplyWarState(int turn)
            {
                var diplomacy = AIMap.Diplomacy;
                if (!diplomacy.Enabled) return;
                var me = Player.playerID;

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
```

- [ ] **Step 4: Search for other callers.** `Grep` for `UpdateDiplomacy(` across `Assets`: the only callers must be `PlayerAI.ProcessResults` and `DiplomacySelfCheck` (all rewritten to `Turn`/the two-argument form). Fix any other.

- [ ] **Step 5: Verify GREEN.** Rider `get_file_problems` on `PlayerAI.cs` and `DiplomacySelfCheck.cs`. Then ask the user to focus the Unity Editor and run `FlatSpace -> AI -> Run All AI Self-Checks`. Expected: `ALL 11 SUITES PASSED` (the new simultaneity suite is registered in Task 3).

- [ ] **Step 6: Docs.** `CLAUDE.md`, "Diplomacy" section: replace the sentence beginning "`PlayerAI.UpdateDiplomacy(turn)` runs at the END of `ProcessResults`..." and its last sentence ("Finally a Consolidate player at war ...") so they say: `PlayerAI.UpdateDiplomacy(turn, orders)` runs at the END of `ProcessResults` (after `RefreshBlockadeView` and the `switch (Strategy)` block), updates hostility directly (the player's own private view) and **emits an `OrderTypeDeclareWar` / `OrderTypeMakePeace` order for each stance decision that differs from the held stance** (immediate, `Data` = the rival id, empty `Origin`/`Target`); `GameAI.ExecuteOrder` applies it through `GameAI.ApplyStanceOrder` (pure) and logs the `Stance` line, so every player decides against the same stances and a declaration is first seen by others on the next turn. `PlayerAI.ApplyWarState(turn)` runs at the START of `ProcessResults` (after `TryEnterConsolidate`), reads the committed stances, logs `WarForced` and switches Consolidate to Amass while at war with anyone and back once at war with nobody (Expand and None are never switched). Also in "AI Tuning Log": `Stance` is written when the order executes, `WarForced` and `StrategyChange` (Amass) at the start of the next `ProcessResults`; formats unchanged. In "Orders" add one sentence: stance changes are immediate orders (`OrderTypeDeclareWar`, `OrderTypeMakePeace`) that never enter `CurrentAIOrders`. `.claude/skills/tuning-log/SKILL.md`, the Diplomacy section: one note that stances commit through orders, so a rival's declaration is first seen (`WarForced Start`, the Amass switch) one `ProcessResults` after it was decided, and `Stance` lines are logged at order execution.

- [ ] **Step 7: Commit**

```bash
cd /c/Projects/FlatSpace && git add Assets/Flatspace/GameAI/PlayerAI.cs Assets/Editor/DiplomacySelfCheck.cs CLAUDE.md .claude/skills/tuning-log/SKILL.md && git commit -q -m "$(cat <<'EOF'
feat: diplomacy decides through orders; the strategy switch reads committed stances

Co-Authored-By: Claude Sonnet 5.5 <noreply@anthropic.com>
EOF
)" && git log -1 --oneline
```

---

### Task 3: The simultaneity self-check

**Files:**
- Create: `Assets/Editor/SimultaneitySelfCheck.cs` (+ `.meta`)
- Modify: `Assets/Editor/AllAISelfChecks.cs`

**Interfaces:**
- Consumes: `PlayerAI.ProcessResults`, `PlayerAI.ApplyWarState`, `GameAI.ApplyStanceOrder`, `GameAI.Rand` (now assignable), `WarshipSelfCheck.MakeTemplate/MakeConstants/MakeResearch/DockWarships/DestroyAll`, `ChokepointSelfCheck.Spawn`.
- Produces: `public static bool SimultaneitySelfCheck.RunChecks()`.

- [ ] **Step 1: Write the check** (this is the test; it is expected to pass if Task 2 made decisions order-independent, and to fail naming the first differing player and field if some other write is order-dependent). Create `Assets/Editor/SimultaneitySelfCheck.cs`:

```csharp
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using FlatSpace.AI;

// Every player decides against the same world: running the players' ProcessResults in forward and in reversed order over
// the same state must give each player the same orders, the same stances and the same strategy. The orders are compared by
// content, not list position (the list is concatenated in the order the players ran).
public static class SimultaneitySelfCheck
{
    [MenuItem("FlatSpace/AI/Run Simultaneity Self-Check")]
    public static void Run() => RunChecks();

    public static bool RunChecks()
    {
        var ok = RunPlayerOrderIndependenceCheck();
        Debug.Log(ok
            ? "[SimultaneitySelfCheck] ALL PASSED"
            : "[SimultaneitySelfCheck] FAILURES (see errors above)");
        return ok;
    }

    private static bool Check(bool condition, string label)
    {
        if (!condition) Debug.LogError($"[SimultaneitySelfCheck] FAIL: {label}");
        return condition;
    }

    private sealed class Outcome
    {
        public List<string> Orders = new List<string>();   // sorted keys, one per order
        public string Stances;                              // this player's stance toward every other player after the orders ran
        public string StrategyAfterRound;                   // before the next turn's ApplyWarState
        public string StrategyNextTurn;                     // after it
    }

    // A(0,0) B C D E F(500,0) in a line. Players 0, 1 and 2 hold A+B, C+D and E+F; each has a food shortage on its first planet
    // and a surplus on its second. Players 0 and 1 are at hostility 100 toward each other (both will declare); player 2 is calm.
    private static Dictionary<int, Outcome> RunRound(bool reversed)
    {
        var template = WarshipSelfCheck.MakeTemplate();
        var constants = WarshipSelfCheck.MakeConstants(template);
        constants.maxPathNodesForKnowledge = 8;
        constants.maxPathNodesForShipTransport = 10;
        constants.maxPathNodesForResourceDistribution = 10;
        constants.defaultTravelSpeed = 1f;
        constants.stanceSteepness = 0.01f;     // exactly 0 or 1 war probability: no roulette luck in the stance
        constants.stanceMidpoint = 30f;
        var research = WarshipSelfCheck.MakeResearch();
        var mapGo = new GameObject("SimultaneityMap");
        var gos = new List<GameObject> { mapGo };
        try
        {
            var map = mapGo.AddComponent<GameAIMap>();
            map.GameAIMapInit(new List<PlanetSpawnData>
            {
                ChokepointSelfCheck.Spawn("A", 0f, 0f, new[] { "B" }),
                ChokepointSelfCheck.Spawn("B", 100f, 0f, new[] { "A", "C" }),
                ChokepointSelfCheck.Spawn("C", 200f, 0f, new[] { "B", "D" }),
                ChokepointSelfCheck.Spawn("D", 300f, 0f, new[] { "C", "E" }),
                ChokepointSelfCheck.Spawn("E", 400f, 0f, new[] { "D", "F" }),
                ChokepointSelfCheck.Spawn("F", 500f, 0f, new[] { "E" }),
            }, constants);
            map.Diplomacy.Enabled = true;

            var homes = new[] { ("A", "B"), ("C", "D"), ("E", "F") };
            var ais = new List<PlayerAI>();
            var results = new List<Planet.PlanetUpdateResult>();
            for (var p = 0; p < 3; ++p)
            {
                foreach (var name in new[] { homes[p].Item1, homes[p].Item2 })
                {
                    var planet = map.GetPlanet(name);
                    planet.Owner = p;
                    planet.Population.Add(new Planet.Inhabitant { Player = p });
                }
                WarshipSelfCheck.DockWarships(map.GetPlanet(homes[p].Item1), p, 2);
                var go = new GameObject("SimultaneityPlayer" + p);
                gos.Add(go);
                var player = go.AddComponent<Player>();
                var ai = go.AddComponent<PlayerAI>();
                ai.Player = player;
                ai.AIMap = map;
                player.playerID = p;
                ai.ResearchCatalog = go.AddComponent<Catalog>();
                ai.ResearchCatalog.catalogItems = research;
                ai.Strategy = PlayerAI.AIStrategy.AIStrategyConsolidate;
                ais.Add(ai);
                results.Add(new Planet.PlanetUpdateResult(homes[p].Item1,
                    Planet.PlanetUpdateResult.PlanetUpdateResultType.PlanetUpdateResultTypeFoodShortage, 10f, playerID: p));
                results.Add(new Planet.PlanetUpdateResult(homes[p].Item2,
                    Planet.PlanetUpdateResult.PlanetUpdateResultType.PlanetUpdateResultTypeFoodSurplus, 20f, playerID: p));
            }
            map.Knowledge.Update(map, 3, 8);
            foreach (var pair in new[] { (0, 1), (1, 0) })
            {
                var entry = map.Diplomacy.Get(pair.Item1, pair.Item2);
                entry.Hostility = 100f;
                map.Diplomacy.Set(pair.Item1, pair.Item2, entry);
            }

            var order = Enumerable.Range(0, 3).ToList();
            if (reversed) order.Reverse();
            var perPlayer = new Dictionary<int, List<GameAI.GameAIOrder>>();
            var all = new List<GameAI.GameAIOrder>();
            foreach (var p in order)
            {
                GameAI.Rand = new System.Random(500 + p);      // the same random stream for a player whatever its position
                var orders = new List<GameAI.GameAIOrder>();
                ais[p].ProcessResults(results, orders);
                perPlayer[p] = orders;
                all.AddRange(orders);
            }
            var strategyAfter = ais.Select(a => a.Strategy.ToString()).ToList();
            foreach (var o in all.Where(o => o.Type == GameAI.GameAIOrder.OrderType.OrderTypeDeclareWar
                                             || o.Type == GameAI.GameAIOrder.OrderType.OrderTypeMakePeace))
                GameAI.ApplyStanceOrder(map.Diplomacy, o, 10);
            foreach (var p in order) ais[p].ApplyWarState(11);

            var outcome = new Dictionary<int, Outcome>();
            for (var p = 0; p < 3; ++p)
            {
                var o = new Outcome
                {
                    Orders = perPlayer[p].Select(x => $"{x.Type}|{x.PlayerId}|{x.Origin}|{x.Target}|{x.Data}").OrderBy(s => s).ToList(),
                    Stances = string.Join(",", Enumerable.Range(0, 3).Where(r => r != p)
                        .Select(r => $"{r}:{map.Diplomacy.StanceToward(p, r)}")),
                    StrategyAfterRound = strategyAfter[p],
                    StrategyNextTurn = ais[p].Strategy.ToString(),
                };
                outcome[p] = o;
            }
            return outcome;
        }
        finally
        {
            WarshipSelfCheck.DestroyAll(research);
            foreach (var go in gos) Object.DestroyImmediate(go);
            Object.DestroyImmediate(constants);
            Object.DestroyImmediate(template);
        }
    }

    public static bool RunPlayerOrderIndependenceCheck()
    {
        var ok = true;
        var forward = RunRound(reversed: false);
        var reverse = RunRound(reversed: true);

        ok &= Check(forward[0].Orders.Any(s => s.StartsWith("OrderTypeDeclareWar|0|")),
            "precondition: player 0 declares war on 1 (the stance path is exercised)");
        ok &= Check(forward[0].StrategyNextTurn == "AIStrategyAmass" && forward[2].StrategyNextTurn == "AIStrategyConsolidate",
            "precondition: the declarer is Amass at the next turn's start, the calm player is not");

        for (var p = 0; p < 3; ++p)
        {
            ok &= Check(forward[p].Orders.SequenceEqual(reverse[p].Orders),
                $"player {p}: the same orders whatever the player order\n  forward: {string.Join("; ", forward[p].Orders)}\n  reverse: {string.Join("; ", reverse[p].Orders)}");
            ok &= Check(forward[p].Stances == reverse[p].Stances, $"player {p}: the same stances ({forward[p].Stances} against {reverse[p].Stances})");
            ok &= Check(forward[p].StrategyAfterRound == reverse[p].StrategyAfterRound,
                $"player {p}: the same strategy after the round ({forward[p].StrategyAfterRound} against {reverse[p].StrategyAfterRound})");
            ok &= Check(forward[p].StrategyNextTurn == reverse[p].StrategyNextTurn,
                $"player {p}: the same strategy at the next turn's start ({forward[p].StrategyNextTurn} against {reverse[p].StrategyNextTurn})");
        }
        return ok;
    }
}
```
If `ProcessResults` throws a `NullReferenceException` because the routine needs a catalog the fixture lacks (for example `ProductionCatalog`), give each `PlayerAI` what `PlayerAIResourceSelfCheck` gives it for the same call (read that suite's fixture and copy it into `RunRound`); do not weaken the assertions.

- [ ] **Step 2: Register the suite.** In `AllAISelfChecks.cs` add `("Simultaneity", SimultaneitySelfCheck.RunChecks),` after the Diplomacy line.

- [ ] **Step 3: Create the `.meta` file** (PowerShell; the GUID is random, the block is what Unity writes for a script):

```powershell
function New-Meta($path) {
  $text = "fileFormatVersion: 2`nguid: $([guid]::NewGuid().ToString('N'))`nMonoImporter:`n  externalObjects: {}`n  serializedVersion: 2`n  defaultReferences: []`n  executionOrder: 0`n  icon: {instanceID: 0}`n  userData: `n  assetBundleName: `n  assetBundleVariant: `n"
  [System.IO.File]::WriteAllText("$path.meta", $text, (New-Object System.Text.UTF8Encoding $false))
}
cd C:\Projects\FlatSpace
New-Meta "Assets\Editor\SimultaneitySelfCheck.cs"
```

- [ ] **Step 4: Run it.** Ask the user to focus the Unity Editor and run `FlatSpace -> AI -> Run Simultaneity Self-Check`. Expected: `[SimultaneitySelfCheck] ALL PASSED`. If a `FAIL` line names a player and a field (orders, stances, strategy), that is a real order dependence: go to Task 4. If only the two preconditions fail, the fixture is wrong (the hostility seed or contact): fix the fixture, not the production code.

- [ ] **Step 5: Commit**

```bash
cd /c/Projects/FlatSpace && git add Assets/Editor/SimultaneitySelfCheck.cs Assets/Editor/SimultaneitySelfCheck.cs.meta Assets/Editor/AllAISelfChecks.cs && git commit -q -m "$(cat <<'EOF'
test: SimultaneitySelfCheck runs the players in forward and reversed order

Co-Authored-By: Claude Sonnet 5.5 <noreply@anthropic.com>
EOF
)" && git log -1 --oneline
```

---

### Task 4: The audit, fixes and docs

**Files:**
- Modify: whatever the audit finds (expected: none beyond Task 2)
- Modify: `CLAUDE.md`

**Interfaces:**
- Consumes: the Task 3 check.
- Produces: a recorded audit result.

- [ ] **Step 1: Audit by reading.** Read `PlayerAI.ProcessResults` and everything it reaches: `ProcessResultsStrategyExpand`, `BuildResourceMatrix`, `ProcessColonizers`, `ProcessShipActions`/`PlanShipActions`, `BuildIndustryMatrix`, `ChooseNewResearch`, `RefreshBlockadeView`, `TryEnterConsolidate`, `UpdateDiplomacy`. For each, list every write to state a DIFFERENT player's `ProcessResults` also reads: fields on `Planet` and `Ship`, `GameAIMap` and its `Knowledge`/`Diplomacy`/centrality, the shared `results` and `orders` lists, and static state. Known and already handled: stance and strategy (Tasks 1 and 2); a player's own caches (`_colonizeHeldBack`, `_shipmentHeldBack`, `_blockadeTargets`, `BlockadeView`, the warship multiplier cache) are private to that `PlayerAI`; `GameAI.Rand` is a shared stream whose draw order differs between players (a statistical, not a state, dependence: the check re-seeds it, and the spec leaves execution-order fairness out of scope). Check in particular `Planet` mutators called outside order execution (a `grep` of `PlayerAI.cs` for assignments to planet fields and calls to `Dock`, `Undock`, `Schedule`, `Set...`), and `Diplomacy.TakeCuts/DiscardCuts` (keyed by the victim, so private to the player). Write the findings as a short list in the commit message and in Step 3.

- [ ] **Step 2: Fix each finding** the way the spec says (move the write into an order, or read a start-of-turn snapshot), test first: extend `SimultaneitySelfCheck.RunRound` so the fixture exercises the write, watch it fail, fix, watch it pass. If the audit and the Task 3 run find nothing, change no production code.

- [ ] **Step 3: Docs.** `CLAUDE.md`, "Diplomacy" section, append a short paragraph: "**Simultaneity:** every player decides against the same world. `ProcessResults` only appends orders (stance changes included) and a player's own private caches; nothing a player's decision writes is read by another player's decision in the same turn. `Assets/Editor/SimultaneitySelfCheck.cs` runs three players forward and reversed and asserts the same orders, stances and strategy per player. `ProcessNewOrders` still executes player 0's orders first (accepted; see `FUTURE_FEATURES.md`)." In the self-check list near the top add `FlatSpace → AI → Run Simultaneity Self-Check` in `Assets/Editor/SimultaneitySelfCheck.cs` (the players' decisions are independent of the player order) and add "Simultaneity" to the `Run All AI Self-Checks` suite list. Record the audit's findings in one sentence (for example "the audit found no other cross-player write").

- [ ] **Step 4: Verify.** Ask the user to focus the Unity Editor and run `FlatSpace -> AI -> Run All AI Self-Checks`. Expected: `ALL 12 SUITES PASSED`.

- [ ] **Step 5: Commit**

```bash
cd /c/Projects/FlatSpace && git add -A CLAUDE.md Assets && git status --short | head && git commit -q -m "$(cat <<'EOF'
docs: simultaneity audit result and self-check listed in CLAUDE.md

Co-Authored-By: Claude Sonnet 5.5 <noreply@anthropic.com>
EOF
)" && git log -1 --oneline
```
Before `git add -A`, run `git status --short` and make sure only intended files are staged (the pre-existing `.idea` deletion must not be staged: use explicit paths if it appears).

---

## Tuning log

No new log line. `Stance|<rival>|<Peace or War>|<hostility>|<cutsTerm>|<nearTerm>|<strengthTerm>|<pWar>` keeps its format and is written by `GameAI.ExecuteOrder` when a stance order changes a stance (same turn as before, after every player decided); `WarForced|<rival>|<Start or End>` and `StrategyChange|<from>|<to>` (Consolidate to Amass and back) are written by `ApplyWarState` at the start of the next `ProcessResults` instead of the end of the same one. They stay change-only (the `Stance` line by `SetStance` returning true; the `WarForced` line by the log-only `_warForcedLogged` set). Questions they answer after this change: does a rival's declaration take one turn longer to show up as `WarForced Start` and an Amass switch (expected), and are stance flips, first-war turns and time in Amass unchanged against the 2026-10-04 diplomacy baselines. Task 2 carries the `CLAUDE.md` and `tuning-log` skill updates; no test covers the logger itself (by design).

## After the plan (not part of it)

A tuning/testing pass follows: Play-mode runs on `test2.json` and `4p.json` compared with the diplomacy baselines in `HANDOFF-diplomacy-2026-10-04.md`. Then the combat brainstorm resumes from `2026-10-05-ship-combat-decisions-so-far.md`.
