#!/usr/bin/env bash
# SessionEnd hook: when a NAMED session (one given a title via /rename or --name) exits,
# resume it headlessly and run /handoff so a HANDOFF file is written to docs/handoffs/,
# then commit that file (and only handoff files; nothing is pushed).
# Detached from the hook because Claude Code kills SessionEnd hooks quickly on exit.
# Modes: (no args) the SessionEnd hook; --worker <session_id> the detached part;
#        --commit-only commits any new or changed docs/handoffs/HANDOFF-*.md right now.
# Log: .claude/hooks/handoff-on-exit.log

CLAUDE_PROJECT_DIR="${CLAUDE_PROJECT_DIR:-$(cd "$(dirname "$0")/../.." && pwd)}"
log="$CLAUDE_PROJECT_DIR/.claude/hooks/handoff-on-exit.log"
note() { printf '%s %s\n' "$(date '+%F %T')" "$*" >> "$log"; }

# Commit new or modified handoff files, and nothing else: other staged or untracked work is left alone
# (git commit --only). Never pushes. Skipped mid merge/rebase/cherry-pick.
commit_handoffs() {
  cd "$CLAUDE_PROJECT_DIR" || return 0
  local pathspec='docs/handoffs/HANDOFF-*.md'
  local gitdir; gitdir=$(git rev-parse --git-dir 2>/dev/null) || { note "commit: not a git repo"; return 0; }
  for state in MERGE_HEAD REBASE_HEAD CHERRY_PICK_HEAD rebase-merge rebase-apply; do
    [ -e "$gitdir/$state" ] && { note "commit: skipped, git is mid $state"; return 0; }
  done
  [ -z "$(git status --porcelain -- "$pathspec")" ] && { note "commit: no handoff changes"; return 0; }

  git add -- "$pathspec" >>"$log" 2>&1 || { note "commit: git add failed"; return 0; }
  local files; files=$(git diff --cached --name-only -- "$pathspec" | sed 's#.*/##' | paste -sd, -)
  [ -z "$files" ] && { note "commit: nothing staged"; return 0; }
  local msg="docs: add session handoff ($files)"
  if git commit --only -q -m "$msg" -- "$pathspec" >>"$log" 2>&1; then
    note "commit: committed $files"
  else
    note "commit: git commit failed (see above)"
  fi
}

[ "$1" = "--commit-only" ] && { commit_handoffs; exit 0; }

# The detached part: run /handoff headlessly, then commit whatever handoff file it wrote.
if [ "$1" = "--worker" ]; then
  export FLATSPACE_HANDOFF_RUNNING=1   # the headless run below ends a session too; its own hook must skip
  cd "$CLAUDE_PROJECT_DIR" || exit 0
  note "launching: claude -p /handoff --resume $2"
  claude -p "/handoff" --resume "$2" \
    --permission-mode acceptEdits \
    --allowedTools "Read" "Write" "Edit" "Glob" "Grep" "Bash(git log:*)" "Bash(git status:*)" "Bash(git diff:*)" \
    >>"$log" 2>&1 </dev/null
  note "handoff run finished: exit=$?"
  commit_handoffs
  exit 0
fi

# The headless run ends a session too; don't recurse.
[ -n "$FLATSPACE_HANDOFF_RUNNING" ] && { note "skip: nested handoff run"; exit 0; }

input=$(cat)
field() { printf '%s' "$input" | sed -n "s/.*\"$1\" *: *\"\([^\"]*\)\".*/\1/p"; }
session_id=$(field session_id)
transcript=$(field transcript_path | sed 's#\\\\#/#g')
reason=$(field reason)
note "fired: session=$session_id reason=$reason transcript=$transcript"

# Only real exits, not /clear or a switch to another session.
case "$reason" in clear|resume) note "skip: reason=$reason"; exit 0 ;; esac
[ -z "$session_id" ] && { note "skip: no session_id in hook input"; exit 0; }
[ -f "$transcript" ] || { note "skip: transcript not found"; exit 0; }

# Named = the transcript carries a custom-title entry.
grep -q '"type":"custom-title"' "$transcript" || { note "skip: unnamed session"; exit 0; }

cd "$CLAUDE_PROJECT_DIR" || exit 0
nohup bash "$CLAUDE_PROJECT_DIR/.claude/hooks/handoff-on-exit.sh" --worker "$session_id" \
  >>"$log" 2>&1 </dev/null &
exit 0
