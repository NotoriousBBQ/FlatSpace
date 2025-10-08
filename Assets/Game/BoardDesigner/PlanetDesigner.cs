using System.Collections.Generic;
using TMPro;
using UnityEngine;


public class PlanetDesigner : MonoBehaviour
{
    public struct DesignerConnection
    {
        public PlanetDesigner Target;
        public readonly float Cost;

        public DesignerConnection(PlanetDesigner target, float cost)
        {
            Target = target;
            Cost = cost;
        }
    }
    
    public Planet.PlanetType type = Planet.PlanetType.PlanetTypeNormal;
    public int population = 0;
    public Planet.PlanetStrategy strategy = Planet.PlanetStrategy.PlanetStrategyBalanced;
    public float morale = 100.0f;
    public int maxPopulation = 10;
    public float food = 0.0f;
    public float foodNeededForNewPop = 10.0f;
    public float grotsits = 0.0f;
    public string planetName = "";
    public Vector2 position = new Vector2(0.0f, 0.0f);
    public Dictionary<string, GameAIMap.DestinationToPathingListEntry> DistanceMapToPathingList;
    public List<DesignerConnection> Connections = new List<DesignerConnection>();
 
    [SerializeField] public TextMeshProUGUI nameTextField;
    [SerializeField] public TextMeshProUGUI typeTextField;

    private readonly Dictionary<Planet.PlanetType, Color32> _planetColors = new Dictionary<Planet.PlanetType, Color32>
    {
        { Planet.PlanetType.PlanetTypeDesolate,  new Color32(196, 65,19, 255 )},
        { Planet.PlanetType.PlanetTypeFarm,  new Color32(91, 188,93, 255 )},
        { Planet.PlanetType.PlanetTypeIndustrial , new Color32(205, 133,65, 255 )},
        { Planet.PlanetType.PlanetTypeNormal , new Color32(135, 206,250, 255 )},
        { Planet.PlanetType.PlanetTypePrime , new Color32(173,173,22, 255 )},
        { Planet.PlanetType.PlanetTypeVerdant , new Color32(0,206,0, 255 )}
    };


    private void OnValidate()
    {
        
        nameTextField.text = planetName;
        typeTextField.text = type.ToString();

        SetPlanetColor(type);
    }

    public void UpdateGraphic()
    {
        OnValidate();
    }

    public void SetPlanetColor(Planet.PlanetType planetType)
    {
        var sprintRenderer = GetComponent<SpriteRenderer>();
        sprintRenderer.color = _planetColors[planetType];
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
