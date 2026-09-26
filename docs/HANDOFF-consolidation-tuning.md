# Handoff: Consolidation Tuning

Saved from the Claude Code session named **"Consolidation Tuning"**. Resume it with `/resume` (pick it) or
`claude --resume "Consolidation Tuning"`. This file is the written version so nothing depends on that history.

Read `CLAUDE.md` first (architecture, conventions, the self-check pattern). Specs and plans for the
first-contact switch and the assault are in `docs/superpowers/specs/` and `docs/superpowers/plans/`.

## Where things stand

The AI now switches from Expand to **Consolidate** on first contact with another player, garrisons only its
outer planets, masses a sized assault force on a known enemy planet, and builds warships toward a bounded,
economy-scaled fleet. Everything below is verified by the Editor self-checks and three long Play-mode runs.

| Area | What it does | Key code |
|---|---|---|
| First contact | one-way Expand -> Consolidate when a known planet holds another player's population or docked ship | `PlayerKnowledge.HasContact`, `PlayerAI.TryEnterConsolidate` |
| Outer garrisons | Consolidate garrisons only outer planets (a neighbour not colonized by me), round 1; every other ship is spare | `ShipTransportPlanner` (`MaintainsGarrison`, `TargetRank`, `HeldPlanet`) |
| Assault | one sticky target, force = `max(assaultMinimumShips, ceil(known enemy docked warships x assaultRatio))` | `AssaultPlanner`, `PlayerAI.PlanShipActions` |
| Production weight | Warship weight x shortfall multiplier, tapering to 0 at `wanted x warshipFleetCap` | `PlayerAI.WarshipShortfallMultiplier`, `WantedWarships`, `OwnedWarships` |
| Fleet ceiling | `wanted` is bounded by `warshipsPerColonizedPlanet` x my colonized planets | `PlayerAI.WantedWarships` |
| Zero-weight exclusion | any production choice without a positive finite weight is dropped (all strategies) | `PlayerAI.OfferedChoices` |
| Per-player incoming ships | in-flight ship counters keyed by (kind, owner) | `Planet.GetIncomingShips(kind, owner)` |
| Fleet icons | one icon per player on a planet with a ship count; click opens that player's fleet | `FleetSummary`, `PlanetUIObject`, `PlanetDetailUIController`, `FleetUIController` |
| Catalogs | improvement research/production to tier 10, Warship Weapons / ColonyShip Improvement research to tier 5 | `Catalogs/*/*.json` |
| Logs | tuning logs moved to `AITuningLogs/` at the project root (outside `Assets/`) | `AITuningLogger` |

### Tunables (`GameAIConstants`, all have in-code defaults)

| Tunable | Default | Meaning |
|---|---|---|
| `garrisonSpecialized/Outer/Prime/HighTraffic/HighlySpecialized` | 4 / 6 / 4 / 2 / 1 | home garrison per category |
| `assaultRatio` / `assaultMinimumShips` | 1.5 / 3 | assault force = max(min, ceil(enemy known docked x ratio)) |
| `warshipShortfallBoost` | 2 | Warship weight multiplier is `1 + boost x shortfall/wanted` below the wanted fleet |
| `warshipFleetCap` | 1.5 | multiplier tapers 1 -> 0 between wanted and wanted x cap, Warship is not offered at the cap |
| `warshipsPerColonizedPlanet` | 8 | ceiling on the wanted fleet |

The `assaultRatio` above 1 makes wanted chase the enemies' fleets (an arms race); the ceiling is what bounds it.

## What the long runs showed

- Warship control works: 238 zero-multiplier player-turns had 0 warship starts; fleets stop near 1.5-1.6 x wanted.
- The colony-ship flood after the warship cutoff was a zero-weight bug (an all-zero row is picked uniformly);
  `OfferedChoices` fixed it.
- **The catalog expansion dilutes choices.** Roulette weights are per item, so each newly unlocked tier is
  another full-weight choice: P2 (most tiers) built few warships despite a 2.2x boost, and P0 researched
  `Warship 1` at turn 233 (others 40-64; earlier runs 30-76).
- Total production starts fall late (208 -> 56 per 50 turns): planets idle or build long, expensive improvements.

## Open items, in the order I would take them

1. **Per-subType normalization of the production and research rolls (recommended, not built).** Divide an item's
   weight by the number of eligible items of its subType in that row, so adding tiers does not change a
   category's odds. Applies to both rolls; slightly changes Expand's odds. Alternatives: production only, or
   retune weights by hand.
2. **Decide about improvement upkeep.** `Catalog.CatalogSaveData.ItemSaveData` has no `tier`/`maintenanceCost`
   (`Catalog.cs`), so both are dropped on load: upkeep is effectively 0 and the values in the catalog JSON are
   dead data. The inspector's **Save Catalog button strips them from the JSON**. Loading them would turn upkeep
   on, and `CompletedImprovements` charges every completed tier (tier 0-10 of one category = 418 per turn) while only the
   best tier's yield applies, so it would also need "charge only the best tier per subType" and a re-tune. All
   tuning so far ran with upkeep off.
3. Play-mode check of the new research tiers (nobody reached tier 9-10 in 400 turns; production tiers 8-10 never started).
4. Reviewer minors, deferred: the self-check drives `ComputeWarshipMultiplier`, a copy of the inline block in
   `BuildIndustryMatrix` (nothing asserts the wiring); `wanted <= 0` means warships are never capped; the taper
   is a step for rows whose only positive choice is Warship, and ships in production are not counted in `have`
   (an option is a per-planet probabilistic offer of Warship).
5. Pre-existing issues noticed, not touched: saves do not store `CompletedImprovements`/yield modifiers (loading
   a save resets improvements); `Planet.cs` uses `_grotsitsProduction` as the rate for Food, Research and
   Industry worker requirements (lines ~227, 272, 284; looks like copy-paste); player builds load catalogs from
   `persistentDataPath/Catalogs/` and nothing copies them there; the same improvement can be queued and completed
   twice; a stale comment on `NumChoices` in `BuildIndustryMatrix`.

## Reading a tuning log

Enable with `_logAIEvents` on the `MainMenu` component (or `Gameboard`); files land in `AITuningLogs/`. Lines are
`T<turn>|P<player>|<Event>|<fields>`. Events used most: `StrategyChange`, `AssaultTarget|<planet>|<required>`,
`WarshipBoost|<wanted>|<have>|<multiplier>`, `ProductionSet|<planet>|<item>`, `ProductionComplete`, `ShipMove`,
`ColonizeStart`, `ResearchComplete|<item>`. `ColonizerReady` and `ResearchComplete` repeat every turn the
condition holds, so their counts are overcounts. Useful cuts: starts per 30-50 turn window by class
(`ColonyShipProduction` / `WarShipProduction` / everything else), `wanted/have/multiplier` sampled per player
every 30 turns, warship starts on zero-multiplier turns (must be 0), and completions against starts (idle planets).

## Working agreements

- Recommend one option with every choice; brainstorm bounded changes in chat and get a yes before coding.
- Test first. Unity cannot be run from the CLI: Rider's Roslyn check is a weak signal (and cannot see brand-new
  files), so ask for the Editor self-checks (`FlatSpace -> AI`/`UI` menus) and a Play-mode look, and read
  `ALL PASSED (N assertions ran)` including the count.
- A brand-new script's `.meta` must be committed with it. Stage explicit paths only; never touch the user's own
  uncommitted files. Commit and push only when asked.
