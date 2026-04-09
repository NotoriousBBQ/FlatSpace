// IndustyMatrix.cs

using System;
using Flatspace.Objects.Production;

// ── Industry element ─────────────────────────────────────────────────────────

/// <summary>
/// IndustryChoiceElement for research choices.
/// Cost comes from the CatalogItem; Priority is set by the caller
/// based on AIStrategy and item type — lower = more preferred.
/// </summary>
public struct IndustryChoiceElement : IScoreMatrixChoiceElement
{
    public CatalogItem Item     { get; set; }
    public float       Priority { get; set; }
    public string PlanetName {get; set;}
    public float Surplus;

    // IScoreMatrixChoiceElement
    public string Target   => Item.itemName;
    float  IScoreMatrixChoiceElement.Cost     => Item.cost;
    float  IScoreMatrixChoiceElement.Surplus  => Surplus;   
    public float  Shortage => 0f;   // not used for Industry
    // Equatable interface
    public bool Equals(IScoreMatrixChoiceElement other)
        => other is IndustryChoiceElement s
           && s.PlanetName   == PlanetName
           && s.Item     == Item;
    public static bool operator ==(IndustryChoiceElement a, IndustryChoiceElement b) => a.Equals(b);
    public static bool operator !=(IndustryChoiceElement a, IndustryChoiceElement b) => !a.Equals(b);
    public override bool Equals(object obj) => obj is IScoreMatrixChoiceElement s && Equals(s);
    public override int GetHashCode() => (PlanetName + Target).GetHashCode();

}

// ── Research action ───────────────────────────────────────────────────────────

public struct IndustryAction : IScoreMatrixAction
{
    public CatalogItem ChosenItem { get; set; }
    public string PlanetName { get; set; }

    // IScoreMatrixAction
    public string Origin => PlanetName;
    public string Target => ChosenItem.itemName;
    public float  Cost   => ChosenItem.cost;
}
