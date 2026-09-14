// SPRINT-141 / TASK-14108 — the termination screen.
//
// Covers: the terminated-inclusive load, the B0 scheduled-change banner (both fields — the
// profile's `scheduled` and the identity read's `scheduledAgreementCode`), the client-side
// self-target block, the identity-fallback wording (which differs depending on whether the
// employee is still active), one full success flow, and the two hand-written 409 refusal shapes
// rendered distinctly (the ★ pin this task calls out) plus the 412 concurrency refusal.
import { describe, it, expect, vi, beforeEach } from 'vitest'
import { render, screen, waitFor, fireEvent } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter, Routes, Route } from 'react-router-dom'
import { TerminationPage } from '../TerminationPage'

const auth = vi.hoisted(() => ({ employeeId: 'HR01' as string | null }))
vi.mock('../../../../contexts/AuthContext', () => ({
  useAuth: () => ({ user: auth.employeeId ? { employeeId: auth.employeeId, role: 'LocalHR' } : null }),
}))

const toastSpy = vi.fn()
vi.mock('../../../../components/ui/Toast', () => ({
  useToast: () => ({ toast: toastSpy }),
}))

const mockFetch = vi.fn()
vi.stubGlobal('fetch', mockFetch)

const mockStorage: Record<string, string> = { statstid_token: 'test-token' }
vi.stubGlobal('localStorage', {
  getItem: (k: string) => mockStorage[k] ?? null,
  setItem: (k: string, v: string) => { mockStorage[k] = v },
  removeItem: (k: string) => { delete mockStorage[k] },
})
Object.defineProperty(window, 'location', { value: { reload: vi.fn() }, writable: true })

function jsonResponse(body: unknown, status = 200, headers: Record<string, string> = {}) {
  return {
    ok: status >= 200 && status < 300,
    status,
    headers: new Headers(headers),
    json: async () => body,
    text: async () => JSON.stringify(body),
  }
}

function endDateFixture(over: Partial<Record<string, unknown>> = {}) {
  return {
    employeeId: 'EMP001',
    employmentEndDate: null,
    endDateDeactivated: false,
    isActive: true,
    version: 5,
    ...over,
  }
}
function identityFixture(over: Partial<Record<string, unknown>> = {}) {
  return {
    userId: 'EMP001',
    username: 'jdoe',
    displayName: 'Jane Doe',
    email: null,
    primaryOrgId: 'ORG1',
    agreementCode: 'AC',
    okVersion: 'OK24',
    employmentCategory: 'MAANEDSLOENNET',
    version: 5,
    scheduledAgreementCode: null,
    ...over,
  }
}
function startDateFixture(over: Partial<Record<string, unknown>> = {}) {
  return { employeeId: 'EMP001', employmentStartDate: '2020-03-01', version: 5, ...over }
}
function profileFixture(over: Partial<Record<string, unknown>> = {}) {
  return {
    employeeId: 'EMP001',
    partTimeFraction: 1.0,
    position: 'Konsulent',
    isPartTime: false,
    version: 5,
    scheduled: null,
    ...over,
  }
}

/** Queues the four initial-load reads in the order the page fires them:
    end-date GET, then (in parallel) identity, start-date, profile. */
function queueInitialLoad(opts: {
  endDate?: Partial<Record<string, unknown>>
  identity?: Partial<Record<string, unknown>> | null
  startDate?: Partial<Record<string, unknown>> | null
  profile?: Partial<Record<string, unknown>> | null
} = {}) {
  mockFetch.mockImplementationOnce(async () => jsonResponse(endDateFixture(opts.endDate), 200, { ETag: `"${(opts.endDate as { version?: number })?.version ?? 5}"` }))
  mockFetch.mockImplementationOnce(async () =>
    opts.identity === null ? jsonResponse({ error: 'User not found' }, 404) : jsonResponse(identityFixture(opts.identity ?? {})),
  )
  mockFetch.mockImplementationOnce(async () =>
    opts.startDate === null
      ? jsonResponse({ error: 'Employee not found' }, 404)
      : jsonResponse(startDateFixture(opts.startDate ?? {})),
  )
  mockFetch.mockImplementationOnce(async () =>
    opts.profile === null ? jsonResponse({ error: 'Employee profile not found' }, 404) : jsonResponse(profileFixture(opts.profile ?? {})),
  )
}

function renderPage(employeeId = 'EMP001') {
  return render(
    <MemoryRouter initialEntries={[`/admin/medarbejdere/${employeeId}/fratraedelse`]}>
      <Routes>
        <Route path="/admin/medarbejdere/:employeeId/fratraedelse" element={<TerminationPage />} />
      </Routes>
    </MemoryRouter>,
  )
}

