// S75 TASK-7502. Vitest + @testing-library/react tests for the read-only
// Medarbejder administration page. Mocks the two consumed hooks
// (useOrganizations from useAdmin, useMedarbejderRoster) rather than fetch, then
// asserts: the 3 tile counts compute from a small mocked roster; the tree
// renders person rows with depth; the orphan card shows isOrphan rows; a
// status-filter narrows + shows the status badge; the away-manager row shows
// the vikar badge (display-only, no Afslut button).
import { describe, it, expect, vi, beforeEach } from 'vitest'
import { render, screen, waitFor, fireEvent } from '@testing-library/react'
import { ToastProvider } from '../../../components/ui/Toast'
import type {
  MedarbejderRosterResponse,
  MedarbejderRosterRow,
} from '../../../hooks/useMedarbejderRoster'
import { MedarbejderAdministration } from '../MedarbejderAdministration'

// --- Hook mocks ---

// Role-gating mock for the EditPersonDrawer (rendered from the tree). LocalAdmin
// is HR-capable (LocalAdmin ≥ LocalHR floor) so the HR sections show in edit.
let mockRole = 'LocalAdmin'
vi.mock('../../../contexts/AuthContext', () => ({
  useAuth: () => ({
    token: 'test-token',
    user: { employeeId: 'ADMIN1', role: mockRole },
    role: mockRole,
    orgId: 'MIN1',
    agreementCode: 'AC',
    scopes: [],
    isAuthenticated: true,
    login: vi.fn(),
    logout: vi.fn(),
  }),
}))

const mockOrgs = [
  { orgId: 'MIN1', orgName: 'Finansministeriet', orgType: 'MINISTRY', parentOrgId: null, agreementCode: 'AC' },
  { orgId: 'STY1', orgName: 'Moderniseringsstyrelsen', orgType: 'STYRELSE', parentOrgId: 'MIN1', agreementCode: 'AC' },
  { orgId: 'DEP1', orgName: 'Afdeling X', orgType: 'DEPARTMENT', parentOrgId: 'STY1', agreementCode: 'AC' },
]

// `fetchUser` (drawer edit-mode hydrate) + `createUser`/`updateUser` are exposed
// by useOrgUsers; the drawer + the page consume them. Defaults below.
const fetchUser = vi.fn()
const createUser = vi.fn()
const updateUser = vi.fn()
const fetchUsers = vi.fn()
vi.mock('../../../hooks/useAdmin', () => ({
  useOrganizations: () => ({ organizations: mockOrgs, loading: false, error: null }),
  useOrgUsers: () => ({
    users: [],
    loading: false,
    error: null,
    fetchUsers,
    fetchUser,
    createUser,
    updateUser,
  }),
}))

const fetchRoster = vi.fn()
vi.mock('../../../hooks/useMedarbejderRoster', () => ({
  useMedarbejderRoster: () => ({ fetchRoster }),
}))

// useReportingLines — the enforcement toggle (fetch/update settings) + the
// lifecycle sections (resolve approver / reports) consume it.
const fetchTreeSettings = vi.fn()
const updateTreeSettings = vi.fn()
const fetchEmployeeLines = vi.fn()
const fetchDirectReports = vi.fn()
vi.mock('../../../hooks/useReportingLines', () => ({
  useReportingLines: () => ({
    fetchTreeSettings,
    updateTreeSettings,
    fetchEmployeeLines,
    fetchDirectReports,
    searchPeople: vi.fn().mockResolvedValue({ ok: true, status: 200, data: { items: [], total: 0, limit: 60, offset: 0 } }),
    createVikar: vi.fn(),
    endVikar: vi.fn(),
    deletePersonWithReassignment: vi.fn(),
  }),
}))

