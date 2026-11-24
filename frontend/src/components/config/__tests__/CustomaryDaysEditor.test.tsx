// AC-C10e (TASK-14835). Kind F for every fact.
// Each fact renders the configuration page, opens the "Kutymefridage" tab and drives the editor
// through it. Red conditions (shared): before the change the page has no "Kutymefridage" tab, so
// each fact's first step (finding and opening that tab) fails. RED is also taken with the tab entry
// removed from the page's tabs array; the per-fact conditions below name the assertion that then fails.
import { describe, it, expect, vi, beforeEach } from 'vitest'
import { render, screen, waitFor, within, fireEvent } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { ConfigManagement } from '../../../pages/config/ConfigManagement'

// jsdom lacks what Radix Select and Tabs touch.
Element.prototype.hasPointerCapture = () => false
Element.prototype.setPointerCapture = () => {}
Element.prototype.releasePointerCapture = () => {}
Element.prototype.scrollIntoView = () => {}
vi.stubGlobal('ResizeObserver', class { observe() {} unobserve() {} disconnect() {} })

const mockStorage: Record<string, string> = { statstid_token: 'test-token' }
vi.stubGlobal('localStorage', {
  getItem: (k: string) => mockStorage[k] ?? null,
  setItem: (k: string, v: string) => { mockStorage[k] = v },
  removeItem: (k: string) => { delete mockStorage[k] },
})

type Row = Record<string, unknown>
const june5: Row = {
  customId: '11111111-1111-1111-1111-111111111111', orgId: 'STY01', agreementCode: null,
  recurringMonth: 6, recurringDay: 5, singleDate: null, kind: 'PAID_DAY_OFF', part: 'FROM_TIME',
  partFromTime: '12:00:00', partHours: null, paidMode: 'GIVEN_TO_ALL', validFrom: '2026-01-05', validTo: null,
}
const may15: Row = {
  customId: '22222222-2222-2222-2222-222222222222', orgId: 'STY01', agreementCode: null,
  recurringMonth: null, recurringDay: null, singleDate: '2026-05-15', kind: 'CLOSURE_DAY', part: 'WHOLE_DAY',
  partFromTime: null, partHours: null, paidMode: null, validFrom: '2026-01-05', validTo: '2026-11-02',
}

const dec24: Row = {
  customId: '44444444-4444-4444-4444-444444444444', orgId: 'STY02', agreementCode: null,
  recurringMonth: 12, recurringDay: 24, singleDate: null, kind: 'CLOSURE_DAY', part: 'WHOLE_DAY',
  partFromTime: null, partHours: null, paidMode: null, validFrom: '2026-01-05', validTo: null,
}

interface Call { method: string; url: string; body: unknown }
interface Reply { status: number; body: unknown }
let calls: Call[]
let rows: Row[]
let rows2: Row[]
let postReply: Reply | null
let postQueue: Reply[]
// A POST waits for this promise (when set) before it answers, so a test can hold a write in flight.
let postGate: Promise<void> | null

function reply(status: number, body: unknown) {
  return {
    ok: status < 400, status, headers: new Headers(),
    json: async () => body, text: async () => JSON.stringify(body),
  }
}

beforeEach(() => {
  calls = []
  rows = []
  rows2 = []
  postReply = null
  postQueue = []
  postGate = null
  vi.stubGlobal('fetch', vi.fn(async (url: string, init?: RequestInit) => {
    const method = init?.method ?? 'GET'
    const body = typeof init?.body === 'string' ? JSON.parse(init.body) : undefined
    calls.push({ method, url, body })
    if (url === '/api/admin/organizations') {
      return reply(200, [{ orgId: 'STY01', orgName: 'Styrelsen' }, { orgId: 'STY02', orgName: 'Ministeriet' }])
    }
    if (url === '/api/config/STY01/customary-days' && method === 'GET') return reply(200, rows)
    if (url === '/api/config/STY02/customary-days' && method === 'GET') return reply(200, rows2)
    if (url.includes('/customary-days') && method === 'POST') {
      const next = postQueue.shift() ?? postReply
      if (postGate) await postGate
      return reply(next?.status ?? 201, next?.body ?? june5)
    }
    return reply(404, {})
  }))
})

const user = () => userEvent.setup()

async function choose(u: ReturnType<typeof user>, name: string, option: string) {
  await u.click(screen.getByRole('combobox', { name }))
  await u.click(await screen.findByRole('option', { name: option }))
}