beforeEach(() => {
  auth.employeeId = 'HR01'
  mockFetch.mockReset()
  toastSpy.mockReset()
})

describe('TerminationPage — load', () => {
  it('renders identity, active status, hire date and "no end date recorded"', async () => {
    queueInitialLoad()
    renderPage()

    await waitFor(() => expect(screen.getByText('Jane Doe')).toBeDefined())
    expect(screen.getByText('Aktiv')).toBeDefined()
    expect(screen.getByTestId('termination-current-end-date').textContent).toBe('Ingen')
    expect(screen.getByText(/1\.3\.2020|03-01-2020|1\/3-2020|1\.3\.2020/)).toBeDefined()
  })

  it('shows the load error and no form when the required (terminated-inclusive) end-date GET fails', async () => {
    mockFetch.mockImplementationOnce(async () => jsonResponse({ error: 'Access denied' }, 403))
    renderPage()

    await waitFor(() => expect(screen.getByTestId('termination-load-error')).toBeDefined())
    expect(screen.queryByTestId('termination-date-input')).toBeNull()
  })
})

describe('TerminationPage — B0: a scheduled change must be visible wherever a profile value is shown', () => {
  it('shows the profile-scheduled-change banner when one exists', async () => {
    queueInitialLoad({
      profile: { scheduled: { effectiveFrom: '2026-11-01', effectiveTo: null, partTimeFraction: 0.6, position: 'Senior konsulent', employmentCategory: 'MAANEDSLOENNET' } },
    })
    renderPage()

    await waitFor(() => expect(screen.getByTestId('termination-scheduled-banner')).toBeDefined())
    expect(screen.getByTestId('termination-scheduled-profile').textContent).toMatch(/Senior konsulent/)
  })

  it('shows the scheduled-agreement-code banner when one exists (a SECOND B0 field, from the identity read)', async () => {
    queueInitialLoad({
      identity: { scheduledAgreementCode: { effectiveFrom: '2026-12-01', effectiveTo: null, agreementCode: 'HK' } },
    })
    renderPage()

    await waitFor(() => expect(screen.getByTestId('termination-scheduled-agreement')).toBeDefined())
    expect(screen.getByTestId('termination-scheduled-agreement').textContent).toMatch(/HK/)
  })

  it('shows NO banner when nothing is scheduled', async () => {
    queueInitialLoad()
    renderPage()
    await waitFor(() => expect(screen.getByText('Jane Doe')).toBeDefined())
    expect(screen.queryByTestId('termination-scheduled-banner')).toBeNull()
  })
})

describe('TerminationPage — identity fallback when the ACTIVE-ONLY identity read 404s', () => {
  it('explains "already terminated" when the employee is inactive', async () => {
    queueInitialLoad({ endDate: { isActive: false, employmentEndDate: '2026-01-01', endDateDeactivated: true }, identity: null, startDate: null })
    renderPage()

    await waitFor(() => expect(screen.getByTestId('termination-identity-fallback')).toBeDefined())
    expect(screen.getByTestId('termination-identity-fallback').textContent).toMatch(/allerede fratrådt/)
    expect(screen.getByText(`Medarbejder EMP001`)).toBeDefined()
  })

  it('does NOT claim "already terminated" when the employee is still active (some other reason for the fetch failure)', async () => {
    queueInitialLoad({ identity: null })
    renderPage()

    await waitFor(() => expect(screen.getByTestId('termination-identity-fallback')).toBeDefined())
    expect(screen.getByTestId('termination-identity-fallback').textContent).not.toMatch(/allerede fratrådt/)
  })
})

describe('TerminationPage — the client-side self-target block', () => {
  it('blocks the form and shows no date input when the actor is the target employee', async () => {
    auth.employeeId = 'EMP001'
    queueInitialLoad()
    renderPage()

    await waitFor(() => expect(screen.getByTestId('termination-self-block')).toBeDefined())
    expect(screen.queryByTestId('termination-date-input')).toBeNull()
  })
})

