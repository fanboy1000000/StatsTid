// Smoke tests for the Monday-only / past-or-today-only date picker (S21 / TASK-2109).
// Basic functional coverage only — Phase 5 owns calendar UX polish.
import { render, screen, fireEvent } from '@testing-library/react'
import { describe, it, expect, vi } from 'vitest'
import { MondayDatePicker } from '../MondayDatePicker'

describe('MondayDatePicker', () => {
  it('rejects a non-Monday date when mondayOnly is true', () => {
    const onChange = vi.fn()
    render(
      <MondayDatePicker
        id="dp"
        value="2026-04-27"  // Monday
        onChange={onChange}
        mondayOnly={true}
        pastOrTodayOnly={false}
      />,
    )
    const input = screen.getByDisplayValue('2026-04-27') as HTMLInputElement
    // Try a Tuesday — must NOT propagate.
    fireEvent.change(input, { target: { value: '2026-04-28' } })
    expect(onChange).not.toHaveBeenCalled()
    // The warning text appears.
    expect(screen.getByRole('alert').textContent).toMatch(/mandag/i)
  })

  it('accepts a Monday date when mondayOnly is true', () => {
    const onChange = vi.fn()
    render(
      <MondayDatePicker
        id="dp"
        value=""
        onChange={onChange}
        mondayOnly={true}
        pastOrTodayOnly={false}
      />,
    )
    const dateInput = document.querySelector('input[type="date"]') as HTMLInputElement
    fireEvent.change(dateInput, { target: { value: '2026-04-27' } })  // Monday (UTC)
    expect(onChange).toHaveBeenCalledWith('2026-04-27')
  })

  it('rejects a future date when pastOrTodayOnly is true', () => {
    const onChange = vi.fn()
    render(
      <MondayDatePicker
        id="dp"
        value=""
        onChange={onChange}
        mondayOnly={false}
        pastOrTodayOnly={true}
      />,
    )
    const dateInput = document.querySelector('input[type="date"]') as HTMLInputElement
    // Year 9999 is unambiguously future.
    fireEvent.change(dateInput, { target: { value: '9999-01-01' } })
    expect(onChange).not.toHaveBeenCalled()
    expect(screen.getByRole('alert').textContent).toMatch(/fremtiden/i)
  })

  it('passes through past dates when pastOrTodayOnly is true', () => {
    const onChange = vi.fn()
    render(
      <MondayDatePicker
        id="dp"
        value=""
        onChange={onChange}
        mondayOnly={false}
        pastOrTodayOnly={true}
      />,
    )
    const dateInput = document.querySelector('input[type="date"]') as HTMLInputElement
    fireEvent.change(dateInput, { target: { value: '2020-01-15' } })
    expect(onChange).toHaveBeenCalledWith('2020-01-15')
  })
})
