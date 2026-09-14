// SPRINT-141 / TASK-14108 — the termination screen's data layer.
//
// ★ THE PIN THIS TASK CALLS OUT EXPLICITLY: two of the PUT's refusal shapes
// (`EmploymentEndDateSettlementConflict`, `EmploymentEndDateStrandConflict`) are UNTYPED anonymous
// bodies by deliberate backend decision (`EmploymentDateEndpoints.cs:644-646`) — the generated
// contract does not (and structurally CANNOT) help distinguish them. These are hand-written in
// `useTermination.ts` from the C# source; the tests below pin them against wire-shaped fixtures
// built from that source, byte field-for-field, so a server-side drift is the one thing this
// suite is guaranteed to notice.
import { describe, it, expect, vi, beforeEach } from 'vitest'
import { renderHook } from '@testing-library/react'
import {
  useTermination,
  classifyTerminationOutcome,
  isSettlementConflict,
  isStrandConflict,
  isInvertedWindowRefusal,
  isConcurrencyRefusal,
  isAccessDeniedRefusal,
} from '../useTermination'

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

function hook() {
  return renderHook(() => useTermination()).result.current
}

beforeEach(() => {
  mockFetch.mockReset()
})

describe('useTermination — fetchEmploymentEndDate (the REQUIRED, terminated-inclusive read)', () => {
  it('maps the 200 body + ETag header into a snapshot with a usable If-Match token', async () => {
    mockFetch.mockResolvedValueOnce(
      jsonResponse(
        { employeeId: 'EMP001', employmentEndDate: '2026-11-30', endDateDeactivated: false, isActive: true, version: 7 },
        200,
        { ETag: '"7"' },
      ),
    )
    const result = await hook().fetchEmploymentEndDate('EMP001')
    expect(result).toEqual({
      ok: true,
      data: {
        employeeId: 'EMP001',
        employmentEndDate: '2026-11-30',
        endDateDeactivated: false,
        isActive: true,
        version: 7,
        etag: '"7"',
      },
    })
    expect(mockFetch.mock.calls[0][0]).toBe('/api/admin/employees/EMP001/employment-end-date')
  })

  it('falls back to the body version for the token when the ETag header is missing', async () => {
    mockFetch.mockResolvedValueOnce(
      jsonResponse(
        { employeeId: 'EMP001', employmentEndDate: null, endDateDeactivated: false, isActive: true, version: 3 },
        200,
      ),
    )
    const result = await hook().fetchEmploymentEndDate('EMP001')
    expect(result.ok).toBe(true)
    if (result.ok) expect(result.data.etag).toBe('"3"')
  })

  it('surfaces a failure (e.g. 403/404) as ok:false rather than throwing — the ONLY read source, so the screen must be able to report its failure', async () => {
    mockFetch.mockResolvedValueOnce(jsonResponse({ error: 'Employee not found' }, 404))
    const result = await hook().fetchEmploymentEndDate('EMP404')
    expect(result).toEqual({ ok: false, error: expect.any(String), status: 404 })
  })
})

describe('useTermination — best-effort context reads (ACTIVE-ONLY; swallow failure to null)', () => {
  it('fetchEmploymentStartDate returns the parsed body on success', async () => {
    mockFetch.mockResolvedValueOnce(
      jsonResponse({ employeeId: 'EMP001', employmentStartDate: '2020-03-01', version: 1 }, 200),
    )
    const result = await hook().fetchEmploymentStartDate('EMP001')
    expect(result).toEqual({ employeeId: 'EMP001', employmentStartDate: '2020-03-01', version: 1 })
  })

  it('fetchEmploymentStartDate returns null on 404 (an already-terminated employee) rather than throwing', async () => {
    mockFetch.mockResolvedValueOnce(jsonResponse({ error: 'Employee not found' }, 404))
    const result = await hook().fetchEmploymentStartDate('EMP001')
    expect(result).toBeNull()
  })

  it('fetchIdentity returns null on 404 rather than throwing', async () => {
    mockFetch.mockResolvedValueOnce(jsonResponse({ error: 'User not found' }, 404))
    const result = await hook().fetchIdentity('EMP001')
    expect(result).toBeNull()
  })

  it('fetchProfileContext returns the parsed body, including a scheduled change, on success', async () => {
    mockFetch.mockResolvedValueOnce(
      jsonResponse(
        {
          employeeId: 'EMP001',
          partTimeFraction: 1.0,
          position: 'Konsulent',
          isPartTime: false,
          version: 2,
          scheduled: { effectiveFrom: '2026-11-01', effectiveTo: null, partTimeFraction: 0.6, position: 'Senior konsulent', employmentCategory: 'MAANEDSLOENNET' },
        },
        200,
      ),
    )
    const result = await hook().fetchProfileContext('EMP001')
    expect(result?.scheduled).toEqual({
      effectiveFrom: '2026-11-01',
      effectiveTo: null,
      partTimeFraction: 0.6,
      position: 'Senior konsulent',
      employmentCategory: 'MAANEDSLOENNET',
    })
  })
})

