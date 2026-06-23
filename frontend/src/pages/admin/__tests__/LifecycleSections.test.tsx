// S76b / TASK-7603 — Vitest + @testing-library tests for the ledelseslinje +
// vikar + delete sections of the unified EditPersonDrawer. Mocks `useReportingLines`
// (the prompt's "mock the hooks") so each contract branch is driven directly:
//   • approver assign (FIRST = If-None-Match:* via no-ifMatch; reassign = If-Match)
//     + remove + the picker forbidden set excludes self + descendants
//   • the create-mode draft approver threaded into the create POST
//   • vikar create + the 409 one-active honest message + Afslut
//   • delete dialog: preflight-409 → resubmit → the in-lock-census SECOND 409 →
//     re-prompt (BOTH 409s), then success
import { describe, it, expect, vi, beforeEach } from 'vitest'
import { render, screen, waitFor, fireEvent, within } from '@testing-library/react'
import { ToastProvider } from '../../../components/ui/Toast'
import { ApproverSection } from '../editPerson/ApproverSection'
import { VikarSection } from '../editPerson/VikarSection'
import { DangerSection } from '../editPerson/DangerSection'
import { PersonPickerDialog } from '../editPerson/PersonPickerDialog'
import { LifecycleSections } from '../editPerson/LifecycleSections'
import type { ApiResult } from '../../../lib/api'
import type {
  ReportingLineEntry,
  DirectReport,
  PersonSearchResult,
  VikarCreatedResult,
  ActiveVikarDto,
  DeletePersonResult,
} from '../../../hooks/useReportingLines'

// --- The mocked useReportingLines (settable per test) ---
const hookMock = {
  assignManager: vi.fn<
    (
      body: { employeeId: string; managerId: string; effectiveFrom: string },
      ifMatch?: string,
    ) => Promise<ApiResult<ReportingLineEntry>>
  >(),
  removeManager: vi.fn<(employeeId: string, ifMatch: string) => Promise<ApiResult<void>>>(),
  createVikar: vi.fn<
    (managerId: string, body: unknown) => Promise<ApiResult<VikarCreatedResult>>
  >(),
  endVikar: vi.fn<(managerId: string) => Promise<ApiResult<void>>>(),
  deletePersonWithReassignment: vi.fn<
    (employeeId: string, replacements: Record<string, string>) => Promise<DeletePersonResult>
  >(),
  searchPeople: vi.fn<(params: unknown) => Promise<ApiResult<PersonSearchResult>>>(),
  // BLOCKER 3 — the reads LifecycleSections self-resolves from (lines/reports/vikar).
  fetchEmployeeLines: vi.fn<
    (employeeId: string) => Promise<ApiResult<{ active: ReportingLineEntry[]; history: ReportingLineEntry[] }>>
  >(),
  fetchDirectReports: vi.fn<(managerId: string) => Promise<ApiResult<DirectReport[]>>>(),
  fetchActiveVikar: vi.fn<
    (managerId: string) => Promise<ApiResult<{ activeVikar: ActiveVikarDto | null }>>
  >(),
}

vi.mock('../../../hooks/useReportingLines', async (importActual) => {
  const actual = await importActual<typeof import('../../../hooks/useReportingLines')>()
  return {
    ...actual,
    useReportingLines: () => hookMock,
  }
})

function searchResult(items: PersonSearchResult['items']): ApiResult<PersonSearchResult> {
  return { ok: true, data: { items, total: items.length, limit: 60, offset: 0 } }
}

const lineEntry = (version: number): ReportingLineEntry => ({
  reportingLineId: 'rl-1',
  employeeId: 'EMP001',
  managerId: 'MGR9',
  organisationId: 'ORG1',
  relationship: 'PRIMARY',
  effectiveFrom: '2026-06-15',
  effectiveTo: null,
  source: 'ADMIN',
  version,
  createdBy: 'ADMIN1',
  createdAt: '2026-06-15T00:00:00Z',
})

function wrap(ui: React.ReactElement) {
  return render(<ToastProvider>{ui}</ToastProvider>)
}

