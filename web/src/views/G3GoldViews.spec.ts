import { mount, flushPromises } from '@vue/test-utils'
import { beforeEach, expect, test, vi } from 'vitest'
import GoldView from './GoldView.vue'
import { todayInBusinessTimeZone } from '../utils/format'

const today = todayInBusinessTimeZone()

const mocks = vi.hoisted(() => ({
  overview: vi.fn(),
  buy: vi.fn(),
  sell: vi.fn(),
  arrive: vi.fn(),
  holding: vi.fn(),
  refresh: vi.fn()
}))

vi.mock('vue-router', () => ({ useRouter: () => ({ push: vi.fn(), replace: vi.fn() }) }))
vi.mock('../api/gold', () => ({
  fetchGoldOverview: mocks.overview,
  buyGold: mocks.buy,
  sellGold: mocks.sell,
  arriveGoldSell: mocks.arrive,
  saveGoldHolding: mocks.holding,
  refreshGoldQuote: mocks.refresh
}))

const overview = {
  historyConfirmed: true,
  hasEffectiveQuote: true,
  version: 0,
  isConfigured: true,
  grams: 30,
  cost: 25600,
  quote: 900,
  quoteDate: today,
  value: 27000,
  averageCost: 853.33,
  holdingProfit: 1400,
  realizedProfit: 0,
  cashBalance: 10000,
  activities: [] as unknown[],
  pendingArrivalAmount: 0,
  pendingArrivals: [] as unknown[],
  lastQuoteAttemptAt: new Date().toISOString(),
  lastQuoteSource: 'icbc',
  lastQuoteError: null
}

beforeEach(() => {
  vi.clearAllMocks()
  mocks.overview.mockResolvedValue(structuredClone(overview))
  document.body.innerHTML = ''
})

test('gold sale uses grams and quote and records received cash by default', async () => {
  mocks.sell.mockResolvedValue({
    grams: 29,
    cost: 24746.67,
    quote: 900,
    quoteDate: today,
    value: 26100,
    cashBalance: 10898,
    activity: { id: 'gs1', type: 'sell', grams: 1, amount: 898, cashEffect: 898 }
  })
  const view = mount(GoldView, { attachTo: document.body })
  await flushPromises()

  const sellButton = view.findAll('button').find((button) => button.text() === '卖出')!
  await sellButton.trigger('click')
  await flushPromises()

  const inputs = Array.from(document.querySelectorAll<HTMLInputElement>('.sheet form input'))
  inputs[0]!.value = '1'
  inputs[0]!.dispatchEvent(new Event('input', { bubbles: true }))
  inputs[1]!.value = '898'
  inputs[1]!.dispatchEvent(new Event('input', { bubbles: true }))
  await flushPromises()

  expect(document.querySelector('.sheet form select')).toBeNull()
  expect(document.querySelector('.sheet')!.textContent).toContain('预计到账 ¥898.00')

  document.querySelector('.sheet form')!.dispatchEvent(new Event('submit', { bubbles: true, cancelable: true }))
  await flushPromises()

  expect(mocks.sell).toHaveBeenCalledTimes(1)
  expect(mocks.sell.mock.calls[0]![0]).toMatchObject({
    grams: 1,
    netAmount: 898
  })
  expect(mocks.sell.mock.calls[0]![0]).not.toHaveProperty('settlement')
  expect(mocks.sell.mock.calls[0]![0]).not.toHaveProperty('relatedCashAdjustmentId')
  expect(view.find('.notice').text()).toContain('当前现金 ¥10,898.00')
  view.unmount()
})

test('gold buy no longer asks for a quote or cash adjustment link', async () => {
  mocks.buy.mockResolvedValue({
    grams: 31,
    cost: 26500,
    quote: 900,
    quoteDate: today,
    value: 27900,
    cashBalance: 9100,
    activity: { id: 'gb1', type: 'buy', grams: 1, amount: 900, cashEffect: -900 }
  })
  const view = mount(GoldView, { attachTo: document.body })
  await flushPromises()

  expect(view.find('.quote-panel').text()).not.toContain('已实现盈亏')
  await view.findAll('button').find((button) => button.text() === '买入')!.trigger('click')
  await flushPromises()

  const inputs = Array.from(document.querySelectorAll<HTMLInputElement>('.sheet form input'))
  expect(inputs).toHaveLength(3)
  inputs[0]!.value = '1'
  inputs[0]!.dispatchEvent(new Event('input', { bubbles: true }))
  inputs[1]!.value = '900'
  inputs[1]!.dispatchEvent(new Event('input', { bubbles: true }))
  await flushPromises()

  expect(document.querySelector('.sheet')!.textContent).not.toContain('当前报价')
  expect(document.querySelector('.sheet')!.textContent).not.toContain('关联此前核对差额')
  document.querySelector('.sheet form')!.dispatchEvent(new Event('submit', { bubbles: true, cancelable: true }))
  await flushPromises()

  expect(mocks.buy.mock.calls[0]![0]).toMatchObject({ grams: 1, amount: 900, quote: 900, quoteDate: today })
  expect(mocks.buy.mock.calls[0]![0]).not.toHaveProperty('relatedCashAdjustmentId')
  view.unmount()
})

