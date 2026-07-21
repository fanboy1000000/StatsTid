// S119 / TASK-11901 — the ProjectManagement page's FIRST component test.
//
// The page shipped with ZERO coverage, which is exactly how prod bug #7 hid:
// the hand-written `Project` interface carried a PHANTOM `isActive` field no
// endpoint emits, and the Status column rendered "Inaktiv" for EVERY row
// (`undefined` → falsy — the S112 blank-columns class). The fix removes the
// field and the column; this test pins the fixed table AND the S119
// dead-control fix (the edit dialog's `projectCode` input is READ-ONLY in
// edit mode — the backend update DTO never bound it, so edits were silently
// discarded; the S91 dead-button class).
import { describe, it, expect, vi, beforeEach } from 'vitest'
import { render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { ProjectManagement } from '../ProjectManagement'

// The page consumes useToast + useAuth; render it standalone by mocking both.
const toastSpy = vi.fn()
vi.mock('../../../components/ui/Toast', () => ({
  useToast: () => ({ toast: toastSpy }),
}))
vi.mock('../../../contexts/AuthContext', () => ({
  useAuth: () => ({ orgId: 'STY02' }),
}))

const mockFetch = vi.fn()
vi.stubGlobal('fetch', mockFetch)

const mockStorage: Record<string, string> = { statstid_token: 'test-token' }
vi.stubGlobal('localStorage', {
  getItem: (k: string) => mockStorage[k] ?? null,
  setItem: (k: string, v: string) => { mockStorage[k] = v },
  removeItem: (k: string) => { delete mockStorage[k] },
})
const mockReload = vi.fn()
Object.defineProperty(window, 'location', { value: { reload: mockReload }, writable: true })

// 4-member spec rows (ProjectResponse — the list only ever serves ACTIVE
// projects; there is NO isActive on the wire).
const rows = [
  { projectId: 'p-1', projectCode: 'PROJ-001', projectName: 'Systemudvikling', sortOrder: 0 },
  { projectId: 'p-2', projectCode: 'DRIFT-100', projectName: 'Drift & support', sortOrder: 1 },
]

function stubList(listRows: unknown[] = rows) {
  mockFetch.mockImplementation(async () => ({
    ok: true,
    status: 200,
    headers: new Headers(),
    json: async () => listRows,
    text: async () => JSON.stringify(listRows),
  }))
}

beforeEach(() => {
  mockFetch.mockReset()
  toastSpy.mockReset()
})

describe('ProjectManagement — the fixed table (prod bug #7)', () => {
  it('renders the project rows WITHOUT a Status column (no constant "Inaktiv" cell)', async () => {
    stubList()
    render(<ProjectManagement />)

    await waitFor(() => {
      expect(screen.getByText('Systemudvikling')).toBeDefined()
    })
    expect(screen.getByText('Drift & support')).toBeDefined()
    expect(screen.getByText('PROJ-001')).toBeDefined()

    // The Status column is GONE — header and the informationless cells.
    expect(screen.queryByText('Status')).toBeNull()
    expect(screen.queryByText('Inaktiv')).toBeNull()
    expect(screen.queryByText('Aktiv')).toBeNull()

    // Exactly the 4 remaining headers, in order.
    const headers = screen.getAllByRole('columnheader').map((th) => th.textContent)
    expect(headers).toEqual(['Projektkode', 'Projektnavn', 'Sortering', 'Handlinger'])
  })

  it('renders the empty state across the 4 remaining columns', async () => {
    stubList([])
    render(<ProjectManagement />)
    await waitFor(() => {
      expect(screen.getByText('Ingen projekter oprettet endnu')).toBeDefined()
    })
    expect(screen.getByText('Ingen projekter oprettet endnu').getAttribute('colspan')).toBe('4')
  })
})

describe('ProjectManagement — the dead-control fix (S91 class)', () => {
  it('EDIT mode renders projectCode READ-ONLY/disabled (the backend never persisted it on update)', async () => {
    stubList()
    const user = userEvent.setup()
    render(<ProjectManagement />)
    await waitFor(() => expect(screen.getByText('Systemudvikling')).toBeDefined())

    const firstRow = screen.getByText('Systemudvikling').closest('tr')!
    await user.click(within(firstRow).getByRole('button', { name: 'Rediger' }))

    expect(screen.getByText('Rediger projekt')).toBeDefined()
    const codeInput = document.getElementById('project-code') as HTMLInputElement
    expect(codeInput.value).toBe('PROJ-001')
    expect(codeInput.disabled).toBe(true)
    // The name field stays editable — only the never-bound code is locked.
    const nameInput = document.getElementById('project-name') as HTMLInputElement
    expect(nameInput.disabled).toBe(false)
  })

  it('CREATE mode keeps projectCode editable (unchanged)', async () => {
    stubList()
    const user = userEvent.setup()
    render(<ProjectManagement />)
    await waitFor(() => expect(screen.getByText('Systemudvikling')).toBeDefined())

    await user.click(screen.getByRole('button', { name: 'Tilfoej projekt' }))

    expect(screen.getByText('Nyt projekt')).toBeDefined()
    const codeInput = document.getElementById('project-code') as HTMLInputElement
    expect(codeInput.disabled).toBe(false)
  })
})
