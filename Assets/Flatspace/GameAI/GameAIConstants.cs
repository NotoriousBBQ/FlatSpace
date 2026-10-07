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
    public int garrisonHighTraffic = 2;         // category 4: chokepoint (see chokepointPercentile)
    public int garrisonHighlySpecialized = 1;   // category 5: Verdant, Desolate
    // Category-5-only planets take ships only once total warships >= this * colonized planet count.
    public float category5UnlockShipsPerColonizedPlanet = 3f;

    [Header("Connectivity (chokepoints)")]
    // A planet whose shortest-path betweenness percentile (0..1, see PlanetCentrality) is at least this is a chokepoint:
    // garrison category 4, and under Consolidate it keeps a garrison. A value above 1 means no planet qualifies.
    public float chokepointPercentile = 0.9f;
    // Consolidate colonization: a target's choice cost is its route cost / (1 + this x its chokepoint percentile), so a
    // hub may be farther and still win. 0 (or below) switches the tilt off; Expand never uses it.
    public float colonizationChokepointWeight = 0.5f;

    [Header("Assault (Consolidate)")]
    // Force committed to a known enemy-occupied planet: ceil(enemy known docked warships x this)...
    public float assaultRatio = 1.5f;
    // ...but never fewer than this.
    public int assaultMinimumShips = 3;

    [Header("Production (Consolidate)")]
    // The Warship production weight is multiplied by 1 + this x (fleet shortfall / wanted fleet), where
    // the wanted fleet is the outer-planet and chokepoint garrisons plus the assault force. 0 disables the boost.
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

    [Header("Blockade breaking (Consolidate)")]
    // A planet that cut one of my orders within this many turns ranks first among blockade-breaking targets (after the
    // planet where I already have the most offense committed). Read from the player's BlockadeMemory.
    public int blockadeTargetRecentTurns = 5;
    // The force sent to a blockaded planet is value x (1 + this) offense, so any small overshoot clears the blockade (a
    // blockade only counts while its value is strictly positive) and float edges or a blocker that adds a ship do not
    // leave it standing.
    public float blockadeBreakMargin = 0.1f;
    // Multiplies the research weight of Warship Offense items while a blockade against the player is visible (1 disables).
    // Blockade value is offense against offense; Health and Defense do nothing until ship combat exists.
    public float blockadedOffenseResearchBoost = 2f;

    [Header("Blockade memory")]
    // How many turns a planet where one of a player's own orders was cut stays remembered as blockaded, counted from
    // the last cut (another cut refreshes it; a fresh sighting of the planet without a blockade forgets it early). 0
    // disables the memory. Raised from 10 to 20 after tuning logs showed a lasting blockade outliving a 10-turn memory
    // and re-cutting the same player's colonists each time it lapsed.
    public int blockadeMemoryTurns = 20;
    // Resource shipping through unavoidable blockades: a (source, shortage) pair is refused when the shipment would deliver
    // less than this share of what the origin pays (amount - blockade loss < fraction x amount), on top of the plain rule that
    // the loss must stay below the amount. 0 = the plain rule; 1 = clean routes only. Tuning logs on test2.json and 4p.json
    // showed lossy shipments delivering ~12% of what they cost (e.g. 11 sent into a blockade of 10), hence 0.5. Read through
    // PlayerAI.MinDeliveredFractionFor, the seam for a per-target (high value planet) threshold.
    public float shipmentMinDeliveredFraction = 0.5f;

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
    // A colonist already in flight whose route now crosses a blockade is detoured to its own target first (ColonistRedirect).
    // When a diversion to another target is also available, the detour is declined for it if the detour's real route cost is
    // more than this many times the diversion's real route cost: long detours stay exposed to later blockades (on 4p.json 79% of
    // them never arrived against 11% of diversions). 0 (or below) = always detour first. With no diversion candidate the
    // detour is always taken.
    public float colonistDetourDivertRatio = 2f;

    [Header("Knowledge")]
    // PlayerKnowledge.Update grants knowledge out to this many path nodes from each vision source (2 = source +
    // direct neighbours only, the behavior every existing call site keeps by default). Should be >= the largest
    // of maxPathNodesForColonization/maxPathNodesForResourceDistribution, otherwise it becomes the real gate
    // underneath whichever of those is wider.
    public int maxPathNodesForKnowledge = 6;

    [Header("Diplomacy")]
    // False = today's behaviour: every rival with contact is an enemy, no stance matrix, no Amass switch. GameAI.InitGameAI
    // copies this onto GameAIMap.Diplomacy.Enabled; a GameAIMap a self-check builds directly stays in that legacy mode.
    public bool diplomacyEnabled = true;
    // Hostility toward a rival grows each turn by: its blockade cuts on my orders x hostilityPerCut, its warships docked on or
    // beside my planets x hostilityPerNearShip, and hostilityStrengthWeight x log2(my fleet strength / its visible fleet
    // strength), clamped to +-2 (a stronger player drifts toward war). It then decays by hostilityDecay a turn and is clamped
    // to 0..hostilityMax.
    public float hostilityPerCut = 5f;
    public float hostilityPerNearShip = 0.5f;
    public float hostilityStrengthWeight = 1f;
    public float hostilityDecay = 0.05f;
    public float hostilityMax = 100f;
    // War probability = 1 / (1 + e^(-(hostility - stanceMidpoint) / stanceSteepness)). The stance held now has its weight
    // multiplied by stanceStickiness, and a stance is not reconsidered for stanceHoldTurns turns after it changes.
    public float stanceMidpoint = 30f;
    public float stanceSteepness = 4f;   // 8 gave about 2.3% war per turn at hostility 0 (random early wars); 4 gives about 0.06%
    public float stanceStickiness = 3f;
    public int stanceHoldTurns = 10;

    [Header("Ship combat")]
    // Warships of players at war that share a planet fight once a turn (CombatSystem). A hit does offense x K / (K + the target's
    // Defense). A damaged warship heals repairFractionPerTurn of its Health stat a turn while docked at a planet its owner
    // populates with no at-war warship present.
    public float combatDamageK = 20f;
    public float repairFractionPerTurn = 0.1f;
    // Hostility toward a rival grows by hostilityPerShipLost for each of my warships it destroyed, and falls by
    // significantLossHostilityDrop a turn while the share of my fleet strength it destroyed over the last lossWindowTurns
    // is at least significantLossFraction.
    public float hostilityPerShipLost = 1f;
    public int lossWindowTurns = 10;
    public float significantLossFraction = 0.15f;   // engaged-force share (2026-10-06 logs): median 0.11-0.13 and p90 0.25-0.34 on the 25-turn lines; 0.08 (tuned to the old whole-fleet share) fired 28-62 times a run; before that 0.3 then 0.2 never fired
    public float significantLossHostilityDrop = 4f;
    // Surrender: a stance choice offered while I am at war and weaker, weighted 1 / (1 + e^(-(loss share - surrenderMidpoint)
    // / surrenderSteepness)). It ends the war for both sides and locks the pair against new declarations (and forced wars)
    // for surrenderTruceTurns.
    public int surrenderTruceTurns = 30;
    public float surrenderMidpoint = 0.3f;   // on the engaged-force share surrenders happened at a median 0.17-0.18 with 0.15 (14-33 a run, about half of all wars); 0.3 is where a heavy defeat lands (p90 at surrender 0.34-0.64)
    public float surrenderSteepness = 0.03f;   // 0.1 gave 0.0025 at share 0: surrenders with no losses; 0.03 with midpoint 0.3 gives 0.00005 (0.04 would give 0.00055)

    [Header("Retreat")]
    // A fight planet is considered for retreat only when the projected fight (FightProjector) leaves a war rival alive and
    // my group would lose at least retreatCheckFraction of its strength (at a planet I populate: only a wipe-out). Past that
    // gate Stay or Retreat is a weighted roulette: the Retreat weight is 1 / (1 + e^(-(loss - retreatLossFraction) /
    // retreatSteepness)). A retreated-from planet is off the assault target list for retreatCooldownTurns.
    public float retreatCheckFraction = 0.3f;
    public float retreatLossFraction = 0.5f;
    public float retreatSteepness = 0.1f;
    public int retreatProjectionTurns = 20;
    public int retreatCooldownTurns = 10;
    // Imperfect intel (so a fight is not a perfect oracle that reads exactly 0% or 100%): besides the exact projection, each
    // projection is run retreatUncertaintySamples more times with the rival seen at 1 +- retreatRivalUncertainty of its offense
    // and health (one stratified factor per sample); the retreat decision reads the mean loss and the share of samples where the
    // rival survives. 0 for either switches it off.
    public float retreatRivalUncertainty = 0.25f;
    public int retreatUncertaintySamples = 8;
    // The destination is a roulette inside the best tier: each candidate's share is (cheapest cost / its cost) ^ this exponent,
    // normalised. 0 = all equal, large = strictly the nearest.
    public float retreatDestinationCostExponent = 2f;

    [Header("Invasion (conversion)")]
    // A planet I dominate (my warships docked, no warship of a player at war with me) converts one inhabitant each time its progress
    // reaches 1; the progress grows each turn by 1 / max(1, conversionTurnsBase x (1 - my share of the inhabitants)).
    public float conversionTurnsBase = 6f;
    // Hostility a player I am NOT at war with gains toward me for each of its inhabitants I convert (an at-war player's add nothing).
    public float hostilityPerConversion = 3f;
    // Colonization: a dominated target's choice cost is divided by 1 + this x (1 - my share of the inhabitants). 0 or below = off.
    public float conversionColonizeWeight = 0.5f;
    // A planet whose conversion I am holding keeps max(1, ceil(this x the largest nearby at-war rival's offense / my offense per ship))
    // ships there (never more than it has) and releases the rest to garrison and blockade calls. 0 keeps just 1 ship; a negative
    // value keeps every ship (the original hold). An assault target and a contested hold always keep every ship.
    public float conversionHoldKeepFraction = 0.25f;

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
