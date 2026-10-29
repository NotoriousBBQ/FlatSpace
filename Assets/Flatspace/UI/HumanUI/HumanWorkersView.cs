using System;
using System.Collections.Generic;
using FlatSpace.Game;
using FlatSpace.Human;
using UnityEngine;
using UnityEngine.UIElements;

namespace Game.UI.MainGameScreenUI
{
    /// <summary>
    /// The Workers section of the planet panel: one box per production category (Food, Grotsits, Industry, Research) holding one small
    /// square per person. On a planet the human owns and develops by hand, pressing a square picks up that person and everyone after
    /// them in the same category (wrapped lines included); dropping on another category's box sends an assignWorkers command, dropping
    /// anywhere else changes nothing. Below the boxes, a red line per resource the split runs short of. Rebuilt on every panel refresh.
    /// </summary>
    public sealed class HumanWorkersView
    {
        private const float DragThresholdPixels = 6f;
        private const float IconSize = 14f;

        private static readonly string[] Names = { "Food", "Grotsits", "Industry", "Research" };
        private static readonly Color[] Colors =
        {
            new Color(0.45f, 0.75f, 0.35f), new Color(0.85f, 0.65f, 0.3f), new Color(0.6f, 0.6f, 0.65f), new Color(0.4f, 0.65f, 0.9f)
        };

        private Label _ghost;

        public VisualElement Build(Planet planet, int humanId, Action<HumanCommand> send)
        {
            var section = new VisualElement { pickingMode = PickingMode.Ignore };
            section.Add(HumanUiKit.Text("Workers", HumanUiKit.Muted, bold: true));

            var counts = planet.CurrentWorkerCounts();
            var canAssign = HumanWorkerView.CanAssign(planet, humanId);
            var boxes = new List<VisualElement>();
            var grid = new VisualElement { pickingMode = PickingMode.Ignore };
            grid.style.flexDirection = FlexDirection.Row;
            grid.style.flexWrap = Wrap.Wrap;

            for (var c = 0; c < 4; c++)
            {
                var category = (Planet.WorkerCategory)c;
                var box = new VisualElement { pickingMode = PickingMode.Position };
                box.style.width = 150;
                box.style.minHeight = 48;   // an empty category must still be big enough to drop on
                box.style.marginRight = 6;
                box.style.marginBottom = 4;
                box.style.paddingLeft = box.style.paddingRight = box.style.paddingTop = box.style.paddingBottom = 4;
                box.style.borderTopWidth = box.style.borderBottomWidth = box.style.borderLeftWidth = box.style.borderRightWidth = 1;
                box.style.borderTopColor = box.style.borderBottomColor = box.style.borderLeftColor = box.style.borderRightColor = new StyleColor(HumanUiKit.Muted);
                box.Add(HumanUiKit.Text($"{Names[c]} ({counts[c]})", size: 12));

                var icons = new VisualElement { pickingMode = PickingMode.Ignore };
                icons.style.flexDirection = FlexDirection.Row;
                icons.style.flexWrap = Wrap.Wrap;
                var iconList = new List<VisualElement>();
                for (var i = 0; i < counts[c]; i++)
                {
                    var icon = new VisualElement { pickingMode = canAssign ? PickingMode.Position : PickingMode.Ignore };
                    icon.style.width = icon.style.height = IconSize;
                    icon.style.marginRight = icon.style.marginBottom = 2;
                    icon.style.backgroundColor = new StyleColor(Colors[c]);
                    iconList.Add(icon);
                    icons.Add(icon);
                }
                if (canAssign)
                    for (var i = 0; i < iconList.Count; i++)
                        MakeDraggable(iconList[i], iconList, i, category, planet, counts, boxes, send);
                box.Add(icons);
                boxes.Add(box);
                grid.Add(box);
            }
            section.Add(grid);

            foreach (var line in HumanWorkerView.Warnings(planet))
                section.Add(HumanUiKit.Text(line, HumanUiKit.Bad));
            return section;
        }

        /// <summary>Removes the drag ghost; the panel calls this before it rebuilds, since a turn can interrupt a drag.</summary>
        public void CancelDrag()
        {
            if (_ghost == null) return;
            _ghost.RemoveFromHierarchy();
            _ghost = null;
        }

        // Arms a press-drag-release on one icon: the press dims the picked icons, past the threshold a ghost with the picked count follows the
        // pointer, and on release the box under the pointer decides the drop. Dropping on the same box or outside every box does nothing.
        private void MakeDraggable(VisualElement icon, List<VisualElement> categoryIcons, int index, Planet.WorkerCategory from, Planet planet,
            int[] counts, List<VisualElement> boxes, Action<HumanCommand> send)
        {
            var picked = HumanWorkerView.PickedCount(categoryIcons.Count, index);
            var pressed = false;
            var dragging = false;
            var start = Vector2.zero;
            VisualElement host = null;

            void Restore()
            {
                CancelDrag();
                foreach (var other in categoryIcons) other.style.opacity = 1f;
            }

            icon.RegisterCallback<PointerDownEvent>(evt =>
            {
                if (evt.button != 0) return;
                pressed = true;
                dragging = false;
                start = evt.position;
                icon.CapturePointer(evt.pointerId);
                for (var i = index; i < categoryIcons.Count; i++) categoryIcons[i].style.opacity = 0.35f;
            });
            icon.RegisterCallback<PointerMoveEvent>(evt =>
            {
                if (!pressed) return;
                if (!dragging && Vector2.Distance(evt.position, start) < DragThresholdPixels) return;
                if (!dragging)
                {
                    dragging = true;
                    host = icon.panel.visualTree;
                    _ghost = new Label(picked.ToString()) { pickingMode = PickingMode.Ignore };
                    _ghost.style.position = Position.Absolute;
                    _ghost.style.backgroundColor = new StyleColor(new Color(0.2f, 0.2f, 0.2f, 0.9f));
                    _ghost.style.color = new StyleColor(Color.white);
                    _ghost.style.paddingLeft = _ghost.style.paddingRight = 6;
                    host.Add(_ghost);
                }
                var local = host.WorldToLocal(evt.position);
                _ghost.style.left = local.x + 8;
                _ghost.style.top = local.y + 8;
            });
            icon.RegisterCallback<PointerUpEvent>(evt =>
            {
                if (!pressed) return;
                pressed = false;
                icon.ReleasePointer(evt.pointerId);
                var wasDragging = dragging;
                dragging = false;
                Restore();
                if (!wasDragging) return;
                for (var c = 0; c < boxes.Count; c++)
                {
                    if (!boxes[c].worldBound.Contains(evt.position)) continue;
                    var command = HumanWorkerView.DropCommand(planet, counts, from, (Planet.WorkerCategory)c, picked);
                    if (command != null) send(command);
                    return;
                }
            });
        }
    }
}
