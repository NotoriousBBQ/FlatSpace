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
    [FormerlySerializedAs("_baseIndustrialProduction")]
    public float _baseIndustryProduction;
    public float _industryProduction;
    public int _maxPopulation;
    public Planet.PlanetStrategy _initialStrategy;
    // Extra grotsits of headroom (per turn) this planet type is allowed to run its improvement upkeep beyond its own
    // GetGrotsitsCapacity(), i.e. how much of its upkeep this type may cover by importing grotsits rather than
    // producing all of it itself. 0 (the default) means fully self-funding. See Planet.CanAffordImprovement.
    public float _grotsitsImportAllowance;
}
 
