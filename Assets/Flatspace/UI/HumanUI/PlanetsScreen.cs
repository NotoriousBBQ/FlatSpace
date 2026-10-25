using System.Collections.Generic;
using FlatSpace.Game;
using FlatSpace.Human;
using UnityEngine;
using UnityEngine.UIElements;

namespace Game.UI.MainGameScreenUI
{
    /// <summary>
    /// The Planets screen: every planet the human knows in one sortable table, with a search box and four filters. A row click opens that planet's
    /// panel and closes the screen. Opened from the Planets button or the P key; the rows, filters, search and sort come from the pure
    /// HumanPlanetTable, so this is only layout. Filter, search and sort live in static fields and reset at the start of a match (ResetState).
    /// </summary>
    public static class PlanetsScreen
    {
        private struct Column
        {
            public PlanetColumn Id;
            public string Title;
            public float Width;
        }

        private static readonly Column[] Columns =
        {
            new Column { Id = PlanetColumn.Planet, Title = "Planet", Width = 90f },
            new Column { Id = PlanetColumn.Owner, Title = "Owner", Width = 170f },
            new Column { Id = PlanetColumn.Type, Title = "Type", Width = 80f },
            new Column { Id = PlanetColumn.Pop, Title = "Pop", Width = 55f },
            new Column { Id = PlanetColumn.Food, Title = "Food", Width = 72f },
            new Column { Id = PlanetColumn.Grotsits, Title = "Grots", Width = 72f },
            new Column { Id = PlanetColumn.Industry, Title = "Ind", Width = 72f },
            new Column { Id = PlanetColumn.Research, Title = "Res", Width = 72f },
            new Column { Id = PlanetColumn.Building, Title = "Building", Width = 150f },
            new Column { Id = PlanetColumn.Ships, Title = "Ships", Width = 150f },
            new Column { Id = PlanetColumn.Alerts, Title = "Alerts", Width = 130f },
        };

        private static readonly string[] FilterNames = { "All known", "Colonization targets", "Mine", "Hostile activity" };

        // The order of the radio buttons; the default is Mine.
        private static readonly PlanetFilter[] FilterOrder =
            { PlanetFilter.AllKnown, PlanetFilter.ColonizationTargets, PlanetFilter.Mine, PlanetFilter.Hostile };

        private static PlanetFilter _filter = PlanetFilter.Mine;
        private static SortState _sort;
        private static string _search = "";

        private static VisualElement _screen;
        private static TextField _searchField;
        private static readonly List<RadioButton> _filterButtons = new List<RadioButton>();
        private static VisualElement _headerRow;
        private static ScrollView _rows;
        private static List<PlanetRow> _all = new List<PlanetRow>();

        public static bool IsOpen => _screen != null && _screen.parent != null;

        /// <summary>Whether the search box has the keyboard: the P key must then type a letter, not open anything.</summary>
        public static bool SearchHasFocus
        {
            get
            {
                if (!IsOpen || _searchField == null || _searchField.panel == null) return false;
                var focused = _searchField.panel.focusController?.focusedElement as VisualElement;
                return focused != null && (focused == _searchField || _searchField.Contains(focused));
            }
        }

        /// <summary>Whether any text input on any HUD or panel document has the keyboard (the search box, an amount field): P must then be a letter.</summary>
        public static bool AnyTextFieldFocused()
        {
            foreach (var document in Object.FindObjectsByType<UIDocument>())
            {
                var root = document.rootVisualElement;
                var focused = root?.panel?.focusController?.focusedElement as VisualElement;
                if (focused != null && focused.ClassListContains("unity-base-text-field__input")) return true;
            }
            return false;
        }

        /// <summary>Back to the defaults: the Mine filter, no sort, an empty search. Called when a match starts or a game loads.</summary>
        public static void ResetState()
        {
            _filter = PlanetFilter.Mine;
            _sort = new SortState();
            _search = "";
        }

        public static void Toggle(VisualElement host)
        {
            if (IsOpen) { Close(); return; }
            if (host == null || Gameboard.Instance == null || Gameboard.Instance.HumanPlayerId < 0) return;
            Build(host);
            Refresh();
        }

        public static void Close()
        {
            _screen?.RemoveFromHierarchy();
            _screen = null;
            _searchField = null;
            _filterButtons.Clear();
            _headerRow = null;
            _rows = null;
        }

