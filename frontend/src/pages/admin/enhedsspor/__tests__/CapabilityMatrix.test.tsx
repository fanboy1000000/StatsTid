// SPRINT-108 / TASK-10804 — the CONSOLIDATED per-role capability-gating MATRIX
// (the load-bearing keystone). Where the per-task suites (UnitDrawer /
// OrgStructureMutations / StrukturPanel) each pin one affordance family, THIS test
// renders the MERGED page (OrganisationOgMedarbejdere) and, for EACH role, selects
// a MAO node, an Organisation node, and a unit node and asserts the EXACT
// structure-affordance set present/absent — the single place the FE gating is
// checked against the LIVE backend floors end-to-end:
//
//   • unit create/rename/move/delete + leaders ...... LocalHR   (S104 UnitEndpoints)
//   • Organisation-node "Omdøb" (rename) ............. LocalAdmin (S98/S99)
//   • MAO-node "+ Organisation" + MAO-node "Omdøb" ... GlobalAdmin (S109 per-node MAO scope-gate)
//   • MAO-create + Organisation move/delete .......... GlobalAdmin
//
// S109 / TASK-10904 — the MAO scope-gate is resolved PER-NODE-KIND: the MAO-node
// create ("+ Organisation") + rename ("Omdøb") scope the MAO on the backend
// (ValidateOrgAccessAsync(MAO)) → a SCOPED LocalAdmin has no MAO scope → those gate
// by GlobalAdmin (no dead button). The Organisation-node "Omdøb" stays LocalAdmin.
//
// The FE gate is UX only (P7) — the backend re-checks every mutation; a wrong gate
// is an expectation/UX bug, which is exactly what this matrix guards against.
//
// useForest / useRoster / useSearch are mocked (offline, REAL S106 wire shapes);
// useAuth is a parametrized role holder; useToast is a no-op — mirroring the prior
// S108 suites. The roster is intentionally PEOPLE-rich (leader + report + cross-unit
// exception + vikar) so the per-role people-affordance assertions (S109: + Medarbejder
// / Rediger › present at the LocalHR floor, absent below) are load-bearing.

import { describe, it, expect, vi, beforeEach } from 'vitest'
import { render, screen, fireEvent } from '@testing-library/react'
import type { ForestMaoNode } from '../../../../hooks/useForest'
import type { RosterResponse } from '../../../../hooks/useRoster'
import type { SearchResponse } from '../../../../hooks/useSearch'

// ── mocks (mutable holders the mocked hooks read) ───────────────────────────────
const auth = vi.hoisted(() => ({ role: 'LocalHR' as string | null }))
vi.mock('../../../../contexts/AuthContext', () => ({
  useAuth: () => ({ role: auth.role }),
}))
vi.mock('../../../../components/ui', async (importOriginal) => {
  const actual = await importOriginal<typeof import('../../../../components/ui')>()
  return { ...actual, useToast: () => ({ toast: vi.fn() }) }
})

const h = vi.hoisted(() => ({
  forest: [] as unknown[],
  roster: {} as Record<string, unknown>,
}))
vi.mock('../../../../hooks/useForest', () => ({
  useForest: () => ({ forest: h.forest, loading: false, error: null, fetchForest: vi.fn() }),
}))
vi.mock('../../../../hooks/useRoster', () => ({
  useRoster: () => ({ byOrg: h.roster, loading: false, error: null, loadRoster: vi.fn(), refetchRoster: vi.fn() }),
}))
vi.mock('../../../../hooks/useSearch', () => ({
  useSearch: () => ({ query: '', setQuery: vi.fn(), results: { units: [], people: [] } as SearchResponse, loading: false, error: null }),
}))

import { OrganisationOgMedarbejdere } from '../../OrganisationOgMedarbejdere'

// ── the fixture: MIN01 (MAO) → STY02 (Organisation) → Vejledning (kontor) ───────
const VEJL = '000000d0-0000-0000-0000-0000000000a1' // a top-level kontor (child = team)

