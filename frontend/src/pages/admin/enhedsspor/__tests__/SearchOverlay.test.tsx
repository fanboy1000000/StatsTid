// SPRINT-107 / TASK-10704 — vitest for the SEARCH overlay.
//
// useSearch is mocked with the REAL S106 wire shape (units + people each carrying
// `organisationId` + `path`) — the S97→S99→S100 "fetchEnheder" drift-class fix: a
// FE test mock must NOT diverge from the backend's actual JSON (see
// SearchEndpointContractTests). Asserts: the two ENHEDER + MEDARBEJDERE sections
// with green count pills; a result row NAVIGATES the tree/panel ONLY (no edit
// drawer — S108); the results RESPECT the Afgrænsning (an out-of-org result is
// filtered out via `organisationId`, never the path text) + the footer note; Esc /
// scrim close; the idle + no-results states.
import { describe, it, expect, vi, beforeEach } from 'vitest'
import { render, screen, fireEvent, within } from '@testing-library/react'
import type { SearchResponse } from '../../../../hooks/useSearch'

// Mutable holder the mocked useSearch reads — each test sets query/results/loading.
const h = vi.hoisted(() => ({
  query: 'vej',
  results: { units: [], people: [] } as SearchResponse,
  loading: false,
  setQuery: undefined as unknown as ReturnType<typeof vi.fn>,
}))

vi.mock('../../../../hooks/useSearch', () => ({
  useSearch: () => ({ query: h.query, setQuery: h.setQuery, results: h.results, loading: h.loading, error: null }),
}))

import { SearchOverlay } from '../SearchOverlay'

/** Two orgs (STY02, STY03), one unit + one person in each — real wire shape. */
function realResults(): SearchResponse {
  return {
    units: [
      { unitId: 'u1', organisationId: 'STY02', type: 'kontor', name: 'Vejledning', path: ['Statens IT'] },
      { unitId: 'u2', organisationId: 'STY03', type: 'team', name: 'Vejteam', path: ['Statens Indkøb', 'Drift'] },
    ],
    people: [
      { userId: 'p1', organisationId: 'STY02', displayName: 'Jens Vej', position: 'Kontorchef', unitName: 'Vejledning', path: ['Statens IT', 'Vejledning'] },
      { userId: 'p2', organisationId: 'STY03', displayName: 'Per Vester', position: 'Konsulent', unitName: null, path: ['Statens Indkøb'] },
    ],
  }
}

const ALL_ORGS = ['STY02', 'STY03']

function renderOverlay(props: Partial<Parameters<typeof SearchOverlay>[0]> = {}) {
  const onClose = vi.fn()
  const onNavigate = vi.fn()
  render(
    <SearchOverlay
      open
      onClose={onClose}
      onNavigate={onNavigate}
      selected={null}
      allOrgIds={ALL_ORGS}
      {...props}
    />,
  )
  return { onClose, onNavigate }
}

beforeEach(() => {
  h.query = 'vej'
  h.results = realResults()
  h.loading = false
  h.setQuery = vi.fn()
})