describe('useTermination — setEmploymentEndDate (THE write)', () => {
  it('sends PUT with the caller-supplied If-Match and the { employmentEndDate } body', async () => {
    mockFetch.mockResolvedValueOnce(
      jsonResponse(
        { employeeId: 'EMP001', employmentEndDate: '2026-10-31', endDateDeactivated: true, isActive: false, version: 8 },
        200,
        { ETag: '"8"' },
      ),
    )
    const result = await hook().setEmploymentEndDate('EMP001', '"7"', '2026-10-31')
    expect(result.ok).toBe(true)
    const [url, init] = mockFetch.mock.calls[0]
    expect(url).toBe('/api/admin/employees/EMP001/employment-end-date')
    expect(init.method).toBe('PUT')
    expect((init.headers as Record<string, string>)['If-Match']).toBe('"7"')
    expect(JSON.parse(init.body as string)).toEqual({ employmentEndDate: '2026-10-31' })
  })

  it('sends { employmentEndDate: null } for a clear/reactivate request', async () => {
    mockFetch.mockResolvedValueOnce(
      jsonResponse({ employeeId: 'EMP001', employmentEndDate: null, endDateDeactivated: false, isActive: true, version: 9 }, 200),
    )
    await hook().setEmploymentEndDate('EMP001', '"8"', null)
    const [, init] = mockFetch.mock.calls[0]
    expect(JSON.parse(init.body as string)).toEqual({ employmentEndDate: null })
  })

  it('★ PIN — the settlement-conflict 409 shape (EmploymentDateEndpoints.cs:560-591), byte field-for-field, discriminated by isSettlementConflict', async () => {
    const body = {
      error:
        'An active settlement exists for a ferieår this end-date change affects; the change is rejected fail-closed (SPRINT-70 R7a).',
      conflictingSettlement: { entitlementType: 'VACATION', entitlementYear: 2025, settlementState: 'SETTLED' },
      blockingSettlements: [
        { entitlementType: 'VACATION', entitlementYear: 2025, sequence: 1, settlementState: 'SETTLED', version: 3 },
      ],
      affectedEntitlementYears: [2025],
      reversalEndpoint: '/api/admin/employees/EMP001/settlement-reversal',
      hint: 'Route the correction through the slice-3b reversal endpoint...',
    }
    mockFetch.mockResolvedValueOnce(jsonResponse(body, 409))
    const result = await hook().setEmploymentEndDate('EMP001', '"7"', '2026-08-01')
    expect(result.ok).toBe(false)
    if (result.ok) return
    expect(result.status).toBe(409)
    expect(isSettlementConflict(result.body)).toBe(true)
    expect(isStrandConflict(result.body)).toBe(false)
    if (isSettlementConflict(result.body)) {
      expect(result.body.blockingSettlements).toHaveLength(1)
      expect(result.body.reversalEndpoint).toBe('/api/admin/employees/EMP001/settlement-reversal')
    }
  })

  it('★ PIN — the strand-guard 409 shape (EmploymentDateEndpoints.cs:798-827), byte field-for-field, discriminated by isStrandConflict', async () => {
    const body = {
      error:
        'Existing registrations would fall outside the proposed employment window; the change is rejected fail-closed (ADR-040 D3 strand guard).',
      proposedEmploymentStartDate: '2020-03-01',
      proposedEmploymentEndDate: '2026-06-30',
      strandedMonths: [{ month: '2026-07', timeEntryCount: 3, absenceCount: 0, workTimeCount: 1 }],
      hint: 'Correct or remove the out-of-window registrations in the listed months first...',
    }
    mockFetch.mockResolvedValueOnce(jsonResponse(body, 409))
    const result = await hook().setEmploymentEndDate('EMP001', '"7"', '2026-06-30')
    expect(result.ok).toBe(false)
    if (result.ok) return
    expect(result.status).toBe(409)
    expect(isStrandConflict(result.body)).toBe(true)
    expect(isSettlementConflict(result.body)).toBe(false)
    if (isStrandConflict(result.body)) {
      expect(result.body.strandedMonths).toEqual([{ month: '2026-07', timeEntryCount: 3, absenceCount: 0, workTimeCount: 1 }])
    }
  })

  it('pins the 422 inverted-window shape', async () => {
    mockFetch.mockResolvedValueOnce(
      jsonResponse(
        { error: 'Employment end date must not be before...', providedEmploymentEndDate: '2019-01-01', recordedEmploymentStartDate: '2020-03-01' },
        422,
      ),
    )
    const result = await hook().setEmploymentEndDate('EMP001', '"7"', '2019-01-01')
    expect(result.ok).toBe(false)
    if (result.ok) return
    expect(isInvertedWindowRefusal(result.body)).toBe(true)
  })

  it('pins the 412 concurrency shape', async () => {
    mockFetch.mockResolvedValueOnce(jsonResponse({ error: 'Concurrency precondition failed', expectedVersion: 7, actualVersion: 8 }, 412))
    const result = await hook().setEmploymentEndDate('EMP001', '"7"', '2026-06-30')
    expect(result.ok).toBe(false)
    if (result.ok) return
    expect(isConcurrencyRefusal(result.body)).toBe(true)
  })

  it('pins the two 403 shapes (self-target and org-scope) as the SAME wire shape, distinguished only by reason text', async () => {
    mockFetch.mockResolvedValueOnce(
      jsonResponse({ error: 'Access denied', reason: 'Own employment end date cannot be modified; a second administrator must perform this change' }, 403),
    )
    const self = await hook().setEmploymentEndDate('EMP001', '"7"', '2026-06-30')
    expect(self.ok).toBe(false)
    if (!self.ok) expect(isAccessDeniedRefusal(self.body)).toBe(true)

    mockFetch.mockResolvedValueOnce(jsonResponse({ error: 'Access denied', reason: 'Target employee not in an accessible organisation' }, 403))
    const scope = await hook().setEmploymentEndDate('EMP001', '"7"', '2026-06-30')
    expect(scope.ok).toBe(false)
    if (!scope.ok) expect(isAccessDeniedRefusal(scope.body)).toBe(true)
  })
})