function makeForest(): ForestMaoNode[] {
  const vejl = {
    unitId: VEJL,
    organisationId: 'STY02',
    parentUnitId: null,
    type: 'kontor' as const,
    name: 'Vejledning',
    level: 1,
    version: 1,
    directMemberCount: 4,
    memberCount: 4,
    children: [],
  }
  return [
    {
      orgId: 'MIN01',
      orgName: 'Finansministeriet',
      orgType: 'MAO',
      parentOrgId: null,
      materializedPath: '/MIN01/',
      memberCount: 4,
      organisations: [
        {
          orgId: 'STY02',
          orgName: 'Statens IT',
          orgType: 'ORGANISATION',
          parentOrgId: 'MIN01',
          materializedPath: '/MIN01/STY02/',
          agreementCode: 'HK',
          okVersion: 'OK24',
          memberCount: 4,
          directMemberCount: 0,
          units: [vejl],
        },
      ],
    },
  ]
}

/** A roster row in the real S106 wire shape. */
function row(p: Partial<RosterResponse['employees'][number]> & { employeeId: string; displayName: string }) {
  return {
    enhedLabel: 'Vejledning',
    position: null,
    structuralApproverId: null,
    periodStatus: 'OPEN' as const,
    outgoingVikar: null,
    isRoot: false,
    isOrphan: false,
    unitId: VEJL,
    unitName: 'Vejledning',
    leaderIds: ['jens'],
    primaryReportingLineVersion: null,
    ...p,
  }
}

/** A PEOPLE-rich Vejledning roster: a leader (Jens, absent w/ vikar Bo), two
    reports (Anna → Jens, Bo the stand-in → Jens) and a cross-unit exception (Carl
    → a leader outside the unit). Renders the full read-only people surface so the
    "no people-mutation affordance" guard is meaningful. */
function makeRoster(): RosterResponse {
  return {
    employees: [
      row({
        employeeId: 'jens', displayName: 'Jens Kofoed', position: 'Kontorchef', structuralApproverId: 'dir1',
        outgoingVikar: { vikarUserId: 'bo', vikarDisplayName: 'Bo Bondo', untilDate: '2099-12-31', reason: 'FERIE' },
      }),
      row({ employeeId: 'anna', displayName: 'Anna Andersen', position: 'Sagsbehandler', structuralApproverId: 'jens' }),
      row({ employeeId: 'bo', displayName: 'Bo Bondo', position: 'Fuldmægtig', structuralApproverId: 'jens' }),
      row({ employeeId: 'carl', displayName: 'Carl Storm', position: 'Specialkonsulent', structuralApproverId: 'extLeader' }),
    ],
    pendingCountByManager: {},
    nameResolution: {
      dir1: { userId: 'dir1', displayName: 'Direktør Dorthe', position: 'Direktør', unitName: 'Direktion' },
      extLeader: { userId: 'extLeader', displayName: 'Ekstern Leder', position: 'Kontorchef', unitName: 'Andet Kontor' },
    },
  }
}

beforeEach(() => {
  h.forest = makeForest()
  h.roster = { STY02: makeRoster() }
  auth.role = 'LocalHR'
})

// ── the matrix: the LIVE floors, encoded per role ───────────────────────────────
// actionRow          — the unit/org action row renders at all (LocalHR floor).
// maoOrgCreateEnabled — the "+ Organisation" create on a MAO node enabled (GlobalAdmin).
// maoRename          — the "Omdøb" on a MAO node present (GlobalAdmin — MAO scope).
// orgRename          — the "Omdøb" on an Organisation node present (LocalAdmin).
// orgMove            — the "Flyt" (Organisation move) affordance present (GlobalAdmin).
// orgDelete          — the "Slet" (Org/MAO delete) affordance present (GlobalAdmin).
// maoCreate          — the top-level "+ Ministerområde" in the tree header (GlobalAdmin).
interface RoleExpect {
  role: string
  actionRow: boolean
  maoOrgCreateEnabled: boolean
  maoRename: boolean
  orgRename: boolean
  orgMove: boolean
  orgDelete: boolean
  maoCreate: boolean
}

