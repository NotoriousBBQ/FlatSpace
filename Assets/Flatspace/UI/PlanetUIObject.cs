using System;
using System.Collections.Generic;
using FlatSpace.AI;
using FlatSpace.Fog;
using FlatSpace.Game;
using Game.UI.MainGameScreenUI;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public class PlanetUIObject : MonoBehaviour, IPointerClickHandler
{
    [SerializeField] public TextMeshProUGUI _nameTextField;
    [SerializeField] public TextMeshProUGUI _populationTextField;
    [SerializeField] public TextMeshProUGUI _foodTextField;
    [SerializeField] public TextMeshProUGUI _grotsitsTextField;
    [SerializeField] public TextMeshProUGUI _moraleTextField;
    [SerializeField] public Canvas _statsCanvas;
    private RectTransform _fleetIconRect;
    private Image _fleetIconBackgroundImage;
    private Image _fleetIconImage;
    private readonly Vector2 _fleetIconBaseAnchoredPosition = new Vector2(40, -40);
    public string _planetName;
    public bool _changeColor = false;
    private Color _fogBasePlanetColor = Color.white;
    private bool _fogBaseCaptured;
    private Dictionary<Planet.PlanetType, Color32> _planetColors = new Dictionary<Planet.PlanetType, Color32>
    {
        { Planet.PlanetType.PlanetTypeDesolate,  new Color32(196, 65,19, 255 )},
        { Planet.PlanetType.PlanetTypeFarm,  new Color32(91, 188,93, 255 )},
        { Planet.PlanetType.PlanetTypeIndustrial , new Color32(205, 133,65, 255 )},
        { Planet.PlanetType.PlanetTypeNormal , new Color32(135, 206,250, 255 )},
        { Planet.PlanetType.PlanetTypePrime , new Color32(173,173,22, 255 )},
        { Planet.PlanetType.PlanetTypeVerdant , new Color32(0,206,0, 255 )},
        { Planet.PlanetType.PlanetTypeOcean, new Color32(0, 255, 255, 255) },
        { Planet.PlanetType.PlanetTypeDesert, new Color32(255, 215, 0, 255) },
    };
    
    public void UIUpdate()
    {
        var planet = Gameboard.Instance.GetPlanet(_planetName);
        if (!planet)
            return;
        _populationTextField.text = planet.Population.Count.ToString();
        _foodTextField.text = Math.Floor(planet.Food).ToString();
        _grotsitsTextField.text = Math.Floor(planet.Grotsits).ToString();
        _moraleTextField.text = Math.Floor(planet.Morale).ToString();
        SetOwnerColor(planet.Owner);
        if (_fleetIconRect)
        {
            if (planet.DockedShips.Count > 0)
            {
                _fleetIconImage.sprite = planet.HasDockedShip(Ship.ShipKind.WarShip) ?
                    planet.GameAIConstants.warShipData.shipIcon : planet.GameAIConstants.colonyShipData.shipIcon;
                _fleetIconBackgroundImage.color = planet.Owner == Planet.NoOwner
                    ? Player.NoPlayerColor : Player.PlayerColors[planet.Owner];
                _fleetIconRect.gameObject.SetActive(true);
            }
            else
            {
                _fleetIconRect.gameObject.SetActive(false);
            }
        }
    }

    public void UIUpdateForScroll(float orthoChange = 0.0f)
    {
        var scaleChange = _statsCanvas.transform.localScale.x + (-orthoChange/5.0f);
        _statsCanvas.transform.localScale = new Vector3(scaleChange, scaleChange, scaleChange);;
        if (_fleetIconRect)
        {
            // Cancel out the stats canvas's counter-scaling for the icon only, so it tracks
            // the planet sprite's on-screen size/position (which is never counter-scaled)
            // instead of staying pinned to a constant screen spot like the readable text labels.
            _fleetIconRect.anchoredPosition = _fleetIconBaseAnchoredPosition / scaleChange;
            _fleetIconRect.localScale = Vector3.one / scaleChange;
        }
    }

    public void SetPlanetColor(Planet.PlanetType planetType)
    {
        if (!_changeColor)
            return;
        var sprintRenderer = GetComponentInChildren<SpriteRenderer>();
        sprintRenderer.color = _planetColors[planetType];

    }

    public void SetOwnerColor(int owner)
    {
        var statsPanelImage = GetComponentInChildren<Image>();
        if (statsPanelImage)
        {
            if (owner == Planet.NoOwner)
                statsPanelImage.color = Player.NoPlayerColor;
            else
                statsPanelImage.color = Player.PlayerColors[owner];
        }
    }

    public void SetFogState(FlatSpace.Fog.FogVisibility state, float exploredDim)
    {
        if (state == FlatSpace.Fog.FogVisibility.Hidden)
        {
            gameObject.SetActive(false);
            return;
        }
        if (!gameObject.activeSelf) gameObject.SetActive(true);

        var spriteRenderer = GetComponentInChildren<SpriteRenderer>();
        var explored = state == FlatSpace.Fog.FogVisibility.Explored;

        // Capture the base color once and always rewrite it from that base, so repeated
        // Visible<->Explored cycles don't compound the dimming and a planet returning to
        // vision brightens back. Capture-once is safe because nothing mutates the sprite
        // color after SetPlanetColor runs at creation.
        if (spriteRenderer)
        {
            if (!_fogBaseCaptured) { _fogBasePlanetColor = spriteRenderer.color; _fogBaseCaptured = true; }
            var d = explored ? exploredDim : 1f;
            spriteRenderer.color = _fogBasePlanetColor * new Color(d, d, d, 1f);
        }

        if (_statsCanvas) _statsCanvas.enabled = !explored;
    }

    public void OnPointerClick(PointerEventData eventData)
    {
        // The fleet icon is a child graphic on the same world-space canvas, so whether the raw
        // click is picked up by the planet's collider (Physics2DRaycaster) or by the icon's
        // Image (GraphicRaycaster) depends on sorting order -- the bubbled click ends here
        // either way. Disambiguate against the icon's live screen rect so it works regardless
        // of zoom level or which raycaster won.
        if (_fleetIconRect && _fleetIconRect.gameObject.activeSelf)
        {
            var iconCamera = _statsCanvas.worldCamera ? _statsCanvas.worldCamera : Camera.main;
            if (RectTransformUtility.RectangleContainsScreenPoint(
                    _fleetIconRect, eventData.position, iconCamera))
            {
                Gameboard.Instance.ShowFleetUI(_planetName);
                return;
            }
        }
        Gameboard.Instance.ShowPlanetDetail(_planetName);
    }

    private void CreateFleetIcon()
    {
        // Container: holds the shared position/size used for zoom-tracking (UIUpdateForScroll)
        // and the click hit-test (OnPointerClick); carries no Graphic of its own.
        var iconObject = new GameObject("FleetIcon");
        iconObject.transform.SetParent(_statsCanvas.transform, false);
        _fleetIconRect = iconObject.AddComponent<RectTransform>();
        _fleetIconRect.anchorMin = _fleetIconRect.anchorMax = _fleetIconRect.pivot = new Vector2(0.5f, 0.5f);
        _fleetIconRect.sizeDelta = new Vector2(16, 16);
        _fleetIconRect.anchoredPosition = _fleetIconBaseAnchoredPosition;

        // Background: a plain square tinted with the owning player's color, filling the
        // container. Added first so it renders behind the ship-kind sprite added below.
        var backgroundObject = new GameObject("FleetIconBackground");
        backgroundObject.transform.SetParent(iconObject.transform, false);
        var backgroundRect = backgroundObject.AddComponent<RectTransform>();
        backgroundRect.anchorMin = Vector2.zero;
        backgroundRect.anchorMax = Vector2.one;
        backgroundRect.sizeDelta = Vector2.zero;
        _fleetIconBackgroundImage = backgroundObject.AddComponent<Image>();
        _fleetIconBackgroundImage.raycastTarget = false;

        // Sprite: the ship-kind icon (warship/colony ship), also filling the container.
        var spriteObject = new GameObject("FleetIconSprite");
        spriteObject.transform.SetParent(iconObject.transform, false);
        var spriteRect = spriteObject.AddComponent<RectTransform>();
        spriteRect.anchorMin = Vector2.zero;
        spriteRect.anchorMax = Vector2.one;
        spriteRect.sizeDelta = Vector2.zero;
        _fleetIconImage = spriteObject.AddComponent<Image>();
        _fleetIconImage.raycastTarget = true;

        iconObject.SetActive(false);
    }

    void Awake()
    {
        // Created in Awake (not Start) so the icon Image exists before the synchronous
        // UIUpdate() call in Gameboard.InitializeUIObject -- otherwise a loaded game shows
        // no fleet icon until the first turn tick.
        CreateFleetIcon();
    }

    // UpdatePlanet is called once per frame
    void Update()
    {
        
    }
}
