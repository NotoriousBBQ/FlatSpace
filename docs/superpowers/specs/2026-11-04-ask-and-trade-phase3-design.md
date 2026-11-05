# Ask and trade, phase 3: asking a rival to go to war or make peace with a third player

**Revision 2026-11-04 (owner's request):** the peace ask ("make peace with X") now validates on EFFECTIVE war between the rival and X (`IsAtWar(rival, X, true)`) and an accepted one sets BOTH stances to Peace
(no truce), so the owner can ask the victim of a declaration to make a cheaper peace with the aggressor. The `startWar` rules are unchanged. A second peace ask is refused `NotAtWarWithThem` because no war is
left. The "still at war with them; the fighting may go on" factor is removed (the caveat is false), the price is unchanged (the asked rival's view), and each stance that really changed logs its own `Stance` line (P = the player whose stance changed).
Where the text below still reads "the rival's own stance" for the peace ask, this note wins.

Date 2026-11-04. Roadmap entry: "Diplomacy, project 2: ask and trade" in `FUTURE_FEATURES.md` (the "Still open: phase 3" part). Builds on `docs/superpowers/specs/2026-10-30-ask-and-trade-design.md`
(goal 4, "Third-party asks") and on phase 2 as built (`CLAUDE.md`, "Proposals (ask and trade, phase 2)"). Decisions are the owner's unless marked **(my call)**.

Standing rules for the builder (owner's "standard guidelines"): **reuse and modify existing components rather than writing parallel ones, and make every decision through
`ScoreMatrix`.** Here that means: the accept/decline stays `DealDecision.Accepts` (a one-row `ScoreMatrix`, unchanged); the new ask is one more term in the existing `Net`
computed by `DealEvaluator`, not a second evaluator; the execution goes through the existing stance orders (`GameAI.ApplyStanceOrder`).

## Intent

In the proposal dialog the human can ask a rival to **declare war on** or **make peace with** a third player X. The rival answers the way it answers every deal: one number,
`Net`, from its own point of view, shown on the existing acceptance meter, accepted when `Net >= 0`. The human can sweeten the ask with the ordinary "I give" list
(planets, fleets, tech). The answer is never arbitrary: it follows from how the rival feels about the human and about X, how strong each is, and what it has lost to X.

## Goals

1. Two new asks in the "I get" list: **Ask to declare war on X** and **Ask to make peace with X**, X being any other player the human has met, at war or at peace with the human.
2. The rival's willingness is computed from the factors below and joins `Net`; the meter, the green/red factor lines and the reply template work for it like for any item.
3. An accepted ask takes effect **instantly**, like every other deal item, and binds the rival to nothing afterwards.
4. The same "I give" list as today pays for it. Nothing new can be offered.

## Non-goals and anti-goals

- No new thing the human can offer (no promise to join a war, no "I get" for the human's own stance). **(my call)** The asks go only in "I get"; the spec of 2026-10-30
  allowed "Start war with" in "I give" as a promise by the human, which the owner's answer ("same 'I give' list as today, nothing new") rules out.
- No lasting commitment: no truce, no treaty, no memory of the promise. After an accepted ask the rival's `StanceMatrix` decides on its next free turn as for any stance.
  (A declared war or a made peace is held for `stanceHoldTurns` like any stance change, because `ApplyStanceOrder` stamps the change turn; that is the existing rule, not new.)
- Not the **Demand surrender** button (still inactive; it was grouped with phase 3 in the roadmap but the owner did not ask for it here). **(my call)**
- Not the chatbot prompt expansion (deal facts in the prompt): the roadmap's separate follow-up. The existing template reply only gains lines for the new factors.
- Not alliances, "ask X to join", asking a rival to ask a third party, or the human being the third party.
- Anti-goal: the meter or the factor lines disagreeing with the real decision. Anti-goal: an ask no offer could ever buy (a rival that fears X must still be buyable at a finite price,
  as with peace, `peacePriceCapFraction`). Anti-goal: an ask that does nothing and is accepted (war on someone it is already at war with).

## How the rival values the ask

Everything below is **from the rival's point of view** and is in the valuation's value units (the same units as planets, fleets and tech), so it adds into `Net` beside the
items. Call the result `ThirdPartyWorth` (a new field on `DealVerdict`, next to `PeaceWorth`):

`Net = Receive - Give + PeaceWorth + ThirdPartyWorth`

The owner's factors, and how each enters:

| Factor (owner's) | Declare war on X | Make peace with X |
|---|---|---|
| Rival's hostility toward the human | worth falls as it rises (the same penalty term as peace: `hostility / hostilityMax x peaceHostilityPenaltyValue`) | the same |
| Rival's hostility toward X | worth rises (it wants war with someone it hates) | worth falls (it wants to keep fighting someone it hates) |
| The human's strength relative to the rival | **(my guess)** stronger human: worth rises (the rival wants the favour of a strong player); weaker: falls | the same direction |
| X's strength relative to the rival | stronger X: worth falls (a dangerous war) | stronger X: worth rises (it wants out of a war with a strong X) |
| The value of the human's offer | already in `Net` as `Receive` | already in `Net` |
| Losses | **remembered losses to X** (see below): worth falls by the war cost X imposed on the rival | **current losses to X**: worth rises by that war cost |

Reuse, not new machinery:

- **Hostility toward X** goes through `StanceMatrix.WarProbability(hostility, constants)` (0..1, the logistic the rival's own stance decision uses). This is the roadmap entry's
  suggestion ("the rival's own `StanceMatrix` weighting toward that player"). War worth gets `+ WarProbability x warAskHostilityValue`, peace worth gets `- WarProbability x warAskHostilityValue`
  (the same signal, opposite sign).
- **Losses** are `PlayerAI.WarCostToward(attacker = X, turn, rivalFleetValue)`, already behind `DealContext.WarCost(victim, attacker)`: called as `WarCost(rival, X)`. This is the
  rival's loss share to X x its fleet value plus the value of planets X took from it, over `lossWindowTurns` (10). **(my call)** "Current losses" for a peace ask and "remembered
  losses" for a war ask are the *same* data over the *same* window; they differ only in direction of effect. A war that ended more than `lossWindowTurns` ago is forgotten, which is
  what "remembered, if any" will mean in practice. A longer memory for war asks would need new state and is not built.
- **Direction of the war-ask loss term is my guess:** remembered losses to X make the rival *wary* of a new war with X (worth falls), the mirror of "current losses make it want peace".
  The opposite reading (revenge: worth rises) is possible. **The owner should confirm this.**
- **Strength** is a log2 ratio clamped to +-2, the shape `HostilityCalculator` already uses for `hostilityStrengthWeight`, times a new `thirdPartyStrengthValue`. Which "strength" is the one the
  AI already uses: `FleetStrength` (docked warships' Offense x (Health + Defense)). The human's and the rival's strengths are what the Diplomacy screen already shows
  (`Gameboard.FleetStrengthOf` / the screen's numbers); X's strength is read **as the rival can see it** (the planets it knows), not as the human sees it. The builder wires this through one new
  `DealContext` delegate (e.g. `Strength(player, asObserver)`), reusing the existing fleet-strength code and not a second formula. **(my call)** If the rival does not know where any of X's warships sit,
  the strength term is **dropped (0)**, not read as "X has no fleet"; the knowledge test behind `UnknownThirdParty` ("the rival knows X") is the same `PlayerKnowledge` contact test the strength delegate uses.
  The builder-check found no existing observer-based strength helper, so this delegate is the one small new piece; say so in the plan.
- **Price cap:** `ThirdPartyWorth` is clamped from below at `-peacePriceCapFraction x rival's score` (the same constant and the same reason as peace), so every ask is buyable. **(my call)**
  Both a war ask and a peace ask are capped by the one constant; a separate constant can be added if tuning needs it.
- **Decision:** unchanged. `DealDecision.Accepts` (the one-row `ScoreMatrix`, tie to Accept). Both asks can sit in one deal with items, with `endWar` and with each other; their worths add.

**Baseline and combining (settled after the builder check, my calls):**
- Each term is **centred on zero for a neutral rival**, so a bare ask is not accepted or refused by an accident of the formula. Hostility toward X enters as `(WarProbability(hostility toward X) - 0.5) x warAskHostilityValue`
  for a war ask and `(0.5 - WarProbability(...)) x warAskHostilityValue` for a peace ask; 0.5 is reached at hostility `stanceMidpoint` (30). The strength terms are log2 ratios, already zero at equal strength.
  Loss terms are zero with no losses. So a neutral rival (no anger at the human, hostility toward X at the midpoint, equal strength, no losses) has `ThirdPartyWorth` 0, and a net of 0 accepts (tie to Accept).
  In practice most rivals sit well below the midpoint on X at the start, so a bare war ask usually costs something (about 25 at hostility 0 with the 50 starting value) and must be paid for with the "I give" list.
- The **hostility-toward-the-human penalty is applied once per deal**, however many of `endWar` and the asks the deal holds, and so is the **price cap**: the floor `-peacePriceCapFraction x rival's score`
  applies to the combined `PeaceWorth + ThirdPartyWorth` (the penalty is computed once and shared, not summed per term). The builder restructures `DealEvaluator.Evaluate` so the shared pieces are computed once.
- A loss term that is zero most of the time (a war ask when the rival never fought X) is expected and fine: build it, the sign comes from the owner's confirmation below.

Starting tunables (new, on `GameAIConstants`, section "Deals", with in-code defaults; **starting points for tuning from the log, not decisions**):
`warAskHostilityValue` 50 (the size of "it hates X", same scale as `peaceHostilityPenaltyValue`), `thirdPartyStrengthValue` 20. The loss term needs none: it is already in value units.

## Rules for a valid ask (closed refusals)

The ask is validated by `DealExecutor.Validate` (so the dry-run `Check`, the meter's preview and the real proposal all use one rule), with new closed reasons:

- `UnknownThirdParty`: the named player does not exist, is the human (the proposer) or is the rival being asked; or the human has not met X (X must be in `HumanMetPlayers`, the Diplomacy
  screen's list; the rival must also know X, **(my call)** which in practice is the same contact test the screen uses for the rival).
- `AlreadyAtWar`: a war ask when **the rival's own stance toward X is already War**. **(my call, from the builder check)** The test is the rival's *own* stance, not the effective state `IsAtWar(rival, X, true)`:
  effective war also counts X's stance toward the rival, so a second peace ask after the rival set its own stance to Peace (X still at War) would pass, be accepted and change nothing, which is the anti-goal.
  A war ask while the rival's own stance is Peace but X has declared on it (a forced war) is valid and meaningful: it turns the forced war into the rival's own.
- `NotAtWarWithThem`: a peace ask when **the rival and X are not at EFFECTIVE war** (`IsAtWar(rival, X, true)`: either side's stance is War, no truce; revised 2026-11-04, see the revision note at the end). **(my call)** Naming: do not reuse `NotAtWar`, which means "the human and the rival are not at war" for `endWar`.
- `Truce`: a war ask while the rival and X are in a truce (`ApplyStanceOrder` already refuses a declaration then; the ask must be refused up front, not accepted and silently ignored).
- `DuplicateItem`: the same ask twice, or a war ask and a peace ask naming the same X in one deal. **(my call)**

Existing refusals still apply to the rest of the deal (`NoContact`, `DiplomacyOff`, `EmptyDeal`, ...). A deal containing only an ask (nothing in "I give") is a valid, if usually
low-odds, proposal.

## What happens on acceptance (instant)

`DealExecutor.Execute` applies the ask with the **rival's own stance order toward X**, via `GameAI.ApplyStanceOrder` (the `OrderTypeDeclareWar` / `OrderTypeMakePeace` path with the rival as
`PlayerId` and X in `Data`), so the usual `Stance` log line, the war-forced logic for X, and `PlayerAI.ApplyWarState` all see an ordinary stance change. The pair's last-change turn is stamped by that
function. Nothing else changes: no truce (unlike a bought `endWar`), no hostility shift from the ask itself.

- **Peace with X sets both stances (revised 2026-11-04):** an accepted `endWarWith` sets the rival's stance toward X AND X's stance toward the rival to Peace, each through `ApplyStanceOrder`
  (for X's stance `PlayerId` = X and `Data` = the rival). A stance already at Peace reports "not changed". No truce. A `startWar` still sets only the rival's own stance.
- If a declaration on X is applied while X is the target, X is an AI and the existing `ApplyStanceOrder(.., targetIsHuman: false)` rules apply.
- Validation runs again at execution; any failure throws before anything is applied (all or nothing), as phase 2 already does.
- The existing hostility shift after an accepted deal is **unchanged** and uses `TangibleDelta` (the items only), so an ask contributes nothing to it. A gift attached to an ask
  moves hostility exactly as a plain gift would.

## The dialog and the words

- `ProposalDialog`: the Add menu gains **Ask to declare war on...** and **Ask to make peace with...**, each opening a list of the met players that currently pass validation (so the human
  cannot pick a pointless one); the chosen ask appears as a row in "I get" and can be removed like any row. Every edit re-previews, as today.
- `HumanDiplomacyView`/`HumanCommand`: `DealItem` gains the two kinds, `"startWar"` and `"endWarWith"` **(my call on names; the builder may rename)**, plus a way to name X.
  **Pitfall:** player 0 is a real player and an unset `int` is 0, so X must not be an `int` that defaults to 0 (use the existing string `name` field to carry the id, or a nullable/-1 default).
  `HumanCommand` is `JsonUtility`-serialised: keep new fields additive, an older save or command has none of them.
- `ProposalText`: the green/red factor lines for the new terms ("They hate X", "They fear X", "X has cost them dearly", "They are angry with you", ...) and the reply templates, in the same
  shape and style as the peace lines. The local-model reply (chat) is untouched and does not see these facts in this project.
- Fleet-detail and visibility rules are unchanged; the factor lines name X but say nothing the human cannot already see (X's strength, for example, is shown only as a band such as
  "stronger than them", never a number, **(my call)**).

## Logging (required by the plan rule)

- The existing `Proposal|<rival>|<give>|<get>|<net>|<band>|<accepted>` line carries the new asks in `<get>` through `HumanCommandProcessor.Terms` (e.g. `startWar:3`, `endWarWith:3`). Nothing to track:
  one line per proposal, as today.
- **New, proposed:** `DealAsk|<rival>|<startWar or endWarWith>|<third>|<worth>|<hostToMe>|<hostToThird>|<strengthTerm>|<lossTerm>` (P = the human), one per ask per proposal, so the log shows
  which factor decided an ask and whether `warAskHostilityValue` and `thirdPartyStrengthValue` are in the right range. No tracker needed (it fires per command).
- Update the `tuning-log` skill (`.claude/skills/tuning-log/SKILL.md`) and the "Proposals" paragraph of `CLAUDE.md` for the new item kinds, the `DealAsk` line and the three tunables.

## Saves

None. The asks are commands that execute at once; stances already persist (`PlayerSave.stances`); the loss window is not saved and is read as it stands (a load restarts it, like the rest of
the window state).

## Self-check

Extend `Assets/Editor/DealSelfCheck.cs` (already in `Run All AI Self-Checks`); no new suite. Cases the owner would want proven: each refusal above; a war ask accepted when the rival hates X and
the human offers value, declined when it does not; a peace ask worth more after the rival lost ships to X (using `WarCost` as the existing checks fake it); the same ask declined at hostility
to the human 100 and accepted after a gift; the price cap makes a feared X buyable (the "winning rival is buyable" pattern); execution sets the rival's stance toward X (and, for a peace ask, X's toward the rival, see the revision note) and stamps the
change turn; all-or-nothing when a second item fails; the verdict and the meter agree. A self-check must not touch `Gameboard.Instance` (build a minimal `GameAIMap`/`PlayerAI` set).

## What exists and what is new

- **Reuse:** `Deal`/`DealItem`/`DealVerdict`/`DealFactor`, `DealEvaluator.Evaluate` (extend it), `DealExecutor.Validate`/`Execute` (extend), `DealDecision.Accepts` (untouched), `DealContext.WarCost`,
  `StanceMatrix.WarProbability`, `GameAI.ApplyStanceOrder`, `HumanCommandProcessor.HandlePropose`/`ProposalRefusal`/`Terms`, `ProposalDialog`, `ProposalText`, `AITuningLogger.LogProposal`, `Gameboard.HumanMetPlayers`.
- **New:** two item kinds, `ThirdPartyWorth` and its terms, a strength delegate on `DealContext`, three-ish refusals (`UnknownThirdParty`, `AlreadyAtWar`, `NotAtWarWithThem`, `Truce`), two tunables, the
  dialog menu entries and words, the `DealAsk` log line, self-check cases.

## Left to the builder

The exact shape of each term (linear versus logistic, signs inside the clamps), how X is carried on `DealItem`, the starting values of the two tunables, the wording of the factor lines and reply templates,
how the dialog lists valid players, and the fixture layout of the new self-check cases. If the builder finds a factor cannot reuse existing code as described here, say so before writing the new code.

## Open for the owner

1. The direction of the remembered-losses term for a war ask (wary, my guess, versus revenge).
2. Whether a stronger human should make a rival *more* willing (my guess) or whether "my relative strength" works the other way.
