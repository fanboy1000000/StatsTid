// S35 TASK-3507 banner-with-retry tests for UserManagement.
//
// Migration of the admin user-edit flow to `apiFetchWithEtag<T>` per the
// S25 admin-strict If-Match contract (ADR-019 D2). The PUT
// `/api/admin/users/{userId}` carries `If-Match: "<version>"` and the page
// renders a stale-conflict banner on 412 with a "Genindlaes" refetch button.
//
// S53 TASK-5306e: updated to account for employee-profile fetch calls added
// when the EmployeeProfileEditor page was removed and its fields were inlined
// into the UserManagement edit dialog. Uses URL-based mock routing instead of
// sequential `mockResolvedValueOnce` to handle parallel fetches deterministically.
import { describe, it, expect, vi, beforeEach } from 'vitest'
import { render, screen, waitFor, fireEvent } from '@testing-library/react'
import { ToastProvider } from '../../../components/ui/Toast'
import { UserManagement } from '../UserManagement'

const mockFetch = vi.fn()
vi.stubGlobal('fetch', mockFetch)

const mockStorage: Record<string, string> = {}
vi.stubGlobal('localStorage', {
  getItem: (key: string) => mockStorage[key] ?? null,
  setItem: (key: string, val: string) => { mockStorage[key] = val },
  removeItem: (key: string) => { delete mockStorage[key] },
})

