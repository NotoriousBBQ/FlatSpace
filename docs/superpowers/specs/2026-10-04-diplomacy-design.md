# Diplomacy simulation (ship_combat milestone, element 1)

Date: 2026-10-04. Source: `FUTURE_FEATURES.md`, milestone `ship_combat`, element (1). Brainstormed one question at a time;
every choice below is the user's pick (recommendation first each time).

## Goal

Today every other player is an enemy: first contact flips Expand to Consolidate (`PlayerKnowledge.HasContact`), any other
player's docked warships blockade a planet (`BlockadeSystem` counts the largest single other player), and the assault targets any
other player's planet. Add a per-player, per-rival **stance** (Peace or War) that each AI simulates, driven by a **hostility
score**, so that combat (elements 2 to 5) has someone to be at war with and peace is possible. War puts a player in
`AIStrategyAmass`; peace with every rival returns it to `AIStrategyConsolidate`.

Success: in a Play run, players that touch drift into war or stay at peace by their own situation, wars end, strategies follow,
the tuning log shows why each stance changed, and the economy keeps running in every strategy.

## Decisions (the user's)

1. **Scope B:** peace/war gate plus a drifting attitude score (not a bare gate, not full alliances or proposals). War triggers
   Amass; peace with all other players goes back to Consolidate.
2. **The stance decision is a new ScoreMatrix variant** (`StanceMatrix`).
3. **Amass runs the Expand routine** with its own war-tuned research and industry tables (today its `ProcessResults` case is an
   empty `break`, which would freeze a player). Not copied rules: Amass inherits Consolidate's behaviour through one helper.
4. **Each player holds its own stance toward each rival; war needs only one side.** If one side picks War, the other side's
   stance is forced to War **once it has contact with that rival** (per-pair contact, not "any player").
5. **Hostility inputs: (1) blockade cuts, (2) rival warships near my planets, (4) relative fleet strength including defense,
   (5) decay.** Colonization competition (3) and border sharing (6) are left out (contested planets are routine and desired).
6. **Strength = sum over docked warships of Offense x (Health + Defense)**, mine against the rival's visible fleet.
7. **The matrix is a weighted pick with the current stance favoured** (stickiness bonus and a minimum hold), not thresholds.
8. **Gate (option B):** assaults on enemy-occupied planets are war-only; blockades themselves and blockade-breaking targets are
   unchanged (so cuts can happen at peace and feed input 1).
9. **Ordering:** the stance update and the strategy switch run after `RefreshBlockadeView` and after the `switch (Strategy)` block
   in `PlayerAI.ProcessResults`. `TryEnterConsolidate` stays first. A new strategy therefore takes effect on the next turn's routine.

## Parts (all pure, no `Gameboard.Instance`, so one self-check suite drives them)

### DiplomacyState (`Assets/Flatspace/GameAI/DiplomacyState.cs`, namespace `FlatSpace.AI`)
Per player: for each rival, the stance (Peace default), the hostility score and the turn of the last stance change. Exposes
`StanceToward(rival)`, `Hostility(rival)`, `TurnsSinceChange(rival, turn)` and `IsAtWar(me, rival, contact)`:

> player P is at war with Q when P's own stance toward Q is War, **or** Q's stance toward P is War **and** P has contact with Q.

Effective war is derived, never stored. A declaration against a player that has never met the declarer changes nothing until the
empires touch. The forced war ends when Q's stance returns to Peace (war needs one side). A player holds a stance only toward
rivals it has contact with; with no contact the stance is Peace and the matrix does not run.

### Per-rival contact (`PlayerKnowledge`)
`HasContactWith(map, playerId, rivalId)`: the existing `HasContact` test restricted to one rival. `HasContact` keeps its behaviour
(it can call the new method over all rivals).

### FleetStrength (next to `WarshipStats`)
`FleetStrength.Of(ships, stats)` = sum over docked warships of `Offense x (Health + Defense)` from `WarshipStats` (the template
plus the ship's research snapshot). One pure method so combat (element 2) can replace the formula in one place. Visibility: my
own docked warships, and the rival's docked warships on planets I know (the limit `AssaultPlanner` already uses; ownerless ships
not counted). **Docked ships only on both sides** (in-flight ships are not counted; one place to add them later).

### HostilityCalculator (`Assets/Flatspace/GameAI/HostilityCalculator.cs`)
Once per turn, per rival with contact:

`H = clamp(H x (1 - hostilityDecay) + cuts + near + strength, 0, hostilityMax)`

