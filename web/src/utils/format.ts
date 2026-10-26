import Decimal from 'decimal.js'

export const MaximumAmount = 1_000_000_000

export function formatMoney(value: Decimal.Value | null | undefined): string {
  if (value === null || value === undefined) {
    return '—'
  }

  return formatFixedDecimal(value, 2)
}

export function formatQuantity(value: Decimal.Value | null | undefined): string {
  if (value === null || value === undefined) {
    return '—'
  }

  return formatFixedDecimal(value, 4)
}

export function formatSignedMoney(value: Decimal.Value | null | undefined): string {
  if (value === null || value === undefined) {
    return '—'
  }

  const amount = new Decimal(value)

  if (amount.isZero()) {
    return formatMoney(0)
  }

  return `${amount.isPositive() ? '+' : '−'}${formatMoney(amount.abs())}`
}

export function formatBusinessDate(value: string): string {
  const parts = value.split('-')

  if (parts.length !== 3) {
    return value
  }

  const year = Number(parts[0])
  const month = Number(parts[1])
  const day = Number(parts[2])

  if (!Number.isInteger(year) || !Number.isInteger(month) || !Number.isInteger(day)) {
    return value
  }

  return `${year}年${month}月${day}日`
}

export function todayInBusinessTimeZone(): string {
  const parts = new Intl.DateTimeFormat('en-US', {
    timeZone: 'Asia/Shanghai',
    year: 'numeric',
    month: '2-digit',
    day: '2-digit'
  }).formatToParts(new Date())
  const year = parts.find((part) => part.type === 'year')?.value
  const month = parts.find((part) => part.type === 'month')?.value
  const day = parts.find((part) => part.type === 'day')?.value

  if (!year || !month || !day) {
    return ''
  }

  return `${year}-${month}-${day}`
}

export function parseAmount(value: string): number | null {
  return toNumber(parseDecimal(value, 2))
}

export function parseQuantity(value: string): number | null {
  const quantity = parseDecimal(value, 4)

  if (quantity === null || !quantity.isPositive() || quantity.greaterThan(1_000_000)) {
    return null
  }

  return quantity.toNumber()
}

export function parseNonNegativeQuantity(value: string): number | null {
  const quantity = parseDecimal(value, 4)

  if (quantity === null || quantity.isNegative() || quantity.greaterThan(1_000_000)) {
    return null
  }

  return quantity.toNumber()
}

export function parsePrice(value: string): number | null {
  const price = parseDecimal(value, 4)

  if (price === null || !price.isPositive()) {
    return null
  }

  return price.toNumber()
}

function parseDecimal(value: string, maximumFractionDigits: number): Decimal | null {
  const normalized = value.trim().replaceAll(',', '')
  const pattern = new RegExp(`^\\d+(?:\\.\\d{1,${maximumFractionDigits}})?$`)

  if (!pattern.test(normalized)) {
    return null
  }

  const amount = new Decimal(normalized)

  if (!amount.isFinite() || amount.greaterThan(MaximumAmount)) {
    return null
  }

  return amount
}

function toNumber(value: Decimal | null): number | null {
  return value?.toNumber() ?? null
}

export function sumAmounts(values: readonly (Decimal.Value | null | undefined)[]): number {
  return values.reduce<Decimal>((sum, value) => sum.plus(value ?? 0), new Decimal(0)).toNumber()
}

export function addAmounts(...values: Decimal.Value[]): number {
  return sumAmounts(values)
}

export function subtractAmounts(left: Decimal.Value, right: Decimal.Value): number {
  return new Decimal(left).minus(right).toNumber()
}

function formatFixedDecimal(value: Decimal.Value, fractionDigits: number): string {
  const fixed = new Decimal(value).toFixed(fractionDigits)
  const [integer, fraction] = fixed.split('.')
  const groupedInteger = integer.replace(/\B(?=(\d{3})+(?!\d))/g, ',')

  return `${groupedInteger}.${fraction}`
}
