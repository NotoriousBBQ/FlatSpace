using System;
using System.Collections.Generic;
using FlatSpace.Game;
using TMPro;
using Unity.VisualScripting;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

using ResultType = Planet.PlanetUpdateResult.PlanetUpdateResultType ;
using ResultPriority = Planet.PlanetUpdateResult.PlanetUpdateResultPriority ;
public class Planet : MonoBehaviour
{
    [SerializeField] private PlanetType _planetType = PlanetType.PlanetTypeNormal;
    
    [SerializeField] private int _population = 0;

    public int FoodWorkers { get; private set; }
    public int GrotsitsWorkers { get; private set; }
    public PlanetStrategy CurrentStrategy { get; private set; }
    public float Morale { get; private set; }

    public int Population
    {
        get { return _population; }
        set { _population = value; }
    }
    public int MaxPopulation => _resourceData._maxPopulation;
    
    [SerializeField] private float _food = 0.0f;
    [SerializeField] private float _foodNeededForNewPop = 10.0f;
    [SerializeField] private float _moraleStep = 5;

    public float Grotsits {get; private set;}
    
    private string _planetName = "";
    private Vector2 _position = new Vector2(0.0f, 0.0f);
    
    private PlanetUIObject _planetUIObject;

    private PlanetResourceData _resourceData = null;
    public Dictionary<string, GameAIMap.DestinationToPathingListEntry> DistanceMapToPathingList;
    public enum PlanetType
    {
        PlanetTypePrime,
        PlanetTypeNormal,
        PlanetTypeFarm,
        PlanetTypeVerdant,
        PlanetTypeIndustrial,
        PlanetTypeDesolate
    }

    public enum PlanetStrategy
    {
        PlanetStrategyBalanced,
        PlanetStrategyGrowth,
        PlanetStrategyFood,
        PlanetStrategyFocusedFood,
        PlanetStrategyGrotsits,
        PlanetStrategyFocusedGrotsits,
    }

    public string PlanetName => _planetName;
    public Vector2 Position => _position;
    public float Food => _food;

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
            PlanetUpdateResultTypeGrotsitsShortage,
            PlanetUpdateResultTypeGrotsitsSurplus,
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

    public void Init(PlanetSpawnData spawnData, Transform parentTransform)
    {
        _resourceData = spawnData._resourceData;
        _planetName = spawnData._planetName;
        _population = _resourceData._initialPopulation;
        _food = _resourceData._initialFood;
        Grotsits = _resourceData._initialGrotsits;
        _position = new Vector2(spawnData._planetPosition.x, spawnData._planetPosition.y);
        _planetType = spawnData._planetType;
        DistanceMapToPathingList = new Dictionary<string, GameAIMap.DestinationToPathingListEntry>();
        CurrentStrategy = _resourceData._initialStrategy;
        Morale = 100.0f;

    }

