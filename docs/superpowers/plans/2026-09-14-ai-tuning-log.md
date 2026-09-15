# AI Tuning Log Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add an opt-in, per-match, plain-text log of outcome-level AI events (shipments, colonization, production, research) so AI behavior can be reviewed after a match instead of only through transient in-game notifications.

**Architecture:** A new standalone static class, `AITuningLogger`, appends compact pipe-delimited lines to a timestamped file, called from a handful of new, unconditional call sites placed right next to the existing (untouched) notification calls in `GameAI.cs` and `PlayerAI.cs`. A single opt-in toggle on `Gameboard` decides whether a match's file gets created at all; `AITuningLogger`'s own methods no-op when no match is active, so no call site needs to check the toggle itself.

**Tech Stack:** Unity 6000.4.1f1, C#, plain `System.IO.File` calls — no new dependencies.

**Spec:** `docs/superpowers/specs/2026-09-14-ai-tuning-log-design.md`

## Global Constraints

- **No automated test framework, and this feature's own spec explicitly opts out of a self-check for it too** (file I/O against the real filesystem doesn't fit this project's in-memory Editor self-check pattern — see the spec's Manual Verification section). Every task's "verify" step is a Rider MCP compile check (`mcp__rider__get_file_problems`); the final task is a human Play-mode walkthrough, not a self-check menu item.
- **`AITuningLogger` lives in the global namespace**, matching `SaveLoadSystem`'s own static-class style — no `namespace` wrapper.
- **Every `Log*` method must no-op when `_currentLogPath` is `null`** (no match active / logging disabled) — callers in `GameAI.cs`/`PlayerAI.cs` call them unconditionally and must never need an `if` guard of their own.
- **No close/flush method of any kind.** Each `Log*` call opens, appends, and closes the file within itself (`File.AppendAllLines`) — there is deliberately no persistent open handle and nothing to finalize at "quit," which this project has no consolidated path for anyway.
- **Field/line format is fixed**: `T<turn>|P<playerId>|<EventCode>|<fields...>`, joined with `|`, turn and player always first. `amount` fields render via plain string concatenation of the boxed `Data` object (`order.Data.ToString()`), matching the existing notification text exactly — never a fresh `Convert.ToSingle(...)`.
- **Path convention**: `Application.dataPath + "/Flatspace/AITuningLogs"` in the editor, `Application.persistentDataPath + "/AITuningLogs"` in a build, via `#if UNITY_EDITOR` — the same pattern `SaveLoadSystem` already repeats at each of its own call sites (no shared path helper introduced).
- Follow the commit-message and attribution convention already used on this branch's history: a short body explaining why, ending with the `Co-Authored-By` / `Claude-Session` lines from this session's system reminder.

---

### Task 1: `AITuningLogger` class

**Files:**
- Create: `Assets/Flatspace/Diagnostics/AITuningLogger.cs`

**Interfaces:**
- Produces: `AITuningLogger.BeginMatch(bool enabled)`
- Produces: `AITuningLogger.LogNewOrders(int turnNumber, List<GameAI.GameAIOrder> orders)`
- Produces: `AITuningLogger.LogExecutingOrders(int turnNumber, List<GameAI.GameAIOrder> orders)`
- Produces: `AITuningLogger.LogPlanetEvents(int turnNumber, List<Planet.PlanetUpdateResult> results)`
- Produces: `AITuningLogger.LogResearchStart(int turnNumber, int playerId, string itemName)`
- Produces: `AITuningLogger.LogResearchComplete(int turnNumber, int playerId, string itemName)`
- Consumes: `GameAI.GameAIOrder` (`Type`, `PlayerId`, `Origin`, `Target`, `Data` — all existing public fields), `Planet.PlanetUpdateResult` (`Result`, `Name`, `PlayerID`, `Data` — all existing public fields/properties)

- [ ] **Step 1: Write the class**

