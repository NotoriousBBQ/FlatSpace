# Distribution Centers (DCs) — Design

Status: approved in chat, writing up for the record before the implementation plan.

## Motivation

Resource shipments (`PlayerAI.ProcessResourceShipments`, see the resource-shipment fix earlier this
session) are gated to `GameAIConstants.maxPathNodesForResourceDistribution` (default 3) hops between
origin and target. A topology probe against `test2.json`, `4p.json` and `2Player10Planet.json` showed
that on boards above a modest size, the number of planet pairs sitting *just* outside that range but
within double the range (a single relay hop away) meaningfully exceeds the number of pairs already in
range — 834 vs. 346 directed pairs on `test2.json`, 3708 vs. 1132 on the 100-planet `4p.json`. Only the
10-planet `2Player10Planet.json` inverted this (34 vs. 56), because a small board's diameter already
fits inside the direct-shipment range.

A **Distribution Center (DC)** is a colonized planet a player's AI designates, per resource, to sit in
that gap: it accepts shipments as a forward stockpile ahead of real need, then re-ships smaller amounts
onward to real shortages within its own reach — extending effective delivery range to roughly
`2 x maxPathNodesForResourceDistribution` without changing the underlying per-shipment range check at
all.

Terminology: "DC" is shorthand for Distribution Center throughout this doc and future discussion of this
feature.

## Scope

Two resources: Food and Grotsits, independently. A planet can be a DC for one, the other, or both at
once (there's no reason a single well-placed hub can't serve both roles).

**Board-size gating**, per two tunables on `GameAIConstants`:

- `minPlanetsForDistributionCenters` — at or above this many colonized planets, a player's AI may
  designate up to **one** DC per resource (Food and/or Grotsits).
- `minPlanetsForSecondDistributionCenter` (> the first) — at or above this many, up to **two** DCs per
  resource. Left very high initially (effectively disabled) so early testing only exercises the
  one-DC-per-resource tier.

Both thresholds count colonized planets on the whole board (matching the topology probe, which was
board-size, not per-player-territory, based), not a single player's colonized count.

## Data model & persistence

DC designation lives on `PlayerAI` as sticky state, not on `Planet` — it's a per-player role assignment,
the same category as `PlayerKnowledge`'s known-planets set or `AssaultPlanner`'s sticky target. Two
fields per resource (planet name or empty): `_foodDistributionCenter`, `_grotsitsDistributionCenter` (a
list of up to 2 entries once the second tier is reachable, but effectively single-entry for now).

Persisted in `GameSave.PlayerSave` as two new string fields (or string lists once the second tier
matters), restored on load — the same pattern `knownPlanets` already uses. A missing/older field
deserializes to empty, i.e. "no DC yet, will be selected fresh."

## Selection: trigger, cadence, heuristic

**Trigger**, once per turn per player per resource: if no DC is currently designated for that resource
*and* the relevant planet-count threshold is met, run selection. If a designated DC is lost (captured or
its population dies out), its slot clears immediately and reselection runs the following turn. Otherwise
selection never re-runs — **sticky**, not recomputed every turn. This avoids the designation flapping
between near-tied candidates as single-turn surplus/shortage snapshots vary, since there's no historical
smoothing of that data today.

**Heuristic (coverage-maximizing)**, evaluated over the player's colonized-but-undesignated planets using
data the shipment system already computes every turn (`Planet.DistanceMapToPathingList`, that turn's
surplus/shortage `PlanetUpdateResult`s):

1. A candidate must itself be reachable (`NumNodes <= maxPathNodesForResourceDistribution`) from at least
   one of the player's *currently surplus-reporting* planets for that resource, or it could never receive
   stock in the first place.
2. Score = the count of the player's colonized planets that are within
   `maxPathNodesForResourceDistribution` of the candidate but **not** already within that range of any
   currently surplus-reporting planet — i.e. how many consumers the candidate would newly reach that
   producers can't reach directly today.
3. Tie-break toward the candidate that is *furthest* (in `NumNodes`) from whichever surplus planet
   supplies it, maximizing forward reach (the "near max shipping distance from producers" framing from
   the original ask).
4. Highest score wins; ties broken by path cost.

## Synthetic demand

Each turn, for each designated DC below its target stock and **with no real shortage already reported
for it that turn** (to avoid two decision rows for the same planet+resource — `BuildResourceMatrix`
already guards against a duplicate key and logs an error, so this must be prevented upstream, not caught
downstream), inject one synthetic entry shaped like a real `Planet.PlanetUpdateResult` shortage:
`Data = -(targetStock - currentStock)` when that gap is positive (matching the negative-Data convention
real shortages use), merged into the same list `ProcessResourceShipments` already consumes.

