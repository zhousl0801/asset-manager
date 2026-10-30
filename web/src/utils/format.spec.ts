import { describe, expect, it } from 'vitest'
import {
  amountTone,
  formatBusinessDate,
  formatBusinessTime,
  formatMoney,
  formatQuantity,
  formatSignedMoney,
  formatUnitPrice,
  parseAmount,
  parseNonNegativeQuantity,
  parsePrice,
  parseQuantity,
  parseUnitPrice,
  todayInBusinessTimeZone
} from './format'

describe('formatBusinessTime', () => {
  it('formats an ISO timestamp in Asia/Shanghai', () => {
    expect(formatBusinessTime('2026-10-29T04:01:09Z')).toBe('12:01:09')
  })

  it('returns empty for missing or invalid values', () => {
    expect(formatBusinessTime(null)).toBe('')
    expect(formatBusinessTime('not-a-date')).toBe('')
  })
})

describe('formatMoney', () => {  it('formats amounts with two decimals', () => {
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

describe('amountTone', () => {
  it('maps positive and negative values to gain and loss', () => {
    expect(amountTone(12.5)).toBe('amount-gain')
    expect(amountTone(-0.01)).toBe('amount-loss')
  })

  it('leaves zero and missing values uncolored', () => {
    expect(amountTone(0)).toBe('')
    expect(amountTone(null)).toBe('')
    expect(amountTone(undefined)).toBe('')
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

describe('formatUnitPrice', () => {
  it('keeps two to three decimals for exchange prices', () => {
  expect(formatUnitPrice(2.435)).toBe('2.435')
  expect(formatUnitPrice(2.772)).toBe('2.772')
  expect(formatUnitPrice(10.5)).toBe('10.50')
  expect(formatUnitPrice(1500)).toBe('1,500.00')
  expect(formatUnitPrice(0)).toBe('0.00')
  expect(formatUnitPrice(null)).toBe('—')
  })
})

describe('parseUnitPrice', () => {
  it('accepts zero and up to four decimals', () => {
    expect(parseUnitPrice('2.772')).toBe(2.772)
    expect(parseUnitPrice('0')).toBe(0)
    expect(parseUnitPrice('2.7721')).toBe(2.7721)
    expect(parseUnitPrice('-1')).toBeNull()
    expect(parseUnitPrice('2.77211')).toBeNull()
    expect(parseUnitPrice('')).toBeNull()
  })
})
