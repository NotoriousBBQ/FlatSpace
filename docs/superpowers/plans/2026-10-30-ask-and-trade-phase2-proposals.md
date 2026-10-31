# Ask and Trade, Phase 2: Proposals (peace and gifts) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** The human proposes a deal to a rival it has met (end the war, and gifts of planets, fleets and tech in either direction); the rival accepts or declines from the shared valuation; an accepted deal executes instantly and shifts the rival's hostility.

**Architecture:** A pure `DealEvaluator` values a `Deal` from the rival's side (items valued by `PlayerValue`, peace worth from the war's cost and hostility) and a one-row `ScoreMatrix` (Accept / Decline) makes the decision. A pure `DealExecutor` applies the items. A new `propose` command in `HumanCommandProcessor` ties them together (`Check`-style preview and `Apply`). A `ProposalDialog` (layout B of the brainstorm) sits on the Diplomacy screen.

**Tech Stack:** Unity 6000.4, C#, UI Toolkit, the repo's Editor self-check pattern.

**Spec:** `docs/superpowers/specs/2026-10-30-ask-and-trade-design.md` (phase 2 of its "Phasing"). Phase 1 (`PlayerValue`, score) is merged. Third-party items (start/end war with P3) are phase 3, not here.

## Standard guidelines (the owner's rules)

1. **Reuse or modify:** `PlayerValue` (all values), `GameAI.ApplySurrender` (peace + truce stamp, called with `dealTruceTurns`), `Planet.ConvertInhabitant`/`PlayerWithMostPopulation`, `DiplomacyState` Get/Set, `HumanCommand`/`HumanCommandProcessor`/`SubmitHumanCommands`, `PlayerAI.RecordLosses`/`LossShareToward`, `DiplomacyScreen.RequestReply` and `ChatClient`, `HumanUiKit`, `AITuningLogger`. New: `DealEvaluator`, `DealExecutor`, `ProposalDialog`, `Planet.TransferShips`, `PlayerAI.GrantTech`.
2. **ScoreMatrix for decisions:** the rival's accept/decline is one `ScoreMatrix<DealDecisionElement, DealChoiceElement, DealAction>` row with choices Accept and Decline (`StanceMatrix` pattern), no `weightSelector` (deterministic: the best-scored choice wins; Accept scores the net, Decline 0, a tie goes to Accept). Passing a `weightSelector` later turns it into the roulette without changing callers.

## Global Constraints

