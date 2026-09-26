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
        public int Count;                 // every docked ship of that owner, warships and colony ships
        public Ship.ShipKind IconKind;    // WarShip if the owner has any warship here, else ColonyShip
    }

    /// <summary>One group per owner with ships docked here, ordered by player id with ownerless ships last.</summary>
    public static List<Group> ForPlanet(Planet planet)
    {
        return planet.DockedShips
            .GroupBy(s => s.Owner)
            .Select(g => new Group
            {
                Owner    = g.Key,
                Count    = g.Count(),
                IconKind = g.Any(s => s.Kind == Ship.ShipKind.WarShip)
                    ? Ship.ShipKind.WarShip : Ship.ShipKind.ColonyShip,
            })
            .OrderBy(g => g.Owner < 0 ? 1 : 0)
            .ThenBy(g => g.Owner)
            .ToList();
    }

    /// <summary>The owning player's color; the neutral color for ownerless or out-of-range owners.</summary>
    public static Color32 ColorFor(int owner)
        => owner >= 0 && owner < Player.PlayerColors.Count ? Player.PlayerColors[owner] : Player.NoPlayerColor;
}