const mockReload = vi.fn()
Object.defineProperty(window, 'location', {
  value: { reload: mockReload },
  writable: true,
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
  partTimeFraction: 1.0,
  position: null,
  isPartTime: false,
  version: 1,
}

beforeEach(() => {
  mockFetch.mockReset()
  mockReload.mockReset()
  Object.keys(mockStorage).forEach((k) => delete mockStorage[k])
})

/**
 * URL-based mock router. Deterministically handles the fetch calls that
 * UserManagement makes regardless of parallel-vs-sequential ordering.
 * Individual tests can override behaviour for specific URLs by passing
 * `overrides` — a map of URL-substring to response factory.
 */
function setupFetchRouter(overrides: Record<string, () => Promise<Response>> = {}) {
  mockFetch.mockImplementation(async (url: string, _init?: RequestInit) => {
    // Check overrides first.
    for (const [pattern, factory] of Object.entries(overrides)) {
      if (url.includes(pattern)) return factory()
    }

    if (url.includes('/api/admin/organizations') && !url.includes('/users')) {
      return { ok: true, status: 200, headers: new Headers(), json: async () => [mockOrg] }
    }
    if (url.includes('/api/admin/organizations/') && url.includes('/users')) {
      return { ok: true, status: 200, headers: new Headers(), json: async () => [mockUser] }
    }
    if (url.includes('/api/admin/users/')) {
      // GET and PUT both succeed with version 1 (PUT returns the same shape so
      // the user-save step in handleEditSubmit completes and the S59 DOB /
      // eligibility saves run afterwards).
      return { ok: true, status: 200, headers: new Headers({ ETag: '"1"' }), json: async () => mockUser }
    }
    if (url.includes('/api/admin/employee-profiles/')) {
      return { ok: true, status: 200, headers: new Headers({ ETag: '"1"' }), json: async () => mockProfile }
    }
    // S59 TASK-5908. HR-only DOB read defaults to no DOB, users.version 1.
    if (url.includes('/birth-date')) {
      return {
        ok: true, status: 200,
        headers: new Headers({ ETag: '"1"' }),
        json: async () => ({ employeeId: 'EMP001', birthDate: null, version: 1 }),
      }
    }
    // S59 follow-up. HR-only CHILD_SICK eligibility GET (read-then-If-Match).
    // Default: NO live row → rowExists:false, eligible:false, NO ETag. A toggle
    // therefore CREATES with If-None-Match: *. Tests that exercise the update
    // path override this to return rowExists:true + a version/ETag.
    if (url.includes('/entitlement-eligibility/')) {
      return {
        ok: true, status: 200,
        headers: new Headers(),
        json: async () => ({
          employeeId: 'EMP001',
          entitlementType: 'CHILD_SICK',
          eligible: false,
          rowExists: false,
        }),
      }
    }
    // Reporting-lines GET fired by handleRowClick (fetchEmployeeLines ->
    // `/api/admin/reporting-lines/{employeeId}`). Must RESOLVE OK so the
    // trailing setActiveLines/setLinesLoading(false) state updates settle
    // inside the act()-wrapped flow instead of resolving (as a 404) after the
    // test returns — the previous `/api/reporting-lines/` substring did not
    // match the real `/api/admin/reporting-lines/` URL and fell through to 404.
    if (url.includes('/api/admin/reporting-lines/') || url.includes('/api/reporting-lines/')) {
      return { ok: true, status: 200, headers: new Headers(), json: async () => ({ active: [], pending: [] }) }
    }
    // Fallback — 404.
    return { ok: false, status: 404, headers: new Headers(), text: async () => 'Not Found' }
  })
}

describe('UserManagement — 412 banner-with-retry', () => {
  it('shows the stale-conflict banner on 412 with expected/actual version pair', async () => {
    let putCalled = false
    setupFetchRouter({
      // Override PUT to /api/admin/users/ -> 412.
      '/api/admin/users/': async () => {
        // Only the PUT should return 412; GETs use the default router.
        // We detect PUT by checking if this is the second call to this URL
        // (first call is the GET on row click).
        if (putCalled) {
          // Already called once for PUT; subsequent calls are GETs.
          return { ok: true, status: 200, headers: new Headers({ ETag: '"1"' }), json: async () => mockUser } as unknown as Response
        }
        putCalled = true
        // But we need to distinguish GET vs PUT. Use a stateful approach.
        return { ok: true, status: 200, headers: new Headers({ ETag: '"1"' }), json: async () => mockUser } as unknown as Response
      },
    })

    // Re-setup with a cleaner approach: track calls by method.
    mockFetch.mockReset()
    mockFetch.mockImplementation(async (url: string, init?: RequestInit) => {
      const method = init?.method ?? 'GET'

      if (url.includes('/api/admin/organizations') && !url.includes('/users')) {
        return { ok: true, status: 200, headers: new Headers(), json: async () => [mockOrg] }
      }
      if (url.includes('/api/admin/organizations/') && url.includes('/users')) {
        return { ok: true, status: 200, headers: new Headers(), json: async () => [mockUser] }
      }
      if (url.includes('/api/admin/users/') && method === 'GET') {
        return { ok: true, status: 200, headers: new Headers({ ETag: '"1"' }), json: async () => mockUser }
      }
      if (url.includes('/api/admin/users/') && method === 'PUT') {
        const stalePayload = {
          error: 'Concurrency precondition failed',
          expectedVersion: 1,
          actualVersion: 5,
        }
        return { ok: false, status: 412, headers: new Headers(), text: async () => JSON.stringify(stalePayload) }
      }
      if (url.includes('/api/admin/employee-profiles/')) {
        return { ok: true, status: 200, headers: new Headers({ ETag: '"1"' }), json: async () => mockProfile }
      }
      if (url.includes('/api/reporting-lines/') || url.includes('/api/admin/reporting-lines')) {
        return { ok: true, status: 200, headers: new Headers(), json: async () => ({ active: [], pending: [] }) }
      }
      return { ok: false, status: 404, headers: new Headers(), text: async () => 'Not Found' }
    })

    render(<ToastProvider><UserManagement /></ToastProvider>)

    // Wait for the user row to render in the table.
    await waitFor(() => {
      expect(screen.getByText('Test Bruger')).toBeDefined()
    })

    // Click the row to open the edit dialog (triggers per-user GET + profile GET).
    fireEvent.click(screen.getByText('Test Bruger'))

    // Wait for the dialog to render with the user-specific title.
    await waitFor(() => {
      expect(screen.getByText(/Rediger bruger/i)).toBeDefined()
    })

    // Submit the form -> triggers the PUT which returns 412.
    fireEvent.click(screen.getByText('Gem'))

    // Banner shows up with the expected/actual pair.
    await waitFor(() => {
      const banner = screen.getByTestId('stale-conflict-banner')
      expect(banner).toBeDefined()
      expect(banner.textContent).toContain('Forventet version 1')
      expect(banner.textContent).toContain('aktuel version 5')
    })
  })

  it('Genindlaes button refetches the user and clears the banner', async () => {
    let putDone = false
    mockFetch.mockImplementation(async (url: string, init?: RequestInit) => {
      const method = init?.method ?? 'GET'

      if (url.includes('/api/admin/organizations') && !url.includes('/users')) {
        return { ok: true, status: 200, headers: new Headers(), json: async () => [mockOrg] }
      }
      if (url.includes('/api/admin/organizations/') && url.includes('/users')) {
        return { ok: true, status: 200, headers: new Headers(), json: async () => [mockUser] }
      }
      if (url.includes('/api/admin/users/') && method === 'GET') {
        // After the PUT-412 and Genindlaes, the refetch GET returns version 5.
        const version = putDone ? 5 : 1
        const etag = `"${version}"`
        return {
          ok: true, status: 200,
          headers: new Headers({ ETag: etag }),
          json: async () => ({ ...mockUser, version }),
        }
      }
      if (url.includes('/api/admin/users/') && method === 'PUT') {
        putDone = true
        return {
          ok: false, status: 412, headers: new Headers(),
          text: async () => JSON.stringify({
            error: 'Concurrency precondition failed',
            expectedVersion: 1,
            actualVersion: 5,
          }),
        }
      }
      if (url.includes('/api/admin/employee-profiles/')) {
        return { ok: true, status: 200, headers: new Headers({ ETag: '"1"' }), json: async () => mockProfile }
      }
      if (url.includes('/api/reporting-lines/') || url.includes('/api/admin/reporting-lines')) {
        return { ok: true, status: 200, headers: new Headers(), json: async () => ({ active: [], pending: [] }) }
      }
      return { ok: false, status: 404, headers: new Headers(), text: async () => 'Not Found' }
    })

    render(<ToastProvider><UserManagement /></ToastProvider>)

    await waitFor(() => {
      expect(screen.getByText('Test Bruger')).toBeDefined()
    })

    fireEvent.click(screen.getByText('Test Bruger'))

    await waitFor(() => {
      expect(screen.getByText(/Rediger bruger/i)).toBeDefined()
    })

    fireEvent.click(screen.getByText('Gem'))

    await waitFor(() => {
      expect(screen.getByTestId('stale-conflict-banner')).toBeDefined()
    })

    // Click Genindlaes — refetch fires, banner clears.
    fireEvent.click(screen.getByText(/Genindlaes/i))

    await waitFor(() => {
      expect(screen.queryByTestId('stale-conflict-banner')).toBeNull()
    })
  })
})

// S59 TASK-5908 (ADR-029). HR-only per-employee entitlement controls inlined on
// the user-edit dialog: (A) date of birth (drives the age-derived SENIOR_DAY
// gate) and (B) the CHILD_SICK eligibility opt-in toggle. CHILD_SICK is the
// ONLY settable type — there is no senior toggle.
describe('UserManagement — S59 entitlement eligibility + DOB', () => {
  async function openEditDialog() {
    render(<ToastProvider><UserManagement /></ToastProvider>)
    await waitFor(() => {
      expect(screen.getByText('Test Bruger')).toBeDefined()
    })
    fireEvent.click(screen.getByText('Test Bruger'))
    await waitFor(() => {
      expect(screen.getByText(/Rediger bruger/i)).toBeDefined()
    })
    // handleRowClick continues AFTER opening the dialog: it sets linesLoading,
    // awaits fetchEmployeeLines, then setActiveLines/setLinesLoading(false).
    // Wait for that final reporting-lines load to settle (the empty-state text
    // replaces the "Indlaeser..." spinner) so no async state update dangles
    // past the end of a test that only opens the dialog and asserts synchronously.
    await waitFor(() => {
      expect(screen.getByText(/Ingen ledelseslinjer registreret/i)).toBeDefined()
    })
  }

  it('pre-populates DOB from the HR-only birth-date GET and offers only the child-sick toggle (no senior toggle)', async () => {
    setupFetchRouter({
      '/birth-date': async () =>
        ({
          ok: true, status: 200,
          headers: new Headers({ ETag: '"3"' }),
          json: async () => ({ employeeId: 'EMP001', birthDate: '1990-04-15', version: 3 }),
        }) as unknown as Response,
    })

    await openEditDialog()

    // DOB field is populated from the GET.
    await waitFor(() => {
      const dob = screen.getByTestId('birth-date-input') as HTMLInputElement
      expect(dob.value).toBe('1990-04-15')
    })

    // The child-sick toggle exists; there is NO senior toggle anywhere.
    expect(screen.getByTestId('child-sick-toggle')).toBeDefined()
    expect(screen.getByText(/Barns sygedag/i)).toBeDefined()
    expect(screen.queryByText(/Seniordag.*berettiget/i)).toBeNull()
    // Senior helper text explains the automatic age-based grant.
    expect(screen.getByText(/fyldte 62\. år/i)).toBeDefined()
  })

  it('toggles child-sick eligibility when NO live row exists: read returns rowExists:false → write uses If-None-Match: *', async () => {
    const eligibilityCalls: Array<{ method: string; headers: Record<string, string>; body: unknown }> = []

    // GET (dialog-open) returns rowExists:false / NO ETag (default router shape);
    // the PUT (create) returns the freshly-created row with a version.
    setupFetchRouter({
      '/entitlement-eligibility/': async () =>
        ({
          ok: true, status: 200,
          headers: new Headers(),
          json: async () => ({
            employeeId: 'EMP001',
            entitlementType: 'CHILD_SICK',
            eligible: false,
            rowExists: false,
          }),
        }) as unknown as Response,
    })

    // Capture eligibility calls; serve the PUT (create) response separately.
    const inner = mockFetch.getMockImplementation()!
    mockFetch.mockImplementation(async (url: string, init?: RequestInit) => {
      const method = init?.method ?? 'GET'
      if (url.includes('/entitlement-eligibility/')) {
        const headers: Record<string, string> = {}
        const h = init?.headers as Record<string, string> | undefined
        if (h) for (const [k, v] of Object.entries(h)) headers[k] = v
        eligibilityCalls.push({ method, headers, body: init?.body ? JSON.parse(init.body as string) : undefined })
        if (method === 'PUT') {
          return {
            ok: true, status: 200,
            headers: new Headers({ ETag: '"1"' }),
            json: async () => ({
              employeeId: 'EMP001', entitlementType: 'CHILD_SICK',
              eligible: true, effectiveFrom: '2026-06-01', version: 1,
            }),
          } as unknown as Response
        }
      }
      return inner(url, init)
    })

    await openEditDialog()

    // Toggle child-sick ON, then save.
    fireEvent.click(screen.getByTestId('child-sick-toggle'))
    fireEvent.click(screen.getByText('Gem'))

    // Wait for the WHOLE save flow to settle (dialog closes on success), not just
    // for the eligibility PUT to be *dispatched*. `eligibilityCalls` increments
    // the instant fetch is invoked (inside the mock wrapper), i.e. BEFORE the PUT
    // promise resolves and BEFORE handleEditSubmit's trailing setState/toast/close
    // run. Asserting only on the call count let the test return while those React
    // state updates were still pending — a dangling async update that surfaced as
    // a flaky act() warning. Asserting on the dialog closing (the terminal state
    // of a successful save) guarantees every awaited promise has resolved.
    await waitFor(() => {
      expect(screen.queryByText(/Rediger bruger/i)).toBeNull()
    })

    const puts = eligibilityCalls.filter((c) => c.method === 'PUT')
    expect(puts.length).toBe(1)
    // No live row read → create with If-None-Match: * (no If-Match), eligible true.
    expect(puts[0].headers['If-None-Match']).toBe('*')
    expect(puts[0].headers['If-Match']).toBeUndefined()
    expect(puts[0].body).toEqual({ eligible: true })
  })

  it('toggles child-sick eligibility when a live row EXISTS: read returns a version → write uses If-Match', async () => {
    const eligibilityCalls: Array<{ method: string; headers: Record<string, string>; body: unknown }> = []

    // GET (dialog-open) returns rowExists:true, eligible:true, version 4 + ETag —
    // so the toggle pre-populates checked and the write must use If-Match: "4".
    setupFetchRouter({
      '/entitlement-eligibility/': async () =>
        ({
          ok: true, status: 200,
          headers: new Headers({ ETag: '"4"' }),
          json: async () => ({
            employeeId: 'EMP001', entitlementType: 'CHILD_SICK',
            eligible: true, effectiveFrom: '2026-05-01', rowExists: true, version: 4,
          }),
        }) as unknown as Response,
    })

    const inner = mockFetch.getMockImplementation()!
    mockFetch.mockImplementation(async (url: string, init?: RequestInit) => {
      const method = init?.method ?? 'GET'
      if (url.includes('/entitlement-eligibility/')) {
        const headers: Record<string, string> = {}
        const h = init?.headers as Record<string, string> | undefined
        if (h) for (const [k, v] of Object.entries(h)) headers[k] = v
        eligibilityCalls.push({ method, headers, body: init?.body ? JSON.parse(init.body as string) : undefined })
        if (method === 'PUT') {
          return {
            ok: true, status: 200,
            headers: new Headers({ ETag: '"5"' }),
            json: async () => ({
              employeeId: 'EMP001', entitlementType: 'CHILD_SICK',
              eligible: false, effectiveFrom: '2026-06-01', version: 5,
            }),
          } as unknown as Response
        }
      }
      return inner(url, init)
    })

    await openEditDialog()

    // The toggle pre-populated from the live row (eligible:true → checked).
    await waitFor(() => {
      const toggle = screen.getByTestId('child-sick-toggle') as HTMLInputElement
      expect(toggle.checked).toBe(true)
    })

    // Toggle OFF, then save.
    fireEvent.click(screen.getByTestId('child-sick-toggle'))
    fireEvent.click(screen.getByText('Gem'))

    await waitFor(() => {
      expect(screen.queryByText(/Rediger bruger/i)).toBeNull()
    })

    const puts = eligibilityCalls.filter((c) => c.method === 'PUT')
    expect(puts.length).toBe(1)
    // Live row read → update with If-Match: "4" (the GET version); no If-None-Match.
    expect(puts[0].headers['If-Match']).toBe('"4"')
    expect(puts[0].headers['If-None-Match']).toBeUndefined()
    expect(puts[0].body).toEqual({ eligible: false })
  })

  it('saves an edited DOB with admin-strict If-Match composed from users.version', async () => {
    const dobPuts: Array<{ headers: Record<string, string>; body: unknown }> = []

    setupFetchRouter({
      '/birth-date': async () =>
        ({
          ok: true, status: 200,
          headers: new Headers({ ETag: '"7"' }),
          json: async () => ({ employeeId: 'EMP001', birthDate: '1960-01-01', version: 7 }),
        }) as unknown as Response,
    })

    const inner = mockFetch.getMockImplementation()!
    mockFetch.mockImplementation(async (url: string, init?: RequestInit) => {
      if (url.includes('/birth-date') && init?.method === 'PUT') {
        const headers: Record<string, string> = {}
        const h = init.headers as Record<string, string> | undefined
        if (h) for (const [k, v] of Object.entries(h)) headers[k] = v
        dobPuts.push({ headers, body: init.body ? JSON.parse(init.body as string) : undefined })
        return {
          ok: true, status: 200,
          headers: new Headers({ ETag: '"8"' }),
          json: async () => ({ employeeId: 'EMP001', birthDate: '1962-02-02', version: 8 }),
        } as unknown as Response
      }
      return inner(url, init)
    })

    await openEditDialog()

    await waitFor(() => {
      const dob = screen.getByTestId('birth-date-input') as HTMLInputElement
      expect(dob.value).toBe('1960-01-01')
    })

    // Edit the DOB and save.
    fireEvent.change(screen.getByTestId('birth-date-input'), { target: { value: '1962-02-02' } })
    fireEvent.click(screen.getByText('Gem'))

    // Wait for the full save flow to settle (dialog closes on success) rather
    // than just for the DOB PUT to be dispatched. `dobPuts` increments inside
    // the mock wrapper before the PUT resolves and before the trailing
    // setBirthDate*/toast/close state updates run; asserting only on the count
    // let those updates dangle past the end of the test (flaky act() warning
    // mis-attributed to a later test). See the child-sick test for the rationale.
    await waitFor(() => {
      expect(screen.queryByText(/Rediger bruger/i)).toBeNull()
    })
    expect(dobPuts.length).toBe(1)
    // Admin-strict If-Match from the GET version (7); no If-None-Match.
    expect(dobPuts[0].headers['If-Match']).toBe('"7"')
    expect(dobPuts[0].headers['If-None-Match']).toBeUndefined()
    expect(dobPuts[0].body).toEqual({ birthDate: '1962-02-02' })
  })
})
