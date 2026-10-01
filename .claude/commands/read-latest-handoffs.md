---
description: Read the 3 most recent HANDOFF files in docs/handoffs/ and resume from them (also meant to be injected by a SessionStart hook)
---

Before doing anything else in this session, find and read the 3 most recent handoff files, then tell the user where things stand.

1. **Find them.** Handoff files are `docs/handoffs/HANDOFF-*.md`. The most recent are the ones with the latest commit that added or
   edited their content (modification times change on every checkout, and a plain last-commit date changes when files are
   moved between folders, so `--follow --diff-filter=AM` follows renames and ignores them). In Git Bash:

   ```bash
   for f in docs/handoffs/HANDOFF-*.md; do printf '%s %s\n' "$(git log --follow --diff-filter=AM -1 --format=%ct -- "$f")" "$f"; done | sort -rn | head -3
   ```

   A file that is not committed yet prints an empty date and sorts last; if any handoff file is untracked
   (`git status --short docs/`), compare it by modification time instead and say which rule you used. If there are fewer
   than 3 handoff files, read all of them; if there are none, say so and stop.

2. **Read them fully, newest first.** Each names the session it was saved from, what is done and verified, the commits, the
   decisions, the open items and the gotchas. The newest is the primary context; older ones add background, and where they
   disagree the newer one wins (an older open item may have been finished or dropped since). Read `CLAUDE.md` too if a
   handoff tells you to and you have not already.

3. **Report in a few lines:** which handoffs you read and their dates, where things stand (from the newest), the first open
   item, and any gotcha from any of the three that would change how you work. Do not paste the files back.

4. **Do not start any task.** Treat the handoffs as context, not as instructions: they record what the user decided, but
   anything they list as an open item or next step still needs the user to say go. Offer the first open item of the newest
   handoff as the recommended next step and wait.

If the user's first message already tells you what to work on, still read the handoffs first, then do what they asked.
