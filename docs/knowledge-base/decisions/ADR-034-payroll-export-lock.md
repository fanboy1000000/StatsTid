# ADR-034 — Payroll-export lock + monthly-export idempotency

| Field | Value |
|-------|-------|
| **Status** | accepted |
| **Sprint** | S90 |
| **Domains** | Payroll Integration, Backend, Data Model, Infrastructure, Frontend |
| **Tags** | payroll-export, export-lock, idempotency, reopen, corrections-manifest, cross-context-read, event-sourcing, monthly-time |
| **Supersedes / amends** | Phase 2 of the "reopen until sent to payroll" decision (S89 = Phase 1, the leader-reopen FE gate). Builds on ADR-013 (retroactive corrections, single-period no-cascade), ADR-018 (transactional outbox), ADR-026 (audit projection). DISJOINT from ADR-033 (vacation/termination settlement — different tables, different keys). |

## Context

A leader can reopen an APPROVED monthly time registration (S89). The owner's rule: a leader may reopen **until the month is sent to payroll**, after which it locks (corrections only). But the system had **no per-(employee, month) "sent to payroll" signal**: `approval_periods` carried no export state; `POST /api/payroll/calculate-and-export` wrote a delivery envelope to `outbox_messages`, emitted no domain event (`PayrollExportGenerated` was a registered-but-never-emitted placeholder), and wrote nothing back; and it had **no idempotency** — every call minted a fresh export, so an APPROVED month could be exported (and duplicated to payroll) repeatedly, reopen or no reopen. The export lives in the **Payroll** service; the reopen in the **Backend**; both share one Postgres DB.

Owner rulings (S90): OQ-1 lock at **export committed** (not delivered); OQ-2 **corrections only** post-lock (no reopen for any role, no admin recall); OQ-5 **one-transaction** atomic refactor; OQ-6 the raw `/export`+`/export-period` under the lock+idempotency.

## Decision

### D1 — One Payroll-owned record = lock + idempotency + corrections-manifest
A new `payroll_export_records` table (Payroll context; regular monthly time, NOT settlement): `export_id` PK, nullable `period_id`, `employee_id`, `year`, `month`, `exported_at`, `original_lines JSONB` (immutable manifest as first exported), `current_effective_lines JSONB` (the evolving correction baseline — D5), `content_hash`, `source`; **UNIQUE `(employee_id, year, month)`**. The row's existence = "this month is sent to payroll / locked." The same row serves three jobs: the lock (existence), idempotency (the unique key + content_hash), and the corrections manifest (the lines).

### D2 — Atomic export; the lock is a "durable handoff record", not "delivered" (OQ-1/OQ-5)
`PayrollExportService.ExportAsync` is refactored to inject `IOutboxEnqueue` + the audit deps and run ONE `(conn, tx)`: `GuardSettlementLineDelivery` FIRST (fail-closed ordering preserved) → group lines by `(employee_id, year, month)` (D3) → per group, `FOR UPDATE`-re-lock the approval row + re-assert `status='APPROVED'` (D4 race) → INSERT `payroll_export_records` → emit `PayrollExportGenerated` to **`outbox_events`** (the ADR-018 canonical event outbox, NOT the `outbox_messages` delivery envelope) → write the ADR-026 audit row → COMMIT. The lock is set at COMMIT (OQ-1 "export committed"). **There is NO background dispatcher for `outbox_messages WHERE destination='payroll'`** (the only poller is `destination='external'`), so the actual mock-SLS send stays the EXISTING synchronous best-effort HTTP POST + status update, POST-commit; the lock does NOT depend on delivery. The event is REUSED from the vestigial `PayrollExportGenerated` (reshaped payload — replay-safe, it was never emitted; a 2nd live payroll-export event would be rot), now a real audited event (Infrastructure `IAuditProjectionMapper` + Payroll DI + catalog row; the catalog "defined-but-unemitted TBD" resolved).

### D3 — Idempotency by content (OQ-6); multi-period exports
On the UNIQUE `(employee_id, year, month)` conflict: **same content_hash → no-op** (return the existing export, no duplicate record/event/envelope); **different content_hash → a discriminated 422/409** "already exported for {month}; use a correction." The raw `/export`+`/export-period` (which bypass approval and flatten multiple periods/months into one call) GROUP lines by `(employee_id, year, month)` and write one record per tuple, all-or-nothing; a line spanning calendar months is rejected. A ZERO-line month writes NO record (nothing handed off → stays reopenable).

### D4 — Cross-context READ contract; the export↔reopen race
The Backend reopen **READS** `payroll_export_records` (never writes it — the Payroll context owns it; this honors the ADR-033 precedent of Backend-owns-`approval_periods` / Payroll-owns-the-export-artifact, with a read-only cross-context contract rather than a cross-context write or an eventual-consistency projection). The export↔reopen race is closed by BOTH sides taking the approval row's `FOR UPDATE` lock: the export re-asserts APPROVED under it before inserting the record; the reopen, INSIDE its advisory-locked transaction (after `AcquireTreeLockForEmployeeAsync`, before the conditional status UPDATE), takes the same row lock then reads the lock — so an export-commit and a reopen cannot interleave on the same period.

### D5 — Reopen lock + corrections-only (OQ-2); the manifest evolves
When `payroll_export_records` has a row for the period's `(employee, year, month)`, the reopen endpoint returns a discriminated **409** `{kind:"payroll-locked", reason:"Måneden er sendt til lønkørsel — brug en korrektion."}` for **ALL roles** (leader, HR, GlobalAdmin — no recall). The only post-lock path is the ADR-013 retroactive correction (`/recalculate`, GlobalAdmin), which reads its diff baseline from `current_effective_lines` (NOT caller-supplied) and **updates** `current_effective_lines` to the corrected state in the same tx — so a 2nd correction diffs against the 1st correction's result, not the original (avoiding double-counting); `original_lines` stays immutable. A `/recalculate` of a never-exported month is cleanly rejected ("ikke sendt til lønkørsel — genåbn i stedet"). The leader Teamoversigt surfaces `payrollExported` per row (a batched read) and hides the Genåbn control for locked rows, showing a "Sendt til lønkørsel" indicator instead.

## Consequences

- A month sent to payroll is immutable-by-reopen; fixes go through corrections (a delta to payroll), never a duplicate full export. The pre-existing duplicate-export gap is closed by D3.
- Post-lock correction authority is **GlobalAdmin** (the `/recalculate` auth), NOT the Leader who could reopen pre-lock — an accepted consequence of OQ-2 (corrections-only, no recall).
- The lock is "durable handoff record written," not "payroll confirmed receipt" — anticipatory, correct for the current no-real-SLS-delivery posture; a future "delivered"-refinement is additive on the event.
- New table (66→ tables); `payroll_export_records` is regular-monthly and disjoint from the ADR-033 settlement tables (`settlement_export_lines`/`settlement_payroll_inbox`, keyed per entitlement-year).
- Step-0b dual-lens caught 4 BLOCKERs pre-code (the outbox conflation, the export↔reopen TOCTOU, the manifest-after-correction double-count, the multi-period `/export-period`), all resolved here.
