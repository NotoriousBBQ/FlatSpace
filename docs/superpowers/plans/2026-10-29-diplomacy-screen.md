# Diplomacy Screen Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** A HUD screen (button + T key) where the human picks a rival they have met, sees the stances, the rival's hostility, its stance toward other met rivals and its visible fleet strength, and can declare war, end a war, or surrender; with an empty response area and an inactive Demand surrender button.

**Architecture:** A pure builder (`HumanDiplomacyView`, no `Gameboard.Instance`) turns `DiplomacyState`, `PlayerKnowledge` and docked ships into rows, a rival view and button states; the button states come from `HumanCommandProcessor.Check`, extended to the three stance commands, so the screen can never offer what the processor would refuse. A static UI Toolkit screen (`DiplomacyScreen`, patterned on `PlanetsScreen`) draws it and sends commands through `Gameboard.SubmitHumanCommands`.

**Tech Stack:** Unity 6000.4.1f1, C#, UI Toolkit built in code (`HumanUiKit`), Editor self-checks (no test framework).

**Spec:** `docs/superpowers/specs/2026-10-29-diplomacy-screen-design.md`

## Global Constraints

- Unity cannot run from the CLI: a "run" step means the owner focuses the Editor, reads the Console and runs `FlatSpace -> Human -> Run Human UI Self-Check` (the `Check` returns `ALL PASSED`). Code that wires in `Awake`/`Start` needs the owner to exit and re-enter Play mode.
- A rival the human has not met has **no entry anywhere** (list, third-party block). The "others" set is exactly `ContactPlayers(human)` minus the picked rival.
- A rival's fleet shows warship count and one strength number only, never ship detail. "Planets I can see" is the fog view (`Gameboard.HumanSeesPlanet`), **not** `FleetStrength.VisibleOf`.
- No simulation writes from the screen: every change is a `HumanCommand` through `Gameboard.SubmitHumanCommands(commands, "Diplomacy")`.
- Declare war, End the war and Surrender each ask for one confirmation. Surrender's text names the truce: `surrenderTruceTurns` (30) turns, no declarations, no war, nothing else changes.
- Demand surrender: visible, greyed, hint "Needs negotiation", no behaviour. No other ask-and-trade control exists.
- Response area: upper right of the screen, empty, dim "No messages yet".
- Rivals are labelled `Player n`, coloured with `Player.PlayerColors[n]`.
- Files are CRLF (no `sed -i`; edit with the Edit tool or an exact-anchor Python replace). New scripts commit with their `.meta`. Stage by explicit path, never `git add -A Assets`. Commit only when told to; the steps below mark where a commit is proposed.
- Namespaces: pure code `FlatSpace.Human`; the screen `Game.UI.MainGameScreenUI` (as `PlanetsScreen`).

## Review Focus

- Diplomacy switched off (`Diplomacy.Enabled` false, the legacy mode): the screen shows "Diplomacy is switched off in this match" and no rows, instead of rows whose buttons all refuse `DiplomacyOff`. (Task 2)
- No rivals met yet: the list is empty with a "No rivals met yet" line and nothing is selected; no exception. (Tasks 2, 3)
- The picked rival stops being a contact (contact flickers) or the match reloads: the selection falls back to the first row, or to none. (Tasks 2, 3)
- Mine Peace, theirs War: shown as "still at war", Declare war enabled, End the war disabled with the Surrender hint. During a truce: Declare war disabled "Truce", Surrender disabled "Not at war". (Task 2)
- A rival with warships on an unseen planet, or only non-warship kinds, adds nothing to the fleet lines or the total. (Task 2)

---

### Task 1: `HumanCommandProcessor.Check` supports the stance commands

**Files:**
- Modify: `Assets/Flatspace/Player/Human/HumanCommandProcessor.cs` (`Check`, about line 46)
- Test: `Assets/Editor/HumanUiSelfCheck.cs` (new `RunDiplomacyCheckChecks`, called from `RunChecks`)

**Interfaces:**
- Consumes: `HandleStance` (refusals `DiplomacyOff`, `UnknownRival`, `AlreadyAtWar`, `Truce`, `NoContact`, `AlreadyPeace`, `NotAtWar`); `HumanModeSelfCheck.Fixture`, `HumanModeSelfCheck.Dock`.
- Produces: `Check(new HumanCommand { type = "declareWar" | "makePeace" | "surrender", rival = id })` returns null when accepted, else the refusal reason; it changes no stance and adds no order.