const MATRIX: RoleExpect[] = [
  { role: 'Employee', actionRow: false, maoOrgCreateEnabled: false, maoRename: false, orgRename: false, orgMove: false, orgDelete: false, maoCreate: false },
  // LocalLeader is the role DIRECTLY below the LocalHR floor (roles.ts: 4 vs 3) — the
  // tightest negative case: it pins the people-edit + unit-structure gate at EXACTLY
  // LocalHR (not merely "above Employee"). Like Employee it sees NO affordance.
  { role: 'LocalLeader', actionRow: false, maoOrgCreateEnabled: false, maoRename: false, orgRename: false, orgMove: false, orgDelete: false, maoCreate: false },
  { role: 'LocalHR', actionRow: true, maoOrgCreateEnabled: false, maoRename: false, orgRename: false, orgMove: false, orgDelete: false, maoCreate: false },
  // A scoped LocalAdmin owns their Organisation (Org "Omdøb") but has NO MAO scope:
  // the MAO-node create + rename are GlobalAdmin → DISABLED/ABSENT here (no dead button).
  { role: 'LocalAdmin', actionRow: true, maoOrgCreateEnabled: false, maoRename: false, orgRename: true, orgMove: false, orgDelete: false, maoCreate: false },
  { role: 'GlobalAdmin', actionRow: true, maoOrgCreateEnabled: true, maoRename: true, orgRename: true, orgMove: true, orgDelete: true, maoCreate: true },
]

const present = (tid: string): boolean => !!screen.queryByTestId(tid)

