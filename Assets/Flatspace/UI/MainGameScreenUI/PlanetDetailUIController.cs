using FlatSpace.Game;
using UnityEngine;
using UnityEngine.UIElements;

public class PlanetDetailUIController : MonoBehaviour
{
    public UIDocument uiDocument;

    private Planet _planet;
    private VisualElement _element;
    private VisualElement _panelElement;
    private VisualElement _fleetIcon;
    private Label _planetName;
    private Label _populationValue;
    private Label _populationProgress;
    private Label _foodProduction;
    private Label _foodStorage;
    private Label _grotsitsProduction;
    private Label _grotsitsStorage;
    private Image _planetIcon;
    private Label _industryProduction;
    private Label _researchProduction;
    private Label _productionItem;
    private Label _productionProgress;
    public Sprite desertIcon;
    public Sprite desolateIcon;
    public Sprite farmIcon;
    public Sprite industrialIcon;
    public Sprite normalIcon;
    public Sprite oceanIcon;
    public Sprite primeIcon;
    public Sprite verdantIcon;

    private void SetIconForPlanetType()
    {
        switch (_planet.Type)
        {
            case Planet.PlanetType.PlanetTypeDesert:
                _planetIcon.sprite = desertIcon;
                break;
            case Planet.PlanetType.PlanetTypeDesolate:
                _planetIcon.sprite = desolateIcon;
                break;
            case Planet.PlanetType.PlanetTypeFarm:
                _planetIcon.sprite = farmIcon;
                break;
            case Planet.PlanetType.PlanetTypeIndustrial:
                _planetIcon.sprite = industrialIcon;
                break;
            case Planet.PlanetType.PlanetTypeNormal:
                _planetIcon.sprite = normalIcon;
                break;
            case Planet.PlanetType.PlanetTypeOcean:
                _planetIcon.sprite = oceanIcon;
                break;
            case Planet.PlanetType.PlanetTypePrime:
                _planetIcon.sprite = primeIcon;
                break;
            case Planet.PlanetType.PlanetTypeVerdant:
                _planetIcon.sprite = verdantIcon;
                break;
        }
    }
    private void OnEnable()
    {
        if(_planet == null || _element == null) return;
        _element.SetEnabled(true);
        _element.visible = true;
        _element.pickingMode = PickingMode.Ignore;
    }

    // Used by Gameboard to detect clicks outside this panel so it can be dismissed without
    // swallowing clicks meant for controls inside the panel itself. Bounds-test against
    // _panelElement (the "PlanetDetailElement" box), not _element (uiDocument.rootVisualElement),
    // which stretches to fill the whole screen and would make every point match.
    public bool ContainsScreenPoint(Vector2 screenPosition)
    {
        if (_panelElement == null || _panelElement.panel == null) return false;
        var panelPosition = RuntimePanelUtils.ScreenToPanel(_panelElement.panel, screenPosition);
        return _panelElement.worldBound.Contains(panelPosition);
    }

    // Each fleet icon is a real pickable element with its own ClickEvent (registered when the icon
    // is built), rather than a screen-point bounds test polled from Gameboard: a 20px target is
    // too small to hit reliably through the ScreenToPanel / worldBound round-trip, and UI
    // Toolkit's own picking uses the resolved layout at event time. The icon decides whose fleet opens.
    private void OnFleetIconClicked(ClickEvent evt, int owner)
    {
        if (_planet == null) return;
        Gameboard.Instance.ShowFleetUI(_planet.PlanetName, owner);
        evt.StopPropagation();
    }

    private const float FleetIconSize = 25f;

