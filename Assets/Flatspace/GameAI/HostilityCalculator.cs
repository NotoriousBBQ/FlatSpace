using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace FlatSpace
{
    namespace AI
    {
        /// <summary>
        /// The per-turn hostility update for one (player, rival) pair: the previous score decays, then the rival's blockade cuts,
        /// its warships near my planets and the strength ratio are added, all clamped to 0..hostilityMax. Pure.
        /// </summary>
        public static class HostilityCalculator
        {
            public const float StrengthLogClamp = 2f;

            public struct Inputs
            {
                public float Previous;
                public int Cuts;
                public int NearShips;
                public float MyStrength;
                public float RivalStrength;
                public float ShipsLost;   // my warships this rival destroyed this turn (the attributed share, so fractional)
                public float LossShare;   // lost / (current + lost) strength over the loss window
                public int Conversions;   // my inhabitants this rival converted this turn (it was not at war with me)
            }

            public struct Result
            {
                public float Hostility;
                public float CutsTerm;
                public float NearTerm;
                public float StrengthTerm;
                public float LossTerm;   // ships lost x hostilityPerShipLost, minus the significant-loss drop when it applies
                public float ConversionTerm;   // inhabitants converted x hostilityPerConversion
            }

            /// <summary>weight x log2(mine / rival) clamped to +-2; no rival fleet is +2, no fleet of mine against one is -2, two empty fleets 0.</summary>
            public static float StrengthTerm(float mine, float rival, float weight)
            {
                if (mine <= 0f && rival <= 0f) return 0f;
                float log;
                if (rival <= 0f) log = StrengthLogClamp;
                else if (mine <= 0f) log = -StrengthLogClamp;
                else log = Mathf.Clamp((float)Math.Log(mine / rival, 2.0), -StrengthLogClamp, StrengthLogClamp);
                return weight * log;
            }

            public static Result Compute(Inputs input, GameAIConstants constants)
            {
                var cuts = input.Cuts * constants.hostilityPerCut;
                var near = input.NearShips * constants.hostilityPerNearShip;
                var strength = StrengthTerm(input.MyStrength, input.RivalStrength, constants.hostilityStrengthWeight);
                var loss = input.ShipsLost * constants.hostilityPerShipLost;
                var drop = input.LossShare >= constants.significantLossFraction ? -constants.significantLossHostilityDrop : 0f;
                var conversions = input.Conversions * constants.hostilityPerConversion;
                var hostility = input.Previous * (1f - constants.hostilityDecay) + cuts + near + strength + loss + drop + conversions;
                return new Result
                {
                    Hostility = Mathf.Clamp(hostility, 0f, constants.hostilityMax),
                    CutsTerm = cuts,
                    NearTerm = near,
                    StrengthTerm = strength,
                    LossTerm = loss + drop,
                    ConversionTerm = conversions,
                };
            }

            /// <summary>
            /// The rival's docked warships on planets I hold or beside my populated planets (known planets only). Ships docked
            /// on a planet the rival populates and I do not are its garrison, not a threat, and are not counted.
            /// </summary>
            public static int CountNearShips(GameAIMap map, int me, int rival)
            {
                var near = new HashSet<string>();
                foreach (var planet in map.PlanetList)
                {
                    if (planet.Owner != me || planet.Population.Count == 0) continue;
                    near.Add(planet.PlanetName);
                    foreach (var neighbour in map.GetNeighbours(planet.PlanetName)) near.Add(neighbour);
                }

                var count = 0;
                foreach (var name in near)
                {
                    if (!map.Knowledge.IsKnown(me, name)) continue;
                    var planet = map.GetPlanet(name);
                    if (planet == null) continue;
                    if (planet.Population.Exists(p => p.Player == rival) && !planet.Population.Exists(p => p.Player == me)) continue;
                    count += planet.DockedShips.Count(s => s.Kind == Ship.ShipKind.WarShip && s.Owner == rival);
                }
                return count;
            }
        }
    }
}
