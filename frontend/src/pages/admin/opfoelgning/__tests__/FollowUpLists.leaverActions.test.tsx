// S147 / TASK-14714 (HRP-011; AC-230-9 and AC-230-10, the screen half) - the leaver list's
// row actions and the hand-over text.
//
// Kinds and Red conditions (RED shown against f65bf849's FollowUpLists.tsx, which has no row
// actions and no window column):
//  1 F  every row offers "Åbn måned" and "Send måned"; "Godkend"/"Afvis" only with a period.
//       Red: the open/send/approve/reject test ids do not exist on f65bf849.
//  2 F  the row says whose turn it is from lastManagerDay / firstHrOnlyDay, in Danish dates.
//       Red: no `-turn-` cell on f65bf849.
//  3 F  a 403 four-eyes refusal on approve shows its reason in Danish (AC-230-10).
//       Red: no approve action on f65bf849, so no message can appear.
//  4 F  the 422 month_outside_employment_period message is shown (AC-230-9 edge).
//       Red: no send action on f65bf849.
//  5 F  a send on a row without a period reveals approve/reject and calls the endpoints
//       with the returned periodId.
//       Red: no send action on f65bf849.
//  6 B  boundary pin - holds today: the aging columns are unchanged ("Frist overskredet med").
import { describe, it, expect, vi, beforeEach } from 'vitest'
import { render, screen, fireEvent, waitFor } from '@testing-library/react'
import { MemoryRouter } from 'react-router-dom'

const api = vi.hoisted(() => ({ get: vi.fn(), post: vi.fn() }))
vi.mock('../../../../lib/api', () => ({
  apiClient: { get: api.get, post: api.post, put: vi.fn(), delete: vi.fn() },
}))

import { LeaverFinalMonthList } from '../FollowUpLists'
import type { HrFollowUpMonthItem, HrLeaverFinalMonthResponse } from '../../../../hooks/useHrFollowUp'

const WITH_PERIOD = '11111111-1111-1111-1111-111111111111'

function item(over: Partial<HrFollowUpMonthItem> = {}): HrFollowUpMonthItem {
  return {
    employeeId: 'mette', displayName: 'Mette Leaver', orgId: 'org1', year: 2026, month: 3,
    periodStatus: 'EMPLOYEE_APPROVED', periodId: WITH_PERIOD,
    ageAnchor: '2026-04-10', daysPastAnchor: 4, deadlineSource: 'stored',
    lastManagerDay: '2026-03-18', firstHrOnlyDay: '2026-03-19',
    ...over,
  }
}

function renderList(items: HrFollowUpMonthItem[]) {
  const data = { today: '2026-04-14', lookbackFloor: '2025-04-14', count: items.length, items } as unknown as HrLeaverFinalMonthResponse
  return render(
    <MemoryRouter>
      <LeaverFinalMonthList data={data} loading={false} error={null} />
    </MemoryRouter>,
  )
}

const T = 'list-leaver-final-month'
const K = 'mette-2026-3'

beforeEach(() => {
  api.get.mockReset()
  api.post.mockReset()
})

describe('LeaverFinalMonthList row actions', () => {
  it('offers open and send on every row, approve and reject only where a period exists', () => {
    renderList([item(), item({ employeeId: 'ole', displayName: 'Ole Ingen', periodStatus: 'NONE', periodId: null })])
    expect(screen.getByTestId(`${T}-open-${K}`)).toBeInTheDocument()
    expect(screen.getByTestId(`${T}-send-${K}`)).toBeInTheDocument()
    expect(screen.getByTestId(`${T}-approve-${K}`)).toBeInTheDocument()
    expect(screen.getByTestId(`${T}-reject-${K}`)).toBeInTheDocument()
    expect(screen.getByTestId(`${T}-open-ole-2026-3`)).toBeInTheDocument()
    expect(screen.getByTestId(`${T}-send-ole-2026-3`)).toBeInTheDocument()
    expect(screen.queryByTestId(`${T}-approve-ole-2026-3`)).not.toBeInTheDocument()
    expect(screen.queryByTestId(`${T}-reject-ole-2026-3`)).not.toBeInTheDocument()
  })

  it('says whose turn it is, in Danish dates', () => {
    renderList([item()])
    expect(screen.getByTestId(`${T}-turn-${K}`)).toHaveTextContent(
      'Leder til og med 18. marts 2026, kun HR fra 19. marts 2026',
    )
  })

  it('shows the four-eyes reason in Danish when approve is refused with 403', async () => {
    api.post.mockResolvedValue({
      ok: false, status: 403, error: 'x',
      body: { error: 'Access denied', reason: 'the sender of a month cannot approve or reject it (four-eyes)' },
    })
    renderList([item()])
    fireEvent.click(screen.getByTestId(`${T}-approve-${K}`))
    const out = await screen.findByTestId(`${T}-outcome-${K}`)
    expect(out).toHaveTextContent('fire-øjne-princippet')
    expect(out).toHaveTextContent('en anden HR-medarbejder')
    expect(api.post).toHaveBeenCalledWith('/api/approval/{periodId}/approve', { params: { path: { periodId: WITH_PERIOD } } })
  })

  it('shows the server message for a month outside the employment period (422)', async () => {
    api.post.mockResolvedValue({
      ok: false, status: 422, error: 'x',
      body: { error: 'month_outside_employment_period', kind: 'employment-window', message: 'Måneden ligger uden for ansættelsesperioden.' },
    })
    renderList([item({ periodId: null, periodStatus: 'NONE' })])
    fireEvent.click(screen.getByTestId(`${T}-send-${K}`))
    expect(await screen.findByTestId(`${T}-outcome-${K}`)).toHaveTextContent('Måneden ligger uden for ansættelsesperioden.')
  })

  it('after a send on a row without a period, approve and reject appear and use the returned id', async () => {
    api.post.mockResolvedValueOnce({ ok: true, data: { periodId: WITH_PERIOD, status: 'EMPLOYEE_APPROVED' } })
    api.post.mockResolvedValueOnce({ ok: true, data: { periodId: WITH_PERIOD, status: 'APPROVED' } })
    renderList([item({ periodId: null, periodStatus: 'NONE' })])
    fireEvent.click(screen.getByTestId(`${T}-send-${K}`))
    const approve = await screen.findByTestId(`${T}-approve-${K}`)
    expect(screen.getByTestId(`${T}-reject-${K}`)).toBeInTheDocument()
    fireEvent.click(approve)
    await waitFor(() => expect(screen.getByTestId(`${T}-status-${K}`)).toHaveTextContent('Godkendt'))
    expect(api.post).toHaveBeenLastCalledWith('/api/approval/{periodId}/approve', { params: { path: { periodId: WITH_PERIOD } } })
  })

  it('boundary pin - holds today: the aging column is unchanged', () => {
    renderList([item()])
    expect(screen.getByTestId(`${T}-row-${K}`)).toHaveTextContent('4 dage')
  })
})
