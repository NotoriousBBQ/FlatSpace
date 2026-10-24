using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace Game.UI.MainGameScreenUI
{
    /// <summary>
    /// The one small popup of the human UI: fleet icons and order lines in flight show their text through it. A single element on the
    /// HUD document, never picking, kept on screen and placed beside the pointer.
    /// </summary>
    internal static class HumanHoverPopup
    {
        private static VisualElement _panel;

        public static bool Visible => _panel != null && _panel.panel != null && _panel.style.display != DisplayStyle.None;

        public static void Show(VisualElement root, Vector2 screenPosition, IReadOnlyList<string> blocks)
        {
            if (root == null || root.panel == null || blocks == null || blocks.Count == 0)
            {
                Hide();
                return;
            }
            // The element lives and dies with the document; a scene reload leaves a stale static, so rebuild when it is detached.
            if (_panel == null || _panel.panel != root.panel)
            {
                _panel = HumanUiKit.Panel();
                _panel.pickingMode = PickingMode.Ignore;
                _panel.style.position = Position.Absolute;
                root.Add(_panel);
            }

            _panel.Clear();
            foreach (var block in blocks)
                _panel.Add(HumanUiKit.Text(block, size: 13));
            _panel.style.display = DisplayStyle.Flex;

            var at = RuntimePanelUtils.ScreenToPanel(root.panel, screenPosition);
            var size = root.layout.size;
            // Beside the pointer, flipped to the other side near the right or bottom edge (the element's own size is only known after layout,
            // so the flip uses a generous estimate and the clamp keeps it on screen).
            var left = at.x + 16f;
            var top = at.y + 16f;
            if (size.x > 0f && left + 260f > size.x) left = Mathf.Max(0f, at.x - 276f);
            if (size.y > 0f && top + 24f * blocks.Count + 16f > size.y) top = Mathf.Max(0f, at.y - 24f * blocks.Count - 16f);
            _panel.style.left = left;
            _panel.style.top = top;
            _panel.BringToFront();
        }

        public static void Hide()
        {
            if (_panel != null && _panel.panel != null) _panel.style.display = DisplayStyle.None;
        }
    }
}
