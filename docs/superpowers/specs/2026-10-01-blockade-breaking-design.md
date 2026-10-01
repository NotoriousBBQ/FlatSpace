# Blockade breaking: the assault clears blockades (sub-project 3)

Date: 2026-10-01. Roadmap entry: `FUTURE_FEATURES.md`, "AI avoids blockaded routes and uses blockades", item (3). Builds on
sub-projects 1 and 2 (`2026-09-29-blockade-avoidance-colonization-design.md`,
`2026-09-30-blockade-avoidance-shipping-design.md`) and on the Consolidate assault
(`AssaultPlanner`, `PlayerAI.PlanShipActions`).

## Goal

Under Consolidate, the AI sends enough warship offense to a planet that is visibly blockaded against it that the blockade
value there falls to 0 or less. It also researches warship offense faster while blockaded. Today the assault only targets
enemy-occupied planets, sizes its force by ship count, and ignores blockades.

A blockade is a standoff, not combat (there is no ship-to-ship combat yet). `BlockadeSystem.Value(planet, owner)` is the
largest single other player's docked warship offense minus the owner's own docked offense there, counted only when
strictly positive. "Breaking" a blockade therefore means docking offense there until my docked offense is at least the
blocker's.

## Decisions agreed in brainstorming

1. **Candidates: any planet blockaded against me in my `BlockadeView`**, whether enemy-occupied, mine or empty. When there
   is none, the existing enemy-occupied target selection runs unchanged.
2. **One target.** Blockaded planets rank ahead of the enemy-occupied fallback inside the existing single, sticky assault
   target. No second planner, no second fleet.
3. **Force sized by exact offense**, including offense already in flight, plus a margin.
4. **Research:** a situational multiplier on the Warship Offense research line only, while any blockade against me is visible.
5. **Ranking** has no connectivity term yet (see "Follow-up for sub-project 4").
6. **Consolidate only.** A visible blockade means a known planet holds another player's docked ship, which is exactly the
   first-contact test (`PlayerKnowledge.HasContact`), so the player is already in Consolidate. A remembered blockade on an
   unseen planet also needs prior contact.

## Design

### 1. Target selection (`AssaultPlanner`, `PlayerAI.PlanShipActions`)

`AssaultPlanner` stays the one planner. `PlayerAI.PlanShipActions` passes it the current `BlockadeView`
(`PlayerAI._blockadeView`) and a `WarshipStats`.

`ChooseTarget` first considers blockade candidates. A planet is a candidate when:

- `view.IsBlockaded(name)`, and
- it is reachable: a usable path (`IsUsablePath`, i.e. `2 <= NumNodes <= maxPathNodesForShipTransport`) from a planet
  holding my warships, or I already have ships committed there (docked or incoming), as the current rule allows.

Remembered, unseen planets are included because `BlockadeView.Build` already adds them (value as remembered, blocker
`Planet.NoOwner`). A candidate may be enemy-occupied, held by me, or empty.

Ranking, first difference wins:

1. Most of my offense committed at the planet (docked + incoming), descending. This keeps the target sticky.
2. Cut one of my orders within `blockadeTargetRecentTurns` turns (new tunable, default 5), from the player's
   `BlockadeMemory` entry (planet, value, turn). Cut recently ranks first.
3. Smallest offense still needed (section 2), ascending: the cheapest blockade to clear.
4. Cheapest path from a planet holding my warships, ascending.
5. Planet name, ordinal.

When there are no candidates, `ChooseTarget` falls back to the existing enemy-occupied rule (most ships committed, then
cheapest path, then name), unchanged. A blockade target uses the new offense-based sizing; a fallback target keeps the
ship-count `RequiredForce`/`Deficit`.

**Home defence.** The chosen planet is passed as `ShipTransportPlanner.HeldPlanet` exactly as the assault target is today,
so ships docked there are not sent home. If the target is one of my own colonies it is also a garrison target; the assault
plan keeps running after the home plan and uses only the ships left spare (`homeActions` already subtracted), as now. Ships
the home plan sends to the target this turn are not yet in the incoming counters, so the force can overshoot by that
amount; accepted.

