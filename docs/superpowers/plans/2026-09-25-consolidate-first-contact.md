# AIStrategyConsolidate First-Contact Trigger Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Switch a player from `AIStrategyExpand` to `AIStrategyConsolidate` the turn it first makes contact with another player, with Consolidate behaving exactly like Expand for now.

**Architecture:** `PlayerKnowledge.HasContact` reports whether any planet the player knows holds another player's population or docked ship. `PlayerAI.TryEnterConsolidate` (called first thing in `ProcessResults`) uses it to flip the strategy one-way and log a `StrategyChange` event. Consolidate's `case` runs the Expand routine and its weight-table entries alias Expand's dictionaries so nothing changes behaviorally.

**Tech Stack:** Unity 6000.4.1f1, C#, Editor self-check (`Assets/Editor/PlayerKnowledgeSelfCheck.cs`).

**Spec:** `docs/superpowers/specs/2026-09-25-consolidate-first-contact-design.md`

## Global Constraints

- There is no command-line build or test runner. "Run" for a self-check means: the user focuses the Unity Editor (so it recompiles), then runs `FlatSpace -> AI -> Run Player Knowledge Self-Check` and reads the Console. `mcp__rider__build_solution_*` is unreliable here; `mcp__rider__get_file_problems` is a weak signal only.
- A self-check must never depend on `Gameboard.Instance`. Give test planets distinct positions (the existing `MakeSpawn` helper already does).
- A method a self-check calls directly is `public`, not `internal` (`Assets/Editor/` is a separate assembly).
- `AITuningLogger` is a global-namespace static class; `PlayerKnowledge` and `PlayerAI` are in `FlatSpace.AI`. Match each file's existing style.
- `OrderType` values are unaffected (no new order types), so no append-last concern here.
- No new script files are created, so there are no new `.meta` files to commit.
- The working tree has an unrelated modification to `Assets/Flatspace/UI/PlanetUIObject.cs`; never `git add -A`. Stage explicit paths only.
- Commit messages end with: `Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>`

## Review Focus

- Own presence never counts as contact: a player's own population/docked ship on a known planet must not trigger the switch.
- A rival two hops away (not in the known set) is not contact, for either player.
- The switch is sticky: once Consolidate, the strategy stays even after the rival's population is gone.
- An `AIStrategyAmass` player is never switched to Consolidate.
- Switching must not silently drop Expand's tuning: Consolidate's research/industry weights equal Expand's, not the neutral default.

---

### Task 1: `PlayerKnowledge.HasContact`

**Files:**
- Modify: `Assets/Flatspace/GameAI/PlayerKnowledge.cs` (add a method after `KnownPlanets`, line 22)
- Modify: `Assets/Editor/PlayerKnowledgeSelfCheck.cs` (register in `Run()`, add `RunFirstContactChecks`)

**Interfaces:**
- Consumes: `GameAIMap.GetPlanet(string)`, `Planet.Population` (`List<Planet.Inhabitant>`, field `Player`), `Planet.DockedShips` (`List<Ship>`, field `Owner`), `PlayerKnowledge.KnownPlanets(int)`.
- Produces: `public bool PlayerKnowledge.HasContact(GameAIMap map, int playerId)`.

- [ ] **Step 1: Write the failing self-check**

In `PlayerKnowledgeSelfCheck.Run()`, add after `ok &= RunKnownPlanetsSaveRoundTripCheck();`:

```csharp
        ok &= RunFirstContactChecks();
```

Add these methods before the final closing brace of the class:

```csharp
    // A - B - C, all Normal planets; A starts populated by player 0.
    private static GameAIMap BuildContactLine(GameObject mapGo)
    {
        var map = mapGo.AddComponent<GameAIMap>();
        var constants = ScriptableObject.CreateInstance<GameAIConstants>();
        var spawns = new List<PlanetSpawnData>
        {
            MakeSpawn("A", initialPopulation: 1, connections: new[] { "B" }),
            MakeSpawn("B", initialPopulation: 0, connections: new[] { "C" }),
            MakeSpawn("C", initialPopulation: 0),
        };
        map.GameAIMapInit(spawns, constants);
        return map;
    }

    public static bool RunFirstContactChecks()
    {
        var ok = true;

        // No rival anywhere; player 0's own ship on a known planet must not count.
        var go = new GameObject("PKSelfCheckMap_Contact_None");
        try
        {
            var map = BuildContactLine(go);
            map.GetPlanet("B").DockShipFromSave(Ship.ShipKind.WarShip, 0, new List<string>());
            var knowledge = new PlayerKnowledge();
            knowledge.Update(map, numPlayers: 2);
            ok &= Check(!knowledge.HasContact(map, 0), "no rival: own population and own ship are not contact");
            ok &= Check(!knowledge.HasContact(map, 1), "player 1 knows nothing, so it has no contact");
        }
        finally { Object.DestroyImmediate(go); }

        // Rival population on a direct neighbour of A (B is known to player 0).
        go = new GameObject("PKSelfCheckMap_Contact_Population");
        try
        {
            var map = BuildContactLine(go);
            map.GetPlanet("B").Population.Add(new Planet.Inhabitant { Player = 1 });
            var knowledge = new PlayerKnowledge();
            knowledge.Update(map, numPlayers: 2);
            ok &= Check(knowledge.HasContact(map, 0), "rival population on a known neighbour is contact");
            ok &= Check(knowledge.HasContact(map, 1), "contact is mutual: player 1 knows A, which player 0 populates");
        }
        finally { Object.DestroyImmediate(go); }

        // Rival docked ship, no population, on a known neighbour.
        go = new GameObject("PKSelfCheckMap_Contact_Ship");
        try
        {
            var map = BuildContactLine(go);
            map.GetPlanet("B").DockShipFromSave(Ship.ShipKind.WarShip, 1, new List<string>());
            var knowledge = new PlayerKnowledge();
            knowledge.Update(map, numPlayers: 2);
            ok &= Check(knowledge.HasContact(map, 0), "a rival docked ship on a known planet is contact");
        }
        finally { Object.DestroyImmediate(go); }

        // Rival two hops from A: C is not in player 0's known set, and A is not in player 1's.
        go = new GameObject("PKSelfCheckMap_Contact_TwoHops");
        try
        {
            var map = BuildContactLine(go);
            map.GetPlanet("C").Population.Add(new Planet.Inhabitant { Player = 1 });
            var knowledge = new PlayerKnowledge();
            knowledge.Update(map, numPlayers: 2);
            ok &= Check(!knowledge.IsKnown(0, "C"), "precondition: C is two hops from A and unknown to player 0");
            ok &= Check(!knowledge.HasContact(map, 0), "a rival outside the known set is not contact");
            ok &= Check(!knowledge.HasContact(map, 1), "and neither is player 0's A for player 1 (also two hops)");
        }
        finally { Object.DestroyImmediate(go); }

        // Single-player board.
        go = new GameObject("PKSelfCheckMap_Contact_Solo");
        try
        {
            var map = BuildContactLine(go);
            var knowledge = new PlayerKnowledge();
            knowledge.Update(map, numPlayers: 1);
            ok &= Check(!knowledge.HasContact(map, 0), "a lone player never has contact");
        }
        finally { Object.DestroyImmediate(go); }

        return ok;
    }
```

- [ ] **Step 2: Verify it fails**

Ask the user to focus the Unity Editor. Expected: a compile error in the Console, `'PlayerKnowledge' does not contain a definition for 'HasContact'`.

- [ ] **Step 3: Implement**

In `PlayerKnowledge.cs`, after `KnownPlanets` (line 22), add:

```csharp
            /// <summary>
            /// True when any planet this player knows holds another player's population or docked
            /// ship. Reads Planet.Population / DockedShips directly (not Planet.Owner, which is
            /// NoOwner on a population tie) and needs no player count or Gameboard.Instance.
            /// </summary>
            public bool HasContact(GameAIMap map, int playerId)
            {
                foreach (var name in KnownPlanets(playerId))
                {
                    var planet = map.GetPlanet(name);
                    if (planet == null) continue;
                    if (planet.Population.Exists(p => p.Player != playerId)) return true;
                    if (planet.DockedShips.Exists(s => s.Owner != playerId)) return true;
                }
                return false;
            }
```

- [ ] **Step 4: Verify it passes**

User focuses the Editor, then runs `FlatSpace -> AI -> Run Player Knowledge Self-Check`. Expected: no `FAIL` errors and `[PlayerKnowledgeSelfCheck] ALL PASSED`. (The `[PathingSystem]` "has no connections" noise from the older checks is expected.)

- [ ] **Step 5: Commit**

```bash
git add Assets/Flatspace/GameAI/PlayerKnowledge.cs Assets/Editor/PlayerKnowledgeSelfCheck.cs
git commit -m "feat(ai): add PlayerKnowledge.HasContact for first-contact detection

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>"
```

---

### Task 2: Strategy switch, Consolidate plumbing, and logging

**Files:**
- Modify: `Assets/Flatspace/GameAI/PlayerAI.cs` (`ProcessResults` lines 36-50; weight tables lines 343-361 and 484-513; `GetResearchWeight` line 473)
- Modify: `Assets/Flatspace/Diagnostics/AITuningLogger.cs` (add `LogStrategyChange`)
- Modify: `Assets/Editor/PlayerKnowledgeSelfCheck.cs` (register and add two checks)

**Interfaces:**
- Consumes: `PlayerKnowledge.HasContact(GameAIMap, int)` from Task 1; `PlayerAI.Strategy`, `AIMap`, `Player.playerID`.
- Produces: `public bool PlayerAI.TryEnterConsolidate(int turnNumber)`; `public static float PlayerAI.GetResearchWeight(CatalogItem, AIStrategy)` (was private); `public static float PlayerAI.GetIndustryStrategyWeight(CatalogItem, AIStrategy)`; `public static void AITuningLogger.LogStrategyChange(int turnNumber, int playerId, string from, string to)`.

- [ ] **Step 1: Write the failing self-checks**

In `Run()`, add after `ok &= RunFirstContactChecks();`:

```csharp
        ok &= RunConsolidateSwitchCheck();
        ok &= RunConsolidateWeightAliasCheck();
```

Add before the final closing brace of the class:

```csharp
    public static bool RunConsolidateSwitchCheck()
    {
        var ok = true;
        var mapGo = new GameObject("PKSelfCheckMap_Switch");
        var playerGo = new GameObject("PKSelfCheckPlayer_Switch");
        try
        {
            var map = BuildContactLine(mapGo);
            var player = playerGo.AddComponent<Player>();
            var playerAI = playerGo.AddComponent<PlayerAI>();
            playerAI.Player = player;
            playerAI.AIMap = map;
            player.playerID = 0;

            map.Knowledge.Update(map, numPlayers: 2);
            ok &= Check(playerAI.Strategy == PlayerAI.AIStrategy.AIStrategyExpand,
                "a new PlayerAI starts in Expand");
            ok &= Check(!playerAI.TryEnterConsolidate(1) &&
                        playerAI.Strategy == PlayerAI.AIStrategy.AIStrategyExpand,
                "no contact: stays in Expand");

            var rival = new Planet.Inhabitant { Player = 1 };
            map.GetPlanet("B").Population.Add(rival);
            map.Knowledge.Update(map, numPlayers: 2);
            ok &= Check(playerAI.TryEnterConsolidate(2) &&
                        playerAI.Strategy == PlayerAI.AIStrategy.AIStrategyConsolidate,
                "contact: switches to Consolidate and reports it");

            map.GetPlanet("B").Population.Remove(rival);
            map.Knowledge.Update(map, numPlayers: 2);
            ok &= Check(!playerAI.TryEnterConsolidate(3) &&
                        playerAI.Strategy == PlayerAI.AIStrategy.AIStrategyConsolidate,
                "sticky: stays Consolidate after the rival is gone, and does not report a second switch");

            playerAI.Strategy = PlayerAI.AIStrategy.AIStrategyAmass;
            map.GetPlanet("B").Population.Add(rival);
            map.Knowledge.Update(map, numPlayers: 2);
            ok &= Check(!playerAI.TryEnterConsolidate(4) &&
                        playerAI.Strategy == PlayerAI.AIStrategy.AIStrategyAmass,
                "an Amass player is never switched to Consolidate");
        }
        finally
        {
            Object.DestroyImmediate(playerGo);
            Object.DestroyImmediate(mapGo);
        }
        return ok;
    }

    public static bool RunConsolidateWeightAliasCheck()
    {
        var ok = true;
        var item = ScriptableObject.CreateInstance<CatalogItem>();
        try
        {
            foreach (var subType in new[] { "Food", "Industry", "Grotsits", "Research", "ColonyShip", "Warship" })
            {
                item.subType = subType;
                ok &= Check(
                    PlayerAI.GetResearchWeight(item, PlayerAI.AIStrategy.AIStrategyConsolidate) ==
                    PlayerAI.GetResearchWeight(item, PlayerAI.AIStrategy.AIStrategyExpand),
                    $"Consolidate research weight matches Expand for {subType}");
                ok &= Check(
                    PlayerAI.GetIndustryStrategyWeight(item, PlayerAI.AIStrategy.AIStrategyConsolidate) ==
                    PlayerAI.GetIndustryStrategyWeight(item, PlayerAI.AIStrategy.AIStrategyExpand),
                    $"Consolidate industry weight matches Expand for {subType}");
            }

            // Guard against both sides silently being the neutral default.
            item.subType = "Food";
            ok &= Check(PlayerAI.GetResearchWeight(item, PlayerAI.AIStrategy.AIStrategyConsolidate) == 3.0f,
                "Consolidate Food research weight is Expand's 3.0, not the neutral default");
            ok &= Check(PlayerAI.GetIndustryStrategyWeight(item, PlayerAI.AIStrategy.AIStrategyConsolidate) == 2.5f,
                "Consolidate Food industry weight is Expand's 2.5, not the neutral default");
        }
        finally
        {
            Object.DestroyImmediate(item);
        }
        return ok;
    }
```

- [ ] **Step 2: Verify it fails**

User focuses the Editor. Expected: compile errors, `'PlayerAI' does not contain a definition for 'TryEnterConsolidate'` / `'GetIndustryStrategyWeight'`, and `'PlayerAI.GetResearchWeight' is inaccessible due to its protection level`.

- [ ] **Step 3: Add the logger method**

In `AITuningLogger.cs`, after `LogResearchComplete` (line 132):

```csharp
    public static void LogStrategyChange(int turnNumber, int playerId, string from, string to)
    {
        if (_currentLogPath == null) return;
        AppendLines(new List<string> { FormatLine(turnNumber, playerId, "StrategyChange", from, to) });
    }
```

- [ ] **Step 4: Alias the research weights**

