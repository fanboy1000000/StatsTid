// SPRINT-107 / TASK-10704 — a LIGHT hook test for useSearch: confirms the INLINE
// literal URL (the PAT-010 contract-lint requirement — a path-helper const would
// evade tools/check_endpoint_contracts.py; the `?q=...` query is interpolated but
// the path stays a literal prefix), the debounce, and that the parsed
// `{ units, people }` envelope is surfaced. The fuller real-shape hook + contract
// -lint registration is TASK-10705.
import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest'
import { renderHook, act } from '@testing-library/react'
import type { ApiResult } from '../../lib/api'
import type { SearchResponse } from '../useSearch'

const { mockGet, RESULTS } = vi.hoisted(() => {
  const results: SearchResponse = {
    units: [{ unitId: 'u1', organisationId: 'STY02', type: 'kontor', name: 'Vejledning', path: ['Statens IT'] }],
    people: [{ userId: 'p1', organisationId: 'STY02', displayName: 'Jens Vej', position: 'Kontorchef', unitName: 'Vejledning', path: ['Statens IT', 'Vejledning'] }],
    unitsTotal: 1,
    peopleTotal: 1,
  }
  return { mockGet: vi.fn(), RESULTS: results }
})

vi.mock('../../lib/api', () => ({
  apiClient: { get: (...args: unknown[]) => mockGet(...args) },
}))

import { useSearch } from '../useSearch'

beforeEach(() => {
  mockGet.mockReset()
  vi.useFakeTimers()
})
afterEach(() => {
  vi.useRealTimers()
})

describe('useSearch', () => {
  it('debounces, then fetches from the INLINE literal URL and surfaces { units, people }', async () => {
    mockGet.mockResolvedValue({ ok: true, data: RESULTS } satisfies ApiResult<SearchResponse>)

    const { result } = renderHook(() => useSearch())

    act(() => result.current.setQuery('vej'))
    // Not yet fired (debounced).
    expect(mockGet).not.toHaveBeenCalled()

    // Run the debounce timer + flush the awaited fetch microtasks.
    await act(async () => {
      await vi.runAllTimersAsync()
    })

    // The URL is the inline literal prefix with q interpolated (lint normalizes the query off).
    expect(mockGet).toHaveBeenCalledWith('/api/admin/search?q=vej')
    expect(result.current.results.units).toHaveLength(1)
    expect(result.current.results.people).toHaveLength(1)
    expect(result.current.results.units[0].organisationId).toBe('STY02')
    // S110 — the per-section totals surface for the truncation signal.
    expect(result.current.results.unitsTotal).toBe(1)
    expect(result.current.results.peopleTotal).toBe(1)
  })

  it('an empty/blank query resets to the idle empty result without a request', async () => {
    mockGet.mockResolvedValue({ ok: true, data: RESULTS } satisfies ApiResult<SearchResponse>)
    const { result } = renderHook(() => useSearch())

    act(() => result.current.setQuery('   '))
    await act(async () => {
      await vi.runAllTimersAsync()
    })
    expect(mockGet).not.toHaveBeenCalled()
    expect(result.current.results.units).toHaveLength(0)
    expect(result.current.results.people).toHaveLength(0)
  })

  it('url-encodes the query token in the inline URL', async () => {
    mockGet.mockResolvedValue({ ok: true, data: { units: [], people: [], unitsTotal: 0, peopleTotal: 0 } } satisfies ApiResult<SearchResponse>)
    const { result } = renderHook(() => useSearch())

    act(() => result.current.setQuery('a b'))
    await act(async () => {
      await vi.runAllTimersAsync()
    })
    expect(mockGet).toHaveBeenCalledWith('/api/admin/search?q=a%20b')
  })
})
