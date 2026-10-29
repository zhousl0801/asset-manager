import { mount, flushPromises } from '@vue/test-utils'
import { beforeEach, expect, test, vi } from 'vitest'
import FundReturnsPanel from './FundReturnsPanel.vue'

const mocks = vi.hoisted(() => ({
  returns: vi.fn(),
  day: vi.fn()
}))

vi.mock('../api/funds', () => ({
  fetchFundReturns: mocks.returns,
  fetchFundReturnDay: mocks.day
}))

const returns = {
  range: 'month',
  from: '2026-10-01',
  to: '2026-10-21',
  intervalFrom: '2026-10-10',
  openingAssetValue: 3000,
  closingAssetValue: 3450,
  netTransfer: 300,
  reconciliationDifference: 0,
  intervalProfit: 150,
  intervalRate: 5,
  holdingProfit: 300,
  holdingRate: 10,
  disclosureDays: 2,
  isComplete: true,
  points: [
    {
      date: '2026-10-10', fundValue: 3300, dailyProfit: 100, cumulativeProfit: 100,
      disclosedFunds: 1, totalFunds: 1, coveredPositions: 1, totalPositions: 1,
      hasDisclosure: true, isComplete: true, status: 'complete'
    },
    {
      date: '2026-10-11', fundValue: 3300, dailyProfit: null, cumulativeProfit: 100,
      disclosedFunds: 0, totalFunds: 1, coveredPositions: 0, totalPositions: 1,
      hasDisclosure: false, isComplete: false, status: 'notDisclosed'
    },
    {
      date: '2026-10-12', fundValue: 3450, dailyProfit: -20, cumulativeProfit: 80,
      disclosedFunds: 1, totalFunds: 2, coveredPositions: 1, totalPositions: 2,
      hasDisclosure: true, isComplete: false, status: 'partial'
    }
  ],
  funds: [],
  notes: ['未披露净值的日期不计当日收益。']
}

const day = {
  date: '2026-10-12',
  totalProfit: null,
  disclosedFunds: 1,
  totalFunds: 2,
  coveredPositions: 0,
  totalPositions: 1,
  isComplete: false,
  status: 'notDisclosed',
  positions: [{
    positionId: 'p1', name: '纳指基金', shareClass: 'A', platform: '平台一',
    value: 1200, dailyProfit: null, netValue: 12, netValueDate: '2026-10-10',
    hasDisclosure: false, isComplete: false, status: 'notDisclosed', note: '该日未披露净值。'
  }],
  notes: ['该日没有基金披露净值。']
}

beforeEach(() => {
  vi.clearAllMocks()
  mocks.returns.mockResolvedValue(structuredClone(returns))
  mocks.day.mockResolvedValue(structuredClone(day))
  document.body.innerHTML = ''
})

test('separates interval profit from holding profit and labels coverage', async () => {
  const view = mount(FundReturnsPanel, {
    props: { groupId: null, ungrouped: false },
    global: { stubs: { FundReturnChart: true } }
  })
  await flushPromises()

  const summary = view.find('.return-summary').text()
  expect(summary).toContain('区间盈亏')
  expect(summary).toContain('+150.00')
  expect(summary).toContain('持有收益')
  expect(summary).toContain('+300.00')
  expect(summary).toContain('区间收益率')
  expect(view.find('.return-meta').text()).toContain('净值披露 2 天')
  expect(view.find('.calendar-note').text()).toContain('不代表收益为 0')
  expect(mocks.returns).toHaveBeenCalledWith('month', null, false)
  view.unmount()
})

test('changing range refetches and keeps the selected month', async () => {
  const view = mount(FundReturnsPanel, {
    props: { groupId: 'g1', ungrouped: false },
    global: { stubs: { FundReturnChart: true } }
  })
  await flushPromises()

  await view.findAll('.range-tabs button').find((button) => button.text() === '近3月')!.trigger('click')
  await flushPromises()

  expect(mocks.returns).toHaveBeenLastCalledWith('quarter', 'g1', false)
  view.unmount()
})

test('calendar day shows undisclosed rows with a dash instead of zero', async () => {
  const view = mount(FundReturnsPanel, {
    props: { groupId: null, ungrouped: false },
    global: { stubs: { FundReturnChart: true } }
  })
  await flushPromises()

  const dayButton = view.findAll('.calendar-day')
    .find((button) => button.text().startsWith('12'))!
  await dayButton.trigger('click')
  await flushPromises()

  expect(mocks.day).toHaveBeenCalledWith('2026-10-12', null, false)
  const sheet = document.querySelector('.sheet')!
  expect(sheet.textContent).toContain('当日合计')
  expect(sheet.textContent).toContain('未披露')
  expect(sheet.textContent).toContain('纳指基金')
  expect(sheet.textContent).toContain('—')
  view.unmount()
})

test('calendar rounds large daily profit so values do not overlap', async () => {
  const large = structuredClone(returns)
  large.points = [{
    date: '2026-10-20', fundValue: 3300, dailyProfit: 2136.82, cumulativeProfit: 2136.82,
    disclosedFunds: 1, totalFunds: 1, coveredPositions: 1, totalPositions: 1,
    hasDisclosure: true, isComplete: true, status: 'complete'
  }]
  mocks.returns.mockResolvedValue(large)

  const view = mount(FundReturnsPanel, {
    props: { groupId: null, ungrouped: false },
    global: { stubs: { FundReturnChart: true } }
  })
  await flushPromises()

  const dayButton = view.findAll('.calendar-day')
    .find((button) => button.text().startsWith('20'))!
  expect(dayButton.text()).toContain('+2,137')
  expect(dayButton.text()).not.toContain('2,136.82')
  view.unmount()
})

test('group changes refetch returns with the new scope', async () => {
  const view = mount(FundReturnsPanel, {
    props: { groupId: null, ungrouped: false },
    global: { stubs: { FundReturnChart: true } }
  })
  await flushPromises()

  await view.setProps({ groupId: null, ungrouped: true })
  await flushPromises()

  expect(mocks.returns).toHaveBeenLastCalledWith('month', null, true)
  view.unmount()
})
