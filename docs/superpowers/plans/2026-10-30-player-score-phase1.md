# Player Value and Score (ask and trade, phase 1) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** One pure valuation of planets, fleets and tech, a per-player score (their sum) shown on the Diplomacy screen and logged every 25 turns.

**Architecture:** A pure `PlayerValue` class (namespace `FlatSpace.AI`, never touches `Gameboard.Instance`) sums three things: owned planets' `Planet.PlanetValue`, every owned ship's production cost scaled by its health, and researched tech's cost over a divisor. Ship cost reuses `WarshipCosts` (one new method beside `BuildCost`) and health scaling reuses `WarshipStats` (one new method). The Diplomacy screen's pure `HumanDiplomacyView` gets two optional score fields; `Gameboard` supplies the numbers.

**Tech Stack:** Unity 6000.4, C#, UI Toolkit, the repo's Editor self-check pattern (no test framework).

**Spec:** `docs/superpowers/specs/2026-10-30-ask-and-trade-design.md` (phase 1 of its "Phasing"; "The valuation" section). Already built on branch `player-score`: `PlanetResourceData._planetValue`, `Planet.PlanetValue`, the nine type assets, research costs doubled.

## Standard guidelines (the owner's rules)

1. **Reuse or modify existing components:** `WarshipCosts.BuildCost` (the cost formula), `WarshipStats` (health stat, snapshot tiers), `Planet.PlanetValue`, `PlayerAI.ResearchCatalog`/`ProductionCatalog`, `GameAIConstants` (tunables), `HumanDiplomacyView`/`RivalView`/`DiplomacyScreen` (the line), `HumanUiKit` (widgets), `GameAI.LogEconomySummary` plus `AITuningLogger` (the log line), `AllAISelfChecks` and `WarshipSelfCheck.MakeTemplate/MakeResearch/DockWarships` (the self-check). Nothing parallel is built.
2. **ScoreMatrix for decisions:** phase 1 makes **no AI decision** (it is a valuation and a display), so no matrix is used here. The rival's accept/refuse decision in phase 2 will be a `ScoreMatrix` row per proposal (choices Accept / Decline, weighted from the `net` the valuation produces), built with the same row/choice pattern as `StanceMatrix` (`IndependentRows`).

## Global Constraints

