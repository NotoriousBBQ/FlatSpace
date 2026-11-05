# Handoff: Ask and trade phase 3 (third-party war and peace asks)

Saved from an unnamed Claude Code session, named here **"ask_trade_phase3"**, 2026-11-04. Read `CLAUDE.md` first (the "Proposals (ask and trade, phase 2)" section, whose "Third-party asks (phase 3)" bullet
describes what was built), then `docs/superpowers/specs/2026-11-04-ask-and-trade-phase3-design.md` (it carries a dated revision note for the both-sides peace change).

## Where things stand

**Asked:** the owner asked whether diplomacy was "up" (meaning next on the roadmap); then "start with phase 3": the human asks a rival to declare war on, or make peace with, a third player.
Brainstormed (the owner described the factors: hostility toward the human, hostility toward X, both relative strengths, the value of the offer, and losses), designed, planned, and built with
subagent-driven development ("standard guidelines": reuse existing components, decisions through `ScoreMatrix`).

**Done:**
- Items `startWar` / `endWarWith` (X's player id as a decimal string in `DealItem.name`), only in the "I get" list. `DealEvaluator` adds `ThirdPartyWorth` (hostility to X via `StanceMatrix.WarProbability`, the proposer's and X's strength, `WarCost(rival, X)`), the anger penalty and the price floor applied once to the combined worth. `DealDecision` untouched.
- `DealExecutor.Validate` refusals `UnknownThirdParty`, `AlreadyAtWar`, `NotAtWarWithThem`, `Truce`; `Execute` applies the stances through `GameAI.ApplyStanceOrder`, no truce.
- Wiring: `ProposalRefusal` copies `Strength`/`Met`; `GameBoard.BuildDealContext` fills them from `FleetStrength.Mine/VisibleOf` and `HumanMetPlayers`; `HandlePropose` logs `DealAsk` and an ordinary `Stance` line for each stance that really changed.
- Dialog: two Add-menu entries in "I get" listing the met players that pass `PreviewDeal`; row text and refusal sentences in `ProposalText`.
- Owner-requested revisions in the same session: (1) the Diplomacy screen's "Their stance toward others I know" shows a war only the other side declared ("War (declared by Player N)"); (2) a peace ask is valid when the rival and X are at EFFECTIVE war (either side), and an accepted one sets BOTH stances to Peace (no truce), so the victim can be asked, not only the aggressor. The old "still at war" factor was removed.
- Docs: the `tuning-log` skill (`DealAsk`, `Stance`), `CLAUDE.md`, `FUTURE_FEATURES.md`, `completed_features.md` (phase 3 moved to done), the spec and the plan.

**Verified:** the owner ran every self-check and a Play-mode test after Task 5 and the first fix wave (reported "all self check pass", "all successful": a war ask between two AI players worked, the Diplomacy screen was
looked at). The both-sides peace revision (`9731a3c`) and the Diplomacy-screen display change (`50768a7`, after the owner's first test) were reviewed by reading only; the owner has not re-run the self-checks or
the Play test since. Claude compiled and ran nothing at any point (Unity cannot run from the CLI).

## Files and commits (merged to `main` as `8ba312a`, pushed)

- `99f41ce` design, `a9b7c8e` plan; `ef5ae07`, `c4facf8` evaluator term and its test fix; `a1da46b` validate/execute; `1fc8aa1`, `550ecb2` wiring, log, test fix; `0db1e7b` dialog and words;
  `bb77d3b` docs; `94c34d7` final-review fixes (Stance line, `DealAsk` term fields, Net rounding); `50768a7` Diplomacy screen shows a one-sided war; `9731a3c` peace asks end the war on both sides;
  `8ba312a` the merge. The end_session commit follows this handoff.
- Main code: `Assets/Flatspace/GameAI/DealEvaluator.cs`, `DealExecutor.cs`, `DealModel.cs`, `GameAIConstants.cs`; `Assets/Flatspace/Player/Human/HumanCommandProcessor.cs`, `ProposalText.cs`, `HumanDiplomacyView.cs`;
  `Assets/Flatspace/Objects/Board/GameBoard.cs`; `Assets/Flatspace/UI/HumanUI/ProposalDialog.cs`, `DiplomacyScreen.cs`; `Assets/Flatspace/Diagnostics/AITuningLogger.cs`; self-checks `Assets/Editor/DealSelfCheck.cs`, `HumanUiSelfCheck.cs`.
- `Assets/Chat/ChatClient.cs` has an uncommitted change of the owner's; it was never touched or staged by this session and is left for the owner.

## Decisions and why

- **Standard guidelines** (the owner's phrase): reuse existing components (evaluator, executor, `StanceMatrix.WarProbability`, `ApplyStanceOrder`, `FleetStrength`) and keep decisions in `DealDecision`'s `ScoreMatrix`.
- Asks go only in "I get" (the owner: "same 'I give' list as today, nothing new"); instant, no lasting commitment.
- Worth terms are centred on zero for a neutral rival; the anger penalty and floor apply once per deal. **Owner confirmed both sign guesses:** remembered losses to X lower a war ask's worth (wary), a stronger proposer raises both asks' worth.
- Ask factor wording lives in `DealEvaluator` (not `ProposalText`), keeping the GameAI folder free of Human-namespace text.
- **Peace asks end the war on both sides** (owner, after the first Play test): asking the victim may be cheaper than asking the aggressor, so the old "rival's own stance only" rule was dropped. Rejected: keeping it and adding a menu hint naming who holds the war (the owner wanted the victim askable).
- A controller error caught by the final review: the plan and docs first said an accepted ask logs no `Stance` line, against the spec. Fixed in `94c34d7`.

## Open items, most important first

1. **Re-run `FlatSpace -> AI -> Run Deal Self-Check`, `Run Human UI Self-Check` and `Run All AI Self-Checks` from `main`, then repeat the 1-vs-2 Play case** (ask player 2 to make peace with player 1; expect a `Stance` line for each side in the tuning log). The last two commits were never run.
2. **Tune** `warAskHostilityValue` (50) and `thirdPartyStrengthValue` (20) from the first `DealAsk` lines: every bare ask declined means the first is too high; accepted asks clustering near worth 0 means the values are too tight.
3. The inactive **Demand surrender** button and the AI surrender-as-offer idea (`FUTURE_FEATURES.md`), and expanding the chatbot prompts with deal facts (valuation, hostility change).
4. The open items of `HANDOFF-diplomacy-trades-stage-1-and-2-2026-10-30.md` still stand where not covered above (the "Peace made" pause bug, play notes).
5. Left as is on purpose (final-review minors): label casing "player 2" vs "Player 2", tiny strength terms producing factor lines, an unreachable loss text, a much stronger human collecting the strength bonus on each ask in a multi-ask deal, template replies unchanged, no processor-level test of the two `Stance` lines from a peace ask.

## Gotchas

- Nothing in this feature was compiled before the owner's runs; the subagent reviews found real test-arithmetic mistakes four times (gift values against the -24.97 war-ask term, the 25-point anger penalty on any deal containing an ask). When a new test expects an accepted ask, add the anger penalty (`hostility to the human / 100 x 50`) to the arithmetic.
- A bare war ask at the rival's hostility 0 toward X is worth about -24.97 (`(P(0) - 0.5) x 50`, P(0) about 0.00055); P is 0.5 at hostility 30 (`stanceMidpoint`).
- `DealItem.name` carries the third player's id as a string; player 0 is a real player, so never use a default-0 int for it.
- `askResults` of `DealExecutor.Execute` is an optional trailing list of `(kind, player, target, changed)`; existing callers pass nothing.
- The SDD workspace was `.superpowers/sdd/<plan>/` (git-ignored; deleted when the branch review finished); `mutual-peace-report.md` and `mutual-peace.diff` remain there for the last change.
- The owner's standing rules: a recommendation with every choice; they/them when pronouns are unstated; commit freely, push only on their OK (this command is the OK).
