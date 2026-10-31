using System;
using System.Collections.Generic;
using System.Linq;
using FlatSpace.AI;

namespace FlatSpace
{
    namespace Human
    {
        /// <summary>A button of the Diplomacy screen: whether it can be pressed now and, if not, why.</summary>
        public struct DiplomacyButton
        {
            public bool Enabled;
            public string Reason;
        }

        /// <summary>One entry of the rival list.</summary>
        public class RivalRow
        {
            public int Rival;
            public string Label;
            public bool AtWar;
            public string State;   // War, Peace or Truce
        }

        /// <summary>The rival's docked warships on one planet the human sees.</summary>
        public class FleetLine
        {
            public string Planet;
            public int Warships;
            public float Strength;
        }

        /// <summary>The rival's stance toward another player the human has met.</summary>
        public class OtherStance
        {
            public int Rival;
            public string Label;
            public Stance Stance;
        }

        /// <summary>Everything the right-hand pane of the Diplomacy screen shows for one rival.</summary>
        public class RivalView
        {
            public int Rival;
            public string Label;
            public Stance MyStance, TheirStance;
            public int MyChangeTurn, TheirChangeTurn;   // the turn each stance last changed; -1 = never
            public int TruceEndsTurn;                   // 0 = no truce running
            public bool AtWar;
            public string StateText;
            public float Hostility, HostilityMax;       // the rival's hostility toward the human
            public List<OtherStance> Others = new List<OtherStance>();
            public List<FleetLine> Fleets = new List<FleetLine>();
            public int TotalWarships;
            public float TotalStrength, MyStrength;
            public DiplomacyButton EndWar, DeclareWar, Surrender, DemandSurrender;
            public PlayerValue.ScoreBreakdown MyScore, RivalScore;   // zeros when no scoreOf was given
        }

        /// <summary>
        /// The Diplomacy screen's content (docs/superpowers/specs/2026-10-29-diplomacy-screen-design.md). Pure: it never touches Gameboard.Instance.
        /// A rival the human has not met (not in ContactPlayers) appears nowhere, neither as a row nor as another rival's stance. The button states
        /// come from the command processor's own Check (the `refusal` function), so the screen cannot offer what the processor would refuse.
        /// </summary>
        public static class HumanDiplomacyView
        {
            public const string DemandSurrenderReason = "Needs negotiation";

            public static string Label(int player) => $"Player {player}";

            /// <summary>False in legacy mode (diplomacy switched off): every rival with contact is an enemy and no stance exists to show.</summary>
            public static bool IsOn(GameAIMap map) => map != null && map.Diplomacy != null && map.Diplomacy.Enabled;

            /// <summary>
            /// The rivals shown: the players in contact, plus any the human has declared war on whose ships have since left (the war stays on, and
            /// so must the way to end it). A player never met is in neither set.
            /// </summary>
            public static List<int> Known(GameAIMap map, int humanId)
            {
                var known = new SortedSet<int>(map.Knowledge.ContactPlayers(map, humanId));
                foreach (var rival in map.Diplomacy.Rivals(humanId))
                    if (rival != humanId && map.Diplomacy.StanceToward(humanId, rival) == Stance.War) known.Add(rival);
                return known.ToList();
            }

            /// <summary>The rivals the human has met, the ones at war first, then by player id. Empty when diplomacy is off.</summary>
            public static List<RivalRow> Rivals(GameAIMap map, int humanId)
            {
                var rows = new List<RivalRow>();
                if (!IsOn(map)) return rows;
                var diplomacy = map.Diplomacy;
                foreach (var rival in Known(map, humanId))
                {
                    var atWar = diplomacy.IsAtWar(humanId, rival, true);
                    var truce = diplomacy.InTruce(humanId, rival, diplomacy.Turn);
                    rows.Add(new RivalRow { Rival = rival, Label = Label(rival), AtWar = atWar, State = atWar ? "War" : truce ? "Truce" : "Peace" });
                }
                return rows.OrderBy(r => r.AtWar ? 0 : 1).ThenBy(r => r.Rival).ToList();
            }