- [ ] **Step 1: Write the failing test** `RunDiplomacyCheckChecks`. Fixture of 3 planets, `f.Map.Diplomacy.Enabled = true`, rival 1 docks a warship on P2 (contact). Assertions, each via `new HumanCommandProcessor(f.Map, f.Human, 5).Check(...)`:
  - `declareWar` rival 1 returns `null` (Peace, contact, no truce); `makePeace` returns `"AlreadyPeace"`; `surrender` returns `"NotAtWar"`.
  - After `f.Map.Diplomacy.SetStance(0, 1, Stance.War, 4)`: `declareWar` returns `"AlreadyAtWar"`, `makePeace` returns `null`, `surrender` returns `null`.
  - After `SetStance(0, 1, Stance.Peace, 4)` and `SetStance(1, 0, Stance.War, 4)` (mine Peace, theirs War): `declareWar` returns `null`, `makePeace` returns `"AlreadyPeace"`, `surrender` returns `null`.
  - With `TruceUntil` set to 30 on the pair `(0,1)`: `declareWar` returns `"Truce"` and `surrender` returns `"NotAtWar"`.
  - A rival id 2 with no ships anywhere (no contact): `declareWar` returns `"NoContact"`.
  - After each batch of `Check` calls, `f.Map.Diplomacy.StanceToward(0, 1)` is unchanged (Check applies nothing).
- [ ] **Step 2: Run** the Human UI Self-Check. Expected: the new assertions FAIL with `CheckNotSupported`.
- [ ] **Step 3: Implement** in `Check`: add `case "declareWar": case "makePeace": case "surrender":` to the dry-run group (`scratch._dryRun = true; scratch.Dispatch(...)`). `HandleStance` only reads state and appends to a throwaway order list, so no further change is needed; update the doc comment on `Check` to list the new commands. Add `RunDiplomacyCheckChecks` to `HumanUiSelfCheck.RunChecks`.
- [ ] **Step 4: Run** the Human UI Self-Check and `Run All AI Self-Checks`. Expected: ALL PASSED.
- [ ] **Step 5: Commit** (when asked): `HumanCommandProcessor.cs`, `HumanUiSelfCheck.cs`; message "feat: Check supports the stance commands".

### Task 2: `HumanDiplomacyView`, the pure builder

**Files:**
- Create: `Assets/Flatspace/Player/Human/HumanDiplomacyView.cs` (+ `.meta`)
- Test: `Assets/Editor/HumanUiSelfCheck.cs` (new `RunDiplomacyViewChecks`, called from `RunChecks`)

**Interfaces:**
- Consumes: `DiplomacyState` (`Enabled`, `Get`, `StanceToward`, `Hostility`, `InTruce`, `IsAtWar`), `GameAIMap.Knowledge.ContactPlayers(map, id)`, `GameAIMap.GameAIConstants.hostilityMax`, `FleetStrength.Of` / `FleetStrength.Mine`, `WarshipStats`, Task 1's `Check`.
- Produces (namespace `FlatSpace.Human`):
  - `public struct DiplomacyButton { public bool Enabled; public string Reason; }`
  - `public class RivalRow { public int Rival; public string Label; public bool AtWar; public string State; }`
  - `public class FleetLine { public string Planet; public int Warships; public float Strength; }`
  - `public class OtherStance { public int Rival; public string Label; public Stance Stance; }`
  - `public class RivalView { int Rival; string Label; Stance MyStance, TheirStance; int MyChangeTurn, TheirChangeTurn (-1 if never changed); int TruceEndsTurn (0 = none); bool AtWar; string StateText; float Hostility, HostilityMax; List<OtherStance> Others; List<FleetLine> Fleets; int TotalWarships; float TotalStrength, MyStrength; DiplomacyButton EndWar, DeclareWar, Surrender, DemandSurrender; }`
  - `public static class HumanDiplomacyView { public static bool IsOn(GameAIMap map); public static string Label(int player); public static List<RivalRow> Rivals(GameAIMap map, int humanId); public static RivalView Build(GameAIMap map, int humanId, int rival, int turn, Func<string, bool> seesPlanet, WarshipStats stats, Func<string, int, string> refusal); public static string RefusalText(string reason, int truceEndsTurn); }`
  - `refusal(type, rival)` is the Check result (null = accepted); the screen's wiring passes `Check`.

