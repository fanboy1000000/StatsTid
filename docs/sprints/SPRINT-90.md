# Sprint 90 — Payroll-export lock (reopen-until-payroll Phase 2)

| Field | Value |
|-------|-------|
| **Sprint** | 90 |
| **Status** | closed |
| **Start Date** | 2026-06-21 |
| **End Date** | 2026-06-21 |
| **Orchestrator Approved** | yes (Step-0b dual-lens; 4 BLOCKERs + WARNINGs baked into the tasks; forks resolved) |
| **Build Verified** | yes (`dotnet build` 0 err; `tsc` + `npm run build` clean) |
| **Test Verified** | yes — local: unit 856, FE 499, the S90 regression suite 43/43 (testcontainers) **AND CI GREEN run `27910135469`, all 7 jobs** (build-and-test = full fresh-greenfield regression **1040** + smoke + e2e) |

## Sprint Goal
**Phase 2 of the "reopen until sent to payroll" decision** (`REFINEMENT-reopen-until-payroll-lock.md`; S89 shipped Phase 1). Build the per-(employee, month) **payroll-export lock** so that once a month is sent to payroll it can no longer be reopened (corrections only); and close the latent **duplicate-export gap** (today `/calculate-and-export` has no idempotency). Owner rulings: **OQ-1** lock-at-export-committed · **OQ-2** corrections-only post-lock (no recall; no reopen for ANY role) · **OQ-5** one-transaction atomic refactor · **OQ-6** raw `/export`+`/export-period` under the lock+idempotency.

## Grounded reality (from the refinement's dual-lens review + S90 pre-read)
- The export is in the **Payroll** service (`/calculate-and-export`, `Program.cs`); the reopen is in the **Backend** (`ApprovalEndpoints.cs:1559`). **Both share the SAME Postgres DB** and the Payroll service has a `DbConnectionFactory` + `IOutboxEnqueue` (`PostgresEventStore`) → the atomic refactor is feasible.
- `PayrollExportService.ExportAsync(lines, ct)` today: generates an `exportId`, writes `outbox_messages` on its **own connection, no tx** (`:126-143`), then HTTP-POSTs to mock payroll + updates status. It receives ONLY `lines` — **no period/employee context** → must be threaded in.
- `/calculate-and-export` guards `status='APPROVED'`, computes lines, calls `ExportAsync` — **writes nothing back to approval_periods, emits no domain event, has NO idempotency** (every call = a fresh `outbox_messages` row → duplicate exports). `PayrollExportGenerated` is dead/unemitted.
- `/recalculate` (the ADR-013 correction path) requires the caller to supply `PreviousExportLines` (`Program.cs:382`, diffed at `RetroactiveCorrectionService.cs:198`) → a correction can't run without the prior manifest, which is nowhere persisted today.
- The export **lines themselves ARE the manifest** (persisting them = the corrections source).

## Proposed Approach — one Payroll-owned record that IS the lock + manifest + idempotency
A single new **`payroll_export_records`** table (Payroll context) written ATOMICALLY in the refactored export tx serves THREE Phase-2 needs at once:
- **Lock:** the row's existence (per period / per (employee,year,month)) = "sent to payroll".
- **Idempotency:** a UNIQUE key (period_id, and/or (employee_id, year, month)) → a 2nd export is a no-op (OQ-6/duplicate-gap fix).
- **Manifest:** the export `lines` stored as JSONB → `/recalculate` retrieves `PreviousExportLines` from it (TASK-9004).

**Reopen gate (Backend):** the reopen handler, after loading the period, does a **cross-context READ** of `payroll_export_records` for that period; if present → discriminated **409** ("Måneden er sendt til lønkørsel — brug en korrektion") for **ALL roles** (OQ-2 corrections-only); if absent → the S89 Leader+ reopen. (Reading a Payroll table from the Backend is a cross-context READ — far less coupling than a write; the Backend aggregate already reads many tables. Authoritative + atomic, no eventual-consistency window. **Step-0b fork:** this vs mirroring to an `approval_periods.payroll_exported_at` column — see OQ-A.)

