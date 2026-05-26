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
  mockFetch.mockImplementation(async (url: string, init?: RequestInit) => {
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
    if (url.includes('/api/admin/users/') && (!init || init.method !== 'PUT')) {
      return { ok: true, status: 200, headers: new Headers({ ETag: '"1"' }), json: async () => mockUser }
    }
    if (url.includes('/api/admin/employee-profiles/')) {
      return { ok: true, status: 200, headers: new Headers({ ETag: '"1"' }), json: async () => mockProfile }
    }
    if (url.includes('/api/reporting-lines/')) {
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
