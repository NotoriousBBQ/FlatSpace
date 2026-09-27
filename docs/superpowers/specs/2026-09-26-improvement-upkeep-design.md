# Improvement Upkeep — DRAFT design for review

Status: **approved by the user on 2026-09-26: all seven recommendations accepted.** One refinement (below) was made
to decision 5 because of the user's long-term goals. Implementation plan at the end.

## Why this matters (the user's suspicion, confirmed)

Improvement upkeep is paid in **grotsits**, every turn, together with the population's own consumption
(`Planet.GetMaintenanceCost() = Population.Count + improvement upkeep`, used by `ConsumeGrotsits` and by
`GrotsitsWorkerRequirement`). So turning it on changes three things at once:

1. **Grotsits shipments.** More planets fall short (`GrotsitsShortage`), and the AI answers by shipping grotsits from
   surplus planets. The last long run had 31 grotsits shipments in 431 turns (4 of them from the two Desolate planets),
   so this system is barely exercised today and would carry real load.
2. **Morale, and through it all production.** A grotsits shortfall sets grotsits to 0 and lowers `Morale` by
   `moraleStep`; morale scales every resource's output. A planet that cannot afford its upkeep gets poorer at
   everything.
3. **Worker allocation.** The grotsits worker requirement rises with upkeep, so workers move off food, industry and
   research. Upkeep is an economy-wide tax, not just a grotsits line item.

## Current state (verified in code)

- `CatalogItem` has `tier` and `maintenanceCost`, and the catalog JSON carries both, but
  `Catalog.CatalogSaveData.ItemSaveData` has neither field, so **`JsonUtility` drops them on load**. Runtime upkeep is
  0 for every improvement. The Save Catalog inspector button also strips both keys from the JSON.
- `Planet.AddActiveImprovement` appends `(itemName, maintenanceCost)` to `CompletedImprovements` for every finished
  improvement, and `GetImprovementMaintenanceCost` **sums all of them**, so a planet with tiers 0-3 of a category
  pays for all four while only the best tier's yield applies (`ApplyImprovementYield` keeps the maximum).
- `BuildIndustryMatrix` excludes only exact-name completed items, so every researched tier stays on offer (a planet
  can build tier 1 after tier 3, and the roulette sees every tier as a separate full-weight choice).
- `CompletedImprovements` and the yield modifiers are **not saved**, so loading a save resets a planet's improvements.
- The catalog's `maintenanceCost` equals the item's `effect` (3, 7, 12, 18 ... 88 at tier 10).

## What the numbers say (replay of the 431-turn log `19-09-57`)

I replayed the improvements each of the 40 planets actually built, priced at the catalog values, against each planet's
full-staffed grotsits capacity (`base + maxPop x per-worker`; median 31). Upkeep as a share of that capacity, median
across planets, and the number of planets whose population plus upkeep exceeds capacity:

