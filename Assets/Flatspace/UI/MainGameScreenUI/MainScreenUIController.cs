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

        void OnEnable()
        {
            var root = uiDocument.rootVisualElement;

            // Bind status bar labels
            _turnLabel     = root.Q<Label>("TurnLabel");
            _researchLabel = root.Q<Label>("ResearchLabel");
            _currentResearchLabel = root.Q<Label>("CurrentResearchLabel");
            _grotsitsLabel = root.Q<Label>("GrotsitsLabel");

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
        }
    }
}