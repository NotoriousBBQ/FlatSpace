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
            public bool IsSurrender;   // a Surrender is Peace's stance plus the surrender flag: Stance is never extended (it is saved)

            public string Target => Rival.ToString(CultureInfo.InvariantCulture);
            public float Cost => 0f;
            public float Surplus => 0f;
            public float Shortage => 0f;

            public bool Equals(IScoreMatrixChoiceElement other)
                => other is StanceChoiceElement s && s.Rival == Rival && s.Stance == Stance && s.IsSurrender == IsSurrender;
            public override bool Equals(object obj) => obj is IScoreMatrixChoiceElement s && Equals(s);
            public override int GetHashCode() => Rival * 31 + (int)Stance + (IsSurrender ? 1000 : 0);
        }

        public struct StanceAction : IScoreMatrixAction
        {
            public int Player;
            public int Rival;
            public Stance Stance;
            public bool IsSurrender;

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
                public float LossShare;       // the share of my fleet strength this rival destroyed over the loss window
                public bool AtWar;            // I am at war with this rival right now (my own War, or a forced war)
                public float MyStrength;
                public float RivalStrength;
                public bool Truce;            // the pair is locked by a surrender: no War, no Surrender
            }

            public struct Decision
            {
                public int Rival;
                public Stance Stance;
                public float PWar;
                public bool Surrender;        // the decision is to surrender (Stance is Peace)
            }

            public static float SurrenderWeight(float lossShare, GameAIConstants constants)
            {
                var steepness = Math.Max(constants.surrenderSteepness, 0.0001f);
                return (float)(1.0 / (1.0 + Math.Exp(-(lossShare - constants.surrenderMidpoint) / steepness)));
            }

            // Offered only while I am at war with the rival, weaker than it, and not in a truce.
            private static bool CanSurrender(Row row) => row.AtWar && !row.Truce && row.MyStrength < row.RivalStrength;

            public static float WarProbability(float hostility, GameAIConstants constants)
            {
                var steepness = Math.Max(constants.stanceSteepness, 0.0001f);
                return (float)(1.0 / (1.0 + Math.Exp(-(hostility - constants.stanceMidpoint) / steepness)));
            }

            public static float WarWeight(Row row, GameAIConstants constants)
                => WarProbability(row.Hostility, constants) * (row.Current == Stance.War ? constants.stanceStickiness : 1f);

            public static float PeaceWeight(Row row, GameAIConstants constants)
                => (1f - WarProbability(row.Hostility, constants)) * (row.Current == Stance.Peace ? constants.stanceStickiness : 1f);

            public struct ChoiceWeights
            {
                public float Peace;
                public float War;
                public float Surrender;
            }

            /// <summary>
            /// The weights a row offers. When a surrender is on offer its weight IS its probability: Surrender / (Peace + War +
            /// Surrender) equals SurrenderWeight, held or not, so the logged pSurrender is the real chance (without the scaling
            /// the stickiness on Peace and War shrank it once the stance hold ended). Inside the hold the stance held keeps
            /// 1 - surrender and the other stance has no weight; inside a truce there is no War and no Surrender.
            /// </summary>
            public static ChoiceWeights Weights(Row row, GameAIConstants constants)
            {
                var surrender = CanSurrender(row) ? SurrenderWeight(row.LossShare, constants) : 0f;
                float peace, war;
                if (row.TurnsSinceChange < constants.stanceHoldTurns)
                {
                    peace = row.Current == Stance.Peace ? 1f - surrender : 0f;
                    war = row.Current == Stance.War ? 1f - surrender : 0f;
                }
                else
                {
                    peace = PeaceWeight(row, constants);
                    war = row.Truce ? 0f : WarWeight(row, constants);
                    var sum = peace + war;
                    if (surrender > 0f && sum > 0f)
                    {
                        var scale = (1f - surrender) / sum;
                        peace *= scale;
                        war *= scale;
                    }
                }
                return new ChoiceWeights { Peace = peace, War = war, Surrender = surrender };
            }

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
                    var held = row.TurnsSinceChange < constants.stanceHoldTurns;   // held since its last change
                    var w = Weights(row, constants);
                    if (held && w.Surrender <= 0f) continue;
                    pWar[row.Rival] = WarProbability(row.Hostility, constants);
                    var choices = new List<StanceChoiceElement>();
                    if (held)
                    {
                        // A surrender needs no hold: inside the hold the row is only "keep the stance" or "surrender".
                        choices.Add(new StanceChoiceElement
                        {
                            Rival = row.Rival, Stance = row.Current, Weight = row.Current == Stance.War ? w.War : w.Peace,
                        });
                    }
                    else
                    {
                        choices.Add(new StanceChoiceElement { Rival = row.Rival, Stance = Stance.Peace, Weight = w.Peace });
                        // Inside a truce the War choice is left out altogether: a zero-weight choice could still be picked when
                        // every weight in the row is 0 (a ScoreMatrix row picks uniformly then).
                        if (!row.Truce)
                            choices.Add(new StanceChoiceElement { Rival = row.Rival, Stance = Stance.War, Weight = w.War });
                    }
                    if (w.Surrender > 0f)
                        choices.Add(new StanceChoiceElement
                        {
                            Rival = row.Rival, Stance = Stance.Peace, IsSurrender = true, Weight = w.Surrender,
                        });
                    matrix.MatrixElements.Add(
                        new StanceDecisionElement { Target = row.Rival.ToString(CultureInfo.InvariantCulture), Priority = -rank++ },
                        choices);
                }

                var actions = matrix.GenerateActionList(
                    (decision, choice) => new StanceAction
                    {
                        Player = player, Rival = choice.Rival, Stance = choice.Stance, IsSurrender = choice.IsSurrender,
                    },
                    null,
                    choice => choice.Weight);
                return actions
                    .OrderBy(a => a.Rival)
                    .Select(a => new Decision { Rival = a.Rival, Stance = a.Stance, PWar = pWar[a.Rival], Surrender = a.IsSurrender })
                    .ToList();
            }
        }
    }
}
