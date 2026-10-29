# Other lightweight models to try (2026-10-29)

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
