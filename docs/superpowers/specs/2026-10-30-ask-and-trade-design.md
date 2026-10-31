# Diplomacy project 2: ask and trade, and the player score

Date 2026-10-30. Roadmap entry: "Diplomacy, project 2: ask and trade" in `FUTURE_FEATURES.md`. Builds on the Diplomacy screen (`CLAUDE.md`, "The human UI ... the Diplomacy screen"; spec `docs/superpowers/specs/2026-10-29-diplomacy-screen-design.md`).
Decisions below are the owner's unless marked **(my call)**.

## Intent

The human can talk to a rival and make deals: end a war, buy peace with gifts, trade planets, fleets and tech, and ask a rival to start or end a war with a third player. A rival answers from the same values the game uses everywhere, so the answer is never arbitrary and the human can see roughly why. The value of things also gives every player a **score**.

One shared valuation (what a planet, a fleet and a tech are worth) drives three things: the player score, the rival's accept/refuse decision, and the coarse acceptance meter. The text a local model writes in the response area follows the decision and never drives it.

## Goals

1. A single valuation of planets, fleets and tech, and a player score = the total value of everything a player owns.
2. A proposal dialog on the Diplomacy screen for the picked rival, with "I give" and "I get" lists, a coarse acceptance meter and the rival's reply.
3. Peace can be bought with gifts. Ending a war is priced by what the war costs the rival and by its hostility. A rival that is winning prices peace high but never refuses outright.
4. Third-party asks: start a war with, or end a war with, another player, as items in the same lists.
5. Lopsided deals move the rival's hostility toward the proposer.
6. Instant, in-place transfers. No new order types, no delivery.

## Non-goals and anti-goals

- Rivals never propose to the human, and the AI never proposes to another AI (human-initiated only). Not building: counter-offers, haggling rounds, alliances, treaties that bind future stance changes, tribute over time.
- Not expanding the chatbot prompts: feeding the model the deal valuation, the hostility change and similar facts is a **separate, later task** (owner), to follow once this first pass is done. This project keeps the existing chat path and adds only the template reply.
- Not a new combat or stance model. Fleet strength stays the measure for combat decisions (retreat, surrender, assault); it is not the score.
- No delivery mechanics: a fleet does not fly to the receiver, a tech does not travel.
- Anti-goal: the meter or the reply text disagreeing with the real decision. Anti-goal: gift spam erasing a war in one turn (hence the per-deal hostility cap). Anti-goal: a rival refusing a price that no offer could meet (a rival that is winning must still be buyable).

## The valuation

All in one pure class (no `Gameboard.Instance`, so a self-check can drive it).

- **Planet:** `PlanetResourceData._planetValue` per type, **built on branch `player-score`** (`Planet.PlanetValue`): Prime 10, Verdant 6, Desolate 6, Industrial 5, Farm 4, Ocean 4, Desert 4, Normal 2. Authored in the nine type assets, retunable there. (`_producerValue` is a different, older number and is not used here.)
- **Fleet:** each ship's production cost scaled by its current health, divided by 50 (a new warship is worth 2). The cost is `WarshipCosts`' formula evaluated for the research tiers the ship actually carries (its `ResearchSnapshot`), not the owner's current research count. Siegeships and bunkers use their own subtype cost; a colony ship its catalog cost. Fleet strength is not used.
- **Tech:** the research item's cost divided by `GameAIConstants.techValueDivisor` (new, **2000**). Research costs were doubled on 2026-10-30 (`Research Catalog.json`, 300 to 30,000 per item; the whole tree is about 790 points). If research costs change again, retune the divisor with them. Each researched item counts once.
- **Player score** = sum of planet values (planets it owns) + fleet values (its ships, bunkers excluded or included: **(my call)** include them, they cost industry) + tech values (items researched). Shown on the Diplomacy screen for the human and for each rival met **(my call)**.

## The proposal

### Items

Two lists, **I give** and **I get**. An **Add** menu offers: Planet..., Fleet..., Tech..., End the war with them, Start war with... (a third player), End war with... (a third player).

