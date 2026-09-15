# Backlog

Pending items that don't belong to any single feature spec. Each feature spec also keeps its own
Follow-ups section for work specific to that feature (see `docs/superpowers/specs/`); check both when
asked "where are we with pending tasks."

## Open

- **Resource shipment eligibility during colonization.** Planets with colonization in progress
  (`Planet.IsPopulationTransferInProgress`) should still be valid targets for food shipment if the
  planet's type is low-food-producing. Needs investigation into how `PlayerAI.BuildResourceMatrix` /
  `ProcessFoodShortage` currently treats a planet with an incoming colonization, and what "low
  food-producing" should mean (by `Planet.PlanetType`, or by resource-data values). Raised
  2026-09-14, alongside the `BuildResourceMatrix` `PlayerID`-scoping fix.
