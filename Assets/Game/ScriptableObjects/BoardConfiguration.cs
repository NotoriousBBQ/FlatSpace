using UnityEngine;
using System;
using System.Collections;
using System.Collections.Generic;

[CreateAssetMenu(fileName = "BoardConfiguration", menuName = "Scriptable Objects/BoardConfiguration")]
public class BoardConfiguration : ScriptableObject
{
    public List<PlanetSpawnData> _planetSpawnData;
}
