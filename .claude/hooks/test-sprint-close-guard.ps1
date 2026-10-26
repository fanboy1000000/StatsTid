# Test harness for sprint-close-guard.ps1
# Not a sprint-close commit itself — just invokes the hook with mocked stdin.

$ErrorActionPreference = 'Stop'

# S141 — paths resolve from THIS SCRIPT's location rather than a hardcoded
# C:\StatsTid. The hardcoded form meant the harness could not run at all from a
# checkout anywhere else, which is how a guard's own tests quietly stop being run.
$repoRoot   = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$hookPath   = Join-Path $PSScriptRoot 'sprint-close-guard.ps1'
$reviewsDir = Join-Path $repoRoot '.claude\reviews'
New-Item -ItemType Directory -Force -Path $reviewsDir | Out-Null

# S145 Step 7a cycle 6 (Opus N6-2): HEAD of THIS repository, wherever the harness is launched from; the hook
# runs at $repoRoot too (Invoke-Hook's default working directory).
$headSha = (git -C $repoRoot rev-parse HEAD).Trim()
$headShort = $headSha.Substring(0,7)
$staleSha = "deadbee"

function Invoke-Hook {
    # The hook resolves .claude/reviews, docs/sprints, the telemetry log and HEAD from its working
    # directory. S145 Step 7a (R-137): that directory is explicit, the repo root, so the hook meets the same
    # repository as $headSha wherever the harness is launched from. (R-137 added the parameter for T23-T24's
    # throwaway repository; R-139 deleted those tests and kept the explicit default.)
    param([string]$mockCommand, [string]$workDir = $repoRoot)
    $payload = @{
        tool_name = "Bash"
        tool_input = @{ command = $mockCommand }
    } | ConvertTo-Json -Depth 3 -Compress

    $tmpIn = [System.IO.Path]::GetTempFileName()
    [System.IO.File]::WriteAllText($tmpIn, $payload, [System.Text.UTF8Encoding]::new($false))

    $tmpOut = [System.IO.Path]::GetTempFileName()
    $tmpErr = [System.IO.Path]::GetTempFileName()

    $proc = Start-Process -FilePath "powershell.exe" `
        -ArgumentList "-NoProfile","-ExecutionPolicy","Bypass","-File",$hookPath `
        -WorkingDirectory $workDir `
        -RedirectStandardInput $tmpIn `
        -RedirectStandardOutput $tmpOut `
        -RedirectStandardError $tmpErr `
        -NoNewWindow -PassThru -Wait

    $exit = $proc.ExitCode
    $stderr = Get-Content $tmpErr -Raw -ErrorAction SilentlyContinue
    Remove-Item $tmpIn, $tmpOut, $tmpErr -ErrorAction SilentlyContinue
    return [PSCustomObject]@{ Exit = $exit; Stderr = $stderr }
}

$codex    = Join-Path $reviewsDir "SPRINT-99-step7a-codex.md"
$reviewer = Join-Path $reviewsDir "SPRINT-99-step7a-reviewer.md"
$waiver   = Join-Path $reviewsDir "SPRINT-99-step7a-WAIVED.md"
$ciHealthWaiver  = Join-Path $reviewsDir "SPRINT-99-ci-health-WAIVED.md"
$ciPendingWaiver = Join-Path $reviewsDir "SPRINT-99-ci-pending-WAIVED.md"
$untrackedWaiver = Join-Path $reviewsDir "SPRINT-99-untracked-WAIVED.md"
$reviewFloorWaiver = Join-Path $reviewsDir "SPRINT-99-review-floor-WAIVED.md"
# S146 - the review-floor waiver (T27-T28). A file already at this path would turn T21, T22 and T25 red: it lets
# T21's Opus fixture and T22's bracket-suffixed floor id past the model check, and a waived run writes no
# telemetry echo, which is T25's evidence. So it is saved byte for byte here, removed inside the try below, and
# restored in that try's finally.
$savedReviewFloorWaiver = $null
if (Test-Path $reviewFloorWaiver) {
    $savedReviewFloorWaiver = [System.IO.File]::ReadAllBytes($reviewFloorWaiver)
}
$mock = 'git commit -m "S99 TASK-9999: sprint close -- test"'

