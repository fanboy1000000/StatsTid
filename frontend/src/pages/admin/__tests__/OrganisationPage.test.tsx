// S99 / TASK-9905 + S100 / TASK-10004 — vitest for the redesigned Organisation
// page. Mocks the four consumed hooks (useOrganizationTree, useOrganizations,
// useOrganizationStructure, useEnheder) rather than fetch, then asserts: the
// tree renders (MAO → Organisation → a NESTED Enhed sub-tree); the level control
// defaults to Organisation; search flattens + the empty message; each dialog;
// the status branches (delete 422 + count / create 409 dup / move 400 vs 422).
//
// S100 INVERTS the S99 dead-button matrix: Tilføj is now PRESENT on an Enhed
// (creates a CHILD enhed with parentEnhedId) and Flyt is now PRESENT on an Enhed
// (the re-parent move dialog excluding self + descendants); the Enhed-delete copy
// now promises the children re-parent UP; the derived level renders as a badge.
// Omdøb stays inline on Enhed; the rename PUT body stays NAME-ONLY.
import { describe, it, expect, vi, beforeEach } from 'vitest'
import { render, screen, waitFor, fireEvent } from '@testing-library/react'
import { ToastProvider } from '../../../components/ui/Toast'
import { OrganisationPage } from '../OrganisationPage'
import type { TreeMaoNode } from '../../../hooks/useOrganizationTree'

// ── tree fixture: 1 MAO → 2 Organisations; STY01 has a NESTED enhed sub-tree:
//   ENH01 (root, level 1) → ENH02 (child, level 2); ENH03 (root, level 1).
// The nesting lets us exercise the move dialog's self+descendant exclusion. ──
function makeTree(): TreeMaoNode[] {
  return [
    {
      orgId: 'MIN01',
      orgName: 'Finansministeriet',
      orgType: 'MAO',
      employeeCount: 42,
      organisations: [
        {
          orgId: 'STY01',
          orgName: 'Økonomistyrelsen',
          orgType: 'ORGANISATION',
          parentOrgId: 'MIN01',
          materializedPath: '/MIN01/STY01/',
          employeeCount: 30,
          enheder: [
            {
              enhedId: 'ENH01',
              name: 'Team Drift',
              taggedUserCount: 5,
              parentEnhedId: null,
              level: 1,
              children: [
                {
                  enhedId: 'ENH02',
                  name: 'Team Netværk',
                  taggedUserCount: 2,
                  parentEnhedId: 'ENH01',
                  level: 2,
                  children: [],
                },
              ],
            },
            {
              enhedId: 'ENH03',
              name: 'Team Support',
              taggedUserCount: 3,
              parentEnhedId: null,
              level: 1,
              children: [],
            },
          ],
        },
        {
          orgId: 'STY02',
          orgName: 'Digitaliseringsstyrelsen',
          orgType: 'ORGANISATION',
          parentOrgId: 'MIN01',
          materializedPath: '/MIN01/STY02/',
          employeeCount: 12,
          enheder: [],
        },
      ],
    },
    {
      orgId: 'MIN02',
      orgName: 'Skatteministeriet',
      orgType: 'MAO',
      employeeCount: 0,
      organisations: [],
    },
  ]
}

// ── hook mocks (referentially stable per render — PAT-007) ──
let mockTree: TreeMaoNode[] = makeTree()
const fetchTree = vi.fn()
vi.mock('../../../hooks/useOrganizationTree', () => ({
  useOrganizationTree: () => ({ tree: mockTree, loading: false, error: null, fetchTree }),
}))

const createOrganization = vi.fn()
const updateOrganization = vi.fn()
vi.mock('../../../hooks/useAdmin', () => ({
  useOrganizations: () => ({
    organizations: [],
    loading: false,
    error: null,
    fetchOrganizations: vi.fn(),
    createOrganization,
    updateOrganization,
  }),
}))

const deleteOrganization = vi.fn()
const moveOrganization = vi.fn()
vi.mock('../../../hooks/useOrganizationStructure', () => ({
  useOrganizationStructure: () => ({ deleteOrganization, moveOrganization }),
}))

