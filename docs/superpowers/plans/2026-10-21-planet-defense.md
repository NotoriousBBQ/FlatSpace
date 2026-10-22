# Planet Defense Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add defense bunkers, siegeships and a bombard stat, a separate bombardment step in combat, a bunker gate and a bombard-scaled speed for conversion, and the AI that builds, sizes, escorts and projects with them.

**Architecture:** Bunkers and siegeships are new `Ship.ShipKind`s in `Planet.DockedShips`. Ship stats generalise `WarshipStats` by a per-kind research subtype. A new pure `BombardmentSystem` runs after `CombatSystem` each turn and reuses `CombatSystem.Round` twice (once per direction) so damage arithmetic is shared. `ConversionSystem`, `FightProjector`, `AssaultPlanner`, `BlockadeSystem` and `PlayerAI` are extended, not replaced. Stage 1 (tasks 1-10) is the mechanics; stage 2 (tasks 11-16) is the AI.

**Tech Stack:** Unity 6000.4.1f1, C#, `JsonUtility` saves, JSON catalogs, Editor self-checks (`Assets/Editor/*SelfCheck.cs`, no test framework).

**Spec:** `docs/superpowers/specs/2026-10-21-planet-defense-design.md` (read it and its "Calls settled after the builder check" section first). Also read `CLAUDE.md` and `Assets/Flatspace/GameAI/CLAUDE.md`.

## Global Constraints

- Unity cannot run from the command line. Every "Run" step means: the user focuses the Unity Editor (recompile), then clicks the named menu item and reports the Console line. Compile errors are only visible to them.
- Self-checks must never touch `Gameboard.Instance`; `Planet.DockNewShip`/`DockShipRebuiltSnapshot` need it, so fixtures use `Planet.DockShipFromSave(kind, owner, snapshot, damage)`. Make methods a self-check calls `public`, not `internal`.
- `Ship.ShipKind` and `GameAI.GameAIOrder.OrderType` serialize as ints: **append new values last**.
- Subtype strings are load-bearing and case-exact: `"Warship"` (lowercase s), new ones `"Siegeship"` and `"Bunker"`. Research effect keys: `"<Subtype> Offense"`, `"<Subtype> Health"`, `"<Subtype> Defense"`, `"<Subtype> Bombard"`.
- Namespaces: match the file being edited (`FlatSpace.AI` for `Assets/Flatspace/GameAI/`).
- A new script's `.meta` is committed with it. New `GameAIConstants` fields need in-code defaults (the constants asset needs no edit until tuned).
- Siegeship and Defense Bunker start at 1.5 x the Warship catalog cost (`siegeshipCostRatio`, `bunkerCostRatio`, default 1.5; the runtime `Production Catalog.json` Warship is 100). No grotsits upkeep for bunkers; bunkers are outside `Planet.CanAffordImprovement`.
- Bunker count per planet is capped by `Planet.MaxPopulation`; existing bunkers survive a population drop. Combat stays one round per turn (multiple rounds are parked in `FUTURE_FEATURES.md`).
- Commits are made task by task only with the user's go-ahead; end commit messages with `Co-Authored-By: Claude Sonnet 5.5 <noreply@anthropic.com>`.

## Review Focus

Failure modes the spec implies that a user would hit, each pinned by a test in the owning task:

1. A planet whose last inhabitant dies while it holds bunkers: bunkers must vanish, not linger as an ownerless presence (task 4, 5).
2. A planet over its bunker cap after population loss: no new bunker is offered, the existing ones stay and still fight (task 4).
3. A warship-only fleet (tiny bombard) against a bunkered planet: bunkers must out-damage it, conversion must stay blocked, and the fight projection must show the loss so retreat can fire (tasks 7, 8, 9).
4. A defender left with only a siegeship (no warship): the bombardment must NOT start, and attackers' attack values must still hit that siegeship (tasks 6, 7).
5. Older saves and in-flight orders: a save with no bunkers/siegeships loads unchanged; an order saved without the new escort field loads as 0; a save with bunkers and a mid-flight siegeship fleet round-trips (tasks 10, 15).

---

## Stage 1: Mechanics

### Task 1: Ship kinds, bombard field, asset setup

**Files:**
- Modify: `Assets/Flatspace/Objects/Ships/Ship.cs` (enum), `ShipData.cs`, `Assets/Flatspace/GameAI/GameAIConstants.cs`, `Assets/Flatspace/Objects/Planets/Planet.cs` (`CreateShip`)
- Create: `Assets/Editor/PlanetDefenseAssetSetup.cs`, `Assets/Editor/PlanetDefenseSelfCheck.cs`
- Modify: `Assets/Editor/AllAISelfChecks.cs`

