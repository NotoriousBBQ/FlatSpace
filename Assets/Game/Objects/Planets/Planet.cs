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
    public struct Inhabitant
    {
        public int Player;
    }
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
    public List<Inhabitant> Population  = new List<Inhabitant>();
    public int FoodWorkers = 0;
    public int ProjectedFoodWorkers { get; private set; }
    public int GrotsitsWorkers = 0;
    public int ProjectedGrotsitsWorkers { get; private set; }
    public PlanetStrategy CurrentStrategy { get; set; }
    public float Morale { get; set; }
    public static int NoOwner = -1;
    public int Owner = NoOwner;
    public int MaxPopulation => _resourceData._maxPopulation;
    
    public float Food { get; set; } = 0.0f;
    public float ProjectedFood { get; set; } = 0.0f;
    [SerializeField] private float _foodNeededForNewPop = 10.0f;
    public float Grotsits {get; set;}
    public float ProjectedGrotsits {get; set;}
    
    public string PlanetName {get; private set;} = "";
    public Vector2 Position { get; private set; }= new Vector2(0.0f, 0.0f);
    public bool PopulationTransferInProgress {get; set;}
    
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

        for (var i = 0; i < _resourceData._initialPopulation; i++)
        {
            Population.Add(new Inhabitant());            
        }
        
        Food = ProjectedFood = _resourceData._initialFood;
        Grotsits = ProjectedGrotsits = _resourceData._initialGrotsits;
        Position = new Vector2(spawnData._planetPosition.x, spawnData._planetPosition.y);
        Type = spawnData._planetType;
        DistanceMapToPathingList = new Dictionary<string, GameAIMap.DestinationToPathingListEntry>();
        CurrentStrategy = _resourceData._initialStrategy;
        Morale = 100.0f;
    }

    private bool FoodWorkerRequirementForPopulation(out int foodWorkers)
    {
        foodWorkers = 0;

        var strategyPopulaitonModifer = 1;
        var modifierData = _gameAIConstants.foodWorkerAdjustment.Find(x => x.planetStrategy == CurrentStrategy);
        if (modifierData != null)
            strategyPopulaitonModifer = modifierData.modifier;
        var populationAdjustedForPlanetType = Population.Count * strategyPopulaitonModifer;
        if(populationAdjustedForPlanetType <= 0.0f)
            return false;
        return ResourceWorkerRequirementForPopulation(populationAdjustedForPlanetType, _resourceData._grotsitProduction, out foodWorkers);
    }
    
    private bool GrotsitWorkerRequirementForPopulation(out int grotsitsWorkers)
    {
        grotsitsWorkers = 0;
        var strategyPopulaitonModifer = 1;
        var modifierData = _gameAIConstants.grotsitWorkerAdjustment.Find(x => x.planetStrategy == CurrentStrategy);
        if (modifierData != null)
            strategyPopulaitonModifer = modifierData.modifier;
        var populationAdjustedForPlanetType = Population.Count * strategyPopulaitonModifer;
        if(populationAdjustedForPlanetType <= 0.0f)
            return false;
        return ResourceWorkerRequirementForPopulation(populationAdjustedForPlanetType, _resourceData._grotsitProduction, out grotsitsWorkers);
    }

    private bool ResourceWorkerRequirementForPopulation(int population, float productionRate,out int requiredWorkers)
    {
        requiredWorkers = 0;
        if (productionRate <= 0.0f || Morale <= 0.0f)
            return false;
        var moraleModifier = Morale / 100.0f;
        var actualProductionRate = productionRate * moraleModifier;
        requiredWorkers = Convert.ToInt32(Math.Ceiling(population / actualProductionRate));
        // note this clamp is for the planets population, not the param 'population'
        Math.Clamp(requiredWorkers, 0, Population.Count);
        return true;
    }
    

    private void AssignWorkForStrategy()
    {
        int remainingWorkers;
        switch (CurrentStrategy)
        {
            case PlanetStrategy.PlanetStrategyBalanced:
                // first, make sure the basics are covered
                // always err on the side of more food
                FoodWorkerRequirementForPopulation(out FoodWorkers);
                // fopr grotsits, use the existing first
                GrotsitWorkerRequirementForPopulation(out GrotsitsWorkers);
                Math.Clamp(GrotsitsWorkers, 0, Math.Max(Population.Count - FoodWorkers, 0));
                // the even out the rest
                remainingWorkers = Population.Count - (FoodWorkers + GrotsitsWorkers);
                if (remainingWorkers > 0)
                {
                    FoodWorkers += remainingWorkers / 2;
                    GrotsitsWorkers += remainingWorkers / 2;
                    remainingWorkers = Population.Count -(FoodWorkers + GrotsitsWorkers);
                    if (remainingWorkers > 0)
                        FoodWorkers += remainingWorkers;
                }

                break;
            case PlanetStrategy.PlanetStrategyGrowth:
                FoodWorkerRequirementForPopulation(out FoodWorkers);
                GrotsitWorkerRequirementForPopulation(out GrotsitsWorkers);
                GrotsitsWorkers = Math.Clamp(GrotsitsWorkers, 0, Math.Max(Population.Count - FoodWorkers, 0));
                break;
            case PlanetStrategy.PlanetStrategyFood:
                FoodWorkerRequirementForPopulation(out FoodWorkers);
                GrotsitsWorkers = Math.Clamp(Population.Count - FoodWorkers, 0, Population.Count);
                break;
            case PlanetStrategy.PlanetStrategyFocusedFood:
                FoodWorkerRequirementForPopulation(out FoodWorkers);
                GrotsitsWorkers = Math.Clamp(Population.Count - FoodWorkers, 0, Population.Count);
                break;
            case PlanetStrategy.PlanetStrategyGrotsits:
                GrotsitWorkerRequirementForPopulation(out GrotsitsWorkers);
                FoodWorkers = Math.Clamp(Population.Count - GrotsitsWorkers, 0, Population.Count);
                break;
            case PlanetStrategy.PlanetStrategyFocusedGrotsits:
                GrotsitWorkerRequirementForPopulation(out GrotsitsWorkers);
                FoodWorkers = Math.Clamp(Population.Count - GrotsitsWorkers, 0, Population.Count);
                break;
        }
    }

    public void PlanetProductionUpdate(List<PlanetUpdateResult> resultList)
    {
        if (Population.Count == 0)
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
        if (Population.Count <= 0 && Food <= _resourceData._initialFood)
            return;
        bool DEBUG_HAD_SURPLUS = false;
        float foodShortage = 0.0f;
        if (Food < Population.Count) 
        { 
            // cant feed everyong
            foodShortage = Food - Population.Count;
            //hinky code to prevent mass dieoffs
             Food--;
             if (Food <= 0.0f)
             {
                 // lose a pop
                 ChangePopulation(-1, Owner);
                 // start the food countdown again
                 Food = Population.Count;
                 resultList.Add(new PlanetUpdateResult(PlanetName, ResultType.PlanetUpdateResultTypePopulationLoss,
                     1));
                 if (Population.Count <= 0)
                 {
                     // planet id dead
                     resultList.Add(new PlanetUpdateResult(PlanetName, ResultType.PlanetUpdateResultTypeDead, null));
                 }
             }
        }
        else
        {
            // feed everybody
            Food -= Population.Count;
            // enough to grow?
            if (Food >= _foodNeededForNewPop && Population.Count < MaxPopulation)
            {
                resultList.Add(new PlanetUpdateResult(PlanetName, ResultType.PlanetUpdateResultTypePopulationGain, 1));
                Food -= _foodNeededForNewPop;
                ChangePopulation(1, Owner);
                if (Population.Count >= MaxPopulation)
                    resultList.Add(new PlanetUpdateResult(PlanetName,
                        ResultType.PlanetUpdateResultTypePopulationSurplus, Population.Count - _resourceData._maxPopulation));
            }
            else if (Population.Count >= MaxPopulation)
            {
                resultList.Add(new PlanetUpdateResult(PlanetName,
                    ResultType.PlanetUpdateResultTypePopulationMax, 1));
            }
        }

        // add 1 to population here to allow for growth if possible
        var projectedPopulation = Population.Count + (Population.Count < MaxPopulation ? 1 : 0);
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
            var projectedFoodWorkersFloat =  _resourceData._foodProduction > 0.0f ? 
                Math.Ceiling((projectedFoodGap / _resourceData._foodProduction))
                : 0;
            ProjectedFoodWorkers = Math.Clamp(Convert.ToInt32(projectedFoodWorkersFloat), 0, Population.Count); 
            ProjectedFoodWorkers = Math.Clamp(Population.Count - FoodWorkers, 0, Population.Count);
        }
    }

    private void ConsumeGrotsits(List<PlanetUpdateResult> resultList)
    {
        if (Population.Count <= 0 && Grotsits <= _resourceData._initialGrotsits)
            return;

        var grotsitsShort = 0.0f;
        if (Grotsits < Population.Count) 
        { 
            // Can't give everyone goods
            grotsitsShort += Grotsits - Population.Count;
            Grotsits = 0.0f;
            Morale = Math.Clamp(Morale - _gameAIConstants.moraleStep, 0.0f, 200.0f);
        }
        else
        {
            // Grotsits for everyone
            Grotsits -= Population.Count;
            Morale = Math.Clamp(Morale + _gameAIConstants.moraleStep, 0.0f, 200.0f);
        }

        // add 1 to population here to allow for growth if possible
        var projectedPopulation = Population.Count + (Population.Count < MaxPopulation ? 1 : 0);
        // assumes projected worker already set
        ProjectedGrotsits = Grotsits + (ProjectedGrotsitsWorkers *_resourceData._grotsitProduction);
        if (ProjectedGrotsits >= projectedPopulation)
        {
            grotsitsShort += ProjectedGrotsits - projectedPopulation;
            if (Grotsits > projectedPopulation)
            {
                resultList.Add(new PlanetUpdateResult(PlanetName,
                    ResultType.PlanetUpdateResultTypeGrotsitsSurplus,
                    Math.Clamp(Grotsits - projectedPopulation, 0, Grotsits)));
            }

        }
        else
        {
            grotsitsShort += ProjectedGrotsits - projectedPopulation;
        }

        if (grotsitsShort < 0.0f)
        {
            resultList.Add(new PlanetUpdateResult(PlanetName, ResultType.PlanetUpdateResultTypeGrotsitsShortage,
                grotsitsShort));
        }
    }

    public void ChangePopulation(int changeAmount, int playerId)
    {
        if (changeAmount >= 0)
        {
            for (var i = 0; i < changeAmount; i++)
            {
                Population.Add(new Planet.Inhabitant { Player = playerId });
            }
        }
        else
        {
            for (var i = 0; i < -changeAmount; i++)
            {
                Population.Remove(Population.Find(x => x.Player == playerId));
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
