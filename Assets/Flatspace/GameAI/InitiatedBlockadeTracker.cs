using System;
using System.Collections.Generic;
using System.Linq;

namespace FlatSpace.AI
{
    /// <summary>Which of the three reasons an initiated blockade is held for (priority order: Dc, then Lane, then Colony).</summary>
    public enum BlockadeKind { Dc, Lane, Colony }

    /// <summary>
    /// The state of the blockades the AI holds on purpose: which planets are active, since when, the lane hysteresis and give-up
    /// counters, the skips already reported, and last turn's wanted ship total (the fleet term). Pure (no Gameboard.Instance, no
    /// Planet), fed by PlayerAI; never saved, so a load starts with no holds and logs each one again.
    /// </summary>
    public class InitiatedBlockadeTracker
    {
        public struct Held
        {
            public string Planet;
            public BlockadeKind Kind;
            public int Rival;
            public string Stance;   // War or Peace, toward Rival when it started
            public float Score;
            public float Needed;    // offense still needed this turn
            public int Ships;       // warships sent at it this turn
        }

        public struct Change
        {
            public bool Started;     // true: became active; false: stopped being active
            public string Planet;
            public BlockadeKind Kind;
            public string EndReason; // ends only
            public int TurnsHeld;    // ends only
            public Held Held;        // the details (starts: the new hold; ends: the last turn's)
        }

        public struct Skip
        {
            public string Planet;
            public BlockadeKind Kind;
            public string Reason;    // NoSpare, NoPath, Outmatched or OverCap
        }

        private readonly Dictionary<string, Held> _active = new Dictionary<string, Held>(StringComparer.Ordinal);
        private readonly Dictionary<string, int> _startTurn = new Dictionary<string, int>(StringComparer.Ordinal);
        private readonly Dictionary<string, int> _laneLow = new Dictionary<string, int>(StringComparer.Ordinal);
        private readonly Dictionary<string, (string cause, int turns)> _giveUp =
            new Dictionary<string, (string cause, int turns)>(StringComparer.Ordinal);
        private readonly Dictionary<string, int> _cooldownUntil = new Dictionary<string, int>(StringComparer.Ordinal);
        private Dictionary<string, Skip> _skips = new Dictionary<string, Skip>(StringComparer.Ordinal);

        /// <summary>The ships the active blockades wanted at the end of the last planning turn (PlayerAI.WantedWarships adds it).</summary>
        public int WantedShipsLastTurn { get; private set; }

        public void RecordWantedShips(int ships) => WantedShipsLastTurn = Math.Max(0, ships);

        public bool IsActive(string planet) => _active.ContainsKey(planet);

        public IReadOnlyList<string> ActivePlanets => _active.Keys.OrderBy(n => n, StringComparer.Ordinal).ToList();

        public BlockadeKind? KindOf(string planet) => _active.TryGetValue(planet, out var held) ? held.Kind : (BlockadeKind?)null;

        public Held? HeldOf(string planet) => _active.TryGetValue(planet, out var held) ? held : (Held?)null;

        /// <summary>
        /// `active` is this turn's selected set. Returns the transitions since the last call: an end for each planet that dropped out
        /// (its reason from `endReason`, given the hold as it was last turn; a null answer is read as Outmatched) before the starts of
        /// planets that are new. A planet that stays active keeps its start turn and takes the new details.
        /// </summary>
        public List<Change> Update(int turn, List<Held> active, Func<Held, string> endReason)
        {
            var changes = new List<Change>();
            var now = new HashSet<string>(active.Select(h => h.Planet), StringComparer.Ordinal);
            foreach (var name in _active.Keys.Where(n => !now.Contains(n)).OrderBy(n => n, StringComparer.Ordinal).ToList())
            {
                var held = _active[name];
                changes.Add(new Change
                {
                    Started = false, Planet = name, Kind = held.Kind, EndReason = endReason(held) ?? "Outmatched",
                    TurnsHeld = turn - _startTurn[name], Held = held,
                });
                _active.Remove(name);
                _startTurn.Remove(name);
                _laneLow.Remove(name);
                _giveUp.Remove(name);
            }
            foreach (var held in active)
            {
                if (!_active.ContainsKey(held.Planet))
                {
                    _startTurn[held.Planet] = turn;
                    changes.Add(new Change { Started = true, Planet = held.Planet, Kind = held.Kind, Held = held });
                }
                _active[held.Planet] = held;
            }
            return changes;
        }

        /// <summary>The skips that are new or changed since the last call (so a standing skip logs once); skips that went away are forgotten.</summary>
        public List<Skip> UpdateSkips(List<Skip> skips)
        {
            var reported = new List<Skip>();
            var next = new Dictionary<string, Skip>(StringComparer.Ordinal);
            foreach (var skip in skips)
            {
                next[skip.Planet] = skip;
                if (!_skips.TryGetValue(skip.Planet, out var before) || before.Kind != skip.Kind || before.Reason != skip.Reason)
                    reported.Add(skip);
            }
            _skips = next;
            return reported;
        }

        /// <summary>
        /// Lane hysteresis, called once a turn for an active lane with its current count: a count at or above `endCount` resets the
        /// cooling; each turn below it adds one, and the lane is released (false) when that reaches `endTurns` in a row.
        /// </summary>
        public bool LaneStillHeld(string planet, int count, int endCount, int endTurns)
        {
            if (count >= endCount)
            {
                _laneLow.Remove(planet);
                return true;
            }
            _laneLow.TryGetValue(planet, out var low);
            low++;
            _laneLow[planet] = low;
            return low < endTurns;
        }

        /// <summary>Counts consecutive turns of the same cause (Outmatched, Unreachable or NoSpare); a different cause restarts at 1.</summary>
        public int BumpGiveUp(string planet, string cause)
        {
            var turns = _giveUp.TryGetValue(planet, out var before) && before.cause == cause ? before.turns + 1 : 1;
            _giveUp[planet] = (cause, turns);
            return turns;
        }

        public void ClearGiveUp(string planet) => _giveUp.Remove(planet);

        /// <summary>After a give-up the planet may not be chosen again before `untilTurn` (kept across the hold's end).</summary>
        public void StartCooldown(string planet, int untilTurn) => _cooldownUntil[planet] = untilTurn;

        public bool InCooldown(string planet, int turn) => _cooldownUntil.TryGetValue(planet, out var until) && turn < until;
    }
}
