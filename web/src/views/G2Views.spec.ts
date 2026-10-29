import { mount, flushPromises } from '@vue/test-utils'
import { beforeEach, expect, test, vi } from 'vitest'
import FundsView from './FundsView.vue'

const mocks = vi.hoisted(() => ({
  overview: vi.fn(),
  buy: vi.fn(),
  cancel: vi.fn(),
  createPosition: vi.fn(),
  sell: vi.fn(),
  confirmSell: vi.fn(),
  arriveSell: vi.fn(),
  quote: vi.fn(),
  refresh: vi.fn()
}))

vi.mock('vue-router', () => ({ useRoute: () => ({ params: {}, path: '/funds' }), useRouter: () => ({ push: vi.fn(), replace: vi.fn() }) }))
vi.mock('../api/funds', () => ({
  fetchFundOverview: mocks.overview,
  buyFund: mocks.buy,
  confirmFundBuy: vi.fn(),
  cancelFundPending: mocks.cancel,
  createFundPosition: mocks.createPosition,
  sellFund: mocks.sell,
  confirmFundSell: mocks.confirmSell,
  arriveFundSell: mocks.arriveSell,
  correctFundShares: mocks.quote,
  refreshFundQuote: mocks.refresh
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
    realizedProfit: 0,
    lastQuoteAttemptAt: null,
    lastQuoteSource: 'manual',
    lastQuoteError: null
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
    tradeDate: '2026-10-21',
    confirmationDate: '2026-10-23',
    fee: 0,
    canCancel: false,
    note: '定投'
  }]
}

beforeEach(() => {
  vi.clearAllMocks()
  mocks.overview.mockResolvedValue(structuredClone(overview))
  document.body.innerHTML = ''
})

async function showActivities(view: ReturnType<typeof mount>): Promise<void> {
  await view.findAll('.view-tabs button').find((button) => button.text() === '交易')!.trigger('click')
  await flushPromises()
}

test('pending buy shows trade and automatic confirmation dates and allows manual confirmation', async () => {
  const view = mount(FundsView, { attachTo: document.body })
  await flushPromises()
  await showActivities(view)
  expect(view.find('.hero-meta').text()).toContain('含待确认 ¥300.00')
  expect(view.find('.pending-card').text()).toContain('纳指基金')
  expect(view.find('.pending-card').text()).toContain('T 日 2026年10月21日')
  expect(view.find('.pending-card').text()).toContain('预计 2026年10月23日 按确认净值自动结转')
  expect(view.find('.pending-card .confirm-button').text()).toBe('登记确认份额')
  expect(view.find('.pending-card .secondary-button').exists()).toBe(false)
  view.unmount()
})

test('pending buy before cutoff can be cancelled with an idempotent request', async () => {
  const cancellable = structuredClone(overview)
  cancellable.pendingBuys[0]!.canCancel = true
  mocks.overview.mockResolvedValue(cancellable)
  mocks.cancel.mockResolvedValue({})
  const view = mount(FundsView, { attachTo: document.body })
  await flushPromises()
  await showActivities(view)
  await view.get('.pending-card .secondary-button').trigger('click')
  await flushPromises()
  expect(mocks.cancel).toHaveBeenCalledWith('pb1', expect.any(String))
  expect(mocks.overview).toHaveBeenCalledTimes(2)
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

test('expanded fund no longer exposes a manual quote refresh action', async () => {
  const view = mount(FundsView, { attachTo: document.body })
  await flushPromises()

  await view.get('.fund-card > header').trigger('click')
  await flushPromises()

  expect(view.findAll('.fund-card')[0]!.text()).not.toContain('刷新净值')
  expect(mocks.refresh).not.toHaveBeenCalled()
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
  await showActivities(view)

  const confirmButton = view.findAll('button').find((button) => button.text() === '登记实际结果')!
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

test('redemption confirmation can estimate net proceeds from shares, net value and fee', async () => {
  const withApplication = structuredClone(overview)
  withApplication.pendingAmount = 0
  withApplication.pendingBuys = []
  withApplication.sellApplications = [{
    transactionId: 'sa2',
    positionId: 'p1',
    positionName: '纳指基金',
    shares: 10,
    businessDate: '2026-10-21',
    note: null
  }]
  mocks.overview.mockResolvedValue(withApplication)
  mocks.confirmSell.mockResolvedValue({
    position: overview.positions[0],
    cashBalance: 9700,
    activity: { id: 'sc2', type: 'sellConfirm', shares: 10, amount: 118, cashEffect: 0, fee: 2 }
  })
  const view = mount(FundsView, { attachTo: document.body })
  await flushPromises()
  await showActivities(view)

  await view.findAll('button').find((button) => button.text() === '登记实际结果')!.trigger('click')
  await flushPromises()

  const field = (label: string) => [...document.querySelectorAll('.sheet label')]
    .find((item) => item.querySelector('span')?.textContent === label)!
    .querySelector('input')!
  const setValue = (label: string, value: string) => {
    const input = field(label)
    input.value = value
    input.dispatchEvent(new Event('input', { bubbles: true }))
  }

  setValue('成交净值', '12')
  setValue('费用（元）', '2')
  await flushPromises()

  const estimateButton = [...document.querySelectorAll<HTMLButtonElement>('.sheet button')]
    .find((button) => button.textContent?.includes('试算净到账'))!
  estimateButton.click()
  await flushPromises()

  expect(field('净到账金额（元）').value).toBe('118.00')

  document.querySelector('.sheet form')!.dispatchEvent(new Event('submit', { bubbles: true, cancelable: true }))
  await flushPromises()

  expect(mocks.confirmSell.mock.calls[0]![1]).toMatchObject({ shares: 10, netValue: 12, netAmount: 118, fee: 2 })
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
  await showActivities(view)

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
