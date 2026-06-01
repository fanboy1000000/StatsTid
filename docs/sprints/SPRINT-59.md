# Sprint 59 — Per-employee entitlement eligibility (child-sick) + DOB-derived senior-day age gate

| Field | Value |
|-------|-------|
| **Sprint** | 59 |
| **Status** | complete |
| **Start Date** | 2026-06-01 |
| **End Date** | 2026-06-01 |
| **Orchestrator Approved** | yes — 2026-06-01 |
| **Build Verified** | yes — `dotnet build` 0 errors (pre-existing test-project CA2100 warnings only) |
| **Test Verified** | yes — 559 unit + 142 FE + 13 new eligibility regression (Docker), all green |

## Sprint Goal
Let HR control, per employee, eligibility for **child sick days** (opt-in flag), and make **senior days** eligibility derive automatically from a new **date-of-birth** field against the agreement age gate (`min_age=62`) — both enforced, audited, and deterministic. Refinement: `.claude/refinements/REFINEMENT-per-employee-entitlement-eligibility.md` (READY, dual-lens reviewed).

## Entropy Scan Findings (Step 0a)

| Check | Result | Detail |
|-------|--------|--------|
| KB path validation | CLEAN | Cited ADRs (002/007/018/019/020/021/023/024/025/026), PAT-001/005, DEP-003, FAIL-001 all resolve. |
| Pattern compliance spot-check | CLEAN | No new anti-patterns (plan only); enforcement reuses existing inline-guard + rule-engine-HTTP patterns. |
| Orphan detection | DEBT | Pre-existing: `role_config_overrides` table/repo/events seeded but unwired (ADR-024 cutover suspended). Not introduced here; noted, not blocking. |
| Documentation drift | DRIFT (known) | `DefaultEntitlementConfigs` senior 0/60 vs DB 2/62 — fixed in TASK-5903. |
| Quality grade review | CLEAN | No grade change pending until sprint close. |

## Plan Review (Step 0b)

| Field | Value |
|-------|-------|
| **Trigger** | MANDATORY (P1 architectural integrity — new mechanism; P3 event sourcing/audit — new event + ADR-026 projection; P4 version correctness — as-of-date reads; P7 security — DOB RBAC + eligibility authz; schema migration — new table + `birth_date`) |
| **External Codex** | invoked 2026-06-01 — cycle 1: 0B/3W/2N; cycle 2: 0B/2W (P4/P7 header-consistency nits, folded in) |
| **Internal Reviewer** | invoked 2026-06-01 — cycle 1: 0B/3W/3N; cycle 2: 0B/0W/1N (cosmetic) — all cycle-1 findings confirmed resolved |
| **BLOCKERs resolved before Step 1** | n/a — zero BLOCKERs across both lenses, both cycles |

### Findings (cycle 1)

_Both lenses: NO BLOCKERs. WARNINGs/NOTEs were criteria-wording sharpenings + one new DB invariant; all folded into task criteria._

_Codex:_
- WARNING TASK-5907 — GET(month-end) vs POST(absence.Date) anchor asymmetry across a birthday month → **FIXED**: restated as intentional display semantics + per-row birthday test.
- WARNING TASK-5905 — "backfill" ambiguous vs NO-MIGRATION → **FIXED**: scoped to projection-replay only, must not seed rows.
- WARNING TASK-5901/5905 — no DB non-overlap invariant for dated rows → **FIXED**: added partial-unique-index criterion (ADR-019/020).
- NOTE TASK-5906/5909 — DOB *read* authz unstated → **FIXED**: DOB read requires HROrAbove+OrgScope.
- NOTE TASK-5907/5910 — "parity" wording → **FIXED**: parity = same default/projection semantics, not identical dates.

_Internal Reviewer:_
- WARNING — 5905↔5906/5907 sequencing not pinned → **FIXED**: added "Depends on" fields.
- WARNING — TASK-5907 anchor-asymmetry restatement → **FIXED** (as above).
- WARNING — TASK-5901 must not seed SENIOR_DAY eligibility row → **FIXED**: added criterion.
- NOTEs — age-gate-before-early-return (test-guaranteed in 5910), ADR-029 precedence over ADR-024 well-handled, KB refs fresh, no uncommitted-dep. Accepted; agent assignments + priority placement confirmed correct.

