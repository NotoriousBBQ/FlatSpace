using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using FlatSpace.Game;
using Game.UI.MainGameScreenUI;

namespace Game.UI.MainGameScreenUI
{
    public class MainScreenUIController : MonoBehaviour
    {
        public UIDocument uiDocument;

        private GameButtonHandler            _gameButtonHandler;
        private NotificationListController   _notificationListController;

        // Status bar labels
        private Label _turnLabel;
        private Label _researchLabel;
        private Label _currentResearchLabel;
        private Label _grotsitsLabel;
        private Button _researchButton;
        private Button _fleetsButton;
        private Button _planetsButton;
        private Button _pauseBanner;
        private string _pauseBannerPlanet;

        void OnEnable()
        {
            var root = uiDocument.rootVisualElement;

            // Bind status bar labels
            _turnLabel     = root.Q<Label>("TurnLabel");
            _researchLabel = root.Q<Label>("ResearchLabel");
            _currentResearchLabel = root.Q<Label>("CurrentResearchLabel");
            _grotsitsLabel = root.Q<Label>("GrotsitsLabel");

            // The human's research screen opens from here. Built in code and only shown while a human plays.
            _researchButton = new Button(() => ResearchScreen.Toggle(root)) { text = "Research" };
            _researchButton.style.position = Position.Absolute;
            _researchButton.style.top = 40;
            _researchButton.style.right = 10;
            _researchButton.style.display = DisplayStyle.None;
            HumanUiKit.StyleButton(_researchButton);
            root.Add(_researchButton);

            // The fleet summary opens from the button beside it, under the Research button.
            _fleetsButton = new Button(() => FleetSummaryScreen.Toggle(root)) { text = "Fleets" };
            _fleetsButton.style.position = Position.Absolute;
            _fleetsButton.style.top = 76;
            _fleetsButton.style.right = 10;
            _fleetsButton.style.display = DisplayStyle.None;
            HumanUiKit.StyleButton(_fleetsButton);
            root.Add(_fleetsButton);

            // The planets screen (search, filters, one row per known planet) opens from the button under Fleets, or the P key.
            _planetsButton = new Button(() => PlanetsScreen.Toggle(root)) { text = "Planets" };
            _planetsButton.style.position = Position.Absolute;
            _planetsButton.style.top = 112;
            _planetsButton.style.right = 10;
            _planetsButton.style.display = DisplayStyle.None;
            HumanUiKit.StyleButton(_planetsButton);
            root.Add(_planetsButton);

            // Why the timer stopped, shown at once and clickable to go there; hidden otherwise.
            _pauseBanner = new Button(() => { if (!string.IsNullOrEmpty(_pauseBannerPlanet)) Gameboard.Instance.GoToPlanet(_pauseBannerPlanet); });
            _pauseBanner.style.position = Position.Absolute;
            _pauseBanner.style.top = 40;
            _pauseBanner.style.left = Length.Percent(22);
            _pauseBanner.style.width = Length.Percent(56);
            _pauseBanner.style.minHeight = 40;
            _pauseBanner.style.whiteSpace = WhiteSpace.Normal;
            _pauseBanner.style.fontSize = 18;
            _pauseBanner.style.unityFontStyleAndWeight = FontStyle.Bold;
            HumanUiKit.StyleButton(_pauseBanner);
            _pauseBanner.style.backgroundColor = new StyleColor(new Color(0.45f, 0.08f, 0.08f));
            _pauseBanner.style.display = DisplayStyle.None;
            root.Add(_pauseBanner);

            _notificationListController = GetComponent<NotificationListController>();
            if (!_notificationListController)
                return;

            _notificationListController.Setup(root);
            _notificationListController.enabled = false;
            
            _gameButtonHandler = GetComponent<GameButtonHandler>();
            if (!_gameButtonHandler)
                return;
            _gameButtonHandler.Setup(root, _notificationListController);
            _gameButtonHandler.enabled = true;
            
        }

        /// <summary>Opens or closes the Planets screen; the P key calls this through Gameboard.</summary>
        public void TogglePlanets()
        {
            if (uiDocument != null) PlanetsScreen.Toggle(uiDocument.rootVisualElement);
        }

        /// <summary>Shows why the timer stopped (and where, on click), or hides the banner when text is null or empty.</summary>
        public void SetPauseBanner(string text, string planet)
        {
            if (_pauseBanner == null) return;
            _pauseBannerPlanet = planet;
            _pauseBanner.text = text ?? "";
            _pauseBanner.style.display = string.IsNullOrEmpty(text) ? DisplayStyle.None : DisplayStyle.Flex;
        }

        public void SetNotifications(List<PlayerNotification> notifications)
        {
            _notificationListController?.SetNotifications(notifications);
        }

        /// <summary>
        /// Call this each turn to update the status bar display.
        /// </summary>
        public void SetStatus(int turn, float research, string currentResearch, float grotsits)
        {
            if (_turnLabel     != null) _turnLabel.text     = $"Turn: {turn}";
            if (_researchLabel != null) _researchLabel.text = $"Research: {research:0.#}";
            if (_currentResearchLabel != null) _currentResearchLabel.text = string.IsNullOrEmpty(currentResearch) ? "None" : currentResearch;  
            if (_grotsitsLabel != null) _grotsitsLabel.text = $"Grotsits: {grotsits:0.#}";
            if (_researchButton != null)
            {
                var human = Gameboard.Instance != null && Gameboard.Instance.HumanPlayerId >= 0;
                _researchButton.style.display = human ? DisplayStyle.Flex : DisplayStyle.None;
                if (!human) ResearchScreen.Close();
                else ResearchScreen.Refresh();
            }
            if (_fleetsButton != null)
            {
                var human = Gameboard.Instance != null && Gameboard.Instance.HumanPlayerId >= 0;
                _fleetsButton.style.display = human ? DisplayStyle.Flex : DisplayStyle.None;
                if (!human) FleetSummaryScreen.Close();
                else FleetSummaryScreen.Refresh();
            }
            if (_planetsButton != null)
            {
                var human = Gameboard.Instance != null && Gameboard.Instance.HumanPlayerId >= 0;
                _planetsButton.style.display = human ? DisplayStyle.Flex : DisplayStyle.None;
                if (!human) PlanetsScreen.Close();
                else PlanetsScreen.Refresh();
            }
        }

    }
}