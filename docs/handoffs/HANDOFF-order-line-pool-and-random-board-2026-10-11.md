# Handoff: Order line pool and random board start

Saved from an unnamed Claude Code session (ID `2977c60a-045b-4876-bf74-5bcb91d1139b`), named from its content, on 2026-10-11. All the work was
done on 2026-10-11. The owner called it "a little detour for some infrastructure": two small features outside the `ship_combat` milestone. The
session-start hook had the three newest handoffs read first (invasion, retreat, ship-combat signoff); the newest, `HANDOFF-invasion-2026-10-07.md`,
is still the context for the milestone work. Read `CLAUDE.md` first.

## Where things stand

**Asked and done, all merged to `main` and pushed (`origin/main` is at `b461484`):**

1. **Push all.** Nothing to push: after a fresh fetch `main` already matched `origin/main` at `00561fb`. The invasion handoff's "21 commits ahead" and
   "`retreat-v1-fallback` is a local tag" are stale: no tags and no other branches exist locally or on the remote.
2. **Order line pool.** `GameBoard.DisplayOrderGraphics` destroyed and recreated every order's `LineDrawObject` each turn. It now keeps one line per
   in-flight order and pools the lines of finished orders.
3. **Start a game from a random board.** The Start button asks "Use Saved Board"; No opens a popup for players, planets and seed, generates a board
   with `MapGenerator` (no designer scene) and starts the game on it.
4. **Two follow-ups to (3):** a "Random Boards" sidebar link in the config load dialog, and cancelling that dialog now stays on the main menu.

### Order line pool (`GameBoard.cs`, `LineDrawObject.cs`)

| Area | What it does | Key code |
|---|---|---|
| Per-order lines | `_orderLines` (`Dictionary<GameAIOrder, LineDrawObject>`, keyed by the order object; `GameAIOrder` is a class, so identity holds across turns) | `GameBoard.DisplayOrderGraphics` |
| Pool | `_orderLinePool` (`Stack<LineDrawObject>`): a finished order's line is `SetActive(false)` and pushed; a new order pops one (skipping destroyed entries) or instantiates from `_orderLineDrawObjectPrefab` | same |
| Update | each turn a live line gets `SetActive(true)`, both renderers re-enabled, `ResetFogCapture()`, then `SetPath`, `SetColor`, `SetSpriteColor` and the fog state exactly as before | same |
| No route | an order with fewer than 2 path points draws nothing (before, a line was instantiated and destroyed) | same |
| Teardown | `ClearGraphics` destroys pooled (inactive) lines, which its `GetComponentsInChildren` search skips, then clears both collections | `GameBoard.ClearGraphics` |
| Fog base colors | `LineDrawObject.ResetFogCapture()` clears `_fogBaseCaptured`; without it a recycled line kept the previous order's captured colors under fog | `LineDrawObject.cs` |

### Random board start

