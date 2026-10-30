# Handoff: Qwen chat (local LLM replies on the Diplomacy screen)

Saved from the Claude Code session named **"qwen_chat"** on 2026-10-29. Branch `QwenTalk`. Read `CLAUDE.md` first (the Diplomacy screen section) and
`FUTURE_FEATURES.md` (the two LLM entries).

## Where things stand

**Asked:** review the user's first-pass integration of `qwen2.5:3b` (served by Ollama, built from `Qwen/Modelfile`; later switched to `gemma3:4b`, see below) for diplomacy text, then finish it:
show the reply in the Diplomacy screen's response box, greet on open and on picking a rival, and use the human player's id instead of a hardcoded 0.

**Done:**
- Code review of the branch (found: singleton broken after a scene reload, Modelfile `SYSTEM` probably overridden by the request's own system message,
  few-shot samples that were instructions, no error handling or timeout, prompt formatting, an unused `ChatContext.cs`). The user fixed most of them
  themselves; Claude added the timeout (`requestTimeoutSeconds`, 60 s), the failure and empty-reply warnings, and three tidy-ups (unused `using`,
  a no-op statement, a comment typo). The unneeded repeat-avoidance prompt line was deleted (each request is stateless).
- `ChatClient.RequestChatResponse(message, me, player, diplomacyState, Action<string> onReply = null)`: `me` is the rival (the speaker), `player` is the
  human. The reply goes to `onReply` (trimmed) and still to `Debug.Log`.
- `DiplomacyScreen`: the response box (upper right) shows `Player N: "reply"` (statics `_replyText`/`_replyFrom`/`_replyLabel`, cleared by `ResetState`);
  `RequestReply(message, rival)` is shared by `Send`, by `Toggle` (greeting "Negotiations Begin" for the selected rival on open) and by the list click
  (same greeting, only when the selection changes). It uses `Diplomacy.Get(rival, HumanPlayerId)` (the rival's view of the human) and the name
  `"Player " + human`; it returns without sending when no human plays.
- Wrote `Qwen/model-experiments.md` (candidate models and a comparison method) and the FUTURE_FEATURES entry "Experiment with other LLMs".

**Verified:** by reading the code. **Nothing was compiled or run in Play mode by Claude.** The user ran the committed code (`ab63ab4`) in Play mode and
said they were happy with the responses so far; Claude did not see the Console or any reply text.

## Files and commits

| Commit | Content |
|---|---|
| `20345d2` | the user's "Safety committ of first pass qwuen integration" (ChatClient, Modelfile, the temporary hook in `DiplomacyScreen.Send`) |
| `ab63ab4` | feat: show Qwen replies in the Diplomacy response box (`ChatClient.cs`, `DiplomacyScreen.cs`); pushed to `origin/QwenTalk` |

Uncommitted when this was written: `FUTURE_FEATURES.md` (new "Experiment with other LLMs" entry; the "AI response text from a local LLM" entry got a
"first pass built" note), `Qwen/model-experiments.md` (new), this handoff. `completed_features.md` was not changed: nothing was labelled done.

## Decisions

- **Third parameter, not second:** the user asked for the human's name as the "second parameter"; Claude used the third (`player`) because the second is the
  speaker (`You are {me}, talking to {player}`). The user confirmed.
- **Instructions in the request, not the Modelfile:** the rules ("output only dialogue", under 3 sentences) now live in `GenerateDynamicSystemPrompt`;
  the Modelfile's `SYSTEM` block was removed by the user.
- **No history in the prompt:** storing the last replies to forbid repeated phrases was rejected for now (small models do it poorly and copy what they are
  shown). If repetition appears, in order: `repeat_penalty` option, a client-side n-gram check with one retry, then history in the prompt.
- **Frog sample replaced (2026-10-29):** the enigmatic sample (frog and pond) became a descriptive line in the same few-shot slot (astronomy or spiritual metaphors, zen phrasing), which tested better on Gemma.
- **Greeting also on open:** fires on every open, replaces the box text, one model call each time.
- **Nothing moved to completed_features:** the entry is not labelled done and the work is a first pass.

## Open items

1. DONE 2026-10-29 (branch `qwen-chat-followups`): the request is sent only after `SubmitHumanCommands` applied the command.
2. DONE 2026-10-29: `DiplomacyScreen.EventMessage` turns `declareWar` / `makePeace` / `surrender` into sentences.
3. DONE 2026-10-29: one `Tone` enum and `ToneOf` in `ChatClient.cs` (`HostileFrom` 66, `FearfulBelow` 33) drive the sample and the prompt.
4. DONE 2026-10-29 (first round): the game uses `gemma3:4b` (`gemmaChat`, built from `Qwen/Modelfile.gemma`), merged to `main` as `4ce77a8`. `Qwen/experiments/run_chat_experiment.py` reruns the comparison; findings in `Qwen/model-experiments.md`. The few-shot stays for Hostility and Fear; Enigmatic's sample was replaced by a descriptive line (the owner's tests). Still open: repeated Gemma runs, `qwen3.5:4b` with a stricter prompt (`think: false` needed), other small models (see the FUTURE_FEATURES entry).
5. Tie replies to the AI's real decisions once ask-and-trade exists (the FUTURE_FEATURES entry, "never the other way round").
6. A failed request leaves the old reply in the box with only a console warning; there is no on-screen error.

## Gotchas

- Plain `http://localhost` works in the Editor; a built player may need "Allow downloads over HTTP".
- `ChatClient.Instance` is created on the `Gameboard` GameObject; the null check must be a Unity `!= null` (statics survive a scene load).
- `modelName` is `gemmaChat:latest`: it must match `ollama list` (created with `ollama create gemmaChat -f Qwen/Modelfile.gemma`; the old Qwen model is `qwenChat`, from `Qwen/Modelfile`).
- The first reply after Ollama starts is slow (model load); the timeout is 60 s.
- `Diplomacy.Get(me, rival)` is `me`'s view of `rival`; `DiplomacyState.Pair` is a struct, so a `Get` result is a copy.
- Replies can arrive out of order if the user clicks quickly; the last to arrive is shown (the "Player N:" prefix shows who spoke).