async function openTab(u: ReturnType<typeof user>, withOrg = true) {
  render(<ConfigManagement />)
  await u.click(await screen.findByRole('tab', { name: 'Kutymefridage' }))
  if (withOrg) await choose(u, 'Organisation', 'Styrelsen (STY01)')
}

const posts = () => calls.filter(c => c.method === 'POST')
const rowOf = (text: string) => screen.getByText(text).closest('tr') as HTMLElement

async function fillWorkedExample(u: ReturnType<typeof user>) {
  await u.click(screen.getByLabelText('Hvert år på samme dato'))
  await choose(u, 'Måned', 'juni')
  await choose(u, 'Dag', '5')
  await u.click(screen.getByLabelText('Betalt fridag'))
  await u.click(screen.getByLabelText('Fra et klokkeslæt'))
  await u.type(screen.getByLabelText('Fri fra kl.'), '12:00')
  await u.click(screen.getByLabelText('Gives til alle'))
  await u.type(screen.getByLabelText('Gælder fra'), '2026-01-05')
}

describe('Kutymefridage tab', { timeout: 30000 }, () => {
  // Fact 1. Red: the tab does not exist, so opening it throws; with the entry removed the same step fails.
  it('lists the institution\'s customs in Danish, shows the pay note, and asks for an organisation first', async () => {
    rows = [june5, may15]
    const u = user()
    await openTab(u, false)
    expect(screen.getByText('Vælg en organisation for at se kutymefridage.')).toBeInTheDocument()

    await choose(u, 'Organisation', 'Styrelsen (STY01)')
    expect(await screen.findByRole('heading', { name: 'Kutymefridage og lukkedage' })).toBeInTheDocument()
    expect(calls.some(c => c.method === 'GET' && c.url === '/api/config/STY01/customary-days')).toBe(true)
    expect(screen.getByText('Gemmes nu, men bruges endnu ikke i nogen beregning og påvirker endnu ikke lønberegningen.')).toBeInTheDocument()

    const yearly = (await screen.findByText('Hvert år den 5. juni')).closest('tr') as HTMLElement
    for (const t of ['Betalt fridag', 'Fra kl. 12:00', 'Gives til alle', 'Alle overenskomster', '5. januar 2026'])
      expect(within(yearly).getByText(t)).toBeInTheDocument()
    const single = rowOf('15. maj 2026')
    for (const t of ['Lukkedag', 'Hele dagen', 'Alle overenskomster', '5. januar 2026', '2. november 2026'])
      expect(within(single).getByText(t)).toBeInTheDocument()
    expect(screen.getByRole('columnheader', { name: 'Ophører fra' })).toBeInTheDocument()
  })

  // Fact 2. Red: no tab, so no form to fill; with the entry removed, the first step fails.
  it('creates with the exact body of the worked example, re-reads the list and announces it', async () => {
    const u = user()
    await openTab(u)
    await screen.findByText('Der er ikke registreret kutymefridage eller lukkedage for institutionen.')
    await fillWorkedExample(u)
    rows = [june5]
    await u.click(screen.getByRole('button', { name: 'Tilføj' }))
    await waitFor(() => expect(posts()).toHaveLength(1))
    expect(posts()[0].url).toBe('/api/config/STY01/customary-days')
    expect(posts()[0].body).toEqual({
      agreementCode: null, recurringMonth: 6, recurringDay: 5, kind: 'PAID_DAY_OFF', part: 'FROM_TIME',
      partFromTime: '12:00:00', paidMode: 'GIVEN_TO_ALL', validFrom: '2026-01-05',
    })
    expect(await screen.findByText('Hvert år den 5. juni')).toBeInTheDocument()
    expect(screen.getByRole('status')).toHaveTextContent('Dagen er gemt.')
  })

  // Fact 3. Red: no tab; with the entry removed, the first step fails.
  it('never offers a refused combination', async () => {
    const u = user()
    await openTab(u)
    await screen.findByText('Der er ikke registreret kutymefridage eller lukkedage for institutionen.')
    const submit = screen.getByRole('button', { name: 'Tilføj' })
    expect(submit).toBeDisabled()
    expect(screen.queryByLabelText('Fri fra kl.')).not.toBeInTheDocument()
    expect(screen.queryByLabelText('Antal timer (ved fuld tid)')).not.toBeInTheDocument()
    expect(screen.queryByRole('group', { name: 'Tildeling' })).not.toBeInTheDocument()

    await u.click(screen.getByLabelText('Fra et klokkeslæt'))
    expect(screen.getByLabelText('Fri fra kl.')).toBeInTheDocument()
    expect(screen.queryByLabelText('Antal timer (ved fuld tid)')).not.toBeInTheDocument()
    await u.click(screen.getByLabelText('Et antal timer'))
    expect(screen.getByLabelText('Antal timer (ved fuld tid)')).toBeInTheDocument()
    expect(screen.queryByLabelText('Fri fra kl.')).not.toBeInTheDocument()

    await u.click(screen.getByLabelText('Lukkedag'))
    expect(screen.queryByRole('group', { name: 'Tildeling' })).not.toBeInTheDocument()
    await u.click(screen.getByLabelText('Betalt fridag'))
    expect(screen.getByRole('group', { name: 'Tildeling' })).toBeInTheDocument()

    await u.click(screen.getByLabelText('Hvert år på samme dato'))
    await choose(u, 'Måned', 'februar')
    await u.click(screen.getByRole('combobox', { name: 'Dag' }))
    const days = (await screen.findAllByRole('option')).map(o => o.textContent)
    expect(days).toHaveLength(29)
    expect(days).toContain('29')
    expect(days).not.toContain('30')
    await u.keyboard('{Escape}')

    await u.click(screen.getByLabelText('Hele dagen'))
    await u.click(screen.getByLabelText('Gives til alle'))
    await choose(u, 'Dag', '29')
    expect(submit).toBeDisabled()
    await u.type(screen.getByLabelText('Gælder fra'), '2026-01-05')
    expect(submit).toBeEnabled()
  })

  // Fact 4. Red: no tab; with the entry removed, the first step fails.
  it('withdraws from a date after its start and reinstates from its end', async () => {
    rows = [june5, may15]
    const u = user()
    await openTab(u)
    const open = (await screen.findByText('Hvert år den 5. juni')).closest('tr') as HTMLElement
    expect(within(open).queryByRole('button', { name: 'Genindfør' })).not.toBeInTheDocument()
    await u.click(within(open).getByRole('button', { name: 'Afslut' }))
    const wDate = within(open).getByLabelText('Ophører fra')
    expect(wDate).toHaveAttribute('min', '2026-01-06')
    await u.type(wDate, '2026-11-02')
    await u.click(within(open).getByRole('button', { name: 'Afslut' }))
    await waitFor(() => expect(posts()).toHaveLength(1))
    expect(posts()[0].url).toBe('/api/config/STY01/customary-days/11111111-1111-1111-1111-111111111111/withdraw')
    expect(posts()[0].body).toEqual({ validTo: '2026-11-02' })
    await waitFor(() => expect(screen.getByRole('status')).toHaveTextContent('Dagen ophører fra 2. november 2026.'))

    const closed = rowOf('15. maj 2026')
    expect(within(closed).queryByRole('button', { name: 'Afslut' })).not.toBeInTheDocument()
    await u.click(within(closed).getByRole('button', { name: 'Genindfør' }))
    const rDate = within(closed).getByLabelText('Genindføres fra')
    expect(rDate).toHaveAttribute('min', '2026-11-02')
    await u.type(rDate, '2026-11-10')
    await u.click(within(closed).getByRole('button', { name: 'Genindfør' }))
    await waitFor(() => expect(posts()).toHaveLength(2))
    expect(posts()[1].url).toBe('/api/config/STY01/customary-days/22222222-2222-2222-2222-222222222222/reinstate')
    expect(posts()[1].body).toEqual({ validFrom: '2026-11-10' })
    await waitFor(() => expect(screen.getByRole('status')).toHaveTextContent('Dagen er genindført fra 10. november 2026.'))
  })

  // Fact 5. Red: no tab; with the entry removed, the first step fails.
  it('shows each refusal in Danish on its field or in the form area, never the server\'s English', async () => {
    rows = [june5, may15]
    const u = user()
    await openTab(u)
    await screen.findByText('Hvert år den 5. juni')
    await u.click(screen.getByLabelText('Et antal timer'))
    await u.type(screen.getByLabelText('Antal timer (ved fuld tid)'), '5')
    await u.click(screen.getByLabelText('Betalt fridag'))
    await u.click(screen.getByLabelText('En enkelt dato'))
    await u.type(screen.getByLabelText('Dato'), '2026-05-16')
    await u.click(screen.getByLabelText('Gives til alle'))
    await u.type(screen.getByLabelText('Gælder fra'), '2026-01-05')

    postReply = {
      status: 400,
      body: {
        error: 'Customary day validation failed',
        fields: [
          { field: 'PartHours', code: 'EXCEEDS_STORAGE_LIMIT', nearestValid: null },
          { field: 'PaidMode', code: 'NOT_ALLOWED', nearestValid: null },
        ],
      },
    }
    await u.click(screen.getByRole('button', { name: 'Tilføj' }))
    const hours = screen.getByLabelText('Antal timer (ved fuld tid)')
    await waitFor(() => expect(hours).toHaveAttribute('aria-invalid', 'true'))
    const hoursErr = screen.getByText('Tallet er for stort til at blive gemt.')
    expect(hours.getAttribute('aria-describedby')).toBe(hoursErr.id)
    const modeErr = screen.getByText('Må ikke udfyldes ved dette valg.')
    const modeGroup = screen.getByRole('group', { name: 'Tildeling' })
    expect(modeGroup.getAttribute('aria-describedby')).toBe(modeErr.id)
    expect(modeGroup).not.toHaveAttribute('aria-invalid')
    for (const r of within(modeGroup).getAllByRole('radio')) expect(r).toHaveAttribute('aria-invalid', 'true')
    expect(screen.queryByText(/validation failed/)).not.toBeInTheDocument()

    const refuse = async (status: number, body: unknown, text: string) => {
      postReply = { status, body }
      await u.click(screen.getByRole('button', { name: 'Tilføj' }))
      expect(await screen.findByText(text)).toBeInTheDocument()
      expect(screen.queryByText(/overlaps|denied|already withdrawn/)).not.toBeInTheDocument()
    }
    await refuse(409, { error: 'Customary day overlaps an existing one', conflictingCustomId: june5.customId },
      'Institutionen har allerede denne dag registreret for samme overenskomst i den valgte periode.')
    await refuse(403, { error: 'Access denied', reason: 'x' },
      'Du har ikke adgang til at ændre denne institutions kutymefridage.')

    postReply = { status: 409, body: { error: 'Customary day is already withdrawn' } }
    const open = rowOf('Hvert år den 5. juni')
    await u.click(within(open).getByRole('button', { name: 'Afslut' }))
    await u.type(within(open).getByLabelText('Ophører fra'), '2026-11-02')
    await u.click(within(open).getByRole('button', { name: 'Afslut' }))
    expect(await screen.findByText('Dagen er allerede afsluttet. Genindlæs siden.')).toBeInTheDocument()
    expect(screen.queryByText(/already withdrawn/)).not.toBeInTheDocument()
  })

  // Fact 6 (ruling 14). Red: no tab; with the entry removed, the first step fails.
  it('does not offer Genindfør while another row of the same day and scope is open; else its min is the latest end', async () => {
    const a = { ...june5, validTo: '2026-11-02' }
    const b = { ...june5, customId: '33333333-3333-3333-3333-333333333333', validFrom: '2026-11-10', validTo: null }
    rows = [a, b]
    const u = user()
    await openTab(u)
    await screen.findAllByText('Hvert år den 5. juni')
    const [rowA] = screen.getAllByText('Hvert år den 5. juni').map(el => el.closest('tr') as HTMLElement)
    expect(within(rowA).queryByRole('button', { name: 'Genindfør' })).not.toBeInTheDocument()
    expect(within(rowA).queryByRole('button', { name: 'Afslut' })).not.toBeInTheDocument()
  })

  it('uses the latest end among rows of the same day and scope as the Genindfør date\'s min', async () => {
    const a = { ...june5, validTo: '2026-11-02' }
    const b = { ...june5, customId: '33333333-3333-3333-3333-333333333333', validFrom: '2026-11-10', validTo: '2026-11-16' }
    rows = [a, b]
    const u = user()
    await openTab(u)
    await screen.findAllByText('Hvert år den 5. juni')
    const [rowA] = screen.getAllByText('Hvert år den 5. juni').map(el => el.closest('tr') as HTMLElement)
    await u.click(within(rowA).getByRole('button', { name: 'Genindfør' }))
    expect(within(rowA).getByLabelText('Genindføres fra')).toHaveAttribute('min', '2026-11-16')
  })
})

