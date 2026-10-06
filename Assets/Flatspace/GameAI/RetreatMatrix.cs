using System;
using System.Collections.Generic;
using System.Linq;

namespace FlatSpace
{
    namespace AI
    {
        public struct RetreatDecisionElement : IScoreMatrixDecisionElement
        {
            public string Target { get; set; }     // the fight planet
            public float Priority { get; set; }
            public int NumChoices => 1;
        }

        public class RetreatDecisionComparer : IComparer<RetreatDecisionElement>
        {
            // Higher priority first, then by planet name (never compare the name diff against the priority itself).
            public int Compare(RetreatDecisionElement x, RetreatDecisionElement y)
                => x.Priority == y.Priority ? string.CompareOrdinal(x.Target, y.Target) : y.Priority.CompareTo(x.Priority);
        }

        public struct RetreatChoiceElement : IScoreMatrixChoiceElement
        {
            public string Planet;        // the fight planet (the row)
            public bool Retreat;         // false = Stay
            public string Destination;   // empty for Stay
            public int Ships;
            public float PathCost;
            public float Weight;

            public string Target => Retreat ? Destination : Planet;
            public float Cost => PathCost;
            public float Surplus => 0f;
            public float Shortage => 0f;

            public bool Equals(IScoreMatrixChoiceElement other)
                => other is RetreatChoiceElement r && r.Planet == Planet && r.Retreat == Retreat && r.Destination == Destination;
            public override bool Equals(object obj) => obj is IScoreMatrixChoiceElement s && Equals(s);
            public override int GetHashCode() => ((Planet ?? string.Empty).GetHashCode() * 31 + (Destination ?? string.Empty).GetHashCode()) * 2 + (Retreat ? 1 : 0);
        }

        /// <summary>
        /// The retreat-or-stay decision for the fight planets that passed the gate, a ScoreMatrix with IndependentRows (the
        /// stance-matrix pattern): one row per planet, the choices Stay and Retreat (to the best tiered destination), weighted by
        /// how much of the group the projected fight would cost. A row with no destination offers Stay only. Pure.
        /// </summary>
        public static class RetreatMatrix
        {
            public struct Row
            {
                public string Planet;
                public int Ships;
                public float LossFraction;     // the projected share of the group's strength lost
                public string Destination;     // empty or null = none: the row can only Stay
                public int Tier;
                public float PathCost;
            }

            public struct Decision
            {
                public string Planet;
                public bool Retreat;
                public float PRetreat;         // the weight Retreat had in the roll
                public ShipAction Action;      // meaningful when Retreat
            }

            public static float RetreatProbability(float lossFraction, GameAIConstants constants)
            {
                var steepness = Math.Max(constants.retreatSteepness, 0.0001f);
                return (float)(1.0 / (1.0 + Math.Exp(-(lossFraction - constants.retreatLossFraction) / steepness)));
            }

            public static List<Decision> Decide(List<Row> rows, GameAIConstants constants)
            {
                var matrix = new ScoreMatrix<RetreatDecisionElement, RetreatChoiceElement, ShipAction>(new RetreatDecisionComparer())
                {
                    IndependentRows = true,
                };
                var pRetreat = new Dictionary<string, float>();
                var rank = 0f;
                foreach (var row in rows.OrderByDescending(r => r.LossFraction).ThenBy(r => r.Planet, StringComparer.Ordinal))
                {
                    var p = RetreatProbability(row.LossFraction, constants);
                    pRetreat[row.Planet] = p;
                    var choices = new List<RetreatChoiceElement>
                    {
                        new RetreatChoiceElement { Planet = row.Planet, Retreat = false, Destination = string.Empty, Ships = row.Ships, Weight = 1f - p },
                    };
                    if (!string.IsNullOrEmpty(row.Destination))
                        choices.Add(new RetreatChoiceElement
                        {
                            Planet = row.Planet, Retreat = true, Destination = row.Destination, Ships = row.Ships,
                            PathCost = row.PathCost, Weight = p,
                        });
                    matrix.MatrixElements.Add(new RetreatDecisionElement { Target = row.Planet, Priority = -rank++ }, choices);
                }

                var actions = matrix.GenerateActionList(
                    (decision, choice) => choice.Retreat
                        ? new ShipAction
                        {
                            Origin = choice.Planet, Target = choice.Destination, Cost = choice.PathCost, Count = choice.Ships,
                            Kind = Ship.ShipKind.WarShip,
                        }
                        : new ShipAction { Origin = choice.Planet, Target = string.Empty, Cost = 0f, Count = 0, Kind = Ship.ShipKind.WarShip },
                    null,
                    choice => choice.Weight);
                return actions
                    .OrderBy(a => a.Origin, StringComparer.Ordinal)
                    .Select(a => new Decision { Planet = a.Origin, Retreat = a.Count > 0, PRetreat = pRetreat[a.Origin], Action = a })
                    .ToList();
            }
        }
    }
}
