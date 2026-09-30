using System.Collections.Generic;
using System.Linq;

namespace FlatSpace.AI
{
    /// <summary>
    /// One player's memory of planets where one of its OWN orders was cut by a blockade: planet, blockade value and the
    /// turn it was last learned. An entry is active while turn - Turn &lt; lifetime, so it lasts `lifetime` turns after
    /// the LAST cut (another cut refreshes it). lifetime &lt;= 0 disables memory. Pure (no Gameboard.Instance).
    /// </summary>
    public class BlockadeMemory
    {
        public struct Entry
        {
            public string Planet;
            public float Value;
            public int Turn;
        }

        private readonly Dictionary<string, Entry> _entries = new Dictionary<string, Entry>();

        public bool IsActive(string planet, int turn, int lifetime)
            => lifetime > 0 && planet != null && _entries.TryGetValue(planet, out var entry) && turn - entry.Turn < lifetime;

        /// <summary>Stores or refreshes the planet. True when it was NOT active before (news); false on a refresh or when memory is disabled.</summary>
        public bool Learn(string planet, float value, int turn, int lifetime)
        {
            if (lifetime <= 0 || planet == null) return false;
            var wasActive = IsActive(planet, turn, lifetime);
            _entries[planet] = new Entry { Planet = planet, Value = value, Turn = turn };
            return !wasActive;
        }

        public void Forget(string planet)
        {
            if (planet != null) _entries.Remove(planet);
        }

        public List<Entry> Active(int turn, int lifetime)
        {
            if (lifetime <= 0) return new List<Entry>();
            return _entries.Values.Where(e => turn - e.Turn < lifetime).OrderBy(e => e.Planet).ToList();
        }

        public void Prune(int turn, int lifetime)
        {
            foreach (var name in _entries.Keys.ToList())
                if (lifetime <= 0 || turn - _entries[name].Turn >= lifetime)
                    _entries.Remove(name);
        }

        /// <summary>What is saved: the active entries only.</summary>
        public List<Entry> Snapshot(int turn, int lifetime) => Active(turn, lifetime);

        /// <summary>Replaces the contents; null means empty.</summary>
        public void Restore(IEnumerable<Entry> entries)
        {
            _entries.Clear();
            if (entries == null) return;
            foreach (var entry in entries)
                if (entry.Planet != null) _entries[entry.Planet] = entry;
        }
    }
}
