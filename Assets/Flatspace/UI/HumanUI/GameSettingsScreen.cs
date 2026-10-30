using FlatSpace.Game;
using FlatSpace.Human;
using UnityEngine;
using UnityEngine.UIElements;

namespace Game.UI.MainGameScreenUI
{
    /// <summary>
    /// The game settings: one row per setting, a checkbox each. For now only "Automate resource shipments" (the AI ships food and grotsits and
    /// runs the distribution centers for the human; off, the human ships by hand). The change goes through Gameboard.SubmitHumanCommands as a
    /// setShipments command. Opened from the Settings button.
    /// </summary>
    public static class GameSettingsScreen
    {
        private static VisualElement _screen;
        private static Toggle _shipments;

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
            _shipments = null;
        }

        /// <summary>Whether a screen point is on the screen: a press there is not a click outside the planet or fleet panel.</summary>
        public static bool ContainsScreenPoint(Vector2 screenPosition)
        {
            if (!IsOpen || _screen.panel == null) return false;
            return _screen.worldBound.Contains(RuntimePanelUtils.ScreenToPanel(_screen.panel, screenPosition));
        }

        private static void Build(VisualElement host)
        {
            _screen = HumanUiKit.Panel(360f);
            _screen.style.position = Position.Absolute;
            _screen.style.left = Length.Percent(1);
            _screen.style.top = Length.Percent(12);

            var top = HumanUiKit.Row();
            var header = HumanUiKit.Header("Game Settings");
            header.style.flexGrow = 1;
            top.Add(header);
            top.Add(HumanUiKit.Btn("Close", Close));
            _screen.Add(top);

            _shipments = new Toggle("Automate resource shipments");
            _shipments.style.color = new StyleColor(Color.white);
            _shipments.style.marginTop = 6;
            _shipments.RegisterValueChangedCallback(evt =>
            {
                var board = Gameboard.Instance;
                if (board == null) return;
                board.SubmitHumanCommands(new[] { new HumanCommand { type = "setShipments", flag = evt.newValue } }, "GameSettings");
            });
            _screen.Add(_shipments);
            _screen.Add(HumanUiKit.Text("On: the AI ships food and grotsits for you. Off: you ship by hand.", HumanUiKit.Muted, size: 12));
            host.Add(_screen);
        }

        /// <summary>Shows the current setting; a no-op while the screen is closed.</summary>
        public static void Refresh()
        {
            if (!IsOpen || _shipments == null || Gameboard.Instance == null) return;
            var board = Gameboard.Instance;
            if (board.HumanPlayerId < 0 || board.HumanPlayerId >= board.players.Count)
            {
                Close();
                return;
            }
            var automated = board.players[board.HumanPlayerId].playerAI.ShipmentsAutomated;
            _shipments.SetValueWithoutNotify(automated);   // no command: this only mirrors the saved setting
        }
    }
}
