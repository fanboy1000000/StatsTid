// S76b / TASK-7602 — Vitest + @testing-library/react tests for the unified
// EditPersonDrawer SHELL + the STAMDATA re-house. Mocks `useAuth` (role-gating)
// + a URL/method-based `fetch` router (so the REAL useEditPerson save
// orchestration + the entitlement/profile helpers exercise their independent
// preconditions). Asserts:
//   • create flow → POST /api/admin/users
//   • edit flow → each PUT's distinct contract incl. CHILD_SICK's
//     If-None-Match-create vs If-Match-update + the 409 re-read path
//   • the 412 staleConflict banner + "Genindlæs"
//   • per-section partial-failure honesty (a later PUT fails → earlier sections
//     committed, the failed one shows its error)
//   • role-gating (a non-HR LocalAdmin sees no HR sections; an HR actor does)
import type { ComponentProps } from 'react'
import { describe, it, expect, vi, beforeEach } from 'vitest'
import { render, screen, waitFor, fireEvent } from '@testing-library/react'
import { ToastProvider } from '../../../components/ui/Toast'
import { EditPersonDrawer } from '../EditPersonDrawer'
import type { Organization, User } from '../../../hooks/useAdmin'

// --- Role-gating mock (settable per test) ---
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
  setItem: (key: string, val: string) => {
    mockStorage[key] = val
  },
  removeItem: (key: string) => {
    delete mockStorage[key]
  },
})

const organizations: Organization[] = [
  // WARNING fix: the org carries `okVersion` so the create flow can derive the
  // required `CreateUserRequest.OkVersion` and the test can assert the POST body
  // threads it (the backend 400s without it).
  { orgId: 'ORG1', orgName: 'Test Org', orgType: 'DEPARTMENT', parentOrgId: null, agreementCode: 'AC', okVersion: 'OK24' },
]

const editUser: User = {
  userId: 'EMP001',
  username: 'emp001',
  displayName: 'Test Bruger',
  email: 'test@example.dk',
  primaryOrgId: 'ORG1',
  agreementCode: 'AC',
  version: 1,
}

const profileBody = {
  employeeId: 'EMP001',
  weeklyNormHours: 37,
  partTimeFraction: 1.0,
  position: 'Fuldmægtig',
  enhedLabel: 'Netværk',
  isPartTime: false,
  version: 1,
}

interface RouterOverrides {
  [pattern: string]: (init?: RequestInit) => Promise<Response> | Response
}

function ok(json: unknown, etag?: string): Response {
  return {
    ok: true,
    status: 200,
    headers: new Headers(etag ? { ETag: etag } : {}),
    json: async () => json,
    text: async () => JSON.stringify(json),
  } as unknown as Response
}

function err(status: number, body: unknown): Response {
  return {
    ok: false,
    status,
    headers: new Headers(),
    json: async () => body,
    text: async () => JSON.stringify(body),
  } as unknown as Response
}

/** URL+method router with a healthy default for every GET the drawer fires. */
function setupRouter(overrides: RouterOverrides = {}) {
  mockFetch.mockImplementation(async (url: string, init?: RequestInit) => {
    for (const [pattern, factory] of Object.entries(overrides)) {
      if (url.includes(pattern)) return factory(init)
    }
    const method = init?.method ?? 'GET'
    // S97 — the ProfileSection's EnhedTagPicker lists the person's Organisation
    // enheder (GET /api/admin/enheder?organisationId=…). Default to one active
    // enhed ("Netværk") so the picker renders + name-seeds from the roster label.
    if (url.includes('/api/admin/enheder')) {
      if (method === 'PUT') return ok(undefined)
      // The list GET serves the object envelope `{ enheder: [...] }`.
      return ok({ enheder: [{ enhedId: 'ENH-NET', organisationId: 'ORG1', name: 'Netværk', version: 1 }] })
    }
    if (url.includes('/api/admin/users/') && url.includes('/enheder')) {
      return ok(undefined)
    }
    if (url.includes('/api/admin/employee-profiles/')) {
      return ok(profileBody, '"1"')
    }
    if (url.includes('/birth-date')) {
      return ok({ employeeId: 'EMP001', birthDate: null, version: 1 }, '"1"')
    }
    if (url.includes('/employment-start-date')) {
      return ok({ employeeId: 'EMP001', employmentStartDate: null, version: 1 }, '"1"')
    }
    if (url.includes('/entitlement-eligibility/')) {
      return ok({
        employeeId: 'EMP001',
        entitlementType: 'CHILD_SICK',
        eligible: false,
        rowExists: false,
      })
    }
    if (url.includes('/api/admin/users/') && method === 'PUT') {
      return ok(editUser, '"2"')
    }
    if (url.includes('/api/admin/users') && method === 'POST') {
      return ok({ ...editUser, version: 1 }, '"1"')
    }
    if (url.includes('/api/admin/users/')) {
      return ok(editUser, '"1"')
    }
    return err(404, { error: 'Not Found' })
  })
}

