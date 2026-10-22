import { mount, flushPromises } from '@vue/test-utils'
import { beforeEach, expect, test, vi } from 'vitest'
import FundsView from './FundsView.vue'

const mocks = vi.hoisted(() => ({
  overview: vi.fn(),
  buy: vi.fn(),
  confirm: vi.fn(),
  createPosition: vi.fn(),
  sell: vi.fn(),
  confirmSell: vi.fn(),
  arriveSell: vi.fn(),
  quote: vi.fn()
}))

vi.mock('vue-router', () => ({ useRouter: () => ({ push: vi.fn(), replace: vi.fn() }) }))
vi.mock('../api/funds', () => ({
  fetchFundOverview: mocks.overview,
  buyFund: mocks.buy,
  confirmFundBuy: mocks.confirm,
  createFundPosition: mocks.createPosition,
  sellFund: mocks.sell,
  confirmFundSell: mocks.confirmSell,
  arriveFundSell: mocks.arriveSell,
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
  pendingArrivalAmount: 0,
  sellApplications: [] as unknown[],
  pendingArrivals: [] as unknown[],
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

test('redemption application records shares without collecting proceeds', async () => {
  mocks.sell.mockResolvedValue({
    position: overview.positions[0],
    cashBalance: 9700,
    activity: { id: 'sa1', type: 'sellApply', shares: 10, amount: 0, cashEffect: 0, fee: 0 }
  })
  const view = mount(FundsView, { attachTo: document.body })
  await flushPromises()

  const sellButton = view.findAll('button').find((button) => button.text() === '卖出')!
  await sellButton.trigger('click')
  await flushPromises()

  const inputs = Array.from(document.querySelectorAll<HTMLInputElement>('.sheet form input'))
  inputs[0]!.value = '10'
  inputs[0]!.dispatchEvent(new Event('input', { bubbles: true }))
  await flushPromises()

  document.querySelector('.sheet form')!.dispatchEvent(new Event('submit', { bubbles: true, cancelable: true }))
  await flushPromises()

  expect(mocks.sell).toHaveBeenCalledTimes(1)
  expect(mocks.sell.mock.calls[0]![0]).toMatchObject({ positionId: 'p1', shares: 10 })
  expect(mocks.sell.mock.calls[0]![0]).not.toHaveProperty('netAmount')
  view.unmount()
})

test('redemption confirmation submits shares, net value, fee and proceeds', async () => {
  const withApplication = structuredClone(overview)
  withApplication.pendingAmount = 0
  withApplication.pendingBuys = []
  withApplication.sellApplications = [{
    transactionId: 'sa1',
    positionId: 'p1',
    positionName: '纳指基金',
    shares: 10,
    businessDate: '2026-10-21',
    note: '赎回'
  }]
  mocks.overview.mockResolvedValue(withApplication)
  mocks.confirmSell.mockResolvedValue({
    position: overview.positions[0],
    cashBalance: 9700,
    activity: { id: 'sc1', type: 'sellConfirm', shares: 10, amount: 100, cashEffect: 0, fee: 0 }
  })
  const view = mount(FundsView, { attachTo: document.body })
  await flushPromises()

  const confirmButton = view.findAll('button').find((button) => button.text() === '确认赎回')!
  await confirmButton.trigger('click')
  await flushPromises()

  const inputs = Array.from(document.querySelectorAll<HTMLInputElement>('.sheet form input'))
  inputs[1]!.value = '10'
  inputs[1]!.dispatchEvent(new Event('input', { bubbles: true }))
  inputs[4]!.value = '100'
  inputs[4]!.dispatchEvent(new Event('input', { bubbles: true }))
  await flushPromises()

  document.querySelector('.sheet form')!.dispatchEvent(new Event('submit', { bubbles: true, cancelable: true }))
  await flushPromises()

  expect(mocks.confirmSell).toHaveBeenCalledTimes(1)
  expect(mocks.confirmSell.mock.calls[0]![0]).toBe('sa1')
  expect(mocks.confirmSell.mock.calls[0]![1]).toMatchObject({ shares: 10, netValue: 10, netAmount: 100, fee: 0 })
  view.unmount()
})

test('redemption arrival sends the pending amount to cash', async () => {
  const withArrival = structuredClone(overview)
  withArrival.pendingAmount = 0
  withArrival.pendingBuys = []
  withArrival.pendingArrivalAmount = 100
  withArrival.pendingArrivals = [{
    transactionId: 'sc1',
    positionId: 'p1',
    positionName: '纳指基金',
    shares: 10,
    amount: 100,
    businessDate: '2026-10-21',
    note: null
  }]
  mocks.overview.mockResolvedValue(withArrival)
  mocks.arriveSell.mockResolvedValue({
    position: overview.positions[0],
    cashBalance: 9800,
    activity: { id: 'ar1', type: 'sellArrival', shares: null, amount: 100, cashEffect: 100, fee: 0 }
  })
  const view = mount(FundsView, { attachTo: document.body })
  await flushPromises()

  const arrivalButton = view.findAll('button').find((button) => button.text() === '登记到账')!
  await arrivalButton.trigger('click')
  await flushPromises()

  document.querySelector('.sheet form')!.dispatchEvent(new Event('submit', { bubbles: true, cancelable: true }))
  await flushPromises()

  expect(mocks.arriveSell).toHaveBeenCalledTimes(1)
  expect(mocks.arriveSell.mock.calls[0]![0]).toBe('sc1')
  expect(mocks.arriveSell.mock.calls[0]![1]).toMatchObject({ amount: 100 })
  view.unmount()
})
