using System.Collections.Generic;
using FlatSpace.Game;
using TMPro;
using UnityEngine;

public class PlanetUIObject : MonoBehaviour
{
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    [SerializeField] public TextMeshProUGUI _nameTextField;
    [SerializeField] public TextMeshProUGUI _populationTextField;
    [SerializeField] public TextMeshProUGUI _foodTextField;
    public string _planetName;

    private Dictionary<Planet.PlanetType, Color32> _planetColors = new Dictionary<Planet.PlanetType, Color32>
    {
        { Planet.PlanetType.PlanetTypeDesolate,  new Color32(196, 65,19, 255 )},
        { Planet.PlanetType.PlanetTypeFarm,  new Color32(143, 255,143, 255 )},
        { Planet.PlanetType.PlanetTypeIndustrial , new Color32(205, 133,65, 255 )},
        { Planet.PlanetType.PlanetTypeNormal , new Color32(135, 206,250, 255 )},
        { Planet.PlanetType.PlanetTypePrime , new Color32(255,255,255, 255 )},
        { Planet.PlanetType.PlanetTypeVerdant , new Color32(0,206,0, 255 )}
    };
    
    public void UIUpdate()
    {
        var planet = Gameboard.Instance.GetPlanet(_planetName);
        if (planet)
        {
            _populationTextField.text = planet.Population.ToString();
            _foodTextField.text = planet.Food.ToString();
        }
    }

    public void SetPlanetColor(Planet.PlanetType planetType)
    {
        var sprintRenderer = GetComponent<SpriteRenderer>();
        sprintRenderer.color = _planetColors[planetType];
    }

    void Start()
    {
        
    }

    // Update is called once per frame
    void Update()
    {
        
    }
}
