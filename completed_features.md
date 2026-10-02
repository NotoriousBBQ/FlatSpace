# Completed Features

Items moved here from [`FUTURE_FEATURES.md`](FUTURE_FEATURES.md). Each entry carries the date it was moved.

## Done

- **Blockade planet with docked ships** — ships docked at a planet can blockade it. *Done: docked offense blockades colony ships and food/grotsits shipments; see CLAUDE.md "Warships and Blockade".* *(moved 2026-10-01)*

## Partially done

Only the sub features marked done are listed here; the unfinished remainder stays in `FUTURE_FEATURES.md`.

- **AIStrategyConsolidate** — a strategy phase the AI enters after first contact with another player, focused on fleet build, strategic colonization, and planet defense.
  - First cycle done: the first-contact trigger and Expand → Consolidate switch (Consolidate plays like Expand for now). *(moved 2026-10-01)*
  - Second cycle done: outer-only garrisons and assault targeting under Consolidate. *(moved 2026-10-01)*
  - Third cycle done: Consolidate's Warship production weight scales with the fleet shortfall. *(moved 2026-10-01)*
- **AI avoids blockaded routes and uses blockades**
  - (1) Colonization done: blockaded planets build no colony ships and do not colonize; colonization routes avoid visible blockaded planets and cancel when none exists; blockaded planets get a tunable warship/upgrade boost (`blockadedWarshipBoost`); see CLAUDE.md "Warships and Blockade". *(moved 2026-10-01)*
  - (2) Resource shipping — done: shipments route around visible blockaded planets (the route may exceed the maximum range); with no clean route they ship only if the amount exceeds the total blockade values along the least-loss route, otherwise they are cancelled. A blockaded source or target is an ordinary node on the route (its value counts), and a shipment is now cut at a blockaded origin on its first turn; see CLAUDE.md "Warships and Blockade". *(moved 2026-10-01)*
  - (3) Assault breaks blockades done: under Consolidate the assault targets planets visibly blockaded against the player first (ranked by offense already committed, a cut of the player's own orders within `blockadeTargetRecentTurns`, the smallest offense still needed, path cost and name), sizes the force by real warship offense (value x (1 + `blockadeBreakMargin`) minus offense in flight, ships taken from the cheapest sources), holds a broken blockade so the force does not leave and let it re-form (`AssaultPlanner.ContestedHolds`; a held colony is sink-only), and boosts Warship Offense research while blockaded (`blockadedOffenseResearchBoost`). Logged as `BlockadeTarget`, `BlockadeForce`, `BlockadeTargetEnd`, `OffenseResearchBoost` and `BlockadeSkipped`; see CLAUDE.md "Warships and Blockade" and `docs/superpowers/specs/2026-10-01-blockade-breaking-design.md`. A large committed force outranking a cheap blockade is the desired outcome, not a defect. *(moved 2026-10-01)*
