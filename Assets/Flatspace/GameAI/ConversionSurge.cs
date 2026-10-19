using System;
using System.Collections.Generic;
using System.Linq;

namespace FlatSpace.AI
{
    /// <summary>
    /// A dominated planet and the colony ships it still needs: the one fact both the ColonyShip production weight and the
    /// colonization pass read. Wanted is a TOTAL committed to the planet (en route plus pipeline), so EffectiveWanted
    /// (EW = max(0, min(wanted - enRoute, room))) is how many MORE ships should be added. Spec
    /// docs/superpowers/specs/2026-10-18-conversion-colony-surge-design.md.
    /// </summary>
    public class SurgeTarget
    {
        public string Planet;
        public int MyPop;
        public int TotalPop;
        public int Wanted;
        public int EnRoute;           // my colonists in flight toward it
        public int Room;              // MaxPopulation - population - EnRoute
        public int EffectiveWanted;
        public int Pipeline;          // colony ships docked or in production on its servers
        public List<string> Servers = new List<string>();
    }

    public class SurgePlan
    {
        public readonly List<SurgeTarget> Targets = new List<SurgeTarget>();

        /// <summary>The target a neighbour serves, else null.</summary>
        public SurgeTarget For(string neighbour)
            => Targets.Find(t => t.Servers.Contains(neighbour));
    }

    /// <summary>Pure planner for the colony ship surge on dominated planets (no Gameboard.Instance).</summary>
    public static class ConversionSurge
    {
        /// <summary>ceil(total / max(1, mine) x shipsPerRatio); 0 for an empty planet or a scale of 0 or below.</summary>
        public static int Wanted(int total, int mine, float shipsPerRatio)
        {
            if (total <= 0 || shipsPerRatio <= 0f) return 0;
            return (int)Math.Ceiling(total / (double)Math.Max(1, mine) * shipsPerRatio);
        }

        /// <summary>max(0, min(wanted - enRoute, room)).</summary>
        public static int EffectiveWanted(int wanted, int enRoute, int room)
            => Math.Max(0, Math.Min(wanted - enRoute, room));

        /// <summary>
        /// One target per dominated planet (isDominated) that can still take colonists. Planets are taken in order of descending
        /// wanted, then name; each claims the planets I own within conversionSurgeReachNodes path nodes of it (cheapest path first) that no
        /// earlier target claimed, so a ship is counted once. The
        /// plan is empty while the scale is 0 or below and in legacy mode (no diplomacy).
        /// </summary>
        public static SurgePlan Build(GameAIMap map, int playerId, Func<Planet, bool> isDominated)
        {
            var plan = new SurgePlan();
            var scale = map.GameAIConstants.conversionSurgeShipsPerRatio;
            if (scale <= 0f || !map.Diplomacy.Enabled) return plan;
            var reach = map.GameAIConstants.conversionSurgeReachNodes;

            var candidates = new List<SurgeTarget>();
            foreach (var planet in map.PlanetList)
            {
                if (!isDominated(planet)) continue;
                var mine = planet.Population.Count(p => p.Player == playerId);
                var enRoute = planet.GetIncomingColonists(playerId);
                var room = planet.MaxPopulation - planet.Population.Count - enRoute;
                var wanted = Wanted(planet.Population.Count, mine, scale);
                var ew = EffectiveWanted(wanted, enRoute, room);
                if (ew <= 0) continue;
                candidates.Add(new SurgeTarget
                {
                    Planet = planet.PlanetName, MyPop = mine, TotalPop = planet.Population.Count, Wanted = wanted,
                    EnRoute = enRoute, Room = room, EffectiveWanted = ew,
                });
            }

            var claimed = new HashSet<string>();
            foreach (var target in candidates.OrderByDescending(t => t.Wanted).ThenBy(t => t.Planet, StringComparer.Ordinal))
            {
                var dominated = map.GetPlanet(target.Planet);
                var inRange = new List<(float cost, Planet server)>();
                foreach (var server in map.PlanetList)
                {
                    if (server == dominated || server.Owner != playerId) continue;
                    if (dominated.DistanceMapToPathingList.TryGetValue(server.PlanetName, out var entry)
                        && entry.NumNodes >= 2 && entry.NumNodes <= reach)
                        inRange.Add((entry.Cost, server));
                }
                foreach (var (_, server) in inRange.OrderBy(x => x.cost).ThenBy(x => x.server.PlanetName, StringComparer.Ordinal))
                {
                    if (!claimed.Add(server.PlanetName)) continue;
                    target.Servers.Add(server.PlanetName);
                    target.Pipeline += server.DockedShips.Count(s => s.Kind == Ship.ShipKind.ColonyShip && s.Owner == playerId)
                                       + server.CountColonyShipsInProduction();
                }
                plan.Targets.Add(target);
            }
            return plan;
        }
    }
}
