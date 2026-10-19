using System.Collections.Generic;
using System.Linq;

namespace FlatSpace.AI
{
    /// <summary>
    /// Turns each turn's colony ship surge plan into reports on change only, so the tuning log shows how long a surge lasts and
    /// whether the room cap binds without a line per dominated planet per turn. A planet is reported the first turn it is in the
    /// plan and again when its wanted, room, en-route or pipeline count changes; its state clears when it leaves the plan. Pure,
    /// log-only, never saved (a load reports each current one once more).
    /// </summary>
    public class ConversionSurgeTracker
    {
        public struct Entry
        {
            public string Planet;
            public int Wanted;
            public int Room;
            public int EnRoute;
            public int Pipeline;
            public int MyPop;
            public int TotalPop;
            public int Servers;
        }

        private readonly Dictionary<string, (int wanted, int room, int enRoute, int pipeline, int servers)> _state
            = new Dictionary<string, (int wanted, int room, int enRoute, int pipeline, int servers)>();

        public void Clear() => _state.Clear();

        public List<Entry> Update(IEnumerable<Entry> current)
        {
            var now = current.ToList();
            var changed = now
                .Where(e => !_state.TryGetValue(e.Planet, out var last)
                            || last.wanted != e.Wanted || last.room != e.Room || last.enRoute != e.EnRoute || last.pipeline != e.Pipeline
                            || last.servers != e.Servers)
                .OrderBy(e => e.Planet, System.StringComparer.Ordinal)
                .ToList();
            _state.Clear();
            foreach (var e in now) _state[e.Planet] = (e.Wanted, e.Room, e.EnRoute, e.Pipeline, e.Servers);
            return changed;
        }
    }
}
