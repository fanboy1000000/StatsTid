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
$mock = 'git commit -m "S99 TASK-9999: sprint close -- test"'

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

# Cleanup
Remove-Item $codex,$reviewer,$waiver -ErrorAction SilentlyContinue

Write-Output $results
$failed = $results | Where-Object { $_ -match 'FAIL' }
if ($failed) { Write-Output ""; Write-Output "FAILURES PRESENT"; exit 1 }
Write-Output ""
Write-Output "ALL 7 TESTS PASSED"
