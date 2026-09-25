# sprint-close-guard.ps1
#
# PreToolUse hook on Bash | PowerShell tools. Blocks sprint-close git-commit
# commands unless Step 7a review artifacts exist AND CI health is acceptable.
#
# Required artifacts per sprint N:
#   .claude/reviews/SPRINT-{N}-step7a-codex.md
#   .claude/reviews/SPRINT-{N}-step7a-reviewer.md
# Each must include a `verdict:` line and a `reviewed-against-commit:` line.
#
# Waiver (use sparingly, document reason):
#   .claude/reviews/SPRINT-{N}-step7a-WAIVED.md   -> bypasses ALL close gates
#   .claude/reviews/SPRINT-{N}-ci-health-WAIVED.md -> bypasses the CI-health gate only
#   .claude/reviews/SPRINT-{N}-ci-pending-WAIVED.md -> bypasses the consecutive-CI-pending gate only
#   .claude/reviews/SPRINT-{N}-untracked-WAIVED.md -> bypasses the untracked-source gate only
#   .claude/reviews/SPRINT-{N}-worktree-WAIVED.md  -> bypasses the worktree-teardown gate only
#
# Why this exists: post-S35 governance change (commit a094630) requires
# Codex + Reviewer dual-lens at every sprint-end. Advisory memory + WORKFLOW.md
# edits were silently bypassed at S36 close. This hook makes the gate mechanical.
#
# S63 post-close additions (2026-06-04) — two new mechanical gates:
#   (1) CI-HEALTH: the latest COMPLETED push-triggered CI run on master must not
#       have conclusion 'failure' ("you cannot close sprint N+1 on top of a red
#       sprint N"). Background: CI's regression step had been RED on every master
#       push since >= S57 with nobody reading it — all the enforced close gates
#       were local — letting a ~47-test deterministic-failure cluster accumulate
#       invisibly. Fail-OPEN on infrastructure errors (gh missing/unauthenticated/
#       network/no-runs) per this hook's best-effort convention; fail-CLOSED only
#       on a real 'failure' conclusion.
#       Test seam: $env:STATSTID_CI_HEALTH_MOCK ('success'|'failure') skips gh —
#       honored ONLY when the close commit's sprint number is the harness-reserved
#       S99 (cycle-1 Codex hardening: a leaked env var must not disable the gate).
#   (2) CONSECUTIVE-CI-PENDING: one Docker-down close may record "CI-pending" on
#       its `**Test Verified**` header line; a SECOND consecutive one requires an
#       explicit waiver. Three consecutive CI-pending closes (S61/S62/S63) is how
#       the Docker-gated suite went locally unverified for weeks. Line-anchored to
#       the Test Verified row so narrative mentions of "CI-pending" don't trigger.
#       Test seam: $env:STATSTID_SPRINTS_DIR overrides the docs/sprints directory.

$ErrorActionPreference = 'Stop'

# --- Input ----------------------------------------------------------------

$rawInput = [Console]::In.ReadToEnd()
if ([string]::IsNullOrWhiteSpace($rawInput)) {
    exit 0  # no payload, nothing to gate
}

# Strip UTF-8 BOM if present (defensive; Claude Code stdin shouldn't include one
# but the cost of tolerance is one trim and one branch)
if ($rawInput.Length -gt 0 -and $rawInput[0] -eq [char]0xFEFF) {
    $rawInput = $rawInput.Substring(1)
}

try {
    $payload = $rawInput | ConvertFrom-Json
} catch {
    # Fail-open on parse error (don't block on hook-internal bugs)
    [Console]::Error.WriteLine("sprint-close-guard: could not parse hook input as JSON; allowing")
    exit 0
}

# --- Filter to relevant tool calls ----------------------------------------

if ($payload.tool_name -ne 'Bash' -and $payload.tool_name -ne 'PowerShell') {
    exit 0
}

$command = $payload.tool_input.command
if (-not $command) {
    exit 0
}

if ($command -notmatch '(?i)git\s+commit') {
    exit 0  # not a commit attempt
}

# Close-commit detection. Broadened 2026-05-31: the original trigger required the
# rigid `TASK-\d+: sprint close` phrasing, which the S56 close commit ("S56 Work-Time
# Persistence...") did not match — so the gate silently no-op'd. We now fire on any
# `sprint close` / `sprint-close` phrasing. NOTE: message-based detection is
# fundamentally best-effort (a close commit that names no close marker can't be caught
# here). The durable backstop is `tools/check_docs.py` (CI `docs` job), whose
# sprint-inventory check fails when a sprint shipped in git history has no SPRINT-<n>.md.
# Require a WHITESPACE-separated "sprint close" (the form real close commits use,
# e.g. "S47 TASK-4705: sprint close — ..."). This deliberately does NOT match
# hyphenated identifiers like "sprint-close-guard" appearing in a commit that merely
# edits or discusses the hook — that incidental-mention over-trigger is what an earlier
# `[\s-]?` form caused.
if ($command -notmatch '(?i)sprint\s+close') {
    exit 0  # commit, but not a recognizable sprint-close commit
}

