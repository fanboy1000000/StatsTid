# Sprint 68 — Vacation settlement slice 1a (Backend close machinery)

| Field | Value |
|-------|-------|
| **Sprint** | 68 |
| **Status** | complete — 2026-06-08 |
| **Start Date** | 2026-06-08 |
| **End Date** | 2026-06-08 |
| **Orchestrator Approved** | yes (2026-06-08) |
| **Build Verified** | yes (0E) |
| **Test Verified** | local full-pyramid GREEN (Docker-gated suites run against live Postgres) — 645 unit + 511 regression (509+2 FAIL-002 isolation-cleared 8/8) + 5 smoke + 176 FE = 1337; GitHub CI runs on push (CI-health gate green at close: S67 run 27121201014) |

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

_(verified at sprint close; dual-lens Step-7a APPROVED)_

- [x] P1 — Architectural integrity (Backend close bounded context; Payroll emitter stays S69; outbox-only cross-context coupling per ADR-018 D3) — Reviewer-confirmed
- [x] P2 — Deterministic settlement quantities (pure fn of the immutable snapshot; replay-stable marquee green) — both lenses
- [x] P3 — Event sourcing/auditability (settlement event family on `employee-{id}`; ADR-026 sync-in-tx audit from the BackgroundService dispatch site; all 9 events registered)
- [x] P4 — OK-version correctness (entry-date-stamped recorded feriedage, ADR-032 D2; §21/§24 boundary on the Europe/Copenhagen business clock; **VACATION `reset_month` pinned 9 — Step-7a B1 fix** closes the live-vs-dated boundary divergence)
- [x] P5 — Integration isolation (money-free; no Payroll line in 1a; SLS owns the rate) — Reviewer-confirmed no kroner/rate field anywhere
- [x] P6 — Payroll correctness (§24 disposition recorded; line + SLS contract are S69; §24 manual-reconciliation marker S69's emitter must honor)
- [x] P7 — Security/access control (§21 + D10 endpoints: HROrAbove + OrgScope + If-Match + audit; cross-org 403; FAIL-001-safe). **Known limitation:** terminated-employee resolution on §21/reconcile deferred to slice 3 (B2)
- [x] P8 — CI/CD (schema regen + check_docs green, 59 tables; full pyramid; CI-health gate at push)
- [N/A] P9 — UX (TASK-6809 thin HR §21 admin surface deferred — explicitly outside the 1a gate; the API is the 1a deliverable)

## Task Log

### TASK-6800 — Sprint open + ADR-033 D6 clarification + business-timezone interim ruling

| Field | Value |
|-------|-------|
| **ID** | TASK-6800 |
| **Status** | complete 2026-06-08 — ADR-033 D3 clarification (the go-live gate) + D6 clarification (OQ-3 disposition-on-the-row) added; the business-timezone interim recorded (Copenhagen boundary via TimeProvider, ahead of the (v) ADR). **Step-7a addendum:** the D3 boundary note now records the VACATION `reset_month=9` enforcement (B1). |
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
| **Status** | complete 2026-06-08 — the slice-1a test bar green: `VacationSettlementServiceTests` (atomic/forced-rollback, replay-determinism, half-timer flat-day parity, idempotent single-settle, carryover-raises-ceiling, no-balance-row), `VacationSettlementEndpointTests` (§21 RBAC/If-Match/cross-org/deadline/cap/post-settlement-409, D10 FORFEIT/DEFER CAS, §24 reconcile), `SettlementCloseServiceBoundaryTests` (Copenhagen 31-Dec boundary + D13 go-live gate), `SettledYearReaderTests`. **+ Step-7a fix-forward D-tests:** `SettlementSchemaConstraintTests` (8: the 3 new W4 CHECKs) + `Post_VacationConfig_NonNineResetMonth_Returns422` (B1). |
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
| **Status** | **DEFERRED** (2026-06-08) — outside the 1a completion gate (Codex NOTE); the §21 API (TASK-6806) is the slice-1a deliverable and ships complete. The thin HR admin form is a polish follow-up (ROADMAP); FE count unchanged at 176. |
| **Agent** | UX |
| **Components** | `frontend/**` — a thin HR form to record a §21 transfer agreement (consumes TASK-6806's API as-is) |
| **KB Refs** | docs/FRONTEND.md, ADR-011 (tokens) |

**Description**: A minimal HROrAbove admin form to record a §21 written transfer agreement (employee, year, days, agreement date) against the TASK-6806 endpoint — tokens-only, If-Match-aware. No employee/manager signing flow (the law's default is §24 auto-payout; this captures the exception). Deferrable: 1a's deliverable is the API; the form is the thin operator surface.

**Validation Criteria**:
- [ ] The form records an agreement via the API (vitest); RBAC-gated; tokens-only.

## Legal & Payroll Verification

| Check | Status | Notes |
|-------|--------|-------|
| Agreement rules match legal requirements | verified | §21 transfer (Ferielov §21, 31-Dec Copenhagen deadline) / §24 auto-payout / §34 forfeiture — the verified S67 spine (`vacation-settlement-law-research.md`). **Step-7a B1 added the statutory 1-Sep ferieår (`reset_month=9`) enforcement** (LBK 230/2021) |
| Wage type mappings produce correct SLS codes | N/A (1a) | The §24 day-count LINE + SLS contract are S69 = slice 1b |
| Overtime/supplement calculations are deterministic | N/A | — |
| Absence effects on norm/flex/pension are correct | verified | The settled quantities reuse recorded feriedage (ADR-032 D2) — no re-valuation; Reviewer-confirmed no `used` mutation / balance zeroing |
| Retroactive recalculation produces stable results | verified | Replay-determinism marquee green (settled = pure fn of the immutable snapshot, `ToEven`-matched to D9 `expiring`) |

## External Review (Step 7a)

Dual-lens, post-implementation (against commit `1fd77ed` + the working-tree fix-forward below).

### Cycle 1

**External Codex — BLOCKED (2B/3W/1N)** (`.claude/reviews/SPRINT-68-step7a-codex-raw.log`):

- **BLOCKER 1** — poller boundary (`SettlementCloseService.IsBoundaryPassed`) resolves `reset_month` from the employee's *current* agreement, while settlement valuation uses the *dated* closed-year config; flagged the `reset_month==1`→`31 Dec E` geometry. → **Cycle-1 owner-ratified as a principled acceptance** (reset_month uniform 9 in the seed). → **Cycle-2 Codex escalated to ACCEPTANCE-UNSOUND** with a sharper, CORRECT point: the seed is uniform-9, but a GlobalAdmin `POST /api/admin/entitlement-configs` can create a *new* VACATION config (new `ok_version` natural key) with an arbitrary `reset_month` — the ADR-021 immutability guard only blocks *changing* it on an existing key. That makes the poller-vs-dated divergence reachable. A genuine missed fact, not thrash. → **FIXED BY ENFORCEMENT (owner-ratified 2026-06-08).** The Danish vacation year is statutorily fixed at 1 Sep–31 Aug (samtidighedsferie, LBK 230/2021), so VACATION `reset_month` is *always* 9 and a non-9 VACATION config is legally malformed. Pinned it true-by-construction: (a) DB CHECK `entitlement_configs_vacation_reset_month` (`entitlement_type <> 'VACATION' OR reset_month = 9`) on every fresh-DB write path + a `schema_migrations`-guarded idempotent ALTER (`s68-vacation-reset-month-check`, remediate-then-DROP-then-ADD) for the legacy-DB upgrade path (Reviewer follow-up); (b) a friendly 422 endpoint guard in the POST handler (PUT already covered by the reset_month-immutability guard); (c) test `Post_VacationConfig_NonNineResetMonth_Returns422`. CARE_DAY/SENIOR_DAY (calendar-year `reset_month` 1) and SPECIAL_HOLIDAY are unconstrained. With `reset_month` provably uniform-9 for VACATION, the poller's live-config read can no longer diverge from the dated snapshot and the `reset_month==1` geometry is unreachable.
- **BLOCKER 2** — terminated/inactive employees are unreachable through the settlement endpoints: `OrgScopeValidator.ValidateEmployeeAccessAsync` → `UserRepository.GetByIdAsync` filters `is_active = TRUE`, so an authorized HR/GlobalAdmin gets **403** on §21 write / D10 resolve / §24 reconcile for an employee who has since left. → **DEFERRED to slice 3 (owner-ratified 2026-06-08).** The 403 is the surface symptom of an unmodeled domain: the leaver/TERMINATION settlement (Ferielov §26 payout-at-termination, §7 stk.1 cap, the SLS payout handoff, AND the HR access path for a departed employee). Slice 1a auto-creates settlements for ACTIVE employees only (the poller enumerates `is_active = TRUE`); the active→terminate→manual-complete window is the gap. Band-aiding the 403 alone would pre-empt the slice-3 design. **Known limitation recorded** (the manual operator fallback covers the gap until slice 3); **"deep-dive: what happens when an employee leaves" elevated to the named next-sprint candidate = ADR-033 slice 3 / TERMINATION** (ROADMAP).
- **WARNING 5** — `Settlement:GoLiveDate` documented strict-ISO but parsed with permissive `DateOnly.TryParse` (locale/ambiguous forms could ACTIVATE automation on a misread date). → **FIXED**: `TryParseExact("yyyy-MM-dd")`; a non-ISO value fails closed to dormant (`SettlementCloseService.cs`).
- **WARNING 4** — schema lacked non-negative-bucket / positive-counter / state-disposition-coupling integrity checks (`SETTLED+DEFER`, negative days were DB-valid). → **FIXED**: 3 new CHECK constraints on `vacation_settlements` (`_nonneg_buckets`, `_positive_counters`, `_disposition_state` — DEFER⇒PENDING_REVIEW, FORFEIT⇒¬PENDING_REVIEW) + 8 negative/positive D-tests (`SettlementSchemaConstraintTests`); db-schema regenerated (59 tables, check_docs green).
- **WARNING 3** — a zero-transfer settlement skips the derived §21 carryover write, leaving any existing next-year `carryover_in`. → **PRINCIPLED ACCEPTANCE.** This directly conflicts with the Step-5a TASK-6804 W1 decision (skip-when-zero, to avoid CLOBBERING the future §22 producer and mass-materializing next-year rows). In slice 1a §21 is the SOLE carryover producer and `carryover_in` starts at 0; settling year E writes to E+1 (a legitimate inflow, never stale), so no zero-transfer settlement can leave a stale nonzero value. The provenance concern is fully resolved when slice 4 makes carryover source-keyed. A genuine missed-facts-vs-thrash divergence between the two lenses (Step-7a Codex lacked the Step-5a rationale).
- **NOTE** — clean: atomic settlement/outbox/audit tx, advisory locking, partial-unique active row, D10 CAS / no double-§34-emit, settled-year readers, no `used` mutation, money isolation, RBAC/OrgScope shape, FAIL-001 scan.

**Internal Reviewer — APPROVED (0B/0W/3N), cycle 1** against the post-fix-forward tree. Independently verified all three cycle-1 fix-forward items (W5 strict parse `:132`; W4 the 3 CHECK constraints `:2620-2632` — confirmed NO valid D10 state wrongly rejected, incl. that a future REVERSED+FORFEIT row is admitted so slice-4 reversal isn't pre-broken; the 8 D-tests), and all ADR-033 invariants in code (money isolation, determinism, ADR-032 D2 no-`used`-mutation, atomicity, single-active, go-live gate, D10 CAS/no-double-emit, §21 guards, FAIL-001/RBAC). NOTES: a pre-existing `AuditProjectionParityTests` doc/assert drift (unrelated to S68); B1/B2/W3 correctly recorded as owner-ratified acceptances/deferrals; FORFEIT already resolves a since-deactivated employee via the in-tx org read.

### Cycle 2

**External Codex — verify (`SPRINT-68-step7a-codex-c2-raw.log`):** findings 2 (B2 deferral), 3 (W5), 4 (W4), 5 (W3) all **RESOLVED**; no new findings; **B1 escalated to ACCEPTANCE-UNSOUND** (the admin-can-POST-non-9 reachability above) → addressed by the B1 enforcement fix.

**Internal Reviewer — B1 fix confirmation (continued agent):** **B1-RESOLVED, verdict APPROVED.** Verified the DB CHECK + endpoint guard make a non-9 VACATION `reset_month` impossible to persist on every CI-validated path (so "uniform 9" is true by construction and the divergence cannot occur), reject nothing legitimate (SPECIAL_HOLIDAY is a distinct enum value and unaffected; all 14 seeded VACATION rows are 9), and introduce no new defect. Its one advisory — the inline CHECK needed a legacy-DB ALTER backstop + the overclaiming comment tightened — was **absorbed** (the `s68-vacation-reset-month-check` guarded ALTER + corrected comment).

### Resolution

Fix-forward (working tree atop `1fd77ed`): **W5** strict `TryParseExact` parse; **W4** 3 `vacation_settlements` integrity CHECKs + 8 `SettlementSchemaConstraintTests` D-tests; **B1** VACATION-`reset_month`-9 enforcement (DB CHECK + legacy ALTER + endpoint 422 guard + 1 D-test); db-schema regen (59 tables, check_docs green). **W3** principled acceptance (Step-5a/Step-7a lens divergence; skip-when-zero correct for slice 1a — no stale source possible); **B2** deferred to slice 3 (the leaver/TERMINATION sprint) with a recorded known-limitation + next-sprint candidate. Both lenses 0-BLOCKER at the final state (Codex's sole cycle-2 blocker B1 fixed by enforcement; Reviewer APPROVED both rounds). Build 0E; full pyramid re-verified at close.

## Test Summary

Local full-pyramid GREEN 2026-06-08 (compose Postgres up on `:5432` for the ReportingLine-era classes; the post-fix-forward authoritative run):

| Suite | S67 baseline | S68 | Δ |
|-------|--------------|-----|---|
| Unit | 631 | **645** | +14 |
| Regression | 466 | **511** | +45 |
| Smoke | 5 | **5** | 0 |
| Frontend | 176 | **176** | 0 |
| **Total** | **1278** | **1337** | **+59** |

`1278 + 59 = 1337`. Regression +45 = the settlement suites (+36) + `SettlementSchemaConstraintTests` (+8, W4) + `Post_VacationConfig_NonNineResetMonth_Returns422` (+1, B1). All new settlement suites green: `VacationSettlementServiceTests` (atomic/forced-rollback, replay-determinism, half-timer flat-day parity, idempotent single-settle, carryover-raises-ceiling, no-balance-row), `VacationSettlementEndpointTests` (§21 RBAC/If-Match/cross-org/deadline/cap/post-settlement-409, D10 FORFEIT/DEFER CAS, §24 reconcile), `SettlementCloseServiceBoundaryTests` (Copenhagen 31-Dec boundary + D13 go-live gate), `SettledYearReaderTests`.

**Run record:** the final full run (`.claude/s68-regression-final.log`, 24m46s) was `509/511` with **2 FAIL-002 flakes** (`ProfileMigrationTests.TypoKey_…`, `AuditProjectionSchemaConstraintTests.Insert_GlobalScopeWithoutTargetOrg_Succeeds` — both pre-existing, non-S68 classes failing at `DockerHarness.StartAsync():441` with the verbatim "connection aborted by the software in your host machine" container-shed signature, NOT test-logic). **Isolation-cleared 8/8** (the S66 FAIL-002 adjudication precedent — never modify tests for it) ⇒ regression effectively **511/511**. The earlier run2 37 "failures" were all `ReportingLine` `:5432` connection-refused (compose Postgres was down). Unit 645, smoke 5, FE 176.

## Sprint Retrospective

**Shipped:** ADR-033 slice 1a — the Backend vacation-settlement close machinery. At each closed VACATION ferieår boundary a deterministic idempotent `SettlementCloseService` partitions the remainder into §21 transfer (the first non-zero `carryover_in` writer in project history), §24 auto-payout (recorded disposition, money-free), and §34 forfeiture-candidate → fail-closed PENDING_REVIEW (D10) with a CAS-guarded manual FORFEIT/DEFER path — writing the `vacation_settlements` state-machine identity row + the 9-event family (4 emitted, 5 define-only) + the ADR-026 audit, all in ONE atomic tx under the ADR-032 D4 advisory lock. Money stays OUT (day/hour-counts only; SLS owns kroner). The §24 export line + Payroll exactly-once emitter remain S69 = slice 1b (gated on the still-unverified §24 SLS contract). +4 tables (59 total). The D13 go-live gate keeps it launch-neutral (dormant until `Settlement:GoLiveDate`; pre-launch boundaries = manual operator fallback).

**Reviews:** Step-0b dual-lens 2 cycles (Codex 4B→0B; Reviewer 0B). Per-task Step-5a (TASK-6804 5 cycles incl. the config-resolution seam; 6806 2 cycles). Step-7a dual-lens: Reviewer APPROVED (0B/0W/3N) both rounds; **Codex cycle-1 BLOCKED (2B/3W) → cycle-2 4-of-5 RESOLVED, B1 escalated to ACCEPTANCE-UNSOUND → fixed by enforcement → both lenses 0-BLOCKER.**

**Lessons:**
- **The recurring "configless/current-vs-dated config" edge was NOT actually unreachable.** It was accepted 3× across TASK-6804/6805/6806 and again at Step-7a cycle-1 on the premise "VACATION `reset_month` is uniform-9 and immutable." Codex cycle-2 falsified it: the admin config-creation endpoint lets a GlobalAdmin POST a *new* VACATION config with any `reset_month` (immutability only blocks *changes*). **A "uniform by seed" invariant is not "uniform by construction" until something ENFORCES it.** The fix made it real (DB CHECK + endpoint guard + legacy ALTER) rather than re-accepting — and that enforcement is independently correct (the statutory 1-Sep ferieår). Carries the [[missed-facts-vs-thrash]] precedent: a sharper restatement of a prior-accepted finding is a missed fact, not thrash.
- **The two lenses diverged on W3 (zero-transfer carryover).** Step-5a said skip-when-zero (avoid clobbering the future §22 producer); Step-7a said don't-skip (D6 provenance). Both correct under their context; the resolution (accept skip-when-zero for slice 1a — §21 is the sole producer, no stale source) needed the Step-5a rationale Codex lacked. [[review-lens-complementarity]] held: Codex caught the B1 reachability the Reviewer marked only NOTE; the Reviewer caught the legacy-DB ALTER-backstop gap + the overclaiming comment Codex didn't.
- **B2 (terminated-employee 403) was a symptom of an unmodeled domain, not a bug.** Owner correctly reframed it: the leaver flow (Ferielov §26 payout-at-termination, §7 cap, SLS handoff, the departed-employee HR access path) is its own design+build sprint = ADR-033 slice 3 / TERMINATION (the launch-relevant slice). Band-aiding the 403 alone would pre-empt that design. Recorded as a known limitation (manual fallback covers it) + elevated to the named next-sprint candidate.

**Entropy discovered (recorded, not masked):** the pre-existing `AuditProjectionParityTests` doc/assert drift ("6 TBD rows" vs `Equal(5)`) — Reviewer NOTE, S45-era, unrelated to S68; docs-debt candidate. The S68 `vacation_settlements` audit-catalog rows (58 total) are wired (the `e0d1dc3` null-tolerant-mapper lesson held — no catalog-parity NRE).

**Follow-ups (ROADMAP):** ADR-033 **slice 3 = TERMINATION / leaver deep-dive** (now the headline next-sprint candidate; the only launch-relevant remaining slice, and the B2 home); slice 1b = the §24 Payroll emitter + export line (gated on the §24 SLS contract); slice 2 = SPECIAL_HOLIDAY §15 stk.2/§17 godtgørelse; slice 4 = §22 feriehindring + source-keyed carryover (resolves W3's provenance shape); TASK-6809 thin HR §21 admin form; the standing business-timezone ADR (ADR-033 D3's boundary-clock surface); the `AuditProjectionParityTests` drift.
