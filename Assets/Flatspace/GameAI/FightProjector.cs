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
        public float RivalStrength;         // war rivals' strength at the start (their fighting ships and their bunkers)
        public float RivalStrengthLeft;
        public bool RivalSurvives;          // a player I am at war with still has a fighting ship or a bunker at the end
        public int Turns;                   // rounds projected
        public List<int> Rivals = new List<int>();   // war rivals with fighting ships or bunkers at the planet, ascending

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
    /// start-of-turn state: every docked warship and siegeship at the planet, my own in-flight ships landing there on their known
    /// arrival turn. Each projected turn the ship-to-ship round is followed by the real bombardment step (BombardmentSystem.Step: same
    /// trigger, same arithmetic) against the docked bunkers, whose damage carries across the projected turns. Rival in-flight ships
    /// and repair (ships and bunkers) are not modeled. My group may be inbound only (nothing of mine docked yet). Pure.
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

        /// <summary>`rivalFactor` scales the offense, bombard and health of every war rival's ship and bunker (damage scales with it, so health fractions are unchanged).</summary>
        public static FightProjection Project(GameAIMap map, Planet planet, int player, WarshipStats stats,
            GameAIConstants constants, IEnumerable<GameAI.GameAIOrder> orders, float rivalFactor = 1f)
        {
            if (map == null || planet == null || stats == null || !map.Diplomacy.Enabled) return null;

            // Fighting ships (warships/siegeships, with their bombard) and bunkers (Class 2) as separate unit lists: bunkers take no part
            // in the ship-to-ship round, only in the bombardment step. Ids come from one counter, so they never collide across the two.
            var units = new List<CombatUnit>();
            var bunkers = new List<CombatUnit>();
            var nextId = 0;
            for (var i = 0; i < planet.DockedShips.Count; i++)
            {
                var ship = planet.DockedShips[i];
                if (ship.Owner == Planet.NoOwner) continue;
                if (ship.Kind == Ship.ShipKind.Bunker)
                {
                    if (stats.CurrentHealth(ship) <= 0f) continue;
                    bunkers.Add(CombatUnit.Make(nextId++, ship.Owner, i, stats.Offense(ship), stats.Health(ship), stats.Defense(ship), ship.Damage, 2));
                    continue;
                }
                if (!Ship.IsFightingKind(ship.Kind)) continue;
                if (stats.CurrentHealth(ship) <= 0f) continue;
                units.Add(CombatUnit.Make(nextId++, ship.Owner, i, stats.Offense(ship), stats.Health(ship), stats.Defense(ship), ship.Damage,
                    ship.Kind == Ship.ShipKind.SiegeShip ? 1 : 0, stats.Bombard(ship.Template, ship.ResearchSnapshot, WarshipStats.SubtypeOf(ship.Kind))));
            }

            Func<int, int, bool> atWar = (a, b) => map.Diplomacy.IsAtWar(a, b, true);
            var rivals = units.Select(u => u.Owner).Concat(bunkers.Select(b => b.Owner)).Distinct()
                .Where(o => o != player && atWar(player, o)).OrderBy(o => o).ToList();
            if (rivals.Count == 0) return null;

            // Imperfect intel: the rival seen at `rivalFactor` of its offense, bombard and health (the same health fraction, the same
            // Defense), its bunkers included.
            if (rivalFactor != 1f)
            {
                CombatUnit Scaled(CombatUnit u) => rivals.Contains(u.Owner)
                    ? CombatUnit.Make(u.Id, u.Owner, u.Index, u.BaseOffense * rivalFactor, u.Max * rivalFactor, u.Defense,
                        (u.Max - u.Health) * rivalFactor, u.Class, u.BaseBombard * rivalFactor)
                    : u;
                units = units.Select(Scaled).ToList();
                bunkers = bunkers.Select(Scaled).ToList();
            }

            // My own fleets heading here: they join before the combat of the projected round equal to their delay.
            var inbound = new List<(int round, CombatUnit unit)>();
            var index = planet.DockedShips.Count;
            foreach (var order in orders ?? Enumerable.Empty<GameAI.GameAIOrder>())
            {
                if (order.Type != GameAI.GameAIOrder.OrderType.OrderTypeShipTransport || order.PlayerId != player) continue;
                if (order.Target != planet.PlanetName || order.Fleet == null || !Ship.IsFightingKind(order.Fleet.Kind)) continue;
                var round = Math.Max(1, order.TimingDelay);
                if (round > constants.retreatProjectionTurns) continue;
                var siege = order.Fleet.Kind == Ship.ShipKind.SiegeShip;
                var template = siege ? constants.siegeShipData : constants.warShipData;
                var subtype = WarshipStats.SubtypeOf(order.Fleet.Kind);
                for (var i = 0; i < order.Fleet.Snapshots.Count; i++)
                {
                    var snapshot = order.Fleet.Snapshots[i];
                    var unit = CombatUnit.Make(nextId++, player, index++, stats.Offense(template, snapshot, subtype),
                        stats.Health(template, snapshot, subtype), stats.Defense(template, snapshot, subtype), order.Fleet.DamageAt(i), siege ? 1 : 0,
                        stats.Bombard(template, snapshot, subtype));
                    if (unit.Health > 0f) inbound.Add((round, unit));
                }
            }
            // No group of mine: nothing docked that can fight and nothing landing within the projection.
            if (!units.Any(u => u.Owner == player) && inbound.Count == 0) return null;

            bool IsRival(CombatUnit u) => rivals.Contains(u.Owner);
            var projection = new FightProjection
            {
                Planet = planet.PlanetName, Player = player, Rivals = rivals,
                MyShips = units.Count(u => u.Owner == player),
                MyStrength = units.Where(u => u.Owner == player).Sum(u => u.Strength),
                RivalStrength = units.Where(IsRival).Sum(u => u.Strength) + bunkers.Where(IsRival).Sum(b => b.Strength),
            };

            Func<int, int, float> hostility = (a, b) => map.Diplomacy.Hostility(a, b);
            // Whether any ship of mine has been on the planet yet (docked at the start, or landed). Only an inbound-only group that has
            // not landed yet waits for its arrival; once a group of mine has been here, its loss ends the projection as it always did.
            var mineEntered = units.Any(u => u.Owner == player);
            for (var round = 1; round <= constants.retreatProjectionTurns; round++)
            {
                foreach (var arrival in inbound.Where(a => a.round == round))
                {
                    units.Add(arrival.unit);
                    projection.InboundShips++;
                    projection.MyStrength += arrival.unit.Strength;
                }
                var mineHere = units.Any(u => u.Owner == player);
                var pending = inbound.Any(a => a.round > round);
                if ((!mineHere && (mineEntered || !pending)) || !(units.Any(IsRival) || bunkers.Any(IsRival))) break;
                mineEntered |= mineHere;

                // The ship-to-ship round, then (as in the real turn) the bombardment step on the state it left: bunkers whose owner
                // has no fighting ship left fire at the at-war ships and take their bombard. With no bunkers the step never runs.
                var fight = CombatSystem.Round(units, atWar, hostility, constants.combatDamageK);
                units = Apply(units, fight);
                var bombard = bunkers.Count == 0 ? null : BombardmentSystem.Step(units, bunkers, atWar, hostility, constants.combatDamageK);
                if (bombard != null)
                {
                    units = Apply(units, bombard.ShipHits);
                    bunkers = Apply(bunkers, bombard.BunkerHits);
                }
                if (Idle(fight) && (bombard == null || Idle(bombard.ShipHits) && Idle(bombard.BunkerHits)))
                {
                    if (!mineEntered) continue;   // still waiting for my group's first landing: a quiet turn does not end the fight
                    break;                     // nobody can hurt anybody: the fight is over
                }
                projection.Turns = round;
            }

            projection.MyStrengthLeft = units.Where(u => u.Owner == player).Sum(u => u.Strength);
            projection.RivalStrengthLeft = units.Where(IsRival).Sum(u => u.Strength) + bunkers.Where(IsRival).Sum(b => b.Strength);
            projection.RivalSurvives = units.Any(IsRival) || bunkers.Any(IsRival);
            return projection;
        }

        private static bool Idle(RoundOutcome outcome) => outcome.Damage.Count == 0 && outcome.Destroyed.Count == 0;

        // A round's outcome applied to a unit list: destroyed units removed, damaged ones advanced (Ids it does not name are untouched).
        private static List<CombatUnit> Apply(List<CombatUnit> units, RoundOutcome outcome)
            => units.Where(u => !outcome.Destroyed.Contains(u.Id))
                .Select(u => outcome.Damage.TryGetValue(u.Id, out var damage) ? u.WithDamage(damage) : u).ToList();
    }
}
