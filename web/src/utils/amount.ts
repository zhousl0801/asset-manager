export const MaximumAmount = 1_000_000_000

export type AmountKey =
  | '0'
  | '1'
  | '2'
  | '3'
  | '4'
  | '5'
  | '6'
  | '7'
  | '8'
  | '9'
  | '.'
  | '+'
  | '-'
  | 'back'

export function applyAmountKey(expression: string, key: AmountKey): string {
  if (key === 'back') {
    return expression.slice(0, -1)
  }

  if (key === '+' || key === '-') {
    if (!expression) {
      return expression
    }

    let next = expression

    if (/[+-]$/.test(next)) {
      next = next.slice(0, -1)
    }

    if (next.endsWith('.')) {
      next = next.slice(0, -1)
    }

    return next ? `${next}${key}` : ''
  }

  if (key === '.') {
    const currentTerm = expression.split(/[+-]/).pop() ?? ''

    if (currentTerm.includes('.')) {
      return expression
    }

    return `${expression}${currentTerm ? '' : '0'}.`
  }

  const currentTerm = expression.split(/[+-]/).pop() ?? ''
  const decimalPart = currentTerm.split('.')[1]

  if (currentTerm.includes('.') && (decimalPart?.length ?? 0) >= 2) {
    return expression
  }

  let next = currentTerm === '0' ? expression.slice(0, -1) : expression

  if (next.length >= 24) {
    return expression
  }

  return `${next}${key}`
}

export function evaluateAmountExpression(expression: string): number | null {
  const normalized = expression.replace(/[+\-.]$/, '')

  if (!/^\d+(?:\.\d{1,2})?(?:[+-]\d+(?:\.\d{1,2})?)*$/.test(normalized)) {
    return null
  }

  const terms = normalized.match(/[+-]?\d+(?:\.\d{1,2})?/g)

  if (!terms) {
    return null
  }

  const total = Math.round(terms.reduce((sum, term) => sum + Number(term), 0) * 100) / 100

  if (!Number.isFinite(total) || total <= 0 || total > MaximumAmount) {
    return null
  }

  return total
}
