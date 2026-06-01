import { render, fireEvent } from '@testing-library/react'
import { SkemaGrid } from '../SkemaGrid'
import type { SkemaRow } from '../../types'

const mockRows: SkemaRow[] = [
  { type: 'project', key: 'DRIFT', label: 'Drift' },
  { type: 'project', key: 'PROJ-2', label: 'Udvikling' },
  { type: 'absence', key: 'VACATION', label: 'Ferie' },
]

describe('SkemaGrid', () => {
  it('renders correct number of day columns for March 2026', () => {
    const { container } = render(
      <SkemaGrid
        year={2026}
        month={3}
        rows={mockRows}
        cellValues={new Map()}
        readOnly={false}
        onCellChange={vi.fn()}
      />
    )

    // March 2026 has 31 days
    // Header row: 1 label column + 31 day columns + 1 sum column = 33 th elements
    const headerCells = container.querySelectorAll('thead th')
    expect(headerCells.length).toBe(33)
  })

  it('applies weekend CSS class to Saturday and Sunday columns', () => {
    const { container } = render(
      <SkemaGrid
        year={2026}
        month={3}
        rows={mockRows}
        cellValues={new Map()}
        readOnly={false}
        onCellChange={vi.fn()}
      />
    )

    // March 2026: 1st is Sunday, 7th is Saturday, 8th is Sunday
    // Day headers should have weekend class
    const dayHeaders = container.querySelectorAll('thead th')
    // Index 0 is the label header, indices 1-31 are days, index 32 is sum
    // March 1 (Sunday) is at index 1
    expect(dayHeaders[1].className).toContain('weekend')
    // March 7 (Saturday) is at index 7
    expect(dayHeaders[7].className).toContain('weekend')
    // March 2 (Monday) is at index 2 — should not have weekend class
    expect(dayHeaders[2].className).not.toContain('weekend')
  })

  it('calls onCellChange when cell input changes', () => {
    const onCellChange = vi.fn()

    const { container } = render(
      <SkemaGrid
        year={2026}
        month={3}
        rows={mockRows}
        cellValues={new Map()}
        readOnly={false}
        onCellChange={onCellChange}
      />
    )

    // Find the first number input (project row, first day)
    const inputs = container.querySelectorAll('input[type="number"]')
    expect(inputs.length).toBeGreaterThan(0)

    fireEvent.change(inputs[0], { target: { value: '7.4' } })
    expect(onCellChange).toHaveBeenCalledTimes(1)
    // onCellChange(rowKey, date, hours)
    expect(onCellChange).toHaveBeenCalledWith('DRIFT', '2026-03-01', 7.4)
  })

  it('disables inputs when readOnly is true', () => {
    const { container } = render(
      <SkemaGrid
        year={2026}
        month={3}
        rows={mockRows}
        cellValues={new Map()}
        readOnly={true}
        onCellChange={vi.fn()}
      />
    )

    // In readOnly mode, inputs are replaced by span elements
    const inputs = container.querySelectorAll('input[type="number"]')
    expect(inputs.length).toBe(0)

    // Instead, span elements with cellDisplay class should be present
    const displays = container.querySelectorAll('span')
    expect(displays.length).toBeGreaterThan(0)
  })

  // ── S56 Step 7a fix: "Ikke fordelt" MONTH-TOTAL must be per-day, not netted ──
  // The month-total cell is green (✓ / .allocBalanced) ONLY when every gated day
  // is individually balanced (allDaysBalanced via classifyAllocation), NOT when
  // workedSumMonth nets against allocatedSumMonth. A month with one under-allocated
  // day (+X) and one over-allocated day (−X) nets to 0 but MUST NOT show green,
  // because the backend approval gate fails BOTH days.
  //
  // We drive `worked` per day via `manualHours` and `allocated` per day via project
  // cellValues. The "Ikke fordelt" row only renders when manualHours (or
  // workIntervals) is supplied; its last <td> is the month-total cell.
  const monthTotalCell = (container: HTMLElement): HTMLElement => {
    const unallocRow = container.querySelector('tr.unallocatedRow')
    expect(unallocRow).not.toBeNull()
    const cells = unallocRow!.querySelectorAll('td')
    // [0] = "Ikke fordelt" label, [1..31] = days, last = month-total sum cell.
    return cells[cells.length - 1] as HTMLElement
  }

  it('month-total is NOT green when days net to zero but are individually unbalanced', () => {
    // Day 2 (Mon): worked 7.4, allocated 4.4 → under by +3.0
    // Day 3 (Tue): worked 7.4, allocated 10.4 → over by −3.0
    // Net month: worked 14.8 vs allocated 14.8 → 0. Old (netting) logic = green ✓.
    // New (per-day) logic = unbalanced (both days fail the gate).
    const cellValues = new Map<string, number>([
      ['DRIFT:2026-03-02', 4.4],
      ['DRIFT:2026-03-03', 10.4],
    ])
    const manualHours = new Map<string, number>([
      ['2026-03-02', 7.4],
      ['2026-03-03', 7.4],
    ])

    const { container } = render(
      <SkemaGrid
        year={2026}
        month={3}
        rows={mockRows}
        cellValues={cellValues}
        readOnly={true}
        onCellChange={vi.fn()}
        manualHours={manualHours}
      />
    )

    const cell = monthTotalCell(container)
    // Must NOT be the green/balanced cell despite net-zero.
    expect(cell.className).not.toContain('allocBalanced')
    expect(cell.className).toContain('allocUnbalanced')
    // Must NOT render the ✓ checkmark — renders the (net) signed unallocated
    // hours string instead (here net is 0). The point is it is NOT the green ✓.
    expect(cell.textContent).not.toContain('✓')
    expect(cell.textContent).toContain('t') // an hours readout, not the checkmark
    // Carries the blocked-approval tooltip.
    expect(cell.getAttribute('title')).toBeTruthy()
  })

  it('month-total IS green when every gated day is individually balanced', () => {
    // Both days worked 7.4 and allocated 7.4 → each balanced → month green ✓.
    const cellValues = new Map<string, number>([
      ['DRIFT:2026-03-02', 7.4],
      ['DRIFT:2026-03-03', 7.4],
    ])
    const manualHours = new Map<string, number>([
      ['2026-03-02', 7.4],
      ['2026-03-03', 7.4],
    ])

    const { container } = render(
      <SkemaGrid
        year={2026}
        month={3}
        rows={mockRows}
        cellValues={cellValues}
        readOnly={true}
        onCellChange={vi.fn()}
        manualHours={manualHours}
      />
    )

    const cell = monthTotalCell(container)
    expect(cell.className).toContain('allocBalanced')
    expect(cell.className).not.toContain('allocUnbalanced')
    expect(cell.textContent).toContain('✓')
    // No blocked tooltip when balanced.
    expect(cell.getAttribute('title')).toBeNull()
  })

  // ── Per-day "Diff. fra normtid" / "Ikke fordelt" on every norm day ──
  // A day that HAS a norm but no registered work time must surface its full
  // -norm shortfall (Diff) and a balanced ✓ (Ikke fordelt), instead of blank.
  // cells: [0] = row label, [1..N] = days, last = month-total/sum cell.
  const dayCell = (container: HTMLElement, rowSelector: string, dayOfMonth: number): HTMLElement => {
    const row = container.querySelector(rowSelector)
    expect(row).not.toBeNull()
    const cells = row!.querySelectorAll('td')
    return cells[dayOfMonth] as HTMLElement // [1] = day 1, [2] = day 2, ...
  }

  it('Diff. fra normtid shows -norm on a workday with a norm but no registered work time', () => {
    // March 2 2026 is a Monday (workday). Norm 7,4 t, nothing worked → -7,4 t.
    const dailyNorm = new Map<string, number | null>([['2026-03-02', 7.4]])

    const { container } = render(
      <SkemaGrid
        year={2026}
        month={3}
        rows={mockRows}
        cellValues={new Map()}
        readOnly={true}
        onCellChange={vi.fn()}
        dailyNorm={dailyNorm}
      />
    )

    // formatDiff(-7.4) === '-7t 24m'
    expect(dayCell(container, 'tr.diffRow', 2).textContent).toContain('-7t 24m')
    // A day with no norm entry stays blank (day 3 not in the map).
    expect(dayCell(container, 'tr.diffRow', 3).textContent?.trim()).toBe('')
  })

  it('Ikke fordelt shows balanced ✓ on a norm day with no work and no allocation', () => {
    const dailyNorm = new Map<string, number | null>([['2026-03-02', 7.4]])

    const { container } = render(
      <SkemaGrid
        year={2026}
        month={3}
        rows={mockRows}
        cellValues={new Map()}
        readOnly={true}
        onCellChange={vi.fn()}
        manualHours={new Map()}
        dailyNorm={dailyNorm}
      />
    )

    const cell = dayCell(container, 'tr.unallocatedRow', 2)
    expect(cell.textContent).toContain('✓')
    expect(cell.className).toContain('allocBalanced')
    // A day with no norm and no activity stays blank (day 3).
    expect(dayCell(container, 'tr.unallocatedRow', 3).textContent?.trim()).toBe('')
  })
})
