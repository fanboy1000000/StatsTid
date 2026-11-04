// S147 / TASK-14714 + fix-up 1 (HRP-011; AC-230-9 and AC-230-10, the screen half) - the leaver
// list's row actions and the hand-over text.
//
// THE FAILURE SHAPE IS THE CLIENT'S OWN: `apiClient` returns { ok:false, status, error } where
// `error` is the RAW response text and there is NO parsed `body` (lib/api.ts). Every refusal below
// is mocked exactly so - the first version of these tests mocked a `body` the client never sets,
// which hid the blocker.
//
// Kinds and Red conditions (RED shown against 7d53bd53's FollowUpLists.tsx):
//  1 F  the actions follow the row's status: Send for NONE/DRAFT/REJECTED, Godkend/Afvis only for
//       SUBMITTED/EMPLOYEE_APPROVED. Red: 7d53bd53 shows Send on every row and Godkend/Afvis on
//       every row with a period (a DRAFT row would show Godkend).
//  2 F  the four-eyes 403 (raw JSON text) is worded in Danish. Red: 7d53bd53 reads `r.body`
//       (undefined), so it prints the generic "ikke adgang" sentence.
//  3 F  the 422 month_outside_employment_period message is shown. Red: same cause - the generic
//       sentence is shown instead of the server's Danish message.
//  4 F  a 409 is worded from the parsed error. Red: 7d53bd53 prints "ændret af en anden".
//  5 F  the coverage 422 lists the missing days. Red: 7d53bd53 has no missingDays handling.
//  6 F  send posts the employee and month; after it, the row takes the returned status.
//       Red: 7d53bd53 does not hide Send after a send.
//  7 F  reject opens a reason box (aria-expanded), and confirming posts {reason} with the id.
//       Red: no aria-expanded on 7d53bd53's Afvis toggle.
//  8 F  "Åbn måned" toggles the real month grid and flips its label. Red: 7d53bd53 shows a
//       one-line summary, never the grid.
//  9 B  boundary pin - holds today: the aging column and the window text are unchanged.
// S147 tidy item 23b (TIDY-4, against 94bd32be; vitest does not type-check, so the old component
// simply ignores the new prop):
// 10 F  `onResolved` is called once after a successful send and once after a successful approve.
//       Red: the prop is ignored, 0 calls.
// 11 F  `onResolved` is called once after a successful reject. Red: 0 calls.
// 12 B  boundary pin - holds today: a refused action (409) never calls `onResolved`.
import { describe, it, expect, vi, beforeEach } from 'vitest'
import { screen, fireEvent, waitFor } from '@testing-library/react'
import { MemoryRouter } from 'react-router-dom'
import { renderWithCalendar } from '../../../../test/renderWithCalendar'

const api = vi.hoisted(() => ({ get: vi.fn(), post: vi.fn() }))
vi.mock('../../../../lib/api', () => ({
  apiClient: { get: api.get, post: api.post, put: vi.fn(), delete: vi.fn() },
}))

const stable = vi.hoisted(() => {
  const monthData = {
    year: 2026, month: 3, daysInMonth: 31, projects: [], absenceTypes: [], entries: [], absences: [],
    workTime: [], dailyNorm: [], approval: null, employeeDeadline: '2026-04-05', managerDeadline: '2026-04-10',
    rowPreferences: { configured: false, projects: [], absenceTypes: [] },
    catalogs: { projects: [], absenceTypes: [] }, boundaryWorkTime: [], fullDayNormAtMonthEnd: null, consumptionBasis: [],
  }
  return { data: monthData, loading: false, error: null }
})
vi.mock('../../../../hooks/useSkema', async (importOriginal) => ({
  ...(await importOriginal<typeof import('../../../../hooks/useSkema')>()),
  useSkema: () => stable,
}))

import { LeaverFinalMonthList } from '../FollowUpLists'
import type { HrFollowUpMonthItem, HrLeaverFinalMonthResponse } from '../../../../hooks/useHrFollowUp'

const PID = '11111111-1111-1111-1111-111111111111'

function item(over: Partial<HrFollowUpMonthItem> = {}): HrFollowUpMonthItem {
  return {
    employeeId: 'mette', displayName: 'Mette Leaver', orgId: 'org1', year: 2026, month: 3,
    periodStatus: 'EMPLOYEE_APPROVED', periodId: PID,
    ageAnchor: '2026-04-10', daysPastAnchor: 4, deadlineSource: 'stored',
    lastManagerDay: '2026-03-18', firstHrOnlyDay: '2026-03-19',
    ...over,
  }
}

