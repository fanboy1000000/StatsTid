// S147 / TASK-14718 (QUAL-241) fix-up 4 — saveBatch posts EXACTLY the batch it is given, under the
// batch's own month. The page classifies a cell (absence vs project) and fixes its month when the
// edit is QUEUED; this pins the hook half of that contract in isolation: a later change of the
// hook's absence-type list, or of the month the hook is rendered for, cannot change what a queued
// batch sends. (`saveMonth`, the legacy entry point, still classifies at call time — pinned too, as
// the contrast.)
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

function monthBody(year: number, month: number, absenceTypes: string[]) {
  return {
    year,
    month,
    daysInMonth: 30,
    projects: [],
    absenceTypes: absenceTypes.map((type) => ({ type, label: type, fullDayOnly: false })),
    entries: [],
    absences: [],
    approval: null,
    employeeDeadline: null,
    managerDeadline: null,
    workTime: [],
    dailyNorm: [],
    consumptionBasis: [],
  }
}

let served: ReturnType<typeof monthBody>
let postBodies: Array<Record<string, unknown>>

beforeEach(() => {
  mockFetch.mockReset()
  postBodies = []
  served = monthBody(2026, 3, ['VACATION'])
  mockFetch.mockImplementation(async (url: string, init?: RequestInit) => {
    const isPost = (init?.method ?? 'GET') === 'POST'
    if (isPost && String(url).includes('/save')) postBodies.push(JSON.parse(String(init?.body)))
    const body = isPost ? {} : served
    return {
      ok: true,
      status: 200,
      headers: new Headers(),
      json: async () => body,
      text: async () => JSON.stringify(body),
    }
  })
})

describe('useSkema.saveBatch — what was queued is what is sent', () => {
  it('B: a batch classified as an ABSENCE is posted as an absence, in ITS month, after the hook\'s absence-type list changed and the hook moved to another month', async () => {
    // Kind B (behaviour pin; it ALSO passes against 0974b286, by design — it isolates the hook
    // half of "classified and enveloped when queued", which the page pin cannot isolate because
    // the April load waits for the save queue).
    // Red conditions (mutation, NOT run — a hypothesis, not evidence): make saveBatch classify
    // its rows from absenceTypesRef, or take year/month from the hook's closure; the absence
    // would then go out as a project entry / under April.
    const { result, rerender } = renderHook(({ y, m }) => useSkema('emp001', y, m), {
      initialProps: { y: 2026, m: 3 },
    })
    await waitFor(() => expect(result.current.data?.month).toBe(3))
    const queued = {
      year: 2026,
      month: 3,
      entries: null,
      absences: [{ date: '2026-03-05', absenceType: 'VACATION', hours: 3 }],
      workTime: null,
    }
    // The hook now holds April data whose list has no VACATION, and is rendered for April.
    served = monthBody(2026, 4, [])
    rerender({ y: 2026, m: 4 })
    await waitFor(() => expect(result.current.data?.month).toBe(4))

    await act(async () => { await result.current.saveBatch(queued) })
    expect(postBodies).toHaveLength(1)
    expect(postBodies[0].year).toBe(2026)
    expect(postBodies[0].month).toBe(3)
    expect(postBodies[0].absences).toEqual([{ date: '2026-03-05', absenceType: 'VACATION', hours: 3 }])
    expect(postBodies[0].entries).toBeNull()
  })

  it('contrast: saveMonth classifies at call time with the list it holds then', async () => {
    // Kind B (a contrast pin: it documents the legacy entry point, and passes against every
    // earlier commit; it has no Red condition of its own).
    const { result } = renderHook(() => useSkema('emp001', 2026, 3))
    await waitFor(() => expect(result.current.data?.month).toBe(3))
    await act(async () => {
      await result.current.saveMonth([{ rowKey: 'VACATION', date: '2026-03-05', hours: 3 }])
    })
    expect(postBodies[0].absences).toEqual([{ date: '2026-03-05', absenceType: 'VACATION', hours: 3 }])
  })

  it('F: a failed save is reported with its month and days, and clears only on a success for the SAME month', async () => {
    // Red condition: against 0974b286 the action error has no `dates` and ANY successful save
    // (here: April's) cleared it.
    const { result } = renderHook(() => useSkema('emp001', 2026, 3))
    await waitFor(() => expect(result.current.data?.month).toBe(3))
    mockFetch.mockImplementationOnce(async () => ({
      ok: false,
      status: 500,
      headers: new Headers(),
      json: async () => ({ error: 'boom' }),
      text: async () => 'boom',
    }))
    // Fails: the next POST in line (the first call after the GET above).
    await act(async () => {
      await result.current.saveBatch({
        year: 2026, month: 3, entries: [{ date: '2026-03-03', projectCode: 'UDV', hours: 2 }], absences: null, workTime: null,
      })
    })
    expect(result.current.actionError).toMatchObject({ kind: 'save', year: 2026, month: 3, dates: ['2026-03-03'] })
    // A success for ANOTHER month does not clear it…
    await act(async () => {
      await result.current.saveBatch({
        year: 2026, month: 4, entries: [{ date: '2026-04-03', projectCode: 'UDV', hours: 1 }], absences: null, workTime: null,
      })
    })
    expect(result.current.actionError).not.toBeNull()
    // …a success for the SAME month does.
    await act(async () => {
      await result.current.saveBatch({
        year: 2026, month: 3, entries: [{ date: '2026-03-03', projectCode: 'UDV', hours: 2 }], absences: null, workTime: null,
      })
    })
    expect(result.current.actionError).toBeNull()
  })
})

