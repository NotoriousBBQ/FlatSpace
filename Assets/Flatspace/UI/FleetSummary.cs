using System.Collections.Generic;
using System.Linq;
using UnityEngine;

/// <summary>
/// Presentation helper: a planet's docked ships grouped into one fleet per owning player, for the
/// fleet icons on the planet marker and in the planet detail panel. Reads only planet state (no
/// Gameboard.Instance) so the Editor self-check can drive it directly.
/// </summary>
public static class FleetSummary
{
    public struct Group
    {
        public int Owner;                 // player id; negative = ownerless
        public int Count;                 // every docked ship of that owner except bunkers
        public Ship.ShipKind IconKind;    // WarShip if the owner has any warship here, else SiegeShip if any siegeship, else ColonyShip
        public int FleetId;               // the human's fleet at this planet; 0 for every other owner
    }

    /// <summary>
    /// One group per owner with ships docked here, ordered by player id with ownerless ships last. The human's ships (humanId, -1 = none)
    /// are split further into one group per fleet id, in id order; no other owner's fleet ids are read.
    /// </summary>
    public static List<Group> ForPlanet(Planet planet, int humanId = -1)
    {
        return planet.DockedShips
            .Where(s => Ship.CountsAsShip(s.Kind))   // bunkers are planetary defenses, not part of any fleet
            .GroupBy(s => (s.Owner, Fleet: s.Owner == humanId ? s.FleetId : 0))
            .Select(g => new Group
            {
                Owner    = g.Key.Owner,
                FleetId  = g.Key.Fleet,
                Count    = g.Count(),
                IconKind = g.Any(s => s.Kind == Ship.ShipKind.WarShip) ? Ship.ShipKind.WarShip
                    : g.Any(s => s.Kind == Ship.ShipKind.SiegeShip) ? Ship.ShipKind.SiegeShip
                    : Ship.ShipKind.ColonyShip,
            })
            .OrderBy(g => g.Owner < 0 ? 1 : 0)
            .ThenBy(g => g.Owner)
            .ThenBy(g => g.FleetId)
            .ToList();
    }

    /// <summary>The icon sprite for a fleet group's IconKind (or any ship kind); null when that kind has no template.</summary>
    public static Sprite IconFor(GameAIConstants constants, Ship.ShipKind kind)
    {
        ShipData data;
        switch (kind)
        {
            case Ship.ShipKind.WarShip: data = constants.warShipData; break;
            case Ship.ShipKind.SiegeShip: data = constants.siegeShipData; break;
            case Ship.ShipKind.Bunker: data = constants.bunkerData; break;
            default: data = constants.colonyShipData; break;
        }
        return data != null ? data.shipIcon : null;
    }

    /// <summary>The owning player's color; the neutral color for ownerless or out-of-range owners.</summary>
    public static Color32 ColorFor(int owner)
        => owner >= 0 && owner < Player.PlayerColors.Count ? Player.PlayerColors[owner] : Player.NoPlayerColor;
}