**Interfaces:**
- Produces: `Ship.ShipKind { ColonyShip, WarShip, SiegeShip, Bunker }`; `ShipData.shipBombard`, `shipBombardMax` (float); `GameAIConstants.siegeShipData`, `bunkerData` (ShipData); `siegeshipCostRatio` = 1.5f, `bunkerCostRatio` = 1.5f (`[Header("Planet defense")]`); `public static bool Ship.IsFightingKind(ShipKind k)` (WarShip or SiegeShip); `PlanetDefenseSelfCheck.RunChecks()`; menu `FlatSpace/AI/Run Planet Defense Self-Check`; `PlanetDefenseSelfCheck.Fixture` (copy of `CombatSelfCheck.Fixture` pattern with siege/bunker templates: `Bunkers(planet, owner, count, damage=0)`, `Siege(planet, owner, count, damage=0)`).
- Consumes: `WarshipSelfCheck.MakeTemplate/MakeConstants/MakeResearch/DockWarships`.

- [ ] **Step 1: Write the failing check** `RunKindsAndDefaultsCheck` in `PlanetDefenseSelfCheck`: `(int)Ship.ShipKind.SiegeShip == 2`, `Bunker == 3`; `new GameAIConstants` defaults `siegeshipCostRatio == 1.5f`, `bunkerCostRatio == 1.5f`; `Ship.IsFightingKind` true for WarShip/SiegeShip, false for ColonyShip/Bunker. Register `("Planet Defense", PlanetDefenseSelfCheck.RunChecks)` in `AllAISelfChecks`.
- [ ] **Step 2: Run** `FlatSpace -> AI -> Run Planet Defense Self-Check`. Expected: compile error / FAIL.
- [ ] **Step 3: Implement** the enum values (append), `ShipData` fields, the constants fields and `Ship.IsFightingKind`. In `Planet.CreateShip` replace the two-way template ternary with a switch over all four kinds (`colonyShipData`, `warShipData`, `siegeShipData`, `bunkerData`). Add `PlanetDefenseAssetSetup` (`[MenuItem("FlatSpace/Setup/Create Planet Defense Assets")]`, idempotent): creates `Assets/Flatspace/Objects/Ships/Siegeship.asset` and `Bunker.asset`, sets `WarShip.asset` bombard to `shipBombard = 1`, `shipBombardMax = 3` (very low), and assigns both new assets on `Assets/GameAIConstantsProductionTypes.asset`. Starting stats (tuning later): Siegeship speed 120, health 100/200, defense 3/9, offense 3/9, bombard 20/60; Bunker speed 0, health 150/300, defense 8/20, offense 12/36, bombard 0.
- [ ] **Step 4: Run** the menu item `Create Planet Defense Assets`, then the self-check. Expected: `[PlanetDefenseSelfCheck] ALL PASSED`.
- [ ] **Step 5: Commit** (scripts with `.meta`, the two new `.asset` files with `.meta`, the edited assets).

### Task 2: Generalise ship stats and add bombard

**Files:** Modify `Assets/Flatspace/GameAI/WarshipStats.cs`; test in `PlanetDefenseSelfCheck`, keep `WarshipSelfCheck` green.

**Interfaces:**
- Produces on `WarshipStats` (existing methods keep their signatures and mean "Warship"): `public static string SubtypeOf(Ship.ShipKind kind)` ("Warship"/"Siegeship"/"Bunker"/"ColonyShip"); overloads taking a `string subtype` for `Offense/Health/Defense/TierCount/IsImprovement`; `public float Bombard(ShipData template, ICollection<string> snapshot, string subtype = "Warship")`; `public float EffectiveBombard(Ship ship)` (bombard x current health / Health stat, 0 for Health stat 0); and `Ship`-based `Offense/Health/Defense/CurrentHealth/EffectiveOffense(Ship)` that pick the subtype from `ship.Kind`; `public static List<string> ResearchedNames(IEnumerable<CatalogItem>, string subtype = "Warship")`.
- Consumes: `ShipData.shipBombard/Max` (task 1).

- [ ] **Step 1: Failing checks:** with a research fixture holding a `"Siegeship Bombard"` line of 5 tiers, a siegeship carrying 2 tiers has bombard `base + 2 x (max - base) / 5`; a damaged siegeship's `EffectiveBombard` scales with health; the existing warship `Offense` for a warship carrying "Off" tiers is unchanged; a bunker snapshot only counts `"Bunker ..."` items; a warship with no `Warship Bombard` line returns the template base.
- [ ] **Step 2: Run** the self-check. Expected FAIL.
- [ ] **Step 3: Implement** by threading a `subtype` through `IsWarshipImprovement/TierCount/TiersCarried/Stat` (key = `$"{subtype} Offense"` etc.); keep the formula in `Stat` only.
- [ ] **Step 4: Run** `Planet Defense` and `Warship` self-checks. Expected: both pass.
- [ ] **Step 5: Commit.**

### Task 3: Catalog entries and production cost