**Atomic export (OQ-5) — corrected per Step-0b.** The lock = a **durable handoff record written**, NOT "delivered": there is NO background dispatcher for `outbox_messages WHERE destination='payroll'` (the only poller is `destination='external'`, `EventConsumerService.cs:97`), so the actual mock-SLS send stays the EXISTING synchronous best-effort HTTP POST. `ExportAsync` is refactored to take the period context + inject `IOutboxEnqueue` + open ONE `(conn, tx)`: `GuardSettlementLineDelivery` FIRST (preserve fail-closed ordering) → re-read+`FOR UPDATE`-lock the approval row and re-assert `status='APPROVED'` (closes the export-vs-reopen race) → INSERT `payroll_export_records` (manifest + idempotency) → emit `PeriodExportedToPayroll` via `IOutboxEnqueue` to **`outbox_events`** (the ADR-018 D3 canonical event outbox — DISTINCT from the `outbox_messages` delivery envelope; OutboxPublisher drains it) → write the ADR-026 audit row in the same tx → COMMIT. THEN (post-commit, best-effort, as today) the `outbox_messages` envelope + the synchronous HTTP POST + status update. The lock is set at COMMIT (OQ-1 "export committed"), independent of delivery. In-tx template = `RetroactiveCorrectionService.cs:238-250` (conn→tx→EnqueueAndReturnIdAsync→audit insert→Commit), NOT `WriteToOutboxAsync`.

## Open Questions for Step-0b (design forks)
- **OQ-A — lock read locality:** reopen reads `payroll_export_records` cross-context (recommended — atomic, no new approval column) **vs** Payroll also writes/Backend projects `approval_periods.payroll_exported_at` (local read, but a cross-context write or an eventual-consistency projection). *Lean: cross-context read.*
- **OQ-B — record key:** key the lock/idempotency on `period_id` (clean 1:1 with the approval period; but the raw `/export`+`/export-period` bypass approval and may have no period) **vs** `(employee_id, year, month)` (works for raw endpoints too). *Lean: (employee_id, year, month) as the idempotency key + a nullable period_id; reopen matches on the period's (employee, year, month).*
- **OQ-C — ADR:** this introduces a new cross-context lock pattern → a new **ADR-034 (payroll-export lock + monthly-export idempotency)** vs extending ADR-012/ADR-018. *Lean: ADR-034.*

