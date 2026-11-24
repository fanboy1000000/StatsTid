// AC-C2v (N-7). Kind F.
// Red conditions: before the change 24 of the editor's 33 labels carry no htmlFor, so
// getByLabelText cannot find the 24 fields below whose label is untied (the eight checkboxes were
// already tied); the assertion that each lookup returns a form control fails with "Unable to find a
// form control associated with that label". Where a label text repeats (Start, Slut, Sats), each
// lookup is scoped to its fieldset by the legend, so a tie to the wrong field fails too.
import { describe, it, expect, vi, beforeEach } from 'vitest'
import { screen, within, fireEvent } from '@testing-library/react'
import { MemoryRouter, Routes, Route } from 'react-router-dom'
import { AgreementConfigEditor } from '../AgreementConfigEditor'
import { renderWithCalendar } from '../../../test/renderWithCalendar'
import { CONFIG_ID, draftConfig } from './agreementConfigFixture'

const mockFetch = vi.fn()
vi.stubGlobal('fetch', mockFetch)

// [fieldset legend, label text, expected control tag]
const FIELDS: [string, string, string][] = [
  ['Grundlaeggende', 'Overenskomstkode', 'INPUT'],
  ['Grundlaeggende', 'OK-version', 'INPUT'],
  ['Grundlaeggende', 'Normmodel', 'SELECT'],
  ['Grundlaeggende', 'Beskrivelse', 'TEXTAREA'],
  ['Normtid', 'Ugentlige normtimer', 'INPUT'],
  ['Normtid', 'Normperiode (uger)', 'INPUT'],
  ['Normtid', 'Arlige normtimer', 'INPUT'],
  ['Flex', 'Maks. flexsaldo (timer)', 'INPUT'],
  ['Overarbejde', 'Overarbejde aktiveret', 'INPUT'],
  ['Overarbejde', 'Merarbejde aktiveret', 'INPUT'],
  ['Overarbejde', 'Overarbejdsgraense 50% (timer)', 'INPUT'],
  ['Overarbejde', 'Overarbejdsgraense 100% (timer)', 'INPUT'],
  ['Aftentillaeg', 'Aftentillaeg aktiveret', 'INPUT'],
  ['Aftentillaeg', 'Start (time)', 'INPUT'],
  ['Aftentillaeg', 'Slut (time)', 'INPUT'],
  ['Aftentillaeg', 'Sats', 'INPUT'],
  ['Nattillaeg', 'Nattillaeg aktiveret', 'INPUT'],
  ['Nattillaeg', 'Start (time)', 'INPUT'],
  ['Nattillaeg', 'Slut (time)', 'INPUT'],
  ['Nattillaeg', 'Sats', 'INPUT'],
  ['Weekendtillaeg', 'Weekendtillaeg aktiveret', 'INPUT'],
  ['Weekendtillaeg', 'Lordagssats', 'INPUT'],
  ['Weekendtillaeg', 'Sondagssats', 'INPUT'],
  ['Helligdagstillaeg', 'Helligdagstillaeg aktiveret', 'INPUT'],
  ['Helligdagstillaeg', 'Sats', 'INPUT'],
  ['Radighedsvagt', 'Radighedsvagt aktiveret', 'INPUT'],
  ['Radighedsvagt', 'Sats', 'INPUT'],
  ['Tilkald', 'Tilkald aktiveret', 'INPUT'],
  ['Tilkald', 'Minimumstimer', 'INPUT'],
  ['Tilkald', 'Sats', 'INPUT'],
  ['Rejsetid', 'Rejsetid aktiveret', 'INPUT'],
  ['Rejsetid', 'Arbejdsrejse-sats', 'INPUT'],
  ['Rejsetid', 'Ikke-arbejdsrejse-sats', 'INPUT'],
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
    for (const [legend, label, tag] of FIELDS) {
      const group = within(screen.getByRole('group', { name: legend }))
      const control = group.getByLabelText(label)
      expect(control.tagName, `${legend} / ${label}`).toBe(tag)
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
