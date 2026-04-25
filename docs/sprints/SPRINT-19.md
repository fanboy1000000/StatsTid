# Sprint 19 — Codex BLOCKER Remediation Round 2

| Field | Value |
|-------|-------|
| **Sprint** | 19 |
| **Status** | planned |
| **Start Date** | TBD |
| **End Date** | TBD |
| **Orchestrator Approved** | no |
| **Build Verified** | n/a |
| **Test Verified** | n/a |

## Sprint Goal

Remediate the 2 BLOCKERs and 3 WARNINGs surfaced by the 2026-04-23 external Codex review of Sprint 18 ([`docs/sprints/SPRINT-18.md`](SPRINT-18.md) External Review section). Two of the findings are genuine scope-enforcement regressions — `LocalAdminOrAbove` and `EmployeeOrAbove` policies prove the caller's role but not that caller scope covers the request-body's target resource. Close these gaps, then re-run external review to confirm.

**Theme**: "role authorization ≠ resource scoping." The S18 remediation conflated role-level checks with per-org / per-employee scoping. S19 adds explicit resource-scope validation at the remaining endpoints.

## Entropy Scan Findings

_To be recorded at sprint start (Step 0a)._

| Check | Result | Detail |
|-------|--------|--------|
| KB path validation | pending | |
| Pattern compliance spot-check | pending | |
| Orphan detection | pending | |
| Documentation drift | pending | |
| Quality grade review | pending | Security grade likely remains at its post-S18 value until these fixes land |

## Architectural Constraints Verified

- [ ] P1 — Architectural integrity preserved
- [ ] P2 — Rule engine determinism maintained (no rule-engine changes expected)
- [ ] P3 — Event sourcing append-only semantics respected (TASK-1904 only changes the VALUE on `RetroactiveCorrectionRequested`, not schema)
- [ ] P4 — OK version correctness (primary focus of TASK-1903 + TASK-1904)
- [ ] P5 — Integration isolation preserved
- [ ] P6 — Payroll integration correctness (TASK-1902, TASK-1903)
- [ ] P7 — Security and access control — **primary focus** (TASK-1901, TASK-1902, TASK-1905)
- [ ] P8 — CI/CD enforcement
- [ ] P9 — Usability and UX (not in scope)

## Task Log

### TASK-1901 — Orchestrator `/execute` resource-scope validation

| Field | Value |
|-------|-------|
| **ID** | TASK-1901 |
| **Status** | planned |
| **Agent** | Security + API Integration |
| **Components** | Orchestrator (`Program.cs`, `Services/OrchestratorControlLoop.cs`, `Services/WeeklyCalculationPipeline.cs`), possibly new scope-check helper |
| **KB Refs** | ADR-007 (JWT/RBAC), ADR-008 (org hierarchy), ADR-009 (scope-embedded JWT), FAIL-001 (claim remapping) |
| **Reviewer Audit** | required (P7 — MANDATORY) |
| **External Review (Codex)** | required (high-risk: auth/security) |

**Description**: Codex BLOCKER on S18 remediation. `/api/orchestrator/execute` requires `EmployeeOrAbove`, but `OrchestratorControlLoop.ExecuteAsync` and `WeeklyCalculationPipeline.ExecuteAsync` act on the caller-supplied `parameters.employeeId` without verifying it falls in the caller's scope. Downstream Backend endpoints reject cross-employee data reads (so no data leak), but a task record is still persisted with the attacker-chosen target — an audit-log poisoning / orchestrator-layer scope bypass. Fix: extract caller identity + scopes from `HttpContext` (via `GetActorContext`), then enforce that `parameters.employeeId` either equals the caller's employeeId (Employee self-scope) or falls inside a caller scope path (LocalAdmin+). Reject with 403 before creating the task record.