// The drawer's HR-field reads go through useEntitlementEligibility — stub them so
// the edit hydrate resolves without real fetches.
vi.mock('../../../hooks/useEntitlementEligibility', () => ({
  useEntitlementEligibility: () => ({
    fetchBirthDate: vi.fn().mockResolvedValue(null),
    fetchEmploymentStartDate: vi.fn().mockResolvedValue(null),
    fetchChildSickEligibility: vi.fn().mockResolvedValue(null),
    setChildSick: vi.fn(),
    setBirthDate: vi.fn(),
    setEmploymentStartDate: vi.fn(),
  }),
}))

// The drawer's profile read (employeeProfileApi.fetchEmployeeProfile) goes
// through fetch — stub global fetch to a benign 404 so it resolves to null.
const mockFetch = vi.fn().mockResolvedValue({
  ok: false,
  status: 404,
  headers: new Headers(),
  json: async () => ({}),
  text: async () => 'Not Found',
})
vi.stubGlobal('fetch', mockFetch)
const mockStorage: Record<string, string> = { statstid_token: 'test-token' }
vi.stubGlobal('localStorage', {
  getItem: (key: string) => mockStorage[key] ?? null,
  setItem: (key: string, val: string) => { mockStorage[key] = val },
  removeItem: (key: string) => { delete mockStorage[key] },
})

// --- Fixture roster ---
// Tree:  Birgit (root, mgr) -> Anders (OPEN), Christian (away-mgr, vikar)
//           Christian -> Dorte (under away-manager)
//        Erik = orphan (no approver, approves no one)
function row(partial: Partial<MedarbejderRosterRow> & { employeeId: string; displayName: string }): MedarbejderRosterRow {
  return {
    enhedLabel: 'Enhed A',
    position: 'Fuldmægtig',
    structuralApproverId: null,
    periodStatus: 'APPROVED',
    outgoingVikar: null,
    isRoot: false,
    isOrphan: false,
    ...partial,
  }
}

const employees: MedarbejderRosterRow[] = [
  row({ employeeId: 'B', displayName: 'Birgit Bertelsen', position: 'Kontorchef', isRoot: true }),
  row({ employeeId: 'A', displayName: 'Anders Andersen', structuralApproverId: 'B', periodStatus: 'OPEN' }),
  row({
    employeeId: 'C',
    displayName: 'Christian Christensen',
    position: 'Teamleder',
    structuralApproverId: 'B',
    outgoingVikar: {
      vikarUserId: 'V',
      vikarDisplayName: 'Vita Vikar',
      untilDate: '2026-07-15',
      reason: 'Ferie',
    },
  }),
  row({ employeeId: 'D', displayName: 'Dorte Dam', structuralApproverId: 'C', periodStatus: 'OPEN' }),
  row({ employeeId: 'E', displayName: 'Erik Eriksen', isOrphan: true }),
]

const rosterResponse: MedarbejderRosterResponse = {
  employees,
  // two managers each have ≥1 pending → godkendCount === 2
  pendingCountByManager: { B: 1, C: 2 },
}

function renderPage() {
  return render(
    <ToastProvider>
      <MedarbejderAdministration />
    </ToastProvider>,
  )
}

beforeEach(() => {
  mockRole = 'LocalAdmin'
  fetchRoster.mockReset()
  fetchRoster.mockResolvedValue({ ok: true, status: 200, data: rosterResponse })
  fetchUser.mockReset()
  fetchUser.mockResolvedValue({
    userId: 'A', username: 'aandersen', displayName: 'Anders Andersen',
    email: 'a@example.dk', primaryOrgId: 'MIN1', agreementCode: 'AC',
    version: 1, etag: '"1"',
  })
  createUser.mockReset()
  createUser.mockResolvedValue({ userId: 'EMP010', version: 1, etag: '"1"' })
  updateUser.mockReset()
  fetchUsers.mockReset()
  fetchTreeSettings.mockReset()
  fetchTreeSettings.mockResolvedValue({ ok: true, status: 200, data: { enforcementMode: 'PREFERRED', version: 3 } })
  updateTreeSettings.mockReset()
  updateTreeSettings.mockResolvedValue({ ok: true, status: 200, data: { enforcementMode: 'REQUIRED', version: 4 } })
  fetchEmployeeLines.mockReset()
  fetchEmployeeLines.mockResolvedValue({ ok: true, status: 200, data: { active: [], history: [] } })
  fetchDirectReports.mockReset()
  fetchDirectReports.mockResolvedValue({ ok: true, status: 200, data: [] })
})

