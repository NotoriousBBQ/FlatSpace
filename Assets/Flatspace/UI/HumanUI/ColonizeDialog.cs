using System.Linq;
using FlatSpace.Game;
using FlatSpace.Human;
using UnityEngine;
using UnityEngine.UIElements;

namespace Game.UI.MainGameScreenUI
{
    /// <summary>
    /// Colonize, opened from a planet's action strip: the known planets this planet's colony ship can reach, with the turns the colonist
    /// takes. Pick one and commit; the food rider (if the target needs one) goes without being mentioned.
    /// </summary>
    public static class ColonizeDialog
    {
        public static void Show(string originName)
        {
            var board = Gameboard.Instance;
            var host = board != null ? board.PlanetDetail : null;
            var origin = board != null ? board.GetPlanet(originName) : null;
            if (host == null || origin == null || board.HumanPlayerId < 0 || board.HumanPlayerId >= board.players.Count) return;
            var human = board.players[board.HumanPlayerId].playerAI;
            if (human == null) return;

            var dialog = HumanUiKit.Panel(560f);
            dialog.Add(HumanUiKit.Header($"Colonize from {originName}"));
            var list = new ScrollView(ScrollViewMode.Vertical) { pickingMode = PickingMode.Position };
            list.horizontalScrollerVisibility = ScrollerVisibility.Hidden;
            list.style.maxHeight = 280;
            dialog.Add(list);
            var status = HumanUiKit.Text("", HumanUiKit.Bad);
            dialog.Add(status);

            string selected = null;
            var rows = new System.Collections.Generic.List<(string name, VisualElement line)>();

            void Highlight()
            {
                foreach (var (name, line) in rows)
                    line.style.backgroundColor = new StyleColor(name == selected ? new Color(0.35f, 0.1f, 0.1f) : Color.clear);
            }

            var options = HumanPlanetView.ColonizeOptions(board.GameAI.GameAIMap, human, origin, board.TurnNumber);
            if (options.Count == 0)
                list.Add(HumanUiKit.Text("No known planet in range can be colonized from here.", HumanUiKit.Muted));
            foreach (var option in options)
            {
                var name = option.Target.PlanetName;
                var line = HumanUiKit.Row();
                line.pickingMode = PickingMode.Position;
                line.Add(HumanUiKit.Cell(name, 200));
                line.Add(HumanUiKit.Cell(option.Target.Type.ToString().Replace("PlanetType", ""), 100));
                line.Add(HumanUiKit.Cell($"{option.Turns} turn{(option.Turns == 1 ? "" : "s")}", 80));
                line.RegisterCallback<ClickEvent>(_ => { selected = name; Highlight(); });
                list.Add(line);
                rows.Add((name, line));
            }

            var buttons = HumanUiKit.Row();
            buttons.Add(HumanUiKit.Btn("Commit", () =>
            {
                if (selected == null) { status.text = "Pick a planet first."; return; }
                var results = board.SubmitHumanCommands(new[]
                {
                    new HumanCommand { type = "colonize", origin = originName, target = selected },
                }, "Colonize");
                if (results.All(r => r.applied)) host.CloseDialog(dialog);
                else status.text = HumanUiKit.RefusalSummary(results);
            }));
            buttons.Add(HumanUiKit.Btn("Cancel", () => host.CloseDialog(dialog)));
            dialog.Add(buttons);
            host.ShowDialog(dialog);
        }
    }
}