function renderDrawer(props: Partial<ComponentProps<typeof EditPersonDrawer>> = {}) {
  return render(
    <ToastProvider>
      <EditPersonDrawer
        open
        organizations={organizations}
        defaultOrgId="ORG1"
        onClose={props.onClose ?? vi.fn()}
        onSaved={props.onSaved}
        user={props.user}
      />
    </ToastProvider>,
  )
}

beforeEach(() => {
  mockFetch.mockReset()
  mockRole = 'LocalHR'
  setupRouter()
})

// Collect every request to a URL substring for assertion.
function collect(substring: string) {
  const calls: Array<{ method: string; headers: Record<string, string>; body: unknown }> = []
  const inner = mockFetch.getMockImplementation()!
  mockFetch.mockImplementation(async (url: string, init?: RequestInit) => {
    if (url.includes(substring)) {
      const headers: Record<string, string> = {}
      const h = init?.headers as Record<string, string> | undefined
      if (h) for (const [k, v] of Object.entries(h)) headers[k] = v
      calls.push({
        method: init?.method ?? 'GET',
        headers,
        body: init?.body ? JSON.parse(init.body as string) : undefined,
      })
    }
    return inner(url, init)
  })
  return calls
}

describe('EditPersonDrawer — create flow', () => {
  it('POSTs /api/admin/users with the credentials + stamdata and calls onSaved', async () => {
    const userCalls = collect('/api/admin/users')
    const onSaved = vi.fn()
    const onClose = vi.fn()
    renderDrawer({ onSaved, onClose })

    await waitFor(() => {
      expect(screen.getByTestId('ep-title').textContent).toBe('Opret medarbejder')
    })

    fireEvent.change(screen.getByTestId('ep-create-user-id'), { target: { value: 'EMP010' } })
    fireEvent.change(screen.getByTestId('ep-create-username'), { target: { value: 'emp010' } })
    fireEvent.change(screen.getByTestId('ep-create-password'), { target: { value: 'pw' } })
    fireEvent.change(screen.getByTestId('ep-display-name'), { target: { value: 'Ny Bruger' } })

    fireEvent.click(screen.getByText('Opret medarbejder', { selector: 'button[type="submit"]' }))

    await waitFor(() => {
      expect(onSaved).toHaveBeenCalled()
    })
    expect(onClose).toHaveBeenCalled()
    const post = userCalls.find((c) => c.method === 'POST')
    expect(post).toBeDefined()
    expect(post!.body).toMatchObject({
      userId: 'EMP010',
      username: 'emp010',
      password: 'pw',
      displayName: 'Ny Bruger',
      primaryOrgId: 'ORG1',
      agreementCode: 'AC',
      // WARNING fix: the required okVersion is derived from the selected org and
      // threaded into the create POST (the backend CreateUserRequest requires it).
      okVersion: 'OK24',
    })
  })

  it('does NOT render the HR sections on create (defaults stand)', async () => {
    renderDrawer()
    await waitFor(() => {
      expect(screen.getByTestId('ep-title').textContent).toBe('Opret medarbejder')
    })
    // HR fields (profile + entitlement) are unavailable at create — the POST
    // seeds defaults; HR edits happen via the post-create PUTs.
    expect(screen.queryByTestId('ep-part-time')).toBeNull()
    // S97 — the structured-enhed tag picker (replaced the free-text field) is part
    // of the HR profile section → also absent on create.
    expect(screen.queryByTestId('ep-enheder-picker')).toBeNull()
    expect(screen.queryByTestId('ep-birth-date')).toBeNull()
  })
})

