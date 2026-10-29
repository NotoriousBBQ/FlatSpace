using System;
using System.Collections.Generic;
using FlatSpace.AI;
using FlatSpace.Game;
using FlatSpace.Human;
using UnityEngine;
using UnityEngine.UIElements;

namespace Game.UI.MainGameScreenUI
{
    /// <summary>
    /// The Diplomacy screen (docs/superpowers/specs/2026-10-29-diplomacy-screen-design.md): the rivals the human has met on the left, the picked
    /// rival on the right (stances, its hostility toward the human, its stance toward the other rivals met, its fleets on the planets in sight) and
    /// the buttons Declare war, End the war, Surrender and the inactive Demand surrender. The response area in the upper right stays empty until the
    /// negotiation project fills it. The content comes from the pure HumanDiplomacyView; this is only layout. Opened from the Diplomacy button or
    /// the T key; Esc or Close shuts it.
    /// </summary>
    public static class DiplomacyScreen
    {
        private const string DeclareWar = "declareWar";
        private const string EndWar = "makePeace";
        private const string Surrender = "surrender";

        private static int _selected = -1;
        private static string _pending;   // the command waiting for its confirmation, or null
        private static int _pendingTurn;  // the turn it was asked on: a new turn may have changed what is allowed, so the question lapses

        // The rival's latest reply (shown in the response box at the top). Kept here so it survives the screen being closed and reopened.
        private static string _replyText;
        private static string _replyFrom;
        private static Label _replyLabel;

        private static VisualElement _screen;
        private static VisualElement _host;
        private static ScrollView _list;
        private static ScrollView _detail;

        public static bool IsOpen => _screen != null && _screen.parent != null;

        /// <summary>No rival picked and nothing awaiting confirmation. Called when a match starts or a game loads.</summary>
        public static void ResetState()
        {
            _selected = -1;
            _pending = null;
            _replyText = null;
            _replyFrom = null;
        }

        public static void Toggle(VisualElement host)
        {
            if (IsOpen) { Close(); return; }
            if (host == null || Gameboard.Instance == null || Gameboard.Instance.HumanPlayerId < 0) return;
            Build(host);
            Refresh();
            // The rival picked on open (the first, or the one last viewed) greets the human, like a click on a rival does.
            if (IsOpen && _selected >= 0) RequestReply("Negotiations Begin", _selected);
        }

        public static void Close()
        {
            _screen?.RemoveFromHierarchy();
            _screen = null;
            _host = null;
            _list = null;
            _detail = null;
            _replyLabel = null;
            _pending = null;
        }

        /// <summary>Whether a screen point is on the screen: a press there is not a click outside the planet or fleet panel.</summary>
        public static bool ContainsScreenPoint(Vector2 screenPosition)
        {
            if (!IsOpen || _screen.panel == null) return false;
            return _screen.worldBound.Contains(RuntimePanelUtils.ScreenToPanel(_screen.panel, screenPosition));
        }

        private static void Build(VisualElement host)
        {
            _host = host;
            _screen = HumanUiKit.Panel();
            _screen.style.position = Position.Absolute;
            _screen.style.left = Length.Percent(1);
            _screen.style.right = Length.Percent(1);
            _screen.style.top = Length.Percent(8);
            _screen.style.bottom = Length.Percent(3);
            _screen.style.maxWidth = 1000;

            var top = HumanUiKit.Row();
            top.style.alignItems = Align.FlexStart;
            var header = HumanUiKit.Header("Diplomacy");
            header.style.flexGrow = 1;
            top.Add(header);
            // Where a rival's answers to proposals will appear (project 2). Empty for now.
            var response = HumanUiKit.Panel(280f);
            response.style.minHeight = 44;
            response.tooltip = "A rival's replies appear here";
            _replyLabel = HumanUiKit.Text("", size: 13);
            response.Add(_replyLabel);
            ShowReply();
            top.Add(response);
            top.Add(HumanUiKit.Btn("Close", Close));
            _screen.Add(top);

            var body = HumanUiKit.Row();
            body.style.flexGrow = 1;
            body.style.alignItems = Align.Stretch;
            body.style.minHeight = 0;
            body.style.marginTop = 6;

            _list = new ScrollView(ScrollViewMode.Vertical) { pickingMode = PickingMode.Position };
            _list.horizontalScrollerVisibility = ScrollerVisibility.Hidden;
            _list.style.width = 200;
            _list.style.flexShrink = 0;
            _list.style.borderRightWidth = 1;
            _list.style.borderRightColor = new StyleColor(HumanUiKit.Muted);
            _list.style.paddingRight = 6;
            body.Add(_list);

            _detail = new ScrollView(ScrollViewMode.Vertical) { pickingMode = PickingMode.Position };
            _detail.horizontalScrollerVisibility = ScrollerVisibility.Hidden;
            _detail.style.flexGrow = 1;
            _detail.style.paddingLeft = 10;
            body.Add(_detail);

            _screen.Add(body);
            host.Add(_screen);
        }

