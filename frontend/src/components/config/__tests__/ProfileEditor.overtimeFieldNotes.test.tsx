// AC-S0-8b (2) (TASK-14829, NR-P13; ruling 23). Kind F.
// Red conditions: before the change no per-field note exists, so the overtime ceiling's and the
// pre-approval's controls carry no aria-describedby and the note's id does not resolve. The note
// is the ruled text, identical on both; weekly norm and flex cap (which reach pay) get none; the
// note shows with the "Overskriv central" toggle off and on, and is not a live region.
import { describe, it, expect, vi } from 'vitest'
import { render, screen, fireEvent } from '@testing-library/react'
import { ProfileEditor } from '../ProfileEditor'

vi.stubGlobal('fetch', vi.fn(() => new Promise(() => {})))

const NOTE =
  'Påvirker endnu ikke lønberegningen. Advarslerne om overarbejde bruger stadig aftalens indbyggede værdi.'

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

/** Every control of one field row: the toggle, plus the value control when the toggle is on. */
function controlsOf(fieldKey: string): HTMLElement[] {
  const toggle = document.getElementById(`override-${fieldKey}-toggle`)
  const value = document.getElementById(`override-${fieldKey}`)
  return [toggle, value].filter((e): e is HTMLElement => e !== null)
}

function noteOf(control: HTMLElement): string | null {
  const id = control.getAttribute('aria-describedby')
  return id ? (document.getElementById(id)?.textContent ?? null) : null
}

describe('ProfileEditor — per-field "not yet in pay" notes', () => {
  for (const key of ['maxOvertimeHoursPerPeriod', 'overtimeRequiresPreApproval']) {
    it(`${key}: every control names the ruled note, toggle off and on`, () => {
      renderEditor()
      let controls = controlsOf(key)
      expect(controls).toHaveLength(1) // toggle only
      for (const c of controls) expect(noteOf(c)).toBe(NOTE)

      fireEvent.click(document.getElementById(`override-${key}-toggle`)!)
      controls = controlsOf(key)
      expect(controls).toHaveLength(2) // toggle + value control
      for (const c of controls) expect(noteOf(c)).toBe(NOTE)

      const note = document.getElementById(controls[0].getAttribute('aria-describedby')!)!
      expect(note.getAttribute('aria-live')).toBeNull()
      expect(note.closest('[role="alert"], [aria-live]')).toBeNull()
    })
  }

  for (const key of ['weeklyNormHours', 'maxFlexBalance']) {
    it(`${key}: no per-field note, toggle off and on`, () => {
      renderEditor()
      for (const c of controlsOf(key)) expect(c.getAttribute('aria-describedby')).toBeNull()
      fireEvent.click(document.getElementById(`override-${key}-toggle`)!)
      for (const c of controlsOf(key)) expect(c.getAttribute('aria-describedby')).toBeNull()
      expect(screen.getAllByText(NOTE)).toHaveLength(2) // the two overtime fields only
    })
  }
})
