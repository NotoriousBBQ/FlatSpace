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
        // Sibling of Assets (project root), so Unity's asset database and IDE searches never see the logs.
        var dir = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "AITuningLogs"));
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
                case GameAI.GameAIOrder.OrderType.OrderTypeShipTransport:
                    lines.Add(FormatLine(turnNumber, order.PlayerId, "ShipMove",
                        $"{order.Origin}->{order.Target}", order.Data.ToString()));
                    break;
                case GameAI.GameAIOrder.OrderType.OrderTypeColonyFoodRider:
                    lines.Add(FormatLine(turnNumber, order.PlayerId, "ColonyRider",
                        $"{order.Origin}->{order.Target}", order.Data.ToString()));
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
                case GameAI.GameAIOrder.OrderType.OrderTypeShipTransport:
                    lines.Add(FormatLine(turnNumber, order.PlayerId, "ShipArrive",
                        $"{order.Origin}->{order.Target}", order.Data.ToString()));
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
                // Colony failures: a lost inhabitant (with its player) and, when the last one goes, a dead planet
                // (the dead result carries no player, so it logs as P-1; pair it with the PopulationLoss before it).
                case Planet.PlanetUpdateResult.PlanetUpdateResultType.PlanetUpdateResultTypePopulationLoss:
                    lines.Add(FormatLine(turnNumber, result.PlayerID, "PopulationLoss", result.Name));
                    break;
                case Planet.PlanetUpdateResult.PlanetUpdateResultType.PlanetUpdateResultTypeDead:
                    lines.Add(FormatLine(turnNumber, result.PlayerID, "PlanetDead", result.Name));
                    break;
                // Colony ships destroyed in combat: planet, owner, how many, and the lowest-numbered rival that did it.
                case Planet.PlanetUpdateResult.PlanetUpdateResultType.PlanetUpdateResultTypeColonyShipsLost:
                    if (result.Data is FlatSpace.AI.ColonyLoss colonyLoss)
                        lines.Add(FormatLine(turnNumber, result.PlayerID, "ColonyShipsLost", result.Name,
                            result.PlayerID.ToString(), colonyLoss.Count.ToString(), colonyLoss.ByPlayer.ToString()));
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

    /// <summary>Combat at a planet: T&lt;turn&gt;|P&lt;attacker&gt;|Combat|planet|attacker-&gt;victim|damageDealt|shipsDestroyed (ships may be fractional: the attacker's share).</summary>
    public static void LogCombat(int turnNumber, int attacker, string planet, int victim, float damage, float ships)
    {
        if (_currentLogPath == null) return;
        var ci = System.Globalization.CultureInfo.InvariantCulture;
        AppendLines(new List<string> { FormatLine(turnNumber, attacker, "Combat", planet, $"{attacker}->{victim}",
            damage.ToString("0.#", ci), ships.ToString("0.##", ci)) });
    }

    /// <summary>A surrender: T&lt;turn&gt;|P&lt;surrenderer&gt;|Surrender|rival|lossShare|pSurrender|truceUntil.</summary>
    public static void LogSurrender(int turnNumber, int me, int rival, float lossShare, float pSurrender, int truceUntil)
    {
        if (_currentLogPath == null) return;
        var ci = System.Globalization.CultureInfo.InvariantCulture;
        AppendLines(new List<string> { FormatLine(turnNumber, me, "Surrender", rival.ToString(ci), lossShare.ToString("0.##", ci),
            pSurrender.ToString("0.###", ci), truceUntil.ToString(ci)) });
    }

    public static void LogStrategyChange(int turnNumber, int playerId, string from, string to)
    {
        if (_currentLogPath == null) return;
        AppendLines(new List<string> { FormatLine(turnNumber, playerId, "StrategyChange", from, to) });
    }

    public static void LogAssaultTarget(int turnNumber, int playerId, string target, int requiredForce)
    {
        if (_currentLogPath == null) return;
        AppendLines(new List<string> { FormatLine(turnNumber, playerId, "AssaultTarget", target, requiredForce.ToString()) });
    }
    /// <summary>The blockade-breaking target changed: T&lt;turn&gt;|P&lt;id&gt;|BlockadeTarget|planet|blocker|value|neededOffense|Committed, RecentCut, Chokepoint or Cheapest. Blocker -1 = remembered, unseen.</summary>
    public static void LogBlockadeTarget(int turnNumber, int playerId, string planet, int blocker, float value,
        float neededOffense, string reason)
    {
        if (_currentLogPath == null) return;
        var ci = System.Globalization.CultureInfo.InvariantCulture;
        AppendLines(new List<string> { FormatLine(turnNumber, playerId, "BlockadeTarget", planet, blocker.ToString(ci),
            value.ToString("0.#", ci), neededOffense.ToString("0.#", ci), reason) });
    }

    /// <summary>Ships were sent at the blockade target this turn: T&lt;turn&gt;|P&lt;id&gt;|BlockadeForce|planet|ships|offense|stillNeeded.</summary>
    public static void LogBlockadeForce(int turnNumber, int playerId, string planet, int ships, float offense,
        float stillNeeded)
    {
        if (_currentLogPath == null) return;
        var ci = System.Globalization.CultureInfo.InvariantCulture;
        AppendLines(new List<string> { FormatLine(turnNumber, playerId, "BlockadeForce", planet, ships.ToString(ci),
            offense.ToString("0.#", ci), stillNeeded.ToString("0.#", ci)) });
    }

    /// <summary>A blockade target stopped being the target: T&lt;turn&gt;|P&lt;id&gt;|BlockadeTargetEnd|planet|Cleared, Switched or Unreachable|turnsHeld.</summary>
    public static void LogBlockadeTargetEnd(int turnNumber, int playerId, string planet, string reason, int turnsHeld)
    {
        if (_currentLogPath == null) return;
        AppendLines(new List<string> { FormatLine(turnNumber, playerId, "BlockadeTargetEnd", planet, reason,
            turnsHeld.ToString(System.Globalization.CultureInfo.InvariantCulture)) });
    }

    /// <summary>A blockaded planet the assault passed over, on change only: T&lt;turn&gt;|P&lt;id&gt;|BlockadeSkipped|planet|NoPath or Outranked|value|winner|winnerCommitted. Winner is - and winnerCommitted 0 for NoPath.</summary>
    public static void LogBlockadeSkipped(int turnNumber, int playerId, string planet, string reason, float value,
        string winner, float winnerCommitted)
    {
        if (_currentLogPath == null) return;
        var ci = System.Globalization.CultureInfo.InvariantCulture;
        AppendLines(new List<string> { FormatLine(turnNumber, playerId, "BlockadeSkipped", planet, reason,
            value.ToString("0.#", ci), winner, winnerCommitted.ToString("0.#", ci)) });
    }

    /// <summary>A research start picked a Warship Offense item while the blockade boost was active: T&lt;turn&gt;|P&lt;id&gt;|OffenseResearchBoost|item|multiplier.</summary>
    public static void LogOffenseResearchBoost(int turnNumber, int playerId, string itemName, float multiplier)
    {
        if (_currentLogPath == null) return;
        AppendLines(new List<string> { FormatLine(turnNumber, playerId, "OffenseResearchBoost", itemName,
            multiplier.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture)) });
    }

    public static void LogWarshipBoost(int turnNumber, int playerId, int wanted, int have, float multiplier)
    {
        if (_currentLogPath == null) return;
        AppendLines(new List<string> { FormatLine(turnNumber, playerId, "WarshipBoost",
            wanted.ToString(), have.ToString(),
            multiplier.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture)) });
    }

    /// <summary>A blockade was found at a planet an order passed: T&lt;turn&gt;|P&lt;order owner&gt;|Blockade|planet|blocker|value.</summary>
    public static void LogBlockade(int turnNumber, int playerId, string planetName, int blocker, float value)
    {
        if (_currentLogPath == null) return;
        AppendLines(new List<string> { FormatLine(turnNumber, playerId, "Blockade", planetName, blocker.ToString(),
            value.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture)) });
    }

    /// <summary>A planet became newly remembered as blockaded after one of the player's orders was cut there (not logged on refreshes): T&lt;turn&gt;|P&lt;id&gt;|BlockadeLearned|planet|value.</summary>
    public static void LogBlockadeLearned(int turnNumber, int playerId, string planetName, float value)
    {
        if (_currentLogPath == null) return;
        AppendLines(new List<string> { FormatLine(turnNumber, playerId, "BlockadeLearned", planetName,
            value.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture)) });
    }

    /// <summary>A Warship or Update Warship was started on a planet blockaded against its owner: T&lt;turn&gt;|P&lt;id&gt;|BlockadedProduction|planet|item|value. Marks the fleet-cap exemption.</summary>
    public static void LogBlockadedProduction(int turnNumber, int playerId, string planetName, string itemName, float value)
    {
        if (_currentLogPath == null) return;
        AppendLines(new List<string> { FormatLine(turnNumber, playerId, "BlockadedProduction", planetName, itemName,
            value.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture)) });
    }

    /// <summary>An order was cut by a blockade: T&lt;turn&gt;|P&lt;order owner&gt;|OrderBlocked|orderType|planet|amountRemaining (0 = removed).</summary>
    public static void LogOrderBlocked(int turnNumber, int playerId, string orderType, string planetName, float remaining)
    {
        if (_currentLogPath == null) return;
        AppendLines(new List<string> { FormatLine(turnNumber, playerId, "OrderBlocked", orderType, planetName,
            remaining.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture)) });
    }

    /// <summary>A colonist took a detour around visible blockades: T&lt;turn&gt;|P&lt;id&gt;|RouteDetour|origin-&gt;target|nodes joined by '&gt;'|cost.</summary>
    public static void LogRouteDetour(int turnNumber, int playerId, string origin, string target,
        IEnumerable<string> nodes, float cost)
    {
        if (_currentLogPath == null) return;
        AppendLines(new List<string> { FormatLine(turnNumber, playerId, "RouteDetour", $"{origin}->{target}",
            string.Join(">", nodes), cost.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture)) });
    }

    /// <summary>A colonist arrived at a full planet and docked as a colony ship: T&lt;turn&gt;|P&lt;id&gt;|ColonistDocked|planet|amount.</summary>
    public static void LogColonistDocked(int turnNumber, int playerId, string planetName, int amount)
    {
        if (_currentLogPath == null) return;
        AppendLines(new List<string> { FormatLine(turnNumber, playerId, "ColonistDocked", planetName,
            amount.ToString(System.Globalization.CultureInfo.InvariantCulture)) });
    }

    /// <summary>A colonist in flight was redirected: T&lt;turn&gt;|P&lt;id&gt;|ColonistRedirect|origin-&gt;oldTarget|Detour or Divert|newTarget|nodes joined by '&gt;'|cost|blocked planets (name=value joined by ',', '-' when none)|declined detour cost ('-' unless a diversion was chosen over an available detour).</summary>
    public static void LogColonistRedirect(int turnNumber, int playerId, string origin, string oldTarget, string kind,
        string newTarget, IEnumerable<string> nodes, float cost, string blockedSummary, float declinedDetourCost = -1f)
    {
        if (_currentLogPath == null) return;
        var ci = System.Globalization.CultureInfo.InvariantCulture;
        AppendLines(new List<string> { FormatLine(turnNumber, playerId, "ColonistRedirect", $"{origin}->{oldTarget}", kind,
            newTarget, string.Join(">", nodes), cost.ToString("0.#", ci), blockedSummary,
            declinedDetourCost < 0f ? "-" : declinedDetourCost.ToString("0.#", ci)) });
    }

    /// <summary>A colonist in flight could not be saved from a blockade ahead: T&lt;turn&gt;|P&lt;id&gt;|ColonistRedirectFailed|origin-&gt;target|blocked planets. Once per order.</summary>
    public static void LogColonistRedirectFailed(int turnNumber, int playerId, string origin, string target, string blockedSummary)
    {
        if (_currentLogPath == null) return;
        AppendLines(new List<string> { FormatLine(turnNumber, playerId, "ColonistRedirectFailed", $"{origin}->{target}",
            blockedSummary) });
    }

    /// <summary>A shipment was sent through unavoidable blockades: T&lt;turn&gt;|P&lt;id&gt;|ShipmentLossy|origin-&gt;target|amount|loss.</summary>
    public static void LogShipmentLossy(int turnNumber, int playerId, string origin, string target, float amount, float loss)
    {
        if (_currentLogPath == null) return;
        AppendLines(new List<string> { FormatLine(turnNumber, playerId, "ShipmentLossy", $"{origin}->{target}",
            amount.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture),
            loss.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture)) });
    }

    /// <summary>
    /// A shortage could not be supplied because blockades removed every source: T&lt;turn&gt;|P&lt;id&gt;|ShipmentCancelled|target|reason|source|loss|amount|blockedNodes.
    /// reason is Blockade (loss &gt;= amount) or LowYield (below the minimum delivered fraction); source, loss and amount describe the
    /// dropped pair closest to shipping; blockedNodes lists that route's blockaded planets as name=value joined by ',' ('-' when none).
    /// Logged on state change only.
    /// </summary>
    public static void LogShipmentCancelled(int turnNumber, int playerId, string target, string reason, string source,
        float loss, float amount, string blockedNodes)
    {
        if (_currentLogPath == null) return;
        var ci = System.Globalization.CultureInfo.InvariantCulture;
        AppendLines(new List<string> { FormatLine(turnNumber, playerId, "ShipmentCancelled", target, reason, source,
            loss.ToString("0.#", ci), amount.ToString("0.#", ci), blockedNodes) });
    }

    /// <summary>
    /// A populated planet became short of grotsits or stopped being short: T&lt;turn&gt;|P&lt;owner&gt;|GrotsitsShort|planet|Start or End|population|capacity|upkeep|morale.
    /// capacity is the planet's grotsits capacity (base plus max population x worker rate) and upkeep its improvement upkeep,
    /// so demand is about population + upkeep; morale is after the turn's update. Logged on state change only.
    /// </summary>
    public static void LogGrotsitsShort(int turnNumber, int playerId, string planet, bool started, int population,
        float capacity, float upkeep, float morale)
    {
        if (_currentLogPath == null) return;
        var ci = System.Globalization.CultureInfo.InvariantCulture;
        AppendLines(new List<string> { FormatLine(turnNumber, playerId, "GrotsitsShort", planet, started ? "Start" : "End",
            population.ToString(ci), capacity.ToString("0.#", ci), upkeep.ToString("0.#", ci), morale.ToString("0", ci)) });
    }

    /// <summary>A ready colonizer was held back: T&lt;turn&gt;|P&lt;id&gt;|ColonizeCancelled|origin|BlockadedOrigin or NoRoute. Repeats every turn the condition holds.</summary>
    public static void LogColonizeCancelled(int turnNumber, int playerId, string origin, string reason)
    {
        if (_currentLogPath == null) return;
        AppendLines(new List<string> { FormatLine(turnNumber, playerId, "ColonizeCancelled", origin, reason) });
    }

    /// <summary>A Consolidate colonist went to a different planet than the nearest candidate because of the chokepoint tilt: T&lt;turn&gt;|P&lt;id&gt;|ChokepointColonize|origin-&gt;target|routeCost|percentile|nearestTarget|nearestCost.</summary>
    public static void LogChokepointColonize(int turnNumber, int playerId, string origin, string target, float routeCost,
        float percentile, string nearestTarget, float nearestCost)
    {
        if (_currentLogPath == null) return;
        var ci = System.Globalization.CultureInfo.InvariantCulture;
        AppendLines(new List<string> { FormatLine(turnNumber, playerId, "ChokepointColonize", $"{origin}->{target}",
            routeCost.ToString("0.#", ci), percentile.ToString("0.00", ci), nearestTarget,
            nearestCost.ToString("0.#", ci)) });
    }

    /// <summary>Logged the turn a Distribution Center is designated for a resource.</summary>
    public static void LogDCSelected(int turnNumber, int playerId, string planetName, string resource)
    {
        if (_currentLogPath == null) return;
        AppendLines(new List<string> { FormatLine(turnNumber, playerId, "DCSelected", planetName, resource) });
    }

    /// <summary>Logged the turn a Distribution Center designation clears (captured or population died out).</summary>
    public static void LogDCLost(int turnNumber, int playerId, string planetName, string resource)
    {
        if (_currentLogPath == null) return;
        AppendLines(new List<string> { FormatLine(turnNumber, playerId, "DCLost", planetName, resource) });
    }

    /// <summary>
    /// Logged on a newly-colonized planet's arrival, once per resource, when that planet is unreachable
    /// from both every currently-surplus-reporting planet and every currently-designated DC for that
    /// resource — evidence for whether sticky single-DC selection is giving good enough coverage.
    /// </summary>
    public static void LogDCCoverageGap(int turnNumber, int playerId, string planetName, string resource)
    {
        if (_currentLogPath == null) return;
        AppendLines(new List<string> { FormatLine(turnNumber, playerId, "DCCoverageGap", planetName, resource) });
    }

    /// <summary>
    /// The name to log for the board a match runs on: the BoardConfiguration asset's name when there is one,
    /// otherwise the designer JSON's file name (not its full path), otherwise "unknown". Never contains the
    /// log's field separator. Pure, so it is self-checked even though the file I/O around it is not.
    /// </summary>
    public static string DescribeBoard(string boardConfigName, string boardDesignPath)
    {
        string name;
        if (!string.IsNullOrEmpty(boardConfigName))
            name = boardConfigName;
        else if (!string.IsNullOrEmpty(boardDesignPath))
            name = boardDesignPath.Substring(boardDesignPath.LastIndexOfAny(new[] { '/', '\\' }) + 1);
        else
            name = "unknown";
        return string.IsNullOrEmpty(name) ? "unknown" : name.Replace('|', '_');
    }

    /// <summary>One player's economy every 25 turns: T&lt;turn&gt;|P&lt;id&gt;|Economy|planets|planetsShortOfGrotsits|meanMorale|totalUpkeep.</summary>
    public static void LogEconomy(int turnNumber, int playerId, int planets, int planetsShort, float meanMorale, float totalUpkeep)
    {
        if (_currentLogPath == null) return;
        var inv = System.Globalization.CultureInfo.InvariantCulture;
        AppendLines(new List<string> { FormatLine(turnNumber, playerId, "Economy", planets.ToString(),
            planetsShort.ToString(), meanMorale.ToString("0.#", inv), totalUpkeep.ToString("0.#", inv)) });
    }

    /// <summary>The Chokepoints field: name=betweenness%percentile joined by commas, or - when empty.</summary>
    public static string FormatChokepointList(IEnumerable<(string name, int betweenness, float percentile)> chokepoints)
    {
        var inv = System.Globalization.CultureInfo.InvariantCulture;
        var parts = new List<string>();
        foreach (var c in chokepoints)
            parts.Add($"{c.name}={c.betweenness}%{c.percentile.ToString("0.00", inv)}");
        return parts.Count == 0 ? "-" : string.Join(",", parts);
    }

    /// <summary>The board's top chokepoints, once per InitGame (the last is the real board): T0|P-1|Chokepoints|name=betweenness%percentile,...</summary>
    public static void LogChokepoints(IEnumerable<(string name, int betweenness, float percentile)> chokepoints)
    {
        if (_currentLogPath == null) return;
        AppendLines(new List<string> { FormatLine(0, -1, "Chokepoints", FormatChokepointList(chokepoints)) });
    }

    /// <summary>Every 25 turns per player: T&lt;turn&gt;|P&lt;id&gt;|ChokepointGarrison|colonized|boardTotal|shipsOnThem|allShips.</summary>
    public static void LogChokepointGarrison(int turnNumber, int playerId, int colonized, int boardTotal,
        int shipsOnThem, int allShips)
    {
        if (_currentLogPath == null) return;
        AppendLines(new List<string> { FormatLine(turnNumber, playerId, "ChokepointGarrison", colonized.ToString(),
            boardTotal.ToString(), shipsOnThem.ToString(), allShips.ToString()) });
    }

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

    /// <summary>Records which board the match started on, right after BeginMatch, as T0|P-1|BoardConfig|name.</summary>
    public static void LogBoardConfig(string boardName)
    {
        if (_currentLogPath == null) return;
        AppendLines(new List<string> { FormatLine(0, -1, "BoardConfig", boardName) });
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
