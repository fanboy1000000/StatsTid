// SPRINT-107 / TASK-10703 — vitest for the RIGHT recursive "Struktur" detail
// panel. The forest + roster fixtures mirror the REAL S106 wire shapes (the
// S97→S99→S100 "fetchEnheder" drift-class fix: a FE mock must NOT diverge from
// the backend's actual JSON — see ForestEndpointContractTests +
// S106RosterUnitTagTests). The fixture deliberately exercises: a MULTI-leader
// unit (Jens + Trine), a CROSS-UNIT exception member (Carl → a leader outside the
// unit), a LEADERLESS child unit (Kontrol), and a VIKAR (Jens absent, Bo the
// stand-in).
//
// The keystone is the RE-ARCHITECTED affordance allowlist (the S91 dead-button
// discipline, both-lens-required): the interactive affordances are the expansion
// carets, the two view toggles, breadcrumb + back/forward, "Åbn ›" navigation, the
// gated UNIT structure actions (S108) and — NEW in S109 — the gated PEOPLE actions
// (+ Medarbejder on a unit + a per-row "Rediger ›"). The out-of-scope shortcuts
// stay absent: cross-unit "Ret" + leaderless "Tildel leder" (TASK-10903) and the
// inline approver/vikar ("Skift"/"Afslut" — those live inside the drawer).

import { describe, it, expect, vi, beforeEach } from 'vitest'
import { useState, type ComponentProps } from 'react'
import { render, screen, fireEvent, within, waitFor } from '@testing-library/react'

// SPRINT-108 / TASK-10803 — StrukturPanel now consumes useAuth (the capability
// spine) + useToast; both THROW outside their providers. A parametrized role mock
// (default LocalHR = permitting) drives the gating; useToast is a no-op spy.
const auth = vi.hoisted(() => ({ role: 'LocalHR' as string | null }))
vi.mock('../../../../contexts/AuthContext', () => ({
  useAuth: () => ({ role: auth.role }),
}))
vi.mock('../../../../components/ui', async (importOriginal) => {
  const actual = await importOriginal<typeof import('../../../../components/ui')>()
  return { ...actual, useToast: () => ({ toast: vi.fn() }) }
})

// SPRINT-109 / TASK-10903 — the cross-unit "Ret" fires POST /api/admin/reporting-
// lines via useReportingLines.assignManager. Spy on it so the tests can pin the
// body + the create-vs-supersede If-Match (the row's nullable etag).
const reportingLines = vi.hoisted(() => ({ assignManager: vi.fn() }))
vi.mock('../../../../hooks/useReportingLines', () => ({
  useReportingLines: () => ({ assignManager: reportingLines.assignManager }),
}))

import { StrukturPanel } from '../StrukturPanel'
import type { SelectedNode } from '../OrgStructureTree'
import type { ForestMaoNode } from '../../../../hooks/useForest'
import type { RosterResponse } from '../../../../hooks/useRoster'