**Files:** Modify `Assets/Flatspace/Catalogs/Research/Research Catalog.json`, `Assets/Flatspace/Catalogs/Production/Production Catalog.json`, `Assets/Flatspace/GameAI/WarshipCosts.cs`, `GameAI.cs` (`IndustrySetProduction` case, ~line 451); test in `PlanetDefenseSelfCheck`.

**Interfaces:**
- Produces: research items (all `type "Ship Improvement"`): `Warship Bombard Upgrade 1..5` (subType Warship, effect `Warship Bombard`), `Siegeship Offense/Armor/Shields/Bombard 1..5` (subType Siegeship; effects `Siegeship Offense/Health/Defense/Bombard`), `Bunker Weapons/Armor/Shields 1..5` (subType Bunker; effects `Bunker Offense/Health/Defense`); unlock items `Siegeship 1` and `Bunker 1` (`type "Ship Type"`, requiredTech `Warship 1`); each line's tier 1 requires its unlock item (Warship Bombard 1 requires `Warship 1`), tier n requires tier n-1; costs mirror the warship lines (3000..15000). Production items `SiegeshipProduction` (type Ship, subType `Siegeship`, effect `Siegeship`, requiredTech `Siegeship 1`, cost 150) and `BunkerProduction` (type Ship, subType `Bunker`, effect `Bunker`, requiredTech `Bunker 1`, cost 150). `WarshipCosts.ProductionCost(item, planet, owner, research, baseWarshipCost, factor, float siegeshipRatio = 1.5f, float bunkerRatio = 1.5f)`: `"Siegeship"` returns `BuildCost(baseWarshipCost * siegeshipRatio, factor, ResearchedNames(research, "Siegeship").Count)`, `"Bunker"` likewise with bunker names; other subtypes unchanged.
- Consumes: constants fields (task 1); `ProductionCost` caller passes `GameAIMap.GameAIConstants.siegeshipCostRatio/bunkerCostRatio`.

- [ ] **Step 1: Failing checks:** `ProductionCost` for Siegeship with base warship cost 100, no research = 150; with 2 siegeship improvements researched and factor 0.1 = 180; Bunker likewise; Warship result unchanged. A catalog check reads both JSON files from disk (`Application.dataPath/Flatspace/Catalogs/...`) and asserts every new item exists, every `requiredTech` names an existing research item or `""`, effects match the keys above, and the production items' `requiredTech` exist in the research catalog.
- [ ] **Step 2: Run.** Expected FAIL.
- [ ] **Step 3: Implement** the JSON entries (match the formatting of the existing Warship items) and the `WarshipCosts` branches and call-site arguments.
- [ ] **Step 4: Run** `Planet Defense` and `Warship`. Expected pass. User reloads the catalogs in the Editor (`Catalog` inspector "Load Catalog") if a Play session uses a stale copy.
- [ ] **Step 5: Commit.**

### Task 4: Production completion, bunker cap, planet death

**Files:** Modify `Planet.cs` (`StageCompletedProductionItem` ~953-965, `BuildResearchSnapshot` ~1130, `ScheduleProductionItem`, the planet-death path in `UpdatePlanet`); test in `PlanetDefenseSelfCheck`.

**Interfaces:**
- Produces on `Planet`: `public int BunkerCount(int owner)`; `public bool CanBuildBunker()` (`BunkerCount(Owner) < MaxPopulation` and `Owner != NoOwner`); `StageCompletedProductionItem` docks `SiegeShip` for subType `"Siegeship"` and `Bunker` for `"Bunker"`; `BuildResearchSnapshot(kind, owner)` uses `WarshipStats.SubtypeOf(kind)`; on planet death all `Bunker` ships are destroyed (`DestroyDockedShip`).
- Consumes: kinds, `SubtypeOf` (tasks 1, 2).

- [ ] **Step 1: Failing checks:** `CanBuildBunker` false at cap (MaxPopulation bunkers), true below; a planet over cap (population dropped) is false but keeps its bunkers; a planet reaching zero population loses its bunkers (use the same death trigger the economy uses; build a minimal planet, no `Gameboard`). `BuildResearchSnapshot` is private and needs `Gameboard.Instance`: test `SubtypeOf` mapping only, and test completion through `DockShipFromSave` equivalents.
- [ ] **Step 2: Run.** FAIL.
- [ ] **Step 3: Implement.** Unknown: how the death path is reached in a fixture; if it cannot be reached without `Gameboard`, put the cleanup in a small public `Planet.ClearBunkers()` called from the death path and test that directly.
- [ ] **Step 4: Run** `Planet Defense`. Pass.
- [ ] **Step 5: Commit.**

### Task 5: Bunkers are never counted as ships (audit)

