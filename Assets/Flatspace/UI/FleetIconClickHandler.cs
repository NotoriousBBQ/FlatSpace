using FlatSpace.Game;
using UnityEngine;
using UnityEngine.EventSystems;

public class FleetIconClickHandler : MonoBehaviour, IPointerClickHandler
{
    private PlanetUIObject _owner;

    public void Init(PlanetUIObject owner)
    {
        _owner = owner;
    }

    public void OnPointerClick(PointerEventData eventData)
    {
        Gameboard.Instance.ShowFleetUI(_owner._planetName);
    }
}
