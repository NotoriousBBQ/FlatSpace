using UnityEngine;

[CreateAssetMenu(fileName = "PlanetSpawnData", menuName = "Scriptable Objects/PlanetSpawnData")]
public class PlanetSpawnData : ScriptableObject
{
    public string _planetName;
    public Vector3 _planetPosition;
    public Planet.PlanetType _planetType;
    public PlanetResourceData _resourceData;
}
