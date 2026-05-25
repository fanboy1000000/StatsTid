// S48 TASK-4814. Vitest + @testing-library/react tests for the
// ReportingLineTree admin page. Mirrors UserManagement.test.tsx pattern:
// mock globalThis.fetch, wrap in ToastProvider, assert DOM state.
import { describe, it, expect, vi, beforeEach } from 'vitest'
import { render, screen, waitFor, fireEvent } from '@testing-library/react'
import { ToastProvider } from '../../../components/ui/Toast'
import { ReportingLineTree } from '../ReportingLineTree'

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

const mockOrgs = [
  {
    orgId: 'MIN1',
    orgName: 'Finansministeriet',
    orgType: 'MINISTRY',
    parentOrgId: null,
    agreementCode: 'AC',
  },
  {
    orgId: 'STY1',
    orgName: 'Moderniseringsstyrelsen',
    orgType: 'STYRELSE',
    parentOrgId: 'MIN1',
    agreementCode: 'AC',
  },
  {
    orgId: 'DEP1',
    orgName: 'Afdeling X',
    orgType: 'DEPARTMENT',
    parentOrgId: 'STY1',
    agreementCode: 'AC',
  },
]

const mockTreeEntries = [
  {
    reportingLineId: 'rl-1',
    employeeId: 'EMP001',
    managerId: 'MGR001',
    treeRootOrgId: 'MIN1',
    relationship: 'PRIMARY',
    effectiveFrom: '2026-01-01',
    effectiveTo: null,
    source: 'ADMIN',
    version: 1,
    createdBy: 'admin@example.dk',
    createdAt: '2026-01-01T00:00:00Z',
    employeeDisplayName: 'Anders Andersen',
    managerDisplayName: 'Birgit Bertelsen',
  },
  {
    reportingLineId: 'rl-2',
    employeeId: 'EMP002',
    managerId: 'MGR001',
    treeRootOrgId: 'MIN1',
    relationship: 'ACTING',
    effectiveFrom: '2026-02-01',
    effectiveTo: null,
    source: 'ADMIN',
    version: 2,
    createdBy: 'admin@example.dk',
    createdAt: '2026-02-01T00:00:00Z',
    employeeDisplayName: 'Christian Christensen',
    managerDisplayName: 'Birgit Bertelsen',
  },
]

function renderPage() {
  return render(
    <ToastProvider>
      <ReportingLineTree />
    </ToastProvider>,
  )
}

/** Helper: queue the initial organisations GET that useOrganizations fires. */
function mockOrgsResponse() {
  mockFetch.mockResolvedValueOnce({
    ok: true,
    status: 200,
    headers: new Headers(),
    json: async () => mockOrgs,
  })
}

/** Helper: queue the tree GET response. */
function mockTreeResponse(entries = mockTreeEntries) {
  mockFetch.mockResolvedValueOnce({
    ok: true,
    status: 200,
    headers: new Headers(),
    json: async () => entries,
  })
}

beforeEach(() => {
  mockFetch.mockReset()
  mockReload.mockReset()
  Object.keys(mockStorage).forEach((k) => delete mockStorage[k])
})

