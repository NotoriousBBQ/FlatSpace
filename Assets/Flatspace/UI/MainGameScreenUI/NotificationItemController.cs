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
            // The template's dark grey text on the list's light rows could not be read: white on a dark row.
            visualElement.style.backgroundColor = new StyleColor(new UnityEngine.Color(0.1f, 0.1f, 0.14f, 0.92f));
            if (_displayLabel != null)
            {
                _displayLabel.style.color = new StyleColor(UnityEngine.Color.white);
                _displayLabel.style.whiteSpace = WhiteSpace.Normal;
            }
        }

        public void SetNotification(PlayerNotification notification)
        {
            if(_displayLabel == null) return;
            _displayLabel.text = notification.Message;
            _viewTargetName = notification.ViewTarget;
        }
    }
}
