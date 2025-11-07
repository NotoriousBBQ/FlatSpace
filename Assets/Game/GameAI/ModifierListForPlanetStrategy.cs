using UnityEngine;

[CreateAssetMenu(fileName = "ModifierListForPlanetStrategy", menuName = "Scriptable Objects/AIModifier/ModifierListForPlanetStrategy")]
public class ModifierListForPlanetStrategy : ScriptableObject
{
    public Planet.PlanetStrategy planetStrategy;
    public int foodModifier = 1;    
    public int grotsitsModifier = 1;    
    public int researchModifier = 1;    
    public int industryModifier = 1;    
}
