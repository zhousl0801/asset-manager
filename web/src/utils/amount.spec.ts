import { describe, expect, it } from 'vitest'
import { applyAmountKey, evaluateAmountExpression } from './amount'

describe('applyAmountKey', () => {
  it('builds a decimal amount', () => {
    let expression = ''

    for (const key of ['1', '2', '3', '.', '4', '5'] as const) {
      expression = applyAmountKey(expression, key)
    }

    expect(expression).toBe('123.45')
  })

  it('keeps at most two decimals in the current term', () => {
    expect(applyAmountKey('1.23', '4')).toBe('1.23')
    expect(applyAmountKey('1.2', '4')).toBe('1.24')
  })

  it('replaces a trailing operator and strips a trailing dot', () => {
    expect(applyAmountKey('21+', '-')).toBe('21-')
    expect(applyAmountKey('21.', '+')).toBe('21+')
  })

  it('starts a new term with 0. when a dot follows an operator', () => {
    expect(applyAmountKey('21+', '.')).toBe('21+0.')
  })

  it('removes a leading zero before the next digit', () => {
    expect(applyAmountKey('0', '5')).toBe('5')
  })

  it('deletes the last character', () => {
    expect(applyAmountKey('123', 'back')).toBe('12')
  })
})

describe('evaluateAmountExpression', () => {
  it('evaluates addition and subtraction expressions', () => {
    expect(evaluateAmountExpression('21+35')).toBe(56)
    expect(evaluateAmountExpression('100-35.60')).toBe(64.4)
  })

  it('ignores a trailing operator or dot', () => {
    expect(evaluateAmountExpression('21+')).toBe(21)
    expect(evaluateAmountExpression('21.')).toBe(21)
  })

  it('rejects empty, zero, negative results and over-precise terms', () => {
    expect(evaluateAmountExpression('')).toBeNull()
    expect(evaluateAmountExpression('0')).toBeNull()
    expect(evaluateAmountExpression('10-20')).toBeNull()
    expect(evaluateAmountExpression('1.234')).toBeNull()
  })

  it('rounds the result to cents', () => {
    expect(evaluateAmountExpression('0.1+0.2')).toBe(0.3)
  })

  it('rejects amounts above the server limit', () => {
    expect(evaluateAmountExpression('1000000000.01')).toBeNull()
  })
})
