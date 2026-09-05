using System;
using System.Collections.Generic;
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
    private Image _fleetIconImage;
    public string _planetName;
    public bool _changeColor = false;
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
        if (_fleetIconImage)
            _fleetIconImage.gameObject.SetActive(planet.DockedShips.Count > 0);
    }

    public void UIUpdateForScroll(float orthoChange = 0.0f)
    {
        var scaleChange = _statsCanvas.transform.localScale.x + (-orthoChange/5.0f);
        _statsCanvas.transform.localScale = new Vector3(scaleChange, scaleChange, scaleChange);; 
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

    public void OnPointerClick(PointerEventData eventData)
    {
        // The fleet icon is a child graphic on the same world-space canvas, so whether the raw
        // click is picked up by the planet's collider (Physics2DRaycaster) or by the icon's
        // Image (GraphicRaycaster) depends on sorting order -- the bubbled click ends here
        // either way. Disambiguate against the icon's live screen rect so it works regardless
        // of zoom level or which raycaster won.
        if (_fleetIconImage && _fleetIconImage.gameObject.activeSelf)
        {
            var iconCamera = _statsCanvas.worldCamera ? _statsCanvas.worldCamera : Camera.main;
            if (RectTransformUtility.RectangleContainsScreenPoint(
                    _fleetIconImage.rectTransform, eventData.position, iconCamera))
            {
                Gameboard.Instance.ShowFleetUI(_planetName);
                return;
            }
        }
        Gameboard.Instance.ShowPlanetDetail(_planetName);
    }

    private void CreateFleetIcon()
    {
        var iconObject = new GameObject("FleetIcon");
        iconObject.transform.SetParent(_statsCanvas.transform, false);
        var rect = iconObject.AddComponent<RectTransform>();
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
        rect.sizeDelta = new Vector2(16, 16);
        rect.anchoredPosition = new Vector2(20, -20);
        _fleetIconImage = iconObject.AddComponent<Image>();
        _fleetIconImage.color = new Color32(255, 215, 0, 255); // placeholder gold badge -- swap for real art later
        _fleetIconImage.raycastTarget = true;
        _fleetIconImage.gameObject.SetActive(false);
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
