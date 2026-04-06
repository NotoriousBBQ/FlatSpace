// ResearchMatrix.cs

using System;
using Flatspace.Objects.Production;

// ── Research element ─────────────────────────────────────────────────────────

/// <summary>
/// ResearchChoiceElement for research choices.
/// Cost comes from the CatalogItem; Priority is set by the caller
/// based on AIStrategy and item type — lower = more preferred.
/// </summary>
public struct ResearchChoiceElement : IScoreMatrixChoiceElement
{
    public CatalogItem Item     { get; set; }
    public float       Priority { get; set; }

    // IScoreMatrixChoiceElement
    public string Target   => Item.itemName;
    public float  Cost     => Item.cost;
    public float  Surplus  => 0f;   // not used for research
    public float  Shortage => 0f;   // not used for research
    // Equatable interface
    public bool Equals(IScoreMatrixChoiceElement other)
        => other is ResearchChoiceElement s
           && s.Target   == Target
           && s.Item     == Item
           && Math.Abs(s.Cost - Cost) < float.Epsilon
           && Math.Abs(s.Surplus - Surplus) < float.Epsilon 
           && Math.Abs(s.Shortage - Shortage) < float.Epsilon;
    public static bool operator ==(ResearchChoiceElement a, ResearchChoiceElement b) => a.Equals(b);
    public static bool operator !=(ResearchChoiceElement a, ResearchChoiceElement b) => !a.Equals(b);
    public override bool Equals(object obj) => obj is IScoreMatrixChoiceElement s && Equals(s);
    public override int GetHashCode() => Target.GetHashCode();

}

// ── Research action ───────────────────────────────────────────────────────────

public struct ResearchAction : IScoreMatrixAction
{
    public CatalogItem ChosenItem { get; set; }

    // IScoreMatrixAction
    public string Origin => ChosenItem.itemName;
    public string Target => ChosenItem.itemName;
    public float  Cost   => ChosenItem.cost;
}