# --- Extract sprint number ------------------------------------------------

$sprintNum = $null
# Prefer the canonical "S<N> ... sprint close" shape; fall back to any S<N> token.
if ($command -match '(?i)S(?<num>\d+)[a-z]?\b.*sprint\s+close') {
    $sprintNum = $matches['num']
} elseif ($command -match '(?i)\bS(?<num>\d+)[a-z]?\b') {
    $sprintNum = $matches['num']
}
if (-not $sprintNum) {
    # The phrase "sprint close" appeared but there's no S<N> token — this is almost
    # certainly NOT a real sprint-close commit (e.g. `git commit -m "docs: explain the
    # sprint close guard"`). Don't block; a real close carries an S<N>. The CI
    # `tools/check_docs.py` sprint-inventory check is the backstop for missing logs.
    [Console]::Error.WriteLine("sprint-close-guard: 'sprint close' phrase without an 'S<N>' token; treating as a non-close commit and allowing.")
    exit 0
}

# --- Resolve artifact paths -----------------------------------------------

$reviewsDir = Join-Path (Get-Location) '.claude/reviews'
$waiver     = Join-Path $reviewsDir "SPRINT-$sprintNum-step7a-WAIVED.md"
$codex      = Join-Path $reviewsDir "SPRINT-$sprintNum-step7a-codex.md"
$reviewer   = Join-Path $reviewsDir "SPRINT-$sprintNum-step7a-reviewer.md"

# --- Waiver short-circuit -------------------------------------------------

if (Test-Path $waiver) {
    [Console]::Error.WriteLine("sprint-close-guard: S$sprintNum has explicit Step 7a waiver at $waiver -- allowing commit")
    exit 0
}

# --- Artifact presence ----------------------------------------------------

$missing = @()
if (-not (Test-Path $codex))    { $missing += $codex }
if (-not (Test-Path $reviewer)) { $missing += $reviewer }

if ($missing.Count -gt 0) {
    [Console]::Error.WriteLine("sprint-close-guard: BLOCKING sprint S$sprintNum close commit.")
    [Console]::Error.WriteLine('')
    [Console]::Error.WriteLine('Missing required Step 7a review artifacts:')
    foreach ($m in $missing) {
        [Console]::Error.WriteLine("  - $m")
    }
    [Console]::Error.WriteLine('')
    [Console]::Error.WriteLine('Per post-S35 governance (commit a094630), every sprint-close requires')
    [Console]::Error.WriteLine('external Codex review + internal Reviewer Agent review on the full sprint')
    [Console]::Error.WriteLine('diff before the close commit lands.')
    [Console]::Error.WriteLine('')
    [Console]::Error.WriteLine('Remediation:')
    [Console]::Error.WriteLine('  1. Run dual-lens Step 7a review against the sprint diff (cycle-cap 2 per lens).')
    [Console]::Error.WriteLine('  2. Save artifacts to the paths above. Each must include a "verdict:" line.')
    [Console]::Error.WriteLine('  3. Absorb any BLOCKERs in follow-up commits.')
    [Console]::Error.WriteLine('  4. Re-attempt the sprint-close commit.')
    [Console]::Error.WriteLine('')
    [Console]::Error.WriteLine('Explicit waiver (use sparingly, document reason):')
    [Console]::Error.WriteLine("  Create $waiver with rationale for the waiver.")
    exit 2
}

# --- Resolve HEAD SHA (parent of the pending close commit) ----------------
# Required for the staleness check below. If git fails (not a repo, etc.),
# fail-open per existing convention (the gate is best-effort defense).

$headSha = $null
try {
    $headSha = (git rev-parse HEAD 2>$null | Out-String).Trim()
} catch {
    [Console]::Error.WriteLine("sprint-close-guard: git rev-parse HEAD failed ($_); skipping staleness check")
}

# --- Verdict line + staleness check ---------------------------------------
#
# Stronger contract added post-S38 (after retroactive Codex review caught
# narrative-edited artifacts slipping through the gate). Each artifact MUST
# declare which commit it reviewed via a "reviewed-against-commit: <SHA>"
# line. The SHA must be a prefix of HEAD (i.e., the review must have
# reviewed the immediate predecessor of the pending close commit).
#
# This catches:
#   - Narrative-edited artifacts (verdict updated but review not re-run)
#   - Stale artifacts from cycle N when the absorption commit is cycle N+1
#   - Any close commit attempted before re-running review against latest
#
# Bookkeeping pattern: bundle all sprint-close docs (SPRINT-N.md outcomes,
# INDEX, ROADMAP, MEMORY) into the close commit itself, so the artifact's
# reviewed-against-commit equals the parent of the close commit.
# "Close polish" commits (e.g., backfilling sprint-end HEAD hash) land
# AFTER the close commit and don't trip this gate.

