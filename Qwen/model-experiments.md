# Model experiments for the diplomacy chat (2026-10-29)

## Result: the game uses `gemma3:4b` (`gemmaChat`)

Built from `Qwen/Modelfile.gemma` (`ollama create gemmaChat -f Qwen/Modelfile.gemma`); `ChatClient.modelName` defaults to `gemmaChat:latest`.
The `qwenChat` model (`Qwen/Modelfile`, `qwen2.5:3b`) is the previous baseline.

Compared with `Qwen/experiments/run_chat_experiment.py` (3 hostility levels x 3 messages, with and without the few-shot, one reply per cell at
temperature 0.9; replies in `Qwen/experiments/results-gemma3-4b.md` and `results-qwen3.5-4b.md`). Latency was about 2.4-2.7 s a reply for all three
models after the load (the first request after Ollama starts loads the model, 5-10 s).

| Model | Tone control | Rule-following (dialogue only, short) | Reacts to the event |
|---|---|---|---|
| `gemma3:4b` | good; enigmatic flat without help | best | best |
| `qwen3.5:4b` (`think: false`) | best enigmatic, uneven fear (a threat instead of fear) | weakest: stage directions ("I whisper..."), up to 59 words, quoted paragraphs | good |
| `qwen2.5:3b` | weakest; leaks the tone word, misreads events | good | weakest |

Why Gemma: it keeps to short in-character dialogue with no preamble and answers what actually happened ("Good. About time you showed some spine." to a war
declaration), which the Qwen models often missed. Qwen3.5 writes the most atmospheric lines but would need stricter instructions and a length cap.

**Few-shot findings (owner's tests, after the script run, which suggested otherwise from one reply per cell):** on Gemma the few-shot sample works better
than none for Hostility and Fear, so both stay. Enigmatic does better with a descriptive line than with a sample reply: the frog and pond sample is
replaced by "Use astronomy or spiritual metaphors and profound zen-like phrasing to obfuscate your meaning. don't use 'river' or 'ocean'", still in the
assistant slot of the few-shot (the owner likes the results there; moving it into the system prompt's tone constraint is an untried option). Naming
'river' and 'ocean' may prime them, so watch for those words.

Not done: Gemma was not sampled repeatedly to measure variation; `qwen3.5:4b` with a stricter prompt was not tried.

## Candidates considered

Candidates for the diplomacy chat (`Assets/Chat/ChatClient.cs`, model set by `modelName` and the Modelfile's `FROM` line). Tags are from memory: check each against the Ollama library before pulling.

1. **`gemma3:4b` (first pick).** Good at creative and in-character writing for its size, with a more distinctive voice than Qwen or Llama at 3-4B. Handles tone instructions well, so a good candidate for dropping the few-shot. Gemma has no true system role, but Ollama's template folds the system prompt into the first user turn, so the code needs no change. About 3 GB.
2. **`phi4-mini` (3.8B).** Strong at following instructions precisely (length limits, "output only dialogue"). The prose is drier and more clinical, so the Fear and Enigmatic tones may come out flat. Worth testing if consistency matters more than flavour.
3. **`llama3.2:3b`.** The safe baseline: fluent and fast. (There is no `llama3.3:3b`; Llama 3.3 is 70B only.) May soften the aggressive tone.
4. **`qwen3:1.7b` or `gemma3:1b`.** To see how small it can go. Quick, but expect weaker tone control and more repetition. Only worth it if latency or VRAM becomes a concern, for example replies from several rivals per turn.
5. **`smollm2:1.7b`.** Another small test; expected to be the least reliable here.

Also `qwen3:4b`: a likely quality step up from `qwen2.5:3b`, but Qwen3 "thinks" by default, which adds latency and can put `<think>...</think>` into the reply. Needs `"think": false` in the request (a `bool think` field on `OllamaChatPayload`). I could not confirm a `qwen3.5:4b` tag exists.

## How to compare

Fix a test set of 3 hostility values (>= 66, < 33, in between) x 3 messages (`Negotiations Begin`, `declareWar`, `surrender`) and run each model with and without the few-shot sample turns. Look at:

- whether the tone holds,
- under 3 sentences, no preamble,
- whether the same nouns repeat (the frog and spleen samples),
- response time.

Nine prompts per model is enough to see which are worth a closer look. Keep temperature at 0.9 (Modelfile) since it suits dialogue; if a model rambles, try 0.7 before blaming the model.

Swapping models is an Inspector change to `modelName` plus an `ollama create` from `Qwen/Modelfile` with a different `FROM` line.
