# Sprint 68 — Vacation settlement slice 1a (Backend close machinery)

| Field | Value |
|-------|-------|
| **Sprint** | 68 |
| **Status** | planned |
| **Start Date** | 2026-06-08 |
| **End Date** | — |
| **Orchestrator Approved** | no |
| **Build Verified** | no |
| **Test Verified** | no |

## Sprint Goal

Implement **ADR-033 slice 1a — the Backend vacation-settlement close machinery** (the first implementation sprint of the period-end execution layer behind the S66 D9 disposition row). At each closed VACATION entitlement-year boundary, a deterministic idempotent `SettlementCloseService` partitions the remainder into its legal buckets — **§21 transfer** (the first non-zero `carryover_in` writer), **§24 auto-payout** (recorded as disposition, no kroner, no line — manual fallback until S69 = slice 1b), and any **§34 first-4-week forfeiture-candidate** → fail-closed `PENDING_REVIEW` (D10) with a CAS-guarded manual-completion path — writing the `vacation_settlements` identity row + the settlement event family + the ADR-026 audit, all in one atomic tx. **Money stays out of StatsTid** (day/hour-count event payloads only). The Payroll exactly-once emitter + the §24 export line are **S69 = slice 1b** (gated on the still-UNVERIFIED §24 SLS contract). Refinement: `.claude/refinements/REFINEMENT-s68-slice1-ferie-settlement.md` (READY; Step-4 dual-lens 2 cycles, owner-ratified OQ-1 split / OQ-2 real-boundary / OQ-3 settlement-row-disposition).

## Entropy Scan Findings

| Check | Result | Detail |
|-------|--------|--------|
| KB path validation | CLEAN | `tools/check_docs.py` all hard checks pass (db-schema 55 tables in sync; KB INDEX 49 entries, 0 orphans/dangling; sprint inventory through S67; freshness anchored S67) |
| Pattern compliance spot-check | CLEAN | No `FindFirst("scopes")` (FAIL-001) in production code; `http://localhost` only in dev `launchSettings.json` |
| Orphan detection | CLEAN | S67 was design-only (no new src/); the S66 settlement-adjacent surfaces (`EntitlementBalanceRevalued`, the D9 disposition row) are fully wired |
| Documentation drift | CLEAN | MEMORY.md current (S67 closed+pushed; docs-debt backfill done; this slice teed up). sprints/INDEX backfilled S58–S67 (2026-06-08 `2f57e10`) |
| Quality grade review | CLEAN | S67 grades current per QUALITY.md anchor (S67) |

## Step-0 Inputs

- **Refinement** (READY): `.claude/refinements/REFINEMENT-s68-slice1-ferie-settlement.md`. Step-4 dual-lens, 2 cycles: Codex 3B/4W→verify (B2/B3 RESOLVED, residuals mechanical/absorbed at cap); Reviewer 0B/2W (advisory-lock + manual-§24-reconciliation → Step-0b scope). Owner decisions: **OQ-1 SPLIT** (1a Backend / 1b Payroll); **OQ-2 real §24 boundary**; **OQ-3 settlement-row disposition record** (no `used` mutation, ADR-032 D2 honored; readers special-case settled years; + an ADR-033 D6 clarification in TASK-6800).
- **Binding design**: ADR-033 D1–D13 (settled S67; the S67 Step-7a reversal-sequencing clarification stands — export-line uniqueness invariant, exact arithmetic slice-Step-0b).
- **Verified seams (grep-confirmed)**: `DelegationExpiryService` (BackgroundService poll shape) · `EntitlementBalanceRepository.ApplyRevaluationAsync`/`UpsertAsync` (ungated carryover-write extension point) · the ADR-018 D3 outbox `EnqueueAsync(conn,tx,...)` · the ADR-026 `IAuditProjectionMapper` family + `EventSerializer.EventTypeMap` + the `audit-projection-catalog.md` · `AccrualMath.EarnedToDate` + `absences_projection.feriedage` (the D9 operands, reused) · the ADR-032 D4 `pg_advisory_xact_lock(employee-key)` two-phase contract. Greenfield: `vacation_settlements`, `vacation_transfer_agreements`, `SettlementCloseService`.

## Plan Review (Step 0b)

