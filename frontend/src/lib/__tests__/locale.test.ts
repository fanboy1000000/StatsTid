import { describe, it, expect } from 'vitest'
import { parseDanishNumber, formatDanishNumber, formatYearMonthWord } from '../locale'

// S146 / TASK-14608 (kind N — new helper) — the guarded month formatter behind the flex screens.
describe('formatYearMonthWord', () => {
  it('formats a well-formed month as lower-case Danish: 2026-09 -> september 2026', () => {
    expect(formatYearMonthWord('2026-09')).toBe('september 2026')
    expect(formatYearMonthWord('2026-01')).toBe('januar 2026')
    expect(formatYearMonthWord('2026-12')).toBe('december 2026')
  })

  it('returns null for month 0 and 13 (out of range)', () => {
    expect(formatYearMonthWord('2026-00')).toBeNull()
    expect(formatYearMonthWord('2026-13')).toBeNull()
  })

  it('returns null for garbage and never throws', () => {
    for (const bad of ['', 'garbage', '2026-9', '2026-09-01', '26-09', ' 2026-09', 'abcd-ef']) {
      expect(() => formatYearMonthWord(bad)).not.toThrow()
      expect(formatYearMonthWord(bad)).toBeNull()
    }
  })

  it('returns null for null and undefined', () => {
    expect(formatYearMonthWord(null)).toBeNull()
    expect(formatYearMonthWord(undefined)).toBeNull()
  })
})

describe('parseDanishNumber', () => {
  it('parses a Danish decimal comma', () => {
    expect(parseDanishNumber('7,4')).toBe(7.4)
  })

  it('parses a plain dot decimal unchanged', () => {
    expect(parseDanishNumber('7.4')).toBe(7.4)
  })

  it('parses an integer', () => {
    expect(parseDanishNumber('8')).toBe(8)
  })

  it('parses zero', () => {
    expect(parseDanishNumber('0')).toBe(0)
    expect(parseDanishNumber('0,0')).toBe(0)
  })

  it('returns NaN for non-numeric input (caller-handled)', () => {
    expect(Number.isNaN(parseDanishNumber(''))).toBe(true)
    expect(Number.isNaN(parseDanishNumber('abc'))).toBe(true)
  })
})

describe('formatDanishNumber', () => {
  it('uses a Danish comma and trims trailing ,0', () => {
    expect(formatDanishNumber(7)).toBe('7')
    expect(formatDanishNumber(7.4)).toBe('7,4')
  })

  it('respects the decimals argument', () => {
    expect(formatDanishNumber(5.92, 2)).toBe('5,92')
  })
})
