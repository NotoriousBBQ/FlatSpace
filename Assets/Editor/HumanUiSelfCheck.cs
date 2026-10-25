using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using FlatSpace.AI;
using FlatSpace.Human;
using Flatspace.Objects.Production;

// Self-check for the human player UI (docs/superpowers/specs/2026-10-23-human-player-ui-design.md, stage 1): the pure classes the
// screens are built from. Like the other suites it never touches Gameboard.Instance. It reuses the fixture of HumanModeSelfCheck.
public static class HumanUiSelfCheck
{
    private static int _checkCount;

    [MenuItem("FlatSpace/Human/Run Human UI Self-Check")]
    public static void Run()
    {
        var ok = RunChecks();
        Debug.Log(ok
            ? $"[HumanUiSelfCheck] ALL PASSED ({_checkCount} assertions ran)"
            : $"[HumanUiSelfCheck] FAILURES (see errors above; {_checkCount} assertions ran)");
    }

    public static bool RunChecks()
    {
        _checkCount = 0;
        var ok = RunCheckChecks();
        ok &= RunNotificationChecks();
        ok &= RunResearchFollowUpChecks();
        ok &= RunContactChecks();
        ok &= RunVisibleRivalChecks();
        ok &= RunCheckBatchChecks();
        ok &= RunShipmentListChecks();
        ok &= RunBuildListChecks();
        ok &= RunPlanetViewChecks();
        ok &= RunResearchTreeChecks();
        ok &= RunHoverTextChecks();
        ok &= RunLinePickChecks();
        ok &= RunFleetListChecks();
        ok &= RunSightMemoryChecks();
        ok &= RunPlanetTableChecks();
        ok &= RunBlockadeRelevanceChecks();
        return ok;
    }

    #region Stage 2 task 3: hover text, line pick, fleet list

    private static GameAI.GameAIOrder ShipOrder(int count, int turns, int player = 0, string origin = "P0", string target = "P1")
    {
        var fleet = new GameAI.GameAIOrder.ShipFleetPayload { Kind = Ship.ShipKind.WarShip };
        for (var i = 0; i < count; i++) fleet.Snapshots.Add(new List<string>());
        return new GameAI.GameAIOrder
        {
            Type = GameAI.GameAIOrder.OrderType.OrderTypeShipTransport,
            Data = count, Origin = origin, Target = target, PlayerId = player, TimingDelay = turns, Fleet = fleet,
        };
    }

    private static GameAI.GameAIOrder TypedOrder(GameAI.GameAIOrder.OrderType type, object data, int turns, int player = 0)
        => new GameAI.GameAIOrder { Type = type, Data = data, Origin = "P0", Target = "P1", PlayerId = player, TimingDelay = turns };

    public static bool RunHoverTextChecks()
    {
        var ok = true;
        ok &= Check(HumanHoverText.Ships(new[] { Ship.ShipKind.WarShip, Ship.ShipKind.SiegeShip, Ship.ShipKind.WarShip, Ship.ShipKind.WarShip })
                    == "3 WarShip, 1 SiegeShip", "ship text lists counts per kind in enum order");
        ok &= Check(HumanHoverText.Ships(new[] { Ship.ShipKind.ColonyShip }) == "1 ColonyShip", "a single ship reads '1 ColonyShip'");
        ok &= Check(HumanHoverText.Ships(new Ship.ShipKind[0]) == "no ships", "no ships reads 'no ships'");

        ok &= Check(HumanHoverText.Order(ShipOrder(2, 3)) == "2 WarShip to P1, arrives in 3 turns", "ship order text: ships, destination, turns");
        ok &= Check(HumanHoverText.Order(ShipOrder(1, 1)) == "1 WarShip to P1, arrives in 1 turn", "one turn is singular");
        ok &= Check(HumanHoverText.Order(TypedOrder(GameAI.GameAIOrder.OrderType.OrderTypeFoodTransport, 7, 3)) == "7 Food to P1, arrives in 3 turns", "food shipment text");
        ok &= Check(HumanHoverText.Order(TypedOrder(GameAI.GameAIOrder.OrderType.OrderTypeGrotsitsTransport, 7.5f, 2)) == "7.5 Grotsits to P1, arrives in 2 turns", "grotsits shipment text keeps one decimal");
        ok &= Check(HumanHoverText.Order(TypedOrder(GameAI.GameAIOrder.OrderType.OrderTypePopulationTransport, 1, 4)) == "Colonist to P1, arrives in 4 turns", "colonist text");
        ok &= Check(HumanHoverText.Order(TypedOrder(GameAI.GameAIOrder.OrderType.OrderTypeColonyFoodRider, 10, 4)) == "10 Food (colony rider) to P1, arrives in 4 turns", "food rider text");
        ok &= Check(HumanHoverText.Order(TypedOrder(GameAI.GameAIOrder.OrderType.OrderTypeShipDeparture, 2, 0)) == null, "an order type that draws no line has no text");

        var own = ShipOrder(1, 2, 0);
        var rival = ShipOrder(1, 2, 1, "P2", "P3");
        ok &= Check(HumanHoverText.CanSee(own, 0, name => false), "the human's own order is always visible");
        ok &= Check(HumanHoverText.CanSee(rival, 0, name => true), "a rival's order with both ends visible shows");
        ok &= Check(!HumanHoverText.CanSee(rival, 0, name => name == "P2"), "a rival's order with one end hidden does not show");
        ok &= Check(!HumanHoverText.CanSee(rival, 0, name => false), "a rival's order with both ends hidden does not show");

        ok &= Check(HumanHoverText.RefusalSentence("OutOfRange").Length > 0 && HumanHoverText.RefusalSentence("OutOfRange") != "OutOfRange",
            "a known refusal reason has a sentence");
        ok &= Check(HumanHoverText.RefusalSentence("SomethingNew") == "SomethingNew", "an unknown reason falls back to its own text");
        return ok;
    }

    public static bool RunLinePickChecks()
    {
        var ok = true;
        var lines = new List<(string, IReadOnlyList<Vector2>)>
        {
            ("near", new List<Vector2> { new Vector2(0, 0), new Vector2(100, 0) }),
            ("far", new List<Vector2> { new Vector2(0, 30), new Vector2(100, 30) }),
            ("bent", new List<Vector2> { new Vector2(200, 0), new Vector2(200, 50), new Vector2(300, 50) }),
            ("dot", new List<Vector2> { new Vector2(50, 4), new Vector2(50, 4) }),
            ("empty", new List<Vector2>()),
        };
        var picked = HumanHoverText.Pick(lines, new Vector2(50, 2), 10f);
        ok &= Check(picked.SequenceEqual(new[] { "near", "dot" }), "only lines within tolerance are picked, nearest first (near 2px, dot 2px: input order breaks the tie)");
        ok &= Check(HumanHoverText.Pick(lines, new Vector2(50, 20), 10f).SequenceEqual(new[] { "far" }), "a pointer 10 px from one line and 20 from another picks the closer in tolerance");
        ok &= Check(HumanHoverText.Pick(lines, new Vector2(250, 55), 10f).SequenceEqual(new[] { "bent" }), "a later segment of a bent line is tested");
        ok &= Check(HumanHoverText.Pick(lines, new Vector2(500, 500), 10f).Count == 0, "nothing in tolerance picks nothing");
        return ok;
    }

    public static bool RunFleetListChecks()
    {
        var ok = true;
        using (var f = new HumanModeSelfCheck.Fixture(3))
        {
            f.P(2).DockShipFromSave(Ship.ShipKind.WarShip, 0, new List<string>(), 0f, 0);
            f.P(0).DockShipFromSave(Ship.ShipKind.WarShip, 0, new List<string>(), 0f, 0);
            f.P(0).DockShipFromSave(Ship.ShipKind.WarShip, 0, new List<string>(), 0f, 0);
            f.P(0).DockShipFromSave(Ship.ShipKind.SiegeShip, 0, new List<string>(), 0f, 1);
            f.P(1).DockShipFromSave(Ship.ShipKind.WarShip, 1, new List<string>());   // a rival's ship: never listed
            var orders = new List<GameAI.GameAIOrder> { ShipOrder(2, 3, 0, "P1", "P2"), ShipOrder(4, 1, 1, "P1", "P0") };
            var rows = HumanFleetList.Build(new[] { f.P(0), f.P(1), f.P(2) }, orders, 0);
            ok &= Check(rows.Count == 4, "three docked fleets and one fleet in flight make four rows, the rival's none");
            ok &= Check(rows[0].PlanetToShow == "P0" && !rows[0].InFlight && rows[0].Detail == "2 WarShip", "first row: the 2-warship fleet at P0");
            ok &= Check(rows[1].PlanetToShow == "P0" && rows[1].Detail == "1 SiegeShip", "second row: the siegeship fleet at P0");
            ok &= Check(rows[2].PlanetToShow == "P2" && rows[2].Detail == "1 WarShip", "third row: the fleet at P2");
            ok &= Check(rows[3].InFlight && rows[3].PlanetToShow == "P2" && rows[3].TurnsLeft == 3 && rows[3].Where == "P1 -> P2" && rows[3].Detail == "2 WarShip",
                "the last row is the flight, showing its destination and turns left");
        }
        return ok;
    }