describe('EditPersonDrawer — edit flow per-endpoint contract', () => {
  it('hydrates the sections + sends the users PUT and the structured-enhed tags PUT', async () => {
    // S97 / TASK-9705 — the free-text enhedLabel field is RETIRED in favour of the
    // structured multi-tag picker. The profile PUT still carries the (read-only,
    // hydrated) enhedLabel unchanged; toggling a tag fires the SEPARATE set-user-
    // tags PUT (PUT /api/admin/users/{id}/enheder) with the chosen enhed ids.
    // All /api/admin/users/ calls (the users-row PUT carries `displayName`; the
    // set-user-tags PUT carries `enhedIds` — both hit this substring).
    const usersCalls = collect('/api/admin/users/')
    const profileCalls = collect('/api/admin/employee-profiles/')
    const onSaved = vi.fn()
    renderDrawer({ user: editUser, currentEnhedTagNames: null, onSaved })

    // The structured tag picker renders the org's active enheder as checkboxes.
    await waitFor(() => {
      expect(screen.getByTestId('ep-enheder-picker')).toBeDefined()
    })
    // Select the "Netværk" tag (checkbox label = enhed name).
    fireEvent.click(screen.getByLabelText('Netværk'))
    fireEvent.click(screen.getByText('Gem ændringer'))

    await waitFor(() => {
      expect(onSaved).toHaveBeenCalled()
    })
    // The users-row PUT (body has displayName) carries the dialog-open If-Match.
    const userPut = usersCalls.find(
      (c) => c.method === 'PUT' && (c.body as { displayName?: string })?.displayName !== undefined,
    )
    expect(userPut).toBeDefined()
    expect(userPut!.headers['If-Match']).toBe('"1"')
    // The profile PUT carries the unchanged read-only enhedLabel.
    const profilePut = profileCalls.find((c) => c.method === 'PUT')
    expect(profilePut!.headers['If-Match']).toBe('"1"')
    expect((profilePut!.body as { enhedLabel: string }).enhedLabel).toBe('Netværk')
    // The set-user-tags PUT (body has enhedIds) fired with the selected enhed id.
    const tagPut = usersCalls.find(
      (c) => c.method === 'PUT' && (c.body as { enhedIds?: string[] })?.enhedIds !== undefined,
    )
    expect(tagPut).toBeDefined()
    expect((tagPut!.body as { enhedIds: string[] }).enhedIds).toEqual(['ENH-NET'])
  })

  it('CHILD_SICK CREATE uses If-None-Match: * when no live row exists', async () => {
    setupRouter({
      '/entitlement-eligibility/': (init) =>
        (init?.method ?? 'GET') === 'PUT'
          ? ok({
              employeeId: 'EMP001',
              entitlementType: 'CHILD_SICK',
              eligible: true,
              effectiveFrom: '2026-06-14',
              version: 1,
            }, '"1"')
          : ok({
              employeeId: 'EMP001',
              entitlementType: 'CHILD_SICK',
              eligible: false,
              rowExists: false,
            }),
    })
    const eligCalls = collect('/entitlement-eligibility/')
    renderDrawer({ user: editUser, onSaved: vi.fn() })

    await waitFor(() => {
      expect(screen.getByTestId('ep-child-sick')).toBeDefined()
    })
    fireEvent.click(screen.getByTestId('ep-child-sick'))
    fireEvent.click(screen.getByText('Gem ændringer'))

    await waitFor(() => {
      const put = eligCalls.find((c) => c.method === 'PUT')
      expect(put).toBeDefined()
    })
    const put = eligCalls.find((c) => c.method === 'PUT')!
    expect(put.headers['If-None-Match']).toBe('*')
    expect(put.headers['If-Match']).toBeUndefined()
    expect(put.body).toEqual({ eligible: true })
  })

  it('CHILD_SICK UPDATE uses If-Match when a live row exists', async () => {
    setupRouter({
      '/entitlement-eligibility/': (init) =>
        (init?.method ?? 'GET') === 'PUT'
          ? ok({
              employeeId: 'EMP001',
              entitlementType: 'CHILD_SICK',
              eligible: false,
              effectiveFrom: '2026-06-14',
              version: 5,
            }, '"5"')
          : ok({
              employeeId: 'EMP001',
              entitlementType: 'CHILD_SICK',
              eligible: true,
              rowExists: true,
              version: 4,
            }, '"4"'),
    })
    const eligCalls = collect('/entitlement-eligibility/')
    renderDrawer({ user: editUser, onSaved: vi.fn() })

    await waitFor(() => {
      expect((screen.getByTestId('ep-child-sick') as HTMLInputElement).checked).toBe(true)
    })
    fireEvent.click(screen.getByTestId('ep-child-sick')) // toggle OFF
    fireEvent.click(screen.getByText('Gem ændringer'))

    await waitFor(() => {
      expect(eligCalls.some((c) => c.method === 'PUT')).toBe(true)
    })
    const put = eligCalls.find((c) => c.method === 'PUT')!
    expect(put.headers['If-Match']).toBe('"4"')
    expect(put.headers['If-None-Match']).toBeUndefined()
    expect(put.body).toEqual({ eligible: false })
  })

  it('CHILD_SICK 409 (create race) re-reads and shows the failure honestly', async () => {
    let putCount = 0
    setupRouter({
      '/entitlement-eligibility/': (init) => {
        const method = init?.method ?? 'GET'
        if (method === 'PUT') {
          putCount += 1
          // First PUT (If-None-Match: *) races an existing row → 409.
          return err(409, { error: 'lost update', currentVersion: 9 })
        }
        // GET: first read no row; the post-409 re-read returns the now-existing row.
        return putCount === 0
          ? ok({ employeeId: 'EMP001', entitlementType: 'CHILD_SICK', eligible: false, rowExists: false })
          : ok({ employeeId: 'EMP001', entitlementType: 'CHILD_SICK', eligible: false, rowExists: true, version: 9 }, '"9"')
      },
    })
    renderDrawer({ user: editUser, onSaved: vi.fn() })

    await waitFor(() => {
      expect(screen.getByTestId('ep-child-sick')).toBeDefined()
    })
    fireEvent.click(screen.getByTestId('ep-child-sick'))
    fireEvent.click(screen.getByText('Gem ændringer'))

    // The child-sick failure surfaces in the entitlement section error region.
    await waitFor(() => {
      expect(screen.getByTestId('ep-entitlement-error').textContent).toMatch(/Barns sygedag/)
    })
    expect(putCount).toBe(1)
  })
})