- Values (spec): planet = its type's `_planetValue`; fleet = production cost scaled by current health, divided by **50**; tech = research cost divided by **2000**; score = planets + fleets + tech. Fleet strength is not used.
- A ship's cost uses the research tiers **it carries** (`ship.ResearchSnapshot.Count`), not the owner's current research. Siegeship and Bunker base cost = Warship base × `siegeshipCostRatio` / `bunkerCostRatio` (1.5); a colony ship costs the Production catalog's `ColonyShip` item cost.
- Bunkers and colony ships count. Docked ships and ships in flight (`OrderTypeShipTransport` with a `Fleet` payload) count; colonists in flight (people, not ships) do not.
- `PlayerValue` must not touch `Gameboard.Instance`. A self-check never depends on `Gameboard.Instance` (CLAUDE.md).
- Namespace casing: `FlatSpace.AI` for the new class (the folder's).
- New tunables get in-code defaults on `GameAIConstants` (no asset edit).
- CLAUDE.md: a new script's `.meta` is committed with it; no `sed -i` on the repo (CRLF); the owner commits/pushes only when asked, so commit steps below wait for the owner's go.
- Unity cannot run from the CLI: "run" means the owner focuses the Editor and runs the menu item, reading the Console.

## Review Focus

- A player with no planets, ships or research: every part is 0, no exception (Task 2 test).
- A ship with no template or a Health stat of 0 (colony ship): health fraction 1, not NaN/0 (Task 1 test).
- A divisor of 0 or negative in the constants: treated as 1, not infinity (Task 2 test).
- A tied planet (`Owner == NoOwner`) and another player's planet are not counted (Task 2 test).
- A fleet in flight counts exactly once and a rival's does not count for me (Task 2 test).

---

## File structure

- Create `Assets/Flatspace/GameAI/PlayerValue.cs` (+ `.meta`): the valuation.
- Create `Assets/Editor/PlayerValueSelfCheck.cs` (+ `.meta`): the suite, menu `FlatSpace/AI/Run Player Value Self-Check`.
- Modify `Assets/Flatspace/GameAI/GameAIConstants.cs`, `WarshipCosts.cs`, `WarshipStats.cs`, `Assets/Editor/AllAISelfChecks.cs`.
- Modify `Assets/Flatspace/Player/Human/HumanDiplomacyView.cs`, `Assets/Flatspace/Objects/Board/GameBoard.cs`, `Assets/Flatspace/UI/HumanUI/DiplomacyScreen.cs`, `Assets/Editor/HumanUiSelfCheck.cs`.
- Modify `Assets/Flatspace/GameAI/GameAI.cs` (`LogEconomySummary`), `Assets/Flatspace/Diagnostics/AITuningLogger.cs`, `.claude/skills/tuning-log/SKILL.md`, `CLAUDE.md`.

### Task 1: Tunables, ship cost and health fraction

**Files:**
- Modify: `GameAIConstants.cs`, `WarshipCosts.cs`, `WarshipStats.cs`, `AllAISelfChecks.cs`
- Create: `Assets/Editor/PlayerValueSelfCheck.cs`

**Interfaces:**
- Produces: `GameAIConstants.fleetValueDivisor` (float, 50), `GameAIConstants.techValueDivisor` (float, 2000); `WarshipCosts.ShipCost(Ship.ShipKind kind, int improvements, float baseWarshipCost, float colonyShipCost, float factor, float siegeshipRatio, float bunkerRatio) : float`; `WarshipStats.HealthFraction(ShipData template, ICollection<string> snapshot, float damage, string subtype) : float` and `HealthFraction(Ship ship)`.
- Consumes: `WarshipCosts.BuildCost`, `WarshipStats.Health/CurrentHealth/SubtypeOf`.

- [ ] **Step 1: Write the failing checks** in `PlayerValueSelfCheck` (`public static bool RunChecks()`, `[MenuItem("FlatSpace/AI/Run Player Value Self-Check")] Run()`, a `Check`/`Near` helper like `WarshipSelfCheck`): `RunShipCostChecks` asserts `ShipCost(WarShip, 0, 100, 300, 0.1, 1.5, 1.5) == 100`; `ShipCost(WarShip, 3, ...) == 130`; `ShipCost(SiegeShip, 3, ...) == 195`; `ShipCost(Bunker, 0, ...) == 150`; `ShipCost(ColonyShip, 5, ...) == 300`. `RunHealthFractionChecks` (template `WarshipSelfCheck.MakeTemplate()`, Health 100): damage 0 → 1, damage 25 → 0.75, damage 150 → 0; a template with `shipHealth = 0` → 1; a null template → 1. `RunDefaultsCheck` asserts the two new divisors default to 50 and 2000.
- [ ] **Step 2: Run to verify it fails.** Owner focuses the Editor: Console shows compile errors for the missing members. Expected: errors naming `ShipCost`, `HealthFraction`, `fleetValueDivisor`.
- [ ] **Step 3: Implement.** `GameAIConstants`: add a `[Header("Player value")]` block with the two fields and a one-line comment each (units: industry points per value point; research points per value point; retune the tech one with research costs). `WarshipCosts.ShipCost`: ColonyShip returns `colonyShipCost`; Warship uses base 100-style `baseWarshipCost`; Siegeship/Bunker multiply it by their ratio; all through `BuildCost`. `WarshipStats.HealthFraction`: `Health(...) <= 0 ? 1 : CurrentHealth(...) / Health(...)`; the `Ship` overload passes `SubtypeOf(ship.Kind)`.
- [ ] **Step 4: Register the suite** in `AllAISelfChecks` as `("Player Value", PlayerValueSelfCheck.RunChecks)`.
- [ ] **Step 5: Run to verify it passes.** `FlatSpace -> AI -> Run Player Value Self-Check`. Expected: `[PlayerValueSelfCheck] ALL PASSED`.
- [ ] **Step 6: Commit** (when the owner says): `feat: ship cost and health fraction for the player value`.

### Task 2: The `PlayerValue` class

**Files:**
- Create: `Assets/Flatspace/GameAI/PlayerValue.cs`
- Modify: `Assets/Editor/PlayerValueSelfCheck.cs`

**Interfaces:**
- Consumes: Task 1; `Planet.PlanetValue`, `Planet.Owner`, `Planet.DockedShips`, `GameAIMap.PlanetList`, `GameAI.GameAIOrder` (`Type == OrderTypeShipTransport`, `PlayerId`, `Fleet` : `ShipFleetPayload` with `Kind`, `Snapshots`, `DamageAt(i)`), `GameAIConstants` (`warShipData`, `siegeShipData`, `bunkerData`, `colonyShipData`, `warshipImprovementCostFactor`, `siegeshipCostRatio`, `bunkerCostRatio`, the two divisors), `CatalogItem` (`researched`, `cost`, `subType`).
- Produces:
  - `struct PlayerValue.ScoreBreakdown { float Planets, Fleets, Tech; float Total => Planets + Fleets + Tech; }`
  - `class PlayerValue.ValueSettings` with `static ValueSettings From(GameAIConstants constants, IEnumerable<CatalogItem> productionCatalog)` (base warship cost = the catalog item with `subType == "Warship"` cost, colony ship cost = `subType == "ColonyShip"` cost, 0 when absent; a divisor `<= 0` is read as 1).
  - `static float ShipValue(Ship ship, WarshipStats stats, ValueSettings s)` and `static float ShipValue(Ship.ShipKind kind, ShipData template, ICollection<string> snapshot, float damage, WarshipStats stats, ValueSettings s)`: `ShipCost(kind, snapshot.Count, ...) / FleetDivisor * stats.HealthFraction(...)`.
  - `static float TechValue(IEnumerable<CatalogItem> research, ValueSettings s)`: sum of `cost` of `researched` items / TechDivisor; null → 0.
  - `static ScoreBreakdown ScoreOf(GameAIMap map, int player, IEnumerable<GameAI.GameAIOrder> orders, IEnumerable<CatalogItem> research, WarshipStats stats, ValueSettings s)`.

- [ ] **Step 1: Write the failing checks** (new function `RunScoreChecks`, built on a `GameAIMap` from three spawns with distinct x positions and a `PlanetResourceData` whose `_planetValue` you set per planet, as `PlayerAIResourceSelfCheck.MakeSpawn` does; population added with `new Planet.Inhabitant { Player = n }`; ships via `planet.DockShipFromSave`; research via `WarshipSelfCheck.MakeResearch`). Assertions: planets owned by player 0 worth 10 and 2 → `Planets == 12`, another player's planet and a tied planet (`Owner == Planet.NoOwner`) add 0; a new docked warship (base 100, factor 0.1, divisor 50) → `ShipValue == 2`; the same ship with 3 snapshot names → 2.6; at `Damage = 50` of Health 100 → 1.0; a Bunker (base 100 × 1.5) → 3; a ColonyShip with colony cost 300 → 6; `TechValue` of items researched costing 6000 and 300 plus an unresearched 12000 → 3.15; an in-flight order with a 2-warship payload for player 0 adds 4 and the same order for player 1 adds 0 to player 0; a player with nothing → `Total == 0`; a `ValueSettings` built with `techValueDivisor = 0` gives a finite `TechValue`; `ScoreOf` with `research == null` and `orders == null` does not throw; `Total == Planets + Fleets + Tech`.
- [ ] **Step 2: Run to verify it fails** (compile errors: `PlayerValue` missing).
- [ ] **Step 3: Implement** `PlayerValue.cs` as specified. `ScoreOf` walks `map.PlanetList` once: planets with `Owner == player` add `PlanetValue`; `DockedShips` with `Owner == player` add `ShipValue(ship, ...)`; then each order of the player with a `Fleet` payload adds `ShipValue(payload.Kind, TemplateFor(kind), payload.Snapshots[i], payload.DamageAt(i), ...)`. The template for a payload kind comes from `ValueSettings` (copy the four `ShipData` references in `From`).
- [ ] **Step 4: Run to verify it passes** (`Run Player Value Self-Check`: ALL PASSED; also `Run All AI Self-Checks` still passes).
- [ ] **Step 5: Commit** (when asked): `feat: PlayerValue, the valuation and score of a player`.

### Task 3: Score on the Diplomacy screen

**Files:**
- Modify: `HumanDiplomacyView.cs`, `GameBoard.cs`, `DiplomacyScreen.cs`, `Assets/Editor/HumanUiSelfCheck.cs`

**Interfaces:**
- Consumes: Task 2.
- Produces: `RivalView.MyScore`, `RivalView.RivalScore` (`PlayerValue.ScoreBreakdown`); `HumanDiplomacyView.Build(..., Func<string,int,string> refusal, Func<int, PlayerValue.ScoreBreakdown> scoreOf = null)` (trailing optional, so existing callers compile); `Gameboard.PlayerScoreOf(int player) : PlayerValue.ScoreBreakdown` (zeros without a game or for an unknown player).

- [ ] **Step 1: Write the failing check** in `HumanUiSelfCheck.RunDiplomacyViewChecks`: `Build` with `scoreOf: p => new ScoreBreakdown { Planets = p * 10, Fleets = 1, Tech = 2 }` gives `view.MyScore.Planets == 0` (human 0) and `view.RivalScore.Planets == 10` for rival 1; without `scoreOf` both are zeros.
- [ ] **Step 2: Run `Run Human UI Self-Check`** to verify it fails (compile error on the new parameter/fields).
- [ ] **Step 3: Implement** the view fields and parameter; `Gameboard.PlayerScoreOf` builds `ValueSettings.From(constants, humanOrAnyAI.ProductionCatalog.catalogItems)` and calls `ScoreOf` with `GameAI.CurrentAIOrders`, the player's own `ResearchCatalog.catalogItems` and a `WarshipStats` over them; `BuildRivalView` passes `PlayerScoreOf` as `scoreOf`. In `DiplomacyScreen`, add a "Score" block above the fleet list in the detail pane with two rows (`You` and the rival's label), each `total` then `(planets a, fleets b, tech c)`, using `HumanUiKit.Row/Cell/Text` and `0.#` formatting.
- [ ] **Step 4: Run `Run Human UI Self-Check` and `Run All AI Self-Checks`** (both pass), then the owner opens the Diplomacy screen in Play mode: both rows show, the human's changes after a planet is colonized.
- [ ] **Step 5: Commit** (when asked): `feat: show the player score on the Diplomacy screen`.