        /// <summary>Rebuilds both panes from the current board; a no-op while the screen is closed.</summary>
        public static void Refresh()
        {
            if (!IsOpen || _list == null || Gameboard.Instance == null) return;
            if (Gameboard.Instance.HumanPlayerId < 0)
            {
                Close();
                return;
            }

            if (_pending != null && Gameboard.Instance.TurnNumber != _pendingTurn) _pending = null;
            var rows = Gameboard.Instance.BuildDiplomacyRows();
            // The picked rival may no longer be a contact (or the match was reloaded): fall back to the first row, or to none.
            if (!rows.Exists(r => r.Rival == _selected))
            {
                _selected = rows.Count > 0 ? rows[0].Rival : -1;
                _pending = null;
            }

            _list.Clear();
            _detail.Clear();
            if (!HumanDiplomacyView.IsOn(Gameboard.Instance.GameAI.GameAIMap))
            {
                _detail.Add(HumanUiKit.Text("Diplomacy is switched off in this match.", HumanUiKit.Muted));
                return;
            }
            if (rows.Count == 0)
            {
                _list.Add(HumanUiKit.Text("No rivals met yet.", HumanUiKit.Muted));
                return;
            }

            foreach (var row in rows)
                _list.Add(BuildListRow(row));

            var view = Gameboard.Instance.BuildRivalView(_selected);
            if (view != null) BuildDetail(view);
        }

        private static VisualElement BuildListRow(RivalRow row)
        {
            var line = HumanUiKit.Row();
            line.pickingMode = PickingMode.Position;
            line.style.paddingTop = line.style.paddingBottom = 4;
            line.style.paddingLeft = 4;
            if (row.Rival == _selected) line.style.backgroundColor = new StyleColor(new Color(0.23f, 0.29f, 0.4f));
            line.Add(Swatch(row.Rival));
            line.Add(HumanUiKit.Cell(row.Label, 80f));
            line.Add(HumanUiKit.Text(row.State, row.AtWar ? HumanUiKit.Bad : row.State == "Truce" ? HumanUiKit.Warn : HumanUiKit.Good));
            var rival = row.Rival;
            line.RegisterCallback<ClickEvent>(_ =>
            {
                var changed = rival != _selected;
                _selected = rival;
                _pending = null;
                if (changed) RequestReply("Negotiations Begin", rival);
                Refresh();
            });
            return line;
        }

        private static VisualElement Swatch(int player)
        {
            var swatch = new VisualElement { pickingMode = PickingMode.Ignore };
            swatch.style.width = swatch.style.height = 12;
            swatch.style.marginRight = 6;
            swatch.style.backgroundColor = new StyleColor(player >= 0 && player < Player.PlayerColors.Count ? (Color)Player.PlayerColors[player] : Color.grey);
            return swatch;
        }

