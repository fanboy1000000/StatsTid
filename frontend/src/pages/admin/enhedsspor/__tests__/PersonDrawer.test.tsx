// SPRINT-109 / TASK-10901 — the Person drawer renders the design §3 fields and the
// Placering options RELOAD when the Organisation changes. Rendered in CREATE mode:
// the reused LifecycleSections fires NO reporting-lines GETs in create mode (its
// resolve effect early-returns), so the drawer renders fully offline. A real
// ToastProvider satisfies the reused cores' useToast; useAuth is a LocalHR mock.

import { describe, it, expect, vi, beforeEach } from 'vitest'
import { render, screen, fireEvent, waitFor } from '@testing-library/react'
import { ToastProvider } from '../../../../components/ui/Toast'
import type { ForestMaoNode } from '../../../../hooks/useForest'
import { orgsFromForest } from '../personDrawerData'

const auth = vi.hoisted(() => ({ role: 'LocalHR' as string | null }))
vi.mock('../../../../contexts/AuthContext', () => ({
  useAuth: () => ({ role: auth.role }),
}))

// S124 / TASK-12401 — the picker's Organisation scoping is asserted here, at the DRAWER, not at
// PersonPickerDialog: the interesting question is not "is the prop forwarded" but "which org does
// each MODE choose", and only the drawer owns both candidate values (the draft `stamdata` org and
// the persisted `user` org). The hook is mocked so the picker's search is observable; the other
// tests in this file never touch it, so the mock is inert for them.
const rlMock = vi.hoisted(() => ({
  searchPeople: vi.fn(),
  assignManager: vi.fn(),
  removeManager: vi.fn(),
  createVikar: vi.fn(),
  endVikar: vi.fn(),
  fetchActiveVikar: vi.fn(),
  fetchEmployeeLines: vi.fn(),
  fetchDirectReports: vi.fn(),
  deletePersonWithReassignment: vi.fn(),
}))
vi.mock('../../../../hooks/useReportingLines', async (importActual) => ({
  ...(await importActual<typeof import('../../../../hooks/useReportingLines')>()),
  useReportingLines: () => rlMock,
}))

/** The org id the picker actually asked the server for, from the latest search call. */
const searchedOrg = (): string | undefined => {
  const calls = rlMock.searchPeople.mock.calls
  if (calls.length === 0) return undefined
  return (calls[calls.length - 1][0] as { organisationId?: string }).organisationId
}

import { PersonDrawer } from '../PersonDrawer'

const VEJL = '000000d0-0000-0000-0000-0000000000a1'
const KONTROL = '000000d0-0000-0000-0000-0000000000a2'
const INDKOEB = '000000d0-0000-0000-0000-0000000000b1'

function makeForest(): ForestMaoNode[] {
  return [
    {
      orgId: 'MIN01',
      orgName: 'Finansministeriet',
      orgType: 'MAO',
      parentOrgId: null,
      materializedPath: '/MIN01/',
      memberCount: 0,
      organisations: [
        {
          orgId: 'STY02', orgName: 'Statens IT', orgType: 'ORGANISATION', parentOrgId: 'MIN01',
          materializedPath: '/MIN01/STY02/', agreementCode: 'HK', okVersion: 'OK24',
          memberCount: 0, directMemberCount: 0,
          units: [
            {
              unitId: VEJL, organisationId: 'STY02', parentUnitId: null, type: 'kontor', name: 'Vejledning',
              level: 1, version: 1, directMemberCount: 0, memberCount: 0,
              children: [
                { unitId: KONTROL, organisationId: 'STY02', parentUnitId: VEJL, type: 'team', name: 'Kontrol', level: 2, version: 1, directMemberCount: 0, memberCount: 0, children: [] },
              ],
            },
          ],
        },
        {
          orgId: 'STY03', orgName: 'Statens Indkøb', orgType: 'ORGANISATION', parentOrgId: 'MIN01',
          materializedPath: '/MIN01/STY03/', agreementCode: 'AC', okVersion: 'OK24',
          memberCount: 0, directMemberCount: 0,
          units: [
            { unitId: INDKOEB, organisationId: 'STY03', parentUnitId: null, type: 'kontor', name: 'Indkøb', level: 1, version: 1, directMemberCount: 0, memberCount: 0, children: [] },
          ],
        },
      ],
    },
  ]
}

