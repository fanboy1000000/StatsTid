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

$headSha = (git rev-parse HEAD).Trim()
$headShort = $headSha.Substring(0,7)
$staleSha = "deadbee"

function Invoke-Hook {
    param([string]$mockCommand)
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
$mock = 'git commit -m "S99 TASK-9999: sprint close -- test"'

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

# Cleanup
Remove-Item $codex,$reviewer,$waiver,$ciHealthWaiver,$ciPendingWaiver,$untrackedWaiver,$ledgerWaiver -ErrorAction SilentlyContinue
Remove-Item $tmpSprints -Recurse -Force -ErrorAction SilentlyContinue
Remove-Item Env:\STATSTID_CI_HEALTH_MOCK -ErrorAction SilentlyContinue
Remove-Item Env:\STATSTID_SPRINTS_DIR -ErrorAction SilentlyContinue
Remove-Item Env:\STATSTID_UNTRACKED_MOCK -ErrorAction SilentlyContinue

Write-Output $results
$failed = $results | Where-Object { $_ -match 'FAIL' }
if ($failed) { Write-Output ""; Write-Output "FAILURES PRESENT"; exit 1 }
Write-Output ""
Write-Output "ALL 19 TESTS PASSED"
