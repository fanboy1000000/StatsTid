# PAT-008 — FixedTimeProvider Pattern for WAF-Hosted Today-Dependent Regression Tests

| Field | Value |
|-------|-------|
| **ID** | PAT-008 |
| **Category** | pattern |
| **Status** | approved (updated S139 — shared fixture, factory opt-in, falsifiability probe) |
| **Sprint** | S65 (S139 update) |
| **Domains** | Test, Backend, Infrastructure |
| **Tags** | timeprovider, regression-tests, webapplicationfactory, determinism, wall-clock, boot-order, fixed-today, qual-153 |

## Context

S65 TASK-6502 established the project's first `System.TimeProvider` seam (DI default `TimeProvider.System` in
`Program.cs`; the year-overview handler derives `today` once per request from the injected provider). TASK-6504's
regression suite needed today-dependent assertions with zero wall-clock fragility and wrote a private
`FixedTimeProvider` inside one test file. That helper stayed `internal` to `YearOverviewTests.cs` for 74 sprints,
and suites kept reaching for the real clock instead — until S138 lost two CI runs to pins whose dates floated with
the calendar (a seeded absence at "today − 60" landed on a weekend, where the norm is zero, and the pin proved
nothing). **S139 (QUAL-153) made the pattern the easy default**: one shared fixture, a one-line opt-in on the shared
factory, a probe that proves the fixed clock reaches the product, and the product's clock SOURCE routed through the
seam on the paths the converted suites exercise.

## Pattern

**The fixture** — `tests/StatsTid.Tests.Regression/Hosting/FixedTimeProvider.cs` (public, namespace
`StatsTid.Tests.Regression.Hosting`; the ONLY definition in the test tree):

```csharp
public sealed class FixedTimeProvider : TimeProvider
{
    public FixedTimeProvider(DateOnly date)        // pins UTC MIDNIGHT — see the warning below:
        : this(new DateTimeOffset(date.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero)) { }   // both calendars AGREE there
    public FixedTimeProvider(DateTimeOffset value) => _fixed = value;   // verbatim instant
    public override DateTimeOffset GetUtcNow() => _fixed;
    private readonly DateTimeOffset _fixed;
}
```

**The opt-in** — `StatsTidWebApplicationFactory.WithFixedToday(DateOnly today)` returns a derived host whose
`TimeProvider` singleton is replaced (mirrors `WithThrowingOutbox()`):

```csharp
private static readonly DateOnly F = new(2025, 3, 12);   // Wednesday; OK24 side of the 2026-04-01 cutover
using var fixedHost = factory.WithFixedToday(F);
var client = fixedHost.CreateClient();                   // boots the host — seeders run HERE
// direct-INSERT fixtures AFTER this line (boot-order rule below)
```

Suites that construct a repository DIRECTLY pass the same provider through the repository's optional trailing
parameter (S139: `EmployeeProfileRepository`, `UserAgreementCodeRepository`, `ApprovalPeriodRepository` all take
`TimeProvider? timeProvider = null`, defaulting to `TimeProvider.System`):

```csharp
var repo = new ApprovalPeriodRepository(dbFactory, timeProvider: new FixedTimeProvider(F)); // dbFactory = the suite's own DbConnectionFactory
```

**Choose the anchor deliberately and assert it once per suite**: it must make every seed's classification
unambiguous — a weekday where a working day is required (weekend norm is 0), the intended side of 1 September
(ferieår), the 31 December transfer and 1 May (særlige feriedage) where the pin reasons about a holiday year, and
the intended side of any OK-version cutover (`OkVersionResolver.cs`, 2026-04-01 — both versions are seeded ACTIVE, so
a wrong-side read is silent). One `[Fact]` (or the constructor) asserts `F.DayOfWeek` and the OK side, so a future
edit that moves the anchor fails loudly. Name constants by what they are (`HalfTimeMonday = 2025-01-06` —
`Skema/Adr032ConsumptionPinTests.cs:69-72` is the exemplar).

**Combine with the S63 boot-order rule**: `WithWebHostBuilder`-derived hosts re-run `Program.cs` seeders (e.g. the
S31 `EmployeeProfileSeeder` backfills a profile row for every user lacking one — `StatsTidWebApplicationFactory.cs`
~`:171-175`), so "absent-state" fixtures (a profile-less employee, a missing eligibility row) must be inserted
AFTER the last `CreateClient()` call that boots a host — otherwise the seeder repairs the very absence the test
needs. Under a fixed clock there is a second consequence: a fixture employee's hire date must be ≤ the fixed today
(the profile repository refuses `EffectiveFrom` before `employment_start_date`, and the admin-create default is
"hired today" — under the fixed clock, `F`).

