// SPRINT-108 / TASK-10801 + TASK-10803 — the gated UNIT structure-mutation flows
// driven through StrukturPanel (the action row hosts the affordances; UnitDrawer /
// UnitMoveDialog / UnitDeleteConfirm are mounted by it). lib/api is mocked so the
// assertions pin the REAL S104 UnitEndpoints contracts (URL + body + If-Match):
//
//   • create-child: type derived from CHILD[parentType] (Organisation → top-level
//     direktion); leaf `enhed` + `ministeromrade` disabled.
//   • rename + leaders: PUT (If-Match) only when the name changed; the Ledere diff
//     → POST /leaders {userId} (designate) / DELETE /leaders/{userId} (remove).
//   • move: the picker excludes self + descendants + same-or-deeper TYPE-RANK; the
//     "→ Rod" option maps to null. PUT /move (If-Match).
//   • delete: a confirm-and-CASCADE dialog (no blocked-422 branch). DELETE (If-Match).
//   • gating: a below-floor role sees NO action row.

import { describe, it, expect, vi, beforeEach } from 'vitest'
import { type ComponentProps } from 'react'
import { render, screen, fireEvent, waitFor } from '@testing-library/react'

// ── mocks ──────────────────────────────────────────────────────────────────────
const auth = vi.hoisted(() => ({ role: 'LocalHR' as string | null }))
vi.mock('../../../../contexts/AuthContext', () => ({
  useAuth: () => ({ role: auth.role }),
}))
vi.mock('../../../../components/ui', async (importOriginal) => {
  const actual = await importOriginal<typeof import('../../../../components/ui')>()
  return { ...actual, useToast: () => ({ toast: vi.fn() }) }
})

const api = vi.hoisted(() => ({ post: vi.fn(), del: vi.fn(), etag: vi.fn() }))
vi.mock('../../../../lib/api', () => ({
  apiClient: {
    get: vi.fn(async () => ({ ok: true, data: {} })),
    post: api.post,
    put: vi.fn(async () => ({ ok: true, data: {} })),
    delete: api.del,
  },
  apiFetchWithEtag: api.etag,
}))

import { StrukturPanel } from '../StrukturPanel'
import type { SelectedNode } from '../OrgStructureTree'
import type { ForestMaoNode } from '../../../../hooks/useForest'
import type { RosterResponse } from '../../../../hooks/useRoster'

// ── ids (GUID-shaped; encodeURIComponent leaves them unchanged) ──
const VEJL = '000000d0-0000-0000-0000-0000000000a1' // kontor
const KONTROL = '000000d0-0000-0000-0000-0000000000a2' // team (child of VEJL)
const ENH = '000000d0-0000-0000-0000-0000000000a3' // enhed (child of KONTROL)
const DIR = '000000d0-0000-0000-0000-0000000000a4' // direktion (top-level)

function makeForest(): ForestMaoNode[] {
  const enh = {
    unitId: ENH, organisationId: 'STY02', parentUnitId: KONTROL, type: 'enhed' as const,
    name: 'Drift-enhed', level: 3, version: 3, directMemberCount: 0, memberCount: 0, children: [],
  }
  const kontrol = {
    unitId: KONTROL, organisationId: 'STY02', parentUnitId: VEJL, type: 'team' as const,
    name: 'Kontrol', level: 2, version: 2, directMemberCount: 0, memberCount: 0, children: [enh],
  }
  const vejl = {
    unitId: VEJL, organisationId: 'STY02', parentUnitId: null, type: 'kontor' as const,
    name: 'Vejledning', level: 1, version: 1, directMemberCount: 5, memberCount: 5, children: [kontrol],
  }
  const dir = {
    unitId: DIR, organisationId: 'STY02', parentUnitId: null, type: 'direktion' as const,
    name: 'Direktion', level: 1, version: 4, directMemberCount: 0, memberCount: 0, children: [],
  }
  return [
    {
      orgId: 'MIN01', orgName: 'Finansministeriet', orgType: 'MAO', parentOrgId: null,
      materializedPath: '/MIN01/', memberCount: 5,
      organisations: [
        {
          orgId: 'STY02', orgName: 'Statens IT', orgType: 'ORGANISATION', parentOrgId: 'MIN01',
          materializedPath: '/MIN01/STY02/', agreementCode: 'HK', okVersion: 'OK24',
          memberCount: 5, directMemberCount: 0, units: [dir, vejl],
        },
      ],
    },
  ]
}