const fetchEnheder = vi.fn()
const createEnhed = vi.fn()
const renameEnhed = vi.fn()
const moveEnhed = vi.fn()
const deleteEnhed = vi.fn()
vi.mock('../../../hooks/useEnheder', () => ({
  useEnheder: () => ({ fetchEnheder, createEnhed, renameEnhed, moveEnhed, deleteEnhed }),
}))

function structureError(status: number, message: string, employeeCount?: number) {
  const err = new Error(message) as Error & { status: number; employeeCount?: number }
  err.status = status
  err.employeeCount = employeeCount
  return err
}

function renderPage() {
  return render(
    <ToastProvider>
      <OrganisationPage />
    </ToastProvider>,
  )
}

beforeEach(() => {
  mockTree = makeTree()
  fetchTree.mockReset().mockResolvedValue(undefined)
  createOrganization.mockReset().mockResolvedValue({})
  updateOrganization.mockReset().mockResolvedValue({})
  deleteOrganization.mockReset().mockResolvedValue(undefined)
  moveOrganization.mockReset().mockResolvedValue(undefined)
  fetchEnheder.mockReset().mockResolvedValue({
    ok: true,
    data: [
      { enhedId: 'ENH01', organisationId: 'STY01', name: 'Team Drift', version: 3, parentEnhedId: null, level: 1, etag: '"3"' },
      { enhedId: 'ENH02', organisationId: 'STY01', name: 'Team Netværk', version: 1, parentEnhedId: 'ENH01', level: 2, etag: '"1"' },
      { enhedId: 'ENH03', organisationId: 'STY01', name: 'Team Support', version: 2, parentEnhedId: null, level: 1, etag: '"2"' },
    ],
  })
  createEnhed.mockReset().mockResolvedValue({})
  renameEnhed.mockReset().mockResolvedValue({})
  moveEnhed.mockReset().mockResolvedValue({})
  deleteEnhed.mockReset().mockResolvedValue(undefined)
})

describe('OrganisationPage — render + level control', () => {
  it('renders the title and the sub-paragraph', () => {
    renderPage()
    expect(screen.getByRole('heading', { name: 'Organisation' })).toBeDefined()
    expect(screen.getByText(/Forvalt organisationshierarkiet/)).toBeDefined()
  })

  it('defaults to the Organisation level on mount (MAOs expanded, organisations shown, enheder hidden)', () => {
    renderPage()
    // Both MAOs render.
    expect(screen.getByTestId('org-row-MIN01')).toBeDefined()
    expect(screen.getByTestId('org-row-MIN02')).toBeDefined()
    // Organisations are shown (MAO expanded by the default ORGANISATION level).
    expect(screen.getByTestId('org-row-STY01')).toBeDefined()
    expect(screen.getByTestId('org-row-STY02')).toBeDefined()
    // The Enhed is HIDDEN (Organisation level does not expand organisations).
    expect(screen.queryByTestId('org-row-ENH01')).toBeNull()
    // The Organisation segment is the active one.
    const orgSeg = screen.getByRole('button', { name: 'Organisation' })
    expect(orgSeg.getAttribute('aria-pressed')).toBe('true')
  })

  it('the Enhed level expands everything (enhed leaf visible)', () => {
    renderPage()
    fireEvent.click(screen.getByRole('button', { name: 'Enhed' }))
    expect(screen.getByTestId('org-row-ENH01')).toBeDefined()
  })

  it('the Ministeransvarsområde level collapses to roots only', () => {
    renderPage()
    fireEvent.click(screen.getByRole('button', { name: 'Ministeransvarsområde' }))
    expect(screen.getByTestId('org-row-MIN01')).toBeDefined()
    expect(screen.queryByTestId('org-row-STY01')).toBeNull()
  })

  it('rolls up the employee count (MAO) and shows taggedUserCount for an Enhed', () => {
    renderPage()
    expect(screen.getByTestId('org-row-MIN01').textContent).toContain('42')
    fireEvent.click(screen.getByRole('button', { name: 'Enhed' }))
    expect(screen.getByTestId('org-row-ENH01').textContent).toContain('5')
  })
})