- [ ] **Step 1: Write the failing tests** `RunDiplomacyViewChecks` on a `Fixture(4)` with `Diplomacy.Enabled = true`, rival 1 with 2 warships on P2, rival 2 with 1 warship on P3, rival 3 absent (no contact), human warships on P0:
  - `Rivals` returns ids `[1, 2]` only (rival 3 absent), `Label(1) == "Player 1"`; with 1 at war (`SetStance(0,1,War,3)`) the order is 1 then 2 and `Rivals[0].AtWar`; with nobody at war, ascending id.
  - `Rivals` is empty when `Diplomacy.Enabled` is false and `IsOn` is false.
  - `Build` for rival 1: `Others` has exactly one entry (rival 2) with `Stance` equal to `Diplomacy.StanceToward(1, 2)`, and no entry for rival 3 (unmet) or 0 (human) or 1 (itself).
  - `Hostility` equals `Diplomacy.Hostility(1, 0)`; `HostilityMax` equals `Constants.hostilityMax`.
  - Fleets: with `seesPlanet = n => n != "P2"` rival 1's `Fleets` is empty and `TotalWarships == 0`; with `n => true`, one line `P2`, `Warships == 2`, `Strength == FleetStrength.Of(P2 ships owned by 1, stats)` and `TotalStrength` equal to it; `MyStrength == FleetStrength.Mine(map, 0, stats)`. A docked ColonyShip of rival 1 on a seen planet adds no line.
  - States: Peace/Peace gives `StateText` "Peace", `AtWar` false; mine War gives "War"; mine Peace + theirs War gives `AtWar` true and `StateText` containing "still at war"; a truce (`TruceUntil` 30, turn 5) gives `TruceEndsTurn == 30` and a `StateText` containing "truce".
  - Buttons, with `refusal` wired to a real `Check`: Peace/Peace: `DeclareWar.Enabled`, `!EndWar.Enabled`, `!Surrender.Enabled` with Reason "Not at war"; mine War: `!DeclareWar.Enabled` Reason "Already at war", `EndWar.Enabled`, `Surrender.Enabled`; mine Peace + theirs War: `DeclareWar.Enabled`, `!EndWar.Enabled` with a Reason containing "Surrender", `Surrender.Enabled`; truce: `!DeclareWar.Enabled` with Reason containing "Truce", `!Surrender.Enabled`. `DemandSurrender.Enabled` is always false with Reason "Needs negotiation".
- [ ] **Step 2: Run** the self-check. Expected: compile failure (type missing), which the owner reads from the Console.
- [ ] **Step 3: Implement** `HumanDiplomacyView` per the signatures. Contact set = `ContactPlayers(map, humanId)`. `Rivals` orders war first (by `DiplomacyState.IsAtWar(humanId, r, true)`), then id. `RivalRow.State` is the short state ("War", "Peace", "Truce"). Fleets: for each `map.PlanetList` planet with `seesPlanet(name)`, warships = docked ships with `Kind == Ship.ShipKind.WarShip` and `Owner == rival`; add a line only if count > 0; order lines by strength descending. Button reasons come from `RefusalText(reason, truceEndsTurn)`: `AlreadyAtWar` "Already at war", `Truce` "Truce until turn N", `NoContact` "No contact", `DiplomacyOff` "Diplomacy is off", `NotAtWar` "Not at war", `AlreadyPeace` "Your stance is already Peace" and, when the rival's stance toward the human is War, " - they are still at war with you; Surrender ends it" (build that suffix in `Build`, since `RefusalText` has no stance), anything else the raw reason. A contact rival ids list is rebuilt on each call (no caching).
- [ ] **Step 4: Run** the Human UI Self-Check. Expected: ALL PASSED.
- [ ] **Step 5: Commit** (when asked): `HumanDiplomacyView.cs`, `HumanDiplomacyView.cs.meta`, `HumanUiSelfCheck.cs`; message "feat: pure Diplomacy screen builder".