| Field | Value |
|-------|-------|
| **Trigger** | MANDATORY (P1 architectural integrity + P3 event sourcing + P4 version-correctness + **schema migration** + the OQ-3 disposition data-model decision) |
| **External Codex** | invoked 2026-06-08 — cycle 1 **BLOCKED** (4B/4W/1N) → cycle 2 **APPROVED-WITH-WARNINGS** (8/8 cycle-1 RESOLVED; 2 new mechanical W absorbed) |
| **Internal Reviewer** | invoked 2026-06-08 — cycle 1 APPROVED-WITH-WARNINGS (0B/3W/4N) → cycle 2 **APPROVED** (all absorbed; 0 findings) |
| **BLOCKERs resolved before Step 1** | YES — all 4 Codex BLOCKERs + 9 WARNINGs absorbed; both lenses 0-BLOCKER at cycle 2 |

### Findings (cycle 1)

_Codex (BLOCKED — 4B/4W/1N), all on TASK-6806 + cross-task:_
- BLOCKER — TASK-6806 — §21 agreements lacked legal/state guards → added VACATION-only + ≤31-Dec-Copenhagen-deadline + transfer-cap + reject-post-settlement.
- BLOCKER — TASK-6806 — §24 pending list had no reconciliation marker (S69 double-pay risk) → audited CAS `payout_reconciled_*` marker (TASK-6801 schema) S69 must honor.
- BLOCKER — TASK-6806 — D10 was §34-forfeit-only (D10 needs §34-vs-§22) → FORFEIT-vs-DEFER outcome (DEFER = suspected-§22, stays PENDING_REVIEW until slice 4); forfeit-only ≠ "full resolution".
- BLOCKER — TASK-6806 — manual completion not pinned atomic → ONE-atomic-tx (CAS + forfeit_days + event + audit) + forced-rollback + concurrent-winner.
- WARNING — TASK-6802 — only 8 of 9 events (omitted `SaerligeFeriedagePaidOut`) → added as the 9th (DEFINE-ONLY, slice 2).
- WARNING — TASK-6805 — sequential-only idempotency → concurrent-poller (multi-instance) test + lock-then-recheck.
- WARNING — TASK-6807 — PENDING_REVIEW reader semantics + D9 mapping unpinned → specified (PENDING_REVIEW §34 remainder ≠ 0; D9-mapping consistent across all readers).
- WARNING — TASK-6804 — carryover "provenance-keyed" underspecified → DERIVED-from-`transfer_days` (idempotent by construction).
- NOTE — TASK-6809 outside the 1a gate → marked explicit.

_Internal Reviewer (APPROVED-WITH-WARNINGS — 0B/3W/4N):_
- WARNING — TASK-6803 — mapper location is `Infrastructure/AuditMappers/` (cross-process precedent `EmployeeEntitlementEligibilitySet`), NOT `Backend.Api/` → scope path + KB-ref corrected.
- WARNING — TASK-6804 — the BackgroundService-dispatched `audit_projection` write is a NEW pattern → pinned read-your-write + forced-rollback from the dispatch site.
- WARNING — TASK-6802 — `EventSerializerCoverageTests` round-trips ALL classes incl. DEFINE-ONLY → added the round-trippability constraint.
- NOTE — D9-mapping verified correct at source; `visibility_scope = TENANT_TARGETED` + `target_org_resolution = employee→primary_org_id`; drop the advisory-lock "harmlessness" escape (require the lock); TASK-6809 deferral well-formed.

### Resolution

Cycle 1: all 4 Codex BLOCKERs + 7 WARNINGs absorbed (TASK-6801/6802/6803/6804/6805/6806/6807/6809). **Cycle 2 (verify): Reviewer APPROVED (0 findings); Codex APPROVED-WITH-WARNINGS — all 8 cycle-1 findings RESOLVED + 2 new mechanical WARNINGs (DEFER-path atomicity parity; the `review_disposition`/`payout_reconciled_*` schema CHECK constraints) — both absorbed.** Both lenses 0-BLOCKER at cycle 2; the cycle-2 residuals were one-line mechanical tightenings, absorbed without a cycle-3 (no new BLOCKERs; the substantive design is dual-lens-verified clean). **Plan READY for Step-1 decompose** pending owner go-ahead.

## Architectural Constraints Verified

_(to verify at each task + sprint close)_

