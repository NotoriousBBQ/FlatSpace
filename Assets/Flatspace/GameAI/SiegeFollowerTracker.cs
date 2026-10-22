using System.Collections.Generic;
using System.Linq;

namespace FlatSpace.AI
{
    /// <summary>
    /// Turns each turn's follower-gate decisions (a siegeship group sent to the assault target, or kept where it is because its
    /// arrival projection is a loss) into reports on change only, for the SiegeFollow and SiegeHeld tuning-log lines. A route
    /// (origin -> target) is reported the first turn it appears and again when its kind or reason changes; its state clears when it
    /// leaves the list (no gate call for it that turn). Pure, log-only, never saved (a load reports each current one once more).
    /// </summary>
    public class SiegeFollowerTracker
    {
        public const string KindFollow = "Follow";
        public const string KindHeld = "Held";
        public const string ReasonWiped = "Wiped";            // the arriving group is projected to be destroyed
        public const string ReasonOutmatched = "Outmatched";  // it survives but loses followerMaxLossFraction or more of its strength

        public struct Entry
        {
            public string Origin;
            public string Target;
            public string Kind;          // KindFollow or KindHeld
            public string Reason;        // ReasonWiped / ReasonOutmatched for Held, empty for Follow
            public int Ships;
            public float LossFraction;   // the projected loss of the arriving group (0 when there is no fight to project)
        }

        private readonly Dictionary<string, (string kind, string reason)> _state = new Dictionary<string, (string kind, string reason)>();

        private static string Key(Entry e) => e.Origin + "->" + e.Target;

        public void Clear() => _state.Clear();

        public List<Entry> Update(IEnumerable<Entry> current)
        {
            // One entry per route: a route seen twice in a turn keeps its last decision.
            var now = new Dictionary<string, Entry>();
            foreach (var e in current) now[Key(e)] = e;
            var changed = now.Values
                .Where(e => !_state.TryGetValue(Key(e), out var last) || last.kind != e.Kind || last.reason != e.Reason)
                .OrderBy(Key, System.StringComparer.Ordinal)
                .ToList();
            _state.Clear();
            foreach (var pair in now) _state[pair.Key] = (pair.Value.Kind, pair.Value.Reason);
            return changed;
        }
    }
}
