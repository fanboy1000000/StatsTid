// TASK-14115 (S141 wave 3) — before this fix, `CannotRegisterList` always
// captioned the list "no agreement code covers today" and told HR to fix
// the agreement code, even for a row where the actual gap was the
// employment PROFILE (wave 2's HRP-015 widening added `missingRecord` so the
// list can say which). Worse, it never distinguished a genuine gap from one
// a scheduled record will close on its own (`coveredFrom`) — presenting a
// self-healing row as broken risks HR "fixing" it by deleting a colleague's
// already-scheduled change. These pins are written to FAIL if either
// regresses.
import { describe, it, expect } from 'vitest'
import { render, screen } from '@testing-library/react'
import { MemoryRouter } from 'react-router-dom'
import { CannotRegisterList } from '../FollowUpLists'
import type { HrCannotRegisterEmployee, HrCannotRegisterResponse } from '../../../../hooks/useHrFollowUp'

function renderList(data: HrCannotRegisterResponse | null) {
  return render(
    <MemoryRouter>
      <CannotRegisterList data={data} loading={false} error={null} />
    </MemoryRouter>,
  )
}

function baseItem(over: Partial<HrCannotRegisterEmployee> = {}): HrCannotRegisterEmployee {
  return {
    employeeId: 'e1',
    displayName: 'Anna And',
    orgId: 'org1',
    unitName: 'Vejledning',
    missingRecord: 'AGREEMENT_CODE',
    gapSince: '2025-09-01',
    daysSinceGapStart: 10,
    coveredFrom: null,
    ...over,
  }
}

function response(items: HrCannotRegisterEmployee[]): HrCannotRegisterResponse {
  return { today: '2025-09-11', count: items.length, oldestGapSince: items[0]?.gapSince ?? null, items }
}

describe('CannotRegisterList', () => {
  it('names the agreement code as the missing record and flags a genuine gap as needing action', () => {
    renderList(response([baseItem()]))
    expect(screen.getByTestId('list-cannot-register-missing-e1')).toHaveTextContent('Overenskomstkode')
    expect(screen.getByTestId('list-cannot-register-status-e1')).toHaveTextContent('Skal rettes')
  })

  it('names the employment profile as missing, not the agreement code, when that is the actual gap', () => {
    renderList(response([baseItem({ missingRecord: 'EMPLOYMENT_PROFILE' })]))
    const cell = screen.getByTestId('list-cannot-register-missing-e1')
    expect(cell).toHaveTextContent('Ansættelsesprofil')
    expect(cell).not.toHaveTextContent('Overenskomstkode')
  })

  it('names both records when both are missing', () => {
    renderList(response([baseItem({ missingRecord: 'BOTH' })]))
    expect(screen.getByTestId('list-cannot-register-missing-e1')).toHaveTextContent('Overenskomstkode og ansættelsesprofil')
  })

  it('falls back to the raw value for an unrecognised missingRecord rather than mislabeling it', () => {
    renderList(response([baseItem({ missingRecord: 'SOMETHING_NEW' })]))
    expect(screen.getByTestId('list-cannot-register-missing-e1')).toHaveTextContent('SOMETHING_NEW')
  })

  it('never tells HR a row is broken when a scheduled record will cover it again — it shows the scheduled date instead', () => {
    renderList(response([baseItem({ coveredFrom: '2025-11-01' })]))
    const status = screen.getByTestId('list-cannot-register-status-e1')
    expect(status).toHaveTextContent('Planlagt fra')
    expect(status).toHaveTextContent('1. november 2025')
    expect(status).not.toHaveTextContent('Skal rettes')
  })

  it('still flags a row as needing action when only ONE side of a BOTH gap has anything scheduled (coveredFrom stays null)', () => {
    // Matches the backend contract (HrFollowUpApprovalReadRepository.HrCannotRegisterItem):
    // CoveredFrom is non-null only once EVERY missing side has a scheduled row.
    renderList(response([baseItem({ missingRecord: 'BOTH', coveredFrom: null })]))
    expect(screen.getByTestId('list-cannot-register-status-e1')).toHaveTextContent('Skal rettes')
  })

  it('captions the list generically, not as an agreement-code-only problem', () => {
    renderList(response([baseItem()]))
    expect(
      screen.getByText('Medarbejdere uden en overenskomstkode eller ansættelsesprofil, der dækker i dag.'),
    ).toBeInTheDocument()
  })

  it('the empty state also reads generically, not as an agreement-code-only problem', () => {
    renderList(response([]))
    expect(screen.getByTestId('list-cannot-register-empty')).toHaveTextContent(
      'Ingen medarbejdere mangler en dækkende overenskomstkode eller ansættelsesprofil.',
    )
  })

  it('points the action note at both possible fixes and calls out that a scheduled row needs no action', () => {
    renderList(response([baseItem()]))
    const note = screen.getByTestId('list-cannot-register-action-note')
    expect(note).toHaveTextContent(/overenskomstkode eller ansættelsesprofil/i)
    expect(note).toHaveTextContent(/planlagt dækning/i)
  })
})