const optionTexts = (testid: string): string[] =>
  Array.from((screen.getByTestId(testid) as HTMLSelectElement).options).map((o) => (o.textContent ?? '').trim())

function renderCreate(defaultUnitId: string | null = VEJL) {
  const forest = makeForest()
  return render(
    <ToastProvider>
      <PersonDrawer
        open
        organizations={orgsFromForest(forest)}
        forest={forest}
        defaultOrgId="STY02"
        defaultUnitId={defaultUnitId}
        onClose={vi.fn()}
        onSaved={vi.fn()}
      />
    </ToastProvider>,
  )
}

beforeEach(() => {
  auth.role = 'LocalHR'
  rlMock.searchPeople.mockReset()
  rlMock.searchPeople.mockResolvedValue({
    ok: true,
    data: { items: [], total: 0, limit: 60, offset: 0 },
  })
  rlMock.fetchEmployeeLines.mockResolvedValue({ ok: true, data: { active: [], history: [] } })
  rlMock.fetchDirectReports.mockResolvedValue({ ok: true, data: [] })
  rlMock.fetchActiveVikar.mockResolvedValue({ ok: true, data: { activeVikar: null } })
})

describe('PersonDrawer — the design §3 fields + the Placering reload', () => {
  it('renders the create-mode fields (credentials, Navn/E-mail/Organisation, Placering, apex, promote, Nærmeste leder)', () => {
    renderCreate()
    expect(screen.getByTestId('person-drawer-title').textContent).toBe('Opret medarbejder')
    // credentials (create-only)
    expect(screen.getByTestId('pd-create-user-id')).toBeDefined()
    expect(screen.getByTestId('pd-create-username')).toBeDefined()
    expect(screen.getByTestId('pd-create-password')).toBeDefined()
    // stamdata (reused)
    expect(screen.getByTestId('ep-display-name')).toBeDefined()
    expect(screen.getByTestId('ep-email')).toBeDefined()
    expect(screen.getByTestId('ep-primary-org')).toBeDefined()
    // S109 unit fields
    expect(screen.getByTestId('pd-placement')).toBeDefined()
    expect(screen.getByTestId('pd-apex')).toBeDefined()
    expect(screen.getByTestId('pd-promote')).toBeDefined()
    // the reused Nærmeste-leder (ApproverSection) renders its "Godkendes af" row.
    expect(screen.getByText('Godkendes af')).toBeDefined()
  })

  it('derives the Placering options from the forest for the chosen Organisation (incl. null = org-home), nested', () => {
    renderCreate()
    expect(optionTexts('pd-placement')).toEqual(['Direkte under organisationen', 'Vejledning', 'Kontrol'])
    // pre-selected to the unit the "+ Medarbejder" was opened on.
    expect((screen.getByTestId('pd-placement') as HTMLSelectElement).value).toBe(VEJL)
    // promote is enabled + labels the chosen unit.
    expect((screen.getByTestId('pd-promote') as HTMLInputElement).disabled).toBe(false)
    expect(screen.getByText(/Er leder af\s+Vejledning/)).toBeDefined()
  })

  it('RELOADS the Placering options when the Organisation changes (and resets to org-home + disables promote)', () => {
    renderCreate()
    fireEvent.change(screen.getByTestId('ep-primary-org'), { target: { value: 'STY03' } })
    // the STY02 units are gone; STY03's unit appears.
    expect(optionTexts('pd-placement')).toEqual(['Direkte under organisationen', 'Indkøb'])
    // the selection reset to org-home (a unit in the old org is no longer valid).
    expect((screen.getByTestId('pd-placement') as HTMLSelectElement).value).toBe('')
    // with no unit chosen, promote is disabled.
    expect((screen.getByTestId('pd-promote') as HTMLInputElement).disabled).toBe(true)
  })

  it('homes directly under the Organisation when defaultUnitId is null (promote disabled)', () => {
    renderCreate(null)
    expect((screen.getByTestId('pd-placement') as HTMLSelectElement).value).toBe('')
    expect((screen.getByTestId('pd-promote') as HTMLInputElement).disabled).toBe(true)
  })
})