beforeEach(() => {
  vi.clearAllMocks()
  hookMock.searchPeople.mockResolvedValue(
    searchResult([
      { userId: 'MGR9', displayName: 'Mette Holm', primaryOrgName: 'Direktion', enhedLabel: null },
      { userId: 'BO1', displayName: 'Bo Dahl', primaryOrgName: 'Drift', enhedLabel: 'Drift' },
    ]),
  )
  // BLOCKER 3 defaults: the person has a PRIMARY approver and approves one report
  // (so the vikar section renders), and no active vikar unless a test overrides it.
  hookMock.fetchEmployeeLines.mockResolvedValue({ ok: true, data: { active: [], history: [] } })
  hookMock.fetchDirectReports.mockResolvedValue({ ok: true, data: [] })
  hookMock.fetchActiveVikar.mockResolvedValue({ ok: true, data: { activeVikar: null } })
})

// ═══════════════════════════════════════════════════════════════════════════
describe('ApproverSection — assign / reassign / remove', () => {
  it('FIRST assign sends NO If-Match (the hook composes If-None-Match:*)', async () => {
    hookMock.assignManager.mockResolvedValue({ ok: true, data: lineEntry(1) })
    const onChanged = vi.fn()
    wrap(
      <ApproverSection
        mode="edit"
        personName="Test Bruger"
        employeeId="EMP001"
        currentApproverId={null}
        currentReportingLineEtag={null}
        onChanged={onChanged}
      />,
    )

    fireEvent.click(screen.getByTestId('approver-assign'))
    // Pick from the server-search picker.
    await waitFor(() => expect(screen.getByTestId('picker-row-MGR9')).toBeDefined())
    fireEvent.click(screen.getByTestId('picker-row-MGR9'))

    await waitFor(() => expect(hookMock.assignManager).toHaveBeenCalled())
    const [body, ifMatch] = hookMock.assignManager.mock.calls[0]
    expect(body).toMatchObject({ employeeId: 'EMP001', managerId: 'MGR9' })
    expect(ifMatch).toBeUndefined() // FIRST assign → no If-Match → hook sends If-None-Match:*
    await waitFor(() => expect(onChanged).toHaveBeenCalled())
    expect(screen.getByTestId('approver-assigned').textContent).toContain('Mette Holm')
  })

  it('REASSIGN sends the current line ETag as If-Match', async () => {
    hookMock.assignManager.mockResolvedValue({ ok: true, data: lineEntry(3) })
    wrap(
      <ApproverSection
        mode="edit"
        personName="Test Bruger"
        employeeId="EMP001"
        currentApproverId="MGR9"
        currentApproverName="Mette Holm"
        currentReportingLineEtag='"2"'
        onChanged={vi.fn()}
      />,
    )

    fireEvent.click(screen.getByTestId('approver-change'))
    await waitFor(() => expect(screen.getByTestId('picker-row-BO1')).toBeDefined())
    fireEvent.click(screen.getByTestId('picker-row-BO1'))

    await waitFor(() => expect(hookMock.assignManager).toHaveBeenCalled())
    const [, ifMatch] = hookMock.assignManager.mock.calls[0]
    expect(ifMatch).toBe('"2"') // reassign → If-Match the current line version
  })

  it('remove calls removeManager with the current ETag', async () => {
    hookMock.removeManager.mockResolvedValue({ ok: true, data: undefined })
    const onChanged = vi.fn()
    wrap(
      <ApproverSection
        mode="edit"
        personName="Test Bruger"
        employeeId="EMP001"
        currentApproverId="MGR9"
        currentApproverName="Mette Holm"
        currentReportingLineEtag='"2"'
        onChanged={onChanged}
      />,
    )

    fireEvent.click(screen.getByTestId('approver-remove'))
    await waitFor(() => expect(hookMock.removeManager).toHaveBeenCalledWith('EMP001', '"2"'))
    await waitFor(() => expect(onChanged).toHaveBeenCalled())
    expect(screen.getByTestId('approver-assign')).toBeDefined() // back to the empty state
  })

  it('a root person shows the read-only top-of-line label (no assign affordance)', () => {
    wrap(
      <ApproverSection
        mode="edit"
        personName="Mette Holm"
        employeeId="MGR9"
        isRoot
        currentApproverId={null}
        onChanged={vi.fn()}
      />,
    )
    expect(screen.getByTestId('approver-root').textContent).toContain('Øverste')
    expect(screen.queryByTestId('approver-assign')).toBeNull()
  })

  it('CREATE mode sets the draft approver and does NOT call the reporting-line API', async () => {
    const onDraft = vi.fn()
    wrap(
      <ApproverSection
        mode="create"
        personName="Ny Bruger"
        draftApproverId={null}
        onDraftApproverChange={onDraft}
        onChanged={vi.fn()}
      />,
    )
    fireEvent.click(screen.getByTestId('approver-assign'))
    await waitFor(() => expect(screen.getByTestId('picker-row-MGR9')).toBeDefined())
    fireEvent.click(screen.getByTestId('picker-row-MGR9'))

    await waitFor(() => expect(onDraft).toHaveBeenCalledWith('MGR9', 'Mette Holm'))
    expect(hookMock.assignManager).not.toHaveBeenCalled()
  })
})

