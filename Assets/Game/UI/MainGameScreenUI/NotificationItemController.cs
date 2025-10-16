using UnityEngine.UIElements;

namespace Game.UI.MainGameScreenUI
{
    public class NotificationItemController
    {
        private Label _displayLabel;
        private string _viewTargetName;
        
        public void SetVisualElement(VisualElement visualElement)
        {
            _displayLabel = visualElement.Q<Label>("DisplayLabel");
        }

        public void SetNotification(PlayerNotification notification)
        {
            if(_displayLabel == null) return;
            _displayLabel.text = notification.Message;
            _viewTargetName = notification.ViewTarget;
        }
    }
}
