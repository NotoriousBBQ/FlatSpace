# Player Control

Summary of everything discussed so far about letting something other than `PlayerAI` control a
player — a human, or an LLM agent. Speculative and exploratory; nothing here has been designed or
built. See **Human player mode** in `FUTURE_FEATURES.md` for the original future-feature entry this
expands on.

## Existing references (found in prior docs)

Two design specs mention this in passing, as forward-looking notes rather than discussion:

- `docs/superpowers/specs/2026-09-08-fog-of-war-design.md`: fog of war exists purely as a
  development/debugging aid, "because every player in FlatSpace is AI-driven and there is no human
  player" — its debug view (No Fog / All Players / Player N) is the closest thing today to
  "looking at the board as a specific player would see it."
- `docs/superpowers/specs/2026-09-14-player-knowledge-design.md`: notes that `PlayerKnowledge` (the
  sticky, per-player discovered-planets set) would be "the natural data source for 'what can this
  player currently see/target in the UI'" if FlatSpace ever supports a human player, independent of
  the debug fog-of-war view.

Neither treats player control as a project of its own; both just flag that a piece they were
building would be reusable if one existed.

## This session's discussion

Prompted by a speculative question: could an LLM (Claude) take over all of `PlayerAI`'s decisions
for a player, using its own reasoning/algorithms instead of `PlayerAI`'s weight tables and
`ScoreMatrix` heuristics, given a hypothetical way to feed it board state and take orders back?

**Feasibility, by decision category:**

- **Good fit for direct LLM judgment:** strategy-phase transitions (Expand→Consolidate), choosing
  among a few colonization candidates, research/production priority weighting, Distribution Center
  placement, assault/blockade targeting. These are low-frequency, qualitative, context-dependent
  calls — the same kind of reasoning already exercised informally throughout this session's
  tuning-log reviews (e.g. diagnosing why Prime 0's DC role changed, or why P2's second DC ended up
  redundant with its first). The LLM has an advantage PlayerAI's static tables don't: it can read
  the *actual* board situation instead of applying a fixed weight table uniformly.
- **Initially flagged as a weak fit, then reconsidered:** the fine-grained numeric optimization
  `ProcessResourceShipments`/`BuildResourceMatrix` does every turn (rounds-based shipment routing
  across every shortage/surplus pair, with capacity capping). Freehanding this via prose reasoning
  would be slow and error-prone. But the question explicitly allows "different methods and
  algorithms," which changes the answer: the LLM isn't limited to intuitive judgment, it can
  construct and run an actual solver each turn (demonstrated in this same session by writing and
  running a real Dijkstra-based script to analyze shipment pass-through rates) — e.g. real
  min-cost-flow instead of `ScoreMatrix`'s greedy round-based heuristic, which this session
  independently found real limits in (the priority-ordering bug fixed today, DCs landing
  redundantly close together, chronic per-player coverage gaps). Under that framing, every decision
  category PlayerAI makes is plausibly replaceable, not just the strategic ones.
- **Turn cadence** (the game currently runs a turn every ~0.1s, and an LLM call takes seconds) was
  raised as a practical blocker, then explicitly set aside by the user as out of scope for this
  discussion. It remains a real constraint if this were ever built: a 400-turn match with every
  decision re-derived by an LLM every turn is a lot of paid, non-deterministic LLM calls, which
  matters if reproducible tuning-log comparisons (this session's primary tool) are still wanted
  afterward.

**What building the interface would take** (scoped, not designed):

1. **State export (perception).** No new format needed — `SaveLoadSystem.GameSave` already
   serializes nearly everything relevant (planets, resources, connections, ownership, in-flight
   orders, per-player catalogs, DC lists, `PlayerKnowledge`). Mainly plumbing: snapshot it to a file
   after each turn's `GameAI.GameAIUpdate()`. Open question: full board state (what `PlayerAI`
   itself already sees, reading `AIMap` directly) vs. fog-scoped to what that player actually knows.
2. **Order injection (actuation).** `PlayerAI.ProcessResults(results, orders)` just appends to a
   shared `List<GameAI.GameAIOrder>` that `GameAI` consumes uniformly regardless of source — a
   clean existing seam. An external source should supply **directives** ("ship 15 food A→B",
   "colonize target X", "prioritize Grotsits research"), routed through the existing trio-emission
   helpers (`EmitResourceOrders`, the colonization order sequence), so the external caller never
   hand-derives `TimingDelay`/cost math or the deduction/in-progress order pairs itself.
3. **Turn synchronization.** The manual single-step entry point already exists
   (`Gameboard.SingleUpdate()`, wired to the `NextTurnButton`) and is the natural foundation: pause
   per-turn, wait for that turn's external orders, then call `SingleUpdate()` once — "Next Turn"
   driven externally instead of by a click. Needs a per-player "externally controlled" switch
   (analogous to the existing `AIStrategy` field) so `GameAI.ProcessResults` skips calling
   `PlayerAI.ProcessResultsStrategyExpand` for that player and waits on the external source instead.
4. **Transport.** Pragmatic MVP is file-based, matching the project's existing conventions
   (BoardConfigs, GameSaves, tuning logs are all already JSON on disk): write `state.json`, block
   until `orders.json` updates, read it, proceed. No new infra needed for a first version.

**Relationship to Human player mode:** this would build the same backend that future-feature entry
needs. A human-facing version is just a UI translating clicks into the same order JSON and reading
the same state snapshot — building this for an LLM effectively delivers that feature's plumbing too.

## Status

Not pursued. Recorded for future reference at the user's request; revisit via the brainstorming
skill (this is architectural-scope work — a new subsystem, and a change to how `GameAI.ProcessResults`
dispatches per player) if it's picked back up.
