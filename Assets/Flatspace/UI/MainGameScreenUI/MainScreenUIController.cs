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

        private GameButtonHandler _gameButtonHandler;
        private NotificationListController _notificationListController;

        void OnEnable()
        {
            var root = uiDocument.rootVisualElement;
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
    }
}