### Task 3: `DiplomacyScreen`, the view

**Files:**
- Create: `Assets/Flatspace/UI/HumanUI/DiplomacyScreen.cs` (+ `.meta`)
- Modify: `Assets/Flatspace/Objects/Board/GameBoard.cs` (two public methods next to `BuildPlanetRows`, about line 396)

**Interfaces:**
- Consumes: Task 2's `HumanDiplomacyView`, `RivalRow`, `RivalView`; `HumanUiKit` (`Panel`, `Row`, `Header`, `Text`, `Btn`, `Cell`, `Toast`, `RefusalSummary`, colours); `Player.PlayerColors`; `Gameboard.SubmitHumanCommands`.
- Produces: `Gameboard.BuildDiplomacyRows() : List<RivalRow>` and `Gameboard.BuildRivalView(int rival) : RivalView` (null when no human or the rival is not a contact), both building the `Check` closure from a `HumanCommandProcessor(GameAI.GameAIMap, humanAI, TurnNumber)` and `new WarshipStats(humanAI.ResearchCatalog?.catalogItems)` as `PlayerAI` does, with `HumanSeesPlanet` as `seesPlanet`. `DiplomacyScreen` (static, namespace `Game.UI.MainGameScreenUI`): `IsOpen`, `Toggle(VisualElement host)`, `Close()`, `Refresh()`, `ContainsScreenPoint(Vector2)`, `ResetState()`.

- [ ] **Step 1: Implement the two `Gameboard` methods** (return empty / null without a human, like `BuildPlanetRows`).
- [ ] **Step 2: Implement `DiplomacyScreen`** following `PlanetsScreen` for `Build`/`Close`/`Refresh`/`ContainsScreenPoint`/`Toggle` (returns without a human). Layout: header row ("Diplomacy" + Close button); response area in the upper right of that row (a bordered box, fixed width about 280 px, dim "No messages yet"); body with a left column of rival rows (colour swatch from `PlayerColors`, label, state, selected row highlighted) and a right pane for the selected rival: Between us (my stance + turn, their stance + turn, effective `StateText`, hostility bar from `Hostility / HostilityMax`), Their stance toward others, Their fleets (table `Planet / Warships / Strength`, a Total row beside "Mine: <MyStrength>"), then the four buttons. Disabled buttons use `SetEnabled(false)` with `tooltip = Reason`, and the reason is also shown as small dim text beside the button (a tooltip alone is invisible on a controller-less HUD). Selection is a static `int _selected`; when it is no longer in the rows, it falls back to the first row, or -1. Empty list shows "No rivals met yet". `!HumanDiplomacyView.IsOn` shows "Diplomacy is switched off in this match" and no rows.
- [ ] **Step 3: Confirmation strip.** Clicking Declare war / End the war / Surrender replaces the button row with one line of text and Confirm / Cancel buttons (no modal; `_pending` string). Texts: Declare war: "Declare war on Player n?"; End the war: "Set your stance toward Player n to Peace? They may still be at war with you."; Surrender: "Surrender to Player n? Both sides go to Peace and a truce of 30 turns starts: nobody can declare war. Nothing else changes." Read the 30 from `GameAIConstants.surrenderTruceTurns` through a `Gameboard` accessor rather than hard-coding it. Confirm sends `new HumanCommand { type = "declareWar" | "makePeace" | "surrender", rival = id }` through `Gameboard.Instance.SubmitHumanCommands(new[] { command }, "Diplomacy")`, shows `HumanUiKit.Toast(host, HumanUiKit.RefusalSummary(results))` when any is refused, clears `_pending` and calls `Refresh()`. Changing the selected rival or `Refresh()` after a turn clears `_pending`.
- [ ] **Step 4: Run** (owner): focus the Editor, confirm the Console has no compile error. Visual checks come in Task 4 once the button exists.
- [ ] **Step 5: Commit** (when asked): `DiplomacyScreen.cs`, its `.meta`, `GameBoard.cs`; message "feat: Diplomacy screen view".

### Task 4: Wiring, docs and the Play-mode check

