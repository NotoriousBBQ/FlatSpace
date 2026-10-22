using System.Collections.Generic;
using System.Linq;

namespace FlatSpace.AI
{
    /// <summary>
    /// Turns each turn's declined colonist escorts (a colonization candidate whose shortest route crosses a known blockade, or whose
    /// target is a dominated planet, and whose origin has too little spare warship offense to cover it) into reports on change only,
    /// for the EscortDetour tuning-log line. A route (origin -> target) is reported the first turn it appears and again when its
    /// reason changes; its state clears when it leaves the list (not declined that turn). Pure, log-only, never saved (a load
    /// reports each current one once more). Same pattern as SiegeFollowerTracker.
    /// </summary>
    public class EscortDetourTracker
    {
        public const string ReasonNoSpare = "NoSpare";            // no spare warship at the origin
        public const string ReasonShortOffense = "ShortOffense";  // spare warships, but their offense is below the blockade x (1 + margin)
        public const string ReasonFight = "Fight";                // the origin has a fight projection of mine this turn: a retreat may need
                                                                  // its ships, and a retreat has priority, so it escorts nothing

        public struct Entry
        {
            public string Origin;
            public string Target;
            public string Reason;   // ReasonNoSpare, ReasonShortOffense or ReasonFight
        }

        private readonly Dictionary<string, string> _state = new Dictionary<string, string>();

        private static string Key(Entry e) => e.Origin + "->" + e.Target;

        public void Clear() => _state.Clear();

        public List<Entry> Update(IEnumerable<Entry> current)
        {
            // One entry per route: a route seen twice in a turn keeps its last reason.
            var now = new Dictionary<string, Entry>();
            foreach (var e in current) now[Key(e)] = e;
            var changed = now.Values
                .Where(e => !_state.TryGetValue(Key(e), out var last) || last != e.Reason)
                .OrderBy(Key, System.StringComparer.Ordinal)
                .ToList();
            _state.Clear();
            foreach (var pair in now) _state[pair.Key] = pair.Value.Reason;
            return changed;
        }
    }
}
