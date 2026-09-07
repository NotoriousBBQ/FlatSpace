using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(fileName = "PlanetSpawnData", menuName = "Scriptable Objects/PlanetSpawnData")]
public class PlanetSpawnData : ScriptableObject
{
    public string _planetName;
    public Vector3 _planetPosition;
    public Planet.PlanetType _planetType;
    public PlanetResourceData _resourceData;

    // Names of planets this planet is directly connected to. Empty => the
    // pathing graph falls back to the "everything within MaxConnectionSize"
    // rule for this board (see PathingSystem.InitializePathMap).
    public List<string> _connections = new List<string>();
}