**Validation Criteria**:
- [ ] Employee token with `employeeId = USR01` calling `/execute` with `parameters.employeeId = USR02` is rejected 403 (no task record persisted)
- [ ] Employee token calling for their own `employeeId` succeeds (existing behavior preserved)
- [ ] LocalAdmin/Leader whose scope includes `USR02`'s org succeeds for target `USR02`
- [ ] LocalAdmin whose scope does not include `USR02`'s org is rejected 403
- [ ] `rule-evaluation` task parameters that embed an `employeeId` (if they do) get the same check — audit all task types that consume identity parameters
- [ ] No task record persisted on rejection path
- [ ] Unit tests cover all 4 decision branches

**Files Expected to Change**:
- `src/Orchestrator/StatsTid.Orchestrator/Program.cs` — add scope check before calling `loop.ExecuteAsync`
- `src/Orchestrator/StatsTid.Orchestrator/Services/OrchestratorControlLoop.cs` — optionally take `ActorContext` or require the caller to have pre-validated
- `tests/StatsTid.Tests.Unit/Orchestrator/OrchestratorScopeEnforcementTests.cs` — new

---

### TASK-1902 — `calculate-and-export` per-org scope validation

| Field | Value |
|-------|-------|
| **ID** | TASK-1902 |
| **Status** | planned |
| **Agent** | Security + Payroll Integration |
| **Components** | Payroll (`Program.cs`), possibly `ApprovalPeriodRepository`, `OrgScopeValidator` (Infrastructure) |
| **KB Refs** | ADR-007, ADR-008, ADR-009, FAIL-001 |
| **Reviewer Audit** | required (P7 — MANDATORY) |
| **External Review (Codex)** | required (high-risk: auth/security, payroll) |

**Description**: Codex BLOCKER on S18 remediation. `/api/payroll/calculate-and-export` relies on `LocalAdminOrAbove` + the APPROVED-period guard for per-org scoping, but the approval guard matches only `(employee_id, period)` — a LocalAdmin from org A can trigger payroll export for any employee in org B whose period happens to be APPROVED. Fix: before accepting the request, resolve `request.Profile.EmployeeId`'s org path and verify it falls in the caller's scope claims (use the existing `OrgScopeValidator` / `ScopeAuthorizationHandler` machinery with an explicit resource-scope check). Alternative: revert `/calculate-and-export` to `GlobalAdminOnly` if per-org delegation is not a product requirement — Orchestrator to decide during sprint planning.

Also absorb the internal-Reviewer WARNING on auth-policy tests: add one test that resolves the real policy by name via `IAuthorizationPolicyProvider.GetPolicyAsync("GlobalAdminOnly")` / `GetPolicyAsync("LocalAdminOrAbove")` from a `ServiceCollection` that called `AddStatsTidPolicies`, asserting the requirement set — so policy-name typos at `RequireAuthorization("...")` sites would fail the test suite.

**Validation Criteria**:
- [ ] Product decision recorded (add per-org scope vs. escalate to GlobalAdminOnly) — if add per-org scope, proceed with below
- [ ] LocalAdmin from org A requesting export for employee in org B rejected 403 before any downstream call
- [ ] LocalAdmin from org A requesting export for employee in org A (APPROVED) succeeds
- [ ] GlobalAdmin bypasses the org check (existing behavior)
- [ ] New policy-wiring test fails if `"GlobalAdminOnly"` / `"LocalAdminOrAbove"` are typo'd at any `RequireAuthorization` call site
- [ ] Unit tests cover: cross-org admin rejected, same-org admin accepted, GlobalAdmin accepted

**Files Expected to Change**:
- `src/Integrations/StatsTid.Integrations.Payroll/Program.cs` — add resource-scope check at `/calculate-and-export`
- Possibly `src/Infrastructure/StatsTid.Infrastructure/Security/*` — factor resource-scope validation helper if missing
- `tests/StatsTid.Tests.Unit/Security/AuthorizationPolicyTests.cs` — policy-wiring test
- `tests/StatsTid.Tests.Unit/Payroll/CalculateAndExportScopeTests.cs` — new

---

### TASK-1903 — Mixed-version export boundary guard