// ═══════════════════════════════════════════════════════════════════════════════════
// S124 / TASK-12401 — WHICH Organisation the godkender picker searches.
//
// The rule is one sentence: scope to the Organisation the SERVER will validate the
// resulting edge against. That is a DIFFERENT field per mode, which is the whole
// subtlety and the reason these tests exist:
//   • CREATE — the approver rides in the create POST, which carries the DRAFT org and
//     validates against it ⇒ follow the draft, live.
//   • EDIT   — the assign is an IMMEDIATE POST validated against the PERSISTED org.
//     A cross-Organisation transfer is a first-class flow, so the select can be dirty;
//     following the draft there would list the new org's people and then 400 on pick —
//     reinstating the dishonest picker this task removed.
// ═══════════════════════════════════════════════════════════════════════════════════
describe('PersonDrawer — the godkender picker is Organisation-scoped', () => {
  it('CREATE: searches the DRAFT organisation, and FOLLOWS it when the Organisation changes', async () => {
    renderCreate()

    fireEvent.click(screen.getByTestId('approver-assign'))
    await waitFor(() => expect(rlMock.searchPeople).toHaveBeenCalled())
    expect(searchedOrg()).toBe('STY02') // the draft org as opened

    // Re-target the draft org; the OPEN picker must re-search against the new one.
    fireEvent.change(screen.getByTestId('ep-primary-org'), { target: { value: 'STY03' } })
    await waitFor(() => expect(searchedOrg()).toBe('STY03'))
  })

  it('CREATE: changing the Organisation DISCARDS a picked approver and says why', async () => {
    rlMock.searchPeople.mockResolvedValue({
      ok: true,
      data: {
        items: [{ userId: 'U1', displayName: 'Bo Dahl', primaryOrgName: 'Statens IT' }],
        total: 1,
        limit: 60,
        offset: 0,
      },
    })
    renderCreate()

    fireEvent.click(screen.getByTestId('approver-assign'))
    await waitFor(() => expect(screen.getByTestId('picker-row-U1')).toBeDefined())
    fireEvent.click(screen.getByTestId('picker-row-U1'))
    await waitFor(() => expect(screen.getByTestId('approver-assigned')).toBeDefined())

    // The pick was made under STY02; STY03 would 400 on the create POST.
    fireEvent.change(screen.getByTestId('ep-primary-org'), { target: { value: 'STY03' } })

    await waitFor(() => expect(screen.queryByTestId('approver-assigned')).toBeNull())
    // Silently emptying a field the user filled reads as a bug — it must be explained.
    expect(screen.getByTestId('approver-notice').textContent).toContain('organisationen blev ændret')
  })

  it('EDIT: keeps searching the PERSISTED organisation while an unsaved transfer is pending', async () => {
    const forest = makeForest()
    render(
      <ToastProvider>
        <PersonDrawer
          open
          user={{
            userId: 'EMP1',
            username: 'emp1',
            displayName: 'Karen Nielsen',
            email: 'k@x.dk',
            role: 'Employee',
            primaryOrgId: 'STY02', // the PERSISTED org — what the server validates against
            agreementCode: 'HK',
            isActive: true,
          } as never}
          organizations={orgsFromForest(forest)}
          forest={forest}
          currentUnitId={null}
          onClose={vi.fn()}
          onSaved={vi.fn()}
        />
      </ToastProvider>,
    )

    // EDIT mode hydrates the HR profile before enabling its controls; wait that out, or the click
    // lands on a still-disabled button and the test silently proves nothing.
    await waitFor(() =>
      expect((screen.getByTestId('approver-assign') as HTMLButtonElement).disabled).toBe(false),
    )

    // Stage a cross-Organisation transfer WITHOUT saving it.
    fireEvent.change(screen.getByTestId('ep-primary-org'), { target: { value: 'STY03' } })

    fireEvent.click(screen.getByTestId('approver-assign'))
    await waitFor(() => expect(rlMock.searchPeople).toHaveBeenCalled())

    // THE ASSERTION: the persisted STY02, NOT the dirty STY03. Following the draft here is
    // exactly the bug — every STY03 name would be rejected by the immediate assign POST.
    expect(searchedOrg()).toBe('STY02')
    // …and the mismatch with the Organisation shown above is explained rather than mysterious.
    expect(screen.getByTestId('approver-notice').textContent).toContain('indtil overflytningen er gemt')
  })
})
