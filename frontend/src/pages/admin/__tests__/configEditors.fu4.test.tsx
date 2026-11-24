// S148 T23 FU-4 (cycle-4 ruling C4-R1). Kind F, one section per fact.
// Red conditions on be76a295: a refusal on the create form and a refusal on a row share one slot, so
// the second wipes the first; a list re-read takes the table (and the open row step) off the screen;
// the create form's key fields stay editable while its create is pending.
//
// Today is Thursday 12 November 2026 throughout.
import { describe, it, expect, vi, beforeEach } from 'vitest'
import { screen, waitFor, fireEvent, act } from '@testing-library/react'
import { PositionOverrideManagement } from '../PositionOverrideManagement'
import { renderWithCalendar } from '../../../test/renderWithCalendar'
import type { PositionOverrideConfig } from '../../../hooks/usePositionOverrides'

const mockFetch = vi.fn()
vi.stubGlobal('fetch', mockFetch)

vi.mock('../../../contexts/AuthContext', () => ({
  useAuth: () => ({ role: 'GlobalAdmin' }),
}))

const TODAY = '2026-11-12'

const jsonResponse = (status: number, body: unknown) => ({
  ok: true,
  status,
  headers: new Headers({ ETag: '"3"' }),
  json: async () => body,
})

const failure = (status: number, text: string) => ({
  ok: false,
  status,
  headers: new Headers(),
  text: async () => text,
})

const refusal = (code: string, nearestValid: string[]) =>
  failure(
    400,
    JSON.stringify({
      error: 'Applies-from date validation failed',
      fields: [{ field: 'AppliesFrom', code, nearestValid }],
    }),
  )
const beforePredecessor = (date: string) => refusal('BEFORE_PREDECESSOR', [date])
const notMonday = () => refusal('NOT_MONDAY_ALIGNED', ['2026-11-09', '2026-11-16'])

function held() {
  let release: (v: unknown) => void = () => {}
  const promise = new Promise((resolve) => { release = resolve })
  return { promise, release: (v: unknown) => release(v) }
}

const row = (overrideId: string, positionCode = 'DEPARTMENT_HEAD'): PositionOverrideConfig => ({
  overrideId,
  agreementCode: 'AC',
  okVersion: 'OK24',
  positionCode,
  status: 'ACTIVE',
  version: 1,
  maxFlexBalance: null,
  normPeriodWeeks: 4,
  weeklyNormHours: 40,
  createdBy: 'admin',
  createdAt: '2026-03-08T00:00:00Z',
  updatedAt: '2026-03-08T00:00:00Z',
  appliesFrom: '2026-11-02',
  description: null,
})

function mountOverrides(
  rows: PositionOverrideConfig[],
  onWrite: () => unknown,
  onList: (read: number) => unknown = () => jsonResponse(200, rows),
) {
  const reads = { list: 0 }
  const writes = { count: 0 }
  mockFetch.mockImplementation(async (_url: string, init?: RequestInit) => {
    if (init?.method === 'POST' || init?.method === 'PUT') {
      writes.count += 1
      return onWrite()
    }
    reads.list += 1
    return onList(reads.list)
  })
  renderWithCalendar(TODAY, <PositionOverrideManagement />)
  return { reads, writes }
}

const button = (name: string) => screen.getByRole('button', { name }) as HTMLButtonElement
const dateOf = (label: string) => screen.getByLabelText(label) as HTMLInputElement
const WEDNESDAY = '2026-11-11'

function fillCreateKey(position: string) {
  fireEvent.change(screen.getByLabelText(/^Aftale/), { target: { value: 'AC' } })
  fireEvent.change(screen.getByLabelText(/^OK Version/), { target: { value: 'OK24' } })
  fireEvent.change(screen.getByLabelText(/^Stillingskode/), { target: { value: position } })
}

async function refuseCreate(writes: { count: number }) {
  fireEvent.click(button('Opret ny'))
  fillCreateKey('NEW')
  fireEvent.change(dateOf('Gælder fra'), { target: { value: WEDNESDAY } })
  fireEvent.click(button('Opret'))
  await waitFor(() => expect(writes.count).toBe(1))
  await waitFor(() => expect(screen.getAllByRole('button', { name: 'Brug 9. november 2026' })).toHaveLength(1))
}

async function refuseRow(writes: { count: number }, expected: number) {
  fireEvent.click(await screen.findByRole('button', { name: 'Rediger' }))
  fireEvent.change(dateOf('Ændringen gælder fra'), { target: { value: WEDNESDAY } })
  fireEvent.click(button('Gem'))
  await waitFor(() => expect(writes.count).toBe(expected))
}

beforeEach(() => {
  mockFetch.mockReset()
})

