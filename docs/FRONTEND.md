# StatsTid Frontend Reference
> Design system, component patterns, routing, and test infrastructure for UX Agent context.

## Technology Stack
- React 18 + TypeScript
- Vite (dev port 3000, proxy `/api` to `localhost:5100`)
- react-router-dom v6 (BrowserRouter, declarative Routes)
- CSS Modules + CSS custom properties (`tokens.css`)
- vitest + @testing-library/react (jsdom environment)

## Design System (ADR-011, palette amended S57)
- Inspired by designsystem.dk; palette re-skinned S57 to the oes.dk (Økonomistyrelsen) AA-safe scheme
- Font: IBM Plex Sans
- Primary color: `#066b43` (oes-green), hover `#055638`, pressed `#04412b`
- Border radius: `0px` (sharp corners -- Danish gov style)
- Borders: `1px solid` hairlines; no shadows/gradients beyond `--shadow-sm`
- Spacing: 8px baseline grid (`--space-1` through `--space-12`)
- Shadows: minimal (`--shadow-sm: 0 1px 2px rgba(0,0,0,0.05)`)
- Focus ring: `2px solid` gray-600 with `2px` offset (accessibility)

## Design Tokens (`src/styles/tokens.css`)

> **`tokens.css` is canonical** — the values below mirror it as of S57/ADR-011-amended; if they ever disagree, the CSS file wins.

### Colors
| Token | Value | Usage |
|-------|-------|-------|
| `--color-primary` | `#066b43` | Primary actions (6.58:1 on white) |
| `--color-primary-hover` / `--color-primary-dark` | `#055638` | Hover states |
| `--color-primary-darker` | `#04412b` | Pressed/active states |
| `--color-text` | `#343536` | Body text (12.3:1) |
| `--color-text-secondary` | `#6b6b6e` | Secondary text, AA-safe (5.31:1) |
| `--color-gray-50` to `--color-gray-600` | Warm-neutral scale `#f7f6f6`…`#55565a` | Backgrounds, borders, muted text (gray-400 is UI-only, NOT text) |
| `--color-white` | `#FFFFFF` | Card backgrounds |
| `--color-border` / `--color-border-strong` | `#e8e6e6` / `#55565a` | Hairlines / strong rules |
| `--color-success` / `--color-success-light` | `#0f766e` / `#ccfbf1` | Success states |
| `--color-error` / `--color-error-light` | `#cc0000` / `#fee2e2` | Error states |
| `--color-warning` / `--color-warning-light` | `#8a6a00` / `#fef9c3` | Warning states |
| `--color-info` / `--color-info-light` | `#1a6a86` / `#ddeaf3` | Informational states (info-light = current-period highlight) |
| `--color-link` | `#3e72a6` | Hyperlinks |
| `--color-link-visited` | `#6b4d8a` | Visited links |

### Spacing (8px baseline)
| Token | Value |
|-------|-------|
| `--space-1` | `4px` |
| `--space-2` | `8px` |
| `--space-3` | `12px` |
| `--space-4` | `16px` |
| `--space-6` | `24px` |
| `--space-8` | `32px` |
| `--space-10` | `40px` |
| `--space-12` | `48px` |

### Typography
| Token | Value |
|-------|-------|
| `--font-family` | `'IBM Plex Sans', system-ui, sans-serif` |
| `--font-weight-regular` | `400` |
| `--font-weight-medium` | `500` |
| `--font-weight-semibold` | `600` |
| `--font-weight-bold` | `700` |

### Borders & Focus
| Token | Value |
|-------|-------|
| `--border-radius` | `0px` |
| `--border-color` | `var(--color-gray-200)` |
| `--border-focus` | `var(--color-gray-600)` |
| `--border-width` | `1px` |
| `--focus-outline` | `2px solid var(--color-gray-600)` |
| `--focus-outline-offset` | `2px` |

## Component Library (`src/components/`)