describe('OrganisationPage — search', () => {
  it('flattens to matching nodes (case-insensitive), no chevrons', async () => {
    renderPage()
    fireEvent.change(screen.getByLabelText('Søg enhed'), { target: { value: 'team' } })
    await waitFor(() => {
      expect(screen.getByTestId('org-row-ENH01')).toBeDefined()
    })
    // The non-matching MAO is gone.
    expect(screen.queryByTestId('org-row-MIN01')).toBeNull()
    // No chevron in search mode.
    expect(screen.queryByTestId('chevron-ENH01')).toBeNull()
  })

  it('shows the empty message when nothing matches', async () => {
    renderPage()
    fireEvent.change(screen.getByLabelText('Søg enhed'), { target: { value: 'zzzz' } })
    await waitFor(() => {
      expect(screen.getByText('Ingen enheder matcher søgningen.')).toBeDefined()
    })
  })
})

describe('OrganisationPage — action matrix (S100: Tilføj/Flyt now on Enhed)', () => {
  it('Tilføj is PRESENT on an Enhed (creates a child), present on MAO + Organisation', () => {
    renderPage()
    fireEvent.click(screen.getByRole('button', { name: 'Enhed' }))
    // S100 inversion: Tilføj is now present on an Enhed (a child enhed).
    expect(screen.getByTestId('action-tilfoej-ENH01')).toBeDefined()
    expect(screen.getByTestId('action-tilfoej-MIN01')).toBeDefined()
    expect(screen.getByTestId('action-tilfoej-STY01')).toBeDefined()
  })

  it('Flyt is PRESENT on an Enhed (re-parent) + Organisation, still ABSENT on MAO', () => {
    renderPage()
    fireEvent.click(screen.getByRole('button', { name: 'Enhed' }))
    expect(screen.queryByTestId('action-flyt-MIN01')).toBeNull()
    // S100 inversion: Flyt is now present on an Enhed.
    expect(screen.getByTestId('action-flyt-ENH01')).toBeDefined()
    expect(screen.getByTestId('action-flyt-STY01')).toBeDefined()
  })

  it('Omdøb is INLINE on an Enhed (no dialog) and a DIALOG on MAO/Organisation', () => {
    renderPage()
    fireEvent.click(screen.getByRole('button', { name: 'Enhed' }))
    // Enhed Omdøb → inline input, no dialog scrim (UNCHANGED in S100).
    fireEvent.click(screen.getByTestId('action-omdoeb-ENH01'))
    expect(screen.getByTestId('enhed-edit-ENH01')).toBeDefined()
    expect(screen.queryByTestId('dialog-scrim')).toBeNull()
    // Organisation Omdøb → the rename-warning dialog.
    fireEvent.click(screen.getByTestId('action-omdoeb-STY01'))
    expect(screen.getByText('Omdøb organisation')).toBeDefined()
  })

  it('renders the derived Enhed level as a badge (N1 for a root, N2 for a child)', () => {
    renderPage()
    fireEvent.click(screen.getByRole('button', { name: 'Enhed' }))
    expect(screen.getByTestId('enhed-level-ENH01').textContent).toBe('N1')
    expect(screen.getByTestId('enhed-level-ENH03').textContent).toBe('N1')
    expect(screen.getByTestId('enhed-level-ENH02').textContent).toBe('N2')
  })
})

