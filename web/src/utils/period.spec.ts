import { describe, expect, it } from 'vitest'
import { formatPeriodLabel, periodStart, shiftAnchor } from './period'

describe('shiftAnchor', () => {
  it('shifts days across month boundaries', () => {
    expect(shiftAnchor('2026-10-31', 'day', 1)).toBe('2026-11-01')
    expect(shiftAnchor('2026-03-01', 'day', -1)).toBe('2026-02-28')
  })

  it('shifts months across year boundaries', () => {
    expect(shiftAnchor('2026-12-15', 'month', 1)).toBe('2027-01-01')
    expect(shiftAnchor('2026-01-15', 'month', -1)).toBe('2025-12-01')
  })

  it('shifts years', () => {
    expect(shiftAnchor('2026-10-21', 'year', -1)).toBe('2025-01-01')
  })
})

describe('periodStart', () => {
  it('returns the first day of the month and year', () => {
    expect(periodStart('2026-10-21', 'month')).toBe('2026-10-01')
    expect(periodStart('2026-10-21', 'year')).toBe('2026-01-01')
    expect(periodStart('2026-10-21', 'day')).toBe('2026-10-21')
  })
})

describe('formatPeriodLabel', () => {
  it('formats day, month and year labels', () => {
    expect(formatPeriodLabel('2026-10-21', 'day')).toBe('2026年10月21日')
    expect(formatPeriodLabel('2026-10-21', 'month')).toBe('2026年10月')
    expect(formatPeriodLabel('2026-10-21', 'year')).toBe('2026年')
  })
})