    #endregion

    #region Stage 3: Planets screen

    public static bool RunSightMemoryChecks()
    {
        var ok = true;
        using (var f = new HumanModeSelfCheck.Fixture(3))
        {
            var sights = new PlanetSightMemory();
            ok &= Check(!sights.TryGet("P1", out _), "NeverSeenHasNoEntry: nothing is remembered before the first update");

            f.Own(1, 2, 2);
            sights.Update(f.Map.PlanetList, name => name == "P1", 3);
            ok &= Check(sights.TryGet("P1", out var first) && first.Owner == 2 && first.Turn == 3, "P1 in sight at turn 3 is remembered with owner 2");
            ok &= Check(!sights.TryGet("P0", out _), "a planet out of sight is not remembered");

            f.Own(1, 5, 1);   // changes hands while the human cannot see it (more people of player 1)
            sights.Update(f.Map.PlanetList, name => false, 9);
            ok &= Check(sights.TryGet("P1", out var stale) && stale.Owner == 2 && stale.Turn == 3, "StaleOwnerKeptWhileUnseen: owner 2 and turn 3 stay");
            sights.Update(f.Map.PlanetList, name => name == "P1", 10);
            ok &= Check(sights.TryGet("P1", out var fresh) && fresh.Owner == 1 && fresh.Turn == 10, "seen again at turn 10: the new owner and turn replace it");

            var copy = new PlanetSightMemory();
            copy.Restore(sights.Entries());
            ok &= Check(copy.TryGet("P1", out var again) && again.Owner == 1 && again.Turn == 10, "RestoreRoundTrip: a restored copy answers the same");
            copy.Restore(new List<(string, int, int)>());
            ok &= Check(!copy.TryGet("P1", out _), "EmptyRestore: restoring nothing leaves nothing remembered");
            copy.Restore(null);
            ok &= Check(!copy.TryGet("P1", out _), "EmptyRestore: restoring null (an older save) is the same as nothing");
        }
        return ok;
    }

    public static bool RunBlockadeRelevanceChecks()
    {
        var ok = true;
        using (var f = new HumanModeSelfCheck.Fixture(3))
        {
            f.Own(0, 3);
            f.Own(1, 2, 1);   // a rival's planet
            var all = new[] { "P0", "P1", "P2" };
            var noCuts = new string[0];
            ok &= Check(HumanTurnEvents.RelevantBlockades(f.Map, 0, all, noCuts).SequenceEqual(new[] { "P0" }),
                "RelevantBlockades: only my own planet counts when none of my orders were cut; the rival's garrisoned planet and an empty one do not");
            ok &= Check(HumanTurnEvents.RelevantBlockades(f.Map, 0, all, new[] { "P2" }).SequenceEqual(new[] { "P0", "P2" }),
                "a planet where my order was cut counts too, whoever owns it");
            ok &= Check(HumanTurnEvents.RelevantBlockades(f.Map, 0, all, new[] { "P1" }).SequenceEqual(new[] { "P0", "P1" }),
                "a rival's planet where my order was cut counts");
            ok &= Check(HumanTurnEvents.RelevantBlockades(f.Map, 0, null, noCuts).Count == 0 && HumanTurnEvents.RelevantBlockades(f.Map, 0, all, null).SequenceEqual(new[] { "P0" }),
                "null inputs: no blockades, and no cuts means only my planets");
        }
        return ok;
    }