| Charging rule | Scale on catalog values | T200 median share (over) | T430 median share (over) |
|---|---|---|---|
| all tiers (today's semantics) | 1.0 | 139% (30/40) | 571% (40/40) |
| best tier per category only | 1.0 | 88% (24/40) | 245% (40/40) |
| best tier only | 0.3 | 26% (8/40) | 74% (25/40) |
| best tier only | 0.2 | 18% (8/40) | 49% (14/40) |
| best tier only | 0.15 | 13% (8/40) | 37% (9/40) |
| best tier only | 0.1 | 9% (8/40) | 25% (9/40) |

Two conclusions:

- **The catalog values cannot be used as they are.** At scale 1.0 every planet is bankrupt by the end of a match (total
  upkeep 6,323 summing all tiers, 2,371 best-tier-only, against 1,135 of total grotsits capacity). They need to come
  down roughly 5-10x.
- **Some planets can never pay any upkeep themselves** (the constant 8-9 planets "over" at any scale): Farm and
  Verdant have grotsits base 0 and per-worker 1, so their full-staff capacity equals their own population's
  consumption. They can only afford improvements with grotsits imports. Upkeep therefore makes the grotsits shipping
  system, and the grotsits-rich planet types (Desolate 52, Desert 52, Prime 46 at full staff), structurally important.
  That is a design consequence worth deciding on purpose, and it links directly to the open Desolate grotsits reward
  question.

## Proposed design

1. **Load the fields.** Add `tier` and `maintenanceCost` to `ItemSaveData` (constructor and
   `CreateCatalogFromCatalogSaveData`). This also stops the Save Catalog button destroying data. Self-check: a
   `CatalogSaveData` -> JSON -> back round trip keeps both.
2. **A single knob:** `GameAIConstants.improvementUpkeepScale`, multiplying the catalog's `maintenanceCost` when it
   is charged, so the catalog stays a relative price list and re-tuning does not mean editing 44 items.
3. **Charge only the best tier per category** (`GetImprovementMaintenanceCost` = sum over subTypes of the highest
   completed tier's cost x scale). This matches how yield already works and is about 2.7x cheaper than summing.
4. **Do not offer superseded tiers:** `BuildIndustryMatrix` drops improvement items whose tier is at or below the
   planet's best completed tier for that subType. That fixes the lower-after-higher waste at its source and shrinks
   each planet's improvement choice list (a partial cure for the roulette dilution).
5. **Affordability as a situational weight:** in `GetIndustrySituationalWeightMultiplier` (your standing structural
   preference), an improvement's weight is 0 when the planet's grotsits capacity could not cover its population plus
   the upkeep after building it, so the AI stops digging planets into a morale spiral. Needs a small
   `Planet.GetGrotsitsCapacity()` helper (base + population x per-worker x yield).
6. **Observability:** add per-player economy lines to the tuning log every 25 turns (planets in grotsits shortage,
   mean morale, total upkeep), so grotsits shipments and morale can be tuned from logs like everything else.
7. **Saves:** persist each planet's completed improvements (names) in `PlanetSave` and rebuild `CompletedImprovements`
   and yields on load. Without it a loaded game silently loses improvements and upkeep together. Small, but a save
   format change (older saves load with none).

Files touched: `Catalog.cs`, `GameAIConstants.cs`, `Planet.cs`, `PlayerAI.cs`, `AITuningLogger.cs`, `SaveLoadSystem.cs`,
`GameAIMap.cs` (load), the self-checks, `CLAUDE.md`. No catalog JSON edits, no new scripts.

## Decisions (all accepted as recommended, with the refinement below)

1. **Knob or edit the catalog numbers?** Recommend the `improvementUpkeepScale` knob. Alternative: edit `maintenanceCost`
   in the JSON directly (no code knob, but 44 edits per re-tune).
2. **Charging rule:** recommend best-tier-only. Alternative: keep summing all tiers (needs a much smaller scale).
3. **Starting scale:** recommend 0.15 with best-tier-only (median burden about 13% at turn 200 and 37% at turn 430 in the
   replay), then tune from a long run.
4. **Superseded tiers:** recommend "offer only tiers above the planet's best" (skips allowed). Alternative: only the
   next tier, which forces sequential progression.
5. **Affordability rule:** recommend the hard exclusion (weight 0) in step 5. Alternative: a soft taper.
6. **Saves:** recommend including step 7 in this work. Alternative: a follow-up change.
7. **Order relative to the Desolate grotsits reward:** recommend upkeep first (it changes what grotsits are worth), then
   tune Desolate's grotsits against the new demand.

## Risks

- The knob at scale 0 reproduces today's behavior exactly, which is the safe fallback and the way to stage rollout.
- Every economy number shifts again (upkeep draws workers and morale), so the next long run is another baseline.
- The grotsits shipping system has barely been exercised; expect it to expose limits (path-node reach, shipment
  size) once demand is real. Measure with step 6 before tuning.
- The affordability rule uses full-staff capacity as its ceiling; a planet mid-shortage can still dip. Acceptable
  because morale recovers when grotsits catch up.

## Testing and rollout

Self-checks (test first): catalog round trip; best-tier charging including a lower tier built after a higher one;
superseded tiers filtered; affordability weight 0 vs 1 at the boundary; scale 0 charges nothing. Then one long match
with the knob at 0.15, comparing grotsits shipments, morale, completions per turn and improvement tiers reached
against the last run (`19-09-57`: 31 grotsits shipments, 80/279/193/172 completions per 100 turns).

## Long-term goals that shape the design (not required now)

- **Prime and Normal** should need no, or minimal, incoming shipments unless the AI chooses otherwise.
- **Specialized planets** (Desert, Farm, Industrial, Ocean) develop their specialty and industry, with some
  development of other resources, minimizing incoming shipments of shortfall resources.
- **Super-specialized planets** (Desolate for grotsits, Verdant for food) develop only their specialty and industry
  and rely on a stream of incoming shipments for every shortfall.

### Refinement to decision 5 (affordability)

A hard "the planet must cover its own upkeep" rule would block Farm and Verdant entirely (their full-staff grotsits
capacity equals their population's consumption, so any upkeep is unaffordable), contradicting the goal that
super-specialized planets run on imports. So the rule is:

- A planet whose grotsits capacity does **not exceed its maximum population** is an importer by data (Farm, Verdant
  today): it is exempt and may build improvements, relying on grotsits shipments.
- Every other planet (Prime, Normal, Desert, Industrial, Ocean, Desolate) may only build an improvement if
  `maxPopulation + upkeep after building <= grotsits capacity`, which keeps Prime and Normal self-sufficient.
- The whole rule lives in **one method**, `Planet.CanAffordImprovement`, which is the seam for later per-type import
  allowances (none for Prime/Normal, small for specialized, large for super-specialized). Nothing else needs to change
  when those tiers are tuned. Incoming shipments per planet type can already be measured from the existing log
  (`FoodShip`/`GrotsitsShip` targets carry the planet name), so no extra logging is needed for that goal.

## Implementation plan (executed inline, test first, one commit)

1. **Catalog loading.** `ItemSaveData` gains `tier` and `maintenanceCost` (constructor and load);
   `CreateCatalogFromCatalogSaveData` becomes public for the self-check. Checks: JSON round trip through
   `ItemSaveData`; loading JSON with both keys yields a `CatalogItem` with them set.
2. **Knob and per-planet upkeep.** `GameAIConstants.improvementUpkeepScale` (default 0.15).
   `Planet.RecordImprovement(CatalogItem)` records the name, applies the yield (max), and tracks the best tier per
   subType; `GetImprovementMaintenanceCost` (public) = sum of the best tier's `maintenanceCost` per subType x scale.
   Checks: best-tier charging including a lower tier completed after a higher one, scale 0 charges nothing.
3. **Superseded tiers and affordability.** `Planet.GetBestImprovementTier`, `IsImprovementSuperseded`,
   `GetGrotsitsCapacity`, `CanAffordImprovement`. `BuildIndustryMatrix` drops superseded improvements;
   `GetIndustrySituationalWeightMultiplier` returns 0 for an unaffordable improvement. Checks: capacity arithmetic,
   the importer exemption, the boundary, the situational weight, superseded filtering.
4. **Observability.** `Planet.GrotsitsShort`; `AITuningLogger.LogEconomy` every 25 turns per player
   (`Economy|planetsShort|meanMorale|totalUpkeep`), called from `GameAI`.
5. **Saves.** `PlanetSave.completedImprovements` (names) written by `SaveLoadSystem`, restored in
   `GameAIMap.SetPlanetSimulationStats` through the production catalog and `RecordImprovement`. Older saves load with
   none. Verified by a Play-mode save/load (the wiring needs `Gameboard`, so it has no self-check).
6. **Docs.** `CLAUDE.md`, the handoff.