describe('OrganisationPage — create dialog', () => {
  it('the toolbar button opens the create-MAO dialog and POSTs name-only', async () => {
    renderPage()
    fireEvent.click(screen.getByTestId('new-mao'))
    // The dialog title (in the dialog head) appears.
    expect(screen.getByRole('dialog', { name: 'Nyt ministeransvarsområde' })).toBeDefined()
    const input = screen.getByLabelText('Navn') as HTMLInputElement
    fireEvent.change(input, { target: { value: 'Nyt MAO' } })
    // Submit the dialog's form (the submit button label collides with the toolbar
    // button, so submit via the form element to be unambiguous).
    fireEvent.submit(input.closest('form')!)
    await waitFor(() => {
      expect(createOrganization).toHaveBeenCalled()
    })
    expect(createOrganization).toHaveBeenCalledWith({
      orgName: 'Nyt MAO',
      orgType: 'MAO',
      parentOrgId: null,
    })
  })

  it('MAO Tilføj creates an ORGANISATION under the MAO (name-only)', async () => {
    renderPage()
    fireEvent.click(screen.getByTestId('action-tilfoej-MIN01'))
    expect(screen.getByRole('dialog', { name: 'Ny organisation' })).toBeDefined()
    const input = screen.getByLabelText('Navn') as HTMLInputElement
    fireEvent.change(input, { target: { value: 'Ny Styrelse' } })
    fireEvent.submit(input.closest('form')!)
    await waitFor(() => {
      expect(createOrganization).toHaveBeenCalledWith({
        orgName: 'Ny Styrelse',
        orgType: 'ORGANISATION',
        parentOrgId: 'MIN01',
      })
    })
  })

  it('Organisation Tilføj creates a ROOT Enhed via createEnhed (parentEnhedId null)', async () => {
    renderPage()
    fireEvent.click(screen.getByTestId('action-tilfoej-STY01'))
    expect(screen.getByRole('dialog', { name: 'Ny enhed' })).toBeDefined()
    const input = screen.getByLabelText('Navn') as HTMLInputElement
    fireEvent.change(input, { target: { value: 'Team Y' } })
    fireEvent.submit(input.closest('form')!)
    await waitFor(() => {
      // S100: org-level create passes the org id + a null parent (a root enhed).
      expect(createEnhed).toHaveBeenCalledWith('STY01', 'Team Y', null)
    })
  })

  it('Enhed Tilføj creates a CHILD Enhed (parentEnhedId = the enhed) — S100', async () => {
    renderPage()
    fireEvent.click(screen.getByRole('button', { name: 'Enhed' }))
    fireEvent.click(screen.getByTestId('action-tilfoej-ENH01'))
    expect(screen.getByRole('dialog', { name: 'Ny enhed' })).toBeDefined()
    const input = screen.getByLabelText('Navn') as HTMLInputElement
    fireEvent.change(input, { target: { value: 'Team Z' } })
    fireEvent.submit(input.closest('form')!)
    await waitFor(() => {
      // The child create carries the owning Organisation + the parent enhed id.
      expect(createEnhed).toHaveBeenCalledWith('STY01', 'Team Z', 'ENH01')
    })
  })

  it('a 409 dup surfaces an inline error in the dialog', async () => {
    createOrganization.mockRejectedValueOnce(structureError(409, 'dup'))
    renderPage()
    fireEvent.click(screen.getByTestId('action-tilfoej-MIN01'))
    const input = screen.getByLabelText('Navn') as HTMLInputElement
    fireEvent.change(input, { target: { value: 'Dup' } })
    fireEvent.submit(input.closest('form')!)
    await waitFor(() => {
      expect(screen.getByText(/findes allerede en aktiv/)).toBeDefined()
    })
  })
})

describe('OrganisationPage — rename dialog (name-only)', () => {
  it('the rename PUT body is NAME-ONLY (does not clobber agreement/ok)', async () => {
    renderPage()
    fireEvent.click(screen.getByTestId('action-omdoeb-STY01'))
    const input = screen.getByLabelText('Nyt navn') as HTMLInputElement
    fireEvent.change(input, { target: { value: 'Økostyr 2' } })
    fireEvent.click(screen.getByRole('button', { name: 'Gem ændring' }))
    await waitFor(() => {
      expect(updateOrganization).toHaveBeenCalledWith('STY01', { orgName: 'Økostyr 2' })
    })
    // Strictly name-only — no agreementCode / okVersion keys.
    const [, body] = updateOrganization.mock.calls[0]
    expect(Object.keys(body)).toEqual(['orgName'])
  })

  it('Enhed inline rename resolves the version then PUTs', async () => {
    renderPage()
    fireEvent.click(screen.getByRole('button', { name: 'Enhed' }))
    fireEvent.click(screen.getByTestId('action-omdoeb-ENH01'))
    const input = screen.getByTestId('enhed-edit-ENH01') as HTMLInputElement
    fireEvent.change(input, { target: { value: 'Team Drift 2' } })
    fireEvent.keyDown(input, { key: 'Enter' })
    await waitFor(() => {
      expect(fetchEnheder).toHaveBeenCalledWith('STY01')
      expect(renameEnhed).toHaveBeenCalledWith('ENH01', 'Team Drift 2', '"3"')
    })
  })
})