## Plan Review (Step 0b) — dual-lens (both verified grounding accurate); forks resolved, BLOCKERs baked in
**Design forks RESOLVED (both lenses agreed):** OQ-A → cross-context READ of `payroll_export_records` (lowest coupling; honors ADR-033's Backend-owns-period / Payroll-owns-export; **add an explicit read-only Backend contract + repository API in ADR-034; Backend NEVER writes the table**). OQ-B → UNIQUE `(employee_id, year, month)` + nullable `period_id`. OQ-C → new **ADR-034**.

**BLOCKERs (resolved in the tasks below):**
- **B1 — outbox conflation + no payroll dispatcher.** `outbox_messages` (delivery envelope; synchronous best-effort POST, NO background dispatcher for `destination='payroll'`) ≠ `outbox_events` (ADR-018 D3 canonical event outbox, OutboxPublisher-drained). The atomic tx enlists `payroll_export_records` + the `PeriodExportedToPayroll` event (`IOutboxEnqueue`→`outbox_events`) + the audit row; `PayrollExportService` must INJECT `IOutboxEnqueue` (today only `DbConnectionFactory`). The lock = "durable handoff record written" (NOT "delivered") — matches OQ-1. → TASK-9002.
- **B2 — export↔reopen race (TOCTOU).** A pre-tx lock read races a concurrent export commit. Export tx `FOR UPDATE`-re-locks + re-asserts `APPROVED` before the record INSERT; reopen does the `payroll_export_records` existence check INSIDE the advisory-locked tx (after `AcquireTreeLockForEmployeeAsync` `:1642`, with/just before the conditional `TryUpdateStatusConditionalAsync` `:1663`), NOT the pre-tx read at `:1581`. A dedicated export-vs-reopen concurrency test. → TASK-9002/9003.
- **B3 — corrections manifest can't stay original-only.** A 2nd correction would re-diff from the original and double-count correction #1's delta. Store immutable `original_lines` + a `current_effective_lines` (or a `payroll_export_corrections` ledger); `/recalculate` diffs against the CURRENT effective lines and updates the baseline when a correction export commits. → TASK-9004.
- **B4 — raw `/export-period` spans multiple periods/months.** It flattens many `CalculationResults` into one `ExportAsync` (`Program.cs:147`); `PayrollExportLine` carries per-line `EmployeeId`/`PeriodStart`/`PeriodEnd`. The export tx GROUPs lines by `(employee_id, year, month)` and inserts ONE record per tuple, all-or-nothing; reject spans that don't normalize to calendar months. → TASK-9002.

**WARNINGs baked in:** idempotency = UNIQUE `(employee,year,month)` + a `content_hash` (same key+same hash → no-op return existing; same key+DIFFERENT hash → 409 "already exported, use correction") → TASK-9001/9002. `PeriodExportedToPayroll` is a REAL audited event (EventSerializer + Infrastructure `IAuditProjectionMapper` + Payroll DI marker + catalog row + parity tests; emit one per employee-month on `employee-{id}`); resolve the vestigial unemitted `PayrollExportGenerated` (reuse or delete — the catalog parity test asserts exactly-6-TBD) → TASK-9001. The reopen lock check runs after the row lock before BOTH arms' source-state update (employee arm can't reach an exported period — export needs APPROVED — but apply uniformly) → TASK-9003. The FE needs `payrollExported`/`exportedAt` ADDED to the team-overview backend response + the hook type (LEFT JOIN), not just UI hiding → TASK-9001/9005. **N2 edges:** decide whether a ZERO-line export locks (today `ExportAsync` is only called when `lines.Count>0`, `Program.cs:231`) — *lean: a 0-line month does NOT lock (nothing handed off); document it*; and note the post-lock correction authority is **GlobalAdmin** (`/recalculate` is GlobalAdminOnly), NOT the Leader — an accepted consequence of OQ-2.

## Architectural Constraints
- [ ] P3/P5 — the export record + `PeriodExportedToPayroll` event + outbox row commit in ONE tx (OQ-5); the lock is set at COMMIT (OQ-1). The Payroll service writes only Payroll-owned tables (+ outbox); no cross-context WRITE to approval_periods (per OQ-A lean).
- [ ] P6 — idempotency: a re-export of an already-exported (employee,month) does NOT duplicate payroll lines (closes the pre-existing gap); applies to `/calculate-and-export` AND the raw `/export`+`/export-period` (OQ-6).
- [ ] P5 — corrections-only post-lock (OQ-2): reopen post-export → 409 for every role; `/recalculate` (approval-unguarded, ADR-013) is the only post-lock path and reads the persisted manifest (TASK-9004).
- [ ] P9 — the FE reflects the lock: Teamoversigt hides/disables Genåbn for exported rows + shows "Sendt til lønkørsel"; the S89 leader-reopen is now gated on not-exported.
- [ ] No regression to S89 (pre-export leader reopen), the two-step approval (ADR-012), the settlement pipeline (ADR-033 — distinct tables, untouched), or the S78/S83 reopen concurrency hardening.

