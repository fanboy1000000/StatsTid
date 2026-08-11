// S128 / TASK-12803 — the submit-error surface (both arms, PAT-016 discipline).
// Before this sprint the form's try/finally had NO catch: a server refusal from
// `registerEntry` (which throws the raw error body) became an unhandled rejection
// and the user saw NOTHING. The load-bearing case is the NEW period-locked 409
// (ApprovalPeriodSaveLock): body `{ "error": "Cannot save entries for a period
// with status EMPLOYEE_APPROVED" }` thrown as raw JSON text — the form must parse
// out the message. Both arms pinned: refusal renders the alert with the PARSED
// message; success renders no alert and resets the hours field.
import { describe, it, expect, vi } from 'vitest'
import { render, screen, fireEvent, waitFor } from '@testing-library/react'
import { TimeEntryForm } from '../TimeEntryForm'

function fillRequiredAndSubmit() {
  fireEvent.change(screen.getByLabelText(/Dato/), { target: { value: '2026-08-03' } })
  fireEvent.change(screen.getByLabelText(/Timer/), { target: { value: '7.4' } })
  fireEvent.click(screen.getByRole('button', { name: /Registrer tid/ }))
}

describe('TimeEntryForm — submit refusal surface (S128 / TASK-12803)', () => {
  it('409-refusal arm: a thrown JSON error body renders the alert with the PARSED message', async () => {
    const serverBody = JSON.stringify({
      error: 'Cannot save entries for a period with status EMPLOYEE_APPROVED',
    })
    const onSubmit = vi.fn().mockRejectedValue(new Error(serverBody))
    render(<TimeEntryForm employeeId="emp001" onSubmit={onSubmit} />)

    fillRequiredAndSubmit()

    const alert = await screen.findByTestId('time-entry-error')
    expect(alert).toHaveTextContent(
      'Cannot save entries for a period with status EMPLOYEE_APPROVED',
    )
    // Parsed, not the raw JSON envelope.
    expect(alert.textContent).not.toContain('{')
    expect(alert.getAttribute('role')).toBe('alert')
  })

  it('non-JSON refusal falls back to the raw message', async () => {
    const onSubmit = vi.fn().mockRejectedValue(new Error('Unauthorized'))
    render(<TimeEntryForm employeeId="emp001" onSubmit={onSubmit} />)

    fillRequiredAndSubmit()

    expect(await screen.findByTestId('time-entry-error')).toHaveTextContent('Unauthorized')
  })

  it('success arm: no alert, fields reset, and a later success CLEARS a prior refusal', async () => {
    const onSubmit = vi.fn()
      .mockRejectedValueOnce(new Error(JSON.stringify({ error: 'refused once' })))
      .mockResolvedValueOnce(undefined)
    render(<TimeEntryForm employeeId="emp001" onSubmit={onSubmit} />)

    fillRequiredAndSubmit()
    await screen.findByTestId('time-entry-error')

    fireEvent.change(screen.getByLabelText(/Timer/), { target: { value: '5' } })
    fireEvent.click(screen.getByRole('button', { name: /Registrer tid/ }))

    await waitFor(() =>
      expect(screen.queryByTestId('time-entry-error')).not.toBeInTheDocument(),
    )
    // The success path still resets the hours field to the default.
    expect((screen.getByLabelText(/Timer/) as HTMLInputElement).value).toBe('7.4')
  })
})
