using System.Collections.Generic;
using System.Linq;
using FlatSpace.Game;
using FlatSpace.Human;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// Pointer behaviour of one fleet icon on a planet marker: a hover popup listing the fleet's ships (any owner's icon), and for the human's
/// own fleets a drag that sends the whole fleet to the planet it is released on. A press that does not become a drag still reaches
/// PlanetUIObject.OnPointerClick (Unity sends no click after a drag), so a click opens the fleet panel as before. An icon that cannot be
/// dragged hands its drag to the board's ScrollRect, so it never blocks scrolling.
/// </summary>
public class FleetIconHandle : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IInitializePotentialDragHandler,
    IBeginDragHandler, IDragHandler, IEndDragHandler
{
    // Set by PlanetUIObject.UIUpdate each time the icon is (re)assigned to a group.
    public string PlanetName;
    public int Owner;
    public int FleetId;

    private ScrollRect _scrollRect;
    private bool _dragging;
    private bool _hovering;

    public bool CanDrag => Draggable;

    private bool Draggable => Gameboard.Instance != null && Gameboard.Instance.HumanPlayerId >= 0 && Owner == Gameboard.Instance.HumanPlayerId;

    private ScrollRect Scroll => _scrollRect != null ? _scrollRect : _scrollRect = GetComponentInParent<ScrollRect>();

    /// <summary>"Fleet 2: 3 WarShip, 1 SiegeShip" for the human's own fleet, "Player 1: 3 WarShip" for a rival's.</summary>
    private string PopupText()
    {
        var planet = Gameboard.Instance.GetPlanet(PlanetName);
        if (planet == null) return null;
        var human = Gameboard.Instance.HumanPlayerId;
        var kinds = planet.DockedShips
            .Where(s => s.Owner == Owner && Ship.CountsAsShip(s.Kind) && (Owner != human || s.FleetId == FleetId))
            .Select(s => s.Kind);
        var label = Owner == human ? $"Fleet {FleetId + 1}" : Owner < 0 ? "Unowned" : $"Player {Owner}";
        return $"{label}: {HumanHoverText.Ships(kinds)}";
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        var text = PopupText();
        _hovering = true;
        if (text != null && Gameboard.Instance != null) Gameboard.Instance.ShowIconHover(eventData.position, new List<string> { text });
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        if (_dragging) return;
        _hovering = false;
        if (Gameboard.Instance != null) Gameboard.Instance.HideIconHover();
    }

    // An icon that goes away under the pointer (its fleet left) never gets an exit event; without this the popup would stay up.
    private void OnDisable()
    {
        // An icon that disappears mid-drag (its fleet was lost or left) never sends the end of the drag: the drag line would stay on the board.
        if (_dragging && Gameboard.Instance != null) Gameboard.Instance.CancelFleetDrag();
        if (!_hovering) return;
        _hovering = false;
        _dragging = false;
        if (Gameboard.Instance != null) Gameboard.Instance.HideIconHover();
    }

    // Answering this stops the ScrollRect above from also arming itself for a drag that this icon takes.
    public void OnInitializePotentialDrag(PointerEventData eventData)
    {
        if (!Draggable && Scroll != null) Scroll.OnInitializePotentialDrag(eventData);
    }

    public void OnBeginDrag(PointerEventData eventData)
    {
        if (!Draggable)
        {
            if (Scroll != null) Scroll.OnBeginDrag(eventData);
            return;
        }
        _dragging = true;
        Gameboard.Instance.BeginFleetDrag(PlanetName, FleetId);
    }

    public void OnDrag(PointerEventData eventData)
    {
        if (!_dragging)
        {
            if (!Draggable && Scroll != null) Scroll.OnDrag(eventData);
            return;
        }
        Gameboard.Instance.UpdateFleetDrag(eventData.position);
    }

    public void OnEndDrag(PointerEventData eventData)
    {
        if (!_dragging)
        {
            if (!Draggable && Scroll != null) Scroll.OnEndDrag(eventData);
            return;
        }
        _dragging = false;
        Gameboard.Instance.EndFleetDrag(eventData.position);
    }
}
