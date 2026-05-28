import { describe, it, expect } from 'vitest'
import { parseDanishNumber, formatDanishNumber } from '../locale'

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