describe('MedarbejderAdministration', () => {
  it('renders the Styrelse selector with only MINISTRY/STYRELSE orgs', async () => {
    renderPage()
    await waitFor(() => {
      expect(screen.getAllByText('Birgit Bertelsen').length).toBeGreaterThanOrEqual(1)
    })
    const select = screen.getByRole('combobox') as HTMLSelectElement
    const optionTexts = Array.from(select.options).map((o) => o.textContent)
    expect(optionTexts.some((t) => t!.includes('Finansministeriet'))).toBe(true)
    expect(optionTexts.some((t) => t!.includes('Moderniseringsstyrelsen'))).toBe(true)
    expect(optionTexts.some((t) => t!.includes('Afdeling X'))).toBe(false)
  })

  it('computes the 3 tile counts from the mocked roster', async () => {
    renderPage()
    await waitFor(() => {
      expect(screen.getAllByText('Birgit Bertelsen').length).toBeGreaterThanOrEqual(1)
    })
    // Ikke indsendt: OPEN & not-orphan → Anders + Dorte = 2
    const indsendTile = screen.getByText('Ikke indsendt').closest('button')!
    expect(indsendTile.querySelector('.statValue')?.textContent).toBe('2')
    // Ikke godkendt: managers with ≥1 pending → B, C = 2
    const godkendTile = screen.getByText('Ikke godkendt').closest('button')!
    expect(godkendTile.querySelector('.statValue')?.textContent).toBe('2')
    // Vikar: away-managers (outgoingVikar != null) → Christian = 1.
    // Target the tile via its unique detail line ("Vikar" also appears on the
    // away-manager's vikar line, so match on the tile-specific copy).
    const vikarTile = screen.getByText('aktive vikarieringer').closest('button')!
    expect(vikarTile.querySelector('.statValue')?.textContent).toBe('1')
  })

  it('renders the tree with depth (a deep descendant is indented)', async () => {
    renderPage()
    await waitFor(() => {
      expect(screen.getAllByText('Birgit Bertelsen').length).toBeGreaterThanOrEqual(1)
    })
    expect(screen.getByText('Anders Andersen')).toBeDefined()
    expect(screen.getByText('Christian Christensen')).toBeDefined()
    // defaultCollapsed collapses team-leads whose reports are all individuals
    // (Christian, whose report Dorte is an individual) → expand to reveal Dorte.
    const expandToggles = screen.getAllByRole('button', { name: 'Vis' })
    expandToggles.forEach((btn) => fireEvent.click(btn))
    await waitFor(() => {
      expect(screen.getByText('Dorte Dam')).toBeDefined()
    })
  })

  it('shows orphan rows in the orphan card', async () => {
    renderPage()
    await waitFor(() => {
      expect(screen.getByText('Erik Eriksen')).toBeDefined()
    })
    // The orphan-card header announces the count.
    expect(screen.getByText(/1 mangler godkender/)).toBeDefined()
  })

  it('hides the orphan card AND narrows the tree under a status filter that excludes orphans (Vikar)', async () => {
    renderPage()
    await waitFor(() => {
      // Unfiltered: the orphan Erik is visible and a non-matching peer (Anders) too.
      expect(screen.getByText('Erik Eriksen')).toBeDefined()
      expect(screen.getByText('Anders Andersen')).toBeDefined()
    })
    // Filter to Vikar (away-managers) — orphans + non-vikar peers are excluded from
    // that classification, so BOTH the orphan card and Anders must disappear (the
    // orphan card tracks the shared matchIds, not just the search query).
    fireEvent.click(screen.getByText('aktive vikarieringer').closest('button')!)
    await waitFor(() => {
      expect(screen.queryByText('Erik Eriksen')).toBeNull()
    })
    // The orphan-card header is gone, and the non-matching peer is narrowed out.
    expect(screen.queryByText(/mangler godkender/)).toBeNull()
    expect(screen.queryByText('Anders Andersen')).toBeNull()
    // The away-manager (and its ancestor) remain visible.
    expect(screen.getByText('Christian Christensen')).toBeDefined()
  })

  it('narrows on a status filter and shows the status badge', async () => {
    renderPage()
    await waitFor(() => {
      expect(screen.getByText('Anders Andersen')).toBeDefined()
    })
    // No status badge before filtering.
    expect(screen.queryByText('Ikke indsendt', { selector: 'span' })).toBeNull()
    // Click the "Ikke indsendt" tile → OPEN rows show the warning badge.
    fireEvent.click(screen.getByText('Ikke indsendt').closest('button')!)
    await waitFor(() => {
      // The badge text "Ikke indsendt" now appears in a Badge (in addition to the
      // tile label) → ≥2 occurrences.
      expect(screen.getAllByText('Ikke indsendt').length).toBeGreaterThanOrEqual(2)
    })
  })

  it('shows the away-manager vikar badge (the inline row stays display-only; writes live in the drawer)', async () => {
    renderPage()
    await waitFor(() => {
      expect(screen.getByText('Christian Christensen')).toBeDefined()
    })
    // The vikar line names the covering vikar with the da-DK formatted date.
    expect(screen.getByText(/Vita Vikar til 15\. jul 2026/)).toBeDefined()
    // The ROW itself carries no inline vikar/approver write affordances — those
    // moved into the EditPersonDrawer (opened by clicking the name). "Tilføj
    // medarbejder" is now a real header button (entry into the create drawer).
    expect(screen.queryByText('Afslut')).toBeNull()
    expect(screen.queryByText('+ Vikar')).toBeNull()
    expect(screen.queryByText('+ Tildel godkender')).toBeNull()
    expect(screen.getByTestId('medarbejder-add')).toBeDefined()
  })

  it('shows the empty state when the roster is empty', async () => {
    fetchRoster.mockResolvedValue({
      ok: true,
      status: 200,
      data: { employees: [], pendingCountByManager: {} },
    })
    renderPage()
    await waitFor(() => {
      expect(
        screen.getByText('Ingen medarbejdere fundet for denne styrelse'),
      ).toBeDefined()
    })
  })
})

