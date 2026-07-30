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
// carets, the two view toggles, breadcrumb + back/forward, unit-NAME navigation, the
// gated UNIT structure actions (S108) and — NEW in S109 — the gated PEOPLE actions
// (+ Medarbejder on a unit + the person-NAME edit). The out-of-scope shortcuts
// stay absent: cross-unit "Ret" + leaderless "Tildel leder" (TASK-10903) and the
// inline approver/vikar ("Skift"/"Afslut" — those live inside the drawer).

import { describe, it, expect, vi, beforeEach } from 'vitest'
import { useState, type ComponentProps } from 'react'
import { render, screen, fireEvent, within, waitFor, cleanup } from '@testing-library/react'

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

// S123 T2 — the focus happy-path is the FIRST test here to open the PersonDrawer →
// it invokes fetchUser (the panel's inlined openEditPerson). Mock useOrgUsers so
// fetchUser PENDS (never resolves): the drawer opens in its loading state (editUser
// stays null → create-mode render) and the LifecycleSections resolve effect
// early-returns (no reporting-lines GET), keeping the subtree offline.
const admin = vi.hoisted(() => ({ fetchUser: vi.fn(() => new Promise<never>(() => {})) }))
vi.mock('../../../../hooks/useAdmin', () => ({
  useOrgUsers: () => ({ fetchUser: admin.fetchUser }),
}))

import { ToastProvider } from '../../../../components/ui/Toast'
import { StrukturPanel } from '../StrukturPanel'
import type { SelectedNode } from '../OrgStructureTree'
import type { ForestMaoNode } from '../../../../hooks/useForest'
import type { RosterResponse } from '../../../../hooks/useRoster'

