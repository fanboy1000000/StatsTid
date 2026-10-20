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
    # directory. S145 Step 7a cycle 5 (R-137): that directory is now explicit, so T23-T24 can run the hook
    # in a throwaway repository instead of the real one. Every other test runs it at the repo root.
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
# S145 — the worktree-teardown gate (S143) brought its own S99 seam, but this harness never set it, so
# every test expecting exit 0 passed only on a repository with no worktrees standing and failed on one
# mid-sprint (found when T23-T24 were added: T5, T7, T9, T11, T12, T14 and T16-T20 all blocked on the
# real worktree list). Neutral default, for the same reason as the seams above: those tests are not
# about this gate. (The gate itself still has no test of its own in this harness.)
$env:STATSTID_WORKTREE_MOCK = 'clean'
# S145 Step 7a (Opus N-2; a switch since cycle 2, Codex B2-1) — telemetry is SUPPRESSED, never written:
# every S99 test whose run passes the model check would otherwise write a PASS line that in the live log
# looks like a real close (T23 runs in a sandbox since cycle 5, so its line would land in the sandbox's
# own log, but it needs the echo all the same). The hook echoes the suppressed line to stderr
# instead, which T23 and T25 use as positive evidence. A switch writes nothing, so it can create no file
# (the cycle-1 path seam could create a waiver a later gate reads; T25 pins that the old variable is
# dead). The switch stays when T23/T24 are deleted after the S145 close: the S99 tests still need it.
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

# T23-T24 — the S145-only review-model exception (owner ruling 2026-10-20, R-104 option 2). T21 above
# already pins that the reserved S99 still refuses Opus; these two pin the exception's other edges.
#
# They run the hook in a THROWAWAY REPOSITORY under %TEMP%, never in the real one (S145 Step 7a cycle 5,
# R-137). Cycles 1-5 found one leftover-file hazard after another while these tests overwrote the real
# SPRINT-145 artifacts and restored them afterwards: a temporary waiver (cycle 2), fakes that passed the
# gate if a run was killed (cycles 3 and 4), recovery copies a re-run destroyed (cycle 4), a byte compare
# that ignored letter case (cycle 5). The hook resolves .claude/reviews, docs/sprints, the telemetry log
# and HEAD from its working directory, so a sandbox with its own git commit and its own reviews folder
# gives the same test with nothing real to overwrite. A real close runs in the real repository, so
# nothing left in a sandbox can ever reach one.
#
# Isolation (S145 Step 7a cycle 6, owner ruling R-138; Codex W1/W2 and NOTE, Opus N6-1/N6-3):
#   - the git variables that route a command to ANOTHER repository (GIT_DIR, GIT_WORK_TREE, ...) are
#     cleared for the sandbox steps and the hook runs they start, and restored afterwards;
#   - the sandbox is made from an empty template and committed with hooks pointed at an empty folder,
#     so no hook installed on this machine runs;
#   - each git step's exit code is checked, and the sandbox must answer `rev-parse --git-dir` with `.git`
#     (its own repository) before anything is committed;
#   - only sandboxes over an hour old are cleared (a run takes about two minutes), so a concurrent run's
#     live sandbox is left alone; a sandbox that cannot be removed is reported, not ignored.
#
# Every GATE seam in the hook is honoured for S99 only (the telemetry switch, which gates nothing, is the
# one any-sprint seam), so the S145 runs meet the hook's real checks. T23 stops at the reviewer's staleness
# check and T24 at its model check, both before the gates that read the repository (CI health, consecutive
# CI-pending, untracked files, worktrees, the task ledger).
$mock145 = 'git commit -m "S145 TASK-9999: sprint close -- test"'
$gitRouting = @('GIT_DIR', 'GIT_WORK_TREE', 'GIT_INDEX_FILE', 'GIT_OBJECT_DIRECTORY', 'GIT_ALTERNATE_OBJECT_DIRECTORIES', 'GIT_COMMON_DIR', 'GIT_NAMESPACE')
$savedGitEnv = @{}
foreach ($v in $gitRouting) { $savedGitEnv[$v] = [Environment]::GetEnvironmentVariable($v, 'Process') }
foreach ($old in @(Get-ChildItem -Path $env:TEMP -Directory -Filter 'statstid-guard-test-sandbox-*' -ErrorAction SilentlyContinue)) {
    if ($old.LastWriteTime -lt (Get-Date).AddHours(-1)) {
        Remove-Item $old.FullName -Recurse -Force -ErrorAction SilentlyContinue
        if (Test-Path $old.FullName) { $results += "sandbox cleanup: could not remove the abandoned sandbox $($old.FullName) (harmless: no real close reads it; remove it by hand)" }
    }
}
$sandbox = Join-Path $env:TEMP ("statstid-guard-test-sandbox-" + [guid]::NewGuid().ToString('N'))
try {
    foreach ($v in $gitRouting) { [Environment]::SetEnvironmentVariable($v, $null, 'Process') }
    $sbReviews = Join-Path $sandbox '.claude\reviews'
    $sbEmpty   = Join-Path $sandbox '.empty'   # an empty git template and hooks folder
    New-Item -ItemType Directory -Force -Path $sbReviews, $sbEmpty | Out-Null
    $sbStep = ''
    git -C $sandbox init -q "--template=$sbEmpty"
    if ($LASTEXITCODE -ne 0) { $sbStep = 'git init' }
    else {
        $sbGitDir = git -C $sandbox rev-parse --git-dir
        if ($LASTEXITCODE -ne 0 -or "$sbGitDir".Trim() -ne '.git') { $sbStep = "isolation check: rev-parse --git-dir answered '$sbGitDir', not '.git'" }
        else {
            git -C $sandbox -c "core.hooksPath=$sbEmpty" -c user.name=guard-harness -c user.email=guard-harness@invalid -c commit.gpgsign=false commit -q --allow-empty --no-verify -m "guard harness sandbox"
            if ($LASTEXITCODE -ne 0) { $sbStep = 'git commit' }
            else {
                $sbHead = git -C $sandbox rev-parse HEAD
                if ($LASTEXITCODE -ne 0 -or -not $sbHead) { $sbStep = 'git rev-parse HEAD' }
            }
        }
    }
    if ($sbStep) {
        $results += "T23-T24 FAIL: the sandbox repository at $sandbox could not be set up ($sbStep)"
    } else {
        $sbHeadShort = "$sbHead".Trim().Substring(0,7)
        $sbCodex    = Join-Path $sbReviews 'SPRINT-145-step7a-codex.md'
        $sbReviewer = Join-Path $sbReviews 'SPRINT-145-step7a-reviewer.md'
        Set-Content -Path $sbCodex -Value "verdict: APPROVED`nreviewed-against-commit: $sbHeadShort" -Encoding UTF8

        # T23: S145 accepts the Opus id at the model check (the exception itself). The suppressed-telemetry
        # echo, reached only after the check passes, is the positive evidence. The reviewer fake names a
        # stale commit, so the run then refuses at the reviewer's staleness check, before any gate that
        # reads the repository: asserting that refusal pins where the run stopped.
        Set-Content -Path $sbReviewer -Value "reviewed-by-model: claude-opus-5-5`nverdict: APPROVED`nreviewed-against-commit: $staleSha" -Encoding UTF8
        $r = Invoke-Hook $mock145 $sandbox
        $ok = (-not ($r.Stderr -match 'Step 7a internal review ran on')) -and ($r.Stderr -match 'telemetry suppressed \(test seam\): \S+ \| close \| S145 \| claude-opus-5-5 \| PASS') -and ($r.Exit -eq 2) -and ($r.Stderr -match 'Artifact is STALE')
        $results += "T23 (S145 accepts Opus at the model check, positive echo; stops at the reviewer's staleness check): exit=$($r.Exit) expect=2 $(if($ok){'PASS'}else{'FAIL'})"
        if (-not $ok) { $results += $r.Stderr }

        # T24: the exception is Opus only; S145 still refuses a cheaper model.
        Set-Content -Path $sbReviewer -Value "reviewed-by-model: claude-sonnet-5-5`nverdict: APPROVED`nreviewed-against-commit: $staleSha" -Encoding UTF8
        $r = Invoke-Hook $mock145 $sandbox
        $ok = ($r.Exit -eq 2 -and $r.Stderr -match "Step 7a internal review ran on 'claude-sonnet-5-5'")
        $results += "T24 (S145 still refuses a cheaper model): exit=$($r.Exit) expect=2 $(if($ok){'PASS'}else{'FAIL'})"
        if (-not $ok) { $results += $r.Stderr }
    }
} finally {
    Remove-Item $sandbox -Recurse -Force -ErrorAction SilentlyContinue
    if (Test-Path $sandbox) { $results += "sandbox cleanup: could not remove $sandbox (harmless: no real close reads it; a later run clears it once it is an hour old)" }
    foreach ($v in $gitRouting) { [Environment]::SetEnvironmentVariable($v, $savedGitEnv[$v], 'Process') }
}

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

