using System;
using System.Collections.Generic;
using System.Linq;

namespace FlatSpace
{
    namespace AI
    {
        /// <summary>The loss one attacker caused a victim at a planet this turn (carried in a WarshipsLost result's Data).</summary>
        public class CombatLoss
        {
            public int Attacker;
            public float Ships;           // the attacker's share of the ships destroyed
            public float StrengthLost;    // the same share of the destroyed ships' strength at the start of the turn
        }

        /// <summary>Colony ships destroyed at a planet (carried in a ColonyShipsLost result's Data).</summary>
        public class ColonyLoss
        {
            public int Count;
            public int ByPlayer;
        }

        /// <summary>One line of the tuning log: what one attacker dealt to one victim at a planet this turn.</summary>
        public struct CombatReport
        {
            public string Planet;
            public int Attacker;
            public int Victim;
            public float DamageDealt;
            public float ShipsDestroyed;
        }

        /// <summary>
        /// Docked warships of players at war that share a planet fight once a turn: deterministic, simultaneous, focus fire
        /// on the most damaged ship, damage persistent on the ship (Ship.Damage). Pure (no Gameboard.Instance). Off in legacy
        /// mode (no stances to fight on). Appends WarshipsLost and ColonyShipsLost results, like Planet.UpdatePlanet does.
        /// </summary>
        public static class CombatSystem
        {
            private struct Unit
            {
                public Ship Ship;
                public int Owner;
                public int Index;       // dock order
                public float Health;
                public float Max;
                public float Defense;
                public float Offense;   // effective
                public float Strength;  // offense x (health + Defense)
            }

            public static List<CombatReport> Resolve(GameAIMap map, WarshipStats stats, GameAIConstants constants, int turn,
                List<Planet.PlanetUpdateResult> results)
            {
                var reports = new List<CombatReport>();
                if (map == null || stats == null || !map.Diplomacy.Enabled) return reports;
                foreach (var planet in map.PlanetList)
                {
                    FightAt(planet, map, stats, constants, results, reports);
                    Repair(planet, map, stats, constants);
                    DestroyStrandedColonyShips(planet, map, results);
                }
                return reports;
            }

            private static void FightAt(Planet planet, GameAIMap map, WarshipStats stats, GameAIConstants constants,
                List<Planet.PlanetUpdateResult> results, List<CombatReport> reports)
            {
                // Start-of-turn snapshot of every warship that can fight (a ship with no health left, or no Health stat, cannot).
                var units = new List<Unit>();
                for (var i = 0; i < planet.DockedShips.Count; i++)
                {
                    var ship = planet.DockedShips[i];
                    if (ship.Kind != Ship.ShipKind.WarShip || ship.Owner == Planet.NoOwner) continue;
                    var health = stats.CurrentHealth(ship);
                    if (health <= 0f) continue;
                    var offense = stats.EffectiveOffense(ship);
                    var defense = stats.Defense(ship.Template, ship.ResearchSnapshot);
                    units.Add(new Unit
                    {
                        Ship = ship, Owner = ship.Owner, Index = i, Health = health,
                        Max = stats.Health(ship.Template, ship.ResearchSnapshot),
                        Defense = defense, Offense = offense, Strength = offense * (health + defense),
                    });
                }
                var players = units.Select(u => u.Owner).Distinct().OrderBy(p => p).ToList();
                if (players.Count < 2) return;

                // pools[victim][attacker]: the offense an attacker aims at a victim (hostility-weighted over its war rivals).
                var pools = new Dictionary<int, Dictionary<int, float>>();
                foreach (var attacker in players)
                {
                    var pool = units.Where(u => u.Owner == attacker).Sum(u => u.Offense);
                    if (pool <= 0f) continue;
                    var targets = players.Where(v => v != attacker && map.Diplomacy.IsAtWar(attacker, v, true)).ToList();
                    if (targets.Count == 0) continue;
                    var weights = targets.ToDictionary(v => v, v => Math.Max(1f, map.Diplomacy.Hostility(attacker, v)));
                    var total = weights.Values.Sum();
                    foreach (var victim in targets)
                    {
                        if (!pools.ContainsKey(victim)) pools[victim] = new Dictionary<int, float>();
                        pools[victim][attacker] = pool * weights[victim] / total;
                    }
                }

                var damage = new Dictionary<Ship, float>();
                var destroyed = new List<Ship>();
                foreach (var victim in pools.Keys.OrderBy(v => v))
                {
                    var contributions = pools[victim];
                    var pool = contributions.Values.Sum();    // one pool per victim: the order of the attackers cannot matter
                    var lostShips = 0;
                    var lostStrength = 0f;
                    var dealt = 0f;
                    var ordered = units.Where(u => u.Owner == victim).OrderBy(u => u.Health / u.Max).ThenBy(u => u.Index);
                    foreach (var target in ordered)
                    {
                        if (pool <= 0f) break;
                        var factor = constants.combatDamageK / (constants.combatDamageK + target.Defense);
                        var needed = target.Health / factor;
                        if (pool >= needed)
                        {
                            destroyed.Add(target.Ship);
                            lostShips++;
                            lostStrength += target.Strength;
                            dealt += target.Health;
                            pool -= needed;
                        }
                        else
                        {
                            damage[target.Ship] = (damage.TryGetValue(target.Ship, out var d) ? d : 0f) + pool * factor;
                            dealt += pool * factor;
                            pool = 0f;
                        }
                    }

                    var totalContribution = contributions.Values.Sum();
                    foreach (var attacker in contributions.Keys.OrderBy(a => a))
                    {
                        var share = contributions[attacker] / totalContribution;
                        reports.Add(new CombatReport
                        {
                            Planet = planet.PlanetName, Attacker = attacker, Victim = victim,
                            DamageDealt = dealt * share, ShipsDestroyed = lostShips * share,
                        });
                        if (lostShips > 0)
                            results.Add(new Planet.PlanetUpdateResult(planet.PlanetName,
                                Planet.PlanetUpdateResult.PlanetUpdateResultType.PlanetUpdateResultTypeWarshipsLost,
                                new CombatLoss { Attacker = attacker, Ships = lostShips * share, StrengthLost = lostStrength * share },
                                victim));
                    }
                }

                foreach (var pair in damage) pair.Key.Damage += pair.Value;    // applied after every side was computed
                foreach (var ship in destroyed) planet.DestroyDockedShip(ship);
            }