Its `ScoreMatrixDecisionElement.Priority` is a fixed sentinel (e.g. `float.MinValue / 2`, leaving headroom
so it can't itself underflow), not derived from the gap size — this must hold regardless of whatever sign
convention real shortages' `Data` turns out to use, so "below the floor" is a concrete constant, not a
relative comparison. (Aside, discovered while checking this and worth a line here since it directly
affects whether "real shortages always sort first" is actually true today: `Planet.cs` stores a real food
shortage's `Data` as the already-negative deficit, e.g. `-20`, but `ScoreMatrixDecisionElement.Priority`
descending-sorts on that raw signed value — so a `-20` shortage currently sorts *after* a milder `-5`
one, the opposite of "worse shortage served first." This predates the DC work and isn't touched here; the
DC sentinel is deliberately an absolute constant so it doesn't inherit that ambiguity, but the underlying
real-shortage priority ordering may itself want a look someday.) With the sentinel in place, real
shortages always claim available surplus before a DC's synthetic row does within a round — a DC only
ever pulls what real shortages didn't need. This reuses the rounds-based shipment loop and per-shipment
capping exactly as built; no new shipment code path.

**Target stock:** a flat tunable, one value each — `distributionCenterFoodTargetStock`,
`distributionCenterGrotsitsTargetStock` — matching the precedent set by `colonyFoodRider` and every other
"how much buffer should this carry" constant in `GameAIConstants`. Deferred idea, not built now: if a
flat number proves too crude once tested, explore sizing it off the DC planet's connection count instead
(a well-connected hub plausibly needs a bigger buffer than a lightly-connected one) — recorded here so it
isn't lost, not scheduled.

## Downstream redistribution

No new code. Once a DC's stock rises above its own consumption, `Planet.UpdatePlanet` reports it as an
ordinary surplus planet next turn, and the existing rounds-based shipment logic (`ProcessResourceShipments`)
routes it onward to real shortages within *its own* `maxPathNodesForResourceDistribution` reach — the same
machinery every other producer already uses. This is what turns one relay hop into an effective
`2 x maxPathNodesForResourceDistribution` reach, entirely for free.

## UI indicators

- **`PlanetUIObject`** (world-space marker): two `UnityEngine.UI.Outline` components added to the stats
  panel's `Image` (the same one `SetOwnerColor` fills) — one green (Food DC), one brown (Grotsits DC),
  each independently enabled and with a different `effectDistance` so a planet that's both shows a
  visible green-brown double ring rather than needing an invented third "mixed" color.
- **`PlanetDetailUIController`** (UI Toolkit detail panel): a new named `Label` in the underlying
  `.uxml`, queried via `Q<Label>("DistributionCenterStatus")` and set in `UpdatePlanetDetail()` to
  "Distribution Center: Food", "Distribution Center: Grotsits", "Distribution Center: Food, Grotsits", or
  hidden/empty when the planet holds no DC role.

## Diagnostics: is sticky selection actually sufficient?

Sticky, once-per-resource selection means the AI never re-evaluates whether its current DC placement
still gives good coverage as new territory is colonized — a new colony could land outside
`maxPathNodesForResourceDistribution` of *both* every currently-surplus planet *and* every current DC,
and nothing in the design above would notice or react (the second-DC tier exists for exactly this, but is
gated behind a threshold left deliberately unreachable for now). This needs to be observable, not
assumed, so three new `AITuningLogger` events are added:

- `DCSelected|<planetName>|<resource>` — logged the turn a DC is designated.
- `DCLost|<planetName>|<resource>` — logged the turn a designation clears (captured/dead).
- `DCCoverageGap|<planetName>|<resource>` — logged on `ColonizeArrive`, once per newly-colonized planet
  per resource, when that planet is unreachable (`NumNodes > maxPathNodesForResourceDistribution`) from
  **both** every currently-surplus-reporting planet **and** every currently-designated DC for that
  resource. A `/tuning-log`-style analysis of a real match can then answer directly: does this fire often
  enough on the boards being tested to justify lowering `minPlanetsForSecondDistributionCenter`, or does
  sticky single-DC selection hold up in practice?

## Future considerations (recorded, not built now)

- Tie `distributionCenterFoodTargetStock`/`GrotsitsTargetStock` to the DC planet's connection count if
  the flat tunable proves too crude once tested.
- DCs as strategic targets for enemy warship actions (a natural follow-on once DCs exist as a concept;
  `AssaultPlanner` would need a reason to weight a known DC planet higher than an arbitrary enemy planet).

## Testing plan (detail left to the implementation plan)

Extend the Editor self-check pattern (no `Gameboard.Instance` dependency): coverage-heuristic scoring on
a small synthetic map, sticky selection surviving a turn with no changes, reselection after a DC is lost,
synthetic-demand priority never outranking a real shortage in the same round, the real-shortage/synthetic
duplicate-row guard, and the three new log events firing at the right moments.
