// AC-S0-8c (TASK-14833). Kind F.
// Red conditions: before the change no D6 control exists, so the first group lookup throws. Every
// control of each of the nine fields (each input; for a radio group or the office-hours group, its
// <fieldset>) names an element holding the ruled D6 note; there are exactly nine such elements,
// none inside a live region, and the NR-P13 note still appears exactly twice.
import { describe, it, expect, vi } from 'vitest'
import { render, screen } from '@testing-library/react'
import { ProfileEditor } from '../ProfileEditor'

vi.stubGlobal('fetch', vi.fn(() => new Promise(() => {})))

const D6_NOTE =
  'Gemmes nu, men bruges endnu ikke i nogen beregning og påvirker endnu ikke lønberegningen.'
const NR_P13_NOTE =
  'Påvirker endnu ikke lønberegningen. Advarslerne om overarbejde bruger stadig aftalens indbyggede værdi.'

const GROUPS = [
  'Afregningsperiode for flex',
  'Periode, som flexloftet måles over',
  'Timer over loftet for plussaldo',
  'Flexsaldo ved fratræden',
  'Kontortid',
  'Grundlag for godskrivning af betalt fravær',
]
const INPUTS = [
  'Loft for plussaldo på flex (timer)',
  'Loft for minussaldo på flex (timer, uden minustegn)',
  'Loft for daglig optjening af flex (timer)',
  ...['Mandag', 'Tirsdag', 'Onsdag', 'Torsdag', 'Fredag'].flatMap(d => [`${d} fra`, `${d} til`]),
]

function noteText(control: HTMLElement): string | undefined {
  const ids = (control.getAttribute('aria-describedby') ?? '').split(/\s+/)
  return ids.map(id => document.getElementById(id)?.textContent).find(t => t === D6_NOTE)
}

describe('ProfileEditor — D6 per-field notes', () => {
  it('every control of the nine fields names the ruled note; nine note elements, none live', () => {
    render(
      <ProfileEditor
        orgId="ORG1" agreementCode="AC" okVersion="OK24" orgLabel="Test org"
        profile={null} etag={null} centralConfig={null}
        loading={false} loadError={null} onSaved={() => {}}
      />,
    )
    const controls = [
      ...GROUPS.map(name => screen.getByRole('group', { name })),
      ...INPUTS.map(label => screen.getByLabelText(label)),
    ]
    for (const c of controls) expect(noteText(c)).toBe(D6_NOTE)

    const notes = screen.getAllByText(D6_NOTE)
    expect(notes).toHaveLength(9)
    for (const n of notes) expect(n.closest('[role="alert"], [aria-live]')).toBeNull()
    expect(screen.getAllByText(NR_P13_NOTE)).toHaveLength(2)
  })
})
