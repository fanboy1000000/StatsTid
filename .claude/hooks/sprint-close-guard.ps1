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

# All checks passed
exit 0