describe('TerminationPage — a full success flow (scheduled outcome)', () => {
  it('confirms before submitting, then shows the distinct "scheduled" success message', async () => {
    const user = userEvent.setup()
    queueInitialLoad()
    renderPage()
    await waitFor(() => expect(screen.getByTestId('termination-date-input')).toBeDefined())

    fireEvent.change(screen.getByTestId('termination-date-input'), { target: { value: '2027-01-01' } })
    await user.click(screen.getByTestId('termination-submit'))

    await waitFor(() => expect(screen.getByTestId('termination-confirm-dialog')).toBeDefined())
    expect(screen.getByTestId('termination-confirm-dialog').textContent).toMatch(/planlægger fratrædelsen/)

    mockFetch.mockImplementationOnce(async () =>
      jsonResponse(endDateFixture({ employmentEndDate: '2027-01-01', isActive: true, version: 6 }), 200, { ETag: '"6"' }),
    )
    queueInitialLoad({ endDate: { employmentEndDate: '2027-01-01', version: 6 } }) // the post-save reload

    await user.click(screen.getByTestId('termination-confirm-submit'))

    await waitFor(() => expect(screen.getByTestId('termination-success')).toBeDefined())
    expect(screen.getByTestId('termination-success').textContent).toMatch(/forbliver aktiv indtil da/)
    expect(toastSpy).toHaveBeenCalledWith(expect.objectContaining({ title: 'Fratrædelse planlagt' }))
  })
})

describe('TerminationPage — refusals rendered distinctly, worded as an instruction', () => {
  async function submitAndFail(refusalBody: unknown, status: number) {
    const user = userEvent.setup()
    queueInitialLoad()
    renderPage()
    await waitFor(() => expect(screen.getByTestId('termination-date-input')).toBeDefined())
    fireEvent.change(screen.getByTestId('termination-date-input'), { target: { value: '2026-06-30' } })
    await user.click(screen.getByTestId('termination-submit'))
    await waitFor(() => expect(screen.getByTestId('termination-confirm-dialog')).toBeDefined())
    mockFetch.mockImplementationOnce(async () => jsonResponse(refusalBody, status))
    // A 412 triggers the page's own auto-reload (mirrors WorklistList's convention for a stale
    // token); queue a reload set too so that path has somewhere to resolve. Unused by the other
    // refusal kinds' assertions below — an unconsumed `mockImplementationOnce` is harmless.
    queueInitialLoad()
    await user.click(screen.getByTestId('termination-confirm-submit'))
  }

  it('★ the settlement-conflict 409 — tells HR to reverse the settlement first', async () => {
    await submitAndFail(
      {
        error: 'An active settlement exists...',
        conflictingSettlement: { entitlementType: 'VACATION', entitlementYear: 2025, settlementState: 'SETTLED' },
        blockingSettlements: [{ entitlementType: 'VACATION', entitlementYear: 2025, sequence: 1, settlementState: 'SETTLED', version: 3 }],
        affectedEntitlementYears: [2025],
        reversalEndpoint: '/api/admin/employees/EMP001/settlement-reversal',
        hint: 'Route via reversal...',
      },
      409,
    )
    await waitFor(() => expect(screen.getByTestId('termination-refusal-settlement-conflict')).toBeDefined())
    expect(screen.getByTestId('termination-refusal-settlement-conflict').textContent).toMatch(/Reverér afregningen først/)
    expect(screen.getByTestId('termination-refusal-settlement-rows').textContent).toMatch(/VACATION 2025/)
  })

  it('★ the strand-guard 409 — tells HR to fix the out-of-window registrations first', async () => {
    await submitAndFail(
      {
        error: 'Existing registrations would fall outside...',
        proposedEmploymentStartDate: '2020-03-01',
        proposedEmploymentEndDate: '2026-06-30',
        strandedMonths: [{ month: '2026-07', timeEntryCount: 3, absenceCount: 0, workTimeCount: 1 }],
        hint: 'Correct or remove...',
      },
      409,
    )
    await waitFor(() => expect(screen.getByTestId('termination-refusal-strand-conflict')).toBeDefined())
    expect(screen.getByTestId('termination-refusal-strand-conflict').textContent).toMatch(/Ret eller fjern registreringerne/)
    expect(screen.getByTestId('termination-refusal-stranded-rows').textContent).toMatch(/2026-07/)
  })

  it('412 — tells HR the record changed and reloads', async () => {
    await submitAndFail({ error: 'Concurrency precondition failed', expectedVersion: 5, actualVersion: 6 }, 412)
    await waitFor(() => expect(screen.getByTestId('termination-refusal-concurrency')).toBeDefined())
    expect(screen.getByTestId('termination-refusal-concurrency').textContent).toMatch(/En anden har ændret/)
  })

  it('422 inverted window — names both dates and what to pick instead', async () => {
    await submitAndFail(
      { error: 'Employment end date must not be before...', providedEmploymentEndDate: '2019-01-01', recordedEmploymentStartDate: '2020-03-01' },
      422,
    )
    await waitFor(() => expect(screen.getByTestId('termination-refusal-inverted-window')).toBeDefined())
    expect(screen.getByTestId('termination-refusal-inverted-window').textContent).toMatch(/hire|ansættelsesstart/i)
  })
})