    public static bool RunPlanetTableChecks()
    {
        var ok = true;
        using (var f = new HumanModeSelfCheck.Fixture(3))
        {
            f.Own(0, 3);
            f.Own(1, 2, 1);   // a rival's planet
            var warship = HumanModeSelfCheck.MakeItem("W", "Ship", "Warship");
            warship.cost = 10f;
            f.P(0).IndustryProduced = 3f;
            f.P(0).ProductionQueue.Add(new Planet.ProductionItem(warship));
            var sights = new PlanetSightMemory();
            sights.Update(f.Map.PlanetList, name => name == "P1", 3);
            var none = BlockadeView.Of();
            System.Func<string, bool> sees = name => name == "P0";
            System.Func<string, bool> seen = name => true;   // the fog shows every planet of the fixture

            var rows = HumanPlanetTable.Build(f.Map, f.Human, 0, 10, sees, sees, seen, sights, none, p => false);
            var byName = rows.ToDictionary(r => r.Planet);
            ok &= Check(rows.Count == 3, "all three known planets are rows");
            ok &= Check(byName["P0"].IsMine && byName["P0"].OwnerText == "You" && byName["P0"].Pop == 3, "my planet: owner You, population shown");
            ok &= Check(byName["P0"].BuildItem == "W" && byName["P0"].BuildTurns == 4, "building shows the item and ceil(10 / 3) = 4 turns");
            ok &= Check(byName["P1"].OwnerText == "Player 1 (7 turns ago)" && !byName["P1"].Pop.HasValue && !byName["P1"].Food.HasValue
                        && byName["P1"].BuildItem == "" && !byName["P1"].Ships.HasValue && byName["P1"].Alerts == "",
                "StaleOwnerText: a rival planet out of sight shows its last owner and age, every figure blank");
            ok &= Check(byName["P1"].Type != "" && byName["P1"].MaxPop > 0, "type and max population are shown out of sight");
            ok &= Check(byName["P2"].OwnerText == "" && !byName["P2"].Pop.HasValue && byName["P2"].BuildItem == "" && byName["P2"].Alerts == "",
                "NeverSeenPlanetIsBlank: a known planet never seen has no owner and no figures");
            f.P(1).DockShipFromSave(Ship.ShipKind.WarShip, 1, new List<string>());   // a rival ship on the planet next to mine
            var adjacent = HumanPlanetTable.Build(f.Map, f.Human, 0, 10, sees, name => name == "P1", seen, sights, none, p => false).Find(r => r.Planet == "P1");
            ok &= Check(adjacent.OwnerText == "Player 1" && !adjacent.Pop.HasValue && adjacent.BuildItem == "" && adjacent.Ships == 1 && adjacent.ShipOwners == "P1 1",
                "AdjacentOwnerVisible: a planet next to mine shows its current owner, its ship count and who owns them, no age, and still no other figures");
            ok &= Check(HumanPlanetTable.OwnerText(0, 1, false, true, 1) == "Player 1 (1 turn ago)", "one turn ago is singular");
            ok &= Check(HumanPlanetTable.OwnerText(0, 1, true, true, 5) == "Player 1", "in sight: no age");
            ok &= Check(HumanPlanetTable.OwnerText(0, Planet.NoOwner, false, true, 5) == "Unowned (5 turns ago)" && HumanPlanetTable.OwnerText(0, Planet.NoOwner, true, true, 0) == "" && HumanPlanetTable.OwnerText(0, Planet.NoOwner, false, false, 0) == "",
                "UnownedAge: an unowned planet seen earlier and now out of sight shows its age; in sight or never seen it is blank");

            ok &= Check(HumanPlanetTable.Filter(rows, PlanetFilter.Mine).Select(r => r.Planet).SequenceEqual(new[] { "P0" }), "FilterMine");
            ok &= Check(HumanPlanetTable.Count(rows, PlanetFilter.AllKnown) == 3, "FilterAllKnownOnlyKnown: three rows");

            // Colonization targets follow the AI's own target rule and need no colony ship.
            var expected = f.Map.PlanetList.Where(p => f.Human.IsValidColonizationTarget(p) && !p.Population.Exists(q => q.Player != 0))
                .Select(p => p.PlanetName).OrderBy(n => n).ToList();
            ok &= Check(expected.Contains("P2") && !expected.Contains("P1"), "the empty planet P2 is a target, the rival's P1 is not (fixture sanity)");
            ok &= Check(HumanPlanetTable.Filter(rows, PlanetFilter.ColonizationTargets).Select(r => r.Planet).OrderBy(n => n).SequenceEqual(expected),
                "FilterTargetsFollowTheAiRule: no colony ship needed");

            // The human's own planets are listed even before the knowledge has been updated (the first turn).
            var unseen = HumanPlanetTable.Build(f.Map, f.Human, 0, 1, sees, sees, name => false, sights, none, p => false);
            ok &= Check(unseen.Select(r => r.Planet).SequenceEqual(new[] { "P0" }), "OwnedPlanetListedBeforeSeen: only the owned planet, none of the planets not seen yet");
            var targetsUnseen = HumanPlanetTable.Build(f.Map, f.Human, 0, 1, sees, sees, name => name != "P1", sights, none, p => false);
            ok &= Check(!targetsUnseen.Exists(r => r.Planet == "P1") && targetsUnseen.Exists(r => r.Planet == "P2" && r.IsTarget),
                "a planet not seen is not listed, and a seen empty one is a target without the AI's knowledge having been updated");

            // Hostile activity.
            ok &= Check(HumanPlanetTable.Count(rows, PlanetFilter.Hostile) == 0, "nothing hostile yet");
            f.P(0).ConversionBy = 1;
            var own = HumanPlanetTable.Build(f.Map, f.Human, 0, 10, sees, sees, seen, sights, BlockadeView.Of("P0"), p => false);
            ok &= Check(own.Find(r => r.Planet == "P0").IsHostile && own.Find(r => r.Planet == "P0").Alerts.Contains("Blockaded")
                        && own.Find(r => r.Planet == "P0").Alerts.Contains("Converting"), "FilterHostileOwnPlanet: blockaded and converting");
            f.P(0).ConversionBy = Planet.NoOwner;
            var rival = HumanPlanetTable.Build(f.Map, f.Human, 0, 10, sees, sees, seen, sights, none, p => p.PlanetName == "P1");
            ok &= Check(rival.Find(r => r.Planet == "P1").IsHostile, "FilterHostileRivalPlanet: the human blockades it, out of sight");
            f.P(2).ConversionBy = 0;
            var conv = HumanPlanetTable.Build(f.Map, f.Human, 0, 10, sees, sees, seen, sights, none, p => false);
            ok &= Check(conv.Find(r => r.Planet == "P2").IsHostile, "a rival planet the human converts is hostile");
            f.P(2).ConversionBy = Planet.NoOwner;
            f.P(1).DockShipFromSave(Ship.ShipKind.WarShip, 0, new List<string>());
            var docked = HumanPlanetTable.Build(f.Map, f.Human, 0, 10, sees, sees, seen, sights, none, p => false);
            ok &= Check(!docked.Find(r => r.Planet == "P1").IsHostile, "DockedOnlyStaysOut: ships docked at a rival planet are not hostile activity");
        }

        var a = new PlanetRow { Planet = "A", Type = "T", OwnerText = "Player 2", Pop = 5, Food = 1f, BuildItem = "Zed", Ships = 2, Alerts = "" };
        var b = new PlanetRow { Planet = "B", Type = "T", OwnerText = "", Pop = 9, Food = null, BuildItem = "Alpha", Ships = null, Alerts = "Blockaded" };
        var c = new PlanetRow { Planet = "C", Type = "T", OwnerText = "Player 10", Pop = 5, Food = 3f, BuildItem = "", Ships = 2, Alerts = "" };
        var table = new List<PlanetRow> { c, b, a };

        var s = new SortState();
        s = HumanPlanetTable.Click(s, PlanetColumn.Pop);
        ok &= Check(s.Active && s.Descending, "ClickCycle: the first click sorts descending");
        s = HumanPlanetTable.Click(s, PlanetColumn.Pop);
        ok &= Check(!s.Descending, "the second click sorts ascending");
        s = HumanPlanetTable.Click(s, PlanetColumn.Pop);
        ok &= Check(s.Descending, "the third click descends again");
        s = HumanPlanetTable.Click(s, PlanetColumn.Food);
        ok &= Check(s.Column == PlanetColumn.Food && s.Descending, "another heading starts at descending");

        var byPop = HumanPlanetTable.Sort(table, new SortState { Column = PlanetColumn.Pop, Descending = true, Active = true }).Select(r => r.Planet);
        ok &= Check(byPop.SequenceEqual(new[] { "B", "A", "C" }), "SortNumericAndText: numeric descending, the 5-5 tie by planet name");
        var byPopAsc = HumanPlanetTable.Sort(table, new SortState { Column = PlanetColumn.Pop, Descending = false, Active = true }).Select(r => r.Planet);
        ok &= Check(byPopAsc.SequenceEqual(new[] { "A", "C", "B" }), "ascending: the tie is still by planet name");
        var byFoodDesc = HumanPlanetTable.Sort(table, new SortState { Column = PlanetColumn.Food, Descending = true, Active = true }).Select(r => r.Planet);
        var byFoodAsc = HumanPlanetTable.Sort(table, new SortState { Column = PlanetColumn.Food, Descending = false, Active = true }).Select(r => r.Planet);
        ok &= Check(byFoodDesc.SequenceEqual(new[] { "C", "A", "B" }) && byFoodAsc.SequenceEqual(new[] { "A", "C", "B" }), "BlanksLastBothDirections");
        var byBuild = HumanPlanetTable.Sort(table, new SortState { Column = PlanetColumn.Building, Descending = false, Active = true }).Select(r => r.Planet);
        ok &= Check(byBuild.SequenceEqual(new[] { "B", "A", "C" }), "BuildingSortsByItemName: Alpha, Zed, then the blank");
        var allBlank = HumanPlanetTable.Sort(table, new SortState { Column = PlanetColumn.Alerts, Descending = true, Active = true }).Select(r => r.Planet);
        ok &= Check(allBlank.SequenceEqual(new[] { "B", "A", "C" }), "BlanksAndTiesSortStable: the blanks come in planet-name order");
        ok &= Check(HumanPlanetTable.Search(table, "b").Count == 1 && HumanPlanetTable.Search(table, "").Count == 3 && HumanPlanetTable.Search(table, "zz").Count == 0,
            "SearchNarrowsActiveFilter: case-insensitive contains, empty text returns all");
        return ok;
    }

    #endregion

    private static bool Check(bool condition, string label)
    {
        _checkCount++;
        if (!condition) Debug.LogError($"[HumanUiSelfCheck] FAIL: {label}");
        return condition;
    }

    #region Task 8: research tree

    private static ResearchNode FindNode(IEnumerable<ResearchNode> roots, string name)
    {
        foreach (var node in HumanResearchTree.Flatten(roots))
            if (node.Item.itemName == name) return node;
        return null;
    }