**Exit.** A target stops being a candidate when its value drops to 0 or less (it leaves the view). The next `ChooseTarget`
then picks the next candidate or the fallback.

### 2. Force sizing by offense

For a blockade target:

```
needed = value x (1 + blockadeBreakMargin) - incomingOffense
```

`value` is `view.Value(planet)` (the blocker's docked offense minus my docked offense there; the remembered value for an
unseen planet). `blockadeBreakMargin` is a new tunable, default 0.1. The value counts only when strictly positive, so any
small overshoot clears it; the margin guards float edges and a blocker that adds a little. The target is covered when
`needed <= 0`.

**Incoming offense.** `Planet.GetIncomingOffense(kind, owner)` is new, a float parallel to the incoming ship counter
(`GetIncomingShips`, maintained in `GameAI.cs` at ship transfer in progress and at arrival, and rebuilt by
`GameAIMap.RecomputeIncomingShips` on load). It is maintained at the same three places. The delta is the sum of
`WarshipStats.Offense(template, snapshot)` over the order's `Fleet` snapshots. The arrival order carries the same
snapshots as the departure, so add and remove match. It is derived state (never saved) and clamped at 0 like the counter.
`WarshipStats` is built from the research items (`BlockadeSystem.ResearchItemsFrom`), as `BlockadeSystem` is.

