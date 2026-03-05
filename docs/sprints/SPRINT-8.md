# Sprint 8 — Frontend: Design System + Role-Based UI

| Field | Value |
|-------|-------|
| **Sprint** | 8 |
| **Status** | complete |
| **Start Date** | 2026-03-04 |
| **End Date** | 2026-03-04 |
| **Orchestrator Approved** | yes — 2026-03-04 |
| **Build Verified** | yes — `dotnet build` 0 warnings 0 errors; `tsc --noEmit` 0 errors; `vite build` succeeds |
| **Test Verified** | yes — 202 unit + 15 regression = 217 backend tests passing; 25 frontend tests passing |

## Sprint Goal
Transform the minimal Sprint 2 frontend scaffold into a production-quality role-based application covering all 30 backend API endpoints, using a designsystem.dk-inspired aesthetic (ADR-011). Split into Sprint 8a (foundation + employee pages) and Sprint 8b (admin + approval + config pages).

## Architectural Constraints Verified

- [x] P1 — Architectural integrity preserved (frontend consumes backend APIs as-is, no backend changes)
- [ ] P2 — Rule engine determinism maintained (no rule engine changes this sprint)
- [ ] P3 — Event sourcing append-only semantics respected (no event changes this sprint)
- [ ] P4 — OK version correctness (no version logic changes this sprint)
- [ ] P5 — Integration isolation and delivery guarantees (no integration changes this sprint)
- [ ] P6 — Payroll integration correctness (no payroll changes this sprint)
- [x] P7 — Security and access control (role-based route guards, JWT decode, 401/403 handling)
- [x] P8 — CI/CD enforcement (tsc + vite build verified, 25 vitest tests added)
- [x] P9 — Usability and UX (full design system, role-based navigation, all pages styled)

## Task Log

### TASK-801 — Design tokens + global styles + font loading

| Field | Value |
|-------|-------|
| **ID** | TASK-801 |
| **Status** | complete |
| **Agent** | UX Agent (worktree Agent A, Phase 1) |
| **Components** | Frontend (styles) |
| **KB Refs** | ADR-011 |
| **Reviewer Audit** | skipped — pure UI, no backend change |
| **Orchestrator Approved** | yes — 2026-03-04 |

**Description**: Created CSS custom properties design token foundation with designsystem.dk-inspired palette, 8px spacing grid, IBM Plex Sans typography, sharp corners (0px radius), 1px borders. Loaded @fontsource/ibm-plex-sans (400/500/600/700 weights).

**Validation Criteria**:
- [x] tokens.css defines all color, spacing, typography, border tokens
- [x] global.css provides reset and base styles consuming tokens
- [x] utilities.css provides layout helpers
- [x] @fontsource/ibm-plex-sans installed and imported in main.tsx
- [x] tsc + vite build passes

**Files Changed**:
- `frontend/src/styles/tokens.css` — new: CSS custom properties (colors, spacing, typography, borders, focus)
- `frontend/src/styles/global.css` — new: reset + body defaults + heading styles
- `frontend/src/styles/utilities.css` — new: layout helpers (visually-hidden, flex-row, flex-col, gaps)
- `frontend/src/main.tsx` — modified: added font and style imports
- `frontend/package.json` — modified: added @fontsource/ibm-plex-sans dependency

---

### TASK-802 — Core component library (14 scratch-built components)

| Field | Value |
|-------|-------|
| **ID** | TASK-802 |
| **Status** | complete |
| **Agent** | UX Agent (worktree Agent A, Phase 1) |
| **Components** | Frontend (components/ui) |
| **KB Refs** | ADR-011 |
| **Reviewer Audit** | skipped — pure UI |
| **Orchestrator Approved** | yes — 2026-03-04 |

**Description**: Built 14 scratch components with CSS Modules consuming design tokens: Button, Input, Textarea, Checkbox, Radio, Badge, Alert, Card, Label, Spinner, Divider, Table, FormField, plus barrel export index.ts. All sharp corners, 1px borders, WCAG AA contrast.

**Validation Criteria**:
- [x] 14 components created with CSS Modules
- [x] All consume tokens.css custom properties (no hardcoded colors/spacing)
- [x] Barrel export in index.ts
- [x] tsc + vite build passes