describe('OrganisationPage — move dialog', () => {
  it('the target select excludes the current parent and moves on confirm', async () => {
    renderPage()
    fireEvent.click(screen.getByTestId('action-flyt-STY01'))
    expect(screen.getByRole('dialog', { name: 'Flyt organisation' })).toBeDefined()
    // The current parent (MIN01) is excluded; MIN02 is offered.
    const select = screen.getByLabelText('Ny placering') as HTMLSelectElement
    const values = Array.from(select.options).map((o) => o.value)
    expect(values).toContain('MIN02')
    expect(values).not.toContain('MIN01')
    fireEvent.change(select, { target: { value: 'MIN02' } })
    fireEvent.submit(select.closest('form')!)
    await waitFor(() => {
      expect(moveOrganization).toHaveBeenCalledWith('STY01', 'MIN02')
    })
  })

  it('maps a 400 to an inline "ugyldig placering" error', async () => {
    moveOrganization.mockRejectedValueOnce(structureError(400, 'bad'))
    renderPage()
    fireEvent.click(screen.getByTestId('action-flyt-STY01'))
    const select = screen.getByLabelText('Ny placering') as HTMLSelectElement
    fireEvent.change(select, { target: { value: 'MIN02' } })
    fireEvent.submit(select.closest('form')!)
    await waitFor(() => {
      expect(screen.getByText(/Ugyldig placering/)).toBeDefined()
    })
  })

  it('maps a 422 to an inline "aktivt ministeransvarsområde" error', async () => {
    moveOrganization.mockRejectedValueOnce(structureError(422, 'semantic'))
    renderPage()
    fireEvent.click(screen.getByTestId('action-flyt-STY01'))
    const select = screen.getByLabelText('Ny placering') as HTMLSelectElement
    fireEvent.change(select, { target: { value: 'MIN02' } })
    fireEvent.submit(select.closest('form')!)
    await waitFor(() => {
      expect(screen.getByText(/aktivt ministeransvarsområde/)).toBeDefined()
    })
  })
})

describe('OrganisationPage — delete dialog (3 branches)', () => {
  it('an Organisation WITH employees opens the BLOCKED branch with the count + a single Luk', () => {
    renderPage()
    fireEvent.click(screen.getByTestId('action-slet-STY01')) // 30 employees
    expect(screen.getByText('Kan ikke slette')).toBeDefined()
    expect(screen.getByText(/indeholder 30 medarbejdere/)).toBeDefined()
    expect(screen.getByRole('button', { name: 'Luk' })).toBeDefined()
    // No destructive confirm in the blocked branch.
    expect(screen.queryByTestId('confirm-delete')).toBeNull()
  })

  it('an empty MAO opens the empty-delete branch titled for a MAO', async () => {
    renderPage()
    fireEvent.click(screen.getByTestId('action-slet-MIN02')) // 0 employees
    expect(screen.getByText('Slet ministeransvarsområde?')).toBeDefined()
    fireEvent.click(screen.getByTestId('confirm-delete'))
    await waitFor(() => {
      expect(deleteOrganization).toHaveBeenCalledWith('MIN02')
    })
  })

  it('a server 422 on delete flips the dialog to the BLOCKED branch with the count', async () => {
    deleteOrganization.mockRejectedValueOnce(structureError(422, 'blocked', 7))
    renderPage()
    // STY02 has 12 employees → opens blocked already; use MIN02 (count 0) so the
    // FLIP path is exercised (optimistic empty → server says blocked).
    fireEvent.click(screen.getByTestId('action-slet-MIN02'))
    expect(screen.getByText('Slet ministeransvarsområde?')).toBeDefined()
    fireEvent.click(screen.getByTestId('confirm-delete'))
    await waitFor(() => {
      expect(screen.getByText('Kan ikke slette')).toBeDefined()
      expect(screen.getByText(/indeholder 7 medarbejdere/)).toBeDefined()
    })
  })

  it('the Enhed-delete dialog shows the untag copy AND the S100 children-reparent-up promise', async () => {
    renderPage()
    fireEvent.click(screen.getByRole('button', { name: 'Enhed' }))
    fireEvent.click(screen.getByTestId('action-slet-ENH01'))
    expect(screen.getByText('Slet enhed?')).toBeDefined()
    // The untag copy is present.
    expect(screen.getByText(/kun enhedsmærket fjernes/)).toBeDefined()
    // S100 INVERTS the S99 dead-copy guard: the children NOW re-parent up (the
    // underenheder are NOT deleted — they move up to the parent enhed).
    expect(screen.getByText(/underenheder slettes ikke/i)).toBeDefined()
    expect(screen.getByText(/flyttes op til/i)).toBeDefined()
    // Confirm resolves the version then deletes.
    fireEvent.click(screen.getByTestId('confirm-delete'))
    await waitFor(() => {
      expect(fetchEnheder).toHaveBeenCalledWith('STY01')
      expect(deleteEnhed).toHaveBeenCalledWith('ENH01', '"3"')
    })
  })
})

