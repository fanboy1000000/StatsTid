# PAT-013 — `ON CONFLICT DO NOTHING` vs catching `23505` is a transaction-liveness decision, and only one assertion tells them apart

| Field | Value |
|-------|-------|
| **ID** | PAT-013 |
| **Category** | pattern |
| **Status** | approved |
| **Sprint** | S127 |
| **Domains** | Infrastructure, Backend, Test |
| **Tags** | concurrency, on-conflict, transaction-abort, false-green, falsification-probe, create-if-absent |
| **Origin** | TASK-12702 (S127), proposed by the implementing agent after a falsification probe |

## The pattern

A "create if absent" primitive can be written two ways:

```csharp
// A — ON CONFLICT
INSERT INTO t (…) VALUES (…) ON CONFLICT (natural_key) DO NOTHING RETURNING id;   // null on conflict

// B — catch the unique violation
try   { INSERT INTO t (…) VALUES (…); return id; }
catch (PostgresException e) when (e.SqlState == "23505") { return null; }
```

**Both return `null` on conflict. They are not interchangeable.** In PostgreSQL a unique-violation error
**aborts the transaction** — every subsequent statement on it fails with `25P02: current transaction is
aborted, commands ignored until end of transaction block`.

So form **B is only usable when the loser does no further database work on that transaction.** Any caller
that must *continue* after losing a race — re-read the winning row, take a different arm, write an audit
row, enqueue an outbox event — must use form **A**.

## Why this is a knowledge-base entry and not a code comment

**A conflict test that asserts only the return value passes against the broken form.** Both return
`null`. The green tells you nothing about the property you actually depend on.

The discriminating assertion is **real database work issued on the same transaction after the conflict**:

```csharp
var second = await repo.TryCreateIfAbsentAsync(conn, tx, period, ct);
Assert.Null(second);
// THE assertion that separates A from B — B throws 25P02 here:
var rows = await CountRowsAsync(conn, tx, naturalKey, ct);
Assert.Equal(1, rows);
await tx.CommitAsync(ct);          // and the transaction still commits
```

Verified empirically in S127: substituting form B into `TryCreateIfAbsentAsync` turned **3 of 5** tests
red with `25P02`, while the two tests that did not continue on the transaction stayed green.

## How to apply

1. Default to `ON CONFLICT (…) DO NOTHING RETURNING <pk>` for any `Try*IfAbsent` primitive.
2. Never assert only "the second call returned null". Assert **post-conflict transaction liveness** —
   a subsequent read/write on the same transaction, and a successful commit.
3. Add a negative control that the conflict target is the **exact** key you think it is (vary one column
   of the natural key and prove it does *not* conflict). A too-broad `ON CONFLICT` target silently
   swallows legitimate inserts.
4. Where form B is genuinely fine (the loser returns immediately), **roll back first** and do any further
   reads on a fresh connection. Existing instances in this repo do exactly that:
   `AdminEndpoints.cs`, `AgreementEntitlementEndpoints.cs`, `EntitlementConfigEndpoints.cs`,
   `PositionOverrideEndpoints.cs` — note two of those return **412**, not 409, so do not cite them as a
   uniform "23505 → 409" precedent.

## Known instances

- `ApprovalPeriodRepository.TryCreateIfAbsentAsync` (S127) — the send command's create arm; the loser
  **must** continue (re-read, take the transition arm, return a clean 409), so form A is mandatory.
- `PayrollExportService.TryInsertRecordAsync` — the pre-existing instance the S127 primitive was copied
  from.

## Relationship to other entries

Complements the project's standing lesson that *a test written to demonstrate a result rather than
falsify one goes green on nothing*. This is a named, reusable case of it: two implementations that are
observably identical on the happy path and differ only in a property no obvious assertion covers. The
S127 agent found it by **substituting the wrong implementation and re-running** — the probe, not the
review, is what proved the test discriminating.
