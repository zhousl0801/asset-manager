import type { EntryPeriod } from '../types/entry'

export function shiftAnchor(anchor: string, period: EntryPeriod, direction: -1 | 1): string {
  const date = parseIsoDate(anchor)

  if (!date) {
    return anchor
  }

  if (period === 'day') {
    date.setUTCDate(date.getUTCDate() + direction)
  } else if (period === 'month') {
    date.setUTCMonth(date.getUTCMonth() + direction, 1)
  } else {
    date.setUTCFullYear(date.getUTCFullYear() + direction, 0, 1)
  }

  return toIsoDate(date)
}

export function periodStart(anchor: string, period: EntryPeriod): string {
  const date = parseIsoDate(anchor)

  if (!date) {
    return anchor
  }

  if (period === 'month') {
    date.setUTCDate(1)
  }

  if (period === 'year') {
    date.setUTCMonth(0, 1)
  }

  return toIsoDate(date)
}

export function formatPeriodLabel(anchor: string, period: EntryPeriod): string {
  const date = parseIsoDate(anchor)

  if (!date) {
    return anchor
  }

  const year = date.getUTCFullYear()
  const month = date.getUTCMonth() + 1

  if (period === 'year') {
    return `${year}年`
  }

  if (period === 'month') {
    return `${year}年${month}月`
  }

  return `${year}年${month}月${date.getUTCDate()}日`
}

function parseIsoDate(value: string): Date | null {
  const match = /^(\d{4})-(\d{2})-(\d{2})$/.exec(value)

  if (!match) {
    return null
  }

  return new Date(Date.UTC(Number(match[1]), Number(match[2]) - 1, Number(match[3])))
}

function toIsoDate(value: Date): string {
  return value.toISOString().slice(0, 10)
}
