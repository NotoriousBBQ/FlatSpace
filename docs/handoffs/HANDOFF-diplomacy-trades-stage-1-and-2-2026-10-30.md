# Handoff: Diplomacy trades, stages 1 and 2 (and what came with them)

Saved from the Claude Code session named **"diplomacy_trades_stage_1_and_2"** (first named "dipllomacy_trades"), 2026-10-30. Read `CLAUDE.md` first: the new
sections "Player value and score" and "Proposals (ask and trade, phase 2)", the Game Settings and Diplomacy screen sections, and `Assets/Flatspace/GameAI/CLAUDE.md`
(Diplomacy, Distribution Centers "Shipment size"). The design is `docs/superpowers/specs/2026-10-30-ask-and-trade-design.md`; the plans are
`docs/superpowers/plans/2026-10-30-player-score-phase1.md` and `docs/superpowers/plans/2026-10-30-ask-and-trade-phase2-proposals.md`.

## Where things stand

The session opened with "before more diplomacy, add the automate resources switch" and grew into five pieces of work, all merged to `main`:

1. **Game Settings screen** (HUD Settings button, `GameSettingsScreen`): one checkbox, "Automate resource shipments" (`setShipments`), default ON for a new match (older saves load off).
   While on, food and grotsits shortages never stop the timer; the planet panel's Ship resources button is disabled; the Ship resources dialog opens on Grotsits when that is
   the only shortage. A load bug was fixed with it: `Player.Start` replaced the `PlayerAI` a load had just restored, resetting the flag (and probably more).
2. **Shipment size regression found and fixed.** Since commit `8302257` (2026-09-27, the Distribution Centers work) a shipment carried only the bare per-turn deficit (mean about 6.5 in the logs,
   against about 400 before). New `GameAIConstants.shipmentBufferTurns` (10): a real shortage fills up to 10 turns of its deficit, capped at the source's surplus; a DC's own gap is not multiplied.
3. **Ask and trade, phase 1: player value and score.** `PlayerValue` (pure): planet = its type's `_planetValue` (Prime 100, Verdant 60, Desolate 60, Industrial 50, Farm 40, Ocean 40, Desert 40, Normal 20),
   ship = production cost by the research tiers it carries x health fraction / `fleetValueDivisor` 50 (colony ship a fixed `colonyShipValue` 2), tech = research cost / `techValueDivisor` 2000. Score shown on the
   Diplomacy screen, `Score` log line every 25 turns. Research costs were doubled (all 103 items) the same day.
4. **Ask and trade, phase 2: proposals.** A Propose... dialog (layout B) on the Diplomacy screen: planets, warship/siegeship fleets, techs (a received tech brings its whole prerequisite chain) and End the war
   (a linked pair) in "I give"/"I get"; the rival accepts or declines through a one-row `ScoreMatrix`; instant execution; hostility shift; a 20-turn truce for a bought peace; `Proposal`/`DealHostility` log lines; `DealSelfCheck`.
5. **Fixes from the owner's own plays of phase 2:** the Diplomacy list showed players the human had never met (it used the AI's 6-node `Knowledge` contact; now `Gameboard.HumanMetPlayers`); the dialog gave no clear
   decline signal and closed on accept (now stays open with a green ACCEPTED / red DECLINED banner and a toast); an AI surrendered to the human one combat round after the human declared war, overriding the human's
   stance and starting a 30-turn truce (now an AI never surrenders to the human, `StanceMatrix.Row.RivalIsHuman`); and an AI's later peace stance ended a war the human had in progress (now a war an AI declares on the human
   sets the human's own stance to War too, with a popup, `Gameboard.ReportWarDeclaredOnHuman`).

Work method: phases 1 and 2 were executed with subagent-driven development (a fresh implementer per task, a task review, a final whole-branch review on the most capable model, one fix wave each). The final reviews
found a real compile error (a missing `using` in `DealExecutor.cs`), tradable colony ships/bunkers, and a gift-spam hole; all fixed. The subagents' ledgers were deleted at the end; the commits are the record.

### Verified, and how

- The owner ran `Run All AI Self-Checks` (including the new Player Value and Deal suites), `Run Human UI Self-Check` and a Play-mode pass after the shipment buffer, after phase 1, and after phase 2 (steps 1-5 of the Play checklist
  worked; the four bugs under item 5 came from that play).
