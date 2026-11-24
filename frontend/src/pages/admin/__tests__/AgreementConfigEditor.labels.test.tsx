// AC-C2v (N-7). Kind F.
// Red conditions: before the change 24 of the editor's 33 labels carry no htmlFor, so
// getByLabelText cannot find the 24 fields below whose label is untied (the eight checkboxes were
// already tied); the assertion that each lookup returns a form control fails with "Unable to find a
// form control associated with that label". Where a label text repeats (Start, Slut, Sats), each
// lookup is scoped to its fieldset by the legend, so a tie to the wrong field fails too, and each
// row names the id its label must reach (S148 T23 FU-1: a label tied to another field's control
// would pass the tag check alone).
import { describe, it, expect, vi, beforeEach } from 'vitest'
import { screen, within, fireEvent } from '@testing-library/react'
import { MemoryRouter, Routes, Route } from 'react-router-dom'
import { AgreementConfigEditor } from '../AgreementConfigEditor'
import { renderWithCalendar } from '../../../test/renderWithCalendar'
import { CONFIG_ID, draftConfig } from './agreementConfigFixture'

const mockFetch = vi.fn()
vi.stubGlobal('fetch', mockFetch)

// [fieldset legend, label text, expected control tag, expected control id]
const FIELDS: [string, string, string, string][] = [
  ['Grundlaeggende', 'Overenskomstkode', 'INPUT', 'ac-agreementCode'],
  ['Grundlaeggende', 'OK-version', 'INPUT', 'ac-okVersion'],
  ['Grundlaeggende', 'Normmodel', 'SELECT', 'ac-normModel'],
  ['Grundlaeggende', 'Beskrivelse', 'TEXTAREA', 'ac-description'],
  ['Normtid', 'Ugentlige normtimer', 'INPUT', 'ac-weeklyNormHours'],
  ['Normtid', 'Normperiode (uger)', 'INPUT', 'ac-normPeriodWeeks'],
  ['Normtid', 'Arlige normtimer', 'INPUT', 'ac-annualNormHours'],
  ['Flex', 'Maks. flexsaldo (timer)', 'INPUT', 'ac-maxFlexBalance'],
  ['Overarbejde', 'Overarbejde aktiveret', 'INPUT', 'hasOvertime'],
  ['Overarbejde', 'Merarbejde aktiveret', 'INPUT', 'hasMerarbejde'],
  ['Overarbejde', 'Overarbejdsgraense 50% (timer)', 'INPUT', 'ac-overtimeThreshold50'],
  ['Overarbejde', 'Overarbejdsgraense 100% (timer)', 'INPUT', 'ac-overtimeThreshold100'],
  ['Aftentillaeg', 'Aftentillaeg aktiveret', 'INPUT', 'eveningSupplementEnabled'],
  ['Aftentillaeg', 'Start (time)', 'INPUT', 'ac-eveningStart'],
  ['Aftentillaeg', 'Slut (time)', 'INPUT', 'ac-eveningEnd'],
  ['Aftentillaeg', 'Sats', 'INPUT', 'ac-eveningRate'],
  ['Nattillaeg', 'Nattillaeg aktiveret', 'INPUT', 'nightSupplementEnabled'],
  ['Nattillaeg', 'Start (time)', 'INPUT', 'ac-nightStart'],
  ['Nattillaeg', 'Slut (time)', 'INPUT', 'ac-nightEnd'],
  ['Nattillaeg', 'Sats', 'INPUT', 'ac-nightRate'],
  ['Weekendtillaeg', 'Weekendtillaeg aktiveret', 'INPUT', 'weekendSupplementEnabled'],
  ['Weekendtillaeg', 'Lordagssats', 'INPUT', 'ac-weekendSaturdayRate'],
  ['Weekendtillaeg', 'Sondagssats', 'INPUT', 'ac-weekendSundayRate'],
  ['Helligdagstillaeg', 'Helligdagstillaeg aktiveret', 'INPUT', 'holidaySupplementEnabled'],
  ['Helligdagstillaeg', 'Sats', 'INPUT', 'ac-holidayRate'],
  ['Radighedsvagt', 'Radighedsvagt aktiveret', 'INPUT', 'onCallDutyEnabled'],
  ['Radighedsvagt', 'Sats', 'INPUT', 'ac-onCallDutyRate'],
  ['Tilkald', 'Tilkald aktiveret', 'INPUT', 'callInWorkEnabled'],
  ['Tilkald', 'Minimumstimer', 'INPUT', 'ac-callInMinimumHours'],
  ['Tilkald', 'Sats', 'INPUT', 'ac-callInRate'],
  ['Rejsetid', 'Rejsetid aktiveret', 'INPUT', 'travelTimeEnabled'],
  ['Rejsetid', 'Arbejdsrejse-sats', 'INPUT', 'ac-workingTravelRate'],
  ['Rejsetid', 'Ikke-arbejdsrejse-sats', 'INPUT', 'ac-nonWorkingTravelRate'],
]

beforeEach(() => {
  mockFetch.mockReset()
  mockFetch.mockResolvedValue({
    ok: true,
    status: 200,
    headers: new Headers({ ETag: '"3"' }),
    json: async () => draftConfig,
  })
})

describe('AgreementConfigEditor — every label is tied to its field (N-7)', () => {
  it('reads each of the 33 fields by its label, scoped by fieldset', async () => {
    renderWithCalendar(
      '2026-11-12',
      <MemoryRouter initialEntries={[`/global/overenskomster/${CONFIG_ID}`]}>
        <Routes>
          <Route path="/global/overenskomster/:configId" element={<AgreementConfigEditor />} />
        </Routes>
      </MemoryRouter>,
    )
    await screen.findByText('AC / OK24')

    expect(FIELDS).toHaveLength(33)
    const ids = new Set<string>()
    for (const [legend, label, tag, id] of FIELDS) {
      const group = within(screen.getByRole('group', { name: legend }))
      const control = group.getByLabelText(label)
      expect(control.tagName, `${legend} / ${label}`).toBe(tag)
      expect(control.id, `${legend} / ${label}`).toBe(id)
      ids.add(control.id)
    }
    // No two fields share an id (a repeated label text must not collapse onto one field).
    expect(ids.size).toBe(33)
  })

  it("reads the publish dialog's date input by its label too", async () => {
    // The version list (read when the dialog opens) is a bare array; the by-id GET is the config.
    mockFetch.mockImplementation(async (url: string) => ({
      ok: true,
      status: 200,
      headers: new Headers({ ETag: '"3"' }),
      json: async () => (url.endsWith('/AC/OK24') ? [] : draftConfig),
    }))
    renderWithCalendar(
      '2026-11-12',
      <MemoryRouter initialEntries={[`/global/overenskomster/${CONFIG_ID}`]}>
        <Routes>
          <Route path="/global/overenskomster/:configId" element={<AgreementConfigEditor />} />
        </Routes>
      </MemoryRouter>,
    )
    await screen.findByText('AC / OK24')
    fireEvent.click(screen.getByRole('button', { name: 'Publicer' }))
    const date = screen.getByLabelText('Gælder fra')
    expect(date.tagName).toBe('INPUT')
    expect(date.getAttribute('type')).toBe('date')
  })
})
