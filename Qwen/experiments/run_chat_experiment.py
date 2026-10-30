"""Runs the diplomacy chat prompt (a copy of Assets/Chat/ChatClient.cs) against Ollama models.

3 hostility levels x 3 messages, each with and without the few-shot sample turns, so models can be compared
on tone, length, preamble, repeated imagery and time. Keep GenerateDynamicSystemPrompt / sample lines in step with ChatClient.cs.

    python Qwen/experiments/run_chat_experiment.py gemma3:4b qwenChat:latest --out Qwen/experiments/results.md
    python Qwen/experiments/run_chat_experiment.py qwen3.5:4b --no-think
"""
import argparse
import json
import time
import urllib.request

URL = "http://localhost:11434/api/chat"
# Same sampling the Modelfile sets (the request carries it so any base model gets the same settings).
OPTIONS = {"temperature": 0.9, "top_p": 0.9, "num_ctx": 8192}

QUESTION = "What are you going to do about it?"
SAMPLES = {
    "Hostility": "I'll rip out your spleen!",
    "Fear": "I'm going to beg and plead and whine and cry",
    "Enigmatic": "The wind blows gently across the surface of the pond, splashing the frog in its face.",
}
# (label, hostility, stance): the stance a rival at that hostility would plausibly hold.
LEVELS = [("hostile", 80.0, "War"), ("enigmatic", 50.0, "Peace"), ("fearful", 15.0, "Peace")]
MESSAGES = [
    "Negotiations Begin",
    "Player 0 has declared war on you.",
    "Player 0 has surrendered to you. A truce begins.",
]


def tone_of(hostility):
    if hostility >= 66:
        return "Hostility"
    if hostility < 33:
        return "Fear"
    return "Enigmatic"


def system_prompt(me, player, hostility, stance):
    p = "\n\nCURRENT SITUATION CONTEXT:\n"
    p += f"- You are {me}, talking to {player}\n"
    p += f"- Your current hostility score with the player is: {hostility:g}. Do not state your hostility directly, use it to guide your word choice\n"
    p += f"- Your current stance with the player is: {stance}. Do not state your stance directly, use it to guide your word choice\n"
    p += "- You must strictly maintain your designated tone constraint while addressing this relationship and requested response.\n"
    p += "\n"
    p += "SYSTEM:\n"
    p += "You are an advanced NPC dialogue generation engine operating inside a game loop.\n"
    p += "Your core priority is to process the tone and game variables provided in the system context and output speech that completely embodies the assigned tone profile.\n"
    p += "\n"
    p += "CRITICAL RULES:\n"
    p += "1. Output ONLY the literal spoken dialogue of the character.\n"
    p += "2. Never include meta-commentary, introductory text, or explanatory footnotes (e.g., Do NOT say 'Here is an aggressive response:').\n"
    p += "3. Do not break character under any circumstances.\n"
    p += "4. Keep responses punchy, direct, and under 3 structural sentences.\n"
    p += "\n"
    p += "CURRENT TONE CONSTRAINT:\n"
    p += tone_of(hostility)
    return p


def chat(model, messages, no_think):
    payload = {"model": model, "messages": messages, "stream": False, "options": OPTIONS}
    if no_think:
        payload["think"] = False
    req = urllib.request.Request(URL, json.dumps(payload).encode("utf-8"), {"Content-Type": "application/json"})
    start = time.time()
    with urllib.request.urlopen(req, timeout=300) as resp:
        body = json.load(resp)
    return body["message"]["content"].strip(), time.time() - start


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("models", nargs="+")
    ap.add_argument("--no-think", action="store_true", help="send think:false (Qwen3-family models)")
    ap.add_argument("--out", help="write a markdown report here")
    args = ap.parse_args()

    lines = []
    for model in args.models:
        chat(model, [{"role": "user", "content": "hi"}], args.no_think)  # warm-up: load the model before timing
        lines.append(f"## {model}\n")
        for fewshot in (True, False):
            lines.append(f"### {'with' if fewshot else 'without'} few-shot\n")
            for label, hostility, stance in LEVELS:
                sys_p = system_prompt("Player 2", "Player 0", hostility, stance)
                for msg in MESSAGES:
                    messages = [{"role": "system", "content": sys_p}]
                    if fewshot:
                        messages += [{"role": "user", "content": QUESTION},
                                     {"role": "assistant", "content": SAMPLES[tone_of(hostility)]}]
                    messages.append({"role": "user", "content": msg})
                    reply, secs = chat(model, messages, args.no_think)
                    words = len(reply.split())
                    row = f"- **{label}** | _{msg}_ | {secs:.1f}s, {words} words\n  > {reply.replace(chr(10), ' / ')}"
                    print(f"[{model} {'FS' if fewshot else 'no-FS'}] {row}")
                    lines.append(row)
            lines.append("")
    if args.out:
        with open(args.out, "w", encoding="utf-8") as f:
            f.write("\n".join(lines) + "\n")


if __name__ == "__main__":
    main()
