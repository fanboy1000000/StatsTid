# [FAIL-009] The governance hooks silently did not run on a quarter of S147's agent spawns — a relative script path and a 10-second timeout, both failing open

| Field | Value |
|-------|-------|
| **ID** | FAIL-009 |
| **Category** | failure |
| **Status** | resolved (paths and timeout fixed 2026-11-05; fail-open remains by design, the fail-closed choice queued for the owner — see Residual) |
| **Sprint** | S147 (found after C-5) |
| **Date** | 2026-11-05 |
| **Domains** | CI/Tooling (the enforcement layer: `.claude/settings.json`, `.claude/hooks/*.ps1`) |
| **Tags** | tooling, hooks, governance, model-routing, sprint-close-guard, fail-open, windows, telemetry |

## Summary
The two governance guards — `model-routing-guard.ps1` (checks every Agent spawn against the routing table) and
`sprint-close-guard.ps1` (checks every Bash/PowerShell command for a close commit) — were started from
`.claude/settings.json` with a **relative** path (`powershell -File .claude/hooks/<guard>.ps1`) and a
**10-second timeout**. Both failure modes are non-blocking in Claude Code: the tool call goes ahead as if the
guard had allowed it. In S147 (2026-10-27 → 11-05) the routing guard did not run on **51 of 200** agent
spawns: 38 timed out (the guard had not finished at 10 s; Claude Code recorded the cancellations 10.1–18.4 s
after the start, under memory pressure), 13 failed with "The argument
'.claude/hooks/model-routing-guard.ps1' to the -File parameter does not exist" because the session's working
directory had moved into `.claude/reviews`. The close guard had the same two faults on 83 and 72 shell commands.
No harm resulted: every agent still ran on its definition's model (the definitions pin the model; the guard is
the second layer), and the actual close commit was checked: the session transcript records no hook failure on
it, and the log's one `close | S147 | … | PASS` line carries the close commit's own time (16:32:03Z; no earlier
blocked attempt). The line alone would not prove it: it is written mid-guard, before the later gates. But a
close commit made at an unlucky moment would have passed unchecked.

## How it surfaced
The model-routing register's S147 row (C-5) found the telemetry log's 149 `ALLOW` spawn lines (plus 1 `BLOCK`, which starts no
agent) 51 short of the 200 subagent transcripts.
Asked for pros and cons of counting from the transcripts instead, the Orchestrator looked for the gap's cause
before recommending: the guard writes its log relative to the working directory, which pointed at the session
cwd; then the hook command itself was relative; then the session transcript's own hook records
(`hook_cancelled` with `timedOut: true`, `hook_non_blocking_error` with the "-File … does not exist" stderr)
accounted for exactly the 51. Earlier rows had already called the log "a floor" (S146) without asking why.

**The trigger for the path fault** was the Orchestrator's own habit of `cd`-ing into a subfolder in the Bash
tool (`cd .claude/reviews && cat > artifact.md`, `cd .claude/worktrees/agent-x && git commit`): the Bash tool's
working directory persists, and it is the directory Claude Code starts hooks in. A further path for lost lines:
while the session sat in a worktree, the routing guard (tracked in the worktree too) could run and write to that
worktree's `.claude/telemetry`, deleted with the worktree. S147's arithmetic (149 + 38 + 13 = 200) leaves no
spawn lost that way; the session did sit in a worktree on 2026-10-23 (S146).

## Fix (2026-11-05)
- `.claude/settings.json`: each hook is `"command": "powershell.exe"` with `"args"` naming
  `${CLAUDE_PROJECT_DIR}/.claude/hooks/<guard>.ps1` (the documented form; `CLAUDE_PROJECT_DIR` is the folder the
  session started in, and is set only for hook commands), and `"timeout": 60`.
- `model-routing-guard.ps1` `Set-Location`s to `$env:CLAUDE_PROJECT_DIR` when it is set, so its log is the
  project's central one. Hand-run seam tests do not have the variable, so they keep their scratch folder.
- `sprint-close-guard.ps1`, once a command is recognised as a close attempt, works out the directory the
  commit is MADE in and moves to the top of that checkout (`git -C <dir> rev-parse --show-toplevel`). It starts
  from the hook's working directory and follows exactly two forms in the command: ONE `cd` / `Set-Location` /
  `sl` at its very start (`cd <dir> && …` or `cd <dir>; …`), then `git -C <dir>`, each relative to the one
  before; Git Bash paths (`/c/Users/...`) and `~` are converted. So from a subfolder the gates read the repo
  root; from a worktree, that worktree, as before; for `cd <other> && git commit` or `git -C <other> commit`,
  the other checkout. (Pinning it to `CLAUDE_PROJECT_DIR` would have checked the main checkout for a commit made
  elsewhere: review cycle 1, Codex W1; following the command: cycle 2, Codex W1 / Fable W-1.)
