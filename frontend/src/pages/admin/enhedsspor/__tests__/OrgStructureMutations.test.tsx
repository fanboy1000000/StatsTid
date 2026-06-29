// SPRINT-108 / TASK-10802 + TASK-10803 — the gated ORG / MAO structure-mutation
// flows driven through StrukturPanel (the title-block action row hosts the
// affordances; OrgCreateDialog / OrgRenameDialog / OrgMoveDialog / OrgDeleteDialog
// are mounted by it) + the top-level MaoCreateAction. lib/api is mocked so the
// assertions pin the REAL S98/S99 org endpoints (URL + body):
//
//   • org create (under a MAO): POST /api/admin/organizations
//     { orgName, orgType:'ORGANISATION', parentOrgId } — LocalAdmin.
//   • MAO-create (top-level):    POST … { orgType:'MAO', parentOrgId:null } — GlobalAdmin.
//   • rename:  PUT /api/admin/organizations/{id} { orgName } — LocalAdmin.
//   • move:    PUT /api/admin/organizations/{id}/move { newParentOrgId } — GlobalAdmin.
//   • delete:  DELETE /api/admin/organizations/{id} (2-branch blocked/empty) — GlobalAdmin.
//
// The keystone is the per-role gating mirroring the LIVE floors: a LocalHR sees the
// UNIT affordances only; a LocalAdmin adds org create/rename; a GlobalAdmin sees
// all (incl. MAO-create + org move/delete). The backend re-checks every mutation.

import { describe, it, expect, vi, beforeEach } from 'vitest'
import { type ComponentProps } from 'react'
import { render, screen, fireEvent, waitFor } from '@testing-library/react'

// ── mocks ──────────────────────────────────────────────────────────────────────
const auth = vi.hoisted(() => ({ role: 'GlobalAdmin' as string | null }))
vi.mock('../../../../contexts/AuthContext', () => ({
  useAuth: () => ({ role: auth.role }),
}))
vi.mock('../../../../components/ui', async (importOriginal) => {
  const actual = await importOriginal<typeof import('../../../../components/ui')>()
  return { ...actual, useToast: () => ({ toast: vi.fn() }) }
})

const api = vi.hoisted(() => ({ post: vi.fn(), put: vi.fn(), del: vi.fn(), etag: vi.fn() }))
vi.mock('../../../../lib/api', () => ({
  apiClient: {
    get: vi.fn(async () => ({ ok: true, data: {} })),
    post: api.post,
    put: api.put,
    delete: api.del,
  },
  apiFetchWithEtag: api.etag,
}))

import { StrukturPanel } from '../StrukturPanel'
import { MaoCreateAction } from '../OrgStructureDialogs'
import type { SelectedNode } from '../OrgStructureTree'
import type { ForestMaoNode } from '../../../../hooks/useForest'
import type { RosterResponse } from '../../../../hooks/useRoster'

// A two-MAO forest: MIN01 → { STY02 (6 employees, with a unit), STY03 (empty) },
// plus MIN02 (an alternate move target).
function makeForest(): ForestMaoNode[] {
  const unit = {
    unitId: '000000d0-0000-0000-0000-0000000000a1',
    organisationId: 'STY02',
    parentUnitId: null,
    type: 'kontor' as const,
    name: 'Vejledning',
    level: 1,
    version: 1,
    directMemberCount: 6,
    memberCount: 6,
    children: [],
  }
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
          orgId: 'STY02', orgName: 'Statens IT', orgType: 'ORGANISATION', parentOrgId: 'MIN01',
          materializedPath: '/MIN01/STY02/', agreementCode: 'HK', okVersion: 'OK24',
          memberCount: 6, directMemberCount: 0, units: [unit],
        },
        {
          orgId: 'STY03', orgName: 'Statens Indkøb', orgType: 'ORGANISATION', parentOrgId: 'MIN01',
          materializedPath: '/MIN01/STY03/', agreementCode: 'AC', okVersion: 'OK24',
          memberCount: 0, directMemberCount: 0, units: [],
        },
      ],
    },
    {
      orgId: 'MIN02', orgName: 'Skatteministeriet', orgType: 'MAO', parentOrgId: null,
      materializedPath: '/MIN02/', memberCount: 0, organisations: [],
    },
  ]
}

const emptyRoster = (): RosterResponse => ({ employees: [], pendingCountByManager: {}, nameResolution: {} })

const MAO: SelectedNode = { id: 'MIN01', kind: 'mao', name: 'Finansministeriet', type: 'ministeromrade' }
const ORG: SelectedNode = { id: 'STY02', kind: 'organisation', name: 'Statens IT', type: 'organisation' }
const ORG_EMPTY: SelectedNode = { id: 'STY03', kind: 'organisation', name: 'Statens Indkøb', type: 'organisation' }

