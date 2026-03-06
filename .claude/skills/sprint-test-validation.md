# Sprint Test Validation

Use this skill at sprint validation time (Orchestrator workflow step 4/5) to produce accurate, consistent test counts.

## Procedure

1. Run each test suite separately and record exact counts:

```
dotnet test tests/StatsTid.Tests.Unit --verbosity quiet
dotnet test tests/StatsTid.Tests.Regression --verbosity quiet
dotnet test tests/StatsTid.Tests.Smoke --verbosity quiet
```

For frontend (only if frontend changes were made this sprint):
```
cd frontend && npx vitest run 2>&1 | tail -5
```

2. Extract the `Passed: N, Failed: N, Skipped: N` line from each run. If a suite fails to connect (smoke tests without Docker), record as "skipped (Docker not running)" — do NOT guess or carry forward old numbers.

3. Look up the previous sprint's test counts from `docs/sprints/INDEX.md` (Test Progression table, last row).

4. Compute deltas:
   - Per-suite delta: current - previous
   - Total delta: sum of all suites current - sum of all suites previous

5. Output this exact block (fill in the numbers):

```
## Test Validation Report
| Suite | Previous | Current | Delta |
|-------|----------|---------|-------|
| Unit | {prev} | {curr} | +{delta} |
| Regression | {prev} | {curr} | +{delta} |
| Smoke | {prev} | {curr} | +{delta} |
| Frontend | {prev} | {curr} | +{delta} |
| **Total** | **{prev}** | **{curr}** | **+{delta}** |
```

6. Use these exact numbers when filling in:
   - SPRINT-N.md Test Summary table
   - INDEX.md Test Progression row
   - MEMORY.md Test Counts section

## Rules

- Never estimate or carry forward counts from memory. Always run the commands.
- If a suite was not changed this sprint, still run it to confirm no regressions.
- If frontend tests were not run (no frontend changes, no vitest installed), carry forward the previous count explicitly and note "(not re-run)" in the delta column.
- If any test fails, stop and report the failure before recording counts.
- The delta must be arithmetically correct. Double-check: previous + delta = current.