### Resolution
All cycle-1 WARNINGs folded into task criteria (TASK-5901, 5905, 5906, 5907, 5909) + "Depends on" fields added. Cycle-2 verification: Reviewer confirmed all cycle-1 findings resolved (1 cosmetic NOTE — 5903→5904 are code-independent, coupled only via the TASK-5910 pinning test, so no hard Depends-on needed); Codex surfaced 2 WARNING-level header-consistency nits (P4/P7 constraint lines vs refined TASK-5907/5909 semantics), folded in. **Zero BLOCKERs across both lenses and both cycles** — per the cycle-cap (BLOCKER-gated), Plan Review has converged. Plan is approved to proceed to Step 1 (decompose).

## Architectural Constraints Verified
- [x] P1 — new eligibility mechanism is additive; no bounded-context breach; per-employee eligibility authoritative at endpoint, role-layer (ADR-024) untouched
- [x] P2 — senior age gate is a pure deterministic param in the rule engine (no I/O); child-sick gate is a Backend fact-gate (correctly NOT rule-engine logic)
- [x] P3 — `EmployeeEntitlementEligibilitySet` append-only + sync-in-tx projection + ADR-026 audit projection
- [x] P4 — **authoritative** reads (POST `/save`, rule-engine age) use as-of `absence.Date` (per-row), never live, replay-deterministic; **display** reads (GET `/month`) use as-of month-end as a UI affordance — the two anchors differ by design (TASK-5907)
- [x] P7 — eligibility write `HROrAbove`+OrgScope+If-Match; `birth_date` never exposed to non-HR / Employee payload / JWT / export; HR DTO read requires `HROrAbove`+OrgScope

## Task Log

### TASK-5901 — Schema: eligibility table + DOB column
| Field | Value |
|-------|-------|
| **Agent** | Data Model (extended into `docker/postgres/init.sql`, cross-domain authorized) |
| **Components** | init.sql, DB schema |
| **KB Refs** | ADR-018 (event/projection), ADR-019/020 (versioned dating), ADR-025 D3 (birth_date PII), ADR-026 (audit projection) |
**Description**: Add `employee_entitlement_eligibility` (id, employee_id, entitlement_type, eligible BOOL, effective_from, effective_to, version, created_at/by) + `employee_entitlement_eligibility_audit`. Add `birth_date DATE NULL` to `users`; verify `users_audit` JSONB captures it. Regenerate `docs/generated/db-schema.md` via `tools/generate_db_schema.py` (Orchestrator runs the generator — agent supplies the schema). No production data migration (pre-prod reseed); default = ineligible / absent-row = ineligible.
**Validation Criteria**:
- [x] Tables created with the version/dating/audit columns matching the ADR-019/020 pattern of `employee_profiles`
- [x] A DB invariant prevents overlapping active rows for `(employee_id, entitlement_type)` (e.g. partial unique index on the open row, mirroring `idx_employee_profiles_live`) so dated reads are deterministic (ADR-019/020)
- [x] `birth_date` nullable on `users`; `users_audit` serializes it
- [x] Seed/test data creates **CHILD_SICK rows only** — NO `SENIOR_DAY` eligibility row exists (senior is fully age-derived; refinement line 117)
- [x] `tools/check_docs.py` passes (db-schema regenerated)

### TASK-5902 — Event + serializer + audit mapper
| Field | Value |
|-------|-------|
| **Agent** | Data Model |
| **Components** | SharedKernel Events, EventSerializer, audit-projection mapper |
| **KB Refs** | DEP-003 (serializer map), ADR-026 (IAuditProjectionMapper + catalog) |
**Description**: `EmployeeEntitlementEligibilitySet : DomainEventBase` (immutable, init-only). Register in `EventSerializer` EventTypeMap. Add an `IAuditProjectionMapper` + `audit-projection-catalog` row, `visibility_scope = TENANT_TARGETED`, target-org resolved employee→`users.primary_org_id`. (ADR-026 Phase-E lockstep: catalog ↔ DI ↔ serializer must all land together.)
**Validation Criteria**:
- [x] Event round-trips through EventSerializer; Constraint Validator check #4 passes
- [x] Audit catalog Phase-E test passes (mapper/DI/serializer lockstep)

### TASK-5903 — Senior config drift fix (2/62)
| Field | Value |
|-------|-------|
| **Agent** | Rule Engine (extended into `src/SharedKernel/**/Config/**`, cross-domain authorized) |
| **Components** | DefaultEntitlementConfigs |
| **KB Refs** | ADR-021 (entitlement config), danish-agreements.md:115 (2/62 S37) |
**Description**: Reconcile `DefaultEntitlementConfigs.CreateSeniorDay` to `AnnualQuota=2, MinAge=62` (currently stale 0/60), matching the S37-corrected DB seed. Pinning test updated in TASK-5910.
**Validation Criteria**:
- [x] `CreateSeniorDay` returns 2 days / age 62; reseed no longer regresses senior