// ═══════════════════════════════════════════════════════════════════════════
// BLOCKER 1 (S76b fix-forward) — the users PUT, the DOB write, and the
// employment-start write ALL mutate the SAME `users` row, so each bumps
// `users.version`. This router MODELS that shared version: a PUT/DOB/
// employment-start carrying a STALE If-Match → 412; only the current version
// succeeds and bumps it. On the PRE-FIX code (which used the dialog-open
// users.version for the DOB + employment-start writes) the DOB write would 412
// the moment the users PUT bumped the version → this test would FAIL. It passes
// ONLY because the fix threads the latest users.version across the sequence.
// ═══════════════════════════════════════════════════════════════════════════
/**
 * A router whose `users` row has ONE shared version, validated on every write
 * that mutates it (users PUT, DOB PUT, employment-start PUT). Returns the call
 * recorder so the test can inspect the If-Match each write carried.
 */
function setupSharedUsersVersionRouter(initialVersion = 1) {
  let usersVersion = initialVersion
  const ifMatchOf = (init?: RequestInit): number | null => {
    const h = init?.headers as Record<string, string> | undefined
    const raw = h?.['If-Match']
    if (!raw) return null
    const m = /^"?(-?\d+)"?$/.exec(raw.trim())
    return m ? Number.parseInt(m[1], 10) : null
  }
  mockFetch.mockImplementation(async (url: string, init?: RequestInit) => {
    const method = init?.method ?? 'GET'

    // S97 — the enhed picker list + the (un-touched) set-tags PUT.
    if (url.includes('/api/admin/users/') && url.includes('/enheder')) {
      return ok(undefined)
    }
    if (url.includes('/api/admin/enheder')) {
      // The list GET serves the object envelope `{ enheder: [...] }`.
      return ok({ enheder: [{ enhedId: 'ENH-NET', organisationId: 'ORG1', name: 'Netværk', version: 1 }] })
    }
    // employee-profiles — a SEPARATE version (not the shared users row).
    if (url.includes('/api/admin/employee-profiles/')) {
      return ok(profileBody, '"1"')
    }
    // CHILD_SICK — a SEPARATE eligibility row.
    if (url.includes('/entitlement-eligibility/')) {
      return ok({ employeeId: 'EMP001', entitlementType: 'CHILD_SICK', eligible: false, rowExists: false })
    }

    // DOB — keyed on the SHARED users.version.
    if (url.includes('/birth-date')) {
      if (method === 'PUT') {
        const im = ifMatchOf(init)
        if (im !== usersVersion) {
          return err(412, { error: 'stale', expectedVersion: im, actualVersion: usersVersion })
        }
        usersVersion += 1
        return ok({ employeeId: 'EMP001', birthDate: '1990-01-01', version: usersVersion }, `"${usersVersion}"`)
      }
      return ok({ employeeId: 'EMP001', birthDate: null, version: usersVersion }, `"${usersVersion}"`)
    }
    // employment-start — keyed on the SHARED users.version.
    if (url.includes('/employment-start-date')) {
      if (method === 'PUT') {
        const im = ifMatchOf(init)
        if (im !== usersVersion) {
          return err(412, { error: 'stale', expectedVersion: im, actualVersion: usersVersion })
        }
        usersVersion += 1
        return ok({ employeeId: 'EMP001', employmentStartDate: '2024-03-01', version: usersVersion }, `"${usersVersion}"`)
      }
      return ok({ employeeId: 'EMP001', employmentStartDate: null, version: usersVersion }, `"${usersVersion}"`)
    }
    // users PUT — keyed on the SHARED users.version (the first writer to bump it).
    if (url.includes('/api/admin/users/') && method === 'PUT') {
      const im = ifMatchOf(init)
      if (im !== usersVersion) {
        return err(412, { error: 'stale', expectedVersion: im, actualVersion: usersVersion })
      }
      usersVersion += 1
      return ok({ ...editUser, version: usersVersion }, `"${usersVersion}"`)
    }
    // users GET (the stale-refresh re-read) — current shared version.
    if (url.includes('/api/admin/users/')) {
      return ok({ ...editUser, version: usersVersion }, `"${usersVersion}"`)
    }
    return err(404, { error: 'Not Found' })
  })
}

