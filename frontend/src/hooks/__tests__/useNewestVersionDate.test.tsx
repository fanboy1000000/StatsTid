// S148 T23 FU-2 (C2-R1 d). Kind F. Red condition on a10cd057: the hook resets to `loading` inside an
// effect, so the render right after a key change still returns the previous key's loaded date.
import { describe, it, expect, vi, beforeEach } from 'vitest'
import { render, waitFor } from '@testing-library/react'
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
  })
})
