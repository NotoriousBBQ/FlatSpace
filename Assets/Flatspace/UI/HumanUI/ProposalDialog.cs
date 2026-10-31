using System.Collections.Generic;
using System.Linq;
using FlatSpace.AI;
using FlatSpace.Game;
using FlatSpace.Human;
using UnityEngine;
using UnityEngine.UIElements;

namespace Game.UI.MainGameScreenUI
{
    /// <summary>
    /// The proposal dialog opened from the Diplomacy screen (docs/superpowers/specs/2026-10-30-ask-and-trade-design.md, "The proposal"; mockup
    /// proposal-dialog-b-v2): "I give" and "I get" lists with an Add menu on the left, "How they see it" (the acceptance meter, green and red
    /// factor lines, the rival's reply) on the right. Every change re-previews the deal through Gameboard.PreviewDeal; Propose sends one `propose`
    /// command. The dialog is a child of the Diplomacy screen's host, so it shares that root and z-order. Words come from the pure ProposalText.
    /// </summary>
    public static class ProposalDialog
    {
        private static readonly Color[] BandColors =
        {
            new Color(0.6f, 0.2f, 0.2f), new Color(0.65f, 0.4f, 0.2f), new Color(0.67f, 0.67f, 0.2f), new Color(0.47f, 0.67f, 0.2f), new Color(0.2f, 0.67f, 0.2f),
        };

        private static VisualElement _root;
        private static VisualElement _host;
        private static int _rival;
        private static readonly List<DealItem> Give = new List<DealItem>();
        private static readonly List<DealItem> Get = new List<DealItem>();
        private static bool _menuForGive;        // which list's Add menu is open
        private static bool _menuOpen;
        private static string _menuCategory;     // null = the category buttons, else planet / fleet / tech
        private static Label _replyLabel;

        public static bool IsOpen => _root != null && _root.parent != null;

        /// <summary>Opens the dialog for a rival over the host (the Diplomacy screen's root); an open one is rebuilt empty.</summary>
        public static void Show(VisualElement host, int rival)
        {
            Close();
            if (host == null || Gameboard.Instance == null || Gameboard.Instance.HumanPlayerId < 0) return;
            _host = host;
            _rival = rival;
            Give.Clear();
            Get.Clear();
            _menuOpen = false;
            _menuCategory = null;

            _root = HumanUiKit.Panel();
            _root.style.position = Position.Absolute;
            _root.style.left = Length.Percent(6);
            _root.style.right = Length.Percent(6);
            _root.style.top = Length.Percent(12);
            _root.style.bottom = Length.Percent(6);
            _root.style.maxWidth = 900;
            host.Add(_root);
            Redraw();
        }

        public static void Close()
        {
            _root?.RemoveFromHierarchy();
            _root = null;
            _host = null;
            _replyLabel = null;
        }

        /// <summary>
        /// Called by the Diplomacy screen's Refresh: closes the dialog when the picked rival is no longer the one it was opened for, else redraws it
        /// (the items stay; the deal is re-previewed, so the meter follows the new turn).
        /// </summary>
        internal static void Sync(int selectedRival)
        {
            if (!IsOpen) return;
            if (selectedRival != _rival) Close();
            else Redraw();
        }

        /// <summary>Whether a screen point is on the dialog: a press there is not a click outside the planet or fleet panel.</summary>
        public static bool ContainsScreenPoint(Vector2 screenPosition)
        {
            if (!IsOpen || _root.panel == null) return false;
            return _root.worldBound.Contains(RuntimePanelUtils.ScreenToPanel(_root.panel, screenPosition));
        }

        /// <summary>The Diplomacy screen's response box changed: the dialog shows the same line.</summary>
        internal static void ReplyChanged(string line)
        {
            if (_replyLabel != null) _replyLabel.text = line ?? "";
        }

        private static void Redraw()
        {
            if (!IsOpen || Gameboard.Instance == null) return;
            _root.Clear();
            _root.Add(HumanUiKit.Header("Proposal to " + HumanDiplomacyView.Label(_rival)));

            var body = HumanUiKit.Row();
            body.style.flexGrow = 1;
            body.style.alignItems = Align.Stretch;
            body.style.minHeight = 0;

            var left = new ScrollView(ScrollViewMode.Vertical) { pickingMode = PickingMode.Position };
            left.horizontalScrollerVisibility = ScrollerVisibility.Hidden;
            left.style.flexGrow = 2;
            left.style.flexBasis = 0;
            var verdict = Gameboard.Instance.PreviewDeal(_rival, new List<DealItem>(Give), new List<DealItem>(Get));
            BuildList(left, "I give", Give, true);
            BuildList(left, "I get", Get, false);
            left.Add(BuildButtons(verdict));
            body.Add(left);

            var right = new ScrollView(ScrollViewMode.Vertical) { pickingMode = PickingMode.Position };
            right.horizontalScrollerVisibility = ScrollerVisibility.Hidden;
            right.style.flexGrow = 1.2f;
            right.style.flexBasis = 0;
            right.style.marginLeft = 8;
            right.style.paddingLeft = 8;
            right.style.borderLeftWidth = 1;
            right.style.borderLeftColor = new StyleColor(HumanUiKit.Muted);
            BuildVerdict(right, verdict);
            body.Add(right);

            _root.Add(body);
        }

