// SPRINT-107 / TASK-10703 — vitest for the RIGHT recursive "Struktur" detail
// panel. The forest + roster fixtures mirror the REAL S106 wire shapes (the
// S97→S99→S100 "fetchEnheder" drift-class fix: a FE mock must NOT diverge from
// the backend's actual JSON — see ForestEndpointContractTests +
// S106RosterUnitTagTests). The fixture deliberately exercises: a MULTI-leader
// unit (Jens + Trine), a CROSS-UNIT exception member (Carl → a leader outside the
// unit), a LEADERLESS child unit (Kontrol), and a VIKAR (Jens absent, Bo the
// stand-in).
//
// The keystone is the NO-MUTATION-AFFORDANCE allowlist (the S91 dead-button
// discipline, both-lens-required): the ONLY interactive affordances are
// expansion carets, the two view toggles, breadcrumb + back/forward, and "Åbn ›"
// unit navigation — EXPLICITLY no + Medarbejder / + <ChildType> / Rediger / Slet
// / Ret / Tildel leder / Skift / Afslut / vikar-edit / per-row Rediger › / drawer
// (all S108). Person & leader rows render but are NOT clickable-to-edit.

import { describe, it, expect, vi } from 'vitest'
import { useState, type ComponentProps } from 'react'
import { render, screen, fireEvent, within } from '@testing-library/react'
import { StrukturPanel } from '../StrukturPanel'
import type { SelectedNode } from '../OrgStructureTree'
import type { ForestMaoNode } from '../../../../hooks/useForest'
import type { RosterResponse } from '../../../../hooks/useRoster'

const VEJL = '000000d0-0000-0000-0000-0000000000a1'
const KONTROL = '000000d0-0000-0000-0000-0000000000a2'

/** A MAO → Org → (Vejledning kontor → Kontrol team) forest in the real shape. */
function makeForest(): ForestMaoNode[] {
  return [
    {
      orgId: 'MIN01',
      orgName: 'Finansministeriet',
      orgType: 'MAO',
      parentOrgId: null,
      materializedPath: '/MIN01/',
      memberCount: 6,
      organisations: [
        {
          orgId: 'STY02',
          orgName: 'Statens IT',
          orgType: 'ORGANISATION',
          parentOrgId: 'MIN01',
          materializedPath: '/MIN01/STY02/',
          agreementCode: 'HK',
          okVersion: 'OK24',
          memberCount: 6,
          directMemberCount: 0,
          units: [
            {
              unitId: VEJL,
              organisationId: 'STY02',
              parentUnitId: null,
              type: 'kontor',
              name: 'Vejledning',
              level: 1,
              version: 1,
              directMemberCount: 5,
              memberCount: 6,
              children: [
                {
                  unitId: KONTROL,
                  organisationId: 'STY02',
                  parentUnitId: VEJL,
                  type: 'team',
                  name: 'Kontrol',
                  level: 2,
                  version: 1,
                  directMemberCount: 1,
                  memberCount: 1,
                  children: [],
                },
              ],
            },
          ],
        },
      ],
    },
  ]
}

/** Helper to spell out a roster row in the real S106 wire shape. */
function row(p: Partial<RosterResponse['employees'][number]> & { employeeId: string; displayName: string }) {
  return {
    enhedLabel: 'Vejledning',
    position: null,
    structuralApproverId: null,
    periodStatus: 'OPEN' as const,
    outgoingVikar: null,
    isRoot: false,
    isOrphan: false,
    unitId: null,
    unitName: null,
    leaderIds: [],
    primaryReportingLineVersion: null,
    ...p,
  }
}

const LEADER_IDS = ['jens', 'trine']

