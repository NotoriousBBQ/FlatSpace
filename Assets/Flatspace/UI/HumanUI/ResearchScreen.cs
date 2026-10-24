using System.Collections.Generic;
using System.Linq;
using FlatSpace.AI;
using FlatSpace.Game;
using FlatSpace.Human;
using UnityEngine;
using UnityEngine.UIElements;

namespace Game.UI.MainGameScreenUI
{
    /// <summary>
    /// The research screen: the whole research catalog as a tree (researched items checked off, items whose prerequisite is not done greyed
    /// out), the research in progress and the queue with turn estimates. Click an available item to queue it, a greyed item to queue the
    /// chain of prerequisites that leads to it (after a confirmation) and a queued item to remove it. Opened from the Research button.
    /// </summary>
    public static class ResearchScreen
    {
        private static VisualElement _screen;
        private static VisualElement _tree;
        private static VisualElement _queue;
        private static Label _header;
        private static Label _status;
        private static VisualElement _confirm;
        private static List<string> _pendingChain = new List<string>();
        private static string _pendingTarget;

        public static bool IsOpen => _screen != null && _screen.parent != null;

        public static void Toggle(VisualElement host)
        {
            if (IsOpen) { Close(); return; }
            if (host == null || Gameboard.Instance == null || Gameboard.Instance.HumanPlayerId < 0) return;
            Build(host);
            Refresh();
        }

        public static void Close()
        {
            _screen?.RemoveFromHierarchy();
            _screen = null;
        }

        private static void Build(VisualElement host)
        {
            _screen = HumanUiKit.Panel(900f);
            _screen.style.position = Position.Absolute;
            _screen.style.left = Length.Percent(10);
            _screen.style.top = Length.Percent(8);

            var top = HumanUiKit.Row();
            _header = HumanUiKit.Header("Research");
            _header.style.flexGrow = 1;
            top.Add(_header);
            top.Add(HumanUiKit.Btn("Close", Close));
            _screen.Add(top);

            // The confirmation and the status line sit under the header: at the bottom they could fall off the screen.
            _status = HumanUiKit.Text("", HumanUiKit.Bad);
            _screen.Add(_status);
            _confirm = HumanUiKit.Row();
            _confirm.style.display = DisplayStyle.None;
            _screen.Add(_confirm);

            var body = HumanUiKit.Row();
            body.style.alignItems = Align.FlexStart;
            var treeScroll = new ScrollView { pickingMode = PickingMode.Position };
            treeScroll.style.width = 560;
            treeScroll.style.height = 520;
            _tree = treeScroll;
            body.Add(treeScroll);

            var right = new VisualElement { pickingMode = PickingMode.Ignore };
            right.style.width = 300;
            right.style.marginLeft = 10;
            _queue = right;
            body.Add(right);
            _screen.Add(body);
            host.Add(_screen);
        }

        public static void Refresh()
        {
            if (!IsOpen) return;
            var board = Gameboard.Instance;
            var human = board != null && board.HumanPlayerId >= 0 && board.HumanPlayerId < board.players.Count ? board.players[board.HumanPlayerId].playerAI : null;
            if (human == null || human.ResearchCatalog == null) { Close(); return; }

            var catalog = human.ResearchCatalog.catalogItems;
            var queueNames = human.ResearchQueue.ToList();
            var perTurn = HumanResearchTree.ResearchPerTurn(board.GameAI.GameAIMap, board.HumanPlayerId);
            _header.text = $"Research   +{perTurn:0.#} per turn";

            // ── The tree ──
            _tree.Clear();
            var roots = HumanResearchTree.Build(catalog, queueNames, human.currentResearch);
            foreach (var node in HumanResearchTree.Flatten(roots))
                _tree.Add(TreeRow(node, human, catalog, queueNames));

            // ── The queue ──
            _queue.Clear();
            var queuedItems = queueNames.Select(n => human.ResearchCatalog.GetItem(n)).Where(i => i != null).ToList();
            var estimates = HumanResearchTree.Estimates(human.currentResearch, human.researchTotal, queuedItems, perTurn);
            var estimateIndex = 0;
            _queue.Add(HumanUiKit.Text("In progress", HumanUiKit.Muted, bold: true));
            if (human.currentResearch != null)
            {
                _queue.Add(HumanUiKit.Text($"{human.currentResearch.itemName}  {human.researchTotal:0.#}/{human.currentResearch.cost:0.#}{Turns(estimates, estimateIndex)}"));
                estimateIndex++;
            }
            else
                _queue.Add(HumanUiKit.Text("Nothing: queue something to use your research", HumanUiKit.Warn));

            _queue.Add(HumanUiKit.Text("Queue", HumanUiKit.Muted, bold: true));
            if (queuedItems.Count == 0) _queue.Add(HumanUiKit.Text("empty", HumanUiKit.Muted));
            for (var i = 0; i < queuedItems.Count; i++)
            {
                var index = i;
                var line = HumanUiKit.Row();
                line.Add(HumanUiKit.Cell($"{i + 1}. {queuedItems[i].itemName}{Turns(estimates, estimateIndex + i)}", 230));
                line.Add(HumanUiKit.Btn("x", () => Send(new HumanCommand { type = "removeResearch", index = index })));
                _queue.Add(line);
            }
        }

