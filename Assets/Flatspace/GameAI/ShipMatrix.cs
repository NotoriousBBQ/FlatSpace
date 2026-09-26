// ShipMatrix.cs
using System;

// ── Ship choice element ──────────────────────────────────────────────────────

/// <summary>
/// One candidate target planet for a source planet's spare ships.
/// Equality is on the TARGET PLANET ONLY: ScoreMatrix removes a chosen choice from every other
/// row via Equals, and the same target appears in each row with different cost/spare values,
/// so equality must ignore them for "a target is claimed once per turn" to hold.
/// </summary>
public struct ShipChoiceElement : IScoreMatrixChoiceElement
{
    public string TargetPlanet { get; set; }
    public int    Category     { get; set; }   // 1 (best) .. 5; int.MaxValue = none
    public int    Rank         { get; set; }   // sort key, lower first; equals Category except under Consolidate
    public float  PathCost     { get; set; }
    public float  SpareShips   { get; set; }   // ships the source can spare
    public float  Deficit      { get; set; }   // ships the target still needs

    // IScoreMatrixChoiceElement
    public string Target => TargetPlanet;
    float IScoreMatrixChoiceElement.Cost     => PathCost;
    float IScoreMatrixChoiceElement.Surplus  => SpareShips;
    float IScoreMatrixChoiceElement.Shortage => Deficit;

    public bool Equals(IScoreMatrixChoiceElement other)
        => other is ShipChoiceElement s && s.TargetPlanet == TargetPlanet;
    public static bool operator ==(ShipChoiceElement a, ShipChoiceElement b) => a.Equals(b);
    public static bool operator !=(ShipChoiceElement a, ShipChoiceElement b) => !a.Equals(b);
    public override bool Equals(object obj) => obj is IScoreMatrixChoiceElement s && Equals(s);
    public override int GetHashCode() => (TargetPlanet ?? string.Empty).GetHashCode();
}

// ── Ship action ──────────────────────────────────────────────────────────────

public struct ShipAction : IScoreMatrixAction
{
    public string        Origin { get; set; }
    public string        Target { get; set; }
    public float         Cost   { get; set; }
    public int           Count  { get; set; }
    public Ship.ShipKind Kind   { get; set; }
}
