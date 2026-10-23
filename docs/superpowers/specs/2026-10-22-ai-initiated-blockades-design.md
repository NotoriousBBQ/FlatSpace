# AI-initiated blockades (and producer value) - design

Date 2026-10-22. Session "choke_point_changes" (continued). Works from `FUTURE_FEATURES.md`: "AI initiates blockades" (milestone ship_combat, element "the AI moving warships in order to
blockade") and the Blockade Targets list (DCs controlled by another player, planets through which other players' shipments pass, uncolonized high value planets), and builds the agreed but
deferred **producer value by planet type**. Follows the chokepoint weighting feature (`2026-10-22-chokepoint-weighting-design.md`), whose connectivity signals it reuses.

## Intent

Today a blockade only happens as a side effect: warships docked on a planet for another reason (garrison, an assault target) impose one. The owner wants the AI to **start blockades on purpose**,
aggressively, to starve rivals of resources, and to deny valuable colonization targets to rivals until the AI can colonize them itself. In priority order:

1. **Enemy Distribution Centers** (a blockade on a DC cuts the shipments that keep its stockpile up and the shipments it sends).
2. **Known planets that carry heavy enemy resource shipments** (food and grotsits), to cut those lanes.
3. **Uncolonized high value planets**, held until a colony ship of mine can get there (protect, not starve).

## Goals

1. A new "initiate" target pass picks up to `blockadeInitiateMaxActive` planets of the three kinds, in the order above (all of kind 1, then 2, then 3; ranked within a kind), and sends spare warships to hold them.
2. The wanted fleet rises per active initiated blockade, so spare ships exist (the owner: "raise the cap per active blockade").
3. Producer value by planet type exists as authored data and ranks priority 3 (and breaks ties in 1 and 2).
4. Every start, end and skip is logged with its reason; the side effects (below) are logged and minimized.

## Non-goals

- Changing assault targeting, `ChooseBlockadeTarget` (breaking blockades against me), garrison rules, retreat, combat or conversion.
- Blockading planets of players not at war, except rivals at peace above the hostility threshold (below).
- New ship kinds, new blockade mechanics (a blockade is still docked warship offense, `BlockadeSystem`), or any change to who a blockade affects.
- Using producer value for garrison priority, colonization choice, DC selection or the shipment threshold (still deferred).
- Human control, fog-of-war rendering, saved state for the new trackers.

## Anti-goals (a failure even if it technically works)

- Starving the existing behavior: initiated blockades take ships only after garrison, blockade-breaking and assault have claimed theirs.
- Fleets parked on blockades while the AI loses planets for lack of defense, or flapping on and off a target every few turns.
- Starting wars the owner did not intend: a peace-time blockade is gated by a tunable hostility value.
- A hold that outlives its reason (a colonized target, a lost DC, an ended war).

## Design

### 1. Who can be targeted
A rival `r` is a valid blockade-initiation target for player `p` when `p` is at war with `r`, **or** `p` is at peace with `r` and their hostility toward `r` is at or above the new tunable
`blockadeInitiateHostility`. A value above 100 disables peace-time blockades. Only Consolidate-like strategies (`IsConsolidateLike`) initiate; Expand and None never do. The hostility value comes from the
existing `DiplomacyState`. Note (existing behavior, kept): a blockade cut feeds the victim's hostility toward the blocker, so a peace-time blockade escalates toward war by design.

### 2. Target kinds and scores
- **DC** (kind 1): a planet that is currently a Food or Grotsits Distribution Center of a valid rival and that I know. Score: the rival DC's chokepoint percentile + producer value of the planet (ties by cost).
- **Lane** (kind 2): a known planet, held by or passed through by a valid rival's shipments, where the count of that rival's **in-flight** Food and Grotsits transport orders whose stored path includes the planet (as an endpoint or an intermediate node) is
  at least `blockadeLaneStartCount`; it is released when the count falls below the lower `blockadeLaneEndCount` (hysteresis). Score: that count, then chokepoint percentile. *Designer's call:* in-flight orders are the window
  (an order is in flight for the path's travel time), so no separate N-turn tracker is added; if the logs show flapping, add one.
- **Colony** (kind 3): an uncolonized (no population, no owner) planet that is a valid colonization target for me (`PlayerAI.IsValidColonizationTarget`) but for which I have no colony ship ready or in flight, and whose value is at least `blockadeColonyMinValue`.
  Value = chokepoint percentile x `blockadeColonyChokepointWeight` + producer value x `blockadeColonyProducerWeight` (both default 1; tuned from logs). Held until it is colonized (by anyone) or stops being a valid target.

"Known" means the planet is in my `Knowledge`. Reading a rival's DC role from its `PlayerAI` is omniscient about the role, gated by me knowing the planet (designer's call).

### 3. Producer value (new data)
`PlanetResourceData` gets an authored `_producerValue` (float), exposed by `GameAIMap.ProducerValue(planetName)` (by the planet's type, 0 for unknown): Verdant 1.0, Desolate 1.0, Farm 0.5, Normal/Prime/Desert/Industrial/Ocean 0 (Desert, Industrial and Ocean are excluded
because their output is local-only today; the 2026-10-02 agreement). Edit the per-type resource-data assets, not code, to retune. It is read by this feature only.

### 4. Choosing and sending
The initiate pass runs in `PlayerAI.PlanShipActions` **after** the garrison plan, the blockade-breaking/assault plan and the siegeship part, and uses only the spare left per origin (the same `PlanetState.Spare` minus what those plans send). It builds the
candidate list from the three kinds, ranks by kind then score, keeps targets already held (sticky, like `BlockadeTargetTracker`) before taking new ones, caps the total at `blockadeInitiateMaxActive`, and for each target sends spare ships cheapest path first until the target's
needed offense is covered, the same way `AssaultPlanner.PlanBlockadeForce` sizes a force against a blockade (reuse it or its sizing; the target is not blockaded against me, so the needed offense is the largest other player's docked offense there plus the margin, at least one ship).
A target no spare ship can reach, or whose force cannot be covered, is skipped and logged with the reason; it is retried next turn.

### 5. Release
A held target is released when: its reason ends (kind 1: no longer a rival DC, lost, or the war/hostility gate fails; kind 2: below `blockadeLaneEndCount` for `blockadeLaneEndTurns` consecutive turns; kind 3: colonized by anyone or no longer a valid target), the war ends and the hostility gate no longer holds,
or garrison, blockade-breaking or assault needs the ships (they have priority, so the ships simply stop being spare). Released ships return to the ordinary planner; they are not recalled by a special order.

### 6. Fleet cap per active blockade
`PlayerAI.WantedWarships` gains the ships needed to hold each active initiated blockade (needed offense converted to ships at the player's current offense per ship, at least 1 each), summed over the active set and
added after the existing economy-ceiling minimum and bounded per blockade by `blockadeInitiateMaxShips` (see the designer's calls). The existing `warshipFleetCap` taper still applies on top, so the fleet-cap invariant
(`WarshipBoost` multiplier 0 means no warship starts, except the existing blockaded-planet exemption) is unchanged. "Active" means held this turn or already sent.

### 7. Side effects and how they are watched
| Side effect | Why it can happen | Log / measure |
|---|---|---|
| Defense neglected | ships go to blockades | initiated ships only come from spare; `ChokepointGarrison` share and `PlanetDead`/`OwnerChanged` per run, against the 10-22 batches |
| Flapping on and off a target | thresholds near the line | hysteresis on lanes; `BlockadeInitiateEnd` turnsHeld distribution; repeat Start/End on one planet |
| Unintended wars | peace-time blockade feeds hostility | `Stance ... War` turns after a peace-time `BlockadeInitiateStart` (the Start line carries the stance) |
| Repeated fights over the same planet | blockaders meet defenders | `Combat` lines at initiated targets |
| Wasted holds | target never pays off | rival `OrderBlocked` / `ShipmentCancelled` at the planet while held (lane/DC); `ColonizeArrive` on a held colony target by me or by a rival |
| Fleet growth with no use | cap raised per blockade | `WarshipBoost` wanted/have, ships held per blockade |

## Constants (all on `GameAIConstants`, in-code defaults, 0 or below = off where noted)

`blockadeInitiateMaxActive` (2; 0 = off), `blockadeInitiateHostility` (starting point 60; above 100 = no peace-time blockades), `blockadeLaneStartCount`, `blockadeLaneEndCount`, `blockadeLaneEndTurns`,
`blockadeColonyMinValue`, `blockadeColonyChokepointWeight` (1), `blockadeColonyProducerWeight` (1), `blockadeInitiateGiveUpTurns`, `blockadeInitiateMaxShips` (4). Defaults are starting points to be tuned from Play logs (builder proposes concrete numbers from the logs' typical lane counts).

## Tuning log output

| Line | When | Fields | Question | Repeat guard |
|---|---|---|---|---|
| `T<t>\|P<id>\|BlockadeInitiateStart\|planet\|kind(Dc, Lane or Colony)\|rival\|stance(War or Peace)\|score\|neededOffense\|ships` | a target becomes active | | what is held and why, was it a peace-time blockade? | new tracker `InitiatedBlockadeTracker` (state change only) |
| `...\|BlockadeInitiateEnd\|planet\|kind\|reason(ReasonEnded, Colonized, LostDc, WarEnded, NeededElsewhere, Unreachable)\|turnsHeld` | a target stops being active | | how long do holds last, why do they end? | same tracker |
| `...\|BlockadeInitiateSkipped\|planet\|kind\|reason(NoSpare, NoPath, OverCap)` | on change only | | are targets starved of ships? | tracker |
| `WarshipBoost` unchanged | existing line | wanted now includes the blockade ships | did the cap rise? | existing |

Plus the `tuning-log` skill and `CLAUDE.md` / `Assets/Flatspace/GameAI/CLAUDE.md` updates (new lines, the producer value, the wanted-fleet term, the hold/release rules).

## Tests

A new `Assets/Editor/BlockadeInitiateSelfCheck.cs` (`FlatSpace -> AI -> Run Blockade Initiation Self-Check`, `public static bool RunChecks()`, added to `AllAISelfChecks`; no `Gameboard.Instance`): valid-rival gate (war; peace below and above the hostility value; Expand never);
each kind's eligibility; kind order and sticky holds; `blockadeInitiateMaxActive`; only spare ships are used and garrison/assault claims are untouched; lane hysteresis; release reasons; producer value lookup and the default values; the wanted-fleet term and the
economy ceiling raise; log formatters. Fixtures that need the old behavior set `blockadeInitiateMaxActive = 0`.

## Success measure

Play runs on `4p.json` and `test2.json` (6 per board, read with `/tuning-log`) against the 2026-10-22 chokepoint batches: initiated blockades start and end for sensible reasons; rival `OrderBlocked` and `ShipmentCancelled` at held planets rise; the rival's shortage counts rise; defense and conquest figures stay within noise; no fleet-cap violations; peace-time blockades do not cause wars faster than the hostility value implies.

## Calls made by the designer (from the builder check; the owner can override)

- **Blockades ignore stance (verified):** `BlockadeSystem.Value` counts the largest docked warship offense of any other player, with no war test, so a peace-time blockade works mechanically; combat still needs war, so my ships at peace
  sit on a planet without fighting. A peace-time blockade is an act of aggression only through the hostility its cuts add (existing behavior).
- **Lane paths are recomputed, not stored:** orders carry no path. The tally derives each in-flight Food/Grotsits transport order's nodes from `GameAIMap.GetPath(order.Origin, order.Target).PathNodes` (the all-pairs paths ships fly; a detour order's
  extra waypoints are included if the order carries them, as `GameAIMap.OrderPathPoints` does). No save-format change.
- **What a hold means:** a hold is a standing commitment while its reason is true. The needed offense is re-sized every turn: the **targeted rival's own docked offense at the planet** x (1 + `blockadeBreakMargin`), minus my docked and incoming offense there, at least one ship
  (a blockade value is my offense minus the rival's own docked offense, so that is what must be exceeded; a planet with no rival ships needs one ship). Bunkers do not enter `BlockadeSystem`, only docked ships do. A target below its needed force still counts against
  `blockadeInitiateMaxActive` and keeps being fed with whatever spare exists each turn; it is released after `blockadeInitiateGiveUpTurns` consecutive turns on which the arrival gate (next item) says Outmatched.
- **Safety gate:** before sending ships to a target the initiate pass uses the existing arrival projection (`PlayerAI.ArrivalNotOutmatched`, as the siegeship follower gate does) so ships are not fed into a fight they lose; a target that fails it is skipped as `Outmatched`.
- **Partial forces:** ships are sent with whatever spare exists even if the needed force is not covered (as `PlanBlockadeForce` already does), subject to the safety gate on the total arriving force.
- **Skip reasons (on change only):** `NoSpare` (no spare ship can reach), `NoPath`, `Outmatched` (safety gate), `OverCap` (`blockadeInitiateMaxActive` reached). `Unreachable` as an End reason means `NoPath` for `blockadeInitiateGiveUpTurns` turns.
- **Fleet term, no feedback loop:** the wanted-fleet addition is computed from the active set **at the end of the previous turn** (kept by `InitiatedBlockadeTracker`, not saved), and each active blockade adds at most `blockadeInitiateMaxShips` (new tunable, starting point 4) ships, so the total
  addition is bounded by `blockadeInitiateMaxActive x blockadeInitiateMaxShips`. It is added **after** the existing economy-ceiling minimum (the ceiling is not raised), and the `warshipFleetCap` taper applies to the sum, so the fleet-cap invariant is unchanged.
- **Colony kind needs a valid rival:** a Colony target is only held while at least one valid rival (section 1) exists. A blockade on an empty planet cuts any other player's colonist order that ends at or passes through it (the existing colonist blockade rule, the target node included), so
  it denies the target to rivals as intended. Side effect, accepted: it also cuts colonists of rivals at peace and feeds their hostility; the `Stance` line after a `BlockadeInitiateStart` shows whether it started a war.
- **API reuse:** chokepoint percentile is `GameAIMap.Chokepoint(name)` / `IsChokepoint(name)` (board-wide, not per player); "known" is `Knowledge.IsKnown(player, name)` / `KnownPlanets(player)`; the valid-rival test reads `AIMap.Diplomacy` (stance and hostility) through the same war-rival filter the assault uses (`PlayerAI.AssaultWarFilter()` / `WarRivals()`), extended by the hostility value.

Owner decisions after the builder check (2026-10-22): partial forces are sent, behind the arrival-projection gate; colony blockades may also cut colonists of rivals at peace (accepted, watched through the `Stance` line, tightened only if the logs show wars starting from it); the wanted-fleet addition is bounded per blockade by `blockadeInitiateMaxShips` as written above.

## Left to the builder

Exact default numbers (from log lane counts), how to reuse `PlanBlockadeForce` versus a thin wrapper, where the in-flight lane tally is computed once per turn, the tracker's data layout, and the ship conversion of needed offense for the wanted-fleet term.