Create `Assets/Flatspace/Diagnostics/AITuningLogger.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.IO;
using FlatSpace.AI;
using UnityEngine;

public static class AITuningLogger
{
    private static string _currentLogPath;

    /// <summary>Call once per match, from Gameboard.InitGame. Sets up a fresh timestamped log
    /// file when enabled, or clears any previous match's active path when not — either way,
    /// every Log* method below becomes safe to call unconditionally afterward.</summary>
    public static void BeginMatch(bool enabled)
    {
        if (!enabled)
        {
            _currentLogPath = null;
            return;
        }

#if UNITY_EDITOR
        var dir = Path.Combine(Application.dataPath, "Flatspace/AITuningLogs");
#else
        var dir = Path.Combine(Application.persistentDataPath, "AITuningLogs");
#endif
        Directory.CreateDirectory(dir);
        var fileName = $"aiTuningLog_{DateTime.Now:yyyy-MM-dd_HH-mm-ss}.txt";
        _currentLogPath = Path.Combine(dir, fileName);
    }

    public static void LogNewOrders(int turnNumber, List<GameAI.GameAIOrder> orders)
    {
        if (_currentLogPath == null) return;
        var lines = new List<string>();
        foreach (var order in orders)
        {
            switch (order.Type)
            {
                case GameAI.GameAIOrder.OrderType.OrderTypeFoodTransport:
                    lines.Add(FormatLine(turnNumber, order.PlayerId, "FoodShip",
                        $"{order.Origin}->{order.Target}", order.Data.ToString()));
                    break;
                case GameAI.GameAIOrder.OrderType.OrderTypeGrotsitsTransport:
                    lines.Add(FormatLine(turnNumber, order.PlayerId, "GrotsitsShip",
                        $"{order.Origin}->{order.Target}", order.Data.ToString()));
                    break;
                case GameAI.GameAIOrder.OrderType.OrderTypePopulationTransport:
                    lines.Add(FormatLine(turnNumber, order.PlayerId, "ColonizeStart",
                        $"{order.Origin}->{order.Target}"));
                    break;
                case GameAI.GameAIOrder.OrderType.OrderTypeIndustrySetProduction:
                    lines.Add(FormatLine(turnNumber, order.PlayerId, "ProductionSet",
                        order.Origin, order.Data.ToString()));
                    break;
            }
        }
        AppendLines(lines);
    }

    public static void LogExecutingOrders(int turnNumber, List<GameAI.GameAIOrder> orders)
    {
        if (_currentLogPath == null) return;
        var lines = new List<string>();
        foreach (var order in orders)
        {
            switch (order.Type)
            {
                case GameAI.GameAIOrder.OrderType.OrderTypeFoodTransport:
                    lines.Add(FormatLine(turnNumber, order.PlayerId, "FoodArrive",
                        $"{order.Origin}->{order.Target}", order.Data.ToString()));
                    break;
                case GameAI.GameAIOrder.OrderType.OrderTypeGrotsitsTransport:
                    lines.Add(FormatLine(turnNumber, order.PlayerId, "GrotsitsArrive",
                        $"{order.Origin}->{order.Target}", order.Data.ToString()));
                    break;
                case GameAI.GameAIOrder.OrderType.OrderTypePopulationTransport:
                    lines.Add(FormatLine(turnNumber, order.PlayerId, "ColonizeArrive",
                        $"{order.Origin}->{order.Target}"));
                    break;
            }
        }
        AppendLines(lines);
    }

    public static void LogPlanetEvents(int turnNumber, List<Planet.PlanetUpdateResult> results)
    {
        if (_currentLogPath == null) return;
        var lines = new List<string>();
        foreach (var result in results)
        {
            switch (result.Result)
            {
                case Planet.PlanetUpdateResult.PlanetUpdateResultType.PlanetUpdateResultTypeIndustryProductionComplete:
                    lines.Add(FormatLine(turnNumber, result.PlayerID, "ProductionComplete",
                        result.Name, result.Data?.ToString() ?? string.Empty));
                    break;
                case Planet.PlanetUpdateResult.PlanetUpdateResultType.PlanetUpdateResultTypeColonizerReady:
                    lines.Add(FormatLine(turnNumber, result.PlayerID, "ColonizerReady", result.Name));
                    break;
            }
        }
        AppendLines(lines);
    }

    public static void LogResearchStart(int turnNumber, int playerId, string itemName)
    {
        if (_currentLogPath == null || string.IsNullOrEmpty(itemName)) return;
        AppendLines(new List<string> { FormatLine(turnNumber, playerId, "ResearchStart", itemName) });
    }

    public static void LogResearchComplete(int turnNumber, int playerId, string itemName)
    {
        if (_currentLogPath == null || string.IsNullOrEmpty(itemName)) return;
        AppendLines(new List<string> { FormatLine(turnNumber, playerId, "ResearchComplete", itemName) });
    }

    private static string FormatLine(int turnNumber, int playerId, string eventCode, params string[] fields)
    {
        var parts = new List<string> { $"T{turnNumber}", $"P{playerId}", eventCode };
        parts.AddRange(fields);
        return string.Join("|", parts);
    }

    private static void AppendLines(List<string> lines)
    {
        if (lines.Count == 0 || _currentLogPath == null) return;
        File.AppendAllLines(_currentLogPath, lines);
    }
}
```