beforeEach(() => {
  auth.role = 'LocalHR'
  reportingLines.assignManager.mockReset()
  reportingLines.assignManager.mockResolvedValue({ ok: true, data: { version: 7 } })
  admin.fetchUser.mockClear()
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
// The MAO root of makeForest(). A MAO's `organisationId` is null by design, so no roster
// ever loads for it — the tier where the people-visibility toggles cannot function.
const MIN01_NODE: SelectedNode = { id: 'MIN01', kind: 'mao', name: 'Finansministeriet', type: 'ministeromrade' }

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
    onExpandSync: vi.fn(),
    ...overrides,
  }
  // A real ToastProvider satisfies the drawer's '/Toast' useToast (the focus
  // happy-path opens the PersonDrawer); it renders no extra buttons when empty, so
  // the S91 allowlist test is unaffected.
  return { ...render(
    <ToastProvider>
      <StrukturPanel {...props} />
    </ToastProvider>,
  ), props }
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

  // S123 T1 — the three peer layers. "Skjul medarbejdere" now hides ONLY the
  // NON-LEADER rows; the leaders stay (they have their own "Skjul ledere" toggle).
  // This is the "org + leaders only" capability (OQ-2: static report count, no chevron).
  it('the "Skjul medarbejdere" toggle hides only the NON-LEADER rows; the leaders stay (peer layers)', () => {
    renderPanel()
    // Both people-layers on by default → leaders + their nested reports render.
    expect(screen.getByTestId('leader-jens')).toBeDefined()
    expect(screen.getByTestId('employee-anna')).toBeDefined()
    const toggle = screen.getByTestId('toggle-people')
    expect(toggle.textContent).toContain('Skjul medarbejdere')
    fireEvent.click(toggle)
    // The non-leaders (anna/bo/carl) are hidden…
    expect(screen.queryByTestId('employee-anna')).toBeNull()
    expect(screen.queryByTestId('employee-bo')).toBeNull()
    expect(screen.queryByTestId('employee-carl')).toBeNull()
    // …but the leaders themselves remain (showLeaders still on) — "org + leaders only".
    expect(screen.getByTestId('leader-jens')).toBeDefined()
    expect(screen.getByTestId('leader-trine')).toBeDefined()
    // OQ-2: the leader keeps its static "1 medarb." count, but the expand chevron is
    // gone (employees hidden → nothing to expand).
    expect(within(screen.getByTestId('leader-jens')).getByText('1 medarb.')).toBeDefined()
    expect(screen.queryByTestId('caret-leader-jens')).toBeNull()
    expect(screen.getByTestId('toggle-people').textContent).toContain('Vis medarbejdere')
    // Re-show restores the non-leader rows (nested under their leaders again).
    fireEvent.click(screen.getByTestId('toggle-people'))
    expect(screen.getByTestId('employee-anna')).toBeDefined()
    expect(screen.getByTestId('caret-leader-jens')).toBeDefined()
  })

  // S123 T1 — the new peer toggle. "Skjul ledere" hides the leader rows; the
  // (non-leader) employees stay, now rendered FLAT with no leader-parent grouping
  // (OQ-3). This is the "org + employees only" capability.
  it('the "Vis/Skjul ledere" toggle hides and re-shows the leader rows; employees stay flat (OQ-3)', () => {
    renderPanel()
    expect(screen.getByTestId('leader-jens')).toBeDefined()
    expect(screen.getByTestId('leader-trine')).toBeDefined()
    const toggle = screen.getByTestId('toggle-leaders')
    expect(toggle.textContent).toContain('Skjul ledere')
    fireEvent.click(toggle)
    // Leaders are hidden…
    expect(screen.queryByTestId('leader-jens')).toBeNull()
    expect(screen.queryByTestId('leader-trine')).toBeNull()
    expect(screen.getByTestId('toggle-leaders').textContent).toContain('Vis ledere')
    // …but every non-leader employee stays, rendered flat (no leader grouping).
    expect(screen.getByTestId('employee-anna')).toBeDefined()
    expect(screen.getByTestId('employee-bo')).toBeDefined()
    expect(screen.getByTestId('employee-carl')).toBeDefined()
    // Re-show restores the leaders (and the nested grouping returns).
    fireEvent.click(screen.getByTestId('toggle-leaders'))
    expect(screen.getByTestId('leader-jens')).toBeDefined()
    expect(screen.getByTestId('caret-leader-jens')).toBeDefined()
  })

  // S123 T1 — the load-bearing regression invariant: both people-layers ON (the
  // default) MUST reproduce the pre-split nested view (leaders as expandable parents,
  // reports nested, the cross-unit exception flagged, section count = all visible people).
  it('both people-layers ON (the default) reproduces the prior nested view exactly', () => {
    renderPanel()
    // Leaders render as expandable parents (reports exist → the chevron is present).
    expect(screen.getByTestId('caret-leader-jens')).toBeDefined()
    // Reports nest under their matching leader.
    expect(screen.getByTestId('employee-anna')).toBeDefined() // → Jens
    expect(screen.getByTestId('employee-bo')).toBeDefined() // → Trine
    // The cross-unit exception is still flagged (external variant preserved).
    const carl = screen.getByTestId('employee-carl')
    expect(within(carl).getByText('Leder uden for enheden: Ekstern Leder')).toBeDefined()
    // The "Medarbejdere" section count = all 5 visible people (2 leaders + 3 non-
    // leaders) == the pre-split members.length (no regression).
    expect(within(screen.getByTestId(`caret-med-${VEJL}`)).getByText('5')).toBeDefined()
  })

  // A MAO's `organisationId` is null by design (forestIndex.ts — a MAO spans multiple
  // Organisations, each its own roster), so no roster loads and `membersOf` is empty
  // across the whole subtree: neither people toggle can ever reveal a row there. They are
  // HIDDEN, matching how this panel already treats tier-inapplicable surfaces (the
  // settlement overview and the unit action cluster both omit rather than grey), and
  // matching the MAO reframing of the panel as an organisation list.
  it('omits BOTH people-visibility toggles on a MAO (they cannot function without a roster)', () => {
    renderPanel({ selected: MIN01_NODE })
    expect(screen.queryByTestId('toggle-people')).toBeNull()
    expect(screen.queryByTestId('toggle-leaders')).toBeNull()
    // The panel is reframed as an organisation list at this tier.
    expect(screen.getByTestId('str-count').textContent).toContain('organisation')
  })

  // Guards against over-gating: the toggles must survive everywhere they DO work. The
  // predicate is the MAO tier, not "no roster yet" — an Organisation mid-load keeps them.
  it('keeps both people-visibility toggles on an Organisation and on a unit', () => {
    renderPanel({ selected: STY02_NODE })
    expect(screen.getByTestId('toggle-people')).toBeDefined()
    expect(screen.getByTestId('toggle-leaders')).toBeDefined()
    cleanup()
    renderPanel() // VEJL_NODE — a unit
    expect(screen.getByTestId('toggle-people')).toBeDefined()
    expect(screen.getByTestId('toggle-leaders')).toBeDefined()
  })

  // "Vis org." is NOT people-dependent — at a MAO it expands the child Organisations, so
  // it stays. (It disables only when the node genuinely has no descendants.)
  it('keeps "Vis org." on a MAO — it expands the child organisations and still works', () => {
    renderPanel({ selected: MIN01_NODE })
    const expand = screen.getByTestId('toggle-expand-all')
    expect(expand.textContent).toContain('Vis org.')
    expect((expand as HTMLButtonElement).disabled).toBe(false)
    fireEvent.click(expand)
    expect(screen.getByTestId('toggle-expand-all').textContent).toContain('Skjul org.')
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

  it('"Vis medarbejdere" REVEALS the non-leader rows nested in collapsed units (post-S114 every person is unit-homed — the toggle looked dead at Organisation/MAO level)', () => {
    renderPanel()
    // kim (a non-leader in the leaderless Kontrol unit) sits in the COLLAPSED Kontrol
    // unit — invisible at render.
    expect(screen.queryByTestId('employee-kim')).toBeNull()
    // Hide the non-leaders, then show them again with ONE click: the reveal must also
    // expand the descendant units (the settlement-filter reveal semantics), so
    // kim appears — under the pre-fix behavior Kontrol stayed collapsed and the
    // toggle flipped a dead label at unit-homed-only levels.
    fireEvent.click(screen.getByTestId('toggle-people')) // Skjul
    fireEvent.click(screen.getByTestId('toggle-people')) // Vis → reveal
    expect(screen.getByTestId('employee-kim')).toBeDefined()
    // Skjul hides the non-leader rows again but does NOT own unit expansion.
    fireEvent.click(screen.getByTestId('toggle-people'))
    expect(screen.queryByTestId('employee-kim')).toBeNull()
  })

  it('the unit NAME + breadcrumb + back/forward drive navigation (the only nav affordances)', () => {
    const onNavigate = vi.fn()
    const onBack = vi.fn()
    renderPanel({ onNavigate, onBack, canBack: true })
    // S124 — the open affordance IS the unit name (the right-edge "Åbn ›" is gone), so
    // the testid must sit on an element whose own text is the name, not on a sibling link.
    const unitName = screen.getByTestId(`open-unit-${KONTROL}`)
    expect(unitName.tagName).toBe('BUTTON')
    expect(unitName.textContent).toBe('Kontrol')
    expect(unitName.getAttribute('aria-label')).toBe('Åbn Kontrol')
    fireEvent.click(unitName)
    expect(onNavigate).toHaveBeenCalledWith({ id: KONTROL, kind: 'unit', name: 'Kontrol', type: 'team' })
    fireEvent.click(screen.getByTestId('crumb-STY02'))
    expect(onNavigate).toHaveBeenCalledWith({ id: 'STY02', kind: 'organisation', name: 'Statens IT', type: 'organisation' })
    fireEvent.click(screen.getByTestId('nav-back'))
    expect(onBack).toHaveBeenCalled()
  })

  // ── the keystone: the RE-ARCHITECTED allowlist (S108 structure + S109 people) ─
  // The S107 allowlist asserted EVERY button was a caret/toggle/breadcrumb/open.
  // S108 added the gated UNIT structure buttons; S109 (this) adds the gated PEOPLE
  // buttons (+ Medarbejder on a unit + a per-row person edit — since S124 that edit
  // is the person NAME itself, still carrying `person-edit-<id>`, so the allowlist
  // entry is unchanged). The guard SURVIVES for the affordances NOT in S109's
  // TASK-10901/10902 scope: cross-unit "Ret" +
  // leaderless "Tildel leder" (TASK-10903) and the INLINE approver/vikar
  // ("Skift"/"Afslut" — those live inside the drawer, never on the panel).
  it('exposes the gated STRUCTURE + PEOPLE affordances (S109 inversion); the out-of-scope shortcuts stay absent', () => {
    renderPanel() // default role LocalHR (permitting)
    fireEvent.click(screen.getByTestId(`caret-unit-${KONTROL}`)) // surface every node type

    // (a) ALLOWLIST: caret / view-toggle / breadcrumb / back-forward / the unit-NAME
    //     open button PLUS the four UNIT structure actions PLUS the people affordances
    //     (+ Medarbejder and the person-NAME edit button). Any OTHER stray button fails.
    const STRUCTURE = ['unit-action-create', 'unit-action-edit', 'unit-action-move', 'unit-action-delete']
    const allowed = (tid: string | null): boolean =>
      !!tid &&
      (['nav-back', 'nav-forward', 'toggle-expand-all', 'toggle-leaders', 'toggle-people'].includes(tid) ||
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
    //     on the unit + the per-row name edit on leaders + employees + the S109
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

    // (e) S124 INVERSION — person & leader NAMES ARE the edit affordance now. This
    //     assertion used to say the opposite (the edit entry was the dedicated
    //     "Rediger ›" link); it is inverted rather than deleted, because the surviving
    //     half — that a below-floor role gets an inert <span> and never a dead button —
    //     is the load-bearing guard. Its below-floor twin lives in CapabilityMatrix.
    for (const [name, id] of [
      ['Jens Kofoed', 'jens'], // a leader row
      ['Anna Andersen', 'anna'], // an employee row
      ['Carl Storm', 'carl'], // a cross-unit employee row
    ] as const) {
      const el = screen.getByText(name)
      expect(el.tagName).toBe('BUTTON')
      expect(el.getAttribute('data-testid')).toBe(`person-edit-${id}`)
      expect(el.getAttribute('aria-label')).toBe(`Rediger ${name}`)
      // The hit area is the NAME, not the row: the affordance class is the one that
      // hugs the text (`align-self: flex-start`). jsdom has no layout engine, so class
      // presence is the verifiable proxy for the geometry; the box itself is a visual
      // check. It must NOT be styled via the shared `personName` class alone — that
      // one also renders the INERT orphan-card name.
      expect(el.className).toContain('nameAction')
    }

    // (f) THE SAME ALLOWLIST AT MAO — the dead-button discipline extended to the tier
    //     where the people toggles cannot function. A MAO loads no roster, so neither
    //     toggle could ever reveal a row: both are OMITTED, and every button still
    //     standing at this tier must be one that actually does something here.
    //     Re-rendered LAST so the assertions above keep the unit-node context.
    cleanup()
    renderPanel({ selected: MIN01_NODE })
    for (const btn of screen.getAllByRole('button')) {
      expect(allowed(btn.getAttribute('data-testid'))).toBe(true)
    }
    expect(screen.queryByTestId('toggle-people')).toBeNull()
    expect(screen.queryByTestId('toggle-leaders')).toBeNull()
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
          onExpandSync={vi.fn()}
        />
      )
    }
    render(<Harness />)
    expect(screen.getByTestId('leader-jens')).toBeDefined()
  })

  // ── S123 T2 — search-to-person focus: reveal the row + open the edit drawer ──────
  // Given a `focusPersonId` for a row present in the loaded roster, the panel reveals
  // the row IN PLACE (org stays selected) and opens its edit drawer, then signals
  // `onFocusConsumed` exactly once. This is the FIRST test to open the PersonDrawer →
  // fetchUser is mocked to pend, so the drawer renders in its loading state.
  it('S123 T2 — focusPersonId reveals the row + opens the edit drawer (loading while fetchUser pends), consumed once', () => {
    const onFocusConsumed = vi.fn()
    renderPanel({ focusPersonId: 'anna', onFocusConsumed })
    // The edit drawer opened for the focused person (loading — the fresh user pends).
    expect(screen.getByTestId('person-drawer-loading')).toBeDefined()
    expect(screen.getByTestId('person-drawer-title')).toBeDefined()
    expect(admin.fetchUser).toHaveBeenCalledWith('anna')
    // REVEAL: the focused row is visible (Anna nests under her leader Jens, both
    // people-layers forced on by the reveal).
    expect(screen.getByTestId('employee-anna')).toBeDefined()
    // Consumed exactly once → the host clears pendingFocus (Back/Forward can't re-open).
    expect(onFocusConsumed).toHaveBeenCalledTimes(1)
  })

  it('S123 T2 — a focusPersonId absent from the loaded roster consumes the intent with NO drawer (not-found)', () => {
    const onFocusConsumed = vi.fn()
    renderPanel({ focusPersonId: 'ghost', onFocusConsumed })
    // Terminal not-found: no drawer, no throw, and the intent is consumed once.
    expect(screen.queryByTestId('person-drawer-title')).toBeNull()
    expect(screen.queryByTestId('person-drawer-loading')).toBeNull()
    expect(admin.fetchUser).not.toHaveBeenCalled()
    expect(onFocusConsumed).toHaveBeenCalledTimes(1)
  })

  // S123 T2 (BLOCKER regression) — an ORG-HOMED person has `unitId === null`; their
  // row renders under the SELECTED ORG's own med-section (keyed by the org id, NOT a
  // null unit key). The reveal must un-collapse `medClosed[selectedNode.id]`. A
  // controlled harness collapses that section first, then focuses the person, proving
  // the reveal re-opens the CORRECT key (the buggy `medClosed[null]` clear left it hidden).
  it('S123 T2 — an ORG-HOMED person (unitId null) is revealed by un-collapsing the ORG med-section (BLOCKER)', () => {
    const orgHomedRoster: RosterResponse = {
      employees: [
        row({ employeeId: 'omni', displayName: 'Omni Org', position: 'Konsulent', unitId: null, unitName: null, leaderIds: [], structuralApproverId: null }),
      ],
      pendingCountByManager: {},
      nameResolution: {},
    }
    function Harness() {
      const [focus, setFocus] = useState<string | undefined>(undefined)
      return (
        <ToastProvider>
          <button data-testid="do-focus" onClick={() => setFocus('omni')}>focus</button>
          <StrukturPanel
            forest={makeForest()}
            selected={STY02_NODE}
            rosterByOrg={{ STY02: orgHomedRoster }}
            rosterLoading={false}
            onLoadRoster={vi.fn()}
            onNavigate={vi.fn()}
            canBack={false}
            canForward={false}
            onBack={vi.fn()}
            onForward={vi.fn()}
            focusPersonId={focus}
            onFocusConsumed={vi.fn()}
            onExpandSync={vi.fn()}
          />
        </ToastProvider>
      )
    }
    render(<Harness />)
    // The org-homed row shows at the org level by default…
    expect(screen.getByTestId('employee-omni')).toBeDefined()
    // …collapse the ORG med-section (keyed by the org id) → the row hides…
    fireEvent.click(screen.getByTestId('caret-med-STY02'))
    expect(screen.queryByTestId('employee-omni')).toBeNull()
    // …focusing the org-homed person un-collapses medClosed[selectedNode.id] (NOT a
    // null key) so the row re-appears, and the edit drawer opens.
    fireEvent.click(screen.getByTestId('do-focus'))
    expect(screen.getByTestId('employee-omni')).toBeDefined()
    expect(screen.getByTestId('person-drawer-loading')).toBeDefined()
    expect(screen.getByTestId('person-drawer-title')).toBeDefined()
    expect(admin.fetchUser).toHaveBeenCalledWith('omni')
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
