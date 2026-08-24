# PAT-019 — Wire-driven atomic-outbox rollback proof

| Field | Value |
|-------|-------|
| **ID** | PAT-019 |
| **Category** | pattern |
| **Status** | approved |
| **Sprint** | S133 |
| **Domains** | Test, Backend, Infrastructure |
| **Tags** | atomic-outbox, rollback, webapplicationfactory, false-green, verification-theater, adr-018, hollow-assertion |
| **Origin** | TASK-13307 (S133 QUAL-016 spike); precedent `Approval.SendAtomicityTests` (S127) |

## The trap (verification theater)

An "atomic-outbox" rollback test is supposed to prove that when a state-change endpoint's outbox enqueue
fails, the WHOLE transaction rolls back — no state row, no audit row, no canonical event, no outbox row
survives (the ADR-018 D3 everything-or-nothing contract). *Outbox* = the table a state change writes its
event-row into **inside the same DB transaction**, so state and event commit or roll back together.

The trap: the older `*AtomicTests` **re-type the endpoint's save-orchestration in the test body** (open
connection → begin tx → call the repository `(conn, tx)` overload → append audit → call `IOutboxEnqueue` →
commit) and assert that the *hand-typed copy* rolls back. That proves only the copy. If the real endpoint
drifts (commits before enqueue, opens its own connection, swaps to a self-connection repo overload) — or is
deleted entirely — the test stays **green**, because it never touches the shipped handler. It looks like
evidence and is not (the S125 hollow-assertion / PAT-016 family, at the persistence layer).

## The rule

**Prove the atomic contract by driving the REAL endpoint, never by re-typing its orchestration.** Derive a
host from `StatsTidWebApplicationFactory` (the regression project's `WebApplicationFactory<Program>` that
boots the real `Backend.Api` against a Postgres testcontainer) via one of the shared helpers:

```csharp
using var factory = _factory.WithThrowingOutbox();            // throws on every IOutboxEnqueue call
// or, for dual-emit (supersede) endpoints:
using var factory = _factory.WithThrowOnSecondEnqueueOutbox(); // real 1st enqueue, throw on the 2nd
```

Then: POST to the **real route** with an appropriately-scoped JWT, assert the escaped throw surfaces as
**5xx**, and assert on a **fresh connection** that nothing leaked, reusing
`ForcedRollbackHarness.AssertNoStateMutationAsync / AssertNoAuditRowAsync / AssertNoEventRowAsync /
AssertNoOutboxRowAsync`. Only `IOutboxEnqueue` is swapped — `PostgresEventStore`/`IEventStore` (the
background publisher's dependency) is untouched, so only the state-change-site enqueue faults.

Falsifiability this buys (the mutations that now turn the test RED, and the hand-mirror version could not):
move `CommitAsync` before the enqueue; swap the `(conn, tx)` repo overload for a self-connection sibling;
delete/rename the route (→ 404/405, not 5xx).

## Gotchas

- **Boot order (S63/S65 lesson).** Run the base host's startup seeders ONCE before deriving the throwing
  host — a seeder with backfill still to do would invoke the throwing outbox at startup and fail the boot.
- **Server-generated ids.** For create/clone endpoints the row id is generated INSIDE the rolled-back tx and
  never returned, so the outbox/event stream id is unknown to the test. Key the witnesses on a test-unique
  payload field (`::text LIKE` over the JSONB) via the generic `AssertNoStateMutationAsync`, not on the
  stream id. Where the stream id IS test-chosen (e.g. `employee-{id}`), reuse the stream-keyed asserts verbatim.

## Cost signal (from the S133 spike)

The conversion cost splits sharply by the endpoint's auth+seed surface: **cheap** for admin/config/time
endpoints (`GlobalAdmin`/`Employee` token, little or no row seeding, single-emit — copy-shape from a
template); **bespoke** for Skema/approval/overtime endpoints (org-scope token minting + status-specific row
seeds + a rule-engine `IHttpClientFactory` stub). Because the regression suite is Docker-gated (CI-only), the
bespoke conversions iterate slowly — plan them as their own increment, not co-scheduled with unrelated work.

## Related

- [PAT-016](PAT-016-container-predicate-silently-gates-its-contents.md) — a green test that could not see the
  thing it named (same false-green family).
- [PAT-014](PAT-014-characterization-baseline-one-inversion-per-encoding.md) — prove the assertion can fail
  (the mutation-on-the-real-guard discipline this pattern's falsifiability list applies).
