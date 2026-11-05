# Ask and Trade Phase 3 (third-party war and peace asks) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** The human can ask a rival to declare war on, or make peace with, a third player X, as items in the proposal dialog's "I get" list, answered by the existing deal evaluator and carried out instantly.

**Architecture:** Two new `DealItem` kinds (`startWar`, `endWarWith`; X's id in `name`) add a `ThirdPartyWorth` term to the existing `DealEvaluator.Evaluate` net, validated by the existing `DealExecutor.Validate` and applied by `DealExecutor.Execute` through `GameAI.ApplyStanceOrder`. The accept/decline stays `DealDecision.Accepts` (a one-row `ScoreMatrix`), untouched. Dialog, words and log follow the existing proposal path.

**Tech Stack:** Unity 6 C#, UI Toolkit (built in code), the project's Editor self-check pattern (no test framework).

**Spec:** `docs/superpowers/specs/2026-11-04-ask-and-trade-phase3-design.md` (read it first; it carries the reasoning and the owner's decisions). Parent: `docs/superpowers/specs/2026-10-30-ask-and-trade-design.md`. Roadmap entry: "Diplomacy, project 2: ask and trade" in `FUTURE_FEATURES.md`.

## Global Constraints

- Standard guidelines (owner's rule): **reuse and modify existing components; make decisions through `ScoreMatrix`.** No second evaluator, no second decision routine. If a step cannot reuse something, stop and tell the owner before writing the new code.
- Simulation code stays pure: nothing under `Assets/Flatspace/GameAI/` touches `Gameboard.Instance`; a self-check never depends on `Gameboard.Instance` (build a minimal `GameAIMap`/`PlayerAI` set, as `DealSelfCheck` does).
- A method a self-check calls is `public`, not `internal` (`Assets/Editor/` is a separate assembly).
- Tunables live on `GameAIConstants` under `[Header("Deals")]` with in-code defaults (the constants asset needs no edit): `warAskHostilityValue = 50f`, `thirdPartyStrengthValue = 20f`. Starting points for tuning, not decisions.
- Item kinds are exactly `"startWar"` and `"endWarWith"`; X's player id is carried as a decimal string in `DealItem.name` (player 0 is real, so no `int` field that defaults to 0). Both go only in `Deal.Get`.
- New refusal strings are exactly `UnknownThirdParty`, `AlreadyAtWar`, `NotAtWarWithThem`, `Truce` (plus the existing `DuplicateItem`, `UnknownKind`).
- Signs (owner confirmed 2026-11-04): remembered losses to X **lower** a war ask's worth (wary); a stronger proposer **raises** both asks' worth.
- `OrderType` and saves: no new order type, no new save field.
- Line endings: working tree is CRLF, index LF; edit with the Edit tool, never a script that converts `\n` twice.
- Commits: end each message with `Co-Authored-By: Claude Sonnet 5.5 <noreply@anthropic.com>`. Commit freely; do not push (push only on the owner's OK).
- Unity cannot run from the CLI. "Run the self-check" means: the owner focuses the Unity Editor so it recompiles, then runs `FlatSpace -> AI -> Run Deal Self-Check` and reads the Console for `[DealSelfCheck] ALL PASSED`. Plan to ask the owner at each such step; an empty Rider problem list is not proof of compiling.

## Review Focus

Input classes the spec implies that no single task's happy path exercises (each gets its test in the named task):

1. A second peace ask after the rival's own stance already went to Peace while X still holds War must be refused, not accepted-and-ignored (Task 2: `NotAtWarWithThem` tests the rival's *own* stance).
2. An ask naming the rival itself, the human, a nonexistent id (`"9"`), a non-number (`"x"`) or an unmet player must refuse `UnknownThirdParty` and never throw (Task 2).
3. A war ask combined with `endWar` must charge the anger penalty once and apply the price floor once to the combined total (Task 1).
4. A rival that cannot see any of X's warships (visible strength 0) must drop the strength term, not read X as harmless (Task 1).
5. A deal where a second item fails validation must apply nothing, including the stance change (Task 2).

---

## File Structure

| File | Change |
|---|---|
| `Assets/Flatspace/GameAI/GameAIConstants.cs` | two tunables |
| `Assets/Flatspace/GameAI/DealModel.cs` | `DealVerdict.ThirdPartyWorth`; `DealItem` doc line for the kinds |
| `Assets/Flatspace/GameAI/DealEvaluator.cs` | `DealContext.Strength`/`Met` delegates; `Evaluate` restructured |
| `Assets/Flatspace/GameAI/DealExecutor.cs` | new refusals in `Validate`; stance application in `Execute` |
| `Assets/Flatspace/Player/Human/HumanCommandProcessor.cs` | copy the new context fields; `Terms`; `DealAsk` log call |
| `Assets/Flatspace/Objects/Board/GameBoard.cs` | fill `Strength`/`Met` in `BuildDealContext` |
| `Assets/Flatspace/Diagnostics/AITuningLogger.cs` | `LogDealAsk` |
| `Assets/Flatspace/Player/Human/ProposalText.cs` | row/describe text, refusal sentences, factor wording |
| `Assets/Flatspace/UI/HumanUI/ProposalDialog.cs` | Add-menu entries and the player pick list |
| `Assets/Editor/DealSelfCheck.cs` | the new cases (extend the existing suites; no new suite) |
| `.claude/skills/tuning-log/SKILL.md`, `CLAUDE.md`, `FUTURE_FEATURES.md`, `completed_features.md` | docs (Task 5) |

No new scripts, so no new `.meta` files.

---

### Task 1: Tunables, the verdict field and the evaluator term

**Files:**
- Modify: `Assets/Flatspace/GameAI/GameAIConstants.cs` (the "Deals" block, after `peaceHostilityPenaltyValue`)
- Modify: `Assets/Flatspace/GameAI/DealModel.cs` (`DealVerdict`, `DealItem` comment)
- Modify: `Assets/Flatspace/GameAI/DealEvaluator.cs` (`DealContext`, `Evaluate`)
- Test: `Assets/Editor/DealSelfCheck.cs` (`RunConstantsCheck`, `RunEvaluatorChecks`)

**Interfaces:**
- Consumes: `StanceMatrix.WarProbability(float hostility, GameAIConstants constants)`; `DiplomacyState.Hostility(me, rival)`; `DealContext.WarCost(victim, attacker)`; `DealContext.ScoreOf(player).Total`.
- Produces:
  - `GameAIConstants.warAskHostilityValue` (float, 50), `thirdPartyStrengthValue` (float, 20).
  - `DealVerdict.ThirdPartyWorth` (float).
  - `DealContext.Strength` : `Func<int, int, float>`, `Strength(observer, subject)` = the warship strength `observer` can see of `subject` (observer == subject = its own full strength). May be null (every strength term is then 0).
  - `DealContext.Met` : `Func<int, bool>`, true when the proposer has met that player. May be null (Task 2 then falls back to `Knowledge.ContactPlayers(map, Proposer)`).
  - `public static bool DealEvaluator.IsAsk(DealItem item)` : true for kind `startWar` or `endWarWith`.
  - `public static int DealEvaluator.AskTarget(DealItem item)` : the id in `item.name`, or -1 when it does not parse (invariant culture).
  - `Evaluate` rule: `Net = Receive - Give + PeaceWorth + ThirdPartyWorth` as before, where `ThirdPartyWorth` is the sum of the asks' centred terms and `PeaceWorth` is the combined shared remainder (below).

- [ ] **Step 1: Write the failing tests** in `RunConstantsCheck` (defaults 50 and 20) and a new method `RunAskEvaluatorChecks()` called from `RunChecks` after `RunEvaluatorChecks`. Build the fixture like `RunEvaluatorChecks` does (a `GameAIMap` of two planets, `DealContext` with fake delegates; add `Strength = (obs, subj) => strength[(obs, subj)]` over a dictionary the test edits; players: human 0 = proposer, rival 1, third player 2). Assertions, with `H(a,b)` = `map.Diplomacy.Hostility(a,b)` set through `Diplomacy.Set`, `P(h)` = `StanceMatrix.WarProbability(h, constants)`:
  - neutral war ask (rival→X hostility 30, rival→human 0, all strengths 100, `WarCost` 0): `ThirdPartyWorth` ≈ 0, `Net` ≈ 0, `Accept` true (tie to Accept).
  - war ask at rival→X hostility 0: `ThirdPartyWorth` ≈ `(P(0) - 0.5f) * 50f` (about -24.97) and `Accept` false; the same ask plus a `planet` gift worth 30 in `Give`: `Accept` true.
  - peace ask at hostility 0 is the mirror: `ThirdPartyWorth` ≈ `(0.5f - P(0)) * 50f`.
  - human strength 200 vs rival 100 adds +20 to a war ask and to a peace ask; X strength 400 vs rival 100 subtracts 40 from a war ask and adds 40 to a peace ask (log2 clamped to +-2 times 20); human 1000 vs 100 still adds only +40 (clamp).
  - `Strength(rival, 2)` returns 0 (rival cannot see X): the X term is 0, not a bonus; same for `Strength(rival, rival)` 0 (all strength terms 0).
  - `WarCost(1, 2)` = 30: a war ask lowers worth by 30, a peace ask raises it by 30.
  - hostility toward the human 100: both asks lose the full `peaceHostilityPenaltyValue` (50) once.
  - `endWar` + a war ask in one deal, rival→human hostility 50, `WarCost(1, 0)` 30: the penalty (25) is subtracted once: `PeaceWorth + ThirdPartyWorth` == `max(30 + askTerms - 25, floor)`.
  - floor: rival score 40, `peaceHostilityPenaltyValue` 500: combined worth is `-20`, and a gift of 20 then nets 0 and accepts (the existing "winning rival is buyable" pattern, now for an ask).
  - existing `endWar`-only assertions in `RunEvaluatorChecks` stay unchanged and pass (PeaceWorth 5, -20, floor).
  - factor lines exist: one `Helps = true` and one `Helps = false` line appear for a war ask with hostility to X high and hostility to the human high (exact texts are in Task 4's `ProposalText`; assert only on `Helps` and count here).
  - a null `Strength` delegate evaluates without throwing and leaves strength terms at 0.

- [ ] **Step 2: Ask the owner to focus the Editor and run the Deal Self-Check; expect compile errors or FAIL lines for the new members.**

- [ ] **Step 3: Implement**
  1. `GameAIConstants`: add the two fields with one-line comments under "Deals".
  2. `DealVerdict`: `public float ThirdPartyWorth;` beside `PeaceWorth`.
  3. `DealContext`: add `Strength` and `Met` with doc comments.
  4. `DealEvaluator`: add `IsAsk` and `AskTarget`. In `Evaluate`, compute the shared pieces **once**: `hasAsk`, `endsWar`, `penalty = hostility(rival, proposer) / hostilityMax x peaceHostilityPenaltyValue`, `floor = -peacePriceCapFraction x ScoreOf(rival).Total`. For each ask in `deal.Get`: `x = AskTarget(item)` (skip when < 0), `war = item.kind == "startWar"`, then the term is the sum of
     - hostility: `(P - 0.5f) x warAskHostilityValue` with `P = StanceMatrix.WarProbability(hostility(rival, x), constants)`, negated for a peace ask;
     - proposer strength: `+clampLog2(Strength(rival, proposer) / Strength(rival, rival)) x thirdPartyStrengthValue` for both kinds;
     - X strength: `-clampLog2(Strength(rival, x) / Strength(rival, rival)) x thirdPartyStrengthValue` for a war ask, `+` for a peace ask;
     - losses: `WarCost(rival, x)` negated for a war ask, positive for a peace ask;
     where `clampLog2(ratio)` is `Mathf.Clamp(Mathf.Log(ratio, 2f), -2f, 2f)` and any strength term is 0 when `Strength` is null or either operand is <= 0. Set `verdict.ThirdPartyWorth` to the sum of the terms. Then, when `endsWar || hasAsk`: `raw = (endsWar ? ctx.WarCost(rival, proposer) : 0f) + ThirdPartyWorth`; `combined = Math.Max(raw - penalty, floor)`; `PeaceWorth = combined - ThirdPartyWorth`. When neither is present both stay 0. Keep the existing "The war is costing them" and "They are angry with you" factors (add the anger factor once, also for an ask-only deal when `penalty > 0`). Add one factor per ask per non-zero term (Helps = the term is > 0); the wording comes from `ProposalText` in Task 4, so build the text here with a private helper `AskFactorText(kind, term)` returning a placeholder that Task 4 replaces by calling `ProposalText.AskFactor(...)`. `Net` formula unchanged.
  5. Do not touch `DealDecision`.

- [ ] **Step 4: Ask the owner to run the Deal Self-Check; expect `[DealSelfCheck] ALL PASSED` (including every pre-existing case).**

- [ ] **Step 5: Commit**

```bash
git add Assets/Flatspace/GameAI/GameAIConstants.cs Assets/Flatspace/GameAI/DealModel.cs Assets/Flatspace/GameAI/DealEvaluator.cs Assets/Editor/DealSelfCheck.cs
git commit -m "feat: third-party ask worth in the deal evaluator"
```

---

### Task 2: Validation and execution of the asks

**Files:**
- Modify: `Assets/Flatspace/GameAI/DealExecutor.cs` (`Validate`, `Execute`, refusal constants)
- Test: `Assets/Editor/DealSelfCheck.cs` (`RunExecutorChecks`)

**Interfaces:**
- Consumes: Task 1's `DealEvaluator.IsAsk/AskTarget`, `DealContext.Met`; `DiplomacyState.StanceToward(me, rival)`, `.InTruce(a, b, turn)`; `GameAI.ApplyStanceOrder(DiplomacyState, GameAIOrder, int turn, bool targetIsHuman = false)`; `Knowledge.ContactPlayers(map, player)`.
- Produces: string constants `DealExecutor.UnknownThirdParty`, `AlreadyAtWar`, `NotAtWarWithThem`, `Truce`; `Validate`/`Execute` handle the two kinds.

Validation rules (in `Validate`'s per-item switch; the item is only legal in `Get`, giver = the rival):
- the kind appears in `Deal.Give` -> `UnknownKind`;
- `x = AskTarget(item)`; `x < 0`, `x == ctx.Proposer`, `x == ctx.Rival`, not met by the proposer (a nonexistent id is never a contact, so no separate existence test) (`ctx.Met != null ? ctx.Met(x) : ContactPlayers(map, Proposer).Contains(x)`), or not known to the rival (`ContactPlayers(map, Rival).Contains(x)`) -> `UnknownThirdParty`;
- `startWar`: rival's own stance toward x already War -> `AlreadyAtWar`; `InTruce(Rival, x, Diplomacy.Turn)` -> `Truce`;
- `endWarWith`: rival's own stance toward x is not War -> `NotAtWarWithThem`;
- duplicates: the same (kind, name) twice, or a `startWar` and an `endWarWith` naming the same x -> `DuplicateItem`.

`Execute` applies each ask with `GameAI.ApplyStanceOrder(ctx.Map.Diplomacy, new GameAIOrder { Type = OrderTypeDeclareWar or OrderTypeMakePeace, TimingType = Immediate, PlayerId = ctx.Rival, Data = x }, turn)`; x is an AI in the normal case (`targetIsHuman` stays false because x is never the proposer).

- [ ] **Step 1: Write the failing tests** (a new method `RunAskExecutorChecks()` called from `RunChecks`, three-player fixture like `RunExecutorChecks`, with `Diplomacy.Enabled = true`, `Diplomacy.Turn = 5`, contact set through `Knowledge.SetKnownPlanets` with planets docking each player's ship so `ContactPlayers` lists them; copy the helper pattern of `RunExecutorChecks`):
  - `Validate` returns null for a legal war ask (rival 1 at Peace with 2, both met) and a legal peace ask (rival's own stance toward 2 is War).
  - `UnknownThirdParty` for `name` = `"0"` (the proposer), `"1"` (the rival), `"9"`, `"x"`, `""`, and for a player the proposer has not met (`Met = p => p != 2`).
  - `UnknownKind` for either kind placed in `Give`.
  - `AlreadyAtWar` for a war ask when rival's own stance toward 2 is War; `NotAtWarWithThem` for a peace ask when it is Peace.
  - Review Focus 1: set rival's own stance toward 2 to Peace but player 2's stance toward the rival to War (effective war on): a peace ask refuses `NotAtWarWithThem`; a war ask validates.
  - `Truce` for a war ask while a truce is stamped on the pair (`TruceUntil` > turn).
  - `DuplicateItem` for the same ask twice, and for `startWar` + `endWarWith` naming the same player.
  - `Execute` of a war ask sets only `StanceToward(1, 2)` to War, stamps `LastChangeTurn` = turn, leaves `StanceToward(2, 1)` and both stances with the human alone, and starts no truce; `Execute` of a peace ask sets `StanceToward(1, 2)` to Peace only.
  - Review Focus 5: a deal with a valid war ask plus an invalid second item (a planet the rival does not own) throws `InvalidOperationException` and leaves `StanceToward(1, 2)` unchanged.
  - `startWar` + `endWar` + a gift in one deal executes all three.

- [ ] **Step 2: Ask the owner to run the Deal Self-Check; expect FAILs for the new cases.**

- [ ] **Step 3: Implement** the constants, the `Validate` cases and the `Execute` cases in `DealExecutor.cs` as specified above. Reuse `Sides`. Keep `Validate` the single source of truth (the processor's `Check` and `Preview` already call it).

- [ ] **Step 4: Ask the owner to run the Deal Self-Check; expect `ALL PASSED`.**

- [ ] **Step 5: Commit**

```bash
git add Assets/Flatspace/GameAI/DealExecutor.cs Assets/Editor/DealSelfCheck.cs
git commit -m "feat: validate and execute third-party war and peace asks"
```

---

### Task 3: Wiring through the processor, the board and the log

**Files:**
- Modify: `Assets/Flatspace/Player/Human/HumanCommandProcessor.cs` (`ProposalRefusal` context copy at ~line 396, `Terms` at ~447, `HandlePropose` at ~465)
- Modify: `Assets/Flatspace/Objects/Board/GameBoard.cs` (`BuildDealContext`, ~line 454)
- Modify: `Assets/Flatspace/Diagnostics/AITuningLogger.cs` (next to `LogProposal`, ~line 182)
- Test: `Assets/Editor/DealSelfCheck.cs` (`RunProposeChecks`; the `DealScene` fixture)

**Interfaces:**
- Consumes: Tasks 1 and 2; `FleetStrength.Mine(GameAIMap, int player, WarshipStats)` and `FleetStrength.VisibleOf(GameAIMap, int observer, int subject, WarshipStats)` (the exact helpers `PlayerAI.UpdateDiplomacy` uses for hostility); `Gameboard.HumanMetPlayers()` (a `SortedSet<int>`).
- Produces:
  - `AITuningLogger.LogDealAsk(int turn, int human, int rival, string kind, int third, float worth, float hostToHuman, float hostToThird, bool accepted)` writing `T<turn>|P<human>|DealAsk|<rival>|<kind>|<third>|<worth>|<hostToHuman>|<hostToThird>|<accepted 1/0>`, numbers `"0.##"` invariant, no-op when no match is logging (the existing pattern).
  - In `ProposalRefusal`: the new `DealContext` copies `Deal.Strength` and `Deal.Met`.
  - In `BuildDealContext`: `Strength = (observer, subject) => ...` : `observer == subject ? FleetStrength.Mine(map, subject, stats) : FleetStrength.VisibleOf(map, observer, subject, stats)` with `stats = new WarshipStats(<observer's research catalog items>)` (null-safe like the existing lambdas), 0 for an invalid id; `Met = id => HumanMetPlayers().Contains(id)`.
  - `Terms` renders the kinds as `startWar:<id>` and `endWarWith:<id>` (they already fall to the `default` branch as `kind:name`; add an explicit comment, no code change if it already prints that way).
  - `HandlePropose` logs one `DealAsk` per ask item (after the verdict, with `verdict.Accept`) using the hostility values read **before** `Execute`: `Diplomacy.Hostility(rival, human)` and `Diplomacy.Hostility(rival, x)`, and `worth` = that ask's own term (expose it from the evaluator as `verdict.AskTerms`: a `List<(string kind, int third, float worth)>` filled in Task 1's loop; add the field in this task's first step if Task 1 did not).

- [ ] **Step 1: Write the failing tests** in `RunProposeChecks` (extend `DealScene` to add a third player: `AttachThird()` giving player 2 a ship at a planet and contact; the scene's `Processor.Deal` gets `Strength = (o, s) => 100f` and `Met = _ => true`):
  - `Preview` of a war ask returns no refusal, a band, and `Accept` false at rival→2 hostility 0; with a 30-value planet gift added `Accept` true (the dialog's meter path).
  - `Propose` (accepted) with a gift + war ask: `StanceToward(1, 2)` is War afterwards, the planet changed hands, hostility toward the human shifted exactly as the gift alone would (the ask adds nothing to `TangibleDelta`), `result.detail` starts `accepted` and contains `startWar:2`.
  - `Propose` declined (bare war ask at hostility 0): `applied` true, detail starts `declined`, stance unchanged, nothing else changed.
  - `Check` of a peace ask when the rival is not at war with 2 returns `NotAtWarWithThem`; `Preview` of it carries `Refusal == "NotAtWarWithThem"`.
  - a fixture whose `Deal.Strength` is null still previews and proposes (Review Focus 4 at the processor level).
  - the log method does not throw when no match is logging (call `AITuningLogger.LogDealAsk` directly once).

- [ ] **Step 2: Ask the owner to run the Deal Self-Check; expect FAILs.**

- [ ] **Step 3: Implement** the context copy, `BuildDealContext` delegates, `LogDealAsk`, the per-ask logging in `HandlePropose`, and the `AskTerms` list. No other change to `HandlePropose`'s flow (hostility shift stays `-dealHostilityPerValue x TangibleDelta`).

- [ ] **Step 4: Ask the owner to run the Deal Self-Check; expect `ALL PASSED`. Then ask them to run `FlatSpace -> AI -> Run All AI Self-Checks` for regressions in the neighbouring suites.**

- [ ] **Step 5: Commit**

```bash
git add Assets/Flatspace/Player/Human/HumanCommandProcessor.cs Assets/Flatspace/Objects/Board/GameBoard.cs Assets/Flatspace/Diagnostics/AITuningLogger.cs Assets/Editor/DealSelfCheck.cs Assets/Flatspace/GameAI/DealEvaluator.cs Assets/Flatspace/GameAI/DealModel.cs
git commit -m "feat: wire third-party asks through the processor, board context and tuning log"
```

---

### Task 4: The dialog and the words

**Files:**
- Modify: `Assets/Flatspace/Player/Human/ProposalText.cs` (`RefusalSentence`, `RowText`, `Describe`, new `AskFactor`)
- Modify: `Assets/Flatspace/GameAI/DealEvaluator.cs` (replace Task 1's placeholder `AskFactorText` with `ProposalText.AskFactor`)
- Modify: `Assets/Flatspace/UI/HumanUI/ProposalDialog.cs` (`BuildMenu`, `Options`, `Remove` untouched for asks)
- Test: `Assets/Editor/HumanUiSelfCheck.cs` (the existing `ProposalText` checks)

**Interfaces:**
- Consumes: Tasks 1 to 3; `HumanDiplomacyView.Label(int player)` (the dialog already uses it for the rival's name); `Gameboard.HumanMetPlayers()`; `HumanCommandProcessor.Check` is not needed (the dialog filters with the same rules through `PreviewDeal`'s refusal).
- Produces:
  - `ProposalText.AskFactor(string kind, string thirdLabel, string term, bool helps) : string` with `term` one of `"hostility"`, `"proposerStrength"`, `"thirdStrength"`, `"losses"`; `ProposalText.RefusalSentence` gains the four reasons; `RowText`/`Describe` handle both kinds (`"Ask them to declare war on <label>"`, `"Ask them to make peace with <label>"`; label = `HumanDiplomacyView.Label(id)` from the item's `name`, falling back to `"player " + name`).
  - Dialog Add menu (only in "I get"): two buttons, **Ask them to declare war on...** and **Ask them to make peace with...**, each opening a list (`_menuCategory` = `"askWar"` / `"askPeace"`, handled in `Options`) of the met players other than the rival for which a preview of just that ask returns no `Refusal` (build a one-item `Get` list and call `Gameboard.Instance.PreviewDeal(_rival, empty, thatAsk)`; "Nothing to add." shows when empty). Picking one adds the `DealItem` and re-previews. `InDeal`/`SameItem` already stop a duplicate.

- [ ] **Step 1: Write the failing tests** in `HumanUiSelfCheck` next to the existing `ProposalText` checks (find them with `grep -n "ProposalText" Assets/Editor/HumanUiSelfCheck.cs`): `RowText` and `Describe` for both kinds (non-empty, contain the label, differ between kinds); `RefusalSentence` returns a non-default sentence (not equal to the reason string) for each of the four new reasons; `AskFactor` returns non-empty distinct text for each (kind, term, helps) combination used (2 kinds x 4 terms x 2 signs, all 16 distinct); `SameItem` treats two asks with the same kind and `name` as equal and different `name`s as different.

- [ ] **Step 2: Ask the owner to run `FlatSpace -> UI -> ` the Human UI self-check (list with `ls Assets/Editor/*SelfCheck*.cs`; the menu path is in `HumanUiSelfCheck.cs`); expect FAILs.**

- [ ] **Step 3: Implement** the text and the dialog changes. Wording is the builder's (match the tone of the peace lines: "They hate X", "They fear X", "X has cost them dearly", "They are angry with you", "You are strong enough to matter to them"); a factor never names a number and never reveals more than the Diplomacy screen already shows (X's strength appears only as "stronger than them" / "weaker than them"). The dialog menu is a small addition to `BuildMenu`/`Options`; do not restructure the existing categories.

- [ ] **Step 4: Ask the owner to run the Human UI self-check and the Deal Self-Check; expect both pass. Then ask the owner for a Play-mode look: start a human match with 3+ players, open Diplomacy (T), pick a met rival, open the proposal dialog, add each ask, watch the meter move as items are added, propose one and read the banner and toast. Report what they see (what was not looked at stays unverified).**

- [ ] **Step 5: Commit**

```bash
git add Assets/Flatspace/Player/Human/ProposalText.cs Assets/Flatspace/UI/HumanUI/ProposalDialog.cs Assets/Flatspace/GameAI/DealEvaluator.cs Assets/Editor/HumanUiSelfCheck.cs
git commit -m "feat: ask entries, rows and words in the proposal dialog"
```

---

### Task 5: Tuning-log skill and documentation

**Files:**
- Modify: `.claude/skills/tuning-log/SKILL.md` (the "Proposals (ask and trade, phase 2)" paragraph, ~line 126)
- Modify: `CLAUDE.md` (the "Proposals (ask and trade, phase 2)" section: the `Open:` bullet, the `propose` command bullet, Tunables, Logging)
- Modify: `FUTURE_FEATURES.md` (the ask-and-trade entry: remove "Still open: phase 3" for the asks; keep Demand surrender and the chatbot follow-up as open), `completed_features.md` (move the third-party asks)

**Interfaces:** none (documentation).

- [ ] **Step 1: Update the skill** with the `DealAsk` line: fields, when it fires (once per ask per proposal, accepted or declined), what to read from it (which of `worth`, `hostToHuman`, `hostToThird` decides asks; if accepted asks cluster at worth near 0 the starting values are too tight; if every bare ask is declined `warAskHostilityValue` is too high), and the invariant "a `DealAsk` is always preceded by its `Proposal` line on the same turn; an accepted war ask is followed by a `Stance` line only if another system logs it (this feature logs none)".
- [ ] **Step 2: Update `CLAUDE.md`**: the two item kinds and their id-in-`name` carrier, the four refusals, the rival's-own-stance rule, the `ThirdPartyWorth` terms and their signs, the shared penalty/floor, the two tunables, `DealAsk`, and that `Demand surrender` and the chat-prompt facts remain open. Keep it as short as the neighbouring bullets.
- [ ] **Step 3: Update `FUTURE_FEATURES.md` and `completed_features.md`** (the `/update_feature_list` command's job; do it by hand now if the owner has not run it).
- [ ] **Step 4: Commit**

```bash
git add .claude/skills/tuning-log/SKILL.md CLAUDE.md FUTURE_FEATURES.md completed_features.md
git commit -m "docs: ask and trade phase 3 (asks, DealAsk log line, tunables)"
```

---

## Self-Review (done against the spec)

- **Spec coverage:** asks in "I get" only (Tasks 2, 4); factors table and signs (Task 1); baseline, shared penalty and floor (Task 1, Review Focus 3); refusals including the rival's-own-stance rule (Task 2); instant execution through `ApplyStanceOrder`, no truce, no hostility shift from the ask (Tasks 2, 3); dialog menu, rows, factor words, refusal sentences (Task 4); logging, skill and CLAUDE.md (Tasks 3, 5); self-check extension, no new suite (Tasks 1 to 4); saves none. Non-goals (Demand surrender, chat prompt facts, promises in "I give") appear in no task.
- **Type consistency:** `DealContext.Strength(observer, subject)` and `Met(id)` are defined in Task 1 and used identically in Tasks 2 and 3; `DealEvaluator.IsAsk/AskTarget` in Task 1 are the only parsers; refusal constants are the strings in Global Constraints.
- **Deviation recorded:** the spec said the builder finds no observer-strength helper; one exists (`FleetStrength.VisibleOf`, used by `PlayerAI.UpdateDiplomacy`), so the strength delegate is a thin wrapper, not new logic.
- **Tuning log section (project rule):** `DealAsk` is the one new line (Task 3, documented in Task 5); it answers "which factor decided an ask and are `warAskHostilityValue` 50 and `thirdPartyStrengthValue` 20 in range". It needs no tracker (it fires per command, not per turn). The `Proposal` line already carries the asks through `Terms`.
- **Not covered by an automated test:** the dialog layout and the real board context (`BuildDealContext`'s `Strength`/`Met` delegates read `Gameboard`, which self-checks must not touch). Both need the Play-mode look in Task 4 Step 4.
