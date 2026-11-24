// The institution's new flex / office-hours / credit-basis settings on the local-profile editor
// (TASK-14833; AC-C4h, and the cases the change reaches). Kind F for every case.
// Red conditions: before the change none of these controls exist (every lookup throws) and
// fieldErrorMessage maps none of the nine refusal codes.
import { describe, it, expect, vi, afterEach } from 'vitest'
import { render, screen, fireEvent, waitFor, within } from '@testing-library/react'
import { ProfileEditor } from '../ProfileEditor'
import type { LocalAgreementProfile } from '../../../hooks/useConfig'

const D8B_LABEL =
  'Standard ved analogi: Ingen central aftaletekst fastsætter, hvordan en betalt sygedag tæller for AC-ansatte. StatsTid bruger derfor reglen for HK og PROSA (§ 7 nr. 2) som standard.'
const HINT = 'Kræver kontortid for mindst én ugedag.'

const emptyProfile: LocalAgreementProfile = {
  profileId: '11111111-1111-1111-1111-111111111111',
  orgId: 'ORG1',
  agreementCode: 'AC',
  okVersion: 'OK24',
  effectiveFrom: '2026-04-27',
  effectiveTo: null,
  weeklyNormHours: 35,
  maxFlexBalance: null,
  maxOvertimeHoursPerPeriod: null,
  overtimeRequiresPreApproval: null,
  createdBy: 'admin@example.dk',
  createdAt: '2026-04-27T08:00:00Z',
  version: 1,
  flexSettlementPeriod: null,
  flexCapTestPeriod: null,
  flexPlusCapHours: null,
  flexMinusCapHours: null,
  flexDailyCreditCapHours: null,
  flexAboveCapAction: null,
  flexLeavingAction: null,
  officeHoursMonStart: null,
  officeHoursMonEnd: null,
  officeHoursTueStart: null,
  officeHoursTueEnd: null,
  officeHoursWedStart: null,
  officeHoursWedEnd: null,
  officeHoursThuStart: null,
  officeHoursThuEnd: null,
  officeHoursFriStart: null,
  officeHoursFriEnd: null,
  paidAbsenceCreditBasis: null,
}

const fullProfile: LocalAgreementProfile = {
  ...emptyProfile,
  flexSettlementPeriod: 'MONTH',
  flexCapTestPeriod: 'MONTH',
  flexPlusCapHours: 37,
  flexMinusCapHours: 10,
  flexDailyCreditCapHours: 13,
  flexAboveCapAction: 'FORFEIT',
  flexLeavingAction: 'FORFEIT_POSITIVE_RECORD_NEGATIVE',
  officeHoursMonStart: '08:00:00',
  officeHoursMonEnd: '15:30:00',
  officeHoursTueStart: '08:00:00',
  officeHoursTueEnd: '15:30:00',
  officeHoursWedStart: '08:00:00',
  officeHoursWedEnd: '15:30:00',
  officeHoursThuStart: '08:00:00',
  officeHoursThuEnd: '15:30:00',
  officeHoursFriStart: '08:00:00',
  officeHoursFriEnd: '14:00:00',
  paidAbsenceCreditBasis: 'OFFICE_HOURS',
}

function renderEditor(profile: LocalAgreementProfile | null, agreementCode = 'AC') {
  return render(
    <ProfileEditor
      orgId="ORG1"
      agreementCode={agreementCode}
      okVersion="OK24"
      orgLabel="Test org"
      profile={profile}
      etag={profile ? `"${profile.version}"` : null}
      centralConfig={null}
      loading={false}
      loadError={null}
      onSaved={() => {}}
    />,
  )
}

function mockResponse(status: number, body: unknown) {
  const ok = status >= 200 && status < 300
  return vi.fn().mockResolvedValue({
    ok, status, headers: { get: () => null },
    text: async () => JSON.stringify(body), json: async () => body,
  })
}

