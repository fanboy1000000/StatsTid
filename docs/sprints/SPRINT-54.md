# Sprint 54 — UI Restructuring: Two-Level Navigation + Skema Approval Flow

| Field | Value |
|-------|-------|
| **Sprint** | 54 |
| **Phase** | UI & Functionality Improvement |
| **Status** | complete |
| **Start Date** | 2026-05-26 |
| **End Date** | 2026-05-26 |
| **Orchestrator Approved** | yes — 2026-05-26 |
| **Build Verified** | yes — 0 errors |
| **Test Verified** | yes — 700 total (546 unit + 44 regression + 110 frontend) |

## Sprint Goal

Replace the flat single-sidebar navigation with a two-level system: 5 role-gated top-level tabs + per-tab left sidebar with menu items. Fix the Skema approval flow (entries disappearing, missing reopen button, missing validation error display).

## Navigation Structure

| Tab | Min role | Menu items |
|-----|----------|------------|
| **Min tid** | Employee (all) | Registrering, Oversigt (placeholder) |
| **Godkend tid** | LocalLeader | Godkendelser, Vikariering |
| **Administration** | LocalHR | Medarbejdere, Audit log, Projekter*, Ledelseslinjer*, Brugerrettigheder* |
| **Lokale tilpasninger** | LocalAdmin | Lokal OK konfiguration, Lokale stillingstilpasninger |
| **Global administration** | GlobalAdmin | Overenskomster, Organisation, Lønartstilknytning |

*Items marked with * require LocalAdmin+ and are hidden from LocalHR users.

## Intentional RBAC Changes

| Page | Previous role | New role | Rationale |
|------|-------------|----------|-----------|
| OrgManagement | LocalAdmin | **GlobalAdmin** | Organisation management is a global concern |
| PositionOverrideManagement | GlobalAdmin | **LocalAdmin** | Position overrides are local customizations |

## Task Log

| Task | Description | Status |
|------|------------|--------|
| TASK-5401 | **TopNav component** — 5 role-gated tabs with `aria-current="page"`, active state by URL prefix, mobile overflow handling | complete |
| TASK-5402 | **Sidebar rewrite** — context-aware per-tab menu items, per-item role gating via `hasMinRole` | complete |
| TASK-5403 | **AppLayout update** — TopNav rendered between Header and body | complete |
| TASK-5404 | **Route restructuring** — 5 prefix groups (`/tid/`, `/godkend/`, `/admin/`, `/lokal/`, `/global/`), per-route `RequireRole` guards, `OversightPlaceholder.tsx`, root redirect to `/tid/registrering` | complete |
| TASK-5405 | **Internal link updates** — 11 navigate/Link occurrences updated across AgreementConfigList, AgreementConfigEditor, ForbiddenPage, NotFoundPage | complete |
| TASK-5406 | **Full verification** — tsc clean, 110/110 frontend tests, backend builds, app loads | complete |
| TASK-5407 | **Skema approval flow fixes** — see below | complete |

### TASK-5407 Details (Skema Approval Flow)

| Change | Description |
|--------|------------|
| Removed "Mine perioder" | Sidebar entry removed from Min tid tab |
| Reopen button | "Genåbn" appears when status is EMPLOYEE_APPROVED (displayed as "Indsendt") |
| Status display | EMPLOYEE_APPROVED → "Indsendt — afventer leder godkendelse" |
| Rejected resubmit | REJECTED periods show "Godkend maaned" (re-approve flow, not reopen — backend reopen only accepts EMPLOYEE_APPROVED) |
| Validation errors | 422 workday-coverage errors display missing days in Danish date format |
| Data persistence | `submitAndApprove` refetches data even on error — entries no longer disappear |
| `reopenPeriod` | New function in `useSkema` hook calling `POST /api/approval/{periodId}/reopen` |

## Codex Review (pre-commit)

2 BLOCKERs absorbed, 2 WARNINGs absorbed:
- **B1**: PositionOverrideEndpoints backend auth `GlobalAdminOnly` → `LocalAdminOrAbove` (aligned with frontend RBAC change)
- **B2**: REJECTED periods "Genåbn" → "Godkend maaned" (backend reopen only accepts EMPLOYEE_APPROVED)
- **W1**: ARIA `role="tablist"/"tab"` → `aria-current="page"` (correct pattern for route navigation)
- **W2**: Tab list `overflow-x: auto` for mobile

## Commits

| Commit | Description |
|--------|------------|
| `f13eaed` | S54 UI Restructuring: Two-Level Navigation + Skema Approval Flow |

## Test Summary

| Category | Before (S53) | Delta | After |
|----------|:---:|:---:|:---:|
| Unit | 546 | 0 | 546 |
| Plain regression | 44 | 0 | 44 |
| Docker-gated | 297 | not run | 297 |
| Frontend vitest | 110 | 0 | 110 |
| **Total** | **700 verified** | **0** | **700 verified** |

## Files Changed (14 files, +188 net lines)

**Created:**
- `frontend/src/components/layout/TopNav.tsx` — top-level tab navigation component
- `frontend/src/components/layout/TopNav.module.css` — tab bar styles
- `frontend/src/pages/OversightPlaceholder.tsx` — "Kommer snart" placeholder

**Key modifications:**
- `frontend/src/App.tsx` — complete route restructuring under 5 prefixes
- `frontend/src/components/layout/Sidebar.tsx` — context-aware per-tab menu items
- `frontend/src/components/layout/AppLayout.tsx` — added TopNav
- `frontend/src/hooks/useSkema.ts` — reopenPeriod, approval validation errors
- `frontend/src/pages/SkemaPage.tsx` — approval footer rewrite (Indsendt/Genåbn/validation)
- `src/Backend/StatsTid.Backend.Api/Endpoints/PositionOverrideEndpoints.cs` — auth policy `GlobalAdminOnly` → `LocalAdminOrAbove`
