---
description: End the session: run feature_close, commit and push everything (no separate push approval needed), then delete the local branches confirmed merged into main
---

End the current session in three steps, in this order. Running this command is the owner's standing approval to commit AND push in step 2; no separate OK for the
push is needed here. (Outside this command the rule stays: commit freely, push only on the owner's OK.)

1. **Close out the work.** Read `.claude/commands/feature_close.md` and carry it out exactly (it reads and follows `update_feature_list.md` and `handoff.md`
   itself). Do not work from memory of an older version. Its own "do not commit or push" line does not apply here: step 2 commits what it produced.

2. **Commit and push everything.**
   - Run `git status --short`. Stage everything the session left (`git add -A`; `.superpowers/`, `Library/` and the other ignored paths stay out through `.gitignore`).
     Before committing, read the list: if a file looks like a secret, a credential, a large binary or scratch output that does not belong in the repo, leave it
     out of the commit and say so in the report instead of committing it.
   - Commit with a message that says what the session's remaining changes are (group unrelated changes into separate commits when that is natural), ending with
     the attribution line from the session's system reminder when one is present. Never skip hooks or bypass signing; if a hook fails, fix the cause and commit again.
   - Push the current branch to `origin` (set the upstream if it has none). Do not force-push. If the push is rejected, stop and report; do not rebase, merge or
     force without the owner's word. A merge of a feature branch into `main` is still the owner's call: this command pushes the branch you are on and does not merge.

3. **Delete the merged branches.**
   - Switch to `main` first only if you are already on it or the working tree is clean; otherwise skip this step and say why.
   - List the local branches confirmed merged: `git branch --merged main`. Delete every one except `main` and the branch you are on, with `git branch -d`
     (never `-D`: `-d` refuses an unmerged branch, which is the safety). A branch not in that list is not touched, whatever its name or age.
   - Remote branches are not touched. If a merged local branch still exists on `origin`, list it in the report and leave it for the owner.

When all three are done, report in a few lines: what step 1 moved and the handoff file name, the commit hashes pushed, the branches deleted, and anything left
alone (files kept out of the commit, unmerged branches, merged branches still on `origin`).