const LEADERS = ['jens', 'trine']
function rrow(p: Partial<RosterResponse['employees'][number]> & { employeeId: string; displayName: string }) {
  return {
    enhedLabel: 'Vejledning', position: null, structuralApproverId: null, periodStatus: 'OPEN' as const,
    outgoingVikar: null, isRoot: false, isOrphan: false, unitId: VEJL, unitName: 'Vejledning',
    leaderIds: LEADERS, primaryReportingLineVersion: null, ...p,
  }
}
function makeRoster(): RosterResponse {
  return {
    employees: [
      rrow({ employeeId: 'jens', displayName: 'Jens Kofoed', position: 'Kontorchef', structuralApproverId: 'dir1' }),
      rrow({ employeeId: 'trine', displayName: 'Trine Toft', position: 'Teamleder', structuralApproverId: 'dir1' }),
      rrow({ employeeId: 'anna', displayName: 'Anna Andersen', position: 'Sagsbehandler', structuralApproverId: 'jens' }),
      rrow({ employeeId: 'bo', displayName: 'Bo Bondo', position: 'Fuldmægtig', structuralApproverId: 'trine' }),
      rrow({ employeeId: 'carl', displayName: 'Carl Storm', position: 'Specialkonsulent', structuralApproverId: 'jens' }),
    ],
    pendingCountByManager: {},
    nameResolution: { dir1: { userId: 'dir1', displayName: 'Direktør Dorthe', position: 'Direktør', unitName: 'Direktion' } },
  }
}

const node = (id: string, kind: SelectedNode['kind'], name: string, type: SelectedNode['type']): SelectedNode =>
  ({ id, kind, name, type })

function renderPanel(selected: SelectedNode | null, overrides: Partial<ComponentProps<typeof StrukturPanel>> = {}) {
  const onMutated = vi.fn()
  const props: ComponentProps<typeof StrukturPanel> = {
    forest: makeForest(),
    selected,
    rosterByOrg: { STY02: makeRoster() },
    rosterLoading: false,
    onLoadRoster: vi.fn(),
    onNavigate: vi.fn(),
    canBack: false,
    canForward: false,
    onBack: vi.fn(),
    onForward: vi.fn(),
    onMutated,
    ...overrides,
  }
  return { onMutated, ...render(<StrukturPanel {...props} />) }
}

beforeEach(() => {
  auth.role = 'LocalHR'
  api.post.mockReset().mockResolvedValue({ ok: true, data: {} })
  api.del.mockReset().mockResolvedValue({ ok: true, data: {} })
  api.etag.mockReset().mockResolvedValue({ ok: true, data: { data: {}, etag: '"9"', status: 200 } })
})