function renderPanel(selected: SelectedNode, overrides: Partial<ComponentProps<typeof StrukturPanel>> = {}) {
  const onMutated = vi.fn()
  const props: ComponentProps<typeof StrukturPanel> = {
    forest: makeForest(),
    selected,
    rosterByOrg: { STY02: emptyRoster(), STY03: emptyRoster() },
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
  auth.role = 'GlobalAdmin'
  api.post.mockReset().mockResolvedValue({ ok: true, data: {} })
  api.put.mockReset().mockResolvedValue({ ok: true, data: {} })
  api.del.mockReset().mockResolvedValue({ ok: true, data: undefined })
  api.etag.mockReset().mockResolvedValue({ ok: true, data: { data: {}, etag: '"1"', status: 200 } })
})

describe('Org/MAO structure mutations (TASK-10802)', () => {
  // ── create (an Organisation under a MAO) ──
  it('creates an Organisation under a MAO via the "+ Organisation" button', async () => {
    renderPanel(MAO)
    const create = screen.getByTestId('unit-action-create') as HTMLButtonElement
    expect(create.textContent).toContain('Organisation')
    expect(create.disabled).toBe(false) // GlobalAdmin ≥ LocalAdmin floor
    fireEvent.click(create)
    fireEvent.change(screen.getByTestId('org-create-name'), { target: { value: 'Statens Nye Styrelse' } })
    fireEvent.click(screen.getByTestId('org-create-submit'))
    await waitFor(() =>
      expect(api.post).toHaveBeenCalledWith('/api/admin/organizations', {
        orgName: 'Statens Nye Styrelse',
        orgType: 'ORGANISATION',
        parentOrgId: 'MIN01',
      }),
    )
  })

  // ── rename (warning dialog) ──
  it('renames an Organisation via PUT (name-only)', async () => {
    const { onMutated } = renderPanel(ORG)
    fireEvent.click(screen.getByTestId('org-action-rename'))
    expect(screen.getByTestId('org-rename-warning').textContent).toMatch(/SLS/)
    fireEvent.change(screen.getByTestId('org-rename-name'), { target: { value: 'Statens IT Vest' } })
    fireEvent.click(screen.getByTestId('org-rename-submit'))
    await waitFor(() =>
      expect(api.put).toHaveBeenCalledWith('/api/admin/organizations/STY02', { orgName: 'Statens IT Vest' }),
    )
    await waitFor(() => expect(onMutated).toHaveBeenCalledWith(null))
  })

  it('renames a MAO via PUT (MAO warning copy)', async () => {
    renderPanel(MAO)
    fireEvent.click(screen.getByTestId('org-action-rename'))
    expect(screen.getByTestId('org-rename-warning').textContent).toMatch(/budgetansvar/)
    fireEvent.change(screen.getByTestId('org-rename-name'), { target: { value: 'Finansmin. (nyt)' } })
    fireEvent.click(screen.getByTestId('org-rename-submit'))
    await waitFor(() =>
      expect(api.put).toHaveBeenCalledWith('/api/admin/organizations/MIN01', { orgName: 'Finansmin. (nyt)' }),
    )
  })

  // ── move (target = a MAO; excludes the current parent) ──
  it('moves an Organisation to another MAO; the picker excludes the current parent', async () => {
    renderPanel(ORG)
    fireEvent.click(screen.getByTestId('org-action-move'))
    // MIN02 is offered; MIN01 (the current parent) is NOT.
    expect(screen.getByTestId('org-move-option-MIN02')).toBeDefined()
    expect(screen.queryByTestId('org-move-option-MIN01')).toBeNull()
    fireEvent.change(screen.getByTestId('org-move-target'), { target: { value: 'MIN02' } })
    fireEvent.click(screen.getByTestId('org-move-submit'))
    await waitFor(() =>
      expect(api.put).toHaveBeenCalledWith('/api/admin/organizations/STY02/move', { newParentOrgId: 'MIN02' }),
    )
  })

  // ── delete: 2-branch ──
  it('delete EMPTY branch confirms then DELETEs (count 0)', async () => {
    const { onMutated } = renderPanel(ORG_EMPTY)
    fireEvent.click(screen.getByTestId('org-action-delete'))
    expect(screen.getByTestId('org-delete-warning')).toBeDefined()
    expect(screen.queryByTestId('org-delete-blocked')).toBeNull()
    fireEvent.click(screen.getByTestId('org-delete-confirm'))
    await waitFor(() => expect(api.del).toHaveBeenCalledWith('/api/admin/organizations/STY03'))
    await waitFor(() => expect(onMutated).toHaveBeenCalledWith(null))
  })

  it('delete BLOCKED branch (count > 0) shows the count and never calls DELETE', () => {
    renderPanel(ORG) // STY02 has 6 employees
    fireEvent.click(screen.getByTestId('org-action-delete'))
    const blocked = screen.getByTestId('org-delete-blocked')
    expect(blocked.textContent).toContain('6 medarbejdere')
    expect(screen.queryByTestId('org-delete-confirm')).toBeNull()
    expect(api.del).not.toHaveBeenCalled()
  })

  it('a 422 on an optimistically-empty delete FLIPS to the blocked branch with the server count', async () => {
    api.del.mockResolvedValue({ ok: false, status: 422, error: JSON.stringify({ employeeCount: 3 }) })
    const { onMutated } = renderPanel(ORG_EMPTY)
    fireEvent.click(screen.getByTestId('org-action-delete'))
    fireEvent.click(screen.getByTestId('org-delete-confirm'))
    await waitFor(() => expect(screen.getByTestId('org-delete-blocked')).toBeDefined())
    expect(screen.getByTestId('org-delete-blocked').textContent).toContain('3 medarbejdere')
    expect(onMutated).not.toHaveBeenCalled()
  })

  it('surfaces a 409 dup-name inline on create (no refetch)', async () => {
    api.post.mockResolvedValue({ ok: false, status: 409, error: 'dup' })
    const { onMutated } = renderPanel(MAO)
    fireEvent.click(screen.getByTestId('unit-action-create'))
    fireEvent.change(screen.getByTestId('org-create-name'), { target: { value: 'Statens IT' } })
    fireEvent.click(screen.getByTestId('org-create-submit'))
    await waitFor(() => expect(screen.getByTestId('org-create-error')).toBeDefined())
    expect(screen.getByTestId('org-create-error').textContent).toMatch(/findes allerede/)
    expect(onMutated).not.toHaveBeenCalled()
  })
})

