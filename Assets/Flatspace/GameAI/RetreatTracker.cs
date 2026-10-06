using System.Collections.Generic;
using System.Linq;

namespace FlatSpace.AI
{
    /// <summary>
    /// Turns each turn's "gated fights that stayed, and why" into reports on change only, so the tuning log can show how often
    /// the roll keeps a group in a bad fight (RetreatStay) or no destination exists (RetreatHeld) without a line per planet per
    /// turn. A planet is reported the first turn it appears and again when its kind or reason changes; its state clears when it
    /// leaves the list (it retreated or left the gate). Pure, log-only, never saved (a load reports each current one once more).
    /// </summary>
    public class RetreatTracker
    {
        public const string KindStay = "Stay";
        public const string KindHeld = "Held";

        public struct Entry
        {
            public string Planet;
            public string Kind;        // KindStay or KindHeld
            public string Reason;      // NoDestination / OwnPlanetNotWiped for Held, empty for Stay
            public float LossFraction;
            public float PRetreat;
        }

        private readonly Dictionary<string, (string kind, string reason)> _state = new Dictionary<string, (string kind, string reason)>();

        public void Clear() => _state.Clear();

        public List<Entry> Update(IEnumerable<Entry> current)
        {
            var now = current.ToList();
            var changed = now
                .Where(e => !_state.TryGetValue(e.Planet, out var last) || last.kind != e.Kind || last.reason != e.Reason)
                .OrderBy(e => e.Planet, System.StringComparer.Ordinal)
                .ToList();
            _state.Clear();
            foreach (var e in now) _state[e.Planet] = (e.Kind, e.Reason);
            return changed;
        }
    }
}