beforeEach(() => {
  auth.role = 'LocalHR'
  reportingLines.assignManager.mockReset()
  reportingLines.assignManager.mockResolvedValue({ ok: true, data: { version: 7 } })
})

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

  it('flags a cross-unit exception with the amber tag AND the cross-unit "Ret" action (TASK-10903)', () => {
    renderPanel()
    const carl = screen.getByTestId('employee-carl')
    expect(within(carl).getByText('Leder uden for enheden: Ekstern Leder')).toBeDefined()
    // TASK-10903 — the cross-unit "Ret" is now PRESENT (the S107/S108 read-only
    // amber tag re-enabled as an action), alongside the generic "Rediger ›" drawer
    // entry. The row carries exactly those two affordances.
    expect(within(carl).getByTestId('ret-carl')).toBeDefined()
    expect(within(carl).getByText('Ret')).toBeDefined()
    const tids = within(carl)
      .getAllByRole('button')
      .map((b) => b.getAttribute('data-testid'))
      .sort()
    expect(tids).toEqual(['person-edit-carl', 'ret-carl'])
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

  it('shows the leaderless-unit note WITH the "Tildel leder" action when the child unit is expanded (TASK-10903)', () => {
    renderPanel()
    // Kontrol is collapsed by default → its leaderless note is not yet shown.
    expect(screen.queryByTestId('leaderless-note')).toBeNull()
    fireEvent.click(screen.getByTestId(`caret-unit-${KONTROL}`))
    const note = screen.getByTestId('leaderless-note')
    expect(note.textContent).toContain('Ingen leder i enheden')
    expect(note.textContent).toContain('Jens Kofoed') // resolves the upward reference
    expect(screen.getByTestId('employee-kim')).toBeDefined()
    // TASK-10903 — the note re-enables as an action ("Tildel leder" → the unit
    // edit drawer's Ledere checkboxes for THIS unit).
    expect(within(note).getByTestId(`assign-leader-${KONTROL}`)).toBeDefined()
    expect(within(note).getByText('Tildel leder')).toBeDefined()
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

  // ── the keystone: the RE-ARCHITECTED allowlist (S108 structure + S109 people) ─
  // The S107 allowlist asserted EVERY button was a caret/toggle/breadcrumb/open.
  // S108 added the gated UNIT structure buttons; S109 (this) adds the gated PEOPLE
  // buttons (+ Medarbejder on a unit + a per-row "Rediger ›"). The guard SURVIVES
  // for the affordances NOT in S109's TASK-10901/10902 scope: cross-unit "Ret" +
  // leaderless "Tildel leder" (TASK-10903) and the INLINE approver/vikar
  // ("Skift"/"Afslut" — those live inside the drawer, never on the panel).
  it('exposes the gated STRUCTURE + PEOPLE affordances (S109 inversion); the out-of-scope shortcuts stay absent', () => {
    renderPanel() // default role LocalHR (permitting)
    fireEvent.click(screen.getByTestId(`caret-unit-${KONTROL}`)) // surface every node type

    // (a) ALLOWLIST: caret / view-toggle / breadcrumb / back-forward / "Åbn ›" PLUS
    //     the four UNIT structure actions PLUS the people affordances (+ Medarbejder
    //     and the per-row "Rediger ›"). Any OTHER stray button fails.
    const STRUCTURE = ['unit-action-create', 'unit-action-edit', 'unit-action-move', 'unit-action-delete']
    const allowed = (tid: string | null): boolean =>
      !!tid &&
      (['nav-back', 'nav-forward', 'toggle-expand-all', 'toggle-people'].includes(tid) ||
        STRUCTURE.includes(tid) ||
        tid === 'person-action-create' ||
        tid.startsWith('person-edit-') ||
        // S109 TASK-10903 — the cross-unit "Ret" + leaderless "Tildel leder" actions.
        tid.startsWith('ret-') ||
        tid.startsWith('assign-leader-') ||
        tid.startsWith('crumb-') ||
        tid.startsWith('caret-') ||
        tid.startsWith('open-unit-'))
    for (const btn of screen.getAllByRole('button')) {
      expect(allowed(btn.getAttribute('data-testid'))).toBe(true)
    }

    // (b) the STRUCTURE surface is PRESENT (the inversion) — a kontor's child is a team.
    expect(screen.getByTestId('unit-action-create').textContent).toContain('Team')
    expect(screen.getByTestId('unit-action-edit')).toBeDefined()
    expect(screen.getByTestId('unit-action-move')).toBeDefined()
    expect(screen.getByTestId('unit-action-delete')).toBeDefined()

    // (c) the PEOPLE surface is now PRESENT (the S109 inversion) — "+ Medarbejder"
    //     on the unit + a per-row "Rediger ›" on leaders + employees + the S109
    //     TASK-10903 cross-unit "Ret" (Carl) + leaderless "Tildel leder" (Kontrol).
    expect(screen.getByTestId('person-action-create').textContent).toContain('Medarbejder')
    expect(screen.getByTestId('person-edit-jens')).toBeDefined() // a leader row
    expect(screen.getByTestId('person-edit-anna')).toBeDefined() // an employee row
    expect(screen.getByTestId('ret-carl')).toBeDefined() // the cross-unit exception
    expect(screen.getByTestId(`assign-leader-${KONTROL}`)).toBeDefined() // the leaderless unit

    // (d) the OUT-OF-SCOPE shortcuts stay absent — the inline drawer-only
    //     approver/vikar controls ("Skift"/"Afslut") never render on the panel.
    for (const label of ['Skift', 'Afslut']) {
      expect(screen.queryByText(label)).toBeNull()
    }
    expect(screen.queryAllByRole('link')).toHaveLength(0)

    // (e) person & leader NAMES are not themselves buttons — the edit entry is the
    //     dedicated "Rediger ›" affordance, not the name.
    expect(screen.getByText('Jens Kofoed').closest('button')).toBeNull()
    expect(screen.getByText('Anna Andersen').closest('button')).toBeNull()
    expect(screen.getByText('Carl Storm').closest('button')).toBeNull()
  })

  // ── the gating spine: a non-permitting role sees NO unit affordances ─────────
  it('hides the unit AND people affordances for a below-floor role (Employee)', () => {
    auth.role = 'Employee'
    renderPanel()
    expect(screen.queryByTestId('unit-action-row')).toBeNull()
    expect(screen.queryByTestId('unit-action-create')).toBeNull()
    expect(screen.queryByTestId('unit-action-edit')).toBeNull()
    // S109 — the people-mutation surface is gated at the same LocalHR floor.
    expect(screen.queryByTestId('person-action-create')).toBeNull()
    expect(screen.queryByTestId('person-edit-jens')).toBeNull()
    // S109 TASK-10903 — the cross-unit "Ret" + leaderless "Tildel leder" are gated too.
    expect(screen.queryByTestId('ret-carl')).toBeNull()
    expect(screen.queryByText('Ret')).toBeNull()
    fireEvent.click(screen.getByTestId(`caret-unit-${KONTROL}`)) // surface the leaderless note
    expect(screen.getByTestId('leaderless-note')).toBeDefined() // the READ-ONLY note still shows…
    expect(screen.queryByTestId(`assign-leader-${KONTROL}`)).toBeNull() // …but not the action
    expect(screen.queryByText('Tildel leder')).toBeNull()
    // …but the READ-ONLY view still renders.
    expect(screen.getByTestId('title-name').textContent).toBe('Vejledning')
    expect(screen.getByTestId('leader-jens')).toBeDefined()
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

// ── SPRINT-109 / TASK-10903 — cross-unit "Ret" + leaderless "Tildel leder" ───────
// A Vejledning roster with a SINGLE leader (Jens) and a cross-unit-exception member
// (Carl → extLeader, outside the unit). The single own-unit leader ⇒ "Ret" is
// one-click; `carlVersion` drives the create-vs-supersede etag.
function singleLeaderRoster(carlVersion: number | null): RosterResponse {
  return {
    employees: [
      row({
        employeeId: 'jens', displayName: 'Jens Kofoed', position: 'Kontorchef',
        unitId: VEJL, unitName: 'Vejledning', leaderIds: ['jens'], structuralApproverId: 'dir1',
      }),
      row({
        employeeId: 'carl', displayName: 'Carl Storm', position: 'Specialkonsulent',
        unitId: VEJL, unitName: 'Vejledning', leaderIds: ['jens'], structuralApproverId: 'extLeader',
        primaryReportingLineVersion: carlVersion,
      }),
    ],
    pendingCountByManager: {},
    nameResolution: {
      dir1: { userId: 'dir1', displayName: 'Direktør Dorthe', position: 'Direktør', unitName: 'Direktion' },
      extLeader: { userId: 'extLeader', displayName: 'Ekstern Leder', position: 'Kontorchef', unitName: 'Andet Kontor' },
    },
  }
}

describe('StrukturPanel — cross-unit "Ret" + leaderless "Tildel leder" (TASK-10903)', () => {
  it('"Ret" single own-unit leader → one-click POST with If-Match (SUPERSEDE) when the etag is non-null', async () => {
    const onMutated = vi.fn()
    renderPanel({ rosterByOrg: { STY02: singleLeaderRoster(3) }, onMutated })
    fireEvent.click(screen.getByTestId('ret-carl'))
    await waitFor(() => expect(reportingLines.assignManager).toHaveBeenCalledTimes(1))
    // The reassign targets the unit's OWN single leader (Jens), with the row's
    // active PRIMARY edge version as If-Match (supersede).
    expect(reportingLines.assignManager).toHaveBeenCalledWith(
      { employeeId: 'carl', managerId: 'jens', effectiveFrom: expect.any(String) },
      '"3"',
    )
    // No picker (single leader → one-click).
    expect(screen.queryByTestId('ret-picker-scrim')).toBeNull()
    // Refetch on success.
    await waitFor(() => expect(onMutated).toHaveBeenCalledWith('STY02'))
  })

  it('"Ret" single own-unit leader → one-click POST with If-None-Match:* (CREATE) when the etag is null', async () => {
    renderPanel({ rosterByOrg: { STY02: singleLeaderRoster(null) } })
    fireEvent.click(screen.getByTestId('ret-carl'))
    await waitFor(() => expect(reportingLines.assignManager).toHaveBeenCalledTimes(1))
    // A null primaryReportingLineVersion → no If-Match (the hook sends If-None-Match:*).
    expect(reportingLines.assignManager).toHaveBeenCalledWith(
      { employeeId: 'carl', managerId: 'jens', effectiveFrom: expect.any(String) },
      undefined,
    )
  })

  it('"Ret" with MULTIPLE peer leaders → the picker pre-filtered to the unit\'s OWN leaders, then the POST', async () => {
    // The default fixture: Vejledning has two peer leaders (Jens + Trine); Carl is
    // the cross-unit exception (→ extLeader). Several leaders ⇒ no auto-pick.
    renderPanel()
    fireEvent.click(screen.getByTestId('ret-carl'))
    // One-click did NOT fire — the picker opened instead.
    expect(reportingLines.assignManager).not.toHaveBeenCalled()
    expect(screen.getByTestId('ret-picker-scrim')).toBeDefined()
    // The options are EXACTLY the unit's own leaders (never an arbitrary candidate).
    expect(screen.getByTestId('ret-leader-option-jens')).toBeDefined()
    expect(screen.getByTestId('ret-leader-option-trine')).toBeDefined()
    expect(screen.queryByTestId('ret-leader-option-extLeader')).toBeNull()
    expect(screen.queryByTestId('ret-leader-option-anna')).toBeNull()
    // Choose Trine → the POST targets her (NOT the default first option).
    fireEvent.change(screen.getByTestId('ret-leader-select'), { target: { value: 'trine' } })
    fireEvent.click(screen.getByTestId('ret-leader-submit'))
    await waitFor(() => expect(reportingLines.assignManager).toHaveBeenCalledTimes(1))
    expect(reportingLines.assignManager).toHaveBeenCalledWith(
      { employeeId: 'carl', managerId: 'trine', effectiveFrom: expect.any(String) },
      undefined, // Carl's version is null in the default fixture → create
    )
  })

  it('"Tildel leder" opens the S108 unit-leader edit drawer (Ledere checkboxes) for the leaderless unit', () => {
    renderPanel()
    fireEvent.click(screen.getByTestId(`caret-unit-${KONTROL}`)) // surface the leaderless note
    fireEvent.click(screen.getByTestId(`assign-leader-${KONTROL}`))
    // The unit edit drawer for Kontrol (a team), focused on the Ledere checkboxes.
    expect(screen.getByTestId('unit-drawer-title').textContent).toBe('Rediger team')
    expect(screen.getByText('Ledere')).toBeDefined()
    // Its own member (Kim) is the leader candidate (the Drawer portals to body, so
    // resolve the checkbox via the document, not the render container).
    expect(document.getElementById('leader-checkbox-kim')).not.toBeNull()
  })

  it('gating: a below-floor role (Employee) sees neither "Ret" nor "Tildel leder"', () => {
    auth.role = 'Employee'
    renderPanel({ rosterByOrg: { STY02: singleLeaderRoster(3) } })
    expect(screen.getByTestId('employee-carl')).toBeDefined() // the row still renders read-only
    expect(screen.queryByTestId('ret-carl')).toBeNull()
    expect(screen.queryByText('Ret')).toBeNull()
  })
})

// ── SPRINT-109 / TASK-10904 — the ported period-settlement overview ──────────────
// The status tiles ("Ikke indsendt" / "Ikke godkendt") + the aggregated orphan card
// ("X mangler godkender" + an inline approver-assign) port from the retired
// MedarbejderAdministration. They are scoped to the SELECTED Organisation's loaded
// roster, so they render on an Organisation node (not a unit / MAO).
const STY02_NODE: SelectedNode = { id: 'STY02', kind: 'organisation', name: 'Statens IT', type: 'organisation' }

/** A settlement-focused STY02 roster: two OPEN (not-submitted) non-orphan people
    (Jens + Anna), one APPROVED (Bo), one ORPHAN (Orla, no approver), and one
    manager with a pending period (Jens, via pendingCountByManager). */
function settlementRoster(): RosterResponse {
  return {
    employees: [
      row({
        employeeId: 'jens', displayName: 'Jens Kofoed', position: 'Kontorchef',
        unitId: VEJL, unitName: 'Vejledning', leaderIds: ['jens'], structuralApproverId: 'dir1', periodStatus: 'OPEN',
      }),
      row({
        employeeId: 'anna', displayName: 'Anna Andersen', position: 'Sagsbehandler',
        unitId: VEJL, unitName: 'Vejledning', leaderIds: ['jens'], structuralApproverId: 'jens', periodStatus: 'OPEN',
      }),
      row({
        employeeId: 'bo', displayName: 'Bo Bondo', position: 'Fuldmægtig',
        unitId: VEJL, unitName: 'Vejledning', leaderIds: ['jens'], structuralApproverId: 'jens', periodStatus: 'APPROVED',
      }),
      row({
        employeeId: 'orla', displayName: 'Orla Frisk', position: 'Konsulent',
        unitId: VEJL, unitName: 'Vejledning', leaderIds: ['jens'], structuralApproverId: null, isOrphan: true, periodStatus: 'OPEN',
      }),
    ],
    pendingCountByManager: { jens: 2 },
    nameResolution: {
      dir1: { userId: 'dir1', displayName: 'Direktør Dorthe', position: 'Direktør', unitName: 'Direktion' },
    },
  }
}

describe('StrukturPanel — period-settlement overview (TASK-10904)', () => {
  it('renders the status tiles scoped to the Organisation roster (OPEN non-orphan count + pending managers)', () => {
    renderPanel({ selected: STY02_NODE, rosterByOrg: { STY02: settlementRoster() } })
    const overview = screen.getByTestId('settlement-overview')
    expect(within(overview).getByText('Ikke indsendt')).toBeDefined()
    expect(within(overview).getByText('Ikke godkendt')).toBeDefined()
    // Jens (OPEN) + Anna (OPEN) → 2; Bo (APPROVED) + Orla (orphan) excluded.
    expect(screen.getByTestId('settle-count-indsend').textContent).toBe('2')
    // One manager (Jens) carries a pending period.
    expect(screen.getByTestId('settle-count-godkend').textContent).toBe('1')
    // The ported period label.
    expect(within(overview).getByText('Maj 2026')).toBeDefined()
  })

  it('does NOT render the settlement overview on a unit node (Organisation-scoped)', () => {
    renderPanel({ selected: VEJL_NODE, rosterByOrg: { STY02: settlementRoster() } })
    expect(screen.queryByTestId('settlement-overview')).toBeNull()
  })

  it('the orphan card lists the roster orphans with an inline "+ Tildel godkender" (LocalHR)', () => {
    renderPanel({ selected: STY02_NODE, rosterByOrg: { STY02: settlementRoster() } })
    const card = screen.getByTestId('orphan-overview')
    expect(screen.getByTestId('orphan-count').textContent).toContain('⚠ 1 mangler godkender')
    expect(within(card).getByTestId('orphan-orla')).toBeDefined()
    expect(within(card).getByText('Orla Frisk')).toBeDefined()
    // The inline assign reuses InlineApproverControl (trigger "+ Tildel godkender").
    expect(within(card).getByTestId('inline-approver-assign-orla')).toBeDefined()
    expect(within(card).getByText('+ Tildel godkender')).toBeDefined()
  })

  it('the orphan inline-assign is gated: a below-floor role (Employee) sees the list but no assign', () => {
    auth.role = 'Employee'
    renderPanel({ selected: STY02_NODE, rosterByOrg: { STY02: settlementRoster() } })
    // The read-only overview + orphan list still render…
    expect(screen.getByTestId('orphan-overview')).toBeDefined()
    expect(screen.getByTestId('orphan-orla')).toBeDefined()
    // …but the mutation affordance is absent (no dead button).
    expect(screen.queryByTestId('inline-approver-assign-orla')).toBeNull()
    expect(screen.queryByText('+ Tildel godkender')).toBeNull()
  })

  it('click-to-filter: the "Ikke indsendt" tile narrows the Struktur to OPEN non-orphan people', () => {
    renderPanel({ selected: STY02_NODE, rosterByOrg: { STY02: settlementRoster() } })
    const tile = screen.getByTestId('settle-tile-indsend')
    expect(tile.getAttribute('aria-pressed')).toBe('false')
    fireEvent.click(tile)
    expect(screen.getByTestId('settle-tile-indsend').getAttribute('aria-pressed')).toBe('true')
    // The filter auto-expands the units and shows ONLY the OPEN non-orphan people:
    // Jens (leader, OPEN) + Anna (OPEN) render; Bo (APPROVED) is filtered out.
    expect(screen.getByTestId('leader-jens')).toBeDefined()
    expect(screen.getByTestId('employee-anna')).toBeDefined()
    expect(screen.queryByTestId('employee-bo')).toBeNull()
    // Clicking again clears the filter (Bo re-appears once the unit is expanded).
    fireEvent.click(screen.getByTestId('settle-tile-indsend'))
    expect(screen.getByTestId('settle-tile-indsend').getAttribute('aria-pressed')).toBe('false')
    expect(screen.getByTestId('employee-bo')).toBeDefined()
  })
})
