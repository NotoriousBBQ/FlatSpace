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
    int NumChoices { get; }
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

public class ScoreMatrixMultipleDecisionComparer : IComparer<ScoreMatrixMultipleDecisionElement>
{
    public int Compare(ScoreMatrixMultipleDecisionElement x, ScoreMatrixMultipleDecisionElement y)
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
    int IScoreMatrixDecisionElement.NumChoices => 1;
}

public struct ScoreMatrixMultipleDecisionElement : IScoreMatrixDecisionElement
{
    public string Target { get; set; }
    public float Priority { get; set; }

    public int NumChoices { get; set; }
    
    string IScoreMatrixDecisionElement.Target => Target;
    float IScoreMatrixDecisionElement.Priority => Priority;
    int IScoreMatrixDecisionElement.NumChoices => NumChoices;
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

        var actionList = new List<TAction>();
        if (MatrixElements.Count <= 0)
            return actionList;

        foreach (var list in MatrixElements.Values)
            list.Sort(ChoiceCompare);

        foreach (var decision in MatrixElements)
        {
            if(decision.Value.Count <= 0) continue;
            var numChoices = decision.Key.NumChoices;

            var choiceIndex = 0;

            while (choiceIndex < numChoices)
            {
                var bestChoice = decision.Value[choiceIndex++];
                actionList.Add(actionFactory(decision.Key, bestChoice));
                foreach (var remaining in MatrixElements)
                {
                    if (remaining.Value.Count > 0)
                        remaining.Value.RemoveAll(v => v.Equals(bestChoice));
                }
                
            }
        }

        return actionList;
    }
}