### Scratch-built Components
| Component | File | Description |
|-----------|------|-------------|
| Alert | `ui/Alert.tsx` | Status messages (success, error, warning, info) |
| Badge | `ui/Badge.tsx` | Status indicator labels |
| Button | `ui/Button.tsx` | Primary action element |
| Card | `ui/Card.tsx` | Content container with border |
| Checkbox | `ui/Checkbox.tsx` | Boolean input |
| Divider | `ui/Divider.tsx` | Horizontal separator |
| FormField | `ui/FormField.tsx` | Label + input + error wrapper |
| Input | `ui/Input.tsx` | Text input field |
| Label | `ui/Label.tsx` | Form label |
| Radio | `ui/Radio.tsx` | Radio button input |
| Spinner | `ui/Spinner.tsx` | Loading indicator |
| Table | `ui/Table.tsx` | Data table |
| Textarea | `ui/Textarea.tsx` | Multi-line text input |

### Radix-wrapped Components
| Component | File | Description |
|-----------|------|-------------|
| Dialog | `ui/Dialog.tsx` | Modal dialog (Radix Dialog primitive) |
| DropdownMenu | `ui/DropdownMenu.tsx` | Dropdown menu (Radix DropdownMenu) |
| Select | `ui/Select.tsx` | Select dropdown (Radix Select) |
| Tabs | `ui/Tabs.tsx` | Tab navigation (Radix Tabs) |
| Toast | `ui/Toast.tsx` | Notification toast (Radix Toast) |
| Tooltip | `ui/Tooltip.tsx` | Hover tooltip (Radix Tooltip) |

All UI components are barrel-exported from `ui/index.ts`.

### Domain Components
| Component | File | Description |
|-----------|------|-------------|
| BalanceSummary | `BalanceSummary.tsx` | Employee balance overview (norm, flex, absence) |
| FlexBalanceCard | `FlexBalanceCard.tsx` | Flex balance display card |
| SkemaGrid | `SkemaGrid.tsx` | Monthly spreadsheet grid (days x projects/absence rows); supports read-only/locked rendering (S55) |
| AllocationSummary | `AllocationSummary.tsx` | Work-time allocation reconciliation summary (S56/ADR-028) |
| ComplianceWarnings | `ComplianceWarnings.tsx` | EU-WTD compliance warnings |
| ProjectPicker | `ProjectPicker.tsx` | Project selector for work-time rows |
| TimeEntryForm _(legacy)_ | `TimeEntryForm.tsx` | Superseded by SkemaPage; only referenced by unrouted legacy pages |
| WeekGrid _(legacy)_ | `WeekGrid.tsx` | Superseded by SkemaGrid; only referenced by unrouted `WeeklyView` |

> Removed S56 (ADR-028 timer retirement): `TimerControl.tsx` and the `useTimer` hook no longer exist.

### Layout Components
| Component | File | Description |
|-----------|------|-------------|
| AppLayout | `layout/AppLayout.tsx` | Main layout: `Header` → `TopNav` → (`Sidebar` + `<Outlet/>`) |
| Header | `layout/Header.tsx` | Top bar with user info |
| TopNav | `layout/TopNav.tsx` | Level-1 group tabs (S54 two-level nav), role-gated by group prefix |
| Sidebar | `layout/Sidebar.tsx` | Level-2 nav: items for the active group only, each filtered by `hasMinRole` |

### Route Guards
| Component | File | Description |
|-----------|------|-------------|
| RequireAuth | `guards/RequireAuth.tsx` | Redirects to `/login` if unauthenticated |
| RequireRole | `guards/RequireRole.tsx` | Checks `hasMinRole`, renders ForbiddenPage if insufficient |

## Layout System (two-level navigation — S54)
`AppLayout` wraps all authenticated routes: `Header` → `TopNav` → (`Sidebar` + `<Outlet/>`).
Guards compose via nested `<Route element={<RequireRole minRole=.../>}>` wrappers (see `App.tsx`).

**Level 1 — `TopNav` group tabs** (visible by role, Danish labels):