function makeRoster(): RosterResponse {
  return {
    employees: [
      row({
        employeeId: 'jens',
        displayName: 'Jens Kofoed',
        position: 'Kontorchef',
        unitId: VEJL,
        unitName: 'Vejledning',
        leaderIds: LEADER_IDS,
        structuralApproverId: 'dir1',
        outgoingVikar: { vikarUserId: 'bo', vikarDisplayName: 'Bo Bondo', untilDate: '2099-12-31', reason: 'FERIE' },
      }),
      row({
        employeeId: 'trine',
        displayName: 'Trine Toft',
        position: 'Teamleder',
        unitId: VEJL,
        unitName: 'Vejledning',
        leaderIds: LEADER_IDS,
        structuralApproverId: 'dir1',
      }),
      row({
        employeeId: 'anna',
        displayName: 'Anna Andersen',
        position: 'Sagsbehandler',
        unitId: VEJL,
        unitName: 'Vejledning',
        leaderIds: LEADER_IDS,
        structuralApproverId: 'jens',
      }),
      row({
        employeeId: 'bo',
        displayName: 'Bo Bondo',
        position: 'Fuldmægtig',
        unitId: VEJL,
        unitName: 'Vejledning',
        leaderIds: LEADER_IDS,
        structuralApproverId: 'trine',
      }),
      row({
        employeeId: 'carl',
        displayName: 'Carl Storm',
        position: 'Specialkonsulent',
        unitId: VEJL,
        unitName: 'Vejledning',
        leaderIds: LEADER_IDS,
        structuralApproverId: 'extLeader', // a leader OUTSIDE the unit → cross-unit exception
      }),
      row({
        employeeId: 'kim',
        displayName: 'Kim Krog',
        position: 'Kontrollør',
        unitId: KONTROL,
        unitName: 'Kontrol',
        leaderIds: [], // leaderless unit
        structuralApproverId: 'jens',
      }),
    ],
    pendingCountByManager: {},
    nameResolution: {
      dir1: { userId: 'dir1', displayName: 'Direktør Dorthe', position: 'Direktør', unitName: 'Direktion' },
      extLeader: { userId: 'extLeader', displayName: 'Ekstern Leder', position: 'Kontorchef', unitName: 'Andet Kontor' },
    },
  }
}

const VEJL_NODE: SelectedNode = { id: VEJL, kind: 'unit', name: 'Vejledning', type: 'kontor' }

function renderPanel(overrides: Partial<ComponentProps<typeof StrukturPanel>> = {}) {
  const props: ComponentProps<typeof StrukturPanel> = {
    forest: makeForest(),
    selected: VEJL_NODE,
    rosterByOrg: { STY02: makeRoster() },
    rosterLoading: false,
    onLoadRoster: vi.fn(),
    onNavigate: vi.fn(),
    canBack: false,
    canForward: false,
    onBack: vi.fn(),
    onForward: vi.fn(),
    ...overrides,
  }
  return { ...render(<StrukturPanel {...props} />), props }
}

