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
        public float BombardFactor = 1f;   // the pace factor this turn (ConversionSystem.BombardSpeedFactor)
    }

    public enum ConversionEndReason { Clean, DominanceLost, WarEnded }

    /// <summary>One line of the tuning log: a session started, an inhabitant flipped, or a session ended.</summary>
    public class ConversionReport
    {
        public enum Kind { Start, Flip, End, Blocked }

        public Kind What;
        public string Planet;
        public int Dominator;
        public int WarMask;                         // Start: the at-war holders
        public int MyPopulation, TotalPopulation;   // Start: before the first flip
        public float TurnsPerFlip;                  // Start
        public float BombardFactor = 1f;            // Start
        public float ResumedProgress;               // Start: progress carried over from a session that lost dominance (0 = a fresh start)
        public ConversionEvent Flip;                // Flip
        public ConversionEndReason EndReason;       // End
        public int Bunkers;                         // Blocked: the bunkers (any owner's) standing on the planet
    }

    /// <summary>
    /// Invasion: a planet one player dominates (docked warships/siegeships with bombard, no such ship of a player at war with it) converts
    /// one inhabitant each time its progress reaches 1; the progress grows by BombardSpeedFactor / max(1, conversionTurnsBase x (1 - my
    /// share)) a turn. No session starts or continues while any bunker stands on the planet (a bunker ends a running one as "dominance lost").
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

        /// <summary>Combat strength of one docked ship, the fight projector's figure: effective offense x (current health + defense).</summary>
        private static float ShipStrength(Ship ship, WarshipStats stats)
            => stats.EffectiveOffense(ship) * (stats.CurrentHealth(ship) + stats.Defense(ship));

        /// <summary>
        /// The player that dominates the planet, or Planet.NoOwner. The single dominance rule (BombardmentSystem.Dominated uses it too):
        /// players with no docked warship or siegeship of effective bombard above 0 never dominate; a player is contested, and does not
        /// dominate, when the docked WARSHIPS of the players at war with it that hold inhabitants here add up to more than
        /// dominanceRivalRatio x its own fighting strength (warships and siegeships; a rival's siegeships and a rival with no inhabitants
        /// here never contest); of two that are not contested the one with more docked bombard wins, ties to the lower id. A negative
        /// ratio is the old rule: any docked fighting ship of an at-war rival contests. Bunkers are not considered (Step gates on them).
        /// </summary>
        public static int Dominator(Planet planet, GameAIMap map, WarshipStats stats)
        {
            var ratio = map.GameAIConstants != null ? map.GameAIConstants.dominanceRivalRatio : -1f;
            var present = new SortedSet<int>();
            var bombard = new SortedDictionary<int, float>();
            var fleet = new Dictionary<int, float>();      // each player's strength, warships and siegeships
            var warships = new Dictionary<int, float>();   // each player's warship strength
            foreach (var ship in planet.DockedShips)
            {
                if (!Ship.IsFightingKind(ship.Kind) || ship.Owner == Planet.NoOwner) continue;
                present.Add(ship.Owner);
                var strength = ShipStrength(ship, stats);
                fleet[ship.Owner] = (fleet.TryGetValue(ship.Owner, out var all) ? all : 0f) + strength;
                if (ship.Kind == Ship.ShipKind.WarShip)
                    warships[ship.Owner] = (warships.TryGetValue(ship.Owner, out var war) ? war : 0f) + strength;
                var value = stats.EffectiveBombard(ship);
                if (value <= 0f) continue;
                bombard[ship.Owner] = (bombard.TryGetValue(ship.Owner, out var sum) ? sum : 0f) + value;
            }
            var best = Planet.NoOwner;
            var bestBombard = 0f;
            foreach (var pair in bombard)   // ascending id: a tie keeps the lower id
            {
                var player = pair.Key;
                bool contested;
                if (ratio < 0f)
                    contested = present.Any(other => other != player && map.Diplomacy.IsAtWar(player, other, true));
                else
                {
                    var rivals = 0f;
                    foreach (var rival in warships)
                        if (rival.Key != player && map.Diplomacy.IsAtWar(player, rival.Key, true)
                            && planet.Population.Exists(p => p.Player == rival.Key))
                            rivals += rival.Value;
                    contested = rivals > ratio * fleet[player];
                }
                if (contested) continue;
                if (pair.Value > bestBombard) { best = player; bestBombard = pair.Value; }
            }
            return best;
        }

        /// <summary>The owner's total effective bombard of docked warships and siegeships at the planet (0 for an ownerless id).</summary>
        public static float DockedBombard(Planet planet, int owner, WarshipStats stats)
        {
            if (planet == null || stats == null || owner == Planet.NoOwner) return 0f;
            var sum = 0f;
            foreach (var ship in planet.DockedShips)
                if (Ship.IsFightingKind(ship.Kind) && ship.Owner == owner)
                    sum += stats.EffectiveBombard(ship);
            return sum;
        }

        /// <summary>Conversion pace as a fraction of full: clamp(bombard / conversionBombardReference, conversionBombardMinFactor, 1).</summary>
        public static float BombardSpeedFactor(float myBombard, GameAIConstants c)
        {
            if (c.conversionBombardReference <= 0f) return 1f;
            var factor = myBombard / c.conversionBombardReference;
            return Math.Min(1f, Math.Max(c.conversionBombardMinFactor, factor));
        }

        /// <summary>
        /// A bunker stands here that player `me` will never bombard away: its owner is me or not at war with me (BombardmentSystem.Triggers
        /// needs war), so no conversion can ever run for me. A bunker of a player I am at war with is not stuck (my fleet bombards it).
        /// </summary>
        public static bool HasStuckBunker(Planet planet, int me, DiplomacyState diplomacy)
            => planet.DockedShips.Exists(s => s.Kind == Ship.ShipKind.Bunker && (s.Owner == Planet.NoOwner || !diplomacy.IsAtWar(me, s.Owner, true)));

        /// <summary>Any bunker (any owner's) stands on the planet: no conversion session can run there.</summary>
        public static bool HasBunkers(Planet planet) => planet.DockedShips.Exists(s => s.Kind == Ship.ShipKind.Bunker);

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
                Step(planet, map, stats, constants, turn, results, reports);
            return reports;
        }

        private static void Step(Planet planet, GameAIMap map, WarshipStats stats, GameAIConstants constants, int turn,
            List<Planet.UpdateResult> results, List<ConversionReport> reports)
        {
            var diplomacy = map.Diplomacy;
            var dominator = Dominator(planet, map, stats);
            var bunkers = HasBunkers(planet);

            // A running session ends when its conditions no longer hold; progress goes back to 0.
            if (planet.ConversionBy != Planet.NoOwner)
            {
                var reason = EndReason(planet, bunkers ? Planet.NoOwner : dominator, diplomacy);   // a bunker standing = not dominated
                if (reason.HasValue)
                {
                    reports.Add(new ConversionReport
                    {
                        What = ConversionReport.Kind.End, Planet = planet.PlanetName, Dominator = planet.ConversionBy, EndReason = reason.Value,
                    });
                    if (reason.Value == ConversionEndReason.DominanceLost && constants.conversionCarryTurns > 0)
                    {
                        planet.ConversionCarryBy = planet.ConversionBy;
                        planet.ConversionCarryProgress = planet.ConversionProgress;
                        planet.ConversionCarryUntilTurn = turn + constants.conversionCarryTurns;
                    }
                    else planet.ConversionCarryBy = Planet.NoOwner;
                    planet.EndConversionSession();
                }
            }
            if (dominator == Planet.NoOwner) return;
            if (bunkers)   // bunkers (any owner's) hold the planet: no session starts or continues
            {
                // Report it (each turn; the log dedups) when a session would otherwise run: a foreign inhabitant to convert and a war with a holder.
                if (planet.Population.Exists(p => p.Player != dominator) && WarMask(planet, dominator, diplomacy) != 0)
                    reports.Add(new ConversionReport
                    {
                        What = ConversionReport.Kind.Blocked, Planet = planet.PlanetName, Dominator = dominator,
                        Bunkers = planet.DockedShips.Count(s => s.Kind == Ship.ShipKind.Bunker),
                    });
                return;
            }
            if (!planet.Population.Exists(p => p.Player != dominator)) return;
            var factor = BombardSpeedFactor(DockedBombard(planet, dominator, stats), constants);

            if (planet.ConversionBy == Planet.NoOwner)
            {
                var mask = WarMask(planet, dominator, diplomacy);
                if (mask == 0) return;       // a session starts only against a holder I am at war with
                planet.ConversionBy = dominator;
                // The same player back within the carry window resumes its progress; any other start is fresh.
                var resumed = planet.ConversionCarryBy == dominator && turn <= planet.ConversionCarryUntilTurn
                    ? planet.ConversionCarryProgress : 0f;
                planet.ConversionCarryBy = Planet.NoOwner;
                planet.ConversionProgress = resumed;
                planet.ConversionWarMask = mask;
                var mineNow = planet.Population.Count(p => p.Player == dominator);
                reports.Add(new ConversionReport
                {
                    What = ConversionReport.Kind.Start, Planet = planet.PlanetName, Dominator = dominator, WarMask = mask,
                    MyPopulation = mineNow, TotalPopulation = planet.Population.Count,
                    TurnsPerFlip = TurnsPerFlip(mineNow, planet.Population.Count, constants), BombardFactor = factor, ResumedProgress = resumed,
                });
            }

            var mine = planet.Population.Count(p => p.Player == dominator);
            var total = planet.Population.Count;
            planet.ConversionProgress += factor / TurnsPerFlip(mine, total, constants);
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
                ClearedItem = ownerChanged ? cleared : null, BombardFactor = factor,
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
