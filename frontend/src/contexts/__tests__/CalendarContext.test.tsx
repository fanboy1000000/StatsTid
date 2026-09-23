// S143 / TASK-14301 — pins for `useCalendarToday` and its test seam
// (`src/test/renderWithCalendar.tsx`), independent of the network/timer machinery in
// `useCalendarBootstrap`. This is the contract the FOUR sibling screen-migration tasks build on.
import { describe, it, expect } from 'vitest'
import { renderHook } from '@testing-library/react'
import { screen } from '@testing-library/react'
import { useCalendarToday } from '../CalendarContext'
import { renderWithCalendar, CalendarTestProvider } from '../../test/renderWithCalendar'

function Probe() {
  return <div data-testid="today">{useCalendarToday()}</div>
}

describe('useCalendarToday', () => {
  it('throws when read outside a mounted provider — a missing wrapper must fail loudly, not silently return undefined', () => {
    expect(() => renderHook(() => useCalendarToday())).toThrow(/useCalendarToday must be used within/)
  })

  it('returns the value supplied by a wrapping CalendarContext.Provider', () => {
    const { result } = renderHook(() => useCalendarToday(), {
      wrapper: ({ children }) => <CalendarTestProvider today="2026-06-01">{children}</CalendarTestProvider>,
    })
    expect(result.current).toBe('2026-06-01')
  })
})

describe('renderWithCalendar (the shared test helper for sibling screen migrations)', () => {
  it('renders the given UI with `today` fixed to the hand-written literal passed in', () => {
    renderWithCalendar('2025-12-24', <Probe />)
    expect(screen.getByTestId('today')).toHaveTextContent('2025-12-24')
  })

  it('a DIFFERENT literal produces a DIFFERENT rendered value (the fixture is not a hardcoded passthrough)', () => {
    renderWithCalendar('2027-01-01', <Probe />)
    expect(screen.getByTestId('today')).toHaveTextContent('2027-01-01')
  })
})
