using System;
using System.Collections.Generic;

namespace FlatSpace.AI
{
    /// <summary>
    /// Turns each turn's blockade-breaking target into start and end transitions, so the tuning log can say which planet was
    /// chosen, why, and how it ended without a line per turn. Pure (no Gameboard.Instance, no Planet), fed by PlayerAI;
    /// log-only state, never saved, so a load just reports the current target once more.
    /// </summary>
    public class BlockadeTargetTracker
    {
        public struct Target
        {
            public string Planet;
            public int    Blocker;   // -1 (Planet.NoOwner) for a remembered, unseen planet
            public float  Value;
            public float  Needed;
            public string Reason;    // Committed, RecentCut, Chokepoint or Cheapest
        }

        public struct Change
        {
            public bool   Started;   // true: the planet became the target; false: it stopped being the target
            public string Planet;
            public string EndReason; // Cleared, Switched or Unreachable (ends only)
            public int    TurnsHeld; // turns since it became the target (ends only)
            public Target Target;    // the new target's details (starts only)
        }

        private Target? _current;
        private int _startTurn;

        public string Current => _current?.Planet;

        public void Clear() => _current = null;

        /// <summary>
        /// `current` is this turn's blockade target (null when the assault has none or goes to an ordinary enemy planet);
        /// `isBlockaded` says whether a planet is still blockaded in my view. Returns the transitions since the last call,
        /// an end before the start that replaces it. An ended target is Cleared when it is no longer blockaded, else
        /// Switched when another target replaced it, else Unreachable.
        /// </summary>
        public List<Change> Update(int turn, Target? current, Func<string, bool> isBlockaded)
        {
            var changes = new List<Change>();
            if (_current != null && (current == null || current.Value.Planet != _current.Value.Planet))
            {
                var ended = _current.Value.Planet;
                var reason = !isBlockaded(ended) ? "Cleared" : current != null ? "Switched" : "Unreachable";
                changes.Add(new Change { Started = false, Planet = ended, EndReason = reason, TurnsHeld = turn - _startTurn });
                _current = null;
            }
            if (current != null && _current == null)
            {
                _current = current;
                _startTurn = turn;
                changes.Add(new Change { Started = true, Planet = current.Value.Planet, Target = current.Value });
            }
            return changes;
        }
    }
}