    public static bool RunResearchTreeChecks()
    {
        var ok = true;
        var a = HumanModeSelfCheck.MakeResearch("A");
        var b = HumanModeSelfCheck.MakeResearch("B", "A");
        var c = HumanModeSelfCheck.MakeResearch("C", "B");
        var d = HumanModeSelfCheck.MakeResearch("D");
        var e = HumanModeSelfCheck.MakeResearch("E", "Z");   // its prerequisite is not in the catalog
        var f = HumanModeSelfCheck.MakeResearch("F", "F");   // itself
        var g = HumanModeSelfCheck.MakeResearch("G", "H");   // a two-item cycle
        var h = HumanModeSelfCheck.MakeResearch("H", "G");
        var catalog = new List<CatalogItem> { c, h, a, e, g, b, d, f };   // deliberately not in order
        var none = new string[0];

        var roots = HumanResearchTree.Build(catalog, none, null);
        ok &= Check(roots.Select(r => r.Item.itemName).SequenceEqual(new[] { "A", "D", "E", "F", "G", "H" }),
            "the roots are the items with no prerequisite or one the tree cannot use, in name order");
        ok &= Check(FindNode(roots, "B").Parent == FindNode(roots, "A") && FindNode(roots, "C").Depth == 2, "B hangs under A and C is two levels down");
        ok &= Check(FindNode(roots, "A").State == ResearchNodeState.Available && FindNode(roots, "B").State == ResearchNodeState.Locked, "A can be researched, B waits for A");
        ok &= Check(FindNode(roots, "E").State == ResearchNodeState.Locked && FindNode(roots, "F").State == ResearchNodeState.Locked
                    && FindNode(roots, "G").State == ResearchNodeState.Locked && FindNode(roots, "H").State == ResearchNodeState.Locked,
            "an unknown or cyclic prerequisite leaves the item locked, and the tree still builds");

        a.researched = true;
        var after = HumanResearchTree.Build(catalog, new[] { "B" }, d);
        ok &= Check(FindNode(after, "A").State == ResearchNodeState.Researched, "a researched item is Researched");
        ok &= Check(FindNode(after, "B").State == ResearchNodeState.Queued && FindNode(after, "B").QueuePosition == 1, "a queued item is Queued with its position");
        ok &= Check(FindNode(after, "C").State == ResearchNodeState.Locked, "the item after a queued one is still Locked");
        ok &= Check(FindNode(after, "D").State == ResearchNodeState.Current, "the item in progress is Current");

        a.researched = false;
        ok &= Check(HumanResearchTree.ChainFor(catalog, none, null, "C").SequenceEqual(new[] { "A", "B", "C" }), "the chain to C with nothing done is A, B, C in order");
        a.researched = true;
        ok &= Check(HumanResearchTree.ChainFor(catalog, none, null, "C").SequenceEqual(new[] { "B", "C" }), "a researched prerequisite is not in the chain");
        ok &= Check(HumanResearchTree.ChainFor(catalog, new[] { "B" }, null, "C").SequenceEqual(new[] { "C" }), "a queued prerequisite is not in the chain");
        ok &= Check(HumanResearchTree.ChainFor(catalog, none, b, "C").SequenceEqual(new[] { "C" }), "the research in progress counts as a prerequisite that is on its way");
        ok &= Check(HumanResearchTree.ChainFor(catalog, none, null, "Nope").Count == 0, "an unknown item has no chain");
        ok &= Check(HumanResearchTree.ChainFor(catalog, none, null, "A").Count == 0, "a researched item has no chain");
        ok &= Check(HumanResearchTree.ChainFor(catalog, none, null, "F").Count == 0 && HumanResearchTree.ChainFor(catalog, none, null, "G").Count == 0,
            "a cyclic item has no chain");
        ok &= Check(HumanResearchTree.ChainFor(catalog, none, null, "E").SequenceEqual(new[] { "E" }), "an item whose prerequisite is not in the catalog is just itself");

        // Estimates: the item in progress, then the queue, cumulatively.
        var current = HumanModeSelfCheck.MakeResearch("Cur", cost: 30f);
        var queued = HumanModeSelfCheck.MakeResearch("Q", cost: 20f);
        var turns = HumanResearchTree.Estimates(current, 10f, new List<CatalogItem> { queued }, 5f);
        ok &= Check(turns.Count == 2 && turns[0] == 4 && turns[1] == 8, "30 cost with 10 banked at 5 a turn is 4 turns; the queued 20 adds 4 more");
        var stalled = HumanResearchTree.Estimates(current, 10f, new List<CatalogItem> { queued }, 0f);
        ok &= Check(stalled.Count == 2 && stalled.All(t => t == null), "with no research per turn every estimate is null");
        var idle = HumanResearchTree.Estimates(null, 15f, new List<CatalogItem> { queued }, 5f);
        ok &= Check(idle.Count == 1 && idle[0] == 1, "when idle, banked points count toward the first queued item (20 - 15 = 5 at 5 a turn = 1 turn)");
        ok &= Check(HumanResearchTree.Estimates(null, 0f, new List<CatalogItem>(), 5f).Count == 0, "nothing researching and nothing queued has no estimates");

        using (var fx = new HumanModeSelfCheck.Fixture(3))
        {
            fx.Own(0, 2);
            fx.Own(1, 1);
            fx.Own(2, 1, player: 1);
            fx.P(0).ResearchProduced = 3f;
            fx.P(1).ResearchProduced = 4.5f;
            fx.P(2).ResearchProduced = 9f;
            ok &= Check(HumanResearchTree.ResearchPerTurn(fx.Map, 0) == 7.5f, "research per turn is the sum over the human's planets, not a rival's");
        }
        return ok;
    }

    #endregion

    #region Tasks 6 and 7: planet view helpers and colonize options

    public static bool RunPlanetViewChecks()
    {
        var ok = true;
        using (var f = new HumanModeSelfCheck.Fixture(3))
        {
            f.Own(0, 2);
            f.Own(1, 1, player: 1);
            f.P(0).Food = 120f;
            var net = HumanPlanetView.NetPerTurn(f.P(0));   // projects the production itself, so it needs no turn to have run
            ok &= Check(net.food == f.P(0).FoodProduced - 2f, "food net is the food produced minus one per inhabitant (two here)");
            ok &= Check(net.grotsits == f.P(0).GrotsitsProduced - f.P(0).GetMaintenanceCost(), "grotsits net is the grotsits produced minus the planet's maintenance cost");
            f.P(0).Food = 0f;
            ok &= Check(HumanPlanetView.NetPerTurn(f.P(0)).food == net.food, "the net rate does not depend on the stock (a full store does not read as a loss)");
            ok &= Check(HumanPlanetView.StockCovers(10f, -3f) && HumanPlanetView.StockCovers(3f, -3f) && !HumanPlanetView.StockCovers(2f, -3f),
                "a stock covers a loss when it holds at least a turn's worth of it");
            ok &= Check(HumanPlanetView.StockCovers(0f, 1f) && HumanPlanetView.StockCovers(0f, 0f), "with no loss an empty stock still covers it");
            ok &= Check(HumanPlanetView.OwnsPlanet(f.P(0), 0) && !HumanPlanetView.OwnsPlanet(f.P(1), 0) && !HumanPlanetView.OwnsPlanet(null, 0),
                "OwnsPlanet is true only for the planet the human owns");
            ok &= Check(HumanPlanetView.CanOrder(f.P(0), 0), "the human can order on a planet they populate");
            ok &= Check(!HumanPlanetView.CanOrder(f.P(1), 0), "a rival planet: the human cannot order");
            ok &= Check(!HumanPlanetView.CanOrder(f.P(2), 0), "an unpopulated planet: the human cannot order");
            HumanModeSelfCheck.Dock(f.P(2), Ship.ShipKind.WarShip, 0, 1);
            ok &= Check(!HumanPlanetView.CanOrder(f.P(2), 0), "a docked ship alone does not give order rights over a planet (production, shipping and colonizing need people there)");
            ok &= Check(!HumanPlanetView.CanOrder(null, 0), "no planet: no orders");
            f.Own(1, 2, player: 0);   // 1 rival + 2 of the human's: the human now holds the plurality of P1
            f.P(1).ChangePopulation(2, 1);   // and the rival takes it back, 3 against 2
            ok &= Check(HumanPlanetView.CanOrder(f.P(1), 0) && !HumanPlanetView.OwnsPlanet(f.P(1), 0),
                "people on a planet the rival owns: the human can ship and colonize from it but does not own it");
        }

        using (var f = new HumanModeSelfCheck.Fixture(5, c => { c.maxPathNodesForColonization = 3; c.defaultTravelSpeed = 100f; }))
        {
            f.Own(0, 3);
            HumanModeSelfCheck.Dock(f.P(0), Ship.ShipKind.ColonyShip, 0, 1);
            var options = HumanPlanetView.ColonizeOptions(f.Map, f.Human, f.P(0), 3);
            ok &= Check(options.Count == 2 && options[0].Target.PlanetName == "P1" && options[1].Target.PlanetName == "P2",
                "targets in range (P1, P2; P3 and P4 are beyond three nodes), nearest first");
            ok &= Check(options[0].Turns >= 1 && options[1].Turns >= options[0].Turns, "turns are at least one and grow with distance");
            ok &= Check(HumanPlanetView.CanColonizeFrom(f.P(0), 0), "a docked colony ship and three inhabitants can colonize");

            f.Own(2, 5, player: 1);
            ok &= Check(HumanPlanetView.ColonizeOptions(f.Map, f.Human, f.P(0), 3).Select(o => o.Target.PlanetName).SequenceEqual(new[] { "P1" }), "a full rival planet is not a target");
            ok &= Check(HumanPlanetView.ColonizeOptions(f.Map, f.Human, f.P(0), 3, name => false).Count == 0, "planets the human has not seen are not offered");
            f.Map.Knowledge.SetKnownPlanets(0, new[] { "P0" });
            ok &= Check(HumanPlanetView.ColonizeOptions(f.Map, f.Human, f.P(0), 3).Count == 0, "planets the human does not know are not targets");
        }

        using (var f = new HumanModeSelfCheck.Fixture(3))
        {
            f.Own(0, 1);
            HumanModeSelfCheck.Dock(f.P(0), Ship.ShipKind.ColonyShip, 0, 1);
            ok &= Check(!HumanPlanetView.CanColonizeFrom(f.P(0), 0) && HumanPlanetView.ColonizeOptions(f.Map, f.Human, f.P(0), 3).Count == 0,
                "with a single inhabitant nothing can be colonized from here");
            f.Own(0, 2);
            f.P(0).UndockShip(Ship.ShipKind.ColonyShip);
            ok &= Check(!HumanPlanetView.CanColonizeFrom(f.P(0), 0) && HumanPlanetView.ColonizeOptions(f.Map, f.Human, f.P(0), 3).Count == 0,
                "with no colony ship nothing can be colonized from here");
        }
        return ok;
    }