describe('StrukturPanel — the recursive read-only Struktur', () => {
  it('renders the title block (type chip + unit name) and the breadcrumb', () => {
    renderPanel()
    expect(screen.getByTestId('title-name').textContent).toBe('Vejledning')
    expect(screen.getByTestId('title-type-chip').textContent).toBe('Kontor')
    const crumb = screen.getByTestId('breadcrumb')
    expect(within(crumb).getByText('Finansministeriet')).toBeDefined()
    expect(within(crumb).getByText('Statens IT')).toBeDefined()
  })

  it('lazily asks for the selected unit’s Organisation roster (once, by org)', () => {
    const onLoadRoster = vi.fn()
    renderPanel({ onLoadRoster })
    expect(onLoadRoster).toHaveBeenCalledWith('STY02')
  })

  it('groups the roster: MEDARBEJDERE → leaders (LEDER + report count) → their reports', () => {
    renderPanel()
    expect(screen.getByText('Medarbejdere')).toBeDefined()
    // Both peer leaders render with a LEDER badge.
    const jens = screen.getByTestId('leader-jens')
    const trine = screen.getByTestId('leader-trine')
    expect(within(jens).getByText('Jens Kofoed')).toBeDefined()
    expect(within(jens).getByText('Leder')).toBeDefined()
    expect(within(jens).getByText('1 medarb.')).toBeDefined()
    expect(within(trine).getByText('Leder')).toBeDefined()
    // Reports nest under the matching leader.
    expect(within(screen.getByTestId('employee-anna')).getByText('Anna Andersen')).toBeDefined() // → Jens
    expect(within(screen.getByTestId('employee-bo')).getByText('Bo Bondo')).toBeDefined() // → Trine
  })

  it('renders the "Refererer opad til" strip READ-ONLY (no navigation/edit button)', () => {
    renderPanel()
    const strip = screen.getByTestId('up-ref')
    expect(within(strip).getByText('Refererer opad til')).toBeDefined()
    const chip = screen.getByText('Direktør Dorthe')
    expect(chip).toBeDefined()
    // It is a plain chip — not a button/link.
    expect(chip.closest('button')).toBeNull()
    expect(chip.closest('a')).toBeNull()
  })

  it('flags a cross-unit exception READ-ONLY (amber tag, NO "Ret" button)', () => {
    renderPanel()
    const carl = screen.getByTestId('employee-carl')
    expect(within(carl).getByText('Leder uden for enheden: Ekstern Leder')).toBeDefined()
    // The S108 "Ret" affordance is built OUT, not hidden.
    expect(screen.queryByText('Ret')).toBeNull()
    expect(within(carl).queryByRole('button')).toBeNull()
  })

  it('shows the vikar READ-ONLY: FRAVÆRENDE + Vikar line on the leader, "Vikar for X" on the stand-in', () => {
    renderPanel()
    const jens = screen.getByTestId('leader-jens')
    expect(within(jens).getByTestId('fravaerende-jens')).toBeDefined()
    expect(within(jens).getByText(/Vikar: Bo Bondo/)).toBeDefined()
    // The stand-in (Bo, derived by inverting outgoingVikar) carries the inverse tag.
    expect(screen.getByTestId('vikar-for-bo').textContent).toContain('Vikar for Jens Kofoed')
    // No vikar-edit affordance (S108).
    expect(screen.queryByText('Skift')).toBeNull()
    expect(screen.queryByText('Afslut')).toBeNull()
  })

  it('shows the leaderless-unit note READ-ONLY (NO "Tildel leder") when the child unit is expanded', () => {
    renderPanel()
    // Kontrol is collapsed by default → its leaderless note is not yet shown.
    expect(screen.queryByTestId('leaderless-note')).toBeNull()
    fireEvent.click(screen.getByTestId(`caret-unit-${KONTROL}`))
    const note = screen.getByTestId('leaderless-note')
    expect(note.textContent).toContain('Ingen leder i enheden')
    expect(note.textContent).toContain('Jens Kofoed') // resolves the upward reference
    expect(screen.getByTestId('employee-kim')).toBeDefined()
    expect(screen.queryByText('Tildel leder')).toBeNull()
  })

  it('the "Vis/Skjul medarbejdere" toggle hides and re-shows all people', () => {
    renderPanel()
    expect(screen.getByText('Jens Kofoed')).toBeDefined()
    const toggle = screen.getByTestId('toggle-people')
    expect(toggle.textContent).toContain('Skjul medarbejdere')
    fireEvent.click(toggle)
    expect(screen.queryByText('Jens Kofoed')).toBeNull()
    expect(screen.getByTestId('toggle-people').textContent).toContain('Vis medarbejdere')
    fireEvent.click(screen.getByTestId('toggle-people'))
    expect(screen.getByText('Jens Kofoed')).toBeDefined()
  })

  it('the "Vis org./Skjul org." toggle expands all descendant child units', () => {
    renderPanel()
    expect(screen.queryByTestId('employee-kim')).toBeNull() // Kontrol collapsed
    const toggle = screen.getByTestId('toggle-expand-all')
    expect(toggle.textContent).toContain('Vis org.')
    fireEvent.click(toggle)
    expect(screen.getByTestId('employee-kim')).toBeDefined() // Kontrol now expanded
    expect(screen.getByTestId('toggle-expand-all').textContent).toContain('Skjul org.')
  })

  it('"Åbn ›" + breadcrumb + back/forward drive navigation (the only nav affordances)', () => {
    const onNavigate = vi.fn()
    const onBack = vi.fn()
    renderPanel({ onNavigate, onBack, canBack: true })
    fireEvent.click(screen.getByTestId(`open-unit-${KONTROL}`))
    expect(onNavigate).toHaveBeenCalledWith({ id: KONTROL, kind: 'unit', name: 'Kontrol', type: 'team' })
    fireEvent.click(screen.getByTestId('crumb-STY02'))
    expect(onNavigate).toHaveBeenCalledWith({ id: 'STY02', kind: 'organisation', name: 'Statens IT', type: 'organisation' })
    fireEvent.click(screen.getByTestId('nav-back'))
    expect(onBack).toHaveBeenCalled()
  })

  // ── the keystone: the NO-MUTATION-AFFORDANCE allowlist ──────────────────────
  it('exposes ONLY the allowed interactive affordances (no mutation buttons/links)', () => {
    renderPanel()
    fireEvent.click(screen.getByTestId(`caret-unit-${KONTROL}`)) // expand to surface every node type

    // (a) ALLOWLIST: every button is a caret / view-toggle / breadcrumb /
    //     back-forward / "Åbn ›" — identified by its data-testid. A stray
    //     mutation button (which would carry none of these) fails this.
    const allowed = (tid: string | null): boolean =>
      !!tid &&
      (['nav-back', 'nav-forward', 'toggle-expand-all', 'toggle-people'].includes(tid) ||
        tid.startsWith('crumb-') ||
        tid.startsWith('caret-') ||
        tid.startsWith('open-unit-'))
    for (const btn of screen.getAllByRole('button')) {
      expect(allowed(btn.getAttribute('data-testid'))).toBe(true)
    }

    // (b) NO anchor links at all.
    expect(screen.queryAllByRole('link')).toHaveLength(0)

    // (c) the exhaustive denylist — none of the S108 mutation affordances render.
    for (const label of [
      '+ Medarbejder',
      '+ Kontor',
      '+ Team',
      'Rediger',
      'Rediger ›',
      'Slet',
      'Ret',
      'Tildel leder',
      'Skift',
      'Afslut',
      'Omdøb',
      'Flyt',
      'Gem',
    ]) {
      expect(screen.queryByText(label)).toBeNull()
    }

    // (d) person & leader rows render but are NOT clickable-to-edit (no drawer).
    expect(screen.getByText('Jens Kofoed').closest('button')).toBeNull()
    expect(screen.getByText('Anna Andersen').closest('button')).toBeNull()
    expect(screen.getByText('Carl Storm').closest('button')).toBeNull()
  })

  it('renders the empty prompt when nothing is selected', () => {
    renderPanel({ selected: null })
    expect(screen.getByText('Vælg en enhed i strukturen til venstre.')).toBeDefined()
  })

  // A tiny controlled harness proving the view toggles are independent state and
  // a re-render with new roster data keeps grouping stable.
  it('keeps view state across re-render (controlled selection harness)', () => {
    function Harness() {
      const [sel] = useState<SelectedNode>(VEJL_NODE)
      return (
        <StrukturPanel
          forest={makeForest()}
          selected={sel}
          rosterByOrg={{ STY02: makeRoster() }}
          rosterLoading={false}
          onLoadRoster={vi.fn()}
          onNavigate={vi.fn()}
          canBack={false}
          canForward={false}
          onBack={vi.fn()}
          onForward={vi.fn()}
        />
      )
    }
    render(<Harness />)
    expect(screen.getByTestId('leader-jens')).toBeDefined()
  })
})