- **cuts (1):** that rival's blockade cuts on my orders this turn x `hostilityPerCut` (default 5). `BlockadeSystem.BlockadeCut` has
  only `PlayerId`, `Planet` and `Value` today, so it gains `BlockerId` (the player whose offense set the blockade value at that
  node, the same player the `Blockade` log line already names). `GameAI.ApplyBlockades` already routes each cut to the owner.
- **near (2):** `hostilityPerNearShip` (0.5) per rival warship docked on a planet I hold or beside one of my populated planets
  (known planets only).
- **strength (4):** `hostilityStrengthWeight` (1.0) x log2(myStrength / rivalStrength), clamped to +-2. A rival with no visible
  fleet counts as +2, two empty fleets as 0. A stronger player drifts toward war, a weaker one toward peace.
- **decay (5):** `hostilityDecay` (0.05) per turn. With only the strength term the steady state is about 40.
- `hostilityMax` 100.

### StanceMatrix (`Assets/Flatspace/GameAI/StanceMatrix.cs`, wrapper types like `ResourceMatrix.cs`)
A new variant of the `ScoreMatrix` machinery: one **independent row per rival** with contact; the choices are Peace and War.
`ScoreMatrix.GenerateActionList` removes each chosen choice from every other row (`RemoveAll(v => v.Equals(chosenChoice))`), which
is wrong here (War toward rival 1 must not remove War toward rival 2), so the variant decides each row on its own and reuses the
same `WeightedPick` roulette (`weightSelector`). The generic class is not changed for its existing users.

- Weights: `pWar = 1 / (1 + e^(-(H - stanceMidpoint) / stanceSteepness))` (midpoint 30, steepness 8); War gets `pWar`, Peace
  `1 - pWar`.
- The current stance's weight is multiplied by `stanceStickiness` (3).
- A row is not evaluated for `stanceHoldTurns` (10) turns after a change.
- Row priorities are unique ranks (defensive habit; ties break by Target name, see `ScoreMatrixDecisionComparer`).

### Strategy switch (`PlayerAI`)
At the end of `ProcessResults`, after `RefreshBlockadeView` and the `switch (Strategy)` block: a Consolidate player at war with any
rival becomes Amass; an Amass player at war with no rival becomes Consolidate (both logged as `StrategyChange`). Expand and None are
untouched (an Expand player flips to Consolidate on first contact through `TryEnterConsolidate`, which is first in the method and is
unchanged, and it holds no stance before contact). `TryEnterConsolidate`'s "Amass is never switched" test stays true: the return path
is this separate switch. Players run in order within a turn, so a declaration by player 0 is visible to player 1 the same turn and
not the reverse (at most one turn of difference, accepted).

### Amass tables and `IsConsolidateLike`
A missing strategy key falls back to a neutral weight, so every key gets an entry.

| | Food | Industry | Grotsits | Research | ColonyShip | Warship | WarshipUpdate |
|---|---|---|---|---|---|---|---|
| Research, Consolidate | 1.0 | 3.0 | 2.0 | 1.0 | 0.5 | 2.5 | |
| **Research, Amass** | 1.0 | 2.5 | 2.0 | 1.0 | 0.5 | **4.0** | |
| Industry, Consolidate | 1.0 | 2.0 | 1.5 | 1.0 | 1.0 | 2.5 | 2.5 |
| **Industry, Amass** | 1.0 | 1.5 | 1.5 | 0.5 | 0.5 | **4.0** | **4.0** |

These are tuning starting points. `ProcessResults`'s Amass case runs the Expand routine (like Consolidate). The rules written as
"Consolidate only" (the Warship fleet-shortfall boost, the ColonyShip x2 while targets remain, the chokepoint colonization tilt, the
planner's garrison rules, the assault and the blockade-breaking targets) use one helper, `IsConsolidateLike(strategy)` (Consolidate
or Amass), so Amass differs from Consolidate only by its tables and the war gate. No separate fleet cap for Amass (a recorded option,
not built).

### The assault gate
`AssaultPlanner.ChooseEnemyTarget` and `HasKnownEnemyPlanet` take a war filter (the set of players I am at war with): an
enemy-occupied target must belong to one of them. Blockade-breaking targets (`ChooseBlockadeTarget`) are unchanged. **This touches
`WantedWarships`:** the assault's required force is added whenever a known enemy planet exists, so a Consolidate player at peace with
everyone no longer wants that force, and only Amass does. Peaceful players want fewer warships than before; war players the same.
The blockade ranking and the committed-force rules are not touched.

