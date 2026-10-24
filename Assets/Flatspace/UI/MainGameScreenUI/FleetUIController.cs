using System.Collections.Generic;
using Flatspace.Objects.Production;
using FlatSpace.AI;
using FlatSpace.Game;
using FlatSpace.Human;
using Game.UI.MainGameScreenUI;
using UnityEngine;
using UnityEngine.UIElements;

public class FleetUIController : MonoBehaviour
{
    public UIDocument uiDocument;

    private VisualElement _element;
    private VisualElement _panelElement;
    private VisualElement _shipListContainer;
    private Planet _planet;
    private int _owner;

    private void OnEnable()
    {
        if (_element == null) return;
        _element.SetEnabled(true);
        _element.visible = true;
        _element.pickingMode = PickingMode.Ignore;
    }

    private void OnDisable()
    {
        if (_element == null) return;
        _element.SetEnabled(false);
        _element.visible = false;
        _element.pickingMode = PickingMode.Ignore;
    }

    public void Awake()
    {
        _element = uiDocument.rootVisualElement;
        _panelElement = _element.Q<VisualElement>("FleetUIElement");
        _shipListContainer = _element.Q<VisualElement>("ShipListContainer");
        enabled = false;
    }

    // Lists only `owner`'s ships docked at the planet (the fleet icon that was clicked).
    public void SetPlanet(Planet planet, int owner)
    {
        _planet = planet;
        _owner = owner;
        RefreshShipList();
    }

    public void RefreshShipList()
    {
        if (_shipListContainer == null) return;
        _shipListContainer.Clear();
        _boxes.Clear();
        if (_planet == null) return;

        // Null until a player's AI exists (first turn of a repeat run): rows then fall back to base stats.
        var research = Gameboard.Instance != null
            ? BlockadeSystem.ResearchItemsFrom(Gameboard.Instance.players)
            : null;
        // The human's own ships are shown as fleet boxes they can reorganise by dragging; everyone else's stay a read-only list.
        if (Gameboard.Instance != null && Gameboard.Instance.HumanPlayerId >= 0 && _owner == Gameboard.Instance.HumanPlayerId)
        {
            BuildFleetBoxes(research);
            return;
        }
        foreach (var ship in _planet.DockedShips)
        {
            if (ship.Owner != _owner || !Ship.CountsAsShip(ship.Kind)) continue;
            var row = new Label(FormatShipRow(ship, research))
            {
                pickingMode = PickingMode.Ignore
            };
            // A long row (a damaged ship's current/max values, the Bomb figure) wraps instead of running under the scroll bar.
            row.style.whiteSpace = WhiteSpace.Normal;
            row.style.paddingRight = 12;
            _shipListContainer.Add(row);
        }
    }

    // The human's fleet boxes: drag a ship row to another box (or "+ New fleet") to move it, a box title onto another box to merge.

    private const float DragThresholdPixels = 6f;
    private readonly List<(VisualElement box, int fleetId)> _boxes = new List<(VisualElement, int)>();
    private Label _ghost;

    private void BuildFleetBoxes(IEnumerable<CatalogItem> research)
    {
        var row = new VisualElement { pickingMode = PickingMode.Ignore };
        row.style.flexDirection = FlexDirection.Row;
        row.style.flexWrap = Wrap.Wrap;
        _shipListContainer.Add(row);

        foreach (var fleet in HumanFleetView.Fleets(_planet, _owner))
        {
            var box = NewBox();
            var title = new Label($"Fleet {fleet.FleetId + 1} ({fleet.Ships.Count})") { pickingMode = PickingMode.Position };
            title.style.unityFontStyleAndWeight = FontStyle.Bold;
            box.Add(title);
            var fleetId = fleet.FleetId;
            MakeDraggable(title, title.text, target => target >= 0 && target != fleetId
                ? new HumanCommand { type = "mergeFleets", origin = _planet.PlanetName, index = fleetId, toIndex = target }
                : null);
            foreach (var info in fleet.Ships)
            {
                var ship = _planet.DockedShips[info.DockIndex];
                var shipRow = new Label(FormatShipRow(ship, research)) { pickingMode = PickingMode.Position };
                shipRow.style.whiteSpace = WhiteSpace.Normal;
                shipRow.style.fontSize = 12;
                var dockIndex = info.DockIndex;
                MakeDraggable(shipRow, ship.Kind.ToString(), target => target != fleetId
                    ? new HumanCommand { type = "setFleet", origin = _planet.PlanetName, index = target, ships = new List<int> { dockIndex } }
                    : null);
                box.Add(shipRow);
            }
            row.Add(box);
            _boxes.Add((box, fleetId));
        }

        var newBox = NewBox();
        newBox.style.borderTopColor = newBox.style.borderBottomColor = newBox.style.borderLeftColor = newBox.style.borderRightColor = new StyleColor(Color.gray);
        newBox.Add(new Label("+ New fleet") { pickingMode = PickingMode.Ignore });
        newBox.Add(new Label("drop ships here") { pickingMode = PickingMode.Ignore });
        row.Add(newBox);
        _boxes.Add((newBox, -1));
    }

    private static VisualElement NewBox()
    {
        var box = new VisualElement { pickingMode = PickingMode.Position };
        box.style.width = 170;
        box.style.minHeight = 70;
        box.style.marginRight = 6;
        box.style.marginBottom = 6;
        box.style.paddingLeft = box.style.paddingRight = box.style.paddingTop = box.style.paddingBottom = 4;
        box.style.borderTopWidth = box.style.borderBottomWidth = box.style.borderLeftWidth = box.style.borderRightWidth = 1;
        box.style.borderTopColor = box.style.borderBottomColor = box.style.borderLeftColor = box.style.borderRightColor = new StyleColor(Color.white);
        return box;
    }

