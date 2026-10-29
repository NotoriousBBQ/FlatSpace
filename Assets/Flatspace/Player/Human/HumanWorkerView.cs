using System;
using System.Collections.Generic;
using System.Linq;

namespace FlatSpace
{
    namespace Human
    {
        /// <summary>
        /// The questions the Workers section of the planet panel asks, pure (no Gameboard.Instance) so a self-check drives them: who can
        /// assign, what a press picks up, what a drop makes of the split, and when to warn that the split starves the planet.
        /// </summary>
        public static class HumanWorkerView
        {
            /// <summary>A Food or Grotsits loss is warned about when the stock covers it for fewer than this many turns.</summary>
            public const int WarnTurns = 10;

            /// <summary>The human owns the planet and develops it by hand: only then can its workers be moved.</summary>
            public static bool CanAssign(Planet planet, int humanId)
                => HumanPlanetView.OwnsPlanet(planet, humanId) && planet.ManualDevelopment;

            /// <summary>
            /// How many people a press on an icon picks up: that icon and every icon after it in its category (wrapped lines included).
            /// iconIndex is 0-based in reading order; an index outside the category picks up nothing.
            /// </summary>
            public static int PickedCount(int categoryCount, int iconIndex)
                => iconIndex < 0 || iconIndex >= categoryCount ? 0 : categoryCount - iconIndex;

            /// <summary>The split after moving picked people from one category to another (a new array; the total never changes).</summary>
            public static int[] AfterDrop(int[] counts, Planet.WorkerCategory from, Planet.WorkerCategory to, int picked)
            {
                var result = (int[])counts.Clone();
                if (from == to || picked <= 0) return result;
                var moved = Math.Min(picked, result[(int)from]);
                result[(int)from] -= moved;
                result[(int)to] += moved;
                return result;
            }

            /// <summary>The assignWorkers command for a drop, or null when the drop would change nothing.</summary>
            public static HumanCommand DropCommand(Planet planet, int[] counts, Planet.WorkerCategory from, Planet.WorkerCategory to, int picked)
            {
                var after = AfterDrop(counts, from, to, picked);
                if (after.SequenceEqual(counts)) return null;
                return new HumanCommand { type = "assignWorkers", origin = planet.PlanetName, workers = new List<int>(after) };
            }

            /// <summary>
            /// One line per resource (Food, Grotsits) that the planet loses each turn and whose stock lasts fewer than WarnTurns turns, for
            /// example "Food: -3 per turn, stock lasts 4 turns". Empty when nothing is short.
            /// </summary>
            public static List<string> Warnings(Planet planet)
            {
                var lines = new List<string>();
                if (planet == null || planet.Population.Count == 0) return lines;
                var net = HumanPlanetView.NetPerTurn(planet);
                AddWarning(lines, "Food", planet.Food, net.food);
                AddWarning(lines, "Grotsits", planet.Grotsits, net.grotsits);
                return lines;
            }

            private static void AddWarning(List<string> lines, string label, float stock, float perTurn)
            {
                if (perTurn >= 0f) return;
                string lasts;
                if (stock <= 0f)
                    lasts = "stock is empty";
                else
                {
                    var turns = (int)Math.Floor(stock / -perTurn);
                    if (turns >= WarnTurns) return;
                    lasts = turns < 1 ? "stock lasts less than a turn" : turns == 1 ? "stock lasts 1 turn" : $"stock lasts {turns} turns";
                }
                lines.Add($"{label}: {perTurn:0.#} per turn, {lasts}");
            }
        }
    }
}
