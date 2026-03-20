// ScoreMatrix.cs
using System;
using System.Collections.Generic;
using System.Linq;

public class ScoreMatrix
{
    // ── Types ────────────────────────────────────────────────────────────────

    public struct ScoreMatrixElement
    {
        public float  Surplus;
        public string Target;
        public float  Shortage;
        public float  Cost;
    }

    public struct Action
    {
        public string Origin;
        public string Target;
        public float  Cost;
    }

    // ── Data ─────────────────────────────────────────────────────────────────

    public Dictionary<string, List<ScoreMatrixElement>> MatrixElements
        = new Dictionary<string, List<ScoreMatrixElement>>();

    // ── Default sort: lowest (Cost - Surplus) first ──────────────────────────

    public static int DefaultCompare(ScoreMatrixElement x, ScoreMatrixElement y)
        => (x.Cost - x.Surplus).CompareTo(y.Cost - y.Surplus);

    // ── Core algorithm ───────────────────────────────────────────────────────

    /// <summary>
    /// Greedy assignment: repeatedly picks the globally cheapest unassigned
    /// (origin → target) pair, then removes that origin and target from
    /// further consideration.
    /// </summary>
    /// <param name="compare">
    /// Optional sort strategy. Defaults to <see cref="DefaultCompare"/>
    /// (lowest Cost−Surplus wins).
    /// </param>
    public List<Action> GenerateActionList(
        Comparison<ScoreMatrixElement> compare = null)
    {
        compare = compare ?? DefaultCompare;

        // Work on a shallow copy so the original matrix is not consumed.
        var remaining = new Dictionary<string, List<ScoreMatrixElement>>(
            MatrixElements.ToDictionary(
                kvp => kvp.Key,
                kvp => new List<ScoreMatrixElement>(kvp.Value)));

        // Sort each origin's candidate list once up front.

        var actionList   = new List<Action>();
        if (remaining.Count <= 0)
            return actionList;
        foreach (var list in remaining.Values)
            list.Sort(compare);
        var roundBest    = new List<ScoreMatrixElement>();

        while (remaining.Count > 0 && remaining.Values.First().Count > 0)
        {
            // Collect each origin's current best candidate.
            foreach (var kvp in remaining)
            {
                if (kvp.Value.Count == 0) continue;
                roundBest.Add(new ScoreMatrixElement
                {
                    Target   = kvp.Key,           // re-used as "origin key" here
                    Cost     = kvp.Value[0].Cost,
                    Surplus  = kvp.Value[0].Surplus,
                    Shortage = kvp.Value[0].Shortage,
                });
            }

            roundBest.Sort(compare);

            var bestOrigin = roundBest[0].Target;          // origin planet name
            var bestTarget = remaining[bestOrigin][0].Target; // destination planet name
            var bestCost   = roundBest[0].Cost;

            actionList.Add(new Action
            {
                Origin = bestOrigin,
                Target = bestTarget,
                Cost   = bestCost,
            });

            // Remove the chosen origin and strike the chosen target from all
            // remaining lists so neither is reused.
            remaining.Remove(bestOrigin);
            foreach (var list in remaining.Values)
                list.RemoveAll(e => e.Target == bestTarget);

            var keyList = remaining.Keys.ToList();
            foreach(var key in keyList)
                if (remaining[key].Count <= 0)
                    remaining.Remove(key);
            roundBest.Clear();
        }

        return actionList;
    }
}