### Legacy switch
`GameAIConstants.diplomacyEnabled` (default true). When false: every rival with contact counts as at war, there is no stance matrix
and no Amass switch (strategy stays Consolidate), i.e. today's behaviour. Older self-check fixtures set it false (like their
`chokepointPercentile = 2f`) and it gives a clean A/B against old logs.

### Saves
`GameSave.PlayerSave` gains a stance list (rival id, stance, hostility, last-change turn). An older save (missing field) loads as all
Peace at 0 hostility. The effective state is derived, so nothing else is saved.

## Tunables (`GameAIConstants`, all with in-code defaults, no asset edit needed)
`diplomacyEnabled` true, `hostilityPerCut` 5, `hostilityPerNearShip` 0.5, `hostilityStrengthWeight` 1, `hostilityDecay` 0.05,
`hostilityMax` 100, `stanceMidpoint` 30, `stanceSteepness` 8, `stanceStickiness` 3, `stanceHoldTurns` 10.

## Proposed tuning log output
All in `T<turn>|P<playerId>|<EventCode>|<fields...>`. Added in the task that builds the stance matrix and the strategy switch.

| Line | When | Question answered | Repeat control | Test |
|---|---|---|---|---|
| `Stance\|<rival>\|<Peace or War>\|<hostility>\|<cutsTerm>\|<nearTerm>\|<strengthTerm>\|<pWar>` | when a stance changes | why did this war or peace start; which input drove it | logged only on a change, from `DiplomacyState` reporting it (no tracker) | none (logger has no self-check by design; the change report is tested) |
| `WarForced\|<rival>\|<Start or End>` | when a war arrives or ends through the rival's stance | how often players are dragged in; delay after the declaration (contact timing) | log-only state in `PlayerAI` (like `_colonizeHeldBack`), not saved, so a load logs each current one once more | the transition detection |
| `Hostility\|<rival>\|<H>\|<myStrength>\|<rivalStrength>\|<nearShips>` | every 25 turns per player and rival with contact, beside `Economy` | are the weights sane; where the score settles | periodic | none |
| `StrategyChange\|<from>\|<to>` (existing) | now also Consolidate to Amass and back | when wars start, how long they last | already one line per change | strategy-switch cases |

No new line for the assault gate: the existing `AssaultTarget` line, counted against `Stance`, shows whether assaults still happen.
The `tuning-log` skill gets a "Diplomacy" section: first war turn, share of players in Amass, war length, forced wars against chosen
ones, stance flips within 20 turns (the stickiness check), warship starts under Amass against Consolidate, fleet-cap discipline under
Amass, where hostility settles. `CLAUDE.md` gets a Diplomacy section, the new lines under "AI Tuning Log", the Amass change,
`IsConsolidateLike`, the wanted-fleet note and the save fields.

## Tests
New `Assets/Editor/DiplomacySelfCheck.cs` (`FlatSpace -> AI -> Run Diplomacy Self-Check`, `public static bool RunChecks()`),
registered in `AllAISelfChecks` (the 11th suite). No `Gameboard.Instance`; every test planet has a distinct position (A* tie-break
note); no assertion exactly on a float boundary.
1. `FleetStrength`: formula, clamp, empty cases, docked only, known planets only.
2. `HostilityCalculator`: each term alone (cuts attributed to the blocker, near ships on held and neighbouring planets only,
   strength sign and clamp, decay, the maximum).
3. `StanceMatrix`: rows are independent (two rivals both War), the logistic weights, stickiness, the hold period.
4. `DiplomacyState`: effective war (own War; the rival's War forces it only with contact; ends when the rival returns to Peace),
   `HasContactWith` and an unchanged `HasContact`.
5. Strategy switching: Consolidate to Amass on any war, back when none, war with one of two rivals keeps Amass, Expand and None
   untouched, `TryEnterConsolidate` unchanged.
6. The Amass tables (every key, in `PlayerKnowledgeSelfCheck` beside the existing ones) and the `IsConsolidateLike` seams, including
   that an Amass player emits the same orders as Consolidate in a resource-style fixture.
7. The assault gate (an enemy-occupied target only from a war pair, blockade-breaking unchanged), `WantedWarships` without the
   assault force at peace, and `diplomacyEnabled = false` reproducing today's behaviour.
8. The save round trip of the stance list, and an older save loading as Peace at 0.
Older suites that run two-player `ProcessResults` set `diplomacyEnabled = false` in their constants helpers.

## Out of scope
Colonization competition and border sharing as hostility inputs; in-flight ships in strength; a separate Amass fleet cap;
proposals, alliances, non-aggression; combat, invasion and planet defense (elements 2 to 4); the AI blockading on purpose (5);
any change to the blockade ranking or the committed-force rules.