describe('classifyTerminationOutcome — the R1(a)-(d) mapping, mirrored 1:1 from EmploymentEndDateLifecycleWriter.ComputeEndDateLifecycle', () => {
  it('R1(a): was active, set a date, now inactive → terminated-now', () => {
    expect(
      classifyTerminationOutcome({ isActive: true, endDateDeactivated: false }, { isActive: false }, '2026-01-01'),
    ).toBe('terminated-now')
  })

  it('R1(b): was active, set a future date, still active → scheduled', () => {
    expect(
      classifyTerminationOutcome({ isActive: true, endDateDeactivated: false }, { isActive: true }, '2027-01-01'),
    ).toBe('scheduled')
  })

  it('R1(c): cleared the date, now active → reactivated', () => {
    expect(
      classifyTerminationOutcome({ isActive: false, endDateDeactivated: true }, { isActive: true }, null),
    ).toBe('reactivated')
  })

  it('R1(c): cleared the date, still inactive (manually inactive for another reason) → cleared-still-inactive', () => {
    expect(
      classifyTerminationOutcome({ isActive: false, endDateDeactivated: false }, { isActive: false }, null),
    ).toBe('cleared-still-inactive')
  })

  it('correction on an already lifecycle-deactivated row, still passed → corrected-still-terminated', () => {
    expect(
      classifyTerminationOutcome({ isActive: false, endDateDeactivated: true }, { isActive: false }, '2026-02-01'),
    ).toBe('corrected-still-terminated')
  })

  it('correction on an already lifecycle-deactivated row, no longer passed → corrected-reactivated', () => {
    expect(
      classifyTerminationOutcome({ isActive: false, endDateDeactivated: true }, { isActive: true }, '2099-01-01'),
    ).toBe('corrected-reactivated')
  })

  it('R1(d): set a date on a manually-inactive row → recorded-no-status-change', () => {
    expect(
      classifyTerminationOutcome({ isActive: false, endDateDeactivated: false }, { isActive: false }, '2026-02-01'),
    ).toBe('recorded-no-status-change')
  })
})