// FU-3 (ruling R3 of the 14834 stack). Red conditions are named per fact; all are taken against the
// editor and page as they were before FU-3.
describe('Kutymefridage editor: isolation, unsent dates, duplicates, accessibility', { timeout: 30000 }, () => {
  const release = () => {
    let open!: () => void
    postGate = new Promise<void>(r => { open = r })
    return open
  }
  const idle = () => new Promise<void>(r => setTimeout(r, 50))
  const setDate = (el: HTMLElement, value: string) => fireEvent.change(el, { target: { value } })

  // Red: the editor is not remounted per organisation and its completion runs against whichever
  // organisation is shown, so the late save shows "Dagen er gemt." and re-reads STY01 into STY02's list.
  it('ignores a save that finishes after the organisation was switched', async () => {
    rows = [may15]
    rows2 = [dec24]
    const u = user()
    await openTab(u)
    await screen.findByText('15. maj 2026')
    await fillWorkedExample(u)
    const open = release()
    await u.click(screen.getByRole('button', { name: 'Tilføj' }))
    await waitFor(() => expect(posts()).toHaveLength(1))

    await choose(u, 'Organisation', 'Ministeriet (STY02)')
    expect(await screen.findByText('Hvert år den 24. december')).toBeInTheDocument()
    const getsBefore = calls.filter(c => c.method === 'GET' && c.url === '/api/config/STY01/customary-days').length
    rows = [may15, june5]
    open()
    await idle()

    expect(screen.getByRole('status')).toHaveTextContent('')
    expect(screen.getByText('Hvert år den 24. december')).toBeInTheDocument()
    expect(screen.queryByText('Hvert år den 5. juni')).not.toBeInTheDocument()
    expect(calls.filter(c => c.method === 'GET' && c.url === '/api/config/STY01/customary-days')).toHaveLength(getsBefore)
  })

  // Red: the confirm button only checks that a date is typed, so a below-minimum date is posted.
  it('sends no POST for a date before the minimum, past the maximum, or empty', async () => {
    rows = [june5]
    const u = user()
    await openTab(u)
    const row = (await screen.findByText('Hvert år den 5. juni')).closest('tr') as HTMLElement
    await u.click(within(row).getByRole('button', { name: 'Afslut' }))
    const date = within(row).getByLabelText('Ophører fra')
    expect(date).toHaveAttribute('max', '9999-12-30')
    const confirm = within(row).getByRole('button', { name: 'Afslut' })
    expect(confirm).toBeDisabled()
    setDate(date, '2026-01-05')
    expect(confirm).toBeDisabled()
    await u.click(confirm)
    setDate(date, '9999-12-31')
    expect(confirm).toBeDisabled()
    await u.click(confirm)
    expect(posts()).toHaveLength(0)
    setDate(date, '2026-01-06')
    expect(confirm).toBeEnabled()
  })

  // Red: a second click sends a second POST (the server's 409 then shows as a false error).
  it('sends one POST on a double click and shows no error', async () => {
    rows = [june5]
    postQueue = [{ status: 201, body: june5 }, { status: 409, body: { error: 'Customary day is already withdrawn' } }]
    const u = user()
    await openTab(u)
    const row = (await screen.findByText('Hvert år den 5. juni')).closest('tr') as HTMLElement
    await u.click(within(row).getByRole('button', { name: 'Afslut' }))
    setDate(within(row).getByLabelText('Ophører fra'), '2026-11-02')
    const open = release()
    await u.dblClick(within(row).getByRole('button', { name: 'Afslut' }))
    open()
    await waitFor(() => expect(screen.getByRole('status')).toHaveTextContent('Dagen ophører fra 2. november 2026.'))
    expect(posts()).toHaveLength(1)
    expect(screen.queryByText('Dagen er allerede afsluttet. Genindlæs siden.')).not.toBeInTheDocument()
  })

  // Red: a result always sets the status and closes the inline form, even for a row no longer edited.
  it('ignores a result for a row action that is no longer the one being edited', async () => {
    rows = [june5, may15]
    const u = user()
    await openTab(u)
    const open = rowOf('Hvert år den 5. juni')
    await u.click(within(open).getByRole('button', { name: 'Afslut' }))
    setDate(within(open).getByLabelText('Ophører fra'), '2026-11-02')
    const done = release()
    await u.click(within(open).getByRole('button', { name: 'Afslut' }))
    await waitFor(() => expect(posts()).toHaveLength(1))
    await u.click(within(open).getByRole('button', { name: 'Annuller' }))
    await u.click(within(rowOf('15. maj 2026')).getByRole('button', { name: 'Genindfør' }))
    done()
    await idle()
    expect(screen.getByRole('status')).toHaveTextContent('')
    expect(within(rowOf('15. maj 2026')).getByLabelText('Genindføres fra')).toBeInTheDocument()
  })

  // Red: a failed confirm leaves the earlier success sentence in the live region.
  it('clears the success status when a later confirm fails', async () => {
    rows = [june5, may15]
    const u = user()
    await openTab(u)
    const open = (await screen.findByText('Hvert år den 5. juni')).closest('tr') as HTMLElement
    await u.click(within(open).getByRole('button', { name: 'Afslut' }))
    setDate(within(open).getByLabelText('Ophører fra'), '2026-11-02')
    await u.click(within(open).getByRole('button', { name: 'Afslut' }))
    await waitFor(() => expect(screen.getByRole('status')).toHaveTextContent('Dagen ophører fra 2. november 2026.'))

    postReply = { status: 409, body: { error: 'Customary day is not withdrawn' } }
    const closed = rowOf('15. maj 2026')
    await u.click(within(closed).getByRole('button', { name: 'Genindfør' }))
    setDate(within(closed).getByLabelText('Genindføres fra'), '2026-11-10')
    await u.click(within(closed).getByRole('button', { name: 'Genindfør' }))
    expect(await screen.findByText('Dagen er ikke afsluttet. Genindlæs siden.')).toBeInTheDocument()
    expect(screen.getByRole('status')).toHaveTextContent('')
  })

  // Red: the form has no duplicate check, so "Tilføj" is enabled and no sentence is shown.
  it('disables Tilføj and says why when a listed row already holds the day for the same scope', async () => {
    rows = [june5]
    const u = user()
    await openTab(u)
    await screen.findByText('Hvert år den 5. juni')
    await fillWorkedExample(u)
    const submit = screen.getByRole('button', { name: 'Tilføj' })
    expect(submit).toBeDisabled()
    const why = screen.getByText('Institutionen har allerede denne dag registreret for samme overenskomst i den valgte periode.')
    expect(submit.getAttribute('aria-describedby')).toBe(why.id)

    await choose(u, 'Gælder for', 'HK')
    expect(submit).toBeEnabled()
    expect(submit).not.toHaveAttribute('aria-describedby')
    expect(screen.queryByText(why.textContent as string)).not.toBeInTheDocument()
  })

  // Red: no duplicate check, so it cannot tell a row that ended by the draft's start from one still running.
  it('does not block when the listed row ended on or before the draft\'s start', async () => {
    rows = [{ ...june5, validTo: '2026-01-05' }]
    const u = user()
    await openTab(u)
    await screen.findByText('Hvert år den 5. juni')
    await fillWorkedExample(u)
    expect(screen.getByRole('button', { name: 'Tilføj' })).toBeEnabled()
  })

  // Red: the Select trigger and radios carry no aria-invalid / aria-describedby.
  it('marks each refused control with aria-invalid and its message', async () => {
    const u = user()
    await openTab(u)
    await screen.findByText('Der er ikke registreret kutymefridage eller lukkedage for institutionen.')
    await fillWorkedExample(u)
    postReply = {
      status: 400,
      body: {
        error: 'x',
        fields: [
          { field: 'AgreementCode', code: 'UNKNOWN_VALUE', nearestValid: null },
          { field: 'RecurringMonth', code: 'DAY_INCOMPLETE', nearestValid: null },
          { field: 'RecurringDay', code: 'NOT_A_CALENDAR_DAY', nearestValid: null },
          { field: 'Kind', code: 'REQUIRED', nearestValid: null },
        ],
      },
    }
    await u.click(screen.getByRole('button', { name: 'Tilføj' }))
    for (const [name, text] of [
      ['Gælder for', 'Ugyldig værdi. Vælg en af de viste muligheder.'], ['Måned', 'Angiv både måned og dag.'], ['Dag', 'Datoen findes ikke.'],
    ] as const) {
      const box = await screen.findByRole('combobox', { name })
      expect(box).toHaveAttribute('aria-invalid', 'true')
      expect(box.getAttribute('aria-describedby')).toBe(screen.getByText(text).id)
    }
    const kind = screen.getByRole('group', { name: 'Type' })
    expect(kind).not.toHaveAttribute('aria-invalid')
    expect(kind.getAttribute('aria-describedby')).toBe(screen.getByText('Skal udfyldes.').id)
    for (const r of within(kind).getAllByRole('radio')) expect(r).toHaveAttribute('aria-invalid', 'true')
    expect(screen.getByLabelText('Hele dagen', { selector: 'input' })).not.toHaveAttribute('aria-invalid')
  })

  // Red: the single date input is not tied to the refusal, only its group is.
  it('ties a single-date refusal to the date input as well as the group', async () => {
    const u = user()
    await openTab(u)
    await screen.findByText('Der er ikke registreret kutymefridage eller lukkedage for institutionen.')
    await u.click(screen.getByLabelText('En enkelt dato'))
    setDate(screen.getByLabelText('Dato'), '2026-05-16')
    await u.click(screen.getByLabelText('Lukkedag'))
    await u.click(screen.getByLabelText('Hele dagen'))
    setDate(screen.getByLabelText('Gælder fra'), '2026-01-05')
    postReply = { status: 400, body: { fields: [{ field: 'SingleDate', code: 'NOT_A_CALENDAR_DAY', nearestValid: null }] } }
    await u.click(screen.getByRole('button', { name: 'Tilføj' }))
    const text = await screen.findByText('Datoen findes ikke.')
    const date = screen.getByLabelText('Dato')
    expect(date).toHaveAttribute('aria-invalid', 'true')
    expect(date.getAttribute('aria-describedby')).toBe(text.id)
    expect(screen.getByRole('group', { name: 'Hvornår' }).getAttribute('aria-describedby')).toBe(text.id)
  })

  // Red (reinstate arms): the route-specific 409 split is not pinned; a wrong mapping would pass the
  // create-only facts above.
  it('tells a reinstate that overlaps from one on a row that is not stopped', async () => {
    rows = [may15]
    const u = user()
    await openTab(u)
    const closed = rowOf('15. maj 2026')
    const reinstate = async (reply: Reply) => {
      postReply = reply
      await u.click(within(closed).getByRole('button', { name: 'Genindfør' }))
      setDate(within(closed).getByLabelText('Genindføres fra'), '2026-11-10')
      await u.click(within(closed).getByRole('button', { name: 'Genindfør' }))
    }
    await reinstate({ status: 409, body: { error: 'x', conflictingCustomId: june5.customId } })
    expect(await within(closed).findByText(
      'Institutionen har allerede denne dag registreret for samme overenskomst i den valgte periode.')).toBeInTheDocument()
    await u.click(within(closed).getByRole('button', { name: 'Annuller' }))
    await reinstate({ status: 409, body: { error: 'x' } })
    expect(await within(closed).findByText('Dagen er ikke afsluttet. Genindlæs siden.')).toBeInTheDocument()
  })

  // Red: an unknown code or a hidden control's field would otherwise show a code, nothing, or the English error.
  it('shows the generic sentence for an unknown code and the form alert for a hidden control', async () => {
    const u = user()
    await openTab(u)
    await screen.findByText('Der er ikke registreret kutymefridage eller lukkedage for institutionen.')
    await fillWorkedExample(u)
    postReply = { status: 400, body: { fields: [{ field: 'Kind', code: 'SOMETHING_NEW', nearestValid: null }] } }
    await u.click(screen.getByRole('button', { name: 'Tilføj' }))
    expect(await screen.findByText('Værdien kunne ikke gemmes.')).toHaveAttribute('id', 'cd-err-kind')
    expect(screen.queryByText('SOMETHING_NEW')).not.toBeInTheDocument()

    postReply = { status: 400, body: { fields: [{ field: 'PartHours', code: 'EXCEEDS_STORAGE_LIMIT', nearestValid: null }] } }
    await u.click(screen.getByRole('button', { name: 'Tilføj' }))
    await waitFor(() => expect(screen.getByRole('alert')).toHaveTextContent('Tallet er for stort til at blive gemt.'))
    expect(screen.getByRole('alert').id).toBe('')
    expect(screen.queryByText('Værdien kunne ikke gemmes.')).not.toBeInTheDocument()
  })
})
