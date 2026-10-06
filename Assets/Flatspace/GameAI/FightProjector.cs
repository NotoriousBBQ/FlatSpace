using System;
using System.Collections.Generic;
using System.Linq;

namespace FlatSpace.AI
{
    /// <summary>
    /// What one player's group at one fight planet is projected to come to: its share of strength lost, and whether a war rival
    /// is still standing, after the fight is played to its end (carried in a FightProjection result's Data).
    /// </summary>
    public class FightProjection
    {
        public string Planet;
        public int Player;
        public int MyShips;                 // my warships docked there that can fight (health > 0)
        public int InboundShips;            // my own ships that join within the projection
        public float MyStrength;            // strength of every ship of mine that takes part (docked + inbound), at its entry state
        public float MyStrengthLeft;        // strength of my survivors at the end (damage lowers it: a wounded survivor counts as partly lost)
        public float RivalStrength;         // war rivals' strength at the start
        public float RivalStrengthLeft;
        public bool RivalSurvives;          // a player I am at war with still has a warship at the end
        public int Turns;                   // rounds projected
        public List<int> Rivals = new List<int>();   // war rivals with warships at the planet, ascending

        public bool MyGroupWiped => MyStrengthLeft <= 0f;
        public float ProjectedLossFraction
            => MyStrength <= 0f ? 0f : Math.Min(1f, Math.Max(0f, 1f - MyStrengthLeft / MyStrength));
        public float RivalSurvivorsFraction => RivalStrength <= 0f ? 0f : Math.Min(1f, RivalStrengthLeft / RivalStrength);
    }

    /// <summary>
    /// Plays a planet's fight forward round by round with the same core real combat uses (CombatSystem.Round), on a copy of the
    /// start-of-turn state: every docked warship at the planet, my own in-flight ships landing there on their known arrival turn.
    /// Rival in-flight ships and repair (which never happens while an at-war warship is present) are not modeled. Pure.
    /// </summary>
    public static class FightProjector
    {
        public static FightProjection Project(GameAIMap map, Planet planet, int player, WarshipStats stats,
            GameAIConstants constants, IEnumerable<GameAI.GameAIOrder> orders)
        {
            if (map == null || planet == null || stats == null || !map.Diplomacy.Enabled) return null;

            var units = new List<CombatUnit>();
            var nextId = 0;
            for (var i = 0; i < planet.DockedShips.Count; i++)
            {
                var ship = planet.DockedShips[i];
                if (ship.Kind != Ship.ShipKind.WarShip || ship.Owner == Planet.NoOwner) continue;
                if (stats.CurrentHealth(ship) <= 0f) continue;
                units.Add(CombatUnit.Make(nextId++, ship.Owner, i, stats.Offense(ship.Template, ship.ResearchSnapshot),
                    stats.Health(ship.Template, ship.ResearchSnapshot), stats.Defense(ship.Template, ship.ResearchSnapshot), ship.Damage));
            }

            Func<int, int, bool> atWar = (a, b) => map.Diplomacy.IsAtWar(a, b, true);
            var rivals = units.Select(u => u.Owner).Distinct().Where(o => o != player && atWar(player, o)).OrderBy(o => o).ToList();
            if (rivals.Count == 0 || !units.Any(u => u.Owner == player)) return null;

            // My own fleets heading here: they join before the combat of the projected round equal to their delay.
            var inbound = new List<(int round, CombatUnit unit)>();
            var index = planet.DockedShips.Count;
            foreach (var order in orders ?? Enumerable.Empty<GameAI.GameAIOrder>())
            {
                if (order.Type != GameAI.GameAIOrder.OrderType.OrderTypeShipTransport || order.PlayerId != player) continue;
                if (order.Target != planet.PlanetName || order.Fleet == null || order.Fleet.Kind != Ship.ShipKind.WarShip) continue;
                var round = Math.Max(1, order.TimingDelay);
                if (round > constants.retreatProjectionTurns) continue;
                var template = constants.warShipData;
                for (var i = 0; i < order.Fleet.Snapshots.Count; i++)
                {
                    var snapshot = order.Fleet.Snapshots[i];
                    var unit = CombatUnit.Make(nextId++, player, index++, stats.Offense(template, snapshot),
                        stats.Health(template, snapshot), stats.Defense(template, snapshot), order.Fleet.DamageAt(i));
                    if (unit.Health > 0f) inbound.Add((round, unit));
                }
            }

            var projection = new FightProjection
            {
                Planet = planet.PlanetName, Player = player, Rivals = rivals,
                MyShips = units.Count(u => u.Owner == player),
                MyStrength = units.Where(u => u.Owner == player).Sum(u => u.Strength),
                RivalStrength = units.Where(u => rivals.Contains(u.Owner)).Sum(u => u.Strength),
            };

            Func<int, int, float> hostility = (a, b) => map.Diplomacy.Hostility(a, b);
            for (var round = 1; round <= constants.retreatProjectionTurns; round++)
            {
                foreach (var arrival in inbound.Where(a => a.round == round))
                {
                    units.Add(arrival.unit);
                    projection.InboundShips++;
                    projection.MyStrength += arrival.unit.Strength;
                }
                if (!units.Any(u => u.Owner == player) || !units.Any(u => rivals.Contains(u.Owner))) break;

                var outcome = CombatSystem.Round(units, atWar, hostility, constants.combatDamageK);
                if (outcome.Damage.Count == 0 && outcome.Destroyed.Count == 0) break;   // nobody can hurt anybody: the fight is over
                units = units.Where(u => !outcome.Destroyed.Contains(u.Id))
                    .Select(u => outcome.Damage.TryGetValue(u.Id, out var damage) ? u.WithDamage(damage) : u).ToList();
                projection.Turns = round;
            }

            projection.MyStrengthLeft = units.Where(u => u.Owner == player).Sum(u => u.Strength);
            projection.RivalStrengthLeft = units.Where(u => rivals.Contains(u.Owner)).Sum(u => u.Strength);
            projection.RivalSurvives = units.Any(u => rivals.Contains(u.Owner));
            return projection;
        }
    }
}
