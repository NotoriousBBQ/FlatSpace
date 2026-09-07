using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

public class FleetUIController : MonoBehaviour
{
    public UIDocument uiDocument;

    private VisualElement _element;
    private VisualElement _panelElement;
    private VisualElement _shipListContainer;
    private Planet _planet;

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

    public void SetPlanet(Planet planet)
    {
        _planet = planet;
        RefreshShipList();
    }

    public void RefreshShipList()
    {
        if (_shipListContainer == null) return;
        _shipListContainer.Clear();
        if (_planet == null) return;

        foreach (var ship in _planet.DockedShips)
        {
            var row = new Label(
                $"{ship.Kind} - {ship.Template?.shipName} " +
                $"(Spd {ship.Template?.shipSpeed:0.#}, " +
                $"Off {ship.Template?.shipOffense:0.#}, " +
                $"Def {ship.Template?.shipDefense:0.#})")
            {
                pickingMode = PickingMode.Ignore
            };
            _shipListContainer.Add(row);
        }
    }

    // Same bounds-test pattern as PlanetDetailUIController.ContainsScreenPoint.
    public bool ContainsScreenPoint(Vector2 screenPosition)
    {
        if (_panelElement == null || _panelElement.panel == null) return false;
        var panelPosition = RuntimePanelUtils.ScreenToPanel(_panelElement.panel, screenPosition);
        return _panelElement.worldBound.Contains(panelPosition);
    }
}
