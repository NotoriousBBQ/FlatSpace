// ResearchMatrix.cs
using Flatspace.Objects.Production;

// ── Research element ─────────────────────────────────────────────────────────

/// <summary>
/// ScoreMatrixChoiceElement for research choices.
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
