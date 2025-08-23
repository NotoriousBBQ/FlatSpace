using System;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

public class Planet : MonoBehaviour
{
    [SerializeField] private PlanetType _planetType = PlanetType.PlanetTypeNormal;
    [SerializeField] private string _planetPrefab = "Assets/UI/PlanetUI.prefab";
    
    [SerializeField] private int _population = 0;

    [SerializeField] private float _food = 0.0f;
    [SerializeField] private float _foodNeededForNewPop = 10.0f;

    private string _planetName = "";
    
    private PlanetUIObject _planetUIObject;
    private GameObject _mapUI;
    private PlanetResourceData _resourceData = null;
    public enum PlanetType
    {
        PlanetTypePrime,
        PlanetTypeNormal,
        PlanetTypeFarm,
        PlanetTypeVerdant,
        PlanetTypeIndustial,
        PlanetTypeDesolate
    }

    public void Init(PlanetType planetType, Transform parentTransform, 
        Vector3 position)
    {
        if (planetType == PlanetType.PlanetTypePrime)
        {
            _planetType = PlanetType.PlanetTypePrime;
            _population = 1;
        }
        
        if (!string.IsNullOrEmpty(_planetPrefab))
        {
            var prefab = AssetDatabase.LoadAssetAtPath(_planetPrefab, typeof(GameObject)) as GameObject;
            if (prefab)
            {
                _mapUI = Instantiate(prefab,parentTransform) as GameObject;
                _mapUI.transform.localPosition += position;
                InitializeUIObject();
            }
        }  

    }

    private void InitUIElement(Vector3 position, Transform parentTransform)
    {
        if (!string.IsNullOrEmpty(_planetPrefab))
        {
            var prefab = AssetDatabase.LoadAssetAtPath(_planetPrefab, typeof(GameObject)) as GameObject;
            if (prefab)
            {
                _mapUI = Instantiate(prefab,parentTransform) as GameObject;
                _mapUI.transform.localPosition += position;
                InitializeUIObject();
            }
        }       
    }
    public void Init(PlanetSpawnData spawnData, Transform parentTransform)
    {
        _resourceData = spawnData._resourceData;
        _planetName = spawnData._planetName;
        _population = _resourceData._initialPopulation;
        _food = _resourceData._initialFood;

        InitUIElement(spawnData._planetPosition, parentTransform);
    }

    private void InitializeUIObject()
    {

        _planetUIObject  = _mapUI.GetComponent<PlanetUIObject>();
        if (_planetUIObject == null)
            return;
        _planetUIObject._nameTextField.text = this._planetName;
        UpdateMapUI();        
    }

    public void UpdateMapUI()
    {
        if (_planetUIObject)
        {
            _planetUIObject._populationTextField.text = _population.ToString();
            _planetUIObject._foodTextField.text = _food.ToString();
        }
    }

    public void PlanetUpdate()
    {
        // grow food
        _food += _population * _resourceData._foodProduction;
        // feed everybody
        _food -= _population;
        // enough to grow?
        if (_food >= _foodNeededForNewPop)
        {
            if (_population < _resourceData._maxPopulation)
            {
                _food -= _foodNeededForNewPop;
                _population++;
            }
            
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