foreach ($artifact in @($codex, $reviewer)) {
    try {
        $content = Get-Content -Path $artifact -Raw -ErrorAction Stop
    } catch {
        [Console]::Error.WriteLine("sprint-close-guard: could not read $artifact ($_); blocking")
        exit 2
    }

    if ($content -notmatch '(?im)^\s*verdict\s*:\s*\S') {
        [Console]::Error.WriteLine("sprint-close-guard: BLOCKING sprint S$sprintNum close commit.")
        [Console]::Error.WriteLine('')
        [Console]::Error.WriteLine('Artifact present but lacks a "verdict:" line:')
        [Console]::Error.WriteLine("  $artifact")
        [Console]::Error.WriteLine('')
        [Console]::Error.WriteLine('Add a line in the form: verdict: <APPROVED | APPROVED-WITH-WARNINGS | BLOCKED | ...>')
        [Console]::Error.WriteLine('Any non-empty verdict value satisfies the gate.')
        exit 2
    }

    # Model-routing check (owner ruling 2026-09-07, WORKFLOW.md § Model Routing): the INTERNAL
    # lens must have run on the review floor model. The reviewer definition fixes it, the
    # model-routing-guard blocks a cheaper override at spawn, and the reviewer prints
    # `reviewed-by-model:` as its first line and REFUSES on the wrong model — this is the
    # fourth layer, at close: a sprint cannot close on a review that ran cheap. Applies to the
    # reviewer artifact only (the Codex artifact is the external lens; no Claude model).
    if ($artifact -eq $reviewer) {
        $reviewFloor = 'claude-fable-5-1'
        if ($content -notmatch '(?im)^\s*reviewed-by-model\s*:\s*(\S+)') {
            [Console]::Error.WriteLine("sprint-close-guard: BLOCKING sprint S$sprintNum close commit.")
            [Console]::Error.WriteLine('')
            [Console]::Error.WriteLine('Reviewer artifact lacks a "reviewed-by-model:" line:')
            [Console]::Error.WriteLine("  $artifact")
            [Console]::Error.WriteLine('')
            [Console]::Error.WriteLine("The Step 7a internal lens must run on the review floor ($reviewFloor) and say so:")
            [Console]::Error.WriteLine("  reviewed-by-model: $reviewFloor")
            [Console]::Error.WriteLine('The reviewer agent (.claude/agents/reviewer.md) prints this as its first line; copy it into the artifact.')
            exit 2
        }
        # A self-report may carry a context-window suffix (`claude-fable-5-1[1m]`); it is not part of the id.
        # Only that shape (digits + m) is stripped; `[]`, `[garbage]` or anything else still fails the compare.
        $reviewedBy = $matches[1] -replace '\[[0-9]+m\]$', ''
        if ($reviewedBy -ne $reviewFloor) {
            [Console]::Error.WriteLine("sprint-close-guard: BLOCKING sprint S$sprintNum close commit.")
            [Console]::Error.WriteLine('')
            [Console]::Error.WriteLine("Step 7a internal review ran on '$reviewedBy'; the review floor is '$reviewFloor'.")
            [Console]::Error.WriteLine("  $artifact")
            [Console]::Error.WriteLine('')
            [Console]::Error.WriteLine('Planning and review run on the newest Fable (owner rulings 2026-09-07 and 2026-09-24).')
            [Console]::Error.WriteLine('Re-run the Reviewer Agent without a cheaper model override and replace the artifact.')
            exit 2
        }
        # Telemetry (thin monitoring, owner ruling 2026-09-07): record which model the close's
        # internal review ran on, next to the spawn lines the routing guard writes. Best-effort.
        try {
            $tlog = Join-Path (Get-Location) '.claude/telemetry/model-routing.log'
            $tdir = Split-Path $tlog -Parent
            if (-not (Test-Path $tdir)) { New-Item -ItemType Directory -Path $tdir -Force | Out-Null }
            $ts = (Get-Date).ToUniversalTime().ToString('yyyy-MM-ddTHH:mm:ssZ')
            Add-Content -Path $tlog -Value "$ts | close | S$sprintNum | $reviewedBy | PASS | step-7a reviewer artifact on the floor model" -Encoding utf8
        } catch { }
    }

    # Staleness check: artifact must declare which commit was reviewed.
    # Skip if HEAD resolution failed above (fail-open per existing convention).
    if (-not $headSha) { continue }

    $reviewedSha = $null
    if ($content -match '(?im)^\s*reviewed-against-commit\s*:\s*([0-9a-fA-F]{7,40})') {
        $reviewedSha = $matches[1]
    }

    if (-not $reviewedSha) {
        [Console]::Error.WriteLine("sprint-close-guard: BLOCKING sprint S$sprintNum close commit.")
        [Console]::Error.WriteLine('')
        [Console]::Error.WriteLine('Artifact missing "reviewed-against-commit:" line:')
        [Console]::Error.WriteLine("  $artifact")
        [Console]::Error.WriteLine('')
        [Console]::Error.WriteLine('Each Step 7a artifact MUST declare which commit was reviewed via:')
        [Console]::Error.WriteLine('  reviewed-against-commit: <SHA>')
        [Console]::Error.WriteLine('')
        [Console]::Error.WriteLine('This is the post-S38 staleness check. Background: S38 retroactive')
        [Console]::Error.WriteLine('Codex review caught narrative-edited artifacts that satisfied the')
        [Console]::Error.WriteLine('"verdict line present" check without actually reflecting a run on')
        [Console]::Error.WriteLine('the current state. The reviewed-against-commit field closes that gap.')
        exit 2
    }

    if (-not $headSha.StartsWith($reviewedSha)) {
        $headShort = if ($headSha.Length -ge 7) { $headSha.Substring(0,7) } else { $headSha }
        [Console]::Error.WriteLine("sprint-close-guard: BLOCKING sprint S$sprintNum close commit.")
        [Console]::Error.WriteLine('')
        [Console]::Error.WriteLine('Artifact is STALE (reviewed against an older commit):')
        [Console]::Error.WriteLine("  $artifact")
        [Console]::Error.WriteLine('')
        [Console]::Error.WriteLine("  reviewed-against-commit: $reviewedSha")
        [Console]::Error.WriteLine("  HEAD (pending close parent):  $headShort")
        [Console]::Error.WriteLine('')
        [Console]::Error.WriteLine('Step 7a was run against an earlier sprint state; commits since then')
        [Console]::Error.WriteLine('are not covered by the review. Re-run Step 7a against HEAD before close.')
        [Console]::Error.WriteLine('')
        [Console]::Error.WriteLine('Bookkeeping pattern: bundle all close-state doc updates (SPRINT-N.md')
        [Console]::Error.WriteLine('outcomes, INDEX, ROADMAP, MEMORY) into the close commit itself, so the')
        [Console]::Error.WriteLine("artifact's reviewed-against-commit equals the parent of the close commit.")
        exit 2
    }
}