        /// <summary>Whether a screen point is on the screen: a press there is not a click outside the planet or fleet panel.</summary>
        public static bool ContainsScreenPoint(Vector2 screenPosition)
        {
            if (!IsOpen || _screen.panel == null) return false;
            return _screen.worldBound.Contains(RuntimePanelUtils.ScreenToPanel(_screen.panel, screenPosition));
        }

        private static void Build(VisualElement host)
        {
            _screen = HumanUiKit.Panel();
            _screen.style.position = Position.Absolute;
            _screen.style.left = Length.Percent(1);
            _screen.style.right = Length.Percent(1);
            _screen.style.top = Length.Percent(8);
            _screen.style.bottom = Length.Percent(3);   // inside the window however short it is; the rows scroll
            _screen.style.maxWidth = 1150;

            var top = HumanUiKit.Row();
            var header = HumanUiKit.Header("Planets");
            header.style.flexGrow = 1;
            top.Add(header);
            top.Add(HumanUiKit.Btn("Close", Close));
            _screen.Add(top);

            _searchField = new TextField { value = _search };
            _searchField.style.marginTop = 4;
            _searchField.tooltip = "Search planets by name";
            // The caret is black and cannot be recoloured from code (the USS property replaced the old calls), so the field is light: black text and a
            // black caret on light grey read clearly against the dark panel.
            var searchInput = _searchField.Q(className: TextField.inputUssClassName);
            if (searchInput != null)
            {
                searchInput.style.color = new StyleColor(Color.black);
                searchInput.style.backgroundColor = new StyleColor(new Color(0.88f, 0.88f, 0.88f));
            }
            _searchField.RegisterValueChangedCallback(e => { _search = e.newValue ?? ""; ApplyView(); });
            _screen.Add(_searchField);

            var filterBox = new GroupBox();   // radio buttons in one GroupBox exclude each other; a row keeps them side by side
            filterBox.style.flexDirection = FlexDirection.Row;
            filterBox.style.marginTop = 4;
            filterBox.style.marginLeft = filterBox.style.marginRight = 0;
            filterBox.style.paddingTop = filterBox.style.paddingBottom = 0;
            _filterButtons.Clear();
            for (var i = 0; i < FilterOrder.Length; i++)
            {
                var index = i;
                var radio = new RadioButton { label = FilterNames[i] };
                radio.style.color = new StyleColor(Color.white);
                // The label of a field has a fixed wide slot: right-align the text so it sits against the button instead of floating far from it.
                radio.labelElement.style.color = new StyleColor(Color.white);
                radio.labelElement.style.unityTextAlign = TextAnchor.MiddleRight;
                radio.labelElement.style.minWidth = 0;
                radio.labelElement.style.whiteSpace = WhiteSpace.NoWrap;
                radio.style.marginRight = 12;
                radio.SetValueWithoutNotify(FilterOrder[i] == _filter);
                radio.RegisterValueChangedCallback(e =>
                {
                    if (!e.newValue) return;
                    _filter = FilterOrder[index];
                    ApplyView();
                });
                _filterButtons.Add(radio);
                filterBox.Add(radio);
            }
            _screen.Add(filterBox);

            _headerRow = HumanUiKit.Row();
            _screen.Add(_headerRow);

            _rows = new ScrollView(ScrollViewMode.Vertical) { pickingMode = PickingMode.Position };
            _rows.horizontalScrollerVisibility = ScrollerVisibility.Hidden;
            _rows.style.flexGrow = 1;   // the rest of the panel; the header above stays put
            _rows.style.flexShrink = 1;
            _rows.style.minHeight = 0;
            _screen.Add(_rows);
            host.Add(_screen);

            // The cursor starts in the search box so a name can be typed at once; focus needs the element to be laid out first.
            _searchField.schedule.Execute(() => _searchField?.Focus());
        }

        /// <summary>Rebuilds the rows from the current board; a no-op while the screen is closed.</summary>
        public static void Refresh()
        {
            if (!IsOpen || _rows == null || Gameboard.Instance == null) return;
            if (Gameboard.Instance.HumanPlayerId < 0)
            {
                Close();
                return;
            }
            _all = Gameboard.Instance.BuildPlanetRows();
            ApplyView();
        }

