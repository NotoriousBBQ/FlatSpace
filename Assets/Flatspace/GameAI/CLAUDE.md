# CLAUDE.md (AI subsystems)

Loaded when working under `Assets/Flatspace/GameAI/`. Moved out of the root `CLAUDE.md`; the root file still holds the architecture, turn loop,
orders, pathing, saves and conventions these sections build on. Section references like "see Diplomacy" point within this file.

### Player Knowledge

`PlayerKnowledge` (`Assets/Flatspace/GameAI/PlayerKnowledge.cs`, namespace `FlatSpace.AI`) is a
simulation-owned, per-player sticky "discovered planets" set. It gates the one AI decision that used
to reason about the whole map regardless of a player's actual presence: colonization targeting. A
planet becomes known to a player once it's a *vision source* for that player (population or a docked
ship) or a direct neighbour of one — both facts come from two shared queries on `GameAIMap`,
`GetVisionSourcePlanets(playerId)` and `GetNeighbours(planetName)` (the latter a symmetrized view of
`Planet.Connections`, built once in `GameAIMapInit`), also consumed by `FogOfWarSystem` (below) so
the two systems never independently re-derive the same fact. Knowledge is sticky (never un-learned)
and grows via a breadth-first search out to `maxPathNodesForKnowledge` hops (default 2, i.e. direct
neighbours only — widen it to give newly-colonized frontier planets a head start on Distribution Center
coverage, below) from every vision source, each time `Update` runs. Each source's own walk tracks a
`visited` set kept separate from the player's permanent known set, so it can pass *through* a planet
already known (from an earlier turn, or from a different, closer source) to reach genuinely new
territory beyond it — gating the walk on the permanent set alone made a source's BFS stop dead the
instant it touched known territory, silently undermining the whole point of a growing empire's known
frontier keeping pace with colonization (a real bug, fixed by the final whole-branch review for this
feature; see `RunKnowledgeBfsPassesThroughKnownTerritoryCheck` in `PlayerKnowledgeSelfCheck`).
`GameAIMap.Knowledge.Update(...)` runs once per turn in `GameAI.GameAIUpdate()`, between
`UpdateAllPlanets()` and `ProcessResults()`, so this turn's arrivals are known before this turn's AI
decisions run. The first consumer is
`PlayerAI.IsValidColonizationTarget` (`public` — see Conventions below — so both `ProcessColonizers`
and `PlanetCanColonize`/`GetIndustrySituationalWeightMultiplier` are gated by the one guard clause).
Persisted per player as `GameSave.PlayerSave.knownPlanets`. `Assets/Editor/PlayerKnowledgeSelfCheck.cs`
is this subsystem's self-check.

