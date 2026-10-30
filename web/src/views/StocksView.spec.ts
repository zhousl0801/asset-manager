import { flushPromises, mount } from '@vue/test-utils'
import { beforeEach, expect, test, vi } from 'vitest'
import StocksView from './StocksView.vue'
import { todayInBusinessTimeZone } from '../utils/format'

const today = todayInBusinessTimeZone()

const mocks = vi.hoisted(() => ({
  overview: vi.fn(),
  search: vi.fn(),
  buy: vi.fn(),
  sell: vi.fn(),
  holding: vi.fn(),
  refresh: vi.fn(),
  refreshOne: vi.fn(),
  quote: vi.fn(),
  ledger: vi.fn()
}))

vi.mock('vue-router', () => ({ useRouter: () => ({ push: vi.fn(), replace: vi.fn() }) }))
vi.mock('../api/stocks', () => ({
  fetchStockOverview: mocks.overview,
  searchStocks: mocks.search,
  buyStock: mocks.buy,
  sellStock: mocks.sell,
  saveStockHolding: mocks.holding,
  refreshStockQuotes: mocks.refresh,
  refreshStockQuote: mocks.refreshOne,
  fetchStockQuote: mocks.quote
}))
vi.mock('../api/ledger', () => ({
  fetchLedger: mocks.ledger,
  updateAssetCoverage: vi.fn()
}))

const position = {
  id: 'stock-1',
  market: 'shanghai',
  code: '600519',
  name: '贵州茅台',
  currency: 'CNY',
  shares: 100,
  cost: 1000,
  averageCost: 10,
  price: 10.5,
  priceDate: today,
  previousClose: 10,
  previousCloseDate: today,
  value: 1050,
  holdingProfit: 50,
  realizedProfit: 0,
  todayProfit: 50,
  todayProfitComplete: true,
  historyConfirmed: true,
  hasEffectiveQuote: true,
  version: 1,
  lastQuoteAttemptAt: new Date().toISOString(),
  lastQuoteSource: 'eastmoney',
  lastQuoteError: null
}

const overview = {
  isConfigured: true,
  positionCount: 1,
  totalCost: 1000,
  totalValue: 1050,
  todayProfit: 50,
  todayProfitComplete: true,
  todayProfitDate: today,
  holdingProfit: 50,
  realizedProfit: 0,
  cashBalance: 9000,
  positions: [position],
  activities: [
    {
      id: 'a1',
      positionId: 'stock-1',
      positionName: '贵州茅台',
      type: 'buy',
      shares: 100,
      amount: 1000,
      cashEffect: -1000,
      fee: 0,
      price: 10,
      businessDate: today,
      note: null,
      createdAt: new Date().toISOString()
    }
  ],
  historyConfirmed: true,
  hasEffectiveQuotes: true
}

beforeEach(() => {
  vi.clearAllMocks()
  mocks.ledger.mockResolvedValue({ startDate: today, fundNotHeld: false, providentFundNotHeld: false, goldNotHeld: false, stockNotHeld: false })
  mocks.quote.mockResolvedValue({ market: 'shenzhen', code: '159915', price: 2.435, priceDate: today, previousClose: 2.5, source: 'eastmoney' })
  mocks.overview.mockResolvedValue(structuredClone(overview))
  mocks.refreshOne.mockResolvedValue({
    positionId: 'stock-1',
    name: '贵州茅台',
    code: '600519',
    market: 'shanghai',
    updated: false,
    source: 'eastmoney',
    price: 10.5,
    priceDate: today,
    previousClose: 10,
    previousCloseDate: today,
    attemptedAt: new Date().toISOString(),
    completedAt: new Date().toISOString(),
    value: 1050,
    cashBalance: 9000
  })
  mocks.refresh.mockResolvedValue({
    attempted: 1,
    succeeded: 1,
    failed: 0,
    updated: 0,
    items: [],
    errors: []
  })
  document.body.innerHTML = ''
})

test('shows stock positions and today profit', async () => {
  const view = mount(StocksView, { attachTo: document.body })
  await flushPromises()

  expect(view.text()).toContain('股票资产')
  expect(view.text()).toContain('贵州茅台')
  expect(view.text()).toContain('今日盈亏')
  expect(view.text()).toContain('+50.00')
  expect(view.text()).toContain('沪市')
  expect(mocks.refresh).toHaveBeenCalled()
  view.unmount()
})

