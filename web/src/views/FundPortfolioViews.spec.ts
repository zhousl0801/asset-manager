import { mount, flushPromises } from '@vue/test-utils'
import { beforeEach, expect, test, vi } from 'vitest'
import FundsView from './FundsView.vue'

const mocks = vi.hoisted(() => ({
  overview: vi.fn(),
  createGroup: vi.fn(),
  renameGroup: vi.fn(),
  deleteGroup: vi.fn(),
  assignGroup: vi.fn(),
  plans: vi.fn()
}))

vi.mock('vue-router', () => ({ useRouter: () => ({ push: vi.fn(), replace: vi.fn() }) }))
vi.mock('../api/funds', () => ({
  fetchFundOverview: mocks.overview,
  buyFund: vi.fn(),
  confirmFundBuy: vi.fn(),
  createFundPosition: vi.fn(),
  sellFund: vi.fn(),
  confirmFundSell: vi.fn(),
  arriveFundSell: vi.fn(),
  updateFundQuote: vi.fn(),
  refreshFundQuote: vi.fn(),
  createFundGroup: mocks.createGroup,
  renameFundGroup: mocks.renameGroup,
  deleteFundGroup: mocks.deleteGroup,
  assignFundGroup: mocks.assignGroup
}))
vi.mock('../api/plans', () => ({ fetchPlanOverview: mocks.plans }))

const positionA = {
  historyConfirmed: true,
  hasEffectiveQuote: true,
  id: 'a',
  name: '纳指基金',
  code: '000001',
  shareClass: 'A',
  platform: '平台甲',
  shares: 100,
  cost: 1000,
  netValue: 12,
  netValueDate: '2026-10-20',
  value: 1200,
  holdingProfit: 200,
  realizedProfit: 0,
  lastQuoteAttemptAt: null,
  lastQuoteSource: 'manual',
  lastQuoteError: null,
  groupId: 'g1',
  groupName: '纳斯达克指数'
}

const positionC = {
  ...positionA,
  id: 'c',
  code: '000002',
  shareClass: 'C',
  platform: '平台乙',
  shares: 50,
  cost: 500,
  value: 600,
  holdingProfit: 100
}

const positionB = {
  ...positionA,
  id: 'b',
  name: '债券基金',
  code: '100001',
  shareClass: null,
  platform: '平台丙',
  shares: 200,
  cost: 200,
  netValue: 1.5,
  value: 300,
  holdingProfit: 100,
  groupId: null,
  groupName: null
}

const fundA = {
  key: '纳指基金',
  name: '纳指基金',
  groupId: 'g1',
  groupName: '纳斯达克指数',
  value: 1800,
  cost: 1500,
  holdingProfit: 300,
  realizedProfit: 0,
  pendingBuyAmount: 0,
  pendingArrivalAmount: 0,
  assetValue: 1800,
  platformCount: 2,
  latestNetValueDate: '2026-10-20',
  historyConfirmed: true,
  hasEffectiveQuotes: true,
  classes: [
    {
      shareClass: 'A',
      shares: 100,
      cost: 1000,
      value: 1200,
      holdingProfit: 200,
      realizedProfit: 0,
      netValue: 12,
      netValueDate: '2026-10-20',
      platforms: [{
        positionId: 'a', platform: '平台甲', name: '纳指基金', code: '000001', shareClass: 'A',
        shares: 100, cost: 1000, netValue: 12, netValueDate: '2026-10-20', value: 1200,
        holdingProfit: 200, realizedProfit: 0, hasEffectiveQuote: true
      }]
    },
    {
      shareClass: 'C',
      shares: 50,
      cost: 500,
      value: 600,
      holdingProfit: 100,
      realizedProfit: 0,
      netValue: 12,
      netValueDate: '2026-10-20',
      platforms: [{
        positionId: 'c', platform: '平台乙', name: '纳指基金', code: '000002', shareClass: 'C',
        shares: 50, cost: 500, netValue: 12, netValueDate: '2026-10-20', value: 600,
        holdingProfit: 100, realizedProfit: 0, hasEffectiveQuote: true
      }]
    }
  ]
}

