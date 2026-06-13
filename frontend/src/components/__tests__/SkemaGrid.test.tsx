// S72 / TASK-7202 — component-level tests for the restructured Skema grid
// (design_handoff_skema README §3). Mocked props/handlers only (R13 layering: the
// end-to-end pins live in 7204/7205). Fixture Maps are created once per test and
// passed by reference (PAT-007 — referentially stable inputs, no per-render litter).
//
// March 2026 reference: Mar 1 = Sunday, Mar 2 = Monday, Mar 7 = Saturday,
// Mar 8 = Sunday, Mar 11 = Wednesday. Row tds: [0] = label, [1..31] = days,
// [32] = trailing Sum cell.
import { render, fireEvent, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { useState } from 'react'
import { SkemaGrid } from '../SkemaGrid'
import type { SkemaRow, SkemaRowPreferences } from '../../types'

const mockRows: SkemaRow[] = [
  { type: 'project', key: 'DRIFT', label: 'Drift' },
  { type: 'project', key: 'PROJ-2', label: 'Udvikling' },
  { type: 'absence', key: 'VACATION', label: 'Ferie' },
]

const prefsAll: SkemaRowPreferences = {
  configured: true,
  projects: [
    { projectId: 'p1', projectCode: 'DRIFT', projectName: 'Drift', sortOrder: 0 },
    { projectId: 'p2', projectCode: 'PROJ-2', projectName: 'Udvikling', sortOrder: 1 },
  ],
  absenceTypes: [{ type: 'VACATION', label: 'Ferie', sortOrder: 0 }],
}

const prefsHideDrift: SkemaRowPreferences = {
  configured: true,
  projects: [{ projectId: 'p2', projectCode: 'PROJ-2', projectName: 'Udvikling', sortOrder: 0 }],
  absenceTypes: [{ type: 'VACATION', label: 'Ferie', sortOrder: 0 }],
}

function renderGrid(overrides: Partial<Parameters<typeof SkemaGrid>[0]> = {}) {
  return render(
    <SkemaGrid
      year={2026}
      month={3}
      rows={mockRows}
      cellValues={new Map()}
      readOnly={false}
      onCellChange={vi.fn()}
      {...overrides}
    />
  )
}

/** Find a tbody row by the exact start of its label cell. */
function rowByLabel(container: HTMLElement, label: string): HTMLElement {
  const rows = Array.from(container.querySelectorAll('tbody tr'))
  const row = rows.find((r) => r.querySelector('td')?.textContent?.startsWith(label))
  expect(row).toBeTruthy()
  return row as HTMLElement
}

/** Day cell of a row: [0] = label, [day] = day-of-month, [last] = Sum. */
function dayCell(row: HTMLElement, day: number): HTMLElement {
  return row.querySelectorAll('td')[day] as HTMLElement
}

function sumCell(row: HTMLElement): HTMLElement {
  const cells = row.querySelectorAll('td')
  return cells[cells.length - 1] as HTMLElement
}

afterEach(() => {
  vi.unstubAllGlobals()
  vi.useRealTimers()
})

describe('SkemaGrid — structure', () => {
  it('renders 33 header columns for March 2026 with Dato and Sum labels', () => {
    const { container } = renderGrid()
    const headerCells = container.querySelectorAll('thead th')
    expect(headerCells.length).toBe(33) // 1 label + 31 days + 1 sum
    expect(headerCells[0].textContent).toBe('Dato')
    expect(headerCells[32].textContent).toBe('Sum')
  })

  it('renders the handoff row order: Diff → Registrér arbejdstid → divider → Projekter band → project rows → Ferie og fravær band → absence rows → I alt', () => {
    const { container } = renderGrid()
    const trs = Array.from(container.querySelectorAll('tbody tr'))
    expect(trs.length).toBe(9)
    expect(trs[0].className).toContain('diffRow')
    expect(trs[0].querySelector('td')?.textContent).toBe('Diff. fra normtid')
    expect(trs[1].className).toContain('workRow')
    expect(trs[1].querySelector('td')?.textContent).toBe('Registrér arbejdstid')
    expect(trs[2].className).toContain('dividerRow')
    expect(trs[2].textContent).toBe('') // no label, no gap — a pure rule
    expect(trs[3].className).toContain('bandRow')
    expect(trs[3].textContent).toContain('Projekter')
    expect(trs[4].querySelector('td')?.textContent).toBe('Drift')
    expect(trs[5].querySelector('td')?.textContent).toBe('Udvikling')
    expect(trs[6].className).toContain('bandRow')
    expect(trs[6].textContent).toContain('Ferie og fravær')
    expect(trs[7].querySelector('td')?.textContent).toBe('Ferie')
    expect(trs[8].className).toContain('totalRow')
    expect(trs[8].querySelector('td')?.textContent).toBe('I alt')
  })

  it('accepts the legacy page props (workIntervals/manualHours change handlers) without rendering the retired Tilføj periode / Tilføj timer rows', () => {
    const { container } = renderGrid({
      workIntervals: new Map([['2026-03-02', [{ start: '08:00', end: '12:00' }]]]),
      onWorkIntervalsChange: vi.fn(),
      manualHours: new Map([['2026-03-02', 2]]),
      onManualHoursChange: vi.fn(),
      // no onOpenDay — the pre-7205 page; the interactive row renders as data
    })
    expect(screen.queryByText(/Tilføj periode/)).toBeNull()
    expect(screen.queryByText(/Tilføj timer/)).toBeNull()
    // Without onOpenDay the work row has no click targets.
    const workRow = rowByLabel(container, 'Registrér arbejdstid')
    expect(workRow.querySelectorAll('button').length).toBe(0)
    // The legacy-supplied work time still feeds the arithmetic: 4h + 2h worked,
    // nothing allocated → under-allocated remainder "6,0" on Mar 2.
    expect(dayCell(workRow, 2).textContent).toBe('6,0')
  })

  it('renders an informational row note (e.g. "hele dage") after the label when provided', () => {
    const rows: SkemaRow[] = [
      ...mockRows,
      { type: 'absence', key: 'CARE_DAY', label: 'Omsorgsdage', note: 'hele dage' },
    ]
    const { container } = renderGrid({ rows })
    const careRow = rowByLabel(container, 'Omsorgsdage')
    const note = careRow.querySelector('td span')
    expect(note?.className).toContain('rowNote')
    expect(note?.textContent).toBe('hele dage')
  })
})

describe('SkemaGrid — Diff. fra normtid (R2/R1 pins)', () => {
  it('R2 pin: a full-absence day renders 0,0 GREEN — the pre-S72 grid showed -7,4 red because absence hours were excluded from the diff basis', () => {
    const { container } = renderGrid({
      cellValues: new Map([['VACATION:2026-03-02', 7.4]]),
      dailyNorm: new Map([['2026-03-02', 7.4]]),
      readOnly: true,
    })
    const cell = dayCell(rowByLabel(container, 'Diff. fra normtid'), 2)
    expect(cell.textContent).toBe('0,0')
    const value = cell.querySelector('span') as HTMLElement
    expect(value.className).toContain('diffPos')
    expect(value.className).not.toContain('diffNeg')
  })

  it('R2 pin: a workday with NO registration at all renders BLANK — the pre-S72 grid deliberately surfaced the full -norm shortfall', () => {
    const { container } = renderGrid({
      dailyNorm: new Map([['2026-03-02', 7.4]]), // norm exists, nothing registered
    })
    expect(dayCell(rowByLabel(container, 'Diff. fra normtid'), 2).textContent).toBe('')
  })

  it('R1/R15 pin: a null-norm day (academic ANNUAL_ACTIVITY) renders blank even when work time is registered', () => {
    const { container } = renderGrid({
      dailyNorm: new Map([['2026-03-02', null]]),
      manualHours: new Map([['2026-03-02', 6]]),
    })
    expect(dayCell(rowByLabel(container, 'Diff. fra normtid'), 2).textContent).toBe('')
  })

  it('computes diff = (interval hours + manual hours + absence hours) − served norm, green ≥ 0 / red < 0', () => {
    const { container } = renderGrid({
      workIntervals: new Map([['2026-03-03', [{ start: '08:00', end: '12:00' }]]]), // 4h
      manualHours: new Map([
        ['2026-03-03', 2], // Mar 3: 4 + 2 + 2 = 8 → +0,6
        ['2026-03-04', 4], // Mar 4: 4 − 7,4 = −3,4
      ]),
      cellValues: new Map([['VACATION:2026-03-03', 2]]),
      dailyNorm: new Map([
        ['2026-03-03', 7.4],
        ['2026-03-04', 7.4],
      ]),
    })
    const diffRow = rowByLabel(container, 'Diff. fra normtid')
    const pos = dayCell(diffRow, 3).querySelector('span') as HTMLElement
    expect(pos.textContent).toBe('+0,6')
    expect(pos.className).toContain('diffPos')
    const neg = dayCell(diffRow, 4).querySelector('span') as HTMLElement
    expect(neg.textContent).toBe('-3,4')
    expect(neg.className).toContain('diffNeg')
  })

  it('R2 pin: the trailing Diff total sums the RENDERED values only (blank days contribute nothing)', () => {
    const { container } = renderGrid({
      manualHours: new Map([
        ['2026-03-03', 8], // +0,6
        ['2026-03-04', 4], // −3,4
      ]),
      // Mar 2 has a norm but NO registration → blank → NOT counted as −7,4.
      dailyNorm: new Map([
        ['2026-03-02', 7.4],
        ['2026-03-03', 7.4],
        ['2026-03-04', 7.4],
      ]),
    })
    const total = sumCell(rowByLabel(container, 'Diff. fra normtid'))
    expect(total.textContent).toBe('-2,8') // 0,6 − 3,4 — NOT −10,2
    expect((total.querySelector('span') as HTMLElement).className).toContain('diffNeg')
  })
})

describe('SkemaGrid — visibility-independence (R3 pins)', () => {
  const r3CellValues = new Map([
    ['DRIFT:2026-03-02', 4.4],
    ['PROJ-2:2026-03-02', 1.0],
  ])
  const r3Manual = new Map([['2026-03-02', 7.4]])
  const r3Norm = new Map<string, number | null>([['2026-03-02', 7.4]])

  function assertArithmetic(container: HTMLElement) {
    // Diff: worked 7,4 + absence 0 − norm 7,4 = 0,0
    expect(dayCell(rowByLabel(container, 'Diff. fra normtid'), 2).textContent).toBe('0,0')
    // Remainder: 7,4 − (4,4 + 1,0) = 2,0 — over ALL served project rows
    expect(dayCell(rowByLabel(container, 'Registrér arbejdstid'), 2).textContent).toBe('2,0')
    // I alt: 5,4 over ALL served cells
    const totalRow = rowByLabel(container, 'I alt')
    expect(dayCell(totalRow, 2).textContent).toBe('5,4')
    expect(sumCell(totalRow).textContent).toBe('5,4')
  }

  it('R3 pin: Diff / remainder / I alt / grand total are IDENTICAL with a populated project hidden vs visible', () => {
    const visible = renderGrid({
      cellValues: r3CellValues,
      manualHours: r3Manual,
      dailyNorm: r3Norm,
      rowPreferences: prefsAll,
    })
    assertArithmetic(visible.container)
    expect(visible.container.textContent).toContain('Drift')
    visible.unmount()

    const hidden = renderGrid({
      cellValues: r3CellValues,
      manualHours: r3Manual,
      dailyNorm: r3Norm,
      rowPreferences: prefsHideDrift,
    })
    assertArithmetic(hidden.container) // byte-identical values
    // …but the populated DRIFT row is not RENDERED.
    const rows = Array.from(hidden.container.querySelectorAll('tbody tr'))
    expect(rows.some((r) => r.querySelector('td')?.textContent === 'Drift')).toBe(false)
  })

  it('R3 pin: the hidden-rows affordance appears exactly when hidden keys carry hours in the viewed month', () => {
    // Hidden DRIFT carries hours → the note appears.
    const withHours = renderGrid({
      cellValues: r3CellValues,
      rowPreferences: prefsHideDrift,
    })
    expect(withHours.container.textContent).toContain('1 skjulte rækker har timer i denne måned')
    withHours.unmount()

    // Hidden DRIFT has NO hours → no note.
    const noHours = renderGrid({
      cellValues: new Map([['PROJ-2:2026-03-02', 1.0]]),
      rowPreferences: prefsHideDrift,
    })
    expect(noHours.container.textContent).not.toContain('skjulte rækker')
    noHours.unmount()

    // No rowPreferences (R12 fallback — every served row renders) → no note.
    const noPrefs = renderGrid({ cellValues: r3CellValues })
    expect(noPrefs.container.textContent).not.toContain('skjulte rækker')
  })

  it('the affordance is a keyboard-reachable button that fires onOpenManager', () => {
    const onOpenManager = vi.fn()
    renderGrid({
      cellValues: r3CellValues,
      rowPreferences: prefsHideDrift,
      onOpenManager,
    })
    const note = screen.getByRole('button', {
      name: '1 skjulte rækker har timer i denne måned',
    })
    expect(note.className).toContain('hiddenNoteButton')
    fireEvent.click(note)
    expect(onOpenManager).toHaveBeenCalledTimes(1)
  })

  it('renders visible rows in rowPreferences sortOrder (not served order)', () => {
    const reversed: SkemaRowPreferences = {
      configured: true,
      projects: [
        { projectId: 'p2', projectCode: 'PROJ-2', projectName: 'Udvikling', sortOrder: 0 },
        { projectId: 'p1', projectCode: 'DRIFT', projectName: 'Drift', sortOrder: 1 },
      ],
      absenceTypes: [{ type: 'VACATION', label: 'Ferie', sortOrder: 0 }],
    }
    const { container } = renderGrid({ rowPreferences: reversed })
    const labels = Array.from(container.querySelectorAll('tbody tr'))
      .map((r) => r.querySelector('td')?.textContent)
    const udvikling = labels.indexOf('Udvikling')
    const drift = labels.indexOf('Drift')
    expect(udvikling).toBeGreaterThan(-1)
    expect(drift).toBeGreaterThan(udvikling)
  })
})

describe('SkemaGrid — Registrér arbejdstid (interactive row)', () => {
  it('classifies ✓ / amber / over at the gate tolerance (R5: 2dp rounding, |Δ| < 0,005)', () => {
    const { container } = renderGrid({
      manualHours: new Map([
        ['2026-03-02', 7.4],
        ['2026-03-03', 7.4],
        ['2026-03-04', 7.4],
      ]),
      cellValues: new Map([
        ['DRIFT:2026-03-02', 7.398], // rounds to 7,4 → inside the gate tolerance → ✓
        ['DRIFT:2026-03-03', 5.4],   // under by 2,0 → amber number
        ['DRIFT:2026-03-04', 8.4],   // over by 1,0 → red "+1,0"
      ]),
      onOpenDay: vi.fn(),
    })
    const workRow = rowByLabel(container, 'Registrér arbejdstid')

    const balanced = dayCell(workRow, 2).querySelector('button') as HTMLElement
    expect(balanced.textContent).toBe('✓')
    expect(balanced.className).toContain('workBalanced')

    const under = dayCell(workRow, 3).querySelector('button') as HTMLElement
    expect(under.textContent).toBe('2,0')
    expect(under.className).toContain('workUnder')

    const over = dayCell(workRow, 4).querySelector('button') as HTMLElement
    expect(over.textContent).toBe('+1,0')
    expect(over.className).toContain('workOver')

    // A day with no work and no allocation renders empty (still clickable).
    const empty = dayCell(workRow, 5).querySelector('button') as HTMLElement
    expect(empty.textContent).toBe('')
  })

  it('fires onOpenDay with the dateKey when a day cell is clicked', () => {
    const onOpenDay = vi.fn()
    renderGrid({ onOpenDay })
    fireEvent.click(screen.getByRole('button', { name: 'Registrér arbejdstid 2026-03-02' }))
    expect(onOpenDay).toHaveBeenCalledTimes(1)
    expect(onOpenDay).toHaveBeenCalledWith('2026-03-02')
  })

  it('R12: in read-only mode the row renders as DATA — no buttons, no onOpenDay, label "Arbejdstid"', () => {
    const onOpenDay = vi.fn()
    const { container } = renderGrid({
      readOnly: true,
      onOpenDay,
      manualHours: new Map([['2026-03-02', 7.4]]),
      cellValues: new Map([['DRIFT:2026-03-02', 7.4]]),
    })
    const workRow = rowByLabel(container, 'Arbejdstid')
    expect(workRow.querySelectorAll('button').length).toBe(0)
    expect(dayCell(workRow, 2).textContent).toBe('✓') // still shows the data
    fireEvent.click(dayCell(workRow, 2))
    expect(onOpenDay).not.toHaveBeenCalled()
  })

  it('R11: date headers are NOT click targets — the trigger is the Arbejdstid cell only', () => {
    const onOpenDay = vi.fn()
    const { container } = renderGrid({ onOpenDay })
    expect(container.querySelectorAll('thead button').length).toBe(0)
    fireEvent.click(container.querySelectorAll('thead th')[2])
    expect(onOpenDay).not.toHaveBeenCalled()
  })

  it('the trailing Sum totals ONLY the amber (under-allocated) hours — over-allocated days never net against it', () => {
    const { container } = renderGrid({
      manualHours: new Map([
        ['2026-03-02', 7.4],
        ['2026-03-03', 7.4],
        ['2026-03-04', 7.4],
      ]),
      cellValues: new Map([
        ['DRIFT:2026-03-02', 5.4], // under 2,0
        ['DRIFT:2026-03-03', 6.4], // under 1,0
        ['DRIFT:2026-03-04', 8.4], // over 1,0 — must NOT subtract
      ]),
      onOpenDay: vi.fn(),
    })
    const total = sumCell(rowByLabel(container, 'Registrér arbejdstid'))
    expect(total.textContent).toBe('3,0')
    expect((total.querySelector('span') as HTMLElement).className).toContain('workUnder')
  })
})

describe('SkemaGrid — disclosure bands', () => {
  it('the Projekter band toggles its rows and aria-expanded', () => {
    const { container } = renderGrid()
    const band = screen.getByRole('button', { name: 'Projekter (2)' })
    expect(band.getAttribute('aria-expanded')).toBe('true')
    expect(band.textContent).toContain('▾')

    fireEvent.click(band)
    expect(band.getAttribute('aria-expanded')).toBe('false')
    expect(band.textContent).toContain('▸')
    const labels = Array.from(container.querySelectorAll('tbody tr'))
      .map((r) => r.querySelector('td')?.textContent)
    expect(labels).not.toContain('Drift')
    expect(labels).not.toContain('Udvikling')
    expect(labels).toContain('Ferie') // the other group is untouched

    fireEvent.click(band)
    expect(rowByLabel(container, 'Drift')).toBeTruthy()
  })

  it('the Ferie og fravær band toggles its rows independently', () => {
    const { container } = renderGrid()
    const band = screen.getByRole('button', { name: 'Ferie og fravær (1)' })
    fireEvent.click(band)
    const labels = Array.from(container.querySelectorAll('tbody tr'))
      .map((r) => r.querySelector('td')?.textContent)
    expect(labels).not.toContain('Ferie')
    expect(labels).toContain('Drift')
  })

  it('band counts reflect the VISIBLE rows under preferences', () => {
    renderGrid({ rowPreferences: prefsHideDrift })
    expect(screen.getByRole('button', { name: 'Projekter (1)' })).toBeTruthy()
    expect(screen.getByRole('button', { name: 'Ferie og fravær (1)' })).toBeTruthy()
  })
})

describe('SkemaGrid — weekend band + today (R8)', () => {
  it('applies the weekend class to the interactive/project/absence rows ONLY — Diff and I alt stay grey', () => {
    const { container } = renderGrid({ onOpenDay: vi.fn() })
    // Mar 1 2026 is a Sunday → td index 1.
    expect(dayCell(rowByLabel(container, 'Registrér arbejdstid'), 1).className).toContain('weekend')
    expect(dayCell(rowByLabel(container, 'Drift'), 1).className).toContain('weekend')
    expect(dayCell(rowByLabel(container, 'Ferie'), 1).className).toContain('weekend')
    expect(dayCell(rowByLabel(container, 'Diff. fra normtid'), 1).className).not.toContain('weekend')
    expect(dayCell(rowByLabel(container, 'I alt'), 1).className).not.toContain('weekend')
    // A weekday column carries no weekend class anywhere.
    expect(dayCell(rowByLabel(container, 'Drift'), 2).className).not.toContain('weekend')
    // Headers carry the weekend marker (abbrev tint), Saturdays included.
    const headers = container.querySelectorAll('thead th')
    expect(headers[1].className).toContain('weekend')
    expect(headers[7].className).toContain('weekend')
    expect(headers[2].className).not.toContain('weekend')
  })

  it("highlights today's day number in the header", () => {
    vi.useFakeTimers()
    vi.setSystemTime(new Date('2026-03-11T09:00:00'))
    const { container } = renderGrid()
    const headers = container.querySelectorAll('thead th')
    expect(headers[11].className).toContain('today')
    expect(headers[12].className).not.toContain('today')
  })
})

describe('SkemaGrid — editable cells', () => {
  it('shows the RAW typed text while focused (the decimal comma is not stripped mid-typing)', () => {
    const onCellChange = vi.fn()
    renderGrid({ onCellChange })
    const input = screen.getByLabelText('Drift dag 2') as HTMLInputElement
    fireEvent.focus(input)
    fireEvent.change(input, { target: { value: '7,' } })
    expect(input.value).toBe('7,') // raw text preserved
    expect(onCellChange).toHaveBeenCalledWith('DRIFT', '2026-03-02', 7)
  })

  it('formats to 1 decimal with a Danish comma on blur', () => {
    // Stateful harness: the parent applies onCellChange so blur re-reads cellValues.
    function Harness() {
      const [cells, setCells] = useState(new Map<string, number>())
      return (
        <SkemaGrid
          year={2026}
          month={3}
          rows={mockRows}
          cellValues={cells}
          readOnly={false}
          onCellChange={(rowKey, date, hours) => {
            setCells((prev) => {
              const next = new Map(prev)
              const k = `${rowKey}:${date}`
              if (hours === null) next.delete(k)
              else next.set(k, hours)
              return next
            })
          }}
        />
      )
    }
    render(<Harness />)
    const input = screen.getByLabelText('Drift dag 2') as HTMLInputElement
    fireEvent.focus(input)
    fireEvent.change(input, { target: { value: '7,456' } })
    expect(input.value).toBe('7,456') // raw while focused
    fireEvent.blur(input)
    expect(input.value).toBe('7,5') // 1-decimal Danish format
  })

  it('keeps the shipped propagation semantics: "" → null, "0" → null (R17 recorded limitation — clearing never persists server-side), unparsable → no call', () => {
    const onCellChange = vi.fn()
    renderGrid({ onCellChange })
    const input = screen.getByLabelText('Drift dag 2') as HTMLInputElement

    fireEvent.change(input, { target: { value: '0' } })
    expect(onCellChange).toHaveBeenLastCalledWith('DRIFT', '2026-03-02', null)

    fireEvent.change(input, { target: { value: '' } })
    expect(onCellChange).toHaveBeenLastCalledWith('DRIFT', '2026-03-02', null)

    onCellChange.mockClear()
    fireEvent.change(input, { target: { value: 'abc' } })
    expect(onCellChange).not.toHaveBeenCalled()
  })

  // ── ADR-032 D3 prefill — UNCHANGED behavior (R1) ──
  it('prefills an EMPTY absence cell with the day\'s SERVED norm on first focus and shows it as editable raw text', () => {
    const onCellChange = vi.fn()
    renderGrid({
      onCellChange,
      dailyNorm: new Map([['2026-03-02', 7.4]]),
    })
    const input = screen.getByLabelText('Ferie dag 2') as HTMLInputElement
    fireEvent.focus(input)
    expect(onCellChange).toHaveBeenCalledWith('VACATION', '2026-03-02', 7.4)
    expect(input.value).toBe('7,4') // visible immediately while focused, fully editable
  })

  it('does NOT overwrite an existing absence value on focus (prefill-once)', () => {
    const onCellChange = vi.fn()
    renderGrid({
      onCellChange,
      dailyNorm: new Map([['2026-03-02', 7.4]]),
      cellValues: new Map([['VACATION:2026-03-02', 3.7]]),
    })
    fireEvent.focus(screen.getByLabelText('Ferie dag 2'))
    expect(onCellChange).not.toHaveBeenCalled()
  })

  it('does NOT prefill on zero-norm, null-norm, or missing-norm days', () => {
    const onCellChange = vi.fn()
    renderGrid({
      onCellChange,
      dailyNorm: new Map<string, number | null>([
        ['2026-03-01', 0], // weekend
        ['2026-03-07', null], // ANNUAL_ACTIVITY
        // 2026-03-02 deliberately absent
      ]),
    })
    fireEvent.focus(screen.getByLabelText('Ferie dag 1'))
    fireEvent.focus(screen.getByLabelText('Ferie dag 7'))
    fireEvent.focus(screen.getByLabelText('Ferie dag 2'))
    expect(onCellChange).not.toHaveBeenCalled()
  })

  it('flashes the recently-changed cell on blur (success-light → transparent class)', () => {
    const { container } = renderGrid()
    const input = screen.getByLabelText('Drift dag 2') as HTMLInputElement
    fireEvent.focus(input)
    fireEvent.change(input, { target: { value: '5' } })
    fireEvent.blur(input)
    expect(dayCell(rowByLabel(container, 'Drift'), 2).className).toContain('recent')
  })

  it('does not flash on blur when the value did not change', () => {
    const { container } = renderGrid({
      cellValues: new Map([['DRIFT:2026-03-02', 5]]),
    })
    const input = screen.getByLabelText('Drift dag 2') as HTMLInputElement
    fireEvent.focus(input)
    fireEvent.blur(input)
    expect(dayCell(rowByLabel(container, 'Drift'), 2).className).not.toContain('recent')
  })

  it('respects prefers-reduced-motion: no flash class when reduce is set', () => {
    vi.stubGlobal(
      'matchMedia',
      vi.fn().mockReturnValue({ matches: true } as unknown as MediaQueryList)
    )
    const { container } = renderGrid()
    const input = screen.getByLabelText('Drift dag 2') as HTMLInputElement
    fireEvent.focus(input)
    fireEvent.change(input, { target: { value: '5' } })
    fireEvent.blur(input)
    expect(dayCell(rowByLabel(container, 'Drift'), 2).className).not.toContain('recent')
  })

  it('cell inputs are borderless decimal text inputs (inputMode="decimal")', () => {
    const { container } = renderGrid()
    const input = screen.getByLabelText('Drift dag 2') as HTMLInputElement
    expect(input.getAttribute('type')).toBe('text')
    expect(input.getAttribute('inputmode')).toBe('decimal')
    expect(input.className).toContain('cellInput')
    expect(container.querySelectorAll('input[type="number"]').length).toBe(0)
  })
})

// ── S73 / TASK-7302 — the full-day snap (R5) + the served-flag note ──
describe('SkemaGrid — full-day snap (S73 R5)', () => {
  // CARE_DAY is the served full-day-only row; VACATION (ferie) stays hours-based.
  const fullDayRows: SkemaRow[] = [
    { type: 'project', key: 'DRIFT', label: 'Drift' },
    { type: 'absence', key: 'VACATION', label: 'Ferie' },
    { type: 'absence', key: 'CARE_DAY', label: 'Omsorgsdage', fullDayOnly: true },
  ]

  /** Stateful harness so the snap's onCellChange round-trips into cellValues. */
  function FullDayHarness({
    consumptionBasis,
    dailyNorm,
    initial,
  }: {
    consumptionBasis?: Map<string, number | null>
    dailyNorm?: Map<string, number | null>
    initial?: Map<string, number>
  }) {
    const [cells, setCells] = useState(initial ?? new Map<string, number>())
    return (
      <SkemaGrid
        year={2026}
        month={3}
        rows={fullDayRows}
        cellValues={cells}
        readOnly={false}
        consumptionBasis={consumptionBasis}
        dailyNorm={dailyNorm}
        onCellChange={(rowKey, date, hours) => {
          setCells((prev) => {
            const next = new Map(prev)
            const k = `${rowKey}:${date}`
            if (hours === null) next.delete(k)
            else next.set(k, hours)
            return next
          })
        }}
      />
    )
  }

  it('R5: an entry in a full-day cell SNAPS to the served consumption basis on commit (blur)', () => {
    render(
      <FullDayHarness
        consumptionBasis={new Map([['2026-03-02', 7.4]])}
        dailyNorm={new Map([['2026-03-02', 7.4]])}
      />,
    )
    const input = screen.getByLabelText('Omsorgsdage dag 2') as HTMLInputElement
    fireEvent.focus(input)
    // Type a PARTIAL value the user shouldn't be able to keep on a full-day type
    fireEvent.change(input, { target: { value: '3' } })
    fireEvent.blur(input)
    // The committed value snapped to the day's basis (7,4) — not the typed 3.
    expect(input.value).toBe('7,4')
  })

  it('R5 academic case: a null DISPLAY norm but a served basis (the ANNUAL_ACTIVITY 7.4×fraction fallback) STILL snaps', () => {
    render(
      <FullDayHarness
        // The academic surface: dailyNorm null (renders blank) but the basis is
        // served (the deliberate 7,4×fraction fallback) → the snap still fires.
        dailyNorm={new Map<string, number | null>([['2026-03-02', null]])}
        consumptionBasis={new Map([['2026-03-02', 3.7]])}
      />,
    )
    const input = screen.getByLabelText('Omsorgsdage dag 2') as HTMLInputElement
    fireEvent.focus(input) // no prefill (null norm), as ADR-032 D3 specifies
    fireEvent.change(input, { target: { value: '1' } })
    fireEvent.blur(input)
    expect(input.value).toBe('3,7') // snapped to the academic basis
  })

  it('R5 null-basis: no dated profile covers the day → NO snap, NO invented value — the typed entry STANDS locally', () => {
    render(
      <FullDayHarness
        // basis explicitly null for the day → the server fail-closes via anchor-422
        consumptionBasis={new Map<string, number | null>([['2026-03-02', null]])}
      />,
    )
    const input = screen.getByLabelText('Omsorgsdage dag 2') as HTMLInputElement
    fireEvent.focus(input)
    fireEvent.change(input, { target: { value: '3' } })
    fireEvent.blur(input)
    // The typed value stands — no snap, no invented basis value.
    expect(input.value).toBe('3')
  })

  it('R5 blank-stays-blank: clearing a full-day cell on commit leaves it blank (no snap to basis)', () => {
    render(
      <FullDayHarness
        consumptionBasis={new Map([['2026-03-02', 7.4]])}
        initial={new Map([['CARE_DAY:2026-03-02', 7.4]])}
      />,
    )
    const input = screen.getByLabelText('Omsorgsdage dag 2') as HTMLInputElement
    fireEvent.focus(input)
    fireEvent.change(input, { target: { value: '' } }) // clear
    fireEvent.blur(input)
    expect(input.value).toBe('') // blank, NOT re-snapped to 7,4
  })

  it('R5 missing-basis-map: a full-day cell with no served basis does NOT snap (the typed value stands)', () => {
    render(<FullDayHarness />) // no consumptionBasis prop
    const input = screen.getByLabelText('Omsorgsdage dag 2') as HTMLInputElement
    fireEvent.focus(input)
    fireEvent.change(input, { target: { value: '3' } })
    fireEvent.blur(input)
    expect(input.value).toBe('3')
  })

  it('R5: a NON-full-day (ferie) cell does NOT snap — a partial below-norm value commits unchanged (ferie behavior UNCHANGED)', () => {
    render(
      <FullDayHarness
        consumptionBasis={new Map([['2026-03-02', 7.4]])}
        dailyNorm={new Map([['2026-03-02', 7.4]])}
      />,
    )
    const input = screen.getByLabelText('Ferie dag 2') as HTMLInputElement
    fireEvent.focus(input) // ADR-032 D3 prefill seeds 7,4…
    fireEvent.change(input, { target: { value: '3,7' } }) // …user edits to a partial day
    fireEvent.blur(input)
    expect(input.value).toBe('3,7') // ferie keeps the partial value — no snap
  })

  it('R5: the non-full-day ADR-032 D3 prefill is UNCHANGED on the ferie row even with a basis served', () => {
    const onCellChange = vi.fn()
    render(
      <SkemaGrid
        year={2026}
        month={3}
        rows={fullDayRows}
        cellValues={new Map()}
        readOnly={false}
        onCellChange={onCellChange}
        consumptionBasis={new Map([['2026-03-02', 7.4]])}
        dailyNorm={new Map([['2026-03-02', 7.4]])}
      />,
    )
    fireEvent.focus(screen.getByLabelText('Ferie dag 2'))
    // Prefill still seeds the served NORM (not the basis path) on first focus.
    expect(onCellChange).toHaveBeenCalledWith('VACATION', '2026-03-02', 7.4)
  })

  it('R5: the "hele dage" note renders from the SERVED fullDayOnly flag (not a hardcoded type list)', () => {
    const { container } = renderGrid({ rows: fullDayRows })
    const careRow = rowByLabel(container, 'Omsorgsdage')
    const note = careRow.querySelector('td span')
    expect(note?.className).toContain('rowNote')
    expect(note?.textContent).toBe('hele dage')
    // The hours-based ferie row carries NO note.
    const ferieRow = rowByLabel(container, 'Ferie')
    expect(ferieRow.querySelector('td span')).toBeNull()
  })
})

describe('SkemaGrid — read-only / review mode (R12)', () => {
  it('renders NO inputs in read-only mode; values render as formatted text', () => {
    const { container } = renderGrid({
      readOnly: true,
      cellValues: new Map([['DRIFT:2026-03-02', 7.4]]),
    })
    expect(container.querySelectorAll('input').length).toBe(0)
    expect(dayCell(rowByLabel(container, 'Drift'), 2).textContent).toBe('7,4')
  })

  it('R12 fallback pin: without rowPreferences ALL served rows render (the approval surface\'s leader-sees-the-full-record contract)', () => {
    const { container } = renderGrid({
      readOnly: true,
      cellValues: new Map([['DRIFT:2026-03-02', 7.4]]),
    })
    const labels = Array.from(container.querySelectorAll('tbody tr'))
      .map((r) => r.querySelector('td')?.textContent)
    expect(labels).toContain('Drift')
    expect(labels).toContain('Udvikling')
    expect(labels).toContain('Ferie')
    expect(container.textContent).not.toContain('skjulte rækker')
  })

  it('does NOT prefill absence cells in read-only mode (no inputs exist to focus)', () => {
    const onCellChange = vi.fn()
    const { container } = renderGrid({
      readOnly: true,
      onCellChange,
      dailyNorm: new Map([['2026-03-02', 7.4]]),
    })
    const cell = dayCell(rowByLabel(container, 'Ferie'), 2)
    expect(cell.querySelector('input')).toBeNull()
    fireEvent.focus(cell)
    expect(onCellChange).not.toHaveBeenCalled()
  })
})

describe('SkemaGrid — scale', () => {
  it('renders 25 projects without error and with the correct row count', () => {
    const manyRows: SkemaRow[] = [
      ...Array.from({ length: 25 }, (_, i) => ({
        type: 'project' as const,
        key: `PRJ-${i + 1}`,
        label: `Projekt ${i + 1}`,
      })),
      { type: 'absence', key: 'VACATION', label: 'Ferie' },
    ]
    const { container } = renderGrid({ rows: manyRows })
    // diff + work + divider + band + 25 projects + band + 1 absence + total = 32
    expect(container.querySelectorAll('tbody tr').length).toBe(32)
    expect(screen.getByRole('button', { name: 'Projekter (25)' })).toBeTruthy()
  })
})

describe('SkemaGrid — keyboard operability (R15)', () => {
  it('the first Tab stop is the first interactive day cell (everything before it is read-only)', async () => {
    const user = userEvent.setup()
    renderGrid({ onOpenDay: vi.fn() })
    await user.tab()
    expect(document.activeElement?.getAttribute('aria-label')).toBe(
      'Registrér arbejdstid 2026-03-01'
    )
  })

  it('Enter and Space on a focused day cell fire onOpenDay', async () => {
    const user = userEvent.setup()
    const onOpenDay = vi.fn()
    renderGrid({ onOpenDay })
    const button = screen.getByRole('button', { name: 'Registrér arbejdstid 2026-03-02' })
    button.focus()
    await user.keyboard('{Enter}')
    expect(onOpenDay).toHaveBeenCalledWith('2026-03-02')
    await user.keyboard(' ')
    expect(onOpenDay).toHaveBeenCalledTimes(2)
  })

  it('Enter on a focused disclosure band toggles its rows', async () => {
    const user = userEvent.setup()
    const { container } = renderGrid()
    const band = screen.getByRole('button', { name: 'Projekter (2)' })
    band.focus()
    await user.keyboard('{Enter}')
    const labels = Array.from(container.querySelectorAll('tbody tr'))
      .map((r) => r.querySelector('td')?.textContent)
    expect(labels).not.toContain('Drift')
  })

  it('cell inputs are keyboard-reachable and carry the focus-treatment class', async () => {
    const user = userEvent.setup()
    renderGrid() // no onOpenDay → first tabbable is the Projekter band, then the first cell input
    await user.tab()
    expect((document.activeElement as HTMLElement).className).toContain('bandButton')
    await user.tab()
    const active = document.activeElement as HTMLElement
    expect(active.tagName).toBe('INPUT')
    expect(active.className).toContain('cellInput')
  })

  it('every interactive element carries its focus-visible style hook class', () => {
    const onOpenManager = vi.fn()
    renderGrid({
      onOpenDay: vi.fn(),
      onOpenManager,
      cellValues: new Map([['DRIFT:2026-03-02', 4]]),
      rowPreferences: {
        configured: true,
        projects: [], // hides the populated DRIFT → the affordance renders
        absenceTypes: [{ type: 'VACATION', label: 'Ferie', sortOrder: 0 }],
      },
    })
    expect(
      screen.getByRole('button', { name: 'Registrér arbejdstid 2026-03-02' }).className
    ).toContain('workButton')
    expect(screen.getByRole('button', { name: 'Projekter (0)' }).className).toContain(
      'bandButton'
    )
    expect(
      screen.getByRole('button', { name: '1 skjulte rækker har timer i denne måned' }).className
    ).toContain('hiddenNoteButton')
    expect((screen.getByLabelText('Ferie dag 2') as HTMLInputElement).className).toContain(
      'cellInput'
    )
  })
})
