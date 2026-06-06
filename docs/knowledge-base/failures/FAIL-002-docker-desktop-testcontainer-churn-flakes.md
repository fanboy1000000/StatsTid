# FAIL-002 — Docker Desktop Sheds Testcontainer Starts Under Sustained Churn

| Field | Value |
|-------|-------|
| **ID** | FAIL-002 |
| **Category** | failure |
| **Status** | resolved (operational mitigation; no code defect) |
| **Sprint** | S65 |
| **Domains** | Test, CI/Tooling |
| **Tags** | docker-desktop, testcontainers, regression-suite, flake, consecutive-runs, close-protocol |

## Failure

During the S65 close validation (2026-06-06), full regression double-runs intermittently failed with `Docker.DotNet.DockerApiException : Docker API responded with status code=Conflict, response={"message":"container <id> is not running"}` thrown from `DockerHarness.StartAsync()` → `PostgreSqlContainer.StartAsync()` — i.e. a freshly-built ephemeral container died at start, before any SQL ran.

Three observations, all on one long-lived Docker Desktop (Windows) session under sustained load:
1. Consecutive full run: **23 failures** across many classes (first double-run of the day, after ~80 min of container churn).
2. Consecutive full run after rebuild: **1 failure** (`ProfileNoOpShortCircuitTests`, init, `[1 ms]`).
3. Pristine full run overlapping a parallel agent's class runs: **1 failure** (`ProfileBoundaryHydrationTests`, init, `[1 ms]`).

Signature: always `DockerApiException` at container START; always `[1 ms]` test duration (class-init death); DIFFERENT classes and counts each time; never reproducible on a quiet, fresh Docker session (the same trees ran 444/444 and 447/447 green).

## Root cause

Environmental, not test logic: each Docker-gated regression class spins its own ephemeral postgres testcontainer (`DockerHarness.StartAsync()`, no `WithReuse`); a full 440+-test run cycles dozens of containers. Docker Desktop for Windows degrades under hours of accumulated churn (plus a running 8-service compose stack), failing container starts with the not-running Conflict. Parallel `dotnet test` processes against the same daemon amplify it.

## Resolution / Mitigation (close-protocol rules)

1. **Fresh Docker session for close evidence**: restart Docker Desktop before the definitive pristine+consecutive double-run (S64's green double-run was also on a fresh session).
2. **Exclusive runs**: never run agent class-validations, builds, or a second `dotnet test` process concurrently with a full-suite run — both for daemon pressure and shared-postgres state.
3. **Triage signature**: a regression failure that is `DockerApiException` + `[1 ms]` + class-init is THIS flake — do not chase it as test debt; re-run on a fresh session. A failure with an assertion message or SQL error is NOT this flake.
4. Full per-run logs (`Out-File`) on close runs, so failure NAMES are always captured — a tail-only capture cost one 20-minute re-run during S65.

## Agent Guidance

- Test & QA agents: do not "fix" tests for this signature; report it as the environmental flake per this entry.
- Orchestrator: budget a Docker restart into the close sequence; treat a 1-2-test consecutive failure with this signature as re-run-on-fresh-session, not laundering.
