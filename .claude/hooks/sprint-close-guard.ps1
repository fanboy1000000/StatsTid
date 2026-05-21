# sprint-close-guard.ps1
#
# PreToolUse hook on Bash | PowerShell tools. Blocks sprint-close git-commit
# commands unless Step 7a review artifacts exist.
#
# Required artifacts per sprint N:
#   .claude/reviews/SPRINT-{N}-step7a-codex.md
#   .claude/reviews/SPRINT-{N}-step7a-reviewer.md
# Each must include a `verdict:` line.
#
# Waiver (use sparingly, document reason):
#   .claude/reviews/SPRINT-{N}-step7a-WAIVED.md
# If present, the gate allows the commit.
#
# Why this exists: post-S35 governance change (commit a094630) requires
# Codex + Reviewer dual-lens at every sprint-end. Advisory memory + WORKFLOW.md
# edits were silently bypassed at S36 close. This hook makes the gate mechanical.

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

if ($command -notmatch '(?i)TASK-\d+:\s+sprint\s+close') {
    exit 0  # commit, but not a sprint-close commit
}

# --- Extract sprint number ------------------------------------------------

$sprintNum = $null
if ($command -match '(?i)S(?<num>\d+)\s+TASK-\d+:\s+sprint\s+close') {
    $sprintNum = $matches['num']
}
if (-not $sprintNum) {
    [Console]::Error.WriteLine("sprint-close-guard: detected 'sprint close' pattern but could not extract sprint number from command.")
    [Console]::Error.WriteLine("Expected format: 'S<N> TASK-<NN>: sprint close ...'")
    exit 2
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

# All checks passed
exit 0