# S146 Step 7a cycle 3 (Codex WARNING / Opus N-5): everything from here to the results output is ONE try/finally,
# so a terminating error anywhere in the run still restores the saved waiver and removes the S99 fixtures. The
# body is deliberately not indented: the here-string sprint logs below must keep their column-0 lines (the
# ledger gate's heading match allows at most three leading spaces).
try {
if ($null -ne $savedReviewFloorWaiver) { Remove-Item $reviewFloorWaiver }

# Deterministic seams (S63 post-close gates): default the CI-health mock to
# 'success' and point the sprints dir at an empty temp dir so T1-T7 keep their
# original semantics regardless of real CI state / real sprint logs. The child
# hook process inherits these via Start-Process environment inheritance.
$tmpSprints = Join-Path $env:TEMP "statstid-guard-test-sprints"
Remove-Item $tmpSprints -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force -Path $tmpSprints | Out-Null
$env:STATSTID_CI_HEALTH_MOCK = 'success'
$env:STATSTID_SPRINTS_DIR = $tmpSprints
$env:STATSTID_UNTRACKED_MOCK = 'clean'
# S145 — the worktree-teardown gate (S143) brought its own S99 seam, but this harness never set it, so
# every test expecting exit 0 passed only on a repository with no worktrees standing and failed on one
# mid-sprint (found when T23-T24 were added: T5, T7, T9, T11, T12, T14 and T16-T20 all blocked on the
# real worktree list). Neutral default, for the same reason as the seams above: those tests are not
# about this gate. (The gate itself still has no test of its own in this harness.)
$env:STATSTID_WORKTREE_MOCK = 'clean'
# S145 Step 7a (Opus N-2; a switch since cycle 2, Codex B2-1) — telemetry is SUPPRESSED, never written:
# every S99 test whose run passes the model check would otherwise write a PASS line that in the live log
# looks like a real close. The hook echoes the suppressed line to stderr instead, which T25 uses as positive
# evidence. A switch writes nothing, so it can create no file (the cycle-1 path seam could create a waiver
# a later gate reads; T25 pins that the old variable is dead).
$env:STATSTID_TELEMETRY_SUPPRESS = '1'

# S141 — the task-ledger seam gets a neutral default for T1-T14, for the same
# reason the two seams above do: those tests are not about this gate.
#
# This default is NOT cosmetic. The harness reserves S99 as its mock sprint
# number, and a REAL docs/sprints/SPRINT-99.md exists in this repo, naming seven
# tasks and carrying no ledger — so without this, every test expecting exit 0
# would block on a genuine historical sprint log rather than on the thing under
# test. The new gate surfaced that collision the first time it ran, which is a
# fair advertisement for it: a reserved identifier that collides with real data
# is exactly the class of bookkeeping error this gate exists to catch.
$tmpLedgerLog = Join-Path $env:TEMP "statstid-guard-test-neutral-log.md"
Set-Content -Path $tmpLedgerLog -Value "# Neutral sprint log: names no task ids, so the ledger gate is not applicable." -Encoding UTF8
$env:STATSTID_SPRINTLOG_MOCK = $tmpLedgerLog

$ciPendingLine = '| **Test Verified** | yes (unit) -- Docker-gated Regression/Smoke CI-pending (engine down) |'
$cleanLine     = '| **Test Verified** | yes -- all suites green |'

$results = @()

# T1: Missing artifacts
Remove-Item $codex,$reviewer,$waiver -ErrorAction SilentlyContinue
$r = Invoke-Hook $mock
$ok = ($r.Exit -eq 2)
$results += "T1 (missing): exit=$($r.Exit) expect=2 $(if($ok){'PASS'}else{'FAIL'})"

# T2: Verdict missing
Set-Content -Path $codex -Value "no verdict here" -Encoding UTF8
Set-Content -Path $reviewer -Value "no verdict here" -Encoding UTF8
$r = Invoke-Hook $mock
$ok = ($r.Exit -eq 2)
$results += "T2 (no verdict): exit=$($r.Exit) expect=2 $(if($ok){'PASS'}else{'FAIL'})"

# T3: Verdict present, reviewed-against-commit missing
Set-Content -Path $codex -Value "verdict: APPROVED" -Encoding UTF8
Set-Content -Path $reviewer -Value "verdict: APPROVED" -Encoding UTF8
$r = Invoke-Hook $mock
$ok = ($r.Exit -eq 2)
$results += "T3 (no reviewed-sha): exit=$($r.Exit) expect=2 $(if($ok){'PASS'}else{'FAIL'})"

# T4: Stale reviewed-against-commit
# S141 — every artifact body now carries `reviewed-by-model:`. The guard began requiring it
# on the REVIEWER artifact after this harness was written, and the harness was never updated,
# so T5/T9/T11/T12/T14 had been failing for the wrong reason ever since. That is the failure
# mode the path fix above also addresses: a guard whose own tests quietly stop meaning anything.
$staleContent = "reviewed-by-model: claude-fable-5-1`nverdict: APPROVED`nreviewed-against-commit: $staleSha"
Set-Content -Path $codex -Value $staleContent -Encoding UTF8
Set-Content -Path $reviewer -Value $staleContent -Encoding UTF8
$r = Invoke-Hook $mock
$ok = ($r.Exit -eq 2)
$results += "T4 (stale sha): exit=$($r.Exit) expect=2 $(if($ok){'PASS'}else{'FAIL'})"

# T5: Valid reviewed-against-commit matches HEAD prefix
$validContent = "reviewed-by-model: claude-fable-5-1`nverdict: APPROVED`nreviewed-against-commit: $headShort"
Set-Content -Path $codex -Value $validContent -Encoding UTF8
Set-Content -Path $reviewer -Value $validContent -Encoding UTF8
$r = Invoke-Hook $mock
$ok = ($r.Exit -eq 0)
$results += "T5 (valid sha): exit=$($r.Exit) expect=0 $(if($ok){'PASS'}else{'FAIL'})"
if (-not $ok) { $results += $r.Stderr }

# T6: Waiver bypass
Set-Content -Path $waiver -Value "waiver rationale" -Encoding UTF8
Remove-Item $codex,$reviewer -ErrorAction SilentlyContinue
$r = Invoke-Hook $mock
$ok = ($r.Exit -eq 0)
$results += "T6 (waiver): exit=$($r.Exit) expect=0 $(if($ok){'PASS'}else{'FAIL'})"

# T7: Full 40-char SHA also accepted (not just short)
Set-Content -Path $codex -Value "reviewed-by-model: claude-fable-5-1`nverdict: APPROVED`nreviewed-against-commit: $headSha" -Encoding UTF8
Set-Content -Path $reviewer -Value "reviewed-by-model: claude-fable-5-1`nverdict: APPROVED`nreviewed-against-commit: $headSha" -Encoding UTF8
Remove-Item $waiver -ErrorAction SilentlyContinue
$r = Invoke-Hook $mock
$ok = ($r.Exit -eq 0)
$results += "T7 (full sha): exit=$($r.Exit) expect=0 $(if($ok){'PASS'}else{'FAIL'})"

# ── S63 post-close gates ─────────────────────────────────────────────────────
# Baseline for T8+: valid artifacts (so only the new gates are under test).
$validContent = "reviewed-by-model: claude-fable-5-1`nverdict: APPROVED`nreviewed-against-commit: $headShort"
Set-Content -Path $codex -Value $validContent -Encoding UTF8
Set-Content -Path $reviewer -Value $validContent -Encoding UTF8

# T8: CI red (mocked failure) blocks
$env:STATSTID_CI_HEALTH_MOCK = 'failure'
$r = Invoke-Hook $mock
$ok = ($r.Exit -eq 2)
$results += "T8 (ci red blocks): exit=$($r.Exit) expect=2 $(if($ok){'PASS'}else{'FAIL'})"

# T9: CI red + ci-health waiver allows
Set-Content -Path $ciHealthWaiver -Value "waiver rationale: tracked debt item X" -Encoding UTF8
$r = Invoke-Hook $mock
$ok = ($r.Exit -eq 0)
$results += "T9 (ci red + waiver): exit=$($r.Exit) expect=0 $(if($ok){'PASS'}else{'FAIL'})"
if (-not $ok) { $results += $r.Stderr }
Remove-Item $ciHealthWaiver -ErrorAction SilentlyContinue
$env:STATSTID_CI_HEALTH_MOCK = 'success'

# T10: second consecutive CI-pending close blocks
Set-Content -Path (Join-Path $tmpSprints "SPRINT-98.md") -Value $ciPendingLine -Encoding UTF8
Set-Content -Path (Join-Path $tmpSprints "SPRINT-99.md") -Value $ciPendingLine -Encoding UTF8
$r = Invoke-Hook $mock
$ok = ($r.Exit -eq 2)
$results += "T10 (2x ci-pending blocks): exit=$($r.Exit) expect=2 $(if($ok){'PASS'}else{'FAIL'})"

# T11: second consecutive CI-pending + waiver allows
Set-Content -Path $ciPendingWaiver -Value "waiver rationale: debt clears in S100" -Encoding UTF8
$r = Invoke-Hook $mock
$ok = ($r.Exit -eq 0)
$results += "T11 (2x ci-pending + waiver): exit=$($r.Exit) expect=0 $(if($ok){'PASS'}else{'FAIL'})"
if (-not $ok) { $results += $r.Stderr }
Remove-Item $ciPendingWaiver -ErrorAction SilentlyContinue

# T12: FIRST CI-pending close (previous sprint clean) allows without waiver;
# narrative "CI-pending" text elsewhere in the previous log must NOT trigger.
Set-Content -Path (Join-Path $tmpSprints "SPRINT-98.md") -Value @($cleanLine, "Narrative mention: the S97 Docker-gated tests were CI-pending back then.") -Encoding UTF8
$r = Invoke-Hook $mock
$ok = ($r.Exit -eq 0)
$results += "T12 (1x ci-pending allows): exit=$($r.Exit) expect=0 $(if($ok){'PASS'}else{'FAIL'})"
if (-not $ok) { $results += $r.Stderr }

# ── FAIL-003 untracked-source gate ──────────────────────────────────────────
# T13: untracked source files (mocked) block
$env:STATSTID_UNTRACKED_MOCK = "tests/Fake.Tests/NewGateTests.cs`nsrc/Fake/NewThing.cs"
$r = Invoke-Hook $mock
$ok = ($r.Exit -eq 2)
$results += "T13 (untracked blocks): exit=$($r.Exit) expect=2 $(if($ok){'PASS'}else{'FAIL'})"

# T14: untracked source files + waiver allows
Set-Content -Path $untrackedWaiver -Value "waiver rationale: files X/Y stay uncommitted because Z" -Encoding UTF8
$r = Invoke-Hook $mock
$ok = ($r.Exit -eq 0)
$results += "T14 (untracked + waiver): exit=$($r.Exit) expect=0 $(if($ok){'PASS'}else{'FAIL'})"
if (-not $ok) { $results += $r.Stderr }
Remove-Item $untrackedWaiver -ErrorAction SilentlyContinue
$env:STATSTID_UNTRACKED_MOCK = 'clean'

# ---------------------------------------------------------------------------
# T15-T19 — the TASK-LEDGER gate (S141). A plan that names tasks must account
# for every one of them. S141 closed with a whole wave never dispatched.
# ---------------------------------------------------------------------------
$ledgerWaiver = Join-Path $reviewsDir "SPRINT-99-ledger-WAIVED.md"
$mockLog = Join-Path $env:TEMP "statstid-guard-test-SPRINT-99.md"
$env:STATSTID_SPRINTLOG_MOCK = $mockLog

# Artifacts valid for all of T15-T19 (we are testing the ledger, not the others).
Set-Content -Path $codex    -Value "verdict: APPROVED`nreviewed-against-commit: $headShort" -Encoding UTF8
Set-Content -Path $reviewer -Value "reviewed-by-model: claude-fable-5-1`nverdict: APPROVED`nreviewed-against-commit: $headShort" -Encoding UTF8

# T15: tasks named, NO ledger section at all -> block
Set-Content -Path $mockLog -Encoding UTF8 -Value @"
# Sprint 99
Wave 1: TASK-9901 does a thing. Wave 2: TASK-9902 does another.
"@
$r = Invoke-Hook $mock
$ok = ($r.Exit -eq 2)
$results += "T15 (tasks, no ledger): exit=$($r.Exit) expect=2 $(if($ok){'PASS'}else{'FAIL'})"
if (-not $ok) { $results += $r.Stderr }

# T16: ledger present but one task unaccounted -> block (the S141 failure exactly)
Set-Content -Path $mockLog -Encoding UTF8 -Value @"
# Sprint 99
Wave 1: TASK-9901. Wave 2: TASK-9902.
## Task ledger
| Task | Disposition | Note |
|---|---|---|
| TASK-9901 | DONE | merged |
"@
$r = Invoke-Hook $mock
$ok = ($r.Exit -eq 2 -and $r.Stderr -match 'TASK-9902')
$results += "T16 (one unaccounted): exit=$($r.Exit) expect=2 names-9902=$($r.Stderr -match 'TASK-9902') $(if($ok){'PASS'}else{'FAIL'})"
if (-not $ok) { $results += $r.Stderr }

# T17: every task accounted for -> allow
Set-Content -Path $mockLog -Encoding UTF8 -Value @"
# Sprint 99
Wave 1: TASK-9901. Wave 2: TASK-9902.
## Task ledger
| Task | Disposition | Note |
|---|---|---|
| TASK-9901 | DONE | merged |
| TASK-9902 | DONE | merged |
"@
$r = Invoke-Hook $mock
$ok = ($r.Exit -eq 0)
$results += "T17 (all accounted): exit=$($r.Exit) expect=0 $(if($ok){'PASS'}else{'FAIL'})"
if (-not $ok) { $results += $r.Stderr }

# T18: CUT is a first-class disposition, not a failure -> allow
Set-Content -Path $mockLog -Encoding UTF8 -Value @"
# Sprint 99
Wave 1: TASK-9901. Wave 3: TASK-9902.
## Task ledger
| Task | Disposition | Note |
|---|---|---|
| TASK-9901 | DONE | merged |
| TASK-9902 | CUT | per the pre-declared cut order |
"@
$r = Invoke-Hook $mock
$ok = ($r.Exit -eq 0)
$results += "T18 (CUT allowed): exit=$($r.Exit) expect=0 $(if($ok){'PASS'}else{'FAIL'})"
if (-not $ok) { $results += $r.Stderr }

# T19: unaccounted task BUT an explicit waiver -> allow
Set-Content -Path $mockLog -Encoding UTF8 -Value @"
# Sprint 99
Wave 1: TASK-9901. Wave 2: TASK-9902.
## Task ledger
| TASK-9901 | DONE | merged |
"@
Set-Content -Path $ledgerWaiver -Value "waiver rationale: this sprint tracks tasks elsewhere because Z" -Encoding UTF8
$r = Invoke-Hook $mock
$ok = ($r.Exit -eq 0)
$results += "T19 (unaccounted + waiver): exit=$($r.Exit) expect=0 $(if($ok){'PASS'}else{'FAIL'})"
if (-not $ok) { $results += $r.Stderr }
Remove-Item $ledgerWaiver -ErrorAction SilentlyContinue
Remove-Item $mockLog -ErrorAction SilentlyContinue
Remove-Item Env:\STATSTID_SPRINTLOG_MOCK -ErrorAction SilentlyContinue

# ---------------------------------------------------------------------------
# T20-T21 — the reviewed-by-model comparison ignores a context-window suffix
# (2026-09-25). A self-report can read `claude-opus-5-5[1m]`; the `[1m]` is not
# part of the id, and a literal compare would block a valid close on it. The
# strip must not over-accept: a suffixed WRONG model still blocks.
# T20 is the discriminating test (fails without the strip). T21 is a negative CONTROL, not a test of the
# strip: an unstripped `claude-opus-5-5[1m]` fails the compare just the same. T22 pins the tightened shape.
# T21 is also the harness's only RUN-TIME check that the floor refuses another model, for its one fixture:
# Opus (with a context suffix) on the reserved S99 (S145 Step 7a cycle 8, NOTE 3). An exception that let
# that fixture through would turn it red; one scoped to another sprint number, or admitting another model,
# would not (S145's did not). T26 adds a static, token-based pin on the model-check block (see its comment for
# what it does not see), and any change to the hook requires its own Step 7a review. Keep both tests.
# The ledger gate runs after the model check, so give it a fully accounted log.
# ---------------------------------------------------------------------------
$env:STATSTID_SPRINTLOG_MOCK = $mockLog
Set-Content -Path $mockLog -Encoding UTF8 -Value @"
# Sprint 99
Wave 1: TASK-9901.
## Task ledger
| Task | Disposition | Note |
|---|---|---|
| TASK-9901 | DONE | merged |
"@
Set-Content -Path $codex    -Value "verdict: APPROVED`nreviewed-against-commit: $headShort" -Encoding UTF8
Set-Content -Path $reviewer -Value "reviewed-by-model: claude-fable-5-1[1m]`nverdict: APPROVED`nreviewed-against-commit: $headShort" -Encoding UTF8
$r = Invoke-Hook $mock
$ok = ($r.Exit -eq 0)
$results += "T20 (floor + [1m] suffix): exit=$($r.Exit) expect=0 $(if($ok){'PASS'}else{'FAIL'})"
if (-not $ok) { $results += $r.Stderr }

Set-Content -Path $reviewer -Value "reviewed-by-model: claude-opus-5-5[1m]`nverdict: APPROVED`nreviewed-against-commit: $headShort" -Encoding UTF8
$r = Invoke-Hook $mock
$ok = ($r.Exit -eq 2 -and $r.Stderr -match 'claude-opus-5-5')
$results += "T21 (wrong model + suffix blocks): exit=$($r.Exit) expect=2 $(if($ok){'PASS'}else{'FAIL'})"
if (-not $ok) { $results += $r.Stderr }

# T22: only the context-window shape is stripped; an arbitrary bracket suffix on the floor id still blocks
Set-Content -Path $reviewer -Value "reviewed-by-model: claude-fable-5-1[garbage]`nverdict: APPROVED`nreviewed-against-commit: $headShort" -Encoding UTF8
$r = Invoke-Hook $mock
$ok = ($r.Exit -eq 2 -and $r.Stderr -match '\[garbage\]')
$results += "T22 (floor + invalid suffix blocks): exit=$($r.Exit) expect=2 $(if($ok){'PASS'}else{'FAIL'})"
if (-not $ok) { $results += $r.Stderr }
Remove-Item $mockLog -ErrorAction SilentlyContinue
Remove-Item Env:\STATSTID_SPRINTLOG_MOCK -ErrorAction SilentlyContinue

# T23-T24 (removed). They pinned the S145-only review-model exception (owner ruling R-104), which let the
# S145 close accept an Opus review while Fable was on its usage limit. The tests were deleted before that
# close by owner ruling R-139 (2026-10-21; docs/sprints/SPRINT-145.md § "C-3 — Step 7a", cycle 7): every
# run had passed, a mutation check showed T23 fails without the exception, seven review cycles had examined
# them, and at the time of R-139, from cycle 5 on, no review finding concerned the exception itself. The
# exception was deleted from the hook after the S145 close. What guards the floor now: T21 (its S99 Opus
# fixture, at run time), T26 (a static, token-based pin on the model-check block), and the Step 7a
# review that any change to the hook requires, for everything else. T25 keeps its number so the records that
# cite it stay valid.

# T25 (S145 Step 7a cycle 2, Codex B2-1; strengthened at cycle 3, Codex W / Opus N3-1): the telemetry seam
# cannot create or activate a waiver. The cycle-1 seam took a PATH (STATSTID_TELEMETRY_LOG_MOCK) and
# Add-Content created any file there, so pointing it at SPRINT-<N>-ci-health-WAIVED.md made the next gate
# skip CI health. The seam is now a switch. This pins (a) that the hook no longer reads the old variable at
# all, and (b) at run time, with the old variable set to the S99 ci-health waiver path: no waiver appears,
# and the run POSITIVELY passes the model check (the S99 echo) and reaches the CI-health gate (its mocked
# line), so the absence of the waiver line is not mistaken for evidence on its own.
$hookSource = Get-Content -Path $hookPath -Raw
$noPathSeam = -not ($hookSource -match 'STATSTID_TELEMETRY_LOG_MOCK')
Remove-Item $ciHealthWaiver -ErrorAction SilentlyContinue
$validS99 = "reviewed-by-model: claude-fable-5-1`nverdict: APPROVED`nreviewed-against-commit: $headShort"
Set-Content -Path $codex -Value $validS99 -Encoding UTF8
Set-Content -Path $reviewer -Value $validS99 -Encoding UTF8
$env:STATSTID_TELEMETRY_LOG_MOCK = $ciHealthWaiver
try {
    $r = Invoke-Hook $mock
    $noWaiver   = -not (Test-Path $ciHealthWaiver)
    $notWaived  = -not ($r.Stderr -match 'has a CI-health waiver')
    $echoS99    = $r.Stderr -match 'telemetry suppressed \(test seam\): \S+ \| close \| S99 \| claude-fable-5-1 \| PASS'
    $ciMocked   = $r.Stderr -match 'CI-health gate using MOCKED conclusion'
    $ok = $noPathSeam -and $noWaiver -and $notWaived -and $echoS99 -and $ciMocked
    $results += "T25 (telemetry seam cannot create a waiver): no-path-seam=$noPathSeam no-waiver=$noWaiver no-waiver-line=$notWaived s99-echo=$echoS99 ci-gate-reached=$ciMocked $(if($ok){'PASS'}else{'FAIL'})"
    if (-not $ok) { $results += $r.Stderr }
} finally {
    Remove-Item Env:\STATSTID_TELEMETRY_LOG_MOCK -ErrorAction SilentlyContinue
    Remove-Item $ciHealthWaiver -ErrorAction SilentlyContinue
}

# T26 (S145 post-close review: added at cycle 1, reworked at cycles 2-4, rebuilt on PowerShell's tokenizer by
# owner ruling R-141 after cycle 5, its claims matched to its checks by R-142 after cycle 6 and R-143 after cycle 7) - a static
# REGRESSION PIN on the hook's model-check block. It is not a proof. T21 runs only the reserved S99 with its one
# Opus fixture, so an exception scoped to another sprint number leaves T21 green: S145's did, through K7 and K8,
# and only T23 (now deleted) ever discriminated on it.
#
# It reads the block (from the "# Model-routing check" comment to the "# Staleness check" comment, each exactly
# once, in that order) through PowerShell's tokenizer (PSParser) and looks at these token types only; comments
# are ignored. Because it reads tokens, a variable's spelling (`$sprintNum`, `${sprintNum}`, `$script:sprintNum`)
# does not matter for the checks below, nor do a list operator's spacing or typographic dash. (The floor compare
# must be `-ne` in any letter case; a typographic dash, `-cne`, `-ine` or any other form fails as floor-compare.) It fails on:
#   - Variable tokens: `sprintNum` in any spelling or case; a scope prefix (script, global, local, private,
#     using, variable); an environment variable other than the one telemetry switch, which must itself appear
#     exactly once; the name `acceptedReviewModels`;
#   - Command tokens: the *-Variable cmdlets and their Windows PowerShell aliases (gv, sv, set, nv, rv, clv),
#     with or without a module prefix (`Microsoft.PowerShell.Utility\Get-Variable`);
#   - Member tokens: PSVariable, Get/SetEnvironmentVariable and GetEnvironmentVariables;
#   - Operator tokens: -contains or -in, in any case or negated form;
#   - String tokens, searched ignoring case: one mentioning `sprintNum` other than the two message strings (the
#     BLOCKING message exactly twice, the telemetry line exactly once), and any naming a model (`claude-`)
#     other than the floor literal, which must be the value of the one `$reviewFloor =` assignment;
#   - counts other than one `$reviewFloor =`, one `$reviewedBy =` (each a Variable token followed by an
#     assignment operator), and one complete `if ($reviewedBy -ne $reviewFloor)` (its six tokens, through the
#     closing bracket).
# What it does NOT see, so an exception written these ways passes it:
#   - inside the block, the same names in another token position: inside a double-quoted string's `$( )` (the
#     tokenizer returns the whole string as one String token, searched only for the two names above), as a
#     command argument (`Get-Item env:X`, `variable:sprintNum`), or given as a string (`& 'gv'`,
#     `.('PSVariable')`), or a member not listed (`ExpandEnvironmentVariables`);
#   - inside the block, also: variables that embed the sprint number (for example $command, $artifact,
#     $reviewer, $codex, $waiver, and $tline once built); the BODY of the floor compare (a conditional `exit 2`);
#     a pattern test on the model that does not spell a model id (`-like 'claude*'`, `-match 'opus'`) or one
#     assembled from pieces ('claude' + '-opus-5-5'); an exception keyed on something else (the artifact's text,
#     the date, a file); the right-hand side of the one `$reviewedBy =` (what is assigned to the reviewed model is
#     not inspected); a multiple assignment (`$reviewedBy, $x = ...`); code assembled from strings and run
#     with Invoke-Expression (`iex`), the generic way around any token check; and any other construct the
#     checks above do not name;
#   - outside the block: code before it in the same loop (for example rewriting $content) and helpers defined
#     elsewhere in the hook.
# So any change to the hook requires its own Step 7a review; that review, not this pin, is where such a change
# is meant to be caught. The pin can false-fail on a harmless edit inside the block (a reworded message fails as
# sprint-used if it still mentions the sprint variable, otherwise as sprint-lines; a second string naming a
# model as model-literal); each failure names its reason, so update the pin deliberately.
function Test-FloorBlock([string]$text) {
    $errs = $null
    $all = [System.Management.Automation.PSParser]::Tokenize($text, [ref]$errs)
    $sm = @($all | Where-Object { $_.Type -eq 'Comment' -and $_.Content.StartsWith('# Model-routing check') })
    $em = @($all | Where-Object { $_.Type -eq 'Comment' -and $_.Content.StartsWith('# Staleness check') })
    if ($sm.Count -ne 1 -or $em.Count -ne 1) { return 'markers-not-unique' }
    if ($em[0].Start -le $sm[0].Start) { return 'markers-out-of-order' }
    $t = @($all | Where-Object { $_.Start -gt $sm[0].Start -and $_.Start -lt $em[0].Start -and
                                $_.Type -notin @('Comment', 'NewLine', 'LineContinuation') })
    $msgA = 'sprint-close-guard: BLOCKING sprint S$sprintNum close commit.'
    $msgB = '$ts | close | S$sprintNum | $reviewedBy | PASS | step-7a reviewer artifact on the floor model'
    $nA = 0; $nB = 0; $nSwitch = 0; $nFloorAssign = 0; $nByAssign = 0; $nCompare = 0; $nClaude = 0
    $assignOps = @('=', '+=', '-=', '*=', '/=', '%=')
    for ($i = 0; $i -lt $t.Count; $i++) {
        $k = $t[$i]; $c = [string]$k.Content
        switch ($k.Type) {
            'Variable' {
                $scope = ''; $name = $c
                if ($c -match '^(?<s>[A-Za-z]+):(?<n>.*)$') { $scope = $Matches.s.ToLower(); $name = $Matches.n }
                if ($scope -in @('script', 'global', 'local', 'private', 'using', 'variable')) { return 'indirect-access' }
                if ($scope -eq 'env') {
                    if ($name -ne 'STATSTID_TELEMETRY_SUPPRESS') { return 'indirect-access' }
                    $nSwitch++
                }
                if ($name -eq 'sprintNum') { return 'sprint-used' }
                if ($name -eq 'acceptedReviewModels') { return 'model-list' }
                $next = if ($i + 1 -lt $t.Count) { $t[$i + 1] } else { $null }
                $isAssign = $next -and $next.Type -eq 'Operator' -and ($assignOps -contains [string]$next.Content)
                if ($isAssign -and $name -eq 'reviewFloor') {
                    $nFloorAssign++
                    $val = if ($i + 2 -lt $t.Count) { $t[$i + 2] } else { $null }
                    if (-not $val -or $val.Type -ne 'String' -or ([string]$val.Content) -notmatch '^claude-[a-z0-9.-]+$') { return 'floor-literal' }
                }
                if ($isAssign -and $name -eq 'reviewedBy') { $nByAssign++ }
                if ($name -eq 'reviewedBy' -and $next -and $next.Type -eq 'Operator' -and [string]$next.Content -eq '-ne' -and
                    $i + 2 -lt $t.Count -and $t[$i + 2].Type -eq 'Variable' -and [string]$t[$i + 2].Content -eq 'reviewFloor' -and
                    $i -ge 2 -and $t[$i - 2].Type -eq 'Keyword' -and [string]$t[$i - 2].Content -eq 'if' -and $t[$i - 1].Type -eq 'GroupStart' -and
                    $i + 3 -lt $t.Count -and $t[$i + 3].Type -eq 'GroupEnd') { $nCompare++ }
            }
            'String' {
                if ($c.IndexOf('sprintNum', [System.StringComparison]::OrdinalIgnoreCase) -ge 0) {
                    if ($c -ceq $msgA) { $nA++ } elseif ($c -ceq $msgB) { $nB++ } else { return 'sprint-used' }
                }
                if ($c.IndexOf('claude-', [System.StringComparison]::OrdinalIgnoreCase) -ge 0) { $nClaude++ }
            }
            'Operator' {
                $op = ($c -replace ('[' + [char]0x2013 + [char]0x2014 + [char]0x2015 + ']'), '-').ToLower()
                if ($op -match '^-[ci]?(not)?(contains|in)$') { return 'model-list' }
            }
            'Command' {
                if ($c -match '(^|\\)((Get|Set|New|Remove|Clear)-Variable|gv|sv|set|nv|rv|clv)$') { return 'indirect-access' }
            }
            'Member' {
                if ($c -in @('PSVariable', 'GetEnvironmentVariable', 'SetEnvironmentVariable', 'GetEnvironmentVariables')) { return 'indirect-access' }
            }
        }
    }
    if ($nA -ne 2 -or $nB -ne 1) { return 'sprint-lines' }
    if ($nSwitch -ne 1) { return 'env-switch' }
    if ($nFloorAssign -ne 1) { return 'floor-assign' }
    if ($nClaude -ne 1) { return 'model-literal' }
    if ($nByAssign -ne 1) { return 'reviewedBy-assign' }
    if ($nCompare -ne 1) { return 'floor-compare' }
    return 'ok'
}
$floorCheck = Test-FloorBlock $hookSource
$results += "T26 (the model-check block shows none of the pinned exception patterns, token-based static pin): $floorCheck $(if($floorCheck -eq 'ok'){'PASS'}else{'FAIL'})"

# ---------------------------------------------------------------------------
# T27-T28 - the review-floor waiver (S146, owner decision 2026-10-26: Fable was at its usage limit and the
# owner directed the reviews onto Opus 5.5). SPRINT-<N>-review-floor-WAIVED.md lifts the reviewer artifact's
# model check and nothing else; before it, the only way past that check was SPRINT-<N>-step7a-WAIVED.md, which
# lifts every gate.
# T27 is the discriminating test: the S99 Opus fixture that T21 blocks (here without the suffix) gets past the
# model check once the waiver exists, and fails if the hook's condition ignores the waiver. Its evidence is
# positive: the waiver line, the CI-health gate reached (only after the artifact loop), exit 0, and no
# review-floor message. It also pins that a waived close writes no telemetry: the S99 echo (T25's evidence) is
# absent, because the telemetry line lives inside the skipped check.
# T28 pins the "only": the same fixture and waiver with a stale reviewed-against-commit still blocks, with the
# staleness message for the reviewer artifact, not the review-floor one.
# The ledger gate runs after the model check, so give it the neutral log.
# ---------------------------------------------------------------------------
$env:STATSTID_SPRINTLOG_MOCK = $tmpLedgerLog
Set-Content -Path $codex    -Value "verdict: APPROVED`nreviewed-against-commit: $headShort" -Encoding UTF8
Set-Content -Path $reviewer -Value "reviewed-by-model: claude-opus-5-5`nverdict: APPROVED`nreviewed-against-commit: $headShort" -Encoding UTF8
Set-Content -Path $reviewFloorWaiver -Value "waiver rationale (test fixture): the Step 7a internal review ran on claude-opus-5-5 by the owner's direction" -Encoding UTF8
try {
    # T27: Opus review + review-floor waiver gets past the model check
    $r = Invoke-Hook $mock
    $waiverLine  = $r.Stderr -match 'has a review-floor waiver at .*SPRINT-99-review-floor-WAIVED\.md'
    $noFloor     = -not ($r.Stderr -match 'the review floor is')
    $ciReached   = $r.Stderr -match 'CI-health gate using MOCKED conclusion'
    $noTelemetry = -not ($r.Stderr -match 'telemetry suppressed')
    $ok = ($r.Exit -eq 0) -and $waiverLine -and $noFloor -and $ciReached -and $noTelemetry
    $results += "T27 (review-floor waiver lets an Opus review past the model check): exit=$($r.Exit) expect=0 waiver-line=$waiverLine no-floor-block=$noFloor ci-gate-reached=$ciReached no-telemetry=$noTelemetry $(if($ok){'PASS'}else{'FAIL'})"
    if (-not $ok) { $results += $r.Stderr }

    # T28: the waiver lifts only the floor check; a stale reviewed-against-commit still blocks
    Set-Content -Path $reviewer -Value "reviewed-by-model: claude-opus-5-5`nverdict: APPROVED`nreviewed-against-commit: $staleSha" -Encoding UTF8
    $r = Invoke-Hook $mock
    $waiverLine = $r.Stderr -match 'has a review-floor waiver at .*SPRINT-99-review-floor-WAIVED\.md'
    $staleBlock = ($r.Stderr -match 'Artifact is STALE') -and ($r.Stderr -match 'SPRINT-99-step7a-reviewer\.md')
    $noFloor    = -not ($r.Stderr -match 'the review floor is')
    $ok = ($r.Exit -eq 2) -and $waiverLine -and $staleBlock -and $noFloor
    $results += "T28 (review-floor waiver lifts only the floor: a stale Opus review still blocks): exit=$($r.Exit) expect=2 waiver-line=$waiverLine stale-block=$staleBlock no-floor-block=$noFloor $(if($ok){'PASS'}else{'FAIL'})"
    if (-not $ok) { $results += $r.Stderr }
} finally {
    Remove-Item $reviewFloorWaiver -ErrorAction SilentlyContinue
    Remove-Item Env:\STATSTID_SPRINTLOG_MOCK -ErrorAction SilentlyContinue
}

} finally {
    # Cleanup - the outer finally, so it runs on success and on abort alike. The saved waiver goes back FIRST, in
    # its own try, so no cleanup error can stand between it and its restore; the fixture cleanup runs in that
    # try's finally. Paths are filtered for $null: a run that aborted early has not defined all of them, and
    # Remove-Item fails on a $null path even with -ErrorAction SilentlyContinue.
    try {
        # S146: a review-floor waiver saved at the top goes back byte for byte.
        Remove-Item $reviewFloorWaiver -ErrorAction SilentlyContinue
        if ($null -ne $savedReviewFloorWaiver) { [System.IO.File]::WriteAllBytes($reviewFloorWaiver, $savedReviewFloorWaiver) }
    } finally {
        $fixtures = @($codex,$reviewer,$waiver,$ciHealthWaiver,$ciPendingWaiver,$untrackedWaiver,$ledgerWaiver,$mockLog) | Where-Object { $_ }
        if ($fixtures) { Remove-Item $fixtures -ErrorAction SilentlyContinue }
        if ($tmpSprints) { Remove-Item $tmpSprints -Recurse -Force -ErrorAction SilentlyContinue }
        if ($tmpLedgerLog) { Remove-Item $tmpLedgerLog -ErrorAction SilentlyContinue }   # S145 Step 7a cycle 3 (Opus N3-4): left behind since S141
        Remove-Item Env:\STATSTID_CI_HEALTH_MOCK -ErrorAction SilentlyContinue
        Remove-Item Env:\STATSTID_SPRINTS_DIR -ErrorAction SilentlyContinue
        Remove-Item Env:\STATSTID_UNTRACKED_MOCK -ErrorAction SilentlyContinue
        Remove-Item Env:\STATSTID_WORKTREE_MOCK -ErrorAction SilentlyContinue
        Remove-Item Env:\STATSTID_TELEMETRY_SUPPRESS -ErrorAction SilentlyContinue
        Remove-Item Env:\STATSTID_SPRINTLOG_MOCK -ErrorAction SilentlyContinue
        Remove-Item Env:\STATSTID_TELEMETRY_LOG_MOCK -ErrorAction SilentlyContinue
    }
}

Write-Output $results
$failed = $results | Where-Object { $_ -match 'FAIL' }
if ($failed) { Write-Output ""; Write-Output "FAILURES PRESENT"; exit 1 }
# S145 Step 7a (Codex W1; Opus N-D at cycle 2): a skipped test is not a passed one, and "ALL" means the
# full count of PASS lines, not merely the absence of FAIL. 26: T1-T22 and T25-T28 (T23-T24 were deleted,
# R-139; T27-T28 added in S146 for the review-floor waiver).
$skipped = @($results | Where-Object { $_ -cmatch 'SKIPPED' })   # case-sensitive: only the explicit "SKIPPED" marker counts
$passed  = @($results | Where-Object { $_ -match '^T\d+ .* PASS$' })
Write-Output ""
if ($skipped.Count -gt 0 -or $passed.Count -ne 26) { Write-Output "$($passed.Count) OF 26 PASS LINES, $($skipped.Count) SKIPPED LINE(S); NOT ALL 26 RAN"; exit 1 }
else { Write-Output "ALL 26 TESTS PASSED ($($passed.Count) PASS lines)" }
