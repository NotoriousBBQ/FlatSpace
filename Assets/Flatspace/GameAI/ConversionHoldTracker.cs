using System.Collections.Generic;
using System.Linq;

namespace FlatSpace.AI
{
    /// <summary>
    /// Turns each turn's "held conversion planets and what a call would have taken from them" into reports on change only, so the
    /// tuning log shows whether held ships were ever wanted elsewhere (evidence for offense-scaled conversion) without a line per
    /// planet per turn. A planet is reported the first turn it is held and again when its wanted count, call or call target
    /// changes; its state clears when it leaves the holds. Pure, log-only, never saved (a load reports each current one once more).
    /// </summary>
    public class ConversionHoldTracker
    {
        public struct Entry
        {
            public string Planet;
            public int HeldShips;
            public float HeldOffense;
            public int Wanted;          // held ships a call would have taken
            public string Call;         // Garrison, Assault, Blockade or -
            public string CallTarget;   // the planet the call was for, or -
            public float RivalNearby;   // the largest at-war rival's offense on or beside the planet
            public float Progress;
        }

        private readonly Dictionary<string, (int wanted, string call, string target)> _state = new Dictionary<string, (int wanted, string call, string target)>();

        public void Clear() => _state.Clear();

        public List<Entry> Update(IEnumerable<Entry> current)
        {
            var now = current.ToList();
            var changed = now
                .Where(e => !_state.TryGetValue(e.Planet, out var last) || last.wanted != e.Wanted || last.call != e.Call || last.target != e.CallTarget)
                .OrderBy(e => e.Planet, System.StringComparer.Ordinal)
                .ToList();
            _state.Clear();
            foreach (var e in now) _state[e.Planet] = (e.Wanted, e.Call, e.CallTarget);
            return changed;
        }
    }
}