# Cleanup
Remove-Item $codex,$reviewer,$waiver,$ciHealthWaiver,$ciPendingWaiver,$untrackedWaiver,$ledgerWaiver -ErrorAction SilentlyContinue
Remove-Item $tmpSprints -Recurse -Force -ErrorAction SilentlyContinue
Remove-Item $tmpLedgerLog -ErrorAction SilentlyContinue   # S145 Step 7a cycle 3 (Opus N3-4): left behind since S141
Remove-Item Env:\STATSTID_CI_HEALTH_MOCK -ErrorAction SilentlyContinue
Remove-Item Env:\STATSTID_SPRINTS_DIR -ErrorAction SilentlyContinue
Remove-Item Env:\STATSTID_UNTRACKED_MOCK -ErrorAction SilentlyContinue
Remove-Item Env:\STATSTID_WORKTREE_MOCK -ErrorAction SilentlyContinue
Remove-Item Env:\STATSTID_TELEMETRY_SUPPRESS -ErrorAction SilentlyContinue

Write-Output $results
$failed = $results | Where-Object { $_ -match 'FAIL' }
if ($failed) { Write-Output ""; Write-Output "FAILURES PRESENT"; exit 1 }
# S145 Step 7a (Codex W1; Opus N-D at cycle 2): a skipped test is not a passed one, and "ALL" means the
# full count of PASS lines, not merely the absence of FAIL. When T23/T24 are deleted after the S145 close,
# this count becomes 23 (Opus N3-5).
$skipped = @($results | Where-Object { $_ -cmatch 'SKIPPED' })   # case-sensitive: only the explicit "SKIPPED" marker counts
$passed  = @($results | Where-Object { $_ -match '^T\d+ .* PASS$' })
Write-Output ""
if ($skipped.Count -gt 0 -or $passed.Count -ne 25) { Write-Output "$($passed.Count) OF 25 PASS LINES, $($skipped.Count) SKIPPED LINE(S); NOT ALL 25 RAN"; exit 1 }
else { Write-Output "ALL 25 TESTS PASSED ($($passed.Count) PASS lines)" }
