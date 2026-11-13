// S148 / TASK-14814 fix-up 1 — a failed compliance call must not leave the PREVIOUS month's result
// in state: the always-mounted live region would announce last month's warnings under this month's
// title (neither screen renders the hook's `error`).
//
// Kind F. Red conditions: before the fix the failure branch sets only the error, so after March
// succeeds and April fails, `result.current.result` is still March's result (not null).
import { describe, it, expect, vi } from 'vitest'
import { renderHook, waitFor } from '@testing-library/react'
import { useCompliance } from '../useCompliance'

const mockFetch = vi.fn()
vi.stubGlobal('fetch', mockFetch)
vi.stubGlobal('localStorage', {
  getItem: (k: string) => (k === 'statstid_token' ? 'test-token' : null),
  setItem: () => {},
  removeItem: () => {},
})

const respond = (status: number, body: unknown) => ({
  ok: status >= 200 && status < 300,
  status,
  headers: new Headers(),
  json: async () => body,
  text: async () => JSON.stringify(body),
})

describe('useCompliance — a failed fetch clears the previous month', () => {
  it('March succeeds, April fails: result is null and error is set', async () => {
    const march = {
      ruleId: 'EU_WTD',
      employeeId: 'emp001',
      success: false,
      violations: [],
      warnings: [
        {
          violationType: 0,
          date: '2026-03-10',
          actualValue: 9,
          thresholdValue: 11,
          severity: 0,
          isVoluntaryExempt: false,
          message: 'Marts-advarsel.',
        },
      ],
      restNotCheckedDays: [],
    }
    mockFetch.mockImplementation(async (url: string) =>
      url.includes('month=3') ? respond(200, march) : respond(500, { error: 'boom' }),
    )
    const { result, rerender } = renderHook(({ m }) => useCompliance('emp001', 2026, m), {
      initialProps: { m: 3 },
    })
    await waitFor(() => expect(result.current.result).not.toBeNull())
    rerender({ m: 4 })
    await waitFor(() => expect(result.current.error).not.toBeNull())
    expect(result.current.result).toBeNull()
  })
})
