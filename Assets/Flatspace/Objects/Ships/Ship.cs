using System.Collections.Generic;
using UnityEngine;

public class Ship : MonoBehaviour
{
    public enum ShipKind
    {
        ColonyShip,
        WarShip
    }

    public ShipKind Kind;
    public int Owner = Planet.NoOwner;
    public ShipData Template;
    public List<string> ResearchSnapshot = new List<string>();
}