    #endregion

    #region Task 5: build list

    public static bool RunBuildListChecks()
    {
        var ok = true;
        using (var f = new HumanModeSelfCheck.Fixture(2))
        {
            f.Own(0, 3);
            var planet = f.P(0);
            planet.IndustryProduced = 3f;
            var built = HumanModeSelfCheck.MakeItem("BuiltFood1", "Improvement", "Food", 1);
            var next = HumanModeSelfCheck.MakeItem("NextFood2", "Improvement", "Food", 2);
            next.cost = 20f;
            var lowerTier = HumanModeSelfCheck.MakeItem("LowFood0", "Improvement", "Food", 0);
            var locked = HumanModeSelfCheck.MakeItem("LockedInd1", "Improvement", "Industry", 1, researched: false);
            var warship = HumanModeSelfCheck.MakeItem("W", "Ship", "Warship");
            warship.cost = 10f;
            var siege = HumanModeSelfCheck.MakeItem("S", "Ship", "Siegeship");
            var bunker = HumanModeSelfCheck.MakeItem("B", "Ship", "Bunker");
            f.Human.ProductionCatalog.catalogItems.AddRange(new[] { built, next, lowerTier, locked, warship, siege, bunker });
            planet.RecordImprovement(built);

            var options = HumanBuildList.Options(planet, f.Human);
            var names = options.Select(o => o.Item.itemName).ToList();
            ok &= Check(!names.Contains("BuiltFood1") && !names.Contains("LowFood0") && !names.Contains("LockedInd1"),
                "an improvement already built, a tier below the best built and an unresearched item are not offered");
            ok &= Check(names.Contains("NextFood2") && names.Contains("W") && names.Contains("S") && names.Contains("B"), "the next tier, a warship, a siegeship and a bunker are offered");

            var food2 = options.Find(o => o.Item.itemName == "NextFood2");
            ok &= Check(food2.Group == "Improvements" && food2.Cost == 20f && food2.Turns == 7, "an improvement shows its group, cost and ceil(cost / industry) turns (20 / 3 = 7)");
            ok &= Check(options.Find(o => o.Item.itemName == "W").Group == "Ships" && options.Find(o => o.Item.itemName == "B").Group == "Defenses", "ships and bunkers are grouped");
            ok &= Check(options.Find(o => o.Item.itemName == "W").Cost == 10f, "a warship costs its base cost with no ship research");
            ok &= Check(options.Find(o => o.Item.itemName == "S").Cost == 15f, "a siegeship costs the warship base x the siegeship ratio (10 x 1.5)");
            ok &= Check(options.First().Group == "Improvements" && options.Last().Group == "Defenses", "groups are ordered improvements, ships, defenses");

            planet.ProductionQueue.Add(new Planet.ProductionItem(next));
            ok &= Check(!HumanBuildList.Options(planet, f.Human).Exists(o => o.Item.itemName == "NextFood2"), "an improvement already in the queue is not offered a second time");
            ok &= Check(HumanBuildList.Options(planet, f.Human).Exists(o => o.Item.itemName == "W"), "a ship can be queued again");
            planet.ProductionQueue.Clear();
            planet.CurrentProduction = new Planet.ProductionItem(next);
            ok &= Check(!HumanBuildList.Options(planet, f.Human).Exists(o => o.Item.itemName == "NextFood2"), "nor one that is being built");
            planet.CurrentProduction = null;

            planet.IndustryProduced = 0f;
            ok &= Check(HumanBuildList.Options(planet, f.Human).Find(o => o.Item.itemName == "NextFood2").Turns == null, "with no industry there is no turn estimate");

            for (var i = 0; i < planet.MaxPopulation; i++)
                HumanModeSelfCheck.Dock(planet, Ship.ShipKind.Bunker, HumanModeSelfCheck.Fixture.HumanId, 1);
            ok &= Check(!HumanBuildList.Options(planet, f.Human).Exists(o => o.Item.itemName == "B"), "a bunker over the population cap is not offered");
        }

        using (var f = new HumanModeSelfCheck.Fixture(2))
        {
            f.Own(0, 2);
            var planet = f.P(0);
            ok &= Check(HumanBuildList.QueueEstimates(planet).Count == 0, "an idle planet with an empty queue has no estimates");
            var ten = HumanModeSelfCheck.MakeItem("Ten", "Ship", "Warship");
            ten.cost = 10f;
            var nine = HumanModeSelfCheck.MakeItem("Nine", "Ship", "Warship");
            nine.cost = 9f;
            planet.IndustryProduced = 3f;
            planet.CurrentProduction = new Planet.ProductionItem(ten) { Progress = 4f };
            planet.ProductionQueue.Add(new Planet.ProductionItem(nine));
            var estimates = HumanBuildList.QueueEstimates(planet);
            ok &= Check(estimates.Count == 2 && estimates[0] == 2 && estimates[1] == 5, "the item in progress needs ceil(6 / 3) = 2 turns and the queued one finishes cumulatively at 2 + 3 = 5");
            planet.IndustryProduced = 0f;
            var stalled = HumanBuildList.QueueEstimates(planet);
            ok &= Check(stalled.Count == 2 && stalled.All(e => e == null), "without industry every estimate is null");
        }
        return ok;
    }

    #endregion

    #region Task 4: shipment sources

    public static bool RunShipmentListChecks()
    {
        var ok = true;
        using (var f = new HumanModeSelfCheck.Fixture(4))
        {
            f.Own(0, 2);
            f.Own(1, 1);
            f.Own(3, 1);
            f.Own(2, 1, player: 1);
            f.P(0).Food = 120f;
            f.P(1).Food = 40f;
            f.P(3).Grotsits = 30f;
            var results = new List<Planet.UpdateResult>
            {
                new Planet.UpdateResult("P0", Planet.UpdateResult.UpdateResultType.UpdateResultTypeFoodSurplus, 34f, 0),
                new Planet.UpdateResult("P1", Planet.UpdateResult.UpdateResultType.UpdateResultTypeFoodSurplus, 11f, 0),
                new Planet.UpdateResult("P2", Planet.UpdateResult.UpdateResultType.UpdateResultTypeFoodSurplus, 5f, 1),   // the rival's
                new Planet.UpdateResult("P3", Planet.UpdateResult.UpdateResultType.UpdateResultTypeGrotsitsSurplus, 9f, 0),
            };
            System.Func<Planet, float> whole = p => 1f;

            var food = HumanShipmentList.Build(f.Map, 0, true, "P2", results, whole);
            ok &= Check(food.Count == 2 && food[0].Planet == "P1" && food[1].Planet == "P0", "food sources to P2: P1 (1 hop) before P0 (2 hops); not the rival's planet, not the destination");
            ok &= Check(food[0].Surplus == 11f && food[0].Stock == 40f && food[0].Hops == 1 && food[1].Surplus == 34f && food[1].Stock == 120f && food[1].Hops == 2,
                "each source shows its surplus, its raw stock and its hops");

            var half = HumanShipmentList.Build(f.Map, 0, true, "P2", results, p => 0.5f);
            ok &= Check(half[0].Surplus == 5.5f && half[1].Surplus == 17f, "the surplus is scaled by the population fraction");
            var none = HumanShipmentList.Build(f.Map, 0, true, "P2", results, p => p.PlanetName == "P0" ? 0f : 1f);
            ok &= Check(none.Count == 1 && none[0].Planet == "P1", "a surplus scaled to nothing is not offered");

            var sent = new Dictionary<string, float> { { "P0", 30f }, { "P1", 11f } };
            var afterSending = HumanShipmentList.Build(f.Map, 0, true, "P2", results, whole, sent);
            ok &= Check(afterSending.Count == 1 && afterSending[0].Planet == "P0" && afterSending[0].Surplus == 4f,
                "what was already shipped this turn comes off the surplus; a planet with nothing left drops out");

            f.Constants.maxPathNodesForResourceDistribution = 2;
            var near = HumanShipmentList.Build(f.Map, 0, true, "P2", results, whole);
            ok &= Check(near.Count == 1 && near[0].Planet == "P1", "a source beyond the resource-distribution node limit is not offered");
            f.Constants.maxPathNodesForResourceDistribution = 10;

            var grotsits = HumanShipmentList.Build(f.Map, 0, false, "P2", results, whole);
            ok &= Check(grotsits.Count == 1 && grotsits[0].Planet == "P3" && grotsits[0].Stock == 30f, "grotsits lists only the grotsits sources, with the grotsits stock");
            ok &= Check(HumanShipmentList.Build(f.Map, 0, true, "P2", new List<Planet.UpdateResult>(), whole).Count == 0, "a planet with no surplus result is absent");
            ok &= Check(HumanShipmentList.Build(f.Map, 0, true, "Nowhere", results, whole).Count == 0, "an unknown destination gives an empty list");
        }
        return ok;
    }

