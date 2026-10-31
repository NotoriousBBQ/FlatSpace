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
    // How much this planet type is worth as a specialised producer, 0..1 (Verdant 1, Desolate 1, Farm 0.5; types whose output is
    // local-only today stay 0). Read by the AI's initiated blockades (colony target value); see GameAIMap.ProducerValue.
    public float _producerValue;
    // What one planet of this type is worth to its owner, in the same units as a fleet's or a tech's value; a player's score is the sum
    // over everything it owns. Starting points, retune in the type assets: Prime 10, Verdant 6, Desolate 6, Industrial 5, Farm 4,
    // Ocean 4, Desert 4, Normal 2. Read through Planet.PlanetValue.
    public float _planetValue;
}
 
