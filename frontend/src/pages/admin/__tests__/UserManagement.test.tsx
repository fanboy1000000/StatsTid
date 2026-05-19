// S35 TASK-3507 banner-with-retry tests for UserManagement.
//
// Migration of the admin user-edit flow to `apiFetchWithEtag<T>` per the
// S25 admin-strict If-Match contract (ADR-019 D2). The PUT
// `/api/admin/users/{userId}` carries `If-Match: "<version>"` and the page
// renders a stale-conflict banner on 412 with a "Genindlaes" refetch button.
//
// Test shape mirrors `PositionOverrideManagement.test.tsx` (S25 TASK-2508)
// and `AgreementConfigEditor.test.tsx` precedents: mock `globalThis.fetch`
// to walk through (initial list GET -> per-user GET -> PUT 412 -> Genindlaes
// refetch GET).
import { describe, it, expect, vi, beforeEach } from 'vitest'
import { render, screen, waitFor, fireEvent } from '@testing-library/react'
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

beforeEach(() => {
  mockFetch.mockReset()
  mockReload.mockReset()
  Object.keys(mockStorage).forEach((k) => delete mockStorage[k])
})

describe('UserManagement — 412 banner-with-retry', () => {
  it('shows the stale-conflict banner on 412 with expected/actual version pair', async () => {
    // 1) Organizations list GET (the page's local `useOrganizations` hook).
    mockFetch.mockResolvedValueOnce({
      ok: true,
      status: 200,
      headers: new Headers(),
      json: async () => [mockOrg],
    })
    // 2) Users-in-org list GET (apiClient.get from useAdmin.useOrgUsers).
    mockFetch.mockResolvedValueOnce({
      ok: true,
      status: 200,
      headers: new Headers(),
      json: async () => [mockUser],
    })
    // 3) Per-user GET on row click — capture the current ETag (apiFetchWithEtag).
    mockFetch.mockResolvedValueOnce({
      ok: true,
      status: 200,
      headers: new Headers({ ETag: '"1"' }),
      json: async () => mockUser,
    })
    // 4) PUT save -> 412 stale; structured body carries
    //    `expectedVersion` / `actualVersion`.
    const stalePayload = {
      error: 'Concurrency precondition failed',
      expectedVersion: 1,
      actualVersion: 5,
    }
    mockFetch.mockResolvedValueOnce({
      ok: false,
      status: 412,
      headers: new Headers(),
      text: async () => JSON.stringify(stalePayload),
    })

    render(<UserManagement />)

    // Wait for the user row to render in the table.
    await waitFor(() => {
      expect(screen.getByText('Test Bruger')).toBeDefined()
    })

    // Click the row to open the edit dialog (triggers the per-user GET).
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
    // 1) Organizations list GET.
    mockFetch.mockResolvedValueOnce({
      ok: true,
      status: 200,
      headers: new Headers(),
      json: async () => [mockOrg],
    })
    // 2) Users-in-org list GET.
    mockFetch.mockResolvedValueOnce({
      ok: true,
      status: 200,
      headers: new Headers(),
      json: async () => [mockUser],
    })
    // 3) Per-user GET on row click.
    mockFetch.mockResolvedValueOnce({
      ok: true,
      status: 200,
      headers: new Headers({ ETag: '"1"' }),
      json: async () => mockUser,
    })
    // 4) PUT -> 412.
    mockFetch.mockResolvedValueOnce({
      ok: false,
      status: 412,
      headers: new Headers(),
      text: async () => JSON.stringify({
        error: 'Concurrency precondition failed',
        expectedVersion: 1,
        actualVersion: 5,
      }),
    })
    // 5) Refetch on Genindlaes — returns the refreshed user (version 5).
    mockFetch.mockResolvedValueOnce({
      ok: true,
      status: 200,
      headers: new Headers({ ETag: '"5"' }),
      json: async () => ({ ...mockUser, version: 5 }),
    })

    render(<UserManagement />)

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

    // Confirm five fetches happened (orgs + users + per-user GET + PUT + refetch).
    expect(mockFetch).toHaveBeenCalledTimes(5)
  })
})
