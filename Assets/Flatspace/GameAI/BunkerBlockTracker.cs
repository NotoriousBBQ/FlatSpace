using System.Collections.Generic;
using System.Linq;

namespace FlatSpace.AI
{
    /// <summary>
    /// Turns each turn's "planets where bunkers block a conversion that would otherwise run" into reports on change only, so the tuning
    /// log shows how long bunkers stall conversion without a line per planet per turn. A planet is reported the first turn it is
    /// blocked and again when its bunker count or dominator changes; its state clears the first turn it is not blocked, so a later
    /// block reports again. Pure, log-only, never saved (a load reports each current block once more).
    /// </summary>
    public class BunkerBlockTracker
    {
        public struct Entry
        {
            public string Planet;
            public int Dominator;
            public int Bunkers;
        }

        private readonly Dictionary<string, (int dominator, int bunkers)> _state = new Dictionary<string, (int dominator, int bunkers)>();

        public void Clear() => _state.Clear();

        /// <summary>The planets blocked this turn (all of them; an absent planet is no longer blocked). Returns those to log.</summary>
        public List<Entry> Update(IEnumerable<Entry> current)
        {
            var now = current.ToList();
            var changed = now
                .Where(e => !_state.TryGetValue(e.Planet, out var last) || last.dominator != e.Dominator || last.bunkers != e.Bunkers)
                .OrderBy(e => e.Planet, System.StringComparer.Ordinal)
                .ToList();
            _state.Clear();
            foreach (var e in now) _state[e.Planet] = (e.Dominator, e.Bunkers);
            return changed;
        }
    }
}
