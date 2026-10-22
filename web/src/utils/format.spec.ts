import { describe, expect, it } from 'vitest'
import {
  formatBusinessDate,
  formatMoney,
  formatQuantity,
  formatSignedMoney,
  parseAmount,
  parseNonNegativeQuantity,
  parsePrice,
  parseQuantity,
  todayInBusinessTimeZone
} from './format'

describe('formatMoney', () => {
  it('formats amounts with two decimals', () => {
    expect(formatMoney(23400)).toBe('23,400.00')
  })

  it('shows a dash for missing values', () => {
    expect(formatMoney(null)).toBe('—')
    expect(formatMoney(undefined)).toBe('—')
  })
})

describe('formatSignedMoney', () => {
  it('adds a positive or negative sign and keeps two decimals', () => {
    expect(formatSignedMoney(1200)).toBe('+1,200.00')
    expect(formatSignedMoney(-35.6)).toBe('−35.60')
  })

  it('keeps zero unsigned and shows a dash for missing values', () => {
    expect(formatSignedMoney(0)).toBe('0.00')
    expect(formatSignedMoney(null)).toBe('—')
  })
})

describe('formatQuantity', () => {
  it('formats quantities with four decimals', () => {
    expect(formatQuantity(30)).toBe('30.0000')
    expect(formatQuantity(1.2345)).toBe('1.2345')
    expect(formatQuantity(null)).toBe('—')
  })
})

describe('parseQuantity and parsePrice', () => {
  it('accepts positive values with up to four decimals', () => {
    expect(parseQuantity('1.2345')).toBe(1.2345)
    expect(parsePrice('900.1234')).toBe(900.1234)
  })

  it('rejects zero and over-precise values', () => {
    expect(parseQuantity('0')).toBeNull()
    expect(parseQuantity('1.23456')).toBeNull()
    expect(parsePrice('0.00')).toBeNull()
  })

  it('allows zero for holding snapshots', () => {
    expect(parseNonNegativeQuantity('0')).toBe(0)
    expect(parseNonNegativeQuantity('30.0000')).toBe(30)
    expect(parseNonNegativeQuantity('-1')).toBeNull()
  })
})

describe('parseAmount', () => {
  it('accepts zero and amounts with commas', () => {
    expect(parseAmount('0')).toBe(0)
    expect(parseAmount('12,345.67')).toBe(12345.67)
  })

  it('rejects negative, empty and over-precise values', () => {
    expect(parseAmount('')).toBeNull()
    expect(parseAmount('-1')).toBeNull()
    expect(parseAmount('1.234')).toBeNull()
  })

  it('rejects amounts above the server limit', () => {
    expect(parseAmount('1000000000.01')).toBeNull()
    expect(parseAmount('1000000000')).toBe(1000000000)
  })
})

describe('formatBusinessDate', () => {
  it('formats an ISO date without shifting the day', () => {
    expect(formatBusinessDate('2026-10-01')).toBe('2026年10月1日')
  })

  it('returns the original value when the input is not an ISO date', () => {
    expect(formatBusinessDate('2026-10')).toBe('2026-10')
  })
})

describe('todayInBusinessTimeZone', () => {
  it('returns an ISO date for the Shanghai business day', () => {
    expect(todayInBusinessTimeZone()).toMatch(/^\d{4}-\d{2}-\d{2}$/)
  })
})
