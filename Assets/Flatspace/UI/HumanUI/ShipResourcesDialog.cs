using System.Collections.Generic;
using System.Linq;
using FlatSpace.Game;
using FlatSpace.Human;
using UnityEngine;
using UnityEngine.UIElements;

namespace Game.UI.MainGameScreenUI
{
    /// <summary>
    /// The Ship resources dialog, opened from the destination planet's panel: pick food or grotsits, tick the human's planets in range
    /// that have a surplus, set an amount for each and commit. One shipResource command per ticked planet, sent as one batch.
    /// </summary>
    public static class ShipResourcesDialog
    {
        private class Row
        {
            public ShipmentSource Source;
            public Toggle Tick;
            public IntegerField Amount;
            public Label Note;
        }

        public static void Show(string destination)
        {
            var board = Gameboard.Instance;
            var host = board != null ? board.PlanetDetail : null;
            var destinationPlanet = board != null ? board.GetPlanet(destination) : null;
            if (host == null || destinationPlanet == null || board.HumanPlayerId < 0) return;

            // Opens on Food, unless grotsits is the only one losing stock here (the same net rates the dialog and the panel show).
            var opening = HumanPlanetView.NetPerTurn(destinationPlanet);
            var food = !(opening.grotsits < 0f && opening.food >= 0f);
            var dialog = HumanUiKit.Panel(780f);
            dialog.Add(HumanUiKit.Header($"Ship to {destination}"));

            var destinationLine = HumanUiKit.Text("");
            dialog.Add(destinationLine);

            var resourceRow = HumanUiKit.Row();
            var foodButton = HumanUiKit.Btn("Food", null);
            var grotsitsButton = HumanUiKit.Btn("Grotsits", null);
            resourceRow.Add(foodButton);
            resourceRow.Add(grotsitsButton);
            dialog.Add(resourceRow);

            var list = new ScrollView(ScrollViewMode.Vertical) { pickingMode = PickingMode.Position };
            list.horizontalScrollerVisibility = ScrollerVisibility.Hidden;
            list.style.maxHeight = 240;
            dialog.Add(list);

            var total = HumanUiKit.Text("");
            var status = HumanUiKit.Text("", HumanUiKit.Bad);
            dialog.Add(total);
            dialog.Add(status);

            var rows = new List<Row>();
            Button commit = null;

            void UpdateTotal()
            {
                var sum = rows.Where(r => r.Tick.value).Sum(r => Mathf.Max(0, r.Amount.value));
                total.text = $"Total shipping: {sum}";
                var overStock = false;
                foreach (var r in rows)
                {
                    var amount = r.Amount.value;
                    if (amount > r.Source.Stock) { r.Note.text = "more than the stock"; r.Note.style.color = new StyleColor(HumanUiKit.Bad); overStock |= r.Tick.value; }
                    else if (amount > r.Source.Surplus) { r.Note.text = "above the surplus"; r.Note.style.color = new StyleColor(HumanUiKit.Warn); }
                    else r.Note.text = "";
                }
                commit?.SetEnabled(!overStock);
            }

            void Rebuild()
            {
                list.Clear();
                rows.Clear();
                status.text = "";
                var netRates = HumanPlanetView.NetPerTurn(destinationPlanet);
                var net = food ? netRates.food : netRates.grotsits;
                destinationLine.text = food
                    ? $"Food here: {destinationPlanet.Food:0.#} ({net:+0.#;-0.#;0} per turn)"
                    : $"Grotsits here: {destinationPlanet.Grotsits:0.#} ({net:+0.#;-0.#;0} per turn)";
                foodButton.SetEnabled(!food);
                grotsitsButton.SetEnabled(food);

                var sources = board.GetShipmentSources(food, destination);
                if (sources.Count == 0)
                    list.Add(HumanUiKit.Text("None of your planets in range has a surplus.", HumanUiKit.Muted));
                foreach (var source in sources)
                {
                    var line = HumanUiKit.Row();
                    var row = new Row { Source = source };
                    row.Tick = new Toggle();
                    row.Amount = new IntegerField { value = Mathf.FloorToInt(source.Surplus) };
                    row.Amount.style.width = 60;
                    HumanUiKit.StyleField(row.Amount);
                    row.Note = HumanUiKit.Text("");
                    row.Tick.RegisterValueChangedCallback(_ => UpdateTotal());
                    row.Amount.RegisterValueChangedCallback(_ => UpdateTotal());
                    line.Add(row.Tick);
                    line.Add(HumanUiKit.Cell(source.Planet, 110));
                    line.Add(HumanUiKit.Cell($"surplus {source.Surplus:0.#}", 100));
                    line.Add(HumanUiKit.Cell($"stock {source.Stock:0.#}", 90));
                    line.Add(HumanUiKit.Cell($"{source.Hops} hop{(source.Hops == 1 ? "" : "s")}", 60));
                    line.Add(row.Amount);
                    line.Add(row.Note);
                    list.Add(line);
                    rows.Add(row);
                }
                UpdateTotal();
            }

            foodButton.clicked += () => { food = true; Rebuild(); };
            grotsitsButton.clicked += () => { food = false; Rebuild(); };

            var buttons = HumanUiKit.Row();
            commit = HumanUiKit.Btn("Commit", () =>
            {
                var commands = rows.Where(r => r.Tick.value && r.Amount.value > 0).Select(r => new HumanCommand
                {
                    type = "shipResource", origin = r.Source.Planet, target = destination, kind = food ? "food" : "grotsits", count = r.Amount.value,
                }).ToList();
                if (commands.Count == 0) { status.text = "Tick a planet and set an amount."; return; }
                // All or nothing: if any row would be refused, none is sent and the status says which.
                var pre = board.CheckHumanBatch(commands);
                if (pre.Exists(r => !r.applied)) { status.text = "Nothing sent. " + HumanUiKit.RefusalSummary(pre); return; }
                var results = board.SubmitHumanCommands(commands, "ShipResources");
                if (results.All(r => r.applied)) host.CloseDialog(dialog);
                else { status.text = HumanUiKit.RefusalSummary(results); Rebuild(); }
            });
            buttons.Add(commit);
            buttons.Add(HumanUiKit.Btn("Cancel", () => host.CloseDialog(dialog)));
            dialog.Add(buttons);

            Rebuild();
            host.ShowDialog(dialog);
        }

    }
}
