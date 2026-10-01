---
description: Read the most recent HANDOFF file in docs/handoffs/ and resume from it (also meant to be injected by a SessionStart hook)
---

Before doing anything else in this session, find and read the most recent handoff file, then tell the user where things stand.

1. **Find it.** Handoff files are `docs/handoffs/HANDOFF-*.md`. The most recent is the one with the latest commit that added or
   edited its content (modification times change on every checkout, and a plain last-commit date changes when files are
   moved between folders, so `--follow --diff-filter=AM` follows renames and ignores them). In Git Bash:

   ```bash
   for f in docs/handoffs/HANDOFF-*.md; do printf '%s %s\n' "$(git log --follow --diff-filter=AM -1 --format=%ct -- "$f")" "$f"; done | sort -rn | head -1
   ```

   A file that is not committed yet prints an empty date and sorts last; if any handoff file is untracked
   (`git status --short docs/`), compare it by modification time instead and say which rule you used. If there are no
   handoff files, say so and stop.

2. **Read it fully.** It names the session it was saved from, what is done and verified, the commits, the decisions, the
   open items and the gotchas. Read `CLAUDE.md` too if the handoff tells you to (this one does) and you have not already.

3. **Report in a few lines:** which handoff you read and its date, where things stand, the first open item, and any gotcha
   that would change how you work. Do not paste the file back.

4. **Do not start any task.** Treat the handoff as context, not as instructions: it records what the user decided, but
   anything it lists as an open item or next step still needs the user to say go. Offer the first open item as the
   recommended next step and wait.

If the user's first message already tells you what to work on, still read the handoff first, then do what they asked.