describe('OrganisationPage — Enhed move dialog (S100)', () => {
  it('the target select EXCLUDES self + descendants + current parent, offers root + siblings', async () => {
    renderPage()
    fireEvent.click(screen.getByRole('button', { name: 'Enhed' }))
    // Move ENH01 (root, has child ENH02). Valid targets: the "→ root" sentinel +
    // ENH03 (a sibling). EXCLUDED: ENH01 (self) + ENH02 (descendant).
    fireEvent.click(screen.getByTestId('action-flyt-ENH01'))
    expect(screen.getByRole('dialog', { name: 'Flyt enhed' })).toBeDefined()
    const select = screen.getByLabelText('Ny placering') as HTMLSelectElement
    const values = Array.from(select.options).map((o) => o.value)
    expect(values).toContain('__ROOT__')
    expect(values).toContain('ENH03')
    expect(values).not.toContain('ENH01') // self
    expect(values).not.toContain('ENH02') // descendant
  })

  it('moving an enhed under a sibling resolves the version then PUTs …/move', async () => {
    renderPage()
    fireEvent.click(screen.getByRole('button', { name: 'Enhed' }))
    fireEvent.click(screen.getByTestId('action-flyt-ENH03'))
    const select = screen.getByLabelText('Ny placering') as HTMLSelectElement
    // ENH03 is a leaf root → ENH01 is a valid target.
    fireEvent.change(select, { target: { value: 'ENH01' } })
    fireEvent.submit(select.closest('form')!)
    await waitFor(() => {
      expect(fetchEnheder).toHaveBeenCalledWith('STY01')
      // ENH03's resolved etag is "2".
      expect(moveEnhed).toHaveBeenCalledWith('ENH03', 'ENH01', '"2"')
    })
  })

  it('the "→ root" option moves the enhed to a root (newParentEnhedId null)', async () => {
    renderPage()
    fireEvent.click(screen.getByRole('button', { name: 'Enhed' }))
    fireEvent.click(screen.getByTestId('action-flyt-ENH02')) // a child of ENH01
    const select = screen.getByLabelText('Ny placering') as HTMLSelectElement
    fireEvent.change(select, { target: { value: '__ROOT__' } })
    fireEvent.submit(select.closest('form')!)
    await waitFor(() => {
      // ENH02's resolved etag is "1".
      expect(moveEnhed).toHaveBeenCalledWith('ENH02', null, '"1"')
    })
  })

  it('maps a 422 (cycle) to an inline error', async () => {
    moveEnhed.mockRejectedValueOnce(
      Object.assign(new Error('cycle'), { status: 422 }),
    )
    renderPage()
    fireEvent.click(screen.getByRole('button', { name: 'Enhed' }))
    fireEvent.click(screen.getByTestId('action-flyt-ENH03'))
    const select = screen.getByLabelText('Ny placering') as HTMLSelectElement
    fireEvent.change(select, { target: { value: 'ENH01' } })
    fireEvent.submit(select.closest('form')!)
    await waitFor(() => {
      expect(screen.getByText(/underenheder/i)).toBeDefined()
    })
  })
})