# --- CI-health gate (S63 post-close governance) -----------------------------
# The latest COMPLETED push-triggered CI run on master must not be a 'failure'.
# Fail-open on infrastructure problems; fail-closed only on a real red run.

$ciHealthWaiver = Join-Path $reviewsDir "SPRINT-$sprintNum-ci-health-WAIVED.md"
if (Test-Path $ciHealthWaiver) {
    [Console]::Error.WriteLine("sprint-close-guard: S$sprintNum has a CI-health waiver at $ciHealthWaiver -- skipping the CI-health gate")
} else {
    $ciConclusion = $null
    $ciTitle = ''
    $ciUrl = ''

    if ($env:STATSTID_CI_HEALTH_MOCK -and $sprintNum -eq '99') {
        # Test seam for test-sprint-close-guard.ps1 — deterministic, no network.
        # HARDENED (cycle-1 Codex WARNING): honored ONLY for the harness's reserved
        # sprint number S99, so a leaked/persistent env var can never silently
        # disable the gate for a real close. Loud on stderr whenever used.
        [Console]::Error.WriteLine("sprint-close-guard: CI-health gate using MOCKED conclusion '$($env:STATSTID_CI_HEALTH_MOCK)' (test seam, S99 only)")
        $ciConclusion = $env:STATSTID_CI_HEALTH_MOCK
        $ciTitle = '(mocked run)'
        $ciUrl = '(mocked)'
    } else {
        try {
            $ghJson = gh run list --branch master --event push --status completed --limit 1 --json conclusion,displayTitle,url 2>$null | Out-String
            if ($LASTEXITCODE -eq 0 -and $ghJson.Trim()) {
                $ghRuns = @($ghJson | ConvertFrom-Json)
                if ($ghRuns.Count -ge 1) {
                    $ciConclusion = $ghRuns[0].conclusion
                    $ciTitle = $ghRuns[0].displayTitle
                    $ciUrl = $ghRuns[0].url
                }
            }
        } catch {
            # gh missing / unauthenticated / network down — best-effort gate, allow.
            [Console]::Error.WriteLine("sprint-close-guard: CI-health check could not run ($_); allowing (fail-open)")
        }
    }

    if ($ciConclusion -eq 'failure') {
        [Console]::Error.WriteLine("sprint-close-guard: BLOCKING sprint S$sprintNum close commit.")
        [Console]::Error.WriteLine('')
        [Console]::Error.WriteLine('CI is RED on master (latest completed push-triggered run):')
        [Console]::Error.WriteLine("  $ciTitle")
        [Console]::Error.WriteLine("  $ciUrl")
        [Console]::Error.WriteLine('')
        [Console]::Error.WriteLine('Per S63 post-close governance: you cannot close sprint N+1 on top of a')
        [Console]::Error.WriteLine('red sprint N. A red CI that nobody reads is not enforcement (P8) — the')
        [Console]::Error.WriteLine('regression step was red on every master push >= S57 while a ~47-test')
        [Console]::Error.WriteLine('deterministic-failure cluster accumulated invisibly.')
        [Console]::Error.WriteLine('')
        [Console]::Error.WriteLine('Remediation:')
        [Console]::Error.WriteLine('  1. Inspect the failing run (gh run view <id> --log-failed).')
        [Console]::Error.WriteLine('  2. Fix the failures (or land the fix that turns master green) BEFORE close.')
        [Console]::Error.WriteLine('  3. Re-attempt the sprint-close commit once a green master run exists.')
        [Console]::Error.WriteLine('')
        [Console]::Error.WriteLine('Explicit waiver (use sparingly, document reason + the tracked debt item):')
        [Console]::Error.WriteLine("  Create $ciHealthWaiver with rationale.")
        exit 2
    }

    if (-not $ciConclusion) {
        [Console]::Error.WriteLine('sprint-close-guard: no completed master CI run found; allowing (fail-open)')
    } elseif ($ciConclusion -ne 'success') {
        # cancelled / skipped / neutral etc. — ambiguous, not a proven red; allow with a note.
        [Console]::Error.WriteLine("sprint-close-guard: latest master CI run conclusion is '$ciConclusion' (not success, not failure); allowing with this note")
    }
}

