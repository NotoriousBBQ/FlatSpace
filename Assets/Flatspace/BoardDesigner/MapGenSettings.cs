using System;
using System.Collections.Generic;
using UnityEngine;

[Serializable]
public struct TypeWeight
{
    public Planet.PlanetType type;
    public float weight;
}

[CreateAssetMenu(fileName = "MapGenSettings", menuName = "Scriptable Objects/MapGenSettings")]
public class MapGenSettings : ScriptableObject
{
    [Header("Composition")]
    [Tooltip("0 = pick and log a random seed; non-zero = reproducible output.")]
    public int seed = 0;

    [Tooltip("Number of Prime (capital) planets == number of players.")]
    public int playerCount = 2;

    [Tooltip("Total planets, including the Primes.")]
    public int totalPlanetCount = 20;

    [Tooltip("Relative frequency weights for the non-Prime types. weight 0 excludes a type. Prime entries are ignored.")]
    public List<TypeWeight> typeWeights = new List<TypeWeight>();

    [Header("Geometry")]
    [Tooltip("Minimum distance between any two planets (blue-noise rejection radius).")]
    public float minPlanetSeparation = 120f;

    [Tooltip("Two planets closer than this are candidates for a connection.")]
    public float connectionRadius = 400f;

    [Tooltip("Reference spacing; map side length = sqrt(totalPlanetCount) * this.")]
    public float nominalSpacing = 200f;

    [Header("Robustness")]
    [Tooltip("How many different seeds to try before giving up when constraints can't be satisfied.")]
    public int seedRetryBudget = 8;
}