### Task 4: Tuning log, docs, final checks

**Files:**
- Modify: `GameAI.cs` (`LogEconomySummary`), `AITuningLogger.cs`, `.claude/skills/tuning-log/SKILL.md`, `CLAUDE.md`

**Tuning log additions.**
- `T<turn>|P<id>|Score|<total>|<planets>|<fleets>|<tech>` (one decimal each): logged **every 25 turns** inside the existing `LogEconomySummary` loop, for each player with a planet, so no tracker is needed (it fires on the turn cadence, like `Economy`). It answers: are the planet, fleet and tech values balanced (does tech or fleets swamp planets over a match, does a leading player lead on all three), which tunes `_planetValue`, `fleetValueDivisor` and `techValueDivisor`. Covered by a Play-mode run, not a self-check (`AITuningLogger` has none by design). Added by this task.

- [ ] **Step 1: Add** `AITuningLogger.LogScore(int turnNumber, int playerId, float total, float planets, float fleets, float tech)` beside `LogEconomy` and call it from the loop with `PlayerValue.ScoreOf` (settings built once per call from the first player's production catalog).
- [ ] **Step 2: Update docs:** a `Score` line entry in the `tuning-log` skill (field order and the balance check); a "Player value and score" paragraph in `CLAUDE.md` (formula, the three divisors/fields, in-flight ships counted, colonists not, phase 1 of the ask-and-trade design, the Diplomacy screen line, the `Score` log line).
- [ ] **Step 3: Verify:** owner runs `Run All AI Self-Checks` (all pass) and a Play-mode match with logging on; `grep "|Score|" AITuningLogs/<newest>` shows one line per player every 25 turns, totals non-negative, tech growing over the match.
- [ ] **Step 4: Commit** (when asked): `feat: log the player score every 25 turns; document the player value`.