# --- Consecutive-CI-pending gate (S63 post-close governance) -----------------
# If the sprint log being closed AND the previous sprint's log BOTH carry
# "CI-pending" on their `**Test Verified**` header line, require a waiver.
# Line-anchored so narrative mentions of CI-pending elsewhere don't trigger.

$ciPendingWaiver = Join-Path $reviewsDir "SPRINT-$sprintNum-ci-pending-WAIVED.md"
# Same S99-only hardening as the CI mock: a leaked STATSTID_SPRINTS_DIR (e.g. an
# empty dir) must not be able to blind the consecutive-CI-pending check for a
# real close. Identical leak vector, identical fix.
$sprintsDir = if ($env:STATSTID_SPRINTS_DIR -and $sprintNum -eq '99') {
    [Console]::Error.WriteLine("sprint-close-guard: consecutive-CI-pending gate using OVERRIDDEN sprints dir '$($env:STATSTID_SPRINTS_DIR)' (test seam, S99 only)")
    $env:STATSTID_SPRINTS_DIR
} else {
    Join-Path (Get-Location) 'docs/sprints'
}

function Test-CiPendingTestVerifiedLine {
    param([string]$logPath)
    if (-not (Test-Path $logPath)) { return $false }  # fail-open: missing log is check_docs.py's job
    try {
        $hit = Select-String -Path $logPath -Pattern '^\|\s*\*\*Test Verified\*\*' | Select-Object -First 1
        return [bool]($hit -and $hit.Line -match '(?i)CI-pending')
    } catch {
        return $false  # fail-open on read errors
    }
}

$curSprintLog  = Join-Path $sprintsDir "SPRINT-$sprintNum.md"
$prevSprintNum = [int]$sprintNum - 1
$prevSprintLog = Join-Path $sprintsDir "SPRINT-$prevSprintNum.md"

if ((Test-CiPendingTestVerifiedLine $curSprintLog) -and (Test-CiPendingTestVerifiedLine $prevSprintLog)) {
    if (Test-Path $ciPendingWaiver) {
        [Console]::Error.WriteLine("sprint-close-guard: S$sprintNum is a second-consecutive CI-pending close but has a waiver at $ciPendingWaiver -- allowing")
    } else {
        [Console]::Error.WriteLine("sprint-close-guard: BLOCKING sprint S$sprintNum close commit.")
        [Console]::Error.WriteLine('')
        [Console]::Error.WriteLine('SECOND CONSECUTIVE CI-pending close detected:')
        [Console]::Error.WriteLine("  S$prevSprintNum log Test Verified line: CI-pending")
        [Console]::Error.WriteLine("  S$sprintNum log Test Verified line: CI-pending")
        [Console]::Error.WriteLine('')
        [Console]::Error.WriteLine('One Docker-down close is an acceptable exception; a standing exception is')
        [Console]::Error.WriteLine('how the Docker-gated suite went locally unverified across S61/S62/S63.')
        [Console]::Error.WriteLine('')
        [Console]::Error.WriteLine('Remediation:')
        [Console]::Error.WriteLine('  1. Start the Docker engine and run the Docker-gated suites before close')
        [Console]::Error.WriteLine('     (engine start + full Regression run is ~10 minutes), OR')
        [Console]::Error.WriteLine('  2. Confirm the suites green in CI for this sprint state, update the Test')
        [Console]::Error.WriteLine('     Verified line accordingly, and re-attempt, OR')
        [Console]::Error.WriteLine('  3. Create an explicit waiver (document reason + when the debt clears):')
        [Console]::Error.WriteLine("     $ciPendingWaiver")
        exit 2
    }
}

