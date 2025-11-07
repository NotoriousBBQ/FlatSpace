using System;
using System.Collections.Generic;
using System.Linq;
using FlatSpace.AI;
using FlatSpace.Game;
using UnityEngine;


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

        public PlanetUpdateResult(string planetName, ResultType type, object data, int playerID = -1)
        {
            Name = planetName;
            Result = type;
            Data = data;
            PlayerID = playerID;
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
        public readonly int PlayerID;
    }
    public PlanetType Type { get; private set; } = PlanetType.PlanetTypeNormal;
    public List<Inhabitant> Population  = new List<Inhabitant>();
    public int FoodWorkers = 0;
    public int ProjectedFoodWorkers { get; private set; }
    public int GrotsitsWorkers = 0;
    public int ProjectedGrotsitsWorkers { get; private set; }
    public int ResearchWorkers = 0;
    public int ProjectedResearchWorkers { get; private set; }
    public int IndustryWorkers = 0;
    public int ProjectedIndustryWorkers { get; private set; }
    public PlanetStrategy CurrentStrategy { get; set; }
    public float Morale { get; set; }
    public static int NoOwner = -1;
    public static float MaxFoodStorage = 600f;
    public static float MaxGrotsitsStorage = 600f;
    public int Owner = NoOwner;
    public int MaxPopulation => _resourceData._maxPopulation;
    
    public float Food { get; set; } = 0.0f;
    public float ProjectedFood { get; set; } = 0.0f;
    [SerializeField] private float _foodNeededForNewPop = 10.0f;
    public float Grotsits {get; set;}
    public float ProjectedGrotsits {get; set;}
    
    public string PlanetName {get; private set;} = "";
    public Vector2 Position { get; private set; }= new Vector2(0.0f, 0.0f);
    public List<int> IncomingPopulationSource = new List<int>();
    public bool IsPopulationTransferInProgress(int playerID) {return IncomingPopulationSource.Contains(playerID);}
    public bool FoodShipmentIncoming = false;
    public bool GrotsitsShipmentIncoming = false;

    public void SetPopulationTransferInProgress(int playerID, bool inProgress = true)
    {
        if (inProgress)
        {
            if (!IncomingPopulationSource.Contains(playerID))
            {
                IncomingPopulationSource.Add(playerID);
            }
        }
        else
        {
            IncomingPopulationSource.Remove(playerID);
        }
    }
    
    private PlanetResourceData _resourceData = null;
    public Dictionary<string, GameAIMap.DestinationToPathingListEntry> DistanceMapToPathingList;
    public enum PlanetType
    {
        PlanetTypePrime,
        PlanetTypeNormal,
        PlanetTypeFarm,
        PlanetTypeVerdant,
        PlanetTypeIndustrial,
        PlanetTypeDesolate,
        PlanetTypeOcean,
        PlanetTypeDesert
    }

    public enum PlanetStrategy
    {
        PlanetStrategyBalanced,
        PlanetStrategyGrowth,
        PlanetStrategyFood,
        PlanetStrategyFocusedFood,
        PlanetStrategyGrotsits,
        PlanetStrategyFocusedGrotsits,
        PlanetStrategyResearch,
        PlanetStrategyFocusedResearch,
        PlanetStrategyIndustry,
        PlanetStrategyFocusedIndustry

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
        var modifierData = _gameAIConstants.productionModifierLists?[(int)CurrentStrategy];
        if (modifierData != null)
            strategyPopulaitonModifer = modifierData.foodModifier;
        var populationAdjustedForPlanetType = Population.Count * strategyPopulaitonModifer;
        if(populationAdjustedForPlanetType <= 0.0f)
            return false;
        return ResourceWorkerRequirementForPopulation(populationAdjustedForPlanetType, _resourceData._grotsitProduction, out foodWorkers);
    }
    
    private bool GrotsitWorkerRequirementForPopulation(out int grotsitsWorkers)
    {
        grotsitsWorkers = 0;
        var strategyPopulaitonModifer = 1;
        var modifierData = _gameAIConstants.productionModifierLists?[(int)CurrentStrategy];
        if (modifierData != null)
            strategyPopulaitonModifer = modifierData.grotsitsModifier;
        var populationAdjustedForPlanetType = Population.Count * strategyPopulaitonModifer;
        if(populationAdjustedForPlanetType <= 0.0f)
            return false;
        return ResourceWorkerRequirementForPopulation(populationAdjustedForPlanetType, _resourceData._grotsitProduction, out grotsitsWorkers);
    }

    private bool ResearchWorkerRequirementForPopulation(out int researchWorkers)
    {
        researchWorkers = 0;
        var strategyPopulaitonModifer = 1;
        var modifierData = _gameAIConstants.productionModifierLists?[(int)CurrentStrategy];
        if (modifierData != null)
            strategyPopulaitonModifer = modifierData.researchModifier;
        var populationAdjustedForPlanetType = Population.Count * strategyPopulaitonModifer;
        if(populationAdjustedForPlanetType <= 0.0f)
            return false;
        return ResourceWorkerRequirementForPopulation(populationAdjustedForPlanetType, _resourceData._researchProduction, out researchWorkers);
    }

    private bool IndustryWorkerRequirementForPopulation(out int industryWorkers)
    {
        industryWorkers = 0;
        var strategyPopulaitonModifer = 1;
        var modifierData = _gameAIConstants.productionModifierLists?[(int)CurrentStrategy];
        if (modifierData != null)
            strategyPopulaitonModifer = modifierData.researchModifier;
        var populationAdjustedForPlanetType = Population.Count * strategyPopulaitonModifer;
        if(populationAdjustedForPlanetType <= 0.0f)
            return false;
        return ResourceWorkerRequirementForPopulation(populationAdjustedForPlanetType, _resourceData._grotsitProduction, out industryWorkers);
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
            case PlanetStrategy.PlanetStrategyResearch:
                ResearchWorkerRequirementForPopulation(out ResearchWorkers);
                FoodWorkers = Math.Clamp(Population.Count - ResearchWorkers, 0, Population.Count);
                break;
            case PlanetStrategy.PlanetStrategyFocusedResearch:
                ResearchWorkerRequirementForPopulation(out ResearchWorkers);
                FoodWorkers = Math.Clamp(Population.Count - ResearchWorkers, 0, Population.Count);
                break;
            case PlanetStrategy.PlanetStrategyIndustry:
                IndustryWorkerRequirementForPopulation(out IndustryWorkers);
                FoodWorkers = Math.Clamp(Population.Count - IndustryWorkers, 0, Population.Count);
                break;
            case PlanetStrategy.PlanetStrategyFocusedIndustry:
                IndustryWorkerRequirementForPopulation(out IndustryWorkers);
                FoodWorkers = Math.Clamp(Population.Count - IndustryWorkers, 0, Population.Count);
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
        Food = Math.Clamp(Food, 0.0f, MaxFoodStorage);
        // produce grosits
        Grotsits += GrotsitsWorkers * _resourceData._grotsitProduction * (Morale/100.0f);
        Grotsits = Math.Clamp(Grotsits, 0.0f, MaxGrotsitsStorage);
        ConsumeFood(resultList);
        ConsumeGrotsits(resultList);
     
    }

    private void ConsumeFood(List<PlanetUpdateResult> resultList)
    {
        if (Population.Count <= 0 && Food <= _resourceData._initialFood)
            return;

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
                 var playerID = ChangePopulation(-1);
                 // start the food countdown again
                 Food = Population.Count;
                 resultList.Add(new PlanetUpdateResult(PlanetName, ResultType.PlanetUpdateResultTypePopulationLoss,
                     1, playerID));
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
                Food -= _foodNeededForNewPop;
                var playerID = ChangePopulation(1);
                resultList.Add(new PlanetUpdateResult(PlanetName, ResultType.PlanetUpdateResultTypePopulationGain, 1, playerID));
                if (Population.Count >= MaxPopulation)
                    resultList.Add(new PlanetUpdateResult(PlanetName,
                        ResultType.PlanetUpdateResultTypePopulationSurplus,
                        Population.Count - _resourceData._maxPopulation));
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
        }
    }

    private void SetProjectedWorkers(float projectedPopulation)
    {
        var projectedFoodGap = Food + (FoodWorkers * _resourceData._foodProduction) - projectedPopulation;
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
        ProjectedGrotsits = Math.Clamp(Grotsits + (ProjectedGrotsitsWorkers *_resourceData._grotsitProduction), 0.0f, MaxGrotsitsStorage);
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

    // returns the player id of the population change
    private int ChangePopulation(int changeAmount)
    {
        var playerID = NoOwner;
        // first determine which randomly based on pop distribution
        GetPopulationDistribution(out var popDistribution);
        if (popDistribution.Count == 1)
            playerID = popDistribution.First().Key;
        else
        {
            var stack = new int[popDistribution.Count];
            var stackTotal = 0;
            for (var i = 0; i < popDistribution.Count; i++)
            {
                stack[i] = popDistribution[i];
                stackTotal += popDistribution[i];
            }

            var stackPick = GameAI.Rand.Next(stackTotal);
            for (var i = 0; i < popDistribution.Count; i++)
            {
                if (stackPick < stack[i])
                {
                    playerID = i;
                    break;
                }
            }
        }
        // then change that owner's population
        ChangePopulation(changeAmount, playerID);
        return playerID;
    }
    
    

    public void ChangePopulation(int changeAmount, int playerId)
    {
        if (playerId == NoOwner)
            return;
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
        SetPlanetOwnership();
    }

    private void SetPlanetOwnership()
    {
        Owner = PlayerWithMostPopulation();
    }

    private void GetPopulationDistribution(out Dictionary<int, int> popDistribution)
    {
        popDistribution = new Dictionary<int, int>();
        for (var i = 0; i < Gameboard.Instance.players.Count; i++)
        {
            var popByPlayer = Population.FindAll(x => x.Player == i).Count;
            if(popByPlayer > 0)
                popDistribution.Add(i, popByPlayer);
        }
    }

    public float GetPopulationFraction(int playerID)
    {
        GetPopulationDistribution(out var popDistribution);
        if (popDistribution.TryGetValue(playerID, out var playerPop))
            return (float)popDistribution[playerID]/ (float)Population.Count;
        return 0.0f;
    }

    public int PlayerWithMostPopulation()
    {
        GetPopulationDistribution(out var popDistribution);
        
        if (popDistribution.Count == 0)
            return NoOwner;
        if (popDistribution.Count == 1)
            return popDistribution.First().Key;
        
        var sortedPopDict = popDistribution.OrderByDescending(pair => pair.Value);
        return sortedPopDict.ElementAt(0).Value > sortedPopDict.ElementAt(1).Value ? sortedPopDict.ElementAt(0).Key : NoOwner;
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
