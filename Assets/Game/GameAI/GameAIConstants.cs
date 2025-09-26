using System;
using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(fileName = "GameAIConstants", menuName = "Scriptable Objects/GameAIConstants")]
public class GameAIConstants : ScriptableObject
{

    public float moraleStep;
    public float defaultTravelSpeed;
    public float expandPopulationTrigger;
    public List<ModifierForPlanetStrategy> foodWorkerAdjustment;
    public List<ModifierForPlanetStrategy> grotsitWorkerAdjustment;
    public List<PlanetResourceData> resourceData;

}
