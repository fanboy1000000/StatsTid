// S49 TASK-4901. Vitest + @testing-library/react tests for the
// ApprovalDashboard page — tabbed layout with "Mine medarbejdere" and
// "Alle i omraade" views. Mirrors ReportingLineTree.test.tsx pattern:
// mock globalThis.fetch, assert DOM state.
import { describe, it, expect, vi, beforeEach } from 'vitest'
import { render, screen, waitFor, fireEvent } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { ApprovalDashboard } from '../ApprovalDashboard'

const mockFetch = vi.fn()
vi.stubGlobal('fetch', mockFetch)

const mockStorage: Record<string, string> = {
  statstid_token: 'test-token',
}
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

const mockMyReportPeriods = [
  {
    periodId: 'p-1',
    employeeId: 'EMP001',
    orgId: 'ORG1',
    periodStart: '2026-05-01',
    periodEnd: '2026-05-07',
    periodType: 'WEEKLY',
    status: 'SUBMITTED',
    submittedAt: '2026-05-08T10:00:00Z',
    approvedBy: null,
    approvedAt: null,
    rejectionReason: null,
    agreementCode: 'AC',
    okVersion: '1',
    createdAt: '2026-05-01T00:00:00Z',
    employeeApprovedAt: '2026-05-07T16:00:00Z',
  },
  {
    periodId: 'p-2',
    employeeId: 'EMP002',
    orgId: 'ORG1',
    periodStart: '2026-05-01',
    periodEnd: '2026-05-07',
    periodType: 'WEEKLY',
    status: 'SUBMITTED',
    submittedAt: '2026-05-08T11:00:00Z',
    approvedBy: null,
    approvedAt: null,
    rejectionReason: null,
    agreementCode: 'AC',
    okVersion: '1',
    createdAt: '2026-05-01T00:00:00Z',
    employeeApprovedAt: '2026-05-07T17:00:00Z',
  },
]

const mockAllPeriods = [
  ...mockMyReportPeriods,
  {
    periodId: 'p-3',
    employeeId: 'EMP003',
    orgId: 'ORG2',
    periodStart: '2026-05-01',
    periodEnd: '2026-05-07',
    periodType: 'WEEKLY',
    status: 'SUBMITTED',
    submittedAt: '2026-05-08T12:00:00Z',
    approvedBy: null,
    approvedAt: null,
    rejectionReason: null,
    agreementCode: 'HK',
    okVersion: '1',
    createdAt: '2026-05-01T00:00:00Z',
    employeeApprovedAt: '2026-05-07T18:00:00Z',
  },
]

/**
 * Helper: build a mock Response object for the given JSON body.
 * Both hooks call apiClient.get which uses fetch internally, so we
 * intercept at the globalThis.fetch level.
 */
function jsonResponse(body: unknown, status = 200) {
  return {
    ok: status >= 200 && status < 300,
    status,
    headers: new Headers(),
    json: async () => body,
    text: async () => JSON.stringify(body),
  }
}

/**
 * Queue the two initial fetch calls that fire on mount:
 *   1. usePendingApprovals  -> GET /api/approval/pending
 *   2. usePendingMyReports  -> GET /api/approval/pending?my-reports=true
 *
 * The order these hooks fire is deterministic (both useEffect callbacks run
 * in declaration order during the same commit phase). We use mockImplementation
 * to route by URL to avoid ordering sensitivity.
 *
 * After these two, compliance fetches may fire per-period — we queue a
 * catch-all for those.
 */
function mockInitialFetches(
  myReportData = mockMyReportPeriods,
  allData = mockAllPeriods,
) {
  mockFetch.mockImplementation(async (url: string) => {
    if (typeof url === 'string' && url.includes('my-reports=true')) {
      return jsonResponse(myReportData)
    }
    if (typeof url === 'string' && url.includes('/api/approval/pending')) {
      return jsonResponse(allData)
    }
    // Compliance endpoint — return empty result
    if (typeof url === 'string' && url.includes('/api/compliance/')) {
      return jsonResponse({ ruleId: '', employeeId: '', success: true, violations: [], warnings: [] })
    }
    // Fallback
    return jsonResponse({})
  })
}

beforeEach(() => {
  mockFetch.mockReset()
  mockReload.mockReset()
})

describe('ApprovalDashboard', () => {
  it('renders tab bar with two tabs', async () => {
    mockInitialFetches()

    render(<ApprovalDashboard />)

    await waitFor(() => {
      expect(screen.getByText(/Mine medarbejdere/)).toBeDefined()
    })
    expect(screen.getByText(/Alle i omraade/)).toBeDefined()
  })

  it('"Mine medarbejdere" tab fetches with my-reports=true', async () => {
    mockInitialFetches()

    render(<ApprovalDashboard />)

    // Wait for hooks to fire
    await waitFor(() => {
      const myReportsCalls = mockFetch.mock.calls.filter(
        (call: unknown[]) => {
          const url = call[0] as string
          return url.includes('my-reports=true')
        },
      )
      expect(myReportsCalls.length).toBeGreaterThanOrEqual(1)
    })
  })

  it('"Alle i omraade" tab fetches regular pending', async () => {
    mockInitialFetches()

    render(<ApprovalDashboard />)

    // Wait for initial data to load (mine medarbejdere tab is default)
    await waitFor(() => {
      expect(screen.getByText(/Mine medarbejdere/)).toBeDefined()
    })

    // Click the "Alle i omraade" tab
    const alleTab = screen.getByText(/Alle i omraade/)
    fireEvent.click(alleTab)

    // Both hooks fire on mount, so /api/approval/pending (without my-reports)
    // should have been called
    await waitFor(() => {
      const pendingCalls = mockFetch.mock.calls.filter(
        (call: unknown[]) => {
          const url = call[0] as string
          return url.includes('/api/approval/pending') && !url.includes('my-reports')
        },
      )
      expect(pendingCalls.length).toBeGreaterThanOrEqual(1)
    })
  })

  it('shows pending periods in table', async () => {
    const user = userEvent.setup()
    mockInitialFetches()

    render(<ApprovalDashboard />)

    // Default tab is "Mine medarbejdere" — should show EMP001 and EMP002
    await waitFor(() => {
      expect(screen.getByText('EMP001')).toBeDefined()
    })
    expect(screen.getByText('EMP002')).toBeDefined()

    // Switch to "Alle i omraade" tab using userEvent (dispatches the full
    // pointer + mouse + click sequence that Radix Tabs requires in jsdom)
    const allTab = screen.getByRole('tab', { name: /Alle i omraade/ })
    await user.click(allTab)

    // After clicking, the "all" panel becomes active and EMP003 should
    // be visible. EMP001+EMP002 also appear in allPeriods.
    await waitFor(() => {
      expect(screen.getByText('EMP003')).toBeDefined()
    })
  })
})