// ═══════════════════════════════════════════════════════════════════════════
describe('PersonPickerDialog — forbidden set excludes self + descendants', () => {
  it('filters out forbidden ids from the rendered rows', async () => {
    hookMock.searchPeople.mockResolvedValue(
      searchResult([
        { userId: 'EMP001', displayName: 'Self', primaryOrgName: null, enhedLabel: null },
        { userId: 'CHILD1', displayName: 'A Report', primaryOrgName: null, enhedLabel: null },
        { userId: 'OK1', displayName: 'Allowed', primaryOrgName: null, enhedLabel: null },
      ]),
    )
    wrap(
      <PersonPickerDialog
        open
        title="Vælg godkender"
        forbidden={new Set(['EMP001', 'CHILD1'])}
        onPick={vi.fn()}
        onClose={vi.fn()}
      />,
    )
    await waitFor(() => expect(screen.getByTestId('picker-row-OK1')).toBeDefined())
    // Self + the descendant are filtered out client-side (defence-in-depth mirror).
    expect(screen.queryByTestId('picker-row-EMP001')).toBeNull()
    expect(screen.queryByTestId('picker-row-CHILD1')).toBeNull()
  })

  it('threads excludeEmployeeId to the server search (the server-side mirror)', async () => {
    wrap(
      <PersonPickerDialog
        open
        title="Vælg godkender"
        excludeEmployeeId="EMP001"
        onPick={vi.fn()}
        onClose={vi.fn()}
      />,
    )
    await waitFor(() => expect(hookMock.searchPeople).toHaveBeenCalled())
    expect(hookMock.searchPeople.mock.calls[0][0]).toMatchObject({ excludeEmployeeId: 'EMP001' })
  })
})

