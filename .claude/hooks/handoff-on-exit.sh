#!/usr/bin/env bash
# SessionEnd hook: when a NAMED session (one given a title via /rename or --name) exits,
# resume it headlessly and run /handoff so a HANDOFF file is written to docs/handoffs/.
# Detached from the hook because Claude Code kills SessionEnd hooks quickly on exit.
# Log: .claude/hooks/handoff-on-exit.log

log="$CLAUDE_PROJECT_DIR/.claude/hooks/handoff-on-exit.log"
note() { printf '%s %s\n' "$(date '+%F %T')" "$*" >> "$log"; }

# The headless run below ends a session too; don't recurse.
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
export FLATSPACE_HANDOFF_RUNNING=1
note "launching: claude -p /handoff --resume $session_id"
nohup claude -p "/handoff" --resume "$session_id" \
  --permission-mode acceptEdits \
  --allowedTools "Read" "Write" "Edit" "Glob" "Grep" "Bash(git log:*)" "Bash(git status:*)" "Bash(git diff:*)" \
  >>"$log" 2>&1 </dev/null &
exit 0
