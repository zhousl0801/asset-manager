import { flushPromises, mount } from '@vue/test-utils'
import { createPinia } from 'pinia'
import { beforeEach, expect, test, vi } from 'vitest'
import HomeView from './HomeView.vue'
import { todayInBusinessTimeZone } from '../utils/format'
import type { MonthChange } from '../types/ledger'
import type { AssetTrend } from '../types/trend'

const mocks = vi.hoisted(() => ({
  ledger: vi.fn(),
  trend: vi.fn(),
  change: vi.fn(),
  plans: vi.fn()
}))

vi.mock('../api/ledger', () => ({
  fetchLedger: mocks.ledger,
  fetchAssetTrend: mocks.trend,
  fetchMonthChange: mocks.change,
  updateAssetCoverage: vi.fn(),
  initializeLedger: vi.fn()
}))
vi.mock('../api/plans', () => ({ fetchPlanOverview: mocks.plans }))
vi.mock('vue-router', () => ({
  RouterLink: { props: ['to'], template: '<a><slot /></a>' },
  useRouter: () => ({ push: vi.fn(), replace: vi.fn() })
}))
vi.mock('../components/AssetTrendChart.vue', () => ({ default: { template: '<div class="chart-stub" />' } }))

const today = todayInBusinessTimeZone()

function shift(date: string, days: number): string {
  const value = new Date(`${date}T00:00:00Z`)
  value.setUTCDate(value.getUTCDate() + days)
  return value.toISOString().slice(0, 10)
}

function overview(startDate: string, overrides: Record<string, unknown> = {}) {
  return {
    id: 'ledger',
    currency: 'CNY',
    timeZone: 'Asia/Shanghai',
    startDate,
    asOfDate: today,
    isComplete: false,
    totalAssets: 355851.75,
    cash: { isConfigured: true, amount: 244685, note: '统一余额', notHeld: false },
    fund: { isConfigured: false, amount: null, note: '尚未录入', notHeld: false },
    providentFund: { isConfigured: true, amount: 100345, note: '本月暂无缴存', notHeld: false },
    gold: { isConfigured: true, amount: 10821.75, note: '持有 12.4948 克', notHeld: false },
    month: { income: 0, expense: 0, surplus: 0, hasRecords: false },
    fundNotHeld: false,
    providentFundNotHeld: false,
    goldNotHeld: false,
    coverageComplete: false,
    ...overrides
  }
}

function point(date: string, overrides: Record<string, unknown>) {
  return {
    date,
    totalAssets: null,
    knownTotalAssets: 0,
    cash: null,
    provident: null,
    fundValue: null,
    fundPending: 0,
    fundPendingArrival: 0,
    goldValue: null,
    goldPendingArrival: 0,
    isComplete: false,
    hasCash: false,
    hasProvident: false,
    hasFunds: false,
    hasGold: false,
    ...overrides
  }
}

function trend(startDate: string): AssetTrend {
  return {
    range: 'month',
    from: startDate,
    to: today,
    notes: [],
    points: [
      point(startDate, { cash: 244685, hasCash: true }),
      point(today, { cash: 244685, provident: 100345, goldValue: 10821.75, hasCash: true, hasProvident: true, hasGold: true })
    ]
  }
}

const monthChange: MonthChange = {
  from: today,
  to: today,
  totalChange: null,
  livingSurplus: 0,
  providentDeposit: 0,
  investmentProfit: null,
  reconciliationDifference: 0,
  isComplete: false,
  notes: [],
  assetChanges: [
    { assetType: 'cash', change: 0, isNew: false },
    { assetType: 'providentFund', change: null, isNew: true },
    { assetType: 'fund', change: null, isNew: false },
    { assetType: 'gold', change: null, isNew: true }
  ],
  comparableChange: null,
  comparableBase: null,
  addedRecords: 111166.75,
  marketValueChange: null,
  netDeposit: null,
  hasComparableData: false
}

beforeEach(() => {
  vi.clearAllMocks()
  mocks.plans.mockResolvedValue({ plans: [], pendingOccurrences: [] })
  mocks.change.mockResolvedValue(structuredClone(monthChange))
})

test('shows recorded assets, no comparable month change and the seven-day trend gate', async () => {
  const start = shift(today, -1)
  mocks.ledger.mockResolvedValue(overview(start))
  mocks.trend.mockResolvedValue(trend(start))

  const view = mount(HomeView, { global: { plugins: [createPinia()] } })
  await flushPromises()

  expect(view.find('.hero-label').text()).toContain('已记录资产')
  expect(view.find('.hero-change').text()).toContain('暂无可比数据')
  expect(view.find('.added-note').text()).toContain('111,166.75')
  expect(view.find('.hero-meta').text()).toContain('已记录 3 类')
  expect(view.find('.trend-panel h2').text()).toBe('资产趋势')
  expect(view.find('.trend-wait').text()).toContain('记录满 7 天后显示趋势')
  expect(view.find('.trend-wait').text()).toContain('已记录 1/7 天')
  expect(view.find('.distribution').text()).toContain('暂未录入')
  expect(view.find('.chart-stub').exists()).toBe(false)
  view.unmount()
})

test('draws the scoped trend once seven days are recorded', async () => {
  const start = shift(today, -30)
  mocks.ledger.mockResolvedValue(overview(start))
  mocks.trend.mockResolvedValue(trend(start))

  const view = mount(HomeView, { global: { plugins: [createPinia()] } })
  await flushPromises()

  expect(view.find('.chart-stub').exists()).toBe(true)
  expect(view.find('.chart-readout').text()).toContain('244,685.00')
  expect(view.find('.chart-note').exists()).toBe(false)
  expect(view.find('.trend-wait').exists()).toBe(false)
  view.unmount()
})

test('shows the same-scope change and percentage when a comparable baseline exists', async () => {
  const start = shift(today, -30)
  mocks.ledger.mockResolvedValue(overview(start))
  mocks.trend.mockResolvedValue(trend(start))
  mocks.change.mockResolvedValue({
    ...structuredClone(monthChange),
    hasComparableData: true,
    comparableChange: 1000,
    comparableBase: 20000,
    totalChange: 1000,
    marketValueChange: 600,
    netDeposit: 400
  })

  const view = mount(HomeView, { global: { plugins: [createPinia()] } })
  await flushPromises()

  expect(view.find('.hero-change').text()).toContain('+1,000.00')
  expect(view.find('.hero-change').text()).toContain('+5.00%')
  view.unmount()
})
