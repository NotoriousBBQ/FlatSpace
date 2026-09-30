using System.Collections.Generic;

namespace FlatSpace.AI
{
    /// <summary>
    /// One player's view of blockades this turn: the planets it can SEE whose docked-offense blockade value against it is
    /// positive. Visible = every planet the player has presence at (a GetVisionSourcePlanets planet: population or a
    /// docked ship) plus their direct neighbours. A planet the player cannot see is treated as unblockaded. Pure (no
    /// Gameboard.Instance) and derived, so it is rebuilt each turn and never saved.
    /// </summary>
    public class BlockadeView
    {
        private readonly Dictionary<string, (float value, int blocker)> _blockaded
            = new Dictionary<string, (float value, int blocker)>();

        public IEnumerable<string> BlockadedNames => _blockaded.Keys;

        public bool IsBlockaded(string planetName)
            => planetName != null && _blockaded.ContainsKey(planetName);

        public float Value(string planetName)
            => planetName != null && _blockaded.TryGetValue(planetName, out var entry) ? entry.value : 0f;

        public int Blocker(string planetName)
            => planetName != null && _blockaded.TryGetValue(planetName, out var entry) ? entry.blocker : Planet.NoOwner;

        public static BlockadeView Build(GameAIMap map, int playerId, BlockadeSystem blockade)
        {
            var view = new BlockadeView();
            var visible = new HashSet<string>();
            foreach (var source in map.GetVisionSourcePlanets(playerId))
            {
                visible.Add(source.Planet.PlanetName);
                foreach (var neighbour in map.GetNeighbours(source.Planet.PlanetName))
                    visible.Add(neighbour);
            }

            foreach (var name in visible)
            {
                var planet = map.GetPlanet(name);
                if (planet == null) continue;
                var value = blockade.Value(planet, playerId, out var blocker);
                if (value > 0f) view._blockaded[name] = (value, blocker);
            }
            return view;
        }

        /// <summary>A view in which exactly these planets are blockaded (value 1, no blocker). For planning tests.</summary>
        public static BlockadeView Of(params string[] blockadedPlanets)
        {
            var view = new BlockadeView();
            foreach (var name in blockadedPlanets) view._blockaded[name] = (1f, Planet.NoOwner);
            return view;
        }
    }
}
