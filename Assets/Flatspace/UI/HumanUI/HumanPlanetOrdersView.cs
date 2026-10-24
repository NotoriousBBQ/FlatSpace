using System.Linq;
using FlatSpace.Game;
using FlatSpace.Human;
using UnityEngine;
using UnityEngine.UIElements;

namespace Game.UI.MainGameScreenUI
{
    /// <summary>
    /// The order-giving part of the planet panel, shown only while a human plays and only on a planet they can order on: the action strip
    /// (Ship resources, Colonize, Fleet, the development switch) and a scrolling body with stock and rates, the production queue,
    /// and the collapsed Improvements built and Ships docked sections. Rebuilt from the planet on every refresh; the open state of the
    /// two foldouts is remembered here so a refresh does not close them.
    /// </summary>
    public class HumanPlanetOrdersView
    {
        private readonly VisualElement _container;
        private readonly VisualElement _strip;
        private readonly ScrollView _scroll;
        private Planet _boundPlanet;
        private bool _improvementsOpen;
        private bool _shipsOpen;
        private string _status = "";

        public HumanPlanetOrdersView(VisualElement panel)
        {
            _container = new VisualElement { name = "HumanOrders", pickingMode = PickingMode.Ignore };
            _container.style.flexDirection = FlexDirection.Column;
            _container.style.display = DisplayStyle.None;
            // Built once and only emptied on a refresh, so the scroll position survives the rebuild every turn brings.
            _strip = HumanUiKit.Row();
            _container.Add(_strip);
            _scroll = new ScrollView { pickingMode = PickingMode.Position };
            _scroll.style.maxHeight = 240;
            _container.Add(_scroll);
            panel.Add(_container);
        }