describe('ReportingLineTree', () => {
  it('renders the tree root selector with MINISTRY/STYRELSE orgs', async () => {
    // 1) Orgs GET
    mockOrgsResponse()
    // 2) Tree GET (auto-fires for first tree root org)
    mockTreeResponse([])

    renderPage()

    await waitFor(() => {
      const select = screen.getByRole('combobox') as HTMLSelectElement
      expect(select).toBeDefined()
    })

    const select = screen.getByRole('combobox') as HTMLSelectElement
    const options = Array.from(select.options)
    const optionTexts = options.map((o) => o.textContent)

    // MINISTRY and STYRELSE orgs should be in the selector
    expect(optionTexts.some((t) => t!.includes('Finansministeriet'))).toBe(true)
    expect(optionTexts.some((t) => t!.includes('Moderniseringsstyrelsen'))).toBe(true)
    // DEPARTMENT should NOT appear in the tree root selector
    expect(optionTexts.some((t) => t!.includes('Afdeling X'))).toBe(false)
  })

  it('renders table rows with employee and manager names', async () => {
    // 1) Orgs GET
    mockOrgsResponse()
    // 2) Tree GET with entries
    mockTreeResponse()

    renderPage()

    // Wait for employee names to appear in the table
    await waitFor(() => {
      expect(screen.getByText('Anders Andersen')).toBeDefined()
    })

    expect(screen.getByText('Christian Christensen')).toBeDefined()
    // Manager name appears as root row AND in the Leder column
    expect(screen.getAllByText('Birgit Bertelsen').length).toBeGreaterThanOrEqual(1)
    // Relationship badges
    expect(screen.getByText('Vikarierende')).toBeDefined()
    expect(screen.getByText('Primaer')).toBeDefined()
  })

  it('opens the "Tildel leder" dialog with form fields', async () => {
    // 1) Orgs GET
    mockOrgsResponse()
    // 2) Tree GET
    mockTreeResponse()

    renderPage()

    // Wait for the page to finish loading
    await waitFor(() => {
      expect(screen.getByText('Anders Andersen')).toBeDefined()
    })

    // Click the "Tildel leder" button in the header
    const assignButton = screen.getAllByText('Tildel leder')[0]
    fireEvent.click(assignButton)

    // Assert dialog fields appear
    await waitFor(() => {
      expect(screen.getByLabelText(/Medarbejder-ID/)).toBeDefined()
    })
    expect(screen.getByLabelText(/Leder-ID/)).toBeDefined()
    expect(screen.getByLabelText(/Gyldig fra/)).toBeDefined()
    // The dialog submit button
    expect(screen.getByRole('button', { name: 'Tildel' })).toBeDefined()
    // The cancel button
    expect(screen.getByRole('button', { name: 'Annuller' })).toBeDefined()
  })

  it('shows loading spinner while fetching', async () => {
    // 1) Orgs: resolve immediately
    mockOrgsResponse()
    // 2) Tree: return a promise that never resolves (simulates pending state)
    mockFetch.mockReturnValueOnce(new Promise(() => {}))

    renderPage()

    // Wait for the org selector to appear (orgs loaded)
    await waitFor(() => {
      expect(screen.getByRole('combobox')).toBeDefined()
    })

    // The tree loading spinner should be visible (Spinner uses role="status"
    // with aria-label="Indlaeser")
    const spinners = screen.getAllByRole('status')
    expect(spinners.length).toBeGreaterThanOrEqual(1)
  })

  it('shows empty state message when tree has no entries', async () => {
    // 1) Orgs GET
    mockOrgsResponse()
    // 2) Tree GET with empty array
    mockTreeResponse([])

    renderPage()

    await waitFor(() => {
      expect(
        screen.getByText('Ingen ledelseslinjer fundet for denne organisation'),
      ).toBeDefined()
    })
  })

  it('calls DELETE API when "Fjern" button is clicked', async () => {
    // 1) Orgs GET
    mockOrgsResponse()
    // 2) Tree GET with entries
    mockTreeResponse()

    renderPage()

    // Wait for entries to render
    await waitFor(() => {
      expect(screen.getByText('Anders Andersen')).toBeDefined()
    })

    // 3) Queue the DELETE response
    mockFetch.mockResolvedValueOnce({
      ok: true,
      status: 204,
      headers: new Headers(),
      json: async () => ({}),
      text: async () => '',
    })
    // 4) Queue the tree refresh GET that follows a successful delete
    mockTreeResponse([])

    // Click the first "Fjern" button
    const removeButtons = screen.getAllByText('Fjern')
    expect(removeButtons.length).toBeGreaterThan(0)
    fireEvent.click(removeButtons[0])

    // Assert that a DELETE call was made
    await waitFor(() => {
      const deleteCalls = mockFetch.mock.calls.filter(
        (call: unknown[]) => {
          const init = call[1] as RequestInit | undefined
          return init?.method === 'DELETE'
        },
      )
      expect(deleteCalls.length).toBe(1)
      // The URL should contain the employee ID from the first entry
      expect(deleteCalls[0][0]).toContain('EMP001')
    })
  })
})
