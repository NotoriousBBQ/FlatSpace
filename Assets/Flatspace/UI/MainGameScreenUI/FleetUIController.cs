using System.Collections.Generic;
using Flatspace.Objects.Production;
using FlatSpace.AI;
using FlatSpace.Game;
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
        if (_planet == null) return;

        // Null until a player's AI exists (first turn of a repeat run): rows then fall back to base stats.
        var research = Gameboard.Instance != null
            ? BlockadeSystem.ResearchItemsFrom(Gameboard.Instance.players)
            : null;
        foreach (var ship in _planet.DockedShips)
        {
            if (ship.Owner != _owner) continue;
            var row = new Label(FormatShipRow(ship, research))
            {
                pickingMode = PickingMode.Ignore
            };
            _shipListContainer.Add(row);
        }
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
        if (ship.Kind != Ship.ShipKind.WarShip || template == null) return label;

        var stats = new WarshipStats(research);
        // A damaged ship shows current/maximum health and effective/base offense; an undamaged one shows the plain numbers.
        var maxHp = stats.Health(template, ship.ResearchSnapshot);
        var baseOff = stats.Offense(template, ship.ResearchSnapshot);
        var hpText = ship.Damage > 0f ? $"{stats.CurrentHealth(ship):0.#}/{maxHp:0.#}" : $"{maxHp:0.#}";
        var offText = ship.Damage > 0f ? $"{stats.EffectiveOffense(ship):0.#}/{baseOff:0.#}" : $"{baseOff:0.#}";
        return $"{label} (Spd {stats.Speed(template):0.#}, " +
               $"HP {hpText}, " +
               $"Off {offText}, " +
               $"Def {stats.Defense(template, ship.ResearchSnapshot):0.#})";
    }

    // Same bounds-test pattern as PlanetDetailUIController.ContainsScreenPoint.
    public bool ContainsScreenPoint(Vector2 screenPosition)
    {
        if (_panelElement == null || _panelElement.panel == null) return false;
        var panelPosition = RuntimePanelUtils.ScreenToPanel(_panelElement.panel, screenPosition);
        return _panelElement.worldBound.Contains(panelPosition);
    }
}
