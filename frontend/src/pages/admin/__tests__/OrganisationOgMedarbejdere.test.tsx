// SPRINT-107 — vitest for the merged "Organisation & medarbejdere" page.
//
// TASK-10701 shipped the shell; TASK-10702/10703 the tree + Struktur; TASK-10704
// (this) wires the Afgrænsning scope popover + the search overlay. useForest /
// useRoster / useSearch are mocked (deterministic + offline) with the REAL S106
// wire shapes. The page-level assertions are the INTEGRATION the unit tests can't
// see: the Afgrænsning narrows the tree + RECOMPUTES the MAO roll-up count; the
// `/` shortcut + Søg button open the overlay; a search result NAVIGATES the panel
// (and closes the overlay) — read-only, no mutation affordances (S91).
import { describe, it, expect, vi, beforeEach } from 'vitest'
import { render, screen, fireEvent } from '@testing-library/react'
import type { ForestMaoNode } from '../../../hooks/useForest'
import type { SearchResponse } from '../../../hooks/useSearch'

// ── mocks (mutable holders the mocked hooks read) ──────────────────────────────
const h = vi.hoisted(() => ({
  forest: [] as ForestMaoNode[],
  search: { query: '', results: { units: [], people: [] } as SearchResponse, loading: false },
}))

// SPRINT-108 / TASK-10803 — the page + StrukturPanel now consume useAuth (the
// capability spine) + useToast; both throw outside their providers. A parametrized
// role mock (default LocalHR = permitting) + a no-op toast keep the suite offline.
const auth = vi.hoisted(() => ({ role: 'LocalHR' as string | null }))
vi.mock('../../../contexts/AuthContext', () => ({
  useAuth: () => ({ role: auth.role }),
}))
vi.mock('../../../components/ui', async (importOriginal) => {
  const actual = await importOriginal<typeof import('../../../components/ui')>()
  return { ...actual, useToast: () => ({ toast: vi.fn() }) }
})

vi.mock('../../../hooks/useForest', () => ({
  useForest: () => ({ forest: h.forest, loading: false, error: null, fetchForest: vi.fn() }),
}))
vi.mock('../../../hooks/useRoster', () => ({
  useRoster: () => ({ byOrg: {}, loading: false, error: null, loadRoster: vi.fn(), refetchRoster: vi.fn() }),
}))
vi.mock('../../../hooks/useSearch', () => ({
  useSearch: () => ({ query: h.search.query, setQuery: vi.fn(), results: h.search.results, loading: h.search.loading, error: null }),
}))

import { OrganisationOgMedarbejdere } from '../OrganisationOgMedarbejdere'

/** A MAO (MIN01) with two orgs: STY02 (30) + STY03 (20) → MAO roll-up 50. */
function twoOrgForest(): ForestMaoNode[] {
  return [
    {
      orgId: 'MIN01',
      orgName: 'Finansministeriet',
      orgType: 'MAO',
      parentOrgId: null,
      materializedPath: '/MIN01/',
      memberCount: 50,
      organisations: [
        {
          orgId: 'STY02', orgName: 'Statens IT', orgType: 'ORGANISATION', parentOrgId: 'MIN01',
          materializedPath: '/MIN01/STY02/', agreementCode: 'HK', okVersion: 'OK24',
          memberCount: 30, directMemberCount: 0, units: [],
        },
        {
          orgId: 'STY03', orgName: 'Statens Indkøb', orgType: 'ORGANISATION', parentOrgId: 'MIN01',
          materializedPath: '/MIN01/STY03/', agreementCode: 'AC', okVersion: 'OK24',
          memberCount: 20, directMemberCount: 0, units: [],
        },
      ],
    },
  ]
}

beforeEach(() => {
  h.forest = twoOrgForest()
  h.search = { query: '', results: { units: [], people: [] }, loading: false }
  auth.role = 'LocalHR'
})