describe('(a) the create form and the row step each keep their own refusal', () => {
  it('create refused, then a row refused: both alerts and both "Brug" offers are shown', async () => {
    const { writes } = mountOverrides([row('o1')], notMonday)
    await screen.findByText('DEPARTMENT_HEAD')
    await refuseCreate(writes)
    await refuseRow(writes, 2)
    await waitFor(() => expect(screen.getAllByRole('button', { name: 'Brug 9. november 2026' })).toHaveLength(2))
    const alerts = screen.getAllByRole('alert').filter((a) => (a.textContent ?? '').includes('mandag'))
    expect(alerts).toHaveLength(2)
    expect(alerts.some((a) => a.closest('form') !== null)).toBe(true)
  })

  it('a row refused, then the create form refused: the row\'s refusal survives', async () => {
    const { writes } = mountOverrides([row('o1')], notMonday)
    await screen.findByText('DEPARTMENT_HEAD')
    await refuseRow(writes, 1)
    await waitFor(() => expect(screen.getAllByRole('button', { name: 'Brug 9. november 2026' })).toHaveLength(1))
    fireEvent.click(button('Opret ny'))
    fillCreateKey('NEW')
    fireEvent.change(dateOf('Gælder fra'), { target: { value: WEDNESDAY } })
    fireEvent.click(button('Opret'))
    await waitFor(() => expect(writes.count).toBe(2))
    await waitFor(() => expect(screen.getAllByRole('button', { name: 'Brug 9. november 2026' })).toHaveLength(2))
    expect(screen.getAllByRole('alert').filter((a) => (a.textContent ?? '').includes('mandag'))).toHaveLength(2)
  })
})

describe('(b) a list re-read while rows are shown does not take the table away', () => {
  it('BEFORE_PREDECESSOR: the row\'s date input stays while the re-read is pending, "Brug" focuses it, the step stays open', async () => {
    const reread = held()
    const rows = [row('o1')]
    const { reads, writes } = mountOverrides(rows, () => beforePredecessor('2026-11-10'), (n) =>
      n === 1 ? jsonResponse(200, rows) : reread.promise,
    )
    await screen.findByText('DEPARTMENT_HEAD')
    fireEvent.click(button('Rediger'))
    fireEvent.change(dateOf('Ændringen gælder fra'), { target: { value: '2026-11-05' } })
    fireEvent.click(button('Gem'))
    await waitFor(() => expect(writes.count).toBe(1))
    await waitFor(() => expect(reads.list).toBe(2))

    const input = dateOf('Ændringen gælder fra')
    expect(document.contains(input)).toBe(true)
    fireEvent.click(await screen.findByRole('button', { name: 'Brug 10. november 2026' }))
    expect(document.activeElement).toBe(dateOf('Ændringen gælder fra'))

    reread.release(jsonResponse(200, rows))
    await act(async () => {})
    expect(dateOf('Ændringen gælder fra').value).toBe('2026-11-10')
    expect(button('Gem')).toBeDefined()
  })

  it('a failed re-read keeps the rows already shown and shows the error', async () => {
    const rows = [row('o1')]
    const { reads, writes } = mountOverrides(rows, () => beforePredecessor('2026-11-10'), (n) =>
      n === 1 ? jsonResponse(200, rows) : failure(500, 'Serverfejl'),
    )
    await screen.findByText('DEPARTMENT_HEAD')
    fireEvent.click(button('Rediger'))
    fireEvent.change(dateOf('Ændringen gælder fra'), { target: { value: '2026-11-05' } })
    fireEvent.click(button('Gem'))
    await waitFor(() => expect(writes.count).toBe(1))
    await waitFor(() => expect(reads.list).toBe(2))
    await screen.findByText(/Serverfejl/)
    expect(screen.getByText('DEPARTMENT_HEAD')).toBeDefined()
    expect(dateOf('Ændringen gælder fra')).toBeDefined()
  })
})

describe('(c) the create form\'s key fields are locked while its create is pending', () => {
  it('disabled while pending, enabled again after the refusal; the date stays editable', async () => {
    const write = held()
    mountOverrides([row('o1')], () => write.promise)
    await screen.findByText('DEPARTMENT_HEAD')
    fireEvent.click(button('Opret ny'))
    fillCreateKey('NEW')
    fireEvent.click(button('Opret'))
    await waitFor(() => expect((screen.getByLabelText(/^Aftale/) as HTMLInputElement).disabled).toBe(true))
    expect((screen.getByLabelText(/^OK Version/) as HTMLInputElement).disabled).toBe(true)
    expect((screen.getByLabelText(/^Stillingskode/) as HTMLInputElement).disabled).toBe(true)
    expect(dateOf('Gælder fra').disabled).toBe(false)

    write.release(failure(409, 'Findes allerede'))
    await screen.findByText(/Findes allerede/)
    expect((screen.getByLabelText(/^Aftale/) as HTMLInputElement).disabled).toBe(false)
    expect((screen.getByLabelText(/^OK Version/) as HTMLInputElement).disabled).toBe(false)
    expect((screen.getByLabelText(/^Stillingskode/) as HTMLInputElement).disabled).toBe(false)
  })
})