describe('EditPersonDrawer — shared users.version multi-PUT (BLOCKER 1)', () => {
  it('threads the bumped users.version across the users → DOB → employment-start writes (would 412 pre-fix)', async () => {
    setupSharedUsersVersionRouter(1)
    const userCalls = collect('/api/admin/users/')
    const dobCalls = collect('/birth-date')
    const startCalls = collect('/employment-start-date')
    const onSaved = vi.fn()
    const onClose = vi.fn()
    renderDrawer({ user: editUser, onSaved, onClose })

    await waitFor(() => {
      expect(screen.getByTestId('ep-birth-date')).toBeDefined()
    })

    // Dirty the stamdata (forces the users PUT to bump the shared version FIRST),
    // the DOB, and the employment-start — all three users-row writers in one save.
    fireEvent.change(screen.getByTestId('ep-display-name'), { target: { value: 'Ændret Navn' } })
    fireEvent.change(screen.getByTestId('ep-birth-date'), { target: { value: '1990-01-01' } })
    fireEvent.change(screen.getByTestId('ep-employment-start'), { target: { value: '2024-03-01' } })
    fireEvent.click(screen.getByText('Gem ændringer'))

    // With the threading fix the WHOLE sequence commits → onSaved + close.
    await waitFor(() => expect(onSaved).toHaveBeenCalled())
    expect(onClose).toHaveBeenCalled()

    // The users PUT carried the dialog-open version "1" and bumped it to 2.
    const userPut = userCalls.find((c) => c.method === 'PUT')!
    expect(userPut.headers['If-Match']).toBe('"1"')
    // The DOB write carried the THREADED "2" (NOT the stale dialog-open "1") and
    // bumped to 3; the employment-start write carried the threaded "3".
    const dobPut = dobCalls.find((c) => c.method === 'PUT')!
    expect(dobPut.headers['If-Match']).toBe('"2"')
    const startPut = startCalls.find((c) => c.method === 'PUT')!
    expect(startPut.headers['If-Match']).toBe('"3"')
  })

  it('skips an untouched DOB without 412 and still threads the version forward', async () => {
    setupSharedUsersVersionRouter(1)
    const userCalls = collect('/api/admin/users/')
    const dobCalls = collect('/birth-date')
    const startCalls = collect('/employment-start-date')
    const onSaved = vi.fn()
    renderDrawer({ user: editUser, onSaved })

    await waitFor(() => {
      expect(screen.getByTestId('ep-employment-start')).toBeDefined()
    })
    // Touch ONLY the stamdata + employment-start; DOB stays at its initial value.
    fireEvent.change(screen.getByTestId('ep-display-name'), { target: { value: 'Ændret Navn' } })
    fireEvent.change(screen.getByTestId('ep-employment-start'), { target: { value: '2024-03-01' } })
    fireEvent.click(screen.getByText('Gem ændringer'))

    await waitFor(() => expect(onSaved).toHaveBeenCalled())
    // No DOB PUT fired (untouched → skipped, not a 412).
    expect(dobCalls.some((c) => c.method === 'PUT')).toBe(false)
    // The employment-start write inherited the post-users-PUT version "2".
    expect(userCalls.find((c) => c.method === 'PUT')!.headers['If-Match']).toBe('"1"')
    expect(startCalls.find((c) => c.method === 'PUT')!.headers['If-Match']).toBe('"2"')
  })
})

