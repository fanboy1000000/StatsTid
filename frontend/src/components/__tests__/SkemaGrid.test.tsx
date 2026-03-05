import { render, screen, fireEvent } from '@testing-library/react'
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
})
