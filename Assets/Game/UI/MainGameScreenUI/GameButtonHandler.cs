using UnityEngine;
using UnityEngine.UIElements;
using FlatSpace.Game;
using Game.UI.MainGameScreenUI;

namespace Game.UI.MainGameScreenUI
{
    public class GameButtonHandler : MonoBehaviour
    {
        public UIDocument uiDocument;

        private Button _nextTurnButton;
        private Button _startRunButton;
        private Button _showNotificationsButton;
        private Button _stopRunButton;
        private Button _saveButton;
        private Button _loadButton;
        private NotificationListController _notificationListController;

        public void Setup(VisualElement root, NotificationListController notificationListController)
        {
            _notificationListController = notificationListController;
            _nextTurnButton = root.Q<Button>("NextTurnButton");
            if (_nextTurnButton != null)
            {
                _nextTurnButton.clicked += OnNextTurnButtonClicked;
            }

            _showNotificationsButton = root.Q<Button>("ShowNotificationsButton");
            if (_showNotificationsButton != null)
            {
                if (!_notificationListController)
                {
                    _showNotificationsButton.SetEnabled(false);
                }
                else
                {
                    _showNotificationsButton.clicked += OnShowNotificationsButtonClicked;
                }
            }

            _startRunButton = root.Q<Button>("StartRunButton");
            if (_startRunButton != null)
            {
                _startRunButton.clicked += OnStartRunButtonClicked;
            }

            _stopRunButton = root.Q<Button>("StopRunButton");
            if (_stopRunButton != null)
            {
                _stopRunButton.clicked += OnStopRunButtonClicked;
                _stopRunButton.SetEnabled(false);
            }

            _saveButton = root.Q<Button>("TestSaveButton");
            if (_saveButton != null)
            {
                _saveButton.clicked += OnSaveButtonClicked;
                _saveButton.visible = false;
            }

            _loadButton = root.Q<Button>("TestLoadButton");
            if (_loadButton != null)
            {
                _loadButton.clicked += OnLoadButtonClicked;
                _loadButton.visible = false;
            }

        }

        void OnDisable()
        {
            if (_nextTurnButton != null)
            {
                _nextTurnButton.clicked -= OnNextTurnButtonClicked;
            }

            if (_showNotificationsButton != null)
            {
                _showNotificationsButton.clicked -= OnShowNotificationsButtonClicked;
            }

            if (_startRunButton != null)
            {
                _startRunButton.clicked -= OnStartRunButtonClicked;
            }

            if (_stopRunButton != null)
            {
                _stopRunButton.clicked -= OnStopRunButtonClicked;
            }

            if (_saveButton != null)
            {
                _saveButton.clicked -= OnSaveButtonClicked;
            }

            if (_loadButton != null)
            {
                _loadButton.clicked -= OnLoadButtonClicked;
            }
        }

        private void OnNextTurnButtonClicked()
        {
            Gameboard.Instance.SingleUpdate();
        }

        private void SetShowNotificationButtonText()
        {
            _showNotificationsButton.text =
                _notificationListController.enabled ? "Hide Notifications" : "Show Notifications";
        }

        private void OnShowNotificationsButtonClicked()
        {
            if (!_notificationListController)
                return;
            _notificationListController.enabled = !_notificationListController.enabled;
            SetShowNotificationButtonText();
        }

        private void OnStartRunButtonClicked()
        {
            _nextTurnButton.SetEnabled(false);
            _startRunButton.SetEnabled(false);
            _stopRunButton.SetEnabled(true);
            Gameboard.Instance.StartTimedUpdate();
        }

        private void OnStopRunButtonClicked()
        {
            _nextTurnButton.SetEnabled(true);
            _startRunButton.SetEnabled(true);
            _stopRunButton.SetEnabled(false);
            Gameboard.Instance.StopTimedUpdate();
        }

        private static void OnSaveButtonClicked()
        {
            SaveLoadSystem.SaveGame();
        }

        private static void OnLoadButtonClicked()
        {
            SaveLoadSystem.LoadGame();
        }

        public void EscapeButtonPressed()
        {
            _saveButton.visible = !_saveButton.visible;
            _loadButton.visible = !_loadButton.visible;
        }
    }
}