**Files:**
- Modify: `Assets/Flatspace/UI/MainGameScreenUI/MainScreenUIController.cs` (button at `top = 148`, `SetStatus` show/hide/refresh, `ToggleDiplomacy()`)
- Modify: `Assets/Flatspace/Objects/Board/GameBoard.cs` (`Update` T key, click-inside rule at line 116, `DiplomacyScreen.Close()` + `ResetState()` beside the `PlanetsScreen` lines at about 713)
- Modify: `Assets/Flatspace/UI/MainGameScreenUI/GameButtonHandler.cs` (`EscapeButtonPressed` closes the screen first)
- Modify: `CLAUDE.md` (Human UI stage 3 section: the Diplomacy screen), `FUTURE_FEATURES.md` ("Remaining of stage 3": diplomacy screen built; project 2 ask-and-trade and the Qwen text entry stay), `.claude/skills/tuning-log/SKILL.md` unchanged.

**Interfaces:** Consumes Task 3's `DiplomacyScreen` and `Gameboard.TogglePlanets`'s pattern. Produces `MainScreenUIController.ToggleDiplomacy()` and `Gameboard.ToggleDiplomacy()`.

- [ ] **Step 1: Button and refresh.** Add `_diplomacyButton` "Diplomacy" (position absolute, `top = 148`, `right = 10`, hidden unless a human plays, `HumanUiKit.StyleButton`) and extend `SetStatus` exactly like the Planets button: show when a human plays, else `DiplomacyScreen.Close()`; refresh when open.
- [ ] **Step 2: T key.** In `Gameboard.Update`, beside the P key: `Keyboard.current.tKey.wasPressedThisFrame` with a human playing, the screen closed and `!PlanetsScreen.AnyTextFieldFocused()` calls `ToggleDiplomacy()`. (D is taken by map scrolling.)
- [ ] **Step 3: Click and Esc rules.** Add `|| DiplomacyScreen.ContainsScreenPoint(mousePosition)` to the line-116 condition; in `EscapeButtonPressed` close the Diplomacy screen first (`if (DiplomacyScreen.IsOpen) { DiplomacyScreen.Close(); return; }`); add `DiplomacyScreen.Close(); DiplomacyScreen.ResetState();` next to the `PlanetsScreen` pair in the match-start reset.
- [ ] **Step 4: Run** `Run All AI Self-Checks` and `Run Human UI Self-Check`. Expected: ALL PASSED.
- [ ] **Step 5: Play-mode check** (owner, re-enter Play mode): with a human match on a board where a rival has been met, confirm: the button and T open the screen, Esc and Close shut it; a rival never met is absent; the list is war first; both stances and "changed on turn" read right; hostility bar moves across turns; the others block lists only met rivals; fleet lines appear only for planets in sight with warship counts and strengths, the total beside "Mine"; Declare war confirms then flips the stance and the list state; End the war confirms and, against a rival still at war, shows "still at war"; Surrender confirms with the 30-turn text, ends the war, and Declare war then reads "Truce until turn N"; Demand surrender is greyed with "Needs negotiation"; the response area is empty upper right; loading a save with the screen open closes it.
- [ ] **Step 6: Docs and commit** (when asked): add a "Diplomacy screen (stage 3)" bullet to the stage 3 section of `CLAUDE.md` (builder, `Check` extension, T key, Surrender and truce, inactive Demand surrender, response area), update `FUTURE_FEATURES.md`, then commit all Task 4 files by explicit path.

## Tuning log

No new log lines. `GameAI.ApplyStanceOrder` / `ApplySurrender` already write `Stance` and `Surrender` lines, and `SubmitHumanCommands` already writes `HumanCommand`, `HumanCommandRefused` and the `HumanUiOrder` line (screen label "Diplomacy"). No change to the `tuning-log` skill or the "AI Tuning Log" section.

## Self-review

- Spec coverage: list and no-entry rule (Task 2, 3); between-us stances, last change, truce, hostility (Task 2, 3); others (Task 2); fleets and total vs mine (Task 2); four buttons, confirmations, Surrender text (Task 2, 3); response area (Task 3); Esc, button, key (Task 4). Score and ask-and-trade are non-goals.
- My calls: hotkey T; the `Check` extension is the single source of button states; the legacy-off message; the confirmation is an inline strip, not a modal; disabled reasons also shown as text.