| Tab | Path prefix | Min Role | First route |
|-----|-------------|----------|-------------|
| Min tid | `/tid` | any auth | `/tid/registrering` |
| Godkend tid | `/godkend` | LocalLeader | `/godkend/godkendelser` |
| Administration | `/admin` | LocalHR | `/admin/medarbejdere` |
| Lokale tilpasninger | `/lokal` | LocalAdmin | `/lokal/ok-konfiguration` |
| Global administration | `/global` | GlobalAdmin | `/global/overenskomster` |

**Level 2 — `Sidebar`** shows items for the active group only, each filtered by `hasMinRole`:
- **Min tid**: Registrering (`/tid/registrering`), Oversigt (`/tid/oversigt`)
- **Godkend tid**: Godkendelser (`/godkend/godkendelser`), Vikariering (`/godkend/vikariering`)
- **Administration**: Medarbejdere (LocalHR), Audit log (LocalHR), Projekter, Ledelseslinjer, Brugerrettigheder (LocalAdmin)
- **Lokale tilpasninger**: Lokal OK konfiguration, Lokale stillingstilpasninger
- **Global administration**: Overenskomster, Organisation, Lønartstilknytning

## Pages & Routes

Routes are defined in `frontend/src/App.tsx`. Unauthenticated → `/login`; authenticated index → `/tid/registrering`.

| Route | Page Component | Min Role | Notes |
|-------|---------------|----------|-------|
| `/login` | `LoginPage` | Public | Redirects to `/tid/registrering` if already authenticated |
| `/` (index) | → redirect | — | → `/tid/registrering` |
| `/tid/registrering` | `SkemaPage` | any auth | Monthly time registration (three-row work-time, S56) |
| `/tid/oversigt` | `OversightPage` | any auth | S61 annual dashboard — being replaced by `ArsoversigtPage` (Direction E year grid) in S65 |
| `/tid/mine-perioder` | `MyPeriods` | any auth | Own submitted periods (no nav item) |
| `/godkend/godkendelser` | `ApprovalDashboard` | LocalLeader | Manager approval; expandable `ApprovalDetailPanel` (S55) |
| `/godkend/vikariering` | `DelegationPage` | LocalLeader | Delegation / vikariering (ADR-027) |
| `/admin/medarbejdere` | `UserManagement` | LocalHR | Employee/user administration |
| `/admin/auditlog` | `AuditLogView` | LocalHR | Audit log viewer (ADR-026) |
| `/admin/projekter` | `ProjectManagement` | LocalAdmin | Project configuration per org |
| `/admin/ledelseslinjer` | `MedarbejderAdministration` | LocalAdmin | Medarbejder administration — the structural approval tree + the unified `EditPersonDrawer` create/edit/approver/vikar/delete surface (ADR-027 Phase 5, S74–S77; replaced the retired `ReportingLineTree`) |
| `/admin/brugerrettigheder` | `RoleManagement` | LocalAdmin | Role / access-rights assignment |
| `/lokal/ok-konfiguration` | `ConfigManagement` | LocalAdmin | Local agreement (OK) config overrides |
| `/lokal/stillingstilpasninger` | `PositionOverrideManagement` | LocalAdmin | Local position overrides |
| `/global/overenskomster` | `AgreementConfigList` | GlobalAdmin | Agreement config overview |
| `/global/overenskomster/new` | `AgreementConfigEditor` | GlobalAdmin | Create agreement config |
| `/global/overenskomster/:configId` | `AgreementConfigEditor` | GlobalAdmin | Edit agreement config |
| `/global/organisation` | `OrgManagement` | GlobalAdmin | Organization unit hierarchy |
| `/global/loenartstilknytning` | `WageTypeMappingManagement` | GlobalAdmin | Wage type mapping administration |
| `/global/entitlement-configs` | → redirect | GlobalAdmin | → `/global/overenskomster` |
| `/health` | `HealthDashboard` | any auth | Service health (hidden from nav) |
| `*` | `NotFoundPage` | any auth | 404 fallback |