    // One player's fleet icon: a square tinted with the ship owner's color, the ship-kind sprite
    // over it, and the ship count in the bottom-right corner.
    private VisualElement CreateFleetIconElement(FleetSummary.Group group)
    {
        var icon = new VisualElement { pickingMode = PickingMode.Position };
        icon.style.width = FleetIconSize;
        icon.style.height = FleetIconSize;
        icon.style.marginLeft = 4;
        icon.style.backgroundColor = new StyleColor((Color)FleetSummary.ColorFor(group.Owner));

        var shipIcon = group.IconKind == Ship.ShipKind.WarShip
            ? _planet.GameAIConstants.warShipData.shipIcon
            : _planet.GameAIConstants.colonyShipData.shipIcon;
        var sprite = new VisualElement { pickingMode = PickingMode.Ignore };
        sprite.style.position = Position.Absolute;
        sprite.style.left = sprite.style.right = sprite.style.top = sprite.style.bottom = 0;
        sprite.style.backgroundImage = new StyleBackground(shipIcon);
        icon.Add(sprite);

        var count = new Label(group.Count.ToString()) { pickingMode = PickingMode.Ignore };
        count.style.position = Position.Absolute;
        count.style.right = 1;
        count.style.bottom = 0;
        count.style.fontSize = 10;
        count.style.unityFontStyleAndWeight = FontStyle.Bold;
        count.style.color = new StyleColor(Color.white);
        // Dark outline so the white number stays readable on any player color.
        count.style.unityTextOutlineColor = new StyleColor(Color.black);
        count.style.unityTextOutlineWidth = 1f;
        icon.Add(count);

        var owner = group.Owner;
        icon.RegisterCallback<ClickEvent>(evt => OnFleetIconClicked(evt, owner));
        return icon;
    }

    private void OnDisable()
    {
        if(_element == null) return;
        _element.SetEnabled(false);
        _element.visible = false;
        _element.pickingMode = PickingMode.Ignore;
    }

    public void Awake()
    {
        _element = uiDocument.rootVisualElement;
        _panelElement = _element.Q<VisualElement>("PlanetDetailElement");

        _planetName = _element.Q<Label>("PlanetName");
        _planetIcon = _element.Q<Image>("PlanetIcon");
        _populationValue = _element.Q<Label>("PopulationValue");
        _populationProgress = _element.Q<Label>("PopulationProgress");
        _foodProduction = _element.Q<Label>("FoodProduction");
        _foodStorage = _element.Q<Label>("FoodStored");
        _grotsitsProduction = _element.Q<Label>("GrotsitsProduction");
        _grotsitsStorage = _element.Q<Label>("GrotsitsStored");
        _industryProduction = _element.Q<Label>("IndustryProduction");
        _researchProduction = _element.Q<Label>("ResearchProduction");
        _productionItem = _element.Q<Label>("ProductionItem");
        _productionProgress = _element.Q<Label>("ProductionProgress");
        // The UXML "FleetIcon" element is a row container for the per-player fleet icons, which
        // are added from code (UpdatePlanetDetail). It ignores picking itself; each icon is pickable.
        _fleetIcon = _element.Q<VisualElement>("FleetIcon");
        if (_fleetIcon != null)
        {
            _fleetIcon.pickingMode = PickingMode.Ignore;
            _fleetIcon.style.width = StyleKeyword.Auto;
            _fleetIcon.style.flexDirection = FlexDirection.Row;
        }
        enabled = false;

    }

    public void SetPlanet(Planet planet)
    {
        if (planet == null) return; // || _element == null) return;
        _planet = planet;
        _planetName.text = _planet.PlanetName;
        SetIconForPlanetType();
        UpdatePlanetDetail();
    }

    public void UpdatePlanetDetail()
    {
        _populationValue.text =
            string.Format("{0}/{1}", _planet.Population.Count.ToString(), _planet.MaxPopulation.ToString());
        _populationProgress.text = 
            string.Format("{0}/{1}", _planet.Food.ToString(), _planet.FoodNeededForNewPop.ToString());
        _foodProduction.text = _planet.FoodProduced.ToString();
        _foodStorage.text = _planet.Food.ToString();
        _grotsitsProduction.text = _planet.GrotsitsProduced.ToString();
        _grotsitsStorage.text = _planet.Grotsits.ToString();
        _industryProduction.text = _planet.IndustryProduced.ToString();
        _researchProduction.text = _planet.ResearchProduced.ToString();
        _productionItem.text = _planet.CurrentProduction?.Item.itemName ?? "None";
        _productionProgress.text = string.Format("{0}/{1}", _planet.CurrentProduction?.Progress.ToString() ?? "0",
            _planet.CurrentProduction?.Item.cost.ToString() ?? "X");
        if (_fleetIcon != null)
        {
            // Rebuilt every update: a handful of icons at most, and it keeps each icon's click
            // callback bound to the right owner.
            _fleetIcon.Clear();
            var groups = FleetSummary.ForPlanet(_planet);
            foreach (var group in groups)
                _fleetIcon.Add(CreateFleetIconElement(group));
            _fleetIcon.style.display = groups.Count > 0 ? DisplayStyle.Flex : DisplayStyle.None;
        }
    }

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        
    }

    // Update is called once per frame
    void Update()
    {
        
    }
}