const saveButton = () => screen.getByRole('button', { name: /gem aendringer/i }) as HTMLButtonElement
const ids = (el: HTMLElement) => (el.getAttribute('aria-describedby') ?? '').split(/\s+/)

describe('ProfileEditor — new settings', () => {
  afterEach(() => vi.unstubAllGlobals())

  it('(a) setting the plus cap alone enables Save and PUTs exactly 23 keys', async () => {
    const fetchMock = mockResponse(200, {})
    vi.stubGlobal('fetch', fetchMock)
    renderEditor(emptyProfile)
    const cap = screen.getByLabelText('Loft for plussaldo på flex (timer)')
    expect(saveButton().disabled).toBe(true)
    fireEvent.change(cap, { target: { value: '37' } })
    expect(saveButton().disabled).toBe(false)
    fireEvent.click(saveButton())
    await waitFor(() => expect(fetchMock).toHaveBeenCalledTimes(1))
    const body = JSON.parse((fetchMock.mock.calls[0][1] as RequestInit).body as string)
    expect(Object.keys(body)).toHaveLength(23)
    expect(body.flexPlusCapHours).toBe(37)
    const others = Object.entries(body).filter(([k]) =>
      /^(flex|officeHours|paidAbsence)/.test(k) && k !== 'flexPlusCapHours')
    expect(others).toHaveLength(17)
    for (const [, v] of others) expect(v).toBeNull()
    expect(body.weeklyNormHours).toBe(35)
    expect(body.maxFlexBalance).toBeNull()
    expect(body.effectiveFrom).toBe('2026-04-27')
  })

  it.each(['AC', 'AC_RESEARCH'])('(b) the D-8b label shows once under the credit-basis legend for %s', (code) => {
    renderEditor(emptyProfile, code)
    const group = screen.getByRole('group', { name: 'Grundlag for godskrivning af betalt fravær' })
    const label = within(group).getByText(D8B_LABEL)
    expect(screen.getAllByText(D8B_LABEL)).toHaveLength(1)
    expect(ids(group)).toContain(label.id)
    expect(group.firstElementChild?.tagName).toBe('LEGEND')
    expect(group.children[1]).toBe(label)
  })

  it('(b) the D-8b label is absent for HK', () => {
    renderEditor(emptyProfile, 'HK')
    screen.getByRole('group', { name: 'Grundlag for godskrivning af betalt fravær' })
    expect(screen.queryByText(D8B_LABEL)).toBeNull()
  })

  it('(c) a refused plus cap shows its Danish message, aria-invalid and both ids', async () => {
    vi.stubGlobal('fetch', mockResponse(400, {
      error: 'Profile validation failed',
      fields: [{ field: 'FlexPlusCapHours', code: 'MUST_NOT_BE_NEGATIVE' }],
    }))
    renderEditor(emptyProfile)
    const cap = screen.getByLabelText('Loft for plussaldo på flex (timer)')
    fireEvent.change(cap, { target: { value: '-1' } })
    fireEvent.click(saveButton())
    const msg = await screen.findByText('Må ikke være negativ.')
    expect(cap.getAttribute('aria-invalid')).toBe('true')
    expect(ids(cap)).toContain(msg.id)
    expect(ids(cap)).toContain('profile-setting-flexPlusCapHours-note')
  })

  it('(d) a loaded profile shows times, radios and caps', () => {
    renderEditor(fullProfile)
    expect((screen.getByLabelText('Mandag fra') as HTMLInputElement).value).toBe('08:00')
    expect((screen.getByLabelText('Mandag til') as HTMLInputElement).value).toBe('15:30')
    expect((screen.getByLabelText('Fredag til') as HTMLInputElement).value).toBe('14:00')
    expect((screen.getByLabelText('Loft for plussaldo på flex (timer)') as HTMLInputElement).value).toBe('37')
    expect((screen.getByLabelText('Loft for minussaldo på flex (timer, uden minustegn)') as HTMLInputElement).value).toBe('10')
    expect((screen.getByLabelText('Loft for daglig optjening af flex (timer)') as HTMLInputElement).value).toBe('13')
    const checked = (group: string, label: string) =>
      (within(screen.getByRole('group', { name: group })).getByLabelText(label) as HTMLInputElement).checked
    expect(checked('Afregningsperiode for flex', 'Kalendermåned')).toBe(true)
    expect(checked('Periode, som flexloftet måles over', 'Kalendermåned')).toBe(true)
    expect(checked('Timer over loftet for plussaldo', 'Bortfalder')).toBe(true)
    expect(checked('Flexsaldo ved fratræden',
      'En positiv saldo bortfalder, en negativ registreres og modregnes ikke')).toBe(true)
    expect(checked('Grundlag for godskrivning af betalt fravær', 'Kontortid: ugedagens kontortid')).toBe(true)
  })

  it('(e) Kontortid is disabled with its hint until a weekday has both times', () => {
    renderEditor(emptyProfile)
    const group = screen.getByRole('group', { name: 'Grundlag for godskrivning af betalt fravær' })
    const radio = within(group).getByLabelText('Kontortid: ugedagens kontortid') as HTMLInputElement
    const hint = screen.getByText(HINT)
    expect(radio.disabled).toBe(true)
    expect(ids(group)).toContain(hint.id)

    fireEvent.change(screen.getByLabelText('Mandag fra'), { target: { value: '08:00' } })
    expect(radio.disabled).toBe(true) // a lone start is not a window
    fireEvent.change(screen.getByLabelText('Mandag til'), { target: { value: '15:30' } })
    expect(radio.disabled).toBe(false)
    expect(screen.queryByText(HINT)).toBeNull()
  })

  // [code, field, message, where the message must sit, the control's or group's name]
  type Place = 'input' | 'group'
  const CODES: [string, string, string, Place, string][] = [
    // The legacy row keeps FormField's id-less error span (batch-A adjudication § 3): placement and
    // aria-invalid only, no id / aria-describedby check for this case.
    ['MUST_BE_POSITIVE', 'WeeklyNormHours', 'Skal være større end 0.', 'input', 'Ugentlig normtimer'],
    ['MUST_NOT_BE_NEGATIVE', 'FlexMinusCapHours', 'Må ikke være negativ.', 'input',
      'Loft for minussaldo på flex (timer, uden minustegn)'],
    ['TOO_MANY_DECIMALS', 'FlexDailyCreditCapHours', 'Brug højst to decimaler.', 'input',
      'Loft for daglig optjening af flex (timer)'],
    ['EXCEEDS_STORAGE_LIMIT', 'FlexPlusCapHours', 'Tallet er for stort til at blive gemt.', 'input',
      'Loft for plussaldo på flex (timer)'],
    ['UNKNOWN_VALUE', 'FlexSettlementPeriod', 'Ugyldig værdi. Vælg en af de viste muligheder.', 'group',
      'Afregningsperiode for flex'],
    ['NOT_WHOLE_MINUTE', 'OfficeHoursMonStart', 'Angiv klokkeslættet i hele minutter.', 'input', 'Mandag fra'],
    ['WINDOW_INCOMPLETE', 'OfficeHoursTueEnd', 'Angiv både fra og til, eller lad begge stå tomme.', 'input',
      'Tirsdag til'],
    ['WINDOW_START_NOT_BEFORE_END', 'OfficeHoursWedEnd', '"Til" skal være senere end "fra".', 'input', 'Onsdag til'],
    ['OFFICE_HOURS_WITHOUT_WINDOWS', 'PaidAbsenceCreditBasis',
      'Kontortid kan kun vælges som grundlag, når der er angivet kontortid for mindst én ugedag.', 'group',
      'Grundlag for godskrivning af betalt fravær'],
  ]
  it.each(CODES)('(f) %s shows its Danish message inside the control of %s', async (code, field, message, place, name) => {
    vi.stubGlobal('fetch', mockResponse(400, {
      error: 'Profile validation failed', fields: [{ field, code }],
    }))
    renderEditor(emptyProfile)
    fireEvent.change(screen.getByLabelText('Loft for plussaldo på flex (timer)'), { target: { value: '1' } })
    fireEvent.click(saveButton())
    const msg = await screen.findByText(message)
    expect(screen.getAllByText(message)).toHaveLength(1)
    if (place === 'group') {
      const group = screen.getByRole('group', { name })
      expect(group.contains(msg)).toBe(true)
      expect(msg.id).not.toBe('')
      expect(ids(group)).toContain(msg.id)
      expect(group.hasAttribute('aria-invalid')).toBe(false)
    } else {
      const input = screen.getByLabelText(name)
      const label = document.querySelector(`label[for="${input.id}"]`) as HTMLElement
      expect(label.parentElement!.contains(msg)).toBe(true)
      expect(input.getAttribute('aria-invalid')).toBe('true')
      // The legacy override row (WeeklyNormHours) renders its error through FormField, whose span
      // has no id to point at; every control of this card references its message.
      if (field !== 'WeeklyNormHours') {
        expect(msg.id).not.toBe('')
        expect(ids(input)).toContain(msg.id)
      }
    }
  })

  describe('a refusal the editor cannot place', () => {
    const BANNER = 'Værdien kunne ikke gemmes.'
    function refuse(fields: unknown[]) {
      vi.stubGlobal('fetch', mockResponse(400, { error: 'Profile validation failed', fields }))
      renderEditor(emptyProfile)
      fireEvent.change(screen.getByLabelText('Loft for plussaldo på flex (timer)'), { target: { value: '1' } })
      fireEvent.click(saveButton())
    }

    it('(g) an unknown field, or an empty one, alone shows the banner', async () => {
      refuse([{ field: 'SomethingElse', code: 'MUST_BE_POSITIVE' }, { field: '', code: 'UNKNOWN_VALUE' }])
      await screen.findByText(BANNER)
      expect(screen.queryByText('Skal være større end 0.')).toBeNull()
      expect(screen.queryByText('Ugyldig værdi. Vælg en af de viste muligheder.')).toBeNull()
    })

    it('(h) mixed with a known field, both the banner and the field message show', async () => {
      refuse([
        { field: 'SomethingElse', code: 'MUST_BE_POSITIVE' },
        { field: 'FlexPlusCapHours', code: 'MUST_NOT_BE_NEGATIVE' },
      ])
      await screen.findByText(BANNER)
      const cap = screen.getByLabelText('Loft for plussaldo på flex (timer)')
      const msg = screen.getByText('Må ikke være negativ.')
      expect(ids(cap)).toContain(msg.id)
    })

    it('(j) a known field with an unmapped code shows the banner text under its control, never the code', async () => {
      refuse([{ field: 'FlexPlusCapHours', code: 'SOMETHING_NEW' }])
      const msg = await screen.findAllByText(BANNER)
      expect(screen.queryByText('SOMETHING_NEW')).toBeNull()
      const cap = screen.getByLabelText('Loft for plussaldo på flex (timer)')
      expect(msg.some(m => ids(cap).includes(m.id))).toBe(true)
      expect(cap.getAttribute('aria-invalid')).toBe('true')
    })

    it('(i) two entries for one field show only the first', async () => {
      refuse([
        { field: 'FlexPlusCapHours', code: 'MUST_NOT_BE_NEGATIVE' },
        { field: 'FlexPlusCapHours', code: 'EXCEEDS_STORAGE_LIMIT' },
      ])
      await screen.findByText('Må ikke være negativ.')
      expect(screen.queryByText('Tallet er for stort til at blive gemt.')).toBeNull()
      expect(screen.queryByText(BANNER)).toBeNull()
    })
  })
})