describe('SearchOverlay — the read-only search palette', () => {
  it('renders the two ENHEDER + MEDARBEJDERE sections with green count pills', () => {
    renderOverlay()
    expect(within(screen.getByTestId('search-section-enheder')).getByText('Enheder')).toBeDefined()
    expect(screen.getByTestId('search-section-enheder-count').textContent).toBe('2')
    expect(within(screen.getByTestId('search-section-medarbejdere')).getByText('Medarbejdere')).toBeDefined()
    expect(screen.getByTestId('search-section-medarbejdere-count').textContent).toBe('2')
    // Unit rows carry name + path; person rows carry name + title + path.
    expect(within(screen.getByTestId('search-unit-u1')).getByText('Vejledning')).toBeDefined()
    expect(screen.getByTestId('search-unit-u1').textContent).toContain('Statens IT')
    expect(within(screen.getByTestId('search-person-p1')).getByText('Jens Vej')).toBeDefined()
    expect(screen.getByTestId('search-person-p1').textContent).toContain('Kontorchef')
  })

  it('a UNIT result navigates to that unit and closes (no drawer)', () => {
    const { onNavigate, onClose } = renderOverlay()
    fireEvent.click(screen.getByTestId('search-unit-u1'))
    expect(onNavigate).toHaveBeenCalledWith({ id: 'u1', kind: 'unit', name: 'Vejledning', type: 'kontor' })
    expect(onClose).toHaveBeenCalled()
  })

  it('a PERSON result navigates to their ORGANISATION (no unitId in the shape) and closes', () => {
    const { onNavigate, onClose } = renderOverlay()
    fireEvent.click(screen.getByTestId('search-person-p1'))
    // path[0] is the OrganisationName per the contract → used as the nav node name.
    expect(onNavigate).toHaveBeenCalledWith({ id: 'STY02', kind: 'organisation', name: 'Statens IT', type: 'organisation' })
    expect(onClose).toHaveBeenCalled()
  })

  it('selecting NAVIGATES ONLY — no edit/mutation affordance renders (S108)', () => {
    renderOverlay()
    for (const label of ['Rediger', 'Rediger ›', 'Slet', 'Ret', 'Gem', '+ Medarbejder']) {
      expect(screen.queryByText(label)).toBeNull()
    }
    // The person row is a single navigation button, not a click-to-edit name link.
    expect(screen.getByText('Jens Vej').closest('a')).toBeNull()
  })

  it('RESPECTS the Afgrænsning: an out-of-org result is filtered out via organisationId + the footer note shows', () => {
    renderOverlay({ selected: new Set(['STY02']), allOrgIds: ALL_ORGS })
    // STY02 rows stay; STY03 rows are filtered out by organisationId (not path text).
    expect(screen.getByTestId('search-unit-u1')).toBeDefined()
    expect(screen.getByTestId('search-person-p1')).toBeDefined()
    expect(screen.queryByTestId('search-unit-u2')).toBeNull()
    expect(screen.queryByTestId('search-person-p2')).toBeNull()
    // The MEDARBEJDERE/ENHEDER counts reflect the narrowed set.
    expect(screen.getByTestId('search-section-enheder-count').textContent).toBe('1')
    expect(screen.getByTestId('search-section-medarbejdere-count').textContent).toBe('1')
    // The scope footer note shows when actively scoped.
    expect(screen.getByTestId('search-scope-note').textContent).toContain('Søgningen er begrænset til den valgte afgrænsning')
  })

  it('shows NO scope note when not narrowed (all selected)', () => {
    renderOverlay({ selected: null, allOrgIds: ALL_ORGS })
    expect(screen.queryByTestId('search-scope-note')).toBeNull()
  })

  it('Esc closes (and the Esc button + scrim click close too)', () => {
    const { onClose } = renderOverlay()
    fireEvent.keyDown(document, { key: 'Escape' })
    expect(onClose).toHaveBeenCalledTimes(1)
    fireEvent.click(screen.getByTestId('search-esc'))
    expect(onClose).toHaveBeenCalledTimes(2)
    fireEvent.click(screen.getByTestId('search-overlay')) // the scrim
    expect(onClose).toHaveBeenCalledTimes(3)
  })

  it('a fold caret collapses a section (its rows hide, the header stays)', () => {
    renderOverlay()
    expect(screen.getByTestId('search-unit-u1')).toBeDefined()
    fireEvent.click(screen.getByTestId('search-section-enheder'))
    expect(screen.queryByTestId('search-unit-u1')).toBeNull()
    // The MEDARBEJDERE section is unaffected (independent fold state).
    expect(screen.getByTestId('search-person-p1')).toBeDefined()
  })

  it('renders the idle hint for an empty query and the no-results message otherwise', () => {
    h.query = ''
    h.results = { units: [], people: [] }
    const { onClose } = renderOverlay()
    expect(screen.getByTestId('search-idle')).toBeDefined()
    expect(screen.queryByTestId('search-section-enheder')).toBeNull()
    onClose.mockClear()

    h.query = 'zzz'
    h.results = { units: [], people: [] }
    renderOverlay()
    expect(screen.getByTestId('search-no-results').textContent).toContain('Ingen enheder eller medarbejdere matcher')
  })
})