# --- Untracked-source gate (FAIL-003, post-S111 governance) ------------------
# The S111 close commit (35bcdf4) claimed the spec≡runtime gate while its three
# test files sat UNTRACKED: local build/test globs everything on disk, so every
# local run was green, but CI built the committed tree without them (regression
# 1155 = S110's, unnoticed). `git commit -a` stages modifications, never
# untracked files — new test files are exactly the class that gets missed.
# This gate blocks a close while `??` entries exist under a source root, so the
# tree that passed locally is provably the tree being committed.
# Fail-open on git errors per this hook's best-effort convention.
# Test seam: $env:STATSTID_UNTRACKED_MOCK (newline-separated paths, or 'clean')
# skips git — honored ONLY for the harness-reserved S99, same hardening as the
# other seams (a leaked env var must not blind the gate for a real close).

$untrackedWaiver = Join-Path $reviewsDir "SPRINT-$sprintNum-untracked-WAIVED.md"
if (Test-Path $untrackedWaiver) {
    [Console]::Error.WriteLine("sprint-close-guard: S$sprintNum has an untracked-source waiver at $untrackedWaiver -- skipping the untracked-source gate")
} else {
    $untrackedSource = @()
    if ($env:STATSTID_UNTRACKED_MOCK -and $sprintNum -eq '99') {
        [Console]::Error.WriteLine("sprint-close-guard: untracked-source gate using MOCKED status (test seam, S99 only)")
        if ($env:STATSTID_UNTRACKED_MOCK -ne 'clean') {
            $untrackedSource = @($env:STATSTID_UNTRACKED_MOCK -split "`n" | Where-Object { $_.Trim() })
        }
    } else {
        try {
            $porcelain = git status --porcelain -- src tests tools frontend 2>$null
            if ($LASTEXITCODE -eq 0 -and $porcelain) {
                $untrackedSource = @($porcelain | Where-Object { $_ -match '^\?\?' } | ForEach-Object { $_.Substring(3) })
            }
        } catch {
            [Console]::Error.WriteLine("sprint-close-guard: untracked-source check could not run ($_); allowing (fail-open)")
        }
    }

    if ($untrackedSource.Count -gt 0) {
        [Console]::Error.WriteLine("sprint-close-guard: BLOCKING sprint S$sprintNum close commit.")
        [Console]::Error.WriteLine('')
        [Console]::Error.WriteLine('UNTRACKED files exist under a source root (src/tests/tools/frontend):')
        foreach ($f in $untrackedSource) {
            [Console]::Error.WriteLine("  ?? $f")
        }
        [Console]::Error.WriteLine('')
        [Console]::Error.WriteLine('Local build/test globs everything on disk, so local green does NOT prove')
        [Console]::Error.WriteLine('these files are in the commit — CI builds only the committed tree. This is')
        [Console]::Error.WriteLine('FAIL-003: the S111 spec≡runtime gate files were verified locally, claimed')
        [Console]::Error.WriteLine('in the close commit, and absent from CI for two days.')
        [Console]::Error.WriteLine('')
        [Console]::Error.WriteLine('Remediation:')
        [Console]::Error.WriteLine('  1. git add the files into the close commit (the usual case), OR')
        [Console]::Error.WriteLine('  2. delete/relocate files that do not belong in the repo, OR')
        [Console]::Error.WriteLine('  3. create an explicit waiver documenting why they legitimately stay uncommitted:')
        [Console]::Error.WriteLine("     $untrackedWaiver")
        exit 2
    }
}

# --- Worktree-teardown gate (S141-owed, built S143 / TASK-14309) -------------
# WHY THIS EXISTS. S141 named worktree teardown as owed work and did not gate it;
# S142 then closed with TWENTY-FOUR worktrees standing, found only because
# `git status` had slowed to 0.47s. The lesson that sprint recorded about its own
# close guard applies to this line of it: work that is named but not gated does
# not get done. It has now cost two sprints, which is why it is mechanical rather
# than a checklist item.
#
# What a leftover worktree costs: every one holds a branch ref and a full index,
# so `git status` degrades repo-wide; a stale worktree can still be edited by a
# later agent that thinks it is fresh; and a branch that looks merged may be
# holding commits that never reached master. The gate blocks the close until the
# sprint's worktrees are provably gone.
#
# Fail-OPEN on git errors, per this hook's best-effort convention: a broken git
# must not make closing impossible. Fail-CLOSED only on a real, enumerated
# leftover.
#
# Test seam: $env:STATSTID_WORKTREE_MOCK (newline-separated paths, or 'clean')
# skips git — honored ONLY for the harness-reserved S99, the same hardening every
# other seam in this file carries. A leaked env var must not blind a real close.