describe('Unit structure mutations (TASK-10801) + gating (TASK-10803)', () => {
  // ── create-child: type derivation + leaf/MAO disabled ──
  it('creates a child unit with the derived child type (kontor → team)', async () => {
    renderPanel(node(VEJL, 'unit', 'Vejledning', 'kontor'))
    expect(screen.getByTestId('unit-action-create').textContent).toContain('Team')
    fireEvent.click(screen.getByTestId('unit-action-create'))
    fireEvent.change(screen.getByTestId('unit-drawer-name'), { target: { value: 'Drift' } })
    fireEvent.click(screen.getByTestId('unit-drawer-submit'))
    await waitFor(() =>
      expect(api.post).toHaveBeenCalledWith('/api/admin/units', {
        organisationId: 'STY02',
        parentUnitId: VEJL,
        type: 'team',
        name: 'Drift',
      }),
    )
  })

  it('creates a TOP-LEVEL direktion under an Organisation (parentUnitId null)', async () => {
    renderPanel(node('STY02', 'organisation', 'Statens IT', 'organisation'))
    expect(screen.getByTestId('unit-action-create').textContent).toContain('Direktion')
    // Organisation carries no UNIT rename/move/delete (those are a separate task).
    expect(screen.queryByTestId('unit-action-edit')).toBeNull()
    expect(screen.queryByTestId('unit-action-delete')).toBeNull()
    fireEvent.click(screen.getByTestId('unit-action-create'))
    fireEvent.change(screen.getByTestId('unit-drawer-name'), { target: { value: 'Direktion Nord' } })
    fireEvent.click(screen.getByTestId('unit-drawer-submit'))
    await waitFor(() =>
      expect(api.post).toHaveBeenCalledWith('/api/admin/units', {
        organisationId: 'STY02',
        parentUnitId: null,
        type: 'direktion',
        name: 'Direktion Nord',
      }),
    )
  })

  it('disables create on a leaf enhed and on a MAO', () => {
    const { unmount } = renderPanel(node(ENH, 'unit', 'Drift-enhed', 'enhed'))
    expect((screen.getByTestId('unit-action-create') as HTMLButtonElement).disabled).toBe(true)
    unmount()
    renderPanel(node('MIN01', 'mao', 'Finansministeriet', 'ministeromrade'))
    expect((screen.getByTestId('unit-action-create') as HTMLButtonElement).disabled).toBe(true)
    // a MAO has no unit edit/move/delete either.
    expect(screen.queryByTestId('unit-action-edit')).toBeNull()
  })

  // ── rename + leaders (the Ledere diff; path-param remove) ──
  it('renames a unit via PUT with the If-Match version', async () => {
    renderPanel(node(VEJL, 'unit', 'Vejledning', 'kontor'))
    fireEvent.click(screen.getByTestId('unit-action-edit'))
    fireEvent.change(screen.getByTestId('unit-drawer-name'), { target: { value: 'Vejledning Vest' } })
    fireEvent.click(screen.getByTestId('unit-drawer-submit'))
    await waitFor(() =>
      expect(api.etag).toHaveBeenCalledWith(
        `/api/admin/units/${VEJL}`,
        expect.objectContaining({
          method: 'PUT',
          headers: { 'If-Match': '"1"' },
          body: JSON.stringify({ name: 'Vejledning Vest' }),
        }),
      ),
    )
  })

  it('designates + removes leaders from the diff (no rename when name unchanged)', async () => {
    renderPanel(node(VEJL, 'unit', 'Vejledning', 'kontor'))
    fireEvent.click(screen.getByTestId('unit-action-edit'))
    // jens + trine are the current leaders; designate anna, remove trine.
    fireEvent.click(screen.getByRole('checkbox', { name: /Anna Andersen/ }))
    fireEvent.click(screen.getByRole('checkbox', { name: /Trine Toft/ }))
    fireEvent.click(screen.getByTestId('unit-drawer-submit'))
    await waitFor(() =>
      expect(api.post).toHaveBeenCalledWith(`/api/admin/units/${VEJL}/leaders`, { userId: 'anna' }),
    )
    expect(api.del).toHaveBeenCalledWith(`/api/admin/units/${VEJL}/leaders/trine`)
    // the name was untouched → NO rename PUT.
    expect(api.etag).not.toHaveBeenCalled()
  })

  it('lists ONLY the unit’s own members as leader candidates (member-invariant)', () => {
    renderPanel(node(VEJL, 'unit', 'Vejledning', 'kontor'))
    fireEvent.click(screen.getByTestId('unit-action-edit'))
    expect(screen.getByText(/En leder skal være placeret i denne enhed/)).toBeDefined()
    for (const name of ['Anna Andersen', 'Bo Bondo', 'Carl Storm', 'Jens Kofoed', 'Trine Toft']) {
      expect(screen.getByRole('checkbox', { name: new RegExp(name) })).toBeDefined()
    }
  })

  // ── move: picker exclusions + If-Match ──
  it('move picker excludes self + descendants + same-or-deeper rank, offers → Rod', () => {
    renderPanel(node(KONTROL, 'unit', 'Kontrol', 'team'))
    fireEvent.click(screen.getByTestId('unit-action-move'))
    // valid shallower parents: VEJL (kontor) + DIR (direktion).
    expect(screen.getByTestId(`unit-move-option-${VEJL}`)).toBeDefined()
    expect(screen.getByTestId(`unit-move-option-${DIR}`)).toBeDefined()
    // self + the enhed descendant are excluded.
    expect(screen.queryByTestId(`unit-move-option-${KONTROL}`)).toBeNull()
    expect(screen.queryByTestId(`unit-move-option-${ENH}`)).toBeNull()
    // the → Rod option is always present.
    expect(screen.getByText(/→ Rod/)).toBeDefined()
  })

  it('moves a unit via PUT /move with the If-Match version', async () => {
    renderPanel(node(KONTROL, 'unit', 'Kontrol', 'team'))
    fireEvent.click(screen.getByTestId('unit-action-move'))
    fireEvent.change(screen.getByTestId('unit-move-target'), { target: { value: DIR } })
    fireEvent.click(screen.getByTestId('unit-move-submit'))
    await waitFor(() =>
      expect(api.etag).toHaveBeenCalledWith(
        `/api/admin/units/${KONTROL}/move`,
        expect.objectContaining({
          method: 'PUT',
          headers: { 'If-Match': '"2"' },
          body: JSON.stringify({ newParentUnitId: DIR }),
        }),
      ),
    )
  })

  it('the → Rod option moves the unit to top-level (newParentUnitId null)', async () => {
    renderPanel(node(KONTROL, 'unit', 'Kontrol', 'team'))
    fireEvent.click(screen.getByTestId('unit-action-move'))
    // default selection is → Rod; submit straight away.
    fireEvent.click(screen.getByTestId('unit-move-submit'))
    await waitFor(() =>
      expect(api.etag).toHaveBeenCalledWith(
        `/api/admin/units/${KONTROL}/move`,
        expect.objectContaining({ body: JSON.stringify({ newParentUnitId: null }) }),
      ),
    )
  })

  // ── delete: confirm-and-CASCADE (no blocked-422 branch) ──
  it('delete is a confirm-and-CASCADE dialog that warns of the up-move, then DELETEs', async () => {
    const { onMutated } = renderPanel(node(KONTROL, 'unit', 'Kontrol', 'team'))
    fireEvent.click(screen.getByTestId('unit-action-delete'))
    // the destructive-cascade warning (NOT a blocked guard).
    expect(screen.getByTestId('unit-delete-warning').textContent).toContain('flyttes ét niveau op')
    expect(screen.queryByText(/kan ikke slettes/)).toBeNull()
    fireEvent.click(screen.getByTestId('unit-delete-confirm'))
    await waitFor(() =>
      expect(api.etag).toHaveBeenCalledWith(
        `/api/admin/units/${KONTROL}`,
        expect.objectContaining({ method: 'DELETE', headers: { 'If-Match': '"2"' } }),
      ),
    )
    // success → the page refetch is triggered for the affected Organisation.
    await waitFor(() => expect(onMutated).toHaveBeenCalledWith('STY02'))
  })

  // ── error surfacing ──
  it('surfaces a 412 stale conflict inline (no refetch)', async () => {
    api.etag.mockResolvedValue({ ok: false, status: 412, error: 'stale' })
    const { onMutated } = renderPanel(node(VEJL, 'unit', 'Vejledning', 'kontor'))
    fireEvent.click(screen.getByTestId('unit-action-edit'))
    fireEvent.change(screen.getByTestId('unit-drawer-name'), { target: { value: 'Andet navn' } })
    fireEvent.click(screen.getByTestId('unit-drawer-submit'))
    await waitFor(() => expect(screen.getByTestId('unit-drawer-error')).toBeDefined())
    expect(screen.getByTestId('unit-drawer-error').textContent).toMatch(/opdateret af en anden/)
    expect(onMutated).not.toHaveBeenCalled()
  })

  // ── gating: a below-floor role sees no action row ──
  it('renders NO unit action row for a below-floor role', () => {
    auth.role = 'Employee'
    renderPanel(node(VEJL, 'unit', 'Vejledning', 'kontor'))
    expect(screen.queryByTestId('unit-action-row')).toBeNull()
    expect(screen.queryByTestId('unit-action-create')).toBeNull()
  })
})
