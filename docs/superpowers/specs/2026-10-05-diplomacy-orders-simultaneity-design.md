# Diplomacy as orders, and simultaneous player decisions: design

Date: 2026-10-05. Path: architectural. Prepares `ship_combat` element 2 (ship to ship combat); its decisions so far are in
`2026-10-05-ship-combat-decisions-so-far.md`. Session rules: `docs/session_configuration.md`. Follows the diplomacy spec
(`2026-10-04-diplomacy-design.md`).

## Purpose

Combat and surrender need every player to decide against the same world and to change shared state only through orders, like
the rest of the AI (`PlanetUpdateResult` in, `GameAIOrder` out). Today diplomacy breaks that pattern: `PlayerAI.UpdateDiplomacy`
writes a stance the moment it decides, and the Consolidate/Amass switch reads other players' stances, so a player deciding later
in the `GameAI.ProcessResults` loop (higher player id) sees an earlier player's new stance in the same turn.

Two task groups, in order, then a tuning/testing pass (not part of this spec) before combat is brainstormed again:

1. **Diplomacy as orders.** Stance changes become orders executed in `ProcessNewOrders`.
2. **Simultaneity.** Audit `ProcessResults` for order dependence, fix what is found, and add a self-check that proves player
   order does not matter.

Success: every player's decisions in a turn are independent of the player order; a stance change reaches the world only through
an order; with diplomacy on, play is unchanged except that a rival's declaration is first seen on the next turn.

## Task group 1: diplomacy as orders

- **Order types.** `GameAIOrder.OrderType` gets `OrderTypeDeclareWar` and `OrderTypeMakePeace`, **appended last** (the type
  serializes as a `JsonUtility` int). `OrderTimingType.Immediate`, `PlayerId` = the deciding player, `Data` = the rival's player
  id (a boxed `int`; code branches on the runtime type as elsewhere), no origin or target planet.
- **Emit.** `PlayerAI.UpdateDiplomacy` still updates hostility directly (it is the player's own private view: its own pair rows,
  its own cut counters) and still runs `StanceMatrix.Decide`. Where it called `DiplomacyState.SetStance` it now appends an order
  for each decision that differs from the held stance and passes the hold check, and stores the decision's `PWar` on the
  decider's own pair row. `UpdateDiplomacy` takes the shared `orders` list (it is already called from `ProcessResults`, which
  has it).
- **Execute.** `GameAI.ExecuteOrder` handles the two types by calling a new pure `DiplomacyState` method (the `SetStance` write,
  the hold-turn bookkeeping and the `Stance` log line), so self-checks drive it with no `Gameboard.Instance`. Execution
  happens in `ProcessNewOrders`, after every player has decided.
- **Strategy switch and forced-war bookkeeping.** The Consolidate to Amass switch, the Amass to Consolidate switch and the
  `WarForced` Start/End lines move out of `UpdateDiplomacy`'s end to the start of `ProcessResults` (beside
  `TryEnterConsolidate`), where they read stances committed by the previous turn's orders. A player's own declaration is
  therefore acted on exactly when it was before (decided turn t, committed end of t, switch at the start of t+1, the t+1
  routine uses it). The one intended change: a rival's declaration, once seen the same turn by any later player, is first seen
  on the next turn.
- **Tolerating orders with no planets.** Confirm and, where needed, guard `GameAI.LogNewOrders`,
  `Gameboard.CreateNotificationsForNewOrders` and the order-line drawing for orders with a null or empty origin and target.
- **Saves.** None: the orders are immediate and never reach `CurrentAIOrders`. Stances are saved as before.
- **Not built here.** `OrderTypeSurrender` and the pair lock arrive with combat; this group only makes the path ready for them.
- **Legacy mode** (`Diplomacy.Enabled` false) emits nothing, as before.

## Task group 2: simultaneity

- **Audit.** Read `ProcessResults` and everything it calls (`ProcessResultsStrategyExpand`, `BuildResourceMatrix`,
  `ProcessColonizers`, `ProcessShipActions`, `BuildIndustryMatrix`, `ChooseNewResearch`, the Distribution Center update,
  `RefreshBlockadeView`, `UpdateDiplomacy`) for any read of state that another player's `ProcessResults` can write in the same
  turn: planets, ships, shared maps and caches, `DiplomacyState`, the shared result and order lists. Record each finding and fix
  it (move the write into an order, or read a start-of-turn snapshot). Findings go into the plan and the final summary.
- **Self-check.** New `Assets/Editor/SimultaneitySelfCheck.cs`, registered in `AllAISelfChecks`. It builds the same
  three-player fixture twice (never `Gameboard.Instance`), runs each `PlayerAI.ProcessResults` in forward order on one and
  reversed on the other, applies the emitted orders, and asserts per player: the same orders (compared by content, not list
  position), the same stances and the same strategy. `ScoreMatrix` has random picks, so the check seeds `UnityEngine.Random`
  to a fixed per-player value before each call. The fixture should include contact and a declared war so the stance path is
  exercised.
- **Out of scope (recorded).** `ProcessNewOrders` still executes player 0's orders first, which biases collisions between two
  players' orders (two colonists to one planet). Rotating the start player would change every economy number and break
  comparison with older tuning logs, so it stays a follow-up unless the audit shows it matters.

## Self-checks

- `DiplomacySelfCheck` cases that assert a stance straight after `UpdateDiplomacy` apply the emitted orders first. New cases:
  declare and peace orders execute and log the stance, hold turns still hold, the switch and the forced-war lines read committed
  stances at the start of `ProcessResults`, the orders carry the rival id.
- `SimultaneitySelfCheck` as above.
- `Run All AI Self-Checks` gets the new suite (12 suites).

## Tuning log

No new line. `Stance|...`, `WarForced|...` and `StrategyChange|...` keep their formats and are written at order execution and at
the start of `ProcessResults` respectively, so the `Stance` line is the same turn as before and the others one `ProcessResults`
call later in the same turn. The `tuning-log` skill and the "Diplomacy" and "AI Tuning Log" sections of `CLAUDE.md` get a note
that stances commit through orders and that a rival's declaration is first seen the next turn. Task: group 1 docs.

## Tuning and testing pass (after both groups, before combat)

Play-mode runs on `test2.json` and `4p.json` compared with the diplomacy baselines in `HANDOFF-diplomacy-2026-10-04.md`
(stance flips, first war turn, share of players in Amass, planets, arrivals, fleet-cap violations). Expected differences: only
the next-turn visibility of a rival's declaration.
