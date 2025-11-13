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

}
