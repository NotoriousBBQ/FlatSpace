using System.Collections.Generic;
using System.Linq;

namespace FlatSpace
{
    namespace Human
    {
        /// <summary>
        /// Which events stop the timer. Only some kinds can pause at all (the rest are messages); each pausing kind can be switched
        /// on or off. A rival's event never pauses, however it is set: the human is stopped for what happens to them.
        /// </summary>
        public class HumanPauseSettings
        {
            /// <summary>The kinds that can pause, in the order they are listed to the human.</summary>
            public static readonly HumanEventKind[] PauseKinds =
            {
                HumanEventKind.PlanetAttacked, HumanEventKind.PlanetBlockaded, HumanEventKind.PlanetLost, HumanEventKind.FirstContact,
                HumanEventKind.WarDeclared, HumanEventKind.PeaceMade, HumanEventKind.FleetArrived,
                HumanEventKind.ShortageFood, HumanEventKind.ShortageGrotsits,
                HumanEventKind.ProductionComplete, HumanEventKind.ResearchComplete, HumanEventKind.ColonyShipReady,
                HumanEventKind.PlanetIdle, HumanEventKind.ResearchIdle,
            };

            private readonly HashSet<HumanEventKind> _enabled = new HashSet<HumanEventKind>();

            public static bool CanPause(HumanEventKind kind) => PauseKinds.Contains(kind);

            /// <summary>The owner's defaults: the events where waiting costs something are on; the routine completions are off.</summary>
            public static HumanPauseSettings Defaults()
            {
                var settings = new HumanPauseSettings();
                foreach (var kind in new[]
                         {
                             HumanEventKind.PlanetAttacked, HumanEventKind.PlanetBlockaded, HumanEventKind.PlanetLost, HumanEventKind.FirstContact,
                             HumanEventKind.WarDeclared, HumanEventKind.PeaceMade, HumanEventKind.FleetArrived,
                             HumanEventKind.ShortageFood, HumanEventKind.ShortageGrotsits,
                         })
                    settings._enabled.Add(kind);
                return settings;
            }

            public bool IsEnabled(HumanEventKind kind) => _enabled.Contains(kind);

            /// <summary>Switches a kind on or off. A kind that cannot pause is ignored.</summary>
            public void Set(HumanEventKind kind, bool enabled)
            {
                if (!CanPause(kind)) return;
                if (enabled) _enabled.Add(kind);
                else _enabled.Remove(kind);
            }

            /// <summary>True when any of the events is an enabled one that happened to the human; first is the first such event.</summary>
            public bool ShouldPause(IEnumerable<HumanEvent> events, out HumanEvent first)
            {
                foreach (var e in events)
                {
                    if (e.Rival || !IsEnabled(e.Kind)) continue;
                    first = e;
                    return true;
                }
                first = default;
                return false;
            }

            /// <summary>The enabled kinds as ints, for the save.</summary>
            public List<int> ToSave() => _enabled.OrderBy(k => (int)k).Select(k => (int)k).ToList();

            /// <summary>Restores a saved list; a save without the setting (has false, an older save) gets the defaults.</summary>
            public static HumanPauseSettings FromSave(bool has, List<int> kinds)
            {
                if (!has) return Defaults();
                var settings = new HumanPauseSettings();
                if (kinds != null)
                    foreach (var value in kinds)
                        settings.Set((HumanEventKind)value, true);
                return settings;
            }
        }
    }
}
