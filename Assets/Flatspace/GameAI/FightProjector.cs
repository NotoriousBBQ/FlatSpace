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

        /// <summary>The same fight with the rival seen at `Factor` of its offense and health (imperfect intel).</summary>
        public struct Sample
        {
            public float Factor;
            public float LossFraction;
            public bool RivalSurvives;
            public bool Wiped;
        }
        public List<Sample> Samples = new List<Sample>();

        /// <summary>The mean projected loss over the samples (the exact loss when there are none): what the retreat decision reads.</summary>
        public float PerceivedLossFraction => Samples.Count == 0 ? ProjectedLossFraction : Samples.Average(s => s.LossFraction);
        /// <summary>The share of samples where a war rival is still standing at the end (1 or 0 from the exact projection when there are none).</summary>
        public float PerceivedRivalSurvivesShare => Samples.Count == 0 ? (RivalSurvives ? 1f : 0f) : Samples.Count(s => s.RivalSurvives) / (float)Samples.Count;
        /// <summary>The share of samples where my group is wiped out (1 or 0 from the exact projection when there are none).</summary>
        public float PerceivedWipedShare => Samples.Count == 0 ? (MyGroupWiped ? 1f : 0f) : Samples.Count(s => s.Wiped) / (float)Samples.Count;

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
        /// <summary>
        /// The exact projection plus retreatUncertaintySamples samples of the same fight with the rival seen at 1 +- retreatRivalUncertainty
        /// (one factor per sample, stratified over the range with one random phase from `rand`, so the samples always straddle 1). With
        /// no `rand`, no samples or no uncertainty the exact projection is returned alone. Null when there is no fight.
        /// </summary>
        public static FightProjection ProjectPerceived(GameAIMap map, Planet planet, int player, WarshipStats stats,
            GameAIConstants constants, IEnumerable<GameAI.GameAIOrder> orders, Random rand)
        {
            var orderList = orders as IList<GameAI.GameAIOrder> ?? orders?.ToList();
            var exact = Project(map, planet, player, stats, constants, orderList);
            if (exact == null) return null;
            var n = constants.retreatUncertaintySamples;
            var uncertainty = constants.retreatRivalUncertainty;
            if (rand == null || n <= 0 || uncertainty <= 0f) return exact;
            var phase = rand.NextDouble();
            for (var i = 0; i < n; i++)
            {
                var factor = 1f + uncertainty * (float)(2.0 * (i + phase) / n - 1.0);
                var sample = Project(map, planet, player, stats, constants, orderList, factor);
                exact.Samples.Add(new FightProjection.Sample
                {
                    Factor = factor, LossFraction = sample.ProjectedLossFraction,
                    RivalSurvives = sample.RivalSurvives, Wiped = sample.MyGroupWiped,
                });
            }
            return exact;
        }

        /// <summary>`rivalFactor` scales the offense and health of every war rival's ship (damage scales with it, so health fractions are unchanged).</summary>
        public static FightProjection Project(GameAIMap map, Planet planet, int player, WarshipStats stats,
            GameAIConstants constants, IEnumerable<GameAI.GameAIOrder> orders, float rivalFactor = 1f)
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

            // Imperfect intel: the rival seen at `rivalFactor` of its offense and health (the same health fraction, the same Defense).
            if (rivalFactor != 1f)
                units = units.Select(u => rivals.Contains(u.Owner)
                    ? CombatUnit.Make(u.Id, u.Owner, u.Index, u.BaseOffense * rivalFactor, u.Max * rivalFactor, u.Defense, (u.Max - u.Health) * rivalFactor)
                    : u).ToList();

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
