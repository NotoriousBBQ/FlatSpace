using System;
using System.Collections.Generic;
using System.Linq;

namespace FlatSpace.AI
{
    /// <summary>One conversion, carried in an UpdateResultTypeConversion result's Data (the result's PlayerID is the victim).</summary>
    public class ConversionEvent
    {
        public int Dominator;
        public int From;               // the player whose inhabitant flipped
        public bool AtWar;             // the dominator was at war with it
        public int MyPopulation;       // the dominator's inhabitants after the flip
        public int TotalPopulation;
        public float Progress;         // the carry after the flip
        public bool OwnerChanged;
        public int OldOwner;
        public int NewOwner;
        public string ClearedItem;     // the production item an ownership change cleared, null when none
    }

    public enum ConversionEndReason { Clean, DominanceLost, WarEnded }

    /// <summary>One line of the tuning log: a session started, an inhabitant flipped, or a session ended.</summary>
    public class ConversionReport
    {
        public enum Kind { Start, Flip, End }

        public Kind What;
        public string Planet;
        public int Dominator;
        public int WarMask;                         // Start: the at-war holders
        public int MyPopulation, TotalPopulation;   // Start: before the first flip
        public float TurnsPerFlip;                  // Start
        public ConversionEvent Flip;                // Flip
        public ConversionEndReason EndReason;       // End
    }

    /// <summary>
    /// Invasion: a planet one player dominates (docked warships with offense, no warship of a player at war with it) converts one
    /// inhabitant each time its progress reaches 1; the progress grows by 1 / max(1, conversionTurnsBase x (1 - my share)) a turn.
    /// A session starts only against a holder the dominator is at war with, then continues against any remaining holder until the
    /// planet is clean, dominance is lost, or the dominator is at war with none of the players it started against. Pure (no
    /// Gameboard.Instance); runs in the engine step right after combat, so no player's decision order can matter. Off in legacy mode.
    /// </summary>
    public static class ConversionSystem
    {
        public static float TurnsPerFlip(int mine, int total, GameAIConstants constants)
        {
            var share = total <= 0 ? 0f : (float)mine / total;
            return Math.Max(1f, constants.conversionTurnsBase * (1f - share));
        }

        /// <summary>
        /// The player that dominates the planet, or Planet.NoOwner. Players with no docked warship of effective offense above 0 never
        /// dominate; a player with an at-war rival's such ship docked beside it does not either; of two that are not at war with
        /// each other the one with more docked offense wins, ties to the lower id.
        /// </summary>
        public static int Dominator(Planet planet, GameAIMap map, WarshipStats stats)
        {
            var offense = new SortedDictionary<int, float>();
            foreach (var ship in planet.DockedShips)
            {
                if (ship.Kind != Ship.ShipKind.WarShip || ship.Owner == Planet.NoOwner) continue;
                var value = stats.EffectiveOffense(ship);
                if (value <= 0f) continue;
                offense[ship.Owner] = (offense.TryGetValue(ship.Owner, out var sum) ? sum : 0f) + value;
            }
            var best = Planet.NoOwner;
            var bestOffense = 0f;
            foreach (var pair in offense)   // ascending id: a tie keeps the lower id
            {
                var player = pair.Key;
                if (offense.Keys.Any(other => other != player && map.Diplomacy.IsAtWar(player, other, true))) continue;
                if (pair.Value > bestOffense) { best = player; bestOffense = pair.Value; }
            }
            return best;
        }

        public static string PlayersIn(int mask)
        {
            var players = Enumerable.Range(0, 31).Where(p => (mask & (1 << p)) != 0).ToList();
            return players.Count == 0 ? "-" : string.Join(",", players);
        }

        public static List<ConversionReport> Resolve(GameAIMap map, WarshipStats stats, GameAIConstants constants, int turn,
            List<Planet.UpdateResult> results)
        {
            var reports = new List<ConversionReport>();
            if (map == null || stats == null || !map.Diplomacy.Enabled) return reports;
            foreach (var planet in map.PlanetList)
                Step(planet, map, stats, constants, results, reports);
            return reports;
        }

