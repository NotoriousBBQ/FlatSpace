using System;
using System.Collections.Generic;
using System.Linq;

namespace FlatSpace.AI
{
    /// <summary>
    /// Which planets the AI holds a blockade on purpose (docs/superpowers/specs/2026-10-22-ai-initiated-blockades-design.md): a rival's
    /// Distribution Center, a planet its food and grotsits shipments pass through in numbers, or a valuable uncolonized planet.
    /// Pure: PlayerAI hands it plain inputs, it returns the ranked, capped target list and the candidates it passed over.
    /// </summary>
    public static class BlockadeInitiator
    {
        public struct Candidate
        {
            public string Planet;
            public BlockadeKind Kind;
            public int Rival;     // the rival the blockade is against (for a Colony target, the lowest valid rival id)
            public float Score;
        }

        public struct Inputs
        {
            public ISet<int> Rivals;                          // valid rivals (see ValidRivals)
            public Dictionary<string, int> DcOwner;           // planet -> the valid rival that has it as a Distribution Center
            public Dictionary<(string planet, int rival), int> Lanes;   // see LaneCounts
            public Func<Planet, bool> ColonyStartAllowed;     // true when the planet may be started as a Colony hold (valid target, none of my colony ships can reach it yet)
            public Func<Planet, bool> ColonyHoldValid;        // true while a held Colony target is still one I could colonize; null = always
            public InitiatedBlockadeTracker Tracker;
            public int Turn;                                  // this turn, for the give-up cooldown
        }

        public struct Selection
        {
            public List<Candidate> Active;                    // the ranked list cut at blockadeInitiateMaxActive
            public List<Candidate> Ranked;                    // every candidate in rank order (held ones first), uncapped
            public List<InitiatedBlockadeTracker.Skip> Skipped;
        }

        /// <summary>
        /// The rivals a blockade may be started against: players I am at war with, plus contact players whose hostility toward me
        /// is at least blockadeInitiateHostility (above 100 means none at peace). With diplomacy off every other player is a rival.
        /// </summary>
        public static ISet<int> ValidRivals(int me, DiplomacyState diplomacy, IEnumerable<int> contact, IEnumerable<int> allOthers,
            GameAIConstants constants)
        {
            var result = new HashSet<int>();
            if (!diplomacy.Enabled)
            {
                foreach (var other in allOthers) if (other != me) result.Add(other);
                return result;
            }
            var contactList = contact.ToList();
            foreach (var rival in diplomacy.WarRivals(me, contactList)) result.Add(rival);
            if (constants.blockadeInitiateHostility <= 100f)
                foreach (var rival in contactList)
                    if (rival != me && diplomacy.Hostility(me, rival) >= constants.blockadeInitiateHostility)
                        result.Add(rival);
            return result;
        }

        /// <summary>
        /// For each rival's in-flight food or grotsits transport order, 1 on every planet of its route (endpoints included), summed per
        /// (planet, rival). The route is the order's carried one when it has one, else the stored shortest path.
        /// </summary>
        public static Dictionary<(string planet, int rival), int> LaneCounts(GameAIMap map, IEnumerable<GameAI.GameAIOrder> orders,
            ISet<int> rivals)
        {
            var counts = new Dictionary<(string planet, int rival), int>();
            foreach (var order in orders)
            {
                if (order.Type != GameAI.GameAIOrder.OrderType.OrderTypeFoodTransport
                    && order.Type != GameAI.GameAIOrder.OrderType.OrderTypeGrotsitsTransport) continue;
                if (!rivals.Contains(order.PlayerId)) continue;
                foreach (var name in RouteNames(map, order))
                {
                    var key = (name, order.PlayerId);
                    counts.TryGetValue(key, out var n);
                    counts[key] = n + 1;
                }
            }
            return counts;
        }

        private static List<string> RouteNames(GameAIMap map, GameAI.GameAIOrder order)
        {
            if (order.Route != null && order.Route.Count >= 2) return order.Route;
            var names = new List<string>();
            if (order.Origin == null || order.Target == null || order.Origin == order.Target) return names;
            var origin = map.GetPlanet(order.Origin);
            if (origin == null || !origin.DistanceMapToPathingList.ContainsKey(order.Target)) return names;
            foreach (var node in map.GetPath(order.Origin, order.Target).PathNodes) names.Add(node.Name);
            return names;
        }