        // ---- the two lists and the Add menu -------------------------------------------------------------------------------------------

        private static void BuildList(VisualElement parent, string title, List<DealItem> items, bool isGive)
        {
            var heading = HumanUiKit.Text(title, HumanUiKit.Warn, bold: true);
            heading.style.marginTop = 8;
            parent.Add(heading);

            var box = new VisualElement { pickingMode = PickingMode.Ignore };
            box.style.backgroundColor = new StyleColor(new Color(0.06f, 0.06f, 0.1f));
            box.style.borderTopWidth = box.style.borderRightWidth = box.style.borderBottomWidth = box.style.borderLeftWidth = 1;
            box.style.borderTopColor = box.style.borderRightColor = box.style.borderBottomColor = box.style.borderLeftColor = new StyleColor(HumanUiKit.Muted);
            box.style.paddingTop = box.style.paddingBottom = 4;
            box.style.paddingLeft = box.style.paddingRight = 6;
            box.style.minHeight = 44;

            if (items.Count == 0) box.Add(HumanUiKit.Text("Nothing yet.", HumanUiKit.Muted, size: 12));
            foreach (var item in items.ToList())
            {
                var row = HumanUiKit.Row();
                row.style.justifyContent = Justify.SpaceBetween;
                row.style.backgroundColor = new StyleColor(new Color(0.11f, 0.11f, 0.16f));
                row.style.paddingLeft = 6;
                row.Add(HumanUiKit.Text(ProposalText.RowText(item, isGive)));
                var captured = item;
                row.Add(HumanUiKit.Btn("x", () => Remove(captured)));
                box.Add(row);
            }

            var add = HumanUiKit.Btn(_menuOpen && _menuForGive == isGive ? "Add (close)" : "+ Add", () =>
            {
                var closing = _menuOpen && _menuForGive == isGive;
                _menuOpen = !closing;
                _menuForGive = isGive;
                _menuCategory = null;
                Redraw();
            });
            add.style.alignSelf = Align.FlexStart;
            add.style.marginTop = 4;
            box.Add(add);
            if (_menuOpen && _menuForGive == isGive) box.Add(BuildMenu(isGive));
            parent.Add(box);
        }

        private static VisualElement BuildMenu(bool isGive)
        {
            var menu = new VisualElement { pickingMode = PickingMode.Ignore };
            menu.style.backgroundColor = new StyleColor(new Color(0.125f, 0.13f, 0.2f));
            menu.style.paddingTop = menu.style.paddingBottom = 4;
            menu.style.paddingLeft = menu.style.paddingRight = 6;

            if (_menuCategory == null)
            {
                menu.Add(CategoryButton("Planet...", "planet"));
                menu.Add(CategoryButton("Fleet...", "fleet"));
                menu.Add(CategoryButton("Tech...", "tech"));
                // At war: peace as a linked pair, one row in each list, removed together.
                if (AtWar() && !Give.Exists(i => i.kind == "endWar"))
                    menu.Add(HumanUiKit.Btn("End the war with them", () =>
                    {
                        Give.Add(new DealItem { kind = "endWar" });
                        Get.Add(new DealItem { kind = "endWar" });
                        CloseMenu();
                    }));
                return menu;
            }

            var options = Options(isGive, _menuCategory);
            menu.Add(HumanUiKit.Btn("< Back", () =>
            {
                _menuCategory = null;
                Redraw();
            }));
            if (options.Count == 0)
                menu.Add(HumanUiKit.Text("Nothing to add.", HumanUiKit.Muted, size: 12));
            var list = new ScrollView(ScrollViewMode.Vertical) { pickingMode = PickingMode.Position };
            list.horizontalScrollerVisibility = ScrollerVisibility.Hidden;
            list.style.maxHeight = 160;
            foreach (var option in options)
            {
                var item = option.Item;
                list.Add(HumanUiKit.Btn(option.Label, () =>
                {
                    (isGive ? Give : Get).Add(item);
                    CloseMenu();
                }));
            }
            menu.Add(list);
            return menu;
        }

        private static Button CategoryButton(string text, string category)
            => HumanUiKit.Btn(text, () =>
            {
                _menuCategory = category;
                Redraw();
            });