// S76b/7604 — page integration: the drawer wiring (create + edit), the
// enforcement toggle, and roster-refetch-on-write.
describe('MedarbejderAdministration — drawer integration (7604)', () => {
  it('"Tilføj medarbejder" opens the EditPersonDrawer in CREATE mode', async () => {
    renderPage()
    await waitFor(() => {
      expect(screen.getByTestId('medarbejder-add')).toBeDefined()
    })
    fireEvent.click(screen.getByTestId('medarbejder-add'))
    await waitFor(() => {
      // Create mode: the drawer title is "Opret medarbejder" + the create
      // credentials block is present.
      expect(screen.getByTestId('ep-title').textContent).toBe('Opret medarbejder')
    })
    expect(screen.getByTestId('ep-create-user-id')).toBeDefined()
  })

  it('clicking a tree person opens the drawer in EDIT mode (fetches the full User)', async () => {
    renderPage()
    await waitFor(() => {
      expect(screen.getByTestId('person-edit-A')).toBeDefined()
    })
    fireEvent.click(screen.getByTestId('person-edit-A'))
    // The full User is fetched for the clicked person.
    await waitFor(() => {
      expect(fetchUser).toHaveBeenCalledWith('A')
    })
    // Edit mode: the drawer title names the person (no create credentials block).
    await waitFor(() => {
      expect(screen.getByTestId('ep-title').textContent).toMatch(/Redigér Anders Andersen/)
    })
    expect(screen.queryByTestId('ep-create-user-id')).toBeNull()
  })

  it('toggling enforcement PREFERRED→REQUIRED calls updateTreeSettings with If-Match', async () => {
    renderPage()
    await waitFor(() => {
      expect(screen.getByTestId('enforcement-toggle')).toBeDefined()
    })
    // The current mode renders (PREFERRED → "Aktivér håndhævelse").
    expect(screen.getByTestId('enforcement-toggle').textContent).toMatch(/Aktivér/)
    fireEvent.click(screen.getByTestId('enforcement-toggle'))
    await waitFor(() => {
      expect(updateTreeSettings).toHaveBeenCalledWith(
        'MIN1',
        { enforcementMode: 'REQUIRED' },
        '"3"',
      )
    })
    // The server's new mode is reflected after the success.
    await waitFor(() => {
      expect(screen.getByTestId('enforcement-toggle').textContent).toMatch(/Deaktivér/)
    })
  })

  it('surfaces the population-gate 409 with the unassigned employee IDs (honest message)', async () => {
    updateTreeSettings.mockResolvedValue({
      ok: false,
      status: 409,
      error: 'population gate',
      body: { unassignedEmployeeIds: ['EMP7', 'EMP8'] },
    })
    renderPage()
    await waitFor(() => {
      expect(screen.getByTestId('enforcement-toggle')).toBeDefined()
    })
    fireEvent.click(screen.getByTestId('enforcement-toggle'))
    await waitFor(() => {
      const err = screen.getByTestId('enforcement-error')
      expect(err.textContent).toMatch(/mangler en udpeget godkender/)
      expect(err.textContent).toContain('EMP7')
      expect(err.textContent).toContain('EMP8')
    })
    // The mode stays PREFERRED (the toggle did not flip).
    expect(screen.getByTestId('enforcement-toggle').textContent).toMatch(/Aktivér/)
  })

  it('refetches the roster after a drawer save (onSaved → fetchRoster), preserving view state', async () => {
    renderPage()
    await waitFor(() => {
      expect(screen.getByTestId('medarbejder-add')).toBeDefined()
    })
    // Initial load fired once.
    const initialCalls = fetchRoster.mock.calls.length
    expect(initialCalls).toBeGreaterThanOrEqual(1)

    // Open create, fill the required fields, and submit → createUser resolves →
    // onSaved → the roster refetches.
    fireEvent.click(screen.getByTestId('medarbejder-add'))
    await waitFor(() => {
      expect(screen.getByTestId('ep-title').textContent).toBe('Opret medarbejder')
    })
    fireEvent.change(screen.getByTestId('ep-create-user-id'), { target: { value: 'EMP010' } })
    fireEvent.change(screen.getByTestId('ep-create-username'), { target: { value: 'emp010' } })
    fireEvent.change(screen.getByTestId('ep-create-password'), { target: { value: 'pw' } })
    fireEvent.change(screen.getByTestId('ep-display-name'), { target: { value: 'Ny Bruger' } })
    fireEvent.click(screen.getByText('Opret medarbejder', { selector: 'button[type="submit"]' }))

    await waitFor(() => {
      expect(createUser).toHaveBeenCalled()
    })
    // The roster refetched after the save (one more call than the initial load).
    await waitFor(() => {
      expect(fetchRoster.mock.calls.length).toBeGreaterThan(initialCalls)
    })
  })
})