## Task Log
### TASK-9001 — schema + `PeriodExportedToPayroll` event + audit mapper (Data Model + SharedKernel)
`payroll_export_records` (init.sql additive table + `schema_migrations`-guarded legacy ALTER per the `init.sql:3163` template; db-schema regen via `generate_db_schema.py`): `export_id UUID`, `period_id UUID NULL`, `employee_id`, `year INT`, `month INT`, `exported_at TIMESTAMPTZ`, `original_lines JSONB`, `current_effective_lines JSONB` (B3 — corrections update this), `content_hash TEXT`, `source TEXT`; **UNIQUE `(employee_id, year, month)`** (OQ-B). Keep the name unambiguously regular-monthly (NOT settlement). `PeriodExportedToPayroll` event (SharedKernel) emitted per employee-month on `employee-{id}`; **MANDATORY** EventSerializer registration + an Infrastructure `IAuditProjectionMapper<PeriodExportedToPayroll>` + Payroll-`Program.cs` DI marker + an `audit-projection-catalog.md` row + the parity tests (the catalog parity test asserts exactly-6-TBD + 3-way lockstep — a registered-but-unmapped event fails it; mirror `RetroactiveCorrectionRequested`). **Resolve the vestigial unemitted `PayrollExportGenerated`** (reuse it as this event, or delete its registration + the catalog TBD row). **Blocked by Step-0b — RESOLVED (OQ-A/B/C ruled above).**

### TASK-9002 — atomic export refactor (Payroll)
Refactor `PayrollExportService.ExportAsync` to accept period context + thread one `(conn, tx)`; atomic record+outbox+event; idempotent on the unique key. Wire `/calculate-and-export` + raw `/export`+`/export-period` (OQ-6). **Depends on 9001.**

### TASK-9003 — reopen lock gate (Backend)
Reopen reads the export record; post-export → discriminated 409 (corrections-only, all roles); pre-export → S89 leader reopen. Tests: pre-export leader reopen 200; post-export leader+HR 409; the 409 is discriminated (kind="payroll-locked"). **Depends on 9001.**

### TASK-9004 — corrections retrieve the manifest (Payroll)
`/recalculate` reads `PreviousExportLines` from `payroll_export_records` (not caller-supplied) — closes the Codex BLOCKER; a correction of a never-exported month is rejected/no-ops cleanly. **Depends on 9001/9002.**

### TASK-9005 — FE lock surfacing (Frontend)
team-overview aggregate LEFT JOINs the export record → `payrollExported`/`exportedAt` per row; Teamoversigt hides/disables Genåbn for exported rows + a "Sendt til lønkørsel" badge; the detail footer reopen likewise. Tests. **Depends on 9001.**

### TASK-9006 — validate + Step-7a + ADR + docs + close (Orchestrator)
Build + full pyramid (FRESH greenfield — init.sql change → smoke + regression reseed) + e2e; Step-7a dual-lens (atomicity, idempotency, the cross-context read, corrections-manifest, the 409, FE lock); ADR-034 + SYSTEM_TARGET §H post-export-immutability amendment; QUALITY/ROADMAP/INDEX/SPRINT-90; commit + push + CI-verify; MEMORY.

## External Review (Step 7a)
| Lens | Cycle 1 | Cycle 2 | Artifact |
|------|---------|---------|----------|
| Internal Reviewer | APPROVE — 4 Step-0b BLOCKERs landed; 1 WARNING + 3 NOTEs | — | `.claude/reviews/SPRINT-90-step7a-reviewer.md` |
| External Codex | **3 BLOCKERs** | **CLEAN — all 3 fixes verified** | `.claude/reviews/SPRINT-90-step7a-codex.md` |