**Prove the clock reaches the product — the falsifiability probe** (`Hosting/FixedClockProbeTests.cs`, S139):
a fixture that silently does nothing is worse than none. The probe pins, with `F` far from the real date: a
future-dating guard that must return 422 at `F+1` and 200 at `F` (RED if the product still reads the real clock —
`F+1` is deep in the past); a soft-delete whose row `effective_to` (a SQL write, formerly `NOW()::date`) and event
`EffectiveTo` (a C# read) must BOTH equal `F` — two independent RED conditions, one per clock; and a plain host
resolving `TimeProvider` to `TimeProvider.System` (`Assert.Same` — a DI fact, never a real-calendar comparison).
Every converted pin carries a one-line RED condition in its comment: what would make it fail.

## Rationale

Wall-clock-dependent expected values rot (S64 census had a whole defect family of them; S138 lost two CI runs);
a fixed provider makes today-dependent endpoints pure functions of (request, seed, pinned-now) —
replay-deterministic per the **Domain correctness** invariant. But a fixed TEST clock the PRODUCT does not share
is a new failure mode: the halves of one request disagree about "today" (S139 found the profile PUT reading C#
`DateTime.UtcNow` while its soft-delete stamped the DATABASE clock). Hence the S139 rule: **on any path a fixed-clock
suite exercises, every clock read — C# and SQL — comes from the injected provider**; SQL business dates are bound
`@today` parameters, never `NOW()::date` / `CURRENT_DATE`. The
boot-order combination matters because the two failure modes co-occur in practice: the same WAF host override
that pins time also re-runs the seeders that destroy absent-state fixtures.

### ★ There is ONE day-derivation, and it is the Copenhagen day (S142)

**Every business date in StatsTid is `CopenhagenBusinessDate.Today(tp)`.** This entry previously taught a split —
the profile and agreement paths keeping the UTC day *"matching the frontend's `toISOString().slice(0,10)`"*, with
settlement and the worklist on Copenhagen. **That split was an accident, not a design.** The frontend used
`toISOString()` because it is JavaScript's easy path; the backend validators were then made UTC to agree with it;
everything downstream followed. S142 moved all of it — including the frontend — onto the Danish calendar day, which
is the day Danish employment law actually means. **Instants are unaffected**: `created_at`, `updated_at`, audit
timestamps and outbox ordering stay UTC, and converting one of those to a calendar day corrupts ordering.

### ⚠ `WithFixedToday(DateOnly)` CANNOT detect a UTC-vs-Copenhagen defect

The `DateOnly` constructor above pins **UTC midnight**, and UTC midnight is precisely the instant where the two
calendars are guaranteed to agree — Denmark's offset is never negative, so Copenhagen's local midnight never falls
before UTC midnight of the same date. **A pin built on it passes under both a correct and a broken implementation.**

Roughly 150 existing fixtures use it, which is fine: they are not testing the calendar. But a test whose *subject*
is the business day must pin an exact instant where the calendars disagree — `WithFixedInstant(DateTimeOffset)`,
with the four shared anchors in `tests/StatsTid.Tests.Regression/Hosting/BoundaryInstants.cs`:

| Instant | Danish day | Kills |
|---|---|---|
| Summer `2026-07-15 22:30Z` | 16 July | raw UTC **and** a hardcoded `+01:00` |
| Winter `2026-01-15 23:30Z` | 16 January | raw UTC (a hardcoded `+01:00` passes — that is why there are others) |
| Winter `2026-01-15 22:30Z` | 15 January | a hardcoded `+02:00` |
| Summer `2026-07-31 22:30Z` | **1 August** | raw UTC and `+01:00`, at a **month** boundary — the payroll-visible form, since every export, settlement and approval period is month-bounded |

**Expected values must be literals.** A test that computes its expectation by calling `CopenhagenBusinessDate`
proves only that the helper agrees with itself — S142 found three such tests and deleted the shape.

## Agent Guidance

- Any test asserting today-dependent behaviour MUST fix the clock — `WithFixedToday(F)` for a WAF host, the
  repository's `timeProvider` parameter for direct construction — and derive EVERY date from the one anchor
  constant. Never compute expected values from `DateTime.UtcNow` / `DateOnly.FromDateTime(DateTime.Today)`; the
  S139 census regex `DateTime\.(Today|UtcNow|Now)\b|DateOnly\.FromDateTime\(DateTime\.` over a converted file
  must return zero (it is a plain-text scan — rephrase comments that quote those tokens).
- **Since S142 the seam reaches every business-date path**, so this guidance is now about *how* to pin, not
  *whether* you can. The endpoint, repository, authorizer, settlement, worklist, migrator, schema-bootstrap and
  frontend sites were all converted; `src/**/*.cs` contains **zero** executable `CURRENT_DATE` / `NOW()::date`
  business-date derivations. If you find a path that still cannot be pinned, that is a defect worth a register row,
  not a reason to work around it — and **do not nudge dates onto weekdays** (the S138 `OnWeekday` / `NextWeekday`
  helpers were deleted in S139 for exactly this reason: a nudge is a guard, not a fix).

  *Historical note on the list this replaces: it named `DelegationExpiryService.cs:86` and
  `ReportingLineRepository.cs:1287` as unconverted for two sprints after S140 had already parameterised them, and
  carried three `file:line` cites that had drifted. **A pinned line number in a KB entry is a claim with a short
  shelf life** — prefer naming the symbol and the mechanism, which is why the replacement above names neither.*
- Pin ONE anchor per test class, assert its weekday and OK side once, and derive all seeds relative to it;
  mixing pinned and relative dates re-introduces ambiguity. State each pin's RED condition in its comment.
- Absent-state fixtures and fixture employees go in AFTER the last host boot (S63 lesson), with hire dates ≤ `F`,
  or use ids the seeders will not touch.
- Security timestamps (token minting, audit `created_at`, `users_audit`) stay on the real clock BY DESIGN — a fixed
  provider under token minting would mint tokens "in the past" against a real-clock validator. Do not route them.