    #endregion

    #region Contact, colony ships and idle research

    private static HumanTurnInput TurnInput(System.Func<string, bool> visible, List<Planet.UpdateResult> results = null, List<GameAI.GameAIOrder> executed = null)
        => new HumanTurnInput
        {
            Results = results ?? new List<Planet.UpdateResult>(), Executed = executed ?? new List<GameAI.GameAIOrder>(),
            NewOrders = new List<GameAI.GameAIOrder>(), Cuts = new List<BlockadeSystem.BlockadeCut>(), Conversions = new List<ConversionReport>(),
            BlockadedPlanets = new string[0], IsVisible = visible,
        };

    public static bool RunContactChecks()
    {
        var ok = true;
        using (var f = new HumanModeSelfCheck.Fixture(4))
        {
            f.Own(0, 2);
            f.Human.currentResearch = HumanModeSelfCheck.MakeResearch("Busy");   // so research idle is not the news here
            HumanModeSelfCheck.Dock(f.P(3), Ship.ShipKind.WarShip, 1, 1);
            var events = new HumanTurnEvents();
            events.Collect(f.Map, f.Human, TurnInput(name => name != "P3"));   // the snapshots; the rival sits on P3, out of sight

            var war = new List<GameAI.GameAIOrder>
            {
                new GameAI.GameAIOrder { Type = GameAI.GameAIOrder.OrderType.OrderTypeDeclareWar, PlayerId = 1, Data = 0, Origin = "", Target = "" },
            };
            var unseen = events.Collect(f.Map, f.Human, TurnInput(name => name != "P3", null, war));
            ok &= Check(!unseen.Exists(e => e.Kind == HumanEventKind.FirstContact), "a player present only on a planet out of sight is not first contact");
            ok &= Check(!unseen.Exists(e => e.Kind == HumanEventKind.WarDeclared), "and a war declared by a player the human has not seen is not reported");

            var seen = events.Collect(f.Map, f.Human, TurnInput(name => true));
            ok &= Check(seen.Exists(e => e.Kind == HumanEventKind.FirstContact && e.Player == 1 && e.Planet == "P3"),
                "once the planet is in sight the player is met, and the event names that planet (so a click can go there)");

            var after = events.Collect(f.Map, f.Human, TurnInput(name => true, null, war));
            ok &= Check(after.Exists(e => e.Kind == HumanEventKind.WarDeclared && e.Player == 1), "after meeting them, their declaration of war is reported");
            ok &= Check(HumanNotificationList.TextFor(new HumanEvent { Kind = HumanEventKind.FirstContact, Planet = "P3", Player = 1 }, 1) == "First contact with player 1 at P3",
                "the message says where");
        }

        // A colony ship completing is its own event.
        using (var f = new HumanModeSelfCheck.Fixture(2))
        {
            f.Own(0, 2);
            f.Human.currentResearch = HumanModeSelfCheck.MakeResearch("Busy");
            var colony = HumanModeSelfCheck.MakeItem("ColonyShipProduction", "Ship", "ColonyShip");
            var warship = HumanModeSelfCheck.MakeItem("WarShipProduction", "Ship", "Warship");
            f.Human.ProductionCatalog.catalogItems.AddRange(new[] { colony, warship });
            var events = new HumanTurnEvents();
            events.Collect(f.Map, f.Human, TurnInput(null));
            var built = events.Collect(f.Map, f.Human, TurnInput(null, new List<Planet.UpdateResult>
            {
                new Planet.UpdateResult("P0", Planet.UpdateResult.UpdateResultType.UpdateResultTypeIndustryProductionComplete, "ColonyShipProduction", 0),
            }));
            ok &= Check(built.Exists(e => e.Kind == HumanEventKind.ColonyShipBuilt && e.Planet == "P0") && built.Exists(e => e.Kind == HumanEventKind.ProductionComplete),
                "a finished colony ship is a ColonyShipBuilt event as well as a production complete");
            var other = events.Collect(f.Map, f.Human, TurnInput(null, new List<Planet.UpdateResult>
            {
                new Planet.UpdateResult("P0", Planet.UpdateResult.UpdateResultType.UpdateResultTypeIndustryProductionComplete, "WarShipProduction", 0),
            }));
            ok &= Check(!other.Exists(e => e.Kind == HumanEventKind.ColonyShipBuilt), "any other item is not");
            ok &= Check(HumanNotificationList.TextFor(new HumanEvent { Kind = HumanEventKind.ColonyShipBuilt, Planet = "P0" }, 1) == "Colony ship built: P0", "and reads as one");
        }

        // Idle research stays on the list while nothing is queued.
        using (var f = new HumanModeSelfCheck.Fixture(1))
        {
            f.Own(0, 2);
            ok &= Check(HumanNotificationList.IsStateKind(HumanEventKind.ResearchIdle), "research idle is a state kind");
            ok &= Check(HumanOngoing.IsStillTrue(f.Map, 0, new string[0], new HumanEvent[0], HumanEventKind.ResearchIdle, "", researchIdle: true)
                        && !HumanOngoing.IsStillTrue(f.Map, 0, new string[0], new HumanEvent[0], HumanEventKind.ResearchIdle, "", researchIdle: false),
                "and holds while the human has nothing to research");
            var list = new HumanNotificationList();
            var idle = new HumanEvent { Kind = HumanEventKind.ResearchIdle, Planet = "", Player = 0 };
            ok &= Check(list.Update(1, new[] { idle }, (k, p) => true).Count == 1, "going idle is news");
            list.Update(2, new HumanEvent[0], (k, p) => true);
            list.Update(3, new HumanEvent[0], (k, p) => true);
            ok &= Check(list.Entries.Count == 1 && list.Entries[0].Turns == 3, "and stays listed, counting turns, with no new event");
        }
        return ok;
    }

    #endregion

    #region Research follow-ups

    public static bool RunResearchFollowUpChecks()
    {
        var ok = true;

        // Queueing when nothing is being researched starts it at once, so the screen shows it as the research in progress.
        using (var f = new HumanModeSelfCheck.Fixture(1))
        {
            var a = HumanModeSelfCheck.MakeResearch("FA");
            var b = HumanModeSelfCheck.MakeResearch("FB", "FA");
            f.Human.ResearchCatalog.catalogItems.AddRange(new[] { a, b });
            var processor = new HumanCommandProcessor(f.Map, f.Human, 2);
            processor.Apply(new[] { HumanModeSelfCheck.QueueResearch("FA"), HumanModeSelfCheck.QueueResearch("FB") }, new List<GameAI.GameAIOrder>());
            ok &= Check(f.Human.currentResearch == null && f.Human.ResearchQueue.Count == 2, "after queueing, nothing is in progress yet (the turn would start it)");

            var orders = new List<GameAI.GameAIOrder>();
            f.Human.StartQueuedResearchIfIdle(orders);
            ok &= Check(f.Human.currentResearch == a && f.Human.ResearchQueue.SequenceEqual(new[] { "FB" }), "an idle human's first queued item becomes the research in progress at once");
            ok &= Check(orders.Exists(o => o.Type == GameAI.GameAIOrder.OrderType.OrderTypeResearchSet && o.Target == "FA"), "and the research-set order is emitted");
            f.Human.StartQueuedResearchIfIdle(orders);
            ok &= Check(f.Human.currentResearch == a && f.Human.ResearchQueue.Count == 1, "with research already in progress nothing changes");

            f.HumanPlayer.isHuman = false;
            f.Human.currentResearch = null;
            f.Human.StartQueuedResearchIfIdle(orders);
            ok &= Check(f.Human.currentResearch == null, "an AI player is never started this way");
        }

        // Removing a queued item takes out the queued items that need it.
        using (var f = new HumanModeSelfCheck.Fixture(1))
        {
            var a = HumanModeSelfCheck.MakeResearch("GA");
            var b = HumanModeSelfCheck.MakeResearch("GB", "GA");
            var c = HumanModeSelfCheck.MakeResearch("GC", "GB");
            var d = HumanModeSelfCheck.MakeResearch("GD");
            var e = HumanModeSelfCheck.MakeResearch("GE", "GD");
            f.Human.ResearchCatalog.catalogItems.AddRange(new[] { a, b, c, d, e });
            var processor = new HumanCommandProcessor(f.Map, f.Human, 2);
            var none = new List<GameAI.GameAIOrder>();
            processor.Apply(new[]
            {
                HumanModeSelfCheck.QueueResearch("GA"), HumanModeSelfCheck.QueueResearch("GD"), HumanModeSelfCheck.QueueResearch("GB"),
                HumanModeSelfCheck.QueueResearch("GC"), HumanModeSelfCheck.QueueResearch("GE"),
            }, none);
            ok &= Check(f.Human.ResearchQueue.SequenceEqual(new[] { "GA", "GD", "GB", "GC", "GE" }), "the queue holds a chain A, B, C and a chain D, E interleaved");

            var removal = processor.Apply(new[] { new HumanCommand { type = "removeResearch", index = 0 } }, none)[0];
            ok &= Check(removal.applied && f.Human.ResearchQueue.SequenceEqual(new[] { "GD", "GE" }),
                "removing A also removes B and C (which need it, one through the other) and leaves the unrelated D and E: " + string.Join(",", f.Human.ResearchQueue));
            ok &= Check(removal.detail.Contains("GB") && removal.detail.Contains("GC"), "the result names what else went");

            processor.Apply(new[] { HumanModeSelfCheck.QueueResearch("GA"), HumanModeSelfCheck.QueueResearch("GB"), HumanModeSelfCheck.QueueResearch("GC") }, none);
            processor.Apply(new[] { new HumanCommand { type = "removeResearch", index = f.Human.ResearchQueue.IndexOf("GC") } }, none);
            ok &= Check(f.Human.ResearchQueue.Contains("GA") && f.Human.ResearchQueue.Contains("GB") && !f.Human.ResearchQueue.Contains("GC"),
                "removing the last of a chain removes only it");
            processor.Apply(new[] { new HumanCommand { type = "removeResearch", index = f.Human.ResearchQueue.IndexOf("GD") } }, none);
            ok &= Check(!f.Human.ResearchQueue.Contains("GD") && !f.Human.ResearchQueue.Contains("GE"), "removing D takes E with it");
        }
        return ok;
    }