        private static void BuildDetail(RivalView view)
        {
            var title = HumanUiKit.Row();
            title.Add(Swatch(view.Rival));
            title.Add(HumanUiKit.Header(view.Label));
            _detail.Add(title);

            _detail.Add(Section("Between us"));
            _detail.Add(StanceLine("My stance toward them", view.MyStance, view.MyChangeTurn));
            _detail.Add(StanceLine("Their stance toward me", view.TheirStance, view.TheirChangeTurn));
            _detail.Add(HumanUiKit.Text("State: " + view.StateText, view.AtWar ? HumanUiKit.Bad : HumanUiKit.Good));
            var hostility = HumanUiKit.Row();
            hostility.Add(HumanUiKit.Cell("Their hostility toward me", 200f));
            hostility.Add(HostilityBar(view.Hostility, view.HostilityMax));
            hostility.Add(HumanUiKit.Text($"{view.Hostility:0} / {view.HostilityMax:0}"));
            _detail.Add(hostility);

            _detail.Add(Section("Their stance toward others I know"));
            if (view.Others.Count == 0)
                _detail.Add(HumanUiKit.Text("No other rivals met.", HumanUiKit.Muted));
            foreach (var other in view.Others)
            {
                var line = HumanUiKit.Row();
                line.Add(Swatch(other.Rival));
                line.Add(HumanUiKit.Cell(other.Label, 120f));
                line.Add(HumanUiKit.Text(other.Stance.ToString(), other.Stance == Stance.War ? HumanUiKit.Bad : HumanUiKit.Good));
                _detail.Add(line);
            }

            _detail.Add(Section("Their fleets on planets I can see"));
            if (view.Fleets.Count == 0)
                _detail.Add(HumanUiKit.Text("No warships seen on the planets in sight.", HumanUiKit.Muted));
            else
            {
                var head = HumanUiKit.Row();
                head.Add(HumanUiKit.Cell("Planet", 120f, HumanUiKit.Warn));
                head.Add(HumanUiKit.Cell("Warships", 90f, HumanUiKit.Warn));
                head.Add(HumanUiKit.Cell("Strength", 90f, HumanUiKit.Warn));
                _detail.Add(head);
                foreach (var fleet in view.Fleets)
                {
                    var line = HumanUiKit.Row();
                    line.Add(HumanUiKit.Cell(fleet.Planet, 120f));
                    line.Add(HumanUiKit.Cell(fleet.Warships.ToString(), 90f));
                    line.Add(HumanUiKit.Cell(fleet.Strength.ToString("0"), 90f));
                    _detail.Add(line);
                }
                var total = HumanUiKit.Row();
                total.Add(HumanUiKit.Cell("Total seen", 120f, HumanUiKit.Warn));
                total.Add(HumanUiKit.Cell(view.TotalWarships.ToString(), 90f, HumanUiKit.Warn));
                total.Add(HumanUiKit.Cell(view.TotalStrength.ToString("0"), 90f, HumanUiKit.Warn));
                total.Add(HumanUiKit.Text($"Mine: {view.MyStrength:0}", HumanUiKit.Good));
                _detail.Add(total);
            }

            _detail.Add(Section("Actions"));
            _detail.Add(_pending != null ? BuildConfirm(view) : BuildButtons(view));
        }

        private static void Ask(string type)
        {
            _pending = type;
            _pendingTurn = Gameboard.Instance.TurnNumber;
        }

        private static Label Section(string text)
        {
            var label = HumanUiKit.Text(text, HumanUiKit.Warn, bold: true);
            label.style.marginTop = 10;
            return label;
        }

        private static VisualElement StanceLine(string label, Stance stance, int changeTurn)
        {
            var line = HumanUiKit.Row();
            line.Add(HumanUiKit.Cell(label, 200f));
            line.Add(HumanUiKit.Text(changeTurn >= 0 ? $"{stance} (since turn {changeTurn})" : stance.ToString(),
                stance == Stance.War ? HumanUiKit.Bad : HumanUiKit.Good));
            return line;
        }

        private static VisualElement HostilityBar(float value, float max)
        {
            var frame = new VisualElement { pickingMode = PickingMode.Ignore };
            frame.style.width = 160;
            frame.style.height = 10;
            frame.style.marginRight = 8;
            frame.style.backgroundColor = new StyleColor(new Color(0.25f, 0.25f, 0.25f));
            var fill = new VisualElement { pickingMode = PickingMode.Ignore };
            fill.style.height = 10;
            fill.style.width = Length.Percent(max > 0f ? Mathf.Clamp01(value / max) * 100f : 0f);
            fill.style.backgroundColor = new StyleColor(HumanUiKit.Bad);
            frame.Add(fill);
            return frame;
        }

