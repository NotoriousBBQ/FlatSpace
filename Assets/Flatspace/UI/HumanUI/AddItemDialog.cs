using System.Linq;
using FlatSpace.Game;
using FlatSpace.Human;
using UnityEngine;
using UnityEngine.UIElements;

namespace Game.UI.MainGameScreenUI
{
    /// <summary>
    /// The + Add item list of a planet's production section: what can be built now, grouped, with cost and an estimate of turns. A click
    /// queues the item (queueProduction) and the list stays open so several can be added; it rebuilds after each add so items that
    /// stop being offered (a tier now built) disappear.
    /// </summary>
    public static class AddItemDialog
    {
        public static void Show(string planetName)
        {
            var board = Gameboard.Instance;
            var host = board != null ? board.PlanetDetail : null;
            var planet = board != null ? board.GetPlanet(planetName) : null;
            if (host == null || planet == null || board.HumanPlayerId < 0 || board.HumanPlayerId >= board.players.Count) return;
            var human = board.players[board.HumanPlayerId].playerAI;
            if (human == null) return;

            var dialog = HumanUiKit.Panel(720f);
            dialog.Add(HumanUiKit.Header($"Add to {planetName}"));
            var list = new ScrollView(ScrollViewMode.Vertical) { pickingMode = PickingMode.Position };
            list.horizontalScrollerVisibility = ScrollerVisibility.Hidden;
            list.style.maxHeight = 320;
            dialog.Add(list);
            var status = HumanUiKit.Text("", HumanUiKit.Bad);
            dialog.Add(status);

            void Rebuild()
            {
                list.Clear();
                var options = HumanBuildList.Options(planet, human);
                if (options.Count == 0) list.Add(HumanUiKit.Text("Nothing can be built here right now.", HumanUiKit.Muted));
                string group = null;
                foreach (var option in options)
                {
                    if (option.Group != group)
                    {
                        group = option.Group;
                        list.Add(HumanUiKit.Text(group, HumanUiKit.Muted, bold: true));
                    }
                    var line = HumanUiKit.Row();
                    var itemName = option.Item.itemName;
                    line.Add(HumanUiKit.Cell(itemName, 240));
                    line.Add(HumanUiKit.Cell($"cost {option.Cost:0.#}", 90));
                    line.Add(HumanUiKit.Cell(option.Turns.HasValue ? $"{option.Turns} turns" : "no industry", 90));
                    line.Add(HumanUiKit.Btn("Add", () =>
                    {
                        var results = board.SubmitHumanCommands(new[]
                        {
                            new HumanCommand { type = "queueProduction", origin = planetName, item = itemName },
                        }, "Production");
                        status.text = results.All(r => r.applied) ? "" : HumanUiKit.RefusalSummary(results);
                        Rebuild();
                    }));
                    list.Add(line);
                }
            }

            dialog.Add(HumanUiKit.Row());
            var close = HumanUiKit.Btn("Close", () => host.CloseDialog(dialog));
            dialog.Add(close);
            Rebuild();
            host.ShowDialog(dialog);
        }
    }
}