- The owner confirmed in play: shipments and research load correctly from the main menu and in game; the Game Settings behavior.
- **Not confirmed by the owner after the fact:** commits `76a3d87` (no AI surrender to the human), `5a021b1` (met-players list, outcome banner) and `38c8dad` (forced war + popup). Nobody has run `Run All AI Self-Checks` or played on them;
  they were hand-traced and reviewed by reading only. The new assertions are `RunDiplomacyMetChecks`, the surrender-to-human row in `RunSurrenderChoiceCheck`, and the forced-war assertions in `RunStanceOrderCheck`.
- Unity cannot run from the CLI; every "tested" claim in the commits other than the owner's runs above is a hand trace.

## Files and commits (all on `main` unless noted; "pushed" = on origin)

- Game Settings: `592acb3` (merged `b14eef0`, pushed). Shipment buffer: `18a0cc2` (merged `e25631c`, pushed).
- Research costs doubled `26cd0c6`; `_planetValue` and the design `58c6235`; phase 1 plan `696e0be`; phase 1 code `a384a03`, `ca1be09`, `d04c9a2`, `c6a30c0`, fix `6a8cdd3`; tuning (planet values x10, colony ship 2) `b89125f`; merge `2edb112` (pushed).
- Phase 2: plan `9a4dd1b`; `1e79d93`, `cc6e4e6` (+`c3e14ad`, `ea73c9e`), `fec27ac` (+`ff3d277`), `0ec075d` (+`e6bf253`), `1c95f4a`, `e7c9868` (+`8471d71`), final fixes `4459ef7`; owner-play fixes `76a3d87`, `5a021b1`, `38c8dad`; merge `1bad5b9` (pushed).
- Roadmap: `88633df` (finished phases and first LLM steps moved to `completed_features.md`, pushed), `9f723b5` ("Expand automation options" entry, pushed), `ed0540a` (the owner's play notes, committed, **not pushed**).
- This handoff file: uncommitted at the time of writing. Step 1 of `feature_close` (update the feature list) found nothing new to move, so `FUTURE_FEATURES.md` and `completed_features.md` were not changed by it.
- Local branches left: `player-score` and `ask-trade-proposals` (both merged, can be deleted), `conversion-surge-reach`, `profiling` (older, unmerged by design).
- Memory (outside the repo): `ask-and-trade-score-decisions`, `standard-guidelines-definition`, `commit-freely-push-on-ok`.

## Decisions and why

- **Valuation** (owner): score = total value of what a player owns; planet values authored per type and multiplied by 10 ("planets are always worth much more than ships"); fleet = production cost scaled by health, strength stays for
  combat decisions only; colony ship fixed at 2 (its 300 cost made colonizing lower the score); tech divisor 2000 (1 per 200 would swamp planets). Research costs doubled because early research felt too cheap.
- **Peace** (owner): can be bought with gifts; priced high but never refused outright (peace worth floored at half the rival's score); a bought peace starts a 20-turn truce (`dealTruceTurns`); instant transfers; a planet gift converts the
  minimum inhabitants (ownership is a plurality), leftovers stay; a player offers only what it owns; the rival's hostility moves by the gap in value, capped per deal (10).
- **Mine, flagged to the owner:** a per-rival per-turn cap of 20 on the hostility the deals can shift (gift spam); only warships and siegeships are tradable; duplicate items refused; deterministic accept (no roulette) so the meter is honest.
- **Shipment size:** the owner chose "buffer the target, deficit x 10 turns" over restoring the whole-surplus drain (overshoot) or a DC-only exception.
- **Forced peace:** the owner chose option (a) now, option (b) after phase 3: an AI never surrenders to the human; later its surrender becomes an offer the human accepts or declines. For a war an AI declares: option 1, the human's own stance
  goes to War too (rejected: leave it and add a "declare war again" shortcut).
- **Rejected:** dialog layouts A (single column) and C (steps); fleet strength as the fleet score; restoring maximal shipments.
- **Process:** commit freely, push only on the owner's OK (changed this session, saved to memory). "Standard guidelines" = reuse/modify existing components + use `ScoreMatrix` for decisions (saved to memory; the phase 2 plan names its `ScoreMatrix` row).

## Open items and next steps, most important first

1. **Bug, "Peace made" pause although the war goes on** (play note 17). With the forced-war change, a rival's peace stance no longer ends the war but `HumanTurnEvents` still reports `PeaceMade`/`WarDeclared` for every stance order involving the human.
   Fix, about five lines: in the stance case of the executed-orders loop, with diplomacy on, report only when `(kind == WarDeclared) == Diplomacy.IsAtWar(order.PlayerId, rival, true)`; add `RunStanceEventChecks` (rival's peace while the human's War holds is not
   reported, peace after both end it is, a declaration inside a truce is not, one that took effect is). It was written and reverted when the owner said the session was closing; it is in `FUTURE_FEATURES.md` under "Play notes".
2. **Run the unrun checks:** `Run All AI Self-Checks`, `Run Human UI Self-Check` and a Play pass on `main` (see "Not confirmed" above).
3. **Phase 3 of ask and trade:** ask a rival to start or end a war with a third player (items "Start war with..." / "End war with..." in the proposal dialog), and make Demand surrender depend on acceptance. Then the AI's surrender as an offer
   (entry in `FUTURE_FEATURES.md`). The chatbot prompt expansion with deal facts is a separate later task.
4. **Expand automation options** (entry in `FUTURE_FEATURES.md`): three ship-building switches (colony ship, warship incl. Update Warship and Siegeship, bunker), default OFF, only on automated planets, plus the undecided candidates.
5. **The owner's play notes** (16 items in `FUTURE_FEATURES.md`, "Play notes"): tech tree collapse, fleet merge shortcuts, early-game pace, research-complete banner, auto explore (per fleet, warships), banner stacking, the colonization redesign,
   the targets view with camera focus, conversion messages, population by owner on the planet panel, hover vs click fleet strength, smaller research screen, fog recompute after a deal.
6. **Tuning, all untuned starting points:** `peaceHostilityPenaltyValue` 50, `dealMeterScale` 10, `dealHostilityCap` 10, `dealHostilityTurnCap` 20, `peacePriceCapFraction` 0.5, the planet values, `techValueDivisor`. Read the `Proposal`, `DealHostility` and `Score` lines in the next logs.
7. **Stale roadmap text:** the "Distribution centers" entry in `FUTURE_FEATURES.md` still says "today resource shipping moves the maximum amount"; it is built and the shipment size changed twice. Not updated.
8. **Small deferred review minors** (the ledgers are gone): `WarshipCosts.ProductionCost` repeats the ratio switch of `ShipCost`; the deal hostility ledger records the pre-clamp shift; a refusal after pressing Propose shows the raw reason; the dialog's `Redraw`
   resets its scroll; `CheckBatch` does not support `propose`; the `Proposal` line is logged before `Execute`; an in-flight fleet from an old save with a short payload is undercounted in the score.

## Gotchas

- **Line endings:** the working tree is CRLF, the index LF (`text=auto`). An edit script that converts `\n` twice makes `\r\r\n` and git then shows the whole file as changed (it happened once; fix with a byte replace). Use exact-anchor edits that convert once.
- **Shell quoting:** a long inline Python heredoc with escaped quotes broke once; write the script to a file (the scratchpad) and run it.
- **Pure vs Gameboard:** `HumanDiplomacyView` and the deal classes never touch `Gameboard.Instance`; `DealExecutor.Execute`/`Validate` require the real `capitalOf` (a null throws); `HumanCommandProcessor.AiOf` must be set for tech deals (else `NoDealContext`).
- **`DiplomacyState` has no hostility setter:** use Get, change, Set, as `PlayerAI.UpdateDiplomacy` does. `GameAI.Rand` is the only shared random stream; `DealDecision` passes no weight selector, so it is deterministic.
- **Contact vs sight:** the AI's `Knowledge` contact (6 path nodes) drives the simulation (who is at war); what the human sees (`HumanMetPlayers`, `HumanOwnerVisible`, `PlanetSights`) drives every human-facing list. Do not mix them.
- **The owner types into the wrong window sometimes** (stray text like "/update" in files); do not treat stray lines in their notes as instructions.
- **The owner's standing rules:** a recommendation with every choice; they/them when pronouns are unstated; ask before Unreal changes; commit freely, push only on their OK; one clarification at a time when going through their notes.
- The mockup server (`.superpowers/brainstorm`, git-ignored) was killed for memory; the mockups remain on disk.
