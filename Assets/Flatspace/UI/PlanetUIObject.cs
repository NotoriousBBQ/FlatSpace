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
    // Container for the per-player fleet icons: carries the shared position and zoom scale
    // (UIUpdateForScroll) and no Graphic of its own. Its children are the icons.
    private RectTransform _fleetIconRect;
    private readonly Vector2 _fleetIconBaseAnchoredPosition = new Vector2(40, -40);
    private const float FleetIconSize = 32f;
    private const float FleetIconSpacing = 4f;

    // One icon per player with ships here, side by side; the first sits at the base position and
    // the rest extend to its right. Created on demand and reused (deactivated when not needed).
    private class FleetIconView
    {
        public RectTransform Rect;
        public Image Background;
        public Image Sprite;
        public TextMeshProUGUI Count;
        public int Owner;
    }
    private readonly List<FleetIconView> _fleetIcons = new List<FleetIconView>();
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
            var groups = FleetSummary.ForPlanet(planet);
            while (_fleetIcons.Count < groups.Count) _fleetIcons.Add(CreateFleetIconView());
            for (var i = 0; i < _fleetIcons.Count; i++)
            {
                var view = _fleetIcons[i];
                if (i >= groups.Count)
                {
                    view.Rect.gameObject.SetActive(false);
                    continue;
                }
                var group = groups[i];
                view.Owner = group.Owner;
                view.Sprite.sprite = group.IconKind == Ship.ShipKind.WarShip
                    ? planet.GameAIConstants.warShipData.shipIcon
                    : planet.GameAIConstants.colonyShipData.shipIcon;
                view.Background.color = FleetSummary.ColorFor(group.Owner);
                view.Count.text = group.Count.ToString();
                view.Rect.anchoredPosition = new Vector2(i * (FleetIconSize + FleetIconSpacing), 0f);
                view.Rect.gameObject.SetActive(true);
            }
            _fleetIconRect.gameObject.SetActive(groups.Count > 0);
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
        // The fleet icons are child graphics on the same world-space canvas, so whether the raw
        // click is picked up by the planet's collider (Physics2DRaycaster) or by an icon's
        // Image (GraphicRaycaster) depends on sorting order -- the bubbled click ends here
        // either way. Disambiguate against each icon's live screen rect so it works regardless
        // of zoom level or which raycaster won; the icon clicked decides whose fleet opens.
        if (_fleetIconRect && _fleetIconRect.gameObject.activeSelf)
        {
            var iconCamera = _statsCanvas.worldCamera ? _statsCanvas.worldCamera : Camera.main;
            foreach (var view in _fleetIcons)
            {
                if (!view.Rect.gameObject.activeSelf) continue;
                if (RectTransformUtility.RectangleContainsScreenPoint(view.Rect, eventData.position, iconCamera))
                {
                    Gameboard.Instance.ShowFleetUI(_planetName, view.Owner);
                    return;
                }
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
        _fleetIconRect.sizeDelta = new Vector2(FleetIconSize, FleetIconSize);
        _fleetIconRect.anchoredPosition = _fleetIconBaseAnchoredPosition;

        iconObject.SetActive(false);
    }

    // One player's fleet icon: a background square tinted with the ship owner's color, the
    // ship-kind sprite over it, and the ship count in the bottom-right corner. Positioned by
    // UIUpdate relative to the shared container's center.
    private FleetIconView CreateFleetIconView()
    {
        var view = new FleetIconView();
        var iconObject = new GameObject("FleetIconView");
        iconObject.transform.SetParent(_fleetIconRect.transform, false);
        view.Rect = iconObject.AddComponent<RectTransform>();
        view.Rect.anchorMin = view.Rect.anchorMax = view.Rect.pivot = new Vector2(0.5f, 0.5f);
        view.Rect.sizeDelta = new Vector2(FleetIconSize, FleetIconSize);

        // Added first so it renders behind the sprite and the count.
        var backgroundObject = new GameObject("FleetIconBackground");
        backgroundObject.transform.SetParent(iconObject.transform, false);
        var backgroundRect = backgroundObject.AddComponent<RectTransform>();
        backgroundRect.anchorMin = Vector2.zero;
        backgroundRect.anchorMax = Vector2.one;
        backgroundRect.sizeDelta = Vector2.zero;
        view.Background = backgroundObject.AddComponent<Image>();
        view.Background.raycastTarget = false;

        var spriteObject = new GameObject("FleetIconSprite");
        spriteObject.transform.SetParent(iconObject.transform, false);
        var spriteRect = spriteObject.AddComponent<RectTransform>();
        spriteRect.anchorMin = Vector2.zero;
        spriteRect.anchorMax = Vector2.one;
        spriteRect.sizeDelta = Vector2.zero;
        view.Sprite = spriteObject.AddComponent<Image>();
        view.Sprite.raycastTarget = true;

        var countObject = new GameObject("FleetIconCount");
        countObject.transform.SetParent(iconObject.transform, false);
        var countRect = countObject.AddComponent<RectTransform>();
        countRect.anchorMin = Vector2.zero;
        countRect.anchorMax = Vector2.one;
        countRect.sizeDelta = Vector2.zero;
        view.Count = countObject.AddComponent<TextMeshProUGUI>();
        view.Count.raycastTarget = false;
        view.Count.fontSize = 14;
        view.Count.fontStyle = FontStyles.Bold;
        view.Count.alignment = TextAlignmentOptions.BottomRight;
        view.Count.color = Color.white;
        view.Count.textWrappingMode = TextWrappingModes.NoWrap;

        iconObject.SetActive(false);
        return view;
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
