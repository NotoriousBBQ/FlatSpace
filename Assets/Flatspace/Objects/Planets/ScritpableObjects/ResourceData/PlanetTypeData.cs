using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.Serialization;

[CreateAssetMenu(fileName = "PlanetResourceData", menuName = "Scriptable Objects/PlanetResourceData")]
public class PlanetResourceData : ScriptableObject
{
    public Planet.PlanetType _planetType;
    public int _initialPopulation;
    public float _baseFoodProduction;
    public float _foodProduction;
    public float _baseGrotsitsProduction;
    [FormerlySerializedAs("_grotsitProduction")]
    public float _grotsitsProduction;
    public float _baseResearchProduction;
    public float _researchProduction;
    public float _baseIndustrialProduction;
    public float _industryProduction;
    public int _maxPopulation;
    public Planet.PlanetStrategy _initialStrategy;
}
 
