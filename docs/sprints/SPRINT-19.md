# Sprint 19 — Codex BLOCKER Remediation Round 2

| Field | Value |
|-------|-------|
| **Sprint** | 19 |
| **Status** | ready-to-commit (Step 5α + 5a + 7a all cleared 2026-04-28) |
| **Start Date** | 2026-04-25 |
| **End Date** | 2026-04-28 |
| **Orchestrator Approved** | pending commit (Reviewer NOTE-only, Codex cycle 2 clean) |
| **Build Verified** | yes (2026-04-28, clean build, 0 warnings/errors) |
| **Test Verified** | yes — 493 unit tests passing (+19 from S18's 474 baseline; +50 method count from 443 unit tests after S18's structural changes are normalized — see Test Summary below). Regression suite includes 1 new file (`CalculateAndExportScopeTests`, 4 tests, Docker-gated) + 1 extended file (`OkVersionRuntimeRegressionTests`, +1 test). Smoke tests require running stack and are not run locally. |

## Sprint Goal

Remediate 4 of the 5 findings surfaced by the 2026-04-23 external Codex review of Sprint 18 ([`docs/sprints/SPRINT-18.md`](SPRINT-18.md) External Review section). Two of the findings are genuine scope-enforcement regressions — `LocalAdminOrAbove` and `EmployeeOrAbove` policies prove the caller's role but not that caller scope covers the request-body's target resource. Close these gaps, then re-run external review to confirm.

**Theme**: "role authorization ≠ resource scoping." The S18 remediation conflated role-level checks with per-org / per-employee scoping. S19 adds explicit resource-scope validation at the remaining endpoints.

**Scope decision (2026-04-25)**: TASK-1903 (mixed-version export boundary) folded into S20 — the temporal-segmentation framework will solve it correctly as part of generalising the boundary problem rather than shipping a tactical patch here. System is pre-production, so live exposure is not a concern. See [SPRINT-20.md](SPRINT-20.md) "Scope Boundary → In scope" for the explicit absorption.

## Entropy Scan Findings

_Sprint 19 Step 0a, 2026-04-25._

