// S147 C-1 (owner ruling 2026-11-04, option a): ONE wording per context.
//  - balance and quota surfaces: the PLURAL balance names, equal to the server's
//    BalanceEndpoints.DanishLabels (BalanceEndpoints.cs:16-23);
//  - admin type pickers: the SINGULAR type names;
//  - the child's sick day is "Barns sygedag" in both (admin's old "Barnets sygedag" is gone).
// Kinds (against 94bd32be): the singular-map fact is F (its CHILD_SICK entry said "Barnets sygedag"
// there). The other three name exports this sprint creates (BALANCE_LABELS, entitlementBalanceLabel),
// so they are N: each is proved RED by ONE named mutation, never by an import failure.
import { describe, it, expect } from 'vitest'
import {
  BALANCE_LABELS,
  TYPE_LABELS,
  TYPE_OPTIONS,
  entitlementBalanceLabel,
  entitlementTypeLabel,
} from '../entitlementConstants'

describe('entitlement wording, one per context', () => {
  // N. Mutation M-1: BALANCE_LABELS.SPECIAL_HOLIDAY = 'Særlig feriedag' (the singular). Red: toEqual
  // fails on that entry.
  it('N: balance and quota surfaces use the server\'s plural balance names', () => {
    expect(BALANCE_LABELS).toEqual({
      VACATION: 'Ferie',
      SPECIAL_HOLIDAY: 'Særlige feriedage',
      CARE_DAY: 'Omsorgsdage',
      CHILD_SICK: 'Barns sygedag',
      SENIOR_DAY: 'Seniordage',
    })
  })

  // Red conditions: before C-1 the CHILD_SICK entry read "Barnets sygedag".
  it('F: admin type pickers use the singular type names, and the child\'s sick day is "Barns sygedag"', () => {
    expect(TYPE_LABELS).toEqual({
      VACATION: 'Ferie',
      SPECIAL_HOLIDAY: 'Særlig feriedag',
      CARE_DAY: 'Omsorgsdag',
      CHILD_SICK: 'Barns sygedag',
      SENIOR_DAY: 'Seniordag',
    })
  })

  // N. Mutation M-2: delete the SENIOR_DAY entry from BALANCE_LABELS. Red: the key list is one short.
  it('N: both maps cover exactly the five entitlement types', () => {
    expect(Object.keys(BALANCE_LABELS).sort()).toEqual([...TYPE_OPTIONS].sort())
    expect(Object.keys(TYPE_LABELS).sort()).toEqual([...TYPE_OPTIONS].sort())
  })

  // N. Mutation M-3: entitlementBalanceLabel reads TYPE_LABELS instead of BALANCE_LABELS. Red:
  // 'Omsorgsdag' is returned where 'Omsorgsdage' is expected.
  it('N: the label helpers fall back to the raw value for an unknown type', () => {
    expect(entitlementBalanceLabel('CARE_DAY')).toBe('Omsorgsdage')
    expect(entitlementTypeLabel('CARE_DAY')).toBe('Omsorgsdag')
    expect(entitlementBalanceLabel('SOMETHING_NEW')).toBe('SOMETHING_NEW')
  })
})
