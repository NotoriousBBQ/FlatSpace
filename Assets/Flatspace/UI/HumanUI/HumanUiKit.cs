using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace Game.UI.MainGameScreenUI
{
    /// <summary>The small widgets the human UI screens are built from, in the existing dark theme (black panels, red border, white text).</summary>
    internal static class HumanUiKit
    {
        public static readonly Color Warn = new Color(1f, 0.75f, 0.2f);
        public static readonly Color Bad = new Color(1f, 0.4f, 0.4f);
        public static readonly Color Muted = new Color(0.55f, 0.55f, 0.55f);
        public static readonly Color Good = new Color(0.5f, 0.9f, 0.5f);

        public static VisualElement Panel(float widthPx = 0f)
        {
            var panel = new VisualElement { pickingMode = PickingMode.Position };
            panel.style.backgroundColor = new StyleColor(new Color(0f, 0f, 0f, 0.95f));
            panel.style.color = new StyleColor(Color.white);
            panel.style.borderTopWidth = panel.style.borderRightWidth = panel.style.borderBottomWidth = panel.style.borderLeftWidth = 2;
            panel.style.borderTopColor = panel.style.borderRightColor = panel.style.borderBottomColor = panel.style.borderLeftColor = new StyleColor(Color.red);
            panel.style.paddingTop = panel.style.paddingBottom = 6;
            panel.style.paddingLeft = panel.style.paddingRight = 8;
            if (widthPx > 0f) panel.style.width = widthPx;
            return panel;
        }

        public static Label Text(string text, Color? color = null, bool bold = false, int size = 14)
        {
            var label = new Label(text) { pickingMode = PickingMode.Ignore };
            label.style.color = new StyleColor(color ?? Color.white);
            label.style.fontSize = size;
            if (bold) label.style.unityFontStyleAndWeight = FontStyle.Bold;
            label.style.whiteSpace = WhiteSpace.Normal;
            return label;
        }

        public static Label Header(string text) => Text(text, bold: true, size: 16);

        public static VisualElement Row()
        {
            var row = new VisualElement { pickingMode = PickingMode.Ignore };
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems = Align.Center;
            row.style.marginTop = 2;
            return row;
        }

        public static Button Btn(string text, Action onClick)
        {
            var button = new Button(onClick) { text = text };
            StyleButton(button);
            return button;
        }

        /// <summary>The default runtime button is light grey; white text on it cannot be read. Dark button, white text, thin light border.</summary>
        public static void StyleButton(Button button)
        {
            button.style.marginLeft = button.style.marginRight = 2;
            button.style.backgroundColor = new StyleColor(new Color(0.16f, 0.17f, 0.24f));
            button.style.color = new StyleColor(Color.white);
            button.style.borderTopWidth = button.style.borderRightWidth = button.style.borderBottomWidth = button.style.borderLeftWidth = 1;
            button.style.borderTopColor = button.style.borderRightColor = button.style.borderBottomColor = button.style.borderLeftColor =
                new StyleColor(new Color(0.65f, 0.65f, 0.75f));
            button.style.borderTopLeftRadius = button.style.borderTopRightRadius = button.style.borderBottomLeftRadius = button.style.borderBottomRightRadius = 3;
        }

        /// <summary>An integer field with a light box and black text, so the number is readable.</summary>
        public static void StyleField(IntegerField field)
        {
            var input = field.Q(className: BaseField<int>.inputUssClassName);
            if (input != null) input.style.backgroundColor = new StyleColor(new Color(0.92f, 0.92f, 0.92f));
            foreach (var text in field.Query<TextElement>().ToList())
                text.style.color = new StyleColor(Color.black);
        }

        /// <summary>White text on a foldout's header and any labels inside it.</summary>
        public static void StyleFoldout(Foldout foldout)
        {
            foldout.style.color = new StyleColor(Color.white);
            foreach (var label in foldout.Query<Label>().ToList())
                label.style.color = new StyleColor(Color.white);
        }

        /// <summary>A label that takes a fixed share of a row, for table-like rows.</summary>
        public static Label Cell(string text, float widthPx, Color? color = null)
        {
            var cell = Text(text, color);
            cell.style.width = widthPx;
            cell.style.flexShrink = 0;
            return cell;
        }

        /// <summary>The first line of a refusal list for a status label ("Prime 0: NotEnoughStock; ...").</summary>
        public static string RefusalSummary(System.Collections.Generic.IEnumerable<FlatSpace.Human.HumanCommandResult> results)
        {
            var parts = new System.Collections.Generic.List<string>();
            foreach (var r in results)
                if (!r.applied) parts.Add($"{r.type}: {r.reason}");
            return string.Join("; ", parts);
        }
    }
}
