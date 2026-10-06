using System.Collections.Generic;
using System.Linq;
using Flatspace.Objects.Production;

namespace FlatSpace.AI
{
    /// <summary>
    /// A warship's effective stats: its ShipData template value plus, for each stat, the tiers of that stat's
    /// research line the ship carries (its ResearchSnapshot) x (Max - base) / (tiers in that line). Pure: it never
    /// touches Gameboard.Instance, so self-checks can drive it directly. The formula lives only here.
    /// </summary>
    public class WarshipStats
    {
        public const string OffenseKey = "Warship Offense";
        public const string HealthKey = "Warship Health";
        public const string DefenseKey = "Warship Defense";

        private readonly List<CatalogItem> _items;

        public WarshipStats(IEnumerable<CatalogItem> researchItems)
        {
            _items = researchItems != null ? researchItems.ToList() : new List<CatalogItem>();
        }

        public static bool IsWarshipImprovement(CatalogItem item)
            => item != null && item.type == "Ship Improvement" && item.subType == "Warship";

        /// <summary>Number of catalog tiers in the research line that improves this stat (effect == key).</summary>
        public int TierCount(string key)
            => _items.Count(i => IsWarshipImprovement(i) && i.effect == key);

        private int TiersCarried(ICollection<string> snapshot, string key)
            => snapshot == null
                ? 0
                : _items.Count(i => IsWarshipImprovement(i) && i.effect == key && snapshot.Contains(i.itemName));

        private float Stat(float baseValue, float maxValue, string key, ICollection<string> snapshot)
        {
            var tiers = TierCount(key);
            if (tiers == 0) return baseValue;   // no research line for this stat: nothing to add, nothing to divide
            return baseValue + TiersCarried(snapshot, key) * (maxValue - baseValue) / tiers;
        }

        public float Offense(ShipData template, ICollection<string> snapshot)
            => template == null ? 0f : Stat(template.shipOffense, template.shipOffenseMax, OffenseKey, snapshot);

        public float Health(ShipData template, ICollection<string> snapshot)
            => template == null ? 0f : Stat(template.shipHealth, template.shipHealthMax, HealthKey, snapshot);

        public float Defense(ShipData template, ICollection<string> snapshot)
            => template == null ? 0f : Stat(template.shipDefense, template.shipDefenseMax, DefenseKey, snapshot);

        /// <summary>Health left: the Health stat minus the damage taken, never below 0.</summary>
        public float CurrentHealth(ShipData template, ICollection<string> snapshot, float damage)
            => System.Math.Max(0f, Health(template, snapshot) - damage);

        /// <summary>
        /// Offense scaled by how much health is left (Offense x current health / Health stat). No floor, by the owner's
        /// choice; 0 when the Health stat is 0 (colony ships, a ship with no template).
        /// </summary>
        public float EffectiveOffense(ShipData template, ICollection<string> snapshot, float damage)
        {
            var max = Health(template, snapshot);
            if (max <= 0f) return 0f;
            return Offense(template, snapshot) * CurrentHealth(template, snapshot, damage) / max;
        }

        public float CurrentHealth(Ship ship) => CurrentHealth(ship.Template, ship.ResearchSnapshot, ship.Damage);

        public float EffectiveOffense(Ship ship) => EffectiveOffense(ship.Template, ship.ResearchSnapshot, ship.Damage);

        /// <summary>Speed has no research line: it stays the template's fixed value.</summary>
        public float Speed(ShipData template) => template == null ? 0f : template.shipSpeed;

        /// <summary>Names of the warship improvements already researched, in catalog order.</summary>
        public static List<string> ResearchedNames(IEnumerable<CatalogItem> researchItems)
            => researchItems == null
                ? new List<string>()
                : researchItems.Where(i => i.researched && IsWarshipImprovement(i)).Select(i => i.itemName).ToList();
    }
}
