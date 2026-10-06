using System.Collections.Generic;
using System.Linq;

namespace FlatSpace
{
    namespace AI
    {
        public enum Stance { Peace = 0, War = 1 }

        /// <summary>
        /// Every player's stance toward every rival, the hostility score behind it, and the blockade cuts waiting to be
        /// charged to a rival's hostility. Pure (no Gameboard.Instance). Effective war is derived, never stored: player P is
        /// at war with Q when P's own stance toward Q is War, or Q's stance toward P is War and P has contact with Q.
        /// </summary>
        public class DiplomacyState
        {
            /// <summary>A pair that never changed has no hold: its turns-since-change is far above any hold.</summary>
            public const int NeverChanged = -1000000;

            public struct Pair
            {
                public Stance Stance;
                public float Hostility;
                public int LastChangeTurn;
                // Log-only: the last turn's terms and strengths. Not saved.
                public float CutsTerm, NearTerm, StrengthTerm, PWar, MyStrength, RivalStrength;
                public float LossShare, PSurrender;   // log-only: the numbers behind a Surrender order
                public int NearShips;
                // A surrender locks the pair until this turn (0 = no truce): no Declare War, and IsAtWar is false.
                public int TruceUntil;
            }

            /// <summary>What a save keeps of a pair.</summary>
            public struct Entry
            {
                public int Rival;
                public Stance Stance;
                public float Hostility;
                public int LastChangeTurn;
                public int TruceUntil;
            }

            /// <summary>False = legacy: every rival with contact is an enemy and no stance is ever decided.</summary>
            public bool Enabled { get; set; }

            private readonly Dictionary<(int, int), Pair> _pairs = new Dictionary<(int, int), Pair>();
            private readonly Dictionary<(int, int), int> _cuts = new Dictionary<(int, int), int>();

            public Pair Get(int me, int rival)
                => _pairs.TryGetValue((me, rival), out var pair)
                    ? pair
                    : new Pair { Stance = Stance.Peace, LastChangeTurn = NeverChanged };

            public void Set(int me, int rival, Pair pair) => _pairs[(me, rival)] = pair;

            public Stance StanceToward(int me, int rival) => Get(me, rival).Stance;

            public float Hostility(int me, int rival) => Get(me, rival).Hostility;

            public int TurnsSinceChange(int me, int rival, int turn) => turn - Get(me, rival).LastChangeTurn;

            /// <summary>Sets a stance; true only when it changed (the caller logs and holds on that).</summary>
            public bool SetStance(int me, int rival, Stance stance, int turn)
            {
                var pair = Get(me, rival);
                if (pair.Stance == stance) return false;
                pair.Stance = stance;
                pair.LastChangeTurn = turn;
                Set(me, rival, pair);
                return true;
            }

            /// <summary>The current turn, set by GameAI each turn (and by self-checks): the truce is tested against it.</summary>
            public int Turn { get; set; }

            /// <summary>True while either side of the pair carries a truce that has not ended at `turn`.</summary>
            public bool InTruce(int a, int b, int turn) => turn < Get(a, b).TruceUntil || turn < Get(b, a).TruceUntil;

            public bool IsAtWar(int me, int rival, bool meHasContactWithRival)
                => !InTruce(me, rival, Turn)
                   && (StanceToward(me, rival) == Stance.War
                       || (meHasContactWithRival && StanceToward(rival, me) == Stance.War));

            /// <summary>
            /// The players `me` is at war with: its contact players, plus anyone it declared on (a stance can only exist after
            /// contact, and contact can flicker) or who declared on it, each tested with IsAtWar.
            /// </summary>
            public SortedSet<int> WarRivals(int me, IEnumerable<int> contactPlayers)
            {
                var contact = new HashSet<int>(contactPlayers);
                var candidates = new SortedSet<int>(contact);
                foreach (var key in _pairs.Keys)
                {
                    if (key.Item1 == me) candidates.Add(key.Item2);
                    if (key.Item2 == me) candidates.Add(key.Item1);
                }
                var result = new SortedSet<int>();
                foreach (var candidate in candidates)
                    if (candidate != me && IsAtWar(me, candidate, contact.Contains(candidate)))
                        result.Add(candidate);
                return result;
            }

            /// <summary>The rivals `me` has a recorded pair with, ascending.</summary>
            public List<int> Rivals(int me)
                => _pairs.Keys.Where(k => k.Item1 == me).Select(k => k.Item2).OrderBy(r => r).ToList();

            // ── Blockade cuts waiting to be charged ──────────────────────────

            /// <summary>One of `victim`'s orders was cut by `blocker`'s blockade. Self and ownerless blockers are ignored.</summary>
            public void RecordCut(int victim, int blocker)
            {
                if (blocker < 0 || blocker == victim) return;
                _cuts.TryGetValue((victim, blocker), out var count);
                _cuts[(victim, blocker)] = count + 1;
            }

            /// <summary>The cuts by `blocker` on `victim`'s orders since the last take; consumed.</summary>
            public int TakeCuts(int victim, int blocker)
            {
                if (!_cuts.TryGetValue((victim, blocker), out var count)) return 0;
                _cuts.Remove((victim, blocker));
                return count;
            }

            /// <summary>Drops every cut still waiting for `victim` (blockers it has no contact with must not pile up).</summary>
            public void DiscardCuts(int victim)
            {
                foreach (var key in _cuts.Keys.Where(k => k.Item1 == victim).ToList())
                    _cuts.Remove(key);
            }

            // ── Saves ────────────────────────────────────────────────────────

            public List<Entry> Snapshot(int me)
                => _pairs.Where(kv => kv.Key.Item1 == me)
                    .OrderBy(kv => kv.Key.Item2)
                    .Select(kv => new Entry
                    {
                        Rival = kv.Key.Item2,
                        Stance = kv.Value.Stance,
                        Hostility = kv.Value.Hostility,
                        LastChangeTurn = kv.Value.LastChangeTurn,
                        TruceUntil = kv.Value.TruceUntil,
                    }).ToList();

            /// <summary>Replaces `me`'s pairs with the entries; null or empty (an older save) leaves it all Peace at 0.</summary>
            public void Restore(int me, IEnumerable<Entry> entries)
            {
                foreach (var key in _pairs.Keys.Where(k => k.Item1 == me).ToList())
                    _pairs.Remove(key);
                if (entries == null) return;
                foreach (var entry in entries)
                    _pairs[(me, entry.Rival)] = new Pair
                    {
                        Stance = entry.Stance,
                        Hostility = entry.Hostility,
                        LastChangeTurn = entry.LastChangeTurn,
                        TruceUntil = entry.TruceUntil,
                    };
            }
        }
    }
}
