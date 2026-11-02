# Chat personality and emotion (approach)

Date: 2026-11-01. Roadmap entry: "Expand the range of chat responses based on diplomatic status members" in `FUTURE_FEATURES.md`.
Owner implements; Claude writes the Editor test window once `BuildMessages` exists.

## Idea

The current three tones become **personalities** (the speech style). Hostility keeps its thresholds but now sets the **emotion** (the emotional content).
Each AI player gets a randomly assigned personality. The pair (personality, emotion) is passed to the chatbot.

## Steps

### 1. Two enums replace `Tone` (`Assets/Chat/ChatClient.cs`)

- `Personality { Aggressive, Fearful, Enigmatic }` - speech style. `Hostile` is renamed `Aggressive`.
- `Emotion { Angry, Neutral, Happy }` - emotional content.
- `ToneOf` becomes `EmotionOf(DiplomacyState.Pair)` with the same thresholds: `HostileFrom` 66 -> Angry, `FearfulBelow` 33 -> Happy, else Neutral.
  Hostility drives it exactly as today; only the names change.

### 2. Personality lives on the player, not the pair

- Add a personality to `Player` (a human has none). Assign once at match start for each AI player.
- **Use a separate `System.Random`, not `GameAI.Rand`.** `GameAI.Rand` is the shared simulation stream; drawing from it changes every match's outcome.
  Seed the separate one from the board seed plus the player id.
- **Save it in `GameSave.PlayerSave`, 0 = "as before".** Make 0 an "unassigned" value (not a real personality), so an older save assigns one on load
  with the same seeded draw.

### 3. Build the prompt from the pair

- `GenerateDynamicSystemPrompt` gets a personality block (speech style, today's tone constraint) and an emotion block
  ("you are angry / neutral / happy with this player").
- Keep the hostility-guided stance line; drop the raw hostility number (the emotion carries it).
- **Few-shot caution:** small models copy the example, so "I'll rip out your spleen!" keeps an Aggressive/Happy rival angry.
  Recommended: one few-shot per personality that is emotion-neutral in content and shows only style; the emotion line carries the mood.
  Fallback if Gemma ignores the mood: 9 samples (personality x emotion). The test window is how to find out.

### 4. Pull prompt building out of the coroutine (do this with step 1)

Add a pure static method the game and the test window both call, so they cannot drift apart:

```csharp
public static List<OllamaMessage> BuildMessages(
    string me, string player, Personality personality, Emotion emotion,
    <stance type of DiplomacyState.Pair.Stance> stance, string message)
```

It returns system + few-shot + user message. `SendChatRequestCoroutine` then takes the list instead of building it.
Keep it free of `Gameboard.Instance` and MonoBehaviour state.

### 5. Logging

Presentation only, so no new tuning-log line is required. Optional: `T0|P<id>|Personality|<name>` at assignment, so a log shows who was which
(if added, update the `tuning-log` skill and the AI Tuning Log section of `CLAUDE.md`).

### 6. Order of work

1. Enums and `EmotionOf`, 2. `BuildMessages` (these two first), 3. player assignment and save, 4. call-site changes in `DiplomacyScreen`.

## Test UI (Claude writes it after steps 1 and 4)

Editor window `FlatSpace -> Chat -> Personality Tester` (`Assets/Editor/`): personality dropdown, emotion dropdown, message box, reply box.
Works outside Play mode and posts straight to Ollama (polling the `UnityWebRequest` from `EditorApplication.update`), using `ChatClient.BuildMessages`.
Model name and URL default to the `ChatClient` field values.