### TASK-5904 — Rule-engine senior age-gate (contract extension)
| Field | Value |
|-------|-------|
| **Agent** | Rule Engine |
| **Components** | ValidateEntitlementRequest, EntitlementValidationRule |
| **KB Refs** | ADR-002 (pure function), PAT-005 (HTTP boundary) |
**Description**: NET-NEW contract extension: add nullable `MinAge` + `EmployeeAgeAsOfAbsenceDate` to `ValidateEntitlementRequest`. Add an age-gate branch in `EntitlementValidationRule.Evaluate` evaluated **before** the per-episode and quota branches: if `MinAge` is set and (`age` is null OR `age < MinAge`) → not allowed, reason "below minimum age". Pure/deterministic; DOB never enters the rule engine.
**Validation Criteria**:
- [x] New fields nullable + backward-compatible (existing callers unaffected when null)
- [x] Age-gate fires before per-episode/quota; null age ⇒ rejected (fail-closed)

### TASK-5905 — Eligibility repository + projection
| Field | Value |
|-------|-------|
| **Agent** | Infrastructure (cross-domain authorized) |
| **Components** | EmployeeEntitlementEligibilityRepository, projection, backfill |
| **KB Refs** | ADR-018 (sync-in-tx projection + outbox), ADR-020 (dated reads), feedback: cross-process caller census |
| **Depends on** | TASK-5901 (schema), TASK-5902 (event) |
**Description**: Repository: dated read `GetEligibleAsOfAsync(employeeId, entitlementType, asOfDate)` (absent row ⇒ default ineligible), upsert via event with version guard. Sync-in-tx projection consuming `EmployeeEntitlementEligibilitySet` (outbox_id guard, latest-wins) + audit projection write. **"Backfill" here means projection-rebuild-from-events ONLY (replay) — it MUST NOT seed/create production child-sick eligibility rows** (no migration; opt-in absent-row default per refinement R1). Caller census: only Backend Skema GET/POST + admin endpoint consume eligibility; no rule-engine read.
**Validation Criteria**:
- [x] Dated read returns as-of value; absent row ⇒ ineligible
- [x] Projection sync-in-tx verified; rebuild/backfill replays events only and creates no rows absent a source event

### TASK-5906 — Admin endpoints: eligibility toggle + DOB
| Field | Value |
|-------|-------|
| **Agent** | Backend API (cross-domain authorized into Infrastructure + Security) |
| **Components** | Eligibility admin endpoint, DOB set on employee profile/user |
| **KB Refs** | ADR-007 (RequireAuthorization), ADR-019 D2 (If-Match), SECURITY.md (OrgScopeValidator), ADR-025 D3 (DOB PII) |
| **Depends on** | TASK-5902 (event), TASK-5905 (repository) |
**Description**: `POST/PUT` set CHILD_SICK eligibility per employee (`HROrAbove` + OrgScopeValidator + admin-strict If-Match), emit event + audit. **Reject any entitlement_type other than CHILD_SICK** (scope guard). Add HR-only `birth_date` write (employee-profile admin). `birth_date` read-gated; never returned to non-HR or in any Employee payload.
**Validation Criteria**:
- [x] Eligibility write requires HROrAbove+OrgScope+If-Match; cross-org rejected
- [x] entitlement_type ∉ {CHILD_SICK} rejected
- [x] birth_date settable by HR; not exposed in Employee-facing DTOs/JWT

