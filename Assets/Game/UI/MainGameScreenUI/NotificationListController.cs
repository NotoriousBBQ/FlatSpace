using System;
using System.Collections.Generic;
using FlatSpace.Game;
using UnityEngine;
using UnityEngine.UIElements;

namespace Game.UI.MainGameScreenUI
{
    public class NotificationListController : MonoBehaviour
    {
        public UIDocument uiDocument;

        private ListView _listView;
        private VisualTreeAsset _listEntryTemplate;
        private List<PlayerNotification> _notifications;

        public void OnEnable()
        {
            _notifications = new List<PlayerNotification>();
            
            _listView = uiDocument.rootVisualElement.Q<ListView>("NotificationList");
            _listView.makeItem = MakeItem;
            _listView.bindItem = BindItem;
            _listView.fixedItemHeight = 24;
            _listView.itemsSource = _notifications;
            _listView.selectionChanged += OnNotificationSelected;
            
            _listEntryTemplate = _listView.itemTemplate;

            DEBUG_NotificationsTest();
 //           uiDocument.enabled = false;
            
        }

        private void DEBUG_NotificationsTest()
        {
            var notifications = new List<PlayerNotification>();
            notifications.Add(new PlayerNotification
            {
                Message = "Capitol One Created",
                PlayerName = "Player One",
                ViewTarget = "Prime 0"
            });
            notifications.Add(new PlayerNotification
            {
                Message = "Capitol Two Created",
                PlayerName = "Player Two",
                ViewTarget = "Prime 1"
            });
            
            ShowNotifications(notifications);
        }

        private void OnNotificationSelected(IEnumerable<object> selectedItems)
        {
            var selectedNotification = _listView.selectedItem as PlayerNotification;
            Gameboard.Instance.ViewPlanet(selectedNotification?.ViewTarget ?? string.Empty);
        }

        private VisualElement MakeItem()
        {
            var newItem = _listEntryTemplate.Instantiate();
            var newItemController = new NotificationItemController();
            
            newItem.userData = newItemController;
            newItemController.SetVisualElement(newItem);
            
            return newItem;
        }

        private void BindItem(VisualElement item, int index)
        {
            (item.userData as NotificationItemController)?.SetNotification(_notifications[index]);
        }

        private void ClearNotifications()
        {
            _notifications.Clear();
            _listView.ClearBindings();
        }

        public void ShowNotifications(List<PlayerNotification> notifications)
        {
           // ClearNotifications();
           _notifications.Clear();
            _notifications.AddRange(notifications);
        }
    }
}
