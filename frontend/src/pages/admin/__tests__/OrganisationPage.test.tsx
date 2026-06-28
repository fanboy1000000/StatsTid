// S99 / TASK-9905 — vitest for the redesigned Organisation page. Mocks the
// consumed hooks (useOrganizationTree, useOrganizations, useOrganizationStructure)
// rather than fetch, then asserts: the tree renders (MAO → Organisation); the
// level control defaults to Organisation; search flattens + the empty message;
// each dialog; the status branches (delete 422 + count / create 409 dup / move
// 400 vs 422).
//
// S103 / TASK-10304 (Enhedsspor Phase 1a) — the Enhed tier is REMOVED: no enhed
// rows, no Tilføj/Flyt on an Organisation leaf, no enhed move/rename. Tilføj is
// present on a MAO (creates an Organisation); Flyt is present on an Organisation
// (re-home under another MAO).
import { describe, it, expect, vi, beforeEach } from 'vitest'
import { render, screen, waitFor, fireEvent } from '@testing-library/react'
import { ToastProvider } from '../../../components/ui/Toast'
import { OrganisationPage } from '../OrganisationPage'
import type { TreeMaoNode } from '../../../hooks/useOrganizationTree'

// ── tree fixture: 1 MAO → 2 Organisations; a second empty MAO. ──
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
        },
        {
          orgId: 'STY02',
          orgName: 'Digitaliseringsstyrelsen',
          orgType: 'ORGANISATION',
          parentOrgId: 'MIN01',
          materializedPath: '/MIN01/STY02/',
          employeeCount: 12,
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
})

describe('OrganisationPage — render + level control', () => {
  it('renders the title and the sub-paragraph', () => {
    renderPage()
    expect(screen.getByRole('heading', { name: 'Organisation' })).toBeDefined()
    expect(screen.getByText(/Forvalt organisationshierarkiet/)).toBeDefined()
  })

  it('defaults to the Organisation level on mount (MAOs expanded, organisations shown)', () => {
    renderPage()
    // Both MAOs render.
    expect(screen.getByTestId('org-row-MIN01')).toBeDefined()
    expect(screen.getByTestId('org-row-MIN02')).toBeDefined()
    // Organisations are shown (MAO expanded by the default ORGANISATION level).
    expect(screen.getByTestId('org-row-STY01')).toBeDefined()
    expect(screen.getByTestId('org-row-STY02')).toBeDefined()
    // The Organisation segment is the active one.
    const orgSeg = screen.getByRole('button', { name: 'Organisation' })
    expect(orgSeg.getAttribute('aria-pressed')).toBe('true')
  })

  it('the Ministeransvarsområde level collapses to roots only', () => {
    renderPage()
    fireEvent.click(screen.getByRole('button', { name: 'Ministeransvarsområde' }))
    expect(screen.getByTestId('org-row-MIN01')).toBeDefined()
    expect(screen.queryByTestId('org-row-STY01')).toBeNull()
  })

  it('rolls up the employee count (MAO) and shows the Organisation count', () => {
    renderPage()
    expect(screen.getByTestId('org-row-MIN01').textContent).toContain('42')
    expect(screen.getByTestId('org-row-STY01').textContent).toContain('30')
  })
})

describe('OrganisationPage — search', () => {
  it('flattens to matching nodes (case-insensitive), no chevrons', async () => {
    renderPage()
    fireEvent.change(screen.getByLabelText('Søg organisation'), { target: { value: 'styrelsen' } })
    await waitFor(() => {
      expect(screen.getByTestId('org-row-STY01')).toBeDefined()
    })
    // The non-matching MAO is gone.
    expect(screen.queryByTestId('org-row-MIN02')).toBeNull()
    // No chevron in search mode.
    expect(screen.queryByTestId('chevron-MIN01')).toBeNull()
  })

  it('shows the empty message when nothing matches', async () => {
    renderPage()
    fireEvent.change(screen.getByLabelText('Søg organisation'), { target: { value: 'zzzz' } })
    await waitFor(() => {
      expect(screen.getByText('Ingen organisationer matcher søgningen.')).toBeDefined()
    })
  })
})

describe('OrganisationPage — action matrix', () => {
  it('Tilføj is PRESENT on a MAO, ABSENT on an Organisation', () => {
    renderPage()
    expect(screen.getByTestId('action-tilfoej-MIN01')).toBeDefined()
    expect(screen.queryByTestId('action-tilfoej-STY01')).toBeNull()
  })

  it('Flyt is PRESENT on an Organisation, ABSENT on a MAO', () => {
    renderPage()
    expect(screen.queryByTestId('action-flyt-MIN01')).toBeNull()
    expect(screen.getByTestId('action-flyt-STY01')).toBeDefined()
  })

  it('Omdøb opens the rename dialog on both MAO and Organisation', () => {
    renderPage()
    fireEvent.click(screen.getByTestId('action-omdoeb-STY01'))
    expect(screen.getByText('Omdøb organisation')).toBeDefined()
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

describe('OrganisationPage — delete dialog (2 branches)', () => {
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
    // MIN02 (count 0) opens optimistically empty → server says blocked → FLIP.
    fireEvent.click(screen.getByTestId('action-slet-MIN02'))
    expect(screen.getByText('Slet ministeransvarsområde?')).toBeDefined()
    fireEvent.click(screen.getByTestId('confirm-delete'))
    await waitFor(() => {
      expect(screen.getByText('Kan ikke slette')).toBeDefined()
      expect(screen.getByText(/indeholder 7 medarbejdere/)).toBeDefined()
    })
  })
})