test('buying an existing position sends market code and computed amount fields', async () => {
  mocks.buy.mockResolvedValue({
    position: { ...position, shares: 110, cost: 2001, value: 1155 },
    cashBalance: 7999,
    activity: { ...overview.activities[0], id: 'a2', shares: 10, amount: 1001, cashEffect: -1001 }
  })
  const view = mount(StocksView, { attachTo: document.body })
  await flushPromises()

  await view.find('.position-row').trigger('click')
  await flushPromises()
  const detailBuy = Array.from(document.querySelectorAll<HTMLButtonElement>('.sheet button'))
    .find((button) => button.textContent?.trim() === '买入')!
  detailBuy.click()
  await flushPromises()

  const inputs = Array.from(document.querySelectorAll<HTMLInputElement>('.sheet form input'))
  inputs[0]!.value = '10'
  inputs[0]!.dispatchEvent(new Event('input', { bubbles: true }))
  inputs[1]!.value = '10'
  inputs[1]!.dispatchEvent(new Event('input', { bubbles: true }))
  await flushPromises()

  expect(document.querySelector('.sheet')!.textContent).toContain('成交金额 ¥100.00')
  expect(document.querySelector('.sheet')!.textContent).not.toContain('费用')
  document.querySelector('.sheet form')!.dispatchEvent(new Event('submit', { bubbles: true, cancelable: true }))
  await flushPromises()

  expect(mocks.buy).toHaveBeenCalledTimes(1)
  expect(mocks.buy.mock.calls[0]![0]).toMatchObject({
    market: 'shanghai',
    code: '600519',
    shares: 10,
    price: 10
  })
  expect(mocks.buy.mock.calls[0]![0]).not.toHaveProperty('fee')
  expect(view.find('.notice').text()).toContain('当前现金 ¥7,999.00')
  view.unmount()
})

test('searching and selecting a stock fills the new buy form', async () => {
  mocks.search.mockResolvedValue([
    { market: 'shenzhen', code: '000001', name: '平安银行', currency: 'CNY', price: null, priceDate: null }
  ])
  mocks.buy.mockResolvedValue({
    position: { ...position, id: 'stock-2', market: 'shenzhen', code: '000001', name: '平安银行' },
    cashBalance: 8990,
    activity: { ...overview.activities[0], id: 'a3', positionId: 'stock-2', positionName: '平安银行' }
  })
  const view = mount(StocksView, { attachTo: document.body })
  await flushPromises()

  await view.findAll('button').find((button) => button.text() === '买入')!.trigger('click')
  await flushPromises()

  const searchInput = document.querySelector<HTMLInputElement>('.search-row input')!
  searchInput.value = '平安'
  searchInput.dispatchEvent(new Event('input', { bubbles: true }))
  await flushPromises()
  document.querySelector<HTMLButtonElement>('.search-button')!.click()
  await flushPromises()

  document.querySelector<HTMLButtonElement>('.search-result')!.click()
  await flushPromises()

  const inputs = Array.from(document.querySelectorAll<HTMLInputElement>('.sheet form input'))
  inputs[0]!.value = '100'
  inputs[0]!.dispatchEvent(new Event('input', { bubbles: true }))
  inputs[1]!.value = '10'
  inputs[1]!.dispatchEvent(new Event('input', { bubbles: true }))
  await flushPromises()

  document.querySelector('.sheet form')!.dispatchEvent(new Event('submit', { bubbles: true, cancelable: true }))
  await flushPromises()

  expect(mocks.buy.mock.calls[0]![0]).toMatchObject({ market: 'shenzhen', code: '000001', shares: 100 })
  view.unmount()
})

test('selling caps the quantity at the current holding', async () => {
  mocks.sell.mockResolvedValue({
    position: { ...position, shares: 40, cost: 400, value: 420 },
    cashBalance: 9630,
    activity: { ...overview.activities[0], id: 'a4', type: 'sell', shares: 60, amount: 630, cashEffect: 630 }
  })
  const view = mount(StocksView, { attachTo: document.body })
  await flushPromises()

  await view.find('.position-row').trigger('click')
  await flushPromises()
  const detailSell = Array.from(document.querySelectorAll<HTMLButtonElement>('.sheet button'))
    .find((button) => button.textContent?.trim() === '卖出')!
  detailSell.click()
  await flushPromises()

  const inputs = Array.from(document.querySelectorAll<HTMLInputElement>('.sheet form input'))
  inputs[0]!.value = '101'
  inputs[0]!.dispatchEvent(new Event('input', { bubbles: true }))
  inputs[1]!.value = '10.5'
  inputs[1]!.dispatchEvent(new Event('input', { bubbles: true }))
  await flushPromises()

  expect(document.querySelector<HTMLInputElement>('.sheet form input[placeholder*="最多"]')!.placeholder).toContain('最多 100 股')
  expect(document.querySelector<HTMLButtonElement>('.sheet .primary-button')!.disabled).toBe(true)
  view.unmount()
})

