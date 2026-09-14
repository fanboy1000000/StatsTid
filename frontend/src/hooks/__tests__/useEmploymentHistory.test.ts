// S141 / TASK-14109 (refinement C2) — hook-level pins for useEmploymentHistory, over the
// TASK-14113 endpoint `GET /api/hr/employees/{employeeId}/history`. Mocks `apiClient.get`
// directly (the useSearch.test.ts idiom) rather than global `fetch`: this hook is a thin
// wrapper, and the interesting behaviour is the CALL SHAPE + the status-aware error split, not
// the wire format.
import { describe, it, expect, vi, beforeEach } from 'vitest'
import { act, renderHook, waitFor } from '@testing-library/react'
import type { ApiResult } from '../../lib/api'
import type { EmploymentHistoryResponse } from '../useEmploymentHistory'

const { mockGet } = vi.hoisted(() => ({ mockGet: vi.fn() }))
vi.mock('../../lib/api', () => ({
  apiClient: { get: (...args: unknown[]) => mockGet(...args) },
}))

import { useEmploymentHistory } from '../useEmploymentHistory'

const RESPONSE: EmploymentHistoryResponse = {
  employeeId: 'emp1',
  today: '2026-09-14',
  windowFrom: null,
  windowTo: null,
  profileHistory: [
    {
      effectiveFrom: '2020-01-01',
      effectiveTo: null,
      status: 'CURRENT',
      isInitial: true,
      changedFields: [],
      partTimeFraction: 1,
      position: 'Konsulent',
      employmentCategory: 'FULL_TIME',
    },
  ],
  agreementCodeHistory: [],
}

beforeEach(() => {
  mockGet.mockReset()
})

describe('useEmploymentHistory', () => {
  it('does not fetch while employeeId is null', () => {
    const { result } = renderHook(() => useEmploymentHistory(null))
    expect(mockGet).not.toHaveBeenCalled()
    expect(result.current.loading).toBe(false)
    expect(result.current.data).toBeNull()
  })

  it('fetches the typed history path with the employeeId path param and an unbounded query when no from/to is given', async () => {
    mockGet.mockResolvedValue({ ok: true, data: RESPONSE } satisfies ApiResult<EmploymentHistoryResponse>)

    const { result } = renderHook(() => useEmploymentHistory('emp1'))

    await waitFor(() => expect(result.current.loading).toBe(false))

    expect(mockGet).toHaveBeenCalledWith('/api/hr/employees/{employeeId}/history', {
      params: { path: { employeeId: 'emp1' } },
      query: { from: undefined, to: undefined },
    })
    expect(result.current.data).toEqual(RESPONSE)
    expect(result.current.error).toBeNull()
    expect(result.current.errorStatus).toBeNull()
  })

  it('threads from/to through to the query', async () => {
    mockGet.mockResolvedValue({ ok: true, data: RESPONSE } satisfies ApiResult<EmploymentHistoryResponse>)

    renderHook(() => useEmploymentHistory('emp1', '2025-01-01', '2025-06-01'))

    await waitFor(() =>
      expect(mockGet).toHaveBeenCalledWith('/api/hr/employees/{employeeId}/history', {
        params: { path: { employeeId: 'emp1' } },
        query: { from: '2025-01-01', to: '2025-06-01' },
      }),
    )
  })

  it('surfaces a 422 (malformed window) as an error WITH its status, not as empty data', async () => {
    mockGet.mockResolvedValue({
      ok: false,
      status: 422,
      error: JSON.stringify({ error: 'Invalid history window', reason: "'from' must be earlier than 'to'." }),
    } satisfies ApiResult<EmploymentHistoryResponse>)

    const { result } = renderHook(() => useEmploymentHistory('emp1', '2025-06-01', '2025-01-01'))

    await waitFor(() => expect(result.current.loading).toBe(false))
    expect(result.current.data).toBeNull()
    expect(result.current.errorStatus).toBe(422)
    expect(result.current.error).toContain('Invalid history window')
  })

  it('surfaces a 403 (access denied / unknown employee, indistinguishable by design) with its status', async () => {
    mockGet.mockResolvedValue({
      ok: false,
      status: 403,
      error: JSON.stringify({ error: 'Access denied', reason: 'not in scope' }),
    } satisfies ApiResult<EmploymentHistoryResponse>)

    const { result } = renderHook(() => useEmploymentHistory('emp-unknown-or-out-of-scope'))

    await waitFor(() => expect(result.current.loading).toBe(false))
    expect(result.current.data).toBeNull()
    expect(result.current.errorStatus).toBe(403)
  })

  it('an older in-flight request resolving AFTER a newer one does not clobber the newer result (stale-response guard)', async () => {
    let resolveFirst!: (v: ApiResult<EmploymentHistoryResponse>) => void
    const first = new Promise<ApiResult<EmploymentHistoryResponse>>((res) => {
      resolveFirst = res
    })
    const second: ApiResult<EmploymentHistoryResponse> = {
      ok: true,
      data: { ...RESPONSE, windowFrom: '2025-01-01' },
    }

    mockGet.mockReturnValueOnce(first).mockResolvedValueOnce(second)

    const { result, rerender } = renderHook(
      ({ from }: { from?: string }) => useEmploymentHistory('emp1', from),
      { initialProps: { from: undefined as string | undefined } },
    )

    // Change the query before the FIRST request resolves — a second, newer request goes out.
    rerender({ from: '2025-01-01' })
    await waitFor(() => expect(result.current.loading).toBe(false))
    expect(result.current.data?.windowFrom).toBe('2025-01-01')

    // The stale first request now resolves. It must NOT overwrite the newer answer.
    resolveFirst({ ok: true, data: RESPONSE })
    await new Promise((r) => setTimeout(r, 0))
    expect(result.current.data?.windowFrom).toBe('2025-01-01')
  })

  it('refetch re-issues the request on demand', async () => {
    mockGet.mockResolvedValue({ ok: true, data: RESPONSE } satisfies ApiResult<EmploymentHistoryResponse>)
    const { result } = renderHook(() => useEmploymentHistory('emp1'))
    await waitFor(() => expect(result.current.loading).toBe(false))
    expect(mockGet).toHaveBeenCalledTimes(1)

    act(() => {
      result.current.refetch()
    })
    await waitFor(() => expect(mockGet).toHaveBeenCalledTimes(2))
  })
})