const fundB = {
  key: '债券基金',
  name: '债券基金',
  groupId: null,
  groupName: null,
  value: 300,
  cost: 200,
  holdingProfit: 100,
  realizedProfit: 0,
  pendingBuyAmount: 300,
  pendingArrivalAmount: 0,
  assetValue: 600,
  platformCount: 1,
  latestNetValueDate: '2026-10-20',
  historyConfirmed: true,
  hasEffectiveQuotes: true,
  classes: [{
    shareClass: null,
    shares: 200,
    cost: 200,
    value: 300,
    holdingProfit: 100,
    realizedProfit: 0,
    netValue: 1.5,
    netValueDate: '2026-10-20',
    platforms: [{
      positionId: 'b', platform: '平台丙', name: '债券基金', code: '100001', shareClass: null,
      shares: 200, cost: 200, netValue: 1.5, netValueDate: '2026-10-20', value: 300,
      holdingProfit: 100, realizedProfit: 0, hasEffectiveQuote: true
    }]
  }]
}

const overview = {
  historyConfirmed: true,
  hasEffectiveQuotes: true,
  totalValue: 2400,
  totalCost: 1700,
  holdingProfit: 400,
  totalRealizedProfit: 0,
  cashBalance: 5000,
  positions: [positionA, positionC, positionB],
  activities: [{
    id: 'act1', positionId: 'a', positionName: '纳指基金', type: 'buy', shares: null,
    amount: 100, cashEffect: -100, netValue: null, businessDate: '2026-10-20',
    note: null, createdAt: '2026-10-20T02:00:00Z', fee: 0, linkedTransactionId: null
  }],
  pendingAmount: 300,
  pendingBuys: [{
    transactionId: 'pb1', positionId: 'b', positionName: '债券基金', amount: 300,
    businessDate: '2026-10-21', note: '定投'
  }],
  pendingArrivalAmount: 0,
  sellApplications: [],
  pendingArrivals: [],
  groups: [{
    id: 'g1', name: '纳斯达克指数', sortOrder: 0, fundCount: 1, positionCount: 2, confirmedValue: 1800
  }],
  funds: [fundA, fundB]
}

beforeEach(() => {
  vi.clearAllMocks()
  localStorage.clear()
  mocks.overview.mockResolvedValue(structuredClone(overview))
  mocks.assignGroup.mockResolvedValue({ groupId: 'g1', positionIds: ['b'] })
  mocks.createGroup.mockResolvedValue({ id: 'g2', name: '美股科技', sortOrder: 1, fundCount: 0, positionCount: 0, confirmedValue: 0 })
  mocks.plans.mockResolvedValue({
    plans: [{
      id: 'plan1', type: 'fundSip', name: '纳指定投', amount: 300, cycle: 'monthly',
      dayOfMonth: 15, dayOfWeek: null, time: '09:00', startDate: '2026-10-01',
      generateFrom: '2026-10-01', isActive: true, fundPositionId: 'a', fundPositionName: '纳指基金',
      nextDueDate: '2026-11-15', version: 1
    }, {
      id: 'plan2', type: 'fundSip', name: '债券定投', amount: 100, cycle: 'monthly',
      dayOfMonth: 15, dayOfWeek: null, time: '09:00', startDate: '2026-10-01',
      generateFrom: '2026-10-01', isActive: true, fundPositionId: 'b', fundPositionName: '债券基金',
      nextDueDate: '2026-11-15', version: 1
    }],
    pendingOccurrences: [],
    recentOccurrences: [],
    activeCount: 2,
    nextDueDate: '2026-11-15',
    cashBalance: 5000,
    providentBalance: 0
  })
  document.body.innerHTML = ''
})

test('switching group filters total, funds, pending items and activities consistently', async () => {
  const view = mount(FundsView, { attachTo: document.body })
  await flushPromises()

  expect(view.find('.hero-balance').text()).toContain('2,400.00')
  expect(view.findAll('.fund-card')).toHaveLength(2)
  expect(view.findAll('.pending-card')).toHaveLength(1)

  const groupChip = view.findAll('.group-chips button').find((button) => button.text().includes('纳斯达克指数'))!
  await groupChip.trigger('click')
  await flushPromises()

  expect(view.find('.hero-balance').text()).toContain('1,800.00')
  expect(view.findAll('.fund-card')).toHaveLength(1)
  expect(view.findAll('.fund-card')[0]!.text()).toContain('纳指基金')
  expect(view.findAll('.pending-card')).toHaveLength(0)

  await view.findAll('.view-tabs button').find((button) => button.text() === '交易')!.trigger('click')
  await flushPromises()
  expect(view.findAll('.activity-row')).toHaveLength(1)
  expect(view.find('.activity-row').text()).toContain('纳指基金')

  view.unmount()
})