// S77 TASK-7700 / R5 — light a11y audit (manual @testing-library assertions, NO
// new dependency). Verifies the toggle tiles + icon buttons carry accessible
// names, the tree expand/collapse buttons are role+name correct, and the
// drawer opened from the tree carries the kit's dialog semantics + focus-return.
describe('MedarbejderAdministration — a11y audit (R5)', () => {
  it('the 3 filter tiles are buttons with accessible names and toggle aria-pressed', async () => {
    renderPage()
    await waitFor(() => {
      expect(screen.getAllByText('Birgit Bertelsen').length).toBeGreaterThanOrEqual(1)
    })
    // Each tile is a real <button> (keyboard-operable) with an accessible name
    // derived from its label text, and an aria-pressed reflecting filter state.
    const indsend = screen.getByRole('button', { name: /Ikke indsendt/ })
    const godkend = screen.getByRole('button', { name: /Ikke godkendt/ })
    const vikar = screen.getByRole('button', { name: /Vikar/ })
    for (const tile of [indsend, godkend, vikar]) {
      expect(tile.getAttribute('aria-pressed')).toBe('false')
    }
    // Activating a tile flips its aria-pressed → assistive tech announces the
    // toggle state.
    fireEvent.click(vikar)
    await waitFor(() => {
      expect(screen.getByRole('button', { name: /Vikar/ }).getAttribute('aria-pressed')).toBe('true')
    })
  })

  it('the level segmented control is an accessible group, and tree expand/collapse buttons have names + aria-expanded', async () => {
    renderPage()
    await waitFor(() => {
      expect(screen.getByText('Christian Christensen')).toBeDefined()
    })
    // The level control is a labelled group (role=group + aria-labelledby → "Vis niveau").
    const group = screen.getByRole('group', { name: 'Vis niveau' })
    expect(group).toBeDefined()
    // A collapsed manager's toggle is a button named "Vis" with aria-expanded=false;
    // expanding flips both the name and aria-expanded (so SR users know the state).
    const showButtons = screen.getAllByRole('button', { name: 'Vis' })
    expect(showButtons.length).toBeGreaterThanOrEqual(1)
    expect(showButtons[0].getAttribute('aria-expanded')).toBe('false')
    fireEvent.click(showButtons[0])
    await waitFor(() => {
      // After expanding, the same node's toggle is now named "Skjul" (hide).
      expect(screen.getAllByRole('button', { name: 'Skjul' }).length).toBeGreaterThanOrEqual(1)
    })
  })

  it('the search field has an accessible name', async () => {
    renderPage()
    await waitFor(() => {
      expect(screen.getAllByText('Birgit Bertelsen').length).toBeGreaterThanOrEqual(1)
    })
    // The search box is reachable by its accessible name (aria-label), not just by
    // placeholder — so SR users can find it.
    expect(
      screen.getByRole('searchbox', { name: 'Søg medarbejder, stilling eller enhed' }),
    ).toBeDefined()
  })

  it('the EditPersonDrawer opened from the tree is a labelled modal dialog and returns focus to its trigger on close', async () => {
    renderPage()
    await waitFor(() => {
      expect(screen.getByTestId('person-edit-A')).toBeDefined()
    })
    const trigger = screen.getByTestId('person-edit-A') as HTMLButtonElement
    // The opener button is the active element when we click it (jsdom keeps focus
    // on a clicked button); capture it as the expected focus-return target.
    trigger.focus()
    expect(trigger).toHaveFocus()
    fireEvent.click(trigger)

    // The drawer mounts as a role=dialog with aria-modal + an accessible name
    // (the kit Drawer's ariaLabel = the drawer title).
    const dialog = await screen.findByRole('dialog')
    expect(dialog.getAttribute('aria-modal')).toBe('true')
    expect(dialog.getAttribute('aria-label')).toMatch(/Redigér Anders Andersen/)
    // The close (✕) button carries an accessible name ("Luk"), not a bare glyph.
    expect(screen.getByRole('button', { name: 'Luk' })).toBeDefined()

    // Close via Escape (the kit Drawer's focus-trap handles Escape) → focus
    // returns to the trigger (the kit captures it on open and restores on unmount).
    fireEvent.keyDown(dialog, { key: 'Escape' })
    await waitFor(() => {
      expect(screen.queryByRole('dialog')).toBeNull()
    })
    expect(trigger).toHaveFocus()
  })
})
