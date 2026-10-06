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

        /// <summary>One warship's state in a fight, pure values so a projection can copy and advance it.</summary>
        public struct CombatUnit
        {
            public int Id;            // the caller's handle (the dock index at the planet, or a projection's own counter)
            public int Owner;
            public int Index;         // dock order, the focus-fire tie-break
            public float Health;      // current health (Health stat minus damage, never below 0)
            public float Max;         // the Health stat
            public float Defense;
            public float BaseOffense; // the Offense stat before damage
            public float Offense;     // effective: BaseOffense x Health / Max, 0 for a Max of 0 (the WarshipStats formula)
            public float Strength;    // Offense x (Health + Defense)

            public static CombatUnit Make(int id, int owner, int index, float baseOffense, float max, float defense, float damage)
            {
                var health = Math.Max(0f, max - damage);
                var offense = max <= 0f ? 0f : baseOffense * health / max;
                return new CombatUnit
                {
                    Id = id, Owner = owner, Index = index, Health = health, Max = max, Defense = defense,
                    BaseOffense = baseOffense, Offense = offense, Strength = offense * (health + defense),
                };
            }

            /// <summary>The same unit after taking `more` further damage.</summary>
            public CombatUnit WithDamage(float more)
                => Make(Id, Owner, Index, BaseOffense, Max, Defense, (Max - Health) + more);
        }

        /// <summary>What one attacker did to one victim in a round. Ships and StrengthLost are the attacker's share of what the victim lost; Engaged is the same share of the victim's strength at the start of the round.</summary>
        public struct RoundLoss
        {
            public int Victim;
            public int Attacker;
            public float DamageDealt;
            public float Ships;
            public float StrengthLost;
            public float Engaged;
        }

        public class RoundOutcome
        {
            public Dictionary<int, float> Damage = new Dictionary<int, float>();   // unit Id -> damage taken this round
            public List<int> Destroyed = new List<int>();                          // unit Ids
            public List<RoundLoss> Losses = new List<RoundLoss>();                 // victim ascending, then attacker ascending
        }

        /// <summary>
        /// Docked warships of players at war that share a planet fight once a turn: deterministic, simultaneous, focus fire
        /// on the most damaged ship, damage persistent on the ship (Ship.Damage). Pure (no Gameboard.Instance). Off in legacy
        /// mode (no stances to fight on). Appends WarshipsLost and ColonyShipsLost results, like Planet.UpdatePlanet does.
        /// </summary>
        public static class CombatSystem
        {
            public static List<CombatReport> Resolve(GameAIMap map, WarshipStats stats, GameAIConstants constants, int turn,
                List<Planet.UpdateResult> results)
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

            /// <summary>
            /// One round of a fight, pure: hostility-weighted pools per attacker, ONE pool per victim, focus fire on the lowest
            /// health fraction (then dock index), a hit scaled by K / (K + Defense), simultaneous. Nothing is mutated; the caller
            /// applies the outcome. Units with no health are ignored by the caller (Make gives them 0 offense and 0 strength).
            /// </summary>
            public static RoundOutcome Round(List<CombatUnit> units, Func<int, int, bool> atWar, Func<int, int, float> hostility, float k)
            {
                var outcome = new RoundOutcome();
                var players = units.Select(u => u.Owner).Distinct().OrderBy(p => p).ToList();
                if (players.Count < 2) return outcome;

                // pools[victim][attacker]: the offense an attacker aims at a victim (hostility-weighted over its war rivals).
                var pools = new Dictionary<int, Dictionary<int, float>>();
                foreach (var attacker in players)
                {
                    var pool = units.Where(u => u.Owner == attacker).Sum(u => u.Offense);
                    if (pool <= 0f) continue;
                    var targets = players.Where(v => v != attacker && atWar(attacker, v)).ToList();
                    if (targets.Count == 0) continue;
                    var weights = targets.ToDictionary(v => v, v => Math.Max(1f, hostility(attacker, v)));
                    var total = weights.Values.Sum();
                    foreach (var victim in targets)
                    {
                        if (!pools.ContainsKey(victim)) pools[victim] = new Dictionary<int, float>();
                        pools[victim][attacker] = pool * weights[victim] / total;
                    }
                }

                foreach (var victim in pools.Keys.OrderBy(v => v))
                {
                    var contributions = pools[victim];
                    var pool = contributions.Values.Sum();    // one pool per victim: the order of the attackers cannot matter
                    var engaged = units.Where(u => u.Owner == victim).Sum(u => u.Strength);
                    var lostShips = 0;
                    var lostStrength = 0f;
                    var dealt = 0f;
                    var ordered = units.Where(u => u.Owner == victim).OrderBy(u => u.Health / u.Max).ThenBy(u => u.Index);
                    foreach (var target in ordered)
                    {
                        if (pool <= 0f) break;
                        var factor = k / (k + target.Defense);
                        var needed = target.Health / factor;
                        if (pool >= needed)
                        {
                            outcome.Destroyed.Add(target.Id);
                            lostShips++;
                            lostStrength += target.Strength;
                            dealt += target.Health;
                            pool -= needed;
                        }
                        else
                        {
                            outcome.Damage[target.Id] = (outcome.Damage.TryGetValue(target.Id, out var d) ? d : 0f) + pool * factor;
                            dealt += pool * factor;
                            pool = 0f;
                        }
                    }

                    var totalContribution = contributions.Values.Sum();
                    foreach (var attacker in contributions.Keys.OrderBy(a => a))
                    {
                        var share = contributions[attacker] / totalContribution;
                        outcome.Losses.Add(new RoundLoss
                        {
                            Victim = victim, Attacker = attacker, DamageDealt = dealt * share, Ships = lostShips * share,
                            StrengthLost = lostStrength * share, Engaged = engaged * share,
                        });
                    }
                }
                return outcome;
            }

            private static void FightAt(Planet planet, GameAIMap map, WarshipStats stats, GameAIConstants constants,
                List<Planet.UpdateResult> results, List<CombatReport> reports)
            {
                // Start-of-turn snapshot of every warship that can fight (a ship with no health left, or no Health stat, cannot).
                var units = new List<CombatUnit>();
                for (var i = 0; i < planet.DockedShips.Count; i++)
                {
                    var ship = planet.DockedShips[i];
                    if (ship.Kind != Ship.ShipKind.WarShip || ship.Owner == Planet.NoOwner) continue;
                    if (stats.CurrentHealth(ship) <= 0f) continue;
                    units.Add(CombatUnit.Make(i, ship.Owner, i, stats.Offense(ship.Template, ship.ResearchSnapshot),
                        stats.Health(ship.Template, ship.ResearchSnapshot), stats.Defense(ship.Template, ship.ResearchSnapshot), ship.Damage));
                }

                var outcome = Round(units, (a, b) => map.Diplomacy.IsAtWar(a, b, true), (a, b) => map.Diplomacy.Hostility(a, b),
                    constants.combatDamageK);
                foreach (var loss in outcome.Losses)
                {
                    reports.Add(new CombatReport
                    {
                        Planet = planet.PlanetName, Attacker = loss.Attacker, Victim = loss.Victim,
                        DamageDealt = loss.DamageDealt, ShipsDestroyed = loss.Ships,
                    });
                    if (loss.Ships > 0f)
                        results.Add(new Planet.UpdateResult(planet.PlanetName,
                            Planet.UpdateResult.UpdateResultType.UpdateResultTypeWarshipsLost,
                            new CombatLoss { Attacker = loss.Attacker, Ships = loss.Ships, StrengthLost = loss.StrengthLost },
                            loss.Victim));
                }
                foreach (var pair in outcome.Damage) planet.DockedShips[pair.Key].Damage += pair.Value;   // applied after every side was computed
                var doomed = outcome.Destroyed.Select(id => planet.DockedShips[id]).ToList();
                foreach (var ship in doomed) planet.DestroyDockedShip(ship);
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
            private static void DestroyStrandedColonyShips(Planet planet, GameAIMap map, List<Planet.UpdateResult> results)
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
                    results.Add(new Planet.UpdateResult(planet.PlanetName,
                        Planet.UpdateResult.UpdateResultType.UpdateResultTypeColonyShipsLost,
                        new ColonyLoss { Count = count, ByPlayer = raiders[0] }, owner));
                }
            }
        }
    }
}