- Each side may offer only what it owns: a planet it owns, ships it owns, a tech it has researched that the receiver has not.
- "End the war with them" adds a **linked pair** of rows, "I end the war" under I give and "They end the war" under I get; removing either removes both. It exists only while the pair is at war.
- "Start war with P3" / "End war with P3" go only in the list they are added to. In I give they are a promise by the human; in I get they are an ask of the rival. They need P3 to be a player both have met **(my call)**.
- What the human may pick under I get is what the board already shows the human (planets it knows, fleets it can see, tech the rival has researched that the human lacks). **(my call; whether a rival's researched tech list should be hidden is left to the builder.)**
- A tech can be given only when its `requiredTech` is already researched by the receiver **(my call)**, so the catalog never holds an orphan.

### Acceptance

For the rival: `net = value of what it receives - value of what it gives + peace worth` (peace worth only when an "end the war" pair is in the deal).

- **Peace worth** to the rival = what the war is costing it (its recent losses to the proposer, valued with the same values, over `lossWindowTurns`) minus a penalty that grows with its hostility toward the proposer. It can be negative: a rival that is winning wants payment. There is no outright refusal; the price is the net.
- **Third-party items:** "Start war with P3" is worth more to the rival the more it already dislikes P3 and the weaker P3 is; ending a war it is winning is worth less. **(the exact terms are left to the builder; reuse the rival's own stance weighting toward P3 from `StanceMatrix` as the signal.)**
- **Decision: a plain threshold, `net >= 0` accepts (my call: deterministic, so the meter is honest).**
- **Meter:** five bands from the net, Very unlikely / Unlikely / Uncertain / Likely / Very likely, from the same `net` and a scale constant. The side panel lists the factors as text only, **green if the factor makes the deal more acceptable, red if less, no numbers on the lines**. The meter and the lines update as items are added or removed.

### Hostility from the deal

When a rival **accepts**, its hostility toward the proposer changes by `delta = -dealHostilityPerValue x (value of the tangible items it received - value of the tangible items it gave)`, clamped to `+-dealHostilityCap` per deal. **The sign is negative**: a rival that came out ahead (received more than it gave) gets a lower hostility, i.e. likes the proposer more; one that accepted a deal worse than even for it gets a higher hostility. **Tangible items only** (planets, fleets, tech): stance items, the peace worth and the hostility penalty decide whether it accepts but do not count as "favour" **(my call)**. The peace worth is computed from the hostility before the deal; the change applies when the deal executes. Defaults `dealHostilityPerValue` 1, `dealHostilityCap` 10 (hostility runs 0 to 100), both on `GameAIConstants`. The cap is the owner's decision. A refused proposal changes nothing.

### Executing an accepted deal (instant)

- **Planet:** convert one inhabitant at a time from the giver to the receiver until `Planet.PlayerWithMostPopulation()` returns the receiver, and no further (the minimum; ownership is a plurality, a tie leaves the planet unowned). Uses `Planet.ConvertInhabitant`: the production queue is cleared, improvements and stocks stay, the planet takes the new owner's manual/automatic default. The giver's remaining inhabitants and ships stay, as after a conquest. Bunkers stay with their owner.
- **Fleet:** the named ships change `Ship.Owner` where they sit (and join the receiver's fleet numbering).
- **Tech:** the receiver's matching catalog item is marked researched (production items with that `requiredTech` follow, as completed research does).
- **Stance items:** applied through the existing stance orders (`GameAI.ApplyStanceOrder`), instant. "End the war" sets both stances to Peace. **A bought peace starts a truce of `dealTruceTurns` (new on `GameAIConstants`, default 20, the owner's decision; Surrender's is `surrenderTruceTurns` 30)**, stamped on both pair rows like `GameAI.ApplySurrender` does, so the AI's stance matrix cannot re-declare the war during it; third-party items (start/end war with P3) change only the named player's stance and nothing prevents a later change.
- Items are checked again at execution; a deal whose item is no longer valid (a planet lost this turn) is refused with a reason, nothing applies (all or nothing).

## Limits and rules settled in review (my calls unless marked)

- **What a player may give away:** never its last populated planet, and never its capital (home planet), for the human or a rival. Otherwise valuation is symmetric, so a human who pays full value can buy a rival's planets in fair trades; that is intended.
- **War cost** counts what the rival lost **to the proposer** within `lossWindowTurns`: ships (valued with the new fleet value, not strength) and planets taken by conversion, using the attribution the loss and conversion records already carry. The peace worth is clamped from below at `-peacePriceCapFraction` (tunable, start 0.5) of the rival's own score, so a rival that is winning prices peace high but finitely; a self-check proves that offering a winning rival its own price is accepted (that is the meaning of "buyable").
- **Reply text:** the decision applies at once and never waits for the model. A deterministic template line for the verdict shows immediately; the model's text replaces it when it arrives (async, with the existing timeout). If no model runs, the template stays.
- **Fleets:** only ships docked at a planet can be given or asked for (ships in flight are orders, not `Ship` objects). A rival's fleet in the I get list obeys the existing rule (`HumanHoverText.ShowFullFleetDetail`): the human sees a ship count and strength, not the ship detail.
- **Scores:** a rival met has its score shown (the owner's roadmap entry asks for rivals' scores); the leak of its fleet and tech totals is accepted. The score is not used at match end in this project (the turn-limit popup still names no winner; scoring at match end is a later feature).
- **Tech:** a gifted tech only flips the receiver's `researched` flag (no research progress or cost credit). The I get list shows the rival's researched techs that the human lacks; one whose `requiredTech` the human lacks is listed greyed out with its reason.

## What exists and what is new

- **Reuse:** `HumanCommand` / `HumanCommandProcessor` (a new `propose` command with `Check` as the dry run that returns the verdict band and the factor lines, and `Apply` that executes), `Gameboard.SubmitHumanCommands`, `DiplomacyScreen` and `HumanDiplomacyView` (the dialog is a new view opened from the screen, shown through the existing dialog mechanism), `DiplomacyState` hostility, `PlayerAI.RecordLosses`/`LossShareToward`, `WarshipCosts`/`WarshipStats`, `StanceMatrix`, `Planet.ConvertInhabitant`, `AITuningLogger`, the chat reply path (`ChatClient`; the reply text is generated after the decision from the verdict).
- **New:** the pure valuation class and its self-check, `techValueDivisor`, `dealHostilityPerValue`, `dealHostilityCap`, a meter scale constant, the proposal view, the `propose` command, recording the value of what a player lost to a rival within the loss window (the war-cost term), the score line on the Diplomacy screen.
- **Layout:** terms on the left (the two lists with the Add menu), a "How they see it" panel on the right (meter, colored factor lines, the reply), Propose and Cancel under the terms. Mockups were reviewed in the brainstorm (layout B, revised).

## Phasing (the order the owner agreed)

1. Valuation and score (including the score line on the Diplomacy screen).
2. The proposal dialog with peace and gifts (planets, fleets, tech) and the hostility effect.
3. Third-party asks (start/end war with another player).

## Saves, log and checks

- **Saves:** nothing new is needed if the value of recent losses is not persisted (a load restarts that window, like the other log-only and window state). Planet values live in assets; no new save field is planned. The builder confirms.
- **Tuning log** (proposed, each one-off per proposal so no tracker is needed): `Proposal|<rival>|<terms>|<net>|<band>|<accepted>` and `DealHostility|<rival>|<delta>|<capped>`, plus the `tuning-log` skill and "AI Tuning Log" updates.
- **Self-checks:** the valuation class, the net and band mapping, the minimum conversion for a planet gift (the 5/2, 5/4 and 1-inhabitant cases), execution of each item kind, the all-or-nothing refusal, and the hostility cap. Added to `Run All AI Self-Checks` as a new suite.

## Left to the builder

The exact war-cost bookkeeping, the third-party item terms, the meter scale and band edges, the factor wording, fleet-item granularity in the dialog (a fleet id at a planet versus a ship count per kind), and the class and file layout.
