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
   
    public void UIUpdate()
    {
        var planet = Gameboard.Instance.GetPlanet(_planetName);
        if (planet)
        {
            _populationTextField.text = planet.Population.ToString();
            _foodTextField.text = planet.Food.ToString();
        }
    }


    void Start()
    {
        
    }

    // Update is called once per frame
    void Update()
    {
        
    }
}
