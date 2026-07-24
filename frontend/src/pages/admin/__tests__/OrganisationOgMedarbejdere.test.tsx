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
import { ToastProvider } from '../../../components/ui/Toast'
import type { ForestMaoNode } from '../../../hooks/useForest'
import type { RosterResponse } from '../../../hooks/useRoster'
import type { SearchResponse } from '../../../hooks/useSearch'

// ── mocks (mutable holders the mocked hooks read) ──────────────────────────────
const h = vi.hoisted(() => ({
  forest: [] as ForestMaoNode[],
  search: { query: '', results: { units: [], people: [], unitsTotal: 0, peopleTotal: 0 } as SearchResponse, loading: false },
  // S123 T2 — the per-org roster cache (byOrg) the happy/not-found path reads.
  roster: {} as Record<string, RosterResponse>,
}))

// S123 T2 — opening the searched person's edit drawer invokes fetchUser (the
// panel's inlined openEditPerson). Mock useOrgUsers so it PENDS (never resolves):
// the drawer opens in its loading state and the subtree stays offline (editUser
// null → create-mode render → LifecycleSections resolve effect early-returns).
const admin = vi.hoisted(() => ({ fetchUser: vi.fn(() => new Promise<never>(() => {})) }))
vi.mock('../../../hooks/useAdmin', () => ({
  useOrgUsers: () => ({ fetchUser: admin.fetchUser }),
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
  useRoster: () => ({ byOrg: h.roster, loading: false, error: null, loadRoster: vi.fn(), refetchRoster: vi.fn() }),
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

const VEJL = '000000d0-0000-0000-0000-0000000000a1'

/** STY02 gains one unit (Vejledning) so a homed person's row can render + reveal. */
function forestWithUnit(): ForestMaoNode[] {
  const f = twoOrgForest()
  f[0].organisations[0].units = [
    {
      unitId: VEJL, organisationId: 'STY02', parentUnitId: null, type: 'kontor',
      name: 'Vejledning', level: 1, version: 1, directMemberCount: 1, memberCount: 1, children: [],
    },
  ]
  f[0].organisations[0].memberCount = 1
  f[0].memberCount = 1
  return f
}

/** STY02 roster with p1 homed in Vejledning (the search target). */
function rosterWithP1(): Record<string, RosterResponse> {
  return {
    STY02: {
      employees: [
        {
          employeeId: 'p1', displayName: 'Jens Vej', position: 'Kontorchef',
          structuralApproverId: null, periodStatus: 'OPEN', outgoingVikar: null,
          isRoot: false, isOrphan: false, unitId: VEJL, unitName: 'Vejledning',
          leaderIds: [], primaryReportingLineVersion: null,
        },
      ],
      pendingCountByManager: {},
      nameResolution: {},
    },
  }
}

beforeEach(() => {
  h.forest = twoOrgForest()
  h.search = { query: '', results: { units: [], people: [], unitsTotal: 0, peopleTotal: 0 }, loading: false }
  h.roster = {}
  auth.role = 'LocalHR'
  admin.fetchUser.mockClear()
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

  // S123 T2 — a person NOT in the loaded roster (moved/stale/cross-org) hits the
  // terminal not-found branch: land on the org, no drawer, no throw. (Repurposes the
  // former "navigate, no drawer" test now that a found person DOES open the drawer.)
  it('a person NOT in the loaded roster lands on the org with NO drawer (not-found, no throw)', () => {
    // STY02's roster IS loaded but does not contain p1 → terminal not-found.
    h.roster = { STY02: { employees: [], pendingCountByManager: {}, nameResolution: {} } }
    h.search = {
      query: 'jens',
      loading: false,
      results: {
        units: [],
        people: [
          { userId: 'p1', organisationId: 'STY02', displayName: 'Jens Vej', position: 'Kontorchef', unitName: 'Vejledning', path: ['Statens IT', 'Vejledning'] },
        ],
        unitsTotal: 0,
        peopleTotal: 1,
      },
    }
    render(<OrganisationOgMedarbejdere />)
    fireEvent.click(screen.getByTestId('soeg-button'))
    fireEvent.click(screen.getByTestId('search-person-p1'))
    // The overlay closed + the panel navigated to the person's Organisation…
    expect(screen.queryByTestId('search-overlay')).toBeNull()
    expect(screen.getByTestId('title-name').textContent).toBe('Statens IT')
    // …but NO edit drawer opened (the person is absent from the loaded roster).
    expect(screen.queryByTestId('person-drawer-title')).toBeNull()
    expect(screen.queryByTestId('person-drawer-loading')).toBeNull()
  })

  // S123 T2 — the happy path: a person result navigates to their org, REVEALS their
  // row in place, and opens their edit drawer (loading while the fresh user pends).
  it('a person result reveals the row + opens their edit drawer (S123 T2 happy path)', () => {
    h.forest = forestWithUnit()
    h.roster = rosterWithP1()
    h.search = {
      query: 'jens',
      loading: false,
      results: {
        units: [],
        people: [
          { userId: 'p1', organisationId: 'STY02', displayName: 'Jens Vej', position: 'Kontorchef', unitName: 'Vejledning', path: ['Statens IT', 'Vejledning'] },
        ],
        unitsTotal: 0,
        peopleTotal: 1,
      },
    }
    render(
      <ToastProvider>
        <OrganisationOgMedarbejdere />
      </ToastProvider>,
    )
    fireEvent.click(screen.getByTestId('soeg-button'))
    fireEvent.click(screen.getByTestId('search-person-p1'))
    // The overlay closed + the panel navigated to the person's Organisation…
    expect(screen.queryByTestId('search-overlay')).toBeNull()
    expect(screen.getByTestId('title-name').textContent).toBe('Statens IT')
    // …the reveal expands the org so the person's row is visible…
    expect(screen.getByTestId('employee-p1')).toBeDefined()
    // …and their edit drawer opens (loading while the fresh user pends via fetchUser).
    expect(screen.getByTestId('person-drawer-loading')).toBeDefined()
    expect(screen.getByTestId('person-drawer-title')).toBeDefined()
    expect(admin.fetchUser).toHaveBeenCalledWith('p1')
  })
})
