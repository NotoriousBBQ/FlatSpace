// ScoreMatrix.cs
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

// ── Interfaces ───────────────────────────────────────────────────────────────

public interface IScoreMatrixElement
{
    string Target   { get; }
    float  Cost     { get; }
    float  Surplus  { get; }
    float  Shortage { get; }
}

public interface IScoreMatrixAction
{
    string Origin { get; }
    string Target { get; }
    float  Cost   { get; }
}

// ── Generic ScoreMatrix ──────────────────────────────────────────────────────

public class ScoreMatrix<TElement, TAction>
    where TElement : IScoreMatrixElement
    where TAction  : IScoreMatrixAction
{
    public Dictionary<string, List<TElement>> MatrixElements
        = new Dictionary<string, List<TElement>>();

    public static int DefaultCompare(TElement x, TElement y)
        => (x.Cost - x.Surplus).CompareTo(y.Cost - y.Surplus);

    public List<TAction> GenerateActionList(
        Func<string, TElement, TAction> actionFactory,
        Comparison<TElement>            compare = null)
    {
        compare = compare ?? DefaultCompare;

        var remaining = MatrixElements.ToDictionary(
            kvp => kvp.Key,
            kvp => new List<TElement>(kvp.Value));

        var actionList = new List<TAction>();
        if (remaining.Count <= 0)
            return actionList;

        foreach (var list in remaining.Values)
            list.Sort(compare);

        var roundBest = new List<(string OriginKey, TElement Element)>();

        while (remaining.Count > 0 && remaining.Values.First().Count > 0)
        {
            foreach (var kvp in remaining)
            {
                if (kvp.Value.Count == 0) continue;
                roundBest.Add((kvp.Key, kvp.Value[0]));
            }

            roundBest.Sort((a, b) => compare(a.Element, b.Element));

            var (bestOrigin, bestElement) = roundBest[0];
            actionList.Add(actionFactory(bestOrigin, bestElement));

            remaining.Remove(bestOrigin);
            foreach (var list in remaining.Values)
                list.RemoveAll(e => e.Target == bestElement.Target);

            // Prune origins that have no remaining candidates — matches your version
            var keyList = remaining.Keys.ToList();
            foreach (var key in keyList)
                if (remaining[key].Count <= 0)
                    remaining.Remove(key);

            roundBest.Clear();
        }

        return actionList;
    }
}

// ── Concrete resource matrix (existing callers unchanged) ────────────────────

public class ScoreMatrix : ScoreMatrix<ScoreMatrix.ScoreMatrixElement, ScoreMatrix.Action>
{
    public struct ScoreMatrixElement : IScoreMatrixElement
    {
        public float  Surplus;
        public string Target;
        public float  Shortage;
        public float  Cost;

        // Interface implementation — fields exposed as properties
        string IScoreMatrixElement.Target   => Target;
        float  IScoreMatrixElement.Cost     => Cost;
        float  IScoreMatrixElement.Surplus  => Surplus;
        float  IScoreMatrixElement.Shortage => Shortage;
    }

    public struct Action : IScoreMatrixAction
    {
        public string Origin;
        public string Target;
        public float  Cost;

        // Interface implementation
        string IScoreMatrixAction.Origin => Origin;
        string IScoreMatrixAction.Target => Target;
        float  IScoreMatrixAction.Cost   => Cost;
    }

    public static int DefaultCompare(ScoreMatrixElement x, ScoreMatrixElement y)
        => (x.Cost - x.Surplus).CompareTo(y.Cost - y.Surplus);

    /// <summary>
    /// Convenience overload — preserves the original call signature.
    /// </summary>
    public List<Action> GenerateActionList(Comparison<ScoreMatrixElement> compare = null)
        => GenerateActionList(
            (origin, element) => new Action
            {
                Origin = origin,
                Target = element.Target,
                Cost   = element.Cost,
            },
            compare);
}