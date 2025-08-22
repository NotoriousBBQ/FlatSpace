using UnityEngine;

[CreateAssetMenu(fileName = "PlanetResourceData", menuName = "Scriptable Objects/PlanetResourceData")]
public class PlanetResourceData : ScriptableObject
{
     public Planet.PlanetType _planetType;
     public int _initialPopulation;
     public float _initialFood;
     public float _foodProduction;
     public float _grotsitProduction;
     public int _maxPopulation;
}
