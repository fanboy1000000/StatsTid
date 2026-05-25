// S51 TASK-5111. Vitest + @testing-library/react tests for the
// DelegationPage self-service delegation page. Mirrors ReportingLineTree.test.tsx
// pattern: mock globalThis.fetch, wrap in ToastProvider, assert DOM state.
import { describe, it, expect, vi, beforeEach } from 'vitest'
import { render, screen, waitFor, fireEvent } from '@testing-library/react'
import { ToastProvider } from '../../../components/ui/Toast'
import { DelegationPage } from '../DelegationPage'

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

// --- Fixtures ---

const inactiveStatus = {
  active: false,
  actingManagerId: null,
  effectiveFrom: null,
  effectiveTo: null,
  delegatedEmployees: [],
}

const activeStatus = {
  active: true,
  actingManagerId: 'MGR002',
  effectiveFrom: '2026-05-20',
  effectiveTo: '2026-06-15',
  delegatedEmployees: [
    { employeeId: 'EMP001', displayName: 'Anders Andersen' },
    { employeeId: 'EMP003', displayName: 'Christian Christensen' },
  ],
}

function renderPage() {
  return render(
    <ToastProvider>
      <DelegationPage />
    </ToastProvider>,
  )
}

/** Helper: queue a GET /delegate response. */
function mockGetDelegation(data: typeof activeStatus | typeof inactiveStatus = inactiveStatus) {
  mockFetch.mockResolvedValueOnce({
    ok: true,
    status: 200,
    headers: new Headers(),
    json: async () => data,
  })
}

beforeEach(() => {
  mockFetch.mockReset()
  mockReload.mockReset()
  Object.keys(mockStorage).forEach((k) => delete mockStorage[k])
})

describe('DelegationPage', () => {
  it('renders form when no active delegation', async () => {
    mockGetDelegation(inactiveStatus)

    renderPage()

    // Wait for the form to appear (loading finishes)
    await waitFor(() => {
      expect(screen.getByText('Uddeleger godkendelser')).toBeDefined()
    })

    // Form inputs should be present
    expect(screen.getByLabelText(/Vikarierende leder/)).toBeDefined()
    expect(screen.getByLabelText(/Returdato/)).toBeDefined()
    // Submit button
    expect(screen.getByRole('button', { name: 'Uddeleger' })).toBeDefined()
  })

  it('renders status when active delegation', async () => {
    mockGetDelegation(activeStatus)

    renderPage()

    // Wait for the active delegation card to appear
    await waitFor(() => {
      expect(screen.getByText('Aktiv uddelegering')).toBeDefined()
    })

    // Acting manager ID should be displayed
    expect(screen.getByText('MGR002')).toBeDefined()
    // Cancel button
    expect(screen.getByText('Annuller uddelegering')).toBeDefined()
    // Delegated employees
    expect(screen.getByText(/Anders Andersen/)).toBeDefined()
    expect(screen.getByText(/Christian Christensen/)).toBeDefined()
  })

  it('submit creates delegation', async () => {
    // 1) Initial GET — no active delegation
    mockGetDelegation(inactiveStatus)

    renderPage()

    await waitFor(() => {
      expect(screen.getByText('Uddeleger godkendelser')).toBeDefined()
    })

    // Fill in the form
    const managerInput = screen.getByLabelText(/Vikarierende leder/) as HTMLInputElement
    fireEvent.change(managerInput, { target: { value: 'MGR002' } })

    const dateInput = screen.getByLabelText(/Returdato/) as HTMLInputElement
    fireEvent.change(dateInput, { target: { value: '2026-06-15' } })

    // 2) Queue the POST response
    mockFetch.mockResolvedValueOnce({
      ok: true,
      status: 200,
      headers: new Headers(),
      json: async () => ({
        delegatedCount: 3,
        skippedCount: 0,
        actingManagerId: 'MGR002',
        effectiveFrom: '2026-05-25',
        effectiveTo: '2026-06-15',
      }),
    })

    // 3) Queue the GET refresh after successful creation
    mockGetDelegation(activeStatus)

    // Click submit
    fireEvent.click(screen.getByRole('button', { name: 'Uddeleger' }))

    // Assert the POST was called with correct body
    await waitFor(() => {
      const postCalls = mockFetch.mock.calls.filter(
        (call: unknown[]) => {
          const init = call[1] as RequestInit | undefined
          return init?.method === 'POST'
        },
      )
      expect(postCalls.length).toBe(1)
      const body = JSON.parse(postCalls[0][1].body as string)
      expect(body.actingManagerId).toBe('MGR002')
      expect(body.effectiveTo).toBe('2026-06-15')
    })
  })

  it('cancel calls DELETE', async () => {
    // 1) Initial GET — active delegation
    mockGetDelegation(activeStatus)

    renderPage()

    await waitFor(() => {
      expect(screen.getByText('Aktiv uddelegering')).toBeDefined()
    })

    // 2) Queue the DELETE response
    mockFetch.mockResolvedValueOnce({
      ok: true,
      status: 204,
      headers: new Headers(),
      json: async () => ({}),
      text: async () => '',
    })

    // 3) Queue the GET refresh after successful cancel
    mockGetDelegation(inactiveStatus)

    // Click cancel
    fireEvent.click(screen.getByText('Annuller uddelegering'))

    // Assert the DELETE was called
    await waitFor(() => {
      const deleteCalls = mockFetch.mock.calls.filter(
        (call: unknown[]) => {
          const init = call[1] as RequestInit | undefined
          return init?.method === 'DELETE'
        },
      )
      expect(deleteCalls.length).toBe(1)
      // URL should contain the delegate endpoint
      expect(deleteCalls[0][0]).toContain('/delegate')
    })
  })
})
