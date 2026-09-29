// SPRINT-140 / TASK-14007 — the backdate worklist's first screen. Every pin
// below is named in the plan cell's acceptance criteria and each is written to
// FAIL if the behaviour it names regresses:
//  - a Global Admin sees the Recalculate instruction card, and rendering it
//    fires NO network call beyond the initial list GET (it is inert text, not
//    a button that reaches the Payroll host — the browser cannot anyway);
//  - an HR (non-Global-Admin) actor never sees that card;
//  - a row whose `recalcBlockedBy` is non-empty renders the blocked notice and
//    NEVER the run-instruction, regardless of role — the exact defect the
//    Step-5a review pinned;
//  - a stale-version Resolve (412) is reported and the list is refetched;
//  - a 428 (this client failing to send If-Match) is surfaced as the bug it
//    is, never silently retried (bonus pin — not explicitly named in the
//    acceptance criteria, but the spec is explicit this must never be
//    swallowed, so it is worth proving);
//  - OQ-7 (a), added post-acceptance by owner ruling (2026-09-09): the
//    "mark Recalculated" gate tracks the REMEDY, not a blanket role. An HR
//    (non-Global-Admin) actor CAN reach it on a SETTLED_YEAR row (its remedy,
//    the settlement reversal, is HROrAbove) and CANNOT on an EXPORTED_MONTH
//    row (its remedy, the payroll recalculate, is GlobalAdminOnly) — and a
//    blocked row suppresses it regardless of kind or role.
import { describe, it, expect, vi, beforeEach } from 'vitest'
import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import WorklistList from '../WorklistList'