// ═══════════════════════════════════════════════════════════════════════════
describe('VikarSection — create / 409 one-active / end', () => {
  it('creates a vikar (no If-Match) and shows the active row', async () => {
    hookMock.createVikar.mockResolvedValue({
      ok: true,
      data: {
        vikarId: 'v1',
        managerId: 'MGR9',
        vikarUserId: 'BO1',
        effectiveFrom: '2026-06-15',
        effectiveTo: '2026-07-01',
        reason: 'FERIE',
      },
    })
    const onChanged = vi.fn()
    wrap(
      <VikarSection managerId="MGR9" managerName="Mette Holm" onChanged={onChanged} />,
    )

    fireEvent.click(screen.getByTestId('vikar-open-form'))
    fireEvent.click(screen.getByTestId('vikar-pick'))
    await waitFor(() => expect(screen.getByTestId('picker-row-BO1')).toBeDefined())
    fireEvent.click(screen.getByTestId('picker-row-BO1'))
    fireEvent.change(screen.getByTestId('vikar-until'), { target: { value: '2026-07-01' } })
    fireEvent.click(screen.getByTestId('vikar-create'))

    await waitFor(() => expect(hookMock.createVikar).toHaveBeenCalled())
    const [managerId, body] = hookMock.createVikar.mock.calls[0]
    expect(managerId).toBe('MGR9')
    expect(body).toMatchObject({ vikarUserId: 'BO1', effectiveTo: '2026-07-01', reason: 'FERIE' })
    await waitFor(() => expect(screen.getByTestId('vikar-active').textContent).toContain('Bo Dahl'))
    expect(onChanged).toHaveBeenCalled()
  })

  it('409 (already has an active vikar) surfaces the honest Danish message', async () => {
    hookMock.createVikar.mockResolvedValue({
      ok: false,
      status: 409,
      error: 'Manager already has an active vikar; revoke it first',
    })
    wrap(<VikarSection managerId="MGR9" managerName="Mette Holm" onChanged={vi.fn()} />)

    fireEvent.click(screen.getByTestId('vikar-open-form'))
    fireEvent.click(screen.getByTestId('vikar-pick'))
    await waitFor(() => expect(screen.getByTestId('picker-row-BO1')).toBeDefined())
    fireEvent.click(screen.getByTestId('picker-row-BO1'))
    fireEvent.change(screen.getByTestId('vikar-until'), { target: { value: '2026-07-01' } })
    fireEvent.click(screen.getByTestId('vikar-create'))

    await waitFor(() =>
      expect(screen.getByText(/allerede en aktiv vikar/i)).toBeDefined(),
    )
  })

  it('400 (cross-tree/coverage/cycle) surfaces a distinct honest message', async () => {
    hookMock.createVikar.mockResolvedValue({ ok: false, status: 400, error: 'bad' })
    wrap(<VikarSection managerId="MGR9" managerName="Mette Holm" onChanged={vi.fn()} />)
    fireEvent.click(screen.getByTestId('vikar-open-form'))
    fireEvent.click(screen.getByTestId('vikar-pick'))
    await waitFor(() => expect(screen.getByTestId('picker-row-BO1')).toBeDefined())
    fireEvent.click(screen.getByTestId('picker-row-BO1'))
    fireEvent.change(screen.getByTestId('vikar-until'), { target: { value: '2026-07-01' } })
    fireEvent.click(screen.getByTestId('vikar-create'))
    await waitFor(() => expect(screen.getByText(/samme styrelse/i)).toBeDefined())
  })

  it('Afslut ends the active vikar', async () => {
    hookMock.endVikar.mockResolvedValue({ ok: true, data: undefined })
    const onChanged = vi.fn()
    wrap(
      <VikarSection
        managerId="MGR9"
        managerName="Mette Holm"
        activeVikar={{
          vikarUserId: 'BO1',
          vikarDisplayName: 'Bo Dahl',
          untilDate: '2026-07-01',
          reason: 'FERIE',
        }}
        onChanged={onChanged}
      />,
    )
    expect(screen.getByTestId('vikar-active').textContent).toContain('Bo Dahl')
    fireEvent.click(screen.getByTestId('vikar-end'))
    await waitFor(() => expect(hookMock.endVikar).toHaveBeenCalledWith('MGR9'))
    await waitFor(() => expect(onChanged).toHaveBeenCalled())
  })
})

