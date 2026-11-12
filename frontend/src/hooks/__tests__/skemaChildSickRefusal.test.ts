// S148 / TASK-14812 (QUAL-236) — kind R (a regression pin: it passes on today's code, by design).
// The server's child's-sick refusals reuse the `absence_type_not_eligible` body and add a `kind`
// the client does not map (child-sick-not-offered, -chain, -illness-start, -call-home-without-work).
// The save's 422 parse must (a) surface the server's Danish `message` unchanged on
// `absenceRuleError`, and (b) not route the body into the quota branch (which needs `remaining`
// and `requested`). A change that started to key the alert on `kind`, or to read these bodies as
// a quota, turns it red.
import { describe, it, expect, vi, beforeEach } from 'vitest'
import { renderHook, waitFor, act } from '@testing-library/react'
import { useSkema } from '../useSkema'

const mockFetch = vi.fn()
vi.stubGlobal('fetch', mockFetch)

const mockStorage: Record<string, string> = { statstid_token: 'test-token' }
vi.stubGlobal('localStorage', {
  getItem: (k: string) => mockStorage[k] ?? null,
  setItem: (k: string, v: string) => { mockStorage[k] = v },
  removeItem: (k: string) => { delete mockStorage[k] },
})

const month = {
  year: 2026, month: 3, daysInMonth: 31, projects: [],
  absenceTypes: [{ type: 'CHILD_SICK_DAY_2', label: 'Barns 2. sygedag', fullDayOnly: false }],
  entries: [], absences: [], approval: null, employeeDeadline: null, managerDeadline: null,
  workTime: [], dailyNorm: [], consumptionBasis: [],
}

beforeEach(() => {
  mockFetch.mockReset()
  mockFetch.mockImplementation(async () => ({
    ok: true, status: 200, headers: new Headers(), json: async () => month, text: async () => JSON.stringify(month),
  }))
})

describe('useSkema save 422 — the child\'s-sick refusal bodies', () => {
  it.each([
    'child-sick-not-offered',
    'child-sick-chain',
    'child-sick-illness-start',
    'child-sick-call-home-without-work',
  ])('R: kind %s surfaces the server message unchanged and is not a quota error', async (kind) => {
    const message = `Barns 2. sygedag kan ikke registreres den 10-03-2026 (${kind}).`
    const refusal = { error: 'absence_type_not_eligible', absenceType: 'CHILD_SICK_DAY_2', date: '2026-03-10', message, kind }
    const { result } = renderHook(() => useSkema('emp001', 2026, 3))
    await waitFor(() => expect(result.current.data?.month).toBe(3))
    mockFetch.mockImplementationOnce(async () => ({
      ok: false, status: 422, headers: new Headers(), json: async () => refusal, text: async () => JSON.stringify(refusal),
    }))
    await act(async () => {
      await result.current.saveBatch({
        year: 2026, month: 3, entries: null,
        absences: [{ date: '2026-03-10', absenceType: 'CHILD_SICK_DAY_2', hours: 7.4 }], workTime: null,
      })
    })
    expect(result.current.absenceRuleError).toMatchObject({
      error: 'absence_type_not_eligible', absenceType: 'CHILD_SICK_DAY_2', date: '2026-03-10', message,
    })
    expect(result.current.quotaError).toBeNull()
  })
})