**Files Changed**:
- `frontend/src/components/ui/Button.tsx` + `Button.module.css` — new
- `frontend/src/components/ui/Input.tsx` + `Input.module.css` — new
- `frontend/src/components/ui/Textarea.tsx` + `Textarea.module.css` — new
- `frontend/src/components/ui/Checkbox.tsx` + `Checkbox.module.css` — new
- `frontend/src/components/ui/Radio.tsx` + `Radio.module.css` — new
- `frontend/src/components/ui/Badge.tsx` + `Badge.module.css` — new
- `frontend/src/components/ui/Alert.tsx` + `Alert.module.css` — new
- `frontend/src/components/ui/Card.tsx` + `Card.module.css` — new
- `frontend/src/components/ui/Label.tsx` + `Label.module.css` — new
- `frontend/src/components/ui/Spinner.tsx` + `Spinner.module.css` — new
- `frontend/src/components/ui/Divider.tsx` + `Divider.module.css` — new
- `frontend/src/components/ui/Table.tsx` + `Table.module.css` — new
- `frontend/src/components/ui/FormField.tsx` + `FormField.module.css` — new
- `frontend/src/components/ui/index.ts` — new: barrel export

---

### TASK-803 — Auth context + JWT decode

| Field | Value |
|-------|-------|
| **ID** | TASK-803 |
| **Status** | complete |
| **Agent** | UX Agent (worktree Agent B, Phase 1) |
| **Components** | Frontend (contexts, lib) |
| **KB Refs** | ADR-009 |
| **Reviewer Audit** | skipped — pure UI |
| **Orchestrator Approved** | yes — 2026-03-04 |

**Description**: Created AuthContext React Context with JWT decode. Parses base64url payload to extract role, orgId, scopes. Auto-clears expired tokens. Provides useAuth() hook app-wide.

**Validation Criteria**:
- [x] AuthProvider wraps app, manages token/user in localStorage
- [x] JWT decode extracts sub, role, org_id, scopes, exp
- [x] Auto-logout on token expiry
- [x] useAuth() exposes user, token, isAuthenticated, role, orgId, scopes, login, logout
- [x] tsc + vite build passes

**Files Changed**:
- `frontend/src/contexts/AuthContext.tsx` — new: React Context provider + useAuth hook
- `frontend/src/lib/jwt.ts` — new: JWT base64url decode, parseScopes, isTokenExpired
- `frontend/src/hooks/useAuth.ts` — modified: re-export from AuthContext
- `frontend/src/App.tsx` — modified: wrapped with AuthProvider

---

### TASK-804 — Centralized API client

| Field | Value |
|-------|-------|
| **ID** | TASK-804 |
| **Status** | complete |
| **Agent** | UX Agent (worktree Agent B, Phase 1) |
| **Components** | Frontend (lib, hooks) |
| **KB Refs** | — |
| **Reviewer Audit** | skipped — pure UI |
| **Orchestrator Approved** | yes — 2026-03-04 |

**Description**: Created centralized fetch wrapper (apiClient) with typed ApiResult<T>, Bearer token injection, 401 auto-logout, 403 typed error, 204 handling. Refactored 3 existing hooks to use apiClient.

**Validation Criteria**:
- [x] apiClient.get/post/put/delete with typed results
- [x] Auth header injected from localStorage
- [x] 401 triggers storage clear + reload, 403 returns typed error
- [x] Existing hooks refactored to use apiClient
- [x] tsc + vite build passes

**Files Changed**:
- `frontend/src/lib/api.ts` — new: centralized API client
- `frontend/src/hooks/useTimeEntries.ts` — modified: use apiClient
- `frontend/src/hooks/useAbsences.ts` — modified: use apiClient
- `frontend/src/hooks/useFlexBalance.ts` — modified: use apiClient

---

### TASK-805 — Layout shell + role-based navigation

| Field | Value |
|-------|-------|
| **ID** | TASK-805 |
| **Status** | complete |
| **Agent** | UX Agent (worktree Agent C, Phase 2) |
| **Components** | Frontend (components/layout) |
| **KB Refs** | ADR-009, ADR-011 |
| **Reviewer Audit** | skipped — pure UI |
| **Orchestrator Approved** | yes — 2026-03-04 |