**[[review-lens-complementarity]] decisive:** the internal Reviewer APPROVED with one WARNING; Codex independently rated that same item a BLOCKER **and caught two more the internal lens missed** — all three in the corrections/idempotency path: (1) a mixed `/export-period` re-delivered an already-exported month (built the post-commit payload from the full `lines`, not just the new groups); (2) the B3 baseline-update + event/audit were best-effort-swallowed (`Success=true` on a rolled-back tx → a phantom-committed correction + stale baseline); (3) concurrent `/recalculate` raced on the baseline (both read before either locked). All three fixed (deliver only new groups; the correction tx propagates failures; `SELECT … FOR UPDATE` the baseline inside the correction tx) with discriminating RED-on-old tests; **Codex cycle-2 CLEAN**.

## Test Summary
Pyramid (S89 → S90):

| Tier | S89 | S90 | Δ | Note |
|------|-----|-----|---|------|
| Unit | 856 | 856 | 0 | the Payroll unit tests were re-pointed to the new `ExportAsync` signature; same count |
| Regression | 1015 | 1040 | +25 | new: `PayrollExportRecordTests`, `ReopenPayrollLockTests`, `RetroactiveCorrectionManifestTests`, `TeamOverviewAggregate` lock tests + the 3 Step-7a fix tests. **CI fresh-greenfield (build-and-test) = 1040/1040** (the init.sql change needs a fresh greenfield); locally the S90 blast-radius 43/43 (testcontainers) + unit + FE green |
| Smoke | 6 | 6 | 0 | fresh greenfield (the `payroll_export_records` table + reseed) — via CI |
| Frontend (vitest) | 495 | 499 | +4 | the lock-surfacing (Genåbn hidden for exported rows + the "Sendt til lønkørsel" indicator) |
| e2e | 3 | 3 | 0 | via CI |

Build: `dotnet build` 0 err; `tsc` + `npm run build` clean; `check_docs` green (66 tables, 50 KB entries incl. ADR-034). The demo stack was preserved (it holds `:5432` with the old schema + demo data); CI's services-postgres seeds from the new init.sql, so CI is the authoritative fresh-greenfield gate.

## Sprint Retrospective
**Phase 2 of "reopen until sent to payroll" — the payroll-export lock — shipped, and it closed a latent duplicate-pay gap on the way.** A single Payroll-owned `payroll_export_records` table (UNIQUE (employee,year,month) + content_hash + original/current-effective manifest JSONB) serves as the lock, the idempotency key, AND the corrections manifest. `ExportAsync` was refactored to write it + emit the (reused, reshaped, replay-safe) `PayrollExportGenerated` event + the audit row atomically; the Backend reopen reads it cross-context (read-only contract) and refuses post-export reopen with a discriminated 409 for all roles; `/recalculate` reads + evolves the manifest so sequential corrections don't double-count.

**The reviews carried the sprint.** Step-0b (dual-lens) caught 4 BLOCKERs PRE-CODE — the outbox-table conflation (the payroll outbox has no dispatcher → the lock is "durable handoff record written," not "delivered," which *validated* the owner's OQ-1 "committed not delivered" choice), the export↔reopen TOCTOU, the manifest-after-correction double-count, and the multi-period `/export-period`. Step-7a's external lens then caught 3 MORE in the implementation (the mixed-export re-delivery, the swallowed correction tx, the concurrent-correction race) that the internal lens rated lower or missed — the recurring [[review-lens-complementarity]] pattern at its most valuable on a money path. Every fix carries a RED-on-old test.

**Owner-relevant consequences (recorded):** post-lock correction authority is GlobalAdmin (the `/recalculate` auth), not the Leader; a zero-line month doesn't lock. **The "reopen until sent to payroll" feature is complete** (S89 Phase 1 + S90 Phase 2). Durable: SPRINT-90.md + ADR-034 + SYSTEM_TARGET §H + REFINEMENT-reopen-until-payroll-lock.md.

**Follow-ups:** a live two-connection interleave test for the B2 export↔reopen race (currently structural + committed-row-observed); a real payroll `outbox_messages` dispatcher (when monthly export goes live — today the sync POST is the delivery, the lock doesn't depend on it); the cosmetic role-claim source inconsistency in `BuildExportContext`.