            // After combat, a damaged warship heals repairFractionPerTurn of its Health stat while docked at a planet its owner
            // populates with no warship of a player it is at war with there.
            private static void Repair(Planet planet, GameAIMap map, WarshipStats stats, GameAIConstants constants)
            {
                if (constants.repairFractionPerTurn <= 0f) return;
                foreach (var ship in planet.DockedShips)
                {
                    if (ship.Kind != Ship.ShipKind.WarShip || ship.Owner == Planet.NoOwner || ship.Damage <= 0f) continue;
                    if (planet.Owner != ship.Owner || planet.Population.Count == 0) continue;
                    var enemyHere = planet.DockedShips.Any(s => s.Kind == Ship.ShipKind.WarShip && s.Owner != Planet.NoOwner
                        && s.Owner != ship.Owner && map.Diplomacy.IsAtWar(ship.Owner, s.Owner, true));
                    if (enemyHere) continue;
                    var max = stats.Health(ship.Template, ship.ResearchSnapshot);
                    ship.Damage = Math.Max(0f, ship.Damage - constants.repairFractionPerTurn * max);
                }
            }

            // After combat and repair: a player with colony ships here but no warship, where a player at war with it has a
            // warship, loses its colony ships. Everywhere, a planet the owner populates included.
            private static void DestroyStrandedColonyShips(Planet planet, GameAIMap map, List<Planet.PlanetUpdateResult> results)
            {
                var owners = planet.DockedShips.Where(s => s.Kind == Ship.ShipKind.ColonyShip && s.Owner != Planet.NoOwner)
                    .Select(s => s.Owner).Distinct().OrderBy(o => o).ToList();
                foreach (var owner in owners)
                {
                    if (planet.DockedShips.Any(s => s.Kind == Ship.ShipKind.WarShip && s.Owner == owner)) continue;
                    var raiders = planet.DockedShips
                        .Where(s => s.Kind == Ship.ShipKind.WarShip && s.Owner != Planet.NoOwner && s.Owner != owner
                                    && map.Diplomacy.IsAtWar(owner, s.Owner, true))
                        .Select(s => s.Owner).Distinct().OrderBy(o => o).ToList();
                    if (raiders.Count == 0) continue;
                    var count = planet.UndockShips(Ship.ShipKind.ColonyShip, owner, int.MaxValue);
                    results.Add(new Planet.PlanetUpdateResult(planet.PlanetName,
                        Planet.PlanetUpdateResult.PlanetUpdateResultType.PlanetUpdateResultTypeColonyShipsLost,
                        new ColonyLoss { Count = count, ByPlayer = raiders[0] }, owner));
                }
            }
        }
    }
}
