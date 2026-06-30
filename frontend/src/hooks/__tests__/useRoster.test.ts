// SPRINT-107 / TASK-10705 — the real-shape hook test for useRoster: confirms the
// INLINE literal URL (the PAT-010 contract-lint requirement — a path-helper const
// would evade tools/check_endpoint_contracts.py), that the parsed
// `{ employees, pendingCountByManager, nameResolution }` envelope is surfaced with
// the FULL unit-tag field-set (unitId/unitName/leaderIds/primaryReportingLineVersion
// /outgoingVikar) mirroring the backend's actual JSON (the S97→S99→S100 drift fix:
// the FE mock must NOT diverge from the backend — `satisfies RosterResponse` pins
// it at compile time; RosterEndpointContractTests pins the backend at runtime), and
// the lazy per-Organisation CACHE (a second load is a no-op).
import { describe, it, expect, vi, beforeEach } from 'vitest'
import { renderHook, act } from '@testing-library/react'
import type { ApiResult } from '../../lib/api'
import type { RosterResponse } from '../useRoster'

const UNIT_ID = 'e7000000-0000-0000-0000-0000000000a1'

const { mockGet, ROSTER } = vi.hoisted(() => {
  // Mirrors the backend's serialized wire JSON VERBATIM (see
  // AdminEndpoints `/reporting-lines/tree/{}/medarbejdere` + RosterEndpointContractTests):
  // a typical member row (null outgoingVikar, a Number etag) + a leader row (a POPULATED
  // outgoingVikar object), plus the pendingCountByManager + by-id nameResolution maps.
  const roster: RosterResponse = {
    employees: [
      {
        employeeId: 'roster_member',
        displayName: 'Roster Member',
        position: 'Kontorchef',
        structuralApproverId: 'roster_leader',
        periodStatus: 'OPEN',
        outgoingVikar: null,
        isRoot: false,
        isOrphan: false,
        unitId: 'e7000000-0000-0000-0000-0000000000a1',
        unitName: 'Roster Enhed',
        leaderIds: ['roster_leader'],
        primaryReportingLineVersion: 1,
      },
      {
        employeeId: 'roster_leader',
        displayName: 'Roster Leader',
        position: null,
        structuralApproverId: null,
        periodStatus: 'OPEN',
        outgoingVikar: {
          vikarUserId: 'roster_vikar',
          vikarDisplayName: 'Roster Vikar',
          untilDate: '2099-12-31',
          reason: 'FERIE',
        },
        isRoot: true,
        isOrphan: false,
        unitId: 'e7000000-0000-0000-0000-0000000000a1',
        unitName: 'Roster Enhed',
        leaderIds: ['roster_leader'],
        primaryReportingLineVersion: null,
      },
    ],
    pendingCountByManager: { roster_leader: 0 },
    nameResolution: {
      roster_leader: {
        userId: 'roster_leader',
        displayName: 'Roster Leader',
        position: null,
        unitName: 'Roster Enhed',
      },
    },
  }
  return { mockGet: vi.fn(), ROSTER: roster }
})

vi.mock('../../lib/api', () => ({
  apiClient: { get: (...args: unknown[]) => mockGet(...args) },
}))

// Imported AFTER the mock is registered.
import { useRoster } from '../useRoster'

beforeEach(() => {
  mockGet.mockReset()
})

describe('useRoster', () => {
  it('fetches from the INLINE literal URL, surfaces the real envelope + unit-tag field-set, and caches per Organisation', async () => {
    mockGet.mockResolvedValue({ ok: true, data: ROSTER } satisfies ApiResult<RosterResponse>)

    const { result } = renderHook(() => useRoster())

    await act(async () => {
      await result.current.loadRoster('STY02')
    })

    // S111 / TASK-11102 — typed call: the TEMPLATED path KEY + the structured
    // `params.path` shape (apiClient interpolates `{organisationId}`, URL-encoding).
    expect(mockGet).toHaveBeenCalledWith(
      '/api/admin/reporting-lines/tree/{organisationId}/medarbejdere',
      { params: { path: { organisationId: 'STY02' } } },
    )

    const roster = result.current.byOrg['STY02']
    expect(roster).toBeDefined()
    expect(roster.employees).toHaveLength(2)

    // The member row — the full unit-tag field-set on the real shape.
    const member = roster.employees.find((e) => e.employeeId === 'roster_member')!
    expect(member.unitId).toBe(UNIT_ID)
    expect(member.unitName).toBe('Roster Enhed')
    expect(member.leaderIds).toEqual(['roster_leader'])
    expect(member.primaryReportingLineVersion).toBe(1)
    expect(member.outgoingVikar).toBeNull()

    // The leader row — a POPULATED outgoingVikar nested object.
    const leader = roster.employees.find((e) => e.employeeId === 'roster_leader')!
    expect(leader.outgoingVikar?.vikarUserId).toBe('roster_vikar')
    expect(leader.primaryReportingLineVersion).toBeNull()

    // The by-id maps surface.
    expect(roster.pendingCountByManager.roster_leader).toBe(0)
    expect(roster.nameResolution.roster_leader.displayName).toBe('Roster Leader')
    expect(result.current.error).toBeNull()

    // The cache: a second load for the SAME Organisation is a no-op (no refetch).
    await act(async () => {
      await result.current.loadRoster('STY02')
    })
    expect(mockGet).toHaveBeenCalledTimes(1)
  })

  it('surfaces the error on a failed read and caches nothing for that Organisation', async () => {
    mockGet.mockResolvedValue({ ok: false, error: 'HTTP 403', status: 403 } satisfies ApiResult<RosterResponse>)

    const { result } = renderHook(() => useRoster())

    await act(async () => {
      await result.current.loadRoster('STY02')
    })

    expect(result.current.error).toBe('HTTP 403')
    expect(result.current.byOrg['STY02']).toBeUndefined()
  })
})