// ═══════════════════════════════════════════════════════════════════════════
describe('DangerSection — delete-with-reassignment (BOTH 409s)', () => {
  it('preflight-409 → reassign → in-lock-census SECOND 409 → re-prompt → success', async () => {
    // 1st submit (empty map): preflight-409 lists CHILD1 needing a replacement.
    // 2nd submit ({CHILD1→R1}): the IN-LOCK census surfaces a NEW report CHILD2
    //   (assigned between preflight and commit) → SECOND 409, re-prompt.
    // 3rd submit ({CHILD1→R1, CHILD2→R2}): success.
    hookMock.deletePersonWithReassignment
      .mockResolvedValueOnce({
        ok: false,
        status: 409,
        error: 'Replacement approver required',
        gap: { reportsNeedingReassignment: ['CHILD1'], message: 'Vælg erstatning for CHILD1' },
      })
      .mockResolvedValueOnce({
        ok: false,
        status: 409,
        error: 'A report was assigned concurrently',
        gap: {
          reportsNeedingReassignment: ['CHILD1', 'CHILD2'],
          message: 'En medarbejder blev tildelt samtidig',
        },
      })
      .mockResolvedValueOnce({ ok: true })

    hookMock.searchPeople.mockResolvedValue(
      searchResult([
        { userId: 'R1', displayName: 'Replacement One', primaryOrgName: null, enhedLabel: null },
        { userId: 'R2', displayName: 'Replacement Two', primaryOrgName: null, enhedLabel: null },
      ]),
    )

    const onRemoved = vi.fn()
    wrap(<DangerSection employeeId="EMP001" personName="Test Bruger" onRemoved={onRemoved} />)

    // Open the danger dialog + confirm (empty submit).
    fireEvent.click(screen.getByTestId('danger-open'))
    fireEvent.click(screen.getByTestId('danger-confirm-submit'))

    // PREFLIGHT-409: the gap list shows CHILD1.
    await waitFor(() => expect(screen.getByTestId('danger-reassign')).toBeDefined())
    expect(screen.getByTestId('gap-row-CHILD1')).toBeDefined()
    expect(screen.queryByTestId('gap-row-CHILD2')).toBeNull()
    // Resubmit must be disabled until CHILD1 has a replacement.
    expect((screen.getByTestId('danger-reassign-submit') as HTMLButtonElement).disabled).toBe(true)

    // Pick R1 for CHILD1.
    fireEvent.click(screen.getByTestId('gap-pick-CHILD1'))
    await waitFor(() => expect(screen.getByTestId('picker-row-R1')).toBeDefined())
    fireEvent.click(screen.getByTestId('picker-row-R1'))
    await waitFor(() =>
      expect((screen.getByTestId('danger-reassign-submit') as HTMLButtonElement).disabled).toBe(
        false,
      ),
    )

    // Resubmit → the SECOND (in-lock-census) 409 surfaces CHILD2.
    fireEvent.click(screen.getByTestId('danger-reassign-submit'))
    await waitFor(() => expect(screen.getByTestId('gap-row-CHILD2')).toBeDefined())
    // CHILD1 still listed; CHILD1's selection persisted; CHILD2 needs one.
    expect(screen.getByTestId('gap-row-CHILD1')).toBeDefined()
    expect((screen.getByTestId('danger-reassign-submit') as HTMLButtonElement).disabled).toBe(true)
    expect(
      within(screen.getByTestId('gap-row-CHILD1')).getByText('Replacement One'),
    ).toBeDefined()

    // Pick R2 for CHILD2 → resubmit → success.
    fireEvent.click(screen.getByTestId('gap-pick-CHILD2'))
    await waitFor(() => expect(screen.getByTestId('picker-row-R2')).toBeDefined())
    fireEvent.click(screen.getByTestId('picker-row-R2'))
    fireEvent.click(screen.getByTestId('danger-reassign-submit'))

    await waitFor(() => expect(onRemoved).toHaveBeenCalled())

    // The third submit carried BOTH replacements.
    expect(hookMock.deletePersonWithReassignment).toHaveBeenCalledTimes(3)
    expect(hookMock.deletePersonWithReassignment.mock.calls[0][1]).toEqual({})
    expect(hookMock.deletePersonWithReassignment.mock.calls[1][1]).toEqual({ CHILD1: 'R1' })
    expect(hookMock.deletePersonWithReassignment.mock.calls[2][1]).toEqual({
      CHILD1: 'R1',
      CHILD2: 'R2',
    })
  })

  it('a person who approves no one is removed directly (empty submit → success)', async () => {
    hookMock.deletePersonWithReassignment.mockResolvedValueOnce({ ok: true })
    const onRemoved = vi.fn()
    wrap(<DangerSection employeeId="EMP001" personName="Test Bruger" onRemoved={onRemoved} />)
    fireEvent.click(screen.getByTestId('danger-open'))
    fireEvent.click(screen.getByTestId('danger-confirm-submit'))
    await waitFor(() => expect(onRemoved).toHaveBeenCalled())
    expect(hookMock.deletePersonWithReassignment).toHaveBeenCalledWith('EMP001', {})
  })

  it('a 400 (transferred report) keeps the dialog open with an honest message', async () => {
    hookMock.deletePersonWithReassignment.mockResolvedValueOnce({
      ok: false,
      status: 400,
      error: 'transferred',
    })
    const onRemoved = vi.fn()
    wrap(<DangerSection employeeId="EMP001" personName="Test Bruger" onRemoved={onRemoved} />)
    fireEvent.click(screen.getByTestId('danger-open'))
    fireEvent.click(screen.getByTestId('danger-confirm-submit'))
    await waitFor(() => expect(screen.getByText(/flyttet til en anden styrelse/i)).toBeDefined())
    expect(onRemoved).not.toHaveBeenCalled()
    // Still on the confirm step (no gap list).
    expect(screen.getByTestId('danger-confirm')).toBeDefined()
  })
})

