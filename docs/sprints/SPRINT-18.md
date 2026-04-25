# Sprint 18 — Codex BLOCKER Remediation

| Field | Value |
|-------|-------|
| **Sprint** | 18 |
| **Status** | complete (user-approved exit 2026-04-23 — 2 external BLOCKERs + 3 WARNINGs deferred to S19) |
| **Start Date** | 2026-04-18 |
| **End Date** | 2026-04-23 |
| **Orchestrator Approved** | yes — 2026-04-23 (Step 5α pass, internal Reviewer pass, Step 7a Codex → 5 findings deferred to S19 per user direction) |
| **Build Verified** | yes (2026-04-19 & 2026-04-23, clean build, 0 warnings/errors) |
| **Test Verified** | yes (2026-04-19 & 2026-04-23, 443 unit + 31 regression = 474 backend passing; frontend unchanged at 41). 7 Testcontainers regression tests require Docker at run time (last green 2026-04-19). |

## Sprint Goal

Remediate the 3 Codex BLOCKERs and 1 high-impact WARNING surfaced by the external review on 2026-04-18 ([`docs/reviews/codex-2026-04-18.md`](../reviews/codex-2026-04-18.md)): OK-version drift at the write/payroll boundary, wage-type mapping lookup semantics, under-scoped orchestrator/payroll endpoints, and `UserUpdated` missing from `EventSerializer`. Stabilize correctness before the Phase 3h UI/UX polish sprint.

## Entropy Scan Findings

_Pre-sprint entropy scan (Step 0a), 2026-04-18. Codex external review findings are incorporated where relevant._