$worktreeWaiver = Join-Path $reviewsDir "SPRINT-$sprintNum-worktree-WAIVED.md"
if (Test-Path $worktreeWaiver) {
    [Console]::Error.WriteLine("sprint-close-guard: S$sprintNum has a worktree waiver at $worktreeWaiver -- skipping the worktree-teardown gate")
} else {
    $leftoverWorktrees = @()
    if ($env:STATSTID_WORKTREE_MOCK -and $sprintNum -eq '99') {
        [Console]::Error.WriteLine("sprint-close-guard: worktree-teardown gate using MOCKED list (test seam, S99 only)")
        if ($env:STATSTID_WORKTREE_MOCK -ne 'clean') {
            $leftoverWorktrees = @($env:STATSTID_WORKTREE_MOCK -split "`n" | Where-Object { $_.Trim() })
        }
    } else {
        try {
            # --porcelain emits a `worktree <path>` line per entry, MAIN CHECKOUT FIRST.
            # Dropping exactly the first is what makes this "extra worktrees", not "any".
            $wtLines = git worktree list --porcelain 2>$null
            if ($LASTEXITCODE -eq 0 -and $wtLines) {
                $paths = @($wtLines | Where-Object { $_ -match '^worktree ' } | ForEach-Object { $_.Substring(9) })
                if ($paths.Count -gt 1) {
                    $leftoverWorktrees = @($paths | Select-Object -Skip 1)
                }
            }
        } catch {
            [Console]::Error.WriteLine("sprint-close-guard: worktree check could not run ($_); allowing (fail-open)")
        }
    }

    if ($leftoverWorktrees.Count -gt 0) {
        [Console]::Error.WriteLine("sprint-close-guard: BLOCKING sprint S$sprintNum close commit.")
        [Console]::Error.WriteLine('')
        [Console]::Error.WriteLine("$($leftoverWorktrees.Count) worktree(s) still exist beyond the main checkout:")
        foreach ($w in $leftoverWorktrees) {
            [Console]::Error.WriteLine("  $w")
        }
        [Console]::Error.WriteLine('')
        [Console]::Error.WriteLine('A sprint does not close with its worktrees standing. Each one holds a branch')
        [Console]::Error.WriteLine('ref and a full index, so `git status` degrades repo-wide; a stale worktree can')
        [Console]::Error.WriteLine('be picked up by a later agent that believes it is fresh; and a branch that')
        [Console]::Error.WriteLine('looks merged may still hold commits that never reached master.')
        [Console]::Error.WriteLine('S142 closed with 24 of them, noticed only because `git status` took 0.47s.')
        [Console]::Error.WriteLine('')
        [Console]::Error.WriteLine('Remediation:')
        [Console]::Error.WriteLine('  1. Confirm each branch is MERGED before removing it:')
        [Console]::Error.WriteLine('       git branch --merged master')
        [Console]::Error.WriteLine('  2. git worktree remove <path>   (add --force only for a dirty tree you have read)')
        [Console]::Error.WriteLine('  3. git worktree prune && git branch -d <branch>')
        [Console]::Error.WriteLine('  4. Or, if a worktree legitimately outlives this sprint, waive it explicitly:')
        [Console]::Error.WriteLine("       $worktreeWaiver")
        exit 2
    }
}

# --- Task-ledger gate (S141 post-close governance) ---------------------------
# WHY THIS EXISTS. S141's plan named seventeen tasks across four waves. Waves 1,
# 2, 3 and 3b were dispatched; WAVE 4 WAS NEVER DISPATCHED, and the sprint closed
# without it. Nothing noticed for three hours, until a route-coverage E2E guard
# fired on a SYMPTOM (two pages unregistered) — the task itself, which carried
# that exact job annotated "(S140's first CI red)", was simply forgotten.
#
# Every other gate in this hook checks the WORK. This one checks the COORDINATOR:
# a plan that enumerates tasks must account for every one of them before it can
# close. Three things turned S141's close red and all three were of this class —
# a wave never dispatched, four freshness markers never bumped, a registry
# decision never made. None was a code defect; the dual-lens review caught
# everything in the code and cannot see a task that was never handed to it.
#
# WHAT "ACCOUNTED FOR" MEANS. Each TASK-<n> mentioned in the sprint log must
# appear in a "Task ledger" section with an explicit disposition: DONE, CUT,
# DEFERRED or DROPPED. CUT is a first-class outcome, not a failure — a sprint
# with a pre-declared cut order needs somewhere to record that the order was
# actually used, which also makes the cut order enforceable rather than
# aspirational. The gate never judges WHICH disposition; it only refuses silence.
#
# Test seam: $env:STATSTID_SPRINTLOG_MOCK (a path) — honored ONLY for the
# harness-reserved S99, same hardening as the other seams.

