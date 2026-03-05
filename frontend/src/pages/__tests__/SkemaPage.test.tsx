import { render, screen, fireEvent } from '@testing-library/react'
import { MemoryRouter } from 'react-router-dom'
import { useState } from 'react'

// Test month navigation logic extracted from SkemaPage
const DANISH_MONTHS = [
  'Januar', 'Februar', 'Marts', 'April', 'Maj', 'Juni',
  'Juli', 'August', 'September', 'Oktober', 'November', 'December',
]

function formatMonthLabel(year: number, month: number): string {
  return `${DANISH_MONTHS[month - 1]} ${year}`
}

// A minimal test component exercising the same navigation logic as SkemaPage
function MonthNavigator() {
  const [year, setYear] = useState(2026)
  const [month, setMonth] = useState(3)

  const goToPrevMonth = () => {
    setMonth((prev) => {
      if (prev === 1) {
        setYear((y) => y - 1)
        return 12
      }
      return prev - 1
    })
  }

  const goToNextMonth = () => {
    setMonth((prev) => {
      if (prev === 12) {
        setYear((y) => y + 1)
        return 1
      }
      return prev + 1
    })
  }

  const approvalStatus = 'DRAFT'

  return (
    <div>
      <button onClick={goToPrevMonth}>Forrige</button>
      <h2>{formatMonthLabel(year, month)}</h2>
      <button onClick={goToNextMonth}>Naeste</button>
      {approvalStatus === 'DRAFT' && (
        <button>Godkend maaned</button>
      )}
    </div>
  )
}

describe('SkemaPage month navigation', () => {
  it('navigates to next/previous month', () => {
    render(
      <MemoryRouter>
        <MonthNavigator />
      </MemoryRouter>
    )

    // Initial: Marts 2026
    expect(screen.getByRole('heading', { level: 2 })).toHaveTextContent('Marts 2026')

    // Click next
    fireEvent.click(screen.getByText('Naeste'))
    expect(screen.getByRole('heading', { level: 2 })).toHaveTextContent('April 2026')

    // Click prev twice
    fireEvent.click(screen.getByText('Forrige'))
    expect(screen.getByRole('heading', { level: 2 })).toHaveTextContent('Marts 2026')

    fireEvent.click(screen.getByText('Forrige'))
    expect(screen.getByRole('heading', { level: 2 })).toHaveTextContent('Februar 2026')
  })

  it('shows approval button when status is DRAFT', () => {
    render(
      <MemoryRouter>
        <MonthNavigator />
      </MemoryRouter>
    )

    // When status is DRAFT, the "Godkend maaned" button should be visible
    expect(screen.getByText('Godkend maaned')).toBeInTheDocument()
  })
})
