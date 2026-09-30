// ResourceMatrix.cs

using System;
using System.Collections.Generic;

namespace Flatspace.Objects.Resource
{
// ── Resource element ─────────────────────────────────────────────────────────

    /// <summary>
    /// ResourceChoiceElement for Resource choices.
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

        /// <summary>The planned route, source to target inclusive (null in tests that do not plan one).</summary>
        public List<string> Route { get; set; }
        /// <summary>Total blockade value along the route, origin included; 0 for a clean route.</summary>
        public float Loss { get; set; }
        /// <summary>True when the route is not the ordinary shortest path (for the RouteDetour log line).</summary>
        public bool IsDetour { get; set; }

        public float Surplus => Convert.ToSingle(SurplusResult.Data);
        public float Shortage => Convert.ToSingle(ShortageResult.Data);

        // IEquatable
        
        // Resource distribution only cares about source
        public bool Equals(IScoreMatrixChoiceElement other)
            => other is ResourceChoiceElement s
               && s.Source == Source;
        public static bool operator ==(ResourceChoiceElement a, ResourceChoiceElement b) => a.Equals(b);
        public static bool operator !=(ResourceChoiceElement a, ResourceChoiceElement b) => !a.Equals(b);
        public override bool Equals(object obj) => obj is IScoreMatrixChoiceElement s && Equals(s);
        public override int GetHashCode() => Source.GetHashCode();
    }

// ── Resource action ───────────────────────────────────────────────────────────

    public struct ResourceAction : IScoreMatrixAction
    {
        public ResourceChoiceElement ChosenChoiceElement { get; set; }

        // IScoreMatrixAction
        public string Origin => ChosenChoiceElement.Source;
        public string Target => ChosenChoiceElement.Target;
        public float Cost => ChosenChoiceElement.Cost;
        public List<string> Route => ChosenChoiceElement.Route;
        public float Loss => ChosenChoiceElement.Loss;
        public bool IsDetour => ChosenChoiceElement.IsDetour;
    }
}