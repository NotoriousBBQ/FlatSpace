---
description: Move done features and sub features from FUTURE_FEATURES.md into completed_features.md
---

Create or open `completed_features.md` at the same level as `FUTURE_FEATURES.md`. Examine `FUTURE_FEATURES.md` and categorize each listed feature as:

- **done**: all listed sub features are labelled done.
- **partially done**: some sub features are labelled done and some are not.
- **unstarted**: no sub features are labelled done.

Then:

1. Move each done feature's description to `completed_features.md`, in a section called "Done".
2. If a feature placed in "Done" has elements in the "Partially done" section, consolidate those partially done sub features under that feature's heading in "Done".
3. For each partially done feature, move any sub feature marked done to a section of `completed_features.md` called "Partially done", under the feature name.
4. Do not move any sub feature that is not marked done.
5. Do not move any feature classified as unstarted.
6. Add the date to every element added to or moved into `completed_features.md`, using the date of the addition or move.

Remove moved text from `FUTURE_FEATURES.md`, leaving the unfinished remainder in place. In place of each sub feature moved to `completed_features.md`, add a line to `FUTURE_FEATURES.md` that reads `<sub feature name> moved to completed_features on <date the command is run>`. For each feature that is done, also add a line to `FUTURE_FEATURES.md` that reads `<feature name> moved to completed_features on <date the command is run>`, and remove any lines in `FUTURE_FEATURES.md` that refer to sub features of that done feature (including earlier "moved to completed_features" lines for those sub features). Do not commit unless asked.
