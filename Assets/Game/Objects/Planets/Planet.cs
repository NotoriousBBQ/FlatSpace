using System;
using System.Collections.Generic;
using FlatSpace.Game;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

using ResultType = Planet.PlanetUpdateResult.PlanetUpdateResultType ;
using ResultPriority = Planet.PlanetUpdateResult.PlanetUpdateResultPriority ;
public class Planet : MonoBehaviour
{
    [SerializeField] private PlanetType _planetType = PlanetType.PlanetTypeNormal;
    
    [SerializeField] private int _population = 0;

    public int Population
    {
        get { return _population; }
        set { _population = value; }
    }
    public int MaxPopulation => _resourceData._maxPopulation;
    
    [SerializeField] private float _food = 0.0f;
    [SerializeField] private float _foodNeededForNewPop = 10.0f;

    private string _planetName = "";
    private Vector2 _position = new Vector2(0.0f, 0.0f);
    
    private PlanetUIObject _planetUIObject;
    private PlanetUIObject _mapUI;
    private PlanetResourceData _resourceData = null;
    public enum PlanetType
    {
        PlanetTypePrime,
        PlanetTypeNormal,
        PlanetTypeFarm,
        PlanetTypeVerdant,
        PlanetTypeIndustrial,
        PlanetTypeDesolate
    }

    public string PlanetName => _planetName;
    public Vector2 Position => _position;

    public struct PlanetUpdateResult
    {
        public enum PlanetUpdateResultType
        {
            PlanetUpdateResultTypeNone,
            PlanetUpdateResultTypeDead,
            PlanetUpdateResultTypePopulationGain,
            PlanetUpdateResultTypePopulationLoss,
            PlanetUpdateResultTypePopulationMaxReached,
            PlanetUpdateResultTypePopulationSurplus,
            PlanetUpdateResultTypeFoodShortage,
            PlanetUpdateResultTypeFoodSurplus,
            PlanetUpdateResultTypeGrotsits
        }

        public enum PlanetUpdateResultPriority
        {
            PlanetUpdateResultPriorityNone,
            PlanetUpdateResultPriorityLow,
            PlanetUpdateResultPriorityMedium,
            PlanetUpdateResultPriorityHigh,
            PlanetUpdateResultPriorityUrgent
        }

        public PlanetUpdateResult(string planetName, ResultType type, object data)
        {
            Name = planetName;
            Result = type;
            Data = data;
            switch (Result)
            {
                case ResultType.PlanetUpdateResultTypeNone:
                    Priority = ResultPriority.PlanetUpdateResultPriorityNone;
                    break;
                case ResultType.PlanetUpdateResultTypeDead:
                    Priority = ResultPriority.PlanetUpdateResultPriorityHigh;
                    break;
                case ResultType.PlanetUpdateResultTypePopulationGain:
                case ResultType.PlanetUpdateResultTypeFoodSurplus:
                    Priority = ResultPriority.PlanetUpdateResultPriorityLow;
                    break;
                case ResultType.PlanetUpdateResultTypePopulationLoss:
                    Priority = ResultPriority.PlanetUpdateResultPriorityUrgent;
                    break;
                case ResultType.PlanetUpdateResultTypePopulationMaxReached:
                case ResultType.PlanetUpdateResultTypePopulationSurplus:
                case ResultType.PlanetUpdateResultTypeFoodShortage:
                    Priority = ResultPriority.PlanetUpdateResultPriorityHigh;
                    break;
                default:
                    Priority = ResultPriority.PlanetUpdateResultPriorityNone;
                    break;
            }
        }

        public readonly string Name;
        public readonly ResultType Result;
        public ResultPriority Priority;
        public readonly object Data;
    }
    
    public void Init(PlanetType planetType, Transform parentTransform, 
        Vector3 position)
    {
#if USE_ALGORIGHMIC_BOARD
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
                _mapUI = Instantiate<PlanetUIObject>(prefab,parentTransform) as PlanetUIObject;
                _mapUI.transform.localPosition += position;
                InitializeUIObject();
            }
        }  
#endif
    }

    private void InitUIElement(Vector3 position, Transform parentTransform)
    {
        var prefab = Gameboard.Instance.planetUIObjects[(int)_planetType];
        if (!prefab)
            return;
            
        _mapUI = Instantiate(prefab,parentTransform) as PlanetUIObject;
        _mapUI.transform.localPosition += position;
        InitializeUIObject();
    }
    public void Init(PlanetSpawnData spawnData, Transform parentTransform)
    {
        _resourceData = spawnData._resourceData;
        _planetName = spawnData._planetName;
        _population = _resourceData._initialPopulation;
        _food = _resourceData._initialFood;
        _position = new Vector2(spawnData._planetPosition.x, spawnData._planetPosition.y);
        _planetType = spawnData._planetType;

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

    public void PlanetProductionUpdate(List<PlanetUpdateResult> resultList)
    {
        if (_population == 0)
            return;
        // grow food
        _food += _population * _resourceData._foodProduction;
        
        if (_food < _population)
        {
            // cant feed everyong
            resultList.Add(new PlanetUpdateResult(_planetName, ResultType.PlanetUpdateResultTypeFoodShortage, _food - _population));
            //hinky code to prevent mass dyeoffs
            _food--;
            if (_food <= 0.0f)
            {
                // lose a pop
                _population--;
                // start the food countdown again
                _food = _population;
                resultList.Add(new PlanetUpdateResult(_planetName, ResultType.PlanetUpdateResultTypePopulationLoss, 1));
                if (_population <= 0)
                {
                    // planet id dead
                    resultList.Add(new PlanetUpdateResult(_planetName, ResultType.PlanetUpdateResultTypeDead, null));
                }
                
            }
        }
        else
        {
            // feed everybody
            _food -= _population;
            // enough to grow?
            if (_food >= _foodNeededForNewPop)
            {
                resultList.Add(new PlanetUpdateResult(_planetName, ResultType.PlanetUpdateResultTypePopulationGain, 1));
                _food -= _foodNeededForNewPop;
                _population++;
                if (_population <= _resourceData._maxPopulation)
                {

                    if (_population == _resourceData._maxPopulation)
                        resultList.Add(new PlanetUpdateResult(_planetName,
                            ResultType.PlanetUpdateResultTypePopulationMaxReached, null));
                }
                else
                {
                    resultList.Add(new PlanetUpdateResult(_planetName,
                        ResultType.PlanetUpdateResultTypePopulationSurplus, _population - _resourceData._maxPopulation));
                    _population =  _resourceData._maxPopulation;
                }
            }

            if (_food > _population)
            {
                resultList.Add(new PlanetUpdateResult(_planetName,
                    ResultType.PlanetUpdateResultTypeFoodSurplus, _food - _population));
                
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
