# [FAIL-008] Bash heredocs over roughly 10 KB are truncated by the tool on this machine — use the Write tool for large files

| Field | Value |
|-------|-------|
| **ID** | FAIL-008 |
| **Category** | failure |
| **Status** | resolved (working practice) |
| **Sprint** | S138 |
| **Date** | 2026-09-03 |
| **Domains** | Tooling (the Claude Code harness on the owner's Windows/Git-Bash machine) |
| **Tags** | tooling, bash, heredoc, write-tool, agent-workflow, windows |

## Summary
Writing a long file through the Bash tool with a quoted here-document (`cat > file <<'EOF' … EOF`) fails
once the command approaches ~10 KB: the shell reports "unexpected EOF while looking for matching `'`" (or
`ENAMETOOLONG`) because the command text is truncated before the terminator, so NOTHING is written. Short
heredocs (a few KB) work fine.

## How it surfaced
The Orchestrator hit it twice in Sprint 137/138 (a knowledge-base entry and the S138 refinement, each ~15–20
KB); the TASK-13803 agent hit it independently on the worklist repository file. Each time the failure looked
like a quoting bug in the content and cost a debugging round before the size cause was recognised.

## Lesson
For any file over a few KB, or any file with mixed quoting, use the dedicated **Write** tool (it has no size
cliff and no shell quoting). Keep Bash heredocs for short snippets, scratch prompts, and awk/sed splices.
The Orchestrator's standing instruction to prefer Bash under bypass-permissions mode does not override this:
"fall back to a dedicated tool when Bash genuinely cannot do the job" applies.