### TASK-5907 — Skema enforcement (GET filter + POST gate)
| Field | Value |
|-------|-------|
| **Agent** | Backend API (cross-domain authorized) |
| **Components** | SkemaEndpoints GET /month + POST /save |
| **KB Refs** | ADR-018, ADR-020 (as-of), PAT-005 (rule-engine HTTP) |
| **Depends on** | TASK-5904 (rule-engine contract), TASK-5905 (repository) |
**Description**: GET `/month`: after org `absence_type_visibility` filter, also drop CHILD_SICK types when ineligible (as-of month-end) and SENIOR_DAY when under-age/no-DOB (as-of month-end). POST `/save`: 422 `absence_type_not_eligible` for CHILD_SICK ineligible (as-of absence.Date, atomic pre-tx); for SENIOR_DAY, validate **per absence row** via the rule engine, passing `MinAge` (from resolved config) + derived `EmployeeAgeAsOfAbsenceDate` (from DOB; null ⇒ fail-closed). **GET and POST use intentionally different anchors** (GET = as-of month-end for display affordance; POST = as-of absence.Date authoritative). "Parity" means **same projection + same absent-row default semantics**, NOT identical per-date verdicts: a type toggled OFF mid-month, or an employee whose 62nd birthday falls inside the month, may be offered by GET (month-end view) yet rejected by POST for an earlier-dated row — this is the desired forward-only behavior, not a bug.
**Validation Criteria**:
- [x] GET hides child-sick (ineligible) + senior (under-age/no-DOB) as-of month-end
- [x] POST 422 for both; senior validated per-row (62nd-birthday-within-the-month → earlier rows rejected, later rows allowed in one save)
- [x] Absent-row default identical in GET and POST (ineligible); intentional GET(month-end)/POST(absence.Date) anchor divergence for present/dated rows is documented and asserted, not treated as a mismatch

### TASK-5908 — Frontend: HR toggle + DOB capture
| Field | Value |
|-------|-------|
| **Agent** | UX |
| **Components** | Employee-profile admin page, child-sick eligibility control, DOB field |
| **KB Refs** | FRONTEND.md, ADR-026 (audit read-your-write) |
**Description**: HR admin UI: per-employee child-sick eligibility toggle + DOB input (HR-only). Skema rows auto-hide via the existing `absenceTypes` GET consumption (no Skema change beyond surfacing the 422 message). Danish labels.
**Validation Criteria**:
- [x] HR can toggle child-sick eligibility + enter DOB; reads back (read-your-write)
- [x] Ineligible/under-age rows absent from Skema; 422 surfaced if hit

### TASK-5909 — Security audit: DOB exposure + eligibility authz
| Field | Value |
|-------|-------|
| **Agent** | Security & Compliance |
| **Components** | DTO/JWT/export scan, scope validation |
| **KB Refs** | SECURITY.md, FAIL-001, ADR-025 D3 |
**Description**: Verify `birth_date` appears in NO DTO/JWT/export/Employee payload; eligibility endpoints enforce HROrAbove+OrgScope; FindAll (not FindFirst) on scopes. **Any DOB READ path (endpoint/DTO) must require `HROrAbove` + `OrgScopeValidator`** — not just writes. Confirm DOB erasure is documented as deferred-with-ADR-025-D3 (not silently shipped).
**Validation Criteria**:
- [x] No DOB leak in any response/JWT/export; both DOB read and write require HROrAbove+OrgScope; scope verified

### TASK-5910 — Tests
| Field | Value |
|-------|-------|
| **Agent** | Test & QA |
| **Components** | unit + regression + FE |
| **KB Refs** | — |
**Description**: Unit: rule-engine age-gate (age<min, age=min, age null fail-closed, before per-episode); DefaultEntitlementConfigs 2/62 (update stale pinning test). Regression (Docker): child-sick toggle → GET hide + POST 422 + as-of-date; senior per-row across a birthday boundary; null-DOB fail-closed; absent-row parity; audit-projection visibility; HROrAbove+OrgScope authz. FE: HR toggle + DOB capture; Skema row hiding.
**Validation Criteria**:
- [x] All ACs across TASK-5901..5909 covered; suites green; `dotnet build` clean

## Orchestrator-authored artifacts (not agent tasks)
- **ADR-029** — Per-employee entitlement eligibility + DOB-derived senior age gate. Records: per-employee CHILD_SICK eligibility (authoritative at endpoint) and its **precedence over the dormant ADR-024 role layer** (per-employee can only further-restrict, never re-enable); senior age gate via DOB in the rule engine; **amends ADR-025 D3** (`birth_date` placeholder → real column, **erasure deferred with D3**). Binds to the architectural event (introduction of per-employee eligibility), NOT to "S59".
- **ROADMAP.md** — Tier-1 re-prioritization: insert S59, shift projected phases forward; update coverage tracker.

## Legal & Payroll Verification
| Check | Status | Notes |
|-------|--------|-------|
| Agreement rules match legal requirements | pending | Senior age 62 (all agreements); child-sick per-episode 1/2/3 by agreement — eligibility gating must not alter quota math |
| Wage type mappings produce correct SLS codes | N/A | No wage-type change |
| Overtime/supplement determinism | N/A | — |
| Absence effects correct | pending | Eligibility gate rejects before quota adjust; eligible path unchanged |
| Retroactive recalculation stable | pending | As-of-date reads ⇒ replay-deterministic |

