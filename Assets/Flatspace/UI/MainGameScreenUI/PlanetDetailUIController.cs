using UnityEngine;
using UnityEngine.UIElements;

public class PlanetDetailUIController : MonoBehaviour
{
    public UIDocument uiDocument;

    private Planet _planet;
    private VisualElement _element;
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
