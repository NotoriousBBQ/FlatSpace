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

    // Whether a docked ship of this kind counts as a ship for presence: vision, contact and the fleet summary. Bunkers are
    // planetary defenses, not ships (siegeships DO count, like warships).
    public static bool CountsAsShip(ShipKind k) => k != ShipKind.Bunker;

    public ShipKind Kind;
    public int Owner = Planet.NoOwner;
    public ShipData Template;
    public List<string> ResearchSnapshot = new List<string>();
    // Damage taken (0 = full health). Stored, not current health: an older save loads at full health, and the Health stat
    // (from the research snapshot) is fixed per ship. Current health is WarshipStats.CurrentHealth.
    public float Damage;
}
