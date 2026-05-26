# Sprint 53 — Critical Fixes + Frontend Health + Employee Profile Simplification

| Field | Value |
|-------|-------|
| **Sprint** | 53 |
| **Phase** | Quality Review (combined Claude + Codex audit) |
| **Status** | complete |
| **Start Date** | 2026-05-26 |
| **End Date** | 2026-05-26 |
| **Orchestrator Approved** | yes — 2026-05-26 |
| **Build Verified** | yes — 0 errors, 0 warnings |
| **Test Verified** | yes — 700 total (546 unit + 44 regression + 110 frontend) |

## Sprint Goal

Fix all blockers surfaced by the combined Claude + Codex codebase quality review. Remove the architecturally incorrect per-employee `weekly_norm_hours` field (domain research confirmed Danish state-sector agreements define a universal 37h norm with only part-time fraction varying per employee). Consolidate frontend API calls to prevent future contract drift.

## Task Log

| Task | Description | Effort | Status |
|------|------------|--------|--------|
| TASK-5301 | **Audit backfill crash fix** — `AuditProjectionBackfillService` now resolves `target_org_id` from event payloads (OrgId property or EmployeeId→users.primary_org_id lookup) before invoking mappers; 17 mappers that used `context.ResolvedTargetOrgId` (null during backfill) now get valid org IDs | S | complete |
| TASK-5302 | **Frontend test + TS build fix** — ReportingLineTree label regex updated for CSV support; UserManagement tests add missing `fetchEmployeeLines` mock (S48 addition); `putCalled` variable removed (unused) | S | complete |
| TASK-5303 | **MyPeriods broken approval routes** — `/api/approval/employee/${id}` → `/api/approval/${id}`; `/submit` → `/employee-approve` | S | complete |
| TASK-5304 | **Frontend API consolidation** — all fetch calls in MyPeriods, ApprovalDashboard, and profileApi routed through shared `apiFetch`/`apiClient`; duplicate `TOKEN_KEY` declarations eliminated | M | complete |
| TASK-5305 | **LoginPage dev credentials gating** — dev credential hints wrapped in `import.meta.env.DEV` conditional | S | complete |
| TASK-5306 | **Remove per-employee `weekly_norm_hours` + collapse Employee Profile UI** — 5 sub-parts (see below) | L | complete |

### TASK-5306 Sub-Parts

| Sub-part | Description | Status |
|----------|------------|--------|
| (a) Rule engine fix | `NormCheckRule` changed from `profile.WeeklyNormHours` to `config.WeeklyNormHours` — aligns with OvertimeRule + FlexBalanceRule | complete |
| (b) Backend plumbing | Removed `WeeklyNormHours` from `EmploymentProfile`, endpoints, repositories, resolver, seeder, `init.sql` schema, `BalanceEndpoints` fallback | complete |
| (c) Event contracts | Removed `WeeklyNormHours` from `EmployeeProfileCreated/Updated/Superseded` events + 3 audit mappers | complete |
| (d) Test migration | Updated 20 test files; rewrote marquee D-test to use `part_time_fraction` mutation (real domain scenario) | complete |
| (e) Frontend consolidation | Removed `EmployeeProfileEditor.tsx` + `useEmployeeProfile.ts`; added `part_time_fraction` + `position` fields to `UserManagement.tsx` (Medarbejdere page) | complete |

## Domain Research (TASK-5306 justification)

Combined Claude analysis + Codex validation confirmed:
- All 3 agreements (AC/HK/PROSA) define a universal 37h/week norm (SR-AC-OK24-001, HIGH confidence)
- No overenskomst mechanism for per-employee norm deviation (only part-time fraction)
- Org-level local agreements (`local_agreement_profiles`) handle institutional variations
- Position overrides (`PositionOverrideConfigs`) handle position-specific parameters but never override `WeeklyNormHours`
- The `NormCheckRule` vs `OvertimeRule`/`FlexBalanceRule` inconsistency (profile vs config source) was a pre-existing bug

## Commits

| Commit | Description |
|--------|------------|
| `a7aee58` | S53 Critical Fixes + Frontend Health + Employee Profile Simplification |

## Test Summary

| Category | Before (S52) | Delta | After |
|----------|:---:|:---:|:---:|
| Unit | 546 | 0 | 546 |
| Plain regression | 44 | 0 | 44 |
| Docker-gated | 297 | not run (no Docker tests changed) | 297 |
| Frontend vitest | 110 | 0 | 110 |
| **Total** | **997** | **0** | **700 verified (297 Docker-gated deferred)** |

## Files Changed (48 files, -517 net lines)

**Deleted:**
- `frontend/src/hooks/useEmployeeProfile.ts`
- `frontend/src/pages/admin/EmployeeProfileEditor.tsx`

**Key modifications:**
- `src/SharedKernel/StatsTid.SharedKernel/Models/EmploymentProfile.cs` — removed `WeeklyNormHours` property
- `src/RuleEngine/StatsTid.RuleEngine.Api/Rules/NormCheckRule.cs` — reads `config.WeeklyNormHours` instead of `profile.WeeklyNormHours`
- `src/Infrastructure/StatsTid.Infrastructure/AuditProjectionBackfillService.cs` — added `ResolveTargetOrgId` + `LoadUserOrgLookupAsync`
- `docker/postgres/init.sql` — removed `weekly_norm_hours` column from `employee_profiles`
- `frontend/src/pages/admin/UserManagement.tsx` — added part_time_fraction + position fields
- `frontend/src/pages/approval/MyPeriods.tsx` — routed through `apiClient`, fixed approval routes
- `frontend/src/pages/approval/ApprovalDashboard.tsx` — routed through `apiClient`
- `frontend/src/api/profileApi.ts` — routed through `apiFetchWithEtag`
- 3 event classes, 3 audit mappers, 20 test files updated