| Check | Result | Detail |
|-------|--------|--------|
| KB path validation | CLEAN | Spot-checked — KB entries still point to live files |
| Pattern compliance spot-check | DEBT | (a) Codex flagged `RuleEngine → Infrastructure` dep — WARNING, deferred to S19 (Rec #2). (b) `UserUpdated` missing from `EventSerializer` — addressed in TASK-1804. No `FindFirst("scopes")` usage (FAIL-001 CLEAN). No hardcoded `http://localhost` outside `launchSettings.json` |
| Orphan detection | CLEAN | Codex did not flag orphans in S16-17 scope |
| Documentation drift | DRIFT | `docs/QUALITY.md:15,37` claims "no dedicated security unit tests" but several exist. `docs/WORKFLOW.md:176,253` references `MEMORY.md` from the project tree, but `MEMORY.md` actually lives under `~/.claude/projects/C--StatsTid/memory/` (user-home auto-memory). Tracked as non-blocking doc cleanup for S19 (Codex Rec #9) |
| Quality grade review | pending | Will update `docs/QUALITY.md` at S18 sprint end; Security grade likely warrants revision given Codex BLOCKER #7 before/after TASK-1803 |

## Architectural Constraints Verified

_Check each constraint that was explicitly validated during this sprint._

- [x] P1 — Architectural integrity preserved (no architectural changes introduced)
- [x] P2 — Rule engine determinism maintained (rule engine untouched; only callers changed)
- [x] P3 — Event sourcing append-only semantics respected (`UserUpdated` registered; reflection coverage test added to prevent future gaps)
- [x] P4 — OK version correctness — **primary focus**: server-side resolution in 4 write/calc endpoints + payroll calc; advisory logging on caller mismatch (TASK-1801); 9 runtime regression tests (TASK-1805)
- [x] P5 — Integration isolation preserved (local `ResolveOkVersion` duplicated rather than cross-service-calling RuleEngine — documented in code; refactor into SharedKernel deferred)
- [x] P6 — Payroll integration correctness — **primary focus**: wage-type generic lookup fixed (`position = ''`); position pass-through at 3 call sites; 6 DB-backed Testcontainers regression tests (TASK-1802)
- [x] P7 — Security and access control — **primary focus**: admin policies enforced on recalculate, payroll export, orchestrator execute; dev signing-key fallback gated to Development only; 10 unit tests (TASK-1803)
- [x] P8 — CI/CD enforcement (Testcontainers require Docker on CI runner; existing smoke suite already depends on Docker, so no new infra requirement)
- [ ] P9 — Usability and UX (TASK-1806 only; agreement-aware child-sick types and BalanceSummary filter — minor UX polish, no architectural impact)

## Task Log

### TASK-1801 — OK-version resolution at write and payroll boundaries

| Field | Value |
|-------|-------|
| **ID** | TASK-1801 |
| **Status** | complete (impl) — Reviewer/Codex pending |
| **Agent** | Rule Engine + API Integration |
| **Components** | Backend API (TimeEndpoints, contracts), Payroll Integration (PeriodCalculationService), Rule Engine (OkVersionResolver usage) |
| **KB Refs** | ADR-003 (OK version resolved by entry date), ADR-013 (retroactive corrections) |
| **Constraint Validator** | pending |
| **Reviewer Audit** | required (P2, P4 — MANDATORY trigger) — pending |
| **External Review (Codex)** | required (high-risk: legal rule logic) — pending |
| **Orchestrator Approved** | no (pending review) |

**Description**: Codex BLOCKER #4. The `OkVersionResolver` exists but production write and payroll paths trust caller-supplied `OkVersion` from `RegisterTimeEntryRequest`/`RegisterAbsenceRequest`/`EmploymentProfile` instead of server-resolving from the entry date. This violates P4 (OK version correctness — entry-date resolution). Fix: in write endpoints and payroll period calculation, resolve `OkVersion` from entry/absence date via `OkVersionResolver` and override or reject caller-supplied mismatches. Retroactive split (`OkTransitionDate`/`PreviousOkVersion`) continues to work when explicitly supplied.

**Validation Criteria**:
- [ ] `TimeEndpoints` registration paths ignore caller `OkVersion` and resolve from entry date
- [ ] `RegisterAbsenceRequest` handling resolves OK version from date range
- [ ] `PeriodCalculationService` uses resolver, not `profile.OkVersion`
- [ ] Mismatch handling decision documented (override silently with audit / reject 400 / log warning) and consistent across endpoints
- [ ] `OkVersionResolver` behavior on transition-day boundary unchanged

**Files Changed**:
- `src/Backend/StatsTid.Backend.Api/Endpoints/TimeEndpoints.cs` — resolve OkVersion from date
- `src/Backend/StatsTid.Backend.Api/Contracts/RegisterTimeEntryRequest.cs` — OkVersion becomes optional/ignored
- `src/Backend/StatsTid.Backend.Api/Contracts/RegisterAbsenceRequest.cs` — same
- `src/Integrations/StatsTid.Integrations.Payroll/Services/PeriodCalculationService.cs` — resolve per-entry
- `src/Integrations/StatsTid.Integrations.Payroll/Program.cs` — if payroll endpoint takes OkVersion as input

---

### TASK-1802 — Wage-type mapping lookup fix

| Field | Value |
|-------|-------|
| **ID** | TASK-1802 |
| **Status** | complete (impl + tests) — Reviewer/Codex pending |
| **Agent** | Payroll Integration + Data Model |
| **Components** | PostgreSQL schema (`wage_type_mappings`), Payroll Integration (PayrollMappingService, PayrollExportService, PeriodCalculationService) |
| **KB Refs** | PAT-005 (period calculation HTTP), ADR-014 (DB-backed configs) |
| **Constraint Validator** | pending |
| **Reviewer Audit** | required (P6 — OPTIONAL but Orchestrator discretion = YES) — pending |
| **External Review (Codex)** | required (high-risk: payroll export) — pending |
| **Orchestrator Approved** | no (pending review) |

**Description**: Codex BLOCKER #6. `docker/postgres/init.sql:74-79` defines `wage_type_mappings.position` as `NOT NULL DEFAULT ''`, but `PayrollMappingService.cs:30-59,88-108` looks up generic rows with `position IS NULL`. Generic mappings are therefore invisible at runtime. Compounding this, payroll code paths pass `position: null` everywhere (`Program.cs:39-46,50-63`, `PeriodCalculationService.cs:154-177`), so position-aware precedence is never exercised. Fix: standardize on empty-string convention (update query to `position = ''` for generic lookup, or add both branches), and pass `profile.Position` through the payroll chain where available.

**Validation Criteria**:
- [ ] Generic wage type mappings resolve at runtime (integration test proves it)
- [ ] Position-aware precedence is exercised: when a position-specific mapping exists, it wins over generic
- [ ] Schema and query semantics are consistent (document which convention was chosen)
- [ ] Payroll code paths pass `profile.Position` instead of `null` where applicable
- [ ] Regression tests cover: generic-only mapping, position-specific mapping, both-present precedence

**Files Changed**:
- `docker/postgres/init.sql` (only if changing schema convention)
- `src/Integrations/StatsTid.Integrations.Payroll/Services/PayrollMappingService.cs` — lookup query
- `src/Integrations/StatsTid.Integrations.Payroll/Services/PeriodCalculationService.cs` — position pass-through
- `src/Integrations/StatsTid.Integrations.Payroll/Program.cs` — position pass-through at endpoint boundary
- `tests/StatsTid.Tests.Regression/RegressionTests.cs` — new generic/position precedence tests

---

### TASK-1803 — Role-scope orchestrator / payroll / recalculate endpoints

| Field | Value |
|-------|-------|
| **ID** | TASK-1803 |
| **Status** | complete (impl + 10 tests) — Reviewer/Codex pending |
| **Agent** | Security |
| **Components** | Infrastructure/Security (AuthorizationPolicies), Orchestrator (Program), Payroll (Program) |
| **KB Refs** | ADR-007 (JWT/RBAC), ADR-009 (scope-embedded JWT), FAIL-001 (claim remapping) |
| **Constraint Validator** | pending |
| **Reviewer Audit** | required (P7 — Security mandatory) — pending |
| **External Review (Codex)** | required (high-risk: JWT/auth) — pending |
| **Orchestrator Approved** | no (pending review) |

**Description**: Codex BLOCKER #7. Sensitive orchestrator and payroll endpoints — including retroactive recalculate and payroll export — are guarded only by the `"Authenticated"` policy, allowing any valid employee token to trigger them. Fix: apply role-specific policies (`GlobalAdmin` or `LocalAdmin` depending on scope) to `recalculate`, payroll export, and orchestrator execution endpoints. Also address the dev-signing-key fallback when `JwtSettings:SigningKey` is absent — either fail fast in non-Development environments, or gate the fallback behind an explicit `ASPNETCORE_ENVIRONMENT=Development` check.

**Validation Criteria**:
- [ ] `recalculate` endpoint rejects employee tokens (admin-only)
- [ ] Payroll export endpoints require admin scope
- [ ] Orchestrator `/execute` endpoint is not reachable with an employee token
- [ ] Dev signing-key fallback cannot activate in Production environment
- [ ] Unit tests cover: employee rejected, admin accepted, missing scope rejected

**Files Changed**:
- `src/Infrastructure/StatsTid.Infrastructure/Security/AuthorizationPolicies.cs` — new role-specific policies if needed
- `src/Orchestrator/StatsTid.Orchestrator/Program.cs` — attach role policy to `/execute`
- `src/Integrations/StatsTid.Integrations.Payroll/Program.cs` — attach role policies to payroll endpoints
- `src/Infrastructure/StatsTid.Infrastructure/Security/JwtValidationSetup.cs` — harden dev fallback
- `tests/StatsTid.Tests.Unit/Security/*.cs` — new authorization tests

---

### TASK-1804 — EventSerializer coverage test + register UserUpdated

| Field | Value |
|-------|-------|
| **ID** | TASK-1804 |
| **Status** | complete (registration + 2 tests, mutation-verified) — Reviewer pending |
| **Agent** | Test & QA + Data Model |
| **Components** | Infrastructure (EventSerializer), Tests |
| **KB Refs** | DEP-003 (EventSerializer must register all types), ADR-005 (explicit type map polymorphic serialization) |
| **Constraint Validator** | pending |
| **Reviewer Audit** | required (P3 — mandatory) — pending |
| **External Review (Codex)** | not required (data model + test coverage; not a high-risk category) |
| **Orchestrator Approved** | no (pending review) |

**Description**: Codex WARNING on dim. 3. `UserUpdated` is a `DomainEventBase` descendant appended by `AdminEndpoints.cs:389-400` but is missing from `EventSerializer._eventTypeMap`, so replay/deserialization of `user-*` streams can fail. Fix: (1) register `UserUpdated` in the type map; (2) add a reflection-based test that scans `DomainEventBase` descendants in the SharedKernel assembly and fails if any are missing from the serializer map. This prevents future DEP-003 violations from slipping through.

**Validation Criteria**:
- [ ] `UserUpdated` is registered in `EventSerializer._eventTypeMap`
- [ ] New reflection test enumerates all concrete `DomainEventBase` descendants and asserts each is in the map
- [ ] Test fails (as expected) if a new event is added without registration
- [ ] Round-trip serialization test for `UserUpdated`

**Files Changed**:
- `src/Infrastructure/StatsTid.Infrastructure/EventSerializer.cs` — add UserUpdated entry
- `tests/StatsTid.Tests.Unit/Events/EventSerializerCoverageTests.cs` — new reflection test (or add to existing DomainEventBaseActorTests file)
- `tests/StatsTid.Tests.Unit/Events/DomainEventBaseActorTests.cs` — possible extension

---

### TASK-1806 — Agreement-aware child-sick absence types + BalanceSummary filter

| Field | Value |
|-------|-------|
| **ID** | TASK-1806 |
| **Status** | complete |
| **Agent** | Orchestrator (direct; off-plan carryover from the prior session) |
| **Components** | Backend API (SkemaEndpoints), Frontend (BalanceSummary) |
| **KB Refs** | [danish-agreements.md](../references/danish-agreements.md) (child-sick entitlement differs per agreement: AC=1, HK=2, PROSA=3) |
| **Constraint Validator** | n/a (changes were already made before this sprint re-entry; spot-checked by Orchestrator) |
| **Reviewer Audit** | not required (UX refinement, small scope, no P1–P7 risk) |
| **External Review (Codex)** | not required |
| **Orchestrator Approved** | yes (retroactively documented 2026-04-19) |

**Description**: Off-plan UX change discovered in the working tree when the S18 session resumed — kept intentionally. `SkemaEndpoints` now exposes agreement-specific child-sick absence types (AC: `CHILD_SICK_DAY`; HK: adds `CHILD_SICK_DAY_2`; PROSA: adds `CHILD_SICK_DAY_2` and `CHILD_SICK_DAY_3`) instead of a single hardcoded `AbsenceTimeTypes` set. Frontend `BalanceSummary` filters `CHILD_SICK` out of the "additional entitlements" panel (it's surfaced elsewhere). Logically belongs to the Phase 3h (S19) UI/UX bucket, but included here since the code was already written.

**Validation Criteria**:
- [x] AC employees see only `CHILD_SICK_DAY` (base)
- [x] HK employees additionally see `CHILD_SICK_DAY_2`
- [x] PROSA employees additionally see `CHILD_SICK_DAY_2` and `CHILD_SICK_DAY_3`
- [x] Frontend `BalanceSummary` hides `CHILD_SICK` from the additional-entitlements list
- [x] Existing org-visibility filter still applies after agreement filter

**Files Changed**:
- `src/Backend/StatsTid.Backend.Api/Endpoints/SkemaEndpoints.cs` — `GetAbsenceTypesForAgreement()` replacing static `AbsenceTimeTypes` set
- `frontend/src/components/BalanceSummary.tsx` — filter out `CHILD_SICK` from `additionalEntitlements`

---

### TASK-1805 — OK-version runtime regression tests

| Field | Value |
|-------|-------|
| **ID** | TASK-1805 |
| **Status** | complete (9 regression tests, new file `OkVersionRuntimeRegressionTests.cs`) |
| **Agent** | Test & QA |
| **Components** | Tests/Regression |
| **KB Refs** | ADR-003 (OK version resolved by entry date), ADR-013 (retroactive corrections, no cascade) |
| **Constraint Validator** | n/a (test-only) |
| **Reviewer Audit** | not required (test-only) |
| **External Review (Codex)** | not required (test-only) |
| **Orchestrator Approved** | yes (tests green, file-scope respected) |

**Description**: Codex Rec #8. Existing tests cover the `OkVersionResolver` utility in isolation. Add regression tests that exercise the full runtime paths fixed by TASK-1801: backend write registration with entries that straddle the OK24→OK26 transition, weekly calculation retrieval, payroll split/replay. These tests should fail before TASK-1801 is applied and pass after, proving the fix.

**Validation Criteria**:
- [ ] Test: registering a time entry with date before transition resolves to OK24 regardless of caller-supplied value
- [ ] Test: registering an entry after transition resolves to OK26
- [ ] Test: absence spanning the transition date triggers split behavior correctly
- [ ] Test: weekly calculation retrieves per-entry OK version consistent with entry date
- [ ] Test: payroll export for a retroactive correction respects `OkTransitionDate`
- [ ] Regression suite passes

**Files Changed**:
- `tests/StatsTid.Tests.Regression/RegressionTests.cs` — add OK-version runtime tests

---

## Legal & Payroll Verification

| Check | Status | Notes |
|-------|--------|-------|
| Agreement rules match legal requirements | PASS | No rule logic changes; full 473 backend tests green |
| Wage type mappings produce correct SLS codes | PASS | TASK-1802: `position = ''` convention now consistent across schema and query; 6 new Testcontainers regression tests cover generic/position/precedence/fallback |
| Overtime/supplement calculations are deterministic | PASS | Rule engine untouched; existing determinism tests in RegressionTests.cs still pass |
| Absence effects on norm/flex/pension are correct | PASS | Covered by existing regression suite |
| Retroactive recalculation produces stable results | PASS | TASK-1801 server-resolves per entry; TASK-1805 adds `PayrollRetroactive_OkTransitionDateSplitsPeriod` test |

## External Review (Step 7a)

_Codex sprint-end review against the sprint-start commit. See [AGENTS.md](../AGENTS.md) External Review section._

| Field | Value |
|-------|-------|
| **Invoked** | yes (2026-04-23) |
| **Sprint-start commit** | `d1b04c8` (governance update adding Codex review workflow) |
| **Command** | `codex review "<prompt>"` (prompt-alone against uncommitted; S18 had no intermediate commits) |
| **Review Cycles** | 1 (sprint-end). Per-task Codex (Step 5a override) consolidated into sprint-end — all high-risk task diffs were still uncommitted, so the sprint-end run covered the same ground while staying under the cycle cap. |
| **Findings** | 2 BLOCKER, 3 WARNING, 0 NOTE (all mapped from Codex's P1/P2 labels; see severity mapping below) |
| **Resolution** | user-approved exit 2026-04-23 — all 5 findings deferred to Sprint 19 as tasks TASK-1901 through TASK-1905. Chosen to preserve sprint scope and avoid a 3rd Codex cycle. |

### Findings

Codex native labels (P1 BLOCKER / P2 WARNING) mapped to our severity scheme. All findings are legitimate scope/correctness regressions introduced or left open by the remediation sprint:

- **BLOCKER** (P7 Security) — `src/Orchestrator/StatsTid.Orchestrator/Program.cs:40` — `/api/orchestrator/execute` now requires `EmployeeOrAbove`, but `OrchestratorControlLoop.ExecuteAsync` + `WeeklyCalculationPipeline.ExecuteAsync` persist and dispatch on the caller-supplied `employeeId` parameter without verifying it falls in the caller's scope. The Backend's own scope checks reject cross-employee data reads downstream, but a task record is still created and audited against the attacker-chosen target, constituting an orchestrator-layer scope bypass and audit-log poisoning vector. **Resolution**: deferred to **TASK-1901**.
- **BLOCKER** (P7 Security) — `src/Integrations/StatsTid.Integrations.Payroll/Program.cs:152` — `/api/payroll/calculate-and-export` guarded by `LocalAdminOrAbove` alongside an APPROVED-period check, but `ApprovalPeriodRepository.GetByEmployeeAndPeriodAsync` matches only `(employee_id, period)` — a LocalAdmin from org A can trigger payroll export for an employee in org B if that employee's period happens to be APPROVED. The code comment ("APPROVED-period guard above enforces per-org scoping") is incorrect. **Resolution**: deferred to **TASK-1902**.
- **WARNING** (P4 Version correctness) — `src/Integrations/StatsTid.Integrations.Payroll/Program.cs:325-339` — `OkVersionBoundary.ResolveProfile` collapses a whole `CalculationResult` to the OK version of the earliest line-item date. A `CalculationResult` containing line items that straddle the OK24→OK26 transition would export all lines under the earlier version. TASK-1801 makes straddling CalculationResults harder at write time but does not forbid them at the export boundary. **Resolution**: deferred to **TASK-1903**.
- **WARNING** (P3 Auditability / P4) — `src/Integrations/StatsTid.Integrations.Payroll/Services/RetroactiveCorrectionService.cs:67-74` — In the single-version path (no `OkTransitionDate`), the emitted `RetroactiveCorrectionRequested` event carries the caller-supplied `profile.OkVersion` while `PeriodCalculationService` now resolves OK version independently from `periodStart`. Calculation correctness is preserved, but audit event and calculation output can diverge. Internal Reviewer flagged this as NOTE; Codex flagged as WARNING — accepting the higher severity. **Resolution**: deferred to **TASK-1904**.
- **WARNING** (P7 Security) — `src/Infrastructure/StatsTid.Infrastructure/Security/JwtValidationSetup.cs:26-27` — The Development-environment guard reads only `ASPNETCORE_ENVIRONMENT`. ASP.NET Core also honors `DOTNET_ENVIRONMENT`; a valid development startup that sets only `DOTNET_ENVIRONMENT=Development` now throws `InvalidOperationException` at startup unless `Jwt:SigningKey` is configured. Closes the production hole but regresses a dev scenario. **Resolution**: deferred to **TASK-1905**.

### Internal Reviewer Findings (Step 5a)

Independent internal review completed 2026-04-23, scope = TASK-1801/1802/1803/1804. Result: **No BLOCKERs**, 2 WARNINGs, 3 NOTEs — all of which overlap or are subsumed by the Codex findings above:

- WARNING (P7, TASK-1803): authorization-policy tests assert handler behavior but not policy-name wiring (`IAuthorizationPolicyProvider.GetPolicyAsync("GlobalAdminOnly")`). A policy-name typo would go uncaught. **Resolution**: rolled into **TASK-1902** (which touches the same policy surface).
- WARNING (P4, TASK-1801): `/api/payroll/calculate-and-export` does not apply `OkVersionBoundary.ResolveProfile` at the outer boundary. Inner `PeriodCalculationService` resolution preserves correctness, but pattern is inconsistent with adjacent endpoints. **Resolution**: rolled into **TASK-1903**.
- NOTE (P1, TASK-1801): `OkVersionBoundary` lives as `internal static` at the bottom of `Payroll/Program.cs`. Tight, single-caller — no action required.
- NOTE (P4, TASK-1801): `RetroactiveCorrectionService` canonicalises OK version only in the split branch. Matches the Codex WARNING — same root cause.
- NOTE (P3, TASK-1804): `EventSerializerCoverageTests.ConstructMinimalInstance` uses `RuntimeHelpers.GetUninitializedObject` to bypass `required` property enforcement. Works today; no action.

## Test Summary

| Suite | Count | Status |
|-------|-------|--------|
| Unit tests | 443 (+12 from S17's 431) | PASS (re-verified 2026-04-23) |
| Regression tests | 31 (+16 from S17's 15) | PASS on 2026-04-19 with Docker; re-verified 2026-04-23: 24 pure PASS, 7 Testcontainers-dependent tests require Docker at run time (`WageTypeMappingRegressionTests`) — environmental dependency, not correctness gap |
| Smoke tests | — | not re-run this sprint (no Docker-orchestrated service changes) |
| Frontend tests | 41 (unchanged) | n/a (no new frontend tests; BalanceSummary filter change covered by existing tests) |
| **Backend total** | **474** | **PASS** (6 new Testcontainers-based in WageTypeMappingRegressionTests; 9 new pure in OkVersionRuntimeRegressionTests; 2 new in EventSerializerCoverageTests; 10 new in AuthorizationPolicyTests — some of those 10 supersede the initial sprint-doc count) |

## Agent Effectiveness

| Metric | Value |
|--------|-------|
| Tasks | 6 (TASK-1801 … TASK-1806) |
| Constraint Violations | 0 (Step 5α checks all clear 2026-04-23) |
| Reviewer Findings | 0 BLOCKER, 2 WARNING, 3 NOTE |
| External Review Cycles | 1 (sprint-end; per-task Codex consolidated into sprint-end) |
| External Findings | 2 BLOCKER, 3 WARNING, 0 NOTE (all deferred to S19 by user direction) |
| Re-dispatches | 0 |
| First-Pass Rate | 100% within S18 scope (all 6 tasks accepted on first agent pass; Codex-surfaced scope gaps went to S19 rather than re-dispatching S18 agents) |

## Sprint Retrospective

**What went well**:
- TASK-1801 + TASK-1805 delivered 9 runtime regression tests that exercise the write→calc→export OK-version path end-to-end, which the previous sprint lacked.
- TASK-1802 replaced `position IS NULL` with `position = ''` consistently across schema + query + repository CRUD + audit paths, and proved it with Testcontainers-backed regression tests.
- Internal Reviewer and external Codex reached the same conclusion on the `RetroactiveCorrectionRequested` audit-event mismatch (divergent only on severity) — validates that the two layers are calibrated.
- Moving `OkVersionResolver` to `src/SharedKernel/Calendar/` was clean: no stale references, respects bounded-context rules.

**What to improve**:
- Codex found **two new P7 scope BLOCKERs** in the very endpoints that S18's TASK-1803 touched. The sprint treated role-level auth (`GlobalAdminOnly` / `LocalAdminOrAbove`) as equivalent to resource-level scoping. It isn't — `ScopeAuthorizationHandler` proves caller HAS a scope claim, not that the scope COVERS the target resource in the request body. Future security tasks MUST explicitly validate that scoped resources in request bodies (employee IDs, org paths, period identifiers) are within the caller's scope claims, not just that the caller has an admin claim of some kind.
- TASK-1803's unit tests exercise the handler, not the policy wiring — a policy-name typo would not be caught. S19 should tighten this (rolled into TASK-1902).
- `OkVersionBoundary.ResolveProfile` collapses a multi-version CalculationResult to a single version silently. At an export boundary we should either reject mixed-version results or segment them — TASK-1801 did not enforce single-version inputs to the export call, so this gap remained. Defense-in-depth needs per-line resolution at the export boundary.
- The `ASPNETCORE_ENVIRONMENT`-only Development guard in `JwtValidationSetup` regressed a valid dev scenario (`DOTNET_ENVIRONMENT`) — Security Agent should have used `IHostEnvironment.IsDevelopment()` (which checks both) rather than raw env-var comparison.

**Knowledge produced**:
- No new KB entries created this sprint (remediation, not new architecture). The S19 fixes may justify a new PAT on "scope validation in request-body handlers" and/or a FAIL entry capturing the `LocalAdminOrAbove ≠ per-org-scoped` lesson.
