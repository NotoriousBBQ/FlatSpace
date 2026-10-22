using System.Collections.Generic;
using UnityEngine;

public class Ship : MonoBehaviour
{
    public enum ShipKind
    {
        ColonyShip,
        WarShip,
        SiegeShip,
        Bunker
    }

    // Kinds that fight in ship combat (Bunkers defend planets through their own rules).
    public static bool IsFightingKind(ShipKind k) => k == ShipKind.WarShip || k == ShipKind.SiegeShip;

    public ShipKind Kind;
    public int Owner = Planet.NoOwner;
    public ShipData Template;
    public List<string> ResearchSnapshot = new List<string>();
    // Damage taken (0 = full health). Stored, not current health: an older save loads at full health, and the Health stat
    // (from the research snapshot) is fixed per ship. Current health is WarshipStats.CurrentHealth.
    public float Damage;
}