| Field | Value |
|-------|-------|
| **ID** | TASK-1903 |
| **Status** | planned |
| **Agent** | Payroll Integration + Rule Engine |
| **Components** | Payroll (`Program.cs` `OkVersionBoundary`, `Services/PayrollMappingService.cs`, `Services/PeriodCalculationService.cs`) |
| **KB Refs** | ADR-003 (OK version resolved by entry date), ADR-013 (retroactive corrections, no cascade) |
| **Reviewer Audit** | required (P4 — MANDATORY) |
| **External Review (Codex)** | required (high-risk: legal/payroll correctness) |

**Description**: Codex WARNING on S18 remediation. `OkVersionBoundary.ResolveProfile` collapses a whole `CalculationResult` to the OK version of `LineItems.Min(li => li.Date)` and `MapCalculationResultAsync` then stamps every exported line with that single version. A `CalculationResult` containing line items on both sides of the OK24→OK26 transition would therefore export later lines under the wrong OK version. TASK-1801 makes cross-transition CalculationResults harder at write/calc time but does not forbid them at the export boundary.

Rolls in internal Reviewer's WARNING about `/calculate-and-export` not applying the boundary consistently with `/export` / `/export-period`.

Fix options (choose during sprint planning):
1. **Reject mixed-version results** at the export boundary (defensive fail-fast). Simple; forces callers to segment.
2. **Per-line OK-version resolution** in `MapCalculationResultAsync`. Preserves correctness even for mixed results; matches `RetroactiveCorrectionService` split semantics.
3. **Segment + emit two separate exports** at the boundary. Most invasive; best fidelity.

**Validation Criteria**:
- [ ] Chosen approach documented (rejection / per-line / segmentation)
- [ ] CalculationResult with line items spanning 2026-04-01 produces either (a) 400 with a clear mixed-version error, or (b) line items correctly mapped to OK24 / OK26 respectively
- [ ] `/calculate-and-export` uses the same boundary helper as `/export` / `/export-period`, or the asymmetry is documented with a code comment explaining why
- [ ] Regression test: mixed-version CalculationResult → expected outcome
- [ ] Existing single-version tests still pass unchanged

**Files Expected to Change**:
- `src/Integrations/StatsTid.Integrations.Payroll/Program.cs` — `OkVersionBoundary` behavior change, apply to `/calculate-and-export`
- `src/Integrations/StatsTid.Integrations.Payroll/Services/PayrollMappingService.cs` — possibly per-line resolution
- `tests/StatsTid.Tests.Regression/OkVersionMixedResultTests.cs` — new

---

### TASK-1904 — Canonicalize OkVersion in single-version retroactive audit event

| Field | Value |
|-------|-------|
| **ID** | TASK-1904 |
| **Status** | planned |
| **Agent** | Payroll Integration + Data Model |
| **Components** | Payroll (`Services/RetroactiveCorrectionService.cs`) |
| **KB Refs** | ADR-003, ADR-013, DEP-003 |
| **Reviewer Audit** | required (P3 — MANDATORY) |
| **External Review (Codex)** | not required (audit-event value normalization; no new high-risk surface) |

**Description**: Codex WARNING + internal Reviewer NOTE on S18 remediation. In `RetroactiveCorrectionService.RecalculateWithVersionSplitAsync`, `canonicalCurrentOkVersion` / `canonicalPreviousOkVersion` are only resolved when `okTransitionDate.HasValue && previousOkVersion is not null` (the split branch). In the single-version path, the emitted `RetroactiveCorrectionRequested` event carries the caller-supplied `profile.OkVersion`, but `PeriodCalculationService.CalculateAsync` now ignores that value and resolves independently from `periodStart`. Audit event can diverge from the calculation/export that actually ran.

Fix: mirror `canonicalCurrentOkVersion = OkVersionResolver.ResolveVersion(periodStart)` unconditionally so `RetroactiveCorrectionRequested.OkVersion` is always date-canonical. Trivial change (~5 LOC + test).

