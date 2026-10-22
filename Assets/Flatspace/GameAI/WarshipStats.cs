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

        public const string WarshipSubtype = "Warship";

        /// <summary>Catalog subtype string of a ship kind. Case-exact and load-bearing ("Warship", lowercase s).</summary>
        public static string SubtypeOf(Ship.ShipKind kind)
        {
            switch (kind)
            {
                case Ship.ShipKind.WarShip: return "Warship";
                case Ship.ShipKind.SiegeShip: return "Siegeship";
                case Ship.ShipKind.Bunker: return "Bunker";
                default: return "ColonyShip";
            }
        }

        public static bool IsWarshipImprovement(CatalogItem item) => IsImprovement(item, WarshipSubtype);

        public static bool IsImprovement(CatalogItem item, string subtype)
            => item != null && item.type == "Ship Improvement" && item.subType == subtype;

        /// <summary>Number of catalog tiers in the research line that improves this stat (effect == key).</summary>
        public int TierCount(string key) => TierCount(key, WarshipSubtype);

        public int TierCount(string key, string subtype)
            => _items.Count(i => IsImprovement(i, subtype) && i.effect == key);

        private int TiersCarried(ICollection<string> snapshot, string key, string subtype)
            => snapshot == null
                ? 0
                : _items.Count(i => IsImprovement(i, subtype) && i.effect == key && snapshot.Contains(i.itemName));

        // The one place the stat formula lives. The research line key is "<subtype> <stat>".
        private float Stat(float baseValue, float maxValue, string stat, string subtype, ICollection<string> snapshot)
        {
            var key = subtype + " " + stat;
            var tiers = TierCount(key, subtype);
            if (tiers == 0) return baseValue;   // no research line for this stat: nothing to add, nothing to divide
            return baseValue + TiersCarried(snapshot, key, subtype) * (maxValue - baseValue) / tiers;
        }

        public float Offense(ShipData template, ICollection<string> snapshot) => Offense(template, snapshot, WarshipSubtype);

        public float Offense(ShipData template, ICollection<string> snapshot, string subtype)
            => template == null ? 0f : Stat(template.shipOffense, template.shipOffenseMax, "Offense", subtype, snapshot);

        public float Health(ShipData template, ICollection<string> snapshot) => Health(template, snapshot, WarshipSubtype);

        public float Health(ShipData template, ICollection<string> snapshot, string subtype)
            => template == null ? 0f : Stat(template.shipHealth, template.shipHealthMax, "Health", subtype, snapshot);

        public float Defense(ShipData template, ICollection<string> snapshot) => Defense(template, snapshot, WarshipSubtype);

        public float Defense(ShipData template, ICollection<string> snapshot, string subtype)
            => template == null ? 0f : Stat(template.shipDefense, template.shipDefenseMax, "Defense", subtype, snapshot);

        /// <summary>Bombard stat: template base plus the carried tiers of the "&lt;subtype&gt; Bombard" line.</summary>
        public float Bombard(ShipData template, ICollection<string> snapshot, string subtype = WarshipSubtype)
            => template == null ? 0f : Stat(template.shipBombard, template.shipBombardMax, "Bombard", subtype, snapshot);

        /// <summary>Health left: the Health stat minus the damage taken, never below 0.</summary>
        public float CurrentHealth(ShipData template, ICollection<string> snapshot, float damage)
            => CurrentHealth(template, snapshot, damage, WarshipSubtype);

        public float CurrentHealth(ShipData template, ICollection<string> snapshot, float damage, string subtype)
            => System.Math.Max(0f, Health(template, snapshot, subtype) - damage);

        /// <summary>
        /// Offense scaled by how much health is left (Offense x current health / Health stat). No floor, by the owner's
        /// choice; 0 when the Health stat is 0 (colony ships, a ship with no template).
        /// </summary>
        public float EffectiveOffense(ShipData template, ICollection<string> snapshot, float damage)
            => EffectiveOffense(template, snapshot, damage, WarshipSubtype);

        public float EffectiveOffense(ShipData template, ICollection<string> snapshot, float damage, string subtype)
        {
            var max = Health(template, snapshot, subtype);
            if (max <= 0f) return 0f;
            return Offense(template, snapshot, subtype) * CurrentHealth(template, snapshot, damage, subtype) / max;
        }

        /// <summary>Bombard scaled by remaining health (bombard x current health / Health stat); 0 for a Health stat of 0.</summary>
        public float EffectiveBombard(Ship ship)
        {
            var subtype = SubtypeOf(ship.Kind);
            var max = Health(ship.Template, ship.ResearchSnapshot, subtype);
            if (max <= 0f) return 0f;
            return Bombard(ship.Template, ship.ResearchSnapshot, subtype)
                   * CurrentHealth(ship.Template, ship.ResearchSnapshot, ship.Damage, subtype) / max;
        }

        // Ship-based overloads pick the subtype from the ship's kind (a WarShip gives the same numbers as before).
        public float Offense(Ship ship) => Offense(ship.Template, ship.ResearchSnapshot, SubtypeOf(ship.Kind));

        public float Health(Ship ship) => Health(ship.Template, ship.ResearchSnapshot, SubtypeOf(ship.Kind));

        public float Defense(Ship ship) => Defense(ship.Template, ship.ResearchSnapshot, SubtypeOf(ship.Kind));

        public float CurrentHealth(Ship ship)
            => CurrentHealth(ship.Template, ship.ResearchSnapshot, ship.Damage, SubtypeOf(ship.Kind));

        public float EffectiveOffense(Ship ship)
            => EffectiveOffense(ship.Template, ship.ResearchSnapshot, ship.Damage, SubtypeOf(ship.Kind));

        /// <summary>Speed has no research line: it stays the template's fixed value.</summary>
        public float Speed(ShipData template) => template == null ? 0f : template.shipSpeed;

        /// <summary>Names of the warship improvements already researched, in catalog order.</summary>
        public static List<string> ResearchedNames(IEnumerable<CatalogItem> researchItems, string subtype = WarshipSubtype)
            => researchItems == null
                ? new List<string>()
                : researchItems.Where(i => i.researched && IsImprovement(i, subtype)).Select(i => i.itemName).ToList();
    }
}