function renderList(items: HrFollowUpMonthItem[], onResolved: () => void = () => {}) {
  const data = { today: '2026-04-14', lookbackFloor: '2025-04-14', count: items.length, items } as unknown as HrLeaverFinalMonthResponse
  return renderWithCalendar(
    '2026-04-14',
    <MemoryRouter>
      <LeaverFinalMonthList data={data} loading={false} error={null} onResolved={onResolved} />
    </MemoryRouter>,
  )
}

/** What apiClient really returns on a refusal: raw text in `error`, no `body`. */
const refusal = (status: number, body: unknown) => ({ ok: false, status, error: JSON.stringify(body) })

const T = 'list-leaver-final-month'
const K = 'mette-2026-3'
const id = (what: string) => `${T}-${what}-${K}`

beforeEach(() => {
  api.get.mockReset()
  api.post.mockReset()
})

describe('LeaverFinalMonthList row actions', () => {
  it.each([
    ['NONE', null, true, false],
    ['DRAFT', PID, true, false],
    ['REJECTED', PID, true, false],
    ['SUBMITTED', PID, false, true],
    ['EMPLOYEE_APPROVED', PID, false, true],
    ['APPROVED', PID, false, false],
  ] as const)('status %s', (periodStatus, periodId, send, decide) => {
    renderList([item({ periodStatus, periodId })])
    expect(screen.getByTestId(id('open'))).toBeInTheDocument()
    expect(!!screen.queryByTestId(id('send'))).toBe(send)
    expect(!!screen.queryByTestId(id('approve'))).toBe(decide)
    expect(!!screen.queryByTestId(id('reject'))).toBe(decide)
  })

  it('worded four-eyes refusal: the raw 403 JSON becomes a Danish sentence', async () => {
    api.post.mockResolvedValue(refusal(403, { error: 'Access denied', reason: 'the sender of a month cannot approve or reject it (four-eyes)' }))
    renderList([item()])
    fireEvent.click(screen.getByTestId(id('approve')))
    const out = await screen.findByTestId(id('outcome'))
    expect(out).toHaveTextContent('fire-øjne-princippet')
    expect(out).toHaveTextContent('en anden HR-medarbejder')
    expect(out).not.toHaveTextContent('{')
    expect(api.post).toHaveBeenCalledWith('/api/approval/{periodId}/approve', { params: { path: { periodId: PID } } })
  })

  it('shows the server message for a month outside the employment period (422)', async () => {
    api.post.mockResolvedValue(refusal(422, { error: 'month_outside_employment_period', kind: 'employment-window', message: 'Måneden ligger uden for ansættelsesperioden.' }))
    renderList([item({ periodId: null, periodStatus: 'NONE' })])
    fireEvent.click(screen.getByTestId(id('send')))
    const out = await screen.findByTestId(id('outcome'))
    expect(out).toHaveTextContent('Måneden ligger uden for ansættelsesperioden.')
    expect(out).not.toHaveTextContent('{')
  })

  it('words a 409 from the parsed error', async () => {
    api.post.mockResolvedValue(refusal(409, { error: 'Cannot approve period with status APPROVED. Only SUBMITTED or EMPLOYEE_APPROVED periods can be approved.' }))
    renderList([item()])
    fireEvent.click(screen.getByTestId(id('approve')))
    const out = await screen.findByTestId(id('outcome'))
    expect(out).toHaveTextContent('Måneden er allerede i status "Godkendt"')
    expect(out).not.toHaveTextContent('ændret af en anden')
  })

  it('lists the missing days on a coverage 422', async () => {
    api.post.mockResolvedValue(refusal(422, { kind: 'coverage', missingDays: ['2026-03-02', '2026-03-03'], coveredDays: 19, totalWorkdays: 21 }))
    renderList([item({ periodId: null, periodStatus: 'NONE' })])
    fireEvent.click(screen.getByTestId(id('send')))
    const out = await screen.findByTestId(id('outcome'))
    expect(out).toHaveTextContent('der mangler registreringer for')
    expect(out).toHaveTextContent('2.3.2026')
  })

  it('send posts the employee and month, then the row takes the returned status', async () => {
    api.post.mockResolvedValueOnce({ ok: true, data: { periodId: PID, status: 'EMPLOYEE_APPROVED' } })
    api.post.mockResolvedValueOnce({ ok: true, data: { periodId: PID, status: 'APPROVED' } })
    renderList([item({ periodId: null, periodStatus: 'NONE' })])
    fireEvent.click(screen.getByTestId(id('send')))
    const approve = await screen.findByTestId(id('approve'))
    expect(api.post).toHaveBeenCalledWith('/api/approval/send', { body: { employeeId: 'mette', year: 2026, month: 3 } })
    expect(screen.queryByTestId(id('send'))).not.toBeInTheDocument()
    fireEvent.click(approve)
    await waitFor(() => expect(screen.getByTestId(id('status'))).toHaveTextContent('Godkendt'))
    expect(screen.queryByTestId(id('send'))).not.toBeInTheDocument()
    expect(screen.queryByTestId(id('approve'))).not.toBeInTheDocument()
  })

  it('reject opens a reason box and confirming posts the reason with the period id', async () => {
    api.post.mockResolvedValue({ ok: true, data: { periodId: PID, status: 'REJECTED' } })
    renderList([item()])
    const toggle = screen.getByTestId(id('reject'))
    expect(toggle).toHaveAttribute('aria-expanded', 'false')
    fireEvent.click(toggle)
    expect(toggle).toHaveAttribute('aria-expanded', 'true')
    const box = screen.getByLabelText('Begrundelse for afvisning af Mette Leaver')
    await waitFor(() => expect(box).toHaveFocus())
    fireEvent.change(box, { target: { value: ' mangler dage ' } })
    fireEvent.click(screen.getByTestId(id('reject-confirm')))
    await waitFor(() => expect(screen.getByTestId(id('status'))).toHaveTextContent('Afvist'))
    expect(api.post).toHaveBeenCalledWith('/api/approval/{periodId}/reject', {
      params: { path: { periodId: PID } },
      body: { reason: 'mangler dage' },
    })
  })

  // S147 tidy item 23b, kind F. Red conditions: before the change LeaverFinalMonthList ignored an
  // `onResolved` prop, so every toHaveBeenCalledTimes below failed with 0 calls (vitest does not
  // type-check, so passing the prop to the old component is legal). Each success calls it once; a
  // refusal calls it never (the row's own error sentence is the only reaction).
  it('onResolved is called once after each successful send, approve and reject', async () => {
    const onResolved = vi.fn()
    api.post.mockResolvedValueOnce({ ok: true, data: { periodId: PID, status: 'EMPLOYEE_APPROVED' } })
    api.post.mockResolvedValueOnce({ ok: true, data: { periodId: PID, status: 'APPROVED' } })
    renderList([item({ periodId: null, periodStatus: 'NONE' })], onResolved)
    fireEvent.click(screen.getByTestId(id('send')))
    const approve = await screen.findByTestId(id('approve'))
    expect(onResolved).toHaveBeenCalledTimes(1)
    fireEvent.click(approve)
    await waitFor(() => expect(screen.getByTestId(id('status'))).toHaveTextContent('Godkendt'))
    expect(onResolved).toHaveBeenCalledTimes(2)
  })

  it('onResolved is called once after a successful reject', async () => {
    const onResolved = vi.fn()
    api.post.mockResolvedValue({ ok: true, data: { periodId: PID, status: 'REJECTED' } })
    renderList([item()], onResolved)
    fireEvent.click(screen.getByTestId(id('reject')))
    fireEvent.change(screen.getByLabelText('Begrundelse for afvisning af Mette Leaver'), { target: { value: 'mangler dage' } })
    fireEvent.click(screen.getByTestId(id('reject-confirm')))
    await waitFor(() => expect(screen.getByTestId(id('status'))).toHaveTextContent('Afvist'))
    expect(onResolved).toHaveBeenCalledTimes(1)
  })

  it('onResolved is NOT called when the server refuses (boundary pin - holds today)', async () => {
    const onResolved = vi.fn()
    api.post.mockResolvedValue(refusal(409, { error: 'Cannot approve period with status APPROVED.' }))
    renderList([item()], onResolved)
    fireEvent.click(screen.getByTestId(id('approve')))
    await waitFor(() => expect(screen.getByTestId(id('outcome'))).toBeInTheDocument())
    expect(onResolved).not.toHaveBeenCalled()
  })

  it('"Åbn måned" toggles the real month grid and flips its label', () => {
    renderList([item()])
    const toggle = screen.getByTestId(id('open'))
    expect(toggle).toHaveTextContent('Åbn måned')
    expect(toggle).toHaveAttribute('aria-expanded', 'false')
    expect(screen.queryByTestId(id('grid'))).not.toBeInTheDocument()
    fireEvent.click(toggle)
    expect(toggle).toHaveTextContent('Skjul måned')
    expect(toggle).toHaveAttribute('aria-expanded', 'true')
    expect(screen.getByTestId(id('grid')).querySelector('thead th')).not.toBeNull()
    fireEvent.click(toggle)
    expect(screen.queryByTestId(id('grid'))).not.toBeInTheDocument()
  })

  it('boundary pin - holds today: the aging column and the window text are unchanged', () => {
    renderList([item()])
    expect(screen.getByTestId(id('turn'))).toHaveTextContent(
      'Leder til og med 18. marts 2026, kun HR fra 19. marts 2026',
    )
    expect(screen.getByTestId(`${T}-row-${K}`)).toHaveTextContent('4 dage')
  })
})
