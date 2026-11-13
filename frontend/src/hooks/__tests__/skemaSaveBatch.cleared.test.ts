// S148 / TASK-14841 — AC-CL-31: the hook carries the removals of a save. `saveBatch` posts a
// `cleared` member (null/absent when the batch has none) and `saveMonth` emits an emptied or 0
// cell as a removal instead of skipping it, so the two builders agree. `batchDates` (module-
// private) is read through its observable effect: a cleared-only batch settles its day.
//
// Kind F — every assertion below fails on the code before this task: `saveBatch` ignored a
// `cleared` member (the body had none) and `saveMonth` dropped null/0 cells.
// Red conditions: the first fact's `cleared` toEqual (no member on the body), the second fact's
// `cleared.entries` / `cleared.absences` (the cells were skipped), the third fact's droppedSaves
// day set (a cleared-only batch listed no date).
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

const served = {
  year: 2026,
  month: 3,
  daysInMonth: 31,
  projects: [],
  absenceTypes: [{ type: 'VACATION', label: 'Ferie', fullDayOnly: false }],
  entries: [],
  absences: [],
  approval: null,
  employeeDeadline: null,
  managerDeadline: null,
  workTime: [],
  dailyNorm: [],
  consumptionBasis: [],
}

let postBodies: Array<Record<string, unknown>>
let saveStatus: number

beforeEach(() => {
  mockFetch.mockReset()
  postBodies = []
  saveStatus = 200
  mockFetch.mockImplementation(async (url: string, init?: RequestInit) => {
    const isPost = (init?.method ?? 'GET') === 'POST'
    let status = 200
    if (isPost && String(url).includes('/save')) {
      postBodies.push(JSON.parse(String(init?.body)))
      status = saveStatus
    }
    const body = isPost ? (status === 200 ? {} : { error: 'refused' }) : served
    return {
      ok: status === 200,
      status,
      headers: new Headers(),
      json: async () => body,
      text: async () => JSON.stringify(body),
    }
  })
})

async function loaded() {
  const hook = renderHook(() => useSkema('emp001', 2026, 3))
  await waitFor(() => expect(hook.result.current.data?.month).toBe(3))
  return hook
}

describe('useSkema — the save carries its removals (AC-CL-31, kind F)', () => {
  it('saveBatch posts the cleared member, each kind null when empty; a batch with none posts no member', async () => {
    const { result } = await loaded()
    await act(async () => {
      await result.current.saveBatch({
        year: 2026,
        month: 3,
        entries: null,
        absences: null,
        workTime: null,
        cleared: {
          entries: [{ date: '2026-03-10', projectCode: 'PRJ-1' }],
          absences: null,
          workTime: [{ date: '2026-03-11' }],
        },
      })
      await result.current.saveBatch({ year: 2026, month: 3, entries: null, absences: null, workTime: null })
    })
    expect(postBodies[0].cleared).toEqual({
      entries: [{ date: '2026-03-10', projectCode: 'PRJ-1' }],
      absences: null,
      workTime: [{ date: '2026-03-11' }],
    })
    expect(postBodies[1]).not.toHaveProperty('cleared')
  })

  it('saveMonth sends an emptied or 0 cell as a removal under its own kind, and posts the rest', async () => {
    const { result } = await loaded()
    await act(async () => {
      await result.current.saveMonth([
        { rowKey: 'PRJ-1', date: '2026-03-10', hours: null },
        { rowKey: 'VACATION', date: '2026-03-12', hours: 0 },
        { rowKey: 'PRJ-2', date: '2026-03-13', hours: 4 },
      ])
    })
    expect(postBodies).toHaveLength(1)
    expect(postBodies[0].entries).toEqual([{ date: '2026-03-13', projectCode: 'PRJ-2', hours: 4 }])
    expect(postBodies[0].cleared).toEqual({
      entries: [{ date: '2026-03-10', projectCode: 'PRJ-1' }],
      absences: [{ date: '2026-03-12', absenceType: 'VACATION' }],
      workTime: null,
    })
  })

  it('batchDates includes cleared dates: a failed cleared-only batch names its days in the dropped notice', async () => {
    const { result } = await loaded()
    saveStatus = 500
    await act(async () => {
      await result.current.saveBatch({
        year: 2026,
        month: 3,
        entries: null,
        absences: null,
        workTime: null,
        cleared: { entries: [{ date: '2026-03-10', projectCode: 'PRJ-1' }], absences: null, workTime: [{ date: '2026-03-11' }] },
        isDropped: () => true,
      })
    })
    expect(result.current.droppedSaves.flatMap((n) => n.dates)).toEqual(['2026-03-10', '2026-03-11'])
  })
})