        public static Selection Select(GameAIMap map, int me, Inputs inputs)
        {
            var selection = new Selection
            {
                Active = new List<Candidate>(), Ranked = new List<Candidate>(), Skipped = new List<InitiatedBlockadeTracker.Skip>(),
            };
            var constants = map.GameAIConstants;
            if (constants.blockadeInitiateMaxActive <= 0 || inputs.Rivals == null || inputs.Rivals.Count == 0) return selection;
            var tracker = inputs.Tracker;
            var all = new List<Candidate>();

            if (inputs.DcOwner != null)
                foreach (var pair in inputs.DcOwner.OrderBy(p => p.Key, StringComparer.Ordinal))
                {
                    if (!inputs.Rivals.Contains(pair.Value) || !IsKnown(map, me, pair.Key)) continue;
                    all.Add(new Candidate
                    {
                        Planet = pair.Key, Kind = BlockadeKind.Dc, Rival = pair.Value,
                        Score = map.Chokepoint(pair.Key) + map.ProducerValue(pair.Key),
                    });
                }

            if (inputs.Lanes != null)
            {
                // The strongest valid rival lane through each known planet; an active lane gets its hysteresis check once a turn.
                var best = new Dictionary<string, (int rival, int count)>(StringComparer.Ordinal);
                foreach (var pair in inputs.Lanes.OrderBy(p => p.Key.planet, StringComparer.Ordinal).ThenBy(p => p.Key.rival))
                {
                    if (!inputs.Rivals.Contains(pair.Key.rival) || !IsKnown(map, me, pair.Key.planet)) continue;
                    if (!best.TryGetValue(pair.Key.planet, out var current) || pair.Value > current.count)
                        best[pair.Key.planet] = (pair.Key.rival, pair.Value);
                }
                var laneNames = new HashSet<string>(best.Keys, StringComparer.Ordinal);
                // A held lane whose rival is no longer valid is not kept: it ends with the war (WarEnded), not after the cooling turns.
                foreach (var name in tracker.ActivePlanets)
                    if (tracker.KindOf(name) == BlockadeKind.Lane && inputs.Rivals.Contains(tracker.HeldOf(name).Value.Rival)) laneNames.Add(name);
                foreach (var name in laneNames.OrderBy(n => n, StringComparer.Ordinal))
                {
                    best.TryGetValue(name, out var lane);
                    var active = tracker.KindOf(name) == BlockadeKind.Lane;
                    var qualifies = active
                        ? tracker.LaneStillHeld(name, lane.count, constants.blockadeLaneEndCount, constants.blockadeLaneEndTurns)
                        : lane.count >= constants.blockadeLaneStartCount;
                    if (!qualifies) continue;
                    var rival = best.ContainsKey(name) ? lane.rival : tracker.HeldOf(name).Value.Rival;
                    all.Add(new Candidate { Planet = name, Kind = BlockadeKind.Lane, Rival = rival, Score = lane.count });
                }
            }

            var colonyRival = inputs.Rivals.OrderBy(r => r).First();
            foreach (var planet in map.PlanetList.OrderBy(p => p.PlanetName, StringComparer.Ordinal))
            {
                if (planet.Population.Count > 0 || !IsKnown(map, me, planet.PlanetName)) continue;
                var value = map.Chokepoint(planet.PlanetName) * constants.blockadeColonyChokepointWeight
                            + map.ProducerValue(planet.PlanetName) * constants.blockadeColonyProducerWeight;
                if (value < constants.blockadeColonyMinValue) continue;
                var active = tracker.KindOf(planet.PlanetName) == BlockadeKind.Colony;
                if (!active && (inputs.ColonyStartAllowed == null || !inputs.ColonyStartAllowed(planet))) continue;
                if (active && inputs.ColonyHoldValid != null && !inputs.ColonyHoldValid(planet)) continue;
                all.Add(new Candidate { Planet = planet.PlanetName, Kind = BlockadeKind.Colony, Rival = colonyRival, Score = value });
            }

            all.RemoveAll(c => tracker.InCooldown(c.Planet, inputs.Turn));

            // One target per planet: the hold's own kind if it is active, else the highest-priority kind.
            var perPlanet = all.GroupBy(c => c.Planet, StringComparer.Ordinal)
                .Select(g => g.OrderBy(c => tracker.KindOf(c.Planet) == c.Kind ? 0 : 1).ThenBy(c => (int)c.Kind).First())
                .ToList();
            Func<Candidate, bool> held = c => tracker.KindOf(c.Planet) == c.Kind;
            Func<IEnumerable<Candidate>, List<Candidate>> rank = list => list
                .OrderBy(c => (int)c.Kind).ThenByDescending(c => c.Score).ThenBy(c => c.Planet, StringComparer.Ordinal).ToList();

            var ordered = rank(perPlanet.Where(held)).Concat(rank(perPlanet.Where(c => !held(c)))).ToList();
            selection.Ranked.AddRange(ordered);
            for (var i = 0; i < ordered.Count; i++)
            {
                if (i < constants.blockadeInitiateMaxActive) selection.Active.Add(ordered[i]);
                else selection.Skipped.Add(new InitiatedBlockadeTracker.Skip { Planet = ordered[i].Planet, Kind = ordered[i].Kind, Reason = "OverCap" });
            }
            return selection;
        }

        private static bool IsKnown(GameAIMap map, int me, string planetName) => map.Knowledge.IsKnown(me, planetName);
    }
}