            /// <summary>
            /// The pane for one rival, or null when diplomacy is off or the rival is not a contact. seesPlanet is the planets whose docked ships the human sees (the board's rule, not the AI's
            /// wider knowledge); refusal(type, rival) is HumanCommandProcessor.Check for declareWar / makePeace / surrender (null = accepted).
            /// </summary>
            public static RivalView Build(GameAIMap map, int humanId, int rival, Func<string, bool> seesPlanet, WarshipStats stats,
                Func<string, int, string> refusal, Func<int, PlayerValue.ScoreBreakdown> scoreOf = null)
            {
                if (!IsOn(map)) return null;
                var contacts = Known(map, humanId);
                if (!contacts.Contains(rival)) return null;

                var diplomacy = map.Diplomacy;
                var turn = diplomacy.Turn;
                var mine = diplomacy.Get(humanId, rival);
                var theirs = diplomacy.Get(rival, humanId);
                var truceEnds = Math.Max(mine.TruceUntil, theirs.TruceUntil);
                if (turn >= truceEnds) truceEnds = 0;
                var atWar = diplomacy.IsAtWar(humanId, rival, true);

                var view = new RivalView
                {
                    Rival = rival,
                    Label = Label(rival),
                    MyStance = mine.Stance,
                    TheirStance = theirs.Stance,
                    MyChangeTurn = mine.LastChangeTurn == DiplomacyState.NeverChanged ? -1 : mine.LastChangeTurn,
                    TheirChangeTurn = theirs.LastChangeTurn == DiplomacyState.NeverChanged ? -1 : theirs.LastChangeTurn,
                    TruceEndsTurn = truceEnds,
                    AtWar = atWar,
                    Hostility = diplomacy.Hostility(rival, humanId),
                    HostilityMax = map.GameAIConstants != null ? map.GameAIConstants.hostilityMax : 100f,
                };

                if (truceEnds > 0) view.StateText = $"Peace (truce until turn {truceEnds})";
                else if (mine.Stance == Stance.War) view.StateText = "War";
                else if (atWar) view.StateText = "Peace (mine), War (theirs): still at war";
                else view.StateText = "Peace";

                foreach (var other in contacts)
                    if (other != rival)
                        view.Others.Add(new OtherStance { Rival = other, Label = Label(other), Stance = diplomacy.StanceToward(rival, other) });

                foreach (var planet in map.PlanetList)
                {
                    if (!seesPlanet(planet.PlanetName)) continue;
                    var ships = planet.DockedShips.Where(s => s.Owner == rival && s.Kind == Ship.ShipKind.WarShip).ToList();
                    if (ships.Count == 0) continue;
                    view.Fleets.Add(new FleetLine { Planet = planet.PlanetName, Warships = ships.Count, Strength = FleetStrength.Of(ships, stats) });
                }
                view.Fleets = view.Fleets.OrderByDescending(l => l.Strength).ThenBy(l => l.Planet, StringComparer.Ordinal).ToList();
                view.TotalWarships = view.Fleets.Sum(l => l.Warships);
                view.TotalStrength = view.Fleets.Sum(l => l.Strength);
                view.MyStrength = FleetStrength.Mine(map, humanId, stats);

                if (scoreOf != null)
                {
                    view.MyScore = scoreOf(humanId);
                    view.RivalScore = scoreOf(rival);
                }

                view.DeclareWar = ButtonFor(refusal("declareWar", rival), truceEnds);
                view.Surrender = ButtonFor(refusal("surrender", rival), truceEnds);
                var endWarReason = refusal("makePeace", rival);
                view.EndWar = ButtonFor(endWarReason, truceEnds);
                if (endWarReason == "AlreadyPeace" && atWar)
                    view.EndWar.Reason += " - they are still at war with you; Surrender ends it";
                view.DemandSurrender = new DiplomacyButton { Enabled = false, Reason = DemandSurrenderReason };
                return view;
            }

            private static DiplomacyButton ButtonFor(string reason, int truceEndsTurn)
                => new DiplomacyButton { Enabled = reason == null, Reason = RefusalText(reason, truceEndsTurn) };

            /// <summary>Plain text for a command refusal; empty for null (accepted).</summary>
            public static string RefusalText(string reason, int truceEndsTurn)
            {
                switch (reason)
                {
                    case null: return "";
                    case "AlreadyAtWar": return "Already at war";
                    case "Truce": return truceEndsTurn > 0 ? $"Truce until turn {truceEndsTurn}" : "Truce";
                    case "NoContact": return "No contact";
                    case "DiplomacyOff": return "Diplomacy is off";
                    case "NotAtWar": return "Not at war";
                    case "AlreadyPeace": return "Your stance is already Peace";
                    default: return reason;
                }
            }
        }
    }
}