    // Arms a press-drag-release on `source`: past the threshold a ghost label follows the pointer, and on release `commandFor` turns the fleet
    // id of the box under the pointer (-1 for the "+ New fleet" box) into the command to send, or null for none. A drop outside every box cancels.
    private void MakeDraggable(VisualElement source, string ghostText, System.Func<int, HumanCommand> commandFor)
    {
        var pressed = false;
        var dragging = false;
        var start = Vector2.zero;
        source.RegisterCallback<PointerDownEvent>(evt =>
        {
            if (evt.button != 0) return;
            pressed = true;
            dragging = false;
            start = evt.position;
            source.CapturePointer(evt.pointerId);
        });
        source.RegisterCallback<PointerMoveEvent>(evt =>
        {
            if (!pressed) return;
            if (!dragging && Vector2.Distance(evt.position, start) < DragThresholdPixels) return;
            if (!dragging)
            {
                dragging = true;
                _ghost = new Label(ghostText) { pickingMode = PickingMode.Ignore };
                _ghost.style.position = Position.Absolute;
                _ghost.style.backgroundColor = new StyleColor(new Color(0.2f, 0.2f, 0.2f, 0.9f));
                _ghost.style.color = new StyleColor(Color.white);
                _ghost.style.paddingLeft = _ghost.style.paddingRight = 6;
                _element.Add(_ghost);
            }
            var local = _element.WorldToLocal(evt.position);
            _ghost.style.left = local.x + 8;
            _ghost.style.top = local.y + 8;
        });
        source.RegisterCallback<PointerUpEvent>(evt =>
        {
            if (!pressed) return;
            pressed = false;
            source.ReleasePointer(evt.pointerId);
            if (_ghost != null)
            {
                _ghost.RemoveFromHierarchy();
                _ghost = null;
            }
            if (!dragging) return;
            dragging = false;
            foreach (var (box, fleetId) in _boxes)
            {
                if (!box.worldBound.Contains(evt.position)) continue;
                var command = commandFor(fleetId);
                if (command != null) SubmitFleetCommand(command);
                return;
            }
        });
    }

    private void SubmitFleetCommand(HumanCommand command)
    {
        // The panel refreshes itself: SubmitHumanCommands redraws the board and any open planet or fleet panel.
        var results = Gameboard.Instance.SubmitHumanCommands(new[] { command }, "Fleets");
        if (results.Count > 0 && !results[0].applied)
            HumanUiKit.Toast(_element, HumanHoverText.RefusalSentence(results[0].reason));
    }

    /// <summary>
    /// One panel row. A warship shows its EFFECTIVE stats (template plus the research it carries, via WarshipStats), so an
    /// upgraded ship reads differently from a fresh one; a colony ship, or a ship with no template, has no combat stats to
    /// show. Pure, so a self-check can call it without a UI.
    /// </summary>
    public static string FormatShipRow(Ship ship, IEnumerable<CatalogItem> research)
    {
        var template = ship.Template;
        var label = template == null || string.IsNullOrEmpty(template.shipName)
            ? ship.Kind.ToString()
            : $"{ship.Kind} - {template.shipName}";
        if (ship.Kind == Ship.ShipKind.ColonyShip || template == null) return label;

        var stats = new WarshipStats(research);
        // A damaged ship shows current/maximum health and effective/base offense; an undamaged one shows the plain numbers.
        // Stats come from the ship's own subtype (Warship, Siegeship, Bunker), so each reads its own research lines.
        var maxHp = stats.Health(ship);
        var baseOff = stats.Offense(ship);
        var hpText = ship.Damage > 0f ? $"{stats.CurrentHealth(ship):0.#}/{maxHp:0.#}" : $"{maxHp:0.#}";
        var offText = ship.Damage > 0f ? $"{stats.EffectiveOffense(ship):0.#}/{baseOff:0.#}" : $"{baseOff:0.#}";
        var row = $"{label} (Spd {stats.Speed(template):0.#}, " +
                  $"HP {hpText}, " +
                  $"Off {offText}, " +
                  $"Def {stats.Defense(ship):0.#}";
        // Bombard is the siegeship's and the bunker's stat; a warship row is unchanged (its bombard is a minor side stat).
        if (ship.Kind == Ship.ShipKind.SiegeShip || ship.Kind == Ship.ShipKind.Bunker)
        {
            var maxBombard = stats.Bombard(template, ship.ResearchSnapshot, WarshipStats.SubtypeOf(ship.Kind));
            var bombardText = ship.Damage > 0f ? $"{stats.EffectiveBombard(ship):0.#}/{maxBombard:0.#}" : $"{maxBombard:0.#}";
            row += $", Bomb {bombardText}";
        }
        return row + ")";
    }

    // Same bounds-test pattern as PlanetDetailUIController.ContainsScreenPoint.
    public bool ContainsScreenPoint(Vector2 screenPosition)
    {
        if (_panelElement == null || _panelElement.panel == null) return false;
        var panelPosition = RuntimePanelUtils.ScreenToPanel(_panelElement.panel, screenPosition);
        return _panelElement.worldBound.Contains(panelPosition);
    }
}
