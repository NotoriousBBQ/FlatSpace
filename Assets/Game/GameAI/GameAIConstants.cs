using System;
using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(fileName = "GameAIConstants", menuName = "Scriptable Objects/GameAIConstants")]
public class GameAIConstants : ScriptableObject
{

    public float MoraleStep;
    public float DefaultTravelSpeed;
    public int MaxPathNodesToSearch;
    public float ExpandPopulationTrigger;
    public List<ModifierForPlanetStrategy> FoodWorkerAdjustment;
    public List<ModifierForPlanetStrategy> GrotsitWorkerAdjustment;

}
