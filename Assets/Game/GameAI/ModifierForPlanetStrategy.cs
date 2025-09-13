using UnityEngine;

[CreateAssetMenu(fileName = "ModifierForPlanetStrategy", menuName = "Scriptable Objects/AIModifier/ModifierForPlanetStrategy")]
public class ModifierForPlanetStrategy : ScriptableObject
{
    public Planet.PlanetStrategy planetStrategy;
    public int modifier = 1;    
}