        private static void CloseMenu()
        {
            _menuOpen = false;
            _menuCategory = null;
            Redraw();
        }

        private static void Remove(DealItem item)
        {
            if (item.kind == "endWar")
            {
                // The linked pair goes together.
                Give.RemoveAll(i => i.kind == "endWar");
                Get.RemoveAll(i => i.kind == "endWar");
            }
            else
            {
                Give.Remove(item);
                Get.Remove(item);
            }
            Redraw();
        }

        private static bool AtWar()
        {
            var board = Gameboard.Instance;
            return board.GameAI.GameAIMap.Diplomacy.IsAtWar(board.HumanPlayerId, _rival, true);
        }

        private struct MenuOption
        {
            public string Label;
            public DealItem Item;
        }

        // Whether the item is already on the side (list) the option would go into; the other side holds other owners' things.
        private static bool InDeal(DealItem item, bool isGive) => (isGive ? Give : Get).Exists(i => ProposalText.SameItem(i, item));

        /// <summary>What the Add menu offers: the human's own things on the give side, the rival's the human may see on the get side. Items already in the deal are left out.</summary>
        private static List<MenuOption> Options(bool isGive, string category)
        {
            var board = Gameboard.Instance;
            var human = board.HumanPlayerId;
            var map = board.GameAI.GameAIMap;
            var owner = isGive ? human : _rival;
            var options = new List<MenuOption>();

            switch (category)
            {
                case "planet":
                    var ownerVisible = isGive ? null : board.HumanOwnerVisible();
                    var capital = board.GetPlayerCapitol(owner)?.PlanetName;
                    foreach (var planet in map.PlanetList.OrderBy(p => p.PlanetName))
                    {
                        if (isGive)
                        {
                            if (planet.Owner != owner) continue;
                        }
                        else
                        {
                            // Never read the live owner of a planet out of sight: its last-known owner decides (the Planets screen's rule).
                            if (!board.HumanHasSeenPlanet(planet.PlanetName)) continue;
                            var known = Planet.NoOwner;
                            if (ownerVisible(planet.PlanetName)) known = planet.Owner;
                            else if (board.PlanetSights.TryGet(planet.PlanetName, out var sighting)) known = sighting.Owner;
                            if (known != owner) continue;
                        }
                        if (planet.PlanetName == capital) continue;   // a capital cannot be given
                        var item = new DealItem { kind = "planet", name = planet.PlanetName };
                        if (!InDeal(item, isGive)) options.Add(new MenuOption { Label = planet.PlanetName, Item = item });
                    }
                    break;

                case "fleet":
                    // The rival's ships only as a count per kind, and only on the planets whose ships the board shows.
                    var visible = isGive ? null : board.HumanOwnerVisible();
                    foreach (var planet in map.PlanetList.OrderBy(p => p.PlanetName))
                    {
                        if (!isGive && !visible(planet.PlanetName)) continue;
                        var perKind = HumanFleetView.Docked(planet, owner).Where(s => Ship.CountsAsShip(s.Kind)).GroupBy(s => s.Kind);
                        foreach (var group in perKind.Where(g => Ship.IsFightingKind(g.Key)))   // colony ships and bunkers are not tradable
                        {
                            var count = group.Count();
                            var kind = group.Key.ToString();
                            if (InDeal(new DealItem { kind = "fleet", name = kind, planet = planet.PlanetName }, isGive)) continue;
                            options.Add(new MenuOption
                            {
                                Label = count > 1 ? $"All {count} {kind} at {planet.PlanetName}" : $"1 {kind} at {planet.PlanetName}",
                                Item = new DealItem { kind = "fleet", name = kind, planet = planet.PlanetName, count = count },
                            });
                            if (count > 1)
                                options.Add(new MenuOption
                                {
                                    Label = $"1 {kind} at {planet.PlanetName}",
                                    Item = new DealItem { kind = "fleet", name = kind, planet = planet.PlanetName, count = 1 },
                                });
                        }
                    }
                    break;

                case "tech":
                    // A tech the giver has researched and the receiver lacks; a deep one brings its missing lower tiers along.
                    var giverAi = board.players[owner].playerAI;
                    var receiverAi = board.players[isGive ? _rival : human].playerAI;
                    var catalog = giverAi.ResearchCatalog != null ? giverAi.ResearchCatalog.catalogItems : null;
                    if (catalog == null) break;
                    foreach (var tech in catalog.Where(c => c.researched).OrderBy(c => c.itemName))
                    {
                        var chain = receiverAi.TechChain(tech.itemName);
                        if (chain.Count == 0) continue;
                        var item = new DealItem { kind = "tech", name = tech.itemName };
                        if (InDeal(item, isGive)) continue;
                        options.Add(new MenuOption
                        {
                            Label = chain.Count > 1 ? $"{tech.itemName} (+{chain.Count - 1} lower tiers)" : tech.itemName,
                            Item = item,
                        });
                    }
                    break;
            }
            return options;
        }