`result.Data?.ToString() ?? string.Empty` guards `ProductionComplete` specifically because
`Planet.cs` builds that result with `CurrentProduction?.Item.itemName` — a nullable chain — as the
`Data` value, unlike every other event's `Data`, which is always a non-null boxed number.

- [ ] **Step 2: Compile-check**

Run: `mcp__rider__get_file_problems` (`rootFolder: "C:\\Projects\\FlatSpace"`) on
`Assets/Flatspace/Diagnostics/AITuningLogger.cs`.
Expected: no errors.

- [ ] **Step 3: Commit**

```bash
git add Assets/Flatspace/Diagnostics/AITuningLogger.cs
git commit -m "feat(diagnostics): add AITuningLogger for outcome-level AI event logging"
```

---

### Task 2: Wire the opt-in toggle and match start

**Files:**
- Modify: `Assets/Flatspace/Objects/Board/GameBoard.cs` (new field near line 40; `InitGame` around line 284)

**Interfaces:**
- Consumes: `AITuningLogger.BeginMatch(bool enabled)` (Task 1)

- [ ] **Step 1: Add the toggle field**

Open `Assets/Flatspace/Objects/Board/GameBoard.cs`. Find (around line 40):

```csharp
            [SerializeField] private FlatSpace.Fog.FogOfWarSettings _fogOfWarSettings;
```

Add one line right after it:

```csharp
            [SerializeField] private FlatSpace.Fog.FogOfWarSettings _fogOfWarSettings;
            [SerializeField] private bool _logAIEvents = false;
```

- [ ] **Step 2: Call `BeginMatch` at the start of `InitGame`**

Find (around line 284):

```csharp
            private void InitGame(List<PlanetSpawnData> planetSpawnData)
            {
                if (GameAI == null)
                    GameAI = this.AddComponent<GameAI>() as GameAI;
```

Replace with:

```csharp
            private void InitGame(List<PlanetSpawnData> planetSpawnData)
            {
                AITuningLogger.BeginMatch(_logAIEvents);

                if (GameAI == null)
                    GameAI = this.AddComponent<GameAI>() as GameAI;
```

This runs once per match (including a reload from save, since `InitGame` is also called on the
addressable-board load path), always at the very start before anything else in the match is set up —
`BeginMatch` has no dependency on any of that state.

- [ ] **Step 3: Compile-check**

Run: `mcp__rider__get_file_problems` on `Assets/Flatspace/Objects/Board/GameBoard.cs`.
Expected: no errors.

