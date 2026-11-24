// S148 T23 FU-2 (C2-R1 d). Kind F. Red condition on a10cd057: the hook resets to `loading` inside an
// effect, so the render right after a key change still returns the previous key's loaded date.
import { describe, it, expect, vi, beforeEach } from 'vitest'
import { act, render, waitFor } from '@testing-library/react'
import { useNewestVersionDate, type NewestVersionDate } from '../useAgreementConfigs'

const mockFetch = vi.fn()
vi.stubGlobal('fetch', mockFetch)

const version = (appliesFrom: string) => ({
  ok: true,
  status: 200,
  headers: new Headers(),
  json: async () => [{ publishedAt: '2026-11-01T08:00:00Z', appliesFrom }],
})

function Probe({ code, seen }: { code: string; seen: { code: string; value: NewestVersionDate }[] }) {
  const value = useNewestVersionDate(code, 'OK24')
  seen.push({ code, value })
  return null
}

beforeEach(() => {
  mockFetch.mockReset()
})

describe('useNewestVersionDate', () => {
  it('no render for a new key returns the previous key\'s result', async () => {
    mockFetch.mockImplementation(async (url: string) =>
      url.includes('/AC/') ? version('2026-11-02') : version('2026-11-09'),
    )
    const seen: { code: string; value: NewestVersionDate }[] = []
    const { rerender } = render(<Probe code="AC" seen={seen} />)
    await waitFor(() => expect(seen[seen.length - 1]?.value.date).toBe('2026-11-02'))

    seen.length = 0
    rerender(<Probe code="HK" seen={seen} />)
    await waitFor(() => expect(seen[seen.length - 1]?.value.date).toBe('2026-11-09'))
    // Every render for HK before its own answer arrived is `loading` with no date.
    const beforeAnswer = seen.filter((s) => s.value.status !== 'loaded')
    expect(seen[0].value).toMatchObject({ status: 'loading', date: null })
    expect(beforeAnswer.every((s) => s.value.date === null)).toBe(true)
    // Every HK render is loading with no date, or HK's own answer.
    expect(seen.every((s) => (s.value.status === 'loading' ? s.value.date === null : s.value.date === '2026-11-09'))).toBe(true)
  })

  it('after `retry` no render returns the failed attempt\'s result', async () => {
    let calls = 0
    mockFetch.mockImplementation(async () => {
      calls += 1
      return calls === 1 ? { ok: false, status: 500, headers: new Headers(), text: async () => 'boom' } : version('2026-11-02')
    })
    const seen: { code: string; value: NewestVersionDate }[] = []
    let retry: () => void = () => {}
    function Retrying() {
      const value = useNewestVersionDate('AC', 'OK24')
      retry = value.retry
      seen.push({ code: 'AC', value })
      return null
    }
    render(<Retrying />)
    await waitFor(() => expect(seen[seen.length - 1]?.value.status).toBe('failed'))

    seen.length = 0
    act(() => retry())
    await waitFor(() => expect(seen[seen.length - 1]?.value.date).toBe('2026-11-02'))
    // Every render after the click is loading with no date, or the new attempt's own answer; none is
    // the failed attempt.
    expect(seen[0].value).toMatchObject({ status: 'loading', date: null })
    expect(
      seen.every((s) => (s.value.status === 'loading' ? s.value.date === null : s.value.status === 'loaded' && s.value.date === '2026-11-02')),
    ).toBe(true)
  })
})