The second consumer is first-contact detection: `PlayerKnowledge.HasContact(map, playerId)` is true when
any known planet holds another player's population or docked ship (read directly from
`Planet.Population`/`DockedShips`, since `Planet.Owner` is `NoOwner` on a population tie).
`PlayerAI.TryEnterConsolidate` calls it first thing in `ProcessResults` and flips Expand → Consolidate
one-way; Amass/None are never switched by it (diplomacy switches Consolidate and Amass, see Diplomacy below).
Consolidate's and Amass's `ProcessResults` case runs the Expand routine (Amass has its own two tables, see Diplomacy); Consolidate's
research and industry entries are both their own hand-tuned tables:
research (Food 1.0, Industry 3.0, Grotsits 2.0, Research 1.0, ColonyShip 0.5, Warship 2.5, against Expand's
3.0, 2.0, 1.0, 1.0, 2.5, 1.5) and industry (Food 1.0, Industry 2.0, Grotsits 1.5, Research 1.0, ColonyShip 1.0,
Warship 2.5, against Expand's 2.5, 1.5, 1.0, 1.0, 2.5, 1.5); see the Consolidate bullet under Ship Transport for
the Warship production boost that multiplies the industry Warship value. A missing strategy key silently
falls back to a neutral weight, so a new
strategy must be given entries or it loses Expand's tuning. `PlayerKnowledgeSelfCheck` asserts every value
of both industry tables explicitly.

### Ship Transport

`ShipTransportPlanner` (`Assets/Flatspace/GameAI/ShipTransportPlanner.cs`, namespace `FlatSpace.AI`) moves
a player's spare warships between its own colonized planets to fill tunable garrisons; `PlayerAI.ProcessShipActions`
turns its `ShipAction`s (`ShipMatrix.cs`) into the order trio described under Orders. It must not touch
`Gameboard.Instance` so the self-check can drive it directly.

- **Categories** (per colonized planet, lower = better): 1 Desert/Industrial/Farm/Ocean, 2 outer (has a
  neighbour not colonized by this player: empty, enemy-held or contested), 3 Prime, 4 chokepoint (`GameAIMap.IsChokepoint`: shortest-path betweenness percentile >= `chokepointPercentile`, see Connectivity),
  5 Verdant/Desolate. A planet's priority is its best applicable category; its garrison is the *largest*
  garrison among its applicable categories.
- **Garrison rounds:** `round = 1 + min(floor((docked + incoming) / garrison))` over garrisoned, reachable
  planets; each planet's target that turn is `round x garrison`. Excess ships therefore start a second,
  third... fill; a new empty planet drops the round back to 1, so new planets fill first (this intentionally
  drains planets that were partway through a higher round). Planets above target are sources, below are targets.
- **Locked category 5:** planets whose only category is 5 are ignored entirely until total warships >=
  `category5UnlockShipsPerColonizedPlanet` x colonized planet count.
- **Matrix:** one row per source (largest spare first), choices are reachable targets sorted by category then
  path cost, `count = min(spare, deficit)`. `ShipChoiceElement` equality is on the *target planet only* so
  `ScoreMatrix` removes a claimed target from every other row. Row priorities are unique ranks as a defensive
  habit, not a requirement: `ScoreMatrixDecisionComparer`'s tie-break used to compare the Target-name diff
  against `Priority` itself instead of returning it directly, so two genuinely different rows tied at a
  Priority that happened to numerically match the small integer `string.CompareTo` returns (most commonly
  `Priority == 1`, from two distinct planets each short by exactly 1) were wrongly reported as equal — a real
  "Duplicate Key in Build Resource Matrix" crash, fixed 2026-09-28. Ties now correctly break by Target name.
- **Reachability** is `2 <= NumNodes <= maxPathNodesForShipTransport`: `PathingSystem.FindPath` does not throw
  when no route exists, it returns a 1-node zero-cost path, which must not be read as a free adjacent trip.
- **Incoming counter:** `Planet.GetIncomingShips(kind, owner)` is counted per owning player (fleets can target
  another player's planet under Consolidate, so a shared counter would let one player's fleet distort
  another's garrison and assault arithmetic). It is derived state, recomputed on load from in-flight orders
  (`GameAIMap.RecomputeIncomingShips`), not saved.
- **Saves:** the fleet rides on `OrderSave.fleetShips` (reusing `GameSave.ShipSave`); a missing/empty list
  (older saves) loads as no fleet.
- **Tunables** on `GameAIConstants` (`maxPathNodesForShipTransport`, `garrisonSpecialized/Outer/Prime/HighTraffic/HighlySpecialized`,
  `chokepointPercentile`, `category5UnlockShipsPerColonizedPlanet`) all have in-code defaults, so the
  asset needs no edit until you tune.
- **Consolidate:** `ShipTransportPlanner` takes an optional strategy (default Expand = the rules above,
  unchanged). Under Consolidate `MaintainsGarrison(planet)` is true only for outer planets and colonized
  chokepoints, so only they garrison, at round 1 only; every other ship is spare, including on category-5-only
  planets that would be locked under Expand. A non-outer chokepoint garrisons the *largest* garrison among its
  applicable categories like any planet (a Prime or Desert chokepoint holds 4, only a plain one holds
  `garrisonHighTraffic`, 2). Because `PlayerAI.WantedWarships` sums this planner's round
  garrisons, chokepoint garrisons also raise the wanted fleet (still bounded by `warshipsPerColonizedPlanet`).
  Change `MaintainsGarrison` to garrison more planets later. `TargetRank` sorts
  outer planets first (0 + category, others 100 + category; Expand: rank = category). `HeldPlanet` (the
  assault target) removes that planet's ships from the stranded set so the assault force is not sent home.
  `AssaultPlanner` (`Assets/Flatspace/GameAI/AssaultPlanner.cs`) then picks one known enemy-occupied planet
  (sticky: most of my ships docked/incoming there, else cheapest path), sizes the force to
  `max(assaultMinimumShips, ceil(known enemy docked warships x assaultRatio))` (unknown planets and
  ownerless ships are not counted), and sends the ships home defence left spare, cheapest path first.
  `PlayerAI.PlanShipActions` orchestrates both; ships trickle to the target rather than launching together
  (revisit when combat exists). `assaultRatio` and `assaultMinimumShips` have in-code defaults too.
  **Production:** under Consolidate the Warship industry weight is `table value x (1 + warshipShortfallBoost
  x shortfall / wanted)`. The multiplier is a *situational* weight, so it lives in
  `GetIndustrySituationalWeightMultiplier` (next to the ColonyShip one); `GetIndustryWeight` is just
  `GetIndustryStrategyWeight x GetIndustrySituationalWeightMultiplier`. It reads every planet and is logged, so
  it is computed at most once per production turn (cached in `_warshipMultiplierThisTurn`, reset by
  `BuildIndustryMatrix`) through `PlayerAI.ComputeWarshipMultiplier`.
  Wanted = round-1 garrisons of outer planets and colonized chokepoints + the assault's required force whenever a known enemy planet
  exists (`AssaultPlanner.HasKnownEnemyPlanet`, not `ChooseTarget`, which is null while I hold no warships);
  have = my docked warships + my own in-flight ships. Wanted is bounded by `warshipsPerColonizedPlanet` (default
  8) x my colonized planets: without that ceiling the assault force (`assaultRatio` x the enemies' known
  fleets) chases them while they chase mine, and in a 300-turn test `wanted` reached the hundreds and a
  thousand, so the fleet never reached the cap. The multiplier is 1 exactly at the wanted fleet, then
  tapers linearly to 0 at `wanted x warshipFleetCap` (default 1.5). `BuildIndustryMatrix` then drops every
  choice whose weight is not a positive finite number (`PlayerAI.OfferedChoices`, all strategies):
  `ScoreMatrix.WeightedPick` treats weights as relative and picks uniformly when every weight is 0, so a zero
  weight alone never removes a choice. Two long test runs showed it: once the one-time improvements ran out
  warships flooded the queue, and after the warship cutoff the same flood moved to colony ships (weight 0
  when the planet already holds one) with nothing left to colonize. A planet left with no choices starts
  nothing that turn and re-checks the next. The taper is only a *relative* weight: for a planet whose row
  has nothing else positive (all improvements built, colony ship already docked) any multiplier above 0
  still picks Warship with certainty, so for those planets it behaves as a step at `wanted x cap` rather than
  a ramp, and idle planets bank industry, so overshoot past the cap can approach the number of producing
  planets. Nothing wanted (`wanted <= 0`) means no taper, i.e. no cap at all. Ships still in production
  are not counted, so several planets finishing on the same turn can slightly over-build. A completed
  improvement never lowers a planet's yield for its resource (`Planet.ApplyImprovementYield` keeps the
  maximum), because every researched tier stays on offer. The
  `WarshipBoost|wanted|have|multiplier` tuning-log line shows the multiplier on each production turn in which
  a Warship choice is evaluated (i.e. once Warship research is done).
  **ColonyShip (also a situational weight):** 0 when a colony ship is already docked; 0 when no known, valid
  colonization target is within reach of the planet (`PlanetHasColonizationTarget`, independent of the planet's
  readiness), so planets stop building useless colony ships once the map is full (every strategy); x2
  (`ColonyShipUrgentBoost`) when the planet is ready to colonize; and, Consolidate only, x2 while targets remain even
  if the planet is not ready yet, because Consolidate's table weight for ColonyShip is 1.0 and a test run
  under-expanded (border planets left uncolonized, colony ships used the moment they were ready, so supply and not
  targets was the limit). Expand's 2.5 weight is not doubled.

`Assets/Editor/ShipTransportSelfCheck.cs` is this subsystem's self-check.

### Warships and Blockade

`WarshipStats`, `WarshipCosts` and `BlockadeSystem` (`Assets/Flatspace/GameAI/`, namespace `FlatSpace.AI`) are pure
(no `Gameboard.Instance`), so `Assets/Editor/WarshipSelfCheck.cs` drives them directly.

- **Stats:** `WarshipStats` derives a warship's Offense/Health/Defense from its `ShipData` template plus its
  `ResearchSnapshot`: base value + (tiers carried in that stat's research line) x `(Max - base) / tiers in the line`.
  A line is the "Ship Improvement"/"Warship" catalog items whose `effect` is `Warship Offense`, `Warship Health` or
  `Warship Defense`; a stat with no line stays at its base. The formula lives only in `WarshipStats`.
- **Cost:** a Warship costs `base x (1 + warshipImprovementCostFactor x improvements researched)` (`GameAIConstants`,
  in-code default 0.1). It is fixed when production is scheduled (the `IndustrySetProduction` case in `GameAI`) into
  `Planet.ProductionItem.FixedCost` (0 = use the catalog cost; `ProductionItem.Cost` picks one), saved as
  `ProductionSave.FixedCost`. Accepted quirk: if research completes mid-build, the ship is stamped with the newer
  improvement on completion but was paid at the older cost. `IndustryMatrix.Cost` still holds the catalog cost.
- **Update Warship:** a `"WarshipUpdate"` production item (subtype string is load-bearing). On completion
  (`Planet.ApplyWarshipUpdate`) the docked own warship missing the most researched improvements
  (`FindWarshipUpdateTarget`) gets every improvement it lacks. Cost is `base warship cost x factor x missing`
  (minimum 1), fixed at scheduling like the Warship cost, falling back to the catalog cost when no docked ship needs an update then. Its industry table weights equal Warship's, and its
  situational weight is 0 when nothing docked at the planet needs an upgrade.
- **Blockade:** at a planet the value against an order's owner is the largest single *other* player's docked warship
  offense minus the owner's own docked offense there; only a positive value counts, and only docked ships count.
  `GameAI.ProcessCurrentOrders` applies it once per turn (`ApplyBlockades`), after delays are decremented and before
  orders execute, to `OrderTypePopulationTransport`, `OrderTypeFoodTransport` and `OrderTypeGrotsitsTransport`.
  The route comes from `GameAIMap.GetPath`; each node's fraction is its cumulative edge cost / total cost, and an
  order passes a node in the turn its progress (`1 - TimingDelay / TotalDelay`) first reaches that fraction (the
  target on arrival). A colonist order's origin is never checked; a food/grotsits shipment's origin is checked
  once, on the first turn the order is processed (`TotalDelay > 0` and `TimingDelay + 1 >= TotalDelay`, so a
  zero-delay or mid-flight order is skipped). A blockaded colony order is removed together with its food rider
  (colonist and rider are lost) and the target's population-transfer flag cleared; a food/grotsits shipment loses the
  blockade value at each blockaded node and is removed (incoming flag cleared) once its amount is <= 0.
  Research: Warship has three parallel research lines, so the research roll divides each choice's weight by the
  number of eligible choices in its subtype (`PlayerAI.NormalizeResearchWeightsBySubtype`) to keep Warship's share
  of the wheel at its table weight.
  Limitation: `ProcessNewOrders` both queues and executes a zero-delay `Delayed` order (a pre-existing double
  execution), so its first delivery is never blockaded, but the stale queued copy (`TotalDelay` 0, so
  `PassedNodes` treats the whole route as passed) can be reduced or removed by a blockade on the next turn. The AI
  does not yet move warships to blockade (see `FUTURE_FEATURES.md`).
- **Blockade avoidance (colonization):** `BlockadeView` (`Assets/Flatspace/GameAI/BlockadeView.cs`) is a per-player
  snapshot rebuilt each `ProcessResults` (`PlayerAI.RefreshBlockadeView`); a planet is visible to the player when it is
  a presence planet (population or docked ship) or a direct neighbour of one, and blockaded when its blockade value
  against that player is > 0. A null view means nothing is blockaded. **Blockade memory:** a player also remembers
  planets where one of its OWN orders was cut (`BlockadeMemory`, `Assets/Flatspace/GameAI/BlockadeMemory.cs`: planet,
  value, turn). `BlockadeSystem.Apply` returns every cut it made (colony orders; each blockaded node a food/grotsits
  shipment passes) and `GameAI.ApplyBlockades` hands each to the ORDER OWNER's `PlayerAI.LearnBlockade`. The memory is
  active for `GameAIConstants.blockadeMemoryTurns` (default 20, raised from 10 after tuning logs showed a lasting blockade outliving the shorter memory; 0 disables) after the LAST cut (another cut refreshes
  it), so a hub two hops from anything the player holds stops eating colonists every few turns. `BlockadeView.Build`
  adds remembered planets that are not currently visible; a fresh direct sighting overrides memory (a visible planet
  that is not blockaded is forgotten). Only colonization routing uses it so far (through the view), and it is saved
  per player (`GameSave.PlayerSave.rememberedBlockades`; older saves load with none). `RoutePlanner.PlanRoute`
  (`Assets/Flatspace/GameAI/RoutePlanner.cs`) returns the ordinary shortest path unchanged when it is clean; when an
  in-range target's shortest path visibly crosses a blockaded planet it runs a fresh Dijkstra around them (no node
  limit, so a detour may exceed `maxPathNodesForColonization`; ties expand the smaller node name first, and
  `FindPath`'s fragile A* is left alone). It returns null when the target is blockaded, out of range, unreachable
  or every way round is blocked. Colonist orders carry their planned route (`GameAIOrder.Route`, saved as
  `OrderSave.route`; an older save with none falls back to the shortest path) and `BlockadeSystem.RouteFor` applies
  blockades along that route. `ProcessColonizers` launches nothing from a blockaded origin and drops targets with no
  route: the colony ship stays docked and retries next turn. `PlanetHasColonizationTarget` also goes through the
  planner; as a deliberate tightening the planner rejects `FindPath`'s 1-node no-route stub, which the old
  `NumNodes <= max` filter accepted. The ColonyShip weight is 0 on a blockaded planet; a blockaded planet's Warship
  weight is `max(multiplier, 1) x blockadedWarshipBoost` (`GameAIConstants`, default 3), i.e. exempt from the
  Consolidate fleet-cap cutoff, and Update Warship gets the same boost (its 0 when nothing needs updating stays 0). The `BlockadedProduction` tuning-log
  line marks each such start, so a `WarshipBoost` multiplier of 0 with a Warship start is the intended exemption only
  when that line exists.
- **Blockade avoidance (resource shipping):** `RoutePlanner.PlanShipmentRoute` plans every (source, shortage) pair in
  `PlayerAI.BuildResourceMatrix`. The range rule is colonization's (the target must be in range by its SHORTEST path,
  `2 <= NumNodes <= maxPathNodesForResourceDistribution`, which also rejects `FindPath`'s no-route stub) and only the
  route taken may exceed the limit. A clean route (the ordinary shortest path, else `PlanRoute`'s clean detour) is used
  when one exists; otherwise the least-loss route, then the cheapest, from the shared `RoutePlanner.Search` core (a
  Dijkstra minimising (total blockade value, cost), ties by node name; `ShortestPathAvoiding` is the same core with
  hard-skipped nodes and no loss). `PlannedRoute.Loss` is the total blockade value on the route and includes the
  ORIGIN's own value. A blockaded source or target is an ordinary node, not dropped outright: the pair is offered only
  while `Loss < min(remainingSurplus, remainingShortage)` (loss >= amount is cancelled, since nothing would arrive).
  `PlayerAI.CompareResourceChoices` sorts clean routes before lossy ones, then by cost minus surplus. The transport order
  carries `Route` (saved as `OrderSave.route`, applied by `BlockadeSystem.RouteFor`) and its delay uses the route's cost.
  The simulation agrees with the planner because `BlockadeSystem` now also cuts a shipment at its origin (see above), so
  economy numbers on boards with lasting blockades are not comparable with runs before this change. Logging:
  `RouteDetour` (shared with colonists), `ShipmentLossy|<origin>-><target>|<amount>|<loss>` when a shipment goes out
  through unavoidable blockades, and `ShipmentCancelled|<target>|<Blockade or LowYield>|<source>|<loss>|<amount>|<blocked planets>` when blockades removed
  every source for a shortage (`Blockade`: loss >= amount; `LowYield`: only the minimum delivered fraction refused it;
  `<source>`, `<loss>` and `<amount>` describe the dropped pair closest to shipping and `<blocked planets>` lists that route's
  blockaded planets as `name=value` joined by `,`, `-` when none, so the log says which planet starved the shortage, from
  `PlayerAI.ShipmentHold`; logged on state change only, through `PlayerAI.NoteShipmentHeldBack`, log-only state like
  `_colonizeHeldBack`). A shipment sends and deducts its full amount; the loss happens en route, so the shortage
  re-reports after a lossy shipment lands. **Minimum delivered fraction:** a lossy pair is also refused when it would deliver
  less than `GameAIConstants.shipmentMinDeliveredFraction` (in-code default 0.5, clamped to 0..1; 0 = the plain
  `loss < amount` rule, 1 = clean routes only) of what the origin pays, i.e. it needs `amount - loss >= fraction x amount`.
  Clean routes (loss 0) are never refused. Tuning logs on `test2.json` and `4p.json` showed lossy shipments delivering
  ~12% of their cost (e.g. 11 sent into a blockade of 10), which is why 0.5. The threshold is read through
  `PlayerAI.MinDeliveredFractionFor(targetPlanetName)`, today just the constant: the seam for a per-target threshold
  (see "Value-aware shipment delivery threshold" in `FUTURE_FEATURES.md`). Float caveat as elsewhere: never put a
  self-check exactly on the `fraction x amount` boundary. Assault routes are not blockade-aware yet.
- **Blockade breaking (assault):** under Consolidate the assault's single sticky target is a planet blockaded against the
  player in its `BlockadeView` first (enemy-occupied, own or empty; remembered, unseen planets count), else the old
  enemy-occupied rule (`AssaultPlanner.ChooseBlockadeTarget` / `ChooseEnemyTarget` / `ChooseTarget`; an `AssaultPlanner`
  built without a view behaves as before). Ranking, first difference wins: most of my offense committed there (docked +
  in flight), a cut of one of my orders within `blockadeTargetRecentTurns` (default 5, `BlockadeMemory`), the more
  central planet (a chokepoint, higher percentile first; every planet below `chokepointPercentile` ties at 0, so a value
  above 1 switches the step off; reason `Chokepoint`; see Connectivity), the smallest offense still needed, the cheapest
  path from a holder, name. The chokepoint step only matters when committed offense and a recent cut tie; moving it ahead
  of the recent-cut step is a recorded tuning option. A blockade target is
  sized by real offense, not ship count: `needed = value x (1 + blockadeBreakMargin) - incoming offense` (margin default
  0.1); `Plan` walks sources cheapest path first, takes the exact ships that would leave (after home defence's claims, via
  `PeekShipSnapshots`) and stops when covered, or sends every spare ship when the fleet falls short (docked offense still
  lowers the blockade value, and the sticky first rank keeps the target while the rest arrives). In-flight offense is
  `Planet.GetIncomingOffense(kind, owner)`, derived and recomputed once per turn from the in-flight ship orders
  (`GameAIMap.RecomputeIncomingOffense`, called in `GameAI.GameAIUpdate` before `ProcessResults`), never saved. Research:
  `PlayerAI.GetResearchSituationalMultiplier` multiplies `Warship Offense` items' weight by `blockadedOffenseResearchBoost`
  (default 2) while any blockade against the player is visible, after `NormalizeResearchWeightsBySubtype`. Consolidate-only
  costs nothing: a visible blockade means a known planet holds another player's ship, which is the first-contact test.
  Ships the home plan sends to the target in the same turn are not yet in the incoming counters, so the force can overshoot
  by that amount (accepted). **Holding a broken blockade:** the moment my docked offense brings a blockade's value to 0 the
  planet leaves the `BlockadeView`, so without a hold its ships would be spare that turn and the blockade would re-form.
  `AssaultPlanner.ContestedHolds` (planets where my warships and another player's are both docked; empty without stats)
  goes to `ShipTransportPlanner.HeldPlanets` each turn, stateless: those ships are never stranded sources, and a held
  colony is sink-only (`PlanetState.Held` makes `Spare` 0, it still receives ships up to its garrison). The same applies to
  `HeldPlanet` when it is a colony of mine. The hold ends when the rival's ships leave. A blockade the rival keeps
  reinforcing can pin the force there (the sticky first rank); the tuning log shows it.
- **Colonist redirect (colonists already in flight):** `ColonistRedirect` (`Assets/Flatspace/GameAI/ColonistRedirect.cs`, pure)
  re-plans a colonist whose REMAINING route now crosses a planet in its owner's `BlockadeView` (visible planets plus blockade
  memory; never the live values). `GameAI.RedirectColonists` runs it once per turn right after `ApplyBlockades` (so a colonist
  just cut is already gone), for every `OrderTypePopulationTransport` with `TimingDelay > 0`. The colonist counts as standing on
  the last route node it passed (`BlockadeSystem.CurrentNode`/`NodesAhead`); progress along the edge it was crossing is thrown
  away. Order of preference: (1) a clean `RoutePlanner.PlanRoute` from that node to the SAME target (a detour), unless a clean
  diversion also exists and the detour's REAL route cost is more than `GameAIConstants.colonistDetourDivertRatio` (default 2; 0 or
  below = always detour first) times the diversion's real route cost, in which case the diversion is taken and the declined detour's
  cost is logged (long detours stayed exposed to later blockades: on 4p.json 79% of 14 never arrived against 11% of 84 diversions;
  with no diversion candidate the detour is always taken; the comparison never uses the chokepoint-tilted cost);
  (2) a divert: the best other candidate by `route.Cost / ColonizationCostDivisor` (ties by name) from the wider set
  `PlayerAI.IsDiversionTarget` (known, and empty, or my colonist already inbound, or colonized below max population, my own
  planets included; `CanSupportColony` is not applied, the origin already paid); (3) neither: the colonist continues and is cut as
  before. Ordinary launches keep `IsValidColonizationTarget`. `ColonistRedirect.Apply` replaces the order's `Route`, restarts
  `TotalDelay`/`TimingDelay` at `max(1, route.Cost / defaultTravelSpeed)`, moves the food rider (matched on player, origin, old
  target and the colonist's own delay, so a twin colonist's rider is untouched) and, for a divert, moves the
  `PopulationTransferInProgress` flag (the old target's flag stays while another colonist of the player still heads there). The
  order keeps its real `Origin`. No claim tracking: several colonists may divert to one planet. **Arrival rule (every colonist):**
  `GameAI.ApplyColonistArrival` adds the colonist when `Population.Count < MaxPopulation`; at or above max on a planet the order's
  player OWNS it docks one colony ship for that player (`DockShipRebuiltSnapshot`, since a colonist order carries no research
  snapshot), eligible for the next colonization pass. A full planet owned by someone else (or tied) still takes the colonist as
  before, because colony-ship queries (`CheckColonizationReady`, `UndockShip`, `PlanetHasColonyShip`) never check a ship's owner,
  so a foreign ship docked there would be launched by that planet's owner or sit unused; a planet held by another player below
  max also still takes the colonist. A blockade cut on a colonist (`BlockadeSystem.ApplyToColony`) removes only that order and ITS
  rider (matched on player, origin, target and the colonist's delay), and clears the target's flag only when no other colonist of
  the player still heads there, because a divert can leave two colonists with the same origin and target.
  **Display and routes:** a diverted colonist may be sent back to its own origin (it is below max once the colonist left), so an
  order's `Origin` can equal its `Target`, and a planet's own name is never in its path table, so `GameAIMap.GetPath(order.Origin,
  order.Target)` throws `KeyNotFoundException` for it (this crashed `FogOfWarSystem.Recompute` on a 4p run). Anything that draws
  or samples the path an order flies (order lines in `GameBoard`, fog corridors) must use `GameAIMap.OrderPathPoints(order)`: the
  carried route (a redirected colonist's route starts at the node it redirected from), else the shortest Origin-to-Target path,
  else nothing. Never call `GetPath` for an order's own Origin and Target. Accepted limits: edge progress is lost
  on a redirect, a stale view can redirect needlessly, and a diverted colonist may land on a planet that filled in transit (it
  docks). No new tunable and no save change (route, target and delays already ride on `OrderSave`).

### Ship combat

Docked warships and siegeships of players at war that share a planet fight once a turn (bunkers fire only in the bombardment step, see Planet defense). Deterministic (no randomness), simultaneous, persistent damage.

- **State and stats:** `Ship.Damage` is damage taken (0 = full health; stored, never current health, so older saves load at full health). `WarshipStats.CurrentHealth` = Health stat minus damage (never below 0); `WarshipStats.EffectiveOffense` = Offense x current health / Health stat, **no floor** (the owner's choice; the rout is for retreat logic, elements 4 and 5), 0 for a Health stat of 0. Every reader of a ship's offense uses the effective value: `FleetStrength` (effective offense x (current health + Defense)), `BlockadeSystem.DockedOffense`, `AssaultPlanner` (docked offense, `ContestedHolds`, the force sizing in `Plan`), `GameAIMap.RecomputeIncomingOffense`, and the `FleetUIController` row (current/maximum when damaged).
- **Damage travels:** `ShipFleetPayload.Damage` is a list parallel to `Snapshots` (`DamageAt(i)`, 0 past the end), filled in `PlayerAI.EmitShipOrders` from `Planet.PeekShipDamage` and docked by `GameAI.ApplyShipArrival`; ships in flight do not repair. Saves: `ShipSave.damage` covers docked ships and fleets in flight; an older save loads full health.
- **`CombatSystem.Resolve`** (`Assets/Flatspace/GameAI/CombatSystem.cs`, pure) runs in `GameAI.GameAIUpdate` after `ProcessCurrentOrders` (arrivals have docked) and the results `Clear()`, before `UpdateAllPlanets`, and appends results to the shared list. Off while `Diplomacy.Enabled` is false (legacy). Per planet with docked warships of two or more players: (1) A fights B when `Diplomacy.IsAtWar(A, B, true)` (docked together counts as contact; a truce is not war); (2) every ship's effective offense, current health, Defense and dock index are read once (start-of-turn snapshot, ships with no health or no Health stat do not fight, ownerless ships never fight); (3) a side's pool (sum of effective offense) is split among its war rivals in proportion to `max(1, hostility toward each)`; (4) all attackers' shares aimed at one victim are summed into **one pool per victim**, so attacker order cannot matter; (5) focus fire: the victim's ships sorted by class first (0 warship, 1 siegeship, 2 bunker: `CombatUnit.Class`, so siegeships take fire only after the warships), then lowest current-health fraction, then dock index; a hit scales by `K / (K + Defense)` (`combatDamageK`, default 20); a ship the pool can kill dies and the unspent pool carries to the next; (6) damage and removals are applied after every victim was computed; (7) the ships and strength a victim lost are attributed to each attacker by its share of that victim's pool, one `UpdateResultTypeWarshipsLost` per (victim, attacker) on EVERY fight turn, with 0 lost when nothing died (`PlayerID` = victim, `Data` = `CombatLoss` with `Engaged`, the victim's start-of-turn strength split like the loss). The per-planet core is `CombatSystem.Round` (pure, over `CombatUnit`s), shared with `FightProjector`. Then **repair**: a damaged warship or siegeship heals `repairFractionPerTurn` (0.1) of its Health stat while docked at a planet its owner populates with no at-war warship present. Then **colony ships**: a player's docked colony ships are destroyed (`ColonyShipsLost`, `Data` = `ColonyLoss`) when it has no docked warship or siegeship there and an at-war rival has one, on every planet including the owner's own populated colonies.
- **Hostility:** `PlayerAI.RecordLosses` reads the `WarshipsLost` results for its own player into a private per-rival history pruned to `lossWindowTurns` (10; not saved); `LossShareToward` = how much of my strength the rival took off me / my strength ENGAGED against it (`CombatLoss.Engaged`), both totalled over the window, 0 when nothing was engaged. The numerator is `CombatLoss.StrengthDrop`: how far the victim's strength (offense x (health + Defense)) fell this round, damage included and destroyed ships counted in full, split among attackers like the loss (changed on 2026-10-06 after review: counting only a ship's strength when it died read about 0.004 for a wipe-out by attrition against about 0.5 now); `StrengthLost` (destroyed ships only) stays on the result. (It was lost / (my whole fleet + lost) and reached only 0.02-0.14; `significantLossFraction`, `surrenderMidpoint` and `surrenderSteepness` below were tuned to that old range and were recalibrated on 2026-10-06 from the first Play logs on the new measure: surrender midpoint 0.15 to 0.3, steepness 0.015 to 0.03, `significantLossFraction` 0.08 to 0.15). `HostilityCalculator` adds `hostilityPerShipLost` (1) per ship lost and subtracts `significantLossHostilityDrop` (4) a turn while the share is at least `significantLossFraction` (0.15; on the engaged-force share 0.08 fired 28-62 times a run, and on the old whole-fleet share 0.3 then 0.2 never fired). Kills add nothing.
- **Surrender** is a third `StanceMatrix` choice (`Decision.Surrender`, a flag on Peace: `Stance` is saved and is never extended): offered only while I am at war with the rival (my own War or a forced war), my visible strength is below its, and the pair is not in a truce; weight = logistic of the loss share (`surrenderMidpoint` 0.3, `surrenderSteepness` 0.03, weight 0.00005 at share 0 and even odds at 0.3; recalibrated again on 2026-10-06 to the engaged-force share, where 0.15 / 0.015 made about half of all wars end by surrender, 14-33 a run, at a median share of 0.17-0.18, and 0.04 would have put the share-0 weight at 0.00055; before that twice on 2026-10-05 (0.6 / 0.1 put the midpoint above any share the runs reached and gave 0.0025 at share 0; 0.3 / 0.03 was still above the per-rival maximum of about 0.14 and left surrender dormant)), no stickiness, and no stance hold (inside the hold the row is only "keep" or "surrender"). `StanceMatrix.Weights` scales Peace and War so they share `1 - surrender`: the surrender weight IS its probability, held or not (without it the stickiness shrank the real chance once the hold ended and the logged `pSurrender` overstated it). It emits `OrderTypeSurrender`; `GameAI.ApplySurrender` (pure) sets both stances to Peace and stamps `TruceUntil = turn + surrenderTruceTurns` (30) on both pair rows. During a truce `DiplomacyState.IsAtWar`/`WarRivals` treat the pair as not at war (so an old declaration cannot force a war), `ApplyStanceOrder` refuses a Declare War, and `StanceMatrix` leaves War out of the row. `DiplomacyState.Turn` (set each turn by `GameAI.GameAIUpdate`) is what the truce is tested against. `StanceSave.truceUntil` persists it. The surrender terms ship at plain defaults and are revisited with the diplomacy tuning (not tuned yet).
- **Tunables** on `GameAIConstants` (in code and in the constants asset): `combatDamageK` 20, `repairFractionPerTurn` 0.1, `hostilityPerShipLost` 1, `lossWindowTurns` 10, `significantLossFraction` 0.15, `significantLossHostilityDrop` 4, `surrenderTruceTurns` 30, `surrenderMidpoint` 0.3, `surrenderSteepness` 0.03.
- **Retreat** (spec `docs/superpowers/specs/2026-10-06-retreat-and-engaged-loss-share-design.md`): after combat each turn `GameAI.AppendFightProjections` appends an `UpdateResultTypeFightProjection` result per (planet, player) in a war fight (`FightProjector`, pure: the fight played to its end with `CombatSystem.Round` over every docked warship plus my own in-flight ships landing there, at most `retreatProjectionTurns` rounds; rival in-flight ships and repair are not modeled). `PlayerAI.PlanShipActions` runs `RetreatPlanner` FIRST: a deterministic gate (a war rival survives and I would lose at least `retreatCheckFraction` of my strength, a wounded survivor counting as partly lost; at a planet I populate only a wipe-out), the best destination by tier (1 a planet I populate, 2 a known empty planet or one populated only by players I am not at war with, 3 a war rival's planet whose REMEMBERED blockade value is under my group's offense / (1 + `blockadeBreakMargin`); tiers 1 and 2 hold no war-rival warship and are not blockaded by a war rival, the route avoids war-rival blockades through `RoutePlanner`, `BlockadeView.OnlyFrom`), then `RetreatMatrix` (stance-matrix pattern, `IndependentRows`, a roulette) weighs Stay against Retreat with `1 / (1 + e^(-(loss - retreatLossFraction) / retreatSteepness))`. **Imperfect intel (2026-10-06, so the decision is not a perfect oracle that reads exactly 0% or 100%):** besides the exact projection, `GameAI.AppendFightProjections` (`FightProjector.ProjectPerceived`) adds `retreatUncertaintySamples` samples of the same fight with the rival seen at `1 +- retreatRivalUncertainty` of its offense and health (one factor per sample, stratified with one random phase from `GameAI.Rand`, drawn in the engine step before any player decides so player order cannot matter). `RetreatPlanner` reads the PERCEIVED values: the mean loss (`PerceivedLossFraction`), the rival standing in at least half the samples (gate) and, for my own colony, my group wiped in at least half (gate); with no samples it falls back to the exact projection. **The destination is a roulette too:** every valid destination of the best tier becomes its own Retreat choice in `RetreatMatrix`, weighted by the retreat weight times `(cheapest cost / its cost) ^ retreatDestinationCostExponent` (normalised, `RetreatMatrix.DestinationShares`), so one pick decides whether and where; the tier order stays strict. A retreat is the ordinary ship-transport order trio (no new order type). A retreating planet's ships are removed from the garrison planner (`ShipTransportPlanner.Retreating`) and the planet is off the assault target list for `retreatCooldownTurns` (`AssaultPlanner.ExcludedTargets`; player-private, not saved). Tunables on `GameAIConstants`: `retreatCheckFraction` 0.3, `retreatLossFraction` 0.5, `retreatSteepness` 0.1, `retreatProjectionTurns` 20, `retreatCooldownTurns` 10, `retreatRivalUncertainty` 0.25, `retreatUncertaintySamples` 8, `retreatDestinationCostExponent` 2. Log lines: `Retreat|<planet>|<destination>|<tier>|<ships>|<projectedLossPct>|<rivalSurvivorsPct>|<routeCost>|<cooldownUntil>|<pRetreat>|<exactLossPct>` (`projectedLossPct` is the perceived loss the decision used, `exactLossPct` the exact projection's; tier 3 appends `|<rememberedBlockade>|<myOffense>`; once per retreat order), `RetreatStay|<planet>|<projectedLossPct>|<pRetreat>` and `RetreatHeld|<planet>|<NoDestination or OwnPlanetNotWiped>|<projectedLossPct>` (on change only, through `RetreatTracker`, log-only state), and `RetreatArrive|<planet>|<rememberedBlockade>|<rivalOffense>` (tier 3 only, from a log-only pending list; the second value is the largest single rival's docked offense found on landing, `BlockadeSystem.LargestOtherDockedOffense`, comparable with the remembered value: the blockade value itself falls to 0 as my ships dock, which made the first version read 0 in 34 of 36 arrivals). Two Play-mode findings (2026-10-06): 93-99% of retreats are at a projected loss of 100%, so the gate and the roll curve are barely exercised; a planet whose group is wiped was often refilled by the garrison planner and wiped again (12-30 repeat retreats a run on the first version, 46-106 after the surrender recalibration, about 45% of all retreats), because the cooldown only covered assault targets. Both are addressed (imperfect intel above; a planet inside its cooldown now gets a garrison of 0 from `ShipTransportPlanner.RefillBlocked`, so it is not refilled, while ships already docked there stay spare); check the next logs. Self-check: `Assets/Editor/RetreatSelfCheck.cs` (and a retreat case in `SimultaneitySelfCheck`).
- **Out of scope:** per-ship rout, retreat of colony ships, rival in-flight ships in the projection, the AI blockading on purpose, combat for ships in flight. Planet defense (bunkers, bombardment, siegeships) is built; see Planet defense. Self-checks: `CombatSelfCheck`, `DiplomacySelfCheck` (loss terms, Surrender, truce), `SimultaneitySelfCheck` (a fight, a retreat), `RetreatSelfCheck`, `PlanetDefenseSelfCheck`.

### Planet defense

Spec `docs/superpowers/specs/2026-10-21-planet-defense-design.md`, plan `docs/superpowers/plans/2026-10-21-planet-defense.md`. Two new ship kinds (`Ship.ShipKind.SiegeShip` = 2, `Bunker` = 3, appended last so the int serialization of older saves holds) and a **bombardment** step, so a planet can be defended and a defended planet attacked. Every stat, cost and weight below is a **starting point for tuning from Play logs**, not a decision.

- **Bunkers** are `Ship`s docked in `Planet.DockedShips`, per owner (`Planet.BunkerCount(owner)`), speed 0, built by the `Bunker` production subtype. They are **not ships**: `Ship.CountsAsShip(kind)` is false for them, so vision (`GameAIMap.GetVisionSourcePlanets`), contact (`PlayerKnowledge.HasContact`/`ContactPlayers`), `FleetSummary` and the fleet list ignore them, and every `Kind == WarShip` reader (`FleetStrength`, `BlockadeSystem.DockedOffense`, the garrison counts) skips them. They never fight in ship combat; they fire only in the bombardment step. The count per owner per planet is capped by `Planet.MaxPopulation` (`CanBuildBunker`, re-checked at completion in `StageCompletedProductionItem`); existing bunkers survive a population drop. They die with the planet: the end of `Planet.ChangePopulation(int, int)` calls `ClearBunkers()` whenever the last inhabitant is gone. `Planet.ConvertInhabitant` does not clear bunkers; that is safe only because no conversion runs while any bunker stands (below). No grotsits upkeep and outside `Planet.CanAffordImprovement`.
- **Siegeships** are fighting ships (`Ship.IsFightingKind`: WarShip or SiegeShip) with a large **bombard** stat, little offense, and the same health/defense/offense research mechanics as warships. Asset values (`Assets/Flatspace/Objects/Ships/*.asset`, base/max): Siegeship speed 120, health 100/200, defense 3/9, offense 3/9, bombard 20/60; Bunker health 150/300, defense 8/20, offense 12/36, bombard 0; Warship now has bombard 1/3 (a warship can dominate, barely). Created and wired into `GameAIConstants` by the menu `FlatSpace/Setup/Create Planet Defense Assets` (`PlanetDefenseAssetSetup`, which overwrites the two new assets' stats on every re-run, so do not run it after tuning in the Inspector).
- **Bombard stat and research:** `ShipData.shipBombard`/`shipBombardMax`; `WarshipStats.Bombard`/`EffectiveBombard` (bombard x current health / Health stat, like `EffectiveOffense`). `WarshipStats` is generic over the catalog subtype (`WarshipStats.SubtypeOf(kind)`: `"Warship"`, `"Siegeship"`, `"Bunker"`, case-exact); a stat is `base + tiers carried x (max - base) / tiers in the line`, where the line is the research items with effect `"<Subtype> <Offense|Health|Defense|Bombard>"` (item `type` `"Ship Improvement"`). Research catalog: `Siegeship 1` and `Bunker 1` (type `Ship Type`, cost 300, require `Warship 1`) unlock the production items; Siegeship has five-tier lines Offense, Armor (Health), Shields (Defense) and Bombard; Bunker has Weapons (Offense), Armor, Shields; Warship gained a five-tier `Warship Bombard Upgrade` line. Each line's first tier costs 3000. A ship's carried tiers are its `ResearchSnapshot`, as for warships.
- **Costs:** `WarshipCosts.ProductionCost`: Siegeship and Bunker cost `Warship catalog cost x ratio x (1 + improvementFactor x improvements researched in their own subtype)`; `GameAIConstants.siegeshipCostRatio` and `bunkerCostRatio` default 1.5 (production items cost 150 against Warship's 100).
- **Ship combat changes** (see Ship combat): victims are ordered by class first (warship, then siegeship, then bunker) so siegeships absorb fire after the warships, siegeships take part, repair and guard colony ships like warships. `CombatUnit` carries `Class` and `Bombard`.
- **The bombardment step** (`BombardmentSystem`, pure, `GameAI.RunBombardment`, run after `RunCombat` and before `RunConversion`; off in legacy mode). It triggers per bunker owner (`Triggers`): the owner has bunkers on the planet, **no docked warship or siegeship of its own**, and a player it is at war with (`IsAtWar(a, b, true)`) has a docked warship or siegeship. Then both directions run from one start-of-step snapshot and are applied afterwards (simultaneous): `BombardmentSystem.Round` calls `CombatSystem.Round` **twice** with the same units, using the zero-offense trick: (a) bunkers fire their Offense at the ships, whose Offense is set to 0 so they do not shoot back in this pass (their Strength is kept, so the loss terms are real ship strength); (b) the ships fire their **Bombard** as their offense at the bunkers, whose Offense is set to 0. In (a) only a bunker owner attacks and only a non-owner is a victim, in (b) the reverse, so two attackers at war with each other never bombard each other's ships. Damage uses the normal `K / (K + Defense)` scaling and focus fire. Ship losses are appended as `UpdateResultTypeWarshipsLost` (victim = ship owner, attacker = bunker owner), so they feed hostility and surrender like ship combat; bunker losses do not. One `BombardReport` per (attacker, bunker owner) pair at war per bombardment turn, for the `Bombard` log line.
- **Bunker repair:** after the step a damaged bunker that took no damage this turn heals `repairFractionPerTurn` (0.1) of its Health stat, unless the planet is **`Dominated`** against its owner (`BombardmentSystem.Dominated` = the planet's single dominator, `ConversionSystem.Dominator`, is a player at war with the bunker owner). Known edge: dominance is the single-dominator rule, so a third at-peace player with more docked bombard makes `Dominated` false even while an at-war rival bombards, and the bunkers repair; conversion inherits the same rule.
- **Conversion:** see Invasion. No session starts or continues while any bunker stands on the planet (a bunker appearing mid-session ends it as `DominanceLost`); pace is scaled by the dominator's docked bombard (`ConversionSystem.BombardSpeedFactor`: `clamp(bombard / conversionBombardReference, conversionBombardMinFactor, 1)`, reference 60, minimum 0.05, 0 or below = always full pace); `HasStuckBunker` (a bunker of mine, an ownerless one or one of a player I am not at war with, which bombardment never touches) removes the planet from `IsConversionTarget`/`ConversionHolds` so a fleet is not parked forever; a bunker of an at-war player stays both. `conversionBombardReference`/`MinFactor` are untuned starting points: a rival fleet's bombard at the reference converts at the old pace, so watch the `bombardFactor` field in `Convert` lines.
- **Fight projection** (`FightProjector`): the projection includes bunkers (Class 2 units) as rivals and plays the same bombardment step each projected round via `BombardmentSystem.Step` (same trigger, same participants; bunker damage carries across rounds; repair is still not modeled), so a bunker-only rival is projected and `RivalSurvives` counts bunkers. `FightProjection.MyShips` counts warships and siegeships. **Wait rule:** a group of mine that is inbound only (nothing docked yet) waits for its first landing instead of ending the projection (`mineEntered`), but only while an inbound group has not landed yet; once something of mine has been on the planet and is gone, the fight ends as before (the no-bunker retreat projection is unchanged). `GameAI.AppendFightProjections` counts bunker owners as a side, and `RetreatPlanner.TierOf` excludes a planet holding a war rival's bunker as a retreat destination (both overrides of the "no change to retreat readers" default, scoped to war rivals).
- **Building bunkers (AI):** `PlayerAI.BunkerThreatMultiplier` is the situational production multiplier (it multiplies the strategy table's `Bunker` value; computed in `GetIndustrySituationalWeightMultiplier`, where the situational weights live). 0 under Expand/None (`IsConsolidateLike` false), 0 at the cap, 0 unless an at-war rival is known nearby: its warship/siegeship docked on the planet or a direct neighbour, or an outer planet or chokepoint (the garrison planner's categories) while a known at-war rival planet exists. Else `bunkerBaseWeight` (1) x `bunkerOuterFactor` (2, outer or chokepoint) x `bunkerBlockadedBoost` (3, blockaded planet) x `bunkerRivalAdjacentBoost` (2, rival ship docked here or next door). The per-turn context (rivals, whether a known rival planet exists, the planner) is built once per production turn (`BunkerThreatContextBuilds`). Strategy-table values (`ExpandIndustryWeights`/`ConsolidateIndustryWeights`/`AmassIndustryWeights` and the matching research tables, subtype keys `Siegeship`/`Bunker`): industry Expand 0.5/0.5, Consolidate 2.0/1.5, Amass 3.0/2.0; research Expand 0.75/0.5, Consolidate 2.0/1.5, Amass 3.0/2.0. Expand's Bunker entry is dead (multiplier 0).
- **Building siegeships (AI):** `ComputeSiegeshipMultiplier`: 0 unless Consolidate-like and `WantedSiegeships()` > 0 (the assault target PlanShipActions would pick has known bunkers); then the Warship shortfall shape (`WarshipShortfallMultiplier` with `warshipShortfallBoost` and `warshipFleetCap`) on wanted vs owned siegeships, computed once per production turn (`SiegeMultiplierBuilds`). So nothing is built with no bunker to break (Warship, unlike it, builds on). `OwnedSiegeships()` counts docked plus in flight, including siegeships pinned at a hold, which can suppress production.
- **Assault composition** (`AssaultPlanner`): `KnownBunkerDefense(target)` = the summed Defense stat of the war rivals' bunkers on a target I know; `RequiredBombard` = that x `siegeBombardMargin` (1.3); `WantedSiegeships(target, bombardPerNewSiegeship)` = `ceil((required - committed bombard) / per-siegeship bombard)` limited to the room under `SiegeshipCap` = `floor(share x W / (1 - share))`, `share` = `siegeshipMaxFleetShare` (0.5; 0 or less = none, 1 or more = no cap), `W` = max(required warship force, my warships docked + in flight there). Committed bombard counts docked siegeships (health-scaled) plus in-flight ones (`Planet.GetIncomingBombard`). `AssaultPlanner.Plan` sends warships and siegeships as **separate per-kind `ShipAction`s** (`Kind` on the action; `EmitShipOrders` and `ProcessShipActions` claim per kind): the warship force goes with what exists whether or not any siegeship is available (no stall), and siegeships that exist go the same turn. Siegeship sources exclude a planet where a war rival's bunker stands (they are bombarding it) and any `ConversionHolds()` planet (their bombard sets its conversion pace; without this rule they were pulled to the next target at a roughly 20x conversion-pace loss).
- **Follower gate:** each siegeship group passes `PlayerAI.CanSendSiegeship` (`AssaultPlanner.SiegeshipGate`) before it leaves: `ProjectArrival` runs `FightProjector` for the group's arrival (my ships docked there and in flight, the ships leaving this turn from the plan so far, the group itself, against the rivals' ships and bunker fire; a delay beyond `retreatProjectionTurns` lands on the last round) and the group goes only if it is not wiped and its perceived loss fraction is below `followerMaxLossFraction` (0.5; 1 or more = only a wipe-out holds it, 0 or less = never into a fight). A refused group waits where it is and warships never wait for it. With no war rival's ship or bunker at the target the projection is null and the group goes. Log lines `SiegeFollow`/`SiegeHeld` (`SiegeFollowerTracker`) and `SiegeSized`.
- **Escorted colonists:** a colonist whose shortest route crosses a known blockade (the target included), or that targets a planet I dominate (`IsConversionTarget`), may be escorted instead of detoured. `PlayerAI.PlanEscort`: needed offense = the largest known blockade on the route x (1 + `blockadeBreakMargin`), at least one ship for a dominated target; the ships are the origin's existing **spare** warships only (`EscortSpareShips`: the garrison planner's states built as `PlanShipActions` builds them, no production request), taken in dock order until the offense covers the need; none if the spare falls short. The escort flies the colonist's route as the ordinary ship-transport trio, launched the same turn, and the colonist order records `GameAIOrder.EscortOffense` (saved as `OrderSave.escortOffense`; absent = 0), which `BlockadeSystem` subtracts from the blockade value at each node (`value - EscortOffense <= 0` lets the colonist pass) and `ColonistRedirect` honors. On arrival the escort ships are ordinary docked warships. **Escort-or-detour is a single `ScoreMatrix` entry per target at the cheaper cost** (escorted shortest route versus detour), not two choices. A retreat has priority: an origin with a fight projection of mine that turn has 0 spare, so no ship is both retreated and escorted (`EscortDetour ... Fight`). The escorts are claimed before the planners run: `ShipTransportPlanner.Reserved` (garrison sizing) and `AssaultPlanner.PreDeparting` (assault sizing and the siegeship gate's projection) receive them, so no ship is planned twice and the assault group is sized and gated with the escort already gone; retreats are exempt from the spare cap and are never trimmed by an escort. Log lines `EscortLaunch`/`EscortDetour` (`EscortDetourTracker`).
- **Saves:** no new save-shape beyond ints: `ShipSave.kind` covers the new kinds (older saves, which only know kinds 0 and 1, load unchanged), `ShipSave.damage` covers bunkers and siegeships, `ShipFleetPayload` carries the kind and per-ship snapshots/damage for siegeship fleets, `OrderSave.escortOffense` the escort. Log-only trackers (`SiegeFollowerTracker`, `EscortDetourTracker`, the last `SiegeSized` key) are not saved, so a load re-logs current states; a `ConversionStart` re-logged after a load shows `bombardFactor` 1 (stats are not in scope there).
- **Tunables** on `GameAIConstants` (in code; the asset needs no edit until tuned): `siegeshipCostRatio` 1.5, `bunkerCostRatio` 1.5, `siegeBombardMargin` 1.3, `siegeshipMaxFleetShare` 0.5, `followerMaxLossFraction` 0.5, `bunkerBaseWeight` 1, `bunkerOuterFactor` 2, `bunkerBlockadedBoost` 3, `bunkerRivalAdjacentBoost` 2, `conversionBombardReference` 60, `conversionBombardMinFactor` 0.05 (plus `repairFractionPerTurn` 0.1 now also for bunkers, `combatDamageK` 20 for both bombardment passes, the stat assets above, and the table weights). Fixtures set `conversionBombardReference` to 1 in `WarshipSelfCheck.MakeConstants` so the older pace checks hold.
- **Known limitations (as built):** (1) retreat moves warships only, so siegeships left at a lost fight take all the fire, and a siegeship-only group is never projected or retreated; (2) colony-ship production still requires a clean route (`PlanetHasColonizationTarget` is unchanged), so a frontier target reachable only by escort never triggers colony-ship builds; (3) escort-or-detour is one entry at the cheaper cost, and a dominated target gets no escort when the shortest route is blockaded and a clean route of equal cost exists (strict `<` in the code); (4) an escort can land on a blockaded planet without a `FightProjector` arrival check, and can overshoot a blockade/assault target planned the same turn (this turn's launches are not in the incoming counters); (5) siegeships parked at a conversion hold are not assault sources, so a bunker-broken planet keeps them until its conversion ends; (6) the `Dominated` single-dominator edge above; (7) repair still runs inside `CombatSystem.Resolve` before the bombardment step, so in a rare mixed-ownership case a ship repairs and is then shot the same turn; (8) bunkers are hidden from the fleet list and the planet UI (a player cannot see them there; icons show warship, siegeship or colony ship only); (9) combat is still one round per turn (multiple rounds are in `FUTURE_FEATURES.md`).
- **Self-checks:** `Assets/Editor/PlanetDefenseSelfCheck.cs` (menu `FlatSpace -> AI -> Run Planet Defense Self-Check`, registered as `Planet Defense` in `AllAISelfChecks.cs`): kinds and defaults, bombard stats, production costs, catalog, strategy-table weights, bunker threat weight, siege sizing, Siegeship weight, assault composition, follower gate, escorts and escort reservation, bunker cap/death, bunkers-are-not-ships, siegeship combat, bombardment, conversion gate, projection bunker fire, save round-trip. Extended: `CombatSelfCheck`, `ConversionSelfCheck`, `WarshipSelfCheck`, `RetreatSelfCheck`, `ShipTransportSelfCheck`, `SimultaneitySelfCheck`, `BlockadeAvoidanceSelfCheck`, `DiplomacySelfCheck`, `PlayerKnowledgeSelfCheck`.

### Invasion (conversion)

Spec `docs/superpowers/specs/2026-10-06-invasion-conversion-design.md`. A planet changes hands by **dominance and conversion**, not by troops.
`ConversionSystem` (`Assets/Flatspace/GameAI/ConversionSystem.cs`, pure) runs in `GameAI.GameAIUpdate` right after `RunCombat` (so a won fight
starts dominance the same turn), in the engine step, so no player's decision order matters; off in legacy mode.
- **Dominance** (`ConversionSystem.Dominator`, the single rule; `BombardmentSystem.Dominated` calls it): my warships/siegeships docked with effective
  bombard above 0 and no warship/siegeship of a player I am at war with (`IsAtWar(me, x, true)`, a truce is not war). Two dominators not at war
  with each other: more docked bombard, then the lower id. **Pace:** progress a turn is `BombardSpeedFactor(my docked bombard) / TurnsPerFlip`,
  the factor `clamp(bombard / conversionBombardReference (60), conversionBombardMinFactor (0.05), 1)`. **Bunker gate:** no session starts or
  continues while any bunker (any owner's) stands on the planet; a bunker appearing mid-session ends it as `DominanceLost`. A bunker only a
  war can remove (`ConversionSystem.HasStuckBunker`: mine, ownerless, or of a player I am not at war with, which `BombardmentSystem.Triggers` never
  bombards) also takes the planet out of `IsConversionTarget` and `ConversionHolds`; a bunker of an at-war player stays both. `ConversionStart` and
  `Convert` log a trailing `bombardFactor`. Fixtures set the reference to 1 in `WarshipSelfCheck.MakeConstants` so the older pace checks hold.
- **Session** (state on `Planet`: `ConversionBy`, `ConversionProgress`, `ConversionWarMask`; saved as id + 1, so an older save loads with none):
  starts only against a holder I am at war with (the gate is on holders, never on `Owner`, which is `NoOwner` on a tie); each turn
  progress += `BombardSpeedFactor / max(1, conversionTurnsBase x (1 - p))`, `p` my share of the current inhabitants (so the pace depends on both the population share and the dominator's docked bombard; it used to depend on the share only); at 1 one inhabitant flips (the
  largest at-war holder first, then the non-war tail, ties to the lower id) and the remainder carries. It ends, progress back to 0, on
  dominance lost, nothing foreign left (Clean), or war with every player in `ConversionWarMask` over (peace, surrender, truce: `WarEnded`).
- **The flip:** `Planet.ConvertInhabitant` (no `Gameboard.Instance`); an `Owner` change clears `CurrentProduction` and `ProductionQueue`
  with no refund; improvements, stocks, morale are kept. Each flip appends an `UpdateResultTypeConversion` result (Data `ConversionEvent`,
  `PlayerID` the victim).
- **Hostility:** `hostilityPerConversion` toward the converter for each inhabitant converted from a player NOT at war with it (none for an
  at-war victim), recorded on `DiplomacyState.RecordConversion` in the engine step and charged by the victim's `UpdateDiplomacy`
  (the blockade-cut pattern).
- **Hold:** `AssaultPlanner.ConversionHolds()` (dominated planet with an at-war holder, or my running session with others left) no longer
  holds every ship. `AssaultPlanner.ConversionKeep` gives each such planet a ship count to keep, `max(1, ceil(conversionHoldKeepFraction x
  the largest nearby at-war rival's offense / my offense per ship))` capped at the ships docked (negative fraction = keep all, the original
  hold), and `ShipTransportPlanner.ConversionKeep` turns it into `Spare = docked - max(garrison, keep)`, so garrison deficits and the assault's
  blockade force can take the rest. The assault target and `ContestedHolds()` (`HeldPlanets`) still keep every ship, and retreat still wins.
  Added 2026-10-06 after the tuning log showed calls wanting 54% of held ship-turns while a rival at least as strong was nearby in 80% of
  those entries (only 10% could be released at parity, 27% at 0.5, 55% at 0.25). The `ConversionHoldSpare` audit reruns the planners WITHOUT
  any keep (`PlayerAI.AuditConversionHolds`) to log the full demand, plus the `keep` used and the ships actually `released`.
- **Colonization:** a conversion target (`IsConversionTarget`) is a planet I dominate, below max, with a foreign inhabitant left, where my
  session runs or a player I am at war with holds an inhabitant (otherwise nothing could ever convert and a colonist would only fill my
  own planet; narrowed from the spec's plain "dominated" in the final review). `IsValidColonizationTarget` also accepts it when I hold
  an inhabitant there (`IsConversionColonizeTarget`); its choice cost is divided by `ConversionColonizeDivisor` = `1 + conversionColonizeWeight
  x (1 - my share)` (a colonist is worth most early; no inhabitant of mine needed, so p = 0 gets the full tilt), multiplied into
  `ColonizationCostDivisor` with the chokepoint tilt. The ColonyShip production weight is
  unchanged. Tunables on `GameAIConstants`: `conversionTurnsBase` 3 (6 at first, where a real conquest finished only 5% of the time and planets ending owned by a player averaged 3 a run; 12 nearly stopped conquest (0.8 to 1.8 a run); 3 gave 11 to 12 conquests a run and real conquests finishing 16 to 20%, with colonization, planets at T375, wars and combat unchanged and `test2.json` colony failures possibly +27%), `hostilityPerConversion` 3, `conversionColonizeWeight` 0.5, `conversionHoldKeepFraction` 0.5 (tried 0.25 on 2026-10-06 and reverted: it released about twice as often but spread fleets over more planets, so `4p.json` blockade events doubled (41 to 85 a run), food and grotsits shipment cuts doubled and colony failures rose about 40% for a Clean share gain of 4 points; at 0.5 the release freed about 34 ships a run with Clean 23% on `test2.json` and 28% on `4p.json`).
- **Reevaluate next (owner's order, 2026-10-07; the first item starts the next session):** (1) a colony ship production boost for dominated planets
  (the ColonyShip weight is unchanged; the speed-up is used 0 to 3 times a run); (2) conversion speed scaled by the dominator's strength: BUILT as bombard-scaled speed (Planet defense; retune `conversionBombardReference`/`conversionBombardMinFactor` from the `bombardFactor` on `Convert` lines, no longer the offense idea); (3) a minimum force for dominance (largely answered: force size is not the constraint, real conquests fail on pace, not ships); (4)
  elimination of a player that loses its last planet (`PlayerOutOfPlanets` has never fired); (5) the untested tunables `hostilityPerConversion`
  and `conversionColonizeWeight`. Settled: `conversionTurnsBase` 3, `conversionHoldKeepFraction` 0.5. Closed 2026-10-07: continue-until-clean
  versus stop at ownership (kept continue-until-clean: conquered planets rarely change owner again within 30 turns, 5 to 9%).

Self-check: `Assets/Editor/ConversionSelfCheck.cs` (the bunker gate and bombard pace are also covered by `PlanetDefenseSelfCheck`).

### Diplomacy

`DiplomacyState` (`Assets/Flatspace/GameAI/DiplomacyState.cs`, on `GameAIMap.Diplomacy`, pure) holds each player's `Stance` (Peace or War) toward each rival, a hostility score, the turn of the last change and the blockade cuts waiting to be charged. **Effective war is derived, never stored:** player P is at war with Q when P's own stance toward Q is War, or Q's stance toward P is War and P has contact with Q (`PlayerKnowledge.ContactPlayers`/`HasContactWith`, per rival; ownerless ships are not a player; `HasContact` itself is unchanged). A declaration against a player the declarer never met changes nothing until the empires touch. `PlayerAI.WarRivals()` is that set (it counts my own War stance even without current contact, so a war does not flicker when a rival's ship leaves).
`PlayerAI.UpdateDiplomacy(turn, orders)` runs at the END of `ProcessResults`, after `RefreshBlockadeView` and the `switch (Strategy)` block. It writes no stance and changes no strategy: it updates hostility directly (the player's own private view) and **emits an `OrderTypeDeclareWar` / `OrderTypeMakePeace` order for each stance decision that differs from the held stance** (immediate, `Data` = the rival's id as an `int`, empty `Origin`/`Target`, never added to `CurrentAIOrders`). `GameAI.ExecuteOrder` applies it through the pure `GameAI.ApplyStanceOrder` and logs the `Stance` line, so every player decides against the same stances and a declaration is first seen by the others on the next turn. `PlayerAI.ApplyWarState(turn)` runs at the START of `ProcessResults` (after `TryEnterConsolidate`), reads the committed stances, logs `WarForced` and does the Consolidate/Amass switch, so a new strategy takes effect on the same turn's routine it always did. Per rival with contact `UpdateDiplomacy` updates the hostility (`HostilityCalculator`): `H = clamp(H x (1 - hostilityDecay) + cuts x hostilityPerCut + rival warships docked on or beside my planets x hostilityPerNearShip (not its garrison on a planet it populates and I do not) + hostilityStrengthWeight x log2(myStrength / rivalStrength) clamped to +-2, 0..hostilityMax)`. Cuts come from `BlockadeSystem.BlockadeCut.BlockerId` (recorded in `GameAI.ApplyBlockades`; cuts by players I have no contact with are discarded each turn). `FleetStrength` is the sum of docked warships' Offense x (Health + Defense) (mine anywhere, the rival's on planets I know; in-flight ships are not counted). `StanceMatrix` then decides each rival not held (`stanceHoldTurns` since its last change): a `ScoreMatrix` with the new `IndependentRows` flag (a chosen choice is not removed from the other rows; default false, so every other matrix is unchanged), choices Peace and War, War probability `1 / (1 + e^(-(H - stanceMidpoint) / stanceSteepness))`, the held stance x `stanceStickiness`. `ApplyWarState` then makes a Consolidate player at war with anyone Amass and an Amass player at war with nobody Consolidate again; Expand and None are never switched here.
`PlayerAI.IsConsolidateLike` (Consolidate or Amass) is used wherever a rule was "Consolidate only" (warship fleet-shortfall boost, ColonyShip boost, chokepoint colonization tilt, `ShipTransportPlanner` garrisons and ranking, assault and blockade-breaking), so **Amass differs from Consolidate only by its own research and industry tables** (Research: Food 1.0, Industry 2.5, Grotsits 2.0, Research 1.0, ColonyShip 0.5, Warship 4.0; Industry: Food 1.0, Industry 1.5, Grotsits 1.5, Research 0.5, ColonyShip 1.0 (0.5 at first; it left about 10 planets unclaimed by T375 on 4p.json), Warship 4.0, WarshipUpdate 4.0; starting points) **and the war gate**. The gate: `AssaultPlanner` takes an optional war set (`PlayerAI.AssaultWarFilter()`, null while diplomacy is off): only a war rival's planets are enemy-occupied and only its docked warships size the force; `ContestedHolds` no longer holds a standoff on a planet a player outside the war set populates (and I do not) unless it is blockaded against me, so after peace the force goes home instead of feeding that rival's hostility; blockade-breaking targets and blockades themselves are unchanged. A War stance toward a rival I no longer have contact with keeps its row with decay-only hostility, so the war (and Amass) can still end. `PlayerAI.WantedWarships` therefore adds the assault force only while a known planet of a player I am at war with exists (a Consolidate player at peace with everyone wants fewer warships than before).
**Legacy mode:** `GameAIConstants.diplomacyEnabled` (default true) is copied by `GameAI.InitGameAI` onto `GameAIMap.Diplomacy.Enabled`. False, and any `GameAIMap` a self-check builds directly (it never goes through `InitGameAI`), means every rival with contact is an enemy, no stance matrix and no Amass switch (today's behaviour; the clean A/B against older logs). `DiplomacySelfCheck` switches it on explicitly, so the older suites needed no edit. **Saves:** `PlayerSave.stances` (rival, stance as an int, hostility, last-change turn); an older save loads all Peace at 0; the effective state, the cut counters and the log-only numbers are not saved. Tunables on `GameAIConstants` (all in-code defaults): `hostilityPerCut` 5, `hostilityPerNearShip` 0.5, `hostilityStrengthWeight` 1, `hostilityDecay` 0.05, `hostilityMax` 100, `stanceMidpoint` 30, `stanceSteepness` 4 (8 at first; raised the curve's steepness after the first tuning run showed random early wars at hostility 0), `stanceStickiness` 3, `stanceHoldTurns` 10. Self-check: `Assets/Editor/DiplomacySelfCheck.cs`.
**Simultaneity:** every player decides against the same world. `ProcessResults` only appends orders (stance changes included) and writes a player's own private state (its caches, hostility rows, its own research flags); nothing one player's decision writes is read by another player's decision in the same turn (audit 2026-10-05: the stance write and the strategy switch were the only cross-player dependencies, both now go through orders; no mutable static state exists except `GameAI.Rand`, a shared random stream, so a player's random draws still depend on how many draws came before). `Assets/Editor/SimultaneitySelfCheck.cs` proves it (see Tests). `ProcessNewOrders` still executes player 0's orders first, which biases collisions between two players' orders (accepted, recorded as a follow-up).

### Connectivity (chokepoints)

`PlanetCentrality` (`Assets/Flatspace/GameAI/PlanetCentrality.cs`, namespace `FlatSpace.AI`, pure) counts, for every
planet, the stored shortest paths that pass THROUGH it (endpoints excluded, so a stub or 2-node path adds nothing) and
turns that into a percentile: the fraction of the other planets with strictly lower betweenness (ties share a score,
leaves score 0, a board of 0 or 1 planets scores 0). `GameAIMapInit` builds it once from the all-pairs paths it just
stored (the routes ships really fly; recomputed on every init and load, never saved). `GameAIMap` exposes
`Betweenness(name)`, `Chokepoint(name)` (the percentile, 0 for an unknown name), `IsChokepoint(name)` (betweenness > 0
and percentile >= `GameAIConstants.chokepointPercentile`, default 0.9; above 1 means none), `TopChokepoints(n)` and
`ChokepointSummary(playerId)`. Three consumers: garrison category 4 and the Consolidate garrison seam
(`ShipTransportPlanner`), the blockade target ranking (`AssaultPlanner.ChooseBlockadeTarget`), and a Consolidate-only
colonization tilt in `PlayerAI.ProcessColonizers`: a target's choice cost is `route.Cost / (1 + colonizationChokepointWeight
x percentile)` (default weight 0.5; 0 or below is off; Expand is unchanged). The colonist order's delay always uses the
real `route.Cost`, never the tilted cost. **Producer value (by planet type: Verdant 1.0, Desolate 1.0, Farm 0.5, others 0,
authored as data on `PlanetResourceData`) is agreed but deliberately not built.** The spare-ship calculation is
unchanged. Older self-check fixtures set `chokepointPercentile = 2f` and `colonizationChokepointWeight = 0` so a
3-planet chain's middle planet does not start garrisoning in their Consolidate checks. Self-check: `Assets/Editor/ChokepointSelfCheck.cs`
(centrality, map, garrison, summary and formatter; the ranking and colonization checks live in `BlockadeBreakSelfCheck`).

### Distribution Centers

A Distribution Center (DC) is a sticky, per-player, per-resource role (`PlayerAI.FoodDistributionCenters`
/ `GrotsitsDistributionCenters`, each a `List<string>` of planet names, not a flag on `Planet`) assigned
by `PlayerAI.UpdateDistributionCenters` once per turn, ahead of `ProcessResults`. It exists so that
resource shipping — which otherwise only reacts to a planet's own reported shortage — keeps a forward
stockpile topped up near the player's frontier, shortening the path a real shortage's shipment has to
travel once one is reported nearby. This matters because colonization already reaches farther than
shipping: `ProcessColonizers` gates on its own `maxPathNodesForColonization`, separate from
`maxPathNodesForResourceDistribution`, so a newly colonized planet can easily land beyond a single
producer's shipping range — a DC gives that territory something closer to resupply from. How many DC
slots a player is allowed (0, 1 or 2) is gated by two
board-size tunables on `GameAIConstants`, `minPlanetsForDistributionCenters` and
`minPlanetsForSecondDistributionCenter`, both counted against the WHOLE board's colonized planet total
(every player, not just this one) via `GameAIMap.TotalColonizedPlanetCount()` — small boards get none.
A lost DC (no longer owned/populated by this player) is pruned and logged (`DCLost`) before a
replacement is considered.

**Selection** (`PlayerAI.SelectDistributionCenter`) is coverage-maximizing, not simply "closest to a
producer": among this player's colonized, not-yet-DC planets reachable from at least one current
surplus-reporting planet (a "producer", within `maxPathNodesForResourceDistribution`), it scores each
candidate by how many OTHER colonized, non-producer planets it would newly reach that no producer
already reaches directly — i.e. it picks the planet that extends supply coverage the furthest, not the
cheapest one to supply. Ties break by supply distance (farther from its own supplying producer wins,
so it plants the DC further out), then by cheapest path cost to that producer. A choice sticks
(`DCSelected` logged) until lost.

**Synthetic demand:** once selected, a DC below its per-resource target stock
(`distributionCenterFoodTargetStock` / `distributionCenterGrotsitsTargetStock`, default 50) that has no
real shortage reported for it this turn gets a synthetic shortage-shaped entry added to the same list
`ProcessResourceShipments` already ships against, so it rides the exact same rounds-based capping
and order-emission path as a real shortage. It uses a fixed, very low priority sentinel
(`DistributionCenterPrioritySentinel = float.MinValue / 2f`), not a gap-derived priority, so a real
shortage anywhere always outranks and claims surplus before a DC's synthetic demand does. The synthetic
list (`syntheticShortageNames`) is tracked separately from DC-ownership (`distributionCenters`)
precisely because a DC can ALSO report a genuine real shortage the same turn — that real shortage must
keep its own normal, gap-derived priority instead of being wrongly demoted to the sentinel just because
the planet happens to hold a DC role (`RunNoDuplicateRowForDCWithRealShortageCheck`).

`PlayerAI.IsCoverageGap` is a one-turn-stale diagnostic (using the last-known, cached surplus/DC lists,
since it runs from order execution before this turn's `UpdateResult`s exist) that flags a newly
colonized planet with no path to any surplus planet or DC at all; it drives the `DCCoverageGap` log
line. Saves persist each player's DC lists (`PlayerAI.SetDistributionCenters`); an older save with no
field restores an empty list, i.e. "select fresh."

**Two bugs from the final whole-branch review, both fixed, are worth knowing about because they explain
otherwise-odd-looking code nearby:** (1) `PlayerAI.BuildResourceMatrix`'s surplus-source filter now
checks `s.Name != shortage.Name` before any `pathMap` lookup — `Planet.DistanceMapToPathingList` never
contains a planet's own name, so a DC that is simultaneously a real surplus source (a routine state
while its stock is above its own small population-based need but still below its much larger target)
used to throw `KeyNotFoundException` the moment it checked distance to itself. (2) `PlayerKnowledge.Update`'s
BFS-through-known-territory fix, described under Player Knowledge above, was found by the same review and
exists for the same reason: DCs are only useful once the known frontier can outrun a one-hop-per-source
walk as an empire grows.

`FlatSpace → AI → Run Distribution Center Self-Check` (`Assets/Editor/DistributionCenterSelfCheck.cs`) is
this feature's own self-check; the two regressions above are additionally covered by
`RunDistributionCenterAlsoReportsSurplusCheck` in `PlayerAIResourceSelfCheck` and
`RunKnowledgeBfsPassesThroughKnownTerritoryCheck` in `PlayerKnowledgeSelfCheck`.

