// AC-C4i (TASK-14833). Kind F.
// Red conditions: before the change the expanded history row lists four lines only, so the first
// new line's assertion (the plus cap's "37 timer") fails. A row holding none of the new settings
// shows "(ikke angivet)" on each of the nine, and its four existing lines are unchanged.
import { describe, it, expect } from 'vitest'
import { render, screen, within } from '@testing-library/react'
import { RowWithDelta } from '../ConfigManagement'
import type { LocalAgreementProfile } from '../../../hooks/useConfig'

const base: LocalAgreementProfile = {
  profileId: '22222222-2222-2222-2222-222222222222',
  orgId: 'ORG1',
  agreementCode: 'AC',
  okVersion: 'OK24',
  effectiveFrom: '2026-01-05',
  effectiveTo: '2026-03-02',
  weeklyNormHours: null,
  maxFlexBalance: 30,
  maxOvertimeHoursPerPeriod: null,
  overtimeRequiresPreApproval: null,
  createdBy: 'admin@example.dk',
  createdAt: '2026-01-05T08:00:00Z',
  version: 1,
  flexSettlementPeriod: null,
  flexCapTestPeriod: null,
  flexPlusCapHours: null,
  flexMinusCapHours: null,
  flexDailyCreditCapHours: null,
  flexAboveCapAction: null,
  flexLeavingAction: null,
  officeHoursMonStart: null,
  officeHoursMonEnd: null,
  officeHoursTueStart: null,
  officeHoursTueEnd: null,
  officeHoursWedStart: null,
  officeHoursWedEnd: null,
  officeHoursThuStart: null,
  officeHoursThuEnd: null,
  officeHoursFriStart: null,
  officeHoursFriEnd: null,
  paidAbsenceCreditBasis: null,
}

const full: LocalAgreementProfile = {
  ...base,
  flexSettlementPeriod: 'MONTH',
  flexCapTestPeriod: 'MONTH',
  flexPlusCapHours: 37,
  flexMinusCapHours: 10.5,
  flexDailyCreditCapHours: 13,
  flexAboveCapAction: 'FORFEIT',
  flexLeavingAction: 'FORFEIT_POSITIVE_RECORD_NEGATIVE',
  officeHoursMonStart: '08:00:00',
  officeHoursMonEnd: '15:30:00',
  officeHoursTueStart: '08:00:00',
  officeHoursTueEnd: '15:30:00',
  officeHoursWedStart: '08:00:00',
  officeHoursWedEnd: '15:30:00',
  officeHoursThuStart: '08:00:00',
  officeHoursThuEnd: '15:30:00',
  officeHoursFriStart: '08:00:00',
  officeHoursFriEnd: '14:00:00',
  paidAbsenceCreditBasis: 'OFFICE_HOURS',
}

function lines(row: LocalAgreementProfile): string[] {
  render(
    <table>
      <tbody>
        <RowWithDelta row={row} expanded={true} onToggle={() => {}} />
      </tbody>
    </table>,
  )
  return within(screen.getByRole('list')).getAllByRole('listitem').map(li => li.textContent ?? '')
}

describe('History row — the new settings', () => {
  it('lists the nine new lines under the four existing ones', () => {
    expect(lines(full)).toEqual([
      'WeeklyNormHours: (arvet fra central)',
      'MaxFlexBalance: 30',
      'MaxOvertimeHoursPerPeriod: (arvet fra central)',
      'OvertimeRequiresPreApproval: (arvet fra central)',
      'Afregningsperiode for flex: Kalendermåned',
      'Periode, som flexloftet måles over: Kalendermåned',
      'Loft for plussaldo på flex (timer): 37 timer',
      'Loft for minussaldo på flex (timer, uden minustegn): 10,5 timer',
      'Loft for daglig optjening af flex (timer): 13 timer',
      'Timer over loftet for plussaldo: Bortfalder',
      'Flexsaldo ved fratræden: En positiv saldo bortfalder, en negativ registreres og modregnes ikke',
      'Kontortid: mandag 08:00–15:30, tirsdag 08:00–15:30, onsdag 08:00–15:30, torsdag 08:00–15:30, fredag 08:00–14:00',
      'Grundlag for godskrivning af betalt fravær: Kontortid: ugedagens kontortid',
    ])
  })

  it('shows "(ikke angivet)" on each of the nine for a row holding none', () => {
    const got = lines(base)
    expect(got.slice(0, 4)).toEqual([
      'WeeklyNormHours: (arvet fra central)',
      'MaxFlexBalance: 30',
      'MaxOvertimeHoursPerPeriod: (arvet fra central)',
      'OvertimeRequiresPreApproval: (arvet fra central)',
    ])
    expect(got).toHaveLength(13)
    for (const l of got.slice(4)) expect(l.endsWith(': (ikke angivet)')).toBe(true)
  })
})
