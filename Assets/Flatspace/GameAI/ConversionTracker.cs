using System.Collections.Generic;

namespace FlatSpace.AI
{
    /// <summary>
    /// Log-only state for the tuning log: when each conversion session started and how many inhabitants it has flipped (so the End
    /// line can report turns held and flips), and which players were already reported out of planets. Never saved: a load finds
    /// sessions on the planets and begins tracking them again (the Start line is logged once more).
    /// </summary>
    public class ConversionTracker
    {
        private readonly Dictionary<string, (int startTurn, int converted)> _sessions = new Dictionary<string, (int startTurn, int converted)>();
        private readonly HashSet<int> _outOfPlanets = new HashSet<int>();

        public void Clear()
        {
            _sessions.Clear();
            _outOfPlanets.Clear();
        }

        public bool Tracking(string planet) => _sessions.ContainsKey(planet);

        public void Begin(string planet, int turn) => _sessions[planet] = (turn, 0);

        public void CountFlip(string planet)
        {
            if (_sessions.TryGetValue(planet, out var session)) _sessions[planet] = (session.startTurn, session.converted + 1);
        }

        public (int turnsHeld, int converted) Finish(string planet, int turn)
        {
            if (!_sessions.TryGetValue(planet, out var session)) return (0, 0);
            _sessions.Remove(planet);
            return (turn - session.startTurn, session.converted);
        }

        /// <summary>True only the first time a player is reported out of planets.</summary>
        public bool NoteOutOfPlanets(int player) => _outOfPlanets.Add(player);
    }
}