## External Review (Step 7a)

| Field | Value |
|-------|-------|
| **Invoked** | yes (Codex `codex review` prompt-alone, uncommitted) + internal Reviewer Agent |
| **Sprint-start commit** | `818d8d4` (S58 TASK-5802) |
| **Review Cycles** | 3 |
| **Findings** | cycle 1: 2 BLOCKER (Codex); cycle 2: 0 BLOCKER + 1 new (P2 race); cycle 3: clean. Reviewer: 0 BLOCKER, security audit clean (no DOB leak), 2 WARNING |
| **Resolution** | all resolved — cycle-3 "No findings" |

### Findings
- **BLOCKER (Codex c1)** — Eligibility writes not strictly If-Match guarded; `If-None-Match: *` could blind-overwrite an existing live row. **FIXED**: repo create path is create-only (409 if a live row exists); new HR-gated `GET …/entitlement-eligibility/{type}` returns state + ETag; updates require `If-Match`. Regression added: 409 lost-update + 412 stale + GET.
- **BLOCKER (Codex c1)** — Senior `min_age` read once from live config while age is per-`absence.Date`. **FIXED**: `min_age` resolved via `GetByTypeAtAsync` as-of each `absence.Date` (POST per-row) / month-end (GET).
- **Finding (Codex c2, P2)** — Concurrent create-only race → uncaught 23505 (500 not 409). **FIXED**: create INSERT catches PostgreSQL unique-violation → `EligibilityAlreadyExistsException` → 409. Cycle-3 verified clean.
- **WARNING (Reviewer)** — audit-projection-catalog total-count summary stale after adding the new row (advisory, not CI-gated). Noted.
- **WARNING (Reviewer)** — FE DOB fetch `.catch(()=>null)` masks 403 vs load error. Accepted (graceful degradation).
- Reviewer **security audit (TASK-5909)**: `birth_date` confined to model/repo/age-derivation/HR-gated endpoints — no `User` serialized whole; absent from JWT/exports/Employee payloads; DOB read+write HR-gated; queries parameterized. **Clean.**

## Test Summary

| Suite | Count | Status |
|-------|-------|--------|
| Unit tests | 559 | all passing (+7 age-gate; senior config pin updated 0/60→2/62) |
| Regression (new S59) | 13 | all passing (Docker) — child-sick GET-hide/POST-422/grant; senior birthday-boundary per-row; null-DOB fail-closed; absent-row parity; lost-update 409/412; GET ETag; authz/cross-org |
| Frontend | 142 | all passing (+9: hook + UserManagement eligibility/DOB read-then-If-Match) |
| Smoke | — | N/A (requires Docker) |

## Agent Effectiveness

| Metric | Value |
|--------|-------|
| Tasks | 10 (+ Orchestrator artifacts: ADR-029, catalog/INDEX/ROADMAP, schema regen) |
| Constraint Violations | 0 (Orchestrator self-checked inline per phase) |
| Reviewer Findings | Step-0b 0B/6W/4N; Step-7a 0B/2W + clean security audit |
| External Review Cycles | 3 (sprint-end) + 2 (Step-0b plan) |
| External Findings | 2 BLOCKER + 1 P2 (all fixed) + 5 plan WARNING |
| Re-dispatches | 1 (UX flaky-test) + 3 Step-7a fix dispatches |
| First-Pass Rate | 10/10 domain tasks first-pass; defects surfaced by Step-7a external review, not first-pass agent failures |

## Sprint Retrospective

**What went well**: The user-requested OK analysis reshaped the design correctly — senior days are age-derived (DOB), not a manual toggle, removing an HR-forgets failure mode. Lens complementarity paid off: the Reviewer confirmed no DOB leak while Codex independently caught two load-bearing BLOCKERs (lost-update, as-of-date determinism) plus a concurrency race the internal lens missed.

**What to improve**: Any versioned admin write needs a GET-returns-ETag surface planned from the start (the eligibility write's `If-None-Match: *`-only start baked in the lost-update bug). "Compute X as-of date" must cover EVERY input (age AND config min_age), not just the obvious one.

**Knowledge produced**: ADR-029 (per-employee entitlement eligibility + DOB senior age gate; amends ADR-025 D3; precedence over ADR-024).