- **It blocks rather than guesses** (cycles 3–4). A close attempt is **blocked**, with the reason, when: the
  command contains more than one `git commit` (cycle 4: a `cd` before the second one would go unread); any
  other directory change appears before the commit — a second `cd`, `chdir`, `pushd`/`popd`, a subshell or
  `$( )`, a leading `cd` not joined by `&&` or `;` (e.g. followed by a newline), or even the word `cd` or `sl`
  inside quoted text or as a path segment (shell scope cannot be read reliably from text: cycle 3, Codex W1);
  the repository is named another way (`--git-dir` / `--work-tree`, a `GIT_DIR=` / `GIT_WORK_TREE=` prefix,
  `env -C`); the directory does not exist; it is outside any git checkout; or any error occurs while
  resolving (Fable W-2: .NET Framework's path parser throws on `<`, `"` or `|`, and an uncaught error would end
  the hook with exit 1, which Claude Code lets through). These false blocks are deliberate: the fix is to run
  the close command as `cd <dir> && git add … && git commit …` (body via `-m "$(cat <file>)"` after the commit)
  with nothing else that changes directory. Its `close` line goes to the central log.
- Its commit detector now also sees git global options before `commit` (`git -C <dir> commit`, `-c k=v`,
  `--no-pager`, `--git-dir .git`, which had slipped past `git\s+commit`; review notes), excludes
  `git commit-tree`, and matches each option atomically, so a long run of option-like tokens cannot make it
  backtrack exponentially (Fable cycle-2 NOTE-1: the earlier form reached 3 s at 30 tokens).
- The close-guard harness gains T29–T36: T29 `git -C <repo> commit` is detected; T30 started from a subfolder,
  the guard finds the artifacts at the checkout's top; T31 `git -C <repo> commit` and T32
  `cd /c/<repo> && git add -A && git commit`, both started outside any checkout, follow the command; T33 an
  unresolvable `-C` directory blocks; T34 a subshell `cd` blocks; T35 a close from outside any checkout blocks;
  T36 a path the parser cannot read blocks; T37 two commits in one command block; T38 a `chdir` blocks; T39 a
  `GIT_WORK_TREE=` prefix blocks. **37/37 PASS.** Each piece was removed in turn and, across all 37 tests,
  exactly these went red: the old detector → T29, T31, T33 (the last two need its `-C` capture); no
  checkout-top `Set-Location` → T30–T32; `-C` not followed → T31, T33; the leading `cd` not followed → T32,
  T36; falling back instead of blocking → T33; the strict check removed → T34, T38, T39; the multiple-commit
  block removed → T37; `chdir` dropped from the scan → T38; the `GIT_` environment check dropped → T39;
  rethrowing instead of blocking → T36. The harness's own red search now matches only a test's `FAIL` line
  (Fable NOTE-5: a label saying "failing" once read as red). Established on the final files, the guard
  restored byte-identical after each probe (git blob ids recorded in the commit that carries this entry).
  T26's static pin held: the model-check block reads no new environment variable. (Review cycle 3 of the
  internal lens was void for the close guard: the Orchestrator edited and probed it while that review ran,
  after declaring the tree frozen.)
- Verified live: with the session moved into `.claude/reviews`, a fake `S999` close commit was blocked with
  paths resolved at the root, and a test spawn's log line landed in the root log.
- The register now counts spawns from the transcripts, reconciles "log ALLOW N / transcripts M" every row, and
  treats the close line as a signal to confirm against the close commit's hook record
  (`docs/operations/model-routing-register.md`, "Where the numbers come from").

## Residual
Both hooks still **fail open**: a timeout or a start failure lets the tool call through. Claude Code offers
`"onFailure": "block"` (client 2.1.295+, per the hooks reference at code.claude.com/docs/en/hooks, read
2026-11-05; not yet exercised here) to fail closed instead; for the close guard, which runs on every shell
command, that would stop all shell work whenever PowerShell cannot start in time on this memory-constrained
machine. Whether to fail closed — on the routing guard, the close guard, both or neither — is an owner decision,
not taken here; it is queued in `ROADMAP.md`'s backlog (owner ruling 2026-11-06: keep fail-open for now and
decide at S148's close, on S148's reconciliation). Until then the register's reconciliation and close-line
check are the detective control, and S148's "log ALLOW N / transcripts M" is the first test of whether 60 s is
enough: the 60 s limit is a judgment, not a measurement (a cancelled hook leaves no completion time).

## Lesson
1. **A guard that can fail open must be observed failing.** Count what the guard *should* have seen from an
   independent record (the transcripts) and reconcile; a "floor" that nobody explains is a hole.
2. **Hook commands and the files they read must not depend on the session's working directory.** Use
   `${CLAUDE_PROJECT_DIR}`; and do not leave the session cwd in a subfolder (use absolute paths, `git -C`, or a
   subshell).
3. **Size hook timeouts for the slowest realistic start**, not the typical one, and measure whether it holds:
   PowerShell 5.1 under memory pressure had not finished within 10 s, with cancellations recorded up to 18 s
   (Agent hook) and 22 s (Bash hook) after the start.
