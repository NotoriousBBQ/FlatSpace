using System;
using System.Collections.Generic;
using System.Linq;

namespace FlatSpace.AI
{
    /// <summary>One line of the tuning log: what one bombarding player and one bunker owner did to each other at a planet this turn.</summary>
    public struct BombardReport
    {
        public string Planet;
        public int Attacker;            // the player whose ships bombarded
        public int Victim;              // the bunker owner
        public float BombardDealt;      // damage the attacker's bombard did to the victim's bunkers
        public float BunkersDestroyed;  // the attacker's share of the bunkers destroyed
        public float ShipDamage;        // damage the victim's bunkers did to the attacker's ships
        public int BunkersLeft;         // the victim's bunkers on the planet after the step
    }

    /// <summary>Both directions of one bombardment round, computed from the same start-of-step units.</summary>
    public class BombardOutcome
    {
        public RoundOutcome ShipHits = new RoundOutcome();     // bunker attack on the attackers' ships (Losses: victim = ship owner)
        public RoundOutcome BunkerHits = new RoundOutcome();   // the attackers' bombard on the bunkers (Losses: victim = bunker owner)
    }

    /// <summary>
    /// The bombardment step, run after ship-to-ship combat each turn (spec "Combat: a separate bombardment step"). On a planet where
    /// a bunker owner has bunkers and no docked warship or siegeship, and a player at war with it has docked warships or siegeships,
    /// the attackers' effective bombard damages the bunkers and the bunkers' attack damages the attackers' ships, simultaneously
    /// (both from one start-of-step snapshot, applied after both). Bunkers fire only here. Then bunker repair. Pure (no
    /// Gameboard.Instance); off in legacy mode, like CombatSystem.
    /// </summary>
    public static class BombardmentSystem
    {
        public static List<BombardReport> Resolve(GameAIMap map, WarshipStats stats, GameAIConstants constants,
            List<Planet.UpdateResult> results)
        {
            var reports = new List<BombardReport>();
            if (map == null || stats == null || !map.Diplomacy.Enabled) return reports;
            foreach (var planet in map.PlanetList)
            {
                var hit = BombardAt(planet, map, stats, constants, results, reports);
                RepairBunkers(planet, map, stats, constants, hit);
            }
            return reports;
        }

        /// <summary>
        /// Bombardment happens here against this bunker owner: it has bunkers, no docked warship or siegeship, and a player at war
        /// with it (IsAtWar, docked together counts as contact) has a docked warship or siegeship.
        /// </summary>
        public static bool Triggers(Planet planet, int bunkerOwner, GameAIMap map)
        {
            if (planet == null || bunkerOwner == Planet.NoOwner || planet.BunkerCount(bunkerOwner) == 0) return false;
            if (planet.DockedShips.Any(s => Ship.IsFightingKind(s.Kind) && s.Owner == bunkerOwner)) return false;
            return planet.DockedShips.Any(s => Ship.IsFightingKind(s.Kind) && s.Owner != Planet.NoOwner && s.Owner != bunkerOwner
                                               && map.Diplomacy.IsAtWar(s.Owner, bunkerOwner, true));
        }

        /// <summary>
        /// The planet is dominated against this bunker owner by an at-war rival, so its bunkers do not repair. The dominance rule is the
        /// single one in ConversionSystem.Dominator (a player with docked warships/siegeships of effective bombard above 0 dominates
        /// unless another player it is at war with has a docked warship or siegeship, so a bunker owner's own ship, siegeship
        /// included, blocks; of two that are not at war, more docked bombard, then the lower id).
        /// </summary>
        public static bool Dominated(Planet planet, int bunkerOwner, GameAIMap map, WarshipStats stats)
        {
            var dominator = ConversionSystem.Dominator(planet, map, stats);
            return dominator != Planet.NoOwner && dominator != bunkerOwner && map.Diplomacy.IsAtWar(dominator, bunkerOwner, true);
        }