const auth = vi.hoisted(() => ({ role: 'LocalHR' as string | null }))
vi.mock('../../../../contexts/AuthContext', () => ({
  useAuth: () => ({ role: auth.role }),
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

/** A full BackdateWorklistRow — EXPORTED_MONTH by default, open (unresolved). */
function exportedMonthRow(over: Partial<Record<string, unknown>> = {}) {
  return {
    worklistId: 'wl-1',
    employeeId: 'EMP001',
    kind: 'EXPORTED_MONTH',
    year: 2026,
    month: 3,
    exportId: 'exp-1',
    entitlementType: null,
    entitlementYear: null,
    triggers: [
      {
        kind: 'PROFILE_CHANGE',
        eventId: 'evt-1',
        effectiveFrom: '2026-03-05',
        appendedAt: '2026-03-06T08:00:00Z',
        actorId: 'HR01',
        baselineContentHash: 'abc123',
        baselineSettlementSequence: null,
        baselineSettlementState: null,
        recalculatedSince: false,
        reversedSince: null,
        recalcBlockedBy: null,
      },
    ],
    recalcBlockedBy: [],
    recalculatedSince: false,
    reversedSince: null,
    createdAt: '2026-03-06T08:00:01Z',
    createdBy: 'HR01',
    resolvedAt: null,
    resolvedBy: null,
    resolution: null,
    resolutionReason: null,
    version: 3,
    ...over,
  }
}

/** A full BackdateWorklistRow — SETTLED_YEAR, open (unresolved). `recalcBlockedBy`
    is structurally always empty for this kind (`BackdateWorklistDerivation.RecalcBlockedBy`
    returns an empty set for anything but EXPORTED_MONTH) — callers may still override
    it to prove the UI's blocked-suppression does not silently assume that invariant. */
function settledYearRow(over: Partial<Record<string, unknown>> = {}) {
  return {
    worklistId: 'wl-3',
    employeeId: 'EMP002',
    kind: 'SETTLED_YEAR',
    year: null,
    month: null,
    exportId: null,
    entitlementType: 'VACATION',
    entitlementYear: 2025,
    triggers: [
      {
        kind: 'AGREEMENT_CODE_CHANGE',
        eventId: 'evt-2',
        effectiveFrom: '2025-11-01',
        appendedAt: '2025-11-02T08:00:00Z',
        actorId: 'HR02',
        baselineContentHash: null,
        baselineSettlementSequence: 1,
        baselineSettlementState: 'SETTLED',
        recalculatedSince: null,
        reversedSince: false,
        recalcBlockedBy: null,
      },
    ],
    recalcBlockedBy: [],
    recalculatedSince: null,
    reversedSince: false,
    createdAt: '2025-11-02T08:00:01Z',
    createdBy: 'HR02',
    resolvedAt: null,
    resolvedBy: null,
    resolution: null,
    resolutionReason: null,
    version: 5,
    ...over,
  }
}

beforeEach(() => {
  auth.role = 'LocalHR'
  mockFetch.mockReset()
  toastSpy.mockReset()
})

describe('WorklistList — the Recalculate instruction card (OQ-2 (a))', () => {
  it('a Global Admin sees the card, and showing it fires no network call beyond the list GET', async () => {
    auth.role = 'GlobalAdmin'
    mockFetch.mockImplementationOnce(async () => jsonResponse([exportedMonthRow()]))

    render(<WorklistList onResolved={vi.fn()} />)

    await waitFor(() => expect(screen.getByTestId('worklist-recalc-card-wl-1')).toBeDefined())
    expect(screen.getByText(/POST \/api\/payroll\/recalculate/)).toBeDefined()
    // The card renders no payload — it is prose, not a form.
    expect(screen.queryByText(/"profile"/i)).toBeNull()

    // Rendering the card is the ONLY thing that happened — exactly one fetch (the list GET).
    expect(mockFetch).toHaveBeenCalledTimes(1)
  })

  it('an HR (non-Global-Admin) actor never sees the card, only the "requires Global Admin" hint', async () => {
    auth.role = 'LocalHR'
    mockFetch.mockImplementationOnce(async () => jsonResponse([exportedMonthRow()]))

    render(<WorklistList onResolved={vi.fn()} />)

    await waitFor(() => expect(screen.getByTestId('worklist-recalc-hint-wl-1')).toBeDefined())
    expect(screen.queryByTestId('worklist-recalc-card-wl-1')).toBeNull()
  })
})

describe('WorklistList — a blocked row never shows the run-instruction', () => {
  it('recalcBlockedBy non-empty renders the blocked notice, even for a Global Admin, and suppresses the card AND the Recalculated affordance — only Dismiss remains', async () => {
    const user = userEvent.setup()
    auth.role = 'GlobalAdmin'
    mockFetch.mockImplementationOnce(async () =>
      jsonResponse([exportedMonthRow({ worklistId: 'wl-2', recalcBlockedBy: ['QUAL-149'] })]),
    )

    render(<WorklistList onResolved={vi.fn()} />)

    await waitFor(() => expect(screen.getByTestId('worklist-blocked-wl-2')).toBeDefined())
    expect(screen.getByText(/Genberegning blokeret — QUAL-149/)).toBeDefined()
    expect(screen.queryByTestId('worklist-recalc-card-wl-2')).toBeNull()
    expect(screen.queryByTestId('worklist-recalc-hint-wl-2')).toBeNull()

    // Open the resolve form (the only place the "mark recalculated" button could
    // appear) and prove it is STILL absent — even for a Global Admin — while
    // Dismiss stays reachable. Asserting this only before the form opens would
    // pass vacuously (the button is also absent whenever no form is open).
    await user.click(screen.getByTestId('worklist-resolve-open-wl-2'))
    expect(screen.queryByTestId('worklist-mark-recalculated-wl-2')).toBeNull()
    expect(screen.getByTestId('worklist-dismiss-wl-2')).toBeDefined()
  })
})

describe('WorklistList — OQ-7 (a): the Recalculate gate tracks the remedy, not a blanket role', () => {
  it('an HR (non-Global-Admin) actor CAN mark a SETTLED_YEAR row Recalculated — its remedy (settlement reversal) is HROrAbove', async () => {
    const user = userEvent.setup()
    auth.role = 'LocalHR'
    mockFetch.mockImplementationOnce(async () => jsonResponse([settledYearRow()]))

    render(<WorklistList onResolved={vi.fn()} />)
    await waitFor(() => expect(screen.getByTestId('worklist-reversal-wl-3')).toBeDefined())

    await user.click(screen.getByTestId('worklist-resolve-open-wl-3'))
    expect(screen.getByTestId('worklist-mark-recalculated-wl-3')).toBeDefined()
  })

  it('the SAME HR actor CANNOT mark an EXPORTED_MONTH row Recalculated — its remedy (payroll recalculate) is GlobalAdminOnly', async () => {
    const user = userEvent.setup()
    auth.role = 'LocalHR'
    mockFetch.mockImplementationOnce(async () => jsonResponse([exportedMonthRow()]))

    render(<WorklistList onResolved={vi.fn()} />)
    await waitFor(() => expect(screen.getByTestId('worklist-resolve-open-wl-1')).toBeDefined())

    await user.click(screen.getByTestId('worklist-resolve-open-wl-1'))
    expect(screen.queryByTestId('worklist-mark-recalculated-wl-1')).toBeNull()
    expect(screen.getByTestId('worklist-dismiss-wl-1')).toBeDefined()
  })

  // S144 / TASK-14406: this test used to seed a BLOCKED SETTLED_YEAR row (`recalcBlockedBy:
  // ['QUAL-150']`). The backend can never produce that — `BackdateWorklistDerivation.RecalcBlockedBy`
  // is empty for every kind but EXPORTED_MONTH — so it pinned an impossible premise. The fixture
  // below is one the backend CAN produce: a blocked EXPORTED_MONTH row seen by a non-Global-Admin HR
  // actor (no Recalculated, no Håndteret manuelt — both remedies are GlobalAdmin-only there).
  it('a blocked EXPORTED_MONTH row for a non-Global-Admin HR actor offers only Dismiss', async () => {
    const user = userEvent.setup()
    auth.role = 'LocalHR'
    mockFetch.mockImplementationOnce(async () =>
      jsonResponse([exportedMonthRow({ worklistId: 'wl-4', recalcBlockedBy: ['QUAL-150'] })]),
    )

    render(<WorklistList onResolved={vi.fn()} />)
    await waitFor(() => expect(screen.getByTestId('worklist-blocked-wl-4')).toBeDefined())

    await user.click(screen.getByTestId('worklist-resolve-open-wl-4'))
    expect(screen.queryByTestId('worklist-mark-recalculated-wl-4')).toBeNull()
    expect(screen.queryByTestId('worklist-handled-manually-wl-4')).toBeNull()
    expect(screen.getByTestId('worklist-dismiss-wl-4')).toBeDefined()
  })
})

describe('WorklistList — "Håndteret manuelt" button visibility (role x kind x blocked)', () => {
  const cases: Array<{ role: string; kind: 'EXPORTED_MONTH' | 'SETTLED_YEAR'; blocked: boolean; visible: boolean }> = [
    { role: 'GlobalAdmin', kind: 'EXPORTED_MONTH', blocked: false, visible: true },
    { role: 'GlobalAdmin', kind: 'EXPORTED_MONTH', blocked: true, visible: true },
    { role: 'LocalHR', kind: 'EXPORTED_MONTH', blocked: false, visible: false },
    { role: 'LocalHR', kind: 'EXPORTED_MONTH', blocked: true, visible: false },
    { role: 'GlobalAdmin', kind: 'SETTLED_YEAR', blocked: false, visible: true },
    { role: 'LocalHR', kind: 'SETTLED_YEAR', blocked: false, visible: true },
  ]
  it.each(cases)('$role on $kind (blocked=$blocked) -> visible=$visible', async ({ role, kind, blocked, visible }) => {
    const user = userEvent.setup()
    auth.role = role
    const over = { worklistId: 'wl-x', ...(blocked ? { recalcBlockedBy: ['QUAL-149'] } : {}) }
    const row = kind === 'EXPORTED_MONTH' ? exportedMonthRow(over) : settledYearRow(over)
    mockFetch.mockImplementationOnce(async () => jsonResponse([row]))

    render(<WorklistList onResolved={vi.fn()} />)
    await waitFor(() => expect(screen.getByTestId('worklist-resolve-open-wl-x')).toBeDefined())
    await user.click(screen.getByTestId('worklist-resolve-open-wl-x'))

    expect(screen.getByTestId('worklist-dismiss-wl-x')).toBeDefined()
    expect(!!screen.queryByTestId('worklist-handled-manually-wl-x')).toBe(visible)
  })
})

describe('WorklistList — resolving with each verb', () => {
  async function resolveWith(testId: string, role = 'GlobalAdmin') {
    const user = userEvent.setup()
    auth.role = role
    mockFetch
      .mockImplementationOnce(async () => jsonResponse([exportedMonthRow()])) // initial GET
      .mockImplementationOnce(async () => jsonResponse({ worklistId: 'wl-1', resolution: 'X', resolutionBlockedBy: [] })) // POST
      .mockImplementationOnce(async () => jsonResponse([])) // refetch
    render(<WorklistList onResolved={vi.fn()} />)
    await waitFor(() => expect(screen.getByTestId('worklist-resolve-open-wl-1')).toBeDefined())
    await user.click(screen.getByTestId('worklist-resolve-open-wl-1'))
    await user.type(screen.getByTestId('worklist-reason-wl-1'), 'Testbegrundelse')
    await user.click(screen.getByTestId(testId))
    await waitFor(() => expect(toastSpy).toHaveBeenCalled())
    const posted = JSON.parse(mockFetch.mock.calls[1][1].body as string)
    return { posted, toast: toastSpy.mock.calls[0][0] }
  }

  it('Håndteret manuelt posts HANDLED_MANUALLY and toasts its own text (not "Afvist")', async () => {
    const { posted, toast } = await resolveWith('worklist-handled-manually-wl-1')
    expect(posted.resolution).toBe('HANDLED_MANUALLY')
    expect(toast.description).toBe('Markeret som håndteret manuelt.')
  })

  it('Afvis still toasts "Afvist."', async () => {
    const { posted, toast } = await resolveWith('worklist-dismiss-wl-1')
    expect(posted.resolution).toBe('DISMISSED')
    expect(toast.description).toBe('Afvist.')
  })

  it('a resolved manual row is labelled "Håndteret manuelt", never the raw wire value', async () => {
    mockFetch.mockImplementationOnce(async () =>
      jsonResponse([
        exportedMonthRow({
          resolvedAt: '2026-04-01T09:00:00Z',
          resolvedBy: 'ADMIN01',
          resolution: 'HANDLED_MANUALLY',
          resolutionReason: 'Rettet i SLS',
        }),
      ]),
    )
    render(<WorklistList onResolved={vi.fn()} />)
    await waitFor(() => expect(screen.getByTestId('worklist-resolved-wl-1')).toBeDefined())
    expect(screen.getByTestId('worklist-resolved-wl-1').textContent).toContain('Løst som Håndteret manuelt')
    expect(screen.getByTestId('worklist-resolved-wl-1').textContent).not.toContain('HANDLED_MANUALLY')
  })
})

describe('WorklistList — 409 / 403 messages, fed the real server JSON through fetch -> apiFetchWithEtag -> hook -> component', () => {
  /** A genuine fetch Response, so the whole transport path parses the body. */
  function realResponse(body: unknown, status: number) {
    return new Response(JSON.stringify(body), { status, headers: { 'Content-Type': 'application/json' } })
  }

  async function submitFailing(status: number, body: unknown, buttonId = 'worklist-mark-recalculated-wl-1') {
    const user = userEvent.setup()
    auth.role = 'GlobalAdmin'
    mockFetch
      .mockImplementationOnce(async () => jsonResponse([exportedMonthRow()])) // initial GET
      .mockImplementationOnce(async () => realResponse(body, status)) // the resolve POST
      .mockImplementation(async () => jsonResponse([exportedMonthRow()])) // any refetch
    render(<WorklistList onResolved={vi.fn()} />)
    await waitFor(() => expect(screen.getByTestId('worklist-resolve-open-wl-1')).toBeDefined())
    await user.click(screen.getByTestId('worklist-resolve-open-wl-1'))
    await user.type(screen.getByTestId('worklist-reason-wl-1'), 'Testbegrundelse')
    await user.click(screen.getByTestId(buttonId))
    await waitFor(() => expect(screen.getByTestId('worklist-notice-wl-1')).toBeDefined())
    return screen.getByTestId('worklist-notice-wl-1').textContent ?? ''
  }

  it('409 worklist-recalc-blocked: names the blocking ids and points at "Håndteret manuelt"', async () => {
    const text = await submitFailing(409, {
      error: 'Recalculation is blocked for this row',
      kind: 'worklist-recalc-blocked',
      blockedBy: ['QUAL-149', 'QUAL-150'],
    })
    expect(text).toContain('QUAL-149, QUAL-150')
    expect(text).toContain('Håndteret manuelt')
    expect(text).not.toContain('allerede løst')
  })

  it('409 already-resolved ({ error } only): the already-resolved message', async () => {
    const text = await submitFailing(409, { error: 'Worklist item is already resolved' })
    expect(text).toContain('allerede løst af en anden')
    expect(text).not.toContain('blokeret')
  })

  it('403: its own permission message, not the generic "Kunne ikke gemme"', async () => {
    const text = await submitFailing(403, { error: 'Access denied', reason: 'requires GlobalAdmin' }, 'worklist-handled-manually-wl-1')
    expect(text).toContain('ikke tilladelse')
    expect(text).not.toContain('Kunne ikke gemme')
  })
})

describe('WorklistList — Resolve / If-Match', () => {
  it('412 (stale version): reports it and refetches the list', async () => {
    const user = userEvent.setup()
    mockFetch
      .mockImplementationOnce(async () => jsonResponse([exportedMonthRow()])) // initial GET
      .mockImplementationOnce(async () =>
        jsonResponse({ error: 'Concurrency precondition failed', expectedVersion: 3, actualVersion: 4 }, 412),
      ) // the resolve POST
      .mockImplementationOnce(async () => jsonResponse([exportedMonthRow({ version: 4 })])) // the refetch

    render(<WorklistList onResolved={vi.fn()} />)
    await waitFor(() => expect(screen.getByTestId('worklist-resolve-open-wl-1')).toBeDefined())

    await user.click(screen.getByTestId('worklist-resolve-open-wl-1'))
    await user.type(screen.getByTestId('worklist-reason-wl-1'), 'Testbegrundelse')
    await user.click(screen.getByTestId('worklist-dismiss-wl-1'))

    await waitFor(() => expect(screen.getByTestId('worklist-notice-wl-1')).toBeDefined())
    expect(screen.getByText(/ændret af en anden bruger/)).toBeDefined()
    // The list was refetched (initial GET + resolve POST + refetch GET = 3 calls).
    await waitFor(() => expect(mockFetch).toHaveBeenCalledTimes(3))
  })

  it('428 (missing precondition — a client bug): surfaced distinctly, never silently retried', async () => {
    const user = userEvent.setup()
    mockFetch
      .mockImplementationOnce(async () => jsonResponse([exportedMonthRow()])) // initial GET
      .mockImplementationOnce(async () => jsonResponse({ error: 'Missing If-Match' }, 428)) // the resolve POST

    render(<WorklistList onResolved={vi.fn()} />)
    await waitFor(() => expect(screen.getByTestId('worklist-resolve-open-wl-1')).toBeDefined())

    await user.click(screen.getByTestId('worklist-resolve-open-wl-1'))
    await user.type(screen.getByTestId('worklist-reason-wl-1'), 'Testbegrundelse')
    await user.click(screen.getByTestId('worklist-dismiss-wl-1'))

    await waitFor(() => expect(screen.getByTestId('worklist-notice-wl-1')).toBeDefined())
    expect(screen.getByText(/Intern fejl \(428\)/)).toBeDefined()
    // No refetch after a 428 — it is a client bug report, not a stale-state condition to recover from.
    expect(mockFetch).toHaveBeenCalledTimes(2)
  })
})
