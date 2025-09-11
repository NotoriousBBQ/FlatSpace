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
    public struct PlanetUpdateResult
    {
        public enum PlanetUpdateResultType
        {
            PlanetUpdateResultTypeNone,
            PlanetUpdateResultTypeDead,
            PlanetUpdateResultTypePopulationGain,
            PlanetUpdateResultTypePopulationLoss,
            PlanetUpdateResultTypePopulationMax,
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
                    Priority = ResultPriority.PlanetUpdateResultPriorityMedium;
                    break;
                case ResultType.PlanetUpdateResultTypePopulationLoss:
                    Priority = ResultPriority.PlanetUpdateResultPriorityUrgent;
                    break;
                case ResultType.PlanetUpdateResultTypePopulationMax:
                case ResultType.PlanetUpdateResultTypePopulationSurplus:
                case ResultType.PlanetUpdateResultTypeFoodShortage:
                case ResultType.PlanetUpdateResultTypeGrotsitsShortage:
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
    public PlanetType Type { get; private set; } = PlanetType.PlanetTypeNormal;
    
    public int Population { get; set; }= 0;

    public int FoodWorkers { get; private set; }
    public int ProjectedFoodWorkers { get; private set; }
    public int GrotsitsWorkers { get; private set; }
    public int ProjectedGrotsitsWorkers { get; private set; }
    public PlanetStrategy CurrentStrategy { get; set; }
    public float Morale { get; set; }
    public int MaxPopulation => _resourceData._maxPopulation;
    
    public float Food { get; set; } = 0.0f;
    public float ProjectedFood { get; set; } = 0.0f;
    [SerializeField] private float _foodNeededForNewPop = 10.0f;

    public float Grotsits {get; set;}
    public float ProjectedGrotsits {get; set;}
    
    public string PlanetName {get; private set;} = "";
    public Vector2 Position { get; private set; }= new Vector2(0.0f, 0.0f);
    public bool ColonizationInProgress {get; set;}
    
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

    private GameAIConstants _gameAIConstants;



    public void Init(PlanetSpawnData spawnData, Transform parentTransform, GameAIConstants gameAIConstants)
    {
        _gameAIConstants = gameAIConstants;
        _resourceData = spawnData._resourceData;
        PlanetName = spawnData._planetName;
        Population = _resourceData._initialPopulation;
        Food = ProjectedFood = _resourceData._initialFood;
        Grotsits = ProjectedGrotsits = _resourceData._initialGrotsits;
        Position = new Vector2(spawnData._planetPosition.x, spawnData._planetPosition.y);
        Type = spawnData._planetType;
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
                // always err on the side of more food
                var foodWorkersFloat = Math.Ceiling((Convert.ToSingle(Population + 1) / (_resourceData._foodProduction * (Morale/100.0f))));
                FoodWorkers = Math.Clamp(Convert.ToInt32(foodWorkersFloat), 0, Population);
                // fopr grotsits, use the existing first
                GrotsitsWorkers = Math.Clamp(Convert.ToInt32((Convert.ToSingle(Population)) / (_resourceData._grotsitProduction * (Morale/100.0f))), 0, Population - FoodWorkers);
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
                    Convert.ToInt32(Convert.ToSingle(2 * Population) / (_resourceData._foodProduction * (Morale/100.0f))), 0, Population);
                GrotsitsWorkers = Math.Clamp(
                    Convert.ToInt32(Convert.ToSingle(Population) / (_resourceData._grotsitProduction * (Morale/100.0f))), 0, Population);
                GrotsitsWorkers = Math.Clamp(GrotsitsWorkers, 0, Population - FoodWorkers);
                break;
            case PlanetStrategy.PlanetStrategyFood:
                FoodWorkers = Math.Clamp(Convert.ToInt32(Convert.ToSingle(2 * Population) / (_resourceData._grotsitProduction * (Morale/100.0f))), 0, Population);
                GrotsitsWorkers = Math.Clamp(Population - FoodWorkers, 0, Population);
                break;
            case PlanetStrategy.PlanetStrategyFocusedFood:
                FoodWorkers = Math.Clamp(Convert.ToInt32(Convert.ToSingle(3 * Population) / (_resourceData._grotsitProduction * (Morale/100.0f))), 0, Population);
                GrotsitsWorkers = Math.Clamp(Population - FoodWorkers, 0, Population);
                break;
            case PlanetStrategy.PlanetStrategyGrotsits:
                GrotsitsWorkers = Math.Clamp(Convert.ToInt32(Convert.ToSingle(2 * Population) / (_resourceData._grotsitProduction * (Morale/100.0f))), 0, Population);
                FoodWorkers = Math.Clamp(Population - GrotsitsWorkers, 0, Population);
                break;
            case PlanetStrategy.PlanetStrategyFocusedGrotsits:
                GrotsitsWorkers = Math.Clamp(Convert.ToInt32(Convert.ToSingle(3 * Population) / (_resourceData._grotsitProduction * (Morale/100.0f))), 0, Population);
                FoodWorkers = Math.Clamp(Population - GrotsitsWorkers, 0, Population);
                break;
        }
    }

    public void PlanetProductionUpdate(List<PlanetUpdateResult> resultList)
    {
        if (Population == 0)
            return;
        AssignWorkForStrategy();
        // grow food
        Food += FoodWorkers * _resourceData._foodProduction * (Morale/100.0f);
        // produce grosits
        Grotsits += GrotsitsWorkers * _resourceData._grotsitProduction * (Morale/100.0f);
        
        ConsumeFood(resultList);
        ConsumeGrotsits(resultList);
     
    }

    private void ConsumeFood(List<PlanetUpdateResult> resultList)
    {
        if (Population <= 0 && Food <= _resourceData._initialFood)
            return;
        bool DEBUG_HAD_SURPLUS = false;
        float foodShortage = 0.0f;
        if (Food < Population) 
        { 
            // cant feed everyong
            foodShortage = Food - Population;
            //hinky code to prevent mass dieoffs
             Food--;
             if (Food <= 0.0f)
             {
                 // lose a pop
                 Population--;
                 // start the food countdown again
                 Food = Population;
                 resultList.Add(new PlanetUpdateResult(PlanetName, ResultType.PlanetUpdateResultTypePopulationLoss,
                     1));
                 if (Population <= 0)
                 {
                     // planet id dead
                     resultList.Add(new PlanetUpdateResult(PlanetName, ResultType.PlanetUpdateResultTypeDead, null));
                 }
             }
        }
        else
        {
            // feed everybody
            Food -= Population;
            // enough to grow?
            if (Food >= _foodNeededForNewPop && Population < MaxPopulation)
            {
                resultList.Add(new PlanetUpdateResult(PlanetName, ResultType.PlanetUpdateResultTypePopulationGain, 1));
                Food -= _foodNeededForNewPop;
                Population++;

                if (Population >= MaxPopulation)
                    resultList.Add(new PlanetUpdateResult(PlanetName,
                        ResultType.PlanetUpdateResultTypePopulationSurplus, Population - _resourceData._maxPopulation));
            }

            if (Population >= MaxPopulation)
            {
                resultList.Add(new PlanetUpdateResult(PlanetName,
                    ResultType.PlanetUpdateResultTypePopulationMax, 1));
            }
        }

        // add 1 to population here to allow for growth if possible
        var projectedPopulation = Population + (Population < MaxPopulation ? 1 : 0);
        SetProjectedWorkers(projectedPopulation);
        ProjectedFood = ProjectedFoodWorkers *_resourceData._foodProduction;

        if (ProjectedFood + Food > projectedPopulation && foodShortage >= 0.0f)
        {
            if (Food > projectedPopulation)
            {
                resultList.Add(new PlanetUpdateResult(PlanetName,
                    ResultType.PlanetUpdateResultTypeFoodSurplus, Food - projectedPopulation));
                DEBUG_HAD_SURPLUS = true;
            }
        }
        else if (ProjectedFood < projectedPopulation)
        {
            foodShortage += ProjectedFood - projectedPopulation;
        }


        if (foodShortage < 0.0f)
        {
            resultList.Add(new PlanetUpdateResult(PlanetName, ResultType.PlanetUpdateResultTypeFoodShortage,
                foodShortage));
            if(DEBUG_HAD_SURPLUS)
                Debug.LogError($"Planet {PlanetName} had food surplus and shortage");
        }
    }

    private void SetProjectedWorkers(float ProjectedPopulation)
    {
        var projectedFoodGap = Food + (FoodWorkers * _resourceData._foodProduction) - ProjectedPopulation;
        if (projectedFoodGap >= 0.0f)
        {
            // enough food projected, keep worker allocations
            ProjectedFoodWorkers = FoodWorkers;
            ProjectedGrotsitsWorkers = GrotsitsWorkers;
        }
        else
        {
            var projectedFoodWorkersFloat = Math.Ceiling((projectedFoodGap / _resourceData._foodProduction));
            ProjectedFoodWorkers = Math.Clamp(Convert.ToInt32(projectedFoodWorkersFloat), 0, Population); 
            ProjectedFoodWorkers = Math.Clamp(Population - FoodWorkers, 0, Population);
        }
    }

    private void ConsumeGrotsits(List<PlanetUpdateResult> resultList)
    {
        if (Population <= 0 && Grotsits <= _resourceData._initialGrotsits)
            return;

        bool DEBUG_HAD_SURPLUS = false;
        var grotsitsShort = 0.0f;
        if (Grotsits < Population) 
        { 
            // Can't give everyone goods
            grotsitsShort += Grotsits - Population;
            Grotsits = 0.0f;
            Morale = Math.Clamp(Morale - _gameAIConstants.MoraleStep, 0.0f, 200.0f);
        }
        else
        {
            // Grotsits for everyone
            Grotsits -= Population;
            Morale = Math.Clamp(Morale + _gameAIConstants.MoraleStep, 0.0f, 200.0f);
        }

        // add 1 to population here to allow for growth if possible
        var projectedPopulation = Population + (Population < MaxPopulation ? 1 : 0);
        // assumes projected worker already set
        ProjectedGrotsits = Grotsits + (ProjectedGrotsitsWorkers *_resourceData._grotsitProduction);
        if (ProjectedGrotsits >= projectedPopulation)
        {
            if (Grotsits > projectedPopulation)
            {
                resultList.Add(new PlanetUpdateResult(PlanetName,
                    ResultType.PlanetUpdateResultTypeGrotsitsSurplus,
                    Math.Clamp(Grotsits - projectedPopulation, 0, Grotsits)));
            }
            DEBUG_HAD_SURPLUS = true;
        }
        else
        {
            grotsitsShort += ProjectedGrotsits - projectedPopulation;
        }

        if (grotsitsShort < 0.0f)
        {
            resultList.Add(new PlanetUpdateResult(PlanetName, ResultType.PlanetUpdateResultTypeGrotsitsShortage,
                grotsitsShort));
            if(DEBUG_HAD_SURPLUS)
                Debug.LogError($"Planet {PlanetName} had grotsists surplus and shortage");
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
