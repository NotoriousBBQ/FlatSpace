using System;
using System.Collections.Generic;
using System.Linq;
using FlatSpace.AI;

namespace FlatSpace
{
    namespace Human
    {
        /// <summary>One line of the human's notification list: what happened (or is still happening) and where.</summary>
        public class HumanNotificationEntry
        {
            public HumanEventKind Kind;
            public string Planet = "";
            public int Player = -1;
            public bool Rival;
            public int FirstTurn;
            public int Turns = 1;     // how many turns a state or repeating entry has lasted
            public string Text = "";
        }

        /// <summary>
        /// The notification list of a human player, rebuilt at the start of every turn: what happened this turn, plus the state kinds (a
        /// shortage, a blockade, a conversion, an empty queue) that are still going on, each one entry per planet with a count of its turns.
        /// Nothing is dismissed by hand; an entry is simply gone from the next turn when its cause is. Update returns the events that are
        /// news (an episode starting, or a one-shot), which are what may stop the timer. Not saved. Pure.
        /// </summary>
        public class HumanNotificationList
        {
            private readonly int _cap;
            private readonly List<HumanNotificationEntry> _entries = new List<HumanNotificationEntry>();   // newest first
            // For the kinds that are reported every turn their condition holds: the turn they began and the last turn they were reported.
            private readonly Dictionary<(HumanEventKind kind, string planet, int player), (int first, int last)> _repeating
                = new Dictionary<(HumanEventKind, string, int), (int, int)>();

            public HumanNotificationList(int cap = 60)
            {
                _cap = Math.Max(1, cap);
            }

            public IReadOnlyList<HumanNotificationEntry> Entries => _entries;

            /// <summary>One-shot kinds that are reported again every turn their condition holds: fresh only when they start.</summary>
            public static bool RepeatsWhileTrue(HumanEventKind kind)
                => kind == HumanEventKind.ColonyShipReady || kind == HumanEventKind.PlanetAttacked;

            public static bool IsStateKind(HumanEventKind kind)
                => kind == HumanEventKind.ShortageFood || kind == HumanEventKind.ShortageGrotsits || kind == HumanEventKind.PlanetBlockaded
                   || kind == HumanEventKind.ConversionBegan || kind == HumanEventKind.ProductionQueueEmpty || kind == HumanEventKind.PlanetIdle
                   || kind == HumanEventKind.ResearchIdle;

            public List<HumanEvent> Update(int turn, IEnumerable<HumanEvent> events, Func<HumanEventKind, string, bool> stillTrue)
            {
                var fresh = new List<HumanEvent>();

                // A new turn starts a new list: what was only reported last turn goes; a state entry stays while its cause holds.
                _entries.RemoveAll(e => !IsStateKind(e.Kind));
                foreach (var entry in _entries.ToList())
                {
                    if (stillTrue(entry.Kind, entry.Planet)) entry.Turns = turn - entry.FirstTurn + 1;
                    else _entries.Remove(entry);
                }

                var seenThisTurn = new HashSet<(HumanEventKind, string, int)>();
                foreach (var e in events)
                {
                    var planet = e.Planet ?? "";
                    var key = (e.Kind, planet, e.Player);
                    if (IsStateKind(e.Kind))
                    {
                        if (_entries.Exists(x => x.Kind == e.Kind && x.Planet == planet && x.Player == e.Player)) continue;
                        if (!stillTrue(e.Kind, planet)) continue;   // already over (an automatic planet refilled its queue)
                        _entries.Insert(0, new HumanNotificationEntry
                        {
                            Kind = e.Kind, Planet = planet, Player = e.Player, Rival = e.Rival, FirstTurn = turn, Turns = 1, Text = TextFor(e, 1),
                        });
                        fresh.Add(e);
                    }
                    else if (RepeatsWhileTrue(e.Kind))
                    {
                        if (!seenThisTurn.Add(key)) continue;
                        var isNew = !_repeating.TryGetValue(key, out var span) || span.last < turn - 1;
                        var first = isNew ? turn : span.first;
                        _repeating[key] = (first, turn);
                        var turns = turn - first + 1;
                        _entries.Insert(0, new HumanNotificationEntry
                        {
                            Kind = e.Kind, Planet = planet, Player = e.Player, Rival = e.Rival, FirstTurn = first, Turns = turns, Text = TextFor(e, turns),
                        });
                        if (isNew) fresh.Add(e);
                    }
                    else
                    {
                        _entries.Insert(0, new HumanNotificationEntry
                        {
                            Kind = e.Kind, Planet = planet, Player = e.Player, Rival = e.Rival, FirstTurn = turn, Turns = 1, Text = TextFor(e, 1),
                        });
                        fresh.Add(e);
                    }
                }
                foreach (var stale in _repeating.Where(kv => kv.Value.last < turn - 1).Select(kv => kv.Key).ToList())
                    _repeating.Remove(stale);

                // The text of a running state entry carries its count.
                foreach (var entry in _entries.Where(x => IsStateKind(x.Kind)))
                    entry.Text = TextFor(new HumanEvent { Kind = entry.Kind, Planet = entry.Planet, Player = entry.Player, Rival = entry.Rival }, entry.Turns);

                // Past the cap the one-shots go first: a running state entry is never pushed out.
                while (_entries.Count > _cap)
                {
                    var evict = _entries.FindLastIndex(x => !IsStateKind(x.Kind));
                    _entries.RemoveAt(evict >= 0 ? evict : _entries.Count - 1);
                }
                return fresh;
            }

