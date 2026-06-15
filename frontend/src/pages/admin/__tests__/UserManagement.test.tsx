// S76b / TASK-7604 — UserManagement reduced to the org-user LIST entry point.
//
// The create + edit DIALOGS were RETIRED into the unified EditPersonDrawer (the
// single create/edit-everything person surface). These tests assert: the list +
// org-selector + the Deltid column still render; "Opret bruger" opens the drawer
// in CREATE mode; a row-click opens it in EDIT mode for that User; and the OLD
// dialog markers (the in-dialog stale-conflict banner / DOB / child-sick toggle /
// the in-dialog reporting-line "Skift leder") are GONE (they live in the drawer).
//
// The deep per-dialog save-contract tests (412 banner, S59 DOB / CHILD_SICK
// If-None-Match-vs-If-Match, S60 employment-start) moved to EditPersonDrawer.test.tsx
// — the drawer owns the single save path now.
import { describe, it, expect, vi, beforeEach } from 'vitest'
import { render, screen, waitFor, fireEvent } from '@testing-library/react'
import { ToastProvider } from '../../../components/ui/Toast'
import { UserManagement } from '../UserManagement'

// Role-gating mock for the EditPersonDrawer (LocalHR → HR-capable).
let mockRole = 'LocalHR'
vi.mock('../../../contexts/AuthContext', () => ({
  useAuth: () => ({
    token: 'test-token',
    user: { employeeId: 'ADMIN1', role: mockRole },
    role: mockRole,
    orgId: 'ORG1',
    agreementCode: 'AC',
    scopes: [],
    isAuthenticated: true,
    login: vi.fn(),
    logout: vi.fn(),
  }),
}))

const mockFetch = vi.fn()
vi.stubGlobal('fetch', mockFetch)

const mockStorage: Record<string, string> = { statstid_token: 'test-token' }
vi.stubGlobal('localStorage', {
  getItem: (key: string) => mockStorage[key] ?? null,
  setItem: (key: string, val: string) => { mockStorage[key] = val },
  removeItem: (key: string) => { delete mockStorage[key] },
})

const mockOrg = {
  orgId: 'ORG1',
  orgName: 'Test Organization',
  orgType: 'DEPARTMENT',
  parentOrgId: null,
  materializedPath: '/ORG1',
  agreementCode: 'AC',
}

const mockUser = {
  userId: 'EMP001',
  username: 'emp001',
  displayName: 'Test Bruger',
  email: 'test@example.dk',
  primaryOrgId: 'ORG1',
  agreementCode: 'AC',
  version: 1,
}

const mockProfile = {
  employeeId: 'EMP001',
  weeklyNormHours: 37,
  partTimeFraction: 0.8,
  position: 'Fuldmægtig',
  enhedLabel: 'Netværk',
  isPartTime: true,
  version: 1,
}

beforeEach(() => {
  mockFetch.mockReset()
  mockRole = 'LocalHR'
})

/**
 * URL-based mock router. Serves the org list, the org-users list, the
 * per-employee profile (the Deltid column + the drawer's hydrate), and benign
 * defaults for every other GET the drawer fires on edit-open.
 */
function setupFetchRouter(overrides: Record<string, () => Promise<Response>> = {}) {
  mockFetch.mockImplementation(async (url: string, _init?: RequestInit) => {
    for (const [pattern, factory] of Object.entries(overrides)) {
      if (url.includes(pattern)) return factory()
    }
    if (url.includes('/api/admin/organizations') && !url.includes('/users')) {
      return { ok: true, status: 200, headers: new Headers(), json: async () => [mockOrg] } as unknown as Response
    }
    if (url.includes('/api/admin/organizations/') && url.includes('/users')) {
      return { ok: true, status: 200, headers: new Headers(), json: async () => [mockUser] } as unknown as Response
    }
    if (url.includes('/api/admin/employee-profiles/')) {
      return { ok: true, status: 200, headers: new Headers({ ETag: '"1"' }), json: async () => mockProfile } as unknown as Response
    }
    if (url.includes('/birth-date')) {
      return { ok: true, status: 200, headers: new Headers({ ETag: '"1"' }), json: async () => ({ employeeId: 'EMP001', birthDate: null, version: 1 }) } as unknown as Response
    }
    if (url.includes('/employment-start-date')) {
      return { ok: true, status: 200, headers: new Headers({ ETag: '"1"' }), json: async () => ({ employeeId: 'EMP001', employmentStartDate: null, version: 1 }) } as unknown as Response
    }
    if (url.includes('/entitlement-eligibility/')) {
      return { ok: true, status: 200, headers: new Headers(), json: async () => ({ employeeId: 'EMP001', entitlementType: 'CHILD_SICK', eligible: false, rowExists: false }) } as unknown as Response
    }
    if (url.includes('/api/admin/reporting-lines/') && url.includes('/reports')) {
      // fetchDirectReports → an ARRAY of DirectReport.
      return { ok: true, status: 200, headers: new Headers(), json: async () => [] } as unknown as Response
    }
    if (url.includes('/api/admin/reporting-lines/')) {
      // fetchEmployeeLines → { active, history } for the drawer's lifecycle sections.
      return { ok: true, status: 200, headers: new Headers(), json: async () => ({ active: [], history: [] }) } as unknown as Response
    }
    if (url.includes('/api/admin/users/')) {
      return { ok: true, status: 200, headers: new Headers({ ETag: '"1"' }), json: async () => mockUser } as unknown as Response
    }
    return { ok: false, status: 404, headers: new Headers(), text: async () => 'Not Found' } as unknown as Response
  })
}

