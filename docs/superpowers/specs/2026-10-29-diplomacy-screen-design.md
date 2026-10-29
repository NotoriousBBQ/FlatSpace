# Diplomacy screen (human UI, stage 3)

Date: 2026-10-29. Project 1 of 2. Project 2 (ask and trade: asking a rival to declare war or make peace on a third party, offering and
demanding planets, fleets and tech, the acceptance meter, the AI's answers) is a separate design that comes later.
Background: `CLAUDE.md` (Human player mode, the human UI stages) and `Assets/Flatspace/GameAI/CLAUDE.md` (Diplomacy). Roadmap entry:
"Remaining of stage 3" in `FUTURE_FEATURES.md`.

## Intent

The human player has no view of the diplomatic situation and no way to change a stance except the raw `declareWar` / `makePeace` / `surrender`
commands. This screen shows, for every rival the human has met, the state between them, what that rival's relations are, how
hostile it is toward the human and how strong its visible fleets are, and lets the human declare war or end a war with it.

## Goals

- Open from a HUD Diplomacy button and a hotkey (T; D is taken by map scrolling; same pattern as the Planets screen: Esc or the button closes it).
- A list of the rivals the human has contact with (`PlayerKnowledge.ContactPlayers`), each with its current state (War or Peace).
  **A rival the human has not met has no entry anywhere**: not in the list, and not in any rival's "stance toward others" block.
- For the picked rival, show:
  - **Between us:** the human's stance toward the rival and the rival's toward the human (Peace or War), each with the turn it last changed;
    the effective state (derived: `DiplomacyState.IsAtWar`), with plain text when it differs from the stances (for example mine Peace,
    theirs War: "still at war"); a truce when one is running (`TruceUntil`).
  - **Their hostility toward me:** the rival's own hostility score toward the human (`DiplomacyState.Hostility(rival, human)`, 0 to
    `hostilityMax`) as a bar. The owner asked for the real score.
  - **Their stance toward others I know:** the rival's stance toward each other rival the human has met. Not the human, not anyone unmet.
  - **Their fleets on planets I can see:** per planet the human currently sees, the rival's docked warship count and a strength number
    (`FleetStrength.Of`), a total, and the human's own total strength beside it.
- Buttons: **End the war** when the human's own stance is War; **Declare war** when it is Peace; **Surrender** when the human is
  effectively at war with the rival (`DiplomacyState.IsAtWar`); and **Demand surrender**, which is **visible but inactive** (greyed, with a
  short "needs negotiation" hint) until project 2 gives it behaviour. The first three use the existing `declareWar` / `makePeace` /
  `surrender` commands through `Gameboard.SubmitHumanCommands`. A refused command shows its reason.
- **Surrender** (existing `surrender` command, `GameAI.ApplySurrender`): both stances go to Peace and both pair rows get a truce of
  `surrenderTruceTurns` (30) turns, during which the pair is not at war and nobody can declare. It costs nothing else and needs no acceptance,
  so it is the one button that ends a war for certain. Its confirmation says so and names the 30 turns. Hostility is not reset.
- A **response text area** in the upper right of the screen. It stays empty at first (a dim "No messages yet"). It is where the
  AI's replies to proposals and demands will appear in project 2.

## Non-goals

- No ask-and-trade controls in this project: no "ask to declare war on...", no offer or demand dialog, no acceptance meter. The one
  exception is the inactive **Demand surrender** button above, a placeholder with no behaviour.
- No score display (no scoring system exists yet; a later feature adds a place for it).
- The response text area is never filled here. Generating its text with a small local model (Qwen2.5 3B) is recorded in `FUTURE_FEATURES.md`.

## Anti-goals

- Never show a rival's private view of an unmet player, and never show ship-by-ship detail of a rival's fleet. A rival's fleet shows
  ship count and one strength number only (the owner confirmed this, 2026-10-29), consistent with `HumanHoverText.ShowFullFleetDetail`.
- The screen must not write simulation state itself. Every change goes through a command.
- Ending a war must not pretend to succeed: see "End the war" below.

## Decisions (the owner's)

- **End the war changes only the human's own stance.** Effective war stays while the rival's stance toward the human is War, so the
  button may not end the war. The screen says so ("Peace (mine), War (theirs): still at war"). Making peace depend on the rival
  accepting belongs to project 2.
- The strength number sits next to the ship count for each planet and in a total against the human's own total.
- The response text area is in the upper right.

## Constraints and facts the builder needs