describe('Capability-gating MATRIX (TASK-10804) — the per-role affordance floors', () => {
  for (const m of MATRIX) {
    it(`${m.role}: the exact structure-affordance set across MAO / Organisation / unit`, () => {
      auth.role = m.role
      render(<OrganisationOgMedarbejdere />)

      // ── tree header: the top-level "+ Ministerområde" (MAO-create, GlobalAdmin) ──
      expect(present('mao-create-button')).toBe(m.maoCreate)

      // ── (1) a MAO node ──────────────────────────────────────────────────────────
      fireEvent.click(screen.getByTestId('tree-row-MIN01'))
      if (!m.actionRow) {
        expect(present('unit-action-row')).toBe(false)
        expect(present('unit-action-create')).toBe(false)
        expect(present('org-action-rename')).toBe(false)
        expect(present('org-action-delete')).toBe(false)
      } else {
        expect(present('unit-action-row')).toBe(true)
        // On a MAO the shared create button is the ORG create ("+ Organisation"),
        // which scopes the MAO → enabled only at the GlobalAdmin floor (S109): a
        // scoped LocalAdmin sees the disabled placeholder (no dead button).
        const create = screen.getByTestId('unit-action-create') as HTMLButtonElement
        expect(create.textContent).toContain('Organisation')
        expect(create.disabled).toBe(!m.maoOrgCreateEnabled)
        // Unit edit/move/delete are NEVER on a MAO (it is not a unit).
        expect(present('unit-action-edit')).toBe(false)
        expect(present('unit-action-move')).toBe(false)
        expect(present('unit-action-delete')).toBe(false)
        // The MAO-node "Omdøb" scopes the MAO → GlobalAdmin (S109). A MAO is a root
        // → never a "Flyt". "Slet" = GlobalAdmin.
        expect(present('org-action-rename')).toBe(m.maoRename)
        expect(present('org-action-move')).toBe(false)
        expect(present('org-action-delete')).toBe(m.orgDelete)
      }

      // ── (2) an Organisation node ────────────────────────────────────────────────
      fireEvent.click(screen.getByTestId('tree-row-STY02'))
      if (!m.actionRow) {
        expect(present('unit-action-row')).toBe(false)
        expect(present('unit-action-create')).toBe(false)
        expect(present('org-action-rename')).toBe(false)
        expect(present('org-action-move')).toBe(false)
        expect(present('org-action-delete')).toBe(false)
      } else {
        // On an Organisation the shared create is the UNIT create ("+ Direktion"),
        // present + enabled for every actor at/above the LocalHR floor.
        const create = screen.getByTestId('unit-action-create') as HTMLButtonElement
        expect(create.textContent).toContain('Direktion')
        expect(create.disabled).toBe(false)
        // Unit edit/move/delete are NEVER on an Organisation (it is not a unit).
        expect(present('unit-action-edit')).toBe(false)
        expect(present('unit-action-move')).toBe(false)
        expect(present('unit-action-delete')).toBe(false)
        // The org mutation gates (the differentiator across LocalHR/LocalAdmin/GlobalAdmin).
        expect(present('org-action-rename')).toBe(m.orgRename)
        expect(present('org-action-move')).toBe(m.orgMove)
        expect(present('org-action-delete')).toBe(m.orgDelete)
      }

      // ── (3) a unit node ─────────────────────────────────────────────────────────
      fireEvent.click(screen.getByTestId(`tree-row-${VEJL}`))
      if (!m.actionRow) {
        expect(present('unit-action-row')).toBe(false)
        expect(present('unit-action-create')).toBe(false)
        expect(present('unit-action-edit')).toBe(false)
        expect(present('unit-action-move')).toBe(false)
        expect(present('unit-action-delete')).toBe(false)
      } else {
        // The full UNIT affordance set is present for EVERY permitted actor
        // (LocalHR is the floor — the unit family does not gate higher).
        const create = screen.getByTestId('unit-action-create') as HTMLButtonElement
        expect(create.textContent).toContain('Team') // CHILD[kontor] = team
        expect(create.disabled).toBe(false)
        expect(present('unit-action-edit')).toBe(true)
        expect(present('unit-action-move')).toBe(true)
        expect(present('unit-action-delete')).toBe(true)
        // Org mutations are NEVER on a unit node (mutually exclusive by node type).
        expect(present('org-action-rename')).toBe(false)
        expect(present('org-action-move')).toBe(false)
        expect(present('org-action-delete')).toBe(false)
      }

      // ── the PEOPLE-mutation surface (S109 inversion) — gated at the LocalHR floor ─
      // The unit roster IS rendered (leader + reports + external + vikar). After the
      // S109 inversion the people-edit surface (+ Medarbejder on the unit + a per-row
      // "Rediger ›") is PRESENT for every permitting actor (LocalHR is the floor ==
      // the action-row floor) and ABSENT below it — load-bearing, not vacuous.
      expect(screen.getByTestId('leader-jens')).toBeDefined() // people did render
      expect(screen.getByText('Carl Storm')).toBeDefined()
      const peopleEditable = m.actionRow // people create/edit floor == LocalHR
      expect(present('person-action-create')).toBe(peopleEditable) // + Medarbejder on the unit
      expect(present('person-edit-jens')).toBe(peopleEditable) // a leader row
      expect(present('person-edit-carl')).toBe(peopleEditable) // a (cross-unit) employee row
      // S109 TASK-10903 — the cross-unit "Ret" (Carl → a leader outside the unit) is
      // a people-mutation gated at the SAME LocalHR floor: PRESENT for a permitting
      // actor, ABSENT below it (load-bearing — the fixture seeds the exception).
      expect(present('ret-carl')).toBe(peopleEditable)
      // The inline approver/vikar controls ("Skift"/"Afslut") live INSIDE the drawer
      // and never render on the panel, for any role. (Leaderless "Tildel leder"
      // needs a leaderless unit — exercised in StrukturPanel's dedicated leaderless
      // test; every unit in this fixture has a leader.)
      for (const label of ['Skift', 'Afslut']) {
        expect(screen.queryAllByText(label)).toHaveLength(0)
      }
      // No <a> links; person/leader NAMES are not themselves buttons (the edit entry
      // is the dedicated "Rediger ›" affordance).
      expect(screen.queryAllByRole('link')).toHaveLength(0)
      for (const name of ['Jens Kofoed', 'Anna Andersen', 'Carl Storm']) {
        expect(screen.getByText(name).closest('button')).toBeNull()
      }
    })
  }

  // ── S109 / TASK-10904 — the scoped-LocalAdmin no-dead-button guard (explicit) ───
  // A scoped LocalAdmin owns their Organisation (Org "Omdøb" present + enabled) but
  // has NO MAO scope, so the MAO-node "+ Organisation" create + "Omdøb" must NOT be
  // live affordances (the create button is the disabled placeholder; no rename).
  it('a scoped LocalAdmin sees NO live MAO-node Omdøb / create (no dead button)', () => {
    auth.role = 'LocalAdmin'
    render(<OrganisationOgMedarbejdere />)

    // On the MAO node: the "+ Organisation" create is DISABLED, the "Omdøb" is ABSENT.
    fireEvent.click(screen.getByTestId('tree-row-MIN01'))
    const maoCreate = screen.getByTestId('unit-action-create') as HTMLButtonElement
    expect(maoCreate.textContent).toContain('Organisation')
    expect(maoCreate.disabled).toBe(true)
    expect(present('org-action-rename')).toBe(false)

    // On the Organisation node: the LocalAdmin DOES own "Omdøb" (it is their scope).
    fireEvent.click(screen.getByTestId('tree-row-STY02'))
    expect(present('org-action-rename')).toBe(true)
  })
})