        // ---- the verdict side --------------------------------------------------------------------------------------------------------

        private static void BuildVerdict(VisualElement parent, DealVerdict verdict)
        {
            parent.Add(HumanUiKit.Text("How they see it", HumanUiKit.Warn, bold: true));
            var judged = string.IsNullOrEmpty(verdict.Refusal);

            var meter = HumanUiKit.Row();
            meter.style.height = 14;
            meter.style.marginTop = 4;
            var marker = HumanUiKit.Row();
            for (var i = 0; i < BandColors.Length; i++)
            {
                var active = judged && i == (int)verdict.Band;
                var segment = new VisualElement { pickingMode = PickingMode.Ignore };
                segment.style.flexGrow = 1;
                segment.style.flexBasis = 0;
                segment.style.height = 14;
                segment.style.marginRight = 1;
                var color = BandColors[i];
                color.a = active ? 1f : 0.35f;
                segment.style.backgroundColor = new StyleColor(color);
                meter.Add(segment);

                var slot = HumanUiKit.Text(active ? "^" : "", size: 12);
                slot.style.flexGrow = 1;
                slot.style.flexBasis = 0;
                slot.style.unityTextAlign = TextAnchor.MiddleCenter;
                marker.Add(slot);
            }
            parent.Add(meter);
            parent.Add(marker);

            if (!judged)
            {
                var empty = Give.Count + Get.Count == 0;
                parent.Add(HumanUiKit.Text(empty ? "Add something to the deal." : ProposalText.RefusalSentence(verdict.Refusal), HumanUiKit.Muted, size: 12));
            }
            else
            {
                parent.Add(HumanUiKit.Text(ProposalText.BandLabel(verdict.Band), bold: true, size: 13));
                var factors = new VisualElement { pickingMode = PickingMode.Ignore };
                factors.style.marginTop = 6;
                // Text only: green makes the deal more acceptable, red less. No numbers.
                foreach (var factor in verdict.Factors)
                    factors.Add(HumanUiKit.Text(factor.Text, factor.Helps ? HumanUiKit.Good : HumanUiKit.Bad, size: 12));
                parent.Add(factors);
            }

            var reply = HumanUiKit.Panel();
            reply.style.marginTop = 8;
            _replyLabel = HumanUiKit.Text(DiplomacyScreen.ReplyLine, new Color(0.8f, 1f, 0.9f), size: 13);
            _replyLabel.style.unityFontStyleAndWeight = FontStyle.Italic;
            reply.Add(_replyLabel);
            parent.Add(reply);
        }

        // ---- Propose and Cancel ------------------------------------------------------------------------------------------------------

        private static VisualElement BuildButtons(DealVerdict verdict)
        {
            var row = HumanUiKit.Row();
            row.style.marginTop = 8;
            var propose = HumanUiKit.Btn("Propose", Propose);
            var blocked = !string.IsNullOrEmpty(verdict.Refusal) || Give.Count + Get.Count == 0;
            propose.SetEnabled(!blocked);
            row.Add(propose);
            row.Add(HumanUiKit.Btn("Cancel", Close));
            return row;
        }

        private static void Propose()
        {
            var board = Gameboard.Instance;
            if (!IsOpen || board == null) return;
            var rival = _rival;
            var host = _host;
            var give = new List<DealItem>(Give);
            var get = new List<DealItem>(Get);
            var command = new HumanCommand { type = "propose", rival = rival, dealGive = give, dealGet = get };
            var results = board.SubmitHumanCommands(new[] { command }, "Proposal");

            var refused = HumanUiKit.RefusalSummary(results);
            if (!string.IsNullOrEmpty(refused))
            {
                HumanUiKit.Toast(host, refused);   // the dialog stays so the deal can be mended
                return;
            }

            var applied = results.Find(r => r.type == "propose" && r.applied);
            var accepted = applied != null && applied.detail != null && applied.detail.StartsWith("accepted");
            if (applied == null) return;

            var human = "Player " + board.HumanPlayerId;
            var pair = board.GameAI.GameAIMap.Diplomacy.Get(rival, board.HumanPlayerId);
            var tone = (int)ChatClient.ToneOf(pair);
            var template = ProposalText.TemplateReply(accepted, tone, Random.Range(0, 1000));

            if (accepted) Close();
            // The template answers at once; the model's line replaces it when it arrives.
            DiplomacyScreen.SetReply(template, rival);
            DiplomacyScreen.RequestReply(ProposalText.OutcomeMessage(human, accepted, give, get), rival);
            if (accepted) DiplomacyScreen.Refresh();   // stances and a truce may have changed
            else Redraw();
        }
    }
}