**Planning.** `AssaultPlanner.Plan` already walks sources cheapest path first. For a blockade target, each source peeks the
ships that would actually leave (`Planet.PeekShipSnapshots(kind, owner, count, skip)`, skipping the ships the home plan
already claimed from that origin), adds up their real offense, and sends only as many as the remaining `needed` requires,
stopping when it is met. If every source together cannot cover it, all spare ships are sent (today's trickle): docked
offense still reduces the blockade value, so a partial force lowers the loss until more arrives, and rank 1 (committed
offense) keeps the target stable meanwhile. `ShipAction` is unchanged (origin, target, cost, count, kind).

`AssaultPlanner` gains a `WarshipStats` dependency and still does not touch `Gameboard.Instance`, so the self-check can
drive it directly.

### 3. Research

`PlayerAI.GetResearchSituationalMultiplier(item)` returns `blockadedOffenseResearchBoost` (new tunable, default 2) for
research items whose `effect` is `Warship Offense` while the `BlockadeView` holds any blockade against me, otherwise 1.
`BuildChoiceMatrix` applies it after `NormalizeResearchWeightsBySubtype`, so Armor and Shields do not dilute the boost.
Only Offense is boosted because blockade value is offense against offense; Health and Defense do nothing until ship
combat exists. The static `ResearchWeightTable` is unchanged. The weight stays a situational factor, not a table value, as
the session configuration rules require. The multiplier is computed once per research choice.

### 4. Tuning log

Next to the `AssaultTarget` call site (`AITuningLogger`, `T<turn>|P<player>|<Code>|...`):

- `BlockadeTarget|<planet>|<blocker>|<value>|<neededOffense>|<Committed, RecentCut or Cheapest>`: logged when the blockade
  target changes. The last field is the rank step that decided it. `<blocker>` is `-1` for a remembered, unseen planet.
- `BlockadeForce|<planet>|<ships>|<offense>|<stillNeeded>`: logged each turn ships are sent at a blockade target, so each
  wave's coverage is visible and `blockadeBreakMargin` can be tuned.
- `BlockadeTargetEnd|<planet>|<Cleared, Switched or Unreachable>|<turnsHeld>`: logged when a target stops being the target.
  `Cleared` = the planet is no longer blockaded in the view; otherwise `Switched` if another target was chosen; otherwise
  `Unreachable`. `turnsHeld` = turns since its `BlockadeTarget` line.
- `OffenseResearchBoost|<item>|<multiplier>`: logged when a research start picks a Warship Offense item while the boost is
  active.

Target start and end go through a small pure class, `BlockadeTargetTracker` (`Assets/Flatspace/GameAI/`, namespace
`FlatSpace.AI`), the same pattern as `GrotsitsShortTracker`; `PlayerAI` keeps only log-only state, so a load logs the
current target once more. The tracker takes the turn and the current target (planet, blocker, value, needed, reason) and
returns start and end events. It is the one logging piece with a self-check; `AITuningLogger` itself has none by design.

The `tuning-log` skill (`.claude/skills/tuning-log/SKILL.md`) gets matching analyses: turns to clear per blockade
(`BlockadeTarget` to `Cleared`), how often `RecentCut` decides a target, whether `Blockade`/`OrderBlocked` events at a planet
stop after a `Cleared`, wave coverage from `BlockadeForce`, fleet-cap discipline while a force is committed, and a check
that the research boost lines match blockade presence.

### 5. Saves

No new saved state. Incoming offense is derived and recomputed on load. The sticky rank comes from docked and incoming
ships. Recent cuts already persist in `GameSave.PlayerSave.rememberedBlockades`.

### 6. Tunables (`GameAIConstants`, all with in-code defaults, so the asset needs no edit)

| Tunable | Default | Meaning |
|---|---|---|
| `blockadeTargetRecentTurns` | 5 | a planet that cut one of my orders within this many turns ranks first among blockade candidates |
| `blockadeBreakMargin` | 0.1 | send `value x (1 + margin)` offense so any small overshoot clears the blockade |
| `blockadedOffenseResearchBoost` | 2 | weight multiplier on Warship Offense research while a blockade against me is visible (1 disables) |

## Out of scope

- Connectivity weighting (see below), and moving warships to impose a blockade (Blockade Targets in `FUTURE_FEATURES.md`).
- Planetary invasion and ship-to-ship combat.
- Making blockade breaking apply under Expand (not reachable, see decision 6).
- In-flight colonist avoidance (sub-project 5).

## Follow-up for sub-project 4

The ranking omits connectivity on purpose: no C# shortest-path betweenness exists (only `tools/planet-centrality.ps1`), and
sub-project 4 needs the same computation for garrison and colonization targeting. When sub-project 4 builds it once (from
`GameAIMap`'s all-pairs paths), it must also add it to this ranking as a step after the recent-cut step (chokepoint value and
specialised-producer value; see "Weight planet value by connectivity"). Recorded in `FUTURE_FEATURES.md` and in the
`blockade-target-ranking-add-connectivity-in-subproject-4` memory.

## Testing: `Assets/Editor/BlockadeBreakSelfCheck.cs`

A new self-check (menu item `FlatSpace -> AI -> Run Blockade Breaking Self-Check`, `public static bool RunChecks()`, added to
`AllAISelfChecks`). It builds a minimal `GameAIMap`/`Planet`/`PlayerAI` set directly (never `Gameboard.Instance`) with distinct
planet positions (see the A* tie-break note in `CLAUDE.md`) and covers:

- candidates: blockaded enemy-occupied, own and empty planets all count; unreachable ones do not; the enemy-occupied
  fallback runs when none is blockaded;
- ranking: committed offense, a recent cut inside and outside N turns, smallest offense needed, path cost, name;
- the need: incoming offense and margin, `needed <= 0` when covered;
- planning: stops at the need, partial force when spare ships fall short, ships claimed by home actions skipped, per-ship
  offense differences from research snapshots;
- incoming offense: add, arrival and `RecomputeIncomingShips`;
- the research multiplier, applied after normalization, 1 when nothing is blockaded;
- `BlockadeTargetTracker` transitions: start, `Cleared`, `Switched`, `Unreachable`, no duplicate start lines.

Never put an assertion exactly on the margin boundary (float caveat in `CLAUDE.md`). Play-mode check afterwards on `test2.json`
and `4p.json`: read `BlockadeTarget`/`BlockadeForce`/`BlockadeTargetEnd` and the existing blockade lines with `/tuning-log`.

## Documentation

`CLAUDE.md` (Warships and Blockade: blockade breaking, the new tunables, the new log lines, the incoming offense counter,
the new self-check in the Tests list and in `AllAISelfChecks`), `FUTURE_FEATURES.md` ((3) done and the connectivity step
added to (4)), `completed_features.md` through `/update_feature_list`, and the `tuning-log` skill.
