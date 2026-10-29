# Handoff: Diplomacy screen

Saved from the Claude Code session named **"diplomacy_screen"**, 2026-10-29. This file is the written version so nothing depends on that history.

Read `CLAUDE.md` first, especially the new "The human UI (project 2, stage 3: the Diplomacy screen)" section. The design is
`docs/superpowers/specs/2026-10-29-diplomacy-screen-design.md` and the plan is `docs/superpowers/plans/2026-10-29-diplomacy-screen.md`.

## Where things stand

The session opened with "diplomacy screen" (the recommended next piece of stage 3 from the `pop_assign` handoff). After a brainstorm with mockups it was split into two
projects: **project 1, the screen** (built, committed, merged to `main`) and **project 2, ask and trade** (not designed, recorded in `FUTURE_FEATURES.md`).

What the player sees (HUD Diplomacy button or the T key; Esc or Close shuts it): the rivals met on the left (war first), the picked rival on the right: both stances with the turn
each changed, the effective state ("Peace (mine), War (theirs): still at war", or a truce), the rival's hostility toward the human as a bar, its stance toward the other rivals met,
and its warships on the planets whose ships the board shows (a count and one strength number per planet, a total beside the human's own). Buttons: Declare war, End the war, Surrender
(each behind an inline confirmation; Surrender's text names the 30-turn truce) and a visible but inactive Demand surrender. An empty response area sits in the upper right.
Also built at the owner's request near the end: the fleet icon hover popup now ends with the fleet's strength.

| Area | What it does | Key code |
|---|---|---|
| Pure builder | rows (war first), rival view, button states, fleet lines, others block | `Assets/Flatspace/Player/Human/HumanDiplomacyView.cs` (`Known`, `Rivals`, `Build`, `RefusalText`) |
| View | list, detail pane, confirm strip, response area | `Assets/Flatspace/UI/HumanUI/DiplomacyScreen.cs` |
| Button states | `HumanCommandProcessor.Check` now covers `declareWar`, `makePeace`, `surrender` (dry run), so the screen never offers a refused action | `HumanCommandProcessor.cs` |
| Board access | `BuildDiplomacyRows`, `BuildRivalView`, `SurrenderTruceTurns`, `FleetStrengthOf`, `ToggleDiplomacy`, the T key, the click-outside rule, reset at match start | `GameBoard.cs` |
| HUD / Esc | Diplomacy button at top = 148, Esc closes the screen first | `MainScreenUIController.cs`, `GameButtonHandler.cs` |
| Hover popup | `, strength N` after the fleet text (none for a fleet with no warships) | `HumanHoverText.WithStrength`, `FleetIconHandle.PopupText` |

### Verified

- The owner ran `Run Human UI Self-Check` and `Run All AI Self-Checks` after the screen was built (before the later changes listed under "Not verified"): all passed. The new suites are
  `HumanUiSelfCheck.RunDiplomacyCheckChecks` and `RunDiplomacyViewChecks`.
- The owner played it in Play mode: "everything works" except the one problem below.
- A fresh Opus reviewer read the whole branch before the later changes: no compile errors, both new self-checks traced as passing; two findings fixed (stale `Diplomacy.Turn`, a confirmation
  surviving a turn change); two more went to the owner (both answered, below).

### Not verified

Changes made after the owner's last full Play test, with no report back from the owner before the commit was asked for:
- **Keep a declared war listed after contact is lost** (`HumanDiplomacyView.Known`, new assertions in `RunDiplomacyViewChecks`). The owner reported the self-checks passing after this one, so it is likely covered by that run, but it was not played.
- **The adjacent-planet fix:** fleet lines now use `Gameboard.HumanOwnerVisible()`, not `HumanSeesPlanet`. The owner's save `Assets/Flatspace/GameSaves/DiplomacyView.json` showed rival 1's ships on two dimmed planets beside the human's, missing from the screen. Not re-tested by the owner.
- **The hover popup strength** (two new assertions in `RunHoverTextChecks`). Not run, not looked at in Play mode.
Update, same day: the owner re-ran the self-checks after all three changes and they passed. Still not looked at in Play mode: the adjacent-planet fleet lines (reload `DiplomacyView.json`, rival 1 should show two lines of one ship each) and the hover popup strength (hover a fleet icon).

## Files

Committed on `main` (nothing pushed):
- `37cac91` docs: design for the Diplomacy screen (the spec).
- `bf190ca` feat: Diplomacy screen (human UI stage 3) and fleet strength in the fleet hover popup, merged as `a475b6d` (branch `diplomacy-screen`, now deleted).
  - New: `Assets/Flatspace/Player/Human/HumanDiplomacyView.cs`, `Assets/Flatspace/UI/HumanUI/DiplomacyScreen.cs` (each with its `.meta`), the plan.
  - Changed: `Assets/Editor/HumanUiSelfCheck.cs`, `Assets/Flatspace/Objects/Board/GameBoard.cs`, `Assets/Flatspace/Player/Human/HumanCommandProcessor.cs`, `HumanHoverText.cs`,
    `Assets/Flatspace/UI/FleetIconHandle.cs`, `Assets/Flatspace/UI/MainGameScreenUI/GameButtonHandler.cs`, `MainScreenUIController.cs`, `CLAUDE.md`, `FUTURE_FEATURES.md` (the Qwen entry, wording).

Uncommitted (written at the close-out):
- `FUTURE_FEATURES.md`: the "Stage 3, the Diplomacy screen (project 1) moved to completed_features on 2026-10-29" line, the "Remaining of stage 3" wording, and the new "Diplomacy, project 2: ask and trade" entry.
- `completed_features.md`: the new Diplomacy screen sub feature under "Human player mode, project 2: UI rework".
- `docs/handoffs/HANDOFF-diplomacy-screen-2026-10-29.md` (this file).

## Decisions and why

- **Split in two** (owner): the screen first with the ask-and-trade controls hidden, because the negotiation rules are a design of their own. The owner: "the negotiation system is going to be difficult enough without starting on it here".
- **End the war changes only the human's own stance** (owner): it may not end the war while the rival's stance toward the human is War; the screen says "still at war". Surrender is the sure way (both stances to Peace, a `surrenderTruceTurns` (30) truce, no acceptance needed, nothing else changes, hostility not reset).
- **Confirmations** on Declare war and End the war (owner's request for End the war) and on Surrender (owner approved adding Surrender; the confirmation text naming the truce is my call). Demand surrender: visible and inactive (owner).
- **A rival not met appears nowhere** (owner); **a war the human declared stays listed after contact is lost** (owner, after the reviewer found the way to end it vanished). The rival's hostility toward the human is shown as a bar with the real score (owner).
- **Fleet strength** next to the ship count, no ship detail for a rival (owner). Siegeships are not counted in the fleet lines (warships only, as the spec says); the owner was told and did not ask for a change.
- **For project 2:** the acceptance meter is a coarse reading (Very unlikely ... Very likely), not a number (owner). Give/get lists use dropdowns (owner). Scores on the screen come with a scoring system (owner).
- **Response area upper right, empty** (owner); its text may later come from a local Qwen2.5 3B model (entry in `FUTURE_FEATURES.md`; the text must follow the AI's real decision, never drive it).
- **My calls:** the T key (D is map scrolling); disabled buttons show their reason as text under them; with diplomacy off the screen says so and lists nobody; `HumanDiplomacyView.Build` has no `turn` parameter and reads `DiplomacyState.Turn`, which the two `Gameboard` builders refresh (a load leaves it at 0); the confirmation lapses on a new turn; a corrected spec line: `declareWar` is refused `AlreadyAtWar` on the human's own stance only, so mine Peace and theirs War leaves Declare war enabled.

## Open items and next steps

1. **Look at the adjacent-planet fleet lines and the hover popup strength in Play mode** (see "Not verified"; the self-checks already pass). The close-out files are committed and pushed.
2. **Diplomacy, project 2: ask and trade** (recommended next design; brainstorm first): asking a rival to declare war or make peace on a third party, offers and demands of planets, fleets and tech (dropdowns), the coarse acceptance meter, the AI's answers in the response area, peace and Demand surrender depending on acceptance. Needs an AI scoring of proposals that also drives its real answer. Entry in `FUTURE_FEATURES.md`.
3. **Rest of stage 3:** top bar, Esc/system menu, clickable board icons, pause settings, base-output display, the debug mode, the colony ship `move` bug.
4. **Remind the owner** (memory note): planet ownership indicators; the fleet hover popup showing too much on dimmed planets (the strength just added to it is one number, no detail); re-verify the Blockaded-notification fix once shipment automation has a UI.
5. **Deferred minors:** the Diplomacy and Planets screens can both be open and stack (Esc closes Diplomacy first even when Planets is on top); the carried minors of the `pop_assign` handoff still stand.
6. **Strategy picker** and the optional tuning-log narration script from the previous handoff remain open.

## Gotchas

- Unity cannot run from the CLI: every "run" is the owner focusing the Editor and running the menu self-checks; Play-mode wiring needs exit and re-enter Play mode.
- **Never use `sed -i` on the repo's files** (CRLF); the session used an exact-anchor Python replace that keeps CRLF, and converted new `.cs` files to CRLF.
- `Diplomacy.Turn` is only set by `GameAI` at the start of a turn (and now by the two `Gameboard` builders): a stale 0 after a load makes `IsAtWar` and `InTruce` wrong. `HandleStance`'s own surrender test has the same exposure when called before any turn; the screen refreshes it first.
- `HumanModeSelfCheck.Fixture` leaves `Diplomacy.Enabled` false; a diplomacy self-check must switch it on and set `Diplomacy.Turn`.
- `HumanUiKit` is `internal`; a public screen may use it only inside method bodies, never in a signature. A method named `Action` clashed with `System.Action` (renamed `ActionButton`).
- The "visible" rule for fleets is the board's (`HumanOwnerVisible`: in sight or beside a planet the human occupies or has a ship at), not `HumanSeesPlanet` and not `FleetStrength.VisibleOf`.
- **The owner's standing rules:** commit and push only when asked; give a recommendation with every choice; use they/them when pronouns are unstated.
