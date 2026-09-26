# Future Features

- **Human player mode** — today every player is AI-driven and the game only runs the simulation forward. Add a mode where a human controls a player and issues orders (shipments, colonization, production, research) instead of `PlayerAI`.
- **AIStrategyConsolidate** — a strategy phase the AI enters after first contact with another player, focused on fleet build, strategic colonization, and planet defense. *First cycle done: the first-contact trigger and Expand → Consolidate switch (Consolidate plays like Expand for now). Remaining: fleet build, strategic colonization, planet defense.* *Second cycle done: outer-only garrisons and assault targeting under Consolidate. Third cycle done: Consolidate's Warship production weight scales with the fleet shortfall. Remaining: strategic colonization, planet defense.*
- **Distribution centers** — today resource shipping moves the maximum amount of resources. In consolidate mode, change the model so the AI picks strategically located planets to serve as warehouses for resources.
- **Ship to ship combat** — warships engaging each other.
- **Blockade planet with docked ships** — ships docked at a planet can blockade it.
- **Planetary invasion** — taking over another player's planet.
- **Multi-fleet planet icons** — the planet UI shows a single fleet icon today; once assault fleets sit on enemy planets it needs to show more than one, at most one per player.