describe('EditPersonDrawer — 412 staleConflict banner', () => {
  it('shows the banner with the expected/actual pair on a users-PUT 412', async () => {
    setupRouter({
      '/api/admin/users/': (init) =>
        (init?.method ?? 'GET') === 'PUT'
          ? err(412, { error: 'stale', expectedVersion: 1, actualVersion: 5 })
          : ok(editUser, '"1"'),
    })
    renderDrawer({ user: editUser, onSaved: vi.fn() })

    await waitFor(() => {
      expect(screen.getByTestId('ep-part-time')).toBeDefined()
    })
    fireEvent.change(screen.getByTestId('ep-display-name'), { target: { value: 'Ændret Navn' } })
    fireEvent.click(screen.getByText('Gem ændringer'))

    await waitFor(() => {
      const banner = screen.getByTestId('ep-stale-conflict-banner')
      expect(banner.textContent).toContain('Forventet version 1')
      expect(banner.textContent).toContain('aktuel version 5')
    })
    // The "Genindlæs" button is present.
    expect(screen.getByText('Genindlæs')).toBeDefined()
  })

  it('Genindlæs RE-FETCHES the users row (fresh version) so the retried save succeeds (BLOCKER 2)', async () => {
    // The server's users row is at version 5; the dialog opened at version 1. The
    // FIRST users PUT (If-Match "1") 412s. "Genindlæs" must RE-FETCH the users row
    // (GET → version 5) and re-bind it; the RETRIED users PUT then carries "5" and
    // succeeds. The pre-fix handler refreshed only the profile → the retry would
    // 412 again. We assert BOTH the user GET fired AND the retry committed.
    let usersVersion = 5
    const userCalls: Array<{ method: string; ifMatch?: string }> = []
    mockFetch.mockImplementation(async (url: string, init?: RequestInit) => {
      const method = init?.method ?? 'GET'
      if (url.includes('/api/admin/users/') && url.includes('/enheder')) return ok(undefined)
      if (url.includes('/api/admin/enheder')) return ok({ enheder: [{ enhedId: 'ENH-NET', organisationId: 'ORG1', name: 'Netværk', version: 1 }] })
      if (url.includes('/api/admin/employee-profiles/')) return ok(profileBody, '"1"')
      if (url.includes('/birth-date')) return ok({ employeeId: 'EMP001', birthDate: null, version: usersVersion }, `"${usersVersion}"`)
      if (url.includes('/employment-start-date')) return ok({ employeeId: 'EMP001', employmentStartDate: null, version: usersVersion }, `"${usersVersion}"`)
      if (url.includes('/entitlement-eligibility/')) return ok({ employeeId: 'EMP001', entitlementType: 'CHILD_SICK', eligible: false, rowExists: false })
      if (url.includes('/api/admin/users/') && method === 'PUT') {
        const h = init?.headers as Record<string, string> | undefined
        const ifMatch = h?.['If-Match']
        userCalls.push({ method, ifMatch })
        const im = ifMatch ? Number.parseInt(ifMatch.replace(/"/g, ''), 10) : null
        if (im !== usersVersion) {
          return err(412, { error: 'stale', expectedVersion: im, actualVersion: usersVersion })
        }
        usersVersion += 1
        return ok({ ...editUser, version: usersVersion }, `"${usersVersion}"`)
      }
      // The users GET (the stale-refresh re-read) returns the CURRENT version 5.
      if (url.includes('/api/admin/users/')) {
        userCalls.push({ method: 'GET' })
        return ok({ ...editUser, version: usersVersion }, `"${usersVersion}"`)
      }
      return err(404, { error: 'Not Found' })
    })
    const onSaved = vi.fn()
    const onClose = vi.fn()
    renderDrawer({ user: editUser, onSaved, onClose })

    await waitFor(() => {
      expect(screen.getByTestId('ep-part-time')).toBeDefined()
    })
    fireEvent.change(screen.getByTestId('ep-display-name'), { target: { value: 'Ændret Navn' } })
    fireEvent.click(screen.getByText('Gem ændringer'))
    await waitFor(() => {
      expect(screen.getByTestId('ep-stale-conflict-banner')).toBeDefined()
    })
    // The first users PUT carried the stale dialog-open "1" and 412'd.
    expect(userCalls.find((c) => c.method === 'PUT')?.ifMatch).toBe('"1"')

    // Genindlæs — must RE-FETCH the users row (a GET fires) and clear the banner.
    const userGetsBefore = userCalls.filter((c) => c.method === 'GET').length
    fireEvent.click(screen.getByText('Genindlæs'))
    await waitFor(() => {
      expect(screen.queryByTestId('ep-stale-conflict-banner')).toBeNull()
    })
    expect(userCalls.filter((c) => c.method === 'GET').length).toBeGreaterThan(userGetsBefore)

    // The RETRIED save now carries the fresh "5" → succeeds (onSaved + close).
    fireEvent.click(screen.getByText('Gem ændringer'))
    await waitFor(() => expect(onSaved).toHaveBeenCalled())
    expect(onClose).toHaveBeenCalled()
    const puts = userCalls.filter((c) => c.method === 'PUT')
    expect(puts[puts.length - 1].ifMatch).toBe('"5"')
  })
})

describe('EditPersonDrawer — per-section partial-failure honesty', () => {
  it('a later (profile) PUT failing leaves the users PUT committed and shows the profile error', async () => {
    setupRouter({
      '/api/admin/employee-profiles/': (init) =>
        (init?.method ?? 'GET') === 'PUT'
          ? err(422, { error: 'invalid fraction' })
          : ok(profileBody, '"1"'),
    })
    const userCalls = collect('/api/admin/users/')
    const onSaved = vi.fn()
    const onClose = vi.fn()
    renderDrawer({ user: editUser, onSaved, onClose })

    await waitFor(() => {
      expect(screen.getByTestId('ep-position')).toBeDefined()
    })
    // Dirty the profile via the position field (the free-text enhed field is gone
    // — S97 retired it; the profile PUT still owns position/part-time/enhedLabel).
    fireEvent.change(screen.getByTestId('ep-position'), { target: { value: 'Chefkonsulent' } })
    fireEvent.click(screen.getByText('Gem ændringer'))

    // The profile section shows its error; the drawer stays OPEN (no onSaved/close).
    await waitFor(() => {
      expect(screen.getByTestId('ep-profile-error').textContent).toMatch(/Profilen kunne ikke gemmes/)
    })
    expect(onSaved).not.toHaveBeenCalled()
    expect(onClose).not.toHaveBeenCalled()
    // The earlier users PUT did commit (it ran before the profile PUT).
    expect(userCalls.some((c) => c.method === 'PUT')).toBe(true)
  })
})

describe('EditPersonDrawer — role-gating', () => {
  it('a genuinely non-HR role (LocalLeader) sees NO HR sections', async () => {
    // LocalLeader (level 4) is below the LocalHR floor (level 3) → not HR-capable.
    mockRole = 'LocalLeader'
    renderDrawer({ user: editUser, onSaved: vi.fn() })

    await waitFor(() => {
      expect(screen.getByTestId('ep-display-name')).toBeDefined()
    })
    // Stamdata is visible; the HR profile/entitlement sections are hidden.
    expect(screen.queryByTestId('ep-part-time')).toBeNull()
    // S97 — the structured-enhed tag picker is part of the HR profile section.
    expect(screen.queryByTestId('ep-enheder-picker')).toBeNull()
    expect(screen.queryByTestId('ep-birth-date')).toBeNull()
    expect(screen.queryByTestId('ep-child-sick')).toBeNull()
  })

  it.each(['LocalHR', 'LocalAdmin', 'GlobalAdmin'])(
    'an HR-capable actor (%s) sees the HR sections (LocalAdmin ≥ LocalHR satisfies the floor)',
    async (role) => {
      mockRole = role
      renderDrawer({ user: editUser, onSaved: vi.fn() })

      await waitFor(() => {
        expect(screen.getByTestId('ep-part-time')).toBeDefined()
      })
      expect(screen.getByTestId('ep-position')).toBeDefined()
      expect(screen.getByTestId('ep-birth-date')).toBeDefined()
      expect(screen.getByTestId('ep-child-sick')).toBeDefined()
      // S97 — the structured-enhed tag picker replaces the old free-text field.
      await waitFor(() => {
        expect(screen.getByTestId('ep-enheder-picker')).toBeDefined()
      })
    },
  )
})

// S77 TASK-7700 / R5 — light a11y audit (manual @testing-library assertions, NO
// new dependency). The drawer is the scratch-built kit Drawer, which carries the
// FULL overlay a11y set; these assertions VERIFY it (role/modal/name, the close
// icon-button's accessible name, Escape-to-close + focus-trap wrap).
describe('EditPersonDrawer — a11y audit (R5)', () => {
  it('is a labelled modal dialog with an accessibly-named close button', async () => {
    renderDrawer({ user: editUser, onSaved: vi.fn() })
    const dialog = await screen.findByRole('dialog')
    // role=dialog + aria-modal + an accessible name (the kit Drawer ariaLabel = title).
    expect(dialog.getAttribute('aria-modal')).toBe('true')
    expect(dialog.getAttribute('aria-label')).toMatch(/Redigér Test Bruger/)
    // The ✕ close button has an accessible name ("Luk"), not a bare glyph.
    expect(screen.getByRole('button', { name: 'Luk' })).toBeDefined()
    // The submit button is reachable by its accessible name.
    expect(screen.getByRole('button', { name: 'Gem ændringer' })).toBeDefined()
  })

  it('Escape closes the drawer (the kit focus-trap handles Escape)', async () => {
    const onClose = vi.fn()
    renderDrawer({ user: editUser, onSaved: vi.fn(), onClose })
    const dialog = await screen.findByRole('dialog')
    fireEvent.keyDown(dialog, { key: 'Escape' })
    expect(onClose).toHaveBeenCalled()
  })

  it('focus-trap: Tab from the last focusable wraps to the first inside the drawer', async () => {
    renderDrawer({ user: editUser, onSaved: vi.fn() })
    const dialog = await screen.findByRole('dialog')
    // Enumerate the drawer's focusables (same selector the kit uses).
    const focusables = Array.from(
      dialog.querySelectorAll<HTMLElement>(
        'a[href], button:not([disabled]), input:not([disabled]), select:not([disabled]), textarea:not([disabled]), [tabindex]:not([tabindex="-1"])',
      ),
    )
    expect(focusables.length).toBeGreaterThan(1)
    const first = focusables[0]
    const last = focusables[focusables.length - 1]
    // Tab from the last element wraps to the first (forward trap).
    last.focus()
    expect(last).toHaveFocus()
    fireEvent.keyDown(dialog, { key: 'Tab' })
    expect(first).toHaveFocus()
    // Shift+Tab from the first wraps to the last (backward trap).
    fireEvent.keyDown(dialog, { key: 'Tab', shiftKey: true })
    expect(last).toHaveFocus()
  })

  it('returns focus to the opener trigger when the drawer unmounts (focus-return)', async () => {
    // Mount a trigger button, focus it, then mount the drawer (the kit captures
    // document.activeElement on open) and unmount it (restores the trigger).
    const trigger = document.createElement('button')
    trigger.textContent = 'Open'
    document.body.appendChild(trigger)
    trigger.focus()
    expect(trigger).toHaveFocus()

    const { unmount } = renderDrawer({ user: editUser, onSaved: vi.fn() })
    await screen.findByRole('dialog')
    // Closing/unmounting the drawer restores focus to the captured trigger.
    unmount()
    expect(trigger).toHaveFocus()
    document.body.removeChild(trigger)
  })
})

describe('EditPersonDrawer — 7603 slot', () => {
  it('renders extraSections in the lifecycle slot', async () => {
    render(
      <ToastProvider>
        <EditPersonDrawer
          open
          organizations={organizations}
          user={editUser}
          onClose={vi.fn()}
          extraSections={<div data-testid="slot-7603">Ledelseslinje (7603)</div>}
        />
      </ToastProvider>,
    )
    await waitFor(() => {
      expect(screen.getByTestId('slot-7603')).toBeDefined()
    })
    expect(screen.getByTestId('slot-7603').closest('[data-ep-slot="lifecycle"]')).not.toBeNull()
  })
})
