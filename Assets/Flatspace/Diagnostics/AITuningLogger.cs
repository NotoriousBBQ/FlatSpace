using System;
using System.Collections.Generic;
using System.IO;
using FlatSpace.AI;
using UnityEngine;

public static class AITuningLogger
{
    private static string _currentLogPath;

    /// <summary>Set from MainMenu's own Inspector-configurable toggle, before it loads the
    /// Flatspace scene — lets a developer enable logging without opening/editing Flatspace.unity
    /// directly. OR'd with Gameboard's own scene-level toggle in BeginMatch's caller, so pressing
    /// Play directly on the Flatspace scene (bypassing MainMenu) still works via that field alone.
    /// Static so it survives the MainMenu -> Flatspace scene load with no DontDestroyOnLoad
    /// ceremony (Unity only clears statics on domain reload / exiting Play mode).</summary>
    public static bool EnabledViaMainMenu;

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
        try
        {
            File.AppendAllLines(_currentLogPath, lines);
        }
        catch (Exception e)
        {
            Debug.LogWarning($"AITuningLogger: disabling logging after write failure: {e.Message}");
            _currentLogPath = null;
        }
    }
}