**Description**: Created Sidebar with role-based navigation (5 role levels), Header with user info + role badge + logout, AppLayout combining sidebar + header + content Outlet. Danish labels throughout.

**Validation Criteria**:
- [x] Sidebar shows nav items based on role hierarchy
- [x] Active route highlighted with primary color
- [x] Header shows org, employeeId, role badge, logout
- [x] AppLayout composes sidebar + header + Outlet
- [x] tsc + vite build passes

**Files Changed**:
- `frontend/src/components/layout/Sidebar.tsx` + `Sidebar.module.css` — new
- `frontend/src/components/layout/Header.tsx` + `Header.module.css` — new
- `frontend/src/components/layout/AppLayout.tsx` + `AppLayout.module.css` — new
- `frontend/src/lib/roles.ts` — new: ROLE_LEVELS, hasMinRole utility

---

### TASK-806 — Route guards + routing structure

| Field | Value |
|-------|-------|
| **ID** | TASK-806 |
| **Status** | complete |
| **Agent** | UX Agent (worktree Agent C, Phase 2) |
| **Components** | Frontend (guards, pages, routing) |
| **KB Refs** | ADR-009 |
| **Reviewer Audit** | skipped — pure UI |
| **Orchestrator Approved** | yes — 2026-03-04 |

**Description**: Created RequireAuth (redirects to /login if unauthenticated) and RequireRole (checks role hierarchy, shows ForbiddenPage). Restructured App.tsx with nested routes under AppLayout.

**Validation Criteria**:
- [x] RequireAuth redirects to /login
- [x] RequireRole checks role hierarchy using hasMinRole
- [x] ForbiddenPage and NotFoundPage created
- [x] Nested route structure with role guards
- [x] tsc + vite build passes

**Files Changed**:
- `frontend/src/components/guards/RequireAuth.tsx` — new
- `frontend/src/components/guards/RequireRole.tsx` — new
- `frontend/src/pages/ForbiddenPage.tsx` + `ForbiddenPage.module.css` — new
- `frontend/src/pages/NotFoundPage.tsx` + `NotFoundPage.module.css` — new
- `frontend/src/App.tsx` — modified: nested route structure with guards

---

### TASK-807 — Restyle existing pages + components

| Field | Value |
|-------|-------|
| **ID** | TASK-807 |
| **Status** | complete |
| **Agent** | UX Agent (worktree Agent D, Phase 2) |
| **Components** | Frontend (pages, components) |
| **KB Refs** | ADR-011 |
| **Reviewer Audit** | skipped — pure UI |
| **Orchestrator Approved** | yes — 2026-03-04 |

**Description**: Restyled all 5 existing pages and 3 components from inline styles to CSS Modules + design tokens. Replaced hardcoded employeeId inputs with auth context. Uses new UI components (Button, Input, FormField, Table, Card, Badge).

**Validation Criteria**:
- [x] All inline styles replaced with CSS Modules
- [x] Design tokens consumed (no hardcoded colors/spacing)
- [x] New UI components used throughout
- [x] Auth context used for employeeId
- [x] tsc + vite build passes

**Files Changed**:
- `frontend/src/pages/LoginPage.tsx` + `LoginPage.module.css` — restyled
- `frontend/src/pages/TimeRegistration.tsx` + `TimeRegistration.module.css` — restyled
- `frontend/src/pages/WeeklyView.tsx` + `WeeklyView.module.css` — restyled
- `frontend/src/pages/AbsenceRegistration.tsx` + `AbsenceRegistration.module.css` — restyled
- `frontend/src/pages/HealthDashboard.tsx` + `HealthDashboard.module.css` — restyled
- `frontend/src/components/TimeEntryForm.tsx` + `TimeEntryForm.module.css` — restyled
- `frontend/src/components/WeekGrid.tsx` + `WeekGrid.module.css` — restyled
- `frontend/src/components/FlexBalanceCard.tsx` + `FlexBalanceCard.module.css` — restyled

---

### TASK-808 — Updated types + extended hooks

