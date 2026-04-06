// ScoreMatrix.cs
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

// ── Interfaces ───────────────────────────────────────────────────────────────

public interface IScoreMatrixDecisionElement
{
    string Target { get; }
    float Priority { get; }
}
public interface IScoreMatrixChoiceElement : IEquatable<IScoreMatrixChoiceElement>
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
public class ScoreMatrixDecisionComparer : IComparer<ScoreMatrixDecisionElement>
{
    public int Compare(ScoreMatrixDecisionElement x, ScoreMatrixDecisionElement y)
    {
        return (x.Priority == y.Priority ? x.Target.CompareTo(y.Target) : y.Priority).CompareTo(x.Priority);
    }
                
}

public struct ScoreMatrixDecisionElement : IScoreMatrixDecisionElement
{
    public string Target { get; set; }
    public float Priority { get; set; }
    
    string IScoreMatrixDecisionElement.Target => Target;
    float IScoreMatrixDecisionElement.Priority => Priority;
}
public struct ScoreMatrixChoiceElement : IScoreMatrixChoiceElement
{
    public float  Surplus;
    public string Target;
    public float  Shortage;
    public float  Cost;

    // Interface implementation — fields exposed as properties
    string IScoreMatrixChoiceElement.Target   => Target;
    float  IScoreMatrixChoiceElement.Cost     => Cost;
    float  IScoreMatrixChoiceElement.Surplus  => Surplus;
    float  IScoreMatrixChoiceElement.Shortage => Shortage;

    // Equatable interface
    public bool Equals(IScoreMatrixChoiceElement other)
        => other is ScoreMatrixChoiceElement s
           && s.Target   == Target
           && Math.Abs(s.Cost - Cost) < float.Epsilon
           && Math.Abs(s.Surplus - Surplus) < float.Epsilon 
           && Math.Abs(s.Shortage - Shortage) < float.Epsilon;
    public static bool operator ==(ScoreMatrixChoiceElement a, ScoreMatrixChoiceElement b) => a.Equals(b);
    public static bool operator !=(ScoreMatrixChoiceElement a, ScoreMatrixChoiceElement b) => !a.Equals(b);
    public override bool Equals(object obj) => obj is IScoreMatrixChoiceElement s && Equals(s);
    public override int GetHashCode() => Target.GetHashCode();
}

public struct ScoreMatrixAction : IScoreMatrixAction
{
    public string Origin {get; set;}
    public string Target {get; set;}
    public float Cost {get; set;}

    string IScoreMatrixAction.Origin => Origin;
    string IScoreMatrixAction.Target => Target;
    float IScoreMatrixAction.Cost => Cost;
}

public class ScoreMatrix<TScoreMatrixDecisionElement, TScoreMatrixChoiceElement, TAction>
    where  TScoreMatrixDecisionElement : IScoreMatrixDecisionElement
    where TScoreMatrixChoiceElement : IScoreMatrixChoiceElement
    where TAction  : IScoreMatrixAction
{
    public ScoreMatrix(IComparer<TScoreMatrixDecisionElement> comparer )
    {
        MatrixElements = new SortedDictionary<TScoreMatrixDecisionElement, List<TScoreMatrixChoiceElement>>(comparer);
    }
    public SortedDictionary<TScoreMatrixDecisionElement, List<TScoreMatrixChoiceElement>> MatrixElements;

    private static int DefaultChoiceCompare(TScoreMatrixChoiceElement x, TScoreMatrixChoiceElement y)
        => (x.Cost - x.Surplus).CompareTo(y.Cost - y.Surplus);

    public List<TAction>  GenerateActionList(
        Func<TScoreMatrixDecisionElement, TScoreMatrixChoiceElement, TAction> actionFactory,
        Comparison<TScoreMatrixChoiceElement>            ChoiceCompare = null)
    {
        ChoiceCompare = ChoiceCompare ?? DefaultChoiceCompare;
/*
        var remaining = MatrixElements
            .ToDictionary(
            kvp => kvp.Key,
            kvp => new List<TScoreMatrixChoiceElement>(kvp.Value));
  */      

        var actionList = new List<TAction>();
        if (MatrixElements.Count <= 0)
            return actionList;

        foreach (var list in MatrixElements.Values)
            list.Sort(ChoiceCompare);

        foreach (var choice in MatrixElements)
        {
            if(choice.Value.Count <= 0) continue;

            var bestChoice = choice.Value[0];
            actionList.Add(actionFactory(choice.Key, bestChoice));
            foreach (var remaining in MatrixElements)
            {
                if (remaining.Value.Count > 0)
                    remaining.Value.RemoveAll(v => v.Equals(bestChoice));
            }
        }
#if REMOVE_THIS        
        var roundBest = new List<(TScoreMatrixDecisionElement OriginKey, TScoreMatrixChoiceElement Element)>();

        while (MatrixElements.Count > 0 && MatrixElements.Values.First().Count > 0)
        {
            foreach (var kvp in MatrixElements)
            {
                if (kvp.Value.Count == 0) continue;
                roundBest.Add((kvp.Key, kvp.Value[0]));
            }

            roundBest.Sort((a, b) => ChoiceCompare(a.Element, b.Element));

            var (bestOrigin, bestElement) = roundBest[0];
            actionList.Add(actionFactory(bestOrigin, bestElement));

            var removed = MatrixElements.Remove(bestOrigin);
            if(!removed)
                Debug.Log("Not Removed remaining key is " + bestOrigin);
            foreach (var list in MatrixElements.Values)
                list.RemoveAll(e => e.Target == bestElement.Target);

            // Prune origins that have no remaining candidates — matches your version
            var keyList = MatrixElements.Keys.ToList();
            foreach (var key in keyList)
            {
                if (MatrixElements.TryGetValue(key, out var keyValueList))
                {
                    if (keyValueList.Count <= 0)
                        MatrixElements.Remove(key);
                }
                else
                {
                    MatrixElements.Remove(key);                    
                }
            }

            roundBest.Clear();
        }
#endif
        return actionList;
    }
}