describe('OrganisationOgMedarbejdere — page (shell + Afgrænsning + search)', () => {
  it('renders the header logo, title and subtitle', () => {
    render(<OrganisationOgMedarbejdere />)
    expect(screen.getByText('Organisation & medarbejdere')).toBeDefined()
    expect(screen.getByText('Enhedsspor — organisationen er rygraden')).toBeDefined()
    expect(screen.getByText('St')).toBeDefined()
  })

  it('renders the three regions (header + left tree + right detail)', () => {
    render(<OrganisationOgMedarbejdere />)
    expect(screen.getByText('ORGANISATIONSSTRUKTUR')).toBeDefined()
    expect(screen.getByTestId('tree-placeholder')).toBeDefined()
    expect(screen.getByTestId('detail-placeholder')).toBeDefined()
  })

  it('the Afgrænsning + Søg controls are now LIVE (the placeholders are wired)', () => {
    render(<OrganisationOgMedarbejdere />)
    const afg = screen.getByTestId('afgraensning-trigger') as HTMLButtonElement
    const soeg = screen.getByTestId('soeg-button') as HTMLButtonElement
    expect(afg.disabled).toBe(false)
    expect(soeg.disabled).toBe(false)
    expect(screen.getByTestId('afgraensning-summary').textContent).toBe('Alle organisationer')
  })

  it('reveals the gated UNIT structure affordance on select; the PEOPLE surface stays absent (S109)', () => {
    render(<OrganisationOgMedarbejdere />)
    // S108 inversion: with nothing selected there is no action row…
    expect(screen.queryByTestId('unit-action-row')).toBeNull()
    // …selecting the Organisation STY02 reveals "+ Direktion" (create a top-level
    // unit) under the permitting LocalHR role.
    fireEvent.click(screen.getByTestId('tree-row-STY02'))
    expect(screen.getByTestId('unit-action-create').textContent).toContain('Direktion')
    // the PEOPLE-mutation surface stays absent (those are S109).
    for (const re of [/\+\s*Medarbejder/, /Tildel leder/, /^Ret$/, /Skift/, /Afslut/]) {
      expect(screen.queryAllByText(re)).toHaveLength(0)
    }
  })

  it('gates the structure affordances: a below-floor role sees none', () => {
    auth.role = 'Employee'
    render(<OrganisationOgMedarbejdere />)
    fireEvent.click(screen.getByTestId('tree-row-STY02'))
    expect(screen.queryByTestId('unit-action-row')).toBeNull()
    expect(screen.queryByTestId('unit-action-create')).toBeNull()
  })

  it('the top-level "+ Ministerområde" is GlobalAdmin-gated (TASK-10802)', () => {
    // GlobalAdmin sees it in the tree header…
    auth.role = 'GlobalAdmin'
    const { unmount } = render(<OrganisationOgMedarbejdere />)
    expect(screen.getByTestId('mao-create-button')).toBeDefined()
    unmount()
    // …a LocalAdmin does NOT (MAO-create is GlobalAdmin-only)…
    auth.role = 'LocalAdmin'
    const second = render(<OrganisationOgMedarbejdere />)
    expect(screen.queryByTestId('mao-create-button')).toBeNull()
    second.unmount()
    // …nor does a LocalHR.
    auth.role = 'LocalHR'
    render(<OrganisationOgMedarbejdere />)
    expect(screen.queryByTestId('mao-create-button')).toBeNull()
  })

  it('the Afgrænsning narrows the tree AND recomputes the MAO roll-up count', () => {
    render(<OrganisationOgMedarbejdere />)
    // Before: both orgs visible; the MAO roll-up is the full 50.
    expect(screen.getByTestId('tree-row-STY02')).toBeDefined()
    expect(screen.getByTestId('tree-row-STY03')).toBeDefined()
    expect(screen.getByTestId('tree-count-MIN01').textContent).toBe('50')

    // Deselect STY03 in the popover and apply.
    fireEvent.click(screen.getByTestId('afgraensning-trigger'))
    fireEvent.click(screen.getByTestId('afg-org-STY03'))
    fireEvent.click(screen.getByTestId('afg-apply'))

    // After: STY03 dropped from the tree; the MAO count RECOMPUTES to 30 (not 50).
    expect(screen.queryByTestId('tree-row-STY03')).toBeNull()
    expect(screen.getByTestId('tree-row-STY02')).toBeDefined()
    expect(screen.getByTestId('tree-count-MIN01').textContent).toBe('30')
    expect(screen.getByTestId('afgraensning-summary').textContent).toBe('1 organisation')
  })

  it('the Søg button opens the overlay; Esc closes it', () => {
    render(<OrganisationOgMedarbejdere />)
    expect(screen.queryByTestId('search-overlay')).toBeNull()
    fireEvent.click(screen.getByTestId('soeg-button'))
    expect(screen.getByTestId('search-overlay')).toBeDefined()
    fireEvent.keyDown(document, { key: 'Escape' })
    expect(screen.queryByTestId('search-overlay')).toBeNull()
  })

  it('the `/` shortcut opens the overlay (when not typing in a field)', () => {
    render(<OrganisationOgMedarbejdere />)
    expect(screen.queryByTestId('search-overlay')).toBeNull()
    fireEvent.keyDown(document.body, { key: '/' })
    expect(screen.getByTestId('search-overlay')).toBeDefined()
  })

  it('a search result NAVIGATES the panel (and closes the overlay) — no drawer', () => {
    // A person result in STY02 → navigates to that Organisation.
    h.search = {
      query: 'jens',
      loading: false,
      results: {
        units: [],
        people: [
          { userId: 'p1', organisationId: 'STY02', displayName: 'Jens Vej', position: 'Kontorchef', unitName: 'Vejledning', path: ['Statens IT', 'Vejledning'] },
        ],
      },
    }
    render(<OrganisationOgMedarbejdere />)
    fireEvent.click(screen.getByTestId('soeg-button'))
    fireEvent.click(screen.getByTestId('search-person-p1'))
    // The overlay closed…
    expect(screen.queryByTestId('search-overlay')).toBeNull()
    // …and the detail panel now shows the navigated Organisation (resolved from the forest).
    expect(screen.getByTestId('title-name').textContent).toBe('Statens IT')
  })
})