        private static VisualElement TreeRow(ResearchNode node, PlayerAI human, List<Flatspace.Objects.Production.CatalogItem> catalog, List<string> queueNames)
        {
            var item = node.Item;
            string prefix;
            Color color;
            switch (node.State)
            {
                case ResearchNodeState.Researched: prefix = "[x] "; color = HumanUiKit.Good; break;
                case ResearchNodeState.Current: prefix = "[>] "; color = Color.white; break;
                case ResearchNodeState.Queued: prefix = $"[{node.QueuePosition}] "; color = HumanUiKit.Warn; break;
                case ResearchNodeState.Available: prefix = "[ ] "; color = Color.white; break;
                default: prefix = "[ ] "; color = HumanUiKit.Muted; break;
            }
            var label = HumanUiKit.Text($"{prefix}{item.itemName}  ({item.cost:0.#})", color, bold: node.State == ResearchNodeState.Current);
            label.pickingMode = PickingMode.Position;
            label.style.marginLeft = node.Depth * 16;
            label.RegisterCallback<ClickEvent>(_ => OnNodeClicked(node, human, catalog, queueNames));
            return label;
        }

        private static void OnNodeClicked(ResearchNode node, PlayerAI human, List<Flatspace.Objects.Production.CatalogItem> catalog, List<string> queueNames)
        {
            _status.text = "";
            HideConfirm();
            switch (node.State)
            {
                case ResearchNodeState.Available:
                    Send(new HumanCommand { type = "queueResearch", item = node.Item.itemName });
                    break;
                case ResearchNodeState.Queued:
                    Send(new HumanCommand { type = "removeResearch", index = node.QueuePosition - 1 });
                    break;
                case ResearchNodeState.Locked:
                {
                    var chain = HumanResearchTree.ChainFor(catalog, queueNames, human.currentResearch, node.Item.itemName);
                    if (chain.Count == 0) { _status.text = $"{node.Item.itemName} cannot be researched."; return; }
                    ShowConfirm(chain, node.Item.itemName);
                    break;
                }
            }
        }

        private static void ShowConfirm(List<string> chain, string target)
        {
            _pendingChain = chain;
            _pendingTarget = target;
            _confirm.Clear();
            _confirm.style.display = DisplayStyle.Flex;
            _confirm.Add(HumanUiKit.Text($"Queue {chain.Count} item{(chain.Count == 1 ? "" : "s")} ({string.Join(" > ", chain)})?"));
            _confirm.Add(HumanUiKit.Btn("Yes", () =>
            {
                // Turns may have passed since the question was asked: work the chain out again from what is researched and queued now.
                var board = Gameboard.Instance;
                var ai = board != null && board.HumanPlayerId >= 0 ? board.players[board.HumanPlayerId].playerAI : null;
                var target = _pendingTarget;
                HideConfirm();
                if (ai == null) return;
                var fresh = HumanResearchTree.ChainFor(ai.ResearchCatalog.catalogItems, ai.ResearchQueue, ai.currentResearch, target);
                if (fresh.Count == 0) { _status.text = "Nothing left to queue."; Refresh(); return; }
                Send(fresh.Select(n => new HumanCommand { type = "queueResearch", item = n }).ToArray());
            }));
            _confirm.Add(HumanUiKit.Btn("No", HideConfirm));
        }

        private static void HideConfirm()
        {
            _pendingChain = new List<string>();
            if (_confirm == null) return;
            _confirm.Clear();
            _confirm.style.display = DisplayStyle.None;
        }

        private static void Send(params HumanCommand[] commands)
        {
            var results = Gameboard.Instance.SubmitHumanCommands(commands, "Research");
            _status.text = HumanUiKit.RefusalSummary(results);
            Refresh();
        }

        private static string Turns(List<int?> estimates, int index)
            => index < estimates.Count && estimates[index].HasValue ? $"  ~{estimates[index]} turns" : "";
    }
}