test('shows broker style price, cost and today change percent', async () => {
  const view = mount(StocksView, { attachTo: document.body })
  await flushPromises()

  const row = view.find('.position-row').text()

  expect(row).toContain('现价 10.50')
  expect(row).toContain('成本 ¥10.00')
  expect(row).toContain('+5.00%')
  expect(view.text()).toContain('可用现金 ¥9,000.00')
  view.unmount()
})

test('offers a single refresh quote entry', async () => {
  const view = mount(StocksView, { attachTo: document.body })
  await flushPromises()

  const refreshButtons = view.findAll('button').filter((button) => button.text().includes('刷新行情'))

  expect(refreshButtons).toHaveLength(1)
  expect(refreshButtons[0]!.classes()).toContain('action-secondary')
  view.unmount()
})

test('closed positions move into the folded section and stop counting as held', async () => {
  const closed = {
    ...position,
    id: 'stock-2',
    code: '000001',
    name: '平安银行',
    shares: 0,
    cost: 0,
    averageCost: null,
    value: 0,
    holdingProfit: 0,
    realizedProfit: 120,
    todayProfit: null
  }
  mocks.overview.mockResolvedValue({
    ...structuredClone(overview),
    positionCount: 2,
    positions: [structuredClone(position), closed]
  })
  const view = mount(StocksView, { attachTo: document.body })
  await flushPromises()

  expect(view.text()).toContain('持有 1 只')
  expect(view.text()).toContain('已清仓 1 只')
  expect(view.find('.closed-block').text()).toContain('平安银行')
  expect(view.findAll('.market-group .position-row')).toHaveLength(1)
  view.unmount()
})

test('holding sheet takes cost per share and picks up the latest quote automatically', async () => {
  mocks.search.mockResolvedValue([
    { market: 'shenzhen', code: '159915', name: '创业板ETF易方达', currency: 'CNY', price: null, priceDate: null }
  ])
  mocks.holding.mockResolvedValue({
    position: {
      ...position,
      id: 'stock-3',
      market: 'shenzhen',
      code: '159915',
      name: '创业板ETF易方达',
      shares: 10000,
      cost: 27720,
      averageCost: 2.772,
      price: 2.435
    },
    cashBalance: 9000,
    activity: { ...overview.activities[0], id: 'a5', positionId: 'stock-3', positionName: '创业板ETF易方达' }
  })
  mocks.overview.mockResolvedValue({
    ...structuredClone(overview),
    isConfigured: false,
    positionCount: 0,
    totalCost: 0,
    totalValue: 0,
    todayProfit: null,
    holdingProfit: 0,
    positions: [],
    activities: []
  })
  const view = mount(StocksView, { attachTo: document.body })
  await flushPromises()

  await view.findAll('button').find((button) => button.text() === '录入已有持仓')!.trigger('click')
  await flushPromises()

  const searchInput = document.querySelector<HTMLInputElement>('.search-row input')!
  searchInput.value = '159915'
  searchInput.dispatchEvent(new Event('input', { bubbles: true }))
  await flushPromises()
  document.querySelector<HTMLButtonElement>('.search-button')!.click()
  await flushPromises()
  document.querySelector<HTMLButtonElement>('.search-result')!.click()
  await flushPromises()

  expect(mocks.quote).toHaveBeenCalledWith('shenzhen', '159915')
  const sheetText = () => document.querySelector('.sheet')!.textContent ?? ''
  expect(sheetText()).toContain('现价')
  expect(sheetText()).toContain('2.44')
  expect(sheetText()).not.toContain('行情日期')
  expect(sheetText()).toContain('成本均价（元 / 股）')
  expect(sheetText()).toContain('起始持仓是当天快照')

  const inputs = Array.from(document.querySelectorAll<HTMLInputElement>('.sheet form input'))
  expect(inputs.filter((input) => input.type === 'date')).toHaveLength(1)
  inputs[0]!.value = '10000'
  inputs[0]!.dispatchEvent(new Event('input', { bubbles: true }))
  inputs[1]!.value = '2.772'
  inputs[1]!.dispatchEvent(new Event('input', { bubbles: true }))
  await flushPromises()

  expect(sheetText()).toContain('持仓成本合计 ¥27,720.00')

  document.querySelector('.sheet form')!.dispatchEvent(new Event('submit', { bubbles: true, cancelable: true }))
  await flushPromises()

  expect(mocks.holding.mock.calls[0]![0]).toMatchObject({
    market: 'shenzhen',
    code: '159915',
    shares: 10000,
    cost: 27720,
    price: 2.435
  })
  view.unmount()
})