**Files:** Modify every reader named in the code map that is kind-agnostic: `GameAIMap.cs:231` (vision source), `PlayerKnowledge.cs:37,55-56` (contact), `FleetSummary.cs` (group by owner), `FleetUIController.FormatShipRow`, `GameBoard.FleetKindLabel` (:871), `PlanetDetailUIController.cs:107`, `PlanetUIObject.cs:88` (icon ternaries); confirm the `Kind == WarShip` readers (`FleetStrength`, `BlockadeSystem`, `AssaultPlanner`, `ShipTransportPlanner`, `RetreatPlanner`, `RetreatMatrix`, `HostilityCalculator`, `PlayerAI` `OwnedWarships/AuditConversionHolds`, `GameAI.cs:251,305`) already exclude both new kinds and stay warship-only (siegeships do not count toward garrisons, blockade offense, fleet strength). Tests: `PlayerKnowledgeSelfCheck`, `FleetSummarySelfCheck`, `PlanetDefenseSelfCheck`.

**Interfaces:**
- Produces: `public static bool Ship.CountsAsShip(ShipKind k)` (not Bunker); vision/contact/fleet-summary readers use it. `FormatShipRow` shows stats for every kind with a template, including bombard.

- [ ] **Step 1: Failing checks:** a planet holding only owner 1's bunkers gives `PlayerKnowledge.HasContact` false for player 0 and is not a vision source for owner 1's `GetVisionSourcePlanets` unless populated; `FleetSummary.ForPlanet` ignores bunkers; garrison `CountWarships`, `FleetStrength.Mine`, `BlockadeSystem.DockedOffense` return the same values with and without bunkers present; `FormatShipRow` for a bunker and a siegeship includes health, offense, defense and bombard.
- [ ] **Step 2: Run** the self-checks (`Planet Defense`, `Player Knowledge`, and `FlatSpace -> UI -> Fleet Summary`). FAIL.
- [ ] **Step 3: Implement** the helper and the filters; make the icon lookups pick `siegeShipData`/`bunkerData` icons by kind.
- [ ] **Step 4: Run** the same suites plus `Run All AI Self-Checks`. Pass.
- [ ] **Step 5: Commit.**

### Task 6: Combat damage order and siegeships in ship-to-ship combat

**Files:** Modify `CombatSystem.cs` (`CombatUnit`, `Round` ordering at :144, `FightAt` :192-199 / :195, `Repair` :231-233, `DestroyStrandedColonyShips` :245-251); `FightProjector.cs` (:95, :119-122 so siegeships are units and inbound siegeship fleets use `siegeShipData`); test in `CombatSelfCheck` and `PlanetDefenseSelfCheck`.

**Interfaces:**
- Produces: `CombatUnit.Class` (int; 0 warship, 1 siegeship, 2 bunker) set by `CombatUnit.Make(..., int cls = 0)`; `Round` orders victims `OrderBy(u => u.Class).ThenBy(health/max).ThenBy(Index)`; `FightAt` includes `SiegeShip` with stats from `WarshipStats(Ship)` overloads; siegeship attack joins its side's pool. `Repair` heals siegeships like warships; stranded colony ships are protected by a docked warship or siegeship of the owner and killed by a raider warship or siegeship.
- Consumes: stats overloads (task 2).

- [ ] **Step 1: Failing checks:** owner 1 with a damaged siegeship and a healthy warship, owner 0's pool large enough to kill one: the warship is destroyed first even though the siegeship has lower health fraction; a defender with only siegeships still takes attack damage; siegeship repair after combat; colony ships with a docked siegeship are not destroyed; `Round` with all-class-0 units behaves exactly as before (existing `CombatSelfCheck` stays green).
- [ ] **Step 2: Run** `Combat`, `Planet Defense`. FAIL.
- [ ] **Step 3: Implement.** Bunkers are never `FightAt` units.
- [ ] **Step 4: Run** `Combat`, `Retreat`, `Planet Defense`. Pass.
- [ ] **Step 5: Commit.**

### Task 7: The bombardment step

**Files:** Create `Assets/Flatspace/GameAI/BombardmentSystem.cs`; modify `GameAI.cs` (`GameAIUpdate` after `RunCombat` at :151, before `RunConversion`; add `RunBombardment`), `CombatSystem.cs` only if a helper is shared; test in `PlanetDefenseSelfCheck`.

**Interfaces:**
- Produces: `public static class BombardmentSystem` in `FlatSpace.AI`: `public static List<BombardReport> Resolve(GameAIMap map, WarshipStats stats, GameAIConstants constants, List<Planet.UpdateResult> results)`; `public static bool Triggers(Planet planet, int bunkerOwner, GameAIMap map)` (bunkerOwner has bunkers and no docked WarShip/SiegeShip, and a player at war with it (`Diplomacy.IsAtWar(a, b, true)`) has docked WarShip/SiegeShip there); `public static bool Dominated(Planet planet, int bunkerOwner, GameAIMap map, WarshipStats stats)`; `public struct BombardReport { string Planet; int Attacker; int Victim; float BombardDealt; float BunkersDestroyed; float ShipDamage; int BunkersLeft; }`.
- Mechanism (reuse `CombatSystem.Round`, called twice from one start-of-step snapshot, outcomes applied after both): (a) units = the bunker owner's bunkers (Offense = bunker effective offense, Class 2) plus the attackers' ships with Offense 0 and their Class: result damages the attackers' ships (bunker attack vs ship defense, warships first); (b) units = the attackers' ships with Offense = effective bombard plus the bunkers with Offense 0: result damages bunkers (bombard vs bunker defense). Apply both, destroy dead units. Ship losses from (a) are appended as `UpdateResultTypeWarshipsLost` (victim = attacker ship owner, attacker = bunker owner) so hostility and surrender see them; bunker losses are not.
- After the step, **bunker repair:** a bunker that took no damage this turn heals `repairFractionPerTurn` of its Health stat unless `Dominated(...)` (a rival has docked bombard above zero and the bunker owner has no at-war ship docked; a defender siegeship blocks dominance).
- Wiring: `GameAI.RunBombardment` after `RunCombat`; logs via `AITuningLogger.LogBombard` (task 16 lists the line).