    private void AssignWorkForStrategy()
    {
        int remainingWorkers;
        switch (CurrentStrategy)
        {
            case PlanetStrategy.PlanetStrategyBalanced:
                // first, make sure the basics are covered
                FoodWorkers = Convert.ToInt32(Convert.ToSingle(Population) / _resourceData._foodProduction);
                GrotsitsWorkers = Convert.ToInt32(Convert.ToSingle(Population) / _resourceData._grotsitProduction);
                GrotsitsWorkers = Math.Clamp(GrotsitsWorkers, 0, Population - FoodWorkers);
                // the even out the rest
                remainingWorkers = Population - (FoodWorkers + GrotsitsWorkers);
                if (remainingWorkers > 0)
                {
                    FoodWorkers += remainingWorkers / 2;
                    GrotsitsWorkers += remainingWorkers / 2;
                    remainingWorkers = Population -(FoodWorkers + GrotsitsWorkers);
                    if (remainingWorkers > 0)
                        FoodWorkers += remainingWorkers;
                }

                break;
            case PlanetStrategy.PlanetStrategyGrowth:
                FoodWorkers = Math.Clamp(
                    Convert.ToInt32(Convert.ToSingle(2 * Population) / _resourceData._foodProduction), 0, Population);
                GrotsitsWorkers = Math.Clamp(
                    Convert.ToInt32(Convert.ToSingle(Population) / _resourceData._grotsitProduction), 0, Population);
                GrotsitsWorkers = Math.Clamp(GrotsitsWorkers, 0, Population - FoodWorkers);
                break;
            case PlanetStrategy.PlanetStrategyFood:
                FoodWorkers = Math.Clamp(
                    Convert.ToInt32(Convert.ToSingle(2 * Population) / _resourceData._foodProduction), 0, Population);
                GrotsitsWorkers = Math.Clamp(
                    Convert.ToInt32(Convert.ToSingle(Population) / _resourceData._grotsitProduction), 0, Population);
                GrotsitsWorkers = Math.Clamp(GrotsitsWorkers, 0, Population - FoodWorkers);
                break;
            case PlanetStrategy.PlanetStrategyFocusedFood:
                FoodWorkers = _population;
                break;
            case PlanetStrategy.PlanetStrategyGrotsits:
                FoodWorkers = Convert.ToInt32(Convert.ToSingle(Population) / _resourceData._foodProduction);
                GrotsitsWorkers = Convert.ToInt32(Convert.ToSingle(Population) / _resourceData._grotsitProduction);
                if(GrotsitsWorkers <= 0)
                    GrotsitsWorkers = 1;
                // the focus on grotsits
                remainingWorkers = _population - (FoodWorkers + GrotsitsWorkers);
                if (remainingWorkers > 0)
                {
                    GrotsitsWorkers += remainingWorkers;
                }
                break;
            case PlanetStrategy.PlanetStrategyFocusedGrotsits:
                GrotsitsWorkers = _population;
                break;
        }
    }

    public void PlanetProductionUpdate(List<PlanetUpdateResult> resultList)
    {
        if (_population == 0)
            return;
        AssignWorkForStrategy();
        // grow food
        _food += FoodWorkers * _resourceData._foodProduction;
        // produce grosits
        Grotsits += GrotsitsWorkers * _resourceData._grotsitProduction;
        
        ConsumeFood(resultList);
        ConsumeGrotsits(resultList);
     
    }

    private void ConsumeFood(List<PlanetUpdateResult> resultList)
    {
        if (_population <= 0)
            return;
        if (_food < _population) 
        { 
            // cant feed everyong
            resultList.Add(new PlanetUpdateResult(_planetName, ResultType.PlanetUpdateResultTypeFoodShortage, _food - _population));
            //hinky code to prevent mass dieoffs
             _food--;
             if (_food <= 0.0f)
             {
                 // lose a pop
                 _population--;
                 // start the food countdown again
                 _food = _population;
                 resultList.Add(new PlanetUpdateResult(_planetName, ResultType.PlanetUpdateResultTypePopulationLoss,
                     1));
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

    private void ConsumeGrotsits(List<PlanetUpdateResult> resultList)
    {
        if (_population <= 0)
            return;
        if (Grotsits < _population) 
        { 
            // Can't give everyone goods
            resultList.Add(new PlanetUpdateResult(_planetName, ResultType.PlanetUpdateResultTypeGrotsitsShortage, Grotsits - _population));

            var numberShort = _population - Grotsits;
            Grotsits = 0.0f;
            Morale = Math.Clamp(Morale - _moraleStep, 0.0f, 200.0f);
        }
        else
        {
            // feed everybody
            Grotsits -= _population;
            // enough to grow?
            if (Grotsits > _population)
            {
                resultList.Add(new PlanetUpdateResult(_planetName,
                    ResultType.PlanetUpdateResultTypeGrotsitsSurplus, Grotsits - _population));
                Morale = Math.Clamp(Morale + _moraleStep, 0.0f, 200.0f);                
            }
            else
            {
                Morale = 100.0f;
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
