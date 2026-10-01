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
