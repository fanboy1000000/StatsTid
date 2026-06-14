// S75 TASK-7502. Vitest + @testing-library/react tests for the read-only
// Medarbejder administration page. Mocks the two consumed hooks
// (useOrganizations from useAdmin, useMedarbejderRoster) rather than fetch, then
// asserts: the 3 tile counts compute from a small mocked roster; the tree
// renders person rows with depth; the orphan card shows isOrphan rows; a
// status-filter narrows + shows the status badge; the away-manager row shows
// the vikar badge (display-only, no Afslut button).
import { describe, it, expect, vi, beforeEach } from 'vitest'
import { render, screen, waitFor, fireEvent } from '@testing-library/react'
import type {
  MedarbejderRosterResponse,
  MedarbejderRosterRow,
} from '../../../hooks/useMedarbejderRoster'
import { MedarbejderAdministration } from '../MedarbejderAdministration'

// --- Hook mocks ---

const mockOrgs = [
  { orgId: 'MIN1', orgName: 'Finansministeriet', orgType: 'MINISTRY', parentOrgId: null, agreementCode: 'AC' },
  { orgId: 'STY1', orgName: 'Moderniseringsstyrelsen', orgType: 'STYRELSE', parentOrgId: 'MIN1', agreementCode: 'AC' },
  { orgId: 'DEP1', orgName: 'Afdeling X', orgType: 'DEPARTMENT', parentOrgId: 'STY1', agreementCode: 'AC' },
]

vi.mock('../../../hooks/useAdmin', () => ({
  useOrganizations: () => ({ organizations: mockOrgs, loading: false, error: null }),
}))

const fetchRoster = vi.fn()
vi.mock('../../../hooks/useMedarbejderRoster', () => ({
  useMedarbejderRoster: () => ({ fetchRoster }),
}))

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
  return render(<MedarbejderAdministration />)
}

beforeEach(() => {
  fetchRoster.mockReset()
  fetchRoster.mockResolvedValue({ ok: true, status: 200, data: rosterResponse })
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

  it('shows the away-manager vikar badge (display-only, no Afslut button)', async () => {
    renderPage()
    await waitFor(() => {
      expect(screen.getByText('Christian Christensen')).toBeDefined()
    })
    // The vikar line names the covering vikar with the da-DK formatted date.
    expect(screen.getByText(/Vita Vikar til 15\. jul 2026/)).toBeDefined()
    // Display-only: no write affordances.
    expect(screen.queryByText('Afslut')).toBeNull()
    expect(screen.queryByText('+ Vikar')).toBeNull()
    expect(screen.queryByText('Tilføj medarbejder')).toBeNull()
    expect(screen.queryByText('+ Tildel godkender')).toBeNull()
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