        public void Bind(Planet planet)
        {
            var board = Gameboard.Instance;
            var humanId = board != null ? board.HumanPlayerId : -1;
            _strip.Clear();
            _scroll.contentContainer.Clear();
            if (planet == null || humanId < 0 || !HumanPlanetView.CanOrder(planet, humanId))
            {
                _container.style.display = DisplayStyle.None;
                return;
            }
            _container.style.display = DisplayStyle.Flex;
            if (_boundPlanet != planet)
            {
                _boundPlanet = planet;
                _status = "";   // a refusal belongs to the planet it happened on
                _scroll.scrollOffset = Vector2.zero;
            }
            var name = planet.PlanetName;
            var owned = HumanPlanetView.OwnsPlanet(planet, humanId);

            // ── Action strip ──
            var strip = _strip;
            strip.Add(HumanUiKit.Btn("Ship resources", () => ShipResourcesDialog.Show(name)));
            var colonize = HumanUiKit.Btn("Colonize", () => ColonizeDialog.Show(name));
            colonize.SetEnabled(HumanPlanetView.CanColonizeFrom(planet, humanId));
            strip.Add(colonize);
            var hasShips = planet.DockedShips.Exists(s => s.Owner == humanId && Ship.CountsAsShip(s.Kind));
            var fleet = HumanUiKit.Btn("Fleet", () => board.ShowFleetUI(name, humanId));
            fleet.SetEnabled(hasShips);
            strip.Add(fleet);
            // Production and development change only a planet the human owns; on a planet they merely have people on, the panel
            // shows rates and ships and nothing the command layer would refuse.
            if (owned)
                strip.Add(HumanUiKit.Btn(planet.ManualDevelopment ? "Development: Manual" : "Development: Automatic", () =>
                {
                    // flag true = automatic, so a manual planet is switched to automatic and the other way round
                    Send("Production", new HumanCommand { type = "setDevelopment", origin = name, flag = planet.ManualDevelopment });
                }));

            // ── Scrolling body ──
            var scroll = _scroll.contentContainer;

            var net = HumanPlanetView.NetPerTurn(planet);
            // Red when the rate is negative; the stock itself only when it cannot cover that loss (see RateLine).
            scroll.Add(RateLine("Food", planet.Food, net.food));
            scroll.Add(RateLine("Grotsits", planet.Grotsits, net.grotsits));
            scroll.Add(HumanUiKit.Text($"Industry +{planet.IndustryProduced:0.#}   Research +{planet.ResearchProduced:0.#}"));

            if (owned)
            {
            scroll.Add(HumanUiKit.Text("Production", HumanUiKit.Muted, bold: true));
            var estimates = HumanBuildList.QueueEstimates(planet);
            var estimateIndex = 0;
            if (planet.CurrentProduction.HasValue)
            {
                var current = planet.CurrentProduction.Value;
                scroll.Add(HumanUiKit.Text($"Building: {current.Item.itemName}  {current.Progress:0.#}/{current.Cost:0.#}{TurnsText(estimates, estimateIndex)}"));
                estimateIndex++;
            }
            else
                scroll.Add(HumanUiKit.Text("Nothing in production", HumanUiKit.Muted));

            for (var i = 0; i < planet.ProductionQueue.Count; i++)
            {
                var index = i;
                var queued = planet.ProductionQueue[i];
                var line = HumanUiKit.Row();
                line.Add(HumanUiKit.Cell($"{i + 1}. {queued.Item.itemName}{TurnsText(estimates, estimateIndex + i)}", 250));
                var up = HumanUiKit.Btn("Up", () => Send("Production", new HumanCommand { type = "moveProduction", origin = name, index = index, toIndex = index - 1 }));
                up.SetEnabled(index > 0 || planet.CurrentProduction.HasValue);   // the first item moved up replaces the current production
                var down = HumanUiKit.Btn("Down", () => Send("Production", new HumanCommand { type = "moveProduction", origin = name, index = index, toIndex = index + 1 }));
                down.SetEnabled(index < planet.ProductionQueue.Count - 1);
                line.Add(up);
                line.Add(down);
                line.Add(HumanUiKit.Btn("Remove", () => Send("Production", new HumanCommand { type = "removeProduction", origin = name, index = index })));
                scroll.Add(line);
            }
            scroll.Add(HumanUiKit.Btn("+ Add item", () => AddItemDialog.Show(name)));
            }

            // ── Collapsed sections ──
            var improvements = new Foldout { text = $"Improvements built ({planet.CompletedImprovements.Count})", value = _improvementsOpen };
            foreach (var built in planet.CompletedImprovements)
                improvements.Add(HumanUiKit.Text(built.Item1));
            improvements.RegisterValueChangedCallback(e => { if (e.target == improvements) _improvementsOpen = e.newValue; });
            HumanUiKit.StyleFoldout(improvements);
            scroll.Add(improvements);

            var docked = HumanFleetView.Docked(planet, humanId);
            var ships = new Foldout { text = $"Ships docked ({docked.Count})", value = _shipsOpen };
            foreach (var group in docked.GroupBy(s => s.Kind))
            {
                var damaged = group.Count(s => s.Damaged);
                ships.Add(HumanUiKit.Text($"{group.Count()} {group.Key}" + (damaged > 0 ? $" ({damaged} damaged)" : ""),
                    damaged > 0 ? HumanUiKit.Warn : (Color?)null));
            }
            ships.RegisterValueChangedCallback(e => { if (e.target == ships) _shipsOpen = e.newValue; });
            HumanUiKit.StyleFoldout(ships);
            scroll.Add(ships);

            if (!string.IsNullOrEmpty(_status))
                scroll.Add(HumanUiKit.Text(_status, HumanUiKit.Bad));
        }

        private void Send(string screen, HumanCommand command)
        {
            _status = "";
            var results = Gameboard.Instance.SubmitHumanCommands(new[] { command }, screen);   // refreshes the panel when it is applied
            var summary = HumanUiKit.RefusalSummary(results);
            if (summary.Length == 0) return;
            _status = summary;
            Gameboard.Instance.PlanetDetail?.UpdatePlanetDetail();   // show the refusal
        }

        // The overall loss stays red, but the stock itself only reads red when it cannot cover that loss: a shipment that just landed on a
        // planet that reported a shortage no longer paints the whole line.
        private static VisualElement RateLine(string label, float stock, float perTurn)
        {
            var covered = HumanPlanetView.StockCovers(stock, perTurn);
            var line = HumanUiKit.Row();
            line.Add(HumanUiKit.Text($"{label} {stock:0.#}", covered ? (Color?)null : HumanUiKit.Bad));
            line.Add(HumanUiKit.Text($"  ({perTurn:+0.#;-0.#;0} per turn)", perTurn < 0f ? HumanUiKit.Bad : (Color?)null));
            return line;
        }

        private static string TurnsText(System.Collections.Generic.List<int?> estimates, int index)
            => index < estimates.Count && estimates[index].HasValue ? $"  ~{estimates[index]} turns" : "";
    }
}
