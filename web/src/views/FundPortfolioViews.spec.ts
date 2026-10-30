import { mount, flushPromises } from '@vue/test-utils'
import { beforeEach, expect, test, vi } from 'vitest'
import FundsView from './FundsView.vue'

const mocks = vi.hoisted(() => ({
  overview: vi.fn(),
  createGroup: vi.fn(),
  renameGroup: vi.fn(),
  deleteGroup: vi.fn(),
  assignGroup: vi.fn(),
  plans: vi.fn(),
  search: vi.fn(),
  createPosition: vi.fn(),
  confirmBuy: vi.fn(),
  correctShares: vi.fn()
}))

vi.mock('vue-router', () => ({ useRoute: () => ({ params: {}, path: '/funds' }), useRouter: () => ({ push: vi.fn(), replace: vi.fn() }) }))
vi.mock('../api/funds', () => ({
  fetchFundOverview: mocks.overview,
  buyFund: vi.fn(),
  confirmFundBuy: mocks.confirmBuy,
  createFundPosition: mocks.createPosition,
  correctFundShares: mocks.correctShares,
  sellFund: vi.fn(),
  confirmFundSell: vi.fn(),
  arriveFundSell: vi.fn(),
  updateFundQuote: vi.fn(),
  refreshFundQuote: vi.fn(),
  searchFunds: mocks.search,
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
  groupName: '纳斯达克指数',
  dailyProfit: 12,
  dailyProfitRate: 1.01
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
  dailyProfit: 24,
  dailyProfitRate: 1.35,
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
      dailyProfit: 16,
      dailyProfitRate: 1.35,
      platforms: [{
        positionId: 'a', platform: '平台甲', name: '纳指基金', code: '000001', shareClass: 'A',
        shares: 100, cost: 1000, netValue: 12, netValueDate: '2026-10-20', value: 1200,
        holdingProfit: 200, realizedProfit: 0, hasEffectiveQuote: true, dailyProfit: 16, dailyProfitRate: 1.35
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
      dailyProfit: 8,
      dailyProfitRate: 1.35,
      platforms: [{
        positionId: 'c', platform: '平台乙', name: '纳指基金', code: '000002', shareClass: 'C',
        shares: 50, cost: 500, netValue: 12, netValueDate: '2026-10-20', value: 600,
        holdingProfit: 100, realizedProfit: 0, hasEffectiveQuote: true, dailyProfit: 8, dailyProfitRate: 1.35
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
  dailyProfit: -5,
  dailyProfitRate: -0.83,
  classes: [{
    shareClass: null,
    shares: 200,
    cost: 200,
    value: 300,
    holdingProfit: 100,
    realizedProfit: 0,
    netValue: 1.5,
    netValueDate: '2026-10-20',
    dailyProfit: -5,
    dailyProfitRate: -0.83,
    platforms: [{
      positionId: 'b', platform: '平台丙', name: '债券基金', code: '100001', shareClass: null,
      shares: 200, cost: 200, netValue: 1.5, netValueDate: '2026-10-20', value: 300,
      holdingProfit: 100, realizedProfit: 0, hasEffectiveQuote: true, dailyProfit: -5, dailyProfitRate: -0.83
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
    businessDate: '2026-10-21', tradeDate: '2026-10-21', confirmationDate: '2026-10-23', fee: 0, canCancel: false, note: '定投'
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
  mocks.search.mockResolvedValue([{
    code: '000834',
    name: '大成纳斯达克100ETF联接(QDII)A',
    baseName: '大成纳斯达克100ETF联接(QDII)',
    shareClass: 'A',
    fundType: '指数型-海外股票',
    isQdii: true,
    confirmationDelayDays: 2
  }])
  mocks.createPosition.mockResolvedValue({
    position: positionA,
    cashBalance: 5000,
    activity: { id: 'open1', type: 'opening', shares: 10, amount: 100, cashEffect: 0 }
  })
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
    activeCount: 2,
    nextDueDate: '2026-11-15',
    cashBalance: 5000,
    providentBalance: 0
  })
  document.body.innerHTML = ''
})

test('pending shortcut opens the transaction tab and expands pending items', async () => {
  const view = mount(FundsView, { attachTo: document.body })
  await flushPromises()

  await view.get('.pending-shortcut').trigger('click')
  await flushPromises()

  expect(view.find('.view-tabs button[aria-selected="true"]').text()).toBe('交易')
  expect(view.findAll('.activity-row.is-pending')).toHaveLength(1)
  expect(view.find('.activity-row.is-pending').text()).toContain('交易进行中')
  view.unmount()
})

test('holding profit, holding rate and realized profit share one color convention', async () => {
  const view = mount(FundsView)
  await flushPromises()

  const metrics = view.findAll('.hero-metrics > div')
  expect(metrics[0]!.find('b').text()).toBe('+400.00')
  expect(metrics[0]!.find('b').classes()).toContain('amount-gain')
  expect(metrics[1]!.find('b').text()).toBe('+23.53%')
  expect(metrics[1]!.find('b').classes()).toContain('amount-gain')
  expect(metrics[2]!.find('b').text()).toBe('0.00')
  expect(metrics[2]!.find('b').classes()).not.toContain('amount-gain')
  view.unmount()
})

test('switching group filters total, funds, pending items and activities consistently', async () => {
  const view = mount(FundsView, { attachTo: document.body })
  await flushPromises()

  expect(view.find('.hero-balance').text()).toContain('2,400.00')
  expect(view.findAll('.fund-card')).toHaveLength(2)
  expect(view.find('.pending-shortcut').exists()).toBe(true)
  expect(view.find('.pending-shortcut').text()).toContain('1 笔待处理')

  const groupPill = view.findAll('.group-pill').find((pill) => pill.text() === '纳斯达克指数')!
  await groupPill.trigger('click')
  await flushPromises()

  expect(view.find('.hero-balance').text()).toContain('1,800.00')
  expect(view.findAll('.fund-card')).toHaveLength(1)
  expect(view.findAll('.fund-card')[0]!.text()).toContain('纳指基金')
  expect(view.find('.pending-shortcut').exists()).toBe(false)

  await view.findAll('.view-tabs button').find((button) => button.text() === '交易')!.trigger('click')
  await flushPromises()
  expect(view.findAll('.activity-row.is-pending')).toHaveLength(0)
  expect(view.findAll('.activity-row')).toHaveLength(1)
  expect(view.find('.activity-row').text()).toContain('纳指基金')

  view.unmount()
})

test('a collapsed search keeps its scope visible and can be cleared', async () => {
  const view = mount(FundsView, { attachTo: document.body })
  await flushPromises()

  await view.get('button[aria-label="搜索与指标"]').trigger('click')
  await flushPromises()
  const input = document.querySelector<HTMLInputElement>('.portfolio-toolbar input')!
  input.value = '纳指'
  input.dispatchEvent(new Event('input', { bubbles: true }))
  await flushPromises()

  expect(view.get('.hero-balance').text()).toContain('1,800.00')
  expect(view.get('.hero-label').text()).toContain('搜索「纳指」')
  await view.get('.active-search button').trigger('click')
  expect(view.get('.hero-balance').text()).toContain('2,400.00')
  expect(view.find('.active-search').exists()).toBe(false)
  view.unmount()
})

test('expanded fund keeps A and C share classes separate with platform actions', async () => {
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
  expect(view.findAll('.fund-platform-row')).toHaveLength(0)
  expect(view.find('.holding-action-row').text()).toContain('平台甲')
  expect(view.find('.holding-action-row').text()).not.toContain('刷新净值')
  expect(view.find('.fund-detail').text()).toContain('按基金代码和类别匹配')
  expect(view.find('.fund-detail').text()).not.toContain('150.0000 份')
  expect(view.find('.fund-group-select').exists()).toBe(false)

  view.unmount()
})

test('group detail adds and removes assets through the fund family', async () => {
  const view = mount(FundsView, { attachTo: document.body })
  await flushPromises()

  await view.get('.group-manage').trigger('click')
  await flushPromises()

  const groupRow = [...document.querySelectorAll<HTMLButtonElement>('.sheet .group-manage-row')]
    .find((row) => row.textContent?.includes('纳斯达克指数'))!
  groupRow.click()
  await flushPromises()

  const assetRows = [...document.querySelectorAll<HTMLButtonElement>('.sheet .group-asset-row')]
  expect(assetRows).toHaveLength(2)
  expect(assetRows.find((row) => row.textContent?.includes('纳指基金'))!.classList.contains('selected')).toBe(true)
  const bondRow = assetRows.find((row) => row.textContent?.includes('债券基金'))!
  expect(bondRow.textContent).toContain('当前持仓')
  expect(bondRow.classList.contains('selected')).toBe(false)

  bondRow.click()
  await flushPromises()
  expect(mocks.assignGroup).toHaveBeenCalledWith('b', 'g1')

  const memberRow = [...document.querySelectorAll<HTMLButtonElement>('.sheet .group-asset-row')]
    .find((row) => row.textContent?.includes('纳指基金'))!
  memberRow.click()
  await flushPromises()
  expect(mocks.assignGroup).toHaveBeenCalledWith('a', null)

  view.unmount()
})

test('group management creates a new group from the sheet', async () => {
  const view = mount(FundsView, { attachTo: document.body })
  await flushPromises()

  await view.get('.group-manage').trigger('click')
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

  await view.get('button[aria-label="搜索与指标"]').trigger('click')
  await flushPromises()
  const costButton = [...document.querySelectorAll<HTMLButtonElement>('.metric-picker button')]
    .find((button) => button.textContent?.trim() === '持仓成本')!
  costButton.click()
  await flushPromises()

  expect(document.body.textContent).toContain('最多同时显示 3 项指标')
  expect(document.querySelectorAll('.metric-picker button.active')).toHaveLength(3)

  view.unmount()
})

test('opening a position searches by code and fills class and confirmation period automatically', async () => {
  const view = mount(FundsView, { attachTo: document.body })
  await flushPromises()

  await view.findAll('button').find((button) => button.text() === '录入持仓')!.trigger('click')
  await flushPromises()

  const search = document.querySelector<HTMLInputElement>('.sheet input[type="search"]')!
  search.value = '000834'
  search.dispatchEvent(new Event('input', { bubbles: true }))
  await new Promise(resolve => setTimeout(resolve, 350))
  await flushPromises()

  expect(mocks.search).toHaveBeenCalledWith('000834')
  const result = document.querySelector<HTMLButtonElement>('.sheet .fund-search-result')!
  expect(result.textContent).toContain('大成纳斯达克100ETF联接(QDII)A')
  result.click()
  await flushPromises()

  const sheetText = document.querySelector('.sheet')!.textContent ?? ''
  expect(sheetText).toContain('大成纳斯达克100ETF联接(QDII)A')
  expect(sheetText).toContain('000834')

  const shares = document.querySelector<HTMLInputElement>('.sheet input[placeholder="0.0000"]')!
  shares.value = '10'
  shares.dispatchEvent(new Event('input', { bubbles: true }))
  const cost = document.querySelector<HTMLInputElement>('.sheet input[placeholder="0.00"]')!
  cost.value = '100'
  cost.dispatchEvent(new Event('input', { bubbles: true }))
  await flushPromises()

  document.querySelector('.sheet form')!.dispatchEvent(new Event('submit', { bubbles: true, cancelable: true }))
  await flushPromises()

  expect(mocks.createPosition).toHaveBeenCalledWith(expect.objectContaining({
    name: '大成纳斯达克100ETF联接(QDII)',
    code: '000834',
    shareClass: 'A',
    platform: '券商直销',
    confirmationDelayDays: 2
  }))
  view.unmount()
})

test('opening a duplicate holding asks before updating or adding', async () => {
  const duplicate = structuredClone(overview)
  duplicate.positions = [{
    ...positionA,
    id: 'a',
    name: '大成纳斯达克100ETF联接(QDII)',
    code: '000834',
    shareClass: 'A',
    platform: '券商直销',
    shares: 100,
    cost: 1000
  }]
  duplicate.funds = []
  mocks.overview.mockResolvedValue(duplicate)
  mocks.correctShares.mockResolvedValue({
    position: duplicate.positions[0],
    cashBalance: 5000,
    activity: { id: 'corr1', type: 'correction', shares: 120, amount: 900, cashEffect: 0 }
  })

  const view = mount(FundsView, { attachTo: document.body })
  await flushPromises()
  await view.findAll('button').find((button) => button.text() === '录入持仓')!.trigger('click')
  await flushPromises()

  const search = document.querySelector<HTMLInputElement>('.sheet input[type="search"]')!
  search.value = '000834'
  search.dispatchEvent(new Event('input', { bubbles: true }))
  await new Promise(resolve => setTimeout(resolve, 350))
  await flushPromises()
  document.querySelector<HTMLButtonElement>('.sheet .fund-search-result')!.click()
  await flushPromises()

  const shares = document.querySelector<HTMLInputElement>('.sheet input[placeholder="0.0000"]')!
  shares.value = '120'
  shares.dispatchEvent(new Event('input', { bubbles: true }))
  const cost = document.querySelector<HTMLInputElement>('.sheet input[placeholder="0.00"]')!
  cost.value = '900'
  cost.dispatchEvent(new Event('input', { bubbles: true }))
  await flushPromises()

  document.querySelector('.sheet form')!.dispatchEvent(new Event('submit', { bubbles: true, cancelable: true }))
  await flushPromises()

  expect(mocks.createPosition).not.toHaveBeenCalled()
  expect(document.body.textContent).toContain('已有相同持仓')

  const update = [...document.querySelectorAll<HTMLButtonElement>('.duplicate-actions button')]
    .find((button) => button.textContent?.includes('更新已有持仓'))!
  update.click()
  await flushPromises()

  expect(mocks.correctShares).toHaveBeenCalledWith(expect.objectContaining({
    positionId: 'a',
    shares: 120,
    cost: 900
  }))
  view.unmount()
})

test('pending buy can be confirmed manually with shares, net value and fee', async () => {
  mocks.confirmBuy.mockResolvedValue({
    position: positionB,
    cashBalance: 4700,
    activity: { id: 'bc1', type: 'buyConfirmed', shares: 10, amount: 300, cashEffect: 0, fee: 2 }
  })

  const view = mount(FundsView, { attachTo: document.body })
  await flushPromises()
  await view.findAll('.view-tabs button').find((button) => button.text() === '交易')!.trigger('click')
  await flushPromises()

  const pendingRow = view.findAll('.activity-row')
    .find((row) => row.text().includes('债券基金') && row.text().includes('交易进行中'))!
  await pendingRow.trigger('click')
  await flushPromises()

  const field = (label: string) => [...document.querySelectorAll('.sheet label')]
    .find((item) => item.querySelector('span')?.textContent === label)!
    .querySelector('input')!
  const setValue = (label: string, value: string) => {
    const input = field(label)
    input.value = value
    input.dispatchEvent(new Event('input', { bubbles: true }))
  }

  setValue('确认份额', '10')
  setValue('成交净值', '30')
  setValue('申购费（元）', '2')
  await flushPromises()

  document.querySelector('.sheet form')!.dispatchEvent(new Event('submit', { bubbles: true, cancelable: true }))
  await flushPromises()

  expect(mocks.confirmBuy).toHaveBeenCalledWith('pb1', expect.objectContaining({
    shares: 10,
    netValue: 30,
    fee: 2
  }))
  view.unmount()
})

test('fund recurring view reads plans and filters them to the active group', async () => {
  const view = mount(FundsView, { attachTo: document.body })
  await flushPromises()

  await view.findAll('.view-tabs button').find((button) => button.text() === '定投')!.trigger('click')
  await flushPromises()
  expect(mocks.plans).toHaveBeenCalledTimes(1)
  expect(view.findAll('.fund-plan-row')).toHaveLength(2)

  await view.findAll('.group-pill').find((pill) => pill.text() === '纳斯达克指数')!.trigger('click')
  await flushPromises()
  expect(view.findAll('.fund-plan-row')).toHaveLength(1)
  expect(view.find('.fund-plan-row').text()).toContain('纳指定投')

  view.unmount()
})