        /// <summary>
        /// One bombardment round, pure and simultaneous, reusing CombatSystem.Round twice over the same start-of-step units:
        /// (a) the bunkers fire (their Offense) at the ships, whose Offense is zeroed (their Strength is kept, so the loss terms are
        /// the ship strength the fight lost, as in ship combat); (b) the ships fire their Bombard at the bunkers, whose Offense is
        /// zeroed. In (a) only a bunker owner attacks and only a non-bunker owner is a victim; in (b) the reverse, so two attackers at
        /// war with each other never bombard each other's ships. `ships` are the attackers' warship/siegeship units (Class 0/1,
        /// Bombard set), `bunkers` the bunker units (Class 2); unit Ids must not collide between the two lists.
        /// </summary>
        public static BombardOutcome Round(List<CombatUnit> ships, List<CombatUnit> bunkers, Func<int, int, bool> atWar,
            Func<int, int, float> hostility, float k)
        {
            var outcome = new BombardOutcome();
            if (ships == null || bunkers == null || ships.Count == 0 || bunkers.Count == 0) return outcome;
            var bunkerOwners = new HashSet<int>(bunkers.Select(b => b.Owner));

            var shipTargets = new List<CombatUnit>(bunkers);
            shipTargets.AddRange(ships.Select(s => { var unit = s; unit.Offense = 0f; return unit; }));
            outcome.ShipHits = CombatSystem.Round(shipTargets,
                (a, b) => bunkerOwners.Contains(a) && !bunkerOwners.Contains(b) && atWar(a, b), hostility, k);

            var bunkerTargets = ships.Select(s => { var unit = s; unit.Offense = s.Bombard; return unit; }).ToList();
            bunkerTargets.AddRange(bunkers.Select(b => { var unit = b; unit.Offense = 0f; return unit; }));
            outcome.BunkerHits = CombatSystem.Round(bunkerTargets,
                (a, b) => !bunkerOwners.Contains(a) && bunkerOwners.Contains(b) && atWar(a, b), hostility, k);
            return outcome;
        }

        /// <summary>
        /// The bombardment step on a projection's own unit state (FightProjector), pure: the same trigger as Triggers (a bunker owner with
        /// bunkers, no fighting ship of its own and a fighting ship of a player at war with it) and the same participants as the real
        /// step (the bunkers of every such owner; every fighting ship whose owner is at war with one of them), then Round. `ships` are
        /// the fighting units (warships/siegeships, Bombard set), `bunkers` the Class 2 units; Ids distinct across both. Empty outcome
        /// when nothing triggers.
        /// </summary>
        public static BombardOutcome Step(List<CombatUnit> ships, List<CombatUnit> bunkers, Func<int, int, bool> atWar,
            Func<int, int, float> hostility, float k)
        {
            if (ships == null || bunkers == null || ships.Count == 0 || bunkers.Count == 0) return new BombardOutcome();
            var exposed = bunkers.Select(b => b.Owner).Distinct()
                .Where(o => !ships.Any(s => s.Owner == o) && ships.Any(s => s.Owner != o && atWar(s.Owner, o))).ToList();
            if (exposed.Count == 0) return new BombardOutcome();
            var targets = bunkers.Where(b => exposed.Contains(b.Owner)).ToList();
            var attackers = ships.Where(s => exposed.Any(o => atWar(s.Owner, o))).ToList();
            return Round(attackers, targets, atWar, hostility, k);
        }

