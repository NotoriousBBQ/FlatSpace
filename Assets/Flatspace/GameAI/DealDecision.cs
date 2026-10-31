using System.Collections.Generic;

namespace FlatSpace
{
    namespace AI
    {
        public struct DealDecisionElement : IScoreMatrixDecisionElement
        {
            public string Target { get; set; }
            public float Priority { get; set; }
            public int NumChoices => 1;
        }

        public class DealDecisionComparer : IComparer<DealDecisionElement>
        {
            public int Compare(DealDecisionElement x, DealDecisionElement y)
                => x.Priority == y.Priority ? string.CompareOrdinal(x.Target, y.Target) : y.Priority.CompareTo(x.Priority);
        }

        public struct DealChoiceElement : IScoreMatrixChoiceElement
        {
            public bool Accept;
            public float Score;

            public string Target => Accept ? "Accept" : "Decline";
            public float Cost => 0f;
            public float Surplus => 0f;
            public float Shortage => 0f;

            public bool Equals(IScoreMatrixChoiceElement other) => other is DealChoiceElement d && d.Accept == Accept;
            public override bool Equals(object obj) => obj is IScoreMatrixChoiceElement s && Equals(s);
            public override int GetHashCode() => Accept ? 1 : 0;
        }

        public struct DealAction : IScoreMatrixAction
        {
            public bool Accept;

            public string Origin => "Rival";
            public string Target => Accept ? "Accept" : "Decline";
            public float Cost => 0f;
        }

        /// <summary>
        /// The accept-or-decline decision, a ScoreMatrix with one row and two choices: Accept scores the net value, Decline
        /// scores 0, the higher score first and a tie to Accept (so a deal worth exactly 0 is accepted). Deterministic: no
        /// weight selector, so no randomness. Pure.
        /// </summary>
        public static class DealDecision
        {
            public static bool Accepts(DealVerdict verdict)
            {
                if (verdict == null || verdict.Refusal != null) return false;
                var matrix = new ScoreMatrix<DealDecisionElement, DealChoiceElement, DealAction>(new DealDecisionComparer())
                {
                    IndependentRows = true,
                };
                matrix.MatrixElements.Add(
                    new DealDecisionElement { Target = "deal", Priority = 0f },
                    new List<DealChoiceElement>
                    {
                        new DealChoiceElement { Accept = false, Score = 0f },
                        new DealChoiceElement { Accept = true, Score = verdict.Net },
                    });
                var actions = matrix.GenerateActionList(
                    (decision, choice) => new DealAction { Accept = choice.Accept },
                    (x, y) =>
                    {
                        var byScore = y.Score.CompareTo(x.Score);
                        return byScore != 0 ? byScore : y.Accept.CompareTo(x.Accept);   // a tie goes to Accept
                    });
                return actions.Count > 0 && actions[0].Accept;
            }
        }
    }
}
