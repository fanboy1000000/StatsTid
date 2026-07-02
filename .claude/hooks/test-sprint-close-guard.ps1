# Test harness for sprint-close-guard.ps1
# Not a sprint-close commit itself — just invokes the hook with mocked stdin.

$ErrorActionPreference = 'Stop'
$hookPath = "C:\StatsTid\.claude\hooks\sprint-close-guard.ps1"
$reviewsDir = "C:\StatsTid\.claude\reviews"

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
$staleContent = "verdict: APPROVED`nreviewed-against-commit: $staleSha"
Set-Content -Path $codex -Value $staleContent -Encoding UTF8
Set-Content -Path $reviewer -Value $staleContent -Encoding UTF8
$r = Invoke-Hook $mock
$ok = ($r.Exit -eq 2)
$results += "T4 (stale sha): exit=$($r.Exit) expect=2 $(if($ok){'PASS'}else{'FAIL'})"

# T5: Valid reviewed-against-commit matches HEAD prefix
$validContent = "verdict: APPROVED`nreviewed-against-commit: $headShort"
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
Set-Content -Path $codex -Value "verdict: APPROVED`nreviewed-against-commit: $headSha" -Encoding UTF8
Set-Content -Path $reviewer -Value "verdict: APPROVED`nreviewed-against-commit: $headSha" -Encoding UTF8
Remove-Item $waiver -ErrorAction SilentlyContinue
$r = Invoke-Hook $mock
$ok = ($r.Exit -eq 0)
$results += "T7 (full sha): exit=$($r.Exit) expect=0 $(if($ok){'PASS'}else{'FAIL'})"

# ── S63 post-close gates ─────────────────────────────────────────────────────
# Baseline for T8+: valid artifacts (so only the new gates are under test).
$validContent = "verdict: APPROVED`nreviewed-against-commit: $headShort"
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

# Cleanup
Remove-Item $codex,$reviewer,$waiver,$ciHealthWaiver,$ciPendingWaiver,$untrackedWaiver -ErrorAction SilentlyContinue
Remove-Item $tmpSprints -Recurse -Force -ErrorAction SilentlyContinue
Remove-Item Env:\STATSTID_CI_HEALTH_MOCK -ErrorAction SilentlyContinue
Remove-Item Env:\STATSTID_SPRINTS_DIR -ErrorAction SilentlyContinue
Remove-Item Env:\STATSTID_UNTRACKED_MOCK -ErrorAction SilentlyContinue

Write-Output $results
$failed = $results | Where-Object { $_ -match 'FAIL' }
if ($failed) { Write-Output ""; Write-Output "FAILURES PRESENT"; exit 1 }
Write-Output ""
Write-Output "ALL 14 TESTS PASSED"
