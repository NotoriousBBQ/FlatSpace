using System.Collections.Generic;
using System.Linq;

namespace FlatSpace.AI
{
    /// <summary>
    /// Turns each turn's "blockaded planets the assault passed over, and why" into reports on change only, so the tuning log
    /// can show which blockades the player never answers without a line per planet per turn (see the BlockadeSkipped log
    /// line). A planet is reported the first turn it is skipped, and again when its reason or winner changes; its state clears
    /// when it is no longer skipped (chosen as the target, or no longer blockaded), so a later skip is reported afresh. Pure
    /// (no Gameboard.Instance, no Planet), fed by PlayerAI; log-only state, never saved, so a load reports each current skip
    /// once more.
    /// </summary>
    public class BlockadeSkipTracker
    {
        public struct Skip
        {
            public string Planet;
            public string Reason;           // NoPath or Outranked (AssaultPlanner.SkipNoPath / SkipOutranked)
            public string Winner;           // the planet chosen instead; "-" for NoPath
            public float  Value;            // the skipped planet's blockade value against me
            public float  WinnerCommitted;  // offense committed at the winner (0 for NoPath)
        }

        private readonly Dictionary<string, (string reason, string winner)> _state
            = new Dictionary<string, (string reason, string winner)>();

        public void Clear() => _state.Clear();

        /// <summary>
        /// `current` is every planet skipped this turn. Returns those that are new or whose reason or winner changed since
        /// the last call, sorted by planet name, and replaces the remembered state with `current`.
        /// </summary>
        public List<Skip> Update(IEnumerable<Skip> current)
        {
            var now = current.ToList();
            var changed = now
                .Where(k => !_state.TryGetValue(k.Planet, out var last) || last.reason != k.Reason || last.winner != k.Winner)
                .OrderBy(k => k.Planet, System.StringComparer.Ordinal)
                .ToList();

            _state.Clear();
            foreach (var k in now) _state[k.Planet] = (k.Reason, k.Winner);
            return changed;
        }
    }
}