| Field | Value |
|-------|-------|
| **ID** | TASK-808 |
| **Status** | complete |
| **Agent** | UX Agent (worktree Agent D, Phase 2) |
| **Components** | Frontend (types, hooks) |
| **KB Refs** | — |
| **Reviewer Audit** | skipped — pure UI |
| **Orchestrator Approved** | yes — 2026-03-04 |

**Description**: Extended types.ts with Organization, User, RoleAssignment, ApprovalPeriod, LocalConfiguration, ConfigConstraint. Created 3 new hooks (useApprovals, useAdmin, useConfig) consuming apiClient.

**Validation Criteria**:
- [x] All admin/approval/config types added to types.ts
- [x] useApprovals: submit, approve, reject, pending
- [x] useAdmin: org CRUD, user CRUD, role grant/revoke
- [x] useConfig: effective config, local overrides, constraints
- [x] All hooks use apiClient
- [x] tsc + vite build passes

**Files Changed**:
- `frontend/src/types.ts` — modified: added 6 new types
- `frontend/src/hooks/useApprovals.ts` — new
- `frontend/src/hooks/useAdmin.ts` — new
- `frontend/src/hooks/useConfig.ts` — new

---

### TASK-809 — Radix primitive setup + restyled complex components

| Field | Value |
|-------|-------|
| **ID** | TASK-809 |
| **Status** | complete |
| **Agent** | UX Agent (Phase 3) |
| **Components** | Frontend (components/ui) |
| **KB Refs** | ADR-011 |
| **Reviewer Audit** | skipped — pure UI |
| **Orchestrator Approved** | yes — 2026-03-04 |

**Description**: Installed 6 Radix UI primitives and built styled wrappers: Dialog (modal focus trap), Select (keyboard nav), Toast (portal + timer), DropdownMenu, Tabs, Tooltip. All Radix default styles fully overridden with design tokens.

**Validation Criteria**:
- [x] 6 Radix packages installed
- [x] 6 component wrappers with CSS Modules
- [x] All Radix default styles overridden with design tokens
- [x] Sharp corners, 1px borders, IBM Plex Sans
- [x] Barrel exports updated in index.ts
- [x] tsc + vite build passes

