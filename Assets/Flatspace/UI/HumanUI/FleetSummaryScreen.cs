using FlatSpace.Game;
using FlatSpace.Human;
using UnityEngine;
using UnityEngine.UIElements;

namespace Game.UI.MainGameScreenUI
{
    /// <summary>
    /// The fleet summary: every fleet of the human, docked and in flight, one row each (where, ships, turns left in flight). A click on a row
    /// centres the board on that planet (the destination for a fleet in flight), opens nothing and closes the screen. Opened from the Fleets button; the rows come from the pure
    /// HumanFleetList, so this is only layout.
    /// </summary>
    public static class FleetSummaryScreen
    {
        private static VisualElement _screen;
        private static VisualElement _rows;

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
            _rows = null;
        }

        /// <summary>Whether a screen point is on the summary: a press there is not a click outside the planet or fleet panel.</summary>
        public static bool ContainsScreenPoint(Vector2 screenPosition)
        {
            if (!IsOpen || _screen.panel == null) return false;
            return _screen.worldBound.Contains(RuntimePanelUtils.ScreenToPanel(_screen.panel, screenPosition));
        }

        private static void Build(VisualElement host)
        {
            _screen = HumanUiKit.Panel(520f);
            _screen.style.position = Position.Absolute;
            _screen.style.left = Length.Percent(1);   // clear of the fleet panel, which sits at the centre
            _screen.style.top = Length.Percent(12);

            var top = HumanUiKit.Row();
            var header = HumanUiKit.Header("Fleets");
            header.style.flexGrow = 1;
            top.Add(header);
            top.Add(HumanUiKit.Btn("Close", Close));
            _screen.Add(top);

            var scroll = new ScrollView { pickingMode = PickingMode.Position };
            scroll.style.height = 420;
            _rows = scroll;
            _screen.Add(scroll);
            host.Add(_screen);
        }

        /// <summary>Rebuilds the rows from the current board; a no-op while the screen is closed.</summary>
        public static void Refresh()
        {
            if (!IsOpen || _rows == null || Gameboard.Instance == null) return;
            _rows.Clear();
            var board = Gameboard.Instance;
            if (board.HumanPlayerId < 0)
            {
                Close();
                return;
            }

            var rows = HumanFleetList.Build(board.GameAI.GameAIMap.PlanetList, board.GameAI.CurrentAIOrders, board.HumanPlayerId);
            if (rows.Count == 0)
            {
                _rows.Add(HumanUiKit.Text("You have no fleets.", HumanUiKit.Muted));
                return;
            }
            foreach (var row in rows)
            {
                var line = HumanUiKit.Row();
                line.Add(HumanUiKit.Cell(row.Where, 170f, row.InFlight ? HumanUiKit.Warn : (Color?)null));
                line.Add(HumanUiKit.Cell(row.Detail, 220f));
                line.Add(HumanUiKit.Cell(row.InFlight ? $"{row.TurnsLeft} {(row.TurnsLeft == 1 ? "turn" : "turns")}" : "docked", 80f, HumanUiKit.Muted));
                var planet = row.PlanetToShow;
                line.pickingMode = PickingMode.Position;
                line.RegisterCallback<ClickEvent>(_ =>
                {
                    Gameboard.Instance.GoToFleet(planet);
                    Close();   // choosing a row is the screen's whole job: it goes away and the board shows the fleet
                });
                _rows.Add(line);
            }
        }
    }
}