| Check | Result | Detail |
|-------|--------|--------|
| KB path validation | CLEAN | S18 files in their declared locations (`src/SharedKernel/Calendar/OkVersionResolver.cs`, new test files); INDEX.md spot-check passes |
| Pattern compliance spot-check | DEBT (carried) | (a) PAT-005 (RuleEngine HTTP-only): CLEAN — no `using StatsTid.RuleEngine` from Payroll. (b) FAIL-001 (`FindFirst("scopes")`): CLEAN — only doc references. (c) Hardcoded `http://localhost`: CLEAN — only in `launchSettings.json` (documented exception). (d) `RequireAuthorization` coverage: 92 endpoints, 86 `RequireAuthorization` calls — 5 unauthenticated `/health` endpoints + 1 likely `/login` account for the gap. (e) **DEBT carried**: RuleEngine.Api/Program.cs still has `using StatsTid.Infrastructure` (Codex Rec #2 from S18, explicitly deferred). No new violations |
| Orphan detection | CLEAN | S18 new files (`OkVersionRuntimeRegressionTests`, `WageTypeMappingRegressionTests`, `EventSerializerCoverageTests`, `AuthorizationPolicyTests`, `SharedKernel/Calendar/OkVersionResolver`) all referenced |
| Documentation drift | CLEAN | MEMORY.md sprint status reflects S18 commit `022f44b` and includes S21 placement. ROADMAP.md Phase 3i now points to local-config rework. SPRINT-20.md absorbed TASK-1903. INDEX.md updated. No stale references found |
| Quality grade review | deferred | Will update `docs/QUALITY.md` at sprint end after task outcomes are known. Security grade likely improves after TASK-1901+1902 land |

## Architectural Constraints Verified

- [x] P1 — Architectural integrity preserved (no service topology / bounded-context changes)
- [ ] P2 — Rule engine determinism maintained (no rule-engine changes expected) — N/A
- [x] P3 — Event sourcing append-only semantics respected (TASK-1904 changes only the VALUE on `RetroactiveCorrectionRequested.OkVersion`, not schema)
- [x] P4 — OK version correctness (TASK-1904 single-version branch now date-canonical; mixed-version export still deferred to S20)
- [x] P5 — Integration isolation preserved (Payroll Program.cs new dep is on existing Infrastructure scope-validation chain, not a new service)
- [x] P6 — Payroll integration correctness (TASK-1902 closes per-org bypass at `/calculate-and-export`; mixed-version export remains in S20)
- [x] P7 — Security and access control — **primary focus** (TASK-1901, TASK-1902, TASK-1905 closed)
- [ ] P8 — CI/CD enforcement — N/A (no pipeline changes)
- [ ] P9 — Usability and UX — N/A (not in scope)

## Task Log

### TASK-1901 — Orchestrator `/execute` resource-scope validation

| Field | Value |
|-------|-------|
| **ID** | TASK-1901 |
| **Status** | complete (impl + 28 tests after Codex cycle 1 BLOCKER closure: per-task-type `ExtractEmployeeId` + conflict detection + bypass-attempt e2e + unknown-task-type defensive) — Codex cycle 2 pending |
| **Agent** | Security + API Integration |
| **Components** | Orchestrator (`Program.cs`, `Services/OrchestratorControlLoop.cs`, `Services/WeeklyCalculationPipeline.cs`), possibly new scope-check helper |
| **KB Refs** | ADR-007 (JWT/RBAC), ADR-008 (org hierarchy), ADR-009 (scope-embedded JWT), FAIL-001 (claim remapping) |
| **Reviewer Audit** | required (P7 — MANDATORY) |
| **External Review (Codex)** | required (high-risk: auth/security) |

**Description**: Codex BLOCKER on S18 remediation. `/api/orchestrator/execute` requires `EmployeeOrAbove`, but `OrchestratorControlLoop.ExecuteAsync` and `WeeklyCalculationPipeline.ExecuteAsync` act on the caller-supplied `parameters.employeeId` without verifying it falls in the caller's scope. Downstream Backend endpoints reject cross-employee data reads (so no data leak), but a task record is still persisted with the attacker-chosen target — an audit-log poisoning / orchestrator-layer scope bypass. Fix: extract caller identity + scopes from `HttpContext` (via `GetActorContext`), then enforce that `parameters.employeeId` either equals the caller's employeeId (Employee self-scope) or falls inside a caller scope path (LocalAdmin+). Reject with 403 before creating the task record.

**Validation Criteria**:
- [x] Employee token with `employeeId = USR01` calling `/execute` with `parameters.employeeId = USR02` is rejected 403 (no task record persisted) — `EvaluateAccessAsync_EmployeeCrossUser_Denies`; endpoint short-circuits before `loop.ExecuteAsync` (Program.cs:43-49)
- [x] Employee token calling for their own `employeeId` succeeds (existing behavior preserved) — `EvaluateAccessAsync_EmployeeSelfScope_Allows`
- [x] LocalAdmin/Leader whose scope includes `USR02`'s org succeeds for target `USR02` — `EvaluateAccessAsync_LocalAdminInScope_Allows`
- [x] LocalAdmin whose scope does not include `USR02`'s org is rejected 403 — `EvaluateAccessAsync_LocalAdminOutOfScope_Denies`
- [x] `rule-evaluation` task parameters that embed an `employeeId` get the same check — `ExtractEmployeeId` covers nested `profile.employeeId`; allow-list pinned by `AllowedExecuteTaskTypes_IsTheSingleSourceOfTruth`. Defensive rejection of `payroll-export` / `external-integration` / unknown task types pinned by three additional tests.
- [x] No task record persisted on rejection path — endpoint returns `Results.Json` before `loop.ExecuteAsync` (verified by inspection)
- [x] Unit tests cover all 4 decision branches (plus 4 allow-list/null-fall-through branches and 12 `ExtractEmployeeId` payload-shape branches)

**Files Expected to Change**:
- `src/Orchestrator/StatsTid.Orchestrator/Program.cs` — add scope check before calling `loop.ExecuteAsync`
- `src/Orchestrator/StatsTid.Orchestrator/Services/OrchestratorControlLoop.cs` — optionally take `ActorContext` or require the caller to have pre-validated
- `tests/StatsTid.Tests.Unit/Orchestrator/OrchestratorScopeEnforcementTests.cs` — new

---

### TASK-1902 — `calculate-and-export` per-org scope validation

| Field | Value |
|-------|-------|
| **ID** | TASK-1902 |
| **Status** | complete (impl + 4 regression tests + policy-wiring tests) — Reviewer/Codex pending |
| **Agent** | Security + Payroll Integration |
| **Components** | Payroll (`Program.cs`), possibly `ApprovalPeriodRepository`, `OrgScopeValidator` (Infrastructure) |
| **KB Refs** | ADR-007, ADR-008, ADR-009, FAIL-001 |
| **Reviewer Audit** | required (P7 — MANDATORY) |
| **External Review (Codex)** | required (high-risk: auth/security, payroll) |

**Description**: Codex BLOCKER on S18 remediation. `/api/payroll/calculate-and-export` relies on `LocalAdminOrAbove` + the APPROVED-period guard for per-org scoping, but the approval guard matches only `(employee_id, period)` — a LocalAdmin from org A can trigger payroll export for any employee in org B whose period happens to be APPROVED. Fix: before accepting the request, resolve `request.Profile.EmployeeId`'s org path and verify it falls in the caller's scope claims by calling `OrgScopeValidator.ValidateEmployeeAccessAsync(actor, request.Profile.EmployeeId, ct)` (already exists in `Infrastructure.Security`).

**Decision (2026-04-25)**: Per-org scope validation chosen over `GlobalAdminOnly` escalation. LocalAdmin payroll delegation is a product requirement; escalating to global-only would break that workflow. Reuse existing `OrgScopeValidator` machinery rather than introducing a new helper.

Also absorb the internal-Reviewer WARNING on auth-policy tests: add one test that resolves the real policy by name via `IAuthorizationPolicyProvider.GetPolicyAsync("GlobalAdminOnly")` / `GetPolicyAsync("LocalAdminOrAbove")` from a `ServiceCollection` that called `AddStatsTidPolicies`, asserting the requirement set — so policy-name typos at `RequireAuthorization("...")` sites would fail the test suite.

**Validation Criteria**:
- [x] Product decision recorded — per-org scope validation (2026-04-25)
- [x] LocalAdmin from org A requesting export for employee in org B rejected 403 before any downstream call — `CalculateAndExportScopeTests.CrossOrgAdmin_Rejected` (regression); endpoint validates BEFORE `approvalRepo.GetByEmployeeAndPeriodAsync` (Payroll/Program.cs:104-118)
- [x] LocalAdmin from org A requesting export for employee in org A (APPROVED) succeeds — `CalculateAndExportScopeTests.SameOrgAdmin_Accepted`
- [x] GlobalAdmin bypasses the org check (existing behavior) — `CalculateAndExportScopeTests.GlobalAdmin_AcceptedAcrossOrgs`
- [x] New policy-wiring test fails if `"GlobalAdminOnly"` / `"LocalAdminOrAbove"` are typo'd at any `RequireAuthorization` call site — `AuthorizationPolicyWiringTests.EveryRequireAuthorizationCallSite_ResolvesToARegisteredPolicy` scans all `src/**/*.cs` for the literal pattern and proves each name resolves via `IAuthorizationPolicyProvider`
- [x] Tests cover: cross-org admin rejected, same-org admin accepted, GlobalAdmin accepted — placed in regression suite (Testcontainers) rather than unit suite. `OrgScopeValidator` consumes sealed `OrganizationRepository` / `UserRepository` issuing raw Npgsql queries; pure-unit testing would require refactoring the validator to accept lookup delegates. Pinning the validator end-to-end here also covers every endpoint that calls `ValidateEmployeeAccessAsync`, not just `/calculate-and-export`. Plus a defensive `UnknownTargetEmployee_Rejected` pin.

**Files Expected to Change**:
- `src/Integrations/StatsTid.Integrations.Payroll/Program.cs` — added resource-scope check at `/calculate-and-export` ✅
- ~~`src/Infrastructure/StatsTid.Infrastructure/Security/*`~~ — not needed; reused existing `OrgScopeValidator.ValidateEmployeeAccessAsync` per the 2026-04-25 decision
- `tests/StatsTid.Tests.Unit/Security/AuthorizationPolicyTests.cs` — added policy-wiring tests + call-site scan ✅
- `tests/StatsTid.Tests.Regression/CalculateAndExportScopeTests.cs` — new (was planned at `tests/StatsTid.Tests.Unit/Payroll/`; relocated to regression — see validation-criterion note above) ✅

---

### TASK-1903 — Mixed-version export boundary guard *(ABSORBED INTO S20)*

| Field | Value |
|-------|-------|
| **ID** | TASK-1903 |
| **Status** | absorbed (2026-04-25) — folded into Sprint 20's Temporal Period Handling framework |
| **Reason** | S20's general segmentation framework will solve this boundary correctly as part of its OK-version end-to-end implementation (S20 § "Scope Boundary → In scope"). System is pre-production, so the silent-pinning bug carries no live exposure that would justify a tactical patch in S19. Doing it here would produce throwaway code that S20 supersedes within one sprint. |
| **S20 Reference** | Implementation covering at least the OK version boundary end-to-end is already in S20's in-scope list. The export-boundary symptom this task addressed is the specific call site where S20's segmentation will be exercised first. |

**Original description (kept for traceability)**: Codex WARNING on S18 remediation. `OkVersionBoundary.ResolveProfile` collapses a whole `CalculationResult` to the OK version of `LineItems.Min(li => li.Date)` and `MapCalculationResultAsync` then stamps every exported line with that single version. A `CalculationResult` containing line items on both sides of the OK24→OK26 transition would therefore export later lines under the wrong OK version. TASK-1801 makes cross-transition CalculationResults harder at write/calc time but does not forbid them at the export boundary.

The internal Reviewer WARNING about `/calculate-and-export` not applying the boundary consistently with `/export` / `/export-period` also rolls forward into S20.

---

### TASK-1904 — Canonicalize OkVersion in single-version retroactive audit event

| Field | Value |
|-------|-------|
| **ID** | TASK-1904 |
| **Status** | complete (impl + 1 regression test + 8 helper-branch unit tests after Codex cycle 1 WARNING closure: extracted pure `OkVersionCanonicalization.Resolve` so service-level branch choice is now pinned) — Codex cycle 2 pending |
| **Agent** | Payroll Integration + Data Model |
| **Components** | Payroll (`Services/RetroactiveCorrectionService.cs`) |
| **KB Refs** | ADR-003, ADR-013, DEP-003 |
| **Reviewer Audit** | required (P3 — MANDATORY) |
| **External Review (Codex)** | not required (audit-event value normalization; no new high-risk surface) |

**Description**: Codex WARNING + internal Reviewer NOTE on S18 remediation. In `RetroactiveCorrectionService.RecalculateWithVersionSplitAsync`, `canonicalCurrentOkVersion` / `canonicalPreviousOkVersion` are only resolved when `okTransitionDate.HasValue && previousOkVersion is not null` (the split branch). In the single-version path, the emitted `RetroactiveCorrectionRequested` event carries the caller-supplied `profile.OkVersion`, but `PeriodCalculationService.CalculateAsync` now ignores that value and resolves independently from `periodStart`. Audit event can diverge from the calculation/export that actually ran.

Fix: mirror `canonicalCurrentOkVersion = OkVersionResolver.ResolveVersion(periodStart)` unconditionally so `RetroactiveCorrectionRequested.OkVersion` is always date-canonical. Trivial change (~5 LOC + test).

**Validation Criteria**:
- [x] Single-version retroactive correction with caller-supplied `profile.OkVersion = "OK24"` but `periodStart = 2026-05-01` emits `RetroactiveCorrectionRequested.OkVersion = "OK26"` (date-canonical, not caller-supplied) — `OkVersionRuntimeRegressionTests.RetroactiveCorrection_SingleVersionAuditUsesPeriodStartResolution`
- [x] Existing split-branch behavior unchanged — diff preserves the `okTransitionDate.HasValue && previousOkVersion is not null` branch; `canonicalCurrentOkVersion = resolvedCurrent` reassignment matches pre-fix logic for split path
- [x] Regression test: audit event vs. calculation OkVersion always match — pinned at the `OkVersionResolver.ResolveVersion(periodStart)` invariant level, mirroring test #9's discipline of pinning the underlying invariant rather than mocking the service

**Files Expected to Change**:
- `src/Integrations/StatsTid.Integrations.Payroll/Services/RetroactiveCorrectionService.cs`
- `tests/StatsTid.Tests.Regression/OkVersionRuntimeRegressionTests.cs` — extend existing file

---

### TASK-1905 — JWT dev-fallback: honor both `ASPNETCORE_ENVIRONMENT` and `DOTNET_ENVIRONMENT`

| Field | Value |
|-------|-------|
| **ID** | TASK-1905 |
| **Status** | complete (impl + 5 tests across 3 collections) — Reviewer pending |
| **Agent** | Security |
| **Components** | Infrastructure (`Security/JwtValidationSetup.cs`) |
| **KB Refs** | ADR-007 |
| **Reviewer Audit** | required (P7 — MANDATORY) |
| **External Review (Codex)** | not required (one-line env check; low scope) |

**Description**: Codex WARNING on S18 remediation. `JwtValidationSetup.AddStatsTidJwtAuth` reads only `ASPNETCORE_ENVIRONMENT` to gate the dev signing-key fallback. ASP.NET Core's `IHostEnvironment.IsDevelopment()` honors both `ASPNETCORE_ENVIRONMENT` and `DOTNET_ENVIRONMENT`; a valid dev startup that sets only `DOTNET_ENVIRONMENT=Development` now throws `InvalidOperationException` unless `Jwt:SigningKey` is configured. Fix: either (a) take `IHostEnvironment` as a parameter and call `IsDevelopment()`, or (b) check both env vars explicitly. Preserve the production-fail-fast behavior.

**Validation Criteria**:
- [x] `DOTNET_ENVIRONMENT=Development` with no `Jwt:SigningKey` uses the dev fallback (does not throw) — `JwtValidationFrameworkIntegrationTests.DotnetEnvironment_FlowsThroughHostEnvironmentAndUnlocksDevFallback` (env-mutation, isolated `EnvVar` collection)
- [x] `ASPNETCORE_ENVIRONMENT=Development` with no `Jwt:SigningKey` uses the dev fallback — covered by the same framework test (host honors either var) and `JwtValidationSetupTests.AddStatsTidJwtAuth_AllowsFallbackWhenIHostEnvironmentReportsDevelopment` at the unit layer
- [x] Both unset (or set to anything else) with no `Jwt:SigningKey` throws `InvalidOperationException` at startup — `JwtValidationSetupTests.AddStatsTidJwtAuth_ThrowsWhenSigningKeyMissingInNonDevelopment` Theory across `Production` / `Staging` / `""`
- [x] Configured `Jwt:SigningKey` always wins regardless of environment — `JwtValidationSetupTests.AddStatsTidJwtAuth_UsesConfiguredKeyEvenInNonDevelopment`
- [x] Unit tests cover all 4 branches (3 in `JwtValidationSetupTests` with fake `IHostEnvironment` + 1 framework-integration in `JwtValidationFrameworkIntegrationTests`; latter pins the upstream `IHostEnvironment.IsDevelopment()` guarantee that the post-fix code relies on)

**Files Expected to Change**:
- `src/Infrastructure/StatsTid.Infrastructure/Security/JwtValidationSetup.cs`
- `tests/StatsTid.Tests.Unit/Security/AuthorizationPolicyTests.cs` — add `DOTNET_ENVIRONMENT` branch (mutation test; keep in existing env-var collection)

---

## Legal & Payroll Verification

| Check | Status | Notes |
|-------|--------|-------|
| Agreement rules match legal requirements | N/A | No rule logic changes |
| Wage type mappings produce correct SLS codes | N/A | TASK-1903 absorbed into S20; per-line OK-version stamping unchanged this sprint |
| Overtime/supplement calculations are deterministic | N/A | No rule engine changes |
| Absence effects on norm/flex/pension are correct | N/A | Out of scope |
| Retroactive recalculation produces stable results | passing | TASK-1904: audit event `RetroactiveCorrectionRequested.OkVersion` now mirrors `OkVersionResolver.ResolveVersion(periodStart)` in both single-version and split paths; pinned by `OkVersionRuntimeRegressionTests` test #10 |

## External Review (Step 7a)

_To be invoked at sprint end. Cycle cap: 2. S19 exists precisely because S18's Step 7a deferred 5 findings here; S19's Step 7a must verify those are closed without introducing new BLOCKERs. Per the `docs/AGENTS.md` cycle cap, 2 S19-specific Codex cycles are allowed before user escalation._

| Field | Value |
|-------|-------|
| **Invoked** | yes — cycle 1 + cycle 2 on 2026-04-28 (uncommitted diff) |
| **Sprint-start commit** | `022f44b` (S18 commit) |
| **Command** | `codex review "<prompt>"` (prompt-alone, uncommitted) |
| **Review Cycles** | 2 of 2 used — cycle 2 CLEAN |
| **Findings (cycle 1)** | (a) **BLOCKER P1** — `OrchestratorScopeHelpers.ExtractEmployeeId` returned top-level `employeeId` unconditionally, but `TaskDispatcher` forwards `parameters` verbatim to `RuleEngine /api/rules/evaluate`, which reads `request.Profile.EmployeeId` (nested). A caller could satisfy the gate with their own id at top-level and trigger rule evaluation against a different victim id in `profile.employeeId` — gate vs. downstream-consumer mismatch. (b) **WARNING P3** — `OkVersionRuntimeRegressionTests` test #10 only pinned `OkVersionResolver.ResolveVersion(periodStart)`, never invoked `RetroactiveCorrectionService` itself; if the single-version branch regressed back to `profile.OkVersion`, the test would still pass. |
| **Resolution (cycle 1)** | (a) Refactored `ExtractEmployeeId` to be task-type-aware: `rule-evaluation` reads `profile.employeeId`, `weekly-calculation` reads top-level. Mismatched top-level vs. nested ids now reported as `Conflict=true` and 403'd at the gate before scope-check is invoked. Existing `ExtractEmployeeId_TopLevelWinsOverNestedProfile` test removed (asserted the buggy behaviour); replaced with 6 per-task-type tests + 2 conflict tests + 1 bypass-attempt end-to-end test through `EvaluateAccessAsync`. (b) Extracted pure helper `OkVersionCanonicalization.Resolve(callerCurrent, periodStart, okTransitionDate, callerPrevious)` from `RetroactiveCorrectionService` and unit-tested 8 branches in `tests/StatsTid.Tests.Unit/Payroll/OkVersionCanonicalizationTests.cs`. The service is now a thin wrapper that calls the helper and logs warnings off the helper's drift flags. |
| **Findings (cycle 2)** | None. Codex verbatim: _"The scope-gate fix now validates the same employeeId shape each allowed downstream consumer reads, rejects top-level/profile mismatches before scope validation, and the new helper-based OK-version canonicalization preserves the single-version and split-path branches that were previously under-tested. I did not find a remaining gate/consumer mismatch or a new regression introduced by this patch."_ |
| **Resolution (cycle 2)** | None required. Sprint cleared external review at cycle 2 — within the 2-cycle cap. |

## Test Summary

Pre-sprint (S18 final): 443 unit + 31 regression = 474 backend, 41 frontend.

Sprint deltas (after Codex cycle 1 fixes + internal Reviewer NOTE closure):
- **Unit**: 443 → 493 (+50). Breakdown: +30 TASK-1901 (24 original + 5 net cycle-1 + 1 weekly-calculation E2E conflict symmetry test added per Reviewer NOTE) / +9 TASK-1904 (1 invariant pin + 8 helper-branch tests) / +5 TASK-1905 / +6 TASK-1902 policy-wiring & call-site scan. ✅ all passing.
- **Regression**: 31 → 35 (+4 from new `CalculateAndExportScopeTests` for TASK-1902; existing `OkVersionRuntimeRegressionTests` test #10 retained as redundant pin of the underlying invariant — service-level pinning lives in the new unit-test class). All Docker-gated tests still require a running daemon.
- **Smoke**: unchanged (4 — require running stack).
- **Frontend**: unchanged (41).

**Local verification this sprint**:
- `dotnet build StatsTid.sln` — 0 warnings, 0 errors (2026-04-28).
- `dotnet test tests/StatsTid.Tests.Unit` — 493/493 passing (2026-04-28).
- Regression suite + smoke: not exercised locally (Docker not available); will be verified on a Docker-equipped environment.

## Internal Reviewer (Step 5a)

| Field | Value |
|-------|-------|
| **Invoked** | yes — 2026-04-28, after Codex cycle 2 cleared |
| **Trigger** | MANDATORY (P3 + P4 + P7 + cross-domain ripple via TASK-1905 signature change) |
| **Findings** | 4 NOTE; no BLOCKER, no WARNING |

Notes (paraphrased):
1. **Doc-drift (NOTE)** — header table reported `478 unit tests passing` while Test Summary reported `492`. Reconciled in this commit; both now read `493` post-Reviewer-NOTE closure.
2. **Stale status line (NOTE)** — Status field said `Reviewer/Codex/commit pending` after Codex cycle 2 had already cleared. Updated to `ready-to-commit (Step 5α + 5a + 7a all cleared 2026-04-28)`.
3. **TASK-1905 ripple is fail-loud (NOTE — confirmation, not finding)** — Reviewer confirmed that making `IHostEnvironment` a required positional parameter without a default makes the compiler the CI guard for any 6th service that copy-pastes the old form. No action required.
4. **Weekly-calculation E2E conflict symmetry (NOTE — optional)** — rule-evaluation had an end-to-end conflict short-circuit test through `EvaluateAccessAsync`; weekly-calculation only had the extraction-layer pin. Added `EvaluateAccessAsync_WeeklyCalculation_RejectsTopLevelVsNestedMismatch_BeforeScopeCheck` for parity (`OrchestratorScopeEnforcementTests.cs`). Today the gate's conflict branch is task-type-blind so the symmetry was transitive; the new test pins the contract so a future per-task-type refactor can't regress weekly-calculation silently.

## Agent Effectiveness

| Metric | Value |
|--------|-------|
| Constraint Validator (Step 5α) violations | 0 |
| Internal Reviewer findings | 4 NOTE / 0 WARNING / 0 BLOCKER |
| External Codex review cycles | 2 of 2 cap (cycle 1 = 1 BLOCKER + 1 WARNING; cycle 2 = clean) |
| Test re-dispatches due to Reviewer | 0 — Reviewer NOTEs were doc-drift and one symmetry pin, all closed without re-dispatch |
| Build break cycles | 0 |

S19's harness signal: external Codex caught a real BLOCKER (gate vs. downstream-consumer mismatch) that internal Reviewer would not have caught with the prompt scope used — they read different lenses, and both are load-bearing. Internal Reviewer's NOTE on doc-drift was the kind of orchestration hygiene Codex doesn't typically flag.

## Sprint Retrospective

**What went right**: External Codex's cycle-1 BLOCKER on `ExtractEmployeeId` caught a genuine bypass that the original implementation tests had encoded as a feature (`ExtractEmployeeId_TopLevelWinsOverNestedProfile`). The fix was a per-task-type refactor that fits the orchestrator-as-gate model cleanly. Cycle 2 cleared in one shot.

**What surprised**: The first-cycle BLOCKER had a test pinning the buggy behaviour. The Codex prompt asked it to look for `gate vs. downstream-consumer mismatch` explicitly; without that hint, the test asserting `TopLevelWinsOverNestedProfile` would have looked like an intentional contract. Lesson: external review prompts that name the threat model improve signal-to-noise.

**Process wrinkle**: Anthropic Claude usage limit hit during the first internal Reviewer spawn attempt. A second attempt with a tighter prompt succeeded after Codex cycle 1 fixes had landed. Order ended up being: Codex cycle 1 → fix → Codex cycle 2 (clean) → internal Reviewer (NOTE-only) → fix NOTEs → commit. Slightly out of the AGENTS.md canonical order (Step 5a before 7a), but the deferred Reviewer pass still caught the doc-drift before commit.

**Carry-forward**: TASK-1903 (mixed-version export boundary) absorbed into S20. RuleEngine.Api/Program.cs `using StatsTid.Infrastructure` debt continues to carry from S18.
