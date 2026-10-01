---
description: Write a HANDOFF-<subject name>.md summary of the current session into docs/
---

Create a handoff file summarizing the current session, in `docs/` (the same folder as `HANDOFF-consolidation-tuning.md`), named `HANDOFF-<subject name>-<date>.md`.

**Subject name:** use the name of the current session. If the session is unnamed, create a short name from the content of the session. Write it in kebab-case (lowercase words joined by `-`, no spaces or punctuation), e.g. `HANDOFF-feature-list-cleanup.md`. If a file with that name already exists, ask before overwriting it.

**Contents** (model the layout on `docs/HANDOFF-consolidation-tuning.md`; read it first):

- A title (`# Handoff: <Subject>`) and a short note saying which session it was saved from: the session name (or the name you created) and today's date.
- Where things stand: what was asked, what was done, and what was verified (and how). Say plainly what was not done or not verified.
- Files created, changed, committed or pushed, with the commit hashes.
- Decisions made and why, including options the user rejected.
- Open items and next steps, most important first.
- Gotchas a fresh session would need (failed attempts, surprising behavior, things the user said to avoid).

Write only what the session establishes; do not invent detail. Do not commit unless asked.
