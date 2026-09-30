using System;
using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(fileName = "GameAIConstants", menuName = "Scriptable Objects/GameAIConstants")]
public class GameAIConstants : ScriptableObject
{

    public float moraleStep;
    public float defaultTravelSpeed;
    public float expandPopulationTrigger;
    public int maxPathNodesForResourceDistribution;
    public List<ModifierListForPlanetStrategy> productionModifierLists;
    public List<PlanetResourceData> resourceData;
    public ShipData colonyShipData;
    public ShipData warShipData;

    [Header("Ship transport")]
    // Longest trip (path node count) a ship transport may take.
    public int maxPathNodesForShipTransport = 10;
    // Desired garrison (warships) per category. A planet uses the largest of its categories.
    public int garrisonSpecialized = 4;         // category 1: Desert, Industrial, Farm, Ocean
    public int garrisonOuter = 6;               // category 2: colonized with an uncolonized neighbour
    public int garrisonPrime = 4;               // category 3: Prime
    public int garrisonHighTraffic = 2;         // category 4: many connections
    public int garrisonHighlySpecialized = 1;   // category 5: Verdant, Desolate
    // A planet with at least this many neighbours is "high traffic".
    public int highTrafficConnectionCount = 4;
    // Category-5-only planets take ships only once total warships >= this * colonized planet count.
    public float category5UnlockShipsPerColonizedPlanet = 3f;

    [Header("Assault (Consolidate)")]
    // Force committed to a known enemy-occupied planet: ceil(enemy known docked warships x this)...
    public float assaultRatio = 1.5f;
    // ...but never fewer than this.
    public int assaultMinimumShips = 3;

    [Header("Production (Consolidate)")]
    // The Warship production weight is multiplied by 1 + this x (fleet shortfall / wanted fleet), where
    // the wanted fleet is the outer-planet garrisons plus the assault force. 0 disables the boost.
    public float warshipShortfallBoost = 2f;
    // Surplus side: once the fleet passes the wanted size the Warship weight tapers linearly to 0
    // at wanted x this, and Warship is not offered at all from there. Weights are relative (and an
    // all-zero row is picked uniformly), so a zero weight alone would not stop warship production.
    public float warshipFleetCap = 1.5f;
    // Ceiling on the wanted fleet: this x my colonized planets. Without it the wanted fleet (garrisons +
    // assaultRatio x the enemies' known fleets) chases the enemies, who chase mine, and grows without limit.
    public float warshipsPerColonizedPlanet = 8f;

    [Header("Blockade response")]
    // Multiplies the Warship and Update Warship production weights on a planet that is blockaded against its owner (new
    // ships dock where they are built, so building there lifts the blockade). On such a planet the Consolidate fleet-cap
    // cutoff no longer applies: the Warship multiplier is floored at 1 before this boost. Update Warship's existing 0
    // (nothing to upgrade) stays 0.
    public float blockadedWarshipBoost = 3f;

    [Header("Blockade memory")]
    // How many turns a planet where one of a player's own orders was cut stays remembered as blockaded, counted from
    // the last cut (another cut refreshes it; a fresh sighting of the planet without a blockade forgets it early). 0
    // disables the memory. Raised from 10 to 20 after tuning logs showed a lasting blockade outliving a 10-turn memory
    // and re-cutting the same player's colonists each time it lapsed.
    public int blockadeMemoryTurns = 20;

    [Header("Warships")]
    // Each researched Warship improvement a ship carries adds this fraction of the base cost to building it
    // (0.1 with 15 improvements = 2.5x). Update Warship costs base x this x the improvements the ship is missing.
    public float warshipImprovementCostFactor = 0.1f;

    [Header("Improvement upkeep")]
    // Multiplies each improvement's catalog maintenanceCost when it is charged (grotsits per turn, on top of the
    // population's own consumption). Only the BEST tier per resource is charged. 0 switches upkeep off. The catalog
    // values equal the improvements' effect percentages, which are far too high to charge at face value (a replay of
    // a long run put a mid-game planet at 88% and an end-game planet at 245% of its whole grotsits capacity at 1.0).
    public float improvementUpkeepScale = 0.15f;

    [Header("Colonization")]
    // Food a colony ship carries to a planet that produces no food (e.g. Desolate), so the colony survives long
    // enough for the food shipping system to notice its shortage. The origin pays it and must hold at least this
    // much, otherwise that target is not viable from that origin. It is a bridge, not a guarantee: colonies can
    // still fail.
    public float colonyFoodRider = 10f;
    // Longest trip (path node count) a colony ship may take, independent of maxPathNodesForResourceDistribution
    // so an empire's frontier can outrun direct shipping range (a prerequisite for Distribution Centers to have
    // any territory to serve). Requires maxPathNodesForKnowledge to be at least this large, otherwise knowledge
    // becomes the tighter gate again and this constant has no effect.
    public int maxPathNodesForColonization = 6;

    [Header("Knowledge")]
    // PlayerKnowledge.Update grants knowledge out to this many path nodes from each vision source (2 = source +
    // direct neighbours only, the behavior every existing call site keeps by default). Should be >= the largest
    // of maxPathNodesForColonization/maxPathNodesForResourceDistribution, otherwise it becomes the real gate
    // underneath whichever of those is wider.
    public int maxPathNodesForKnowledge = 6;

    [Header("Distribution Centers")]
    // At or above this many colonized planets on the WHOLE BOARD (every player, not just this one), a player's
    // AI may designate one Distribution Center per resource (Food and/or Grotsits).
    public int minPlanetsForDistributionCenters = 30;
    // At or above this many (whole board), up to two DCs per resource. Was left far above any tested board size
    // so only the one-DC-per-resource tier was exercised; the one-DC tier's tuning-log data (test2.json and the
    // larger 4p.json, 100 planets) showed chronic coverage gaps on Desolate/Verdant-type frontier planets even as
    // a single player's territory grew past 30-39 colonized planets, so a second DC is now reachable partway
    // between the first threshold and a board's full-colonization point (~100 on 4p.json).
    public int minPlanetsForSecondDistributionCenter = 60;
    // Flat target stock a Food DC tries to accumulate before its synthetic demand drops to zero. Starting point,
    // not deeply tuned yet — retune via a /tuning-log pass once the mechanism itself is validated.
    public float distributionCenterFoodTargetStock = 50f;
    public float distributionCenterGrotsitsTargetStock = 50f;
}
