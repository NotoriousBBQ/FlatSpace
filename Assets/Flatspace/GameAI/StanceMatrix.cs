using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace FlatSpace
{
    namespace AI
    {
        public struct StanceDecisionElement : IScoreMatrixDecisionElement
        {
            public string Target { get; set; }
            public float Priority { get; set; }
            public int NumChoices => 1;
        }

        public class StanceDecisionComparer : IComparer<StanceDecisionElement>
        {
            // Higher priority first, then by target name (never compare the name diff against the priority itself).
            public int Compare(StanceDecisionElement x, StanceDecisionElement y)
                => x.Priority == y.Priority ? string.CompareOrdinal(x.Target, y.Target) : y.Priority.CompareTo(x.Priority);
        }

        public struct StanceChoiceElement : IScoreMatrixChoiceElement
        {
            public int Rival;
            public Stance Stance;
            public float Weight;

            public string Target => Rival.ToString(CultureInfo.InvariantCulture);
            public float Cost => 0f;
            public float Surplus => 0f;
            public float Shortage => 0f;

            public bool Equals(IScoreMatrixChoiceElement other)
                => other is StanceChoiceElement s && s.Rival == Rival && s.Stance == Stance;
            public override bool Equals(object obj) => obj is IScoreMatrixChoiceElement s && Equals(s);
            public override int GetHashCode() => Rival * 31 + (int)Stance;
        }

        public struct StanceAction : IScoreMatrixAction
        {
            public int Player;
            public int Rival;
            public Stance Stance;

            public string Origin => Player.ToString(CultureInfo.InvariantCulture);
            public string Target => Rival.ToString(CultureInfo.InvariantCulture);
            public float Cost => 0f;
        }

        /// <summary>
        /// The war-or-peace decision, a ScoreMatrix with IndependentRows: one row per rival, the choices Peace and War weighted
        /// from the hostility score, the stance held now favoured (stanceStickiness) and a row left alone for stanceHoldTurns
        /// after its last change. Pure.
        /// </summary>
        public static class StanceMatrix
        {
            public struct Row
            {
                public int Rival;
                public float Hostility;
                public Stance Current;
                public int TurnsSinceChange;
            }

            public struct Decision
            {
                public int Rival;
                public Stance Stance;
                public float PWar;
            }

            public static float WarProbability(float hostility, GameAIConstants constants)
            {
                var steepness = Math.Max(constants.stanceSteepness, 0.0001f);
                return (float)(1.0 / (1.0 + Math.Exp(-(hostility - constants.stanceMidpoint) / steepness)));
            }

            public static float WarWeight(Row row, GameAIConstants constants)
                => WarProbability(row.Hostility, constants) * (row.Current == Stance.War ? constants.stanceStickiness : 1f);

            public static float PeaceWeight(Row row, GameAIConstants constants)
                => (1f - WarProbability(row.Hostility, constants)) * (row.Current == Stance.Peace ? constants.stanceStickiness : 1f);

            public static List<Decision> Decide(int player, List<Row> rows, GameAIConstants constants)
            {
                var matrix = new ScoreMatrix<StanceDecisionElement, StanceChoiceElement, StanceAction>(new StanceDecisionComparer())
                {
                    IndependentRows = true,
                };
                var pWar = new Dictionary<int, float>();
                var rank = 0f;
                foreach (var row in rows.OrderBy(r => r.Rival))
                {
                    if (row.TurnsSinceChange < constants.stanceHoldTurns) continue;   // held since its last change
                    pWar[row.Rival] = WarProbability(row.Hostility, constants);
                    matrix.MatrixElements.Add(
                        new StanceDecisionElement { Target = row.Rival.ToString(CultureInfo.InvariantCulture), Priority = -rank++ },
                        new List<StanceChoiceElement>
                        {
                            new StanceChoiceElement { Rival = row.Rival, Stance = Stance.Peace, Weight = PeaceWeight(row, constants) },
                            new StanceChoiceElement { Rival = row.Rival, Stance = Stance.War, Weight = WarWeight(row, constants) },
                        });
                }

                var actions = matrix.GenerateActionList(
                    (decision, choice) => new StanceAction { Player = player, Rival = choice.Rival, Stance = choice.Stance },
                    null,
                    choice => choice.Weight);
                return actions
                    .OrderBy(a => a.Rival)
                    .Select(a => new Decision { Rival = a.Rival, Stance = a.Stance, PWar = pWar[a.Rival] })
                    .ToList();
            }
        }
    }
}