        private static void Step(Planet planet, GameAIMap map, WarshipStats stats, GameAIConstants constants,
            List<Planet.UpdateResult> results, List<ConversionReport> reports)
        {
            var diplomacy = map.Diplomacy;
            var dominator = Dominator(planet, map, stats);

            // A running session ends when its conditions no longer hold; progress goes back to 0.
            if (planet.ConversionBy != Planet.NoOwner)
            {
                var reason = EndReason(planet, dominator, diplomacy);
                if (reason.HasValue)
                {
                    reports.Add(new ConversionReport
                    {
                        What = ConversionReport.Kind.End, Planet = planet.PlanetName, Dominator = planet.ConversionBy, EndReason = reason.Value,
                    });
                    planet.EndConversionSession();
                }
            }
            if (dominator == Planet.NoOwner) return;
            if (!planet.Population.Exists(p => p.Player != dominator)) return;

            if (planet.ConversionBy == Planet.NoOwner)
            {
                var mask = WarMask(planet, dominator, diplomacy);
                if (mask == 0) return;       // a session starts only against a holder I am at war with
                planet.ConversionBy = dominator;
                planet.ConversionProgress = 0f;
                planet.ConversionWarMask = mask;
                var mineNow = planet.Population.Count(p => p.Player == dominator);
                reports.Add(new ConversionReport
                {
                    What = ConversionReport.Kind.Start, Planet = planet.PlanetName, Dominator = dominator, WarMask = mask,
                    MyPopulation = mineNow, TotalPopulation = planet.Population.Count,
                    TurnsPerFlip = TurnsPerFlip(mineNow, planet.Population.Count, constants),
                });
            }

            var mine = planet.Population.Count(p => p.Player == dominator);
            var total = planet.Population.Count;
            planet.ConversionProgress += 1f / TurnsPerFlip(mine, total, constants);
            if (planet.ConversionProgress < 1f) return;
            planet.ConversionProgress -= 1f;

            var victim = ChooseVictim(planet, dominator, diplomacy);
            var atWar = diplomacy.IsAtWar(dominator, victim, true);
            var oldOwner = planet.Owner;
            var cleared = planet.CurrentProduction?.Item?.itemName;
            var ownerChanged = planet.ConvertInhabitant(victim, dominator);
            if (!atWar) diplomacy.RecordConversion(victim, dominator);   // a charge for a player I am not at war with

            var flip = new ConversionEvent
            {
                Dominator = dominator, From = victim, AtWar = atWar, MyPopulation = mine + 1, TotalPopulation = total,
                Progress = planet.ConversionProgress, OwnerChanged = ownerChanged, OldOwner = oldOwner, NewOwner = planet.Owner,
                ClearedItem = ownerChanged ? cleared : null,
            };
            results.Add(new Planet.UpdateResult(planet.PlanetName, Planet.UpdateResult.UpdateResultType.UpdateResultTypeConversion, flip, victim));
            reports.Add(new ConversionReport { What = ConversionReport.Kind.Flip, Planet = planet.PlanetName, Dominator = dominator, Flip = flip });

            if (!planet.Population.Exists(p => p.Player != dominator))
            {
                reports.Add(new ConversionReport
                {
                    What = ConversionReport.Kind.End, Planet = planet.PlanetName, Dominator = dominator, EndReason = ConversionEndReason.Clean,
                });
                planet.EndConversionSession();
            }
        }

        private static ConversionEndReason? EndReason(Planet planet, int dominator, DiplomacyState diplomacy)
        {
            if (dominator != planet.ConversionBy) return ConversionEndReason.DominanceLost;
            if (!planet.Population.Exists(p => p.Player != dominator)) return ConversionEndReason.Clean;
            for (var p = 0; p < 31; p++)
                if ((planet.ConversionWarMask & (1 << p)) != 0 && diplomacy.IsAtWar(dominator, p, true))
                    return null;
            return ConversionEndReason.WarEnded;
        }

        // The players the dominator is at war with that hold inhabitants here, as a bitmask.
        private static int WarMask(Planet planet, int dominator, DiplomacyState diplomacy)
        {
            var mask = 0;
            foreach (var holder in planet.Population.Select(p => p.Player).Distinct())
                if (holder != dominator && holder >= 0 && holder < 31 && diplomacy.IsAtWar(dominator, holder, true))
                    mask |= 1 << holder;
            return mask;
        }

        // At-war holders first, then the largest, then the lower id.
        private static int ChooseVictim(Planet planet, int dominator, DiplomacyState diplomacy)
            => planet.Population.Where(p => p.Player != dominator)
                .GroupBy(p => p.Player)
                .Select(g => (player: g.Key, count: g.Count(), war: diplomacy.IsAtWar(dominator, g.Key, true)))
                .OrderByDescending(c => c.war).ThenByDescending(c => c.count).ThenBy(c => c.player)
                .First().player;
    }
}