test('gold pending arrival is shown and can be received into cash', async () => {
  const withPending = structuredClone(overview)
  withPending.value = 26998
  withPending.pendingArrivalAmount = 898
  withPending.pendingArrivals = [{
    transactionId: 'gp1',
    grams: 1,
    amount: 898,
    businessDate: '2026-10-21',
    note: '卖出待到账'
  }]
  mocks.overview.mockResolvedValue(withPending)
  mocks.arrive.mockResolvedValue({
    grams: 29,
    cost: 24746.67,
    quote: 900,
    quoteDate: '2026-10-21',
    value: 26100,
    cashBalance: 10898,
    activity: { id: 'ga1', type: 'sellArrival', grams: null, amount: 898, cashEffect: 898 }
  })
  const view = mount(GoldView, { attachTo: document.body })
  await flushPromises()

  expect(view.find('.hero-meta').text()).toContain('含待到账 ¥898.00')
  expect(view.find('.pending-card').text()).toContain('¥898.00')

  const arrivalButton = view.findAll('button').find((button) => button.text() === '登记到账')!
  await arrivalButton.trigger('click')
  await flushPromises()

  document.querySelector('.sheet form')!.dispatchEvent(new Event('submit', { bubbles: true, cancelable: true }))
  await flushPromises()

  expect(mocks.arrive).toHaveBeenCalledTimes(1)
  expect(mocks.arrive.mock.calls[0]![0]).toBe('gp1')
  expect(mocks.arrive.mock.calls[0]![1]).toMatchObject({ amount: 898 })
  view.unmount()
})

test('opening the page refreshes a stale gold quote', async () => {
  const stale = structuredClone(overview)
  stale.quoteDate = '2026-10-01'
  stale.lastQuoteAttemptAt = '2026-10-01T02:00:00Z'
  mocks.overview
    .mockResolvedValueOnce(stale)
    .mockResolvedValue(structuredClone(overview))
  mocks.refresh.mockResolvedValue({
    updated: true,
    source: 'icbc',
    quote: 910,
    quoteDate: today,
    obtainedAt: new Date().toISOString(),
    lastAttemptAt: new Date().toISOString(),
    value: 27300,
    cashBalance: 10000
  })

  const view = mount(GoldView, { attachTo: document.body })
  await flushPromises()

  expect(mocks.refresh).toHaveBeenCalledTimes(1)
  expect(mocks.overview).toHaveBeenCalledTimes(2)
  view.unmount()
})

test('opening the page keeps a fresh gold quote without refreshing', async () => {
  const view = mount(GoldView, { attachTo: document.body })
  await flushPromises()

  expect(mocks.refresh).not.toHaveBeenCalled()
  expect(mocks.overview).toHaveBeenCalledTimes(1)
  view.unmount()
})

test('opening the page fetches a quote automatically when none exists', async () => {
  const empty = {
    ...structuredClone(overview),
    hasEffectiveQuote: false,
    isConfigured: false,
    grams: 0,
    cost: 0,
    quote: 0,
    quoteDate: null,
    lastQuoteAttemptAt: null
  }
  mocks.overview.mockResolvedValue(empty)
  mocks.refresh.mockResolvedValue({
    updated: true,
    source: 'icbc',
    quote: 900,
    quoteDate: today,
    obtainedAt: new Date().toISOString(),
    lastAttemptAt: new Date().toISOString(),
    value: 0,
    cashBalance: 10000
  })

  const view = mount(GoldView, { attachTo: document.body })
  await flushPromises()

  expect(mocks.refresh).toHaveBeenCalledTimes(1)
  view.unmount()
})