**Validation Criteria**:
- [ ] Single-version retroactive correction with caller-supplied `profile.OkVersion = "OK24"` but `periodStart = 2026-05-01` emits `RetroactiveCorrectionRequested.OkVersion = "OK26"` (date-canonical, not caller-supplied)
- [ ] Existing split-branch behavior unchanged
- [ ] Regression test: audit event vs. calculation OkVersion always match

**Files Expected to Change**:
- `src/Integrations/StatsTid.Integrations.Payroll/Services/RetroactiveCorrectionService.cs`
- `tests/StatsTid.Tests.Regression/OkVersionRuntimeRegressionTests.cs` — extend existing file

---

### TASK-1905 — JWT dev-fallback: honor both `ASPNETCORE_ENVIRONMENT` and `DOTNET_ENVIRONMENT`

| Field | Value |
|-------|-------|
| **ID** | TASK-1905 |
| **Status** | planned |
| **Agent** | Security |
| **Components** | Infrastructure (`Security/JwtValidationSetup.cs`) |
| **KB Refs** | ADR-007 |
| **Reviewer Audit** | required (P7 — MANDATORY) |
| **External Review (Codex)** | not required (one-line env check; low scope) |

**Description**: Codex WARNING on S18 remediation. `JwtValidationSetup.AddStatsTidJwtAuth` reads only `ASPNETCORE_ENVIRONMENT` to gate the dev signing-key fallback. ASP.NET Core's `IHostEnvironment.IsDevelopment()` honors both `ASPNETCORE_ENVIRONMENT` and `DOTNET_ENVIRONMENT`; a valid dev startup that sets only `DOTNET_ENVIRONMENT=Development` now throws `InvalidOperationException` unless `Jwt:SigningKey` is configured. Fix: either (a) take `IHostEnvironment` as a parameter and call `IsDevelopment()`, or (b) check both env vars explicitly. Preserve the production-fail-fast behavior.

**Validation Criteria**:
- [ ] `DOTNET_ENVIRONMENT=Development` with no `Jwt:SigningKey` uses the dev fallback (does not throw)
- [ ] `ASPNETCORE_ENVIRONMENT=Development` with no `Jwt:SigningKey` uses the dev fallback (existing behavior)
- [ ] Both unset (or set to anything else) with no `Jwt:SigningKey` throws `InvalidOperationException` at startup
- [ ] Configured `Jwt:SigningKey` always wins regardless of environment
- [ ] Unit tests cover all 4 branches

**Files Expected to Change**:
- `src/Infrastructure/StatsTid.Infrastructure/Security/JwtValidationSetup.cs`
- `tests/StatsTid.Tests.Unit/Security/AuthorizationPolicyTests.cs` — add `DOTNET_ENVIRONMENT` branch (mutation test; keep in existing env-var collection)

---

## Legal & Payroll Verification

| Check | Status | Notes |
|-------|--------|-------|
| Agreement rules match legal requirements | pending | No rule logic changes expected |
| Wage type mappings produce correct SLS codes | pending | TASK-1903 may change per-line OK version stamping |
| Overtime/supplement calculations are deterministic | pending | No rule engine changes expected |
| Absence effects on norm/flex/pension are correct | pending | N/A |
| Retroactive recalculation produces stable results | pending | TASK-1904: audit event value now matches calculation output |

## External Review (Step 7a)

_To be invoked at sprint end. Cycle cap: 2. S19 exists precisely because S18's Step 7a deferred 5 findings here; S19's Step 7a must verify those are closed without introducing new BLOCKERs. Per the `docs/AGENTS.md` cycle cap, 2 S19-specific Codex cycles are allowed before user escalation._

| Field | Value |
|-------|-------|
| **Invoked** | pending |
| **Sprint-start commit** | TBD (will be the S18 commit) |
| **Command** | `codex review "<prompt>"` (prompt-alone, uncommitted — preferred form) |
| **Review Cycles** | — |
| **Findings** | — |
| **Resolution** | — |

## Test Summary

_To be filled at sprint end._

## Agent Effectiveness

_To be filled at sprint end._

## Sprint Retrospective

_To be filled at sprint end._
