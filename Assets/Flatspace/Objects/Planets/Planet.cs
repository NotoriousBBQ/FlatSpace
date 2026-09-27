using System;
using System.Collections.Generic;
using System.Linq;
using FlatSpace.AI;
using FlatSpace.Game;
using Flatspace.Objects.Production;
using Unity.VisualScripting;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering.Universal;
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
            PlanetUpdateResultTypeIndustrySurplus,
            PlanetUpdateResultTypeIndustryProductionComplete,
            PlanetUpdateResultTypeIndustryProductionQueueEmpty,
            PlanetUpdateResultTypeResearchProduced,
            PlanetUpdateResultTypeColonizerReady
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
                case ResultType.PlanetUpdateResultTypeIndustrySurplus:
                case ResultType.PlanetUpdateResultTypeResearchProduced:
                case ResultType.PlanetUpdateResultTypeIndustryProductionQueueEmpty:
                    Priority = ResultPriority.PlanetUpdateResultPriorityMedium;
                    break;
                case ResultType.PlanetUpdateResultTypePopulationLoss:
                    Priority = ResultPriority.PlanetUpdateResultPriorityUrgent;
                    break;
                case ResultType.PlanetUpdateResultTypePopulationMax:
                case ResultType.PlanetUpdateResultTypePopulationSurplus:
                case ResultType.PlanetUpdateResultTypeFoodShortage:
                case ResultType.PlanetUpdateResultTypeGrotsitsShortage:
                case ResultType.PlanetUpdateResultTypeIndustryProductionComplete:
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

    /// <summary>
    /// True for a planet that cannot feed even one colonist by itself (its base food plus one worker's food is below
    /// 1, e.g. Desolate). A colonist there starves the turn it lands, so the colony ship must bring a food rider.
    /// Data-driven: no planet type is named.
    /// </summary>
    public bool NeedsColonyFoodRider => _resourceData._baseFoodProduction + _resourceData._foodProduction < 1f;

    /// <summary>Whether the last turn's grotsits stock could not cover the planet's requirement (population plus upkeep).</summary>
    public bool GrotsitsShort { get; private set; }
    
    public float Food { get; set; } = 0.0f;
    public float FoodProduced { get; set; } = 0.0f;
    public float ProjectedFood { get; set; } = 0.0f;
    [SerializeField] public float FoodNeededForNewPop = 10.0f;
    public float Grotsits {get; set;}
    public float GrotsitsProduced {get; set;}
    public float ProjectedGrotsits {get; set;}
    public float Industry {get; set;}
    public float IndustryProduced {get; set;}
    public float ProjectedIndustry {get; set;}
    
    public float Research {get; set;}
    public float ResearchProduced {get; set;}
    public string PlanetName {get; private set;} = "";
    public Vector2 Position { get; private set; }= new Vector2(0.0f, 0.0f);
    // Names of directly-connected planets for the pathing graph. Empty on a
    // board that predates the random generator => distance-rule fallback.
    public List<string> Connections = new List<string>();
    public List<int> IncomingPopulationSource = new List<int>();
    public bool IsPopulationTransferInProgress(int playerID) {return IncomingPopulationSource.Contains(playerID);}
    public bool FoodShipmentIncoming = false;
    public bool GrotsitsShipmentIncoming = false;

    public List<(string, float)> CompletedImprovements = new List<(string, float)>();
    // The best (highest-tier) improvement built per resource: the only one charged upkeep and the only one whose yield
    // applies. Kept alongside CompletedImprovements, which stays the full list of names.
    private readonly Dictionary<string, (int tier, float maintenance)> _bestImprovement
        = new Dictionary<string, (int tier, float maintenance)>();
    private Dictionary<string, float> ImprovementYieldModifier = new Dictionary<string, float>
    {
        {"Food", 1f},
        {"Industry", 1f},
        {"Research", 1f},
        {"Grotsits", 1f}
    };
    public List<Ship> DockedShips = new List<Ship>();
    
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

    
    public GameAIConstants GameAIConstants { get; set; }

    public void Init(PlanetSpawnData spawnData, Transform parentTransform, GameAIConstants gameAIConstants)
    {
        GameAIConstants = gameAIConstants;
        _resourceData = spawnData._resourceData;
        PlanetName = spawnData._planetName;

        for (var i = 0; i < _resourceData._initialPopulation; i++)
        {
            Population.Add(new Inhabitant());            
        }
        
        Food = ProjectedFood = _resourceData._baseFoodProduction;
        Grotsits = ProjectedGrotsits = _resourceData._baseGrotsitsProduction;
        Position = new Vector2(spawnData._planetPosition.x, spawnData._planetPosition.y);
        Connections = spawnData._connections != null
            ? new List<string>(spawnData._connections)
            : new List<string>();
        Type = spawnData._planetType;
        DistanceMapToPathingList = new Dictionary<string, GameAIMap.DestinationToPathingListEntry>();
        CurrentStrategy = _resourceData._initialStrategy;
        Morale = 100.0f;
    }

    private bool FoodWorkerRequirement(out int foodWorkers)
    {
        foodWorkers = 0;

        var strategyPopulationModifier = 0;
        var modifierData = GameAIConstants.productionModifierLists?[(int)CurrentStrategy];
        if (modifierData != null)
            strategyPopulationModifier = modifierData.foodModifier;
        var populationAdjustedForPlanetType = Population.Count + strategyPopulationModifier;
        if(populationAdjustedForPlanetType <= 0.0f)
            return false;
        if (Food > 3 * populationAdjustedForPlanetType)
            populationAdjustedForPlanetType = Population.Count;
        var productionRate = GetWorkerRate("Food");
        return ResourceWorkerRequirement(populationAdjustedForPlanetType, productionRate, out foodWorkers);
    }

    private float GetMaintenanceCost()
    {
        return Population.Count + GetImprovementMaintenanceCost();
    }

    /// <summary>
    /// Grotsits per turn spent on improvements: the catalog maintenanceCost of the BEST tier built for each resource,
    /// times GameAIConstants.improvementUpkeepScale (0 switches upkeep off). Only the best tier is charged because only
    /// the best tier's yield applies.
    /// </summary>
    public float GetImprovementMaintenanceCost()
    {
        var total = 0f;
        foreach (var best in _bestImprovement.Values)
            total += best.maintenance;
        return total * UpkeepScale();
    }

    private float UpkeepScale() => GameAIConstants != null ? GameAIConstants.improvementUpkeepScale : 0f;

    /// <summary>The highest tier built for a resource; -1 when none.</summary>
    public int GetBestImprovementTier(string subType)
        => _bestImprovement.TryGetValue(subType, out var best) ? best.tier : -1;

    /// <summary>
    /// An improvement whose tier is at or below the best already built for its resource: it would add nothing (the
    /// higher tier's yield already applies), so it is not worth offering. Only improvements can be superseded.
    /// </summary>
    public bool IsImprovementSuperseded(CatalogItem item)
        => item.type == "Improvement" && item.tier <= GetBestImprovementTier(item.subType);

    /// <summary>
    /// The most grotsits this planet could make per turn with every inhabitant staffed at its maximum population:
    /// base production plus maximum population times the grotsits worker rate (improvements included).
    /// </summary>
    public float GetGrotsitsCapacity()
        => _resourceData._baseGrotsitsProduction + MaxPopulation * GetWorkerRate("Grotsits");

    /// <summary>
    /// Can this planet carry the upkeep of building this improvement? True when its maximum population plus the
    /// upkeep afterwards fits within its grotsits capacity (the candidate replaces the same resource's best tier and
    /// adds to every other resource's). A planet whose capacity does not even exceed its own maximum population
    /// (Farm, Verdant) is an importer by data and is exempt: it is meant to run on grotsits shipments. This is the
    /// single seam for per-type import allowances (none for Prime/Normal, small for specialized planets, large for
    /// super-specialized ones).
    /// </summary>
    public bool CanAffordImprovement(CatalogItem item)
    {
        var capacity = GetGrotsitsCapacity();
        if (capacity <= MaxPopulation) return true;

        var upkeepAfter = 0f;
        foreach (var best in _bestImprovement)
            if (best.Key != item.subType) upkeepAfter += best.Value.maintenance;
        var candidate = item.maintenanceCost;
        if (_bestImprovement.TryGetValue(item.subType, out var same))
            candidate = Math.Max(candidate, same.maintenance);
        upkeepAfter = (upkeepAfter + candidate) * UpkeepScale();
        return MaxPopulation + upkeepAfter <= capacity;
    }
    private bool GrotsitsWorkerRequirement(out int grotsitsWorkers)
    {
        grotsitsWorkers = 0;
        var strategyPopulationModifier = 0;
        var modifierData = GameAIConstants.productionModifierLists?[(int)CurrentStrategy];
        if (modifierData != null)
            strategyPopulationModifier = modifierData.grotsitsModifier;
        var grotsitsRequirement = GetMaintenanceCost();
        if(grotsitsRequirement <= 0.0f)
            return false;
        var shortfall = Grotsits - grotsitsRequirement;
        if (shortfall <= 0.0f)
            grotsitsRequirement += -shortfall + strategyPopulationModifier;
        var productionRate = GetWorkerRate("Grotsits");
        return ResourceWorkerRequirement(grotsitsRequirement, productionRate, out grotsitsWorkers);
    }

    private bool ResearchWorkerRequirement(out int researchWorkers)
    {
        researchWorkers = 0;
        var strategyPopulationModifier = 0;
        var modifierData = GameAIConstants.productionModifierLists?[(int)CurrentStrategy];
        if (modifierData != null)
            strategyPopulationModifier = modifierData.researchModifier;
        var populationAdjustedForPlanetType = Population.Count + strategyPopulationModifier;
        if(populationAdjustedForPlanetType <= 0.0f)
            return false;
        var productionRate = GetWorkerRate("Research");
        return ResourceWorkerRequirement(populationAdjustedForPlanetType, productionRate, out researchWorkers);
    }

    private bool IndustryWorkerRequirement(out int industryWorkers)
    {
        industryWorkers = 0;
        var strategyPopulationModifier = 0;
        var modifierData = GameAIConstants.productionModifierLists?[(int)CurrentStrategy];
        if (modifierData != null)
            strategyPopulationModifier = modifierData.industryModifier;
        var populationAdjustedForPlanetType = Population.Count + strategyPopulationModifier;
        var productionRate = GetWorkerRate("Industry");
        return populationAdjustedForPlanetType > 0.0f
               && ResourceWorkerRequirement(populationAdjustedForPlanetType, productionRate, out industryWorkers);
    }

    private bool ResourceWorkerRequirement(float requirement, float productionRate,out int requiredWorkers)
    {
        requiredWorkers = 0;
        if (productionRate <= 0.0f || Morale <= 0.0f)
            return false;
        var moraleModifier = Morale / 100.0f;
        var actualProductionRate = productionRate * moraleModifier;
        requiredWorkers = Convert.ToInt32(Math.Ceiling(requirement / actualProductionRate));
        return true;
    }
    

    private void AssignWorkForStrategy() 
    {
        int remainingWorkers;
        switch (CurrentStrategy)
        {
            case PlanetStrategy.PlanetStrategyBalanced:
                FoodWorkerRequirement(out FoodWorkers);
                GrotsitsWorkerRequirement(out GrotsitsWorkers);
                IndustryWorkerRequirement(out IndustryWorkers);
                ResearchWorkerRequirement(out ResearchWorkers);                
                remainingWorkers = Population.Count - FoodWorkers;
                GrotsitsWorkers = Math.Clamp(GrotsitsWorkers, 0, Math.Max(remainingWorkers, 0));
                remainingWorkers -= GrotsitsWorkers;
                IndustryWorkers = Math.Clamp(IndustryWorkers, 0, Math.Max(remainingWorkers, 0));
                remainingWorkers -= IndustryWorkers;
                ResearchWorkers = Math.Clamp(ResearchWorkers, 0, Math.Max(remainingWorkers, 0));
                remainingWorkers -= ResearchWorkers;
                if (remainingWorkers > 0)
                {
                    var halfRemainingWorkers = remainingWorkers / 2;
                    FoodWorkers += halfRemainingWorkers;
                    IndustryWorkers += halfRemainingWorkers;
                    remainingWorkers -= 2*halfRemainingWorkers;
                    if (remainingWorkers > 0)
                        FoodWorkers += remainingWorkers;
                }

                break;
            case PlanetStrategy.PlanetStrategyGrowth:
                FoodWorkerRequirement(out FoodWorkers);
                GrotsitsWorkerRequirement(out GrotsitsWorkers);
                IndustryWorkerRequirement(out IndustryWorkers);
                ResearchWorkerRequirement(out ResearchWorkers);
                remainingWorkers = Population.Count - FoodWorkers;
                GrotsitsWorkers = Math.Clamp(GrotsitsWorkers, 0, Math.Max(remainingWorkers, 0));
                remainingWorkers -= GrotsitsWorkers;
                IndustryWorkers = Math.Clamp(IndustryWorkers, 0, Math.Max(remainingWorkers, 0));
                remainingWorkers -= IndustryWorkers;
                ResearchWorkers = Math.Clamp(ResearchWorkers, 0, Math.Max(remainingWorkers, 0));
                if(remainingWorkers > 0)
                    FoodWorkers += remainingWorkers;
                break;
            case PlanetStrategy.PlanetStrategyFood:
                FoodWorkerRequirement(out FoodWorkers);
                GrotsitsWorkerRequirement(out GrotsitsWorkers);
                IndustryWorkerRequirement(out IndustryWorkers);
                ResearchWorkerRequirement(out ResearchWorkers);
                remainingWorkers = Population.Count - FoodWorkers;
                GrotsitsWorkers = Math.Clamp(GrotsitsWorkers, 0, Math.Max(remainingWorkers, 0));
                remainingWorkers -= GrotsitsWorkers;
                IndustryWorkers = Math.Clamp(IndustryWorkers, 0, Math.Max(remainingWorkers, 0));
                remainingWorkers -= IndustryWorkers;
                ResearchWorkers = Math.Clamp(ResearchWorkers, 0, Math.Max(remainingWorkers, 0));
                if(remainingWorkers > 0)
                    FoodWorkers += Population.Count - remainingWorkers;
                break;
            case PlanetStrategy.PlanetStrategyFocusedFood:
                FoodWorkerRequirement(out FoodWorkers);
                GrotsitsWorkerRequirement(out GrotsitsWorkers);
                IndustryWorkerRequirement(out IndustryWorkers);
                ResearchWorkerRequirement(out ResearchWorkers);
                remainingWorkers = Population.Count - FoodWorkers;
                GrotsitsWorkers = Math.Clamp(GrotsitsWorkers, 0, Math.Max(remainingWorkers, 0));
                remainingWorkers -= GrotsitsWorkers;
                IndustryWorkers = Math.Clamp(IndustryWorkers, 0, Math.Max(remainingWorkers, 0));
                remainingWorkers -= IndustryWorkers;
                ResearchWorkers = Math.Clamp(ResearchWorkers, 0, Math.Max(remainingWorkers, 0));
                if(remainingWorkers > 0)
                    FoodWorkers += Population.Count - remainingWorkers;
                break;
            case PlanetStrategy.PlanetStrategyGrotsits:
                FoodWorkerRequirement(out FoodWorkers);
                GrotsitsWorkerRequirement(out GrotsitsWorkers);
                IndustryWorkerRequirement(out IndustryWorkers);
                ResearchWorkerRequirement(out ResearchWorkers);
                remainingWorkers = Population.Count - FoodWorkers;
                GrotsitsWorkers = Math.Clamp(GrotsitsWorkers, 0, Math.Max(remainingWorkers, 0));
                remainingWorkers -= GrotsitsWorkers;
                IndustryWorkers = Math.Clamp(IndustryWorkers, 0, Math.Max(remainingWorkers, 0));
                remainingWorkers -= IndustryWorkers;
                ResearchWorkers = Math.Clamp(ResearchWorkers, 0, Math.Max(remainingWorkers, 0));
                if(remainingWorkers > 0)
                    GrotsitsWorkers += remainingWorkers;
                break;
            case PlanetStrategy.PlanetStrategyFocusedGrotsits:
                FoodWorkerRequirement(out FoodWorkers);
                GrotsitsWorkerRequirement(out GrotsitsWorkers);
                IndustryWorkerRequirement(out IndustryWorkers);
                ResearchWorkerRequirement(out ResearchWorkers);
                remainingWorkers = Population.Count - FoodWorkers;
                GrotsitsWorkers = Math.Clamp(GrotsitsWorkers, 0, Math.Max(remainingWorkers, 0));
                remainingWorkers -= GrotsitsWorkers;
                IndustryWorkers = Math.Clamp(IndustryWorkers, 0, Math.Max(remainingWorkers, 0));
                remainingWorkers -= IndustryWorkers;
                ResearchWorkers = Math.Clamp(ResearchWorkers, 0, Math.Max(remainingWorkers, 0));
                if(remainingWorkers > 0)
                    GrotsitsWorkers += remainingWorkers;
                break;
            case PlanetStrategy.PlanetStrategyResearch:
                FoodWorkerRequirement(out FoodWorkers);
                GrotsitsWorkerRequirement(out GrotsitsWorkers);
                IndustryWorkerRequirement(out IndustryWorkers);
                ResearchWorkerRequirement(out ResearchWorkers);
                remainingWorkers = Population.Count - FoodWorkers;
                ResearchWorkers = Math.Clamp(ResearchWorkers, 0, Math.Max(remainingWorkers, 0));
                remainingWorkers -= ResearchWorkers;
                GrotsitsWorkers = Math.Clamp(GrotsitsWorkers, 0, Math.Max(remainingWorkers, 0));
                remainingWorkers -= GrotsitsWorkers;
                IndustryWorkers = Math.Clamp(IndustryWorkers, 0, Math.Max(remainingWorkers, 0));
                if(remainingWorkers > 0)
                    ResearchWorkers += remainingWorkers;
                break;
            case PlanetStrategy.PlanetStrategyFocusedResearch:
                FoodWorkerRequirement(out FoodWorkers);
                GrotsitsWorkerRequirement(out GrotsitsWorkers);
                IndustryWorkerRequirement(out IndustryWorkers);
                ResearchWorkerRequirement(out ResearchWorkers);
                remainingWorkers = Population.Count - FoodWorkers;
                ResearchWorkers = Math.Clamp(ResearchWorkers, 0, Math.Max(remainingWorkers, 0));
                remainingWorkers -= ResearchWorkers;
                GrotsitsWorkers = Math.Clamp(GrotsitsWorkers, 0, Math.Max(remainingWorkers, 0));
                remainingWorkers -= GrotsitsWorkers;
                IndustryWorkers = Math.Clamp(IndustryWorkers, 0, Math.Max(remainingWorkers, 0));
                if(remainingWorkers > 0)
                    ResearchWorkers += remainingWorkers;
                break;
            case PlanetStrategy.PlanetStrategyIndustry:
                FoodWorkerRequirement(out FoodWorkers);
                GrotsitsWorkerRequirement(out GrotsitsWorkers);
                IndustryWorkerRequirement(out IndustryWorkers);
                ResearchWorkerRequirement(out ResearchWorkers);
                remainingWorkers = Population.Count - FoodWorkers;
                IndustryWorkers = Math.Clamp(IndustryWorkers, 0, Math.Max(remainingWorkers, 0));
                remainingWorkers -= IndustryWorkers;
                ResearchWorkers = Math.Clamp(ResearchWorkers, 0, Math.Max(remainingWorkers, 0));
                remainingWorkers -= ResearchWorkers;
                GrotsitsWorkers = Math.Clamp(GrotsitsWorkers, 0, Math.Max(remainingWorkers, 0));
                remainingWorkers -= GrotsitsWorkers;
                if(remainingWorkers > 0)
                    IndustryWorkers += remainingWorkers;
                break;
            case PlanetStrategy.PlanetStrategyFocusedIndustry:
                FoodWorkerRequirement(out FoodWorkers);
                GrotsitsWorkerRequirement(out GrotsitsWorkers);
                IndustryWorkerRequirement(out IndustryWorkers);
                ResearchWorkerRequirement(out ResearchWorkers);
                remainingWorkers = Population.Count - FoodWorkers;
                IndustryWorkers = Math.Clamp(IndustryWorkers, 0, Math.Max(remainingWorkers, 0));
                remainingWorkers -= IndustryWorkers;
                ResearchWorkers = Math.Clamp(ResearchWorkers, 0, Math.Max(remainingWorkers, 0));
                remainingWorkers -= ResearchWorkers;
                GrotsitsWorkers = Math.Clamp(GrotsitsWorkers, 0, Math.Max(remainingWorkers, 0));
                remainingWorkers -= GrotsitsWorkers;
                if(remainingWorkers > 0)
                    IndustryWorkers += remainingWorkers;
                break;
        }
    }

    public void UpdatePlanet(List<PlanetUpdateResult> resultList)
    {
        if (Population.Count == 0)
            return;
        AssignWorkForStrategy();
        // grow food
        FoodProduced = (_resourceData._baseFoodProduction + (FoodWorkers * _resourceData._foodProduction * ImprovementYieldModifier["Food"])) * (Morale/100.0f);
        Food += FoodProduced;
        Food = Math.Clamp(Food, 0.0f, MaxFoodStorage);
        // produce grotsits
        GrotsitsProduced = (_resourceData._baseGrotsitsProduction + (GrotsitsWorkers * _resourceData._grotsitsProduction * ImprovementYieldModifier["Grotsits"])) * (Morale/100.0f);
        Grotsits += GrotsitsProduced;
        Grotsits = Math.Clamp(Grotsits, 0.0f, MaxGrotsitsStorage);
        // produce industry
        IndustryProduced =  (_resourceData._baseIndustryProduction +(IndustryWorkers * _resourceData._industryProduction * ImprovementYieldModifier["Industry"])) * (Morale/100.0f);
        Industry += IndustryProduced;
        // produce research
        ResearchProduced = (_resourceData._baseResearchProduction + (ResearchWorkers * _resourceData._researchProduction * ImprovementYieldModifier["Research"])) * (Morale/100.0f);
        Research += ResearchProduced;         
        ConsumeFood(resultList);
        ConsumeGrotsits(resultList);
        ConsumeIndustry(resultList);
        ConsumeResearch(resultList);
        CheckColonizationReady(resultList);
    }
    
    private void CheckColonizationReady(List<PlanetUpdateResult> resultList,bool populationDecrease = false)
    {
        
        if (resultList.Any(x =>
                x.Name == PlanetName && x.Result == ResultType.PlanetUpdateResultTypeColonizerReady))
            return;
        
        if (HasDockedShip(Ship.ShipKind.ColonyShip) && (Population.Count >= MaxPopulation * GameAIConstants.expandPopulationTrigger))
        {
            resultList.Add(new PlanetUpdateResult(PlanetName, ResultType.PlanetUpdateResultTypeColonizerReady,
                1, Owner));
        }
    }

    private void ConsumeFood(List<PlanetUpdateResult> resultList)
    {
        if (Population.Count <= 0 && Food <= _resourceData._baseFoodProduction)
            return;

        float foodShortage = 0.0f;
        if (Food < Population.Count) 
        { 
            // can't feed everyone
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
                     // planet is dead
                     resultList.Add(new PlanetUpdateResult(PlanetName, ResultType.PlanetUpdateResultTypeDead, null));
                 }
             }
        }
        else
        {
            // feed everybody
            Food -= Population.Count;
            // enough to grow?
            if (Food >= FoodNeededForNewPop && Population.Count < MaxPopulation)
            {
                Food -= FoodNeededForNewPop;
                var playerID = ChangePopulation(1);
                resultList.Add(new PlanetUpdateResult(PlanetName, ResultType.PlanetUpdateResultTypePopulationGain, 1, playerID));
                if (Population.Count >= MaxPopulation)
                    resultList.Add(new PlanetUpdateResult(PlanetName,
                        ResultType.PlanetUpdateResultTypePopulationSurplus,
                        Population.Count - _resourceData._maxPopulation, playerID));
            }
            else if (Population.Count >= MaxPopulation)
            {
                resultList.Add(new PlanetUpdateResult(PlanetName,
                    ResultType.PlanetUpdateResultTypePopulationMax, 1, Owner));
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
                    ResultType.PlanetUpdateResultTypeFoodSurplus, Food - projectedPopulation, Owner));
            }
        }
        else if (ProjectedFood < projectedPopulation)
        {
            foodShortage += ProjectedFood - projectedPopulation;
        }


        if (foodShortage < 0.0f)
        {
            resultList.Add(new PlanetUpdateResult(PlanetName, ResultType.PlanetUpdateResultTypeFoodShortage,
                foodShortage, Owner));
        }
    }

    private void SetProjectedWorkers(float projectedPopulation)
    {
        var projectedFoodGap = Food + _resourceData._baseFoodProduction + (FoodWorkers * _resourceData._foodProduction) - projectedPopulation;
        if (projectedFoodGap >= 0.0f)
        {
            // enough food projected, keep worker allocations
            ProjectedFoodWorkers = FoodWorkers;
            ProjectedGrotsitsWorkers = GrotsitsWorkers;
        }
        else
        {
            var projectedFoodWorkersFloat =  _resourceData._baseFoodProduction + _resourceData._foodProduction > 0.0f ? 
                Math.Ceiling((projectedFoodGap / _resourceData._foodProduction))
                : 0;
            ProjectedFoodWorkers = Math.Clamp(Convert.ToInt32(projectedFoodWorkersFloat), 0, Population.Count); 
            ProjectedFoodWorkers = Math.Clamp(Population.Count - FoodWorkers, 0, Population.Count);
        }
    }

    private void ConsumeGrotsits(List<PlanetUpdateResult> resultList)
    {
        if (Population.Count <= 0 && Grotsits <= _resourceData._baseGrotsitsProduction)
            return;

        var grotsitsShort = 0.0f;
        var grotsitsRequired = GetMaintenanceCost();
        GrotsitsShort = Grotsits < grotsitsRequired;   // for the tuning log's economy line
        if (Grotsits < grotsitsRequired)
        { 
            // Can't give everyone goods
            grotsitsShort += Grotsits - grotsitsRequired;
            Grotsits = 0.0f;
            Morale = Math.Clamp(Morale - GameAIConstants.moraleStep, 0.0f, 200.0f);
        }
        else
        {
            // Grotsits for everyone
            Grotsits -= grotsitsRequired;
            Morale = Math.Clamp(Morale + GameAIConstants.moraleStep, 0.0f, 200.0f);
        }

        // add 1 to population here to allow for growth if possible
        var projectedPopulation = Population.Count + (Population.Count < MaxPopulation ? 1 : 0);
        // assumes projected worker already set
        ProjectedGrotsits = Math.Clamp(Grotsits + _resourceData._baseGrotsitsProduction + (ProjectedGrotsitsWorkers *_resourceData._grotsitsProduction), 0.0f, MaxGrotsitsStorage);
        var projectedGrotsitsRequirement = grotsitsRequired + projectedPopulation;
        if (ProjectedGrotsits >= projectedGrotsitsRequirement)
        {
            grotsitsShort += ProjectedGrotsits - projectedGrotsitsRequirement;
            if (Grotsits > projectedGrotsitsRequirement)
            {
                resultList.Add(new PlanetUpdateResult(PlanetName,
                    ResultType.PlanetUpdateResultTypeGrotsitsSurplus,
                    Math.Clamp(Grotsits - projectedGrotsitsRequirement, 0, Grotsits), Owner));
            }

        }
        else
        {
            grotsitsShort += ProjectedGrotsits - projectedPopulation;
        }

        if (grotsitsShort < 0.0f)
        {
            resultList.Add(new PlanetUpdateResult(PlanetName, ResultType.PlanetUpdateResultTypeGrotsitsShortage,
                grotsitsShort, Owner));
        }
    }

    private void ConsumeIndustry(List<PlanetUpdateResult> resultList)
    {
        UpdateProduction(resultList);

        if (Industry >= 0.0f)
        {
            resultList.Add(new PlanetUpdateResult(PlanetName, ResultType.PlanetUpdateResultTypeIndustrySurplus,
                Industry, Owner));
        }
    }

    private void ConsumeResearch(List<PlanetUpdateResult> resultList)
    {
        if (Research > 0)
            resultList.Add(new PlanetUpdateResult(PlanetName, ResultType.PlanetUpdateResultTypeResearchProduced,
                Research, Owner));
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
            var stack = new int[Gameboard.Instance.NumPlayers];
            var stackTotal = 0;
            foreach (var key in popDistribution.Keys)
            {
                stack[key] = popDistribution[key];
                stackTotal += popDistribution[key];
                    
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
        popDistribution = Population
            .GroupBy(x => x.Player)
            .ToDictionary(g => g.Key, g => g.Count());
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

    #region ProductionQueue
    
    public struct ProductionItem
    {
        public float Progress;
        public CatalogItem Item;

        public ProductionItem(CatalogItem item)
        {
            Item = item;
            Progress = 0.0f;
        }

    }
    
    public ProductionItem? CurrentProduction { get; set; } = null;
    public List<ProductionItem> ProductionQueue = new List<ProductionItem>();

    public void ScheduleProductionItem(CatalogItem productionItem)
    {
        ProductionQueue.Add(new ProductionItem(productionItem));
        if (CurrentProduction == null)
        {
            UpdateProduction();
        }
    }
    private bool UpdateProductionQueue(List<PlanetUpdateResult> resultList = null)
    {
        if (CurrentProduction == null)
        {
            if (ProductionQueue.Count > 0)
            {
                var nextProduction = ProductionQueue.First();
                nextProduction.Progress = Industry;
                Industry = 0f;
                CurrentProduction = nextProduction;
                ProductionQueue.RemoveAt(0);
            }

            if(ProductionQueue.Count == 0)
            {
                resultList?.Add(new PlanetUpdateResult(PlanetName,
                    ResultType.PlanetUpdateResultTypeIndustryProductionQueueEmpty, CurrentProduction?.Item.itemName, Owner));
                return false;
            }
        }
        return true; 
    }
    private void UpdateProduction(List<PlanetUpdateResult> resultList = null)
    {
        if (UpdateProductionQueue(resultList))
        {
            ContinueProduction(resultList);
        }
    }

    private void ContinueProduction(List<PlanetUpdateResult> resultList = null)
    {
        if(!CurrentProduction.HasValue)
            return;
        var currentProduction =  CurrentProduction.Value;
        currentProduction.Progress += Industry;
        Industry = 0f;
        CurrentProduction = currentProduction;
        if (CurrentProduction?.Progress >= CurrentProduction?.Item.cost)
        {
            CompleteProduction(resultList);
        }
    }

    private void CompleteProduction(List<PlanetUpdateResult> resultList = null)
    {
        StageCompletedProductionItem(resultList);
        resultList?.Add(new PlanetUpdateResult(PlanetName,
            ResultType.PlanetUpdateResultTypeIndustryProductionComplete, CurrentProduction?.Item.itemName, Owner));
        var excessIndustry = CurrentProduction?.Progress - CurrentProduction?.Item.cost;
        Industry = excessIndustry ?? 0.0f;
        CurrentProduction = null;
        if (UpdateProductionQueue(resultList))
            ContinueProduction(resultList);
    }

    private void StageCompletedProductionItem(List<PlanetUpdateResult> resultList)
    {
        if (CurrentProduction == null) return;
        if (CurrentProduction?.Item.type == "Improvement")
        {
            AddActiveImprovement(CurrentProduction);
        }
        else if (CurrentProduction?.Item.subType == "ColonyShip")
        {
            DockNewShip(Ship.ShipKind.ColonyShip);
        }
        else if (CurrentProduction?.Item.subType == "Warship")
        {
            DockNewShip(Ship.ShipKind.WarShip);
        }
    }

    public bool HasDockedShip(Ship.ShipKind kind)
    {
        return DockedShips.Exists(s => s.Kind == kind);
    }

    private Ship CreateShip(Ship.ShipKind kind, int owner)
    {
        var template = kind == Ship.ShipKind.ColonyShip
            ? GameAIConstants.colonyShipData
            : GameAIConstants.warShipData;
        var ship = this.AddComponent<Ship>();
        ship.Kind = kind;
        ship.Owner = owner;
        ship.Template = template;
        return ship;
    }

    public void DockNewShip(Ship.ShipKind kind)
    {
        DockShipRebuiltSnapshot(kind, Owner);
    }

    // Docks a ship for `owner` whose snapshot is rebuilt from that owner's CURRENT research.
    // Used for newly built ships and as the fallback for a fleet that arrives without a snapshot.
    // Needs Gameboard.Instance, so self-checks must not call it.
    public void DockShipRebuiltSnapshot(Ship.ShipKind kind, int owner)
    {
        var ship = CreateShip(kind, owner);
        ship.ResearchSnapshot = BuildResearchSnapshot(kind, owner);
        DockedShips.Add(ship);
    }

    public void DockShipFromSave(Ship.ShipKind kind, int owner, List<string> researchSnapshot)
    {
        var ship = CreateShip(kind, owner);
        ship.ResearchSnapshot = new List<string>(researchSnapshot);
        DockedShips.Add(ship);
    }

    public bool UndockShip(Ship.ShipKind kind)
    {
        var ship = DockedShips.Find(s => s.Kind == kind);
        if (ship == null) return false;
        DockedShips.Remove(ship);
        DestroyShipComponent(ship);
        return true;
    }

    // Snapshots of `count` docked ships of this kind and owner, in dock order, after skipping the
    // first `skip`. UndockShips removes the first ships, so a fleet's payload matches the ships that
    // leave; `skip` is for a second fleet leaving the same planet the same turn, whose departure
    // order runs after the first fleet's ships are already gone.
    public List<List<string>> PeekShipSnapshots(Ship.ShipKind kind, int owner, int count, int skip = 0)
    {
        return DockedShips
            .Where(s => s.Kind == kind && s.Owner == owner)
            .Skip(skip)
            .Take(count)
            .Select(s => new List<string>(s.ResearchSnapshot))
            .ToList();
    }

    public int UndockShips(Ship.ShipKind kind, int owner, int count)
    {
        var toRemove = DockedShips
            .Where(s => s.Kind == kind && s.Owner == owner)
            .Take(count)
            .ToList();
        foreach (var ship in toRemove)
        {
            DockedShips.Remove(ship);
            DestroyShipComponent(ship);
        }
        return toRemove.Count;
    }

    // Destroy() is illegal outside Play mode (self-checks run in the Editor).
    private static void DestroyShipComponent(Ship ship)
    {
        if (Application.isPlaying) Destroy(ship);
        else DestroyImmediate(ship);
    }

    // Ships of each kind and owning player currently in flight toward this planet (planned but not
    // yet arrived). Per player: fleets can now target another player's planet, so a shared counter
    // would let one player's fleet distort another's garrison and assault arithmetic.
    private readonly Dictionary<(Ship.ShipKind kind, int owner), int> _incomingShips
        = new Dictionary<(Ship.ShipKind kind, int owner), int>();
    public int GetIncomingShips(Ship.ShipKind kind, int owner)
        => _incomingShips.TryGetValue((kind, owner), out var n) ? n : 0;
    public void AddIncomingShips(Ship.ShipKind kind, int owner, int delta)
        => _incomingShips[(kind, owner)] = System.Math.Max(0, GetIncomingShips(kind, owner) + delta);
    public void ClearIncomingShips() => _incomingShips.Clear();

    private List<string> BuildResearchSnapshot(Ship.ShipKind kind, int owner)
    {
        if (owner < 0 || owner >= Gameboard.Instance.players.Count) return new List<string>();
        var subType = kind == Ship.ShipKind.ColonyShip ? "ColonyShip" : "Warship";
        var researchCatalog = Gameboard.Instance.players[owner].playerAI.ResearchCatalog;
        return researchCatalog.catalogItems
            .Where(x => x.researched && x.type == "Ship Improvement" && x.subType == subType)
            .Select(x => x.itemName)
            .ToList();
    }

    private void AddActiveImprovement(ProductionItem? production)
    {
        if (production == null) return;
        RecordImprovement(production.Value.Item);
    }

    /// <summary>
    /// Records a finished improvement: remembers its name (production choices and saves use the list), applies its
    /// yield (never lowering it) and tracks the best tier per resource for upkeep. Also used to restore improvements
    /// when a save is loaded.
    /// </summary>
    public void RecordImprovement(CatalogItem item)
    {
        CompletedImprovements.Add((item.itemName, item.maintenanceCost));
        ApplyImprovementYield(item.subType, Convert.ToSingle(item.effect));
        if (!_bestImprovement.TryGetValue(item.subType, out var best) || item.tier >= best.tier)
            _bestImprovement[item.subType] = (item.tier, item.maintenanceCost);
    }

    /// <summary>
    /// The per-worker production rate for a resource ("Food", "Grotsits", "Research", "Industry"): that resource's
    /// own production rate from the planet type's resource data times its improvement yield; 0 for anything else.
    /// Used by the worker-requirement calculations.
    /// </summary>
    public float GetWorkerRate(string resource)
    {
        float rate;
        switch (resource)
        {
            case "Food":     rate = _resourceData._foodProduction; break;
            case "Grotsits": rate = _resourceData._grotsitsProduction; break;
            case "Research": rate = _resourceData._researchProduction; break;
            case "Industry": rate = _resourceData._industryProduction; break;
            default:         return 0f;
        }
        return rate * GetImprovementYield(resource);
    }

    /// <summary>The yield modifier for a resource (1 = no improvement); 1 for an unknown subType.</summary>
    public float GetImprovementYield(string subType)
        => ImprovementYieldModifier.TryGetValue(subType, out var yield) ? yield : 1f;

    /// <summary>
    /// Applies an improvement's percent yield. Never lowers the current modifier: every researched tier
    /// stays on offer, so a planet can finish a lower tier after a higher one, and a plain assignment
    /// would silently give the higher tier's yield back.
    /// </summary>
    public void ApplyImprovementYield(string subType, float effectPercent)
    {
        var yieldImprovement = 1f + effectPercent / 100f;
        ImprovementYieldModifier[subType] = Math.Max(GetImprovementYield(subType), yieldImprovement);
    }

    #endregion
}
