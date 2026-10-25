using System;
using System.Collections.Generic;
using System.Linq;
using FlatSpace.AI;

namespace FlatSpace
{
    namespace Human
    {
        public enum PlanetFilter { AllKnown, ColonizationTargets, Mine, Hostile }

        public enum PlanetColumn { Planet, Owner, Type, Pop, Food, Grotsits, Industry, Research, Building, Ships, Alerts }

        /// <summary>One line of the Planets screen. A null number or an empty text is a blank cell: a figure the human cannot see.</summary>
        public struct PlanetRow
        {
            public string Planet;
            public string OwnerText;
            public string Type;
            public int? Pop;
            public int MaxPop;
            public float? Food, Grotsits, Industry, Research;
            public string BuildItem;
            public int? BuildTurns;
            public int? Ships;
            public string ShipOwners;   // "You 1, P2 3"; shown in the Ships cell
            public string Alerts;
            public bool InSight, IsMine, IsTarget, IsHostile;
        }

        public struct SortState
        {
            public PlanetColumn Column;
            public bool Descending;
            public bool Active;
        }

        /// <summary>
        /// The Planets screen's rows, filters, search and sort. Pure: it reads the map and what the caller passes in, never Gameboard.Instance. Every
        /// planet the human has seen is one row with the same columns; the figures of a planet the human cannot see are blank, except the owner, which is the last
        /// one the human saw (PlanetSightMemory) with how long ago.
        /// </summary>
        public static class HumanPlanetTable
        {
            /// <summary>
            /// ownerVisible: planets whose current owner the human sees even though the rest of the planet is out of sight (those next to its occupied or crewed planets). humanBlockades says whether the human is the blocker of a rival planet (BlockadeSystem.Value with the rival as order owner); blockadedMine
            /// is the human's own blockade view. A rival planet the human blockades or converts counts as hostile activity even out of sight.
            /// </summary>
            public static List<PlanetRow> Build(GameAIMap map, PlayerAI human, int humanId, int turn, Func<string, bool> inSight, Func<string, bool> ownerVisible, Func<string, bool> hasSeen,
                PlanetSightMemory sights, BlockadeView blockadedMine, Func<Planet, bool> humanBlockades)
            {
                var contacts = map.Knowledge.ContactPlayers(map, humanId);

                // The planets the human has seen (hasSeen: the fog, not the AI's wider knowledge) and every planet it owns.
                var rows = new List<PlanetRow>();
                foreach (var planet in map.PlanetList)
                {
                    var name = planet.PlanetName;
                    if (planet.Owner != humanId && !hasSeen(name)) continue;
                    var mine = planet.Owner == humanId;
                    var visible = mine || inSight(name);
                    var row = new PlanetRow
                    {
                        Planet = name,
                        Type = planet.Type.ToString().Replace("PlanetType", ""),
                        MaxPop = planet.MaxPopulation,
                        InSight = visible,
                        IsMine = mine,
                        IsTarget = human.IsValidColonizationTarget(planet, false) && !planet.Population.Exists(q => q.Player != humanId),   // the AI's rule, no colony ship needed, and no planet a rival already holds
                        OwnerText = "",
                        BuildItem = "",
                        Alerts = "",
                        ShipOwners = "",
                    };

                    var last = default(PlanetSightMemory.Sighting);
                    var seen = sights != null && sights.TryGet(name, out last);
                    row.OwnerText = visible || ownerVisible(name)
                        ? OwnerText(humanId, planet.Owner, true, true, 0)
                        : OwnerText(humanId, last.Owner, false, seen, turn - last.Turn);

                    // Hostile activity: the human's planets under pressure, plus the rival planets the human is pressing.
                    var alerts = new List<string>();
                    if (mine)
                    {
                        if (blockadedMine != null && blockadedMine.IsBlockaded(name)) alerts.Add("Blockaded");
                        if (planet.ConversionBy != Planet.NoOwner) alerts.Add("Converting");
                        if (planet.DockedShips.Any(s => Ship.IsFightingKind(s.Kind) && s.Owner != humanId && s.Owner >= 0
                                                        && map.Diplomacy.IsAtWar(humanId, s.Owner, contacts.Contains(s.Owner))))
                            alerts.Add("Enemy ships");
                    }
                    else
                    {
                        if (humanBlockades != null && humanBlockades(planet)) alerts.Add("Blockading");
                        if (planet.ConversionBy == humanId) alerts.Add("Converting (you)");
                    }
                    row.IsHostile = alerts.Count > 0;

                    // Ships (all players') are seen on a planet in sight and on one next to a planet the human occupies or has a ship at, like its owner.
                    if (visible || ownerVisible(name))
                    {
                        var ships = planet.DockedShips.Where(x => Ship.CountsAsShip(x.Kind)).ToList();
                        row.Ships = ships.Count;
                        row.Pop = planet.Population.Count;
                        row.ShipOwners = string.Join(", ", ships.GroupBy(x => x.Owner).OrderBy(g => g.Key)
                            .Select(g => $"{(g.Key == humanId ? "You" : $"P{g.Key}")} {g.Count()}"));
                    }

                    if (visible)
                    {
                        if (planet.Population.Count > 0)
                        {
                            var net = HumanPlanetView.NetPerTurn(planet);
                            row.Food = net.food;
                            row.Grotsits = net.grotsits;
                            row.Industry = planet.IndustryProduced;
                            row.Research = planet.ResearchProduced;
                        }
                        var item = planet.CurrentProduction.HasValue ? planet.CurrentProduction.Value.Item
                            : planet.ProductionQueue.Count > 0 ? planet.ProductionQueue[0].Item : null;
                        if (item != null)
                        {
                            row.BuildItem = item.itemName;
                            var estimates = HumanBuildList.QueueEstimates(planet);
                            row.BuildTurns = estimates.Count > 0 ? estimates[0] : null;
                        }
                        // Alerts show on a planet in sight; the filter flag above does not depend on sight.
                        row.Alerts = string.Join(", ", alerts);
                    }
                    rows.Add(row);
                }
                return rows;
            }

