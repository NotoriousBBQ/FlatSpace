using System.Collections.Generic;
using System.Linq;
using FlatSpace.AI;

namespace FlatSpace
{
    namespace Human
    {
        /// <summary>
        /// The words of the proposal dialog (docs/superpowers/specs/2026-10-30-ask-and-trade-design.md, "The proposal"). Pure: band labels, item
        /// lines, the message the chat model is told, and the fixed template reply shown at once while the model thinks.
        /// </summary>
        public static class ProposalText
        {
            public static readonly string[] BandLabels = { "Very unlikely", "Unlikely", "Uncertain", "Likely", "Very likely" };

            public static string BandLabel(DealBand band) => BandLabels[(int)band];

            /// <summary>The same item (kind, name and planet; a fleet's count does not matter).</summary>
            public static bool SameItem(DealItem a, DealItem b)
                => a != null && b != null && a.kind == b.kind && a.name == b.name && a.planet == b.planet;

            /// <summary>An item as a row of the dialog; "I give" rows speak for the human, "I get" rows for the rival.</summary>
            public static string RowText(DealItem item, bool inGive)
            {
                switch (item.kind)
                {
                    case "planet": return "Planet: " + item.name;
                    case "fleet": return $"{item.count} {item.name} at {item.planet}";
                    case "tech": return "Tech: " + item.name;
                    case "endWar": return inGive ? "I end the war with them" : "They end the war with me";
                    default: return item.kind;
                }
            }

            /// <summary>An item in a sentence ("planet Farm 3"), for the message the rival is told.</summary>
            public static string Describe(DealItem item)
            {
                switch (item.kind)
                {
                    case "planet": return "planet " + item.name;
                    case "fleet": return $"{item.count} {item.name} at {item.planet}";
                    case "tech": return "the tech " + item.name;
                    case "endWar": return "an end to the war";
                    default: return item.kind;
                }
            }

            public static string DescribeAll(IEnumerable<DealItem> items)
            {
                var parts = items.Select(Describe).ToList();
                return parts.Count == 0 ? "nothing" : string.Join(", ", parts);
            }

            /// <summary>What the rival is told happened (the model reacts better to a sentence than to a result code).</summary>
            public static string OutcomeMessage(string human, bool accepted, IEnumerable<DealItem> give, IEnumerable<DealItem> get)
            {
                var terms = $"{human} offered {DescribeAll(give)} and asked for {DescribeAll(get)}.";
                return accepted ? $"{terms} You accepted the deal." : $"{terms} You declined the deal.";
            }

            private static readonly string[][] Accepted =
            {
                new[] { "Your offer is acceptable. For now.", "Done. Do not mistake this for friendship.", "Take it. We are finished here." },
                new[] { "Thank you, thank you! We accept!", "Yes, of course, whatever you say.", "We agree. Please, no trouble." },
                new[] { "The stars align. We accept.", "A fair orbit. It is agreed.", "So it is written. We accept." },
            };

            private static readonly string[][] Declined =
            {
                new[] { "Laughable. No.", "You insult us. Declined.", "Not a chance." },
                new[] { "Please, we cannot... not this.", "We are sorry, we must say no.", "Forgive us, but no." },
                new[] { "The path does not bend that way. No.", "Not under this sky.", "The tide does not carry us there. Declined." },
            };

            /// <summary>tone: 0 hostile, 1 fearful, 2 enigmatic (the order of ChatClient.Tone). variant picks one of the fixed lines.</summary>
            public static string TemplateReply(bool accepted, int tone, int variant)
            {
                var set = (accepted ? Accepted : Declined)[tone < 0 || tone > 2 ? 2 : tone];
                return set[((variant % set.Length) + set.Length) % set.Length];
            }
        }
    }
}