- [ ] **Step 1: Failing checks:** (1) trigger: bunkers + defender warship present -> no bombardment; defender with only a siegeship -> none; neither -> bombardment even on the turn the last ship died (run `CombatSystem.Resolve` then `BombardmentSystem.Resolve` on a fixture where the last defender ship dies this turn); (2) simultaneity: both directions use start-of-step values (bunkers destroyed this step still fired); (3) arithmetic: attackers' bombard 100 vs one bunker, defense 20, `combatDamageK` 20 -> damage `100 x 20/(20+20)`; (4) warship-only fleet with bombard 1 vs three bunkers: bunkers' damage exceeds the fleet's, bunkers survive with tiny damage; (5) repair: undamaged-this-turn bunker heals when not dominated, does not heal when a rival has docked bombard > 0 and the owner has no ship; a damaged bunker that was hit this turn does not heal; (6) no bunkers -> no results.
- [ ] **Step 2: Run.** FAIL.
- [ ] **Step 3: Implement.**
- [ ] **Step 4: Run** `Planet Defense`, `Combat`, `Simultaneity` (add a bombardment case: result identical with the players' order swapped). Pass.
- [ ] **Step 5: Commit.**

### Task 8: Conversion gate, bombard dominance and bombard-scaled speed

**Files:** Modify `ConversionSystem.cs` (`Dominator` :59-75, `Step` progress at :136, new factor), `AssaultPlanner.cs` (`ConversionHolds` :191, dominance readers), `PlayerAI.cs` (`IsConversionTarget` :721), `GameAIConstants.cs`; test in `ConversionSelfCheck` and `PlanetDefenseSelfCheck`.

**Interfaces:**
- Produces: `Dominator` counts docked WarShip/SiegeShip with `EffectiveBombard > 0` as dominators (two-dominator tie-break compares total bombard, then lower id) and any at-war docked WarShip/SiegeShip of another player blocks; `public static float BombardSpeedFactor(float myBombard, GameAIConstants c)` = `clamp(myBombard / c.conversionBombardReference, c.conversionBombardMinFactor, 1)`; tunables `conversionBombardReference` = 60f, `conversionBombardMinFactor` = 0.05f (starting points, tuned from logs); progress per turn = `BombardSpeedFactor(...) / TurnsPerFlip(...)`. `ConversionSystem.Step` starts and continues a session only if `BunkerCount` of every foreign holder is 0 (all bunkers on the planet); a session with bunkers appearing ends with the existing end reasons (no new reason; bunkers present counts as "not dominated" for the session).
- Consumes: `EffectiveBombard`, `Bunker` kind.

- [ ] **Step 1: Failing checks:** warship-only dominator with bombard 1 dominates (bombard > 0) but progress per turn is `1 x factor` (min factor) / turns; siegeship fleet bombard >= reference gives factor 1; a bunker on the planet blocks session start and continue (progress does not advance); the last bunker destroyed the same turn lets conversion start next `Resolve`; `ConversionSelfCheck` existing expectations updated (fixtures' warship template gets bombard) and pass.
- [ ] **Step 2: Run** `Conversion`, `Planet Defense`. FAIL.
- [ ] **Step 3: Implement**, including `AssaultPlanner.ConversionHolds` and `PlayerAI.IsConversionTarget` reading bombard-based dominance through `Dominator`.
- [ ] **Step 4: Run** `Conversion`, `Planet Defense`, `Run All AI Self-Checks`. Pass.
- [ ] **Step 5: Commit.**

### Task 9: Bunker fire in the fight projection

**Files:** Modify `FightProjector.cs` (`Project` :84-148, `ProjectPerceived`), `RetreatPlanner.cs` only if a reader assumes ship-only losses; test in `RetreatSelfCheck` and `PlanetDefenseSelfCheck`.

**Interfaces:**
- Consumes `BombardmentSystem` arithmetic: after the projection's ship-to-ship rounds, while the rival side holds bunkers and no ship, run the same two-direction round each projected turn (the projection already counts my in-flight ships landing; inbound siegeship fleets use `siegeShipData`). Rival repair and rival in-flight ships stay unmodelled.
- Produces: a shared pure helper `BombardmentSystem.Round(List<CombatUnit> ships, List<CombatUnit> bunkers, Func<int,int,bool> atWar, Func<int,int,float> hostility, float k)` returning both outcomes, used by `Resolve` (task 7) and the projector (refactor task 7's mechanism into it at this task if not already).

- [ ] **Step 1: Failing checks:** a warship-only group at a bunkered planet projects a high loss fraction and `MyGroupWiped`; a siegeship-heavy group with enough bombard projects destroying the bunkers; an inbound siegeship alone at a planet whose defenders hold warships projects a loss (feeds the follower gate, task 14); with no bunkers the projection equals the old one (existing `RetreatSelfCheck` unchanged).
- [ ] **Step 2: Run** `Retreat`, `Planet Defense`. FAIL.
- [ ] **Step 3: Implement.**
- [ ] **Step 4: Run** `Retreat`, `Combat`, `Planet Defense`. Pass.
- [ ] **Step 5: Commit.**

### Task 10: Saves

**Files:** Modify `SaveLoadSystem.cs` only if needed; test in `PlanetDefenseSelfCheck` (build a `GameSave.PlanetSave` and `OrderSave` by hand, no `Gameboard`).

**Interfaces:** `ShipSave.kind` already carries the int; `ShipFleetPayload` stays one kind per fleet (mixed fleets are several orders, one per kind).

- [ ] **Step 1: Failing checks:** `ShipSave` round-trips `SiegeShip`/`Bunker` kinds and damage through `JsonUtility`; `GameAIMap` restore path docks them with `DockShipFromSave`; a `ShipFleetPayload` of siegeships round-trips through `ToSave/FromSave`; a JSON `PlanetSave` with no bunkers loads unchanged.
- [ ] **Step 2: Run.** If it passes untouched, record that and skip step 3. **Step 3:** fix any gap (for example `GameAIMap.RecomputeIncomingShips` defaulting a missing Fleet to WarShip). **Step 4: Run.** Pass. **Step 5: Commit.**

---

## Stage 2: AI

### Task 11: Weights for the new subtypes, research and production

**Files:** Modify `PlayerAI.cs` (`ExpandIndustryWeights`/`ConsolidateIndustryWeights`/`AmassIndustryWeights` ~1479-1514, the three research tables ~1297, `GetIndustrySituationalWeightMultiplier` :1621, `GetResearchSituationalMultiplier` :1456); test in `PlayerKnowledgeSelfCheck` (asserts every table value explicitly) and `PlanetDefenseSelfCheck`.

**Interfaces:** table entries `"Siegeship"` and `"Bunker"` for industry and research in all three strategies (starting points; a missing key silently falls back to 1, so every strategy needs explicit entries: Expand `Siegeship` 0.5, `Bunker` 0.5; Consolidate `Siegeship` 2.0, `Bunker` 1.5; Amass `Siegeship` 3.0, `Bunker` 2.0); `Siegeship` and `Bunker` research items are already normalised by subtype; `WarshipStats.OffenseKey` boost generalises to `"<Subtype> Offense"` for siegeships only if wanted (not in scope).

- [ ] **Step 1: Failing checks:** every table value for the two new keys in all strategies; a bunker/siegeship choice from `BuildIndustryMatrix` carries `GetIndustryStrategyWeight` times the situational multiplier; with no entry the weight would be 1 (guard against a silent fallback by asserting the explicit values).
- [ ] **Step 2-5:** Run, implement, run (`Player Knowledge`, `PlayerAI Resource`, `Planet Defense`), commit.

### Task 12: Threat-weighted bunker building

**Files:** Modify `PlayerAI.cs` (`GetIndustrySituationalWeightMultiplier` bunker branch), `GameAIConstants.cs`; test in `PlanetDefenseSelfCheck`.

**Interfaces:** `PlayerAI.BunkerThreatMultiplier(string planetName)` (private helper, cached per production turn like `_warshipMultiplierThisTurn`): 0 when not `IsConsolidateLike`, when `!planet.CanBuildBunker()`, or when no at-war rival is known nearby; otherwise a base (`bunkerBaseWeight` 1) times category factor (outer planet or chokepoint, using `ShipTransportPlanner.IsOuter/IsChokepoint`) times `bunkerBlockadedBoost` (blockaded) and `bunkerRivalAdjacentBoost` (an at-war rival's docked ship on or beside the planet, tunables default 3 and 2). "Nearby" = the planet or a direct neighbour holds an at-war rival's docked warship/siegeship, or the planet is outer/chokepoint with a known at-war rival planet (`AssaultPlanner.HasKnownEnemyPlanet`).
- Log: `BunkerBuilt` (task 16).

- [ ] **Step 1: Failing checks:** weight 0 under Expand; 0 at cap; 0 with no known rival; boosted when blockaded; larger on an outer planet than an interior one; computed once per production turn.
- [ ] **Step 2-5:** Run, implement, run (`Planet Defense`, `PlayerAI Resource`), commit.

### Task 13: Assault composition sized by need

**Files:** Modify `AssaultPlanner.cs` (`RequiredForce` :109, `Deficit` :114, `Plan` :438, force building :475-511), `PlayerAI.cs` (`WantedWarships` :1558, a new `WantedSiegeships`, `ComputeWarshipMultiplier` pattern for siegeships, `PlanShipActions`), `Planet.cs`/`GameAIMap.cs` (`GetIncomingBombard`, `RecomputeIncomingOffense` :352-364); `GameAIConstants` (`siegeBombardMargin` 1.3, `siegeshipMaxFleetShare` 0.5); test in `AssaultSelfCheck`-style cases in `PlanetDefenseSelfCheck` and `ShipTransportSelfCheck`.

**Interfaces:**
- `AssaultPlanner.KnownBunkerDefense(Planet target)` (sum of known bunker defense, 0 when none); `AssaultPlanner.RequiredBombard()` = `KnownBunkerDefense x siegeBombardMargin`; `AssaultPlanner.WantedSiegeships(float bombardPerSiegeship)` = `ceil((RequiredBombard - docked and incoming bombard) / bombardPerSiegeship)` capped so siegeships are at most `siegeshipMaxFleetShare` of the fleet; `Planet.GetIncomingBombard(ShipKind, owner)` (derived, recomputed with the other incoming counters, never saved); `PlayerAI.WantedSiegeships()`; the siegeship situational weight is the Warship shortfall multiplier pattern using wanted vs owned siegeships. `AssaultPlanner.Plan` emits **separate `ShipAction`s per kind** (`Kind = WarShip`, `Kind = SiegeShip`; `ShipFleetPayload` stays single-kind). Siegeships are never garrison sources or sinks: `ShipTransportPlanner` stays warship-only; they sit at their planet until an assault pulls them. Warship force sizing is unchanged.
- The warship part leaves immediately with what exists; siegeships go the same turn only if built (see task 14 for those still to come).

- [ ] **Step 1: Failing checks:** target with bunkers of total defense 100 and margin 1.3 wants 130 bombard; wanted siegeships stop at the share cap; incoming siegeship bombard counts toward satisfied need; with no bunkers wanted siegeships is 0; `Plan` returns a warship action and a siegeship action when both are available and departs with the warships alone when no siegeship exists (no stall); garrison planner ignores siegeships.
- [ ] **Step 2-5:** Run, implement, run (`Planet Defense`, `Ship Transport`, `Blockade Breaking`, `Warship`), commit.

### Task 14: Follow-up siegeships and the arrival gate

**Files:** Modify `PlayerAI.cs` (`PlanShipActions` :1989), `AssaultPlanner.cs`; test in `PlanetDefenseSelfCheck`.

**Interfaces:** `PlayerAI.ArrivalNotOutmatched(string origin, Planet target, int count)` builds the group (docked friendly ships at the target, incoming, plus the ships about to leave) and returns `FightProjector.Project(...)`'s result `!MyGroupWiped && PerceivedLossFraction < followerMaxLossFraction` (tunable 0.5; it counts bunker fire from task 9). Siegeships for the sticky assault target may leave from any planet when `ArrivalNotOutmatched` holds, else they stay and a `SiegeHeld` line is logged once per change (via a tracker, log-only state).

- [ ] **Step 1: Failing checks:** a follower siegeship stays when the first force is gone and bunkers stand (projection shows a loss); it leaves when a friendly warship force holds the planet; siegeships from two origins both leave when the projection is safe; no follower leaves before a sticky target exists.
- [ ] **Step 2-5:** Run, implement, run (`Planet Defense`, `Retreat`), commit.

### Task 15: Escorted colonists

**Files:** Modify `GameAI.cs` (`GameAIOrder` gets `public float EscortOffense;` serialized, `OrderType` unchanged), `SaveLoadSystem.cs` (`OrderSave.escortOffense`, writer :284-297, loader GameAI.cs:636), `BlockadeSystem.cs` (`Value` use in `ApplyToColony` :232, `RouteFor`), `PlayerAI.cs` (`ProcessColonizers` ~772-960), `RoutePlanner.cs`; test in `PlanetDefenseSelfCheck` and `BlockadeAvoidanceSelfCheck`.

**Interfaces:**
- `GameAIOrder.EscortOffense` (0 = none; an older order saves as 0 because a missing key deserialises to 0); `BlockadeSystem.ApplyToColony` subtracts `order.EscortOffense` from `Value(...)` at each node (a node is passed when `Value - EscortOffense <= 0`).
- `PlayerAI.PlanEscort(Planet origin, Planet target, RoutePlanner.PlannedRoute shortest, WarshipStats stats)` returns `(int ships, float offense)` or null: needed = max blockade value along the shortest route times `1 + blockadeBreakMargin`; uses existing spare warships at `origin` only (`ShipTransportPlanner` states give spare); null if spare offense is short. Used when the target is a dominated planet (`IsConversionTarget`) or the shortest route crosses a known blockade (non-dominated), and the escorted shortest route cost is below the detour cost; `ProcessColonizers` then emits the escort as the ordinary ship-transport order trio (`EmitShipOrders`) on the same turn and sets the colonist's `EscortOffense` and route to the shortest. Decision through the existing colonization `ScoreMatrix` (escorted-shortest and detour are two choices of the same target with their route cost).

- [ ] **Step 1: Failing checks:** an escorted colonist with offense >= blockade value x 1.1 survives a blockaded node and an unescorted one is cut; offense short -> `PlanEscort` null and the detour is used; a dominated-planet target launches an escort; an order JSON without `escortOffense` loads as 0; `EscortOffense` round-trips; the escort's ships leave the origin and arrive as ordinary docked ships.
- [ ] **Step 2-5:** Run, implement, run (`Planet Defense`, `Blockade Avoidance`, `Colonist Redirect`, `Run All AI Self-Checks`), commit. Also confirm `ColonistRedirect` keeps `EscortOffense` on a redirected order.

### Task 16: Tuning log, docs, registration

**Files:** Modify `AITuningLogger.cs`, `.claude/skills/tuning-log/SKILL.md` (near the conversion bullets, ~316-333), `CLAUDE.md` (root, "AI Tuning Log" and Planet defense mention), `Assets/Flatspace/GameAI/CLAUDE.md` (new "Planet defense" section; update the "Reevaluate next" list at ~337 to drop the offense-scaled item), `FUTURE_FEATURES.md` (move entry (4) to `completed_features.md` with `/update_feature_list` once the user confirms done), `AllAISelfChecks.cs` (confirm `Planet Defense` registered).

**Tuning-log additions (state-change only unless stated; each through a tracker or log-only state, like `ConversionTracker`):**

| Line | When | Answers |
|---|---|---|
| `BunkerBuilt\|<planet>\|<count>\|<cap>\|<weight>` | on completion | where and how fast bunkers go up |
| `Bombard\|<planet>\|<attacker>\|<victim>\|<bombardDealt>\|<bunkersLeft>\|<shipDamageTaken>` | every bombardment turn | whether siege fleets beat bunkers |
| `ConversionBlocked\|<planet>\|<bunkers>` | on state change | how long bunkers stall conversion |
| `SiegeSized\|<target>\|<bunkerDefense>\|<bombardNeeded>\|<siegeships>\|<cap>` | on change | whether sizing by need works |
| `SiegeFollow\|<origin>-><target>\|<ships>\|<projectedLossPct>` and `SiegeHeld\|<origin>\|<target>\|<reason>` | on change | whether followers arrive safely |
| `EscortLaunch\|<origin>-><target>\|<escortOffense>\|<blockadeValue>` and `EscortDetour\|<origin>-><target>\|<reason>` | per launch / on change | escort versus detour |
| trailing `bombardFactor` on `Convert`/`ConversionStart` | existing lines | conversion pace tuning |

- [ ] **Step 1:** add the `AITuningLogger.Log*` methods (pattern: `if (_currentLogPath == null) return;`, `FormatLine(turn, player, "Code", ...)`, invariant culture), call them from the tasks above, document every line in the `tuning-log` skill and root `CLAUDE.md`.
- [ ] **Step 2:** write the "Planet defense" section in `Assets/Flatspace/GameAI/CLAUDE.md` (bunkers, siegeships, bombardment, bombard-scaled speed, escorts, tunables with defaults) and trim anything now false.
- [ ] **Step 3:** user runs `Run All AI Self-Checks`. Expected: all suites pass including `Planet Defense`.
- [ ] **Step 4:** user does Play runs (a bunkered 4p board, and `test2.json`) with `_logAIEvents` on and runs `/tuning-log`; tune `conversionBombardReference/MinFactor`, bunker and siegeship stats, costs and margins from the logs.
- [ ] **Step 5: Commit** docs; then `/feature_close`.

## Self-Review notes

- Spec coverage: requirements 1-2 tasks 1, 3, 4; 3-6 tasks 1-3; 7 task 6; 8-9 task 7; 10 task 8; 11 task 7; 12-14 tasks 11-15; 15 plan-wide (reuse of `Round`, `FightProjector`, `AssaultPlanner`, `ScoreMatrix`); 16 task 8; 17-18 task 15; anti-goal tasks 13-14; projection decision task 9; cost ratio tasks 1, 3.
- The spec said conversion speed uses "my share p" as an offense term; it does not (speed was population share only). Corrected in the spec: task 8 adds the bombard factor.
- The siegeship/bunker numbers in tasks 1, 3, 8, 11, 12 are starting points for tuning, not decisions.