$ledgerWaiver = Join-Path $reviewsDir "SPRINT-$sprintNum-ledger-WAIVED.md"
if (Test-Path $ledgerWaiver) {
    [Console]::Error.WriteLine("sprint-close-guard: S$sprintNum has a task-ledger waiver at $ledgerWaiver -- skipping the task-ledger gate")
} else {
    $sprintLog = Join-Path (Get-Location) "docs/sprints/SPRINT-$sprintNum.md"
    if ($env:STATSTID_SPRINTLOG_MOCK -and $sprintNum -eq '99') {
        [Console]::Error.WriteLine("sprint-close-guard: task-ledger gate using MOCKED sprint log (test seam, S99 only)")
        $sprintLog = $env:STATSTID_SPRINTLOG_MOCK
    }

    if (-not (Test-Path $sprintLog)) {
        # Fail-open: a missing sprint log is already the docs job's hard failure
        # (tools/check_docs.py sprint-inventory). Blocking here too would only
        # duplicate that signal with a worse message.
        [Console]::Error.WriteLine("sprint-close-guard: no sprint log at $sprintLog; skipping the task-ledger gate (the docs job owns that failure)")
    } else {
        $logText = ''
        try {
            $logText = Get-Content -Path $sprintLog -Raw -ErrorAction Stop
        } catch {
            [Console]::Error.WriteLine("sprint-close-guard: could not read $sprintLog ($_); allowing (fail-open)")
        }

        if ($logText) {
            # Every distinct TASK-<digits> the log mentions anywhere.
            $planned = @([regex]::Matches($logText, 'TASK-(\d+)') | ForEach-Object { $_.Value } | Sort-Object -Unique)

            if ($planned.Count -eq 0) {
                [Console]::Error.WriteLine("sprint-close-guard: S$sprintNum names no TASK ids; task-ledger gate not applicable")
            } else {
                # The ledger section: from a "Task ledger" heading to the next
                # top-or-second-level heading, or end of file.
                $ledgerText = ''
                $ledgerMatch = [regex]::Match(
                    $logText,
                    '(?ims)^\s{0,3}#{1,3}\s*task\s+ledger\b.*?(?=^\s{0,3}#{1,2}\s|\z)')
                if ($ledgerMatch.Success) { $ledgerText = $ledgerMatch.Value }

                if (-not $ledgerText) {
                    [Console]::Error.WriteLine("sprint-close-guard: BLOCKING sprint S$sprintNum close commit.")
                    [Console]::Error.WriteLine('')
                    [Console]::Error.WriteLine("The sprint log names $($planned.Count) task(s) but has no 'Task ledger' section:")
                    [Console]::Error.WriteLine("  $sprintLog")
                    [Console]::Error.WriteLine('')
                    [Console]::Error.WriteLine('Add a section like:')
                    [Console]::Error.WriteLine('')
                    [Console]::Error.WriteLine('  ## Task ledger')
                    [Console]::Error.WriteLine('  | Task | Disposition | Note |')
                    [Console]::Error.WriteLine('  |---|---|---|')
                    [Console]::Error.WriteLine('  | TASK-1234 | DONE | merged in wave 1 |')
                    [Console]::Error.WriteLine('  | TASK-1235 | CUT | per the pre-declared cut order |')
                    [Console]::Error.WriteLine('')
                    [Console]::Error.WriteLine('Dispositions: DONE | CUT | DEFERRED | DROPPED. CUT is a first-class')
                    [Console]::Error.WriteLine('outcome, not a failure — a pre-declared cut order needs somewhere to')
                    [Console]::Error.WriteLine('record that it was used.')
                    [Console]::Error.WriteLine('')
                    [Console]::Error.WriteLine('WHY: S141 closed with a whole wave never dispatched. Every other gate')
                    [Console]::Error.WriteLine('here checks the work; this one checks that the plan was actually run.')
                    exit 2
                }

                $unaccounted = @()
                foreach ($t in $planned) {
                    $pattern = [regex]::Escape($t) + '\b.*\b(DONE|CUT|DEFERRED|DROPPED)\b'
                    if ($ledgerText -notmatch "(?im)$pattern") { $unaccounted += $t }
                }

                if ($unaccounted.Count -gt 0) {
                    [Console]::Error.WriteLine("sprint-close-guard: BLOCKING sprint S$sprintNum close commit.")
                    [Console]::Error.WriteLine('')
                    [Console]::Error.WriteLine('These tasks are named in the sprint log but have no disposition in the Task ledger:')
                    foreach ($t in $unaccounted) { [Console]::Error.WriteLine("  $t") }
                    [Console]::Error.WriteLine('')
                    [Console]::Error.WriteLine('Each needs a line carrying the task id and one of: DONE | CUT | DEFERRED | DROPPED.')
                    [Console]::Error.WriteLine('')
                    [Console]::Error.WriteLine('This is the S141 failure: a task can be planned, written down, reviewed,')
                    [Console]::Error.WriteLine('and then simply never dispatched. A task written down is not a task done.')
                    [Console]::Error.WriteLine('If one was deliberately not built, say CUT or DEFERRED and why — the gate')
                    [Console]::Error.WriteLine('never judges which disposition, only refuses silence.')
                    exit 2
                }
            }
        }
    }
}

# All checks passed
exit 0