test('a collapsed search keeps its scope visible and can be cleared', async () => {
  const view = mount(FundsView, { attachTo: document.body })
  await flushPromises()
  await view.get('.portfolio-toolbar input').setValue('纳指')
  view.get<HTMLDetailsElement>('.portfolio-settings').element.open = false

  expect(view.get('.hero-balance').text()).toContain('1,800.00')
  expect(view.get('.hero-label').text()).toContain('搜索「纳指」')
  expect(view.get('.portfolio-settings summary').text()).toContain('已筛选：纳指')
  await view.get('.active-search button').trigger('click')
  expect(view.get('.hero-balance').text()).toContain('2,400.00')
  expect(view.find('.active-search').exists()).toBe(false)
  view.unmount()
})

test('expanded fund keeps A and C share classes separate and shows platform rows', async () => {
  const view = mount(FundsView, { attachTo: document.body })
  await flushPromises()

  await view.findAll('.fund-card > header')[0]!.trigger('click')
  await flushPromises()

  const classes = view.findAll('.fund-class')
  expect(classes).toHaveLength(2)
  expect(classes[0]!.text()).toContain('A 类')
  expect(classes[0]!.text()).toContain('100.0000 份')
  expect(classes[1]!.text()).toContain('C 类')
  expect(classes[1]!.text()).toContain('50.0000 份')
  expect(view.findAll('.fund-platform-row')).toHaveLength(2)
  expect(view.find('.fund-detail').text()).not.toContain('150.0000 份')

  view.unmount()
})

test('moving a fund assigns the group through the first platform position', async () => {
  const view = mount(FundsView, { attachTo: document.body })
  await flushPromises()

  const bondCard = view.findAll('.fund-card').find((card) => card.text().includes('债券基金'))!
  await bondCard.find('header').trigger('click')
  await flushPromises()

  const select = bondCard.find('.fund-group-select select')
  await select.setValue('g1')
  await flushPromises()

  expect(mocks.assignGroup).toHaveBeenCalledWith('b', 'g1')
  expect(mocks.overview).toHaveBeenCalledTimes(2)
  view.unmount()
})

test('group management creates a new group from the sheet', async () => {
  const view = mount(FundsView, { attachTo: document.body })
  await flushPromises()

  await view.findAll('button').find((button) => button.text() === '管理')!.trigger('click')
  await flushPromises()

  const input = document.querySelector<HTMLInputElement>('.sheet input')!
  input.value = '美股科技'
  input.dispatchEvent(new Event('input', { bubbles: true }))
  document.querySelector('.sheet form')!.dispatchEvent(new Event('submit', { bubbles: true, cancelable: true }))
  await flushPromises()

  expect(mocks.createGroup).toHaveBeenCalledWith('美股科技')
  view.unmount()
})

test('metric picker keeps at most three indicators', async () => {
  const view = mount(FundsView, { attachTo: document.body })
  await flushPromises()

  const costButton = view.findAll('.metric-picker button').find((button) => button.text() === '持仓成本')!
  await costButton.trigger('click')
  await flushPromises()

  expect(view.text()).toContain('最多同时显示 3 项指标')
  expect(view.findAll('.metric-picker button.active')).toHaveLength(3)

  view.unmount()
})

test('fund recurring view reads plans and filters them to the active group', async () => {
  const view = mount(FundsView, { attachTo: document.body })
  await flushPromises()

  await view.findAll('.view-tabs button').find((button) => button.text() === '定投')!.trigger('click')
  await flushPromises()
  expect(mocks.plans).toHaveBeenCalledTimes(1)
  expect(view.findAll('.fund-plan-row')).toHaveLength(2)

  await view.findAll('.group-chips button').find((button) => button.text().includes('纳斯达克指数'))!.trigger('click')
  await flushPromises()
  expect(view.findAll('.fund-plan-row')).toHaveLength(1)
  expect(view.find('.fund-plan-row').text()).toContain('纳指定投')

  view.unmount()
})
