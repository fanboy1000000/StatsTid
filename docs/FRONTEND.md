# StatsTid Frontend Reference
> Design system, component patterns, routing, and test infrastructure for UX Agent context.

## Technology Stack
- React 18 + TypeScript
- Vite (dev port 3000, proxy `/api` to `localhost:5100`)
- react-router-dom v6 (BrowserRouter, declarative Routes)
- CSS Modules + CSS custom properties (`tokens.css`)
- vitest + @testing-library/react (jsdom environment)

## Design System (ADR-011)
- Inspired by designsystem.dk (Danish government design system)
- Font: IBM Plex Sans
- Primary color: `#0059B3`, dark variants `#004993` / `#003972`
- Border radius: `0px` (sharp corners -- Danish gov style)
- Borders: `1px solid`
- Spacing: 8px baseline grid (`--space-1` through `--space-12`)
- Shadows: minimal (`--shadow-sm: 0 1px 2px rgba(0,0,0,0.05)`)
- Focus ring: `2px solid` gray-600 with `2px` offset (accessibility)

## Design Tokens (`src/styles/tokens.css`)

### Colors
| Token | Value | Usage |
|-------|-------|-------|
| `--color-primary` | `#0059B3` | Primary actions, links |
| `--color-primary-dark` | `#004993` | Hover states |
| `--color-primary-darker` | `#003972` | Active states |
| `--color-text` | `#1A1A1A` | Body text |
| `--color-gray-100` to `--color-gray-600` | Gray scale | Backgrounds, borders, muted text |
| `--color-white` | `#FFFFFF` | Card backgrounds |
| `--color-success` / `--color-success-light` | `#358000` / `#DDF7CE` | Success states |
| `--color-error` / `--color-error-light` | `#CC0000` / `#FFE0E0` | Error states |
| `--color-warning` / `--color-warning-light` | `#FEBB30` / `#FFEECC` | Warning states |
| `--color-info` / `--color-info-light` | `#1B86C3` / `#E2F2FB` | Informational states |
| `--color-link` | `#004D99` | Hyperlinks |
| `--color-link-visited` | `#800080` | Visited links |

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
| SkemaGrid | `SkemaGrid.tsx` | Monthly spreadsheet grid (days x projects/absence rows) |
| TimeEntryForm | `TimeEntryForm.tsx` | Time entry input form |
| TimerControl | `TimerControl.tsx` | Check-in / check-out timer (Tjek ind/Tjek ud) |
| WeekGrid | `WeekGrid.tsx` | Weekly time overview grid |

### Layout Components
| Component | File | Description |
|-----------|------|-------------|
| AppLayout | `layout/AppLayout.tsx` | Main layout: Sidebar + Header + Outlet |
| Header | `layout/Header.tsx` | Top bar with user info |
| Sidebar | `layout/Sidebar.tsx` | Left nav with role-based menu items |

### Route Guards
| Component | File | Description |
|-----------|------|-------------|
| RequireAuth | `guards/RequireAuth.tsx` | Redirects to `/login` if unauthenticated |
| RequireRole | `guards/RequireRole.tsx` | Checks `hasMinRole`, renders ForbiddenPage if insufficient |

## Layout System
- `AppLayout` wraps all authenticated routes with Sidebar + Header + `<Outlet />`
- `Sidebar` renders navigation items conditionally based on user role:
  - **Employee**: Min Tid (skema), Min balance
  - **LocalLeader+**: Godkendelser (approval dashboard)
  - **LocalAdmin+**: Organisation, Roller, Projekter, Konfiguration
  - **GlobalAdmin**: Overenskomster, Positioner, Lontypekortlaegning
- Guards compose via nested `<Route element={...}>` wrappers

## Pages & Routes

| Route | Page Component | Min Role | Description |
|-------|---------------|----------|-------------|
| `/` (index) | `SkemaPage` | Employee | Monthly time registration spreadsheet |
| `/login` | `LoginPage` | Public | Login form |
| `/health` | `HealthDashboard` | Employee | Service health status |
| `/approval/mine` | `MyPeriods` | Employee | Employee's own period submissions |
| `/approval` | `ApprovalDashboard` | LocalLeader | Manager approval of submitted periods |
| `/admin/users` | `UserManagement` | LocalHR | User CRUD |
| `/admin/orgs` | `OrgManagement` | LocalAdmin | Organization unit hierarchy |
| `/admin/roles` | `RoleManagement` | LocalAdmin | Role assignment management |
| `/admin/projects` | `ProjectManagement` | LocalAdmin | Project configuration per org |
| `/config` | `ConfigManagement` | LocalAdmin | Local agreement config overrides |
| `/admin/agreements` | `AgreementConfigList` | GlobalAdmin | Agreement config overview with filters |
| `/admin/agreements/:configId` | `AgreementConfigEditor` | GlobalAdmin | Agreement config form editor |
| `/admin/position-overrides` | `PositionOverrideManagement` | GlobalAdmin | Position override config management |
| `/admin/wage-type-mappings` | `WageTypeMappingManagement` | GlobalAdmin | Wage type mapping administration |
| `*` | `NotFoundPage` | Employee | 404 fallback |

Legacy pages still present in source but no longer routed: `AbsenceRegistration`, `TimeRegistration`, `WeeklyView` (superseded by SkemaPage in Sprint 9).

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
| `useSkema` | Monthly skema data (time entries by day/project) |
| `useTimeEntries` | Time entry CRUD operations |
| `useTimer` | Timer check-in/check-out state management |
| `useWageTypeMappings` | Wage type mapping CRUD for GlobalAdmin |

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
- **Approx. test count**: ~38 frontend tests

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
      layout/                  # AppLayout, Header, Sidebar
      guards/                  # RequireAuth, RequireRole
      BalanceSummary.tsx        # Domain: balance overview
      FlexBalanceCard.tsx       # Domain: flex display
      SkemaGrid.tsx             # Domain: monthly spreadsheet
      TimeEntryForm.tsx         # Domain: time entry
      TimerControl.tsx          # Domain: check-in/out
      WeekGrid.tsx              # Domain: weekly grid
    hooks/                     # 14 data-fetching hooks
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
      admin/                   # Admin pages (7 pages)
      approval/                # Approval pages (2 pages)
      config/                  # Config pages (1 page)
    styles/
      tokens.css               # Design tokens (colors, spacing, typography)
    test/
      setup.ts                 # vitest setup
  vite.config.ts               # Vite + vitest configuration
```