// ═══════════════════════════════════════════════════════════════════════════
// BLOCKER 3 — LifecycleSections must hydrate the active vikar from the single-
// manager GET when opened from the UserManagement LIST (no tree context). An
// away-manager who approves ≥1 report must show their vikar + be able to revoke.
// ═══════════════════════════════════════════════════════════════════════════
const report = (id: string): DirectReport => ({
  reportingLineId: `rl-${id}`,
  employeeId: id,
  employeeDisplayName: id,
  managerId: 'MGR9',
  organisationId: 'ORG1',
  relationship: 'PRIMARY',
  effectiveFrom: '2026-06-15',
  effectiveTo: null,
  source: 'ADMIN',
  version: 1,
  createdBy: 'ADMIN1',
  createdAt: '2026-06-15T00:00:00Z',
})

describe('LifecycleSections — vikar hydration from the single-manager read (BLOCKER 3)', () => {
  it('hydrates the active vikar via fetchActiveVikar when no tree context supplies it', async () => {
    // The manager approves one report → the vikar section renders. With NO tree
    // context, the active vikar is read from GET .../{managerId}/vikar.
    hookMock.fetchDirectReports.mockResolvedValue({ ok: true, data: [report('CHILD1')] })
    hookMock.fetchActiveVikar.mockResolvedValue({
      ok: true,
      data: {
        activeVikar: {
          vikarUserId: 'BO1',
          vikarDisplayName: 'Bo Dahl',
          untilDate: '2026-07-01',
          reason: 'FERIE',
        },
      },
    })

    wrap(<LifecycleSections mode="edit" employeeId="MGR9" personName="Mette Holm" />)

    // The single-manager vikar read fired for the manager, and the active row shows.
    await waitFor(() => expect(hookMock.fetchActiveVikar).toHaveBeenCalledWith('MGR9'))
    await waitFor(() =>
      expect(screen.getByTestId('vikar-active').textContent).toContain('Bo Dahl'),
    )
    // The revoke affordance is present (the away-manager can now revoke).
    expect(screen.getByTestId('vikar-end')).toBeDefined()
  })

  it('does NOT call the single-manager read when the tree context already supplies activeVikar', async () => {
    hookMock.fetchDirectReports.mockResolvedValue({ ok: true, data: [report('CHILD1')] })
    wrap(
      <LifecycleSections
        mode="edit"
        employeeId="MGR9"
        personName="Mette Holm"
        context={{
          approvesOthers: true,
          activeVikar: {
            vikarUserId: 'BO1',
            vikarDisplayName: 'Bo Dahl',
            untilDate: '2026-07-01',
            reason: 'FERIE',
          },
        }}
      />,
    )
    await waitFor(() => expect(screen.getByTestId('vikar-active').textContent).toContain('Bo Dahl'))
    // The context supplied it → no wasted single-manager read.
    expect(hookMock.fetchActiveVikar).not.toHaveBeenCalled()
  })

  it('skips the vikar read for a person who approves no one', async () => {
    hookMock.fetchDirectReports.mockResolvedValue({ ok: true, data: [] })
    wrap(<LifecycleSections mode="edit" employeeId="EMP001" personName="Test Bruger" />)
    // The danger section is always present; wait for the resolve to settle.
    await waitFor(() => expect(screen.getByTestId('danger-open')).toBeDefined())
    // No vikar section (approves no one) → the read was not fired.
    expect(screen.queryByTestId('vikar-active')).toBeNull()
    expect(screen.queryByTestId('vikar-open-form')).toBeNull()
    expect(hookMock.fetchActiveVikar).not.toHaveBeenCalled()
  })
})
