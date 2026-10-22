export const MaximumAmount = 1_000_000_000

const moneyFormatter = new Intl.NumberFormat('zh-CN', {
  minimumFractionDigits: 2,
  maximumFractionDigits: 2
})

const quantityFormatter = new Intl.NumberFormat('zh-CN', {
  minimumFractionDigits: 4,
  maximumFractionDigits: 4
})

export function formatMoney(value: number | null | undefined): string {
  if (value === null || value === undefined) {
    return '—'
  }

  return moneyFormatter.format(value)
}

export function formatQuantity(value: number | null | undefined): string {
  if (value === null || value === undefined) {
    return '—'
  }

  return quantityFormatter.format(value)
}

export function formatSignedMoney(value: number | null | undefined): string {
  if (value === null || value === undefined) {
    return '—'
  }

  if (value === 0) {
    return formatMoney(0)
  }

  return `${value > 0 ? '+' : '−'}${formatMoney(Math.abs(value))}`
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
  return parseDecimal(value, 2)
}

export function parseQuantity(value: string): number | null {
  const quantity = parseDecimal(value, 4)

  if (quantity === null || quantity <= 0 || quantity > 1_000_000) {
    return null
  }

  return quantity
}

export function parseNonNegativeQuantity(value: string): number | null {
  const quantity = parseDecimal(value, 4)

  if (quantity === null || quantity > 1_000_000) {
    return null
  }

  return quantity
}

export function parsePrice(value: string): number | null {
  const price = parseDecimal(value, 4)

  if (price === null || price <= 0) {
    return null
  }

  return price
}

function parseDecimal(value: string, maximumFractionDigits: number): number | null {
  const normalized = value.trim().replaceAll(',', '')
  const pattern = new RegExp(`^\\d+(?:\\.\\d{1,${maximumFractionDigits}})?$`)

  if (!pattern.test(normalized)) {
    return null
  }

  const amount = Number(normalized)

  if (!Number.isFinite(amount) || amount > MaximumAmount) {
    return null
  }

  return amount
}