function renderPage() {
  return render(<ToastProvider><UserManagement /></ToastProvider>)
}

describe('UserManagement — list entry point (dialogs retired)', () => {
  it('renders the org selector + the user list with the Deltid column', async () => {
    setupFetchRouter()
    renderPage()

    await waitFor(() => {
      expect(screen.getByText('Test Bruger')).toBeDefined()
    })
    // Org selector present.
    expect(screen.getByLabelText('Organisation')).toBeDefined()
    // The Deltid column renders the part-time fraction from the profile map (0.8).
    await waitFor(() => {
      expect(screen.getByText('0.80')).toBeDefined()
    })
    // "Opret bruger" entry present.
    expect(screen.getByTestId('um-create')).toBeDefined()
  })

  it('the OLD create/edit DIALOGS are gone — only the drawer remains', async () => {
    setupFetchRouter()
    renderPage()
    await waitFor(() => {
      expect(screen.getByText('Test Bruger')).toBeDefined()
    })
    // None of the retired in-dialog markers exist on initial render.
    expect(screen.queryByTestId('stale-conflict-banner')).toBeNull()
    expect(screen.queryByTestId('birth-date-input')).toBeNull()
    expect(screen.queryByTestId('child-sick-toggle')).toBeNull()
    expect(screen.queryByTestId('employment-start-input')).toBeNull()
    // The in-dialog reporting-line section ("Skift leder"/"Ledelseslinjer") is gone.
    expect(screen.queryByText('Skift leder')).toBeNull()
    expect(screen.queryByText('Ledelseslinjer')).toBeNull()
  })

  it('"Opret bruger" opens the unified EditPersonDrawer in CREATE mode', async () => {
    setupFetchRouter()
    renderPage()
    await waitFor(() => {
      expect(screen.getByTestId('um-create')).toBeDefined()
    })
    fireEvent.click(screen.getByTestId('um-create'))
    await waitFor(() => {
      expect(screen.getByTestId('ep-title').textContent).toBe('Opret medarbejder')
    })
    // Create-mode credentials block (the drawer's, not the old dialog's #newUserId).
    expect(screen.getByTestId('ep-create-user-id')).toBeDefined()
  })

  it('a row-click opens the drawer in EDIT mode for that User', async () => {
    setupFetchRouter()
    renderPage()
    await waitFor(() => {
      expect(screen.getByText('Test Bruger')).toBeDefined()
    })
    fireEvent.click(screen.getByText('Test Bruger'))
    // Edit mode: the drawer title names the user; the create credentials are absent.
    await waitFor(() => {
      expect(screen.getByTestId('ep-title').textContent).toMatch(/Redigér Test Bruger/)
    })
    expect(screen.queryByTestId('ep-create-user-id')).toBeNull()
    // The drawer's stamdata hydrates from the supplied row (no extra user GET needed).
    await waitFor(() => {
      expect((screen.getByTestId('ep-display-name') as HTMLInputElement).value).toBe('Test Bruger')
    })
  })

  it('shows the empty state when the org has no users', async () => {
    setupFetchRouter({
      '/organizations/ORG1/users': async () =>
        ({ ok: true, status: 200, headers: new Headers(), json: async () => [] }) as unknown as Response,
    })
    renderPage()
    await waitFor(() => {
      expect(screen.getByText('Ingen brugere fundet for denne organisation')).toBeDefined()
    })
  })
})