- [ ] P1 — Architectural integrity (the Backend close bounded context; the Payroll emitter stays S69; outbox-only cross-context coupling per ADR-018 D3)
- [ ] P2 — Deterministic settlement quantities (pure of the immutable snapshot; `AccrualMath.EarnedToDate` + recorded feriedage; replay-stable marquee)
- [ ] P3 — Event sourcing/auditability (the settlement event family on `employee-{id}`; ADR-026 sync-in-tx audit from the BackgroundService dispatch site)
- [ ] P4 — OK-version correctness (entry-date-stamped recorded feriedage, ADR-032 D2; the §21/§24 boundary on the Europe/Copenhagen business clock, NOT raw `CURRENT_DATE`)
- [ ] P5 — Integration isolation (money-free; no Payroll line in 1a; SLS owns the rate)
- [ ] P6 — Payroll correctness (the §24 disposition recorded; the line + SLS contract are S69)
- [ ] P7 — Security/access control (the §21 HR-agreement + the D10 manual-completion endpoints: HROrAbove + OrgScope + If-Match + audit; cross-org rejected)
- [ ] P8 — CI/CD (schema regen + check_docs green; the test pyramid)
- [ ] P9 — UX (the thin HR §21-agreement admin surface, if in scope)

## Task Log

### TASK-6800 — Sprint open + ADR-033 D6 clarification + business-timezone interim ruling

| Field | Value |
|-------|-------|
| **ID** | TASK-6800 |
| **Status** | planned |
| **Agent** | Orchestrator (docs/KB) |
| **Components** | ADR-033 (D6 clarification + D3 timezone interim) · KB INDEX · ROADMAP (S68 promotion) · this log |
| **KB Refs** | ADR-033 D3/D6, ADR-032 D2, ROADMAP follow-up (v) |