- Spec values: accept when `net >= 0`; `net = value received - value given + peace worth`; peace worth = war cost - hostility penalty, clamped at `-peacePriceCapFraction` (0.5) of the rival's score; hostility delta `= -dealHostilityPerValue (1) x (tangible received - tangible given)` clamped to `+-dealHostilityCap` (10), clamped to `0..hostilityMax`; bought peace stamps a `dealTruceTurns` (20) truce on both pair rows; planets convert one inhabitant at a time until `PlayerWithMostPopulation()` is the receiver; leftovers stay.
- A player may give only what it owns; nobody gives its last populated planet or its capital; a tech needs the receiver to lack it, and **a received tech brings its whole prerequisite chain** (every item reached by following `requiredTech`, the owner's rule of 2026-10-30): the chain's items are all marked researched and removed from the queue, so no prerequisite is needed; "End the war" only while at war with contact; a deal is all-or-nothing, re-checked at execution.
- Pure classes never touch `Gameboard.Instance`; self-checks never depend on it; `internal` is invisible to the Editor assembly (use `public`).
- `HumanCommand` stays flat (`JsonUtility`): new fields default empty so old JSON loads.
- CRLF files, no `sed -i`; new scripts' `.meta` committed; the owner commits/pushes only when asked.
- Unity cannot run from the CLI: the owner runs the menu self-checks; implementers hand-trace and report "not run (Unity)".

## Plan rulings (where this plan fixes what the spec left to the builder; all mine)

- **War cost** = `LossShareToward(proposer) x rival's fleet value` (ships) + the value of planets the proposer took from it within `lossWindowTurns` (new record in `PlayerAI.RecordLosses` from `UpdateResultTypeConversion` with `OwnerChanged`).
- **Hostility penalty** = `hostility / hostilityMax x peaceHostilityPenaltyValue` (new tunable, 50 value points at max hostility); the hostility already contains the strength ratio, so a rival that is winning prices peace higher without a separate term.
- **Meter bands** on `net / dealMeterScale` (10): `< -2` Very unlikely, `< -0.5` Unlikely, `< 0` Uncertain, `< 2` Likely, else Very likely. Uncertain means "just short".
- **Fleet items** are `(planet, kind, count)`, not dock indices (the human sees only a count of a rival's ships); the giver's healthiest ships of that kind at that planet go first.
- **Declined** is `applied = true` with `detail` starting `declined`; accepted starts `accepted`. Only invalid proposals are refusals (closed reasons: `EmptyDeal`, `NotOwned`, `LastPlanet`, `Capital`, `TechUnavailable`, `NotAtWar`, plus the existing `DiplomacyOff`, `UnknownRival`, `NoContact`).
- **Receiving a tech** (owner's rule): the tech and every lower tier the receiver lacks (the transitive `requiredTech` chain) are marked researched, their dependent production items flip, each is removed from the research queue, and `currentResearch` is cleared if it is any of them. The tech item's value to the receiver (and to a rival giving it) is the sum of `TechValue` over the items of that chain the receiver lacks, so a deep tech is worth its whole chain.
- The existing End the war / Surrender / Demand surrender buttons are unchanged.

## Review Focus

- A deal that becomes invalid between preview and execution (planet lost, ships gone) applies nothing (Task 3 test).
- The last populated planet and the capital are never given; an empty deal is refused (Task 4 tests).
- A tech the receiver already has is refused; a deep tech whose chain the receiver lacks is accepted and grants every missing tier, queue entries for those tiers vanish (Task 3 test).
- The planet minimum: giver 5 alone converts 3; giver 5 plus a third player 2 converts 3; giver 5 plus third 4 converts 5; one inhabitant converts 1 (Task 3 test).
- A declined deal changes nothing: no transfer, no hostility change, no truce (Task 4 test). Hostility never leaves `0..hostilityMax`.

---

## File structure

- Create `Assets/Flatspace/GameAI/DealModel.cs` (items, deal, verdict, band), `DealEvaluator.cs`, `DealDecision.cs` (the ScoreMatrix types), `DealExecutor.cs` (+ `.meta`s).
- Create `Assets/Editor/DealSelfCheck.cs` (menu `FlatSpace/AI/Run Deal Self-Check`, registered in `AllAISelfChecks`).
- Create `Assets/Flatspace/UI/HumanUI/ProposalDialog.cs`.
- Modify `GameAIConstants.cs`, `PlayerAI.cs`, `Planet.cs`, `HumanCommand.cs`, `HumanCommandProcessor.cs`, `GameBoard.cs`, `DiplomacyScreen.cs`, `AITuningLogger.cs`, the `tuning-log` skill, `CLAUDE.md`.

### Task 1: Tunables, deal model, war-cost records

**Files:** Modify `GameAIConstants.cs`, `PlayerAI.cs`; Create `DealModel.cs`, `Assets/Editor/DealSelfCheck.cs`; modify `AllAISelfChecks.cs`.

**Interfaces:**
- Produces `GameAIConstants`: `dealTruceTurns` 20, `dealHostilityPerValue` 1, `dealHostilityCap` 10, `peacePriceCapFraction` 0.5, `peaceHostilityPenaltyValue` 50, `dealMeterScale` 10 (header "Deals").
- Produces (`DealModel.cs`, namespace `FlatSpace.AI`): `[Serializable] class DealItem { string kind; string name; string planet; int count; }` with `kind` one of `"planet"` (`name` = planet), `"fleet"` (`planet`, `name` = ship kind name, `count`), `"tech"` (`name`), `"endWar"`; `class Deal { List<DealItem> Give, Get; }` (Give = what the proposer gives); `enum DealBand { VeryUnlikely, Unlikely, Uncertain, Likely, VeryLikely }`; `class DealFactor { string Text; bool Helps; }`; `class DealVerdict { float Receive, Give, PeaceWorth, Net, TangibleDelta; DealBand Band; bool Accept; string Refusal; List<DealFactor> Factors; }`.
- Produces `PlayerAI.WarCostToward(int attacker, int turn, float ownFleetValue) : float` and the planet-loss record fed by `RecordLosses` (a `UpdateResultTypeConversion` result for me with `ConversionEvent.OwnerChanged` and `OldOwner == me` records `(turn, AIMap.GetPlanet(name).PlanetValue)` against `NewOwner`; pruned to `lossWindowTurns`).

- [ ] **Step 1: Write the failing checks** in `DealSelfCheck.RunChecks` (`RunConstantsCheck`: the six defaults; `RunWarCostChecks`: build a `PlayerAI` as `PlayerAIResourceSelfCheck` does, feed `RecordLosses` a `WarshipsLost` result (Ships 1, StrengthDrop 5, Engaged 10, Attacker 1) and a conversion result of a planet worth 20 taken by player 1: `WarCostToward(1, turn, 30f) == 0.5 x 30 + 20 = 35`; another attacker gets 0; entries older than `lossWindowTurns` drop out).
- [ ] **Step 2:** the owner's Editor shows compile errors (missing members). **Step 3:** implement the constants, the model file and the `PlayerAI` record/method (extend `RecordLosses`, same pruning style as `_losses`). **Step 4:** register `("Deal", DealSelfCheck.RunChecks)`; the suite passes.
- [ ] **Step 5: Commit** (when asked): `feat: deal model, tunables and war-cost records`.

### Task 2: `DealEvaluator` and the ScoreMatrix decision

**Files:** Create `DealEvaluator.cs`, `DealDecision.cs`; modify `DealSelfCheck.cs`.

**Interfaces:**
- Consumes: Task 1; `PlayerValue.ValueSettings/ShipValue/TechValue/ScoreBreakdown`, `DiplomacyState.Hostility/IsAtWar`.
- Produces `class DealContext { GameAIMap Map; int Proposer, Rival; PlayerValue.ValueSettings Settings; WarshipStats Stats; Func<int, PlayerValue.ScoreBreakdown> ScoreOf; Func<int, int, float> WarCost /* (victim, attacker) */; Func<int, IEnumerable<CatalogItem>> Research /* a player's research catalog items */; }`.
- Produces `static class DealEvaluator { static DealVerdict Evaluate(DealContext ctx, Deal deal); static float ItemValue(DealContext ctx, int owner, DealItem item); static DealBand BandOf(float net, float scale); }` and `static class DealDecision { static bool Accepts(DealVerdict verdict); }` built on `ScoreMatrix<DealDecisionElement, DealChoiceElement, DealAction>` (new element types in `DealDecision.cs` beside `StanceMatrix`'s pattern; `IndependentRows = true`; `ChoiceCompare` orders higher score first, a tie to Accept).
- `Evaluate`: Receive = values of Give items, Give = values of Get items (`ItemValue` with the owning side: planet `Map.GetPlanet(name).PlanetValue`; fleet = sum of `ShipValue` of the first `count` giver ships of that kind at that planet, healthiest first; tech = `TechValue` of the receiver's missing `TechChain` (the item and its lower tiers it lacks); endWar = 0); PeaceWorth only when an `endWar` item is present: `WarCost(Rival, Proposer) - Hostility(Rival, Proposer)/hostilityMax x peaceHostilityPenaltyValue`, floored at `-peacePriceCapFraction x ScoreOf(Rival).Total`; `TangibleDelta = Receive - Give`; `Net = Receive - Give + PeaceWorth`; factors (text only): "The war is costing them" (helps, when war cost > 0), "They are angry with you" (hurts, when the hostility penalty > 0), "They want <item>" per received item, "They would give up <item>" per given item, "They would have to give up the war on you" is not a factor (peace worth already counts).

- [ ] **Step 1: Write the failing checks** (`RunEvaluatorChecks`, fake delegates, no Gameboard): a gift of a planet worth 20 for nothing → `Receive 20, Net 20, Accept`; asking a planet worth 20 for a gift worth 5 → `Net -15, !Accept`, and `BandOf(-15, 10) == Unlikely` (-1.5 is below -0.5 but not below -2); `BandOf` edges: `-25 VeryUnlikely, -10 Unlikely, -3 Uncertain, 0 Likely, 25 VeryLikely` at scale 10; an `endWar` deal with war cost 30, hostility 50/100 (penalty 25) and nothing offered → `PeaceWorth 5, Net 5, Accept`; the same with hostility 100 (penalty 50) → `PeaceWorth -20, !Accept`; the floor: rival score 40 and penalty far above → `PeaceWorth == -20` (0.5 x 40); an exact tie `Net == 0` accepts (`DealDecision.Accepts`); `Factors` colors: an angry rival has a `Helps == false` "They are angry with you".
- [ ] **Step 2: run (compile errors); Step 3: implement; Step 4: run (pass).**
- [ ] **Step 5: Commit** (when asked): `feat: DealEvaluator and the ScoreMatrix accept decision`.

### Task 3: `DealExecutor`

**Files:** Create `DealExecutor.cs`; modify `Planet.cs`, `PlayerAI.cs`, `DealSelfCheck.cs`.

**Interfaces:**
- Produces `Planet.TransferShips(IEnumerable<Ship> ships, int newOwner)`: sets `Owner` and a single fresh `NextFleetId(newOwner)` for the group.
- Produces `PlayerAI.GrantTech(string itemName) : List<string>`: marks the research item and every prerequisite reached by following `requiredTech` (stopping at an empty value or an item already researched) researched, flips production items whose `requiredTech` is any of them (as `CompleteResearch` does, without its notifications or log), removes each from `ResearchQueue`, clears `currentResearch` when it is any of them; returns the names newly granted (empty when unknown or already researched). `PlayerAI.TechChain(string itemName) : List<CatalogItem>` returns the not-yet-researched items of that chain (the item and its lower tiers), used for both the value and the grant.
- Produces `static class DealExecutor { static string Validate(DealContext ctx, Deal deal, Func<int,string> capitalOf); static void Execute(DealContext ctx, Deal deal, int turn, GameAIConstants constants, Func<int, PlayerAI> aiOf); static int ConversionsNeeded(IList<int> populationByPlayer, int giver, int receiver); }`. `Validate` returns `null` or the closed reasons (`EmptyDeal`, `NotOwned`, `LastPlanet`, `Capital`, `TechUnavailable` (the giver lacks it or the receiver already has it), `NotAtWar`). `Execute` applies planets (`ConvertInhabitant` loop until `PlayerWithMostPopulation()` is the receiver), fleets (`TransferShips`), tech (`GrantTech`), and for an `endWar` item calls `GameAI.ApplySurrender` with an order whose `PlayerId` is the proposer and `Data` the rival, `turn`, and `constants.dealTruceTurns`.

- [ ] **Step 1: Write the failing checks** (`RunExecutorChecks`, maps built as in Task 2): the minimum-conversion cases of Review Focus through `ConversionsNeeded` and through a real planet (giver 5 alone: ownership flips after 3, the giver keeps 2; giver 5 + third 2: 3; giver 5 + third 4: 5; one inhabitant: 1); a fleet gift moves exactly `count` ships of that kind, healthiest first, owner changes and the others stay; a tech gift of a tier-3 item to a receiver that has only tier 1 marks tiers 2 and 3 researched, flips their dependent production items, removes both from the queue and clears `currentResearch` if it was one of them (`GrantTech` returns both names); a tech already held grants nothing; an `endWar` deal sets both stances to Peace and stamps `TruceUntil = turn + 20` on both rows; `Validate` refusals: empty deal, a planet the proposer does not own, its last populated planet, its capital (via `capitalOf`), a tech the receiver has, a tech the giver lacks, `endWar` while at peace; after one item becomes invalid `Validate` refuses and a following `Execute` is not called (the caller's contract), and `Execute` itself applies nothing if called with an invalid deal (it re-runs `Validate` first and throws `InvalidOperationException`).
- [ ] **Step 2-4: run (fails), implement, run (passes).**
- [ ] **Step 5: Commit** (when asked): `feat: DealExecutor, ship transfer and tech grant`.

### Task 4: The `propose` command and hostility effect

**Files:** Modify `HumanCommand.cs`, `HumanCommandProcessor.cs`, `GameBoard.cs`, `AITuningLogger.cs`, `DealSelfCheck.cs`.

**Interfaces:**
- Consumes Tasks 1-3.
- Produces `HumanCommand.dealGive`, `HumanCommand.dealGet` (`List<DealItem>`, default empty) and command type `"propose"` (field `rival`).
- Produces `HumanCommandProcessor.DealContext Deal { get; set; }`, `Func<int,string> CapitalOf { get; set; }` and `DealVerdict Preview(HumanCommand command)` (pure: validation refusal in `Verdict.Refusal`, else the evaluation); `Dispatch` case `"propose"`: refusal reasons from `DealExecutor.Validate` plus `HandleStance`'s contact/diplomacy checks; accepted → `Execute`, then the hostility shift `Get(rival, human).Hostility += clamp(-dealHostilityPerValue x TangibleDelta, -cap, +cap)` clamped to `0..hostilityMax` (direct `Get/Set`), `detail` `accepted <terms>`; declined → nothing changes, `detail` `declined <terms>`, `applied = true`. `Check` allows `"propose"` (validation only). Log `Proposal|<rival>|<give>|<get>|<net>|<band>|<accepted>` and `DealHostility|<rival>|<delta>|<hostilityAfter>` (one-offs, no tracker).
- Produces `Gameboard.PreviewDeal(int rival, List<DealItem> give, List<DealItem> get) : DealVerdict` and a `DealContext` builder used by `SubmitHumanCommands` (`ScoreOf` = `PlayerScoreOf`, `WarCost` from the victim's `PlayerAI.WarCostToward(attacker, TurnNumber, fleetValueOf(victim))`, `Research` = each player's catalog, `CapitalOf` = `GetPlayerCapitol(id)?.PlanetName`).

- [ ] **Step 1: Write the failing checks** (`RunProposeChecks`, `HumanModeSelfCheck.Fixture`-style fixture with diplomacy switched on and `Diplomacy.Turn` set, fake `DealContext`): an accepted planet gift flips the planet, lowers the rival's hostility by `min(cap, value)` (planet 20 -> -10), and never leaves `0..100`; a declined deal (asks a planet for nothing) changes no owner, no hostility, no truce and returns `applied` with `detail` starting `declined`; an accepted `endWar` deal sets Peace both ways with the 20-turn truce; the sign of the hostility shift: a deal where the rival gives a planet worth 20 and receives a gift worth 5 but still accepts (positive peace worth) raises its hostility by `min(cap, 15)`, and a peace deal with no tangible items leaves hostility unchanged; refusals `EmptyDeal`, `UnknownRival`, `NoContact`, `DiplomacyOff`; old JSON without the new fields still parses (`HumanCommandProcessor.Parse`).
- [ ] **Step 2-4: run (fails), implement, run (passes).**
- [ ] **Step 5: Commit** (when asked): `feat: propose command, hostility from deals`.

### Task 5: Proposal dialog and reply

**Files:** Create `ProposalDialog.cs`; modify `DiplomacyScreen.cs`, `HumanUiSelfCheck.cs` only if a pure helper is added.

**Interfaces:** Consumes `Gameboard.PreviewDeal`, `Gameboard.SubmitHumanCommands`, `HumanUiKit`, `HumanFleetView.Docked/Fleets`, `HumanPlanetTable`/`Gameboard.HumanOwnerVisible` for what the Add menu lists (my planets and ships with detail; the rival's planets the human has seen and a ship count per kind per visible planet; techs the rival has researched that the human lacks; a deep tech shows `(+N lower tiers)` from `TechChain`). Produces `public static void ProposalDialog.Show(VisualElement host, int rival)`, `ContainsScreenPoint(Vector2)`, `Close()`.

- [ ] **Step 1:** add a **Propose...** button to `DiplomacyScreen.BuildButtons` (enabled with contact and diplomacy on) that opens the dialog; `DiplomacyScreen.ContainsScreenPoint` also tests the dialog; `Close`/`ResetState` close it.
- [ ] **Step 2: build the dialog as layout B** (mockup `.superpowers/brainstorm/*/content/proposal-dialog-b-v2.html`): left, "I give" and "I get" lists, each with an **Add** menu (Planet, Fleet, Tech, End the war with them as a linked pair of rows removed together) and an item row with a remove button; right, "How they see it": the meter (five bands) and colored factor lines from `DealVerdict.Factors` (green helps, red hurts, **no numbers**), plus the reply. It re-previews on every change. **Propose** sends one `propose` command; a refusal shows `HumanUiKit.RefusalSummary` as a toast; `accepted`/`declined` closes the dialog (accepted) or keeps it (declined) and shows the reply.
- [ ] **Step 3: reply:** show an immediate template line from the verdict in the response box (accepted / declined, two or three fixed variants chosen by the tone of `ChatClient.ToneOf`), then `DiplomacyScreen.RequestReply` with a message naming the outcome replaces it when the model answers (make `RequestReply` `internal`/callable from the dialog).
- [ ] **Step 4: verify in Play mode (owner):** open Diplomacy, Propose..., add a planet to I give: the meter moves and a green line appears; propose: the planet flips and the response box answers; ask for a planet for nothing: declined, nothing changes; at war, End the war with a gift: peace and a 20-turn truce show on the screen.
- [ ] **Step 5: Commit** (when asked): `feat: proposal dialog on the Diplomacy screen`.

### Task 6: Docs and final checks

**Files:** `.claude/skills/tuning-log/SKILL.md`, `CLAUDE.md`, `FUTURE_FEATURES.md`.

**Tuning log additions (required):** `T<turn>|P<humanId>|Proposal|<rival>|<give>|<get>|<net>|<band>|<accepted>` (once per proposal, fields compact like `planet:Farm 3;tech:Food Improvement 2`) answers: how often do rivals accept, at what net, is `dealMeterScale` right; `T<turn>|P<rivalId>|DealHostility|<human>|<delta>|<hostilityAfter>` (once per accepted deal) answers: do gifts and bad deals move hostility as intended, does the cap bind. Both fire once per command, so no tracker; added in Task 4, covered by a Play-mode run (the logger has no self-check by design).

- [ ] **Step 1:** document both lines in the `tuning-log` skill; add a "Proposals" paragraph to `CLAUDE.md` under the Diplomacy screen (items, the net formula, the ScoreMatrix decision, hostility shift, truce, tunables, the plan rulings above) and the `DealSelfCheck` suite; move the shipped part of the roadmap entry in `FUTURE_FEATURES.md` (third-party asks stay open).
- [ ] **Step 2: verify:** the owner runs `Run All AI Self-Checks` and a Play-mode match; the log shows `Proposal` lines.
- [ ] **Step 3: Commit** (when asked): `docs: proposals, the deal log lines`.
