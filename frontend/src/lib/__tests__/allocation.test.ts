import { describe, it, expect } from 'vitest'
import { classifyAllocation, unallocated, ALLOCATION_TOLERANCE } from '../allocation'

describe('classifyAllocation', () => {
  it('treats exactly-equal as balanced', () => {
    expect(classifyAllocation(7.4, 7.4)).toBe('balanced')
  })

  it('treats a sub-tolerance difference as balanced (< 0.005 after 2dp rounding)', () => {
    // 7.40 vs 7.4 — equal after rounding, mirrors the backend 7.40-vs-7.4 tolerance case.
    expect(classifyAllocation(7.404, 7.4)).toBe('balanced')
    expect(ALLOCATION_TOLERANCE).toBe(0.005)
  })

  it('classifies worked > allocated as under (hours still to distribute)', () => {
    expect(classifyAllocation(7.4, 3.0)).toBe('under')
  })

  it('classifies allocated > worked as over (more allocated than registered)', () => {
    expect(classifyAllocation(0, 7.4)).toBe('over')
  })
})

describe('unallocated', () => {
  it('returns worked minus allocated, rounded to 2dp', () => {
    expect(unallocated(7.4, 3.0)).toBe(4.4)
  })

  it('returns 0 for a balanced day', () => {
    expect(unallocated(7.4, 7.4)).toBe(0)
  })

  it('returns a negative value when over-allocated', () => {
    expect(unallocated(3.0, 7.4)).toBe(-4.4)
  })
})