- [ ] **Step 4: Commit**

```bash
git add Assets/Flatspace/Objects/Board/GameBoard.cs
git commit -m "feat(diagnostics): add the AI tuning log opt-in toggle"
```

---

### Task 3: Wire the remaining call sites in `GameAI.cs` and `PlayerAI.cs`

**Files:**
- Modify: `Assets/Flatspace/GameAI/GameAI.cs` (`ProcessCurrentOrders` around line 84; `GameAIUpdate` around line 71)
- Modify: `Assets/Flatspace/GameAI/PlayerAI.cs` (`CompleteResearch` around line 399; `ChooseNewResearch` around line 412)

**Interfaces:**
- Consumes: `AITuningLogger.LogExecutingOrders`, `LogPlanetEvents`, `LogNewOrders`, `LogResearchStart`, `LogResearchComplete` (all from Task 1)

- [ ] **Step 1: Log executing orders**

Open `Assets/Flatspace/GameAI/GameAI.cs`. Find, in `ProcessCurrentOrders()` (around line 91):

```csharp
                var executableOrders = CurrentAIOrders.FindAll(x => x.TimingDelay <= 0);
                Gameboard.Instance.CreateNotificationsForExecutingOrders(executableOrders);
                foreach (var executableOrder in executableOrders)
```

Add one line:

```csharp
                var executableOrders = CurrentAIOrders.FindAll(x => x.TimingDelay <= 0);
                Gameboard.Instance.CreateNotificationsForExecutingOrders(executableOrders);
                AITuningLogger.LogExecutingOrders(Gameboard.Instance.TurnNumber, executableOrders);
                foreach (var executableOrder in executableOrders)
```

- [ ] **Step 2: Log planet events and new orders**

In the same file, find `GameAIUpdate()` (around line 71-82):

```csharp
            public void GameAIUpdate()
            {
                var gameAIOrders = new List<GameAIOrder>();
                var planetUpdateResults = new List<Planet.PlanetUpdateResult>();
                ProcessCurrentOrders();
                planetUpdateResults.Clear();
                UpdateAllPlanets(planetUpdateResults);
                GameAIMap.Knowledge.Update(GameAIMap, Gameboard.Instance.players.Count);
                ProcessResults(planetUpdateResults, gameAIOrders);
                Gameboard.Instance.CreateNotificationsForNewOrders(gameAIOrders);
                ProcessNewOrders(gameAIOrders);
            }
```

Replace with:

```csharp
            public void GameAIUpdate()
            {
                var gameAIOrders = new List<GameAIOrder>();
                var planetUpdateResults = new List<Planet.PlanetUpdateResult>();
                ProcessCurrentOrders();
                planetUpdateResults.Clear();
                UpdateAllPlanets(planetUpdateResults);
                AITuningLogger.LogPlanetEvents(Gameboard.Instance.TurnNumber, planetUpdateResults);
                GameAIMap.Knowledge.Update(GameAIMap, Gameboard.Instance.players.Count);
                ProcessResults(planetUpdateResults, gameAIOrders);
                Gameboard.Instance.CreateNotificationsForNewOrders(gameAIOrders);
                AITuningLogger.LogNewOrders(Gameboard.Instance.TurnNumber, gameAIOrders);
                ProcessNewOrders(gameAIOrders);
            }
```

- [ ] **Step 3: Compile-check**

Run: `mcp__rider__get_file_problems` on `Assets/Flatspace/GameAI/GameAI.cs`.
Expected: no errors.

- [ ] **Step 4: Log research start and completion**

Open `Assets/Flatspace/GameAI/PlayerAI.cs`. Find `CompleteResearch` (around line 399-411):

```csharp
            private void CompleteResearch(List<GameAI.GameAIOrder> orders)
            {
                if (currentResearch == null)
                    return;
                var completedResearchName = currentResearch.name;
                currentResearch.researched = true;
                foreach( var dependentItem in ProductionCatalog.catalogItems.FindAll(x => x.requiredTech == currentResearch.itemName))
                {
                    dependentItem.researched  = true;
                }
                Gameboard.Instance.CreateNotificationsForCompletedResearch(completedResearchName, Player.playerID);

            }
```