            /// <summary>"You" for the human, "Player N" for a rival, empty for none in sight. Out of sight, the last-known owner ("Unowned" for none) plus how long ago; empty when never seen.</summary>
            public static string OwnerText(int humanId, int owner, bool inSight, bool seenBefore, int turnsAgo)
            {
                if (!seenBefore) return "";
                if (inSight && owner < 0) return "";
                // Out of sight an unowned planet still gets its age: it may have been colonized since.
                var name = owner < 0 ? "Unowned" : owner == humanId ? "You" : $"Player {owner}";
                if (inSight) return name;
                var ago = Math.Max(1, turnsAgo);
                return $"{name} ({ago} {(ago == 1 ? "turn" : "turns")} ago)";
            }

            public static List<PlanetRow> Filter(List<PlanetRow> rows, PlanetFilter filter)
                => rows.Where(r => Matches(r, filter)).ToList();

            public static int Count(List<PlanetRow> rows, PlanetFilter filter) => rows.Count(r => Matches(r, filter));

            private static bool Matches(PlanetRow row, PlanetFilter filter)
            {
                switch (filter)
                {
                    case PlanetFilter.ColonizationTargets: return row.IsTarget;
                    case PlanetFilter.Mine: return row.IsMine;
                    case PlanetFilter.Hostile: return row.IsHostile;
                    default: return true;
                }
            }

            public static List<PlanetRow> Search(List<PlanetRow> rows, string text)
            {
                if (string.IsNullOrEmpty(text)) return new List<PlanetRow>(rows);
                return rows.Where(r => r.Planet.IndexOf(text, StringComparison.OrdinalIgnoreCase) >= 0).ToList();
            }

            /// <summary>The first click on a heading sorts descending, the next ascending, and so on; another heading starts over at descending.</summary>
            public static SortState Click(SortState current, PlanetColumn column)
            {
                if (current.Active && current.Column == column)
                    return new SortState { Column = column, Descending = !current.Descending, Active = true };
                return new SortState { Column = column, Descending = true, Active = true };
            }

            /// <summary>Blank cells go last in either direction; ties (and the blanks) fall back to the planet name.</summary>
            public static List<PlanetRow> Sort(List<PlanetRow> rows, SortState state)
            {
                if (!state.Active) return new List<PlanetRow>(rows);
                var filled = rows.Where(r => !IsBlank(r, state.Column)).ToList();
                var blank = rows.Where(r => IsBlank(r, state.Column)).OrderBy(r => r.Planet, StringComparer.OrdinalIgnoreCase).ToList();
                var comparer = Comparer<PlanetRow>.Create((a, b) =>
                {
                    var c = Compare(a, b, state.Column);
                    if (state.Descending) c = -c;
                    return c != 0 ? c : string.Compare(a.Planet, b.Planet, StringComparison.OrdinalIgnoreCase);
                });
                var result = filled.OrderBy(r => r, comparer).ToList();
                result.AddRange(blank);
                return result;
            }

            private static bool IsBlank(PlanetRow r, PlanetColumn column)
            {
                switch (column)
                {
                    case PlanetColumn.Owner: return string.IsNullOrEmpty(r.OwnerText);
                    case PlanetColumn.Pop: return !r.Pop.HasValue;
                    case PlanetColumn.Food: return !r.Food.HasValue;
                    case PlanetColumn.Grotsits: return !r.Grotsits.HasValue;
                    case PlanetColumn.Industry: return !r.Industry.HasValue;
                    case PlanetColumn.Research: return !r.Research.HasValue;
                    case PlanetColumn.Building: return string.IsNullOrEmpty(r.BuildItem);
                    case PlanetColumn.Ships: return !r.Ships.HasValue;
                    case PlanetColumn.Alerts: return string.IsNullOrEmpty(r.Alerts);
                    default: return false;
                }
            }

            private static int Compare(PlanetRow a, PlanetRow b, PlanetColumn column)
            {
                switch (column)
                {
                    case PlanetColumn.Owner: return string.Compare(a.OwnerText, b.OwnerText, StringComparison.OrdinalIgnoreCase);
                    case PlanetColumn.Type: return string.Compare(a.Type, b.Type, StringComparison.OrdinalIgnoreCase);
                    case PlanetColumn.Pop: return a.Pop.Value.CompareTo(b.Pop.Value);
                    case PlanetColumn.Food: return a.Food.Value.CompareTo(b.Food.Value);
                    case PlanetColumn.Grotsits: return a.Grotsits.Value.CompareTo(b.Grotsits.Value);
                    case PlanetColumn.Industry: return a.Industry.Value.CompareTo(b.Industry.Value);
                    case PlanetColumn.Research: return a.Research.Value.CompareTo(b.Research.Value);
                    case PlanetColumn.Building: return string.Compare(a.BuildItem, b.BuildItem, StringComparison.OrdinalIgnoreCase);
                    case PlanetColumn.Ships: return a.Ships.Value.CompareTo(b.Ships.Value);
                    case PlanetColumn.Alerts: return string.Compare(a.Alerts, b.Alerts, StringComparison.OrdinalIgnoreCase);
                    default: return string.Compare(a.Planet, b.Planet, StringComparison.OrdinalIgnoreCase);
                }
            }
        }
    }
}