Legacy/orphaned pages still in source but **not routed**: `AbsenceRegistration`, `TimeRegistration`, `WeeklyView` (superseded by `SkemaPage`); `EntitlementConfigEditor`, `OvertimePreApprovalManagement` (not imported by `App.tsx`).

## Hooks (`src/hooks/`)

| Hook | Description |
|------|-------------|
| `useAbsences` | Fetch and manage absence entries |
| `useAdmin` | Admin CRUD operations (orgs, users, roles) |
| `useAgreementConfigs` | Agreement config CRUD for GlobalAdmin |
| `useApprovals` | Period approval workflow (submit, approve, reject, reopen) |
| `useAuth` | Legacy auth hook (prefer AuthContext) |
| `useBalanceSummary` | Fetch employee balance summary (norm, flex, absence) |
| `useConfig` | Local configuration management |
| `useFlexBalance` | Fetch flex balance data |
| `usePositionOverrides` | Position override CRUD for GlobalAdmin |
| `useProjects` | Project management per org unit |
| `useSkema` | Monthly skema data + approval/reopen plumbing |
| `useTimeEntries` | Time entry CRUD operations |
| `useWageTypeMappings` | Wage type mapping CRUD for GlobalAdmin |
| `useCompliance` | EU-WTD compliance warnings |
| `useCompensationChoice` | Overtime compensation-model choice |
| `useEntitlementConfig` | Entitlement config (GlobalAdmin) |
| `useReportingLines` | Reporting-line hierarchy (ADR-027) |
| `useDelegation` | Acting-manager delegation / vikariering |

> Removed S56: `useTimer` (timer retired, ADR-028). 18 hook files total (excluding tests).

## State Management

### AuthContext (`src/contexts/AuthContext.tsx`)
React Context provider wrapping the entire app. Decodes JWT on login and exposes:
- `user`, `role`, `orgId`, `agreementCode` -- from JWT claims
- `isAuthenticated` -- boolean
- `login(token)` / `logout()` -- state transitions

### apiClient (`src/lib/api.ts`)
Centralized fetch wrapper. All API calls go through this client.
- Typed return: `ApiResult<T> = { ok: true; data: T } | { ok: false; error: string; status: number }`
- Automatic `Authorization: Bearer <token>` header injection
- 401 handling: clears token from localStorage, reloads page
- 204 No Content handling: returns `undefined` as data
- Methods: `get<T>`, `post<T>`, `put<T>`, `delete<T>`

### Role Utilities (`src/lib/roles.ts`)
- `ROLE_LEVELS`: Maps role names to numeric hierarchy (GlobalAdmin=1 ... Employee=5)
- `hasMinRole(userRole, minRole)`: Returns true if user's role level is at or above the minimum

## CSS Conventions
- **CSS Modules** for all component styling (`*.module.css` co-located with `.tsx`)
- Global styles in `src/styles/` (only `tokens.css` currently)
- Never hardcode colors or spacing -- always use CSS custom properties from `tokens.css`
- Class names: camelCase in CSS modules (e.g., `.formField`, `.submitButton`)
- Border radius is always `0px` -- do not override
- Import pattern: `import styles from './Component.module.css'`

## Test Infrastructure
- **Runner**: vitest (configured in `vite.config.ts`)
- **Environment**: jsdom
- **Globals**: `true` (no need to import `describe`, `it`, `expect`)
- **Setup file**: `src/test/setup.ts`
- **CSS modules**: `classNameStrategy: 'non-scoped'` so class names are stable in tests
- **Test locations**: `__tests__/` directories co-located with source
  - `src/components/__tests__/`
  - `src/components/ui/__tests__/`
  - `src/hooks/__tests__/`
  - `src/pages/__tests__/`
  - `src/pages/admin/__tests__/`
- **Test count (component tier)**: 468 tests across 37 files (`npm run test`, verified 2026-06-17 / S82)
- **`testTimeout` / `hookTimeout`**: 30000 (S82) — the grown userEvent-heavy suite (~200s) tripped the default 5s per-test ceiling under vitest's parallel pool even though tests pass <1s in isolation (the FAIL-002/SkemaPage load-contention class); the raised ceiling keeps the gated CI `frontend-build` (no retries) reliably green.

