// ResourceMatrix.cs

using System;

namespace Flatspace.Objects.Resource
{
// ── Resource element ─────────────────────────────────────────────────────────

    /// <summary>
    /// ScoreMatrixChoiceElement for Resource choices.
    /// Cost comes from the 
    /// 
    /// </summary>
    public struct ResourceChoiceElement : IScoreMatrixChoiceElement
    {
        public Planet.PlanetUpdateResult SurplusResult { get; set; }
        public Planet.PlanetUpdateResult ShortageResult { get; set; }

        // IScoreMatrixChoiceElement
        public string Target => ShortageResult.Name;
        public string Source => SurplusResult.Name;
        public float Cost { get; set; }    
        public float Surplus => Convert.ToSingle(SurplusResult.Data);
        public float Shortage => Convert.ToSingle(ShortageResult.Data);
    }

// ── Research action ───────────────────────────────────────────────────────────

    public struct ResourceAction : IScoreMatrixAction
    {
        public ResourceChoiceElement ChosenChoiceElement { get; set; }

        // IScoreMatrixAction
        public string Origin => ChosenChoiceElement.Source;
        public string Target => ChosenChoiceElement.Target;
        public float Cost => ChosenChoiceElement.Cost;
    }
}