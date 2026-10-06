---
description: Close out a piece of work: run update_feature_list, then write the session handoff (in that order)
---

Close out the current piece of work in two steps, in this order. Each step's instructions live in its own command file, so read the file
at the start of the step and follow it exactly (do not work from memory of an older version):

1. **Update the feature list.** Read `.claude/commands/update_feature_list.md` and carry it out: move the done sub features and features from
   `FUTURE_FEATURES.md` into `completed_features.md`, with the "moved to completed_features on <date>" lines and the dates it requires. Tell the user
   in a few lines which sub features you moved and which judgment calls you made (for example an item labelled "built, pending tuning" that you
   treated as done), because the handoff in step 2 will repeat them.

2. **Write the handoff.** Read `.claude/commands/handoff.md` and carry it out: write `docs/handoffs/HANDOFF-<subject name>-<date>.md` for the current
   session. Because step 1 changed `FUTURE_FEATURES.md` and `completed_features.md`, list those two files under the files changed (uncommitted unless
   they already are) and mention in the open items anything step 1 left in `FUTURE_FEATURES.md` for this work. If a handoff with that name already
   exists, ask before overwriting it (as `handoff.md` says).

Do not commit or push in either step unless the user asks. When both are done, report in a few lines: what moved, the handoff file name, and the
list of uncommitted files.