    #endregion

    #region Batch pre-check

    public static bool RunCheckBatchChecks()
    {
        var ok = true;
        using (var f = new HumanModeSelfCheck.Fixture(4))
        {
            f.Own(0, 2);
            f.P(0).Food = 50f;
            var processor = new HumanCommandProcessor(f.Map, f.Human, 3);
            var results = processor.CheckBatch(new[]
            {
                HumanModeSelfCheck.Shipment("P0", "P2", "food", 30),
                HumanModeSelfCheck.Shipment("P0", "P3", "food", 30),
                HumanModeSelfCheck.Shipment("P0", "P3", "food", 10),
            });
            ok &= Check(results.Count == 3 && results[0].applied && !results[1].applied && results[1].reason == "NotEnoughStock" && results[2].applied,
                "the batch check sees the first shipment's claim on the stock: the second 30 of a 50 stock is refused, the 10 after it fits");
            ok &= Check(f.P(0).Food == 50f, "checking a batch changes nothing");
            var again = processor.CheckBatch(new[] { HumanModeSelfCheck.Shipment("P0", "P2", "food", 30) });
            ok &= Check(again[0].applied, "and its claims do not outlive the check");
            var research = processor.CheckBatch(new[] { HumanModeSelfCheck.QueueResearch("X") });
            ok &= Check(!research[0].applied && research[0].reason == "CheckNotSupported", "a type the batch check does not cover says so");
        }
        return ok;
    }

    #endregion

    #region Task 3: notification list

    private static HumanEvent Ev(HumanEventKind kind, string planet = "", int player = 0, bool rival = false)
        => new HumanEvent { Kind = kind, Planet = planet, Player = player, Rival = rival };

    public static bool RunNotificationChecks()
    {
        var ok = true;
        System.Func<HumanEventKind, string, bool> always = (k, p) => true;
        System.Func<HumanEventKind, string, bool> never = (k, p) => false;
        var shortage = Ev(HumanEventKind.ShortageFood, "P0");

        // A shortage that lasts: one entry, a growing count, fresh only when it starts.
        var list = new HumanNotificationList();
        var fresh = list.Update(1, new[] { shortage }, always);
        ok &= Check(fresh.Count == 1 && list.Entries.Count == 1 && list.Entries[0].Turns == 1, "a new shortage is one fresh entry with a count of 1");
        for (var turn = 2; turn <= 5; turn++)
            fresh = list.Update(turn, new[] { shortage }, always);
        ok &= Check(list.Entries.Count == 1 && list.Entries[0].Turns == 5 && fresh.Count == 0, "five turns of the same shortage: still one entry, count 5, nothing fresh after the first turn");
        ok &= Check(HumanNotificationList.TextFor(shortage, 12) == "Food shortage: P0 (12 turns)", "TextFor names the kind, planet and turns");
        ok &= Check(HumanNotificationList.TextFor(shortage, 1) == "Food shortage: P0", "a first-turn entry shows no count");
        ok &= Check(HumanNotificationList.TextFor(Ev(HumanEventKind.FleetArrived, "P3", 1, rival: true), 1) == "Player 1: Fleet arrived: P3",
            "a rival's event names the player, not 'Rival'");
        ok &= Check(HumanNotificationList.TextFor(Ev(HumanEventKind.WarDeclared, "", 2), 1) == "Player 2 declared war", "a war declaration names the player");

        // The list is rebuilt every turn: nothing needs dismissing, and an ended cause is gone.
        list.Update(6, new HumanEvent[0], never);
        ok &= Check(list.Entries.Count == 0, "when the cause is no longer true the state entry disappears");

        // One-shot kinds last for the turn they are reported, and are fresh every time.
        var oneShots = new HumanNotificationList();
        var arrived = Ev(HumanEventKind.FleetArrived, "P1");
        ok &= Check(oneShots.Update(1, new[] { arrived }, never).Count == 1 && oneShots.Entries.Count == 1, "a one-shot event is fresh and listed this turn");
        ok &= Check(oneShots.Update(2, new[] { arrived }, never).Count == 1 && oneShots.Entries.Count == 1, "reported again next turn it is fresh again, and the list holds only this turn's");
        ok &= Check(oneShots.Update(3, new HumanEvent[0], never).Count == 0 && oneShots.Entries.Count == 0, "a quiet turn clears the one-shot entries");

        // A state kind that started earlier stays while its cause holds, with no new event.
        var blockade = new HumanNotificationList();
        blockade.Update(1, new[] { Ev(HumanEventKind.PlanetBlockaded, "P2") }, always);
        blockade.Update(2, new HumanEvent[0], always);
        blockade.Update(3, new HumanEvent[0], always);
        ok &= Check(blockade.Entries.Count == 1 && blockade.Entries[0].Turns == 3, "a blockade is listed every turn it lasts, counting turns, without a new event");
        blockade.Update(4, new HumanEvent[0], never);
        ok &= Check(blockade.Entries.Count == 0, "and goes when the blockade ends");

        // A state event whose cause is already over (an automatic planet refilled its queue) makes no entry and no pause.
        var transient = new HumanNotificationList();
        ok &= Check(transient.Update(1, new[] { Ev(HumanEventKind.ProductionQueueEmpty, "P2") }, never).Count == 0 && transient.Entries.Count == 0,
            "a state event that is no longer true is ignored");

        // Two kinds on one planet are two entries; contact with two players are two entries.
        var two = new HumanNotificationList();
        two.Update(1, new[] { Ev(HumanEventKind.ShortageFood, "P0"), Ev(HumanEventKind.ShortageGrotsits, "P0"), Ev(HumanEventKind.FirstContact, "", 1), Ev(HumanEventKind.FirstContact, "", 2) }, always);
        ok &= Check(two.Entries.Count == 4, "different kinds or players are separate entries");

        // A one-shot that repeats while its condition holds (a colony ship waiting, a fight) is fresh only when it starts.
        var repeating = new HumanNotificationList();
        var ready = Ev(HumanEventKind.ColonyShipReady, "P4");
        ok &= Check(repeating.Update(1, new[] { ready }, never).Count == 1, "a colony ship becoming ready is fresh");
        for (var turn = 2; turn <= 6; turn++)
            fresh = repeating.Update(turn, new[] { ready }, never);
        ok &= Check(repeating.Entries.Count == 1 && fresh.Count == 0 && repeating.Entries[0].Turns == 6,
            "the same ready colony ship for six turns is one entry with a count of 6 and is not fresh again");
        repeating.Update(7, new HumanEvent[0], never);
        ok &= Check(repeating.Update(8, new[] { ready }, never).Count == 1, "after a gap it starts again: fresh");
        ok &= Check(repeating.Update(9, new[] { Ev(HumanEventKind.PlanetAttacked, "P4") }, never).Count == 1
                    && repeating.Update(10, new[] { Ev(HumanEventKind.PlanetAttacked, "P4") }, never).Count == 0,
            "a fight that goes on is fresh only on its first turn");

        // The cap drops one-shots before a running state entry.
        var capped = new HumanNotificationList(cap: 3);
        capped.Update(1, new[] { shortage }, always);
        capped.Update(2, new[] { shortage, Ev(HumanEventKind.FleetArrived, "A"), Ev(HumanEventKind.FleetArrived, "B"), Ev(HumanEventKind.FleetArrived, "C"), Ev(HumanEventKind.FleetArrived, "D") }, always);
        ok &= Check(capped.Entries.Count == 3 && capped.Entries.Any(x => x.Kind == HumanEventKind.ShortageFood && x.Turns == 2),
            "past the cap the one-shots go and the running shortage stays");
        ok &= Check(capped.Update(3, new[] { shortage }, always).Count == 0, "and the shortage does not come back as news");

        ok &= Check(HumanNotificationList.IsStateKind(HumanEventKind.PlanetBlockaded) && HumanNotificationList.IsStateKind(HumanEventKind.PlanetIdle)
                    && !HumanNotificationList.IsStateKind(HumanEventKind.FleetArrived), "state kinds are shortages, blockade, conversion, queue empty and idle");

        // HumanOngoing.
        using (var f = new HumanModeSelfCheck.Fixture(3))
        {
            f.Own(0, 2);
            var thisTurn = new[] { shortage };
            ok &= Check(HumanOngoing.IsStillTrue(f.Map, 0, new string[0], thisTurn, HumanEventKind.ShortageFood, "P0"), "a shortage is true while this turn reports it");
            ok &= Check(!HumanOngoing.IsStillTrue(f.Map, 0, new string[0], new HumanEvent[0], HumanEventKind.ShortageFood, "P0"), "and false once it stops being reported");
            ok &= Check(HumanOngoing.IsStillTrue(f.Map, 0, new[] { "P1" }, thisTurn, HumanEventKind.PlanetBlockaded, "P1")
                        && !HumanOngoing.IsStillTrue(f.Map, 0, new[] { "P1" }, thisTurn, HumanEventKind.PlanetBlockaded, "P2"), "a blockade is true while the planet is in the blockaded list");
            ok &= Check(HumanOngoing.IsStillTrue(f.Map, 0, new string[0], thisTurn, HumanEventKind.ProductionQueueEmpty, "P0"), "an owned planet with nothing in production and no queue is queue-empty");
            f.P(0).ManualDevelopment = false;
            ok &= Check(!HumanOngoing.IsStillTrue(f.Map, 0, new string[0], thisTurn, HumanEventKind.PlanetIdle, "P0"), "idle needs a manual planet");
            f.P(0).ManualDevelopment = true;
            ok &= Check(HumanOngoing.IsStillTrue(f.Map, 0, new string[0], thisTurn, HumanEventKind.PlanetIdle, "P0"), "a manual planet with an empty queue is idle");
            f.P(0).ProductionQueue.Add(new Planet.ProductionItem(HumanModeSelfCheck.MakeItem("OngoingItem", "Ship Type", "Warship")));
            ok &= Check(!HumanOngoing.IsStillTrue(f.Map, 0, new string[0], thisTurn, HumanEventKind.ProductionQueueEmpty, "P0"), "a planet with a queued item is not queue-empty");
            ok &= Check(!HumanOngoing.IsStillTrue(f.Map, 0, new string[0], thisTurn, HumanEventKind.ProductionQueueEmpty, "Nowhere"), "an unknown planet is never ongoing");
            ok &= Check(!HumanOngoing.IsStillTrue(f.Map, 0, new string[0], thisTurn, HumanEventKind.ConversionBegan, "P0"), "no conversion session means the conversion is over");
        }
        return ok;
    }

