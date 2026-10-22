import { mount, flushPromises } from '@vue/test-utils'
import { beforeEach, expect, test, vi } from 'vitest'
import FundsView from './FundsView.vue'

const mocks = vi.hoisted(() => ({
  overview: vi.fn(),
  buy: vi.fn(),
  confirm: vi.fn(),
  createPosition: vi.fn(),
  sell: vi.fn(),
  quote: vi.fn()
}))

vi.mock('vue-router', () => ({ useRouter: () => ({ push: vi.fn(), replace: vi.fn() }) }))
vi.mock('../api/funds', () => ({
  fetchFundOverview: mocks.overview,
  buyFund: mocks.buy,
  confirmFundBuy: mocks.confirm,
  createFundPosition: mocks.createPosition,
  sellFund: mocks.sell,
  updateFundQuote: mocks.quote
}))

const overview = {
  historyConfirmed: true,
  hasEffectiveQuotes: true,
  totalValue: 1500,
  totalCost: 1000,
  holdingProfit: 200,
  totalRealizedProfit: 0,
  cashBalance: 9700,
  positions: [{
    historyConfirmed: true,
    hasEffectiveQuote: true,
    id: 'p1',
    name: '纳指基金',
    code: '000001',
    shareClass: 'A',
    platform: '示例平台',
    shares: 100,
    cost: 1000,
    netValue: 12,
    netValueDate: '2026-10-20',
    value: 1200,
    holdingProfit: 200,
    realizedProfit: 0
  }],
  activities: [],
  pendingAmount: 300,
  pendingBuys: [{
    transactionId: 'pb1',
    positionId: 'p1',
    positionName: '纳指基金',
    amount: 300,
    businessDate: '2026-10-21',
    note: '定投'
  }]
}

beforeEach(() => {
  vi.clearAllMocks()
  mocks.overview.mockResolvedValue(structuredClone(overview))
  document.body.innerHTML = ''
})

test('pending buy is shown inside fund assets and can be confirmed with shares and fee', async () => {
  mocks.confirm.mockResolvedValue({
    position: overview.positions[0],
    cashBalance: 9700,
    activity: { id: 'c1', type: 'buyConfirmed', shares: 10, amount: 300, cashEffect: 0, fee: 2 }
  })
  const view = mount(FundsView, { attachTo: document.body })
  await flushPromises()

  expect(view.find('.hero-meta').text()).toContain('含待确认 ¥300.00')
  expect(view.find('.pending-card').text()).toContain('纳指基金')
  expect(view.find('.pending-card').text()).toContain('¥300.00')

  await view.find('.confirm-button').trigger('click')
  await flushPromises()

  const inputs = Array.from(document.querySelectorAll<HTMLInputElement>('.sheet form input'))
  inputs[0]!.value = '10'
  inputs[0]!.dispatchEvent(new Event('input', { bubbles: true }))
  inputs[1]!.value = '30'
  inputs[1]!.dispatchEvent(new Event('input', { bubbles: true }))
  inputs[3]!.value = '2'
  inputs[3]!.dispatchEvent(new Event('input', { bubbles: true }))
  await flushPromises()

  document.querySelector('.sheet form')!.dispatchEvent(new Event('submit', { bubbles: true, cancelable: true }))
  await flushPromises()

  expect(mocks.confirm).toHaveBeenCalledTimes(1)
  expect(mocks.confirm.mock.calls[0]![0]).toBe('pb1')
  expect(mocks.confirm.mock.calls[0]![1]).toMatchObject({ shares: 10, netValue: 30, fee: 2 })
  expect(mocks.confirm.mock.calls[0]![1].requestId).toBeTruthy()
  expect(mocks.buy).not.toHaveBeenCalled()
  view.unmount()
})

test('buy only records the paid amount and defers shares to confirmation', async () => {
  mocks.buy.mockResolvedValue({
    position: overview.positions[0],
    cashBalance: 9700,
    activity: { id: 'pb2', type: 'buyPending', shares: null, amount: 300, cashEffect: -300 }
  })
  const view = mount(FundsView, { attachTo: document.body })
  await flushPromises()

  const buyButton = view.findAll('button').find((button) => button.text() === '买入')!
  await buyButton.trigger('click')
  await flushPromises()

  const inputs = Array.from(document.querySelectorAll<HTMLInputElement>('.sheet form input'))
  inputs[0]!.value = '300'
  inputs[0]!.dispatchEvent(new Event('input', { bubbles: true }))
  await flushPromises()

  document.querySelector('.sheet form')!.dispatchEvent(new Event('submit', { bubbles: true, cancelable: true }))
  await flushPromises()

  expect(mocks.buy).toHaveBeenCalledTimes(1)
  expect(mocks.buy.mock.calls[0]![0]).toMatchObject({ positionId: 'p1', amount: 300 })
  expect(mocks.buy.mock.calls[0]![0]).not.toHaveProperty('shares')
  view.unmount()
})