        // Returns the bunkers that took damage this turn (destroyed ones are gone), for the repair rule.
        private static HashSet<Ship> BombardAt(Planet planet, GameAIMap map, WarshipStats stats, GameAIConstants constants,
            List<Planet.UpdateResult> results, List<BombardReport> reports)
        {
            var hit = new HashSet<Ship>();
            var bunkerOwners = planet.DockedShips.Where(s => s.Kind == Ship.ShipKind.Bunker && s.Owner != Planet.NoOwner)
                .Select(s => s.Owner).Distinct().Where(o => Triggers(planet, o, map)).OrderBy(o => o).ToList();
            if (bunkerOwners.Count == 0) return hit;

            bool AtWarWithABunkerOwner(int owner) => bunkerOwners.Any(b => map.Diplomacy.IsAtWar(owner, b, true));

            // Start-of-step snapshot (dock index as Id). A unit with no health, or no Health stat, takes no part.
            var ships = new List<CombatUnit>();
            var bunkers = new List<CombatUnit>();
            for (var i = 0; i < planet.DockedShips.Count; i++)
            {
                var ship = planet.DockedShips[i];
                if (ship.Owner == Planet.NoOwner || stats.CurrentHealth(ship) <= 0f) continue;
                if (ship.Kind == Ship.ShipKind.Bunker)
                {
                    if (!bunkerOwners.Contains(ship.Owner)) continue;
                    bunkers.Add(CombatUnit.Make(i, ship.Owner, i, stats.Offense(ship), stats.Health(ship), stats.Defense(ship),
                        ship.Damage, 2));
                }
                else if (Ship.IsFightingKind(ship.Kind) && AtWarWithABunkerOwner(ship.Owner))
                {
                    var subtype = WarshipStats.SubtypeOf(ship.Kind);
                    ships.Add(CombatUnit.Make(i, ship.Owner, i, stats.Offense(ship), stats.Health(ship), stats.Defense(ship),
                        ship.Damage, ship.Kind == Ship.ShipKind.SiegeShip ? 1 : 0,
                        stats.Bombard(ship.Template, ship.ResearchSnapshot, subtype)));
                }
            }
            if (ships.Count == 0 || bunkers.Count == 0) return hit;

            var outcome = Round(ships, bunkers, (a, b) => map.Diplomacy.IsAtWar(a, b, true),
                (a, b) => map.Diplomacy.Hostility(a, b), constants.combatDamageK);

            // Ship losses feed hostility and surrender like ship combat (victim = ship owner, attacker = bunker owner); bunker losses do not.
            foreach (var loss in outcome.ShipHits.Losses)
            {
                results.Add(new Planet.UpdateResult(planet.PlanetName,
                    Planet.UpdateResult.UpdateResultType.UpdateResultTypeWarshipsLost,
                    new CombatLoss
                    {
                        Attacker = loss.Attacker, Ships = loss.Ships, StrengthLost = loss.StrengthLost,
                        StrengthDrop = loss.StrengthDrop, Engaged = loss.Engaged,
                    },
                    loss.Victim));
            }

            // Applied after both directions were computed (simultaneous). Ships and bunkers have distinct Ids (dock indexes).
            foreach (var pair in outcome.ShipHits.Damage) planet.DockedShips[pair.Key].Damage += pair.Value;
            foreach (var pair in outcome.BunkerHits.Damage)
            {
                var bunker = planet.DockedShips[pair.Key];
                bunker.Damage += pair.Value;
                hit.Add(bunker);
            }
            var doomed = outcome.ShipHits.Destroyed.Concat(outcome.BunkerHits.Destroyed).Distinct()
                .Select(id => planet.DockedShips[id]).ToList();
            foreach (var ship in doomed)
            {
                hit.Remove(ship);
                planet.DestroyDockedShip(ship);
            }

            // One report per (attacker, bunker owner) pair at war, every bombardment turn, zeros included.
            var attackers = ships.Select(s => s.Owner).Distinct().OrderBy(o => o).ToList();
            foreach (var victim in bunkerOwners)
            {
                foreach (var attacker in attackers)
                {
                    if (!map.Diplomacy.IsAtWar(attacker, victim, true)) continue;
                    var dealt = outcome.BunkerHits.Losses.Where(l => l.Attacker == attacker && l.Victim == victim).ToList();
                    var taken = outcome.ShipHits.Losses.Where(l => l.Attacker == victim && l.Victim == attacker).ToList();
                    reports.Add(new BombardReport
                    {
                        Planet = planet.PlanetName, Attacker = attacker, Victim = victim,
                        BombardDealt = dealt.Sum(l => l.DamageDealt), BunkersDestroyed = dealt.Sum(l => l.Ships),
                        ShipDamage = taken.Sum(l => l.DamageDealt), BunkersLeft = planet.BunkerCount(victim),
                    });
                }
            }
            return hit;
        }

        // After the step: a damaged bunker that took no damage this turn heals repairFractionPerTurn of its Health stat, unless the
        // planet is dominated against its owner by an at-war rival.
        private static void RepairBunkers(Planet planet, GameAIMap map, WarshipStats stats, GameAIConstants constants, HashSet<Ship> hit)
        {
            if (constants.repairFractionPerTurn <= 0f) return;
            var dominated = new Dictionary<int, bool>();
            foreach (var bunker in planet.DockedShips)
            {
                if (bunker.Kind != Ship.ShipKind.Bunker || bunker.Owner == Planet.NoOwner || bunker.Damage <= 0f) continue;
                if (hit.Contains(bunker)) continue;
                if (!dominated.TryGetValue(bunker.Owner, out var isDominated))
                    dominated[bunker.Owner] = isDominated = Dominated(planet, bunker.Owner, map, stats);
                if (isDominated) continue;
                bunker.Damage = Math.Max(0f, bunker.Damage - constants.repairFractionPerTurn * stats.Health(bunker));
            }
        }
    }
}