- `HumanCommandProcessor.HandleStance` (`Assets/Flatspace/Player/Human/HumanCommandProcessor.cs`, "Diplomacy"): `declareWar` refuses
  AlreadyAtWar, Truce, NoContact (or DiplomacyOff, UnknownRival); `makePeace` refuses AlreadyPeace, which fires when the human's own stance is
  already Peace, even if the rival's is War. So the End the war button shows only when the human's own stance is War.
  `HumanCommandProcessor.Check` supports other commands; use it for these two if it can, else disable by the same conditions.
- Effective war: `DiplomacyState.IsAtWar(me, rival, meHasContactWithRival)`. Stances, hostility, last change: `DiplomacyState.Get`,
  `StanceToward`, `Hostility`, `TurnsSinceChange`, `InTruce`. The AI's `UpdateDiplomacy` runs at the end of `ProcessResults` and decides a
  stance only once `stanceHoldTurns` (10) have passed since its last change.
- **Corrected after Play testing (2026-10-29):** "planets I can see" means the planets whose docked ships the board shows: in sight, or next to a planet the human occupies or has a ship at (`Gameboard.HumanOwnerVisible`, the Planets screen's Owner/Ships rule). The first version used only `HumanSeesPlanet` and missed a rival's ships on an adjacent dimmed planet.
- (Superseded) "Planets I can see" means the human's fog view (`Gameboard.HumanSeesPlanet`), the same rule as the Planets screen, **not**
  `FleetStrength.VisibleOf`, which uses the AI's wider `Knowledge.KnownPlanets` (up to 6 path nodes). Strength per planet is
  `FleetStrength.Of(planet.DockedShips.Where(owner == rival), stats)`; the human's own total is `FleetStrength.Mine`.
- Pure logic goes in a builder class in `Assets/Flatspace/Player/Human/` (never touches `Gameboard.Instance`), with its self-check added
  to `Assets/Editor/HumanUiSelfCheck.cs`; the view goes in `Assets/Flatspace/UI/HumanUI/` (UI Toolkit, built in code, `HumanUiKit` dark
  theme). Follow `PlanetsScreen` / `FleetSummaryScreen` for opening, closing, Esc and the HUD button. With no human playing, there is no screen.
- Files are CRLF: no `sed -i`. Stage by explicit path with `.meta` files.

## Left to the builder

- The screen's exact layout and sizes, and the list's ordering (suggestion: War first, then by player id).
- Whether the screen refreshes when a turn ends while it is open (suggestion: yes).
- The wording of the plain-text states and of refusal reasons.

## My calls (not the owner's; flagged for review)

- **Declare war asks for one confirming click** (it is hard to take back: the pair's stance is held for 10 turns for the AI's own decisions,
  and a truce can block a later declaration). **End the war asks for one too (the owner's decision, 2026-10-29).**
- **Surrender and the inactive Demand surrender are in this project** (the owner's decision, 2026-10-29, after I explained what surrender does).
  Demand surrender's design (what it asks, how the AI answers) is project 2.
- HUD Diplomacy button and a hotkey; D is taken by map scrolling (WASD), so the plan uses T.
- **A war the human declared stays listed after contact is lost** (the owner's decision, 2026-10-29, after review): the list is the contact players plus any rival the human's own stance is War toward, so the way to end the war never disappears. A player never met, or at peace and out of contact, is not listed.
- **Button states are shown disabled with the refusal reason, never hidden** (reviewer question, my call): Declare war is disabled with
  "Already at war" (the human's own stance is already War), "Truce" or "No contact"; with mine Peace and theirs War it stays enabled (`HandleStance` tests only the human's own stance), and End the war is disabled with a hint that Surrender ends it; End the war is enabled whenever the human's own stance is War
  (`makePeace` has no truce refusal; read in `HandleStance`). The pure builder returns a button state plus a reason; use
  `HumanCommandProcessor.Check` if it can take `declareWar` / `makePeace`, else derive the state from the same conditions.
- After `declareWar` / `makePeace` the screen re-reads state at once (commands run between turns through `SubmitHumanCommands`,
  which refreshes the panels).
- "Other rivals I know" in the third-party block is exactly the human's `ContactPlayers` minus the picked rival: the same set as the list,
  so nothing about an unmet player can appear (fog-of-war rule).
- Rivals are labelled "Player n" through `Gameboard.OwnerLabel` (no player-name system exists), in the same colour as their planets on the board.


## Tuning log

No new log lines. The stance orders already log `Stance` lines through `GameAI.ApplyStanceOrder`, and `HumanCommand` / `HumanCommandRefused`
already log the human's commands. The plan states this.