**Description**: (a) Add the **ADR-033 D6 clarification** (owner-ratified OQ-3): the §24/§34 disposition is recorded **per-bucket on the `vacation_settlements` row** (the authoritative record); only §21 writes `entitlement_balances.carryover_in`; balance readers special-case a SETTLED entitlement-year (show `remaining = 0` + the disposition from the active SETTLED-sequence snapshot; the ferieår's earlier monthly `saldo` is NOT retroactively zeroed). **No `used` mutation** (ADR-032 D2 pins `used` to recorded absences). (b) Record the **business-timezone interim** (the §21/§24 boundary derives from a Europe/Copenhagen business clock via a TimeProvider seam, ahead of the system-wide (v) ADR; the poll trigger may read wall-clock `CURRENT_DATE` — trigger only, not a replayed quantity — per ADR-033 D3). (c) ROADMAP S68 promotion (Tier-1; slices 2–4 shift forward).

**Validation Criteria**:
- [ ] ADR-033 D6 carries the disposition-persistence clarification; the S66 D9 `expiring`-vs-full-disposition mapping pinned (D9 `expiring` = the over-cap §34-candidate bucket only).
- [ ] check_docs green; KB INDEX ADR-033 row unchanged (clarification is in-body).

---

### TASK-6801 — Schema: `vacation_settlements` + `vacation_transfer_agreements` (+ audit tables)

| Field | Value |
|-------|-------|
| **ID** | TASK-6801 |
| **Status** | complete 2026-06-08 — 4 tables (additive, +4 → 59; `check_docs` green); partial-unique-on-state + composite PK + all CHECK constraints + audit tables verified. Orchestrator merge-fix: `recorded_by`/`payout_reconciled_by` `UUID`→`TEXT` (match `users.user_id` + `actor_id`). |
| **Agent** | Data Model (extended into `docker/postgres/init.sql` schema + Infrastructure, cross-domain authorized; Orchestrator-approved schema) |
| **Components** | init.sql · `docs/generated/db-schema.md` (regen) |
| **KB Refs** | ADR-033 D5/D8, ADR-018 D8 (partial-unique live-row), ADR-019 D8 (version-transition audit columns), ADR-020 (versioned natural key — for the §21 record's evidence/dating shape) |

**Description**: `vacation_settlements` — **composite PK `(employee_id, entitlement_type, entitlement_year, sequence)`** + **partial-unique index `(employee_id, entitlement_type, entitlement_year) WHERE settlement_state <> 'REVERSED'`** (single-active, ADR-018 D8); columns: `settlement_state` (CHECK ∈ PENDING_REVIEW/SETTLED/REVERSED), `trigger` (CHECK ∈ YEAR_END/TERMINATION), the immutable snapshot (`jsonb`), per-bucket disposition day-counts (`transfer_days`/`payout_days`/`forfeit_days`), a **§24 manual-payout-reconciliation marker** (`payout_reconciled_at`/`payout_reconciled_by` — the audited CAS field S69's emitter honors so it never double-pays a manually-handled case; Codex B), a **PENDING_REVIEW review-disposition** field (the operator FORFEIT-vs-DEFER outcome — DEFER = suspected-§22, stays pending until slice 4; Codex B), `version` (If-Match), timestamps. `vacation_transfer_agreements` — keyed `(employee_id, entitlement_year, entitlement_type)`; `transfer_days` + written-agreement evidence (`agreement_date`, `recorded_by`) + `version`. Both gain `*_audit` tables (ADR-019 D8 columns). Regen `db-schema.md` via `tools/generate_db_schema.py`.

**Validation Criteria**:
- [ ] Greenfield seed boots clean; `check_docs.py` db-schema in sync (table count +4).
- [ ] The partial-unique enforces single-active across YEAR_END/TERMINATION; the composite PK lets history sequences coexist (a schema D-test).
- [ ] Schema CHECK constraints: `review_disposition IN ('FORFEIT','DEFER')` (nullable until resolved); `payout_reconciled_at`/`payout_reconciled_by` paired-nullable (both NULL or both set) (Codex cycle-2 W).
- [ ] **High-risk (schema migration)** → Step-5a Codex override.

---

### TASK-6802 — Settlement event family + immutable-snapshot value object + EventSerializer

| Field | Value |
|-------|-------|
| **ID** | TASK-6802 |
| **Status** | complete 2026-06-08 — 9 events + the `VacationSettlementSnapshot` value object + EventSerializer (all 9 registered); `dotnet build` 0E; `EventSerializerCoverageTests` 2/2 green (round-trip incl. the DEFINE-ONLY classes — Snapshot modeled nullable + value-types defaulted per the round-trip constraint). |
| **Agent** | Data Model |
| **Components** | `src/SharedKernel/**/Events/**`, `**/Models/**`, `Infrastructure/EventSerializer.cs` |
| **KB Refs** | ADR-033 D5, PAT-001 (immutable init-only), PAT-004 (DomainEventBase), DEP-003 (EventSerializer type map) |

**Description**: The **immutable input-snapshot** value object (D3: recorded per-absence feriedage, closed-year balance, dated config, the transfer-agreement, impediment status — the pure settle-time inputs). Event classes (each `DomainEventBase`, stream `employee-{id}`, carrying the snapshot + bucket day-count + `(identity, sequence)`): **EMITTED in 1a** — `VacationCarryoverExecuted` (§21), `VacationAutoPaidOut` (§24), `VacationForfeitedToFeriefond` (§34, via the D10 manual path), `SettlementManualReviewFlagged` (D10). **DEFINE-ONLY in 1a** (class + EventTypeMap entry, NO mapper/emit) — `SettlementReversed`, `FeriehindringTransferred`, `FeriehindringPaidOut`, `SaerligeFeriedagePaidOut` (§15 stk.2/§17, slice 2 — Codex W), `TerminationSettled`. ALL **nine** registered in `EventSerializer.EventTypeMap` (Data Model's declared scope — not cross-domain). The DEFINE-ONLY classes must be **round-trippable under the coverage test's `GetUninitializedObject` path** (Reviewer W): their `required` members are reference/collection types the test prefills (or the snapshot value object serializes cleanly from an uninitialized instance) — registering a not-yet-emitted class must not break replay coverage.

**Validation Criteria**:
- [ ] `EventSerializerCoverageTests` green (all **9** settlement types registered AND round-trip clean, incl. the DEFINE-ONLY classes — Reviewer round-trippability).
- [ ] Models init-only; events carry the `(identity, sequence)` + snapshot; no I/O.
- [ ] P3 → MANDATORY Reviewer.

---

### TASK-6803 — ADR-026 audit mappers + catalog rows for the 4 emitted events

| Field | Value |
|-------|-------|
| **ID** | TASK-6803 |
| **Status** | complete 2026-06-08 — 4 audit mappers in `Infrastructure/AuditMappers/` (TENANT_TARGETED, `ResolvedTargetOrgId`, null-tolerant per the `e0d1dc3` lesson); Orchestrator wired the DI (Program.cs) + the 4 catalog rows (58 total). build 0E. |
| **Agent** | Data Model (extended into `src/Infrastructure/StatsTid.Infrastructure/AuditMappers/**`, cross-domain authorized; the catalog doc row is Orchestrator-written) |
| **Components** | the mapper impls in `Infrastructure/AuditMappers/` (the cross-process precedent) · `docs/operations/audit-projection-catalog.md` (Orchestrator) |
| **KB Refs** | ADR-026, ADR-025 D3 (GDPR), the S59 `EmployeeEntitlementEligibilitySet` Infrastructure-mapper precedent, the `e0d1dc3` null-tolerance lesson |

**Description**: `IAuditProjectionMapper<T>` for the 4 EMITTED events, in **`src/Infrastructure/.../AuditMappers/`** (the cross-process precedent — `EmployeeEntitlementEligibilitySet` / `RetroactiveCorrectionRequested` — NOT the endpoint-dispatched `Backend.Api/AuditMappers/` where the S66 `EntitlementBalanceRevalued` lives; settlement dispatches from the `SettlementCloseService` BackgroundService, not an endpoint — Reviewer W). Each mapper is **null-tolerant of its `required` members** (the `Activator.CreateInstance` catalog-parity path bypasses `required` init — the S66 `e0d1dc3` NRE class). `visibility_scope = TENANT_TARGETED` with `target_org_resolution = employee → users.primary_org_id` (Reviewer NOTE — settlement is a specific employee's tenant-scoped data; D12's "GLOBAL" governs the rule/config, not the per-employee audit row). The DEFINE-ONLY events get NO mapper (gold-plating — they ride their automation slice). Orchestrator adds the catalog rows at validation (the catalog is test-parsed — mapper + row land together).

**Validation Criteria**:
- [ ] The catalog-parity / per-class visibility test green (no `Activator.CreateInstance` NRE).
- [ ] 4 mappers (emitted set only); catalog rows match.

---

### TASK-6804 — Repositories + provenance-keyed carryover-write + the atomic settlement tx

| Field | Value |
|-------|-------|
| **ID** | TASK-6804 |
| **Status** | complete 2026-06-08 — 2 repos + derived `carryover_in` writer + `VacationSettlementService.SettleAsync` (atomic pass under the ADR-032 D4 advisory lock; partition matches the S66 D9 `expiring`; in-lock re-check idempotent). **Step-5a dual-lens, 5 cycles** (Codex cycle-1 3B/3W + Reviewer 1B → all fixed; the config-resolution seam took 3 further cycles): fixed = config-resolution→D9 chain, YEAR_END-only guard, 23505→SAVEPOINT+constraint-discrimination, carryover skip-when-zero, negative-days clamp+DB-CHECK, rounding ToEven, version INT→BIGINT, the resetMonth bootstrap→D9 year-start anchor probe. **Principled acceptance (owner-ratified 2026-06-08):** the residual cycle-5 finding (probe anchors `{Jan-1 E, Sep-1 E}` not byte-identical to D9's `{Jan-1 Y, Sep-1 Y−1, Sep-1 Y}`) is ACCEPTED — the 2-anchor subset is *correct* for a known `entitlementYear` (D9's extra `Sep-1 Y−1` is for its calendar-view-year ambiguity and would resolve the *prior* ferieår's config; the dated-primary read stays authoritative; the branch is unreachable without a configless-VACATION agreement, which does not exist). build 0E. |
| **Agent** | Infrastructure (cross-domain authorized: new repos + `EntitlementBalanceRepository` extension + the atomic pass) |
| **Components** | `VacationSettlementRepository`, `VacationTransferAgreementRepository`, `EntitlementBalanceRepository` (extension), the settlement-pass orchestration |
| **KB Refs** | ADR-033 D5/D6, ADR-018 D3 (atomic outbox), ADR-032 D2/D4 (recorded feriedage + advisory lock), ADR-026 D13 (sync-in-tx projection), ADR-013 (no auto-cascade) |

**Description**: The two new repos (`(conn, tx)` overloads, ADR-018 D3). Extend `EntitlementBalanceRepository` with the **provenance-keyed `carryover_in` writer** (the `ApplyRevaluationAsync` ungated-upsert pattern, NOT the booking guard) — the first non-zero `carryover_in` writer. The carryover is **DERIVED from the settlement row's `transfer_days`** (idempotent by construction — a retry recomputes the same value, never incrementally re-adds §21; source-keyed so later §22 composition in slice 4 stays deterministic; Codex W); slice-1 total = §21 only (the §22 term is 0). The **atomic settlement pass**: capture the immutable snapshot → partition the closed-year remainder → write the settlement row + the §21 carryover + the per-bucket disposition + emit the events + **the ADR-026 `audit_projection` row sync-in-tx FROM the BackgroundService dispatch site** (via `IAuditProjectionMapperRegistry.TryMap` + `AuditProjectionRepository`, NOT an endpoint dispatch — a NEW dispatch pattern, Reviewer W), ALL under `pg_advisory_xact_lock(employee-key)` (the ADR-032 D4 contract — serialize vs a racing Skema-save/revaluation writing `used` on the closing year's row while the close writes next-year `carryover_in`; Reviewer W1). Quantities are a pure function of the snapshot (replay-stable).

**Validation Criteria**:
- [ ] The settlement pass is ONE atomic tx (balance + row + events + **`audit_projection` from the BackgroundService dispatch site**); a **read-your-write** assert in-tx + a **forced-rollback** D-test prove all-or-nothing incl. the audit row (Reviewer W).
- [ ] The carryover-write is **DERIVED from `transfer_days`** (idempotent by construction — a retry never double-adds §21); a re-run D-test proves stability (Codex W).
- [ ] The advisory-lock contract holds — a **concurrency D-test** (the next-year-carryover-write vs the closing-year revaluation interleaving), NOT a "documented harmlessness" escape (Reviewer NOTE).
- [ ] **High-risk (atomic tx + carryover-write)** → Step-5a Codex override.

---

### TASK-6805 — `SettlementCloseService` BackgroundService

| Field | Value |
|-------|-------|
| **ID** | TASK-6805 |
| **Status** | complete 2026-06-08 — `SettlementCloseService` (DelegationExpiryService poll shape; employee×config-driven enumeration incl. missing-`entitlement_balances`-row; year-band floor `GREATEST(2020, hireYear)` — Orchestrator fix, no pre-hire zero-settlements; Copenhagen business-clock boundary `31 Dec E+1`; `ReadCommitted` for the advisory-lock-then-recheck; concurrent-poller-safe). DI wired (`AddHostedService`). **Step-5a: the configless-*current*-agreement enumeration edge (Codex W4) ACCEPTED** — same unreachable edge as the TASK-6804 acceptance (no configless-VACATION agreement exists; `SettleAsync` is more robust than the enumeration). build 0E. |
| **Agent** | Infrastructure (cross-domain authorized: the BackgroundService + DI) |
| **Components** | `SettlementCloseService`, `Backend.Api/Program.cs` (DI) |
| **KB Refs** | ADR-033 D3, the `DelegationExpiryService` shape |

**Description**: The `DelegationExpiryService` poll shape. **Due-tuple enumeration**: every employee with a closed VACATION entitlement-year — INCLUDING those with NO `entitlement_balances` row (enumerate from the employment/entitlement-config set, NOT balance-row-driven; Codex W4) — skipping any with an active settlement row in {SETTLED, PENDING_REVIEW}. **Two date uses**: the poll TRIGGER reads wall-clock `CURRENT_DATE` (is-it-time-to-check); the §21/§24 **boundary** comparison uses the **Europe/Copenhagen business date** (TimeProvider seam), never `CURRENT_DATE`. Idempotent single-settle under CONCURRENT pollers (multi-instance): lock-then-recheck (the `pg_advisory_xact_lock` + a re-check for an active row inside the lock) with the partial-unique key as the backstop (a benign unique-conflict is swallowed) → exactly one settlement + one event family; a missed/late poll still settles exactly once (Codex W).

**Validation Criteria**:
- [ ] A due closed-VACATION-year settles exactly once — proven under **concurrent pollers** (two racing pollers → ONE settlement row + ONE event family; lock-then-recheck + partial-unique backstop), not just sequential re-polls (Codex W).
- [ ] An employee with NO `entitlement_balances` row is still enumerated + settled (missing-row D-test).
- [ ] Both sides of the 31 Dec Copenhagen boundary pinned (a FixedTimeProvider D-test — PAT-008).

---

### TASK-6806 — §21 HR-agreement endpoint + D10 manual-completion endpoint + §24-pending list

| Field | Value |
|-------|-------|
| **ID** | TASK-6806 |
| **Status** | complete 2026-06-08 (cycle-2: the 4 substantive fixes RESOLVED — advisory lock, future-date, forfeitDays bound, terminated-employee org-resolution; the residual deadline/cap reset-month fallback when the *current* agreement is configless is the SAME unreachable configless-VACATION-agreement edge ACCEPTED for 6804/6805 — for all reachable cases the endpoint's dated config == the snapshot's) — `VacationSettlementEndpoints` (§21 POST/PUT + D10 resolve FORFEIT/DEFER + §24 payout-pending GET + reconcile-payout POST; all `RequireAuthorization("HROrAbove")` + `OrgScopeValidator` + admin-strict If-Match + ADR-026 audit; FindAll/FAIL-001-safe). **Step-5a: Codex 2B/4W + Reviewer 2W → all fixed** (§21 deadline year `31 Dec E`→`E+1` geometry; the §21 write takes the `EmployeeConsumptionLock` before its check+write closing the poller race; future-date 422; dated cap OK-version via `ResolveVersion(ferieaarStart)`; FORFEIT `forfeitDays == flagged remainder`; terminated-employee org-resolution no longer 500s). CAS state-transition in the endpoint = design-sanctioned (ADR-033 D10), repo-hoist deferred to slice 4. build 0E. |
| **Agent** | Backend API (cross-domain authorized: `Backend.Api/Endpoints` + Infrastructure repo + Security audit) |
| **Components** | new `VacationSettlementEndpoints` / `VacationTransferAgreementEndpoints` |
| **KB Refs** | ADR-033 D8/D10, ADR-019 D2 (admin-strict If-Match), ADR-026 (audit), ADR-025 D6 (settlement GLOBAL — no per-institution override) |

**Description**: (a) `POST/PUT /api/vacation-transfer-agreements/{employeeId}` — the §21 record (HROrAbove + `OrgScopeValidator` + If-Match + ADR-026 audit; cross-org rejected; distinct from the §7 forskud manager-approval). **Legal/state guards (Codex B):** VACATION-only; agreement date ≤ the **31 Dec Copenhagen deadline** (§21 stk.2); `transfer_days` ≤ the statutory transfer cap (the available 5th-week tranche / `carryover_max`); **REJECT any mutation once an active settlement row exists** for that `(emp, VACATION, year)` — you cannot agree a transfer for an already-settled year. (b) `POST /api/vacation-settlements/{id}/resolve` — the **D10 manual-completion** with **TWO outcomes (Codex B): FORFEIT** (operator confirms §34 → ONE atomic tx: CAS PENDING_REVIEW→SETTLED + `forfeit_days` + `VacationForfeitedToFeriefond` event/outbox + ADR-026 audit; If-Match/CAS winner guard, loser 409s) **OR DEFER** (operator marks a suspected §22-feriehindring case → likewise ONE CAS tx: `review_disposition = DEFER` + version bump + ADR-026 audit, stale/concurrent If-Match → 409; the row STAYS PENDING_REVIEW, audited, until slice 4 models the impediment signal — Codex cycle-2 W). §34-forfeiture-only is explicitly NOT "full D10 resolution" (the §34-vs-§22 split per ADR-033 D10 is only completable once §22 is modeled). (c) `GET` the **§24-payout-pending set** + a **§24 manual-reconciliation marker (Codex B):** an audited CAS path for an operator to mark a §24 bucket "manually settled" (the fallback line was issued out-of-band); **S69's emitter MUST honor this marker** (no re-emit / no double-pay). All `RequireAuthorization`.

**Validation Criteria**:
- [ ] §21: RBAC + OrgScope + If-Match D-tests; cross-org 403; missing/stale If-Match 412; **VACATION-only + ≤31-Dec-Copenhagen-deadline + transfer-cap + reject-post-settlement** each D-tested (Codex B).
- [ ] D10 FORFEIT is ONE atomic tx (CAS + `forfeit_days` + event + audit) — forced-rollback proof + concurrent-winner (loser 409s, exactly one `VacationForfeitedToFeriefond`) (Codex B).
- [ ] D10 DEFER keeps the row PENDING_REVIEW (audited), NOT SETTLED — forfeiture-only is never labeled full resolution (Codex B); DEFER is itself ONE CAS tx (`review_disposition` + version + audit), stale/concurrent → 409 (Codex cycle-2 W).
- [ ] The §24 manual-reconciliation marker is an audited CAS write; a D-test asserts a marked bucket is honored (a stub S69-consumer query excludes it) (Codex B + Reviewer W2).
- [ ] P7 → MANDATORY Reviewer + (auth/endpoint) Step-5a discretion.

---

### TASK-6807 — Balance-reader settled-year handling + the carryover-reader regression

| Field | Value |
|-------|-------|
| **ID** | TASK-6807 |
| **Status** | complete 2026-06-08 — `BalanceEndpoints` settled-year reader semantics: SETTLED → `remaining = 0` + recorded disposition from the active-sequence snapshot (not re-derived); PENDING_REVIEW → §34 remainder shown pending (NOT 0); unsettled unchanged; earlier monthly `saldo` preserved; D9 `expiring` ≡ recorded `forfeit_days` by construction (ToEven). Carryover read+write-path (`CheckAndAdjustAsync` ceiling) confirmed correct as-is. Step-5a: clean (NOTE: per-type N+1 settlement read mirrors existing pattern). build 0E. |
| **Agent** | Backend API (cross-domain authorized: `BalanceEndpoints` + the year-overview reader) |
| **Components** | `BalanceEndpoints` (`/summary`, `/year-overview`, the D9 row), `CheckAndAdjustAsync` quota guard |
| **KB Refs** | ADR-033 D6 (clarified), ADR-030 D9, ADR-032 D2 |

**Description**: Readers special-case the settlement STATE: a **SETTLED** year shows `remaining = 0` + the disposition from the active sequence's **snapshot** (deterministic, not a live recompute); a **PENDING_REVIEW** year shows the auto-resolved §21/§24 buckets as disposed BUT the unresolved §34 remainder as **still pending (flagged, NOT counted as 0)** — Codex W; the ferieår's **earlier monthly `saldo` is preserved** (not retroactively zeroed). Pin the D9 `expiring`-vs-full-disposition mapping (D9 `expiring` = the over-cap §34-candidate bucket ONLY, never the §21+§24+§34 total). The **first-non-zero-`carryover_in` regression** covers BOTH the read surfaces AND the **WRITE path** — `CheckAndAdjustAsync`'s quota guard now legitimately raises the next-year bookable ceiling by the carried days (Reviewer NOTE).

**Validation Criteria**:
- [ ] A SETTLED year reads `remaining = 0` + disposition; a **PENDING_REVIEW year shows the §34 remainder pending, NOT 0** (Codex W); an unsettled year unchanged; earlier monthly saldo intact.
- [ ] The D9 `expiring`-vs-disposition mapping is consistent across `/summary`, `/year-overview`, the D9 row, AND `CheckAndAdjustAsync` (Codex W).
- [ ] A non-zero `carryover_in` correctly raises the next-year bookable ceiling (a write-path regression D-test), and every read surface displays it.

---

### TASK-6808 — Tests (marquee + atomic + idempotency + regression)

| Field | Value |
|-------|-------|
| **ID** | TASK-6808 |
| **Status** | planned |
| **Agent** | Test & QA |
| **Components** | `tests/**` |
| **KB Refs** | PAT-008 (FixedTimeProvider WAF), FAIL-002 (Docker testcontainer churn), the marquee replay precedent |

**Description**: The slice-1a test bar — marquee **replay-determinism** (settled quantities pure of the snapshot, byte-identical under replay); **atomic / forced-rollback**; **idempotent single-settle** (repeated polls); **missing-balance-row** enumeration; the **first-non-zero-`carryover_in`** regression (read + write); the **D10 manual-completion CAS race**; the **§21 endpoint** RBAC/If-Match/cross-org; the **Copenhagen boundary** (both sides of 31 Dec, FixedTimeProvider); EventSerializer + catalog-parity coverage.

**Validation Criteria**:
- [ ] All new D-tests green; the pyramid green (unit + regression + FE + smoke).
- [ ] Runs AFTER all implementation tasks (Phase 3).

---

### TASK-6809 — (thin) HR §21-agreement admin surface

| Field | Value |
|-------|-------|
| **ID** | TASK-6809 |
| **Status** | planned — **explicitly OUTSIDE the 1a completion gate** (Codex NOTE); the §21 API (TASK-6806) is the deliverable. Ship only if the sprint has room; otherwise a thin polish follow-up. |
| **Agent** | UX |
| **Components** | `frontend/**` — a thin HR form to record a §21 transfer agreement (consumes TASK-6806's API as-is) |
| **KB Refs** | docs/FRONTEND.md, ADR-011 (tokens) |

**Description**: A minimal HROrAbove admin form to record a §21 written transfer agreement (employee, year, days, agreement date) against the TASK-6806 endpoint — tokens-only, If-Match-aware. No employee/manager signing flow (the law's default is §24 auto-payout; this captures the exception). Deferrable: 1a's deliverable is the API; the form is the thin operator surface.

**Validation Criteria**:
- [ ] The form records an agreement via the API (vitest); RBAC-gated; tokens-only.

## Legal & Payroll Verification

| Check | Status | Notes |
|-------|--------|-------|
| Agreement rules match legal requirements | pending | §21 transfer (Ferielov §21, 31 Dec deadline) / §24 auto-payout / §34 forfeiture — the verified S67 spine (`vacation-settlement-law-research.md`) |
| Wage type mappings produce correct SLS codes | N/A (1a) | The §24 day-count LINE + SLS contract are S69 = slice 1b |
| Overtime/supplement calculations are deterministic | N/A | — |
| Absence effects on norm/flex/pension are correct | pending | The settled quantities reuse recorded feriedage (ADR-032 D2) — no re-valuation |
| Retroactive recalculation produces stable results | pending | Replay-determinism marquee (settled = pure fn of the snapshot) |

## External Review (Step 7a)

_pending — sprint-end dual-lens after implementation._

## Test Summary

_pending — baseline carried from S67: 631 unit + 466 regression + 5 smoke + 176 FE = 1278._

## Sprint Retrospective

_pending_
