using System;
using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(fileName = "GameAIConstants", menuName = "Scriptable Objects/GameAIConstants")]
public class GameAIConstants : ScriptableObject
{

    public float moraleStep;
    public float defaultTravelSpeed;
    public float expandPopulationTrigger;
    public int maxPathNodesForResourceDistribution;
    public List<ModifierListForPlanetStrategy> productionModifierLists;
    public List<PlanetResourceData> resourceData;
    public ShipData colonyShipData;
    public ShipData warShipData;

    [Header("Ship transport")]
    // Longest trip (path node count) a ship transport may take.
    public int maxPathNodesForShipTransport = 10;
    // Desired garrison (warships) per category. A planet uses the largest of its categories.
    public int garrisonSpecialized = 4;         // category 1: Desert, Industrial, Farm, Ocean
    public int garrisonOuter = 6;               // category 2: colonized with an uncolonized neighbour
    public int garrisonPrime = 4;               // category 3: Prime
    public int garrisonHighTraffic = 2;         // category 4: many connections
    public int garrisonHighlySpecialized = 1;   // category 5: Verdant, Desolate
    // A planet with at least this many neighbours is "high traffic".
    public int highTrafficConnectionCount = 4;
    // Category-5-only planets take ships only once total warships >= this * colonized planet count.
    public float category5UnlockShipsPerColonizedPlanet = 3f;

    [Header("Assault (Consolidate)")]
    // Force committed to a known enemy-occupied planet: ceil(enemy known docked warships x this)...
    public float assaultRatio = 1.5f;
    // ...but never fewer than this.
    public int assaultMinimumShips = 3;
}