## End-to-End Tests (Playwright — S82, the A-ceiling lifter)
- **Runner**: Playwright (`@playwright/test`), config `frontend/playwright.config.ts` (chromium-only headless, `retries: 2`, trace/video on first-retry).
- **Location**: `frontend/e2e/` (kept OUT of the vitest `src/**` include so the two tiers never collide). `frontend/e2e/helpers/` holds the shared `login()` + the runtime UTC non-boundary-date + per-run-nonce helpers.
- **What it drives**: a REAL browser against the REAL app — the 7-service docker-compose stack (`:5100-5700`) + the vite **dev** server (`:3000`, whose `server.proxy` forwards `/api`→`:5100`; `vite preview` has no proxy, so the harness uses the dev server). `baseURL` is parameterized via `E2E_BASE_URL` (default `http://localhost:3000`) so the same journeys can later target a staging environment.
- **Journeys (3 critical)**: (1) login → `/tid/registrering`; (2) `emp001` Skema absence registration → persists across reload; (3) `mgr03` approves one period + rejects another (each created as a raw SUBMITTED period via the MyPeriods submit form, on a per-run-nonce unique non-boundary month so runs are re-run-tolerant + dodge the `/submit` 409 guard).
- **Run locally**: `cd frontend && npm run e2e` (with the compose stack up + the dev server running; the config's `webServer` auto-starts/reuses the dev server). 
- **CI**: a dedicated **gated** `e2e-tests` job (`.github/workflows/ci.yml`), independent of `build-and-test`/`smoke-tests` (the S63 "one red must not blind another" lesson): compose up → host `/health` wait (5100-5700) → `playwright install chromium` → `npm run e2e`; on failure it uploads the Playwright report/traces + the compose logs, and `docker compose down -v` runs `if: always()`.
- **Grade note**: this harness lifted the Frontend grade **B→A−** (S82). The remaining residual to reach **A** is a visual-regression baseline + a component-docs pass (tracked follow-ups).

## File Structure Summary
```
frontend/
  src/
    App.tsx                    # Root component, route definitions
    contexts/
      AuthContext.tsx           # Auth state provider
    components/
      ui/                      # 19 reusable UI components (13 scratch + 6 Radix)
        index.ts               # Barrel export
      layout/                  # AppLayout, Header, TopNav, Sidebar
      guards/                  # RequireAuth, RequireRole
      BalanceSummary.tsx        # Domain: balance overview
      FlexBalanceCard.tsx       # Domain: flex display
      SkemaGrid.tsx             # Domain: monthly spreadsheet (lockable)
      AllocationSummary.tsx     # Domain: work-time allocation (S56)
      ComplianceWarnings.tsx    # Domain: EU-WTD warnings
      ProjectPicker.tsx         # Domain: project selector
      TimeEntryForm.tsx         # Domain: legacy (unrouted)
      WeekGrid.tsx              # Domain: legacy (unrouted)
    hooks/                     # 18 data-fetching hooks
    lib/
      api.ts                   # apiClient fetch wrapper
      jwt.ts                   # JWT decode utility
      roles.ts                 # Role hierarchy + hasMinRole
    pages/
      SkemaPage.tsx            # Main employee page
      LoginPage.tsx            # Authentication
      HealthDashboard.tsx      # Service health
      ForbiddenPage.tsx        # 403 display
      NotFoundPage.tsx         # 404 display
      admin/                   # Admin pages (7 routed + 2 orphaned legacy)
      approval/                # Approval pages (MyPeriods, ApprovalDashboard, ApprovalDetailPanel)
      delegation/              # DelegationPage (vikariering)
      config/                  # Config pages (ConfigManagement)
    styles/
      tokens.css               # Design tokens (colors, spacing, typography)
    test/
      setup.ts                 # vitest setup
  vite.config.ts               # Vite + vitest configuration
```
