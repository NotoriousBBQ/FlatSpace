using System;
using UnityEngine;

namespace FlatSpace
{
    namespace Tools
    {
        /// <summary>
        /// Single source of truth for the per-planet-type designer defaults:
        /// the default <see cref="Planet.PlanetStrategy"/> and the short display
        /// name used for "{ShortName} {n}" planet naming. Used by both the manual
        /// "Generate Names And Strategies" action and the random board generator.
        /// </summary>
        public static class PlanetTypeDefaults
        {
            public static readonly Planet.PlanetType[] AllTypes =
                (Planet.PlanetType[])Enum.GetValues(typeof(Planet.PlanetType));

            public static Planet.PlanetStrategy StrategyFor(Planet.PlanetType type)
            {
                switch (type)
                {
                    case Planet.PlanetType.PlanetTypeDesolate:   return Planet.PlanetStrategy.PlanetStrategyFocusedGrotsits;
                    case Planet.PlanetType.PlanetTypeFarm:       return Planet.PlanetStrategy.PlanetStrategyFood;
                    case Planet.PlanetType.PlanetTypeIndustrial: return Planet.PlanetStrategy.PlanetStrategyFocusedIndustry;
                    case Planet.PlanetType.PlanetTypeNormal:     return Planet.PlanetStrategy.PlanetStrategyBalanced;
                    case Planet.PlanetType.PlanetTypePrime:      return Planet.PlanetStrategy.PlanetStrategyBalanced;
                    case Planet.PlanetType.PlanetTypeVerdant:    return Planet.PlanetStrategy.PlanetStrategyFocusedFood;
                    case Planet.PlanetType.PlanetTypeOcean:      return Planet.PlanetStrategy.PlanetStrategyFocusedResearch;
                    case Planet.PlanetType.PlanetTypeDesert:     return Planet.PlanetStrategy.PlanetStrategyIndustry;
                    default:                                     return Planet.PlanetStrategy.PlanetStrategyBalanced;
                }
            }

            public static string ShortName(Planet.PlanetType type)
            {
                const string prefix = "PlanetType";
                var name = type.ToString();
                return name.StartsWith(prefix) ? name.Substring(prefix.Length) : name;
            }
        }
    }
}
