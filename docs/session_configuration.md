# Session configuration: rules for code changes

Rules for any session that changes code in this repository. `CLAUDE.md` describes the architecture and conventions; this
file says how to approach a change.

## 1. Read FUTURE_FEATURES.md first

Before designing or changing anything, read `FUTURE_FEATURES.md` (repo root).

- It holds the roadmap, the ideas that are recorded but not started, the design decisions already agreed with the user,
  and the evidence behind them (tuning-log findings, numbers, rejected options).
- Check whether the work you are about to do is, or touches, an existing entry. If it is, follow that entry's notes and
  say which entry you are working from; do not re-derive or contradict a decision it records without telling the user.
- Features that are finished are moved to `completed_features.md` (see the `/update_feature_list` command); look there
  too when a feature seems to exist already.

## 2. Use existing code, algorithms and architecture

When working on a feature, build on what is already in the codebase.

- Before writing new code, search for what already does the job (a planner, a matrix, a log pattern, a self-check
  helper, an order type, a save field) and reuse or extend it. Follow the patterns of the surrounding code: its
  naming, its comments, its way of handling saves, logging and tests (`CLAUDE.md` lists the conventions).
- Prefer a small change to an existing algorithm over a parallel implementation of the same idea. If you generalise
  something that other features use, keep its behaviour for them unchanged and keep their existing self-checks passing.
- **If reuse is not possible, say so before writing the new code:** explain what you looked at and why it does not fit,
  and propose the smallest change that would let it be reused. Wait for the user's decision when the alternative is a
  new algorithm or a new architectural piece.

## 3. Tuning change preferences

When you suggest a tuning change (something that makes the AI or the economy behave differently), look in these
places first, in this order, and say which one you chose and why before proposing anything else.

**ScoreMatrix and related AI choices** (what to build, research, ship or colonize): express the change as a
situational weight, using the structure of `PlayerAI.GetIndustryWeight`.

- The weight stays `GetIndustryStrategyWeight(item, strategy)` x `GetIndustrySituationalWeightMultiplier(item, planetName)`.
  The strategy tables hold the fixed per-strategy values; every factor that depends on live game state (for example the
  ColonyShip "already have one" and "ready to colonize" rules, the Warship fleet-shortfall multiplier, the blockaded
  planet boost) goes inside the situational multiplier.
- Do not put state-dependent logic into the strategy tables or into `ScoreMatrix` itself. Give a new factor a tunable on
  `GameAIConstants` with an in-code default, and compute it at most once per production turn when it reads many planets.
- A choice is only offered with a positive, finite weight (`PlayerAI.OfferedChoices` drops the rest), so a weight of 0
  removes a choice and an all-zero row never reaches the matrix.

**Planet resource production** (how much food, grotsits, industry and research a planet makes): assess in this order.

1. **First,** changes to `Planet.AssignWorkForStrategy` and how it uses `Planet.CurrentStrategy`: the worker allocation
   order, the priorities and modifiers, and the worker requirements that turn a planet's strategy into workers on each
   resource.
2. **Second,** changing the value of `Planet.CurrentStrategy`: a planet type's `_initialStrategy` in its resource-data
   asset, or switching a planet's strategy at runtime.

Only after both, look at other levers (shipping, constants, catalogs). Read the "Planet strategy" section of `CLAUDE.md`
first: a planet's `PlanetStrategy` is not the player's `AIStrategy`, and the AI does not change a planet's strategy today.

## 4. Tuning log output in implementation plans

Every implementation plan must include a section of proposed additions to the tuning log output (`AITuningLogger`; see
"AI Tuning Log" in `CLAUDE.md`), so a Play-mode run can show whether the feature works and how to tune it.

- For each proposed line give the code and fields in the existing `T<turn>|P<playerId>|<EventCode>|<fields...>` format,
  when it is logged (on a state change only, or every turn, and why), and which tuning question it answers.
- Say how each line is kept from repeating every turn (a tracker class such as `GrotsitsShortTracker`, or log-only state
  in `PlayerAI`), and which task adds it and which test, if any, covers it.
- Include the matching update to the `tuning-log` skill (`.claude/skills/tuning-log/SKILL.md`) and to the "AI Tuning Log"
  section of `CLAUDE.md`.
- If the feature needs no new log output, the section says so and gives the reason; it is never left out.
