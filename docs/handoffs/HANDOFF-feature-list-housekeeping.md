# Handoff: Feature List Housekeeping

Saved from an unnamed Claude Code session on 2026-10-01; the name "feature-list-housekeeping" was chosen from the
session's content. The session set up a way to track finished features and added three project commands.

## Where things stand

- `completed_features.md` (repo root, next to `FUTURE_FEATURES.md`) now holds the finished work, in a "Done" section and
  a "Partially done" section. Every entry is dated 2026-10-01.
  - Done: "Blockade planet with docked ships".
  - Partially done: AIStrategyConsolidate (first, second and third cycles) and "AI avoids blockaded routes and uses
    blockades" ((1) colonization, (2) resource shipping).
- `FUTURE_FEATURES.md` lost the moved text and keeps one line per moved item, in the form
  `<name> moved to completed_features on 2026-10-01`. The unfinished remainder is untouched, including (3) to (5) and
  the Milestone note (which still says "2 is done; 3 to 5 remain").
- Classification followed the labels in the file, not the code. "Distribution centers" is described as built in
  `CLAUDE.md` but is not marked done in `FUTURE_FEATURES.md`, so it was treated as unstarted and not moved.

## Commands added (in `.claude/commands/`)

| Command | What it does |
|---|---|
| `/update_feature_list` | Does the move described above, leaving the dated placeholder lines. For a fully done feature it leaves a "moved" line and removes the lines for that feature's sub features. |
| `/handoff` | Writes `docs/handoffs/HANDOFF-<subject name>.md` (this kind of file), named after the session or, if unnamed, from its content. |

## Commits and pushes

- `0d39bce` docs: move done features from FUTURE_FEATURES.md to completed_features.md. Pushed.
- `3500c68` docs: add update_feature_list command and moved-to-completed markers. Pushed (the first attempt got a
  GitHub "Internal Server Error"; the retry worked).
- Not committed: `.claude/commands/handoff.md` and this file.

## Verified

- Running `/update_feature_list` a second time changed nothing, which is the expected result.

## Not verified

- The "done feature" rule in `/update_feature_list` (leave a line, remove the sub feature lines) has not run on real
  data, because no feature is currently fully done.
- `/handoff` was not run through the command system. This file was written by following the command file by hand.

## Gotchas

- Commands added during a session are not always picked up by that session. `/update_feature_list` ran from an older
  copy that lacked the last rule, and `/handoff` came back as "Unknown skill". Start a new session before testing a
  new or edited command.
- The user asked for `HANDOFF-` with a hyphen, matching `docs/handoffs/HANDOFF-consolidation-tuning.md` (an earlier request
  said underscore; the final decision is the hyphen).
- Do not commit unless asked. The user has been committing and pushing in separate steps.

## Next steps

1. Commit `.claude/commands/handoff.md` and this handoff file if wanted.
2. In a new session, run `/handoff` and `/update_feature_list` to confirm both load their latest text.
3. Decide whether "Distribution centers" should be marked done in `FUTURE_FEATURES.md`, and whether the Milestone note
   should be reworded now that (1) and (2) have moved.
