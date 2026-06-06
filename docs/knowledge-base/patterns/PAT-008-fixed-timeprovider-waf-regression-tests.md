# PAT-008 — FixedTimeProvider Pattern for WAF-Hosted Today-Dependent Regression Tests

| Field | Value |
|-------|-------|
| **ID** | PAT-008 |
| **Category** | pattern |
| **Status** | approved |
| **Sprint** | S65 |
| **Domains** | Test, Backend |
| **Tags** | timeprovider, regression-tests, webapplicationfactory, determinism, wall-clock, boot-order |

## Context

S65 TASK-6502 established the project's first `System.TimeProvider` seam (DI default `TimeProvider.System` in Program.cs; the year-overview handler derives `today` once per request from the injected provider). TASK-6504's regression suite needed today-dependent assertions (past/future month split, "Nu" semantics, planned-vs-used consumption) with zero wall-clock fragility. Proposed by the Test & QA agent; approved at Step 5b.

## Pattern

```csharp
private sealed class FixedTimeProvider : TimeProvider
{
    private readonly DateTimeOffset _fixed;
    public FixedTimeProvider(DateTimeOffset value) => _fixed = value;
    public override DateTimeOffset GetUtcNow() => _fixed;
}
```

Inject into the WebApplicationFactory host:

```csharp
factory.WithWebHostBuilder(b => b.ConfigureTestServices(s =>
    s.AddSingleton<TimeProvider>(new FixedTimeProvider(
        new DateTimeOffset(2026, 6, 15, 12, 0, 0, TimeSpan.Zero)))));
```

Choose the pinned date deliberately: it must make every seed's past/future classification unambiguous (e.g. mid-month, mid-year, weekday) and sit on the intended side of any version cutover the test exercises.

**Combine with the S63 boot-order rule**: `WithWebHostBuilder`-derived hosts re-run Program.cs seeders (e.g. the S31 `EmployeeProfileSeeder` backfills a profile row for every user lacking one), so "absent-state" fixtures (a profile-less employee, a missing eligibility row) must be inserted AFTER the last `CreateClient()` call that boots a host — otherwise the seeder repairs the very absence the test needs.

## Rationale

Wall-clock-dependent expected values rot (S64 census had a whole defect family of them); a fixed provider makes today-dependent endpoints pure functions of (request, seed, pinned-now) — replay-deterministic per P2. The boot-order combination matters because the two failure modes co-occur in practice: the same WAF host override that pins time also re-runs the seeders that destroy absent-state fixtures.

## Agent Guidance

- Any test asserting today-dependent behavior MUST override `TimeProvider` in the test host — never compute expected values from `DateTime.UtcNow`/`DateOnly.FromDateTime(DateTime.Today)`.
- The seam is per-endpoint opt-in (established new-endpoint-only in S65): verify the endpoint under test actually reads the injected provider before relying on the override; an endpoint still on raw `DateTime.UtcNow` will silently ignore it.
- Pin ONE `FixedToday` per test class and derive all seeds relative to it; mixing pinned and relative dates re-introduces ambiguity.
- Absent-state fixtures go in AFTER the last host boot (S63 lesson), or use ids the seeders won't touch.