describe('useSkema — dropped-save notices persist (S147 TASK-14720)', () => {
  const batch = (month: number, ...days: number[]) => ({
    year: 2026,
    month,
    entries: days.map((d) => ({ date: `2026-0${month}-${String(d).padStart(2, '0')}`, projectCode: 'UDV', hours: 1 })),
    absences: null,
    workTime: null,
  })
  const failNextPost = (message = 'boom') =>
    mockFetch.mockImplementationOnce(async () => ({
      ok: false,
      status: 500,
      headers: new Headers(),
      json: async () => ({ error: message }),
      text: async () => message,
    }))

  it('F: March\'s dropped save stays through a later April failure AND an April success; days merge into one notice; only a covering March success or dismissal clears it', async () => {
    // Kind F. Red condition: against 782e2285 the hook has no `droppedSaves` (the assertions on
    // it fail), and the one action-error slot is overwritten by April's failure and cleared by
    // April's success. Mutation (not run): put the dropped notice back into the shared slot.
    const { result, rerender } = renderHook(({ m }) => useSkema('emp001', 2026, m), { initialProps: { m: 3 } })
    await waitFor(() => expect(result.current.data?.month).toBe(3))
    served = monthBody(2026, 4, ['VACATION'])
    rerender({ m: 4 })
    await waitFor(() => expect(result.current.data?.month).toBe(4))

    // March's save fails AFTER the person moved to April: dropped, one notice per month.
    failNextPost('offline')
    await act(async () => { await result.current.saveBatch(batch(3, 3)) })
    failNextPost('offline')
    await act(async () => { await result.current.saveBatch(batch(3, 5)) })
    expect(result.current.droppedSaves).toEqual([
      { year: 2026, month: 3, dates: ['2026-03-03', '2026-03-05'], message: 'offline' },
    ])
    expect(result.current.actionError).toBeNull()

    // An April failure takes the retryable slot, not March's notice; an April success clears
    // the slot only.
    failNextPost('april down')
    await act(async () => { await result.current.saveBatch(batch(4, 2)) })
    expect(result.current.actionError).toMatchObject({ kind: 'save', month: 4 })
    expect(result.current.droppedSaves).toHaveLength(1)
    await act(async () => { await result.current.saveBatch(batch(4, 2)) })
    expect(result.current.actionError).toBeNull()
    expect(result.current.droppedSaves).toHaveLength(1)
    // A timeout report does not touch it either.
    act(() => result.current.reportActionError({ kind: 'save', message: 'x', year: 2026, month: 4, uncertain: true }))
    expect(result.current.droppedSaves).toHaveLength(1)

    // A March success covering only SOME of the days keeps it…
    await act(async () => { await result.current.saveBatch(batch(3, 3)) })
    expect(result.current.droppedSaves).toHaveLength(1)
    // …one covering EVERY listed day clears it.
    await act(async () => { await result.current.saveBatch(batch(3, 3, 5, 9)) })
    expect(result.current.droppedSaves).toEqual([])
  })

  it('F: dismissing a dropped-save notice removes exactly that month\'s notice', async () => {
    // Kind F. Red condition: against 782e2285 `dismissDroppedSave` does not exist.
    const { result, rerender } = renderHook(({ m }) => useSkema('emp001', 2026, m), { initialProps: { m: 3 } })
    await waitFor(() => expect(result.current.data?.month).toBe(3))
    served = monthBody(2026, 5, [])
    rerender({ m: 5 })
    await waitFor(() => expect(result.current.data?.month).toBe(5))
    failNextPost()
    await act(async () => { await result.current.saveBatch(batch(3, 3)) })
    failNextPost()
    await act(async () => { await result.current.saveBatch(batch(4, 3)) })
    expect(result.current.droppedSaves.map((n) => n.month)).toEqual([3, 4])
    act(() => result.current.dismissDroppedSave(2026, 3))
    expect(result.current.droppedSaves.map((n) => n.month)).toEqual([4])
  })

  it('F: a retryable save failure becomes a persistent notice once the person has moved to another month', async () => {
    // Kind F. Red condition: against 782e2285 it stays in the one slot, where the next failure
    // (any month) overwrites it, while its pending edits are dropped on the other month's load.
    const { result, rerender } = renderHook(({ m }) => useSkema('emp001', 2026, m), { initialProps: { m: 3 } })
    await waitFor(() => expect(result.current.data?.month).toBe(3))
    failNextPost()
    await act(async () => { await result.current.saveBatch(batch(3, 3)) })
    expect(result.current.actionError).toMatchObject({ kind: 'save', month: 3 }) // on screen: retryable
    served = monthBody(2026, 4, [])
    rerender({ m: 4 })
    await waitFor(() => expect(result.current.droppedSaves).toHaveLength(1))
    expect(result.current.actionError).toBeNull()
  })
})
