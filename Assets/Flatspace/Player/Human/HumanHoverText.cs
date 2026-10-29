using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using FlatSpace.AI;
using UnityEngine;

namespace FlatSpace
{
    namespace Human
    {
        /// <summary>
        /// The text and picking rules behind the hover popups (fleet icons and order lines in flight), the refusal sentences of the fleet
        /// drag, and which of a rival's lines the human may see. Pure: no Gameboard.Instance, so a self-check drives it directly.
        /// </summary>
        public static class HumanHoverText
        {
            /// <summary>
            /// Whether the human may see the ships of a fleet in detail (kinds, health): their own fleets always, and every fleet when no human plays.
            /// A rival's fleet shows only a ship count (ShipCount). A future debug mode (FUTURE_FEATURES.md) would make this true for everything.
            /// </summary>
            public static bool ShowFullFleetDetail(int owner, int humanId) => humanId < 0 || owner == humanId;

            /// <summary>"4 ships", "1 ship".</summary>
            public static string ShipCount(int count) => $"{count} {(count == 1 ? "ship" : "ships")}";

            /// <summary>"3 WarShip, strength 420": the fleet text with its strength (FleetStrength.Of) added; unchanged when the strength is 0 (no warships).</summary>
            public static string WithStrength(string text, float strength)
                => strength > 0f ? $"{text}, strength {strength.ToString("0", CultureInfo.InvariantCulture)}" : text;

            /// <summary>"3 WarShip, 1 SiegeShip": counts per kind in enum order; "no ships" for none.</summary>
            public static string Ships(IEnumerable<Ship.ShipKind> kinds)
            {
                var parts = kinds.GroupBy(k => k).OrderBy(g => g.Key).Select(g => $"{g.Count()} {g.Key}").ToList();
                return parts.Count == 0 ? "no ships" : string.Join(", ", parts);
            }

            /// <summary>
            /// The popup line for an order that draws a line in flight, or null for an order type that does not (departures, the
            /// in-progress flags): ships, food or grotsits with the amount, a colonist, or a colony's food rider, then the destination and the
            /// turns left.
            /// </summary>
            public static string Order(GameAI.GameAIOrder order, bool fullShipDetail = true)
            {
                if (order == null) return null;
                string what;
                switch (order.Type)
                {
                    case GameAI.GameAIOrder.OrderType.OrderTypeShipTransport:
                        if (order.Fleet == null) return null;
                        var count = order.Fleet.Snapshots != null && order.Fleet.Snapshots.Count > 0
                            ? order.Fleet.Snapshots.Count
                            : System.Convert.ToInt32(order.Data);
                        what = fullShipDetail ? Ships(Enumerable.Repeat(order.Fleet.Kind, count)) : ShipCount(count);
                        break;
                    case GameAI.GameAIOrder.OrderType.OrderTypeFoodTransport:
                        what = Amount(order.Data) + " Food";
                        break;
                    case GameAI.GameAIOrder.OrderType.OrderTypeGrotsitsTransport:
                        what = Amount(order.Data) + " Grotsits";
                        break;
                    case GameAI.GameAIOrder.OrderType.OrderTypePopulationTransport:
                        what = "Colonist";
                        break;
                    case GameAI.GameAIOrder.OrderType.OrderTypeColonyFoodRider:
                        what = Amount(order.Data) + " Food (colony rider)";
                        break;
                    default:
                        return null;
                }
                return $"{what} to {order.Target}, arrives in {order.TimingDelay} {(order.TimingDelay == 1 ? "turn" : "turns")}";
            }

            private static string Amount(object data)
                => System.Convert.ToSingle(data, CultureInfo.InvariantCulture).ToString("0.#", CultureInfo.InvariantCulture);

            /// <summary>The human's own orders are always shown; a rival's only when both its end planets are visible to the human right now.</summary>
            public static bool CanSee(GameAI.GameAIOrder order, int humanId, Func<string, bool> planetVisible)
            {
                if (order == null) return false;
                if (order.PlayerId == humanId) return true;
                return planetVisible(order.Origin) && planetVisible(order.Target);
            }

            /// <summary>
            /// Every item with a segment within `tolerance` screen pixels of the pointer, nearest first (ties keep the input order). Each
            /// item is a polyline; fewer than one point never matches and a single point is a dot.
            /// </summary>
            public static List<T> Pick<T>(IReadOnlyList<(T item, IReadOnlyList<Vector2> points)> lines, Vector2 pointer, float tolerance)
            {
                var hits = new List<(T item, float distance)>();
                foreach (var (item, points) in lines)
                {
                    if (points == null || points.Count == 0) continue;
                    var best = Vector2.Distance(pointer, points[0]);
                    for (var i = 1; i < points.Count; i++)
                        best = Mathf.Min(best, DistanceToSegment(pointer, points[i - 1], points[i]));
                    if (best <= tolerance) hits.Add((item, best));
                }
                return hits.OrderBy(h => h.distance).Select(h => h.item).ToList();
            }

            private static float DistanceToSegment(Vector2 p, Vector2 a, Vector2 b)
            {
                var ab = b - a;
                var lengthSquared = ab.sqrMagnitude;
                if (lengthSquared <= 0f) return Vector2.Distance(p, a);
                var t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / lengthSquared);
                return Vector2.Distance(p, a + ab * t);
            }

            /// <summary>One sentence for a refusal reason of the closed set a command or Check can give; an unknown reason is returned as it is.</summary>
            public static string RefusalSentence(string reason)
            {
                switch (reason)
                {
                    case "OutOfRange": return "That planet is out of range for these ships.";
                    case "TargetUnknown": return "You have not discovered that planet yet.";
                    case "UnknownPlanet": return "There is no such planet.";
                    case "SameDestination": return "The fleet is already there.";
                    case "NotYourPlanet": return "You have nothing at that planet.";
                    case "NotYourShips": return "Those ships are not yours.";
                    case "NotEnoughShips": return "Those ships are already ordered elsewhere.";
                    case "BadShipIndex": return "That ship is no longer there.";
                    case "UnknownKind": return "Those ships cannot be moved.";
                    case "UnknownFleet": return "That fleet no longer exists.";
                    case "SameFleet": return "That is the same fleet.";
                    default: return reason;
                }
            }
        }
    }
}