describe('Capability-gating per role (TASK-10803) — the live floors', () => {
  // LocalHR: UNIT affordances only; NO org mutations.
  it('LocalHR sees the unit create but NO org rename/move/delete', () => {
    auth.role = 'LocalHR'
    // On a MAO: the "+ Organisation" create is DISABLED (below the LocalAdmin floor)…
    const { unmount } = renderPanel(MAO)
    expect((screen.getByTestId('unit-action-create') as HTMLButtonElement).disabled).toBe(true)
    expect(screen.queryByTestId('org-action-rename')).toBeNull()
    expect(screen.queryByTestId('org-action-delete')).toBeNull()
    unmount()
    // …on an Organisation: the UNIT "+ Direktion" shows, but no org mutations.
    renderPanel(ORG)
    expect(screen.getByTestId('unit-action-create').textContent).toContain('Direktion')
    expect(screen.queryByTestId('org-action-rename')).toBeNull()
    expect(screen.queryByTestId('org-action-move')).toBeNull()
    expect(screen.queryByTestId('org-action-delete')).toBeNull()
  })

  // LocalAdmin: + org create/rename; NOT move/delete (those are GlobalAdmin).
  it('LocalAdmin adds org create + rename, but NOT move/delete', () => {
    auth.role = 'LocalAdmin'
    const { unmount } = renderPanel(MAO)
    expect((screen.getByTestId('unit-action-create') as HTMLButtonElement).disabled).toBe(false) // org-create enabled
    expect(screen.getByTestId('org-action-rename')).toBeDefined()
    expect(screen.queryByTestId('org-action-delete')).toBeNull() // GlobalAdmin-only
    unmount()
    renderPanel(ORG)
    expect(screen.getByTestId('org-action-rename')).toBeDefined()
    expect(screen.queryByTestId('org-action-move')).toBeNull() // GlobalAdmin-only
    expect(screen.queryByTestId('org-action-delete')).toBeNull() // GlobalAdmin-only
  })

  // GlobalAdmin: everything.
  it('GlobalAdmin sees create + rename + move + delete', () => {
    auth.role = 'GlobalAdmin'
    const { unmount } = renderPanel(MAO)
    expect((screen.getByTestId('unit-action-create') as HTMLButtonElement).disabled).toBe(false)
    expect(screen.getByTestId('org-action-rename')).toBeDefined()
    expect(screen.getByTestId('org-action-delete')).toBeDefined()
    expect(screen.queryByTestId('org-action-move')).toBeNull() // a MAO is a root → no Flyt
    unmount()
    renderPanel(ORG)
    expect(screen.getByTestId('org-action-rename')).toBeDefined()
    expect(screen.getByTestId('org-action-move')).toBeDefined()
    expect(screen.getByTestId('org-action-delete')).toBeDefined()
  })

  // A below-floor role sees no action row at all.
  it('an Employee sees no action row', () => {
    auth.role = 'Employee'
    renderPanel(ORG)
    expect(screen.queryByTestId('unit-action-row')).toBeNull()
    expect(screen.queryByTestId('org-action-rename')).toBeNull()
  })
})

describe('MaoCreateAction (TASK-10802) — the top-level MAO-create', () => {
  it('creates a top-level MAO (parent-less) and refetches the forest', async () => {
    const onCreated = vi.fn()
    render(<MaoCreateAction onCreated={onCreated} />)
    fireEvent.click(screen.getByTestId('mao-create-button'))
    fireEvent.change(screen.getByTestId('org-create-name'), { target: { value: 'Klimaministeriet' } })
    fireEvent.click(screen.getByTestId('org-create-submit'))
    await waitFor(() =>
      expect(api.post).toHaveBeenCalledWith('/api/admin/organizations', {
        orgName: 'Klimaministeriet',
        orgType: 'MAO',
        parentOrgId: null,
      }),
    )
    await waitFor(() => expect(onCreated).toHaveBeenCalled())
  })
})