        private static VisualElement BuildButtons(RivalView view)
        {
            var row = HumanUiKit.Row();
            row.style.alignItems = Align.FlexStart;
            row.Add(ActionButton("Declare war", view.DeclareWar, () => Ask(DeclareWar)));
            row.Add(ActionButton("End the war", view.EndWar, () => Ask(EndWar)));
            row.Add(ActionButton("Surrender", view.Surrender, () => Ask(Surrender)));
            row.Add(ActionButton("Demand surrender", view.DemandSurrender, null));
            return row;
        }

        // A button with its reason under it when it is off (a tooltip alone is easy to miss).
        private static VisualElement ActionButton(string text, DiplomacyButton state, Action onPress)
        {
            var column = new VisualElement { pickingMode = PickingMode.Ignore };
            column.style.width = 190;
            column.style.marginRight = 8;
            var button = HumanUiKit.Btn(text, () =>
            {
                onPress?.Invoke();
                Refresh();
            });
            button.SetEnabled(state.Enabled && onPress != null);
            button.tooltip = state.Reason ?? "";
            column.Add(button);
            if (!state.Enabled && !string.IsNullOrEmpty(state.Reason))
                column.Add(HumanUiKit.Text(state.Reason, HumanUiKit.Muted, size: 12));
            return column;
        }

        private static VisualElement BuildConfirm(RivalView view)
        {
            var box = new VisualElement { pickingMode = PickingMode.Ignore };
            box.Add(HumanUiKit.Text(ConfirmText(_pending, view.Label), HumanUiKit.Warn));
            var row = HumanUiKit.Row();
            var type = _pending;
            row.Add(HumanUiKit.Btn("Confirm", () => Send(type, view.Rival)));
            row.Add(HumanUiKit.Btn("Cancel", () =>
            {
                _pending = null;
                Refresh();
            }));
            box.Add(row);
            return box;
        }

        private static string ConfirmText(string type, string label)
        {
            switch (type)
            {
                case DeclareWar: return $"Declare war on {label}?";
                case EndWar: return $"Set your stance toward {label} to Peace? They may still be at war with you.";
                default:
                    return $"Surrender to {label}? Both sides go to Peace and a truce of {Gameboard.Instance.SurrenderTruceTurns} turns starts: " +
                           "nobody can declare war. Nothing else changes.";
            }
        }

        /// <summary>Puts the stored reply (or the empty-state text) into the response box, if the screen is open.</summary>
        private static void ShowReply()
        {
            if (_replyLabel == null) return;
            if (string.IsNullOrEmpty(_replyText))
            {
                _replyLabel.text = "No messages yet";
                _replyLabel.style.color = new StyleColor(HumanUiKit.Muted);
            }
            else
            {
                _replyLabel.text = $"{_replyFrom}: \"{_replyText}\"";
                _replyLabel.style.color = new StyleColor(Color.white);
            }
        }

        /// <summary>Asks the chat model for the rival's reply to a message; the answer lands in the response box.</summary>
        private static void RequestReply(string message, int rival)
        {
            var human = Gameboard.Instance.HumanPlayerId;
            if (human < 0) return;
            // The rival's own view of the human: its stance and hostility toward them.
            var status = Gameboard.Instance.GameAI.GameAIMap.Diplomacy.Get(rival, human);
            var from = "Player " + rival;
            ChatClient.Instance.RequestChatResponse(message, from, "Player " + human, status, reply =>
            {
                _replyText = reply;
                _replyFrom = from;
                ShowReply();
            });
        }

        private static void Send(string type, int rival)
        {
            RequestReply(type, rival);

            _pending = null;
            var results = Gameboard.Instance.SubmitHumanCommands(new[] { new HumanCommand { type = type, rival = rival } }, "Diplomacy");
            var refused = HumanUiKit.RefusalSummary(results);
            if (!string.IsNullOrEmpty(refused)) HumanUiKit.Toast(_host, refused);
            Refresh();
        }
    }
}
