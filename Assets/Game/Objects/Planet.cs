using System;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

public class Planet : MonoBehaviour
{
    [SerializeField] private PlanetType _planetType = PlanetType.PlanetTypeNormal;
    [SerializeField] private string _planetPrefab = "Assets/UI/PlanetUI.prefab";
    
    [SerializeField] private float _population = 0.0f;
    [SerializeField] private float _maxPopulation = 10.0f;

    [SerializeField] private float _food = 0.0f;
    [SerializeField] private float _MaxFood = 10.0f;

    private PlanetUIObject _planetUIObject;
    
    private GameObject _mapUI;
    public enum PlanetType
    {
        PlanetTypePrime,
        PlanetTypeNormal
    }

    public Planet(PlanetType planetType = PlanetType.PlanetTypeNormal, Transform parentTransform = null)
    {
        if (planetType == PlanetType.PlanetTypePrime)
        {
            _planetType = PlanetType.PlanetTypePrime;
            _population = 1.0f;
        }
               

        if (!string.IsNullOrEmpty(_planetPrefab))
        {
            var prefab = AssetDatabase.LoadAssetAtPath(_planetPrefab, typeof(GameObject)) as GameObject;
  //          prefab.transform.localPosition = new Vector3(100, 100, 0);
             _mapUI = Instantiate(prefab, parentTransform);

             InitializeUIObject();
        }
        
    }

    private void InitializeUIObject()
    {

        _planetUIObject  = _mapUI.GetComponent<PlanetUIObject>();
        if (_planetUIObject == null)
            return;
        _planetUIObject._nameTextField.text = _planetType == PlanetType.PlanetTypePrime ? "Prime" : "Normal";
        UpdateMapUI();        
    }

    private void UpdateMapUI()
    {
        if (_planetUIObject)
        {
            _planetUIObject._populationTextField.text = _population.ToString();
            _planetUIObject._foodTextField.text = _food.ToString();
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