| Area | What it does | Key code |
|---|---|---|
| Menu | `MainMenu.StartGame` creates (once) and shows `RandomBoardPopup` under the root Canvas; new serialized `_randomBoardSettings` (the `MapGenSettings` template, assigned to `MapGenSettings_Default` in `MainMenu.unity`) | `MainMenu.cs` |
| Popups | built entirely in code (TMP and uGUI, no prefab): "Use Saved Board" Yes/No, then Number of Players / Number of Planets / Seed with OK/Back. Yes runs the old `StartNewGame()` file browser; OK clamps and writes the clamped values back into the fields | `RandomBoardPopup.cs` |
| Ranges | players 2 to `Player.PlayerColors.Count` (5), planets 10 to 400, defaults 2 / 20, seed a random positive int drawn each time the popup opens; a blank, zero or negative seed resolves to a fresh random one | `RandomBoardBuilder` |
| Generation | `MapGenerator.Generate` on a runtime copy of the template (the asset is never modified); a failure shows red text in the popup and loads nothing | `RandomBoardBuilder.Generate` |
| File | the board is written to `Application.persistentDataPath/BoardConfigs/Random/Random_s<seed>_<players>p_<planets>.json` (on this machine `C:\Users\clapt\AppData\LocalLow\DefaultCompany\FlatSpace\BoardConfigs\Random\`), so a saved game on it can reload through `boardDesignDataPath` | `RandomBoardBuilder.WriteBoardFile` |
| Start | `SaveLoadSystem.StartGameFromBoard(path, save)` (extracted from `LoadDesignerContent`, which now calls it): load `Flatspace`, then `InitGameFromDesignerConfig`; new parameterless `BoardDesignerSave()` | `SaveLoadSystem.cs` |
| Picker link | `ShowConfigFileBrowserAndStartNewGame` creates the random folder and adds a "Random Boards" quick link to the SimpleFileBrowser sidebar (the plugin skips duplicates) | `SaveLoadSystem.cs` |
| Cancel | cancelling that dialog only logs "Board config load cancelled"; it used to load `Flatspace` with its default board | `SaveLoadSystem.cs` |
| Self-check | `FlatSpace -> Board -> Run Random Board Self-Check`: ranges, parsing, same seed gives the same board, the template is untouched, names unique, connections valid, file round trip | `Assets/Editor/RandomBoardSelfCheck.cs` |

No new tuning-log output (the existing `BoardConfig` line carries the random file's name).

### What was verified, and how

- **Compile:** the owner focused the Editor after each step. One warning came up (`FindFirstObjectByType` obsolete in `MainMenu.cs`), fixed by switching to
  `FindAnyObjectByType`; no other errors were reported.
- **Random board:** the owner reported the compile, the scene assignment and `Run Random Board Self-Check` all succeeded, and ran the popups in Play
  (a board file `Random_s1414227327_3p_30.json` was written at 13:24). The owner did not report a result for each popup button.
- **Not verified:** the order line pool has not been seen in a Play run (no report back), and neither have the "Random Boards" sidebar link or the new
  cancel behaviour. I could not run Unity; Rider's `get_file_problems` was not usable, so there was no compile signal from me.
- **Not covered by any self-check:** the pool and the popup UI (the self-check covers `RandomBoardBuilder` only). Nothing was save/load-tested in Play with a
  random board.

## Files and commits (all on `main`, pushed)

| Commit | What |
|---|---|
| `0083f63` | perf: pool order line graphics (`GameBoard.cs`, `LineDrawObject.cs`) |
| `279a81b` | feat: start a game from a random board (new `RandomBoardBuilder.cs`, `RandomBoardPopup.cs`, `RandomBoardSelfCheck.cs`, each with its `.meta`; edits to `MainMenu.cs`, `SaveLoadSystem.cs`) |
| `da20900` | scene: assign `MapGenSettings_Default` to `MainMenu` (one line in `MainMenu.unity`) |
| `b461484` | fix: cancelling the board config dialog stays on the main menu |

Pushes: `00561fb..0083f63`, `0083f63..da20900`, `da20900..b461484` (the second push carried two commits). **Uncommitted:** this handoff file only.
`FUTURE_FEATURES.md` and `completed_features.md` were examined by `/feature_close` and left unchanged (neither feature was listed). A pre-existing
untracked `.idea/.idea.FlatSpace/.idea/indexLayout.xml` is not ours.

## Decisions and why (all the owner's; recommendation first each time)

- **Fog colors on a reused line:** add `LineDrawObject.ResetFogCapture()` (chosen over making `SetColor` clear the flag, or leaving it). It is the one edit
  outside `DisplayOrderGraphics` and was asked about first because the brief limited the change to that method (the brief's point 11).
- **Random board persistence:** write a JSON file under `persistentDataPath/BoardConfigs/Random/` (chosen over in-memory only, which could not be reloaded from a
  save). The owner then said it is fine to leave them there rather than under `Assets/` (rejected: an Editor-only copy under `Assets/Flatspace/BoardConfigs/Random`
  with `.meta` files and a `.gitignore` entry).
- **Picker access:** a sidebar quick link, chosen over any other navigation help, so the dialog needs no directory changing.
- **Cancel:** return to the main menu (the owner's wording). I did not reopen the "Use Saved Board" popup; offered as an alternative, not asked for.
- **Max players is `Player.PlayerColors.Count` (5)**, the real ceiling today (one colour per player). The owner's "current max" was read that way.
- **Rulings I made:** the popup is built in code, not as scene or prefab edits, so no hand-edited scene YAML; the only scene change is the one field
  assignment the owner saved from the Editor; `Instantiate` of the settings asset so the asset is never mutated; the bounded path of the brainstorming skill (short
  in-chat design, no spec or plan file) for both features.

## Open items, most important first

1. **Play-check the order line pool.** Run a few hundred turns with fog on (`Player n` and `All Players`) and watch: the number of `OrderLineDraw` children should
   plateau at the peak order count instead of churning; line colors under fog should match the order type; a save load or board change should leave no stale lines.
2. **Play-check the rest of the random board flow:** the "Random Boards" sidebar link (Start, then Yes), cancelling the dialog (should stay on the menu), and
   saving then loading a game started from a random board.
3. **The milestone work is unchanged:** the first open item of `HANDOFF-invasion-2026-10-07.md` is still next (a colony ship production boost for dominated planets),
   then offense-scaled conversion speed; see `FUTURE_FEATURES.md`.
4. **Minor, only if symptomatic:** random board files accumulate in `persistentDataPath` and are never pruned; the sidebar link exists only while the dialog is used from
   `MainMenu`; the popup has no Escape handling.
5. **Housekeeping:** commit this handoff when you want it kept; the stale items in the invasion handoff (unpushed commits, the fallback tag) no longer apply.

## Gotchas for a fresh session

- **Seed in the file name can differ from the typed seed:** `MapGenerator.Generate` retries `baseSeed + attempt` when a seed cannot be satisfied, and the file name
  uses `result.EffectiveSeed`, while the popup field keeps the typed seed.
- **Pooled lines are inactive**, so any `GetComponentsInChildren<LineDrawObject>()` (without `true`) skips them: `ClearGraphics` handles this explicitly, and
  `FogUIUpdate` also skips tagged `OrderLineDraw` lines. Anything new that must reach every order line has to go through the pool or pass `includeInactive`.
- **`TMP_InputField` built in code** must be disabled and re-enabled after its `textViewport` and `textComponent` are assigned (it was enabled when the component was
  added); `RandomBoardPopup.AddField` does this.
- **`FindFirstObjectByType` is obsolete in Unity 6.4** (a compile warning); use `FindAnyObjectByType`.
- **`Application.persistentDataPath` is `...\LocalLow\DefaultCompany\FlatSpace`** on this machine (company name is the default in `ProjectSettings.asset`); the Editor's
  config picker opens in `Assets/Flatspace/BoardConfigs`, which is a different folder.
- **A scene field reference lives only in the open scene:** the `_randomBoardSettings` assignment had to be saved from the Editor before it could be committed.
- **`python3 -I` worked in Git Bash this session** (a short heredoc script rewrote a block in `GameBoard.cs` and kept its line endings), unlike the earlier handoffs'
  hangs; the working copy is CRLF, so git warns "LF will be replaced by CRLF" on add, which is harmless here.
- **The owner wants** a recommendation with every choice, a yes before code, and commits and pushes only when asked (this session each commit and push was asked for).