    #endregion

    #region Rival events: only what is visible

    public static bool RunVisibleRivalChecks()
    {
        var ok = true;
        using (var f = new HumanModeSelfCheck.Fixture(4))
        {
            f.Own(0, 2);
            var events = new HumanTurnEvents();
            HumanTurnInput Input(System.Func<string, bool> visible) => new HumanTurnInput
            {
                Results = new List<Planet.UpdateResult>
                {
                    new Planet.UpdateResult("P2", Planet.UpdateResult.UpdateResultType.UpdateResultTypeIndustryProductionComplete, "x", 1),
                    new Planet.UpdateResult("P3", Planet.UpdateResult.UpdateResultType.UpdateResultTypeIndustryProductionComplete, "x", 1),
                },
                Executed = new List<GameAI.GameAIOrder>(), NewOrders = new List<GameAI.GameAIOrder>(),
                Cuts = new List<BlockadeSystem.BlockadeCut>(), Conversions = new List<ConversionReport>(),
                BlockadedPlanets = new string[0], IsVisible = visible,
            };
            events.Collect(f.Map, f.Human, Input(null));   // the first call takes the snapshots

            var seen = events.Collect(f.Map, f.Human, Input(name => name == "P2"));
            ok &= Check(seen.Exists(e => e.Kind == HumanEventKind.ProductionComplete && e.Planet == "P2" && e.Rival)
                        && !seen.Exists(e => e.Planet == "P3"),
                "with a visibility test, a rival's event on a planet that is known but not in sight is not reported");

            var known = events.Collect(f.Map, f.Human, Input(null));
            ok &= Check(known.Exists(e => e.Planet == "P3" && e.Rival), "without a visibility test the rule falls back to known planets");
        }
        return ok;
    }

    #endregion

    #region Task 1: Check

    public static bool RunCheckChecks()
    {
        var ok = true;
        using (var f = new HumanModeSelfCheck.Fixture(4))
        {
            f.Own(0, 2);
            HumanModeSelfCheck.Dock(f.P(0), Ship.ShipKind.ColonyShip, HumanModeSelfCheck.Fixture.HumanId, 1);
            f.P(0).Food = 50f;
            var open = HumanModeSelfCheck.MakeItem("UiOpen", "Ship Type", "Warship");
            var locked = HumanModeSelfCheck.MakeItem("UiLocked", "Ship Type", "Warship", researched: false);
            f.Human.ProductionCatalog.catalogItems.AddRange(new[] { open, locked });
            var ra = HumanModeSelfCheck.MakeResearch("UiRA");
            var rb = HumanModeSelfCheck.MakeResearch("UiRB", requiredTech: "UiRA");
            f.Human.ResearchCatalog.catalogItems.AddRange(new[] { ra, rb });
            var processor = new HumanCommandProcessor(f.Map, f.Human, 3);

            ok &= Check(processor.Check(HumanModeSelfCheck.Colonize("P0", "P2")) == null, "Check: a valid colonize is null");
            ok &= Check(processor.Check(HumanModeSelfCheck.Colonize("P0", "P0")) == "SameDestination", "Check: colonizing the origin is SameDestination");
            ok &= Check(processor.Check(HumanModeSelfCheck.Shipment("P0", "P2", "food", 20)) == null, "Check: a valid shipment is null");
            ok &= Check(processor.Check(HumanModeSelfCheck.Shipment("P0", "P2", "food", 500)) == "NotEnoughStock", "Check: shipping more than the stock is NotEnoughStock");
            ok &= Check(processor.Check(HumanModeSelfCheck.Queue("P0", "UiOpen")) == null, "Check: queueing an unlocked item is null");
            ok &= Check(processor.Check(HumanModeSelfCheck.Queue("P0", "UiLocked")) == "NotUnlocked", "Check: queueing a locked item is NotUnlocked");
            ok &= Check(processor.Check(HumanModeSelfCheck.QueueResearch("UiRB")) == "PrerequisiteMissing", "Check: research without its prerequisite is PrerequisiteMissing");
            ok &= Check(processor.Check(HumanModeSelfCheck.QueueResearch("UiRA")) == null, "Check: a valid research is null");
            ok &= Check(f.Human.ResearchQueue.Count == 0, "Check queued nothing");
            ok &= Check(processor.Check(HumanModeSelfCheck.Cmd("setShipments")) == "CheckNotSupported", "Check: a command type it does not cover is CheckNotSupported");

            // Claims must not leak: ten checks of the same colonize still pass, and the real batch still has its one colony ship.
            for (var i = 0; i < 10; i++)
                ok &= Check(processor.Check(HumanModeSelfCheck.Colonize("P0", "P2")) == null, "Check repeated: still null (" + i + ")");
            var applied = processor.Apply(new[] { HumanModeSelfCheck.Colonize("P0", "P2") }, new List<GameAI.GameAIOrder>());
            ok &= Check(applied[0].applied, "after Check, Apply of the same colonize still works: " + applied[0].reason);
        }
        return ok;
    }

    #endregion
}
