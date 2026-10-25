using System;
using System.Collections.Generic;

namespace FlatSpace
{
    namespace Human
    {
        /// <summary>
        /// What the human last saw of each planet: its owner and the turn. Presentation only (the Planets screen's Owner column), kept for the human
        /// alone and never read by the simulation. A planet is recorded only while it is in sight, so a planet that changes hands out of sight keeps
        /// its old owner here and its age grows.
        /// </summary>
        public sealed class PlanetSightMemory
        {
            public struct Sighting
            {
                public int Owner;   // Planet.Owner at the time, Planet.NoOwner for none
                public int Turn;
            }

            private readonly Dictionary<string, Sighting> _seen = new Dictionary<string, Sighting>();

            /// <summary>Records owner and turn for every planet that is in sight now; the rest keep what they had.</summary>
            public void Update(IEnumerable<Planet> planets, Func<string, bool> inSight, int turn)
            {
                foreach (var planet in planets)
                    if (inSight(planet.PlanetName))
                        _seen[planet.PlanetName] = new Sighting { Owner = planet.Owner, Turn = turn };
            }

            public bool TryGet(string planetName, out Sighting sighting) => _seen.TryGetValue(planetName, out sighting);

            public IEnumerable<(string planet, int owner, int turn)> Entries()
            {
                foreach (var pair in _seen)
                    yield return (pair.Key, pair.Value.Owner, pair.Value.Turn);
            }

            /// <summary>Replaces everything with the given entries; null (an older save) means nothing remembered.</summary>
            public void Restore(IEnumerable<(string planet, int owner, int turn)> entries)
            {
                _seen.Clear();
                if (entries == null) return;
                foreach (var entry in entries)
                    _seen[entry.planet] = new Sighting { Owner = entry.owner, Turn = entry.turn };
            }
        }
    }
}
