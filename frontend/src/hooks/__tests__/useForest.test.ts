// SPRINT-107 / TASK-10702 — a LIGHT hook test for useForest: confirms the
// INLINE literal URL (the PAT-010 contract-lint requirement — a path-helper const
// would evade tools/check_endpoint_contracts.py) and that the parsed forest is
// surfaced from the real `{ forest: [...] }` envelope. The fuller real-shape hook
// + contract-lint coverage is TASK-10705.
import { describe, it, expect, vi, beforeEach } from 'vitest'
import { renderHook, waitFor } from '@testing-library/react'
import type { ApiResult } from '../../lib/api'
import type { ForestResponse } from '../useForest'

const { mockGet, FOREST } = vi.hoisted(() => {
  const forest: ForestResponse = {
    forest: [
      {
        orgId: 'MIN01',
        orgName: 'Finansministeriet',
        orgType: 'MAO',
        parentOrgId: null,
        materializedPath: '/MIN01/',
        memberCount: 42,
        organisations: [],
      },
    ],
  }
  return { mockGet: vi.fn(), FOREST: forest }
})

vi.mock('../../lib/api', () => ({
  apiClient: { get: (...args: unknown[]) => mockGet(...args) },
}))

// Imported AFTER the mock is registered.
import { useForest } from '../useForest'

beforeEach(() => {
  mockGet.mockReset()
})

describe('useForest', () => {
  it('fetches the forest from the INLINE literal URL and surfaces the parsed forest', async () => {
    mockGet.mockResolvedValue({ ok: true, data: FOREST } satisfies ApiResult<ForestResponse>)

    const { result } = renderHook(() => useForest())

    await waitFor(() => expect(result.current.loading).toBe(false))

    // The URL MUST be the inline literal (lint-enumerable).
    expect(mockGet).toHaveBeenCalledWith('/api/admin/units/forest')
    expect(result.current.forest).toHaveLength(1)
    expect(result.current.forest[0].orgId).toBe('MIN01')
    expect(result.current.error).toBeNull()
  })

  it('surfaces the error on a failed read', async () => {
    mockGet.mockResolvedValue({ ok: false, error: 'HTTP 403', status: 403 } satisfies ApiResult<ForestResponse>)

    const { result } = renderHook(() => useForest())

    await waitFor(() => expect(result.current.loading).toBe(false))
    expect(result.current.error).toBe('HTTP 403')
    expect(result.current.forest).toHaveLength(0)
  })
})
