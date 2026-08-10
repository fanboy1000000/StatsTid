# PAT-015 — An advisory-lock-serialized critical section must pin READ COMMITTED, and snapshot-memoized authority must not ride on it

| Field | Value |
|-------|-------|
| **ID** | PAT-015 |
| **Category** | pattern |
| **Status** | approved |
| **Sprint** | S127 |
| **Domains** | Backend, Infrastructure, Security |
| **Tags** | advisory-lock, isolation-level, read-committed, repeatable-read, memoization, concurrency, snapshot |
| **Origin** | TASK-12703 (S127), proposed by the implementing agent |

## The rule

**A transaction whose first statement is a blocking advisory-lock acquire must pin
`IsolationLevel.ReadCommitted` explicitly — never the default overload.**

`pg_advisory_xact_lock` blocks until granted. Under REPEATABLE READ the transaction snapshot is taken
**before** the lock is granted, so the winner's commit — *the exact thing the loser was waiting for* —
stays invisible, and the loser proceeds on pre-lock state. The lock serializes execution while the
snapshot silently un-serializes the data, which is the worst of both.

Concretely in S127: a losing sender would fail to see the winner's committed period row, take the
*create* arm instead of the *transition* arm, and collide on the unique constraint — while every
observable outcome (a 409, the same row/audit/event counts) looked identical to the correct path. That
is why the acceptance criterion for it needs a protocol-level test asserting *the loser's post-lock read
returns the row*, not merely that a 409 came back.

## The collision worth recording

The opposite discipline also exists in this codebase and is also correct — in its own place:

`DesignatedApproverAuthorizer.EnsureContextIsSnapshotBound` **throws** unless it is given
REPEATABLE READ or stronger. A memoized authority answer is only equivalent to re-querying if the
reads happen inside one pinned snapshot; under READ COMMITTED each statement gets a fresh snapshot and
memoization stops being an equivalence (the S125/F1 result).

So the two disciplines are **mutually exclusive on a single transaction**:

| Path shape | Isolation | Authority |
|---|---|---|
| Lock-serialized **write** path | **READ COMMITTED**, pinned explicitly | resolve per-request; do **not** pass a memoized authority context |
| Snapshot-memoized **read** projection | **REPEATABLE READ** | memoization is an equivalence, and takes no advisory lock |

Pass an `ApprovalAuthorityContext` into a lock-serialized send transaction and it throws — correctly.
The fix is never to weaken the authorizer's guard; it is to resolve authority per-request on that path.

## Known sites (all pinning READ COMMITTED with a comment)

- `ReportingLineEndpoints.cs:1787`, `:2470`
- `ReportingLineRepository.cs:216-223`
- `SettlementCloseService.cs:363-367`
- `ApprovalEndpoints.ExecuteSendAsync` (S127) and the reopen transaction
- `TimeEndpoints.cs` — `POST /api/time-entries` (S127/TASK-12704; was on the bare overload)
- `SkemaEndpoints.cs` — the month-save transaction (S127/TASK-12704; was on the bare overload)

## Checklist

- [ ] Advisory lock is the **first** statement in the transaction
- [ ] `BeginTransactionAsync(IsolationLevel.ReadCommitted, ct)` — explicit, with a comment saying why
- [ ] No memoized/snapshot-bound authority context is passed on this transaction
- [ ] Where two paths take advisory locks, they take **the same** lock in **the same** order — two locks
      acquired in different orders by two paths is a deadlock
- [ ] A test asserts the post-lock read **sees the winner's row**, not merely that the loser got a 409 —
      the two isolation levels are indistinguishable by outcome alone

### Added S127/TASK-12704 — the two hazards the in-lock re-read itself creates

- [ ] **The in-lock re-read uses the `(conn, tx)` overload.** A repository method that opens its own
      connection reads *outside* the caller's transaction and therefore outside the lock — **it looks
      like a re-read and is not one.** The defect is invisible at the call site:
      `GetByEmployeeAndPeriodAsync(employeeId, …)` and `GetByEmployeeAndPeriodAsync(conn, tx, employeeId, …)`
      differ only by two leading arguments, and only the latter is inside the lock.
- [ ] **Where a pre-transaction guard is retained as a fast path, the in-lock authoritative check shares
      ONE predicate and ONE response-construction site with it** — not a hand-copied literal bound by a
      comment. Two spellings of the same status set is a drift bug waiting for the next status to be
      added. S127 did this with `IsPeriodLockedForSave` / `PeriodLockedForSaveConflict`, so "the two 409s
      match" holds by construction rather than by inspection. Read-side precedent: S124/TASK-12405's
      shared `ApprovalVisibility` member.

## Related

[PAT-013](PAT-013-on-conflict-vs-23505-transaction-liveness.md) — why the same critical section uses
`ON CONFLICT DO NOTHING` rather than catching `23505`: the loser must keep its transaction alive to
re-read and take the transition arm.
