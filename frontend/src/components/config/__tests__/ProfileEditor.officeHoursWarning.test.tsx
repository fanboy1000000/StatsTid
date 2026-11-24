// AC-C4c (TASK-14833). Kind F.
// Red conditions: before the change the office-hours inputs do not exist, so the first
// getByLabelText('Mandag fra') throws. The warning sits in one always-mounted polite region (not
// role="alert"), names the sum and the norm in hours, and never blocks the save: the PUT is sent
// unchanged, with clock times as "HH:mm:ss".
import { describe, it, expect, vi, afterEach } from 'vitest'
import { render, screen, fireEvent, waitFor } from '@testing-library/react'
import { ProfileEditor } from '../ProfileEditor'

const REGION_ID = 'profile-setting-officeHours-warning'
const DAYS = ['Mandag', 'Tirsdag', 'Onsdag', 'Torsdag', 'Fredag']

function renderEditor() {
  return render(
    <ProfileEditor
      orgId="ORG1"
      agreementCode="AC"
      okVersion="OK24"
      orgLabel="Test org"
      profile={null}
      etag={null}
      centralConfig={null}
      loading={false}
      loadError={null}
      onSaved={() => {}}
    />,
  )
}

function setWeek(times: [string, string][]) {
  times.forEach(([from, to], i) => {
    fireEvent.change(screen.getByLabelText(`${DAYS[i]} fra`), { target: { value: from } })
    fireEvent.change(screen.getByLabelText(`${DAYS[i]} til`), { target: { value: to } })
  })
}

const region = () => document.getElementById(REGION_ID)!

const WEEK_36: [string, string][] = [
  ['08:00', '15:30'], ['08:00', '15:30'], ['08:00', '15:30'], ['08:00', '15:30'], ['08:00', '14:00'],
]
const WEEK_37: [string, string][] = [
  ['08:00', '16:00'], ['08:00', '16:00'], ['08:00', '16:00'], ['08:00', '16:00'], ['08:00', '13:00'],
]

describe('ProfileEditor — office-hours warning (never a refusal)', () => {
  afterEach(() => vi.unstubAllGlobals())

  it('a 36 h week warns in a polite region, keeps Save enabled and sends one PUT', async () => {
    const fetchMock = vi.fn().mockResolvedValue({
      ok: true, status: 200, headers: { get: () => null },
      text: async () => '{}', json: async () => ({}),
    })
    vi.stubGlobal('fetch', fetchMock)
    renderEditor()
    setWeek(WEEK_36)
    expect(region().textContent).toBe(
      'Kontortiden er i alt 36 timer om ugen, men den ugentlige norm er 37 timer. Du kan stadig gemme.',
    )
    expect(region().getAttribute('aria-live')).toBe('polite')
    expect(region().getAttribute('role')).toBeNull()

    fireEvent.change(document.getElementById('profile-effective-from')!, { target: { value: '2026-03-02' } })
    const save = screen.getByRole('button', { name: /opret profil/i }) as HTMLButtonElement
    expect(save.disabled).toBe(false)
    fireEvent.click(save)
    await waitFor(() => expect(fetchMock).toHaveBeenCalledTimes(1))
    const init = fetchMock.mock.calls[0][1] as RequestInit
    expect(init.method).toBe('PUT')
    const body = JSON.parse(init.body as string)
    expect(body.officeHoursMonEnd).toBe('15:30:00')
    expect(body.officeHoursFriEnd).toBe('14:00:00')
  })

  it('a 37 h week leaves the region empty', () => {
    renderEditor()
    setWeek(WEEK_37)
    expect(region().textContent).toBe('')
  })

  it('compares with the weekly-norm override in the draft when it is on', () => {
    renderEditor()
    fireEvent.click(document.getElementById('override-weeklyNormHours-toggle')!)
    fireEvent.change(document.getElementById('override-weeklyNormHours')!, { target: { value: '36' } })
    setWeek(WEEK_36)
    expect(region().textContent).toBe('')
    setWeek(WEEK_37)
    expect(region().textContent).toBe(
      'Kontortiden er i alt 37 timer om ugen, men den ugentlige norm er 36 timer. Du kan stadig gemme.',
    )
  })

  it('with no window at all there is no warning', () => {
    renderEditor()
    screen.getByLabelText('Mandag fra') // the inputs exist before "absent" is asserted
    expect(region().textContent).toBe('')
  })
})