Add one line after the notification call:

```csharp
            private void CompleteResearch(List<GameAI.GameAIOrder> orders)
            {
                if (currentResearch == null)
                    return;
                var completedResearchName = currentResearch.name;
                currentResearch.researched = true;
                foreach( var dependentItem in ProductionCatalog.catalogItems.FindAll(x => x.requiredTech == currentResearch.itemName))
                {
                    dependentItem.researched  = true;
                }
                Gameboard.Instance.CreateNotificationsForCompletedResearch(completedResearchName, Player.playerID);
                AITuningLogger.LogResearchComplete(Gameboard.Instance.TurnNumber, Player.playerID, completedResearchName);

            }
```

Find `ChooseNewResearch` (around line 412-438), the closing lines:

```csharp
                orders.Add(MakeOrder(
                    GameAI.GameAIOrder.OrderType.OrderTypeResearchSet,
                    GameAI.GameAIOrder.OrderTimingType.OrderTimingTypeImmediate,
                    0, 0, 0f, currentResearch.itemName, currentResearch.itemName));
                Gameboard.Instance.CreateNotificationsForNewResearch(currentResearch?.itemName, Player.playerID);
            }
```

Add one line:

```csharp
                orders.Add(MakeOrder(
                    GameAI.GameAIOrder.OrderType.OrderTypeResearchSet,
                    GameAI.GameAIOrder.OrderTimingType.OrderTimingTypeImmediate,
                    0, 0, 0f, currentResearch.itemName, currentResearch.itemName));
                Gameboard.Instance.CreateNotificationsForNewResearch(currentResearch?.itemName, Player.playerID);
                AITuningLogger.LogResearchStart(Gameboard.Instance.TurnNumber, Player.playerID, currentResearch?.itemName);
            }
```

- [ ] **Step 5: Compile-check**

Run: `mcp__rider__get_file_problems` on `Assets/Flatspace/GameAI/PlayerAI.cs`.
Expected: no errors.

- [ ] **Step 6: Commit**

```bash
git add Assets/Flatspace/GameAI/GameAI.cs Assets/Flatspace/GameAI/PlayerAI.cs
git commit -m "feat(diagnostics): log orders, planet events, and research to the AI tuning log"
```

---

### Task 4: Manual verification

**Files:** none (verification only)

**Interfaces:** none

- [ ] **Step 1: Confirm the toggle stays off by default**

Ask the user to let the Editor recompile, leave the new `Log AI Events` checkbox on the `Gameboard`
component unchecked (its default), play a full match, and confirm no
`Assets/Flatspace/AITuningLogs/` folder appears.

- [ ] **Step 2: Confirm a full match logs correctly**

Ask the user to check the `Log AI Events` box on `Gameboard` in the Inspector, start a new match, and
play enough turns to see at least one food or grotsits shipment, one colonization, one production
cycle, and one research completion. Confirm a timestamped file appears under
`Assets/Flatspace/AITuningLogs/` and that its lines match what actually happened, in the format from
the spec (e.g. `T12|P0|FoodShip|P0Surplus->P0Shortage|15`).

- [ ] **Step 3: Confirm a new match starts a new file**

Ask the user to start a second match with the toggle still on, play a few turns, and confirm a second,
distinctly-timestamped file appears rather than the first one being overwritten or appended past a
gap.

- [ ] **Step 4: Confirm the log is usable for a real tuning question**

Ask the user to point Claude at one of the generated log files and ask a concrete question about it
(e.g. "how many times did player 2 ship food this match" or "when did player 0 first colonize"), and
confirm Claude can answer it directly from the file's contents via the `Read` tool.

No commit for this task — it produces no code changes.