            public static string TextFor(HumanEvent e, int turns)
            {
                var planet = e.Planet ?? "";
                var suffix = turns > 1 ? $" ({turns} turns)" : "";
                string text;
                switch (e.Kind)
                {
                    case HumanEventKind.ShortageFood: text = $"Food shortage: {planet}{suffix}"; break;
                    case HumanEventKind.ShortageGrotsits: text = $"Grotsits shortage: {planet}{suffix}"; break;
                    case HumanEventKind.PlanetBlockaded: text = $"Blockaded: {planet}{suffix}"; break;
                    case HumanEventKind.ConversionBegan: text = $"Conversion under way: {planet}{suffix}"; break;
                    case HumanEventKind.ProductionQueueEmpty: text = $"Queue empty: {planet}{suffix}"; break;
                    case HumanEventKind.PlanetIdle: text = $"Planet idle: {planet}{suffix}"; break;
                    case HumanEventKind.PlanetAttacked: text = $"Planet attacked: {planet}{suffix}"; break;
                    case HumanEventKind.PlanetLost: text = $"Planet lost: {planet}"; break;
                    case HumanEventKind.FirstContact: text = $"First contact with player {e.Player}" + (planet.Length > 0 ? $" at {planet}" : ""); break;
                    case HumanEventKind.WarDeclared: text = $"Player {e.Player} declared war"; break;
                    case HumanEventKind.PeaceMade: text = $"Peace with player {e.Player}"; break;
                    case HumanEventKind.FleetArrived: text = $"Fleet arrived: {planet}"; break;
                    case HumanEventKind.ProductionComplete: text = $"Production complete: {planet}"; break;
                    case HumanEventKind.ResearchComplete: text = "Research complete"; break;
                    case HumanEventKind.ResearchIdle: text = $"Research idle: nothing queued{suffix}"; break;
                    case HumanEventKind.ColonyShipBuilt: text = $"Colony ship built: {planet}"; break;
                    case HumanEventKind.ColonyShipReady: text = $"Colony ship ready: {planet}{suffix}"; break;
                    case HumanEventKind.PopulationGained: text = $"Population grew: {planet}"; break;
                    case HumanEventKind.PopulationLost: text = $"Population lost: {planet}"; break;
                    case HumanEventKind.PlanetColonized: text = $"Colonized: {planet}"; break;
                    case HumanEventKind.ShipmentLost: text = $"Shipment lost: {planet}"; break;
                    case HumanEventKind.ConversionEnded: text = $"Conversion ended: {planet}"; break;
                    case HumanEventKind.TransportOrdered: text = $"Shipment sent to {planet}"; break;
                    default: text = e.Kind.ToString(); break;
                }
                // A rival's event names the player who did it (the players have no other names).
                return e.Rival ? $"Player {e.Player}: {text}" : text;
            }
        }

        /// <summary>Whether the cause of a state notification still holds. Pure: reads the map and what this turn reported.</summary>
        public static class HumanOngoing
        {
            public static bool IsStillTrue(GameAIMap map, int humanId, IEnumerable<string> blockaded, IEnumerable<HumanEvent> thisTurn,
                HumanEventKind kind, string planet, bool researchIdle = false)
            {
                var p = string.IsNullOrEmpty(planet) ? null : map.GetPlanet(planet);
                switch (kind)
                {
                    case HumanEventKind.ShortageFood:
                    case HumanEventKind.ShortageGrotsits:
                        return thisTurn.Any(e => e.Kind == kind && e.Planet == planet && !e.Rival);
                    case HumanEventKind.PlanetBlockaded:
                        return blockaded.Contains(planet);
                    case HumanEventKind.ConversionBegan:
                        return p != null && p.ConversionBy != Planet.NoOwner;
                    case HumanEventKind.ProductionQueueEmpty:
                        return p != null && p.Owner == humanId && !p.CurrentProduction.HasValue && p.ProductionQueue.Count == 0;
                    case HumanEventKind.PlanetIdle:
                        return p != null && p.Owner == humanId && p.ManualDevelopment && !p.CurrentProduction.HasValue && p.ProductionQueue.Count == 0;
                    case HumanEventKind.ResearchIdle:
                        return researchIdle;
                    default:
                        return false;
                }
            }
        }
    }
}
