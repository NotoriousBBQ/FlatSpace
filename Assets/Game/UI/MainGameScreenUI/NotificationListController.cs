using System;
using System.Collections.Generic;
using FlatSpace.Game;
using UnityEngine;
using UnityEngine.UIElements;

namespace Game.UI.MainGameScreenUI
{
    public class NotificationListController : MonoBehaviour
    {
        private ListView _listView;
        private VisualTreeAsset _listEntryTemplate;
        private List<PlayerNotification> _notifications= new List<PlayerNotification>();
        private List<PlayerNotification> _storedNotifications = new List<PlayerNotification>();

        public void OnEnable()
        {
            if (_listView == null)
                return;
            _listView.visible = true;
        }

        public void OnDisable()
        {
            if (_listView == null)
                return;
            _listView.visible = false;
            
        }

        public void Setup(VisualElement root)
        {
            _listView = root.Q<ListView>("NotificationList");
            _listEntryTemplate = _listView.itemTemplate;
            _listView.makeItem = MakeItem;
            _listView.bindItem = BindItem;
            _listView.fixedItemHeight = 48;
            _listView.itemsSource = _notifications;
            _listView.selectionChanged += OnNotificationSelected;   
            ShowNotifications();
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

        public void SetNotifications(List<PlayerNotification> notifications)
        {
            _storedNotifications.Clear();
            _storedNotifications.AddRange(notifications);
            if (enabled)
                ShowNotifications();
        }
        public void ShowNotifications()
        {
            _notifications.Clear();
            _notifications.AddRange(_storedNotifications);

            _listView?.RefreshItems();
        }
    }
}