In `PlayerAI.cs`, replace the block from `// Roulette-wheel weight per item subType.` (line 343) through the closing `};` of `ResearchWeightTable` (line 361) with (the named dictionary must be declared *before* the table: C# static initializers run in textual order):

```csharp
            // Roulette-wheel weight per item subType. Higher = more likely to be picked.
            // 1.0f = neutral. Add an entry for AIStrategyAmass when needed.
            private static readonly Dictionary<string, float> ExpandResearchWeights =
                new Dictionary<string, float>
                {
                    { "Food",          3.0f },  // food upgrades biggest boost
                    { "Industry",      2.0f },  // useful but secondary
                    { "Grotsits",      1.0f },  // least useful while expanding
                    { "Research",      1.0f },  // least useful while expanding
                    { "ColonyShip",    2.5f },  // ships useful but secondary
                    { "Warship",       1.5f },  // Updated priority
                };
            private static readonly Dictionary<AIStrategy, Dictionary<string, float>> ResearchWeightTable =
                new Dictionary<AIStrategy, Dictionary<string, float>>
                {
                    { AIStrategy.AIStrategyExpand,      ExpandResearchWeights },
                    // Consolidate reuses Expand's weights until it gets its own tuning.
                    { AIStrategy.AIStrategyConsolidate, ExpandResearchWeights },
                    // AIStrategyAmass — add when needed
                };
```

Make `GetResearchWeight` public: change `private static float GetResearchWeight(` to `public static float GetResearchWeight(`.

- [ ] **Step 5: Alias the industry weights and extract the static lookup**

Replace the block from `// Roulette-wheel weight per item subType.` above `IndustryWeightTable` (line 485) through the end of `GetIndustryWeight` (line 513) with:

```csharp
            // Roulette-wheel weight per item subType. Higher = more likely to be picked.
            // 1.0f = neutral. Add an entry for AIStrategyAmass when needed.
            private static readonly Dictionary<string, float> ExpandIndustryWeights =
                new Dictionary<string, float>
                {
                    { "Food",          2.5f },  // food needed for pop growth
                    { "Industry",      1.5f },  // slightly useful while expanding
                    { "Grotsits",      1.0f },  // build the base
                    { "Research",      1.0f },  // build the base
                    { "ColonyShip",    2.5f },  // colony ships needed
                    { "Warship",       1.5f },  // Updated priority
                };
            private static readonly Dictionary<AIStrategy, Dictionary<string, float>> IndustryWeightTable =
                new Dictionary<AIStrategy, Dictionary<string, float>>
                {
                    { AIStrategy.AIStrategyExpand,      ExpandIndustryWeights },
                    // Consolidate reuses Expand's weights until it gets its own tuning.
                    { AIStrategy.AIStrategyConsolidate, ExpandIndustryWeights },
                    // AIStrategyAmass — add when needed
                };

            public static float GetIndustryStrategyWeight(CatalogItem item, AIStrategy strategy)
            {
                if (IndustryWeightTable.TryGetValue(strategy, out var typeWeights) &&
                    typeWeights.TryGetValue(item.subType, out var strategyWeight))
                {
                    return strategyWeight;
                }

                return DefaultChoiceWeight;
            }

            private float GetIndustryWeight(CatalogItem item, AIStrategy strategy, string planetName)
            {
                return GetIndustryStrategyWeight(item, strategy) * GetIndustrySituationalWeightMultiplier(item, planetName);
            }
```

- [ ] **Step 6: Add the switch and route the Consolidate case**

Replace `ProcessResults` (lines 36-50) with:

```csharp
            public void ProcessResults(
                List<Planet.PlanetUpdateResult> results,
                List<GameAI.GameAIOrder>        orders)
            {
                TryEnterConsolidate(Gameboard.Instance.TurnNumber);

                switch (Strategy)
                {
                    case AIStrategy.AIStrategyExpand:
                    case AIStrategy.AIStrategyConsolidate:
                        // Consolidate has no behavior of its own yet; it plays like Expand.
                        ProcessResultsStrategyExpand(results, Player, ref orders);
                        break;
                    case AIStrategy.AIStrategyAmass:
                        break;
                }
            }

            /// <summary>
            /// One-way switch from Expand to Consolidate on first contact with another player.
            /// Public (and free of Gameboard.Instance) so the self-check can drive it directly.
            /// Returns true only on the turn the switch happens.
            /// </summary>
            public bool TryEnterConsolidate(int turnNumber)
            {
                if (Strategy != AIStrategy.AIStrategyExpand) return false;
                if (!AIMap.Knowledge.HasContact(AIMap, Player.playerID)) return false;

                Strategy = AIStrategy.AIStrategyConsolidate;
                AITuningLogger.LogStrategyChange(turnNumber, Player.playerID,
                    AIStrategy.AIStrategyExpand.ToString(), AIStrategy.AIStrategyConsolidate.ToString());
                return true;
            }
```

- [ ] **Step 7: Verify it passes**

User focuses the Editor (check the Console for compile errors first), then runs `FlatSpace -> AI -> Run Player Knowledge Self-Check`. Expected: no `FAIL` errors and `[PlayerKnowledgeSelfCheck] ALL PASSED`. Also run `FlatSpace -> AI -> Run PlayerAI Resource Self-Check` and `Run Ship Transport Self-Check` to confirm the table refactor broke nothing.

- [ ] **Step 8: Verify the real match (Play mode)**

Set `_logAIEvents` on the `MainMenu` component in `MainMenu.unity`, enter Play mode from the main menu, and let a two-player match run until the players meet. Expected: the newest file in `Assets/Flatspace/AITuningLogs/` contains exactly one `T<turn>|P<id>|StrategyChange|AIStrategyExpand|AIStrategyConsolidate` line per player that made contact, and colonization/production keep happening for those players afterward. Turn the toggle back off afterward.

- [ ] **Step 9: Commit**

```bash
git add Assets/Flatspace/GameAI/PlayerAI.cs Assets/Flatspace/Diagnostics/AITuningLogger.cs Assets/Editor/PlayerKnowledgeSelfCheck.cs
git commit -m "feat(ai): switch Expand to Consolidate on first contact

Consolidate reuses Expand's behavior and weight tables for now.

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>"
```

---

### Task 3: Documentation

**Files:**
- Modify: `CLAUDE.md` (Player Knowledge section; AI Tuning Log section)
- Modify: `FUTURE_FEATURES.md` (AIStrategyConsolidate line)

**Interfaces:**
- Consumes: the behavior shipped in Tasks 1-2.
- Produces: nothing code-facing.

- [ ] **Step 1: Update `CLAUDE.md`**

In the **Player Knowledge** section, after the sentence ending "`Assets/Editor/PlayerKnowledgeSelfCheck.cs` is this subsystem's self-check.", add a new paragraph:

```markdown
`PlayerKnowledge.HasContact(map, playerId)` reports first contact: any known planet holding another
player's population or docked ship (read directly from `Planet.Population`/`DockedShips`, since
`Planet.Owner` is `NoOwner` on a population tie). `PlayerAI.TryEnterConsolidate` calls it first thing in
`ProcessResults` and flips Expand → Consolidate one-way; Amass/None are never switched. Consolidate has no
behavior of its own yet: its `ProcessResults` case runs the Expand routine and its research/industry
weight-table entries alias Expand's dictionaries (a missing strategy key silently falls back to a neutral
weight, so a new strategy must be given entries or it loses Expand's tuning).
```

In the **AI Tuning Log** section, add `strategy switched (StrategyChange|<from>|<to>)` to the parenthesized
list of outcome-level events (after "research started/completed").

- [ ] **Step 2: Update `FUTURE_FEATURES.md`**

Replace the AIStrategyConsolidate line with:

```markdown
- **AIStrategyConsolidate** — a strategy phase the AI enters after first contact with another player, focused on fleet build, strategic colonization, and planet defense. *First cycle done: the first-contact trigger and Expand → Consolidate switch (Consolidate plays like Expand for now). Remaining: fleet build, strategic colonization, planet defense.*
```

- [ ] **Step 3: Commit**

```bash
git add CLAUDE.md FUTURE_FEATURES.md
git commit -m "docs: document Consolidate first-contact switch

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>"
```