**Files Changed**:
- `frontend/src/components/ui/Dialog.tsx` + `Dialog.module.css` — new
- `frontend/src/components/ui/Select.tsx` + `Select.module.css` — new
- `frontend/src/components/ui/Toast.tsx` + `Toast.module.css` — new
- `frontend/src/components/ui/DropdownMenu.tsx` + `DropdownMenu.module.css` — new
- `frontend/src/components/ui/Tabs.tsx` + `Tabs.module.css` — new
- `frontend/src/components/ui/Tooltip.tsx` + `Tooltip.module.css` — new
- `frontend/src/components/ui/index.ts` — modified: added 6 new exports
- `frontend/package.json` — modified: added 6 @radix-ui/* dependencies

---

### TASK-810 — Approval workflow — employee view (Mine perioder)

| Field | Value |
|-------|-------|
| **ID** | TASK-810 |
| **Status** | complete |
| **Agent** | UX Agent (worktree Agent E, Phase 4) |
| **Components** | Frontend (pages/approval) |
| **KB Refs** | — |
| **Reviewer Audit** | skipped — pure UI |
| **Orchestrator Approved** | yes — 2026-03-04 |

**Description**: Employee period submission page. Shows own approval periods, submit button for DRAFT/REJECTED, status badges (DRAFT gray, SUBMITTED warning, APPROVED success, REJECTED error with reason), period type selector, date range inputs.

**Validation Criteria**:
- [x] Lists employee's approval periods
- [x] Submit action for DRAFT/REJECTED periods
- [x] Status badges with correct color variants
- [x] Period type and date range inputs
- [x] tsc + vite build passes

**Files Changed**:
- `frontend/src/pages/approval/MyPeriods.tsx` + `MyPeriods.module.css` — new

---

### TASK-811 — Approval workflow — leader dashboard (Godkendelser)

| Field | Value |
|-------|-------|
| **ID** | TASK-811 |
| **Status** | complete |
| **Agent** | UX Agent (worktree Agent E, Phase 4) |
| **Components** | Frontend (pages/approval) |
| **KB Refs** | — |
| **Reviewer Audit** | skipped — pure UI |
| **Orchestrator Approved** | yes — 2026-03-04 |

**Description**: Leader approval dashboard. Lists pending periods, approve/reject buttons, rejection reason dialog, employee details per row. Confirmation dialogs and toast notifications.

**Validation Criteria**:
- [x] Lists pending periods for leader's scope
- [x] Approve and reject actions with dialogs
- [x] Employee info, period dates, agreement code shown
- [x] tsc + vite build passes

**Files Changed**:
- `frontend/src/pages/approval/ApprovalDashboard.tsx` + `ApprovalDashboard.module.css` — new

---

### TASK-812 — Organization management

| Field | Value |
|-------|-------|
| **ID** | TASK-812 |
| **Status** | complete |
| **Agent** | UX Agent (worktree Agent F, Phase 4) |
| **Components** | Frontend (pages/admin) |
| **KB Refs** | ADR-008 |
| **Reviewer Audit** | skipped — pure UI |
| **Orchestrator Approved** | yes — 2026-03-04 |

**Description**: Organization list with hierarchy visualization, create org form in dialog. Fields: orgId, orgName, orgType (MINISTRY/STYRELSE/AFDELING/TEAM), parentOrgId, agreementCode.

**Validation Criteria**:
- [x] Lists organizations with hierarchy indentation
- [x] Create organization dialog with all fields
- [x] Org type selector
- [x] tsc + vite build passes

**Files Changed**:
- `frontend/src/pages/admin/OrgManagement.tsx` + `OrgManagement.module.css` — new

---

### TASK-813 — User management

| Field | Value |
|-------|-------|
| **ID** | TASK-813 |
| **Status** | complete |
| **Agent** | UX Agent (worktree Agent F, Phase 4) |
| **Components** | Frontend (pages/admin) |
| **KB Refs** | — |
| **Reviewer Audit** | skipped — pure UI |
| **Orchestrator Approved** | yes — 2026-03-04 |

**Description**: Org-filtered user list, create user dialog, edit user dialog. Fields: username, password (create only), displayName, email, primaryOrgId, agreementCode.

**Validation Criteria**:
- [x] Org selector filters user list
- [x] Create and edit user dialogs
- [x] Correct fields per mode (password on create only)
- [x] tsc + vite build passes

**Files Changed**:
- `frontend/src/pages/admin/UserManagement.tsx` + `UserManagement.module.css` — new

---

### TASK-814 — Role management

| Field | Value |
|-------|-------|
| **ID** | TASK-814 |
| **Status** | complete |
| **Agent** | UX Agent (worktree Agent G, Phase 4) |
| **Components** | Frontend (pages/admin) |
| **KB Refs** | ADR-009 |
| **Reviewer Audit** | skipped — pure UI |
| **Orchestrator Approved** | yes — 2026-03-04 |

**Description**: Role assignment management. Select user, view role assignments, grant role (roleId, orgId, scopeType, expiresAt), revoke role with confirmation. Shows role name, org, scope type, assigned by, expires.

**Validation Criteria**:
- [x] User selection + role assignment list
- [x] Grant role dialog with scope type selector
- [x] Revoke role with confirmation
- [x] Uses shared hooks (useAdmin, useConfig)
- [x] tsc + vite build passes

**Files Changed**:
- `frontend/src/pages/admin/RoleManagement.tsx` + `RoleManagement.module.css` — new

---

### TASK-815 — Local configuration management

| Field | Value |
|-------|-------|
| **ID** | TASK-815 |
| **Status** | complete |
| **Agent** | UX Agent (worktree Agent G, Phase 4) |
| **Components** | Frontend (pages/config) |
| **KB Refs** | ADR-010 |
| **Reviewer Audit** | skipped — pure UI |
| **Orchestrator Approved** | yes — 2026-03-04 |

**Description**: Three-tab config management: Effective Config (merged view), Local Overrides (CRUD), Central Constraints (reference). Create override dialog with constraint validation fields. Deactivate with confirmation.

**Validation Criteria**:
- [x] Three tabs: Effective, Local Overrides, Constraints
- [x] Create override dialog with all fields
- [x] Deactivate override with confirmation
- [x] Uses Tabs, Table, Dialog, Select, FormField components
- [x] tsc + vite build passes

**Files Changed**:
- `frontend/src/pages/config/ConfigManagement.tsx` + `ConfigManagement.module.css` — new

---

### TASK-816 — Route integration + final wiring

| Field | Value |
|-------|-------|
| **ID** | TASK-816 |
| **Status** | complete |
| **Agent** | Orchestrator (direct, small task) |
| **Components** | Frontend (routing) |
| **KB Refs** | — |
| **Reviewer Audit** | skipped — pure UI, < 10 lines |
| **Orchestrator Approved** | yes — 2026-03-04 |

**Description**: Replaced placeholder `<div>` elements in App.tsx routes with actual Sprint 8b page component imports. Wired all 6 new pages into the route structure with correct role guards.

**Validation Criteria**:
- [x] All 6 Sprint 8b pages imported and routed
- [x] Role guards maintained (LocalLeader for approval, LocalHR for users, LocalAdmin for orgs/roles/config)
- [x] tsc + vite build passes

**Files Changed**:
- `frontend/src/App.tsx` — modified: replaced 6 placeholder divs with actual page components

---

### TASK-817 — Sprint 8 frontend tests

| Field | Value |
|-------|-------|
| **ID** | TASK-817 |
| **Status** | complete |
| **Agent** | UX Agent |
| **Components** | Frontend (tests, config) |
| **KB Refs** | — |
| **Reviewer Audit** | skipped — pure UI |
| **Orchestrator Approved** | yes — 2026-03-04 |

**Description**: Set up vitest + @testing-library/react test infrastructure. Created 4 test files (25 tests) covering JWT decode, API client (401/403/204/network errors), role hierarchy, and Button component rendering.

**Validation Criteria**:
- [x] vitest, jsdom, @testing-library/react, @testing-library/jest-dom, @testing-library/user-event installed
- [x] vite.config.ts updated with vitest test config
- [x] 4 test files, 25 tests, all passing
- [x] `npm run test` works
- [x] tsc + vite build still passes

**Files Changed**:
- `frontend/vite.config.ts` — modified: added vitest test config
- `frontend/tsconfig.json` — modified: added vitest/globals types
- `frontend/package.json` — modified: added test script + devDependencies
- `frontend/src/test/setup.ts` — new: test setup importing jest-dom matchers
- `frontend/src/lib/__tests__/jwt.test.ts` — new: 7 JWT tests
- `frontend/src/lib/__tests__/api.test.ts` — new: 7 API client tests
- `frontend/src/lib/__tests__/roles.test.ts` — new: 5 role hierarchy tests
- `frontend/src/components/ui/__tests__/Button.test.tsx` — new: 6 Button render tests

---

## Legal & Payroll Verification

| Check | Status | Notes |
|-------|--------|-------|
| Agreement rules match legal requirements | N/A | No rule engine changes this sprint |
| Wage type mappings produce correct SLS codes | N/A | No payroll changes this sprint |
| Overtime/supplement calculations are deterministic | N/A | No rule changes this sprint |
| Absence effects on norm/flex/pension are correct | N/A | No absence logic changes this sprint |
| Retroactive recalculation produces stable results | N/A | No retroactive changes this sprint |

## Test Summary

| Suite | Count | Status |
|-------|-------|--------|
| Backend unit tests | 202 | all passing |
| Backend regression tests | 15 | all passing |
| Backend smoke tests | 4 | N/A (requires Docker) |
| Frontend tests (vitest) | 25 | all passing |
| **Total** | 242 | — |

## Sprint Retrospective

**What went well**: Massive scope (~65 new files) completed in a single sprint by parallelizing UX agents across worktrees. Phase-based execution (foundation → layout → pages) prevented merge conflicts. Design token system provides consistent visual language across all components.

**What to improve**: Some agents in worktrees recreated Phase 1 files since worktrees branched from pre-Phase-1 state — selective merge was needed. Agents E and F wrote self-contained pages with local fetch instead of using shared hooks/apiClient — future refinement could standardize all pages on shared infrastructure. Badge variant type mismatch caught during merge (variant="primary" not in type union).

**Knowledge produced**: No new KB entries (Sprint 8 consumed ADR-011 established pre-sprint). ADR-011 validated in practice — scratch-built components + Radix primitives for complex interactions works well.
