using System;
using System.Collections.Generic;
using System.Linq;
using Flatspace.Objects.Production;
using FlatSpace.AI;

namespace FlatSpace
{
    namespace Human
    {
        public enum ResearchNodeState { Researched, Available, Locked, Queued, Current }

        /// <summary>One item of the research tree: where it hangs, and what the human can do with it.</summary>
        public class ResearchNode
        {
            public CatalogItem Item;
            public ResearchNode Parent;
            public List<ResearchNode> Children = new List<ResearchNode>();
            public ResearchNodeState State;
            public int QueuePosition;   // 1-based, only for Queued
            public int Depth;
        }

        /// <summary>
        /// The research catalog as a tree by prerequisite (<c>requiredTech</c>), the chain of items that must be queued to reach a locked one,
        /// and the turn estimates of the queue. Every item has at most one prerequisite. An item whose prerequisite is not in the catalog, or
        /// that sits in a cycle, is a root that is always Locked: the builders always terminate. Pure.
        /// </summary>
        public static class HumanResearchTree
        {
            /// <summary>
            /// Whether moving the queue entry at `from` to position `to` leaves every queued item after the queued item it requires (an item
            /// whose prerequisite is researched, in progress or not queued has nothing to keep ahead of it). Indices outside the queue are false.
            /// </summary>
            public static bool MoveKeepsPrerequisites(IReadOnlyList<string> queue, Func<string, string> requiredTechOf, int from, int to)
            {
                if (from < 0 || from >= queue.Count || to < 0 || to >= queue.Count) return false;
                var moved = queue.ToList();
                var entry = moved[from];
                moved.RemoveAt(from);
                moved.Insert(to, entry);
                for (var i = 0; i < moved.Count; i++)
                {
                    var required = requiredTechOf(moved[i]);
                    if (string.IsNullOrEmpty(required)) continue;
                    var at = moved.IndexOf(required);
                    if (at >= i) return false;   // -1 (not queued) is fine; its own position at or after ours is not
                }
                return true;
            }

            public static List<ResearchNode> Build(IEnumerable<CatalogItem> catalog, IEnumerable<string> queue, CatalogItem current)
            {
                var items = catalog.ToList();
                var byName = new Dictionary<string, CatalogItem>();
                foreach (var item in items)
                    if (!byName.ContainsKey(item.itemName)) byName[item.itemName] = item;
                var queued = queue.ToList();

                var nodes = new Dictionary<string, ResearchNode>();
                foreach (var item in byName.Values)
                    nodes[item.itemName] = new ResearchNode { Item = item };

                var unusable = new HashSet<string>();   // missing prerequisite or part of a cycle
                foreach (var item in byName.Values)
                {
                    if (string.IsNullOrEmpty(item.requiredTech)) continue;
                    if (!byName.ContainsKey(item.requiredTech) || InCycle(item, byName))
                    {
                        unusable.Add(item.itemName);
                        continue;
                    }
                    var parent = nodes[item.requiredTech];
                    nodes[item.itemName].Parent = parent;
                    parent.Children.Add(nodes[item.itemName]);
                }

                foreach (var node in nodes.Values)
                {
                    node.Children.Sort((x, y) => string.CompareOrdinal(x.Item.itemName, y.Item.itemName));
                    var queueIndex = queued.IndexOf(node.Item.itemName);
                    if (node.Item == current) node.State = ResearchNodeState.Current;
                    else if (node.Item.researched) node.State = ResearchNodeState.Researched;
                    else if (queueIndex >= 0) { node.State = ResearchNodeState.Queued; node.QueuePosition = queueIndex + 1; }
                    else if (unusable.Contains(node.Item.itemName)) node.State = ResearchNodeState.Locked;
                    else if (node.Parent == null || node.Parent.Item.researched) node.State = ResearchNodeState.Available;
                    else node.State = ResearchNodeState.Locked;
                }

                var roots = nodes.Values.Where(n => n.Parent == null).OrderBy(n => n.Item.itemName, StringComparer.Ordinal).ToList();
                foreach (var root in roots) SetDepth(root, 0);
                return roots;
            }

            /// <summary>The nodes in display order: each parent followed by its subtree.</summary>
            public static IEnumerable<ResearchNode> Flatten(IEnumerable<ResearchNode> roots)
            {
                foreach (var root in roots)
                {
                    yield return root;
                    foreach (var child in Flatten(root.Children)) yield return child;
                }
            }

            /// <summary>
            /// The items to queue, in order, to research <paramref name="itemName"/>: its missing prerequisites first, itself last. Stops at the
            /// first prerequisite that is researched, queued or in progress (it is already on its way). Empty for an unknown, researched,
            /// queued or cyclic item.
            /// </summary>
            public static List<string> ChainFor(IEnumerable<CatalogItem> catalog, IEnumerable<string> queue, CatalogItem current, string itemName)
            {
                var byName = new Dictionary<string, CatalogItem>();
                foreach (var item in catalog)
                    if (!byName.ContainsKey(item.itemName)) byName[item.itemName] = item;
                var queued = new HashSet<string>(queue);

                var chain = new List<string>();
                var visited = new HashSet<string>();
                CatalogItem cursor;
                byName.TryGetValue(itemName ?? "", out cursor);
                while (cursor != null)
                {
                    if (!visited.Add(cursor.itemName)) return new List<string>();   // a cycle: nothing can be queued
                    if (cursor.researched || cursor == current || queued.Contains(cursor.itemName)) break;
                    chain.Add(cursor.itemName);
                    CatalogItem next = null;
                    if (!string.IsNullOrEmpty(cursor.requiredTech)) byName.TryGetValue(cursor.requiredTech, out next);
                    cursor = next;
                }
                chain.Reverse();
                return chain;
            }

            /// <summary>
            /// Cumulative turns until the item in progress, then each queued item, is done at <paramref name="perTurn"/> research a turn:
            /// the head item (the current one, else the first queued) has the banked points taken off its cost. All null at 0 a turn.
            /// </summary>
            public static List<int?> Estimates(CatalogItem current, float researchTotal, IList<CatalogItem> queue, float perTurn)
            {
                var order = new List<CatalogItem>();
                if (current != null) order.Add(current);
                order.AddRange(queue);
                var estimates = new List<int?>();
                var elapsed = 0;
                for (var i = 0; i < order.Count; i++)
                {
                    if (perTurn <= 0f) { estimates.Add(null); continue; }
                    var cost = order[i].cost - (i == 0 ? researchTotal : 0f);
                    elapsed += (int)Math.Ceiling(Math.Max(0f, cost) / perTurn);
                    estimates.Add(elapsed);
                }
                return estimates;
            }

            /// <summary>The research the human's planets make each turn (planets they own).</summary>
            public static float ResearchPerTurn(GameAIMap map, int humanId)
                => map.PlanetList.Where(p => p.Owner == humanId).Sum(p => p.ResearchProduced);

            private static bool InCycle(CatalogItem start, Dictionary<string, CatalogItem> byName)
            {
                var seen = new HashSet<string> { start.itemName };
                var cursor = start;
                while (!string.IsNullOrEmpty(cursor.requiredTech))
                {
                    if (!byName.TryGetValue(cursor.requiredTech, out cursor)) return false;
                    if (!seen.Add(cursor.itemName)) return true;
                }
                return false;
            }

            private static void SetDepth(ResearchNode node, int depth)
            {
                node.Depth = depth;
                foreach (var child in node.Children) SetDepth(child, depth + 1);
            }
        }
    }
}
