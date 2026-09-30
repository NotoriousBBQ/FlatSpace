using System.Collections.Generic;
using System.Linq;

namespace FlatSpace.AI
{
    /// <summary>
    /// Turns each turn's "which populated planets are short of grotsits" into start and end transitions, so the tuning log
    /// can say WHICH planets went short and WHEN without a line per planet per turn (see the GrotsitsShort log line). Pure
    /// (no Gameboard.Instance, no Planet), fed by GameAI after the planets update; log-only state, never saved, so a load
    /// just reports each planet that is still short once more.
    /// </summary>
    public class GrotsitsShortTracker
    {
        public struct Change
        {
            public string Planet;
            public bool Started;   // true: the planet became short; false: it recovered or is no longer populated
        }

        private readonly HashSet<string> _short = new HashSet<string>();

        public bool IsShort(string planet) => _short.Contains(planet);

        public void Clear() => _short.Clear();

        /// <summary>
        /// `populatedPlanets` is every planet with population this turn and whether it is short of grotsits. Returns the
        /// planets whose state changed since the last call, sorted by name. A short planet that is absent from the set
        /// (nobody lives there any more) ends its episode.
        /// </summary>
        public List<Change> Update(IEnumerable<(string planet, bool isShort)> populatedPlanets)
        {
            var nowShort = new HashSet<string>();
            foreach (var (planet, isShort) in populatedPlanets)
                if (isShort) nowShort.Add(planet);

            var changes = new List<Change>();
            foreach (var planet in nowShort.Where(p => !_short.Contains(p)))
                changes.Add(new Change { Planet = planet, Started = true });
            foreach (var planet in _short.Where(p => !nowShort.Contains(p)))
                changes.Add(new Change { Planet = planet, Started = false });

            _short.Clear();
            foreach (var planet in nowShort) _short.Add(planet);

            changes.Sort((a, b) => string.CompareOrdinal(a.Planet, b.Planet));
            return changes;
        }
    }
}
