# PAT-028 — Compute "today" once per operation and pass it down

| Field | Value |
|-------|-------|
| **ID** | PAT-028 |
| **Category** | pattern |
| **Status** | approved |
| **Sprint** | S139 |
| **Domains** | Backend, Infrastructure, Test |
| **Tags** | timeprovider, business-date, midnight, audit-consistency, compute-once, closeDate, adr-023-d8, qual-153 |

## Context

S139 routed the product's clock SOURCE through the injected `TimeProvider` on the profile, agreement-code and
approval-projection paths (QUAL-153). The Step-5a reviews then found the same defect shape twice, in code that had
just been converted and in code that pre-dated the sprint:

- the profile **soft-delete** stamped the database row from one provider read (inside the repository's SQL bind)
  and the `EmployeeProfileSoftDeleted` event from another (in the handler) — one clock, two instants. At 23:59:59.9
  UTC the row could say the 8th and the replayable event the 7th, and a fresh comment asserted "they cannot disagree";
- the admin **create POST** computed "today" three separate times for the profile row, the agreement-code row and the
  new reporting line — beneath S137's comment "Computed ONCE here so the three can never disagree". A midnight straddle
  would have left a new employee's first day with no agreement code covering it, or no approver.

Neither was a wrong clock. Both were the right clock read more than once.

## Pattern

**One operation, one date.** A handler (or the outermost unit of work) reads the business date ONCE, before it opens a
connection or transaction, and passes that value to everything the operation writes or compares — repository calls,
SQL parameters, event fields, audit payloads:

```csharp
// handler — the single read for the whole operation
var today = DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime);   // UTC-day form (see PAT-008 / QUAL-157)

await repo.SoftDeleteAsync(conn, tx, employeeId, expectedVersion, closeDate: today, ct);
outbox.Enqueue(new EmployeeProfileSoftDeleted { …, EffectiveTo = today });  // the SAME variable
```

```csharp
// repository — accepts the date; falls back to its own provider only when called without one
public async Task<…> SoftDeleteAsync(NpgsqlConnection conn, NpgsqlTransaction tx, string employeeId,
    long expectedVersion, DateOnly? closeDate = null, CancellationToken ct = default)
{
    var today = closeDate ?? DateOnly.FromDateTime(_timeProvider.GetUtcNow().UtcDateTime);
    … "SET effective_to = @today, updated_at = NOW()" …   // the business DATE is a parameter; the maintenance INSTANT may stay NOW()
}
```

Rules:
- **The caller that owns the operation reads the date; callees accept it.** Give repository methods an OPTIONAL trailing
  `DateOnly? closeDate = null` (or `asOf`) so existing callers and direct test constructions keep compiling; the fallback
  exists for callers that genuinely have no operation-level date, not as the normal path.
- **Business DATES are bound parameters** (`@today`), never `NOW()::date` / `CURRENT_DATE` in the statement — a SQL clock
  read is a second read by definition, on a clock the test host cannot fix (PAT-008). Maintenance instants such as
  `updated_at = NOW()` are timestamps, not business dates, and may stay on the database clock.
- **Reuse, do not recompute.** Within one handler, a second `timeProvider.GetUtcNow()` for a business date is a review
  finding. When a comment says "computed once", the code must have exactly one read — the S137 comment was true for
  three of five cells and nobody noticed for two sprints.
- **Say the invariant the code actually holds.** "Row and event carry the same date by construction because one value
  is passed to both" is checkable; "they cannot disagree" over two reads is not.

## Rationale

**Auditability** is the invariant at stake: the event stream must reconstruct the row. Two reads of a correct clock
disagree only in a window of milliseconds a night — rare enough never to be seen in a test, real enough to produce a
row/event contradiction that a replay cannot resolve. **Domain correctness** is next: a first day with no covering
agreement code is a wrong answer, not a cosmetic one. Passing the date down also makes the operation a pure function of
(request, seed, today) — which is exactly what the fixed test clock (PAT-008) needs to prove anything. ADR-023 D8
already described the soft-delete call as `SoftDeleteAsync(..., closeDate, ct)`; S139 made the code match the decision.

## Agent Guidance

- Before converting a clock read to the seam, count the reads in the operation. If there are two or more for the same
  business date, the fix is "compute once and pass", not "convert each".
- A repository that reads the clock for a business date should offer the optional parameter; a handler that has the
  date should always supply it.
- The Step-5a Reviewer's check: for every write path, trace where each DATE column and each event date field gets its
  value; they must trace to ONE variable per operation. Two provider reads, or a provider read plus a SQL `CURRENT_DATE`,
  is a WARNING even when the day-derivation is identical.
- Related: PAT-008 (the fixed clock must reach the product), PAT-025 (the router takes `today` as a parameter — the same
  idea one level down), QUAL-155/156 (the remaining raw reads and SQL clock sites), QUAL-157 (which day is "today").