        // Filter, search and sort over the rows already built; used by every control so typing never rebuilds the board's rows.
        private static void ApplyView()
        {
            if (!IsOpen || _rows == null) return;

            // The counts follow the board, so the labels are refreshed each time.
            for (var i = 0; i < FilterOrder.Length && i < _filterButtons.Count; i++)
            {
                _filterButtons[i].label = $"{FilterNames[i]} ({HumanPlanetTable.Count(_all, FilterOrder[i])})";
                _filterButtons[i].SetValueWithoutNotify(FilterOrder[i] == _filter);
            }

            BuildHeader();

            var view = HumanPlanetTable.Sort(HumanPlanetTable.Search(HumanPlanetTable.Filter(_all, _filter), _search), _sort);
            _rows.Clear();
            if (view.Count == 0)
            {
                _rows.Add(HumanUiKit.Text("No planets match.", HumanUiKit.Muted));
                return;
            }
            foreach (var row in view)
                _rows.Add(BuildRow(row));
        }

        // A fixed-width cell becomes a flex basis that may shrink: the table fits the window, long text is cut with an ellipsis.
        private static Label Fit(Label cell)
        {
            cell.style.flexShrink = 1;
            cell.style.marginRight = 6;   // a gap, so a sort arrow never touches the next heading
            cell.style.minWidth = 0;
            cell.style.whiteSpace = WhiteSpace.NoWrap;
            cell.style.overflow = Overflow.Hidden;
            cell.style.textOverflow = TextOverflow.Ellipsis;
            return cell;
        }

        private static void BuildHeader()
        {
            _headerRow.Clear();
            foreach (var column in Columns)
            {
                var title = column.Title;
                if (_sort.Active && _sort.Column == column.Id) title += _sort.Descending ? " v" : " ^";
                var cell = Fit(HumanUiKit.Cell(title, column.Width, HumanUiKit.Warn));
                cell.style.unityFontStyleAndWeight = FontStyle.Bold;
                cell.pickingMode = PickingMode.Position;
                var id = column.Id;
                cell.RegisterCallback<ClickEvent>(_ =>
                {
                    _sort = HumanPlanetTable.Click(_sort, id);
                    ApplyView();
                });
                _headerRow.Add(cell);
            }
        }

        private static VisualElement BuildRow(PlanetRow row)
        {
            var line = HumanUiKit.Row();
            line.pickingMode = PickingMode.Position;
            var muted = row.InSight ? (Color?)null : HumanUiKit.Muted;
            line.Add(Fit(HumanUiKit.Cell(row.Planet, Columns[0].Width, row.IsMine ? HumanUiKit.Good : (Color?)null)));
            line.Add(Fit(HumanUiKit.Cell(row.OwnerText, Columns[1].Width, muted)));
            line.Add(Fit(HumanUiKit.Cell(row.Type, Columns[2].Width, muted)));
            line.Add(Fit(HumanUiKit.Cell(row.Pop.HasValue ? $"{row.Pop}/{row.MaxPop}" : $"/{row.MaxPop}", Columns[3].Width, muted)));
            line.Add(Fit(HumanUiKit.Cell(Rate(row.Food), Columns[4].Width, RateColor(row.Food))));
            line.Add(Fit(HumanUiKit.Cell(Rate(row.Grotsits), Columns[5].Width, RateColor(row.Grotsits))));
            line.Add(Fit(HumanUiKit.Cell(Rate(row.Industry), Columns[6].Width)));
            line.Add(Fit(HumanUiKit.Cell(Rate(row.Research), Columns[7].Width)));
            var building = string.IsNullOrEmpty(row.BuildItem) ? ""
                : row.BuildTurns.HasValue ? $"{row.BuildItem}, {row.BuildTurns} {(row.BuildTurns == 1 ? "turn" : "turns")}" : row.BuildItem;
            line.Add(Fit(HumanUiKit.Cell(building, Columns[8].Width)));
            var ships = !row.Ships.HasValue ? "" : row.Ships.Value == 0 || string.IsNullOrEmpty(row.ShipOwners) ? row.Ships.Value.ToString() : $"{row.Ships.Value} ({row.ShipOwners})";
            line.Add(Fit(HumanUiKit.Cell(ships, Columns[9].Width)));
            line.Add(Fit(HumanUiKit.Cell(row.Alerts, Columns[10].Width, HumanUiKit.Warn)));
            var planet = row.Planet;
            line.RegisterCallback<ClickEvent>(_ =>
            {
                Gameboard.Instance.GoToPlanet(planet);
                Close();   // choosing a row is the screen's whole job: it goes away and the planet's panel shows
            });
            return line;
        }

        private static string Rate(float? value) => value.HasValue ? $"{value.Value:+0.#;-0.#;0}" : "";

        private static Color? RateColor(float? value) => value.HasValue && value.Value < 0f ? HumanUiKit.Bad : (Color?)null;
    }
}
